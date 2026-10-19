using SoulsFormats;
using SoulsFormats.Cryptography;

namespace Lordran.Tools.Commands;

/// <summary>
/// save-unpack SL2 OUTDIR — decrypts every entry of a DS2 SOTFS save (BND4 of AES-128-CBC USERDATA files, key from
/// SoulsFormats' SL2Decryptor) into OUTDIR as NAME.bin. Read-only: the save itself is never written.
/// </summary>
public static class SaveUnpack
{
    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: save-unpack SL2 OUTDIR"); return 1; }
        var bnd = BND4.Read(args[1]);
        Directory.CreateDirectory(args[2]);
        byte[] key = SL2Decryptor.GetScholarSaveKey();
        foreach (var f in bnd.Files)
        {
            byte[] plain = SL2Decryptor.DecryptSL2File(f.Bytes, key);
            File.WriteAllBytes(Path.Combine(args[2], f.Name + ".bin"), plain);
            Console.WriteLine($"{f.Name,-16} {f.Bytes.Length,10:N0} → {plain.Length,10:N0} bytes");
        }
        return 0;
    }
}
