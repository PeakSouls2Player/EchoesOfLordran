using Lordran.Tools.Formats;

namespace Lordran.Tools.Commands;

/// <summary>
/// dump-param dsr|ds2 [ParamName] [row id | name text]...
///   no ParamName       → list loaded params
///   no row selectors   → list rows (id + community name)
///   numeric selector   → print that row field by field
///   text selector      → print every row whose name contains the text
/// </summary>
public static class DumpParam
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: dump-param dsr|ds2|ds2pack [ParamName] [row id | name text]..."); return 1; }
        var store = args[1].ToLowerInvariant() switch
        {
            // session 8: "ds2pack:DIR" = the regulation of a pack built elsewhere (a sandbox), read-only
            string s when s.StartsWith("ds2pack:") => ParamStore.LoadDs2(Path.Combine(args[1]["ds2pack:".Length..], "title"), ExtractDs2.DefaultOut),
            "dsr" => ParamStore.LoadDsr(Program.DefaultDsrRoot),
            "ds2" => ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut),
            // The built pack's regulation, decrypted by Smithbox's decryptor (an independent check of our cipher).
            "ds2pack" => ParamStore.LoadDs2(Path.Combine(Build.Pack.DefaultOut, "title"), ExtractDs2.DefaultOut),
            // ds2pack:DIR = another built pack (session 8: an agent's sandbox pack, e.g. data\sandbox\potroll\pack)
            var g when g.StartsWith("ds2pack:") => ParamStore.LoadDs2(Path.Combine(args[1]["ds2pack:".Length..], "title"), ExtractDs2.DefaultOut),
            _ => throw new ArgumentException("game must be dsr or ds2"),
        };

        if (args.Length == 2)
        {
            foreach (var (name, p) in store.Params.OrderBy(p => p.Key))
                Console.WriteLine($"{name,-44} {p.ParamType,-34} rows={p.Rows.Count}");
            foreach (var (name, why) in store.Skipped.OrderBy(p => p.Key))
                Console.WriteLine($"{name,-44} SKIPPED: {why}");
            return 0;
        }

        string paramName = args[2];
        var param = store.Params[paramName];
        if (args.Length == 3)
        {
            foreach (var row in param.Rows)
                Console.WriteLine($"{row.ID,10}  {store.RowName(paramName, row.ID)}");
            return 0;
        }

        foreach (string selector in args.Skip(3))
        {
            var ids = int.TryParse(selector, out int id)
                ? new[] { id }
                : store.FindRows(paramName, selector).Select(r => r.Id).ToArray();
            foreach (int rowId in ids)
            {
                var row = param.Rows.FirstOrDefault(r => r.ID == rowId);
                if (row == null) { Console.WriteLine($"# {paramName} {rowId}: not found"); continue; }
                Console.WriteLine($"# {store.Game} {paramName} {rowId}  {store.RowName(paramName, rowId)}");
                foreach (var cell in row.Cells)
                    Console.WriteLine($"  {cell.Def.InternalName,-36} {cell.Def.DisplayType,-8} {Format(cell.Value)}");
            }
        }
        return 0;
    }

    static string Format(object value) => value switch
    {
        float f => f.ToString("0.####"),
        byte[] b => Convert.ToHexString(b),
        _ => value?.ToString() ?? "",
    };
}
