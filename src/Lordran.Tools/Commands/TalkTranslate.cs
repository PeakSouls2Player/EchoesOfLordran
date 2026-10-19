using System.Text.RegularExpressions;
using Lordran.Tools.Build;
using Lordran.Tools.Build.Talk;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// talk-translate — the DS1 → DS2 talk translator as a report, without building a pack (session 8, track npc, step 2;
/// like emevd-translate for events):
/// <list type="bullet">
/// <item><c>talk-translate TALKID [--states]</c>: one DS1 talk (t181602 Oscar …): what is left out, the NpcTalkParam rows
/// and talk lines it registers, with --states every DS2 state (DS2 command/function names from the vanilla talk EDDs).</item>
/// <item><c>talk-translate MAP</c>: every talk of a DS1 map (m10_02_00_00).</item>
/// <item><c>talk-translate --all</c>: all 179 DS1 talk scripts: per talk the counts, then every left-out construct with
/// the number of talks using it (how general the rules are).</item>
/// </list>
/// Ids that only a build knows (items, lots, windows) show as placeholders: DS1 ids or our row ids.
/// </summary>
public static class TalkTranslate
{
    const string Usage = "usage: talk-translate TALKID [--states] | MAP | --all";

    static readonly Dictionary<int, string> Ds2Names = new()
    {
        [130001] = "End", [130101] = "SetFlag", [130121] = "SetAreaVariable", [130351] = "DamageCountReset", [130407] = "KeyGuideDelete", [130408] = "KeyGuideCreate", [130441] = "YesNoWindow",
        [130442] = "YesNoClose", [130443] = "OkWindow", [130444] = "OkClose", [130461] = "NpcWindowOpen", [130462] = "NpcWindowClose",
        [130463] = "NpcWindowClearResult", [131402] = "ItemLotAdd", [132001] = "TalkStart", [132002] = "TalkCancel",
        [130381] = "CovenantJoin", [130382] = "CovenantContribution", [130124] = "GlobalVariableAdd", [131401] = "ItemConsume", // session 11 (cov11)
    };
    static readonly Dictionary<int, string> Ds2Functions = new()
    {
        [6] = "Flag", [130121] = "AreaVariable", [130201] = "PlayerDistance", [130301] = "Hp", [130302] = "HpPercent", [130305] = "PlayerGender",
        [130311] = "DamageCount", [130412] = "ItemWindowShown", [130421] = "YesNoResult",
        [130422] = "YesNoShown", [130431] = "OkShown", [130451] = "NpcWindowShown", [130452] = "NpcWindowResult", [130453] = "NpcWindowUsed",
        [130602] = "IsGuest", [131002] = "CommonParamFloat", [131401] = "ItemCount", [132002] = "TalkEnded", [132003] = "TalkRequested",
        [130361] = "Covenant", [130362] = "CovenantLevel", [130122] = "GlobalVariable", [130661] = "MultiplayerCount", // session 11 (cov11)
    };

    public static int Run(string[] args)
    {
        if (args.Length < 2) return CliArgs.Fail("talk-translate", Usage, "missing TALKID, MAP or --all");
        var ds1 = new Ds1Source(Program.DefaultDsrRoot);
        bool states = args.Contains("--states");
        var talks = Ds1Talk.AllTalks().ToList();
        if (args[1] == "--all") return All(ds1, talks);
        IEnumerable<(string Map, int Talk, byte[] Bytes)> pick;
        if (Regex.IsMatch(args[1], @"^m\d\d_\d\d_\d\d_\d\d$")) pick = talks.Where(t => t.Map == args[1]);
        else if (int.TryParse(args[1].TrimStart('t', 'T'), out int id)) pick = talks.Where(t => t.Talk == id).Take(1);
        else return CliArgs.Fail("talk-translate", Usage, $"'{args[1]}' is not a talk id or map");
        var list = pick.ToList();
        if (list.Count == 0) return CliArgs.Fail("talk-translate", Usage, "no such talk (ds1-talk --list)");
        foreach (var (map, talk, bytes) in list)
        {
            var mt = new TalkBuilder.MapTalk(null, ds1, map, map, new HashSet<long>());
            var npc = new TalkBuilder.Npc(6000, 6000, 6000, $"t{talk}", talk, "report");
            // the NPC whose MSB part uses this talk: its DS1 death (fix pass: the death branch shows in the report as built)
            string msbPath = Path.Combine(ds1.Root, "map", "MapStudio", map + ".msb");
            if (File.Exists(msbPath) && MSB1.Read(msbPath).Parts.Enemies.FirstOrDefault(e => e.TalkID == talk && e.EntityID > 0) is { } part
                && Build.Chr.NpcPorter.FindDeath(ds1.Root, map, part.EntityID) is { } death && ds1.Params.Params["NpcParam"][part.NPCParamID] is { } npcRow)
                npc = npc with { Entity = part.EntityID, Death = death with { TalkDropLots = Build.Chr.NpcPorter.SplitDrops(ds1, map, npcRow, true).TalkRows } };
            var tr = new TalkTranslator(ESD.Read(bytes), talk, mt.For(npc));
            var group = tr.Translate();
            var (reach, prompt) = TalkTranslator.PromptReach(group);
            Console.WriteLine($"# t{talk} ({map}): DS2 group {talk}, {group.Count} states ({tr.PromptCopies} extra prompt/hit copies, {reach} reachable, talk prompt {(prompt ? "reachable" : "UNREACHABLE")}), {mt.TalkRows.Count} NpcTalkParam rows, {mt.Lines.Count} talk lines, {tr.Missing.Values.Sum()} left-out uses");
            foreach (var n in tr.Notes) Console.WriteLine($"  note: {n}");
            foreach (var (what, count) in tr.Missing) Console.WriteLine($"  left out: {what}{(count > 1 ? $" ×{count}" : "")}");
            foreach (var (rid, (msg, voice, next)) in mt.TalkRows)
                Console.WriteLine($"  talk row {rid}: msg {msg} voice {voice}{(next ? " → next" : "")}  \"{Formats.Ds1Fmgs.OneLine(mt.Lines[msg])}\"");
            if (states)
                foreach (var (sid, s) in group.OrderBy(k => k.Key))
                {
                    Console.WriteLine($"  state {sid}");
                    foreach (var c in s.EntryCommands) Console.WriteLine($"    entry {Cmd(c)}");
                    foreach (var c in s.WhileCommands) Console.WriteLine($"    while {Cmd(c)}");
                    foreach (var c in s.ExitCommands) Console.WriteLine($"    exit  {Cmd(c)}");
                    foreach (var c in s.Conditions) Cond(c, "    ");
                }
        }
        return 0;
    }

    /// <summary>Session 11 (cov11): one DS1 talk translated without a pack, with its NPC's DS1 death as in <see cref="Run"/>
    /// (talk-sim --ds1).</summary>
    public static (Dictionary<long, ESD.State> Group, TalkBuilder.MapTalk Mt, string Map)? Ds1Group(Ds1Source ds1, int talk)
    {
        if (Ds1Talk.AllTalks().FirstOrDefault(t => t.Talk == talk) is not { Bytes: not null } found) return null;
        var (map, _, bytes) = found;
        var mt = new TalkBuilder.MapTalk(null, ds1, map, map, new HashSet<long>());
        var npc = new TalkBuilder.Npc(6000, 6000, 6000, $"t{talk}", talk, "report");
        string msbPath = Path.Combine(ds1.Root, "map", "MapStudio", map + ".msb");
        if (File.Exists(msbPath) && MSB1.Read(msbPath).Parts.Enemies.FirstOrDefault(e => e.TalkID == talk && e.EntityID > 0) is { } part
            && Build.Chr.NpcPorter.FindDeath(ds1.Root, map, part.EntityID) is { } death && ds1.Params.Params["NpcParam"][part.NPCParamID] is { } npcRow)
            npc = npc with { Entity = part.EntityID, Death = death with { TalkDropLots = Build.Chr.NpcPorter.SplitDrops(ds1, map, npcRow, true).TalkRows } };
        var group = new TalkTranslator(ESD.Read(bytes), talk, mt.For(npc)).Translate();
        return (group, mt, map);
    }

    static string Fn(int id) => Ds2Functions.TryGetValue(id, out var n) ? n : $"f{id}";

    static string Cmd(ESD.CommandCall c) =>
        $"{(c.CommandBank == 5 ? "if-" : "")}{(Ds2Names.TryGetValue(c.CommandID, out var n) ? n : $"{c.CommandBank}:{c.CommandID}")}({string.Join(", ", c.Arguments.Select(a => TalkExpr.Parse(a).Text(Fn)))})";

    static void Cond(ESD.Condition c, string indent)
    {
        Console.WriteLine($"{indent}if {TalkExpr.Parse(c.Evaluator).Text(Fn)} -> {(c.TargetState?.ToString() ?? "-")}");
        foreach (var p in c.PassCommands) Console.WriteLine($"{indent}  pass {Cmd(p)}");
        foreach (var sub in c.Subconditions) Cond(sub, indent + "  ");
    }

    static int All(Ds1Source ds1, List<(string Map, int Talk, byte[] Bytes)> talks)
    {
        var byWhat = new Dictionary<string, HashSet<int>>();
        int ok = 0, failed = 0;
        var noPrompt = new List<string>();
        var original = new HashSet<int>();
        foreach (var (map, talk, bytes) in talks)
        {
            try
            {
                var mt = new TalkBuilder.MapTalk(null, ds1, map, map, new HashSet<long>());
                var esd = ESD.Read(bytes);
                var tr = new TalkTranslator(esd, talk, mt.For(new TalkBuilder.Npc(6000, 6000, 6000, $"t{talk}", talk, "report")));
                var g = tr.Translate();
                ok++;
                // the talk prompt must stay reachable (fix pass, review): DS1 talks that have a SetActionButtonText at all
                bool hasPrompt = esd.StateGroups.Values.SelectMany(s => s.Values).SelectMany(s => s.EntryCommands).Any(c => c.CommandBank == 1 && c.CommandID == 6 && TalkExpr.Parse(c.Arguments[0]) is not TalkExpr.Num { V: -1 });
                var (reach, prompt) = TalkTranslator.PromptReach(g);
                if (hasPrompt && original.Add(talk) && !prompt) noPrompt.Add($"{map} t{talk}");
                Console.WriteLine($"{map} t{talk,-7} {g.Count,4} states {mt.TalkRows.Count,4} talk rows {tr.Missing.Values.Sum(),4} left-out uses{(hasPrompt ? prompt ? "" : "  PROMPT UNREACHABLE" : "  (no DS1 prompt)")}");
                foreach (var what in tr.Missing.Keys)
                {
                    // group by kind: drop the varying ids so the survey counts rules, not values
                    string key = Regex.Replace(what, @"\b\d{4,}\b", "N");
                    if (!byWhat.TryGetValue(key, out var set)) byWhat[key] = set = [];
                    set.Add(talk);
                }
            }
            catch (Exception e) when (e is InvalidDataException or InvalidOperationException or KeyNotFoundException)
            {
                failed++;
                Console.WriteLine($"{map} t{talk,-7} FAILED: {e.Message}");
            }
        }
        Console.WriteLine($"# talk prompt unreachable in {noPrompt.Count} of {original.Count} distinct talks that have a DS1 prompt: {string.Join(", ", noPrompt)}");
        Console.WriteLine($"# {ok} talks translated, {failed} failed; left-out constructs by number of talks:");
        foreach (var (what, set) in byWhat.OrderByDescending(k => k.Value.Count))
            Console.WriteLine($"  {set.Count,4} talks  {what}");
        return failed == 0 ? 0 : 1;
    }
}
