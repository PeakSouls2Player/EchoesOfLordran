using SoulsFormats;

namespace Lordran.Tools.Build.Events;

/// <summary>
/// Fix pass e2 (review finding 5), rule K6 key source: a DS1 ObjAct goods requirement (a key) becomes a DS2 lock only when
/// the build gives the player a way to get that item; otherwise the door opens with its prompt (no lock-in: the same
/// policy as rule G4's pending ladders and the boss rule) and the report says so. Like G4, the lock turns on by itself
/// once the source is in the build (the Asylum's far door: the Big Pilgrim's Key comes from the Asylum Demon's drop, DS1
/// lot 2660 (chrint track) or Oscar's gift lot 1081 / 6021 (asy-items: DS1 pre-sets their flag 50000081 in a map event
/// this build doesn't translate); neither is in this sandbox).
///
/// The check runs once the whole pack is built (<see cref="Apply"/>, called by build-pack after the maps, their events,
/// characters and talk; before the TEST shop, which never counts). A source is an ItemLotParam2_Other / _Chr row that
/// holds the item AND is referenced by the pack: a map pickup (mapobjectinstanceparam_*.param itemLotId), an ESD
/// lot award 131402(lot) (talk gifts; event awards from DS1 2003:4 / 2003:36), or an EnemyParam's
/// drop lot (itemLotId; every EnemyParam row counts, so for a DS2-native item a vanilla enemy's drop would count too:
/// such a lock stays, i.e. the conservative side).
/// K2 locks (131602 with an item, the Door route) are only reported: their keys are all sourced in the Asylum (cell key
/// pickup 18016000, F2 East Oscar's gift 1100001080, F2 West Firelink pickup 19026130), and a DS2 door lock without an
/// item has no verified form yet.
/// </summary>
public static class KeySources
{
    /// <summary>A K6 group with a goods requirement: its ESD, group id, DS2 item(s) (any one opens it), and the same group
    /// without the requirement (the helper's prompt passes at once).</summary>
    public sealed record Pending(string Esd, long Group, int[] Items, Dictionary<long, ESD.State> Unlocked, string What);

    public static readonly List<Pending> K6 = [];

    /// <summary>TEST ONLY (build-pack --test-key-locks): keep every K6 lock even when its key has no source (the locked
    /// path's in-game tests use givelot).</summary>
    public static bool KeepAllLocks { get; set; }

    /// <summary>DS2's item-lot award (131402(lot, 1): talk gifts, TalkTranslator.EmitGift; DS1 2003:4 / 2003:36 in the
    /// event translator). Only this command counts: a pickup's instance id equals its lot id (18016000), so any-argument
    /// matching would take 130702(instance, …) for a source.</summary>
    const int AwardLot = 131402;

    static int A(byte[] b) { try { return (int)EventSim.Evaluate(b, (_, _) => 0); } catch { return int.MinValue; } }

    /// <summary>DS2 item → where the pack gives it (empty = no source).</summary>
    public static Dictionary<int, List<string>> Sources(Pack pack, IEnumerable<int> items)
    {
        var want = items.ToHashSet();
        var result = want.ToDictionary(i => i, _ => new List<string>());
        Dictionary<int, int[]> Lots(string name) => !pack.HasParam(name) ? [] : pack.Param(name).Rows
            .Select(r => (r.ID, Items: Enumerable.Range(1, 10).Select(k => Convert.ToInt32(r[$"itemId{k}"].Value)).Where(want.Contains).ToArray()))
            .Where(x => x.Items.Length > 0).ToDictionary(x => x.ID, x => x.Items);
        var other = Lots("ItemLotParam2_Other");
        var chr = Lots("ItemLotParam2_Chr");
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        foreach (var key in pack.FileKeys)
        {
            string name = Path.GetFileName(key);
            if (key.StartsWith("gamedata/param/", StringComparison.OrdinalIgnoreCase) && name.StartsWith("mapobjectinstanceparam_", StringComparison.OrdinalIgnoreCase))
            {
                var p = PARAM.Read(pack.GetFile("gamedata", key["gamedata/".Length..]));
                if (!p.ApplyParamdefCarefully(defs)) continue;
                foreach (var r in p.Rows)
                    if (other.TryGetValue(Convert.ToInt32(r["itemLotId"].Value), out var its))
                        foreach (int i in its) result[i].Add($"pickup {name} instance {r.ID} lot {r["itemLotId"].Value}");
            }
            else if (key.StartsWith("gamedata/ezstate/", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".esd", StringComparison.OrdinalIgnoreCase))
            {
                var esd = ESD.Read(pack.GetFile("gamedata", key["gamedata/".Length..]));
                foreach (var (g, states) in esd.StateGroups)
                    foreach (var c in states.Values.SelectMany(s => s.EntryCommands.Concat(s.ExitCommands)))
                        if (c.CommandID == AwardLot && c.Arguments.Count > 0 && other.TryGetValue(A(c.Arguments[0]), out var its))
                            foreach (int i in its) result[i].Add($"{name} group {g} {c.CommandID}(lot {A(c.Arguments[0])})");
            }
        }
        // session 11 (burg11): a shop row selling it (ShopLineupParam saleItem; build-pack applies this before the TEST shop:
        // only shops the build's talk translation and vanilla DS2 have; the Burg's Residence Key is the male merchant's)
        if (pack.HasParam("ShopLineupParam"))
            foreach (var r in pack.Param("ShopLineupParam").Rows)
                if (want.Contains(Convert.ToInt32(r["saleItem"].Value))) result[Convert.ToInt32(r["saleItem"].Value)].Add($"ShopLineupParam {r.ID}");
        if (chr.Count > 0 && pack.HasParam("EnemyParam"))
            foreach (var r in pack.Param("EnemyParam").Rows)
                if (chr.TryGetValue(Convert.ToInt32(r["itemLotId"].Value), out var its))
                    foreach (int i in its) result[i].Add($"EnemyParam {r.ID} drop lot {r["itemLotId"].Value}");
        foreach (var l in result.Values) { var d = l.Distinct().ToList(); l.Clear(); l.AddRange(d); }
        return result;
    }

    /// <summary>Applies rule K6 key source to the registered groups (see the class summary); returns the report lines.</summary>
    public static List<string> Apply(Pack pack)
    {
        var log = new List<string>();
        var locks = new List<(string Esd, long Group, int Item)>();
        foreach (var key in pack.FileKeys.Where(k => k.StartsWith("gamedata/ezstate/event_", StringComparison.OrdinalIgnoreCase)))
        {
            var esd = ESD.Read(pack.GetFile("gamedata", key["gamedata/".Length..]));
            foreach (var (g, states) in esd.StateGroups)
                foreach (var c in states.Values.SelectMany(s => s.EntryCommands))
                    if (c.CommandID == 131602 && c.Arguments.Count > 1 && A(c.Arguments[1]) is var it && it > 0) locks.Add((Path.GetFileName(key), g, it));
        }
        var src = Sources(pack, K6.SelectMany(p => p.Items).Concat(locks.Select(l => l.Item)));
        foreach (var l in locks)
            log.Add($"rule K2 key source: {l.Esd} lock group {l.Group} item {l.Item}: {(src[l.Item].Count > 0 ? string.Join("; ", src[l.Item]) : "WARN no source in this build (lock kept: K2 has no unlocked form yet)")}");
        foreach (var g in K6.GroupBy(p => p.Esd))
        {
            if (pack.TryGetFile("gamedata", g.Key) is not { } bytes) { log.Add($"rule K6 key source: {g.Key} not in the pack"); continue; }
            var esd = ESD.Read(bytes);
            bool changed = false;
            foreach (var p in g)
            {
                var s = p.Items.SelectMany(i => src[i]).ToList();
                if (s.Count > 0) { log.Add($"rule K6 key source: {p.What} (group {p.Group}, item {string.Join("/", p.Items)}): lock kept, source {string.Join("; ", s)}"); continue; }
                if (KeepAllLocks) { log.Add($"TEST --test-key-locks: {p.What} (group {p.Group}, item {string.Join("/", p.Items)}): no source in this build, lock KEPT (test only: the key comes from givelot)"); continue; }
                esd.StateGroups[p.Group] = p.Unlocked;
                // rule K7's "Used <item>" for that item would now show for a key the player doesn't hold
                int removed = esd.StateGroups.Values.SelectMany(st => st.Values).Sum(st => st.EntryCommands.RemoveAll(c => c.CommandID == 130443 && c.Arguments.Count > 5
                    && A(c.Arguments[4]) == 2 && p.Items.Contains(A(c.Arguments[5]))));
                changed = true;
                log.Add($"NOTE rule K6 key source: {p.What} (group {p.Group}): no source for item {string.Join("/", p.Items)} in this build → unlocked (the prompt opens it; no lock-in), {removed} K7 \"Used <item>\" dialog(s) dropped; the lock returns by itself once the key's source is built");
            }
            if (changed) pack.AddFile("gamedata", g.Key, esd.Write());
        }
        return log;
    }
}
