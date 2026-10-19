using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// boss-ai-survey [DIR] [--dump OUTDIR] [--use N...] — DS2's AI ESDs (ezstate/ai*.esd, 485 files; they ship without EDDs, so
/// the meaning of a command or function comes from how vanilla uses it): every command (bank:id) and condition function
/// (fN) with its use count, the number of files using it and example arguments / expressions. --dump writes each file as
/// text (esd-dump format) for grepping; --use N lists every use of command or function N (session 8, track boss: the
/// evidence behind the DS1 Lua → DS2 AI ESD rules in Build/Ai).
/// </summary>
public static class BossAiSurvey
{
    public static int Run(string[] args)
    {
        string dir = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : Path.Combine(ExtractDs2.DefaultOut, "ezstate");
        string? dump = args.SkipWhile(a => a != "--dump").Skip(1).FirstOrDefault();
        var use = args.SkipWhile(a => a != "--use").Skip(1).TakeWhile(a => !a.StartsWith("--")).Select(int.Parse).ToHashSet();
        if (dump != null) Directory.CreateDirectory(dump);
        var cmdUses = new Dictionary<string, (int N, HashSet<string> Files, List<string> Ex)>();
        var fnUses = new Dictionary<int, (int N, HashSet<string> Files, List<string> Ex)>();
        var files = Directory.GetFiles(dir, "ai*.esd").OrderBy(f => f, StringComparer.Ordinal).ToList();
        foreach (var f in files)
        {
            ESD esd;
            try { esd = ESD.Read(f); } catch (Exception e) { Console.WriteLine($"# {Path.GetFileName(f)}: {e.Message}"); continue; }
            string name = Path.GetFileNameWithoutExtension(f);
            var text = dump != null ? new System.Text.StringBuilder() : null;
            foreach (var (gid, states) in esd.StateGroups.OrderBy(g => g.Key))
            {
                text?.AppendLine($"group {gid}: {states.Count} states");
                foreach (var (sid, s) in states.OrderBy(s => s.Key))
                {
                    text?.AppendLine($"  state {sid}");
                    foreach (var (kind, list) in new[] { ("entry", s.EntryCommands), ("while", s.WhileCommands), ("exit ", s.ExitCommands) })
                        foreach (var c in list)
                        {
                            string call = Cmd(c);
                            text?.AppendLine($"    {kind} {call}");
                            Count(cmdUses, $"{c.CommandBank}:{c.CommandID}", name, $"{name} g{gid} s{sid} {kind.Trim()} {call}");
                            if (use.Contains(c.CommandID)) Console.WriteLine($"{name} g{gid} s{sid} {kind.Trim()} {call}");
                            foreach (var a in c.Arguments) Functions(a, name, gid, sid, $"arg of {call}");
                        }
                    foreach (var cond in s.Conditions) Cond(cond, "    ", name, gid, sid, text);
                }
            }
            if (dump != null) File.WriteAllText(Path.Combine(dump, name + ".txt"), text!.ToString());
        }
        if (use.Count > 0) return 0;
        Console.WriteLine($"# {files.Count} AI ESDs in {dir}");
        Console.WriteLine("## commands (bank:id  uses  files  examples)");
        foreach (var (k, v) in cmdUses.OrderBy(k => int.Parse(k.Key.Split(':')[1])).ThenBy(k => k.Key))
            Console.WriteLine($"{k,-10} {v.N,6} {v.Files.Count,4}  {string.Join(" | ", v.Ex)}");
        Console.WriteLine("## functions (id  uses  files  examples)");
        foreach (var (k, v) in fnUses.OrderBy(k => k.Key))
            Console.WriteLine($"f{k,-9} {v.N,6} {v.Files.Count,4}  {string.Join(" | ", v.Ex)}");
        return 0;

        void Cond(ESD.Condition c, string indent, string name, long gid, long sid, System.Text.StringBuilder? text)
        {
            string e = Esd.Expr(c.Evaluator);
            text?.AppendLine($"{indent}if {e}   [{Convert.ToHexString(c.Evaluator)}] -> {(c.TargetState?.ToString() ?? "-")}");
            Functions(c.Evaluator, name, gid, sid, $"if {e}");
            foreach (var p in c.PassCommands)
            {
                string call = Cmd(p);
                text?.AppendLine($"{indent}  pass {call}");
                Count(cmdUses, $"{p.CommandBank}:{p.CommandID}", name, $"{name} g{gid} s{sid} pass {call}");
                if (use.Contains(p.CommandID)) Console.WriteLine($"{name} g{gid} s{sid} pass {call}");
            }
            foreach (var sub in c.Subconditions) Cond(sub, indent + "  ", name, gid, sid, text);
        }

        void Functions(byte[] b, string name, long gid, long sid, string ctx)
        {
            // function ids: a pushed int immediately followed (after its arguments) by a call op; scan the stack machine
            var stack = new Stack<int?>();
            for (int i = 0; i < b.Length; i++)
            {
                byte op = b[i];
                if (op < 0x80) stack.Push(op - 0x40);
                else if (op == 0x80) { stack.Push(null); i += 4; }
                else if (op == 0x81) { stack.Push(null); i += 8; }
                else if (op == 0x82) { stack.Push(BitConverter.ToInt32(b, i + 1)); i += 4; }
                else if (op >= 0x84 && op <= 0x8A)
                {
                    for (int k = 0; k < op - 0x84 && stack.Count > 0; k++) stack.Pop();
                    int? id = stack.Count > 0 ? stack.Pop() : null;
                    if (id is int fid)
                    {
                        Count(fnUses, fid, name, $"{name} g{gid} s{sid} {ctx}");
                        if (use.Contains(fid)) Console.WriteLine($"{name} g{gid} s{sid} {ctx}");
                    }
                    stack.Push(null);
                }
                else if (op == 0xA5) { int j = i + 1; while (j + 1 < b.Length && (b[j] | b[j + 1]) != 0) j += 2; i = j + 1; stack.Push(null); }
                else if (op == 0xA1) break;
                else if (op >= 0x8C && op <= 0x9A && op != 0x8D && op != 0x97) { if (stack.Count > 0) stack.Pop(); if (stack.Count > 0) stack.Pop(); stack.Push(null); }
                else if (op is 0x8D or 0x9A or 0xA6 || op >= 0xA7 && op <= 0xAE) { /* unary / store: stack depth unchanged */ }
                else stack.Push(null); // register reads, params, B7/B9/BA
            }
        }
    }

    static void Count<TK>(Dictionary<TK, (int N, HashSet<string> Files, List<string> Ex)> d, TK key, string file, string example) where TK : notnull
    {
        if (!d.TryGetValue(key, out var v)) d[key] = v = (0, [], []);
        v.Files.Add(file);
        if (v.Ex.Count < 3 && !v.Ex.Any(x => x.StartsWith(file + " "))) v.Ex.Add(example.Length > 140 ? example[..140] + "…" : example);
        d[key] = (v.N + 1, v.Files, v.Ex);
    }

    static string Cmd(ESD.CommandCall c) => $"{c.CommandBank}:{c.CommandID}({string.Join(", ", c.Arguments.Select(Esd.Expr))})";
}
