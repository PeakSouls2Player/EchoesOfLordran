using System.Text;

namespace Lordran.Tools.Build.Events;

/// <summary>
/// The DS1 → DS2 flag slot table (session 9, track npcflags; rules F1-F3 in <see cref="FlagRules"/>):
/// `Mappings/ds1-flag-slots.tsv`, one row per DS1 flag of the census (<see cref="Ds1FlagCensus"/>, all of DSR's data):
///   G  DS1 flag   DS2 global flag (blocks 10/20)          a persistent DS1 flag (every class but 1AAB5nnn)
///   T  DS1 flag   slot                                  a temporary DS1 flag 1AAB5nnn: slot in its DS1 map's temporary list
///   R  DS1 event  slot                                  the restart request of DS1 event id + slot (rule R9): same list
///   S  key        DS2 global flag (blocks 10/20)          session 10 (asy-items), rule C1: a synthetic persistent flag for
///                                                         DS1 state DS2 has no data for (key "ds1-origin:c" = the DS1 class
///                                                         c, 0 Warrior … 9 Deprived, read by 4:11); same pool as G rows
/// The table is **append-only**: a regeneration keeps every existing row and gives new census entries the next free slots,
/// so a save written with one pack keeps meaning the same flags with the next. Pool for G rows: the bits of DS2's global
/// blocks that no vanilla script, param or engine constant references, with a guard of <see cref="Guard"/> bits around
/// each reference and only runs of at least <see cref="MinRun"/> bits (`flag-survey ds1 --write-table` prints them).
/// </summary>
public static class FlagSlots
{
    public const string FileName = "ds1-flag-slots.tsv";
    public const int Guard = 16, MinRun = 64;

    public sealed class Table
    {
        public readonly Dictionary<int, int> Global = [];
        public readonly Dictionary<int, int> Temp = [];
        public readonly Dictionary<int, int> Restart = [];
        /// <summary>Session 10 (asy-items), rule C1: synthetic key ("ds1-origin:c") → DS2 global flag (S rows).</summary>
        public readonly Dictionary<string, int> Synthetic = [];
        /// <summary>DS1 map "AAB" → number of temporary-list slots (T + R rows) it uses.</summary>
        public readonly Dictionary<string, int> TempCount = [];
        public readonly List<string> Rows = [];
    }

    static Table? loaded;

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Mappings", FileName);

    public static Table Current => loaded ??= Load(DefaultPath);

    /// <summary>DS1 map "AAB" of an 8-digit DS1 flag (digits 2-4), or null.</summary>
    public static string? Aab(long flag) => flag is >= 10_000_000 and < 100_000_000 ? flag.ToString().Substring(1, 3) : null;

    public static Table Load(string path)
    {
        var t = new Table();
        if (!File.Exists(path)) return t;
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var c = line.Split('\t');
            if (c.Length >= 3 && c[0] == "S" && int.TryParse(c[2], out int sv)) { t.Rows.Add(line); t.Synthetic[c[1]] = sv; continue; }
            if (c.Length < 3 || !int.TryParse(c[1], out int key) || !int.TryParse(c[2], out int v)) continue;
            t.Rows.Add(line);
            switch (c[0])
            {
                case "G": t.Global[key] = v; break;
                case "T": t.Temp[key] = v; Count(t, key, v); break;
                case "R": t.Restart[key] = v; Count(t, key, v); break;
            }
        }
        return t;

        static void Count(Table t, int key, int slot)
        {
            if (Aab(key) is string a) t.TempCount[a] = Math.Max(t.TempCount.GetValueOrDefault(a), slot + 1);
        }
    }

    /// <summary>
    /// Session 10 (asy-items), rule C1: the synthetic persistent flags (S rows). DS1's `4:11 IF Player's Class(c)` reads
    /// the class chosen at character creation; DS2 has no DS1 class and its scripts can't read its own class either (the
    /// complete vanilla EDD command/function list has no class getter; f130304 reads stats only: FUN_140470f00, table
    /// DAT_1410efd70), so the DS1 class lives in one flag per class, set once by the origin prompt
    /// (<see cref="OriginPrompt"/>). Key "ds1-origin:c", c = DS1's class index (EMEDF enum ClassType: 0 Warrior, 1 Knight,
    /// 2 Wanderer, 3 Thief, 4 Bandit, 5 Hunter, 6 Sorcerer, 7 Pyromancer, 8 Cleric, 9 Deprived).
    /// </summary>
    public static readonly (string Key, string What)[] SyntheticKeys = Enumerable.Range(0, OriginPrompt.Classes)
        .Select(c => ($"ds1-origin:{c}", $"synthetic: DS1 class {c} ({OriginPrompt.ClassName(c)}), rule C1 (4:11)"))
        // session 11 (cov11), rule K2 / D2-A: the covenant sub-identity flags of Mappings\covenants.tsv (Princess's Guard)
        .Append(("ds1-covenant:pg", "synthetic: DS1 covenant sub-identity Princess's Guard (shares DS2 Heirs of the Sun with Warrior of Sunlight), rule K2 / D2-A (session 11, cov11)"))
        // session 11 (gest11), rule GS2: a learned DS1 gesture (DS1 keeps them in its own gesture state, set by talk
        // AcquireGesture; DS2's gestures are items, so DS1's are flags a vanilla game ignores): the nine a DS1 talk teaches
        .Concat(Chr.Ds1Gestures.TaughtIndices.Select(i => (Chr.Ds1Gestures.FlagKey(i), $"synthetic: DS1 gesture {9000 + i} learned (talk AcquireGesture({i})), rule GS2 (session 11, gest11)"))).ToArray();

    /// <summary>A temporary DS1 flag (1AAB5000..1AAB5999 of an 8-digit map flag): reset whenever its map loads (rule T1).</summary>
    public static bool IsTemporary(long ds1Flag) => ds1Flag is >= 10_000_000 and < 20_000_000 && ds1Flag % 10000 is >= 5000 and < 6000;

    /// <summary>Free runs of DS2's global blocks: no vanilla reference within <see cref="Guard"/> bits, ≥ <see cref="MinRun"/> bits.</summary>
    public static List<(int First, int Last)> Pool(IReadOnlySet<long> vanillaUsed)
    {
        var runs = new List<(int, int)>();
        foreach (int blk in new[] { Ds2FlagStore.GlobalA, Ds2FlagStore.GlobalB })
        {
            int lo = blk * 10000, hi = lo + 9999;
            var bad = new HashSet<int>();
            foreach (long u in vanillaUsed)
                if (u >= lo - Guard && u <= hi + Guard)
                    for (int d = -Guard; d <= Guard; d++) bad.Add((int)u + d);
            int start = -1;
            for (int f = lo; f <= hi + 1; f++)
            {
                bool free = f <= hi && !bad.Contains(f);
                if (free && start < 0) start = f;
                if (!free && start >= 0) { if (f - start >= MinRun) runs.Add((start, f - 1)); start = -1; }
            }
        }
        return runs;
    }

    /// <summary>
    /// Rows for every census entry, keeping <paramref name="existing"/> (append-only). New G rows take the pool's free bits
    /// in order; new T/R rows the next slot of their DS1 map. Persistent order: DS1 globals, global items, then by flag.
    /// </summary>
    public static (List<string> Rows, List<string> Report) Generate(IEnumerable<Ds1FlagCensus.Use> census, List<(int First, int Last)> pool,
        IReadOnlySet<long> vanillaUsed, Table existing)
    {
        var report = new List<string>();
        var rows = new List<string>(existing.Rows);
        var usedG = existing.Global.Values.Concat(existing.Synthetic.Values).ToHashSet();
        var next = pool.SelectMany(r => Enumerable.Range(r.First, r.Last - r.First + 1)).Where(b => !usedG.Contains(b)).GetEnumerator();
        var uses = census.ToList();
        var restarts = uses.Where(u => u.Source.StartsWith(Ds1FlagCensus.RestartSource)).Select(u => u.Flag).Distinct().Order().ToList();
        var flags = uses.Where(u => !u.Source.StartsWith(Ds1FlagCensus.RestartSource) && u.Flag >= 0).Select(u => u.Flag).Distinct().ToList();
        static int Rank(int f) => f < 10000 ? 0 : f is >= 50_000_000 and < 50_010_000 ? 1 : 2;
        int addedG = 0, addedT = 0, addedR = 0, full = 0;
        foreach (int f in flags.Where(f => !IsTemporary(f)).OrderBy(Rank).ThenBy(f => f))
        {
            if (existing.Global.ContainsKey(f)) continue;
            if (!next.MoveNext()) { full++; continue; }
            rows.Add($"G\t{f}\t{next.Current}\t{Commands.FlagSurvey.Ds1Class(f)}");
            addedG++;
        }
        // session 10 (asy-items), rule C1: the synthetic keys, after the census rows (append-only like them)
        int addedS = 0;
        foreach (var (key, what) in SyntheticKeys)
        {
            if (existing.Synthetic.ContainsKey(key)) continue;
            if (!next.MoveNext()) { full++; continue; }
            rows.Add($"S\t{key}\t{next.Current}\t{what}");
            addedS++;
        }
        if (addedS > 0) report.Add($"S rows +{addedS} (synthetic: {string.Join(", ", SyntheticKeys.Select(k => k.Key))})");
        var count = new Dictionary<string, int>(existing.TempCount);
        foreach (int f in flags.Where(f => IsTemporary(f)).OrderBy(f => f))
        {
            if (existing.Temp.ContainsKey(f)) continue;
            string a = Aab(f)!;
            int s = count.GetValueOrDefault(a);
            count[a] = s + 1;
            rows.Add($"T\t{f}\t{s}\tm{a[..2]}_0{a[2]} temporary");
            addedT++;
        }
        foreach (int k in restarts)
        {
            if (existing.Restart.ContainsKey(k) || Aab(k) is not string a) continue;
            int s = count.GetValueOrDefault(a);
            count[a] = s + 1;
            rows.Add($"R\t{k}\t{s}\tm{a[..2]}_0{a[2]} restart request of event {k}");
            addedR++;
        }
        long poolBits = pool.Sum(r => (long)(r.Last - r.First + 1));
        report.Add($"pool: {pool.Count} runs, {poolBits} bits (guard {Guard}, min run {MinRun}); G rows {existing.Global.Count} kept + {addedG} new = {existing.Global.Count + addedG} ({poolBits - existing.Global.Count - addedG} bits left){(full > 0 ? $", {full} DID NOT FIT" : "")}");
        report.Add($"T rows +{addedT}, R rows +{addedR}; temporary list sizes per DS1 map: " + string.Join(", ", count.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}{(k.Value > 2 * Ds2FlagStore.MapBlockFlags ? " OVER 400" : "")}")));
        var clash = rows.Where(r => r.StartsWith("G\t")).Select(r => long.Parse(r.Split('\t')[2])).Where(vanillaUsed.Contains).ToList();
        report.Add($"G rows on a vanilla-referenced DS2 flag: {clash.Count}");
        var poolBitsSet = pool.SelectMany(r => Enumerable.Range(r.First, r.Last - r.First + 1)).ToHashSet();
        var outside = rows.Where(r => r.StartsWith("G\t")).Select(r => int.Parse(r.Split('\t')[2])).Where(v => !poolBitsSet.Contains(v)).ToList();
        report.Add($"G rows outside today's pool (within {Guard} of a vanilla reference found since): {outside.Count}{(outside.Count > 0 ? " e.g. " + string.Join(", ", outside.Take(8)) : "")}");
        return (rows, report);
    }

    public static void Write(string path, List<string> rows, IEnumerable<string> header)
    {
        var sb = new StringBuilder();
        foreach (var h in header) sb.Append("# ").Append(h).Append('\n');
        foreach (var r in rows) sb.Append(r).Append('\n');
        File.WriteAllText(path, sb.ToString());
    }
}
