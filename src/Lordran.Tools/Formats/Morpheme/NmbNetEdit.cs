using System.Buffers.Binary;
using System.Text;

namespace Lordran.Tools.Formats.Morpheme;

/// <summary>
/// DS2 Morpheme network bundle (NMB type 10) re-serialiser (session 8, track enemies step 1): the C# port of our own Python
/// research code data\research\morpheme\net_rewrite.py (block model + relocation), net_nodewrite.py (generic node-block
/// writer), net_smwrite.py (state-machine node writer) and the table helpers of net_clone.py. No NaturalMotion code.
/// <para><b>Block model</b> (NETWORK-FORMAT.md §1): the bundle is cut into self-contained blocks (every byte belongs to one):
/// node 0 region (NetworkDef + its attribs, always at 0), semantic-lookup pointer array + tables, node pointer table,
/// request-distributor pointer array, 4 node-id lists, 3 string tables, the per-node table, 2 task-function tables,
/// node blocks 1..n-1 (ascending), request distributors, 2 bone-order tables. Bundle-absolute pointers (fixed up when
/// blocks move): NetworkDef 0x98/0xA8..0xF0/0x100/0x110/0x130/0x138, every node-table entry, every distributor /
/// lookup-table pointer-array entry, the data pointer at the start of each bone-order table; everything else is relative
/// to its own block or record. Python proof: re-emit byte-identical 173/173, relocated copies decode identically 173/173;
/// the C# port is checked the same way by <c>nmb-net-roundtrip</c> (all 173 DS2 networks).</para>
/// <para><b>Node block</b> (writer rule, byte-exact on 216,254 DS2 nodes): NodeDef header (0x90) + child ids (u16, padded to
/// 4 with 0xCD) + input-CP connections {u16 node, u16 pin} + attrib handles {u64 ptr, u64 size, u32 align, u32 pad} +
/// each attrib blob 16-aligned, in file order.</para>
/// </summary>
public static class NmbNetEdit
{
    static readonly int[] HdrPtrs = [0x98, 0xA8, 0xB0, 0xB8, 0xC0, 0xC8, 0xD0, 0xD8, 0xE0, 0xE8, 0xF0, 0x100, 0x110, 0x130, 0x138];

    static uint U32(byte[] x, int o) => BinaryPrimitives.ReadUInt32LittleEndian(x.AsSpan(o));
    static ulong U64(byte[] x, int o) => BinaryPrimitives.ReadUInt64LittleEndian(x.AsSpan(o));
    static ushort U16(byte[] x, int o) => BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(o));

    public sealed class Block
    {
        public string Kind = ""; public int Start, End, Idx = -1;
        public List<int> AbsPtrs = [];
    }

    /// <summary>net_rewrite.layout: the blocks of a network bundle, sorted by start.</summary>
    public static List<Block> Layout(byte[] x)
    {
        int nn = (int)U32(x, 0x90);
        var B = new List<Block> { new() { Kind = "node0", Start = 0, AbsPtrs = [.. HdrPtrs] } };
        int pN = (int)U64(x, 0x98);
        B.Add(new Block { Kind = "node_table", Start = pN, AbsPtrs = [.. Enumerable.Range(0, nn).Select(i => 8 * i)] });
        for (int i = 1; i < nn; i++) B.Add(new Block { Kind = "node", Start = (int)U64(x, pN + 8 * i), Idx = i });
        foreach (var (hp, k) in new[] { (0xA8, "idlistA"), (0xB0, "smlist"), (0xB8, "emitters"), (0xC0, "idlistC"), (0xC8, "names"), (0xD0, "reqnames"),
                     (0xD8, "tracknames"), (0xE0, "pernode"), (0xE8, "tasks"), (0xF0, "cptasks") })
            B.Add(new Block { Kind = k, Start = (int)U64(x, hp) });
        int nm = (int)U32(x, 0xF8), pM = (int)U64(x, 0x100);
        B.Add(new Block { Kind = "dist_ptrs", Start = pM, AbsPtrs = [.. Enumerable.Range(0, nm).Select(i => 8 * i)] });
        for (int i = 0; i < nm; i++) B.Add(new Block { Kind = "dist", Start = (int)U64(x, pM + 8 * i), Idx = i });
        int nl = (int)U32(x, 0x108), pL = (int)U64(x, 0x110);
        B.Add(new Block { Kind = "lut_ptrs", Start = pL, AbsPtrs = [.. Enumerable.Range(0, nl).Select(i => 8 * i)] });
        for (int i = 0; i < nl; i++) B.Add(new Block { Kind = "lut", Start = (int)U64(x, pL + 8 * i), Idx = i });
        foreach (var (hp, k) in new[] { (0x130, "boneorderA"), (0x138, "boneorderName") })
            B.Add(new Block { Kind = k, Start = (int)U64(x, hp), AbsPtrs = [0] });
        B.Sort((a, b) => a.Start.CompareTo(b.Start));
        for (int i = 0; i + 1 < B.Count; i++)
        {
            if (B[i].Start >= B[i + 1].Start) throw new InvalidDataException($"network blocks overlap: {B[i].Kind} 0x{B[i].Start:X} / {B[i + 1].Kind} 0x{B[i + 1].Start:X}");
            B[i].End = B[i + 1].Start;
        }
        B[^1].End = x.Length;
        return B;
    }

    /// <summary>A block to (re-)emit: its bytes, alignment, the 16-byte phase node blocks keep (their attribs are 16-aligned
    /// in absolute bundle terms), and its bundle-absolute pointers as (field, target item, offset in the target).</summary>
    public sealed class Item
    {
        public string Kind = ""; public int Idx = -1, Align = 16; public int? Phase;
        public byte[] Data = [];
        public List<(int Field, Item Target, long Delta)> Ptrs = [];
        public override string ToString() => $"{Kind}{(Idx >= 0 ? $" {Idx}" : "")} ({Data.Length} bytes)";
    }

    static int BlockAlign(int start) { int a = 16; while (a > 1 && start % a != 0) a /= 2; return a; }

    /// <summary>net_clone.build_items.</summary>
    public static List<Item> BuildItems(byte[] x)
    {
        var B = Layout(x);
        var starts = B.Select(b => b.Start).ToArray();
        var items = B.Select(b => new Item
        {
            Kind = b.Kind, Data = x[b.Start..b.End], Align = BlockAlign(b.Start), Idx = b.Idx, Phase = b.Kind == "node" ? b.Start % 16 : null,
        }).ToList();
        for (int k = 0; k < B.Count; k++)
            foreach (int f in B[k].AbsPtrs)
            {
                long v = (long)U64(x, B[k].Start + f);
                if (v == 0) continue;
                int t = Array.BinarySearch(starts, (int)v);
                if (t < 0) t = ~t - 1;
                items[k].Ptrs.Add((f, items[t], v - B[t].Start));
            }
        return items;
    }

    /// <summary>net_clone.emit_items: items in order, each at its alignment (and node phase), pointers fixed up.</summary>
    public static byte[] Emit(List<Item> items)
    {
        var o = new List<byte>(items.Sum(i => i.Data.Length) + 4096);
        var pos = new Dictionary<Item, int>(ReferenceEqualityComparer.Instance);
        foreach (var it in items)
        {
            if (it.Kind == "node0" && o.Count != 0) throw new InvalidOperationException("node 0 must come first");
            while (o.Count % it.Align != 0 || (it.Phase is int ph && o.Count % 16 != ph)) o.Add(0xCD);
            pos[it] = o.Count;
            o.AddRange(it.Data);
        }
        var r = o.ToArray();
        foreach (var it in items)
            foreach (var (f, t, d) in it.Ptrs)
                BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(pos[it] + f), (ulong)(pos[t] + d));
        return r;
    }

    /// <summary>net_rewrite test: emit with `pad` 0xCD bytes before every block after node 0 (relocation proof).</summary>
    public static byte[] EmitPadded(byte[] x, int pad)
    {
        var items = BuildItems(x);
        var o = new List<byte>(); var pos = new Dictionary<Item, int>(ReferenceEqualityComparer.Instance);
        foreach (var it in items)
        {
            if (it.Kind != "node0") { for (int i = 0; i < pad; i++) o.Add(0xCD); while (o.Count % 16 != 0) o.Add(0xCD); }
            pos[it] = o.Count; o.AddRange(it.Data);
        }
        var r = o.ToArray();
        foreach (var it in items) foreach (var (f, t, d) in it.Ptrs) BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(pos[it] + f), (ulong)(pos[t] + d));
        return r;
    }

    // ================================================================== node blocks (net_nodewrite.py)

    public sealed class NodeModel
    {
        public byte[] Hdr = [];
        public List<ushort> Children = [];
        public List<(ushort Node, ushort Pin)> Cps = [];
        public List<(long Ptr, long Size, uint Align, uint Pad)> Handles = [];
        public Dictionary<long, byte[]> Blobs = [];
        public List<long> Order = [];
        public long FpCh, FpCP; // original child / CP pointers (null-pointer convention)
    }

    public static NodeModel ReadNode(byte[] x, int p, int end)
    {
        var m = new NodeModel { Hdr = x[p..(p + 0x90)] };
        int nch = U16(x, p + 0x0A), ncp = x[p + 0x0E], nah = U16(x, p + 0x10);
        long pCh = (long)U64(x, p + 0x20), pCP = (long)U64(x, p + 0x28), pAH = (long)U64(x, p + 0x30);
        m.FpCh = pCh; m.FpCP = pCP;
        if (nch > 0 && pCh != 0) for (int k = 0; k < nch; k++) m.Children.Add(U16(x, p + (int)pCh + 2 * k));
        if (ncp > 0 && pCP != 0) for (int k = 0; k < ncp; k++) m.Cps.Add((U16(x, p + (int)pCP + 4 * k), U16(x, p + (int)pCP + 4 * k + 2)));
        if (nah > 0 && pAH != 0)
            for (int k = 0; k < nah; k++)
            {
                int h = p + (int)pAH + 0x18 * k;
                m.Handles.Add(((long)U64(x, h), (long)U64(x, h + 8), U32(x, h + 16), U32(x, h + 20)));
            }
        var starts = m.Handles.Where(h => h.Ptr != 0).Select(h => h.Ptr).Distinct().Order().ToList();
        for (int k = 0; k < starts.Count; k++)
        {
            long s = starts[k], e = k + 1 < starts.Count ? starts[k + 1] : end - p;
            m.Blobs[s] = x[(p + (int)s)..(p + (int)e)];
        }
        m.Order = starts;
        return m;
    }

    public static byte[] WriteNode(NodeModel m)
    {
        var o = new List<byte>(m.Hdr);
        long pCh = 0, pCP = 0, pAH = 0;
        if (m.Children.Count > 0)
        {
            pCh = o.Count; foreach (var c in m.Children) { o.Add((byte)c); o.Add((byte)(c >> 8)); }
            while (o.Count % 4 != 0) o.Add(0xCD);
        }
        if (m.Cps.Count > 0)
        {
            pCP = o.Count; foreach (var (a, b) in m.Cps) { o.Add((byte)a); o.Add((byte)(a >> 8)); o.Add((byte)b); o.Add((byte)(b >> 8)); }
        }
        if (m.Handles.Count > 0)
        {
            while (o.Count % 4 != 0) o.Add(0xCD);
            pAH = o.Count; o.AddRange(new byte[0x18 * m.Handles.Count]);
        }
        var newpos = new Dictionary<long, long>();
        foreach (long s in m.Order)
        {
            while (o.Count % 16 != 0) o.Add(0xCD);
            newpos[s] = o.Count; o.AddRange(m.Blobs[s]);
        }
        var r = o.ToArray();
        for (int k = 0; k < m.Handles.Count; k++)
        {
            var (pa, sz, al, pad) = m.Handles[k];
            int h = (int)pAH + 0x18 * k;
            BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(h), (ulong)(newpos.TryGetValue(pa, out long np) ? np : 0));
            BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(h + 8), (ulong)sz);
            BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(h + 16), al);
            BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(h + 20), pad);
        }
        if (m.Children.Count > 0 || m.FpCh != 0) BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x0A), (ushort)m.Children.Count);
        r[0x0E] = (byte)m.Cps.Count;
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x10), (ushort)m.Handles.Count);
        BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(0x20), (ulong)(m.Children.Count > 0 || m.FpCh != 0 ? pCh : 0));
        BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(0x28), (ulong)(m.Cps.Count > 0 || m.FpCP != 0 ? pCP : 0));
        BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(0x30), (ulong)pAH);
        return r;
    }

    // ================================================================== state-machine nodes (net_smwrite.py)

    /// <summary>Condition sizes by type (net_smwrite.COND_SIZE).</summary>
    public static readonly Dictionary<int, int> CondSize = new()
    {
        [601] = 0x50, [617] = 0x50, [607] = 0x48, [602] = 0x58, [603] = 0x58, [606] = 0x58, [608] = 0x58, [623] = 0x58, [624] = 0x58,
        [625] = 0x58, [627] = 0x58, [610] = 0x60, [618] = 0x60, [611] = 0x68,
    };

    public sealed class SmState
    {
        public ushort Node, Src = 0xCDCD, Dst = 0xCDCD, U20;
        public List<ushort> Entry = [], ExitConds = [], ExitTrans = [];
        public SmState Clone() => new() { Node = Node, Src = Src, Dst = Dst, U20 = U20, Entry = [.. Entry], ExitConds = [.. ExitConds], ExitTrans = [.. ExitTrans] };
    }

    public sealed class SmDef
    {
        public byte[] Hdr = [];
        public ushort Default; public byte B28;
        public List<SmState> States = [];
        public SmState Glob = new();
        public List<byte[]> Conds = [];
    }

    public sealed class SmNodeModel
    {
        public byte[] Hdr = [];
        public List<ushort> Children = [];
        public (long Size, uint Align, uint Pad) Handle;
        public SmDef Sm = new();
        public byte[] Tail = [];
    }

    static SmState Rec(byte[] x, int o)
    {
        var s = new SmState { Node = U16(x, o) };
        int nent = U16(x, o + 2); long pE = (long)U64(x, o + 8);
        s.Src = U16(x, o + 0x10); s.Dst = U16(x, o + 0x12); int nexc = U16(x, o + 0x14); long pX = (long)U64(x, o + 0x18);
        s.U20 = U16(x, o + 0x20); int nxt = U16(x, o + 0x22); long pT = (long)U64(x, o + 0x28);
        for (int k = 0; k < nent; k++) s.Entry.Add(U16(x, o + (int)pE + 2 * k));
        for (int k = 0; k < nexc; k++) s.ExitConds.Add(U16(x, o + (int)pX + 2 * k));
        for (int k = 0; k < nxt; k++) s.ExitTrans.Add(U16(x, o + (int)pT + 2 * k));
        return s;
    }

    /// <summary>The SM definition attrib (type 38) at absolute offset A.</summary>
    public static SmDef ReadSmDef(byte[] x, int A)
    {
        var sm = new SmDef { Hdr = x[A..(A + 0x10)], Default = U16(x, A + 0x10), B28 = x[A + 0x28] };
        int nst = U16(x, A + 0x12);
        long pSt = (long)U64(x, A + 0x18), pGl = (long)U64(x, A + 0x20);
        int ncond = U16(x, A + 0x2A); long pC = (long)U64(x, A + 0x30);
        for (int k = 0; k < nst; k++) sm.States.Add(Rec(x, A + (int)pSt + 0x30 * k));
        sm.Glob = Rec(x, A + (int)pGl);
        for (int k = 0; k < ncond; k++)
        {
            int c = A + (int)U64(x, A + (int)pC + 8 * k);
            int t = U16(x, c);
            sm.Conds.Add(x[c..(c + CondSize[t])]);
        }
        return sm;
    }

    public static SmNodeModel ReadSmNode(byte[] x, int p, int end)
    {
        var m = new SmNodeModel { Hdr = x[p..(p + 0x90)] };
        int nch = U16(x, p + 0x0A);
        long pCh = (long)U64(x, p + 0x20), pAH = (long)U64(x, p + 0x30);
        for (int k = 0; k < nch; k++) m.Children.Add(U16(x, p + (int)pCh + 2 * k));
        int nah = U16(x, p + 0x10);
        if (nah != 1) throw new InvalidDataException($"state machine node at 0x{p:X} has {nah} attribs");
        long pa = (long)U64(x, p + (int)pAH), sz = (long)U64(x, p + (int)pAH + 8);
        m.Handle = (sz, U32(x, p + (int)pAH + 16), U32(x, p + (int)pAH + 20));
        int A = p + (int)pa;
        m.Sm = ReadSmDef(x, A);
        int smEnd;
        if (m.Sm.Conds.Count > 0)
        {
            long pC = (long)U64(x, A + 0x30);
            long last = (long)U64(x, A + (int)pC + 8 * (m.Sm.Conds.Count - 1));
            smEnd = A + (int)last + m.Sm.Conds[^1].Length;
        }
        else smEnd = A + WriteSmDef(m.Sm).Length;
        m.Tail = x[smEnd..end];
        return m;
    }

    public static byte[] WriteSmDef(SmDef sm)
    {
        int n = sm.States.Count;
        var o = new List<byte>(sm.Hdr);
        void U16w(int v) { o.Add((byte)v); o.Add((byte)(v >> 8)); }
        void U64w(long v) { for (int i = 0; i < 8; i++) o.Add((byte)(v >> (8 * i))); }
        void Cd(int c) { for (int i = 0; i < c; i++) o.Add(0xCD); }
        U16w(sm.Default); U16w(n); Cd(4);
        U64w(0x40); U64w(0x40 + 0x30 * n);
        o.Add(sm.B28); o.Add(0xCD); U16w(sm.Conds.Count); Cd(4);
        U64w(0); Cd(8);
        var recs = sm.States.Append(sm.Glob).ToList();
        var lists = new List<byte>(); int cur = 0x40 + 0x30 * recs.Count;
        var recbytes = new List<byte>();
        for (int k = 0; k < recs.Count; k++)
        {
            var st = recs[k]; int ro = 0x40 + 0x30 * k;
            var ptr = new Dictionary<string, long>();
            foreach (var (key, L) in new[] { ("exit_conds", st.ExitConds), ("exit_trans", st.ExitTrans), ("entry", st.Entry) })
            {
                if (L.Count > 0)
                {
                    ptr[key] = cur - ro;
                    var b = new List<byte>(); foreach (var v in L) { b.Add((byte)v); b.Add((byte)(v >> 8)); }
                    while (b.Count % 4 != 0) b.Add(0xCD);
                    lists.AddRange(b); cur += b.Count;
                }
                else ptr[key] = 0;
            }
            void R16(int v) { recbytes.Add((byte)v); recbytes.Add((byte)(v >> 8)); }
            void R64(long v) { for (int i = 0; i < 8; i++) recbytes.Add((byte)(v >> (8 * i))); }
            void RCd(int c) { for (int i = 0; i < c; i++) recbytes.Add(0xCD); }
            R16(st.Node); R16(st.Entry.Count); RCd(4); R64(ptr["entry"]);
            R16(st.Src); R16(st.Dst); R16(st.ExitConds.Count); RCd(2); R64(ptr["exit_conds"]);
            R16(st.U20); R16(st.ExitTrans.Count); RCd(4); R64(ptr["exit_trans"]);
        }
        o.AddRange(recbytes); o.AddRange(lists);
        int pC = o.Count;
        var r = o;
        long co = pC + 8 * sm.Conds.Count;
        var ptrs = new List<long>();
        foreach (var c in sm.Conds) { ptrs.Add(co); co += c.Length; }
        foreach (long p in ptrs) U64w(p);
        foreach (var c in sm.Conds) o.AddRange(c);
        var arr = o.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(arr.AsSpan(0x30), (ulong)(sm.Conds.Count > 0 ? pC : 0));
        return arr;
    }

    public static byte[] WriteSmNode(SmNodeModel m)
    {
        var hdr = (byte[])m.Hdr.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(hdr.AsSpan(0x0A), (ushort)m.Children.Count);
        var o = new List<byte>(hdr);
        int pCh = o.Count;
        foreach (var c in m.Children) { o.Add((byte)c); o.Add((byte)(c >> 8)); }
        while (o.Count % 4 != 0) o.Add(0xCD);
        int pAH = o.Count;
        o.AddRange(new byte[0x18]);
        while (o.Count % 16 != 0) o.Add(0xCD);
        int A = o.Count;
        o.AddRange(WriteSmDef(m.Sm));
        o.AddRange(m.Tail);
        var r = o.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(pAH), (ulong)A);
        BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(pAH + 8), (ulong)m.Handle.Size);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(pAH + 16), m.Handle.Align);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(pAH + 20), m.Handle.Pad);
        BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(0x20), (ulong)(m.Children.Count > 0 ? pCh : 0));
        BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(0x30), (ulong)pAH);
        return r;
    }

    // ================================================================== network-level reads used by the editors

    /// <summary>End offset of every node block (the next block's start), from the block layout.</summary>
    public static Dictionary<int, int> NodeEnds(byte[] x)
    {
        var d = new Dictionary<int, int>();
        foreach (var b in Layout(x)) if (b.Kind == "node") d[b.Idx] = b.End;
        return d;
    }

    /// <summary>String table {u32 n, u32 dataLen, u64 pIds, u64 pOffs, u64 pData} at o: (id, name) pairs (cp932).</summary>
    public static List<(uint Id, string Name, uint Off)> StringTable(byte[] x, int o)
    {
        int n = (int)U32(x, o); long pI = (long)U64(x, o + 8), pO = (long)U64(x, o + 16), pD = (long)U64(x, o + 24);
        var l = new List<(uint, string, uint)>();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var enc = Encoding.GetEncoding(932);
        for (int i = 0; i < n; i++)
        {
            uint id = U32(x, o + (int)pI + 4 * i), off = U32(x, o + (int)pO + 4 * i);
            int s = o + (int)pD + (int)off, e = Array.IndexOf(x, (byte)0, s);
            l.Add((id, enc.GetString(x, s, e - s), off));
        }
        return l;
    }

    /// <summary>Node names (first non-empty entry per id: CPs 'ControlParameters|X', some state machines).</summary>
    public static Dictionary<int, string> NodeNames(byte[] x)
    {
        var d = new Dictionary<int, string>();
        foreach (var (id, nm, _) in StringTable(x, (int)U64(x, 0xC8))) if (nm.Length > 0) d.TryAdd((int)id, nm);
        return d;
    }

    /// <summary>{u32 count, 4xCD, u64 0x10, u16 ids...} padded with 0xCD to 4 (net_clone.rebuild_id_list).</summary>
    public static byte[] RebuildIdList(byte[] blob, IReadOnlyList<int> ids)
    {
        var o = new List<byte>(blob[..16]);
        var r0 = o.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(r0.AsSpan(0), (uint)ids.Count);
        BinaryPrimitives.WriteUInt64LittleEndian(r0.AsSpan(8), ids.Count > 0 ? 0x10UL : 0UL);
        o = [.. r0];
        foreach (int v in ids) { o.Add((byte)v); o.Add((byte)(v >> 8)); }
        while (o.Count % 4 != 0) o.Add(0xCD);
        return o.ToArray();
    }

    public static List<int> IdsOf(byte[] blob)
    {
        int n = (int)U32(blob, 0); long p = (long)U64(blob, 8);
        var l = new List<int>();
        for (int i = 0; i < n; i++) l.Add(U16(blob, (int)p + 2 * i));
        return l;
    }

    /// <summary>String table rebuilt with entries (id, offset into the unchanged string data) (net_clone.rebuild_strtab).</summary>
    public static byte[] RebuildStrtab(byte[] blob, IReadOnlyList<(uint Id, uint Off)> entries)
    {
        uint dlen = U32(blob, 4); long pD = (long)U64(blob, 24);
        var data = blob[(int)pD..((int)pD + (int)dlen)];
        int n = entries.Count;
        var o = new List<byte>();
        void U32w(uint v) { for (int i = 0; i < 4; i++) o.Add((byte)(v >> (8 * i))); }
        void U64w(ulong v) { for (int i = 0; i < 8; i++) o.Add((byte)(v >> (8 * i))); }
        U32w((uint)n); U32w(dlen); U64w(32); U64w((ulong)(32 + 4 * n)); U64w((ulong)(32 + 8 * n));
        foreach (var e in entries) U32w(e.Id);
        foreach (var e in entries) U32w(e.Off);
        o.AddRange(data);
        while (o.Count % 4 != 0) o.Add(0xCD);
        return o.ToArray();
    }
}
