using System.Globalization;
using System.Text;

namespace Lordran.Tools.Build.Ai.Boss;

/// <summary>
/// A parser for the Lua 5.0 text that our DS1 decompiler (data\research\lua\luadec50.py) writes for DSR's AI scripts
/// (session 8, track boss: the DS1 Lua AI → DS2 AI ESD translator, <see cref="Ds1AiInterpreter"/>). The decompiler's output
/// is a regular subset of Lua: function declarations (global, local, nested closures), locals, assignments, if/elseif/else,
/// numeric and generic for, while/repeat, return, method calls, table constructors; comments only as "-- upvalues: …"
/// after a function header and the file banner. The parser covers the whole language grammar for those statements, so
/// every one of the 558 DSR AI/event scripts parses (checked by <c>ai-lua-parse</c>).
/// </summary>
public static class LuaSyntax
{
    // ================================================================== AST
    public abstract record Expr;
    public sealed record Nil : Expr;
    public sealed record True : Expr;
    public sealed record False : Expr;
    public sealed record Num(double Value) : Expr;
    public sealed record Str(string Value) : Expr;
    public sealed record Vararg : Expr;
    public sealed record Name(string Id) : Expr;
    public sealed record Index(Expr Obj, Expr Key) : Expr;
    public sealed record Call(Expr Fn, List<Expr> Args) : Expr;
    public sealed record Method(Expr Obj, string Name, List<Expr> Args) : Expr;
    public sealed record Function(FuncBody Body) : Expr;
    public sealed record Binary(string Op, Expr L, Expr R) : Expr;
    public sealed record Unary(string Op, Expr E) : Expr;
    public sealed record Table(List<(Expr? Key, Expr Value)> Items) : Expr;

    public sealed record FuncBody(List<string> Params, bool IsVararg, List<Stmt> Block, string Name, int Line);

    public abstract record Stmt(int Line);
    public sealed record Local(int Line, List<string> Names, List<Expr> Values) : Stmt(Line);
    public sealed record Assign(int Line, List<Expr> Targets, List<Expr> Values) : Stmt(Line);
    public sealed record CallStmt(int Line, Expr Call) : Stmt(Line);
    public sealed record If(int Line, List<(Expr Cond, List<Stmt> Block)> Branches, List<Stmt>? Else) : Stmt(Line);
    public sealed record NumFor(int Line, string Var, Expr Start, Expr Stop, Expr? Step, List<Stmt> Block) : Stmt(Line);
    public sealed record GenFor(int Line, List<string> Vars, List<Expr> Exprs, List<Stmt> Block) : Stmt(Line);
    public sealed record While(int Line, Expr Cond, List<Stmt> Block) : Stmt(Line);
    public sealed record Repeat(int Line, List<Stmt> Block, Expr Cond) : Stmt(Line);
    public sealed record Do(int Line, List<Stmt> Block) : Stmt(Line);
    public sealed record Return(int Line, List<Expr> Values) : Stmt(Line);
    public sealed record Break(int Line) : Stmt(Line);
    /// <summary>function a.b.c(...) / function a:b(...) / local function f(...): Target is the assigned place.</summary>
    public sealed record FuncDecl(int Line, Expr Target, bool IsLocal, FuncBody Body) : Stmt(Line);

    // ================================================================== tokens
    enum T { Name, Num, Str, Op, Eof }
    readonly record struct Tok(T Kind, string Text, double Num, int Line);

    static List<Tok> Lex(string s)
    {
        var o = new List<Tok>();
        int i = 0, line = 1;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\n') { line++; i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '-' && i + 1 < s.Length && s[i + 1] == '-')
            {
                if (i + 3 < s.Length && s[i + 2] == '[' && s[i + 3] == '[')
                {
                    int e = s.IndexOf("]]", i + 4, StringComparison.Ordinal);
                    e = e < 0 ? s.Length : e + 2;
                    line += s[i..e].Count(ch => ch == '\n'); i = e; continue;
                }
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int st = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                o.Add(new Tok(T.Name, s[st..i], 0, line));
                continue;
            }
            if (char.IsDigit(c) || c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1]))
            {
                int st = i;
                if (c == '0' && i + 1 < s.Length && (s[i + 1] == 'x' || s[i + 1] == 'X'))
                {
                    i += 2;
                    while (i < s.Length && Uri.IsHexDigit(s[i])) i++;
                    o.Add(new Tok(T.Num, s[st..i], Convert.ToInt64(s[(st + 2)..i], 16), line));
                    continue;
                }
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
                {
                    i++;
                    if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                    while (i < s.Length && char.IsDigit(s[i])) i++;
                }
                o.Add(new Tok(T.Num, s[st..i], double.Parse(s[st..i], CultureInfo.InvariantCulture), line));
                continue;
            }
            if (c == '"' || c == '\'')
            {
                var sb = new StringBuilder();
                i++;
                while (i < s.Length && s[i] != c)
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        char n = s[++i];
                        if (char.IsDigit(n))
                        {
                            int st = i;
                            while (i < s.Length && i - st < 3 && char.IsDigit(s[i])) i++;
                            sb.Append((char)int.Parse(s[st..i])); continue;
                        }
                        sb.Append(n switch { 'n' => '\n', 't' => '\t', 'r' => '\r', 'a' => '\a', 'b' => '\b', 'f' => '\f', 'v' => '\v', _ => n });
                        i++; continue;
                    }
                    if (s[i] == '\n') line++;
                    sb.Append(s[i++]);
                }
                i++;
                o.Add(new Tok(T.Str, sb.ToString(), 0, line));
                continue;
            }
            if (c == '[' && i + 1 < s.Length && s[i + 1] == '[')
            {
                int e = s.IndexOf("]]", i + 2, StringComparison.Ordinal);
                if (e < 0) e = s.Length;
                o.Add(new Tok(T.Str, s[(i + 2)..e], 0, line));
                line += s[i..e].Count(ch => ch == '\n'); i = Math.Min(s.Length, e + 2); continue;
            }
            string[] ops = ["...", "..", "==", "~=", "<=", ">="];
            var op = ops.FirstOrDefault(p => string.CompareOrdinal(s, i, p, 0, p.Length) == 0);
            if (op != null) { o.Add(new Tok(T.Op, op, 0, line)); i += op.Length; continue; }
            o.Add(new Tok(T.Op, c.ToString(), 0, line)); i++;
        }
        o.Add(new Tok(T.Eof, "", 0, line));
        return o;
    }

    // ================================================================== parser
    public static List<Stmt> Parse(string source) => new Parser(Lex(source)).Chunk();

    sealed class Parser(List<Tok> t)
    {
        int p;
        Tok Peek => t[p];
        Tok Next() => t[p++];
        bool IsOp(string s) => Peek.Kind == T.Op && Peek.Text == s;
        bool IsKw(string s) => Peek.Kind == T.Name && Peek.Text == s;
        void Expect(string s)
        {
            if (Peek.Text != s) throw new FormatException($"line {Peek.Line}: expected '{s}', got '{Peek.Text}'");
            p++;
        }
        string NameTok()
        {
            if (Peek.Kind != T.Name) throw new FormatException($"line {Peek.Line}: expected a name, got '{Peek.Text}'");
            return Next().Text;
        }
        static readonly HashSet<string> BlockEnd = ["end", "else", "elseif", "until"];

        public List<Stmt> Chunk()
        {
            var b = Block();
            if (Peek.Kind != T.Eof) throw new FormatException($"line {Peek.Line}: unexpected '{Peek.Text}'");
            return b;
        }

        List<Stmt> Block()
        {
            var o = new List<Stmt>();
            while (Peek.Kind != T.Eof && !(Peek.Kind == T.Name && BlockEnd.Contains(Peek.Text)))
            {
                if (IsOp(";")) { p++; continue; }
                var s = Statement();
                o.Add(s);
                if (s is Return or Break) { if (IsOp(";")) p++; break; }
            }
            return o;
        }

        Stmt Statement()
        {
            int line = Peek.Line;
            if (IsKw("local"))
            {
                p++;
                if (IsKw("function"))
                {
                    p++;
                    string n = NameTok();
                    return new FuncDecl(line, new Name(n), true, Body(n, line));
                }
                var names = new List<string> { NameTok() };
                while (IsOp(",")) { p++; names.Add(NameTok()); }
                var vals = new List<Expr>();
                if (IsOp("=")) { p++; vals = ExprList(); }
                return new Local(line, names, vals);
            }
            if (IsKw("function"))
            {
                p++;
                Expr target = new Name(NameTok());
                string full = ((Name)target).Id;
                bool method = false;
                while (IsOp(".") || IsOp(":"))
                {
                    bool colon = Next().Text == ":";
                    string k = NameTok();
                    full += (colon ? ":" : ".") + k;
                    target = new Index(target, new Str(k));
                    if (colon) { method = true; break; }
                }
                var body = Body(full, line);
                if (method) body = body with { Params = ["self", .. body.Params] };
                return new FuncDecl(line, target, false, body);
            }
            if (IsKw("if"))
            {
                p++;
                var br = new List<(Expr, List<Stmt>)>();
                var c = Expression(); Expect("then"); br.Add((c, Block()));
                List<Stmt>? els = null;
                while (true)
                {
                    if (IsKw("elseif")) { p++; var c2 = Expression(); Expect("then"); br.Add((c2, Block())); continue; }
                    if (IsKw("else")) { p++; els = Block(); }
                    Expect("end");
                    break;
                }
                return new If(line, br, els);
            }
            if (IsKw("while")) { p++; var c = Expression(); Expect("do"); var b = Block(); Expect("end"); return new While(line, c, b); }
            if (IsKw("repeat")) { p++; var b = Block(); Expect("until"); return new Repeat(line, b, Expression()); }
            if (IsKw("do")) { p++; var b = Block(); Expect("end"); return new Do(line, b); }
            if (IsKw("return"))
            {
                p++;
                var vals = Peek.Kind == T.Eof || IsOp(";") || Peek.Kind == T.Name && BlockEnd.Contains(Peek.Text) ? [] : ExprList();
                return new Return(line, vals);
            }
            if (IsKw("break")) { p++; return new Break(line); }
            if (IsKw("for"))
            {
                p++;
                string v = NameTok();
                if (IsOp("="))
                {
                    p++;
                    var a = Expression(); Expect(","); var b = Expression();
                    Expr? st = null;
                    if (IsOp(",")) { p++; st = Expression(); }
                    Expect("do"); var blk = Block(); Expect("end");
                    return new NumFor(line, v, a, b, st, blk);
                }
                var vars = new List<string> { v };
                while (IsOp(",")) { p++; vars.Add(NameTok()); }
                Expect("in");
                var ex = ExprList(); Expect("do"); var blk2 = Block(); Expect("end");
                return new GenFor(line, vars, ex, blk2);
            }
            // expression statement: call or assignment
            var e = Suffixed();
            if (IsOp("=") || IsOp(","))
            {
                var targets = new List<Expr> { e };
                while (IsOp(",")) { p++; targets.Add(Suffixed()); }
                Expect("=");
                return new Assign(line, targets, ExprList());
            }
            if (e is not (Call or Method)) throw new FormatException($"line {line}: expression is not a statement");
            return new CallStmt(line, e);
        }

        FuncBody Body(string name, int line)
        {
            Expect("(");
            var ps = new List<string>(); bool va = false;
            if (!IsOp(")"))
                while (true)
                {
                    if (IsOp("...")) { p++; va = true; break; }
                    ps.Add(NameTok());
                    if (!IsOp(",")) break;
                    p++;
                }
            Expect(")");
            var b = Block();
            Expect("end");
            return new FuncBody(ps, va, b, name, line);
        }

        List<Expr> ExprList()
        {
            var o = new List<Expr> { Expression() };
            while (IsOp(",")) { p++; o.Add(Expression()); }
            return o;
        }

        // precedence (Lua 5.0): or < and < comparison < .. (right) < + - < * / % < unary < ^ (right)
        static readonly Dictionary<string, (int L, int R)> Bin = new()
        {
            ["or"] = (1, 1), ["and"] = (2, 2),
            ["<"] = (3, 3), [">"] = (3, 3), ["<="] = (3, 3), [">="] = (3, 3), ["~="] = (3, 3), ["=="] = (3, 3),
            [".."] = (5, 4), ["+"] = (6, 6), ["-"] = (6, 6), ["*"] = (7, 7), ["/"] = (7, 7), ["%"] = (7, 7), ["^"] = (10, 9),
        };
        const int UnaryPrec = 8;

        public Expr Expression(int limit = 0)
        {
            Expr l;
            if (IsKw("not") || IsOp("-") || IsOp("#"))
            {
                string op = Next().Text;
                l = new Unary(op, Expression(UnaryPrec));
            }
            else l = Simple();
            while (true)
            {
                string op = Peek.Text;
                if (!(Peek.Kind == T.Op || Peek.Kind == T.Name && (op == "and" || op == "or")) || !Bin.TryGetValue(op, out var pr) || pr.L <= limit) break;
                p++;
                l = new Binary(op, l, Expression(pr.R));
            }
            return l;
        }

        Expr Simple()
        {
            var k = Peek;
            switch (k.Kind)
            {
                case T.Num: p++; return new Num(k.Num);
                case T.Str: p++; return new Str(k.Text);
                case T.Name when k.Text == "nil": p++; return new Nil();
                case T.Name when k.Text == "true": p++; return new True();
                case T.Name when k.Text == "false": p++; return new False();
                case T.Name when k.Text == "function": p++; return new Function(Body("<closure>", k.Line));
                case T.Op when k.Text == "...": p++; return new Vararg();
                case T.Op when k.Text == "{": return TableCons();
            }
            return Suffixed();
        }

        Expr Primary()
        {
            if (IsOp("(")) { p++; var e = Expression(); Expect(")"); return e; }
            return new Name(NameTok());
        }

        Expr Suffixed()
        {
            var e = Primary();
            while (true)
            {
                if (IsOp(".")) { p++; e = new Index(e, new Str(NameTok())); }
                else if (IsOp("[")) { p++; var k = Expression(); Expect("]"); e = new Index(e, k); }
                else if (IsOp(":")) { p++; string m = NameTok(); e = new Method(e, m, Args()); }
                else if (IsOp("(") || IsOp("{") || Peek.Kind == T.Str) e = new Call(e, Args());
                else return e;
            }
        }

        List<Expr> Args()
        {
            if (Peek.Kind == T.Str) return [new Str(Next().Text)];
            if (IsOp("{")) return [TableCons()];
            Expect("(");
            if (IsOp(")")) { p++; return []; }
            var a = ExprList();
            Expect(")");
            return a;
        }

        Expr TableCons()
        {
            Expect("{");
            var items = new List<(Expr?, Expr)>();
            while (!IsOp("}"))
            {
                if (IsOp("[")) { p++; var k = Expression(); Expect("]"); Expect("="); items.Add((k, Expression())); }
                else if (Peek.Kind == T.Name && t[p + 1].Kind == T.Op && t[p + 1].Text == "=") { string n = Next().Text; p++; items.Add((new Str(n), Expression())); }
                else items.Add((null, Expression()));
                if (IsOp(",") || IsOp(";")) p++; else break;
            }
            Expect("}");
            return new Table(items);
        }
    }
}
