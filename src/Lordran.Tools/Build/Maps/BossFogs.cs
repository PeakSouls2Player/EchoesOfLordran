using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Session 13 (seam13), rule <b>FW1-B</b> (user, 10/19: "bosses should only spawn when they enter the boss arena through the fog,
/// then during the boss fight the doors fog up again and only unfog if the boss is defeated. Look how bosses are handled in DS2"):
/// a DS1 light wall that a boss's defeat event removes is a boss fog, and becomes DS2's boss fog, not FW1's two-way mist.
/// <para>DS2 (vanilla MapObjectWhiteDoorParam): Forest of Fallen Giants' Last Giant fog 101000 = whiteDoorCategory 1, front key
/// guide only ("Enter the mist" from outside, none from inside: the arena is sealed while the fight runs), boss-fog effect 281,
/// passFlagId = the boss's killed flag (110000081: the fog is gone once the boss is dead). FW1's template 100 is category 5 with
/// both guides (400 / 410) and no pass flag: crossable both ways at any time. The Asylum's boss fog (AsylumScript.Params: 101000,
/// front 401, back 0, passFlag = the Asylum Demon's killed flag) is the verified instance (in game, sessions 7-9).</para>
/// <para>Which walls: the walls a DS1 event that runs Handle Boss Defeat (2003:12 on boss B) hides (2005:3 object state 0, or
/// 2006:1 delete map SFX on wall + 1, its fog effect); killed flag = the flag that event sets right before 2003:12 (else the event
/// id), as Ds1MapContext.KilledFlag and BossBattleParam.killedEventId. Orientation: the DS2 door only offers "Enter" on its front,
/// so the front must face away from the arena. Calibrated on the Asylum's verified boss fog (DS1 o8950 1811990 placed with DS1's
/// rotation, the Asylum Demon 1810800 on its back side): a wall whose boss stands on the other side of its local Z than the
/// Asylum's demon does is turned 180° about Y.</para>
/// </summary>
public static class BossFogs
{
    /// <summary>Session 12 (fog12): <paramref name="Exit"/> = rule FW1-X (a boss fog no DS1 action button crosses: the arena's far
    /// wall, sealed until the kill → DS2 category 2); <paramref name="EntryArea"/> = the DS1 action area of an entrance (rule FW1-F).</summary>
    public sealed record Fog(int Wall, int Boss, int Ds1KilledFlag, bool Turned, bool Exit = false, int EntryArea = 0);

    /// <summary>
    /// Session 12 (fog12), rules FW1-X / FW1-F: DS1's fog crossings = every 3:18 / 3:19 "Action Button (Line Segment)" over a
    /// wall: wall entity → the action areas the player presses in (one per side DS1 lets the player cross from). The wall and the
    /// area are literal or event parameters (X0_4 …), resolved through every 2000:0 Initialize Event of that event (parameters
    /// from byte 8 of its arguments). A wall with no crossing is never walked through in DS1.
    /// </summary>
    public static Dictionary<int, List<int>> Crossings(EMEVD emevd, Dictionary<(int, int), (string Name, int[] Types)> defs)
    {
        var res = new Dictionary<int, List<int>>();
        var inits = emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2000 && i.ID == 0 && i.ArgData.Length >= 8)
            .GroupBy(i => (long)BitConverter.ToUInt32(i.ArgData, 4)).ToDictionary(g => g.Key, g => g.Select(i => i.ArgData).ToList());
        foreach (var ev in emevd.Events)
            for (int k = 0; k < ev.Instructions.Count; k++)
            {
                var ins = ev.Instructions[k];
                if (ins.Bank != 3 || ins.ID is not (18 or 19) || !defs.TryGetValue((3, ins.ID), out var d)) continue;
                var offs = new List<int>(); int off = 0;
                foreach (int t in d.Types) { int size = t is 0 or 3 ? 1 : t is 1 or 4 ? 2 : 4; off = (off + size - 1) / size * size; offs.Add(off); off += size; }
                if (offs.Count < 10) continue;
                int areaOff = offs[2], wallOff = offs[9];
                var ps = ev.Parameters.Where(p => p.InstructionIndex == k).ToList();
                var pw = ps.FirstOrDefault(p => p.TargetStartByte == wallOff); var pa = ps.FirstOrDefault(p => p.TargetStartByte == areaOff);
                var values = new List<(int Wall, int Area)>();
                if (pw == null && pa == null) values.Add((BitConverter.ToInt32(ins.ArgData, wallOff), BitConverter.ToInt32(ins.ArgData, areaOff)));
                else
                    foreach (var a in inits.GetValueOrDefault(ev.ID) ?? [])
                    {
                        int R(EMEVD.Parameter? p, int o) => p == null ? BitConverter.ToInt32(ins.ArgData, o) : 8 + p.SourceStartByte + 4 <= a.Length ? BitConverter.ToInt32(a, (int)(8 + p.SourceStartByte)) : 0;
                        values.Add((R(pw, wallOff), R(pa, areaOff)));
                    }
                foreach (var (w, a) in values)
                {
                    if (w <= 0) continue;
                    if (!res.TryGetValue(w, out var l)) res[w] = l = [];
                    if (a > 0 && !l.Contains(a)) l.Add(a);
                }
            }
        return res;
    }

    static int? asylumSign;

    /// <summary>Session 12 (fog12), rule FW1-M: objects DS1's event 0 (the load event) disables (2005:3 state 0). For a light
    /// wall nothing crosses and no boss defeat removes, these are DS1's multiplayer-only walls (event 0 disables them unless the
    /// player is a client: m10_01 1011994 … 1011972, under "SKIP IF Multiplayer State(Client)").</summary>
    public static HashSet<int> HostDisabled(EMEVD emevd, Dictionary<(int, int), (string Name, int[] Types)> defs) =>
        emevd.Events.Where(e => e.ID == 0).SelectMany(e => e.Instructions).Where(i => i.Bank == 2005 && i.ID == 3)
            .Select(i => Events.EmevdTranslator.Decode(defs, i)).Where(a => a.Length > 1 && a[1] == 0).Select(a => (int)a[0]).ToHashSet();

    /// <summary>Signed distance of <paramref name="target"/> along the wall's local +Z (DS1 / DS2 yaw in degrees).</summary>
    public static float Side(Vector3 wallPos, Vector3 wallRotDeg, Vector3 target)
    {
        double y = wallRotDeg.Y * Math.PI / 180;
        var fwd = new Vector3((float)Math.Sin(y), 0, (float)Math.Cos(y));
        return Vector3.Dot(fwd, target - wallPos);
    }

    /// <summary>The sign of the Asylum boss fog's local Z toward its boss (the verified "front faces out" placement).</summary>
    public static int AsylumSign(string dsrRoot)
    {
        if (asylumSign is int s) return s;
        var msb = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", "m18_01_00_00.msb"));
        var wall = msb.Parts.Objects.First(p => p.EntityID == 1811990);
        var boss = msb.Parts.Enemies.First(p => p.EntityID == 1810800);
        asylumSign = Math.Sign(Side(wall.Position, wall.Rotation, boss.Position));
        return asylumSign.Value;
    }

    /// <summary>The boss fogs of a DS1 map: wall entity → (boss, DS1 killed flag, turned 180°).</summary>
    public static Dictionary<int, Fog> Of(string dsrRoot, MSB1 msb1, EMEVD emevd, Dictionary<(int, int), (string Name, int[] Types)> defs, IReadOnlySet<int> walls)
    {
        var res = new Dictionary<int, Fog>();
        int s0 = AsylumSign(dsrRoot);
        var crossings = Crossings(emevd, defs);
        foreach (var ev in emevd.Events)
        {
            int at = ev.Instructions.FindIndex(i => i.Bank == 2003 && i.ID == 12);
            if (at < 0) continue;
            int boss = (int)Events.EmevdTranslator.Decode(defs, ev.Instructions[at])[0];
            if (boss <= 0) continue;
            int killed = (int)ev.ID;
            for (int k = at - 1; k >= 0; k--)
                if (ev.Instructions[k] is { Bank: 2003, ID: 2 } x && Events.EmevdTranslator.Decode(defs, x) is var v && v[1] == 1) { killed = (int)v[0]; break; }
            var bossPart = msb1.Parts.Enemies.FirstOrDefault(p => p.EntityID == boss);
            foreach (var i in ev.Instructions)
            {
                int wall;
                if (i is { Bank: 2005, ID: 3 } && Events.EmevdTranslator.Decode(defs, i) is var a && a.Length > 1 && a[1] == 0) wall = (int)a[0];
                else if (i is { Bank: 2006, ID: 1 } && Events.EmevdTranslator.Decode(defs, i) is var b && b.Length > 0) wall = (int)b[0] - 1;
                else continue;
                if (!walls.Contains(wall) || res.ContainsKey(wall)) continue;
                var w = msb1.Parts.Objects.First(p => p.EntityID == wall);
                // session 12 (fog12), rule FW1-X: a boss fog no DS1 crossing names is the arena's far wall (Taurus 1011892, the
                // Gargoyles' 1011992): DS1 never lets the player through it until the kill; DS2's form is the exit fog
                // (vanilla 101011: category 2, no key guide, pass flag = killed flag), placed with DS1's rotation
                if (!crossings.TryGetValue(wall, out var areas)) { res[wall] = new Fog(wall, boss, killed, false, Exit: true); continue; }
                // rule FW1-F: an entrance's front ("Enter the mist"; the Asylum calibration: the side opposite s0) faces DS1's own
                // action area (where DS1 lets the player press); the boss's spawn spot is only the fallback (a boss can stand on
                // a tower behind its arena's far wall, as the Taurus Demon does)
                var area = areas.Select(a => msb1.Regions.GetEntries().FirstOrDefault(r => r.EntityID == a)).FirstOrDefault(r => r != null);
                bool turn = area != null
                    ? Math.Sign(Side(w.Position, w.Rotation, area.Position)) is int sa && sa != 0 && sa != -s0
                    : bossPart != null && Math.Sign(Side(w.Position, w.Rotation, bossPart.Position)) is int s && s != 0 && s != s0;
                res[wall] = new Fog(wall, boss, killed, turn, EntryArea: area?.EntityID ?? 0);
            }
        }
        return res;
    }
}
