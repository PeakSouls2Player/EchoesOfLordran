using System.Numerics;
using Lordran.Tools.Formats.Morpheme;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// <c>chr-inspect FILE [--rig NMB|ANIBND] [--dummies] [--meshes]</c> (session 8, track chr: the DS2 character archive
/// layout for the 1:1 DS1 character pipeline). FILE is a .flv/.flver, or a character BND (DS2 model/chr/cNNNN.bnd,
/// DS1 chrbnd(.dcx)): every FLVER inside is listed with its nodes (index, flags, name, parent, local TRS, bind-pose world
/// position), and with --rig the nodes are compared with a Morpheme rig by name and index (the rig's bind pose is in
/// Morpheme character space, Z up; FLVER space is Y up: the comparison maps it by (x, y, z) → (x, z, y)). Also lists
/// the archive's other files and, for Havok packfiles, their skeletons (bone names) so ragdoll/skeleton-mapper bone
/// lists can be checked against the rig.
/// </summary>
public static class ChrInspect
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: chr-inspect FILE [--rig NMB|ANIBND] [--dummies] [--meshes]"); return 1; }
        string path = args[1];
        string rigSrc = args.SkipWhile(a => a != "--rig").Skip(1).FirstOrDefault() ?? "";
        NmbRig? rig = null;
        if (rigSrc != "")
        {
            var nmbBytes = MorphemeTools.LoadFiles(rigSrc).First(f => f.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)).Bytes;
            var nmb = NmbFile.Read(nmbBytes);
            rig = NmbRig.Read(nmb.Bundles[nmb.IndexOf(1)].Data);
        }
        byte[] b = File.ReadAllBytes(path);
        if (DCX.Is(b)) b = DCX.Decompress(b);
        var files = new List<(string Name, byte[] Bytes)>();
        if (BND4.Is(b)) files.AddRange(BND4.Read(b).Files.Select(f => (f.Name, f.Bytes)));
        else if (BND3.Is(b)) files.AddRange(BND3.Read(b).Files.Select(f => (f.Name, f.Bytes)));
        else files.Add((Path.GetFileName(path), b));
        foreach (var (name, bytes) in files)
        {
            byte[] d = DCX.Is(bytes) ? DCX.Decompress(bytes) : bytes;
            Console.WriteLine($"== {name} ({d.Length} bytes)");
            if (FLVER2.Is(d)) Flver(FLVER2.Read(d), rig, args.Contains("--dummies"), args.Contains("--meshes"));
            else if (d.Length > 16 && BitConverter.ToUInt32(d, 0) == 0x57E0E057) Havok(d);
        }
        return 0;
    }

    static string V(Vector3 v) => $"({v.X,8:0.0000}, {v.Y,8:0.0000}, {v.Z,8:0.0000})";

    static void Flver(FLVER2 f, NmbRig? rig, bool dummies, bool meshes)
    {
        var w = ChrCompare.World(f);
        Console.WriteLine($"FLVER version 0x{f.Header.Version:X}, {f.Nodes.Count} nodes, {f.Meshes.Count} meshes, {f.Materials.Count} materials, {f.Dummies.Count} dummies, {f.BufferLayouts.Count} layouts, skeleton set {(f.Skeletons == null ? "none" : $"base {f.Skeletons.BaseSkeleton.Count} all {f.Skeletons.AllSkeletons.Count}")}");
        var rigWorld = rig != null ? RigWorld(rig) : null;
        for (int i = 0; i < f.Nodes.Count; i++)
        {
            var n = f.Nodes[i];
            string extra = "";
            if (rig != null)
            {
                int ri = rig.Names.FindIndex(x => x == n.Name);
                if (ri < 0) extra = "  rig: -";
                else
                {
                    var rp = rigWorld![ri];
                    var fp = new Vector3(rp.X, rp.Z, rp.Y);
                    extra = $"  rig {ri}{(ri == i ? "" : " (index differs)")} dist {Vector3.Distance(fp, w[i].Translation):0.0000}";
                }
            }
            Console.WriteLine($"  {i,3} {(int)n.Flags,2} {n.Name,-24} p{n.ParentIndex,3} c{n.FirstChildIndex,3} s{n.NextSiblingIndex,3} t{V(n.Translation)} r{V(n.Rotation)} s{V(n.Scale)} world{V(w[i].Translation)} bb{V(n.BoundingBoxMin)}..{V(n.BoundingBoxMax)}{extra}");
        }
        if (rig != null)
        {
            var names = f.Nodes.Select(n => n.Name).ToHashSet();
            Console.WriteLine($"  rig {rig.Names.Count} bones; rig bones without a FLVER node: [{string.Join(", ", rig.Names.Where(x => !names.Contains(x)))}]");
        }
        if (dummies)
            foreach (var d in f.Dummies)
                Console.WriteLine($"  dummy ref {d.ReferenceID,4} parent {d.ParentBoneIndex,3} attach {d.AttachBoneIndex,3} ({(d.AttachBoneIndex >= 0 && d.AttachBoneIndex < f.Nodes.Count ? f.Nodes[d.AttachBoneIndex].Name : "-")}) pos {V(d.Position)} fwd {V(d.Forward)} up {V(d.Upward)} useUp {d.UseUpwardVector} flag1 {d.Flag1}");
        if (meshes)
            for (int g = 0; g < f.GXLists.Count; g++)
                Console.WriteLine($"  gxlist {g}: " + string.Join(" | ", f.GXLists[g].Select(x => $"{x.ID} u{x.Unk04} [{Convert.ToHexString(x.Data)}]")));
        if (meshes)
            foreach (var m in f.Meshes)
            {
                var mat = f.Materials[m.MaterialIndex];
                Console.WriteLine($"  mesh mat '{mat.Name}' mtd {mat.MTD} gx {mat.GXIndex} verts {m.Vertices.Count} node {m.NodeIndex} dyn {m.Dynamic} useBW {m.UseBoneWeights} palette [{string.Join(",", m.BoneIndices)}] layout {m.VertexBuffers[0].LayoutIndex}: "
                    + string.Join(",", f.BufferLayouts[m.VertexBuffers[0].LayoutIndex].Select(x => x.Semantic + ":" + x.Type)));
            }
    }

    /// <summary>Bind-pose world positions of a Morpheme rig (Morpheme character space, Z up).</summary>
    public static Vector3[] RigWorld(NmbRig r)
    {
        var pos = new Vector3[r.Names.Count]; var rot = new Quaternion[r.Names.Count];
        for (int i = 0; i < r.Names.Count; i++)
        {
            var t = new Vector3(r.BindPos[i][0], r.BindPos[i][1], r.BindPos[i][2]);
            var q = new Quaternion(r.BindRot[i][0], r.BindRot[i][1], r.BindRot[i][2], r.BindRot[i][3]);
            int p = r.Parents[i];
            if (p < 0) { pos[i] = t; rot[i] = q; }
            else { pos[i] = pos[p] + Vector3.Transform(t, rot[p]); rot[i] = rot[p] * q; }
        }
        return pos;
    }

    static void Havok(byte[] d)
    {
        try
        {
            var hk = new Formats.Havok.HkPackfile(d);
            Console.WriteLine($"  Havok packfile {hk.ContentsVersion}: " + string.Join(", ", hk.Objects.GroupBy(o => o.Class).Select(g => $"{g.Key} x{g.Count()}")));
            foreach (var (off, cls) in hk.Objects.Where(o => o.Class == "hkaSkeleton"))
            {
                var s = hk.ReadObject(off);
                var bones = ((System.Collections.IEnumerable)s["bones"]!).Cast<Formats.Havok.HkPObj>().Select(x => x["name"] as string ?? "").ToList();
                Console.WriteLine($"  hkaSkeleton '{s["name"]}' {bones.Count} bones: {string.Join(", ", bones)}");
            }
        }
        catch (Exception e) { Console.WriteLine($"  (Havok read failed: {e.Message})"); }
    }
}
