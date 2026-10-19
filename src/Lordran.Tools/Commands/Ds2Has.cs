using Lordran.Tools.Formats;
using SoulsFormats.Cryptography;

namespace Lordran.Tools.Commands;

/// <summary>
/// ds2-has PATH... (session 8, cutscene track) — whether DS2's archives hold a file, by its name hash (paths the UXM
/// dictionary doesn't list, e.g. the exe's <c>map:/param/DemoObject_scn%06d.param</c> → <c>/param/demoobject_scn101010.param</c>).
/// A PATH containing {scn} is tried for every vanilla demo id (model/scn/*.scnbnd in the extract).
/// </summary>
public static class Ds2Has
{
    public static int Run(string[] args)
    {
        var hashes = new Dictionary<ulong, string>();
        foreach (var (name, key) in Ds2Archive.All)
            foreach (var f in Ds2Archive.Open(Program.DefaultDs2Root, name, key).Files)
                hashes.TryAdd(f.FileNameHash, $"{name} ({f.PaddedFileSize:N0} bytes)");
        var scns = Directory.GetFiles(Path.Combine(ExtractDs2.DefaultOut, "model", "scn"), "scn*.scnbnd").Select(p => Path.GetFileNameWithoutExtension(p)[3..]).ToList();
        foreach (var pattern in args.Skip(1))
            foreach (var path in pattern.Contains("{scn}") ? scns.Select(s => pattern.Replace("{scn}", s)) : [pattern])
                Console.WriteLine($"{path}: {(hashes.TryGetValue(HashHelper.FromPathHash(path.ToLowerInvariant()), out var where) ? where : "-")}");
        return 0;
    }
}
