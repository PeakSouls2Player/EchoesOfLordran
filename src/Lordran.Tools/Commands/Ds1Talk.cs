using System.Text;
using System.Text.RegularExpressions;
using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// ds1-talk — DS1 (DSR) talk scripts (script\talk\*.talkesdbnd.dcx, one t&lt;talkId&gt;.esd per NPC/bonfire talk) dumped as
/// state machines with DS1 talk command and function names, like esd-dump does for DS2.
/// <list type="bullet">
/// <item><c>ds1-talk TALKID|MAP|FILE.esd [--out DIR] [--raw] [--no-text]</c>: TALKID (e.g. 181602 = Oscar) finds its bnd;
/// MAP (m18_01_00_00) dumps every talk of the map; --out writes one t&lt;id&gt;.txt per talk (and t&lt;id&gt;.esd with --raw)
/// instead of printing; a talk id found in more than one bnd is written as t&lt;id&gt;_&lt;map&gt;.</item>
/// <item><c>ds1-talk --list</c>: every talk script per map with its size, state count and the MSB parts using it.</item>
/// <item><c>ds1-talk --survey [--max N]</c>: every command/function id across all DS1 talk scripts with use counts, sample
/// arguments, which text tables the arguments resolve in and what results are compared against: the evidence behind
/// the name table.</item>
/// <item><c>ds1-talk --names</c>: the name table (Mappings\ds1-talk-esd.tsv).</item>
/// </list>
/// <para>
/// Names: no DarkScript/ESDLang DS1 talk name list exists locally (DarkScript3's emedf in reference\emedf covers EMEVD
/// only; Smithbox's DS1R TalkScripts.json alias list is empty), so the names in Mappings\ds1-talk-esd.tsv are our own,
/// inferred from --survey evidence (argument ranges resolving in Conversation_/Event_text_/Menu FMGs, shop params,
/// flags, comparison constants such as distances/angles), each with a status V/P/U; unnamed ids print as c1_NN / fNN.
/// </para>
/// Expression bytecode is the same as DS2's (Esd.Expr). Arguments that resolve in a text table get the text appended.
/// Session 8 (census tool gap: no tool extracted DS1 talk ESDs, which the NPC questlines need).
/// </summary>
public static class Ds1Talk
{
    const string Usage =
        "usage: ds1-talk TALKID|MAP|FILE.esd [--out DIR] [--raw] [--no-text]\n" +
        "       ds1-talk --list | --names | --survey [--max N]\n" +
        "  TALKID: MSB TalkID / t<id>.esd, e.g. 181602 (Oscar); MAP: m18_01_00_00 (all talks of the map)\n" +
        "  --out DIR: write t<id>.txt per talk (--raw also writes the .esd); --no-text: no FMG texts after arguments";

    static string TalkDir => Path.Combine(Program.DefaultDsrRoot, "script", "talk");

    /// <summary>One entry of the name table.</summary>
    public sealed record Name(string Kind, int Id, string Text, string[] Args, string Status, string Evidence);

    static Dictionary<(string, int), Name>? names;

    /// <summary>Mappings\ds1-talk-esd.tsv: kind (cmd/fn) TAB id TAB name TAB arg kinds TAB status TAB evidence.</summary>
    public static Dictionary<(string Kind, int Id), Name> Names
    {
        get
        {
            if (names != null) return names;
            names = [];
            string path = Path.Combine(AppContext.BaseDirectory, "Mappings", "ds1-talk-esd.tsv");
            if (!File.Exists(path)) return names;
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var c = line.Split('\t');
                if (c.Length < 3 || !int.TryParse(c[1], out int id)) continue;
                var args = c.Length > 3 && c[3].Trim() is { Length: > 0 } a && a != "-" ? a.Split(',').Select(x => x.Trim()).ToArray() : [];
                names[(c[0], id)] = new Name(c[0], id, c[2], args, c.Length > 4 ? c[4] : "U", c.Length > 5 ? c[5] : "");
            }
            return names;
        }
    }

    static string? FnName(int id) => Names.TryGetValue(("fn", id), out var n) ? n.Text : null;
    static string CmdName(int bank, int id) => bank == 1 && Names.TryGetValue(("cmd", id), out var n) ? n.Text : $"c{bank}_{id}";

    /// <summary>Every talk script: (bnd map name, talk id, bytes).</summary>
    public static IEnumerable<(string Map, int Talk, byte[] Bytes)> AllTalks()
    {
        foreach (string bndPath in Directory.GetFiles(TalkDir, "*.talkesdbnd.dcx").Order())
        {
            string map = Path.GetFileName(bndPath).Replace(".talkesdbnd.dcx", "");
            foreach (var f in BND3.Read(bndPath).Files.OrderBy(f => f.ID))
                if (TalkId(f.Name) is int id) yield return (map, id, f.Bytes);
        }
    }

    static int? TalkId(string name)
    {
        var m = Regex.Match(Path.GetFileName(name), @"^t(\d+)\.esd$", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    public static int Run(string[] args)
    {
        if (args.Length < 2) return CliArgs.Fail("ds1-talk", Usage, "missing TALKID, MAP, FILE or mode");
        if (args[1] == "--names") return PrintNames();
        if (args[1] == "--list") return List();
        if (args[1] == "--survey")
        {
            string? maxText = CliArgs.Option(args, "--max", out bool missing);
            if (missing || maxText != null && !int.TryParse(maxText, out _)) return CliArgs.Fail("ds1-talk", Usage, "--max needs a number");
            return Survey(maxText != null ? int.Parse(maxText) : 8);
        }
        if (args[1].StartsWith("--")) return CliArgs.Fail("ds1-talk", Usage, $"unknown mode {args[1]}");

        string? outDir = CliArgs.Option(args, "--out", out bool outMissing);
        if (outMissing) return CliArgs.Fail("ds1-talk", Usage, "--out needs a directory");
        bool raw = args.Contains("--raw"), noText = args.Contains("--no-text");
        foreach (string o in args.Skip(2).Where(a => a.StartsWith("--")))
            if (o is not ("--out" or "--raw" or "--no-text")) return CliArgs.Fail("ds1-talk", Usage, $"unknown option {o}");
        var extra = args.Skip(2).Where((a, i) => !a.StartsWith("--") && !(i > 0 && args[i + 1] == "--out")).ToList();
        if (extra.Count > 0) return CliArgs.Fail("ds1-talk", Usage, $"unexpected argument '{extra[0]}'");
        if (raw && outDir == null) return CliArgs.Fail("ds1-talk", Usage, "--raw needs --out DIR");

        string target = args[1];
        List<(string Map, int Talk, byte[] Bytes)> talks;
        if (File.Exists(target))
            talks = [("(file)", TalkId(target) ?? 0, File.ReadAllBytes(target))];
        else if (Regex.IsMatch(target, @"^m\d\d_\d\d_\d\d_\d\d$"))
        {
            string bnd = Path.Combine(TalkDir, target + ".talkesdbnd.dcx");
            if (!File.Exists(bnd)) return CliArgs.Fail("ds1-talk", Usage, $"no talk bnd for map {target} in {TalkDir}");
            talks = AllTalks().Where(t => t.Map == target).ToList();
            if (talks.Count == 0) { Console.WriteLine($"{target}: no talk scripts"); return 0; }
        }
        else if (int.TryParse(target.TrimStart('t', 'T'), out int talkId))
        {
            talks = AllTalks().Where(t => t.Talk == talkId).ToList();
            if (talks.Count == 0) return CliArgs.Fail("ds1-talk", Usage, $"no t{talkId}.esd in any DSR talk bnd (ds1-talk --list shows them)");
        }
        else return CliArgs.Fail("ds1-talk", Usage, $"'{target}' is not a talk id, map name or file");

        var users = TalkUsers();
        if (outDir != null) Directory.CreateDirectory(outDir);
        // a talk id can sit in two bnds (t100000: m10_00_00_00 and m12_00_00_01). Such ids always get the map in the file
        // name, not only when one run selects both, so per-map runs into one folder keep both files (session 8 fix pass:
        // 18 per-map runs into one folder wrote 178 files for the 179 scripts)
        HashSet<int> inSeveralBnds = outDir == null ? [] : AllTalks().GroupBy(t => t.Talk)
            .Where(g => g.Select(t => t.Map).Distinct().Count() > 1).Select(g => g.Key).ToHashSet();
        foreach (var (map, talk, bytes) in talks)
        {
            string text = Dump(map, talk, bytes, users, !noText);
            if (outDir == null) { Console.Write(text); continue; }
            bool byMap = talks.Count(t => t.Talk == talk) > 1 || map != "(file)" && inSeveralBnds.Contains(talk);
            string stem = byMap ? $"t{talk}_{map}" : $"t{talk}";
            File.WriteAllText(Path.Combine(outDir, stem + ".txt"), text, new UTF8Encoding(false));
            if (raw) File.WriteAllBytes(Path.Combine(outDir, stem + ".esd"), bytes);
            Console.WriteLine($"{map} t{talk}: {Path.Combine(outDir, stem + ".txt")}");
        }
        return 0;
    }

    /// <summary>MSB parts per talk id across every DSR map (Enemy/DummyEnemy TalkID), for the dump headers.</summary>
    static Dictionary<int, List<string>> TalkUsers()
    {
        var users = new Dictionary<int, List<string>>();
        foreach (string msbPath in Directory.GetFiles(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio"), "*.msb").Order())
        {
            MSB1 msb;
            try { msb = MSB1.Read(msbPath); } catch (Exception e) when (e is InvalidDataException or EndOfStreamException) { continue; }
            string map = Path.GetFileNameWithoutExtension(msbPath);
            foreach (var e in msb.Parts.Enemies.Cast<MSB1.Part.EnemyBase>().Concat(msb.Parts.DummyEnemies).Where(e => e.TalkID > 0))
            {
                string npc = store.Value.RowName("NpcParam", e.NPCParamID);
                if (!users.TryGetValue(e.TalkID, out var l)) users[e.TalkID] = l = [];
                l.Add($"{map} {e.Name} entity={e.EntityID} npc={e.NPCParamID}{(npc != "" ? $" '{npc}'" : "")}");
            }
        }
        return users;
    }

    static string Dump(string map, int talk, byte[] bytes, Dictionary<int, List<string>> users, bool texts)
    {
        var sb = new StringBuilder();
        var esd = ESD.Read(bytes);
        var fmgs = texts ? Ds1Fmgs.Load(Program.DefaultDsrRoot) : null;
        int states = esd.StateGroups.Values.Sum(g => g.Count);
        sb.AppendLine($"# t{talk}  ({map}.talkesdbnd.dcx, {bytes.Length} bytes)  long={esd.LongFormat} dsCount={esd.DarkSoulsCount} groups={esd.StateGroups.Count} states={states}");
        foreach (string u in users.GetValueOrDefault(talk) ?? []) sb.AppendLine($"# used by {u}");
        var talkLines = new SortedDictionary<int, string>();
        foreach (var (gid, group) in esd.StateGroups.OrderBy(g => g.Key))
        {
            sb.AppendLine($"group {GroupId(gid)}: {group.Count} states");
            foreach (var (sid, s) in group.OrderBy(s => s.Key))
            {
                string debug = s.EntryCommands.Where(c => c.CommandBank == 1 && c.CommandID == 0 && c.Arguments.Count == 1).Select(c => Esd.Expr(c.Arguments[0])).FirstOrDefault() ?? "";
                sb.AppendLine($"  state {sid}{(debug != "" ? $"  {debug}" : "")}");
                foreach (var c in s.EntryCommands) sb.AppendLine($"    entry {Cmd(c, fmgs, talkLines)}");
                foreach (var c in s.WhileCommands) sb.AppendLine($"    while {Cmd(c, fmgs, talkLines)}");
                foreach (var c in s.ExitCommands) sb.AppendLine($"    exit  {Cmd(c, fmgs, talkLines)}");
                foreach (var cond in s.Conditions) Cond(sb, cond, "    ", fmgs, talkLines);
            }
        }
        if (talkLines.Count > 0)
        {
            sb.AppendLine($"# {talkLines.Count} text ids referenced:");
            foreach (var (id, t) in talkLines) sb.AppendLine($"#   {id}: {t}");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>State group ids: DSR uses small ids (1, 2 …) per talk; large ones print in hex.</summary>
    static string GroupId(long gid) => gid is >= 0 and < 1000000 ? gid.ToString() : $"0x{gid:X}";

    static string Cmd(ESD.CommandCall c, Ds1Fmgs? fmgs, SortedDictionary<int, string> lines)
    {
        Names.TryGetValue(("cmd", c.CommandID), out var n);
        if (c.CommandBank != 1) n = null;
        var exprs = c.Arguments.Select(a => Esd.Expr(a, FnName)).ToList();
        var shown = new List<string>();
        int? category = null; // the last "itemcat" argument, for a following "item"
        for (int i = 0; i < exprs.Count; i++)
        {
            string? kind = n != null && i < n.Args.Length ? n.Args[i] : null;
            bool constant = int.TryParse(exprs[i], out int v);
            if (kind == "itemcat" && constant) category = v;
            shown.Add(fmgs != null && constant && v > 0 && Annotate(kind, v, category, fmgs) is { } note ? Note(exprs[i], v, note, lines) : exprs[i]);
        }
        return $"{(n != null ? n.Text : $"c{c.CommandBank}_{c.CommandID}")}({string.Join(", ", shown)})";
    }

    static string Note(string expr, int v, (string Table, string Text) note, SortedDictionary<int, string> lines)
    {
        string text = Ds1Fmgs.OneLine(note.Text);
        if (note.Table != "") lines[v] = $"[{note.Table}] {text}";
        return $"{expr} \"{(text.Length > 48 ? text[..48] + "…" : text)}\"";
    }

    static readonly Lazy<ParamStore> store = new(() => ParamStore.LoadDsr(Program.DefaultDsrRoot));

    /// <summary>
    /// The text behind a constant argument of a known kind: talk → Conversation_, event → Event_text_, goods → Item_name_,
    /// item → the name table of the preceding itemcat (DS1 talk/EMEVD item types 0 weapon, 1 armor, 2 ring, 3 goods),
    /// shop → the ShopLineupParam row's community name (Table "" = not listed in the text summary).
    /// </summary>
    static (string Table, string Text)? Annotate(string? kind, int v, int? category, Ds1Fmgs fmgs)
    {
        if (kind == "talk")
        {
            var lines = TalkLines(v, fmgs);
            if (lines.Count > 0)
                return ($"TalkParam {lines[0].Row}{(lines[^1].Row != lines[0].Row ? $"-{lines[^1].Row}" : "")}",
                    string.Join(" / ", lines.Select(l => l.Text)) + (lines[0].Motion >= 0 ? $"  (motion {lines[0].Motion})" : ""));
            var direct = fmgs.Get("talk", v);
            return direct != null ? ("Conversation_, no TalkParam row", direct) : null;
        }
        string? sel = kind switch
        {
            "event" => "event", "goods" => "goods",
            "item" => category switch { 0 => "weapon", 1 => "armor", 2 => "ring", 3 => "goods", _ => null },
            _ => null,
        };
        if (sel != null)
        {
            var hit = fmgs.Hits(v, fmgs.Select(sel)).FirstOrDefault();
            return hit.Table != null ? (hit.Table.Name, hit.Text) : null;
        }
        if (kind == "shop")
        {
            string name = store.Value.RowName("ShopLineupParam", v);
            return name != "" ? ("", name) : null;
        }
        return null;
    }

    /// <summary>
    /// The subtitle lines of a TalkToPlayer id. Session 8 evidence (Oscar, t181602): the argument is a TalkParam row id;
    /// a talk plays that row and the consecutive rows after it (12000000-12000005 = the first meeting, like item-lot
    /// chains); each row's msgId is its first Conversation_ entry and the English text continues over the following ids
    /// up to the next msgId used by any row (row 12000002 → msg 12000002 + 12000003, row 12000003 → msg 12000004). Rows also carry
    /// the NPC's talk motion (motionId) and voice. A few calls name no row (Oscar state 26: 12000009, which is the msgId of
    /// row 12000005); those fall back to the Conversation_ entry of the same id.
    /// </summary>
    static int[]? msgIds;

    public static List<(int Row, int Msg, int Motion, string Text)> TalkLines(int talkId, Ds1Fmgs fmgs)
    {
        var result = new List<(int, int, int, string)>();
        if (!store.Value.Params.TryGetValue("TalkParam", out var talk) || talk[talkId] == null) return result;
        var rows = new List<PARAM.Row>();
        for (int id = talkId; rows.Count < 64 && talk[id] is { } row; id++) rows.Add(row);
        int Msg(PARAM.Row r) => System.Convert.ToInt32(r["msgId"].Value);
        msgIds ??= talk.Rows.Select(Msg).Where(m => m > 0).Distinct().Order().ToArray();
        for (int i = 0; i < rows.Count; i++)
        {
            int msg = Msg(rows[i]);
            // a row's English lines run up to the next msgId used by any TalkParam row
            int next = Array.BinarySearch(msgIds, msg + 1);
            if (next < 0) next = ~next;
            int end = next < msgIds.Length ? Math.Min(msgIds[next], msg + 16) : msg + 1;
            var parts = new List<string>();
            for (int m = msg; m < end && fmgs.Get("talk", m) is { } t; m++) parts.Add(Ds1Fmgs.OneLine(t));
            result.Add((rows[i].ID, msg, System.Convert.ToInt32(rows[i]["motionId"].Value), parts.Count > 0 ? string.Join(" ", parts) : $"(msg {msg} has no text)"));
        }
        return result;
    }

    static void Cond(StringBuilder sb, ESD.Condition c, string indent, Ds1Fmgs? fmgs, SortedDictionary<int, string> lines)
    {
        sb.AppendLine($"{indent}if {Esd.Expr(c.Evaluator, FnName)} -> {(c.TargetState?.ToString() ?? "-")}");
        foreach (var p in c.PassCommands) sb.AppendLine($"{indent}  pass {Cmd(p, fmgs, lines)}");
        foreach (var sub in c.Subconditions) Cond(sb, sub, indent + "  ", fmgs, lines);
    }

    static int PrintNames()
    {
        Console.WriteLine("# DS1 talk ESD names (Mappings\\ds1-talk-esd.tsv): kind id name(args) status evidence");
        foreach (var n in Names.Values.OrderBy(n => n.Kind).ThenBy(n => n.Id))
            Console.WriteLine($"{n.Kind,-3} {n.Id,4} {n.Text}({string.Join(", ", n.Args)}) [{n.Status}] {n.Evidence}");
        return 0;
    }

    static int List()
    {
        var users = TalkUsers();
        foreach (var (map, talk, bytes) in AllTalks())
        {
            var esd = ESD.Read(bytes);
            var u = users.GetValueOrDefault(talk);
            Console.WriteLine($"{map} t{talk,-7} {bytes.Length,7} bytes groups={esd.StateGroups.Count} states={esd.StateGroups.Values.Sum(g => g.Count),-4} {(u == null ? "(no MSB part uses it)" : string.Join("; ", u))}");
        }
        return 0;
    }

    static IEnumerable<ESD.Condition> Flatten(IEnumerable<ESD.Condition> cs) => cs.SelectMany(c => new[] { c }.Concat(Flatten(c.Subconditions)));

    /// <summary>Evidence for the name table: per command/function id, uses, arguments and comparisons.</summary>
    static int Survey(int max)
    {
        var fmgs = Ds1Fmgs.Load(Program.DefaultDsrRoot);
        var store = Ds1Talk.store.Value;
        var cmdUses = new Dictionary<(int, int), List<(int Talk, List<string> Args)>>();
        var cmdStates = new Dictionary<(int, int), List<string>>(); // debug labels (c1_0) of the states using a command
        var fnStates = new Dictionary<int, List<string>>();
        var fnUses = new Dictionary<int, List<(int Talk, string Args, string Cmp)>>();
        var call = new Regex(@"(?<![\w])f(\d+)\(([^()]*)\)(?:!)?(?:\s*(==|!=|<=|>=|<|>)\s*(-?[\d.]+d?f?))?");
        int scripts = 0;
        foreach (var (map, talk, bytes) in AllTalks())
        {
            scripts++;
            var esd = ESD.Read(bytes);
            foreach (var s in esd.StateGroups.Values.SelectMany(g => g.Values))
            {
                var conds = Flatten(s.Conditions).ToList();
                string label = s.EntryCommands.Where(c => c.CommandBank == 1 && c.CommandID == 0 && c.Arguments.Count == 1).Select(c => Esd.Expr(c.Arguments[0])).FirstOrDefault() ?? "";
                foreach (var c in s.EntryCommands.Concat(s.WhileCommands).Concat(s.ExitCommands).Concat(conds.SelectMany(c => c.PassCommands)))
                {
                    var key = (c.CommandBank, c.CommandID);
                    if (!cmdUses.TryGetValue(key, out var l)) cmdUses[key] = l = [];
                    l.Add((talk, c.Arguments.Select(a => Esd.Expr(a)).ToList()));
                    if (label != "" && key != (1, 0)) { if (!cmdStates.TryGetValue(key, out var ls)) cmdStates[key] = ls = []; ls.Add(label); }
                }
                var exprs = conds.Select(c => Esd.Expr(c.Evaluator)).Concat(s.EntryCommands.Concat(s.WhileCommands).Concat(s.ExitCommands).SelectMany(c => c.Arguments).Select(a => Esd.Expr(a)));
                foreach (string e in exprs)
                    foreach (Match m in call.Matches(e))
                    {
                        int id = int.Parse(m.Groups[1].Value);
                        if (!fnUses.TryGetValue(id, out var l)) fnUses[id] = l = [];
                        l.Add((talk, m.Groups[2].Value, m.Groups[3].Success ? $"{m.Groups[3].Value} {m.Groups[4].Value}" : ""));
                        if (label != "") { if (!fnStates.TryGetValue(id, out var ls)) fnStates[id] = ls = []; ls.Add(label); }
                    }
            }
        }
        Console.WriteLine($"# {scripts} DS1 talk scripts; commands (bank:id) then functions");
        foreach (var ((bank, id), uses) in cmdUses.OrderBy(k => k.Key))
        {
            Console.WriteLine($"cmd {bank}:{id,-4} {CmdName(bank, id),-28} uses={uses.Count,-5} talks={uses.Select(u => u.Talk).Distinct().Count(),-4} argc={string.Join("/", uses.Select(u => u.Args.Count).Distinct().Order())}");
            int argc = uses.Max(u => u.Args.Count);
            for (int i = 0; i < argc; i++)
            {
                var vals = uses.Where(u => u.Args.Count > i).Select(u => u.Args[i]).ToList();
                Console.WriteLine($"      arg{i}: {Classify(vals, fmgs, store)}  e.g. {string.Join(", ", vals.Distinct().Take(max))}");
            }
            if (cmdStates.TryGetValue((bank, id), out var labels)) Console.WriteLine($"      in states: {Top(labels, max)}");
        }
        foreach (var (id, uses) in fnUses.OrderBy(k => k.Key))
        {
            var args = uses.Select(u => u.Args).Where(a => a != "").ToList();
            Console.WriteLine($"fn  {id,-4} {FnName(id) ?? $"f{id}",-28} uses={uses.Count,-5} talks={uses.Select(u => u.Talk).Distinct().Count(),-4} args e.g. [{string.Join("] [", args.Distinct().Take(max))}]");
            var argVals = args.SelectMany(a => a.Split(", ")).ToList();
            if (argVals.Count > 0) Console.WriteLine($"      args: {Classify(argVals, fmgs, store)}");
            var cmps = uses.Select(u => u.Cmp).Where(c => c != "").GroupBy(c => c).OrderByDescending(g => g.Count()).Take(max);
            Console.WriteLine($"      compared: {string.Join(", ", cmps.Select(g => $"{g.Key} ×{g.Count()}"))}");
            if (fnStates.TryGetValue(id, out var labels)) Console.WriteLine($"      in states: {Top(labels, max)}");
        }
        return 0;
    }

    static string Top(List<string> labels, int max) =>
        string.Join(", ", labels.GroupBy(l => l).OrderByDescending(g => g.Count()).Take(max).Select(g => $"{g.Key} ×{g.Count()}"));

    /// <summary>Which tables a set of constant argument values falls in (counts of distinct values).</summary>
    static string Classify(List<string> vals, Ds1Fmgs fmgs, ParamStore store)
    {
        var ints = vals.Select(v => int.TryParse(v, out int x) ? (int?)x : null).Where(x => x is > 0).Select(x => x!.Value).Distinct().ToList();
        int strs = vals.Count(v => v.StartsWith('"'));
        var parts = new List<string>();
        if (strs > 0) parts.Add($"strings {strs}");
        if (ints.Count == 0) return parts.Count > 0 ? string.Join(", ", parts) : $"no positive constants ({vals.Distinct().Count()} distinct)";
        parts.Add($"{ints.Count} distinct >0 in {ints.Min()}..{ints.Max()}");
        foreach (var (label, sel) in new[] { ("talk", "talk"), ("event", "event"), ("menu", "menu"), ("dialogue", "dialogue"), ("system", "system"), ("npc", "npc") })
        {
            int n = ints.Count(v => fmgs.Hits(v, fmgs.Select(sel)).Any());
            if (n > 0) parts.Add($"{label} {n}");
        }
        foreach (string p in new[] { "TalkParam", "ShopLineupParam", "EquipParamGoods", "ItemLotParam", "NpcParam", "BonfireWarpParam" })
        {
            if (!store.Params.TryGetValue(p, out var param)) continue;
            int n = ints.Count(v => param[v] != null);
            if (n > 0) parts.Add($"{p} {n}");
        }
        return string.Join(", ", parts);
    }
}
