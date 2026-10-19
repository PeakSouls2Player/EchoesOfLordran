using System.Globalization;
using SoulsFormats;
using static Lordran.Tools.Build.Ai.Ds1Goal;
using static Lordran.Tools.Build.Ai.Esb;
using static Lordran.Tools.Build.Ai.LuaEval;
using M = Lordran.Tools.Build.Ai.Ds2AiMachines;

namespace Lordran.Tools.Build.Ai;

/// <summary>
/// Compiles a symbolically evaluated DS1 battle goal (<see cref="Ds1Goal"/>) into one DS2 AI state group, the "battle
/// machine" (session 8, track enemies step 2). Rules (status P until seen in game):
/// <list type="number">
/// <item><b>Think</b> = DS1 <c>_Activate</c> (re-run whenever the sub-goal queue is empty): its paths form a decision tree over
/// atomic DS1 queries; identical subtrees merge. The tree is cut into regions whose random rolls fit DS2's 4 random slots
/// (<c>1:1(slot, lo, hi)</c>; vars that never meet on one path share a slot); a region is one state: entry rolls, then one
/// condition per exit = the conjunction of the DS1 decisions leading to it (DS1's ladder order, first match wins).</item>
/// <item><b>Decisions</b>: GetDist → <c>f7001()</c>, GetDistYSigned → <c>f7007()</c>, GetHpRate(self) → <c>f5200()·0.01</c>,
/// GetRandam_Int/Float → <c>f1(slot)</c> (floats rolled ×1000), IsInterupt(n) → <c>f5050(n)</c>, IsFinishTimer(i) → <c>f2(i) ≥ t</c>
/// (t of DS1's SetTimer), IsInsideTarget(F/B, a) → <c>f7002()</c> vs a/2.</item>
/// <item><b>Leaf</b> = the DS1 sub-goal queue of that path, in order, each a call of a shared machine (<see cref="Ds2AiMachines"/>);
/// a machine returning 1 (DS1: the sub-goal failed) drops the rest of the queue (back to think). CommonAttack runs that DS1
/// marks as a combo (isComboAttack true … first false) become the DS2 combo action of the matching DS1 chain (E1: 50 + k),
/// stopped after DS1's number of stages; single attacks are action A − 3000 (or the plan's allocated number).</item>
/// <item><b>Interrupts</b> = DS1 <c>_Interupt</c>: evaluated in the states of interruptible sub-goals (DS1 registers Attack-type
/// goals, ApproachTarget and Turn with REGISTER_GOAL_NO_INTERUPT; Wait, LeaveTarget, SidewayMove, SpinStep, KeepDist take
/// interrupts): their rolls happen at the sub-goal's start, each "return true" path is a condition that abandons the
/// sub-goal and runs its new queue (after ClearSubGoal), then thinks again.</item>
/// <item><b>OnIf</b> (GOAL_COMMON_If): its decision tree is compiled where the queue reaches it, then the queue continues.</item>
/// </list>
/// <para><b>Vanilla envelope</b> (fix pass, rule AI10): every state stays inside what the 485 vanilla AI ESDs contain (at most
/// 31 top-level conditions and 1768 evaluator bytes per state, 209 bytes per condition; validate\esd_envelope.py). A decision
/// region stops growing at <see cref="MaxRules"/> rules / <see cref="MaxStateBytes"/> bytes (deeper decisions continue in the
/// next region state, one transition later); interrupt reactions are compiled per single interrupt (rule AI7b) under the same
/// caps. <see cref="GroupSplit"/> then keeps every state group at most 90 states (vanilla 93).</para>
/// </summary>
public sealed class GoalCompiler
{
    public sealed record Chain(int Row, int[] Ids);

    /// <summary>The character's DS2 action numbers (step 1's plan): DS1 attack/step id → action; DS1 chains → combo rows.</summary>
    public sealed record ActionMap(Dictionary<int, int> Actions, List<Chain> Chains);

    /// <summary>Caps per generated state, under vanilla's maxima (31 conditions, 1768 evaluator bytes per state, 209 per
    /// condition): a region state's rules, a step state's interrupt rules (+ its 2 return conditions), bytes.</summary>
    public const int MaxRules = 28, MaxStateBytes = 1600, MaxCondBytes = 200;

    public sealed class Coverage
    {
        public readonly SortedDictionary<string, int> Translated = [];
        public readonly SortedDictionary<string, int> Skipped = [];
        public readonly List<string> Notes = [];
        public readonly HashSet<int> ActionsUsed = [];
        public readonly HashSet<int> ChainsUsed = [];
        public int Paths, Leaves, Regions, States;
        /// <summary>&gt; 0 while costs are only being estimated (region growth): nothing is counted.</summary>
        public int Quiet;
        public void T(string what) { if (Quiet == 0) Translated[what] = Translated.GetValueOrDefault(what) + 1; }
        public void S(string what) { if (Quiet == 0) Skipped[what] = Skipped.GetValueOrDefault(what) + 1; }
        public void N(string what) { if (Quiet == 0) Notes.Add(what); }
    }

    readonly Ds1Goal goal;
    readonly ActionMap map;
    readonly Dictionary<M.Kind, long> machines;
    public readonly Coverage Cov = new();
    readonly Dictionary<long, ESD.State> states = [];
    long nextState = 1;
    long thinkState;
    /// <summary>Session 11 (ret11), rule A1: DS1 logic regions the goal observes (AddObserveRegion on TARGET_ENE_0).</summary>
    readonly HashSet<double> observeRegions = [];
    /// <summary>Session 11 (ret11), rule A1: the DS2 test of DS1 interrupt n: f5050(n), except Inside_ObserveArea (16) of a goal that
    /// observes regions, which is polled: f7070(EventLocation, 0) over them (DS2 has no registration of a DS1 observe region).</summary>
    E InterruptExpr(int n) => n == 16 && observeRegions.Count > 0
        ? observeRegions.Order().Select(r => E.Fn(7070, E.I(Maps.AsylumScript.RegionId((int)r)), E.I(0)).Ne(0)).Aggregate((a, b) => a | b)
        : M.Interrupt(n).Ne(0);
    readonly Dictionary<double, float> timerTime = [];
    /// <summary>DS1 timer index → DS2 timer slot (<see cref="Ds2AiMachines.TimerSlotOrder"/>: slots the template never resets first).</summary>
    readonly Dictionary<double, int> timerSlot = [];
    readonly List<(int Slot, int Resets)> timerSlotOrder;
    readonly HashSet<string> interruptible = ["Wait", "LeaveTarget", "SidewayMove", "SpinStep", "KeepDist", "Guard", "Stay"];
    List<InterruptExit> interrupts = [];
    /// <summary>Session 11 (track enemy11, G5 rule AI7c): a DS1 battle goal's _Interupt runs under EVERY sub-goal - the DSR
    /// dispatcher FUN_1405d1a90 recurses into the active sub-goal and then calls the battle goal's own handler; REGISTER_GOAL_NO_INTERUPT
    /// (definition byte +0xfd, written only by FUN_140629610) mutes only that sub-goal's own callback. So Attack / combo / Approach /
    /// ApproachStep / Turn steps carry the interrupt conditions too (build-pack --ds1-ai-no-attack-interrupts: the old set).</summary>
    public static bool BattleInterrupts { get; set; } = true;
    readonly Dictionary<string, long> leafCache = [];

    /// <param name="timerSlots">DS2 timer slots for DS1 timers, best first (<see cref="Ds2AiMachines.TimerSlotOrder"/>).</param>
    public GoalCompiler(Ds1Goal goal, ActionMap map, Dictionary<M.Kind, long> machines, List<(int Slot, int Resets)>? timerSlots = null)
    {
        this.goal = goal; this.map = map; this.machines = machines;
        timerSlotOrder = timerSlots ?? [(2, 0), (1, 7), (0, 23), (3, 53)];
    }

    // ------------------------------------------------------------------ decision trees
    abstract class Node { public string Sig = ""; }
    sealed class Decision(SymBool atom, int line, Node t, Node f) : Node { public SymBool Atom = atom; public int Line = line; public Node T = t, F = f; }
    sealed class Leaf(PathResult p) : Node { public PathResult Path = p; }

    static Node BuildTree(List<PathResult> paths)
    {
        Node Build(List<PathResult> ps, int depth)
        {
            var first = ps[0];
            if (first.Literals.Count <= depth)
            {
                if (ps.Count > 1 && ps.Any(p => p.Literals.Count > depth)) throw new InvalidDataException("inconsistent path tree (a path ends where another branches)");
                return new Leaf(first);
            }
            var lit = first.Literals[depth];
            var t = ps.Where(p => p.Literals[depth].Value).ToList();
            var f = ps.Where(p => !p.Literals[depth].Value).ToList();
            if (ps.Any(p => p.Literals.Count <= depth || p.Literals[depth].Atom.Key != lit.Atom.Key)) throw new InvalidDataException($"inconsistent path tree at depth {depth} ({lit.Atom.Key})");
            if (t.Count == 0 || f.Count == 0) throw new InvalidDataException($"one-sided branch at depth {depth} ({lit.Atom.Key})");
            return new Decision(lit.Atom, lit.Line, Build(t, depth + 1), Build(f, depth + 1));
        }
        return Build(paths, 0);
    }

    /// <summary>Merge identical subtrees: a decision whose arms do the same thing disappears.</summary>
    Node Simplify(Node n)
    {
        switch (n)
        {
            case Leaf l: l.Sig = "L:" + LeafKey(l.Path); return l;
            case Decision d:
            {
                d.T = Simplify(d.T); d.F = Simplify(d.F);
                if (d.T.Sig == d.F.Sig) return d.T;
                d.Sig = $"D({d.Atom.Key}?{d.T.Sig}:{d.F.Sig})";
                return d;
            }
        }
        return n;
    }

    /// <summary>What a leaf does (sub-goals with their args incl. the random vars they read, immediates, return value).</summary>
    static string LeafKey(PathResult p) => string.Join(";", p.Effects.Select(e => e.ToString())) + "|ret=" + F(p.Return);

    static IEnumerable<Rand> RandsIn(object? o)
    {
        switch (o)
        {
            case Rand r: yield return r; break;
            case Arith a: foreach (var x in RandsIn(a.A).Concat(RandsIn(a.B))) yield return x; break;
            case Abs ab: foreach (var x in RandsIn(ab.A)) yield return x; break;
            case Cmp c: foreach (var x in RandsIn(c.A).Concat(RandsIn(c.B))) yield return x; break;
            case Not n: foreach (var x in RandsIn(n.A)) yield return x; break;
        }
    }

    // ------------------------------------------------------------------ slots
    /// <summary>Random var → slot, with the roll range the slot uses.</summary>
    sealed class Slots
    {
        public readonly Dictionary<int, (int Slot, int Lo, int Hi)> Of = [];
        public Slots Clone() { var s = new Slots(); foreach (var (k, v) in Of) s.Of[k] = v; return s; }
    }

    static (int Lo, int Hi) RangeOf(Rand r) => r.IsFloat ? ((int)Math.Round(r.Lo * 1000), (int)Math.Round(r.Hi * 1000)) : ((int)r.Lo, (int)r.Hi);

    /// <summary>Colour the vars of a region: vars that meet on one root-to-exit path (or with a carried var) need distinct
    /// slots; equal ranges may share. Returns null when more than <paramref name="maxSlots"/> are needed.</summary>
    static Slots? Colour(IEnumerable<List<Rand>> pathsVars, Slots carried, int maxSlots, IReadOnlySet<int> reserved)
    {
        var res = carried.Clone();
        var interfere = new Dictionary<int, HashSet<int>>();
        var rands = new Dictionary<int, Rand>();
        var order = new List<int>();
        foreach (var pv in pathsVars)
        {
            var ids = pv.Select(r => r.Id).Distinct().ToList();
            foreach (var r in pv) { rands.TryAdd(r.Id, r); if (!order.Contains(r.Id)) order.Add(r.Id); }
            foreach (int a in ids) { interfere.TryAdd(a, []); foreach (int b in ids) if (a != b) interfere[a].Add(b); foreach (var c in carried.Of.Keys) if (c != a) interfere[a].Add(c); }
        }
        foreach (int id in order)
        {
            if (res.Of.ContainsKey(id)) continue;
            var (lo, hi) = RangeOf(rands[id]);
            var busy = interfere[id].Where(res.Of.ContainsKey).Select(o => res.Of[o]).ToList();
            int slot = -1;
            // reuse a slot holding the same range when no interfering var uses it
            foreach (var (k, v) in res.Of)
                if (v.Lo == lo && v.Hi == hi && !busy.Any(b => b.Slot == v.Slot) && !carried.Of.Values.Any(c => c.Slot == v.Slot && !(c.Lo == lo && c.Hi == hi))) { slot = v.Slot; break; }
            if (slot < 0)
                for (int s = 0; s < maxSlots; s++)
                    if (!reserved.Contains(s) && !res.Of.Values.Any(v => v.Slot == s)) { slot = s; break; }
            if (slot < 0) return null;
            res.Of[id] = (slot, lo, hi);
        }
        return res;
    }

    // ------------------------------------------------------------------ expressions
    E? Expr(object? o, Slots slots, string where)
    {
        switch (o)
        {
            case double d: return E.Fl((float)d);
            case bool b: return E.I(b ? 1 : 0);
            case Rand r:
                if (!slots.Of.TryGetValue(r.Id, out var s)) throw new InvalidDataException($"{where}: random #{r.Id} has no slot");
                return r.IsFloat ? M.Rand(s.Slot) * new EFloat(0.001f) : M.Rand(s.Slot);
            case Query q:
                switch (q.Name)
                {
                    case "Dist" when q.Args[0] == 0: Cov.T("GetDist(TARGET_ENE_0) → f7001()"); return M.Dist;
                    case "DistY" when q.Args[0] == 0: Cov.T("GetDistYSigned(TARGET_ENE_0) → f7007()"); return M.Height;
                    case "EventRequest":
                    {
                        // session 11 (burgchr11), rule E-REQ: Σ v · (f5903(k) != 0) over the slot's channels
                        E sum = E.I(0);
                        foreach (var (_, v, k) in EventRequests.ForThink((int)q.Args[1]).Where(c => c.Slot == (int)q.Args[0]))
                            sum = sum + E.I(v) * E.Fn(5903, E.I(k)).Ne(E.I(0));
                        Cov.T($"GetEventRequest({F(q.Args[0])}) → Σ v·(f5903(k) != 0) (rule E-REQ)");
                        return sum;
                    }
                    case "HpRate" when q.Args[0] == -1: Cov.T("GetHpRate(TARGET_SELF) → f5200()·0.01"); return M.HpPercent * new EFloat(0.01f);
                    case "Timer" when timerTime.TryGetValue(q.Args[0], out float tt) && timerSlot.TryGetValue(q.Args[0], out int ts): Cov.T($"GetTimer({F(q.Args[0])}) → t − f2({ts})"); return E.Fl(tt) - M.Timer(ts);
                }
                Cov.S($"query {q.Key} (no DS2 function)");
                return null;
            case Arith a:
            {
                var x = Expr(a.A, slots, where); var y = Expr(a.B, slots, where);
                if (x == null || y == null) return null;
                return a.Op switch { "+" => x + y, "-" => x - y, "*" => x * y, "/" => x / y, _ => null };
            }
            case Abs ab:
            {
                var x = Expr(ab.A, slots, where);
                if (x == null) return null;
                Cov.T("math.abs(x) → (x·x)^½ as max(x, −x) test"); // compiled where compared (see Lit)
                return x;
            }
        }
        return null;
    }

    /// <summary>One DS1 decision literal as a DS2 condition (null = untranslatable).</summary>
    E? LitOrNull(Literal l, Slots slots, string where)
    {
        E? pos = null;
        switch (l.Atom)
        {
            case Cmp c:
            {
                // math.abs(x) op v → (x op v) ∧ (−x op v) for ≤/<, (x op v) ∨ (−x op v) for ≥/>
                if (c.A is Abs ab && c.B is double v)
                {
                    var x = Expr(ab.A, slots, where);
                    if (x == null) return null;
                    var nx = E.I(0) - x;
                    E Op(E a) => c.Op switch { "<" => a.Lt((float)v), "<=" => a.Le((float)v), ">" => a.Gt((float)v), ">=" => a.Ge((float)v), _ => a.Eq((float)v) };
                    pos = c.Op is "<" or "<=" ? Op(x) & Op(nx) : Op(x) | Op(nx);
                    break;
                }
                var ea = Expr(c.A, slots, where); var eb = Expr(c.B, slots, where);
                if (ea == null || eb == null) return null;
                pos = c.Op switch { "<" => ea.Lt(eb), "<=" => ea.Le(eb), ">" => ea.Gt(eb), ">=" => ea.Ge(eb), "==" => ea.Eq(eb), "~=" => ea.Ne(eb), _ => null };
                break;
            }
            case Pred p:
                switch (p.Name)
                {
                    // f5050(n) only for the indices vanilla DS2 AI reads (rule AI5b, Ds2AiMachines.InterruptHasDs2Form): 0-20 minus
                    // 6 MissSwing / 8 GuardFinish / 11 ShootReady, which no vanilla script reads; DS1's ParryTiming 24 / LookedTarget 30 …
                    // are past DS2's range
                    // session 11 (ret11), rule A1: DS1 raises Inside_ObserveArea (16) when the observed target enters a region the goal
                    // registered with ai:AddObserveRegion; DS2 has no such registration (no counterpart of the interrupt known), so it is
                    // polled: "an observed target is inside an observed region" = f7070(EventLocation, 0) over the goal's registered
                    // regions (first frame inside = DS1's entering edge)
                    case "Interupt" when (int)p.Args[0] == 16 && observeRegions.Count > 0:
                        pos = InterruptExpr(16);
                        Cov.T($"IsInterupt(Inside_ObserveArea) → polled f7070 on the observed region(s) {string.Join(", ", observeRegions.Select(r => F(r)))}");
                        break;
                    case "InRegion" when p.Args[0] == 0:
                        // rule A1: IsInsideTargetRegion(TARGET_ENE_0, R) → f7070(EventLocation of R, 0) (vanilla ai325000; the boss rule SRegion)
                        pos = E.Fn(7070, E.I(Maps.AsylumScript.RegionId((int)p.Args[1])), E.I(0)).Ne(0);
                        Cov.T($"IsInsideTargetRegion(ENE_0, {F(p.Args[1])}) → f7070({Maps.AsylumScript.RegionId((int)p.Args[1])}, 0)");
                        break;
                    case "Interupt" when M.InterruptHasDs2Form((int)p.Args[0]): Cov.T($"IsInterupt({F(p.Args[0])}) → f5050({F(p.Args[0])})"); pos = M.Interrupt((int)p.Args[0]).Ne(0); break;
                    case "TimerDone":
                        if (!timerTime.TryGetValue(p.Args[0], out float t)) { Cov.N($"IsFinishTimer({F(p.Args[0])}) without a SetTimer: taken as finished"); return E.True; }
                        if (!timerSlot.TryGetValue(p.Args[0], out int ts)) { Cov.S($"IsFinishTimer({F(p.Args[0])}) (no DS2 timer slot left)"); return null; }
                        Cov.T($"IsFinishTimer({F(p.Args[0])}) → f2({ts}) ≥ t"); pos = M.Timer(ts).Ge(t); break;
                    case "InsideTarget" when p.Args[0] == 0 && p.Args[1] == 1: Cov.T("IsInsideTarget(F, a) → f7002() ≤ a/2"); pos = M.Angle.Le((float)(p.Args[2] / 2)); break;
                    case "InsideTarget" when p.Args[0] == 0 && p.Args[1] == 2: Cov.T("IsInsideTarget(B, a) → f7002() ≥ 180 − a/2"); pos = M.Angle.Ge((float)(180 - p.Args[2] / 2)); break;
                    // session 11 (dchr11, coordinator decision), rule AI-NF (deviation, P): DS2 reads no other character's HP (exe jump tables,
                    // dchr11 work\airesearch) → "a friendly non-player within r" f7331(r, -45, 45, -180, 180, 5) (vanilla f7331 sector search,
                    // type 5 = relation 0 non-players): the HP condition is dropped
                    case "NearFriend":
                        Cov.T($"rule AI-NF (deviation): ChkNearLowHpFriend(rate, {F(p.Args[0])}) → f7331({F(p.Args[0])}, -45, 45, -180, 180, 5) ≠ 0 (a friend within r; no DS2 HP read of another character)");
                        pos = E.Fn(7331, E.I((int)p.Args[0]), E.I(-45), E.I(45), E.I(-180), E.I(180), E.I(5)).Ne(0); break;
                    // rule AI-NM (deviation, P): DS2 has no navmesh probe in a direction; forward: no wall within d (f7082(d), the sphere
                    // cast along the facing vector, vanilla 308×); back / left / right: not pressed against anything (f7081() = 0)
                    case "NearMeshF":
                        Cov.T($"rule AI-NM (deviation): IsOnNearMeshByPos(F, {F(p.Args[0])}) → f7082({F(p.Args[0])}) = 0 (no wall ahead)");
                        pos = E.Fn(7082, E.I((int)Math.Round(p.Args[0]))).Eq(0); break;
                    case "NearMeshSide":
                        Cov.T("rule AI-NM (deviation): IsOnNearMeshByPos(B/L/R, d) → f7081() = 0 (not pressed against anything; DS2 has no directional navmesh probe)");
                        pos = E.Fn(7081).Eq(0); break;
                    default: Cov.S($"predicate {p.Key} (no DS2 function)"); return null;
                }
                break;
        }
        if (pos == null) return null;
        return l.Value ? pos : pos.Eq(0);
    }

    /// <summary><see cref="LitOrNull"/> with DS1 queries that have no DS2 form taken as false: such a decision taken true never
    /// happens (null: the rule dies), taken false always holds (true: the guard drops out). Interrupt guards like
    /// "not IsInterupt(ParryTiming)" thus keep the reactions behind them.</summary>
    E? Lit(Literal l, Slots slots, string where)
    {
        var e = LitOrNull(l, slots, where);
        if (e != null) return e;
        return l.Value ? null : E.True;
    }

    // ------------------------------------------------------------------ compile
    public sealed record Result(Dictionary<long, ESD.State> States, long Entry);

    /// <summary>For <c>ai-verify</c>: per decision-region state, the random var → slot it rolls; per DS1 queue (leaf key), the
    /// states that start it (ids after renumbering).</summary>
    public readonly Dictionary<long, Dictionary<int, int>> RegionVarSlots = [];
    public readonly Dictionary<string, HashSet<long>> LeafEntries = [];
    public static string KeyOf(PathResult p) => LeafKey(p);
    /// <summary>DS1 timer index → the SetTimer seconds the translation uses (IsFinishTimer(i) ⇔ f2(slot) ≥ t).</summary>
    public IReadOnlyDictionary<double, float> TimerTimes => timerTime;
    /// <summary>DS1 timer index → DS2 timer slot.</summary>
    public IReadOnlyDictionary<double, int> TimerSlots => timerSlot;
    /// <summary>For <c>ai-verify</c>: the interrupt conditions every interruptible step state carries (in order), their random
    /// var → slot map, and the DS1 interrupt reactions left out (key "n|leaf|literals").</summary>
    public IReadOnlyList<ESD.Condition> InterruptConditions => interrupts.Select(i => i.Cond).ToList();
    public IReadOnlyDictionary<int, int> InterruptVarSlots => interruptSlots.Of.ToDictionary(kv => kv.Key, kv => kv.Value.Slot);
    public readonly HashSet<string> DroppedInterrupts = [];
    /// <summary>The DS1 interrupt numbers compiled (DS1's check order) and those without a DS2 form.</summary>
    public readonly List<int> InterruptsCompiled = [], InterruptsWithoutDs2Form = [];
    readonly Dictionary<long, HashSet<Decision>> RegionNodes = [];
    Node? thinkTree;

    /// <summary>For <c>ai-verify</c>: per region state the random vars a DS1 think path compares inside that region (the path
    /// routed through the simplified decision tree, so a var compared further down on this path is not counted here).</summary>
    public Dictionary<long, HashSet<int>> RegionVarsOnPath(PathResult p)
    {
        var res = new Dictionary<long, HashSet<int>>();
        var value = p.Literals.GroupBy(l => l.Atom.Key).ToDictionary(g => g.Key, g => g.First().Value);
        var route = new List<Decision>();
        var n = thinkTree;
        while (n is Decision d && value.TryGetValue(d.Atom.Key, out bool v)) { route.Add(d); n = v ? d.T : d.F; }
        foreach (var (sid, nodes) in RegionNodes)
        {
            var set = new HashSet<int>();
            foreach (var d in route.Where(nodes.Contains)) foreach (var r in RandsIn(d.Atom)) set.Add(r.Id);
            res[renumber.TryGetValue(sid, out long ns) ? ns : sid] = set;
        }
        return res;
    }
    readonly Dictionary<long, long> renumber = [];

    /// <summary>Compile think + interrupts. <paramref name="activate"/> / <paramref name="interupt"/> are the evaluated runs.</summary>
    public Result Compile(Run activate, Run? interupt)
    {
        Cov.Paths = activate.Paths.Count + (interupt?.Paths.Count ?? 0);
        foreach (var e in activate.Errors.Concat(interupt?.Errors ?? [])) Cov.Notes.Add("Lua: " + e);
        foreach (var p in activate.Paths.Concat(interupt?.Paths ?? [])) foreach (var n in p.Notes) Cov.Notes.Add(n);
        // DS1 timers: the constant each SetTimer(i, t) uses (across think, OnIf)
        foreach (var e in activate.Paths.Concat(interupt?.Paths ?? []).SelectMany(p => p.Effects).OfType<Immediate>().Where(i => i.Api == "SetTimer"))
            if (e.Args.Count > 1 && e.Args[0] is double ti && e.Args[1] is double tv) timerTime[ti] = Math.Max(timerTime.GetValueOrDefault(ti), (float)tv);
        if (goal.HasFunction(goal.OnIfName))
            foreach (var code in activate.Paths.SelectMany(p => p.Effects).OfType<SubGoal>().Where(s => s.Name == "If").Select(s => s.Args.Count > 0 ? s.Args[0] : null).OfType<double>().Distinct())
                foreach (var e in goal.Evaluate(goal.OnIfName, code).Paths.SelectMany(p => p.Effects).OfType<Immediate>().Where(i => i.Api == "SetTimer"))
                    if (e.Args.Count > 1 && e.Args[0] is double ti && e.Args[1] is double tv) timerTime[ti] = Math.Max(timerTime.GetValueOrDefault(ti), (float)tv);
        // session 11 (ret11), rule A1: the regions the goal observes for TARGET_ENE_0 (ai:AddObserveRegion(slot, target, region))
        foreach (var e in activate.Paths.Concat(interupt?.Paths ?? []).SelectMany(p => p.Effects).OfType<Immediate>().Where(i => i.Api == "AddObserveRegion"))
            if (e.Args.Count > 2 && e.Args[1] is double tg && tg == 0 && e.Args[2] is double rg) observeRegions.Add(rg);
        // DS1 timer i → DS2 timer slot (rule AI5c): in DS1 index order onto the slots the template lower resets least (ai103000:
        // slot 2 never; 0 / 1 / 3 are reset by its search / return / approach machines while the upper is outside battle)
        foreach (var (ti, k) in timerTime.Keys.Order().Select((t, k) => (t, k)))
        {
            if (k >= timerSlotOrder.Count) { Cov.S($"DS1 timer {F(ti)} (no DS2 timer slot left)"); continue; }
            var (slot, resets) = timerSlotOrder[k];
            timerSlot[ti] = slot;
            Cov.N($"DS1 timer {F(ti)} (SetTimer {timerTime[ti]:0.##} s) → DS2 timer slot {slot}" + (resets == 0 ? " (never reset by the template lower)"
                : $" (the template lower resets it {resets}× in machines that run outside the battle modes: the timer restarts there)"));
        }

        thinkState = nextState++;
        // interrupts first (step states reference them)
        if (interupt != null && interupt.Paths.Count > 0) interrupts = CompileInterrupts(interupt);
        var tree = Simplify(BuildTree(activate.Paths));
        thinkTree = tree;
        long entry = Region(tree, new Slots(), "think");
        // think waits for the next frame (DS1 re-activates a battle goal on the update after its queue empties): an empty
        // queue (an act whose sub-goals were all dropped, an interrupt that only cleared the queue) can never loop in one frame
        states[thinkState] = S(null, null, Go(entry, M.StateTime.Gt(0)));
        Cov.States = states.Count;
        var all = new Dictionary<long, ESD.State> { [0] = S(null, null, Go(thinkState)) };
        foreach (var (k, v) in states) all[k] = v;
        // state ids 0..N-1 in order, like every vanilla group (interning leaves gaps)
        var order = all.Keys.Order().ToList();
        var newId = order.Select((k, i) => (k, i)).ToDictionary(x => x.k, x => (long)x.i);
        var seen = new HashSet<ESD.Condition>(ReferenceEqualityComparer.Instance);
        void Remap(ESD.Condition c) { if (!seen.Add(c)) return; if (c.TargetState is long t) c.TargetState = newId[t]; foreach (var x in c.Subconditions) Remap(x); }
        var renumbered = new Dictionary<long, ESD.State>();
        foreach (var k in order) { foreach (var c in all[k].Conditions) Remap(c); renumbered[newId[k]] = all[k]; }
        Cov.States = renumbered.Count;
        foreach (var (k, v) in newId) renumber[k] = v;
        foreach (var k in RegionVarSlots.Keys.ToList()) { var v = RegionVarSlots[k]; RegionVarSlots.Remove(k); RegionVarSlots[-1 - newId[k]] = v; }
        foreach (var k in RegionVarSlots.Keys.ToList()) { var v = RegionVarSlots[k]; RegionVarSlots.Remove(k); RegionVarSlots[-1 - k] = v; }
        foreach (var set in LeafEntries.Values) { var mapped = set.Select(x => newId.TryGetValue(x, out long y) ? y : x).ToList(); set.Clear(); foreach (var y in mapped) set.Add(y); }
        return new Result(renumbered, newId[thinkState]);
    }

    /// <summary>Exits of a region as an ordered rule list (first match wins): a rule carries only the decisions its path takes
    /// as TRUE. The rules under a decision's true arm end in a catch-all (the arm's all-false path), so whenever the decision
    /// holds one of them matches; the false arm's rules are reached only when it does not and need no negation. (Random vars on
    /// one tree path never share a slot, so a failed earlier rule never biases a later roll.)</summary>
    static List<(List<Literal> Lits, Node N, HashSet<int> PathVars)> Exits(Node root, HashSet<Decision> inRegion, IEnumerable<int> carried)
    {
        var exits = new List<(List<Literal> Lits, Node N, HashSet<int> PathVars)>();
        void Walk(Node n, List<Literal> lits, HashSet<int> vars)
        {
            if (n is Decision d && inRegion.Contains(d))
            {
                var v2 = new HashSet<int>(vars);
                foreach (var r in RandsIn(d.Atom)) v2.Add(r.Id);
                Walk(d.T, [.. lits, new Literal(d.Atom, true, d.Line)], v2);
                Walk(d.F, lits, v2);
            }
            else exits.Add((lits, n, vars));
        }
        Walk(root, [], [.. carried]);
        return exits;
    }

    /// <summary>A region state's cost (rules, evaluator bytes, largest condition) under the vanilla envelope caps, without
    /// counting anything in the coverage.</summary>
    (int Rules, int Bytes, int MaxCond) RegionCost(Node root, HashSet<Decision> inRegion, Slots colour)
    {
        Cov.Quiet++;
        try
        {
            int rules = 0, bytes = 0, maxCond = 0;
            foreach (var (lits, _, _) in Exits(root, inRegion, colour.Of.Keys))
            {
                var conds = new List<E>();
                bool dead = false;
                foreach (var l in lits) { var e = Lit(l, colour, ""); if (e == null) { dead = true; break; } conds.Add(e); }
                if (dead) continue;
                int b = E.And(conds).Bytes().Length;
                rules++; bytes += b; maxCond = Math.Max(maxCond, b);
            }
            return (rules, bytes, maxCond);
        }
        finally { Cov.Quiet--; }
    }

    /// <summary>Region compile: greedy growth (breadth first) while the region's random vars colour into 4 slots (minus
    /// reserved ones) and its state stays under <see cref="MaxRules"/> rules / <see cref="MaxStateBytes"/> bytes (rule AI10:
    /// vanilla's per-state maxima); the decisions left out start child regions, one transition later.</summary>
    long Region(Node root, Slots carried, string where, long? next = null, IReadOnlySet<int>? reserved = null)
    {
        reserved ??= new HashSet<int>();
        if (root is Leaf rl) return LeafStates(rl.Path, carried, where, next ?? thinkState);
        // grow
        var inRegion = new HashSet<Decision> { (Decision)root };
        var queue = new Queue<Decision>();
        void Enqueue(Decision d) { foreach (var c in new[] { d.T, d.F }) if (c is Decision cd) queue.Enqueue(cd); }
        Enqueue((Decision)root);
        Slots? colour = Colour(PathVars(root, inRegion), carried, 4, reserved) ?? throw new InvalidDataException($"{where}: a single DS1 decision needs more than 4 random slots");
        int capped = 0;
        while (queue.Count > 0)
        {
            var d = queue.Dequeue();
            inRegion.Add(d);
            var c = Colour(PathVars(root, inRegion), carried, 4, reserved);
            if (c == null) { inRegion.Remove(d); continue; }
            var cost = RegionCost(root, inRegion, c);
            if (cost.Rules > MaxRules || cost.Bytes > MaxStateBytes || cost.MaxCond > MaxCondBytes) { inRegion.Remove(d); capped++; continue; }
            colour = c;
            Enqueue(d);
        }
        Cov.Regions++;
        if (capped > 0) Cov.T("decision region split at the vanilla per-state envelope (rule AI10)");
        var exits = Exits(root, inRegion, carried.Of.Keys);
        long id = nextState++;
        RegionVarSlots[id] = colour.Of.ToDictionary(kv => kv.Key, kv => kv.Value.Slot);
        RegionNodes[id] = [.. inRegion];
        var st = new ESD.State();
        foreach (var (rid, s) in colour.Of.Where(kv => !carried.Of.ContainsKey(kv.Key)).Select(kv => (kv.Key, kv.Value)).DistinctBy(x => x.Value.Slot))
            st.EntryCommands.Add(M.RollRandom(s.Slot, s.Lo, s.Hi));
        states[id] = st;
        foreach (var (lits, n, pathVars) in exits)
        {
            var conds = new List<E>();
            bool dead = false;
            foreach (var lit in lits)
            {
                var e = Lit(lit, colour, $"{where} line {lit.Line}");
                if (e == null) { dead = true; Cov.Notes.Add($"{where}: DS1 line {lit.Line} decision {lit} has no DS2 form: that branch is never taken"); break; }
                conds.Add(e);
                Cov.T("DS1 decision");
            }
            if (dead) continue;
            // vars this path already compared and still needed below keep their slots; a var needed below but not yet compared
            // on this path is rolled afresh further down (its slot here may hold another var of this path: colouring only
            // separates vars that meet inside the region)
            var below = new Slots();
            var need = SubtreeVars(n).ToHashSet();
            foreach (var (k, v) in colour.Of) if (need.Contains(k) && pathVars.Contains(k)) below.Of[k] = v;
            long target = n is Leaf l ? LeafStates(l.Path, below, where, next ?? thinkState) : Region(n, below, where, next, reserved);
            st.Conditions.Add(Go(target, E.And(conds)));
        }
        // the all-false path is the last rule and unconditional; a region whose every rule died still moves on
        if (st.Conditions.Count == 0) st.Conditions.Add(Go(next ?? thinkState));
        return id;
    }

    /// <summary>Per root-to-exit path inside the region: the random vars its decisions read.</summary>
    static IEnumerable<List<Rand>> PathVars(Node root, HashSet<Decision> region)
    {
        var res = new List<List<Rand>>();
        void Walk(Node n, List<Rand> acc)
        {
            if (n is Decision d && region.Contains(d))
            {
                var here = acc.Concat(RandsIn(d.Atom)).ToList();
                Walk(d.T, here); Walk(d.F, here);
            }
            else res.Add(acc);
        }
        Walk(root, []);
        return res;
    }

    static IEnumerable<int> SubtreeVars(Node n) => n switch
    {
        Decision d => RandsIn(d.Atom).Select(r => r.Id).Concat(SubtreeVars(d.T)).Concat(SubtreeVars(d.F)),
        Leaf l => l.Path.Effects.OfType<SubGoal>().SelectMany(s => s.Args.Append(s.Life).SelectMany(RandsIn)).Select(r => r.Id),
        _ => [],
    };

    // ------------------------------------------------------------------ leaves: DS1 sub-goal queues
    /// <summary>A step: one machine call (or an inline OnIf). <see cref="Guarded"/>: the machine raises the guard (DS1
    /// guardStateId) and the step state lowers it on exit.</summary>
    sealed record Step(string What, M.Kind? Kind, List<object?> Args, bool Interruptible, string Source, int Line, double? IfCode = null)
    {
        public bool Guarded { get; init; }
    }

    /// <summary>DS1 guardStateId → the DS2 guard flag (rule AI6b): the shield-guard EzStates 9910 / 9920 → 1 (<c>1:5206(1, 0)</c>);
    /// −1 / nil → 0; any other id (4: the casters' pose, not used by the translated Asylum / Firelink thinks) is not a guard.</summary>
    double GuardOf(object? id, string goalName)
    {
        if (id is not double g || g <= 0) return 0;
        if (g is 9910 or 9920) { Cov.T($"{goalName} guard {F(g)} → 1:5206(1, 0) while it runs"); return 1; }
        Cov.S($"{goalName} guard state {F(g)} (not a shield guard: no DS2 form)");
        return 0;
    }

    List<Step> Steps(PathResult p, string where)
    {
        var steps = new List<Step>();
        var subs = p.Effects.OfType<SubGoal>().ToList();
        for (int i = 0; i < subs.Count; i++)
        {
            var s = subs[i];
            object? A(int k) => k < s.Args.Count ? s.Args[k] : null;
            // DS1 goal life (seconds): a number or a roll (GetRandam_Float(2, 3) for a SidewayMove: f4 at the call); nil → 10
            object Life() => s.Life ?? 10.0;
            switch (s.Name)
            {
                case "CommonAttack":
                {
                    // a combo run: isComboAttack true … the first false (DS1 ComboAttack / ComboRepeat → ComboFinal)
                    var run = new List<SubGoal> { s };
                    if (A(6) is true)
                        while (i + 1 < subs.Count && subs[i + 1].Name == "CommonAttack")
                        {
                            run.Add(subs[++i]);
                            if (run[^1].Args.Count > 6 && run[^1].Args[6] is true) continue;
                            break;
                        }
                    var ids = run.Select(r => r.Args.Count > 0 && r.Args[0] is double d ? (int)d : -1).ToArray();
                    double turnTime = A(4) is double tt ? tt : 0, face = A(5) is double fa ? fa : 180;
                    if (ids.Length > 1)
                    {
                        var ch = map.Chains.FirstOrDefault(c => c.Ids.Length >= ids.Length && c.Ids.Take(ids.Length).SequenceEqual(ids));
                        if (ch != null)
                        {
                            Cov.T($"combo {string.Join("→", ids)} → action {50 + ch.Row}{(ch.Ids.Length > ids.Length ? $" (stop after {ids.Length})" : "")}");
                            Cov.ChainsUsed.Add(ch.Row);
                            steps.Add(new Step($"combo {string.Join("→", ids)}", M.Kind.Attack, [(double)(50 + ch.Row), turnTime, face, (double)(ch.Ids.Length > ids.Length ? ids.Length : 0), Life()], BattleInterrupts, s.Via, s.Line));
                            break;
                        }
                        Cov.S($"combo {string.Join("→", ids)} (no DS2 combo row: played as single attacks)");
                    }
                    foreach (var (id, k) in ids.Select((x, k) => (x, k)))
                    {
                        if (!map.Actions.TryGetValue(id, out int act)) { Cov.S($"attack {id} (no DS2 action in the plan)"); continue; }
                        Cov.ActionsUsed.Add(act);
                        Cov.T($"attack {id} → action {act}");
                        var r = run[k];
                        double tt2 = r.Args.Count > 4 && r.Args[4] is double x4 ? x4 : turnTime, fa2 = r.Args.Count > 5 && r.Args[5] is double x5 ? x5 : face;
                        steps.Add(new Step($"attack {id}", M.Kind.Attack, [(double)act, tt2, fa2, 0.0, r.Life is double rl ? rl : 10], BattleInterrupts, r.Via, r.Line));
                    }
                    break;
                }
                case "SpinStep" or "ApproachStep":
                {
                    int ez = A(0) is double d ? (int)d : -1;
                    if (!map.Actions.TryGetValue(ez, out int act)) { Cov.S($"{s.Name} {ez} (DS1 step clip not in the plan)"); break; }
                    Cov.ActionsUsed.Add(act);
                    Cov.T($"{s.Name} {ez} → action {act}");
                    if (s.Name == "SpinStep") steps.Add(new Step($"step {ez}", M.Kind.Attack, [(double)act, 0.0, 180.0, 0.0, Life()], true, s.Via, s.Line));
                    else steps.Add(new Step($"approach-step {ez}", M.Kind.ApproachStep, [(double)act, A(2) ?? 1.5, Life()], BattleInterrupts, s.Via, s.Line));
                    break;
                }
                case "ApproachTarget":
                {
                    // session 11 (ret11), rule A1: to POINT_EVENT (102) = the point the path named with ai:SetEventMoveTarget(P) → the
                    // move-to-point machine on P's EventLocation (archer 1810112: flees to 1812102 when the player crosses 1812101)
                    if (A(0) is 102.0 && p.Effects.OfType<Immediate>().LastOrDefault(i => i.Api == "SetEventMoveTarget" && i.Args.Count > 0 && i.Args[0] is double) is { } emt)
                    {
                        int loc = Maps.AsylumScript.RegionId((int)(double)emt.Args[0]!);
                        Cov.T($"ApproachTarget(POINT_EVENT {F(emt.Args[0])}) → move-to-point {loc}");
                        steps.Add(new Step($"move to {F(emt.Args[0])}", M.Kind.MovePoint, [(double)loc, A(1) ?? 1.0, Life()], BattleInterrupts, s.Via, s.Line));
                        break;
                    }
                    if (A(0) is not 0.0) { Cov.S($"ApproachTarget to {F(A(0))} (a point: no DS2 move-to-point command known)"); return Truncate(steps, s, where); }
                    Cov.T("ApproachTarget");
                    double g = GuardOf(A(4), "ApproachTarget");
                    steps.Add(new Step("approach", M.Kind.Approach, [A(1) ?? 1.0, Walk(A(3)), Life(), g], BattleInterrupts, s.Via, s.Line) { Guarded = g > 0 });
                    break;
                }
                case "LeaveTarget":
                {
                    if (A(0) is not 0.0) { Cov.S($"LeaveTarget from {F(A(0))}"); break; }
                    Cov.T("LeaveTarget");
                    double g = GuardOf(A(4), "LeaveTarget");
                    steps.Add(new Step("leave", M.Kind.Leave, [A(1) ?? 3.0, Walk(A(3)), Life(), g], true, s.Via, s.Line) { Guarded = g > 0 });
                    break;
                }
                case "SidewayMove":
                {
                    Cov.T("SidewayMove");
                    double g = GuardOf(A(5), "SidewayMove");
                    steps.Add(new Step("sideway", M.Kind.Sideway, [A(1) ?? 0.0, A(2) ?? 45.0, Life(), g], true, s.Via, s.Line) { Guarded = g > 0 });
                    break;
                }
                case "Wait" or "Stay":
                    Cov.T(s.Name);
                    steps.Add(new Step("wait", M.Kind.Wait, [s.Life ?? 1.0], true, s.Via, s.Line));
                    break;
                case "Guard":
                    // GOAL_COMMON_Guard(life, guardStateId, target, …): 9910 = shield up (DS1 a00_9900 guard idle in the network)
                    if (GuardOf(A(0), "Guard") > 0) steps.Add(new Step("guard", M.Kind.Guard, [s.Life ?? 1.0], true, s.Via, s.Line) { Guarded = true });
                    else steps.Add(new Step("guard→wait", M.Kind.Wait, [s.Life ?? 1.0], true, s.Via, s.Line));
                    break;
                case "Turn":
                {
                    Cov.T("Turn");
                    double ang = A(1) is double a && a > 0 ? a : 10;
                    steps.Add(new Step("turn", M.Kind.Turn, [ang, Life()], BattleInterrupts, s.Via, s.Line));
                    break;
                }
                case "KeepDist":
                {
                    Cov.T("KeepDist");
                    double g = GuardOf(A(5), "KeepDist");
                    steps.Add(new Step("keepdist", M.Kind.KeepDist, [A(1) ?? 2.0, A(2) ?? 4.0, Walk(A(4)), Life(), g], true, s.Via, s.Line) { Guarded = g > 0 });
                    break;
                }
                case "If":
                    Cov.T("If (OnIf at its turn)");
                    steps.Add(new Step("if", null, [], false, s.Via, s.Line, A(0) as double? ?? 0));
                    break;
                default:
                    Cov.S($"goal {s.Name}");
                    break;
            }
        }
        return steps;
    }

    void Entry(PathResult p, long id)
    {
        string k = LeafKey(p);
        if (!LeafEntries.TryGetValue(k, out var set)) LeafEntries[k] = set = [];
        set.Add(id);
    }

    List<Step> Truncate(List<Step> steps, SubGoal at, string where)
    {
        Cov.Notes.Add($"{where}: DS1 line {at.Line}: {at.Name} to a point is not translated; the rest of that queue is dropped");
        return steps;
    }

    static double Walk(object? v) => v is true ? 1 : 0;   // DS1 bWalk: nil (e.g. the undefined global True) and false run

    long LeafStates(PathResult p, Slots carried, string where, long next)
    {
        string key = LeafKey(p) + "→" + next + "|" + string.Join(",", carried.Of.Select(kv => $"{kv.Key}:{kv.Value.Slot}"));
        if (leafCache.TryGetValue(key, out long cached)) { Entry(p, cached); return cached; }
        Cov.Leaves++;
        var steps = Steps(p, where);
        foreach (var s in steps.Skip(1))
            foreach (var r in s.Args.SelectMany(RandsIn).Where(r => carried.Of.ContainsKey(r.Id)).DistinctBy(r => r.Id))
                Cov.N($"{where}: DS1 roll {r} is compared in the think and read again by a later sub-goal ({s.What}): rolled afresh there");
        var imm = new List<ESD.CommandCall>();
        foreach (var i in p.Effects.OfType<Immediate>())
        {
            switch (i.Api)
            {
                case "SetTimer" when i.Args.Count > 0 && i.Args[0] is double ti && timerSlot.TryGetValue(ti, out int ts):
                    imm.Add(M.ResetTimer(ts)); Cov.T($"SetTimer({F(ti)}, t) → 1:2({ts})"); break;
                default: Cov.S($"ai:{i.Api} (no DS2 command)"); break;
            }
        }
        long id = Sequence(steps, 0, carried, where, next, imm);
        leafCache[key] = id;
        Entry(p, id);
        return id;
    }

    long Sequence(List<Step> steps, int from, Slots carried, string where, long next, List<ESD.CommandCall>? firstEntry = null)
    {
        if (from >= steps.Count)
        {
            if (firstEntry is { Count: > 0 }) { long only = nextState++; states[only] = S(firstEntry, null, Go(next)); return only; }
            return next;
        }
        var step = steps[from];
        if (step.IfCode is double code)
        {
            long cont = Sequence(steps, from + 1, new Slots(), where, next);
            var run = goal.Evaluate(goal.OnIfName, code);
            foreach (var n in run.Paths.SelectMany(pp => pp.Notes).Distinct()) Cov.Notes.Add($"OnIf: {n}");
            long ifEntry = run.Paths.Count == 0 ? cont : Region(Simplify(BuildTree(run.Paths)), new Slots(), $"{where}/OnIf({F(code)})", cont);
            if (firstEntry is { Count: > 0 }) { long pre = nextState++; states[pre] = S(firstEntry, null, Go(ifEntry)); return pre; }
            return ifEntry;
        }
        long nextId = Sequence(steps, from + 1, new Slots(), where, next);
        var st = new ESD.State();
        if (firstEntry != null) st.EntryCommands.AddRange(firstEntry);
        var ints = step.Interruptible ? interrupts : [];
        var slots = carried.Clone();
        var args = new List<E>();
        // random rolls the arguments read (DS1: GetRandam_Float(0.5, 1) for a Wait's life, GetRandam_Int(0, 1) / (30, 45) for
        // a SidewayMove's side / angle, r and r + 0.5 for Watching_Parry_Chance_Act's KeepDist): each is rolled into a slot by
        // this state's entry (1:1(slot, lo, hi), floats ×1000) and the argument reads f1(slot) (×0.001) — vanilla primitives
        // only (fix pass, rule AI5d: no f4 outside a condition; a function-valued machine argument is vanilla's 6:X(p0,
        // f5017(), 30)). An argument takes a slot the interrupt rolls (after the call) leave free when there is one; only when all
        // four are needed does an interrupt roll reuse it, relying on arguments being evaluated when the call runs (P, like every
        // command argument).
        var stepArgs = step.Args.ToList();
        // slots the interrupt rolls do not use come first, so an argument keeps its slot even if a machine read it lazily
        HashSet<int> intSlots = ints.Count > 0 ? interruptSlots.Of.Values.Select(v => v.Slot).ToHashSet() : [];
        var slotOrder = Enumerable.Range(0, 4).OrderBy(s => intSlots.Contains(s) ? 1 : 0).ThenBy(s => s).ToList();
        foreach (var r in step.Args.SelectMany(RandsIn).DistinctBy(r => r.Id))
        {
            if (slots.Of.ContainsKey(r.Id)) continue;
            int slot = slotOrder.FirstOrDefault(s => !slots.Of.Values.Any(v => v.Slot == s), -1);
            if (slot < 0)
            {
                Cov.S($"{step.What}: random argument {r} (no slot left: its mean {F((r.Lo + r.Hi) / 2)} is used)");
                for (int i = 0; i < stepArgs.Count; i++) stepArgs[i] = Subst(stepArgs[i], r.Id, (r.Lo + r.Hi) / 2);
                continue;
            }
            var (lo, hi) = RangeOf(r);
            slots.Of[r.Id] = (slot, lo, hi);
            st.EntryCommands.Add(M.RollRandom(slot, lo, hi));
            Cov.T("GetRandam as a sub-goal argument → 1:1(slot) in the step's entry, argument f1(slot)");
        }
        foreach (var a in stepArgs)
        {
            var e = Expr(a, slots, $"{where} {step.What}");
            if (e == null) { Cov.S($"{step.What} argument {F(a)}"); e = E.I(0); }
            args.Add(e);
        }
        st.EntryCommands.Add(Call(machines[step.Kind!.Value], [.. args]));
        // interrupt rolls after the call (DS1 rolls inside _Interupt; here once per sub-goal, at its start)
        if (ints.Count > 0)
            foreach (var s in interruptSlots.Of.Values.DistinctBy(v => v.Slot)) st.EntryCommands.Add(M.RollRandom(s.Slot, s.Lo, s.Hi));
        // DS1's guard ends with its goal: lowered whenever this state is left (return, failure, an interrupt abandoning it)
        if (step.Guarded) st.ExitCommands.Add(M.GuardDown);
        foreach (var ie in ints) st.Conditions.Add(ie.Cond);   // the same Condition objects in every step state: written once (vanilla shares conditions within a group too)
        st.Conditions.AddRange(Returned(nextId, thinkState));
        return Intern(st);
    }

    static object? Subst(object? o, int id, double v) => o switch
    {
        Rand r when r.Id == id => v,
        Arith a => new Arith(a.Op, Subst(a.A, id, v)!, Subst(a.B, id, v)!),
        Abs ab => new Abs(Subst(ab.A, id, v)!),
        _ => o,
    };

    // ------------------------------------------------------------------ interrupts
    sealed record InterruptExit(ESD.Condition Cond, long Target, string What);

    /// <summary>One state per distinct content: queues ending alike (the same spacing after different attacks, the same
    /// combo before different spacing) share their tail states, as the queue's states are built back to front.</summary>
    long Intern(ESD.State st)
    {
        var sb = new System.Text.StringBuilder();
        void Cmds(string k, List<ESD.CommandCall> cs) { foreach (var c in cs) { sb.Append(k).Append(c.CommandBank).Append(':').Append(c.CommandID); foreach (var a in c.Arguments) sb.Append(',').Append(Convert.ToHexString(a)); sb.Append(';'); } }
        Cmds("E", st.EntryCommands); Cmds("W", st.WhileCommands); Cmds("X", st.ExitCommands);
        foreach (var c in st.Conditions) { if (interruptConds.Contains(c)) sb.Append("I").Append(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(c)).Append(';'); else { sb.Append(Convert.ToHexString(c.Evaluator)).Append('>').Append(c.TargetState); Cmds("P", c.PassCommands); sb.Append('{'); foreach (var x in c.Subconditions) { sb.Append(Convert.ToHexString(x.Evaluator)).Append('>').Append(x.TargetState).Append(';'); } sb.Append('}'); } }
        string key = sb.ToString();
        if (interned.TryGetValue(key, out long id)) return id;
        id = nextState++;
        states[id] = st;
        interned[key] = id;
        return id;
    }
    readonly Dictionary<string, long> interned = [];
    readonly HashSet<ESD.Condition> interruptConds = new(ReferenceEqualityComparer.Instance);
    Slots interruptSlots = new();

    static bool Fires(PathResult p) => p.Return is true || (p.Return is double d && d != 0);

    /// <summary>DS1's interrupt tree with only interrupt <paramref name="n"/> set: every IsInterupt(m) decision takes its true arm
    /// for m = n and its false arm otherwise.</summary>
    static Node Specialize(Node node, int n) => node switch
    {
        Decision d when d.Atom is Pred p && p.Name == "Interupt" => Specialize((int)p.Args[0] == n ? d.T : d.F, n),
        Decision d => new Decision(d.Atom, d.Line, Specialize(d.T, n), Specialize(d.F, n)),
        _ => node,
    };

    /// <summary>
    /// DS1 <c>_Interupt</c> → the interrupt conditions of every interruptible step state (rule AI7b, fix pass). The DS1 tree is
    /// compiled once per interrupt n that has a DS2 form, in DS1's own check order (first appearance in the tree), as DS1's
    /// reaction to that interrupt alone (<see cref="Specialize"/>): ordered rules "f5050(n) ≠ 0 ∧ the decisions DS1 takes", a
    /// false arm carrying the negated decision only where its true arm has no-op leaves. Exact for one interrupt per frame
    /// (ai-verify checks every such DS1 path); with several at once the first matching rule in DS1's check order wins (DS1's own
    /// code may combine them: such paths are counted, not modelled). The rules share ≤ 3 random slots (rolled at the sub-goal's
    /// start) and must fit the vanilla per-state envelope with the state's two return conditions (≤ <see cref="MaxRules"/> + 1
    /// rules, ≤ <see cref="MaxStateBytes"/> bytes); rules that do not fit are left out in reverse priority (coverage) and their
    /// situations guarded off the later rules.
    /// </summary>
    List<InterruptExit> CompileInterrupts(Run run)
    {
        var res = new List<InterruptExit>();
        foreach (var p in run.Paths.Where(p => !Fires(p) && p.Effects.OfType<SubGoal>().Any())) Cov.S("interrupt path adding sub-goals but returning false");
        if (!run.Paths.Any(Fires)) return res;
        var tree = Simplify(BuildTree(run.Paths));
        var order = new List<int>();
        void Find(Node n) { if (n is not Decision d) return; if (d.Atom is Pred p && p.Name == "Interupt" && !order.Contains((int)p.Args[0])) order.Add((int)p.Args[0]); Find(d.T); Find(d.F); }
        Find(tree);
        foreach (int n in order)
        {
            if (M.InterruptHasDs2Form(n)) InterruptsCompiled.Add(n);
            else { InterruptsWithoutDs2Form.Add(n); Cov.S($"IsInterupt({n}) (no DS2 form: {(n > 20 ? "past DS2's f5050 range 0-20" : "no vanilla DS2 AI reads f5050(" + n + ")")}; its reactions never fire)"); }
        }
        var trees = InterruptsCompiled.Select(n => (N: n, T: Simplify(Specialize(tree, n)))).ToList();
        var drop = new HashSet<(int, PathResult)>();
        bool Noop(int n, Leaf l) => !Fires(l.Path) || drop.Contains((n, l.Path));
        bool HasNoop(int n, Node x) => x is Leaf l ? Noop(n, l) : x is Decision d && (HasNoop(n, d.T) || HasNoop(n, d.F));
        List<(int N, List<Literal> Lits, List<Rand> TreeVars, PathResult Leaf)> Rules()
        {
            var rules = new List<(int, List<Literal>, List<Rand>, PathResult)>();
            foreach (var (n, t) in trees)
            {
                void R(Node x, List<Literal> acc, List<Rand> vars)
                {
                    if (x is Leaf l) { if (!Noop(n, l)) rules.Add((n, acc, vars, l.Path)); return; }
                    var d = (Decision)x;
                    var v2 = vars.Concat(RandsIn(d.Atom)).ToList();
                    R(d.T, [.. acc, new Literal(d.Atom, true, d.Line)], v2);
                    R(d.F, HasNoop(n, d.T) ? [.. acc, new Literal(d.Atom, false, d.Line)] : acc, v2);
                }
                R(t, [], []);
            }
            return rules;
        }
        E? RuleExpr(int n, List<Literal> lits, Slots slots)
        {
            var conds = new List<E> { InterruptExpr(n) };   // session 11 (ret11), rule A1: 16 polled on the observed regions
            foreach (var l in lits) { var e = Lit(l, slots, $"interrupt line {l.Line}"); if (e == null) return null; conds.Add(e); }
            return E.And(conds);
        }
        // fit: slots, conditions and bytes of a step state (its 2 return conditions take 12 bytes); drop from the end until
        // stable (a drop adds negations to sibling rules, which may cost bytes)
        List<(int N, List<Literal> Lits, List<Rand> TreeVars, PathResult Leaf)> kept;
        for (int round = 0; ; round++)
        {
            kept = Rules();
            var keep = new List<(int N, List<Literal> Lits, List<Rand> TreeVars, PathResult Leaf)>();
            int bytes = 12;
            bool changed = false;
            Cov.Quiet++;
            try
            {
                foreach (var r in kept)
                {
                    var trySlots = Colour(keep.Append(r).Select(x => x.TreeVars), new Slots(), 3, new HashSet<int>());
                    int b = trySlots == null ? int.MaxValue : RuleExpr(r.N, r.Lits, trySlots)?.Bytes().Length ?? 0;
                    if (b == 0) { keep.Add(r); continue; }   // dies on a decision with no DS2 form: costs nothing
                    if (trySlots == null || keep.Count + 1 > MaxRules + 1 || b > MaxCondBytes || bytes + b > MaxStateBytes) { drop.Add((r.N, r.Leaf)); changed = true; continue; }
                    keep.Add(r); bytes += b;
                }
            }
            finally { Cov.Quiet--; }
            if (!changed) break;
            if (round > 20) throw new InvalidDataException("interrupt rules: no stable fit under the vanilla per-state envelope");
        }
        var allRules = trees.Sum(t => { int c = 0; void R(Node x) { if (x is Leaf l) { if (Fires(l.Path)) c++; } else if (x is Decision d) { R(d.T); R(d.F); } } R(t.T); return c; });
        if (drop.Count > 0) Cov.S($"interrupt reactions left out to stay in the vanilla per-state envelope / 3 random slots ({drop.Count} of {allRules} single-interrupt DS1 reactions)");
        foreach (var (n, leaf) in drop) DroppedInterrupts.Add($"{n}|{LeafKey(leaf with { Effects = [.. leaf.Effects.Where(e => e is not Clear)] })}");
        interruptSlots = Colour(kept.Select(x => x.TreeVars), new Slots(), 3, new HashSet<int>()) ?? new Slots();
        foreach (var (n, lits, _, p) in kept)
        {
            var e = RuleExpr(n, lits, interruptSlots);
            if (e == null) { Cov.S($"interrupt {n} reactions behind a DS1 decision with no DS2 form (never taken)"); continue; }
            if (!p.Effects.OfType<Clear>().Any()) Cov.N($"interrupt path f5050({n}) ∧ {string.Join(" ∧ ", lits)} returns true without ClearSubGoal: its sub-goals replace the queue");
            long target = LeafStates(p with { Effects = [.. p.Effects.Where(e2 => e2 is not Clear)] }, new Slots(), "interrupt", thinkState);
            var cond = Go(target, e);
            interruptConds.Add(cond);
            res.Add(new InterruptExit(cond, target, $"f5050({n}) ∧ " + string.Join(" ∧ ", lits)));
            Cov.T("interrupt reaction (single interrupt, DS1 check order)");
        }
        return res;
    }
}
