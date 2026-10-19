using System.Globalization;
using Lordran.Tools.Build.Chr;
using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (track bal11): <c>balance-check PACK [--chars]</c> - DS1's difficulty / souls in a built pack against DS2's curve
/// (data\sandbox\balance\BALANCE.md option A; rules BAL1-BAL3, the one table Mappings\balance.tsv, <see cref="Ds1Balance"/>).
/// Population: the DS1 MSB placements of every built DS1 map (Maps.MapHosts rows whose host is in PACK\gamedata\map) whose NpcParam has
/// an EnemyParam in the pack's <c>lordran-chr\balance-manifest.tsv</c> (written by build-pack; every Ds1Calibration.Apply).
/// <list type="bullet">
/// <item><b>BAL-C0</b> (consistency): each sampled EnemyParam's HP and souls equal the active table's formula (a stale pack or a second
/// writer of these fields fails).</item>
/// <item><b>BAL-C1</b>: per DS1 tier (area SpEffect 7001-7015) with at least <see cref="MinSample"/> regular placements, the median HP,
/// physical defence and souls lie inside the tier's DS2 band (Mappings\balance.tsv "band" rows of its DS2 maps: min p25 .. max p90):
/// above = FAIL, below = WARN.
/// Tiers with fewer placements are listed, not judged.</item>
/// <item><b>BAL-C2</b>: every built boss's HP against its tier's DS2 boss HP band; above 1.5× the band's top = WARN (DS1's "boss
/// sponge", reported, not a failure).</item>
/// <item><b>BAL-C3</b>: the effective tier factors (HP, physical defence, physical attack) never decrease 7001 → 7015.</item>
/// <item><b>BAL-C4</b>: total DS1 souls of one pass (every DS1 MSB placement's getSoul × tier scale + every placed boss's GameAreaParam
/// bonusSoul_single × boss scale) inside 1.8-2.4M; projected from DS1's data while not every area is ported, plus the built areas'
/// own sum from the pack (EnemyParam dropSoulValue / BossBattleParam soulValue) against the same formula.</item>
/// </list>
/// Exit 1 on any FAIL (WARN / INFO do not fail).
/// </summary>
public static class BalanceCheck
{
    public const int MinSample = 10;
    public const double TotalMin = 1_800_000, TotalMax = 2_400_000, BossOutlier = 1.5;

    static double D(PARAM.Row r, string f) => Convert.ToDouble(r[f]?.Value ?? 0);
    static double Median(List<double> xs) { if (xs.Count == 0) return 0; var s = xs.Order().ToList(); int n = s.Count; return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2; }
    static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: balance-check PACK [--chars] [--balance FILE]"); return 1; }
        string packDir = args[1];
        bool chars = args.Contains("--chars");
        int bi = Array.IndexOf(args, "--balance");
        if (bi > 0) Ds1Balance.TablePath = Path.GetFullPath(args[bi + 1]);
        int fails = 0, warns = 0;
        void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {what}"); if (!ok) fails++; }
        void Warn(string what) { Console.WriteLine($"  [WARN] {what}"); warns++; }
        Console.WriteLine($"balance-check {packDir}: {Ds1Balance.Summary()}");

        var ds1 = new Build.Ds1Source(Program.DefaultDsrRoot);
        var npcP = ds1.Params.Params["NpcParam"]; var spP = ds1.Params.Params["SpEffectParam"]; var gaP = ds1.Params.Params["GameAreaParam"];
        string manifestPath = Path.Combine(packDir, "lordran-chr", Ds1Balance.ManifestName);
        var manifest = new Dictionary<int, int>();
        if (File.Exists(manifestPath))
            foreach (var l in File.ReadLines(manifestPath))
                if (l.Length > 0 && !l.StartsWith('#') && l.Split('\t') is { Length: >= 2 } c) manifest[int.Parse(c[0])] = int.Parse(c[1]);
        var header = File.Exists(manifestPath) ? File.ReadLines(manifestPath).FirstOrDefault() ?? "" : "";
        if (header.Length > 0) Console.WriteLine($"  pack built with: {header.TrimStart('#', ' ')}");

        // ---- BAL-C3: the effective tier factors (table + DS1's SpEffect rates)
        Console.WriteLine("BAL-C3 tier factors (DS1 rate → effective): tier | HP | phys def | phys attack (BAL3 applied) | souls ×");
        double pH = 0, pD = 0, pA = 0; bool mono = true;
        for (int t = Ds1Balance.FirstTier; t <= Ds1Balance.LastTier; t++)
        {
            var row = spP[t]!;
            double h = D(row, "maxHpRate"), d = D(row, "physicsDiffenceRate"), a = D(row, "physicsAttackPowerRate");
            double eh = Ds1Balance.Compress(h), ed = Ds1Balance.Compress(d), ea = Ds1Balance.EffectiveAttack(ds1, t, "physicsAttackPowerRate");
            Console.WriteLine($"  {t} {Ds1Balance.Tiers[t].Name,-32} HP {F(h)} → {F(eh)} | def {F(d)} → {F(ed)} | atk {F(a)} → {F(ea)} (×{F(ea / a)}{(Math.Abs(ea / a - Ds1Balance.Tiers[t].Damage) > 1e-9 ? $", table {F(Ds1Balance.Tiers[t].Damage)} raised to stay monotone" : "")}) | souls ×{F(Ds1Balance.Tiers[t].Souls)}");
            if (eh < pH - 1e-9 || ed < pD - 1e-9 || ea < pA - 1e-9) { mono = false; Console.WriteLine($"    decreases against {t - 1}"); }
            pH = eh; pD = ed; pA = ea;
        }
        Check(mono, "BAL-C3 effective HP / defence / attack factors never decrease 7001 → 7015 (DS1's order kept)");

        // ---- BAL-C4: total DS1 souls of one pass (DS1 data, the active table)
        // DS1's 17 maps (DSR's m12_00_00_01 is the DLC copy of m12_00_00_00: one of them, as BALANCE.md's gather.py)
        var msbs = Directory.GetFiles(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio"), "m1*.msb").Order()
            .GroupBy(f => Path.GetFileNameWithoutExtension(f)[..6]).Select(g => g.First()).ToList();
        double regTotal = 0, bossTotal = 0, oneOff = 0;
        var perMap = new List<string>();
        var bossEntities = new HashSet<int>(); var oneOffNpcs = new HashSet<int>();
        foreach (var f in msbs)
        {
            var msb = MSB1.Read(f);
            double r = 0, b = 0;
            foreach (var e in msb.Parts.Enemies)
            {
                if (e.EntityID > 0 && gaP[e.EntityID] is { } ga) { if (bossEntities.Add(e.EntityID)) b += Ds1Calibration.BossSouls(ga); continue; }
                if (npcP[e.NPCParamID] is not { } n) continue;
                // BALANCE.md's regular population: placed characters (NpcParam >= 100000: not the human NPCs) with getSoul < 3000; the
                // one-off characters (human NPCs, getSoul >= 3000: Kalameet, Gough, Havel, …) count once per NpcParam, reported apart
                if (e.NPCParamID < 100000 || D(n, "getSoul") >= 3000) { if (oneOffNpcs.Add(e.NPCParamID)) oneOff += Ds1Calibration.Souls(n); continue; }
                r += Ds1Calibration.Souls(n);
            }
            regTotal += r; bossTotal += b;
            perMap.Add($"{Path.GetFileNameWithoutExtension(f)[..6]} {r / 1000:0.#}k+{b / 1000:0.#}k");
        }
        double total = regTotal + bossTotal;
        Console.WriteLine($"BAL-C4 DS1 souls of one pass (projection from DS1's {msbs.Count} MSBs, the active table): regulars {regTotal / 1e6:0.000}M + bosses {bossTotal / 1e6:0.000}M ({bossEntities.Count} GameAreaParam bosses) = {total / 1e6:0.000}M; per map (regulars+bosses): {string.Join(", ", perMap)}");
        Console.WriteLine($"  [INFO] not in the total: {oneOffNpcs.Count} one-off characters (human NPCs, getSoul >= 3000), {oneOff / 1e6:0.000}M if every one is killed once");
        Check(total >= TotalMin && total <= TotalMax, $"BAL-C4 total DS1 souls {total / 1e6:0.00}M inside {TotalMin / 1e6:0.0}-{TotalMax / 1e6:0.0}M (DS2 base game ~2.0M)");

        if (manifest.Count == 0)
        {
            Console.WriteLine("  no DS1 characters in this pack (no lordran-chr\\balance-manifest.tsv): BAL-C0 / C1 / C2 not applicable");
            return Done(fails, warns);
        }
        var ps = ParamStore.LoadDs2(Path.Combine(packDir, "title"), ExtractDs2.DefaultOut);
        var enemy = ps.Params["EnemyParam"];
        var epOfNpc = manifest.GroupBy(x => x.Value).ToDictionary(g => g.Key, g => g.Min(x => x.Key));
        var bossSpecs = new[] { Ds1Boss.AsylumDemon, Ds1Boss.StrayDemon }.Concat(Ds1Boss.Burg).Concat(Ds1Boss.Depths).Where(s => !s.LargeRegular && enemy[s.EnemyParam] != null).ToList();
        var bossNpcs = bossSpecs.SelectMany(s => s.ExtraVariants.Select(x => x.Npc).Append(s.Ds1Npc)).ToHashSet();

        // ---- BAL-C0 consistency + samples of the built maps
        var samples = new Dictionary<int, List<(int Ep, int Npc)>>();
        int c0Bad = 0, c0 = 0, notPorted = 0;
        double builtSouls = 0, builtExpect = 0;
        // session 11 (burg11, round 8), rule H2: one row per host (its first: a merged map's MSB holds the others' enemies)
        var built = Build.Maps.MapHosts.Rows.Where(r => Directory.Exists(Path.Combine(packDir, "gamedata", "map", r.Host))
            && Build.Maps.MapHosts.Rows.First(x => x.Host == r.Host) == r).ToList();
        var bossesPlaced = new List<(int Entity, PARAM.Row Ga)>();
        foreach (var row in built)
        {
            var msb = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", row.Ds1Map + ".msb"));
            int n = 0, miss = 0;
            foreach (var e in msb.Parts.Enemies)
            {
                if (e.EntityID > 0 && gaP[e.EntityID] is { } ga) { bossesPlaced.Add((e.EntityID, ga)); continue; }
                if (npcP[e.NPCParamID] is not { } npc || D(npc, "hp") <= 1) continue;
                if (!epOfNpc.TryGetValue(e.NPCParamID, out int ep) || enemy[ep] is not { } er) { miss++; continue; }
                if (bossNpcs.Contains(e.NPCParamID)) continue;
                n++;
                int tier = Ds1Balance.AreaTier(npc);
                (samples.TryGetValue(tier, out var l) ? l : samples[tier] = []).Add((ep, e.NPCParamID));
                builtSouls += D(er, "dropSoulValue"); builtExpect += Ds1Calibration.Souls(npc);
            }
            notPorted += miss;
            Console.WriteLine($"  built DS1 map {row.Ds1Map} (host {row.Host}): {n} regular placements sampled, {miss} placements without a ported EnemyParam");
        }
        foreach (var (ep, npcId) in manifest)
        {
            var er = enemy[ep]; var npc = npcP[npcId];
            if (er == null || npc == null) continue;
            c0++;
            int wantHp = (int)Math.Round(D(npc, "hp") * Ds1Calibration.Resident(ds1, npc, "maxHpRate") * Ds1Calibration.HpScale);
            int wantDef = (int)Math.Round(D(npc, "def_phys") * Ds1Calibration.Resident(ds1, npc, "physicsDiffenceRate") * Ds1Calibration.PhysDefScale);
            bool isBoss = bossSpecs.Any(s => s.EnemyParam == ep);
            int wantSouls = isBoss ? 0 : Ds1Calibration.Souls(npc);
            bool ok = (int)D(er, "hitPoint") == wantHp && (int)D(er, "armorDefenseValue") == wantDef && (isBoss || (int)D(er, "dropSoulValue") == wantSouls);
            if (!ok) { c0Bad++; if (c0Bad <= 10) Console.WriteLine($"    EnemyParam {ep} (DS1 {npcId}): HP {D(er, "hitPoint")} (want {wantHp}), def {D(er, "armorDefenseValue")} (want {wantDef}), souls {D(er, "dropSoulValue")} (want {wantSouls})"); }
        }
        Check(c0Bad == 0, $"BAL-C0 {c0} calibrated EnemyParams carry the active table's HP / phys defence / souls ({c0Bad} differ)");

        // ---- BAL-C1 tier medians against the DS2 bands
        Console.WriteLine("BAL-C1 tier medians of the built maps (regular placements) against the DS2 band [min p25 .. max p90] of the tier's DS2 maps:");
        foreach (var (tier, list) in samples.OrderBy(x => x.Key))
        {
            var hp = list.Select(s => D(enemy[s.Ep]!, "hitPoint")).ToList();
            var df = list.Select(s => D(enemy[s.Ep]!, "armorDefenseValue")).ToList();
            var so = list.Select(s => D(enemy[s.Ep]!, "dropSoulValue")).ToList();
            var hp0 = list.Select(s => Math.Round(D(npcP[s.Npc]!, "hp") * Ds1AttackParams.RawResidentRate(ds1, npcP[s.Npc]!, "maxHpRate") * Ds1Calibration.HpScale)).ToList();
            var df0 = list.Select(s => Math.Round(D(npcP[s.Npc]!, "def_phys") * Ds1AttackParams.RawResidentRate(ds1, npcP[s.Npc]!, "physicsDiffenceRate") * Ds1Calibration.PhysDefScale)).ToList();
            var so0 = list.Select(s => Math.Round(D(npcP[s.Npc]!, "getSoul") * Ds1Calibration.SoulScale)).ToList();
            if (tier == 0) { Console.WriteLine($"  (no area SpEffect) n {list.Count}: HP {Median(hp)} def {Median(df)} souls {Median(so)} - not a tier, not judged"); continue; }
            var t = Ds1Balance.Tiers[tier];
            var bands = t.Ds2Maps.Select(m => Ds1Balance.Bands.GetValueOrDefault(m)).Where(b => b != null).Select(b => b!).ToList();
            if (bands.Count == 0) { Check(false, $"tier {tier}: no band rows for {string.Join(",", t.Ds2Maps)} in the table"); continue; }
            (double Lo, double Hi) B(Func<Ds1Balance.Band, double> lo, Func<Ds1Balance.Band, double> hi) => (bands.Min(lo), bands.Max(hi));
            var bh = B(b => b.HpP25, b => b.HpP90); var bd = B(b => b.DefP25, b => b.DefP90); var bs = B(b => b.SoulsP25, b => b.SoulsP90);
            double mh = Median(hp), md = Median(df), ms = Median(so);
            bool In((double Lo, double Hi) r, double v) => v >= r.Lo && v <= r.Hi;
            string line = $"tier {tier} {t.Name} (n {list.Count}, DS2 {string.Join("+", t.Ds2Maps)}): HP {Median(hp0)} → {mh} [{bh.Lo}-{bh.Hi}], def {Median(df0)} → {md} [{bd.Lo}-{bd.Hi}], souls {Median(so0)} → {ms} [{bs.Lo}-{bs.Hi}]";
            if (list.Count < MinSample) Console.WriteLine($"  [INFO] BAL-C1 {line}: fewer than {MinSample} placements, not judged{(In(bh, mh) && In(bd, md) && In(bs, ms) ? " (inside)" : " (outside)")}");
            else
            {
                // above the band = the overshoot BAL1-BAL3 exist to remove: FAIL. Below it = DS1's own species weaker than DS2's at that tier
                // (the Asylum's unarmoured Hollows are the calibration anchor itself; no balance rule raises a tier): WARN
                var high = new List<string>(); var low = new List<string>();
                foreach (var (nm, r, v) in new[] { ("HP", bh, mh), ("def", bd, md), ("souls", bs, ms) })
                    if (v > r.Hi) high.Add(nm); else if (v < r.Lo) low.Add(nm);
                if (high.Count > 0) Check(false, $"BAL-C1 {line}: above the band ({string.Join(", ", high)})");
                else if (low.Count > 0) Warn($"BAL-C1 {line}: below the band ({string.Join(", ", low)}; DS1 easier / poorer than DS2 here)");
                else Check(true, $"BAL-C1 {line}");
            }
        }

        // ---- BAL-C2 bosses
        Console.WriteLine("BAL-C2 bosses (HP against the tier's DS2 boss band; souls = BossBattleParam):");
        foreach (var s in bossSpecs)
        {
            var er = enemy[s.EnemyParam]!; var npc = npcP[s.Ds1Npc]!;
            int tier = Ds1Balance.AreaTier(npc);
            double hp = D(er, "hitPoint"), hp0 = Math.Round(D(npc, "hp") * Ds1AttackParams.RawResidentRate(ds1, npc, "maxHpRate") * Ds1Calibration.HpScale);
            var bb = ps.Params["BossBattleParam"][Build.Events.EntityRules.BossBattle(s.Ds1Entity)];
            var ga = gaP[s.Ds1Entity];
            int want = ga != null ? Ds1Calibration.BossSouls(ga) : Ds1Calibration.Souls(npc);   // no GameAreaParam row (the Hellkite): Ds1Boss pays getSoul
            string souls = bb != null ? $"souls {D(bb, "soulValue")} (want {want})" : $"souls {want} (no BossBattleParam: boss not placed yet, M3)";
            if (bb != null && (int)D(bb, "soulValue") != want) Check(false, $"BAL-C2 {s.Ds1Chr}: BossBattleParam souls {D(bb, "soulValue")} != {want}");
            if (tier == 0) { Console.WriteLine($"  [INFO] {s.Ds1Chr} EnemyParam {s.EnemyParam}: HP {hp0} → {hp}, no area SpEffect; {souls}"); continue; }
            var t = Ds1Balance.Tiers[tier];
            string l = $"{s.Ds1Chr} EnemyParam {s.EnemyParam} (tier {tier}): HP {hp0} → {hp} against DS2 {t.BossHpMin}-{t.BossHpMax} ({t.Ds2Bosses}), ×{F(hp / t.BossHpMax)} of the top; {souls}";
            if (hp > BossOutlier * t.BossHpMax) Warn($"BAL-C2 {l}: above {BossOutlier}×");
            else Console.WriteLine($"  [ok] BAL-C2 {l}");
        }

        // ---- BAL-C4 (built part): the pack's souls of the built maps against the formula
        double builtBoss = 0, builtBossWant = 0;
        foreach (var (e, ga) in bossesPlaced.DistinctBy(x => x.Entity))
        {
            builtBossWant += Ds1Calibration.BossSouls(ga);
            var bb = ps.Params["BossBattleParam"][Build.Events.EntityRules.BossBattle(e)];
            builtBoss += bb != null ? D(bb, "soulValue") : Ds1Calibration.BossSouls(ga);
        }
        Check(Math.Abs(builtSouls - builtExpect) < 0.5 && Math.Abs(builtBoss - builtBossWant) < 0.5,
            $"BAL-C4 built maps: regular souls {builtSouls:N0} (formula {builtExpect:N0}), boss souls {builtBoss:N0} (formula {builtBossWant:N0}; unplaced bosses at the formula) - {(builtSouls + builtBoss) / total * 100:0.0}% of the projected {total / 1e6:0.00}M; {notPorted} placements not ported yet");

        if (chars)
        {
            Console.WriteLine("characters (EnemyParam | DS1 NpcParam | name | tier | HP before → after | phys def before → after | souls before → after | attack factor before → after)");
            foreach (var (ep, npcId) in manifest.OrderBy(x => x.Key))
            {
                var er = enemy[ep]; var npc = npcP[npcId];
                if (er == null || npc == null) continue;
                bool isBoss = bossSpecs.Any(s => s.EnemyParam == ep);
                var spec = bossSpecs.FirstOrDefault(s => s.EnemyParam == ep);
                double hp0 = Math.Round(D(npc, "hp") * Ds1AttackParams.RawResidentRate(ds1, npc, "maxHpRate") * Ds1Calibration.HpScale);
                double df0 = Math.Round(D(npc, "def_phys") * Ds1AttackParams.RawResidentRate(ds1, npc, "physicsDiffenceRate") * Ds1Calibration.PhysDefScale);
                double so0 = isBoss && spec != null && gaP[spec.Ds1Entity] is { } g0 ? Math.Round(D(g0, "bonusSoul_single") * Ds1Calibration.SoulScale) : Math.Round(D(npc, "getSoul") * Ds1Calibration.SoulScale);
                double so1 = isBoss && spec != null && gaP[spec.Ds1Entity] is { } g1 ? Ds1Calibration.BossSouls(g1) : isBoss ? Ds1Calibration.Souls(npc) : D(er, "dropSoulValue");   // a boss without a GameAreaParam row (the Hellkite) pays getSoul through its boss event
                double a0 = Ds1AttackParams.RawResidentRate(ds1, npc, "physicsAttackPowerRate"), a1 = Ds1AttackParams.ResidentRate(ds1, npc, "physicsAttackPowerRate");
                Console.WriteLine($"  {ep} | {npcId} | {ps.RowName("EnemyParam", ep) ?? ""} | {Ds1Balance.AreaTier(npc)} | {hp0} → {D(er, "hitPoint")} | {df0} → {D(er, "armorDefenseValue")} | {so0} → {so1}{(isBoss ? " (boss)" : "")} | {F(a0)} → {F(a1)}");
            }
        }
        return Done(fails, warns);
    }

    static int Done(int fails, int warns)
    {
        Console.WriteLine(fails == 0 ? $"balance-check: all checks passed ({warns} WARN)" : $"balance-check: {fails} FAILED ({warns} WARN)");
        return fails == 0 ? 0 : 1;
    }
}
