using System.Globalization;
using Lordran.Tools.Build;
using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 12 (track bomb12, user: "the firebombs bug"): <c>bullet-check PACK</c> — offline integrity of every projectile chain a
/// built pack ships, the player's (ItemParam → BulletParam → PlayerDamageParam, children) and the enemies' (EnemyBehavior* /
/// EnemyComboBehaviorParam → EnemyBulletParam → EnemyDamageParam → DamageCtrlParam, children), against vanilla DS2:
/// <list type="bullet">
/// <item><b>P1</b> DS2's own throwables (an ItemParam row with a BulletParam row of the same id: Firebomb 60570000, Black Firebomb
/// 60575000, knives, …) and their whole bullet / damage chain are byte-identical to vanilla (shared DS1 goods use DS2's version,
/// rule 4: the Dev Kit / --goods all hand out exactly these rows).</item>
/// <item><b>P2</b> every pack ItemParam row whose use is a throw (itemUsageParamId of a vanilla throwable) has its BulletParam and
/// PlayerDamageParam row (DS2 looks the bullet up by the item id).</item>
/// <item><b>R1</b> every bullet row (BulletParam, EnemyBulletParam) names effects that exist in the title effect banks (vanilla
/// sfx9999[_Append], or the pack's own sfx9999_Append) and are below 30000 (rule B2b-id: DS2 aborts on a title-bank id ≥ 30000),
/// and children / damage rows that exist; ItemParam start / action / effect sfx likewise.</item>
/// <item><b>R2</b> every enemy behaviour / combo slot's BulletId / DamageId names an existing row; every damage row's damageCategory
/// an existing DamageCtrlParam row.</item>
/// <item><b>B1</b> (the fix of this track, rule E10f): a ported DS1 bullet's damage category takes the DS1 <i>Bullet</i>'s attack
/// attribute (DS1 Bullet.atkAttribute overrides the AtkParam's: the Hollow Warrior's bottle explosion 25402 is 4 "none", its
/// AtkParam_Npc 25400 says 0 "slash"), so a fire explosion is no DS2 slash (DamageCtrlParam.attackType 3 as DS2's own
/// firebomb explosions 200300120 / the player's 99993020).</item>
/// </list>
/// A failure that vanilla DS2 has itself is KNOWN, not FAIL. Exit 1 on any FAIL.
/// </summary>
public static class BulletCheck
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static int I(PARAM.Row r, string f) => Convert.ToInt32(r[f]?.Value ?? 0, Inv);
    static string Cells(PARAM.Row r) => string.Join("|", r.Cells.Select(c => c.Value is byte[] x ? Convert.ToHexString(x) : Convert.ToString(c.Value, Inv)));

    static readonly string[] BulletSfx = ["bulletSfxId", "explosionSfxId", "attachSfxId", "muzzleFlashSfxId", "hitbackSfxId"];
    static readonly string[] ItemSfx = ["startSfxId", "actionSfxId", "effectSfxId"];
    static readonly (string Bullet, string Damage)[] Children = [("childeBulletId", "childeDamageId"), ("childeBulletId02", "childeDamageId02"), ("childeBulletId03", "childeDamageId03")];

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: bullet-check PACK [--all]"); return 1; }
        string pack = args[1];
        bool all = args.Contains("--all");
        var van = ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        if (pack == "--survey")
        {
            // vanilla DS2 falling bullets: launch pitch, weight, gravityValue, their product (rule E10g evidence)
            foreach (var bp in new[] { "EnemyBulletParam", "BulletParam" })
                foreach (var r in van.Params[bp].Rows.Where(r => Convert.ToDouble(r["gravityValue"]!.Value, Inv) > 0))
                {
                    double w = Convert.ToDouble(r["weight"]!.Value, Inv), g = Convert.ToDouble(r["gravityValue"]!.Value, Inv);
                    Console.WriteLine($"{bp}\t{r.ID}\t{van.RowName(bp, r.ID)}\tpitch {-Convert.ToDouble(r["shootAngle"]!.Value, Inv) * 180 / Math.PI:0.#}\tv0 {r["initialVelocity"]!.Value}\tweight {w}\tgravity {g}\tw*g {w * g:0.##}\tdownStart {r["downStartTime"]!.Value}\tupDownCorr {Convert.ToDouble(r["upDownCorrectAngle"]!.Value, Inv) * 180 / Math.PI:0.#}\tchild {r["childeBulletId"]!.Value} expl {r["isChildeExplosion"]!.Value}\tsfx {r["bulletSfxId"]!.Value}");
                }
            return 0;
        }
        var ps = ParamStore.LoadDs2(Path.Combine(pack, "title"), ExtractDs2.DefaultOut);
        int fails = 0, known = 0, oks = 0;
        void Fail(string m) { fails++; Console.WriteLine("  FAIL  " + m); }
        void Known(string m) { known++; if (all) Console.WriteLine("  KNOWN " + m); }
        void Ok(string m) { oks++; Console.WriteLine("  ok    " + m); }

        // ---- effects the title banks hold (vanilla + the pack's own append bank, rule B2b)
        var fx = new HashSet<int>(Build.Maps.FfxPorter.Ds2UsedIds());
        string ownAppend = Path.Combine(pack, "title", "sfx", "sfx9999_Append.ffxbnd.dcx");
        if (File.Exists(ownAppend))
            foreach (var f in BND4.Read(DCX.Decompress(File.ReadAllBytes(ownAppend))).Files)
            {
                string n = Path.GetFileName(f.Name);
                if (n.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase) && int.TryParse(n[1..^4], out int id)) fx.Add(id);
            }
        string? FxProblem(int id) => id <= 0 ? null : id >= Build.Maps.FfxPorter.LazyIdLimit ? $"effect {id} ≥ {Build.Maps.FfxPorter.LazyIdLimit} (DS2 aborts)" : fx.Contains(id) ? null : $"effect {id} in no title effect bank";
        Console.WriteLine($"bullet-check {pack}: {fx.Count} title-bank effect ids{(File.Exists(ownAppend) ? " (incl. the pack's sfx9999_Append)" : "")}");

        PARAM P(ParamStore s, string n) => s.Params[n];
        bool SameAsVanilla(string param, int id) => P(van, param)[id] is { } v && P(ps, param)[id] is { } p && Cells(v) == Cells(p);

        // ---- one bullet row's problems (R1)
        List<string> BulletProblems(ParamStore s, string bulletParam, string damageParam, PARAM.Row r)
        {
            var bad = new List<string>();
            foreach (var f in BulletSfx) if (r[f] != null && FxProblem(I(r, f)) is { } m) bad.Add($"{f}: {m}");
            foreach (var (cb, cd) in Children)
            {
                if (r[cb] == null) continue;
                int b = I(r, cb), d = I(r, cd);
                if (b > 0 && P(s, bulletParam)[b] == null) bad.Add($"{cb} {b} missing");
                if (d > 0 && P(s, damageParam)[d] == null) bad.Add($"{cd} {d} missing");
            }
            return bad;
        }
        List<string> DamageProblems(ParamStore s, PARAM.Row d)
        {
            var bad = new List<string>();
            if (d["damageCategory"] is { } c && Convert.ToInt64(c.Value, Inv) is long cat && cat > 0 && cat <= int.MaxValue && P(s, "DamageCtrlParam")[(int)cat] == null) bad.Add($"damageCategory {cat} missing");
            return bad;
        }
        // KNOWN when vanilla has the same row with the same problems
        void Judge(string what, string param, int id, List<string> bad, List<string>? vanBad)
        {
            if (bad.Count == 0) return;
            if (vanBad != null && vanBad.SequenceEqual(bad)) { Known($"{what}: {string.Join("; ", bad)} (vanilla too)"); return; }
            Fail($"{what}: {string.Join("; ", bad)}");
        }

        // ================================================================ P1 / P2: the player's throwables
        Console.WriteLine("- player throwables (ItemParam → BulletParam by item id)");
        var vItems = P(van, "ItemParam"); var vBul = P(van, "BulletParam");
        var throwables = vItems.Rows.Where(r => vBul[r.ID] != null).Select(r => r.ID).ToList();
        // a throw usage = one that only vanilla throwables use (1700 Firebomb …; not 1000, which weapons share)
        var throwUsage = throwables.Select(id => I(vItems[id]!, "itemUsageParamId"))
            .Where(u => vItems.Rows.Where(r => I(r, "itemUsageParamId") == u).All(r => vBul[r.ID] != null)).ToHashSet();
        Console.WriteLine($"  vanilla throwables: {throwables.Count} items; throw-only usages [{string.Join(", ", throwUsage.Order())}]");
        foreach (int id in throwables)
        {
            // the whole chain from the item: bullet, its children (depth-first), damage rows
            var chainB = new SortedSet<int>(); var chainD = new SortedSet<int> { id };
            void Walk(int b) { if (!chainB.Add(b) || vBul[b] is not { } r) return; foreach (var (cb, cd) in Children) { if (r[cb] == null) continue; if (I(r, cd) > 0) chainD.Add(I(r, cd)); if (I(r, cb) > 0) Walk(I(r, cb)); } }
            Walk(id);
            var changed = new List<string>();
            if (!SameAsVanilla("ItemParam", id)) changed.Add($"ItemParam {id}");
            foreach (int b in chainB) if (!SameAsVanilla("BulletParam", b)) changed.Add($"BulletParam {b}{(P(ps, "BulletParam")[b] == null ? " (missing)" : "")}");
            foreach (int d in chainD) if (vBul.Rows.Count > 0 && P(van, "PlayerDamageParam")[d] != null && !SameAsVanilla("PlayerDamageParam", d)) changed.Add($"PlayerDamageParam {d}{(P(ps, "PlayerDamageParam")[d] == null ? " (missing)" : "")}");
            string name = van.RowName("ItemParam", id);
            if (changed.Count > 0) Fail($"P1 {id} {name}: differs from vanilla: {string.Join(", ", changed)}");
            else if (all || id is 60570000 or 60575000) Ok($"P1 {id} {name}: item + bullets [{string.Join(", ", chainB)}] + damage [{string.Join(", ", chainD)}] = vanilla");
            else oks++;
        }
        foreach (var r in P(ps, "ItemParam").Rows.Where(r => vItems[r.ID] == null && throwUsage.Contains(I(r, "itemUsageParamId"))))
        {
            var bad = new List<string>();
            if (P(ps, "BulletParam")[r.ID] == null) bad.Add($"BulletParam {r.ID} missing");
            if (P(ps, "PlayerDamageParam")[r.ID] == null) bad.Add($"PlayerDamageParam {r.ID} missing");
            if (bad.Count > 0) Fail($"P2 pack item {r.ID} uses throw usage {I(r, "itemUsageParamId")}: {string.Join("; ", bad)}");
            else Ok($"P2 pack item {r.ID}: throw usage {I(r, "itemUsageParamId")} with its bullet + damage rows");
        }
        foreach (var r in P(ps, "ItemParam").Rows)
        {
            var bad = ItemSfx.Where(f => r[f] != null).Select(f => FxProblem(I(r, f)) is { } m ? $"{f}: {m}" : null).OfType<string>().ToList();
            Judge($"R1 ItemParam {r.ID}", "ItemParam", r.ID, bad, vItems[r.ID] is { } v ? ItemSfx.Where(f => v[f] != null).Select(f => FxProblem(I(v, f)) is { } m ? $"{f}: {m}" : null).OfType<string>().ToList() : null);
        }

        // ================================================================ R1: every bullet row
        foreach (var (bp, dp) in new[] { ("BulletParam", "PlayerDamageParam"), ("EnemyBulletParam", "EnemyDamageParam") })
        {
            int n = 0, nNew = 0;
            foreach (var r in P(ps, bp).Rows)
            {
                n++; if (P(van, bp)[r.ID] == null) nNew++;
                var vb = P(van, bp)[r.ID] is { } v ? BulletProblems(van, bp, dp, v) : null;
                Judge($"R1 {bp} {r.ID}", bp, r.ID, BulletProblems(ps, bp, dp, r), vb);
            }
            int m = 0;
            foreach (var d in P(ps, dp).Rows)
            {
                m++;
                Judge($"R2 {dp} {d.ID}", dp, d.ID, DamageProblems(ps, d), P(van, dp)[d.ID] is { } vd ? DamageProblems(van, vd) : null);
            }
            Console.WriteLine($"- {bp}: {n} rows ({nNew} not vanilla), {dp}: {m} rows checked (effects, children, damage categories)");
        }

        // ================================================================ R2: enemy behaviour / combo references
        var eb = P(ps, "EnemyBulletParam"); var ed = P(ps, "EnemyDamageParam");
        string[] behParams = ["EnemyBehaviorParam", "EnemyBehaviorSecondParam", "EnemyBehaviorThirdParam", "EnemyComboBehaviorParam"];
        // (field, value) pairs that point nowhere in vanilla DS2 already (e.g. the Smelter Demon's action3BulletId01 130500311, which
        // template clones inherit): KNOWN, not a pack defect
        IEnumerable<(string F, int V, bool Bullet)> Refs(PARAM.Row r)
        {
            foreach (var c in r.Cells)
            {
                string f = c.Def.InternalName;
                bool bullet = f.Contains("BulletId", StringComparison.Ordinal), damage = f.Contains("DamageId", StringComparison.Ordinal) || f.Contains("AttackId0", StringComparison.Ordinal);
                if (!bullet && !damage) continue;
                int v = Convert.ToInt32(c.Value, Inv);
                if (v > 0) yield return (f, v, bullet);
            }
        }
        var vanDangling = new HashSet<(bool, int)>();
        foreach (string beh in behParams)
            foreach (var r in P(van, beh).Rows)
                foreach (var (_, v, isB) in Refs(r))
                    if ((isB ? P(van, "EnemyBulletParam")[v] : P(van, "EnemyDamageParam")[v]) == null) vanDangling.Add((isB, v));
        foreach (string beh in behParams)
        {
            int refs = 0;
            foreach (var r in P(ps, beh).Rows)
                foreach (var (f, v, isB) in Refs(r))
                {
                    refs++;
                    if ((isB ? eb[v] : ed[v]) != null) continue;
                    string m = $"R2 {beh} {r.ID}: {f} {v}: no {(isB ? "EnemyBulletParam" : "EnemyDamageParam")}";
                    if (vanDangling.Contains((isB, v))) Known(m + " (dangling in vanilla DS2 too)"); else Fail(m);
                }
            Console.WriteLine($"- {beh}: {P(ps, beh).Rows.Count} rows, {refs} bullet / damage references");
        }


        // ================================================================ B1: ported DS1 bullets' damage category attack type (rule E10f)
        Console.WriteLine("- ported DS1 bullets (EnemyBulletParam rule E-ID1 EnemyRow(V, J, 0) of DS1 BehaviorParam 2_VVVVV_JJJ; explosion child kind 1): attack attribute");
        var dsrBeh = Ds1Source.LoadParam(Program.DefaultDsrRoot, "BehaviorParam")!; var dsrBul = Ds1Source.LoadParam(Program.DefaultDsrRoot, "Bullet")!;
        var dsrAtk = Ds1Source.LoadParam(Program.DefaultDsrRoot, "AtkParam_Npc")!;
        var dc = P(ps, "DamageCtrlParam");
        // session 12 (merge12): rule E-ID1 (ehit12) moved the 1:1 rows from 100,000,000 + BehaviorParam id to EnemyRow(V, J, k)
        static int? BehOf(PARAM beh, int id) => Build.Chr.Ds1AttackParams.FromEnemyRow(beh, id) is { K: 0 } e ? 200_000_000 + e.Variation * 1000 + e.Judge : null;
        foreach (var r in eb.Rows.Where(r => BehOf(dsrBeh, r.ID) is int bid && dsrBeh[bid] is { } be && I(be, "refType") == 1))
        {
            var be = dsrBeh[BehOf(dsrBeh, r.ID)!.Value]!;
            if (dsrBul[I(be, "refId")] is not { } b) continue;
            void One(PARAM.Row bullet, int rowId, string role)
            {
                if (ed[rowId] is not { } d || dsrAtk[I(bullet, "atkId_Bullet")] is not { } atk) return;   // a bottle without a hit of its own
                int cat = I(d, "damageCategory");
                if (cat < 950_000_000 || dc[cat] is not { } c) return;   // not a DS1-calibrated category (E9c)
                bool elemental = Build.Chr.Ds1Calibration.ElementalOnly(atk);   // E10f scope: hits without physical damage
                int want = Build.Chr.Ds1Calibration.AttackTypeOf(elemental ? Build.Chr.Ds1Calibration.BulletAttribute(bullet, atk) : I(atk, "atkAttribute"));
                string what = $"B1 {role} EnemyDamageParam {rowId} (DS1 Bullet {bullet.ID} atkAttribute {I(bullet, "atkAttribute")}, AtkParam_Npc {atk.ID} atkAttribute {I(atk, "atkAttribute")}) → DamageCtrlParam {cat} attackType {I(c, "attackType")}";
                if (I(c, "attackType") != want) Fail($"{what} ≠ {want}");
                else if (all || bullet.ID is 25400 or 25402) Ok($"{what} = {(elemental ? "the DS1 bullet's" : "the AtkParam's (physical hit)")} attack attribute");
                else oks++;
            }
            One(b, r.ID, "bullet");
            // B2 (rule E10g): a thrown DS1 bullet (hit bullet + in-range gravity) falls at DS1's rate: weight × gravityValue = DS1
            // gravityInRange, weight = DS2's Firebomb's
            double g1 = Convert.ToDouble(b["gravityInRange"]!.Value, Inv);
            if (I(b, "HitBulletID") > 0 && g1 > 0)
            {
                double w = Convert.ToDouble(r["weight"]!.Value, Inv), g = Convert.ToDouble(r["gravityValue"]!.Value, Inv);
                string what = $"B2 thrown EnemyBulletParam {r.ID} (DS1 Bullet {b.ID}: +{b["shootAngleXZ"]!.Value}°, {b["initVellocity"]!.Value} m/s, gravity {g1} m/s²): DS2 weight {w} × gravityValue {g:0.###} = {w * g:0.###}";
                if (Math.Abs(w * g - g1) > 1e-3 || Math.Abs(w - Build.Chr.Ds1AttackParams.ThrownWeight) > 1e-6) Fail($"{what} ≠ DS1 {g1} with weight {Build.Chr.Ds1AttackParams.ThrownWeight} (rule E10g; DS2's own thrown bombs: 10)");
                else Ok($"{what} = DS1 (rule E10g, as DS2's Firebomb 5 × 2)");
            }
            if (I(b, "HitBulletID") > 0 && dsrBul[I(b, "HitBulletID")] is { } hb && I(r, "childeDamageId") is int cd && cd > 0) One(hb, cd, "explosion");
        }

        Console.WriteLine($"bullet-check: {oks} ok, {known} known (vanilla has them too{(all ? "" : "; --all lists them")}), {fails} FAIL");
        return fails > 0 ? 1 : 0;
    }
}
