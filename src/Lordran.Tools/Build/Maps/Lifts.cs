using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Session 11 (burg11, BURG-PLAN LF1): DS1 two-way lifts (Firelink ↔ Parish o1460, entity 1021000, events 11020300/301)
/// as DS2 event-driven prefab objects, found by rule in DS1's own script — no map, model or entity literal:
///  - a **lift** is an object part whose DS1 events play (2003:18) animations right after setting one event flag F on
///    (the car goes to F's "on" end: o1460 anims 0 and 2) and others right after setting F off (anim 1);
///  - the clip of the ON move played in the same event as an OFF move is the canonical one (o1460: 2; 0 is the first ride
///    from the same OFF pose, an alias); the OFF move likewise (1);
///  - **rest anims**: an animation the script plays on the part with no flag set before it, after an ON / OFF move in the
///    same event (o1460 22 after 0/2, 21 after 1: the car settling), and the poses event 0 reproduces (2005:7) on each
///    side of "SKIP IF F" (o1460 11 when F is off, 12 when on) → the side's rest state, no movement.
/// DS2 side: DS2's own platforms are prefabs whose script requests a move state and waits for its rest state
/// (m10_19 【実行】レバーで上下する足場 o19_9004: 70 → rests 30, 80 → rests 10; FoFG's twin lift o10_9210 the same pattern)
/// on instance rows with objCtrlType 11 (Elevator) and objType 1607 (vanilla 10191100). The port uses the gimmick-door
/// template o15_1060 (rule C11 table: 10001 plays in 70 → rests 30, 10002 in 80 → rests 10; its machine also takes direct
/// requests of 10 and 30, so DS1's reproduced poses at load are requests of the rest states, not replays): DS1's ON move →
/// a00_01_0001, OFF move → a00_01_0002; the prefab starts in 10 (= the OFF pose, DS1's flag off at a new game).
/// Instance AABB·10000 + entity % 10000 (the door rule), objCtrlType 11 / objType 1607 as vanilla's lifts (U: the ride).
/// </summary>
public static class Lifts
{
    public const string Template = "o15_1060";
    /// <summary>Template states (AsylumScript.PrefabStates["o15_1060"]): ON move plays in 70 → rests 30; OFF in 80 → 10.</summary>
    public const int OnPlay = 70, OnRest = 30, OffPlay = 80, OffRest = 10;

    public sealed record Lift(int Entity, string Ds1Model, string Ds2Model, int Instance, Vector3 Position, Vector3 Rotation, int Flag,
        int On, int Off, IReadOnlySet<int> OnMoves, IReadOnlySet<int> OffMoves, IReadOnlySet<int> OnRests, IReadOnlySet<int> OffRests)
    {
        public string Describe() => $"lift {Entity} ({Ds1Model} → {Ds2Model}, instance {Instance}): flag {Flag}; ON move a00_{On:D4} (+{string.Join(",", OnMoves.Where(a => a != On))}) → state {OnPlay}/{OnRest}, OFF move a00_{Off:D4} (+{string.Join(",", OffMoves.Where(a => a != Off))}) → {OffPlay}/{OffRest}; rests ON {string.Join(",", OnRests)} / OFF {string.Join(",", OffRests)}";
    }

    static readonly Dictionary<string, List<Lift>> cache = [];

    /// <summary>The DS1 map's lifts by rule (pure: DS1's MSB and EMEVD).</summary>
    public static List<Lift> Find(string dsrRoot, string ds1Map, string ds2Map)
    {
        string key = ds1Map + "|" + ds2Map;
        if (cache.TryGetValue(key, out var hit)) return hit;
        var result = new List<Lift>();
        string emevdPath = Path.Combine(dsrRoot, "event", ds1Map + ".emevd.dcx");
        if (!File.Exists(emevdPath)) return cache[key] = result;
        var msb1 = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", ds1Map + ".msb"));
        var defs = Commands.EmevdDump.Defs();
        var emevd = EMEVD.Read(emevdPath);
        // per object entity: (flag, state) → anims moved there; (event id) → [(anim, flag state or null)] in order
        var moves = new Dictionary<int, List<(long Event, int Anim, int Flag, int State)>>();
        var plays = new Dictionary<int, List<(long Event, int Index, int Anim)>>();
        var poses = new Dictionary<int, List<(int Anim, int Flag, int State)>>();
        foreach (var ev in emevd.Events)
        {
            var ins = ev.Instructions;
            (int Flag, int State)? lastSet = null;
            for (int i = 0; i < ins.Count; i++)
            {
                var x = ins[i];
                double[] v;
                try { v = Events.EmevdTranslator.Decode(defs, x); } catch { continue; }
                if (x.Bank == 2003 && x.ID == 2 && v.Length >= 2) { lastSet = ((int)v[0], (int)v[1]); continue; }
                if (x.Bank == 2003 && x.ID == 18 && v.Length >= 2)
                {
                    int e = (int)v[0], a = (int)v[1];
                    if (!msb1.Parts.Objects.Any(p => p.EntityID == e)) continue;
                    if (lastSet is { } s) (moves.TryGetValue(e, out var l) ? l : moves[e] = []).Add((ev.ID, a, s.Flag, s.State));
                    (plays.TryGetValue(e, out var pl) ? pl : plays[e] = []).Add((ev.ID, i, a));
                    lastSet = null;
                    continue;
                }
                // 1003:1 SKIP IF Event Flag(lines, state, type, flag): the next `lines` instructions run when flag != state,
                // the one after them when flag == state (DS1's if/else of event 0's reproduced poses)
                if (x.Bank == 1003 && x.ID == 1 && v.Length >= 4 && (int)v[2] == 0)
                {
                    int n = (int)v[0], state = (int)v[1], flag = (int)v[3];
                    for (int k = i + 1; k <= i + n + 1 && k < ins.Count; k++)
                    {
                        if (ins[k].Bank != 2005 || ins[k].ID != 7) continue;
                        double[] w;
                        try { w = Events.EmevdTranslator.Decode(defs, ins[k]); } catch { continue; }
                        int s2 = k <= i + n ? 1 - state : state;
                        (poses.TryGetValue((int)w[0], out var pp) ? pp : poses[(int)w[0]] = []).Add(((int)w[1], flag, s2));
                    }
                }
            }
        }
        int aabb = MapHosts.Aabb(ds2Map);
        foreach (var (e, list) in moves)
        {
            foreach (var g in list.GroupBy(m => m.Flag))
            {
                var on = g.Where(m => m.State == 1).Select(m => m.Anim).ToHashSet();
                var off = g.Where(m => m.State == 0).Select(m => m.Anim).ToHashSet();
                if (on.Count == 0 || off.Count == 0 || on.Overlaps(off)) continue;
                var bothEvents = g.GroupBy(m => m.Event).Where(eg => eg.Any(m => m.State == 1) && eg.Any(m => m.State == 0)).SelectMany(eg => eg).ToList();
                int canonOn = bothEvents.Where(m => m.State == 1).Select(m => m.Anim).DefaultIfEmpty(on.Min()).Min();
                int canonOff = bothEvents.Where(m => m.State == 0).Select(m => m.Anim).DefaultIfEmpty(off.Min()).Min();
                // rest anims: the next animation on the part after a move in the same event, not itself a move
                var onRests = new HashSet<int>(); var offRests = new HashSet<int>();
                foreach (var eg in plays[e].GroupBy(p => p.Event))
                {
                    var seq = eg.OrderBy(p => p.Index).Select(p => p.Anim).ToList();
                    for (int k = 0; k + 1 < seq.Count; k++)
                    {
                        if (on.Contains(seq[k]) && !on.Contains(seq[k + 1]) && !off.Contains(seq[k + 1])) onRests.Add(seq[k + 1]);
                        if (off.Contains(seq[k]) && !on.Contains(seq[k + 1]) && !off.Contains(seq[k + 1])) offRests.Add(seq[k + 1]);
                    }
                }
                foreach (var (a, f, s) in poses.GetValueOrDefault(e) ?? [])
                    if (f == g.Key && !on.Contains(a) && !off.Contains(a)) (s == 1 ? onRests : offRests).Add(a);
                if (onRests.Overlaps(offRests)) continue;
                var part = msb1.Parts.Objects.First(p => p.EntityID == e);
                string ds2Model = MapHosts.ObjectName(ds2Map.Substring(1, 2), part.ModelName);
                result.Add(new Lift(e, part.ModelName, ds2Model, aabb * 10000 + e % 10000, part.Position, part.Rotation, g.Key,
                    canonOn, canonOff, on, off, onRests, offRests));
            }
        }
        return cache[key] = result;
    }

    /// <summary>The prefab ports (one per DS1 model).</summary>
    public static ObjPorter.Options[] Ports(string dsrRoot, string ds1Map, string ds2Map) =>
        Find(dsrRoot, ds1Map, ds2Map).GroupBy(l => l.Ds1Model).Select(g => g.First()).Select(l =>
            new ObjPorter.Options(l.Ds1Model, l.Ds2Model, Template, [($"a00_{l.On:D4}", "a00_01_0001"), ($"a00_{l.Off:D4}", "a00_01_0002")], ObjPorter.Kind.Prefab)).ToArray();

    /// <summary>The placements (MapBuilder.Options.Placements); registers the parts in MapBuilder.PlacedObjects.</summary>
    public static List<MapBuilder.Placement> Placements(string dsrRoot, string ds1Map, string ds2Map) =>
        Find(dsrRoot, ds1Map, ds2Map).Select(l =>
        {
            MapBuilder.PlacedObjects[(ds2Map, l.Entity)] = l.Instance;
            return new MapBuilder.Placement(l.Ds2Model, l.Position, l.Rotation, l.Instance, MapBuilder.PlacementKind.Generic, 0,
                $"(DS1 entity {l.Entity}) rule LF1: {l.Describe()}")
            { Fields = new() { ["objCtrlType"] = 11, ["objType"] = 1607 } };
        }).ToList();

    /// <summary>The lift of a DS1 entity, or null.</summary>
    public static Lift? Of(string dsrRoot, string ds1Map, string ds2Map, int e) => Find(dsrRoot, ds1Map, ds2Map).FirstOrDefault(l => l.Entity == e);

    /// <summary>Rule LF1 states of DS1 animation <paramref name="anim"/>: (play state or null for a rest anim, rest state).</summary>
    public static (int? Play, int RestState)? States(Lift l, int anim) =>
        l.OnMoves.Contains(anim) ? (OnPlay, OnRest) : l.OffMoves.Contains(anim) ? (OffPlay, OffRest)
        : l.OnRests.Contains(anim) ? (null, OnRest) : l.OffRests.Contains(anim) ? (null, OffRest) : null;
}
