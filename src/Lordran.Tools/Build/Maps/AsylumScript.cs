using System.Numerics;
using SoulsFormats;
using WE = Lordran.Tools.Build.Events.WorldEvents;
using Ev = Lordran.Tools.Build.Events;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// The Undead Asylum's DS1 event script (m18_01_00_00.emevd: `lr.cmd emevd-dump`) rebuilt with DS2's means (session 7).
///
/// DS1 → DS2 vocabulary (found in vanilla DS2 scripts; `lr.cmd edd-machines`, `esd-usage`, `obj-states`):
///  - event-driven objects (DS1 "Force Animation Playback" on doors/gates) = prefab objects: prefabeditor/MODEL.pfbbin
///    is the object's state machine, MapObjectGDParam.isPrefab = 1; events request states with 131636(instance, state)
///    and test them with f131653(instance, state, 0). Cloned from o10_1001 (FoFG bars: 10 initial → 70 plays anim
///    10001 → 30 → 80 plays anim 10002 → 10) and o10_9240 (FoFG boulder: 10 → 70 plays anim 1 → 20);
///  - examining something = a key-guide object + 140205(group, instance) (Majula's obelisk helper o04_9300: an invisible
///    prefab object showing KeyGuideParam text); 131638(instance, 1) switches its key guide off;
///  - DS1 ObjAct key doors = DS2 doors + an event owned by the door calling 131602(region, item, usedText, lockedText,
///    flag, 0) (FoFG preset 【Lib】アイテム指定扉開錠; region = only openable from inside: 【Lib】領域指定扉開錠);
///  - fog walls ("Light Wall" o8950/8952: an invisible DS1 box + map SFX) = DS2 white doors o00_0501 (5 m; DS1's are
///    4.07 m) with MapObjectWhiteDoorParam rows (category 1 = boss fog, passFlag = boss killed flag; 5 = two-way);
///    shown/hidden with 130702(instance, 1|0);
///  - DS1 event flags 118xxxxx → DS2 per-map flags 1 BB nnnnnn (FoFG 110000081, TB 102000030): the Asylum's are
///    101000nnn with nnn = DS1's last three digits (16 = boss killed → 101000016).
/// Instance ids: AABB0000 + the DS1 entity id's last four digits (o8541 hall door 1811111 → 18011111).
///
/// Session 8: the hand-written groups for the key doors (37310-37313) and the courtyard fog (37320) are gone: DS1's own
/// 11810100 (slots 3-6) and 11810090 translate by rule (Events.EmevdTranslator: ObjAct key doors, white-door crossing).
/// The hall door's ObjAct group (37330; session 10: 37331, the far door o8544 comes first in the MSB = 37330) is now generated from DS1's MSB ObjAct + ObjActParam by the ObjAct rule
/// (<see cref="ObjActGroups"/>). Hand-written still: the boulder (37340), see there. Fix pass: DS1's action-button areas
/// (the crow's nest) get key-guide helpers by rule (<see cref="AreaButtons"/>), so DS1's nest event 11810150 translates.
/// </summary>
public static class AsylumScript
{
    public static int Inst(int ds1Entity) => 18010000 + ds1Entity % 10000;

    // ---- objects (DS1 entity → instance)
    public static readonly int HallDoor = Inst(1811111);      // o8541 "church door (large)" between the courtyard and the hall
    public static readonly int Gate = Inst(1811115);          // o8542: the escape gate on the hall's east side
    public static readonly int FarDoor = Inst(1811110);       // o8544 "church door (small)" to the cliff: Big Pilgrim's Key
    public static readonly int Boulder = Inst(1811215);       // o8530: the rolling iron ball
    public static readonly int BossFog = Inst(1811990);       // o8950: the boss fog on the balcony (return trip)
    public static readonly int CourtyardFog = Inst(1811700);  // o8952: the fog before the courtyard
    public static readonly int PitFog = Inst(1811890);        // o8953: the Stray Demon pit's fog (return trip)
    public const int HallDoorGuide = 18011119;                // invisible "Open" helper in front of the hall door
    public const int HallDoorGuide2 = 18011118;               // a second one for DS1 action buttons (11810313); both face like the door (verified: prompts from the courtyard)
    public static readonly int CellDoor = Inst(1811103);

    // ---- event flags: the universal rule (Events.FlagRules; session 9 npcflags: persistent DS1 flags → DS2 global slots,
    // temporary 1181 5nnn → m18_01's map-slot block)
    static int Fl(int ds1) => Lordran.Tools.Build.Events.FlagRules.Map("m18_01", ds1)!.Value;
    public static readonly int FBossDead = Fl(16), FBoulder = Fl(11810211);

    // ---- EventLocation ids: every DS1 region as 3701000 + entity % 1000; rule-made boxes (collision bounds 3702000 + …,
    // white-door crossing boxes 3703000 + …) are added to ExtraRegions by Events.AsylumContext while translating
    public static int RegionId(int ds1Entity) => 3701000 + ds1Entity % 1000;
    public static readonly HashSet<int> RegionIds = [];
    public static readonly List<(int Id, Vector3 Pos, Vector3 Rot, Vector3 Size, bool Point)> ExtraRegions = [];
    public static readonly int RBoulder = RegionId(1812120);

    // ---- regulation rows
    public const int WdBoss = 18011990, WdCourtyard = 18011700, WdPit = 18011890;  // MapObjectWhiteDoorParam
    public const int KgHallDoor = 18011111;                       // KeyGuideParam: "Open" (both doors) at the helper

    /// <summary>DS1 objects ported as prefab objects (placed by <see cref="Placements"/>). The o10_1001 state machine
    /// plays 10001 on 70 and 10002 on 80: DS1's open animation becomes 10001, its close animation 10002.</summary>
    public static ObjPorter.Options[] Objects =>
    [
        new("o8541", "o18_8541", HallTemplate, [("a00_0001", "a00_01_0001"), ("a00_0003", "a00_01_0002")], ObjPorter.Kind.Prefab),
        new("o8542", "o18_8542", HallTemplate, [("a00_0001", "a00_01_0001"), ("a00_0003", "a00_01_0002")], ObjPorter.Kind.Prefab),
        new("o8530", "o18_8530", "o10_9240", [("a00_0000", "a00_00_0001")], ObjPorter.Kind.Prefab),
        // session 10 (rule WE1): the hall's floor centre o8510 (was a Static here) and every other object part DS1's
        // events address is placed by the world-event rule (WorldParts / WorldPorts): o8510 + o8520 Breakable, o8511 /
        // o8521 / o8531 Static, the boulder's rest copy o8530_0001 as the boulder's prefab
        // session 10 (rule K1, doors.md rev. 2): every other DS1 ObjAct part of MSB type Default/Loop is a prefab door on
        // the K10′ template (the far door o8544 → o18_8544 on o15_1060), its ObjAct anim the template's open clip
        .. PrefabActPorts(),
        .. (JarControl ? RollTestObjects : []),
    ];

    /// <summary>The template of the V hand-ported hall door / escape gate (session 7, o10_1001). TEST ONLY: build-pack
    /// --hall-door-template o15_1060 (COVERAGE CV5: does DS2 honour a direct request of the rest state 30 on them?).</summary>
    public static string HallTemplate { get; set; } = "o10_1001";

    /// <summary>
    /// Rule K1 + K12 (session 10): a DS1 ObjAct part of MSB type Default/Loop (not a chest): the prefab model, the ObjAct's
    /// anim (ObjActParam.objAnimId) and dummy (objDummyId), its helper instance (the hall door keeps the V 18011119, the
    /// others <see cref="Events.EntityRules.ObjActHelper"/>).
    /// </summary>
    public sealed record PrefabAct(int Entity, string PartName, string Ds1Model, string Ds2Model, int AnimId, int DummyId, int Helper, Ev.ObjActRules.Req Req)
    {
        /// <summary>Fix pass e2 (finding 3, rule K6 prompt): the helper model and its KeyGuideParam row, one per DS1 prompt
        /// (ObjActParam actionEnableMsgId, validDist, validPlayerAngle), see <see cref="PromptGuides"/>.</summary>
        public string HelperModel { get; init; } = "o18_9300";
        public int KeyGuide { get; init; } = KgHallDoor;
    }
    static List<PrefabAct>? prefabActs;
    public static List<PrefabAct> PrefabActs()
    {
        if (prefabActs != null) return prefabActs;
        var msb1 = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", "m18_01_00_00.msb"));
        var param = Ds1Source.LoadParam(Program.DefaultDsrRoot, "ObjActParam")!;
        var list = new List<PrefabAct>();
        foreach (var a in msb1.Events.ObjActs)
        {
            if (Ev.ObjActRules.Of(msb1, param, a) is not { } req || req.Part.EntityID <= 0) continue;
            if (Ev.ObjActRules.KindOf(a, req.Part) != Ev.ObjActRules.Kind.Prefab) continue;
            int e = req.Part.EntityID;
            list.Add(new PrefabAct(e, req.Part.Name, req.Part.ModelName, "o18_" + req.Part.ModelName[1..], req.AnimId, req.DummyId,
                e == 1811111 ? HallDoorGuide : Ev.EntityRules.ObjActHelper(e), req));
        }
        var guides = PromptGuides(list);
        return prefabActs = list.Select(p => p with { HelperModel = guides[Key(p.Req.Prompt)].Model, KeyGuide = guides[Key(p.Req.Prompt)].KeyGuide }).ToList();
    }

    static (int, float, int) Key(Ev.ObjActRules.Prompt p) => (p.Msg, p.Dist, p.Angle);

    /// <summary>
    /// Rule K6 prompt (fix pass e2, finding 3): one helper model + KeyGuideParam per DS1 prompt (actionEnableMsgId,
    /// validDist, validPlayerAngle) of the K1 prefab ObjActs. The prompt of the V hall door (ObjActParam 8541: 10010400
    /// "Open", 70 cm, 60°) keeps its session-7 helper o18_9300 / KgHallDoor 18011111 (Majula's obelisk guide shape: a
    /// 2.5 m box in front of dummy 190, angle 80°: verified in game, wider than DS1's 0.7 m); the far door o8544 has the
    /// same prompt (ObjActParam 8544 = 8541's three fields). Any other prompt (levers 10010501 "Pull lever" 90 cm 45°,
    /// coffins …) gets model o18_93{50+k} + KeyGuideParam 18012000 + k: DS2's torch sphere 1110 (both sides, 180°) with
    /// radius = validDist and the text by the action-button rule (<see cref="KeyGuideText"/>). DS1's facing cone and
    /// player animation (playerAnimId) have no counterpart on this route (deviations, rulebook §3 K6).
    /// </summary>
    public static Dictionary<(int, float, int), (string Model, int KeyGuide)> PromptGuides(List<PrefabAct> acts)
    {
        var hall = acts.FirstOrDefault(p => p.Entity == 1811111)?.Req.Prompt;
        var result = new Dictionary<(int, float, int), (string, int)>();
        int k = 0;
        foreach (var key in acts.Select(p => Key(p.Req.Prompt)).Distinct())
            result[key] = hall != null && key == Key(hall) ? ("o18_9300", KgHallDoor) : ($"o18_93{50 + k:D2}", 18012000 + k++);
        return result;
    }

    /// <summary>The DS2 key-guide text of a DS1 help message (the action-button rule, session 8 fix pass): DS2's keyguide
    /// entry with the same English action ("⑲:Open" = 200 for DS1 10010400 "Open"), else DS1's text after DS2's button
    /// glyph as a new entry 18000000 + message % 1000000.</summary>
    public static (int TextId, string Text) KeyGuideText(Pack pack, Ds1Source ds1, int message)
    {
        string text = ds1.Text("Event_text_", message);
        if (string.IsNullOrEmpty(text)) text = ds1.AnyText(message) ?? $"[DS1 {message}]";
        var kgFmg = pack.Text("keyguide");
        string glyph = kgFmg.Entries.First(e => e.ID == 100).Text.Split(':')[0] + ":";
        var same = kgFmg.Entries.Where(e => e.Text?.Trim() == glyph + text.Trim()).OrderBy(e => e.ID).FirstOrDefault();
        int textId = same?.ID ?? 18000000 + message % 1000000;
        if (same == null) pack.SetText("keyguide", textId, glyph + text.Trim());
        return (textId, text);
    }

    static ObjPorter.Options[]? prefabActPorts;
    /// <summary>Fix pass e2: how <see cref="PrefabActPorts"/> chose the rest clips (Params writes it to the build log).</summary>
    public static readonly List<string> RestClipLog = [];
    /// <summary>Rule K1 ports: one per DS1 model of a <see cref="PrefabActs"/> part the hand list doesn't port (o8544),
    /// template by rule K10′ (<see cref="Ev.ObjActRules.DoorTemplate"/>), DS1's ObjAct anim → the template's open clip
    /// a00_01_0001 (state 70 → 30); another anim DS1's events play on parts of that model → the close clip a00_01_0002;
    /// none (o8544: DS1 has no close clip) → the model's closed rest pose (fix pass e2, <see cref="Ev.ObjActRules.RestClip"/>:
    /// o8544 a00_0000) as a00_01_0002, which the closed state 10 holds.</summary>
    static ObjPorter.Options[] PrefabActPorts()
    {
        if (prefabActPorts != null) return prefabActPorts;
        string[] hand = ["o8541", "o8542", "o8530"];
        var defs = Commands.EmevdDump.Defs();
        var emevd = EMEVD.Read(Path.Combine(Program.DefaultDsrRoot, "event", "m18_01_00_00.emevd.dcx"));
        var ports = new List<ObjPorter.Options>();
        foreach (var g in PrefabActs().Where(p => !hand.Contains(p.Ds1Model)).GroupBy(p => p.Ds1Model))
        {
            var ents = g.Select(p => p.Entity).ToHashSet();
            int open = g.First().AnimId;
            var other = emevd.Events.SelectMany(ev => ev.Instructions).Where(x => (x.Bank, x.ID) is (2003, 18) or (2005, 7))
                .Select(x => Ev.EmevdTranslator.Decode(defs, x)).Where(v => ents.Contains((int)v[0]) && (int)v[1] != open).Select(v => (int)v[1]).Distinct().ToList();
            var anims = new List<(string, string)> { ($"a00_{open:D4}", "a00_01_0001") };
            if (other.Count > 0) anims.Add(($"a00_{other[0]:D4}", "a00_01_0002"));
            // fix pass e2 (finding 2): no close anim → the model's closed rest pose (ObjActRules.RestClip: o8544 a00_0000) as
            // the close clip, so the template's closed state 10 (holds 10002's end frame) shows DS1's closed door
            else if (Ev.ObjActRules.RestClip(Program.DefaultDsrRoot, g.Key, open, RestClipLog) is string rest) anims.Add((rest, "a00_01_0002"));
            ports.Add(new ObjPorter.Options(g.Key, g.First().Ds2Model, Ev.ObjActRules.DoorTemplate(Commands.ExtractDs2.DefaultOut), [.. anims], ObjPorter.Kind.Prefab));
        }
        return prefabActPorts = [.. ports];
    }

    /// <summary>
    /// Rule C11 (session 10): per prefab template, DS2 animation id → (the state that plays it, the state it rests in
    /// afterwards). From the templates' state machines (data\scratch\prefab-states.txt; pfbbin survey
    /// data\sandbox\asy-events\scratch\pfb_survey.py: o10_1001 plays 10001 in 70 → 30 and 10002 in 80 → 10, its rest states
    /// hold the clip's end pose with 5008(anim, 16, 0); o10_9240 plays 1 in 70 → 20, 5008(1, 16, 0)).
    /// </summary>
    public static readonly Dictionary<string, Dictionary<int, (int Play, int After)>> PrefabStates = new()
    {
        ["o10_1001"] = new() { [10001] = (70, 30), [10002] = (80, 10) },
        ["o10_9240"] = new() { [1] = (70, 20) },
        // session 10 (rule K10′): the gimmick door template, same states and anim records as o10_1001 (pfbbin 0x880:
        // 70 → (1, 10001), 30 → (10001, 16), 80 → (1, 10002), 10 → (10002, 16); asy10 pfb-o10_1001-o15_1060.txt) and request
        // records for 70, 30, 80, 10 (o10_1001: 70, 80)
        ["o15_1060"] = new() { [10001] = (70, 30), [10002] = (80, 10) },
    };

    /// <summary>The prefab port of a DS1 model (Kind.Prefab in <see cref="Objects"/>), or null.</summary>
    public static ObjPorter.Options? PrefabPort(string ds1Model) => Objects.FirstOrDefault(o => o.Kind == ObjPorter.Kind.Prefab && o.Ds1Obj == ds1Model);

    /// <summary>Rule C11: (play state, rest state, template) of DS1 animation a00_ANIM on a part whose model is a prefab
    /// port: the port names the DS2 clip (a00_AA_BBBB → AnimList id AA·10000 + BBBB), the template's table the states.</summary>
    public static (int Play, int After, string Template)? PrefabAnim(string ds1Model, int anim)
    {
        if (PrefabPort(ds1Model) is not { } port) return null;
        var clip = port.Anims.FirstOrDefault(a => a.Ds1Anim.Equals($"a00_{anim:D4}", StringComparison.OrdinalIgnoreCase)).Ds2Anim;
        if (clip == null || clip.Length < 11) return null;
        int id = int.Parse(clip[4..6]) * 10000 + int.Parse(clip[7..11]);
        return PrefabStates.TryGetValue(port.TemplateObj, out var t) && t.TryGetValue(id, out var s) ? (s.Play, s.After, port.TemplateObj) : null;
    }


    /// <summary>Entities the Asylum's script places itself (prefabs, white doors, the never-opening cells).</summary>
    /// <summary>The objects placed one by one above (session 7 hand list).</summary>
    static readonly int[] ScriptedHand = [1811111, 1811115, 1811215];
    public static HashSet<int> ScriptedEntities =>[1811111, 1811115, 1811215, 1811990, 1811700, 1811890, .. NeverOpen, .. PrefabActs().Select(p => p.Entity)];
    /// <summary>DS1 models the map builder places at every part by their own rule (MapBuilder.Asylum: cell doors o8540,
    /// ladders o8500/o8501) or the decoration rule.</summary>
    static bool ModelRule(string m) => m is "o8540" or "o8500" or "o8501" || MapBuilder.IsDecorModel(m);

    static List<WE.Part>? worldParts;
    /// <summary>Rule WE1 for the Asylum: the event-addressed object parts no other rule places (see WorldEvents.Parts).</summary>
    public static List<WE.Part> WorldParts(List<string>? log = null)
    {
        if (worldParts != null && log == null) return worldParts;
        var msb1 = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", "m18_01_00_00.msb"));
        var prefabs = Objects.Where(o => o.Kind == ObjPorter.Kind.Prefab).ToDictionary(o => o.Ds1Obj, o => o.Ds2Name);
        var other = msb1.Models.Objects.Select(m => m.Name).Where(ModelRule).ToHashSet();
        return worldParts = WE.Parts(Program.DefaultDsrRoot, "m18_01_00_00", "m18_01_00_00", msb1, prefabs, other, ScriptedEntities, log);
    }

    /// <summary>Rule WE1: the ports of the non-prefab models <see cref="WorldParts"/> places (one per model).</summary>
    public static ObjPorter.Options[] WorldPorts => WorldParts().Where(p => p.Kind != WE.Kind.Prefab).GroupBy(p => p.Ds2Model)
        .Select(g => new ObjPorter.Options(g.First().Ds1Model, g.Key, WE.Template(g.First().Kind)!, [],
            g.First().Kind switch { WE.Kind.Breakable => ObjPorter.Kind.Breakable, WE.Kind.Static => ObjPorter.Kind.Static, _ => ObjPorter.Kind.Scenery }))
        .ToArray();

    /// <summary>Parts the script places one by one (the map builder's per-model loops skip them).</summary>
    public static HashSet<int> ScriptPlaced => [.. NeverOpen, .. WorldParts().Select(p => p.Entity)];

    /// <summary>Rule WE4 (session 10): the SystemDamageParam / DamageCtrlParam row id (rule E-ID2 since session 12; was the instance) of a scripted or WE1
    /// object that carries DS1's damaging-object behaviour (2005:9), or null.</summary>
    public static int? DamageRow(int ds1Entity) =>
        WE.Damaging(Program.DefaultDsrRoot, "m18_01_00_00", ds1Entity) != null
        && (ScriptedEntities.Contains(ds1Entity) || WorldParts().Any(p => p.Entity == ds1Entity)) ? Chr.Ds1AttackParams.ObjectDamageRow(ds1Entity) : null;   // session 12 (ehit12), rule E-ID2: System range (was Inst(entity): the Player range)

    /// <summary>Test build switch (session 8, track potroll): LR_JAR_CONTROL=1 adds the roll-break A/B lanes.</summary>
    public static bool JarControl => Environment.GetEnvironmentVariable("LR_JAR_CONTROL") == "1";

    /// <summary>The A/B's test variants of DS1's church urn o8410 (see <see cref="ObjPorter.TestVariant"/>), each its own
    /// model (MapObject*Param rows copied from the jar o00_2100 like o18_8410's: objType 1906). o18_849x: DS1 has no
    /// o849x objects and DS2 no o18_849x.</summary>
    static ObjPorter.Options[] RollTestObjects =>
    [
        new("o8410", "o18_8491", "o00_2100", [], ObjPorter.Kind.Breakable) { Variant = ObjPorter.TestVariant.LegacyHavok },
        new("o8410", "o18_8492", "o00_2100", [], ObjPorter.Kind.Breakable) { Variant = ObjPorter.TestVariant.Ds2IntactBody },
        new("o8410", "o18_8493", "o00_2100", [], ObjPorter.Kind.Breakable) { Variant = ObjPorter.TestVariant.NoFragmentBase },
    ];

    /// <summary>
    /// Roll-break A/B layout (session 8, track potroll): six parallel lanes in the courtyard below the hall door's
    /// steps, each running +Z from the walk row z = <see cref="RollRowZ"/> past an approach point at z
    /// <see cref="RollApproachZ"/> to its target at z <see cref="RollTargetZ"/> (1.5 m ahead: the roll meets it
    /// mid-roll). Checked with lr.cmd lane-check on the LR_JAR_CONTROL pack (DS1 walkable collision, walls in the body
    /// band, object footprints, radius 0.45): each lane (x, 2.0..7.0) is clear by ≥ 0.67 m with its own target excepted
    /// (the other lanes' targets in place) and blocked only by its own target otherwise; floor 195.86..196.19; the walk
    /// row (-3, 2.2)..(9.5, 2.2) clear by 0.87 m; the steps from test warp 3700150 (3.19, -1.0) down to (3.19, 2.2)
    /// clear by 1.84 m. Lane x -3 has a low obstacle at (-2.75, 7.8): walk into its urn for at most ~0.7 s. The first
    /// layout (hall east passage, x = 11.5) ran through our urn o18_8410_0036 (reviewer, session 8).
    /// Lane x −3: DS1 urn o8403 (DS1 isBreakByPlayerCollide 1 → objType 1150: breaks when walked into); −1: DS2's own
    /// jar o00_2100 (control); 1: our church urn o18_8410 as converted; 5.5 / 7.5 / 9.5: the test variants.
    /// </summary>
    public static (float X, string Model, string Note)[] RollLanes =>
    [
        (-3.0f, "o18_8403", "DS1 urn o8403, objType 1150: walk into it (no roll), it must break"),
        (-1.0f, "o00_2100", "vanilla DS2 jar (control)"),
        (1.0f, "o18_8410", "DS1 church urn o8410 as converted"),
        (5.5f, "o18_8491", "variant LegacyHavok (pre-session-8 collision conventions)"),
        (7.5f, "o18_8492", "variant Ds2IntactBody (DS2 jar's intact body)"),
        (9.5f, "o18_8493", "variant NoFragmentBase (fragment base plate not instantiated)"),
    ];

    public const float RollRowZ = 2.2f, RollApproachZ = 3.5f, RollTargetZ = 5.0f;

    /// <summary>Floor under each lane's target (DS1 walkable collision at z 5.0, lane-check --profile).</summary>
    static float RollLaneFloor(float x) => x switch { -3.0f => 195.93f, -1.0f => 195.99f, 1.0f => 195.99f, 5.5f => 196.01f, 7.5f => 196.04f, _ => 195.93f };

    /// <summary>DS1 cell doors whose ObjAct DS1's map-start event disables (1811100..02: other prisoners' cells):
    /// placed as plain objects that never open.</summary>
    public static HashSet<int> NeverOpen => [1811100, 1811101, 1811102];

    /// <summary>TEST ONLY (build-pack --test-flags): DS1 flags the Asylum's script sets at every load. Session 10: same
    /// option and group as asy-events' (C22 shared test infrastructure); asy-items i2 adds "-F" = clear F (rule K1 tests:
    /// the post-early-kill state 11810301 ON, 50000081 / 50001661 OFF, 50001660 ON).</summary>
    public static readonly List<int> TestFlags = [];

    public static List<MapBuilder.Placement> Placements(MSB1 msb1)
    {
        MSB1.Part.Object Part(int entity) => msb1.Parts.Objects.First(p => p.EntityID == entity);
        var list = new List<MapBuilder.Placement>();
        void Add(string model, int entity, int inst, MapBuilder.PlacementKind kind, int ctrl = 0, string note = "", Dictionary<string, object>? fields = null)
        {
            var p = Part(entity);
            list.Add(new MapBuilder.Placement(model, p.Position, p.Rotation, inst, kind, ctrl, $"(DS1 {p.Name}, entity {entity}) {note}") { Fields = fields ?? [] });
        }
        Add("o18_8541", 1811111, HallDoor, MapBuilder.PlacementKind.Generic, note: "hall door (prefab)");
        Add("o18_8542", 1811115, Gate, MapBuilder.PlacementKind.Generic, note: "escape gate (prefab)");
        // the boulder: its own instance row deals DS1's damage while it rolls (session 10, rule WE4: damageId0 = its own
        // SystemDamageParam row from DS1's 2005:9; was FoFG's rolling rock 210100030, whose hit dummy 200 our model lacks)
        Add("o18_8530", 1811215, Boulder, MapBuilder.PlacementKind.Generic, note: "boulder (prefab)", fields: new() { ["damageId0"] = DamageRow(1811215) ?? 210100030 });
        // session 10 (rule WE1): every other object part DS1's events address (the hall floor o8510 + its copy and
        // under-layer, Oscar's wall o8520 + copy + o8521, the boulder's rest copy o8530_0001 + rubble o8531), with WE2's
        // resetType 0 and WE3's instance type in the row; DS1's own events show/hide/break them
        foreach (var w in WorldParts())
        {
            var f = new Dictionary<string, object>(w.Fields);
            if (DamageRow(w.Entity) is int dmg) f["damageId0"] = dmg;
            list.Add(new MapBuilder.Placement(w.Ds2Model, w.Position, w.Rotation, w.Instance, MapBuilder.PlacementKind.Generic, 0,
                $"(DS1 {w.Name}, entity {w.Entity}) world-event object: {w.Note}") { Fields = f, Scale = w.Scale });
        }
        foreach (int e in NeverOpen) Add("o18_8540", e, Inst(e), MapBuilder.PlacementKind.Generic, note: "cell door that never opens");
        // session 10 (rule K1): the other K1 prefab doors (the far door o8544 1811110), placed like the hall door
        foreach (var pa in PrefabActs().Where(p => !ScriptedHand.Contains(p.Entity)))
            Add(pa.Ds2Model, pa.Entity, Inst(pa.Entity), MapBuilder.PlacementKind.Generic, note: $"K1 prefab door (MSB ObjAct Default, template {PrefabPort(pa.Ds1Model)?.TemplateObj})");
        // the "Open" key guides of the K1 prefab doors (session 7: the hall door's, hand-placed +0.9 m world Z; session 10,
        // rule K12: at the part × its ObjActParam dummy (objDummyId 120 on both doors), facing like the part); the hall
        // door has a second helper for DS1's action buttons (11810313)
        var door = Part(1811111);
        foreach (var pa in PrefabActs())
        {
            var (pos, rot, k12) = Ev.ObjActRules.HelperPose(Program.DefaultDsrRoot, Part(pa.Entity), pa.DummyId);
            list.Add(new MapBuilder.Placement(pa.HelperModel, pos, rot, pa.Helper, MapBuilder.PlacementKind.Generic, 0, $"ObjAct key guide of {pa.PartName} ({k12}; prompt KeyGuideParam {pa.KeyGuide})"));
            if (pa.Entity == 1811111)
                list.Add(new MapBuilder.Placement("o18_9300", pos, rot, HallDoorGuide2, MapBuilder.PlacementKind.Generic, 0, $"hall door key guide (action buttons; {k12})"));
        }
        // TEST CONTROL (LR_OBJ_CONTROL=1): vanilla DS2 doors in the courtyard, to tell a model problem from a lighting one
        if (Environment.GetEnvironmentVariable("LR_OBJ_CONTROL") == "1")
        {
            list.Add(new MapBuilder.Placement("o00_0308", door.Position + new Vector3(-3.5f, -0.4f, 5f), Vector3.Zero, 18019001, MapBuilder.PlacementKind.Generic, 0, "TEST vanilla door"));
            list.Add(new MapBuilder.Placement("o00_0471", door.Position + new Vector3(3.5f, -0.4f, 5f), Vector3.Zero, 18019002, MapBuilder.PlacementKind.Generic, 0, "TEST vanilla double door"));
        }
        // TEST CONTROL (LR_JAR_CONTROL=1, session 8, track potroll): the roll-break A/B, see RollLanes
        if (JarControl)
            foreach (var (x, model, note) in RollLanes)
                list.Add(new MapBuilder.Placement(model, new Vector3(x, RollLaneFloor(x), RollTargetZ), Vector3.Zero, 0, MapBuilder.PlacementKind.Generic, 0, $"TEST roll lane x {x}: {note}"));
        // fog walls: DS2 white doors (medium, 5 m) where DS1's light walls stand
        Add("o00_0501", 1811990, BossFog, MapBuilder.PlacementKind.WhiteDoor, WdBoss, "boss fog");
        Add("o00_0501", 1811700, CourtyardFog, MapBuilder.PlacementKind.WhiteDoor, WdCourtyard, "courtyard fog");
        Add("o00_0501", 1811890, PitFog, MapBuilder.PlacementKind.WhiteDoor, WdPit, "Stray Demon pit fog");
        // session 10 (asy-items i2), rule S1: the "Offer" helper of each DS1 drop-trade area (Snuggly's nest)
        list.AddRange(Lordran.Tools.Build.Events.DropTrades.Placements(msb1, Program.DefaultDsrRoot, "m18_01_00_00"));
        // session 8 fix pass: key-guide helpers for DS1's action-button areas (the area rule, see AreaButtons)
        foreach (var b in AreaButtons())
        {
            var r = msb1.Regions.GetEntries().First(x => x.EntityID == b.Area);
            list.Add(new MapBuilder.Placement(b.Model, r.Position, r.Rotation, b.Helper, MapBuilder.PlacementKind.Generic, 0,
                $"(DS1 region {r.Name}, entity {b.Area}) key guide for DS1 action button {b.Message}"));
        }
        return list;
    }

    /// <summary>A DS1 action button on an area: its prompt (DS1 help message), the k-th prompt of that area, the helper
    /// instance (<see cref="Events.EntityRules.AreaHelper"/>), the helper model (o18_93NN, one per prompt text) and its
    /// KeyGuideParam row (AABB0000 + message % 10000, like KgHallDoor).</summary>
    public sealed record AreaButton(int Area, int Message, int K, int Helper, string Model, int KeyGuide);
    static List<AreaButton>? areaButtons;

    /// <summary>
    /// The area rule (session 8 fix pass): every DS1 action button on an area (3:5 / 3:18 with TargetEntityType 1 and no
    /// light-wall line segment; the Asylum: the crow's nest 1812001, "Curl up like a ball" 10010506 and "Stand up"
    /// 10010507) gets an invisible key-guide helper standing at the area (Majula's obelisk helper o04_9300, as the hall
    /// door's, with its own KeyGuideParam: a sphere around the helper like DS2's torch guide 1110, both sides, 180°, radius
    /// = the area's half diagonal + 1 m; text = DS2's keyguide entry with the same English action, else DS1's text after
    /// DS2's button glyph, "⑲:Curl up like a ball"). The
    /// translator shows its prompt only while the player is inside DS1's area (EmevdTranslator, action buttons).
    /// Fog events (a light wall) use the white-door rule instead; parameterised areas (X-arguments) are fog events here.
    /// </summary>
    public static List<AreaButton> AreaButtons()
    {
        if (areaButtons != null) return areaButtons;
        var defs = Commands.EmevdDump.Defs();
        var emevd = EMEVD.Read(Path.Combine(Program.DefaultDsrRoot, "event", "m18_01_00_00.emevd.dcx"));
        var found = new List<(int Area, int Message)>();
        foreach (var x in emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 3 && i.ID is 5 or 18))
        {
            var v = Lordran.Tools.Build.Events.EmevdTranslator.Decode(defs, x);
            if (v.Length < 7 || (int)v[1] != 1 || (int)v[2] <= 0) continue; // areas with a fixed entity
            if (x.ID == 18 && v.Length > 9 && (int)v[9] > 0) continue;        // a light wall: the white-door rule
            found.Add(((int)v[2], (int)v[6]));
        }
        var messages = found.Select(f => f.Message).Distinct().ToList();
        areaButtons = found.Distinct().GroupBy(f => f.Area).SelectMany(g => g.Select((f, k) => new AreaButton(f.Area, f.Message, k,
            Lordran.Tools.Build.Events.EntityRules.AreaHelper(f.Area, k), $"o18_93{1 + messages.IndexOf(f.Message):D2}", 18010000 + f.Message % 10000))).ToList();
        return areaButtons;
    }

    /// <summary>Regulation rows: white doors, the key guide, the helper object.</summary>
    public static void Params(Pack pack, Ds1Source ds1)
    {
        var wd = pack.AddRow("MapObjectWhiteDoorParam", pack.Param("MapObjectWhiteDoorParam")[101000]!, WdBoss, "[Lordran] Asylum boss fog");
        wd["frontKeyGuideParamId"].Value = 401u;   // white door M: "Enter the mist"
        wd["backKeyGuideParamId"].Value = 0u;
        wd["passFlagId"].Value = FBossDead;         // gone once the Asylum Demon is dead
        var wd2 = pack.AddRow("MapObjectWhiteDoorParam", pack.Param("MapObjectWhiteDoorParam")[100]!, WdCourtyard, "[Lordran] Asylum courtyard fog");
        wd2["frontKeyGuideParamId"].Value = 401u;
        wd2["backKeyGuideParamId"].Value = 411u;
        wd2["passFlagId"].Value = 0;

        // the hall door's key guide: Majula's obelisk guide (a box in front of dummy 190) with "Open"
        var wd3 = pack.AddRow("MapObjectWhiteDoorParam", pack.Param("MapObjectWhiteDoorParam")[101011]!, WdPit, "[Lordran] Asylum Stray Demon pit fog");
        wd3["passFlagId"].Value = Fl(11810900);     // gone once the Stray Demon is dead

        // session 10, rule WE4: DS1's damaging objects (2005:9) → their own SystemDamageParam + DamageCtrlParam rows (id =
        // rule E-ID2 ObjectDamageRow since session 12, was the instance), cloned from FoFG's rolling iron ball (210100030 / 200200020): DS1's hit dummy and radius; the
        // object attack class from DS1's atkObj (≥ 100 → DS2 100 "giant", which 1561 walls take; else 40 blow), objDamage
        // = atkObj; the damage value stays FoFG's until the chr track's DS1 damage calibration is merged (open)
        var ds1Msb = MSB1.Read(Path.Combine(ds1.Root, "map", "MapStudio", "m18_01_00_00.msb"));
        foreach (int e in ScriptedEntities.Concat(WorldParts().Select(p => p.Entity)).Distinct())
        {
            if (DamageRow(e) is not int row || WE.Damaging(ds1.Root, "m18_01_00_00", e) is not { } d) continue;
            var sd = pack.AddRow("SystemDamageParam", pack.Param("SystemDamageParam")[210100030]!, row, $"[Lordran] Asylum DS1 damaging object {e}");
            sd["hitDummyPolyId"].Value = Convert.ChangeType(d.Dummy, sd["hitDummyPolyId"].Value.GetType());
            sd["radius"].Value = d.Radius;
            sd["damageCategory"].Value = Convert.ChangeType(row, sd["damageCategory"].Value.GetType());
            var dc = pack.AddRow("DamageCtrlParam", pack.Param("DamageCtrlParam")[200200020]!, row, $"[Lordran] Asylum DS1 damaging object {e}");
            dc["objAttackAttr"].Value = Convert.ChangeType(d.AtkObj >= 100 ? 100 : 40, dc["objAttackAttr"].Value.GetType());
            dc["objDamage"].Value = Convert.ChangeType(d.AtkObj, dc["objDamage"].Value.GetType());
            // session 11 (merge11, boulder defect 1, rule WE4c): DS1's own attack values through the chr track's calibration
            // (Ds1AttackParams: atkPhys × 1.46, atkStam × 13, knockbackDist as knockBackPower) instead of FoFG's 652 / 10
            var notes = new List<string>();
            if (ds1.Params.Params["AtkParam_Npc"][d.AtkId] is { } atkRow) Chr.Ds1AttackParams.DamageFromAtk(atkRow, sd, notes, $"WE4 object {e}");
            else notes.Add($"WE4 object {e}: DS1 AtkParam_Npc {d.AtkId} missing, FoFG's damage kept");
            pack.Note($"rule WE4: DS1 2005:9 on {e} (dummy {d.Dummy}, radius {d.Radius}, lifespan {d.Lifespan} s, AtkParam_Npc {d.AtkId} atkObj {d.AtkObj} atkPhys {d.AtkPhys}) → SystemDamageParam/DamageCtrlParam {row} (objAttackAttr {dc["objAttackAttr"].Value}; damage01 {sd["damage01"].Value}, knockBackPower {sd["knockBackPower"].Value}); {string.Join("; ", notes)}");
            // rule WE4c (defect 2): the object's TAE hit window (the template's 2200 event) = min(DS1 lifespan, the ported clip)
            // and the clip length field = the DS1 clip's frames at 30 fps (FoFG's template said 0-9.97 s / its own length)
            var part = ds1Msb.Parts.Objects.FirstOrDefault(p => p.EntityID == e);
            if (part != null && PrefabPort(part.ModelName) is { } port && port.Anims.Length > 0)
            {
                string taePath = $"timeact/obj/{port.Ds2Name}.tae";
                byte[]? taeBytes = null;
                try { taeBytes = pack.GetFile("gamedata", taePath); } catch (KeyNotFoundException) { }
                var (ds1Anim, ds2Anim) = port.Anims[0];
                float? clip = WE.ClipSeconds(ds1.Root, part.ModelName, int.Parse(ds1Anim[4..]));
                var ids = ds2Anim.Split('_');
                long animId = long.Parse(ids[1]) * 10000 + long.Parse(ids[2]);
                if (taeBytes != null && clip is float cs)
                {
                    var tae = new Formats.Ds2Tae(taeBytes);
                    var evs = tae.Events(animId);
                    float end = Math.Min(d.Lifespan, cs);
                    var changed = new List<string>();
                    for (int k = 0; k < evs.Count; k++)
                        if (evs[k].Type == 2200 && Math.Abs(evs[k].End - end) > 1e-3) { tae.SetEventEnd(animId, k, end); changed.Add($"2200 {evs[k].Start:0.##}-{evs[k].End:0.##} → -{end:0.###} s"); }
                    int frames = (int)Math.Round(cs * 30);
                    int? had = tae.Length(animId);
                    tae.SetLength(animId, frames);
                    pack.AddFile("gamedata", taePath, tae.Bytes);
                    pack.Note($"rule WE4c: {taePath} anim {animId}: {(changed.Count > 0 ? string.Join(", ", changed) : "no 2200 window to change")} (DS1 lifespan {d.Lifespan} s, clip {cs:0.###} s); length {had} → {frames} frames");
                }
                else pack.Note($"rule WE4c: {taePath}: {(taeBytes == null ? "no TAE in the pack" : "DS1 clip not found")}, hit window left as the template's");
            }
        }

        // fix pass e2 (rule K6 prompt, PromptGuides): the text from DS1's actionEnableMsgId (10010400 → keyguide 200 "Open",
        // as session 7 hand-set it); the hall door's prompt keeps its V shape, any other DS1 prompt gets DS2's sphere 1110
        // with radius = DS1's validDist
        var hallPrompt = PrefabActs().First(p => p.Entity == 1811111).Req.Prompt;
        var (hallText, _) = KeyGuideText(pack, ds1, hallPrompt.Msg);
        var kg = pack.AddRow("KeyGuideParam", pack.Param("KeyGuideParam")[10049040]!, KgHallDoor, "[Lordran] Asylum hall door: Open");
        kg["textId"].Value = hallText;
        kg["width_radius"].Value = 2.5f;
        ObjPorter.CloneDs2(pack, "o04_9300", "o18_9300", 10049040, KgHallDoor);
        foreach (var g in PrefabActs().GroupBy(p => p.KeyGuide))
        {
            var p0 = g.First();
            var pr = p0.Req.Prompt;
            if (p0.KeyGuide != KgHallDoor)
            {
                var (textId, text) = KeyGuideText(pack, ds1, pr.Msg);
                var row = pack.AddRow("KeyGuideParam", pack.Param("KeyGuideParam")[1110]!, p0.KeyGuide, $"[Lordran] Asylum ObjAct prompt {pr.Msg}: {text}");
                row["textId"].Value = textId;
                row["width_radius"].Value = pr.Dist;
                ObjPorter.CloneDs2(pack, "o04_9300", p0.HelperModel, 10049040, (uint)p0.KeyGuide);
            }
            pack.Note($"rule K6 prompt: DS1 ObjActParam prompt {pr.Msg} ({pr.Dist:0.##} m, {pr.Angle}°, player anim {pr.PlayerAnim}: not played, deviation) → helper {p0.HelperModel} KeyGuideParam {p0.KeyGuide}"
                + $" (text {pack.Param("KeyGuideParam")[p0.KeyGuide]!["textId"].Value}{(p0.KeyGuide == KgHallDoor ? ", the V hall-door shape" : $", sphere r {pr.Dist:0.##} m")}) for {string.Join(", ", g.Select(p => p.PartName))}");
        }
        foreach (var l in RestClipLog) pack.Note(l);

        // session 8 fix pass: the action-button areas' helpers (AreaButtons): one model + KeyGuideParam per prompt text
        var msb1 = MSB1.Read(Path.Combine(ds1.Root, "map", "MapStudio", "m18_01_00_00.msb"));
        // session 10 (asy-items i2), rule S1: the drop-trade "Offer" helpers' models and KeyGuideParam rows
        Lordran.Tools.Build.Events.DropTrades.Params(pack, ds1, msb1, "m18_01_00_00", 1801);
        foreach (var g in AreaButtons().GroupBy(b => b.Message))
        {
            var b0 = g.First();
            float radius = g.Max(b => msb1.Regions.GetEntries().First(r => r.EntityID == b.Area).Shape is MSB.Shape.Box box
                ? MathF.Sqrt(box.Width * box.Width + box.Depth * box.Depth) / 2 + 1f : 2f);
            // DS2 key guides read "<button glyph>:<action>" (100 "⑲:Examine", 200 "⑲:Open"); DS1's help message is the action
            var (textId, text) = KeyGuideText(pack, ds1, b0.Message);
            var row = pack.AddRow("KeyGuideParam", pack.Param("KeyGuideParam")[1110]!, b0.KeyGuide, $"[Lordran] Asylum action button {b0.Message}: {text}");
            row["textId"].Value = textId;
            row["width_radius"].Value = radius;
            ObjPorter.CloneDs2(pack, "o04_9300", b0.Model, 10049040, (uint)b0.KeyGuide);
            pack.Note($"action-button area rule: DS1 prompt {b0.Message} \"{text}\" → helper {b0.Model} (KeyGuideParam {b0.KeyGuide}, keyguide text {textId}, sphere r {radius:0.##} m) at {string.Join(", ", g.Select(b => $"{b.Area} → instance {b.Helper}"))}");
        }
    }

    /// <summary>
    /// EventLocation rows from DS1's regions (session 8: every shape, with DS1's rotation, so warp targets face as in
    /// DS1): boxes as DS1 gives them (position = bottom centre, the convention the vanilla boss boxes suggest: m10_23's
    /// 800000 sits 1 m below the arena floor), points as points, spheres/cylinders as boxes around them (a sphere: from
    /// r below its centre, 2r + 2 m high, so the floor is inside whichever box convention DS2 uses); plus the rule-made
    /// boxes in <see cref="ExtraRegions"/>.
    /// </summary>
    public static IEnumerable<(int Id, Vector3 Pos, Vector3 Rot, Vector3 Size, bool Point)> Regions(MSB1 msb1)
    {
        foreach (var r in msb1.Regions.GetEntries().Where(r => r.EntityID > 0))
        {
            (Vector3 Pos, Vector3 Size, bool Point)? s = r.Shape switch
            {
                MSB.Shape.Box b => (r.Position, new Vector3(b.Width, b.Height, b.Depth), false),
                MSB.Shape.Point => (r.Position, Vector3.Zero, true),
                MSB.Shape.Sphere sp => (r.Position - new Vector3(0, sp.Radius, 0), new Vector3(2 * sp.Radius, 2 * sp.Radius + 2, 2 * sp.Radius), false),
                MSB.Shape.Cylinder c => (r.Position, new Vector3(2 * c.Radius, c.Height, 2 * c.Radius), false),
                _ => null,
            };
            if (s is not { } v) continue;
            RegionIds.Add(RegionId(r.EntityID));
            yield return (RegionId(r.EntityID), v.Pos, r.Rotation, v.Size, v.Point);
        }
        foreach (var x in ExtraRegions.ToList()) yield return x;
    }

    // ================= events =================
    static readonly byte[] True = [0x41, 0xA1];
    static byte[] Int(int v) => v is >= -64 and < 64 ? [(byte)(0x40 + v), 0xA1] : [0x82, .. BitConverter.GetBytes(v), 0xA1];
    static byte[] Push(int v) => v is >= -64 and < 64 ? [(byte)(0x40 + v)] : [0x82, .. BitConverter.GetBytes(v)];
    /// <summary>f140001(n): group condition n met.</summary>
    static byte[] Group(int n) => [0x82, .. BitConverter.GetBytes(140001), .. Push(n), 0x85, 0xA6, 0xA1];
    /// <summary>f6(flag) != 0 / == 0.</summary>
    static byte[] Flag(int flag, bool on = true) => [0x46, .. Push(flag), 0x85, 0xA6, .. Push(0), on ? (byte)0x96 : (byte)0x95, 0xA1];
    static ESD.CommandCall C(int id, params int[] args) => new(1, id, args.Select(Int).ToArray());
    static ESD.State S(IEnumerable<ESD.CommandCall> entry, params (byte[] If, long Then)[] conds)
    {
        var s = new ESD.State();
        s.EntryCommands.AddRange(entry);
        foreach (var (c, t) in conds) s.Conditions.Add(new ESD.Condition(t, c));
        return s;
    }

    /// <summary>
    /// The ObjAct rule (translation-rules §3 "ObjAct (player acts on an object)", verified on the hall door in session 7)
    /// as data, not hand-written: every DS1 MSB ObjAct without an ObjActParam requirement (spQualifiedType 0) on a placed
    /// prefab object with an ObjAct helper → one group: wait for the helper to be examined (140205) → key guide off,
    /// the object's state for the ObjActParam's objAnimId (8541: anim 1 = open → 70), DS1's ObjAct flag (the ObjAct entity,
    /// 11810111) set; once per map load (DS1's ObjAct re-arms on load). DS1's map-start event switches the helper off
    /// when the ObjAct is disabled (event 0: Set ObjAct State → 131638), so no extra condition is needed.
    /// </summary>
    /// <remarks>
    /// Session 10, rule K6 (doors.md rev. 2): the requirement of the ObjAct (ObjActParam) on the helper route, tested in
    /// the frame of the examine (vanilla test idiom `if f140001(n) → A; if 1 → B`, rule G1): goods (type 1) → 140801(1, DS2
    /// item, 1, 3, 1, 1, 0) 「所持アイテム数比較」 (vanilla's "holds it" form, m10_10 group 2147483554), an OR alternative
    /// (type2 1) a second slot joined with ||; SpEffect side test (type 2) → 140132(1, the K3 inside region, …, 1); a pass
    /// flag P ≠ A that DS1 sets elsewhere → || f6(P) (K5; the Asylum's are A: nothing). Pass → key guide off, the object's
    /// state for objAnimId, DS1's ObjAct flag A (and the state flag S when the MSB has one). Fail → DS1's actionFailedMsgId
    /// as a dialog (10010163 "Locked" → 1105) and a re-execute (130002, rule G2), so the prompt re-arms. The far door
    /// o8544 (BPK 2011 → 54011000) is the Asylum's case; the hall door has no requirement (states unchanged).
    /// </remarks>
    public static List<(int Id, Dictionary<long, ESD.State> States)> ObjActGroups(MSB1 msb1, Ds1Source ds1, Events.EmevdTranslator.IContext ctx, int firstId, Pack? pack = null)
    {
        var list = new List<(int, Dictionary<long, ESD.State>)>();
        var param = ds1.Params.Params["ObjActParam"];
        foreach (var a in msb1.Events.ObjActs)
        {
            var part = msb1.Parts.Objects.FirstOrDefault(p => p.Name == a.ObjActPartName);
            if (part == null || ctx.ObjActHelper(part.EntityID) is not int helper || ctx.Object(part.EntityID) is not int obj) continue;
            if (Ev.ObjActRules.Of(msb1, param, a) is not { } req) continue;
            if (ctx.ObjectAnimState(part.EntityID, req.AnimId) is not int state || ctx.Flag(req.A) is not int flag) continue;
            var pass = new List<ESD.CommandCall> { C(131638, helper, 1), C(131636, obj, state), C(130101, flag, 1) };
            if (req.S != 0 && ctx.Flag(req.S) is int sf) pass.Add(C(130101, sf, 1));
            if (req.Type == 0)
            {
                list.Add((firstId + list.Count, new()
                {
                    [0] = S([], (True, 1)),
                    [1] = S([C(140205, 0, helper)], (Group(0), 2)),
                    [2] = S(pass, (True, 3)),
                    [3] = S([]),
                }));
                continue;
            }
            // K6: the requirement
            var test = new List<ESD.CommandCall>();
            byte[]? expr = null;
            void Or(ESD.CommandCall reg, int slot) { test.Add(reg); var e = Group(slot)[..^1]; expr = expr == null ? e : [.. expr, .. e, 0x99]; }
            int Goods(int q) => pack != null ? GoodsPorter.Port(pack, ds1, q) : GoodsPorter.Id(q);
            if (req.Type == 1)
            {
                Or(C(140801, 1, Goods(req.Id), 1, 3, 1, 1, 0), 1);
                if (req.Type2 == 1 && req.Id2 > 0) Or(C(140801, 2, Goods(req.Id2), 1, 3, 1, 1, 0), 2);
            }
            else if (req.Type == 2)
            {
                var log = new List<string>();
                var emevd = EMEVD.Read(Path.Combine(ds1.Root, "event", "m18_01_00_00.emevd.dcx"));
                if (Ev.ObjActRules.InnerRegion(emevd, Commands.EmevdDump.Defs(), req.A, req.Id, log) is not int r || ctx.Region(r) is not int loc)
                { pack?.Note($"rule K6: ObjAct {req.A} on {part.Name}: no side region ({string.Join("; ", log)}): left out"); continue; }
                Or(C(140132, 1, loc, loc, 1), 1);
            }
            else { pack?.Note($"rule K6: ObjAct {req.A} on {part.Name}: requirement type {req.Type} has no rule: left out"); continue; }
            if (req.Pass > 0 && req.Pass != req.A && ctx.Flag(req.Pass) is int pf) expr = [.. expr!, .. Flag(pf)[..^1], 0x99];
            int fail = ctx.Message(req.FailMsg) ?? 1105;
            list.Add((firstId + list.Count, new()
            {
                [0] = S([], (True, 1)),
                [1] = S([C(140205, 0, helper)], (Group(0), 2)),
                [2] = S(test, ([.. expr!, 0xA1], 3), (True, 4)),
                [3] = S(pass, (True, 5)),
                [4] = S([C(130443, fail, 0, 0, 190, 0, 0, 0)], (True, 6)),
                [5] = S([]),
                [6] = S([C(130002)]),
            }));
            pack?.Note($"rule K6: ObjAct {req.A} on {part.Name} (ObjActParam {req.ParamRow}: type {req.Type} id {req.Id}{(req.Type2 != 0 ? $", or type {req.Type2} id {req.Id2}" : "")}) → helper {helper} group {firstId + list.Count - 1}: pass → state {state} + flag {flag}; fail → mapevent {fail}, re-execute");
            // fix pass e2 (finding 5): a key lock stays only if the finished build sources the key (Events.KeySources, run by
            // build-pack at the end); the unlocked form = the no-requirement group above
            if (pack != null && req.Type == 1)
                Ev.KeySources.K6.Add(new($"ezstate/event_m18_01_00_00.esd", firstId + list.Count - 1,
                    req.Type2 == 1 && req.Id2 > 0 ? [Goods(req.Id), Goods(req.Id2)] : [Goods(req.Id)],
                    new() { [0] = S([], (True, 1)), [1] = S([C(140205, 0, helper)], (Group(0), 2)), [2] = S(pass, (True, 3)), [3] = S([]) },
                    $"far-door-style ObjAct {req.A} on {part.Name}"));
        }
        return list;
    }

    /// <summary>Adds the Asylum's groups to its event ESD and EventParam (rows from Things Betwixt: 5010 = ownerless
    /// bootType 1; owner rows get bootType 2 / ownerType 1 / ownerId like FoFG's key doors).</summary>
    /// <param name="generators">DS1 character entity → placed generator (AsylumEnemies.Build), null = none placed.</param>
    /// <returns>The DS1 events that translated (AsylumEvents leaves out its own stand-ins for them, e.g. the crow).</returns>
    /// <param name="sounds">DS1 sound entity → DS2 sound id of the Sound regions the map builder placed (atmos track,
    /// Events.EntityRules.SoundId), null = none: 2010:3 BGM toggles are no-ops.</param>
    public static HashSet<long> Events(Pack pack, Ds1Source ds1, ESD esd, PARAM events, PARAM.Row ownerless,
        IReadOnlyDictionary<int, int>? generators = null, bool firelink = true, bool hitGroups = false, IReadOnlyDictionary<int, int>? sounds = null)
    {
        void AddGroup(int id, Dictionary<long, ESD.State> states, int owner = 0)
        {
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

        // ---- DS1's own events, translated by rule (Events.EmevdTranslator; the report lists what doesn't translate yet):
        // event 0 (map load), the hall door / gate (11810311 hall entry, 11810312 gate shut, 11810313 "Locked by some
        // contraption", 11810111 door opened), the key doors (11810100 slots: DS2 door locks owned by the doors), the
        // courtyard fog (11810090: white-door crossing), the crow (11810000: map warp), boss-room flags…
        var msb1 = MSB1.Read(Path.Combine(ds1.Root, "map", "MapStudio", "m18_01_00_00.msb"));
        _ = Regions(msb1).ToList(); // region ids known before translating (MapLinks writes the rows afterwards)
        var ctx = new Events.AsylumContext(pack, ds1, generators ?? new Dictionary<int, int>(), firelink, hitGroups, sounds);
        var emevd = EMEVD.Read(Path.Combine(ds1.Root, "event", "m18_01_00_00.emevd.dcx"));
        var tr = new Events.EmevdTranslator(emevd, ctx, Commands.EmevdDump.Defs());
        int nextGroup = 38000;
        tr.TranslateMap((id, slot) => nextGroup++);
        // DS1 common.emevd events instanced for this map's flags (session 8, track boss step 2: event 970 slot 21 = the
        // Asylum Demon's rewards on flag 16)
        tr.TranslateCommon(EMEVD.Read(Path.Combine(ds1.Root, "event", "common.emevd.dcx")), (id, slot) => nextGroup++, [970]);
        foreach (var r in tr.Results.Where(r => r.States != null))
            AddGroup(r.GroupId, r.States!, r.Owner);
        pack.Note("m18_01_00_00 DS1 events translated:" + Environment.NewLine + tr.Report());

        // ---- session 10 (asy-items), rule C2: the DS1 origin (class) prompt on DS1's opening (event 0's first-load branch,
        // found in DS1's data: Events.OriginPrompt), group 37410; 11810400 (class corpses) and every 4:11 event wait for it
        Lordran.Tools.Build.Events.OriginPrompt.Add(pack, ds1, esd, events, ownerless, emevd, "m18_01_00_00");

        // ---- session 10 (asy-items i2), rules S1/S1-R: DS1's drop trades (Snuggly's nest, 11810641) as DS2 give-item
        // prompts, group 37420 (Events.DropTrades)
        Lordran.Tools.Build.Events.DropTrades.Add(pack, ds1, esd, events, ownerless, "m18_01_00_00", ctx);

        // ---- session 11 (cov11), rule K2 / D2-A: the covenant sub-identity housekeeping (Build/Talk/Covenants), group 37430
        Lordran.Tools.Build.Talk.Covenants.AddHousekeeping(pack, esd, events, ownerless, "m18_01_00_00");

        // ---- TEST ONLY (build-pack --test-flags, session 10): DS1 flags set at every load of the map (group 37398, first
        // in the group order), e.g. 11810000 for the return trip without the crow route; asy-items i2: a negative id clears
        if (TestFlags.Count > 0)
        {
            var set = TestFlags.Select(f => (Flag: Lordran.Tools.Build.Events.FlagRules.Map("m18_01", Math.Abs(f)) ?? throw new InvalidDataException($"--test-flags: no DS2 flag for DS1 {Math.Abs(f)}"), On: f > 0)).ToList();
            AddGroup(37398, new() { [0] = S([], (True, 1)), [1] = S(set.Select(f => C(130101, f.Flag, f.On ? 1 : 0)).ToList(), (True, 2L)), [2] = S([]) });
            pack.Note($"TEST --test-flags: DS1 {string.Join(", ", TestFlags)} → DS2 {string.Join(", ", set.Select(f => (f.On ? "" : "-") + f.Flag))} set (- = cleared) at every Asylum load (group 37398)");
        }

        // ---- session 11 (track ret11), rule XM1-R (save repair; XM1 itself is in EmevdTranslator): DS1 sets 11810000 ("left the
        // Asylum") only on the cliff past the far door (11810000's crow) - or, as a fallback for a start in Firelink, when the
        // player arrives in Firelink without having been in the Asylum (m10_02 11020001 [2]-[4]). In the port Firelink is reachable
        // by DS2 travel / test warps, so saves made before XM1 can carry 11810000 without the far door ever opened (11810110 off),
        // which turns the first Asylum visit into the return trip (the Echo lands in the cell with the key gone and the Black
        // Knight outside). At every Asylum load: 11810000 ON and 11810110 OFF → 11810000 and its world switch's fold guard
        // 11810350 cleared (no legitimate departure can leave 11810110 off). Not with --test-flags 11810000 (test builds).
        // session 11 (fix11): only with --entry cell; with the Firelink entry the fallback 11810000 is the intended state
        if (!EntryItem.ToFirelink && !TestFlags.Contains(11810000) && Lordran.Tools.Build.Events.FlagRules.Map("m18_01", 11810000) is int fLeft
            && Lordran.Tools.Build.Events.FlagRules.Map("m18_01", 11810110) is int fFar && Lordran.Tools.Build.Events.FlagRules.Map("m18_01", 11810350) is int fSwitch)
        {
            byte[] stale = [0x46, .. Push(fLeft), 0x85, 0xA6, .. Push(0), 0x96, 0x46, .. Push(fFar), 0x85, 0xA6, .. Push(0), 0x95, 0x98, 0xA1];
            AddGroup(37397, new() { [0] = S([], (True, 1)), [1] = S([], (stale, 2), (True, 3L)), [2] = S([C(130101, fLeft, 0), C(130101, fSwitch, 0)], (True, 3L)), [3] = S([]) });
            pack.Note($"rule XM1-R: Asylum load repairs a fallback departure flag: {fLeft} (DS1 11810000) ON with {fFar} (11810110) OFF → {fLeft} and {fSwitch} (11810350) cleared (group 37397)");
        }

        // ---- DS1 ObjActs without a key (the hall door), by the ObjAct rule
        var objActs = ObjActGroups(msb1, ds1, ctx, 37330, pack);
        foreach (var (id, states) in objActs) AddGroup(id, states);

        // ---- the boulder (DS1 11810211): rolls when the player reaches the stair landing below it, once.
        // Hand-written (session 7; kept in session 8): DS1 swaps the rolled boulder for an end-state copy (o8530_0001, anim
        // 0 = resting at the bottom) + rubble (o8531) standing at the same spot; DS2's prefab has its own end state (o10_9240:
        // 10 → 70 plays the roll → 20), so DS1's hide/show/reproduce sequence has no instruction-level rule yet (the
        // translator reports "object 1811216/1811217": the swap-copy objects). Its damaging-object part (2005:9) is the
        // instance row's damageId0.
        // Session 10: DS1's own 11810211 translates by rule (WE1 places the rest copy and the rubble, C11 requests the rest
        // copy's end state, WE4b breaks the wall at DS1's contact time): this stand-in only when it doesn't.
        bool ownBoulder = tr.Results.Any(r => r.EventId == 11810211 && r.States != null);
        if (!ownBoulder)
        AddGroup(37340, new()
        {
            [0] = S([], (True, 1)),
            [1] = S([], (Flag(FBoulder), 4), (True, 2)),
            [2] = S([C(140132, 0, RBoulder, RBoulder, 1)], (Group(0), 3)),
            [3] = S([C(130101, FBoulder, 1), C(131636, Boulder, 70)], (True, 5)),
            // already rolled (earlier visit; session 9 npcflags: FBoulder now persists): DS1's own branch by rule, 2005:3
            // (De)activate Object(1811215, 0) → 130702(instance, 0); its 2005:7 on the end-state copy 1811216 has no object
            // (absent: swap-copy). Not 131636(…, 70): state 70 IS the roll (damageId0), it would roll + hurt at every load;
            // no vanilla script requests 20 of this prefab family directly (obj-states: 51 three-state prefabs, only 70).
            [4] = S([C(130702, Boulder, 0)], (True, 5)),
            [5] = S([]),
        });
        pack.Note($"m18_01_00_00 script: {tr.Results.Count(r => r.States != null)} translated DS1 event instances (38000+), ObjAct rule groups {string.Join(", ", objActs.Select(o => o.Id))}, boulder {(ownBoulder ? "DS1's 11810211 (translated)" : "37340 (hand-written stand-in)")}");
        return tr.Results.Where(r => r.States != null).Select(r => r.EventId).ToHashSet();
    }
}
