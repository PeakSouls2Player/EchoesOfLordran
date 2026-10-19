using System.Globalization;
using System.Numerics;
using Lordran.Tools.Build.Maps;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (walk11): <c>walk-check PACK MAP [--seams] [--max N] [--steps FILE] [--quiet]</c> — can the player walk
/// everywhere DS1's player walks, and does it look right? MAP = a DS1 map (m10_02_00_00) or its DS2 host (m19_42_00_00).
/// Prints FAIL / WARN lines with world coordinates, then a summary; exit 1 on any FAIL.
/// <list type="number">
/// <item><b>void (coverage)</b>: every DS1 navmesh face (DS1's "where the player walks") and every standable DS1
/// high-collision floor (filter 8 parts the map rules place, ≥ 1.6 m headroom) sampled every 0.5 m; the pack must have a
/// solid DS2 surface (map collision incl. step ramps, object collision; with --seams also the neighbour hosts) within
/// 0.15 m of DS1's floor height (a ramp up to 0.5 m above it counts). Failures are clustered per 2 m cell.</item>
/// <item><b>invisible walls</b>: every steep DS2 collision face (map + objects) inside the player's body band (0.3-1.7 m)
/// over a DS1 navmesh sample within 0.35 m, with no DS1 player-solid face within 0.2 m (map collision) or no rendered
/// geometry of the pack within 0.35 m (objects, map collision).</item>
/// <item><b>draw</b>: for each DS2 collision part, every map piece DS1 draws from its DS1 counterpart (piece DrawGroups ∩
/// collision DispGroups) must be drawn and loaded in DS2 (piece DrawGroups ∩ collision DrawGroups/DispGroups and ∩ the
/// collision's area mask Unk44..50; objects ∩ the area mask); connect masks must reach the neighbour's parts.</item>
/// <item><b>assets</b>: every collision part's model in the h archive (FAIL) and l archive (WARN); every piece model in
/// the map archive with an ACB entry; every texture a piece material references in this map's t archive (+ low_ copy)
/// or GI archive; MTDs that exist in DS2; the LQ (maplq) archives.</item>
/// </list>
/// </summary>
public static class WalkCheck
{
    const float Tol = 0.15f;

    sealed class Report
    {
        public int Fails, Warns;
        public readonly Dictionary<string, int> Counts = [];
        public readonly List<(string Kind, Vector3 P, string Text, int N)> Worst = [];
        readonly int max;
        readonly Dictionary<string, int> printed = [];
        public Report(int max) { this.max = max; }
        public void Fail(string kind, string text, Vector3? p = null, int n = 1)
        {
            Fails++; Counts[kind] = Counts.GetValueOrDefault(kind) + 1;
            if (p is { } q) Worst.Add((kind, q, text, n));
            if ((printed[kind] = printed.GetValueOrDefault(kind) + 1) <= max) Console.WriteLine($"FAIL {kind}: {text}");
            else if (printed[kind] == max + 1) Console.WriteLine($"FAIL {kind}: … (more not printed; --max N)");
        }
        public void Warn(string kind, string text)
        {
            Warns++; Counts["warn " + kind] = Counts.GetValueOrDefault("warn " + kind) + 1;
            if ((printed["w" + kind] = printed.GetValueOrDefault("w" + kind) + 1) <= max) Console.WriteLine($"WARN {kind}: {text}");
        }
    }

    static string V(Vector3 p) => string.Create(CultureInfo.InvariantCulture, $"({p.X:0.00}, {p.Y:0.00}, {p.Z:0.00})");

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: walk-check PACK MAP [--seams] [--max N] [--steps FILE]"); return 1; }
        string pack = args[1];
        string gamedata = Path.Combine(pack, "gamedata");
        // MAP = DS1 map or its host; --host H checks an older pack whose host differs (Firelink m19_02 before rule H1)
        string ds1Map = MapHosts.Ds1Of(args[2]) ?? (args[2] == "m19_02_00_00" ? "m10_02_00_00" : args[2]);
        string host = args.Contains("--host") ? args[Array.IndexOf(args, "--host") + 1]
            : Directory.Exists(Path.Combine(gamedata, "map", args[2])) ? args[2] : MapHosts.Of(ds1Map)?.Host ?? ds1Map;
        int max = args.Contains("--max") ? int.Parse(args[Array.IndexOf(args, "--max") + 1]) : 25;
        string? stepsOut = args.Contains("--steps") ? args[Array.IndexOf(args, "--steps") + 1] : null;
        var rep = new Report(max);
        string dsr = Program.DefaultDsrRoot;
        if (args.Contains("--route")) return Route(pack, host, args[Array.IndexOf(args, "--route") + 1], stepsOut, args.Contains("--seams"), args.Contains("--no-objects"));
        Console.WriteLine($"walk-check {pack} {ds1Map} → {host}");
        var msb1 = MSB1.Read(Path.Combine(dsr, "map", "MapStudio", ds1Map + ".msb"));
        var msb2 = MSB2.Read(Path.Combine(gamedata, "map", host, host + ".msb"));

        // ---- DS1 side: which filter-8 parts our map rules place (MapBuilder: not DisableStart, not switched off by event 0)
        var startDisabled = StartDisabled(dsr, ds1Map);
        bool PlacedByRule(MSB1.Part.Collision c) => c.HitFilterID == 8 && !c.DisableStart && !startDisabled.Contains(c.EntityID);
        var byName = msb1.Parts.Collisions.ToDictionary(c => c.Name);
        var ds1All8 = FloorProbe.FromDs1(dsr, ds1Map, c => c.HitFilterID == 8);
        var ds1Obj = Ds1ObjectCollision(dsr, msb1);
        var ownFloorTris = ds1All8.Triangles.Concat(ds1Obj.Triangles).ToList(); // the floors sampled (this map only)

        // ---- DS2 side: map collision (+ neighbours with --seams) + object collision
        var ds2Tris = new List<FloorProbe.Tri>(FloorProbe.FromFolder(pack, host).Triangles);
        if (args.Contains("--seams"))
            foreach (var k in msb2.Parts.ConnectCollisions.Select(k => $"m{k.MapID[0]:D2}_{k.MapID[1]:D2}_{k.MapID[2]:D2}_{k.MapID[3]:D2}").Distinct())
                if (k != host && File.Exists(Path.Combine(gamedata, "map", k, k + ".msb")))
                {
                    var nb = FloorProbe.FromFolder(pack, k).Triangles.Select(t => t with { Part = k + ":" + t.Part }).ToList();
                    ds2Tris.AddRange(nb);
                    Console.WriteLine($"  --seams: + {nb.Count} collision triangles of neighbour {k}");
                    // the neighbour's DS1 side too (its walls are DS1's walls, not ours)
                    if (MapHosts.Ds1Of(k) is { } nds1)
                    {
                        var nmsb = MSB1.Read(Path.Combine(dsr, "map", "MapStudio", nds1 + ".msb"));
                        ds1All8 = new FloorProbe(ds1All8.Triangles.Concat(FloorProbe.FromDs1(dsr, nds1, c => c.HitFilterID == 8).Triangles));
                        ds1Obj = new FloorProbe(ds1Obj.Triangles.Concat(Ds1ObjectCollision(dsr, nmsb).Triangles));
                    }
                }
        var objTris = ObjectCollision(gamedata, msb2, rep);
        ds2Tris.AddRange(objTris);
        var ds2 = new FloorProbe(ds2Tris);
        var ds1Nums = msb1.Models.Collisions.Select(m => m.Name.Substring(1, 4)).ToHashSet();
        foreach (var c in msb2.Parts.Collisions) if (!ds1Nums.Contains(c.ModelName.Substring(4, 4))) rampParts.Add(c.Name);
        Console.WriteLine($"  DS1: {ds1All8.Count} filter-8 triangles; DS2: {ds2.Count} triangles ({objTris.Count} object)");

        Console.WriteLine($"  DS1 objects: {ds1Obj.Count} collision triangles");
        Coverage(dsr, ds1Map, msb1, ds1All8, ds1Obj, ownFloorTris, byName, PlacedByRule, ds2, rep);
        // every DS1 object part with collision that DS1's events never touch must be a DS2 object part at its place
        // (rule OC1): a dropped one is a missing floor / wall (the Asylum balcony o8700-o8702)
        {
            var touched = MapBuilder.EventEntities(dsr, ds1Map);
            int missing = 0, objs = 0;
            foreach (var g in msb1.Parts.Objects.GroupBy(o => o.ModelName))
            {
                if (!HasDs1Collision(dsr, g.Key)) continue;
                foreach (var o in g)
                {
                    if (o.EntityID > 0 && touched.Contains(o.EntityID)) continue; // event-driven: the event rules decide
                    objs++;
                    bool there = msb2.Parts.Objects.Any(x => (x.ModelName.Split(' ')[0].EndsWith(g.Key[1..]) || x.ModelName.Length >= 3 && x.ModelName.Split(' ')[0] == Build.Maps.MapHosts.ObjectName(x.ModelName.Substring(1, 2), g.Key)) && Vector3.Distance(x.Position, o.Position) < 0.1f) // fx11: rule ON1 names
                        || msb2.Parts.MapPieces.Any(x => x.ModelName.Split(' ')[0].EndsWith(g.Key[1..]) && Vector3.Distance(x.Position, o.Position) < 0.1f);
                    if (there) continue;
                    missing++;
                    rep.Fail("object-missing", $"DS1 object {o.Name} ({g.Key}, entity {o.EntityID}) has collision but no DS2 object at {V(o.Position)}", o.Position, 10);
                }
            }
            Console.WriteLine($"[objects] {objs} DS1 object parts with collision (not event-driven): {missing} without a DS2 object");
        }
        var render = RenderVox(gamedata, host, msb2);
        Walls(dsr, ds1Map, msb1, ds1All8, ds1Obj, ds2, render, rep);
        Draw(msb1, msb2, gamedata, host, rep);
        // ---- structure: ramps not steeper than 45° (rule SR2), DS2 name forms (SR3), one model entry / one object per place (OD1)
        {
            int steep = 0;
            var steepCells = new Dictionary<(int, int, int), (int N, Vector3 P, string Part)>();
            foreach (var t in ds2.Triangles.Where(t => !t.Part.Contains(':') && IsRamp(t.Part)))
            {
                var n = Vector3.Cross(t.B - t.A, t.C - t.A);
                if (n.Length() < 1e-7f || MathF.Abs(Vector3.Normalize(n).Y) >= 0.69f) continue; // > 46° (SR2 allows 45°)
                steep++;
                var c = (t.A + t.B + t.C) / 3;
                var k = ((int)MathF.Floor(c.X / 2), (int)MathF.Floor(c.Y / 2), (int)MathF.Floor(c.Z / 2));
                steepCells[k] = steepCells.TryGetValue(k, out var e) ? (e.N + 1, e.P, e.Part) : (1, c, $"{t.Part} [{V(t.A)} {V(t.B)} {V(t.C)}]");
            }
            foreach (var (_, e) in steepCells.OrderByDescending(x => x.Value.N))
                rep.Fail("steep-ramp", $"{V(e.P)} {e.Part}: {e.N} step-ramp face(s) steeper than 45° (a wall on stairs, rule SR2)", e.P, e.N);
            foreach (var m in msb2.Models.GetEntries().GroupBy(m => m.Name).Where(g => g.Count() > 1))
                rep.Fail("duplicate-model", $"MSB model {m.Key} listed {m.Count()}× (rule OD1)");
            foreach (var m in msb2.Models.Collisions.Where(m => !System.Text.RegularExpressions.Regex.IsMatch(m.Name, @"^h\d\d_\d{4}$")))
                rep.Fail("model-name", $"collision model {m.Name} is not hAA_BBBB (rule SR3)");
            int dup = 0;
            // (DS1's own stacked copies — the hall floor o8510_0000/0001 for the collapse, the class corpses on one spot — are
            // the events' business: only more DS2 parts than DS1 has at the place count)
            foreach (var g in msb2.Parts.Objects.GroupBy(o => (o.ModelName, (int)MathF.Round(o.Position.X * 20), (int)MathF.Round(o.Position.Y * 20), (int)MathF.Round(o.Position.Z * 20))).Where(g => g.Count() > 1))
            {
                if (g.Key.ModelName == "o00_0240" || g.Key.ModelName[3..].StartsWith("_93") || g.Key.ModelName[3..].StartsWith("_91")) continue; // pickups (class corpses share a spot), key-guide helpers (no body; one per prompt)
                string suffix = g.Key.ModelName.Length >= 8 ? g.Key.ModelName[4..8] : g.Key.ModelName;
                int ds1Here = msb1.Parts.Objects.Count(o => o.ModelName.Length >= 5 && o.ModelName.Substring(1, 4) == suffix && Vector3.Distance(o.Position, g.First().Position) < 0.06f);
                if (g.Count() <= ds1Here) continue;
                dup++; rep.Fail("duplicate-object", $"{g.Count()} parts of {g.Key.ModelName} at {V(g.First().Position)}: {string.Join(", ", g.Select(o => o.Name))} (doubled collision, z-fighting; rule OD1)", g.First().Position, 5); }
            Console.WriteLine($"[structure] {steep} step-ramp faces steeper than 45°; {dup} doubled object placements");
        }
        Assets(pack, gamedata, host, msb2, rep);

        Console.WriteLine();
        foreach (var (k, n) in rep.Counts.OrderBy(x => x.Key)) Console.WriteLine($"  {k}: {n}");
        if (stepsOut != null)
        {
            var lines = rep.Worst.OrderByDescending(w => w.N).Take(12)
                .SelectMany(w => new[] { $"# {w.Kind}: {w.Text}", $"goto {w.P.X.ToString("0.00", CultureInfo.InvariantCulture)} {w.P.Z.ToString("0.00", CultureInfo.InvariantCulture)}", $"shot {w.Kind}-{(int)w.P.X}_{(int)w.P.Z}", "pos" }).ToList();
            File.WriteAllLines(stepsOut, lines);
            Console.WriteLine($"steps: {stepsOut} ({rep.Worst.Count} located failures, worst 12 written)");
        }
        Console.WriteLine($"walk-check {host}: {rep.Fails} FAIL, {rep.Warns} WARN");
        return rep.Fails > 0 ? 1 : 0;
    }

    /// <summary>Every 7-digit integer literal argument in the map's EMEVD (entities its events address), cheap superset.</summary>
    static HashSet<int> Events(string dsr, string ds1Map)
    {
        var set = new HashSet<int>();
        string path = Path.Combine(dsr, "event", ds1Map + ".emevd.dcx");
        if (!File.Exists(path)) return set;
        foreach (var ins in EMEVD.Read(path).Events.SelectMany(e => e.Instructions))
            for (int i = 0; i + 4 <= ins.ArgData.Length; i += 4)
            {
                int v = BitConverter.ToInt32(ins.ArgData, i);
                if (v is >= 1000000 and < 10000000) set.Add(v);
            }
        return set;
    }

    static readonly Dictionary<string, bool> ds1Col = new();
    public static bool HasDs1Collision(string dsr, string model)
    {
        if (ds1Col.TryGetValue(model, out bool b)) return b;
        string path = Path.Combine(dsr, "obj", model + ".objbnd.dcx");
        b = File.Exists(path) && BND3.Read(DCX.Decompress(File.ReadAllBytes(path))).Files.Any(f => Path.GetFileName(f.Name).Equals(model + ".hkx", StringComparison.OrdinalIgnoreCase));
        return ds1Col[model] = b;
    }

    /// <summary>--route "X,Y,Z X,Y,Z …" [--steps FILE]: a floor walk over the pack's DS2 collision (seam-check's A*: 0.5 m
    /// cells, ≤ 0.55 m up / 1.0 m down, no wall at body height, objects with collision avoided) through the waypoints;
    /// FAIL when a leg has no path; --steps writes ds2-steps "goto X Z" lines every ~3 m plus a "pos" per waypoint.</summary>
    static int Route(string pack, string host, string spec, string? stepsOut, bool seams, bool noObjects = false)
    {
        var ci = CultureInfo.InvariantCulture;
        var pts = spec.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => { var c = s.Split(','); return new Vector3(float.Parse(c[0], ci), float.Parse(c[1], ci), float.Parse(c[2], ci)); }).ToList();
        var maps = new List<string> { host };
        if (seams)
        {
            var msb = MSB2.Read(Path.Combine(pack, "gamedata", "map", host, host + ".msb"));
            foreach (var k in msb.Parts.ConnectCollisions.Select(k => $"m{k.MapID[0]:D2}_{k.MapID[1]:D2}_{k.MapID[2]:D2}_{k.MapID[3]:D2}").Distinct())
                if (!maps.Contains(k) && File.Exists(Path.Combine(pack, "gamedata", "map", k, k + ".msb"))) maps.Add(k);
        }
        // the objects' real collision (MOPP meshes; boxes for convex shapes) joins each map's probe: doors are thin slabs, not the
        // discs LaneCheck approximates (the Firelink tunnel door 1011320's disc closed the aqueduct passage beside it). White doors
        // (crossed with the action; FW1 walls start disabled) and key-guide helpers (no body) are left out.
        var probes = maps.ToDictionary(m => m, m =>
        {
            var tris = new List<FloorProbe.Tri>(FloorProbe.FromFolder(pack, m).Triangles);
            if (!noObjects)
            {
                var msb = MSB2.Read(Path.Combine(pack, "gamedata", "map", m, m + ".msb"));
                var keep = new MSB2();
                foreach (var o in msb.Parts.Objects.Where(o => !o.ModelName.StartsWith("o00_050") && !o.ModelName.Contains("_93") && !o.ModelName.Contains("_91"))) keep.Parts.Objects.Add(o);
                tris.AddRange(ObjectCollision(Path.Combine(pack, "gamedata"), keep, new Report(0)));
            }
            return new FloorProbe(tris);
        });
        var objs = new List<LaneCheck.Obj>();
        var lines = new List<string>();
        int fails = 0; float total = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            var path = SeamCheck.FloorWalk(maps.ToArray(), probes, pts[i - 1], pts[i], out int expanded, [], objs);
            if (path == null) { fails++; Console.WriteLine($"FAIL route: no floor walk {V(pts[i - 1])} → {V(pts[i])} ({expanded} cells)"); continue; }
            float len = 0; for (int k = 1; k < path.Count; k++) len += Vector3.Distance(path[k - 1].P, path[k].P);
            for (int k = 1; k < path.Count; k++)
                if (path[k - 1].P.Y - path[k].P.Y > 0.56f) Console.WriteLine($"  one-way drop {path[k - 1].P.Y - path[k].P.Y:0.00} m at {V(path[k].P)} ({path[k - 1].Part} → {path[k].Part}): the floor walk can't climb back here");
            total += len;
            Console.WriteLine($"route leg {i}: {V(pts[i - 1])} → {V(pts[i])}: {len:0} m, {string.Join(" → ", path.Select(p => p.Map + ":" + p.Part.Split('_')[0] + "_" + p.Part.Split('_')[1]).Distinct())}");
            for (int k = 6; k < path.Count; k += 6) lines.Add($"goto {path[k].P.X.ToString("0.00", ci)} {path[k].P.Z.ToString("0.00", ci)}");
            lines.Add($"goto {path[^1].P.X.ToString("0.00", ci)} {path[^1].P.Z.ToString("0.00", ci)}");
            lines.Add("pos");
        }
        if (stepsOut != null) { File.WriteAllLines(stepsOut, lines); Console.WriteLine($"steps: {stepsOut} ({lines.Count} lines)"); }
        Console.WriteLine($"walk-check route {host}: {pts.Count - 1} legs, {total:0} m, {fails} FAIL");
        return fails > 0 ? 1 : 0;
    }

    public static HashSet<int> StartDisabled(string dsr, string ds1Map)
    {
        var set = new HashSet<int>();
        string path = Path.Combine(dsr, "event", ds1Map + ".emevd.dcx");
        if (File.Exists(path))
            foreach (var ins in EMEVD.Read(path).Events.Where(e => e.ID == 0).SelectMany(e => e.Instructions))
                if (ins.Bank == 2011 && ins.ID == 1 && ins.ArgData.Length >= 5 && ins.ArgData[4] == 0) set.Add(BitConverter.ToInt32(ins.ArgData, 0));
        return set;
    }

    // ------------------------------------------------------------------ 1. void
    static void Coverage(string dsr, string ds1Map, MSB1 msb1, FloorProbe ds1, FloorProbe ds1Obj, List<FloorProbe.Tri> ownFloorTris, Dictionary<string, MSB1.Part.Collision> byName,
        Func<MSB1.Part.Collision, bool> placed, FloorProbe ds2, Report rep)
    {
        var clusters = new Dictionary<(int, int, int), (int N, Vector3 P, string Part, float Drop, string Src)>();
        int samples = 0, ok = 0, byDesign = 0, noDs1 = 0, objFloors = 0;
        var seen = new HashSet<(int, int, int)>();
        void Check(Vector3 p, string src)
        {
            // dedupe on a 0.4 m grid
            if (!seen.Add(((int)MathF.Floor(p.X / 0.4f), (int)MathF.Floor(p.Y / 0.4f), (int)MathF.Floor(p.Z / 0.4f)))) return;
            // DS1's floor under the sample: the highest walkable filter-8 surface within +0.5 / −1.0 m
            var f1 = ds1.Floor(p, above: 0.5f, below: 1.0f, minNormalY: 0.5f);
            var fObj = ds1Obj.Floor(p, above: 0.5f, below: 1.0f, minNormalY: 0.5f);
            if (fObj is { } fob && f1 is { } fm && fob.Y > fm.Y + 0.05f) f1 = null; // an object floor over the map floor wins
            if (f1 is not { } f)
            {
                // no DS1 map floor: a DS1 OBJECT floor (the hall floor o8510, lifts, bridges)? Then DS2 must have a floor
                // within the tolerance too. Navmesh over nothing at all in DS1 (nav anomalies) is not walkable in DS1 either.
                if (ds1Obj.Floor(p, above: 0.5f, below: 1.0f, minNormalY: 0.5f) is not { } fo) { noDs1++; return; }
                if (ds1.Down(p.X, p.Z).Concat(ds1Obj.Down(p.X, p.Z)).Any(h => h.Y > fo.Y + 0.05f && h.Y < fo.Y + 1.6f)) return;
                objFloors++; samples++;
                if (ds2.Down(p.X, p.Z).Any(h => MathF.Abs(h.Y - fo.Y) <= Tol)) { ok++; return; }
                var keyo = ((int)MathF.Floor(p.X / 2), (int)MathF.Floor(fo.Y / 2), (int)MathF.Floor(p.Z / 2));
                var dn = ds2.Down(p.X, p.Z).Where(h => h.Y < fo.Y && h.NormalY >= 0.5f).Select(h => fo.Y - h.Y).DefaultIfEmpty(float.PositiveInfinity).Min();
                var ato = new Vector3(p.X, fo.Y, p.Z);
                clusters[keyo] = clusters.TryGetValue(keyo, out var co) ? (co.N + 1, co.P, co.Part, MathF.Min(co.Drop, dn), co.Src) : (1, ato, "object " + fo.Part, dn, src);
                return;
            }
            var part = byName.GetValueOrDefault(PartOf(f.Part));
            if (part != null && !placed(part)) { byDesign++; return; }
            // headroom: DS1 collision above within 1.6 m = not standable (under stairs, inside walls)
            var hs = ds1.Down(p.X, p.Z).Concat(ds1Obj.Down(p.X, p.Z));
            if (hs.Any(h => h.Y > f.Y + 0.05f && h.Y < f.Y + 1.6f)) return;
            samples++;
            var downs = ds2.Down(p.X, p.Z);
            bool good = downs.Any(h => MathF.Abs(h.Y - f.Y) <= Tol)
                || downs.Any(h => h.Y > f.Y && h.Y <= f.Y + 0.5f && h.NormalY >= 0.5f && IsRamp(h.Part));
            if (good) { ok++; return; }
            var below = downs.Where(h => h.Y < f.Y && h.NormalY >= 0.5f).Select(h => f.Y - h.Y).DefaultIfEmpty(float.PositiveInfinity).Min();
            var key = ((int)MathF.Floor(f.Y > 0 ? p.X / 2 : p.X / 2), (int)MathF.Floor(f.Y / 2), (int)MathF.Floor(p.Z / 2));
            var at = new Vector3(p.X, f.Y, p.Z);
            clusters[key] = clusters.TryGetValue(key, out var c) ? (c.N + 1, c.P, c.Part, MathF.Min(c.Drop, below), c.Src) : (1, at, PartOf(f.Part), below, src);
        }
        // DS1 navmesh
        string nvmPath = Path.Combine(dsr, "map", ds1Map, ds1Map + ".nvmbnd.dcx");
        int navFaces = 0;
        if (File.Exists(nvmPath))
        {
            var bnd = BND3.Read(nvmPath);
            var nvms = bnd.Files.ToDictionary(f => Path.GetFileNameWithoutExtension(f.Name)[..7], f => NVM.Read(f.Bytes), StringComparer.OrdinalIgnoreCase);
            foreach (var part in msb1.Parts.Navmeshes)
            {
                if (!nvms.TryGetValue(part.ModelName, out var nvm)) continue;
                var m = ColSurvey.Place(part.Position, part.Rotation, part.Scale);
                foreach (var t in nvm.Triangles)
                {
                    if ((t.Flags & NotFloor) != 0) continue;
                    navFaces++;
                    var a = Vector3.Transform(nvm.Vertices[t.VertexIndex1], m); var b = Vector3.Transform(nvm.Vertices[t.VertexIndex2], m); var c = Vector3.Transform(nvm.Vertices[t.VertexIndex3], m);
                    var nn = Vector3.Cross(b - a, c - a);
                    if (nn.Length() < 1e-6f || MathF.Abs(Vector3.Normalize(nn).Y) < 0.6f) continue; // jump/drop links, not floor
                    foreach (var p in WalkProbe.Samples(a, b, c, 0.5f)) Check(p + new Vector3(0, 0.1f, 0), "navmesh");
                }
            }
        }
        // DS1 high collision floors (standable)
        foreach (var t in ownFloorTris)
        {
            var n = Vector3.Cross(t.B - t.A, t.C - t.A);
            if (n.Length() < 1e-6f || MathF.Abs(Vector3.Normalize(n).Y) < 0.7f) continue;
            foreach (var p in WalkProbe.Samples(t.A, t.B, t.C, 0.5f)) Check(p + new Vector3(0, 0.05f, 0), t.Part.StartsWith("obj:") ? "object floor" : "h floor");
        }
        Console.WriteLine($"[void] {navFaces} DS1 navmesh faces + DS1 floors: {samples} standable samples, {ok} on DS2 collision within {Tol} m, {samples - ok} not; {byDesign} on DS1 collision our rules leave out by design (DS1 enables it later), {objFloors} on DS1 object floors; {noDs1} navmesh samples over no DS1 collision at all (DS1 nav anomalies, ignored)");
        foreach (var (k, c) in clusters.OrderByDescending(c => c.Value.N))
        {
            string what = float.IsInfinity(c.Drop) ? "VOID (nothing below)" : c.Drop > 2 ? $"fall {c.Drop:0.0} m" : $"DS2 floor {c.Drop:0.00} m lower";
            if (c.N < 2 && !float.IsInfinity(c.Drop) && c.Drop < 0.5f) { rep.Warn("floor-offset", $"{V(c.P)} on DS1 {c.Part} ({c.Src}): {what}, {c.N} sample"); continue; }
            rep.Fail("void", $"{V(c.P)} on DS1 {c.Part} ({c.Src}, {c.N} samples in this 2 m cell): no DS2 floor within {Tol} m — {what}", c.P, c.N);
        }
    }

    /// <summary>DS1 navmesh faces that are not floor the player stands on (ladders, holes, walls, jump links, blocked gates).</summary>
    const NVM.TriangleFlags NotFloor = NVM.TriangleFlags.DEGENERATE | NVM.TriangleFlags.DISABLE | NVM.TriangleFlags.INSIDE_WALL | NVM.TriangleFlags.LADDER | NVM.TriangleFlags.HOLE
        | NVM.TriangleFlags.WALL | NVM.TriangleFlags.FLOOR_TO_WALL | NVM.TriangleFlags.BLOCK | NVM.TriangleFlags.BLOCK_GATE | NVM.TriangleFlags.CLOSED_DOOR | NVM.TriangleFlags.LANDING_POINT;

    static string PartOf(string tag) => tag.Split(' ')[0];

    /// <summary>Our step-ramp collision parts (rule SR3: any DS2 collision model whose number is no DS1 collision model of the map), filled by Run.</summary>
    static readonly HashSet<string> rampParts = new(StringComparer.OrdinalIgnoreCase);
    static bool IsRamp(string part) => rampParts.Contains(part.Contains(':') ? part[(part.IndexOf(':') + 1)..] : part);

    // ------------------------------------------------------------------ 2. invisible walls
    static void Walls(string dsr, string ds1Map, MSB1 msb1, FloorProbe ds1All8, FloorProbe ds1Obj, FloorProbe ds2, Vox render, Report rep)
    {
        // DS1 navmesh samples in a 0.5 m XZ hash
        var nav = new Dictionary<(int, int), List<float>>();
        string nvmPath = Path.Combine(dsr, "map", ds1Map, ds1Map + ".nvmbnd.dcx");
        if (File.Exists(nvmPath))
        {
            var nvms = BND3.Read(nvmPath).Files.ToDictionary(f => Path.GetFileNameWithoutExtension(f.Name)[..7], f => NVM.Read(f.Bytes), StringComparer.OrdinalIgnoreCase);
            foreach (var part in msb1.Parts.Navmeshes)
            {
                if (!nvms.TryGetValue(part.ModelName, out var nvm)) continue;
                var m = ColSurvey.Place(part.Position, part.Rotation, part.Scale);
                foreach (var t in nvm.Triangles)
                {
                    if ((t.Flags & NotFloor) != 0) continue;
                    foreach (var p in WalkProbe.Samples(Vector3.Transform(nvm.Vertices[t.VertexIndex1], m), Vector3.Transform(nvm.Vertices[t.VertexIndex2], m), Vector3.Transform(nvm.Vertices[t.VertexIndex3], m), 0.4f))
                    {
                        var k = ((int)MathF.Floor(p.X / 0.5f), (int)MathF.Floor(p.Z / 0.5f));
                        if (!nav.TryGetValue(k, out var l)) nav[k] = l = [];
                        l.Add(p.Y);
                    }
                }
            }
        }
        // DS1 player-solid faces as voxels
        var solid = new Vox(0.2f);
        foreach (var t in ds1All8.Triangles) foreach (var p in WalkProbe.Samples(t.A, t.B, t.C, 0.15f)) solid.Add(p);
        var solidObj = new Vox(0.2f); // DS1 object collision (its boxes and hulls as in DS1)
        foreach (var t in ds1Obj.Triangles) foreach (var p in WalkProbe.Samples(t.A, t.B, t.C, 0.15f)) solidObj.Add(p);
        var clusters = new Dictionary<(int, int, int), (int N, Vector3 P, string Part, string Why)>();
        int checkedTris = 0;
        foreach (var t in ds2.Triangles)
        {
            var n = Vector3.Cross(t.B - t.A, t.C - t.A);
            if (n.Length() < 1e-6f || MathF.Abs(Vector3.Normalize(n).Y) >= 0.5f) continue;
            bool obj = t.Part.StartsWith("obj");
            checkedTris++;
            foreach (var p in WalkProbe.Samples(t.A, t.B, t.C, 0.25f))
            {
                bool inBand = false;
                for (int dx = -1; dx <= 1 && !inBand; dx++) for (int dz = -1; dz <= 1 && !inBand; dz++)
                    if (nav.TryGetValue(((int)MathF.Floor(p.X / 0.5f) + dx, (int)MathF.Floor(p.Z / 0.5f) + dz), out var ys))
                        inBand = ys.Any(y => p.Y - y > 0.3f && p.Y - y < 1.7f);
                if (!inBand) continue;
                string? why = null;
                if (!obj && !solid.Near(p, 1)) why = "no DS1 player-solid face within 0.2 m";
                else if (!render.Near(p, 1)) why = !obj ? "DS1 wall with no rendered geometry within 0.35 m (DS1's own invisible wall?)" : solidObj.Near(p, 1) || solid.Near(p, 1) ? "DS1 object wall with no rendered geometry within 0.35 m (DS1 has the same collision)" : "object collision with no visible model within 0.35 m and no DS1 collision there";
                if (why == null) continue;
                bool ds1Own = why.StartsWith("DS1");
                var key = ((int)MathF.Floor(p.X / 2), (int)MathF.Floor(p.Y / 2), (int)MathF.Floor(p.Z / 2));
                var tag = (ds1Own ? "ds1:" : "") + t.Part;
                clusters[key] = clusters.TryGetValue(key, out var c) ? (c.N + 1, c.P, c.Part, c.Why) : (1, p, tag, why);
            }
        }
        int ds1OwnCells = clusters.Count(c => c.Value.Part.StartsWith("ds1:"));
        Console.WriteLine($"[walls] {checkedTris} steep DS2 faces checked against {nav.Count} DS1 navmesh cells: {clusters.Count - ds1OwnCells} suspicious 2 m cells, {ds1OwnCells} cells of DS1's own unrendered walls (DS1 has them too: WARN)");
        foreach (var (k, c) in clusters.OrderByDescending(c => c.Value.N))
        {
            if (c.Part.StartsWith("ds1:")) { if (c.N >= 4) rep.Warn("ds1-invisible-wall", $"{V(c.P)} {c.Part[4..]}: {c.Why} ({c.N} samples)"); continue; }
            if (c.N < 2) { rep.Warn("wall", $"{V(c.P)} {c.Part}: {c.Why} (1 sample)"); continue; }
            if (c.Part.StartsWith("objbox:")) { rep.Warn("object-box", $"{V(c.P)} {c.Part}: {c.Why} ({c.N} samples; convex shape approximated by its box, body rotation ignored: check in game if on the route)"); continue; }
            rep.Fail("invisible-wall", $"{V(c.P)} {c.Part}: {c.Why} ({c.N} samples in the player's band over DS1 navmesh)", c.P, c.N);
        }
    }

    public sealed class Vox
    {
        readonly HashSet<(int, int, int)> set = [];
        readonly float cell;
        public Vox(float cell) { this.cell = cell; }
        (int, int, int) K(Vector3 p) => ((int)MathF.Floor(p.X / cell), (int)MathF.Floor(p.Y / cell), (int)MathF.Floor(p.Z / cell));
        public void Add(Vector3 p) => set.Add(K(p));
        public int Count => set.Count;
        public bool Near(Vector3 p, int r)
        {
            var (x, y, z) = K(p);
            for (int dx = -r; dx <= r; dx++) for (int dy = -r; dy <= r; dy++) for (int dz = -r; dz <= r; dz++)
                if (set.Contains((x + dx, y + dy, z + dz))) return true;
            return false;
        }
    }

    /// <summary>The pack's rendered geometry of the map (pieces + objects) as 0.35 m voxels.</summary>
    static Vox RenderVox(string gamedata, string host, MSB2 msb)
    {
        var vox = new Vox(0.35f);
        string mdir = Path.Combine(gamedata, "model", "map");
        var bxf = BXF4.Read(Path.Combine(mdir, host + ".mapbhd"), Path.Combine(mdir, host + ".mapbdt"));
        var models = new Dictionary<string, List<(Vector3, Vector3, Vector3)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in bxf.Files.Where(f => f.Name.EndsWith(".flv.dcx", StringComparison.OrdinalIgnoreCase)))
        {
            string n = Path.GetFileName(f.Name); n = n[..n.IndexOf('.')];
            models[n] = FlverTris(FLVER2.Read(DCX.Decompress(f.Bytes)));
        }
        foreach (var p in msb.Parts.MapPieces)
            if (models.TryGetValue(p.ModelName, out var tris)) Mark(vox, tris, ColSurvey.Place(p.Position, p.Rotation, p.Scale));
        var objModels = new Dictionary<string, List<(Vector3, Vector3, Vector3)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in msb.Parts.Objects)
        {
            if (!objModels.TryGetValue(o.ModelName, out var tris))
            {
                tris = [];
                var bnd = ObjBnd(gamedata, o.ModelName);
                var flv = bnd?.Files.FirstOrDefault(f => Path.GetFileName(f.Name).Equals(o.ModelName + ".flv", StringComparison.OrdinalIgnoreCase));
                if (flv != null) try { tris = FlverTris(FLVER2.Read(flv.Bytes)); } catch { }
                objModels[o.ModelName] = tris;
            }
            Mark(vox, tris, ColSurvey.Place(o.Position, o.Rotation, o.Scale));
        }
        return vox;
    }

    static void Mark(Vox vox, List<(Vector3 A, Vector3 B, Vector3 C)> tris, Matrix4x4 m)
    {
        foreach (var (a, b, c) in tris)
        {
            var wa = Vector3.Transform(a, m); var wb = Vector3.Transform(b, m); var wc = Vector3.Transform(c, m);
            if (!Sane(wa) || !Sane(wb) || !Sane(wc) || Vector3.Distance(wa, wb) > 400 || Vector3.Distance(wa, wc) > 400) continue;
            foreach (var p in WalkProbe.Samples(wa, wb, wc, 0.3f)) vox.Add(p);
        }
    }

    static bool Sane(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z) && p.Length() < 1e5f;

    static List<(Vector3, Vector3, Vector3)> FlverTris(FLVER2 flv)
    {
        var list = new List<(Vector3, Vector3, Vector3)>();
        foreach (var mesh in flv.Meshes)
        {
            var fs = mesh.FaceSets.FirstOrDefault(f => f.Flags == FLVER2.FaceSet.FSFlags.None) ?? mesh.FaceSets.FirstOrDefault();
            if (fs == null) continue;
            var idx = fs.Triangulate(true);
            for (int i = 0; i + 2 < idx.Count; i += 3)
                if (idx[i] < mesh.Vertices.Count && idx[i + 1] < mesh.Vertices.Count && idx[i + 2] < mesh.Vertices.Count)
                    list.Add((mesh.Vertices[idx[i]].Position, mesh.Vertices[idx[i + 1]].Position, mesh.Vertices[idx[i + 2]].Position));
        }
        return list;
    }

    static BND4? ObjBnd(string gamedata, string model)
    {
        string bnd = Path.Combine(gamedata, "model", "obj", model + ".bnd");
        if (!File.Exists(bnd)) bnd = Path.Combine(ExtractDs2.DefaultOut, "model", "obj", model + ".bnd");
        if (!File.Exists(bnd)) return null;
        try { return BND4.Read(bnd); } catch { return null; }
    }

    /// <summary>Object parts' collision (MOPP mesh bodies of MODEL_h.hkx) placed with the part transform (bind pose).</summary>
    static List<FloorProbe.Tri> ObjectCollision(string gamedata, MSB2 msb, Report rep)
    {
        var cache = new Dictionary<string, (List<(Vector3, Vector3, Vector3)> Tris, int Mesh)>(StringComparer.OrdinalIgnoreCase);
        var result = new List<FloorProbe.Tri>();
        foreach (var o in msb.Parts.Objects)
        {
            if (!cache.TryGetValue(o.ModelName, out var shapes))
            {
                shapes = ([], 0);
                var bnd = ObjBnd(gamedata, o.ModelName);
                var h = bnd?.Files.FirstOrDefault(f => Path.GetFileName(f.Name).Equals(o.ModelName + "_h.hkx", StringComparison.OrdinalIgnoreCase));
                if (h != null) try { shapes = ObjectShapes(h.Bytes); } catch { }
                cache[o.ModelName] = shapes;
            }
            var m = ColSurvey.Place(o.Position, o.Rotation, o.Scale);
            for (int i = 0; i < shapes.Tris.Count; i++) { var (a, b, c) = shapes.Tris[i]; result.Add(new FloorProbe.Tri(Vector3.Transform(a, m), Vector3.Transform(b, m), Vector3.Transform(c, m), (i < shapes.Mesh ? "obj:" : "objbox:") + o.Name)); }
        }
        return result;
    }

    /// <summary>An object collision packfile's shapes as triangles in the object frame: MOPP meshes exactly; boxes, convex
    /// hulls, spheres, capsules as their AABB box (through list/transform/translate wrappers, translation only). Body
    /// transforms are ignored (object bodies sit at the bind pose).</summary>
    static (List<(Vector3, Vector3, Vector3)> Tris, int Mesh) ObjectShapes(byte[] packfile)
    {
        var tris = FloorProbe.Ds2Triangles(packfile); int meshCount = tris.Count;
        var root = new Formats.Havok.HkPackfile(packfile).ReadRoot();
        var sys = (Formats.Havok.HkPObj)((Formats.Havok.HkPObj)root.List("namedVariants")[0]!)["variant"]!;
        static Vector3 F(object? v) => v is float[] a && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.Zero;
        void Box(Vector3 c, Vector3 h)
        {
            var p = new Vector3[8];
            for (int i = 0; i < 8; i++) p[i] = c + new Vector3((i & 1) == 0 ? -h.X : h.X, (i & 2) == 0 ? -h.Y : h.Y, (i & 4) == 0 ? -h.Z : h.Z);
            int[][] faces = [[0, 1, 3, 2], [4, 6, 7, 5], [0, 4, 5, 1], [2, 3, 7, 6], [0, 2, 6, 4], [1, 5, 7, 3]];
            foreach (var f in faces) { tris.Add((p[f[0]], p[f[1]], p[f[2]])); tris.Add((p[f[0]], p[f[2]], p[f[3]])); }
        }
        object? Field(Formats.Havok.HkPObj s, string k) => s.Fields.GetValueOrDefault(k);
        void Walk(object? o, Vector3 off, int depth)
        {
            if (o is not Formats.Havok.HkPObj s || depth > 8) return;
            switch (s.Class)
            {
                case "hkpBoxShape": Box(off, F(Field(s, "halfExtents"))); break;
                case "hkpConvexVerticesShape": Box(off + F(Field(s, "aabbCenter")), F(Field(s, "aabbHalfExtents"))); break;
                case "hkpSphereShape": { float r = Convert.ToSingle(Field(s, "radius") ?? 0f); Box(off, new Vector3(r)); break; }
                case "hkpCapsuleShape": case "hkpCylinderShape":
                {
                    var a = F(Field(s, "vertexA")); var b = F(Field(s, "vertexB")); float r = Convert.ToSingle(Field(s, "radius") ?? 0f);
                    Box(off + (a + b) / 2, Vector3.Abs(a - b) / 2 + new Vector3(r)); break;
                }
                case "hkpListShape":
                    if (Field(s, "childInfo") is List<object?> infos) foreach (var ci in infos) Walk(ci is Formats.Havok.HkPObj c ? Field(c, "shape") : null, off, depth + 1);
                    break;
                case "hkpConvexTranslateShape": case "hkpConvexTransformShape": case "hkpTransformShape":
                {
                    var child = Field(s, "childShape");
                    if (child is Formats.Havok.HkPObj w && w.Fields.ContainsKey("childShape")) child = w.Fields["childShape"];
                    Walk(child, off + (s.Class == "hkpConvexTranslateShape" ? F(Field(s, "translation")) : Vector3.Zero), depth + 1); break;
                }
            }
        }
        foreach (Formats.Havok.HkPObj sy in sys.List("systems"))
            foreach (Formats.Havok.HkPObj rb in sy.List("rigidBodies"))
                try { if (rb.Get("collidable")["shape"] is Formats.Havok.HkPObj sh && sh.Class != "hkpMoppBvTreeShape") Walk(sh, Vector3.Zero, 0); } catch { }
        return (tris, meshCount);
    }

    /// <summary>DS1 object parts' collision (obj\MODEL.objbnd.dcx → MODEL.hkx mesh storage; not the _1 break fragments),
    /// placed with the part transform, tagged "obj:PART".</summary>
    static FloorProbe Ds1ObjectCollision(string dsr, MSB1 msb1)
    {
        var cache = new Dictionary<string, List<(Vector3 A, Vector3 B, Vector3 C)>>(StringComparer.OrdinalIgnoreCase);
        var result = new List<FloorProbe.Tri>();
        foreach (var o in msb1.Parts.Objects)
        {
            if (!cache.TryGetValue(o.ModelName, out var tris))
            {
                tris = [];
                string path = Path.Combine(dsr, "obj", o.ModelName + ".objbnd.dcx");
                if (File.Exists(path))
                    try
                    {
                        var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(path)));
                        var h = bnd.Files.FirstOrDefault(f => Path.GetFileName(f.Name).Equals(o.ModelName + ".hkx", StringComparison.OrdinalIgnoreCase));
                        if (h != null) tris = ColSurvey.Triangles(h.Bytes);
                    }
                    catch { }
                cache[o.ModelName] = tris;
            }
            var m = ColSurvey.Place(o.Position, o.Rotation, o.Scale);
            foreach (var (a, b, c) in tris) result.Add(new FloorProbe.Tri(Vector3.Transform(a, m), Vector3.Transform(b, m), Vector3.Transform(c, m), "obj:" + o.Name));
        }
        return new FloorProbe(result);
    }

    // ------------------------------------------------------------------ 3. draw groups
    static bool Any(uint[] a, uint[] b) { for (int i = 0; i < Math.Min(a.Length, b.Length); i++) if ((a[i] & b[i]) != 0) return true; return false; }
    static uint[] Area(MSB2.Part p) => [unchecked((uint)p.Unk44), unchecked((uint)p.Unk48), unchecked((uint)p.Unk4C), unchecked((uint)p.Unk50)];

    /// <summary>The DS1 piece parts a DS2 piece stands for: same model digits and position; with DG1 also a DS1 piece or
    /// decoration object at the position whose groups the DS2 piece carries (split sky models m9xxx, renamed clashes,
    /// decoration objects as pieces).</summary>
    static List<MSB1.Part> Ds1Twins(MSB1 msb1, MSB2.Part p2, bool dg1)
    {
        var byModel = msb1.Parts.MapPieces.Where(p => p.ModelName.Length >= 5 && p2.ModelName.Length >= 5 && p.ModelName.Substring(1, 4) == p2.ModelName.Substring(1, 4) && Vector3.Distance(p.Position, p2.Position) < 0.05f).Cast<MSB1.Part>().ToList();
        if (byModel.Count > 0 || !dg1) return byModel;
        return msb1.Parts.MapPieces.Cast<MSB1.Part>().Concat(msb1.Parts.Objects)
            .Where(p => Vector3.Distance(p.Position, p2.Position) < 0.05f && p.DrawGroups.SequenceEqual(p2.DrawGroups)).ToList();
    }

    static void Draw(MSB1 msb1, MSB2 msb2, string gamedata, string host, Report rep)
    {
        // DS1 piece part ↔ DS2 piece part: same model digits (mBBBB…) and position (rule S4 merges draw-group variants)
        var ds2Pieces = msb2.Parts.MapPieces;
        int hidden = 0, pairs = 0, unloadedObj = 0, overDraw = 0;
        var ds1Cols = msb1.Parts.Collisions.Where(c => c.HitFilterID == 8).ToList();
        // session 11 (burg11, round 9), rule DG1: a map whose pieces carry DS1's groups (more than one piece mask) must cull
        // like DS1 both ways; the draw budget = pieces drawn per collision
        bool dg1 = ds2Pieces.Select(p => string.Join(",", p.DrawGroups)).Distinct().Skip(1).Any();
        var drawnPer = new List<int>();
        foreach (var c2 in msb2.Parts.Collisions)
        {
            // DS1 counterpart: hBBBBBx (DS1) → h0x_BBBB (DS2); ramps (h0x_99NN) have none: they take the strictest test only
            var m = System.Text.RegularExpressions.Regex.Match(c2.ModelName, @"^h0(\d)_(\d{4})$");
            var c1 = m.Success ? ds1Cols.FirstOrDefault(c => c.ModelName.Length >= 7 && c.ModelName.Substring(1, 4) == m.Groups[2].Value && c.ModelName[6] == m.Groups[1].Value[0] && Vector3.Distance(c.Position, c2.Position) < 0.05f) : null;
            var area = Area(c2);
            int drawn = 0;
            foreach (var p2 in ds2Pieces)
            {
                pairs++;
                // round 9 (rule DG1): pieces are culled by the groups alone (vanilla TB pieces' DrawGroups miss their
                // collisions' object-area words Unk44..50 and still draw: the area words gate objects only)
                bool ds2Shows = Any(p2.DrawGroups, c2.DrawGroups) && Any(p2.DispGroups, c2.DispGroups);
                if (ds2Shows) drawn++;
                if (ds2Shows && dg1 && c1 != null)
                {
                    var q1s = Ds1Twins(msb1, p2, dg1);
                    if (q1s.Count > 0 && !q1s.Any(p1 => Any(p1.DrawGroups, c1.DispGroups)))
                    {
                        overDraw++;
                        rep.Fail("draw-over", $"piece {p2.ModelName} ({p2.Name}) drawn while standing on {c2.Name}, DS1 does not draw it from {c1.Name} (rule DG1)", c2.Position);
                    }
                }
                if (ds2Shows) continue;
                // would DS1 show it from c1? (rule DG1: a ramp, which has no DS1 counterpart, carries its DS1 collision's groups: not judged)
                bool ds1Shows = !(dg1 && c1 == null);
                if (c1 != null)
                {
                    var p1s = Ds1Twins(msb1, p2, dg1);
                    if (p1s.Count > 0) ds1Shows = p1s.Any(p1 => Any(p1.DrawGroups, c1.DispGroups));
                    else if (dg1) ds1Shows = false; // no DS1 part to compare (DG1 copied the groups from one): not judged
                }
                if (!ds1Shows) continue;
                hidden++;
                rep.Fail("draw", $"piece {p2.ModelName} ({p2.Name}) not drawn/loaded while standing on {c2.Name} (DS1 draws it): piece draw [{string.Join(",", p2.DrawGroups.Select(x => x.ToString("X8")))}] vs collision draw [{string.Join(",", c2.DrawGroups.Select(x => x.ToString("X8")))}] area [{string.Join(",", area.Select(x => x.ToString("X8")))}]", c2.Position);
            }
            if (c1 != null) drawnPer.Add(drawn);
            foreach (var o in msb2.Parts.Objects.Where(o => !dg1 || o.DrawGroups.All(w => w == 0xFFFFFFFF))) // rule DG1: DS1-grouped static objects load where DS1 shows them
                if (!Any(o.DrawGroups, area)) { unloadedObj++; rep.Fail("draw-object", $"object {o.Name} not active on {c2.Name} (draw [{string.Join(",", o.DrawGroups.Select(x => x.ToString("X8")))}] vs area [{string.Join(",", area.Select(x => x.ToString("X8")))}])", o.Position); }
        }
        // connects: the neighbour's pieces and collisions must be reached by the mask
        int connectMiss = 0;
        foreach (var k in msb2.Parts.ConnectCollisions)
        {
            string t = $"m{k.MapID[0]:D2}_{k.MapID[1]:D2}_{k.MapID[2]:D2}_{k.MapID[3]:D2}";
            string path = Path.Combine(gamedata, "map", t, t + ".msb");
            if (!File.Exists(path)) continue;
            var tm = MSB2.Read(path);
            var mask = Area(k);
            int miss = tm.Parts.MapPieces.Count(p => !Any(p.DrawGroups, mask)) + tm.Parts.Collisions.Count(p => !Any(p.DrawGroups, mask));
            // round 9, rule DG1: a neighbour with DS1's groups is shown by DS1's connect groups (MapSeams): only an empty mask fails
            bool tDg1 = tm.Parts.MapPieces.Select(p => string.Join(",", p.DrawGroups)).Distinct().Skip(1).Any();
            if (tDg1 ? mask.All(w => w == 0) : miss > 0) { connectMiss++; rep.Fail("seam-draw", $"connect {k.Name} on {k.CollisionName} → {t}: {(tDg1 ? "empty mask" : $"{miss} neighbour pieces/collisions outside its mask")}"); }
        }
        // vanilla rule (ds2-re-notes): map pieces and collisions set bits in all 4 words? Only report odd words
        Console.WriteLine($"[draw] {msb2.Parts.Collisions.Count} collisions × {ds2Pieces.Count} pieces = {pairs} pairs: {hidden} pieces DS1 draws but DS2 hides; {(dg1 ? $"{overDraw} drawn that DS1 culls" : "no DS1 groups (DG1 off: every piece everywhere)")}; {unloadedObj} inactive objects; {msb2.Parts.ConnectCollisions.Count} connects, {connectMiss} with neighbour parts outside the mask");
        if (drawnPer.Count > 0)
            Console.WriteLine($"[draw] budget (rule DG1): pieces drawn per DS1-backed collision: max {drawnPer.Max()}, mean {drawnPer.Average():0.0}, of {ds2Pieces.Count}; {ds2Pieces.Select(p => string.Join(",", p.DrawGroups)).Distinct().Count()} distinct piece masks");
    }

    // ------------------------------------------------------------------ 4. assets
    static void Assets(string pack, string gamedata, string host, MSB2 msb, Report rep)
    {
        string mdir = Path.Combine(gamedata, "model", "map");
        string tail = host[1..];
        var map = BXF4.Read(Path.Combine(mdir, host + ".mapbhd"), Path.Combine(mdir, host + ".mapbdt"));
        var tex = BXF4.Read(Path.Combine(mdir, $"t{tail}.tpfbhd"), Path.Combine(mdir, $"t{tail}.tpfbdt"));
        var gi = BXF4.Read(Path.Combine(mdir, $"g{tail}.gibhd"), Path.Combine(mdir, $"g{tail}.gibdt"));
        var hi = BXF4.Read(Path.Combine(mdir, $"h{tail}.hkxbhd"), Path.Combine(mdir, $"h{tail}.hkxbdt"));
        var lo = BXF4.Read(Path.Combine(mdir, $"l{tail}.hkxbhd"), Path.Combine(mdir, $"l{tail}.hkxbdt"));
        var low = File.Exists(Path.Combine(mdir, $"t{tail}_low.tpfbdt")) ? BND4.Read(Path.Combine(mdir, $"t{tail}_low.tpfbdt")) : null;
        static string Stem(string n) { n = Path.GetFileName(n.Replace('\\', '/')); int i = n.IndexOf('.'); return (i > 0 ? n[..i] : n).ToLowerInvariant(); }
        var texNames = tex.Files.Select(f => Stem(f.Name)).ToHashSet();
        var giNames = gi.Files.Select(f => Stem(f.Name)).ToHashSet();
        var lowNames = low?.Files.Select(f => Stem(f.Name)).ToHashSet() ?? [];
        var hNames = hi.Files.Select(f => Stem(f.Name)).ToHashSet();
        var lNames = lo.Files.Select(f => Stem(f.Name)).ToHashSet();
        var acb = ACB.Read(map.Files.First(f => f.Name.EndsWith(".acb", StringComparison.OrdinalIgnoreCase)).Bytes);
        var acbModels = acb.Assets.OfType<ACB.Asset.Model>().Select(a => Stem(a.AbsolutePath)).ToHashSet();
        var acbHkt = acb.Assets.OfType<ACB.Asset.General>().Select(a => Stem(a.AbsolutePath)).ToHashSet();
        var mtds = Ds2Mtds();
        int missingTex = 0, missingLow = 0, badMtd = 0, models = 0;
        foreach (var f in map.Files.Where(f => f.Name.EndsWith(".flv.dcx", StringComparison.OrdinalIgnoreCase)))
        {
            string name = Stem(f.Name);
            models++;
            if (!acbModels.Contains(name)) rep.Fail("asset", $"{host} model {name}: no ACB Model entry (DS2 draws it with defaults or not at all)");
            var flv = FLVER2.Read(DCX.Decompress(f.Bytes));
            foreach (var mat in flv.Materials)
            {
                if (mtds != null && !mtds.Contains(Path.GetFileName(mat.MTD).ToLowerInvariant())) { badMtd++; rep.Fail("mtd", $"{host} {name} material '{mat.Name}': MTD {mat.MTD} is not a DS2 MTD"); }
                foreach (var t in mat.Textures.Where(t => !string.IsNullOrWhiteSpace(t.Path)))
                {
                    string tn = Stem(t.Path);
                    bool isGi = t.ParamName.Contains("GI", StringComparison.OrdinalIgnoreCase) || tn.Contains("_gi_") || tn.StartsWith("hq_");
                    if (isGi) { if (!giNames.Contains(tn.StartsWith("hq_") ? tn[3..] : tn)) { missingTex++; rep.Fail("texture", $"{host} {name} '{mat.Name}' {t.ParamName}: GI texture {tn} not in g{tail}"); } continue; }
                    if (!texNames.Contains(tn)) { if (!SharedTexture(t.Path)) { missingTex++; rep.Fail("texture", $"{host} {name} '{mat.Name}' {t.ParamName}: {tn} not in t{tail} ({t.Path})"); } continue; }
                    if (!lowNames.Contains("low_" + tn)) { missingLow++; rep.Warn("texture-low", $"{host} {name}: no low_{tn} in t{tail}_low (DS2 shows the low copy until the full one streams in)"); }
                }
            }
        }
        foreach (var c in msb.Parts.Collisions)
        {
            string m = c.ModelName.ToLowerInvariant();
            if (!hNames.Contains(m)) rep.Fail("collision-file", $"{host} collision {c.Name}: model {c.ModelName} not in h{tail} (no collision: the player falls)", c.Position);
            if (!lNames.Contains("l" + m[1..])) rep.Warn("collision-file", $"{host} collision {c.Name}: no l{c.ModelName[1..]} in l{tail} (DS2 builds the low body from it)");
            if (!acbHkt.Contains(m)) rep.Warn("collision-acb", $"{host} collision model {c.ModelName}: no model_hkt ACB entry");
        }
        foreach (var p in msb.Parts.MapPieces)
            if (!map.Files.Any(f => Stem(f.Name) == p.ModelName.ToLowerInvariant())) rep.Fail("asset", $"{host} piece {p.Name}: model {p.ModelName} not in the map archive", p.Position);
        // LQ archives: DS2 reads maplq:/model_lq/map/t|g… when the resource manager's LQ flag is set (FUN_14048dd30)
        string lq = Path.Combine(pack, "maplq", "model_lq", "map", $"t{tail}.tpfbhd");
        if (!File.Exists(lq)) rep.Warn("lq", $"{host}: no maplq\\model_lq\\map\\t{tail} / g{tail} archives (DS2's LQ texture path would find no textures for this map)");
        Console.WriteLine($"[assets] {models} piece models: {missingTex} missing textures, {missingLow} without a low copy, {badMtd} unknown MTDs; {msb.Parts.Collisions.Count} collision parts checked against h/l archives");
    }

    static bool SharedTexture(string path) => path.Contains(@"\common\", StringComparison.OrdinalIgnoreCase) || path.Contains("/common/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("N:\\FRPG2\\data\\Model\\system", StringComparison.OrdinalIgnoreCase) || path.Contains("SYSTEX", StringComparison.OrdinalIgnoreCase);

    static HashSet<string>? Ds2Mtds()
    {
        try
        {
            var m = Ds2MapMaterials.Load(ExtractDs2.DefaultOut);
            return m.ByMtd.Keys.Select(k => Path.GetFileName(k).ToLowerInvariant()).ToHashSet();
        }
        catch { return null; }
    }
}
