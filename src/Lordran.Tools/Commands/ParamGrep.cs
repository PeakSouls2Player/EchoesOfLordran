namespace Lordran.Tools.Commands;

/// <summary>
/// param-grep dsr|ds2 ParamName FIELD_REGEX VALUE [--show FIELD_REGEX] [--max N] — rows having any field whose name matches
/// FIELD_REGEX with value VALUE; prints the row id, its community name and the fields matching --show (default: the
/// non-zero fields). Example: param-grep ds2 ItemLotParam2_Chr ^itemId 60510000 --show "itemId|itemWeight|lotCount"
/// Session 8: arguments are validated (game, param name with suggestions, regexes, --max) and print the usage instead
/// of crashing; a field regex that matches no field of the param is reported.
/// </summary>
public static class ParamGrep
{
    const string Usage =
        "usage: param-grep dsr|ds2 ParamName FIELD_REGEX VALUE [--show FIELD_REGEX] [--max N]\n" +
        "  e.g. param-grep dsr ItemLotParam ^lotItemId0 852000 --show \"lotItemId|getItemFlagId\"\n" +
        "       param-grep ds2 ItemLotParam2_Chr ^itemId 60510000 --max 20";

    public static int Run(string[] args)
    {
        // positional arguments are the ones before the first option
        var pos = args.Skip(1).TakeWhile(a => !a.StartsWith("--")).ToList();
        if (pos.Count < 4) return CliArgs.Fail("param-grep", Usage, $"expected 4 arguments before the options, got {pos.Count}");
        if (pos.Count > 4) return CliArgs.Fail("param-grep", Usage, $"too many arguments: {string.Join(" ", pos.Skip(4))} (quote a regex with spaces)");
        if (!CliArgs.IsGame(pos[0]))
            return CliArgs.Fail("param-grep", Usage, $"first argument must be dsr or ds2, got '{pos[0]}'" + (pos.Skip(1).Any(CliArgs.IsGame) ? " (arguments out of order?)" : ""));
        foreach (string opt in args.Skip(1).Where(a => a.StartsWith("--")))
            if (opt is not ("--show" or "--max")) return CliArgs.Fail("param-grep", Usage, $"unknown option {opt}");
        string? show = CliArgs.Option(args, "--show", out bool showMissing);
        string? maxText = CliArgs.Option(args, "--max", out bool maxMissing);
        if (showMissing) return CliArgs.Fail("param-grep", Usage, "--show needs a FIELD_REGEX");
        if (maxMissing) return CliArgs.Fail("param-grep", Usage, "--max needs a number");
        int max = 200;
        if (maxText != null && (!int.TryParse(maxText, out max) || max <= 0)) return CliArgs.Fail("param-grep", Usage, $"--max must be a positive number, got '{maxText}'");
        System.Text.RegularExpressions.Regex field, showRe;
        try
        {
            field = new System.Text.RegularExpressions.Regex(pos[2]);
            showRe = show != null ? new System.Text.RegularExpressions.Regex(show) : null!;
        }
        catch (ArgumentException e) { return CliArgs.Fail("param-grep", Usage, $"bad regex: {e.Message}"); }

        var store = CliArgs.LoadGame(pos[0]);
        string paramName = pos[1];
        if (!store.Params.TryGetValue(paramName, out var param))
            return CliArgs.Fail("param-grep", Usage, $"no param '{paramName}' in {pos[0]}" + CliArgs.Suggest(store.Params.Keys, paramName)
                + (store.Skipped.ContainsKey(paramName) ? $" (it was skipped: {store.Skipped[paramName]})" : ""));
        if (param.Rows.Count > 0 && !param.Rows[0].Cells.Any(c => field.IsMatch(c.Def.InternalName)))
            return CliArgs.Fail("param-grep", Usage, $"FIELD_REGEX '{pos[2]}' matches no field of {paramName}; fields: " +
                string.Join(" ", param.Rows[0].Cells.Select(c => c.Def.InternalName).Take(40)) + (param.Rows[0].Cells.Count > 40 ? " …" : ""));
        string value = pos[3];
        int n = 0;
        foreach (var row in param.Rows)
        {
            if (!row.Cells.Any(c => field.IsMatch(c.Def.InternalName) && System.Convert.ToString(c.Value, System.Globalization.CultureInfo.InvariantCulture) == value)) continue;
            var cells = row.Cells.Where(c => show != null ? showRe.IsMatch(c.Def.InternalName) : System.Convert.ToString(c.Value) is not ("0" or "-1" or "0.0" or ""));
            Console.WriteLine($"{row.ID,10} {store.RowName(paramName, row.ID),-40} " + string.Join(" ", cells.Select(c => $"{c.Def.InternalName}={(c.Value is byte[] b ? System.Convert.ToHexString(b) : c.Value)}")));
            if (++n >= max) break;
        }
        Console.WriteLine($"{n} row(s)");
        return 0;
    }
}
