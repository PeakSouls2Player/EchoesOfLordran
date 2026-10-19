using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// inspect FILE — prints the structure of a binder (BND3/BND4, recursing into DCX) and of every FLVER/TPF
/// inside it: materials with texture slots, vertex buffer layouts, dummies (hitbox/effect points), nodes.
/// </summary>
public static class Inspect
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: inspect FILE [text-id ...]"); return 1; }
        showVerts = args.Contains("--verts");
        textIds = args.Skip(2).Where(a => !a.StartsWith("--")).Select(int.Parse).ToHashSet();
        Describe(Path.GetFileName(args[1]), File.ReadAllBytes(args[1]), "");
        return 0;
    }

    static HashSet<int> textIds = new();
    static bool showVerts;

    static void Describe(string name, byte[] bytes, string indent)
    {
        if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);

        if (name.EndsWith(".fmg", StringComparison.OrdinalIgnoreCase))
        {
            var fmg = FMG.Read(bytes);
            Console.WriteLine($"{indent}FMG {name}: {fmg.Entries.Count} entries, ids {fmg.Entries.Min(e => e.ID)}..{fmg.Entries.Max(e => e.ID)}");
            foreach (var e in fmg.Entries.Where(e => textIds.Contains(e.ID)))
                Console.WriteLine($"{indent}  [{e.ID}] {e.Text?.Replace("\n", "\\n")}");
            return;
        }

        if (BND3.Is(bytes) || BND4.Is(bytes))
        {
            IBinder bnd = BND3.Is(bytes) ? BND3.Read(bytes) : BND4.Read(bytes);
            Console.WriteLine($"{indent}{name}: {(BND3.Is(bytes) ? "BND3" : "BND4")} with {bnd.Files.Count} files");
            foreach (var f in bnd.Files)
            {
                Console.WriteLine($"{indent}  [{f.ID}] {f.Name}  ({f.Bytes.Length:N0} bytes)");
                Describe(Path.GetFileName(f.Name), f.Bytes, indent + "    ");
            }
        }
        else if (FLVER2.Is(bytes))
        {
            var flver = FLVER2.Read(bytes);
            Console.WriteLine($"{indent}FLVER 0x{flver.Header.Version:X} unicode={flver.Header.Unicode} meshes={flver.Meshes.Count} nodes={flver.Nodes.Count} dummies={flver.Dummies.Count}");
            for (int i = 0; i < flver.Materials.Count; i++)
            {
                var m = flver.Materials[i];
                Console.WriteLine($"{indent}  material[{i}] '{m.Name}' mtd={m.MTD} gx={m.GXIndex} index={m.Index}");
                foreach (var t in m.Textures)
                    Console.WriteLine($"{indent}    {t.ParamName,-28} {t.Path}  tiling={t.TilingScale}");
            }
            for (int i = 0; i < flver.BufferLayouts.Count; i++)
                Console.WriteLine($"{indent}  layout[{i}] " + string.Join(" ", flver.BufferLayouts[i].Select(lm => $"{lm.Semantic}{lm.Index}:{lm.Type}")));
            Console.WriteLine($"{indent}  header bbox={flver.Header.BoundingBoxMin}..{flver.Header.BoundingBoxMax} unk5C={flver.Header.Unk5C} unk5D={flver.Header.Unk5D} unk68={flver.Header.Unk68}");
            foreach (var mesh in flver.Meshes)
            {
                Console.WriteLine($"{indent}  mesh mat={mesh.MaterialIndex} verts={mesh.Vertices.Count} buffers=[{string.Join(",", mesh.VertexBuffers.Select(b => b.LayoutIndex))}] facesets={mesh.FaceSets.Count} bones=[{string.Join(",", mesh.BoneIndices)}] node={mesh.NodeIndex} dynamic={mesh.Dynamic} bbox={mesh.BoundingBox?.Min}..{mesh.BoundingBox?.Max}");
                foreach (var fs in mesh.FaceSets)
                    Console.WriteLine($"{indent}    faceset flags={fs.Flags} strip={fs.TriangleStrip} cull={fs.CullBackfaces} unk06={fs.Unk06} indices={fs.Indices.Count}");
                if (showVerts)
                    foreach (var v in mesh.Vertices.Take(2))
                        Console.WriteLine($"{indent}    v pos={v.Position} n={v.Normal} nw={v.NormalW} t=[{string.Join(" ", v.Tangents)}] uvs=[{string.Join(" ", v.UVs)}] colors=[{string.Join(" ", v.Colors.Select(c => $"({c.R:0.##},{c.G:0.##},{c.B:0.##},{c.A:0.##})"))}] bi=[{v.BoneIndices[0]},{v.BoneIndices[1]},{v.BoneIndices[2]},{v.BoneIndices[3]}] bw=[{v.BoneWeights[0]:0.##},{v.BoneWeights[1]:0.##}]");
            }
            foreach (var d in flver.Dummies)
                Console.WriteLine($"{indent}  dummy ref={d.ReferenceID} parent={d.ParentBoneIndex} attach={d.AttachBoneIndex} pos=({d.Position.X:0.###},{d.Position.Y:0.###},{d.Position.Z:0.###})");
            foreach (var n in flver.Nodes)
                Console.WriteLine($"{indent}  node '{n.Name}' parent={n.ParentIndex} child={n.FirstChildIndex} t={n.Translation} r={n.Rotation} s={n.Scale} flags={n.Flags} bb={n.BoundingBoxMin}..{n.BoundingBoxMax}");
            for (int g = 0; g < flver.GXLists.Count; g++)
                Console.WriteLine($"{indent}  gxlist[{g}] " + string.Join(" ", flver.GXLists[g].Select(it => $"{it.ID}/{it.Unk04}/{BitConverter.ToString(it.Data)}")));
        }
        else if (TPF.Is(bytes))
        {
            var tpf = TPF.Read(bytes);
            Console.WriteLine($"{indent}TPF platform={tpf.Platform} encoding={tpf.Encoding} flag2={tpf.Flag2}");
            foreach (var t in tpf.Textures)
                Console.WriteLine($"{indent}  '{t.Name}' format={t.Format} type={t.Type} mipmaps={t.Mipmaps} flags1={t.Flags1} {t.Bytes.Length:N0} bytes dds={DdsSize(t.Bytes)}");
        }
    }

    static string DdsSize(byte[] dds) =>
        dds.Length > 128 && dds[0] == 'D' && dds[1] == 'D' && dds[2] == 'S'
            ? $"{BitConverter.ToInt32(dds, 16)}x{BitConverter.ToInt32(dds, 12)} fourcc='{System.Text.Encoding.ASCII.GetString(dds, 84, 4).TrimEnd('\0')}'"
            : "raw";
}
