using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// winding-check FLVER-SOURCE... — for each culled face set, how many triangles have their geometric normal
/// ((b-a)×(c-a)) along the vertex normals (+) or against them (-). Sources: a .flver(.dcx) file, or
/// mapbhd:PATH (every model in a map archive). Tells which winding a game treats as front-facing.
/// </summary>
public static class WindingCheck
{
    public static int Run(string[] args)
    {
        foreach (string src in args.Skip(1))
        {
            var models = new List<(string, FLVER2)>();
            if (src.StartsWith("mapbhd:"))
            {
                string bhd = src[7..];
                var bxf = BXF4.Read(bhd, Path.ChangeExtension(bhd, ".mapbdt"));
                foreach (var f in bxf.Files.Where(f => f.Name.EndsWith(".flv.dcx", StringComparison.OrdinalIgnoreCase)).Take(12))
                    models.Add((Path.GetFileName(f.Name), FLVER2.Read(DCX.Decompress(f.Bytes))));
            }
            else
            {
                byte[] b = File.ReadAllBytes(src);
                models.Add((Path.GetFileName(src), FLVER2.Read(DCX.Is(b) ? DCX.Decompress(b) : b)));
            }
            long plus = 0, minus = 0;
            foreach (var (name, flver) in models)
            {
                long p = 0, m = 0;
                foreach (var mesh in flver.Meshes)
                    foreach (var fs in mesh.FaceSets.Where(fs => fs.Flags == FLVER2.FaceSet.FSFlags.None && fs.CullBackfaces))
                    {
                        var idx = fs.Triangulate(mesh.Vertices.Count < ushort.MaxValue);
                        for (int i = 0; i + 2 < idx.Count; i += 3)
                        {
                            var a = mesh.Vertices[idx[i]]; var bb = mesh.Vertices[idx[i + 1]]; var c = mesh.Vertices[idx[i + 2]];
                            var g = Vector3.Cross(bb.Position - a.Position, c.Position - a.Position);
                            if (g.LengthSquared() < 1e-12f) continue;
                            var n = a.Normal + bb.Normal + c.Normal;
                            var n3 = new Vector3(n.X, n.Y, n.Z);
                            if (Vector3.Dot(g, n3) > 0) p++; else m++;
                        }
                    }
                Console.WriteLine($"  {name}: {p} along, {m} against");
                plus += p; minus += m;
            }
            Console.WriteLine($"{src}: geometric normal along vertex normal {plus}, against {minus}");
        }
        return 0;
    }
}
