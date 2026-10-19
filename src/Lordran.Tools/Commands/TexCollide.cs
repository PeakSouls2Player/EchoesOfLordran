using System.Security.Cryptography;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// tex-collide PACK_GAMEDATA DS2_GAMEDATA — texture name clashes. DS2 keeps loaded textures in a name-keyed cache, so a
/// pack texture that shares its name with another loaded texture (vanilla, or one of ours with other pixels) gets shared
/// and released by whichever owner unloads first: the session-6 bonfire-menu crash (render thread binds a texture whose
/// refcount is already 0) after traveling between maps. Lists every pack texture name that (a) vanilla DS2 also defines
/// in model/chr, obj, parts or map archives, or (b) two pack files define with different pixels.
/// </summary>
public static class TexCollide
{
    static readonly string[] Dirs = ["model/chr", "model/obj", "model/parts", "model/map"];

    public static int Run(string[] args)
    {
        string pack = args[1], vanilla = args[2];
        var ours = Collect(pack, withHash: true);
        var theirs = Collect(vanilla, withHash: false, only: ours.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));
        int clashes = 0;
        foreach (var (name, uses) in ours.OrderBy(k => k.Key))
        {
            var distinct = uses.Select(u => u.Hash).Distinct().Count();
            bool v = theirs.TryGetValue(name, out var vuses);
            if (!v && distinct < 2) continue;
            clashes++;
            Console.WriteLine($"{name}: " + (v ? $"vanilla in {string.Join(", ", vuses!.Select(u => u.File).Distinct().Take(4))}{(vuses!.Count > 4 ? $" (+{vuses.Count - 4})" : "")}; " : "")
                + $"pack {uses.Count}x/{distinct} pixel set(s) in {string.Join(", ", uses.Select(u => u.File).Distinct().Take(6))}");
        }
        Console.WriteLine($"{ours.Count} pack texture names, {clashes} clash(es)");
        return clashes == 0 ? 0 : 1;
    }

    record Use(string File, string Hash);

    static Dictionary<string, List<Use>> Collect(string root, bool withHash, HashSet<string>? only = null)
    {
        var all = new Dictionary<string, List<Use>>(StringComparer.OrdinalIgnoreCase);
        void Add(string file, TPF tpf)
        {
            foreach (var t in tpf.Textures)
            {
                if (only != null && !only.Contains(t.Name)) continue;
                string h = withHash ? Convert.ToHexString(SHA1.HashData(t.Bytes))[..12] : "";
                if (!all.TryGetValue(t.Name, out var l)) all[t.Name] = l = new();
                l.Add(new Use(file, h));
            }
        }
        void FromBytes(string file, byte[] b)
        {
            if (DCX.Is(b)) b = DCX.Decompress(b);
            if (TPF.Is(b)) { Add(file, TPF.Read(b)); return; }
            if (BND4.Is(b))
                foreach (var f in BND4.Read(b).Files.Where(f => f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase) || f.Name.EndsWith(".tpf.dcx", StringComparison.OrdinalIgnoreCase)))
                    FromBytes(file, f.Bytes);
        }
        foreach (var d in Dirs)
        {
            string dir = Path.Combine(root, d);
            if (!Directory.Exists(dir)) continue;
            foreach (var path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
                string ext = Path.GetFileName(path).ToLowerInvariant();
                try
                {
                    if (ext.EndsWith(".tpfbhd") || ext.EndsWith(".gibhd"))
                    {
                        string bdt = Path.ChangeExtension(path, ext.EndsWith(".tpfbhd") ? ".tpfbdt" : ".gibdt");
                        if (!File.Exists(bdt)) continue;
                        foreach (var f in BXF4.Read(path, bdt).Files.Where(f => f.Name.Contains(".tpf", StringComparison.OrdinalIgnoreCase)))
                            FromBytes(rel, f.Bytes);
                    }
                    else if (ext.EndsWith(".texbnd") || ext.EndsWith(".tpf") || ext.EndsWith(".tpf.dcx") || (ext.EndsWith(".bnd") && !d.EndsWith("map")))
                        FromBytes(rel, File.ReadAllBytes(path));
                }
                catch (Exception e) { Console.Error.WriteLine($"{rel}: {e.Message}"); }
            }
        }
        return all;
    }
}
