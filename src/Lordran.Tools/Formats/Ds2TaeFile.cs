using System.Buffers.Binary;

namespace Lordran.Tools.Formats;

/// <summary>
/// DS2 SOTFS TimeAct re-serialiser (session 8, track enemies step 1): parses a vanilla <c>timeact/chr/*.tae</c> into its
/// blocks and writes it back byte for byte (<c>tae-write-roundtrip</c>: all 696 DS2 chr TAEs), so animations can be added
/// (every new DS1 attack clip needs a TAE animation of its own: hit 2200 / bullet 2300 / turn-rate 280100 events). The
/// in-place editor <see cref="Ds2Tae"/> cannot add records; SoulsFormats' writer gives another layout (0/696 identical).
/// <para><b>Layout</b> (64-bit, own survey validate\tae_layout.py on all 696 files): header 0xC0 (u32 file size at 0x0C, u32
/// animation count at 0x54, u64 table at 0x58, u64 group header at 0x60, u64 count again at 0x70, u64 first record at 0x78,
/// u64 skeleton / sib name pointers at 0xB0 / 0xB8 = the table start); animation table {u64 id, u64 record} sorted by id;
/// group header {u64 count, u64 ptr} + groups {i32 first id, i32 last id, u64 table entry} over runs of consecutive ids;
/// records (0x30 each) {u64 event headers, u64 event groups, u64 times, u64 anim file, i32 events, i32 groups, i32 times,
/// i32 0}; then per animation, in table order, its data blob: anim file struct (0x30: u32 type, u32, u64 â†’ +0x10, u64 name
/// â†’ +0x30 (empty), u8 loop, u8 30, u8, u8, i32 length in frames at 30 fps, 16 zero bytes), times (sorted unique f32,
/// padded to 16), event headers {u64 start-time ptr, u64 end-time ptr, u64 data ptr} (padded to 16), event data blocks {u32
/// type, u32 0, u64 â†’ params, params} (each padded to 16), event groups {u64 count, u64 â†’ event list, u64 â†’ group data,
/// u64 0}, then per group its data block {u32 group type, u32 0, u64 â†’ 16 zero bytes} and its event-header list (padded
/// to 16). Every event is in exactly one group, keyed by event type (2200 â†’ 1100, 2300 â†’ 1150, 280100 â†’ 280000, 120200 â†’
/// 120000, 110000 â†’ 110000, 2105 â†’ 2100: all 696 files); groups are ordered by group type, events by start time.
/// All pointers are file-absolute.</para>
/// </summary>
public sealed class Ds2TaeFile
{
    public byte[] Header = [];
    public List<Anim> Anims = [];

    public sealed class Anim
    {
        public long Id;
        public byte[] Record = new byte[0x30];
        public byte[] Blob = [];
        public long BlobStart;           // file offset of the blob when parsed
        public List<int> BlobPtrs = [];  // offsets inside the blob of u64 file-absolute pointers into the blob
        public override string ToString() => $"anim {Id} ({Blob.Length} bytes)";
    }

    static long L(byte[] b, long o) => BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan((int)o));
    static int I(byte[] b, long o) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan((int)o));

    public static Ds2TaeFile Read(byte[] b)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(b) != 0x20454154) throw new InvalidDataException("not a TAE");
        var t = new Ds2TaeFile { Header = b[..0xC0] };
        int n = I(b, 0x54); long tab = L(b, 0x58);
        var recs = new List<(long Id, long Rec)>();
        for (int i = 0; i < n; i++) recs.Add((L(b, tab + 16 * i), L(b, tab + 16 * i + 8)));
        // blobs in table order: [anim file pointer of k, anim file pointer of k+1)
        var starts = recs.Select(r => L(b, r.Rec + 24)).ToList();
        for (int k = 0; k < n; k++)
        {
            long rec = recs[k].Rec, s = starts[k];
            long e = k + 1 < n ? starts[k + 1] : b.Length;
            if (s == 0 || e < s) throw new InvalidDataException($"TAE anim {recs[k].Id}: unexpected layout (file struct 0x{s:X}, next 0x{e:X})");
            var a = new Anim { Id = recs[k].Id, Record = b[(int)rec..(int)(rec + 0x30)], Blob = b[(int)s..(int)e], BlobStart = s };
            // pointer fields inside the blob
            void Ptr(long abs) { if (abs < s || abs + 8 > e) throw new InvalidDataException($"TAE anim {a.Id}: pointer field outside its blob"); a.BlobPtrs.Add((int)(abs - s)); }
            bool In(long v) => v >= s && v <= e;
            // anim file struct: +8 inner, inner +0 name (both point into the blob or just past the struct)
            if (L(b, s + 8) != 0) { Ptr(s + 8); long inner = L(b, s + 8); if (I(b, s) == 0 && L(b, inner) != 0) Ptr(inner); }   // standard header: inner +0 = name pointer
            long pEv = L(b, rec), pGr = L(b, rec + 8);
            int nE = I(b, rec + 32), nG = I(b, rec + 36);
            for (int k2 = 0; k2 < nE; k2++)
            {
                long h = pEv + 24 * k2;
                Ptr(h); Ptr(h + 8); Ptr(h + 16);
                long d = L(b, h + 16);
                if (L(b, d + 8) != 0) Ptr(d + 8);
            }
            for (int g = 0; g < nG; g++)
            {
                long gh = pGr + 32 * g;
                long cnt = L(b, gh), pl = L(b, gh + 8), pd = L(b, gh + 16);
                if (pl != 0) Ptr(gh + 8);
                if (pd != 0) { Ptr(gh + 16); if (L(b, pd + 8) != 0) Ptr(pd + 8); }
                for (int j = 0; j < cnt; j++) Ptr(pl + 8 * j);
            }
            foreach (int f in a.BlobPtrs)
                if (!In(L(b, s + f))) throw new InvalidDataException($"TAE anim {a.Id}: pointer at blob+0x{f:X} leaves the blob");
            t.Anims.Add(a);
        }
        return t;
    }

    /// <summary>Re-emit: header, table, groups, records, blobs (contiguous, as vanilla), every pointer relocated.</summary>
    public byte[] Write()
    {
        var anims = Anims.OrderBy(a => a.Id).ToList();
        int n = anims.Count;
        long tab = 0xC0;
        // groups over runs of consecutive ids
        var runs = new List<(int First, int Last, int Index)>();
        for (int i = 0; i < n; i++)
        {
            int first = i;
            while (i + 1 < n && anims[i + 1].Id == anims[i].Id + 1) i++;
            runs.Add(((int)anims[first].Id, (int)anims[i].Id, first));
        }
        long grp = tab + 16 * n, grpRecs = grp + 16, recs = grpRecs + 16 * runs.Count, blobs = recs + 0x30 * n;
        var blobStart = new long[n]; long p = blobs;
        for (int k = 0; k < n; k++) { blobStart[k] = p; p += anims[k].Blob.Length; }
        var o = new byte[p];
        Header.CopyTo(o, 0);
        void W64(long off, long v) => BinaryPrimitives.WriteInt64LittleEndian(o.AsSpan((int)off), v);
        void W32(long off, int v) => BinaryPrimitives.WriteInt32LittleEndian(o.AsSpan((int)off), v);
        W32(0x0C, (int)p); W32(0x54, n); W64(0x58, tab); W64(0x60, grp); W64(0x70, n); W64(0x78, recs); W64(0xB0, tab); W64(0xB8, tab);
        for (int k = 0; k < n; k++) { W64(tab + 16 * k, anims[k].Id); W64(tab + 16 * k + 8, recs + 0x30 * k); }
        W64(grp, runs.Count); W64(grp + 8, grpRecs);
        for (int r = 0; r < runs.Count; r++) { W32(grpRecs + 16 * r, runs[r].First); W32(grpRecs + 16 * r + 4, runs[r].Last); W64(grpRecs + 16 * r + 8, tab + 16 * runs[r].Index); }
        for (int k = 0; k < n; k++)
        {
            var a = anims[k]; long delta = blobStart[k] - a.BlobStart;
            var rec = (byte[])a.Record.Clone();
            for (int f = 0; f < 4; f++) { long v = L(rec, 8 * f); if (v != 0) BinaryPrimitives.WriteInt64LittleEndian(rec.AsSpan(8 * f), v + delta); }
            rec.CopyTo(o, recs + 0x30 * k);
            var bl = (byte[])a.Blob.Clone();
            foreach (int f in a.BlobPtrs) BinaryPrimitives.WriteInt64LittleEndian(bl.AsSpan(f), L(bl, f) + delta);
            bl.CopyTo(o, blobStart[k]);
        }
        return o;
    }

    public Anim? Find(long id) => Anims.FirstOrDefault(a => a.Id == id);

    /// <summary>One event of a new animation: type, times (seconds), parameter bytes (the type's DS2 size).</summary>
    public sealed record Event(int Type, float Start, float End, byte[] Params);

    /// <summary>DS2 event type â†’ group type (every event of the 696 vanilla files sits in a group of this type).</summary>
    public static readonly Dictionary<int, int> GroupOf = new() { [2200] = 1100, [2300] = 1150, [280100] = 280000, [120200] = 120000, [110000] = 110000, [2105] = 2100, [2100] = 2100, [110100] = 110000, [110600] = 110000, [280000] = 280000 };   // 110100 / 110600: session 11, tae-groups on c103000_pl / c305000_pl; 280000 (rule LP1, leap12): group 280000 as 280100 (FUN_140324eb0 l.622066 → FUN_14016c150 handles both)

    /// <summary>Parameter bytes of the first event of <paramref name="type"/> anywhere in this file (its DS2 size and default
    /// content), or null.</summary>
    public byte[]? ParamTemplate(int type)
    {
        foreach (var a in Anims)
            foreach (var e in Events(a))
                if (e.Type == type) return e.Params;
        return null;
    }

    /// <summary>The events of an animation (type, times, parameter bytes up to the next block).</summary>
    public static List<Event> Events(Anim a)
    {
        var o = new List<Event>();
        var b = a.Blob; long s = a.BlobStart;
        long pEv = L(a.Record, 0); int nE = I(a.Record, 32);
        var dataStarts = new List<long>();
        for (int k = 0; k < nE; k++) dataStarts.Add(L(b, pEv - s + 24 * k + 16));
        long pGr = L(a.Record, 8); long pT = L(a.Record, 16);
        long limit = pGr != 0 ? pGr : s + b.Length;
        var sorted = dataStarts.Order().ToList();
        for (int k = 0; k < nE; k++)
        {
            long h = pEv - s + 24 * k;
            float st = BitConverter.ToSingle(b, (int)(L(b, h) - s)), en = BitConverter.ToSingle(b, (int)(L(b, h + 8) - s));
            long d = dataStarts[k];
            int type = I(b, d - s);
            long pp = L(b, d - s + 8);
            int ix = sorted.IndexOf(d);
            long end = ix + 1 < sorted.Count ? sorted[ix + 1] : limit;
            o.Add(new Event(type, st, en, pp != 0 ? b[(int)(pp - s)..(int)(end - s)] : []));
        }
        return o;
    }

    /// <summary>
    /// Add (or replace) animation <paramref name="id"/>: a standard anim file struct (not looping, 30 fps, length
    /// <paramref name="frames30"/>), its events with DS2's layout and grouping. An animation without events gets null
    /// event / group / time pointers, as vanilla's event-less animations.
    /// </summary>
    public void AddAnimation(long id, int frames30, IReadOnlyList<Event> events)
    {
        Anims.RemoveAll(a => a.Id == id);
        // the blob is built with pointers relative to a large base (never 0, so the record's null-pointer convention stays
        // unambiguous); Write relocates them to the blob's final position
        const long Base = 0x100000000L;
        var a = new Anim { Id = id, BlobStart = Base };
        var bl = new List<byte>(); var ptrs = new List<int>();
        void U64(long v) => bl.AddRange(BitConverter.GetBytes(v));
        void U32(int v) => bl.AddRange(BitConverter.GetBytes(v));
        void Pad16() { while (bl.Count % 16 != 0) bl.Add(0); }
        void Patch(int at, long v) { var arr = BitConverter.GetBytes(Base + v); for (int i = 0; i < 8; i++) bl[at + i] = arr[i]; }
        // anim file struct (0x30)
        U32(0); U32(0); ptrs.Add(bl.Count); U64(Base + 0x10); ptrs.Add(bl.Count); U64(Base + 0x30);
        bl.Add(0); bl.Add(30); bl.Add(0); bl.Add(0); U32(frames30); bl.AddRange(new byte[16]);
        var evs = events.OrderBy(e => e.Start).ThenBy(e => e.Type).ToList();
        long pEv = 0, pGr = 0, pT = 0;
        int nE = evs.Count, nG = 0, nT = 0;
        if (nE > 0)
        {
            var times = evs.SelectMany(e => new[] { e.Start, e.End }).Distinct().Order().ToList();
            nT = times.Count; pT = bl.Count;
            foreach (float f in times) bl.AddRange(BitConverter.GetBytes(f));
            Pad16();
            pEv = bl.Count;
            var hdrAt = new List<int>();
            foreach (var e in evs)
            {
                hdrAt.Add(bl.Count);
                ptrs.Add(bl.Count); U64(Base + pT + 4 * times.IndexOf(e.Start));
                ptrs.Add(bl.Count); U64(Base + pT + 4 * times.IndexOf(e.End));
                ptrs.Add(bl.Count); U64(0);
            }
            Pad16();
            for (int k = 0; k < nE; k++)
            {
                int d = bl.Count;
                Patch(hdrAt[k] + 16, d);
                U32(evs[k].Type); U32(0);
                if (evs[k].Params.Length > 0) { ptrs.Add(bl.Count); U64(Base + d + 0x10); } else U64(0);
                bl.AddRange(evs[k].Params);
                Pad16();
            }
            var groups = evs.Select((e, k) => (G: GroupOf.TryGetValue(e.Type, out int g) ? g : throw new InvalidDataException($"TAE event type {e.Type}: no known DS2 group type"), K: k))
                .GroupBy(x => x.G).OrderBy(g => g.Key).ToList();
            nG = groups.Count; pGr = bl.Count;
            var ghAt = new List<int>();
            foreach (var g in groups) { ghAt.Add(bl.Count); U64(g.Count()); ptrs.Add(bl.Count); U64(0); ptrs.Add(bl.Count); U64(0); U64(0); }
            for (int gi = 0; gi < groups.Count; gi++)
            {
                int d = bl.Count;
                Patch(ghAt[gi] + 16, d);
                U32(groups[gi].Key); U32(0); ptrs.Add(bl.Count); U64(Base + d + 0x10); bl.AddRange(new byte[16]);
                int l = bl.Count;
                Patch(ghAt[gi] + 8, l);
                foreach (var x in groups[gi]) { ptrs.Add(bl.Count); U64(Base + hdrAt[x.K]); }
                Pad16();
            }
        }
        a.Blob = bl.ToArray(); a.BlobPtrs = ptrs;
        long R(long v) => nE > 0 ? Base + v : 0;
        BinaryPrimitives.WriteInt64LittleEndian(a.Record.AsSpan(0), R(pEv)); BinaryPrimitives.WriteInt64LittleEndian(a.Record.AsSpan(8), R(pGr));
        BinaryPrimitives.WriteInt64LittleEndian(a.Record.AsSpan(16), R(pT)); BinaryPrimitives.WriteInt64LittleEndian(a.Record.AsSpan(24), Base);
        BinaryPrimitives.WriteInt32LittleEndian(a.Record.AsSpan(32), nE); BinaryPrimitives.WriteInt32LittleEndian(a.Record.AsSpan(36), nG);
        BinaryPrimitives.WriteInt32LittleEndian(a.Record.AsSpan(40), nT); BinaryPrimitives.WriteInt32LittleEndian(a.Record.AsSpan(44), 0);
        Anims.Add(a);
    }
}
