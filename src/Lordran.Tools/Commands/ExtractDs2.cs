using Lordran.Tools.Formats;

namespace Lordran.Tools.Commands;

/// <summary>
/// extract-ds2 [--game DIR] [--out DIR] [--dict FILE] [--lq] [--list] [--only /prefix ...]
/// Extracts DS2's archived files into a working folder (never into the game folder).
/// --list only writes an index (index.tsv) without extracting.
/// </summary>
public static class ExtractDs2
{
    public const string DefaultDictionary = @"C:\EchoesOfLordran\tools\uxm\UXM Selective Unpack 2.4.2.0\res\ScholarDictionary.txt";
    public const string DefaultOut = @"C:\EchoesOfLordran\data\ds2";

    public static int Run(string[] args)
    {
        string game = Program.DefaultDs2Root, outDir = DefaultOut, dict = DefaultDictionary;
        bool lq = false, listOnly = false;
        var only = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--game": game = args[++i]; break;
                case "--out": outDir = args[++i]; break;
                case "--dict": dict = args[++i]; break;
                case "--lq": lq = true; break;
                case "--list": listOnly = true; break;
                case "--only":
                    while (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                        only.Add(args[++i].ToLowerInvariant());
                    break;
                default: Console.Error.WriteLine($"unknown option {args[i]}"); return 1;
            }
        }

        if (Path.GetFullPath(outDir).StartsWith(Path.GetFullPath(game), StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("refusing to extract into the game folder");
            return 1;
        }

        var names = new Ds2FileNames(dict);
        Console.WriteLine($"dictionary: {names.Count} paths");
        Directory.CreateDirectory(outDir);
        using var index = new StreamWriter(Path.Combine(outDir, listOnly ? "index.tsv" : "extracted.tsv"));
        index.WriteLine("archive\tpath\tbytes\taes");

        foreach (var (archiveName, key) in Ds2Archive.All)
        {
            if (!lq && archiveName.StartsWith("Lq"))
                continue;

            var archive = Ds2Archive.Open(game, archiveName, key);
            var files = archive.Files.OrderBy(f => f.FileOffset).ToList();
            int known = files.Count(f => names.Lookup(f.FileNameHash) != null);
            Console.WriteLine($"{archiveName}: {files.Count} files, {known} named, {files.Count - known} unnamed");

            using var bdt = listOnly ? null : File.OpenRead(archive.BdtPath);
            long written = 0;
            int count = 0;
            foreach (var file in files)
            {
                string path = names.Lookup(file.FileNameHash) ?? $"/_unknown/{archiveName}/{file.FileNameHash:x8}";
                if (only.Count > 0 && !only.Any(p => path.StartsWith(p)))
                    continue;

                index.WriteLine($"{archiveName}\t{path}\t{file.PaddedFileSize}\t{(file.AESKey != null ? "y" : "n")}");
                if (bdt == null)
                    continue;

                byte[] bytes = file.ReadFile(bdt);
                string target = Path.Combine(outDir, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
                written += bytes.Length;
                if (++count % 1000 == 0)
                    Console.WriteLine($"  {count} files, {written / (1024 * 1024)} MB");
            }
            if (bdt != null)
                Console.WriteLine($"  done: {count} files, {written / (1024 * 1024)} MB");
        }
        return 0;
    }
}
