using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>bnd-ls FILE [INNER...] — a BND4/BND3's header and every entry (id, flags, full name, size, DCX or not);
/// with INNER names (file name endings, e.g. ".anibnd"), lists that nested binder instead.</summary>
public static class BndLs
{
    public static int Run(string[] args)
    {
        byte[] bytes = File.ReadAllBytes(args[1]);
        foreach (string inner in args.Skip(2))
        {
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            var files = BND4.Is(bytes) ? BND4.Read(bytes).Files : BND3.Read(bytes).Files;
            bytes = files.First(f => f.Name.EndsWith(inner, StringComparison.OrdinalIgnoreCase)).Bytes;
        }
        if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
        if (BND4.Is(bytes))
        {
            var b = BND4.Read(bytes);
            Console.WriteLine($"BND4 version={b.Version} format={b.Format} unicode={b.Unicode} extended={b.Extended} unk04={b.Unk04} unk05={b.Unk05} files={b.Files.Count}");
            foreach (var f in b.Files)
                Console.WriteLine($"  id={f.ID,-4} flags={f.Flags} {(DCX.Is(f.Bytes) ? "DCX " : "    ")}{f.Bytes.Length,10:N0}  {f.Name}");
        }
        else
        {
            var b = BND3.Read(bytes);
            Console.WriteLine($"BND3 version={b.Version} format={b.Format} files={b.Files.Count}");
            foreach (var f in b.Files)
                Console.WriteLine($"  id={f.ID,-4} flags={f.Flags} {(DCX.Is(f.Bytes) ? "DCX " : "    ")}{f.Bytes.Length,10:N0}  {f.Name}");
        }
        return 0;
    }
}
