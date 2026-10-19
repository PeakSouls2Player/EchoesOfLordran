using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// DS1's material definitions (DSR <c>mtd\Mtd.mtdbnd.dcx</c>, overridden by <c>MtdPatch.mtdbnd.dcx</c>), session 8 track
/// skylight. The converter needs three things DS2's map MTDs cannot say for a DS1 material:
///  - the colour multiplier <c>g_DiffuseMapColor × g_DiffuseMapColorPower</c>. DSR's shaders multiply the diffuse sample
///    by it in gamma space before linearising (<c>FRPG_Phn_Dif___BmpMulLit____Non.fpo</c>: <c>tex × cb0[9].xyz × vertex
///    colour</c>, then <c>pow(x, 2.2)</c>; <c>shader-disasm</c>). Unlit DS1 materials rely on it: Firelink's sky
///    A10_02_m9000_Sky[Dn]_LS = 7, the Asylum's A19_Sky[Dn] = 2, mist A10_mist[Dn]_Alp = 0.7 × (0.86, 1, 0.94),
///    clouds 0.5, distant mountains 1.5. DS2's NoLit/SkyDome MTDs fix g_DiffuseMapColor at (1,1,1).
///  - the developer description: 【天球】 / 【天球_空】 = sky sphere, 【天球_雲】 = sky clouds, 【霧】 = mist,
///    【天使のはしご】 = light shafts ("angel's ladder"), 【遠景・山】 = distant mountains.
///  - g_BlendMode: 0 opaque, 1 edge (alpha test), 2 alpha blend, 4 additive, 32 the light-scattered sky ("_LS").
/// </summary>
public sealed class Ds1Mtd
{
    public sealed record Info(string Name, string Description, string Shader, int BlendMode, int LightingType, Vector3 Color, float NtoAMin = float.NaN, float NtoAMax = float.NaN);

    readonly Dictionary<string, Info> byName = new(StringComparer.OrdinalIgnoreCase);
    static Ds1Mtd? cached;

    public static Ds1Mtd Load(string dsrRoot)
    {
        if (cached != null) return cached;
        var result = new Ds1Mtd();
        foreach (string bnd in new[] { "Mtd.mtdbnd.dcx", "MtdPatch.mtdbnd.dcx" })
        {
            string path = Path.Combine(dsrRoot, "mtd", bnd);
            if (!File.Exists(path)) continue;
            foreach (var f in BND3.Read(DCX.Decompress(File.ReadAllBytes(path))).Files)
            {
                MTD mtd;
                try { mtd = MTD.Read(f.Bytes); } catch (Exception) { continue; }
                object? P(string n) => mtd.Params.FirstOrDefault(p => p.Name == n)?.Value;
                var col = P("g_DiffuseMapColor") is float[] c && c.Length >= 3 ? new Vector3(c[0], c[1], c[2]) : Vector3.One;
                float power = P("g_DiffuseMapColorPower") is float pw ? pw : 1f;
                string name = Path.GetFileName(f.Name);
                result.byName[name] = new Info(name, mtd.Description ?? "", Path.GetFileName(mtd.ShaderPath ?? ""),
                    P("g_BlendMode") is int bm ? bm : 0, P("g_LightingType") is int lt ? lt : -1, col * power,
                    P("g_Normal2Alpha_MinAngle") is float n0 ? n0 : float.NaN, P("g_Normal2Alpha_MaxAngle") is float n1 ? n1 : float.NaN); // session 11: rule S3f
            }
        }
        return cached = result;
    }

    public Info? Get(string mtdPath) => byName.GetValueOrDefault(Path.GetFileName(mtdPath.Replace('\\', '/')));

    /// <summary>
    /// The opaque sky of a sky dome: DS1 marks it in the MTD name ("Sky": A19_Sky[Dn], A10_02_m9000_Sky[Dn]_LS) and in
    /// the description (【天球】 / 【天球_空】 = celestial sphere / its sky; clouds on the dome are 【天球_雲】 and stay
    /// translucent unlit pieces). Opaque blend modes only (0, or 32 = the light-scattered sky).
    /// </summary>
    public static bool IsSky(Info? m, string mtdPath)
    {
        string n = Path.GetFileNameWithoutExtension(mtdPath.Replace('\\', '/'));
        bool named = n.Contains("Sky", StringComparison.OrdinalIgnoreCase);
        if (m == null) return named;
        bool described = m.Description.Contains("天球") && !m.Description.Contains("雲");
        return (named || described) && m.BlendMode is 0 or 32;
    }
}
