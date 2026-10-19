using System.Text;

namespace Lordran.Tools.Formats;

/// <summary>
/// Identifies which Havok container and SDK version an HKX file was written with.
/// SoulsFormats doesn't parse HKX, so this only reads the header.
/// </summary>
public static class HkxInfo
{
    public static string Describe(byte[] bytes)
    {
        if (bytes.Length < 0x40)
            return "too small for HKX";

        // Packfile: 0x57E0E057 0x10C0C010, layout rules at 0x10, version string at 0x28.
        if (BitConverter.ToUInt32(bytes, 0) == 0x57E0E057 && BitConverter.ToUInt32(bytes, 4) == 0x10C0C010)
        {
            int fileVersion = BitConverter.ToInt32(bytes, 0x0C);
            byte pointerSize = bytes[0x10];
            string contents = ReadCString(bytes, 0x28, 16);
            return $"packfile v{fileVersion} ptr{pointerSize * 8} '{contents}'";
        }

        // Tagfile (2014+): "TAG0" chunk near the start.
        string head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 0x40));
        if (head.Contains("TAG0"))
        {
            int sdk = IndexOf(bytes, "SDKV"u8);
            string ver = sdk >= 0 ? ReadCString(bytes, sdk + 4, 8) : "?";
            return $"tagfile SDK '{ver}'";
        }

        int hk = IndexOf(bytes, "hk_20"u8, 0x400);
        return hk >= 0 ? $"unknown container, mentions '{ReadCString(bytes, hk, 20)}'" : "unknown container";
    }

    static string ReadCString(byte[] bytes, int offset, int max)
    {
        int end = offset;
        while (end < bytes.Length && end < offset + max && bytes[end] != 0)
            end++;
        return Encoding.ASCII.GetString(bytes, offset, end - offset);
    }

    static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle, int limit = int.MaxValue)
    {
        int idx = haystack.AsSpan(0, Math.Min(haystack.Length, limit)).IndexOf(needle);
        return idx;
    }
}
