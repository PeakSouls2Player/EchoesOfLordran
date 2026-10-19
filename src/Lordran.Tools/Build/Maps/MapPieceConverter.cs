using System.Numerics;
using System.Text.RegularExpressions;
using Lordran.Tools.Textures;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// DS1 (DSR, FLVER 0x2000C) map piece → DS2 (FLVER 0x20010) map piece, plus the textures it references.
///  - Materials: the DS1 MTD's channels ([DSB]…), multi-blend ([M]/[ML]), lightmap ([L]) and alpha (_Alp/_Edge) pick a
///    DS2 map MTD that DS2's own map pieces use (<see cref="Ds2MapMaterials"/>). DS1's baked lightmap goes into DS2's
///    g_GITexture slot (same shader slot: uv=3, index 6); see <see cref="Ds2Tex.GiFromLightmap"/>.
///  - Vertices: written in the DS2 material's layout (DS1 UVs: [diffuse, (diffuse 2), lightmap]).
///  - Faces: LOD0 triangle strips → lists (DS2 map pieces have one face set).
/// Texture names get a "l_" prefix so they can't collide with DS2's.
/// </summary>
public sealed class MapPieceConverter
{
    public enum TexKind { Color, Normal, Lightmap, FlatNormal, Black, Specular, GreyGi, Ds2, Sky, Baked, DarkGi }

    /// <summary>Extracted DS2 game data (data\ds2): source of <see cref="TexKind.Ds2"/> textures.</summary>
    public static string? Ds2Data { get; set; }

    /// <summary>DS1's MTDs (colour multiplier, description, blend mode; session 8 track skylight, <see cref="Ds1Mtd"/>).
    /// Null: unlit materials keep (1,1,1) and no material is treated as a sky.</summary>
    public static Ds1Mtd? Ds1Mtds { get; set; }

    /// <summary>Session 8 (track skylight): DS1 sky domes → DS2 SkyDome2[NoFog] with a baked RGBM texture (<see cref="Sky"/>,
    /// rules S1/S3). Null for objects and previews (then skies stay NoLit[D] as before).</summary>
    public Sky.Context? SkyContext { get; init; }

    /// <summary>What each converted model turned out to be (MapBuilder picks the ACB settings from it: rules S1/S2).</summary>
    public sealed record ModelKind(bool HasSky, bool AllUnlit, float Extent);
    public Dictionary<string, ModelKind> Kinds { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// DS1 water ([We] MTDs: A13_Water_lake in the Asylum) carries only a wave normal map; DS1's water shader made the
    /// rest. It becomes DS2's translucent map water (Things Betwixt's Water_L[DSB]_Alp) with DS1's normal map and DS2's
    /// river diffuse/specular (session 6: as a lit [D] material with no diffuse it rendered as a black floor).
    /// </summary>
    const string WaterMtd = "Water_L[DSB]_Alp.mtd";
    static readonly Dictionary<string, string> WaterDs2Tex = new()
    {
        ["g_DiffuseTexture"] = "t10_02_00_00/river_00_d",
        ["g_SpecularTexture"] = "t10_02_00_00/river_00_s",
    };
    static bool IsWater(string ds1Mtd) => Regex.IsMatch(Path.GetFileName(ds1Mtd.Replace('\\', '/')), @"Water|\[We\]", RegexOptions.IgnoreCase);
    /// <summary>A texture to make. <paramref name="Bake"/>: the DS1 sky look baked into a <see cref="TexKind.Sky"/> texture.</summary>
    public sealed record TexRequest(string Name, string Ds1Name, TexKind Kind, Sky.Bake? Bake = null);

    /// <summary>DS1's own "no lighting" marker: the channel bracket ends in 'n' ([Dn], [DSBn]) or the MTD is a sky.</summary>
    public static bool IsUnlit(string ds1Mtd)
    {
        string n = Path.GetFileNameWithoutExtension(ds1Mtd.Replace('\\', '/'));
        var m = Regex.Match(n, @"\[([^\]]*)\]");
        return m.Success && m.Groups[1].Value.EndsWith('n') || n.Contains("Sky", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Rule S1: whether this DS1 material becomes DS2's sky material (a DS1 sky MTD with a diffuse texture, in map
    /// mode with a sky context, DS2's SkyDome2[NoFog] available). The same test <see cref="Convert"/> uses.</summary>
    public bool IsSkyMaterial(FLVER2.Material sm)
    {
        if (ObjectMode || SkyContext == null || !materials.ByMtd.ContainsKey(Sky.SkyMtd)) return false;
        if (IsWater(sm.MTD) && materials.ByMtd.ContainsKey(WaterMtd)) return false;
        string diffuse = Path.GetFileNameWithoutExtension(sm.Textures.FirstOrDefault(t => t.ParamName == "g_Diffuse")?.Path?.Replace('\\', '/') ?? "");
        return diffuse.Length > 0 && Ds1Mtd.IsSky(Ds1Mtds?.Get(sm.MTD), sm.MTD);
    }

    /// <summary>
    /// <b>Rule S1c</b> (session 11, atmos11): a DS1 sky material is a sky dome only when its meshes span at least
    /// <see cref="Sky.BackgroundExtent"/> (DS1's domes: 5.5–5.8 km). DS1 also leaves scraps of its sky/shaft/cloud
    /// materials inside ordinary pieces (the Burg's m2063B1: three flat meshes of 14 vertices, 2.1 × 0.7 m, at y −39 under
    /// the Lower Burg); rule S1b split them into a "2 m sky dome" with DS2's sky ACB and DS1's ×7 sky multiplier. Below the
    /// threshold the material stays an ordinary unlit material (rule S3), as DS1 draws it.
    /// </summary>
    public bool IsSkyMaterial(FLVER2 src, int mi)
    {
        if (!IsSkyMaterial(src.Materials[mi])) return false;
        var pts = src.Meshes.Where(m => m.MaterialIndex == mi).SelectMany(m => m.Vertices).Select(v => v.Position).Where(Sane).ToList();
        if (pts.Count == 0) return false;
        var d = pts.Aggregate(Vector3.Max) - pts.Aggregate(Vector3.Min);
        return MathF.Max(d.X, MathF.Max(d.Y, d.Z)) >= Sky.BackgroundExtent;
    }

    /// <summary>
    /// Rule S1b (session-8 skylight fix pass): the meshes of the given DS1 materials as a FLVER of their own, materials in
    /// the given order (re-indexed) and meshes sorted by it (stable: DS1's order within a material). MapBuilder uses it to
    /// give a DS1 sky material its own model, dome first, like every DS2 sky model (validation\sky-ds2-all.txt: all 16
    /// RenderType-2 skies hold only SkyDome/SkyDome2/NoLit materials, the dome as material 0), and to keep DS1's LIT
    /// meshes of the same model (Firelink m9000B2: ground l_m10_base_floor_ground_02_02 GI_M[D] and clouds
    /// A10_02_m9000_cloud[DB]_Alp beside the dome) in an ordinary map-piece model with shadows and lighting.
    /// </summary>
    public static FLVER2 Subset(FLVER2 src, IReadOnlyList<int> materialOrder)
    {
        var remap = materialOrder.Select((m, i) => (m, i)).ToDictionary(x => x.m, x => x.i);
        var meshes = src.Meshes.Where(m => remap.ContainsKey(m.MaterialIndex)).OrderBy(m => remap[m.MaterialIndex])
            .Select(m => new FLVER2.Mesh
            {
                MaterialIndex = remap[m.MaterialIndex], NodeIndex = m.NodeIndex, BoneIndices = m.BoneIndices, Dynamic = m.Dynamic,
                VertexBuffers = m.VertexBuffers, FaceSets = m.FaceSets, Vertices = m.Vertices, BoundingBox = m.BoundingBox,
            }).ToList();
        return new FLVER2
        {
            Header = src.Header, Dummies = src.Dummies, Materials = materialOrder.Select(i => src.Materials[i]).ToList(), GXLists = src.GXLists,
            Nodes = src.Nodes, Meshes = meshes, BufferLayouts = src.BufferLayouts,
        };
    }

    readonly Ds2MapMaterials materials;
    readonly string mapId;
    public Dictionary<string, TexRequest> Textures { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Notes { get; } = new();

    /// <summary>Session 11 (burg11, play-test fix), rule GI2: DS1 lights its lightmap-less pieces (the aqueduct walls, M[DB]
    /// without [L]) with the zone's lights and real-time shadows, so under a roof they are dark; DS2 gives them our constant
    /// GI stand-in everywhere (gi_grey 72: "no lighting inside the aqueduct"). With <see cref="Covered"/> (world point →
    /// map geometry above it) such a mesh's triangles under cover take the dark stand-in (gi_dark, this level).</summary>
    public const byte CoveredGiGrey = 18;
    /// <summary>Rule GI2: whether a world point lies under the map's geometry (null: rule off).</summary>
    public Func<Vector3, bool>? Covered { get; set; }
    /// <summary>Rule GI2: the model's placement (world transform) the cover test uses.</summary>
    public Matrix4x4 Placement { get; set; } = Matrix4x4.Identity;
    readonly Dictionary<int, int> coveredTwin = [];
    /// <summary>Rule GI2: triangles placed under cover (centroid) and how many meshes were split / darkened, per model.</summary>
    public int CoveredTriangles, CoveredMeshes;

    /// <summary>Rule GI2: moves the mesh's covered triangles into a copy on the dark twin material (the mesh itself keeps the
    /// open ones; all covered → the mesh just takes the twin). Returns the new mesh, or null.</summary>
    FLVER2.Mesh? SplitCovered(FLVER2.Mesh mesh, int twin)
    {
        var idx = mesh.FaceSets[0].Indices;
        var open = new List<int>(); var covered = new List<int>();
        for (int t = 0; t + 2 < idx.Count; t += 3)
        {
            var c = (mesh.Vertices[idx[t]].Positions[0] + mesh.Vertices[idx[t + 1]].Positions[0] + mesh.Vertices[idx[t + 2]].Positions[0]) / 3;
            (Covered!(Vector3.Transform(c, Placement)) ? covered : open).AddRange([idx[t], idx[t + 1], idx[t + 2]]);
        }
        if (covered.Count == 0) return null;
        CoveredTriangles += covered.Count / 3; CoveredMeshes++;
        if (open.Count == 0) { mesh.MaterialIndex = twin; return null; }
        FLVER2.Mesh Sub(List<int> tris, int material)
        {
            var remap = new Dictionary<int, int>();
            var verts = new List<FLVER.Vertex>();
            var ni = tris.Select(i => { if (!remap.TryGetValue(i, out int n)) { n = verts.Count; remap[i] = n; verts.Add(new FLVER.Vertex(mesh.Vertices[i])); } return n; }).ToList();
            var fs0 = mesh.FaceSets[0];
            return new FLVER2.Mesh
            {
                MaterialIndex = material, NodeIndex = mesh.NodeIndex, BoneIndices = mesh.BoneIndices.ToList(), Dynamic = mesh.Dynamic,
                VertexBuffers = mesh.VertexBuffers.Select(v => new FLVER2.VertexBuffer(v.LayoutIndex)).ToList(),
                FaceSets = [new FLVER2.FaceSet(fs0.Flags, false, fs0.CullBackfaces, fs0.Unk06, ni)],
                Vertices = verts,
            };
        }
        var dark = Sub(covered, twin);
        var lit = Sub(open, mesh.MaterialIndex);
        mesh.Vertices = lit.Vertices; mesh.FaceSets = lit.FaceSets;
        return dark;
    }

    /// <summary>DS2's map FLVERs (version 0x20010) store UVs as shorts / 2048: ±16 (vanilla maxima are exactly 16.0); DS1's
    /// are shorts / 1024, ±32.</summary>
    public const float Ds2UvLimit = 32767f / 2048f;

    /// <summary>
    /// Session 11 (burg11, play-test fix), rule UV1: a DS1 UV beyond DS2's ±16 wrapped in the 16-bit write (the Burg's walls
    /// "one stretched texture", the aqueduct corridor's "glitched" textures: 250 / 82 DS1 UV channels of m10_01 / m10_02 reach
    /// 16..32). Map textures repeat, so a whole-number shift samples the same texels: per UV channel the mesh is first moved
    /// by one integer offset (u and v apart) to centre its range; a channel still too wide gets a per-triangle offset (the
    /// triangle's centre to ≈ 0), its vertices duplicated where triangles need different offsets. Lightmap UVs (0..1) never
    /// move. A triangle wider than ±16 by itself is clamped (reported). Returns whether anything changed.
    /// </summary>
    public static bool FitUvs(FLVER2.Mesh mesh, out string note)
    {
        note = "";
        if (mesh.Vertices.Count == 0 || mesh.Vertices[0].UVs.Count == 0) return false;
        const float lim = Ds2UvLimit - 0.01f;
        int channels = mesh.Vertices[0].UVs.Count;
        bool Out(int k) => mesh.Vertices.Any(v => MathF.Abs(v.UVs[k].X) > lim || MathF.Abs(v.UVs[k].Y) > lim);
        var bad = Enumerable.Range(0, channels).Where(Out).ToList();
        if (bad.Count == 0) return false;
        var notes = new List<string>();
        // 1. one integer offset per channel and axis
        foreach (int k in bad)
        {
            float minU = mesh.Vertices.Min(v => v.UVs[k].X), maxU = mesh.Vertices.Max(v => v.UVs[k].X);
            float minV = mesh.Vertices.Min(v => v.UVs[k].Y), maxV = mesh.Vertices.Max(v => v.UVs[k].Y);
            float du = -MathF.Round((minU + maxU) / 2), dv = -MathF.Round((minV + maxV) / 2);
            if (du == 0 && dv == 0) continue;
            foreach (var v in mesh.Vertices) v.UVs[k] = new System.Numerics.Vector3(v.UVs[k].X + du, v.UVs[k].Y + dv, v.UVs[k].Z);
            notes.Add($"UV{k} shifted by ({du}, {dv})");
        }
        // 2. per triangle where the mesh's range is still wider than DS2's
        var still = bad.Where(Out).ToList();
        int dup = 0, clamped = 0;
        if (still.Count > 0 && mesh.FaceSets.Count > 0)
        {
            var fs = mesh.FaceSets[0];
            var idx = fs.Indices;
            var verts = mesh.Vertices;
            var map = new Dictionary<(int, string), int>();
            for (int t = 0; t + 2 < idx.Count; t += 3)
            {
                var shifts = new (float U, float V)[channels];
                foreach (int k in still)
                {
                    var c = (verts[idx[t]].UVs[k] + verts[idx[t + 1]].UVs[k] + verts[idx[t + 2]].UVs[k]) / 3;
                    shifts[k] = (-MathF.Round(c.X), -MathF.Round(c.Y));
                }
                string key = string.Join(";", still.Select(k => $"{shifts[k].U},{shifts[k].V}"));
                for (int j = 0; j < 3; j++)
                {
                    int vi = idx[t + j];
                    if (still.All(k => shifts[k] == (0f, 0f))) continue;
                    if (!map.TryGetValue((vi, key), out int ni))
                    {
                        var nv = new FLVER.Vertex(verts[vi]);
                        foreach (int k in still)
                        {
                            float u = nv.UVs[k].X + shifts[k].U, w = nv.UVs[k].Y + shifts[k].V;
                            if (MathF.Abs(u) > lim || MathF.Abs(w) > lim) clamped++;
                            nv.UVs[k] = new System.Numerics.Vector3(Math.Clamp(u, -lim, lim), Math.Clamp(w, -lim, lim), nv.UVs[k].Z);
                        }
                        ni = verts.Count; verts.Add(nv); dup++;
                        map[(vi, key)] = ni;
                    }
                    idx[t + j] = ni;
                }
            }
            // drop the vertices no triangle uses any more (the originals of shifted copies)
            var used = new Dictionary<int, int>();
            var kept = new List<FLVER.Vertex>();
            for (int i = 0; i < idx.Count; i++)
            {
                if (!used.TryGetValue(idx[i], out int ni)) { ni = kept.Count; used[idx[i]] = ni; kept.Add(verts[idx[i]]); }
                idx[i] = ni;
            }
            dup -= verts.Count - kept.Count;
            mesh.Vertices = verts = kept;
            // a triangle wider than DS2's range by itself (huge water planes): clamped
            foreach (var v in verts) foreach (int k in still)
                if (MathF.Abs(v.UVs[k].X) > lim || MathF.Abs(v.UVs[k].Y) > lim)
                { v.UVs[k] = new System.Numerics.Vector3(Math.Clamp(v.UVs[k].X, -lim, lim), Math.Clamp(v.UVs[k].Y, -lim, lim), v.UVs[k].Z); clamped++; }
            notes.Add($"UV{string.Join("/", still)} per-triangle offsets ({dup:+0;-0;0} vertices{(clamped > 0 ? $", {clamped} clamped (a triangle wider than ±16 itself)" : "")})");
            if (verts.Count >= ushort.MaxValue) notes.Add($"WARNING {verts.Count} vertices: past 16-bit indices");
        }
        note = "rule UV1: " + string.Join(", ", notes);
        return true;
    }
    public const string Prefix = "l_";
    /// <summary>Texture name prefix: "l_" for maps; objects use their own name (no texture name shared between owners).</summary>
    public string TexPrefix { get; init; } = Prefix;

    /// <summary>
    /// Object mode (session 6, <see cref="ObjPorter"/>): materials without GI (objects move; DS2's door uses M[DB]) and
    /// vertices left in their nodes' space (the object's skeleton and animations place them).
    /// </summary>
    public bool ObjectMode { get; init; }

    /// <summary>GI materials (map pieces). Objects use DS2's plain object materials (M[DB]: o00_0302, ladders o00_09xx);
    /// a static DS1 ladder converted with GI materials did not render (run s6-12).</summary>
    public bool Gi { get; init; } = true;

    public MapPieceConverter(Ds2MapMaterials materials, string ds2MapId)
    {
        this.materials = materials;
        mapId = ds2MapId;
    }

    static readonly Dictionary<string, (string Ds1, TexKind Kind)> SlotMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["g_DiffuseTexture"] = ("g_Diffuse", TexKind.Color),
        ["g_DiffuseTexture2"] = ("g_Diffuse_2", TexKind.Color),
        ["g_SpecularTexture"] = ("g_Specular", TexKind.Specular),
        ["g_SpecularTexture2"] = ("g_Specular_2", TexKind.Specular),
        ["g_BumpmapTexture"] = ("g_Bumpmap", TexKind.Normal),
        ["g_BumpmapTexture2"] = ("g_Bumpmap_2", TexKind.Normal),
        ["g_GITexture"] = ("g_Lightmap", TexKind.Lightmap),
    };

    /// <summary>Picks the DS2 MTD for a DS1 one. Returns the catalogue key (file name).</summary>
    public string MapMtd(string ds1Mtd, bool hasLightmap, Ds1Mtd.Info? info = null)
    {
        string n = Path.GetFileNameWithoutExtension(ds1Mtd.Replace('\\', '/'));
        var brackets = Regex.Matches(n, @"\[([^\]]*)\]").Select(m => m.Groups[1].Value).ToList();
        string channels = brackets.FirstOrDefault() ?? "D";
        bool multi = brackets.Skip(1).Any(b => b.Contains('M'));
        bool noLight = channels.EndsWith('n') || n.Contains("Sky", StringComparison.OrdinalIgnoreCase);
        bool alpha = n.Contains("_Alp", StringComparison.OrdinalIgnoreCase) || n.Contains("_Add", StringComparison.OrdinalIgnoreCase);
        bool edge = n.Contains("_Edge", StringComparison.OrdinalIgnoreCase);
        string letters = new(channels.Where(c => c is 'D' or 'S' or 'B').ToArray());
        if (letters.Length == 0) letters = "D";
        string sfx = alpha ? "_Alp" : edge ? "_Alp_1bit" : "";

        var candidates = new List<string>();
        // session 8 (skylight): DS1 additive unlit materials (light shafts A10_BG_shaft[Dn]_Add, g_BlendMode 4) → DS2's
        // additive NoLit[D]_Add (25 DS2 map pieces use it, e.g. the Moon on m10_16's sky); as _Alp they were alpha-blended
        // white sheets
        // session 11 (atmos11), rule S3e: additive only when DS1's own g_BlendMode says so (4, or 36 = 4 + light scattering);
        // DS1 names several alpha-blended (g_BlendMode 2) materials "_Add": A10_cloud[Dn]_Add, A10_BG_cloud[Dn]_Add, A03/A04
        // shafts, A11/A17/M99 clouds (mtd-dump of DSR Mtd.mtdbnd). As DS2 additives they glowed instead of veiling (the
        // Burg sky's cloud layer: 2.4× brighter than DS1 offline, atmos-check render)
        bool additive = info != null ? (info.BlendMode & ~32) == 4 : n.Contains("_Add", StringComparison.OrdinalIgnoreCase);
        if (noLight && additive) candidates.Add("NoLit[D]_Add");
        if (noLight)
            candidates.AddRange(new[] { $"NoLit[D]{sfx}", "NoLit[D]_Alp_1bit", "NoLit[D]" });
        foreach (string root in hasLightmap ? new[] { "GI_M", "M" } : new[] { "M", "GI_M" })
            foreach (string l in new[] { letters, letters.Contains('B') ? letters : letters + "B", "DSB", "DB", "D" }.Distinct())
            {
                if (multi) { candidates.Add($"{root}[{l}][M]{sfx}"); candidates.Add($"{root}[{l}][M]"); }
                candidates.Add($"{root}[{l}]{sfx}");
                if (edge) candidates.Add($"{root}[{l}]_Edge");
            }
        foreach (string c in candidates)
            if (materials.ByMtd.ContainsKey(c + ".mtd")) return c + ".mtd";
        throw new InvalidOperationException($"no DS2 MTD for {n}");
    }

    string TexPath(string name, bool gi) => gi
        ? $@"n:\temp\windows64\map\{mapId}\tex\{name}.tga"
        : $@"N:\FRPG2\data\Model\{(IsObject ? "obj" : "map")}\{mapId}\tex\{name}.tga";

    /// <summary>Converting a DS2 object (mapId = the object's name, oAA_BBBB): its materials point into Model\obj\NAME\tex
    /// like vanilla objects (session 7: with Model\map\ the hall and cell doors rendered black).</summary>
    bool IsObject => mapId.StartsWith('o');

    string Request(string ds1Name, TexKind kind)
    {
        string name = kind switch
        {
            TexKind.FlatNormal => TexPrefix + "flat_n",
            TexKind.Black => TexPrefix + "black",
            TexKind.GreyGi => TexPrefix + "gi_grey",
            TexKind.DarkGi => TexPrefix + "gi_dark",
            TexKind.Ds2 => TexPrefix + "ds2_" + ds1Name[(ds1Name.IndexOf('/') + 1)..],
            _ => TexPrefix + ds1Name,
        };
        // session 11 (walk11), rule TX2: one DS1 texture used in two roles (the Asylum's m18_AO / m18_Lit_Black: a lightmap
        // slot in one material, the diffuse slot in another) needs one DS2 texture per role (archive and format differ:
        // GI → g archive, colour/specular → t archive DXT1/5, normal → DXT5nm). Before, the first request won: the texture
        // went into the GI archive only and the diffuse slots pointed at a t-archive file that didn't exist (missing
        // texture on m0010/m0050/m0062/m0100 and the sky = glitched pieces; walk-check FAIL texture). The first role keeps
        // the plain name (existing names stay stable); a later different role gets "_KIND".
        if (Textures.TryGetValue(name, out var had) && had.Kind != kind && kind is not (TexKind.FlatNormal or TexKind.Black or TexKind.GreyGi or TexKind.DarkGi or TexKind.Ds2))
        {
            name = $"{name}_{kind.ToString().ToLowerInvariant()}";
            RoleClashes.Add($"{ds1Name}: {had.Kind} and {kind} → {name}");
        }
        Textures.TryAdd(name, new TexRequest(name, ds1Name, kind));
        return name;
    }

    /// <summary>Rule TX2 (walk11): DS1 textures requested in two roles, for build.log.</summary>
    public HashSet<string> RoleClashes { get; } = [];

    /// <summary>A sky texture (rule S1): its own name ("l_NAME_sky"), so a texture the sky shares with other materials
    /// keeps its plain conversion there.</summary>
    string RequestSky(string ds1Name, Sky.Bake bake)
    {
        string name = TexPrefix + ds1Name + "_sky";
        Textures.TryAdd(name, new TexRequest(name, ds1Name, TexKind.Sky, bake));
        return name;
    }

    /// <summary>A distant textured unlit material's baked texture (rule S3b v2): its own name ("l_NAME_MODEL_MATERIAL"),
    /// since the bake depends on the material's part rows and distance.</summary>
    string RequestBaked(string ds1Name, Sky.Bake bake, string key)
    {
        string name = $"{TexPrefix}{ds1Name}_{key}";
        Textures.TryAdd(name, new TexRequest(name, ds1Name, TexKind.Baked, bake));
        return name;
    }

    /// <summary>
    /// Session 9 (track skyfix): how one DS1 unlit material's vertex colours are made. Rule S1: a sky ÷ its mean (the
    /// mean is in the RGBM texture). Rule S3: × DS1's g_DiffuseMapColor × Power (gamma space). Rule S3n: NormalToAlpha
    /// materials × DS1's ambient L(n, p)^(1/2.2) (<see cref="Ds1Unlit.Ambient"/>). Rule S3b v2: distant materials get
    /// DS1's part fog and exact scattering (<see cref="Ds1Scatter"/>) — exactly per vertex when the texture is uniform
    /// (vertex colour = G^1.1 / texture mean), else as the ratio to a baked texture (<see cref="TexBake"/>).
    /// Additive excess p &gt; 1 moves into alpha as p² (DS2 squares rgb, not alpha).
    /// </summary>
    sealed class UnlitColour
    {
        public Vector3 Mul = Vector3.One, Div = Vector3.One;
        public bool Additive;
        /// <summary>Rules S3/S3n (session 9): DS2 squares (texture × vertex colour) where DS1 takes pow 2.2, so the vertex
        /// colour is c^1.1 × texture-mean^0.1 (exact at the texture mean); false for the sky (its RGBM texture is exact).</summary>
        public bool Exact;
        public Ds1Unlit.Ambient? Lit;
        public Matrix4x4 World = Matrix4x4.Identity;
        public bool Distant;
        public PARAM.Row? Fog, Scatter;
        public Vector3 Viewpoint, Tex = Vector3.One;
        public Vector3? BakeRef;
        public Sky.Bake? TexBake;
        /// <summary>Rule S3d (fix pass): × on DS2's pre-square value dividing out DS2's saturated black zone fog (a baked
        /// texture carries it in the texels instead).</summary>
        public float Ds2Gain = 1;
        /// <summary>Session 11 (atmos11), rule S3f: DS1 NormalToAlpha's view-angle alpha factor averaged over where the
        /// player can stand (world position, world normal → factor 0..1); null = none.</summary>
        public Func<Vector3, Vector3, float>? Fade;
        /// <summary>Session 11 (atmos11), rule S1d: the sky dome's per-vertex view-direction factor (world position → rgb ≤ 1).</summary>
        public Func<Vector3, Vector3>? SkyDir;

        /// <summary>DS1's gamma-space result for one vertex colour at a world position/normal and texture value t.</summary>
        public Vector3 Ds1Result(Vector3 vc, Vector3 p, Vector3 n, Vector3 t)
        {
            var c = t * vc * Mul * (Lit?.Gamma(n, p) ?? Vector3.One);
            if (!Distant) return c;
            float d = Vector3.Distance(p, Viewpoint);
            return Ds1Scatter.Toward(Scatter, p, Viewpoint).Apply(Vector3.Lerp(c, Sky.FogColor(Fog), Sky.FogAmount(Fog, d)));
        }

        static Vector3 Pow11(Vector3 v) => new(MathF.Pow(MathF.Max(v.X, 0), 1.1f), MathF.Pow(MathF.Max(v.Y, 0), 1.1f), MathF.Pow(MathF.Max(v.Z, 0), 1.1f));

        public FLVER.VertexColor Apply(FLVER.VertexColor c, Vector3 localPos, Vector3 localNormal)
        {
            var p = Vector3.Transform(localPos, World);
            var nw = Vector3.TransformNormal(localNormal, World);
            var n = nw.LengthSquared() > 1e-12f ? Vector3.Normalize(nw) : Vector3.UnitY;
            var vc = new Vector3(c.R, c.G, c.B);
            Vector3 rgb;
            if (!Distant) rgb = Exact ? Pow11(Ds1Result(vc, p, n, Vector3.One)) * new Vector3(MathF.Pow(Tex.X, 0.1f), MathF.Pow(Tex.Y, 0.1f), MathF.Pow(Tex.Z, 0.1f))
                : Ds1Result(vc, p, n, Vector3.One) / Div * (SkyDir?.Invoke(p) ?? Vector3.One); // rule S1d
            else if (BakeRef is Vector3 r)
            {
                var g = Ds1Result(vc, p, n, Tex);
                rgb = Pow11(new Vector3(r.X > 1e-4f ? g.X / r.X : 1, r.Y > 1e-4f ? g.Y / r.Y : 1, r.Z > 1e-4f ? g.Z / r.Z : 1));
            }
            else rgb = Pow11(Ds1Result(vc, p, n, Tex)) / Vector3.Max(Tex, new Vector3(1f / 255)) * Ds2Gain;
            float a = c.A * (Fade?.Invoke(p, n) ?? 1f); // rule S3f
            float peak = MathF.Max(rgb.X, MathF.Max(rgb.Y, rgb.Z));
            if (Additive && peak > 1) { a = MathF.Min(1, a * peak * peak); rgb /= peak; }
            rgb = Vector3.Clamp(rgb, Vector3.Zero, Vector3.One);
            return new FLVER.VertexColor(a, rgb.X, rgb.Y, rgb.Z);
        }
    }

    /// <summary>Rules S3/S3n/S3b v2 for one DS1 unlit material (null: vertex colours stay DS1's).</summary>
    UnlitColour? UnlitRuleFor(FLVER2 src, int mi, string modelName, FLVER2.Material sm, Ds1Mtd.Info info, string ds2Key, Func<FLVER2.Mesh, FLVER.Vertex, Matrix4x4> vertexNode)
    {
        var part = SkyContext?.PartFor(modelName);
        var rule = new UnlitColour
        {
            Mul = info.Color, Additive = ds2Key.EndsWith("_Add.mtd", StringComparison.OrdinalIgnoreCase),
            World = part?.World ?? Matrix4x4.Identity, Viewpoint = SkyContext?.Viewpoint ?? Vector3.Zero, Exact = SkyContext != null,
        };
        string mtdName = Path.GetFileName(sm.MTD.Replace('\\', '/'));
        string diffuseName = Path.GetFileNameWithoutExtension(sm.Textures.FirstOrDefault(t => t.ParamName == "g_Diffuse")?.Path?.Replace('\\', '/') ?? "");
        var diffuseBytes = diffuseName.Length > 0 ? SkyContext?.Texture?.Invoke(diffuseName) : null;
        var (texMean, texMin, texMax) = diffuseBytes != null ? Ds1Unlit.Stats(diffuseBytes) : (Vector3.One, Vector3.One, Vector3.One);
        rule.Tex = texMean;
        if (SkyContext != null && part?.Light != null && Ds1Unlit.IsNormalToAlpha(info))
        {
            rule.Lit = new Ds1Unlit.Ambient(part.Light, SkyContext.Env);
            SkyContext.Log.Add($"sky: {modelName} material {mi} '{sm.Name}' {mtdName}: DS1 NormalToAlpha shader lights it with part {part.Name} {rule.Lit.Note} (rule S3n)");
        }
        // session 11 (atmos11), rule S3f: NormalToAlpha's view-angle fade, averaged over the walkable viewpoints
        if (SkyContext?.WalkPoints is { Count: > 0 } walk && part != null && Ds1Unlit.IsNormalToAlpha(info) && !float.IsNaN(info.NtoAMin) && !float.IsNaN(info.NtoAMax))
        {
            rule.Fade = Ds1Unlit.ViewFade(walk, info.NtoAMin, info.NtoAMax);
            var vs = src.Meshes.Where(m => m.MaterialIndex == mi).SelectMany(m => m.Vertices.Where(v => Sane(v.Position))
                .Select(v => (P: Vector3.Transform(Vector3.Transform(v.Position, vertexNode(m, v)), part.World),
                              N: Vector3.TransformNormal(Vector3.TransformNormal(v.Normal, vertexNode(m, v)), part.World)))).ToList();
            double mean = vs.Count == 0 ? 1 : vs.Average(v => rule.Fade(v.P, v.N.LengthSquared() > 1e-12f ? Vector3.Normalize(v.N) : Vector3.UnitY));
            SkyContext.Log.Add($"sky: {modelName} material {mi} '{sm.Name}' {mtdName}: NormalToAlpha view fade (min {info.NtoAMin}°, max {info.NtoAMax}°) averaged over the walkable viewpoints: mean alpha × {mean:0.###} over {vs.Count} vertices (rule S3f)");
        }
        if (SkyContext != null && part != null)
        {
            var verts = src.Meshes.Where(m => m.MaterialIndex == mi).SelectMany(m => m.Vertices.Where(v => Sane(v.Position))
                .Select(v => (P: Vector3.Transform(Vector3.Transform(v.Position, vertexNode(m, v)), part.World),
                              N: Vector3.TransformNormal(Vector3.TransformNormal(v.Normal, vertexNode(m, v)), part.World),
                              C: v.Colors.Count > 0 ? new Vector3(v.Colors[0].R, v.Colors[0].G, v.Colors[0].B) : Vector3.One))).ToList();
            var dists = verts.Select(v => Vector3.Distance(v.P, SkyContext.Viewpoint)).Order().ToList();
            float dist = dists.Count > 0 ? dists[dists.Count / 2] : 0;
            // S3b v2: farther than FogBakeDistance (DS2's zone fog has saturated), or fogged by a dedicated part row no
            // DS2 zone carries (the Asylum cloud ring's 'clouds' row, Firelink's 'B2中景' mist row)
            bool dedicated = part.Fog != null && SkyContext.ZoneFogIds != null && !SkyContext.ZoneFogIds.Contains(part.Fog.ID);
            if (dist >= Sky.FogBakeDistance || dedicated)
            {
                rule.Distant = true; rule.Fog = part.Fog; rule.Scatter = part.Scatter;
                string diffuse = diffuseName;
                var bytes = diffuseBytes;
                var (mean, min, max) = (texMean, texMin, texMax);
                var spread = max - min;
                bool uniform = MathF.Max(spread.X, MathF.Max(spread.Y, spread.Z)) <= Ds1Unlit.UniformSpread;
                var F = Sky.FogColor(part.Fog); float f = Sky.FogAmount(part.Fog, dist);
                var terms = Ds1Scatter.Horizontal(part.Scatter, dist);
                // the material's mean DS1 colour factor (vertex colour × MTD colour × NormalToAlpha ambient)
                var kMean = verts.Count == 0 ? info.Color : verts.Select(v => v.C * info.Color * (rule.Lit?.Gamma(Vector3.Normalize(v.N), v.P) ?? Vector3.One)).Aggregate(Vector3.Zero, (a, b) => a + b) / verts.Count;
                var g = terms.Apply(Vector3.Lerp(mean * kMean, F, f));
                // rule S3d (fix pass): DS2 fogs this piece again with the viewpoint zone's fog, saturated at this distance;
                // a black zone fog only scales (1 − α), which the bake divides out exactly
                string s3d = "";
                if (dist >= Sky.FogBakeDistance && SkyContext.ViewZoneFog is { } vz)
                {
                    rule.Ds2Gain = vz.Gain;
                    s3d = vz.Black ? $"; DS2 zone fog ({vz.Note}) divided out: × {vz.Gain:0.###} on DS2's value (rule S3d)"
                        : $"; DS2 zone fog ({vz.Note}) not divided out: non-black, its inversion would amplify the untraced fog-colour units by 1/(1 − α) (rule S3d)";
                }
                string how;
                if (!uniform && bytes != null)
                {
                    rule.TexBake = new Sky.Bake(kMean, F, f, terms, $"K {Ds1Scatter.V(kMean)}, part {part.Name} fog '{part.Fog?.Name}' {f:0.##} of {Ds1Scatter.V(F)}, scatter '{part.Scatter?.Name}' {terms} at {dist:0} m", rule.Ds2Gain);
                    rule.BakeRef = g;
                    how = $"texture {diffuse} baked (RGB spread {Ds1Scatter.V(spread)}): texel V = G^1.1, vertex colour = per-vertex ratio";
                }
                else how = $"texture {diffuse} uniform (mean {Ds1Scatter.V(mean)}): exact per vertex";
                // what DS1 shows at the texture mean vs what the skylight pass (S3b v1) baked: c×T + S applied to the vertex colour
                SkyContext.Log.Add($"sky: {modelName} material {mi} '{sm.Name}' {mtdName}: {(dist >= Sky.FogBakeDistance ? "distant" : "dedicated-fog-row")} unlit piece at {dist:0} m: DS1 part {part.Name} fog '{part.Fog?.Name}' {f:0.##} of {Ds1Scatter.V(F)}, scatter '{part.Scatter?.Name}' {terms}; DS1 result at the texture mean {Ds1Scatter.V(g)} (linear {Ds1Scatter.V(new Vector3(MathF.Pow(g.X, 2.2f), MathF.Pow(g.Y, 2.2f), MathF.Pow(g.Z, 2.2f)))}); {how} (rule S3b v2){s3d}");
            }
        }
        if (rule.Mul == Vector3.One && rule.Lit == null && !rule.Distant && rule.Fade == null) return null;
        return rule;
    }

    /// <summary>Mean RGB of vertex colour 0 over the meshes of one material (1 where a mesh has none).</summary>
    static Vector3 MeanColor(FLVER2 src, int material)
    {
        var cols = src.Meshes.Where(m => m.MaterialIndex == material).SelectMany(m => m.Vertices)
            .Select(v => v.Colors.Count > 0 ? new Vector3(v.Colors[0].R, v.Colors[0].G, v.Colors[0].B) : Vector3.One).ToList();
        if (cols.Count == 0) return Vector3.One;
        var mean = cols.Aggregate(Vector3.Zero, (a, b) => a + b) / cols.Count;
        return Vector3.Max(mean, new Vector3(1f / 255));
    }

    /// <summary>
    /// Rule S1 (Sky): what DS1 shows on this sky material, in DS1's gamma space — the texture × g_DiffuseMapColor ×
    /// Power × the mean vertex colour, fogged with the PART's own FogBank row at the dome's median distance from the
    /// bonfire (DS1's linear fog: saturate((z − begin) / (end − begin)) × density), then its ScatterBank row there
    /// (Atmosphere.ScatterTerms, rule H1).
    /// </summary>
    Sky.Bake SkyBakeFor(FLVER2 src, int material, string modelName, Ds1Mtd.Info? info, Func<FLVER2.Mesh, FLVER.Vertex, Matrix4x4> vertexNode)
    {
        var part = SkyContext!.PartFor(modelName);
        var world = part?.World ?? Matrix4x4.Identity;
        var dists = src.Meshes.Where(m => m.MaterialIndex == material)
            .SelectMany(m => m.Vertices.Where(v => Sane(v.Position)).Select(v => Vector3.Distance(Vector3.Transform(Vector3.Transform(v.Position, vertexNode(m, v)), world), SkyContext.Viewpoint)))
            .Order().ToList();
        float dist = dists.Count > 0 ? dists[dists.Count / 2] : 0;
        var vc = MeanColor(src, material);
        var k = (info?.Color ?? Vector3.One) * vc;
        float f = Sky.FogAmount(part?.Fog, dist);
        var fog = Sky.FogColor(part?.Fog);
        var sc = Atmosphere.Scatter(part?.Scatter, dist);
        string note = $"k = DS1 colour ({info?.Color.X:0.##},{info?.Color.Y:0.##},{info?.Color.Z:0.##}) × mean vertex colour ({vc.X:0.##},{vc.Y:0.##},{vc.Z:0.##}); part {part?.Name ?? "-"} fog '{part?.Fog?.Name}' {f:0.##} of ({fog.X:0.##},{fog.Y:0.##},{fog.Z:0.##}), scatter '{part?.Scatter?.Name}' {sc} at {dist:0} m";
        // session 11 (atmos11), rule S1d: DS1's in-scatter depends on the view direction against the sun (exact angle per
        // pixel in DSR's shader); the RGBM texture holds the horizontal average, the dome's vertex colours carry the ratio
        // G(direction of the vertex from the viewpoint) / G(horizontal average) ^1.1 (DS2 squares (tex × vc)), divided by
        // its maximum M; the texture is scaled by M (Bake.Ds2Gain) so every vertex colour stays ≤ 1. LR_SKY_DIR=0: off.
        lastSkyDir = null; lastSkyWorld = world;
        float gain = 1;
        if (part?.Scatter != null && Environment.GetEnvironmentVariable("LR_SKY_DIR") != "0")
        {
            string diffuse = Path.GetFileNameWithoutExtension(src.Materials[material].Textures.FirstOrDefault(t => t.ParamName == "g_Diffuse")?.Path?.Replace('\\', '/') ?? "");
            var bytes = diffuse.Length > 0 ? SkyContext.Texture?.Invoke(diffuse) : null;
            var tex = bytes != null ? Ds1Unlit.Stats(bytes).Item1 : Vector3.One;
            var gH = sc.Apply(Vector3.Lerp(tex * k, fog, f));
            var view = SkyContext.Viewpoint;
            var scatterRow = part.Scatter;
            Vector3 Ratio(Vector3 p)
            {
                float d = Vector3.Distance(p, view);
                var g = Ds1Scatter.Toward(scatterRow, p, view).Apply(Vector3.Lerp(tex * k, fog, Sky.FogAmount(part.Fog, d)));
                static float R(float a, float b) => b > 1e-4f ? MathF.Min(MathF.Pow(MathF.Max(a, 0) / b, 1.1f), SkyDirCap) : 1; // cap: 8-bit vertex colours
                return new Vector3(R(g.X, gH.X), R(g.Y, gH.Y), R(g.Z, gH.Z));
            }
            var ratios = src.Meshes.Where(m => m.MaterialIndex == material).SelectMany(m => m.Vertices.Where(v => Sane(v.Position))
                .Select(v => Ratio(Vector3.Transform(Vector3.Transform(v.Position, vertexNode(m, v)), world)))).ToList();
            if (ratios.Count > 0)
            {
                float max = ratios.Max(r => MathF.Max(r.X, MathF.Max(r.Y, r.Z)));
                float min = ratios.Min(r => MathF.Min(r.X, MathF.Min(r.Y, r.Z)));
                gain = MathF.Max(1, max);
                float g0 = gain;
                lastSkyDir = p => Ratio(p) / g0;
                note += $"; rule S1d: per-vertex view-direction ratio {min:0.###}–{max:0.###} (texture × {gain:0.###})";
            }
        }
        SkyContext.Log.Add($"sky: {modelName} material {material} '{src.Materials[material].Name}': {note}");
        return new Sky.Bake(k, fog, f, sc, note, gain);
    }

    /// <summary>Rule S1d: largest per-vertex ratio kept (DS1's sun halo reaches ×30 and more in Firelink's B2天球 row; above this
    /// the 8-bit vertex colours of the rest of the dome would lose their precision).</summary>
    public const float SkyDirCap = 4f;
    Func<Vector3, Vector3>? lastSkyDir;
    Matrix4x4 lastSkyWorld = Matrix4x4.Identity;

    /// <summary>
    /// Object mode: a rigid mesh using several bones (DS1's double door: both leaves in one mesh, bone list [1,2]) is split
    /// into one mesh per bone with a one-entry bone list, the form vanilla rigid objects use (o10_1001: [1]). Session 7:
    /// as one mesh, DS2 drew only the first leaf even with NormalW per vertex.
    /// </summary>
    static FLVER2 SplitRigidMeshes(FLVER2 src)
    {
        var meshes = new List<FLVER2.Mesh>();
        foreach (var m in src.Meshes)
        {
            if (m.Dynamic != 0 || m.BoneIndices.Count < 2) { meshes.Add(m); continue; }
            var fs = m.FaceSets.FirstOrDefault(f => f.Flags == FLVER2.FaceSet.FSFlags.None) ?? m.FaceSets[0];
            var tris = fs.Triangulate(allowPrimitiveRestarts: true);
            for (int b = 0; b < m.BoneIndices.Count; b++)
            {
                var map = new Dictionary<int, int>();
                var verts = new List<FLVER.Vertex>();
                var idx = new List<int>();
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    int[] tri = [tris[t], tris[t + 1], tris[t + 2]];
                    if (tri.Any(v => Math.Clamp(m.Vertices[v].BoneIndices[0], 0, m.BoneIndices.Count - 1) != b)) continue;
                    foreach (int v in tri)
                    {
                        if (!map.TryGetValue(v, out int nv))
                        {
                            nv = verts.Count; map[v] = nv;
                            var copy = new FLVER.Vertex(m.Vertices[v]);
                            copy.BoneIndices = new FLVER.VertexBoneIndices(); // index 0 = this mesh's only bone
                            verts.Add(copy);
                        }
                        idx.Add(nv);
                    }
                }
                if (idx.Count == 0) continue;
                meshes.Add(new FLVER2.Mesh
                {
                    MaterialIndex = m.MaterialIndex, NodeIndex = m.BoneIndices[b], Dynamic = 0, BoneIndices = [m.BoneIndices[b]],
                    VertexBuffers = m.VertexBuffers, Vertices = verts, BoundingBox = m.BoundingBox,
                    FaceSets = [new FLVER2.FaceSet(FLVER2.FaceSet.FSFlags.None, false, fs.CullBackfaces, fs.Unk06, idx)],
                });
            }
        }
        var outF = new FLVER2 { Header = src.Header, Dummies = src.Dummies, Materials = src.Materials, GXLists = src.GXLists, Nodes = src.Nodes, Meshes = meshes, BufferLayouts = src.BufferLayouts };
        return outF;
    }

    public FLVER2 Convert(FLVER2 src, string modelName, FLVER2 template)
    {
        if (ObjectMode) src = SplitRigidMeshes(src);
        var dst = new FLVER2
        {
            Header = template.Header,
            Materials = new List<FLVER2.Material>(),
            Meshes = new List<FLVER2.Mesh>(),
            BufferLayouts = new List<FLVER2.BufferLayout>(),
            GXLists = new List<FLVER2.GXList>(),
            Nodes = new List<FLVER.Node>(),
            Dummies = new List<FLVER.Dummy>(),
        };
        var layoutIndex = new Dictionary<string, int>();
        var materialMap = new Dictionary<int, (int Index, Ds2MapMaterials.Entry Entry, bool Gi)>();
        coveredTwin.Clear(); // rule GI2: per model

        // DS1 map pieces built from fragments (the Asylum's broken stairs m0040 and rubble m5001, most of Firelink's
        // m50xx pieces) keep each vertex in the space of a node with its own transform (vertex bone index → the mesh's
        // bone palette → node). Placed raw, those fragments floated around the map (session 6). Measured offline
        // (lr.cmd node-survey): with the node transforms 95-100% of their vertices lie on DS1's collision, raw 0-32%.
        var nodeWorld = new Matrix4x4[src.Nodes.Count];
        for (int ni = 0; ni < src.Nodes.Count; ni++)
        {
            var m = src.Nodes[ni].ComputeLocalTransform();
            for (int p = src.Nodes[ni].ParentIndex, guard = 0; p >= 0 && p < src.Nodes.Count && guard < 64; p = src.Nodes[p].ParentIndex, guard++)
                m *= src.Nodes[p].ComputeLocalTransform();
            nodeWorld[ni] = m;
        }
        Matrix4x4 VertexNode(FLVER2.Mesh mesh, FLVER.Vertex v)
        {
            int node = mesh.BoneIndices.Count > 0 ? mesh.BoneIndices[Math.Clamp(v.BoneIndices[0], 0, mesh.BoneIndices.Count - 1)] : mesh.NodeIndex;
            return !ObjectMode && node >= 0 && node < nodeWorld.Length ? nodeWorld[node] : Matrix4x4.Identity;
        }

        // Session 8 (skylight): per DS1 material, how its vertex colours change (rules S1/S3, Sky): × DS1's
        // g_DiffuseMapColor × Power for unlit materials, ÷ the mean for a sky (its mean is baked into the RGBM texture).
        var colorRule = new Dictionary<int, UnlitColour>();
        bool hasSky = false;

        for (int mi = 0; mi < src.Materials.Count; mi++)
        {
            var sm = src.Materials[mi];
            string ds1Tex(string param) => Path.GetFileNameWithoutExtension(sm.Textures.FirstOrDefault(t => t.ParamName == param)?.Path?.Replace('\\', '/') ?? "");
            string lightmap = ds1Tex("g_Lightmap");
            // GI materials even without a DS1 lightmap (props): they get a neutral grey GI texture; non-GI DS2 materials rely
            // on dynamic lights only, which the converted maps barely have (the Asylum gargoyles rendered black).
            bool water = IsWater(sm.MTD) && materials.ByMtd.ContainsKey(WaterMtd);
            var ds1Info = Ds1Mtds?.Get(sm.MTD);
            Sky.Bake? skyBake = IsSkyMaterial(src, mi) ? SkyBakeFor(src, mi, modelName, ds1Info, VertexNode) : null;
            string key = water ? WaterMtd : skyBake != null ? Sky.SkyMtd : MapMtd(sm.MTD, hasLightmap: Gi && !ObjectMode, ds1Info);
            var entry = materials.ByMtd[key];
            if (skyBake != null)
            {
                hasSky = true;
                colorRule[mi] = new UnlitColour { Div = MeanColor(src, mi), SkyDir = lastSkyDir, World = lastSkyWorld }; // session 11: + rule S1d
            }
            else if (!ObjectMode && ds1Info != null && IsUnlit(sm.MTD))
            {
                // rules S3 (MTD colour), S3n (session 9: DS1's NormalToAlpha ambient) and S3b v2 (session 9: a distant
                // unlit piece — cloud layers, mountain rings: farther than Sky.FogBakeDistance from the bonfire — gets its
                // DS1 PART's own fog and DS1's exact scattering; the in-scatter is added after the texture, so textured
                // pieces get a baked texture). DS2's zone fog still applies on top.
                if (UnlitRuleFor(src, mi, modelName, sm, ds1Info, key, VertexNode) is { } rule) colorRule[mi] = rule;
            }
            bool gi = entry.Textures.Any(t => t.Param == "g_GITexture");

            var dm = new FLVER2.Material(sm.Name, entry.Mtd, 0) { Index = 0, GXIndex = dst.GXLists.Count };
            dst.GXLists.Add(materials.GxList(entry));
            foreach (var slot in entry.Textures)
            {
                string path = slot.Path;
                var scale = new Vector2(slot.ScaleX, slot.ScaleY);
                if (SlotMap.TryGetValue(slot.Param, out var map))
                {
                    string ds1 = ds1Tex(map.Ds1);
                    var srcTex = sm.Textures.FirstOrDefault(t => t.ParamName == map.Ds1);
                    if (ds1.Length == 0 && slot.Param == "g_DiffuseTexture2") { ds1 = ds1Tex("g_Diffuse"); srcTex = sm.Textures.FirstOrDefault(t => t.ParamName == "g_Diffuse"); }
                    if (ds1.Length == 0 && slot.Param == "g_SpecularTexture2") ds1 = ds1Tex("g_Specular");
                    if (ds1.Length == 0 && slot.Param == "g_BumpmapTexture2") ds1 = ds1Tex("g_Bumpmap");
                    string name = ds1.Length > 0 && skyBake != null && slot.Param == "g_DiffuseTexture" ? RequestSky(ds1, skyBake)
                        : ds1.Length > 0 && slot.Param == "g_DiffuseTexture" && colorRule.GetValueOrDefault(mi)?.TexBake is { } texBake ? RequestBaked(ds1, texBake, $"{modelName}_{mi}")
                        : ds1.Length > 0 ? Request(ds1, map.Kind)
                        : water && WaterDs2Tex.TryGetValue(slot.Param, out var ds2Tex) ? Request(ds2Tex, TexKind.Ds2) : map.Kind switch
                    {
                        TexKind.Normal => Request("", TexKind.FlatNormal),
                        // no DS1 lightmap (objects): neutral indirect light instead of black (black GI rendered props black)
                        TexKind.Lightmap => Request("", TexKind.GreyGi),
                        _ => Request("", TexKind.Black),
                    };
                    path = TexPath(name, map.Kind == TexKind.Lightmap);
                    scale = srcTex != null && srcTex.TilingScale != Vector2.Zero ? srcTex.TilingScale : new Vector2(1, 1);
                }
                dm.Textures.Add(new FLVER2.Texture(slot.Param, path, scale, 0, 0, 0, 0, 0));
            }
            materialMap[mi] = (dst.Materials.Count, entry, gi);
            // rule GI2: a material whose GI slot fell back to the grey stand-in (no DS1 lightmap) gets a covered twin
            if (Covered != null && dm.Textures.FirstOrDefault(t => t.ParamName == "g_GITexture") is { } giTex && giTex.Path == TexPath(TexPrefix + "gi_grey", true))
            {
                var dark = new FLVER2.Material(dm.Name, dm.MTD, 0) { Index = dm.Index, GXIndex = dm.GXIndex };
                foreach (var t in dm.Textures)
                    dark.Textures.Add(t.ParamName == "g_GITexture" ? new FLVER2.Texture(t.ParamName, TexPath(Request("", TexKind.DarkGi), true), t.TilingScale, 0, 0, 0, 0, 0) : new FLVER2.Texture(t.ParamName, t.Path, t.TilingScale, 0, 0, 0, 0, 0));
                coveredTwin[dst.Materials.Count] = -1; // filled below (the twin follows its material)
                dst.Materials.Add(dm);
                coveredTwin[dst.Materials.Count - 1] = dst.Materials.Count;
                dst.Materials.Add(dark);
            }
            else
            dst.Materials.Add(dm);
            Notes.Add($"{modelName}: '{sm.Name}' {Path.GetFileName(sm.MTD.Replace('\\', '/'))} → {key}"
                + (skyBake != null ? $" (sky, rule S1: {skyBake.Note})" : colorRule.TryGetValue(mi, out var cr) ? $" (DS1 colour ×({cr.Mul.X:0.###},{cr.Mul.Y:0.###},{cr.Mul.Z:0.###}) in vertex colours, rule S3{(cr.Lit != null ? " + S3n ambient" : "")}{(cr.Distant ? cr.TexBake != null ? " + S3b baked texture" : " + S3b per vertex" : "")}{(cr.Additive ? ", additive" : "")})" : ""));
        }

        bool allUnlit = true;
        foreach (var sMesh in src.Meshes)
        {
            // DS1 effect meshes (A19_Fly flies, A19_Division …) keep packed data in their positions (NaN, ±1.7e38). DS2 has
            // no such shaders, and those values would poison the model's bounding box, which DS2 culls the whole model by
            // (session 5: the Asylum cell, m0001, was invisible in game). Drop them.
            int bad = sMesh.Vertices.Count(v => !Sane(v.Position));
            if (bad > 0)
            {
                Notes.Add($"{modelName}: dropped a {Path.GetFileName(src.Materials[sMesh.MaterialIndex].MTD.Replace('\\', '/'))} mesh ({bad}/{sMesh.Vertices.Count} vertices with unusable positions)");
                continue;
            }
            var (matIndex, entry, gi) = materialMap[sMesh.MaterialIndex];
            if (!IsUnlit(src.Materials[sMesh.MaterialIndex].MTD)) allUnlit = false;
            bool recolor = colorRule.TryGetValue(sMesh.MaterialIndex, out var rule);
            var layout = materials.Layout(entry);
            string layoutKey = string.Join(",", layout.Select(m => $"{m.Semantic}{m.Index}{m.Type}"));
            if (!layoutIndex.TryGetValue(layoutKey, out int li))
            {
                li = dst.BufferLayouts.Count;
                layoutIndex[layoutKey] = li;
                dst.BufferLayouts.Add(layout);
                dst.BufferLayouts.Add(materials.SkinnedTwin(layout));
            }
            int uvCount = layout.Count(m => m.Semantic == FLVER.LayoutSemantic.UV) +
                          layout.Count(m => m.Semantic == FLVER.LayoutSemantic.UV && m.Type is FLVER.LayoutType.Float4 or FLVER.LayoutType.Short4);
            int tangents = layout.Count(m => m.Semantic == FLVER.LayoutSemantic.Tangent);
            int colors = layout.Count(m => m.Semantic == FLVER.LayoutSemantic.VertexColor);

            var faceSet = sMesh.FaceSets.FirstOrDefault(f => f.Flags == FLVER2.FaceSet.FSFlags.None) ?? sMesh.FaceSets[0];
            var indices = faceSet.Triangulate(allowPrimitiveRestarts: true);
            // session 9 (crowfix, rule §3 skinned meshes): a skinned mesh's face-set Unk06 is the number of palette matrices
            // DS2 uploads for it (see SkinnedPaletteCount); the map pieces' value (1) left every other slot stale
            bool skinned = ObjectMode && sMesh.Dynamic != 0;
            short unk06 = skinned ? SkinnedPaletteCount(sMesh) : entry.FaceSetUnk06;
            if (skinned)
            {
                int top = sMesh.Vertices.Count == 0 ? 0 : sMesh.Vertices.Max(v => Math.Max(Math.Max(v.BoneIndices[0], v.BoneIndices[1]), Math.Max(v.BoneIndices[2], v.BoneIndices[3]))) + 1;
                SkinnedNotes.Add($"mesh {dst.Meshes.Count}: palette {sMesh.BoneIndices.Count}, Unk06 {unk06} (DS1 {faceSet.Unk06})"
                    + (top > sMesh.BoneIndices.Count ? $" WARNING: a vertex uses slot {top - 1} past the palette" : ""));
            }
            var mesh = new FLVER2.Mesh
            {
                MaterialIndex = matIndex,
                // object mode keeps DS1's skinning: bone palette and node, on the layout's skinned twin (li + 1)
                // (only dynamic meshes: rigid ones use the plain layout and pick their bone by NormalW, like vanilla)
                NodeIndex = ObjectMode ? sMesh.NodeIndex : 0,
                BoneIndices = ObjectMode ? sMesh.BoneIndices.ToList() : new List<int> { 0 },
                Dynamic = ObjectMode ? sMesh.Dynamic : (byte)0,
                VertexBuffers = new List<FLVER2.VertexBuffer> { new FLVER2.VertexBuffer(ObjectMode && sMesh.Dynamic != 0 ? li + 1 : li) },
                FaceSets = new List<FLVER2.FaceSet> { new FLVER2.FaceSet(FLVER2.FaceSet.FSFlags.None, false, faceSet.CullBackfaces, unk06, indices) },
                Vertices = new List<FLVER.Vertex>(sMesh.Vertices.Count),
            };
            var vc1 = new FLVER.VertexColor(entry.Vc1[3], entry.Vc1[0], entry.Vc1[1], entry.Vc1[2]);
            foreach (var v in sMesh.Vertices)
            {
                var nv = new FLVER.Vertex(uvCount, tangents, colors, 1, 1, 1);
                var nodeM = VertexNode(sMesh, v);
                nv.Positions.Add(Vector3.Transform(v.Position, nodeM));
                var nn = Vector3.TransformNormal(v.Normal, nodeM);
                nv.Normals.Add(nn.LengthSquared() > 1e-12f ? Vector3.Normalize(nn) : v.Normal);
                // DS2 rigid (non-dynamic) object meshes pick their bone from the mesh bone list by NormalW, not by the
                // bone indices (vanilla double door o00_0471: indices all 0, NormalW 0/1 per leaf). Session 7: with
                // NormalW 0 both leaves of DS1's hall door followed the first leaf.
                nv.NormalWs.Add(ObjectMode && sMesh.Dynamic == 0 && sMesh.BoneIndices.Count > 0 ? Math.Clamp(v.BoneIndices[0], 0, sMesh.BoneIndices.Count - 1) : entry.NormalW);
                var t = v.Tangents.Count > 0 ? v.Tangents[0] : new Vector4(1, 0, 0, 1);
                var tt = Vector3.TransformNormal(new Vector3(t.X, t.Y, t.Z), nodeM);
                if (tt.LengthSquared() > 1e-12f) { tt = Vector3.Normalize(tt); t = new Vector4(tt, t.W); }
                t.W = t.W >= 0 ? 1 : -1;
                for (int i = 0; i < tangents; i++) nv.Tangents.Add(t);
                // objects: colour 0 alpha 1 like vanilla DS2 objects (DS1 doors carry alpha 0, which DS2's object
                // shading turns black: session 7, hall and cell doors)
                var c0 = v.Colors.Count > 0 ? v.Colors[0] : new FLVER.VertexColor(1f, 1f, 1f, 1f);
                if (ObjectMode || IsObject) c0 = new FLVER.VertexColor(1f, c0.R, c0.G, c0.B);
                if (recolor) c0 = rule!.Apply(c0, Vector3.Transform(v.Position, nodeM), nn);
                if (colors > 0) nv.Colors.Add(c0);
                for (int i = 1; i < colors; i++) nv.Colors.Add(vc1);

                Vector3 d1 = v.UVs.Count > 0 ? v.UVs[0] : Vector3.Zero;
                Vector3 lm = v.UVs.Count > 1 ? v.UVs[^1] : d1;
                Vector3 d2 = v.UVs.Count >= 3 ? v.UVs[1] : d1;
                var uvs = (gi, uvCount) switch
                {
                    (_, 0) => Array.Empty<Vector3>(),
                    (_, 1) => new[] { d1 },
                    (true, 2) => new[] { d1, lm },
                    (true, _) => new[] { d1, d2, lm },
                    (false, 2) => new[] { d1, d2 },
                    (false, _) => new[] { d1, d2, lm },
                };
                nv.UVs.AddRange(uvs);
                for (int i = uvs.Length; i < uvCount; i++) nv.UVs.Add(d1);
                nv.BoneIndices = ObjectMode ? v.BoneIndices : new FLVER.VertexBoneIndices();
                nv.BoneWeights = ObjectMode ? v.BoneWeights : new FLVER.VertexBoneWeights();
                mesh.Vertices.Add(nv);
            }
            if (Covered != null && coveredTwin.TryGetValue(matIndex, out int twin) && SplitCovered(mesh, twin) is { } coveredPart)
                dst.Meshes.Add(coveredPart);
            if (FitUvs(mesh, out string fitNote)) Notes.Add($"{modelName}: mesh {dst.Meshes.Count}: {fitNote}");
            dst.Meshes.Add(mesh);
        }

        var all = dst.Meshes.SelectMany(m => m.Vertices).Select(v => v.Position).ToList();
        var min = all.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
        var max = all.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        dst.Header = CopyHeader(template.Header, min, max);
        if (all.Count > 0) Kinds[modelName] = new ModelKind(hasSky, allUnlit && dst.Meshes.Count > 0, MathF.Max(max.X - min.X, MathF.Max(max.Y - min.Y, max.Z - min.Z)));
        if (ObjectMode)
        {
            // the object's own skeleton (its Havok skeleton and animation tracks name these nodes) and dummy polys
            dst.Nodes.AddRange(src.Nodes.Select(n => new FLVER.Node
            {
                Name = n.Name, ParentIndex = n.ParentIndex, FirstChildIndex = n.FirstChildIndex, NextSiblingIndex = n.NextSiblingIndex,
                PreviousSiblingIndex = n.PreviousSiblingIndex, Translation = n.Translation, Rotation = n.Rotation, Scale = n.Scale,
                Flags = n.Flags, BoundingBoxMin = n.BoundingBoxMin, BoundingBoxMax = n.BoundingBoxMax,
            }));
            dst.Dummies.AddRange(src.Dummies);
            return dst;
        }
        dst.Nodes.Add(new FLVER.Node
        {
            Name = modelName, ParentIndex = -1, FirstChildIndex = -1, NextSiblingIndex = -1, PreviousSiblingIndex = -1,
            Translation = Vector3.Zero, Rotation = Vector3.Zero, Scale = Vector3.One, BoundingBoxMin = min, BoundingBoxMax = max,
        });
        return dst;
    }

    /// <summary>Session 9 (crowfix): one line per skinned (dynamic) object mesh, its palette and face-set Unk06 (build.log).</summary>
    public List<string> SkinnedNotes { get; } = new();

    /// <summary>
    /// Rule §3 skinned meshes (session 9, crowfix): the face-set Unk06 of a skinned mesh = the number of bone-palette
    /// matrices DS2 uploads when it draws the mesh. Exe: FUN_140b630d0 (FLVER → runtime meshes, 0x58 bytes each) keeps per
    /// mesh and LOD the largest Unk06 of that LOD's face sets (Unk06 0 → the mesh's whole palette, mesh +0x14), and the
    /// draw (FUN_140b335f0, previous frame FUN_140b32ca0/FUN_140b32f40) fills exactly that many 3×4 matrices, palette slot
    /// i = pose(node) × inverse bind(node) of the mesh's bone list entry i. Slots past it keep whatever the last draw left
    /// in the constant buffer, which the skinned vertex shader indexes freely (Shader/Normal/FlverVS_*_Skin.vpo: cb12
    /// VC_aObjMatrix[BLENDINDICES × 3], weights divided by their sum, normals by the same matrices). Every vanilla DS2
    /// face set (1191 objects: 396 dynamic, 5912 rigid) has Unk06 ≥ the highest slot its vertices use + 1 (387/396 dynamic exactly that with weight > 0, the rest more); DS1 writes 0 (= whole
    /// palette, which our chr route keeps: c2501 V). Map pieces' Unk06 1 on DS1's crow (palettes of 2–38 bones) made
    /// 12,148 of 12,205 vertices read stale slots: the long black bar/stick of runs s9-4/s9-5. Here: the highest slot any
    /// vertex of the mesh references (any weight: a zero weight still multiplies the slot's matrix) + 1, like DS2's content
    /// (capped at the bone list: DS2 reads the list that far; a slot past it is a broken source mesh, reported in the note).
    /// </summary>
    public static short SkinnedPaletteCount(FLVER2.Mesh m)
    {
        int need = 0;
        foreach (var v in m.Vertices)
            for (int k = 0; k < 4; k++) need = Math.Max(need, v.BoneIndices[k] + 1);
        if (m.BoneIndices.Count > 0) need = Math.Min(need, m.BoneIndices.Count);
        return (short)Math.Max(1, need);
    }

    /// <summary>A real vertex position (not NaN/infinite and not a packed value like ±1.7e38).</summary>
    public static bool Sane(Vector3 p) =>
        float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z) && MathF.Abs(p.X) < 1e5f && MathF.Abs(p.Y) < 1e5f && MathF.Abs(p.Z) < 1e5f;

    static FLVER2.FLVERHeader CopyHeader(FLVER2.FLVERHeader t, Vector3 min, Vector3 max)
    {
        var h = (FLVER2.FLVERHeader)typeof(FLVER2.FLVERHeader).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(t, null)!;
        h.BoundingBoxMin = min;
        h.BoundingBoxMax = max;
        return h;
    }

    /// <summary>
    /// The DS2 TPF for one requested texture. GI textures: HQ archives name the texture "hq_NAME" inside NAME.tpf, LQ
    /// archives plain NAME (as DS2 does).
    /// </summary>
    /// <summary>DS1 texture names MakeTpf couldn't find (given stand-ins).</summary>
    public static readonly HashSet<string> Missing = new(StringComparer.OrdinalIgnoreCase);

    static readonly Dictionary<string, BXF4> ds2Archives = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A vanilla DS2 map texture, "tAA_BB_CC_DD/name" (from data\ds2\model\map\tAA….tpfbhd/bdt).</summary>
    static TPF.Texture Ds2MapTexture(string spec)
    {
        string archive = spec[..spec.IndexOf('/')], name = spec[(spec.IndexOf('/') + 1)..];
        string dir = Path.Combine(Ds2Data ?? throw new InvalidOperationException("MapPieceConverter.Ds2Data not set"), "model", "map");
        if (!ds2Archives.TryGetValue(archive, out var bxf))
            ds2Archives[archive] = bxf = BXF4.Read(Path.Combine(dir, archive + ".tpfbhd"), Path.Combine(dir, archive + ".tpfbdt"));
        var f = bxf.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith(name + ".tpf", StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"{name} not in {archive}.tpfbhd");
        return TPF.Read(DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes).Textures[0];
    }

    public static TPF MakeTpf(TexRequest req, Func<string, byte[]?> ds1Dds, bool hqGi, double giGain, int maxSize = 0)
    {
        byte[] bytes; byte format;
        if (req.Kind == TexKind.Sky && req.Bake != null && ds1Dds(req.Ds1Name) is byte[] skySrc)
        {
            // rule S1: DS1's sky result as DS2's RGBM sky texture (DXT5, format 33, FloatStruct 1010 = range); the _low copy
            // bakes from the downscaled DS1 texture (RGBM does not survive a bilinear resize)
            var (skyDds, range) = Sky.BakeRgbm(maxSize > 0 ? Ds2Tex.Downscale(skySrc, maxSize) : skySrc, req.Bake);
            return new TPF { Platform = TPF.TPFPlatform.PC, Encoding = 2, Flag2 = 3, Textures = new List<TPF.Texture> { Sky.RgbmTexture(req.Name, skyDds, range) } };
        }
        if (req.Kind == TexKind.Baked && req.Bake != null && ds1Dds(req.Ds1Name) is byte[] bakeSrc)
        {
            // rule S3b v2 (session 9): DS1's part fog + scattering result of a distant textured unlit piece, per texel
            var (bakedDds, bakedFormat) = Ds1Unlit.BakeTexture(maxSize > 0 ? Ds2Tex.Downscale(bakeSrc, maxSize) : bakeSrc, req.Bake);
            return new TPF { Platform = TPF.TPFPlatform.PC, Encoding = 2, Flag2 = 3, Textures = new List<TPF.Texture> { new(req.Name, bakedFormat, 0, bakedDds, TPF.TPFPlatform.PC) { Mipmaps = (byte)Ds2Tex.MipCount(bakedDds) } } };
        }
        switch (req.Kind) // (a Sky or Baked request whose DS1 texture is missing gets the colour stand-in of `default`)
        {
            case TexKind.FlatNormal: bytes = Ds2Tex.FlatNormal(); format = Ds2Tex.FormatNormal; break;
            case TexKind.Black: bytes = Ds2Tex.Solid(0, 0, 0); format = Ds2Tex.FormatDxt1; break;
            case TexKind.GreyGi: bytes = Ds2Tex.GiSolid(72); format = Ds2Tex.FormatDxt5; break;
            case TexKind.DarkGi: bytes = Ds2Tex.GiSolid(CoveredGiGrey); format = Ds2Tex.FormatDxt5; break; // rule GI2
            case TexKind.Ds2:
            {
                var src = Ds2MapTexture(req.Ds1Name);
                bytes = maxSize > 0 ? Ds2Tex.Downscale(src.Bytes, maxSize) : src.Bytes;
                format = src.Format;
                break;
            }
            default:
                var dds = ds1Dds(req.Ds1Name);
                if (dds == null)
                {
                    // a few DS1 materials name textures DSR doesn't ship (Firelink's ivy urns: m10_ivy_diteal_01):
                    // a neutral stand-in, reported in the pack log
                    Missing.Add(req.Ds1Name);
                    (bytes, format) = req.Kind switch
                    {
                        TexKind.Normal => (Ds2Tex.FlatNormal(), Ds2Tex.FormatNormal),
                        TexKind.Specular => (Ds2Tex.Solid(0, 32, 126), Ds2Tex.FormatDxt1),
                        TexKind.Lightmap => (Ds2Tex.GiSolid(72), Ds2Tex.FormatDxt5),
                        _ => (Ds2Tex.Solid(110, 105, 95), Ds2Tex.FormatDxt1),
                    };
                    break;
                }
                if (maxSize > 0) dds = Ds2Tex.Downscale(dds, maxSize);
                (bytes, format) = req.Kind switch
                {
                    TexKind.Normal => (Ds2Tex.Normal(dds), Ds2Tex.FormatNormal),
                    TexKind.Lightmap => (Ds2Tex.GiFromLightmap(dds, giGain, 0), Ds2Tex.FormatDxt5),
                    TexKind.Specular => (Ds2Tex.Specular(dds), Ds2Tex.FormatDxt1),
                    _ => Ds2Tex.Color(dds),
                };
                break;
        }
        string inner = req.Kind is TexKind.Lightmap or TexKind.GreyGi or TexKind.DarkGi && hqGi ? "hq_" + req.Name : req.Name;
        var tex = new TPF.Texture(inner, format, 0, bytes, TPF.TPFPlatform.PC) { Mipmaps = (byte)Ds2Tex.MipCount(bytes) };
        return new TPF { Platform = TPF.TPFPlatform.PC, Encoding = 2, Flag2 = 3, Textures = new List<TPF.Texture> { tex } };
    }
}
