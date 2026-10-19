using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>edd-machines FILE.edd [MACHINE...] — the debug descriptions an EDD keeps per state machine: parameter
/// names, state names and the source text of each state's commands and conditions (developer-written).</summary>
public static class EddMachines
{
    public static int Run(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var edd = EDD.Read(args[1]);
        var want = args.Skip(2).Select(long.Parse).ToHashSet();
        foreach (var m in edd.Machines)
        {
            if (want.Count > 0 && !want.Contains((uint)m.ID) && !want.Contains(m.ID)) continue;
            Console.WriteLine($"machine {(uint)m.ID} '{m.Name}' params [{string.Join(", ", m.ParamNames.Where(p => !string.IsNullOrEmpty(p)))}]");
            foreach (var s in m.States)
            {
                Console.WriteLine($"  state {s.ID} '{s.Name}'");
                foreach (var c in s.EntryCommands) Console.WriteLine($"    entry {c.Name}");
                foreach (var c in s.ExitCommands) Console.WriteLine($"    exit  {c.Name}");
                foreach (var c in s.WhileCommands) Console.WriteLine($"    while {c.Name}");
            }
        }
        return 0;
    }
}
