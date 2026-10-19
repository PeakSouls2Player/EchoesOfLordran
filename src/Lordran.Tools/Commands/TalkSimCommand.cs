using System.Globalization;
using Lordran.Tools.Build.Talk;

namespace Lordran.Tools.Commands;

/// <summary>
/// talk-sim PACKDIR MAP [GROUP...] [--scenario rest,far,talk,talk-no,hit,hit2,death|all] [--script "STEPS"] [--near M]
/// [--line-frames N] [--answer-frames N] [--start-count N] [--stuck-count] [--legacy-ops] [--bearing DEG] [--facing DEG]
/// [--log] [--trace]
/// Session 11 (cov11): talk-sim --ds1 TALKID â€¦ runs a DS1 talk translated in memory (maps not ported yet); scenarios cov-join,
/// cov-member, cov-switch, cov-offer (steps covenant N | item ID N | var ID V; menu pledge / offer = window results 5 / 6).
/// â€” session 9 (track npctalk): runs a built pack's talk groups frame by frame with DS2's ESD semantics (Build/Talk/TalkSim:
/// up to 100 transitions per frame, stopping at the first iteration without one; first true condition, exit â†’ pass â†’
/// entry, then while commands; DS2's comparison opcodes) in a scripted world and prints per scenario: within-frame cycles,
/// the prompt's lifetime, the lines in order, dialogs and answers, items, flags.
/// Steps (';'-separated): dist D [over N] | hold N | press | hit | kill | hp V | flag F V | answer yes,no,â€¦ |
/// menu talk,leave,â€¦ | until-idle [N]. --legacy-ops evaluates 0x91-0x94 with the table our tools used before session 9,
/// --start-count / --stuck-count are what-ifs for the hit counter (start value; 130351 without effect), --bearing /
/// --facing place the player for the key guide's angle tests (angle from the guide's +Z; facing off the guide).
/// </summary>
public static class TalkSimCommand
{
    const string Usage = "usage: talk-sim PACKDIR MAP | --ds1 TALKID [GROUP...] [--scenario NAME,...|all] [--script \"STEPS\"] [--near M] [--line-frames N] [--answer-frames N] [--start-count N] [--stuck-count] [--legacy-ops] [--bearing DEG] [--facing DEG] [--log] [--trace]";

    public static int Run(string[] args)
    {
        if (args.Length < 3) return CliArgs.Fail("talk-sim", Usage, "missing PACKDIR or MAP");
        string pack = args[1], map = args[2];
        var groups = new List<long>();
        var scenarios = new List<string> { "rest", "talk", "hit", "death" };
        string? script = null, nearArg = null;
        bool log = false, trace = false;
        // session 11 (cov11): --ds1 TALKID runs a DS1 talk translated in memory (no pack: maps not ported yet), e.g. the
        // covenant scenarios on Kaathe or the Sunlight Altar
        var (esd, template) = pack == "--ds1" ? Ds1Talk(map) : TalkSim.LoadPack(pack, map);
        for (int i = 3; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value");
            switch (a)
            {
                case "--scenario": { string v = Next(); scenarios = v == "all" ? [.. TalkSim.Scripts.Keys] : [.. v.Split(',')]; break; }
                case "--script": script = Next(); break;
                case "--near": nearArg = Next(); break;
                case "--line-frames": template.LineFrames = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--answer-frames": template.AnswerFrames = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--start-count": template.StartCount = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--stuck-count": template.ResetWorks = false; break;
                case "--legacy-ops": template.LegacyOps = true; break;
                case "--no-flag-store": template.FlagMap = null; break; // session 9 (npcflags): every flag id stores (old model)
                case "--bearing": template.Bearing = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--facing": template.Facing = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--log": log = true; break;
                case "--trace": trace = true; log = true; break;
                default:
                    if (long.TryParse(a, out long gid)) groups.Add(gid);
                    else return CliArgs.Fail("talk-sim", Usage, $"unknown option {a}");
                    break;
            }
        }
        if (groups.Count == 0) groups = esd.StateGroups.Keys.Where(k => k < 2_000_000_000).OrderBy(k => k).ToList();
        Console.WriteLine($"talk-sim {map}: {esd.StateGroups.Count} groups, {template.TalkRows.Count} NpcTalkParam rows, {template.Lines.Count} talk lines; "
            + $"{template.Iterations} iterations/frame at {template.Fps} fps, line {template.LineFrames} frames, answers after {template.AnswerFrames} frames"
            + $"{(template.LegacyOps ? ", LEGACY comparison opcodes" : "")}{(template.StartCount > 0 ? $", hit count starts at {template.StartCount}" : "")}{(template.ResetWorks ? "" : ", 130351 without effect")}"
            + $"{(template.Bearing != 0 || template.Facing != 0 ? $", player at {template.Bearing}Â° from the key guide's +Z, facing {template.Facing}Â° off it" : "")}"
            + (template.FlagMap != null ? $", DS2 flag storage of {template.FlagMap} (unbacked writes dropped)" : ", every flag id stores (--no-flag-store)"));
        int cycles = 0;
        foreach (long gid in groups)
        {
            if (!esd.StateGroups.TryGetValue(gid, out var group)) { Console.WriteLine($"# group {gid}: not in talk_{map}.esd"); continue; }
            string near = nearArg ?? TalkSim.NearFor(group, template).ToString("0.##", CultureInfo.InvariantCulture);
            Console.WriteLine($"# talk group {gid}: {group.Count} states, player stands at {near} m");
            List<(string Name, string Steps)> runs = script != null ? [("script", script)] : [.. scenarios.Select(s => (s, TalkSim.Scripts[s]))];
            foreach (var (name, steps) in runs)
            {
                var ph = TalkSim.Placeholders(group); // session 11 (cov11): {pledge} {votive} {other}
                string filled = ph.Aggregate(steps.Replace("{near}", near), (s, kv) => s.Replace(kv.Key, kv.Value));
                var r = TalkSim.Run(group, template, name, filled, trace);
                cycles += r.CycleFrames;
                Console.WriteLine("  " + TalkSim.Summary(r));
                foreach (var l in r.Lines) Console.WriteLine($"    line {l}");
                foreach (var d in r.Dialogs) Console.WriteLine($"    dialog {d}");
                foreach (var it in r.Items) Console.WriteLine($"    item {it}");
                foreach (var f in r.FlagChanges) Console.WriteLine($"    flag {f}");
                foreach (var cv in r.Covenant) Console.WriteLine($"    covenant {cv}"); // session 11 (cov11)
                if (r.Covenant.Count > 0 || name.StartsWith("cov-")) Console.WriteLine($"    end: {r.CovenantEnd}");
                foreach (var u in r.Unmodelled) Console.WriteLine($"    not modelled: {u}");
                if (log) foreach (var e in r.Log) Console.WriteLine($"    @{e.Frame} {e.Kind}: {e.Text}");
            }
        }
        return cycles == 0 ? 0 : 1;
    }

    /// <summary>Session 11 (cov11): a DS1 talk translated without a pack (as talk-translate does) as a talk-sim world: its
    /// NpcTalkParam rows and lines, the lots it would write (DS1 item ids: no item map without a pack), DS2's VowParam;
    /// flags stored as in the hosting DS2 map when the DS1 map is ported, else every id stores.</summary>
    static (SoulsFormats.ESD, TalkSim.Options) Ds1Talk(string talkArg)
    {
        int talk = int.Parse(talkArg.TrimStart('t', 'T'), CultureInfo.InvariantCulture);
        var ds1 = new Build.Ds1Source(Program.DefaultDsrRoot);
        var (group, mt, ds1Map) = TalkTranslate.Ds1Group(ds1, talk) ?? throw new ArgumentException($"talk-sim --ds1: no DS1 talk t{talk}");
        var esd = new SoulsFormats.ESD();
        esd.StateGroups[talk] = group;
        var o = new TalkSim.Options { FlagMap = Build.Events.FlagRules.Host.GetValueOrDefault(ds1Map[..6]) };
        foreach (var (id, (msg, _, next)) in mt.TalkRows) o.TalkRows.Add((id, msg, next));
        foreach (var (id, text) in mt.Lines) o.Lines[id] = text;
        foreach (var (id, items) in mt.ReportLots) o.Lots[id] = items;
        TalkSim.VowThresholds(o, Lordran.Tools.Formats.ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut));
        Console.WriteLine($"talk-sim --ds1 t{talk} ({ds1Map}, not built: translated in memory; item ids are DS1's), flag storage {o.FlagMap ?? "every id"}");
        return (esd, o);
    }
}
