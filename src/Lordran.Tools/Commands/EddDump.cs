using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>edd-dump FILE.edd [filter] — the command and function names an ESD's debug file declares (ids → names).</summary>
public static class EddDump
{
    public static int Run(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var edd = EDD.Read(args[1]);
        string? filter = args.Length > 2 ? args[2] : null;
        bool Match(string n) => filter == null || n.Contains(filter, StringComparison.OrdinalIgnoreCase);
        foreach (var c in edd.CommandSpecs.Where(c => Match(c.Name)))
            Console.WriteLine($"command {c.ID} (bank {c.ID >> 32}:{c.ID & 0xFFFFFFFF}): {c.Name}");
        foreach (var f in edd.FunctionSpecs.Where(f => Match(f.Name)))
            Console.WriteLine($"function {f.ID}: {f.Name}");
        Console.WriteLine($"{edd.CommandSpecs.Count} commands, {edd.FunctionSpecs.Count} functions, {edd.Machines.Count} machines");
        return 0;
    }
}
