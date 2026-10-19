using Lordran.Tools.Textures;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// tex FILE [texture-name] [--png OUT] [--crop L,T,R,B] [--alpha]
/// Decodes a texture from a TPF (optionally inside a binder/DCX), prints per-channel statistics of the top mip,
/// and optionally saves it (or a cropped region) as PNG.
/// </summary>
public static class Tex
{
    public static int Run(string[] args)
    {
        string file = args[1];
        string? name = args.Length > 2 && !args[2].StartsWith("--") ? args[2] : null;
        string? png = Option(args, "--png");
        string? crop = Option(args, "--crop");

        var textures = FindTpfs(File.ReadAllBytes(file)).SelectMany(t => t.Textures).ToList();
        var tex = name == null ? textures[0] : textures.First(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var dds = Dds.Read(tex.Bytes);
        var image = dds.DecodeTop();
        Console.WriteLine($"{tex.Name}: {dds.Width}x{dds.Height} {dds.Format} mips={dds.Mips.Count}");
        Stats("R", image, p => p.R); Stats("G", image, p => p.G); Stats("B", image, p => p.B); Stats("A", image, p => p.A);

        if (crop != null)
        {
            var c = crop.Split(',').Select(int.Parse).ToArray();
            image = image.Crop(c[0], c[1], c[2] - c[0], c[3] - c[1]);
        }
        if (args.Contains("--alpha"))
        {
            var alpha = new Image(image.Width, image.Height);
            for (int i = 0; i < image.Pixels.Length; i++) { byte a = image.Pixels[i].A; alpha.Pixels[i] = new Rgba(a, a, a, 255); }
            image = alpha;
        }
        if (png != null)
        {
            Png.Save(image, png);
            Console.WriteLine($"saved {png} ({image.Width}x{image.Height})");
        }
        return 0;
    }

    static string? Option(string[] args, string key)
    {
        int i = Array.IndexOf(args, key);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    static void Stats(string channel, Image image, Func<Rgba, byte> get)
    {
        var values = image.Pixels.Select(get).ToArray();
        Console.WriteLine($"  {channel}: min={values.Min()} max={values.Max()} mean={values.Average(v => v):0.0}");
    }

    public static IEnumerable<TPF> FindTpfs(byte[] bytes)
    {
        if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
        if (TPF.Is(bytes)) { yield return TPF.Read(bytes); yield break; }
        IBinder? bnd = BND3.Is(bytes) ? BND3.Read(bytes) : BND4.Is(bytes) ? BND4.Read(bytes) : null;
        if (bnd == null) yield break;
        foreach (var f in bnd.Files)
            foreach (var t in FindTpfs(f.Bytes))
                yield return t;
    }
}
