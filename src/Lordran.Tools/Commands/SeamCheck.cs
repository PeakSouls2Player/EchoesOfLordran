using System.Numerics;
using Lordran.Tools.Build.Maps;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (burg11, BURG-PLAN M1 gate link-check): <c>seam-check PACK MAP_A MAP_B [--from X,Y,Z --to X,Y,Z]
/// [--via X,Y,Z ...] [--steps FILE]</c>.
/// 1. Static (rules CC1 / SB1 / WL1, research\seam.md): WorldMapList ≤ 42 lines and lists both maps; every ConnectCollision
///    references a collision of its map and a registered target; each map has ≥ 1 connect to the other; every
///    category-23 EventLocation row names a registered map and lies on a collision that carries a connect to that map (the
///    runtime only stalls on such a collision: FUN_1401ea510).
/// 2. Walk (with --from/--to): an A* route over BOTH maps' navmeshes (NGP, joined at shared or ≤ 0.1 m apart vertices),
///    then the route sampled every 0.25 m against both maps' DS2 collision: FAIL on a sample with no floor within
///    +0.6 / −1.2 m (a gap or fall-through) and on a map change A → B unless the A collision just left carries a connect to
///    B (B is loaded before the player reaches it) — WARN when the B collision has no connect back to A (walking back would
///    need the stall). --steps writes the route as ds2-steps "goto X Z" lines.
/// </summary>
public static class SeamCheck
{
    sealed record Tri(Vector3 A, Vector3 B, Vector3 C, string Map) { public Vector3 Center => (A + B + C) / 3; }

    public static int Run(string[] args)
    {
        if (args.Length < 4) { Console.Error.WriteLine("usage: seam-check PACK MAP_A MAP_B [--from X,Y,Z --to X,Y,Z] [--via X,Y,Z ...] [--steps FILE]"); return 1; }
        string pack = args[1];
        string gamedata = Path.Combine(pack, "gamedata");
        string[] maps = [args[2], args[3]];
        Vector3? from = null, to = null; var via = new List<Vector3>(); string? stepsOut = null;
        var avoid = new List<(Vector3 P, float R)>(); // --avoid X,Y,Z,R: cells the floor walk may not enter (a closed door object: objects aren't in the probe)
        var objectBlocks = new List<LaneCheck.Obj>(); // --objects: the built MSBs' objects with collision block their footprint (0.7 × bounding radius)
        for (int i = 4; i < args.Length; i++)
            switch (args[i])
            {
                case "--from": from = V(args[++i]); break;
                case "--to": to = V(args[++i]); break;
                case "--via": via.Add(V(args[++i])); break;
                case "--steps": stepsOut = args[++i]; break;
                case "--avoid": { var q = args[++i].Split(','); avoid.Add((V(string.Join(',', q[..3])), float.Parse(q[3], System.Globalization.CultureInfo.InvariantCulture))); break; }
            }
        int fails = 0, warns = 0;
        void Fail(string s) { fails++; Console.WriteLine("FAIL " + s); }
        void Warn(string s) { warns++; Console.WriteLine("WARN " + s); }

        // ---- 1. static
        string list = File.ReadAllText(Path.Combine(gamedata, "map", "worldmaplist", "worldmaplist.list"));
        var listed = list.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith('m')).Select(l => l.Split('\t', ' ')[0]).ToList();
        Console.WriteLine($"WorldMapList: {listed.Count} maps (cap {MapBuilder.WorldMapListCap}); index of {maps[0]} = {listed.IndexOf(maps[0])}, {maps[1]} = {listed.IndexOf(maps[1])}");
        if (listed.Count > MapBuilder.WorldMapListCap) Fail($"rule WL1: {listed.Count} WorldMapList lines");
        foreach (var m in maps) if (!listed.Contains(m)) Fail($"{m} is not in WorldMapList");
        var msbs = maps.ToDictionary(m => m, m => MSB2.Read(Path.Combine(gamedata, "map", m, m + ".msb")));
        // connect-bearing collisions: map → collision part → target maps
        var connects = new Dictionary<string, Dictionary<string, HashSet<string>>>();
        foreach (var (m, msb) in msbs)
        {
            var d = connects[m] = new();
            foreach (var k in msb.Parts.ConnectCollisions)
            {
                string target = $"m{k.MapID[0]:D2}_{k.MapID[1]:D2}_{k.MapID[2]:D2}_{k.MapID[3]:D2}";
                if (!msb.Parts.Collisions.Any(c => c.Name == k.CollisionName)) Fail($"{m} connect {k.Name}: collision {k.CollisionName} not in the MSB");
                if (!listed.Contains(target)) Fail($"{m} connect {k.Name} → {target}: not registered");
                if (!d.TryGetValue(k.CollisionName, out var set)) d[k.CollisionName] = set = [];
                set.Add(target);
            }
            int toOther = d.Values.Count(s => s.Contains(maps[0] == m ? maps[1] : maps[0]));
            Console.WriteLine($"{m}: {msb.Parts.ConnectCollisions.Count} ConnectCollisions, {toOther} on collisions toward {(maps[0] == m ? maps[1] : maps[0])}");
            if (toOther == 0) Fail($"{m} has no ConnectCollision toward the other map (rule CC1)");
        }
        var probes = maps.ToDictionary(m => m, m => FloorProbe.FromFolder(pack, m));
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        foreach (var m in maps)
        {
            var p = PARAM.Read(Path.Combine(gamedata, "param", $"eventlocation_{m}.param"));
            if (!p.ApplyParamdefCarefully(defs)) { Fail($"EventLocation_{m}: no paramdef fits"); continue; }
            int boxes = 0;
            foreach (var r in p.Rows.Where(r => Convert.ToInt32(r["categoryType"].Value) == 23))
            {
                boxes++;
                int cat = Convert.ToInt32(r["categoryId"].Value);
                string target = $"m{cat / 1000000:D2}_{cat / 10000 % 100:D2}_{cat / 100 % 100:D2}_{cat % 100:D2}";
                var pos = new Vector3(Convert.ToSingle(r["PositionX"].Value), Convert.ToSingle(r["PositionY"].Value), Convert.ToSingle(r["PositionZ"].Value));
                if (!listed.Contains(target)) Fail($"{m} box {r.ID} → {target}: not registered");
                float half = Convert.ToSingle(r["ScaleY"].Value) / 2;
                var floor = probes[m].Floor(pos - new Vector3(0, half - 0.5f, 0), above: 0.6f, below: 2f); // from the box's bottom (MapSeams puts it 0.5 m under the floor)
                bool ok = floor is { } f && connects[m].TryGetValue(f.Part, out var t) && t.Contains(target);
                Console.WriteLine($"{m} category-23 box {r.ID} → {target} at {pos:0.00} on {floor?.Part ?? "(no floor)"}: {(ok ? "on a collision with a connect to it" : "NOT on a connect-bearing collision")}");
                if (!ok) Fail($"{m} box {r.ID}: not on a collision carrying a connect to {target} (the stall never triggers)");
            }
            if (boxes == 0) Warn($"{m}: no category-23 box (rule SB1)");
        }

        // ---- 2b. floor walk (--flood): A* over the two maps' DS2 COLLISION (what the player stands on), independent of the
        // navmesh: 0.5 m cells, a step is allowed when the next floor is within 0.55 m up / 1.0 m down (the step ramps make
        // DS1's risers slopes) and no wall crosses the body band (rays at floor + 0.6 / + 1.2 m); every map change is
        // judged as in the navmesh walk
        if (args.Contains("--flood") && from is { } fa && to is { } fb)
        {
            if (args.Contains("--objects"))
                foreach (var m in maps)
                    foreach (var ob in LaneCheck.LoadObjects(m, pack).Where(x => !x.Model.Contains("_93") && !x.Model.Contains("_91") && !x.Model.StartsWith("o00_050")))   // key-guide helpers have no body; session 11 (walk11): white doors are crossed with the action (FW1 walls start disabled by event 0)
                        objectBlocks.Add(ob);
            var path = FloorWalk(maps, probes, fa, fb, out int expanded, avoid, objectBlocks);
            if (path == null) Fail($"floor walk: no path on the collision from {fa:0.00} to {fb:0.00} ({expanded} cells searched)");
            else
            {
                Console.WriteLine($"floor walk: {path.Count} cells ({expanded} searched), {(path.Count - 1) * 0.5f:0} m");
                for (int i = 1; i < path.Count; i++)
                {
                    var (m0, part0, _) = path[i - 1]; var (m1, part1, p1) = path[i];
                    if (m0 == m1) continue;
                    bool loaded = connects[m0].TryGetValue(part0, out var t1) && t1.Contains(m1);
                    bool back = connects[m1].TryGetValue(part1, out var t2) && t2.Contains(m0);
                    Console.WriteLine($"  floor walk map change {m0} ({part0}) → {m1} ({part1}) at {p1:0.00}: {(loaded ? "target loaded" : "TARGET NOT LOADED")}, {(back ? "back-connect present" : "no back-connect")}");
                    if (!loaded) Fail($"floor walk map change at {p1:0.00}: {part0} has no connect to {m1}");
                    if (!back) Warn($"floor walk map change at {p1:0.00}: {part1} has no connect back to {m0}");
                }
                if (stepsOut != null)
                {
                    var pts = new List<Vector3>();
                    for (int i = 0; i < path.Count; i += 6) pts.Add(path[i].P);
                    pts.Add(path[^1].P);
                    File.WriteAllLines(stepsOut + ".floor.txt", pts.Select(q => $"goto {q.X:0.00} {q.Z:0.00}   # y {q.Y:0.00}"));
                    Console.WriteLine($"floor-walk route: {stepsOut}.floor.txt ({pts.Count} goto lines, every 3 m)");
                }
            }
        }

        // ---- 2. walk
        if (!args.Contains("--flood") && from is { } a && to is { } b)
        {
            var tris = new List<Tri>();
            foreach (var m in maps)
            {
                var ngp = NGP.Read(Path.Combine(gamedata, "map", m, m + ".ngp"));
                var off = msbs[m].Events.MapOffsets.FirstOrDefault()?.Translation ?? Vector3.Zero;
                foreach (var mesh in ngp.Meshes) foreach (var f in mesh.Faces) tris.Add(new Tri(mesh.Vertices[f.V1] + off, mesh.Vertices[f.V2] + off, mesh.Vertices[f.V3] + off, m));
            }
            var waypoints = new List<Vector3> { a };
            waypoints.AddRange(via);
            waypoints.Add(b);
            var route = new List<Vector3> { a };
            for (int i = 1; i < waypoints.Count; i++)
            {
                var leg = Route(tris, waypoints[i - 1], waypoints[i]);
                if (leg == null) { Fail($"no navmesh route {waypoints[i - 1]:0.00} → {waypoints[i]:0.00} over {maps[0]} + {maps[1]} (navmeshes not joined at the seam?)"); route = null; break; }
                route.AddRange(leg.Skip(1));
            }
            if (route != null)
            {
                float length = 0; for (int i = 1; i < route.Count; i++) length += Vector3.Distance(route[i - 1], route[i]);
                Console.WriteLine($"route: {route.Count} points, {length:0.0} m");
                string? lastMap = null, lastPart = null; int gaps = 0, samples = 0, transitions = 0, cracks = 0;
                Vector3? lastPos = null;
                for (int i = 1; i < route.Count; i++)
                {
                    int n = Math.Max(1, (int)(Vector3.Distance(route[i - 1], route[i]) / 0.25f));
                    for (int s = 0; s <= n; s++)
                    {
                        var p = Vector3.Lerp(route[i - 1], route[i], s / (float)n);
                        samples++;
                        (string Map, FloorProbe.Hit Hit)? best = null;
                        // the navmesh line runs up to ~1 m over stairs (edge midpoints): a floor within +1.0 / -1.5 m of it
                        foreach (var m in maps)
                            if (probes[m].Floor(p, above: 1.0f, below: 1.5f) is { } h && (best == null || h.Y > best.Value.Hit.Y)) best = (m, h);
                        if (best == null)
                        {
                            // a crack narrower than the character (DS1's own floors meet with < 0.35 m gaps): floor all around
                            int around = 0;
                            foreach (var d in new[] { new Vector3(0.35f, 0, 0), new Vector3(-0.35f, 0, 0), new Vector3(0, 0, 0.35f), new Vector3(0, 0, -0.35f) })
                                if (maps.Any(m => probes[m].Floor(p + d, above: 1.0f, below: 1.5f) != null)) around++;
                            if (around >= 3) { cracks++; continue; }
                            var near = maps.SelectMany(m => probes[m].Down(p.X, p.Z).Select(h => (m, h))).OrderBy(q => MathF.Abs(q.h.Y - p.Y)).FirstOrDefault();
                            if (gaps++ < 10) Fail($"no floor within +1.0/-1.5 m at {p:0.00}; nearest surface {(near.m == null ? "none" : $"{near.h.Y - p.Y:+0.00;-0.00} m ({near.m} {near.h.Part}, normal y {near.h.NormalY:0.00})")}");
                            continue;
                        }
                        var (bm, bh) = best.Value;
                        if (lastMap != null && bm != lastMap)
                        {
                            transitions++;
                            bool loaded = lastPart != null && connects[lastMap].TryGetValue(lastPart, out var t1) && t1.Contains(bm);
                            bool back = connects[bm].TryGetValue(bh.Part, out var t2) && t2.Contains(lastMap);
                            Console.WriteLine($"  map change {lastMap} ({lastPart}) → {bm} ({bh.Part}) at {p:0.00}: {(loaded ? "target loaded (connect on the collision left)" : "TARGET NOT LOADED by the collision left")}, {(back ? "back-connect present" : "no back-connect")}");
                            if (!loaded) Fail($"map change at {p:0.00}: {lastPart} in {lastMap} has no connect to {bm} (load screen or fall)");
                            if (!back) Warn($"map change at {p:0.00}: {bh.Part} in {bm} has no connect back to {lastMap}");
                        }
                        lastMap = bm; lastPart = bh.Part; lastPos = p;
                    }
                }
                Console.WriteLine($"walk: {samples} samples, {gaps} without floor, {cracks} on cracks < 0.35 m (floor on 3+ sides), {transitions} map changes");
                if (transitions == 0) Warn("the route never changes map (does it cross the seam?)");
                if (stepsOut != null)
                {
                    var lines = route.Skip(1).Select(q => $"goto {q.X:0.00} {q.Z:0.00}").ToList();
                    File.WriteAllLines(stepsOut, lines);
                    Console.WriteLine($"ds2-steps route: {stepsOut} ({lines.Count} goto lines)");
                }
            }
        }
        Console.WriteLine($"seam-check {maps[0]} <-> {maps[1]}: {fails} FAIL, {warns} WARN");
        return fails > 0 ? 1 : 0;
    }

    internal static List<(string Map, string Part, Vector3 P)>? FloorWalk(string[] maps, Dictionary<string, FloorProbe> probes, Vector3 from, Vector3 to, out int expanded, List<(Vector3 P, float R)> avoid, List<LaneCheck.Obj> objs)
    {
        const float S = 0.5f, Up = 0.55f, Down = 1.0f;
        // a node = a walkable floor at a cell centre: (ix, iz, y in cm)
        List<(string Map, string Part, float Y)> Floors(int ix, int iz)
        {
            var list = new List<(string, string, float)>();
            foreach (var m in maps)
                foreach (var h in probes[m].Down(ix * S, iz * S))
                    if (h.NormalY >= 0.6f && !list.Any(f => MathF.Abs(f.Item3 - h.Y) < 0.05f)) list.Add((m, h.Part, h.Y));
            return list;
        }
        bool Blocked(Vector3 p, Vector3 q)
        {
            var d = q - p; d.Y = 0;
            foreach (float hgt in new[] { 0.6f, 1.2f })
                foreach (var m in maps)
                    if (probes[m].Ray(p + new Vector3(0, hgt, 0), d, S) is float t && t < S * 0.9f) return true;
            return false;
        }
        var cache = new Dictionary<(int, int), List<(string Map, string Part, float Y)>>();
        List<(string Map, string Part, float Y)> F(int ix, int iz) => cache.TryGetValue((ix, iz), out var l) ? l : cache[(ix, iz)] = Floors(ix, iz);
        (int, int, int)? Snap(Vector3 p)
        {
            int ix = (int)MathF.Round(p.X / S), iz = (int)MathF.Round(p.Z / S);
            var f = F(ix, iz).Where(x => x.Y <= p.Y + 1f).OrderBy(x => MathF.Abs(x.Y - p.Y)).Select(x => (ix, iz, (int)MathF.Round(x.Y * 100))).FirstOrDefault();
            return f == default ? null : f;
        }
        expanded = 0;
        if (Snap(from) is not { } s || Snap(to) is not { } g) return null;
        var cost = new Dictionary<(int, int, int), float> { [s] = 0 };
        var prev = new Dictionary<(int, int, int), (int, int, int)>();
        var open = new PriorityQueue<(int, int, int), float>();
        open.Enqueue(s, 0);
        var closed = new HashSet<(int, int, int)>();
        Vector3 P((int X, int Z, int Y) n) => new(n.X * S, n.Y / 100f, n.Z * S);
        while (open.Count > 0 && expanded < 400_000)
        {
            var cur = open.Dequeue();
            if (cur == g) break;
            if (!closed.Add(cur)) continue;
            expanded++;
            var pc = P(cur);
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                foreach (var f in F(cur.Item1 + dx, cur.Item2 + dz))
                {
                    float dy = f.Y - pc.Y;
                    if (dy > Up || dy < -Down) continue;
                    var nb = (cur.Item1 + dx, cur.Item2 + dz, (int)MathF.Round(f.Y * 100));
                    if (closed.Contains(nb)) continue;
                    var pn = P(nb);
                    if (avoid.Any(a => Vector3.Distance(a.P, pn) < a.R)) continue;
                    if (objs.Any(ob => Vector2.Distance(ob.P, new Vector2(pn.X, pn.Z)) < ob.Radius * 0.7f && ob.Top > pn.Y + 0.3f && ob.Bottom < pn.Y + 1.7f)) continue;
                    if (Blocked(pc, pn)) continue;
                    float c = cost[cur] + Vector3.Distance(pc, pn);
                    if (cost.TryGetValue(nb, out float old) && old <= c) continue;
                    cost[nb] = c; prev[nb] = cur;
                    open.Enqueue(nb, c + Vector3.Distance(pn, to));
                }
            }
        }
        if (!cost.ContainsKey(g)) return null;
        var path = new List<(string, string, Vector3)>();
        for (var n = g; ; n = prev[n])
        {
            var p = P(n);
            var fl = F(n.Item1, n.Item2).First(x => (int)MathF.Round(x.Y * 100) == n.Item3);
            path.Add((fl.Map, fl.Part, p));
            if (n == s) break;
        }
        path.Reverse();
        return path;
    }

    static Vector3 V(string s) { var c = s.Split(','); return new Vector3(float.Parse(c[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(c[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(c[2], System.Globalization.CultureInfo.InvariantCulture)); }

    /// <summary>A* over the joined navmesh triangles (NavPath's method; shared edges by 10 cm-rounded vertex positions, so
    /// meshes of both maps join where DS1's NVMs meet), shortened where the straight line stays on the mesh.</summary>
    static List<Vector3>? Route(List<Tri> tris, Vector3 from, Vector3 to)
    {
        static (long, long, long) Key(Vector3 v) => ((long)MathF.Round(v.X * 10), (long)MathF.Round(v.Y * 10), (long)MathF.Round(v.Z * 10));
        var edges = new Dictionary<((long, long, long), (long, long, long)), List<int>>();
        for (int i = 0; i < tris.Count; i++)
        {
            var t = tris[i];
            foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
            {
                var ka = Key(p); var kb = Key(q);
                var k = ka.CompareTo(kb) < 0 ? (ka, kb) : (kb, ka);
                if (!edges.TryGetValue(k, out var l)) edges[k] = l = [];
                l.Add(i);
            }
        }
        var adj = new List<(int To, Vector3 Mid)>[tris.Count];
        for (int i = 0; i < tris.Count; i++) adj[i] = [];
        foreach (var (k, l) in edges)
        {
            if (l.Count < 2) continue;
            var mid = (new Vector3(k.Item1.Item1, k.Item1.Item2, k.Item1.Item3) + new Vector3(k.Item2.Item1, k.Item2.Item2, k.Item2.Item3)) / 20;
            foreach (int x in l) foreach (int y in l) if (x != y) adj[x].Add((y, mid));
        }
        // across the seam the two maps' meshes (converted from different DS1 NVMs) share no vertices: join triangles of
        // different maps whose vertices come within 0.3 m of each other (1 m buckets)
        var buckets = new Dictionary<(int, int, int), List<int>>();
        static (int, int, int) B(Vector3 v) => ((int)MathF.Floor(v.X), (int)MathF.Floor(v.Y), (int)MathF.Floor(v.Z));
        for (int i = 0; i < tris.Count; i++)
            foreach (var v in new[] { tris[i].A, tris[i].B, tris[i].C })
            {
                if (!buckets.TryGetValue(B(v), out var l)) buckets[B(v)] = l = [];
                if (l.Count == 0 || l[^1] != i) l.Add(i);
            }
        for (int i = 0; i < tris.Count; i++)
        {
            var ti = tris[i];
            foreach (var v in new[] { ti.A, ti.B, ti.C })
            {
                var (bx, by, bz) = B(v);
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                {
                    if (!buckets.TryGetValue((bx + dx, by + dy, bz + dz), out var l)) continue;
                    foreach (int j in l)
                    {
                        if (tris[j].Map == ti.Map || adj[i].Any(e => e.To == j)) continue;
                        var tj = tris[j];
                        var w = new[] { tj.A, tj.B, tj.C }.MinBy(u => Vector3.Distance(u, v));
                        if (Vector3.Distance(w, v) > 0.3f) continue;
                        var mid = (v + w) / 2;
                        adj[i].Add((j, mid)); adj[j].Add((i, mid));
                    }
                }
            }
        }
        int Locate(Vector3 p)
        {
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < tris.Count; i++) if (InXZ(tris[i], p, out float y) && MathF.Abs(y - p.Y) < bd) { bd = MathF.Abs(y - p.Y); best = i; }
            return best >= 0 ? best : Enumerable.Range(0, tris.Count).MinBy(i => Vector3.DistanceSquared(tris[i].Center, p));
        }
        int s = Locate(from), g = Locate(to);
        var pos = new Dictionary<int, Vector3> { [s] = from };
        var cost = new Dictionary<int, float> { [s] = 0 };
        var prev = new Dictionary<int, int>();
        var open = new PriorityQueue<int, float>();
        open.Enqueue(s, 0);
        var closed = new HashSet<int>();
        while (open.Count > 0)
        {
            int cur = open.Dequeue();
            if (cur == g) break;
            if (!closed.Add(cur)) continue;
            foreach (var (nb, mid) in adj[cur])
            {
                float c = cost[cur] + Vector3.Distance(pos[cur], mid);
                if (cost.TryGetValue(nb, out float old) && old <= c) continue;
                cost[nb] = c; pos[nb] = mid; prev[nb] = cur;
                open.Enqueue(nb, c + Vector3.Distance(mid, to));
            }
        }
        if (!cost.ContainsKey(g)) return null;
        var ids = new List<int>();
        for (int n = g; ; n = prev[n]) { ids.Add(n); if (n == s) break; }
        ids.Reverse();
        var points = new List<Vector3> { from };
        points.AddRange(ids.Skip(1).Select(n => pos[n]));
        points.Add(to);
        bool OnMesh(Vector3 p0, Vector3 p1)
        {
            int steps = Math.Max(2, (int)(Vector3.Distance(p0, p1) / 0.25f));
            for (int i = 1; i < steps; i++)
            {
                var p = Vector3.Lerp(p0, p1, i / (float)steps);
                if (!tris.Any(t => InXZ(t, p, out float y) && MathF.Abs(y - p.Y) < 1.2f)) return false;
            }
            return true;
        }
        var kept = new List<Vector3> { points[0] };
        int at = 0;
        while (at < points.Count - 1)
        {
            int next = at + 1;
            for (int j = Math.Min(points.Count - 1, at + 40); j > at + 1; j--) if (OnMesh(points[at], points[j])) { next = j; break; }
            kept.Add(points[next]);
            at = next;
        }
        return kept;
    }

    static bool InXZ(Tri t, Vector3 p, out float y)
    {
        y = 0;
        float d = (t.B.Z - t.C.Z) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Z - t.C.Z);
        if (MathF.Abs(d) < 1e-6f) return false;
        float l1 = ((t.B.Z - t.C.Z) * (p.X - t.C.X) + (t.C.X - t.B.X) * (p.Z - t.C.Z)) / d;
        float l2 = ((t.C.Z - t.A.Z) * (p.X - t.C.X) + (t.A.X - t.C.X) * (p.Z - t.C.Z)) / d;
        float l3 = 1 - l1 - l2;
        if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) return false;
        y = l1 * t.A.Y + l2 * t.B.Y + l3 * t.C.Y;
        return true;
    }
}
