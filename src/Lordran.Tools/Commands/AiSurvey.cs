using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// ai-survey DIR OUT [--only ID...] — session 8 (enemies track, step 2: DS1 Lua AI → DS2 AI ESD). DS2's AI ESDs ship
/// without EDDs, so command and function meanings come from usage. For every <c>aiNNNNNN.esd</c> in DIR this writes the
/// esd-dump text to OUT\aiNNNNNN.txt and prints, per command (bank:id, entry/while/exit/pass) and per condition function
/// (fNNN with its argument count), how many files and calls use it, plus every debug string (commands 1:90 / 1:93) with
/// the file, group and state it sits in (developer names of machines). The counts back the AI translator's primitive
/// table (<c>Build/Ai/Ds2AiPrimitives</c>).
/// </summary>
public static class AiSurvey
{
    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: ai-survey DIR OUT [--only ID...]"); return 1; }
        string dir = args[1], outDir = args[2];
        var only = args.SkipWhile(a => a != "--only").Skip(1).ToHashSet();
        Directory.CreateDirectory(outDir);
        var cmdFiles = new Dictionary<string, HashSet<string>>();
        var cmdCalls = new Dictionary<string, int>();
        var fnFiles = new Dictionary<string, HashSet<string>>();
        var fnCalls = new Dictionary<string, int>();
        var strings = new List<string>();
        int files = 0;
        foreach (var f in Directory.GetFiles(dir, "ai*.esd").OrderBy(f => f))
        {
            string name = Path.GetFileNameWithoutExtension(f);
            if (only.Count > 0 && !only.Contains(name.Substring(2))) continue;
            ESD esd;
            try { esd = ESD.Read(f); } catch (Exception e) { Console.WriteLine($"# {name}: {e.Message}"); continue; }
            files++;
            using var w = new StreamWriter(Path.Combine(outDir, name + ".txt"));
            foreach (var (gid, states) in esd.StateGroups.OrderBy(g => g.Key))
            {
                w.WriteLine($"group {gid}: {states.Count} states");
                foreach (var (sid, s) in states.OrderBy(s => s.Key))
                {
                    w.WriteLine($"  state {sid}");
                    foreach (var (kind, list) in new[] { ("entry", s.EntryCommands), ("while", s.WhileCommands), ("exit ", s.ExitCommands) })
                        foreach (var c in list) { w.WriteLine($"    {kind} {Cmd(c)}"); Count(c, kind.Trim(), name, gid, sid); }
                    foreach (var cond in s.Conditions) Cond(w, cond, "    ", name, gid, sid);
                }
            }
        }
        Console.WriteLine($"# {files} AI ESDs");
        Console.WriteLine("# commands: id kind files calls");
        foreach (var (k, v) in cmdFiles.OrderBy(k => k.Key, StringComparer.Ordinal)) Console.WriteLine($"cmd {k} {v.Count} {cmdCalls[k]}");
        Console.WriteLine("# functions: fid/argc files calls");
        foreach (var (k, v) in fnFiles.OrderBy(k => k.Key, StringComparer.Ordinal)) Console.WriteLine($"fn {k} {v.Count} {fnCalls[k]}");
        Console.WriteLine("# debug strings");
        foreach (var s in strings.Distinct()) Console.WriteLine(s);
        return 0;

        void Count(ESD.CommandCall c, string kind, string file, long gid, long sid)
        {
            string k = $"{c.CommandBank}:{c.CommandID} {kind}";
            if (!cmdFiles.TryGetValue(k, out var set)) cmdFiles[k] = set = [];
            set.Add(file);
            cmdCalls[k] = cmdCalls.GetValueOrDefault(k) + 1;
            foreach (var a in c.Arguments) Functions(a, file);
            if (c.CommandBank == 1 && c.CommandID is 90 or 93 && c.Arguments.Count > 0)
                strings.Add($"str {file} g{gid} s{sid} {c.CommandID}: {string.Join(", ", c.Arguments.Select(Esd.Expr))}");
        }
        void Cond(StreamWriter w, ESD.Condition c, string indent, string file, long gid, long sid)
        {
            w.WriteLine($"{indent}if {Esd.Expr(c.Evaluator)} -> {(c.TargetState?.ToString() ?? "-")}");
            Functions(c.Evaluator, file);
            foreach (var p in c.PassCommands) { w.WriteLine($"{indent}  pass {Cmd(p)}"); Count(p, "pass", file, gid, sid); }
            foreach (var sub in c.Subconditions) Cond(w, sub, indent + "  ", file, gid, sid);
        }
        void Functions(byte[] b, string file)
        {
            // a call is "push id, push args..., 84+argc": track the id pushed before the arguments by a small stack walk
            var st = new Stack<int?>();
            for (int i = 0; i < b.Length; i++)
            {
                byte op = b[i];
                if (op < 0x80) st.Push(op - 0x40);
                else if (op == 0x80) { st.Push(null); i += 4; }
                else if (op == 0x81) { st.Push(null); i += 8; }
                else if (op == 0x82) { st.Push(BitConverter.ToInt32(b, i + 1)); i += 4; }
                else if (op >= 0x84 && op <= 0x8A)
                {
                    int argc = op - 0x84;
                    for (int k = 0; k < argc && st.Count > 0; k++) st.Pop();
                    int? id = st.Count > 0 ? st.Pop() : null;
                    if (id is int fid)
                    {
                        string k = $"f{fid}/{argc}";
                        if (!fnFiles.TryGetValue(k, out var set)) fnFiles[k] = set = [];
                        set.Add(file);
                        fnCalls[k] = fnCalls.GetValueOrDefault(k) + 1;
                    }
                    st.Push(null);
                }
                else if (op >= 0x8C && op <= 0x9A && op != 0x8D && op != 0x97) { if (st.Count > 0) st.Pop(); if (st.Count > 0) st.Pop(); st.Push(null); }
                else if (op == 0x8D || op == 0xA6) { if (st.Count > 0) st.Pop(); st.Push(null); }
                else if (op == 0xA1) break;
                else if (op == 0xA5) { int j = i + 1; while (j + 1 < b.Length && (b[j] | b[j + 1]) != 0) j += 2; i = j + 1; st.Push(null); }
                else if (op >= 0xA7 && op <= 0xAE) { if (st.Count > 0) st.Pop(); st.Push(null); }
                else if (op == 0xB8) { if (st.Count > 0) st.Pop(); st.Push(null); }
                else st.Push(null);
            }
        }
    }

    static string Cmd(ESD.CommandCall c) => $"{c.CommandBank}:{c.CommandID}({string.Join(", ", c.Arguments.Select(Esd.Expr))})";
}
