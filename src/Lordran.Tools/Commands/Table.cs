using Lordran.Tools.Formats;

namespace Lordran.Tools.Commands;

/// <summary>
/// table dsr|ds2 ParamName field+field+... [name-filter]
/// Prints the chosen fields for every row (optionally only rows whose community name contains the filter).
/// Fields are '+'-separated so the argument survives cmd.exe.
/// </summary>
public static class Table
{
    public static int Run(string[] args)
    {
        if (args.Length < 4) { Console.Error.WriteLine("usage: table dsr|ds2 ParamName field+field+... [name-filter]"); return 1; }
        var store = args[1] == "dsr"
            ? ParamStore.LoadDsr(Program.DefaultDsrRoot)
            : ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        string paramName = args[2];
        string[] fields = args[3].Split('+');
        string? filter = args.Length > 4 ? args[4] : null;

        Console.WriteLine($"{"id",10}  {string.Join("  ", fields.Select(f => f.PadLeft(Math.Min(f.Length, 14))))}  name");
        foreach (var row in store.Params[paramName].Rows)
        {
            string name = store.RowName(paramName, row.ID);
            if (filter != null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;
            var values = fields.Select(f => (row[f]?.Value switch { float v => v.ToString("0.###"), var v => v?.ToString() ?? "-" }).PadLeft(Math.Min(f.Length, 14)));
            Console.WriteLine($"{row.ID,10}  {string.Join("  ", values)}  {name}");
        }
        return 0;
    }
}
