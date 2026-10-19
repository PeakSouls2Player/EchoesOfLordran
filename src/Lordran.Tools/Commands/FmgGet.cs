using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// fmg-get FILE.fmg ID... | --all | --range FIRST LAST — entries of a DS2 (or any) FMG by id. Session 8 (track npc):
/// --all / --range list every non-empty entry (in id order) for checking the talk FMGs a build writes; FILE may also be
/// gamedata-relative inside a built pack folder (e.g. data\sandbox\npc\pack\gamedata\menu\text\english\talk\m18_01_00_00.fmg).
/// </summary>
public static class FmgGet
{
    public static int Run(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length < 3) { Console.Error.WriteLine("usage: fmg-get FILE.fmg ID... | --all | --range FIRST LAST"); return 1; }
        var fmg = FMG.Read(args[1]);
        static string One(string? t) => t?.Replace("\n", " / ") ?? "";
        if (args[2] is "--all" or "--range")
        {
            long first = long.MinValue, last = long.MaxValue;
            if (args[2] == "--range")
            {
                if (args.Length < 5 || !long.TryParse(args[3], out first) || !long.TryParse(args[4], out last)) { Console.Error.WriteLine("--range needs FIRST LAST"); return 1; }
            }
            var hits = fmg.Entries.Where(e => e.ID >= first && e.ID <= last && !string.IsNullOrEmpty(e.Text)).OrderBy(e => e.ID).ToList();
            foreach (var e in hits) Console.WriteLine($"{e.ID}: {One(e.Text)}");
            Console.WriteLine($"# {hits.Count} non-empty entries of {fmg.Entries.Count}");
            return 0;
        }
        foreach (var a in args.Skip(2)) Console.WriteLine($"{a}: {One(fmg.Entries.FirstOrDefault(e => e.ID == int.Parse(a))?.Text)}");
        return 0;
    }
}
