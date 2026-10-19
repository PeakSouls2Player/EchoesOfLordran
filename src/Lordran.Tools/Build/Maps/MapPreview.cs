using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Visual test for the map-piece converter (session 3): every map piece of a DS1 map is converted and added to an
/// existing DS2 map, shifted so a chosen DS1 point lands on a chosen DS2 point. Default: the Undead Asylum overlaid on
/// Majula with the Asylum courtyard bonfire on The Far Fire, where the test save loads.
/// Writes the DS2 map's mapbhd/bdt (FLVERs, MTEs, ACB), tpfbhd/bdt (textures, also the LQ archive), gibhd/bdt (DS1
/// lightmaps as GI textures, HQ and LQ) and MSB (models + map piece parts).
/// </summary>
public static class MapPreview
{
    public sealed record Options(string Ds1Map, string Ds2Map, Vector3 Ds1Anchor, Vector3 Ds2Anchor, string ModelPrefix = "m9", double GiGain = 0.6);

    public static Options AsylumOnMajula => new("m18_01_00_00", "m10_04_00_00",
        Ds1Anchor: new Vector3(3.1f, 196.41f, 7.44f),     // DS1 courtyard bonfire (c1000_0000 / o0200_0000)
        Ds2Anchor: new Vector3(9.68f, 5.92f, -16.96f));  // The Far Fire (o00_0100_0000)

    public static void Build(Pack pack, string dsrRoot, Options o)
    {
        var materials = Ds2MapMaterials.Load(pack.Ds2Data);
        var conv = new MapPieceConverter(materials, o.Ds2Map);
        string mdir = Path.Combine(pack.Ds2Data, "model", "map");
        string tail = o.Ds2Map[1..];
        Vector3 offset = o.Ds2Anchor - o.Ds1Anchor;

        var msb1 = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", o.Ds1Map + ".msb"));
        var msb2 = MSB2.Read(Path.Combine(pack.Ds2Data, "map", o.Ds2Map, o.Ds2Map + ".msb"));
        var map = BXF4.Read(Path.Combine(mdir, o.Ds2Map + ".mapbhd"), Path.Combine(mdir, o.Ds2Map + ".mapbdt"));

        var flvFile = map.Files.First(f => f.Name.EndsWith(".flv.dcx", StringComparison.OrdinalIgnoreCase));
        var template = FLVER2.Read(DCX.Decompress(flvFile.Bytes, out DCX.CompressionInfo dcxType));
        var mteFile = map.Files.First(f => f.Name.EndsWith(".mte.dcx", StringComparison.OrdinalIgnoreCase));
        var acbFile = map.Files.First(f => f.Name.EndsWith(".acb", StringComparison.OrdinalIgnoreCase));
        var acb = ACB.Read(acbFile.Bytes);
        var acbModel = acb.Assets.OfType<ACB.Asset.Model>().First(m => m.RenderType == 0 && !m.DisableShadowSource);
        var acbGi = acb.Assets.OfType<ACB.Asset.GITexture>().First(g => g.AbsolutePath.Contains(@"\hq_"));
        int nextId = map.Files.Max(f => f.ID) + 1;
        string folder = Path.GetDirectoryName(flvFile.Name)!;

        // One DS2 model per DS1 map piece model.
        var modelNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string area = o.Ds1Map.Substring(1, 2);
        foreach (var model in msb1.Models.MapPieces)
        {
            string ds1File = Path.Combine(dsrRoot, "map", o.Ds1Map, $"{model.Name}A{area}.flver.dcx");
            if (!H2Merge.Exists(ds1File)) { pack.Note($"preview: {model.Name}: no {Path.GetFileName(ds1File)}"); continue; }
            string name = $"{o.ModelPrefix}{modelNames.Count:D3}";
            modelNames[model.Name] = name;
            var ds2 = conv.Convert(FLVER2.Read(DCX.Decompress(H2Merge.ReadAllBytes(ds1File))), name, template); // rule H2-B
            map.Files.Add(new BinderFile(flvFile.Flags, nextId++, $@"{folder}\{name}.flv.dcx", DCX.Compress(ds2.Write(), dcxType)));
            map.Files.Add(new BinderFile(mteFile.Flags, nextId++, $@"{folder}\{name}.mte.dcx", mteFile.Bytes));
            acb.Assets.Add(new ACB.Asset.Model
            {
                AbsolutePath = acbModel.AbsolutePath.Replace(Path.GetFileName(acbModel.AbsolutePath), name + ".sib"),
                RelativePath = $"model_sib/{name}.sib",
                Unk0A = acbModel.Unk0A,
                Members = new ACB.Asset.Model.MemberList(ds2.Materials.Select(m => new ACB.Asset.Model.Member { Text = m.Name, Unk04 = 0 })) { Unk00 = acbModel.Members?.Unk00 ?? -1 },
                DrawDistance = acbModel.DrawDistance, MeshLodRate = acbModel.MeshLodRate, Reflectible = acbModel.Reflectible,
                NormalInteraction = acbModel.NormalInteraction, Unk20 = acbModel.Unk20, RenderType = acbModel.RenderType,
                DisableShadowSource = acbModel.DisableShadowSource, DisableShadowTarget = acbModel.DisableShadowTarget,
                Unk27 = acbModel.Unk27, Unk28 = acbModel.Unk28, Unk2C = acbModel.Unk2C, FixToCamera = acbModel.FixToCamera,
                Unk2E = acbModel.Unk2E, LowTextureDistance = acbModel.LowTextureDistance, CheapRenderDistance = acbModel.CheapRenderDistance,
                Unk34 = acbModel.Unk34, Unk35 = acbModel.Unk35, Unk36 = acbModel.Unk36, Unk37 = acbModel.Unk37,
            });
            msb2.Models.MapPieces.Add(new MSB2.Model.MapPiece { Name = name });
        }

        // Parts: every DS1 map piece instance, shifted by the anchor offset. Draw/display groups are copied from the DS2
        // map's first map piece (its main terrain), so ours draw whenever that does; all four words matter (vanilla
        // pieces always set bit 3 of the last word).
        var groupsFrom = msb2.Parts.MapPieces[0];
        int partIndex = 0;
        foreach (var part in msb1.Parts.MapPieces)
        {
            if (!modelNames.TryGetValue(part.ModelName, out string? name)) continue;
            var p = new MSB2.Part.MapPiece
            {
                Name = $"{name}_{partIndex++:D4}",
                ModelName = name,
                Position = part.Position + offset,
                Rotation = part.Rotation,
                Scale = part.Scale,
                UnkT00 = -1,
            };
            for (int i = 0; i < 4; i++) { p.DrawGroups[i] = groupsFrom.DrawGroups[i]; p.DispGroups[i] = groupsFrom.DispGroups[i]; }
            msb2.Parts.MapPieces.Add(p);
        }

        // Collision: DS1 hXXXXBn / lXXXXBn (hi / lo) → DS2 h9n_XXXX / l9n_XXXX (CollisionConverter), MSB collision models and
        // parts (fields copied from the DS2 map's first collision part), ACB "model_hkt" entries.
        var hi = BXF4.Read(Path.Combine(mdir, $"h{tail}.hkxbhd"), Path.Combine(mdir, $"h{tail}.hkxbdt"));
        var lo = BXF4.Read(Path.Combine(mdir, $"l{tail}.hkxbhd"), Path.Combine(mdir, $"l{tail}.hkxbdt"));
        var hkxTemplateFile = hi.Files.First(f => f.Name.EndsWith(".hkx.dcx", StringComparison.OrdinalIgnoreCase));
        var hkxTemplate = new Formats.Havok.HkPackfile(DCX.Decompress(hkxTemplateFile.Bytes, out DCX.CompressionInfo hkxDcx));
        var acbHkt = acb.Assets.OfType<ACB.Asset.General>().First(g => g.RelativePath?.StartsWith("model_hkt/") == true);
        var colTemplate = msb2.Parts.Collisions[0];
        int hiId = hi.Files.Max(f => f.ID) + 1, loId = lo.Files.Max(f => f.ID) + 1;
        string ds1MapDir = Path.Combine(dsrRoot, "map", o.Ds1Map);
        var ds1Hi = BXF3.Read(Path.Combine(ds1MapDir, $"h{o.Ds1Map[1..]}.hkxbhd"), Path.Combine(ds1MapDir, $"h{o.Ds1Map[1..]}.hkxbdt"));
        var ds1Lo = BXF3.Read(Path.Combine(ds1MapDir, $"l{o.Ds1Map[1..]}.hkxbhd"), Path.Combine(ds1MapDir, $"l{o.Ds1Map[1..]}.hkxbdt"));
        static string Ds2ColName(string ds1Model, char kind) => $"{kind}9{ds1Model[6]}_{ds1Model.Substring(1, 4)}"; // h0005B1 → h91_0005
        var colModels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int colTris = 0, colParts = 0;
        foreach (var model in msb1.Models.Collisions)
        {
            string ds2Name = Ds2ColName(model.Name, 'h');
            bool added = false;
            foreach (var (bxf, src, kind) in new[] { (hi, ds1Hi, 'h'), (lo, ds1Lo, 'l') })
            {
                var f = src.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith($"{kind}{model.Name[1..]}A", StringComparison.OrdinalIgnoreCase));
                if (f == null) continue;
                string name = Ds2ColName(model.Name, kind);
                try
                {
                    var r = CollisionConverter.Convert(DCX.Decompress(f.Bytes), hkxTemplate, name);
                    bxf.Files.Add(new BinderFile(hkxTemplateFile.Flags, kind == 'h' ? hiId++ : loId++, $@"{folder}\{name}.hkx.dcx", DCX.Compress(r.Bytes, hkxDcx)));
                    if (kind == 'h') { colTris += r.Triangles; added = true; }
                    pack.Note($"collision {model.Name} ({kind}) → {name}: {r.Triangles} triangles, materials {r.Materials}");
                }
                catch (Exception e) { pack.Note($"collision {model.Name} ({kind}) FAILED: {e.Message}"); }
            }
            if (!added) continue;
            colModels[model.Name] = ds2Name;
            msb2.Models.Collisions.Add(new MSB2.Model.Collision { Name = ds2Name });
            acb.Assets.Add(new ACB.Asset.General { AbsolutePath = acbHkt.AbsolutePath.Replace(Path.GetFileName(acbHkt.AbsolutePath), ds2Name + ".hkt"), RelativePath = $"model_hkt/{ds2Name}.hkt" });
        }
        foreach (var part in msb1.Parts.Collisions)
        {
            if (!colModels.TryGetValue(part.ModelName, out string? name)) continue;
            var c = (MSB2.Part.Collision)colTemplate.DeepCopy();
            c.Name = $"{name}_{colParts++:D4}";
            c.ModelName = name;
            c.Position = part.Position + offset;
            c.Rotation = part.Rotation;
            c.Scale = Vector3.One;
            msb2.Parts.Collisions.Add(c);
        }

        // Textures.
        var ds1Tex = new Ds1MapTextures(dsrRoot, o.Ds1Map.Substring(0, 3));
        var tex = BXF4.Read(Path.Combine(mdir, $"t{tail}.tpfbhd"), Path.Combine(mdir, $"t{tail}.tpfbdt"));
        var gi = BXF4.Read(Path.Combine(mdir, $"g{tail}.gibhd"), Path.Combine(mdir, $"g{tail}.gibdt"));
        string lqDir = Path.Combine(Path.GetDirectoryName(pack.Ds2Data)!, "ds2-lq", "model_lq", "map");
        BXF4? texLq = File.Exists(Path.Combine(lqDir, $"t{tail}.tpfbhd")) ? BXF4.Read(Path.Combine(lqDir, $"t{tail}.tpfbhd"), Path.Combine(lqDir, $"t{tail}.tpfbdt")) : null;
        BXF4? giLq = File.Exists(Path.Combine(lqDir, $"g{tail}.gibhd")) ? BXF4.Read(Path.Combine(lqDir, $"g{tail}.gibhd"), Path.Combine(lqDir, $"g{tail}.gibdt")) : null;
        var texTemplate = tex.Files[0];
        DCX.Decompress(texTemplate.Bytes, out DCX.CompressionInfo texDcx);
        string texFolder = Path.GetDirectoryName(texTemplate.Name)!;
        int texId = tex.Files.Max(f => f.ID) + 1, giId = gi.Files.Max(f => f.ID) + 1;
        int texLqId = texLq?.Files.Max(f => f.ID) + 1 ?? 0, giLqId = giLq?.Files.Max(f => f.ID) + 1 ?? 0;
        int converted = 0;
        foreach (var req in conv.Textures.Values.OrderBy(r => r.Name))
        {
            bool isGi = req.Kind is MapPieceConverter.TexKind.Lightmap or MapPieceConverter.TexKind.GreyGi or MapPieceConverter.TexKind.DarkGi;
            var hq = MapPieceConverter.MakeTpf(req, ds1Tex.Dds, hqGi: true, o.GiGain);
            byte[] hqBytes = DCX.Compress(hq.Write(), texDcx);
            string file = $@"{texFolder}\{req.Name}.tpf.dcx";
            if (isGi)
            {
                gi.Files.Add(new BinderFile(texTemplate.Flags, giId++, file, hqBytes));
                acb.Assets.Add(new ACB.Asset.GITexture { AbsolutePath = acbGi.AbsolutePath.Replace(Path.GetFileName(acbGi.AbsolutePath), $"hq_{req.Name}.tga"), RelativePath = acbGi.RelativePath, Unk10 = acbGi.Unk10, Unk14 = acbGi.Unk14 });
                if (giLq != null)
                {
                    var lq = MapPieceConverter.MakeTpf(req, ds1Tex.Dds, hqGi: false, o.GiGain);
                    giLq.Files.Add(new BinderFile(texTemplate.Flags, giLqId++, file, DCX.Compress(lq.Write(), texDcx)));
                }
            }
            else
            {
                tex.Files.Add(new BinderFile(texTemplate.Flags, texId++, file, hqBytes));
                texLq?.Files.Add(new BinderFile(texTemplate.Flags, texLqId++, file, hqBytes));
                acb.Assets.Add(new ACB.Asset.Texture { AbsolutePath = $@"N:\FRPG2_64\data\model\map\{o.Ds2Map}\tex\{req.Name}.tga", RelativePath = $"tex/{req.Name}.tga" });
            }
            converted++;
        }
        acbFile.Bytes = acb.Write();

        void AddSplit(BXF4 bxf, string device, string dir, string bhdName, string bdtName)
        {
            bxf.Write(out byte[] bhd, out byte[] bdt);
            pack.AddFile(device, $"{dir}/{bhdName}", bhd);
            pack.AddFile(device, $"{dir}/{bdtName}", bdt);
        }
        AddSplit(map, "gamedata", "model/map", $"{o.Ds2Map}.mapbhd", $"{o.Ds2Map}.mapbdt");
        AddSplit(tex, "gamedata", "model/map", $"t{tail}.tpfbhd", $"t{tail}.tpfbdt");
        AddSplit(gi, "gamedata", "model/map", $"g{tail}.gibhd", $"g{tail}.gibdt");
        AddSplit(hi, "gamedata", "model/map", $"h{tail}.hkxbhd", $"h{tail}.hkxbdt");
        AddSplit(lo, "gamedata", "model/map", $"l{tail}.hkxbhd", $"l{tail}.hkxbdt");
        pack.Note($"preview: {colModels.Count} collision models, {colParts} collision parts, {colTris} triangles");
        Console.WriteLine($"map preview collision: {colModels.Count} models, {colParts} parts, {colTris:N0} triangles");
        if (texLq != null) AddSplit(texLq, "maplq", "model_lq/map", $"t{tail}.tpfbhd", $"t{tail}.tpfbdt");
        if (giLq != null) AddSplit(giLq, "maplq", "model_lq/map", $"g{tail}.gibhd", $"g{tail}.gibdt");
        pack.AddFile("gamedata", $"map/{o.Ds2Map}/{o.Ds2Map}.msb", msb2.Write());
        foreach (string n in conv.Notes) pack.Note(n);
        pack.Note($"preview: {modelNames.Count} models, {partIndex} parts, {converted} textures; offset {offset}");
        Console.WriteLine($"map preview: {o.Ds1Map} → {o.Ds2Map}: {modelNames.Count} models, {partIndex} parts, {converted} textures (offset {offset})");
    }
}

/// <summary>DS1 map textures by name, from map\mAA\*.tpfbhd (BXF3), loaded on first use.</summary>
public sealed class Ds1MapTextures
{
    readonly Dictionary<string, TPF.Texture> byName = new(StringComparer.OrdinalIgnoreCase);

    public Ds1MapTextures(string dsrRoot, string area)
    {
        foreach (string bhd in Directory.GetFiles(Path.Combine(dsrRoot, "map", area), "*.tpfbhd"))
            foreach (var f in BXF3.Read(bhd, Path.ChangeExtension(bhd, ".tpfbdt")).Files)
                foreach (var t in TPF.Read(DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes).Textures)
                    byName.TryAdd(t.Name, t);
        // plus loose TPFs (mAA_9999.tpf.dcx: textures shared by the area's objects, e.g. Firelink's urns)
        foreach (string tpf in Directory.GetFiles(Path.Combine(dsrRoot, "map", area), "*.tpf.dcx"))
            foreach (var t in TPF.Read(DCX.Decompress(File.ReadAllBytes(tpf))).Textures)
                byName.TryAdd(t.Name, t);
    }

    public byte[]? Dds(string name) => byName.TryGetValue(name, out var t) ? t.Bytes : null;
}
