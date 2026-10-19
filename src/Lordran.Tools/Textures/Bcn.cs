namespace Lordran.Tools.Textures;

/// <summary>
/// Block-compression codecs for the formats DS1 and DS2 use: BC1 (DXT1), BC3 (DXT5), BC4, BC5.
/// Decoders follow the D3D spec; encoders use a bounding-box range fit with endpoint refinement,
/// which is plenty for icons and for re-packing already-compressed game textures.
/// </summary>
public static class Bcn
{
    // ---- decoding ---------------------------------------------------------------------------

    /// <summary>Decodes one BC1 block into 16 RGBA pixels (row-major). bc3Color forces 4-colour mode.</summary>
    public static void DecodeBC1(ReadOnlySpan<byte> block, Span<Rgba> outPixels, bool bc3Color = false)
    {
        ushort c0 = (ushort)(block[0] | block[1] << 8), c1 = (ushort)(block[2] | block[3] << 8);
        Span<Rgba> palette = stackalloc Rgba[4];
        palette[0] = Rgba.From565(c0);
        palette[1] = Rgba.From565(c1);
        if (c0 > c1 || bc3Color)
        {
            palette[2] = Rgba.Lerp(palette[0], palette[1], 1, 3);
            palette[3] = Rgba.Lerp(palette[0], palette[1], 2, 3);
        }
        else
        {
            palette[2] = Rgba.Lerp(palette[0], palette[1], 1, 2);
            palette[3] = new Rgba(0, 0, 0, 0);
        }
        uint indices = (uint)(block[4] | block[5] << 8 | block[6] << 16 | block[7] << 24);
        for (int i = 0; i < 16; i++)
            outPixels[i] = palette[(int)(indices >> (2 * i)) & 3];
    }

    /// <summary>Decodes one BC4 (or BC3 alpha) block into 16 values.</summary>
    public static void DecodeBC4(ReadOnlySpan<byte> block, Span<byte> outValues)
    {
        Span<byte> palette = stackalloc byte[8];
        BC4Palette(block[0], block[1], palette);
        ulong bits = 0;
        for (int i = 0; i < 6; i++)
            bits |= (ulong)block[2 + i] << (8 * i);
        for (int i = 0; i < 16; i++)
            outValues[i] = palette[(int)(bits >> (3 * i)) & 7];
    }

    static void BC4Palette(byte a0, byte a1, Span<byte> palette)
    {
        palette[0] = a0;
        palette[1] = a1;
        if (a0 > a1)
            for (int i = 1; i < 7; i++) palette[i + 1] = (byte)(((7 - i) * a0 + i * a1 + 3) / 7);
        else
        {
            for (int i = 1; i < 5; i++) palette[i + 1] = (byte)(((5 - i) * a0 + i * a1 + 2) / 5);
            palette[6] = 0;
            palette[7] = 255;
        }
    }

    public static Image Decode(BcFormat format, int width, int height, ReadOnlySpan<byte> data)
    {
        var image = new Image(width, height);
        int blocksX = Math.Max(1, (width + 3) / 4), blocksY = Math.Max(1, (height + 3) / 4);
        int blockSize = format is BcFormat.BC1 or BcFormat.BC4 ? 8 : 16;
        Span<Rgba> px = stackalloc Rgba[16];
        Span<byte> a = stackalloc byte[16];
        Span<byte> b = stackalloc byte[16];
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                var block = data.Slice((by * blocksX + bx) * blockSize, blockSize);
                switch (format)
                {
                    case BcFormat.BC1:
                        DecodeBC1(block, px);
                        break;
                    case BcFormat.BC3:
                        DecodeBC4(block[..8], a);
                        DecodeBC1(block[8..], px, bc3Color: true);
                        for (int i = 0; i < 16; i++) px[i].A = a[i];
                        break;
                    case BcFormat.BC4:
                        DecodeBC4(block, a);
                        for (int i = 0; i < 16; i++) px[i] = new Rgba(a[i], a[i], a[i], 255);
                        break;
                    case BcFormat.BC5:
                        DecodeBC4(block[..8], a);
                        DecodeBC4(block[8..], b);
                        for (int i = 0; i < 16; i++) px[i] = new Rgba(a[i], b[i], ReconstructZ(a[i], b[i]), 255);
                        break;
                }
                for (int i = 0; i < 16; i++)
                {
                    int x = bx * 4 + (i & 3), y = by * 4 + (i >> 2);
                    if (x < width && y < height)
                        image[x, y] = px[i];
                }
            }
        }
        return image;
    }

    /// <summary>Blue channel of a tangent-space normal stored as two channels.</summary>
    public static byte ReconstructZ(byte r, byte g)
    {
        double x = r / 127.5 - 1, y = g / 127.5 - 1;
        double z = Math.Sqrt(Math.Max(0, 1 - x * x - y * y));
        return (byte)Math.Clamp(Math.Round((z + 1) * 127.5), 0, 255);
    }

    // ---- encoding ---------------------------------------------------------------------------

    public static byte[] Encode(BcFormat format, Image image)
    {
        int blocksX = Math.Max(1, (image.Width + 3) / 4), blocksY = Math.Max(1, (image.Height + 3) / 4);
        int blockSize = format is BcFormat.BC1 or BcFormat.BC4 ? 8 : 16;
        var output = new byte[blocksX * blocksY * blockSize];
        Span<Rgba> px = stackalloc Rgba[16];
        Span<byte> a = stackalloc byte[16];
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                for (int i = 0; i < 16; i++)
                    px[i] = image[Math.Min(bx * 4 + (i & 3), image.Width - 1), Math.Min(by * 4 + (i >> 2), image.Height - 1)];
                var block = output.AsSpan((by * blocksX + bx) * blockSize, blockSize);
                switch (format)
                {
                    case BcFormat.BC1:
                        EncodeBC1(px, block);
                        break;
                    case BcFormat.BC3:
                        for (int i = 0; i < 16; i++) a[i] = px[i].A;
                        EncodeBC4(a, block[..8]);
                        EncodeBC1(px, block[8..], bc3Color: true);
                        break;
                    case BcFormat.BC4:
                        for (int i = 0; i < 16; i++) a[i] = px[i].R;
                        EncodeBC4(a, block);
                        break;
                    case BcFormat.BC5:
                        for (int i = 0; i < 16; i++) a[i] = px[i].R;
                        EncodeBC4(a, block[..8]);
                        for (int i = 0; i < 16; i++) a[i] = px[i].G;
                        EncodeBC4(a, block[8..]);
                        break;
                }
            }
        }
        return output;
    }

    static void EncodeBC1(ReadOnlySpan<Rgba> px, Span<byte> block, bool bc3Color = false)
    {
        // Bounding box of the colours (ignoring transparent texels in BC1 mode), inset slightly.
        bool anyTransparent = false;
        int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
        foreach (var p in px)
        {
            if (!bc3Color && p.A < 128) { anyTransparent = true; continue; }
            minR = Math.Min(minR, p.R); minG = Math.Min(minG, p.G); minB = Math.Min(minB, p.B);
            maxR = Math.Max(maxR, p.R); maxG = Math.Max(maxG, p.G); maxB = Math.Max(maxB, p.B);
        }
        if (minR > maxR) { minR = minG = minB = maxR = maxG = maxB = 0; }
        int insetR = (maxR - minR) >> 4, insetG = (maxG - minG) >> 4, insetB = (maxB - minB) >> 4;
        ushort hi = Rgba.To565(maxR - insetR, maxG - insetG, maxB - insetB);
        ushort lo = Rgba.To565(minR + insetR, minG + insetG, minB + insetB);

        bool threeColor = anyTransparent;
        if (threeColor ? hi > lo : hi < lo) (hi, lo) = (lo, hi);
        if (!threeColor && hi == lo)
        {
            // Degenerate block: 4-colour mode needs c0 > c1.
            if (hi < 0xFFFF) hi++; else lo--;
        }

        block[0] = (byte)hi; block[1] = (byte)(hi >> 8);
        block[2] = (byte)lo; block[3] = (byte)(lo >> 8);

        Span<Rgba> pal = stackalloc Rgba[4];
        pal[0] = Rgba.From565(hi);
        pal[1] = Rgba.From565(lo);
        if (!threeColor)
        {
            pal[2] = Rgba.Lerp(pal[0], pal[1], 1, 3);
            pal[3] = Rgba.Lerp(pal[0], pal[1], 2, 3);
        }
        else
        {
            pal[2] = Rgba.Lerp(pal[0], pal[1], 1, 2);
            pal[3] = new Rgba(0, 0, 0, 0);
        }

        uint indices = 0;
        for (int i = 0; i < 16; i++)
        {
            int best = 0;
            if (threeColor && px[i].A < 128)
                best = 3;
            else
            {
                int bestDist = int.MaxValue;
                for (int c = 0; c < (threeColor ? 3 : 4); c++)
                {
                    int d = Rgba.Distance(px[i], pal[c]);
                    if (d < bestDist) { bestDist = d; best = c; }
                }
            }
            indices |= (uint)best << (2 * i);
        }
        block[4] = (byte)indices; block[5] = (byte)(indices >> 8);
        block[6] = (byte)(indices >> 16); block[7] = (byte)(indices >> 24);
    }

    static void EncodeBC4(ReadOnlySpan<byte> values, Span<byte> block)
    {
        byte min = 255, max = 0;
        foreach (byte v in values) { if (v < min) min = v; if (v > max) max = v; }
        // 8-value mode (a0 > a1); a flat block uses a0 == a1 which decodes fine in either mode.
        block[0] = max;
        block[1] = min;
        Span<byte> palette = stackalloc byte[8];
        BC4Palette(max, min, palette);
        ulong bits = 0;
        for (int i = 0; i < 16; i++)
        {
            int best = 0, bestDist = int.MaxValue;
            for (int c = 0; c < 8; c++)
            {
                int d = Math.Abs(values[i] - palette[c]);
                if (d < bestDist) { bestDist = d; best = c; }
            }
            bits |= (ulong)best << (3 * i);
        }
        for (int i = 0; i < 6; i++)
            block[2 + i] = (byte)(bits >> (8 * i));
    }
}

/// <summary>BC7 is decode-only (via Pfim, see <see cref="Dds.DecodeTop"/>).</summary>
public enum BcFormat { BC1, BC3, BC4, BC5, BC7 }

public struct Rgba
{
    public byte R, G, B, A;

    public Rgba(byte r, byte g, byte b, byte a) { R = r; G = g; B = b; A = a; }

    public static Rgba From565(ushort c)
    {
        int r = (c >> 11) & 31, g = (c >> 5) & 63, b = c & 31;
        return new Rgba((byte)(r << 3 | r >> 2), (byte)(g << 2 | g >> 4), (byte)(b << 3 | b >> 2), 255);
    }

    public static ushort To565(int r, int g, int b) =>
        (ushort)((Math.Clamp(r, 0, 255) * 31 + 127) / 255 << 11 | (Math.Clamp(g, 0, 255) * 63 + 127) / 255 << 5 | (Math.Clamp(b, 0, 255) * 31 + 127) / 255);

    public static Rgba Lerp(Rgba a, Rgba b, int num, int den) => new(
        (byte)((a.R * (den - num) + b.R * num) / den),
        (byte)((a.G * (den - num) + b.G * num) / den),
        (byte)((a.B * (den - num) + b.B * num) / den),
        255);

    public static int Distance(Rgba a, Rgba b)
    {
        int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return dr * dr * 3 + dg * dg * 4 + db * db * 2;
    }
}
