using System.Numerics;
using System.Text.RegularExpressions;
using Lordran.Tools.Build.Chr;
using Lordran.Tools.Build.Maps;
using SoulsFormats;

namespace Lordran.Tools.Build.Events;

/// <summary>
/// The Asylum's map knowledge for <see cref="EmevdTranslator"/>: what AsylumScript / MapBuilder / AsylumEnemies placed
/// and named, plus the session-8 rules that read DS1's own data (MSB ObjActs + ObjActParam for key doors, GameAreaParam
/// + NpcParam for boss rows, ItemLotParam for event lots, Event_text_ for dialogs, collision hkx bounds for "standing on
/// hit" boxes, the light walls' positions for white-door crossing boxes).
/// With <paramref name="pack"/> null (the emevd-translate report) nothing is written and placeholders stand for ids that
/// only a build can know (item ids, pickup instances).
/// </summary>
public sealed class AsylumContext : EmevdTranslator.IContext
{
    const string Map = "m18_01_00_00";
    readonly Pack? pack;
    readonly Ds1Source ds1;
    readonly MSB1 msb1;
    readonly EMEVD emevd;
    readonly Dictionary<(int, int), (string Name, int[] Types)> defs;
    readonly IReadOnlyDictionary<int, int> generators;
    readonly bool firelink;
    readonly Dictionary<int, int> hitGroups;
    readonly IReadOnlyDictionary<int, int> sounds;
    readonly HashSet<int> bossesBuilt = [];
    readonly Dictionary<int, int?> lots = [];
    readonly Dictionary<int, List<(int Lot, int? Flag)>?> lotRows = [];
    readonly Dictionary<(int, int), double?> wakeClips = [];
    readonly HashSet<int> noted = [];

    /// <summary>Objects AsylumScript places itself (prefabs, white doors, the never-opening cell doors); session 10: the
    /// hall floor 1811200 is a world-event part now (rule WE1, <see cref="AsylumScript.WorldParts"/>).</summary>
    static readonly HashSet<int> Scripted = AsylumScript.ScriptedEntities;
    /// <summary>Light walls placed as DS2 white doors (o00_0501, 5 m wide).</summary>
    static readonly HashSet<int> Fogs = [1811990, 1811700, 1811890];
    const float WhiteDoorWidth = 5f;

    /// <param name="generators">DS1 character entity → placed generator (AsylumEnemies); null = the default placement rule
    /// (report).</param>
    /// <param name="hitGroups">the map builder placed DS1's event-toggled collisions in hit groups (build-pack --hit-groups,
    /// opt-in since the session-8 fix pass); off: 2011:1 is a no-op (those collisions are left out)</param>
    /// <param name="sounds">DS1 sound entity → DS2 sound id of the MSB2 Sound regions the build placed
    /// (<see cref="EntityRules.SoundId"/>; the atmos track's DS1 sound regions); null = none (2010:3 → no-op)</param>
    public AsylumContext(Pack? pack, Ds1Source ds1, IReadOnlyDictionary<int, int>? generators = null, bool firelink = true, bool hitGroups = false,
        IReadOnlyDictionary<int, int>? sounds = null)
    {
        this.pack = pack; this.ds1 = ds1;
        msb1 = MSB1.Read(Path.Combine(ds1.Root, "map", "MapStudio", Map + ".msb"));
        emevd = EMEVD.Read(Path.Combine(ds1.Root, "event", Map + ".emevd.dcx"));
        defs = Commands.EmevdDump.Defs();
        // report default: AsylumEnemies' rule for the ported regulars and the 1:1 bosses (step 2: the boss is placed by default)
        this.generators = generators ?? AsylumEnemies.Placements(msb1, Map, ChrPorter.AsylumChrs, onlyGroup: null).ToDictionary(p => p.Entity, p => p.Generator)
            .Concat(AsylumEnemies.BossPlacements(msb1, Map).Select(b => KeyValuePair.Create(b.Entity, b.Generator))).ToDictionary(kv => kv.Key, kv => kv.Value);
        this.firelink = firelink;
        this.hitGroups = hitGroups ? MapBuilder.ToggledCollisions(ds1.Root, Map, msb1) : [];
        this.sounds = sounds ?? new Dictionary<int, int>();
    }

    public string Ds1Map => Map;
    /// <summary>Session 8 (cutscene track): the crow's demos, when Firelink (their arrival map) is built and build-pack
    /// planned them (<see cref="Cutscenes.Cutscenes.Planned"/>).</summary>
    public int? Demo(int ds1Scene) => firelink ? Cutscenes.Cutscenes.Demo(ds1Scene) : null;
    /// <summary>Session 10 (e3): the map's own flags and globals (FlagRules.Map), and rule F1x: persistent flags of other DS1
    /// maps (event 50's Kiln flags 11807200…, NG+ reset) — F1 slots are global, as DS1's flags are.</summary>
    public int? Flag(int f) => FlagRules.Map("m18_01", f) ?? FlagRules.OtherMapPersistent("m18_01", f);
    /// <summary>Session 9 (track npcflags): rule T1 for the Asylum too. Its temporary flags 11815nnn now have storage
    /// (FlagRules F2: m18_01's map-slot block, which survives a reload and a quit), so event 0 must clear them at load as DS1
    /// does. Boss track merge: its E6 fold of event 0's writes must keep this reset state (8997) first.</summary>
    public bool ResetsTemporaryFlags => true;

    // ---------------------------------------------------------------- objects
    public int? Object(int e)
    {
        if (Scripted.Contains(e)) return AsylumScript.Inst(e);
        if (AsylumScript.WorldParts().FirstOrDefault(p => p.Entity == e) is { } wp) return wp.Instance; // session 10, rule WE1
        if (MapBuilder.PlacedObjects.TryGetValue((Map, e), out int inst)) return inst;
        if (MapBuilder.PlacedObjects.Keys.Any(k => k.Map == Map)) return null; // a build: only what it placed
        // report: MapBuilder's rules (doors of ported door models; item pickups on treasure parts it keeps)
        var part = msb1.Parts.Objects.FirstOrDefault(p => p.EntityID == e);
        if (part == null) return null;
        if (part.ModelName == "o8540" && !AsylumScript.NeverOpen.Contains(e)) return AsylumScript.Inst(e);
        if (EventLadders.ContainsKey(e)) return AsylumScript.Inst(e); // session 10, rule G4 (MapBuilder gives it its own instance)
        if (MapBuilder.IsScenery(ds1.Root, Map, part)) return AsylumScript.Inst(e); // session 8 (track nests): the cliff nest
        // session 10 (asy-items), rule C3: every treasure but chests is placed (a placeholder id in the report)
        if (IsTreasurePart(part)) return 18016000 + e % 1000;
        return null;
    }

    /// <summary>Session 10 (rule C3): the part carries a DS1 treasure the map builder places (not in a chest).</summary>
    bool IsTreasurePart(MSB1.Part part) => msb1.Events.Treasures.Any(t => t.TreasurePartName == part.Name && !t.InChest);

    /// <summary>A DS1 object part the map doesn't place — unless it stands where a placed object stands (DS1's "swap in
    /// the end-state copy" idiom, e.g. the boulder's o8530_0001 + rubble o8531 at o8530_0000's spot: no rule yet).</summary>
    public bool ObjectAbsent(int e)
    {
        var part = msb1.Parts.Objects.FirstOrDefault(p => p.EntityID == e);
        if (part == null || Object(e) != null) return false;
        // session 10 (rule C3): a treasure corpse the build didn't place (none of its items is in DS2 yet) is absent, even
        // where other corpses stand at the same spot (the class corpses: ten per spot)
        if (IsTreasurePart(part)) return true;
        return !msb1.Parts.Objects.Any(p => p.EntityID > 0 && p.EntityID != e && Object(p.EntityID) != null && Vector3.Distance(p.Position, part.Position) < 0.05f);
    }

    /// <summary>Session 10, rule WE4: a placed object with DS1's 2005:9 carries the damage on its own instance row
    /// (AsylumScript.DamageRow; was the boulder alone, FoFG's 210100030).</summary>
    public bool ObjectDamages(int e) => AsylumScript.DamageRow(e) != null && Object(e) != null;

    /// <summary>A placed prefab part's model (scripted prefab or a WE1 swap copy of one), or null.</summary>
    string? PrefabModel(int e) => (Scripted.Contains(e) || AsylumScript.WorldParts().Any(p => p.Entity == e && p.Kind == WorldEvents.Kind.Prefab))
        && msb1.Parts.Objects.FirstOrDefault(p => p.EntityID == e) is { } part && AsylumScript.PrefabPort(part.ModelName) != null ? part.ModelName : null;

    /// <summary>Session 10 (rule C11): by the part's model, not its entity, so DS1's swap copies (the boulder's rest copy
    /// o8530_0001) share the prefab's table: o10_1001 70 plays 10001 = DS1 open, 80 plays 10002 = close; o10_9240 70 plays
    /// 1 = the roll (<see cref="AsylumScript.PrefabAnim"/>).</summary>
    public int? ObjectAnimState(int e, int anim) => PrefabModel(e) is { } m && AsylumScript.PrefabAnim(m, anim) is { } s ? s.Play : null;

    /// <summary>Where the prefab rests after the state above: o10_1001 70 → 30 (waiting, lowered/opened), 80 → 10 (initial);
    /// o10_9240 70 → 20 (end state). From the prefab state names (data\scratch\prefab-states.txt, session 7).</summary>
    public int? ObjectAnimEndState(int e, int anim) => PrefabModel(e) is { } m && AsylumScript.PrefabAnim(m, anim) is { } s ? s.After : null;

    /// <summary>Rule C11: the rest state, when the template's state machine takes a direct request of it (o10_9240: 20 yes;
    /// o10_1001: 30 no → the fallback replays 70).</summary>
    public int? ObjectRestState(int e, int anim) => PrefabModel(e) is { } m && AsylumScript.PrefabAnim(m, anim) is { } s
        && WorldEvents.PrefabRequests(Commands.ExtractDs2.DefaultOut, s.Template).Contains(s.After) ? s.After : null;

    /// <summary>Rule WE5 (C11): a WE1 part DS1 only reproduces (never plays, no ObjAct): its placement bakes the end pose
    /// (the boulder's rest copy o8530_0001: a copy of the boulder's prefab in its initial state at the baked transform).
    /// Fix pass (C16): only a part WE1 really baked (hidden at load in DS1; a prefab copy only when its clip starts at the
    /// bind pose), i.e. <see cref="WorldEvents.Part.Baked"/>; otherwise the 2005:7 falls through to C11 / no rule.</summary>
    public bool ObjectPoseBaked(int e, int anim) => AsylumScript.WorldParts().Any(p => p.Entity == e && p.Baked);

    /// <summary>Rule WE4b: DS1's damaging-object hits as (instance, s); the log goes to the pack notes.</summary>
    public IReadOnlyList<(int Instance, float Seconds)> ObjectHits(int e, int anim)
    {
        var log = new List<string>();
        var hits = WorldEvents.Hits(ds1.Root, Map, msb1, e, anim, log);
        foreach (var l in log) { if (pack != null) pack.Note("WE4b " + l); else Console.WriteLine("  [WE4b] " + l); }
        return hits.Where(h => Object(h.Target) != null).Select(h => (Object(h.Target)!.Value, h.Seconds)).ToList();
    }

    /// <summary>Rule WE8: the drop under a floor DS1's events break reaches DS2's lethal fall height (DS1 survives it).</summary>
    public bool BreakNeedsFallGuard(int e) => WorldEvents.Drop(ds1.Root, Map, msb1, e) is float d && d >= WorldEvents.DeadFallHeight();

    /// <summary>DS1 action buttons on the hall door (11810313) use the second helper; the first stands for its ObjAct. An
    /// action-button area (the nest 1812001) has one helper per prompt (<see cref="AsylumScript.AreaButtons"/>).</summary>
    public int? ActionHelper(int e, int message) => e == 1811111 ? AsylumScript.HallDoorGuide2
        : AsylumScript.AreaButtons().FirstOrDefault(b => b.Area == e && b.Message == message) is { } b ? b.Helper : null;

    public int? Sound(int e) => sounds.TryGetValue(e, out int s) ? s : null;
    // session 10 (asy-sound, rules SND1/SND4/SND5): the map's sound plan (EntityRules.SoundId → MapSounds) when the build
    // placed DS1's sound (--ds1-sound default); a plain dictionary (report tools) = regions only
    public int? SoundEffect(int e, int type, int id) => (sounds as MapSounds)?.EffectKey(e, type, id);
    public int? OneShotSfx(int e, int sfx) => (sounds as MapSounds)?.OneShotKey(e, sfx);
    public IEnumerable<int> SoundsStartedIn(long ev) => (sounds as MapSounds)?.StartedIn(ev) ?? [];
    /// <summary>The ObjAct helper of a K1 prefab ObjAct part (session 10: rule K1 + K12, AsylumScript.PrefabActs): the hall
    /// door keeps its session-7 instance 18011119 (V), the others EntityRules.ObjActHelper (AABB5000 + entity % 1000).</summary>
    public int? ObjActHelper(int e) => AsylumScript.PrefabActs().FirstOrDefault(p => p.Entity == e)?.Helper;

    // ---------------------------------------------------------------- ladders (session 10, rule G4)
    Dictionary<int, int>? eventLadders;
    /// <summary>The Asylum's ladder parts DS1 registers later than load (ObjActRules.EventRegisteredLadders): the Stray
    /// Demon pit ladder 1811141 (11815382 / 11810900); the pool ladder 1811140 is registered by event 0 at load.</summary>
    public IReadOnlyDictionary<int, int> EventLadders => eventLadders ??= MapBuilder.EventLadders(ds1.Root, Map, msb1)
        .ToDictionary(e => e, e => MapBuilder.PlacedObjects.TryGetValue((Map, e), out int i) ? i : AsylumScript.Inst(e));
    public int? Ladder(int e) => EventLadders.TryGetValue(e, out int i) ? i : null;

    /// <summary>Rule K7: the item of DS1's &lt;?gdsparam@N?&gt; (DS1 goods N: the keys; GoodsPorter's id rule, ported in a build).</summary>
    public int? MessageItem(int m)
    {
        var match = Regex.Match(ds1.Text("Event_text_", m) is { Length: > 0 } t ? t : ds1.AnyText(m), @"<\?gdsparam@(\d+)\?>");
        if (!match.Success) return null;
        int goods = int.Parse(match.Groups[1].Value);
        return pack != null ? GoodsPorter.Port(pack, ds1, goods) : GoodsPorter.Id(goods);
    }
    public int? SfxObject(int e) => e switch { 1811991 => AsylumScript.BossFog, 1811701 => AsylumScript.CourtyardFog, 1811891 => AsylumScript.PitFog, _ => null };
    /// <summary>Session 12 (fog12), rule FW1-P project-wide (as Ds1MapContext): a light wall the Asylum places as a white door
    /// that is not a boss fog (BossFogs) and DS1 crosses with 3:18 = a native one-shot fog (the courtyard fog o8952, 11810090);
    /// AsylumScript.Params gives its row the pass flag = the crossing event's own flag.</summary>
    public bool FogNativePass(int fog) => SfxObject(fog + 1) != null && !AsylumBossFogs.Value.ContainsKey(fog);
    static readonly Lazy<Dictionary<int, Maps.BossFogs.Fog>> AsylumBossFogs = new(() =>
    {
        string root = Program.DefaultDsrRoot;
        var msb = MSB1.Read(Path.Combine(root, "map", "MapStudio", "m18_01_00_00.msb"));
        var ev = EMEVD.Read(Path.Combine(root, "event", "m18_01_00_00.emevd.dcx"));
        var sfx = msb.Events.SFX.Select(s => s.EntityID).Where(e => e > 0).ToHashSet();
        return Maps.BossFogs.Of(root, msb, ev, Commands.EmevdDump.Defs(), msb.Parts.Objects.Where(p => p.EntityID > 0 && sfx.Contains(p.EntityID + 1)).Select(p => p.EntityID).ToHashSet());
    });

    // ---------------------------------------------------------------- regions
    public int? Region(int e) => e is >= 1812000 and < 1813000 && AsylumScript.RegionIds.Contains(AsylumScript.RegionId(e)) ? AsylumScript.RegionId(e) : null;
    public int? Point(int e) => Region(e);

    /// <summary>"Standing on hit" (11:2) → a box over the DS1 collision's world bounds (triangles of its hkx), from 1 m
    /// below its lowest point to 2 m above its highest: id 3702000 + entity % 1000 (h0006B1 1813120 → 3702120). A hit
    /// group test (140401) would be closer to DS1, but the invisible ramps over its risers are separate collision parts.</summary>
    public int? HitRegion(int e)
    {
        var part = msb1.Parts.Collisions.FirstOrDefault(p => p.EntityID == e);
        if (part == null) return null;
        int id = 3702000 + e % 1000;
        if (AsylumScript.ExtraRegions.Any(r => r.Id == id)) return id;
        string d1 = Path.Combine(ds1.Root, "map", Map);
        var hi = BXF3.Read(Path.Combine(d1, $"h{Map[1..]}.hkxbhd"), Path.Combine(d1, $"h{Map[1..]}.hkxbdt"));
        var f = hi.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith($"h{part.ModelName[1..]}A", StringComparison.OrdinalIgnoreCase));
        if (f == null) return null;
        var m = Commands.ColSurvey.Place(part.Position, part.Rotation, part.Scale);
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (var (a, b, c) in Commands.ColSurvey.Triangles(DCX.Decompress(f.Bytes)))
            foreach (var p in new[] { a, b, c }) { var w = Vector3.Transform(p, m); min = Vector3.Min(min, w); max = Vector3.Max(max, w); }
        if (min.X > max.X) return null;
        // DS1-exact in X/Z (h0006B1 begins 0.16 m past the escape gate; the session-7 hand-made box began at x 16.5)
        var size = new Vector3(max.X - min.X, max.Y - min.Y + 3f, max.Z - min.Z);
        AsylumScript.ExtraRegions.Add((id, new Vector3((min.X + max.X) / 2, min.Y - 1f, (min.Z + max.Z) / 2), Vector3.Zero, size, false));
        return id;
    }

    public int? HitGroup(int e) => hitGroups.TryGetValue(e, out int g) ? g : null;

    /// <summary>
    /// White-door crossing boxes for DS1's action button on a light wall from area A: 5 m (the white door's width) along
    /// the wall, 4 m high from 1 m below the wall's origin, 2.5 m deep on each side of the wall's plane (normal = the
    /// wall's local Z, DS1 yaw), oriented like the wall. Ids 3703000 + (wall % 1000)·2 + (0 on the +normal side, 1 on the
    /// −normal side): courtyard fog 1811700 → 3704400 (+X, courtyard) / 3704401 (−X, bonfire side; the hand-written
    /// group's box 3703601 stood there).
    /// </summary>
    public (int From, int To)? FogCrossing(int fog, int area)
    {
        if (!Fogs.Contains(fog)) return null;
        var wall = msb1.Parts.Objects.FirstOrDefault(p => p.EntityID == fog);
        var region = msb1.Regions.GetEntries().FirstOrDefault(r => r.EntityID == area);
        if (wall == null || region == null) return null;
        float yaw = wall.Rotation.Y * MathF.PI / 180;
        var n = new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw));
        int side = Vector3.Dot(region.Position - wall.Position, n) >= 0 ? 1 : -1;
        int Box(int s)
        {
            int id = 3703000 + fog % 1000 * 2 + (s > 0 ? 0 : 1);
            if (!AsylumScript.ExtraRegions.Any(r => r.Id == id))
                AsylumScript.ExtraRegions.Add((id, wall.Position + n * (1.25f * s) - new Vector3(0, 1f, 0), new Vector3(0, wall.Rotation.Y, 0), new Vector3(WhiteDoorWidth, 4f, 2.5f), false));
            return id;
        }
        return (Box(side), Box(-side));
    }

    // ---------------------------------------------------------------- texts
    static FMG? ds2MapEvent;

    /// <summary>DS1 Event_text_ message → DS2 mapevent: DS2's own entry when the English text is the same (DS1's item
    /// placeholder &lt;?gdsparam@N?&gt; read as DS2's %s: 10010869 "Used …" → 1005, 10010163 "It's locked" → 1105,
    /// 10010161 → 1101, 10010160 "Locked by some contraption" → 1103), else DS1's text as 18000000 + id % 1000000.</summary>
    public int? Message(int m)
    {
        string text = ds1.Text("Event_text_", m);
        if (string.IsNullOrEmpty(text)) text = ds1.AnyText(m);
        if (string.IsNullOrEmpty(text)) return null;
        string norm = Regex.Replace(text, @"<\?gdsparam@\d+\?>", "%s").Trim();
        ds2MapEvent ??= FMG.Read(Path.Combine(Commands.ExtractDs2.DefaultOut, "menu", "text", "english", "mapevent.fmg"));
        var same = ds2MapEvent.Entries.Where(e => e.Text?.Trim() == norm).OrderBy(e => e.ID).FirstOrDefault();
        if (same != null) return same.ID;
        int id = 18000000 + m % 1000000;
        pack?.SetText("mapevent", id, text);
        return id;
    }

    /// <summary>Session 10 (rule M4): a DS1 message with this entity that this build doesn't place (hidden with M4 off,
    /// or --no-blood-messages).</summary>
    public bool MessageAbsent(int e) => msb1.Events.Messages.Any(x => x.EntityID == e) && MessageLocation(e) == null;

    /// <summary>Session 10 (asy-items), rule M2: the EventLocation row of a placed DS1 message (BloodMessages).</summary>
    public int? MessageLocation(int e) => BloodMessages.Location(ds1.Root, Map, e);

    /// <summary>Session 10 (rule C3, load-init): the start-disabled treasures' pickups (the build's; report: placeholders).</summary>
    public IEnumerable<int> TreasuresDisabledAtLoad => MapBuilder.StartDisabledPickups.TryGetValue(Map, out var l) ? l
        : msb1.Events.Treasures.Where(t => t.StartDisabled && !t.InChest).Select(t => msb1.Parts.Objects.FirstOrDefault(p => p.Name == t.TreasurePartName))
            .Where(p => p != null && p.EntityID > 0).Select(p => Object(p!.EntityID)).OfType<int>();

    // ---------------------------------------------------------------- ObjActs
    MSB1.Event.ObjAct? ObjAct(int flag) => msb1.Events.ObjActs.FirstOrDefault(a => a.ObjActEntityID == flag);
    MSB1.Part.Object? ObjActPart(MSB1.Event.ObjAct a) => msb1.Parts.Objects.FirstOrDefault(p => p.Name == a.ObjActPartName);
    PARAM.Row? ObjActParam(MSB1.Event.ObjAct a, MSB1.Part.Object part) =>
        ds1.Params.Params["ObjActParam"][a.ObjActParamID >= 0 ? a.ObjActParamID : int.Parse(part.ModelName[1..])];

    /// <summary>The hall door's ObjAct (no requirement): the ObjAct helper rule group sets its DS1 flag.</summary>
    public int? ObjActFlag(int f) => ObjAct(f) is { } a && ObjActPart(a) is { } p && ObjActHelper(p.EntityID) != null ? Flag(f) : null;

    /// <summary>An ObjAct DS2 has no interaction for: on a cell door that never opens, or on an object the map doesn't
    /// place (the far door o8544 until it is ported).</summary>
    public bool ObjActAbsent(int f) => ObjAct(f) is { } a && ObjActPart(a) is { } p && (AsylumScript.NeverOpen.Contains(p.EntityID) || ObjectAbsent(p.EntityID));

    /// <summary>
    /// DS1 ObjAct (MSB: ObjAct entity = the flag 5:2 waits for, EventFlagID = the door's persistent state flag) on a ported
    /// door with an ObjActParam requirement → DS2 door lock: spQualifiedType 1 = goods (spQualifiedId, ported with
    /// GoodsPorter), 2 = a SpEffect DS1 grants in a region (event 11810120: SpEffect 4150 while in 1812020) → the door opens
    /// only from that region; fail text = actionFailedMsgId through the text rule; the lock sets the state flag.
    /// </summary>
    /// <summary>
    /// Session 10 (doors.md K1-K5, ObjActRules): only an ObjAct whose MSB type is Door (K1) on a placed DS2 door; the
    /// requirement from ObjActParam: type 1 goods → the ported item (K2; type2 1 = an OR alternative → Item2, rule K4);
    /// type 2 SpEffect → the side test (K3): the lock region is the grant slot's region with the door's flag A OFF
    /// (<see cref="ObjActRules.InnerRegion"/>, was "the first 3:2 of any granting event": unsubstituted, A ignored), opened
    /// text 0 (DS1 shows no "Used" message there; vanilla 【Lib】領域指定扉開錠 passes 0), fail text "Does not open from
    /// this side" (actionFailedMsgId 10010161 → 1101); lock flag = S, else A (K2/K9). K5: a pass flag ≠ A that some DS1
    /// event sets has no rule yet (reported); the Asylum's are A or never set (8540's 11810100 = cell 0000's A).
    /// </summary>
    public EmevdTranslator.ObjActLock? Lock(int f)
    {
        if (ObjAct(f) is not { } a || ObjActPart(a) is not { } part || AsylumScript.NeverOpen.Contains(part.EntityID) || Scripted.Contains(part.EntityID)) return null;
        if (ObjActRules.KindOf(a, part) != ObjActRules.Kind.Door) return null;
        if (Object(part.EntityID) is not int door || ObjActRules.Of(msb1, ds1.Params.Params["ObjActParam"], a) is not { } req) return null;
        int Goods(int q) => pack != null ? GoodsPorter.Port(pack, ds1, q) : GoodsPorter.Id(q);
        int item = 0, region = 0, item2 = 0;
        if (req.Type == 1)
        {
            item = Goods(req.Id);
            if (req.Type2 == 1 && req.Id2 > 0) item2 = Goods(req.Id2);
        }
        else if (req.Type == 2)
        {
            var log = new List<string>();
            if (ObjActRules.InnerRegion(emevd, defs, req.A, req.Id, log) is int r && Region(r) is int loc) region = loc;
            if (noted.Add(req.A)) foreach (var l in log) { if (pack != null) pack.Note(l); else Console.WriteLine("  [K3] " + l); }
            if (region == 0) return null;
        }
        else return null;
        int fail = Message(req.FailMsg) ?? 1105;
        if (Flag(req.S != 0 ? req.S : req.A) is not int state) return null;
        return new EmevdTranslator.ObjActLock(door, region, item, req.Type == 1 ? 1005 : 0, fail, state, item2);
    }

    public bool SpEffectGatesObjAct(int sp) => msb1.Events.ObjActs.Any(a => ObjActPart(a) is { } p && ObjActParam(a, p) is { } row
        && Convert.ToInt32(row["spQualifiedType"].Value) == 2 && Convert.ToInt32(row["spQualifiedId"].Value) == sp);

    // ---------------------------------------------------------------- characters and bosses
    public int? Generator(int e) => generators.TryGetValue(e, out int g) ? g : null;
    /// <summary>Session 11 (track ret11), rule W1: a placed non-boss character whose generator carries DS1's world-state fold
    /// (<see cref="AsylumEnemies.WorldConditions"/>; the same question AsylumEnemies.Build answered).</summary>
    public bool WorldOwned(int e) => Generator(e) != null && !IsBoss(e) && AsylumEnemies.WorldConditions(Map, e) != null;
    /// <summary>Session 11 (track ret11), rule N-H2: a placed NPC whose generator carries DS1's load-time hostility
    /// (<see cref="NpcPorter.HostileAtLoad"/>, the same question NpcPorter answered).</summary>
    public bool NpcHostile(int e) => e is > 0 and < 10000 && Generator(e) != null && NpcPorter.HostileAtLoad(ds1.Root, Map, e) is int f && FlagRules.Any(f) != null;

    /// <summary>Session 10 (e3, rule N1): DS1 NpcParam getSoul × ChrPorter.SoulScale of the MSB character (what NpcPorter /
    /// ChrPorter write as dropSoulValue), null when the part or its NpcParam row is unknown.</summary>
    public int? CharacterSouls(int e)
    {
        var part = msb1.Parts.Enemies.FirstOrDefault(p => p.EntityID == e);
        if (part == null || ds1.Params.Params["NpcParam"][part.NPCParamID] is not { } row) return null;
        return Chr.Ds1Calibration.Souls(row);   // bal11 rule BAL2
    }
    public bool IsBoss(int e) => e > 10000 && ds1.Params.Params["GameAreaParam"][e] != null;
    /// <summary>Session 11 (ret11), rule B-k: the boss's index k among the map's DS1 bosses in entity order (Asylum Demon 0, Stray Demon 1).</summary>
    int BossIndex(int e) => Math.Max(0, msb1.Parts.Enemies.Where(p => p.EntityID > 0 && IsBoss(p.EntityID)).Select(p => p.EntityID).Distinct().Order().ToList().IndexOf(e));

    /// <summary>
    /// BossBattleParam row for a DS1 boss (id = DS1 entity = its GameAreaParam row), cloned from Smelter Demon's 1019000
    /// (a large demon: camera/lock-on params, soul bullet, FE timing): souls = GameAreaParam.bonusSoul_single ×
    /// ChrPorter.SoulScale (Asylum Demon 2000 → 3000); item lot = the boss's NpcParam drops (Ds1Drops; DS1's real boss
    /// rewards are common.emevd event 970: not translated yet); killed flag = the DS1 flag its defeat event sets right
    /// before Handle Boss Defeat (11810001: 16 → 109000016), else that event's own flag (11810900); onceKilledEventId =
    /// the same flag (DS1 has no separate one); no DS2 bonfire link (bonfireId / bonfireEventValueId 0: no ascetic revival);
    /// generator event group <see cref="EntityRules.BossEventGroup"/>; name = DS1's HP bar NameID in talk/charaname.
    /// </summary>
    public int? BossBattle(int e, int nameId)
    {
        if (!IsBoss(e)) return null;
        int id = EntityRules.BossBattle(e);
        if (pack == null) return id;
        var bb = pack.Param("BossBattleParam");
        if (bossesBuilt.Add(id))
        {
            var row = pack.AddRow("BossBattleParam", bb[1019000]!, id, $"[Lordran] DS1 boss {e}");
            var area = ds1.Params.Params["GameAreaParam"][e]!;
            Set(row, "soulValue", Chr.Ds1Calibration.BossSouls(area));   // bal11 rule BAL2
            int lot = BossLot(e);
            Set(row, "itemLotId", lot); Set(row, "itemLotIdMultiPlay", lot);
            int killed = KilledFlag(e) ?? 0;
            Set(row, "killedEventId", killed); Set(row, "onceKilledEventId", killed);
            Set(row, "bonfireEventValueId", 0); Set(row, "bonfireId", 0);
            Set(row, "genEventGroupId1", EntityRules.BossEventGroupOf(BossIndex(e))); Set(row, "genEventGroupId2", 0); Set(row, "genEventGroupId3", 0);   // session 11 (ret11): rule B-k
            Set(row, "hpDispType2", 0); Set(row, "hpDispType3", 0);
            Set(row, "bossNameTextId1", 0); Set(row, "bossNameTextId2", 0); Set(row, "bossNameTextId3", 0);
            pack.Note($"BossBattleParam {id} (DS1 boss {e}): souls {row["soulValue"].Value} (DS1 GameAreaParam {area["bonusSoul_single"].Value}), lot {lot}, killed flag {killed}");
        }
        if (nameId > 0)
        {
            int text = EntityRules.CharaName(nameId);
            pack.SetText("talk/charaname", text, ds1.Text("NPC_name_", nameId));
            Set(bb[id]!, "bossNameTextId1", text);
        }
        return id;
    }

    /// <summary>The DS1 boss's NpcParam drops (MSB part of the entity → NpcParam itemLotId_1..6) as an ItemLotParam2_Other row, 0 if none.</summary>
    int BossLot(int e)
    {
        var part = msb1.Parts.Enemies.FirstOrDefault(p => p.EntityID == e);
        if (part == null || pack == null || ds1.Params.Params["NpcParam"][part.NPCParamID] is not { } npc) return 0;
        var notes = new List<string>();
        var drops = Ds1Drops.Resolve(ds1, npc, pack, notes);
        foreach (var n in notes) pack.Note($"boss {e} drops: {n}");
        if (drops.Count == 0) return 0;
        int id = EntityRules.ItemLot(Convert.ToInt32(npc["itemLotId_1"].Value));
        WriteLot(id, $"[Lordran] DS1 boss {e} drops", drops.Select(d => (d.ItemId, d.Num, d.Reinforce, d.Percent)).ToList());
        return id;
    }

    int? KilledFlag(int e)
    {
        foreach (var ev in emevd.Events)
        {
            int at = ev.Instructions.FindIndex(i => i.Bank == 2003 && i.ID == 12 && (int)EmevdTranslator.Decode(defs, i)[0] == e);
            if (at < 0) continue;
            for (int k = at - 1; k >= 0; k--)
                if (ev.Instructions[k] is { Bank: 2003, ID: 2 } s && EmevdTranslator.Decode(defs, s) is var v && v[1] == 1) return Flag((int)v[0]);
            return Flag((int)ev.ID);
        }
        return null;
    }

    // ---------------------------------------------------------------- bosses: logic flag, start-up, wake (step 2)
    /// <summary>Session 11 (G4, rule T1-B): every placed boss's logic flag (reset by event 0 at load).</summary>
    public IEnumerable<int> BossLogicFlags => msb1.Parts.Enemies.Where(p => p.EntityID > 0 && IsBoss(p.EntityID)).Select(p => p.EntityID)
        .Distinct().Select(LogicFlag).OfType<int>().Distinct().Order();

    /// <summary>The boss's logic flag (FlagRules.BossLogicFlag: the map's k-th DS1 boss in entity order).</summary>
    public int? LogicFlag(int e)
    {
        if (!IsBoss(e) || Generator(e) == null) return null;
        var bosses = msb1.Parts.Enemies.Where(p => p.EntityID > 0 && IsBoss(p.EntityID)).Select(p => p.EntityID).Distinct().Order().ToList();
        return FlagRules.BossLogicFlag("m18_01", bosses.IndexOf(e));
    }

    /// <summary>The 1:1 boss's start-up state for a DS1 waiting animation (Ds1Boss.StartUpType: 9000 → 2).</summary>
    public int? StartUpState(int e, int anim) => Ds1Boss.ForEntity(e) is { } s && Generator(e) != null ? Ds1Boss.StartUpType(s, anim) : null;

    /// <summary>DS1 length of the 1:1 boss's start-up wake clip (Ds1Boss.WakeClip: 9060, 4.667 s) when <paramref name="anim"/> is it.</summary>
    public double? WakeClip(int e, int anim)
    {
        if (wakeClips.TryGetValue((e, anim), out var d)) return d;
        double? r = null;
        if (Ds1Boss.ForEntity(e) is { } s && Generator(e) != null && (Ds1Boss.WakeClip(s, 2) == anim || Ds1Boss.WakeClip(s, 3) == anim))
            r = Ds1ChrSource.Load(ds1.Root, s.Ds1Chr).Samples(anim).Duration;
        return wakeClips[(e, anim)] = r;
    }

    /// <summary>DS1 SpEffectParam stateInfo 47 ("Falling control for enemy", 4160).</summary>
    public bool FallControl(int sp) => ds1.Params.Params["SpEffectParam"][sp] is { } row && Convert.ToInt32(row["stateInfo"].Value) == 47;

    // ---------------------------------------------------------------- items
    /// <summary>
    /// DS1 ItemLotParam (+ its chained rows lot+1, lot+2 … while they exist: DS1 rolls each) → ItemLotParam2_Other row
    /// <see cref="EntityRules.ItemLot"/>, items through the item map (Ds1Drops.MapItem). Every row a sure item → DS2's
    /// event-lot shape (lotCount = item count, weight 1 each, like 60001000); a random row → one roll with percent weights
    /// (Ds1Drops' convention; DS1's "nothing" share stays empty). Rows after a random one are left out (noted).
    /// </summary>
    /// <summary>Session 10 (asy-items i2), rule S2: DS1 ItemLotParam getItemFlagId.</summary>
    /// <summary>Session 10 (asy-items i2 fix pass), rule R5: a ring's DS1 SpEffect → its DS2 ring item (RingPorter).</summary>
    public int? RingItem(int sp) => RingPorter.ItemForSpEffect(pack, ds1, sp);

    public int? ItemLotFlag(int lot) => ds1.Params.Params["ItemLotParam"][lot] is { } r && Convert.ToInt32(r["getItemFlagId"].Value) is int f and > 0 ? f : null;

    public int? ItemLot(int lot)
    {
        if (lots.TryGetValue(lot, out var done)) return done;
        var dl = ds1.Params.Params["ItemLotParam"];
        var rows = new List<PARAM.Row>();
        for (int k = lot; rows.Count < 8 && dl[k] is { } r; k++) rows.Add(r);
        int? result = null;
        if (rows.Count > 0)
        {
            var items = new List<(int Item, int Num, int Reinforce, float Percent)>();
            foreach (var r in rows)
            {
                var entries = Enumerable.Range(1, 8).Select(i => (Id: Convert.ToInt32(r[$"lotItemId0{i}"].Value), Cat: unchecked((uint)Convert.ToInt64(r[$"lotItemCategory0{i}"].Value)),
                    Points: Convert.ToDouble(r[$"lotItemBasePoint0{i}"].Value), Num: Convert.ToInt32(r[$"lotItemNum0{i}"].Value))).Where(x => x.Points > 0).ToList();
                double total = entries.Sum(x => x.Points);
                bool sure = entries.Count == 1 && entries[0].Id > 0;
                if (!sure && items.Count > 0) { pack?.Note($"item lot {lot}: chained random row {r.ID} left out"); break; }
                foreach (var x in entries.Where(x => x.Id > 0))
                {
                    int reinforce = 0, ds2 = pack != null ? Ds1Drops.MapItem(pack, ds1, x.Cat, x.Id, out _, out reinforce, out _) : x.Id;
                    if (ds2 == 0) { pack?.Note($"item lot {lot}: DS1 item {x.Id} not in DS2 yet, left out"); continue; }
                    items.Add((ds2, Math.Max(1, x.Num), reinforce, sure ? 100f : (float)(100 * x.Points / total)));
                }
                if (!sure) break;
            }
            if (items.Count > 0)
            {
                result = EntityRules.ItemLot(lot);
                if (pack != null) WriteLot(result.Value, $"[Lordran] DS1 item lot {lot}", items);
            }
        }
        lots[lot] = result;
        return result;
    }

    /// <summary>
    /// A DS1 lot chain whose rows carry getItemFlagId (step 2; DS1: a row whose flag is set is skipped, awarding sets it; the
    /// Asylum Demon's 2660 = Big Pilgrim's Key 50001660 + 2661 Demon's Great Hammer 50001661): one ItemLotParam2_Other row per
    /// DS1 row (EntityRules.ItemLot(row id), sure items) with its flag through the flag rule; null when no row has a flag.
    /// Rows whose items aren't in DS2 yet are left out (noted).
    /// </summary>
    public List<(int Lot, int? Flag)>? ItemLotRows(int lot)
    {
        if (lot <= 0) return null;   // rule L0: DS1 lot 0 = "no lot" (its row 0 is a DEBUG row)
        if (lotRows.TryGetValue(lot, out var done)) return done;
        var dl = ds1.Params.Params["ItemLotParam"];
        var rows = new List<PARAM.Row>();
        for (int k = lot; rows.Count < 8 && dl[k] is { } r; k++) rows.Add(r);
        List<(int, int?)>? result = null;
        // session 11 (track ret11), rule L-chain (asy10 enemies.md §3): DS1 rolls every row of a chain on its own, so a chain
        // with a random row and more rows (the Black Knight's 27907000: nothing 75 / BK Sword 20 / BK Shield 5, then 27907001 Red
        // Titanite Chunk 100 %) is awarded row by row too; ItemLot would leave out every row after a random one. A row's
        // percents are taken over ALL its weighted slots incl. DS1's "nothing" (id 0 / category -1), so DS2's percent weights
        // (one roll, the remainder = nothing: D16) give DS1's odds; a row is sure when its only weighted slot is an item.
        bool Item((int Id, uint Cat, double Points, int Num) x) => x.Id > 0 && x.Cat != 0xFFFFFFFF;
        List<(int Id, uint Cat, double Points, int Num)> Weighted(PARAM.Row r) => Enumerable.Range(1, 8).Select(i => (Id: Convert.ToInt32(r[$"lotItemId0{i}"].Value), Cat: unchecked((uint)Convert.ToInt64(r[$"lotItemCategory0{i}"].Value)),
            Points: Convert.ToDouble(r[$"lotItemBasePoint0{i}"].Value), Num: Convert.ToInt32(r[$"lotItemNum0{i}"].Value))).Where(x => x.Points > 0).ToList();
        bool flagged = rows.Any(r => Convert.ToInt32(r["getItemFlagId"].Value) > 0);
        bool chainAfterRandom = rows.Count > 1 && rows.Any(r => Weighted(r) is var w && !(w.Count == 1 && Item(w[0])));
        if (flagged || chainAfterRandom)
        {
            result = [];
            foreach (var r in rows)
            {
                var weighted = Weighted(r);
                var entries = weighted.Where(Item).ToList();
                var items = new List<(int Item, int Num, int Reinforce, float Percent)>();
                double total = weighted.Sum(x => x.Points);
                bool sure = weighted.Count == 1 && entries.Count == 1;
                foreach (var x in entries)
                {
                    int reinforce = 0, ds2 = pack != null ? Ds1Drops.MapItem(pack, ds1, x.Cat, x.Id, out _, out reinforce, out _) : x.Id;
                    if (ds2 == 0) { pack?.Note($"item lot {lot} row {r.ID}: DS1 item {x.Id} not in DS2 yet, left out"); continue; }
                    items.Add((ds2, Math.Max(1, x.Num), reinforce, sure ? 100f : (float)(100 * x.Points / total)));
                }
                if (chainAfterRandom && !flagged) pack?.Note($"rule L-chain: item lot {lot} row {r.ID} awarded on its own ({(sure ? "sure" : $"random, {100 * weighted.Where(x => !Item(x)).Sum(x => x.Points) / Math.Max(1, total):0.#} % nothing")})");
                if (items.Count == 0) continue;
                int id = EntityRules.ItemLot(r.ID);
                if (pack != null) WriteLot(id, $"[Lordran] DS1 item lot {r.ID}", items);
                int f = Convert.ToInt32(r["getItemFlagId"].Value);
                result.Add((id, f > 0 ? Flag(f) : null));
            }
        }
        return lotRows[lot] = result;
    }

    void WriteLot(int id, string name, List<(int Item, int Num, int Reinforce, float Percent)> items)
    {
        var other = pack!.Param("ItemLotParam2_Other");
        var row = pack.AddRow("ItemLotParam2_Other", other[60042000]!, id, name);
        bool sure = items.All(x => x.Percent >= 100);
        items = items.Take(10).ToList();
        for (int i = 1; i <= 10; i++)
        {
            var it = i <= items.Count ? items[i - 1] : default;
            bool used = i <= items.Count;
            Set(row, $"itemId{i}", used ? it.Item : 10);
            Set(row, $"itemNum{i}", used ? it.Num : 0);
            Set(row, $"reinforceLevel{i}", used ? it.Reinforce : 0);
            Set(row, $"attribute{i}", 0);
            Set(row, $"bExFlag{i}", used ? 1 : 0);
            Set(row, $"itemWeight{i}", used ? (sure ? 1f : it.Percent) : 0f);
        }
        Set(row, "lotCount", sure ? items.Count : 1);
        pack.Note($"{name} → ItemLotParam2_Other {id}: " + string.Join(", ", items.Select(x => $"{x.Item} x{x.Num} {(sure ? "sure" : $"{x.Percent:0.#}%")}")));
    }

    // ---------------------------------------------------------------- other maps
    /// <summary>DS1 Firelink (m10_02) start point 1022100 = MapLinks' Firelink arrival (DS2 m19_02_00_00, verified s5-37);
    /// any other DS1 Firelink region a warp names (the nest's landing 1022121) = MapLinks' point for it
    /// (<see cref="MapLinks.WarpTargets"/>, id <see cref="AsylumScript.RegionId"/>), when Firelink is built.</summary>
    public (int Map, int Point)? MapPoint(int area, int block, int point)
    {
        if (!firelink || area != 10 || block != 2) return null;
        if (point == 1022100) return (MapBuilder.Firelink.MapIdDecimal, MapLinks.FirelinkArrival);
        return MapLinks.WarpTargets().Any(t => t.Entity == point) ? (MapBuilder.Firelink.MapIdDecimal, AsylumScript.RegionId(point)) : null;
    }

    static void Set(PARAM.Row row, string field, object v) => row[field].Value = Convert.ChangeType(v, row[field].Value.GetType());
}
