using System.Buffers.Binary;
using System.Text;

namespace Lordran.Tools.Formats.Morpheme.Boss;

/// <summary>
/// Structural edits of a DS2 Morpheme network (session 8, track boss): the C# port of data\research\morpheme\net_clone.py
/// (add_category, widen_band, add_clips, the file-name-table writer) plus what the 1:1 boss port needs beyond it (per-leaf
/// event tracks of their own, state-machine queries). Own code (NETWORK-FORMAT.md §2-§3); every edit goes through the block
/// model (<see cref="NmbNetBlocks"/>), so node ids, tables and pointers stay consistent (checked by <see cref="Check"/>).
/// </summary>
public static class NmbNetEdit
{
    static ushort H(byte[] b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o));
    static uint U(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    static ulong Q(byte[] b, int o) => BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(o));
    static void PutH(byte[] b, int o, int v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), (ushort)v);
    static void PutF(byte[] b, int o, float v) => BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(o), v);

    /// <summary>Conditions whose payload names a node (patched when nodes are cloned): 602/603/610/611/617/618.</summary>
    static readonly HashSet<int> NodeConds = [602, 603, 610, 611, 617, 618];

    // ================================================================== names / queries

    /// <summary>Node names (first non-empty name per id, string table at +0xC8): CPs "ControlParameters|X", some SMs.</summary>
    public static Dictionary<int, string> NodeNames(byte[] x)
    {
        int o = (int)Q(x, 0xC8);
        var (ids, names) = NmbFile.ReadStringTable(x, o);
        var d = new Dictionary<int, string>();
        for (int i = 0; i < ids.Count; i++) if (names[i].Length > 0) d.TryAdd((int)ids[i], names[i]);
        return d;
    }

    public static string CpName(Dictionary<int, string> names, int node) =>
        names.TryGetValue(node, out var s) && s.StartsWith("ControlParameters|") ? s["ControlParameters|".Length..] : $"node{node}";

    /// <summary>Request names (string table at +0xD0) → request id.</summary>
    public static Dictionary<string, int> RequestIds(byte[] x)
    {
        var (ids, names) = NmbFile.ReadStringTable(x, (int)Q(x, 0xD0));
        var d = new Dictionary<string, int>();
        for (int i = 0; i < ids.Count; i++) d.TryAdd(names[i], (int)ids[i]);
        return d;
    }

    /// <summary>Event-track runtime ids by name (string table at +0xD8).</summary>
    public static Dictionary<string, int> TrackNameIds(byte[] x)
    {
        var (ids, names) = NmbFile.ReadStringTable(x, (int)Q(x, 0xD8));
        var d = new Dictionary<string, int>();
        for (int i = 0; i < ids.Count; i++) d.TryAdd(names[i], (int)ids[i]);
        return d;
    }

    /// <summary>The SM definition (attrib 38) of state machine node <paramref name="i"/>.</summary>
    public static NmbNetBlocks.SmDef Sm(NmbNetwork net, int i)
    {
        var a = NmbNetwork.Find(net.GetNode(i), 38) ?? throw new InvalidDataException($"node {i} is not a state machine");
        return NmbNetBlocks.ReadSmDef(net.X, a.Offset).Def;
    }

    public static int CondType(byte[] c) => H(c, 0);
    public static int CondCp(byte[] c) => H(c, 0x48);
    public static (float Lo, float Hi) CondFloatRange(byte[] c) => (BitConverter.ToSingle(c, 0x4C), BitConverter.ToSingle(c, 0x50));
    public static (int Lo, int Hi) CondIntRange(byte[] c) => (BitConverter.ToInt32(c, 0x4C), BitConverter.ToInt32(c, 0x50));

    static bool IsTransition(NmbNetwork net, int node) => node != 0xFFFF && net.GetNode(node).Type is 400 or 402;

    /// <summary>Anim leaves (type 104) under node i (children followed, transitions skipped), first-seen order.</summary>
    public static List<int> Leaves(NmbNetwork net, int i, HashSet<int>? seen = null)
    {
        seen ??= [];
        var o = new List<int>();
        if (i == 0xFFFF || !seen.Add(i)) return o;
        var n = net.GetNode(i);
        if (n.Type == 104) { o.Add(i); return o; }
        if (n.Type is 400 or 402) return o;
        foreach (var c in n.Children) o.AddRange(Leaves(net, c, seen));
        return o;
    }

    /// <summary>The weapon-0 attack categories of SM_Attack (NETWORK-FORMAT.md §3.2): category value → (band SM, category
    /// SM). Bands are found as the weapon hub's children, categories by their incoming AttackCategory_Ene [n,n] transition.</summary>
    public static Dictionary<int, (int Band, int Sm)> AttackCategories(NmbNetwork net, Dictionary<int, string> names, int weapon = 0)
    {
        int smAttack = net.Nodes().First(n => n.Type == 10 && names.GetValueOrDefault(n.Index) == "SM_Main|SM_Attack" && net.GetNode(n.Parent).Type == 10 && names.GetValueOrDefault(n.Parent) != "SM_Main|SM_Attack").Index;
        var res = new Dictionary<int, (int, int)>();
        int hub = TargetOf(net, names, smAttack, "WeaponCategory_Ene", weapon) ?? throw new InvalidDataException("no weapon hub");
        var hubSm = Sm(net, hub);
        foreach (var st in hubSm.States.Where(s => !IsTransition(net, s.Node) && net.GetNode(s.Node).Type == 10))
        {
            int band = st.Node;
            var bsm = Sm(net, band);
            foreach (var tr in bsm.States.Where(s => IsTransition(net, s.Node)))
                foreach (int ci in tr.Entry)
                {
                    var c = bsm.Conds[ci];
                    if (CondType(c) != 627 || CpName(names, CondCp(c)) != "AttackCategory_Ene") continue;
                    var (lo, hi) = CondFloatRange(c);
                    // session 11 (burgchr11): only state-machine categories (the Crystal Lizard c2130's shot category 20 is an emitter node)
                    if (lo == hi && tr.Dst != 0xCDCD && net.GetNode(bsm.States[tr.Dst].Node).Type == 10) res.TryAdd((int)lo, (band, bsm.States[tr.Dst].Node));
                }
        }
        return res;
    }

    /// <summary>Session 11 (burgchr11): the band SM of the weapon hub whose AttackCategory_Ene range starts at <paramref name="lo"/> (a
    /// band with no category of its own: the Stray Dog c2270 has an empty [0,9] band), or null.</summary>
    public static int? BandOf(NmbNetwork net, Dictionary<int, string> names, float lo, int weapon = 0)
    {
        int smAttack = net.Nodes().First(n => n.Type == 10 && names.GetValueOrDefault(n.Index) == "SM_Main|SM_Attack" && net.GetNode(n.Parent).Type == 10 && names.GetValueOrDefault(n.Parent) != "SM_Main|SM_Attack").Index;
        int hub = TargetOf(net, names, smAttack, "WeaponCategory_Ene", weapon) ?? throw new InvalidDataException("no weapon hub");
        var hsm = Sm(net, hub);
        foreach (var tr in hsm.States.Where(s => IsTransition(net, s.Node) && s.Dst != 0xCDCD))
            foreach (int ci in tr.Entry)
            {
                var c = hsm.Conds[ci];
                if (CondType(c) == 627 && CpName(names, CondCp(c)) == "AttackCategory_Ene" && CondFloatRange(c).Item1 == lo) return hsm.States[tr.Dst].Node;
            }
        return null;
    }

    /// <summary>The state an SM reaches by a transition with the CP condition [v, v] (first match), or null.</summary>
    public static int? TargetOf(NmbNetwork net, Dictionary<int, string> names, int sm, string cp, float v)
    {
        var d = Sm(net, sm);
        foreach (var tr in d.States.Where(s => IsTransition(net, s.Node)))
            foreach (int ci in tr.Entry)
            {
                var c = d.Conds[ci];
                if (CondType(c) == 627 && CpName(names, CondCp(c)) == cp && CondFloatRange(c) is var (lo, hi) && lo <= v && v <= hi && tr.Dst != 0xCDCD)
                    return d.States[tr.Dst].Node;
            }
        return null;
    }

    /// <summary>The stage states of a category SM along a combo route (NETWORK-FORMAT.md §3.2): the default state, then
    /// each state reached from the previous one by a transition testing Combo{s}_Route ∋ route (blade-dash transitions
    /// excluded). Returns (state node, the AttackStateControl value that transition waits for: 101 / 102, 0 for stage 1).</summary>
    public static List<(int State, int Value)> Stages(NmbNetwork net, Dictionary<int, string> names, int catSm, int route = 1)
    {
        var d = Sm(net, catSm);
        var o = new List<(int, int)> { (d.States[d.Default].Node, 0) };
        int cur = d.Default;
        for (int guard = 0; guard < 8; guard++)
        {
            int next = -1, value = 0;
            foreach (var tr in d.States.Where(s => IsTransition(net, s.Node) && s.Src == cur))
            {
                // conditions are ANDed; alternatives (OR) are separate transition records
                var conds = tr.Entry.Select(ci => d.Conds[ci]).ToList();
                if (conds.Any(c => CondType(c) == 627 && CpName(names, CondCp(c)) == "IsBladeDash")) continue;
                var routeCond = conds.FirstOrDefault(c => CondType(c) == 625 && CpName(names, CondCp(c)).StartsWith("Combo") && CondIntRange(c) is var (lo, hi) && lo <= route && route <= hi)
                    ?? conds.FirstOrDefault(c => CondType(c) == 627 && CpName(names, CondCp(c)).StartsWith("Combo") && CondFloatRange(c) is var (fl, fh) && fl <= route && route <= fh);
                if (routeCond == null) continue;
                var ev = conds.FirstOrDefault(c => CondType(c) == 602);
                next = tr.Dst; value = ev != null ? BitConverter.ToInt32(ev, 0x48 + 12) : 0;
                break;
            }
            if (next < 0) break;
            o.Add((d.States[next].Node, value)); cur = next;
        }
        return o;
    }

    // ================================================================== in-place condition edits

    /// <summary>Every CPFloatInRange (627) condition on <paramref name="cp"/> with exactly [lo, hi] → [newLo, newHi]
    /// (net_clone.widen_band), in place. Returns the count.</summary>
    public static int SetFloatRange(byte[] x, string cp, float lo, float hi, float newLo, float newHi)
    {
        var net = new NmbNetwork(x); var names = NodeNames(x);
        int n = 0;
        foreach (var node in net.Nodes().Where(nd => nd.Type == 10))
        {
            var a = NmbNetwork.Find(node, 38)!;
            int A = a.Offset;
            int ncond = H(x, A + 0x2A); long pC = (long)Q(x, A + 0x30);
            for (int k = 0; k < ncond; k++)
            {
                int c = A + (int)Q(x, A + (int)pC + 8 * k);
                if (H(x, c) != 627 || CpName(names, H(x, c + 0x48)) != cp) continue;
                if (BitConverter.ToSingle(x, c + 0x4C) != lo || BitConverter.ToSingle(x, c + 0x50) != hi) continue;
                PutF(x, c + 0x4C, newLo); PutF(x, c + 0x50, newHi); n++;
            }
        }
        return n;
    }

    // ================================================================== category clone (net_clone.add_category)

    /// <summary>Nodes owned by root (parent chain), DFS order, including transitions inside cloned SMs.</summary>
    static List<int> Subtree(NmbNetwork net, int root)
    {
        var o = new List<int>(); var seen = new HashSet<int>();
        void Rec(int i)
        {
            if (i == 0xFFFF || !seen.Add(i)) return;
            o.Add(i);
            foreach (var c in net.GetNode(i).Children)
                if (c != 0xFFFF && net.GetNode(c).Parent == i) Rec(c);
        }
        Rec(root);
        return o;
    }

    /// <summary>Attrib 59 destination sub-state path: +0x30 rel ptr → {u64 rel 0x10, u16 n, 6×CD, u64 rel[n]} → records
    /// {u16 0, u16 state machine, u16 its start state}; node ids remapped.</summary>
    static void PatchA59(byte[] b, int A, Func<int, int> mp)
    {
        long q = (long)Q(b, A + 0x30);
        if (q == 0) return;
        long p0 = (long)Q(b, A + (int)q); int cnt = H(b, A + (int)q + 8);
        for (int k = 0; k < cnt; k++)
        {
            long r = (long)Q(b, A + (int)q + (int)p0 + 8 * k);
            foreach (int j in new[] { 2, 4 })
            {
                int o = A + (int)(q + r) + j;
                PutH(b, o, mp(H(b, o)));
            }
        }
    }

    /// <summary>Rewrite node ids inside a node block copy (header id/parent, children, CP sources, SM records and the
    /// node references of conditions, attrib-59 paths): net_clone.patch_node_ids. <paramref name="net"/> describes the
    /// block's original node <paramref name="i"/>.</summary>
    static byte[] PatchNodeIds(NmbNetwork net, int i, byte[] blob, Dictionary<int, int> idmap)
    {
        var b = (byte[])blob.Clone();
        int Mp(int v) => idmap.TryGetValue(v, out int w) ? w : v;
        PutH(b, 0x06, Mp(H(b, 0x06)));
        int par = H(b, 0x08); if (par != 0xFFFF) PutH(b, 0x08, Mp(par));
        int nch = H(b, 0x0A), ncp = b[0x0E];
        long pCh = (long)Q(b, 0x20), pCP = (long)Q(b, 0x28);
        for (int k = 0; pCh != 0 && k < nch; k++) { int v = H(b, (int)pCh + 2 * k); if (v != 0xFFFF) PutH(b, (int)pCh + 2 * k, Mp(v)); }
        for (int k = 0; pCP != 0 && k < ncp; k++) { int v = H(b, (int)pCP + 4 * k); if (v != 0xFFFF) PutH(b, (int)pCP + 4 * k, Mp(v)); }
        var n = net.GetNode(i);
        foreach (var a in n.Attribs)
        {
            int A = a.Offset - n.Offset;
            if (a.Type == 38)
            {
                int nst = H(b, A + 0x12);
                long pSt = (long)Q(b, A + 0x18), pGl = (long)Q(b, A + 0x20);
                for (int k = 0; k <= nst; k++)
                {
                    int o = A + (int)(k < nst ? pSt + 0x30 * k : pGl);
                    int v = H(b, o); if (v != 0xFFFF) PutH(b, o, Mp(v));
                }
                int ncond = H(b, A + 0x2A); long pC = (long)Q(b, A + 0x30);
                for (int k = 0; k < ncond; k++)
                {
                    int c = A + (int)Q(b, A + (int)pC + 8 * k);
                    int t = H(b, c);
                    if (!NodeConds.Contains(t)) continue;
                    PutH(b, c + 0x48, Mp(H(b, c + 0x48)));
                    if (t == 611) PutH(b, c + 0x48 + 12, Mp(H(b, c + 0x48 + 12)));
                }
            }
            else if (a.Type == 59) PatchA59(b, A, Mp);
        }
        return b;
    }

    public sealed class CloneResult
    {
        public byte[] Network = [];
        public Dictionary<int, int> IdMap = [];
        public int Transition;
        /// <summary>Rule E2b: what the clone copied instead of sharing ("" = nothing).</summary>
        public string Copied = "";
    }

    /// <summary>
    /// Clone category SM <paramref name="templateSm"/> (and its owned subtree) as category <paramref name="newValue"/>
    /// under <paramref name="parentSm"/> (net_clone.add_category): new node ids appended, the template's incoming
    /// transition cloned with AttackCategory_Ene [new, new] from the parent's hub (its default state when the parent is
    /// another band), and every table that lists nodes updated (node pointer table, +0x90 node count, SM id list, flag-0x80
    /// id list, emitter list, per-node table, node names, request distributors). Cloned blocks keep their 16-byte phase.
    /// The cloned leaves still play the template's clips (retarget them afterwards).
    /// <para>Session 10 (chrint stage 2): rule E2b, the same as <see cref="Lordran.Tools.Formats.Morpheme.NmbNetClone.AddCategory"/>:
    /// every node the template uses from outside its own subtree whose def parent would not stay strictly above all of its
    /// users is copied with the clone (owned by the clone's band SM), so no clone shares a node with an owner that is
    /// inactive while it plays (nmb-runtime-check D1/D3, the crash in FUN_1405f8ed0).</para>
    /// </summary>
    public static CloneResult AddCategory(byte[] x, int templateSm, int newValue, int parentSm)
    {
        var net = new NmbNetwork(x); var names = NodeNames(x);
        int tparent = net.GetNode(templateSm).Parent;
        var tm = Sm(net, tparent);
        var tnodes = tm.States.Select(s => (int)s.Node).ToList();
        int tidx = tnodes.IndexOf(templateSm);
        var inc = tm.States.FirstOrDefault(s => IsTransition(net, s.Node) && s.Dst == tidx) ?? throw new InvalidDataException($"category SM {templateSm} has no incoming transition");
        var items = NmbNetBlocks.BuildItems(x);
        var nodeItems = items.Where(it => it.Kind == "node").ToDictionary(it => it.Index);
        var B = NmbNetBlocks.Layout(x);
        var blockOf = B.Where(b => b.Kind == "node").ToDictionary(b => b.Index);
        int parent = parentSm;
        var pmStates = parent == tparent ? tm : Sm(net, parent);
        int hubIdx = parent == tparent ? inc.Src : pmStates.Default;
        int hubNode = hubIdx != 0xFFFF ? pmStates.States[hubIdx].Node : 0xFFFF;
        var sub = Subtree(net, templateSm);
        int ownCount = sub.Count;
        // session 10 (chrint stage 2, rule E2b of track enemyfix, = NmbNetClone.AddCategory): a clone is self-contained. The
        // template's stages use nodes it does not own (DS2's stage-shared additive banking layer FilterTransforms → PlaySpeed →
        // BlendN → a00_50_0410, owned by the band SM above the category). DS2's runtime takes a non-state node's def parent as
        // its active parent (only an SM re-points it, for its own states), so a clone under another band SM that shares such a
        // node makes a time update walk into an inactive SM's stale node bin → access violation in FUN_1405f8ed0 (s9-6/s9-7;
        // nmb-runtime-check D1/D3). Rule: a used node stays shared only if its def parent stays strictly above the common
        // ancestor of all of its users once the clone exists; otherwise it is copied (with its owned subtree) and owned by the
        // clone's band SM (or by the copy of its owner): each category gets its own layer, the vanilla per-category pattern.
        var refsOf = new Dictionary<int, List<int>>();
        foreach (var nd in net.Nodes())
            foreach (int c in nd.Children)
                if (c != 0xFFFF) { if (!refsOf.TryGetValue(c, out var l)) refsOf[c] = l = []; if (!l.Contains(nd.Index)) l.Add(nd.Index); }
        List<int> DefChain(int a) { var l = new List<int>(); for (int k = a, g = 0; k != 0xFFFF && g <= net.NodeCount; k = net.GetNode(k).Parent, g++) l.Add(k); return l; }
        var parentChain = DefChain(parent);
        var inClone = sub.ToHashSet();
        var copyRoots = new Dictionary<int, int>();   // copied external node → its def parent in the template
        for (int k = 0; k < sub.Count; k++)
        {
            var nd = net.GetNode(sub[k]);
            if (nd.Type is 400 or 402) continue;      // a transition's children are states of its own SM
            foreach (int c in nd.Children)
            {
                if (c == 0xFFFF || inClone.Contains(c)) continue;
                int owner = net.GetNode(c).Parent;
                var chains = refsOf[c].Where(r => !IsTransition(net, r)).Select(DefChain).Append(parentChain).ToList();
                var common = chains[0].Where(a => chains.Skip(1).All(l => l.Contains(a))).ToList();
                if (common.Count > 0 && owner != common[0] && common.Contains(owner)) continue;   // stays above every user: share
                foreach (int s in Subtree(net, c)) if (inClone.Add(s)) sub.Add(s);
                copyRoots[c] = owner;
            }
        }
        int nn = net.NodeCount;
        var idmap = sub.Select((old, k) => (old, k)).ToDictionary(t => t.old, t => nn + t.k);
        int newTrans = nn + sub.Count;
        var newItems = new List<NmbNetBlocks.Item>();
        foreach (int old in sub)
        {
            var nb = PatchNodeIds(net, old, nodeItems[old].Data, idmap);
            newItems.Add(new NmbNetBlocks.Item { Kind = "node", Data = nb, Align = 4, Index = idmap[old], Phase = blockOf[old].Start % 16 });
        }
        // copied shared nodes: owned by the copy of their owner when it was copied too (PatchNodeIds did that), else by the band SM
        foreach (var (root, owner) in copyRoots)
            if (!idmap.ContainsKey(owner)) PutH(newItems[sub.IndexOf(root)].Data, 0x08, parent);
        string copied = copyRoots.Count == 0 ? "" : $"copied {sub.Count - ownCount} nodes the template shares from outside it (roots {string.Join(", ", copyRoots.Select(kv => $"{kv.Key} owned by {kv.Value}"))})";
        // the new transition: a clone of the template's incoming one, listed in the parent, children [hub, clone root]
        var tmap = new Dictionary<int, int>(idmap) { [inc.Node] = newTrans };
        if (parent != tparent && inc.Src != 0xFFFF) tmap[tnodes[inc.Src]] = hubNode;
        var tb = PatchNodeIds(net, inc.Node, nodeItems[inc.Node].Data, tmap);
        PutH(tb, 0x08, parent);
        long tpCh = (long)Q(tb, 0x20);
        PutH(tb, (int)tpCh, hubNode); PutH(tb, (int)tpCh + 2, idmap[templateSm]);
        newItems.Add(new NmbNetBlocks.Item { Kind = "node", Data = tb, Align = 4, Index = newTrans, Phase = blockOf[inc.Node].Start % 16 });
        PutH(newItems[0].Data, 0x08, parent);                // the clone root is a child of the SM it is listed in
        // parent SM: new state + transition
        var pb = blockOf[parent];
        var model = NmbNetBlocks.ReadSmNode(x, pb.Start, pb.End);
        var tmodelConds = tm.Conds;
        var sm = model.Sm;
        var newConds = new List<ushort>();
        foreach (int ci in inc.Entry)
        {
            var c = (byte[])tmodelConds[ci].Clone();
            int t = CondType(c);
            if (t == 627 && CpName(names, CondCp(c)) == "AttackCategory_Ene") { PutF(c, 0x4C, newValue); PutF(c, 0x50, newValue); }
            if (NodeConds.Contains(t))
            {
                PutH(c, 0x48, tmap.TryGetValue(H(c, 0x48), out int w) ? w : H(c, 0x48));
                if (t == 611) PutH(c, 0x48 + 12, tmap.TryGetValue(H(c, 0x48 + 12), out int w2) ? w2 : H(c, 0x48 + 12));
            }
            newConds.Add((ushort)sm.Conds.Count); sm.Conds.Add(c);
        }
        int newState = sm.States.Count;
        sm.States.Add(new NmbNetBlocks.SmRecord { Node = (ushort)idmap[templateSm] });
        int newTr = sm.States.Count;
        sm.States.Add(new NmbNetBlocks.SmRecord { Node = (ushort)newTrans, Entry = newConds, Src = (ushort)hubIdx, Dst = (ushort)newState });
        var hub = hubIdx == 0xFFFF ? sm.Global : sm.States[hubIdx];
        hub.ExitConds = [.. hub.ExitConds, .. newConds];
        hub.ExitTrans = [.. hub.ExitTrans, (ushort)newTr];
        model.Children = [.. model.Children, (ushort)idmap[templateSm], (ushort)newTrans];
        nodeItems[parent].Data = NmbNetBlocks.WriteSmNode(model);
        // tables
        int total = newItems.Count, nn2 = nn + total;
        var n0 = items.First(it => it.Kind == "node0");
        BinaryPrimitives.WriteUInt32LittleEndian(n0.Data.AsSpan(0x90), (uint)nn2);
        var tab = items.First(it => it.Kind == "node_table");
        var all = new Dictionary<int, NmbNetBlocks.Item> { [0] = n0 };
        foreach (var (k, v) in nodeItems) all[k] = v;
        foreach (var it in newItems) all[it.Index] = it;
        tab.Data = new byte[8 * nn2];
        tab.Ptrs = [.. Enumerable.Range(1, nn2 - 1).Select(i => (8 * i, all[i], 0L))];
        var smlist = items.First(it => it.Kind == "smlist");
        var lC = items.First(it => it.Kind == "idlistC");
        var em = items.First(it => it.Kind == "emitters");
        var newSms = sub.Where(o => net.GetNode(o).Type == 10).Select(o => (ushort)idmap[o]).ToList();
        var newC = sub.Where(o => (net.U16(net.GetNode(o).Offset + 4) & 0x80) != 0).Select(o => (ushort)idmap[o]).ToList();
        if ((net.U16(net.GetNode(inc.Node).Offset + 4) & 0x80) != 0) newC.Add((ushort)newTrans);
        var newEm = sub.Where(o => net.GetNode(o).Type == 153).Select(o => (ushort)idmap[o]).ToList();
        smlist.Data = NmbNetBlocks.RebuildIdList(smlist.Data, [.. NmbNetBlocks.IdList(smlist.Data), .. newSms]);
        if (newC.Count > 0) lC.Data = NmbNetBlocks.RebuildIdList(lC.Data, [.. NmbNetBlocks.IdList(lC.Data), .. newC]);
        if (newEm.Count > 0) em.Data = NmbNetBlocks.RebuildIdList(em.Data, [.. NmbNetBlocks.IdList(em.Data), .. newEm]);
        // per-node u16 table {u64 0x20, u64 0, u64 0x20, u16 0, u16 numNodes, 4xCD, u16[numNodes]}
        var pn = items.First(it => it.Kind == "pernode");
        int cnt = H(pn.Data, 0x1A);
        var pd = new List<byte>(pn.Data[..(0x20 + 2 * cnt)]);
        var pda = pd.ToArray(); PutH(pda, 0x1A, nn2); pd = [.. pda];
        pd.AddRange(new byte[2 * total]);
        while (pd.Count % 4 != 0) pd.Add(0xCD);
        pn.Data = [.. pd];
        // node names: (id, offset of an empty string)
        var nmt = items.First(it => it.Kind == "names");
        var ent = NmbNetBlocks.StrTabEntries(nmt.Data);
        long pD = (long)Q(nmt.Data, 24);
        uint empty = ent.First(e => nmt.Data[(int)pD + (int)e.Off] == 0).Off;
        nmt.Data = NmbNetBlocks.RebuildStrTab(nmt.Data, [.. ent, .. Enumerable.Range(nn, total).Select(i => ((uint)i, empty))]);
        // request distributors: clones of SMs that react to a request are added to it
        foreach (var it in items.Where(i => i.Kind == "dist"))
        {
            int c2 = (int)U(it.Data, 8); long p1 = (long)Q(it.Data, 0x10);
            var lst = Enumerable.Range(0, c2).Select(k => H(it.Data, (int)p1 + 2 * k)).ToList();
            var add = lst.Where(o => idmap.ContainsKey(o)).Select(o => (ushort)idmap[o]).ToList();
            if (add.Count == 0) continue;
            var lst2 = lst.Concat(add).ToList();
            var d = new List<byte>(it.Data[..0x30]);
            var da = d.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(da.AsSpan(8), (uint)lst2.Count);
            BinaryPrimitives.WriteUInt64LittleEndian(da.AsSpan(0x10), 0x30);
            d = [.. da];
            foreach (var v in lst2) { d.Add((byte)v); d.Add((byte)(v >> 8)); }
            while (d.Count % 4 != 0) d.Add(0xCD);
            it.Data = [.. d];
        }
        int last = items.FindLastIndex(it => it.Kind == "node");
        items.InsertRange(last + 1, newItems);
        return new CloneResult { Network = NmbNetBlocks.Emit(items), IdMap = idmap, Transition = newTrans, Copied = copied };
    }

    // ================================================================== leaves: clip index, event tracks

    /// <summary>Point anim node <paramref name="leaf"/> at file-name-table clip <paramref name="clip"/> (SourceAnim +0x74;
    /// duration/trajectory are patched later from the clip's NSA), in place.</summary>
    public static void SetClipIndex(byte[] x, int leaf, int clip)
    {
        var net = new NmbNetwork(x);
        var sa = NmbNetwork.Find(net.GetNode(leaf), 21) ?? throw new InvalidDataException($"node {leaf} has no SourceAnim");
        BinaryPrimitives.WriteUInt32LittleEndian(x.AsSpan(sa.Offset + 0x74), (uint)clip);
    }

    /// <summary>
    /// Give anim node <paramref name="leaf"/> event-track bundles of its own: every track its SourceEventTracks attrib
    /// (23) names is copied under a new assetId (a unique id from <paramref name="newId"/>, a GUID derived from it) and the
    /// attrib's id field is pointed at the copy (in place: same count, same sizes). DS2 gives every attack leaf its own
    /// tracks (Smelter: one TimeAct / HitDuration / AttackStateControl bundle per attack clip), so a cloned category needs
    /// this before its tracks are rewritten. Returns the new bundles (to insert into the NMB) as (assetId, guid, type, data).
    /// </summary>
    public static List<(uint Id, byte[] Guid, uint Type, byte[] Data)> UnshareTracks(byte[] x, int leaf, IReadOnlyDictionary<uint, NmbFile.Bundle> tracks, Func<uint> newId)
    {
        var net = new NmbNetwork(x);
        var et = NmbNetwork.Find(net.GetNode(leaf), 23) ?? throw new InvalidDataException($"node {leaf} has no event tracks");
        var o = new List<(uint, byte[], uint, byte[])>();
        int e = et.Offset;
        for (int k = 0; k < 3; k++)
        {
            int cnt = (int)U(x, e + 0x10 + 0x18 * k);
            long pid = (long)Q(x, e + 0x18 + 0x18 * k);
            for (int j = 0; j < cnt; j++)
            {
                int f = e + (int)pid + 8 * j;
                uint old = U(x, f);
                if (!tracks.TryGetValue(old, out var b)) continue;
                uint id = newId();
                var guid = System.Security.Cryptography.MD5.HashData(Encoding.ASCII.GetBytes($"lordran-track-{id:x8}"));
                o.Add((id, guid, b.Type, (byte[])b.Data.Clone()));
                BinaryPrimitives.WriteUInt32LittleEndian(x.AsSpan(f), id);
            }
        }
        return o;
    }

    // ================================================================== new event tracks on a leaf (session 8, track boss step 2)

    /// <summary>A SourceEventTracks attrib (type 23) as data: the 16-byte AttribData header and per group (0 discrete, 1
    /// curve, 2 duration) the track assetIds and their size fields (= the track bundle's data length).</summary>
    public sealed class TrackAttrib
    {
        public byte[] Header = new byte[16];
        public List<(uint Id, uint Size)>[] Groups = [[], [], []];
    }

    public static TrackAttrib ReadTrackAttrib(byte[] blob)
    {
        var t = new TrackAttrib { Header = blob[..16] };
        for (int k = 0; k < 3; k++)
        {
            int cnt = (int)U(blob, 0x10 + 0x18 * k);
            long pid = (long)Q(blob, 0x18 + 0x18 * k), psz = (long)Q(blob, 0x20 + 0x18 * k);
            for (int j = 0; j < cnt; j++) t.Groups[k].Add((U(blob, (int)pid + 8 * j), psz != 0 ? U(blob, (int)psz + 4 * j) : 0));
        }
        return t;
    }

    /// <summary>
    /// The attrib as DS2 lays it out (NETWORK-FORMAT.md §2.3, checked on every DS2 anim node by <c>nmb-track-attrib-selftest</c>):
    /// header; three group records {u32 count, 4×CD, u64 ids, u64 sizes} at 0x10; then per group its ids (8 bytes: u32 id,
    /// u32 0) and its sizes (u32), an empty group pointing at the current position; CD padding to 16.
    /// </summary>
    public static byte[] WriteTrackAttrib(TrackAttrib t)
    {
        var o = new List<byte>(t.Header);
        var head = new byte[0x48];
        o.AddRange(head);
        long pos = 0x58;
        var data = new List<byte>();
        for (int k = 0; k < 3; k++)
        {
            var g = t.Groups[k];
            int r = 0x10 + 0x18 * k;
            long pIds = pos, pSz = pos + 8 * g.Count;
            var b = o.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(r), (uint)g.Count);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(r + 4), 0xCDCDCDCD);
            BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(r + 8), (ulong)pIds);
            BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(r + 16), (ulong)pSz);
            o = [.. b];
            foreach (var (id, _) in g) { data.AddRange(BitConverter.GetBytes(id)); data.AddRange(new byte[4]); }
            foreach (var (_, sz) in g) data.AddRange(BitConverter.GetBytes(sz));
            pos += 12 * g.Count;
        }
        o.AddRange(data);
        while (o.Count % 16 != 0) o.Add(0xCD);
        return [.. o];
    }

    /// <summary>
    /// Append tracks to anim node <paramref name="leaf"/>'s SourceEventTracks (group, track assetId, its bundle's data length):
    /// the attrib is rebuilt (<see cref="WriteTrackAttrib"/>), the node block re-laid out (<see cref="NmbNetBlocks.WriteNode"/>,
    /// the attrib handle's size updated) and the network re-emitted through the block model, so every block that moves keeps
    /// its pointers. Returns the new network bundle.
    /// </summary>
    public static byte[] AddLeafTracks(byte[] x, int leaf, IReadOnlyList<(int Group, uint Id, uint Size)> add)
    {
        var items = NmbNetBlocks.BuildItems(x);
        var item = items.First(it => it.Kind == "node" && it.Index == leaf);
        var model = NmbNetBlocks.ReadNode(item.Data, 0, item.Data.Length);
        int hk = model.Handles.FindIndex(h => h.Ptr != 0 && model.Blobs.TryGetValue(h.Ptr, out var bl) && bl.Length >= 12 && H(bl, 10) == 23);
        if (hk < 0) throw new InvalidDataException($"node {leaf} has no SourceEventTracks attrib");
        var (ptr, _, align, padv) = model.Handles[hk];
        var ta = ReadTrackAttrib(model.Blobs[ptr]);
        foreach (var (g, id, sz) in add) ta.Groups[g].Add((id, sz));
        var blob = WriteTrackAttrib(ta);
        model.Blobs[ptr] = blob;
        model.Handles[hk] = (ptr, blob.Length, align, padv);
        item.Data = NmbNetBlocks.WriteNode(model);
        return NmbNetBlocks.Emit(items);
    }

    /// <summary>
    /// The runtime id of event-track name <paramref name="name"/> in the network's track-name table (+0xD8), adding it (id = the
    /// largest + 1, the string appended to the table's data) when the network has none. DS2 keys a track's meaning on its
    /// userData (Gravity 9600, HitControl 9700: c3180 names its gravity track "IK_1_1"), the name table maps runtime ids.
    /// </summary>
    public static (byte[] X, uint Id) EnsureTrackName(byte[] x, string name)
    {
        var have = TrackNameIds(x);
        if (have.TryGetValue(name, out int id)) return (x, (uint)id);
        var items = NmbNetBlocks.BuildItems(x);
        var tab = items.First(it => it.Kind == "tracknames");
        var ent = NmbNetBlocks.StrTabEntries(tab.Data);
        uint newId = ent.Count == 0 ? 0 : ent.Max(e => e.Id) + 1;
        tab.Data = AppendStrTab(tab.Data, newId, name);
        return (NmbNetBlocks.Emit(items), newId);
    }

    /// <summary>A string table with one more entry (NmbNetBlocks.RebuildStrTab's layout, the string NUL-terminated after the
    /// old data).</summary>
    public static byte[] AppendStrTab(byte[] old, uint id, string name)
    {
        int dlen = (int)U(old, 4); long pD = (long)Q(old, 24);
        var data = old[(int)pD..((int)pD + dlen)].ToList();
        var ent = NmbNetBlocks.StrTabEntries(old);
        ent.Add((id, (uint)data.Count));
        data.AddRange(Encoding.ASCII.GetBytes(name)); data.Add(0);
        int n = ent.Count;
        var o = new List<byte>();
        void U32(uint v) { for (int i = 0; i < 4; i++) o.Add((byte)(v >> (8 * i))); }
        void U64(long v) { for (int i = 0; i < 8; i++) o.Add((byte)(v >> (8 * i))); }
        U32((uint)n); U32((uint)data.Count); U64(32); U64(32 + 4 * n); U64(32 + 8 * n);
        foreach (var e in ent) U32(e.Id);
        foreach (var e in ent) U32(e.Off);
        o.AddRange(data);
        while (o.Count % 4 != 0) o.Add(0xCD);
        return [.. o];
    }

    /// <summary>Insert event-track bundles after the last existing track bundle (types 3/4/5), header like the existing
    /// ones (magic 0x18, version 10, align 4).</summary>
    public static void InsertTrackBundles(NmbFile f, IEnumerable<(uint Id, byte[] Guid, uint Type, byte[] Data)> add)
    {
        int last = f.Bundles.FindLastIndex(b => b.Type is 3 or 4 or 5);
        var proto = f.Bundles[last];
        var list = add.Select(a => new NmbFile.Bundle
        {
            Magic = proto.Magic, Version = proto.Version, Type = a.Type, AssetId = a.Id, Guid = a.Guid, Align = proto.Align, U2C = proto.U2C, Data = a.Data,
        }).ToList();
        f.Bundles.InsertRange(last + 1, list);
    }

    // ================================================================== file-name table (bundle 13)

    public sealed class FileNames
    {
        public List<string> Files = [], Formats = [], Sources = [], Takes = [];
        public List<uint> Hashes = [];
        public byte Pad;
    }

    public static FileNames ReadFileNames(byte[] d)
    {
        var p = Enumerable.Range(0, 5).Select(k => (int)Q(d, 8 * k)).ToArray();
        var f = new FileNames
        {
            Files = NmbFile.ReadStringTable(d, p[0]).Names, Formats = NmbFile.ReadStringTable(d, p[1]).Names,
            Sources = NmbFile.ReadStringTable(d, p[2]).Names, Takes = NmbFile.ReadStringTable(d, p[3]).Names,
        };
        for (int i = 0; i < f.Files.Count; i++) f.Hashes.Add(U(d, p[4] + 4 * i));
        // padding between the string tables: 0xCD in 160 DS2 files, 0x00 in 13 (both re-emit identically)
        f.Pad = WriteFileNames(f, 0).AsSpan().SequenceEqual(d) ? (byte)0 : (byte)0xCD;
        return f;
    }

    static byte[] WriteStrTable(List<string> names)
    {
        int n = names.Count;
        var data = new List<byte>(); var offs = new List<int>();
        foreach (var s in names) { offs.Add(data.Count); data.AddRange(Encoding.ASCII.GetBytes(s)); data.Add(0); }
        var o = new NmbFile.Buf();
        o.U32((uint)n); o.U32((uint)data.Count); o.U64(0x20); o.U64((ulong)(0x20 + 4 * n)); o.U64((ulong)(0x20 + 8 * n));
        for (int i = 0; i < n; i++) o.U32((uint)i);
        foreach (int v in offs) o.U32((uint)v);
        o.Raw(data);
        return o.ToArray();
    }

    /// <summary>Bundle-13 data (nmb_rig.write_filenames): u64 ptr[4] to the string tables (each 4-aligned, gaps
    /// <paramref name="pad"/>) + u64 ptr to the u32 hashes (directly after the last table).</summary>
    public static byte[] WriteFileNames(FileNames f, byte pad)
    {
        var o = new NmbFile.Buf(); o.Fill(0, 0x28);
        var ptrs = new List<int>();
        var tabs = new[] { f.Files, f.Formats, f.Sources, f.Takes };
        for (int k = 0; k < 4; k++)
        {
            if (k > 0) o.Pad(4, pad);
            ptrs.Add(o.Len); o.Raw(WriteStrTable(tabs[k]));
        }
        ptrs.Add(o.Len);
        foreach (var h in f.Hashes) o.U32(h);
        var b = o.ToArray();
        for (int k = 0; k < 5; k++) BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(8 * k), (ulong)ptrs[k]);
        return b;
    }

    /// <summary>CRC-32/BZIP2 (poly 0x04C11DB7, not reflected, init/xorout 0xFFFFFFFF): the file-name table's u32 per clip =
    /// this of the NSA bytes (19,125/19,125 DS2 clips, NETWORK-FORMAT.md §2.6).</summary>
    public static uint Crc32Bzip2(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= (uint)b << 24;
            for (int k = 0; k < 8; k++) crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04C11DB7 : crc << 1;
        }
        return crc ^ 0xFFFFFFFF;
    }

    /// <summary>DS2's NSA name rule: (anim + "_" + take)[:37 - len(hex)] + "_" + hex + ".nsa" (19,222/19,222).</summary>
    public static string NsaName(string anim, uint suffix, string take = "untitled")
    {
        string hx = suffix.ToString("x");
        string s = $"{anim}_{take}";
        return s[..Math.Min(s.Length, 37 - hx.Length)] + "_" + hx + ".nsa";
    }

    // ================================================================== checks

    /// <summary>Structural checks of a network after edits (the C# subset of nmb_net_validate's strict structure): every
    /// state/transition listed in an SM has it as parent; a transition's children are [source, destination] matching its
    /// record; every state's exit conditions are the union of its exit transitions' entry conditions; condition node
    /// references are valid node ids; CP conditions name a CP node; the block model relocates cleanly; every anim leaf's
    /// clip index is inside the file-name table. Returns the problems (empty = clean).</summary>
    public static List<string> Check(byte[] x, int clipCount)
    {
        var bad = new List<string>();
        var net = new NmbNetwork(x);
        foreach (var node in net.Nodes().Where(n => n.Type == 10))
        {
            var d = Sm(net, node.Index);
            for (int k = 0; k < d.States.Count; k++)
            {
                var st = d.States[k];
                var sn = net.GetNode(st.Node);
                if (sn.Parent != node.Index) bad.Add($"SM {node.Index}: state/transition {st.Node} has parent {sn.Parent}");
                if (sn.Type is 400 or 402)
                {
                    int src = st.Src == 0xFFFF ? 0xFFFF : d.States[st.Src].Node, dst = d.States[st.Dst].Node;
                    if (sn.Children.Length != 2 || sn.Children[0] != src || sn.Children[1] != dst) bad.Add($"SM {node.Index}: transition {st.Node} children [{string.Join(",", sn.Children)}] != [{src},{dst}]");
                }
                else
                {
                    var want = st.ExitTrans.SelectMany(t => d.States[t].Entry).ToHashSet();
                    if (!want.SetEquals(st.ExitConds)) bad.Add($"SM {node.Index}: state {st.Node} exit conditions != union of its exit transitions'");
                }
            }
            foreach (var c in d.Conds)
            {
                int t = CondType(c);
                if (NodeConds.Contains(t) && H(c, 0x48) != 0xFFFF && H(c, 0x48) >= net.NodeCount) bad.Add($"SM {node.Index}: condition {t} names node {H(c, 0x48)}");
                if (t is 606 or 608 or 623 or 624 or 625 or 627 && net.GetNode(CondCp(c)).Type is not (20 or 21 or 22 or 23 or 24 or 25)) bad.Add($"SM {node.Index}: condition {t} on non-CP node {CondCp(c)}");
            }
            if (bad.Count > 40) break;
        }
        foreach (var n in net.Nodes().Where(n => n.Type == 104))
        {
            var sa = NmbNetwork.Find(n, 21);
            if (sa != null && net.ClipIndex(sa) >= clipCount) bad.Add($"anim node {n.Index}: clip index {net.ClipIndex(sa)} >= {clipCount}");
        }
        var rel = NmbNetBlocks.RelocationCheck(x, NmbNetBlocks.Emit(NmbNetBlocks.BuildItems(x), 48));
        bad.AddRange(rel.Select(r => "relocation: " + r));
        return bad;
    }
}
