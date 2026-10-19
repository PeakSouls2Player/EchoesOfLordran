using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>mtd-dump BINDER|FILE [name-substring...] — shader, texture slots (type, UV set, shader index) and params of MTDs.</summary>
public static class MtdDump
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: mtd-dump BINDER|FILE [name-substring...]"); return 1; }
        var filters = args.Skip(2).ToList();
        IEnumerable<(string Name, byte[] Bytes)> files = args[1].EndsWith(".mtd", StringComparison.OrdinalIgnoreCase)
            ? new[] { (Path.GetFileName(args[1]), File.ReadAllBytes(args[1])) }
            : Unbind.Open(args[1]).Files.Select(f => (Path.GetFileName(f.Name), DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes));
        foreach (var (name, bytes) in files)
        {
            if (filters.Count > 0 && !filters.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase))) continue;
            var mtd = MTD.Read(bytes);
            Console.WriteLine($"{name}: shader={mtd.ShaderPath} desc='{mtd.Description}'");
            foreach (var t in mtd.Textures)
                Console.WriteLine($"    tex {t.Type,-26} uv={t.UVNumber} idx={t.ShaderDataIndex} ext={t.Extended} {t.Path}");
            foreach (var p in mtd.Params)
                Console.WriteLine($"    {p.Name,-28} {p.Type,-8} {Fmt(p.Value)}");
        }
        return 0;
    }

    static string Fmt(object v) => v switch
    {
        float[] f => string.Join(",", f),
        int[] i => string.Join(",", i),
        _ => v?.ToString() ?? "null",
    };
}
