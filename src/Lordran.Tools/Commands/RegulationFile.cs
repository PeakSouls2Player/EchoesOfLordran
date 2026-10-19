using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>regulation-file NAME OUT — writes one file from DS2's vanilla regulation (e.g. SpEffectWeapon.param) as is.</summary>
public static class RegulationFile
{
    public static int Run(string[] args)
    {
        var bnd = BND4.Read(Ds2RegulationCipher.Decrypt(File.ReadAllBytes(Path.Combine(Program.DefaultDs2Root, "enc_regulation.bnd.dcx"))));
        var file = bnd.Files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f.Name).Equals(args[1], StringComparison.OrdinalIgnoreCase));
        if (file == null) { Console.Error.WriteLine($"{args[1]} not in the regulation"); return 1; }
        File.WriteAllBytes(args[2], file.Bytes);
        Console.WriteLine($"{file.Name}: {file.Bytes.Length:N0} bytes → {args[2]}");
        return 0;
    }
}
