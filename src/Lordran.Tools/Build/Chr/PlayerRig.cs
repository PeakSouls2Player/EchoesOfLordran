using System.Numerics;
using Lordran.Tools.Commands;
using SoulsFormats;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// DS1's player rig (c0000) → DS2's player rig (c0001): the shared re-binding behind every DS1 model DS2 draws on a
/// human body (session 8, track npc): armour parts (<see cref="ArmorPorter"/>), and whole DS1 characters built on the
/// player skeleton (Anastacia c2750: a human-rig chr whose mesh is DS1's Fire Keeper parts, <see cref="NpcPorter"/>).
/// <para>Evidence (`chr-compare c0000.flver c0001.flv`, `inspect`): both rigs share the bone names of DS1's biped
/// ("L Clavicle" style names are already "L_Clavicle" in both player rigs), DS1 binds in a T-pose (L_Hand at
/// (0.684, 1.391, -0.033)), DS2 in an A-pose (L_Hand (0.541, 1.029, 0.031)), DS1's skeleton sits 5.4 cm further back
/// (Pelvis z -0.054 vs 0). DS2 has bones DS1 lacks (L/R_Finger3/4, L/R_Elbow, L/R_Shoulder, L/R_Knee, Root, Character);
/// DS1 has bones DS2 lacks (Lower_Root, Upper_Root, *_Wepon_Case, *_Shoulderpad, L/R_Po, *Nub, L/R_Shield): those move
/// with their nearest DS1 ancestor that DS2 has.</para>
/// <para>Rule: a vertex skinned to DS1 bone b moves rigidly with b from DS1's bind pose into DS2's,
/// p' = Σ wᵢ · p · W1[bᵢ]⁻¹ · W2[m(bᵢ)] (W = bind-pose world matrices, m = the name map), normals and tangents by the
/// rotation part. Unlike ChrPorter's direction-only re-binding this keeps each bone's twist, which needs both rigs'
/// bone frames to agree: <see cref="FrameCheck"/> measures it (angle between DS1's bone→child direction carried by
/// W1⁻¹·W2 and DS2's own; session 8, `parts-check`: 30 bones, 0.00–0.03° except the thumbs' root 10.3° (DS2's thumb
/// rests further splayed), Spine1→Neck 3.9° and the toes 2.2–2.3°: the frames agree, those are rest-pose differences).
/// Seen in software renders (`parts-view`): DS1's Chain and Elite Cleric sets sit on DS2's A-pose like DS2's own Elite
/// Knight set.</para>
/// <para>DS2 part FLVERs (`inspect model/parts/*/??_1060_m.bnd`): the player skeleton's nodes (all or a subset, matched
/// by name; bd_1060_m has the full 66: Character … R_Knee) then the part's own model node; meshes are dynamic with a
/// bone palette of skeleton node indices, NormalW 0, triangle lists (faceset Unk06 = palette size), vertex colours
/// beyond the first carry per-vertex data (filled with the template's means).</para>
/// </summary>
public sealed class PlayerRig
{
    /// <summary>DS2's player skeleton nodes (from the full-skeleton body part bd_1060_m: Character … R_Knee, 66 nodes).</summary>
    public IReadOnlyList<FLVER.Node> Ds2Skeleton { get; }
    readonly Matrix4x4[] w2;
    readonly Dictionary<string, int> ds2ByName;
    readonly FLVER2 ds1Rig;
    readonly Matrix4x4[] w1;
    readonly Dictionary<string, int> ds1ByName;

    /// <summary>DS2 body part with the whole player skeleton (no cloth needed: only its nodes are read).</summary>
    public const string Ds2SkeletonPart = "body/bd_1060_m.bnd";

    public PlayerRig(string dsrRoot, string ds2Data)
        : this(FLVER2.Read(BND3.Read(Path.Combine(dsrRoot, "chr", "c0000.chrbnd.dcx")).Files.First(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes),
               Ds2Nodes(ds2Data))
    { }

    PlayerRig(FLVER2 ds1Skeleton, List<FLVER.Node> ds2Nodes)
    {
        ds1Rig = ds1Skeleton;
        w1 = ChrCompare.World(ds1Rig);
        ds1ByName = ds1Rig.Nodes.Select((n, i) => (Name: ChrCompare.Norm(n.Name), i)).GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.First().i);
        var skel = new FLVER2 { Nodes = ds2Nodes };
        Ds2Skeleton = skel.Nodes;
        w2 = ChrCompare.World(skel);
        ds2ByName = skel.Nodes.Select((n, i) => (Name: ChrCompare.Norm(n.Name), i)).GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.First().i);
    }

    static List<FLVER.Node> Ds2Nodes(string ds2Data)
    {
        var part = FLVER2.Read(BND4.Read(Path.Combine(ds2Data, "model", "parts", Ds2SkeletonPart)).Files.First(f => f.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase)).Bytes);
        int end = part.Nodes.FindIndex(n => n.Name.StartsWith("BD_", StringComparison.OrdinalIgnoreCase));
        return part.Nodes.Take(end).ToList();
    }

    /// <summary>
    /// The same DS2 target with another DS1 skeleton: a DS1 character that has its own human rig (chr FLVERs carry their
    /// skeleton with its hierarchy, parts don't). Anastacia's c2750 binds where c0000 does (L_Hand 0.683/1.385 vs
    /// 0.684/1.391); the Undead Merchant c2510 is hunched (Pelvis y 0.945, Neck 1.515): with c0000's matrices his
    /// vertices would move as if he were built on the player's bind pose.
    /// </summary>
    public PlayerRig WithDs1Skeleton(FLVER2 ds1Skeleton) => new(ds1Skeleton, Ds2Skeleton.ToList());

    /// <summary>DS1 rig bone → DS2 skeleton node index (by name, else the nearest DS1 ancestor DS2 has; Pelvis last).</summary>
    public int Map(string ds1Bone) => Anchor(ds1Bone).Ds2;

    /// <summary>
    /// The DS1 rig bone a DS1 bone moves with (itself when DS2 has its name, else its nearest DS1 ancestor DS2 has) and
    /// that bone's DS2 node. A vertex on L_Shoulderpad moves rigidly with L_UpperArm: using the shoulder pad's own DS1
    /// frame against the upper arm's DS2 frame would turn it by the difference of two unrelated frames.
    /// </summary>
    (int Ds1, int Ds2) Anchor(string ds1Bone)
    {
        string name = ChrCompare.Norm(ds1Bone);
        ds1ByName.TryGetValue(name, out int b);
        if (ds2ByName.TryGetValue(name, out int direct)) return (ds1ByName.ContainsKey(name) ? b : -1, direct);
        if (ds1ByName.ContainsKey(name))
            for (int x = ds1Rig.Nodes[b].ParentIndex; x >= 0; x = ds1Rig.Nodes[x].ParentIndex)
                if (ds2ByName.TryGetValue(ChrCompare.Norm(ds1Rig.Nodes[x].Name), out int m)) return (x, m);
        return (ds1ByName["pelvis"], ds2ByName["pelvis"]);
    }

    /// <summary>The DS1 → DS2 bind-pose transform of a DS1 rig bone (row vectors: p' = p · M): W1[a]⁻¹ · W2[m(a)] of its
    /// anchor bone a. Identity for a name neither rig's DS1 side knows (part model nodes carry no vertices).</summary>
    public Matrix4x4 Rebind(string ds1Bone)
    {
        var (a, m) = Anchor(ds1Bone);
        if (a < 0) return Matrix4x4.Identity;
        Matrix4x4.Invert(w1[a], out var inv);
        return inv * w2[m];
    }

    /// <summary>
    /// How well the rigs' bone frames agree: for every DS1 bone with a single mapped child, the angle between the
    /// child direction carried by W1⁻¹·W2 and DS2's own child direction (degrees). Large values would mean a frame
    /// convention difference (twist would come out wrong), not just a pose difference.
    /// </summary>
    public (int Bones, double MaxDeg, string Worst) FrameCheck(List<string>? lines = null)
    {
        double max = 0; string worst = ""; int n = 0;
        for (int b = 0; b < ds1Rig.Nodes.Count; b++)
        {
            if (!ds2ByName.TryGetValue(ChrCompare.Norm(ds1Rig.Nodes[b].Name), out int m)) continue;
            int c = ds1Rig.Nodes[b].FirstChildIndex;
            if (c < 0 || !ds2ByName.TryGetValue(ChrCompare.Norm(ds1Rig.Nodes[c].Name), out int mc)) continue;
            var d1 = w1[c].Translation - w1[b].Translation;
            var d2 = w2[mc].Translation - w2[m].Translation;
            if (d1.Length() < 1e-4f || d2.Length() < 1e-4f) continue;
            var carried = Vector3.TransformNormal(d1, Rebind(ds1Rig.Nodes[b].Name));
            double deg = Math.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(carried), Vector3.Normalize(d2)), -1, 1)) * 180 / Math.PI;
            n++;
            lines?.Add($"{ds1Rig.Nodes[b].Name}→{ds1Rig.Nodes[c].Name} {deg:0.00}°");
            if (deg > max) { max = deg; worst = $"{ds1Rig.Nodes[b].Name}→{ds1Rig.Nodes[c].Name}"; }
        }
        return (n, max, worst);
    }

    /// <summary>A DS2 material kind for a DS1 part material (MTD name), and DS2's MTD for it.</summary>
    public enum Kind { Armour, Alpha, Skin }

    public static Kind KindOf(FLVER2.Material m)
    {
        string mtd = Path.GetFileNameWithoutExtension(m.MTD);
        if (mtd.StartsWith("Ps_Body", StringComparison.OrdinalIgnoreCase) || mtd.Contains("Skin", StringComparison.OrdinalIgnoreCase)) return Kind.Skin;
        if (mtd.Contains("_Edge", StringComparison.OrdinalIgnoreCase) || mtd.Contains("Alp", StringComparison.OrdinalIgnoreCase)) return Kind.Alpha;
        return Kind.Armour;
    }

    /// <summary>DS2's own MTDs for the kinds (from bd_1060_m and am_1001_m).</summary>
    public static string Mtd(Kind k) => k switch
    {
        Kind.Skin => @"N:\FRPG2\data\Material\mtd\common\Human[Skin][AO_0].mtd",
        Kind.Alpha => @"N:\FRPG2\data\Material\mtd\parts\P[Ibl][DSB]_Alp_1bit.mtd",
        _ => @"N:\FRPG2\data\Material\mtd\parts\P[Ibl][DSB].mtd",
    };

    /// <summary>One converted mesh: DS1 mesh re-bound, its DS2 palette and vertices (layout decided by the kind).</summary>
    public sealed record Mesh(int Ds1Material, Kind Kind, List<int> Palette, List<FLVER.Vertex> Vertices, List<int> Triangles, bool Cull);

    /// <summary>
    /// The DS1 meshes of a player-rig FLVER (a part, or a whole human-rig character) re-bound to DS2's skeleton.
    /// <paramref name="keep"/> filters DS1 meshes (by material); vertex colours/UVs/tangents are completed to the
    /// counts of the kind's DS2 layout with <paramref name="fill"/> (template means).
    /// </summary>
    public List<Mesh> Convert(FLVER2 src, Func<FLVER2.Mesh, FLVER2.Material, bool>? keep, Func<Kind, (int Colors, int Uvs, int Tangents, FLVER.VertexColor[] Fill)> layout,
        out string report)
    {
        var result = new List<Mesh>();
        // per DS1 node: the rebind matrix (the part's own nodes carry the names; their transforms are not needed)
        var rebind = src.Nodes.Select(n => Rebind(n.Name)).ToArray();
        var target = src.Nodes.Select(n => Map(n.Name)).ToArray();
        int verts = 0;
        foreach (var sm in src.Meshes)
        {
            var mat = src.Materials[sm.MaterialIndex];
            if (keep != null && !keep(sm, mat)) continue;
            var kind = KindOf(mat);
            var (nColors, nUvs, nTangents, fill) = layout(kind);
            var palette = new List<int>();
            int Slot(int node) { int i = palette.IndexOf(node); if (i < 0) { palette.Add(node); i = palette.Count - 1; } return i; }
            var outVerts = new List<FLVER.Vertex>(sm.Vertices.Count);
            foreach (var v in sm.Vertices)
            {
                var infl = new List<(int Node, float W)>();
                for (int k = 0; k < 4; k++)
                {
                    float w = v.BoneWeights[k];
                    if (w <= 0) continue;
                    infl.Add((sm.BoneIndices.Count > 0 ? sm.BoneIndices[v.BoneIndices[k]] : v.BoneIndices[k], w));
                }
                // rigid DS1 vertices (no weights): bound to their first palette bone
                if (infl.Count == 0) infl.Add((sm.BoneIndices.Count > 0 ? sm.BoneIndices[v.BoneIndices[0]] : sm.NodeIndex, 1f));
                float sum = infl.Sum(x => x.W);
                Vector3 pos = Vector3.Zero, nrm = Vector3.Zero, tan = Vector3.Zero;
                var dst = new Dictionary<int, float>();
                foreach (var (node, w0) in infl)
                {
                    float w = w0 / sum;
                    var M = rebind[node];
                    pos += w * Vector3.Transform(v.Position, M);
                    nrm += w * Vector3.TransformNormal(v.Normal, M);
                    if (v.Tangents.Count > 0) tan += w * Vector3.TransformNormal(new Vector3(v.Tangents[0].X, v.Tangents[0].Y, v.Tangents[0].Z), M);
                    dst[target[node]] = dst.GetValueOrDefault(target[node]) + w;
                }
                var nv = new FLVER.Vertex(v) { Position = pos, Normal = nrm.LengthSquared() > 1e-12f ? Vector3.Normalize(nrm) : Vector3.UnitY, NormalW = 0 };
                float tw = v.Tangents.Count > 0 ? v.Tangents[0].W : 1;
                var t3 = tan.LengthSquared() > 1e-12f ? Vector3.Normalize(tan) : Vector3.UnitX;
                nv.Tangents = Enumerable.Range(0, nTangents).Select(_ => new Vector4(t3, tw)).ToList();
                var uv0 = v.UVs.Count > 0 ? v.UVs[0] : Vector3.Zero;
                nv.UVs = Enumerable.Range(0, nUvs).Select(i => i < v.UVs.Count ? v.UVs[i] : uv0).ToList();
                var c0 = v.Colors.Count > 0 ? v.Colors[0] : new FLVER.VertexColor(1f, 1f, 1f, 1f);
                nv.Colors = Enumerable.Range(0, nColors).Select(i => i == 0 ? c0 : fill[Math.Min(i, fill.Length - 1)]).ToList();
                var top = dst.OrderByDescending(x => x.Value).Take(4).ToList();
                float ts = top.Sum(x => x.Value);
                nv.BoneIndices = new FLVER.VertexBoneIndices();
                nv.BoneWeights = new FLVER.VertexBoneWeights();
                for (int k = 0; k < top.Count; k++) { nv.BoneIndices[k] = Slot(top[k].Key); nv.BoneWeights[k] = top[k].Value / ts; }
                outVerts.Add(nv);
            }
            var fs = sm.FaceSets.FirstOrDefault(f => f.Flags == FLVER2.FaceSet.FSFlags.None) ?? sm.FaceSets[0];
            var tris = fs.TriangleStrip ? fs.Triangulate(allowPrimitiveRestarts: true) : new List<int>(fs.Indices);
            result.Add(new Mesh(sm.MaterialIndex, kind, palette, outVerts, tris, fs.CullBackfaces));
            verts += outVerts.Count;
        }
        report = $"{result.Count} meshes, {verts} vertices";
        return result;
    }

    /// <summary>
    /// Offline checks of a DS2 part/character FLVER (session 8, `parts-check`): every palette bone of every mesh names a
    /// node of DS2's player skeleton, every vertex's weights sum to 1 (UByte4Norm: within 4/255), palette indices in
    /// range, facesets are triangle lists with whole triangles.
    /// </summary>
    public List<string> Check(FLVER2 f)
    {
        var problems = new List<string>();
        var names = Ds2Skeleton.Select(n => ChrCompare.Norm(n.Name)).ToHashSet();
        for (int i = 0; i < f.Meshes.Count; i++)
        {
            var m = f.Meshes[i];
            foreach (int b in m.BoneIndices)
                if (b < 0 || b >= f.Nodes.Count || !names.Contains(ChrCompare.Norm(f.Nodes[b].Name)))
                    problems.Add($"mesh {i}: palette bone {b} ({(b >= 0 && b < f.Nodes.Count ? f.Nodes[b].Name : "out of range")}) is not a DS2 player-skeleton bone");
            int bad = 0, badIdx = 0;
            foreach (var v in m.Vertices)
            {
                float s = v.BoneWeights[0] + v.BoneWeights[1] + v.BoneWeights[2] + v.BoneWeights[3];
                if (Math.Abs(s - 1) > 4f / 255) bad++;
                for (int k = 0; k < 4; k++) if (v.BoneWeights[k] > 0 && v.BoneIndices[k] >= m.BoneIndices.Count) badIdx++;
            }
            if (bad > 0) problems.Add($"mesh {i}: {bad}/{m.Vertices.Count} vertices' weights don't sum to 1");
            if (badIdx > 0) problems.Add($"mesh {i}: {badIdx} weighted bone indices past the palette ({m.BoneIndices.Count})");
            foreach (var fs in m.FaceSets)
            {
                if (fs.TriangleStrip) problems.Add($"mesh {i}: triangle strip (DS2 parts use lists)");
                else if (fs.Indices.Count % 3 != 0) problems.Add($"mesh {i}: {fs.Indices.Count} indices is not whole triangles");
                if (fs.Indices.Any(x => x >= m.Vertices.Count)) problems.Add($"mesh {i}: face index past the vertices");
            }
        }
        return problems;
    }
}
