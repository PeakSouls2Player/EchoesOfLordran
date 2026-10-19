using System.Buffers.Binary;
using Lordran.Tools.Build.Chr;
using Lordran.Tools.Formats;
using Lordran.Tools.Formats.Morpheme;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// ds1rig-check section 8 (session 8, track enemies step 1): offline checks of a 1:1 character's DS1 attack layer
/// (<see cref="Ds1Attacks"/>) and, with --pack, of its params (<see cref="Ds1AttackParams"/>).
/// <list type="number">
/// <item>network structure (a C# subset of nmb_net_validate.py's strict checks): block model re-emits, every transition / state
/// listed in an SM has that SM as parent, transition records resolve to the transit node's own [source, destination], every
/// "on request" condition's request reaches that SM, clip indices and track ids resolve, attrib blobs 16-aligned;</item>
/// <item>SM_Attack routing: under the character's weapon category exactly one category per DS1 attack (n = A − 3000), per DS1
/// chain (30 + k) and per interim alias, the template's categories retired (≥ 900), every stage leaf plays the planned DS1
/// clip;</item>
/// <item>attack tracks vs the DS1 TAE: HitDuration = DS1 hit windows; AttackStateControl partitions [0,1] as 106 [0, first hit)
/// 100 101 102 with 101 at DS1's continuation flag; cancel controls / AttackDuration end at DS1's AI cancels; return to base
/// not before DS1's movement cancel; TimeAct = the clip's TAE id; Footsteps sync [0, first hit) [first hit, 1];</item>
/// <item>TimeAct: every attack TAE animation exists in _pl/_sfx/_snd with the clip's length; its 2200 events = DS1's hit
/// windows with the clip's hit slot as ID, 2300 = DS1's bullet events; in every chain stage s plays a clip whose hit slot is s−1
/// (combo AttackId0s);</item>
/// <item>the model: DS1 weapon meshes on the planned template materials (culling ids), only on weapon bones; the DS1 dummies the
/// hit capsules use exist.</item>
/// </list>
/// </summary>
public static class Ds1AttackCheck
{
    static double F(double s, double d) => d > 0 ? Math.Clamp(s / d, 0, 1) : 0;

    public static void Network(string dir, ChrPorter.Options o, ChrPorter.OneToOne rig, Ds1ChrSource ds1, MorphemeSkeleton skel, Action<bool, string> Check)
    {
        var plan = rig.Attacks!;
        string gd = Path.Combine(dir, "gamedata");
        string anibnd = Directory.GetFiles(Path.Combine(gd, "morpheme4", "chr"), "*.anibnd.dcx").Single();
        var files = MorphemeTools.LoadFiles(anibnd);
        var nmbBytes = files.First(f => f.Name.EndsWith(".nmb")).Bytes;
        var nmb = NmbFile.Read(nmbBytes);
        var x = nmb.Bundles[nmb.IndexOf(10)].Data;
        var net = new NmbNetwork(x);
        var fn = NmbFileNamesFull.Read(nmb.Bundles[nmb.IndexOf(13)].Data);
        var trackOf = new Dictionary<uint, NmbEventTrack>();
        foreach (var b in nmb.Bundles.Where(b => b.Type is 3 or 4 or 5)) trackOf[b.AssetId] = NmbEventTrack.Read(b.Data, b.Type);
        Console.WriteLine("  -- 8. DS1 attack layer");

        // ---- 8a. structure
        Check(NmbNetEdit.Emit(NmbNetEdit.BuildItems(x)).AsSpan().SequenceEqual(x), "network block model re-emits byte-identically (NmbNetEdit)");
        int trans = 0, transBad = 0, states = 0, statesBad = 0, reqs = 0, reqBad = 0, clipsBad = 0, tracksBad = 0, align = 0, alignBad = 0;
        var dists = new Dictionary<uint, HashSet<int>>();
        int nm = (int)net.U32(0xF8), pM = (int)net.U64(0x100);
        for (int i = 0; i < nm; i++)
        {
            int p = (int)net.U64(pM + 8 * i);
            int cnt = (int)net.U32(p + 8); int p1 = (int)net.U64(p + 0x10);
            dists[net.U32(p)] = [.. Enumerable.Range(0, cnt).Select(k => (int)net.U16(p + p1 + 2 * k))];
        }
        foreach (var n in net.Nodes())
        {
            foreach (var a in n.Attribs) { align++; if (a.Offset % 16 != 0) alignBad++; }
            if (n.Type == 104)
            {
                var sa = NmbNetwork.Find(n, 21);
                if (sa != null && net.ClipIndex(sa) >= fn.Files.Count) clipsBad++;
                var et = NmbNetwork.Find(n, 23);
                if (et != null) foreach (var (_, tid, _) in net.Tracks(et)) if (!trackOf.ContainsKey(tid)) tracksBad++;
            }
            if (n.Type != 10) continue;
            var def = NmbNetEdit.ReadSmDef(x, n.Attribs.First(a => a.Type == 38).Offset);
            foreach (var st in def.States)
            {
                var sn = net.GetNode(st.Node);
                if (sn.Type is 400 or 402)
                {
                    trans++;
                    bool ok = sn.Parent == n.Index && sn.Children.Length == 2
                        && (st.Src == 0xFFFF ? sn.Children[0] == 0xFFFF : st.Src < def.States.Count && def.States[st.Src].Node == sn.Children[0])
                        && st.Dst < def.States.Count && def.States[st.Dst].Node == sn.Children[1];
                    if (!ok) transBad++;
                }
                else { states++; if (sn.Parent != n.Index) statesBad++; }
            }
            foreach (var c in def.Conds)
            {
                if (BitConverter.ToUInt16(c, 0) != 601) continue;
                reqs++;
                uint rq = BitConverter.ToUInt32(c, 0x48);
                if (!dists.TryGetValue(rq, out var l) || !l.Contains(n.Index)) reqBad++;
            }
        }
        Check(transBad == 0 && statesBad == 0, $"state machines: {trans} transitions (parent = their SM, records = the transit node's [source, destination]), {states} states with their SM as parent; bad {transBad + statesBad}");
        Check(reqBad == 0, $"{reqs} on-request conditions name a request whose distributor lists their SM; bad {reqBad}");
        Check(clipsBad == 0 && tracksBad == 0, $"anim nodes: clip indices < {fn.Files.Count} file-name entries, every event-track id resolves (bad {clipsBad} / {tracksBad})");
        Check(alignBad == 0, $"{align} attrib blobs 16-aligned in absolute bundle terms; bad {alignBad}");
        Check(fn.Files.Count == fn.Hashes.Count && files.Count(f => f.Name.EndsWith(".nsa")) == fn.Files.Count
            && fn.Files.Select((f, i) => (f, i)).Where(e => System.Text.RegularExpressions.Regex.IsMatch(e.f, @"^a65_\d\d_\d{4}_untitled_[0-9a-f]+\.nsa$")).All(e => files.FirstOrDefault(ff => ff.Name == e.f).Bytes is { } nb && NmbNetClone.Crc32Bzip2(nb) == fn.Hashes[e.i]),
            $"file-name table: {fn.Files.Count} clips = the anibnd's NSA files; every DS1 attack clip's hash = CRC-32/BZIP2 of its bytes (the template slots keep DS2's hash: the runtime looks clips up by name, c2501 verified in game with them)");

        // ---- 8b. routing
        var ed = new NmbNetClone(nmbBytes);
        var names = NmbNetEdit.NodeNames(x);
        int smAttack = names.First(kv => kv.Value == "SM_Main|SM_Attack").Key;
        List<(int Node, int Src, int DstState, int DstNode, List<(string Cp, float Lo, float Hi, int Type)> Conds)> Tr(int sm)
        {
            var def = ed.Sm(sm); var o2 = new List<(int, int, int, int, List<(string, float, float, int)>)>();
            foreach (var st in def.States.Where(s => net.GetNode(s.Node).Type is 400 or 402))
            {
                var conds = st.Entry.Select(ci =>
                {
                    var c = def.Conds[ci]; int t = BitConverter.ToUInt16(c, 0);
                    string cp = t is 627 or 625 && names.TryGetValue(BitConverter.ToUInt16(c, 0x48), out var nn) ? nn.Replace("ControlParameters|", "") : "";
                    return (cp, t == 627 ? BitConverter.ToSingle(c, 0x4C) : t == 625 ? BitConverter.ToInt32(c, 0x4C) : 0f, t == 627 ? BitConverter.ToSingle(c, 0x50) : t == 625 ? BitConverter.ToInt32(c, 0x50) : 0f, t);
                }).ToList();
                o2.Add((st.Node, st.Src, st.Dst, st.Dst < def.States.Count ? def.States[st.Dst].Node : -1, conds));
            }
            return o2;
        }
        var weaponSm = Tr(smAttack).First(t => t.Conds.Any(c => c.Cp == "WeaponCategory_Ene" && c.Lo == plan.WeaponCategory)).DstNode;
        var bands = Tr(weaponSm).Where(t => t.Conds.Any(c => c.Cp == "AttackCategory_Ene" && c.Lo != c.Hi)).Select(t => (t.DstNode, t.Conds.First(c => c.Cp == "AttackCategory_Ene"))).ToList();
        Check(bands.Any(b => b.Item2.Lo == 0 && b.Item2.Hi == 9) && bands.Any(b => b.Item2.Lo == 10 && b.Item2.Hi == 39), $"weapon category {plan.WeaponCategory} bands: {string.Join(", ", bands.Select(b => $"[{b.Item2.Lo},{b.Item2.Hi}]"))} ([10,19] widened to [10,39])");
        var cats = bands.SelectMany(b => ed.Categories(b.DstNode).Select(c => (c.Value, c.Node))).ToList();
        var expect = new Dictionary<int, int[]>();
        foreach (var a in plan.Attacks) expect[plan.ActionOf(a.Id)] = [plan.Clip(a.Id, ds1)];
        for (int k = 0; k < plan.Chains.Length; k++) expect[30 + k] = [.. plan.Chains[k].Ids.Select(i => plan.Clip(i, ds1))];
        foreach (var al in plan.LiveAliases) if (!expect.ContainsKey(al.Action)) expect[al.Action] = [plan.Clip(al.Id, ds1)];
        var live = cats.Where(c => c.Value < 900).ToList();
        Check(live.Select(c => (int)c.Value).Order().SequenceEqual(expect.Keys.Order()) && live.Count == expect.Count,
            $"categories under weapon {plan.WeaponCategory}: [{string.Join(", ", live.Select(c => c.Value).Order())}] = DS1 attacks n = A−3000, chains 30+k, aliases; {cats.Count(c => c.Value >= 900)} template categories retired (≥ 900)");
        int AttackClip(string nm2)
        {
            var m = System.Text.RegularExpressions.Regex.Match(nm2, @"^a65_(\d\d)_(\d{4})_untitled_");
            return m.Success ? int.Parse(m.Groups[1].Value) * 10000 + int.Parse(m.Groups[2].Value) : -1;
        }
        // session 11 (burgchr11, rule CH5): the damage slot each (category, stage) plays: a chain stage = the first stage of its id in
        // the chain; the clip's primary slot = its first chain stage (in plan order); a single attack = the primary when ≤ 2, else 0
        var hitSlot = new Dictionary<int, int>();
        foreach (var ch in plan.Chains) for (int s2 = 0; s2 < ch.Ids.Length; s2++) hitSlot.TryAdd(plan.Clip(ch.Ids[s2], ds1), Array.IndexOf(ch.Ids, ch.Ids[s2]));
        var expectSlot = new Dictionary<int, int[]>();
        foreach (var a in plan.Attacks) { int c0 = plan.Clip(a.Id, ds1); expectSlot[plan.ActionOf(a.Id)] = [hitSlot.GetValueOrDefault(c0) <= 2 ? hitSlot.GetValueOrDefault(c0) : 0]; }
        for (int k = 0; k < plan.Chains.Length; k++) expectSlot[30 + k] = [.. plan.Chains[k].Ids.Select(i => Array.IndexOf(plan.Chains[k].Ids, i))];
        foreach (var al in plan.LiveAliases) if (!expectSlot.ContainsKey(al.Action)) { int c0 = plan.Clip(al.Id, ds1); expectSlot[al.Action] = [hitSlot.GetValueOrDefault(c0) <= 2 ? hitSlot.GetValueOrDefault(c0) : 0]; }
        int TaeOf(int clip, int slot) => slot == hitSlot.GetValueOrDefault(clip) ? 65_000_000 + clip / 10000 * 10000 + clip % 10000 : Ds1Attacks.AltTaeId(clip, slot);
        // stage leaves per category (follow the Combo{s}_Route transitions from the default state)
        int stagesOk = 0, stagesBad = 0; string stageNote = "";
        var leafClip = new Dictionary<int, int>();
        var leafSlot = new Dictionary<int, int>();
        foreach (var (val, node) in live)
        {
            var def = ed.Sm(node); var tr = Tr(node);
            var st = new List<int> { def.States[def.Default].Node }; int cur = def.Default;
            for (int s2 = 1; s2 < 8; s2++)
            {
                var t = tr.FirstOrDefault(q => q.Src == cur && q.Conds.Any(c => c.Cp == $"Combo{s2}_Route"));
                if (t.Conds == null) break;
                st.Add(t.DstNode); cur = t.DstState;
            }
            var want = expect[(int)val];
            for (int s2 = 0; s2 < st.Count; s2++)
            {
                var leaves = Leaves(net, st[s2]).Where(l => AttackClip(fn.Files[net.ClipIndex(NmbNetwork.Find(net.GetNode(l), 21)!)]) >= 0).ToList();
                int wantClip = want[Math.Min(s2, want.Length - 1)];
                bool ok = leaves.Count == 1 && AttackClip(fn.Files[net.ClipIndex(NmbNetwork.Find(net.GetNode(leaves[0]), 21)!)]) == wantClip;
                if (ok) { stagesOk++; leafClip[leaves[0]] = wantClip; leafSlot[leaves[0]] = expectSlot[(int)val][Math.Min(s2, want.Length - 1)]; } else { stagesBad++; stageNote = $"category {val} stage {s2 + 1}"; }
            }
            if (st.Count < want.Length) { stagesBad++; stageNote = $"category {val}: {st.Count} stages < chain {want.Length}"; }
        }
        Check(stagesBad == 0, $"stage leaves: {stagesOk} play the planned DS1 clip (chains in route order){(stagesBad > 0 ? $" - bad: {stageNote}" : "")}");

        // ---- 8c. tracks vs DS1
        int tOk = 0, tBad = 0; var tNotes = new List<string>();
        var timing = new Dictionary<int, Ds1Attacks.ClipTiming>();
        foreach (var (leaf, clip) in leafClip)
        {
            var n = net.GetNode(leaf); var sa = NmbNetwork.Find(n, 21)!; var et = NmbNetwork.Find(n, 23)!;
            double d = net.ClipDuration(sa);
            if (!timing.TryGetValue(clip, out var t)) timing[clip] = t = Ds1Attacks.Timing(ds1, clip, d);
            double h0 = t.FirstHit >= 0 ? F(t.FirstHit, d) : 0, h1 = t.LastHit >= 0 ? F(t.LastHit, d) : h0;
            double rec = t.Recovery >= 0 ? Math.Max(F(t.Recovery, d), h1) : 1;
            int si = net.SyncTrackIndex(sa), disc = 0;
            var problems = new List<string>();
            foreach (var (g, tid, _) in net.Tracks(et))
            {
                var tr = trackOf[tid]; bool isSync = g == 0 && disc++ == si;
                var ev = tr.Events.OrderBy(e => e.Start).ToList();
                var vals = ev.Select(e => e.Value).Distinct().ToList();
                bool Near(double a, double b) => Math.Abs(a - b) < 2e-3;
                if (isSync) { if (!(Near(ev[0].Start, 0) && Near(ev[^1].Start + ev[^1].Duration, 1) && (ev.Count == 1 || Near(ev[1].Start, h0)))) problems.Add("sync"); }
                else if (tr.Name == "TimeAct") { if (ev.Count != 1 || ev[0].Value != TaeOf(clip, leafSlot[leaf])) problems.Add($"TimeAct {(ev.Count > 0 ? ev[0].Value : -1)} != {TaeOf(clip, leafSlot[leaf])}"); }
                else if (vals.Count > 0 && vals.All(v => v == 123))
                {
                    if (ev.Count != t.Hits.Count || !ev.Zip(t.Hits).All(p => Near(p.First.Start, F(p.Second.S, d)) && Near(p.First.Start + p.First.Duration, F(p.Second.E, d)))) problems.Add("HitDuration");
                }
                else if (vals.Count > 0 && vals.All(v => v is >= 100 and <= 106))
                {
                    bool part = Near(ev[0].Start, 0) && Near(ev[^1].Start + ev[^1].Duration, 1) && ev.Zip(ev.Skip(1)).All(p => Near(p.First.Start + p.First.Duration, p.Second.Start));
                    var e101 = ev.FirstOrDefault(e => e.Value == 101);
                    double c0 = t.Continuation.Count > 0 ? Math.Min(Math.Max(F(t.Continuation[0].S, d), h1), rec) : h1;
                    bool combo = e101.Value != 101 || Near(e101.Start, c0);
                    bool first = ev[0].Value != 106 || Near(ev[0].Duration, h0);
                    if (!part || !combo || !first) problems.Add($"AttackStateControl (partition {part}, 101 at continuation {combo}, 106 to first hit {first})");
                }
                else if (vals.Count > 0 && vals.All(v => v == 80))
                {
                    double mc = t.MoveCancel >= 0 ? Math.Max(F(t.MoveCancel, d), h1) : rec;
                    if (ev.Any(e => e.Start < mc - 2e-3)) problems.Add("return to base before DS1's movement cancel");
                }
                else if (vals.Count > 0 && vals.All(v => v == 112)) { if (!Near(ev[0].Duration, t.AttackCancel >= 0 ? Math.Max(F(t.AttackCancel, d), h1) : rec)) problems.Add("AttackCancelControl_Attack"); }
                else if (vals.Count > 0 && vals.All(v => v == 110)) { if (!Near(ev[0].Duration, t.DodgeCancel >= 0 ? Math.Max(F(t.DodgeCancel, d), h1) : rec)) problems.Add("AttackCancelControl_Avoid"); }
                else if (vals.Count > 0 && vals.All(v => v == 111)) { if (!Near(ev[0].Duration, t.MoveCancel >= 0 ? Math.Max(F(t.MoveCancel, d), h1) : rec)) problems.Add("AttackCancelControl_Move"); }
                else if (vals.Count > 0 && vals.All(v => v == 120)) { if (!Near(ev[0].Duration, rec)) problems.Add("AttackDuration"); }
            }
            if (problems.Count == 0) tOk++; else { tBad++; if (tNotes.Count < 6) tNotes.Add($"leaf {leaf} {Ds1ChrSource.ClipName(clip)}: {string.Join(", ", problems)}"); }
        }
        Check(tBad == 0, $"attack tracks vs the DS1 TAE: {tOk} stage leaves (HitDuration = DS1 hit windows, AttackStateControl 106/100/101/102 with 101 at DS1's continuation flag, cancels / AttackDuration at DS1's AI cancels, return to base ≥ DS1 movement cancel, TimeAct = TAE id, Footsteps [0, first hit) [first hit, 1]){(tBad > 0 ? " - " + string.Join("; ", tNotes) : "")}");

        // ---- 8d. TimeAct
        var taeDir = Path.Combine(gd, "timeact", "chr");
        var pl = TAE.Read(Directory.GetFiles(taeDir, "*_pl.tae").Single());
        var sfx = TAE.Read(Directory.GetFiles(taeDir, "*_sfx.tae").Single()); var snd = TAE.Read(Directory.GetFiles(taeDir, "*_snd.tae").Single());
        int taeOk = 0, taeBad = 0; var taeNotes = new List<string>();
        // rule CH5: every (clip, slot) the leaves play has its own animation whose 2200 IDs carry that slot
        foreach (var (clip, slot0) in leafClip.Select(kv => (kv.Value, leafSlot[kv.Key])).Distinct().OrderBy(x => x.Item1).ThenBy(x => x.Item2))
        {
            var t = timing[clip];
            long id = TaeOf(clip, slot0);
            var a = pl.Animations.FirstOrDefault(q => q.ID == id);
            int frames = (int)Math.Round(t.Duration * 30);
            bool ok = a != null && a.MiniHeader is TAE.Animation.AnimMiniHeader.Standard sh && sh.ImportHKXSourceAnimID == frames
                && sfx.Animations.Any(q => q.ID == id) && snd.Animations.Any(q => q.ID == id);
            if (ok)
            {
                var hits = a!.Events.Where(e => e.Type == 2200).OrderBy(e => e.StartTime).ToList();
                var buls = a.Events.Where(e => e.Type == 2300).OrderBy(e => e.StartTime).ToList();
                int slot = slot0;
                ok = hits.Count == t.Hits.Count && hits.Zip(t.Hits).All(p => Math.Abs(p.First.StartTime - p.Second.S) < 1e-4 && Math.Abs(p.First.EndTime - p.Second.E) < 1e-4 && BitConverter.ToInt32(p.First.GetParameterBytes(false), 0) == slot + Math.Max(0, t.Judges.IndexOf(p.Second.Judge)))
                    && buls.Count == t.Bullets.Count && buls.Zip(t.Bullets).All(p => Math.Abs(p.First.StartTime - p.Second.S) < 1e-4);
            }
            if (ok) taeOk++; else { taeBad++; taeNotes.Add($"{id}"); }
        }
        Check(taeBad == 0 && taeOk >= timing.Count, $"TimeAct: {taeOk} DS1 attack animations (incl. rule CH5 slot variants) in _pl/_sfx/_snd with the clip's length; 2200 = DS1 hit windows with the clip's hit slot as ID (chain stage s → slot s−1), 2300 = DS1 bullet events{(taeBad > 0 ? " - bad " + string.Join(", ", taeNotes) : "")}");
        var reread = Ds2TaeFile.Read(File.ReadAllBytes(Directory.GetFiles(taeDir, "*_pl.tae").Single()));
        Check(reread.Write().AsSpan().SequenceEqual(File.ReadAllBytes(Directory.GetFiles(taeDir, "*_pl.tae").Single())), "_pl TAE re-serialises byte-identically (Ds2TaeFile layout rules hold for the added animations)");

        // ---- 8e. model: weapon meshes and hit dummies
        string bndPath = Directory.GetFiles(Path.Combine(gd, "model", "chr"), "*.bnd").SingleOrDefault() ?? "";
        if (bndPath != "")
        {
            var bnd = BND4.Read(bndPath);
            var flv = FLVER2.Read(bnd.Files.First(f => f.Name.EndsWith(".flv") && !f.Name.EndsWith("_s.flv")).Bytes);
            var templ = FLVER2.Read(BND4.Read(Path.Combine(ExtractDs2.DefaultOut, "model", "chr", o.TemplateChr + ".bnd")).Files.First(f => f.Name.EndsWith($"{o.TemplateChr}.flv")).Bytes);
            var wantCull = rig.WeaponMeshes.Values.Select(m => Ds1RigModel.CullingOf(templ, m)).Order().ToList();
            var meshCull = flv.Meshes.Select(m => flv.Materials[m.MaterialIndex]).Select(mt => mt.GXIndex >= 0 && mt.GXIndex < flv.GXLists.Count && flv.GXLists[mt.GXIndex].FirstOrDefault(q => q.ID == "1") is { } it ? BitConverter.ToInt32(it.Data, 0) : -1).ToList();
            var weaponMeshes = flv.Meshes.Where((m, i) => meshCull[i] > 0).ToList();
            // a weapon bone: named so (R_Weapon, R_weapon2/3, L_Weapon, L_shield, bow_*) or under one (c2550's lance pennant W_Cloth1-3)
            bool IsWeaponBone(int b)
            {
                for (int x = b; x >= 0; x = flv.Nodes[x].ParentIndex)
                    if (System.Text.RegularExpressions.Regex.IsMatch(flv.Nodes[x].Name, "weapon|shield|bow", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
                return false;
            }
            Check(meshCull.Where(c => c > 0).Distinct().Order().SequenceEqual(wantCull.Where(c => c > 0).Distinct()) && weaponMeshes.All(m => m.BoneIndices.All(IsWeaponBone)),
                $"weapon meshes: culling ids [{string.Join(", ", meshCull.Where(c => c > 0).Distinct().Order())}] = planned [{string.Join(", ", wantCull)}], skinned only to weapon bones ({string.Join(", ", weaponMeshes.SelectMany(m => m.BoneIndices).Distinct().Select(b => flv.Nodes[b].Name))})");
        }
        var ds1Flv = FLVER2.Read(BND3.Read(Path.Combine(Program.DefaultDsrRoot, "chr", o.Ds1Chr + ".chrbnd.dcx")).Files.First(f => f.Name.EndsWith(".flver")).Bytes);
        var atkP = Formats.ParamStore.LoadDsr(Program.DefaultDsrRoot);
        var beh = atkP.Params["BehaviorParam"]; var atk = atkP.Params["AtkParam_Npc"];
        var dummies = new List<string>(); bool dmOk = true;
        foreach (var t in timing.Values)
            foreach (int j in t.Judges)
            {
                var be = beh[200000000 + plan.BehaviorVariation * 1000 + j]; if (be == null) { dmOk = false; dummies.Add($"no BehaviorParam for judge {j}"); continue; }
                var a = atk[Convert.ToInt32(be["refId"].Value)];
                if (a == null) { dmOk = false; continue; }
                int d1 = Convert.ToInt32(a["hit0_DmyPoly1"].Value), d2 = Convert.ToInt32(a["hit0_DmyPoly2"].Value);
                bool has = ds1Flv.Dummies.Any(q => q.ReferenceID == d1) && (d2 < 0 || ds1Flv.Dummies.Any(q => q.ReferenceID == d2));
                dmOk &= has;
                dummies.Add($"j{j}: dmy {d1}→{d2}{(has ? "" : " MISSING")}");
            }
        Check(dmOk, $"hit capsules: every DS1 judge's AtkParam dummies exist on the model ({string.Join(", ", dummies.Distinct())})");
    }

    static List<int> Leaves(NmbNetwork net, int root)
    {
        var o = new List<int>(); var seen = new HashSet<int>();
        void Rec(int i)
        {
            if (i == 0xFFFF || !seen.Add(i)) return;
            var n = net.GetNode(i);
            if (n.Type == 104) { o.Add(i); return; }
            if (n.Type is 400 or 402) return;
            foreach (int c in n.Children) Rec(c);
        }
        Rec(root);
        return o;
    }

    /// <summary>--pack: the armed variants' params (EnemyParam weapon slot, EnemyEquipWeaponParam, behaviour / combo rows,
    /// damage and bullet rows).</summary>
    public static void Pack(BND4 reg, List<PARAMDEF> defs, ChrPorter.OneToOne rig, Action<bool, string> Check)
    {
        var plan = rig.Attacks!;
        PARAM P(string name) { var p = PARAM.Read(reg.Files.First(f => Path.GetFileNameWithoutExtension(f.Name) == name).Bytes); p.ApplyParamdefCarefully(defs); return p; }
        var ep = P("EnemyParam"); var ew = P("EnemyEquipWeaponParam"); var dmg = P("EnemyDamageParam"); var bul = P("EnemyBulletParam");
        var bp = new[] { P("EnemyBehaviorParam"), P("EnemyBehaviorSecondParam"), P("EnemyBehaviorThirdParam") }; var cp = P("EnemyComboBehaviorParam");
        int I(PARAM.Row r, string f) => Convert.ToInt32(r[f].Value);
        var ds1 = Formats.ParamStore.LoadDsr(Program.DefaultDsrRoot);
        foreach (var v in rig.Armed)
        {
            var e = ep[v.EnemyParamId];
            int w = e != null ? I(e, "defaultEquipWeapon") : -1;
            int wid = e != null ? I(e, $"equipWeaponAId0{plan.WeaponCategory}") : -1;
            var wr = ew[wid];
            Check(e != null && w == plan.WeaponCategory && wid == v.EnemyParamId * 100 && Enumerable.Range(0, 6).Where(k => k != plan.WeaponCategory).All(k => I(e, $"equipWeaponAId0{k}") == 0)
                && wr != null && I(wr, "behaviorId") == v.EnemyParamId && I(wr, "cullingId00") == (v.CullingIds.Length > 0 ? v.CullingIds[0] : 0) && I(wr, "cullingId01") == (v.CullingIds.Length > 1 ? v.CullingIds[1] : 0),
                $"EnemyParam {v.EnemyParamId}: weapon slot {w} = EnemyEquipWeaponParam {wid} (behaviour {(wr != null ? I(wr, "behaviorId") : -1)}, culling {(wr != null ? $"{I(wr, "cullingId00")}/{I(wr, "cullingId01")}" : "-")})");
            // actions
            var actionOf = new Dictionary<int, int>();
            foreach (var a in plan.Attacks) actionOf[plan.ActionOf(a.Id)] = a.Id;
            foreach (var al in plan.LiveAliases) actionOf.TryAdd(al.Action, al.Id);
            int actOk = 0, actBad = 0; var notes = new List<string>();
            var ds1Npc = ds1.Params["NpcParam"][v.Ds1Npc];
            int variation = Convert.ToInt32(ds1Npc["behaviorVariationId"].Value);
            for (int n = 0; n < 30; n++)
            {
                var r = bp[n / 10][v.EnemyParamId]; int k = n % 10;
                if (r == null) { actBad++; continue; }
                bool active = I(r, $"action{k}BladeCtrlType") != 4;
                if (active != actionOf.ContainsKey(n)) { actBad++; notes.Add($"action {n} active {active}"); continue; }
                if (!active) { actOk++; continue; }
                var dmgIds = Enumerable.Range(0, 3).Select(s => I(r, $"action{k}DamageId0{s}")).Where(x => x > 0).ToList();
                var bulIds = Enumerable.Range(0, 3).Select(s => I(r, $"action{k}BulletId0{s}")).Where(x => x > 0).ToList();
                // a DS1 attack without hit or bullet events (c2550 3200, a shield stance) has no damage rows
                bool ok = dmgIds.All(id => dmg[id] != null) && bulIds.All(id => bul[id] != null && dmg[id] != null);
                if (ok) actOk++; else { actBad++; notes.Add($"action {n}: damage [{string.Join(",", dmgIds)}] bullets [{string.Join(",", bulIds)}]"); }
            }
            Check(actBad == 0, $"behaviour rows {v.EnemyParamId}: actions active exactly for DS1 attacks/aliases [{string.Join(", ", actionOf.Keys.Order())}], each with existing damage / bullet rows ({actOk}/30 ok){(notes.Count > 0 ? " - " + string.Join("; ", notes.Take(5)) : "")}");
            var c = cp[v.EnemyParamId];
            bool combosOk = c != null;
            for (int k = 0; k < 7 && c != null; k++)
            {
                bool active = I(c, $"combo{k}BladeCtrlType") != 4;
                if (k >= plan.Chains.Length) { combosOk &= !active; continue; }
                var ch = plan.Chains[k];
                combosOk &= active && I(c, $"combo{k}AttackMotionId") == 30 + k && Enumerable.Range(1, 3).All(s => I(c, $"combo{k}ComboAttackRoute{s}") == (s < ch.Ids.Length ? 1 : 0));
                for (int s = 0; s < ch.Ids.Length; s++) { int id = I(c, $"combo{k}AttackId0{s + 1}"); int b = I(c, $"combo{k}BulletId0{s + 1}"); combosOk &= id <= 0 || dmg[id] != null; combosOk &= b <= 0 || bul[b] != null; }
            }
            Check(combosOk, $"combo row {v.EnemyParamId}: combos 0-{plan.Chains.Length - 1} = DS1 chains (AttackMotionId 30+k, routes 1 per next stage, AttackId0s per stage), the rest disabled");
            // damage values: DS1 AtkParam × calibration
            int dOk = 0, dBad = 0;
            foreach (var be in ds1.Params["BehaviorParam"].Rows.Where(b => Convert.ToInt32(b["variationId"].Value) == variation))
            {
                var row = dmg[Ds1AttackParams.EnemyRow(ds1.Params["BehaviorParam"], variation, be.ID % 1000, 0)]; if (row == null) continue;   // session 12, rule E-ID1
                if (Convert.ToInt32(be["refType"].Value) != 0) continue;
                var a = ds1.Params["AtkParam_Npc"][Convert.ToInt32(be["refId"].Value)]; if (a == null) continue;
                double phys = Convert.ToDouble(a["atkPhys"].Value);
                bool ok = phys <= 0 || I(row, "damage01") == (int)Math.Round(phys * Ds1AttackParams.DamageScale) && I(row, "damageType01") == 0;
                if (ok) dOk++; else dBad++;
            }
            Check(dBad == 0 && dOk > 0, $"EnemyDamageParam rows of DS1 variation {variation}: {dOk} melee rows = DS1 atkPhys × {Ds1AttackParams.DamageScale:0.##} (physical)");
            // bullets: explosion child
            var bRows = bul.Rows.Where(r => Ds1AttackParams.FromEnemyRow(ds1.Params["BehaviorParam"], r.ID) is { } q && q.Variation == variation && q.K == 0).ToList();   // session 12, rule E-ID1
            bool bOk = bRows.All(r => dmg[r.ID] != null && (I(r, "childeBulletId") == 0 || bul[I(r, "childeBulletId")] != null && dmg[I(r, "childeDamageId")] != null));
            Check(bOk, $"EnemyBulletParam rows of DS1 variation {variation}: {bRows.Count} ({string.Join(", ", bRows.Select(r => $"{r.ID} sfx {I(r, "bulletSfxId")}{(I(r, "childeBulletId") > 0 ? $" → explosion {I(r, "childeBulletId")} (damage {I(dmg[I(r, "childeDamageId")]!, "damage01")} type {I(dmg[I(r, "childeDamageId")]!, "damageType01")}, radius {dmg[I(r, "childeDamageId")]!["radius"].Value})" : "")}"))}); every row with its damage row and child");
        }
    }
}
