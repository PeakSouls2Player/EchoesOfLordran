using System.Globalization;
using Lordran.Tools.Build;
using Lordran.Tools.Build.Ai;
using Lordran.Tools.Build.Chr;
using SoulsFormats;
using static Lordran.Tools.Build.Ai.LuaEval;

namespace Lordran.Tools.Commands;

/// <summary>
/// ai-verify KEY THINK... [--map M] [--unread-interrupts] [--legacy-ops] — session 8, track enemies step 2 (+ fix pass): the generated DS2 battle machine against
/// DS1's own code, path by path, on the ESD as written.
/// <list type="number">
/// <item><b>Split</b>: every state the <see cref="GroupSplit"/> copied into a group has the original's commands and condition
/// evaluators, every condition target leads to a copy of the original target — directly, through a stub calling the group that
/// has it as entry k (and that group's dispatcher with p0 = k reaches it), or, for "back to think", the main group's think / a
/// sub-group's return.</item>
/// <item><b>Think</b>: for every path of DS1's <c>_Activate</c> (from <see cref="Ds1Goal"/>) a concrete situation is built
/// that satisfies its decisions (target distance / height, HP, timers, each random roll's value inside its interval); the
/// written ESD is then run on it by a small ESD interpreter (conditions in order, first match wins; a decision region's
/// <c>1:1(slot, …)</c> rolls take the value of the DS1 roll the translator coloured into that slot; stubs are followed into
/// their groups with their p0, returns back out) from the battle machine's state 0 until the first sub-goal call. PASS = that
/// state starts the sub-goal queue DS1's path queued.</item>
/// <item><b>Interrupts</b>: every DS1 <c>_Interupt</c> path with exactly one interrupt set (rule AI7b models one interrupt per
/// frame) against the interrupt conditions of an interruptible step state: its reaction (or none) must come out. Paths with
/// several interrupts at once, with an interrupt that has no DS2 form, or left out for the envelope are counted separately.</item>
/// </list>
/// Paths with a decision the translator could not express (dropped on purpose, listed in the coverage) or with non-atomic
/// comparisons are counted separately.
/// </summary>
public static class AiVerify
{
    /// <summary>--legacy-ops: evaluate 0x91-0x94 with the pre-stage-3 (wrong) labels (negative test only).</summary>
    static bool LegacyOps;

    public static int Run(string[] args)
    {
        string key = args[1];
        string? map = null;
        var thinks = new List<int>();
        for (int i = 2; i < args.Length; i++) { if (args[i] == "--map") map = args[++i]; else if (args[i] == "--unread-interrupts") Ds2AiMachines.UnreadInterrupts = true; else if (args[i] == "--legacy-ops") LegacyOps = true; else thinks.Add(int.Parse(args[i], CultureInfo.InvariantCulture)); }
        var (_, rig) = ChrPorter.FindRig(key);
        var amap = AiTranslator.MapOf(rig.Attacks);
        var tp = Ds1Source.LoadParam(Program.DefaultDsrRoot, "NpcThinkParam")!;
        int totalFail = 0;
        foreach (int t in thinks)
        {
            var o = AiTranslator.Translate(tp[t]!, amap, Ds1Ai.TemplateHigh, Ds1Ai.TemplateLow, Path.Combine(ExtractDs2.DefaultOut, "ezstate"), null, map);
            var esd = ESD.Read(o.LowerEsd);
            var machines = o.Machines.Values.ToHashSet();
            var timerSlots = o.Compiler.TimerSlots;

            // 1. the split
            var splitFails = SplitCheck(o, esd);
            Console.WriteLine($"ai-verify {key} think {t} (goal {o.BattleGoal}): battle machine {o.Machine.Count} states → {o.BattleGroups.Count} group(s) "
                + $"[{string.Join(", ", o.BattleGroups.Select(g => $"{g}: {esd.StateGroups[g].Count}"))}], {o.Split.Stubs.Count} stubs: {(splitFails.Count == 0 ? "every copy matches its original" : $"{splitFails.Count} mismatch(es)")}");
            foreach (var f in splitFails.Take(8)) Console.WriteLine("  FAIL split " + f);
            totalFail += splitFails.Count;

            // 2. think paths
            int pass = 0, fail = 0, unbuildable = 0, dropped = 0, maxSteps = 0;
            var fails = new List<string>();
            foreach (var p in o.Activate.Paths)
            {
                string leaf = GoalCompiler.KeyOf(p);
                if (Situation(p, 0.5, o.Compiler.TimerTimes, timerSlots) == null) { unbuildable++; continue; }
                if (!o.Compiler.LeafEntries.TryGetValue(leaf, out var entries)) { dropped++; continue; }   // a queue the translator never built (dead decision)
                bool ok = true;
                // each interval's lower end, middle and upper end (DS1's <= / < boundaries)
                foreach (double at in new[] { 0.0, 0.5, 1.0 })
                {
                    var sit = Situation(p, at, o.Compiler.TimerTimes, timerSlots)!;
                    var reached = Simulate(esd, o, o.Compiler.RegionVarSlots, o.Compiler.RegionVarsOnPath(p), sit, machines, out string trace, out int steps);
                    maxSteps = Math.Max(maxSteps, steps);
                    if (reached is { } r && o.Split.OrigOf.TryGetValue(r, out long orig) && entries.Contains(orig)) continue;
                    ok = false;
                    if (fails.Count < 8) fails.Add($"[{string.Join(" ∧ ", p.Literals)}] at {at}: expected state(s) {string.Join("/", entries)}, reached {(reached is { } rr ? $"{rr.Group}:{rr.State}" : "none")}: {trace}");
                    break;
                }
                if (ok) pass++; else fail++;
            }
            Console.WriteLine($"  think: {o.Activate.Paths.Count} DS1 paths: {pass} reach DS1's queue, {fail} wrong, {dropped} drop a decision with no DS2 form (coverage), {unbuildable} not checkable (non-atomic decision); at most {maxSteps} transitions from think to the queue");
            foreach (var f in fails) Console.WriteLine("  FAIL " + f);
            totalFail += fail;

            // 3. interrupts: one interrupt per frame
            if (o.Interupt is { } intr)
            {
                int ip = 0, ifail = 0, multi = 0, noForm = 0, left = 0, iun = 0, none = 0;
                var ifails = new List<string>();
                var conds = o.Compiler.InterruptConditions;
                var islots = o.Compiler.InterruptVarSlots;
                foreach (var p in intr.Paths)
                {
                    bool fires = p.Return is true || (p.Return is double d && d != 0);
                    var set = p.Literals.Where(l => l.Value && l.Atom is Pred pr && pr.Name == "Interupt").Select(l => (int)((Pred)l.Atom).Args[0]).Distinct().ToList();
                    if (set.Count == 0) { if (fires) none++; else ip++; continue; }   // nothing set: no rule can fire (every rule starts with f5050(n) ≠ 0)
                    if (set.Count > 1) { multi++; continue; }
                    int n = set[0];
                    if (!Ds2AiMachines.InterruptHasDs2Form(n)) { noForm++; continue; }
                    var cleared = p with { Effects = [.. p.Effects.Where(e => e is not Ds1Goal.Clear)] };
                    string key2 = GoalCompiler.KeyOf(cleared);
                    if (fires && o.Compiler.DroppedInterrupts.Contains($"{n}|{key2}")) { left++; continue; }
                    bool ok = true, checkable = true;
                    foreach (double at in new[] { 0.0, 0.5, 1.0 })
                    {
                        var sit = Situation(p, at, o.Compiler.TimerTimes, timerSlots);
                        if (sit == null) { checkable = false; break; }
                        var slots = new double[4];
                        foreach (var (v, sl) in islots) if (sit.Rand.TryGetValue(v, out double x)) slots[sl] = x;
                        long? hit = null;
                        foreach (var c in conds) if (Eval(c.Evaluator, sit, slots, []) != 0) { hit = c.TargetState; break; }
                        bool good = fires ? hit is long h && o.Compiler.LeafEntries.TryGetValue(key2, out var es) && es.Contains(h) : hit == null;
                        if (good) continue;
                        ok = false;
                        if (ifails.Count < 6) ifails.Add($"[{string.Join(" ∧ ", p.Literals)}] at {at}: {(fires ? "expected a reaction" : "expected none")}, got {hit?.ToString() ?? "none"}");
                        break;
                    }
                    if (!checkable) iun++; else if (ok) ip++; else ifail++;
                }
                Console.WriteLine($"  interrupts: {intr.Paths.Count} DS1 paths: {ip} right (reaction or none), {ifail} wrong; not modelled: {multi} with several interrupts in one frame, {noForm} on an interrupt without a DS2 form [{string.Join(", ", o.Compiler.InterruptsWithoutDs2Form)}], {left} left out for the envelope (coverage), {none} firing with no interrupt set; {iun} not checkable");
                foreach (var f in ifails) Console.WriteLine("  FAIL " + f);
                totalFail += ifail;
            }
        }
        return totalFail == 0 ? 0 : 1;
    }

    static IEnumerable<ESD.Condition> Flat(IEnumerable<ESD.Condition> cs) => cs.SelectMany(c => new[] { c }.Concat(Flat(c.Subconditions)));

    /// <summary>Every copied state against its original; every stub against the entry it stands for.</summary>
    static List<string> SplitCheck(AiTranslator.Output o, ESD esd)
    {
        var bad = new List<string>();
        var sp = o.Split;
        bool SameCmds(List<ESD.CommandCall> a, List<ESD.CommandCall> b) => a.Count == b.Count && a.Zip(b).All(x => x.First.CommandBank == x.Second.CommandBank && x.First.CommandID == x.Second.CommandID
            && x.First.Arguments.Count == x.Second.Arguments.Count && x.First.Arguments.Zip(x.Second.Arguments).All(y => y.First.AsSpan().SequenceEqual(y.Second)));
        // where does (group g, state s) lead when entered, for target comparison: a copy → its original; a stub → its callee's entry
        long? Leads(long g, long s)
        {
            if (sp.OrigOf.TryGetValue((g, s), out long orig)) return orig;
            if (sp.Stubs.TryGetValue((g, s), out var stub))
            {
                var entries = sp.Entries[stub.Callee];
                long entry = stub.Entry == 0 ? entries.Single() : entries[stub.Entry - 1];
                // the callee's dispatcher must reach a copy of it with p0 = k
                long cur = 0;
                var cs = esd.StateGroups[stub.Callee];
                for (int i = 0; i < 8 && !sp.OrigOf.ContainsKey((stub.Callee, cur)); i++)
                {
                    long? nx = null;
                    foreach (var c in cs[cur].Conditions) if (c.TargetState is long tt && Eval(c.Evaluator, new Sit(), new double[4], [stub.Entry]) != 0) { nx = tt; break; }
                    if (nx == null) break;
                    cur = nx.Value;
                }
                return sp.OrigOf.TryGetValue((stub.Callee, cur), out long reached) && reached == entry ? entry : -2;
            }
            return null;
        }
        foreach (var ((g, s), orig) in sp.OrigOf)
        {
            var a = o.Machine[orig];
            var b = esd.StateGroups[g][s];
            string at = $"group {g} state {s} (original {orig})";
            if (!SameCmds(a.EntryCommands, b.EntryCommands) || !SameCmds(a.WhileCommands, b.WhileCommands) || !SameCmds(a.ExitCommands, b.ExitCommands)) { bad.Add($"{at}: commands differ"); continue; }
            void Cmp(List<ESD.Condition> ca, List<ESD.Condition> cb, string where)
            {
                if (ca.Count != cb.Count) { bad.Add($"{where}: {cb.Count} conditions, original {ca.Count}"); return; }
                for (int i = 0; i < ca.Count; i++)
                {
                    var x = ca[i]; var y = cb[i];
                    if (!x.Evaluator.AsSpan().SequenceEqual(y.Evaluator) || !SameCmds(x.PassCommands, y.PassCommands)) { bad.Add($"{where} condition {i}: evaluator / pass commands differ"); continue; }
                    if (x.TargetState is long tx)
                    {
                        if (y.TargetState is not long ty) { bad.Add($"{where} condition {i}: lost its target"); continue; }
                        if (tx == o.ThinkState)
                        {
                            long back = g == sp.Main ? sp.Think : sp.ReturnStateOf.GetValueOrDefault(g, -1);
                            if (ty != back) bad.Add($"{where} condition {i}: back-to-think goes to {ty}, expected {back}");
                        }
                        else if (Leads(g, ty) != tx) bad.Add($"{where} condition {i}: target {ty} leads to {Leads(g, ty)?.ToString() ?? "nothing"}, original {tx}");
                    }
                    else if (y.TargetState != null) bad.Add($"{where} condition {i}: gained a target");
                    Cmp(x.Subconditions, y.Subconditions, $"{where} condition {i}");
                }
            }
            Cmp(a.Conditions, b.Conditions, at);
        }
        // every original state is placed somewhere reachable (main's state 0 / think, or a copy)
        foreach (var k in o.Machine.Keys) if (!sp.Copies.ContainsKey(k)) bad.Add($"original state {k} has no copy");
        // stubs: call, then back to think / return on the callee's return
        foreach (var ((g, s), stub) in sp.Stubs)
        {
            var b = esd.StateGroups[g][s];
            long back = g == sp.Main ? sp.Think : sp.ReturnStateOf[g];
            var call = b.EntryCommands.SingleOrDefault(c => c.CommandBank == 6);
            if (call == null || (long)(uint)call.CommandID != stub.Callee) bad.Add($"stub {g}:{s}: does not call {stub.Callee}");
            if (!Flat(b.Conditions).Any(c => c.TargetState == back)) bad.Add($"stub {g}:{s}: does not go back to {back} on return");
        }
        return bad;
    }

    internal sealed class Sit
    {
        public double Dist = 3, Height = 0, Hp = 1;
        public readonly Dictionary<int, double> Rand = [];
        public readonly Dictionary<int, bool> Interrupt = [];
        /// <summary>Session 11 (ret11), rule A1: EventLocation → the target inside it (DS1 IsInsideTargetRegion literals).</summary>
        public readonly Dictionary<int, bool> Region = [];
        /// <summary>DS2 timer slot → f2 (seconds since its reset).</summary>
        public readonly Dictionary<int, double> Timer = [];
        /// <summary>Session 11 (fx11), rule E-REQ: AI request channel k → its logicEventFlag set (f5903(k)).</summary>
        public readonly Dictionary<int, bool> Channel = [];
        /// <summary>Session 11 (dchr11), rules AI-NF / AI-NM: a friend near (f7331), no wall ahead (f7082 = 0), not pressed against anything (f7081 = 0).</summary>
        public bool NearFriend, NearMeshF = true, NearMeshSide = true;
    }

    /// <summary>Session 11 (fx11): rule E-REQ literals (ai:GetEventRequest(slot) compared with a number) as channel states; false
    /// when the literal is not of that form; null when it cannot be satisfied by the think's channels.</summary>
    static bool? EventRequestLiteral(Literal l, Sit s)
    {
        if (l.Atom is not Cmp c) return false;
        LuaEval.Query? q; double v; string op = c.Op;
        if (c.A is LuaEval.Query qa && qa.Name == "EventRequest" && c.B is double cb) { q = qa; v = cb; }
        else if (c.B is LuaEval.Query qb && qb.Name == "EventRequest" && c.A is double ca) { q = qb; v = ca; op = op switch { "<" => ">", "<=" => ">=", ">" => "<", ">=" => "<=", _ => op }; }
        else return false;
        int slot = (int)q.Args[0], think = q.Args.Length > 1 ? (int)q.Args[1] : 0;
        var ch = Build.Ai.EventRequests.ForThink(think).Where(x => x.Slot == slot).ToList();
        bool Holds(double value) => (l.Value, op) switch
        {
            (true, "==") => value == v, (false, "==") => value != v, (true, "~=") => value != v, (false, "~=") => value == v,
            (true, "<") => value < v, (false, "<") => value >= v, (true, "<=") => value <= v, (false, "<=") => value > v,
            (true, ">") => value > v, (false, ">") => value <= v, (true, ">=") => value >= v, (false, ">=") => value < v, _ => false,
        };
        // the value the slot reads: 0 (no channel set) or one channel's command (the event side sets one at a time)
        bool anySet = ch.Any(x => s.Channel.GetValueOrDefault(x.K));
        if (anySet) { var on = ch.First(x => s.Channel.GetValueOrDefault(x.K)); return Holds(on.V) ? true : null; }
        if (Holds(0)) { foreach (var x in ch) s.Channel.TryAdd(x.K, false); return true; }
        var pick = ch.FirstOrDefault(x => Holds(x.V));
        if (pick == default) return null;
        foreach (var x in ch) s.Channel[x.K] = x.K == pick.K;
        return true;
    }

    /// <summary>A concrete situation inside every interval the path's decisions impose; null when a decision is not atomic.</summary>
    internal static Sit? Situation(Ds1Goal.PathResult p, double at, IReadOnlyDictionary<double, float> timers, IReadOnlyDictionary<double, int> timerSlots)
    {
        var iv = new Dictionary<string, (double Lo, double Hi, bool Int)>();
        var s = new Sit();
        var done = new Dictionary<int, bool>();
        foreach (var l in p.Literals)
        {
            switch (EventRequestLiteral(l, s)) { case true: continue; case null: return null; }
            switch (l.Atom)
            {
                case Pred pr when pr.Name == "Interupt": s.Interrupt[(int)pr.Args[0]] = l.Value; continue;
                case Pred pr when pr.Name == "InRegion" && pr.Args[0] == 0: s.Region[Build.Maps.AsylumScript.RegionId((int)pr.Args[1])] = l.Value; continue;   // session 11 (ret11), rule A1
                case Pred pr when pr.Name == "TimerDone": done[(int)pr.Args[0]] = l.Value; continue;
                case Pred pr when pr.Name == "NearFriend": s.NearFriend = l.Value; continue;     // session 11 (dchr11), rule AI-NF
                case Pred pr when pr.Name == "NearMeshF": s.NearMeshF = l.Value; continue;      // rule AI-NM
                case Pred pr when pr.Name == "NearMeshSide": s.NearMeshSide = l.Value; continue;
                case Pred: return null;
                case Cmp c:
                {
                    Sym? x; double v; string op = c.Op;
                    if (c.A is Sym sa && sa is not Arith && c.B is double cb) { x = sa; v = cb; }
                    else if (c.B is Sym sb && sb is not Arith && c.A is double ca) { x = sb; v = ca; op = op switch { "<" => ">", "<=" => ">=", ">" => "<", ">=" => "<=", _ => op }; }
                    else return null;
                    if (x is Abs) return null;
                    bool isInt = x is LuaEval.Rand r && !r.IsFloat;
                    if (!iv.TryGetValue(x.Key, out var cur)) cur = x is LuaEval.Rand rr ? (rr.Lo, rr.Hi, isInt) : (-1000, 1000, false);
                    if (!l.Value) op = op switch { "<" => ">=", "<=" => ">", ">" => "<=", ">=" => "<", "==" => "~=", "~=" => "==", _ => op };
                    double eps = isInt ? 1 : 0.001;
                    cur = op switch
                    {
                        "<" => (cur.Lo, Math.Min(cur.Hi, v - eps), isInt),
                        "<=" => (cur.Lo, Math.Min(cur.Hi, isInt ? Math.Floor(v) : v), isInt),
                        ">" => (Math.Max(cur.Lo, v + eps), cur.Hi, isInt),
                        ">=" => (Math.Max(cur.Lo, isInt ? Math.Ceiling(v) : v), cur.Hi, isInt),
                        "==" => (v, v, isInt),
                        "~=" when isInt && v == cur.Lo => (cur.Lo + 1, cur.Hi, isInt),
                        "~=" when isInt && v == cur.Hi => (cur.Lo, cur.Hi - 1, isInt),
                        _ => cur,
                    };
                    iv[x.Key] = cur;
                    break;
                }
            }
        }
        foreach (var (k, (lo0, hi0, isInt)) in iv)
        {
            if (lo0 > hi0) return null;
            // open distance ends (no DS1 bound) stay near the bounded end so "at 1" is not an absurd 1000 m
            double lo = lo0 <= -1000 ? Math.Min(hi0, 0) - (hi0 > -1000 ? 0.5 : 0) : lo0, hi = hi0 >= 1000 ? Math.Max(lo, 0) + 20 : hi0;
            if (lo0 <= -1000 && hi0 < 1000) lo = hi0 - 20;
            double mid = lo + (hi - lo) * at;
            if (isInt) mid = Math.Round(mid);
            if (k.StartsWith("rand#")) s.Rand[int.Parse(k[5..])] = mid;
            else if (k == "Dist(0)") s.Dist = mid;
            else if (k == "DistY(0)") s.Height = mid;
            else if (k == "HpRate(-1)") s.Hp = mid;
            else if (k.StartsWith("Timer(") && double.Parse(k[6..^1], CultureInfo.InvariantCulture) is double ti && timers.TryGetValue(ti, out float tt) && timerSlots.TryGetValue(ti, out int slot))
                s.Timer[slot] = tt - mid;     // DS1 GetTimer = remaining = t − f2
            else return null;
        }
        foreach (var (ti, d) in done)
        {
            if (!timers.TryGetValue(ti, out float tt) || !timerSlots.TryGetValue(ti, out int slot)) return null;
            if (s.Timer.TryGetValue(slot, out double f2) ? (f2 >= tt) != d : false) return null;   // inconsistent with a GetTimer bound
            if (!s.Timer.ContainsKey(slot)) s.Timer[slot] = d ? tt + 5 : 0;
        }
        // only the rolls this path compares set a slot (a roll it never compares may share its slot with one of another
        // path: the slot then holds whatever the compared roll needs)
        return s;
    }

    /// <summary>Runs the battle machine (all its groups) from the main group's state 0 to the first state that calls a sub-goal
    /// machine (or back to think).</summary>
    internal static (long Group, long State)? Simulate(ESD esd, AiTranslator.Output o, Dictionary<long, Dictionary<int, int>> regionSlots, Dictionary<long, HashSet<int>> comparedIn, Sit sit, HashSet<long> machines, out string trace, out int steps)
    {
        var slots = new double[4];
        var battle = o.BattleGroups.ToHashSet();
        var frames = new Stack<(long G, long S, double[] P)>();
        (long G, long S) cur = (o.BattleMachine, 0);
        double[] prm = [];
        var visited = new List<string>();
        int fromThink = -1;
        for (steps = 0; steps < 200; steps++)
        {
            visited.Add($"{cur.G - o.BattleMachine}:{cur.S}");
            if (fromThink >= 0) fromThink++;
            var st = esd.StateGroups[cur.G][cur.S];
            var call = st.EntryCommands.FirstOrDefault(c => c.CommandBank == 6);
            if (call != null && machines.Contains((long)(uint)call.CommandID)) { trace = string.Join("→", visited); steps = fromThink; return cur; }
            if (call != null && battle.Contains((long)(uint)call.CommandID))
            {
                frames.Push((cur.G, cur.S, prm));
                prm = [.. call.Arguments.Select(a => Eval(a, sit, slots, prm))];
                cur = ((long)(uint)call.CommandID, 0);
                continue;
            }
            // a return state: back to the stub that called this group, then its "returned" condition
            if (st.Conditions.Count == 1 && st.Conditions[0].PassCommands.Any(c => c.CommandBank == 7))
            {
                if (frames.Count == 0) { trace = string.Join("→", visited) + " (returned from the battle machine)"; return null; }
                var (fg, fs, fp) = frames.Pop();
                prm = fp;
                var back = Flat(esd.StateGroups[fg][fs].Conditions).First(c => c.TargetState != null).TargetState!.Value;
                cur = (fg, back);
                continue;
            }
            // a region's rolls: the DS1 rolls this path compares inside this region, in the slots the translator gave them
            if (o.Split.OrigOf.TryGetValue(cur, out long orig) && regionSlots.TryGetValue(orig, out var vs))
                foreach (var (varId, slot) in vs)
                    if (comparedIn.TryGetValue(orig, out var here) && here.Contains(varId) && sit.Rand.TryGetValue(varId, out double v)) slots[slot] = v;
            long? next = null;
            foreach (var c in st.Conditions)
            {
                if (c.TargetState is not long t) continue;
                if (Eval(c.Evaluator, sit, slots, prm) != 0) { next = t; break; }
            }
            if (next == null) { trace = string.Join("→", visited) + " (no condition true)"; return null; }
            bool atThink = cur.G == o.BattleMachine && cur.S == o.Split.Think;
            cur = (cur.G, next.Value);
            if (atThink) { if (fromThink >= 0) { trace = string.Join("→", visited) + "→think"; return (o.BattleMachine, o.Split.Think); } fromThink = 0; }
        }
        trace = string.Join("→", visited) + " (loop)";
        return null;
    }

    static double Eval(byte[] b, Sit sit, double[] slots, double[] prm)
    {
        var st = new Stack<double>();
        for (int i = 0; i < b.Length; i++)
        {
            byte op = b[i];
            if (op < 0x80) { st.Push(op - 0x40); continue; }
            switch (op)
            {
                case 0x80: st.Push(BitConverter.ToSingle(b, i + 1)); i += 4; break;
                case 0x81: st.Push(BitConverter.ToDouble(b, i + 1)); i += 8; break;
                case 0x82: st.Push(BitConverter.ToInt32(b, i + 1)); i += 4; break;
                case >= 0x84 and <= 0x8A:
                {
                    int argc = op - 0x84;
                    var a = new double[argc];
                    for (int k = argc - 1; k >= 0; k--) a[k] = st.Pop();
                    int id = (int)st.Pop();
                    st.Push(id switch
                    {
                        3 => 1.0,                                      // f3(): time in state (past the think's one-frame wait)
                        1 => slots[(int)a[0]],
                        2 => sit.Timer.GetValueOrDefault((int)a[0], 1000),
                        7001 => sit.Dist,
                        7007 => sit.Height,
                        7002 => 0,
                        5200 => sit.Hp * 100,
                        5050 => sit.Interrupt.GetValueOrDefault((int)a[0]) ? 1 : 0,
                        // session 11 (ret11), rule A1: f7070(EventLocation, 0) = DS1's IsInsideTargetRegion; a region the path does not
                        // test is inside exactly when Inside_ObserveArea (16) is set (the polled form of that interrupt)
                        7070 => sit.Region.GetValueOrDefault((int)a[0], sit.Interrupt.GetValueOrDefault(16)) ? 1 : 0,
                        5903 => sit.Channel.GetValueOrDefault((int)a[0]) ? 1 : 0,   // session 11 (fx11), rule E-REQ: logicEventFlag(k+1)
                        7331 => sit.NearFriend ? 1 : 0,                               // session 11 (dchr11), rule AI-NF
                        7082 => sit.NearMeshF ? 0 : 1,                                // rule AI-NM (forward: no wall)
                        7081 => sit.NearMeshSide ? 0 : 1,                             // rule AI-NM (back / sides: not pressed against anything)
                        _ => throw new InvalidDataException($"ai-verify: function f{id} in a decision"),
                    });
                    break;
                }
                case 0x8C: { double y = st.Pop(), x = st.Pop(); st.Push(x + y); break; }
                case 0x8E: { double y = st.Pop(), x = st.Pop(); st.Push(x - y); break; }
                case 0x8F: { double y = st.Pop(), x = st.Pop(); st.Push(x * y); break; }
                case 0x90: { double y = st.Pop(), x = st.Pop(); st.Push(x / y); break; }
                // DS2's comparison opcodes (PTR_FUN_1415b2920, right operand popped first; rulebook §7, V s9-12): 0x91 <,
                // 0x92 >, 0x93 <=, 0x94 >=. --legacy-ops evaluates them with the table our tools used before (0x91 <=,
                // 0x92 >=, 0x93 <, 0x94 >): the negative test that the verifier sees the boundaries (chrint stage 3)
                case >= 0x91 and <= 0x94:
                {
                    double y = st.Pop(), x = st.Pop();
                    int real = LegacyOps ? op switch { 0x91 => 0x93, 0x92 => 0x94, 0x93 => 0x91, _ => 0x92 } : op;
                    st.Push(real switch
                    {
                        0x91 => x < y - 1e-5, 0x92 => x > y + 1e-5, 0x93 => x <= y + 1e-5, _ => x >= y - 1e-5,
                    } ? 1 : 0);
                    break;
                }
                case 0x95: { double y = st.Pop(), x = st.Pop(); st.Push(Math.Abs(x - y) < 1e-5 ? 1 : 0); break; }
                case 0x96: { double y = st.Pop(), x = st.Pop(); st.Push(Math.Abs(x - y) >= 1e-5 ? 1 : 0); break; }
                case 0x98: { double y = st.Pop(), x = st.Pop(); st.Push(x != 0 && y != 0 ? 1 : 0); break; }
                case 0x99: { double y = st.Pop(), x = st.Pop(); st.Push(x != 0 || y != 0 ? 1 : 0); break; }
                case 0x8D: st.Push(-st.Pop()); break;
                case 0xB8: { int k = (int)st.Pop(); st.Push(k < prm.Length ? prm[k] : 0); break; }
                case 0xA1: return st.Count > 0 ? st.Pop() : 0;
                default: throw new InvalidDataException($"ai-verify: opcode {op:X2}");
            }
        }
        return st.Count > 0 ? st.Pop() : 0;
    }
}
