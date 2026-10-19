using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// emevd-grep FILE.emevd.dcx|MAP VALUE... — DS1 (DSR) event scripts: every instruction whose arguments contain one of the
/// int32 VALUEs (entity ids), with its event id, bank:id and all arguments as int32s. Session 6: which DS1 events switch
/// the Asylum's entity collisions (doorway blockers, boulder corridor, collapsing floor) on and off. With no VALUE it lists
/// the instructions that take event parameters.
/// Session 8: a map name (m18_01_00_00, common) resolves to DSR's event\NAME.emevd.dcx; a missing file, non-numeric
/// values or swapped arguments print the usage instead of crashing.
/// </summary>
public static class EmevdGrep
{
    const string Usage =
        "usage: emevd-grep FILE.emevd.dcx|MAP [VALUE...]\n" +
        "  MAP = a DSR event script name, e.g. m18_01_00_00 or common (DSR event\\MAP.emevd.dcx)\n" +
        "  no VALUE: instructions that take event parameters\n" +
        "  e.g. emevd-grep m18_01_00_00 1811111 1811115";

    public static int Run(string[] args)
    {
        if (args.Length < 2) return CliArgs.Fail("emevd-grep", Usage, "missing FILE");
        string? path = Resolve(args[1]);
        if (path == null)
        {
            bool swapped = int.TryParse(args[1], out _) && args.Skip(2).Any(a => Resolve(a) != null);
            return CliArgs.Fail("emevd-grep", Usage, $"no such event script: '{args[1]}'" + (swapped ? " (FILE goes first, then the values)" : ""));
        }
        var want = new HashSet<int>();
        foreach (string a in args.Skip(2))
        {
            if (!int.TryParse(a, out int v)) return CliArgs.Fail("emevd-grep", Usage, $"not an int32 value: '{a}'" + (Resolve(a) != null ? " (only one FILE is allowed, first)" : ""));
            want.Add(v);
        }
        EMEVD emevd;
        try { emevd = EMEVD.Read(path); }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or NotSupportedException or FormatException)
        { return CliArgs.Fail("emevd-grep", Usage, $"{path} is not a readable EMEVD: {e.Message}"); }
        foreach (var ev in emevd.Events)
            for (int i = 0; i < ev.Instructions.Count; i++)
            {
                var ins = ev.Instructions[i];
                var ints = Enumerable.Range(0, ins.ArgData.Length / 4).Select(k => BitConverter.ToInt32(ins.ArgData, k * 4)).ToList();
                bool hitParam = ev.Parameters.Any(p => p.InstructionIndex == i) && want.Count == 0;
                if (ints.Any(want.Contains) || hitParam)
                    Console.WriteLine($"event {ev.ID,8} [{i,3}] {ins.Bank}:{ins.ID,-3} ({string.Join(", ", ints)})");
            }
        return 0;
    }

    /// <summary>An existing file, or a DSR event script name (with or without .emevd.dcx); null if neither exists.</summary>
    static string? Resolve(string a)
    {
        if (File.Exists(a)) return a;
        if (a.Length == 0 || a.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        string name = a.EndsWith(".emevd.dcx", StringComparison.OrdinalIgnoreCase) ? a : a + ".emevd.dcx";
        string dsr = Path.Combine(Program.DefaultDsrRoot, "event", name);
        return File.Exists(dsr) ? dsr : null;
    }
}
