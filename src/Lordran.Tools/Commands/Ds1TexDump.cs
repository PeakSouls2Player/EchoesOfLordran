using Lordran.Tools.Textures;

namespace Lordran.Tools.Commands;

/// <summary>
/// ds1-tex AREA NAME OUT.png — a DS1 (DSR) map texture (from map\mAA\*.tpfbhd/bdt, e.g. AREA m18) decoded with our
/// BCn decoder and saved as PNG, with its DDS format and channel means (to check conversions, e.g. tinted walls).
/// </summary>
public static class Ds1TexDump
{
    public static int Run(string[] args)
    {
        var textures = new Build.Maps.Ds1MapTextures(Program.DefaultDsrRoot, args[1]);
        byte[] dds = textures.Dds(args[2]) ?? throw new FileNotFoundException(args[2]);
        var d = Dds.Read(dds);
        var img = d.DecodeTop();
        double r = img.Pixels.Average(p => p.R), g = img.Pixels.Average(p => p.G), b = img.Pixels.Average(p => p.B), a = img.Pixels.Average(p => p.A);
        Console.WriteLine($"{args[2]}: {d.Width}x{d.Height} {d.Format} (fourcc '{System.Text.Encoding.ASCII.GetString(dds, 84, 4)}') mean R {r:0} G {g:0} B {b:0} A {a:0}");
        Png.Save(img, args[3]);
        return 0;
    }
}
