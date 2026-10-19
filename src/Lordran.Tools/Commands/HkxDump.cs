using Lordran.Tools.Formats.Havok;

namespace Lordran.Tools.Commands;

/// <summary>
/// hkx-dump FILE [--classes|--types] [--max N] [--roundtrip OUT] — dumps a Havok packfile (own reflection data) or
/// tagfile (own type section). --roundtrip rewrites a packfile with HkPackWriter and compares the decoded objects.
/// </summary>
public static class HkxDump
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: hkx-dump FILE [--classes|--types] [--max N] [--roundtrip OUT]"); return 1; }
        byte[] bytes = File.ReadAllBytes(args[1]);
        if (SoulsFormats.DCX.Is(bytes)) bytes = SoulsFormats.DCX.Decompress(bytes);
        int max = args.SkipWhile(a => a != "--max").Skip(1).Select(int.Parse).FirstOrDefault(8);
        if (System.Text.Encoding.ASCII.GetString(bytes, 4, 4) == "TAG0")
        {
            var tag = new HkTagfile(bytes);
            Console.WriteLine(args.Contains("--types") ? tag.DumpTypes() : tag.Dump(max));
            return 0;
        }
        var hk = new HkPackfile(bytes);
        string? roundtrip = args.SkipWhile(a => a != "--roundtrip").Skip(1).FirstOrDefault();
        if (roundtrip != null)
        {
            byte[] written = new HkPackWriter(hk).Write(hk.ReadRoot());
            File.WriteAllBytes(roundtrip, written);
            var again = new HkPackfile(written);
            var la = ObjectLines(hk);
            var lb = ObjectLines(again);
            bool same = la.SequenceEqual(lb);
            Console.WriteLine($"round trip: {bytes.Length} -> {written.Length} bytes, identical bytes: {bytes.AsSpan().SequenceEqual(written)}, identical objects: {same}");
            if (!same)
                for (int i = 0; i < Math.Min(la.Count, lb.Count); i++)
                    if (la[i] != lb[i]) { Console.WriteLine($"first difference at line {i}:"); Console.WriteLine("  < " + la[i]); Console.WriteLine("  > " + lb[i]); break; }
            return 0;
        }
        if (args.Contains("--classes"))
        {
            foreach (var c in hk.Classes.Values.OrderBy(c => c.Name))
            {
                Console.WriteLine(c);
                foreach (var m in c.Members) Console.WriteLine($"    {m}");
            }
            return 0;
        }
        Console.WriteLine(hk.Dump(max));
        return 0;
    }

    /// <summary>The object dump without the header/section lines and without offsets (which legitimately move).</summary>
    static List<string> ObjectLines(HkPackfile hk) =>
        hk.Dump(100000).Split('\n').Skip(5)
            .Select(l => System.Text.RegularExpressions.Regex.Replace(l, @"@0x[0-9A-F]+|→0x[0-9A-F]+", ""))
            .ToList();
}
