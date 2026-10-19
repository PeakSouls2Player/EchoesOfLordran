using System.Collections.Concurrent;
using System.Globalization;
using SoulsFormats;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// Session 11 (track bal11, data\sandbox\balance\BALANCE.md option A): DS1's difficulty / soul ladder mapped onto DS2's curve. The
/// knobs are ONE table, <c>Mappings\balance.tsv</c> (build-pack <c>--balance FILE</c> swaps it, <c>--balance-strength C</c> overrides
/// its strength), read by <see cref="Ds1Calibration"/> / <see cref="Ds1AttackParams.ResidentRate"/>:
/// <list type="bullet">
/// <item><b>BAL1</b> (area-factor compression): for DS1's Area Scaling SpEffects 7001-7015 only, maxHpRate and every *DiffenceRate
/// fold as <c>1 + c·(rate − 1)</c> (c = <see cref="Strength"/>, 0.3) instead of <c>rate</c>; every other resident SpEffect stays 1:1
/// (rule C-AR).</item>
/// <item><b>BAL2</b> (tier souls): getSoul × the tier's soul scale (1.5 for 7001-7009, DS1's own 1.0 from Anor Londo 7010 on;
/// <see cref="UntieredSouls"/> without an area SpEffect); boss souls (GameAreaParam bonusSoul_single) × <see cref="BossSouls"/> (1.5).</item>
/// <item><b>BAL3</b> (tier damage): the area SpEffect's attack rates × the tier's damage (0.9 on 7009-7014, 0.8 on 7015), raised to
/// the previous tier's effective factor where the cut would make DS1's ladder decrease (<see cref="MonotoneDamage"/>).</item>
/// </list>
/// With strength 1, every soul scale 1.5, every damage 1 and monotone off (data\sandbox\bal11\balance\balance-off.tsv) the build is
/// the pre-bal11 calibration. <c>balance-check PACK</c> (Commands\BalanceCheck.cs) checks the result (BAL-C1..C4).
/// </summary>
public static class Ds1Balance
{
    public sealed record Tier(int Id, string Name, double Souls, double Damage, string[] Ds2Maps, double BossHpMin, double BossHpMax, string Ds2Bosses);
    public sealed record Band(string Map, int N, double HpP25, double HpMed, double HpP90, double DefP25, double DefMed, double DefP90, double SoulsP25, double SoulsMed, double SoulsP90);

    public const int FirstTier = 7001, LastTier = 7015;
    public static bool IsTier(int spEffect) => spEffect is >= FirstTier and <= LastTier;

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Mappings", "balance.tsv");
    /// <summary>build-pack --balance FILE (null = <see cref="DefaultPath"/>).</summary>
    public static string? TablePath;
    /// <summary>build-pack --balance-strength C (overrides the table's strength).</summary>
    public static double? StrengthOverride;

    static Dictionary<string, double>? set;
    static Dictionary<int, Tier>? tiers;
    static Dictionary<string, Band>? bands;

    static readonly object gate = new();
    static void Ensure()
    {
        if (set != null) return;
        lock (gate) { if (set == null) Load(); }
    }
    static void Load()
    {
        var s = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var t = new Dictionary<int, Tier>();
        var b = new Dictionary<string, Band>(StringComparer.OrdinalIgnoreCase);
        string path = TablePath ?? DefaultPath;
        static double N(string x) => double.Parse(x, CultureInfo.InvariantCulture);
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var c = line.Split('\t');
            switch (c[0])
            {
                case "set": s[c[1]] = N(c[2]); break;
                case "tier": t[int.Parse(c[1])] = new Tier(int.Parse(c[1]), c[2], N(c[3]), N(c[4]), c[5].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), N(c[6]), N(c[7]), c.Length > 8 ? c[8] : ""); break;
                case "band": b[c[1]] = new Band(c[1], int.Parse(c[2]), N(c[3]), N(c[4]), N(c[5]), N(c[6]), N(c[7]), N(c[8]), N(c[9]), N(c[10]), N(c[11])); break;
                default: throw new InvalidDataException($"{path}: unknown row kind '{c[0]}'");
            }
        }
        foreach (var k in new[] { "strength", "boss_souls", "untiered_souls", "monotone_damage", "compress_stamina", "catch_up" })
            if (!s.ContainsKey(k)) throw new InvalidDataException($"{path}: no 'set {k}' row");
        for (int id = FirstTier; id <= LastTier; id++) if (!t.ContainsKey(id)) throw new InvalidDataException($"{path}: no tier row {id}");
        if (s["catch_up"] != 0) throw new InvalidDataException($"{path}: catch_up {s["catch_up"]}: catch-up / gating (BALANCE.md options B / D) is not implemented, keep 0");
        tiers = t; bands = b; set = s;   // set last: Ensure() tests it without the lock
    }

    public static double Strength { get { Ensure(); return StrengthOverride ?? set!["strength"]; } }
    public static double BossSouls { get { Ensure(); return set!["boss_souls"]; } }
    public static double UntieredSouls { get { Ensure(); return set!["untiered_souls"]; } }
    public static bool MonotoneDamage { get { Ensure(); return set!["monotone_damage"] != 0; } }
    public static bool CompressStamina { get { Ensure(); return set!["compress_stamina"] != 0; } }
    public static IReadOnlyDictionary<int, Tier> Tiers { get { Ensure(); return tiers!; } }
    public static IReadOnlyDictionary<string, Band> Bands { get { Ensure(); return bands!; } }
    public static string Source => TablePath ?? DefaultPath;

    /// <summary>A one-line summary of the active settings (build log / balance-check header).</summary>
    public static string Summary() => $"balance table {Path.GetFileName(Source)}: strength {Strength.ToString(CultureInfo.InvariantCulture)}{(StrengthOverride != null ? " (--balance-strength)" : "")}, boss souls ×{BossSouls.ToString(CultureInfo.InvariantCulture)}, untiered souls ×{UntieredSouls.ToString(CultureInfo.InvariantCulture)}, monotone damage {(MonotoneDamage ? "on" : "off")}, stamina compression {(CompressStamina ? "on" : "off")}, no catch-up";

    static readonly string[] AttackFields = ["physicsAttackPowerRate", "magicAttackPowerRate", "fireAttackPowerRate", "thunderAttackPowerRate"];
    public static bool IsAttackField(string f) => AttackFields.Contains(f);
    public static bool IsCompressedField(string f) => f == "maxHpRate" || f.EndsWith("DiffenceRate", StringComparison.Ordinal) || (f == "maxStaminaRate" && CompressStamina);

    /// <summary>BAL1: the compressed factor of a DS1 area rate.</summary>
    public static double Compress(double rate) => 1 + Strength * (rate - 1);

    /// <summary>The area SpEffect (7001-7015) among the NPC's resident SpEffects, 0 when none.</summary>
    public static int AreaTier(PARAM.Row npc)
    {
        for (int k = 0; k < 8; k++)
            if (npc[$"spEffectID{k}"] is { } c && Convert.ToInt32(c.Value) is int id && IsTier(id)) return id;
        return 0;
    }

    /// <summary>BAL2: the soul scale of an NPC's tier.</summary>
    public static double SoulScale(int tier) => tier == 0 ? UntieredSouls : Tiers[tier].Souls;

    static readonly ConcurrentDictionary<(string Root, string Field), double[]> effAtk = new();
    /// <summary>BAL3: the effective attack factor of area SpEffect <paramref name="tier"/> for <paramref name="field"/>: DS1's rate × the
    /// tier's damage, raised to the previous tier's effective factor where that would be higher (monotone, DS1's ladder order).</summary>
    public static double EffectiveAttack(Ds1Source ds1, int tier, string field)
    {
        var arr = effAtk.GetOrAdd((ds1.Root + "|" + Source + "|" + MonotoneDamage, field), _ =>
        {
            var sp = ds1.Params.Params["SpEffectParam"];
            var a = new double[LastTier - FirstTier + 1];
            double prev = 0;
            for (int id = FirstTier; id <= LastTier; id++)
            {
                double r = sp[id]?[field] is { } f && Convert.ToDouble(f.Value) > 0 ? Convert.ToDouble(f.Value) : 1;
                double e = r * Tiers[id].Damage;
                if (MonotoneDamage && e < prev) e = prev;
                a[id - FirstTier] = prev = e;
            }
            return a;
        });
        return arr[tier - FirstTier];
    }

    /// <summary>The applied attack scale of the tier (effective factor / DS1 rate): 0.9 nominal, higher where clamped.</summary>
    public static double AppliedDamage(Ds1Source ds1, int tier, string field = "physicsAttackPowerRate")
    {
        var sp = ds1.Params.Params["SpEffectParam"];
        double r = sp[tier]?[field] is { } f && Convert.ToDouble(f.Value) > 0 ? Convert.ToDouble(f.Value) : 1;
        return EffectiveAttack(ds1, tier, field) / r;
    }

    // ------------------------------------------------------------------ build manifest (balance-check)

    /// <summary>Every EnemyParam the build calibrated from a DS1 NpcParam (<see cref="Ds1Calibration.Apply"/>), and NPC rows with souls:
    /// written by build-pack as <c>lordran-chr\balance-manifest.tsv</c>.</summary>
    public static readonly ConcurrentDictionary<int, int> Manifest = new();
    public static void Record(int enemyParam, int ds1Npc) => Manifest[enemyParam] = ds1Npc;
    public const string ManifestName = "balance-manifest.tsv";
    public static byte[] ManifestBytes()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# bal11: EnemyParam calibrated from a DS1 NpcParam (rules C-HP / BAL1-BAL3). {Summary()}");
        sb.AppendLine("# EnemyParam\tDS1 NpcParam");
        foreach (var (ep, npc) in Manifest.OrderBy(x => x.Key)) sb.AppendLine($"{ep}\t{npc}");
        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }
}
