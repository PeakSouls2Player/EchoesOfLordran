using Lordran.Tools.Formats;
using Lordran.Tools.Formats.Morpheme;
using SoulsFormats;
// merge stage 5: the boss track's network / TAE code lives in its own namespaces (the enemies track has
// classes of the same names in Formats.Morpheme / Formats); see MERGE-NOTES.md "Unification backlog"
using NmbNetEdit = Lordran.Tools.Formats.Morpheme.Boss.NmbNetEdit;

namespace Lordran.Tools.Commands;

/// <summary>
/// Step-2 checks of the 1:1 boss (session 8, track boss), run by <c>ds1boss-check</c> after its step-1 checks:
/// <list type="number">
/// <item>network: every leaf playing a DS1 clip with "Ignore Gravity" (JT 27) / "Disable Map/Object Collision" (JT 19)
/// windows carries a Gravity (userData 9600, value 9610) / HitControl (9700, 9710) track with exactly those windows; the
/// tracks' runtime ids are in the network's track-name table; every SourceEventTracks attrib is in DS2's layout
/// (re-writes byte-identically, the rule <c>nmb-track-attrib-selftest</c> proves on all 53,008 vanilla attribs);</item>
/// <item>AI ESDs (lower ai{Lower}.esd, upper ai{Upper}.esd): every group has ≥ 2 states and no entry commands in state 0,
/// every target state exists, target-less conditions have pass commands or subconditions, every machine call names a group of
/// the same file, the generated groups' expressions are well-formed (stack depth 1 at A1, parameters p0-p7 only in
/// machines), DS2's roll slots 0-3 and counter slots 0-7, the upper logic's lower dispatch (5301 group ids) exists in the
/// lower logic, the template library machines are byte-identical to the template's;</item>
/// <item>with --pack: the AI's ids exist (5204 actions through the attack machine have a behaviour/combo row, f7070 / 7005 /
/// f7072 EventLocations exist in the map), EnemyDefaultLogicParam / LogicComParam rows, the boss generator (GeneratorParam:
/// event group 10, boss flags 0x6C, logic flag; GeneratorRegistParam: logic ids, start-up 0), BossBattleParam (souls, killed
/// flag, name text), the boss events' groups (11810310 sets the start-up state and the logic flag) and EnemyParam souls 0.</item>
/// </list>
/// </summary>
public static partial class BossTools
{
    static void Ds1BossCheckStep2(string dir, string? packDir, Action<bool, string> check)
    {
        var spec = CheckSpec;   // session 11 (ret11): the boss under check (--boss / boss.txt; default the Asylum Demon)
        string gd = Path.Combine(dir, "gamedata");
        // ---- network tracks
        string anibnd = Directory.GetFiles(Path.Combine(gd, "morpheme4", "chr"), "*.anibnd.dcx").Single();
        var files = MorphemeTools.LoadFiles(anibnd);
        var nmb = NmbFile.Read(files.First(f => f.Name.EndsWith(".nmb")).Bytes);
        var x = nmb.Bundles[nmb.IndexOf(10)].Data;
        var net = new NmbNetwork(x);
        var fn = NmbNetEdit.ReadFileNames(nmb.Bundles[nmb.IndexOf(13)].Data);
        var tracks = nmb.Bundles.Where(b => b.Type is 3 or 4 or 5).ToDictionary(b => b.AssetId, b => NmbEventTrack.Read(b.Data, b.Type));
        var src = Build.Chr.Ds1ChrSource.Load(Program.DefaultDsrRoot, spec.Ds1Chr);
        var trackNames = NmbNetEdit.TrackNameIds(x);
        int leaves = 0, wantG = 0, haveG = 0, wantH = 0, haveH = 0, layout = 0, attribs = 0;
        var bad = new List<string>();
        foreach (var node in net.Nodes().Where(n => n.Type == 104))
        {
            var et = NmbNetwork.Find(node, 23);
            if (et != null)
            {
                attribs++;
                var blob = x[et.Offset..(et.Offset + (int)et.Size)];
                if (NmbNetEdit.WriteTrackAttrib(NmbNetEdit.ReadTrackAttrib(blob)).AsSpan().SequenceEqual(blob)) layout++;
            }
            var sa = NmbNetwork.Find(node, 21);
            if (sa == null || et == null) continue;
            string clip = fn.Files[net.ClipIndex(sa)];
            int ds1 = -1;
            if (clip.StartsWith(spec.N + "_a00_", StringComparison.OrdinalIgnoreCase)) ds1 = int.Parse(clip.Substring(spec.N.Length + 5, 4));
            else if (Build.Chr.Ds1RigAnims.RoleFor(spec.Anim.Roles, clip) is { } role) ds1 = role.Ds1.FirstOrDefault(v => v >= 0 && src.Has(v), -1);
            if (ds1 < 0) continue;
            leaves++;
            var tae = src.TaeAnim(ds1);
            var samples = src.Samples(ds1);
            double d1 = samples.Duration;
            var mine = net.Tracks(et).Select(t => tracks.TryGetValue(t.TrackId, out var tr) ? tr : null).Where(t => t != null).ToList();
            foreach (var (ud, val, jt) in new[] { (9600, 9610, 27), (9700, 9710, 19) })
            {
                var w = Build.Chr.Ds1RigAnims.JumpTable(tae, jt);
                if (w.Count == 0) continue;
                if (ud == 9600) wantG++; else wantH++;
                var t = mine.FirstOrDefault(m => m!.UserData == ud);
                bool ok = t != null && t.Events.Count == w.Count && t.Events.Zip(w).All(p => p.First.Value == val
                    && Math.Abs(p.First.Start - p.Second.S / d1) < 0.02 && Math.Abs(p.First.Start + p.First.Duration - Math.Min(1, p.Second.E / d1)) < 0.02);
                if (ok) { if (ud == 9600) haveG++; else haveH++; }
                else if (bad.Count < 6) bad.Add($"node {node.Index} {clip} (DS1 {ds1}) {ud}");
            }
        }
        check(haveG == wantG && haveH == wantH, $"DS1 JT 27 / JT 19 windows as Gravity 9610 / HitControl 9710 tracks on {leaves} leaves with a DS1 clip: gravity {haveG}/{wantG}, map hit {haveH}/{wantH}{(bad.Count > 0 ? "; wrong: " + string.Join(", ", bad) : "")}");
        var used = tracks.Values.Where(t => t.UserData is 9600 or 9700).Select(t => t.TrackId).Distinct().ToList();
        check(used.All(id => trackNames.ContainsValue(id)), $"track-name table names the new tracks' runtime ids ({string.Join(", ", trackNames.Where(kv => used.Contains(kv.Value)).Select(kv => $"{kv.Value} {kv.Key}"))})");
        check(layout == attribs, $"SourceEventTracks attribs in DS2's layout: {layout}/{attribs}");

        // ---- AI ESDs
        var ai = spec.Ai!;
        string lowerPath = Path.Combine(gd, "ezstate", $"ai{ai.Lower}.esd"), upperPath = Path.Combine(gd, "ezstate", $"ai{ai.Upper}.esd");
        check(File.Exists(lowerPath) && File.Exists(upperPath), $"AI files ezstate/ai{ai.Lower}.esd (lower) and ai{ai.Upper}.esd (upper)");
        if (!File.Exists(lowerPath) || !File.Exists(upperPath)) return;
        var lower = ESD.Read(lowerPath); var upper = ESD.Read(upperPath);
        var tLower = ESD.Read(Path.Combine(ExtractDs2.DefaultOut, "ezstate", $"ai{ai.TemplateLower}.esd"));
        foreach (var (name, esd) in new[] { ($"ai{ai.Lower}", lower), ($"ai{ai.Upper}", upper) })
        {
            var probs = AiEsdProblems(esd, generated: name == $"ai{ai.Lower}" ? g => g < 2147483640 : _ => false);
            check(probs.Count == 0, $"{name}.esd: {esd.StateGroups.Count} groups, {esd.StateGroups.Sum(g => g.Value.Count)} states: structure, calls, expressions, slots{(probs.Count > 0 ? ": " + string.Join("; ", probs.Take(6)) : " clean")}");
        }
        // fix pass: DS2's machine sizes and nesting (vanilla: largest group 93 states, deepest call chain 8), the header as the
        // template's (Unk70..7C non-zero in every vanilla ESD), and the generated machines compact (Compact is a fixpoint and
        // proves itself equivalent: the build already proved the compaction of the original machines)
        var tUpperHdr = ESD.Read(Path.Combine(ExtractDs2.DefaultOut, "ezstate", $"ai{ai.TemplateUpper}.esd"));
        foreach (var (name, esd, tmpl) in new[] { ($"ai{ai.Lower}", lower, tLower), ($"ai{ai.Upper}", upper, tUpperHdr) })
        {
            var largest = esd.StateGroups.MaxBy(g => g.Value.Count);
            int depth = Build.Ai.Boss.Ds2AiEsd.CallDepth(esd, out var chain);
            check(largest.Value.Count <= Build.Ai.Boss.Ds2AiEsd.MaxStates && depth <= Build.Ai.Boss.Ds2AiEsd.MaxCallDepth,
                $"{name}.esd in DS2's machine sizes: largest group {largest.Key} {largest.Value.Count} states (limit {Build.Ai.Boss.Ds2AiEsd.MaxStates}, vanilla max 93), deepest call chain {depth} (vanilla max {Build.Ai.Boss.Ds2AiEsd.MaxCallDepth}): {string.Join(" → ", chain)}");
            check(esd.Name == tmpl.Name && esd.Unk70 == tmpl.Unk70 && esd.Unk74 == tmpl.Unk74 && esd.Unk78 == tmpl.Unk78 && esd.Unk7C == tmpl.Unk7C && esd.Unk70 != 0,
                $"{name}.esd header = the template's (unk70..7C {esd.Unk70},{esd.Unk74},{esd.Unk78},{esd.Unk7C}; template {tmpl.Unk70},{tmpl.Unk74},{tmpl.Unk78},{tmpl.Unk7C})");
        }
        var gen2 = lower.StateGroups.Keys.Where(g => g is >= 2_000_000_000 && g <= Build.Ai.Boss.Ds2AiEsd.Think).ToList();
        var notCompact = gen2.Where(g => Build.Ai.Boss.Ds2AiEsd.Compact(lower.StateGroups[g]) is var c && (c.Count != lower.StateGroups[g].Count || !Build.Ai.Boss.Ds2AiEsd.Equivalent(lower.StateGroups[g], c, out _))).ToList();
        check(notCompact.Count == 0, $"generated machines compact ({gen2.Count} machines, {gen2.Sum(g => lower.StateGroups[g].Count)} states; compaction a fixpoint and equivalent){(notCompact.Count > 0 ? ": not: " + string.Join(", ", notCompact) : "")}");
        // the proof is not vacuous: a copy of the think machine with two branch targets swapped (or one evaluator changed) is
        // rejected
        // session 11 (burgchr11): rule AI-SPLIT may leave the think machine with calls only: probe the first generated machine that
        // branches (the think machine when it does)
        static bool Branches(ESD.State s) => s.Conditions.Count(c => c.TargetState != null) >= 2 && s.Conditions.Where(c => c.TargetState != null).Select(c => c.TargetState).Distinct().Count() >= 2;
        long probe = new[] { Build.Ai.Boss.Ds2AiEsd.Think }.Concat(gen2.OrderByDescending(g => g)).First(g => lower.StateGroups[g].Values.Any(Branches) && lower.StateGroups[g].Values.SelectMany(s => s.Conditions).Any(c => c.Evaluator.Length > 2));
        // a swap of two targets that happen to be equivalent proves nothing: the first branching state (over the generated machines)
        // whose swap the proof rejects (session 11, burgchr11: the Armored Tusk's first branch led to two equivalent states)
        bool swapRejected = false;
        foreach (long g in new[] { probe }.Concat(gen2.Where(x => x != probe)))
        {
            var cands = Build.Ai.Boss.Ds2AiEsd.Compact(lower.StateGroups[g]).Where(kv => Branches(kv.Value)).Select(kv => kv.Key).Take(12).ToList();
            foreach (long sid in cands)
            {
                var copy = Build.Ai.Boss.Ds2AiEsd.Compact(lower.StateGroups[g]);
                var pr = copy[sid].Conditions.Where(c => c.TargetState != null).Take(2).ToList();
                (pr[0].TargetState, pr[1].TargetState) = (pr[1].TargetState, pr[0].TargetState);
                if (!Build.Ai.Boss.Ds2AiEsd.Equivalent(lower.StateGroups[g], copy, out _)) { swapRejected = true; break; }
            }
            if (swapRejected) break;
        }
        var thinkCopy2 = Build.Ai.Boss.Ds2AiEsd.Compact(lower.StateGroups[probe]);
        var c2 = thinkCopy2.Values.SelectMany(s => s.Conditions).First(c => c.Evaluator.Length > 2);
        c2.Evaluator = [.. c2.Evaluator[..^1], 0x40, 0x98, 0xA1]; // "&& 0"
        bool evalRejected = !Build.Ai.Boss.Ds2AiEsd.Equivalent(lower.StateGroups[probe], thinkCopy2, out _);
        check(swapRejected && evalRejected, $"equivalence proof rejects a changed think machine (two targets swapped: {(swapRejected ? "rejected" : "ACCEPTED")}, one condition changed: {(evalRejected ? "rejected" : "ACCEPTED")})");
        foreach (long lib in new[] { Build.Ai.Boss.Ds2AiEsd.LibAttack, Build.Ai.Boss.Ds2AiEsd.LibStep, Build.Ai.Boss.Ds2AiEsd.LibSync, Build.Ai.Boss.Ds2AiEsd.LibInit, 10L })
        {
            var a = new ESD(tLower.LongFormat, tLower.DarkSoulsCount) { Name = tLower.Name }; a.StateGroups[lib] = tLower.StateGroups[lib];
            var b = new ESD(tLower.LongFormat, tLower.DarkSoulsCount) { Name = tLower.Name }; b.StateGroups[lib] = lower.StateGroups[lib];
            check(a.Write().AsSpan().SequenceEqual(b.Write()), $"library group {lib} = the template's (ai{ai.TemplateLower})");
        }
        var dispatch = upper.StateGroups.Values.SelectMany(g => g.Values).SelectMany(s => s.EntryCommands.Concat(s.WhileCommands))
            .Where(c => c.CommandID == 5301).Select(c => c.Arguments.Count > 0 ? ConstOf(c.Arguments[^1]) : null).Where(v => v != null).Select(v => (long)v!.Value).ToHashSet();
        var upperModes = upper.StateGroups.Values.SelectMany(g => g.Values).SelectMany(s => s.EntryCommands).Where(c => c.CommandBank == 6 && c.CommandID == 2147483646)
            .Select(c => c.Arguments.Count > 0 ? ConstOf(c.Arguments[0]) : null).Where(v => v != null).Select(v => (long)v!.Value).ToHashSet();
        check(upperModes.All(lower.StateGroups.ContainsKey), $"upper logic dispatches lower groups {string.Join(", ", upperModes.Order())}: all in the lower logic");
        var tUpper = ESD.Read(Path.Combine(ExtractDs2.DefaultOut, "ezstate", $"ai{ai.TemplateUpper}.esd"));
        // the template's wake test (f3() > 2.25) != 0: vanilla bytes (0x92 = > as DS2 runs it, rulebook §7)
        byte[] wake = [0x43, 0x84, 0x80, .. BitConverter.GetBytes(2.25f), 0x92, 0x40, 0x96, 0xA1];
        int tWake = tUpper.StateGroups.Values.SelectMany(g => g.Values).SelectMany(s => s.Conditions).Count(c => c.Evaluator.AsSpan().SequenceEqual(wake));
        int oWake = upper.StateGroups.Values.SelectMany(g => g.Values).SelectMany(s => s.Conditions).Count(c => c.Evaluator.AsSpan().SequenceEqual(wake));
        check(tWake == 1 && oWake == 0 && upper.StateGroups.Keys.Order().SequenceEqual(tUpper.StateGroups.Keys.Order()), $"upper logic = the template's groups with the wake delay set to DS1's (template 2.25 s conditions {tWake}, ours {oWake})");
        // ESD writer on vanilla AI ESDs (SoulsFormats, the writer of our event ESDs that DS2 loads): read → write → read gives
        // the same machines (dump text) on the two templates; the bytes differ only in layout (SoulsFormats packs the
        // tables in its own order), reported as info
        foreach (int id in new[] { ai.TemplateLower, ai.TemplateUpper })
        {
            var p = Path.Combine(ExtractDs2.DefaultOut, "ezstate", $"ai{id}.esd");
            var orig = File.ReadAllBytes(p);
            var back = ESD.Read(orig).Write();
            int diff = Enumerable.Range(0, Math.Min(orig.Length, back.Length)).FirstOrDefault(i => orig[i] != back[i], -1);
            check(DumpEsd(ESD.Read(back)) == DumpEsd(ESD.Read(orig)), $"ESD writer: ai{id}.esd re-written and re-read = the original's machines ({orig.Length} → {back.Length} bytes{(diff >= 0 ? $", first byte difference at 0x{diff:X}" : ", byte-identical")})");
        }

        if (packDir == null) return;
        // ---- pack: ids the AI names, params, generator, boss rows, events
        string pg = Path.Combine(packDir, "gamedata");
        var reg = ParamStore.LoadDs2(Path.Combine(packDir, "title"), ExtractDs2.DefaultOut);
        var defs = Directory.GetFiles(Path.Combine(ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(f => PARAMDEF.XmlDeserialize(f)).ToList();
        const string map = "m18_01_00_00";
        var locs = PARAM.Read(Path.Combine(pg, "param", $"eventlocation_{map}.param")).Rows.Select(r => r.ID).ToHashSet();
        var locRefs = new List<int>();
        foreach (var s in lower.StateGroups.Values.SelectMany(g => g.Values))
        {
            foreach (var c in s.Conditions.SelectMany(Flat)) foreach (int f in new[] { 7070, 7072 }) locRefs.AddRange(CallArgs(c.Evaluator, f));
            foreach (var c in s.WhileCommands.Where(c => c.CommandID == 7005)) if (ConstOf(c.Arguments[0]) is int l) locRefs.Add(l);
        }
        // session 11 (ret11): a boss whose DS1 goal names no region (the Stray Demon) has no EventLocation to check
        bool isAd = spec.Ds1Entity == Build.Chr.Ds1Boss.AsylumDemon.Ds1Entity;
        // session 11 (burgchr11): a boss of another DS1 map (the Burg's, --burg-chrs) is built but not placed by this pack: its EventLocations,
        // generator, BossBattleParam and events are the placement track's (M3), listed instead of checked
        bool placedHere = spec.Ds1Map == map;
        if (placedHere) check((locRefs.Count > 0 || !isAd) && locRefs.All(locs.Contains), $"AI EventLocations exist in {map}: {(locRefs.Count == 0 ? "none named" : string.Join(", ", locRefs.Distinct().Order()))}");
        else check(true, $"{spec.Ds1Map} boss, not placed in this pack (M3): EventLocations its AI names (DS1 region → 3701000 + entity % 1000): {(locRefs.Count == 0 ? "none" : string.Join(", ", locRefs.Distinct().Order()))}");
        var beh = new[] { "EnemyBehaviorParam", "EnemyBehaviorSecondParam", "EnemyBehaviorThirdParam" }.Select(n => reg.Params[n][spec.BehaviorId]).ToList();
        var combo = reg.Params["EnemyComboBehaviorParam"][spec.BehaviorId];
        var actions = lower.StateGroups.Values.SelectMany(g => g.Values).SelectMany(s => s.EntryCommands).Where(c => c.CommandBank == 6 && c.CommandID == (int)(uint)Build.Ai.Boss.Ds2AiEsd.LibAttack)
            .Select(c => ConstOf(c.Arguments[0])).Where(v => v != null).Select(v => v!.Value).Distinct().Order().ToList();
        bool ActionOk(int n) => n switch
        {
            // an action row in use (BladeCtrlType 4 = disabled); DS1's balcony poses 3020-3022 hit nothing (DamageId00 -1)
            >= 0 and < 30 => beh[n / 10] is { } r && Convert.ToInt32(r[$"action{n % 10}BladeCtrlType"].Value) != 4,
            >= 50 and < 57 => combo is { } c && Convert.ToInt32(c[$"combo{n - 50}AttackMotionId"].Value) > 0 && Convert.ToInt32(c[$"combo{n - 50}BladeCtrlType"].Value) != 4,
            _ => false,
        };
        check(actions.All(ActionOk), $"AI actions {string.Join(",", actions)} have behaviour / combo rows (EnemyBehavior*Param / EnemyComboBehaviorParam {spec.BehaviorId}){string.Concat(actions.Where(a => !ActionOk(a)).Select(a => $"; {a} missing"))}");
        var dl = reg.Params["EnemyDefaultLogicParam"][ai.Lower];
        check(dl != null && Convert.ToInt32(dl["upperLogickId"].Value) == ai.Upper && Convert.ToInt32(dl["lowerLogickId"].Value) == ai.Lower, $"EnemyDefaultLogicParam {ai.Lower}: upper {dl?["upperLogickId"].Value}, lower {dl?["lowerLogickId"].Value}, start {dl?["lowerStartStateId"].Value}");
        check(reg.Params["LogicComParam"][ai.Upper] != null, $"LogicComParam {ai.Upper} (perception)");
        if (!placedHere) { check(true, $"{spec.Ds1Map} boss: generator / BossBattleParam / boss events / flag storage not checked (not placed by this pack: M3)"); return; }
        var rp = PARAM.Read(Path.Combine(pg, "param", $"generatorregistparam_{map}.param"));
        rp.ApplyParamdefCarefully(defs);
        var rr = rp.Rows.FirstOrDefault(r => Convert.ToInt32(r["EnemyId"].Value) == spec.EnemyParam);
        check(rr != null && Convert.ToInt32(rr["HighLogicId"].Value) == ai.Upper && Convert.ToInt32(rr["LowLogicId"].Value) == ai.Lower && Convert.ToInt32(rr["startUpStateType"].Value) == 0,
            $"GeneratorRegistParam {rr?.ID}: EnemyParam {spec.EnemyParam}, logic {rr?["HighLogicId"].Value}/{rr?["LowLogicId"].Value}, start-up {rr?["startUpStateType"].Value} (DS1's event sets 2)");
        var gp = PARAM.Read(Path.Combine(pg, "param", $"generatorparam_{map}.param"));
        int gen = Build.Events.EntityRules.Generator(spec.Ds1Entity);
        var gr = gp.Rows.FirstOrDefault(r => r.ID == gen);
        byte[]? gb = gr == null ? null : RawRow(gp, gr);
        // ---- session 11 (track ret11), rule B-k: every 1:1 boss of the map placed in the pack has its own event group 10·(k+1),
        // logic group 150 + 10·k (used by no other generator) and logic flag BossLogicFlag(map, k); k = entity order
        var mapBosses = Build.Chr.Ds1Boss.All.Where(s => s.Ds1Map == map).OrderBy(s => s.Ds1Entity).ToList();
        int k = mapBosses.FindIndex(s => s.Ds1Entity == spec.Ds1Entity);
        var placedBosses = mapBosses.Select(s => (s, Row: gp.Rows.FirstOrDefault(r => r.ID == Build.Events.EntityRules.Generator(s.Ds1Entity)))).Where(t => t.Row != null).Select(t => (t.s, B: RawRow(gp, t.Row!))).ToList();
        check(placedBosses.Select(t => t.B[2]).Distinct().Count() == placedBosses.Count && placedBosses.Select(t => t.B[3]).Distinct().Count() == placedBosses.Count
            && placedBosses.All(t => t.B[2] == Build.Events.EntityRules.BossEventGroupOf(mapBosses.IndexOf(t.s)) && t.B[3] == Build.Events.EntityRules.BossLogicGroupOf(mapBosses.IndexOf(t.s))),
            $"rule B-k: {placedBosses.Count} boss generators of {map}, event / logic groups pairwise distinct: {string.Join("; ", placedBosses.Select(t => $"{t.s.Ds1Chr} gen {Build.Events.EntityRules.Generator(t.s.Ds1Entity)} event {t.B[2]} logic {t.B[3]} flag {BitConverter.ToUInt32(t.B, 0x5C)}"))}");
        if (!isAd)
        {
            // ---- session 11 (ret11): a 1:1 boss other than the Asylum Demon (no intro, no plunge): generator, battle row, events
            int lfk = Build.Events.FlagRules.BossLogicFlag("m18_01", k)!.Value;
            check(gb != null && gb[2] == Build.Events.EntityRules.BossEventGroupOf(k) && gb[0x48] == 0x6C && BitConverter.ToUInt32(gb, 0x5C) == lfk && BitConverter.ToInt32(gb, 8) == spec.EnemyParam * 100,
                $"GeneratorParam {gen} (k {k}): event group {gb?[2]}, flags 0x{gb?[0x48]:X2}, logic flag {(gb != null ? BitConverter.ToUInt32(gb, 0x5C) : 0)} (want {lfk}), regist {(gb != null ? BitConverter.ToInt32(gb, 8) : 0)}");
            var others = gp.Rows.Where(r => r.ID != gen).Select(r => RawRow(gp, r)[3]).ToHashSet();
            check(gb != null && gb[3] == Build.Events.EntityRules.BossLogicGroupOf(k) && !others.Contains(gb[3]), $"GeneratorParam {gen}: logic group {gb?[3]} (rule B-k {Build.Events.EntityRules.BossLogicGroupOf(k)}), used by no other generator");
            var bbk = reg.Params["BossBattleParam"][spec.Ds1Entity];
            var area = Build.Ds1Source.LoadParam(Program.DefaultDsrRoot, "GameAreaParam")?[spec.Ds1Entity];
            int souls = area != null ? Build.Chr.Ds1Calibration.BossSouls(area) : -1;   // bal11 rule BAL2
            string nm = "";
            try { nm = FMG.Read(Path.Combine(pg, "menu", "text", "english", "talk", "charaname.fmg")).Entries.FirstOrDefault(e => e.ID == Convert.ToInt32(bbk?["bossNameTextId1"].Value ?? 0))?.Text ?? ""; } catch { }
            int killedK = bbk != null ? Convert.ToInt32(bbk["killedEventId"].Value) : 0;
            check(bbk != null && Convert.ToInt32(bbk["soulValue"].Value) == souls && killedK > 0 && Convert.ToInt32(bbk["genEventGroupId1"].Value) == Build.Events.EntityRules.BossEventGroupOf(k) && nm.Length > 0,
                $"BossBattleParam {spec.Ds1Entity}: souls {bbk?["soulValue"].Value} (want {souls}), killed flag {killedK}, event group {bbk?["genEventGroupId1"].Value}, name \"{nm}\", item lot {bbk?["itemLotId"].Value}");
            check(Convert.ToInt32(reg.Params["EnemyParam"][spec.EnemyParam]!["dropSoulValue"].Value) == 0, "EnemyParam souls 0 (the BossBattleParam pays them, DS2's boss convention)");
            var evk = ESD.Read(Path.Combine(pg, "ezstate", $"event_{map}.esd"));
            var cmdk = evk.StateGroups.Values.SelectMany(g => g.Values).SelectMany(s => s.EntryCommands).ToList();
            bool HasK(int id, params int[] a) => cmdk.Any(c => c.CommandID == id && a.Select((v, i) => i < c.Arguments.Count && ConstOf(c.Arguments[i]) == v).All(b => b));
            int bbId = Build.Events.EntityRules.BossBattle(spec.Ds1Entity);
            check(HasK(130101, lfk, 0) && HasK(130101, lfk, 1) && HasK(132201, bbId) && HasK(132202, bbId),
                $"boss events in event_{map}.esd: logic flag {lfk} off/on, boss battle 132201/132202({bbId})");
            var flagsK = new List<(string What, long Flag)> { ("killed flag (BossBattleParam)", killedK), ("logic flag (GeneratorParam 0x5C)", lfk) };
            var unstoredK = flagsK.Where(x => Build.Events.Ds2FlagStore.Lookup(x.Flag, [map]) == Build.Events.Ds2FlagStore.Kind.None).ToList();
            check(unstoredK.Count == 0, $"boss flags have DS2 storage in {map}: {string.Join("; ", flagsK.Select(x => $"{x.What} {x.Flag} {Build.Events.Ds2FlagStore.Describe(Build.Events.Ds2FlagStore.Lookup(x.Flag, [map]))}"))}");
            return;
        }
        check(gb != null && gb[2] == Build.Events.EntityRules.BossEventGroup && gb[0x48] == 0x6C && BitConverter.ToUInt32(gb, 0x5C) == Build.Events.FlagRules.BossLogicFlag("m18_01", 0) && BitConverter.ToInt32(gb, 8) == spec.EnemyParam * 100,
            $"GeneratorParam {gen}: event group {gb?[2]}, flags 0x{gb?[0x48]:X2}, logic flag {(gb != null ? BitConverter.ToUInt32(gb, 0x5C) : 0)}, regist {(gb != null ? BitConverter.ToInt32(gb, 8) : 0)}");
        // session 9 (chrint stage 1): the boss's own logic group (EntityRules.BossLogicGroup), shared with no other generator of the map
        var otherGroups = gp.Rows.Where(r => r.ID != gen).Select(r => RawRow(gp, r)[3]).ToHashSet();
        check(gb != null && gb[3] == Build.Events.EntityRules.BossLogicGroup && !otherGroups.Contains(gb[3]),
            $"GeneratorParam {gen}: logic group {gb?[3]} (boss rule {Build.Events.EntityRules.BossLogicGroup}); other generators' groups {string.Join(",", otherGroups.Order())}");
        var bb = reg.Params["BossBattleParam"][spec.Ds1Entity];
        string nameText = "";
        try { nameText = FMG.Read(Path.Combine(pg, "menu", "text", "english", "talk", "charaname.fmg")).Entries.FirstOrDefault(e => e.ID == Convert.ToInt32(bb?["bossNameTextId1"].Value ?? 0))?.Text ?? ""; } catch { }
        check(bb != null && Convert.ToInt32(bb["soulValue"].Value) == 3000 && Convert.ToInt32(bb["killedEventId"].Value) == Build.Events.FlagRules.Map("m18_01", 16) && nameText.Length > 0,
            $"BossBattleParam {spec.Ds1Entity}: souls {bb?["soulValue"].Value}, killed flag {bb?["killedEventId"].Value}, name \"{nameText}\", item lot {bb?["itemLotId"].Value} (rewards: DS1 common event 970)");
        check(Convert.ToInt32(reg.Params["EnemyParam"][spec.EnemyParam]!["dropSoulValue"].Value) == 0, "EnemyParam souls 0 (the BossBattleParam pays them, DS2's boss convention)");
        var ev = ESD.Read(Path.Combine(pg, "ezstate", $"event_{map}.esd"));
        var cmds = ev.StateGroups.Values.SelectMany(g => g.Values).SelectMany(s => s.EntryCommands).ToList();
        bool Has(int id, params int[] a) => cmds.Any(c => c.CommandID == id && a.Select((v, i) => i < c.Arguments.Count && ConstOf(c.Arguments[i]) == v).All(b => b));
        int lf = Build.Events.FlagRules.BossLogicFlag("m18_01", 0)!.Value;
        check(Has(131702, gen, 2) && Has(131704, gen) && Has(130101, lf, 0) && Has(130101, lf, 1) && Has(130308, gen) && Has(131703, gen) && Has(132201) && Has(132202) && Has(131402, 1_000_002_660),
            $"boss events in event_{map}.esd: start-up 131702({gen}, 2), air warp 131704, logic flag {lf} off/on, fall-damage immunity 130308, home 131703, boss battle 132201/132202, reward lot 1000002660");
        // fix pass: the start-up reset after the wake, and the intro's leading tests folded with event 0's load resets
        static bool Contains(byte[] hay, byte[] needle) => Enumerable.Range(0, Math.Max(0, hay.Length - needle.Length + 1)).Any(i => hay.AsSpan(i, needle.Length).SequenceEqual(needle));
        static byte[] PushI(int v) => [0x82, .. BitConverter.GetBytes(v)];
        int fGate = Build.Events.FlagRules.Map("m18_01", 11810312)!.Value, fArmed = Build.Events.FlagRules.Map("m18_01", 11810314)!.Value, fIntro = Build.Events.FlagRules.Map("m18_01", 11810310)!.Value;
        var intro = ev.StateGroups.Values.FirstOrDefault(g => g.Values.Any(s => s.EntryCommands.Any(c => c.CommandID == 131702 && ConstOf(c.Arguments[0]) == gen && ConstOf(c.Arguments[1]) == 2)));
        bool resetAfterWake = intro != null && intro.Values.Any(s => s.EntryCommands.Any(c => c.CommandID == 131702 && ConstOf(c.Arguments[0]) == gen && ConstOf(c.Arguments[1]) == 0)
            && intro.Values.Any(w => w.Conditions.Any(c => c.TargetState is long t && intro.TryGetValue(t, out var ts) && ts == s && WakeWait(c.Evaluator))));
        // the wake clip's wait: (f3() > DS1's 9060 length, 4.667 s)
        static bool WakeWait(byte[] e) => e.Length >= 8 && e[0] == 0x43 && e[1] == 0x84 && e[2] == 0xA6 && e[3] == 0x80 && BitConverter.ToSingle(e, 4) is > 4f and < 5f;
        bool foldArmed = intro != null && intro.Values.Any(s => s.Conditions.Any(c => Contains(c.Evaluator, PushI(fArmed)) && Contains(c.Evaluator, PushI(fGate))));
        bool foldIntro = intro != null && intro.Values.Any(s => s.Conditions.Any(c => Contains(c.Evaluator, PushI(fIntro)) && Contains(c.Evaluator, PushI(fGate))));
        check(resetAfterWake && foldArmed && foldIntro,
            $"intro (DS1 11810310): start-up reset 131702({gen}, 0) right after the 4.667 s wake wait {(resetAfterWake ? "yes" : "NO")}; leading tests read {fArmed} / {fIntro} as DS1's event 0 leaves them (with gate flag {fGate}) {(foldArmed && foldIntro ? "yes" : "NO")}");
        // chrint stage-1 fix pass (review finding 2): every boss flag needs DS2 storage (Ds2FlagStore, V-code npcflags:
        // FUN_140474230 get / FUN_1404750b0 set; a flag without a block reads 0 and drops writes). The checks above compare
        // flag ids with FlagRules only, so they pass even when the ids have no storage (killed flag 109000016 = DS1 16,
        // hall/gate/fog flags 1010003xx with bit >= 200). Vanilla killedEventId values are map block-0 flags (synced online).
        int killed = bb != null ? Convert.ToInt32(bb["killedEventId"].Value) : 0;
        var bossFlags = new List<(string What, long Flag)> { ("killed flag (BossBattleParam)", killed), ("logic flag (GeneratorParam 0x5C)", gb != null ? BitConverter.ToUInt32(gb, 0x5C) : 0) };
        for (int d = 11810310; d <= 11810315; d++) if (Build.Events.FlagRules.Map("m18_01", d) is int g) bossFlags.Add(($"DS1 {d}", g));
        var unstored = bossFlags.Where(x => Build.Events.Ds2FlagStore.Lookup(x.Flag, [map]) == Build.Events.Ds2FlagStore.Kind.None).ToList();
        check(unstored.Count == 0,
            $"boss flags have DS2 storage in {map} (Ds2FlagStore): {string.Join("; ", bossFlags.Select(x => $"{x.What} {x.Flag} {Build.Events.Ds2FlagStore.Describe(Build.Events.Ds2FlagStore.Lookup(x.Flag, [map]))}"))}"
            + (unstored.Count > 0 ? $" — UNSTORED: {string.Join(", ", unstored.Select(x => x.Flag))} (needs npcflags F1-F3)" : ""));
    }

    static IEnumerable<ESD.Condition> Flat(ESD.Condition c) => new[] { c }.Concat(c.Subconditions.SelectMany(Flat));

    static int? ConstOf(byte[] b)
    {
        if (b.Length == 2 && b[0] < 0x80 && b[1] == 0xA1) return b[0] - 0x40;
        if (b.Length == 6 && b[0] == 0x82 && b[5] == 0xA1) return BitConverter.ToInt32(b, 1);
        return null;
    }

    /// <summary>The constant first arguments of every call of function <paramref name="id"/> in an evaluator.</summary>
    static IEnumerable<int> CallArgs(byte[] b, int id)
    {
        for (int i = 0; i + 5 < b.Length; i++)
        {
            if (b[i] != 0x82 || BitConverter.ToInt32(b, i + 1) != id) continue;
            int j = i + 5;
            if (b[j] == 0x82 && j + 4 < b.Length) yield return BitConverter.ToInt32(b, j + 1);
            else if (b[j] < 0x80) yield return b[j] - 0x40;
        }
    }

    static byte[] RawRow(PARAM p, PARAM.Row r)
    {
        var blob = new PARAMDEF { ParamType = p.ParamType, DataVersion = p.ParamdefDataVersion, Unicode = true, FormatVersion = 104, BigEndian = false };
        blob.Fields.Add(new PARAMDEF.Field(blob, PARAMDEF.DefType.dummy8, "data") { ArrayLength = (int)p.DetectedSize });
        p.ApplyParamdef(blob);
        return (byte[])r["data"].Value;
    }

    /// <summary>Structural problems of an AI ESD (see the class summary); <paramref name="generated"/> selects the groups whose
    /// expressions are checked strictly (the vanilla template groups use bytecode forms - A6/B7 short-circuits - we don't
    /// emit).</summary>
    public static List<string> AiEsdProblems(ESD esd, Func<long, bool> generated)
    {
        var p = new List<string>();
        foreach (var (gid, states) in esd.StateGroups)
        {
            if (states.Count < 2) p.Add($"group {gid}: {states.Count} state(s)");
            if (states.TryGetValue(0, out var s0) && s0.EntryCommands.Count > 0) p.Add($"group {gid}: state 0 has entry commands");
            bool machine = gid >= 2_000_000_000;
            foreach (var (sid, st) in states)
            {
                string at = $"group {gid} state {sid}";
                foreach (var c in st.EntryCommands.Concat(st.WhileCommands).Concat(st.ExitCommands))
                {
                    if (c.CommandBank == 6 && !esd.StateGroups.ContainsKey((uint)c.CommandID)) p.Add($"{at}: calls missing group {(uint)c.CommandID}");
                    if (generated(gid)) foreach (var a in c.Arguments) if (Depth(a, machine) is string e) p.Add($"{at}: {c.CommandBank}:{c.CommandID} argument: {e}");
                    if (c.CommandBank == 1 && c.CommandID == 1 && ConstOf(c.Arguments[0]) is int rs && (rs < 0 || rs > 3)) p.Add($"{at}: roll slot {rs} (DS2: 0-3)");
                    if (c.CommandBank == 1 && c.CommandID == 5750 && ConstOf(c.Arguments[0]) is int ns && (ns < 0 || ns > 7)) p.Add($"{at}: counter slot {ns} (DS2: 0-7)");
                }
                foreach (var c in st.Conditions.SelectMany(Flat))
                {
                    if (c.TargetState is long t && !states.ContainsKey(t)) p.Add($"{at}: targets missing state {t}");
                    if (c.TargetState == null && c.PassCommands.Count == 0 && c.Subconditions.Count == 0) p.Add($"{at}: condition without target, commands or subconditions");
                    if (generated(gid) && Depth(c.Evaluator, machine) is string e) p.Add($"{at}: condition: {e}");
                    foreach (var pc in c.PassCommands) if (generated(gid)) foreach (var a in pc.Arguments) if (Depth(a, machine) is string e2) p.Add($"{at}: pass argument: {e2}");
                }
            }
        }
        return p;
    }

    /// <summary>Stack depth of ESD expression bytecode: null when it ends (A1) with exactly one value; parameters only in
    /// machines (and p0-p7).</summary>
    static string? Depth(byte[] b, bool machine)
    {
        int d = 0;
        for (int i = 0; i < b.Length; i++)
        {
            byte op = b[i];
            if (op < 0x80) { d++; continue; }
            switch (op)
            {
                case 0x80: i += 4; d++; break;
                case 0x81: i += 8; d++; break;
                case 0x82: i += 4; d++; break;
                case >= 0x84 and <= 0x8A: d -= op - 0x84; if (d < 1) return "call without its id"; break;
                case 0x8D: if (d < 1) return "negation of nothing"; break;
                case >= 0x8C and <= 0x9A: d--; if (d < 1) return $"operator 0x{op:X2} without two values"; break;
                case 0xA1: return d == 1 && i == b.Length - 1 ? null : $"ends with {d} values";
                case 0xB8:
                    if (!machine) return "parameter outside a machine";
                    if (i > 0 && b[i - 1] < 0x80 && (b[i - 1] - 0x40 < 0 || b[i - 1] - 0x40 > 7)) return "parameter index out of range";
                    break;
                case 0xB9 or 0xBA: d++; break;
                default: return $"opcode 0x{op:X2} not emitted by the translator";
            }
        }
        return "no A1";
    }
}
