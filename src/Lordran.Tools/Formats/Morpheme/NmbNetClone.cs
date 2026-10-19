using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Lordran.Tools.Formats.Morpheme;

/// <summary>
/// Network editor for new attack categories (session 8, track enemies step 1): the C# port of our own research code
/// data\research\morpheme\net_clone.py (stage 2b of the re-serialiser: clone a category sub-state-machine, band widening,
/// clip retarget, file-name entries) on top of <see cref="NmbNetEdit"/>, plus what the Python left pending: new event-track
/// bundles per retargeted leaf (each new attack clip gets its own TimeAct / hit / combo / cancel / sync tracks) and new NSA
/// entries in the file-name table. No NaturalMotion code.
/// <para>DS2 attack routing (NETWORK-FORMAT.md §3, V on all DS2 enemies): SM_Main → SM_Attack when the Attack request comes
/// with AttackCategory_Ene in a band ([0,9], [10,19], [20,29] + Shot route); SM_Attack → per-weapon SM (WeaponCategory_Ene)
/// → band SM → one category SM per value (AttackCategory_Ene in [k,k]); a category SM's default state is stage 1, stage
/// s → s+1 on AttackStateControl 101 / 102 with Combo{s}_Route 1 / 2. The engine sets AttackCategory_Ene = the requested
/// action n (plain actions 0-29) or the combo row's AttackMotionId.</para>
/// <para>Session 9 (track enemyfix): the clone also copies every node the template uses from outside its own subtree whose
/// def parent would not stay strictly above all of its users (the stage-shared additive layer, owned by the template's band
/// SM), so no clone shares a node with an owner that is inactive while the clone plays (nmb-runtime-check rule D1; the
/// in-game crashes s9-6 / s9-7). The Python reference net_clone.py does not do this: C# = Python only without such nodes.</para>
/// <para>Cloning (net_clone.add_category, fix pass): the template category SM and its owned subtree get new node ids; a new
/// transition (clone of the template's incoming one, condition AttackCategory_Ene in [v,v], parent = the SM it is listed in,
/// attrib-59 sub-state path remapped) is added to the target band SM; node table, node count, SM / flag-0x80 / emitter id
/// lists, per-node table, node names and request distributors are extended; node blocks keep their 16-byte phase.</para>
/// </summary>
public sealed class NmbNetClone
{
    public NmbFile Nmb;
    public int NetIndex => Nmb.IndexOf(10);
    public byte[] X { get => Nmb.Bundles[NetIndex].Data; set => Nmb.Bundles[NetIndex].Data = value; }
    public List<string> Log = [];

    public NmbNetClone(byte[] nmbBytes) { Nmb = NmbFile.Read(nmbBytes); }

    public byte[] Build() => Nmb.Rebuild();

    static ushort U16(byte[] b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o));
    static void P16(byte[] b, int o, int v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), (ushort)v);

    public NmbNetwork View() => new(X);

    public string CpName(int node) => NmbNetEdit.NodeNames(X).TryGetValue(node, out var n) && n.StartsWith("ControlParameters|") ? n["ControlParameters|".Length..] : $"node{node}";

    public int NodeByName(string name) => NmbNetEdit.NodeNames(X).First(kv => kv.Value == name).Key;

    public NmbNetEdit.SmDef Sm(int node)
    {
        var n = View().GetNode(node);
        return NmbNetEdit.ReadSmDef(X, n.Attribs.First(a => a.Type == 38).Offset);
    }

    /// <summary>Nodes owned by root (parent chain), DFS order (net_clone.subtree).</summary>
    public List<int> Subtree(int root)
    {
        var net = View(); var o = new List<int>(); var seen = new HashSet<int>();
        void Rec(int i)
        {
            if (i == 0xFFFF || !seen.Add(i)) return;
            o.Add(i);
            foreach (int c in net.GetNode(i).Children)
            {
                if (c == 0xFFFF) continue;
                if (net.GetNode(c).Parent == i) Rec(c);
            }
        }
        Rec(root);
        return o;
    }

    // ------------------------------------------------------------------ node-id patching (net_clone.patch_node_ids / patch_a59)
    static int PatchA59(byte[] b, int A, Func<int, int> mp)
    {
        long q = (long)BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(A + 0x30));
        if (q == 0) return 0;
        long p0 = (long)BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(A + (int)q));
        int cnt = U16(b, A + (int)q + 8), n = 0;
        for (int k = 0; k < cnt; k++)
        {
            long r = (long)BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(A + (int)q + (int)p0 + 8 * k));
            foreach (int j in new[] { 2, 4 })
            {
                int o = A + (int)q + (int)r + j; int v = U16(b, o), w = mp(v);
                if (w != v) { P16(b, o, w); n++; }
            }
        }
        return n;
    }

    byte[] PatchNodeIds(NmbNetwork net, int i, byte[] blob, IReadOnlyDictionary<int, int> idmap)
    {
        var b = (byte[])blob.Clone();
        int p = net.GetNode(i).Offset;
        int Mp(int v) => idmap.TryGetValue(v, out int w) ? w : v;
        P16(b, 0x06, Mp(U16(b, 0x06)));
        int par = U16(b, 0x08);
        if (par != 0xFFFF) P16(b, 0x08, Mp(par));
        int nch = U16(b, 0x0A), ncp = b[0x0E];
        long pCh = (long)BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(0x20)), pCP = (long)BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(0x28));
        if (pCh != 0) for (int k = 0; k < nch; k++) { int v = U16(b, (int)pCh + 2 * k); if (v != 0xFFFF) P16(b, (int)pCh + 2 * k, Mp(v)); }
        if (pCP != 0) for (int k = 0; k < ncp; k++) { int v = U16(b, (int)pCP + 4 * k); if (v != 0xFFFF) P16(b, (int)pCP + 4 * k, Mp(v)); }
        foreach (var a in net.GetNode(i).Attribs)
        {
            int A = a.Offset - p;
            if (a.Type == 38)
            {
                int nst = U16(b, A + 0x12);
                long pSt = (long)BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(A + 0x18)), pGl = (long)BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(A + 0x20));
                for (int k = -1; k < nst; k++)
                {
                    int o = A + (int)(k >= 0 ? pSt + 0x30 * k : pGl);
                    int v = U16(b, o);
                    if (v != 0xFFFF) P16(b, o, Mp(v));
                }
                int ncond = U16(b, A + 0x2A); long pC = (long)BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(A + 0x30));
                for (int k = 0; k < ncond; k++)
                {
                    long c = (long)BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(A + (int)pC + 8 * k));
                    int t = U16(b, A + (int)c);
                    if (t is 602 or 603 or 610 or 611 or 617 or 618)
                    {
                        int q = A + (int)c + 0x48;
                        P16(b, q, Mp(U16(b, q)));
                        if (t == 611) P16(b, q + 12, Mp(U16(b, q + 12)));
                    }
                }
            }
            else if (a.Type == 59) PatchA59(b, A, Mp);
        }
        return b;
    }

    // ------------------------------------------------------------------ category cloning (net_clone.add_category)
    public sealed class CloneResult
    {
        public Dictionary<int, int> IdMap = [];
        public int NewSm, NewTransition;
    }

    /// <summary>Clone category SM <paramref name="templateSm"/> into band SM <paramref name="parentSm"/> (default: the template's
    /// own parent; the new state hangs off that SM's default/hub state) answering AttackCategory_Ene == <paramref name="value"/>.</summary>
    public CloneResult AddCategory(int templateSm, float value, int? parentSm = null) => CloneState(templateSm, value, parentSm, null, null, null, null);

    /// <summary>Session 11 (track gest11, rule GS1): <see cref="AddCategory"/> on another category control parameter, e.g.
    /// c0001's <c>GestureCategory</c> (an int CP: CPIntInRange conditions, type 625; float CPs use 627). The cloned incoming
    /// transition's single-value range on <paramref name="categoryCp"/> becomes [<paramref name="value"/>, value].</summary>
    public CloneResult AddCategoryOn(string categoryCp, int templateSm, float value, int? parentSm = null) => CloneState(templateSm, value, parentSm, null, null, null, null, categoryCp);

    /// <summary>
    /// Session 11 (track burgchr11, rule CH4): one more route stage on a pure chain category SM. The last stage state (with
    /// its owned subtree and, by rule D1, the nodes it shares with its sibling stages) is cloned into the same SM as a new
    /// state, entered from the old last stage by a clone of the route-1 transition into it (event 101 ∧ Combo{k}_Route in
    /// [1,1]) whose source-event node becomes the old last stage and whose route condition reads Combo{k+1}_Route (c1030
    /// defines Combo1-3_Route; EnemyComboBehaviorParam holds ComboAttackRoute1-3, so a chain has at most 4 stages). Returns
    /// the new stage's node.
    /// </summary>
    public int AddStage(int categorySm, IReadOnlyList<int> stageNodes)
    {
        if (stageNodes.Count < 2) throw new InvalidDataException($"SM {categorySm}: a stage can only be added to a chain of 2+ stages");
        var net = View(); var names = NmbNetEdit.NodeNames(X);
        var def = Sm(categorySm);
        var nodes = def.States.Select(s => (int)s.Node).ToList();
        int last = stageNodes[^1], prev = stageNodes[^2], k = stageNodes.Count - 1;   // the transition into stage k+1 reads Combo{k}_Route
        int lastIdx = nodes.IndexOf(last), prevIdx = nodes.IndexOf(prev);
        int Cp(string n) => names.First(kv => kv.Value == "ControlParameters|" + n).Key;
        int from = Cp($"Combo{k}_Route"), to = names.Any(kv => kv.Value == $"ControlParameters|Combo{k + 1}_Route") ? Cp($"Combo{k + 1}_Route")
            : throw new InvalidDataException($"the network has no control parameter Combo{k + 1}_Route (stage {k + 2})");
        bool Route1(NmbNetEdit.SmState s) => net.GetNode(s.Node).Type is 400 or 402 && s.Src == prevIdx && s.Dst == lastIdx
            && s.Entry.Any(ci => BitConverter.ToUInt16(def.Conds[ci], 0) is 625 or 627 && BitConverter.ToUInt16(def.Conds[ci], 0x48) == from
                && (BitConverter.ToUInt16(def.Conds[ci], 0) == 625 ? BitConverter.ToInt32(def.Conds[ci], 0x4C) : (int)BitConverter.ToSingle(def.Conds[ci], 0x4C)) == 1);
        var r = CloneState(last, k + 1, categorySm, lastIdx, new Dictionary<int, int> { [prev] = last }, new Dictionary<int, int> { [from] = to }, Route1);
        Log.Add($"rule CH4: SM {categorySm} stage {k + 2} = clone of stage {k + 1} (node {last} → {r.NewSm}), entered on event 101 ∧ Combo{k + 1}_Route 1 (transition {r.NewTransition})");
        return r.NewSm;
    }

    /// <summary>The clone behind <see cref="AddCategory"/> and <see cref="AddStage"/>: state <paramref name="templateSm"/> of its SM
    /// (with its subtree) as a new state of <paramref name="parentSm"/>, entered from <paramref name="hubIndex"/> (default: the
    /// template transition's source in the same SM, else the parent's default state) by a clone of the template's incoming
    /// transition (<paramref name="pick"/>, default the first), with <paramref name="extraMap"/> applied to its node references
    /// and <paramref name="cpRemap"/> to its control-parameter conditions.</summary>
    CloneResult CloneState(int templateSm, float value, int? parentSm, int? hubIndex, IReadOnlyDictionary<int, int>? extraMap, IReadOnlyDictionary<int, int>? cpRemap, Func<NmbNetEdit.SmState, bool>? pick, string categoryCp = "AttackCategory_Ene")
    {
        var x = X; var net = new NmbNetwork(x);
        var tpl = net.GetNode(templateSm);
        int tparent = tpl.Parent;
        var tm = Sm(tparent);
        var tnodes = tm.States.Select(s => (int)s.Node).ToList();
        int tidx = tnodes.IndexOf(templateSm);
        var inc = tm.States.FirstOrDefault(s => net.GetNode(s.Node).Type is 400 or 402 && s.Dst == tidx && (pick == null || pick(s))) ?? throw new InvalidDataException($"SM {templateSm} has no incoming transition");
        int parent = parentSm ?? tparent;
        var pm = Sm(parent);
        var nodes = pm.States.Select(s => (int)s.Node).ToList();
        int hubIdx = hubIndex ?? (parent == tparent ? inc.Src : pm.Default);
        int hubNode = hubIdx != 0xFFFF ? nodes[hubIdx] : 0xFFFF;
        var sub = Subtree(templateSm);
        // session 9 (track enemyfix, rule D1 of nmb-runtime-check): nodes the template category uses but does not own. DS2's
        // categories share an additive banking layer (FilterTransforms → PlaySpeed → BlendN → a00_50_0410) between their stages;
        // that layer is owned by the band SM ABOVE the category (vanilla: every multiply-referenced node's def parent lies
        // strictly above the lowest common ancestor of the nodes that list it, 2449/2449). The runtime never re-points the
        // active parent of such a node (only an SM sets it, for its own states), so a time update from its parent walks to the
        // def parent: shared with a clone under another band SM (or another weapon category), it walks into an SM that is not
        // active, whose node bin holds a stale task list → access violation in FUN_1405f8ed0 (runs s9-6 / s9-7). Rule: a used
        // node stays shared only if its def parent stays strictly above the referrers' common ancestor once the clone exists;
        // otherwise the node and its owned subtree are copied with the clone, owned by the clone's band SM (or by the copy of
        // its owner) - each clone then has its own layer, exactly the vanilla per-category pattern.
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
                var chains = refsOf[c].Where(r => net.GetNode(r).Type is not (400 or 402)).Select(DefChain).Append(parentChain).ToList();
                var common = chains[0].Where(a => chains.Skip(1).All(l => l.Contains(a))).ToList();
                if (common.Count > 0 && owner != common[0] && common.Contains(owner)) continue;   // stays above every user: share
                foreach (int s in Subtree(c)) if (inClone.Add(s)) sub.Add(s);
                copyRoots[c] = owner;
            }
        }
        if (copyRoots.Count > 0) Log.Add($"category {value}: copied {sub.Count - Subtree(templateSm).Count} nodes the template shares from outside it (roots {string.Join(", ", copyRoots.Select(kv => $"{kv.Key} owned by {kv.Value}"))})");
        int nn = net.NodeCount;
        var idmap = new Dictionary<int, int>();
        for (int k = 0; k < sub.Count; k++) idmap[sub[k]] = nn + k;
        int newTrans = nn + sub.Count;
        var items = NmbNetEdit.BuildItems(x);
        var nodeItems = items.Where(i => i.Kind == "node").ToDictionary(i => i.Idx);
        var newItems = new List<NmbNetEdit.Item>();
        foreach (int old in sub)
            newItems.Add(new NmbNetEdit.Item { Kind = "node", Data = PatchNodeIds(net, old, nodeItems[old].Data, idmap), Align = 4, Idx = idmap[old], Phase = net.GetNode(old).Offset % 16 });
        var tmap = new Dictionary<int, int>(idmap) { [inc.Node] = newTrans };
        if (parent != tparent && inc.Src != 0xFFFF) tmap[tnodes[inc.Src]] = hubNode;
        foreach (var (a, b) in extraMap ?? new Dictionary<int, int>()) tmap[a] = b;   // CH4: the source-event node of the stage transition
        var tb = PatchNodeIds(net, inc.Node, nodeItems[inc.Node].Data, tmap);
        P16(tb, 0x08, parent);
        long pCh = (long)BinaryPrimitives.ReadUInt64LittleEndian(tb.AsSpan(0x20));
        P16(tb, (int)pCh, hubNode); P16(tb, (int)pCh + 2, idmap[templateSm]);
        newItems.Add(new NmbNetEdit.Item { Kind = "node", Data = tb, Align = 4, Idx = newTrans, Phase = net.GetNode(inc.Node).Offset % 16 });
        P16(newItems[0].Data, 0x08, parent);
        // copied shared nodes: owned by the copy of their owner when it was copied too (PatchNodeIds did that), else by the band SM
        foreach (var (root, owner) in copyRoots)
            if (!idmap.ContainsKey(owner)) P16(newItems[sub.IndexOf(root)].Data, 0x08, parent);
        // parent SM: add state + transition
        var ends = NmbNetEdit.NodeEnds(x);
        NmbNetEdit.SmNodeModel ModelOf(int i) => NmbNetEdit.ReadSmNode(x, net.GetNode(i).Offset, ends[i]);
        var tmodel = ModelOf(tparent);
        var model = parent == tparent ? tmodel : ModelOf(parent);
        var sm = model.Sm;
        var names = NmbNetEdit.NodeNames(x);
        var newConds = new List<ushort>();
        foreach (int ci in inc.Entry)
        {
            var c = (byte[])tmodel.Sm.Conds[ci].Clone();
            int t = U16(c, 0);
            if (t == 627 && names.TryGetValue(U16(c, 0x48), out var cn) && cn == "ControlParameters|" + categoryCp)
            {
                BinaryPrimitives.WriteSingleLittleEndian(c.AsSpan(0x4C), value); BinaryPrimitives.WriteSingleLittleEndian(c.AsSpan(0x50), value);
            }
            // session 11 (gest11): an int category CP (CPIntInRange, e.g. GestureCategory); only the gesture clone asks for it
            if (t == 625 && categoryCp != "AttackCategory_Ene" && names.TryGetValue(U16(c, 0x48), out var cni) && cni == "ControlParameters|" + categoryCp)
            {
                BinaryPrimitives.WriteInt32LittleEndian(c.AsSpan(0x4C), (int)value); BinaryPrimitives.WriteInt32LittleEndian(c.AsSpan(0x50), (int)value);
            }
            if (t is 602 or 603 or 610 or 611 or 617 or 618)
            {
                int v = U16(c, 0x48); P16(c, 0x48, tmap.TryGetValue(v, out int w) ? w : v);
                if (t == 611) { v = U16(c, 0x54); P16(c, 0x54, tmap.TryGetValue(v, out w) ? w : v); }
            }
            if (t is 623 or 624 or 625 or 627 && cpRemap != null && cpRemap.TryGetValue(U16(c, 0x48), out int cpTo)) P16(c, 0x48, cpTo);   // CH4: Combo{k}_Route → Combo{k+1}_Route
            newConds.Add((ushort)sm.Conds.Count); sm.Conds.Add(c);
        }
        int newStateIdx = sm.States.Count;
        sm.States.Add(new NmbNetEdit.SmState { Node = (ushort)idmap[templateSm] });
        int newTrIdx = sm.States.Count;
        sm.States.Add(new NmbNetEdit.SmState { Node = (ushort)newTrans, Entry = newConds, Src = (ushort)hubIdx, Dst = (ushort)newStateIdx });
        var hub = hubIdx == 0xFFFF ? sm.Glob : sm.States[hubIdx];
        hub.ExitConds = [.. hub.ExitConds, .. newConds];
        hub.ExitTrans = [.. hub.ExitTrans, (ushort)newTrIdx];
        model.Children = [.. model.Children, (ushort)idmap[templateSm], (ushort)newTrans];
        nodeItems[parent].Data = NmbNetEdit.WriteSmNode(model);
        // tables
        int total = newItems.Count, nn2 = nn + total;
        var n0 = items.First(i => i.Kind == "node0");
        BinaryPrimitives.WriteUInt32LittleEndian(n0.Data.AsSpan(0x90), (uint)nn2);
        var tab = items.First(i => i.Kind == "node_table");
        var all = new Dictionary<int, NmbNetEdit.Item>(nodeItems) { [0] = n0 };
        foreach (var it in newItems) all[it.Idx] = it;
        tab.Data = new byte[8 * nn2];
        tab.Ptrs = [.. Enumerable.Range(1, nn2 - 1).Select(i => (8 * i, all[i], 0L))];
        var smlist = items.First(i => i.Kind == "smlist"); var lC = items.First(i => i.Kind == "idlistC"); var em = items.First(i => i.Kind == "emitters");
        var newSms = sub.Where(o => net.GetNode(o).Type == 10).Select(o => idmap[o]).ToList();
        var newC = sub.Where(o => (U16(x, net.GetNode(o).Offset + 4) & 0x80) != 0).Select(o => idmap[o]).ToList();
        if ((U16(x, net.GetNode(inc.Node).Offset + 4) & 0x80) != 0) newC.Add(newTrans);
        var newEm = sub.Where(o => net.GetNode(o).Type == 153).Select(o => idmap[o]).ToList();
        smlist.Data = NmbNetEdit.RebuildIdList(smlist.Data, [.. NmbNetEdit.IdsOf(smlist.Data), .. newSms]);
        if (newC.Count > 0) lC.Data = NmbNetEdit.RebuildIdList(lC.Data, [.. NmbNetEdit.IdsOf(lC.Data), .. newC]);
        if (newEm.Count > 0) em.Data = NmbNetEdit.RebuildIdList(em.Data, [.. NmbNetEdit.IdsOf(em.Data), .. newEm]);
        var pn = items.First(i => i.Kind == "pernode");
        int cnt = U16(pn.Data, 0x1A);
        var d = new List<byte>(pn.Data[..(0x20 + 2 * cnt)]);
        var dArr = d.ToArray(); P16(dArr, 0x1A, nn2); d = [.. dArr];
        d.AddRange(new byte[2 * total]);
        while (d.Count % 4 != 0) d.Add(0xCD);
        pn.Data = d.ToArray();
        var nmt = items.First(i => i.Kind == "names");
        var st = NmbNetEdit.StringTable(nmt.Data, 0);
        uint empty = st.First(e => e.Name.Length == 0).Off;
        var ent = st.Select(e => (e.Id, e.Off)).Concat(Enumerable.Range(nn, total).Select(i => ((uint)i, empty))).ToList();
        nmt.Data = NmbNetEdit.RebuildStrtab(nmt.Data, ent);
        foreach (var it in items.Where(i => i.Kind == "dist"))
        {
            int c2 = (int)BinaryPrimitives.ReadUInt32LittleEndian(it.Data.AsSpan(8));
            long p1 = (long)BinaryPrimitives.ReadUInt64LittleEndian(it.Data.AsSpan(0x10));
            var lst = Enumerable.Range(0, c2).Select(k => (int)U16(it.Data, (int)p1 + 2 * k)).ToList();
            var add = lst.Where(idmap.ContainsKey).Select(o => idmap[o]).ToList();
            if (add.Count == 0) continue;
            var lst2 = lst.Concat(add).ToList();
            var dd = new List<byte>(it.Data[..0x30]);
            var ddA = dd.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(ddA.AsSpan(8), (uint)lst2.Count); BinaryPrimitives.WriteUInt64LittleEndian(ddA.AsSpan(0x10), 0x30);
            dd = [.. ddA];
            foreach (int v in lst2) { dd.Add((byte)v); dd.Add((byte)(v >> 8)); }
            while (dd.Count % 4 != 0) dd.Add(0xCD);
            it.Data = dd.ToArray();
        }
        int last = items.FindLastIndex(i => i.Kind == "node");
        items.InsertRange(last + 1, newItems);
        X = NmbNetEdit.Emit(items);
        return new CloneResult { IdMap = idmap, NewSm = idmap[templateSm], NewTransition = newTrans };
    }

    // ------------------------------------------------------------------ small edits
    /// <summary>Every CPFloatInRange condition on <paramref name="cp"/> with exactly [lo, hi] → [newLo, newHi], network-wide
    /// (net_clone.widen_band). Returns the number of conditions changed.</summary>
    public int WidenBand(string cp, float lo, float hi, float newLo, float newHi)
    {
        var x = (byte[])X.Clone(); var net = new NmbNetwork(x); var names = NmbNetEdit.NodeNames(x); int n = 0;
        foreach (var node in net.Nodes().Where(nd => nd.Type == 10))
        {
            int A = node.Attribs.First(a => a.Type == 38).Offset;
            int ncond = U16(x, A + 0x2A); long pC = (long)BinaryPrimitives.ReadUInt64LittleEndian(x.AsSpan(A + 0x30));
            for (int k = 0; k < ncond; k++)
            {
                int c = A + (int)(long)BinaryPrimitives.ReadUInt64LittleEndian(x.AsSpan(A + (int)pC + 8 * k));
                if (U16(x, c) != 627) continue;
                if (!names.TryGetValue(U16(x, c + 0x48), out var nm) || nm != "ControlParameters|" + cp) continue;
                if (BinaryPrimitives.ReadSingleLittleEndian(x.AsSpan(c + 0x4C)) != lo || BinaryPrimitives.ReadSingleLittleEndian(x.AsSpan(c + 0x50)) != hi) continue;
                BinaryPrimitives.WriteSingleLittleEndian(x.AsSpan(c + 0x4C), newLo); BinaryPrimitives.WriteSingleLittleEndian(x.AsSpan(c + 0x50), newHi); n++;
            }
        }
        X = x;
        return n;
    }

    /// <summary>The category states of a band SM: (category value, category SM node, the condition byte offsets in the network)
    /// for every transition whose AttackCategory_Ene condition is a single value.</summary>
    public List<(float Value, int Node, List<int> CondOffsets)> Categories(int bandSm)
    {
        var x = X; var net = new NmbNetwork(x); var names = NmbNetEdit.NodeNames(x);
        int A = net.GetNode(bandSm).Attribs.First(a => a.Type == 38).Offset;
        var sm = NmbNetEdit.ReadSmDef(x, A);
        long pC = (long)BinaryPrimitives.ReadUInt64LittleEndian(x.AsSpan(A + 0x30));
        var o = new List<(float, int, List<int>)>();
        foreach (var st in sm.States.Where(s => net.GetNode(s.Node).Type is 400 or 402 && s.Dst != 0xFFFF && s.Dst != 0xCDCD))
        {
            var offs = new List<int>(); float val = float.NaN;
            foreach (int ci in st.Entry)
            {
                int c = A + (int)(long)BinaryPrimitives.ReadUInt64LittleEndian(x.AsSpan(A + (int)pC + 8 * ci));
                if (U16(x, c) == 627 && names.TryGetValue(U16(x, c + 0x48), out var nm) && nm == "ControlParameters|AttackCategory_Ene")
                {
                    float lo = BinaryPrimitives.ReadSingleLittleEndian(x.AsSpan(c + 0x4C)), hi = BinaryPrimitives.ReadSingleLittleEndian(x.AsSpan(c + 0x50));
                    if (lo == hi) { val = lo; offs.Add(c); }
                }
            }
            if (!float.IsNaN(val)) o.Add((val, sm.States[st.Dst].Node, offs));
        }
        return o;
    }

    /// <summary>Re-point a category: its single-value AttackCategory_Ene conditions get <paramref name="value"/> (used to retire a
    /// template category by moving it to a value no action requests).</summary>
    public void SetCategoryValue(List<int> condOffsets, float value)
    {
        var x = X;
        foreach (int c in condOffsets) { BinaryPrimitives.WriteSingleLittleEndian(x.AsSpan(c + 0x4C), value); BinaryPrimitives.WriteSingleLittleEndian(x.AsSpan(c + 0x50), value); }
    }

    // ------------------------------------------------------------------ clips, tracks, leaves
    public static uint Crc32Bzip2(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= (uint)b << 24;
            for (int k = 0; k < 8; k++) crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04C11DB7 : crc << 1;
        }
        return crc ^ 0xFFFFFFFF;
    }

    /// <summary>NSA file name by DS2's rule (NETWORK-FORMAT.md §2.6): (anim + "_" + take)[:37 - len(hex)] + "_" + hex + ".nsa",
    /// hex = CRC-32/BZIP2 of the NSA bytes (the suffix DS2 uses is a per-project build value, not derivable: any unique hex).</summary>
    public static string NsaName(string anim, string take, uint crc)
    {
        string hx = crc.ToString("x");
        string stem = anim + "_" + take;
        return stem[..Math.Min(stem.Length, 37 - hx.Length)] + "_" + hx + ".nsa";
    }

    /// <summary>Append clips to the file-name table (bundle 13). Returns the first new clip index.</summary>
    public int AddClips(IReadOnlyList<(string File, string Source, string Take, uint Crc)> entries)
    {
        int i = Nmb.IndexOf(13);
        var fn = NmbFileNamesFull.Read(Nmb.Bundles[i].Data);
        int first = fn.Files.Count;
        foreach (var (f, s, t, c) in entries) { fn.Files.Add(f); fn.Formats.Add("nsa"); fn.Sources.Add(s); fn.Takes.Add(t); fn.Hashes.Add(c); }
        Nmb.Bundles[i].Data = fn.Write();
        return first;
    }

    static uint NewAssetId(string key, HashSet<uint> used)
    {
        uint id = BinaryPrimitives.ReadUInt32LittleEndian(SHA1.HashData(Encoding.UTF8.GetBytes(key)));
        while (id == 0 || used.Contains(id)) id = id * 1664525 + 1013904223;
        used.Add(id);
        return id;
    }

    /// <summary>A new event-track bundle: the template track's bundle header (type, version, alignment, flags) and track fields
    /// (channel id, userData, runtime track id, name), new assetId and GUID (derived from <paramref name="key"/>), new events.
    /// Inserted after the last existing event-track bundle (DS2 order: header, controller, rig, tracks, channel map, network,
    /// file names). Returns the new assetId.</summary>
    public uint AddTrack(uint templateId, List<(float Start, float Duration, int Value)> events, string key)
    {
        var used = Nmb.Bundles.Select(b => b.AssetId).ToHashSet();
        var tb = Nmb.Bundles.First(b => b.AssetId == templateId && b.Type is 3 or 4 or 5);
        var tr = NmbEventTrack.Read(tb.Data, tb.Type);
        tr.Events = events;
        uint id = NewAssetId(key, used);
        var guid = SHA1.HashData(Encoding.UTF8.GetBytes("guid:" + key))[..16];
        var nb = new NmbFile.Bundle { Magic = tb.Magic, Version = tb.Version, Type = tb.Type, AssetId = id, Guid = guid, Align = tb.Align, U2C = tb.U2C, Data = tr.Write() };
        int lastTrack = Nmb.Bundles.FindLastIndex(b => b.Type is 3 or 4 or 5);
        Nmb.Bundles.Insert(lastTrack + 1, nb);
        return id;
    }

    public NmbEventTrack Track(uint id)
    {
        var b = Nmb.Bundles.First(bb => bb.AssetId == id && bb.Type is 3 or 4 or 5);
        return NmbEventTrack.Read(b.Data, b.Type);
    }

    /// <summary>Point anim node <paramref name="node"/> at clip <paramref name="clip"/>: clip index, SourceAnim clip part from the
    /// decoded NSA (trajectory at fractions 0/1, duration, fractions 0/1: the rule DS2 follows on 49,897/49,897 anim nodes), and
    /// its event-track list re-pointed through <paramref name="trackMap"/> (old assetId → new, with the new bundle sizes; the
    /// attrib keeps its size, so the node block does not move).</summary>
    public void RetargetLeaf(int node, int clip, NsaAnim back, IReadOnlyDictionary<uint, uint> trackMap, int syncIndex)
    {
        var x = X; var net = new NmbNetwork(x);
        var n = net.GetNode(node);
        var sa = NmbNetwork.Find(n, 21) ?? throw new InvalidDataException($"node {node} has no SourceAnim");
        BinaryPrimitives.WriteUInt32LittleEndian(x.AsSpan(sa.Offset + 0x74), (uint)clip);
        net.PatchSourceAnim(sa, back.RootPos[0], back.RootRot[0], back.RootPos[^1], back.RootRot[^1], (back.Frames - 1) / back.Fps);
        net.PutF32(sa.Offset + 0x80, 0f); net.PutF32(sa.Offset + 0x84, 1f);
        BinaryPrimitives.WriteInt32LittleEndian(x.AsSpan(sa.Offset + 0x8C), syncIndex);
        var et = NmbNetwork.Find(n, 23);
        if (et != null)
        {
            int e = et.Offset;
            for (int k = 0; k < 3; k++)
            {
                int cnt = (int)net.U32(e + 0x10 + 0x18 * k);
                long pid = (long)net.U64(e + 0x18 + 0x18 * k), psz = (long)net.U64(e + 0x20 + 0x18 * k);
                for (int j = 0; j < cnt; j++)
                {
                    uint old = net.U32(e + (int)pid + 8 * j);
                    if (!trackMap.TryGetValue(old, out uint nw)) continue;
                    net.PutU32(e + (int)pid + 8 * j, nw);
                    if (psz != 0) net.PutU32(e + (int)psz + 4 * j, (uint)Nmb.Bundles.First(b => b.AssetId == nw).Data.Length);
                }
            }
        }
        X = x;
    }
}

/// <summary>The whole file-name table (NMB bundle 13; nmb_rig.read_filenames_all / write_filenames, session 8): four string
/// tables (NSA files, formats, source paths, takes) and the u32 CRC-32/BZIP2 per clip. Padding between the tables is 0xCD in
/// 160 DS2 files and 0x00 in 13 (both re-emit byte-identically; the writer keeps the file's own).</summary>
public sealed class NmbFileNamesFull
{
    public List<string> Files = [], Formats = [], Sources = [], Takes = [];
    public List<uint> Hashes = [];
    public byte Pad;

    public static NmbFileNamesFull Read(byte[] d)
    {
        var f = new NmbFileNamesFull
        {
            Files = NmbFile.ReadStringTable(d, (int)NmbFile.U64(d, 0)).Names, Formats = NmbFile.ReadStringTable(d, (int)NmbFile.U64(d, 8)).Names,
            Sources = NmbFile.ReadStringTable(d, (int)NmbFile.U64(d, 16)).Names, Takes = NmbFile.ReadStringTable(d, (int)NmbFile.U64(d, 24)).Names,
        };
        int ph = (int)NmbFile.U64(d, 32);
        for (int i = 0; i < f.Files.Count; i++) f.Hashes.Add(NmbFile.U32(d, ph + 4 * i));
        f.Pad = 0;
        if (!f.Write().AsSpan().SequenceEqual(d)) f.Pad = 0xCD;
        return f;
    }

    static byte[] StrTable(List<string> names)
    {
        int n = names.Count;
        var data = new List<byte>(); var offs = new List<int>();
        foreach (var s in names) { offs.Add(data.Count); data.AddRange(Encoding.ASCII.GetBytes(s)); data.Add(0); }
        var o = new List<byte>();
        o.AddRange(BitConverter.GetBytes((uint)n)); o.AddRange(BitConverter.GetBytes((uint)data.Count));
        o.AddRange(BitConverter.GetBytes(0x20UL)); o.AddRange(BitConverter.GetBytes((ulong)(0x20 + 4 * n))); o.AddRange(BitConverter.GetBytes((ulong)(0x20 + 8 * n)));
        for (int i = 0; i < n; i++) o.AddRange(BitConverter.GetBytes((uint)i));
        foreach (int off in offs) o.AddRange(BitConverter.GetBytes((uint)off));
        o.AddRange(data);
        return o.ToArray();
    }

    public byte[] Write()
    {
        var o = new List<byte>(new byte[0x28]); var ptrs = new List<long>();
        var tabs = new[] { Files, Formats, Sources, Takes };
        for (int k = 0; k < 4; k++)
        {
            if (k > 0) while (o.Count % 4 != 0) o.Add(Pad);
            ptrs.Add(o.Count); o.AddRange(StrTable(tabs[k]));
        }
        ptrs.Add(o.Count);
        foreach (uint h in Hashes) o.AddRange(BitConverter.GetBytes(h));
        var r = o.ToArray();
        for (int k = 0; k < 5; k++) BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(8 * k), (ulong)ptrs[k]);
        return r;
    }
}
