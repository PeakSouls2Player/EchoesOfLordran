using System.Numerics;
using System.Reflection;
using Lordran.Tools.Build;
using Lordran.Tools.Build.Events;
using Lordran.Tools.Build.Maps;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// spot-check MAP|all [--pack DIR] [--boxes] — session 9 (track npcplace): every EventLocation point of a built pack's
/// map (MapLinks' test warps, the warp targets of the translated events: where the player is put) and every generator
/// (NPCs, enemies: where a character is spawned) ray-cast against the DS2 collision the pack ships for that map (MSB2
/// collision parts × the h-archive: converted DS1 filter-8 collision + step ramps, <see cref="FloorProbe.FromDs2Map"/>).
/// Per row: the floor under it (highest walkable surface at most 0.05 m above the point), the gap (point − floor), the
/// nearest surface over it, and a verdict:
/// <list type="bullet">
/// <item>points: OK 0.05–0.8 m over a floor (the placement rule puts them 0.3–0.5 m over it); FAIL no floor (falls out of
/// the world), FAIL under (a floor ≤ 1 m over the point and none within 1 m under it: the player starts below the
/// surface and falls through, run s9-1 3700020), FAIL drop (&gt; 2 m: run s8-5 3700170 fell 12 m); WARN otherwise.</item>
/// <item>generators: OK |gap| ≤ 0.15 m (DS2's own: vanilla generators stand on their floor, `spot-check --vanilla`);
/// WARN floats (gap &gt; 0.15 m: the character hangs in the air when its idle doesn't fall, run s9-2 Oscar +0.5 m) or sunk;
/// FAIL no floor / under.</item>
/// <item>facing (rule P1, fix pass): generators of DS1 characters and DS1 region rows of our maps: FAIL when the yaw is
/// not DS1's θ + 180 (characters, placement regions) or θ (areas); OK rows carry "[P1 ok: …]".</item>
/// </list>
/// `--vanilla MAP` measures DS2's own generators the same way (data\ds2: MapOffset added to the map-local params).
/// Exit code 1 when any FAIL.
/// </summary>
public static class SpotCheck
{
    public sealed record Row(string Kind, int Id, string Label, Vector3 Pos, float Yaw, float? Floor, float? Above, string FloorPart, string Verdict, string Why);

    public static int Run(string[] args)
    {
        string? vanilla = args.SkipWhile(a => a != "--vanilla").Skip(1).FirstOrDefault();
        if (vanilla != null) return Vanilla(vanilla);
        string pack = args.SkipWhile(a => a != "--pack").Skip(1).FirstOrDefault() ?? Pack.DefaultOut;
        int pi = Array.IndexOf(args, "--probe");
        if (pi > 0) return Probe(Path.Combine(pack, "gamedata"), args[1], new Vector3(float.Parse(args[pi + 1]), float.Parse(args[pi + 2]), float.Parse(args[pi + 3])));
        bool boxes = args.Contains("--boxes");
        string which = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : "all";
        string gamedata = Path.Combine(pack, "gamedata");
        var maps = which == "all"
            ? Directory.GetDirectories(Path.Combine(gamedata, "map")).Select(Path.GetFileName).Where(m => m!.StartsWith('m') && File.Exists(Path.Combine(gamedata, "map", m, m + ".msb"))).Select(m => m!).OrderBy(m => m).ToList()
            : [which];
        int fails = 0;
        Console.WriteLine($"spot-check {pack}");
        foreach (string map in maps)
        {
            var rows = Check(gamedata, map, boxes);
            Console.WriteLine($"== {map}: {rows.Count} rows");
            foreach (var r in rows)
            {
                string floor = r.Floor is float f ? $"floor {f,8:0.00} gap {r.Pos.Y - f,6:+0.00;-0.00}" : "floor     none           ";
                string above = r.Above is float a ? $" over {a - r.Pos.Y:+0.00}" : "";
                Console.WriteLine($"  {r.Verdict,-4} {r.Kind,-4} {r.Id,9} ({r.Pos.X,8:0.00}, {r.Pos.Y,8:0.00}, {r.Pos.Z,8:0.00}) yaw {r.Yaw,6:0.#} {floor}{above}  {r.Label}{(r.Why.Length > 0 ? " — " + r.Why : "")}");
                if (r.Verdict == "FAIL") fails++;
            }
        }
        Console.WriteLine(fails == 0 ? "spot-check: no FAIL" : $"spot-check: {fails} FAIL");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>MapLinks' named test-warp ids (its public int constants), for labels.</summary>
    static Dictionary<int, List<string>> Names()
    {
        var names = new Dictionary<int, List<string>>();
        foreach (var f in typeof(MapLinks).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(int)))
        {
            int v = (int)f.GetRawConstantValue()!;
            if (v < 3700000 || v > 3799999) continue;
            if (!names.TryGetValue(v, out var l)) names[v] = l = [];
            l.Add(f.Name);
        }
        return names;
    }

    public static List<Row> Check(string gamedata, string map, bool boxes = false)
    {
        var probe = FloorProbe.FromDs2Map(p => File.Exists(Path.Combine(gamedata, p)) ? File.ReadAllBytes(Path.Combine(gamedata, p)) : null, map);
        var offset = MSB2.Read(Path.Combine(gamedata, "map", map, map + ".msb")).Events.MapOffsets.FirstOrDefault()?.Translation ?? Vector3.Zero;
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        var names = Names();
        // which map's MapLinks labels apply: Asylum spots and Firelink spots share ids (3700200 …)
        bool firelink = map == Lordran.Tools.Build.Maps.MapHosts.Host("m10_02_00_00") || map.StartsWith("m10_02");
        string Label(int id)
        {
            if (!names.TryGetValue(id, out var l)) return id is >= 3701000 and < 3702000 ? "DS1 region point (warp target)" : "";
            var pick = l.Where(n => firelink ? !n.StartsWith("Asylum") : !n.StartsWith("Firelink")).ToList();
            return string.Join("/", pick.Count > 0 ? pick : l);
        }
        var rows = new List<Row>();
        string locPath = Path.Combine(gamedata, "param", $"eventlocation_{map}.param");
        if (File.Exists(locPath))
        {
            var p = PARAM.Read(locPath);
            if (p.ApplyParamdefCarefully(defs))
                foreach (var r in p.Rows)
                {
                    int form = Convert.ToInt32(r["formType"].Value);
                    if (form != 1 && !boxes) continue;
                    var pos = new Vector3(F(r, "PositionX"), F(r, "PositionY"), F(r, "PositionZ")) + offset;
                    rows.Add(Judge(probe, form == 1 ? "warp" : "box", r.ID, Label(r.ID), pos, F(r, "RotationY"), character: false));
                }
        }
        string genLoc = Path.Combine(gamedata, "param", $"generatorlocation_{map}.param"), genPar = Path.Combine(gamedata, "param", $"generatorparam_{map}.param"), regPar = Path.Combine(gamedata, "param", $"generatorregistparam_{map}.param");
        if (File.Exists(genLoc) && File.Exists(genPar) && !Formats.ParamFiles.Rowless(genLoc) && !Formats.ParamFiles.Rowless(genPar)) // session 11: rowless = no characters
        {
            var loc = Raw(genLoc); var gen = Raw(genPar);
            PARAM? reg = null;
            if (File.Exists(regPar)) { reg = PARAM.Read(regPar); if (!reg.ApplyParamdefCarefully(defs)) reg = null; }
            foreach (var lr in loc.Rows)
            {
                var l = (byte[])lr["data"].Value;
                var pos = new Vector3(BitConverter.ToSingle(l, 0), BitConverter.ToSingle(l, 4), BitConverter.ToSingle(l, 8)) + offset;
                var gr = gen.Rows.FirstOrDefault(x => x.ID == lr.ID);
                string label = "";
                bool sits = false;
                if (gr != null && reg?[BitConverter.ToInt32((byte[])gr["data"].Value, 8)] is { } rr)
                {
                    label = $"EnemyParam {rr["EnemyId"].Value} startUp {rr["startUpStateType"].Value}";
                    sits = Convert.ToInt32(rr["startUpStateType"].Value) == 13; // DS2's sitting NPC idle (Saulden's)
                }
                rows.Add(Judge(probe, "gen", lr.ID, label + (sits ? " (sits)" : ""), pos, BitConverter.ToSingle(l, 16), character: true));
            }
        }
        return CheckFacing(map, rows);
    }

    /// <summary>The DS1 map a pack map is converted from (null: not one of ours).</summary>
    public static string? Ds1MapOf(string ds2Map) => Lordran.Tools.Build.Maps.MapHosts.Ds1Of(ds2Map); // session 11 (burg11): every MapHosts row (rule MH1)

    /// <summary>
    /// Rule P1 on the written rows (session 9 fix pass: the check the merges keep): a generator whose DS1 character
    /// (EntityRules.Generator(entity) == id) has rotation θ must have yaw θ + 180; a DS1 region row (3701000 + entity %
    /// 1000) must have θ + 180 when DS1 places a character there (Ds1Placement.IsPlacementRegion), θ otherwise. A miss is
    /// a FAIL "P1" (e.g. a merge that lost Ds1Placement.Generator leaves an enemy's DS1 yaw).
    /// </summary>
    static List<Row> CheckFacing(string map, List<Row> rows)
    {
        if (Ds1MapOf(map) is not { } ds1Map) return rows;
        var msb = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", ds1Map + ".msb"));
        var chars = msb.Parts.Enemies.Where(e => e.EntityID > 0).GroupBy(e => EntityRules.Generator(e.EntityID)).ToDictionary(g => g.Key, g => g.ToList());
        var regions = msb.Regions.GetEntries().Where(r => r.EntityID > 0).GroupBy(r => AsylumScript.RegionId(r.EntityID)).ToDictionary(g => g.Key, g => g.ToList());
        static float D(float a, float b) { float d = (a - b) % 360; if (d > 180) d -= 360; if (d < -180) d += 360; return MathF.Abs(d); }
        var result = new List<Row>();
        foreach (var r in rows)
        {
            float? want = null; string what = "";
            if (r.Kind == "gen" && chars.TryGetValue(r.Id, out var cs) && cs.Count == 1)
            { want = Ds1Placement.Yaw(cs[0].Rotation.Y); what = $"DS1 {cs[0].Name} θ {cs[0].Rotation.Y:0.#} → θ+180"; }
            else if (r.Kind != "gen" && r.Id is >= 3701000 and < 3702000 && regions.TryGetValue(r.Id, out var rs) && rs.Count == 1)
            {
                bool placement = Ds1Placement.TakesPlacementYaw(rs[0], Program.DefaultDsrRoot);
                want = placement ? Ds1Placement.Yaw(rs[0].Rotation.Y) : rs[0].Rotation.Y;
                what = $"DS1 region {rs[0].EntityID} θ {rs[0].Rotation.Y:0.#} ({(placement ? "placement marker → θ+180" : Ds1Placement.IsPlacementRegion(rs[0], Program.DefaultDsrRoot) ? "area-shaped placement → θ" : "area → θ")})";
            }
            if (want is float w && D(r.Yaw, w) > 0.5f)
                result.Add(r with { Verdict = "FAIL", Why = (r.Why.Length > 0 ? r.Why + "; " : "") + $"P1: yaw {r.Yaw:0.#}, {what} = {w:0.#}" });
            else
                result.Add(want != null ? r with { Label = r.Label + (r.Label.Length > 0 ? " " : "") + $"[P1 ok: {what}]" } : r);
        }
        return result;
    }

    static float F(PARAM.Row r, string field) => Convert.ToSingle(r[field].Value);

    static PARAM Raw(string path)
    {
        var p = PARAM.Read(path);
        var blob = new PARAMDEF { ParamType = p.ParamType, DataVersion = p.ParamdefDataVersion, Unicode = true, FormatVersion = 104, BigEndian = false };
        blob.Fields.Add(new PARAMDEF.Field(blob, PARAMDEF.DefType.dummy8, "data") { ArrayLength = (int)p.DetectedSize });
        p.ApplyParamdef(blob);
        return p;
    }

    /// <summary>The verdict for one point (see the class summary).</summary>
    public static Row Judge(FloorProbe probe, string kind, int id, string label, Vector3 pos, float yaw, bool character)
    {
        // every root, sitters' too (rule P2: the played idle's feet are at the root), stands on the floor under it
        var hits = probe.Down(pos.X, pos.Z).Where(h => h.NormalY >= 0.5f).ToList();
        hits.Sort((a, b) => b.Y.CompareTo(a.Y));
        var floor = hits.FirstOrDefault(h => h.Y <= pos.Y + 0.05f);
        bool hasFloor = hits.Any(h => h.Y <= pos.Y + 0.05f);
        var overs = hits.Where(h => h.Y > pos.Y + 0.05f).ToList();
        float? above = overs.Count > 0 ? overs.Min(h => h.Y) : null;
        float? fy = hasFloor ? floor.Y : null;
        float gap = hasFloor ? pos.Y - floor.Y : float.NaN;
        string verdict, why = "";
        if (above is float a && a - pos.Y <= 1.0f && (!hasFloor || gap > 1.0f)) { verdict = "FAIL"; why = $"UNDER a floor {a - pos.Y:0.00} m over it (falls through)"; }
        else if (!hasFloor) { verdict = "FAIL"; why = "no floor under it (falls out of the world)"; }
        else if (character)
        {
            if (MathF.Abs(gap) <= 0.15f) verdict = "OK";
            else if (gap > 0.15f) { verdict = "WARN"; why = $"floats {gap:0.00} m over the floor"; }
            else { verdict = "WARN"; why = $"sunk {-gap:0.00} m"; }
        }
        else
        {
            // DS1's own points (start point, landing, warp regions) lie on the floor (gap 0.00-0.02: the bonfire spot and the
            // crow landing work in game); our test spots go 0.3-0.5 m over it
            if (gap > 2.0f) { verdict = "FAIL"; why = $"drop {gap:0.0} m"; }
            else if (gap >= -0.02f && gap <= 0.8f) verdict = "OK";
            else { verdict = "WARN"; why = gap < -0.02f ? $"{-gap:0.00} m under the floor" : $"drop {gap:0.00} m"; }
            // an edge, step or wall within 0.5 m (Firelink's graveyard spot was 0.5-1 m from the path's edge: run s9-1, MP
            // fell out of the world)
            if (verdict == "OK" && !Ds1Placement.Patch(probe, pos with { Y = floor.Y }))
            { verdict = "WARN"; why = "edge, step or wall within 0.5 m (no walkable patch)"; }
        }
        return new Row(kind, id, label, pos, yaw, fy, above, hasFloor ? floor.Part : "", verdict, why);
    }

    /// <summary>`spot-check MAP --probe X Y Z [--pack DIR]`: what is around a point, to pick a spot: every DS2 collision
    /// surface under it (pack) and every DS1 one (DSR, all hit filters), then the walkable floor (vs Y) at 0.5–4 m in 24
    /// compass directions and the wall distance at Y+1 m.</summary>
    static int Probe(string gamedata, string map, Vector3 p)
    {
        var probe = FloorProbe.FromDs2Map(f => File.Exists(Path.Combine(gamedata, f)) ? File.ReadAllBytes(Path.Combine(gamedata, f)) : null, map);
        Console.WriteLine($"{map} probe {p}: DS2 (pack) surfaces under it:");
        foreach (var h in probe.Down(p.X, p.Z).Where(h => MathF.Abs(h.Y - p.Y) < 15)) Console.WriteLine($"   y {h.Y,9:0.000} ({h.Y - p.Y:+0.00;-0.00}) n {h.NormalY:0.00} {h.Part}");
        string? ds1Map = Ds1MapOf(map);
        if (ds1Map != null)
        {
            var d1 = FloorProbe.FromDs1(Program.DefaultDsrRoot, ds1Map);
            Console.WriteLine($"  DS1 {ds1Map} collision under it (all filters):");
            foreach (var h in d1.Down(p.X, p.Z).Where(h => MathF.Abs(h.Y - p.Y) < 15)) Console.WriteLine($"   y {h.Y,9:0.000} ({h.Y - p.Y:+0.00;-0.00}) n {h.NormalY:0.00} {h.Part}");
        }
        Console.WriteLine("  DS2 walkable floor vs Y (≤ Y+1 m) at 0.5/1/1.5/2/3/4 m, wall distance at Y+1 m (≤ 4 m):");
        for (int a = -180; a < 180; a += 15)
        {
            var dir = PlaceSurvey.Dir(a);
            var floors = new[] { 0.5f, 1, 1.5f, 2, 3, 4 }.Select(r => probe.Floor(p + dir * r, 1.0f, 30) is { } f ? (f.Y - p.Y).ToString("+0.00;-0.00") : " void").ToList();
            float? w = probe.Ray(p + new Vector3(0, 1, 0), dir, 4);
            Console.WriteLine($"   {a,4}: {string.Join(" ", floors.Select(s => s.PadLeft(6)))} | wall {(w is float x ? x.ToString("0.00") : "-")}");
        }
        return 0;
    }

    /// <summary>DS2's own generators of a vanilla map over DS2's own collision: the gap distribution (DS2's height convention).</summary>
    static int Vanilla(string map)
    {
        string data = ExtractDs2.DefaultOut;
        var rows = Check(data, map);
        var gens = rows.Where(r => r.Kind == "gen" && r.Floor != null).ToList();
        foreach (var r in rows.Where(r => r.Kind == "gen"))
            Console.WriteLine($"  gen {r.Id,7} ({r.Pos.X,8:0.00}, {r.Pos.Y,8:0.00}, {r.Pos.Z,8:0.00}) {(r.Floor is float f ? $"gap {r.Pos.Y - f:+0.000;-0.000}" : "no floor")} over {(r.Above is float a ? (a - r.Pos.Y).ToString("+0.00") : "-")} {r.Label}");
        var gaps = gens.Select(r => r.Pos.Y - r.Floor!.Value).OrderBy(g => g).ToList();
        if (gaps.Count > 0)
            Console.WriteLine($"{map}: {rows.Count(r => r.Kind == "gen")} generators, {gaps.Count} over a floor: gap min {gaps[0]:0.000} median {gaps[gaps.Count / 2]:0.000} max {gaps[^1]:0.000}; |gap| ≤ 0.05: {gaps.Count(g => MathF.Abs(g) <= 0.05f)}, ≤ 0.15: {gaps.Count(g => MathF.Abs(g) <= 0.15f)}, > 0.15: {gaps.Count(g => g > 0.15f)}, < -0.15: {gaps.Count(g => g < -0.15f)}");
        return 0;
    }
}
