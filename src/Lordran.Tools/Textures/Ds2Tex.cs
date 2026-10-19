using System.Text;

namespace Lordran.Tools.Textures;

/// <summary>
/// DS1 (DSR) textures re-encoded the way DS2 stores them. TPF format bytes DS2 uses: 0 = DXT1, 5 = DXT5,
/// 23 = DXT5 normal map (X in alpha, Y in green, R = B = 255).
/// </summary>
public static class Ds2Tex
{
    public const byte FormatDxt1 = 0, FormatDxt5 = 5, FormatNormal = 23;

    /// <summary>
    /// A colour texture DS2 can read. Legacy-header DXT1 is kept as is; anything else (BC7, DX10 headers) is decoded and
    /// re-encoded, as DXT5 only if it uses alpha.
    /// </summary>
    public static (byte[] Bytes, byte Format) Color(byte[] ds1Dds)
    {
        if (Encoding.ASCII.GetString(ds1Dds, 84, 4) == "DXT1")
            return (ds1Dds, FormatDxt1);
        var image = Dds.Read(ds1Dds).DecodeTop();
        bool alpha = image.Pixels.Any(p => p.A < 250);
        return alpha ? (Dds.Encode(image, BcFormat.BC3), FormatDxt5) : (Dds.Encode(image, BcFormat.BC1), FormatDxt1);
    }

    /// <summary>
    /// <see cref="Color"/> with a colour grade: saturation × <paramref name="saturation"/> around the pixel's luminance,
    /// then × <paramref name="brightness"/>. For DS1 character textures whose look relied on DSR's lighting (session 6:
    /// the Asylum Hollow's skin, mean R98 G70 B67, read pink under DS2's warmer light; DS2's Hollow Infantry is R71 G59 B50).
    /// </summary>
    public static (byte[] Bytes, byte Format) ColorGraded(byte[] ds1Dds, float saturation, float brightness)
    {
        var image = Dds.Read(ds1Dds).DecodeTop();
        for (int i = 0; i < image.Pixels.Length; i++)
        {
            var p = image.Pixels[i];
            float l = 0.299f * p.R + 0.587f * p.G + 0.114f * p.B;
            byte C(float v) => (byte)Math.Clamp(MathF.Round((l + (v - l) * saturation) * brightness), 0, 255);
            image.Pixels[i] = new Rgba(C(p.R), C(p.G), C(p.B), p.A);
        }
        bool alpha = image.Pixels.Any(p => p.A < 250);
        return alpha ? (Dds.Encode(image, BcFormat.BC3), FormatDxt5) : (Dds.Encode(image, BcFormat.BC1), FormatDxt1);
    }

    /// <summary>
    /// A DS1 specular map in DS2's layout. DS2's map "_s" textures are packed: R = specular intensity (mostly 0..70),
    /// G = gloss (mostly 0..80), B ≈ 126 on every texture, A = 255 (measured on Forest of Fallen Giants / Things Betwixt,
    /// session 5). DSR's are specular colours (DXT1) or colour + gloss in alpha (BC7). Passed through raw, their low B
    /// gave the Asylum's walls strong blue environment reflections. R = luminance × 0.5, G = alpha × 0.25 (or 32), B = 126.
    /// </summary>
    public static byte[] Specular(byte[] ds1Dds)
    {
        var src = Dds.Read(ds1Dds).DecodeTop();
        bool hasGloss = src.Pixels.Any(p => p.A < 250);
        var dst = new Image(src.Width, src.Height);
        for (int i = 0; i < src.Pixels.Length; i++)
        {
            var p = src.Pixels[i];
            double lum = 0.299 * p.R + 0.587 * p.G + 0.114 * p.B;
            dst.Pixels[i] = new Rgba((byte)Math.Clamp(Math.Round(lum * 0.5), 0, 255), (byte)(hasGloss ? Math.Round(p.A * 0.25) : 32), 126, 255);
        }
        return Dds.Encode(dst, BcFormat.BC1);
    }

    /// <summary>DS1 normal maps are BC5 (X = R, Y = G); DS2's are DXT5nm (X = A, Y = G, R = B = 255).</summary>
    public static byte[] Normal(byte[] ds1Dds)
    {
        var src = Dds.Read(ds1Dds).DecodeTop();
        var dst = new Image(src.Width, src.Height);
        for (int i = 0; i < src.Pixels.Length; i++)
            dst.Pixels[i] = new Rgba(255, src.Pixels[i].G, 255, src.Pixels[i].R);
        return Dds.Encode(dst, BcFormat.BC3);
    }

    /// <summary>
    /// A DS1 baked lightmap as a DS2 GI texture (DXT5): DS2's GI RGB is indirect light and its alpha is the baked
    /// sun-visibility mask (multiplied with the dynamic sun). DS1 lightmaps already contain all light, so RGB = lightmap
    /// × <paramref name="gain"/> and alpha = <paramref name="sun"/> (0 = no sun, right for interiors).
    /// </summary>
    public static byte[] GiFromLightmap(byte[] ds1Dds, double gain, byte sun)
    {
        var src = Dds.Read(ds1Dds).DecodeTop();
        var dst = new Image(src.Width, src.Height);
        static byte Scale(byte v, double g) => (byte)Math.Clamp(Math.Round(v * g), 0, 255);
        for (int i = 0; i < src.Pixels.Length; i++)
        {
            var p = src.Pixels[i];
            dst.Pixels[i] = new Rgba(Scale(p.R, gain), Scale(p.G, gain), Scale(p.B, gain), sun);
        }
        return Dds.Encode(dst, BcFormat.BC3);
    }

    /// <summary>A flat normal (DXT5nm) for materials whose DS2 shader wants a bump map DS1 doesn't have.</summary>
    public static byte[] FlatNormal()
    {
        var img = new Image(4, 4);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = new Rgba(255, 128, 255, 128);
        return Dds.Encode(img, BcFormat.BC3, mipmaps: false);
    }

    /// <summary>A constant GI texture (DXT5: grey indirect light, alpha 0 = no baked sun) for models without a DS1 lightmap.</summary>
    public static byte[] GiSolid(byte grey)
    {
        var img = new Image(4, 4);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = new Rgba(grey, grey, grey, 0);
        return Dds.Encode(img, BcFormat.BC3, mipmaps: false);
    }

    public static byte[] Solid(byte r, byte g, byte b)
    {
        var img = new Image(4, 4);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = new Rgba(r, g, b, 255);
        return Dds.Encode(img, BcFormat.BC1, mipmaps: false);
    }

    public static int MipCount(byte[] dds) => Dds.Read(dds).Mips.Count;

    /// <summary>The texture shrunk to at most <paramref name="max"/> pixels a side (DS2's "_low" copies are 128x128), as uncompressed-source BC3/BC1.</summary>
    public static byte[] Downscale(byte[] dds, int max)
    {
        var d = Dds.Read(dds);
        if (d.Width <= max && d.Height <= max) return dds;
        var img = d.DecodeTop();
        int w = Math.Max(4, d.Width * max / Math.Max(d.Width, d.Height)), h = Math.Max(4, d.Height * max / Math.Max(d.Width, d.Height));
        var small = img.Resize(w, h);
        bool alpha = small.Pixels.Any(p => p.A < 250);
        return Dds.Encode(small, d.Format is BcFormat.BC5 ? BcFormat.BC5 : alpha ? BcFormat.BC3 : BcFormat.BC1);
    }
}
