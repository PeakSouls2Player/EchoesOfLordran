namespace Lordran.Tools.Textures;

/// <summary>A simple RGBA8 image with the operations the converters need (crop, resize, pad, mips).</summary>
public sealed class Image
{
    public int Width { get; }
    public int Height { get; }
    public Rgba[] Pixels { get; }

    public Image(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new Rgba[width * height];
    }

    public ref Rgba this[int x, int y] => ref Pixels[y * Width + x];

    public Image Crop(int left, int top, int width, int height)
    {
        var result = new Image(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                result[x, y] = this[Math.Clamp(left + x, 0, Width - 1), Math.Clamp(top + y, 0, Height - 1)];
        return result;
    }

    /// <summary>Bilinear resample (alpha-weighted so transparent edges don't darken colour).</summary>
    public Image Resize(int width, int height)
    {
        var result = new Image(width, height);
        double sx = (double)Width / width, sy = (double)Height / height;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double fx = (x + 0.5) * sx - 0.5, fy = (y + 0.5) * sy - 0.5;
                int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
                double tx = fx - x0, ty = fy - y0;
                double r = 0, g = 0, b = 0, a = 0;
                for (int k = 0; k < 4; k++)
                {
                    int px = Math.Clamp(x0 + (k & 1), 0, Width - 1), py = Math.Clamp(y0 + (k >> 1), 0, Height - 1);
                    double w = ((k & 1) == 1 ? tx : 1 - tx) * ((k >> 1) == 1 ? ty : 1 - ty);
                    var p = this[px, py];
                    double wa = w * p.A;
                    r += p.R * wa; g += p.G * wa; b += p.B * wa; a += wa;
                }
                result[x, y] = a > 0
                    ? new Rgba((byte)Math.Round(r / a), (byte)Math.Round(g / a), (byte)Math.Round(b / a), (byte)Math.Round(a))
                    : new Rgba(0, 0, 0, 0);
            }
        }
        return result;
    }

    /// <summary>Area-average downscale (each output pixel = coverage-weighted mean of the source pixels it spans):
    /// keeps thin strokes and serifs that bilinear sampling skips at non-integer ratios.</summary>
    public Image ResizeArea(int width, int height)
    {
        var result = new Image(width, height);
        double sx = (double)Width / width, sy = (double)Height / height;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                double x0 = x * sx, x1 = (x + 1) * sx, y0 = y * sy, y1 = (y + 1) * sy;
                double r = 0, g = 0, b = 0, a = 0, total = 0;
                for (int py = (int)y0; py < Math.Min(Height, (int)Math.Ceiling(y1)); py++)
                    for (int px = (int)x0; px < Math.Min(Width, (int)Math.Ceiling(x1)); px++)
                    {
                        double w = (Math.Min(x1, px + 1) - Math.Max(x0, px)) * (Math.Min(y1, py + 1) - Math.Max(y0, py));
                        if (w <= 0) continue;
                        var p = this[px, py];
                        r += p.R * w; g += p.G * w; b += p.B * w; a += p.A * w; total += w;
                    }
                result[x, y] = total > 0
                    ? new Rgba((byte)Math.Round(r / total), (byte)Math.Round(g / total), (byte)Math.Round(b / total), (byte)Math.Round(a / total))
                    : default;
            }
        return result;
    }

    /// <summary>Places this image centred on a transparent canvas.</summary>
    public Image PadTo(int width, int height)
    {
        var result = new Image(width, height);
        int ox = (width - Width) / 2, oy = (height - Height) / 2;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (x + ox >= 0 && x + ox < width && y + oy >= 0 && y + oy < height)
                    result[x + ox, y + oy] = this[x, y];
        return result;
    }

    /// <summary>Full mip chain (this image first), each level a 2x box downsample.</summary>
    public List<Image> MipChain()
    {
        var chain = new List<Image> { this };
        while (chain[^1].Width > 1 || chain[^1].Height > 1)
        {
            var prev = chain[^1];
            int w = Math.Max(1, prev.Width / 2), h = Math.Max(1, prev.Height / 2);
            chain.Add(prev.Resize(w, h));
        }
        return chain;
    }
}
