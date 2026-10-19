using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (fix11; build-pack --debug-markers, off by default): instrumentation for the in-game "Asylum bounce". Every
/// cross-map warp (1:132125) in a Lordran event ESD (event_m18_* / event_m19_*) is preceded, in the same state's entry
/// commands, by 130101(marker, 1): marker = 209980 + n (n in ESD file / group / state order; at most 20), DS2's persistent
/// global block 20 at the top of its last free pool run (209558-209999 in Mappings\ds1-flag-slots.tsv; no slot row uses
/// 2099xx). After a bounce, `lr save-flags SL2 SLOT 209980 … 209999` names the group that warped (the table is in build.log
/// and lordran-debug\markers.tsv). The warp itself is unchanged.
/// </summary>
public static class DebugMarkers
{
    public static bool Enabled;
    public const int Base = 209980, Max = 20;

    public static List<string> Apply(Build.Pack pack)
    {
        var lines = new List<string>();
        var table = new List<string> { "flag\tesd\tgroup\tstate\twarp" };
        int n = 0;
        foreach (var (path, bytes) in pack.Files("gamedata", "ezstate/").ToList())
        {
            string name = Path.GetFileName(path).ToLowerInvariant();
            if (!(name.StartsWith("event_m18_") || name.StartsWith("event_m19_")) || !name.EndsWith(".esd")) continue;
            var esd = ESD.Read(bytes);
            bool changed = false;
            foreach (var (gid, g) in esd.StateGroups)
                foreach (var (sid, s) in g)
                    for (int i = 0; i < s.EntryCommands.Count; i++)
                    {
                        var c = s.EntryCommands[i];
                        if (c.CommandBank != 1 || c.CommandID != 132125) continue;
                        if (n >= Max) throw new InvalidDataException($"--debug-markers: more than {Max} cross-map warps");
                        int flag = Base + n++;
                        s.EntryCommands.Insert(i++, new ESD.CommandCall(1, 130101, Int(flag), Int(1)));
                        string warp = string.Join(", ", c.Arguments.Select(a => Build.Events.EventSim.Evaluate(a, (_, _) => 0)));
                        lines.Add($"debug marker {flag}: {name} group {gid} state {sid} sets it before 132125({warp})");
                        table.Add($"{flag}\t{name}\t{gid}\t{sid}\t132125({warp})");
                        changed = true;
                    }
            if (changed) pack.AddFile("gamedata", path, esd.Write());
        }
        pack.AddFile("lordran-debug", "markers.tsv", System.Text.Encoding.UTF8.GetBytes(string.Join("\n", table) + "\n"));
        lines.Add($"--debug-markers: {n} cross-map warp(s) instrumented (flags {Base}..{Base + Math.Max(n, 1) - 1}); read them with lr save-flags SL2 SLOT");
        return lines;
    }

    static byte[] Int(int v) => v is >= -64 and < 64 ? [(byte)(0x40 + v), 0xA1] : [0x82, .. BitConverter.GetBytes(v), 0xA1];
}
