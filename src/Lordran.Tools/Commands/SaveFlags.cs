using SoulsFormats;
using SoulsFormats.Cryptography;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (fix11): a character's persistent global event flags from a DS2 SOTFS save, read-only.
/// <c>save-flags SL2 SLOT [FLAG...]</c> — SLOT as ret11 numbered it (world data = USER_DATA0(10 + SLOT); MP = 2). The flag
/// section at +0x26D34 holds DS2's global block 10 (flags 100000..109999) at +0x0 and block 20 (200000..209999) at +0x4e4
/// (Ds2FlagStore; ret11 read the user's 200550 there): flag F at byte (F % 10000) >> 3, bit 7 − (F &amp; 7).
/// </summary>
public static class SaveFlags
{
    public static Dictionary<int, int> Read(string sl2, int slot)
    {
        var bnd = BND4.Read(sl2);
        byte[] key = SL2Decryptor.GetScholarSaveKey();
        var f = bnd.Files.First(x => x.Name.Equals($"USER_DATA{10 + slot:D3}", StringComparison.OrdinalIgnoreCase));
        byte[] d = SL2Decryptor.DecryptSL2File(f.Bytes, key);
        const int Section = 0x26D34;
        var on = new Dictionary<int, int>();
        foreach (var (block, off) in new[] { (10, 0x0), (20, 0x4e4) })
            for (int bit = 0; bit < 10000; bit++)
            {
                int at = Section + off + (bit >> 3);
                if (at < d.Length && (d[at] >> (7 - (bit & 7)) & 1) != 0) on[block * 10000 + bit] = 1;
            }
        return on;
    }

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: save-flags SL2 SLOT [FLAG...]"); return 1; }
        var on = Read(args[1], int.Parse(args[2]));
        var only = args.Skip(3).Select(int.Parse).ToList();
        if (only.Count > 0) foreach (int fl in only) Console.WriteLine($"{fl} = {on.GetValueOrDefault(fl)}");
        else Console.WriteLine(string.Join(" ", on.Keys.Order()));
        Console.WriteLine($"save-flags: {on.Count} flags on");
        return 0;
    }
}
