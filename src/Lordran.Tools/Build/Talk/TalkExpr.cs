namespace Lordran.Tools.Build.Talk;

/// <summary>
/// ESD expression bytecode as a tree, for rewriting DS1 talk conditions into DS2 ones (session 8, track npc). DS1 (DSR)
/// talk ESDs and DS2 event/talk ESDs share one bytecode (Commands.Esd.Expr decodes both): a stack machine where
/// 0x00-0x7F push (op - 0x40), 0x80 float, 0x81 double, 0x82 int32, 0x84+n call the function whose id was pushed before
/// its n arguments, 0x8C-0x99 binary operators (+ - * / &lt; &gt; &lt;= &gt;= == != &amp;&amp; ||), 0x8D negate, 0xA1 end.
/// Comparison opcodes (session 9, track npctalk, verified in both runtimes' evaluator tables, DS2 PTR_FUN_1415b2920 and
/// DSR PTR_FUN_141a8c620: right operand popped first, then the left one, compared with comisd + seta/setae): 0x91 &lt;,
/// 0x92 &gt;, 0x93 &lt;=, 0x94 &gt;=. Our tools had 0x91 &lt;=, 0x92 &gt;=, 0x93 &lt;, 0x94 &gt; (the community table), so the
/// translator's own "f130311(1) &gt; 0" was written as 0x94 = "&gt;= 0": always true (Oscar's 14-per-frame prompt cycle,
/// the Firelink hit lines at rest, run s9-2). Copied DS1 comparisons kept their meaning (same opcodes in both games).
/// DS1 talk conditions use numbers, calls, comparisons, &amp;&amp;/||, registers and short-circuits (see Parse; all 179 scripts parse:
/// `talk-translate --all`);
/// anything else (parameters 0xB8, call results 0xB9/0xBA, strings) is kept as an opaque leaf so a rewrite can still report it.
/// Writing follows DS2's own encoding (vanilla talk_m10_04: f6(flag) = 46 82 &lt;flag&gt; 85; small ints one byte).
/// </summary>
public abstract record TalkExpr
{
    public sealed record Num(int V) : TalkExpr;
    public sealed record Real(double V, bool IsDouble) : TalkExpr;
    public sealed record Call(int Fn, TalkExpr[] Args) : TalkExpr;
    public sealed record Bin(byte Op, TalkExpr L, TalkExpr R) : TalkExpr;
    public sealed record Neg(TalkExpr X) : TalkExpr;
    /// <summary>Bytes the tree doesn't model (kept verbatim; a rewrite reports them).</summary>
    public sealed record Raw(byte[] Bytes) : TalkExpr;

    public const byte Add = 0x8C, Sub = 0x8E, Mul = 0x8F, Div = 0x90, Lt = 0x91, Gt = 0x92, Le = 0x93, Ge = 0x94, Eq = 0x95, Ne = 0x96, And = 0x98, Or = 0x99;

    public static TalkExpr N(int v) => new Num(v);
    public static TalkExpr F(int fn, params TalkExpr[] args) => new Call(fn, args);
    public static TalkExpr B(byte op, TalkExpr l, TalkExpr r) => new Bin(op, l, r);
    public static TalkExpr AndAll(IEnumerable<TalkExpr> xs) => xs.Aggregate((a, b) => B(And, a, b));
    public static TalkExpr OrAll(IEnumerable<TalkExpr> xs) => xs.Aggregate((a, b) => B(Or, a, b));
    public static bool IsComparison(byte op) => op is >= Lt and <= Ne;

    /// <summary>
    /// Parses one expression (up to its 0xA1). Throws on malformed bytecode. DS1's compiler shares subexpressions between
    /// the conditions of a state through registers (95 of 179 talks, e.g. the Petrus and bonfire talks: condition 1
    /// stores "r0 = IsActionPromptShown() == 1", condition 2 reads r0) and short-circuits with 0xA6 / 0xB7 (Esd.Expr's
    /// "!" and "abort": "if the top value is false, the whole expression is false"; the value stays on the stack, so
    /// "(r0 == 1) abort X &amp;&amp;" is (r0 == 1) &amp;&amp; X). Here both are identity (the tree keeps no evaluation order), a
    /// store 0xA7+n records the stored subexpression in <paramref name="registers"/> (shared by the conditions of one
    /// state, in evaluation order) and leaves it on the stack, a load 0xAF+n inlines it: the result has no registers
    /// (DS2's own scripts don't need them; recomputing a talk function in the same frame gives the same value).
    /// </summary>
    public static TalkExpr Parse(byte[] b, Dictionary<int, TalkExpr>? registers = null)
    {
        var st = new Stack<TalkExpr>();
        TalkExpr Pop() => st.Count > 0 ? st.Pop() : throw new InvalidDataException("ESD expression stack underflow");
        int i = 0;
        for (; i < b.Length; i++)
        {
            byte op = b[i];
            if (op < 0x80) st.Push(new Num(op - 0x40));
            else if (op == 0x80) { st.Push(new Real(BitConverter.ToSingle(b, i + 1), false)); i += 4; }
            else if (op == 0x81) { st.Push(new Real(BitConverter.ToDouble(b, i + 1), true)); i += 8; }
            else if (op == 0x82) { st.Push(new Num(BitConverter.ToInt32(b, i + 1))); i += 4; }
            else if (op is >= 0x84 and <= 0x8A)
            {
                var args = new TalkExpr[op - 0x84];
                for (int k = args.Length - 1; k >= 0; k--) args[k] = Pop();
                if (Pop() is not Num id) throw new InvalidDataException("ESD call without a constant function id");
                st.Push(new Call(id.V, args));
            }
            else if (op is >= 0x8C and <= 0x99 && op is not (0x8D or 0x97))
            {
                var r = Pop(); var l = Pop();
                st.Push(new Bin(op, l, r));
            }
            else if (op == 0x8D) st.Push(new Neg(Pop()));
            else if (op == 0xA1) break;
            else if (op is 0xA6 or 0xB7) { if (st.Count == 0) throw new InvalidDataException("short-circuit on an empty stack"); }
            else if (op is >= 0xA7 and <= 0xAE)
            {
                if (st.Count == 0) throw new InvalidDataException("register store on an empty stack");
                if (registers == null) throw new InvalidDataException($"register r{op - 0xA7} stored without a register table");
                registers[op - 0xA7] = st.Peek();
            }
            else if (op is >= 0xAF and <= 0xB6)
            {
                if (registers == null || !registers.TryGetValue(op - 0xAF, out var v)) throw new InvalidDataException($"register r{op - 0xAF} read before it is set");
                st.Push(v);
            }
            else
            {
                // opaque: the rest of the expression as one leaf (a rewrite of it is reported, not guessed)
                var rest = b[i..];
                int end = Array.LastIndexOf(rest, (byte)0xA1);
                st.Push(new Raw(end > 0 ? rest[..end] : rest));
                i = b.Length;
                break;
            }
        }
        if (st.Count != 1) throw new InvalidDataException($"ESD expression leaves {st.Count} values");
        return st.Pop();
    }

    /// <summary>Bytecode with the closing 0xA1.</summary>
    public byte[] Write()
    {
        var o = new List<byte>();
        Emit(this, o);
        o.Add(0xA1);
        return o.ToArray();
    }

    static void Emit(TalkExpr e, List<byte> o)
    {
        switch (e)
        {
            case Num n when n.V is >= -64 and <= 63: o.Add((byte)(n.V + 0x40)); break;
            case Num n: o.Add(0x82); o.AddRange(BitConverter.GetBytes(n.V)); break;
            case Real r when r.IsDouble: o.Add(0x81); o.AddRange(BitConverter.GetBytes(r.V)); break;
            case Real r: o.Add(0x80); o.AddRange(BitConverter.GetBytes((float)r.V)); break;
            case Call c:
                Emit(new Num(c.Fn), o);
                foreach (var a in c.Args) Emit(a, o);
                o.Add((byte)(0x84 + c.Args.Length));
                break;
            case Bin bi: Emit(bi.L, o); Emit(bi.R, o); o.Add(bi.Op); break;
            case Neg ng: Emit(ng.X, o); o.Add(0x8D); break;
            case Raw raw: o.AddRange(raw.Bytes); break;
        }
    }

    /// <summary>Bottom-up rewrite: <paramref name="f"/> sees each node after its children were rewritten.</summary>
    public TalkExpr Map(Func<TalkExpr, TalkExpr> f) => f(this switch
    {
        Call c => new Call(c.Fn, c.Args.Select(a => a.Map(f)).ToArray()),
        Bin b => new Bin(b.Op, b.L.Map(f), b.R.Map(f)),
        Neg n => new Neg(n.X.Map(f)),
        _ => this,
    });

    public IEnumerable<TalkExpr> Nodes() => new[] { this }.Concat(this switch
    {
        Call c => c.Args.SelectMany(a => a.Nodes()),
        Bin b => b.L.Nodes().Concat(b.R.Nodes()),
        Neg n => n.X.Nodes(),
        _ => [],
    });

    /// <summary>
    /// Constant folding after the rules replaced unknown functions by constants: comparisons and arithmetic of two
    /// numbers, &amp;&amp;/|| with a constant side. DS1 talk conditions use &amp;&amp;/|| only as truth values (never compared
    /// as numbers: --all survey), so "x &amp;&amp; 1" folds to x and "x || 0" to x.
    /// </summary>
    public TalkExpr Fold() => Map(e =>
    {
        if (e is Neg { X: Num nn }) return new Num(-nn.V);
        if (e is not Bin b) return e;
        if (b.L is Num l && b.R is Num r)
            return new Num(b.Op switch
            {
                Add => l.V + r.V, Sub => l.V - r.V, Mul => l.V * r.V, Div => r.V != 0 ? l.V / r.V : 0,
                Le => l.V <= r.V ? 1 : 0, Ge => l.V >= r.V ? 1 : 0, Lt => l.V < r.V ? 1 : 0, Gt => l.V > r.V ? 1 : 0,
                Eq => l.V == r.V ? 1 : 0, Ne => l.V != r.V ? 1 : 0,
                And => l.V != 0 && r.V != 0 ? 1 : 0, Or => l.V != 0 || r.V != 0 ? 1 : 0, _ => 0,
            });
        if (b.Op == And)
        {
            if (b.L is Num { V: 0 } || b.R is Num { V: 0 }) return new Num(0);
            if (b.L is Num) return b.R;
            if (b.R is Num) return b.L;
        }
        if (b.Op == Or)
        {
            if (b.L is Num lo && lo.V != 0 || b.R is Num ro && ro.V != 0) return new Num(1);
            if (b.L is Num) return b.R;
            if (b.R is Num) return b.L;
        }
        return e;
    });

    /// <summary>Readable text (DS2 function ids as fNNN), for reports.</summary>
    public sealed override string ToString() => Text(null);

    public string Text(Func<int, string?>? name) => this switch
    {
        Num n => n.V.ToString(),
        Real r => r.V.ToString("0.###") + (r.IsDouble ? "d" : "f"),
        Call c => $"{name?.Invoke(c.Fn) ?? "f" + c.Fn}({string.Join(", ", c.Args.Select(a => a.Text(name)))})",
        Bin b => $"({b.L.Text(name)} {OpText(b.Op)} {b.R.Text(name)})",
        Neg n => $"-{n.X.Text(name)}",
        Raw raw => $"[raw {Convert.ToHexString(raw.Bytes)}]",
        _ => "?",
    };

    public static string OpText(byte op) => op switch
    {
        Add => "+", Sub => "-", Mul => "*", Div => "/", Le => "<=", Ge => ">=", Lt => "<", Gt => ">", Eq => "==", Ne => "!=", And => "&&", Or => "||", _ => $"op{op:X2}",
    };
}
