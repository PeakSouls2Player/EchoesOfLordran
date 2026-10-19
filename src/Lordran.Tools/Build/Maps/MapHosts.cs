namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Session 11 (track burg11, BURG-PLAN M1 track hosts), rules H1 / H2 / MH1: the single source of the DS2 map that hosts
/// each ported DS1 map (<c>Mappings/ds1-maps.tsv</c>). Rule H1: DS1 mAA_BB → DS2 m19_{40 + 3·(AA − 10) + BB}_00_00 (area 19
/// is unused by DS2; BB ≥ 40 keeps the flag prefix 1BB (Ds2FlagStore.Prefix) away from every vanilla map: m10_02 Firelink →
/// m19_42 (prefix 142), m10_01 Burg → m19_41 (141), m10_00 Depths → m19_40 (140)). A row may pin another id (PIN1: m18_01
/// keeps DS1's own id, verified in game since session 5). Many-to-one is allowed (rule H2: several DS1 maps in one host,
/// the D-cap packing; FlagRules stacks their temporary flags in row order).
/// Session 11 (cap11): rule H2 made concrete for the whole of Lordran. A row whose build option is <c>planned</c> is the
/// agreed host of a DS1 map that is not built yet: it is kept out of <see cref="Rows"/> (so existing builds, flag
/// remapping and seams do not change) and listed in <see cref="Planned"/>. <see cref="HostBudget"/> checks that every
/// host, built or planned, fits the WorldMapList cap the loader can raise to (rule CAP1, src\loader\mapcap.c:
/// <see cref="MapBuilder.LoaderMapCapMax"/>).
/// </summary>
public static class MapHosts
{
    public sealed record Row(string Ds1Map, string Host, string AreaName, string? BannerAtlas, int BannerLine, int BannerColumn,
        string TemplateMap, string Option, string Note, string[] FormerHosts)
    {
        /// <summary>"mAA_BB" of the DS1 map (the key FlagRules uses).</summary>
        public string Ds1Short => Ds1Map[..6];
        /// <summary>The host's decimal map id (AABBCCDD).</summary>
        public int HostId => int.Parse(Host[1..3] + Host[4..6] + Host[7..9] + Host[10..12]);
    }

    public static string TablePath => Path.Combine(AppContext.BaseDirectory, "Mappings", "ds1-maps.tsv");

    static List<Row>? rows, planned;

    /// <summary>Every built row of the table in file order (rows whose option is not <c>planned</c>).</summary>
    public static IReadOnlyList<Row> Rows { get { Ensure(); return rows!; } }

    /// <summary>Rule H2 rows not built yet (option <c>planned</c>), in file order.</summary>
    public static IReadOnlyList<Row> Planned { get { Ensure(); return planned!; } }

    static void Ensure()
    {
        if (rows != null) return;
        var all = Load(TablePath);
        rows = all.Where(r => r.Option != "planned").ToList();
        planned = all.Where(r => r.Option == "planned").ToList();
    }

    /// <summary>Vanilla WorldMapList lines (data\ds2\map\worldmaplist\worldmaplist.list: 38 maps incl. the DLC).</summary>
    public const int VanillaLines = 38;

    /// <summary>
    /// Rule CAP1: the WorldMapList lines the whole table needs (vanilla + one per distinct host, built or planned) and
    /// whether that fits <paramref name="cap"/>. Many DS1 maps on one host (rule H2) cost one line.
    /// </summary>
    public static (int Lines, bool Fits) HostBudget(int cap)
    {
        int hosts = Rows.Concat(Planned).Select(r => r.Host).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return (VanillaLines + hosts, VanillaLines + hosts <= cap);
    }

    /// <summary>Rule H1: the host id of a DS1 map mAA_BB (AA 10..17): m19_{40 + 3·(AA − 10) + BB}_00_00.</summary>
    public static string H1(string ds1Map)
    {
        int aa = int.Parse(ds1Map.Substring(1, 2)), bb = int.Parse(ds1Map.Substring(4, 2));
        int n = 40 + 3 * (aa - 10) + bb;
        if (aa is < 10 or > 18 || bb > 2 || n > 99) throw new ArgumentOutOfRangeException(nameof(ds1Map), $"rule H1 has no host for {ds1Map}");
        return $"m19_{n:D2}_00_00";
    }

    static List<Row> Load(string path)
    {
        var list = new List<Row>();
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var c = line.Split('\t');
            if (c.Length < 6) throw new InvalidDataException($"ds1-maps.tsv: short row '{line}'");
            string ds1 = c[0], host = c[1] == "H1" ? H1(ds1) : c[1];
            string? atlas = null; int bl = 0, bc = -1;
            if (c[3] != "-")
            {
                var b = c[3].Split(':');
                atlas = b[0]; bl = int.Parse(b[1]); bc = b.Length > 2 ? int.Parse(b[2]) : -1;
            }
            list.Add(new Row(ds1, host, c[2], atlas, bl, bc, c[4], c[5], c.Length > 6 ? c[6] : "", c.Length > 7 ? c[7].Split(',', StringSplitOptions.RemoveEmptyEntries) : []));
        }
        foreach (var dup in list.GroupBy(r => r.Ds1Map).Where(g => g.Count() > 1))
            throw new InvalidDataException($"ds1-maps.tsv: {dup.Key} twice");
        int lines = VanillaLines + list.Select(r => r.Host).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (lines > MapBuilder.LoaderMapCapMax)
            throw new InvalidDataException($"ds1-maps.tsv: built + planned hosts need {lines} WorldMapList lines, more than the loader's map cap {MapBuilder.LoaderMapCapMax} (rule CAP1: pack more DS1 maps per host, rule H2)");
        foreach (var r in list)
        {
            int p = Events.Ds2FlagStore.Prefix(r.Host);
            if (p is >= 100 and < 140 && r.Host != "m18_01_00_00")
                throw new InvalidDataException($"ds1-maps.tsv: host {r.Host} of {r.Ds1Map} has flag prefix {p}, a vanilla map's range (rule H1 needs 1BB with BB >= 40)");
        }
        return list;
    }

    /// <summary>The DS1 map's row ("mAA_BB" or the full id), or null when it isn't ported.</summary>
    public static Row? Of(string ds1Map) => Rows.FirstOrDefault(r => r.Ds1Map.StartsWith(ds1Map[..Math.Min(ds1Map.Length, 6)], StringComparison.OrdinalIgnoreCase)
        && (ds1Map.Length <= 6 || r.Ds1Map.Equals(ds1Map, StringComparison.OrdinalIgnoreCase)));

    /// <summary>The DS2 host of a DS1 map ("mAA_BB" or "mAA_BB_CC_DD").</summary>
    public static string Host(string ds1Map) => Of(ds1Map)?.Host ?? throw new KeyNotFoundException($"{ds1Map} has no row in ds1-maps.tsv");

    /// <summary>The DS1 map a DS2 host carries (the first row when several DS1 maps share it), or null for a DS2 map.</summary>
    public static string? Ds1Of(string ds2Map) => Rows.FirstOrDefault(r => r.Host.Equals(ds2Map, StringComparison.OrdinalIgnoreCase))?.Ds1Map;

    /// <summary>"mAA_BB" → host, in table order (FlagRules.Host).</summary>
    public static Dictionary<string, string> FlagHosts() => Rows.ToDictionary(r => r.Ds1Short, r => r.Host);

    /// <summary>
    /// Session 11 (fx11), rule ON1: the DS2 object model a ported DS1 object model becomes in host area <paramref name="aa"/>:
    /// o{AA}_{BBBB} (DS1 oBBBB) — unless DS2 itself has a model of that name (area 19 is DS2's too: Iron Keep's o19_1000 /
    /// 1001 / 1060 / 1110 / 1130 / 1140 / 1150 / 1160 / 1301; our archive, MapObject* rows and FMOD names o{model}{id} would
    /// replace or shadow DS2's), then o{AA}_{(BBBB + 6000) mod 10000} (o19_1130 → o19_7130), which must be free too.
    /// </summary>
    public static string ObjectName(string aa, string ds1Model)
    {
        string n = $"o{aa}_{ds1Model.Substring(1, 4)}";
        string data = Commands.ExtractDs2.DefaultOut;
        if (!File.Exists(Path.Combine(data, "model", "obj", n + ".bnd"))) return n;
        string alt = $"o{aa}_{(int.Parse(ds1Model.Substring(1, 4)) + 6000) % 10000:D4}";
        if (File.Exists(Path.Combine(data, "model", "obj", alt + ".bnd"))) throw new InvalidDataException($"rule ON1: {n} and {alt} are both DS2 models (DS1 {ds1Model})");
        return alt;
    }

    /// <summary>The DS2 AABB of a host (m19_42_00_00 → 1942): the prefix of instance, key-guide and helper ids (ID1).</summary>
    public static int Aabb(string ds2Map) => int.Parse(ds2Map.Substring(1, 2) + ds2Map.Substring(4, 2));
}
