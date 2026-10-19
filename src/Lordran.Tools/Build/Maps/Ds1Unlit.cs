using System.Numerics;
using System.Text.RegularExpressions;
using Lordran.Tools.Textures;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// DS1's "no lighting" ([Dn]) map materials, read from DSR's shaders (session 9, track skyfix; rules S3n, S3b v2).
///
/// <para><b>Two DS1 shader families draw [Dn] materials</b> (validation\dsr-unlit-ntoa-shaders.txt):
/// <c>FRPG_Phn_ColDif</c> (sky, clouds, distant mountains: <c>FRPG_Phn_Dif________________Non.fpo</c>) outputs
/// <c>tex × vertex colour × g_DiffuseMapColor×Power</c>, part fog, part scattering, pow 2.2 — truly unlit. But
/// <c>FRPG_NormalToAlpha</c> (light shafts 【天使のはしご】, mist sheets 【霧】: <c>FRPG_NtoA_Non/Csd/Sdw.fpo</c>) multiplies
/// the linear diffuse by an ambient term <c>L = lerp(HemAmbCol_d, HemAmbCol_u, n.y/2 + ½) + cube(n) × EnvDifMapMulCol</c>
/// before the fog, and fades alpha by the view angle. The ambient comes from the PART's LightBank row (FUN_140563590:
/// every colour <c>(rgb × A/100 / 255)^2.2</c>; FUN_140563460 scales the hemisphere by the global 1.5 at
/// DAT_141a2a130) and the environment-diffuse cube of the GI map spot (<see cref="Ds1EnvCubes"/>). Firelink's nest shafts
/// (m7010B2, LightBank 21 'B2屋内': hemisphere A 25/20, env A 0) therefore show at ≈ 0.22 of their MTD colour (gamma),
/// not × 3 as rule S3 had them.</para>
///
/// <para><b>Rule S3n</b>: a DS1 NormalToAlpha material's vertex colours get the gamma-space factor L(n, p)^(1/2.2) per
/// vertex (world normal n, position p, nearest GI map spot's cube) on top of rule S3's MTD colour. NormalToAlpha's view-angle
/// alpha fade has no DS2 counterpart (no DS2 map shader fades by view angle): not translated (U).
/// <b>Additive excess</b> (S3 fix): DS2 squares rgb but not alpha, so a gamma factor p &gt; 1 moved out of the vertex colour
/// multiplies alpha by p² (it was p).</para>
/// </summary>
public static class Ds1Unlit
{
    /// <summary>DS1's global hemisphere-ambient scale (DAT_141a2a130 = 1.5, read by FUN_140563460).</summary>
    public const float HemisphereScale = 1.5f;

    public static bool IsNormalToAlpha(Ds1Mtd.Info? m) => m != null && m.Shader.Contains("NormalToAlpha", StringComparison.OrdinalIgnoreCase);

    /// <summary>Rule S3f: how many walkable viewpoints (the nearest to a vertex) the fade is averaged over.</summary>
    public const int FadeViewpoints = 32;
    /// <summary>Rule S3f: eye height above the walkable surface (DS1's camera sits ~1.6 m up behind the player).</summary>
    public const float EyeHeight = 1.6f;

    /// <summary>
    /// <b>Rule S3f</b> (session 11, atmos11): DS1's NormalToAlpha shaders (light shafts, mist sheets) fade a sheet with the
    /// view angle: alpha × saturate((|n·v| − cb0[74].x) × cb0[74].y) (DSR FRPG_NtoA_Non.fpo: <c>dp3 r0.w, r1, r0; add
    /// |r0.w|, −cb0[74].x; mul_sat cb0[74].y</c>), cb0[74] = gFC_NormalToAlphaParam from the MTD's g_Normal2Alpha_MinAngle /
    /// MaxAngle: x = cos(min), y = 1/(cos(max) − cos(min)) (shafts 90°/80°: sheets within 10° of edge-on vanish; mist
    /// 90°/60°). DS2 has no such shader (its 'A10_*' MTDs are rebound to plain lit shaders), so a per-vertex constant
    /// approximates it: the factor averaged over the <see cref="FadeViewpoints"/> walkable viewpoints nearest the vertex
    /// (eye at <see cref="EyeHeight"/>), multiplied into the vertex alpha. Exact for a viewer standing at those points; a
    /// sheet the player mostly sees edge-on (the Firelink nest's crossing shafts) is thinned as DS1 thins it.
    /// </summary>
    public static Func<Vector3, Vector3, float> ViewFade(IReadOnlyList<Vector3> walk, float minAngle, float maxAngle)
    {
        float c0 = MathF.Cos(minAngle * MathF.PI / 180), c1 = MathF.Cos(maxAngle * MathF.PI / 180);
        float scale = MathF.Abs(c1 - c0) < 1e-4f ? 1e4f : 1 / (c1 - c0);
        var eyes = walk.Select(w => w + new Vector3(0, EyeHeight, 0)).ToArray();
        return (p, n) =>
        {
            int k = Math.Min(FadeViewpoints, eyes.Length);
            var best = new (float D, int I)[k];
            int filled = 0;
            for (int i = 0; i < eyes.Length; i++)
            {
                float d = Vector3.DistanceSquared(eyes[i], p);
                if (d < 1e-4f) continue;
                if (filled < k)
                {
                    best[filled++] = (d, i);
                    if (filled == k) Array.Sort(best, (a, b) => a.D.CompareTo(b.D));
                    continue;
                }
                if (d >= best[k - 1].D) continue;
                int j = k - 1;
                while (j > 0 && best[j - 1].D > d) { best[j] = best[j - 1]; j--; }
                best[j] = (d, i);
            }
            if (filled == 0) return 1f;
            float sum = 0;
            for (int i = 0; i < filled; i++)
            {
                var v = Vector3.Normalize(eyes[best[i].I] - p);
                sum += Math.Clamp((MathF.Abs(Vector3.Dot(n, v)) - c0) * scale, 0f, 1f);
            }
            return sum / filled;
        };
    }

    /// <summary>Rule S3f's viewpoints: walkable samples deduplicated on a grid.</summary>
    public static List<Vector3> Dedupe(IEnumerable<Vector3> pts, float cell = 3f)
    {
        var seen = new HashSet<(int, int, int)>();
        var list = new List<Vector3>();
        foreach (var p in pts)
            if (seen.Add(((int)MathF.Floor(p.X / cell), (int)MathF.Floor(p.Y / cell), (int)MathF.Floor(p.Z / cell)))) list.Add(p);
        return list;
    }

    static double D(PARAM.Row r, string f) => Ds1DrawParam.Get(r, f) is var v && double.IsNaN(v) ? 0 : v;

    /// <summary>A DS1 LightBank colour as the shader gets it (FUN_140564510): (rgb × A/100 / 255)^2.2.</summary>
    public static Vector3 LightColor(PARAM.Row r, string r_, string g_, string b_, string a_)
    {
        double a = D(r, a_) * 0.01;
        float C(string f) => (float)Math.Pow(Math.Abs(D(r, f) * a / 255), 2.2);
        return new Vector3(C(r_), C(g_), C(b_));
    }

    /// <summary>DS1's GI map spots of one map: MSB1 Environment events → their env-diffuse cubes.</summary>
    public sealed class EnvSpots
    {
        public List<(string Name, Vector3 Pos, Ds1EnvCubes.Cube Cube)> Spots { get; } = [];

        /// <summary>Event "GIマップスポットNN" → cube GI_EnvDif_mAA_BB_00NN; events without a number → the i-th cube of
        /// the map in name order (Asylum: 5 events, cubes 0000/0028/0040/0041/0042).</summary>
        public static EnvSpots For(string dsrRoot, string ds1Map, MSB1 msb)
        {
            var r = new EnvSpots();
            var cubes = Ds1EnvCubes.Load(dsrRoot, ds1Map[..3]);
            var mine = cubes.All().Select(c => c.Name).Where(n => n.Contains("Dif") && n.Contains(ds1Map[..6])).OrderBy(n => n, StringComparer.Ordinal).ToList();
            int i = 0;
            foreach (var e in msb.Events.Environments)
            {
                var region = msb.Regions.GetEntries().FirstOrDefault(x => x.Name == e.RegionName);
                var m = Regex.Match(e.Name, @"(\d+)$");
                var cube = m.Success ? cubes.GiDiffuse(ds1Map, int.Parse(m.Groups[1].Value)) : i < mine.Count ? cubes.Get(mine[i]) : null;
                if (region != null && cube != null) r.Spots.Add((e.Name, region.Position, cube));
                i++;
            }
            return r;
        }

        public (string Name, Ds1EnvCubes.Cube Cube)? Nearest(Vector3 p) =>
            Spots.Count == 0 ? null : Spots.OrderBy(s => Vector3.DistanceSquared(s.Pos, p)).Select(s => ((string, Ds1EnvCubes.Cube)?)(s.Name, s.Cube)).First();
    }

    /// <summary>The NormalToAlpha ambient of one part (its LightBank row) with the map's GI spots.</summary>
    public sealed class Ambient
    {
        readonly Vector3 up, down, envMul;
        readonly EnvSpots? env;
        public string Note { get; }

        /// <summary>A/B lane (environment variable LR_NTOA_ENV=0): leave the env-diffuse term out.
        /// <para>Traced in the fix pass (decomp\dsr): the scale FUN_1401c6900 puts on EnvDifMapMulCol (param_5) is the
        /// drawable's +0x14c, render command 0x804f; map-piece draw entities start at 1.0 (FrpgAsmModelDrawEntity /
        /// FrpgModelDrawEntity constructors) and only the character model update (the 0x800 dirty bit, chr data +0xa4)
        /// changes it, so map pieces use scale 1. The cube is t11, bound by FUN_1401eb320 by NAME:
        /// <c>GI_EnvDif_%s_%02d_%04d</c> (FUN_140563f90: bank area, spot id / 10000, spot id % 10000) from the drawable's
        /// two GI spot ids (+0xd8/+0xdc, blended by +0xe0); the LightBank's own envDif id (+0x50) only names the non-GI
        /// fallback <c>EnvDif_%s_%03d</c> (FUN_1401eade0 → FUN_140563e70), and DSR's map folders ship no such textures
        /// (only GI_EnvM_mAA). Where a map piece's spot ids come from is not traced: the nearest GI map spot is assumed
        /// (P−).</para></summary>
        public static bool UseEnv { get; } = Environment.GetEnvironmentVariable("LR_NTOA_ENV") != "0";

        public Ambient(PARAM.Row light, EnvSpots? env)
        {
            up = LightColor(light, "colR_u", "colG_u", "colB_u", "colA_u") * HemisphereScale;
            down = LightColor(light, "colR_d", "colG_d", "colB_d", "colA_d") * HemisphereScale;
            envMul = UseEnv ? LightColor(light, "envDif_colR", "envDif_colG", "envDif_colB", "envDif_colA") : Vector3.Zero;
            this.env = env;
            Note = $"LightBank {light.ID} '{light.Name}': hemisphere ×{HemisphereScale} up {Ds1Scatter.V(up)} down {Ds1Scatter.V(down)}, env-diffuse × {Ds1Scatter.V(envMul)}{(UseEnv ? " (nearest GI map spot's cube)" : " (env term OFF: LR_NTOA_ENV=0)")}";
        }

        /// <summary>DS1's linear ambient L at a world normal/position.</summary>
        public Vector3 At(Vector3 n, Vector3 p)
        {
            var l = Vector3.Lerp(down, up, Math.Clamp(n.Y * 0.5f + 0.5f, 0, 1));
            if (envMul != Vector3.Zero && env?.Nearest(p) is { } spot) l += spot.Cube.Sample(n) * envMul;
            return l;
        }

        /// <summary>The gamma-space factor L^(1/2.2) (DS1 multiplies the linear diffuse by L, then returns to gamma).</summary>
        public Vector3 Gamma(Vector3 n, Vector3 p)
        {
            var l = At(n, p);
            return new Vector3(MathF.Pow(MathF.Max(l.X, 0), 1 / 2.2f), MathF.Pow(MathF.Max(l.Y, 0), 1 / 2.2f), MathF.Pow(MathF.Max(l.Z, 0), 1 / 2.2f));
        }
    }

    /// <summary>Mean, min and max RGB of a DS1 texture's top level (0..1), over texels with alpha &gt; 0.</summary>
    public static (Vector3 Mean, Vector3 Min, Vector3 Max) Stats(byte[] dds)
    {
        var img = Dds.Read(dds).DecodeTop();
        var sum = Vector3.Zero; var mn = new Vector3(1); var mx = Vector3.Zero; int n = 0;
        foreach (var p in img.Pixels)
        {
            if (p.A == 0) continue;
            var c = new Vector3(p.R, p.G, p.B) / 255f;
            sum += c; mn = Vector3.Min(mn, c); mx = Vector3.Max(mx, c); n++;
        }
        return n == 0 ? (Vector3.One, Vector3.One, Vector3.One) : (sum / n, mn, mx);
    }

    /// <summary>A texture whose RGB varies by less than this (0..1) is "uniform": its DS1 result is exact in vertex colours.</summary>
    public const float UniformSpread = 8 / 255f;

    /// <summary>
    /// Rule S3b v2 for textured distant pieces (the Asylum mountains): DS1's in-scatter is ADDED after the texture
    /// (<c>lerp(c, c T R + S, blend)</c>), which vertex colours (multiplied with the texture by DS2) cannot carry. The
    /// texture itself is baked: per texel <c>V = (scatter(lerp(tex × K, F, f)))^1.1</c> (so DS2's square gives DS1's pow
    /// 2.2), alpha kept (alpha-tested silhouettes), DXT5 if it has alpha else DXT1.
    /// </summary>
    public static (byte[] Dds, byte Format) BakeTexture(byte[] ds1Dds, Sky.Bake b)
    {
        var img = Dds.Read(ds1Dds).DecodeTop();
        var dst = new Image(img.Width, img.Height);
        bool alpha = false;
        for (int i = 0; i < img.Pixels.Length; i++)
        {
            var p = img.Pixels[i];
            var g = b.Scatter.Apply(Vector3.Lerp(new Vector3(p.R, p.G, p.B) / 255f * b.K, b.FogColor, b.FogAmount));
            byte C(float x) => (byte)Math.Clamp(MathF.Round(MathF.Pow(MathF.Max(x, 0), 1.1f) * b.Ds2Gain * 255), 0, 255); // × rule S3d's gain
            dst.Pixels[i] = new Rgba(C(g.X), C(g.Y), C(g.Z), p.A);
            alpha |= p.A < 250;
        }
        return alpha ? (Dds.Encode(dst, BcFormat.BC3), Ds2Tex.FormatDxt5) : (Dds.Encode(dst, BcFormat.BC1), Ds2Tex.FormatDxt1);
    }
}
