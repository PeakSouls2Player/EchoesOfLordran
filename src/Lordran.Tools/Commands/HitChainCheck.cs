using System.Globalization;
using Lordran.Tools.Build.Chr;
using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// <c>hit-chain-check PACK [--vanilla ENEMYPARAM...]</c> (session 12, track ehit12; user report "DS1 enemies' attacks never hit"):
/// every enemy attack of the pack's own characters (EnemyParam rows vanilla DS2 does not have: the 1:1 regulars, the 1:1 bosses,
/// the DS2-body variants) resolves to a full DS2 hit chain the engine can use:
/// <list type="number">
/// <item>EnemyParam.defaultEquipWeapon → equipWeaponAId0w → EnemyEquipWeaponParam row → behaviorId B → EnemyBehavior(Second/Third)Param
/// [B] / EnemyComboBehaviorParam [B] (each active action / combo: BladeCtrlType ≠ 4);</item>
/// <item>every id an active slot names (DamageId00-02, GrabDamageId00-02, BulletId00-02; combo AttackId01-04 / BulletId01-04) lies
/// in DS2's ENEMY id range [100,000,000, 200,000,000) (rule E-ID1: the engine picks Player / Enemy / System param by the id's
/// range, decompile FUN_1404434c0 l.847321) and its row exists (EnemyDamageParam / EnemyBulletParam), as do its childDamage /
/// childeDamageId / childeBulletId chain (same range);</item>
/// <item>every such damage row has a hit shape: radius &gt; 0, a capsule (hitModelType 1) with length &gt; 0, on a dummy the model has
/// (hitDummyPolyType 0: the character's FLVER, gamedata/model/chr/cMMMM.bnd of the pack, else vanilla's), and a DamageCtrlParam
/// row (damageCategory); a bullet's own damage row names its spawn dummy the same way;</item>
/// <item>every 2200 event (attack hit, param ID = slot i) of the character's _pl TAE names a slot some active action or combo stage
/// fills (no hit event without a damage row behind it);</item>
/// <item>pack-wide (rule E-ID1 / E-ID2): every Player / Enemy / System Damage and Bullet row the pack adds lies in its table's range;</item>
/// <item>every object instance's damageId0 / bulletId0 (the pack's MapObjectInstanceParam files) resolves through the range split to an
/// existing row (damage: radius /// fills (no hit event without a damage row behind it).</item>gt; 0, its DamageCtrlParam).</item>
/// </list>
/// --vanilla ENEMYPARAM… checks vanilla DS2 rows (the rule's control: c1030 Hollow Infantry 103010 passes). Exit 1 on any FAIL.
/// </summary>
public static class HitChainCheck
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static int I(PARAM.Row r, string f) => Convert.ToInt32(r[f]?.Value ?? 0, Inv);
    static double D(PARAM.Row r, string f) => Convert.ToDouble(r[f]?.Value ?? 0, Inv);

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: hit-chain-check PACK|vanilla [--vanilla ENEMYPARAM...]"); return 1; }
        bool vanillaOnly = args[1] == "vanilla";
        string? pack = vanillaOnly ? null : args[1];
        var vanillaIds = args.SkipWhile(a => a != "--vanilla").Skip(1).Select(a => int.Parse(a, Inv)).ToList();
        var vps = ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        var ps = pack == null ? vps : ParamStore.LoadDs2(Path.Combine(pack, "title"), ExtractDs2.DefaultOut);
        PARAM P(string n) => ps.Params[n];
        var enemy = P("EnemyParam"); var weapon = P("EnemyEquipWeaponParam"); var dmg = P("EnemyDamageParam"); var bul = P("EnemyBulletParam");
        var ctrl = P("DamageCtrlParam"); var combo = P("EnemyComboBehaviorParam");
        var beh = new[] { P("EnemyBehaviorParam"), P("EnemyBehaviorSecondParam"), P("EnemyBehaviorThirdParam") };
        var vanillaEnemy = vps.Params["EnemyParam"].Rows.Select(r => r.ID).ToHashSet();
        var targets = vanillaIds.Count > 0 ? vanillaIds : [.. enemy.Rows.Where(r => !vanillaEnemy.Contains(r.ID)).Select(r => r.ID)];

        int fails = 0, oks = 0, skipped = 0;
        var failLines = new List<string>();
        void Fail(string m) { fails++; failLines.Add(m); }

        var dummyCache = new Dictionary<int, HashSet<int>?>();
        HashSet<int>? Dummies(int model)
        {
            if (dummyCache.TryGetValue(model, out var s)) return s;
            string rel = Path.Combine("model", "chr", $"c{model:D4}.bnd");
            string? path = pack != null && File.Exists(Path.Combine(pack, "gamedata", rel)) ? Path.Combine(pack, "gamedata", rel)
                : File.Exists(Path.Combine("data", "ds2", rel)) ? Path.Combine("data", "ds2", rel) : null;
            if (path == null) return dummyCache[model] = null;
            byte[] b = File.ReadAllBytes(path);
            if (DCX.Is(b)) b = DCX.Decompress(b);
            IEnumerable<BinderFile> files = BND4.Is(b) ? BND4.Read(b).Files : BND3.Is(b) ? BND3.Read(b).Files : [];
            var flv = files.Select(f => DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes).Where(FLVER2.Is).Select(FLVER2.Read).FirstOrDefault();
            return dummyCache[model] = flv?.Dummies.Select(d => (int)d.ReferenceID).ToHashSet();
        }
        var taeCache = new Dictionary<int, Ds2Tae?>();
        Ds2Tae? Tae(int animId)
        {
            if (taeCache.TryGetValue(animId, out var t)) return t;
            string rel = Path.Combine("timeact", "chr", $"c{animId:D6}_pl.tae");
            string? path = pack != null && File.Exists(Path.Combine(pack, "gamedata", rel)) ? Path.Combine(pack, "gamedata", rel)
                : File.Exists(Path.Combine("data", "ds2", rel)) ? Path.Combine("data", "ds2", rel) : null;
            return taeCache[animId] = path == null ? null : new Ds2Tae(File.ReadAllBytes(path));
        }
        // vanilla DS2 attack animations: (anim id, its 2200 events) over every vanilla chr _pl TAE; a pack animation identical to one is a
        // template copy (kept for the template's other variants), any other is the pack's own and its hit IDs must have a slot
        HashSet<string>? vanillaAnims = null;
        static string Sig(long an, IEnumerable<Ds2Tae.NewEvent> evs) => an + ":" + string.Join(",", evs.Where(x => x.Type == 2200).Select(x => $"{x.Start:0.###}-{x.End:0.###}#{BitConverter.ToInt32(x.Params, 0)}"));
        HashSet<string> VanillaAnims() => vanillaAnims ??= [.. Directory.GetFiles(Path.Combine("data", "ds2", "timeact", "chr"), "*_pl.tae").Select(f => new Ds2Tae(File.ReadAllBytes(f))).SelectMany(t => t.Records.Keys.Select(a => Sig(a, t.EventsFull(a, 4))))];
        static bool InRange(int id) => id >= Ds1AttackParams.Ds2EnemyRangeLo && id < Ds1AttackParams.Ds2EnemyRangeHi;

        // one damage row (and its childDamage chain): range, existence, shape, dummy, category
        bool DamageRow(string who, int id, HashSet<int>? dummies, bool bulletOwn)
        {
            bool ok = true;
            var seen = new HashSet<int>();
            for (int cur = id; cur > 0 && seen.Add(cur);)
            {
                if (!InRange(cur)) { Fail($"{who}: damage id {cur} outside DS2's enemy range [100000000, 200000000) (rule E-ID1: the engine looks in {(cur < 100000000 ? "PlayerDamageParam" : "SystemDamageParam")})"); return false; }
                var r = dmg[cur];
                if (r == null) { Fail($"{who}: EnemyDamageParam {cur} missing"); return false; }
                if (D(r, "radius") <= 0) { Fail($"{who}: EnemyDamageParam {cur}: radius {D(r, "radius")} (no hit shape)"); ok = false; }
                if (!bulletOwn && I(r, "hitModelType") == 1 && D(r, "length") <= 0) { Fail($"{who}: EnemyDamageParam {cur}: capsule with length {D(r, "length")}"); ok = false; }
                if (I(r, "hitDummyPolyType") == 0 && dummies != null && !dummies.Contains(I(r, "hitDummyPolyId")))
                { Fail($"{who}: EnemyDamageParam {cur}: {(bulletOwn ? "spawn" : "hit")} dummy {I(r, "hitDummyPolyId")} not on the model"); ok = false; }
                if (I(r, "damageCategory") > 0 && ctrl[I(r, "damageCategory")] == null) { Fail($"{who}: EnemyDamageParam {cur}: DamageCtrlParam {I(r, "damageCategory")} missing"); ok = false; }
                cur = I(r, "childDamage");
            }
            return ok;
        }
        bool BulletRow(string who, int id, HashSet<int>? dummies)
        {
            if (!InRange(id)) { Fail($"{who}: bullet id {id} outside DS2's enemy range (rule E-ID1: the engine looks in {(id < 100000000 ? "BulletParam" : "SystemBulletParam")})"); return false; }
            var b = bul[id];
            if (b == null) { Fail($"{who}: EnemyBulletParam {id} missing"); return false; }
            bool ok = true;
            for (int cb = I(b, "childeBulletId"), cd = I(b, "childeDamageId"), depth = 0; cb > 0 && depth < 4; depth++)
            {
                if (!InRange(cb) || bul[cb] == null) { Fail($"{who}: bullet {id}: child bullet {cb} {(InRange(cb) ? "missing" : "outside the enemy range")}"); ok = false; break; }
                if (cd > 0) ok &= DamageRow($"{who} bullet {id} child {cb}", cd, null, true);
                var c = bul[cb]!; cb = I(c, "childeBulletId"); cd = I(c, "childeDamageId");
            }
            return ok;
        }

        foreach (int epId in targets.Order())
        {
            var e = enemy[epId];
            if (e == null) { Fail($"EnemyParam {epId} missing"); continue; }
            int model = I(e, "modelId"), animId = I(e, "animId"), slot = I(e, "defaultEquipWeapon");
            string who0 = $"EnemyParam {epId} (c{model:D4}, anim {animId})";
            int wid = e[$"equipWeaponAId0{slot}"] != null ? I(e, $"equipWeaponAId0{slot}") : 0;
            if (wid <= 0) { skipped++; continue; }   // no enemy weapon (NPCs on player weapons, objects)
            var w = weapon[wid];
            if (w == null) { Fail($"{who0}: EnemyEquipWeaponParam {wid} missing"); continue; }
            int b = I(w, "behaviorId");
            var dummies = Dummies(model);
            if (dummies == null) Fail($"{who0}: model c{model:D4}.bnd not found (dummies unchecked)");
            var filled = new HashSet<int>();   // TAE 2200 IDs some slot fills
            int active = 0, rowsOk = 0;
            for (int blk = 0; blk < 3; blk++)
            {
                var r = beh[blk][b];
                if (r == null) continue;
                for (int k = 0; k < 10; k++)
                {
                    if (I(r, $"action{k}BladeCtrlType") == 4) continue;
                    int n = blk * 10 + k; string who = $"{who0} action {n}";
                    bool any = false;
                    for (int s = 0; s < 3; s++)
                    {
                        int d = I(r, $"action{k}DamageId0{s}"), g = I(r, $"action{k}GrabDamageId0{s}"), bl = I(r, $"action{k}BulletId0{s}");
                        if (d > 0) { any = true; filled.Add(s); if (DamageRow($"{who} DamageId0{s}", d, dummies, bl > 0)) rowsOk++; }
                        if (g > 0) { any = true; if (DamageRow($"{who} GrabDamageId0{s}", g, dummies, false)) rowsOk++; }
                        if (bl > 0) { any = true; if (BulletRow($"{who} BulletId0{s}", bl, dummies)) rowsOk++; if (d <= 0) Fail($"{who}: BulletId0{s} {bl} without DamageId0{s} (the shot hits for nothing)"); }
                    }
                    if (any) active++;
                }
            }
            var c = combo[b];
            if (c != null)
                for (int k = 0; k < 7; k++)
                {
                    if (I(c, $"combo{k}BladeCtrlType") == 4) continue;
                    string who = $"{who0} combo {50 + k}";
                    bool any = false;
                    for (int s = 1; s <= 4; s++)
                    {
                        int d = I(c, $"combo{k}AttackId0{s}"), bl = I(c, $"combo{k}BulletId0{s}");
                        if (d > 0) { any = true; filled.Add(s - 1); if (DamageRow($"{who} AttackId0{s}", d, dummies, bl > 0)) rowsOk++; }
                        if (bl > 0) { any = true; if (BulletRow($"{who} BulletId0{s}", bl, dummies)) rowsOk++; if (d <= 0) Fail($"{who}: BulletId0{s} {bl} without AttackId0{s}"); }
                    }
                    if (any) active++;
                }
            if (active == 0) { skipped++; continue; }   // a weapon without attacks (e.g. an unarmed variant whose template has none)
            // 4. TAE 2200 IDs vs filled slots: the animations this pack added (ids no vanilla chr TAE has; the template's own
            // animations stay in the file for its other weapon variants, as vanilla's shared c103010_pl serves 103010 and 103040)
            int hitEvents = 0;
            var tae = pack != null ? Tae(animId) : null;
            if (pack != null && tae == null) Fail($"{who0}: c{animId:D6}_pl.tae not found");
            else if (tae != null)
                foreach (var an in tae.Records.Keys.Where(a => !VanillaAnims().Contains(Sig(a, tae.EventsFull(a, 4)))))
                    foreach (var ev in tae.EventsFull(an, 4).Where(x => x.Type == 2200))
                    {
                        hitEvents++;
                        int id = BitConverter.ToInt32(ev.Params, 0);
                        if (!filled.Contains(id)) Fail($"{who0}: TAE {an} 2200 at {ev.Start.ToString("0.###", Inv)} s has ID {id}, a slot no active action / combo stage fills");
                    }
            oks++;
            Console.WriteLine($"  {who0}: behaviour {b}, {active} active actions/combos, {rowsOk} damage/bullet chains ok, {hitEvents} TAE hit events");
        }
        // 5. rule E-ID1 / E-ID2, pack-wide: every damage / bullet row the pack adds (an id vanilla's table lacks: enemies, bosses,
        // objects, the player's DS1 weapons / spells / ammo) lies in the range the engine reads that table from
        int extraRows = 0;
        string[] ranged = ["PlayerDamageParam", "EnemyDamageParam", "SystemDamageParam", "BulletParam", "EnemyBulletParam", "SystemBulletParam"];
        if (pack != null)
            foreach (var pn in ranged)
            {
                var (lo, hi) = Ds1AttackParams.RangeOf(pn)!.Value;
                var van = vps.Params[pn].Rows.Select(r => r.ID).ToHashSet();
                foreach (var r in ps.Params[pn].Rows.Where(r => !van.Contains(r.ID)))
                {
                    extraRows++;
                    if (r.ID < lo || r.ID >= hi) Fail($"{pn} {r.ID}: a pack row outside the range the engine reads {pn} from [{lo}, {hi}) (rule E-ID1/E-ID2: a reference to it resolves in another table)");
                }
            }
        // 6. rule E-ID2: every object instance's damageId0 / bulletId0 (the pack's per-map MapObjectInstanceParam) resolves through the
        // range split to an existing row, a System damage row with its DamageCtrlParam and a nonzero radius
        int objRefs = 0;
        PARAM? Table(int id, bool bullet) => id < 100_000_000 ? P(bullet ? "BulletParam" : "PlayerDamageParam")
            : id < 200_000_000 ? P(bullet ? "EnemyBulletParam" : "EnemyDamageParam") : P(bullet ? "SystemBulletParam" : "SystemDamageParam");
        if (pack != null)
        {
            var defs = Directory.GetFiles(Path.Combine(ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
            foreach (var f in Directory.GetFiles(Path.Combine(pack, "gamedata", "param"), "mapobjectinstanceparam_*.param"))
            {
                var mp = PARAM.Read(f);
                if (!mp.ApplyParamdefCarefully(defs)) { Fail($"{Path.GetFileName(f)}: no paramdef fits"); continue; }
                foreach (var r in mp.Rows)
                {
                    int dId = I(r, "damageId0"), bId = I(r, "bulletId0");
                    string who = $"{Path.GetFileNameWithoutExtension(f)} instance {r.ID}";
                    if (dId > 0)
                    {
                        objRefs++;
                        var t = Table(dId, false)!; var row = t[dId];
                        if (row == null) Fail($"{who}: damageId0 {dId} → {(dId < 100_000_000 ? "PlayerDamageParam" : dId < 200_000_000 ? "EnemyDamageParam" : "SystemDamageParam")} (by its range) has no such row{(P("SystemDamageParam")[dId] != null ? " (it is a SystemDamageParam row: wrong range, rule E-ID2)" : "")}");
                        else
                        {
                            if (D(row, "radius") <= 0) Fail($"{who}: damage row {dId}: radius {D(row, "radius")}");
                            if (I(row, "damageCategory") > 0 && ctrl[I(row, "damageCategory")] == null) Fail($"{who}: damage row {dId}: DamageCtrlParam {I(row, "damageCategory")} missing");
                        }
                    }
                    if (bId > 0) { objRefs++; if (Table(bId, true)![bId] == null) Fail($"{who}: bulletId0 {bId} has no row in the table its range selects"); }
                }
            }
        }
        foreach (var l in failLines) Console.WriteLine("  FAIL " + l);
        Console.WriteLine($"hit-chain-check: {oks} characters checked, {skipped} without enemy attacks; {extraRows} pack damage/bullet rows range-checked; {objRefs} object damage/bullet references; {(fails == 0 ? "all chains resolve" : $"{fails} FAIL")}");
        return fails == 0 ? 0 : 1;
    }
}
