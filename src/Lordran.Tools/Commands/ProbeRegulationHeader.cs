using System.Security.Cryptography;
using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// probe-regulation-header [file] — checks whether bytes of the 32-byte enc_regulation header are a digest of the
/// payload (ciphertext, DCX plaintext, or decompressed BND4) under MD5/SHA-1/SHA-256, anywhere in the header.
/// </summary>
public static class ProbeRegulationHeader
{
    public static int Run(string[] args)
    {
        string path = args.Length > 1 ? args[1] : Path.Combine(Program.DefaultDs2Root, "enc_regulation.bnd.dcx");
        byte[] file = File.ReadAllBytes(path);
        byte[] header = file[..32];
        byte[] cipher = file[32..];
        byte[] plain = Ds2RegulationCipher.Decrypt(file);
        byte[] bnd = DCX.Decompress(plain);
        Console.WriteLine($"header: {Convert.ToHexString(header)}");
        Console.WriteLine($"sizes: file={file.Length} cipher={cipher.Length} dcx={plain.Length} bnd={bnd.Length}");
        Console.WriteLine($"DCX header: {Convert.ToHexString(plain[..0x4C])}");
        Console.WriteLine($"compressed stream start: {Convert.ToHexString(plain.AsSpan(0x4C, 16))}  end: {Convert.ToHexString(plain.AsSpan(plain.Length - 8))}");
        Console.WriteLine($"BND4 header: {Convert.ToHexString(bnd[..0x40])}");
        Console.WriteLine($"BND4 header ascii: {new string(bnd[..0x40].Select(b => b is >= 32 and < 127 ? (char)b : '.').ToArray())}");
        var parsed = BND4.Read(bnd);
        Console.WriteLine($"BND4: version='{parsed.Version}' format={parsed.Format} unicode={parsed.Unicode} extended={parsed.Extended} unk04={parsed.Unk04} unk05={parsed.Unk05} files={parsed.Files.Count}");
        Console.WriteLine($"first files: {string.Join(", ", parsed.Files.Take(3).Select(f => $"[{f.ID}] {f.Name} flags={f.Flags}"))}");

        var inputs = new (string, byte[])[] { ("cipher", cipher), ("dcx-plain", plain), ("bnd", bnd), ("whole-file-after-16", file[16..]) };
        var digests = new (string, Func<byte[], byte[]>)[] { ("MD5", MD5.HashData), ("SHA1", SHA1.HashData), ("SHA256", SHA256.HashData) };
        bool any = false;
        foreach (var (iname, data) in inputs)
        foreach (var (dname, fn) in digests)
        {
            byte[] h = fn(data);
            for (int len = 16; len >= 8; len -= 4)
            {
                int at = IndexOf(header, h.AsSpan(0, len));
                if (at >= 0)
                {
                    Console.WriteLine($"MATCH: {dname}({iname})[0..{len}] at header offset {at}");
                    any = true;
                    break;
                }
            }
        }
        if (!any) Console.WriteLine("no digest of the payload found in the header");
        return 0;
    }

    static int IndexOf(byte[] hay, ReadOnlySpan<byte> needle) => hay.AsSpan().IndexOf(needle);
}
