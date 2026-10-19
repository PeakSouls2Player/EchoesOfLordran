using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// plane-faces DS1MAP AXIS VALUE TOL XMIN XMAX YMIN YMAX — DS1 map-piece triangles lying in the world plane AXIS=VALUE
/// (±TOL) inside a rectangle of the other two axes (x/y ranges as given; for AXIS=x they mean z/y): per mesh, how many
/// face +AXIS / -AXIS (by vertex normal), the material, and whether the face set culls back faces.
/// </summary>
public static class PlaneFaces
{
    public static int Run(string[] args)
    {
        string map = args[1]; char axis = args[2][0];
        float value = float.Parse(args[3]), tol = float.Parse(args[4]);
        float aMin = float.Parse(args[5]), aMax = float.Parse(args[6]), yMin = float.Parse(args[7]), yMax = float.Parse(args[8]);
        var msb = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", map + ".msb"));
        string area = map.Substring(1, 2);
        float Axis(Vector3 v) => axis == 'x' ? v.X : axis == 'y' ? v.Y : v.Z;
        float Other(Vector3 v) => axis == 'x' ? v.Z : v.X;
        foreach (var part in msb.Parts.MapPieces)
        {
            string f = Path.Combine(Program.DefaultDsrRoot, "map", map, $"{part.ModelName}A{area}.flver.dcx");
            if (!File.Exists(f)) continue;
            var flver = FLVER2.Read(DCX.Decompress(File.ReadAllBytes(f)));
            const float d = MathF.PI / 180;
            var m = Matrix4x4.CreateScale(part.Scale) * Matrix4x4.CreateRotationZ(part.Rotation.Z * d) * Matrix4x4.CreateRotationX(part.Rotation.X * d) * Matrix4x4.CreateRotationY(part.Rotation.Y * d) * Matrix4x4.CreateTranslation(part.Position);
            for (int mi = 0; mi < flver.Meshes.Count; mi++)
            {
                var mesh = flver.Meshes[mi];
                foreach (var fs in mesh.FaceSets.Where(fs => fs.Flags == FLVER2.FaceSet.FSFlags.None))
                {
                    var idx = fs.Triangulate(mesh.Vertices.Count < ushort.MaxValue);
                    int pos = 0, neg = 0;
                    for (int i = 0; i + 2 < idx.Count; i += 3)
                    {
                        var vs = new[] { mesh.Vertices[idx[i]], mesh.Vertices[idx[i + 1]], mesh.Vertices[idx[i + 2]] };
                        var w = vs.Select(v => Vector3.Transform(v.Position, m)).ToArray();
                        if (w.Any(p => float.IsNaN(p.X) || MathF.Abs(Axis(p) - value) > tol || Other(p) < aMin || Other(p) > aMax || p.Y < yMin || p.Y > yMax)) continue;
                        var n = vs.Aggregate(Vector3.Zero, (s, v) => s + new Vector3(v.Normal.X, v.Normal.Y, v.Normal.Z));
                        n = Vector3.TransformNormal(n, m);
                        float c = Axis(n);
                        if (MathF.Abs(c) < 0.5f * n.Length()) continue;
                        if (c > 0) pos++; else neg++;
                    }
                    if (pos + neg > 0)
                        Console.WriteLine($"{part.Name,-12} mesh {mi,-3} +{axis}:{pos,-5} -{axis}:{neg,-5} cull={fs.CullBackfaces} mtd={Path.GetFileName(flver.Materials[mesh.MaterialIndex].MTD)}");
                }
            }
        }
        return 0;
    }
}
