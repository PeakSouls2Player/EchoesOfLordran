using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// chr-compare DS1.flver DS2.flv — two character skeletons side by side (for putting a DS1 mesh on a DS2 skeleton):
/// bones matched by name with their bind-pose world positions, unmatched bones, and per-model mesh/weight summary.
/// </summary>
public static class ChrCompare
{
    public static int Run(string[] args)
    {
        var a = FLVER2.Read(args[1]);
        var b = FLVER2.Read(args[2]);
        var wa = World(a); var wb = World(b);
        Console.WriteLine($"A {Path.GetFileName(args[1])}: {a.Nodes.Count} bones, {a.Meshes.Count} meshes, version {a.Header.Version:X}");
        Console.WriteLine($"B {Path.GetFileName(args[2])}: {b.Nodes.Count} bones, {b.Meshes.Count} meshes, version {b.Header.Version:X}");
        var byName = b.Nodes.Select((n, i) => (Name: Norm(n.Name), i)).GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.First().i);
        var usedA = UsedBones(a); var usedB = UsedBones(b);
        Console.WriteLine("-- A bones (* = skinned by A's meshes): name | A world pos | B world pos (same name) | distance");
        for (int i = 0; i < a.Nodes.Count; i++)
        {
            var n = a.Nodes[i];
            var pa = wa[i].Translation;
            string parent = n.ParentIndex >= 0 ? a.Nodes[n.ParentIndex].Name : "-";
            if (byName.TryGetValue(Norm(n.Name), out int j))
            {
                var pb = wb[j].Translation;
                string bp = b.Nodes[j].ParentIndex >= 0 ? b.Nodes[b.Nodes[j].ParentIndex].Name : "-";
                Console.WriteLine($"{(usedA.Contains(i) ? "*" : " ")} {i,2} {n.Name,-20} {V(pa)} | {(usedB.Contains(j) ? "*" : " ")} {j,2} {V(pb)} | {Vector3.Distance(pa, pb):0.000}{(Norm(bp) != Norm(parent) ? $"  PARENT A {parent} B {bp}" : "")}");
            }
            else
                Console.WriteLine($"{(usedA.Contains(i) ? "*" : " ")} {i,2} {n.Name,-20} {V(pa)} | (no bone in B)  parent {parent}");
        }
        var namesA = a.Nodes.Select(n => Norm(n.Name)).ToHashSet();
        Console.WriteLine("-- B bones missing in A: " + string.Join(", ", b.Nodes.Select((n, i) => (n, i)).Where(x => !namesA.Contains(Norm(x.n.Name))).Select(x => $"{(usedB.Contains(x.i) ? "*" : "")}{x.i}:{x.n.Name}{V(wb[x.i].Translation)}")));
        foreach (var (f, label) in new[] { (a, "A"), (b, "B") })
        {
            Console.WriteLine($"-- {label} meshes");
            foreach (var m in f.Meshes)
            {
                var mat = f.Materials[m.MaterialIndex];
                int maxW = m.Vertices.Count == 0 ? 0 : m.Vertices.Max(v => Enumerable.Range(0, 4).Count(k => v.BoneWeights[k] > 0));
                Console.WriteLine($"  mat '{mat.Name}' verts {m.Vertices.Count} faces {m.FaceSets.FirstOrDefault()?.Indices.Count / 3} boneIdx {m.BoneIndices.Count} default {m.NodeIndex} dynamic {m.Dynamic} maxWeights {maxW} layout {string.Join(",", f.BufferLayouts[m.VertexBuffers[0].LayoutIndex].Select(x => x.Semantic + ":" + x.Type))}");
            }
        }
        return 0;
    }

    /// <summary>Bind-pose world matrices of a FLVER's nodes (local TRS composed down the parent chain).</summary>
    public static Matrix4x4[] World(FLVER2 f)
    {
        var w = new Matrix4x4[f.Nodes.Count];
        var done = new bool[f.Nodes.Count];
        Matrix4x4 Get(int i)
        {
            if (done[i]) return w[i];
            var n = f.Nodes[i];
            var local = Matrix4x4.CreateScale(n.Scale) * Matrix4x4.CreateRotationX(n.Rotation.X) * Matrix4x4.CreateRotationZ(n.Rotation.Z)
                * Matrix4x4.CreateRotationY(n.Rotation.Y) * Matrix4x4.CreateTranslation(n.Translation);
            w[i] = n.ParentIndex >= 0 ? local * Get(n.ParentIndex) : local;
            done[i] = true;
            return w[i];
        }
        for (int i = 0; i < f.Nodes.Count; i++) Get(i);
        return w;
    }

    static HashSet<int> UsedBones(FLVER2 f)
    {
        var used = new HashSet<int>();
        foreach (var m in f.Meshes)
            foreach (var v in m.Vertices)
                for (int k = 0; k < 4; k++)
                    if (v.BoneWeights[k] > 0)
                        used.Add(m.BoneIndices.Count > 0 ? m.BoneIndices[v.BoneIndices[k]] : v.BoneIndices[k]);
        return used;
    }

    /// <summary>Bone name for matching across games: DS1 "L Clavicle" = DS2 "L_Clavicle".</summary>
    public static string Norm(string name) => name.Trim().Replace(' ', '_').ToLowerInvariant(); // DS1 c2900 has "Jaw "

    static string V(Vector3 v) => $"({v.X,6:0.000},{v.Y,6:0.000},{v.Z,6:0.000})";
}
