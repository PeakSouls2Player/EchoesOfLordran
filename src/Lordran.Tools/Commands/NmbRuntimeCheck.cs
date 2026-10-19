using System.Buffers.Binary;
using Lordran.Tools.Formats.Morpheme;

namespace Lordran.Tools.Commands;

/// <summary>
/// <c>nmb-runtime-check FILE|DIR... [--max N] [--quiet]</c> (session 9, track enemyfix): replays the bounds and invariants the DS2
/// Morpheme runtime relies on when it instantiates and updates a network, on NMB files or anibnds (own reverse engineering of
/// decomp\ds2s\DarkSoulsII.c; no NaturalMotion code). Every rule is 0 on all 173 vanilla DS2 networks; a violation in one of
/// ours is a network the runtime may read out of its tables with.
/// <para><b>Runtime model</b> (decompile): network instance init FUN_1405ecfc0: node bins (0x30 per node, FUN_1405ed330),
/// output-CP slots (0x20 × NodeDef+0x0F per node), NodeConnections per node (FUN_1405ed4b0: active-children array of
/// NodeDef+0x0C entries, active parent = NodeDef+0x08), all sized from NetworkDef+0x90; the task queue FUN_1405f8ed0(queue,
/// taskId, nodeId, ...) walks bin[nodeId]+0x18 (the node's queued tasks). A bin's task list is reset only when the node is
/// visited by the start-of-frame walk over the active tree (FUN_1405eecc0 → FUN_1405eed40) or deleted (FUN_1405ec3c0); the
/// active parent (connections+2) of a node that is not a state / transition of a state machine is never changed after init
/// (only the SM update FUN_1405fac70 writes it), so parent-sourced semantics (UPDATE_TIME_POS = semantic 0, pass-through task
/// 0xd8 = 0xd8 + semantic, FUN_1405f7010; queued by FUN_1405fb790 via FUN_1405fb690) walk the DEF parent. A def parent that
/// is not active whenever the node is gives a stale task list → access violation in FUN_1405f8ed0 (runs s9-6 / s9-7).</para>
/// <para>Rules (code: what): H = header / tables, N = node blocks, S = state machines (attrib 38) and their conditions,
/// D = def-parent structure, B = rig-sized data, A = anim leaves. W = warnings (not violations; DS2's own data has them).</para>
/// </summary>
public static class NmbRuntimeCheck
{
    public static readonly (string Code, string What)[] Rules =
    [
        ("H1", "node table: NetworkDef+0x90 nodes, every node block's id (+0x06) == its index, node 0 = the network (type 9) at offset 0, no other type-9 node"),
        ("H2", "string tables: node-name ids < node count, request-name ids < request count (+0xF8)"),
        ("H3", "per-node table (+0xE0) count == node count"),
        ("H4", "control-parameter count (+0x11C) == number of CP nodes (types 20-25)"),
        ("H5", "function-table indices: NodeDef+0x38 < task-table count (+0xE8), +0x3A < output-CP-table count (+0xF0)"),
        ("H6", "semantic lookup (+0x108/+0x110, one table per node type): a node with attrib handles has its type's table; every slot the table maps (×anim sets) < the node's handle count"),
        ("H7", "id lists: A (+0xA8: output fn 0 called) ids < nodes with ≥ 1 output pin; state machines (+0xB0) == the type-10 nodes; emitters (+0xB8: output fn [pins−1] called) ids < nodes with ≥ 1 pin; multiply-connected (+0xC0) == the flag-0x80 nodes; no duplicates"),
        ("H8", "request distributors: count == +0xF8, distributor k has request id k (the runtime indexes by id), listed nodes < node count and state machines"),
        ("N1", "def parent (+0x08) < node count or 0xFFFF"),
        ("N2", "children < node count or 0xFFFF, never the node itself"),
        ("N3", "max active children (+0x0C, sizes the NodeConnections array): state machine 1, transition 2 with 2 children, BlendN / Switch / 133 ≤ children, every other type == children"),
        ("N4", "input-CP connections: source < node count or 0xFFFF (unconnected), source pin < the source's output-pin count (+0x0F, sizes its output-CP slots)"),
        ("N5", "on-enter messages (+0x80 count, +0x88 records): request ids < request count"),
        ("S1", "state machine: one record per SM child, record k = child k; default state < states and not a transition"),
        ("S2", "state records: node < node count, def parent == the SM"),
        ("S3", "transition records: source state < states or 0xFFFF (any), destination < states, both plain states; the transit node's children == [source node | 0xFFFF, destination node]"),
        ("S4", "record lists: entry / exit condition indices < conditions; exit transitions < states, transitions from that state (or from any)"),
        ("S5", "conditions: known type; request (601) < request count and its distributor lists the SM; CP conditions (606/608/623-627) name a CP node; node references (602/603/610/611/617/618) < node count or 0xFFFF, semantic < 74"),
        ("S6", "condition node references are active when evaluated: inside the transition's source state (def subtree), or inside the SM for transitions from any state"),
        ("S7", "attrib 59 sub-state paths: every record names a state machine inside the transition's SM and one of its state nodes"),
        ("D1", "def parent dominates every referrer: the def parent of a node is an ancestor-or-self (def-parent chain) of every node that lists it as a child (the runtime's active parent of a non-SM-child is its def parent: crash rule)"),
        ("D2", "a node listed as a child by ≥ 2 nodes other than transitions carries flag 0x80 (multiply connected: not deleted when one parent drops it)"),
        ("D3", "a node listed as a child by ≥ 2 nodes other than transitions has its def parent strictly above the lowest common ancestor of those nodes (vanilla: 2449/2449)"),
        ("D4", "def-parent chains end at node 0 (no cycles) for every node with a def parent"),
        ("B1", "rig bone count: NetworkDef+0x118 == the rig bundle's bones; attrib 6 (node 0) and 84 per-bone counts == bones"),
        ("B2", "joint fields (attribs 25/29/31/34/73): indices < bones (or −1)"),
        ("B3", "mirror mapping (attrib 66): every bone exactly once, indices < bones"),
        ("B4", "bone-order tables (+0x130/+0x138): per anim set a permutation of [0, bones)"),
        ("A1", "SourceAnim (21): clip index < file-name entries, channel-map assetId = a channel-map bundle, sync track −1 or < discrete tracks"),
        ("A2", "SourceEventTracks (23): every track id is an event-track bundle of its group's type, size field == the bundle size"),
        ("W1", "warning: per-bone float arrays (attribs 87 / 9) whose count != bones (DS2 has 16: c1050, c6820, c6830)"),
        ("W2", "warning: bone lists (attrib 8) with an index >= bones (DS2 has 9: c1021, c3080)"),
    ];

    static readonly HashSet<int> CpTypes = [20, 21, 22, 23, 24, 25];

    public sealed class Result
    {
        public string Name = "";
        public int Nodes;
        public Dictionary<string, int> Counts = [];
        public Dictionary<string, List<string>> Samples = [];
        public int Violations => Counts.Where(kv => !kv.Key.StartsWith('W')).Sum(kv => kv.Value);
        public int Warnings => Counts.Where(kv => kv.Key.StartsWith('W')).Sum(kv => kv.Value);
        public int MaxSamples = 5;
        public void Bad(string code, string what)
        {
            Counts[code] = Counts.GetValueOrDefault(code) + 1;
            if (!Samples.TryGetValue(code, out var l)) Samples[code] = l = [];
            if (l.Count < MaxSamples) l.Add(what);
        }
    }

    static ushort U16(byte[] x, int o) => BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(o));
    static uint U32(byte[] x, int o) => BinaryPrimitives.ReadUInt32LittleEndian(x.AsSpan(o));
    static int I32(byte[] x, int o) => BinaryPrimitives.ReadInt32LittleEndian(x.AsSpan(o));
    static ulong U64(byte[] x, int o) => BinaryPrimitives.ReadUInt64LittleEndian(x.AsSpan(o));

    static List<int> IdList(byte[] x, int o)
    {
        int n = (int)U32(x, o); long p = (long)U64(x, o + 8);
        var l = new List<int>(n);
        for (int i = 0; i < n; i++) l.Add(U16(x, o + (int)p + 2 * i));
        return l;
    }

    static List<uint> StrTabIds(byte[] x, int o)
    {
        int n = (int)U32(x, o); long pI = (long)U64(x, o + 8);
        var l = new List<uint>(n);
        for (int i = 0; i < n; i++) l.Add(U32(x, o + (int)pI + 4 * i));
        return l;
    }

    sealed class NodeInfo
    {
        public int Index, Offset, Type, Parent, Flags, MaxActive, Pins, Handles, TaskIdx, CpTaskIdx, Msgs;
        public ushort[] Children = [];
        public List<(int Src, int Pin)> Cps = [];
        public List<NmbNetwork.Attrib> Attribs = [];
    }

    /// <summary>Check one NMB file (all its bundles).</summary>
    public static Result Check(byte[] nmbBytes, string name, int maxSamples = 5)
    {
        var r = new Result { Name = name, MaxSamples = maxSamples };
        var nmb = NmbFile.Read(nmbBytes);
        var x = nmb.Bundles[nmb.IndexOf(10)].Data;
        int nn = (int)U32(x, 0x90);
        r.Nodes = nn;
        int nSets = U16(x, 0xA0);
        int nReq = (int)U32(x, 0xF8);
        long pTab = (long)U64(x, 0x98);

        // ---------------------------------------------------------------- H1 + node models
        var nodes = new NodeInfo?[nn];
        for (int i = 0; i < nn; i++)
        {
            long p = (long)U64(x, (int)pTab + 8 * i);
            if (i > 0 && p == 0) { r.Bad("H1", $"node {i}: null node-table entry"); continue; }
            if (p < 0 || p + 0x90 > x.Length) { r.Bad("H1", $"node {i}: block 0x{p:X} outside the bundle"); continue; }
            int o = (int)p;
            var n = new NodeInfo
            {
                Index = i, Offset = o, Type = (int)U32(x, o), Flags = U16(x, o + 4), Parent = U16(x, o + 8), MaxActive = U16(x, o + 0x0C),
                Pins = x[o + 0x0F], Handles = U16(x, o + 0x10), TaskIdx = U16(x, o + 0x38), CpTaskIdx = U16(x, o + 0x3A), Msgs = x[o + 0x80],
            };
            if (U16(x, o + 6) != i) r.Bad("H1", $"node {i}: id field {U16(x, o + 6)}");
            if (i == 0 && (p != 0 || n.Type != 9)) r.Bad("H1", "node 0 is not the network block at offset 0");
            if (i > 0 && n.Type == 9) r.Bad("H1", $"node {i}: a second network node");
            int nch = U16(x, o + 0x0A); long pCh = (long)U64(x, o + 0x20);
            n.Children = new ushort[pCh != 0 ? nch : 0];
            for (int k = 0; k < n.Children.Length; k++) n.Children[k] = U16(x, o + (int)pCh + 2 * k);
            int ncp = x[o + 0x0E]; long pCp = (long)U64(x, o + 0x28);
            if (pCp != 0) for (int k = 0; k < ncp; k++) n.Cps.Add((U16(x, o + (int)pCp + 4 * k), U16(x, o + (int)pCp + 4 * k + 2)));
            long pAh = (long)U64(x, o + 0x30);
            for (int k = 0; k < n.Handles && pAh != 0; k++)
            {
                int h = o + (int)pAh + 0x18 * k;
                long pa = (long)U64(x, h), sz = (long)U64(x, h + 8);
                if (pa == 0) continue;
                n.Attribs.Add(new NmbNetwork.Attrib(o + (int)pa, sz, U16(x, o + (int)pa + 10)));
            }
            nodes[i] = n;
        }
        var N = nodes.Where(n => n != null).Select(n => n!).ToList();
        NodeInfo? Node(int i) => i >= 0 && i < nn ? nodes[i] : null;

        // ---------------------------------------------------------------- H2 string tables
        foreach (uint id in StrTabIds(x, (int)U64(x, 0xC8))) if (id >= nn) r.Bad("H2", $"node-name id {id} >= {nn}");
        foreach (uint id in StrTabIds(x, (int)U64(x, 0xD0))) if (id >= nReq) r.Bad("H2", $"request-name id {id} >= {nReq}");

        // ---------------------------------------------------------------- H3 per-node table
        int pPer = (int)U64(x, 0xE0);
        if (U16(x, pPer + 0x1A) != nn) r.Bad("H3", $"per-node table count {U16(x, pPer + 0x1A)} != {nn}");

        // ---------------------------------------------------------------- H4 CP count
        int cps = N.Count(n => CpTypes.Contains(n.Type));
        if (U32(x, 0x11C) != cps) r.Bad("H4", $"+0x11C = {U32(x, 0x11C)}, CP nodes {cps}");

        // ---------------------------------------------------------------- H5 function tables
        int nTask = (int)U32(x, (int)U64(x, 0xE8)), nCpTask = (int)U32(x, (int)U64(x, 0xF0));
        foreach (var n in N)
            if (n.TaskIdx >= nTask || n.CpTaskIdx >= nCpTask) r.Bad("H5", $"node {n.Index} (type {n.Type}): task table {n.TaskIdx}/{nTask}, output-CP table {n.CpTaskIdx}/{nCpTask}");

        // ---------------------------------------------------------------- H6 semantic lookup tables
        int nl = (int)U32(x, 0x108), pL = (int)U64(x, 0x110);
        var luts = new Dictionary<int, (int Stride, byte[] Slots)>();
        for (int k = 0; k < nl; k++)
        {
            int p = (int)U64(x, pL + 8 * k);
            int ty = I32(x, p); int stride = x[p + 4]; int nsem = U16(x, p + 6); long pp = (long)U64(x, p + 8);
            if (luts.ContainsKey(ty)) r.Bad("H6", $"two lookup tables for node type {ty}");
            luts[ty] = (stride, x[(p + (int)pp)..(p + (int)pp + nsem)]);
        }
        foreach (var n in N)
        {
            if (!luts.TryGetValue(n.Type, out var lut))
            {
                if (n.Handles > 0) r.Bad("H6", $"node {n.Index} (type {n.Type}): {n.Handles} attrib handles, no lookup table for its type");
                continue;
            }
            int maxSlot = lut.Slots.Where(s => s != 0xFF).Select(s => (int)s).DefaultIfEmpty(-1).Max();
            if (maxSlot < 0) continue;
            // anim set 0 (FUN_1405eb860: handle = stride × set + slot, set 0xFFFF → 0); which semantics are per anim set is not
            // modelled (only the player network has 2 sets)
            int need = maxSlot + 1;
            if (need > n.Handles) r.Bad("H6", $"node {n.Index} (type {n.Type}): lookup slots need {need} handles, has {n.Handles}");
        }

        // ---------------------------------------------------------------- H7 id lists
        var listA = IdList(x, (int)U64(x, 0xA8));
        var smList = IdList(x, (int)U64(x, 0xB0));
        var emit = IdList(x, (int)U64(x, 0xB8));
        var mcList = IdList(x, (int)U64(x, 0xC0));
        foreach (int id in listA) if (Node(id) is not { Pins: >= 1 }) r.Bad("H7", $"list A id {id}: out of range or no output pin");
        foreach (int id in emit) if (Node(id) is not { Pins: >= 1 }) r.Bad("H7", $"emitter id {id}: out of range or no output pin");
        var sms = N.Where(n => n.Type == 10).Select(n => n.Index).ToHashSet();
        if (smList.Count != smList.Distinct().Count() || !smList.ToHashSet().SetEquals(sms)) r.Bad("H7", $"state-machine list ({smList.Count} ids) != the {sms.Count} type-10 nodes");
        var flagged = N.Where(n => (n.Flags & 0x80) != 0).Select(n => n.Index).ToHashSet();
        if (mcList.Count != mcList.Distinct().Count() || !mcList.ToHashSet().SetEquals(flagged))
            r.Bad("H7", $"multiply-connected list ({mcList.Count} ids) != the {flagged.Count} flag-0x80 nodes (missing {string.Join(",", flagged.Except(mcList).Take(6))}; extra {string.Join(",", mcList.Except(flagged).Take(6))})");
        if (emit.Count != emit.Distinct().Count()) r.Bad("H7", "duplicate emitter ids");

        // ---------------------------------------------------------------- H8 distributors
        int pM = (int)U64(x, 0x100);
        var dist = new Dictionary<int, HashSet<int>>();
        for (int k = 0; k < nReq; k++)
        {
            int p = (int)U64(x, pM + 8 * k);
            if (p == 0) { r.Bad("H8", $"distributor {k} null"); continue; }
            int id = (int)U32(x, p); int cnt = (int)U32(x, p + 8); long p1 = (long)U64(x, p + 0x10);
            if (id != k) r.Bad("H8", $"distributor {k} has request id {id}");
            var set = new HashSet<int>();
            for (int j = 0; j < cnt; j++)
            {
                int nd = U16(x, p + (int)p1 + 2 * j); set.Add(nd);
                if (Node(nd) is not { Type: 10 }) r.Bad("H8", $"distributor {k} lists node {nd} (not a state machine)");
            }
            dist[k] = set;
        }

        // ---------------------------------------------------------------- N1-N5
        foreach (var n in N)
        {
            if (n.Parent != 0xFFFF && n.Parent >= nn) r.Bad("N1", $"node {n.Index}: parent {n.Parent}");
            foreach (int c in n.Children)
                if (c != 0xFFFF && (c >= nn || c == n.Index)) r.Bad("N2", $"node {n.Index}: child {c}");
            int nch = n.Children.Length;
            bool okMax = n.Type switch
            {
                10 => n.MaxActive == 1,
                400 or 402 => n.MaxActive == 2 && nch == 2,
                108 or 131 or 133 => n.MaxActive <= nch,
                _ => n.MaxActive == nch,
            };
            if (!okMax) r.Bad("N3", $"node {n.Index} (type {n.Type}): max active {n.MaxActive}, children {nch}");
            foreach (var (src, pin) in n.Cps)
            {
                if (src == 0xFFFF) continue;
                if (Node(src) is not { } s) { r.Bad("N4", $"node {n.Index}: CP source {src} out of range"); continue; }
                if (pin >= s.Pins) r.Bad("N4", $"node {n.Index}: CP source {src} pin {pin} >= its {s.Pins} output pins");
            }
            if (n.Msgs > 0)
            {
                long pm = (long)U64(x, n.Offset + 0x88);
                for (int k = 0; k < n.Msgs; k++)
                {
                    uint mid = U32(x, n.Offset + (int)pm + 8 * k + 4);
                    if (x[n.Offset + (int)pm + 8 * k] == 0 && mid >= nReq) r.Bad("N5", $"node {n.Index}: on-enter request {mid} >= {nReq}");
                }
            }
        }

        // ---------------------------------------------------------------- def-parent helpers
        bool Under(int a, int b)   // a in the def subtree of b (a == b included)
        {
            int k = a, guard = 0;
            while (k != 0xFFFF && k < nn && guard++ <= nn)
            {
                if (k == b) return true;
                k = nodes[k]?.Parent ?? 0xFFFF;
            }
            return false;
        }
        List<int> Chain(int a)
        {
            var l = new List<int>(); int k = a, guard = 0;
            while (k != 0xFFFF && k < nn && guard++ <= nn) { l.Add(k); k = nodes[k]?.Parent ?? 0xFFFF; }
            return l;
        }

        // ---------------------------------------------------------------- S1-S7
        var condSize = NmbNetEdit.CondSize;
        foreach (var n in N.Where(n => n.Type == 10))
        {
            var a38 = n.Attribs.FirstOrDefault(a => a.Type == 38);
            if (a38 == null) { r.Bad("S1", $"SM {n.Index}: no definition attrib"); continue; }
            NmbNetEdit.SmDef sm;
            try { sm = NmbNetEdit.ReadSmDef(x, a38.Offset); }
            catch (Exception e) { r.Bad("S1", $"SM {n.Index}: definition unreadable ({e.GetType().Name})"); continue; }
            int ns = sm.States.Count, nc = sm.Conds.Count;
            bool IsTr(int si) => si >= 0 && si < ns && Node(sm.States[si].Node) is { Type: 400 or 402 };
            if (ns != n.Children.Length || !sm.States.Select(s => (int)s.Node).SequenceEqual(n.Children.Select(c => (int)c)))
                r.Bad("S1", $"SM {n.Index}: records [{ns}] != children [{n.Children.Length}]");
            if (sm.Default >= ns || IsTr(sm.Default)) r.Bad("S1", $"SM {n.Index}: default state {sm.Default}");
            var conds = new List<(int Type, int A, int B, int Sem, int Sem2, uint Req, int Cp)>();
            foreach (var c in sm.Conds)
            {
                int t = U16(c, 0);
                if (!condSize.ContainsKey(t)) { r.Bad("S5", $"SM {n.Index}: condition type {t}"); conds.Add((t, -1, -1, 0, 0, 0, -1)); continue; }
                int a = -1, b = -1, sem = 0, sem2 = 0, cp = -1; uint req = 0;
                if (t == 601) req = U32(c, 0x48);
                else if (t is 602 or 603 or 610 or 618) { a = U16(c, 0x48); sem = U16(c, 0x4C); }
                else if (t == 611) { a = U16(c, 0x48); sem = U16(c, 0x4C); b = U16(c, 0x54); sem2 = U16(c, 0x58); }
                else if (t == 617) a = U16(c, 0x48);
                else if (t is 606 or 608 or 623 or 624 or 625 or 627) cp = U16(c, 0x48);
                conds.Add((t, a, b, sem, sem2, req, cp));
                if (t == 601 && (req >= nReq || !dist.TryGetValue((int)req, out var dl) || !dl.Contains(n.Index))) r.Bad("S5", $"SM {n.Index}: request {req} (count {nReq}) not distributed to it");
                if (cp >= 0 && Node(cp) == null) r.Bad("S5", $"SM {n.Index}: CP condition on node {cp} (out of range)");
                else if (cp >= 0 && !CpTypes.Contains(Node(cp)!.Type)) r.Bad("S5", $"SM {n.Index}: CP condition on node {cp} (type {Node(cp)!.Type})");
                foreach (var (refNode, s) in new[] { (a, sem), (b, sem2) })
                {
                    if (refNode < 0 || refNode == 0xFFFF) continue;
                    if (refNode >= nn) r.Bad("S5", $"SM {n.Index}: condition {t} on node {refNode} (out of range)");
                    else if (s >= 74) r.Bad("S5", $"SM {n.Index}: condition {t} semantic {s}");
                }
            }
            for (int si = 0; si < ns; si++)
            {
                var st = sm.States[si];
                if (st.Node >= nn) { r.Bad("S2", $"SM {n.Index} record {si}: node {st.Node}"); continue; }
                if (Node(st.Node) is { } rn && rn.Parent != n.Index) r.Bad("S2", $"SM {n.Index} record {si}: node {st.Node} has def parent {rn.Parent}");
                foreach (int ci in st.Entry.Concat(st.ExitConds)) if (ci >= nc) r.Bad("S4", $"SM {n.Index} record {si}: condition index {ci} >= {nc}");
                foreach (int ti in st.ExitTrans)
                    if (!IsTr(ti) || sm.States[ti].Src != si && sm.States[ti].Src != 0xFFFF) r.Bad("S4", $"SM {n.Index} state {si}: exit transition {ti}");
                if (!IsTr(si)) continue;
                var tn = Node(st.Node)!;
                bool srcOk = st.Src == 0xFFFF || st.Src < ns && !IsTr(st.Src);
                bool dstOk = st.Dst < ns && !IsTr(st.Dst);
                if (!srcOk || !dstOk) { r.Bad("S3", $"SM {n.Index} transition {si}: source {st.Src}, destination {st.Dst}"); continue; }
                int srcNode = st.Src == 0xFFFF ? 0xFFFF : sm.States[st.Src].Node, dstNode = sm.States[st.Dst].Node;
                if (tn.Children.Length != 2 || tn.Children[0] != srcNode || tn.Children[1] != dstNode)
                    r.Bad("S3", $"SM {n.Index} transition node {st.Node}: children [{string.Join(",", tn.Children)}] != [{srcNode},{dstNode}]");
                // S6: the nodes a condition reads must be active while the transition is evaluated
                foreach (int ci in st.Entry)
                {
                    if (ci >= conds.Count) continue;
                    var c = conds[ci];
                    foreach (int refNode in new[] { c.A, c.B })
                    {
                        if (refNode < 0 || refNode == 0xFFFF || refNode >= nn) continue;
                        bool ok = st.Src == 0xFFFF ? Under(refNode, n.Index) : Under(refNode, srcNode);
                        if (!ok) r.Bad("S6", $"SM {n.Index} transition node {st.Node} ({(st.Src == 0xFFFF ? "any" : srcNode.ToString())} → {dstNode}): condition {c.Type} reads node {refNode}, outside {(st.Src == 0xFFFF ? $"SM {n.Index}" : $"source state {srcNode}")}");
                    }
                }
                // S7: attrib 59 sub-state path
                foreach (var a59 in tn.Attribs.Where(a => a.Type == 59))
                {
                    int A = a59.Offset; long q = (long)U64(x, A + 0x30);
                    if (q == 0) continue;
                    long p0 = (long)U64(x, A + (int)q); int cnt = U16(x, A + (int)q + 8);
                    for (int k = 0; k < cnt; k++)
                    {
                        long rr = (long)U64(x, A + (int)q + (int)p0 + 8 * k);
                        int smId = U16(x, A + (int)q + (int)rr + 2), stNode = U16(x, A + (int)q + (int)rr + 4);
                        var sn = Node(smId);
                        bool okRec = sn is { Type: 10 } && sn.Children.Contains((ushort)stNode) && Node(stNode) is { Type: not (400 or 402) };
                        if (!okRec) r.Bad("S7", $"transition node {st.Node}: sub-state record SM {smId} state node {stNode}");
                        else if (!Under(smId, n.Index)) r.Bad("S7", $"transition node {st.Node}: sub-state SM {smId} outside SM {n.Index}");
                    }
                }
            }
            foreach (int ci in sm.Glob.Entry.Concat(sm.Glob.ExitConds)) if (ci >= nc) r.Bad("S4", $"SM {n.Index} global record: condition index {ci}");
            foreach (int ti in sm.Glob.ExitTrans) if (!IsTr(ti) || sm.States[ti].Src != 0xFFFF) r.Bad("S4", $"SM {n.Index} global record: exit transition {ti}");
        }

        // ---------------------------------------------------------------- D1-D4
        var refs = new Dictionary<int, List<int>>();
        foreach (var n in N)
            foreach (int c in n.Children)
                if (c != 0xFFFF && c < nn) { if (!refs.TryGetValue(c, out var l)) refs[c] = l = []; if (!l.Contains(n.Index)) l.Add(n.Index); }
        foreach (var n in N)
        {
            if (n.Parent == 0xFFFF || n.Parent >= nn) continue;
            var ch = Chain(n.Index);
            if (ch[^1] != 0) r.Bad("D4", $"node {n.Index}: def-parent chain ends at {ch[^1]} ({(ch.Count > nn ? "cycle" : "not node 0")})");
        }
        foreach (var (c, rs) in refs)
        {
            var cn = Node(c);
            if (cn == null) continue;
            foreach (int rf in rs)
                if (cn.Parent == 0xFFFF || !Under(rf, cn.Parent))
                    r.Bad("D1", $"node {c} (type {cn.Type}, def parent {cn.Parent}) is a child of node {rf}, whose def-parent chain does not pass through {cn.Parent}");
            var nonTr = rs.Where(rf => Node(rf) is not { Type: 400 or 402 }).ToList();
            if (nonTr.Count < 2) continue;
            if ((cn.Flags & 0x80) == 0) r.Bad("D2", $"node {c} (type {cn.Type}) has {nonTr.Count} parents [{string.Join(",", nonTr.Take(6))}] but no flag 0x80");
            var chains = nonTr.Select(Chain).ToList();
            var common = chains[0].Where(a => chains.Skip(1).All(l => l.Contains(a))).ToList();
            int lca = common.Count > 0 ? common[0] : -1;
            if (lca < 0 || cn.Parent == lca || !common.Contains(cn.Parent))
                r.Bad("D3", $"node {c} (type {cn.Type}): def parent {cn.Parent} not strictly above the parents' lowest common ancestor {lca} (parents [{string.Join(",", nonTr.Take(6))}])");
        }

        // ---------------------------------------------------------------- B1-B4, W1
        int bones = (int)U32(x, 0x118);
        int ri = nmb.IndexOf(1);
        if (ri < 0) r.Bad("B1", "no rig bundle");
        else
        {
            try
            {
                int rigBones = NmbRig.Read(nmb.Bundles[ri].Data).Names.Count;
                if (rigBones != bones) r.Bad("B1", $"+0x118 = {bones}, rig bundle {rigBones} bones");
            }
            catch (Exception e) { r.Bad("B1", $"rig bundle unreadable ({e.GetType().Name})"); }
        }
        foreach (var n in N)
            foreach (var a in n.Attribs)
            {
                int o = a.Offset;
                switch (a.Type)
                {
                    case 6: if (U32(x, o + 0x10) != bones) r.Bad("B1", $"node {n.Index} attrib 6: {U32(x, o + 0x10)} entries for {bones} bones"); break;
                    case 84: if (U32(x, o + 0x10) != bones) r.Bad("B1", $"node {n.Index} attrib 84: {U32(x, o + 0x10)} bones for {bones}"); break;
                    case 87: if (U16(x, o + 0x12) != bones) r.Bad("W1", $"node {n.Index} attrib 87: {U16(x, o + 0x12)} alphas for {bones} bones"); break;
                    case 9:
                        {
                            int cnt = (int)U32(x, o + 0x10);
                            if (cnt > 8 && cnt != bones && Math.Abs(cnt - bones) <= 4) r.Bad("W1", $"node {n.Index} attrib 9: {cnt} floats for {bones} bones");
                            break;
                        }
                    case 8:
                        {
                            int cnt = (int)U32(x, o + 0x10); long p = (long)U64(x, o + 0x18);
                            for (int k = 0; k < cnt; k++) if (U32(x, o + (int)p + 4 * k) >= bones) { r.Bad("W2", $"node {n.Index} attrib 8: bone {U32(x, o + (int)p + 4 * k)} of {bones}"); break; }
                            break;
                        }
                    case 66:
                        {
                            var m = NmbNetResize.ReadMirror(x, o);
                            var all = m.Pairs.SelectMany(q => new[] { q.A, q.B }).Concat(m.Self.Select(s => s.Bone)).Order().ToList();
                            if (!all.SequenceEqual(Enumerable.Range(0, bones))) r.Bad("B3", $"node {n.Index}: mirror covers {all.Count} entries (distinct {all.Distinct().Count()}, max {(all.Count > 0 ? all.Max() : -1)}) for {bones} bones");
                            break;
                        }
                }
                if (NmbNetResize.IndexFields.TryGetValue(a.Type, out var fields))
                    foreach (int f in fields)
                    {
                        if (o + f + 4 > x.Length) continue;
                        int v = I32(x, o + f);
                        if (v >= bones || v < -1) r.Bad("B2", $"node {n.Index} attrib {a.Type} +0x{f:X}: joint {v}");
                    }
            }
        if (nSets == 1)   // {u64 abs ptr → data, u32 bone[bones]} (one anim set: every DS2 enemy; the player's 2-set layout is not modelled)
            foreach (int hp in new[] { 0x130, 0x138 })
            {
                int o = (int)U64(x, hp);
                int pd = (int)U64(x, o);
                if (pd <= 0 || pd + 4 * bones > x.Length) { r.Bad("B4", $"bone-order table 0x{hp:X}: data pointer 0x{pd:X}"); continue; }
                var v = Enumerable.Range(0, bones).Select(k => (int)U32(x, pd + 4 * k)).Order().ToList();
                if (!v.SequenceEqual(Enumerable.Range(0, bones))) r.Bad("B4", $"bone-order table 0x{hp:X}: not a permutation of {bones} bones");
            }

        // ---------------------------------------------------------------- A1-A2
        int fi = nmb.IndexOf(13);
        int nClips = fi >= 0 ? NmbFileNamesFull.Read(nmb.Bundles[fi].Data).Files.Count : 0;
        var bundleOf = nmb.Bundles.GroupBy(b => b.AssetId).ToDictionary(g => g.Key, g => g.First());
        var groupType = new Dictionary<int, Dictionary<uint, int>>();
        var trackUses = new List<(int Node, int Group, uint Id, int SizeField)>();
        foreach (var n in N.Where(n => n.Type == 104))
        {
            var sa = n.Attribs.FirstOrDefault(a => a.Type == 21);
            var et = n.Attribs.FirstOrDefault(a => a.Type == 23);
            int nDisc = et != null ? (int)U32(x, et.Offset + 0x10) : 0;
            if (sa != null)
            {
                int clip = (int)U32(x, sa.Offset + 0x74); uint map = U32(x, sa.Offset + 0x78); int sync = I32(x, sa.Offset + 0x8C);
                if (clip >= nClips) r.Bad("A1", $"anim node {n.Index}: clip {clip} >= {nClips} file-name entries");
                if (!bundleOf.TryGetValue(map, out var mb) || mb.Type != 2) r.Bad("A1", $"anim node {n.Index}: channel map {map:X8} is not a channel-map bundle");
                if (sync != -1 && (sync < 0 || sync >= nDisc)) r.Bad("A1", $"anim node {n.Index}: sync track {sync} of {nDisc} discrete tracks");
            }
            if (et == null) continue;
            for (int g = 0; g < 3; g++)
            {
                int cnt = (int)U32(x, et.Offset + 0x10 + 0x18 * g);
                long pid = (long)U64(x, et.Offset + 0x18 + 0x18 * g), psz = (long)U64(x, et.Offset + 0x20 + 0x18 * g);
                for (int j = 0; j < cnt; j++)
                {
                    uint id = U32(x, et.Offset + (int)pid + 8 * j);
                    trackUses.Add((n.Index, g, id, psz != 0 ? et.Offset + (int)psz + 4 * j : -1));
                    if (bundleOf.TryGetValue(id, out var tb) && tb.Type is 3 or 4 or 5)
                    {
                        if (!groupType.TryGetValue(g, out var d)) groupType[g] = d = [];
                        d[tb.Type] = d.GetValueOrDefault(tb.Type) + 1;
                    }
                }
            }
        }
        foreach (var (node, g, id, sf) in trackUses)
        {
            if (!bundleOf.TryGetValue(id, out var tb) || tb.Type is not (3 or 4 or 5)) { r.Bad("A2", $"anim node {node}: track {id:X8} (group {g}) is not an event-track bundle"); continue; }
            uint want = groupType.TryGetValue(g, out var gt) ? gt.OrderByDescending(kv => kv.Value).First().Key : tb.Type;
            if (tb.Type != want) r.Bad("A2", $"anim node {node}: track {id:X8} in group {g} is bundle type {tb.Type} (group type {want})");
            if (sf >= 0 && U32(x, sf) != tb.Data.Length) r.Bad("A2", $"anim node {node}: track {id:X8} size field {U32(x, sf)} != bundle size {tb.Data.Length}");
        }
        return r;
    }

    static IEnumerable<string> Inputs(IEnumerable<string> args)
    {
        foreach (var p in args)
        {
            if (Directory.Exists(p))
                foreach (var f in Directory.GetFiles(p, "*", SearchOption.TopDirectoryOnly)
                             .Where(f => f.EndsWith(".anibnd.dcx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(f => f, StringComparer.Ordinal))
                    yield return f;
            else yield return p;
        }
    }

    public static int Run(string[] args)
    {
        int max = args.SkipWhile(a => a != "--max").Skip(1).Select(int.Parse).FirstOrDefault(5);
        bool quiet = args.Contains("--quiet");
        var pos = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--max") { i++; continue; }
            if (args[i].StartsWith("--")) continue;
            pos.Add(args[i]);
        }
        if (pos.Count == 0)
        {
            Console.Error.WriteLine("usage: nmb-runtime-check FILE|DIR... [--max N] [--quiet]");
            foreach (var (c, w) in Rules) Console.Error.WriteLine($"  {c}: {w}");
            return 1;
        }
        int files = 0, clean = 0, nodes = 0;
        var tot = new Dictionary<string, int>();
        foreach (var path in Inputs(pos))
        {
            files++;
            Result res;
            try { res = Check(NetTools.NmbOf(path), Path.GetFileName(path), max); }
            catch (Exception e) { Console.WriteLine($"{Path.GetFileName(path)}: UNREADABLE {e.GetType().Name}: {e.Message}"); tot["read"] = tot.GetValueOrDefault("read") + 1; continue; }
            nodes += res.Nodes;
            foreach (var (k, v) in res.Counts) tot[k] = tot.GetValueOrDefault(k) + v;
            if (res.Violations == 0) clean++;
            if (res.Violations > 0 || !quiet)
            {
                Console.WriteLine($"{res.Name}: {res.Nodes} nodes, {res.Violations} violations{(res.Warnings > 0 ? $", {res.Warnings} warnings" : "")}"
                    + (res.Counts.Count > 0 ? " [" + string.Join(" ", res.Counts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}")) + "]" : ""));
                foreach (var (code, l) in res.Samples.OrderBy(kv => kv.Key))
                    if (!code.StartsWith('W') || !quiet)
                        foreach (var s in l) Console.WriteLine($"    {code} {s}");
            }
        }
        Console.WriteLine($"{files} networks ({nodes} nodes): {clean} without violations; totals: "
            + (tot.Count == 0 ? "none" : string.Join(" ", tot.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"))));
        Console.WriteLine("rules: " + string.Join(", ", Rules.Select(r => r.Code)) + " (nmb-runtime-check without arguments prints them)");
        return files > 0 && clean == files ? 0 : 2;
    }
}
