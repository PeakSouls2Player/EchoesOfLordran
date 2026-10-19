using System.Globalization;
using static Lordran.Tools.Build.Ai.Boss.LuaSyntax;

namespace Lordran.Tools.Build.Ai.Boss;

/// <summary>
/// Runs DS1's own AI Lua (decompiled battle goal + DSR's aiCommon library) symbolically and turns each goal function into
/// a decision tree of effects (session 8, track boss; rulebook §5 "Lua AI → DS2 AI ESD", <see cref="Ds2AiEsd"/> compiles
/// the trees). Nothing of a battle goal is re-authored: the interpreter executes the Lua statements as Lua does, with the
/// runtime queries the engine answers (GetDist, GetRandam_*, GetNumber, IsInsideTargetRegion, IsInsideObserve, IsInterupt,
/// GetHpRate) as symbolic values. A test of a symbolic value forks the run: every path is explored by re-executing the
/// function with a list of branch decisions (a decision that the facts of the path already settle is not a fork:
/// interval reasoning on distances, rolls and numbers, so "fate &lt;= 10" false then "fate &lt;= 20" is explored once per
/// real outcome). Effects are recorded in order: rolls (where DS1 calls GetRandam), SetNumber, ClearSubGoal and the
/// engine-native sub-goals the goal Lua bottoms out in.
/// <para><b>Goal library.</b> AddSubGoal of a goal that aiCommon implements in Lua (Attack, ComboAttack, ComboRepeat,
/// ComboFinal, AttackTunableSpin, NonspinningAttack, ApproachTarget, …) runs that goal's own _Activate with its parameters
/// (goal:GetParam / GetLife), so DS1's defaults (turn time 1.5 s, face angle 20°, combo flags) come from DS1's code. Goals
/// without Lua (or with an empty _Activate) are the engine's natives and become effects: CommonAttack, MoveToSomewhere,
/// Wait, Turn, SidewayMove, SpinStep, LeaveTarget.</para>
/// <para><b>Template rule.</b> Common_Battle_Activate (common_battle_func.lua: weighted act choice with GetRandam_Int(1,
/// total) against cumulative weights in act order, then the act's GetWellSpace odds against GetRandam_Int(1, 100) for the
/// after-act function) is applied as the rule it is, with the act and the after-act left as calls (each becomes its own
/// DS2 machine; the odds part runs at the end of the act). Its doAdmirer/team branches (crowd behaviour) and the debug
/// force-act index don't apply to bosses (NpcThinkParam.thinkAttr_doAdmirer 0) and are reported if a goal needs them.</para>
/// </summary>
public sealed class Ds1AiInterpreter
{
    // ================================================================== values
    public abstract record V;
    public sealed record VNum(double X) : V;
    public sealed record VStr(string S) : V;
    public sealed record VBool(bool B) : V;
    public sealed record VNil : V { public static readonly VNil I = new(); }
    public sealed record VTable : V { public readonly Dictionary<object, V> Items = []; public bool Equals(VTable? o) => ReferenceEquals(this, o); public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
    public sealed record VFunc(FuncBody Body, Env Closure) : V;
    public sealed record VNative(string Kind, GoalCtx? Goal = null) : V;
    public sealed record VSym(SExpr E) : V;

    /// <summary>A goal object as Lua sees it inside a goal's _Activate: its parameters and life.</summary>
    public sealed record GoalCtx(List<V> Params, V Life);

    // ================================================================== symbolic expressions
    public abstract record SExpr { public virtual bool IsBool => false; }
    public sealed record SConst(double X) : SExpr;
    /// <summary>ai:GetDist(target) / GetDistYSigned(target).</summary>
    public sealed record SDist(int Target, bool Y = false) : SExpr;
    /// <summary>A GetRandam_Int / GetRandam_Float roll (id = the roll's number in its run).</summary>
    public sealed record SRand(int Id, double Lo, double Hi, bool IsFloat) : SExpr;
    /// <summary>ai:GetNumber(slot) as it was when the function started.</summary>
    public sealed record SNumber(int Slot) : SExpr;
    public sealed record SHpRate(int Target) : SExpr;
    /// <summary>Session 11 (burgchr11), rule E-REQ: ai:GetEventRequest(slot) of a think with registered channels.</summary>
    public sealed record SEventRequest(int Slot, int Think) : SExpr;
    public sealed record SRegion(int Target, int Region) : SExpr { public override bool IsBool => true; }
    public sealed record SObserve(int Slot) : SExpr { public override bool IsBool => true; }
    public sealed record SInterrupt(int Id) : SExpr { public override bool IsBool => true; }
    public sealed record SSpEffect(int Target, int Id) : SExpr { public override bool IsBool => true; }
    /// <summary>Session 11 (dchr11), rule AI-IT: the target's unsigned bearing 0-180° (DS2 f7002) and its side (right: DS2 f7003(0, 179) ≠ 0);
    /// ai:IsInsideTarget(TARGET_ENE_0, dir, a) is decided as conditions on these two, so the interval facts prune contradictory cones.</summary>
    public sealed record SBearing : SExpr;
    public sealed record SRightSide : SExpr { public override bool IsBool => true; }
    /// <summary>Session 11 (dchr11), rule AI-TM: ai:IsFinishTimer(index).</summary>
    public sealed record STimerDone(int Index) : SExpr { public override bool IsBool => true; }
    public sealed record SBin(string Op, SExpr L, SExpr R) : SExpr { public override bool IsBool => Op is "<" or ">" or "<=" or ">=" or "==" or "~="; }
    public sealed record SNot(SExpr E) : SExpr { public override bool IsBool => true; }

    // ================================================================== effects
    public abstract record Effect;
    public sealed record ERoll(int Id, double Lo, double Hi, bool IsFloat) : Effect;
    public sealed record ESetNumber(int Slot, SExpr Value) : Effect;
    public sealed record EClear : Effect;
    /// <summary>An engine-native sub-goal: name (goal_list's GOAL_COMMON_X without the prefix), life, parameters.</summary>
    public sealed record EGoal(string Goal, V Life, List<V> Args) : Effect;
    public sealed record EObserve(int Slot, int Target, int Dummy, double Radius) : Effect;
    public sealed record EEventMoveTarget(int Region) : Effect;
    /// <summary>Session 11 (dchr11), rule AI-TM: ai:SetTimer(index, seconds).</summary>
    public sealed record ETimer(int Index, double Seconds) : Effect;
    /// <summary>Common_Battle_Activate chose act <paramref name="Act"/> (1-20), the Lua function registered for it
    /// (REGIST_FUNC), and the after-act function the goal passed (atkAfterFunc).</summary>
    public sealed record ECallAct(int Act, string Function, string? AfterFunction) : Effect
    {
        /// <summary>Session 11 (ret11), rule AI-D1: the extra argument of a DS1 default act (defActNN(ai, goal, defFuncParamTbl[N])).</summary>
        public V? Extra { get; init; }
    }
    /// <summary>The act's GetWellSpace odds won: the after-act function runs.</summary>
    public sealed record ECallAfter : Effect;
    public sealed record EDecision(SExpr Cond, bool Value) : Effect;

    /// <summary>One complete run: the stream of effects and decisions, and the function's return value.</summary>
    public sealed record RunPath(List<Effect> Stream, V Return);

    // ================================================================== environment
    public sealed class Env(Env? parent)
    {
        public readonly Dictionary<string, V> Vars = [];
        public readonly Env? Parent = parent;
        public Env? Find(string n) { for (var e = this; e != null; e = e.Parent) if (e.Vars.ContainsKey(n)) return e; return null; }
    }

    // ================================================================== library
    readonly Env globals = new(null);
    readonly Dictionary<double, string> goalNames = [];
    public List<string> Unsupported { get; } = [];
    public List<string> Loaded { get; } = [];
    /// <summary>Session 11 (dchr11): every loaded file's path and text.</summary>
    public List<(string File, string Text)> Sources { get; } = [];
    /// <summary>Session 11 (burgchr11): the boss's DS1 NpcThinkParam and battle goal ids, the constants ai:GetNpcThinkParamID() and
    /// goal:GetBattleGoalId() return (the Titanite Demon's battle-area regions are keyed by its think id: with nil every in-area test
    /// failed and the AI always walked home).</summary>
    public int ThinkParamId { get; set; }
    /// <summary>Session 11 (dchr11): rules AI-IT (IsInsideTarget), AI-TM (SetTimer / IsFinishTimer) and boolean queries compared with true /
    /// false, on for the Depths' characters (AiPorter); off = the earlier answers (false / taken as never equal), so the Asylum's and the
    /// Burg's AIs stay as built (the Hellkite's Activate would pass the 20,000-path budget, the Gargoyles' call chain DS2's depth 8).</summary>
    public bool QueryRules { get; set; }
    public int BattleGoalId { get; set; }

    /// <summary>Goals the engine implements (no Lua, or an empty _Activate): kept as effects.</summary>
    public static readonly HashSet<string> NativeGoals = ["CommonAttack", "MoveToSomewhere", "Wait", "Turn", "SidewayMove", "SpinStep", "LeaveTarget", "KeepDist", "Guard", "TurnAround"];

    /// <param name="files">Lua files in load order (later definitions win, as when the engine loads them): DSR's aiCommon
    /// first, then the map's script folder.</param>
    public Ds1AiInterpreter(IEnumerable<string> files)
    {
        foreach (var f in files)
        {
            List<Stmt> chunk;
            Sources.Add((f, File.ReadAllText(f)));   // session 11 (dchr11): rule AI-TM reads the goal script's SetTimer constants
            try { chunk = Parse(File.ReadAllText(f)); }
            catch (FormatException e) { Unsupported.Add($"{System.IO.Path.GetFileName(f)}: {e.Message}"); continue; }
            var fileEnv = new Env(globals);
            var st = new RunState(this, []);
            foreach (var s in chunk)
            {
                try { st.ExecTop(s, fileEnv); }
                catch (Exception e) when (e is not NeedDecision) { Unsupported.Add($"{System.IO.Path.GetFileName(f)}:{s.Line}: top level: {e.Message}"); }
            }
            Loaded.Add(System.IO.Path.GetFileName(f));
        }
    }

    /// <summary>The standard file list: every DSR aiCommon script, then the given map's (e.g. "m18_01") .lua files.</summary>
    public static IEnumerable<string> StandardFiles(string decompiledDir, string map)
    {
        // the definition files first (constants: AI_DIR_TYPE_*, TARGET_*, GOAL_COMMON_* …), so the goal scripts' top-level
        // REGISTER_GOAL(GOAL_COMMON_X, "Name") calls see their ids
        string[] first = ["ai_define.lua", "goal_list.lua", "goal_list_dlc.lua", "logic_list.lua", "event_list.lua"];
        var common = Directory.GetFiles(System.IO.Path.Combine(decompiledDir, "aiCommon"), "*.lua").OrderBy(f => f, StringComparer.Ordinal).ToList();
        return common.Where(f => first.Contains(System.IO.Path.GetFileName(f))).OrderBy(f => Array.IndexOf(first, System.IO.Path.GetFileName(f)))
            .Concat(common.Where(f => !first.Contains(System.IO.Path.GetFileName(f))))
            .Concat(Directory.GetFiles(System.IO.Path.Combine(decompiledDir, map), "*.lua").OrderBy(f => f, StringComparer.Ordinal));
    }

    public V Global(string name) => globals.Vars.TryGetValue(name, out var v) ? v : VNil.I;

    /// <summary>A goal id's name: REGISTER_GOAL's name for goals with Lua, else goal_list's GOAL_COMMON_X (natives like
    /// Wait 2000, MoveToSomewhere 2019, SidewayMove 2017, SpinStep 2020 have no script).</summary>
    public string? GoalName(double id)
    {
        if (goalNames.TryGetValue(id, out var n)) return n;
        foreach (var (k, v) in globals.Vars)
            if (v is VNum num && num.X == id && k.StartsWith("GOAL_COMMON_")) return k["GOAL_COMMON_".Length..];
        return null;
    }

    // ================================================================== enumeration
    sealed class NeedDecision : Exception { }

    /// <summary>
    /// Every path of a call to global function <paramref name="name"/>(ai, goal, extra…): DFS over decisions; the run
    /// stops at the first undecided test beyond the given prefix, and both outcomes are explored.
    /// <paramref name="mode"/>: "activate" stops a run at Common_Battle_Activate's act choice (ECallAct); "act" appends the
    /// GetWellSpace part of Common_Battle_Activate to the act's return (ERoll + decision + ECallAfter).
    /// </summary>
    public List<RunPath> Paths(string name, string mode = "", params V[] extra)
    {
        if (Global(name) is not VFunc fn) throw new InvalidDataException($"DS1 AI function {name} not found");
        var done = new List<RunPath>();
        var stack = new Stack<List<bool>>();
        stack.Push([]);
        int guard = 0;
        while (stack.Count > 0)
        {
            if (++guard > 20000) throw new InvalidDataException($"{name}: too many paths");
            var prefix = stack.Pop();
            var st = new RunState(this, prefix) { Mode = mode };
            try
            {
                var ret = st.CallFunc(fn, [new VNative("ai"), new VNative("goal", new GoalCtx([], new VNum(0))), .. extra]);
                if (mode == "act") st.AfterOdds(ret);
                done.Add(new RunPath(st.Stream, ret));
            }
            catch (NeedDecision)
            {
                stack.Push([.. prefix, false]);
                stack.Push([.. prefix, true]);
            }
            catch (StopRun) { done.Add(new RunPath(st.Stream, VNil.I)); }
        }
        return done;
    }

    sealed class StopRun : Exception { }

    // ================================================================== one run
    sealed class Interval
    {
        public double Lo = double.NegativeInfinity, Hi = double.PositiveInfinity;
        public bool LoIn = true, HiIn = true, Int;
        public Interval Clone() => (Interval)MemberwiseClone();
    }

    sealed class RunState(Ds1AiInterpreter lib, List<bool> prefix)
    {
        public string Mode = "";
        /// <summary>Loading a file: assignments to undeclared names define library globals.</summary>
        public bool Loading;
        public readonly List<Effect> Stream = [];
        int decisions, rolls;
        readonly Dictionary<string, Interval> facts = [];
        readonly Dictionary<string, bool> boolFacts = [];
        readonly Dictionary<int, V> numbers = [];
        readonly Env runGlobals = new(lib.globals);
        int depth;

        // ---------- decisions
        bool Decide(SExpr c)
        {
            if (Settled(c) is bool b) return b;
            int k = decisions++;
            if (k >= prefix.Count) throw new NeedDecision();
            bool v = prefix[k];
            Stream.Add(new EDecision(c, v));
            Learn(c, v);
            return v;
        }

        static string Key(SExpr e) => e.ToString();

        /// <summary>Session 11 (dchr11), rule AI-IT: DS1 IsInsideTarget(TARGET_ENE_0, dir, 2h): the bearing within h of the axis (F 0°,
        /// R +90°, L −90°, B 180°) as decisions on the unsigned bearing θ and the side: F θ ≤ h; B θ ≥ 180 − h; R right ∧ θ ∈ [90 − h, 90 + h],
        /// or (h > 90) left ∧ (θ ≤ h − 90 ∨ θ ≥ 270 − h); L mirrored.</summary>
        bool InsideTarget(int dir, double h)
        {
            if (h >= 180) return true;
            bool Cmp(string op, double v) => Decide(new SBin(op, new SBearing(), new SConst(v)));
            switch (dir)
            {
                case 1: return Cmp("<=", h);
                case 2: return Cmp(">=", 180 - h);
                default:
                {
                    bool right = Decide(new SRightSide());
                    bool axisSide = dir == 4 ? right : !right;
                    if (axisSide) return (90 - h <= 0 || Cmp(">=", 90 - h)) && (90 + h >= 180 || Cmp("<=", 90 + h));
                    return h > 90 && (Cmp("<=", h - 90) || Cmp(">=", 270 - h));
                }
            }
        }

        bool? Settled(SExpr c)
        {
            if (c is SNot n) return Settled(n.E) is bool b ? !b : null;
            if (c is SRegion or SObserve or SInterrupt or SSpEffect or SRightSide or STimerDone) return boolFacts.TryGetValue(Key(c), out var bv) ? bv : null;
            if (c is SBin { IsBool: true } cmp)
            {
                if (cmp.R is SConst rc && Range(cmp.L) is { } iv) return Implies(iv, cmp.Op, rc.X);
                if (cmp.L is SConst lc && Range(cmp.R) is { } iv2) return Implies(iv2, Flip(cmp.Op), lc.X);
                return boolFacts.TryGetValue(Key(c), out var b2) ? b2 : null;
            }
            return null;
        }

        static string Flip(string op) => op switch { "<" => ">", ">" => "<", "<=" => ">=", ">=" => "<=", _ => op };
        static string Negate(string op) => op switch { "<" => ">=", ">" => "<=", "<=" => ">", ">=" => "<", "==" => "~=", "~=" => "==", _ => op };

        Interval? Range(SExpr e)
        {
            if (facts.TryGetValue(Key(e), out var iv)) return iv;
            return e switch
            {
                SRand r => new Interval { Lo = r.Lo, Hi = r.Hi, Int = !r.IsFloat },
                // session 11 (dchr11), rule AI-ND (with QueryRules, the Depths): DS1's GetDist is the capsule-surface distance and goes negative
                // when the target is inside the character's capsule (the Gaping Dragon's Act01 / Act06 at targetDist ≤ −0.5 / −3); f7001 likewise (P)
                SDist { Y: false } => new Interval { Lo = lib.QueryRules ? double.NegativeInfinity : 0 },
                SNumber => new Interval { Int = true },
                SHpRate => new Interval { Lo = 0, Hi = 1 },
                SBearing => new Interval { Lo = 0, Hi = 180 },   // session 11 (dchr11), rule AI-IT
                _ => null,
            };
        }

        /// <summary>Does every value of the interval satisfy "x op c" (true), none (false), or some (null)?</summary>
        static bool? Implies(Interval iv, string op, double c)
        {
            double lo = iv.Lo, hi = iv.Hi;
            bool loIn = iv.LoIn, hiIn = iv.HiIn;
            if (iv.Int)
            {
                // integer interval: closed bounds on the integers inside
                if (!double.IsInfinity(lo)) { lo = loIn ? Math.Ceiling(lo) : Math.Floor(lo) + 1; loIn = true; }
                if (!double.IsInfinity(hi)) { hi = hiIn ? Math.Floor(hi) : Math.Ceiling(hi) - 1; hiIn = true; }
                if (lo > hi) return null;
            }
            // x op c for all x in [lo, hi]: decide on the extreme values
            bool AllLe(double v, bool strict) => hi < v || hi == v && (!strict || !hiIn);            // every x <= v (or < v)
            bool NoneLe(double v, bool strict) => lo > v || lo == v && (strict || !loIn);            // no x <= v (or < v)
            switch (op)
            {
                case "<=": if (AllLe(c, false)) return true; if (NoneLe(c, false)) return false; return null;
                case "<": if (AllLe(c, true)) return true; if (NoneLe(c, true)) return false; return null;
                case ">": { var le = Implies(iv, "<=", c); return le is bool b ? !b : null; }
                case ">=": { var lt = Implies(iv, "<", c); return lt is bool b ? !b : null; }
                case "==":
                    if (lo == hi && loIn && hiIn) return lo == c;
                    if (c < lo || c == lo && !loIn || c > hi || c == hi && !hiIn) return false;
                    return null;
                case "~=": { var e = Implies(iv, "==", c); return e is bool b ? !b : null; }
            }
            return null;
        }

        void Learn(SExpr c, bool v)
        {
            if (c is SNot n) { Learn(n.E, !v); return; }
            if (c is SBin { IsBool: true } cmp)
            {
                string op = v ? cmp.Op : Negate(cmp.Op);
                if (cmp.R is SConst rc && Range(cmp.L) is { } iv) { facts[Key(cmp.L)] = Narrow(iv, op, rc.X); return; }
                if (cmp.L is SConst lc && Range(cmp.R) is { } iv2) { facts[Key(cmp.R)] = Narrow(iv2, Flip(op), lc.X); return; }
            }
            boolFacts[Key(c)] = v;
        }

        static Interval Narrow(Interval old, string op, double c)
        {
            var n = old.Clone();
            switch (op)
            {
                case "<=": if (c < n.Hi) { n.Hi = c; n.HiIn = true; } break;            // c == Hi: unchanged
                case "<": if (c < n.Hi) { n.Hi = c; n.HiIn = false; } else if (c == n.Hi) n.HiIn = false; break;
                case ">=": if (c > n.Lo) { n.Lo = c; n.LoIn = true; } break;
                case ">": if (c > n.Lo) { n.Lo = c; n.LoIn = false; } else if (c == n.Lo) n.LoIn = false; break;
                case "==": n.Lo = n.Hi = c; n.LoIn = n.HiIn = true; break;
            }
            return n;
        }

        // ---------- top level (file load): declarations, constants, registrations
        public void ExecTop(Stmt s, Env env)
        {
            Loading = true;
            switch (s)
            {
                case CallStmt { Call: Call { Fn: Name fn } call } when fn.Id.StartsWith("REGISTER_"):
                    if (fn.Id == "REGISTER_GOAL" && call.Args.Count >= 2 && Eval(call.Args[0], env) is VNum id && Eval(call.Args[1], env) is VStr nm)
                        lib.goalNames[id.X] = nm.S;
                    return;
                default:
                    Exec([s], env);
                    return;
            }
        }

        // ---------- statements
        sealed class ReturnSignal(V value) : Exception { public V Value = value; }
        sealed class BreakSignal : Exception { }

        public V CallFunc(VFunc f, List<V> args)
        {
            if (++depth > 200) throw new InvalidDataException("call depth");
            var env = new Env(f.Closure);
            for (int i = 0; i < f.Body.Params.Count; i++) env.Vars[f.Body.Params[i]] = i < args.Count ? args[i] : VNil.I;
            try { Exec(f.Body.Block, env); return VNil.I; }
            catch (ReturnSignal r) { return r.Value; }
            finally { depth--; }
        }

        void Exec(List<Stmt> block, Env env)
        {
            foreach (var s in block)
            {
                switch (s)
                {
                    case Local l:
                    {
                        var vals = l.Values.Select(v => Eval(v, env)).ToList();
                        for (int i = 0; i < l.Names.Count; i++) env.Vars[l.Names[i]] = i < vals.Count ? vals[i] : VNil.I;
                        break;
                    }
                    case Assign a:
                    {
                        var vals = a.Values.Select(v => Eval(v, env)).ToList();
                        for (int i = 0; i < a.Targets.Count; i++) Store(a.Targets[i], i < vals.Count ? vals[i] : VNil.I, env);
                        break;
                    }
                    case CallStmt c: Eval(c.Call, env); break;
                    case FuncDecl fd:
                    {
                        var f = new VFunc(fd.Body, env);
                        if (fd.IsLocal && fd.Target is Name ln) env.Vars[ln.Id] = f; else Store(fd.Target, f, env);
                        break;
                    }
                    case If i:
                    {
                        bool done = false;
                        foreach (var (cond, b) in i.Branches)
                            if (Truthy(Eval(cond, env))) { Exec(b, new Env(env)); done = true; break; }
                        if (!done && i.Else != null) Exec(i.Else, new Env(env));
                        break;
                    }
                    case NumFor nf:
                    {
                        double a0 = Num(Eval(nf.Start, env), "for start"), b0 = Num(Eval(nf.Stop, env), "for stop"), st = nf.Step != null ? Num(Eval(nf.Step, env), "for step") : 1;
                        try
                        {
                            for (double x = a0; st > 0 ? x <= b0 : x >= b0; x += st)
                            {
                                var e2 = new Env(env); e2.Vars[nf.Var] = new VNum(x);
                                Exec(nf.Block, e2);
                            }
                        }
                        catch (BreakSignal) { }
                        break;
                    }
                    case Do d: Exec(d.Block, new Env(env)); break;
                    case Return r: throw new ReturnSignal(r.Values.Count > 0 ? Eval(r.Values[0], env) : VNil.I);
                    case Break: throw new BreakSignal();
                    case While w:
                    {
                        int n = 0;
                        try { while (Truthy(Eval(w.Cond, env))) { if (++n > 1000) throw new InvalidDataException("while loop bound"); Exec(w.Block, new Env(env)); } }
                        catch (BreakSignal) { }
                        break;
                    }
                    default: throw new InvalidDataException($"line {s.Line}: {s.GetType().Name} not supported");
                }
            }
        }

        void Store(Expr target, V v, Env env)
        {
            switch (target)
            {
                case Name n:
                    (env.Find(n.Id) is { } e && e != lib.globals ? e : Loading ? lib.globals : runGlobals).Vars[n.Id] = v;
                    break;
                case LuaSyntax.Index ix:
                    if (Eval(ix.Obj, env) is not VTable t) throw new InvalidDataException("index assignment on a non-table");
                    t.Items[KeyOf(Eval(ix.Key, env))] = v;
                    break;
                default: throw new InvalidDataException("bad assignment target");
            }
        }

        static object KeyOf(V k) => k switch { VNum n => n.X, VStr s => s.S, VBool b => b.B, _ => throw new InvalidDataException("symbolic table key") };

        V Lookup(string n, Env env)
        {
            var e = env.Find(n);
            if (e != null && e != lib.globals) return e.Vars[n];
            if (runGlobals.Vars.TryGetValue(n, out var g)) return g;
            return lib.globals.Vars.TryGetValue(n, out var v) ? v : VNil.I;
        }

        // ---------- expressions
        bool Truthy(V v)
        {
            if (v is VNil) return false;
            if (v is VBool b) return b.B;
            if (v is VSym s && s.E.IsBool) return Decide(s.E);
            return true; // numbers, strings, tables, functions (and numeric symbols) are true in Lua
        }

        static double Num(V v, string what) => v is VNum n ? n.X : throw new InvalidDataException($"{what}: not a constant number ({v})");
        static SExpr S(V v) => v switch { VNum n => new SConst(n.X), VSym s => s.E, VBool b => new SConst(b.B ? 1 : 0), _ => throw new InvalidDataException($"not a number: {v}") };

        V Eval(Expr e, Env env)
        {
            switch (e)
            {
                case LuaSyntax.Nil: return VNil.I;
                case True: return new VBool(true);
                case False: return new VBool(false);
                case Num n: return new VNum(n.Value);
                case Str s: return new VStr(s.Value);
                case Name n: return Lookup(n.Id, env);
                case Function f: return new VFunc(f.Body, env);
                case LuaSyntax.Table t:
                {
                    var tab = new VTable();
                    int k = 1;
                    foreach (var (key, val) in t.Items) tab.Items[key == null ? (double)k++ : KeyOf(Eval(key, env))] = Eval(val, env);
                    return tab;
                }
                case LuaSyntax.Index ix:
                {
                    var o = Eval(ix.Obj, env);
                    if (o is VTable tab) return tab.Items.TryGetValue(KeyOf(Eval(ix.Key, env)), out var v) ? v : VNil.I;
                    // session 11 (burgchr11): Lua's standard library (math.min over the Hellkite's event points) is not modelled: nil, reported
                    if (o is VNil && ix.Obj is LuaSyntax.Name nm) { lib.Unsupported.Add($"{nm.Id}.* (Lua library)"); return VNil.I; }
                    throw new InvalidDataException($"index of {o}");
                }
                case Unary u:
                {
                    var v = Eval(u.E, env);
                    if (u.Op == "not")
                    {
                        if (v is VSym s && s.E.IsBool) return new VBool(!Decide(s.E));
                        return new VBool(!Truthy(v));
                    }
                    if (u.Op == "-") return v is VNum n ? new VNum(-n.X) : new VSym(new SBin("-", new SConst(0), S(v)));
                    throw new InvalidDataException($"unary {u.Op}");
                }
                case Binary b:
                {
                    if (b.Op == "and") { var l = Eval(b.L, env); return !Truthy(l) ? l is VSym ? new VBool(false) : l : Eval(b.R, env); }
                    if (b.Op == "or") { var l = Eval(b.L, env); return Truthy(l) ? l is VSym ? new VBool(true) : l : Eval(b.R, env); }
                    var lv = Eval(b.L, env); var rv = Eval(b.R, env);
                    // session 11 (dchr11): a boolean query compared with true / false (the Gaping Dragon's "ai:IsFinishTimer(1) == true"): the
                    // query is decided (it was taken as never equal: Equals(symbol, true) is false)
                    if (lib.QueryRules && b.Op is "==" or "~=" && (lv is VSym { E.IsBool: true } && rv is VBool || rv is VSym { E.IsBool: true } && lv is VBool))
                    {
                        var (sym, bv) = lv is VSym ls ? (ls.E, ((VBool)rv).B) : (((VSym)rv).E, ((VBool)lv).B);
                        bool eq = Decide(sym) == bv;
                        return new VBool(b.Op == "==" ? eq : !eq);
                    }
                    if (b.Op is "==" or "~=" && (lv is not (VNum or VSym) || rv is not (VNum or VSym)))
                    {
                        bool eq = Equals(lv, rv) || lv is VNil && rv is VNil;
                        return new VBool(b.Op == "==" ? eq : !eq);
                    }
                    if (lv is VNum ln && rv is VNum rn)
                        return b.Op switch
                        {
                            "+" => new VNum(ln.X + rn.X), "-" => new VNum(ln.X - rn.X), "*" => new VNum(ln.X * rn.X), "/" => new VNum(ln.X / rn.X),
                            "%" => new VNum(ln.X - Math.Floor(ln.X / rn.X) * rn.X), "^" => new VNum(Math.Pow(ln.X, rn.X)),
                            "<" => new VBool(ln.X < rn.X), ">" => new VBool(ln.X > rn.X), "<=" => new VBool(ln.X <= rn.X), ">=" => new VBool(ln.X >= rn.X),
                            "==" => new VBool(ln.X == rn.X), "~=" => new VBool(ln.X != rn.X), _ => throw new InvalidDataException($"op {b.Op}"),
                        };
                    if (b.Op == "..") return new VStr($"{lv}{rv}");
                    if (lv is VNil || rv is VNil) throw new InvalidDataException($"arithmetic/comparison on nil ({b.Op})");
                    return new VSym(new SBin(b.Op, S(lv), S(rv)));
                }
                case Call c: return CallExpr(c, env);
                case Method m: return MethodCall(m, env);
                default: throw new InvalidDataException($"expression {e.GetType().Name}");
            }
        }

        V CallExpr(Call c, Env env)
        {
            var fn = Eval(c.Fn, env);
            string fname = c.Fn is Name nn ? nn.Id : "?";
            var args = c.Args.Select(a => Eval(a, env)).ToList();
            switch (fname)
            {
                case "GET_PARAM_IF_NIL_DEF": return args.Count > 0 && args[0] is not VNil ? args[0] : args.Count > 1 ? args[1] : VNil.I;
                case "REGIST_FUNC": return args.Count > 2 ? args[2] : VNil.I;
                case "Common_Battle_Activate": return BattleActivate(args);
            }
            if (fn is VFunc f) return CallFunc(f, args);
            if (fname.StartsWith("REGISTER_") || fname is "ENABLE_COMBO_ATK_CANCEL") return VNil.I;
            lib.Unsupported.Add($"call of {fname}");
            return VNil.I;
        }

        /// <summary>common_battle_func.lua Common_Battle_Activate as a rule (see the class summary).</summary>
        V BattleActivate(List<V> a)
        {
            if (a.Count < 5 || a[2] is not VTable per) throw new InvalidDataException("Common_Battle_Activate arguments");
            var weights = Enumerable.Range(1, 20).Select(i => per.Items.TryGetValue((double)i, out var v) ? v : new VNum(0)).ToList();
            if (weights.Any(w => w is not VNum)) throw new InvalidDataException("act weights must be constants on every path");
            double total = weights.Sum(w => ((VNum)w).X);
            string? after = a[4] is VFunc af ? af.Body.Name : null;
            if (total <= 0) { Stream.Add(new ECallAct(0, "", after)); if (Mode == "activate") throw new StopRun(); return VNil.I; }
            var fate = Roll(1, total, false);
            double cum = 0;
            for (int i = 0; i < 20; i++)
            {
                cum += ((VNum)weights[i]).X;
                if (Truthy(new VSym(new SBin("<=", fate, new SConst(cum)))))
                {
                    var fn = a[3] is VTable funcs && funcs.Items.TryGetValue((double)(i + 1), out var fv) && fv is VFunc f ? f.Body.Name : "";
                    // session 11 (ret11), rule AI-D1: an act without a registered function runs DS1's default act, as
                    // Common_Battle_Activate's defFuncActArr does: defActNN(ai, goal, defFuncParamTbl[N]) (acts 13-20 → defAct12);
                    // the Stray Demon's act 2 = defAct02 {12, 0, 20, 80} (Approach + 3000 | 3000 → 3001)
                    V? extra = null;
                    if (fn == "")
                    {
                        string def = $"defAct{Math.Min(i + 1, 12):D2}";
                        if (lib.Global(def) is VFunc && a.Count >= 6 && a[5] is VTable dt)
                        {
                            fn = def;
                            extra = dt.Items.TryGetValue((double)(i + 1), out var pv) ? pv : new VTable();
                        }
                        else lib.Unsupported.Add($"act {i + 1}: no registered function and no DS1 default act {def}");
                    }
                    Stream.Add(new ECallAct(i + 1, fn, after) { Extra = extra });
                    if (Mode == "activate") throw new StopRun();
                    return VNil.I;
                }
            }
            return VNil.I;
        }

        /// <summary>The GetWellSpace part of Common_Battle_Activate after act <paramref name="ret"/> returned its odds (nil:
        /// Lua 5.0 errors on "fate &lt;= nil" and the after-act never runs - DS1 Act15/Act16 of the Asylum Demon).</summary>
        public void AfterOdds(V ret)
        {
            if (ret is not VNum odds || odds.X <= 0) return;
            var fate = Roll(1, 100, false);
            if (Truthy(new VSym(new SBin("<=", fate, new SConst(odds.X))))) Stream.Add(new ECallAfter());
        }

        SExpr Roll(double lo, double hi, bool isFloat)
        {
            var r = new SRand(rolls++, lo, hi, isFloat);
            Stream.Add(new ERoll(r.Id, lo, hi, isFloat));
            return r;
        }

        V MethodCall(Method m, Env env)
        {
            var obj = Eval(m.Obj, env);
            var args = m.Args.Select(a => Eval(a, env)).ToList();
            int I(int k) => k < args.Count && args[k] is VNum n ? (int)n.X : throw new InvalidDataException($"{m.Name}: argument {k} must be a constant");
            if (obj is VNative { Kind: "ai" })
                switch (m.Name)
                {
                    case "GetDist": return new VSym(new SDist(I(0)));
                    case "GetDistYSigned": return new VSym(new SDist(I(0), true));
                    case "GetRandam_Int": return new VSym(Roll(Num(args[0], "rand lo"), Num(args[1], "rand hi"), false));
                    case "GetRandam_Float": return new VSym(Roll(Num(args[0], "rand lo"), Num(args[1], "rand hi"), true));
                    case "GetNumber": return numbers.TryGetValue(I(0), out var nv) ? nv : new VSym(new SNumber(I(0)));
                    case "SetNumber": numbers[I(0)] = args[1]; Stream.Add(new ESetNumber(I(0), S(args[1]))); return VNil.I;
                    case "IsInsideTargetRegion": return new VSym(new SRegion(I(0), I(1)));
                    case "AddObserveChrDmySphere": Stream.Add(new EObserve(I(0), I(1), I(2), Num(args[3], "radius"))); return VNil.I;
                    case "IsInsideObserve": return new VSym(new SObserve(I(0)));
                    case "IsInsideTarget" when lib.QueryRules && args.Count >= 3 && args[2] is VNum ang && I(0) == 0 && I(1) is >= 1 and <= 4: return new VBool(InsideTarget(I(1), ang.X / 2));   // session 11 (dchr11), rule AI-IT
                    case "IsFinishTimer" when lib.QueryRules: return new VSym(new STimerDone(I(0)));                                    // rule AI-TM
                    case "SetTimer" when lib.QueryRules && args.Count >= 2 && args[1] is VNum ts: Stream.Add(new ETimer(I(0), ts.X)); return VNil.I;
                    case "IsInterupt": return new VSym(new SInterrupt(I(0)));
                    case "GetHpRate": return new VSym(new SHpRate(I(0)));
                    case "GetEventRequest" when EventRequests.ForThink(lib.ThinkParamId).Any(c => c.Slot == I(0)): return new VSym(new SEventRequest(I(0), lib.ThinkParamId));
                    case "HasSpecialEffectId": return new VSym(new SSpEffect(I(0), I(1)));
                    case "SetEventMoveTarget" when args.Count > 0 && args[0] is not VNum: lib.Unsupported.Add("ai:SetEventMoveTarget with a computed point (the Hellkite's nearest-point search)"); return VNil.I;   // session 11 (burgchr11)
                    case "SetEventMoveTarget": Stream.Add(new EEventMoveTarget(I(0))); return VNil.I;
                    case "GetNpcThinkParamID" when lib.ThinkParamId > 0: return new VNum(lib.ThinkParamId);   // session 11 (burgchr11)
                    case "DbgGetForceActIdx": return new VNum(0);
                    case "DbgSetLastActIdx": return VNil.I;
                    // session 11 (burgchr11): an untranslated query answers like the regular translator's assumptions (Ds1Goal): a predicate
                    // (Is* / Has* / Chk*) false, any other 0 (nil made the Armored Tusk's comparisons throw)
                    default: lib.Unsupported.Add($"ai:{m.Name}"); return m.Name.StartsWith("Is") || m.Name.StartsWith("Has") || m.Name.StartsWith("Chk") ? new VBool(false) : m.Name.StartsWith("Get") ? new VNum(0) : VNil.I;
                }
            if (obj is VNative { Kind: "goal", Goal: var g })
                switch (m.Name)
                {
                    case "AddSubGoal" or "AddSubGoal_Front": AddSubGoal(args); return VNil.I;
                    case "ClearSubGoal": Stream.Add(new EClear()); return VNil.I;
                    case "GetParam": return g != null && I(0) < g.Params.Count ? g.Params[I(0)] : VNil.I;
                    case "GetLife": return g?.Life ?? VNil.I;
                    case "GetBattleGoalId" when lib.BattleGoalId > 0: return new VNum(lib.BattleGoalId);   // session 11 (burgchr11)
                    default: lib.Unsupported.Add($"goal:{m.Name}"); return VNil.I;
                }
            lib.Unsupported.Add($"method {m.Name} on {obj}");
            return VNil.I;
        }

        void AddSubGoal(List<V> args)
        {
            if (args.Count < 2 || args[0] is not VNum id) { lib.Unsupported.Add("AddSubGoal with a symbolic or nil goal id"); return; }
            string name = lib.GoalName(id.X) ?? $"goal{id.X}";
            var life = args[1];
            var ps = args.Skip(2).ToList();
            // session 11 (chrai11), rule AI-IF: GOAL_COMMON_If(life, codeNo) runs the battle goal's OnIf_<battle goal id>(ai, goal, codeNo)
            // (if.lua builds that call with loadstring, which the interpreter has no form for): its sub-goals are the If goal's, run in
            // order as the act's own (the Taurus Demon's leaps 3008 / 3009 toward the event point)
            if (name == "If")
            {
                if (lib.BattleGoalId > 0 && lib.Global($"OnIf_{lib.BattleGoalId}") is VFunc onIf)
                {
                    CallFunc(onIf, [new VNative("ai"), new VNative("goal", new GoalCtx(ps, life)), ps.Count > 0 ? ps[0] : new VNum(0)]);
                    return;
                }
                lib.Unsupported.Add($"goal If: no OnIf_{lib.BattleGoalId}");
                return;
            }
            if (!NativeGoals.Contains(name) && lib.Global(name + "_Activate") is VFunc act && act.Body.Block.Count > 0)
            {
                CallFunc(act, [new VNative("ai"), new VNative("goal", new GoalCtx(ps, life))]);
                return;
            }
            if (!NativeGoals.Contains(name)) lib.Unsupported.Add($"goal {name} (no Lua, not a known native)");
            Stream.Add(new EGoal(name, life, ps));
        }
    }
}
