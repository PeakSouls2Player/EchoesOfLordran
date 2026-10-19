using System.IO.Compression;
using System.Text;

namespace Lordran.Tools.Textures;

/// <summary>Writes RGBA8 PNGs (for eyeballing converted textures and icons).</summary>
public static class Png
{
    /// <summary>Reads an 8-bit RGB or RGBA, non-interlaced PNG (e.g. a screenshot saved by System.Drawing).</summary>
    public static Image Load(string path)
    {
        byte[] f = File.ReadAllBytes(path);
        int pos = 8, width = 0, height = 0, colorType = 0;
        using var idat = new MemoryStream();
        while (pos < f.Length)
        {
            int len = ReadBE(f, pos); string type = Encoding.ASCII.GetString(f, pos + 4, 4);
            if (type == "IHDR")
            {
                width = ReadBE(f, pos + 8); height = ReadBE(f, pos + 12); colorType = f[pos + 17];
                if (f[pos + 16] != 8 || f[pos + 20] != 0 || (colorType != 2 && colorType != 6)) throw new NotSupportedException($"{path}: only 8-bit RGB/RGBA non-interlaced PNGs");
            }
            else if (type == "IDAT") idat.Write(f, pos + 8, len);
            else if (type == "IEND") break;
            pos += 12 + len;
        }
        int bpp = colorType == 6 ? 4 : 3, stride = width * bpp;
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        var raw = new byte[(stride + 1) * height];
        int read = 0; while (read < raw.Length) { int n = z.Read(raw, read, raw.Length - read); if (n <= 0) break; read += n; }
        var cur = new byte[stride]; var prev = new byte[stride];
        var image = new Image(width, height);
        for (int y = 0; y < height; y++)
        {
            int filter = raw[y * (stride + 1)];
            for (int i = 0; i < stride; i++)
            {
                int x = raw[y * (stride + 1) + 1 + i], a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                int p = filter switch { 1 => a, 2 => b, 3 => (a + b) / 2, 4 => Paeth(a, b, c), _ => 0 };
                cur[i] = (byte)(x + p);
            }
            for (int xx = 0; xx < width; xx++)
                image[xx, y] = new Rgba(cur[xx * bpp], cur[xx * bpp + 1], cur[xx * bpp + 2], bpp == 4 ? cur[xx * bpp + 3] : (byte)255);
            (cur, prev) = (prev, cur);
        }
        return image;
    }

    static int Paeth(int a, int b, int c) { int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c); return pa <= pb && pa <= pc ? a : pb <= pc ? b : c; }
    static int ReadBE(byte[] b, int o) => b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3];

    public static void Save(Image image, string path)
    {
        using var raw = new MemoryStream();
        for (int y = 0; y < image.Height; y++)
        {
            raw.WriteByte(0); // filter: none
            for (int x = 0; x < image.Width; x++)
            {
                var p = image[x, y];
                raw.WriteByte(p.R); raw.WriteByte(p.G); raw.WriteByte(p.B); raw.WriteByte(p.A);
            }
        }
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            raw.WriteTo(z);

        using var file = File.Create(path);
        file.Write(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, image.Width);
        WriteBE(ihdr, 4, image.Height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // RGBA
        Chunk(file, "IHDR", ihdr);
        Chunk(file, "IDAT", compressed.ToArray());
        Chunk(file, "IEND", Array.Empty<byte>());
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, data.Length);
        s.Write(len);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        uint crc = Crc(typeBytes, data);
        var crcBytes = new byte[4];
        WriteBE(crcBytes, 0, (int)crc);
        s.Write(crcBytes);
    }

    static void WriteBE(byte[] b, int o, int v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }

    static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    static uint Crc(byte[] a, byte[] b)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte x in a) c = Table[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (byte x in b) c = Table[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }
}
