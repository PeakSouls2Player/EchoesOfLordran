using SoulsFormats;

namespace Lordran.Tools.Build.Ai;

/// <summary>
/// ESD expression bytecode for generated DS2 AI scripts (session 8, track enemies step 2). Same encoding as every vanilla
/// AI ESD (and <c>Commands/EsdUsage.Esd.Expr</c>, which prints it): small ints 0x40+v, 0x82 int32, 0x80 float32, function
/// call = push id, push args, 0x84+argc; parameter pN = push N, 0xB8; binary operators 0x8C + / 0x8E − / 0x8F × / 0x90 ÷ /
/// 0x91 &lt; / 0x92 &gt; / 0x93 ≤ / 0x94 ≥ / 0x95 == / 0x96 ≠ / 0x98 ∧ / 0x99 ∨; 0xA1 ends. Machine-call results:
/// 0xB9 (the callee's return value) and 0xBA (no value yet) — vanilla waits on <c>?B9 != ?BA</c>.
/// <para>Comparison opcodes (chrint stage 3, rulebook §7, V s9-12): DS2's evaluator table PTR_FUN_1415b2920 pops the right
/// operand first and runs 0x91 &lt;, 0x92 &gt;, 0x93 ≤, 0x94 ≥ (DSR the same). Until stage 3 this file used the community
/// table 0x91 ≤ / 0x92 ≥ / 0x93 &lt; / 0x94 &gt;, so every generated ≤ ran as &lt;, ≥ as &gt;, &lt; as ≤ and &gt; as ≥
/// (the same defect TalkExpr had). <see cref="Le"/> … <see cref="Gt"/> now mean what they say. Rule AI-OP: a comparison
/// that translates DS1 code takes DS1's operator; a comparison copied from a vanilla DS2 pattern keeps vanilla's real
/// operator (its bytes), e.g. in <see cref="Ds2AiMachines"/> the combo-stage tests <c>f5008() &gt; k</c>, the fixed waits
/// <c>f3() &gt; 0.5 / 3 / 5</c> and the turn gate <c>5:5903(f7002() &gt; 15)</c>.</para>
/// </summary>
public abstract record E
{
    public abstract void Emit(List<byte> o);

    public byte[] Bytes() { var o = new List<byte>(); Emit(o); o.Add(0xA1); return [.. o]; }
    public override string ToString() => Commands.Esd.Expr(Bytes());

    public static E I(int v) => new EInt(v);
    public static E Fl(float v) => v == MathF.Floor(v) && Math.Abs(v) < 1e6 ? new EInt((int)v) : new EFloat(v);
    public static E P(int n) => new EParam(n);
    public static E Fn(int id, params E[] args) => new ECall(id, args);
    public static readonly E True = new EInt(1);
    public static readonly E Ret = new ERaw(0xB9), NoRet = new ERaw(0xBA);

    public static E operator +(E a, E b) => new EBin(0x8C, a, b);
    public static E operator -(E a, E b) => new EBin(0x8E, a, b);
    public static E operator *(E a, E b) => new EBin(0x8F, a, b);
    public static E operator /(E a, E b) => new EBin(0x90, a, b);
    public static E operator &(E a, E b) => new EBin(0x98, a, b);
    public static E operator |(E a, E b) => new EBin(0x99, a, b);
    public const byte OpLt = 0x91, OpGt = 0x92, OpLe = 0x93, OpGe = 0x94;
    public E Lt(E b) => new EBin(OpLt, this, b);
    public E Gt(E b) => new EBin(OpGt, this, b);
    public E Le(E b) => new EBin(OpLe, this, b);
    public E Ge(E b) => new EBin(OpGe, this, b);
    public E Eq(E b) => new EBin(0x95, this, b);
    public E Ne(E b) => new EBin(0x96, this, b);
    public E Le(float b) => Le(Fl(b));
    public E Ge(float b) => Ge(Fl(b));
    public E Lt(float b) => Lt(Fl(b));
    public E Gt(float b) => Gt(Fl(b));
    public E Eq(float b) => Eq(Fl(b));
    public E Ne(float b) => Ne(Fl(b));

    public static E And(IEnumerable<E> es)
    {
        E? acc = null;
        foreach (var e in es) acc = acc == null ? e : acc & e;
        return acc ?? True;
    }
}

public sealed record EInt(int V) : E
{
    public override void Emit(List<byte> o)
    {
        if (V is >= -64 and < 64) o.Add((byte)(0x40 + V));
        else { o.Add(0x82); o.AddRange(BitConverter.GetBytes(V)); }
    }
}
public sealed record EFloat(float V) : E { public override void Emit(List<byte> o) { o.Add(0x80); o.AddRange(BitConverter.GetBytes(V)); } }
public sealed record EParam(int N) : E { public override void Emit(List<byte> o) { new EInt(N).Emit(o); o.Add(0xB8); } }
public sealed record ERaw(byte Op) : E { public override void Emit(List<byte> o) => o.Add(Op); }
public sealed record ECall(int Id, E[] Args) : E
{
    public override void Emit(List<byte> o)
    {
        new EInt(Id).Emit(o);
        foreach (var a in Args) a.Emit(o);
        o.Add((byte)(0x84 + Args.Length));
    }
}
public sealed record EBin(byte Op, E A, E B) : E { public override void Emit(List<byte> o) { A.Emit(o); B.Emit(o); o.Add(Op); } }

/// <summary>State-machine building helpers for generated AI groups.</summary>
public static class Esb
{
    /// <summary>Bank 1 command (plain call).</summary>
    public static ESD.CommandCall C(int id, params E[] args) => new(1, id, args.Select(a => a.Bytes()).ToArray());
    /// <summary>Bank 5 command: runs <paramref name="id"/>(args) only while <paramref name="cond"/> holds (vanilla <c>5:5903(cond)</c>).</summary>
    public static ESD.CommandCall CIf(E cond, int id, params E[] args) => new(5, id, new[] { cond.Bytes() }.Concat(args.Select(a => a.Bytes())).ToArray());
    /// <summary>Call machine (state group) <paramref name="group"/> with arguments (bank 6: <c>6:group(args)</c>).</summary>
    public static ESD.CommandCall Call(long group, params E[] args) => new(6, unchecked((int)group), args.Select(a => a.Bytes()).ToArray());
    /// <summary>Return from a machine with a value (pass command 7:-1(v)).</summary>
    public static ESD.CommandCall Return(int v) => new(7, -1, E.I(v).Bytes());

    public static ESD.Condition Go(long target, E cond) => new(target, cond.Bytes());
    public static ESD.Condition Go(long target) => new(target, E.True.Bytes());

    /// <summary>Wait for a machine call of this state to return, then go to <paramref name="next"/>; on return value 1 (failure)
    /// go to <paramref name="onFail"/> when given (vanilla: <c>if (?B9 == 1) -> - { if 1 -> X }</c>, <c>if (?B9 != ?BA) -> - { if 1 -> Y }</c>).</summary>
    public static IEnumerable<ESD.Condition> Returned(long next, long? onFail = null)
    {
        if (onFail is long f)
        {
            var c1 = new ESD.Condition { Evaluator = E.Ret.Eq(1).Bytes() };
            c1.Subconditions.Add(Go(f));
            yield return c1;
        }
        var c2 = new ESD.Condition { Evaluator = E.Ret.Ne(E.NoRet).Bytes() };
        c2.Subconditions.Add(Go(next));
        yield return c2;
    }

    /// <summary>A returning state: <c>if 1 -> - pass 7:-1(v)</c>.</summary>
    public static ESD.State ReturnState(int v)
    {
        var s = new ESD.State();
        var c = new ESD.Condition { Evaluator = E.True.Bytes() };
        c.PassCommands.Add(Return(v));
        s.Conditions.Add(c);
        return s;
    }

    /// <summary>State ids 0..N-1 in id order (every vanilla group is numbered so); condition targets follow, shared
    /// Condition objects are remapped once.</summary>
    public static Dictionary<long, ESD.State> Renumber(Dictionary<long, ESD.State> states)
    {
        var order = states.Keys.Order().ToList();
        var newId = order.Select((k, i) => (k, i)).ToDictionary(x => x.k, x => (long)x.i);
        var seen = new HashSet<ESD.Condition>(ReferenceEqualityComparer.Instance);
        void Remap(ESD.Condition c) { if (!seen.Add(c)) return; if (c.TargetState is long t) c.TargetState = newId[t]; foreach (var x in c.Subconditions) Remap(x); }
        var res = new Dictionary<long, ESD.State>();
        foreach (var k in order) { foreach (var c in states[k].Conditions) Remap(c); res[newId[k]] = states[k]; }
        return res;
    }

    public static ESD.State S(IEnumerable<ESD.CommandCall>? entry = null, IEnumerable<ESD.CommandCall>? whiles = null, params ESD.Condition[] conds)
    {
        var s = new ESD.State();
        if (entry != null) s.EntryCommands.AddRange(entry);
        if (whiles != null) s.WhileCommands.AddRange(whiles);
        s.Conditions.AddRange(conds);
        return s;
    }
}
