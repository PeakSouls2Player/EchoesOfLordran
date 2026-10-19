using System.Globalization;
using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// lane-check DS1MAP PACK_DIR [--y Y] [--radius R] [--except PART]... [--profile] X1,Z1,X2,Z2 ... — is a straight test lane (the player walks / rolls
/// from 1 to 2) clear in a built map? Per lane: the floor under it and R to each side (DS1's walkable collision,
/// HitFilterID 8 parts enabled at start — the same coordinates our map uses), walls in the body band (steep collision
/// triangles between floor + 0.3 and floor + 1.7 m within R of the lane) and every object part of the built MSB
/// (PACK_DIR\gamedata\map\MAP\MAP.msb) whose footprint (FLVER bounding box, any rotation, overlapping the band) comes
/// within R of the lane; --except skips named parts (the lane's own target).
/// lane-check DS1MAP PACK_DIR --scan XMIN,ZMIN,XMAX,ZMAX --len L --dir +x|-x|+z|-z [--y Y] [--radius R] [--step S]
/// lists the clear lanes of length L in a rectangle (start points on an S grid), best clearance first (40; --all: all).
/// Session 8 (track potroll): the first roll-break A/B layout put the control lane through one of our urns
/// (o18_8410_0036 at (11.88, -22.76), 0.4 m from the approach point); test layouts are now checked with this.
/// </summary>
public static class LaneCheck
{
    sealed record Tri(Vector3 A, Vector3 B, Vector3 C);
    internal sealed record Obj(string Name, string Model, Vector2 P, float Radius, float Bottom, float Top);

    public static int Run(string[] args)
    {
        string map = args[1], pack = args[2];
        float R = Opt(args, "--radius", 0.45f);
        float? yOpt = args.Contains("--y") ? Opt(args, "--y", 0) : null;
        var (floorTris, steepTris) = LoadCollision(map);
        var except = args.Select((a, i) => (a, i)).Where(x => x.a == "--except").Select(x => args[x.i + 1]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var objs = LoadObjects(map, pack).Where(o => !except.Contains(o.Name)).ToList();
        Console.WriteLine($"{map}: {floorTris.Count} walkable + {steepTris.Count} steep DS1 collision triangles, {objs.Count} object parts in {pack}");

        int si = Array.IndexOf(args, "--scan");
        if (si > 0)
        {
            var rect = Floats(args[si + 1]);
            float len = Opt(args, "--len", 6), step = Opt(args, "--step", 0.25f);
            string dir = args[Array.IndexOf(args, "--dir") + 1];
            var d = dir switch { "+x" => new Vector2(1, 0), "-x" => new Vector2(-1, 0), "+z" => new Vector2(0, 1), _ => new Vector2(0, -1) };
            var hits = new List<(Vector2 A, Vector2 B, float Clear, float Floor)>();
            for (float x = rect[0]; x <= rect[2] + 1e-3f; x += step)
                for (float z = rect[1]; z <= rect[3] + 1e-3f; z += step)
                {
                    var a = new Vector2(x, z);
                    var b = a + d * len;
                    var r = Check(a, b, R, yOpt, floorTris, steepTris, objs, null);
                    if (r.Ok) hits.Add((a, b, r.Clearance, r.FloorMin));
                }
            foreach (var h in hits.OrderByDescending(h => h.Clear).Take(args.Contains("--all") ? int.MaxValue : 40))
                Console.WriteLine($"clear lane ({h.A.X:0.00},{h.A.Y:0.00}) → ({h.B.X:0.00},{h.B.Y:0.00}) floor {h.Floor:0.00} clearance {h.Clear:0.00}");
            Console.WriteLine($"{hits.Count} clear lanes");
            return 0;
        }

        int bad = 0;
        foreach (var lane in args.Skip(3).Where(a => a.Count(c => c == ',') == 3))
        {
            var v = Floats(lane);
            var log = new List<string>();
            var r = Check(new Vector2(v[0], v[1]), new Vector2(v[2], v[3]), R, yOpt, floorTris, steepTris, objs, log);
            if (args.Contains("--profile"))
            {
                // the floor along the centre line, every 0.5 m (highest walkable surface ≤ the reference + 0.6 m, or the
                // highest one at all without --y: on a walk down stairs, give each flight as its own lane)
                var a = new Vector2(v[0], v[1]); var b = new Vector2(v[2], v[3]);
                float L = Vector2.Distance(a, b), y0 = yOpt ?? FloorAt(floorTris, a, 1e9f) ?? 0;
                log.Add("profile: " + string.Join(" ", Enumerable.Range(0, (int)(L / 0.5f) + 1).Select(i => FloorAt(floorTris, a + Vector2.Normalize(b - a) * (i * 0.5f), y0 + 0.6f) is float f ? f.ToString("0.00", CultureInfo.InvariantCulture) : "-")));
            }
            Console.WriteLine($"lane ({v[0]},{v[1]}) → ({v[2]},{v[3]}): {(r.Ok ? "CLEAR" : "BLOCKED")} floor {r.FloorMin:0.000}..{r.FloorMax:0.000}, clearance {r.Clearance:0.00} (radius {R})");
            foreach (var l in log) Console.WriteLine("  " + l);
            if (!r.Ok) bad++;
        }
        return bad == 0 ? 0 : 1;
    }

    record Result(bool Ok, float FloorMin, float FloorMax, float Clearance);

    static Result Check(Vector2 a, Vector2 b, float R, float? yRef, List<Tri> floor, List<Tri> steep, List<Obj> objs, List<string>? log)
    {
        // floor: along the lane and R to each side, every 0.25 m
        var dir = Vector2.Normalize(b - a);
        var side = new Vector2(-dir.Y, dir.X);
        float len = Vector2.Distance(a, b);
        float y0 = yRef ?? FloorAt(floor, a, 1e9f) ?? float.NaN;
        if (float.IsNaN(y0)) { log?.Add("no floor at the lane start"); return new Result(false, float.NaN, float.NaN, 0); }
        float fmin = float.MaxValue, fmax = float.MinValue;
        bool ok = true;
        for (float t = 0; t <= len + 1e-3f; t += 0.25f)
            foreach (float s in new[] { -R, 0, R })
            {
                var p = a + dir * t + side * s;
                float? f = FloorAt(floor, p, y0 + 0.6f);
                if (f is not float fy || fy < y0 - 0.5f)
                {
                    ok = false;
                    log?.Add($"no floor within 0.5 m below {y0:0.00} at ({p.X:0.00},{p.Y:0.00})");
                    continue;
                }
                fmin = MathF.Min(fmin, fy); fmax = MathF.Max(fmax, fy);
            }
        if (fmax - fmin > 0.3f) { ok = false; log?.Add($"floor varies {fmin:0.00}..{fmax:0.00} (> 0.3 m: a step or slope)"); }
        // walls in the body band
        float clear = float.MaxValue;
        float lo = fmin + 0.3f, hi = fmax + 1.7f;
        foreach (var t in steep)
        {
            float tmin = MathF.Min(t.A.Y, MathF.Min(t.B.Y, t.C.Y)), tmax = MathF.Max(t.A.Y, MathF.Max(t.B.Y, t.C.Y));
            if (tmax < lo || tmin > hi) continue;
            float d = TriSegDist(t, a, b, lo, hi);
            if (d < clear) clear = d;
            if (d < R) { ok = false; log?.Add($"wall triangle {d:0.00} m from the lane near ({(t.A.X + t.B.X + t.C.X) / 3:0.00},{(t.A.Y + t.B.Y + t.C.Y) / 3:0.00},{(t.A.Z + t.B.Z + t.C.Z) / 3:0.00})"); }
        }
        foreach (var o in objs)
        {
            if (o.Top < lo || o.Bottom > hi) continue;   // below the body band (a floor) or above the head
            float d = SegDist(o.P, a, b) - o.Radius;
            if (d < clear) clear = d;
            if (d < R) { ok = false; log?.Add($"object {o.Name} ({o.Model}) at ({o.P.X:0.00},{o.Bottom:0.00}..{o.Top:0.00},{o.P.Y:0.00}) footprint r {o.Radius:0.00}: {d:0.00} m from the lane"); }
        }
        return new Result(ok, fmin, fmax, clear);
    }

    /// <summary>Highest walkable triangle under p at or below maxY.</summary>
    static float? FloorAt(List<Tri> tris, Vector2 p, float maxY)
    {
        float? best = null;
        foreach (var t in tris)
        {
            if (p.X < MathF.Min(t.A.X, MathF.Min(t.B.X, t.C.X)) || p.X > MathF.Max(t.A.X, MathF.Max(t.B.X, t.C.X))) continue;
            if (p.Y < MathF.Min(t.A.Z, MathF.Min(t.B.Z, t.C.Z)) || p.Y > MathF.Max(t.A.Z, MathF.Max(t.B.Z, t.C.Z))) continue;
            // barycentric in XZ
            var v0 = new Vector2(t.B.X - t.A.X, t.B.Z - t.A.Z); var v1 = new Vector2(t.C.X - t.A.X, t.C.Z - t.A.Z); var v2 = new Vector2(p.X - t.A.X, p.Y - t.A.Z);
            float den = v0.X * v1.Y - v1.X * v0.Y;
            if (MathF.Abs(den) < 1e-9f) continue;
            float u = (v2.X * v1.Y - v1.X * v2.Y) / den, w = (v0.X * v2.Y - v2.X * v0.Y) / den;
            if (u < -1e-4f || w < -1e-4f || u + w > 1 + 1e-4f) continue;
            float y = t.A.Y + u * (t.B.Y - t.A.Y) + w * (t.C.Y - t.A.Y);
            if (y <= maxY && (best == null || y > best)) best = y;
        }
        return best;
    }

    /// <summary>XZ distance from the lane segment to the part of a triangle inside the height band (sampled every ~5 cm).</summary>
    static float TriSegDist(Tri t, Vector2 a, Vector2 b, float lo, float hi)
    {
        float edge = MathF.Max(Vector3.Distance(t.A, t.B), MathF.Max(Vector3.Distance(t.B, t.C), Vector3.Distance(t.C, t.A)));
        int n = Math.Clamp((int)MathF.Ceiling(edge / 0.05f), 1, 400);
        float best = float.MaxValue;
        for (int i = 0; i <= n; i++)
            for (int j = 0; i + j <= n; j++)
            {
                float u = (float)i / n, v = (float)j / n;
                var p = t.A + u * (t.B - t.A) + v * (t.C - t.A);
                if (p.Y < lo || p.Y > hi) continue;
                best = MathF.Min(best, SegDist(new Vector2(p.X, p.Z), a, b));
            }
        return best;
    }

    static float SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / MathF.Max(ab.LengthSquared(), 1e-9f), 0, 1);
        return Vector2.Distance(p, a + ab * t);
    }

    static (List<Tri> Floor, List<Tri> Steep) LoadCollision(string map)
    {
        string root = Program.DefaultDsrRoot;
        var msb = MSB1.Read(Path.Combine(root, "map", "MapStudio", map + ".msb"));
        string d1 = Path.Combine(root, "map", map);
        var hi = BXF3.Read(Path.Combine(d1, $"h{map[1..]}.hkxbhd"), Path.Combine(d1, $"h{map[1..]}.hkxbdt"));
        var floor = new List<Tri>(); var steep = new List<Tri>();
        foreach (var part in msb.Parts.Collisions.Where(p => p.HitFilterID == 8 && !p.DisableStart))
        {
            var f = hi.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith($"h{part.ModelName[1..]}A", StringComparison.OrdinalIgnoreCase));
            if (f == null) continue;
            var m = ColSurvey.Place(part.Position, part.Rotation, part.Scale);
            foreach (var (a, b, c) in ColSurvey.Triangles(DCX.Decompress(f.Bytes)))
            {
                var t = new Tri(Vector3.Transform(a, m), Vector3.Transform(b, m), Vector3.Transform(c, m));
                var nrm = Vector3.Cross(t.B - t.A, t.C - t.A);
                if (nrm.LengthSquared() < 1e-12f) continue;
                nrm = Vector3.Normalize(nrm);
                if (MathF.Abs(nrm.Y) >= 0.6f) floor.Add(t); else steep.Add(t);
            }
        }
        return (floor, steep);
    }

    internal static List<Obj> LoadObjects(string map, string pack) // session 11: also seam-check --objects
    {
        map = Build.Maps.MapHosts.Of(map)?.Host ?? map; // session 11 (burg11): the DS1 map's DS2 host (rule MH1; m18_01 is its own)
        var msb = MSB2.Read(Path.Combine(pack, "gamedata", "map", map, map + ".msb"));
        // footprint: the FLVER bounding box (XZ radius over any yaw, Y extent); objects without a valid box (the
        // invisible key-guide helper o18_9300) or without a collision file are left out and listed
        var box = new Dictionary<string, (float R, float Lo, float Hi)?>(StringComparer.OrdinalIgnoreCase);
        (float R, float Lo, float Hi)? Box(string model)
        {
            if (box.TryGetValue(model, out var c)) return c;
            string bnd = Path.Combine(pack, "gamedata", "model", "obj", model + ".bnd");
            if (!File.Exists(bnd)) bnd = Path.Combine(ExtractDs2.DefaultOut, "model", "obj", model + ".bnd");
            (float, float, float)? res = null;
            if (File.Exists(bnd))
            {
                var files = BND4.Read(bnd).Files;
                var flv = files.FirstOrDefault(f => Path.GetFileName(f.Name).Equals(model + ".flv", StringComparison.OrdinalIgnoreCase));
                bool col = files.Any(f => Path.GetFileName(f.Name).Equals(model + "_h.hkx", StringComparison.OrdinalIgnoreCase));
                if (flv != null && col)
                {
                    var h = FLVER2.Read(flv.Bytes).Header;
                    float r = 0;
                    foreach (float x in new[] { h.BoundingBoxMin.X, h.BoundingBoxMax.X })
                        foreach (float z in new[] { h.BoundingBoxMin.Z, h.BoundingBoxMax.Z })
                            r = MathF.Max(r, MathF.Sqrt(x * x + z * z));
                    if (float.IsFinite(r) && float.IsFinite(h.BoundingBoxMin.Y) && float.IsFinite(h.BoundingBoxMax.Y)) res = (r, h.BoundingBoxMin.Y, h.BoundingBoxMax.Y);
                }
            }
            if (res == null) Console.WriteLine($"  (object model {model}: no FLVER box or no collision file, not counted)");
            return box[model] = res;
        }
        var list = new List<Obj>();
        foreach (var o in msb.Parts.Objects)
            if (Box(o.ModelName) is { } b)
                list.Add(new Obj(o.Name, o.ModelName, new Vector2(o.Position.X, o.Position.Z), b.R, o.Position.Y + b.Lo, o.Position.Y + b.Hi));
        return list;
    }

    static float Opt(string[] args, string name, float def)
    {
        int i = Array.IndexOf(args, name);
        return i > 0 ? float.Parse(args[i + 1], CultureInfo.InvariantCulture) : def;
    }

    static float[] Floats(string s) => s.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
}
