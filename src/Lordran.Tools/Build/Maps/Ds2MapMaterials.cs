using System.Text.Json;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// How DS2's own map pieces use each map MTD: the material's texture slots (with DS2's default paths and tiling), the
/// vertex layout its meshes use, the GX list, and per-vertex conventions (second vertex colour, normal W). Collected
/// once from every DS2 map FLVER (extracted data) and cached as JSON under data\lordran\cache.
/// </summary>
public sealed class Ds2MapMaterials
{
    public sealed class Entry
    {
        public string Mtd { get; set; } = "";
        public List<TexSlot> Textures { get; set; } = new();
        public List<Member> Layout { get; set; } = new();
        public List<Gx> GxList { get; set; } = new();
        public int GxTerminatorId { get; set; } = int.MaxValue;
        public int GxTerminatorLength { get; set; }
        public float[] Vc1 { get; set; } = { 1, 1, 1, 1 }; // RGBA of the second vertex colour
        public int NormalW { get; set; }
        public short FaceSetUnk06 { get; set; } = 1;
        public int Uses { get; set; }
        public string Example { get; set; } = "";
    }

    public sealed record TexSlot(string Param, string Path, float ScaleX, float ScaleY);
    public sealed record Member(string Semantic, string Type, int Index);
    public sealed record Gx(string Id, int Unk04, string Data);

    public Dictionary<string, Entry> ByMtd { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public const string CachePath = @"C:\EchoesOfLordran\data\lordran\cache\ds2-map-materials.json";

    public static Ds2MapMaterials Load(string ds2Data)
    {
        if (File.Exists(CachePath))
        {
            var cached = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(CachePath))!;
            return new Ds2MapMaterials { ByMtd = new(cached, StringComparer.OrdinalIgnoreCase) };
        }
        var result = Scan(ds2Data);
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        File.WriteAllText(CachePath, JsonSerializer.Serialize(result.ByMtd.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value),
            new JsonSerializerOptions { WriteIndented = true }));
        return result;
    }

    static Ds2MapMaterials Scan(string ds2Data)
    {
        var result = new Ds2MapMaterials();
        string dir = Path.Combine(ds2Data, "model", "map");
        foreach (string bhd in Directory.GetFiles(dir, "m*.mapbhd").OrderBy(p => p))
        {
            var bxf = BXF4.Read(bhd, Path.ChangeExtension(bhd, ".mapbdt"));
            foreach (var file in bxf.Files.Where(f => f.Name.EndsWith(".flv.dcx", StringComparison.OrdinalIgnoreCase)))
            {
                FLVER2 flver;
                try { flver = FLVER2.Read(DCX.Decompress(file.Bytes)); }
                catch { continue; }
                foreach (var mesh in flver.Meshes)
                {
                    var mat = flver.Materials[mesh.MaterialIndex];
                    string key = System.IO.Path.GetFileName(mat.MTD);
                    if (result.ByMtd.TryGetValue(key, out var known)) { known.Uses++; continue; }
                    if (mesh.VertexBuffers.Count != 1 || mesh.Vertices.Count == 0) continue;
                    var v = mesh.Vertices[0];
                    result.ByMtd[key] = new Entry
                    {
                        Mtd = mat.MTD,
                        Textures = mat.Textures.Select(t => new TexSlot(t.ParamName, t.Path, t.TilingScale.X, t.TilingScale.Y)).ToList(),
                        Layout = flver.BufferLayouts[mesh.VertexBuffers[0].LayoutIndex].Select(m => new Member(m.Semantic.ToString(), m.Type.ToString(), m.Index)).ToList(),
                        GxList = mat.GXIndex >= 0 && mat.GXIndex < flver.GXLists.Count
                            ? flver.GXLists[mat.GXIndex].Select(g => new Gx(g.ID, g.Unk04, Convert.ToBase64String(g.Data))).ToList() : new(),
                        GxTerminatorId = mat.GXIndex >= 0 && mat.GXIndex < flver.GXLists.Count ? flver.GXLists[mat.GXIndex].TerminatorID : int.MaxValue,
                        GxTerminatorLength = mat.GXIndex >= 0 && mat.GXIndex < flver.GXLists.Count ? flver.GXLists[mat.GXIndex].TerminatorLength : 0,
                        Vc1 = v.Colors.Count > 1 ? new[] { v.Colors[1].R, v.Colors[1].G, v.Colors[1].B, v.Colors[1].A } : new float[] { 1, 1, 1, 1 },
                        NormalW = v.NormalWs.Count > 0 ? v.NormalW : 0,
                        FaceSetUnk06 = mesh.FaceSets.Count > 0 ? mesh.FaceSets[0].Unk06 : (short)1,
                        Uses = 1,
                        Example = $"{Path.GetFileName(bhd)}:{Path.GetFileName(file.Name)}",
                    };
                }
            }
        }
        return result;
    }

    public FLVER2.BufferLayout Layout(Entry e)
    {
        var layout = new FLVER2.BufferLayout();
        foreach (var m in e.Layout)
            layout.Add(new FLVER.LayoutMember(Enum.Parse<FLVER.LayoutType>(m.Type), Enum.Parse<FLVER.LayoutSemantic>(m.Semantic), m.Index));
        return layout;
    }

    /// <summary>The skinned twin DS2 writes after each static layout (bone indices + weights after the tangents).</summary>
    public FLVER2.BufferLayout SkinnedTwin(FLVER2.BufferLayout layout)
    {
        var twin = new FLVER2.BufferLayout();
        bool inserted = false;
        for (int i = 0; i < layout.Count; i++)
        {
            twin.Add(new FLVER.LayoutMember(layout[i]));
            bool lastTangent = (layout[i].Semantic is FLVER.LayoutSemantic.Tangent or FLVER.LayoutSemantic.Normal) &&
                               (i + 1 >= layout.Count || layout[i + 1].Semantic is not (FLVER.LayoutSemantic.Tangent));
            if (!inserted && lastTangent && layout[i].Semantic == FLVER.LayoutSemantic.Tangent ||
                !inserted && layout[i].Semantic == FLVER.LayoutSemantic.Normal && !layout.Any(m => m.Semantic == FLVER.LayoutSemantic.Tangent))
            {
                twin.Add(new FLVER.LayoutMember(FLVER.LayoutType.UByte4, FLVER.LayoutSemantic.BoneIndices, 0));
                twin.Add(new FLVER.LayoutMember(FLVER.LayoutType.UByte4Norm, FLVER.LayoutSemantic.BoneWeights, 0));
                inserted = true;
            }
        }
        return twin;
    }

    public FLVER2.GXList GxList(Entry e)
    {
        var list = new FLVER2.GXList { TerminatorID = e.GxTerminatorId, TerminatorLength = e.GxTerminatorLength };
        foreach (var g in e.GxList) list.Add(new FLVER2.GXItem(g.Id, g.Unk04, Convert.FromBase64String(g.Data)));
        return list;
    }
}
