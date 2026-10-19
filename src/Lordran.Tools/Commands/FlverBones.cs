using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>flver-bones FILE.flv — per mesh: its bone list, node, dynamic flag and a histogram of the vertices' first
/// bone index (weight). Session 7: do non-dynamic DS2 object meshes index the mesh bone list or the nodes directly?</summary>
public static class FlverBones
{
    public static int Run(string[] args)
    {
        byte[] b = File.ReadAllBytes(args[1]);
        if (DCX.Is(b)) b = DCX.Decompress(b);
        var f = FLVER2.Read(b);
        // --nodes (session 8, track potroll): the node list (index, name, parent, translation, flags), e.g. to check
        // which nodes a breakable's fragment model has next to its fragment bodies (DS2 binds bodies to nodes by name)
        if (args.Contains("--nodes"))
            for (int i = 0; i < f.Nodes.Count; i++)
            {
                var n = f.Nodes[i];
                Console.WriteLine($"node {i} {n.Name} parent={n.ParentIndex} t=({n.Translation.X:0.###},{n.Translation.Y:0.###},{n.Translation.Z:0.###}) flags={n.Flags}");
            }
        if (args.Contains("--nodes"))
            foreach (var d in f.Dummies)
                Console.WriteLine($"dummy {d.ReferenceID} parent={d.ParentBoneIndex} attach={d.AttachBoneIndex} pos=({d.Position.X:0.###},{d.Position.Y:0.###},{d.Position.Z:0.###})");
        for (int i = 0; i < f.Meshes.Count; i++)
        {
            var m = f.Meshes[i];
            var hist = m.Vertices.GroupBy(v => (v.BoneIndices[0], v.BoneWeights[0])).OrderBy(g => g.Key.Item1).Select(g => $"{g.Key.Item1}(w{g.Key.Item2:0.##})x{g.Count()}");
            Console.WriteLine($"mesh {i} bones=[{string.Join(",", m.BoneIndices)}] node={m.NodeIndex} dynamic={m.Dynamic}: {string.Join(" ", hist)}; normalW: {string.Join(" ", m.Vertices.GroupBy(v => v.NormalW).OrderBy(g => g.Key).Select(g => $"{g.Key}x{g.Count()}"))}");
            if (m.Vertices.Count > 0)
            {
                var lo = m.Vertices.Aggregate(new System.Numerics.Vector3(float.MaxValue), (a, v) => System.Numerics.Vector3.Min(a, v.Position));
                var hi = m.Vertices.Aggregate(new System.Numerics.Vector3(float.MinValue), (a, v) => System.Numerics.Vector3.Max(a, v.Position));
                Console.WriteLine($"   bounds ({lo.X:0.###}, {lo.Y:0.###}, {lo.Z:0.###})..({hi.X:0.###}, {hi.Y:0.###}, {hi.Z:0.###})  node {(m.NodeIndex >= 0 && m.NodeIndex < f.Nodes.Count ? f.Nodes[m.NodeIndex].Name : "?")}");
            }
            for (int c = 0; c < (m.Vertices.Count > 0 ? m.Vertices[0].Colors.Count : 0); c++)
                Console.WriteLine($"   color{c} mean RGBA = ({m.Vertices.Average(v => v.Colors[c].R):0.##}, {m.Vertices.Average(v => v.Colors[c].G):0.##}, {m.Vertices.Average(v => v.Colors[c].B):0.##}, {m.Vertices.Average(v => v.Colors[c].A):0.##})");
        }
        return 0;
    }
}
