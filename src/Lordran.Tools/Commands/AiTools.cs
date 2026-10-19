using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Lordran.Tools.Build;
using Lordran.Tools.Build.Ai;
using Lordran.Tools.Build.Chr;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// DS1 Lua AI → DS2 AI ESD tools (session 8, track enemies step 2):
/// <list type="bullet">
/// <item><c>ai-goal GOAL [--think T] [--fn NAME] [--max N]</c>: evaluate a DS1 battle goal symbolically and print its paths
/// (decisions → sub-goal queue), assumptions and notes.</item>
/// <item><c>ai-translate KEY THINK... [--out DIR]</c>: translate DS1 thinks for 1:1 character KEY (c2542 …): writes
/// <c>aiHIGH.esd</c> / <c>aiLOW.esd</c>, <c>coverage-THINK.txt</c> and the battle machine dump to DIR (default
/// <c>data\scratch\ai\KEY</c>).</item>
/// <item><c>ai-check PACKDIR</c>: offline checks of every generated AI ESD in a built pack (see <see cref="Check"/>).</item>
/// <item><c>ai-roundtrip [DIR]</c>: every vanilla AI ESD read and written back by SoulsFormats must be byte-identical (the
/// translator writes the template lower through it).</item>
/// </list>
/// </summary>
public static class AiTools
{
    static string Ezstate => Path.Combine(ExtractDs2.DefaultOut, "ezstate");

    public static int Goal(string[] args)
    {
        int goalId = int.Parse(args[1], CultureInfo.InvariantCulture);
        int think = 0, max = 400; string? fn = null, map = null; double? code = null;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--think") think = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--fn") fn = args[++i];
            else if (args[i] == "--max") max = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--code") code = double.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--map") map = args[++i];
        }
        var thinkRow = think > 0 ? Ds1Source.LoadParam(Program.DefaultDsrRoot, "NpcThinkParam")?[think] : null;
        var g = new Ds1Goal(goalId, thinkRow, null, map);
        Console.WriteLine($"DS1 battle goal {goalId}: {g.ScriptPath} ({g.GoalName}), think {think}");
        foreach (var name in fn != null ? [fn] : new[] { g.ActivateName, g.InteruptName })
        {
            if (!g.HasFunction(name)) { Console.WriteLine($"{name}: not defined"); continue; }
            var run = code is double c ? g.Evaluate(name, c) : g.Evaluate(name);
            Console.WriteLine($"=== {name}: {run.Paths.Count} paths");
            foreach (var e in run.Errors) Console.WriteLine($"  ERROR {e}");
            foreach (var p in run.Paths.Take(max))
            {
                Console.WriteLine($"  [{string.Join(" ∧ ", p.Literals)}]");
                foreach (var e in p.Effects) Console.WriteLine($"      {e}");
                Console.WriteLine($"      return {Ds1Goal.F(p.Return)}; rolls {p.Rands.Count}");
            }
            foreach (var n in run.Paths.SelectMany(p => p.Notes).Distinct()) Console.WriteLine($"  note: {n}");
        }
        foreach (var (k, v) in g.Assumptions) Console.WriteLine($"assumption ×{v}: {k}");
        return 0;
    }

    public static int Translate(string[] args)
    {
        string key = args[1];
        var thinks = new List<int>();
        // merge9 (stage 3): the default was data\sandbox\enemies\work\ai\KEY (the track's sandbox); once merged into src the
        // tool must not write into a track sandbox by default, so it goes to the shared scratch folder (--out still overrides)
        string outDir = Path.Combine("data", "scratch", "ai", key);
        int high = 103010, low = 103000;
        string? ds1Map = null;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--out") outDir = args[++i];
            else if (args[i] == "--map") ds1Map = args[++i];
            else if (args[i] == "--template") { high = int.Parse(args[++i]); low = int.Parse(args[++i]); }
            else if (args[i] == "--unread-interrupts") Ds2AiMachines.UnreadInterrupts = true;   // as build-pack --ds1-ai-unread-interrupts
            else thinks.Add(int.Parse(args[i], CultureInfo.InvariantCulture));
        }
        Directory.CreateDirectory(outDir);
        var (o, rig) = ChrPorter.FindRig(key);
        var map = AiTranslator.MapOf(rig.Attacks);
        var thinkParam = Ds1Source.LoadParam(Program.DefaultDsrRoot, "NpcThinkParam") ?? throw new InvalidDataException("DSR NpcThinkParam");
        int fails = 0;
        foreach (int t in thinks)
        {
            var row = thinkParam[t] ?? throw new InvalidDataException($"DS1 NpcThinkParam {t} missing");
            var res = AiTranslator.Translate(row, map, high, low, Ezstate, null, ds1Map);
            File.WriteAllBytes(Path.Combine(outDir, $"ai{res.High:D6}.esd"), res.UpperEsd);
            File.WriteAllBytes(Path.Combine(outDir, $"ai{res.Low:D6}.esd"), res.LowerEsd);
            File.WriteAllText(Path.Combine(outDir, $"coverage-{t}.txt"), AiTranslator.CoverageText(res));
            var esd = ESD.Read(res.LowerEsd);
            var dump = new StringBuilder();
            foreach (var gid in res.Machines.Values.Concat(res.BattleGroups).Order()) DumpGroup(dump, esd, gid);
            File.WriteAllText(Path.Combine(outDir, $"ai{res.Low:D6}-generated.txt"), dump.ToString());
            // the battle machine before the group split (one group; think = state {res.ThinkState})
            var pre = new ESD(); pre.StateGroups[res.BattleMachine] = res.Machine;
            var preDump = new StringBuilder();
            DumpGroup(preDump, pre, res.BattleMachine);
            File.WriteAllText(Path.Combine(outDir, $"ai{res.Low:D6}-battle-unsplit.txt"), preDump.ToString());
            int copies = res.Split.Copies.Values.Sum(l => l.Count);
            Console.WriteLine($"group split: {res.Machine.Count} states → {copies} copies + {res.Split.Stubs.Count} stubs in {res.BattleGroups.Count} groups; most copied: "
                + string.Join(", ", res.Split.Copies.OrderByDescending(kv => kv.Value.Count).Take(12).Select(kv => $"{kv.Key}×{kv.Value.Count}")));
            foreach (var g in res.BattleGroups)
                Console.WriteLine($"  group {g}: {res.Split.Groups[g].Count} states = {res.Split.OrigOf.Keys.Count(k => k.Group == g)} copies + {res.Split.Stubs.Keys.Count(k => k.Group == g)} stubs, "
                    + $"{(res.Split.Entries.TryGetValue(g, out var es) ? es.Count : 1)} entr(y/ies)");
            Console.WriteLine(res.Report[0]);
            Console.Write(AiTranslator.CoverageText(res));
            var problems = CheckEsd(esd, map, res.BattleGroups, res.Machines.Values.ToHashSet(), out var stats);
            Console.WriteLine($"check ai{res.Low:D6}: {stats}; {problems.Count} problem(s)");
            foreach (var p in problems) Console.WriteLine("  FAIL " + p);
            fails += problems.Count;
        }
        return fails == 0 ? 0 : 1;
    }

    public static void DumpGroup(StringBuilder sb, ESD esd, long gid)
    {
        if (!esd.StateGroups.TryGetValue(gid, out var states)) return;
        sb.AppendLine($"group {gid}: {states.Count} states");
        foreach (var (sid, s) in states.OrderBy(s => s.Key))
        {
            sb.AppendLine($"  state {sid}");
            foreach (var c in s.EntryCommands) sb.AppendLine($"    entry {Cmd(c)}");
            foreach (var c in s.WhileCommands) sb.AppendLine($"    while {Cmd(c)}");
            foreach (var c in s.ExitCommands) sb.AppendLine($"    exit  {Cmd(c)}");
            foreach (var c in s.Conditions) Cond(sb, c, "    ");
        }
    }
    static string Cmd(ESD.CommandCall c) => $"{c.CommandBank}:{c.CommandID}({string.Join(", ", c.Arguments.Select(Esd.Expr))})";
    static void Cond(StringBuilder sb, ESD.Condition c, string indent)
    {
        sb.AppendLine($"{indent}if {Esd.Expr(c.Evaluator)} -> {(c.TargetState?.ToString() ?? "-")}");
        foreach (var p in c.PassCommands) sb.AppendLine($"{indent}  pass {Cmd(p)}");
        foreach (var sub in c.Subconditions) Cond(sb, sub, indent + "  ");
    }

    /// <summary>
    /// Offline checks of a generated lower-logic ESD:
    /// <list type="bullet">
    /// <item>every evaluator and argument decodes (known opcodes, ends in A1), every condition target exists in its group,
    /// every group has a state 0 with no entry commands and at most 8 parameters are read (p0-p7);</item>
    /// <item>every machine call (bank 6) names an existing group and passes as many arguments as the callee reads;</item>
    /// <item>random slots 1:1 / f1 and timers 1:2 / f2 are 0-3; every <c>f1(slot)</c> a generated state reads (condition or
    /// argument) is rolled before it on every path into it — over the battle machine's groups as one machine, a stub passing its
    /// rolls to the entry it calls (slots are per logic: vanilla groups read slots their callers roll);</item>
    /// <item>every constant action the generated groups request (5204 / attack-machine argument) is an action of the
    /// character's plan (a category exists in its network), and every action reachable from the template's weapon groups
    /// is either such an action, −1, or reported;</item>
    /// <item>no unreachable states in the generated groups; state ids 0..N-1;</item>
    /// <item>fix pass: the whole file inside the vanilla envelope (<see cref="VanillaEnvelope"/>: groups per file, states per
    /// group, conditions / subconditions / evaluator bytes per state and condition, commands per state, arguments); every weapon
    /// group's engaged modes (<see cref="AiTranslator.EngagedModes"/>) call the battle machine and nothing else does; no
    /// <c>f4</c> in a generated group; <c>f5050(n)</c> only for interrupts with a DS2 form (rule AI5b); the first DS1 timer's DS2
    /// slot is one the template never resets (rule AI5c).</item>
    /// </list>
    /// </summary>
    public static List<string> CheckEsd(ESD esd, GoalCompiler.ActionMap map, IReadOnlyList<long> battleGroups, HashSet<long> machines, out string stats)
    {
        var problems = new List<string>();
        var actions = map.Actions.Values.ToHashSet();
        foreach (var c in map.Chains) actions.Add(50 + c.Row);
        int conds = 0, cmds = 0, calls = 0;
        long battle = battleGroups[0];
        var battleSet = battleGroups.ToHashSet();
        // parameter counts per group
        var paramCount = new Dictionary<long, int>();
        foreach (var (gid, states) in esd.StateGroups)
        {
            int maxP = -1;
            foreach (var s in states.Values)
                foreach (var b in AllExprs(s)) foreach (int p in Params(b)) maxP = Math.Max(maxP, p);
            paramCount[gid] = maxP + 1;
            if (maxP >= 8) problems.Add($"group {gid}: reads p{maxP} (machines take at most 8 arguments)");
        }
        var generated = machines.Concat(battleGroups).ToHashSet();
        foreach (var (gid, states) in esd.StateGroups)
        {
            if (!states.ContainsKey(0)) problems.Add($"group {gid}: no state 0");
            if (states.Count < 2) problems.Add($"group {gid}: {states.Count} state (DS2 runs nothing from an ESD with a one-state group, session 5)");
            else if (states[0].EntryCommands.Count > 0 && generated.Contains(gid)) problems.Add($"group {gid}: state 0 has entry commands");
            foreach (var (sid, s) in states)
            {
                string at = $"group {gid} state {sid}";
                foreach (var c in s.EntryCommands.Concat(s.WhileCommands).Concat(s.ExitCommands))
                {
                    cmds++;
                    foreach (var a in c.Arguments) if (!Decodes(a)) problems.Add($"{at}: {c.CommandBank}:{c.CommandID} argument does not decode [{Convert.ToHexString(a)}]");
                    if (c.CommandBank == 6)
                    {
                        calls++;
                        long callee = (long)(uint)c.CommandID;
                        if (!esd.StateGroups.ContainsKey(callee)) problems.Add($"{at}: calls missing group {callee}");
                        else if (c.Arguments.Count < paramCount[callee]) problems.Add($"{at}: calls {callee} with {c.Arguments.Count} arguments, it reads {paramCount[callee]}");
                    }
                    if (generated.Contains(gid))
                    {
                        var argList = c.CommandBank == 5 ? c.Arguments.Skip(1).ToList() : c.Arguments;
                        if (c.CommandID is 1 or 2 && c.CommandBank == 1 && AiTranslator.Const(c.Arguments[0]) is int sl && sl is < 0 or > 3) problems.Add($"{at}: slot {sl} out of 0-3");
                        if (c.CommandID == 5204 && argList.Count > 0 && AiTranslator.Const(argList[0]) is int act && act >= 0 && !actions.Contains(act))
                            problems.Add($"{at}: requests action {act}, not an action of this character's plan");
                        if (c.CommandBank == 6 && (long)(uint)c.CommandID == machinesAttack(machines) && c.Arguments.Count > 0 && AiTranslator.Const(c.Arguments[0]) is int a2 && !actions.Contains(a2))
                            problems.Add($"{at}: attack machine with action {a2}, not an action of this character's plan");
                    }
                }
                foreach (var cond in Flatten(s.Conditions))
                {
                    conds++;
                    if (!Decodes(cond.Evaluator)) problems.Add($"{at}: condition does not decode [{Convert.ToHexString(cond.Evaluator)}]");
                    if (cond.TargetState is long t && !states.ContainsKey(t)) problems.Add($"{at}: condition targets missing state {t}");
                    foreach (var p in cond.PassCommands) foreach (var a in p.Arguments) if (!Decodes(a)) problems.Add($"{at}: pass command argument does not decode");
                    if (generated.Contains(gid)) foreach (int sl in RandReads(cond.Evaluator)) if (sl is < 0 or > 3) problems.Add($"{at}: reads random slot {sl}");
                }
            }
        }
        // generated groups: reachability and random slots rolled before read. The battle groups form one machine: a stub
        // (6:G(k) to another battle group) hands its rolls to the entry k of G (its dispatcher condition p0 == k).
        int unreachable = 0, slotReads = 0;
        var rolledIn = new Dictionary<(long G, long S), HashSet<int>>();
        var work = new Queue<(long G, long S)>();
        var stubSeed = new Dictionary<(long G, int K), HashSet<int>>();
        void Seed((long G, long S) node, HashSet<int> have)
        {
            if (!rolledIn.TryGetValue(node, out var prev)) { rolledIn[node] = [.. have]; work.Enqueue(node); }
            else { int before = prev.Count; prev.IntersectWith(have); if (prev.Count != before) work.Enqueue(node); }
        }
        static int? DispatchK(byte[] b) => Regex.Match(Esd.Expr(b), @"^\(p0 == (\d+)\)$") is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;
        foreach (long gid in generated)
        {
            if (!esd.StateGroups.ContainsKey(gid)) { problems.Add($"generated group {gid} missing"); continue; }
            if (!battleSet.Contains(gid) || gid == battle) Seed((gid, 0), []);
        }
        while (work.Count > 0)
        {
            var (gid, sid) = work.Dequeue();
            if (!esd.StateGroups[gid].TryGetValue(sid, out var s)) continue;
            var have = new HashSet<int>(rolledIn[(gid, sid)]);
            foreach (var c in s.EntryCommands)
            {
                foreach (var a in c.Arguments) foreach (int sl in RandReads(a)) { slotReads++; if (!have.Contains(sl)) problems.Add($"group {gid} state {sid}: {c.CommandBank}:{c.CommandID} argument reads f1({sl}) not rolled before it"); }
                if (c.CommandBank == 1 && c.CommandID == 1 && AiTranslator.Const(c.Arguments[0]) is int sl2) have.Add(sl2);
                if (c.CommandBank == 6 && battleSet.Contains((long)(uint)c.CommandID) && (long)(uint)c.CommandID != gid)
                {
                    long callee = (long)(uint)c.CommandID;
                    int k = c.Arguments.Count > 0 && AiTranslator.Const(c.Arguments[0]) is int kk ? kk : 0;
                    if (!stubSeed.TryGetValue((callee, k), out var prev)) { stubSeed[(callee, k)] = [.. have]; }
                    else { int before = prev.Count; prev.IntersectWith(have); if (prev.Count == before) continue; }
                    foreach (var ds in esd.StateGroups[callee].Where(x => x.Key == 0 || x.Value.Conditions.Any(cc => DispatchK(cc.Evaluator) != null)).Select(x => x.Key))
                        if (rolledIn.ContainsKey((callee, ds))) work.Enqueue((callee, ds)); else Seed((callee, ds), have);
                }
            }
            foreach (var c in Flatten(s.Conditions))
            {
                foreach (int sl in RandReads(c.Evaluator)) { slotReads++; if (!have.Contains(sl)) problems.Add($"group {gid} state {sid}: reads f1({sl}) not rolled on every path into it"); }
                if (c.TargetState is not long t) continue;
                if (battleSet.Contains(gid) && DispatchK(c.Evaluator) is int k) { if (stubSeed.TryGetValue((gid, k), out var seed)) Seed((gid, t), seed); }
                else if (battleSet.Contains(gid) && gid != battle && sid == 0 && s.Conditions.Count == 1 && stubSeed.TryGetValue((gid, 0), out var seed0)) Seed((gid, t), seed0);
                else Seed((gid, t), have);
            }
        }
        foreach (long gid in generated.Where(esd.StateGroups.ContainsKey))
        {
            var states = esd.StateGroups[gid];
            foreach (long sid in states.Keys) if (!rolledIn.ContainsKey((gid, sid))) { unreachable++; problems.Add($"group {gid}: state {sid} unreachable"); }
            if (!states.Keys.Order().SequenceEqual(Enumerable.Range(0, states.Count).Select(i => (long)i))) problems.Add($"group {gid}: state ids are not 0..{states.Count - 1}");
        }
        // actions requested anywhere reachable from the weapon groups (template machines with constant/parameter arguments)
        var requested = new SortedSet<int>();
        var seen = new HashSet<string>();
        void Walk(long gid, int?[] pargs)
        {
            string k = gid + ":" + string.Join(",", pargs);
            if (!seen.Add(k) || !esd.StateGroups.TryGetValue(gid, out var states)) return;
            foreach (var s in states.Values)
                foreach (var c in s.EntryCommands.Concat(s.WhileCommands).Concat(s.ExitCommands))
                {
                    var argList = c.CommandBank == 5 ? c.Arguments.Skip(1).ToList() : c.Arguments;
                    int? Val(byte[] b) => AiTranslator.Const(b) is int v ? v : ParamOnly(b) is int p && p < pargs.Length ? pargs[p] : null;
                    if (c.CommandID == 5204 && c.CommandBank is 1 or 5 && argList.Count > 0 && Val(argList[0]) is int act) requested.Add(act);
                    if (c.CommandBank == 6)
                    {
                        long callee = (long)(uint)c.CommandID;
                        if (generated.Contains(callee)) continue;
                        Walk(callee, [.. c.Arguments.Select(Val)]);
                    }
                }
        }
        foreach (var gid in esd.StateGroups.Keys.Where(g => g is > 0 and < 100)) Walk(gid, []);
        foreach (int a in requested.Where(a => a >= 0 && !actions.Contains(a) && a is not (60 or 61)))
            problems.Add($"template machines reachable from the weapon groups request action {a}, which this character has no category for");
        // fix pass: engaged modes → battle machine (and only they)
        var notEngaged = E.And(AiTranslator.EngagedModes.Select(m => E.Fn(5900, E.I(0)).Eq(m).Ne(1))).Bytes();
        foreach (var (gid, states) in esd.StateGroups.Where(g => g.Key is > 0 and < 100 && g.Value.Values.Any(s => s.Conditions.Any(c => Regex.IsMatch(Esd.Expr(c.Evaluator), @"f5900\(0\) == \d+\) != 1")))))
        {
            int routed = 0;
            foreach (var (sid, s) in states)
            {
                bool callsBattle = s.EntryCommands.Any(c => c.CommandBank == 6 && battleSet.Contains((long)(uint)c.CommandID));
                if (!callsBattle) continue;
                if ((long)(uint)s.EntryCommands.Single(c => c.CommandBank == 6).CommandID != battle) problems.Add($"weapon group {gid} state {sid}: calls a battle sub-group directly");
                if (!s.Conditions.Any(c => c.Evaluator.AsSpan().SequenceEqual(notEngaged))) problems.Add($"weapon group {gid} state {sid}: calls the battle machine without the engaged-mode exit test");
                routed++;
            }
            if (routed != AiTranslator.EngagedModes.Length) problems.Add($"weapon group {gid}: {routed} states run the battle machine (expected one per engaged mode {string.Join("/", AiTranslator.EngagedModes)})");
        }
        // fix pass: no f4 in generated groups, f5050 only with a DS2 form, DS1 timer slots
        var timerSlotsUsed = new SortedSet<int>();
        foreach (long gid in generated.Where(esd.StateGroups.ContainsKey))
            foreach (var (sid, s) in esd.StateGroups[gid])
                foreach (var b in AllExprs(s))
                {
                    string e = Esd.Expr(b);
                    if (Regex.IsMatch(e, @"\bf4\(")) problems.Add($"group {gid} state {sid}: f4 in a generated expression ({e}): rule AI5d");
                    foreach (Match m in Regex.Matches(e, @"f5050\((\d+)\)")) if (!Ds2AiMachines.InterruptHasDs2Form(int.Parse(m.Groups[1].Value))) problems.Add($"group {gid} state {sid}: f5050({m.Groups[1].Value}) has no DS2 form (rule AI5b)");
                    if (battleSet.Contains(gid)) foreach (Match m in Regex.Matches(e, @"\bf2\((\d)\)")) timerSlotsUsed.Add(int.Parse(m.Groups[1].Value));
                }
        var templateOnly = new ESD();
        foreach (var (gid, g) in esd.StateGroups.Where(g => !generated.Contains(g.Key))) templateOnly.StateGroups[gid] = g;
        var slotOrder = Ds2AiMachines.TimerSlotOrder(templateOnly);
        if (timerSlotsUsed.Count > 0 && !timerSlotsUsed.Contains(slotOrder[0].Slot)) problems.Add($"DS1 timers use DS2 slots [{string.Join(", ", timerSlotsUsed)}] but not slot {slotOrder[0].Slot}, the one the template resets least");
        string timerNote = timerSlotsUsed.Count == 0 ? "no DS1 timers" : $"DS1 timer slots [{string.Join(", ", timerSlotsUsed)}] (template resets: {string.Join(", ", slotOrder.Select(x => $"{x.Slot}×{x.Resets}"))})";
        // fix pass: the vanilla envelope
        var env = Envelope(esd);
        var van = VanillaEnvelope();
        foreach (var (k, v) in env) if (v > van[k]) problems.Add($"envelope: {k} = {v}, over vanilla's maximum {van[k]}");
        stats = $"{esd.StateGroups.Count} groups ({battleGroups.Count} battle), {esd.StateGroups.Sum(g => g.Value.Count)} states, {cmds} commands, {conds} conditions, {calls} machine calls, {slotReads} random reads checked, "
            + $"template-reachable actions [{string.Join(", ", requested)}]; {timerNote}; envelope {string.Join(", ", env.Select(kv => $"{kv.Key} {kv.Value}/{van[kv.Key]}"))}";
        return problems;

        static long machinesAttack(HashSet<long> ms) => ms.Max();
    }

    /// <summary>Structural maxima of one ESD (the quantities validate\esd_envelope.py measures).</summary>
    public static SortedDictionary<string, int> Envelope(ESD esd)
    {
        var r = new SortedDictionary<string, int>
        {
            ["groups"] = esd.StateGroups.Count, ["states/group"] = 0, ["conditions/state"] = 0, ["subconditions/condition"] = 0,
            ["bytes/condition"] = 0, ["bytes/state"] = 0, ["commands/state"] = 0, ["arguments/command"] = 0,
        };
        void Max(string k, int v) { if (v > r[k]) r[k] = v; }
        // machine call depth: the longest chain of 6:G calls from a top group (id < 2000000000; a top group counts 1)
        var callees = esd.StateGroups.ToDictionary(g => g.Key, g => g.Value.Values.SelectMany(s => s.EntryCommands.Concat(s.WhileCommands).Concat(s.ExitCommands))
            .Where(c => c.CommandBank == 6).Select(c => (long)(uint)c.CommandID).Where(esd.StateGroups.ContainsKey).ToHashSet());
        var depthMemo = new Dictionary<long, int>();
        int Depth(long g, HashSet<long> stack)
        {
            if (depthMemo.TryGetValue(g, out int d)) return d;
            if (!stack.Add(g)) return 0;
            d = 1 + callees[g].Select(c => Depth(c, stack)).DefaultIfEmpty(0).Max();
            stack.Remove(g);
            return depthMemo[g] = d;
        }
        r["call depth"] = esd.StateGroups.Keys.Where(g => g < 2_000_000_000).Select(g => Depth(g, [])).DefaultIfEmpty(0).Max();
        foreach (var g in esd.StateGroups.Values)
        {
            Max("states/group", g.Count);
            foreach (var s in g.Values)
            {
                Max("conditions/state", s.Conditions.Count);
                Max("commands/state", s.EntryCommands.Count + s.WhileCommands.Count + s.ExitCommands.Count);
                foreach (var c in s.EntryCommands.Concat(s.WhileCommands).Concat(s.ExitCommands)) Max("arguments/command", c.Arguments.Count);
                Max("bytes/state", s.Conditions.Sum(AiTranslator.CondBytes));
                foreach (var c in Flatten(s.Conditions)) { Max("subconditions/condition", c.Subconditions.Count); Max("bytes/condition", c.Evaluator.Length); }
            }
        }
        return r;
    }

    static SortedDictionary<string, int>? vanillaEnvelope;
    /// <summary>The maxima over all vanilla AI ESDs (data\ds2\ezstate\ai*.esd: 485 files): 133 groups, 93 states per group,
    /// 31 conditions and 1768 evaluator bytes per state, 1 subcondition, 209 bytes per condition, 15 commands, 18 arguments,
    /// machine call depth 8.</summary>
    public static SortedDictionary<string, int> VanillaEnvelope()
    {
        if (vanillaEnvelope != null) return vanillaEnvelope;
        SortedDictionary<string, int>? acc = null;
        foreach (var f in Directory.GetFiles(Ezstate, "ai*.esd"))
        {
            var e = Envelope(ESD.Read(f));
            if (acc == null) { acc = e; continue; }
            foreach (var (k, v) in e) if (v > acc[k]) acc[k] = v;
        }
        return vanillaEnvelope = acc ?? throw new InvalidDataException($"no vanilla AI ESDs in {Ezstate}");
    }

    static IEnumerable<byte[]> AllExprs(ESD.State s)
    {
        foreach (var c in s.EntryCommands.Concat(s.WhileCommands).Concat(s.ExitCommands)) foreach (var a in c.Arguments) yield return a;
        foreach (var c in Flatten(s.Conditions)) { yield return c.Evaluator; foreach (var p in c.PassCommands) foreach (var a in p.Arguments) yield return a; }
    }
    static IEnumerable<ESD.Condition> Flatten(IEnumerable<ESD.Condition> cs) => cs.SelectMany(c => new[] { c }.Concat(Flatten(c.Subconditions)));

    static IEnumerable<int> Params(byte[] b)
    {
        for (int i = 0; i + 1 < b.Length; i++)
        {
            if (b[i] is 0x80 or 0x82) { i += 4; continue; }
            if (b[i] == 0x81) { i += 8; continue; }
            if (b[i] < 0x80 && b[i + 1] == 0xB8) yield return b[i] - 0x40;
        }
    }
    static int? ParamOnly(byte[] b) => b.Length == 3 && b[0] < 0x80 && b[1] == 0xB8 && b[2] == 0xA1 ? b[0] - 0x40 : null;

    /// <summary>Slots of every f1(slot) read (push 1, push slot, call1).</summary>
    static IEnumerable<int> RandReads(byte[] b)
    {
        for (int i = 0; i + 2 < b.Length; i++)
        {
            if (b[i] is 0x80 or 0x82) { i += 4; continue; }
            if (b[i] == 0x81) { i += 8; continue; }
            if (b[i] == 0x41 && b[i + 1] < 0x80 && b[i + 2] == 0x85) yield return b[i + 1] - 0x40;
        }
    }

    static bool Decodes(byte[] b)
    {
        for (int i = 0; i < b.Length; i++)
        {
            byte op = b[i];
            if (op < 0x80) continue;
            switch (op)
            {
                case 0x80: i += 4; break;
                case 0x81: i += 8; break;
                case 0x82: i += 4; break;
                case >= 0x84 and <= 0x8A: break;
                case >= 0x8C and <= 0x9A: break;
                case 0xA1: return i == b.Length - 1;
                case 0xA5: while (i + 2 < b.Length && (b[i + 1] != 0 || b[i + 2] != 0)) i += 2; i += 2; break;
                case >= 0xA6 and <= 0xBA: break;
                default: return false;
            }
        }
        return false;
    }

    /// <summary>ai-check PACKDIR: every generated AI ESD of a pack (the lower logics listed by the pack's ai-manifest.txt).</summary>
    public static int Check(string[] args)
    {
        string pack = args[1];
        string manifest = Path.Combine(pack, "lordran-ai", "manifest.txt");
        if (!File.Exists(manifest)) { Console.WriteLine($"no {manifest}: the pack has no translated DS1 AI"); return 1; }
        int fails = 0, files = 0;
        foreach (var line in File.ReadAllLines(manifest).Where(l => !l.StartsWith('#') && l.Trim().Length > 0))
        {
            // low high battle-groups machines key
            var f = line.Split('\t');
            int low = int.Parse(f[0]), high = int.Parse(f[1]);
            var battle = f[2].Split(',').Select(long.Parse).ToList();
            var machines = f[3].Split(',').Select(long.Parse).ToHashSet();
            string key = f[4];
            var (_, rig) = ChrPorter.FindRig(key);
            var map = AiTranslator.MapOf(rig.Attacks);
            string lowPath = Path.Combine(pack, "gamedata", "ezstate", $"ai{low:D6}.esd"), highPath = Path.Combine(pack, "gamedata", "ezstate", $"ai{high:D6}.esd");
            if (!File.Exists(lowPath) || !File.Exists(highPath)) { Console.WriteLine($"FAIL ai{low:D6} / ai{high:D6} missing"); fails++; continue; }
            var esd = ESD.Read(lowPath);
            var problems = CheckEsd(esd, map, battle, machines, out var stats);
            files++;
            Console.WriteLine($"ai{low:D6} ({key}, upper ai{high:D6}): {stats}: {problems.Count} problem(s)");
            foreach (var p in problems) Console.WriteLine("  FAIL " + p);
            // the upper: the template's state machines unchanged; internal names carry the file ids
            var up = ESD.Read(highPath);
            var tpl = ESD.Read(Path.Combine(Ezstate, $"ai{Ds1Ai.TemplateHigh:D6}.esd"));
            var a = new StringBuilder(); var b = new StringBuilder();
            foreach (var g in up.StateGroups.Keys.Order()) DumpGroup(a, up, g);
            foreach (var g in tpl.StateGroups.Keys.Order()) DumpGroup(b, tpl, g);
            if (a.ToString() != b.ToString()) { Console.WriteLine($"  FAIL ai{high:D6}: upper differs from template ai{Ds1Ai.TemplateHigh:D6}"); fails++; }
            if (!(up.Name ?? "").StartsWith($"ai{high:D6}_") || !(esd.Name ?? "").StartsWith($"ai{low:D6}_")) { Console.WriteLine($"  FAIL internal names '{up.Name}' / '{esd.Name}' do not start with their file ids"); fails++; }
        }
        Console.WriteLine($"ai-check: {files} AI ESD pairs, {fails} problem(s)");
        return fails == 0 ? 0 : 1;
    }

    public static int Roundtrip(string[] args)
    {
        string dir = args.Length > 1 ? args[1] : Ezstate;
        int ok = 0, bad = 0, sem = 0;
        foreach (var f in Directory.GetFiles(dir, "ai*.esd").OrderBy(f => f))
        {
            var b = File.ReadAllBytes(f);
            var esd = ESD.Read(b);
            var w = esd.Write();
            if (w.AsSpan().SequenceEqual(b)) { ok++; continue; }
            bad++;
            // semantic: re-read and compare dumps
            var a = new StringBuilder(); var c = new StringBuilder();
            var esd2 = ESD.Read(w);
            foreach (var g in esd.StateGroups.Keys.Order()) DumpGroup(a, esd, g);
            foreach (var g in esd2.StateGroups.Keys.Order()) DumpGroup(c, esd2, g);
            if (a.ToString() == c.ToString()) sem++;
            else Console.WriteLine($"{Path.GetFileName(f)}: differs after rewrite (content)");
        }
        Console.WriteLine($"ai-roundtrip: {ok} byte-identical, {bad} rewritten differently ({sem} of them with identical content)");
        return bad - sem == 0 ? 0 : 1;
    }
}
