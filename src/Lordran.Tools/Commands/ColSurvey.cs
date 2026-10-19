using System.Numerics;
using Lordran.Tools.Formats.Havok;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// col-survey mAA_BB_CC_DD [--tris PART] — DS1 (DSR) map collision vs visible geometry: per MSB collision part, its
/// HitFilterID, triangle count, bounds, and how many of its triangles have no map-piece geometry within 0.5 m ("hidden",
/// i.e. invisible collision). Used to tell DS1's special collision (invisible hulls for enemies/camera, filters other
/// than 8) from walkable geometry (session 6: the Asylum's "invisible walls"). --tris PART prints the hidden triangles'
/// centroids of one part.
/// </summary>
public static class ColSurvey
{
    public static int Run(string[] args)
    {
        string map = args[1];
        string? only = args.SkipWhile(a => a != "--tris").Skip(1).FirstOrDefault();
        string root = Program.DefaultDsrRoot, area = map.Substring(1, 2);
        var msb = MSB1.Read(Path.Combine(root, "map", "MapStudio", map + ".msb"));

        // visible geometry → 0.5 m voxel set (vertices, edge midpoints, centroids, plus a few interior samples)
        const float cell = 0.5f;
        var vox = new HashSet<(int, int, int)>();
        void Mark(Vector3 p) => vox.Add(((int)MathF.Floor(p.X / cell), (int)MathF.Floor(p.Y / cell), (int)MathF.Floor(p.Z / cell)));
        foreach (var part in msb.Parts.MapPieces)
        {
            string file = Path.Combine(root, "map", map, $"{part.ModelName}A{area}.flver.dcx");
            if (!File.Exists(file)) continue;
            var flver = FLVER2.Read(DCX.Decompress(File.ReadAllBytes(file)));
            var m = Place(part.Position, part.Rotation, part.Scale);
            foreach (var mesh in flver.Meshes)
            {
                var fs = mesh.FaceSets.FirstOrDefault(f => f.Flags == FLVER2.FaceSet.FSFlags.None);
                if (fs == null) continue;
                var idx = fs.Triangulate(true);
                for (int i = 0; i + 2 < idx.Count; i += 3)
                {
                    if (idx[i] >= mesh.Vertices.Count || idx[i + 1] >= mesh.Vertices.Count || idx[i + 2] >= mesh.Vertices.Count) continue;
                    var a = Vector3.Transform(mesh.Vertices[idx[i]].Position, m);
                    var b = Vector3.Transform(mesh.Vertices[idx[i + 1]].Position, m);
                    var c = Vector3.Transform(mesh.Vertices[idx[i + 2]].Position, m);
                    if (!Sane(a) || !Sane(b) || !Sane(c)) continue;
                    float len = MathF.Max(Vector3.Distance(a, b), MathF.Max(Vector3.Distance(b, c), Vector3.Distance(a, c)));
                    int n = Math.Clamp((int)(len / (cell * 0.7f)) + 1, 1, 200);
                    for (int u = 0; u <= n; u++)
                        for (int v = 0; v <= n - u; v++)
                        {
                            float fu = (float)u / n, fv = (float)v / n;
                            Mark(a + (b - a) * fu + (c - a) * fv);
                        }
                }
            }
        }
        bool Near(Vector3 p)
        {
            int x = (int)MathF.Floor(p.X / cell), y = (int)MathF.Floor(p.Y / cell), z = (int)MathF.Floor(p.Z / cell);
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                if (vox.Contains((x + dx, y + dy, z + dz))) return true;
            return false;
        }

        string d1 = Path.Combine(root, "map", map);
        string kind = args.Contains("--low") ? "l" : "h"; // --low: DS1's low-res collision archive
        var hi = BXF3.Read(Path.Combine(d1, $"{kind}{map[1..]}.hkxbhd"), Path.Combine(d1, $"{kind}{map[1..]}.hkxbdt"));
        Console.WriteLine($"{"part",-16} {"filter",6} {"tris",6} {"hidden",7}  bounds");
        foreach (var part in msb.Parts.Collisions)
        {
            if (only != null && part.Name != only) continue;
            var f = hi.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith($"{kind}{part.ModelName[1..]}A", StringComparison.OrdinalIgnoreCase));
            if (f == null) { Console.WriteLine($"{part.Name,-16} no hkx"); continue; }
            var m = Place(part.Position, part.Rotation, part.Scale);
            var tris = Triangles(DCX.Decompress(f.Bytes)).Select(t => (Vector3.Transform(t.A, m), Vector3.Transform(t.B, m), Vector3.Transform(t.C, m))).ToList();
            // --probe X Y0 Y1 Z: steep triangles within 0.3 m of the vertical segment (X, Y0..Y1, Z) (invisible walls)
            int pi = Array.IndexOf(args, "--probe");
            if (pi > 0)
            {
                float px = float.Parse(args[pi + 1]), y0 = float.Parse(args[pi + 2]), y1 = float.Parse(args[pi + 3]), pz = float.Parse(args[pi + 4]);
                foreach (var (a, b, c) in tris)
                {
                    var nrm = Vector3.Cross(b - a, c - a);
                    if (nrm.Length() < 1e-6f || MathF.Abs(Vector3.Normalize(nrm).Y) > 0.5f) continue;
                    var lo = Vector3.Min(a, Vector3.Min(b, c)); var hi2 = Vector3.Max(a, Vector3.Max(b, c));
                    if (lo.X - 0.3f <= px && px <= hi2.X + 0.3f && lo.Z - 0.3f <= pz && pz <= hi2.Z + 0.3f && lo.Y <= y1 && hi2.Y >= y0)
                        Console.WriteLine($"  {part.Name}: steep tri ({lo.X:0.00},{lo.Y:0.00},{lo.Z:0.00})..({hi2.X:0.00},{hi2.Y:0.00},{hi2.Z:0.00})");
                }
                continue;
            }
            int hidden = 0, risers = 0;
            var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
            foreach (var (a, b, c) in tris)
            {
                min = Vector3.Min(min, Vector3.Min(a, Vector3.Min(b, c))); max = Vector3.Max(max, Vector3.Max(a, Vector3.Max(b, c)));
                var g = (a + b + c) / 3;
                var nrm = Vector3.Cross(b - a, c - a);
                if (nrm.Length() > 1e-6f && MathF.Abs(Vector3.Normalize(nrm).Y) < 0.15f)
                {
                    float h = MathF.Max(a.Y, MathF.Max(b.Y, c.Y)) - MathF.Min(a.Y, MathF.Min(b.Y, c.Y));
                    if (h > 0.08f && h < 0.4f) risers++;
                }
                if (!Near(g)) { hidden++; if (only != null) Console.WriteLine($"  hidden tri at ({g.X:0.0}, {g.Y:0.0}, {g.Z:0.0}) normal-y {Vector3.Normalize(Vector3.Cross(b - a, c - a)).Y:0.00}"); }
            }
            Console.WriteLine($"{part.Name,-16} {part.HitFilterID,6} {tris.Count,6} {100.0 * hidden / Math.Max(1, tris.Count),6:0}% risers {risers,4}  ({min.X:0},{min.Y:0},{min.Z:0})..({max.X:0},{max.Y:0},{max.Z:0})");
        }
        return 0;
    }

    static bool Sane(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z) && p.Length() < 1e5f;

    public static Matrix4x4 Place(Vector3 pos, Vector3 rotDeg, Vector3 scale)
    {
        const float d = MathF.PI / 180;
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationZ(rotDeg.Z * d) * Matrix4x4.CreateRotationX(rotDeg.X * d) * Matrix4x4.CreateRotationY(rotDeg.Y * d) * Matrix4x4.CreateTranslation(pos);
    }

    /// <summary>Triangles of a DSR collision tagfile (CustomParamStorageExtendedMeshShape storage subparts).</summary>
    public static List<(Vector3 A, Vector3 B, Vector3 C)> Triangles(byte[] tagfile)
    {
        var tag = new HkTagfile(tagfile);
        static HkTagfile.HkObj O(object? v) => (HkTagfile.HkObj)v!;
        static List<object?> L(object? v) => (List<object?>)v!;
        static float[] F(object? v) => L(v).Select(x => System.Convert.ToSingle(x)).ToArray();
        var rb = O(L(O(L(O(O(L(tag.Root!["namedVariants"])[0])["variant"])["systems"])[0])["rigidBodies"])[0]);
        var mesh = O(O(O(O(rb["collidable"])["shape"])["child"])["childShape"]);
        var subparts = L(mesh["trianglesSubparts"]).Cast<HkTagfile.HkObj>().ToList();
        var storage = L(mesh["meshstorage"]).Cast<HkTagfile.HkObj>().ToList();
        var result = new List<(Vector3, Vector3, Vector3)>();
        for (int i = 0; i < storage.Count; i++)
        {
            var verts = L(storage[i]["vertices"]).Select(v => { var a = F(v); return new Vector3(a[0], a[1], a[2]); }).ToList();
            var idx = L(storage[i]["indices16"]).Select(x => System.Convert.ToInt32(x)).ToList();
            if (idx.Count == 0) idx = L(storage[i]["indices32"]).Select(x => System.Convert.ToInt32(x)).ToList();
            int nt = i < subparts.Count ? System.Convert.ToInt32(subparts[i]["numTriangleShapes"]) : idx.Count / 4;
            int step = nt > 0 && idx.Count >= nt * 4 ? 4 : 3;
            for (int t = 0; t + 2 < idx.Count; t += step)
                if (idx[t] < verts.Count && idx[t + 1] < verts.Count && idx[t + 2] < verts.Count)
                    result.Add((verts[idx[t]], verts[idx[t + 1]], verts[idx[t + 2]]));
        }
        return result;
    }
}

/// <summary>
/// col-survey-ds2 MAP — vanilla DS2 collision for comparison: per h-collision model of a DS2 map, triangles, step risers
/// (vertical faces 0.08-0.4 m high) and the flatness of what's walkable. Session 6: DS1's stairs are real steps in its
/// collision; does DS2 model stairs as steps or as ramps?
/// </summary>
public static class ColSurveyDs2
{
    public static int Run(string[] args)
    {
        string map = args[1];
        string dir = Path.Combine(Lordran.Tools.Commands.ExtractDs2.DefaultOut, "model", "map");
        var bxf = BXF4.Read(Path.Combine(dir, $"h{map[1..]}.hkxbhd"), Path.Combine(dir, $"h{map[1..]}.hkxbdt"));
        int totalTris = 0, totalRisers = 0, steep = 0;
        foreach (var f in bxf.Files.Where(f => f.Name.EndsWith(".hkx.dcx", StringComparison.OrdinalIgnoreCase)))
        {
            var pf = new Lordran.Tools.Formats.Havok.HkPackfile(DCX.Decompress(f.Bytes));
            var root = pf.ReadRoot();
            var tris = new List<(System.Numerics.Vector3, System.Numerics.Vector3, System.Numerics.Vector3)>();
            try
            {
                var sys = (Lordran.Tools.Formats.Havok.HkPObj)((Lordran.Tools.Formats.Havok.HkPObj)root.List("namedVariants")[0]!)["variant"]!;
                foreach (Lordran.Tools.Formats.Havok.HkPObj s in sys.List("systems"))
                    foreach (Lordran.Tools.Formats.Havok.HkPObj rb in s.List("rigidBodies"))
                    {
                        var mopp = (Lordran.Tools.Formats.Havok.HkPObj)rb.Get("collidable")["shape"]!;
                        if (mopp.Class != "hkpMoppBvTreeShape") continue;
                        var mesh = (Lordran.Tools.Formats.Havok.HkPObj)mopp.Get("child")["childShape"]!;
                        foreach (Lordran.Tools.Formats.Havok.HkPObj st in mesh.List("meshstorage"))
                        {
                            var verts = st.List("vertices").Select(v => { var a = (float[])v!; return new System.Numerics.Vector3(a[0], a[1], a[2]); }).ToList();
                            var idx = st.List("indices16").Select(x => System.Convert.ToInt32(x)).ToList();
                            for (int t = 0; t + 2 < idx.Count; t += 4)
                                if (idx[t] < verts.Count && idx[t + 1] < verts.Count && idx[t + 2] < verts.Count) tris.Add((verts[idx[t]], verts[idx[t + 1]], verts[idx[t + 2]]));
                        }
                    }
            }
            catch (Exception e) { Console.WriteLine($"{f.Name}: {e.Message}"); continue; }
            int risers = 0, s30 = 0;
            foreach (var (a, b, c) in tris)
            {
                var n = System.Numerics.Vector3.Cross(b - a, c - a);
                if (n.Length() < 1e-6f) continue;
                n = System.Numerics.Vector3.Normalize(n);
                float h = MathF.Max(a.Y, MathF.Max(b.Y, c.Y)) - MathF.Min(a.Y, MathF.Min(b.Y, c.Y));
                if (MathF.Abs(n.Y) < 0.15f && h > 0.08f && h < 0.4f) risers++;
                if (MathF.Abs(n.Y) is > 0.6f and < 0.95f) s30++;
            }
            totalTris += tris.Count; totalRisers += risers; steep += s30;
            Console.WriteLine($"{Path.GetFileName(f.Name),-24} tris {tris.Count,6} risers {risers,5} sloped(18-53deg) {s30,5}");
        }
        Console.WriteLine($"total: {totalTris} triangles, {totalRisers} risers ({100.0 * totalRisers / Math.Max(1, totalTris):0.0}%), {steep} sloped");
        return 0;
    }
}
