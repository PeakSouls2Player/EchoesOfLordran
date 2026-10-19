using System.Numerics;
using System.Security.Cryptography;

namespace Lordran.Tools.Formats;

/// <summary>
/// DS2 SOTFS enc_regulation.bnd.dcx: AES-128-CWC (Carter-Wegman + Counter; Brian Gladman's implementation, as used by
/// DarkSoulsII.exe FUN_140261150). Layout: 16-byte IV, 16-byte tag, then the ciphertext of a DCX'd BND4.
/// <list type="bullet">
/// <item>Counter block: 0x80, IV bytes 0..10 (the 11-byte CWC nonce), then a 32-bit big-endian counter starting at 1.</item>
/// <item>Tag: CWC-HASH over AAD = the full 16-byte IV and data = the ciphertext, encrypted with AES, XORed with the
/// keystream block for counter 0. The game recomputes it and silently skips decryption on a mismatch, so a rewritten
/// regulation must carry a fresh tag (the cause of the "only byte-identical files load" blocker).</item>
/// </list>
/// Key from Smithbox's Andre.SoulsFormats (Keys.DS2_REGULATION_KEY). `verify-regulation` checks decrypt against Smithbox
/// and that the tag we compute for the vanilla file equals the one shipped in its header.
/// </summary>
public static class Ds2RegulationCipher
{
    static readonly byte[] Key = Convert.FromHexString("40178130DF0A94543309E171ECBF254C");
    const int HeaderSize = 32;
    const int IvSize = 16;
    static readonly BigInteger P = (BigInteger.One << 127) - 1;

    /// <summary>Options for <see cref="ComputeTag"/>. <see cref="Ds2"/> matches the shipped tag; the others exist for probing.</summary>
    public record struct HashVariant(bool LittleEndianWords, bool LengthsInBits)
    {
        public static readonly HashVariant Ds2 = new(LittleEndianWords: true, LengthsInBits: false);
    }

    public static byte[] Decrypt(byte[] file) => Crypt(file.AsSpan(0, IvSize), file.AsSpan(HeaderSize)).ToArray();

    /// <summary>Writes the regulation BND4 with DS2's layout (file data aligned to 4 bytes, not 16).</summary>
    public static byte[] WriteBnd(SoulsFormats.BND4 bnd, SoulsFormats.DCX.CompressionInfo? compression = null)
    {
        int previous = SoulsFormats.BinderFileHeader.DataAlignment;
        SoulsFormats.BinderFileHeader.DataAlignment = 4;
        try { return compression == null ? bnd.Write() : bnd.Write(compression); }
        finally { SoulsFormats.BinderFileHeader.DataAlignment = previous; }
    }

    /// <summary>Encrypts <paramref name="plain"/> (DCX'd BND4) under the IV of an existing regulation file and writes a
    /// fresh tag for the new ciphertext.</summary>
    public static byte[] Encrypt(byte[] plain, ReadOnlySpan<byte> originalHeader)
    {
        var output = new byte[HeaderSize + plain.Length];
        var iv = originalHeader[..IvSize];
        iv.CopyTo(output);
        byte[] cipher = Crypt(iv, plain);
        cipher.CopyTo(output.AsSpan(HeaderSize));
        ComputeTag(iv, cipher).CopyTo(output.AsSpan(IvSize));
        return output;
    }

    /// <summary>The tag stored at header bytes 16..31 (what the game compares against).</summary>
    public static ReadOnlySpan<byte> StoredTag(byte[] file) => file.AsSpan(IvSize, 16);

    /// <summary>CWC-MAC: AES(CWC-HASH(AAD = iv, data = ciphertext)) XOR AES(counter block 0).</summary>
    public static byte[] ComputeTag(ReadOnlySpan<byte> iv, ReadOnlySpan<byte> cipher, HashVariant? hashVariant = null)
    {
        var variant = hashVariant ?? HashVariant.Ds2;
        using var aes = Aes.Create();
        aes.Key = Key;

        // Hash key Z = AES(0xC0 00..00) with the top bit cleared, read big-endian.
        var block = new byte[16];
        block[0] = 0xC0;
        aes.EncryptEcb(block, block, PaddingMode.None);
        block[0] &= 0x7F;
        BigInteger z = new(block, isUnsigned: true, isBigEndian: true);

        BigInteger acc = BigInteger.Zero;
        HashPadded(ref acc, z, iv[..IvSize], variant);
        HashPadded(ref acc, z, cipher, variant);
        BigInteger lengthUnit = variant.LengthsInBits ? 8 : 1;
        acc = (acc + ((IvSize * lengthUnit) << 64) + cipher.Length * lengthUnit) % P;

        byte[] r = new byte[16];
        byte[] accBytes = acc.ToByteArray(isUnsigned: true, isBigEndian: true);
        accBytes.CopyTo(r, 16 - accBytes.Length);
        aes.EncryptEcb(r, r, PaddingMode.None);

        var counter0 = new byte[16];
        counter0[0] = 0x80;
        iv[..11].CopyTo(counter0.AsSpan(1));
        aes.EncryptEcb(counter0, counter0, PaddingMode.None);
        for (int i = 0; i < 16; i++)
            r[i] ^= counter0[i];
        return r;
    }

    // Horner step per 96-bit block: acc = (acc + Y) * Z mod 2^127-1; the last partial block is zero-padded.
    // DS2's build loads each 32-bit word of a block natively (little-endian) instead of big-endian as the CWC spec says.
    static void HashPadded(ref BigInteger acc, BigInteger z, ReadOnlySpan<byte> data, HashVariant variant)
    {
        Span<byte> y = stackalloc byte[12];
        for (int offset = 0; offset < data.Length; offset += 12)
        {
            y.Clear();
            data.Slice(offset, Math.Min(12, data.Length - offset)).CopyTo(y);
            BigInteger value;
            if (variant.LittleEndianWords)
                value = ((BigInteger)BitConverter.ToUInt32(y[..4]) << 64) | ((BigInteger)BitConverter.ToUInt32(y.Slice(4, 4)) << 32) | BitConverter.ToUInt32(y.Slice(8, 4));
            else
                value = new BigInteger(y, isUnsigned: true, isBigEndian: true);
            acc = (acc + value) * z % P;
        }
    }

    static byte[] Crypt(ReadOnlySpan<byte> iv, ReadOnlySpan<byte> data)
    {
        var counter = new byte[16];
        counter[0] = 0x80;
        iv[..11].CopyTo(counter.AsSpan(1));
        counter[15] = 1;

        using var aes = Aes.Create();
        aes.Key = Key;
        var result = new byte[data.Length];
        var keystream = new byte[16];
        for (int offset = 0; offset < data.Length; offset += 16)
        {
            aes.EncryptEcb(counter, keystream, PaddingMode.None);
            int n = Math.Min(16, data.Length - offset);
            for (int i = 0; i < n; i++)
                result[offset + i] = (byte)(data[offset + i] ^ keystream[i]);
            for (int i = 15; i >= 12 && ++counter[i] == 0; i--) { } // 32-bit big-endian counter
        }
        return result;
    }
}
