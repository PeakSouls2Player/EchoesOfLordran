using System.Text;

namespace Lordran.Tools.Textures;

/// <summary>Minimal DDS reader/writer for block-compressed textures (legacy FourCC and DX10 headers).</summary>
public sealed class Dds
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int MipCount { get; init; }
    public BcFormat Format { get; init; }
    /// <summary>Compressed data for each mip level, largest first.</summary>
    public List<byte[]> Mips { get; init; } = new();
    /// <summary>The whole file as read (BC7 is decoded from it by Pfim).</summary>
    public byte[] Raw { get; init; } = [];

    const int DXGI_BC1 = 71, DXGI_BC3 = 77, DXGI_BC4 = 80, DXGI_BC5 = 83, DXGI_BC7 = 98;

    public static Dds Read(byte[] bytes)
    {
        if (Encoding.ASCII.GetString(bytes, 0, 4) != "DDS ")
            throw new InvalidDataException("not a DDS file");
        int height = BitConverter.ToInt32(bytes, 12), width = BitConverter.ToInt32(bytes, 16);
        int mips = Math.Max(1, BitConverter.ToInt32(bytes, 28));
        string fourcc = Encoding.ASCII.GetString(bytes, 84, 4);
        int offset = 128;
        BcFormat format = fourcc switch
        {
            "DXT1" => BcFormat.BC1,
            "DXT5" => BcFormat.BC3,
            "ATI1" or "BC4U" => BcFormat.BC4,
            "ATI2" or "BC5U" => BcFormat.BC5,
            "DX10" => BitConverter.ToInt32(bytes, 128) switch
            {
                DXGI_BC1 or DXGI_BC1 + 1 => BcFormat.BC1,
                DXGI_BC3 or DXGI_BC3 + 1 => BcFormat.BC3,
                DXGI_BC4 or DXGI_BC4 + 1 => BcFormat.BC4,
                DXGI_BC5 or DXGI_BC5 + 1 => BcFormat.BC5,
                DXGI_BC7 or DXGI_BC7 + 1 => BcFormat.BC7,
                int dxgi => throw new NotSupportedException($"DXGI format {dxgi}"),
            },
            _ => throw new NotSupportedException($"FourCC '{fourcc}'"),
        };
        if (fourcc == "DX10") offset += 20;

        var dds = new Dds { Width = width, Height = height, MipCount = mips, Format = format, Raw = bytes };
        int blockSize = format is BcFormat.BC1 or BcFormat.BC4 ? 8 : 16;
        for (int level = 0, w = width, h = height; level < mips && offset < bytes.Length; level++)
        {
            int size = Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * blockSize;
            dds.Mips.Add(bytes.AsSpan(offset, Math.Min(size, bytes.Length - offset)).ToArray());
            offset += size;
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }
        return dds;
    }

    public Image DecodeTop()
    {
        if (Format != BcFormat.BC7)
            return Bcn.Decode(Format, Width, Height, Mips[0]);
        // BC7: Pfim's decoder (a port of DirectXTex's), output BGRA.
        using var decoded = Pfim.Pfimage.FromStream(new MemoryStream(Raw));
        var image = new Image(decoded.Width, decoded.Height);
        for (int y = 0; y < decoded.Height; y++)
            for (int x = 0; x < decoded.Width; x++)
            {
                int o = y * decoded.Stride + x * 4;
                image.Pixels[y * decoded.Width + x] = new Rgba(decoded.Data[o + 2], decoded.Data[o + 1], decoded.Data[o], decoded.Data[o + 3]);
            }
        return image;
    }

    /// <summary>Encodes an image (with a full mip chain) as a legacy-header DDS (DXT1/DXT5 FourCC; BC4/BC5 as ATI1/ATI2).</summary>
    public static byte[] Encode(Image image, BcFormat format, bool mipmaps = true)
    {
        var levels = mipmaps ? image.MipChain() : new List<Image> { image };
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        int blockSize = format is BcFormat.BC1 or BcFormat.BC4 ? 8 : 16;

        w.Write(Encoding.ASCII.GetBytes("DDS "));
        w.Write(124);                                   // header size
        w.Write(0x1 | 0x2 | 0x4 | 0x1000 | 0x80000 | (levels.Count > 1 ? 0x20000 : 0)); // caps|height|width|pixelformat|linearsize|mipcount
        w.Write(image.Height);
        w.Write(image.Width);
        w.Write(Math.Max(1, (image.Width + 3) / 4) * Math.Max(1, (image.Height + 3) / 4) * blockSize);
        w.Write(0);                                     // depth
        w.Write(levels.Count);
        for (int i = 0; i < 11; i++) w.Write(0);       // reserved
        w.Write(32);                                    // pixel format size
        w.Write(0x4);                                   // DDPF_FOURCC
        w.Write(Encoding.ASCII.GetBytes(format switch { BcFormat.BC1 => "DXT1", BcFormat.BC3 => "DXT5", BcFormat.BC4 => "ATI1", _ => "ATI2" }));
        for (int i = 0; i < 5; i++) w.Write(0);        // rgb bit count + masks
        w.Write(0x1000 | (levels.Count > 1 ? 0x400008 : 0)); // caps: texture | complex | mipmap
        for (int i = 0; i < 4; i++) w.Write(0);        // caps2..4, reserved

        foreach (var level in levels)
            w.Write(Bcn.Encode(format, level));
        return ms.ToArray();
    }
}
