using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// A DS2 colour-grading palette as the game samples it (session 9, track skyfix fix pass; offline display model).
/// <c>ToneMap_TabledToneMapLUT.fpo</c> (skylight validation\ds2-tonemap-fog-shaders.txt) samples the 16³ 3D LUT at
/// <c>display × 0.9375 + 0.03125</c> with the default (linear) sampler, i.e. trilinear interpolation between the 16
/// entries per axis at display × 15. The palette TPF is 64×64 A8R8G8B8: slice b at ((b % 4) × 16, (b / 4) × 16), x = red,
/// y = green (rule A7b). Rule A7c's entries are exact at the 16 nodes; between node 0 (kept at 0) and node 1 (display
/// 1/15) the game interpolates linearly, which the analytic curve does not show (review finding).
/// </summary>
public sealed class PaletteLut
{
    readonly Vector3[] e = new Vector3[16 * 16 * 16]; // index r + 16 (g + 16 b), 0..1
    public string Name { get; }

    PaletteLut(string name) => Name = name;

    /// <summary>From a palette texture's DDS bytes (128-byte header, 64×64 B,G,R,A).</summary>
    public static PaletteLut FromDds(string name, byte[] dds)
    {
        var lut = new PaletteLut(name);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                int o = 128 + (y * 64 + x) * 4;
                int r = x % 16, g = y % 16, b = x / 16 + 4 * (y / 16);
                lut.e[r + 16 * (g + 16 * b)] = new Vector3(dds[o + 2], dds[o + 1], dds[o]) / 255f;
            }
        return lut;
    }

    /// <summary>Palette fNNNNNN from a filterresource.fltbnd (a pack's, or DS2's own).</summary>
    public static PaletteLut? FromFltbnd(string fltbnd, int id)
    {
        if (!File.Exists(fltbnd)) return null;
        byte[] raw = File.ReadAllBytes(fltbnd);
        if (DCX.Is(raw)) raw = DCX.Decompress(raw);
        var f = BND4.Read(raw).Files.FirstOrDefault(x => Path.GetFileName(x.Name).Equals($"f{id:D6}.tpf", StringComparison.OrdinalIgnoreCase));
        return f == null ? null : FromDds($"f{id:D6}", TPF.Read(f.Bytes).Textures[0].Bytes);
    }

    /// <summary>The palette a pack's build wrote for a DS1 ToneCorrect row (rule A7b/A7c ids 7AARRR).</summary>
    public static PaletteLut? FromPack(string pack, string area, int toneCorrect) =>
        FromFltbnd(Path.Combine(pack, "gamedata", "filter", "filterresource.fltbnd"), Palette.IdFor(area, toneCorrect));

    /// <summary>The palette rule A7b (toneMatch false) or A7b + A7c (true) builds for a ToneCorrect row, in memory, from
    /// DS2's identity palette f100000 (as <see cref="Palette.Add"/>).</summary>
    public static PaletteLut? Build(string ds2Data, PARAM.Row row, bool toneMatch)
    {
        string path = Path.Combine(ds2Data, "filter", "filterresource.fltbnd");
        if (!File.Exists(path)) return null;
        byte[] raw = File.ReadAllBytes(path);
        if (DCX.Is(raw)) raw = DCX.Decompress(raw);
        var idf = BND4.Read(raw).Files.FirstOrDefault(x => Path.GetFileName(x.Name).Equals($"f{Palette.Identity:D6}.tpf", StringComparison.OrdinalIgnoreCase));
        if (idf == null) return null;
        var tpf = TPF.Read(Palette.Build(TPF.Read(idf.Bytes), "f000000", row, toneMatch));
        return FromDds(toneMatch ? "A7b+A7c (in memory)" : "A7b (in memory)", tpf.Textures[0].Bytes);
    }

    /// <summary>DS2's sample at a display colour (each channel clamped to 0..1): trilinear over the 16³ entries.</summary>
    public Vector3 Sample(Vector3 h)
    {
        static (int I, float F) Axis(float v) { float s = Math.Clamp(v, 0, 1) * 15; int i = Math.Min((int)s, 14); return (i, s - i); }
        var (ri, rf) = Axis(h.X); var (gi, gf) = Axis(h.Y); var (bi, bf) = Axis(h.Z);
        Vector3 E(int r, int g, int b) => e[r + 16 * (g + 16 * b)];
        var c00 = Vector3.Lerp(E(ri, gi, bi), E(ri + 1, gi, bi), rf);
        var c10 = Vector3.Lerp(E(ri, gi + 1, bi), E(ri + 1, gi + 1, bi), rf);
        var c01 = Vector3.Lerp(E(ri, gi, bi + 1), E(ri + 1, gi, bi + 1), rf);
        var c11 = Vector3.Lerp(E(ri, gi + 1, bi + 1), E(ri + 1, gi + 1, bi + 1), rf);
        return Vector3.Lerp(Vector3.Lerp(c00, c10, gf), Vector3.Lerp(c01, c11, gf), bf);
    }
}
