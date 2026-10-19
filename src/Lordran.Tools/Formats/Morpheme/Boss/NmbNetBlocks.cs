using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Lordran.Tools.Formats.Morpheme.Boss;

/// <summary>
/// Block model of a DS2 Morpheme network bundle (NMB type 10): the C# port of the research re-serialiser
/// (data\research\morpheme\net_rewrite.py stage 1, net_clone.py build_items/emit_items, net_nodewrite.py stage 2c,
/// net_smwrite.py stage 2a; NETWORK-FORMAT.md §1-2). Session 8, track boss (Asylum Demon 1:1), own code, no NaturalMotion
/// code.
/// <para>The bundle is cut into self-contained blocks, every byte in exactly one: node 0 region (NetworkDef + its attribs +
/// two bone-order sub-tables, always at 0), the semantic-lookup pointer array + tables, the node pointer table, the
/// request-distributor pointer array + distributors, 4 node-id lists, 3 string tables, 2 task-function tables, node
/// blocks 1..n-1 (ascending), the per-node u16 table and the two bone-order tables. Bundle-absolute pointers (fixed up when
/// blocks move): NetworkDef 0x98/0xA8..0xF0/0x100/0x110/0x130/0x138, every node-table entry, every distributor /
/// lookup-table pointer-array entry and the data pointer at the start of each bone-order table; everything else is
/// relative to its own block or record.</para>
/// <para>Python results (session 8): all 173 DS2 networks re-emit byte-identically from the block model, relocated copies
/// decode identically; the node writer re-emits 216,254/216,254 node blocks and the state-machine writer 15,929/15,929 SM
/// nodes byte-identically. The C# port is checked the same way by <c>boss-nmb-net-roundtrip</c>.</para>
/// </summary>
public static class NmbNetBlocks
{
    /// <summary>NetworkDef fields holding bundle-absolute pointers (NETWORK-FORMAT.md §1.1).</summary>
    public static readonly int[] HeaderPointers = [0x98, 0xA8, 0xB0, 0xB8, 0xC0, 0xC8, 0xD0, 0xD8, 0xE0, 0xE8, 0xF0, 0x100, 0x110, 0x130, 0x138];

    static ulong Q(byte[] x, int o) => BinaryPrimitives.ReadUInt64LittleEndian(x.AsSpan(o));
    static uint U(byte[] x, int o) => BinaryPrimitives.ReadUInt32LittleEndian(x.AsSpan(o));
    static ushort H(byte[] x, int o) => BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(o));

    public sealed class Block
    {
        public string Kind = "";
        public int Start, End, Index = -1;
        /// <summary>Offsets (inside the block) of u64 bundle-absolute pointers.</summary>
        public List<int> AbsPtrs = [];
    }

    /// <summary>Cut the bundle into blocks (net_rewrite.layout), sorted by start; each block ends where the next starts.</summary>
    public static List<Block> Layout(byte[] x)
    {
        int nn = (int)U(x, 0x90);
        var B = new List<Block> { new() { Kind = "node0", Start = 0, AbsPtrs = [.. HeaderPointers] } };
        int pN = (int)Q(x, 0x98);
        B.Add(new Block { Kind = "node_table", Start = pN, AbsPtrs = [.. Enumerable.Range(0, nn).Select(i => 8 * i)] });
        for (int i = 1; i < nn; i++) B.Add(new Block { Kind = "node", Start = (int)Q(x, pN + 8 * i), Index = i });
        foreach (var (hp, k) in new[] { (0xA8, "idlistA"), (0xB0, "smlist"), (0xB8, "emitters"), (0xC0, "idlistC"), (0xC8, "names"),
                     (0xD0, "reqnames"), (0xD8, "tracknames"), (0xE0, "pernode"), (0xE8, "tasks"), (0xF0, "cptasks") })
            B.Add(new Block { Kind = k, Start = (int)Q(x, hp) });
        int nm = (int)U(x, 0xF8), pM = (int)Q(x, 0x100);
        B.Add(new Block { Kind = "dist_ptrs", Start = pM, AbsPtrs = [.. Enumerable.Range(0, nm).Select(i => 8 * i)] });
        for (int i = 0; i < nm; i++) B.Add(new Block { Kind = "dist", Start = (int)Q(x, pM + 8 * i), Index = i });
        int nl = (int)U(x, 0x108), pL = (int)Q(x, 0x110);
        B.Add(new Block { Kind = "lut_ptrs", Start = pL, AbsPtrs = [.. Enumerable.Range(0, nl).Select(i => 8 * i)] });
        for (int i = 0; i < nl; i++) B.Add(new Block { Kind = "lut", Start = (int)Q(x, pL + 8 * i), Index = i });
        B.Add(new Block { Kind = "boneorderA", Start = (int)Q(x, 0x130), AbsPtrs = [0] });
        B.Add(new Block { Kind = "boneorderName", Start = (int)Q(x, 0x138), AbsPtrs = [0] });
        B.Sort((a, b) => a.Start.CompareTo(b.Start));
        for (int i = 0; i + 1 < B.Count; i++)
        {
            if (B[i].Start >= B[i + 1].Start) throw new InvalidDataException($"network blocks overlap: {B[i].Kind} 0x{B[i].Start:x} / {B[i + 1].Kind} 0x{B[i + 1].Start:x}");
            B[i].End = B[i + 1].Start;
        }
        B[^1].End = x.Length;
        return B;
    }

    static int TargetBlock(List<Block> B, int[] starts, long v)
    {
        int k = Array.BinarySearch(starts, (int)v);
        if (k < 0) k = ~k - 1;
        return k;
    }

    /// <summary>One block as a movable item: data (its bytes), alignment of its original start (largest power of two ≤ 16
    /// dividing it), node blocks keep their 16-byte phase (attribs stay 16-aligned in absolute terms), and the absolute
    /// pointers it holds as (field offset, target item, delta into the target).</summary>
    public sealed class Item
    {
        public string Kind = "";
        public byte[] Data = [];
        public int Align = 16, Index = -1;
        public int? Phase;
        public List<(int Field, Item Target, long Delta)> Ptrs = [];
        public override string ToString() => $"{Kind}{(Index >= 0 ? $" {Index}" : "")} ({Data.Length} bytes)";
    }

    public static int BlockAlign(int start)
    {
        int a = 16;
        while (a > 1 && start % a != 0) a /= 2;
        return a;
    }

    /// <summary>The bundle as items (net_clone.build_items).</summary>
    public static List<Item> BuildItems(byte[] x)
    {
        var B = Layout(x);
        var starts = B.Select(b => b.Start).ToArray();
        var items = B.Select(b => new Item { Kind = b.Kind, Data = x[b.Start..b.End], Align = BlockAlign(b.Start), Index = b.Index, Phase = b.Kind == "node" ? b.Start % 16 : null }).ToList();
        for (int i = 0; i < B.Count; i++)
            foreach (int f in B[i].AbsPtrs)
            {
                long v = (long)Q(x, B[i].Start + f);
                if (v == 0) continue;
                int t = TargetBlock(B, starts, v);
                items[i].Ptrs.Add((f, items[t], v - B[t].Start));
            }
        return items;
    }

    /// <summary>Emit the items in order (0xCD padding to each item's alignment / phase), then write every absolute
    /// pointer (net_clone.emit_items). <paramref name="pad"/> extra bytes before every block after node 0 (relocation
    /// test, net_rewrite --pad).</summary>
    public static byte[] Emit(IReadOnlyList<Item> items, int pad = 0)
    {
        var o = new MemoryStream();
        var pos = new Dictionary<Item, long>(ReferenceEqualityComparer.Instance);
        foreach (var it in items)
        {
            if (it.Kind == "node0") { if (o.Length != 0) throw new InvalidDataException("node 0 must be the first block"); }
            else for (int k = 0; k < pad; k++) o.WriteByte(0xCD);
            while (o.Length % it.Align != 0 || (it.Phase is int ph && o.Length % 16 != ph)) o.WriteByte(0xCD);
            pos[it] = o.Length;
            o.Write(it.Data);
        }
        var b = o.ToArray();
        foreach (var it in items)
            foreach (var (f, t, d) in it.Ptrs)
                BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan((int)(pos[it] + f)), (ulong)(pos[t] + d));
        return b;
    }

    // ================================================================== node blocks (net_nodewrite)

    /// <summary>A node block: NodeDef header (0x90), child ids (u16), input-CP connections {u16 node, u16 pin}, attrib
    /// handles {u64 ptr (node-relative), u64 size, u32 align, u32 pad}, attrib blobs (each = base struct + trailing data,
    /// pointers relative to the attrib) in file order.</summary>
    public sealed class NodeModel
    {
        public byte[] Header = new byte[0x90];
        public List<ushort> Children = [];
        public List<(ushort Node, ushort Pin)> Cps = [];
        public List<(long Ptr, long Size, uint Align, uint Pad)> Handles = [];
        public Dictionary<long, byte[]> Blobs = [];
        public List<long> Order = [];
        public bool HadChildPtr, HadCpPtr;
        public int Type => (int)BinaryPrimitives.ReadUInt32LittleEndian(Header);
        public ushort Id => BinaryPrimitives.ReadUInt16LittleEndian(Header.AsSpan(6));
    }

    public static NodeModel ReadNode(byte[] x, int p, int end)
    {
        var m = new NodeModel { Header = x[p..(p + 0x90)] };
        int nch = H(x, p + 0x0A), ncp = x[p + 0x0E], nah = H(x, p + 0x10);
        long pCh = (long)Q(x, p + 0x20), pCP = (long)Q(x, p + 0x28), pAH = (long)Q(x, p + 0x30);
        m.HadChildPtr = pCh != 0; m.HadCpPtr = pCP != 0;
        if (nch > 0 && pCh != 0) for (int k = 0; k < nch; k++) m.Children.Add(H(x, p + (int)pCh + 2 * k));
        if (ncp > 0 && pCP != 0) for (int k = 0; k < ncp; k++) m.Cps.Add((H(x, p + (int)pCP + 4 * k), H(x, p + (int)pCP + 4 * k + 2)));
        if (nah > 0 && pAH != 0)
            for (int k = 0; k < nah; k++)
            {
                int h = p + (int)pAH + 0x18 * k;
                m.Handles.Add(((long)Q(x, h), (long)Q(x, h + 8), U(x, h + 16), U(x, h + 20)));
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
        var o = new List<byte>(m.Header);
        int pCh = 0, pCP = 0, pAH = 0;
        void Pad(int a) { while (o.Count % a != 0) o.Add(0xCD); }
        if (m.Children.Count > 0)
        {
            pCh = o.Count;
            foreach (var c in m.Children) { o.Add((byte)c); o.Add((byte)(c >> 8)); }
            Pad(4);
        }
        if (m.Cps.Count > 0)
        {
            pCP = o.Count;
            foreach (var (cn, cp) in m.Cps) { o.Add((byte)cn); o.Add((byte)(cn >> 8)); o.Add((byte)cp); o.Add((byte)(cp >> 8)); }
        }
        if (m.Handles.Count > 0)
        {
            Pad(4);
            pAH = o.Count;
            o.AddRange(new byte[0x18 * m.Handles.Count]);
        }
        var newPos = new Dictionary<long, int>();
        foreach (long s in m.Order)
        {
            Pad(16);
            newPos[s] = o.Count;
            o.AddRange(m.Blobs[s]);
        }
        var b = o.ToArray();
        for (int k = 0; k < m.Handles.Count; k++)
        {
            var (pa, sz, al, pad) = m.Handles[k];
            int h = pAH + 0x18 * k;
            BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(h), (ulong)(newPos.TryGetValue(pa, out int np) ? np : 0));
            BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(h + 8), (ulong)sz);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(h + 16), al);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(h + 20), pad);
        }
        if (m.Children.Count > 0 || m.HadChildPtr) BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x0A), (ushort)m.Children.Count);
        b[0x0E] = (byte)m.Cps.Count;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x10), (ushort)m.Handles.Count);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(0x20), (ulong)(m.Children.Count > 0 || m.HadChildPtr ? pCh : 0));
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(0x28), (ulong)(m.Cps.Count > 0 || m.HadCpPtr ? pCP : 0));
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(0x30), (ulong)pAH);
        return b;
    }

    // ================================================================== state-machine nodes (net_smwrite)

    /// <summary>Condition sizes by type (NETWORK-FORMAT.md §2.4).</summary>
    public static readonly Dictionary<int, int> CondSize = new()
    {
        [601] = 0x50, [617] = 0x50, [607] = 0x48, [602] = 0x58, [603] = 0x58, [606] = 0x58, [608] = 0x58, [623] = 0x58,
        [624] = 0x58, [625] = 0x58, [627] = 0x58, [610] = 0x60, [618] = 0x60, [611] = 0x68,
    };

    /// <summary>A state record (48 bytes): node, entry conditions, source/destination state (transitions; 0xCDCD
    /// otherwise), exit conditions, u16 +0x20 (U), exit transitions.</summary>
    public sealed class SmRecord
    {
        public ushort Node, Src = 0xCDCD, Dst = 0xCDCD, U20;
        public List<ushort> Entry = [], ExitConds = [], ExitTrans = [];
        public SmRecord Clone() => new() { Node = Node, Src = Src, Dst = Dst, U20 = U20, Entry = [.. Entry], ExitConds = [.. ExitConds], ExitTrans = [.. ExitTrans] };
    }

    /// <summary>State machine definition (attrib 38): 16-byte AttribData header, default state, records (states and
    /// transitions in child order) + the global record, u8 +0x28, conditions (raw bytes, payload at +0x48).</summary>
    public sealed class SmDef
    {
        public byte[] Header = new byte[16];
        public ushort Default;
        public byte B28;
        public List<SmRecord> States = [];
        public SmRecord Global = new();
        public List<byte[]> Conds = [];
    }

    public sealed class SmNodeModel
    {
        public byte[] Header = new byte[0x90];
        public List<ushort> Children = [];
        public (long Size, uint Align, uint Pad) Handle;
        public SmDef Sm = new();
        public byte[] Tail = [];
    }

    static SmRecord ReadRecord(byte[] x, int o)
    {
        List<ushort> L(int c, long q) => c > 0 ? [.. Enumerable.Range(0, c).Select(k => H(x, o + (int)q + 2 * k))] : [];
        return new SmRecord
        {
            Node = H(x, o), Entry = L(H(x, o + 2), (long)Q(x, o + 8)),
            Src = H(x, o + 0x10), Dst = H(x, o + 0x12), ExitConds = L(H(x, o + 0x14), (long)Q(x, o + 0x18)),
            U20 = H(x, o + 0x20), ExitTrans = L(H(x, o + 0x22), (long)Q(x, o + 0x28)),
        };
    }

    /// <summary>Read the SM definition attrib at absolute offset <paramref name="A"/>; returns it and its byte length.</summary>
    public static (SmDef Def, int Length) ReadSmDef(byte[] x, int A)
    {
        var sm = new SmDef { Header = x[A..(A + 16)], Default = H(x, A + 0x10), B28 = x[A + 0x28] };
        int nst = H(x, A + 0x12);
        long pSt = (long)Q(x, A + 0x18), pGl = (long)Q(x, A + 0x20);
        int ncond = H(x, A + 0x2A);
        long pC = (long)Q(x, A + 0x30);
        for (int k = 0; k < nst; k++) sm.States.Add(ReadRecord(x, A + (int)pSt + 0x30 * k));
        sm.Global = ReadRecord(x, A + (int)pGl);
        var cps = Enumerable.Range(0, ncond).Select(k => (long)Q(x, A + (int)pC + 8 * k)).ToList();
        foreach (long c in cps)
        {
            int t = H(x, A + (int)c);
            sm.Conds.Add(x[(A + (int)c)..(A + (int)c + CondSize[t])]);
        }
        int len = cps.Count > 0 ? (int)(cps[^1] + CondSize[H(x, A + (int)cps[^1])]) : WriteSmDef(sm).Length;
        return (sm, len);
    }

    public static SmNodeModel ReadSmNode(byte[] x, int p, int end)
    {
        int nch = H(x, p + 0x0A), nah = H(x, p + 0x10);
        long pCh = (long)Q(x, p + 0x20), pAH = (long)Q(x, p + 0x30);
        if (nah != 1) throw new InvalidDataException($"state machine node at 0x{p:x}: {nah} attrib handles (1 expected)");
        var m = new SmNodeModel { Header = x[p..(p + 0x90)] };
        for (int k = 0; k < nch; k++) m.Children.Add(H(x, p + (int)pCh + 2 * k));
        long pa = (long)Q(x, p + (int)pAH);
        m.Handle = ((long)Q(x, p + (int)pAH + 8), U(x, p + (int)pAH + 16), U(x, p + (int)pAH + 20));
        int A = p + (int)pa;
        var (def, len) = ReadSmDef(x, A);
        m.Sm = def;
        m.Tail = x[(A + len)..end];
        return m;
    }

    public static byte[] WriteSmDef(SmDef sm)
    {
        int n = sm.States.Count;
        var o = new List<byte>(sm.Header);
        void U16(int v) { o.Add((byte)v); o.Add((byte)(v >> 8)); }
        void U64(long v) { for (int i = 0; i < 8; i++) o.Add((byte)(v >> (8 * i))); }
        void Cd(int c) { for (int i = 0; i < c; i++) o.Add(0xCD); }
        U16(sm.Default); U16(n); Cd(4);
        U64(0x40); U64(0x40 + 0x30 * n);
        o.Add(sm.B28); o.Add(0xCD); U16(sm.Conds.Count); Cd(4);
        U64(0); Cd(8);
        var recs = sm.States.Append(sm.Global).ToList();
        var lists = new List<byte>();
        int cur = 0x40 + 0x30 * recs.Count;
        var recBytes = new List<byte>();
        for (int k = 0; k < recs.Count; k++)
        {
            var st = recs[k];
            int ro = 0x40 + 0x30 * k;
            var ptr = new Dictionary<string, int>();
            foreach (var (key, L) in new[] { ("exit_conds", st.ExitConds), ("exit_trans", st.ExitTrans), ("entry", st.Entry) })
            {
                if (L.Count == 0) { ptr[key] = 0; continue; }
                ptr[key] = cur - ro;
                var b = new List<byte>();
                foreach (var v in L) { b.Add((byte)v); b.Add((byte)(v >> 8)); }
                while (b.Count % 4 != 0) b.Add(0xCD);
                lists.AddRange(b); cur += b.Count;
            }
            void R16(int v) { recBytes.Add((byte)v); recBytes.Add((byte)(v >> 8)); }
            void R64(long v) { for (int i = 0; i < 8; i++) recBytes.Add((byte)(v >> (8 * i))); }
            void RCd(int c) { for (int i = 0; i < c; i++) recBytes.Add(0xCD); }
            R16(st.Node); R16(st.Entry.Count); RCd(4); R64(ptr["entry"]);
            R16(st.Src); R16(st.Dst); R16(st.ExitConds.Count); RCd(2); R64(ptr["exit_conds"]);
            R16(st.U20); R16(st.ExitTrans.Count); RCd(4); R64(ptr["exit_trans"]);
        }
        o.AddRange(recBytes); o.AddRange(lists);
        int pC = o.Count;
        var b2 = o.ToArray().ToList();
        long co = pC + 8L * sm.Conds.Count;
        var res = new List<byte>(b2);
        foreach (var c in sm.Conds) { for (int i = 0; i < 8; i++) res.Add((byte)(co >> (8 * i))); co += c.Length; }
        foreach (var c in sm.Conds) res.AddRange(c);
        var arr = res.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(arr.AsSpan(0x30), (ulong)(sm.Conds.Count > 0 ? pC : 0));
        return arr;
    }

    public static byte[] WriteSmNode(SmNodeModel m, bool keepTail = true)
    {
        var hdr = (byte[])m.Header.Clone();
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
        if (keepTail) o.AddRange(m.Tail);
        var b = o.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(pAH), (ulong)A);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(pAH + 8), (ulong)m.Handle.Size);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(pAH + 16), m.Handle.Align);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(pAH + 20), m.Handle.Pad);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(0x20), (ulong)(m.Children.Count > 0 ? pCh : 0));
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(0x30), (ulong)pAH);
        return b;
    }

    // ================================================================== tables

    /// <summary>Node-id list {u32 count, 4×CD, u64 0x10, u16 ids…} padded to 4 with CD (net_clone.rebuild_id_list).</summary>
    public static byte[] RebuildIdList(byte[] old, IReadOnlyList<ushort> ids)
    {
        var o = new List<byte>(old[..16]);
        var b = o.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0), (uint)ids.Count);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(8), ids.Count > 0 ? 0x10UL : 0UL);
        o = [.. b];
        foreach (var v in ids) { o.Add((byte)v); o.Add((byte)(v >> 8)); }
        while (o.Count % 4 != 0) o.Add(0xCD);
        return [.. o];
    }

    public static List<ushort> IdList(byte[] blob)
    {
        int n = (int)U(blob, 0); long p = (long)Q(blob, 8);
        return n > 0 ? [.. Enumerable.Range(0, n).Select(k => H(blob, (int)p + 2 * k))] : [];
    }

    /// <summary>String table {u32 n, u32 dataLen, u64 pIds, u64 pOffs, u64 pData, u32 ids[n], u32 offs[n], data} with new
    /// (id, offset) entries over the old data (net_clone.rebuild_strtab).</summary>
    public static byte[] RebuildStrTab(byte[] old, IReadOnlyList<(uint Id, uint Off)> entries)
    {
        int dlen = (int)U(old, 4); long pD = (long)Q(old, 24);
        var data = old[(int)pD..((int)pD + dlen)];
        int n = entries.Count;
        var o = new List<byte>();
        void U32(uint v) { for (int i = 0; i < 4; i++) o.Add((byte)(v >> (8 * i))); }
        void U64(long v) { for (int i = 0; i < 8; i++) o.Add((byte)(v >> (8 * i))); }
        U32((uint)n); U32((uint)dlen); U64(32); U64(32 + 4 * n); U64(32 + 8 * n);
        foreach (var e in entries) U32(e.Id);
        foreach (var e in entries) U32(e.Off);
        o.AddRange(data);
        while (o.Count % 4 != 0) o.Add(0xCD);
        return [.. o];
    }

    public static List<(uint Id, uint Off)> StrTabEntries(byte[] t)
    {
        int n = (int)U(t, 0); long pI = (long)Q(t, 8), pO = (long)Q(t, 16);
        return [.. Enumerable.Range(0, n).Select(k => (U(t, (int)pI + 4 * k), U(t, (int)pO + 4 * k)))];
    }

    // ================================================================== checks (boss-nmb-net-roundtrip)

    /// <summary>
    /// Relocation proof of the block model (net_rewrite --pad): the bundle re-emitted with <paramref name="pad"/> extra bytes
    /// before every block must cut into the same blocks (kind, index, size), with equal bytes outside the absolute-pointer
    /// fields, and every absolute pointer must name the same block at the same delta. Returns the problems (empty = ok).
    /// </summary>
    public static List<string> RelocationCheck(byte[] x, byte[] moved)
    {
        var bad = new List<string>();
        var A = Layout(x); var B = Layout(moved);
        if (A.Count != B.Count) { bad.Add($"block count {A.Count} != {B.Count}"); return bad; }
        var sa = A.Select(b => b.Start).ToArray(); var sb = B.Select(b => b.Start).ToArray();
        for (int i = 0; i < A.Count; i++)
        {
            var a = A[i]; var b = B[i];
            if (a.Kind != b.Kind || a.Index != b.Index) { bad.Add($"block {i}: {a.Kind} {a.Index} vs {b.Kind} {b.Index}"); continue; }
            var da = x.AsSpan(a.Start, a.End - a.Start).ToArray(); var db = moved.AsSpan(b.Start, Math.Min(b.End - b.Start, da.Length)).ToArray();
            if (db.Length < da.Length) { bad.Add($"{a.Kind} {a.Index}: shorter"); continue; }
            foreach (int f in a.AbsPtrs)
            {
                long va = (long)Q(x, a.Start + f), vb = (long)Q(moved, b.Start + f);
                if ((va == 0) != (vb == 0)) { bad.Add($"{a.Kind} {a.Index} +0x{f:x}: null mismatch"); continue; }
                if (va != 0)
                {
                    int ta = TargetBlock(A, sa, va), tb = TargetBlock(B, sb, vb);
                    if (A[ta].Kind != B[tb].Kind || A[ta].Index != B[tb].Index || va - A[ta].Start != vb - B[tb].Start)
                        bad.Add($"{a.Kind} {a.Index} +0x{f:x}: points at {A[ta].Kind} {A[ta].Index}+{va - A[ta].Start} vs {B[tb].Kind} {B[tb].Index}+{vb - B[tb].Start}");
                }
                for (int k = 0; k < 8; k++) { da[f + k] = 0; db[f + k] = 0; }
            }
            if (!da.AsSpan().SequenceEqual(db)) bad.Add($"{a.Kind} {a.Index}: content differs");
            if (bad.Count > 20) break;
        }
        return bad;
    }

    /// <summary>A digest of what the C# network reader decodes (node types, flags, parents, children, every attrib's
    /// bytes; attribs only hold relative pointers), position-independent: equal for a relocated copy.</summary>
    public static string Digest(byte[] x)
    {
        var net = new NmbNetwork(x);
        using var sha = SHA1.Create();
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(net.NodeCount);
        foreach (var n in net.Nodes())
        {
            w.Write(n.Type); w.Write(n.Parent); w.Write(net.U16(n.Offset + 4));
            foreach (var c in n.Children) w.Write(c);
            int ncp = x[n.Offset + 0x0E]; long pCP = (long)net.U64(n.Offset + 0x28);
            for (int k = 0; k < ncp && pCP != 0; k++) w.Write(net.U32(n.Offset + (int)pCP + 4 * k));
            foreach (var a in n.Attribs) { w.Write(a.Type); w.Write(a.Size); w.Write(x, a.Offset, (int)a.Size); }
        }
        foreach (int hp in new[] { 0xB0, 0xB8, 0xC0 })
        {
            int o = (int)net.U64(hp);
            foreach (var id in IdList(x[o..])) w.Write(id);
        }
        return Convert.ToHexString(sha.ComputeHash(ms.ToArray()));
    }

    /// <summary>Byte-exact round trips of one network bundle (boss-nmb-net-roundtrip): (identity re-emit, relocated copy ok,
    /// node blocks re-emitted identically / total, SM nodes identically / total, first problem).</summary>
    public static (bool Same, bool Moved, int NodesOk, int Nodes, int SmOk, int Sms, string? First) RoundTrip(byte[] x)
    {
        var items = BuildItems(x);
        bool same = Emit(items).AsSpan().SequenceEqual(x);
        var moved = Emit(items, 48);
        var rel = RelocationCheck(x, moved);
        bool movedOk = rel.Count == 0 && Digest(x) == Digest(moved);
        string? first = rel.FirstOrDefault();
        int nodesOk = 0, nodes = 0, smOk = 0, sms = 0;
        var net = new NmbNetwork(x);
        var B = Layout(x);
        foreach (var b in B.Where(b => b.Kind == "node"))
        {
            var orig = x.AsSpan(b.Start, b.End - b.Start);
            var w = WriteNode(ReadNode(x, b.Start, b.End));
            bool ok = w.Length <= orig.Length && orig[..w.Length].SequenceEqual(w) && orig[w.Length..].IndexOfAnyExcept((byte)0xCD) < 0;
            nodes++; if (ok) nodesOk++; else first ??= $"node {b.Index} (type {BinaryPrimitives.ReadUInt32LittleEndian(orig)}) writer differs";
            if (BinaryPrimitives.ReadUInt32LittleEndian(orig) == 10)
            {
                var s = WriteSmNode(ReadSmNode(x, b.Start, b.End));
                bool sok = s.AsSpan().SequenceEqual(orig);
                sms++; if (sok) smOk++; else first ??= $"SM node {b.Index} writer differs";
            }
        }
        return (same, movedOk, nodesOk, nodes, smOk, sms, first);
    }
}
