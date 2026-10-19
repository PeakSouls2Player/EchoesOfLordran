using System.Globalization;
using Lordran.Tools.Build;
using Lordran.Tools.Build.Ai;
using Lordran.Tools.Build.Chr;
using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (fx11, user: "make sure enemies from ds1 who use certain attacks, throw firebombs, use a certain spell etc are
/// doing that"): <c>special-check PACK</c> — every DS1 behaviour of a ported character that is not plain melee is in the pack and
/// selected as DS1 selects it. A "special" is a DS1 attack clip the character's DS1 battle goal requests whose DS1 TAE fires a
/// bullet (type 2 InvokeBulletBehavior → BehaviorParam refType 1: arrows, bolts, firebombs, sorceries, breath) or applies a
/// SpEffect behaviour (refType 2: buffs), plus DS1 goals that are no attack (item use, parries) and DS1 event AI requests.
/// <list type="bullet">
/// <item><b>regulars</b> (every translated think of <c>lordran-ai\manifest.txt</c>): the pack's ai{low}.esd = the translation of
/// DS1's think; every DS1 Activate path that queues the special reaches DS1's queue in the written ESD (ai-verify's interpreter,
/// incl. rule E-REQ channels); the plan maps it to a DS2 action whose behaviour / combo row of every variant fires the bullet;
/// the anim set's _pl TAE has DS1's bullet events (2300 at DS1's times); the EnemyBulletParam row carries DS1's numbers (rule
/// E10b: speed, max speed, life, acceleration, homing, launch elevation, aim limit; a thrown bomb's explosion child with DS1's
/// radius growth; the spawn dummy = DS1's); its visual exists (DS2 effect bank); the clip's DS1 sounds are on the _snd TAE.</item>
/// <item><b>bosses / large regulars</b> (<see cref="Ds1Boss.All"/> built in the pack): every AI-used DS1 clip with a bullet fires
/// it (rule B-BUL: behaviour BulletId, 2300 on the attack TAE, bullet row as above), its action is requested by the boss AI.</item>
/// <item><b>DS1 event AI requests</b> (rule E-REQ-P): every placed regular DS1's events command has its channel flags on its
/// generator, and the map's event ESD sets them.</item>
/// </list>
/// FAIL = a gap; DEV = a known, logged deviation (reason given). Exit 1 on any FAIL.
/// </summary>
public static class SpecialCheck
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static double D(PARAM.Row r, string f) => Convert.ToDouble(r[f]?.Value ?? 0, Inv);
    static int I(PARAM.Row r, string f) => Convert.ToInt32(r[f]?.Value ?? 0, Inv);

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: special-check PACK [--think N]"); return 1; }
        string pack = args[1];
        int? onlyThink = args.Length > 3 && args[2] == "--think" ? int.Parse(args[3], Inv) : null;
        ChrPorter.Burg = File.Exists(Path.Combine(pack, "lordran-chr", "burg-placements.txt"));
        ChrPorter.Depths = ChrPorter.Burg && Directory.Exists(Path.Combine(pack, "gamedata", "map", "m19_40_00_00"));   // session 11 (dchr11): the Depths' characters (built with --map depths)
        string dsr = Program.DefaultDsrRoot, gd = Path.Combine(pack, "gamedata");
        var ps = ParamStore.LoadDs2(Path.Combine(pack, "title"), ExtractDs2.DefaultOut);
        var npcP = Ds1Source.LoadParam(dsr, "NpcParam")!; var behP = Ds1Source.LoadParam(dsr, "BehaviorParam")!;
        var bulP = Ds1Source.LoadParam(dsr, "Bullet")!; var thinkP = Ds1Source.LoadParam(dsr, "NpcThinkParam")!;
        int fails = 0, devs = 0, oks = 0;
        void Fail(string m) { fails++; Console.WriteLine("  FAIL " + m); }
        void Ok(string m) { oks++; Console.WriteLine("  ok   " + m); }
        void Dev(string m) { devs++; Console.WriteLine("  DEV  " + m); }
        var ds2Fx = Build.Maps.FfxPorter.Ds2UsedIds();
        var ourFx = Build.Maps.FfxPorter.All.Select(c => c.Id).ToHashSet();
        bool FxExists(int id) => id <= 0 || ds2Fx.Contains(id) || ourFx.Contains(id);
        var maps = new[] { "m18_01_00_00", "m10_02_00_00", "m10_01_00_00" }.ToDictionary(m => m, m => MSB1.Read(Path.Combine(dsr, "map", "MapStudio", m + ".msb")));
        var srcCache = new Dictionary<string, Ds1ChrSource>();
        Ds1ChrSource Src(string chr) => srcCache.TryGetValue(chr, out var s) ? s : srcCache[chr] = Ds1ChrSource.Load(dsr, chr);
        var taeCache = new Dictionary<string, Ds2Tae?>();
        Ds2Tae? Tae(string rel) => taeCache.TryGetValue(rel, out var t) ? t : taeCache[rel] = File.Exists(Path.Combine(gd, rel)) ? new Ds2Tae(File.ReadAllBytes(Path.Combine(gd, rel))) : null;

        // ---- DS1 specials of a clip: bullets (refType 1) and SpEffect behaviours (refType 2)
        List<(string Kind, double S, int Dummy, int Judge, PARAM.Row? Bullet, int SpEffect)> Specials(Ds1ChrSource src, int clip, int variation)
        {
            var o = new List<(string, double, int, int, PARAM.Row?, int)>();
            var a = src.TaeAnim(clip);
            if (a == null) return o;
            foreach (var e in a.Events.Where(e => e.Type is 1 or 2))
            {
                var p = e.GetParameterBytes(false);
                if (p.Length < 12) continue;
                int judge = BitConverter.ToInt32(p, 8), dummy = BitConverter.ToInt32(p, 0);
                var be = behP[200000000 + variation * 1000 + judge];
                if (be == null) continue;
                int refType = I(be, "refType"), refId = I(be, "refId");
                if (refType == 1) o.Add(("bullet", e.StartTime, dummy, judge, bulP[refId], 0));
                else if (refType == 2) o.Add(("speffect", e.StartTime, dummy, judge, null, refId));
            }
            return o;
        }

        // ---- bullet row against DS1 (rule E10b)
        void BulletRow(string who, int id, PARAM.Row b, int dummy)
        {
            var r = ps.Params["EnemyBulletParam"][id];
            if (r == null) { Fail($"{who}: EnemyBulletParam {id} missing (DS1 Bullet {b.ID})"); return; }
            var bad = new List<string>();
            void Near(string f, double want) { if (Math.Abs(D(r, f) - want) > 1e-3) bad.Add($"{f} {D(r, f):0.###} ≠ {want:0.###}"); }
            Near("initialVelocity", D(b, "initVellocity"));
            if (D(b, "maxVellocity") > 0) Near("maxSpeed", D(b, "maxVellocity"));
            if (D(b, "life") > 0) Near("bulletLife", D(b, "life"));
            Near("acceleration", D(b, "accelInRange"));
            Near("homingAngle", D(b, "homingAngle") * Math.PI / 180);
            Near("shootAngle", -D(b, "shootAngleXZ") * Math.PI / 180);
            Near("rightleftCorrectAngle", D(b, "lockShootLimitAng") * Math.PI / 180);
            var own = ps.Params["EnemyDamageParam"][id];
            if (own == null) bad.Add("own damage row missing");
            else if (I(own, "hitDummyPolyId") != dummy) bad.Add($"spawn dummy {I(own, "hitDummyPolyId")} ≠ DS1 {dummy}");
            int child = I(b, "HitBulletID");
            string childNote = "";
            if (child > 0 && bulP[child] is { } hb)   // rules E10b / E10e: every DS1 hit bullet is an explosion child
            {
                int cid = I(r, "childeBulletId");
                var cd = ps.Params["EnemyDamageParam"][I(r, "childeDamageId")];
                if (ps.Params["EnemyBulletParam"][cid] == null || cd == null) bad.Add($"explosion child {cid} missing");
                else
                {
                    double rad = D(hb, "hitRadius") > 0 ? D(hb, "hitRadius") : D(hb, "hitRadiusMax");
                    if (Math.Abs(D(cd, "radius") - rad) > 1e-3) bad.Add($"explosion radius {D(cd, "radius")} ≠ DS1 {rad}");
                    if (D(hb, "hitRadiusMax") > rad && Math.Abs(D(cd, "expandSize") - D(hb, "hitRadiusMax")) > 1e-3) bad.Add($"explosion growth {D(cd, "expandSize")} ≠ DS1 {D(hb, "hitRadiusMax")}");
                    childNote = $"; explosion {cid}: radius {D(cd, "radius"):0.##}→{D(cd, "expandSize"):0.##} m in {D(cd, "expandTime"):0.##} s, damage {I(cd, "damage01")}";
                    var cr = ps.Params["EnemyBulletParam"][cid]!;
                    int cfx = I(cr, "bulletSfxId") > 0 ? I(cr, "bulletSfxId") : I(cr, "explosionSfxId");
                    if (!FxExists(cfx)) bad.Add($"explosion visual {cfx} not in any effect bank");
                }
            }
            int fx = I(r, "bulletSfxId");
            if (!FxExists(fx)) bad.Add($"visual {fx} not in any effect bank");
            if (bad.Count > 0) Fail($"{who}: EnemyBulletParam {id} vs DS1 Bullet {b.ID}: {string.Join("; ", bad)}");
            else Ok($"{who}: EnemyBulletParam {id} = DS1 Bullet {b.ID} (speed {D(r, "initialVelocity")}→{D(r, "maxSpeed")} m/s, life {D(r, "bulletLife")} s, elevation {-D(r, "shootAngle") * 180 / Math.PI:0.#}°, aim ±{D(r, "rightleftCorrectAngle") * 180 / Math.PI:0.#}°, homing {D(r, "homingAngle") * 180 / Math.PI:0.#}°, spawn dummy {dummy}{childNote})");
            Dev($"{who}: visual = DS2 effect {fx} (rule E10b: DS1 FFX {I(b, "sfxId_Bullet")} keeps the DS2 bullet's look; DS1's in-flight / impact sound comes with that DS2 effect)");
        }

        // ================================================================ regulars
        var manifestPath = Path.Combine(pack, "lordran-ai", "manifest.txt");
        var rows = File.Exists(manifestPath) ? File.ReadLines(manifestPath).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split('\t')).ToList() : [];
        var rigs = ChrPorter.All.SelectMany(o => o.Rigs.Select(r => (O: o, Rig: r))).ToList();
        Console.WriteLine($"special-check {pack}: {rows.Count} translated DS1 thinks, {Ds1Boss.All.Count(b => ps.Params["EnemyParam"][b.EnemyParam] != null)} 1:1 bosses / large regulars in the pack");
        foreach (var row in rows)
        {
            int low = int.Parse(row[0], Inv), think = int.Parse(row[5], Inv), goal = int.Parse(row[6], Inv);
            if (onlyThink is int ot && ot != think) continue;
            var hit = rigs.FirstOrDefault(x => $"c{x.Rig.AnimId / 100:D4}" == row[4]);
            if (hit.Rig?.Attacks is not { } plan) { Fail($"think {think}: anim set {row[4]} has no 1:1 attack plan"); continue; }
            var src = Src(hit.O.Ds1Chr);
            string map = maps.FirstOrDefault(m => m.Value.Parts.Enemies.Any(e => e.ThinkParamID == think)).Key ?? "m18_01_00_00";
            bool placedHere = map != "m10_01_00_00";   // the Burg's characters are built, not placed (M3)
            Console.WriteLine($"- think {think} (goal {goal}, {hit.O.Ds1Chr} anim set {row[4]}, DS1 {map[..6]}): ai{low:D6}");
            // the AI as the build translated it (rule E-REQ registered for the map first, as AsylumEnemies / BurgChrs do)
            if (map != "m18_01_00_00") EventRequests.Register(dsr, map);
            var o = AiTranslator.Translate(thinkP[think]!, AiTranslator.MapOf(plan), Ds1Ai.TemplateHigh, Ds1Ai.TemplateLow, Path.Combine(ExtractDs2.DefaultOut, "ezstate"), null, map[..6]);
            string esdPath = Path.Combine(gd, "ezstate", $"ai{low:D6}.esd");
            bool same = File.Exists(esdPath) && File.ReadAllBytes(esdPath).AsSpan().SequenceEqual(o.LowerEsd);
            if (!same) Fail($"think {think}: the pack's ai{low:D6}.esd is not this translation of DS1's think (other build options or registration order)");
            var esd = ESD.Read(o.LowerEsd);
            var machines = o.Machines.Values.ToHashSet();
            // session 11 (dchr11): DS1 queries translated by an approximation (rules AI-NF / AI-NM) are logged deviations
            foreach (var k in o.Coverage.Translated.Keys.Where(k => k.Contains("(deviation)"))) Dev($"think {think}: {k}");
            // DS1 attacks the goal requests (Activate, Interupt, OnIf)
            var requested = new Dictionary<int, List<Ds1Goal.PathResult>>();
            void Collect(IEnumerable<Ds1Goal.PathResult> paths, bool simulate)
            {
                foreach (var p in paths)
                    foreach (var s in p.Effects.OfType<Ds1Goal.SubGoal>().Where(s => s.Name == "CommonAttack" && s.Args.Count > 0 && s.Args[0] is double))
                    {
                        int a = (int)(double)s.Args[0]!;
                        if (!requested.TryGetValue(a, out var l)) requested[a] = l = [];
                        if (simulate && !l.Contains(p)) l.Add(p);
                    }
            }
            Collect(o.Activate.Paths, true);
            if (o.Interupt != null) Collect(o.Interupt.Paths, false);
            var onIf = o.Goal.HasFunction(o.Goal.OnIfName) ? o.Goal.Evaluate(o.Goal.OnIfName, 0.0).Paths : [];
            Collect(onIf, false);
            var amap = AiTranslator.MapOf(plan);
            // event requests the goal reads (rule E-REQ)
            var channels = EventRequests.ForThink(think);
            bool readsRequests = o.Coverage.Translated.Keys.Any(k => k.Contains("EventRequest")) || o.Activate.Paths.Any(p => p.Literals.Any(l => l.ToString().Contains("EventRequest")));
            if (o.Coverage.Skipped.Keys.Any(k => k.Contains("GetEventRequest")) || o.Goal.Assumptions.Keys.Any(k => k.Contains("GetEventRequest")))
            {
                if (maps[map].Parts.Enemies.Where(e => e.ThinkParamID == think).Any(e => EventRequests.ByEntity(dsr, map).ContainsKey(e.EntityID)))
                    Fail($"think {think}: DS1's events send its characters AI requests, but the translation assumes GetEventRequest = 0");
                else Ok($"think {think}: GetEventRequest = 0 is exact (no DS1 event of {map[..6]} sends this think a request)");
            }
            foreach (var (a, paths) in requested.OrderBy(k => k.Key))
            {
                int clip = plan.Clip(a, src);
                var sp = Specials(src, clip, plan.BehaviorVariation);
                if (sp.Count == 0) continue;
                string what = string.Join(", ", sp.Select(x => x.Kind == "bullet" ? $"bullet {x.Bullet?.ID} at {x.S:0.###} s (dummy {x.Dummy})" : $"SpEffect {x.SpEffect} at {x.S:0.###} s"));
                string who = $"think {think} DS1 {a} (a{clip / 10000:D2}_{clip % 10000:D4})";
                if (!amap.Actions.TryGetValue(a, out int act) && !plan.Chains.Any(c => c.Ids.Contains(a))) { Fail($"{who}: {what}: DS1 requests it, the plan has no DS2 action for it"); continue; }
                // AI: the DS1 decision paths that queue it reach DS1's queue in the written ESD
                int pass = 0, failP = 0, unb = 0, drop = 0; string? example = null, failNote = null;
                foreach (var p in paths)
                {
                    string leaf = GoalCompiler.KeyOf(p);
                    var sit = AiVerify.Situation(p, 0.5, o.Compiler.TimerTimes, o.Compiler.TimerSlots);
                    if (sit == null) { unb++; continue; }
                    if (!o.Compiler.LeafEntries.TryGetValue(leaf, out var entries)) { drop++; continue; }
                    var reached = AiVerify.Simulate(esd, o, o.Compiler.RegionVarSlots, o.Compiler.RegionVarsOnPath(p), sit, machines, out string trace, out _);
                    if (reached is { } rr && o.Split.OrigOf.TryGetValue(rr, out long orig) && entries.Contains(orig)) { pass++; example ??= Conditions(p); }
                    else { failP++; failNote ??= $"[{Conditions(p)}]: {trace}"; }
                }
                int onIfN = onIf.Count(p => p.Effects.OfType<Ds1Goal.SubGoal>().Any(s => s.Name == "CommonAttack" && s.Args.Count > 0 && s.Args[0] is double d && (int)d == a));
                string ai = $"{paths.Count} DS1 think paths ({pass} reach DS1's queue in ai{low:D6}, {unb} not checkable, {drop} without a DS2 form){(onIfN > 0 ? $", {onIfN} OnIf paths (compiled inline)" : "")}; e.g. [{example ?? "-"}]";
                if (failP > 0) Fail($"{who}: AI: {failP} DS1 paths do not reach the throw in the ESD: {failNote}");
                else if (pass == 0 && onIfN == 0) { if (placedHere) Fail($"{who}: AI: no DS1 path that requests it is reproduced ({ai})"); else Dev($"{who}: AI: no DS1 path that requests it is checkable ({ai}; {string.Join("; ", o.Coverage.Notes.Where(n => n.Contains("no DS2 form")).Take(2))}) — Burg character, not placed yet (M3)"); }
                else Ok($"{who}: {what}; AI selects it as DS1: {ai}");
                // params: every variant of this anim set fires it
                foreach (var v in hit.Rig.Armed ?? [])
                {
                    var ep = ps.Params["EnemyParam"][v.EnemyParamId];
                    if (ep == null) continue;
                    int slot = I(ep, "defaultEquipWeapon");
                    var w = ps.Params["EnemyEquipWeaponParam"][I(ep, $"equipWeaponAId0{slot}")];
                    int beh = w != null ? I(w, "behaviorId") : -1;
                    foreach (var x in sp.Where(x => x.Kind == "bullet" && x.Bullet != null))
                    {
                        int bid = Ds1AttackParams.EnemyRow(behP, plan.BehaviorVariation, x.Judge, 0) /* session 12, rule E-ID1 */;
                        var cells = new List<string>();
                        if (amap.Actions.TryGetValue(a, out int n) && ps.Params[new[] { "EnemyBehaviorParam", "EnemyBehaviorSecondParam", "EnemyBehaviorThirdParam" }[Math.Min(2, n / 10)]][beh] is { } br)
                            for (int s2 = 0; s2 < 3; s2++) if (I(br, $"action{n % 10}BulletId0{s2}") == bid) cells.Add($"action {n} BulletId0{s2}");
                        if (ps.Params["EnemyComboBehaviorParam"][beh] is { } cb)
                            for (int k = 0; k < 7; k++) for (int s2 = 1; s2 <= 4; s2++) if (I(cb, $"combo{k}BulletId0{s2}") == bid) cells.Add($"combo {50 + k} BulletId0{s2}");
                        if (cells.Count == 0) Fail($"{who}: EnemyParam {v.EnemyParamId} (behaviour {beh}) fires no EnemyBulletParam {bid}");
                        else Ok($"{who}: EnemyParam {v.EnemyParamId} behaviour {beh}: {string.Join(", ", cells)} = {bid}");
                    }
                }
                foreach (var x in sp.Where(x => x.Kind == "bullet" && x.Bullet != null).DistinctBy(x => x.Judge))
                    BulletRow(who, Ds1AttackParams.EnemyRow(behP, plan.BehaviorVariation, x.Judge, 0) /* session 12, rule E-ID1 */, x.Bullet!, x.Dummy);
                foreach (var x in sp.Where(x => x.Kind == "speffect"))
                    Fail($"{who}: SpEffect behaviour {x.SpEffect} (judge {x.Judge}) at {x.S:0.###} s: no DS2 rule (the attack layer ports hits and bullets only)");
                // TAE: the bullet events and the clip's sounds
                var pl = Tae($"timeact/chr/c{hit.Rig.AnimId:D6}_pl.tae");
                var bullets = sp.Where(x => x.Kind == "bullet").Select(x => x.S).ToList();
                long? anim = pl?.Records.Keys.Cast<long?>().FirstOrDefault(an => pl.EventsFull(an!.Value, 16).Where(e => e.Type == 2300).Select(e => (double)e.Start).OrderBy(t => t).ToList() is { Count: > 0 } ts
                    && ts.Count == bullets.Count && ts.Zip(bullets.OrderBy(t => t)).All(z => Math.Abs(z.First - z.Second) < 0.02));
                if (bullets.Count > 0 && anim == null) Fail($"{who}: no animation of c{hit.Rig.AnimId:D6}_pl.tae carries DS1's bullet events (2300 at {string.Join(", ", bullets.Select(t => t.ToString("0.###", Inv)))} s)");
                else if (anim is long aid)
                {
                    var sndTae = Tae($"timeact/chr/c{hit.Rig.AnimId:D6}_snd.tae");
                    int ds1Snd = src.TaeAnim(clip)?.Events.Count(e => e.Type is 128 or 129) ?? 0;
                    int ds2Snd = sndTae != null && sndTae.Records.ContainsKey(aid) ? sndTae.EventsFull(aid, 16).Count(e => e.Type is 201 or 202) : 0;
                    if (ds1Snd > 0 && ds2Snd == 0) Fail($"{who}: TAE {aid}: DS1's {ds1Snd} clip sounds are not on c{hit.Rig.AnimId:D6}_snd.tae");
                    else Ok($"{who}: TAE {aid}: 2300 at DS1's bullet times; sounds DS1 {ds1Snd} → DS2 {ds2Snd} (chrsound S-rules)");
                }
            }
            // specials DS1's goal script names that no feasible path of the translation requests: a DS1 query without a DS2 form
            // (ai:ChkNearLowHpFriend, IsInsideTargetRegion(TARGET_SELF, …)) closes their branch
            foreach (int a in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(o.Goal.ScriptPath), @"(?<![\w.])(3[0-2]\d\d)(?![\w.])").Select(m => int.Parse(m.Value, Inv)).Distinct().Order())
            {
                if (requested.ContainsKey(a) || !src.Has(plan.Clip(a, src))) continue;
                var spd = Specials(src, plan.Clip(a, src), plan.BehaviorVariation);
                if (spd.Count == 0) continue;
                string why = string.Join("; ", o.Coverage.Notes.Where(n => n.Contains("unsupported") || n.Contains("no DS2 form")).Select(n => n.Trim()).Distinct().Take(4));
                string what0 = string.Join(", ", spd.Select(x => x.Kind == "bullet" ? $"bullet {x.Bullet?.ID}" : $"SpEffect {x.SpEffect}"));
                // no DS1 query was replaced: the branch is dead in DS1's own evaluation too (e.g. the sword Warrior's act05 3008, weight 0)
                if (why.Length == 0) { Ok($"think {think}: DS1 {a} ({what0}) is named in the goal but no DS1 path selects it (dead in DS1 too: weight 0 / unreachable)"); continue; }
                string msg = $"think {think}: DS1 {a} ({what0}) is in DS1's goal but only on branches the translation cannot take ({why})";
                if (placedHere) Fail(msg); else Dev(msg + " — Burg character, not placed yet (M3)");
            }
            // DS1 goals that are no attack and have no DS2 form (items, parries …) — reported by the translator's coverage
            foreach (var (k, n) in o.Coverage.Skipped.Where(kv => kv.Key.Contains("UseItem") || kv.Key.Contains("Parry") || kv.Key.Contains("GuardBreak")))
                Fail($"think {think}: DS1 goal '{k}' ×{n} has no DS2 form");
            foreach (var (k, n) in o.Coverage.Skipped.Where(kv => kv.Key.StartsWith("attack ") && kv.Key.Contains("no DS2 action")))
            {
                int a = int.Parse(k.Split(' ')[1], Inv);
                if (Specials(src, plan.Clip(a, src), plan.BehaviorVariation).Count > 0) Fail($"think {think}: DS1 special attack {a} is requested but not in the plan ('{k}')");
            }
            if (channels.Count > 0 && readsRequests) Ok($"think {think}: reads DS1 event AI requests on channels {string.Join(", ", channels.Select(c => $"slot {c.Slot} = {c.V} → f5903({c.K})"))} (rule E-REQ)");
        }

        // ================================================================ bosses / large regulars (rule B-BUL)
        foreach (var b in Ds1Boss.All)
        {
            var ep = ps.Params["EnemyParam"][b.EnemyParam];
            if (ep == null) continue;
            var src = Src(b.Ds1Chr);
            int variation = I(npcP[b.Ds1Npc]!, "behaviorVariationId");
            var used = b.Anim.Singles.Concat(b.Anim.Chains.SelectMany(c => c)).Distinct().Where(src.Has).Order().ToList();
            var lowEsd = File.Exists(Path.Combine(gd, "ezstate", $"ai{b.LowLogic:D6}.esd")) ? ESD.Read(Path.Combine(gd, "ezstate", $"ai{b.LowLogic:D6}.esd")) : null;
            var requestedActs = lowEsd == null ? [] : lowEsd.StateGroups.Values.SelectMany(g => g.Values).SelectMany(s => s.EntryCommands.Concat(s.Conditions.SelectMany(c => c.PassCommands)))
                .SelectMany(c => c.Arguments.Select(Esd.Expr)).ToHashSet();
            Console.WriteLine($"- boss {b.Ds1Chr} (EnemyParam {b.EnemyParam}, c{b.AnimId:D6}, AI ai{b.LowLogic:D6}): {used.Count} AI-used DS1 attack clips");
            foreach (int a in used)
            {
                var sp = Specials(src, a, variation);
                if (sp.Count == 0) continue;
                string who = $"boss {b.Ds1Chr} DS1 {a}";
                string what = string.Join(", ", sp.Select(x => x.Kind == "bullet" ? $"bullet {x.Bullet?.ID} at {x.S:0.###} s (dummy {x.Dummy})" : $"SpEffect {x.SpEffect}"));
                int n = a - 3000;
                bool asSingle = b.Anim.Singles.Contains(a) && n is >= 0 and < 30;
                string req = asSingle ? (requestedActs.Contains(n.ToString(Inv)) ? $"action {n} named in ai{b.LowLogic:D6}" : $"action {n} not found in ai{b.LowLogic:D6}") : "in a chain";
                var beh = asSingle ? ps.Params[new[] { "EnemyBehaviorParam", "EnemyBehaviorSecondParam", "EnemyBehaviorThirdParam" }[n / 10]][b.BehaviorId] : null;
                var ids = new List<int>();
                if (beh != null) for (int s2 = 0; s2 < 3; s2++) if (I(beh, $"action{n % 10}BulletId0{s2}") is int bi && bi > 0) ids.Add(bi);
                if (ps.Params["EnemyComboBehaviorParam"][b.BehaviorId] is { } cb && !asSingle)
                    for (int k = 0; k < 7; k++) for (int s2 = 1; s2 <= 4; s2++) if (I(cb, $"combo{k}BulletId0{s2}") is int bi && bi > 0) ids.Add(bi);
                foreach (var x in sp.Where(x => x.Kind == "bullet" && x.Bullet == null))
                    Dev($"{who}: bullet judge {x.Judge} at {x.S:0.###} s: DS1's BehaviorParam {200000000 + variation * 1000 + x.Judge} names a Bullet row DSR does not have: DS1 fires nothing (none ported)");
                if (sp.All(x => x.Kind == "bullet" && x.Bullet == null)) continue;
                if (sp.Any(x => x.Kind == "bullet" && x.Bullet != null) && ids.Count == 0) { Fail($"{who}: {what}: the boss pipeline fires no bullet for it (behaviour {b.BehaviorId}; {req})"); continue; }
                Ok($"{who}: {what}: fires {string.Join(", ", ids.Distinct())} ({req})");
                var bt = sp.Where(x => x.Kind == "bullet" && x.Bullet != null).Select(x => x.S).OrderBy(t => t).ToList();
                var plB = Tae($"timeact/chr/c{b.AnimId:D6}_pl.tae");
                long? tid = plB?.Records.Keys.Cast<long?>().FirstOrDefault(an => plB.EventsFull(an!.Value, 16).Where(e => e.Type == 2300).Select(e => (double)e.Start).OrderBy(t => t).ToList() is { Count: > 0 } ts
                    && ts.Count == bt.Count && ts.Zip(bt).All(z => Math.Abs(z.First - z.Second) < 0.02));
                if (tid == null) Fail($"{who}: no animation of c{b.AnimId:D6}_pl.tae carries DS1's bullet events (2300 at {string.Join(", ", bt.Select(t => t.ToString("0.###", Inv)))} s)");
                else Ok($"{who}: TAE {tid}: 2300 at DS1's bullet times (rule B-BUL)");
                foreach (var x in sp.Where(x => x.Kind == "bullet" && x.Bullet != null).DistinctBy(x => x.Judge))
                    BulletRow(who, Ds1AttackParams.EnemyRow(behP, variation, x.Judge, 0) /* session 12, rule E-ID1 */, x.Bullet!, x.Dummy);
                foreach (var x in sp.Where(x => x.Kind == "speffect")) Fail($"{who}: SpEffect behaviour {x.SpEffect}: no DS2 rule");
            }
        }

        // ================================================================ DS1 event AI requests on placed regulars (rule E-REQ-P)
        foreach (var (ds2Map, ds1Map) in new[] { ("m18_01_00_00", "m18_01_00_00"), ("m19_42_00_00", "m10_02_00_00") })
        {
            string gp = Path.Combine(gd, "param", $"generatorparam_{ds2Map}.param");
            string esdPath = Path.Combine(gd, "ezstate", $"event_{ds2Map}.esd");
            if (!File.Exists(gp) || !File.Exists(esdPath)) continue;
            var gens = RawParam(gp);
            var esd = ESD.Read(esdPath);
            var sets = esd.StateGroups.Values.SelectMany(g => g.Values).SelectMany(s => s.EntryCommands.Concat(s.Conditions.SelectMany(c => c.PassCommands)))
                .Where(c => c.CommandID == 130101 && c.Arguments.Count >= 2).Select(c => (Flag: Esd.Expr(c.Arguments[0]), On: Esd.Expr(c.Arguments[1]))).ToHashSet();
            var reqs = EventRequests.ByEntity(dsr, ds1Map);
            foreach (var (entity, pairs) in reqs.OrderBy(k => k.Key))
            {
                var part = maps[ds1Map].Parts.Enemies.FirstOrDefault(e => e.EntityID == entity);
                if (part == null || (Ds1Boss.ForEntity(entity) != null)) continue;
                int gen = Build.Events.EntityRules.Generator(entity);
                var grow = gens.Rows.FirstOrDefault(r => r.ID == gen);
                if (grow == null) { Dev($"{ds2Map} entity {entity}: DS1 events send it AI requests ({string.Join(", ", pairs)}) but it is not placed here"); continue; }
                if (ds1Map == "m18_01_00_00") { Dev($"{ds2Map} entity {entity}: AI requests in the Asylum are not translated (Asylum left as built; only the Stray Demon gets one there)"); continue; }
                // the channels the think reads (registered as the build does) and the flags the build gave the generator
                EventRequests.Register(dsr, ds1Map);
                var ch = EventRequests.ForThink(part.ThinkParamID);
                bool translated = rows.Any(r => int.Parse(r[5], Inv) == part.ThinkParamID);
                if (ch.Count == 0 || !translated)
                {
                    Dev($"{ds2Map} entity {entity} (generator {gen}, think {part.ThinkParamID}): {(translated ? "its think has no channel" : "DS2 template AI (no translated DS1 think)")}: DS1's requests are no-ops for it");
                    continue;
                }
                var data = (byte[])grow["data"].Value;
                var bad = new List<string>();
                var flags = new List<string>();
                foreach (var c in ch)
                {
                    int have = BitConverter.ToInt32(data, 0x60 + 4 * (c.K - 1));
                    if (!Build.Events.FlagRules.IsAiChannelFlag(have)) { bad.Add($"logicEventFlag{c.K + 1} = {have}, not an E-REQ-P channel flag"); continue; }
                    if (!sets.Contains((have.ToString(Inv), "1"))) bad.Add($"no event state sets {have} (slot {c.Slot} = {c.V})");
                    if (!sets.Contains((have.ToString(Inv), "0"))) bad.Add($"no event state clears {have}");
                    flags.Add($"slot {c.Slot} = {c.V} → logicEventFlag{c.K + 1} {have}");
                }
                if (bad.Count > 0) Fail($"{ds2Map} entity {entity} (generator {gen}): {string.Join("; ", bad)}");
                else Ok($"{ds2Map} entity {entity} (generator {gen}, think {part.ThinkParamID}): {string.Join(", ", flags)}, set / cleared by the translated DS1 event (rule E-REQ-P)");
            }
        }

        // ================================================================ DS1 move-to-point targets (ai:SetEventMoveTarget → EventLocation)
        foreach (var (ds2Map, ds1Map) in new[] { ("m18_01_00_00", "m18_01_00_00"), ("m19_42_00_00", "m10_02_00_00") })
        {
            string elp = Path.Combine(gd, "param", $"eventlocation_{ds2Map}.param");
            if (!File.Exists(elp)) continue;
            var locs = RawParam(elp).Rows.Select(r => r.ID).ToHashSet();
            foreach (var think in maps[ds1Map].Parts.Enemies.Select(e => e.ThinkParamID).Distinct())
            {
                string cov = Path.Combine(pack, "lordran-ai", $"coverage-{think}.txt");
                if (!File.Exists(cov)) continue;
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(cov), @"POINT_EVENT (\d+)\) → move-to-point (\d+)"))
                {
                    int loc = int.Parse(m.Groups[2].Value, Inv);
                    if (locs.Contains(loc)) Ok($"{ds2Map} think {think}: DS1 move target {m.Groups[1].Value} → EventLocation {loc} exists");
                    else Fail($"{ds2Map} think {think}: DS1 move target {m.Groups[1].Value} → EventLocation {loc} missing");
                }
            }
        }

        Console.WriteLine($"special-check: {oks} ok, {devs} deviations, {fails} FAIL");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>A per-map param as raw rows (one "data" blob per row, as EnemyTest writes the generator params).</summary>
    static PARAM RawParam(string path)
    {
        var p = PARAM.Read(path);
        var blob = new PARAMDEF { ParamType = p.ParamType, DataVersion = p.ParamdefDataVersion, Unicode = true, FormatVersion = 104, BigEndian = false };
        blob.Fields.Add(new PARAMDEF.Field(blob, PARAMDEF.DefType.dummy8, "data") { ArrayLength = (int)p.DetectedSize });
        p.ApplyParamdef(blob);
        return p;
    }

    /// <summary>A path's decisions in DS1 terms (distance / height / rolls / requests), short.</summary>
    static string Conditions(Ds1Goal.PathResult p)
    {
        var s = string.Join(" ∧ ", p.Literals.Where(l => !l.ToString().Contains("Interupt") && !l.ToString().Contains("LadderAct")).Select(l => l.ToString()));
        return s.Length > 180 ? s[..180] + "…" : s;
    }
}
