using System.Text;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// obj-states — which object states vanilla DS2 event scripts request (131636 instance, state) or wait for (f131653 /
/// 140201), resolved through routine parameters (6:ROUTINE(args) calls, nested), joined with each instance's model,
/// objCtrlType and the model's AnimList.list (anim id → name). Session 7: what state ids mean for event-driven objects.
/// </summary>
public static class ObjStates
{
    public static int Run(string[] args)
    {
        string data = Path.Combine("data", "ds2");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string alDir = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : "";
        // session 10 (asy-events, rule C11 survey): --detail MODEL,MODEL prints every request/test of those models with
        // map, group, state and the group's developer name (EDD)
        var detail = new HashSet<string>();
        for (int i = 1; i < args.Length - 1; i++) if (args[i] == "--detail") detail.UnionWith(args[i + 1].Split(','));
        var enc = Encoding.GetEncoding(932);
        var animLists = new Dictionary<string, string>();
        if (Directory.Exists(alDir))
            foreach (var f in Directory.GetFiles(alDir, "AnimList.list", SearchOption.AllDirectories))
                animLists[Path.GetFileName(Path.GetDirectoryName(f))!] = enc.GetString(File.ReadAllBytes(f)).Replace("\r\n", " | ");
        var stats = new SortedDictionary<string, int>();
        foreach (var esdPath in Directory.GetFiles(Path.Combine(data, "ezstate"), "event_m*.esd"))
        {
            string map = Path.GetFileNameWithoutExtension(esdPath).Replace("event_", "");
            ESD esd; MSB2 msb; PARAM? inst = null;
            try { esd = ESD.Read(esdPath); msb = MSB2.Read(Path.Combine(data, "map", map, map + ".msb")); } catch { continue; }
            try { inst = PARAM.Read(Path.Combine(data, "param", $"mapobjectinstanceparam_{map}.param")); } catch { }
            var model = msb.Parts.Objects.GroupBy(o => o.MapObjectInstanceParamID).ToDictionary(g => g.Key, g => g.First().ModelName);
            string Info(int id)
            {
                string m = model.GetValueOrDefault(id, "?");
                return m;
            }
            // callers: routine id -> list of arg lists (already resolved to constants where possible)
            var calls = new Dictionary<long, List<string[]>>();
            void Collect(Dictionary<long, ESD.State> states, Func<string, string> subst)
            {
                foreach (var s in states.Values)
                    foreach (var c in s.EntryCommands.Concat(s.Conditions.SelectMany(x => x.PassCommands)))
                        if (c.CommandBank == 6)
                        {
                            var a = c.Arguments.Select(x => subst(Esd.Expr(x))).ToArray();
                            if (!calls.TryGetValue(c.CommandID, out var l)) calls[c.CommandID] = l = [];
                            if (!l.Any(x => x.SequenceEqual(a))) l.Add(a);
                        }
            }
            foreach (var (gid, states) in esd.StateGroups.Where(g => g.Key < 2000000000)) Collect(states, x => x);
            for (int pass = 0; pass < 4; pass++)
                foreach (var (gid, states) in esd.StateGroups.Where(g => g.Key >= 2000000000).ToList())
                    if (calls.TryGetValue(gid, out var argSets))
                        foreach (var a in argSets.ToList()) Collect(states, x => Subst(x, a));
            foreach (var (gid, states) in esd.StateGroups)
            {
                var argSets = gid >= 2000000000 ? calls.GetValueOrDefault(gid) ?? [] : [Array.Empty<string>()];
                foreach (var a in argSets)
                    foreach (var (sid, s) in states)
                    {
                        foreach (var c in s.EntryCommands)
                        {
                            if (c.CommandID is not (131636 or 140201)) continue;
                            var x = c.Arguments.Select(y => Subst(Esd.Expr(y), a)).ToArray();
                            int io = c.CommandID == 131636 ? 0 : 1;
                            if (x.Length <= io + 1 || !int.TryParse(x[io], out int id)) continue;
                            string m = Info(id);
                            string k = $"{(c.CommandID == 131636 ? "set " : "wait")} state {x[io + 1],3} model {m}";
                            if (detail.Contains(m)) Console.WriteLine($"DETAIL {map} g{gid} s{sid} {k} inst {id}{(a.Length > 0 ? $" args ({string.Join(", ", a)})" : "")} '{Name(map, gid)}'");
                            stats[k] = stats.GetValueOrDefault(k) + 1;
                        }
                        foreach (var cond in s.Conditions)
                        {
                            string e = Subst(Esd.Expr(cond.Evaluator), a);
                            foreach (System.Text.RegularExpressions.Match mt in System.Text.RegularExpressions.Regex.Matches(e, @"f131653\((\d+), (\d+), (\d+)\)"))
                            {
                                string k = $"test state {mt.Groups[2].Value,3} model {Info(int.Parse(mt.Groups[1].Value))}";
                                if (detail.Contains(Info(int.Parse(mt.Groups[1].Value)))) Console.WriteLine($"DETAIL {map} g{gid} s{sid} {k} inst {mt.Groups[1].Value} '{Name(map, gid)}'");
                                stats[k] = stats.GetValueOrDefault(k) + 1;
                            }
                        }
                    }
            }
        }
        foreach (var (k, v) in stats)
        {
            string m = k[(k.LastIndexOf(' ') + 1)..];
            Console.WriteLine($"{k,-40} x{v,-3} {animLists.GetValueOrDefault(m, "")}");
        }
        return 0;
    }

    static readonly Dictionary<string, Dictionary<long, string>> eddNames = [];
    /// <summary>The developer name of an event machine (data\ds2\ezstate\event_MAP.edd), or "".</summary>
    static string Name(string map, long gid)
    {
        if (!eddNames.TryGetValue(map, out var d))
        {
            eddNames[map] = d = [];
            string f = Path.Combine("data", "ds2", "ezstate", $"event_{map}.edd");
            try { if (File.Exists(f)) foreach (var m in EDD.Read(f).Machines) d[(uint)m.ID] = m.Name; } catch { }
        }
        return d.GetValueOrDefault(gid, "");
    }

    static string Subst(string expr, string[] a) => a.Length == 0 ? expr :
        System.Text.RegularExpressions.Regex.Replace(expr, @"\bp(\d+)\b", m => int.Parse(m.Groups[1].Value) < a.Length ? a[int.Parse(m.Groups[1].Value)] : m.Value);
}
