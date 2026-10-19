using System.Numerics;
using System.Text;

namespace Lordran.Tools.Formats;

/// <summary>
/// DS2 SOTFS "MovieSequencer Binary" (<c>scnNNNNNN.mqb</c> inside a demo's scnbnd; session 8, cutscene track; layout in
/// SCN-FORMAT.md). Our own codec because SoulsFormats' MQB loses two things vanilla files depend on for a byte-identical
/// rewrite: the order of a cut's events (it writes them timeline by timeline; vanilla stores them in creation order and
/// timelines point at them) and the header word at 0x74. Little-endian, 64-bit offsets ("long format", version 0xCA,
/// header size 0x28), UTF-16 fixed 0x40-byte names. Verified: all 35 vanilla MQBs rewrite byte-identical
/// (<c>scn-dump model\scn --roundtrip</c>).
/// </summary>
public sealed class Ds2Mqb
{
    public string Name = "";
    public float Framerate = 30;
    public int UnkX74;
    public List<Resource> Resources = [];
    public List<Cut> Cuts = [];
    public string ResourceDirectory = "";
    /// <summary>Unreferenced bytes after the last structure, kept as found (scn202130 ends with a stale second copy of
    /// its resource-path table and paths).</summary>
    public byte[] Tail = [];
    /// <summary>Unreferenced bytes between the last event-offset list and the resource-path table, kept as found
    /// (scn503810: 96 stale bytes).</summary>
    public byte[] Gap = [];

    public sealed class Resource
    {
        public string Name = "";
        public int ParentIndex = -1;
        /// <summary>Kind: 1 actor/emitter/light, 3 camera, 7 sound, 8 filter, 12 fade, 13 light id, 17 motion blur, 265
        /// cut offset (.sib), 266 Havok animation file (.HKT).</summary>
        public int Unk48;
        public List<Parameter> Parameters = [];
        public string? Path;
    }

    public sealed class Cut
    {
        public string Name = "";
        public int Unk44;
        public int Duration;
        /// <summary>The cut's events in file order (timelines refer to them by index).</summary>
        public List<Event> Events = [];
        public List<Timeline> Timelines = [];
        /// <summary>Unused timeline records after the real ones (scn503810: 12; each points its event list at the start
        /// of the timeline-parameter area, no events, no parameters).</summary>
        public int SpareTimelines;
    }

    public sealed class Timeline
    {
        public List<int> EventIndices = [];
        public List<Parameter> Parameters = [];
        public int Unk10 = -1;
    }

    public sealed class Event
    {
        public int Id, ResourceIndex, Unk08, StartFrame, Duration, Unk14, Unk18, Unk1C, Unk20, Unk28;
        public List<Parameter> Parameters = [];
        public List<Transform> Transforms = [];
    }

    public sealed class Transform
    {
        public float Frame;
        public Vector3 Translation, Unk10, Unk1C, Rotation, Unk34, Unk40, Scale = Vector3.One, Unk58, Unk64;
    }

    public sealed class Parameter
    {
        public string Name = "";
        /// <summary>1 bool, 2 s8, 3 u8, 4 s16, 6 s32, 7 u32, 8 f32, 10 string, 11 custom, 13 colour (3×u8), 17 int colour, 18 vector.</summary>
        public uint Type;
        public int MemberCount;
        /// <summary>The 4-byte value word as stored (scalars; for variable types the payload length).</summary>
        public byte[] Word = new byte[4];
        /// <summary>Variable types' payload (string, custom struct, colour, vector), as stored.</summary>
        public byte[] Payload = [];
        public List<Sequence> Sequences = [];

        /// <summary>Offset of the value inside the parameter record (sequences point there): scalars, strings and custom
        /// structs at the 4-byte word (0x48), colours and vectors at their payload (0x60).</summary>
        internal int ValueOffsetInRecord => Type is 13 or 17 or 18 ? 0x60 : 0x48;
        public int Int => BitConverter.ToInt32(Word);
        public float Float => BitConverter.ToSingle(Word);

        public static Parameter Custom(string name, byte[] payload) => new() { Name = name, Type = 11, Word = BitConverter.GetBytes(payload.Length), Payload = payload };
    }

    public sealed class Sequence
    {
        public uint ValueType;
        public int PointType;
        public int ValueIndex;
        public List<Point> Points = [];
    }

    public sealed class Point
    {
        public byte[] Value = new byte[4];
        public int Unk08;
        public float Unk10, Unk14;
    }

    // ------------------------------------------------------------------ read

    public static Ds2Mqb Read(byte[] b)
    {
        var r = new Reader(b);
        if (Encoding.ASCII.GetString(b, 0, 4) != "MQB ") throw new InvalidDataException("not an MQB");
        if (b[4] != 0 || (sbyte)b[6] != -1 || r.I32(8) != 0xCA || r.I32(12) != 0x28) throw new InvalidDataException("not a DS2 SOTFS MQB (little-endian, long format, version 0xCA)");
        var m = new Ds2Mqb();
        long pathsOffset = r.I64(0x10);
        m.Name = r.FixStr(0x28, 0x40);
        m.Framerate = r.F32(0x68);
        int resourceCount = r.I32(0x6C), cutCount = r.I32(0x70);
        m.UnkX74 = r.I32(0x74);
        r.Pos = 0x88;
        for (int i = 0; i < resourceCount; i++)
        {
            var res = new Resource { Name = r.FixStr(r.Pos, 0x40) };
            r.Pos += 0x40;
            res.ParentIndex = r.Next32();
            if (r.Next32() != i) throw new InvalidDataException($"resource {i}: index mismatch");
            res.Unk48 = r.Next32();
            int pc = r.Next32();
            for (int k = 0; k < pc; k++) res.Parameters.Add(r.Param());
            m.Resources.Add(res);
        }
        var timelineOffsets = new List<(Cut Cut, long Offset, int Count, Dictionary<long, int> EventAt)>();
        for (int c = 0; c < cutCount; c++)
        {
            var cut = new Cut { Name = r.FixStr(r.Pos, 0x40) };
            r.Pos += 0x40;
            int eventCount = r.Next32();
            cut.Unk44 = r.Next32();
            cut.Duration = r.Next32();
            r.Expect0();
            int timelineCount = r.Next32();
            r.Expect0();
            long tlOffset = r.Next64();
            var eventAt = new Dictionary<long, int>();
            for (int e = 0; e < eventCount; e++)
            {
                eventAt[r.Pos] = e;
                var ev = new Event
                {
                    Id = r.Next32(), ResourceIndex = r.Next32(), Unk08 = r.Next32(), StartFrame = r.Next32(), Duration = r.Next32(),
                    Unk14 = r.Next32(), Unk18 = r.Next32(), Unk1C = r.Next32(), Unk20 = r.Next32(),
                };
                int pc = r.Next32();
                ev.Unk28 = r.Next32();
                r.Expect0();
                for (int k = 0; k < pc; k++) ev.Parameters.Add(r.Param());
                r.Expect0();
                int tc = r.Next32();
                for (int z = 0; z < 6; z++) r.Expect0();
                for (int k = 0; k < tc; k++)
                {
                    var t = new Transform { Frame = r.NextF() };
                    Vector3 V() => new(r.NextF(), r.NextF(), r.NextF());
                    t.Translation = V(); t.Unk10 = V(); t.Unk1C = V(); t.Rotation = V(); t.Unk34 = V(); t.Unk40 = V(); t.Scale = V(); t.Unk58 = V(); t.Unk64 = V();
                    ev.Transforms.Add(t);
                }
                cut.Events.Add(ev);
            }
            timelineOffsets.Add((cut, tlOffset, timelineCount, eventAt));
            m.Cuts.Add(cut);
        }
        long knownEnd = r.Pos;
        foreach (var (cut, off, count, eventAt) in timelineOffsets)
            for (int t = 0; t < count; t++)
            {
                long at = off + t * 0x20;
                long evOffs = r.I64(at);
                int ec = r.I32(at + 8);
                long parOff = r.I64(at + 0x10);
                int pc = r.I32(at + 0x18);
                var tl = new Timeline { Unk10 = r.I32(at + 0x1C) };
                for (int e = 0; e < ec; e++) tl.EventIndices.Add(eventAt[r.I64(evOffs + 8 * e)]);
                knownEnd = Math.Max(knownEnd, Math.Max(at + 0x20, evOffs + 8 * ec));
                r.Pos = parOff;
                for (int k = 0; k < pc; k++) tl.Parameters.Add(r.Param());
                knownEnd = Math.Max(knownEnd, r.Pos);
                cut.Timelines.Add(tl);
            }
        if (pathsOffset > knownEnd) m.Gap = b[(int)knownEnd..(int)pathsOffset];
        // spare timeline records: between a cut's records and the next cut's (the last cut's: up to the parameter area)
        long paramArea = timelineOffsets.SelectMany(t => Enumerable.Range(0, t.Count).Select(k => r.I64(t.Offset + k * 0x20 + 0x10))).DefaultIfEmpty(0).Min();
        for (int c = 0; c < timelineOffsets.Count; c++)
        {
            long next = c + 1 < timelineOffsets.Count ? timelineOffsets[c + 1].Offset : paramArea;
            m.Cuts[c].SpareTimelines = (int)Math.Max(0, (next - timelineOffsets[c].Offset) / 0x20 - timelineOffsets[c].Count);
        }
        var pathOffsets = Enumerable.Range(0, resourceCount).Select(i => r.I64(pathsOffset + 8 * i)).ToArray();
        m.ResourceDirectory = r.Utf16(pathsOffset + 8 * resourceCount);
        long end = pathsOffset + 8 * resourceCount + 2 * (m.ResourceDirectory.Length + 1);
        for (int i = 0; i < resourceCount; i++)
            if (pathOffsets[i] != 0) { m.Resources[i].Path = r.Utf16(pathOffsets[i]); end = Math.Max(end, pathOffsets[i] + 2 * (m.Resources[i].Path!.Length + 1)); }
        end = Math.Max(end, r.SequenceEnd);
        m.Tail = b[(int)end..];
        return m;
    }

    sealed class Reader(byte[] b)
    {
        public long Pos;
        /// <summary>End of the furthest sequence or point read (the file's last structure when parameters animate).</summary>
        public long SequenceEnd;
        public int I32(long at) => BitConverter.ToInt32(b, (int)at);
        public long I64(long at) => BitConverter.ToInt64(b, (int)at);
        public float F32(long at) => BitConverter.ToSingle(b, (int)at);
        public int Next32() { int v = I32(Pos); Pos += 4; return v; }
        public long Next64() { long v = I64(Pos); Pos += 8; return v; }
        public float NextF() { float v = F32(Pos); Pos += 4; return v; }
        public void Expect0() { if (Next32() != 0) throw new InvalidDataException($"expected 0 at 0x{Pos - 4:X}"); }
        public string FixStr(long at, int len)
        {
            var s = Encoding.Unicode.GetString(b, (int)at, len);
            int z = s.IndexOf('\0');
            return z < 0 ? s : s[..z];
        }
        public string Utf16(long at)
        {
            int e = (int)at;
            while (b[e] != 0 || b[e + 1] != 0) e += 2;
            return Encoding.Unicode.GetString(b, (int)at, e - (int)at);
        }

        public Parameter Param()
        {
            long start = Pos;
            var p = new Parameter { Name = FixStr(Pos, 0x40) };
            Pos += 0x40;
            p.Type = (uint)Next32();
            p.MemberCount = Next32();
            p.Word = b[(int)Pos..(int)(Pos + 4)];
            Pos += 4;
            Expect0();
            int seqOffset = Next32(), seqCount = Next32();
            Expect0(); Expect0();
            if (p.Type is 10 or 11 or 13 or 17 or 18)
            {
                int len = BitConverter.ToInt32(p.Word);
                p.Payload = b[(int)Pos..(int)(Pos + len)];
                Pos += len;
            }
            for (int s = 0; s < seqCount; s++)
            {
                int at = seqOffset + 0x1C * s;
                if (I32(at) != 0x1C) throw new InvalidDataException("sequence size");
                var seq = new Sequence { ValueType = (uint)I32(at + 8), PointType = I32(at + 12) };
                int n = I32(at + 4), pointSize = I32(at + 16), points = I32(at + 20), valueAt = I32(at + 24);
                SequenceEnd = Math.Max(SequenceEnd, Math.Max(at + 0x1C, points + pointSize * n));
                seq.ValueIndex = valueAt - (int)(start + p.ValueOffsetInRecord);
                for (int k = 0; k < n; k++)
                {
                    int q = points + pointSize * k;
                    var pt = new Point { Value = b[q..(q + 4)], Unk08 = I32(q + 8) };
                    if (seq.PointType == 2) { pt.Unk10 = F32(q + 16); pt.Unk14 = F32(q + 20); }
                    seq.Points.Add(pt);
                }
                p.Sequences.Add(seq);
            }
            return p;
        }
    }

    // ------------------------------------------------------------------ write

    public byte[] Write()
    {
        var w = new Writer();
        w.Ascii("MQB ");
        w.Bytes([0, 0, 0xFF, 0]);
        w.I32(0xCA); w.I32(0x28);
        long pathsField = w.Reserve64();
        for (int i = 0; i < 4; i++) w.I32(0);
        w.FixStr(Name, 0x40);
        w.F32(Framerate);
        w.I32(Resources.Count); w.I32(Cuts.Count); w.I32(UnkX74);
        for (int i = 0; i < 4; i++) w.I32(0);
        var all = new List<(Parameter P, long ValueAt, long SeqField)>();
        for (int i = 0; i < Resources.Count; i++)
        {
            var r = Resources[i];
            w.FixStr(r.Name, 0x40);
            w.I32(r.ParentIndex); w.I32(i); w.I32(r.Unk48); w.I32(r.Parameters.Count);
            foreach (var p in r.Parameters) all.Add(w.Param(p));
        }
        var tlFields = new List<long>();
        var eventOffsets = new List<long[]>();
        foreach (var c in Cuts)
        {
            w.FixStr(c.Name, 0x40);
            w.I32(c.Events.Count); w.I32(c.Unk44); w.I32(c.Duration); w.I32(0);
            w.I32(c.Timelines.Count); w.I32(0);
            tlFields.Add(w.Reserve64());
            var offs = new long[c.Events.Count];
            for (int e = 0; e < c.Events.Count; e++)
            {
                var ev = c.Events[e];
                offs[e] = w.Pos;
                w.I32(ev.Id); w.I32(ev.ResourceIndex); w.I32(ev.Unk08); w.I32(ev.StartFrame); w.I32(ev.Duration);
                w.I32(ev.Unk14); w.I32(ev.Unk18); w.I32(ev.Unk1C); w.I32(ev.Unk20);
                w.I32(ev.Parameters.Count); w.I32(ev.Unk28); w.I32(0);
                foreach (var p in ev.Parameters) all.Add(w.Param(p));
                w.I32(0); w.I32(ev.Transforms.Count);
                for (int z = 0; z < 6; z++) w.I32(0);
                foreach (var t in ev.Transforms)
                {
                    w.F32(t.Frame);
                    foreach (var v in new[] { t.Translation, t.Unk10, t.Unk1C, t.Rotation, t.Unk34, t.Unk40, t.Scale, t.Unk58, t.Unk64 }) { w.F32(v.X); w.F32(v.Y); w.F32(v.Z); }
                }
            }
            eventOffsets.Add(offs);
        }
        var tlEventFields = new List<long[]>();
        var spareFields = new List<long>();
        var tlParamFields = new List<long[]>();
        for (int c = 0; c < Cuts.Count; c++)
        {
            w.Fill64(tlFields[c], w.Pos);
            var ef = new long[Cuts[c].Timelines.Count];
            var pf = new long[Cuts[c].Timelines.Count];
            for (int t = 0; t < Cuts[c].Timelines.Count; t++)
            {
                var tl = Cuts[c].Timelines[t];
                ef[t] = w.Reserve64(); w.I32(tl.EventIndices.Count); w.I32(0);
                pf[t] = w.Reserve64(); w.I32(tl.Parameters.Count); w.I32(tl.Unk10);
            }
            for (int t = 0; t < Cuts[c].SpareTimelines; t++) { spareFields.Add(w.Reserve64()); for (int z = 0; z < 6; z++) w.I32(0); }
            tlEventFields.Add(ef); tlParamFields.Add(pf);
        }
        foreach (var f in spareFields) w.Fill64(f, w.Pos);
        for (int c = 0; c < Cuts.Count; c++)
            for (int t = 0; t < Cuts[c].Timelines.Count; t++)
            {
                w.Fill64(tlParamFields[c][t], w.Pos);
                foreach (var p in Cuts[c].Timelines[t].Parameters) all.Add(w.Param(p));
            }
        for (int c = 0; c < Cuts.Count; c++)
            for (int t = 0; t < Cuts[c].Timelines.Count; t++)
            {
                w.Fill64(tlEventFields[c][t], w.Pos);
                foreach (int e in Cuts[c].Timelines[t].EventIndices) w.I64(eventOffsets[c][e]);
            }
        w.Bytes(Gap);
        w.Fill64(pathsField, w.Pos);
        var pathFields = Resources.Select(_ => w.Reserve64()).ToList();
        w.Utf16(ResourceDirectory);
        for (int i = 0; i < Resources.Count; i++)
        {
            if (Resources[i].Path == null) { w.Fill64(pathFields[i], 0); continue; }
            w.Fill64(pathFields[i], w.Pos);
            w.Utf16(Resources[i].Path!);
        }
        var pointFields = new List<(Sequence S, long Field)>();
        foreach (var (p, valueAt, seqField) in all)
        {
            if (p.Sequences.Count == 0) { w.Fill32(seqField, 0); continue; }
            w.Fill32(seqField, (int)w.Pos);
            foreach (var s in p.Sequences)
            {
                w.I32(0x1C); w.I32(s.Points.Count); w.I32((int)s.ValueType); w.I32(s.PointType);
                w.I32(s.PointType == 2 ? 0x18 : 0x10);
                pointFields.Add((s, w.Reserve32()));
                w.I32((int)valueAt + s.ValueIndex);
            }
        }
        foreach (var (s, field) in pointFields)
        {
            w.Fill32(field, (int)w.Pos);
            foreach (var pt in s.Points)
            {
                w.Bytes(pt.Value); w.I32(0); w.I32(pt.Unk08); w.I32(0);
                if (s.PointType == 2) { w.F32(pt.Unk10); w.F32(pt.Unk14); }
            }
        }
        w.Bytes(Tail);
        return w.ToArray();
    }

    sealed class Writer
    {
        readonly MemoryStream s = new();
        public long Pos => s.Position;
        public void Bytes(byte[] b) => s.Write(b);
        public void Ascii(string a) => Bytes(Encoding.ASCII.GetBytes(a));
        public void I32(int v) => Bytes(BitConverter.GetBytes(v));
        public void I64(long v) => Bytes(BitConverter.GetBytes(v));
        public void F32(float v) => Bytes(BitConverter.GetBytes(v));
        public long Reserve64() { long at = Pos; I64(0); return at; }
        public long Reserve32() { long at = Pos; I32(0); return at; }
        public void Fill64(long at, long v) { long keep = Pos; s.Position = at; I64(v); s.Position = keep; }
        public void Fill32(long at, int v) { long keep = Pos; s.Position = at; I32(v); s.Position = keep; }
        public void FixStr(string v, int len)
        {
            var b = new byte[len];
            Encoding.Unicode.GetBytes(v, 0, v.Length, b, 0);
            Bytes(b);
        }
        public void Utf16(string v) { Bytes(Encoding.Unicode.GetBytes(v)); Bytes([0, 0]); }

        public (Parameter, long, long) Param(Parameter p)
        {
            long start = Pos;
            FixStr(p.Name, 0x40);
            I32((int)p.Type); I32(p.MemberCount);
            Bytes(p.Word);
            I32(0);
            long seqField = Reserve32();
            I32(p.Sequences.Count); I32(0); I32(0);
            Bytes(p.Payload);
            return (p, start + p.ValueOffsetInRecord, seqField);
        }

        public byte[] ToArray() => s.ToArray();
    }
}
