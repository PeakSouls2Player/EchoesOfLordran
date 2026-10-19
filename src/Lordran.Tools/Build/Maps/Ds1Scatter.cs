using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// DS1's own LightScatteringBank math, read from DSR (session 9, track skyfix; replaces the skylight track's assumed depth
/// scale 1e-4/m and in-scatter normalisation, rule H1 v2).
///
/// <para><b>CPU</b> (<c>decomp\dsr</c>): <c>LightScatteringBankEntity</c>'s constructor (FUN_140561370) holds per-channel
/// coefficient vectors (exact floats below): Rayleigh β (+0x20), Rayleigh angular β' (+0x30 = β × 3/16π), Mie β (+0x40,
/// ∝ λ⁻²), Mie angular β' (+0x50). FUN_1405613f0 turns a row into the shader constants: <c>gFC_LsBeta1PlusBeta2</c> =
/// lsBetaRay × Rayleigh β + lsBetaMie × Mie β; <c>OneOverBeta</c> = its reciprocal; <c>BetaDash1/2</c> = lsBetaRay ×
/// Rayleigh β' and lsBetaMie × Mie β'; <c>HGg</c> = (1 − g², 1 + g, 2g) (the exe adds g, not g², to 1: disassembly at
/// 0x1405614c0); <c>TerrainReflectance</c> = reflectance rgb × A/100 / 255, w 1; <c>SunColor</c> = sun rgb × sunA/100 /
/// 255, w = blendCoef/100; <c>LightDir</c> = (sin ry cos rx, −sin rx, cos ry cos rx) from sunRotX/Y, w = distanceMul/100.</para>
///
/// <para><b>Shader</b> (every DSR map shader, e.g. <c>FRPG_Phn_Dif________________Non.fpo</c>, validation\dsr-unlit-ntoa-
/// shaders.txt): in DS1's gamma space, after the fog lerp, with d = |eye vector| and cosθ = dot(eye vector / d, LightDir):
/// <c>T = exp2(−d × β × LightDir.w × 2.081369)</c> per channel; <c>S = SunColor × (BetaDash1 × (1 + cos²θ) + BetaDash2 ×
/// HGg.x × (HGg.y − HGg.z cosθ)^−1.5) × OneOverBeta × (1 − T)</c>; result <c>lerp(c, c × T × reflectance + S,
/// blend)</c>, then pow 2.2. The eye vector points from the surface to the camera (the HG lobe then sits around the sun).</para>
///
/// <para><b>Fix pass (session 9 review):</b> the DXBC <c>exp</c> instruction is base 2 (the same shader computes its pow
/// 2.2 as <c>log</c> / <c>mul 2.2</c> / <c>exp</c>, which is only a power with base-2 log/exp), and 2.081369 = log2(e)²
/// is the compiler's folding of an HLSL <c>exp(x × 1.442695)</c>. So <c>T = 2^(−dβw × 2.081369) = e^(−1.442695 dβw)</c>;
/// the first pass read it as e^(−2.081369 dβw) and over-stated the extinction (Asylum row 26 at 519 m: T 0.25/0.17/0.08
/// instead of 0.38/0.29/0.17).</para>
/// </summary>
public static class Ds1Scatter
{
    static float F(uint bits) => BitConverter.UInt32BitsToSingle(bits);
    public static readonly Vector3 RayBeta = new(F(0x3a36c034), F(0x3a9a84fe), F(0x3b2034b1));
    public static readonly Vector3 RayAngular = new(F(0x382e839f), F(0x38938e26), F(0x3918fc3c));
    public static readonly Vector3 MieBeta = new(F(0x3bbc1ba0), F(0x3bf27913), F(0x3c2c4439));
    public static readonly Vector3 MieAngular = new(F(0x3aaed27c), F(0x3ae356bd), F(0x3b23af18));
    public const float DepthConstant = 2.081369f;

    // the result type is Atmosphere.ScatterTerms (per-channel T, reflectance, in-scatter incl. (1 − T), blend)


    public static string V(Vector3 v) => $"({v.X:0.###},{v.Y:0.###},{v.Z:0.###})";

    static double D(PARAM.Row r, string f) => Ds1DrawParam.Get(r, f) is var v && double.IsNaN(v) ? 0 : v;

    /// <summary>The row's shader constants (FUN_1405613f0).</summary>
    public sealed record Constants(Vector3 Beta, Vector3 Dash1, Vector3 Dash2, Vector3 Hg, Vector3 Reflectance, Vector3 Sun, float Blend, Vector3 LightDir, float DistanceMul);

    public static Constants Of(PARAM.Row r)
    {
        float ray = (float)D(r, "lsBetaRay"), mie = (float)D(r, "lsBetaMie"), g = (float)D(r, "lsHGg");
        float reflA = (float)(D(r, "reflectanceA") * 0.01), sunA = (float)(D(r, "sunA") * 0.01);
        double rx = D(r, "sunRotX") * Math.PI / 180, ry = D(r, "sunRotY") * Math.PI / 180;
        return new Constants(
            ray * RayBeta + mie * MieBeta, ray * RayAngular, mie * MieAngular, new Vector3(1 - g * g, 1 + g, 2 * g),
            new Vector3((float)D(r, "reflectanceR"), (float)D(r, "reflectanceG"), (float)D(r, "reflectanceB")) * reflA / 255f,
            new Vector3((float)D(r, "sunR"), (float)D(r, "sunG"), (float)D(r, "sunB")) * sunA / 255f,
            (float)(D(r, "blendCoef") * 0.01),
            new Vector3((float)(Math.Sin(ry) * Math.Cos(rx)), (float)-Math.Sin(rx), (float)(Math.Cos(ry) * Math.Cos(rx))),
            (float)(D(r, "distanceMul") * 0.01));
    }

    static Vector3 Safe(Vector3 beta) => new(beta.X > 0 ? 1 / beta.X : 0, beta.Y > 0 ? 1 / beta.Y : 0, beta.Z > 0 ? 1 / beta.Z : 0);

    /// <summary>The angular in-scatter factor (BetaDash1 (1 + cos²) + BetaDash2 × HG) × OneOverBeta at one cosθ.</summary>
    static Vector3 Angular(Constants c, float cos)
    {
        float den = c.Hg.Y - c.Hg.Z * cos;
        float hg = den > 1e-6f ? c.Hg.X / (den * MathF.Sqrt(den)) : 0;
        return (c.Dash1 * (1 + cos * cos) + c.Dash2 * hg) * Safe(c.Beta);
    }

    /// <summary>T = exp2(−d β w × 2.081369), DXBC <c>exp</c> being 2^x (fix pass; it was read as e^x).</summary>
    static Vector3 Transmittance(Constants c, float d)
    {
        var tau = c.Beta * (d * c.DistanceMul * DepthConstant);
        return new Vector3(MathF.Pow(2, -tau.X), MathF.Pow(2, -tau.Y), MathF.Pow(2, -tau.Z));
    }

    /// <summary>DS1's terms at distance d and one view angle (cosθ between the surface→camera direction and LightDir).</summary>
    public static Atmosphere.ScatterTerms At(PARAM.Row? r, float d, float cos)
    {
        if (r == null) return Atmosphere.ScatterTerms.None;
        var c = Of(r);
        var t = Transmittance(c, d);
        return new Atmosphere.ScatterTerms(t, c.Reflectance, c.Sun * Angular(c, cos) * (Vector3.One - t), c.Blend);
    }

    /// <summary>DS1's terms for a surface seen from a camera (exact view angle).</summary>
    public static Atmosphere.ScatterTerms Toward(PARAM.Row? r, Vector3 surface, Vector3 camera)
    {
        if (r == null) return Atmosphere.ScatterTerms.None;
        var e = camera - surface;
        float d = e.Length();
        return At(r, d, d > 0 ? Vector3.Dot(e / d, Of(r).LightDir) : 0);
    }

    /// <summary>
    /// The direction-independent value DS2 needs (one fog colour, one baked texture): DS1's terms at distance d averaged
    /// over horizontal view directions (72 azimuths, elevation 0): the views DS1's haze is seen in. A sun halo (HG lobe
    /// around LightDir above the horizon) is mostly outside that band and is not translated.
    /// </summary>
    public static Atmosphere.ScatterTerms Horizontal(PARAM.Row? r, float d)
    {
        if (r == null) return Atmosphere.ScatterTerms.None;
        var c = Of(r);
        var t = Transmittance(c, d);
        var sum = Vector3.Zero;
        const int n = 72;
        for (int i = 0; i < n; i++)
        {
            double phi = 2 * Math.PI * i / n;
            var toCamera = -new Vector3((float)Math.Sin(phi), 0, (float)Math.Cos(phi)); // view direction (sin φ, 0, cos φ)
            sum += Angular(c, Vector3.Dot(toCamera, c.LightDir));
        }
        return new Atmosphere.ScatterTerms(t, c.Reflectance, c.Sun * (sum / n) * (Vector3.One - t), c.Blend);
    }

    public static string Describe(PARAM.Row r)
    {
        var c = Of(r);
        return $"β {V(c.Beta * 1000)}e-3 per m × distanceMul {c.DistanceMul:0.##} × {DepthConstant} (T = 2^−x), HG (1−g², 1+g, 2g) {V(c.Hg)}, sun {V(c.Sun)}, refl {V(c.Reflectance)}, blend {c.Blend:0.##}, LightDir {V(c.LightDir)}";
    }
}
