using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Session 11 (burg11, BURG-PLAN M1 "minimal m19_41 script", the first cut of MapScriptWriter, R2): the event ESD and
/// EventParam of a ported DS1 map whose DS1 events are not translated yet, written by producers instead of a per-map class.
/// Producer 1, rule BF1: every DS2 bonfire of the map gets DS2's per-bonfire event (Things Betwixt's group 80000 →
/// subroutine 2147483630(first, last) = 130131 over EventFlagListParam rows in [first, last]), as FoFG has one per bonfire
/// (EventParam 80000 / 81000 / 83000, bootType 4, bonfireId, flag range map·1000 + 100·i .. + 99, V-data:
/// event_m10_10_00_00.esd). The seam needs no event (category-23 EventLocation rows and ConnectCollision parts: MapLinks).
/// </summary>
public static class MapScript
{
    const string Tb = "m10_02_00_00";
    /// <summary>Test warp in front of bonfire i: BonfireSpot + 10·i (session 11 burg11 range 3700400-3700499).</summary>
    public const int BonfireSpot = 3700400;

    public static void Build(Pack pack, MapBuilder.Options map, IReadOnlyList<MapBuilder.BonfirePlan> bonfires)
    {
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        var esd = ESD.Read(pack.GetFile("gamedata", $"ezstate/event_{map.Ds2Map}.esd"));
        var events = PARAM.Read(Path.Combine(pack.Ds2Data, "param", $"eventparam_{Tb}.param"));
        if (!events.ApplyParamdefCarefully(defs)) throw new InvalidDataException("no paramdef fits EventParam");
        var tbRow = events[80000] ?? throw new InvalidDataException($"EventParam_{Tb} has no row 80000");
        events.Rows.Clear();
        esd.StateGroups.Remove(0x7FFFFFFF); // MapBuilder.MinimalEsd's placeholder: not needed once real groups exist
        int aabb = MapHosts.Aabb(map.Ds2Map);
        var subroutines = ESD.Read(Path.Combine(pack.Ds2Data, "ezstate", $"event_{Tb}.esd"));
        foreach (long id in new long[] { 2147483630, 2147483631, 2147483632, 2147483633 }) esd.StateGroups[id] = subroutines.StateGroups[id];
        var lines = new List<string>();
        foreach (var b in bonfires)
        {
            var tb = ESD.Read(Path.Combine(pack.Ds2Data, "ezstate", $"event_{Tb}.esd")); // fresh state objects per group
            var group = tb.StateGroups[80000];
            var call = group.Values.SelectMany(s => s.EntryCommands).Single(c => c.CommandBank == 6 && c.CommandID == 2147483630);
            int first = aabb * 1000 + 100 * b.Index, last = first + 99;
            call.Arguments = [Int(first), Int(last)];
            esd.StateGroups[b.Group] = group;
            var row = new PARAM.Row(tbRow) { ID = b.Group };
            row["bonfireId"].Value = Convert.ChangeType(b.Id, row["bonfireId"].Value.GetType());
            if (row.Cells.FirstOrDefault(c => c.Def.InternalName == "stateMapId") is { } sm) sm.Value = Convert.ChangeType(b.Group, sm.Value.GetType());
            events.Rows.Add(row);
            lines.Add($"bonfire {b.Id} → group {b.Group} (flag range {first}..{last})");
        }
        foreach (var (id, states) in esd.StateGroups)
        {
            if (states.TryGetValue(0, out var st0) && st0.EntryCommands.Count > 0) throw new InvalidDataException($"ESD group {id}: state 0 has entry commands; DS2 never runs them");
            if (states.Count < 2) throw new InvalidDataException($"ESD group {id} has {states.Count} state(s); DS2 needs at least 2");
        }
        // test warps (rule P4; ids 3700400 + 10·i = this track's range): 1.5 m in front of each bonfire, facing it
        var locs = MapLinks.Locations(pack, map, defs);
        var point = MapLinks.Template(pack, defs, "m20_10_00_00", 3700000);
        var probe = FloorProbe.FromPack(pack, map.Ds2Map);
        foreach (var b in bonfires)
        {
            int id = BonfireSpot + 10 * b.Index;
            if (Ds1Placement.FrontSpot(probe, b.Position, b.Rotation.Y, 1.5f) is { } s) MapLinks.Put(locs, point, id, s.Pos, new System.Numerics.Vector3(0, s.Yaw, 0));
            else if (Ds1Placement.TestSpot(probe, b.Position + new System.Numerics.Vector3(0, 0, 1.5f)) is { } t) MapLinks.Put(locs, point, id, t, System.Numerics.Vector3.Zero);
            else { lines.Add($"no test spot near bonfire {b.Id}"); continue; }
            lines.Add($"test warp {id} at bonfire {b.Id}");
        }
        pack.AddFile("gamedata", $"param/eventlocation_{map.Ds2Map}.param", locs.Write());
        events.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
        pack.AddFile("gamedata", $"ezstate/event_{map.Ds2Map}.esd", esd.Write());
        pack.AddFile("gamedata", $"param/eventparam_{map.Ds2Map}.param", events.Write());
        pack.Note($"{map.Ds2Map} script (MapScript, rule BF1 producer): {string.Join("; ", lines)}");
    }

    /// <summary>Test warps 2 m in front of the map's placed NPCs (rule P4, MapLinks.NpcSpot's rule): 3700430 + k.</summary>
    public const int NpcSpot = 3700430;
    public static void NpcSpots(Pack pack, MapBuilder.Options map, IReadOnlyList<Chr.NpcPorter.Placed> npcs)
    {
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        var locs = MapLinks.Locations(pack, map, defs);
        var point = MapLinks.Template(pack, defs, "m20_10_00_00", 3700000);
        var probe = FloorProbe.FromPack(pack, map.Ds2Map);
        var msb1 = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", map.Ds1Map + ".msb"));
        for (int k = 0; k < npcs.Count && k < 10; k++)
        {
            var e = msb1.Parts.Enemies.First(x => x.EntityID == npcs[k].Entity);
            if (Ds1Placement.FrontSpot(probe, e.Position, e.Rotation.Y) is not { } s) { pack.Note($"test warp {NpcSpot + k}: no walkable spot in front of {npcs[k].Name}"); continue; }
            MapLinks.Put(locs, point, NpcSpot + k, s.Pos, new System.Numerics.Vector3(0, s.Yaw, 0));
            pack.Note($"test warp {NpcSpot + k} in {map.Ds2Map}: 2 m in front of {npcs[k].Name} at {s.Pos:0.00}, yaw {s.Yaw:0.#}");
        }
        pack.AddFile("gamedata", $"param/eventlocation_{map.Ds2Map}.param", locs.Write());
    }

    /// <summary>Session 11 (chrai11), rule FA1: the encoder groups' first id (37460+ = N-H4's hostility groups).</summary>
    public const int Fa1EncoderGroup = 37480;

    /// <summary>The encoder's signals in priority order: (code, flag, what): the forced clips (by code), the AI-off state, the E-REQ pairs.</summary>
    static List<(int Code, int Flag, string What)> Fa1Signals(Ai.Boss.EventMessages.Fa1Entity f) =>
        [.. f.ClipFlags.OrderBy(kv => f.Layout.ClipCode[kv.Key]).Select(kv => (f.Layout.ClipCode[kv.Key], kv.Value, $"a00_{kv.Key:D4}")),
         .. (f.AiOffFlag != 0 ? [(f.Layout.AiOffCode, f.AiOffFlag, "AI off")] : Array.Empty<(int, int, string)>()),
         .. f.PairFlags.OrderBy(kv => f.Layout.PairCode[kv.Key]).Select(kv => (f.Layout.PairCode[kv.Key], kv.Value, $"E-REQ slot {kv.Key.Slot} = {kv.Key.V}"))];

    static string Fa1Describe(Ai.Boss.EventMessages.Fa1Entity f) =>
        string.Join(", ", Fa1Signals(f).Select(s => $"{s.What} flag {s.Flag} → {s.Code}")) + $"; bits {string.Join("/", f.BitFlags)} (logicEventFlag{f.BitBase + 1}-4); clips acknowledged on board {Ai.Boss.EventMessages.AckSlot}";

    /// <summary>
    /// Rule FA1: the request encoder of one part. The event side raises one flag per signal (a forced clip, the AI-off state, an E-REQ
    /// pair); the AI reads one number J from the generator's logicEventFlag bits. The highest-priority raised signal is J (forced clips
    /// before AI off before E-REQ, 0 = none): state S_V writes V's bits and leaves for the signal that should be on top now; in a clip
    /// state the AI's acknowledgement (its board slot 6 = the clip's code, 140171(0, generator, 6, code, 0)) clears the clip's request
    /// (DS1's forced animation is one-shot; the event that waits for it waits the clip's DS1 length).
    /// </summary>
    static Dictionary<long, ESD.State> Fa1Encoder(Ai.Boss.EventMessages.Fa1Entity f)
    {
        var sig = Fa1Signals(f);
        static byte[] On(int flag) => FlagOn(flag)[..^1];
        static byte[] Off(int flag) => [0x46, .. Push(flag), 0x85, 0xA6, .. Push(0), 0x95];
        static byte[] And(byte[] a, byte[] b) => [.. a, .. b, 0x98];
        long Sv(int code) => 10 + code;
        var states = new Dictionary<long, ESD.State> { [0] = S([], (True, 1)) };
        // E: first match = priority
        states[1] = S([], [.. sig.Select(s => ((byte[])[.. On(s.Flag), 0xA1], Sv(s.Code))), (True, Sv(0))]);
        foreach (int v in sig.Select(s => s.Code).Append(0))
        {
            var entry = new List<ESD.CommandCall>();
            for (int b = 0; b < f.BitFlags.Length; b++) entry.Add(C(130101, f.BitFlags[b], (v >> b) & 1));
            var st = S(entry);
            var me = sig.FirstOrDefault(s => s.Code == v);
            bool clip = f.Layout.IsClipCode(v) && me.Flag != 0;
            if (clip)
            {
                // registered with its complement (slot 1: not equal), so one slot always holds and the state steps every frame (DS2 steps a
                // state with registrations only when one holds: rule G1's pattern, vanilla m10_14 2147483453)
                entry.Add(C(140171, 0, f.Generator, Ai.Boss.EventMessages.AckSlot, v, 0));
                entry.Add(C(140171, 1, f.Generator, Ai.Boss.EventMessages.AckSlot, v, 1));
                st = S(entry);
                var ack = new ESD.Condition(1, Group(0));
                ack.PassCommands.Add(C(130101, me.Flag, 0));
                st.Conditions.Add(ack);
            }
            bool past = false;
            foreach (var s in sig)
            {
                if (s.Code == v) { past = true; continue; }
                byte[] test = past && me.Flag != 0 ? And(Off(me.Flag), On(s.Flag)) : On(s.Flag);
                st.Conditions.Add(new ESD.Condition(Sv(s.Code), [.. test, 0xA1]));
            }
            if (v != 0 && me.Flag != 0) st.Conditions.Add(new ESD.Condition(Sv(0), [.. Off(me.Flag), 0xA1]));
            // rule G2: DS2 replays a registering state's first exit when it is re-entered in the same run, so a clip state is left
            // through a re-execute (130002, state 2): the next run starts at state 1's priority ladder (one frame later)
            if (clip) foreach (var c in st.Conditions) c.TargetState = 2;
            states[Sv(v)] = st;
        }
        states[2] = S([C(130002)]);
        return states;
    }

    static byte[] Int(int v) => v is >= -64 and < 64 ? [(byte)(0x40 + v), 0xA1] : [0x82, .. BitConverter.GetBytes(v), 0xA1];
    static byte[] Push(int v) => v is >= -64 and < 64 ? [(byte)(0x40 + v)] : [0x82, .. BitConverter.GetBytes(v)];
    static byte[] Group(int n) => [0x82, .. BitConverter.GetBytes(140001), .. Push(n), 0x85, 0xA6, 0xA1];
    static byte[] FlagOn(int flag) => [0x46, .. Push(flag), 0x85, 0xA6, .. Push(0), 0x96, 0xA1];
    static readonly byte[] True = Convert.FromHexString("41A1");
    static ESD.CommandCall C(int id, params int[] args) => new(1, id, args.Select(Int).ToArray());
    static ESD.State S(IEnumerable<ESD.CommandCall> entry, params (byte[] If, long Then)[] conds)
    {
        var s = new ESD.State();
        s.EntryCommands.AddRange(entry);
        foreach (var (cond, then) in conds) s.Conditions.Add(new ESD.Condition(then, cond));
        return s;
    }

    /// <summary>
    /// Session 11 (burg11, BURG-PLAN M2 "burg-events" first cut): a MapObjects map's DS1 script, translated by the existing
    /// rules (EmevdTranslator + <see cref="Events.Ds1MapContext"/>), after the bonfire groups (<see cref="Build"/>): the
    /// helpers' key guides (K6 prompt, area and object buttons: DS2's sphere 1110 with DS1's distance, text by the
    /// action-button rule), DS1's regions as EventLocation rows (ID2), DS1's events (groups 38000+), the K6 ObjAct groups
    /// (37330+: prompt → requirement test → the prefab's open state + DS1's ObjAct flags, as the Asylum's verified hall /
    /// far door), the covenant housekeeping group (cov11 K2). Returns the DS1 events that translated.
    /// </summary>
    public static HashSet<long> TranslateEvents(Pack pack, Ds1Source ds1, MapBuilder.Options map, MapObjects plan, IReadOnlyDictionary<int, int>? generators)
    {
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        var msb1 = MSB1.Read(Path.Combine(ds1.Root, "map", "MapStudio", map.Ds1Map + ".msb"));
        var emevd = EMEVD.Read(Path.Combine(ds1.Root, "event", map.Ds1Map + ".emevd.dcx"));
        var lines = new List<string>();

        // ---- key guides (KeyGuideParam rows + the invisible helper models, Majula's obelisk helper o04_9300)
        void Guide(int keyGuide, string model, int msg, float radius, string what)
        {
            if (pack.Param("KeyGuideParam")[keyGuide] != null) return;
            var (textId, text) = AsylumScript.KeyGuideText(pack, ds1, msg);
            var row = pack.AddRow("KeyGuideParam", pack.Param("KeyGuideParam")[1110]!, keyGuide, $"[Lordran] {map.Ds2Map} {what} {msg}: {text}");
            row["textId"].Value = textId;
            row["width_radius"].Value = radius;
            ObjPorter.CloneDs2(pack, "o04_9300", model, 10049040, (uint)keyGuide);
            lines.Add($"{what} {msg} \"{text}\" → helper {model}, KeyGuideParam {keyGuide} (sphere r {radius:0.##} m)");
        }
        foreach (var g in plan.Acts.GroupBy(a => a.KeyGuide)) Guide(g.Key, g.First().HelperModel, g.First().Msg, g.Max(a => a.Dist), "ObjAct prompt (K6)");
        foreach (var g in plan.Buttons.GroupBy(a => a.KeyGuide)) Guide(g.Key, g.First().HelperModel, g.First().Msg, g.Max(a => a.Dist), "object action button (K1e)");
        foreach (var g in plan.Areas.GroupBy(b => b.KeyGuide))
        {
            float radius = g.Max(b => msb1.Regions.GetEntries().First(r => r.EntityID == b.Area).Shape is MSB.Shape.Box box
                ? MathF.Sqrt(box.Width * box.Width + box.Depth * box.Depth) / 2 + 1f : 2f);
            Guide(g.Key, g.First().Model, g.First().Message, radius, "area action button");
        }

        // ---- rule WE4 (AsylumScript.Params' rule, generic): DS1's damaging objects' SystemDamageParam / DamageCtrlParam rows
        // (id = rule E-ID2 Ds1AttackParams.ObjectDamageRow(entity) since session 12, was the instance, cloned from FoFG's rolling iron ball 210100030 / 200200020): DS1's hit dummy and radius, object
        // attack class from DS1's atkObj, damage through the chr track's calibration (Ds1AttackParams.DamageFromAtk)
        foreach (var (e, row) in plan.Damaging)
        {
            if (Events.WorldEvents.Damaging(ds1.Root, map.Ds1Map, e) is not { } d || pack.Param("SystemDamageParam")[row] != null) continue;
            var sd = pack.AddRow("SystemDamageParam", pack.Param("SystemDamageParam")[210100030]!, row, $"[Lordran] {map.Ds2Map} DS1 damaging object {e}");
            sd["hitDummyPolyId"].Value = Convert.ChangeType(d.Dummy, sd["hitDummyPolyId"].Value.GetType());
            sd["radius"].Value = d.Radius;
            sd["damageCategory"].Value = Convert.ChangeType(row, sd["damageCategory"].Value.GetType());
            var dc = pack.AddRow("DamageCtrlParam", pack.Param("DamageCtrlParam")[200200020]!, row, $"[Lordran] {map.Ds2Map} DS1 damaging object {e}");
            dc["objAttackAttr"].Value = Convert.ChangeType(d.AtkObj >= 100 ? 100 : 40, dc["objAttackAttr"].Value.GetType());
            dc["objDamage"].Value = Convert.ChangeType(d.AtkObj, dc["objDamage"].Value.GetType());
            var notes = new List<string>();
            if (ds1.Params.Params["AtkParam_Npc"][d.AtkId] is { } atk) Chr.Ds1AttackParams.DamageFromAtk(atk, sd, notes, $"WE4 object {e}");
            else notes.Add($"WE4 object {e}: DS1 AtkParam_Npc {d.AtkId} missing, FoFG's damage kept");
            lines.Add($"rule WE4: DS1 2005:9 on {e} (dummy {d.Dummy}, radius {d.Radius}, AtkParam_Npc {d.AtkId}) -> SystemDamageParam/DamageCtrlParam {row}; {string.Join("; ", notes)}");
        }

        // ---- rule FW1: the white doors' MapObjectWhiteDoorParam rows (vanilla 100: both sides, 401 / 411, no pass flag)
        foreach (var (wall, inst, model) in plan.WhiteDoors)
        {
            if (pack.Param("MapObjectWhiteDoorParam")[inst] != null) continue;
            // session 13 (seam13), rule FW1-B (BossFogs): a boss's fog = DS2's boss fog (vanilla 101000: category 1, "Enter the mist"
            // from the front only, so the arena is sealed while the fight runs; gone once the boss's killed flag is set)
            if (plan.BossFogWalls.TryGetValue(wall, out var fog) && (Events.FlagRules.Map(map.Ds1Map[..6], fog.Ds1KilledFlag) ?? Events.FlagRules.OtherMapPersistent(map.Ds1Map[..6], fog.Ds1KilledFlag)) is int killedDs2)
            {
                // session 12 (fog12), rule FW1-X: a boss fog DS1 never lets the player cross (no 3:18 / 3:19 on it: the arena's far
                // wall, e.g. the Taurus bridge's tower door 1011892, the Gargoyle roof's 1011992) = DS2's boss-room exit fog
                // (vanilla FoFG 101011: category 2, no key guide either side, exit effect 285, gone once the killed flag is set).
                // seam13 gave it 101000's "Enter the mist" and turned it by the boss's spawn spot, so 1011892 offered the prompt
                // from the bridge INTO a sealed boss fog while the Taurus Demon lived (the user's frozen traversal, 10/19)
                if (fog.Exit)
                {
                    var xw = pack.AddRow("MapObjectWhiteDoorParam", pack.Param("MapObjectWhiteDoorParam")[101011]!, inst, $"[Lordran] {map.Ds2Map} DS1 boss exit fog {wall} (boss {fog.Boss})");
                    xw["frontKeyGuideParamId"].Value = Convert.ChangeType(0, xw["frontKeyGuideParamId"].Value.GetType());
                    xw["backKeyGuideParamId"].Value = Convert.ChangeType(0, xw["backKeyGuideParamId"].Value.GetType());
                    xw["passFlagId"].Value = Convert.ChangeType(killedDs2, xw["passFlagId"].Value.GetType());
                    lines.Add($"FW1-X boss exit fog {inst} ({model}) for DS1 light wall {wall}: boss {fog.Boss}, category 2, no key guide (DS1 never crosses it), pass flag {killedDs2} (DS1 {fog.Ds1KilledFlag})");
                    continue;
                }
                var bw = pack.AddRow("MapObjectWhiteDoorParam", pack.Param("MapObjectWhiteDoorParam")[101000]!, inst, $"[Lordran] {map.Ds2Map} DS1 boss fog {wall} (boss {fog.Boss})");
                bw["frontKeyGuideParamId"].Value = Convert.ChangeType(401, bw["frontKeyGuideParamId"].Value.GetType());
                bw["backKeyGuideParamId"].Value = Convert.ChangeType(0, bw["backKeyGuideParamId"].Value.GetType());
                bw["passFlagId"].Value = Convert.ChangeType(killedDs2, bw["passFlagId"].Value.GetType());
                lines.Add($"FW1-B boss fog {inst} ({model}) for DS1 light wall {wall}: boss {fog.Boss}, category 1, front only, pass flag {killedDs2} (DS1 {fog.Ds1KilledFlag}){(fog.Turned ? ", turned 180°" : "")}{(fog.EntryArea != 0 ? $", front toward DS1 action area {fog.EntryArea} (FW1-F)" : "")}");
                continue;
            }
            var wd = pack.AddRow("MapObjectWhiteDoorParam", pack.Param("MapObjectWhiteDoorParam")[100]!, inst, $"[Lordran] {map.Ds2Map} DS1 light wall {wall}");
            wd["passFlagId"].Value = Convert.ChangeType(0, wd["passFlagId"].Value.GetType());
            // session 12 (fog12), rule FW1-S: a key guide only on a side DS1 lets the player cross from (its 3:18 action areas);
            // DS1's multiplayer walls (1011994 … : no crossing anywhere, shown only with phantoms) get none: a wall, as in DS1
            var (cf, cb) = plan.CrossSides.GetValueOrDefault(wall);
            if (!cf) wd["frontKeyGuideParamId"].Value = Convert.ChangeType(0, wd["frontKeyGuideParamId"].Value.GetType());
            if (!cb) wd["backKeyGuideParamId"].Value = Convert.ChangeType(0, wd["backKeyGuideParamId"].Value.GetType());
            lines.Add($"FW1 white door {inst} ({model}) for DS1 light wall {wall}: key guide {(cf ? "front" : "")}{(cf && cb ? " + " : "")}{(cb ? "back" : "")}{(!cf && !cb ? "none (DS1 never crosses it)" : "")} (FW1-S)");
        }

        // ---- the translated DS1 events
        IReadOnlyDictionary<int, int>? sounds = map.Ds1Sound && map.Ds1Atmosphere ? Events.EntityRules.SoundId(msb1, emevd, map.Ds1Map) : null;
        var ctx = new Events.Ds1MapContext(pack, ds1, plan, generators, sounds);
        var tr = new Events.EmevdTranslator(emevd, ctx, Commands.EmevdDump.Defs());
        // session 11 (burg11, round 9), rule H2-V2: a merged map numbers its events member by member (the primary's, with
        // the events they initialise, first) so a later member never shifts an earlier one's groups
        if (H2Merge.GroupOf(map.Ds1Map) is { } h2 && h2.Primary.StartsWith(map.Ds1Map[..6], StringComparison.OrdinalIgnoreCase))
            tr.MemberRank = id => H2Merge.FlagMemberRank(map.Ds1Map, id);
        int nextGroup = 38000;
        tr.TranslateMap((id, slot) => nextGroup++);
        // session 11 (burg11, M4): DS1 common.emevd's boss rewards (970) instanced for this map's flags (AsylumScript's rule)
        tr.TranslateCommon(EMEVD.Read(Path.Combine(ds1.Root, "event", "common.emevd.dcx")), (id, slot) => nextGroup++, [970]);
        // session 12 (fog12), rule FW1-P: the one-shot walls' pass flags (the translator chose each crossing event's own flag)
        foreach (var (wall, flag) in ctx.PassFlags)
            if (plan.Instances.TryGetValue(wall, out int pInst) && pack.Param("MapObjectWhiteDoorParam")[pInst] is { } pr)
            {
                pr["passFlagId"].Value = Convert.ChangeType(flag, pr["passFlagId"].Value.GetType());
                lines.Add($"FW1-P one-shot fog {pInst} for DS1 light wall {wall}: category 5, pass flag {flag} (the engine sets it once the player has walked through; the translated crossing event waits for it)");
            }

        var esd = ESD.Read(pack.GetFile("gamedata", $"ezstate/event_{map.Ds2Map}.esd"));
        var events = PARAM.Read(pack.GetFile("gamedata", $"param/eventparam_{map.Ds2Map}.param"));
        if (!events.ApplyParamdefCarefully(defs)) throw new InvalidDataException("no paramdef fits EventParam");
        var tbEvents = PARAM.Read(Path.Combine(pack.Ds2Data, "param", $"eventparam_{Tb}.param"));
        if (!tbEvents.ApplyParamdefCarefully(defs)) throw new InvalidDataException("no paramdef fits EventParam");
        var ownerless = tbEvents[5010]!;
        void AddGroup(int id, Dictionary<long, ESD.State> states, int owner = 0)
        {
            if (esd.StateGroups.ContainsKey(id) || events[id] != null) throw new InvalidDataException($"{map.Ds2Map}: event group {id} exists");
            esd.StateGroups[id] = states;
            var row = new PARAM.Row(ownerless) { ID = id };
            row["stateMapId"].Value = Convert.ChangeType(id, row["stateMapId"].Value.GetType());
            if (owner != 0)
            {
                row["bootType"].Value = Convert.ChangeType(2, row["bootType"].Value.GetType());
                row["ownerType"].Value = Convert.ChangeType(1, row["ownerType"].Value.GetType());
                row["ownerId"].Value = Convert.ChangeType(owner, row["ownerId"].Value.GetType());
            }
            events.Rows.Add(row);
        }
        foreach (var r in tr.Results.Where(r => r.States != null)) AddGroup(r.GroupId, r.States!, r.Owner);
        // session 11 (burg11), rule N-H4: hostility flags set 1 s after the load (after event 0's T1-B reset of the channel range)
        {
            int k = 0;
            foreach (int f in MapEnemies.LoadOnFlags.GetValueOrDefault(map.Ds2Map) ?? [])
                AddGroup(37460 + k++, new Dictionary<long, ESD.State>
                {
                    [0] = S([], (True, 8997)),
                    [8997] = S([C(130101, f, 0)], ([0x43, 0x84, 0xA6, 0x80, .. BitConverter.GetBytes(1f), 0x94, 0x40, 0x96, 0xA1], 2)), // rule T1's load-reset state (flag-check)
                    [2] = S([C(130101, f, 1)]),
                });
        }

        // session 11 (chrai11), rule FA1: per boss-pipeline part with forced clips / AI-off / coded E-REQ, the encoder of its request code
        {
            int k = 0;
            foreach (var f in Ai.Boss.EventMessages.Fa1All(map.Ds1Map))
            {
                AddGroup(Fa1EncoderGroup + k, Fa1Encoder(f));
                lines.Add($"rule FA1: encoder group {Fa1EncoderGroup + k} for entity {f.Entity} (generator {f.Generator}): {Fa1Describe(f)}");
                k++;
            }
        }

        // ---- K6 ObjAct groups (AsylumScript.ObjActGroups' rule for any map)
        int objActGroup = 37330, k6 = 0;
        var param = ds1.Params.Params["ObjActParam"];
        foreach (var act in plan.Acts)
        {
            var req = act.Req!;
            if (ctx.Object(act.Entity) is not int obj || ctx.ObjectAnimState(act.Entity, req.AnimId) is not int state || ctx.Flag(req.A) is not int flag)
            { lines.Add($"K6: ObjAct {req.A} on {act.Part.Name}: no prefab state / flag for anim {req.AnimId}: no group"); continue; }
            var pass = new List<ESD.CommandCall> { C(131638, act.Helper, 1), C(131636, obj, state), C(130101, flag, 1) };
            if (req.S != 0 && ctx.Flag(req.S) is int sf) pass.Add(C(130101, sf, 1));
            Dictionary<long, ESD.State> states;
            if (req.Type == 0)
                states = new() { [0] = S([], (True, 1)), [1] = S([C(140205, 0, act.Helper)], (Group(0), 2)), [2] = S(pass, (True, 3)), [3] = S([]) };
            else
            {
                var test = new List<ESD.CommandCall>();
                byte[]? expr = null;
                void Or(ESD.CommandCall reg, int slot) { test.Add(reg); var e = Group(slot)[..^1]; expr = expr == null ? e : [.. expr, .. e, 0x99]; }
                if (req.Type == 1)
                {
                    Or(C(140801, 1, GoodsPorter.Port(pack, ds1, req.Id), 1, 3, 1, 1, 0), 1);
                    if (req.Type2 == 1 && req.Id2 > 0) Or(C(140801, 2, GoodsPorter.Port(pack, ds1, req.Id2), 1, 3, 1, 1, 0), 2);
                }
                else if (req.Type == 2)
                {
                    var log = new List<string>();
                    if (Events.ObjActRules.InnerRegion(emevd, Commands.EmevdDump.Defs(), req.A, req.Id, log) is not int r || ctx.Region(r) is not int loc)
                    { lines.Add($"K6: ObjAct {req.A} on {act.Part.Name}: no side region ({string.Join("; ", log)}): no group"); continue; }
                    Or(C(140132, 1, loc, loc, 1), 1);
                }
                else { lines.Add($"K6: ObjAct {req.A} on {act.Part.Name}: requirement type {req.Type} has no rule: no group"); continue; }
                if (req.Pass > 0 && req.Pass != req.A && ctx.Flag(req.Pass) is int pf) expr = [.. expr!, .. FlagOn(pf)[..^1], 0x99];
                int fail = ctx.Message(req.FailMsg) ?? 1105;
                states = new()
                {
                    [0] = S([], (True, 1)), [1] = S([C(140205, 0, act.Helper)], (Group(0), 2)), [2] = S(test, ([.. expr!, 0xA1], 3), (True, 4)),
                    [3] = S(pass, (True, 5)), [4] = S([C(130443, fail, 0, 0, 190, 0, 0, 0)], (True, 6)), [5] = S([]), [6] = S([C(130002)]),
                };
                if (req.Type == 1)
                    Events.KeySources.K6.Add(new($"ezstate/event_{map.Ds2Map}.esd", objActGroup + k6,
                        req.Type2 == 1 && req.Id2 > 0 ? [GoodsPorter.Port(pack, ds1, req.Id), GoodsPorter.Port(pack, ds1, req.Id2)] : [GoodsPorter.Port(pack, ds1, req.Id)],
                        new() { [0] = S([], (True, 1)), [1] = S([C(140205, 0, act.Helper)], (Group(0), 2)), [2] = S(pass, (True, 3)), [3] = S([]) },
                        $"{map.Ds2Map} ObjAct {req.A} on {act.Part.Name}"));
            }
            AddGroup(objActGroup + k6, states);
            lines.Add($"K6: ObjAct {req.A} on {act.Part.Name} (ObjActParam {req.ParamRow}, type {req.Type} id {req.Id}) → helper {act.Helper}, group {objActGroup + k6}: pass → state {state} + flag {flag}");
            k6++;
        }
        Talk.Covenants.AddHousekeeping(pack, esd, events, ownerless, map.Ds2Map); // cov11 rule K2 / D2-A (as every built map)

        foreach (var (id, states) in esd.StateGroups)
        {
            if (states.TryGetValue(0, out var st0) && st0.EntryCommands.Count > 0) throw new InvalidDataException($"{map.Ds2Map} ESD group {id}: state 0 has entry commands; DS2 never runs them");
            if (states.Count < 2) throw new InvalidDataException($"{map.Ds2Map} ESD group {id} has {states.Count} state(s); DS2 needs at least 2");
        }
        foreach (var c in esd.StateGroups.Values.SelectMany(g => g.Values).SelectMany(s => s.EntryCommands).Where(c => c.CommandID == 130443))
            if (c.Arguments.Count > 0 && c.Arguments[0] is { Length: 6 } a && a[0] == 0x82 && a[5] == 0xA1 && ctx.PendingTexts.TryGetValue(BitConverter.ToInt32(a, 1), out var text))
                pack.SetText("mapevent", BitConverter.ToInt32(a, 1), text);
        events.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
        pack.AddFile("gamedata", $"ezstate/event_{map.Ds2Map}.esd", esd.Write());
        pack.AddFile("gamedata", $"param/eventparam_{map.Ds2Map}.param", events.Write());

        // ---- EventLocation: DS1's regions (ID2) and the rule-made hit regions
        var locs = MapLinks.Locations(pack, map, defs);
        var point = MapLinks.Template(pack, defs, "m20_10_00_00", 3700000);
        var box = MapLinks.Template(pack, defs, "m10_02_00_00", 700000);
        var placements = msb1.Regions.GetEntries().Where(r => r.EntityID > 0 && Ds1Placement.TakesPlacementYaw(r, ds1.Root)).Select(r => r.EntityID).ToHashSet();
        int regions = 0;
        // session 11 (burg11, round 7), rule ID2b: a DS1 point with no DS1 floor within 2 m below (the Depths' NPC summon-sign
        // points 1002000/1002002 float 5.3 m, used only by the DS1R-only sign events) that no translated group names is not
        // written (it would be a warp target over a drop); every other region as before
        var named = new HashSet<int>();
        foreach (var states in esd.StateGroups.Values)
            foreach (var st in states.Values)
                foreach (var bytes in st.EntryCommands.Concat(st.ExitCommands).Concat(st.WhileCommands).Concat(st.Conditions.SelectMany(c => c.PassCommands)).SelectMany(c => c.Arguments)
                    .Concat(st.Conditions.Select(c => c.Evaluator)))
                    for (int i = 0; i + 5 <= bytes.Length; i++)
                        if (bytes[i] == 0x82) named.Add(BitConverter.ToInt32(bytes, i + 1));
        FloorProbe? ds1Floor = null;
        foreach (var r in msb1.Regions.GetEntries().Where(r => r.EntityID > 0))
        {
            if (r.Shape is MSB.Shape.Point && !named.Contains(Events.Ds1MapContext.RegionId(r.EntityID))
                && (ds1Floor ??= FloorProbe.FromDs1(ds1.Root, map.Ds1Map)).Floor(r.Position, above: 0.5f, below: 2f) == null)
            {
                pack.Note($"{map.Ds2Map}: rule ID2b: DS1 point {r.Name} ({r.EntityID}) has no floor within 2 m and no translated group names it: not written");
                continue;
            }
            (System.Numerics.Vector3 Pos, System.Numerics.Vector3 Size, bool Point)? s = r.Shape switch
            {
                MSB.Shape.Box b => (r.Position, new System.Numerics.Vector3(b.Width, b.Height, b.Depth), false),
                MSB.Shape.Point => (r.Position, System.Numerics.Vector3.Zero, true),
                MSB.Shape.Sphere sp => (r.Position - new System.Numerics.Vector3(0, sp.Radius, 0), new System.Numerics.Vector3(2 * sp.Radius, 2 * sp.Radius + 2, 2 * sp.Radius), false),
                MSB.Shape.Cylinder cy => (r.Position, new System.Numerics.Vector3(2 * cy.Radius, cy.Height, 2 * cy.Radius), false),
                _ => null,
            };
            if (s is not { } v) continue;
            int id = Events.Ds1MapContext.RegionId(r.EntityID);
            var rot = placements.Contains(r.EntityID) ? r.Rotation with { Y = Ds1Placement.Yaw(r.Rotation.Y) } : r.Rotation; // rule P1
            if (v.Point) MapLinks.Put(locs, point, id, v.Pos, rot); else MapLinks.Put(locs, box, id, v.Pos, rot, v.Size);
            regions++;
        }
        foreach (var (id, pos, rot, size) in ctx.ExtraRegions) MapLinks.Put(locs, box, id, pos, rot, size);
        pack.AddFile("gamedata", $"param/eventlocation_{map.Ds2Map}.param", locs.Write());

        int ok = tr.Results.Count(r => r.States != null);
        pack.Note($"{map.Ds1Map} DS1 events translated ({map.Ds2Map}, Ds1MapContext):" + Environment.NewLine + tr.Report());
        foreach (var l in lines) pack.Note($"{map.Ds2Map}: {l}");
        pack.Note($"{map.Ds2Map} script: {ok} of {tr.Results.Count} DS1 event instances translated (38000+), {k6} K6 ObjAct groups (37330+), {regions} EventLocation rows from DS1's regions (ID2 3710000 + entity % 10000), {ctx.ExtraRegions.Count} hit regions; rule T1: {tr.TemporaryFlags.Count} temporary flags cleared at load");
        Console.WriteLine($"  {map.AreaName}: {ok} of {tr.Results.Count} DS1 event instances translated, {k6} ObjAct groups");
        return tr.Results.Where(r => r.States != null).Select(r => r.EventId).ToHashSet();
    }
}
