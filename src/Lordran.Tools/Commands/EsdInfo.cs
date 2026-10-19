using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>esd-info FILE — header fields and state machines of an ESD (counts of states/conditions/commands).</summary>
public static class EsdInfo
{
    public static int Run(string[] args)
    {
        var esd = ESD.Read(args[1]);
        Console.WriteLine($"long={esd.LongFormat} dsCount={esd.DarkSoulsCount} name='{esd.Name}' unk70..7C={esd.Unk70},{esd.Unk74},{esd.Unk78},{esd.Unk7C} groups={esd.StateGroups.Count}");
        foreach (var (gid, states) in esd.StateGroups.OrderBy(g => g.Key).Take(args.Length > 2 ? int.Parse(args[2]) : 12))
        {
            int conds = states.Values.Sum(s => s.Conditions.Count), cmds = states.Values.Sum(s => s.EntryCommands.Count + s.ExitCommands.Count + s.WhileCommands.Count);
            Console.WriteLine($"  group 0x{gid:X} ({gid}): {states.Count} states, {conds} conditions, {cmds} commands; first state {states.Keys.Min()}");
        }
        return 0;
    }
}
