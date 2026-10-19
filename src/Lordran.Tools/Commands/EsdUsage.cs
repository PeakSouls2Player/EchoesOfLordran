using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// esd-usage DIR|FILE CMD... [--max N] — every call of the given event commands (or condition functions, as fNNN) in
/// DS2's event ESDs, with map, group, state and decoded arguments. Session 7: how vanilla scripts drive doors, fog
/// walls, collision and boss fights. Esd.Expr decodes ESD expression bytecode to text.
/// </summary>
public static class EsdUsage
{
    public static int Run(string[] args)
    {
        var files = Directory.Exists(args[1]) ? Directory.GetFiles(args[1], "event_*.esd").OrderBy(f => f).ToArray() : [args[1]];
        int max = 12;
        var want = new HashSet<string>();
        for (int i = 2; i < args.Length; i++) { if (args[i] == "--max") max = int.Parse(args[++i]); else want.Add(args[i]); }
        var counts = new Dictionary<string, int>();
        foreach (var f in files)
        {
            ESD esd;
            try { esd = ESD.Read(f); } catch { continue; }
            string map = Path.GetFileNameWithoutExtension(f).Replace("event_", "");
            foreach (var (gid, states) in esd.StateGroups)
                foreach (var (sid, s) in states)
                {
                    foreach (var c in s.EntryCommands.Concat(s.WhileCommands).Concat(s.ExitCommands)) Show(c, map, gid, sid, "cmd");
                    foreach (var cond in Flatten(s.Conditions))
                    {
                        foreach (var p in cond.PassCommands) Show(p, map, gid, sid, "pass");
                        string e = Esd.Expr(cond.Evaluator);
                        foreach (var w in want.Where(w => w.StartsWith('f') && e.Contains(w + "(")))
                            if (Count(w) <= max) Console.WriteLine($"{map} g{gid} s{sid} if {e} -> {cond.TargetState}");
                    }
                }
        }
        foreach (var (k, v) in counts.OrderBy(k => k.Key)) Console.WriteLine($"# {k}: {v} uses");
        return 0;

        int Count(string k) { counts[k] = counts.GetValueOrDefault(k) + 1; return counts[k]; }
        void Show(ESD.CommandCall c, string map, long gid, long sid, string kind)
        {
            string k = c.CommandID.ToString();
            if (!want.Contains(k)) return;
            if (Count(k) <= max) Console.WriteLine($"{map} g{gid} s{sid} {kind} {c.CommandBank}:{c.CommandID}({string.Join(", ", c.Arguments.Select(Esd.Expr))})");
        }
    }

    static IEnumerable<ESD.Condition> Flatten(IEnumerable<ESD.Condition> cs) => cs.SelectMany(c => new[] { c }.Concat(Flatten(c.Subconditions)));
}

/// <summary>
/// ESD expression bytecode (DS2 event/talk scripts, DS1 talk scripts) → readable text. Session 8: an optional resolver
/// names condition functions (ds1-talk passes DS1 talk function names; without one they print as fNNN).
/// </summary>
public static class Esd
{
    public static string Expr(byte[] b) => Expr(b, null);

    public static string Expr(byte[] b, Func<int, string?>? function)
    {
        var st = new Stack<string>();
        string Pop() => st.Count > 0 ? st.Pop() : "?";
        for (int i = 0; i < b.Length; i++)
        {
            byte op = b[i];
            if (op < 0x80) st.Push((op - 0x40).ToString());
            else if (op == 0x80) { st.Push(BitConverter.ToSingle(b, i + 1).ToString("0.###") + "f"); i += 4; }
            else if (op == 0x81) { st.Push(BitConverter.ToDouble(b, i + 1).ToString("0.###") + "d"); i += 8; }
            else if (op == 0x82) { st.Push(BitConverter.ToInt32(b, i + 1).ToString()); i += 4; }
            else if (op >= 0x84 && op <= 0x8A)
            {
                var a = new List<string>();
                for (int k = 0; k < op - 0x84; k++) a.Insert(0, Pop());
                string id = Pop();
                st.Push($"{(function != null && int.TryParse(id, out int fid) && function(fid) is { } name ? name : "f" + id)}({string.Join(", ", a)})");
            }
            else if (op >= 0x8C && op <= 0x9A && op != 0x8D && op != 0x97)
            {
                string r = Pop(), l = Pop();
                // comparison opcodes as both runtimes evaluate them (session 9, npctalk: DS2/DSR evaluator tables, right operand
                // popped first; the old labels 0x91 <=, 0x92 >=, 0x93 <, 0x94 > were the community table and wrong)
                string o = op switch { 0x8C => "+", 0x8E => "-", 0x8F => "*", 0x90 => "/", 0x91 => "<", 0x92 => ">", 0x93 => "<=", 0x94 => ">=", 0x95 => "==", 0x96 => "!=", 0x98 => "&&", 0x99 => "||", _ => $"op{op:X2}" };
                st.Push($"({l} {o} {r})");
            }
            else if (op == 0x8D) st.Push($"-{Pop()}");
            else if (op == 0xA1) break;
            else if (op == 0xA5)
            {
                int j = i + 1; var sb = new System.Text.StringBuilder();
                while (j + 1 < b.Length && (b[j] | b[j + 1]) != 0) { sb.Append((char)BitConverter.ToUInt16(b, j)); j += 2; }
                st.Push($"\"{sb}\""); i = j + 1;
            }
            else if (op == 0xA6) st.Push($"{Pop()}!");
            else if (op >= 0xA7 && op <= 0xAE) st.Push($"r{op - 0xA7}={Pop()}");
            else if (op >= 0xAF && op <= 0xB6) st.Push($"r{op - 0xAF}");
            else if (op == 0xB7) st.Push("abort");
            else if (op == 0xB8) st.Push($"p{Pop()}");
            else st.Push($"?{op:X2}");
        }
        return string.Join(" ; ", st.Reverse());
    }
}
