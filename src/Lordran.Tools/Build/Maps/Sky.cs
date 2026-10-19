using System.Numerics;
using Lordran.Tools.Textures;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// DS1 sky domes and unlit background pieces → DS2's sky convention (session 8, track skylight; user: "use the same
/// background for the sky from DS1 for the locations"). Evidence: <c>sky-survey ds1 MAP</c>, <c>sky-survey ds2 all</c>,
/// <c>sky-survey ds2tex</c>, <c>shader-disasm</c> (validation\ of the skylight sandbox).
///
/// <para><b>DS1</b> draws its sky as ordinary map pieces: a dome (Firelink m9000B2, 5.5 km wide; Asylum m9100B1, 5.8 km)
/// with an unlit "Sky" material (A10_02_m9000_Sky[Dn]_LS, A19_Sky[Dn]: description 【天球】 = celestial sphere), plus
/// unlit cloud/mountain rings (m9000B1, m5201B1). Each MSB1 part carries its own FogID/ScatterID: the sky domes use
/// dedicated rows (a10 FogBank 23 'B2遠景(拠点)': density 55 %, colour (105,110,110) × 280 %; a18 27 '天球': 50 %,
/// white × 90 %), so DS1 fogs the sky with its own HDR-bright haze, never with the zone's fog. DSR's shaders apply the
/// material multiplier g_DiffuseMapColor × Power in gamma space (Firelink sky: × 7; its texture m10_sky_02 averages
/// RGB (26,26,32), i.e. it is authored dark for that multiplier) and fog in gamma space (<c>lerp(c, fogColour,
/// saturate((z − begin)/(end − begin)) × density)</c>), then linearise with pow 2.2.</para>
///
/// <para><b>DS2</b> skies (16 of them in the vanilla maps): material SkyDome.mtd / SkyDome2[NoFog].mtd, ACB model
/// RenderType 2 with no shadows (14 of 16 RenderType 2, 15 of 16 DisableShadowSource/Target). DS2's shaders:
/// Flver_Col (NoLit[D], SkyDome.mtd) = <c>(tex × vertex colour)²</c> fogged by the map's fog;
/// Flver_SkyDomeNoFog (SkyDome2[NoFog].mtd, m10_27 m1000) = <c>(tex.rgb × tex.a × R × vertex colour)²</c> without
/// fog: an RGBM-encoded HDR texture, R = the TPF texture's FloatStruct (sky_00_d: format 33 = DXT5, FloatStruct
/// unk00 1010 values [1.5957]).</para>
///
/// <para><b>Rule S1 (sky dome)</b>: a DS1 sky material → SkyDome2[NoFog].mtd with a baked RGBM texture holding DS1's own
/// result: <c>G = scatter(lerp(tex × k, F, f))</c> in DS1's gamma space (k = g_DiffuseMapColor × Power × mean vertex
/// colour, F = the sky part's FogBank colour × colA/100, f = the fog density at the dome's distance from the bonfire,
/// scatter = its ScatterBank row there, <see cref="Atmosphere.ScatterTerms"/>), stored as <c>V = G^1.1</c> so that DS2's square gives
/// DS1's pow 2.2; vertex colours = DS1's relative to their mean. No DS2 map fog on top (DS1 never fogged its sky with the
/// zone's fog). The model gets the ACB settings of DS2's own SkyDome2[NoFog] model (m10_27 m1000: RenderType 2,
/// no shadows). <b>Rule S1b (skylight fix pass)</b>: every DS2 sky model holds only SkyDome/SkyDome2/NoLit materials with
/// the dome as material 0 (all 16 vanilla skies, validation\sky-ds2-all.txt), so a DS1 model that has a sky material AND
/// lit meshes (Firelink m9000B2: GI_M[D] ground and lit A10_02_m9000_cloud[DB]_Alp clouds beside the dome, 400 m+ out)
/// is split: the sky material(s) and the model's other unlit materials become a model of their own (m9999 downward,
/// never a DS1 name; dome first; DS2 sky ACB), the lit meshes stay an ordinary map piece under the DS1 name (map-piece
/// ACB: shadows, lighting, zone fog); both are placed wherever DS1 places the model. Before, the whole model took the
/// sky ACB, a case DS2 never has (its lit pieces could lose shadows/lighting/fog or be painted over by the sky pass).
/// <b>Rule S2 (background pieces)</b>: a DS1 model whose meshes are all unlit and that spans more than
/// <see cref="BackgroundExtent"/> m (mountain/cloud rings) gets RenderType 2 and no shadows (DS2's m10_15 m0066,
/// m10_27 m0010). <b>Rule S3 (unlit colour)</b>: other unlit DS1 materials keep DS2's NoLit MTDs with DS1's
/// g_DiffuseMapColor × Power baked into the vertex colours (mist 0.7 × (0.86, 1, 0.94) = DS1's teal-grey mist; additive
/// light shafts put the part above 1 into vertex alpha). <b>Rule S4</b>: MSB1 parts that place the same model with the
/// same transform are draw-group variants (m9100B1 ×3 with different DrawGroups/FogIDs); DS2 ports no draw groups, so
/// one is placed (the one with the most draw-group bits). Status P (built, not verified in game).</para>
/// </summary>
public static class Sky
{
    /// <summary>Rule S2 threshold: DS1's background rings are 1.1–3.1 km wide, its largest gameplay unlit pieces (mist
    /// layers, light shafts) under 350 m (sky-survey ds1).</summary>
    public const float BackgroundExtent = 500f;

    /// <summary>Rule S3b threshold: unlit materials whose median distance from the bonfire exceeds this get their DS1 part
    /// fog baked into the vertex colours. DS1's zone fogs end at 15–300 m (FogBank fogEndZ, Firelink/Asylum), so beyond
    /// 300 m DS2's own zone fog has saturated and only DS1's per-part row is missing; nearer pieces (mist sheets, light
    /// shafts) are fogged by the zone in both games. Session 9 (S3b v2): DS1 adds the in-scatter AFTER the texture, so a
    /// textured distant piece gets a baked texture (<see cref="Ds1Unlit.BakeTexture"/>) and its vertex colours only the
    /// per-vertex ratio; a piece with a uniform texture (clouds) stays exact in vertex colours.</summary>
    public const float FogBakeDistance = 300f;

    public const string SkyMtd = "SkyDome2[NoFog].mtd";
    public const byte RgbmFormat = 33;
    public const int RgbmFloatStructId = 1010;

    /// <summary>The DS1 part that places a sky model and the draw rows it picks (session 9: + its LightBank row, which
    /// DS1's NormalToAlpha shaders light the material with, rule S3n).</summary>
    public sealed record Part(Matrix4x4 World, PARAM.Row? Fog, PARAM.Row? Scatter, string Name, PARAM.Row? Light = null);

    /// <summary>Everything the converter needs for DS1 skies (null = no sky translation, e.g. objects).</summary>
    public sealed class Context
    {
        public required Func<string, Part?> PartFor { get; init; }
        public required Vector3 Viewpoint { get; init; }
        public List<string> Log { get; } = [];
        /// <summary>Session 9 (rule S3n): the map's GI map spots and their env-diffuse cubes.</summary>
        public Ds1Unlit.EnvSpots? Env { get; init; }
        /// <summary>Session 9 (rule S3b v2): DS1 texture bytes by name (texture statistics for distant pieces).</summary>
        public Func<string, byte[]?>? Texture { get; init; }
        /// <summary>Session 9 (rule S3b v2): the FogBank rows the map's ordinary collisions (the DS2 zones) use. An unlit
        /// part whose own FogID is none of them (Asylum cloud ring m5201B1 'clouds', Firelink mist m8100B2 'B2中景') is
        /// fogged by a row DS2's zone fog never reproduces, so its part fog is baked at any distance.</summary>
        public HashSet<int>? ZoneFogIds { get; init; }
        /// <summary>Session 9 fix pass (rule S3d): the DS2 zone fog at the viewpoint (null: not known / DS1 atmosphere off).</summary>
        public ViewZone.ZoneFog? ViewZoneFog { get; init; }
        /// <summary>Session 11 (atmos11), rule S3f: where the player can stand (DS1 walkable collision, ~3 m apart, deduplicated
        /// on a 3 m grid): the viewpoints NormalToAlpha's view-angle fade is averaged over.</summary>
        public IReadOnlyList<Vector3>? WalkPoints { get; init; }
    }

    /// <summary>The baked look of one sky material (the texture request carries it). <paramref name="Ds2Gain"/> (rule S3d,
    /// session 9 fix pass): a factor on DS2's pre-square texel value that divides out DS2's saturated black zone fog.</summary>
    public sealed record Bake(Vector3 K, Vector3 FogColor, float FogAmount, Atmosphere.ScatterTerms Scatter, string Note, float Ds2Gain = 1);

    static double D(PARAM.Row? r, string f) => Ds1DrawParam.Get(r, f) is var v && double.IsNaN(v) ? 0 : v;

    /// <summary>DS1 linear fog amount at a distance (FogBank begin/end in metres, degRotW = max density in percent).</summary>
    public static float FogAmount(PARAM.Row? fog, float distance)
    {
        if (fog == null) return 0;
        double begin = D(fog, "fogBeginZ"), end = D(fog, "fogEndZ"), density = Math.Clamp(D(fog, "degRotW") / 100, 0, 1);
        double t = end > begin ? Math.Clamp((distance - begin) / (end - begin), 0, 1) : 1;
        return (float)(t * density);
    }

    /// <summary>DS1 fog colour in DS1's gamma space: rgb/255 × colA/100 (can exceed 1: a10 23 = × 2.8).</summary>
    public static Vector3 FogColor(PARAM.Row? fog) =>
        fog == null ? Vector3.Zero : new Vector3((float)D(fog, "colR"), (float)D(fog, "colG"), (float)D(fog, "colB")) / 255f * (float)(D(fog, "colA") / 100);

    /// <summary>The RGBM texture (DXT5, one mip like DS2's sky_00_d) of a sky: <c>V = scatter(lerp(tex × k, F, f))^1.1</c>.</summary>
    public static (byte[] Dds, float Range) BakeRgbm(byte[] ds1Dds, Bake b)
    {
        var src = Dds.Read(ds1Dds).DecodeTop();
        int n = src.Pixels.Length;
        var v = new Vector3[n];
        float max = 0;
        for (int i = 0; i < n; i++)
        {
            var p = src.Pixels[i];
            var tex = new Vector3(p.R, p.G, p.B) / 255f;
            var g = b.Scatter.Apply(Vector3.Lerp(tex * b.K, b.FogColor, b.FogAmount));
            v[i] = new Vector3(MathF.Pow(MathF.Max(g.X, 0), 1.1f), MathF.Pow(MathF.Max(g.Y, 0), 1.1f), MathF.Pow(MathF.Max(g.Z, 0), 1.1f)) * b.Ds2Gain; // session 11: rule S1d headroom (1 = none)
            max = MathF.Max(max, MathF.Max(v[i].X, MathF.Max(v[i].Y, v[i].Z)));
        }
        float range = MathF.Max(1f, max);
        var dst = new Image(src.Width, src.Height);
        for (int i = 0; i < n; i++)
        {
            float m = MathF.Max(v[i].X, MathF.Max(v[i].Y, v[i].Z)) / range;
            byte a = (byte)Math.Clamp(MathF.Ceiling(m * 255), 1, 255);
            float s = a / 255f * range;
            byte C(float x) => (byte)Math.Clamp(MathF.Round(x / s * 255), 0, 255);
            dst.Pixels[i] = new Rgba(C(v[i].X), C(v[i].Y), C(v[i].Z), a);
        }
        return (Dds.Encode(dst, BcFormat.BC3, mipmaps: false), range);
    }

    public static TPF.Texture RgbmTexture(string name, byte[] dds, float range) => new(name, RgbmFormat, 0, dds, TPF.TPFPlatform.PC)
    {
        Mipmaps = (byte)Ds2Tex.MipCount(dds),
        FloatStruct = new TPF.FloatStruct { Unk00 = RgbmFloatStructId, Values = [range] },
    };

    /// <summary>DS2's own SkyDome2[NoFog] model settings (the map piece the material catalogue names as its example:
    /// m10_27_00_00 m1000), read from the vanilla ACB; null if not found.</summary>
    public static ACB.Asset.Model? Ds2SkyAcb(string ds2Data, Ds2MapMaterials materials)
    {
        if (!materials.ByMtd.TryGetValue(SkyMtd, out var e)) return null;
        var parts = e.Example.Split(':');              // "m10_27_00_00.mapbhd:m1000.flv.dcx"
        if (parts.Length != 2) return null;
        string bhd = Path.Combine(ds2Data, "model", "map", parts[0]);
        if (!File.Exists(bhd)) return null;
        var bxf = BXF4.Read(bhd, Path.ChangeExtension(bhd, ".mapbdt"));
        var acbFile = bxf.Files.FirstOrDefault(f => f.Name.EndsWith(".acb", StringComparison.OrdinalIgnoreCase));
        if (acbFile == null) return null;
        string model = parts[1].Replace(".flv.dcx", "", StringComparison.OrdinalIgnoreCase);
        return ACB.Read(acbFile.Bytes).Assets.OfType<ACB.Asset.Model>().FirstOrDefault(a => Path.GetFileNameWithoutExtension(a.AbsolutePath).Equals(model, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Rule S4: one MSB1 part per (model, transform); the variant with the most draw-group bits wins.</summary>
    public static List<MSB1.Part.MapPiece> DistinctPlacements(IEnumerable<MSB1.Part.MapPiece> parts, List<string> log)
    {
        var result = new List<MSB1.Part.MapPiece>();
        foreach (var g in parts.GroupBy(p => (p.ModelName, p.Position, p.Rotation, p.Scale)))
        {
            var list = g.ToList();
            var keep = list.OrderByDescending(p => p.DrawGroups.Sum(x => System.Numerics.BitOperations.PopCount(x))).First();
            result.Add(keep);
            if (list.Count > 1)
                log.Add($"sky: {g.Key.ModelName} placed {list.Count}× with the same transform (DS1 draw-group variants {string.Join(", ", list.Select(p => $"{p.Name} fog {p.FogID}"))}): kept {keep.Name} (DS2 ports no draw groups; rule S4)");
        }
        return result;
    }

    /// <summary>
    /// <b>Rule S4b</b> (session 11, atmos11): two DIFFERENT DS1 models placed with the same transform whose parts have
    /// disjoint DrawGroups are never drawn together by DS1; meshes they both hold (same material MTD + textures, same
    /// vertex count, same vertex positions) would be drawn twice by DS2 (no draw groups): coplanar duplicates with
    /// different lightmaps = z-fighting. The model with fewer draw-group bits drops those meshes, provided every one of
    /// its parts has such a twin (else the meshes would vanish where it stands alone). <c>atmos-check variants MAP</c>:
    /// Firelink m8101B2 (draw groups 91–92) repeats 3249 vertices of m8100B2 (roofs, walls, the mist sheet); none in the
    /// Asylum or the Burg. Returns DS1 model → mesh indices to drop.
    /// </summary>
    public static Dictionary<string, HashSet<int>> VariantDrops(string dsrRoot, string ds1Map, MSB1 msb1, List<string> log)
    {
        var result = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        string area = ds1Map.Substring(1, 2);
        var parts = msb1.Parts.MapPieces;
        static int Bits(MSB1.Part p) => p.DrawGroups.Sum(x => System.Numerics.BitOperations.PopCount(x));
        static bool Disjoint(MSB1.Part a, MSB1.Part b) => !a.DrawGroups.Zip(b.DrawGroups).Any(z => (z.First & z.Second) != 0);
        var candidates = new List<(string Drop, HashSet<string> Keep)>();
        foreach (var g in parts.GroupBy(p => p.ModelName))
        {
            string bModel = g.Key;
            // every part of bModel has a disjoint twin of one other model with more draw-group bits
            var twins = g.Select(b => parts.Where(a => a.ModelName != bModel && a.Position == b.Position && a.Rotation == b.Rotation && a.Scale == b.Scale
                && Disjoint(a, b) && Bits(a) > Bits(b)).Select(a => a.ModelName).ToHashSet()).ToList();
            var common = twins.Aggregate((HashSet<string>?)null, (acc, s) => acc == null ? s : acc.Intersect(s).ToHashSet());
            if (common is { Count: > 0 }) candidates.Add((bModel, common));
        }

        if (candidates.Count == 0) return result;
        var flvers = new Dictionary<string, FLVER2>();
        FLVER2? Load(string m)
        {
            if (flvers.TryGetValue(m, out var f)) return f;
            string file = Path.Combine(dsrRoot, "map", ds1Map, $"{m}A{area}.flver.dcx");
            return H2Merge.Exists(file) ? flvers[m] = FLVER2.Read(DCX.Decompress(H2Merge.ReadAllBytes(file))) : null; // rule H2-B
        }
        // per material (MTD + texture names): vertex count and position sum over all its meshes (the two models split
        // the same geometry into meshes differently)
        static string Key(FLVER2 f, int mi) => $"{Path.GetFileName(f.Materials[mi].MTD)}|{string.Join(",", f.Materials[mi].Textures.Select(t => Path.GetFileName(t.Path ?? "")))}";
        static Dictionary<string, (int N, Vector3 Sum)> Agg(FLVER2 f)
        {
            var d = new Dictionary<string, (int N, Vector3 Sum)>();
            foreach (var m in f.Meshes)
            {
                string k = Key(f, m.MaterialIndex);
                var cur = d.GetValueOrDefault(k);
                d[k] = (cur.N + m.Vertices.Count, cur.Sum + m.Vertices.Aggregate(Vector3.Zero, (s, v) => s + v.Position));
            }
            return d;
        }
        foreach (var (drop, keeps) in candidates)
        {
            var fb = Load(drop);
            if (fb == null) continue;
            var bb = Agg(fb);
            var same = new HashSet<string>();
            var by = new List<string>();
            foreach (string keep in keeps.Order())
            {
                var fa = Load(keep);
                if (fa == null) continue;
                var aa = Agg(fa);
                var s = bb.Where(kv => aa.TryGetValue(kv.Key, out var o) && o.N == kv.Value.N && Vector3.Distance(o.Sum, kv.Value.Sum) < 0.01f * Math.Max(1, kv.Value.N)).Select(kv => kv.Key).ToList();
                if (s.Count == 0) continue;
                same.UnionWith(s);
                by.Add(keep);
            }
            var idx = fb.Meshes.Select((m, i) => (m, i)).Where(x => same.Contains(Key(fb, x.m.MaterialIndex))).Select(x => x.i).ToHashSet();
            if (idx.Count == 0) continue;
            result[drop] = idx;
            log.Add($"sky: {drop} repeats {idx.Count} of its {fb.Meshes.Count} meshes ({idx.Sum(i => fb.Meshes[i].Vertices.Count)} vertices, {same.Count} materials) of {string.Join(", ", by)} at the same transform for disjoint DS1 draw groups: dropped from {drop} (rule S4b; DS2 would draw both, coplanar)");
        }
        return result;
    }
}
