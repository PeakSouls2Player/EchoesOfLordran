using Lordran.Tools.Textures;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// tex-stats TPFBHD SUFFIX [MAX] — channel means of the textures in a DS2 map texture archive (BXF4 tpfbhd/bdt of
/// NAME.tpf.dcx) whose names end with SUFFIX (e.g. _s for specular), to compare DS2's conventions with converted DS1 ones.
/// </summary>
public static class TexStats
{
    public static int Run(string[] args)
    {
        string bhd = args[1], suffix = args[2];
        int max = args.Length > 3 ? int.Parse(args[3]) : 12;
        // A map texture archive (tpfbhd/bdt), a BND of TPFs (character .texbnd) or one .tpf.
        IEnumerable<byte[]> files = bhd.EndsWith(".tpfbhd", StringComparison.OrdinalIgnoreCase)
            ? BXF4.Read(bhd, Path.ChangeExtension(bhd, ".tpfbdt")).Files.Select(f => f.Bytes)
            : bhd.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase) ? [File.ReadAllBytes(bhd)]
            : BND4.Read(bhd).Files.Select(f => f.Bytes);
        int n = 0;
        foreach (var bytes in files)
        {
            var tpf = TPF.Read(DCX.Is(bytes) ? DCX.Decompress(bytes) : bytes);
            foreach (var t in tpf.Textures.Where(t => t.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var d = Dds.Read(t.Headerize());
                    var img = d.DecodeTop();
                    Console.WriteLine($"{t.Name,-36} fmt {t.Format,-3} {d.Width}x{d.Height} mean R {img.Pixels.Average(p => p.R),5:0} G {img.Pixels.Average(p => p.G),5:0} B {img.Pixels.Average(p => p.B),5:0} A {img.Pixels.Average(p => p.A),5:0}");
                }
                catch (Exception e) { Console.WriteLine($"{t.Name}: {e.Message}"); }
                if (++n >= max) return 0;
            }
        }
        return 0;
    }
}
