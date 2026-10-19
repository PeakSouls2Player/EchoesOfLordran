using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using SoulsFormats;
using SoulsFormats.Cryptography;

namespace Lordran.Tools.Formats;

/// <summary>
/// Read-only access to DS2 SOTFS's archives (*.bhd/*.bdt). The .bhd headers are RSA-encrypted with
/// the public keys DS2 ships beside them (*KeyCode.pem); file data may be AES-encrypted per file,
/// which SoulsFormats handles. Nothing here writes to the game folder.
/// </summary>
public sealed class Ds2Archive
{
    public static readonly (string Archive, string Key)[] All =
    {
        ("GameDataEbl", "GameDataKeyCode"),
        ("LqChrEbl", "LqChrKeyCode"),
        ("LqMapEbl", "LqMapKeyCode"),
        ("LqObjEbl", "LqObjKeyCode"),
        ("LqPartsEbl", "LqPartsKeyCode"),
    };

    public string Name { get; }
    public string BdtPath { get; }
    public BHD5 Header { get; }

    Ds2Archive(string name, string bdtPath, BHD5 header)
    {
        Name = name;
        BdtPath = bdtPath;
        Header = header;
    }

    public static Ds2Archive Open(string gameDir, string archive, string key)
    {
        byte[] bhd = DecryptRsa(Path.Combine(gameDir, archive + ".bhd"), Path.Combine(gameDir, key + ".pem"));
        return new Ds2Archive(archive, Path.Combine(gameDir, archive + ".bdt"), BHD5.Read(bhd, BHD5.Game.DarkSouls2));
    }

    public IEnumerable<BHD5.FileHeader> Files => Header.Buckets.SelectMany(b => b);

    /// <summary>
    /// Raw RSA with the public exponent, block by block, left-padding each output block to the
    /// engine's output size (leading zero bytes are dropped by the big-integer conversion).
    /// </summary>
    static byte[] DecryptRsa(string path, string pemPath)
    {
        RsaKeyParameters key;
        using (var reader = File.OpenText(pemPath))
            key = (RsaKeyParameters)new PemReader(reader).ReadObject();

        var engine = new RsaEngine();
        engine.Init(false, key);
        int inSize = engine.GetInputBlockSize();
        int outSize = engine.GetOutputBlockSize();

        byte[] input = File.ReadAllBytes(path);
        using var output = new MemoryStream(input.Length);
        for (int offset = 0; offset + inSize <= input.Length; offset += inSize)
        {
            byte[] block = engine.ProcessBlock(input, offset, inSize);
            if (block.Length < outSize)
                output.Write(new byte[outSize - block.Length]);
            output.Write(block);
        }
        return output.ToArray();
    }
}

/// <summary>
/// Maps DS2 archive name hashes back to paths using UXM's published file list.
/// </summary>
public sealed class Ds2FileNames
{
    readonly Dictionary<ulong, string> byHash = new();

    public Ds2FileNames(string dictionaryPath)
    {
        foreach (string line in File.ReadLines(dictionaryPath))
        {
            string path = line.Trim();
            if (path.StartsWith('/'))
                byHash[HashHelper.FromPathHash(path)] = path;
        }
    }

    public int Count => byHash.Count;

    public string? Lookup(ulong hash) => byHash.TryGetValue(hash, out string? path) ? path : null;
}
