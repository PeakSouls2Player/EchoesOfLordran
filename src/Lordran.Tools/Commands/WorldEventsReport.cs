using System.Numerics;
using Lordran.Tools.Build.Events;
using Lordran.Tools.Build.Maps;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 10 (sandbox asy-events): world-events [PACK] — the world-event object rules for the Asylum (m18_01):
///  - report: WE1 parts (kind, instance, WE2/WE3 row fields, WE5), WE4 damaging objects, WE4b hits, C11 per 2005:7
///    target (rest-state request / fallback / baked), WE8 drop heights, the WE5 self-test (o8530_0001 vs asy10
///    bake_rest.py) and the C11 template survey;
///  - with PACK: checks on the built pack: every object instance the event ESD names (130702, 131613, 131652, 131636,
///    140202, 140209) is placed (MSB part with that instance id); 131613/140202 targets are breakable models (GDParam
///    isBreakable 1: otherwise 131613 is a silent no-op, FUN_1404710d0 needs a break component); every 131636 state is
///    one the model's prefab reacts to (pfbbin request transitions, rule C11); every Generic instance row (objCtrlType 0, no item lot: pickups keep TB's row)
///    resetType 0 (WE2); the WE1 parts and the dropped stand-in 37340. Exit code 1 on a FAIL.
/// </summary>
public static class WorldEventsReport
{
    const string Map = "m18_01_00_00";

    public static int Run(string[] args)
    {
        string root = Program.DefaultDsrRoot;
        var msb1 = MSB1.Read(Path.Combine(root, "map", "MapStudio", Map + ".msb"));
        var log = new List<string>();
        var parts = AsylumScript.WorldParts(log);
        Console.WriteLine("== WE1 placement (event-addressed object parts)");
        foreach (var l in log) Console.WriteLine("  " + l);
        foreach (var p in parts)
            Console.WriteLine($"  {p.Name,-12} {p.Kind,-9} {p.Ds2Model} inst {p.Instance} at ({p.Position.X:0.###}, {p.Position.Y:0.###}, {p.Position.Z:0.###}) rot ({p.Rotation.X:0.#}, {p.Rotation.Y:0.#}, {p.Rotation.Z:0.#}) fields {string.Join(" ", p.Fields.Select(f => $"{f.Key}={f.Value}"))}");
        Console.WriteLine($"  ports: {string.Join(", ", AsylumScript.WorldPorts.Select(o => $"{o.Ds1Obj}→{o.Ds2Name} ({o.Kind}, template {o.TemplateObj})"))}");
        Console.WriteLine($"  ordinary DS1 player atkObj (WE3): {WorldEvents.OrdinaryPlayerAtkObj(root)}");

        Console.WriteLine("== WE4 damaging objects (2005:9) / WE4b hits");
        var ds1 = new Build.Ds1Source(root);
        var ctx = new AsylumContext(null, ds1);
        foreach (int e in WorldEvents.Uses(root, Map).Where(u => (u.Bank, u.Id) == (2005, 9)).Select(u => u.Entity).Distinct())
        {
            var d = WorldEvents.Damaging(root, Map, e)!;
            Console.WriteLine($"  {e}: dummy {d.Dummy}, radius {d.Radius}, lifespan {d.Lifespan} s, BehaviorParam {d.Behavior} → AtkParam_Npc {d.AtkId} atkObj {d.AtkObj} atkPhys {d.AtkPhys}; row {AsylumScript.DamageRow(e)?.ToString() ?? "none"}");
            foreach (var anim in WorldEvents.Uses(root, Map).Where(u => (u.Bank, u.Id, u.Entity) == (2003, 18, e)).Select(u => BitConverter.ToInt32(u.Ins.ArgData, 4)).Distinct())
            {
                var hl = new List<string>();
                var hits = WorldEvents.Hits(root, Map, msb1, e, anim, hl);
                foreach (var l in hl) Console.WriteLine("    " + l);
                Console.WriteLine($"    → instances {string.Join(", ", ctx.ObjectHits(e, anim).Select(h => $"{h.Instance} at {h.Seconds:0.00} s"))}");
            }
        }

        Console.WriteLine("== C11 / WE5: 2005:7 Reproduce Object Animation");
        foreach (var u in WorldEvents.Uses(root, Map).Where(u => (u.Bank, u.Id) == (2005, 7)).DistinctBy(u => (u.Entity, BitConverter.ToInt32(u.Ins.ArgData, 4))))
        {
            int e = u.Entity, anim = BitConverter.ToInt32(u.Ins.ArgData, 4);
            string what = ctx.Object(e) is not int o ? "not placed (absent: no-op)"
                : ctx.ObjectPoseBaked(e, anim) ? $"instance {o}: end pose baked (WE5), no-op"
                : ctx.ObjectRestState(e, anim) is int rest ? $"instance {o}: 131636({o}, {rest}) rest state (direct request accepted)"
                : ctx.ObjectAnimState(e, anim) is int play ? $"instance {o}: fallback 131636({o}, {play}) replays (rest {ctx.ObjectAnimEndState(e, anim)} not requestable)"
                : $"instance {o}: no prefab state for anim {anim}";
            Console.WriteLine($"  event {u.Event}: 2005:7({e}, {anim}) — played elsewhere {WorldEvents.Played(root, Map, e)}, ObjAct {WorldEvents.HasObjAct(msb1, e)} → {what}");
        }
        foreach (var t in new[] { "o10_1001", "o10_9240", "o29_1001", "o15_1060", "o17_1060", "o35_0702", "o04_0400", "o10_0476" })
            Console.WriteLine($"  template {t}: request transitions [{string.Join(", ", WorldEvents.PrefabRequests(ExtractDs2.DefaultOut, t).Order())}]");
        // WE5 self-test: the rest copy's baked pose (asy10 bake_rest.py: (-12.363, 201.917, 18.086) / (48.6, 152.4, -111.4))
        var copy = msb1.Parts.Objects.First(p => p.EntityID == 1811216);
        if (WorldEvents.BakedPose(root, copy, 0) is { } bp)
        {
            var exp = new Vector3(-12.363f, 201.917f, 18.086f);
            Console.WriteLine($"  WE5 self-test o8530_0001 a00_0000 end: ({bp.Position.X:0.###}, {bp.Position.Y:0.###}, {bp.Position.Z:0.###}) rot ({bp.Rotation.X:0.#}, {bp.Rotation.Y:0.#}, {bp.Rotation.Z:0.#}); bake_rest.py ({exp.X}, {exp.Y}, {exp.Z}): {(Vector3.Distance(bp.Position, exp) < 0.01f ? "pass" : $"FAIL ({Vector3.Distance(bp.Position, exp):0.###} m)")}");
        }

        // C16 census (fix pass): every DS1 map's reproduce-only parts (2005:7, never 2003:18, no ObjAct) and the WE5 gate
        Console.WriteLine("== C16 census: reproduce-only parts in every DS1 map → WE5 bake allowed only when hidden at load");
        foreach (var path in Directory.GetFiles(Path.Combine(root, "event"), "m*.emevd.dcx").Order())
        {
            string map = Path.GetFileName(path)[..12];
            string msbPath = Path.Combine(root, "map", "MapStudio", map + ".msb");
            if (!File.Exists(msbPath)) continue;
            MSB1 m1;
            try { m1 = MSB1.Read(msbPath); } catch (Exception ex) { Console.WriteLine($"  {map}: MSB not readable ({ex.GetType().Name}), skipped"); continue; }
            var objs = m1.Parts.Objects.Where(p => p.EntityID > 0).Select(p => p.EntityID).ToHashSet();
            foreach (int e in WorldEvents.Uses(root, map).Where(u => (u.Bank, u.Id) == (2005, 7)).Select(u => u.Entity).Distinct().Where(objs.Contains).Order())
            {
                if (WorldEvents.Played(root, map, e) || WorldEvents.HasObjAct(m1, e)) continue;
                var h = WorldEvents.HiddenAtLoad(root, map, e);
                Console.WriteLine($"  {map} {e} ({m1.Parts.Objects.First(p => p.EntityID == e).ModelName}): {(h == true ? "hidden at load → bake allowed" : h == false ? "shown at load → NOT baked (pose copy needed)" : "untouched at load (MSB-visible) → NOT baked (pose copy needed)")}");
            }
        }

        Console.WriteLine("== WE8 scripted drops (2005:1 on floors)");
        float dead = WorldEvents.DeadFallHeight();
        foreach (int e in WorldEvents.Uses(root, Map).Where(u => (u.Bank, u.Id) == (2005, 1)).Select(u => u.Entity).Distinct())
            Console.WriteLine($"  {e}: drop {WorldEvents.Drop(root, Map, msb1, e)?.ToString("0.00") ?? "none"} m vs DS2 lethal {dead} m → {(ctx.BreakNeedsFallGuard(e) ? "130307 armed" : "no guard (DS2 survives as DS1)")}");

        if (args.Length < 2) return 0;
        return CheckPack(args[1]);
    }

    static int CheckPack(string pack)
    {
        Console.WriteLine($"== pack checks: {pack}");
        int fails = 0;
        void Check(bool ok, string what) { if (!ok) fails++; Console.WriteLine($"  {(ok ? "pass" : "FAIL")}: {what}"); }
        string Gd(string p) => Path.Combine(pack, "gamedata", p.Replace('/', Path.DirectorySeparatorChar));
        var esd = ESD.Read(Gd($"ezstate/event_{Map}.esd"));
        var msb = MSB2.Read(Gd($"map/{Map}/{Map}.msb"));
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        var moip = PARAM.Read(Gd($"param/mapobjectinstanceparam_{Map}.param"));
        moip.ApplyParamdefCarefully(defs);
        var reg = BND4.Read(Formats.Ds2Regulation.Decrypt(Path.Combine(pack, "title", "enc_regulation.bnd.dcx")));
        PARAM? gd = null;
        foreach (var f in reg.Files.Where(f => Path.GetFileNameWithoutExtension(f.Name).Equals("MapObjectGDParam", StringComparison.OrdinalIgnoreCase)))
        { gd = PARAM.Read(f.Bytes); if (!gd.ApplyParamdefCarefully(defs)) gd = null; }
        var model = msb.Parts.Objects.Where(o => o.MapObjectInstanceParamID != 0).GroupBy(o => o.MapObjectInstanceParamID).ToDictionary(g => g.Key, g => g.First().ModelName);
        static int Arg(byte[] b) => (int)EventSim.Evaluate(b, (_, _) => 0);
        var named = new List<(long G, long S, int Cmd, int Inst, int Arg2)>();
        foreach (var (gid, states) in esd.StateGroups)
            foreach (var (sid, st) in states)
                foreach (var c in st.EntryCommands.Concat(st.Conditions.SelectMany(k => k.PassCommands)))
                {
                    int io = c.CommandID is 140202 or 140209 ? 1 : 0;
                    if (c.CommandID is 130702 or 131613 or 131652 or 131636 or 140202 or 140209 && c.Arguments.Count > io)
                        named.Add((gid, sid, c.CommandID, Arg(c.Arguments[io]), c.Arguments.Count > io + 1 ? Arg(c.Arguments[io + 1]) : 0));
                }
        foreach (var g in named.GroupBy(n => n.Inst).OrderBy(g => g.Key))
            Check(model.ContainsKey(g.Key), $"instance {g.Key} ({string.Join(", ", g.Select(n => n.Cmd).Distinct())}) is placed{(model.TryGetValue(g.Key, out var mm) ? $" ({mm})" : "")}");
        foreach (var g in named.Where(n => n.Cmd is 131613 or 140202).GroupBy(n => n.Inst))
        {
            bool brk = model.TryGetValue(g.Key, out var m) && gd?[ObjPorter.ModelId(m)] is { } row && Convert.ToInt32(row["isBreakable"].Value) == 1;
            Check(brk, $"instance {g.Key} ({m}) is breakable (GDParam isBreakable 1) for {string.Join("/", g.Select(n => n.Cmd).Distinct())}");
        }
        foreach (var g in named.Where(n => n.Cmd == 131636).GroupBy(n => (n.Inst, n.Arg2)))
        {
            if (!model.TryGetValue(g.Key.Inst, out var m)) continue;
            // session 11 (ret11): rule G4's plain ladder object (objCtrlType 4, own instance row, no prefab state machine) takes
            // 131636(ladder, 30 hide / 10 show) through DS2's ladder component (vanilla m10_17 group 111095, Majula's laddersmith;
            // V-code FUN_1403e6390 → FUN_1401cd1c0), not a prefab request list. It fires now that the Stray Demon's events
            // (11815382 / 11810900) translate.
            if (g.Key.Arg2 is ObjActRules.LadderShown or ObjActRules.LadderHidden && moip[g.Key.Inst] is { } lrow && Convert.ToInt32(lrow["objCtrlType"].Value) == 4)
            {
                Check(true, $"131636({g.Key.Inst}, {g.Key.Arg2}): {m} is a plain ladder (objCtrlType 4): rule G4 {(g.Key.Arg2 == ObjActRules.LadderShown ? "show" : "hide")} (DS2's ladder component)");
                continue;
            }
            var req = WorldEvents.PrefabRequests(Path.Combine(pack, "gamedata"), m);
            Check(req.Contains(g.Key.Arg2), $"131636({g.Key.Inst}, {g.Key.Arg2}): {m}'s state machine reacts to a request of {g.Key.Arg2} (requests [{string.Join(", ", req.Order())}])");
        }
        var generic = moip.Rows.Where(r => Convert.ToInt32(r["objCtrlType"].Value) == 0 && Convert.ToInt64(r["itemLotId"].Value) == 0).ToList(); // item pickups keep TB's row (a picked item must not return)
        var bad = generic.Where(r => Convert.ToInt32(r["resetType"].Value) != 0).Select(r => r.ID).ToList();
        Check(bad.Count == 0, $"WE2: {generic.Count} Generic instance rows resetType 0{(bad.Count > 0 ? $" (not: {string.Join(", ", bad)})" : "")}");
        foreach (var p in AsylumScript.WorldParts())
        {
            var row = moip[p.Instance];
            bool ok = model.TryGetValue(p.Instance, out var m) && m == p.Ds2Model && row != null
                && p.Fields.All(f => Convert.ToInt64(row[f.Key].Value) == Convert.ToInt64(f.Value));
            Check(ok, $"WE1 {p.Name} placed as {p.Ds2Model} instance {p.Instance} with {string.Join(" ", p.Fields.Select(f => $"{f.Key}={f.Value}"))}");
        }
        Check(!esd.StateGroups.ContainsKey(37340), "the hand-written boulder stand-in 37340 is gone (DS1's 11810211 translates)");
        // C16 (fix pass, binding): every WE5-baked part is hidden in the first state of its groups' no-flag path (fresh
        // save): the first 130702 on it hides it, before anything shows it
        bool NamesInst(ESD.State s, int inst) => s.EntryCommands.Concat(s.Conditions.SelectMany(k => k.PassCommands)).Any(c => c.CommandID == 130702 && Arg(c.Arguments[0]) == inst);
        var baked = AsylumScript.WorldParts().Where(p => p.Baked).ToList();
        Check(baked.All(p => p.Kind != WorldEvents.Kind.Prefab || p.Note.Contains("bind pose")), $"C16: baked prefab copies start at the bind pose ({string.Join(", ", baked.Select(p => p.Name))})");
        foreach (var p in baked)
        {
            var gids = esd.StateGroups.Where(kv => kv.Value.Values.Any(s => NamesInst(s, p.Instance))).Select(kv => kv.Key).ToList();
            var sim = new EventSim(pack, Map, gids, new EventSim.Options());
            sim.Run(["sleep 1"]);
            var first = sim.Log.FirstOrDefault(l => l.Contains($"object {p.Instance} "));
            Check(gids.Count > 0 && first != null && first.Contains($"object {p.Instance} hidden") && sim.Objects.TryGetValue(p.Instance, out var ob) && ob.On == 0,
                $"C16: WE5-baked {p.Name} (instance {p.Instance}) is disabled at load on a fresh save before any enable (groups {string.Join(", ", gids)}; first: {first ?? "none"})");
        }
        // C17 (fix pass): the G11 fold guard is the event's own flag; it must stay persistent: never cleared by any group,
        // not in the T1 load reset (temporary list) and in a global block (no rest/world reset)
        int? own = FlagRules.Map("m18_01", 11810350);
        var clears = own is int of ? esd.StateGroups.SelectMany(kv => kv.Value.Select(st => (kv.Key, st.Key, st.Value)))
            .Where(t => t.Value.EntryCommands.Concat(t.Value.Conditions.SelectMany(k => k.PassCommands))
                .Any(c => c.CommandID == 130101 && Arg(c.Arguments[0]) == of && Arg(c.Arguments[1]) == 0))
            .Where(t => t.Item1 != 37397)   // session 11 (ret11): rule XM1-R clears it together with a fallback 11810000 (the departure never happened)
            .Select(t => $"{t.Item1}/{t.Item2}").ToList() : [];
        Check(own is int gf && gf is >= 100000 and < 210000 && !FlagRules.TemporaryIds("m18_01").Contains(gf) && clears.Count == 0,
            $"C17: the G11 fold guard (11810350's own flag {own}) is persistent: global block, not in the T1 reset, cleared by no group{(clears.Count > 0 ? $" (cleared in {string.Join(", ", clears)})" : "")}");
        // session 11 (merge11): the boulder's WE4 defects (HANDOFF s10 boulder check) and the body-binding rule
        PARAM? RegParam(string name)
        {
            var f = reg.Files.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x.Name).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (f == null) return null;
            var p = PARAM.Read(f.Bytes);
            return p.ApplyParamdefCarefully(defs) ? p : null;
        }
        if (WorldEvents.Damaging(Program.DefaultDsrRoot, "m18_01_00_00", 1811215) is { } bd && RegParam("SystemDamageParam")?[AsylumScript.DamageRow(1811215) ?? -1] is { } sdp)   // session 12, rule E-ID2: the row is no longer the instance
        {
            var atk = Build.Ds1Source.LoadParam(Program.DefaultDsrRoot, "AtkParam_Npc")?[bd.AtkId];
            int want = (int)Math.Round(bd.AtkPhys * Build.Chr.Ds1AttackParams.DamageScale);
            double kb = atk != null ? Convert.ToDouble(atk["knockbackDist"].Value) : double.NaN;
            int got = Convert.ToInt32(sdp["damage01"].Value);
            double gotKb = Convert.ToDouble(sdp["knockBackPower"].Value);
            Check(got == want && Math.Abs(gotKb - kb * Build.Chr.Ds1Calibration.KnockbackScale) < 1e-3, $"WE4c boulder SystemDamageParam {AsylumScript.DamageRow(1811215)}: damage01 {got} (DS1 AtkParam_Npc {bd.AtkId} atkPhys {bd.AtkPhys} × {Build.Chr.Ds1AttackParams.DamageScale:0.##} = {want}), knockBackPower {gotKb} (DS1 knockbackDist {kb} × {Build.Chr.Ds1Calibration.KnockbackScale:0.###}, rule E9u)");
            string taeFile = Gd("timeact/obj/o18_8530.tae");
            if (File.Exists(taeFile))
            {
                var tae = new Formats.Ds2Tae(File.ReadAllBytes(taeFile));
                float clip = WorldEvents.ClipSeconds(Program.DefaultDsrRoot, "o8530", 0) ?? float.NaN;
                var hits = tae.Events(1).Where(e => e.Type == 2200).ToList();
                float end = Math.Min(bd.Lifespan, clip);
                Check(hits.Count > 0 && hits.All(h => Math.Abs(h.End - end) < 1e-3) && tae.Length(1) == (int)Math.Round(clip * 30),
                    $"WE4c boulder TAE o18_8530 anim 1: 2200 window(s) {string.Join(", ", hits.Select(h => $"{h.Start:0.##}-{h.End:0.###}"))} end at min(DS1 lifespan {bd.Lifespan}, clip {clip:0.###}) = {end:0.###} s; length {tae.Length(1)} frames (want {Math.Round(clip * 30)})");
            }
            else Check(false, "WE4c boulder TAE timeact/obj/o18_8530.tae in the pack");
        }
        else Check(false, $"WE4c boulder SystemDamageParam {AsylumScript.DamageRow(1811215)} in the regulation");
        // every rigid body of an animated object's collision (a model with a skeleton) is named like a node of its model:
        // DS2 binds a keyframed body to the node of the same name (FUN_140bcc840); one that matches no node never moves
        // with the object (the boulder's template body "model": no collision while it rolls)
        static IEnumerable<string> BodyNames(object? v) => v switch
        {
            Formats.Havok.HkPObj o => (o.Class == "hkpRigidBody" && o["name"] is string n ? [n] : Array.Empty<string>()).Concat(o.Fields.Values.SelectMany(BodyNames)),
            List<object?> l => l.SelectMany(BodyNames),
            _ => [],
        };
        var unbound = new List<string>();
        int animated = 0;
        foreach (var bndPath in Directory.GetFiles(Gd("model/obj"), "*.bnd"))
        {
            var bnd = BND4.Read(bndPath);
            if (!bnd.Files.Any(f => Path.GetFileName(f.Name).Equals("skeleton.hkx", StringComparison.OrdinalIgnoreCase))) continue;
            foreach (var h in bnd.Files.Where(f => f.Name.EndsWith("_h.hkx", StringComparison.OrdinalIgnoreCase)))
            {
                string stem = Path.GetFileName(h.Name)[..^"_h.hkx".Length];
                var flv = bnd.Files.FirstOrDefault(f => Path.GetFileName(f.Name).Equals(stem + ".flv", StringComparison.OrdinalIgnoreCase));
                if (flv == null) continue;
                var nodes = FLVER2.Read(flv.Bytes).Nodes.Select(n => n.Name).ToHashSet(StringComparer.Ordinal);
                List<string> bodies;
                try { bodies = BodyNames(new Formats.Havok.HkPackfile(h.Bytes).ReadRoot()).ToList(); }
                catch (Exception e) { unbound.Add($"{Path.GetFileName(h.Name)}: unreadable ({e.Message})"); continue; }
                animated++;
                foreach (var b in bodies.Where(b => !nodes.Contains(b))) unbound.Add($"{Path.GetFileName(h.Name)}: body \"{b}\" (nodes {string.Join(", ", nodes.Take(6))})");
            }
        }
        Check(unbound.Count == 0, $"body binding: every rigid body of the {animated} animated object collision(s) is named like a node of its model{(unbound.Count > 0 ? ": " + string.Join("; ", unbound) : "")}");
        Console.WriteLine($"world-events pack checks: {fails} FAIL");
        return fails == 0 ? 0 : 1;
    }
}
