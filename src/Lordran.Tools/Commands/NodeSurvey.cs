using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// node-survey mAA_BB_CC_DD — DS1 (DSR) map piece FLVERs whose meshes hang on nodes with their own transform (vertex
/// positions in node space). The map converter used to copy raw positions onto node 0, which leaves such meshes floating
/// (session 6: stone blocks above the Asylum courtyard, broken stairs).
/// </summary>
public static class NodeSurvey
{
    public static int Run(string[] args)
    {
        string map = args[1], area = map.Substring(1, 2);
        string dir = Path.Combine(Program.DefaultDsrRoot, "map", map);
        // collision voxels (0.5 m) to check which vertex placement lands on the walkable/solid geometry
        var msb = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", map + ".msb"));
        var hi = BXF3.Read(Path.Combine(dir, $"h{map[1..]}.hkxbhd"), Path.Combine(dir, $"h{map[1..]}.hkxbdt"));
        var vox = new HashSet<(int, int, int)>();
        foreach (var part in msb.Parts.Collisions.Where(p => p.HitFilterID == 8))
        {
            var f = hi.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith($"h{part.ModelName[1..]}A", StringComparison.OrdinalIgnoreCase));
            if (f == null) continue;
            var pm = Matrix4x4.CreateRotationX(part.Rotation.X * MathF.PI / 180) * Matrix4x4.CreateRotationZ(part.Rotation.Z * MathF.PI / 180) * Matrix4x4.CreateRotationY(part.Rotation.Y * MathF.PI / 180) * Matrix4x4.CreateTranslation(part.Position);
            foreach (var (a, b, c) in ColSurvey.Triangles(DCX.Decompress(f.Bytes)))
                foreach (var p in new[] { a, b, c, (a + b + c) / 3 })
                {
                    var w = Vector3.Transform(p, pm);
                    vox.Add(((int)MathF.Floor(w.X / 0.5f), (int)MathF.Floor(w.Y / 0.5f), (int)MathF.Floor(w.Z / 0.5f)));
                }
        }
        bool Near(Vector3 p)
        {
            int x = (int)MathF.Floor(p.X / 0.5f), y = (int)MathF.Floor(p.Y / 0.5f), z = (int)MathF.Floor(p.Z / 0.5f);
            for (int dx = -2; dx <= 2; dx++) for (int dy = -2; dy <= 2; dy++) for (int dz = -2; dz <= 2; dz++)
                if (vox.Contains((x + dx, y + dy, z + dz))) return true;
            return false;
        }
        foreach (string file in Directory.GetFiles(dir, "*.flver.dcx").OrderBy(f => f))
        {
            var flver = FLVER2.Read(DCX.Decompress(File.ReadAllBytes(file)));
            string model = Path.GetFileName(file)[..Path.GetFileName(file).IndexOf("A" + area + ".")];
            var piece = msb.Parts.MapPieces.FirstOrDefault(p => p.ModelName == model);
            if (piece != null && flver.Nodes.Any(x => x.Translation.Length() > 0.001f || x.Rotation.Length() > 0.001f))
            {
                var pm = Matrix4x4.CreateRotationX(piece.Rotation.X * MathF.PI / 180) * Matrix4x4.CreateRotationZ(piece.Rotation.Z * MathF.PI / 180) * Matrix4x4.CreateRotationY(piece.Rotation.Y * MathF.PI / 180) * Matrix4x4.CreateTranslation(piece.Position);
                int raw = 0, moved = 0, n = 0;
                foreach (var m in flver.Meshes)
                    foreach (var v in m.Vertices.Where((_, i) => i % 7 == 0))
                    {
                        int bi = m.BoneIndices.Count > 0 ? m.BoneIndices[Math.Clamp(v.BoneIndices[0], 0, m.BoneIndices.Count - 1)] : m.NodeIndex;
                        var nm = bi >= 0 && bi < flver.Nodes.Count ? flver.Nodes[bi].ComputeLocalTransform() : Matrix4x4.Identity;
                        n++;
                        if (Near(Vector3.Transform(v.Position, pm))) raw++;
                        if (Near(Vector3.Transform(Vector3.Transform(v.Position, nm), pm))) moved++;
                    }
                Console.WriteLine($"{model}: {n} sampled vertices near collision: raw {100.0 * raw / Math.Max(1, n):0}%, node-transformed {100.0 * moved / Math.Max(1, n):0}%");
            }
            var odd = flver.Nodes.Select((n, i) => (n, i)).Where(x => x.n.Translation.Length() > 0.001f || x.n.Rotation.Length() > 0.001f || Vector3.Distance(x.n.Scale, Vector3.One) > 0.001f).ToList();
            int dynamic = flver.Meshes.Count(m => m.Dynamic != 0);
            var usesOdd = flver.Meshes.Where(m => odd.Any(o => m.NodeIndex == o.i || m.BoneIndices.Contains(o.i))).ToList();
            bool weighted = flver.Meshes.Any(m => m.Vertices.Any(v => v.BoneWeights[1] > 0 || (v.BoneIndices[0] != 0 && m.BoneIndices.Count > 1)));
            if (odd.Count == 0 && dynamic == 0) continue;
            Console.WriteLine($"{Path.GetFileName(file)}: {flver.Nodes.Count} nodes ({odd.Count} transformed), {flver.Meshes.Count} meshes ({dynamic} dynamic, {usesOdd.Count} on transformed nodes){(weighted ? ", per-vertex bones" : "")}");
            foreach (var (n, i) in odd.Take(6))
                Console.WriteLine($"   node {i} '{n.Name}' parent {n.ParentIndex} T=({n.Translation.X:0.00}, {n.Translation.Y:0.00}, {n.Translation.Z:0.00}) R=({n.Rotation.X:0.00}, {n.Rotation.Y:0.00}, {n.Rotation.Z:0.00})");
            foreach (var m in flver.Meshes.Where(m => m.Dynamic != 0 || m.BoneIndices.Count > 1).Take(4))
                Console.WriteLine($"   mesh mat '{flver.Materials[m.MaterialIndex].Name}' node {m.NodeIndex} dynamic {m.Dynamic} bones [{string.Join(",", m.BoneIndices.Take(8))}] verts {m.Vertices.Count}");
        }
        return 0;
    }
}
