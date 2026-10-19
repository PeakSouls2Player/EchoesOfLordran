using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Rule A7b (session 8, track skylight): DS1's colour correction (ToneCorrectBank) → a DS2 colour-grading palette.
///
/// <para><b>DS1</b> (DSR <c>FRPG_Fil_HDR_PBL_ColAdj.fpo</c>, shader-disasm): after the tone curve and gamma 1/2.2 the
/// final colour is <c>M × (rgb, 1)</c> with a 3×4 matrix (cb0[62..64]) the game builds from the zone's ToneCorrectBank
/// row (brightnessR/G/B, contrastR/G/B, saturation, hue). <b>DS2</b> (<c>ToneMap_ExpToneMapPSLUT.fpo</c> /
/// <c>ToneMap_TabledToneMapLUT.fpo</c>): after its filmic curve the display colour goes through a 16³ 3D LUT, the
/// fltparam's カラーグレーディング "パレットファイルID" (filter\filterresource.fltbnd fNNNNNN.tpf: 64×64 A8R8G8B8, slice b
/// at (b % 4, b / 4) × 16, x = red, y = green; f100000 is the identity (mean error 0.3/255)). 変換モード selects how
/// (exe enum labels at 0x1410c5150/0x1411e06d8: 0 None, 1 GrayScale, 2 Hsb, 3 PaletteFile); the HSB fields
/// (色相 ±180, 彩度/明度 ±100) only act in mode 2. Every vanilla map uses mode 3 with a tinted palette on filter 0
/// (Majula/Things Betwixt 5036: green mid-tones (75,81,73) for grey 85, desaturated primaries) and mode 0 elsewhere. The
/// atmos track copied Majula's 5036 into every DS1 zone and wrote 彩度 = saturation − 1, which has no effect in mode 3
/// (and would be 0.2 % on DS2's ±100 scale) — the yellow-green cast the user saw at Firelink.</para>
///
/// <para><b>Rule</b>: every DS1 ToneCorrect row a zone uses becomes our own palette = DS1's correction applied to the
/// identity LUT, in display space like both games: per channel brightness × , contrast around 0.5, saturation around
/// Rec.601 luma (DSR's own luma weights in this shader: 0.299/0.587/0.114), hue rotation about the grey axis (P: the
/// matrix composition is CPU code in DSR's exe, not verified; the standard composition is used). Zone filter ids get
/// 変換モード 3 + that palette and HSB 0. Palette ids = 7 AA RRR (DS1 area, ToneCorrect row; vanilla ids are 1–999999
/// in the 1xxx/2xxx/5xxx/9980/1000xx/8000xx/999999 ranges); added to filterresource.fltbnd + Filter.acb next to the
/// untouched vanilla entries.</para>
/// </summary>
public static class Palette
{
    public const string ResourcePath = "filter/filterresource.fltbnd";
    public const int Identity = 100000;

    public static int IdFor(string area, int toneCorrect) => 700000 + int.Parse(area) * 1000 + toneCorrect;

    /// <summary>DS1 ToneCorrect on one display-space colour (0..1).</summary>
    public static Vector3 Correct(Vector3 c, PARAM.Row row)
    {
        double D(string f) => Ds1DrawParam.Get(row, f) is var v && double.IsNaN(v) ? 1 : v;
        var bright = new Vector3((float)D("brightnessR"), (float)D("brightnessG"), (float)D("brightnessB"));
        var contrast = new Vector3((float)D("contrastR"), (float)D("contrastG"), (float)D("contrastB"));
        float sat = (float)D("saturation");
        double hueDeg = Ds1DrawParam.Get(row, "hue") is var h && !double.IsNaN(h) ? h : 0;
        c *= bright;
        c = (c - new Vector3(0.5f)) * contrast + new Vector3(0.5f);
        float l = 0.299f * c.X + 0.587f * c.Y + 0.114f * c.Z;
        c = new Vector3(l) + (c - new Vector3(l)) * sat;
        if (Math.Abs(hueDeg) > 1e-6)
        {
            // rotation about the grey axis (1,1,1)/√3 by hue degrees (Rodrigues)
            double a = hueDeg * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a), k = (1 - cos) / 3, s = sin / Math.Sqrt(3);
            float m00 = (float)(cos + k), m01 = (float)(k - s), m02 = (float)(k + s);
            float m10 = (float)(k + s), m11 = (float)(cos + k), m12 = (float)(k - s);
            float m20 = (float)(k - s), m21 = (float)(k + s), m22 = (float)(cos + k);
            c = new Vector3(m00 * c.X + m01 * c.Y + m02 * c.Z, m10 * c.X + m11 * c.Y + m12 * c.Z, m20 * c.X + m21 * c.Y + m22 * c.Z);
        }
        return Vector3.Clamp(c, Vector3.Zero, Vector3.One);
    }

    // ------------------------------------------------------------------ rule A7c (session 9, track skyfix)

    /// <summary>
    /// Rule A7c: DS2's palette sits AFTER its tone curve, so it can carry DS1's tone curve too. Rule A8c matched only the
    /// display of the adapted key (DS2 exposure = τ × DS1's, τ(0.18) = 0.324); away from the key the curves differ: DS2's
    /// table (ToneMap_WriteFilmicToneMappingTable.fpo) is <c>HejlDawson(x − 0.004) × Scaler</c>, whose −0.004 toe at
    /// τ-scaled exposure blacks out everything DS1 shows below ≈ 0.04 (a DS1 scene value of 0.05 × E: DS1 0.166 →
    /// DS2 0.095 display; with DS1's contrast 1.2 (ToneCorrect 'カラス降り場') that is 0, the session-9 black mountains),
    /// and it is brighter than DS1 above the key (x 1: 0.706 vs 0.582). Each palette entry h (DS2 display) now holds DS1's
    /// display of the same scene value: <c>Correct(UC2(x₁)/UC2(W))^(1/2.2)</c> with <c>x₁ = (HD⁻¹(h / Scaler) + 0.004) / τ</c>
    /// — the identity for the key by construction. Same Hable/Uncharted 2 constants A8c assumes for DSR's mode 0 (P: they
    /// are set by DSR's CPU code into FRPG_Fil_HDR_PBL_ColAdj cb0[54..55], not traced); h = 0 stays 0 (DS2's toe loses
    /// the darkest values; DS1 would show them at 0–22/255 before its colour matrix). A/B lane: LR_TONE_MATCH=0.
    /// </summary>
    public static bool ToneMatchOn { get; } = Environment.GetEnvironmentVariable("LR_TONE_MATCH") != "0";
    public const float Ds2Scaler = 1.1f, Tau = 0.324f;
    const float HA = 0.15f, HB = 0.50f, HC = 0.10f, HD_ = 0.20f, HE = 0.02f, HF = 0.30f, HW = 11.2f;

    static float Hable(float x) => (x * (HA * x + HC * HB) + HD_ * HE) / (x * (HA * x + HB) + HD_ * HF) - HE / HF;
    static float HejlDawson(float x) { x = MathF.Max(0, x); return x * (6.2f * x + 0.5f) / (x * (6.2f * x + 1.7f) + 0.06f); }

    /// <summary>x ≥ 0 with HejlDawson(x) = y (y in [0, 1)).</summary>
    static float HejlDawsonInverse(float y)
    {
        y = Math.Clamp(y, 0, 0.9999f);
        double a = 6.2 - 6.2 * y, b = 0.5 - 1.7 * y, c = -0.06 * y;
        return (float)((-b + Math.Sqrt(b * b - 4 * a * c)) / (2 * a));
    }

    /// <summary>DS2 display value (after its tone table × Scaler) → DS1's display of the same scene value (rule A7c).</summary>
    public static float ToneMatch(float h)
    {
        if (h <= 0) return 0;
        float x2 = HejlDawsonInverse(h / Ds2Scaler) + 0.004f;
        float x1 = x2 / Tau;
        return MathF.Pow(Math.Clamp(Hable(x1) / Hable(HW), 0, 1), 1 / 2.2f);
    }

    /// <summary>The palette TPF: the identity palette's file with DS1's correction applied to every LUT entry.</summary>
    public static byte[] Build(TPF identity, string name, PARAM.Row row, bool? toneMatch = null)
    {
        bool tm = toneMatch ?? ToneMatchOn; // offline display models build both lanes (fix pass)
        var src = identity.Textures[0];
        byte[] dds = (byte[])src.Bytes.Clone();
        const int headerSize = 128, w = 64;
        for (int y = 0; y < w; y++)
            for (int x = 0; x < w; x++)
            {
                int o = headerSize + (y * w + x) * 4; // A8R8G8B8: bytes B, G, R, A
                var c = new Vector3(dds[o + 2], dds[o + 1], dds[o]) / 255f;
                if (tm) c = new Vector3(ToneMatch(c.X), ToneMatch(c.Y), ToneMatch(c.Z)); // rule A7c
                var r = Correct(c, row) * 255f;
                dds[o + 2] = (byte)Math.Clamp(Math.Round(r.X), 0, 255);
                dds[o + 1] = (byte)Math.Clamp(Math.Round(r.Y), 0, 255);
                dds[o] = (byte)Math.Clamp(Math.Round(r.Z), 0, 255);
            }
        var tex = new TPF.Texture(name, src.Format, src.Flags1, dds, TPF.TPFPlatform.PC) { Mipmaps = src.Mipmaps, Type = src.Type, FloatStruct = src.FloatStruct };
        var tpf = new TPF { Platform = identity.Platform, Encoding = identity.Encoding, Flag2 = identity.Flag2, Textures = [tex] };
        return tpf.Write();
    }

    /// <summary>
    /// Adds a palette per ToneCorrect row to filterresource.fltbnd (the pack's copy if an earlier map already changed it,
    /// else DS2's) and its texture asset to Filter.acb; returns ToneCorrect id → palette id. Vanilla entries untouched.
    /// </summary>
    public static Dictionary<int, int> Add(Pack pack, string ds2Data, Ds1DrawParam dp, IEnumerable<int> toneCorrectIds, List<string> log)
    {
        var result = new Dictionary<int, int>();
        byte[] raw = pack.TryGetFile("gamedata", ResourcePath) ?? File.ReadAllBytes(Path.Combine(ds2Data, "filter", "filterresource.fltbnd"));
        bool dcx = DCX.Is(raw);
        DCX.CompressionInfo? info = null;
        if (dcx) { raw = DCX.Decompress(raw, out DCX.CompressionInfo ci); info = ci; }
        var bnd = BND4.Read(raw);
        var identityFile = bnd.Files.FirstOrDefault(f => Path.GetFileName(f.Name).Equals($"f{Identity:D6}.tpf", StringComparison.OrdinalIgnoreCase));
        var acbFile = bnd.Files.FirstOrDefault(f => f.Name.EndsWith(".acb", StringComparison.OrdinalIgnoreCase));
        if (identityFile == null || acbFile == null) { log.Add("atmos: filterresource.fltbnd has no identity palette/ACB: DS1 grading not added"); return result; }
        var identity = TPF.Read(identityFile.Bytes);
        var acb = ACB.Read(acbFile.Bytes);
        var acbTemplate = acb.Assets.OfType<ACB.Asset.Texture>().First(a => a.AbsolutePath.Contains($"f{Identity:D6}", StringComparison.OrdinalIgnoreCase));
        bool changed = false;
        foreach (int tc in toneCorrectIds.Distinct().Order())
        {
            var row = dp.Row("ToneCorrectBank", tc);
            if (row == null) continue;
            int id = IdFor(dp.Area, tc);
            string stem = $"f{id:D6}";
            result[tc] = id;
            if (bnd.Files.Any(f => Path.GetFileName(f.Name).Equals(stem + ".tpf", StringComparison.OrdinalIgnoreCase))) continue;
            string entryName = identityFile.Name[..^Path.GetFileName(identityFile.Name).Length] + stem + ".tpf";
            bnd.Files.Add(new BinderFile(identityFile.Flags, bnd.Files.Max(f => f.ID) + 1, entryName, Build(identity, stem, row)));
            acb.Assets.Add(new ACB.Asset.Texture
            {
                AbsolutePath = acbTemplate.AbsolutePath.Replace($"f{Identity:D6}", stem, StringComparison.OrdinalIgnoreCase),
                RelativePath = acbTemplate.RelativePath.Replace($"f{Identity:D6}", stem, StringComparison.OrdinalIgnoreCase),
            });
            changed = true;
            Vector3 Entry(Vector3 c) => Correct(ToneMatchOn ? new Vector3(ToneMatch(c.X), ToneMatch(c.Y), ToneMatch(c.Z)) : c, row);
            var grey = Entry(new Vector3(85 / 255f)) * 255f;
            var red = Entry(new Vector3(1, 0, 0)) * 255f;
            string curve = ToneMatchOn ? "; tone match (A7c) grey ramp DS2 display → entry: " + string.Join(" ", new[] { 0.02f, 0.05f, 0.1f, 0.2f, 0.3f, 0.5f, 0.7f, 0.9f, 1f }.Select(h => $"{h:0.##}→{Entry(new Vector3(h)).X:0.###}")) : "; tone match off (LR_TONE_MATCH=0)";
            log.Add($"atmos: palette {stem} ← DS1 a{dp.Area} ToneCorrectBank {tc} '{row.Name}' (rule A7b{(ToneMatchOn ? " + A7c" : "")}): grey 85 → ({grey.X:0},{grey.Y:0},{grey.Z:0}), red → ({red.X:0},{red.Y:0},{red.Z:0}){curve}");
        }
        if (changed)
        {
            acbFile.Bytes = acb.Write();
            byte[] outBytes = bnd.Write();
            pack.AddFile("gamedata", ResourcePath, dcx ? DCX.Compress(outBytes, info!) : outBytes);
        }
        return result;
    }
}
