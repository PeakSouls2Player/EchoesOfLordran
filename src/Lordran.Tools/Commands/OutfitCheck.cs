using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (track outfit11): armour / NPC outfit evidence and checks.
/// <list type="bullet">
/// <item><c>outfit-survey FILE...</c>: per part binder/FLVER: GX lists, materials (name, MTD, GX index), meshes (material,
/// palette size, vertices): how DS1 and DS2 parts mark their culling / display groups. <c>--groups</c>: group usage over every
/// DS1 / DS2 part; <c>--flags</c>: DS1 invisibleFlags per slot and DS2 interfere bits per slot; <c>--culling-map</c>: writes
/// Mappings\armor-culling.tsv (rule AR8, <see cref="Build.Chr.ArmorCulling"/>).</item>
/// <item><c>outfit-sheet PACK OUTDIR</c>, <c>outfit-npcs PACK OUTDIR</c>: OutfitSheet.cs.</item>
/// </list>
/// </summary>
public static partial class OutfitCheck
{
    static int Ds1Group(string? materialName) => Build.Chr.ArmorCulling.Ds1Group(materialName);
    static int Ds2Group(FLVER2 f, FLVER2.Material m) => Build.Chr.ArmorCulling.Ds2Group(f, m);

    /// <summary>outfit-survey --groups: DS1 display groups per part prefix (DSR parts) and DS2 culling groups per parts
    /// folder (extracted DS2), with file counts.</summary>
    static int Groups()
    {
        var ds1 = new SortedDictionary<string, SortedDictionary<int, int>>();
        foreach (string p in Directory.GetFiles(Path.Combine(Program.DefaultDsrRoot, "parts"), "*.partsbnd.dcx"))
        {
            string prefix = Path.GetFileName(p)[..2] + (Path.GetFileName(p).Contains("_F_") ? "_F" : Path.GetFileName(p).Contains("_A_") ? "_A" : "_M");
            try
            {
                var bnd = BND3.Read(p);
                foreach (var bf in bnd.Files.Where(x => x.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)))
                    foreach (int g in FLVER2.Read(bf.Bytes).Materials.Select(m => Ds1Group(m.Name)).Where(g => g >= 0).Distinct())
                    {
                        var d = ds1.TryGetValue(prefix, out var x) ? x : ds1[prefix] = new();
                        d[g] = d.GetValueOrDefault(g) + 1;
                    }
            }
            catch { }
        }
        foreach (var (k, v) in ds1) Console.WriteLine($"DS1 {k}: " + string.Join(" ", v.Select(x => $"#{x.Key}Ã—{x.Value}")));
        foreach (string folder in new[] { "head", "body", "arm", "leg", "face" })
        {
            var d = new SortedDictionary<int, int>();
            foreach (string p in Directory.GetFiles(Path.Combine(ExtractDs2.DefaultOut, "model", "parts", folder), "*.bnd"))
            {
                if (p.EndsWith("_l.bnd")) continue;
                try
                {
                    foreach (var bf in BND4.Read(p).Files.Where(x => x.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase)))
                    {
                        var f = FLVER2.Read(bf.Bytes);
                        foreach (int g in f.Materials.Select(m => Ds2Group(f, m)).Distinct()) d[g] = d.GetValueOrDefault(g) + 1;
                    }
                }
                catch { }
            }
            Console.WriteLine($"DS2 {folder}: " + string.Join(" ", d.Select(x => $"{x.Key}Ã—{x.Value}")));
        }
        return 0;
    }

    /// <summary>DS1 EquipParamProtector invisibleFlag00-47 of a row (set flags).</summary>
    static List<int> Ds1Flags(PARAM.Row r) => Build.Chr.ArmorCulling.Ds1Flags(r);

    /// <summary>outfit-survey --flags: per DS1 slot, which invisibleFlags its pieces set (counts), the bare-body rows' flags,
    /// and per piece whose own parts carry a group its own flags hide.</summary>
    static int Flags()
    {
        var ds1 = new Build.Ds1Source(Program.DefaultDsrRoot);
        var rows = ds1.Params.Params["EquipParamProtector"].Rows.Where(r => r.ID % 1000 == 0).ToList();
        string[] slot = ["head", "body", "arms", "legs"];
        for (int s = 0; s < 4; s++)
        {
            var d = new SortedDictionary<int, int>();
            foreach (var r in rows.Where(r => r.ID / 1000 % 10 == s)) foreach (int f in Ds1Flags(r)) d[f] = d.GetValueOrDefault(f) + 1;
            Console.WriteLine($"DS1 {slot[s]} pieces ({rows.Count(r => r.ID / 1000 % 10 == s)}) set: " + string.Join(" ", d.Select(x => $"{x.Key}×{x.Value}")));
        }
        foreach (var r in rows.Where(r => r.ID is >= 900000 and < 904000 || r.ID is >= 950000 and < 954000))
            Console.WriteLine($"  {r.ID} {ds1.Text("Armor_name_", r.ID)}: flags {string.Join(",", Ds1Flags(r))} model {r["equipModelId"].Value}");
        // DS2: the culling bits vanilla interfere rows set, per armour slot of the ArmorParam rows pointing at them
        var ds2 = Formats.ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        var inter = ds2.Params["EquipInterfereParam"];
        foreach (int cat in new[] { 1, 2, 3, 4 })
            foreach (int sex in new[] { 0, 1 })
            {
                var d = new SortedDictionary<int, int>();
                var ids = ds2.Params["ArmorParam"].Rows.Where(a => Convert.ToInt32(a["armorCategory"].Value) == cat).Select(a => Convert.ToInt32(a["interfereId"].Value)).Distinct();
                int n = 0;
                foreach (int id in ids)
                {
                    if (inter[id] is not { } r) continue;
                    n++;
                    for (int g = 0; g < 64; g++) if (Convert.ToInt32(r[$"cullingGroup{g:D2}_{sex}"].Value) != 0) d[g] = d.GetValueOrDefault(g) + 1;
                }
                Console.WriteLine($"DS2 slot {cat} sex {sex}: {n} rows set " + string.Join(" ", d.Select(x => $"{x.Key}×{x.Value}")));
            }
        return 0;
    }

    public static int Survey(string[] args)
    {
        if (args.Contains("--groups")) return Groups();
        if (args.Contains("--flags")) return Flags();
        if (args.Contains("--culling-map"))
        {
            // writes the build's copy and the source copy (Mappings\armor-culling.tsv next to the project)
            string src = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Mappings", "armor-culling.tsv"));
            var log = Build.Chr.ArmorCulling.Compute(new Build.Ds1Source(Program.DefaultDsrRoot), Formats.ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut), ExtractDs2.DefaultOut, src);
            File.Copy(src, Build.Chr.ArmorCulling.MapPath, true);
            foreach (string l in log) Console.WriteLine(l);
            Console.WriteLine($"wrote {src}");
            return 0;
        }
        foreach (string path in args.Skip(1))
        {
            byte[] b = File.ReadAllBytes(path);
            if (DCX.Is(b)) b = DCX.Decompress(b);
            IEnumerable<BinderFile> files = FLVER2.Is(b) ? [new BinderFile(Binder.FileFlags.Flag1, 0, Path.GetFileName(path), b)] : BND4.Is(b) ? BND4.Read(b).Files : BND3.Read(b).Files;
            foreach (var bf in files.Where(x => x.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase) || x.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)))
            {
                var f = FLVER2.Read(bf.Bytes);
                Console.WriteLine($"{Path.GetFileName(path)} / {bf.Name}: {f.Nodes.Count} nodes, {f.Meshes.Count} meshes, {f.Materials.Count} materials, {f.GXLists.Count} GX lists");
                for (int g = 0; g < f.GXLists.Count; g++)
                    Console.WriteLine($"  GX{g}: " + string.Join(" ", f.GXLists[g].Select(i => $"{i.ID}/{i.Unk04}[{i.Data.Length}:{Convert.ToHexString(i.Data.Take(16).ToArray())}]")));
                for (int i = 0; i < f.Materials.Count; i++)
                {
                    var m = f.Materials[i];
                    Console.WriteLine($"  mat{i} '{m.Name}' {Path.GetFileName(m.MTD)} gx {m.GXIndex} idx {m.Index} tex {string.Join(",", m.Textures.Select(t => Path.GetFileNameWithoutExtension(t.Path)))}");
                }
                for (int i = 0; i < f.Meshes.Count; i++)
                {
                    var m = f.Meshes[i];
                    Console.WriteLine($"  mesh{i} mat{m.MaterialIndex} node {m.NodeIndex} ({(m.NodeIndex >= 0 && m.NodeIndex < f.Nodes.Count ? f.Nodes[m.NodeIndex].Name : "-")}) palette {m.BoneIndices.Count} verts {m.Vertices.Count} dyn {m.Dynamic} facesets {string.Join(",", m.FaceSets.Select(fs => $"{fs.Flags}:{fs.Indices.Count}"))}");
                }
            }
        }
        return 0;
    }
}
