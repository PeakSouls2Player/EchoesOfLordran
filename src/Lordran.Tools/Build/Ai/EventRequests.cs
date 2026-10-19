using SoulsFormats;

namespace Lordran.Tools.Build.Ai;

/// <summary>
/// Session 11 (track burgchr11), rule E-REQ (asy10 enemies.md §E-REQ / CH, AI side): DS1's events command a character's AI with
/// 2004:17 Request Character AI Command(entity, command v, slot s) and the battle goal reads ai:GetEventRequest(s). A DS2 AI reads its
/// generator's logicEventFlag k+1 as f5903(k) (D11). Per DS1 think, every (slot, command ≠ −1) its characters receive in the map's
/// EMEVD (direct arguments and the arguments of every InitializeEvent of a parameterised event) gets one channel in the order k =
/// 1, 2, 3 (logicEventFlag2-4; lef1 is a boss's logic flag), sorted by (slot, command) so the channels are stable between builds;
/// GetEventRequest(s) = Σ v · (f5903(k_(s,v)) ≠ 0) over that slot's pairs (0 when none is set, as DS1's "no request"). More than 3
/// pairs on one think → build error (rule CH: no silent drop). The event side (M3: 2004:17 → set channel k's flag, clear the slot's
/// other channels; −1 → clear the slot's channels; the flag ids per generator by FlagRules.ChrChannel) uses <see cref="Channels"/>.
/// </summary>
public static class EventRequests
{
    static readonly Dictionary<string, Dictionary<int, SortedSet<(int Slot, int V)>>> byMap = [];

    /// <summary>DS1 entity → its (slot, command) requests in <paramref name="ds1Map"/>'s EMEVD ("m10_01" or "m10_01_00_00").</summary>
    public static IReadOnlyDictionary<int, SortedSet<(int Slot, int V)>> ByEntity(string dsrRoot, string ds1Map)
    {
        string map = ds1Map.Length == 6 ? ds1Map + "_00_00" : ds1Map;
        lock (byMap)
        {
            if (byMap.TryGetValue(map, out var done)) return done;
            var res = new Dictionary<int, SortedSet<(int, int)>>();
            string path = Path.Combine(dsrRoot, "event", map + ".emevd.dcx");
            if (File.Exists(path))
            {
                var emevd = EMEVD.Read(path);
                var inits = emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2000 && i.ID == 0 && i.ArgData.Length >= 8).ToList();
                foreach (var ev in emevd.Events)
                    for (int k = 0; k < ev.Instructions.Count; k++)
                    {
                        var ins = ev.Instructions[k];
                        if (ins.Bank != 2004 || ins.ID != 17 || ins.ArgData.Length < 9) continue;
                        int v = BitConverter.ToInt32(ins.ArgData, 4), slot = (sbyte)ins.ArgData[8];
                        if (v == -1) continue;
                        var entities = new List<int>();
                        var par = ev.Parameters.FirstOrDefault(p => p.InstructionIndex == k && p.TargetStartByte == 0);
                        if (par == null) entities.Add(BitConverter.ToInt32(ins.ArgData, 0));
                        else
                            foreach (var init in inits.Where(i => BitConverter.ToUInt32(i.ArgData, 4) == ev.ID))
                                if (8 + par.SourceStartByte + 4 <= init.ArgData.Length) entities.Add(BitConverter.ToInt32(init.ArgData, 8 + (int)par.SourceStartByte));
                        foreach (int e in entities)
                        {
                            if (!res.TryGetValue(e, out var set)) res[e] = set = [];
                            set.Add((slot, v));
                        }
                    }
            }
            byMap[map] = res;
            return res;
        }
    }

    static readonly Dictionary<int, List<(int Slot, int V, int K)>> active = [];
    /// <summary>Thinks <see cref="Register"/> could not give channels (more than 3 requests).</summary>
    public static readonly List<string> Overflow = [];
    static readonly HashSet<string> registered = [];

    /// <summary>Rule E-REQ-P: <see cref="Register"/> ran for the DS1 map (its placed regulars' requests are translated).</summary>
    public static bool IsRegistered(string ds1Map) { lock (active) return registered.Contains(ds1Map[..6]); }

    /// <summary>Makes the AI translators (Ds1Goal / Ds1AiInterpreter) compile GetEventRequest for every think placed in
    /// <paramref name="ds1Map"/> (called by the map's character build before translating its thinks).</summary>
    public static void Register(string dsrRoot, string ds1Map)
    {
        string map = ds1Map.Length == 6 ? ds1Map + "_00_00" : ds1Map;
        var msb = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", map + ".msb"));
        lock (active)
        {
            registered.Add(map[..6]);
            foreach (var g in msb.Parts.Enemies.Where(e => e.EntityID > 0).GroupBy(e => e.ThinkParamID))
            {
                // session 11 (burg11, round 9): a think with more requests than channels (Demon Ruins' Centipede Demon 520000 in
                // the merged m10_00) is left unregistered here; it fails where it is ported (Channels for that think)
                IReadOnlyList<(int Slot, int V, int K)> ch;
                try { ch = Channels(dsrRoot, map, g.Key, g.Select(e => e.EntityID)); }
                catch (InvalidDataException e) { Overflow.Add($"{map}: {e.Message}"); continue; }
                if (ch.Count > 0) active[g.Key] = [.. ch];
            }
        }
    }

    /// <summary>The registered channels of a think (empty: GetEventRequest stays the assumed 0).</summary>
    public static IReadOnlyList<(int Slot, int V, int K)> ForThink(int think) { lock (active) return active.TryGetValue(think, out var l) ? l : []; }

    /// <summary>
    /// Session 11 (fx11), rule E-REQ-P (the event side of E-REQ, for placed regulars; user: "One guy at the top of the stairs near
    /// Firelink … is supposed to throw fireballs"): every placed DS1 character that receives requests (<see cref="ByEntity"/>) and
    /// whose think has channels gets one DS2 flag per channel k: <see cref="Events.FlagRules.AiChannelFlag"/> (its host's reset
    /// block k2, prefix·1e6 + 20100 + n in (generator, k) order, as DS2's own boss logic flags 1BB020080 live in k2), written as
    /// the generator's logicEventFlag(k+1) (<see cref="Maps.EnemyTest.Custom.ChannelFlags"/>; the AI reads it as f5903(k)). The
    /// map's event translator then turns 2004:17(entity, v, slot) into "set the (slot, v) channel's flag, clear the slot's other
    /// channels" (v = −1: clear them all) and resets every channel flag at load (rule T1-B: DS1's AI requests do not outlive a
    /// load). Filled by <see cref="Maps.AsylumEnemies.Build"/> before the map's events are translated.
    /// </summary>
    static readonly Dictionary<(string Map, int Entity), List<(int Slot, int V, int K, int Flag)>> placed = [];

    public static void Place(string ds1Map, int entity, IReadOnlyList<(int Slot, int V, int K, int Flag)> channels)
    {
        lock (placed) placed[(ds1Map[..6], entity)] = [.. channels];
    }

    /// <summary>Rule E-REQ-P: the placed character's channels (slot, command, k, DS2 flag); empty when it has none.</summary>
    public static IReadOnlyList<(int Slot, int V, int K, int Flag)> PlacedChannels(string ds1Map, int entity)
    {
        lock (placed) return placed.TryGetValue((ds1Map[..6], entity), out var l) ? l : [];
    }

    /// <summary>Every channel flag of the map's placed characters (reset at load, rule T1-B).</summary>
    public static IEnumerable<int> PlacedFlags(string ds1Map)
    {
        // session 11 (chrai11), rule FA1: the forced-clip signals and code bits are AI requests too (reset at load)
        lock (placed) return [.. placed.Where(kv => kv.Key.Map == ds1Map[..6]).SelectMany(kv => kv.Value.Select(c => c.Flag))
            .Concat(Boss.EventMessages.Fa1All(ds1Map).SelectMany(e => e.AllFlags)).Distinct().Order()];
    }

    /// <summary>The channels of DS1 think <paramref name="think"/> in <paramref name="ds1Map"/>: (slot, command) → k (f5903(k), 1-3).</summary>
    public static IReadOnlyList<(int Slot, int V, int K)> Channels(string dsrRoot, string ds1Map, int think, IEnumerable<int>? entities = null)
    {
        string map = ds1Map.Length == 6 ? ds1Map + "_00_00" : ds1Map;
        var table = ByEntity(dsrRoot, map);
        IEnumerable<int> ents = entities ?? MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", map + ".msb")).Parts.Enemies.Where(e => e.ThinkParamID == think && e.EntityID > 0).Select(e => e.EntityID);
        var pairs = new SortedSet<(int, int)>();
        foreach (int e in ents) if (table.TryGetValue(e, out var s)) pairs.UnionWith(s);
        if (pairs.Count > 3) throw new InvalidDataException($"rule E-REQ / CH: DS1 think {think} in {map} receives {pairs.Count} AI commands {string.Join(", ", pairs.Select(p => $"slot {p.Item1} = {p.Item2}"))}: more than the 3 logicEventFlag channels (lef2-4)");
        return [.. pairs.Select((p, i) => (p.Item1, p.Item2, i + 1))];
    }
}
