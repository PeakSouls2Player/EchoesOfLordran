using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// verify-regulation — checks our DS2 regulation cipher against Smithbox's decryptor (same files, same bytes),
/// then round-trips: decrypt → encrypt with the original header → must reproduce the original file exactly.
/// </summary>
public static class VerifyRegulation
{
    public static int Run()
    {
        string path = Path.Combine(Program.DefaultDs2Root, "enc_regulation.bnd.dcx");
        byte[] original = File.ReadAllBytes(path);

        byte[] ours = Ds2RegulationCipher.Decrypt(original);
        var oursBnd = BND4.Read(ours);
        var theirs = BND4.Read(Ds2Regulation.Decrypt(path));

        bool same = oursBnd.Files.Count == theirs.Files.Count &&
                    oursBnd.Files.Zip(theirs.Files).All(p => p.First.Name == p.Second.Name && p.First.Bytes.AsSpan().SequenceEqual(p.Second.Bytes));
        Console.WriteLine($"decrypt matches Smithbox: {same} ({oursBnd.Files.Count} files, DCX={DCX.Is(ours)})");

        // Container fidelity: BND4 read → write (uncompressed) must reproduce the original decompressed archive.
        byte[] originalBnd = DCX.Decompress(ours);
        byte[] rewrittenBnd = Ds2RegulationCipher.WriteBnd(BND4.Read(originalBnd), new DCX.NoCompressionInfo());
        int firstDiff = Enumerable.Range(0, Math.Min(originalBnd.Length, rewrittenBnd.Length)).FirstOrDefault(i => originalBnd[i] != rewrittenBnd[i], -1);
        Console.WriteLine($"BND4 rewrite identical: {originalBnd.Length == rewrittenBnd.Length && firstDiff < 0} " +
                          $"(original {originalBnd.Length}, rewritten {rewrittenBnd.Length}, first diff {(firstDiff < 0 ? "none" : $"0x{firstDiff:X}")})");

        if (firstDiff >= 0)
        {
            for (int row = 0x40; row < 0xA0; row += 0x20)
            {
                Console.WriteLine($"  orig {row:X4}: {Convert.ToHexString(originalBnd.AsSpan(row, 0x20))}");
                Console.WriteLine($"  ours {row:X4}: {Convert.ToHexString(rewrittenBnd.AsSpan(row, 0x20))}");
            }
            var a = BND4.Read(originalBnd);
            Console.WriteLine($"  first file names: {string.Join(" | ", a.Files.Take(2).Select(f => f.Name))}");
        }

        // CWC tag: the game recomputes it over the ciphertext and rejects the file on a mismatch.
        byte[] cipher = original[32..];
        byte[] tag = Ds2RegulationCipher.ComputeTag(original.AsSpan(0, 16), cipher);
        bool tagOk = tag.AsSpan().SequenceEqual(Ds2RegulationCipher.StoredTag(original));
        Console.WriteLine($"CWC tag matches the shipped header: {tagOk} (stored {Convert.ToHexString(Ds2RegulationCipher.StoredTag(original))}, computed {Convert.ToHexString(tag)})");
        if (!tagOk)
            foreach (bool le in new[] { true, false })
            foreach (bool bits in new[] { false, true })
            {
                byte[] t = Ds2RegulationCipher.ComputeTag(original.AsSpan(0, 16), cipher, new(le, bits));
                Console.WriteLine($"  variant words={(le ? "LE" : "BE")} lengths={(bits ? "bits" : "bytes")}: {Convert.ToHexString(t)}{(t.AsSpan().SequenceEqual(Ds2RegulationCipher.StoredTag(original)) ? "  MATCH" : "")}");
            }

        byte[] reencrypted = Ds2RegulationCipher.Encrypt(ours, original);
        bool roundTrip = reencrypted.AsSpan().SequenceEqual(original);
        Console.WriteLine($"encrypt round-trip reproduces original byte-for-byte: {roundTrip}");
        return same && roundTrip && tagOk ? 0 : 1;
    }
}
