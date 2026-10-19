using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// navpath MAP X1 Y1 Z1 X2 Y2 Z2 [--offset] — a walking route on a DS2 map's navmesh (map\MAP\MAP.ngp) between two
/// world positions, printed as goto waypoints for the autopilot ("goto X Z"). Triangles are joined across shared edges
/// (by vertex position, so separate meshes connect too); A* over triangle centroids, then the edge-midpoint route is
/// shortened wherever a straight line stays on the navmesh. navpath MAP alone prints the meshes' extent.
/// NGP vertices are map-local: world = local + the MSB MapOffset (Things Betwixt -498, 30, -260).
/// </summary>
public static class NavPath
{
    sealed record Tri(Vector3 A, Vector3 B, Vector3 C)
    {
        public Vector3 Center => (A + B + C) / 3;
    }

    public static int Run(string[] args)
    {
        string map = args[1];
        string data = Environment.GetEnvironmentVariable("LR_NAV_DATA") ?? ExtractDs2.DefaultOut; // LR_NAV_DATA=dataordranpackgamedata for our maps
        var ngp = NGP.Read(Path.Combine(data, "map", map, map + ".ngp"));
        var msb = MSB2.Read(Path.Combine(data, "map", map, map + ".msb"));
        Vector3 offset = msb.Events.MapOffsets.FirstOrDefault()?.Translation ?? Vector3.Zero;
        var tris = new List<Tri>();
        foreach (var m in ngp.Meshes)
            foreach (var f in m.Faces)
                tris.Add(new Tri(m.Vertices[f.V1] + offset, m.Vertices[f.V2] + offset, m.Vertices[f.V3] + offset));
        if (args.Length < 8)
        {
            var min = tris.Aggregate(new Vector3(float.MaxValue), (a, t) => Vector3.Min(a, Vector3.Min(t.A, Vector3.Min(t.B, t.C))));
            var max = tris.Aggregate(new Vector3(float.MinValue), (a, t) => Vector3.Max(a, Vector3.Max(t.A, Vector3.Max(t.B, t.C))));
            Console.WriteLine($"{map}: {ngp.Meshes.Count} meshes, {tris.Count} triangles, map offset {offset}, world extent {min} .. {max}");
            return 0;
        }
        var from = new Vector3(float.Parse(args[2]), float.Parse(args[3]), float.Parse(args[4]));
        var to = new Vector3(float.Parse(args[5]), float.Parse(args[6]), float.Parse(args[7]));

        // ---- adjacency across shared edges (vertex positions rounded to 1 cm)
        static (long, long, long) Key(Vector3 v) => ((long)MathF.Round(v.X * 100), (long)MathF.Round(v.Y * 100), (long)MathF.Round(v.Z * 100));
        var edges = new Dictionary<((long, long, long), (long, long, long)), List<int>>();
        for (int i = 0; i < tris.Count; i++)
        {
            var t = tris[i];
            foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
            {
                var a = Key(p); var b = Key(q);
                var k = a.CompareTo(b) < 0 ? (a, b) : (b, a);
                if (!edges.TryGetValue(k, out var list)) edges[k] = list = new List<int>();
                list.Add(i);
            }
        }
        var adj = new List<(int To, Vector3 Mid, Vector3 P, Vector3 Q)>[tris.Count];
        for (int i = 0; i < tris.Count; i++) adj[i] = new();
        foreach (var (k, list) in edges)
        {
            if (list.Count < 2) continue;
            var p = new Vector3(k.Item1.Item1, k.Item1.Item2, k.Item1.Item3) / 100;
            var q = new Vector3(k.Item2.Item1, k.Item2.Item2, k.Item2.Item3) / 100;
            foreach (int a in list) foreach (int b in list) if (a != b) adj[a].Add((b, (p + q) / 2, p, q));
        }

        int Locate(Vector3 p)
        {
            int best = -1; float bestDy = float.MaxValue;
            for (int i = 0; i < tris.Count; i++)
            {
                if (!InXZ(tris[i], p, out float y)) continue;
                float dy = MathF.Abs(y - p.Y);
                if (dy < bestDy) { bestDy = dy; best = i; }
            }
            if (best >= 0) return best;
            // off the mesh: the nearest centroid
            return Enumerable.Range(0, tris.Count).MinBy(i => Vector3.DistanceSquared(tris[i].Center, p));
        }
        int s = Locate(from), g = Locate(to);
        Console.WriteLine($"start triangle {s} (centre {Fmt(tris[s].Center)}), goal triangle {g} (centre {Fmt(tris[g].Center)})");

        // ---- A* over triangles; node positions are the entry-edge midpoints
        var pos = new Dictionary<int, Vector3> { [s] = from };
        var cost = new Dictionary<int, float> { [s] = 0 };
        var prev = new Dictionary<int, int>();
        var open = new PriorityQueue<int, float>();
        open.Enqueue(s, Vector3.Distance(from, to));
        var closed = new HashSet<int>();
        while (open.Count > 0)
        {
            int cur = open.Dequeue();
            if (cur == g) break;
            if (!closed.Add(cur)) continue;
            foreach (var (nb, mid, _, _) in adj[cur])
            {
                float c = cost[cur] + Vector3.Distance(pos[cur], mid);
                if (cost.TryGetValue(nb, out float old) && old <= c) continue;
                cost[nb] = c; pos[nb] = mid; prev[nb] = cur;
                open.Enqueue(nb, c + Vector3.Distance(mid, to));
            }
        }
        if (!cost.ContainsKey(g)) { Console.WriteLine("no route on the navmesh"); return 1; }
        var route = new List<int>();
        for (int n = g; ; n = prev[n]) { route.Add(n); if (n == s) break; }
        route.Reverse();
        var points = new List<Vector3> { from };
        points.AddRange(route.Skip(1).Select(n => pos[n]));
        points.Add(to);

        // ---- shorten: drop a point when the straight line from the last kept point to the next stays on the mesh
        bool OnMesh(Vector3 a, Vector3 b)
        {
            int steps = Math.Max(2, (int)(Vector3.Distance(a, b) / 0.25f));
            for (int i = 1; i < steps; i++)
            {
                var p = Vector3.Lerp(a, b, i / (float)steps);
                if (!tris.Any(t => InXZ(t, p, out float y) && MathF.Abs(y - p.Y) < 1.2f)) return false;
            }
            return true;
        }
        var kept = new List<Vector3> { points[0] };
        int at = 0;
        while (at < points.Count - 1)
        {
            int next = at + 1;
            for (int j = points.Count - 1; j > at + 1; j--)
                if (OnMesh(points[at], points[j])) { next = j; break; }
            kept.Add(points[next]);
            at = next;
        }
        float length = 0;
        for (int i = 1; i < kept.Count; i++) length += Vector3.Distance(kept[i - 1], kept[i]);
        Console.WriteLine($"route: {kept.Count} points, {length:0.0} m");
        foreach (var p in kept.Skip(1)) Console.WriteLine($"goto {p.X:0.00} {p.Z:0.00}   # y {p.Y:0.00}");
        return 0;
    }

    static string Fmt(Vector3 v) => $"({v.X:0.00}, {v.Y:0.00}, {v.Z:0.00})";

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
