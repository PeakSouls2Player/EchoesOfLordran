using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Event scripts for a converted DS1 map, in its own event ESD and EventParam (our files; vanilla maps untouched):
///  - the per-bonfire event every DS2 bonfire has (Things Betwixt: EventParam 80000, bootType 4, bonfireId 2650, group
///    80000 → subroutine 2147483630 → 2147483632 → 130131(first, last, 0) = set a range of flags), copied from Things
///    Betwixt with the map's own range (AABB*1000 .. +99, e.g. 1801000..1801099) and bonfire id;
///  - with test = true, a test event (EventParam 9000, bootType 1 like TB's ownerless 5010) that opens a message window
///    (130443, mapevent TestMessageId) as soon as the map runs: proof that events in our SoulsFormats-written ESDs run.
/// </summary>
public static class AsylumEvents
{
    public const int TestMessageId = 9901;
    static readonly int TestGroup = int.TryParse(Environment.GetEnvironmentVariable("LR_TEST_GROUP"), out int g) ? g : 9000;

    /// <param name="generators">DS1 character entity → placed generator (AsylumEnemies.Build; session 8: build the enemies
    /// first so the translated DS1 events address them), null = none placed.</param>
    /// <param name="sounds">DS1 sound entity → DS2 sound id of the MSB2 Sound regions the map builder placed (session 8 fix
    /// pass: Events.EntityRules.SoundId, for the atmos track's DS1 sound regions), null = none: DS1's BGM toggles (2010:3)
    /// are no-ops.</param>
    /// <returns>The DS1 map events the build translated (session 8, track npc: the talk builder's gift-chain rule needs
    /// to know which DS1 events run); empty without a script.</returns>
    public static HashSet<long> Build(Pack pack, MapBuilder.Options map, bool test, MapBuilder.Options? crowTo = null, Ds1Source? script = null,
        IReadOnlyDictionary<int, int>? generators = null, IReadOnlyDictionary<int, int>? sounds = null)
    {
        string tb = "m10_02_00_00";
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        var tbEsd = ESD.Read(Path.Combine(pack.Ds2Data, "ezstate", $"event_{tb}.esd"));
        var esd = ESD.Read(pack.GetFile("gamedata", $"ezstate/event_{map.Ds2Map}.esd"));
        var events = PARAM.Read(Path.Combine(pack.Ds2Data, "param", $"eventparam_{tb}.param"));
        if (!events.ApplyParamdefCarefully(defs)) throw new InvalidDataException("no paramdef fits EventParam");
        var tbRows = events.Rows.ToDictionary(r => r.ID);
        bool tbBase = Environment.GetEnvironmentVariable("LR_EVENT_BASE") == "tb"; // bisect experiment: keep all of TB's events
        if (tbBase) { esd = tbEsd; tbEsd = ESD.Read(Path.Combine(pack.Ds2Data, "ezstate", $"event_{tb}.esd")); events.Rows.RemoveAll(r => r.ID == 80000 || r.ID == TestGroup); }
        else { events.Rows.Clear(); esd.StateGroups.Remove(0x7FFFFFFF); } // MinimalEsd's placeholder: not needed once real groups exist

        // ---- the bonfire's event
        int aabb = int.Parse(map.Ds2Map[1..3] + map.Ds2Map[4..6]);
        int first = aabb * 1000, last = first + 99;
        foreach (long id in new long[] { 2147483630, 2147483631, 2147483632, 2147483633 })
            esd.StateGroups[id] = tbEsd.StateGroups[id];
        var bonfireGroup = tbEsd.StateGroups[80000];
        var call = bonfireGroup.Values.SelectMany(s => s.EntryCommands).Single(c => c.CommandBank == 6 && c.CommandID == 2147483630);
        call.Arguments = [Int(first), Int(last)];
        esd.StateGroups[80000] = bonfireGroup;
        var bonfireRow = new PARAM.Row(tbRows[80000]) { ID = 80000 };
        bonfireRow["bonfireId"].Value = Convert.ChangeType(map.BonfireId, bonfireRow["bonfireId"].Value.GetType());
        events.Rows.Add(bonfireRow);
        // session 11 (travel12), rule BF1-A: the map's further bonfires get the same event as MapScript's rule BF1 maps
        // (group 80000 + 1000·i, bootType 4, bonfireId, flag range AABB·1000 + 100·i .. + 99; FoFG's pattern)
        foreach (var bp in MapBuilder.Bonfires(Program.DefaultDsrRoot, map).Where(b => b.Index > 0))
        {
            if (esd.StateGroups.ContainsKey(bp.Group) || events.Rows.Any(r => r.ID == bp.Group))
                throw new InvalidDataException($"rule BF1-A: event group {bp.Group} of bonfire {bp.Id} is already used in {map.Ds2Map}");
            var fresh = ESD.Read(Path.Combine(pack.Ds2Data, "ezstate", $"event_{tb}.esd")).StateGroups[80000]; // own state objects
            var c2 = fresh.Values.SelectMany(s => s.EntryCommands).Single(c => c.CommandBank == 6 && c.CommandID == 2147483630);
            int f2 = first + 100 * bp.Index;
            c2.Arguments = [Int(f2), Int(f2 + 99)];
            esd.StateGroups[bp.Group] = fresh;
            var r2 = new PARAM.Row(tbRows[80000]) { ID = bp.Group };
            r2["bonfireId"].Value = Convert.ChangeType(bp.Id, r2["bonfireId"].Value.GetType());
            if (r2.Cells.FirstOrDefault(c => c.Def.InternalName == "stateMapId") is { } sm) sm.Value = Convert.ChangeType(bp.Group, sm.Value.GetType());
            events.Rows.Add(r2);
            pack.Note($"{map.Ds2Map} events: rule BF1-A: bonfire {bp.Id} \"{bp.Name}\" (DS1 {bp.Entity}) → group {bp.Group}, flag range {f2}..{f2 + 99}");
        }

        // ---- test: a message window as soon as the map's events run
        if (test)
        {
            // State 0 never has entry commands in DS2's ESDs (the machine starts in it without running them): go to 1.
            var s0 = new ESD.State();
            s0.Conditions.Add(new ESD.Condition(1, Convert.FromHexString("41A1")));
            var s1 = new ESD.State();
            s1.EntryCommands.Add(new ESD.CommandCall(1, 130443, Int(TestMessageId), Int(3), Int(15), Int(220), Int(2), Int(0), Int(0)));
            s1.Conditions.Add(new ESD.Condition(2, Convert.FromHexString("41A1")));
            esd.StateGroups[TestGroup] = new Dictionary<long, ESD.State> { [0] = s0, [1] = s1, [2] = new ESD.State() };
            var testRow = new PARAM.Row(tbRows[5010]) { ID = TestGroup };
            testRow["stateMapId"].Value = Convert.ChangeType(TestGroup, testRow["stateMapId"].Value.GetType());
            events.Rows.Add(testRow);
            pack.SetText("mapevent", TestMessageId, "[Lordran test] The Asylum's event script runs.");
        }

        // ---- the map's DS1 event script (session 7: the Asylum's doors, gate, fogs, boulder; session 8: translated by rule)
        var translated = new HashSet<long>();
        if (script != null)
        {
            AsylumScript.Params(pack, script);
            translated = AsylumScript.Events(pack, script, esd, events, tbRows[5010], generators, crowTo != null, map.HitGroups, sounds);
        }

        // ---- the crow: the cliff edge warps to Firelink Shrine (MapLinks; region added by MapLinks.Build). Session 8: only
        // when DS1's own cliff event (11810000: cutscene + warp to Firelink's start point) didn't translate. Fix pass: the
        // translator's route rule keeps 11810000 back unless DS1's nest (11810150: "Curl up like a ball" → warp to
        // Firelink's ledge, the way back after the cliff's flag is set) translates too, so this group stays whenever the
        // nest can't be offered
        if (crowTo != null && !translated.Contains(11810000))
        {
            esd.StateGroups[MapLinks.CrowGroup] = MapLinks.CrowGroupStates(crowTo.MapIdDecimal);
            var crowRow = new PARAM.Row(tbRows[5010]) { ID = MapLinks.CrowGroup };
            crowRow["stateMapId"].Value = Convert.ChangeType(MapLinks.CrowGroup, crowRow["stateMapId"].Value.GetType());
            events.Rows.Add(crowRow);
        }
        else if (crowTo != null) pack.Note($"{map.Ds2Map}: the crow group {MapLinks.CrowGroup} is left out: DS1's 11810000 translated (warp 132125 to {crowTo.Ds2Map})");

        foreach (var (id, states) in esd.StateGroups)
            if (states.TryGetValue(0, out var st0) && st0.EntryCommands.Count > 0)
                throw new InvalidDataException($"ESD group {id}: state 0 has entry commands; DS2 never runs them (start in state 0, go to 1)");
        foreach (var (id, states) in esd.StateGroups)
            if (states.Count < 2) throw new InvalidDataException($"ESD group {id} has {states.Count} state(s); DS2 needs at least 2 (see MapBuilder.MinimalEsd)");
        if (Environment.GetEnvironmentVariable("LR_ESD_NAME") is { Length: > 0 } nm) esd.Name = nm; // bisect experiment
        events.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
        pack.AddFile("gamedata", $"ezstate/event_{map.Ds2Map}.esd", esd.Write());
        pack.AddFile("gamedata", $"param/eventparam_{map.Ds2Map}.param", events.Write());
        pack.Note($"{map.Ds2Map} events: bonfire {map.BonfireId} flag range {first}..{last} (EventParam 80000){(test ? $", TEST message {TestMessageId} (EventParam 9000)" : "")}");
        return translated;
    }

    static byte[] Int(int v) => v is >= 0 and < 64 ? [(byte)(0x40 + v), 0xA1] : [0x82, .. BitConverter.GetBytes(v), 0xA1];
}
