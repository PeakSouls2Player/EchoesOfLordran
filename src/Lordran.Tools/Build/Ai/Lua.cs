using System.Globalization;
using System.Text;

namespace Lordran.Tools.Build.Ai;

/// <summary>
/// Parser for the pseudo-Lua our decompiler prints (<c>data\research\lua\luadec50.py</c>; session 8, track enemies step 2:
/// DS1 Lua AI → DS2 AI ESD). DS1 (DSR) ships its AI as Lua 5.0 bytecode; luadec50 turns all 558 scripts into structured
/// Lua 5.0 source (if/elseif/else, numeric for, closures, table constructors; no goto occurs). This reads that source back
/// into an AST for <see cref="LuaEval"/>: statements (local, assignment, call, if, numeric/generic for, while, repeat,
/// return, break, function definitions) and expressions with Lua 5.0 precedence. Every node keeps its source line, which
/// the translator's coverage report cites.
/// </summary>
public static class Lua
{
    // ------------------------------------------------------------------ AST
    public abstract record Expr(int Line);
    public sealed record Nil(int Line) : Expr(Line);
    public sealed record Bool(int Line, bool V) : Expr(Line);
    public sealed record Num(int Line, double V) : Expr(Line);
    public sealed record Str(int Line, string V) : Expr(Line);
    public sealed record Vararg(int Line) : Expr(Line);
    public sealed record Name(int Line, string Id) : Expr(Line);
    public sealed record Index(int Line, Expr Obj, Expr Key) : Expr(Line);
    public sealed record Call(int Line, Expr Fn, List<Expr> Args) : Expr(Line);
    public sealed record Method(int Line, Expr Obj, string Name, List<Expr> Args) : Expr(Line);
    public sealed record Function(int Line, List<string> Params, bool IsVararg, List<Stat> Body, string DebugName) : Expr(Line);
    public sealed record Binary(int Line, string Op, Expr A, Expr B) : Expr(Line);
    public sealed record Unary(int Line, string Op, Expr A) : Expr(Line);
    public sealed record Table(int Line, List<(Expr? Key, Expr Value)> Fields) : Expr(Line);

    public abstract record Stat(int Line);
    public sealed record Local(int Line, List<string> Names, List<Expr> Values) : Stat(Line);
    public sealed record Assign(int Line, List<Expr> Targets, List<Expr> Values) : Stat(Line);
    public sealed record CallStat(int Line, Expr Call) : Stat(Line);
    public sealed record If(int Line, List<(Expr Cond, List<Stat> Body)> Arms, List<Stat>? Else) : Stat(Line);
    public sealed record NumFor(int Line, string Var, Expr Start, Expr Stop, Expr? Step, List<Stat> Body) : Stat(Line);
    public sealed record GenFor(int Line, List<string> Names, List<Expr> Exprs, List<Stat> Body) : Stat(Line);
    public sealed record While(int Line, Expr Cond, List<Stat> Body) : Stat(Line);
    public sealed record Repeat(int Line, List<Stat> Body, Expr Cond) : Stat(Line);
    public sealed record Return(int Line, List<Expr> Values) : Stat(Line);
    public sealed record Break(int Line) : Stat(Line);
    public sealed record Do(int Line, List<Stat> Body) : Stat(Line);
    /// <summary><c>function a.b:c(...)</c> / <c>local function f(...)</c>: Target is a Name/Index chain.</summary>
    public sealed record FunctionStat(int Line, Expr Target, Function Fn, bool IsLocal, bool IsMethod) : Stat(Line);

    public sealed record Chunk(string Source, List<Stat> Body);

    // ------------------------------------------------------------------ lexer
    enum T { Name, Number, String, Symbol, Keyword, Eof }
    readonly record struct Tok(T Kind, string Text, double Num, int Line);

    static readonly HashSet<string> Keywords = ["and", "break", "do", "else", "elseif", "end", "false", "for", "function", "if", "in",
        "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while"];
    static readonly string[] Symbols = ["...", "..", "==", "~=", "<=", ">=", "+", "-", "*", "/", "%", "^", "#", "<", ">", "=", "(", ")",
        "{", "}", "[", "]", ";", ":", ",", "."];

    static List<Tok> Lex(string src, string file)
    {
        var toks = new List<Tok>();
        int i = 0, line = 1;
        while (i < src.Length)
        {
            char c = src[i];
            if (c == '\n') { line++; i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '-' && i + 1 < src.Length && src[i + 1] == '-')
            {
                if (i + 3 < src.Length && src[i + 2] == '[' && src[i + 3] == '[')
                {
                    int end = src.IndexOf("]]", i + 4, StringComparison.Ordinal);
                    if (end < 0) end = src.Length - 2;
                    line += src.AsSpan(i, end - i).Count('\n');
                    i = end + 2; continue;
                }
                while (i < src.Length && src[i] != '\n') i++;
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int s = i;
                while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '_')) i++;
                string w = src[s..i];
                toks.Add(new(Keywords.Contains(w) ? T.Keyword : T.Name, w, 0, line));
                continue;
            }
            if (char.IsDigit(c) || (c == '.' && i + 1 < src.Length && char.IsDigit(src[i + 1])))
            {
                int s = i;
                if (c == '0' && i + 1 < src.Length && (src[i + 1] is 'x' or 'X'))
                {
                    i += 2;
                    while (i < src.Length && Uri.IsHexDigit(src[i])) i++;
                    toks.Add(new(T.Number, src[s..i], Convert.ToInt64(src[(s + 2)..i], 16), line));
                    continue;
                }
                while (i < src.Length && (char.IsDigit(src[i]) || src[i] == '.')) i++;
                if (i < src.Length && (src[i] is 'e' or 'E'))
                {
                    i++;
                    if (i < src.Length && (src[i] is '+' or '-')) i++;
                    while (i < src.Length && char.IsDigit(src[i])) i++;
                }
                toks.Add(new(T.Number, src[s..i], double.Parse(src[s..i], NumberStyles.Float, CultureInfo.InvariantCulture), line));
                continue;
            }
            if (c is '"' or '\'')
            {
                var sb = new StringBuilder();
                char q = c; i++;
                while (i < src.Length && src[i] != q)
                {
                    if (src[i] == '\\' && i + 1 < src.Length)
                    {
                        char e = src[i + 1]; i += 2;
                        switch (e)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case 'r': sb.Append('\r'); break;
                            case '\\': sb.Append('\\'); break;
                            case '"': sb.Append('"'); break;
                            case '\'': sb.Append('\''); break;
                            default:
                                if (char.IsDigit(e))
                                {
                                    int v = e - '0', k = 0;
                                    while (k < 2 && i < src.Length && char.IsDigit(src[i])) { v = v * 10 + (src[i] - '0'); i++; k++; }
                                    sb.Append((char)v);
                                }
                                else sb.Append(e);
                                break;
                        }
                        continue;
                    }
                    if (src[i] == '\n') line++;
                    sb.Append(src[i++]);
                }
                i++;
                toks.Add(new(T.String, sb.ToString(), 0, line));
                continue;
            }
            if (c == '[' && i + 1 < src.Length && src[i + 1] == '[')
            {
                int end = src.IndexOf("]]", i + 2, StringComparison.Ordinal);
                string s = src[(i + 2)..end];
                toks.Add(new(T.String, s, 0, line));
                line += s.Count(ch => ch == '\n');
                i = end + 2; continue;
            }
            string? sym = Symbols.FirstOrDefault(sy => string.CompareOrdinal(src, i, sy, 0, sy.Length) == 0);
            if (sym == null) throw new FormatException($"{file}:{line}: unexpected character '{c}'");
            toks.Add(new(T.Symbol, sym, 0, line));
            i += sym.Length;
        }
        toks.Add(new(T.Eof, "", 0, line));
        return toks;
    }

    // ------------------------------------------------------------------ parser
    public static Chunk Parse(string src, string file)
    {
        var p = new Parser(Lex(src, file), file);
        var body = p.Block();
        if (p.Peek.Kind != T.Eof) throw p.Error("expected end of file");
        return new Chunk(file, body);
    }

    public static Chunk ParseFile(string path) => Parse(File.ReadAllText(path), Path.GetFileName(path));

    sealed class Parser(List<Tok> toks, string file)
    {
        int pos;
        public Tok Peek => toks[pos];
        Tok Next() => toks[pos++];
        bool Is(string s) => Peek.Kind is T.Symbol or T.Keyword && Peek.Text == s;
        bool Accept(string s) { if (!Is(s)) return false; pos++; return true; }
        void Expect(string s) { if (!Accept(s)) throw Error($"expected '{s}'"); }
        public FormatException Error(string what) => new($"{file}:{Peek.Line}: {what}, got '{Peek.Text}'");
        string NameTok() { if (Peek.Kind != T.Name) throw Error("expected a name"); return Next().Text; }

        static bool BlockEnd(Tok t) => t.Kind == T.Eof || t.Kind == T.Keyword && t.Text is "end" or "else" or "elseif" or "until";

        public List<Stat> Block()
        {
            var list = new List<Stat>();
            while (!BlockEnd(Peek))
            {
                if (Accept(";")) continue;
                var s = Statement();
                list.Add(s);
                if (s is Return or Break) { Accept(";"); break; }
            }
            return list;
        }

        Stat Statement()
        {
            int line = Peek.Line;
            if (Accept("if"))
            {
                var arms = new List<(Expr, List<Stat>)>();
                var c = Exp(); Expect("then"); arms.Add((c, Block()));
                List<Stat>? els = null;
                while (true)
                {
                    if (Accept("elseif")) { var c2 = Exp(); Expect("then"); arms.Add((c2, Block())); continue; }
                    if (Accept("else")) { els = Block(); }
                    Expect("end");
                    break;
                }
                return new If(line, arms, els);
            }
            if (Accept("while")) { var c = Exp(); Expect("do"); var b = Block(); Expect("end"); return new While(line, c, b); }
            if (Accept("do")) { var b = Block(); Expect("end"); return new Do(line, b); }
            if (Accept("repeat")) { var b = Block(); Expect("until"); return new Repeat(line, b, Exp()); }
            if (Accept("for"))
            {
                string n1 = NameTok();
                if (Accept("="))
                {
                    var a = Exp(); Expect(","); var b = Exp(); Expr? st = null;
                    if (Accept(",")) st = Exp();
                    Expect("do"); var body = Block(); Expect("end");
                    return new NumFor(line, n1, a, b, st, body);
                }
                var names = new List<string> { n1 };
                while (Accept(",")) names.Add(NameTok());
                Expect("in");
                var exprs = ExpList();
                Expect("do"); var gb = Block(); Expect("end");
                return new GenFor(line, names, exprs, gb);
            }
            if (Accept("function"))
            {
                Expr target = new Name(line, NameTok());
                string dbg = ((Name)target).Id;
                bool method = false;
                while (Is(".") || Is(":"))
                {
                    bool colon = Next().Text == ":";
                    string k = NameTok();
                    dbg += (colon ? ":" : ".") + k;
                    target = new Index(line, target, new Str(line, k));
                    if (colon) { method = true; break; }
                }
                var fn = FuncBody(line, dbg, method);
                return new FunctionStat(line, target, fn, false, method);
            }
            if (Accept("local"))
            {
                if (Accept("function"))
                {
                    string n = NameTok();
                    return new FunctionStat(line, new Name(line, n), FuncBody(line, n, false), true, false);
                }
                var names = new List<string> { NameTok() };
                while (Accept(",")) names.Add(NameTok());
                var vals = Accept("=") ? ExpList() : [];
                return new Local(line, names, vals);
            }
            if (Accept("return"))
            {
                var vals = BlockEnd(Peek) || Is(";") ? new List<Expr>() : ExpList();
                return new Return(line, vals);
            }
            if (Accept("break")) return new Break(line);
            // assignment or call
            var e = Suffixed();
            if (Is("=") || Is(","))
            {
                var targets = new List<Expr> { e };
                while (Accept(",")) targets.Add(Suffixed());
                Expect("=");
                return new Assign(line, targets, ExpList());
            }
            if (e is Call or Method) return new CallStat(line, e);
            throw Error("expected a statement");
        }

        Function FuncBody(int line, string dbg, bool method)
        {
            Expect("(");
            var ps = new List<string>();
            if (method) ps.Add("self");
            bool va = false;
            if (!Is(")"))
                do
                {
                    if (Accept("...")) { va = true; break; }
                    ps.Add(NameTok());
                } while (Accept(","));
            Expect(")");
            var body = Block();
            Expect("end");
            return new Function(line, ps, va, body, dbg);
        }

        List<Expr> ExpList()
        {
            var l = new List<Expr> { Exp() };
            while (Accept(",")) l.Add(Exp());
            return l;
        }

        // Lua 5.0 precedence: or < and < comparison < .. (right) < + - < * / < not # - (unary) < ^ (right)
        static readonly Dictionary<string, (int L, int R)> Bin = new()
        {
            ["or"] = (1, 1), ["and"] = (2, 2),
            ["<"] = (3, 3), [">"] = (3, 3), ["<="] = (3, 3), [">="] = (3, 3), ["~="] = (3, 3), ["=="] = (3, 3),
            [".."] = (5, 4), ["+"] = (6, 6), ["-"] = (6, 6), ["*"] = (7, 7), ["/"] = (7, 7), ["%"] = (7, 7), ["^"] = (10, 9),
        };
        const int UnaryPrio = 8;

        public Expr Exp(int limit = 0)
        {
            Expr left;
            int line = Peek.Line;
            if (Is("not") || Is("-") || Is("#"))
            {
                string op = Next().Text;
                left = new Unary(line, op, Exp(UnaryPrio));
            }
            else left = Simple();
            while ((Peek.Kind is T.Symbol or T.Keyword) && Bin.TryGetValue(Peek.Text, out var pr) && pr.L > limit)
            {
                string op = Next().Text;
                var right = Exp(pr.R);
                left = new Binary(line, op, left, right);
            }
            return left;
        }

        Expr Simple()
        {
            var t = Peek; int line = t.Line;
            switch (t.Kind)
            {
                case T.Number: pos++; return new Num(line, t.Num);
                case T.String: pos++; return new Str(line, t.Text);
                case T.Keyword when t.Text == "nil": pos++; return new Nil(line);
                case T.Keyword when t.Text == "true": pos++; return new Bool(line, true);
                case T.Keyword when t.Text == "false": pos++; return new Bool(line, false);
                case T.Keyword when t.Text == "function": pos++; return FuncBody(line, $"<anonymous:{line}>", false);
                case T.Symbol when t.Text == "...": pos++; return new Vararg(line);
                case T.Symbol when t.Text == "{": return TableCons();
            }
            return Suffixed();
        }

        Expr Primary()
        {
            int line = Peek.Line;
            if (Peek.Kind == T.Name) return new Name(line, Next().Text);
            if (Accept("(")) { var e = Exp(); Expect(")"); return e; }
            throw Error("expected an expression");
        }

        Expr Suffixed()
        {
            var e = Primary();
            while (true)
            {
                int line = Peek.Line;
                if (Accept(".")) e = new Index(line, e, new Str(line, NameTok()));
                else if (Accept("[")) { var k = Exp(); Expect("]"); e = new Index(line, e, k); }
                else if (Accept(":")) { string m = NameTok(); e = new Method(line, e, m, CallArgs()); }
                else if (Is("(") || Is("{") || Peek.Kind == T.String) e = new Call(line, e, CallArgs());
                else return e;
            }
        }

        List<Expr> CallArgs()
        {
            if (Peek.Kind == T.String) { var t = Next(); return [new Str(t.Line, t.Text)]; }
            if (Is("{")) return [TableCons()];
            Expect("(");
            if (Accept(")")) return [];
            var l = ExpList();
            Expect(")");
            return l;
        }

        Expr TableCons()
        {
            int line = Peek.Line;
            Expect("{");
            var fields = new List<(Expr?, Expr)>();
            while (!Is("}"))
            {
                if (Accept("[")) { var k = Exp(); Expect("]"); Expect("="); fields.Add((k, Exp())); }
                else if (Peek.Kind == T.Name && toks[pos + 1].Kind == T.Symbol && toks[pos + 1].Text == "=")
                {
                    var k = new Str(Peek.Line, Next().Text); pos++;
                    fields.Add((k, Exp()));
                }
                else fields.Add((null, Exp()));
                if (!Accept(",") && !Accept(";")) break;
            }
            Expect("}");
            return new Table(line, fields);
        }
    }
}
