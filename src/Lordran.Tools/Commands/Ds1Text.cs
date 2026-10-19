using Lordran.Tools.Formats;

namespace Lordran.Tools.Commands;

/// <summary>
/// ds1-text [--fmg SEL]... [--all] ID|LO-HI... — DS1 (DSR, English) texts by id from item.msgbnd and menu.msgbnd, with
/// the FMG each hit came from. ds1-text --list shows the FMGs and the categories.
/// <para>
/// Default (no --fmg): the old behaviour, the first FMG that has the id in Ds1Source.AnyText's order (item names first,
/// then menu texts), now labelled with its FMG, plus a note when other FMGs hold a different text for the same id.
/// --fmg narrows the search to a category (talk, blood/message, event, npc/boss, place, goods, weapon, armor, ring, magic,
/// dialogue, system, keyguide, help, menu, subtitle, names, desc, long), an FMG name (substring) or a BND id
/// ("107", "menu:30"); it can be repeated. --all prints every hit instead of the first.
/// </para>
/// Session 8 (census tool gap): ids collide across FMGs, e.g. tutorial blood message 9000 vs other tables, boss names in
/// NPC_name_ vs event texts; the old tool returned whichever FMG came first without saying which.
/// </summary>
public static class Ds1Text
{
    const string Usage =
        "usage: ds1-text [--fmg CATEGORY|FMGNAME|BNDID]... [--all] ID|LO-HI...\n" +
        "       ds1-text --list\n" +
        "  default: first hit in item.msgbnd then menu.msgbnd order (old behaviour), labelled with its FMG\n" +
        "  --fmg  talk, blood (message), event, npc (boss), place, goods, weapon, armor, ring, magic, dialogue, system,\n" +
        "         keyguide, help, menu, subtitle, names, desc, long; or an FMG name (Blood_writing) or BND id (107, menu:30)\n" +
        "  --all  every FMG that has the id\n" +
        "  e.g. ds1-text --fmg blood 9000 9001   ds1-text --fmg event 10010160   ds1-text --all 2232";

    public static int Run(string[] args)
    {
        var selectors = new List<string>();
        var ids = new List<(int Lo, int Hi)>();
        bool all = false, list = false;
        for (int i = 1; i < args.Length; i++)
        {
            string a = args[i];
            if (a == "--fmg" || a == "--cat")
            {
                if (i + 1 >= args.Length) return Fail($"{a} needs a value");
                selectors.Add(args[++i]);
            }
            else if (a == "--all") all = true;
            else if (a == "--list") list = true;
            else if (a is "-h" or "--help") { Console.WriteLine(Usage); return 0; }
            else if (a.StartsWith("--")) return Fail($"unknown option {a}");
            else if (TryRange(a, out var r)) ids.Add(r);
            else return Fail($"not an id or LO-HI range: '{a}' (put the FMG after --fmg)");
        }
        if (!list && ids.Count == 0) return Fail("no ids given");

        var fmgs = Ds1Fmgs.Load(Program.DefaultDsrRoot);
        if (list)
        {
            foreach (var t in fmgs.Tables)
                Console.WriteLine($"{t.Bnd,-5} {string.Join("/", t.Ids),-8} {t.Name,-30} {t.ById.Count(e => !string.IsNullOrEmpty(e.Value)),6} texts");
            Console.WriteLine("categories: " + string.Join(", ", Ds1Fmgs.Categories.Select(c => $"{c.Key}={string.Join("+", c.Value)}")));
            if (ids.Count == 0) return 0;
        }

        List<Ds1Fmgs.Table> tables;
        if (selectors.Count == 0) tables = fmgs.Tables;
        else
        {
            tables = [];
            foreach (string s in selectors)
            {
                var sel = fmgs.Select(s);
                if (sel.Count == 0) return Fail($"no FMG matches '{s}' (ds1-text --list shows them)");
                tables.AddRange(sel.Where(t => !tables.Contains(t)));
            }
        }

        foreach (var (lo, hi) in ids)
        {
            bool range = lo != hi;
            for (long id = lo; id <= hi; id++)
            {
                var hits = fmgs.Hits((int)id, tables).ToList();
                if (hits.Count == 0)
                {
                    if (!range) Console.WriteLine($"{id}: (none{(selectors.Count > 0 ? " in " + string.Join(", ", selectors) : "")})");
                    continue;
                }
                if (all)
                {
                    foreach (var (t, text) in hits) Console.WriteLine($"{id} [{t.Label}]: {Ds1Fmgs.OneLine(text)}");
                    continue;
                }
                var (first, firstText) = hits[0];
                var others = hits.Skip(1).Where(h => h.Text != firstText).Select(h => h.Table.Name).Distinct().ToList();
                Console.WriteLine($"{id} [{first.Label}]: {Ds1Fmgs.OneLine(firstText)}" +
                    (others.Count > 0 ? $"   (+ different text in {string.Join(", ", others)}: use --fmg or --all)" : ""));
            }
        }
        return 0;
    }

    static bool TryRange(string a, out (int, int) r)
    {
        r = default;
        int dash = a.IndexOf('-', 1);
        if (dash < 0) { if (!int.TryParse(a, out int v)) return false; r = (v, v); return true; }
        if (!int.TryParse(a[..dash], out int lo) || !int.TryParse(a[(dash + 1)..], out int hi) || hi < lo || hi - lo > 100000) return false;
        r = (lo, hi);
        return true;
    }

    static int Fail(string why)
    {
        Console.Error.WriteLine($"ds1-text: {why}");
        Console.Error.WriteLine(Usage);
        return 2;
    }
}
