using Lordran.Tools.Build.Maps;
using Lordran.Tools.Textures;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// <c>travel-check PACK</c> — session 11 (travel12): the Lordran page of the bonfire Travel menu, offline.
/// <list type="number">
/// <item><b>TC1 bonfires</b>: every DS1 bonfire the built DS1 maps register (2009:3 on an o0200 part; the maps = MapHosts rows
/// whose host is in the pack's WorldMapList, H2 secondaries only when lordran-h2\merge.tsv merges them) has its own
/// MapObjectBonfireParam row on its host map, id in 40000..49999 (the loader's Lordran page rule).</item>
/// <item><b>TC2 vanilla rows</b>: every vanilla MapObjectBonfireParam row is byte-identical in the pack's regulation.</item>
/// <item><b>TC3 pictures</b>: each DS1 bonfire has <c>ic_list_&lt;id:D9&gt;</c> and each of their menuAreaIDs
/// <c>ic_area_&lt;id:D9&gt;</c> in the pack; one texture, named like the file (upper case; DS2 caches textures by name, so
/// names are unique by construction); the picture inside the frame is not blank (mean ≥ 12, spread ≥ 10), differs from
/// every vanilla Travel picture (no template copy left) and from every other DS1 picture of the same kind.</item>
/// </list>
/// </summary>
public static class TravelCheck
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: travel-check PACK"); return 1; }
        string pack = args[1], gd = Path.Combine(pack, "gamedata");
        int fails = 0, passes = 0;
        void Check(bool ok, string what) { if (ok) passes++; else fails++; Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} {what}"); }

        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        PARAM Bonfires(string regPath)
        {
            var reg = BND4.Read(Formats.Ds2Regulation.Decrypt(regPath));
            var p = PARAM.Read(reg.Files.First(f => Path.GetFileNameWithoutExtension(f.Name) == "MapObjectBonfireParam").Bytes);
            p.ApplyParamdefCarefully(defs);
            return p;
        }
        var ours = Bonfires(Path.Combine(pack, "title", "enc_regulation.bnd.dcx"));
        var vanilla = Bonfires(Path.Combine(Program.DefaultDs2Root, "enc_regulation.bnd.dcx"));

        // ---- TC1
        Console.WriteLine("TC1 every registered DS1 bonfire of the built maps has a row");
        var listed = File.ReadAllLines(Path.Combine(gd, "map", "worldmaplist", "worldmaplist.list"))
            .Select(l => l.Split('\t', ' ')[0].Trim()).Where(l => l.StartsWith('m')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string mergeTsv = Path.Combine(pack, "lordran-h2", "merge.tsv");
        var merged = File.Exists(mergeTsv) ? File.ReadAllLines(mergeTsv).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split('\t')[1].Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        var hostFirst = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // the first row of a host = its primary
        var vanillaIds = vanilla.Rows.Select(r => r.ID).ToHashSet();
        var ds1Rows = ours.Rows.Where(r => !vanillaIds.Contains(r.ID)).ToList();
        foreach (var row in MapHosts.Rows)
        {
            bool primary = hostFirst.Add(row.Host);
            if (!listed.Contains(row.Host) || (!primary && !merged.Contains(row.Ds1Map))) continue;
            var msb = H2Merge.Original(() => MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", row.Ds1Map + ".msb"))); // the shipped DS1 map (an active H2 layer would serve the merged one)
            var regs = H2Merge.Original(() => MapBuilder.RegisteredBonfireEntities(Program.DefaultDsrRoot, row.Ds1Map));
            var ds1 = msb.Parts.Objects.Where(p => p.ModelName == "o0200" && regs.Contains(p.EntityID)).Select(p => p.EntityID).Order().ToList();
            int hostId = int.Parse(row.Host[1..3] + row.Host[4..6] + row.Host[7..9] + row.Host[10..12]);
            Console.WriteLine($"  {row.Ds1Map} → {row.Host}: DS1 bonfires {string.Join(", ", ds1)}");
            _ = hostId;
        }
        foreach (var host in listed.Where(h => MapHosts.Rows.Any(r => r.Host.Equals(h, StringComparison.OrdinalIgnoreCase))))
        {
            int hostId = int.Parse(host[1..3] + host[4..6] + host[7..9] + host[10..12]);
            int expected = 0;
            bool first = true;
            foreach (var row in MapHosts.Rows.Where(r => r.Host.Equals(host, StringComparison.OrdinalIgnoreCase)))
            {
                if (!first && !merged.Contains(row.Ds1Map)) continue;
                first = false;
                var msb = H2Merge.Original(() => MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", row.Ds1Map + ".msb"))); // the shipped DS1 map (an active H2 layer would serve the merged one)
                var regs = H2Merge.Original(() => MapBuilder.RegisteredBonfireEntities(Program.DefaultDsrRoot, row.Ds1Map));
                expected += msb.Parts.Objects.Count(p => p.ModelName == "o0200" && regs.Contains(p.EntityID));
            }
            var rows = ds1Rows.Where(r => Convert.ToInt32(r["mapId"].Value) == hostId).ToList();
            Check(rows.Count == expected, $"{host}: {rows.Count} bonfire rows for {expected} registered DS1 bonfires [{string.Join(", ", rows.Select(r => $"{r.ID} area {r["menuAreaID"].Value}"))}]");
        }
        var outside = ds1Rows.Where(r => r.ID is < TravelPages.Ds1BonfireMin or > TravelPages.Ds1BonfireMax).Select(r => r.ID).ToList();
        Check(outside.Count == 0, $"added bonfire rows all in {TravelPages.Ds1BonfireMin}..{TravelPages.Ds1BonfireMax} (the Lordran page){(outside.Count > 0 ? ": outside " + string.Join(", ", outside) : "")}");

        // ---- TC2
        Console.WriteLine("TC2 vanilla bonfire rows unchanged");
        int changed = 0;
        foreach (var v in vanilla.Rows)
        {
            var o = ours[v.ID];
            if (o == null || v.Cells.Zip(o.Cells).Any(c => !Equals(c.First.Value, c.Second.Value) && !(c.First.Value is byte[] a && c.Second.Value is byte[] b && a.SequenceEqual(b))))
            { changed++; Console.WriteLine($"    vanilla row {v.ID} {(o == null ? "missing" : "changed")}"); }
        }
        Check(changed == 0, $"{vanilla.Rows.Count} vanilla MapObjectBonfireParam rows identical");

        // ---- TC3
        Console.WriteLine("TC3 Travel pictures");
        string icons = Path.Combine(ExtractDs2.DefaultOut, "menu", "tex", "icon");
        var vanillaPics = new HashSet<string>();
        foreach (string folder in new[] { "bonfire_area", "bonfire_list" })
            foreach (string f in Directory.GetFiles(Path.Combine(icons, folder), "*.tpf"))
                vanillaPics.Add(Picture(TPF.Read(f).Textures[0]).Hash);
        void Pictures(string folder, string prefix, IEnumerable<int> ids)
        {
            var seen = new Dictionary<string, int>();
            foreach (int id in ids)
            {
                string name = $"{prefix}{id:D9}", file = Path.Combine(gd, "menu", "tex", "icon", folder, name + ".tpf");
                if (!File.Exists(file)) { Check(false, $"{folder}/{name}.tpf missing"); continue; }
                var tpf = TPF.Read(file);
                var (hash, mean, spread) = Picture(tpf.Textures[0]);
                bool dup = seen.TryGetValue(hash, out int other);
                seen.TryAdd(hash, id);
                Check(tpf.Textures.Count == 1 && tpf.Textures[0].Name == name.ToUpperInvariant() && mean >= 12 && spread >= 10 && !vanillaPics.Contains(hash) && !dup,
                    $"{folder}/{name}: texture {tpf.Textures[0].Name}, picture mean {mean:0.0} spread {spread:0.0}{(vanillaPics.Contains(hash) ? ", a VANILLA picture (template copy)" : "")}{(dup ? $", SAME picture as {other}" : "")}");
            }
        }
        Pictures("bonfire_list", "ic_list_", ds1Rows.Select(r => r.ID).Order());
        Pictures("bonfire_area", "ic_area_", ds1Rows.Select(r => Convert.ToInt32(r["menuAreaID"].Value)).Distinct().Order());

        Console.WriteLine(fails == 0 ? $"travel-check: ALL {passes} CHECKS PASSED" : $"travel-check: {fails} CHECK(S) FAILED ({passes} passed)");
        return fails == 0 ? 0 : 2;
    }

    /// <summary>Hash, mean and spread (standard deviation) of the RGB inside the frame's opaque part (alpha > 128).</summary>
    static (string Hash, double Mean, double Spread) Picture(TPF.Texture t)
    {
        var img = Dds.Read(t.Bytes).DecodeTop();
        var vals = new List<double>();
        using var sha = System.Security.Cryptography.SHA1.Create();
        var bytes = new List<byte>();
        foreach (var p in img.Pixels)
        {
            if (p.A <= 128) continue;
            vals.Add((p.R + p.G + p.B) / 3.0);
            bytes.Add(p.R); bytes.Add(p.G); bytes.Add(p.B);
        }
        if (vals.Count == 0) return ("empty", 0, 0);
        double mean = vals.Average(), sd = Math.Sqrt(vals.Average(v => (v - mean) * (v - mean)));
        return (Convert.ToHexString(sha.ComputeHash(bytes.ToArray())), mean, sd);
    }
}
