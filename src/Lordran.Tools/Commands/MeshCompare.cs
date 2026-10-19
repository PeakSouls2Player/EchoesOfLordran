using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// mesh-compare DS1MAP MODEL [MESH] — a DS1 map piece model (e.g. m0001B1) against its converted DS2 model in the pack:
/// per mesh, vertex count, bounding box, bone/node binding, and the largest vertex position difference.
/// </summary>
public static class MeshCompare
{
    public static int Run(string[] args)
    {
        string map = args[1], model = args[2];
        int only = args.Length > 3 ? int.Parse(args[3]) : -1;
        string area = map.Substring(1, 2);
        var ds1 = FLVER2.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(Program.DefaultDsrRoot, "map", map, $"{model}A{area}.flver.dcx"))));
        string ds2Name = $"m{model.Substring(1, 4)}";
        string bhd = Path.Combine(Build.Pack.DefaultOut, "gamedata", "model", "map", map + ".mapbhd");
        var bxf = BXF4.Read(bhd, Path.ChangeExtension(bhd, ".mapbdt"));
        var file = bxf.Files.First(x => Path.GetFileName(x.Name).StartsWith(ds2Name + ".flv", StringComparison.OrdinalIgnoreCase));
        var ds2 = FLVER2.Read(DCX.Decompress(file.Bytes));
        Console.WriteLine($"DS1 {model}: {ds1.Meshes.Count} meshes, {ds1.Nodes.Count} nodes; DS2 {ds2Name}: {ds2.Meshes.Count} meshes, {ds2.Nodes.Count} nodes");
        for (int i = 0; i < Math.Min(ds1.Meshes.Count, ds2.Meshes.Count); i++)
        {
            if (only >= 0 && i != only) continue;
            var a = ds1.Meshes[i]; var b = ds2.Meshes[i];
            (Vector3, Vector3) Box(FLVER2.Mesh m) => (m.Vertices.Aggregate(new Vector3(float.MaxValue), (s, v) => Vector3.Min(s, v.Position)), m.Vertices.Aggregate(new Vector3(float.MinValue), (s, v) => Vector3.Max(s, v.Position)));
            var (amin, amax) = Box(a); var (bmin, bmax) = Box(b);
            float maxDiff = 0; int worst = -1;
            for (int k = 0; k < Math.Min(a.Vertices.Count, b.Vertices.Count); k++)
            {
                float dd = Vector3.Distance(a.Vertices[k].Position, b.Vertices[k].Position);
                if (dd > maxDiff) { maxDiff = dd; worst = k; }
            }
            Console.WriteLine($"mesh {i}: verts {a.Vertices.Count}/{b.Vertices.Count} node {a.NodeIndex}/{b.NodeIndex} boneIdx[{string.Join(",", a.BoneIndices.Take(4))}]/[{string.Join(",", b.BoneIndices.Take(4))}] dynamic {a.Dynamic}/{b.Dynamic}");
            Console.WriteLine($"   DS1 box {amin} .. {amax}");
            Console.WriteLine($"   DS2 box {bmin} .. {bmax}   max vertex shift {maxDiff:0.###} at #{worst}{(worst >= 0 ? $" ({a.Vertices[worst].Position} → {b.Vertices[worst].Position}; bone idx {a.Vertices[worst].BoneIndices[0]},{a.Vertices[worst].BoneIndices[1]})" : "")}");
        }
        foreach (var (n, idx) in ds1.Nodes.Select((n, idx) => (n, idx)).Where(t => t.n.Translation != Vector3.Zero || t.n.Rotation != Vector3.Zero).Take(12))
            Console.WriteLine($"DS1 node {idx} '{n.Name}' T={n.Translation} R={n.Rotation} S={n.Scale} parent={n.ParentIndex}");
        return 0;
    }
}
