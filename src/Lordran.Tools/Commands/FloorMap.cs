using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// floor-map DS1MAP X Y Z R [--object PART | --piece PART] [--step S] — session 8 (track nests): what the player stands on
/// around a point in DS1. For every grid point (step S, default 0.5 m) within R of (X, Z): the highest DS1 walkable
/// collision surface (hit filter 8 parts, any part with a DS1 hkx; ray down from Y + 3) and, with --object/--piece, the
/// highest visible surface of that MSB part (object FLVER from obj\MODEL.objbnd.dcx, or a map-piece FLVER), so one can
/// see whether DS1's map collision follows a model (e.g. the crow's nest bowl o8550) or whether the model needs a
/// collision of its own. Prints the grid (collision height − Y, model height − Y) and a summary.
/// </summary>
public static class FloorMap
{
    public static int Run(string[] args)
    {
        if (args.Length < 6) { Console.Error.WriteLine("usage: floor-map DS1MAP X Y Z R [--object PART | --piece PART] [--step S]"); return 1; }
        string map = args[1];
        var at = new Vector3(float.Parse(args[2]), float.Parse(args[3]), float.Parse(args[4]));
        float r = float.Parse(args[5]);
        string? Opt(string k) { int i = Array.IndexOf(args, k); return i > 0 && i + 1 < args.Length ? args[i + 1] : null; }
        float step = Opt("--step") is { } s ? float.Parse(s) : 0.5f;
        string root = Program.DefaultDsrRoot, area = map.Substring(1, 2);
        var msb = MSB1.Read(Path.Combine(root, "map", "MapStudio", map + ".msb"));
        string d1 = Path.Combine(root, "map", map);
        var hi = BXF3.Read(Path.Combine(d1, $"h{map[1..]}.hkxbhd"), Path.Combine(d1, $"h{map[1..]}.hkxbdt"));

        // DS1 collision triangles near the point (world space), per part
        var col = new List<(Vector3 A, Vector3 B, Vector3 C, string Part)>();
        foreach (var part in msb.Parts.Collisions.Where(p => p.HitFilterID == 8))
        {
            var f = hi.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith($"h{part.ModelName[1..]}A", StringComparison.OrdinalIgnoreCase));
            if (f == null) continue;
            var m = ColSurvey.Place(part.Position, part.Rotation, part.Scale);
            foreach (var (a, b, c) in ColSurvey.Triangles(DCX.Decompress(f.Bytes)))
            {
                var wa = Vector3.Transform(a, m); var wb = Vector3.Transform(b, m); var wc = Vector3.Transform(c, m);
                var lo = Vector3.Min(wa, Vector3.Min(wb, wc)); var hiP = Vector3.Max(wa, Vector3.Max(wb, wc));
                if (lo.X > at.X + r || hiP.X < at.X - r || lo.Z > at.Z + r || hiP.Z < at.Z - r) continue;
                col.Add((wa, wb, wc, part.Name));
            }
        }
        Console.WriteLine($"DS1 hit-filter-8 collision triangles within the {2 * r} m square: {col.Count} ({string.Join(", ", col.GroupBy(t => t.Part).Select(g => $"{g.Key} {g.Count()}"))})");

        // the model's visible triangles (world space)
        var model = new List<(Vector3 A, Vector3 B, Vector3 C)>();
        string? objPart = Opt("--object"), piecePart = Opt("--piece");
        if (objPart != null || piecePart != null)
        {
            MSB1.Part part = objPart != null ? msb.Parts.Objects.First(p => p.Name == objPart) : msb.Parts.MapPieces.First(p => p.Name == piecePart);
            FLVER2 flv = objPart != null
                ? FLVER2.Read(BND3.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(root, "obj", part.ModelName + ".objbnd.dcx")))).Files.First(f => f.ID == 200).Bytes)
                : FLVER2.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(root, "map", map, $"{part.ModelName}A{area}.flver.dcx"))));
            var m = ColSurvey.Place(part.Position, part.Rotation, part.Scale);
            foreach (var mesh in flv.Meshes)
            {
                var fs = mesh.FaceSets.FirstOrDefault(f => f.Flags == FLVER2.FaceSet.FSFlags.None);
                if (fs == null) continue;
                var idx = fs.Triangulate(true);
                for (int i = 0; i + 2 < idx.Count; i += 3)
                {
                    if (idx[i] >= mesh.Vertices.Count || idx[i + 1] >= mesh.Vertices.Count || idx[i + 2] >= mesh.Vertices.Count) continue;
                    model.Add((Vector3.Transform(mesh.Vertices[idx[i]].Position, m), Vector3.Transform(mesh.Vertices[idx[i + 1]].Position, m), Vector3.Transform(mesh.Vertices[idx[i + 2]].Position, m)));
                }
            }
            var mlo = model.Aggregate(new Vector3(float.MaxValue), (a, t) => Vector3.Min(a, Vector3.Min(t.A, Vector3.Min(t.B, t.C))));
            var mhi = model.Aggregate(new Vector3(float.MinValue), (a, t) => Vector3.Max(a, Vector3.Max(t.A, Vector3.Max(t.B, t.C))));
            Console.WriteLine($"model {part.Name} ({part.ModelName}) at {part.Position} rot {part.Rotation} scale {part.Scale}: {model.Count} triangles, world bounds {mlo} .. {mhi}");
            for (int i = 0; i < flv.Meshes.Count; i++)
            {
                var mat = flv.Materials[flv.Meshes[i].MaterialIndex];
                Console.WriteLine($"  mesh {i}: {flv.Meshes[i].Vertices.Count} verts, material '{mat.Name}' {Path.GetFileName(mat.MTD)}: {string.Join(", ", mat.Textures.Where(t => !string.IsNullOrEmpty(t.Path)).Select(t => $"{t.ParamName}={Path.GetFileName(t.Path)}"))}");
            }
        }

        // vertical ray down from y0 at (x, z): highest hit at or below y0 (Möller–Trumbore on the XZ projection)
        static float? Hit(IEnumerable<(Vector3 A, Vector3 B, Vector3 C)> tris, float x, float z, float y0)
        {
            float? best = null;
            foreach (var (a, b, c) in tris)
            {
                float d = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
                if (MathF.Abs(d) < 1e-9f) continue; // vertical face
                float l1 = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / d;
                float l2 = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / d;
                float l3 = 1 - l1 - l2;
                if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                float y = l1 * a.Y + l2 * b.Y + l3 * c.Y;
                if (y <= y0 && (best == null || y > best)) best = y;
            }
            return best;
        }

        float top = at.Y + 3;
        var colTris = col.Select(t => (t.A, t.B, t.C)).ToList();
        int cells = 0, both = 0, close = 0, colBelow = 0, colAbove = 0;
        var diffs = new List<float>();
        Console.WriteLine($"grid (x across, z down), each cell \"collision/model\" height − {at.Y:0.00} (· = none; ray from y {top:0.00}):");
        for (float z = at.Z - r; z <= at.Z + r + 1e-3f; z += step)
        {
            var line = new System.Text.StringBuilder($"z {z,8:0.00} ");
            for (float x = at.X - r; x <= at.X + r + 1e-3f; x += step)
            {
                if ((x - at.X) * (x - at.X) + (z - at.Z) * (z - at.Z) > r * r) { line.Append("             "); continue; }
                float? c = Hit(colTris, x, z, top), mdl = model.Count > 0 ? Hit(model, x, z, top) : null;
                line.Append($"{(c is float cv ? (cv - at.Y).ToString("+0.00;-0.00") : "  ·  "),6}/{(mdl is float mv ? (mv - at.Y).ToString("+0.00;-0.00") : "  ·  "),-6}");
                cells++;
                if (c is float c1 && mdl is float m1)
                {
                    both++;
                    float dd = c1 - m1; diffs.Add(dd);
                    if (MathF.Abs(dd) <= 0.15f) close++; else if (dd < 0) colBelow++; else colAbove++;
                }
            }
            Console.WriteLine(line.ToString().TrimEnd());
        }
        Console.WriteLine($"cells {cells}; with collision and model {both}: collision within ±0.15 m of the model's top surface {close}, below it {colBelow}, above it {colAbove}" +
            (diffs.Count > 0 ? $"; collision − model: mean {diffs.Average():0.00}, min {diffs.Min():0.00}, max {diffs.Max():0.00}" : ""));
        return 0;
    }
}
