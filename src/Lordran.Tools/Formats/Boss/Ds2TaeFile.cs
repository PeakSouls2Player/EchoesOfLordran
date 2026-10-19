using System.Buffers.Binary;

namespace Lordran.Tools.Formats.Boss;

/// <summary>
/// DS2 SOTFS TimeAct file (<c>timeact/chr/cNNNNNN_pl|sfx|snd.tae</c>, 64-bit, version 0x1000C) as a full model with a writer
/// (session 8, track boss). <see cref="Ds2Tae"/> edits vanilla bytes in place and can't add animations or events; the 1:1
/// Asylum Demon needs both (DS2 applies an attack's damage on TAE event 2200 "attack index k" = the action's DamageId0k /
/// the combo stage's AttackId: Smelter 65101010/1020/1030 carry 2200 with 0/1/2), so the file is re-serialised. Own reverse
/// engineering of the vanilla layout (no FromSoftware code); <c>tae-ds2-roundtrip</c> re-emits every vanilla DS2 TAE
/// byte-identically from the model.
/// <para><b>Layout</b>: header (copied, patched: 0x0C file size, 0x54 animation count, 0x58 animation table, 0x60 id-run
/// table, 0x70 count again, 0x78 first record); animation table {u64 id, u64 record} sorted by id; id-run table {u64 count,
/// u64 ptr} + {u32 first id, u32 last id, u64 table entry} per run of consecutive ids; records (0x30) {u64 event headers,
/// u64 event groups, u64 times, u64 file struct, i32 events, i32 groups, i32 times, i32 0}; then per animation, in table
/// order: file struct (0x30: u64 type 0, u64 ptr +0x10, u64 name ptr +0x30, u8 loop, u8 30, u16 0, i32 frames at 30 fps,
/// 16 zero bytes), an optional name (UTF-16 + NUL, 0-padded to 16), the time table (unique f32 values, ascending; 0-padded to 16), event headers {u64 start ptr, u64 end ptr,
/// u64 data ptr}, event data {u32 type, u32 (0; 1 on _snd 201 events), u64 ptr +0x10, params} each 0-padded to 16, groups {u64 count, u64 event-ptr
/// list, u64 group data, u64 0}, then per group its data {u32 group type, u32 0, u64 ptr +0x10, 16 zero bytes} and its
/// event pointers (u64, 0-padded to 16). Every vanilla event belongs to a group (87,043/87,043 animations); group type by
/// event type (survey of all 971 DS2 TAEs): 2200/2201/2202 → 1100, 2300/2301 → 1150, 280100 → 280000, 120x00 → 120000,
/// 11xxxx → 110000, 201/202 → 200, 2104/2105 → 2100, 290000-2 → 290000, 310000 → 310000.</para>
/// </summary>
public sealed class Ds2TaeFile
{
    public sealed class Event
    {
        public float Start, End;
        public int Type;
        /// <summary>u32 after the type (0 on most events; 1 on the _snd files' 201 sound events).</summary>
        public int Unk04;
        /// <summary>Parameter bytes (vanilla: everything up to the next event's data, incl. 0 padding).</summary>
        public byte[] Params = [];
        public int Group;
    }

    public sealed class Group
    {
        public int Type;
        public List<int> Events = [];
    }

    public sealed class Animation
    {
        public long Id;
        /// <summary>File struct bytes 0x18..0x30 (loop, 30, frames, zeros) and its type field (0).</summary>
        public byte[] FileTail = new byte[0x18];
        public long FileType;
        /// <summary>Optional name after the file struct (UTF-16 + NUL, 0-padded to 16; e.g. "a65_10_1010.sib" on the
        /// Smelter's route-2 variants), raw; empty on most animations (the name pointer then points at the next data).</summary>
        public byte[] NameBytes = [];
        public List<Event> Events = [];
        public List<Group> Groups = [];
        public int Frames
        {
            get => BinaryPrimitives.ReadInt32LittleEndian(FileTail.AsSpan(4));
            set => BinaryPrimitives.WriteInt32LittleEndian(FileTail.AsSpan(4), value);
        }
    }

    public byte[] Header = [];
    public List<Animation> Animations = [];

    static long Q(byte[] d, int o) => BinaryPrimitives.ReadInt64LittleEndian(d.AsSpan(o));
    static int I(byte[] d, int o) => BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(o));

    public static Ds2TaeFile Read(byte[] d)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(d) != 0x20454154 || I(d, 8) != 0x1000C || d[7] != 0xFF) throw new InvalidDataException("not a DS2 SOTFS TAE");
        var t = new Ds2TaeFile();
        int n = I(d, 0x54); int tab = (int)Q(d, 0x58);
        // 11 object TAEs hold no animation (table pointer 0, an empty id-run table at 0xE0): kept as they are
        if (n == 0) { t.Header = d; return t; }
        t.Header = d[..tab];
        // every data start, to cut raw event parameters
        var recs = Enumerable.Range(0, n).Select(i => (Id: Q(d, tab + 16 * i), R: (int)Q(d, tab + 16 * i + 8))).ToList();
        for (int ai = 0; ai < recs.Count; ai++)
        {
            var (id, r) = recs[ai];
            int pEv = (int)Q(d, r), pGr = (int)Q(d, r + 8), pT0 = (int)Q(d, r + 16), pF = (int)Q(d, r + 24);
            int ne = I(d, r + 32), ng = I(d, r + 36);
            int nameEnd = ne > 0 ? pT0 : ng > 0 ? pGr : ai + 1 < recs.Count ? (int)Q(d, recs[ai + 1].R + 24) : d.Length;
            var a = new Animation { Id = id, FileType = Q(d, pF), FileTail = d[(pF + 0x18)..(pF + 0x30)], NameBytes = d[(pF + 0x30)..nameEnd] };
            var dataPtrs = Enumerable.Range(0, ne).Select(k => (int)Q(d, pEv + 24 * k + 16)).ToList();
            var hdrToIndex = new Dictionary<long, int>();
            for (int k = 0; k < ne; k++)
            {
                int h = pEv + 24 * k;
                hdrToIndex[h] = k;
                int pd = dataPtrs[k];
                int next = k + 1 < ne ? dataPtrs[k + 1] : (ng > 0 ? pGr : pd + 0x10);
                a.Events.Add(new Event
                {
                    Start = BitConverter.ToSingle(d, (int)Q(d, h)), End = BitConverter.ToSingle(d, (int)Q(d, h + 8)),
                    Type = I(d, pd), Unk04 = I(d, pd + 4), Params = d[((int)Q(d, pd + 8))..next],
                });
            }
            for (int k = 0; k < ng; k++)
            {
                int g = pGr + 32 * k;
                int cnt = (int)Q(d, g), pIdx = (int)Q(d, g + 8), pDat = (int)Q(d, g + 16);
                var gr = new Group { Type = pDat != 0 ? I(d, pDat) : 0 };
                for (int j = 0; j < cnt; j++) { int ev = hdrToIndex[Q(d, pIdx + 8 * j)]; gr.Events.Add(ev); a.Events[ev].Group = k; }
                a.Groups.Add(gr);
            }
            t.Animations.Add(a);
        }
        return t;
    }

    /// <summary>Group type of an event type (survey of all vanilla DS2 TAEs, see the class comment).</summary>
    public static int GroupTypeOf(int eventType) => eventType switch
    {
        2200 or 2201 or 2202 => 1100,
        2300 or 2301 => 1150,
        201 or 202 => 200,
        2104 or 2105 => 2100,
        2401 => 2200,
        2600 => 2400,
        2501 => 2300,
        1505 => 1500,
        >= 100000 => eventType / 10000 * 10000,
        _ => eventType,
    };

    public byte[] Write()
    {
        if (Animations.Count == 0) return (byte[])Header.Clone();
        var o = new List<byte>(Header);
        void Pad16() { while (o.Count % 16 != 0) o.Add(0); }
        void U64(long v) { for (int i = 0; i < 8; i++) o.Add((byte)(v >> (8 * i))); }
        void U32(int v) { for (int i = 0; i < 4; i++) o.Add((byte)(v >> (8 * i))); }
        void Put64(int at, long v) { for (int i = 0; i < 8; i++) o[at + i] = (byte)(v >> (8 * i)); }
        void Put32(int at, int v) { for (int i = 0; i < 4; i++) o[at + i] = (byte)(v >> (8 * i)); }
        int n = Animations.Count;
        var anims = Animations.OrderBy(a => a.Id).ToList();
        int tab = o.Count;
        foreach (var a in anims) { U64(a.Id); U64(0); }
        // id runs
        var runs = new List<(int First, int Last, int Index)>();
        for (int i = 0; i < n; i++)
        {
            if (runs.Count > 0 && anims[i].Id == anims[runs[^1].Last].Id + 1) runs[^1] = (runs[^1].First, i, runs[^1].Index);
            else runs.Add((i, i, i));
        }
        int runTab = o.Count;
        U64(runs.Count); U64(runTab + 16);
        foreach (var (f, l, idx) in runs) { U32((int)anims[f].Id); U32((int)anims[l].Id); U64(tab + 16 * idx); }
        int recs = o.Count;
        for (int i = 0; i < n; i++) o.AddRange(new byte[0x30]);
        for (int i = 0; i < n; i++)
        {
            var a = anims[i];
            int r = recs + 0x30 * i;
            Put64(tab + 16 * i + 8, r);
            int pF = o.Count;
            U64(a.FileType); U64(pF + 0x10); U64(pF + 0x30); o.AddRange(a.FileTail);
            o.AddRange(a.NameBytes);
            Put64(r + 24, pF);
            if (a.Events.Count == 0 && a.Groups.Count == 0) continue;
            // time table: unique values ascending (no table without events)
            var times = a.Events.SelectMany(e => new[] { e.Start, e.End }).Distinct().OrderBy(x => x).ToList();
            int pT = 0, pEv = 0;
            if (a.Events.Count > 0)
            {
                pT = o.Count;
                foreach (var x in times) U32(BitConverter.SingleToInt32Bits(x));
                Pad16();
                pEv = o.Count;
                o.AddRange(new byte[24 * a.Events.Count]);
                for (int k = 0; k < a.Events.Count; k++)
                {
                    var e = a.Events[k];
                    int at = o.Count;
                    U32(e.Type); U32(e.Unk04); U64(o.Count + 8);
                    o.AddRange(e.Params);
                    Pad16();
                    Put64(pEv + 24 * k, pT + 4 * times.IndexOf(e.Start));
                    Put64(pEv + 24 * k + 8, pT + 4 * times.IndexOf(e.End));
                    Put64(pEv + 24 * k + 16, at);
                }
            }
            int pGr = o.Count;
            o.AddRange(new byte[32 * a.Groups.Count]);
            for (int g = 0; g < a.Groups.Count; g++)
            {
                var gr = a.Groups[g];
                int pDat = o.Count;
                U32(gr.Type); U32(0); U64(pDat + 0x10); o.AddRange(new byte[16]);
                int pIdx = o.Count;
                foreach (int ev in gr.Events) U64(pEv + 24 * ev);
                Pad16();
                Put64(pGr + 32 * g, gr.Events.Count); Put64(pGr + 32 * g + 8, pIdx); Put64(pGr + 32 * g + 16, pDat);
            }
            Put64(r, pEv); Put64(r + 8, a.Groups.Count > 0 ? pGr : 0); Put64(r + 16, pT);
            Put32(r + 32, a.Events.Count); Put32(r + 36, a.Groups.Count); Put32(r + 40, times.Count);
        }
        var b = o.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0x0C), b.Length);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0x54), n);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(0x58), tab);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(0x60), runTab);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(0x70), n);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(0x78), recs);
        return b;
    }

    public Animation? Get(long id) => Animations.FirstOrDefault(a => a.Id == id);

    /// <summary>Add an event (params = the type's parameter bytes); it joins the animation's group of its group type
    /// (created if missing).</summary>
    public static void AddEvent(Animation a, int type, float start, float end, byte[] param)
    {
        int gt = GroupTypeOf(type);
        int g = a.Groups.FindIndex(x => x.Type == gt);
        if (g < 0) { a.Groups.Add(new Group { Type = gt }); g = a.Groups.Count - 1; }
        a.Events.Add(new Event { Type = type, Start = start, End = end, Params = param, Group = g });
        a.Groups[g].Events.Add(a.Events.Count - 1);
    }

    /// <summary>Drop events matching <paramref name="drop"/> (group lists renumbered, empty groups removed).</summary>
    public static void RemoveEvents(Animation a, Func<Event, bool> drop)
    {
        var keep = a.Events.Select((e, i) => (e, i)).Where(t => !drop(t.e)).ToList();
        var map = keep.Select((t, j) => (t.i, j)).ToDictionary(x => x.i, x => x.j);
        foreach (var g in a.Groups) g.Events = g.Events.Where(map.ContainsKey).Select(i => map[i]).ToList();
        var groups = a.Groups.Where(g => g.Events.Count > 0).ToList();
        a.Events = keep.Select(t => t.e).ToList();
        a.Groups = groups;
        for (int g = 0; g < groups.Count; g++) foreach (int e in groups[g].Events) a.Events[e].Group = g;
    }
}
