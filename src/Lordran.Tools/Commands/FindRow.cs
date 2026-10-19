namespace Lordran.Tools.Commands;

/// <summary>
/// find-row dsr|ds2 ID [ID ...] — lists every param that has a row with each id (one load, all params).
/// Session 8: missing/misordered arguments (no game, non-numeric ids, "find-row 1810000 dsr") print the usage.
/// </summary>
public static class FindRow
{
    const string Usage = "usage: find-row dsr|ds2 ID [ID ...]\n  e.g. find-row dsr 25503000 2660 2661";

    public static int Run(string[] args)
    {
        if (args.Length < 3) return CliArgs.Fail("find-row", Usage, args.Length < 2 ? "missing game and ids" : "missing ids");
        if (!CliArgs.IsGame(args[1]))
            return CliArgs.Fail("find-row", Usage, $"first argument must be dsr or ds2, got '{args[1]}'" + (args.Skip(2).Any(CliArgs.IsGame) ? " (arguments out of order?)" : ""));
        var ids = new List<int>();
        foreach (string a in args.Skip(2))
        {
            if (!int.TryParse(a, out int id)) return CliArgs.Fail("find-row", Usage, $"not a row id: '{a}'");
            ids.Add(id);
        }
        var store = CliArgs.LoadGame(args[1]);
        foreach (int id in ids)
        {
            var hits = store.Params.Where(p => p.Value[id] != null).Select(p => p.Key).Order().ToList();
            Console.WriteLine($"{id}: {(hits.Count == 0 ? "(none)" : string.Join(", ", hits))}");
        }
        return 0;
    }
}
