using System.Text;
using Lordran.Tools.Build.Maps;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (seam12, rule SS1): <c>seam-stream-check PACK [--dll DINPUT8.DLL] [--ds2 DATA\DS2]</c>: does DS2 actually
/// stream each neighbour our ConnectCollisions name? (seam-check / walk-check assume it does.)
/// The runtime (DarkSoulsII.exe v1.0.3.0): FUN_1403dc3e0 sets avail[slot] = FUN_1402db4f0(registry, map) for every
/// WorldMapList line; only slots with avail ≠ 0 take the connect masks of the collision under the player (FUN_1401c8cd0),
/// the second-order propagation (FUN_1403dc930) and the category-23 stall (FUN_1401ea510). FUN_1402db4f0 answers from a
/// hard-coded 28-map table (FUN_141073830 → 0x141613c60: map, PlayGo chunk, 6 neighbours), so a map outside it is never
/// streamed — unless the loader's seam stream gate (src\loader\stream.c: areas 18/19) reports it available.
///  V1 (vanilla model): every vanilla ConnectCollision's map and target are in the table and the target is one of the
///     map's table neighbours; every vanilla category-23 EventLocation names a table map.
///  V2 (vanilla field profile): the values vanilla connects use (UnkT0C set, Draw/DispGroups, Unk44..50 mask).
///  P1 (our pack): every ConnectCollision: collision in the MSB; target listed in WorldMapList below the cap; both its map
///     and the target streamable (table, or area 18/19 with the loader gate on); fields inside the vanilla profile.
///  P2: every category-23 EventLocation names a streamable, listed map.
///  L1: loader.ini seam_stream ≠ 0 and (with --dll) the dll carries the gate ("seam stream: on").
/// </summary>
public static class SeamStreamCheck
{
    /// <summary>FUN_141073830 (decompile l.3190091): {map, chunk, neighbours} as DS2 hard-codes them.</summary>
    public static readonly (string Map, int Chunk, string[] Neighbours)[] ChunkTable =
    [
        ("m10_02_00_00", 5, ["m10_04_00_00"]),
        ("m10_04_00_00", 6, ["m10_02_00_00", "m10_10_00_00", "m10_29_00_00", "m10_31_00_00", "m10_34_00_00", "m10_23_00_00"]),
        ("m10_10_00_00", 7, ["m10_04_00_00"]),
        ("m10_29_00_00", 8, ["m10_04_00_00", "m10_32_00_00"]),
        ("m10_31_00_00", 9, ["m10_04_00_00", "m10_30_00_00"]),
        ("m10_34_00_00", 10, ["m10_04_00_00", "m10_25_00_00"]),
        ("m10_25_00_00", 11, ["m10_34_00_00"]),
        ("m10_23_00_00", 12, ["m10_04_00_00", "m10_17_00_00"]),
        ("m10_17_00_00", 13, ["m10_19_00_00", "m10_23_00_00"]),
        ("m10_19_00_00", 14, ["m10_17_00_00"]),
        ("m10_30_00_00", 15, ["m10_18_00_00", "m10_31_00_00"]),
        ("m10_18_00_00", 16, ["m10_16_00_00", "m10_30_00_00"]),
        ("m10_16_00_00", 17, []),
        ("m10_32_00_00", 18, ["m10_15_00_00", "m10_29_00_00", "m10_33_00_00", "m20_21_00_00"]),
        ("m10_33_00_00", 19, ["m10_14_00_00", "m10_32_00_00"]),
        ("m10_14_00_00", 20, ["m10_33_00_00"]),
        ("m20_21_00_00", 21, ["m10_32_00_00", "m20_11_00_00"]),
        ("m20_11_00_00", 22, ["m20_21_00_00", "m20_24_00_00"]),
        ("m20_24_00_00", 23, ["m20_11_00_00"]),
        ("m10_15_00_00", 24, ["m10_27_00_00", "m10_32_00_00"]),
        ("m10_27_00_00", 25, ["m10_15_00_00"]),
        ("m20_26_00_00", 26, []),
        ("m40_03_00_00", 27, []),
        ("m20_10_00_00", 28, ["m10_10_00_00"]),
        ("m50_35_00_00", 29, []),
        ("m50_36_00_00", 30, []),
        ("m50_37_00_00", 31, []),
        ("m50_38_00_00", 32, []),
    ];

    /// <summary>The loader's gate (stream.c lordran_area): a map absent from the table counts as available in areas 18/19.</summary>
    public static bool GateArea(string map) => map.StartsWith("m18_") || map.StartsWith("m19_");

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: seam-stream-check PACK [--dll DINPUT8.DLL] [--ds2 DATA\\DS2]"); return 1; }
        string pack = args[1];
        string? dll = null; string ds2 = Path.Combine("data", "ds2");
        for (int i = 2; i < args.Length; i++)
            switch (args[i]) { case "--dll": dll = args[++i]; break; case "--ds2": ds2 = args[++i]; break; }
        int fails = 0, warns = 0;
        void Fail(string s) { fails++; Console.WriteLine("FAIL " + s); }
        void Warn(string s) { warns++; Console.WriteLine("WARN " + s); }
        var table = ChunkTable.ToDictionary(t => t.Map, t => t);
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        static string Target(MSB2.Part.ConnectCollision k) => $"m{k.MapID[0]:D2}_{k.MapID[1]:D2}_{k.MapID[2]:D2}_{k.MapID[3]:D2}";
        static string Decimal(int id) => $"m{id / 1000000:D2}_{id / 10000 % 100:D2}_{id / 100 % 100:D2}_{id % 100:D2}";
        static bool MaskEmpty(MSB2.Part p) => p.Unk44 == 0 && p.Unk48 == 0 && p.Unk4C == 0 && p.Unk50 == 0;
        static bool GroupsSet(MSB2.Part p) => p.DrawGroups.Any(g => g != 0) || p.DispGroups.Any(g => g != 0);

        // ---- V1 / V2: vanilla
        var vMaps = Directory.GetDirectories(Path.Combine(ds2, "map"), "m*").Select(d => Path.GetFileName(d)!).Where(m => File.Exists(Path.Combine(ds2, "map", m, m + ".msb"))).ToList();
        int vConnects = 0, vBoxes = 0, vGroups = 0, vEmpty = 0, vFlag = 0, v1Fails = 0;
        var t0c = new HashSet<int>();
        foreach (var m in vMaps)
        {
            var msb = MSB2.Read(Path.Combine(ds2, "map", m, m + ".msb"));
            foreach (var k in msb.Parts.ConnectCollisions)
            {
                vConnects++;
                string t = Target(k);
                if (!table.TryGetValue(m, out var row)) { v1Fails++; Fail($"V1 vanilla {m} {k.Name}: map not in DS2's chunk table (the model is wrong)"); }
                else if (!row.Neighbours.Contains(t)) { v1Fails++; Fail($"V1 vanilla {m} {k.Name} → {t}: not a table neighbour of {m} (the model is wrong)"); }
                if (!table.ContainsKey(t)) { v1Fails++; Fail($"V1 vanilla {m} {k.Name} → {t}: target not in the chunk table"); }
                t0c.Add(k.UnkT0C);
                if (k.UnkT08 != 0) vFlag++;
                if (GroupsSet(k)) vGroups++;
                if (MaskEmpty(k)) vEmpty++;
            }
            string el = Path.Combine(ds2, "param", $"eventlocation_{m}.param");
            if (!File.Exists(el)) continue;
            var p = PARAM.Read(el);
            if (!p.ApplyParamdefCarefully(defs)) continue;
            foreach (var r in p.Rows.Where(r => Convert.ToInt32(r["categoryType"].Value) == 23))
            {
                vBoxes++;
                string t = Decimal(Convert.ToInt32(r["categoryId"].Value));
                if (!table.ContainsKey(t)) { v1Fails++; Fail($"V1 vanilla {m} category-23 box {r.ID} → {t}: target not in the chunk table"); }
            }
        }
        Console.WriteLine($"V1 vanilla: {vMaps.Count} maps, {vConnects} ConnectCollisions, {vBoxes} category-23 boxes; every connect's map and target in DS2's 28-map chunk table with the target a table neighbour, every box target in the table: {(v1Fails == 0 ? "yes" : "NO")}");
        Console.WriteLine($"V2 vanilla connect profile: UnkT0C values {{{string.Join(",", t0c.Order())}}}, {vFlag} with a selector flag UnkT08, {vGroups} with Draw/DispGroups set, {vEmpty} with an empty Unk44..50 mask");

        // ---- L1: loader
        bool gate = true;
        string ini = Path.Combine(pack, "loader.ini");
        var iniLines = File.Exists(ini) ? File.ReadAllLines(ini).Select(l => l.Trim()).ToList() : [];
        if (iniLines.FirstOrDefault(l => l.StartsWith("seam_stream", StringComparison.OrdinalIgnoreCase)) is { } sl && sl.Split('=').ElementAtOrDefault(1)?.Trim() == "0") gate = false;
        int mapCap = 42;
        if (iniLines.FirstOrDefault(l => l.StartsWith("map_cap=")) is { } mc && int.TryParse(mc[8..], out int c) && c > 42) mapCap = Math.Min(c, 48);
        Console.WriteLine($"L1 loader.ini: seam_stream {(gate ? "on (absent = 1)" : "OFF")}, map_cap → {mapCap} usable WorldMapList lines");
        if (dll != null)
        {
            bool has = File.ReadAllBytes(dll).AsSpan().IndexOf(Encoding.Unicode.GetBytes("seam stream: on")) >= 0;
            Console.WriteLine($"L1 {dll}: {(has ? "has" : "LACKS")} the seam stream gate (stream.c)");
            if (!has) gate = false;
        }
        bool Streamable(string map) => table.ContainsKey(map) || (gate && GateArea(map));
        string Why(string map) => table.ContainsKey(map) ? "chunk table" : GateArea(map) ? (gate ? "loader gate (area 18/19)" : "area 18/19 but NO loader gate") : "not in the chunk table, outside the gate's areas";

        // ---- P1 / P2: our pack
        string gamedata = Path.Combine(pack, "gamedata");
        var list = File.ReadAllLines(Path.Combine(gamedata, "map", "worldmaplist", "worldmaplist.list"), Encoding.Latin1)
            .Select(l => l.Trim()).Where(l => l.StartsWith('m')).Select(l => l.Split('\t', ' ')[0]).ToList();
        var ours = Directory.GetDirectories(Path.Combine(gamedata, "map"), "m*").Select(d => Path.GetFileName(d)!).Where(m => File.Exists(Path.Combine(gamedata, "map", m, m + ".msb"))).ToList();
        int pConnects = 0, pBoxes = 0;
        var pairs = new SortedDictionary<string, int>();
        foreach (var m in ours)
        {
            var msb = MSB2.Read(Path.Combine(gamedata, "map", m, m + ".msb"));
            if (msb.Parts.ConnectCollisions.Count > 0 && !Streamable(m)) Warn($"P1 {m}: the map itself is not available ({Why(m)}): FUN_1403dc930's second-order propagation skips it");
            foreach (var k in msb.Parts.ConnectCollisions)
            {
                pConnects++;
                string t = Target(k);
                pairs[$"{m} → {t}"] = pairs.GetValueOrDefault($"{m} → {t}") + 1;
                if (!msb.Parts.Collisions.Any(x => x.Name == k.CollisionName)) Fail($"P1 {m} {k.Name}: collision {k.CollisionName} not in the MSB");
                int idx = list.IndexOf(t);
                if (idx < 0 || idx >= mapCap) Fail($"P1 {m} {k.Name} → {t}: WorldMapList line {(idx < 0 ? "missing" : idx.ToString())} (slots 0..{mapCap - 1}; FUN_1401c9030 gives 0xFF = dead)");
                if (!Streamable(t)) Fail($"P1 {m} {k.Name} → {t}: DS2 never streams it ({Why(t)}; FUN_1402db4f0 = 0 → avail 0)");
                if (!t0c.Contains(k.UnkT0C)) Fail($"P1 {m} {k.Name}: UnkT0C {k.UnkT0C} outside vanilla's {{{string.Join(",", t0c)}}}");
                if (k.UnkT08 == 0 && k.UnkT0C != 0) Fail($"P1 {m} {k.Name}: UnkT08 0 with UnkT0C {k.UnkT0C} (flag 0 never equals 1: connect always off)");
                if (vGroups == 0 && GroupsSet(k)) Warn($"P1 {m} {k.Name}: Draw/DispGroups set (vanilla connects: all 0)");
                if (MaskEmpty(k)) Fail($"P1 {m} {k.Name} → {t}: empty Unk44..50 mask (the target is never wanted: FUN_1403dc930 needs a mask ≠ 0)");
            }
            string el = Path.Combine(gamedata, "param", $"eventlocation_{m}.param");
            if (!File.Exists(el)) continue;
            var p = PARAM.Read(el);
            if (!p.ApplyParamdefCarefully(defs)) { Fail($"P2 EventLocation_{m}: no paramdef fits"); continue; }
            foreach (var r in p.Rows.Where(r => Convert.ToInt32(r["categoryType"].Value) == 23))
            {
                pBoxes++;
                string t = Decimal(Convert.ToInt32(r["categoryId"].Value));
                if (!list.Contains(t)) Fail($"P2 {m} category-23 box {r.ID} → {t}: not in WorldMapList");
                if (!Streamable(t)) Fail($"P2 {m} category-23 box {r.ID} → {t}: {Why(t)} (FUN_1401ea510 skips the stall)");
            }
        }
        foreach (var (pair, n) in pairs)
        {
            var parts = pair.Split(" → ");
            Console.WriteLine($"P1 {pair}: {n} connects; streamed: {(Streamable(parts[1]) ? "yes" : "NO")} ({Why(parts[1])}); WorldMapList line {list.IndexOf(parts[1])}");
        }
        Console.WriteLine($"P1/P2 pack: {ours.Count} maps, {pConnects} ConnectCollisions, {pBoxes} category-23 boxes");
        Console.WriteLine($"seam-stream-check: {fails} FAIL, {warns} WARN");
        return fails > 0 ? 1 : 0;
    }
}
