using Lordran.Tools.Build.Events;
using Lordran.Tools.Build.Maps;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (track ret11), rule W1. <c>world-fold MAP</c>: DS1's world-state fold of a DS1 map (every MSB character:
/// enabled iff …, the DS2 generator conditions). <c>world-check PACK [DS1MAP]</c> (default m18_01_00_00): every generator of
/// the pack's DS2 map that stands for a DS1 character is checked against the fold: its start/stop records (bytes 0x0C-0x2B)
/// are evaluated with the decompile's semantics (FUN_1404125f0: judge 0-6, condition 7 = flag ON, 8 = OFF, 1 = true, 0 =
/// false) for every assignment of the fold's flags and must equal DS1's enable state (start) and live switch (stop); a
/// boss must start unconditionally; a character the fold calls "always" must not carry a flag condition (apart from an
/// NPC's dead-flag rule). Exit 1 on any mismatch.
/// </summary>
public static class WorldCheck
{
    public static int Fold(string[] args)
    {
        string map = args.Length > 1 ? args[1] : "m18_01_00_00";
        var fold = WorldState.Fold(Program.DefaultDsrRoot, map);
        var msb = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", map + ".msb"));
        Console.WriteLine($"{map}: {fold.Count} characters");
        foreach (var (e, x) in fold.OrderBy(kv => kv.Key))
        {
            var p = msb.Parts.Enemies.First(q => q.EntityID == e);
            var g = WorldState.ToGenerator(x, f => FlagRules.Map(map[..6], f) ?? FlagRules.OtherMapPersistent(map[..6], f), out string why);
            Console.WriteLine($"{e,-8} {p.Name,-11} {p.NPCParamID,-7} {x,-70} {(g is { } y ? $"DS2 start {y.Start}{(y.Stop != null ? $", stop {y.Stop}" : "")}" : why)}");
        }
        return 0;
    }

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: world-check PACK [DS1MAP]"); return 2; }
        string pack = args[1], map = args.Length > 2 ? args[2] : "m18_01_00_00";
        string ds2Map = FlagRules.Host[map[..6]];
        var fold = WorldState.Fold(Program.DefaultDsrRoot, map);
        string path = Path.Combine(pack, "gamedata", "param", $"generatorparam_{ds2Map}.param");
        if (!File.Exists(path)) { Console.Error.WriteLine($"no {path}"); return 2; }
        var gen = PARAM.Read(path);
        var blob = new PARAMDEF { ParamType = gen.ParamType, DataVersion = gen.ParamdefDataVersion, Unicode = true, FormatVersion = 104, BigEndian = false };
        blob.Fields.Add(new PARAMDEF.Field(blob, PARAMDEF.DefType.dummy8, "data") { ArrayLength = (int)gen.DetectedSize });
        gen.ApplyParamdef(blob);
        var rows = gen.Rows.ToDictionary(r => r.ID, r => (byte[])r["data"].Value);
        var areas = Build.Ds1Source.LoadParam(Program.DefaultDsrRoot, "GameAreaParam");
        int problems = 0, checkedN = 0, conditioned = 0;
        void Bad(string s) { problems++; Console.WriteLine("  PROBLEM " + s); }
        foreach (var (e, x) in fold.OrderBy(kv => kv.Key))
        {
            int g = EntityRules.Generator(e);
            if (!rows.TryGetValue(g, out var d)) continue; // not placed (no variant / online-only / dummy)
            checkedN++;
            bool boss = areas?[e] != null;
            var start = Rec(d, 0x0C); var stop = Rec(d, 0x1C);
            int respawnDisable = BitConverter.ToInt32(d, 0x04);
            string got = $"start {Show(start)}, stop {Show(stop)}";
            if (boss)
            {
                if (start.Conds.Any(c => c is 7 or 8) || stop.Conds.Any(c => c != 0)) Bad($"{e} (boss, generator {g}): {got}: a boss starts unconditionally (W1 excludes bosses)");
                else Console.WriteLine($"  ok {e} boss generator {g}: {got}");
                continue;
            }
            var want = WorldState.ToGenerator(x, f => FlagRules.Map(map[..6], f) ?? FlagRules.OtherMapPersistent(map[..6], f), out string why);
            if (want == null)
            {
                // "always" (or fallback): no W1 flag condition; an NPC may carry its dead-flag rule (cond 8 on respawnDisableFlag)
                bool deadRule = respawnDisable != 0 && start.Judge == 0 && start.Conds[0] == 8 && start.Params[0] == respawnDisable && start.Conds[1] == 0;
                bool flagCond = start.Conds.Any(c => c is 7 or 8) && !deadRule;
                if (flagCond || stop.Conds.Any(c => c != 0)) Bad($"{e} generator {g}: fold {x} ({why}) but {got}");
                else Console.WriteLine($"  ok {e} generator {g}: {x.Kind} → {got}");
                continue;
            }
            conditioned++;
            // evaluate both records over every assignment of the fold's flags (DS2 ids) and compare with DS1's table
            var lits = x.Start?.Lits.Select(l => l.Flag).Concat(x.Stop.Select(s => s.Flag)).Distinct().ToList() ?? [];
            var ids = lits.ToDictionary(f => f, f => FlagRules.Map(map[..6], f) ?? FlagRules.OtherMapPersistent(map[..6], f) ?? -1);
            bool ok = true;
            for (int r = 0; r < 1 << lits.Count; r++)
            {
                var on = new HashSet<int>(lits.Where((f, b) => (r >> b & 1) != 0).Select(f => ids[f]));
                var ds1On = new HashSet<int>(lits.Where((f, b) => (r >> b & 1) != 0));
                bool ds1 = x.Kind == "never" ? false : Ds1Value(x.Start!, ds1On);
                bool ds2 = Eval(start, on);
                bool ds1Stop = x.Stop.Any(s => ds1On.Contains(s.Flag) == s.On);
                bool ds2Stop = stop.Conds.Any(c => c != 0) && Eval(stop, on);
                if (ds1 != ds2 || ds1Stop != ds2Stop) { ok = false; Bad($"{e} generator {g}: assignment {string.Join(",", lits.Select((f, b) => $"{f}={(r >> b & 1)}"))}: DS1 enabled {ds1} / stop {ds1Stop}, DS2 start {ds2} / stop {ds2Stop} ({got})"); break; }
            }
            if (ok) Console.WriteLine($"  ok {e} generator {g}: {x} → {got}");
        }
        Console.WriteLine($"world-check {pack} {map}: {checkedN} placed DS1 characters, {conditioned} with W1 conditions, {problems} problems");
        return problems == 0 ? 0 : 1;
    }

    sealed record R(byte Judge, byte[] Conds, int[] Params);
    static R Rec(byte[] d, int at) => new(d[at], [d[at + 1], d[at + 2], d[at + 3]], [BitConverter.ToInt32(d, at + 4), BitConverter.ToInt32(d, at + 8), BitConverter.ToInt32(d, at + 12)]);
    static string Show(R r) => $"judge {r.Judge} [{string.Join(" ", Enumerable.Range(0, 3).Where(k => r.Conds[k] != 0).Select(k => $"{r.Conds[k]}/{r.Params[k]}"))}]";

    /// <summary>One condition (GENERATOR_EVAL_ENUM): 0 false, 1 true, 7 flag on, 8 flag off; anything else = unknown (false).</summary>
    static bool Cond(byte c, int p, HashSet<int> on) => c switch { 1 => true, 7 => on.Contains(p), 8 => !on.Contains(p), _ => false };

    /// <summary>The judge (FUN_1404125f0 outer switch): 0 ①, 1 ①&amp;②, 2 ①|②, 3 ①&amp;②&amp;③, 4 ①|②|③, 5 (①&amp;②)|③, 6 (①|②)&amp;③.</summary>
    static bool Eval(R r, HashSet<int> on)
    {
        bool a = Cond(r.Conds[0], r.Params[0], on), b = Cond(r.Conds[1], r.Params[1], on), c = Cond(r.Conds[2], r.Params[2], on);
        return r.Judge switch { 0 => a, 1 => a && b, 2 => a || b, 3 => a && b && c, 4 => a || b || c, 5 => a && b || c, 6 => (a || b) && c, _ => false };
    }

    static bool Ds1Value(WorldState.Form f, HashSet<int> on)
    {
        var v = f.Lits.Select(l => on.Contains(l.Flag) == l.On).ToArray();
        return f.Judge switch { 0 => v[0], 1 => v[0] && v[1], 2 => v[0] || v[1], 3 => v[0] && v[1] && v[2], 4 => v[0] || v[1] || v[2], 5 => v[0] && v[1] || v[2], 6 => (v[0] || v[1]) && v[2], _ => false };
    }
}
