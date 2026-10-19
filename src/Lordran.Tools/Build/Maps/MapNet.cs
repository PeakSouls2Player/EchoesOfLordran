using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Session 11 (burg11, BURG-PLAN NET1 second half, rule NET2): a MapObjects map's multiplayer areas and invasion points.
/// DS2 (FoFG m10_10, Brume m50_36): each collision's UnkT20 names a PlayAreaParam row (FoFG 101010..101070, multiPlayZoneId
/// 101000 = the bonfires' multiPlayAreaid = map id / 100); intrudepointparam_{map} row N = an invasion point whose position
/// is EventLocation row N of the map (categoryType 1, categoryId 1, box 0.6 × 2.2 × 1, FoFG 1001..7006), playAreaId = the
/// area it serves, intrudePriority 1..n. DS1 has the same pair: collision PlayRegionID (m10_01: 101000, 101010, 101011,
/// 101100, 101101, 101200, 101210; negative = keep the current area, 0 = none) and one "黒侵入ポイントNNN" region per area
/// (entity prefix·10000 + 4000 + n for play region prefix·1000 + n: 1014011 ↔ 101011). Rule NET2: DS1 play region k (in
/// ascending order) → PlayAreaParam AABB·100 + 10·(k + 1) (a copy of FoFG 101010, zone AABB·100); DS1's invasion points →
/// IntrudePointParam + EventLocation rows 1000·(k + 1) + i, DS1 position, P1 yaw, priority i.
/// </summary>
public static class MapNet
{
    const string Fofg = "m10_10_00_00";

    /// <summary>DS1 play region → DS2 PlayAreaParam row (rule NET2), for the map's DS1 collisions; cached per map.</summary>
    public static IReadOnlyDictionary<int, int> PlayAreas(string dsrRoot, string ds1Map, string ds2Map)
    {
        string key = ds1Map + "|" + ds2Map;
        if (cache.TryGetValue(key, out var hit)) return hit;
        var msb1 = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", ds1Map + ".msb"));
        int aabb = MapHosts.Aabb(ds2Map);
        // round 9, rule H2-P: a merged map's play regions member by member (a later member never shifts an earlier one's areas)
        var regions = msb1.Parts.Collisions.Where(c => c.PlayRegionID > 0).GroupBy(c => c.PlayRegionID)
            .OrderBy(g => g.Min(c => H2Merge.MemberRank(ds1Map, c.Name))).ThenBy(g => g.Key).Select(g => g.Key).ToList();
        // session 11 (burg11, round 8), rule NET2b: a merged host (rule H2) has more than 9: the 10th.. take AABB·100 + 10·k + 5
        // round 9: series s = k / 9 takes the last digit Series[s] (0, 5, then 2, 7: the 19th..36th of a 3-member host)
        if (regions.Count > 9 * Series.Length) throw new InvalidDataException($"rule NET2: {ds1Map} has {regions.Count} DS1 play regions; AABB·100 + 10·k + (0|5|2|7) holds {9 * Series.Length}");
        var map = regions.Select((r, k) => (r, k)).ToDictionary(x => x.r, x => aabb * 100 + 10 * (x.k % 9 + (x.k < 9 ? 1 : 0)) + Series[x.k / 9]);
        cache[key] = map;
        return map;
    }
    static readonly Dictionary<string, IReadOnlyDictionary<int, int>> cache = [];
    /// <summary>Rule NET2b: the last digit of each series of 9 play areas, and its intrude-point id offset.</summary>
    static readonly int[] Series = [0, 5, 2, 7];
    static readonly int[] SeriesIds = [0, 500, 250, 750];

    /// <summary>The DS2 collision UnkT20 for a DS1 collision's PlayRegionID: its area, -1 for DS1's "keep" (negative), 0 for none.</summary>
    public static int CollisionArea(IReadOnlyDictionary<int, int> areas, int ds1Region) =>
        ds1Region > 0 && areas.TryGetValue(ds1Region, out int a) ? a : ds1Region < 0 ? -1 : 0;

    /// <summary>Rule NET2: the PlayAreaParam rows, the map's IntrudePointParam and its EventLocation rows.</summary>
    public static void Build(Pack pack, MapBuilder.Options map)
    {
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        var areas = PlayAreas(Program.DefaultDsrRoot, map.Ds1Map, map.Ds2Map);
        int zone = MapHosts.Aabb(map.Ds2Map) * 100;
        var tArea = pack.Param("PlayAreaParam")[101010] ?? throw new InvalidDataException("PlayAreaParam has no row 101010");
        foreach (var (ds1, ds2) in areas)
        {
            if (pack.Param("PlayAreaParam")[ds2] != null) continue;
            var row = pack.AddRow("PlayAreaParam", tArea, ds2, $"[Lordran] {map.Ds2Map} DS1 play region {ds1}");
            row["multiPlayZoneId"].Value = Convert.ChangeType(zone, row["multiPlayZoneId"].Value.GetType());
        }

        var msb1 = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", map.Ds1Map + ".msb"));
        var intrude = PARAM.Read(Path.Combine(pack.Ds2Data, "param", $"intrudepointparam_{Fofg}.param"));
        if (!intrude.ApplyParamdefCarefully(defs)) throw new InvalidDataException("no paramdef fits IntrudePointParam");
        var tPoint = intrude[1001] ?? throw new InvalidDataException($"intrudepointparam_{Fofg} has no row 1001");
        intrude.Rows.Clear();
        var locs = MapLinks.Locations(pack, map, defs);
        var tLoc = MapLinks.Template(pack, defs, Fofg, 1001);
        var lines = new List<string>();
        var points = msb1.Regions.GetEntries()
            .Where(r => r.EntityID > 0 && r.Name.Contains("侵入ポイント") && r.EntityID % 10000 / 1000 == 4)
            .Select(r => (Region: r, Ds1Area: r.EntityID / 10000 * 1000 + r.EntityID % 1000))
            .OrderBy(x => x.Region.EntityID).ToList();
        foreach (var g in points.GroupBy(x => x.Ds1Area))
        {
            if (!areas.TryGetValue(g.Key, out int area))
            {
                foreach (var x in g) lines.Add($"DS1 point {x.Region.Name} ({x.Region.EntityID}): DS1 play region {g.Key} is on no collision → left out");
                continue;
            }
            int series = Array.IndexOf(Series, area % 10), k = (area % 100) / 10, i = 0, second = SeriesIds[series]; // rule NET2b
            if (series > 0) k++;
            foreach (var x in g)
            {
                int id = 1000 * k + second + ++i;
                var row = new PARAM.Row(tPoint) { ID = id };
                row["playAreaId"].Value = Convert.ChangeType(area, row["playAreaId"].Value.GetType());
                row["intrudePriority"].Value = Convert.ChangeType(i, row["intrudePriority"].Value.GetType());
                intrude.Rows.Add(row);
                var rot = new Vector3(0, Ds1Placement.Yaw(x.Region.Rotation.Y), 0); // rule P1: an invader stands there
                MapLinks.Put(locs, tLoc, id, x.Region.Position, rot);
                lines.Add($"{id}: DS1 {x.Region.Name} ({x.Region.EntityID}) at {x.Region.Position:0.00} → play area {area} (DS1 {g.Key}), priority {i}");
            }
        }
        intrude.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
        if (intrude.Rows.Count > 0) pack.AddFile("gamedata", $"param/intrudepointparam_{map.Ds2Map}.param", intrude.Write());
        pack.AddFile("gamedata", $"param/eventlocation_{map.Ds2Map}.param", locs.Write());
        pack.Note($"{map.Ds2Map}: rule NET2: {areas.Count} play areas ({string.Join(", ", areas.Select(a => $"DS1 {a.Key} → {a.Value}"))}, zone {zone}), {intrude.Rows.Count} invasion points");
        foreach (var l in lines) pack.Note($"{map.Ds2Map}: rule NET2: {l}");
    }
}
