using System.Globalization;

namespace Lordran.Tools.Build.Ai;

/// <summary>
/// A symbolic Lua 5.0 interpreter for DS1's AI scripts (session 8, track enemies step 2). It runs DS1's own code — the battle
/// goal and the aiCommon library it calls (common_battle_func / common_func2 / the common goals' Lua) — with the runtime
/// queries left symbolic: <c>ai:GetDist</c>, <c>ai:GetRandam_Int</c>, <c>ai:IsInterupt</c> … return <see cref="Sym"/> values.
/// Where DS1's code branches on one, the run forks (paths are enumerated by replay: a run follows a recorded prefix of
/// decisions, takes "true" at the first new branch and queues the "false" alternative). Each path ends with the sub-goals
/// its <c>goal:AddSubGoal</c> calls queued, in order, plus immediate effects (SetNumber / SetTimer …). Infeasible branches
/// are pruned with interval reasoning on each atomic quantity (a random roll, the target distance), so DS1's own quirks
/// survive exactly: <c>if fate &lt;= 20 then … if fate &lt;= 50</c> takes only the true arm, an <c>elseif fate &lt;= 80</c>
/// after <c>fate &lt;= 85</c> is dead code, the copy-pasted defAct13-20 → defAct12, the undefined <c>True</c> is nil.
/// <para>Lua 5.0 semantics kept: numeric for loops drive the visible loop variable (assigning it ends the loop, as
/// Common_Battle_Activate relies on), globals default to nil, functions are first-class closures over their scope.</para>
/// </summary>
public sealed class LuaEval
{
    // ------------------------------------------------------------------ values
    public sealed class LTable
    {
        public readonly Dictionary<object, object?> Map = [];
        public object? Get(object? k) => k == null ? null : Map.GetValueOrDefault(Norm(k));
        public void Set(object? k, object? v) { if (k == null) return; if (v == null) Map.Remove(Norm(k)); else Map[Norm(k)] = v; }
        static object Norm(object k) => k is int i ? (double)i : k;
        public int Length { get { int n = 0; while (Map.ContainsKey((double)(n + 1))) n++; return n; } }
    }
    public sealed class Closure(Lua.Function fn, Scope env) { public Lua.Function Fn = fn; public Scope Env = env; public override string ToString() => $"function {Fn.DebugName}"; }
    public delegate List<object?> BuiltinFn(LuaEval vm, List<object?> args, int line);
    public sealed class Builtin(string name, BuiltinFn fn) { public string Name = name; public BuiltinFn Fn = fn; public override string ToString() => $"builtin {Name}"; }
    /// <summary>A userdata object with methods (DS1's <c>ai</c> and <c>goal</c>).</summary>
    public sealed class Obj(string kind, Func<LuaEval, string, List<object?>, int, List<object?>> call) { public string Kind = kind; public Func<LuaEval, string, List<object?>, int, List<object?>> Call = call; public override string ToString() => Kind; }

    // ------------------------------------------------------------------ symbolic values
    public abstract class Sym { public abstract string Key { get; } public override string ToString() => Key; }
    /// <summary>Numeric symbolic value.</summary>
    public abstract class SymNum : Sym { }
    /// <summary>An engine query (GetDist(0), GetDistYSigned(0), GetHpRate(-1), GetNumber(i) …).</summary>
    public sealed class Query(string name, params double[] args) : SymNum
    {
        public string Name = name; public double[] Args = args;
        public override string Key => $"{Name}({string.Join(",", Args.Select(a => a.ToString(CultureInfo.InvariantCulture)))})";
    }
    /// <summary>A random roll (GetRandam_Int / GetRandam_Float): one per call, numbered in the order of the path.</summary>
    public sealed class Rand(int id, double lo, double hi, bool isFloat, int line) : SymNum
    {
        public int Id = id; public double Lo = lo, Hi = hi; public bool IsFloat = isFloat; public int Line = line;
        public override string Key => $"rand#{Id}";
    }
    public sealed class Arith(string op, object a, object b) : SymNum
    {
        public string Op = op; public object A = a, B = b;
        public override string Key => $"({K(A)} {Op} {K(B)})";
    }
    public sealed class Abs(object a) : SymNum { public object A = a; public override string Key => $"abs({K(A)})"; }
    /// <summary>Boolean symbolic value.</summary>
    public abstract class SymBool : Sym { }
    public sealed class Cmp(string op, object a, object b) : SymBool
    {
        public string Op = op; public object A = a, B = b;
        public override string Key => $"({K(A)} {Op} {K(B)})";
    }
    /// <summary>An engine predicate (IsInterupt(2), IsInsideTarget(0,1,90) …).</summary>
    public sealed class Pred(string name, params double[] args) : SymBool
    {
        public string Name = name; public double[] Args = args;
        public override string Key => $"{Name}({string.Join(",", Args.Select(a => a.ToString(CultureInfo.InvariantCulture)))})";
    }
    public sealed class Not(SymBool a) : SymBool { public SymBool A = a; public override string Key => $"not {A.Key}"; }

    static string K(object o) => o switch { Sym s => s.Key, double d => d.ToString("0.###", CultureInfo.InvariantCulture), _ => Convert.ToString(o, CultureInfo.InvariantCulture) ?? "nil" };

    /// <summary>One branch decision on a path: atom (Cmp or Pred) and the truth value the path took.</summary>
    public sealed record Literal(SymBool Atom, bool Value, int Line)
    {
        public override string ToString() => Value ? Atom.Key : $"not {Atom.Key}";
    }

    // ------------------------------------------------------------------ scopes
    public sealed class Scope(Scope? parent)
    {
        public readonly Scope? Parent = parent;
        public readonly Dictionary<string, object?> Vars = [];
        public bool TryFind(string name, out Scope s)
        {
            for (var c = this; c != null; c = c.Parent) if (c.Vars.ContainsKey(name)) { s = c; return true; }
            s = null!; return false;
        }
    }

    // ------------------------------------------------------------------ machine state
    public readonly LTable Globals = new();
    readonly List<bool> prefix;
    int branchIndex;
    public readonly List<Literal> Path = [];
    public readonly List<bool> Taken = [];
    public readonly List<List<bool>> Alternatives = [];
    readonly Dictionary<string, bool> known = [];
    readonly Dictionary<string, Interval> intervals = [];
    public readonly List<Rand> Rands = [];
    public int Steps;
    public const int MaxSteps = 2_000_000;
    /// <summary>Notes about constructs the interpreter met (unsupported API, assumptions), with source lines.</summary>
    public readonly List<string> Notes = [];

    public LuaEval(List<bool>? prefix = null) => this.prefix = prefix ?? [];

    sealed class Interval { public double Lo = double.NegativeInfinity, Hi = double.PositiveInfinity; public bool LoOpen, HiOpen; public bool Int; }

    public Rand NewRand(double lo, double hi, bool isFloat, int line)
    {
        var r = new Rand(Rands.Count, lo, hi, isFloat, line);
        Rands.Add(r);
        intervals[r.Key] = new Interval { Lo = lo, Hi = hi, Int = !isFloat };
        return r;
    }

    // ------------------------------------------------------------------ branching
    /// <summary>Lua truthiness, forking on a symbolic boolean.</summary>
    public bool Truthy(object? v, int line)
    {
        switch (v)
        {
            case null: return false;
            case bool b: return b;
            case SymBool sb: return Decide(sb, line);
            default: return true; // numbers (also symbolic ones), strings, tables, functions
        }
    }

    bool Decide(SymBool c, int line)
    {
        bool positive = true;
        while (c is Not n) { c = n.A; positive = !positive; }
        if (known.TryGetValue(c.Key, out bool kv)) return kv == positive;
        if (c is Cmp cmp && Entailed(cmp) is bool e) { known[c.Key] = e; return e == positive; }
        int k = branchIndex++;
        bool outcome;
        if (k < prefix.Count) outcome = prefix[k];
        else
        {
            outcome = true;
            Alternatives.Add([.. Taken, false]);
        }
        Taken.Add(outcome);
        Path.Add(new Literal(c, outcome, line));
        known[c.Key] = outcome;
        if (c is Cmp cc) Constrain(cc, outcome);
        return outcome == positive;
    }

    /// <summary>Atomic comparison "X op c" (X a Query/Rand/Abs, c a number), normalised so X is on the left.</summary>
    static (Sym X, string Op, double C)? Atomic(Cmp c)
    {
        static string Flip(string op) => op switch { "<" => ">", "<=" => ">=", ">" => "<", ">=" => "<=", _ => op };
        if (c.A is Sym xa && xa is not Arith && c.B is double cb) return (xa, c.Op, cb);
        if (c.B is Sym xb && xb is not Arith && c.A is double ca) return (xb, Flip(c.Op), ca);
        return null;
    }

    bool? Entailed(Cmp c)
    {
        if (Atomic(c) is not var (x, op, v) || !intervals.TryGetValue(x.Key, out var iv)) return null;
        // truth of "x op v" for all x in iv
        bool AllTrue() => op switch
        {
            "<" => iv.Hi < v || (iv.Hi == v && iv.HiOpen) || (iv.Int && iv.Hi <= v - 1),
            "<=" => iv.Hi <= v || (iv.Int && iv.Hi < v + 1 && Math.Floor(v) >= iv.Hi),
            ">" => iv.Lo > v || (iv.Lo == v && iv.LoOpen) || (iv.Int && iv.Lo >= v + 1),
            ">=" => iv.Lo >= v,
            "==" => iv.Lo == v && iv.Hi == v && !iv.LoOpen && !iv.HiOpen,
            "~=" => v < iv.Lo || v > iv.Hi || (iv.Int && v != Math.Floor(v)),
            _ => false,
        };
        bool AllFalse() => op switch
        {
            "<" => iv.Lo >= v,
            "<=" => iv.Lo > v || (iv.Lo == v && iv.LoOpen) || (iv.Int && iv.Lo >= Math.Floor(v) + 1),
            ">" => iv.Hi <= v,
            ">=" => iv.Hi < v || (iv.Hi == v && iv.HiOpen) || (iv.Int && iv.Hi <= Math.Ceiling(v) - 1),
            "==" => v < iv.Lo || v > iv.Hi || (iv.Int && v != Math.Floor(v)),
            "~=" => iv.Lo == v && iv.Hi == v,
            _ => false,
        };
        if (AllTrue()) return true;
        if (AllFalse()) return false;
        return null;
    }

    void Constrain(Cmp c, bool value)
    {
        if (Atomic(c) is not var (x, op, v)) return;
        if (!intervals.TryGetValue(x.Key, out var iv)) intervals[x.Key] = iv = new Interval { Int = x is Rand r && !r.IsFloat };
        if (!value) op = op switch { "<" => ">=", "<=" => ">", ">" => "<=", ">=" => "<", "==" => "~=", "~=" => "==", _ => op };
        void Hi(double h, bool open) { if (h < iv.Hi || (h == iv.Hi && open)) { iv.Hi = h; iv.HiOpen = open; } }
        void Lo(double l, bool open) { if (l > iv.Lo || (l == iv.Lo && open)) { iv.Lo = l; iv.LoOpen = open; } }
        switch (op)
        {
            case "<": if (iv.Int) Hi(Math.Ceiling(v) - 1, false); else Hi(v, true); break;
            case "<=": if (iv.Int) Hi(Math.Floor(v), false); else Hi(v, false); break;
            case ">": if (iv.Int) Lo(Math.Floor(v) + 1, false); else Lo(v, true); break;
            case ">=": if (iv.Int) Lo(Math.Ceiling(v), false); else Lo(v, false); break;
            case "==": Lo(v, false); Hi(v, false); break;
            // an integer ≠ one end of its range moves that end (ShootNum ~= 1 ∧ ShootNum < 2 is infeasible)
            case "~=" when iv.Int && v == iv.Lo && !iv.LoOpen: iv.Lo = v + 1; break;
            case "~=" when iv.Int && v == iv.Hi && !iv.HiOpen: iv.Hi = v - 1; break;
        }
    }

    // ------------------------------------------------------------------ running chunks and functions
    sealed class ReturnSignal(List<object?> values) : Exception { public List<object?> Values = values; }
    sealed class BreakSignal : Exception { }
    public sealed class LuaError(string msg) : Exception(msg) { }

    public void RunChunk(Lua.Chunk chunk)
    {
        var scope = new Scope(null);
        try { ExecBlock(chunk.Body, scope); }
        catch (ReturnSignal) { }
    }

    public List<object?> Call(object? fn, List<object?> args, int line)
    {
        if (++Steps > MaxSteps) throw new LuaError("step limit");
        switch (fn)
        {
            case Builtin b: return b.Fn(this, args, line);
            case Closure c:
            {
                var scope = new Scope(c.Env);
                for (int i = 0; i < c.Fn.Params.Count; i++) scope.Vars[c.Fn.Params[i]] = i < args.Count ? args[i] : null;
                if (c.Fn.IsVararg) { var t = new LTable(); for (int i = c.Fn.Params.Count; i < args.Count; i++) t.Set((double)(i - c.Fn.Params.Count + 1), args[i]); scope.Vars["arg"] = t; }
                try { ExecBlock(c.Fn.Body, scope); }
                catch (ReturnSignal r) { return r.Values; }
                return [];
            }
            default: throw new LuaError($"line {line}: attempt to call a {TypeName(fn)} value");
        }
    }

    public static string TypeName(object? v) => v switch { null => "nil", bool => "boolean", double => "number", string => "string", LTable => "table", Closure or Builtin => "function", Obj => "userdata", SymBool => "symbolic boolean", SymNum => "symbolic number", _ => v.GetType().Name };

    void ExecBlock(List<Lua.Stat> body, Scope scope)
    {
        foreach (var s in body) Exec(s, scope);
    }

    void Exec(Lua.Stat s, Scope scope)
    {
        if (++Steps > MaxSteps) throw new LuaError("step limit");
        switch (s)
        {
            case Lua.Local l:
            {
                var vals = EvalList(l.Values, scope, l.Names.Count);
                for (int i = 0; i < l.Names.Count; i++) scope.Vars[l.Names[i]] = vals[i];
                break;
            }
            case Lua.Assign a:
            {
                var vals = EvalList(a.Values, scope, a.Targets.Count);
                for (int i = 0; i < a.Targets.Count; i++) AssignTo(a.Targets[i], vals[i], scope);
                break;
            }
            case Lua.CallStat c: Eval(c.Call, scope, multi: true); break;
            case Lua.If f:
            {
                foreach (var (cond, b) in f.Arms)
                    if (Truthy(Eval(cond, scope), cond.Line)) { ExecBlock(b, new Scope(scope)); return; }
                if (f.Else != null) ExecBlock(f.Else, new Scope(scope));
                break;
            }
            case Lua.NumFor nf:
            {
                double start = Number(Eval(nf.Start, scope), nf.Line), stop = Number(Eval(nf.Stop, scope), nf.Line);
                double step = nf.Step != null ? Number(Eval(nf.Step, scope), nf.Line) : 1;
                var loop = new Scope(scope);
                loop.Vars[nf.Var] = start;
                try
                {
                    // Lua 5.0 FORLOOP: the visible variable is the control variable (the body may change it)
                    while (true)
                    {
                        double i = Number(loop.Vars[nf.Var], nf.Line);
                        if (step > 0 ? i > stop : i < stop) break;
                        ExecBlock(nf.Body, new Scope(loop));
                        loop.Vars[nf.Var] = Number(loop.Vars[nf.Var], nf.Line) + step;
                    }
                }
                catch (BreakSignal) { }
                break;
            }
            case Lua.GenFor gf:
            {
                var src = EvalList(gf.Exprs, scope, 3);
                var f = src[0]; var st = src[1]; object? ctl = src[2];
                try
                {
                    while (true)
                    {
                        var r = Call(f, [st, ctl], gf.Line);
                        object? first = r.Count > 0 ? r[0] : null;
                        if (first == null) break;
                        ctl = first;
                        var body = new Scope(scope);
                        for (int i = 0; i < gf.Names.Count; i++) body.Vars[gf.Names[i]] = i < r.Count ? r[i] : null;
                        ExecBlock(gf.Body, body);
                    }
                }
                catch (BreakSignal) { }
                break;
            }
            case Lua.While w:
                try { while (Truthy(Eval(w.Cond, scope), w.Line)) ExecBlock(w.Body, new Scope(scope)); }
                catch (BreakSignal) { }
                break;
            case Lua.Repeat rp:
                try
                {
                    while (true)
                    {
                        var body = new Scope(scope);
                        ExecBlock(rp.Body, body);
                        if (Truthy(Eval(rp.Cond, body), rp.Line)) break;
                    }
                }
                catch (BreakSignal) { }
                break;
            case Lua.Return r: throw new ReturnSignal(EvalList(r.Values, scope, -1));
            case Lua.Break: throw new BreakSignal();
            case Lua.Do d: ExecBlock(d.Body, new Scope(scope)); break;
            case Lua.FunctionStat fs:
            {
                var clo = new Closure(fs.Fn, scope);
                if (fs.IsLocal) { scope.Vars[((Lua.Name)fs.Target).Id] = clo; break; }
                AssignTo(fs.Target, clo, scope);
                break;
            }
            default: throw new LuaError($"line {s.Line}: unsupported statement {s.GetType().Name}");
        }
    }

    void AssignTo(Lua.Expr target, object? v, Scope scope)
    {
        switch (target)
        {
            case Lua.Name n:
                if (scope.TryFind(n.Id, out var sc)) sc.Vars[n.Id] = v; else Globals.Set(n.Id, v);
                break;
            case Lua.Index ix:
            {
                var obj = Eval(ix.Obj, scope);
                if (obj is not LTable t) throw new LuaError($"line {ix.Line}: attempt to index a {TypeName(obj)} value");
                t.Set(Eval(ix.Key, scope), v);
                break;
            }
            default: throw new LuaError($"line {target.Line}: cannot assign to {target.GetType().Name}");
        }
    }

    List<object?> EvalList(List<Lua.Expr> exprs, Scope scope, int want)
    {
        var vals = new List<object?>();
        for (int i = 0; i < exprs.Count; i++)
        {
            bool last = i == exprs.Count - 1;
            if (last && exprs[i] is Lua.Call or Lua.Method or Lua.Vararg) vals.AddRange(EvalMulti(exprs[i], scope));
            else vals.Add(Eval(exprs[i], scope));
        }
        if (want >= 0) { while (vals.Count < want) vals.Add(null); if (vals.Count > want) vals.RemoveRange(want, vals.Count - want); }
        return vals;
    }

    List<object?> EvalMulti(Lua.Expr e, Scope scope)
    {
        switch (e)
        {
            case Lua.Call c:
            {
                var fn = Eval(c.Fn, scope);
                return Call(fn, EvalList(c.Args, scope, -1), c.Line);
            }
            case Lua.Method m:
            {
                var obj = Eval(m.Obj, scope);
                var args = EvalList(m.Args, scope, -1);
                if (obj is Obj o) return o.Call(this, m.Name, args, m.Line);
                if (obj is LTable t) return Call(t.Get(m.Name), [obj, .. args], m.Line);
                throw new LuaError($"line {m.Line}: method {m.Name} on a {TypeName(obj)} value");
            }
            case Lua.Vararg v:
            {
                if (!scope.TryFind("arg", out var sc) || sc.Vars["arg"] is not LTable t) return [];
                return [.. Enumerable.Range(1, t.Length).Select(i => t.Get((double)i))];
            }
        }
        return [Eval(e, scope)];
    }

    public object? Eval(Lua.Expr e, Scope scope, bool multi = false)
    {
        switch (e)
        {
            case Lua.Nil: return null;
            case Lua.Bool b: return b.V;
            case Lua.Num n: return n.V;
            case Lua.Str s: return s.V;
            case Lua.Name n: return scope.TryFind(n.Id, out var sc) ? sc.Vars[n.Id] : Globals.Get(n.Id);
            case Lua.Index ix:
            {
                var obj = Eval(ix.Obj, scope);
                var key = Eval(ix.Key, scope);
                if (obj is LTable t) return t.Get(key);
                if (obj is Sym) { Notes.Add($"line {ix.Line}: index of a symbolic value ({obj}) = nil"); return null; }
                throw new LuaError($"line {ix.Line}: attempt to index a {TypeName(obj)} value");
            }
            case Lua.Call or Lua.Method or Lua.Vararg:
            {
                var r = EvalMulti(e, scope);
                return r.Count > 0 ? r[0] : null;
            }
            case Lua.Function f: return new Closure(f, scope);
            case Lua.Table tc:
            {
                var t = new LTable();
                int n = 1;
                for (int i = 0; i < tc.Fields.Count; i++)
                {
                    var (k, ve) = tc.Fields[i];
                    if (k != null) { t.Set(Eval(k, scope), Eval(ve, scope)); continue; }
                    if (i == tc.Fields.Count - 1 && ve is Lua.Call or Lua.Method or Lua.Vararg)
                        foreach (var x in EvalMulti(ve, scope)) t.Set((double)n++, x);
                    else t.Set((double)n++, Eval(ve, scope));
                }
                return t;
            }
            case Lua.Unary u:
            {
                var a = Eval(u.A, scope);
                switch (u.Op)
                {
                    case "not":
                        if (a is SymBool sb) return new Not(sb);
                        return !(a is not null && !(a is bool bb && !bb));
                    case "-":
                        if (a is double d) return -d;
                        if (a is SymNum) return new Arith("-", 0.0, a);
                        throw new LuaError($"line {u.Line}: arithmetic on a {TypeName(a)} value");
                    case "#":
                        if (a is LTable t) return (double)t.Length;
                        if (a is string str) return (double)str.Length;
                        throw new LuaError($"line {u.Line}: length of a {TypeName(a)} value");
                }
                break;
            }
            case Lua.Binary bin:
            {
                if (bin.Op == "and")
                {
                    var a = Eval(bin.A, scope);
                    if (!Truthy(a, bin.Line)) return a is SymBool ? false : a;
                    return Eval(bin.B, scope);
                }
                if (bin.Op == "or")
                {
                    var a = Eval(bin.A, scope);
                    if (Truthy(a, bin.Line)) return a is SymBool ? true : a;
                    return Eval(bin.B, scope);
                }
                return BinOp(bin.Op, Eval(bin.A, scope), Eval(bin.B, scope), bin.Line);
            }
        }
        throw new LuaError($"line {e.Line}: unsupported expression {e.GetType().Name}");
    }

    public object? BinOp(string op, object? a, object? b, int line)
    {
        switch (op)
        {
            case "+" or "-" or "*" or "/" or "%" or "^":
                if (a is double x && b is double y)
                    return op switch { "+" => x + y, "-" => x - y, "*" => x * y, "/" => x / y, "%" => x - Math.Floor(x / y) * y, _ => Math.Pow(x, y) };
                if ((a is double or SymNum) && (b is double or SymNum)) return new Arith(op, a!, b!);
                if (a is string sstr && double.TryParse(sstr, NumberStyles.Float, CultureInfo.InvariantCulture, out double pa)) return BinOp(op, pa, b, line);
                throw new LuaError($"line {line}: arithmetic on {TypeName(a)} and {TypeName(b)}");
            case "..":
                return $"{Str(a)}{Str(b)}";
            case "==" or "~=":
            {
                bool eq;
                if (a is SymBool sa && b is bool bb) { var r = bb ? sa : new Not(sa); return op == "==" ? r : new Not(r); }
                if (b is SymBool sb && a is bool ab) { var r = ab ? sb : new Not(sb); return op == "==" ? r : new Not(r); }
                if (a is Sym || b is Sym)
                {
                    if (a is SymNum || b is SymNum) { if ((a is double or SymNum) && (b is double or SymNum)) return new Cmp(op, a!, b!); return op != "=="; }
                    return new Cmp(op, a!, b!);
                }
                eq = a == null ? b == null : a.Equals(b) || (a is double da && b is double db && da == db);
                return op == "==" ? eq : !eq;
            }
            case "<" or "<=" or ">" or ">=":
                if (a is double x2 && b is double y2) return op switch { "<" => x2 < y2, "<=" => x2 <= y2, ">" => x2 > y2, _ => x2 >= y2 };
                if (a is string s1 && b is string s2) { int c = string.CompareOrdinal(s1, s2); return op switch { "<" => c < 0, "<=" => c <= 0, ">" => c > 0, _ => c >= 0 }; }
                if ((a is double or SymNum) && (b is double or SymNum)) return new Cmp(op, a!, b!);
                throw new LuaError($"line {line}: attempt to compare {TypeName(a)} with {TypeName(b)}");
        }
        throw new LuaError($"line {line}: unsupported operator {op}");
    }

    static string Str(object? v) => v switch { double d => d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString(CultureInfo.InvariantCulture), null => "nil", _ => v.ToString() ?? "" };

    public static double Number(object? v, int line) => v switch
    {
        double d => d,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double p) => p,
        _ => throw new LuaError($"line {line}: number expected, got {TypeName(v)}"),
    };

    // ------------------------------------------------------------------ standard library subset
    public void InstallStdlib()
    {
        void G(string name, BuiltinFn f) => Globals.Set(name, new Builtin(name, f));
        G("print", (vm, a, l) => []);
        G("type", (vm, a, l) => [TypeName(a.Count > 0 ? a[0] : null)]);
        G("tostring", (vm, a, l) => [Str(a.Count > 0 ? a[0] : null)]);
        G("tonumber", (vm, a, l) => [a.Count > 0 && a[0] is double d ? d : a.Count > 0 && a[0] is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double p) ? p : null]);
        G("pairs", (vm, a, l) =>
        {
            var t = a[0] as LTable ?? throw new LuaError($"line {l}: pairs of a non-table");
            var keys = t.Map.Keys.ToList();
            int idx = 0;
            return [new Builtin("next", (vm2, a2, l2) => { while (idx < keys.Count) { var k = keys[idx++]; if (t.Map.TryGetValue(k, out var v)) return [k, v]; } return [null]; }), t, null];
        });
        G("ipairs", (vm, a, l) =>
        {
            var t = a[0] as LTable ?? throw new LuaError($"line {l}: ipairs of a non-table");
            return [new Builtin("inext", (vm2, a2, l2) => { double i = Number(a2[1] ?? 0.0, l2) + 1; var v = t.Get(i); return v == null ? [null] : [i, v]; }), t, 0.0];
        });
        var math = new LTable();
        math.Set("abs", new Builtin("math.abs", (vm, a, l) => [a[0] is double d ? Math.Abs(d) : a[0] is SymNum s ? new Abs(s) : throw new LuaError($"line {l}: math.abs of {TypeName(a[0])}")]));
        math.Set("min", new Builtin("math.min", (vm, a, l) => [MinMax(vm, a, l, true)]));
        math.Set("max", new Builtin("math.max", (vm, a, l) => [MinMax(vm, a, l, false)]));
        math.Set("floor", new Builtin("math.floor", (vm, a, l) => [Math.Floor(Number(a[0], l))]));
        math.Set("ceil", new Builtin("math.ceil", (vm, a, l) => [Math.Ceiling(Number(a[0], l))]));
        Globals.Set("math", math);
        var table = new LTable();
        table.Set("getn", new Builtin("table.getn", (vm, a, l) => [(double)((a[0] as LTable)?.Length ?? 0)]));
        table.Set("insert", new Builtin("table.insert", (vm, a, l) => { var t = (LTable)a[0]!; t.Set((double)(t.Length + 1), a[^1]); return []; }));
        Globals.Set("table", table);
    }

    /// <summary>math.min / math.max: concrete, or the first argument when symbolic values are involved (Lua compares them with
    /// &lt;, so this forks like DS1 would branch).</summary>
    static object? MinMax(LuaEval vm, List<object?> a, int l, bool min)
    {
        object? best = a[0];
        for (int i = 1; i < a.Count; i++)
        {
            var c = vm.BinOp("<", a[i], best, l);
            if (vm.Truthy(min ? c : vm.BinOp("<", best, a[i], l), l)) best = a[i];
        }
        return best;
    }
}
