using Lordran.Tools.Build.Chr;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (track gest11), DS1 gestures (Build/Chr/Ds1Gestures.cs, rules GS1-GS6):
/// <list type="bullet">
/// <item><c>gesture-check PACKDIR</c>: the pack's c0001.anibnd.dcx and c000100_pl/sfx/snd.tae against vanilla
/// (Ds1Gestures.VerifyAdditive: every vanilla clip, node, category, bundle, file name and TAE animation byte-identical,
/// only additions; nmb-runtime-check), plus the pack's gestures.ini, names, hint texts and icons. Exit 0 = all hold.</item>
/// <item><c>gesture-build OUTDIR</c>: the gesture animation files alone (no pack), with the report and the check.</item>
/// </list>
/// </summary>
public static class GestureTools
{
    static readonly string[] Shared = ["morpheme4/chr/c0001.anibnd.dcx", "timeact/chr/c000100_pl.tae", "timeact/chr/c000100_sfx.tae", "timeact/chr/c000100_snd.tae"];

    public static int Check(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: gesture-check PACKDIR"); return 2; }
        string pack = args[1];
        if (!File.Exists(Path.Combine(pack, "gamedata", "morpheme4", "chr", "c0001.anibnd.dcx")) && !File.Exists(Path.Combine(pack, Ds1Gestures.TableDevice, Ds1Gestures.TablePath)))
        {
            Console.WriteLine("gesture-check: the pack has no DS1 gestures (built with --no-gestures): nothing to check");
            return 0;
        }
        var files = new Dictionary<string, byte[]>();
        foreach (var p in Shared)
        {
            string f = Path.Combine(pack, "gamedata", p.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(f)) { Console.Error.WriteLine($"gesture-check: the pack has no {p}"); return 1; }
            files[p] = File.ReadAllBytes(f);
        }
        var v = Ds1Gestures.VerifyAdditive(ExtractDs2.DefaultOut, files);
        foreach (var l in v.Report) Console.WriteLine(l);
        foreach (var l in v.Problems) Console.WriteLine("PROBLEM " + l);
        int bad = v.Problems.Count;
        // table, texts, icons
        string ini = Path.Combine(pack, Ds1Gestures.TableDevice, Ds1Gestures.TablePath);
        if (!File.Exists(ini)) { Console.WriteLine($"PROBLEM no {Ds1Gestures.TableDevice}\\{Ds1Gestures.TablePath}"); bad++; }
        else
        {
            var lines = File.ReadAllLines(ini);
            int count = lines.Count(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\[g\d+\]"));
            string unlock = lines.FirstOrDefault(l => l.StartsWith("unlock_all=")) ?? "unlock_all=?";
            var cats = lines.Where(l => l.StartsWith("category=")).Select(l => int.Parse(l[9..])).ToList();
            bool catsOk = cats.SequenceEqual(Ds1Gestures.All.Select(Ds1Gestures.Category));
            Console.WriteLine($"table: {count} gestures, {unlock}, categories {(catsOk ? "= the network's" : "DIFFER")}, flags {string.Join(",", lines.Where(l => l.StartsWith("flag=")).Select(l => l[5..]))}");
            if (count != Ds1Gestures.All.Length || !catsOk) bad++;
        }
        string fmgPath = Path.Combine(pack, "gamedata", "menu", "text", "english", "ingamemenu.fmg");
        if (!File.Exists(fmgPath)) { Console.WriteLine("PROBLEM no ingamemenu.fmg in the pack"); bad++; }
        else
        {
            var fmg = SoulsFormats.FMG.Read(fmgPath);
            string T(int id) => fmg.Entries.FirstOrDefault(e => e.ID == id)?.Text ?? "";
            var missing = Ds1Gestures.All.Where(g => T(Ds1Gestures.TextId(g)).Length == 0).ToList();
            Console.WriteLine($"texts: guide {Ds1Gestures.TopGuideText} \"{T(Ds1Gestures.TopGuideText)}\"; Lordran guide \"{T(Ds1Gestures.LordranGuideText)}\"; pages \"{T(Ds1Gestures.PageNameText)}\"/\"{T(Ds1Gestures.PageNameText + 1)}\"; names {Ds1Gestures.All.Length - missing.Count}/{Ds1Gestures.All.Length}");
            if (missing.Count > 0 || !T(Ds1Gestures.TopGuideText).Contains("Drangleic/Lordran") || T(Ds1Gestures.PageNameText + 1).Length == 0) bad++;
        }
        var icons = Ds1Gestures.All.Where(g => !File.Exists(Path.Combine(pack, "gamedata", "menu", "tex", "icon", $"ic_{Ds1Gestures.TextId(g):D10}.tpf"))).ToList();
        Console.WriteLine($"icons: {Ds1Gestures.All.Length - icons.Count}/{Ds1Gestures.All.Length} ic_00631xx000.tpf");
        if (icons.Count > 0) bad++;
        Console.WriteLine(bad == 0 ? "gesture-check: OK" : $"gesture-check: {bad} problems");
        return bad == 0 ? 0 : 1;
    }

    public static int Build(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: gesture-build OUTDIR"); return 2; }
        var (files, report) = Ds1Gestures.BuildAnimations(ExtractDs2.DefaultOut, Program.DefaultDsrRoot);
        foreach (var l in report) Console.WriteLine(l);
        var v = Ds1Gestures.VerifyAdditive(ExtractDs2.DefaultOut, files);
        foreach (var l in v.Report) Console.WriteLine(l);
        foreach (var l in v.Problems) Console.WriteLine("PROBLEM " + l);
        foreach (var (p, b) in files)
        {
            string f = Path.Combine(args[1], "gamedata", p.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            File.WriteAllBytes(f, b);
        }
        return v.Problems.Count == 0 ? 0 : 1;
    }
}
