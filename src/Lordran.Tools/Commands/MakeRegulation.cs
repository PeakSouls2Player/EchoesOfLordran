using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// make-regulation MODE OUT — writes diagnostic variants of DS2's vanilla regulation (no content changes):
///   reencrypt   decrypt → encrypt (byte-identical to vanilla; sanity check)
///   recompress  DCX decompress → DCX compress with SoulsFormats (same archive bytes, our zlib output)
///   rewrite     BND4 read → write (DS2 layout) → DCX compress
/// </summary>
public static class MakeRegulation
{
    public static int Run(string[] args)
    {
        string mode = args[1], outPath = args[2];
        byte[] original = File.ReadAllBytes(Path.Combine(Program.DefaultDs2Root, "enc_regulation.bnd.dcx"));
        byte[] plain = Ds2RegulationCipher.Decrypt(original);
        if (mode.StartsWith("merge:"))
        {
            // merge:Param+Param:FROM — vanilla, with the listed params' files taken from regulation FROM.
            string[] parts = mode.Split(':', 3);
            var take = parts[1].Split('+').ToHashSet(StringComparer.OrdinalIgnoreCase);
            var from = BND4.Read(Ds2RegulationCipher.Decrypt(File.ReadAllBytes(parts[2])));
            var bnd = BND4.Read(plain);
            foreach (var f in bnd.Files)
            {
                string name = Path.GetFileNameWithoutExtension(f.Name);
                if (!take.Contains(name)) continue;
                f.Bytes = from.Files.First(x => x.Name == f.Name).Bytes;
                Console.WriteLine($"  merged {name}");
            }
            File.WriteAllBytes(outPath, Ds2RegulationCipher.Encrypt(Ds2RegulationCipher.WriteBnd(bnd), original));
            Console.WriteLine($"{mode}: wrote {outPath}");
            return 0;
        }

        byte[] result = mode switch
        {
            "reencrypt" => plain,
            "recompress" => DCX.Compress(DCX.Decompress(plain, out var compression), compression),
            "rewrite" => Ds2RegulationCipher.WriteBnd(BND4.Read(plain)),
            _ => throw new ArgumentException($"unknown mode {mode}"),
        };
        File.WriteAllBytes(outPath, Ds2RegulationCipher.Encrypt(result, original));
        Console.WriteLine($"{mode}: wrote {outPath} ({new FileInfo(outPath).Length:N0} bytes; vanilla {original.Length:N0})");
        Console.WriteLine($"  DCX header: {Convert.ToHexString(result.AsSpan(0, 0x4C))}");
        Console.WriteLine($"  vanilla   : {Convert.ToHexString(plain.AsSpan(0, 0x4C))}");
        return 0;
    }
}
