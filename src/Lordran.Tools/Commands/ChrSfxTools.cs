using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Serialization;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// <c>chr-sfx</c> (session 8, track chrsound fix pass): evidence for rule S7 (DS1 character effects → DS2 effects). DS1
/// effects are FXR1 (DSR sfx\FRPG_SfxBnd_*.ffxbnd.dcx, SoulsFormats <see cref="FXR1"/>), DS2's are DLsE (the game's global
/// title:/sfx/sfx9999[_Append].ffxbnd.dcx, SoulsFormats <see cref="FFXDLSE"/>; textures in sfx9999res.ffxbnd).
/// <list type="bullet">
/// <item><c>chr-sfx ds1 ID... [--xml DIR]</c>: a DS1 effect's action types and texture ids (+ an XML dump);</item>
/// <item><c>chr-sfx ds2 ID... [--xml DIR]</c>: a DS2 effect's actions, texture ids, colours (+ an XML dump);</item>
/// <item><c>chr-sfx tex-match DS1EFFECT [--png DIR]</c>: the DS1 effect's textures, the DS2 sfx textures with the same
/// pixels (DDS payload hash), and every DS2 effect that uses one of them (with its actions, colours and size
/// parameters), i.e. the DS2 effects drawn with DS1's own sprite;</item>
/// <item><c>chr-sfx ds2-all</c>: one line per DS2 effect (texture ids, action ids, first colour), for searching.</item>
/// </list>
/// </summary>
public static class ChrSfxTools
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: chr-sfx ds1|ds2 ID... [--xml DIR] | tex-match DS1EFFECT [--png DIR] | ds2-all"); return 1; }
        string? xml = Opt(args, "--xml"), png = Opt(args, "--png");
        var ids = args.Skip(2).TakeWhile(a => !a.StartsWith("--")).Where(a => int.TryParse(a, Inv, out _)).Select(a => int.Parse(a, Inv)).ToList();
        switch (args[1])
        {
            case "ds1":
                foreach (int id in ids) Ds1(id, xml);
                return 0;
            case "ds2":
                var ds2 = Ds2Effects();
                foreach (int id in ids) Ds2(ds2, id, xml);
                return 0;
            case "ds2-all":
                foreach (var (id, bytes) in Ds2Effects().OrderBy(kv => kv.Key))
                {
                    try { Console.WriteLine($"{id}: {Summary(FFXDLSE.Read(bytes))}"); }
                    catch (Exception e) { Console.WriteLine($"{id}: unreadable ({e.GetType().Name})"); }
                }
                return 0;
            case "tex-match":
                return TexMatch(ids[0], png);
            case "ds2-xml-all":
                // every DS2 effect as XML (validate\sfx_ds2_sprites.py lists their sprites: texture, size, colour)
                string xmlDir = args.Length > 2 ? args[2] : "ds2-sfx-xml";
                foreach (var (fid, fb) in Ds2Effects())
                    try { Ds2(new() { [fid] = fb }, fid, xmlDir); } catch (Exception ex) { Console.WriteLine($"DS2 f{fid:D7}: {ex.GetType().Name}"); }
                return 0;
            case "ds2-tex-dump":
                // every DS2 sfx texture as DDS (validate\sfx_tex_similar.py compares them with a DS1 sprite)
                string outDir = args.Length > 2 ? args[2] : "ds2-sfx-tex";
                Directory.CreateDirectory(outDir);
                foreach (var (tid, tb) in Ds2Textures())
                    try { File.WriteAllBytes(Path.Combine(outDir, $"s{tid:D6}.dds"), TPF.Read(tb).Textures[0].Headerize()); } catch { }
                return 0;
        }
        Console.Error.WriteLine($"chr-sfx: unknown mode {args[1]}");
        return 1;
    }

    static string? Opt(string[] a, string k) { int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }

    /// <summary>DS1 effect files by id over every DSR sfx bundle (CommonEffects first).</summary>
    static Dictionary<int, (string Bundle, byte[] Bytes)> Ds1Effects(out Dictionary<int, byte[]> textures)
    {
        var fx = new Dictionary<int, (string, byte[])>();
        textures = [];
        string dir = Path.Combine(Program.DefaultDsrRoot, "sfx");
        foreach (var f in Directory.GetFiles(dir, "*.ffxbnd.dcx").OrderBy(f => f.Contains("CommonEffects") ? 0 : 1).ThenBy(f => f))
        {
            var bnd = BND3.Read(f);
            foreach (var e in bnd.Files)
            {
                string n = Path.GetFileNameWithoutExtension(e.Name);
                if (e.Name.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase) && n.Length > 1 && int.TryParse(n[1..], Inv, out int id)) fx.TryAdd(id, (Path.GetFileName(f), e.Bytes));
                else if (e.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase) && n.Length > 1 && int.TryParse(n[1..], Inv, out int tid)) textures.TryAdd(tid, e.Bytes);
            }
        }
        return fx;
    }

    static Dictionary<int, byte[]> Ds2Effects()
    {
        var fx = new Dictionary<int, byte[]>();
        foreach (var name in new[] { "sfx9999.ffxbnd.dcx", "sfx9999_Append.ffxbnd.dcx" })
        {
            string p = Path.Combine(Program.DefaultDs2Root, "sfx", name);
            if (!File.Exists(p)) continue;
            foreach (var e in BND4.Read(p).Files)
            {
                string n = Path.GetFileNameWithoutExtension(e.Name);
                if (n.Length > 1 && int.TryParse(n[1..], Inv, out int id)) fx[id] = e.Bytes;
            }
        }
        return fx;
    }

    static Dictionary<int, byte[]> Ds2Textures()
    {
        var t = new Dictionary<int, byte[]>();
        foreach (var name in new[] { "sfx9999res.ffxbnd", "sfx9999res_Append.ffxbnd" })
        {
            string p = Path.Combine(Program.DefaultDs2Root, "sfx", name);
            if (!File.Exists(p)) continue;
            foreach (var e in BND4.Read(p).Files)
            {
                string n = Path.GetFileNameWithoutExtension(e.Name);
                if (e.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase) && n.Length > 1 && int.TryParse(n[1..], Inv, out int id)) t[id] = e.Bytes;
            }
        }
        return t;
    }

    /// <summary>DS1 FXR1: action data types and texture ids.</summary>
    static (List<string> Types, List<int> Textures) Ds1Features(FXR1 f)
    {
        var types = new List<string>(); var tex = new List<int>();
        foreach (var d in f.AllFXActionDatas)
        {
            types.Add($"{d.Type}:{d.GetType().Name}");
            foreach (var fi in d.GetType().GetFields().Where(x => x.Name.StartsWith("TextureID") && x.FieldType == typeof(int)))
                if (fi.GetValue(d) is int v && v > 0) tex.Add(v);
        }
        return (types, tex.Distinct().ToList());
    }

    static void Ds1(int id, string? xml)
    {
        var all = Ds1Effects(out _);
        if (!all.TryGetValue(id, out var e)) { Console.WriteLine($"DS1 f{id:D7}: not found"); return; }
        var f = FXR1.Read(e.Bytes);
        f.Flatten();
        var (types, tex) = Ds1Features(f);
        Console.WriteLine($"DS1 f{id:D7} ({e.Bundle}, {e.Bytes.Length} B): {f.AllFXNodes.Count} nodes, {f.AllFXActions.Count} actions, {f.AllStates.Count} states; action data {string.Join(", ", types.GroupBy(x => x).Select(g => $"{g.Key}×{g.Count()}"))}; textures {string.Join(", ", tex.Select(t => $"s{t:D5}"))}");
        if (xml != null)
        {
            Directory.CreateDirectory(xml);
            f.WriteToXml(Path.Combine(xml, $"ds1_f{id:D7}.xml"));
        }
    }

    /// <summary>DS2 DLsE: texture ids (ResourceSet vector 1 + Param40), action ids, colours, float scalars.</summary>
    static string Summary(FFXDLSE f)
    {
        var tex = new SortedSet<int>(f.Effect.ResourceSet?.Vector1 ?? []);
        var actions = new List<int>(); var colors = new List<string>(); var floats = new List<float>();
        void Walk(object? o, int depth)
        {
            if (o == null || depth > 40) return;
            switch (o)
            {
                case FFXDLSE.Action a: actions.Add(a.ID); break;
                case FFXDLSE.Param40 p40: tex.Add(p40.TextureID); break;
                case FFXDLSE.PrimitiveColor c: colors.Add($"({c.R:0.##},{c.G:0.##},{c.B:0.##},{c.A:0.##})"); break;
                case FFXDLSE.Param7 p7: floats.Add(p7.Float); break;
            }
            if (o is string || o.GetType().IsPrimitive) return;
            if (o is System.Collections.IEnumerable en) { foreach (var x in en) Walk(x, depth + 1); return; }
            foreach (var pr in o.GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0 && !p.PropertyType.IsPrimitive && p.PropertyType != typeof(string)))
                Walk(pr.GetValue(o), depth + 1);
        }
        Walk(f.Effect, 0);
        return $"textures [{string.Join(",", tex)}] actions [{string.Join(",", actions.GroupBy(x => x).Select(g => g.Count() > 1 ? $"{g.Key}×{g.Count()}" : $"{g.Key}"))}] colours {colors.Count} first {string.Join(" ", colors.Take(3))} floats {string.Join(",", floats.Take(8).Select(x => x.ToString("0.###", Inv)))}";
    }

    static void Ds2(Dictionary<int, byte[]> all, int id, string? xml)
    {
        if (!all.TryGetValue(id, out var b)) { Console.WriteLine($"DS2 f{id:D7}: not found"); return; }
        var f = FFXDLSE.Read(b);
        Console.WriteLine($"DS2 f{id:D7} ({b.Length} B): {Summary(f)}");
        if (xml != null)
        {
            Directory.CreateDirectory(xml);
            using var w = File.CreateText(Path.Combine(xml, $"ds2_f{id:D7}.xml"));
            f.XmlSerialize(w);
        }
    }

    /// <summary>Hash of the first texture's pixel payload in a TPF (format/size + bytes).</summary>
    static string? TexHash(byte[] tpf)
    {
        try
        {
            var t = TPF.Read(tpf);
            if (t.Textures.Count == 0) return null;
            var x = t.Textures[0];
            var dds = x.Headerize();
            // DDS header (128 B) + pixel data; hash the pixels with the dimensions
            return Convert.ToHexString(SHA1.HashData(dds.AsSpan(Math.Min(128, dds.Length))))[..16];
        }
        catch { return null; }
    }

    static int TexMatch(int ds1Effect, string? png)
    {
        var ds1 = Ds1Effects(out var ds1Tex);
        if (!ds1.TryGetValue(ds1Effect, out var e)) { Console.WriteLine($"DS1 f{ds1Effect:D7}: not found"); return 1; }
        var f = FXR1.Read(e.Bytes);
        f.Flatten();
        var (types, tex) = Ds1Features(f);
        Console.WriteLine($"DS1 f{ds1Effect:D7}: action data {string.Join(", ", types.Distinct())}; textures {string.Join(", ", tex.Select(t => $"s{t:D5}"))}");
        var ds2Tex = Ds2Textures();
        var ds2Hash = ds2Tex.Select(kv => (kv.Key, H: TexHash(kv.Value))).Where(x => x.H != null).GroupBy(x => x.H!).ToDictionary(g => g.Key, g => g.Select(x => x.Key).ToList());
        var matched = new HashSet<int>();
        foreach (int t in tex)
        {
            if (!ds1Tex.TryGetValue(t, out var tb)) { Console.WriteLine($"  s{t:D5}: not in DSR's sfx bundles"); continue; }
            string? h = TexHash(tb);
            var tx = TPF.Read(tb).Textures[0];
            var hits = h != null && ds2Hash.TryGetValue(h, out var l) ? l : [];
            Console.WriteLine($"  DS1 s{t:D5} ({tx.Name}, format {tx.Format}): DS2 sfx textures with the same pixels: {(hits.Count == 0 ? "none" : string.Join(", ", hits.Select(x => $"s{x:D6}")))}");
            foreach (int x in hits) matched.Add(x);
            if (png != null) { Directory.CreateDirectory(png); File.WriteAllBytes(Path.Combine(png, $"ds1_s{t:D5}.dds"), tx.Headerize()); }
        }
        if (matched.Count == 0) return 0;
        Console.WriteLine($"DS2 effects using {string.Join(", ", matched.Select(x => $"s{x:D6}"))}:");
        foreach (var (id, bytes) in Ds2Effects().OrderBy(kv => kv.Key))
        {
            FFXDLSE d;
            try { d = FFXDLSE.Read(bytes); } catch { continue; }
            string s = Summary(d);
            var used = (d.Effect.ResourceSet?.Vector1 ?? []).Intersect(matched).ToList();
            if (used.Count == 0 && !matched.Any(m => s.Contains($"[{m},") || s.Contains($",{m},") || s.Contains($",{m}]") || s.Contains($"[{m}]"))) continue;
            Console.WriteLine($"  f{id:D7}: {s}");
        }
        return 0;
    }
}
