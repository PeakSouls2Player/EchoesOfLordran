using Lordran.Tools.Build;
using Lordran.Tools.Build.Chr;
using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 8 (track npc), offline checks of converted human parts:
/// <list type="bullet">
/// <item><c>parts-check PACK_DIR|FILE.bnd...</c>: every part binder (model/parts/{head,body,arm,leg,face}/*.bnd under a
/// pack, or the given files): each mesh's palette names DS2 player-skeleton bones, weights sum to 1, triangle lists;
/// plus the rig frame check (<see cref="PlayerRig.FrameCheck"/>).</item>
/// <item><c>armor-test DS1ID... [--out DIR]</c>: ports DS1 armour pieces into a throwaway pack (nothing is installed)
/// and writes their part binders to DIR (default: the session scratch folder given), then checks them; prints the
/// rows it would add.</item>
/// </list>
/// </summary>
public static class PartsCheck
{
    public static int Run(string[] args)
    {
        var rig = new PlayerRig(Program.DefaultDsrRoot, ExtractDs2.DefaultOut);
        var frames = new List<string>();
        var (n, max, worst) = rig.FrameCheck(frames);
        Console.WriteLine($"rig frame check: {n} DS1 bones with a mapped child, max angle {max:0.00}° ({worst})");
        Console.WriteLine("  " + string.Join(", ", frames));
        var files = new List<string>();
        foreach (string a in args.Skip(1))
        {
            if (Directory.Exists(a))
                foreach (string folder in new[] { "head", "body", "arm", "leg", "face" })
                {
                    string d = Path.Combine(a, "gamedata", "model", "parts", folder);
                    if (Directory.Exists(d)) files.AddRange(Directory.GetFiles(d, "*.bnd"));
                }
            else files.Add(a);
        }
        int bad = 0;
        foreach (string f in files)
        {
            var bnd = BND4.Read(f);
            foreach (var file in bnd.Files.Where(x => x.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase)))
            {
                var flv = FLVER2.Read(file.Bytes);
                var problems = rig.Check(flv);
                int verts = flv.Meshes.Sum(m => m.Vertices.Count);
                var tex = bnd.Files.Count(x => x.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase));
                var maxW = flv.Meshes.SelectMany(m => m.Vertices).Select(v => Math.Abs(v.BoneWeights[0] + v.BoneWeights[1] + v.BoneWeights[2] + v.BoneWeights[3] - 1)).DefaultIfEmpty(0).Max();
                Console.WriteLine($"{Path.GetFileName(f)} {file.Name}: {flv.Meshes.Count} meshes, {verts} vertices, {flv.Materials.Count} materials, {tex} textures, palette bones {flv.Meshes.SelectMany(m => m.BoneIndices).Distinct().Count()}, max |Σw−1| {maxW:0.0000}: {(problems.Count == 0 ? "OK" : string.Join("; ", problems))}");
                if (problems.Count > 0) bad++;
            }
        }
        Console.WriteLine($"{files.Count} binders checked, {bad} with problems");
        return bad == 0 ? 0 : 1;
    }

    /// <summary>parts-view OUT_PREFIX FILE.bnd... — the FLVERs of several part binders (or .flv files) merged into one
    /// model and rendered front and side (flver-view: OUT_PREFIX-front.png, -side.png), to see a converted outfit on
    /// DS2's bind pose. Merging works because every part of ours (and DS2's full-skeleton parts) starts with the same
    /// player-skeleton nodes.</summary>
    public static int View(string[] args)
    {
        string prefix = args[1];
        FLVER2? merged = null;
        foreach (string path in args.Skip(2))
        {
            byte[] b = File.ReadAllBytes(path);
            var f = FLVER2.Is(b) ? FLVER2.Read(b) : FLVER2.Read(BND4.Read(b).Files.First(x => x.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase)).Bytes);
            if (merged == null) { merged = f; continue; }
            int matBase = merged.Materials.Count, layoutBase = merged.BufferLayouts.Count, gxBase = merged.GXLists.Count;
            foreach (var mat in f.Materials) if (mat.GXIndex >= 0) mat.GXIndex += gxBase;
            merged.GXLists.AddRange(f.GXLists);
            merged.Materials.AddRange(f.Materials);
            merged.BufferLayouts.AddRange(f.BufferLayouts);
            foreach (var m in f.Meshes)
            {
                m.MaterialIndex += matBase;
                foreach (var vb in m.VertexBuffers) vb.LayoutIndex += layoutBase;
                // palettes index this file's nodes: re-point by name into the merged node list
                m.BoneIndices = m.BoneIndices.Select(i => merged.Nodes.FindIndex(n => n.Name == f.Nodes[i].Name) is int j && j >= 0 ? j : 0).ToList();
                m.NodeIndex = 0;
                merged.Meshes.Add(m);
            }
        }
        if (merged == null) return 1;
        string tmp = Path.Combine(Path.GetTempPath(), $"parts-view-{Environment.ProcessId}.flv");
        merged.Write(tmp);
        FlverView.Run(["flver-view", tmp, prefix + "-front.png", "front"]);
        FlverView.Run(["flver-view", tmp, prefix + "-side.png", "side"]);
        File.Delete(tmp);
        return 0;
    }

    public static int ArmorTest(string[] args)
    {
        string? outDir = CliArgs.Option(args, "--out", out _);
        var ids = args.Skip(1).TakeWhile(a => !a.StartsWith("--")).Select(int.Parse).ToList();
        var ds1 = new Ds1Source(Program.DefaultDsrRoot);
        var ds2 = ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        var pack = new Pack(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        var rig = new PlayerRig(Program.DefaultDsrRoot, ExtractDs2.DefaultOut);
        var armour = new ArmorPorter(ds1, ds2, pack, rig);
        foreach (int id in ids)
        {
            var (item, how) = armour.Resolve(id);
            Console.WriteLine($"{id} {ds1.Text("Armor_name_", id)} → item {item} (ArmorParam {item - 10_000_000}): {how}");
        }
        armour.Flush();
        if (outDir != null)
            foreach (string p in armour.Parts.Concat(armour.Parts.Select(p => p.Replace(".bnd", "_l.bnd"))).Distinct())
            {
                if (pack.TryGetFile("gamedata", p["gamedata/".Length..]) is not { } bytes) continue;
                string target = Path.Combine(outDir, p.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
                Console.WriteLine($"  wrote {target} ({bytes.Length:N0} bytes)");
            }
        return outDir != null ? Run(["parts-check", outDir]) : 0;
    }
}
