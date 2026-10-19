using Lordran.Tools.Formats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Argument checks shared by the research commands. Session 8 (census tool gap): param-grep, find-row and emevd-grep
/// threw IndexOutOfRange/FormatException/KeyNotFound on missing or misordered arguments; they now print their usage
/// (exit code 2) and say which argument was wrong.
/// </summary>
public static class CliArgs
{
    /// <summary>Prints "COMMAND: why" and the usage to stderr; returns exit code 2.</summary>
    public static int Fail(string command, string usage, string why)
    {
        Console.Error.WriteLine($"{command}: {why}");
        Console.Error.WriteLine(usage);
        return 2;
    }

    public static bool IsGame(string s) => s.Equals("dsr", StringComparison.OrdinalIgnoreCase) || s.Equals("ds2", StringComparison.OrdinalIgnoreCase);

    /// <summary>Loads the DSR or DS2 params for a validated game argument.</summary>
    public static ParamStore LoadGame(string game) => game.Equals("dsr", StringComparison.OrdinalIgnoreCase)
        ? ParamStore.LoadDsr(Program.DefaultDsrRoot)
        : ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);

    /// <summary>The value after an option (--show X), or null; "missing" is set when the option is last.</summary>
    public static string? Option(string[] args, string name, out bool missing)
    {
        missing = false;
        int i = Array.IndexOf(args, name);
        if (i < 0) return null;
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--")) { missing = true; return null; }
        return args[i + 1];
    }

    /// <summary>Param names close to a wrong one (substring either way, or the same first 5 letters), for "did you mean".</summary>
    public static string Suggest(IEnumerable<string> names, string wrong)
    {
        string stem = wrong.Length > 5 ? wrong[..5] : wrong;
        var close = names.Where(n => n.Contains(wrong, StringComparison.OrdinalIgnoreCase) || wrong.Contains(n, StringComparison.OrdinalIgnoreCase)
            || n.StartsWith(stem, StringComparison.OrdinalIgnoreCase)).Order().Take(8).ToList();
        return close.Count > 0 ? " (did you mean " + string.Join(", ", close) + "?)" : "";
    }
}
