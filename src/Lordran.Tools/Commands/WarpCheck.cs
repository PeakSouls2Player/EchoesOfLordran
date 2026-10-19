using System.Globalization;
using System.Numerics;
using Lordran.Tools.Build.Events;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (fix11, user bug "Echo → Asylum: pose, then back to the last DS2 bonfire"): warp / spawn checks of a built pack.
/// <c>warp-check PACK [--scripts]</c>
/// <list type="number">
/// <item><b>WT1 warp targets</b>: every cross-map warp of every event / talk ESD of the pack (132125(demo, demo, map, point, 0))
/// and every SpEffect script warp (100130:0(1, map, point): the Echo of Lordran) names a map registered in WorldMapList and an
/// EventLocation row of that map (a missing row = DS2 falls back to the last bonfire).</item>
/// <item><b>WT2 spawn boxes</b>: no event box region of a Lordran map that an event registers (140132) contains a player
/// spawn point of that map (warp targets = EventLocation point rows that a warp names, bonfire spots) unless DS1's own region
/// (same id = 3701000 + DS1 entity % 1000) did — a box over a spawn fires its event the moment the player arrives.</item>
/// </list>
/// --scripts also lists every 100130:0 instruction of the regulation's SpEffect scripts (diagnostic).
/// </summary>
public static class WarpCheck
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: warp-check PACK [--scripts]"); return 1; }
        string pack = args[1];
        int pi = Array.IndexOf(args, "--probe"); // diagnostic: warp-check PACK --probe MAP X,Z = every collision surface over that spot
        if (pi > 0) { var xz = args[pi + 2].Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray(); string gdp = Path.Combine(pack, "gamedata"); var pr = Build.Maps.FloorProbe.FromDs2Map(f => File.Exists(Path.Combine(gdp, f)) ? File.ReadAllBytes(Path.Combine(gdp, f)) : null, args[pi + 1]); foreach (var h in pr.Down(xz[0], xz[1])) Console.WriteLine($"  Y {h.Y:0.000} normalY {h.NormalY:0.00} part {h.Part}"); return 0; }
        int oi = Array.IndexOf(args, "--esd-order"); // diagnostic: warp-check DIR --esd-order = each event ESD's group ids in FILE order
        if (oi > 0)
        {
            foreach (var f in Directory.GetFiles(args[1], "event_*.esd", SearchOption.AllDirectories).OrderBy(x => x))
            {
                var ids = ESD.Read(f).StateGroups.Keys.ToList();
                bool sorted = ids.SequenceEqual(ids.Order());
                Console.WriteLine($"{Path.GetFileName(f)}: {ids.Count} groups, file order {(sorted ? "sorted" : "NOT sorted: " + string.Join(",", ids))}");
            }
            return 0;
        }
        int di = Array.IndexOf(args, "--diff-scripts"); // diagnostic: warp-check PACK --diff-scripts OLDPACK = regulation EMEVD events added / changed
        if (di > 0)
        {
            static Dictionary<string, EMEVD> Scripts(string p) => BND4.Read(Formats.Ds2Regulation.Decrypt(Path.Combine(p, "title", "enc_regulation.bnd.dcx"))).Files
                .Where(x => x.Name.EndsWith(".emevd", StringComparison.OrdinalIgnoreCase)).ToDictionary(x => Path.GetFileNameWithoutExtension(x.Name), x => EMEVD.Read(x.Bytes), StringComparer.OrdinalIgnoreCase);
            static string Ins(EMEVD.Instruction x) => $"{x.Bank}:{x.ID}[{Convert.ToHexString(x.ArgData)}]";
            var a = Scripts(args[di + 1]); var b = Scripts(pack);
            foreach (var (name, nb) in b.OrderBy(k => k.Key))
            {
                var oa = a.TryGetValue(name, out var s) ? s.Events.ToDictionary(e => e.ID) : [];
                int added = 0, changed = 0;
                foreach (var e in nb.Events)
                {
                    string now = string.Join(" ", e.Instructions.Select(Ins));
                    bool isNew = !oa.TryGetValue(e.ID, out var old);
                    if (!isNew && string.Join(" ", old!.Instructions.Select(Ins)) == now) continue;
                    if (isNew) added++; else changed++;
                    if (args.Contains("--all") || e.Instructions.Any(x => x.Bank == 100130) || !isNew)
                        Console.WriteLine($"  {name} event {e.ID} {(isNew ? "ADDED" : "CHANGED")}: {now}{(isNew ? "" : $"   (was {string.Join(" ", old!.Instructions.Select(Ins))})")}");
                }
                Console.WriteLine($"{name}: {nb.Events.Count} events, {added} added, {changed} changed, {oa.Keys.Count(k => !nb.Events.Any(e => e.ID == k))} removed");
            }
            return 0;
        }
        string gd = Path.Combine(pack, "gamedata");
        int fails = 0, checkedWarps = 0;
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();

        var listed = File.ReadAllLines(Path.Combine(gd, "map", "worldmaplist", "worldmaplist.list"))
            .Select(l => l.Split('\t', ' ')[0].Trim()).Where(l => l.StartsWith('m')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var locCache = new Dictionary<string, Dictionary<int, (Vector3 Pos, float Yaw, Vector3 Size, bool Box)>?>(StringComparer.OrdinalIgnoreCase);
        Dictionary<int, (Vector3 Pos, float Yaw, Vector3 Size, bool Box)>? Locations(string map)
        {
            if (locCache.TryGetValue(map, out var c)) return c;
            string f = Path.Combine(gd, "param", $"eventlocation_{map}.param");
            if (!File.Exists(f)) f = Path.Combine(ExtractDs2.DefaultOut, "param", $"eventlocation_{map}.param"); // vanilla map: DS2's own
            Dictionary<int, (Vector3, float, Vector3, bool)>? d = null;
            if (File.Exists(f) && !Formats.ParamFiles.Rowless(f))
            {
                var p = PARAM.Read(f);
                if (p.ApplyParamdefCarefully(defs))
                {
                    float F(PARAM.Row r, string n) => Convert.ToSingle(r[n].Value, CultureInfo.InvariantCulture);
                    d = p.Rows.ToDictionary(r => r.ID, r => (new Vector3(F(r, "PositionX"), F(r, "PositionY"), F(r, "PositionZ")), F(r, "RotationY"),
                        new Vector3(F(r, "ScaleX"), F(r, "ScaleY"), F(r, "ScaleZ")), Convert.ToInt32(r["formType"].Value) != 1));
                }
            }
            else if (File.Exists(f)) d = [];
            locCache[map] = d;
            return d;
        }
        static string MapName(int dec) => $"m{dec / 1000000:D2}_{dec / 10000 % 100:D2}_{dec / 100 % 100:D2}_{dec % 100:D2}";
        void Target(string where, int mapDec, int point)
        {
            checkedWarps++;
            string map = MapName(mapDec);
            var loc = Locations(map);
            string? why = !listed.Contains(map) ? $"{map} is not in WorldMapList"
                : loc == null ? $"{map} has no EventLocation param"
                : !loc.TryGetValue(point, out var row) ? $"{map} has no EventLocation row {point}"
                : row.Box ? $"{map} EventLocation {point} is a box, not a point" : null;
            if (why != null) { fails++; Console.WriteLine($"  FAIL WT1 {where}: warp to {mapDec} / {point}: {why} (DS2 falls back to the last bonfire)"); }
            else Console.WriteLine($"  ok   WT1 {where}: warp to {map} / {point} at ({loc![point].Pos.X:0.##}, {loc[point].Pos.Y:0.##}, {loc[point].Pos.Z:0.##})");
        }

        // ---- WT1: ESD warps
        var spawns = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase); // map → warp-target points
        void AddSpawn(int mapDec, int point) { var m = MapName(mapDec); if (!spawns.TryGetValue(m, out var s)) spawns[m] = s = []; s.Add(point); }
        var esds = Directory.GetFiles(Path.Combine(gd, "ezstate"), "*.esd").Where(f => Path.GetFileName(f).StartsWith("event_", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(f).StartsWith("talk_", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f).ToList();
        var registers = new Dictionary<string, Dictionary<int, List<long>>>(StringComparer.OrdinalIgnoreCase); // map → region → groups
        foreach (var f in esds)
        {
            ESD esd;
            try { esd = ESD.Read(f); } catch { continue; }
            string name = Path.GetFileNameWithoutExtension(f), map = name[(name.IndexOf('_') + 1)..];
            foreach (var (gid, g) in esd.StateGroups)
                foreach (var (sid, s) in g)
                    foreach (var c in s.EntryCommands.Concat(s.ExitCommands).Concat(s.WhileCommands).Concat(s.Conditions.SelectMany(AllPass)))
                    {
                        if (c.CommandBank != 1) continue;
                        if (c.CommandID == 132125 && c.Arguments.Count >= 4 && Const(c.Arguments[2]) is int mapDec && Const(c.Arguments[3]) is int point)
                        { Target($"{name} group {gid} state {sid} 132125", mapDec, point); AddSpawn(mapDec, point); }
                        if (c.CommandID == 140132 && c.Arguments.Count >= 2 && Const(c.Arguments[1]) is int region && name.StartsWith("event_"))
                        {
                            if (!registers.TryGetValue(map, out var r)) registers[map] = r = [];
                            if (!r.TryGetValue(region, out var gl)) r[region] = gl = [];
                            if (!gl.Contains(gid)) gl.Add(gid);
                        }
                    }
        }

        // ---- WT1: SpEffect script warps (100130:0 mode 1 = map + EventLocation; mode 0 = last bonfire)
        var reg = BND4.Read(Formats.Ds2Regulation.Decrypt(Path.Combine(pack, "title", "enc_regulation.bnd.dcx")));
        bool scripts = args.Contains("--scripts");
        foreach (var bf in reg.Files.Where(x => x.Name.EndsWith(".emevd", StringComparison.OrdinalIgnoreCase)))
        {
            EMEVD evd;
            try { evd = EMEVD.Read(bf.Bytes); } catch { continue; }
            string sn = Path.GetFileNameWithoutExtension(bf.Name);
            foreach (var ev in evd.Events)
                foreach (var ins in ev.Instructions.Where(i => i.Bank == 100130 && i.ID == 0 && i.ArgData.Length >= 12))
                {
                    int mode = BitConverter.ToInt32(ins.ArgData, 0), m = BitConverter.ToInt32(ins.ArgData, 4), pt = BitConverter.ToInt32(ins.ArgData, 8);
                    if (scripts) Console.WriteLine($"  script {sn} event {ev.ID}: 100130:0({mode}, {m}, {pt})");
                    if (scripts && mode == 0 && sn.Contains("Condition"))
                        foreach (var x in ev.Instructions) Console.WriteLine($"      {x.Bank}:{x.ID} [{string.Join(" ", Enumerable.Range(0, x.ArgData.Length / 4).Select(k => BitConverter.ToInt32(x.ArgData, k * 4)))}] f[{string.Join(" ", Enumerable.Range(0, x.ArgData.Length / 4).Select(k => BitConverter.ToSingle(x.ArgData, k * 4).ToString("0.###", CultureInfo.InvariantCulture)))}]");
                    if (mode == 1) { Target($"{sn} event {ev.ID} 100130:0", m, pt); AddSpawn(m, pt); }
                }
        }

        // ---- WT2: event boxes over spawn points (Lordran maps = the maps whose event ESD registers 37xxxxx regions)
        foreach (var (map, regs) in registers.OrderBy(k => k.Key))
        {
            var loc = Locations(map);
            if (loc == null) continue;
            var points = new List<(string What, Vector3 Pos)>();
            if (spawns.TryGetValue(map, out var sp))
                foreach (int p in sp) if (loc.TryGetValue(p, out var row)) points.Add(($"warp target {p}", row.Pos));
            // the entry / Echo / test spots (3700000-3700099) and the spots in front of each bonfire (MapScript.BonfireSpot + 10·i)
            foreach (var (id, row) in loc.Where(kv => !kv.Value.Box && ((kv.Key >= 3700000 && kv.Key < 3700100)
                         || (kv.Key >= Build.Maps.MapScript.BonfireSpot && kv.Key < Build.Maps.MapSeams.SeamSpot && kv.Key % 10 == 0))))
                if (!points.Any(p => p.What == $"warp target {id}")) points.Add(($"spawn spot {id}", row.Pos));
            if (points.Count == 0) continue;
            foreach (var (region, groups) in regs.OrderBy(k => k.Key))
            {
                if (!loc.TryGetValue(region, out var box) || !box.Box) continue;
                foreach (var (what, pos) in points)
                    if (Inside(box.Pos, box.Yaw, box.Size, pos))
                    {
                        bool ds1 = Ds1RegionContains(map, region, box.Pos, pos);
                        if (ds1) Console.WriteLine($"  ok   WT2 {map}: region {region} (groups {string.Join(",", groups)}) contains {what} — DS1's own region does too");
                        else { fails++; Console.WriteLine($"  FAIL WT2 {map}: region {region} (groups {string.Join(",", groups)}) box at ({box.Pos.X:0.##}, {box.Pos.Y:0.##}, {box.Pos.Z:0.##}) size ({box.Size.X:0.##}, {box.Size.Y:0.##}, {box.Size.Z:0.##}) contains {what} ({pos.X:0.##}, {pos.Y:0.##}, {pos.Z:0.##})"); }
                    }
            }
        }
        // ---- WT3 (informational, WARN): EventLocation ids an event of one Lordran map registers or warps to that another
        // Lordran map's EventLocation also has. Not a FAIL: vanilla DS2 reuses ids across maps freely (609 ids shared by 2+
        // vanilla maps, among them 3700000, 3701000, 3702000, 1000000 and the 1001.. invasion points; fix11 survey of
        // data\ds2\param\eventlocation_*), so DS2 resolves them per map; listed for the in-game bounce investigation.
        var lordranLocs = registers.Keys.Concat(spawns.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(m => (Map: m, Loc: Locations(m))).Where(x => x.Loc != null && File.Exists(Path.Combine(gd, "param", $"eventlocation_{x.Map}.param"))).ToList();
        int wt3 = 0;
        foreach (var (map, regs) in registers.OrderBy(k => k.Key))
            foreach (int id in regs.Keys.Concat(spawns.GetValueOrDefault(map) ?? []).Distinct().Order())
            {
                var others = lordranLocs.Where(o => !o.Map.Equals(map, StringComparison.OrdinalIgnoreCase) && o.Loc!.ContainsKey(id)).Select(o => o.Map).ToList();
                if (others.Count == 0) continue;
                wt3++;
                Console.WriteLine($"  WARN WT3 {map}: event location {id} (registered / warp target here) also exists in {string.Join(", ", others)}");
            }
        Console.WriteLine($"  WT3: {wt3} event-relevant location id(s) shared with another Lordran map (informational; vanilla shares ids per map)");
        // ---- BF1-BF4: every Lordran bonfire (MapObjectBonfireParam rows the pack adds) follows vanilla FoFG's pattern:
        // BF1 the row's mapId is a WorldMapList map and its fields equal a vanilla row's except the per-bonfire ones;
        // BF2 the map's MapObjectInstanceParam has the bonfire object row (objCtrlId = bonfire id, objCtrlType 7, objType 100);
        // BF3 an MSB object part of the map uses that instance row; BF4 an EventParam row (bootType 4, bonfireId) whose ESD
        // group exists (the rest / flag-reset event)
        var regParams = Formats.ParamStore.LoadDs2(Path.Combine(pack, "title"), ExtractDs2.DefaultOut);
        var vanParams = Formats.ParamStore.LoadDs2(Path.Combine(Program.DefaultDs2Root), ExtractDs2.DefaultOut);
        var bonP = regParams.Params["MapObjectBonfireParam"];
        var vanIds = vanParams.Params["MapObjectBonfireParam"].Rows.Select(r => r.ID).ToHashSet();
        var tmpl = vanParams.Params["MapObjectBonfireParam"][2650]; // Things Betwixt: every Lordran row's template
        string[] perBonfire = ["mapId", "menuAreaID", "eventCameraIdBegin", "menuSortID", "multiPlayAreaid", "isUseDisableNetworkItem"];
        int bonfiresChecked = 0;
        foreach (var row in bonP.Rows.Where(r => !vanIds.Contains(r.ID)).OrderBy(r => r.ID))
        {
            bonfiresChecked++;
            int id = row.ID, mapDec = Convert.ToInt32(row["mapId"].Value);
            string map = $"m{mapDec / 1000000:D2}_{mapDec / 10000 % 100:D2}_{mapDec / 100 % 100:D2}_{mapDec % 100:D2}";
            var bad = new List<string>();
            if (!listed.Contains(map)) bad.Add($"BF1 map {map} not in WorldMapList");
            if (tmpl != null)
                foreach (var c in row.Cells.Where(c => !perBonfire.Contains(c.Def.InternalName) && !c.Def.InternalName.StartsWith("pad", StringComparison.OrdinalIgnoreCase)))
                    if (!Equals(c.Value?.ToString(), tmpl[c.Def.InternalName].Value?.ToString())) bad.Add($"BF1 {c.Def.InternalName}={c.Value} (vanilla 2650: {tmpl[c.Def.InternalName].Value})");
            string instPath = Path.Combine(gd, "param", $"mapobjectinstanceparam_{map}.param");
            int instance = -1;
            if (File.Exists(instPath))
            {
                var ip = PARAM.Read(instPath);
                if (ip.ApplyParamdefCarefully(defs))
                {
                    var ir = ip.Rows.FirstOrDefault(r => Convert.ToInt32(r["objCtrlId"].Value) == id && Convert.ToInt32(r["objCtrlType"].Value) == 7);
                    if (ir == null) bad.Add("BF2 no MapObjectInstanceParam row with objCtrlId = the bonfire, objCtrlType 7");
                    else { instance = ir.ID; if (Convert.ToInt32(ir["objType"].Value) != 100) bad.Add($"BF2 instance {ir.ID} objType {ir["objType"].Value} (vanilla 100)"); }
                }
            }
            else bad.Add($"BF2 no mapobjectinstanceparam_{map}");
            string msbPath = Path.Combine(gd, "map", map, map + ".msb");
            if (instance > 0 && File.Exists(msbPath) && !MSB2.Read(msbPath).Parts.Objects.Any(o => o.MapObjectInstanceParamID == instance)) bad.Add($"BF3 no MSB object uses instance {instance}");
            string evPath = Path.Combine(gd, "param", $"eventparam_{map}.param"), esdPath = Path.Combine(gd, "ezstate", $"event_{map}.esd");
            if (File.Exists(evPath) && File.Exists(esdPath))
            {
                var ep = PARAM.Read(evPath);
                var groups = ESD.Read(esdPath).StateGroups;
                var er = ep.ApplyParamdefCarefully(defs) ? ep.Rows.FirstOrDefault(r => Convert.ToInt32(r["bonfireId"].Value) == id) : null;
                if (er == null) bad.Add("BF4 no EventParam row with bonfireId");
                else if (Convert.ToInt32(er["bootType"].Value) != 4 || !groups.ContainsKey(er.ID)) bad.Add($"BF4 EventParam {er.ID}: bootType {er["bootType"].Value} (vanilla 4), ESD group {(groups.ContainsKey(er.ID) ? "present" : "MISSING")}");
            }
            else bad.Add("BF4 no event ESD / EventParam");
            if (bad.Count > 0) { fails++; Console.WriteLine($"  FAIL BF  bonfire {id} ({map}): {string.Join("; ", bad)}"); }
            else Console.WriteLine($"  ok   BF  bonfire {id} ({map}): row like vanilla 2650, object instance {instance}, rest event bootType 4");
        }
        Console.WriteLine($"warp-check: {checkedWarps} warp(s), {spawns.Values.Sum(s => s.Count)} warp target(s), {bonfiresChecked} Lordran bonfire(s), {fails} FAIL");
        return fails == 0 ? 0 : 1;
    }

    static IEnumerable<ESD.CommandCall> AllPass(ESD.Condition c) => c.PassCommands.Concat(c.Subconditions.SelectMany(AllPass));

    static int? Const(byte[] b)
    {
        try { double v = EventSim.Evaluate(b, (_, _) => throw new InvalidOperationException()); return (int)v; } catch { return null; }
    }

    /// <summary>The sim's box test (EventSim.Inside): centred X/Z half-extents, Y from -0.5 to size + 0.5.</summary>
    static bool Inside(Vector3 c, float yaw, Vector3 size, Vector3 p)
    {
        var d = p - c;
        float a = -yaw * MathF.PI / 180f;
        float x = d.X * MathF.Cos(a) - d.Z * MathF.Sin(a), z = d.X * MathF.Sin(a) + d.Z * MathF.Cos(a);
        return MathF.Abs(x) <= size.X / 2 + 1e-3f && MathF.Abs(z) <= size.Z / 2 + 1e-3f && d.Y >= -0.5f && d.Y <= size.Y + 0.5f;
    }

    /// <summary>DS1 region of the same id (AsylumScript.RegionId 3701000 + entity % 1000; Ds1MapContext 3710000 / 3720000 + entity % 10000) in the DS1 map the host is
    /// built from: does it contain the point (DS1 box: centre X/Z, base Y)? Unknown map / region → false.</summary>
    static bool Ds1RegionContains(string ds2Map, int region, Vector3 ds2Centre, Vector3 p)
    {
        if (Build.Maps.MapHosts.Ds1Of(ds2Map) is not { } ds1Map) return false;
        string f = Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", ds1Map + ".msb");
        if (!File.Exists(f)) return false;
        var msb = MSB1.Read(f);
        foreach (var r in msb.Regions.GetEntries().Where(r => r.EntityID > 0 && (Build.Maps.AsylumScript.RegionId(r.EntityID) == region || Build.Events.Ds1MapContext.RegionId(r.EntityID) == region || Build.Events.Ds1MapContext.HitRegionId(r.EntityID) == region)))
            if (r.Shape is MSB.Shape.Box b && Inside(r.Position, r.Rotation.Y, new Vector3(b.Width, b.Height, b.Depth), p - (ds2Centre - r.Position))) return true; // host offset (H2) cancelled
        return false;
    }
}
