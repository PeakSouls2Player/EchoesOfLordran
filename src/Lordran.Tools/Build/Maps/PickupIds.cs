namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Session 10 (asy-items i2), rule PK1 "stable pickup ids": a DS1 treasure's pickup (MapObjectInstanceParam +
/// ItemLotParam2_Other row, DS2 keeps the "taken" state of a pickup under this id in the save) keeps its id for ever.
/// Ids come from the append-only table Mappings/pickup-ids.tsv (key = DS2 map + DS1 treasure part name [+ "#n" for the
/// n-th treasure on the same part]), like F1's ds1-flag-slots.tsv. A treasure that becomes placeable later (its item gets
/// ported: the Rusted Iron Ring on the Asylum's o0500_0006, the Ring of Sacrifice on Firelink's o0500_0013) gets the
/// next free id of its map (max + 10) instead of shifting every later pickup (the old "6000 + 10 × placed index" scheme
/// moved Firelink 19026120-19026140 once ring 126 ported). The build log names every id not yet in the table ("PK1 new
/// pickup id"); `pickup-ids PACK --write-table FILE` appends them.
/// Generalises to every map: the key is DS1 data (MSB part name) and the table only grows.
/// </summary>
public static class PickupIds
{
    public static string TablePath => Path.Combine(AppContext.BaseDirectory, "Mappings", "pickup-ids.tsv");

    static Dictionary<(string Map, string Key), int>? table;

    public static Dictionary<(string Map, string Key), int> Table()
    {
        if (table != null) return table;
        table = new();
        if (!File.Exists(TablePath)) return table;
        // session 11 (burg11, rule H1 key migration): "@rehost<TAB>OLD<TAB>NEW" reads every row of map OLD as map NEW, so a
        // re-hosted map (Firelink m19_02 → m19_42) keeps its pickups' ids (the rows themselves stay append-only)
        var rehost = new Dictionary<string, string>();
        foreach (var line in File.ReadLines(TablePath))
            if (line.StartsWith("@rehost\t") && line.Split('\t') is { Length: >= 3 } rh) rehost[rh[1]] = rh[2];
        foreach (var line in File.ReadLines(TablePath))
        {
            if (line.StartsWith('#') || line.StartsWith('@') || line.Length == 0) continue;
            var c = line.Split('\t');
            if (c.Length < 3 || !int.TryParse(c[2], out int id)) continue;
            if (table.Values.Contains(id)) throw new InvalidDataException($"pickup-ids.tsv: id {id} twice");
            table[(rehost.GetValueOrDefault(c[0], c[0]), c[1])] = id;
        }
        return table;
    }

    /// <summary>The pickup id of a treasure: the table's, else the next free id of the map (noted as new).</summary>
    public static int Get(Pack pack, string map, string key, int mapPrefix, HashSet<int> used)
    {
        var t = Table();
        if (t.TryGetValue((map, key), out int id)) { used.Add(id); return id; }
        int first = mapPrefix * 10000 + 6000;
        int max = t.Where(kv => kv.Key.Map == map).Select(kv => kv.Value).Concat(used).Where(v => v >= first && v < first + 4000).DefaultIfEmpty(first - 10).Max();
        id = max + 10;
        used.Add(id);
        pack.Note($"PK1 new pickup id\t{map}\t{key}\t{id}");
        return id;
    }
}
