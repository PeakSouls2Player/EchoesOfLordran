using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// esd-dump FILE [GROUP...] — every state of the given state groups (all if none): entry/exit/while commands as
/// bank:id(args) and conditions with their evaluator bytecode, arguments and evaluators shown as hex (ESD expression
/// bytecode). For comparing scripts between maps, e.g. the per-bonfire group 80000.
/// </summary>
public static class EsdDump
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: esd-dump FILE [GROUP...]"); return 1; }
        var esd = ESD.Read(args[1]);
        var groups = args.Skip(2).Select(long.Parse).ToHashSet();
        foreach (var (gid, states) in esd.StateGroups.OrderBy(g => g.Key))
        {
            if (groups.Count > 0 && !groups.Contains(gid)) continue;
            Console.WriteLine($"group {gid}: {states.Count} states");
            foreach (var (sid, s) in states.OrderBy(s => s.Key))
            {
                Console.WriteLine($"  state {sid}");
                foreach (var c in s.EntryCommands) Console.WriteLine($"    entry {Cmd(c)}");
                foreach (var c in s.WhileCommands) Console.WriteLine($"    while {Cmd(c)}");
                foreach (var c in s.ExitCommands) Console.WriteLine($"    exit  {Cmd(c)}");
                foreach (var cond in s.Conditions) Cond(cond, "    ");
            }
        }
        return 0;
    }

    static string Cmd(ESD.CommandCall c) => $"{c.CommandBank}:{c.CommandID}({string.Join(", ", c.Arguments.Select(Esd.Expr))})";

    static void Cond(ESD.Condition c, string indent)
    {
        Console.WriteLine($"{indent}if {Esd.Expr(c.Evaluator)}   [{Convert.ToHexString(c.Evaluator)}] -> {(c.TargetState?.ToString() ?? "-")}");
        foreach (var p in c.PassCommands) Console.WriteLine($"{indent}  pass {Cmd(p)}");
        foreach (var sub in c.Subconditions) Cond(sub, indent + "  ");
    }
}
