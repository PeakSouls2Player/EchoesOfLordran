using Lordran.Tools.Build.Events;

namespace Lordran.Tools.Commands;

/// <summary>
/// npc-events [--speffect ID…] — session 10 (asy-events e3): census of the character / game-state event instructions
/// over every DSR map script (rules SP1, HB, N1, N3, N4, G2, G5, G5b in <see cref="CharacterEvents"/>), and with
/// --speffect the SP1 verdict and the effective (non-default, non-inert) fields of DS1 SpEffectParam rows.
/// </summary>
public static class NpcEventsReport
{
    public static int Run(string[] args)
    {
        string root = Program.DefaultDsrRoot;
        var ids = args.SkipWhile(a => a != "--speffect").Skip(1).TakeWhile(a => !a.StartsWith("--")).Select(int.Parse).ToList();
        if (ids.Count > 0)
        {
            foreach (int id in ids)
            {
                var r = CharacterEvents.SpEffect(root, id);
                if (r == null) { Console.WriteLine($"SpEffect {id}: not in DSR"); continue; }
                var hp = CharacterEvents.HpPercent(root, id, out string why);
                Console.WriteLine($"SpEffect {id}: effective fields [{string.Join(", ", CharacterEvents.Effective(r).Select(f => $"{f}={r[f]!.Value}"))}]");
                Console.WriteLine($"  rule SP1: {(hp != null ? $"130317(gen, {hp:0.###}f)" : "no rule")} — {why}");
            }
            return 0;
        }
        foreach (var line in CharacterEvents.Census(root, EmevdDump.Defs())) Console.WriteLine(line);
        return 0;
    }
}
