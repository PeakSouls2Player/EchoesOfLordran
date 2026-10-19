using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (burg11, golden gate): <c>param-diff PACK_A PACK_B [--rows]</c> — every regulation param row that differs
/// between two built packs (added / removed / changed, by the row's cell values), grouped per param; --rows lists the ids.
/// The per-map params are files (compare them with md5); this covers enc_regulation only.
/// </summary>
public static class ParamDiff
{
    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: param-diff PACK_A PACK_B [--rows] [--fields]"); return 1; }
        bool rows = args.Contains("--rows");
        bool fields = args.Contains("--fields");   // session 11 (bal11): per param, which fields of the changed rows differ (and how many rows each)
        var a = ParamStore.LoadDs2(Path.Combine(args[1], "title"), ExtractDs2.DefaultOut);
        var b = ParamStore.LoadDs2(Path.Combine(args[2], "title"), ExtractDs2.DefaultOut);
        static string Cells(PARAM.Row r) => string.Join("|", r.Cells.Select(c => c.Value is byte[] x ? Convert.ToHexString(x) : Convert.ToString(c.Value, System.Globalization.CultureInfo.InvariantCulture)));
        int total = 0;
        foreach (var name in a.Params.Keys.Union(b.Params.Keys).Order())
        {
            var pa = a.Params.GetValueOrDefault(name); var pb = b.Params.GetValueOrDefault(name);
            var ra = pa?.Rows.GroupBy(r => r.ID).ToDictionary(g => g.Key, g => Cells(g.First())) ?? [];
            var rb = pb?.Rows.GroupBy(r => r.ID).ToDictionary(g => g.Key, g => Cells(g.First())) ?? [];
            var added = rb.Keys.Except(ra.Keys).Order().ToList();
            var removed = ra.Keys.Except(rb.Keys).Order().ToList();
            var changed = ra.Keys.Intersect(rb.Keys).Where(k => ra[k] != rb[k]).Order().ToList();
            if (added.Count + removed.Count + changed.Count == 0) continue;
            total += added.Count + removed.Count + changed.Count;
            Console.WriteLine($"{name}: +{added.Count} -{removed.Count} ~{changed.Count}");
            if (fields && changed.Count > 0)
            {
                var ia = pa!.Rows.GroupBy(r => r.ID).ToDictionary(g => g.Key, g => g.First());
                var ib = pb!.Rows.GroupBy(r => r.ID).ToDictionary(g => g.Key, g => g.First());
                var count = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var k in changed)
                    foreach (var c in ia[k].Cells)
                    {
                        var cb = ib[k][c.Def.InternalName];
                        string va = c.Value is byte[] x ? Convert.ToHexString(x) : Convert.ToString(c.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                        string vb = cb?.Value is byte[] y ? Convert.ToHexString(y) : Convert.ToString(cb?.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                        if (va != vb) count[c.Def.InternalName] = count.GetValueOrDefault(c.Def.InternalName) + 1;
                    }
                Console.WriteLine($"  fields: {string.Join(", ", count.Select(kv => $"{kv.Key} ({kv.Value})"))}");
            }
            if (rows)
            {
                if (added.Count > 0) Console.WriteLine($"  + {string.Join(" ", added.Take(60))}{(added.Count > 60 ? " …" : "")}");
                if (removed.Count > 0) Console.WriteLine($"  - {string.Join(" ", removed.Take(60))}{(removed.Count > 60 ? " …" : "")}");
                if (changed.Count > 0) Console.WriteLine($"  ~ {string.Join(" ", changed.Take(60))}{(changed.Count > 60 ? " …" : "")}");
            }
        }
        Console.WriteLine($"param-diff: {total} rows differ");
        return 0;
    }
}
