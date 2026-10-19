using SoulsFormats;
using Lordran.Tools.Build.Events;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 9 (track npcflags): event-flag storage censuses and the offline proof that every flag a pack references has
/// DS2 storage (<see cref="Ds2FlagStore"/> = DS2's own lookup, read from the executable).
///   flag-survey ds1 [--list FILE]          every DS1 flag the DSR data references (<see cref="Ds1FlagCensus"/>), by class and map,
///                                          and where <see cref="FlagRules"/> puts each class (0 unmapped / unbacked expected)
///   flag-survey vanilla [--list FILE]      every flag vanilla DS2 references (event + talk ESDs through their machine parameters,
///                                          per-map GeneratorParam, regulation flag fields) by DS2 block: the evidence for the backed ranges
///   flag-check PACKDIR [--list FILE]       every flag the pack references (its event/talk ESDs, per-map params, regulation rows that
///                                          differ from vanilla) looked up as DS2 does with the script's own map loaded; exit 1 on any
///                                          unbacked id, on a DS1 flag landing on a vanilla-used global id, or on a temporary-flag slot
///                                          that isn't reset at load
/// </summary>
public static class FlagSurvey
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: flag-survey ds1|vanilla [--list FILE] | flag-check PACKDIR [--list FILE]"); return 1; }
        string? list = args.SkipWhile(a => a != "--list").Skip(1).FirstOrDefault();
        string? table = args.SkipWhile(a => a != "--write-table").Skip(1).FirstOrDefault();
        return args[1] switch
        {
            "ds1" => Ds1(list, table),
            "vanilla" => Vanilla(list),
            "regdiff" when args.Length >= 4 => RegDiff(args[2], args[3]),
            _ => Fail($"unknown mode {args[1]}"),
        };
    }

    static int Fail(string m) { Console.Error.WriteLine("flag-survey: " + m); return 1; }

    /// <summary>
    /// flag-check PACKDIR [--list FILE]: every flag the pack references, looked up as DS2 does (<see cref="Ds2FlagStore"/>)
    /// with the referencing script's own map loaded (per-map params: their map; regulation rows the pack adds or changes:
    /// storage in any of the pack's maps). Fails on: an id without storage; a global id vanilla DS2 references (vanilla
    /// progress would read as ours); a temporary (reset-block) flag a map's scripts use that its event 0 doesn't clear at
    /// load (rule T1); the same DS2 id used for two different DS1 flags (slot table); (fix pass) a temporary flag that is
    /// also a flag of a vanilla map with the same prefix (blocks shared while both are among the last 3 loaded maps); a
    /// temporary flag in k = 2 (zeroed at a rest too). Reports the references DS2 doesn't sync online (global blocks).
    /// </summary>
    public static int Check(string[] args)
    {
        if (args.Length < 2 || !Directory.Exists(args[1])) return Fail("usage: flag-check PACKDIR [--list FILE]");
        string pack = args[1];
        string? list = args.SkipWhile(a => a != "--list").Skip(1).FirstOrDefault();
        var reg = Formats.ParamStore.LoadDs2(Path.Combine(pack, "title"), ExtractDs2.DefaultOut);
        var vanillaStore = Formats.ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        var flagList = FlagList(reg);
        var refs = new List<Ref>();
        var maps = new List<string>();
        var resets = new Dictionary<string, HashSet<long>>();
        foreach (string f in Directory.GetFiles(Path.Combine(pack, "gamedata", "ezstate"), "*.esd").Order())
        {
            string name = Path.GetFileName(f), map = MapOf(f);
            if (!name.StartsWith("event_") && !name.StartsWith("talk_")) continue;
            var esd = ESD.Read(f);
            if (map != "" && !maps.Contains(map)) maps.Add(map);
            refs.AddRange(EsdRefs(esd, map, name, flagList));
            if (name.StartsWith("event_")) resets[map] = LoadResetFlags(esd);
        }
        refs.AddRange(GeneratorRefs(Path.Combine(pack, "gamedata", "param")));
        // regulation rows the pack adds or changes (flag fields only)
        var vanillaRows = RegulationRefs(vanillaStore).Select(r => (r.Source, r.Flag)).ToHashSet();
        refs.AddRange(RegulationRefs(reg).Where(r => !vanillaRows.Contains((r.Source, r.Flag))));
        var vanillaRefs = VanillaRefs(out _);
        var vanillaGlobal = VanillaGlobalUse(vanillaRefs);
        // fix pass: vanilla maps sharing a pack map's prefix share its blocks while both are among the last 3 loaded maps
        // (Ds2FlagStore): their own flags (any k) must not be ours
        var alias = maps.ToDictionary(m => m, m => Ds2FlagStore.SamePrefixVanilla(m, Path.Combine(ExtractDs2.DefaultOut, "map")).ToList());
        var aliasFlags = maps.ToDictionary(m => m, m => vanillaRefs.Where(v => v.Flag > 0 && alias[m].Contains(v.Map)).GroupBy(v => v.Flag).ToDictionary(g => g.Key, g => g.First().Source));

        int unbacked = 0, clash = 0, unreset = 0, unresolved = 0, aliased = 0, unsynced = 0, restReset = 0;
        var byKind = new Dictionary<string, int>();
        var lines = new List<string>();
        var problems = new List<string>();
        var notes = new List<string>();
        foreach (var r in refs)
        {
            if (r.Flag < 0)
            {
                // 130131 over EventFlagListParam rows that don't exist sets nothing (the per-bonfire group copied from
                // Things Betwixt names our map's empty range AABB000..+99): a no-op, reported, not a failure
                if (r.Source.Contains(": none")) { notes.Add($"no-op 130131 (no EventFlagListParam rows): {r.Source}"); continue; }
                unresolved++; problems.Add($"UNRESOLVED 130131 rows: {r.Source}"); continue;
            }
            var loaded = r.Map != "" ? new[] { r.Map } : maps.ToArray();
            var kind = Ds2FlagStore.Lookup(r.Flag, loaded);
            string where = r.Map != "" ? r.Map : "any pack map";
            byKind[kind.ToString()] = byKind.GetValueOrDefault(kind.ToString()) + 1;
            lines.Add($"{r.Flag}\t{kind}\t{where}\t{r.Source}");
            if (kind == Ds2FlagStore.Kind.None) { unbacked++; problems.Add($"UNBACKED {r.Flag} ({where}): {r.Source}"); }
            if (kind == Ds2FlagStore.Kind.Global && vanillaGlobal.Contains(r.Flag)) { clash++; problems.Add($"VANILLA GLOBAL {r.Flag}: {r.Source}"); }
            if (kind is Ds2FlagStore.Kind.MapReset or Ds2FlagStore.Kind.MapSlot && r.Map != "" && FlagRules.HostOf(r.Flag) == r.Map && !(resets.GetValueOrDefault(r.Map)?.Contains(r.Flag) ?? false))
            { unreset++; problems.Add($"NOT RESET AT LOAD {r.Flag} ({r.Map}): {r.Source}"); }
            if (kind is not (Ds2FlagStore.Kind.None or Ds2FlagStore.Kind.Global))
                foreach (string m in loaded)
                    if (aliasFlags.TryGetValue(m, out var af) && af.TryGetValue(r.Flag, out var vs))
                    { aliased++; problems.Add($"SAME-PREFIX VANILLA FLAG {r.Flag} ({m} shares blocks with {string.Join("/", alias[m])}: {vs}): {r.Source}"); }
            if (kind != Ds2FlagStore.Kind.None && !Ds2FlagStore.Syncs(r.Flag)) unsynced++;
            // fix pass: a DS1 temporary flag in k = 2 (F2 overflow past 200) is also zeroed by DS2's rest: a lifetime DS1's
            // temporary flags may not have (U) — fails until a rule for lists over 200 is decided (m12_01, m13_00)
            // session 11 (burg11, round 9), rule F2b: a merged host's (rule H2) temporaries overflow into k2 (Depths +
            // Blighttown + Demon Ruins: 252); DS1 resets them at every map load, and DS1's rest IS a map load, so DS2's rest
            // reset is DS1's lifetime: accepted below the boss logic range (k2 bits 0..79, FlagRules.TempId) when event 0
            // clears it at load
            bool f2b = Build.Maps.H2Merge.Active && r.Flag % 10000 < 80 && (resets.GetValueOrDefault(r.Map)?.Contains(r.Flag) ?? false);
            if (kind == Ds2FlagStore.Kind.MapReset && r.Map != "" && FlagRules.HostOf(r.Flag) == r.Map && !FlagRules.IsBossLogicFlag(r.Flag) && !FlagRules.IsAiChannelFlag(r.Flag) && !f2b) // merge11: DS2's boss logic flags live in k2 (vanilla); fx11: E-REQ-P channels too
            { restReset++; problems.Add($"TEMPORARY FLAG IN THE REST-RESET BLOCK k2 {r.Flag} ({r.Map}): {r.Source}"); }
        }
        // the slot table maps distinct DS1 flags to distinct DS2 ids
        var t = FlagSlots.Current;
        int dupG = t.Global.Values.Concat(t.Synthetic.Values).GroupBy(v => v).Count(g => g.Count() > 1); // session 10: + S rows (rule C1)
        var tempIds = t.Temp.Keys.Select(FlagRules.Any).Concat(t.Restart.Keys.Select(k => FlagRules.Restart($"m{k.ToString().Substring(1, 2)}_0{k.ToString()[3]}", k)))
            .Where(v => v != null).GroupBy(v => v).Count(g => g.Count() > 1);
        Console.WriteLine($"flag-check {pack}: maps {string.Join(", ", maps)}; {refs.Count} references, {refs.Where(r => r.Flag > 0).Select(r => r.Flag).Distinct().Count()} distinct flags");
        Console.WriteLine("  by DS2 storage: " + string.Join(", ", byKind.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}")));
        foreach (var (m, set) in resets) Console.WriteLine($"  {m}: event 0 clears {set.Count} flags at load");
        Console.WriteLine($"  slot table: {t.Rows.Count} rows, DS2 global ids used twice {dupG}, temporary ids used twice {tempIds}");
        foreach (var (m, vs) in alias.Where(a => a.Value.Count > 0))
            Console.WriteLine($"  note: {m} has prefix {Ds2FlagStore.Prefix(m)} like vanilla {string.Join(", ", vs)}: while both are among the last 3 loaded maps they share blocks "
                + $"{Ds2FlagStore.Prefix(m) * 100}..{Ds2FlagStore.Prefix(m) * 100 + 2} (k0 too: the vanilla map's progress would read the other's registry block); {aliasFlags[m].Count} vanilla flags checked against ours");
        var pf = refs.Where(r => r.Flag > 0).Select(r => r.Flag).Distinct().ToList();
        Console.WriteLine($"  online (co-op): {unsynced} of {refs.Count(r => r.Flag > 0)} references ({pf.Count(f => !Ds2FlagStore.Syncs(f))} of {pf.Count} flags) are in blocks DS2 keeps per player "
            + "(FUN_14025cdb0: a guest's write dropped, not sent to guests); vanilla keeps world progress in synced map blocks (U-coop)");
        foreach (var n in notes.Distinct()) Console.WriteLine("  note: " + n);
        foreach (var p in problems.Distinct().Take(60)) Console.WriteLine("  " + p);
        Console.WriteLine($"  unbacked {unbacked}, vanilla-global clashes {clash}, temporary not reset at load {unreset}, unresolved 130131 {unresolved}, same-prefix vanilla flags {aliased}, temporary flags in k2 {restReset}");
        if (list != null) File.WriteAllLines(list, lines.Distinct().Order());
        return unbacked + clash + unreset + unresolved + dupG + tempIds + aliased + restReset == 0 ? 0 : 1;
    }

    /// <summary>flag-survey regdiff PACK_A PACK_B: regulation rows added / removed / changed (field names) between two
    /// packs: shows that a flag-rule change touches only flag fields.</summary>
    static int RegDiff(string a, string b)
    {
        var pa = Formats.ParamStore.LoadDs2(Path.Combine(a, "title"), ExtractDs2.DefaultOut);
        var pb = Formats.ParamStore.LoadDs2(Path.Combine(b, "title"), ExtractDs2.DefaultOut);
        int changed = 0;
        foreach (var (name, p) in pa.Params.OrderBy(k => k.Key))
        {
            if (!pb.Params.TryGetValue(name, out var q)) { Console.WriteLine($"{name}: only in A"); continue; }
            var rb = q.Rows.GroupBy(r => r.ID).ToDictionary(g => g.Key, g => g.First());
            var ra = p.Rows.GroupBy(r => r.ID).ToDictionary(g => g.Key, g => g.First());
            foreach (var id in ra.Keys.Except(rb.Keys)) { Console.WriteLine($"{name} {id}: only in A"); changed++; }
            foreach (var id in rb.Keys.Except(ra.Keys)) { Console.WriteLine($"{name} {id}: only in B"); changed++; }
            foreach (var (id, row) in ra)
            {
                if (!rb.TryGetValue(id, out var other)) continue;
                var fields = row.Cells.Zip(other.Cells).Where(c => !Equals(Norm(c.First.Value), Norm(c.Second.Value)))
                    .Select(c => $"{c.First.Def.InternalName} {Norm(c.First.Value)} -> {Norm(c.Second.Value)}").ToList();
                if (fields.Count > 0) { Console.WriteLine($"{name} {id}: {string.Join("; ", fields)}"); changed++; }
            }
        }
        Console.WriteLine($"{changed} rows differ");
        return 0;
        static object Norm(object v) => v is byte[] x ? Convert.ToHexString(x) : v;
    }

    /// <summary>The flags an event ESD clears at load: 130101(f, 0) in rule T1's state 8997 and the states it chains to
    /// unconditionally (the entry-command cap splits a long list).</summary>
    static HashSet<long> LoadResetFlags(ESD esd)
    {
        var set = new HashSet<long>();
        foreach (var (_, states) in esd.StateGroups)
        {
            if (!states.TryGetValue(8997, out var st)) continue;
            var seen = new HashSet<long>();
            long id = 8997;
            while (st != null && seen.Add(id))
            {
                var clears = st.EntryCommands.Where(c => c.CommandBank == 1 && c.CommandID == 130101 && c.Arguments.Count >= 2
                    && EsdCalls.Parse(c.Arguments[1]) is EsdCalls.Const { V: 0 } && EsdCalls.Parse(c.Arguments[0]) is EsdCalls.Const).ToList();
                if (clears.Count == 0) break;
                foreach (var c in clears) set.Add(((EsdCalls.Const)EsdCalls.Parse(c.Arguments[0])!).V);
                if (st.Conditions.Count != 1 || st.Conditions[0].TargetState is not long next || !states.TryGetValue(next, out st)) break;
                id = next;
            }
        }
        return set;
    }

    // ------------------------------------------------------------------ DS1

    public static string Ds1Class(int f)
    {
        if (f is >= 0 and < 10000) return "global 0-9999";
        if (f is >= 50_000_000 and < 50_010_000) return "global item 5000nnnn";
        string s = f.ToString();
        if (s.Length != 8) return $"other ({s.Length} digits)";
        int n = f % 10000;
        return s[0] switch
        {
            '1' => n is >= 5000 and < 6000 ? "map temp 1AAB5nnn" : "map 1AABnnnn",
            '5' => "map item 5AABnnnn",
            '6' => "map objact 6AABnnnn",
            '7' => "map talk 7AABnnnn",
            _ => $"other ({s[0]}xxxxxxx)",
        };
    }

    /// <summary>DS2 engine constants in the global flag ranges: 32-bit immediates of mov r32 / push / cmp eax / ALU r32 and
    /// lea displacements in the code sections of the unpacked executable (an over-approximation: kept out of the pool).</summary>
    public static HashSet<long> EngineConstants(string exe)
    {
        var d = File.ReadAllBytes(exe);
        int pe = BitConverter.ToInt32(d, 0x3c), n = BitConverter.ToUInt16(d, pe + 6), opt = BitConverter.ToUInt16(d, pe + 20);
        var set = new HashSet<long>();
        static bool InRange(uint v) => v is >= 100000 and <= 109999 or >= 200000 and <= 209999;
        for (int s = 0; s < n; s++)
        {
            int o = pe + 24 + opt + s * 40;
            if (System.Text.Encoding.ASCII.GetString(d, o, 8).TrimEnd('\0') != ".text") continue;
            int raw = BitConverter.ToInt32(d, o + 20), size = BitConverter.ToInt32(d, o + 16);
            for (int i = raw; i < raw + size - 7; i++)
            {
                byte b = d[i];
                uint v;
                if (b is >= 0xB8 and <= 0xBF or 0x68 or 0x3D) v = BitConverter.ToUInt32(d, i + 1);
                else if (b == 0x81 && (d[i + 1] & 0xC0) == 0xC0) v = BitConverter.ToUInt32(d, i + 2);
                else if (b == 0x8D && (d[i + 1] & 0xC0) == 0x80 && (d[i + 1] & 7) != 4) v = BitConverter.ToUInt32(d, i + 2);
                else continue;
                if (InRange(v)) set.Add(v);
            }
        }
        return set;
    }

    public const string Ds2Exe = @"C:\EchoesOfLordran\bin\ds2s\DarkSoulsII.unpacked.exe";

    /// <summary>Vanilla DS2's referenced global flags (scripts, params, engine constants): kept out of the slot pool.</summary>
    public static HashSet<long> VanillaGlobalUse(List<Ref>? vanillaRefs = null)
    {
        var used = (vanillaRefs ?? VanillaRefs(out _)).Where(r => r.Flag > 0).Select(r => r.Flag).ToHashSet();
        used.UnionWith(EngineConstants(Ds2Exe));
        return used.Where(f => f / 10000 is Ds2FlagStore.GlobalA or Ds2FlagStore.GlobalB).ToHashSet();
    }

    static int Ds1(string? list, string? writeTable)
    {
        var uses = Ds1FlagCensus.Collect(Program.DefaultDsrRoot, w => Console.Error.WriteLine("warn: " + w));
        if (writeTable != null)
        {
            var vanilla = VanillaGlobalUse();
            var pool = FlagSlots.Pool(vanilla);
            var existing = FlagSlots.Load(writeTable);
            var (rows, report) = FlagSlots.Generate(uses, pool, vanilla, existing);
            var header = new List<string>
            {
                "DS1 -> DS2 flag slots (session 9, track npcflags; Build/Events/FlagSlots.cs, FlagRules F1-F3). APPEND-ONLY: never",
                "renumber a row (saves keep their meaning). Regenerate with: lr flag-survey ds1 --write-table <this file>",
                "G <DS1 flag> <DS2 global flag (blocks 10/20)> | T <DS1 temporary flag> <slot> | R <DS1 event id+slot> <slot> | S <synthetic key> <DS2 global flag>",
                $"pool runs (vanilla scripts/params/engine constants +-{FlagSlots.Guard}, runs >= {FlagSlots.MinRun}): " + string.Join(" ", pool.Select(r => $"{r.First}-{r.Last}")),
            };
            FlagSlots.Write(writeTable, rows, header);
            foreach (var r in report) Console.WriteLine(r);
            Console.WriteLine($"wrote {writeTable}: {rows.Count} rows");
        }
        var flags = uses.GroupBy(u => u.Flag).ToDictionary(g => g.Key, g => g.Select(u => u.Source).Distinct().ToList());
        Console.WriteLine($"DS1 flags referenced: {flags.Count} distinct ({uses.Count} uses)");
        foreach (var cls in flags.Keys.GroupBy(Ds1Class).OrderBy(g => g.Key))
        {
            Console.WriteLine($"  {cls.Key,-26} {cls.Count(),6}  min {cls.Min()} max {cls.Max()}");
            if (cls.Key.StartsWith("map"))
                foreach (var m in cls.GroupBy(f => f.ToString().Substring(1, 3)).OrderBy(g => g.Key))
                    Console.WriteLine($"      AAB {m.Key}: {m.Count(),5}  offsets {m.Min() % 10000}..{m.Max() % 10000}  by thousand: " +
                        string.Join(" ", m.GroupBy(f => f % 10000 / 1000).OrderBy(g => g.Key).Select(g => $"{g.Key}xxx:{g.Count()}")));
            if (cls.Key.StartsWith("global 0"))
                Console.WriteLine("      by thousand: " + string.Join(" ", cls.GroupBy(f => f / 1000).OrderBy(g => g.Key).Select(g => $"{g.Key}xxx:{g.Count()}")));
        }
        if (list != null)
            File.WriteAllLines(list, flags.OrderBy(k => k.Key).Select(k => $"{k.Key}\t{Ds1Class(k.Key)}\t{string.Join("; ", k.Value.Take(6))}"));
        if (writeTable != null) return 0; // the build's copy of the table is stale until the next build
        // where FlagRules puts each class (the table in the build output)
        Console.WriteLine($"FlagRules (table {FlagSlots.DefaultPath}: {FlagSlots.Current.Rows.Count} rows):");
        int bad = 0;
        foreach (var cls in flags.Keys.Where(f => f >= 0).GroupBy(Ds1Class).OrderBy(g => g.Key))
        {
            var kinds = cls.Select(f => FlagRules.Any(f) is int d ? (FlagRules.HostOf(d) is string h ? Ds2FlagStore.Lookup(d, [h]) : Ds2FlagStore.Lookup(d, [])) : (Ds2FlagStore.Kind?)null).ToList();
            int none = kinds.Count(k => k == null), unbacked = kinds.Count(k => k == Ds2FlagStore.Kind.None);
            var unhosted = cls.Where(f => FlagRules.Any(f) == null && FlagSlots.IsTemporary(f) && !FlagRules.Host.ContainsKey($"m{f.ToString().Substring(1, 2)}_0{f.ToString()[3]}")).Count();
            Console.WriteLine($"  {cls.Key,-26} {cls.Count(),6}: " + string.Join(", ", kinds.Where(k => k != null).GroupBy(k => k!.Value).Select(g => $"{g.Key} {g.Count()}"))
                + $"; no rule {none} ({unhosted} temporary of unported maps){(unbacked > 0 ? $"; UNBACKED {unbacked}" : "")}");
            bad += unbacked + none - unhosted;
        }
        Console.WriteLine(bad == 0 ? "every DS1 flag of a ported map or global class has DS2 storage" : $"{bad} DS1 flags without DS2 storage");
        // fix pass: DS1 event values (consecutive bits of one value instruction) whose bits are not consecutive in DS2:
        // rule F1 translates event values bit by bit, never as base + n
        var gt = FlagSlots.Current.Global;
        var straddle = uses.Where(u => u.Source.EndsWith(" value")).GroupBy(u => u.Source)
            .SelectMany(grp =>
            {
                var fs = grp.Select(u => u.Flag).Distinct().Order().ToList();
                return fs.Zip(fs.Skip(1)).Where(p => p.Second == p.First + 1 && gt.TryGetValue(p.First, out int a) && gt.TryGetValue(p.Second, out int b) && b != a + 1)
                    .Select(p => (Pair: (p.First, p.Second), Src: grp.Key));
            })
            .GroupBy(x => x.Pair).Select(x => x.First()).OrderBy(x => x.Pair).ToList();
        Console.WriteLine($"event values: {straddle.Count} consecutive DS1 value bits land on non-consecutive DS2 bits (rule F1: translate event values bit by bit)");
        foreach (var (pair, src) in straddle) Console.WriteLine($"    {pair.First}/{pair.Second} -> {gt[pair.First]}/{gt[pair.Second]}  ({src})");
        return bad == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ DS2 scripts

    /// <summary>A flag reference found in a DS2 file: flag id, file/map, how (f6, 130101, 140301, machine arg, param field).</summary>
    public sealed record Ref(long Flag, string Map, string Source);

    /// <summary>Flag references of one DS2 ESD (event or talk): direct constants and constants passed to the file's own
    /// machines (parameter positions that flow into a flag argument, fixpoint over the machines). 130131 rows are resolved
    /// through <paramref name="flagList"/> (EventFlagListParam row id → flag).</summary>
    public static List<Ref> EsdRefs(ESD esd, string map, string file, IReadOnlyDictionary<int, int>? flagList)
    {
        var refs = new List<Ref>();
        // machine id → set of parameter indices used as flags (and as 130131 row bounds)
        var flagParams = new Dictionary<long, HashSet<int>>();
        var rowParams = new Dictionary<long, HashSet<int>>();
        bool changed = true;
        int guard = 0;
        while (changed && guard++ < 20)
        {
            changed = false;
            foreach (var (gid, states) in esd.StateGroups)
                foreach (var (_, s) in states)
                    foreach (var (fn, args, isCmd) in Uses(s))
                        foreach (var (pos, row) in FlagPositions(fn, isCmd, flagParams, rowParams))
                            if (pos < args.Length && args[pos] is EsdCalls.Param p)
                            {
                                var d = row ? rowParams : flagParams;
                                if (!d.TryGetValue(gid, out var set)) d[gid] = set = [];
                                if (set.Add(p.I)) changed = true;
                            }
        }
        foreach (var (gid, states) in esd.StateGroups)
            foreach (var (sid, s) in states)
                foreach (var (fn, args, isCmd) in Uses(s))
                    foreach (var (pos, row) in FlagPositions(fn, isCmd, flagParams, rowParams))
                    {
                        if (pos >= args.Length || args[pos] is not EsdCalls.Const c) continue;
                        string src = $"{file} g{gid} s{sid} {(isCmd ? "" : "f")}{fn}[{pos}]";
                        if (!row) { if (c.V > 0) refs.Add(new(c.V, map, src)); continue; }
                        // 130131(first, last, v) / a machine forwarding them: rows of EventFlagListParam
                        int other = fn == 130131 ? (pos == 0 ? 1 : -2) : -1;
                        if (fn < 0 && rowParams.TryGetValue(-fn, out var rp) && rp.Count == 2) other = pos == rp.Min() ? rp.Max() : -2;
                        if (other == -2) continue; // the range's upper bound: resolved with its lower one
                        if (other < 0 || flagList == null) { refs.Add(new(-c.V, map, src + " (EventFlagListParam row)")); continue; }
                        if (args[other] is EsdCalls.Const hi)
                        {
                            var rows = flagList.Where(r => r.Key >= c.V && r.Key <= hi.V).ToList();
                            if (rows.Count == 0) refs.Add(new(-c.V, map, src + $" 130131 rows {c.V}..{hi.V}: none"));
                            foreach (var r in rows) refs.Add(new(r.Value, map, src + $" 130131 row {r.Key}"));
                        }
                    }
        return refs;
    }

    /// <summary>(function or command id, argument nodes, is command) for every call in a state.</summary>
    static IEnumerable<(long Fn, EsdCalls.Node[] Args, bool IsCmd)> Uses(ESD.State s)
    {
        IEnumerable<ESD.Condition> Flat(IEnumerable<ESD.Condition> cs) => cs.SelectMany(c => new[] { c }.Concat(Flat(c.Subconditions)));
        var cmds = s.EntryCommands.Concat(s.WhileCommands).Concat(s.ExitCommands).Concat(Flat(s.Conditions).SelectMany(c => c.PassCommands));
        foreach (var c in cmds)
        {
            var a = c.Arguments.Select(x => EsdCalls.Parse(x) ?? new EsdCalls.Other([])).ToArray();
            // bank 1 = engine commands; bank 6 = machine calls (id = the machine's group id)
            yield return (c.CommandBank == 6 ? -c.CommandID : c.CommandID, a, true);
            foreach (var arg in c.Arguments)
                foreach (var call in EsdCalls.Calls(arg)) yield return (call.Fn, call.Args, false);
        }
        foreach (var cond in Flat(s.Conditions))
            foreach (var call in EsdCalls.Calls(cond.Evaluator)) yield return (call.Fn, call.Args, false);
    }

    /// <summary>Argument positions holding a flag (row = an EventFlagListParam row bound instead). Machine calls carry
    /// their id negated (bank 6).</summary>
    static IEnumerable<(int Pos, bool Row)> FlagPositions(long fn, bool isCmd, Dictionary<long, HashSet<int>> flagParams, Dictionary<long, HashSet<int>> rowParams)
    {
        if (!isCmd) { if (fn == 6) yield return (0, false); yield break; }
        switch (fn)
        {
            case 130101: yield return (0, false); break;
            case 140301: yield return (1, false); break;
            case 130131: yield return (0, true); yield return (1, true); break;
        }
        if (fn < 0)
        {
            long machine = -fn;
            // machine ids are stored as unsigned group ids in the file (2147483xxx)
            foreach (var key in new[] { machine, (long)(uint)machine })
            {
                if (flagParams.TryGetValue(key, out var fp)) foreach (int p in fp) yield return (p, false);
                if (rowParams.TryGetValue(key, out var rp)) foreach (int p in rp) yield return (p, true);
            }
        }
    }

    // ------------------------------------------------------------------ vanilla DS2

    static readonly (string Param, string[] Fields)[] RegulationFlagFields =
    [
        ("EventFlagListParam", ["flagId"]), ("BossParam", ["part1EventFlag", "part2EventFlag", "part3EventFlag", "part4EventFlag"]),
        ("ShopLineupParam", ["saleFlag", "saleEndFlag"]), ("MapObjectWhiteDoorParam", ["passFlagId", "enableKeyGuideFlagId"]),
        ("NpcInfoParam", ["deadFlagId"]), ("NpcNameSwapParam", ["changeNameFlagId"]), ("LoadingDisplayItemParam", ["globalFlagId"]),
        ("MapObjectBonfireParam", ["rootFlagId"]), ("ItemUsageParam", ["eventFlag"]), ("PlurslSelectMenuParam", ["checkEventFlagId"]),
        ("BossBattleParam", ["killedEventId", "onceKilledEventId"]),
    ];
    /// <summary>GeneratorParam (GENERATOR_PARAM, read raw: some files fit no paramdef): respawnDisableFlag +0x04,
    /// logicEventFlag1-4 +0x5C..+0x68, and the start/stop condition parameters (+0x10/+0x20, 3 × s32) whose condition
    /// byte (+0x0D/+0x1D, 3 × u8) is a flag test (<see cref="FlagConditions"/>).</summary>
    static readonly (string Name, int Off)[] GeneratorFlagFields = [("respawnDisableFlag", 0x04), ("logicEventFlag1", 0x5C), ("logicEventFlag2", 0x60), ("logicEventFlag3", 0x64), ("logicEventFlag4", 0x68)];
    /// <summary>Generator start/stop condition codes that test an event flag (vanilla NPC generators: 8 = flag off with
    /// the dead flag, Saulden's 104140; 7 = flag on).</summary>
    public static readonly HashSet<int> FlagConditions = [7, 8];

    public static Dictionary<int, int> FlagList(Formats.ParamStore ps) =>
        ps.Params.TryGetValue("EventFlagListParam", out var p)
            ? p.Rows.ToDictionary(r => r.ID, r => Convert.ToInt32(r.Cells.First(c => c.Def.InternalName == "flagId").Value)) : [];

    public static List<Ref> RegulationRefs(Formats.ParamStore ps, Func<string, int, bool>? include = null)
    {
        var refs = new List<Ref>();
        foreach (var (param, fields) in RegulationFlagFields)
        {
            if (!ps.Params.TryGetValue(param, out var p)) continue;
            foreach (var row in p.Rows)
            {
                if (include != null && !include(param, row.ID)) continue;
                foreach (var cell in row.Cells.Where(c => fields.Contains(c.Def.InternalName)))
                    if (Convert.ToInt64(cell.Value) is > 0 and var v) refs.Add(new(v, "", $"{param} {row.ID}.{cell.Def.InternalName}"));
            }
        }
        return refs;
    }

    public static List<Ref> GeneratorRefs(string paramDir, Dictionary<int, int>? conditionCodes = null)
    {
        var refs = new List<Ref>();
        foreach (string f in Directory.GetFiles(paramDir, "generatorparam_m*.param"))
        {
            string map = Path.GetFileNameWithoutExtension(f)["generatorparam_".Length..];
            if (Formats.ParamFiles.Rowless(f)) continue; // session 11 (burg11): a map without characters
            var p = PARAM.Read(f);
            var blob = new PARAMDEF { ParamType = p.ParamType, DataVersion = p.ParamdefDataVersion, Unicode = true, FormatVersion = 104, BigEndian = false };
            blob.Fields.Add(new PARAMDEF.Field(blob, PARAMDEF.DefType.dummy8, "data") { ArrayLength = (int)p.DetectedSize });
            p.ApplyParamdef(blob);
            foreach (var row in p.Rows)
            {
                var b = (byte[])row["data"].Value;
                if (b.Length < 0x6C) continue;
                foreach (var (name, off) in GeneratorFlagFields)
                    if (BitConverter.ToUInt32(b, off) is > 0 and < uint.MaxValue and var v) refs.Add(new(v, map, $"generatorparam_{map} {row.ID}.{name}"));
                foreach (var (what, cond, par) in new[] { ("start", 0x0D, 0x10), ("stop", 0x1D, 0x20) })
                    for (int k = 0; k < 3; k++)
                    {
                        int code = b[cond + k], v = BitConverter.ToInt32(b, par + 4 * k);
                        if (conditionCodes != null && code != 0) conditionCodes[code] = conditionCodes.GetValueOrDefault(code) + 1;
                        if (FlagConditions.Contains(code) && v > 0) refs.Add(new(v, map, $"generatorparam_{map} {row.ID}.{what}Condition{k + 1} {code}"));
                    }
            }
        }
        return refs;
    }

    static string MapOf(string file)
    {
        var m = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(file), @"m\d\d_\d\d_\d\d_\d\d");
        return m.Success ? m.Value : "";
    }

    public static List<Ref> VanillaRefs(out Formats.ParamStore ps)
    {
        ps = Formats.ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        var flagList = FlagList(ps);
        var refs = new List<Ref>();
        foreach (string f in Directory.GetFiles(Path.Combine(ExtractDs2.DefaultOut, "ezstate"), "*.esd").Order())
        {
            string name = Path.GetFileName(f);
            if (!name.StartsWith("event_") && !name.StartsWith("talk_")) continue;
            ESD esd;
            try { esd = ESD.Read(f); } catch { continue; }
            refs.AddRange(EsdRefs(esd, MapOf(f), name, flagList));
        }
        refs.AddRange(GeneratorRefs(Path.Combine(ExtractDs2.DefaultOut, "param")));
        refs.AddRange(RegulationRefs(ps));
        return refs;
    }

    static int Vanilla(string? list)
    {
        var refs = VanillaRefs(out _);
        var flags = refs.Where(r => r.Flag > 0).GroupBy(r => r.Flag).ToDictionary(g => g.Key, g => g.ToList());
        Console.WriteLine($"vanilla DS2 flag references: {refs.Count} ({flags.Count} distinct flags); unresolved 130131 rows: {refs.Count(r => r.Flag < 0)}");
        var codes = new Dictionary<int, int>();
        var genRefs = GeneratorRefs(Path.Combine(ExtractDs2.DefaultOut, "param"), codes);
        Console.WriteLine("generator start/stop condition codes (count): " + string.Join(" ", codes.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"))
            + $"; flag-test codes {string.Join(",", FlagConditions)}: {genRefs.Count(r => r.Source.Contains("Condition"))} params, by block "
            + string.Join(" ", genRefs.Where(r => r.Source.Contains("Condition")).GroupBy(r => r.Flag / 10000).OrderBy(g => g.Key).Take(30).Select(g => $"{g.Key}:{g.Count()}")));
        // by block class
        var maps = Directory.GetFiles(Path.Combine(ExtractDs2.DefaultOut, "ezstate"), "event_*.esd").Select(MapOf).Where(m => m != "").Distinct().ToList();
        Console.WriteLine("by block (flag / 10000):");
        foreach (var g in flags.Keys.GroupBy(f => f / 10000).OrderBy(g => g.Key))
        {
            long b = g.Key;
            string cls = b is Ds2FlagStore.GlobalA or Ds2FlagStore.GlobalB ? "GLOBAL" : b >= 100 && b % 100 <= 2 ? $"map prefix {b / 100} k={b % 100}" : "no block";
            long over = g.Count(f => f % 10000 >= Ds2FlagStore.MapBlockFlags);
            Console.WriteLine($"  block {b,6}  {cls,-22} {g.Count(),5} flags  bits {g.Min() % 10000}..{g.Max() % 10000}" +
                (cls.StartsWith("map") && over > 0 ? $"  ({over} with bit >= 200)" : ""));
        }
        // own-map lookup over every script reference
        int ownOk = 0, ownBad = 0;
        var bad = new List<string>();
        foreach (var r in refs.Where(r => r.Flag > 0 && r.Map != ""))
        {
            var k = Ds2FlagStore.Lookup(r.Flag, [r.Map]);
            if (k != Ds2FlagStore.Kind.None) ownOk++;
            else { ownBad++; if (bad.Count < 40) bad.Add($"{r.Flag} {r.Source}"); }
        }
        Console.WriteLine($"lookup with the script's own map loaded: {ownOk} backed, {ownBad} not (cross-map or unbacked):");
        foreach (var b in bad) Console.WriteLine("    " + b);
        foreach (int g in new[] { Ds2FlagStore.GlobalA, Ds2FlagStore.GlobalB })
        {
            var used = flags.Keys.Where(f => f / 10000 == g).Select(f => (int)(f % 10000)).ToHashSet();
            Console.WriteLine($"global block {g}: {used.Count} of 10000 bits referenced; by thousand: " +
                string.Join(" ", Enumerable.Range(0, 10).Select(t => $"{t}xxx:{used.Count(u => u / 1000 == t)}")));
            var free = FreeRuns(used, 10000).OrderByDescending(r => r.Len).Take(6);
            Console.WriteLine("    largest unreferenced runs: " + string.Join(", ", free.Select(r => $"{g * 10000 + r.Start}..{g * 10000 + r.Start + r.Len - 1} ({r.Len})")));
        }
        if (list != null)
            File.WriteAllLines(list, flags.OrderBy(k => k.Key).Select(k => $"{k.Key}\t{string.Join("; ", k.Value.Select(r => r.Source).Distinct().Take(4))}"));
        return 0;
    }

    static IEnumerable<(int Start, int Len)> FreeRuns(HashSet<int> used, int n)
    {
        int start = -1;
        for (int i = 0; i <= n; i++)
        {
            bool free = i < n && !used.Contains(i);
            if (free && start < 0) start = i;
            if (!free && start >= 0) { yield return (start, i - start); start = -1; }
        }
    }
}
