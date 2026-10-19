using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>regulation-ls [FILTER] — lists the files in DS2's vanilla regulation (id, size, name), optionally only names containing FILTER.</summary>
public static class RegulationLs
{
    public static int Run(string[] args)
    {
        var bnd = BND4.Read(Ds2RegulationCipher.Decrypt(File.ReadAllBytes(Path.Combine(Program.DefaultDs2Root, "enc_regulation.bnd.dcx"))));
        string? filter = args.Length > 1 ? args[1] : null;
        int n = 0;
        foreach (var f in bnd.Files.Where(f => filter == null || f.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"  id={f.ID,-5} {f.Bytes.Length,10:N0}  {f.Name}");
            n++;
        }
        Console.WriteLine($"{n} of {bnd.Files.Count} files");
        return 0;
    }
}
