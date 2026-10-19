namespace Lordran.Tools.Commands;

using Lordran.Tools.Build.Maps;

/// <summary>
/// Session 11 (cap11), rules H1 / H2 / CAP1: prints Mappings/ds1-maps.tsv grouped by DS2 host (built rows and rule-H2
/// planned rows) and the WorldMapList lines the whole table needs against a cap (default: the loader's maximum,
/// src\loader\mapcap.c). Exit 1 when the table does not fit.
/// </summary>
public static class MapHostsCommand
{
    public static int Run(string[] args)
    {
        int cap = MapBuilder.LoaderMapCapMax;
        for (int i = 1; i < args.Length; i++)
            if (args[i] == "--cap" && i + 1 < args.Length) cap = int.Parse(args[++i]);
        var all = MapHosts.Rows.Select(r => (r, built: true)).Concat(MapHosts.Planned.Select(r => (r, built: false))).ToList();
        foreach (var g in all.GroupBy(x => x.r.Host, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key))
        {
            Console.WriteLine($"{g.Key}  prefix {Lordran.Tools.Build.Events.Ds2FlagStore.Prefix(g.Key)}");
            foreach (var (r, built) in g)
                Console.WriteLine($"    {r.Ds1Map}  {(built ? "built  " : "planned")}  {r.AreaName}");
        }
        var (lines, fits) = MapHosts.HostBudget(cap);
        Console.WriteLine($"WorldMapList: {MapHosts.VanillaLines} vanilla + {lines - MapHosts.VanillaLines} hosts = {lines} lines; cap {cap}: {(fits ? "fits" : "DOES NOT FIT")}");
        return fits ? 0 : 1;
    }
}
