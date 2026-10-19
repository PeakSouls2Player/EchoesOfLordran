using System.Numerics;
using Lordran.Tools.Formats.Havok;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// A DS1 map as its own DS2 map (new map id). Writes every per-map file DS2 opens when it loads a map (found with
/// log_opens on Majula): map\ID\ID.msb + .ngp; model\map\{m,t,t_low,g,h,l}ID archives and eID.envbnd; the per-map params;
/// ezstate event/talk ESDs; filter; breakobj; talk/bloodmes text; the map sound bank. DS1 content: map pieces
/// (MapPieceConverter), textures + lightmaps, collision (CollisionConverter), bonfire. Session 8: lighting, fog,
/// post-processing (fltparam per collision FilterID), player light, map effects and (opt-in) sound are DS1's
/// (<see cref="Atmosphere"/>); env cubemaps and the fltparam fields DS1 has no counterpart for come from the best-fit
/// DS2 area; the rest (GI light probes: LightProbes) is ours or the template map's. Parts keep DS1's coordinates
/// (MapOffset 0).
/// </summary>
public static class MapBuilder
{
    /// <summary>DS1 object entities this build placed as DS2 objects (doors, item pickups) → instance id, per DS2 map
    /// (session 8: the event translator's object rule; scripted placements are AsylumScript's own).</summary>
    public static readonly Dictionary<(string Map, int Entity), int> PlacedObjects = new();
    /// <summary>Session 10 (asy-items), rule C3: DS2 map → the pickup instances of DS1 treasures that start disabled
    /// (MSB Treasure.StartDisabled): the map's translated event 0 disables their item at every load (131411(inst, 0)).</summary>
    public static readonly Dictionary<string, List<int>> StartDisabledPickups = new();
    /// <summary>Session 11 (burg11), rule CC1: (DS2 map, DS1 collision part) → the DS2 collision part placed for it.</summary>
    public static readonly Dictionary<(string Map, string Ds1Part), string> PlacedCollisions = new();
    /// <summary>Session 11 (burg11, play-test fix), rule SR3c: DS2 step-ramp collision → the DS1 collision part it lies on
    /// (MapSeams gives a ramp its base collision's connects: the player standing on a ramp keeps the neighbour loaded).</summary>
    public static readonly Dictionary<(string Map, string Ramp), string> RampBase = new();

    /// <summary>Session 10 (asy-items), rule C3: the DS1 object entities a map's events switch as treasures (2005:4 Set
    /// Object Treasure State), Initialize Event arguments substituted. (2005:3 alone, an object shown/hidden, doesn't count:
    /// the cell-key corpse 1811600 is hidden on the return trip but was always placed.)</summary>
    public static HashSet<int> TreasureSwitches(string dsrRoot, string ds1Map)
    {
        string path = Path.Combine(dsrRoot, "event", ds1Map + ".emevd.dcx");
        if (!File.Exists(path)) return [];
        var emevd = EMEVD.Read(path);
        var defs = Commands.EmevdDump.Defs();
        var set = new HashSet<int>();
        // direct targets, plus the arguments the Initialize Events pass into parameterised 2005:4 (11810400's X4/X8/X12)
        foreach (var ev in emevd.Events)
            for (int i = 0; i < ev.Instructions.Count; i++)
            {
                var x = ev.Instructions[i];
                if (x.Bank != 2005 || x.ID != 4) continue;
                var prm = ev.Parameters.FirstOrDefault(q => q.InstructionIndex == i && q.TargetStartByte == 0);
                if (prm == null) { if (Events.EmevdTranslator.Decode(defs, x) is { Length: > 0 } v) set.Add((int)v[0]); continue; }
                foreach (var init in emevd.Events.SelectMany(e => e.Instructions).Where(y => y.Bank == 2000 && y.ID == 0 && y.ArgData.Length >= 8 && BitConverter.ToUInt32(y.ArgData, 4) == ev.ID))
                {
                    var args = init.ArgData[8..];
                    if (prm.SourceStartByte + 4 <= args.Length) set.Add(BitConverter.ToInt32(args, (int)prm.SourceStartByte));
                }
            }
        return set;
    }

    public sealed record Options(string Ds1Map, string Ds2Map, string TemplateMap, Vector3 BonfirePos, Vector3 BonfireRot,
        string AreaName, string BonfireName, double GiGain = 0.6, string? BannerAtlas = null, int BannerLine = 0, int BannerColumn = -1)
    {
        /// <summary>Test control only: write an empty navmesh (as before session 5) instead of DS1's converted one.</summary>
        public bool NoNavmesh { get; init; }
        /// <summary>Session 11 (burg11, round 9), rule DG1: DS1's draw groups — a map piece draws (DrawGroups = DispGroups =
        /// DS1's DrawGroups) only while the collision under the player shows one of its groups (collision DrawGroups =
        /// DispGroups = DS1's DispGroups; vanilla collisions carry equal words, e.g. TB 0x409E/0x409E). Objects and the
        /// collisions' object-area words (Unk44..50) are unchanged (objects stay active everywhere).</summary>
        public bool Dg1 { get; init; }
        /// <summary>Session 11 (burg11, play-test fix), rule GI2: lightmap-less piece triangles under the map's own
        /// geometry get the dark GI stand-in (MapPieceConverter.Covered).</summary>
        public bool CoverGi { get; init; }
        /// <summary>DS1's item pickups (MSB treasure events) as DS2 item objects.</summary>
        public bool Pickups { get; init; } = true;
        /// <summary>Session 10 (asy-items): DS1's developer messages as DS2 messages (<see cref="Maps.BloodMessages"/>).</summary>
        public bool BloodMessages { get; init; } = true;
        /// <summary>DS1 objects ported as real DS2 objects (ObjPorter), placed at every DS1 part of their model as doors.</summary>
        public ObjPorter.Options[] Objects { get; init; } = [];
        /// <summary>Objects placed one by one with their own instance ids (session 7: event-driven doors and gates, the
        /// key-guide helper, DS2 white-door fog walls), filled from the DS1 MSB by the map's script (AsylumScript).</summary>
        public Func<MSB1, List<Placement>>? Placements { get; init; }
        /// <summary>DS1 entity ids of door parts the script places itself (e.g. cells that never open), skipped by the door loop.</summary>
        public HashSet<int> ScriptedDoors { get; init; } = [];
        /// <summary>Session 8 (cutscene track): demo-only objects (<see cref="Cutscenes.Cutscenes.DemoActors"/>), placed like
        /// <see cref="Placements"/> with <see cref="Placement.Groups"/> = draw group 110 only.</summary>
        public List<Placement> DemoActors { get; init; } = [];
        /// <summary>Session 8 (atmosphere): DS1's lighting, fog, post-processing, player light and map effects
        /// translated (<see cref="Atmosphere"/>); false = the session-7 behaviour (Things Betwixt's, greyed).</summary>
        public bool Ds1Atmosphere { get; init; } = true;
        /// <summary>DS1's FMOD projects, banks and sound regions (BGM, ambient loops, event SEs, one-shot SFX) instead of
        /// Things Betwixt's sound bank: session 8 opt-in, default since session 10 (track asy-sound, rules SND1-SND5;
        /// build-pack --no-ds1-sound turns it off).</summary>
        public bool Ds1Sound { get; init; } = true;
        /// <summary>Session 8 (track skylight): DS1 skies → DS2 SkyDome2[NoFog] with DS1's baked look, unlit background
        /// pieces as DS2 background models, DS1's unlit colour multipliers, one placement per draw-group variant
        /// (<see cref="Sky"/>, rules S1–S4); false = the earlier NoLit[D] skies.</summary>
        public bool Ds1Sky { get; init; } = true;
        /// <summary>Session 8 (track skylight): DS1's ToneCorrect rows as DS2 colour-grading palettes (<see cref="Palette"/>,
        /// rule A7b); false = the identity palette (no grading).</summary>
        public bool Ds1Palette { get; init; } = true;
        /// <summary>Session-8 skylight fix pass: the DS2 area (and filter id) the fields DS1 has no counterpart for come from,
        /// "mAA_BB_CC_DD[:filter]", instead of rule A3's best fit (build-pack --atmos-template MAP=AREA[:F]); for A/B tests
        /// of a close ranking. Null = rule A3.</summary>
        public string? AtmosTemplate { get; init; }
        /// <summary>Session 8: DS1 collisions DS1's events switch on/off (2011:1) are placed in hit groups of their own
        /// (<see cref="ToggledCollisions"/>) so translated events can toggle them with 132153; off = the old behaviour
        /// (collisions DS1 starts disabled are left out, the others are always solid). Only for maps whose DS1 script is
        /// translated (the Asylum, with build-pack --hit-groups: opt-in since the session-8 fix pass, because the gate
        /// panel h0550B1 stands in the gate opening and only an untested 132153 at load removes it); otherwise nothing
        /// would switch them off.</summary>
        public bool HitGroups { get; init; }
        /// <summary>Track nests: decoration parts DS1's events show/hide become Scenery objects the translated script
        /// switches; false (the map's events aren't translated: build-pack --no-asylum-script) = those parts are left out.</summary>
        public bool Scenery { get; init; } = true;
        public int BonfireId => LordranBonfireId(Ds1Map, 0);
        /// <summary>Session 11 (burg11), rule BF1: every DS1 o0200 bonfire the map's script registers (2009:3) becomes a DS2
        /// bonfire (<see cref="Bonfires"/>); false = the single bonfire nearest <see cref="BonfirePos"/> (Asylum, Firelink: as
        /// verified; the Asylum's 2nd bonfire waits on D-bfname).</summary>
        public bool AllBonfires { get; init; }
        /// <summary>Session 11 (travel12), rule BF1-A: every DS1 o0200 bonfire the map's script registers (2009:3) becomes a
        /// DS2 bonfire, without the other rules <see cref="AllBonfires"/> turns on (NET1/AP1 profiles, sub-areas, MapObjects):
        /// index 0 stays the verified bonfire nearest <see cref="BonfirePos"/> with <see cref="BonfireName"/>, all of them in
        /// the map's one Travel area (the Asylum: courtyard 48150 + the 2nd bonfire 1811961 → 48155).</summary>
        public bool EveryBonfire { get; init; }
        /// <summary>The map id as DS2's params write it (decimal AABBCCDD, e.g. m18_01_00_00 → 18010000).</summary>
        public int MapIdDecimal => int.Parse(Ds2Map[1..3] + Ds2Map[4..6] + Ds2Map[7..9] + Ds2Map[10..12]);
    }

    /// <summary>How a placed object behaves: its MapObjectInstanceParam row is copied from the template map's row of
    /// that kind (Things Betwixt: Generic = 10021400, objType 1607 / objCtrlType 0, event-driven; WhiteDoor = 10020600,
    /// objType 110 / objCtrlType 6, objCtrlId = MapObjectWhiteDoorParam row; Door = TB's door 10020410); Static = a
    /// generic row too (used for DS1 doors that never open).</summary>
    public enum PlacementKind { Generic, WhiteDoor, Door }

    public sealed record Placement(string Model, Vector3 Position, Vector3 Rotation, int Instance, PlacementKind Kind, int CtrlId = 0, string Note = "")
    {
        /// <summary>Extra instance-row fields (e.g. damageId0 for a rolling boulder).</summary>
        public Dictionary<string, object> Fields { get; init; } = [];
        /// <summary>Draw/display groups instead of all bits (session 8, cutscene track: demo-only objects use group 110
        /// alone, as FoFG's bird o00_6090_0000 [0,0,0,0x4000]; a demo's DemoParam.addGroupMask 110 shows them).</summary>
        public uint[]? Groups { get; init; }
        /// <summary>Session 8 (track nests): the part's scale (null = the template part's).</summary>
        public Vector3? Scale { get; init; }
    }

    /// <summary>The Undead Asylum as m18_01_00_00 (DS1's own id: area 18 is unused in DS2), Things Betwixt as template.</summary>
    public static Options Asylum => new("m18_01_00_00", "m18_01_00_00", "m10_02_00_00",
        BonfirePos: new Vector3(3.1f, 195.91f, 7.44f), BonfireRot: Vector3.Zero,
        AreaName: "Northern Undead Asylum", BonfireName: "Undead Asylum Courtyard", BannerAtlas: "Menu21", BannerLine: 2)
    {
        // DS1's barred cell doors (o8540, the first one = the player's cell) on DS2's iron-bar single door (o00_0308);
        // DS1 a00_0000 (opens 0 → -80° in 2 s) / a00_0001 (closes) → DS2 a00_01_0001 (AnimList 10001 open) /
        // a00_01_0002 (10002 close). Decoded with lr.cmd hkx-anim (session 7); session 6 had mapped 0001/0002, so the
        // resting door showed a00_0002's end pose (a fast open: the door stood open).
        Objects =
        [
            new ObjPorter.Options("o8540", "o18_8540", "o00_0308", [("a00_0000", "a00_01_0001"), ("a00_0001", "a00_01_0002")]),
            // DS1's two Asylum ladders (7.1 m and 6.0 m) on DS2's ladders of the nearest height (7.4 m, 6.2 m)
            new ObjPorter.Options("o8500", "o18_8500", "o00_0904", [], ObjPorter.Kind.Ladder),
            new ObjPorter.Options("o8501", "o18_8501", "o00_0903", [], ObjPorter.Kind.Ladder),
            // session 7: the hall door, the escape gate and the boulder as event-driven prefab objects (AsylumScript)
            .. AsylumScript.Objects,
            // session 10 (rule WE1): the models of the other object parts DS1's events address (AsylumScript.WorldParts)
            .. AsylumScript.WorldPorts,
        ],
        Placements = AsylumScript.Placements,
        ScriptedDoors = AsylumScript.ScriptPlaced,
        // session 11 (travel12), rule BF1-A: the 2nd DS1 bonfire (1811961, archer hall) → 48155 (user: DS1 bonfires missing)
        EveryBonfire = true,
    };

    /// <summary>
    /// Firelink Shrine. DS1's id m10_02 is Things Betwixt's in DS2, so DS1's m10_BB maps get area 19 (unused by both
    /// games): m19_02_00_00. Bonfire 40250 (from the DS1 id), position = DS1's bonfire placeholder c1000_0000.
    /// </summary>
    public static Options Firelink => new("m10_02_00_00", MapHosts.Host("m10_02_00_00"), "m10_02_00_00", // session 11 (burg11): rule H1 → m19_42
        BonfirePos: new Vector3(-51.80f, -60.60f, 55.70f), BonfireRot: Vector3.Zero,
        AreaName: "Firelink Shrine", BonfireName: "Firelink Shrine", BannerAtlas: "Menu20", BannerLine: 0, BannerColumn: 1)
    {
        Dg1 = true, // session 11 (burg11, round 9): rule DG1
        CoverGi = true, // session 11 (burg11, play-test fix): rule GI2
        // session 9 (track firelink): the key-guide helpers of DS1's action-button areas (the nest), for its translated script
        // session 11 (burg11), rule LF1: DS1's lifts (the Parish lift o1460) as prefab objects (Lifts)
        Placements = msb => [.. FirelinkScript.Placements(msb), .. (FirelinkScript.Enabled ? Lifts.Placements(Program.DefaultDsrRoot, "m10_02_00_00", MapHosts.Host("m10_02_00_00")) : [])],
        Objects = FirelinkScript.Enabled ? Lifts.Ports(Program.DefaultDsrRoot, "m10_02_00_00", MapHosts.Host("m10_02_00_00")) : [],
    };

    /// <summary>
    /// DS1 collisions (hit filter 8) DS1's events toggle with 2011:1 Activate Hit, anywhere in the map's script → their
    /// DS2 hit group: <see cref="Events.EntityRules.FirstHitGroup"/> + rank by entity id (Asylum: h0550B1_0000 1813121,
    /// the panel DS1 enables while the escape gate closes → 100). Pure, so the event translator gets the same ids.
    /// </summary>
    public static Dictionary<int, int> ToggledCollisions(string dsrRoot, string ds1Map, MSB1 msb1)
    {
        string emevdPath = Path.Combine(dsrRoot, "event", ds1Map + ".emevd.dcx");
        if (!File.Exists(emevdPath)) return [];
        var hits = EMEVD.Read(emevdPath).Events.SelectMany(e => e.Instructions)
            .Where(i => i.Bank == 2011 && i.ID == 1 && i.ArgData.Length >= 4).Select(i => BitConverter.ToInt32(i.ArgData, 0)).ToHashSet();
        return msb1.Parts.Collisions.Where(p => p.HitFilterID == 8 && p.EntityID > 0 && hits.Contains(p.EntityID))
            .Select(p => p.EntityID).Distinct().Order().Select((e, k) => (e, k)).ToDictionary(x => x.e, x => Events.EntityRules.FirstHitGroup + x.k);
    }

    /// <summary>
    /// Session 10, rule G4 (Events.ObjActRules): the DS1 ladder parts of the map that an event registers later than load
    /// (2009:0 outside the load prefix) and address with 2009:0 only (fix pass e2: kick ladders and other addressed ladders
    /// are not G4, ObjActRules.LaterLadders): they get their own instance row (objCtrlType 4 like DS2's event-controlled
    /// ladders, instance AABB0000 + entity % 10000) so the translated events can hide / show them; every other ladder
    /// keeps instance 0 (the model's MapObjectDefaultInstanceParam row, in-game V). Pure, so the builder and the
    /// translator agree.
    /// </summary>
    public static HashSet<int> EventLadders(string dsrRoot, string ds1Map, MSB1 msb1, bool includeKick = false)
    {
        string path = Path.Combine(dsrRoot, "event", ds1Map + ".emevd.dcx");
        if (!File.Exists(path)) return [];
        // session 11 (burg11, round 6), rule KL1 (includeKick: MapObjects maps): the kick ladders too (hidden until their event
        // registers them; the kick itself is a button helper, MapObjects KL1)
        var later = includeKick ? Events.ObjActRules.LaterLadders(EMEVD.Read(path), Commands.EmevdDump.Defs()).ToDictionary(l => l.Key, l => l.Value.By)
            : Events.ObjActRules.EventRegisteredLadders(EMEVD.Read(path), Commands.EmevdDump.Defs());
        return msb1.Parts.Objects.Where(p => p.EntityID > 0 && later.ContainsKey(p.EntityID)).Select(p => p.EntityID).ToHashSet();
    }

    /// <summary>Session 11 (walk11), rule OC1: every entity-sized (7-digit) literal argument of the map's EMEVD instructions
    /// = the entities DS1's events can address (a superset: also flags/regions; enough to keep event-driven parts out).</summary>
    public static HashSet<int> EventEntities(string dsrRoot, string ds1Map)
    {
        var set = new HashSet<int>();
        string path = Path.Combine(dsrRoot, "event", ds1Map + ".emevd.dcx");
        if (!File.Exists(path)) return set;
        foreach (var ins in EMEVD.Read(path).Events.SelectMany(e => e.Instructions))
            for (int i = 0; i + 4 <= ins.ArgData.Length; i += 4)
            {
                int v = BitConverter.ToInt32(ins.ArgData, i);
                if (v is >= 1000000 and < 10000000) set.Add(v);
            }
        return set;
    }

    /// <summary>Fix pass e2: the map's later-registered ladders rule G4 leaves alone (kick ladders, other addressed ones), for the build log.</summary>
    public static IEnumerable<string> NonG4Ladders(string dsrRoot, string ds1Map)
    {
        string path = Path.Combine(dsrRoot, "event", ds1Map + ".emevd.dcx");
        if (!File.Exists(path)) return [];
        return Events.ObjActRules.LaterLadders(EMEVD.Read(path), Commands.EmevdDump.Defs()).Values.Where(l => l.NotG4 != null)
            .Select(l => $"rule G4: ladder {l.Entity} (registered by {string.Join("/", l.By)}) {l.NotG4}");
    }

    /// <summary>
    /// Bonfire ids for Lordran: 40000 + (AA-10)*1000 + BB*100 + 50 + 5*i for the i-th bonfire of DS1 map mAA_BB (Asylum
    /// courtyard 48150, Firelink 40250). DS2 numbers its own BB*1000 + 650.. (2650 Things Betwixt .. 37685 Eleum Loyce,
    /// debug 62650), which DS1's BB values would collide with; the save keeps bonfire ids as u16, so stay below 65536.
    /// </summary>
    public static int LordranBonfireId(string ds1Map, int index)
    {
        int aa = int.Parse(ds1Map[1..3]), bb = int.Parse(ds1Map[4..6]);
        if (aa is < 10 or > 18 || bb > 9 || index is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(ds1Map));
        return 40000 + (aa - 10) * 1000 + bb * 100 + 50 + 5 * index;
    }

    /// <summary>Session 11 (burg11), rule BF1: one DS2 bonfire of a map. Index i (DS1 entity order) → id
    /// <see cref="LordranBonfireId"/>(map, i), instance AABB0650 + 5i, EventParam / ESD group 80000 + 1000i (the FoFG pattern:
    /// 80000/81000/83000 bootType 4, flag range AABB000 + 100i ..+99, V-data), Travel sub-area MenuAreaId = map id + k
    /// (vanilla: Belfry Luna 10160002, Black Gulch 10250001 …, k = rank of the DS1 place id under it, ascending).</summary>
    public sealed record BonfirePlan(int Index, int Entity, Vector3 Position, Vector3 Rotation, int Id, int Instance, string Name,
        int MenuAreaId, string MenuAreaName)
    {
        public int Group => 80000 + 1000 * Index;
    }

    static readonly Dictionary<string, List<BonfirePlan>> bonfirePlans = new();

    /// <summary>The map's DS2 bonfires (rule BF1 when <see cref="Options.AllBonfires"/>, else the single verified one).</summary>
    public static List<BonfirePlan> Bonfires(string dsrRoot, Options o, MSB1? msb1 = null)
    {
        string key = $"{o.Ds2Map}|{o.AllBonfires}|{o.EveryBonfire}|{o.BonfirePos}";
        if (bonfirePlans.TryGetValue(key, out var done)) return done;
        msb1 ??= MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", o.Ds1Map + ".msb"));
        int aabb = int.Parse(o.Ds2Map.Substring(1, 2) + o.Ds2Map.Substring(4, 2));
        var list = new List<BonfirePlan>();
        if (!o.AllBonfires && o.EveryBonfire)
        {
            // session 11 (travel12), rule BF1-A: the registered bonfires in entity order; index 0 must be the verified one
            var regs = RegisteredBonfireEntities(dsrRoot, o.Ds1Map);
            var everyParts = msb1.Parts.Objects.Where(p => p.ModelName == "o0200" && regs.Contains(p.EntityID)).OrderBy(p => p.EntityID).ToList();
            var verified = msb1.Parts.Objects.Where(x => x.ModelName == "o0200").OrderBy(x => Vector3.Distance(x.Position, o.BonfirePos)).First();
            if (everyParts.Count == 0 || everyParts[0] != verified)
                throw new InvalidDataException($"rule BF1-A: {o.Ds1Map}'s first registered bonfire is not the verified one at {o.BonfirePos} ({verified.EntityID})");
            var everyNames = BonfireNames();
            for (int i = 0; i < everyParts.Count; i++)
            {
                var p = everyParts[i];
                string name = i == 0 ? o.BonfireName : everyNames.GetValueOrDefault((o.Ds1Map, p.EntityID)) ?? o.AreaName;
                list.Add(new BonfirePlan(i, p.EntityID, p.Position, p.Rotation, LordranBonfireId(o.Ds1Map, i), aabb * 10000 + 650 + 5 * i, name, o.MapIdDecimal, o.AreaName));
            }
            return bonfirePlans[key] = list;
        }
        if (!o.AllBonfires)
        {
            var b = msb1.Parts.Objects.Where(x => x.ModelName == "o0200").OrderBy(x => Vector3.Distance(x.Position, o.BonfirePos)).FirstOrDefault();
            list.Add(new BonfirePlan(0, b?.EntityID ?? 0, b?.Position ?? o.BonfirePos, b?.Rotation ?? o.BonfireRot, o.BonfireId, aabb * 10000 + 650, o.BonfireName, o.MapIdDecimal, o.AreaName));
            return bonfirePlans[key] = list;
        }
        var registered = RegisteredBonfireEntities(dsrRoot, o.Ds1Map);
        // session 11 (burg11, round 8), rule H2-P: a merged map numbers the primary's bonfires first (its ids stay)
        var parts = msb1.Parts.Objects.Where(p => p.ModelName == "o0200" && registered.Contains(p.EntityID))
            .OrderBy(p => H2Merge.MemberRank(o.Ds1Map, p.Name)).ThenBy(p => p.EntityID).ToList();
        // the Travel sub-area: the DS1 place (MapNameID) of the collision under the bonfire
        var probe = FloorProbe.FromDs1(dsrRoot, o.Ds1Map);
        var cols = msb1.Parts.Collisions.ToDictionary(c => c.Name, c => c);
        static string PartOf(string tag) => tag.Split(' ')[0]; // FromDs1 tags triangles "NAME fFILTER"
        var ds1 = new Ds1Source(dsrRoot);
        // DS1 MapNameID: n = place n with its banner, -n = place n without a banner, -1 = none (V-data: m10_01 has 1010,
        // 1011, -1011, -1); a bonfire on a -1 collision takes the nearest named collision under or around it
        int Place(MSB1.Part.Object p)
        {
            // rule H2-P: only the bonfire's own member map's collisions (a merged map's other half may lie below)
            string member = H2Merge.MemberOf(o.Ds1Map, p.Name);
            bool Own(string part) => H2Merge.MemberOf(o.Ds1Map, part) == member;
            foreach (var h in probe.Down(p.Position.X, p.Position.Z).Where(h => h.Y <= p.Position.Y + 1f && Own(PartOf(h.Part))).Take(4))
                if (cols.TryGetValue(PartOf(h.Part), out var c) && c.MapNameID != -1) return Math.Abs((int)c.MapNameID);
            var cands = probe.Triangles.Where(t => Own(PartOf(t.Part)) && cols.TryGetValue(PartOf(t.Part), out var c) && c.MapNameID != -1).ToList();
            if (cands.Count == 0) return -1; // (rule H2-P: no named collision of the bonfire's own member)
            var near = cands.MinBy(t => Vector3.DistanceSquared((t.A + t.B + t.C) / 3, p.Position));
            return near.Part != null ? Math.Abs((int)cols[PartOf(near.Part)].MapNameID) : -1;
        }
        var places = parts.Select(Place).ToList();
        // sub-area rank: the map's own place (its text = the row's area name) is 0, the others follow in id order
        var named = places.Where(x => x >= 0).Distinct().Order().ToList();
        int mainPlace = named.FirstOrDefault(x => PlaceText(ds1, x) == o.AreaName, named.FirstOrDefault(-1));
        var ranks = named.Where(x => x == mainPlace).Concat(named.Where(x => x != mainPlace)).ToList();
        var names = BonfireNames();
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            int k = Math.Max(0, ranks.IndexOf(places[i]));
            string placeText = places[i] >= 0 ? PlaceText(ds1, places[i]) : "";
            string areaName = k == 0 ? o.AreaName : placeText.Length > 0 ? placeText : o.AreaName;
            string name = names.GetValueOrDefault((o.Ds1Map, p.EntityID)) ?? (placeText.Length > 0 ? placeText : o.AreaName);
            list.Add(new BonfirePlan(i, p.EntityID, p.Position, p.Rotation, LordranBonfireId(o.Ds1Map, i), aabb * 10000 + 650 + 5 * i, name, o.MapIdDecimal + k, areaName));
        }
        if (list.Count == 0) throw new InvalidDataException($"rule BF1: {o.Ds1Map} registers no o0200 bonfire");
        return bonfirePlans[key] = list;
    }

    /// <summary>DS1's registered bonfires: 2009:3 Register Bonfire (flag, entity, …) with a literal entity (rule BF1).</summary>
    public static HashSet<int> RegisteredBonfireEntities(string dsrRoot, string ds1Map)
    {
        var defs = Commands.EmevdDump.Defs();
        var emevd = EMEVD.Read(Path.Combine(dsrRoot, "event", ds1Map + ".emevd.dcx"));
        return emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2009 && i.ID == 3)
            .Select(i => Events.EmevdTranslator.Decode(defs, i)).Where(v => v.Length >= 2).Select(v => (int)v[1]).ToHashSet();
    }

    /// <summary>DS1's place name (the FMG holding MapNameID texts: PlaceName_ in DSR, else any text with that id).</summary>
    static string PlaceText(Ds1Source ds1, int id)
    {
        foreach (string fmg in ds1.TextNames.Where(n => n.Contains("Place_name", StringComparison.OrdinalIgnoreCase)))
            if (ds1.Text(fmg, id) is { Length: > 0 } t) return t;
        return "";
    }

    /// <summary>Mappings/bonfire-names.tsv (D-bfname): (DS1 map, DS1 bonfire entity) → name.</summary>
    public static Dictionary<(string Map, int Entity), string> BonfireNames()
    {
        var d = new Dictionary<(string, int), string>();
        string path = Path.Combine(AppContext.BaseDirectory, "Mappings", "bonfire-names.tsv");
        if (!File.Exists(path)) return d;
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var c = line.Split('\t');
            if (c.Length >= 3 && int.TryParse(c[1], out int e)) d[(c[0], e)] = c[2];
        }
        return d;
    }

    /// <summary>
    /// Session 11 (burg11, R3 first cut of MapPlan.FromDs1): the options of a ported DS1 map from its MapHosts row alone —
    /// host, area name, banner, template — with every DS1 bonfire (BF1); the viewpoint (sky rule S3d, env regions) is the
    /// bonfire whose DS1 place is the map's main one. M2: every object part by rule (<see cref="MapObjects"/>: K1/K1g/K1e/K1p
    /// prefabs + helpers, G4 ladders, WE1, OK1 decoration/breakables, BD1 border doors kept shut; <paramref name="built"/> =
    /// the DS1 maps of this pack) and the DS1 item pickups (PK1). Blood messages: the Burg's 5 are hidden (rule M4 off).
    /// </summary>
    public static Options ForDs1(MapHosts.Row row, string dsrRoot, IReadOnlySet<string> built)
    {
        var o = new Options(row.Ds1Map, row.Host, row.TemplateMap, Vector3.Zero, Vector3.Zero, row.AreaName, row.AreaName,
            BannerAtlas: row.BannerAtlas, BannerLine: row.BannerLine, BannerColumn: row.BannerColumn)
        { AllBonfires = true, Pickups = true, BloodMessages = true, Scenery = false, Dg1 = true, CoverGi = true }; // + rule DG1 (round 9), rule GI2 (play-test fix)
        var plan = MapObjects.For(dsrRoot, row.Ds1Map, row.Host, built);
        o = o with
        {
            Objects = plan.Ports.ToArray(),
            Placements = msb => [.. plan.Placements, .. plan.HelperPlacements(dsrRoot, msb)],
            ScriptedDoors = plan.Scripted,
        };
        var plans = Bonfires(dsrRoot, o);
        var main = plans.FirstOrDefault(b => b.MenuAreaId == o.MapIdDecimal && b.Name == row.AreaName)
            ?? plans.FirstOrDefault(b => b.MenuAreaId == o.MapIdDecimal) ?? plans[0];
        return o with { BonfirePos = main.Position, BonfireRot = main.Rotation, BonfireName = plans[0].Name };
    }

    /// <summary>DS1 objects ported as static decoration (see Build): urns, pots, tubs, bags, gargoyles, church pillars, sewer columns.</summary>
    static readonly HashSet<string> DecorObjects = new(StringComparer.OrdinalIgnoreCase)
        { "o8400", "o8401", "o8402", "o8403", "o8410", "o8411", "o8420", "o8421", "o8422", "o8425", "o8600", "o8610",
          "o1495", "o1496", "o1497", // Firelink: urns with ivy
          "o8550" }; // session 8 (track nests): the crow's nest (Asylum cliff 1811300 + Snuggly's o8550_0001); model only

    /// <summary>Session 10: the DS1 model is placed by the decoration rule above (world-event rule WE1 leaves it alone).</summary>
    public static bool IsDecorModel(string ds1Model) => DecorObjects.Contains(ds1Model);

    /// <summary>Session 8 (track nests): template of <see cref="ObjPorter.Kind.Scenery"/> objects: Things Betwixt's
    /// humanity model o02_8500, a DS2 static object without collision placed there with the generic event instance row.</summary>
    public const string SceneryTemplate = "o02_8500";

    static readonly Dictionary<string, HashSet<int>> eventShown = new();

    /// <summary>
    /// Session 8 (track nests): DS1 object entities the map's events show or hide (2005:3 (De)activate Object, the entity
    /// literal or passed in by an Initialize Event 2000:0), e.g. the cliff nest 1811300 (11810150: hidden until 11810000,
    /// then shown). A decoration part among them can't be a map piece (DS2 can't switch those): it becomes a DS2 object
    /// (<see cref="ObjPorter.Kind.Scenery"/>) with instance AABB0000 + entity % 10000, which the translated 130702 drives.
    /// </summary>
    public static HashSet<int> EventShownObjects(string dsrRoot, string ds1Map)
    {
        if (eventShown.TryGetValue(ds1Map, out var done)) return done;
        var set = new HashSet<int>();
        string path = Path.Combine(dsrRoot, "event", ds1Map + ".emevd.dcx");
        if (File.Exists(path))
        {
            var emevd = EMEVD.Read(path);
            var inits = emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2000 && i.ID == 0 && i.ArgData.Length >= 8).ToList();
            foreach (var ev in emevd.Events)
                for (int k = 0; k < ev.Instructions.Count; k++)
                {
                    var ins = ev.Instructions[k];
                    if (ins.Bank != 2005 || ins.ID != 3 || ins.ArgData.Length < 4) continue;
                    var param = ev.Parameters.FirstOrDefault(p => p.InstructionIndex == k && p.TargetStartByte == 0);
                    if (param == null) { set.Add(BitConverter.ToInt32(ins.ArgData, 0)); continue; }
                    foreach (var init in inits.Where(i => BitConverter.ToUInt32(i.ArgData, 4) == ev.ID && i.ArgData.Length >= 8 + param.SourceStartByte + 4))
                        set.Add(BitConverter.ToInt32(init.ArgData, 8 + (int)param.SourceStartByte));
                }
        }
        set.Remove(0);
        return eventShown[ds1Map] = set;
    }

    /// <summary>Session 8 (track nests): the part becomes a <see cref="ObjPorter.Kind.Scenery"/> object (a decoration model
    /// with no DS1 collision whose entity DS1's events show/hide), so its instance is AABB0000 + entity % 10000.</summary>
    public static bool IsScenery(string dsrRoot, string ds1Map, MSB1.Part.Object part)
    {
        if (part.EntityID <= 0 || !DecorObjects.Contains(part.ModelName) || !EventShownObjects(dsrRoot, ds1Map).Contains(part.EntityID)) return false;
        string bnd = Path.Combine(dsrRoot, "obj", part.ModelName + ".objbnd.dcx");
        return File.Exists(bnd) && !BND3.Read(DCX.Decompress(File.ReadAllBytes(bnd))).Files.Any(f => Path.GetFileName(f.Name).Equals(part.ModelName + ".hkx", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>DS2's MapObjectInstanceTypeParam 1150 "汎用-接触破壊-置物-破壊可" (general, contact destruction, ornament):
    /// sizeType 2, weightType 7, breakAction 0 (MapObjectBreakActionParam 0: walk, dash, roll, jump, fall all break it),
    /// hp 10, impact 120 — DS2's own row for objects that break on any contact (o00_1030). Session 8, track potroll.</summary>
    public const int ContactBreakInstanceType = 1150;

    public static void Build(Pack pack, string dsrRoot, Options o)
    {
        Ds1Source? ds1Src = null; // session 10: shared by the pickups and the blood messages (loaded on first use)
        string data = pack.Ds2Data;
        string mdir = Path.Combine(data, "model", "map");
        string tId = o.TemplateMap, nId = o.Ds2Map;
        string tTail = tId[1..], nTail = nId[1..];
        var materials = Ds2MapMaterials.Load(data);
        MapPieceConverter.Ds2Data = data;
        var msb1 = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", o.Ds1Map + ".msb"));
        var drawParam = Ds1DrawParam.ForMap(dsrRoot, o.Ds1Map); // session 11 (burg11, round 8): + rule H2-D
        // session 8 (track skylight): DS1's MTDs for the unlit colour rule (S3) and the sky rule (S1); the sky part of a
        // model (after rule S4's de-duplication) gives its FogBank/ScatterBank rows; the viewpoint is the bonfire
        var skyLog = new List<string>();
        MapPieceConverter.Ds1Mtds = o.Ds1Sky ? Ds1Mtd.Load(dsrRoot) : null;
        var placedPieces = Sky.DistinctPlacements(msb1.Parts.MapPieces, o.Ds1Sky ? skyLog : []);
        var pieceModels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // DS1 model → DS2 model name
        // rule S1b (skylight fix pass): a DS1 model whose sky material shares the model with LIT meshes is split; the sky
        // part gets a model of its own (DS1 model → that DS2 name), placed wherever the DS1 model is
        var skyModels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ds1Of = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);         // DS2 model name → DS1 model
        Sky.Part? SkyPartFor(string ds2Model)
        {
            string ds1Model = ds1Of.GetValueOrDefault(ds2Model) ?? ds2Model;
            var p = placedPieces.FirstOrDefault(x => x.ModelName.Equals(ds1Model, StringComparison.OrdinalIgnoreCase));
            if (p == null) return null;
            var world = Matrix4x4.CreateScale(p.Scale)
                * Matrix4x4.CreateRotationZ(p.Rotation.Z * MathF.PI / 180) * Matrix4x4.CreateRotationX(p.Rotation.X * MathF.PI / 180) * Matrix4x4.CreateRotationY(p.Rotation.Y * MathF.PI / 180)
                * Matrix4x4.CreateTranslation(p.Position);
            return new Sky.Part(world, drawParam.Row("FogBank", p.FogID), drawParam.Row("LightScatteringBank", p.ScatterID), p.Name, drawParam.Row("LightBank", p.LightID));
        }
        // session 9 (track skyfix): the DS1 textures load once (rule S3b v2 reads texture statistics while converting)
        var ds1TexLazy = new Lazy<Ds1MapTextures>(() => new Ds1MapTextures(dsrRoot, o.Ds1Map.Substring(0, 3)));
        var conv = new MapPieceConverter(materials, nId)
        {
            SkyContext = o.Ds1Sky ? new Sky.Context
            {
                PartFor = SkyPartFor, Viewpoint = o.BonfirePos,
                Env = Ds1Unlit.EnvSpots.For(dsrRoot, o.Ds1Map, msb1), Texture = n => ds1TexLazy.Value.Dds(n),
                ZoneFogIds = msb1.Parts.Collisions.Where(c => c.HitFilterID == 8).Select(c => (int)c.FogID).ToHashSet(),
                ViewZoneFog = o.Ds1Atmosphere ? ViewZone.FogAt(dsrRoot, o.Ds1Map, msb1, drawParam, o.BonfirePos) : null, // rule S3d
                // session 11 (atmos11), rule S3f (LR_NTOA_FADE=0 turns it off: A/B lane)
                WalkPoints = Environment.GetEnvironmentVariable("LR_NTOA_FADE") == "0" ? null : Ds1Unlit.Dedupe(LightProbes.WalkSamples(dsrRoot, o.Ds1Map, msb1, floorsOnly: true)),
            } : null,
        };
        var tMsb = MSB2.Read(Path.Combine(data, "map", tId, tId + ".msb"));

        // ---- containers from the template map, emptied
        BXF4 Empty(string bhd, string bdt) { var b = BXF4.Read(Path.Combine(mdir, bhd), Path.Combine(mdir, bdt)); b.Files.Clear(); return b; }
        var tMap = BXF4.Read(Path.Combine(mdir, tId + ".mapbhd"), Path.Combine(mdir, tId + ".mapbdt"));
        var flvTemplateFile = tMap.Files.First(f => f.Name.EndsWith(".flv.dcx", StringComparison.OrdinalIgnoreCase));
        var flvTemplate = FLVER2.Read(DCX.Decompress(flvTemplateFile.Bytes, out DCX.CompressionInfo dcx));
        var mteFile = tMap.Files.First(f => f.Name.EndsWith(".mte.dcx", StringComparison.OrdinalIgnoreCase));
        var tAcbFile = tMap.Files.First(f => f.Name.EndsWith(".acb", StringComparison.OrdinalIgnoreCase));
        var tAcb = ACB.Read(tAcbFile.Bytes);
        var map = Empty(tId + ".mapbhd", tId + ".mapbdt");
        var tex = Empty($"t{tTail}.tpfbhd", $"t{tTail}.tpfbdt");
        // The _low archive is one BND4 of 128x128 "low_NAME" copies (DS2 opens only the .tpfbdt).
        var texLow = BND4.Read(Path.Combine(mdir, $"t{tTail}_low.tpfbdt"));
        var lowTemplate = texLow.Files[0];
        texLow.Files.Clear();
        var tGi = BXF4.Read(Path.Combine(mdir, $"g{tTail}.gibhd"), Path.Combine(mdir, $"g{tTail}.gibdt"));
        var gi = Empty($"g{tTail}.gibhd", $"g{tTail}.gibdt");
        var hi = Empty($"h{tTail}.hkxbhd", $"h{tTail}.hkxbdt");
        var lo = Empty($"l{tTail}.hkxbhd", $"l{tTail}.hkxbdt");
        string folder = nId;
        int id = 0;
        BinderFile File_(BinderFile flagsFrom, string name, byte[] bytes) => new(flagsFrom.Flags, id++, $@"{folder}\{name}", bytes);

        // ACB: our assets plus the template's light/GI general entries, re-pathed.
        var acb = new ACB { BigEndian = tAcb.BigEndian, Assets = new List<ACB.Asset>() };
        string Repath(string? p) => (p ?? "").Replace(tId, nId);
        foreach (var g in tAcb.Assets.OfType<ACB.Asset.General>().Where(g => g.AbsolutePath.Contains(@"\light\")))
            acb.Assets.Add(new ACB.Asset.General { AbsolutePath = Repath(g.AbsolutePath), RelativePath = g.RelativePath });
        // session 11 (walk11), rule AC1: the map-piece render settings are the template's MOST COMMON lit piece settings,
        // not its first entry (TB's first had CheapRenderDistance 400: "simplified rendering" past 400 m, which no
        // vanilla big walkable piece uses — TB m0000 1091 m, Majula m1620, Heide m0030: cheap 0; 39 of TB's 51 lit
        // pieces: draw 0, lod 0, low-texture 20, cheap 0). DS1's pieces are huge (Firelink up to 8 km with the sky).
        var acbModel = tAcb.Assets.OfType<ACB.Asset.Model>().Where(m => m.RenderType == 0 && !m.DisableShadowSource)
            .GroupBy(m => (m.DrawDistance, m.MeshLodRate, m.LowTextureDistance, m.CheapRenderDistance, m.Unk2E, m.Unk27, m.Unk2C))
            .OrderByDescending(g => g.Count()).First().First();
        var acbGi = tAcb.Assets.OfType<ACB.Asset.GITexture>().First(g => g.AbsolutePath.Contains(@"\hq_"));
        var acbHkt = tAcb.Assets.OfType<ACB.Asset.General>().First(g => g.RelativePath?.StartsWith("model_hkt/") == true);
        var tModelPart = tMsb.Parts.MapPieces[0];
        var tColPart = tMsb.Parts.Collisions[0];

        // ---- map pieces
        var msb = new MSB2();
        string area = o.Ds1Map.Substring(1, 2);
        // session 8 (skylight) rules S1/S2: the ACB render settings of DS2's own SkyDome2[NoFog] model for DS1 skies; DS1's
        // all-unlit background rings get RenderType 2 + no shadows like DS2's (m10_15 m0066, m10_27 m0010)
        var skyAcb = o.Ds1Sky ? Sky.Ds2SkyAcb(data, materials) : null;
        if (o.Ds1Sky) skyLog.Add(skyAcb != null
            ? $"sky: ACB settings for sky models from DS2's {materials.ByMtd[Sky.SkyMtd].Example}: render {skyAcb.RenderType}, no shadow source/target {skyAcb.DisableShadowSource}/{skyAcb.DisableShadowTarget}, fix to camera {skyAcb.FixToCamera}, u27 {skyAcb.Unk27} u2C {skyAcb.Unk2C} u2E {skyAcb.Unk2E} u35 {skyAcb.Unk35}"
            : "sky: DS2's SkyDome2[NoFog] model not found: sky models keep the map piece ACB settings");
        ACB.Asset.Model AcbFor(string name)
        {
            if (!o.Ds1Sky || !conv.Kinds.TryGetValue(name, out var kind)) return acbModel;
            // only an all-unlit sky model (rule S1b splits the lit meshes off): DS2 has no sky model with a lit material
            if (kind.HasSky && kind.AllUnlit && skyAcb != null) { skyLog.Add($"sky: {name} is a sky dome ({kind.Extent:0} m): DS2 sky ACB (rule S1)"); return skyAcb; }
            if (kind.AllUnlit && kind.Extent > Sky.BackgroundExtent)
            {
                skyLog.Add($"sky: {name} is an unlit background piece ({kind.Extent:0} m): RenderType 2, no shadows (rule S2)");
                return new ACB.Asset.Model
                {
                    AbsolutePath = acbModel.AbsolutePath, RelativePath = acbModel.RelativePath, Unk0A = acbModel.Unk0A, Members = acbModel.Members,
                    DrawDistance = acbModel.DrawDistance, MeshLodRate = acbModel.MeshLodRate, Reflectible = false, NormalInteraction = acbModel.NormalInteraction,
                    Unk20 = acbModel.Unk20, RenderType = 2, DisableShadowSource = true, DisableShadowTarget = true, Unk27 = acbModel.Unk27, Unk28 = acbModel.Unk28,
                    Unk2C = acbModel.Unk2C, FixToCamera = false, Unk2E = acbModel.Unk2E, LowTextureDistance = acbModel.LowTextureDistance,
                    CheapRenderDistance = acbModel.CheapRenderDistance, Unk34 = acbModel.Unk34, Unk35 = acbModel.Unk35, Unk36 = acbModel.Unk36, Unk37 = acbModel.Unk37,
                };
            }
            return acbModel;
        }
        // names DS1's own models take (m0000B1 → m0000): a split-off sky model must not take one of them
        var ds1Names = msb1.Models.MapPieces.Select(m => $"m{m.Name.Substring(1, 4)}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        void AddModel(string name, FLVER2 ds1Flver)
        {
            var ds2 = conv.Convert(ds1Flver, name, flvTemplate);
            map.Files.Add(File_(flvTemplateFile, name + ".flv.dcx", DCX.Compress(ds2.Write(), dcx)));
            map.Files.Add(File_(mteFile, name + ".mte.dcx", mteFile.Bytes));
            var acbT = AcbFor(name);
            acb.Assets.Add(CloneModel(acbT, Repath(acbModel.AbsolutePath).Replace(Path.GetFileName(acbModel.AbsolutePath), name + ".sib"), $"model_sib/{name}.sib", ds2));
            msb.Models.MapPieces.Add(new MSB2.Model.MapPiece { Name = name });
        }
        var variantDrops = new Lazy<Dictionary<string, HashSet<int>>>(() => Sky.VariantDrops(dsrRoot, o.Ds1Map, msb1, skyLog));
        // rule GI2 (session 11 burg11, play-test fix): the map's own visible geometry (no sky domes / backdrops: models over
        // 400 m) for the cover test — a point is covered when a piece surface lies 0.5..60 m straight above it
        if (o.CoverGi)
        {
            var bigModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var model in msb1.Models.MapPieces)
            {
                string f = Path.Combine(dsrRoot, "map", o.Ds1Map, $"{model.Name}A{area}.flver.dcx");
                if (!H2Merge.Exists(f)) continue;
                var h = FLVER2.Read(DCX.Decompress(H2Merge.ReadAllBytes(f))).Header;
                var ext = h.BoundingBoxMax - h.BoundingBoxMin;
                if (MathF.Max(ext.X, MathF.Max(ext.Y, ext.Z)) > 400) bigModels.Add(model.Name);
            }
            var bigParts = msb1.Parts.MapPieces.Where(p => bigModels.Contains(p.ModelName)).Select(p => p.Name).ToHashSet();
            var all = FloorProbe.FromDs1Pieces(dsrRoot, o.Ds1Map, Vector3.Zero, 1e6f);
            var cover = new FloorProbe(all.Triangles.Where(t => !bigParts.Contains(t.Part)));
            conv.Covered = p => cover.Down(p.X, p.Z).Any(hit => hit.Y > p.Y + 0.5f && hit.Y < p.Y + 60f);
            pack.Note($"{nId}: rule GI2: cover test over {cover.Count} piece triangles ({bigModels.Count} sky/backdrop models left out)");
        }
        foreach (var model in msb1.Models.MapPieces)
        {
            string ds1File = Path.Combine(dsrRoot, "map", o.Ds1Map, $"{model.Name}A{area}.flver.dcx");
            // rule GI2: the model's (first) placement for the cover test
            if (msb1.Parts.MapPieces.FirstOrDefault(p => p.ModelName == model.Name) is { } firstPart)
                conv.Placement = FloorProbe.Place(firstPart.Position, firstPart.Rotation, firstPart.Scale);
            if (!H2Merge.Exists(ds1File)) continue; // session 11 (burg11, round 8): rule H2-B (a merged map's renamed pieces)
            string name = $"m{model.Name.Substring(1, 4)}";                       // m0000B1 → m0000 (DS2 map models are mNNNN)
            if (pieceModels.ContainsValue(name)) name = $"m9{pieceModels.Count:D3}";
            pieceModels[model.Name] = name;
            ds1Of[name] = model.Name;
            var flv = FLVER2.Read(DCX.Decompress(H2Merge.ReadAllBytes(ds1File)));
            // session 11 (atmos11), rule S4b: meshes another model already places identically for disjoint draw groups
            if (o.Ds1Sky && !o.Dg1 && variantDrops.Value.TryGetValue(model.Name, out var drop)) // rule DG1: the twins draw in their own groups
                flv.Meshes = flv.Meshes.Where((_, i) => !drop.Contains(i)).ToList();
            // rule S1b: which materials (with meshes) are the sky, which are lit
            var used = flv.Meshes.Select(m => m.MaterialIndex).Distinct().Order().ToList();
            var sky = used.Where(i => conv.IsSkyMaterial(flv, i)).ToList();
            var lit = used.Where(i => !MapPieceConverter.IsUnlit(flv.Materials[i].MTD)).ToList();
            if (sky.Count > 0 && lit.Count > 0)
            {
                // the sky material(s) + the model's other unlit materials (cloud layers: DS2 keeps NoLit materials in its
                // sky models) as their own model, dome first; the lit meshes stay a map piece under the DS1 name
                var skyOrder = sky.Concat(used.Where(i => !sky.Contains(i) && !lit.Contains(i))).ToList();
                string skyName = Enumerable.Range(0, 1000).Select(k => $"m9{999 - k:D3}")
                    .First(n => !ds1Names.Contains(n) && !pieceModels.ContainsValue(n) && !skyModels.ContainsValue(n));
                skyModels[model.Name] = skyName;
                ds1Of[skyName] = model.Name;
                skyLog.Add($"sky: {model.Name} holds a sky material and lit meshes: sky materials [{string.Join(",", skyOrder.Select(i => $"{i} '{flv.Materials[i].Name}'"))}] → model {skyName} (DS2 sky ACB), lit materials [{string.Join(",", lit.Select(i => $"{i} '{flv.Materials[i].Name}'"))}] stay in {name} (map-piece ACB) (rule S1b)");
                AddModel(skyName, MapPieceConverter.Subset(flv, skyOrder));
                AddModel(name, MapPieceConverter.Subset(flv, lit));
                continue;
            }
            // a sky model whose dome is not material 0: dome first, like DS2's (only reorders; same meshes)
            if (sky.Count > 0)
            {
                var order = sky.Concat(Enumerable.Range(0, flv.Materials.Count).Where(i => !sky.Contains(i))).ToList();
                bool sorted = order.SequenceEqual(Enumerable.Range(0, flv.Materials.Count))
                    && flv.Meshes.Select(m => m.MaterialIndex).Zip(flv.Meshes.Skip(1).Select(m => m.MaterialIndex)).All(z => z.First <= z.Second);
                if (!sorted) { flv = MapPieceConverter.Subset(flv, order); skyLog.Add($"sky: {model.Name}: materials reordered dome first (rule S1b)"); }
            }
            AddModel(name, flv);
        }
        int pi = 0;
        // rule DG1: identical placements (rule S4 keeps one) draw in the union of their DS1 groups
        var pieceGroups = new Lazy<Dictionary<(string, Vector3, Vector3, Vector3), uint[]>>(() => msb1.Parts.MapPieces
            .GroupBy(x => (x.ModelName, x.Position, x.Rotation, x.Scale))
            .ToDictionary(g => g.Key, g => Enumerable.Range(0, 4).Select(i => g.Aggregate(0u, (acc, x) => acc | x.DrawGroups[i])).ToArray()));
        foreach (var part in o.Ds1Sky ? placedPieces : msb1.Parts.MapPieces)
        {
            if (!pieceModels.TryGetValue(part.ModelName, out string? name)) continue;
            foreach (string n in skyModels.TryGetValue(part.ModelName, out string? skyName) ? new[] { skyName, name } : [name])
            {
                var p = (MSB2.Part.MapPiece)tModelPart.DeepCopy();
                p.Name = $"{n}_{pi++:D4}"; p.ModelName = n; p.Position = part.Position; p.Rotation = part.Rotation; p.Scale = part.Scale;
                if (o.Dg1) Dg1Groups(p, pieceGroups.Value.GetValueOrDefault((part.ModelName, part.Position, part.Rotation, part.Scale), part.DrawGroups));
                msb.Parts.MapPieces.Add(p);
            }
        }

        // ---- DS1 decoration objects as static map pieces (urns, pots, bags, tubs, gargoyles, pillars, sewer columns):
        // the intact model (objbnd file 200) when it has one node and no skinned meshes; placed like DS1's object parts.
        // No collision yet. Interactive objects (doors, bars, ladders, levers, fog walls, breakable floors) and posed
        // corpses wait for objects to be ported as real DS2 objects.
        // Session 8 (user: "pots should be actual items you can roll into and break; you no-clip through the columns"):
        // decoration with a DS1 collision file becomes a real DS2 object instead (ObjPorter): DS1 ObjectParam hp -1 or
        // defense > 0 → Decor (static, DS1 collision; the church pillars only the Asylum Demon can break: defense 90),
        // otherwise → Breakable on DS2's jar o00_2100. Only objects without DS1 collision stay map pieces.
        var objTex = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var objModels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var decorPorts = new List<ObjPorter.Options>();
        var scenery = new List<Placement>();                                              // session 8 (track nests)
        var sceneryParts = new HashSet<string>();
        var ds1ObjectParam = Ds1Source.LoadParam(dsrRoot, "ObjectParam");
        foreach (var model in msb1.Models.Objects)
        {
            if (!DecorObjects.Contains(model.Name)) continue;
            string bndPath = Path.Combine(dsrRoot, "obj", model.Name + ".objbnd.dcx");
            if (!File.Exists(bndPath)) continue;
            var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(bndPath)));
            if (bnd.Files.Any(f => Path.GetFileName(f.Name).Equals(model.Name + ".hkx", StringComparison.OrdinalIgnoreCase)))
            {
                var row = ds1ObjectParam?.Rows.FirstOrDefault(r => r.ID == int.Parse(model.Name[1..]));
                int hp = row != null ? Convert.ToInt32(row["hp"].Value) : -1, def = row != null ? Convert.ToInt32(row["defense"].Value) : 0;
                bool breakable = hp != -1 && def == 0 && bnd.Files.Any(f => Path.GetFileName(f.Name).Equals(model.Name + "_1.hkx", StringComparison.OrdinalIgnoreCase));
                // session 8 (track potroll): DS1's own break trigger → DS2's break action. ObjectParam.isBreakByPlayerCollide
                // 1 (tubs o8400, urn o8403: break when the player walks into them) → DS2's contact-break instance type
                // 1150 (汎用-接触破壊-置物, breakAction 0: any contact); 0 (church urns o8410/o8411) → the jar's 1906
                // (breakAction 4 "Avoidance": a roll breaks it, walking only pushes against it), as DS2's own urns.
                bool byCollide = row != null && Convert.ToInt32(row["isBreakByPlayerCollide"].Value) != 0;
                string ds2Name = MapHosts.ObjectName(nId.Substring(1, 2), model.Name);
                decorPorts.Add(breakable
                    ? new ObjPorter.Options(model.Name, ds2Name, "o00_2100", [], ObjPorter.Kind.Breakable, byCollide ? ContactBreakInstanceType : 0)
                    : new ObjPorter.Options(model.Name, ds2Name, "o16_1230", [], ObjPorter.Kind.Decor));
                pack.Note($"object {model.Name} (DS1 hp {hp}, defense {def}, break by player collision {(byCollide ? 1 : 0)}) → {ds2Name} {(breakable ? "breakable" : "static with collision")}");
                continue;
            }
            var flvFile = bnd.Files.FirstOrDefault(f => f.ID == 200 && f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase));
            if (flvFile == null) continue;
            var objFlver = FLVER2.Read(flvFile.Bytes);
            if (objFlver.Nodes.Count > 1 || objFlver.Meshes.Any(m => m.Dynamic != 0)) { pack.Note($"object {model.Name}: skipped (skinned or multi-node)"); continue; }
            // session 8 (track nests): parts DS1's events show/hide → Scenery objects (instance rows, 130702), the rest
            // stay map pieces (the crow's nest: the cliff nest 1811300 is shown by 11810150 once 11810000; Snuggly's not)
            var shown = msb1.Parts.Objects.Where(p => p.ModelName == model.Name && IsScenery(dsrRoot, o.Ds1Map, p)).ToList();
            if (shown.Count > 0 && !o.Scenery)
            {
                // fix pass: no translated script would show/hide them (build-pack --no-asylum-script): left out, like the
                // script's own objects, i.e. DS1's state before its events show them (the cliff nest before the crow trip)
                foreach (var p in shown) sceneryParts.Add(p.Name);
                pack.Note($"object {model.Name}: parts {string.Join(", ", shown.Select(p => p.Name))} shown/hidden by DS1's events, but the map's events aren't translated: left out");
                if (shown.Count == msb1.Parts.Objects.Count(p => p.ModelName == model.Name)) continue;
            }
            else if (shown.Count > 0)
            {
                string ds2Name = MapHosts.ObjectName(nId.Substring(1, 2), model.Name);
                decorPorts.Add(new ObjPorter.Options(model.Name, ds2Name, SceneryTemplate, [], ObjPorter.Kind.Scenery));
                int prefix = int.Parse(nId.Substring(1, 2) + nId.Substring(4, 2));
                foreach (var p in shown)
                {
                    int inst = prefix * 10000 + p.EntityID % 10000;
                    scenery.Add(new Placement(ds2Name, p.Position, p.Rotation, inst, PlacementKind.Generic, 0,
                        $"(DS1 {p.Name}, entity {p.EntityID}) scenery object: DS1's events show/hide it") { Scale = p.Scale });
                    sceneryParts.Add(p.Name);
                    PlacedObjects[(nId, p.EntityID)] = inst;
                }
                pack.Note($"object {model.Name} (no DS1 collision): parts {string.Join(", ", shown.Select(p => $"{p.Name} (entity {p.EntityID})"))} shown/hidden by DS1's events → {ds2Name} scenery object(s), template {SceneryTemplate}, no collision; other parts map pieces");
                if (shown.Count == msb1.Parts.Objects.Count(p => p.ModelName == model.Name)) continue;
            }
            foreach (var tf in bnd.Files.Where(f => f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase)))
                foreach (var t in TPF.Read(tf.Bytes).Textures) objTex.TryAdd(t.Name, t.Bytes);
            string name = "m" + model.Name.Substring(1, 4);
            if (pieceModels.ContainsValue(name) || skyModels.ContainsValue(name)) continue;
            var ds2Obj = conv.Convert(objFlver, name, flvTemplate);
            map.Files.Add(File_(flvTemplateFile, name + ".flv.dcx", DCX.Compress(ds2Obj.Write(), dcx)));
            map.Files.Add(File_(mteFile, name + ".mte.dcx", mteFile.Bytes));
            acb.Assets.Add(CloneModel(acbModel, Repath(acbModel.AbsolutePath).Replace(Path.GetFileName(acbModel.AbsolutePath), name + ".sib"), $"model_sib/{name}.sib", ds2Obj));
            msb.Models.MapPieces.Add(new MSB2.Model.MapPiece { Name = name });
            objModels[model.Name] = name;
        }
        int oi = 0;
        foreach (var part in msb1.Parts.Objects)
        {
            if (!objModels.TryGetValue(part.ModelName, out string? name)) continue;
            if (sceneryParts.Contains(part.Name)) continue; // a scenery object instead (track nests)
            var p = (MSB2.Part.MapPiece)tModelPart.DeepCopy();
            p.Name = $"{name}_{oi++:D4}"; p.ModelName = name; p.Position = part.Position; p.Rotation = part.Rotation; p.Scale = part.Scale;
            if (o.Dg1) Dg1Groups(p, part.DrawGroups); // rule DG1: the decoration object's own groups
            msb.Parts.MapPieces.Add(p);
        }
        // ---- textures + lightmaps (+ the template's GI light files)
        foreach (var c in conv.RoleClashes.Order()) pack.Note($"{nId}: rule TX2: DS1 texture in two roles: {c}");
        var ds1Tex = ds1TexLazy.Value;
        // objects share textures across areas (Firelink's ivy urns use m10_ivy_*): fall back to every DS1 map area
        List<Ds1MapTextures>? allAreas = null;
        byte[]? AnyArea(string n)
        {
            allAreas ??= Directory.GetDirectories(Path.Combine(dsrRoot, "map")).Select(Path.GetFileName)
                .Where(d => d!.Length == 3 && d[0] == 'm').Select(d => new Ds1MapTextures(dsrRoot, d!)).ToList();
            return allAreas.Select(a => a.Dds(n)).FirstOrDefault(b => b != null);
        }
        byte[]? TexBytes(string n) => ds1Tex.Dds(n) ?? objTex.GetValueOrDefault(n) ?? AnyArea(n);
        var texTemplate = BXF4.Read(Path.Combine(mdir, $"t{tTail}.tpfbhd"), Path.Combine(mdir, $"t{tTail}.tpfbdt")).Files[0];
        // the template's light files, except its probe cloud: our own over this map's bounds (LightProbes; TB's probes
        // are at TB's map-local coordinates, so every object rendered black, session 7)
        var bounds = PieceBounds(msb1, pieceModels, map);
        foreach (var f in tGi.Files.Where(f => !f.Name.Contains("_gi_", StringComparison.OrdinalIgnoreCase) && !f.Name.EndsWith(".acb", StringComparison.OrdinalIgnoreCase)))
        {
            byte[] bytes = f.Bytes;
            if (Path.GetFileName(f.Name).Replace(".dcx", "", StringComparison.OrdinalIgnoreCase).Equals("pointcloud.btpb", StringComparison.OrdinalIgnoreCase))
            {
                // session 11: rule LP2 layout (atmos11) with rule LP1 payloads (burg11, MapObjects maps); LR_PROBES=lp1 = the session-7 box
                var lp1 = o.AllBonfires && o.Ds1Atmosphere ? Ds1Probes.For(dsrRoot, o.Ds1Map, msb1) : null;
                string lp2Note = "";
                var probes = Environment.GetEnvironmentVariable("LR_PROBES") == "lp1"
                    ? LightProbes.Make(bounds.Min - new Vector3(3, 3, 3), bounds.Max + new Vector3(3, 6, 3), payloadAt: lp1 == null ? null : lp1.At)
                    : LightProbes.MakeSparse(LightProbes.WalkSamples(dsrRoot, o.Ds1Map, msb1), out lp2Note, lp1 == null ? null : lp1.At);
                if (lp1 != null) pack.Note($"{nId}: {lp1.Note}");
                if (DCX.Is(f.Bytes)) { DCX.Decompress(f.Bytes, out DCX.CompressionInfo pci); bytes = DCX.Compress(probes, pci); } else bytes = probes;
                pack.Note(lp2Note.Length > 0 ? $"{nId}: light probes {lp2Note} ({probes.Length:N0} bytes)" : $"{nId}: light probes over {bounds.Min} .. {bounds.Max} ({probes.Length:N0} bytes; LR_PROBES=lp1, rule LP1)");
            }
            gi.Files.Add(File_(f, Path.GetFileName(f.Name), bytes));
        }
        foreach (var req in conv.Textures.Values.OrderBy(r => r.Name))
        {
            var tpf = MapPieceConverter.MakeTpf(req, TexBytes, hqGi: true, o.GiGain);
            byte[] bytes = DCX.Compress(tpf.Write(), dcx);
            if (req.Kind is MapPieceConverter.TexKind.Lightmap or MapPieceConverter.TexKind.GreyGi or MapPieceConverter.TexKind.DarkGi)
            {
                gi.Files.Add(File_(texTemplate, req.Name + ".tpf.dcx", bytes));
                acb.Assets.Add(new ACB.Asset.GITexture { AbsolutePath = Repath(acbGi.AbsolutePath).Replace(Path.GetFileName(acbGi.AbsolutePath), $"hq_{req.Name}.tga"), RelativePath = acbGi.RelativePath, Unk10 = acbGi.Unk10, Unk14 = acbGi.Unk14 });
            }
            else
            {
                tex.Files.Add(File_(texTemplate, req.Name + ".tpf.dcx", bytes));
                var low = MapPieceConverter.MakeTpf(req, TexBytes, hqGi: true, o.GiGain, maxSize: 128);
                texLow.Files.Add(new BinderFile(lowTemplate.Flags, texLow.Files.Count, $@"{folder}\low_{req.Name}.tpf.dcx", DCX.Compress(low.Write(), dcx)));
                acb.Assets.Add(new ACB.Asset.Texture { AbsolutePath = $@"N:\FRPG2_64\data\model\map\{nId}\tex\{req.Name}.tga", RelativePath = $"tex/{req.Name}.tga" });
            }
        }

        // ---- collision
        var tHi = BXF4.Read(Path.Combine(mdir, $"h{tTail}.hkxbhd"), Path.Combine(mdir, $"h{tTail}.hkxbdt"));
        var hkxFile = tHi.Files.First(f => f.Name.EndsWith(".hkx.dcx", StringComparison.OrdinalIgnoreCase));
        var hkxTemplate = new HkPackfile(DCX.Decompress(hkxFile.Bytes, out DCX.CompressionInfo hkxDcx));
        string d1 = Path.Combine(dsrRoot, "map", o.Ds1Map);
        var ds1Hi = BXF3.Read(Path.Combine(d1, $"h{o.Ds1Map[1..]}.hkxbhd"), Path.Combine(d1, $"h{o.Ds1Map[1..]}.hkxbdt"));
        var ds1Lo = BXF3.Read(Path.Combine(d1, $"l{o.Ds1Map[1..]}.hkxbhd"), Path.Combine(d1, $"l{o.Ds1Map[1..]}.hkxbdt"));
        var colModels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int tris = 0;
        // Only DS1's ordinary collision (HitFilterID 8) becomes DS2 map collision. DS1's other filters are special hulls
        // DS2 has no counterpart for in a plain collision part: 9/16 = flat water-surface planes (the Asylum pool at y 190
        // over its floor at 189) and simplified copies, 13/15 = box hulls around the boss hall, 22 = a special piece.
        // As ordinary collision they were invisible walls and walkable water (session 6, user report). DS1's own
        // invisible walls (filter 8, e.g. h0098/h0099 above the arena) are kept.
        const int Ds1NormalHit = 8;
        var special = msb1.Parts.Collisions.Where(p => p.HitFilterID != Ds1NormalHit).ToList();
        foreach (var p in special) pack.Note($"{nId}: DS1 collision {p.Name} (hit filter {p.HitFilterID}) left out: special collision, not walls for the player");
        // Collision that DS1 switches off when the map starts (event 0: 2011:1 (entity, 0), "deactivate collision") or marks
        // DisableStart is state collision its events turn on later (session 6, user: "invisible walls in doorways when
        // walking up stairs": the Asylum's h0550B1_0000, a 1x3 m panel at the top of the boulder-corridor stairs, which DS1
        // enables only during the boulder event 11810312). Until DS1's events are ported, such parts are left out.
        var startDisabled = new HashSet<int>();
        string emevdPath = Path.Combine(dsrRoot, "event", o.Ds1Map + ".emevd.dcx");
        if (File.Exists(emevdPath))
            foreach (var ins in EMEVD.Read(emevdPath).Events.Where(e => e.ID == 0).SelectMany(e => e.Instructions))
                if (ins.Bank == 2011 && ins.ID == 1 && ins.ArgData.Length >= 5 && ins.ArgData[4] == 0)
                    startDisabled.Add(BitConverter.ToInt32(ins.ArgData, 0));
        // Session 8: collisions DS1's events toggle (2011:1 anywhere in the map's script) are placed in hit groups of their
        // own instead (MSB2 Collision.UnkT3C = HitGroupParam row, see Events.EntityRules); the translated events switch
        // them with 132153 (event 0 switches the start-disabled ones off at load, as DS1 does).
        var toggled = o.HitGroups ? ToggledCollisions(dsrRoot, o.Ds1Map, msb1) : [];
        foreach (var p in msb1.Parts.Collisions.Where(p => p.HitFilterID == Ds1NormalHit && (p.DisableStart || startDisabled.Contains(p.EntityID)) && !toggled.ContainsKey(p.EntityID)))
            pack.Note($"{nId}: DS1 collision {p.Name} (entity {p.EntityID}) left out: disabled at start by DS1 (event 0 / DisableStart)");
        foreach (var (e, g) in toggled) pack.Note($"{nId}: DS1 collision entity {e} toggled by DS1's events → hit group {g}");
        bool Placed(MSB1.Part.Collision p) => p.HitFilterID == Ds1NormalHit && (toggled.ContainsKey(p.EntityID) || !p.DisableStart && !startDisabled.Contains(p.EntityID));
        var normalModels = msb1.Parts.Collisions.Where(Placed).Select(p => p.ModelName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var model in msb1.Models.Collisions.Where(m => normalModels.Contains(m.Name)))
        {
            string hName = $"h0{model.Name[6]}_{model.Name.Substring(1, 4)}"; // h0005B1 → h01_0005 (DS2 names are hAA_BBBB)
            bool ok = false;
            foreach (var (bxf, src, kind) in new[] { (hi, ds1Hi, 'h'), (lo, ds1Lo, 'l') })
            {
                var f = src.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith($"{kind}{model.Name[1..]}A", StringComparison.OrdinalIgnoreCase));
                if (f == null) continue;
                string name = kind + hName[1..];
                CollisionConverter.Result r;
                try { r = CollisionConverter.Convert(DCX.Decompress(f.Bytes), hkxTemplate, name); }
                catch (Exception e) when (e is NullReferenceException or InvalidCastException or NotSupportedException or ArgumentOutOfRangeException)
                { pack.Note($"{nId}: collision {model.Name} ({kind}): not converted ({e.GetType().Name}: {e.Message}), left out"); continue; } // session 11 (burg11, round 8)
                bxf.Files.Add(File_(hkxFile, name + ".hkx.dcx", DCX.Compress(r.Bytes, hkxDcx)));
                if (kind == 'h') { ok = true; tris += r.Triangles; }
            }
            if (!ok) { pack.Note($"{nId}: collision {model.Name}: no DS1 hkx file, skipped"); continue; }
            if (colModels.ContainsValue(hName)) pack.Note($"{nId}: collision {model.Name}: name {hName} already used!");
            colModels[model.Name] = hName;
            msb.Models.Collisions.Add(new MSB2.Model.Collision { Name = hName });
            acb.Assets.Add(new ACB.Asset.General { AbsolutePath = Repath(acbHkt.AbsolutePath).Replace(Path.GetFileName(acbHkt.AbsolutePath), hName + ".hkt"), RelativePath = $"model_hkt/{hName}.hkt" });
        }
        // ---- atmosphere (session 8): DS1 draw-param zones → DS2 filter ids, calibration, best-fit DS2 area, player lights
        var atmosLog = new List<string>();
        var placedCols = msb1.Parts.Collisions.Where(p => Placed(p) && colModels.ContainsKey(p.ModelName)).ToList();
        var colTris = o.Ds1Atmosphere ? Atmosphere.CollisionTriangles(dsrRoot, o.Ds1Map) : [];
        Atmosphere.Plan? atmos = o.Ds1Atmosphere ? Atmosphere.Zones(placedCols, atmosLog, p => colTris.GetValueOrDefault(p.ModelName, 1), drawParam,
            member: p => H2Merge.MemberRank(o.Ds1Map, p.Name)) : null; // + rule H2-A (session 11 burg11, round 8)
        Atmosphere.Calibration? cal = null;
        (string Map, int Filter, double Distance) atmosTemplate = (tId, 0, 0);
        IReadOnlyDictionary<int, int> playerLights = new Dictionary<int, int>();
        int atmosEnv = -2;
        IReadOnlyDictionary<string, (int Id, byte Attach)> ambience = new Dictionary<string, (int, byte)>(); // session 11 (atmos11), rule B3
        if (atmos != null)
        {
            cal = Atmosphere.Calibrate(dsrRoot, data);
            atmosLog.Add($"atmos: calibration (DS2 median / DS1 median): {cal.Report}");
            var ranking = Atmosphere.BestTemplate(data, Atmosphere.Ds1Features(drawParam, atmos.Main, cal));
            atmosTemplate = ranking[0];
            atmosLog.Add($"atmos: best-fit DS2 area for the fallback fields (rule A3, DS1's authored fog colour), closest first: {string.Join(", ", ranking.Take(6).Select(r => $"{r.Map} (filter {r.Filter}, d={r.Distance:0.00})"))}; Things Betwixt m10_02_00_00 d={ranking.FirstOrDefault(r => r.Map == "m10_02_00_00").Distance:0.00}");
            if (ranking.Count > 1 && ranking[1].Distance < ranking[0].Distance * (1 + Atmosphere.CloseCall))
                atmosLog.Add($"atmos: rule A3 close call: {ranking[0].Map} d={ranking[0].Distance:0.00} vs {ranking[1].Map} (filter {ranking[1].Filter}) d={ranking[1].Distance:0.00} (within {Atmosphere.CloseCall:P0}); A/B: build-pack --atmos-template {nId}={ranking[1].Map}:{ranking[1].Filter}");
            var scored = Atmosphere.BestTemplate(data, Atmosphere.Ds1Features(drawParam, atmos.Main, cal, scattered: true));
            atmosLog.Add($"atmos: (for comparison, not used) ranked with the H1-scattered fog colour: {string.Join(", ", scored.Take(3).Select(r => $"{r.Map} (filter {r.Filter}, d={r.Distance:0.00})"))}");
            if (o.AtmosTemplate is { } pinned)
            {
                var parts = pinned.Split(':');
                int filter = parts.Length > 1 ? int.Parse(parts[1]) : Atmosphere.UsedFilters(MSB2.Read(Path.Combine(data, "map", parts[0], parts[0] + ".msb"))).FirstOrDefault();
                atmosTemplate = (parts[0], filter, ranking.FirstOrDefault(r => r.Map == parts[0]).Distance);
                atmosLog.Add($"atmos: fallback area PINNED by --atmos-template: {parts[0]} filter {filter} (rule A3 would pick {ranking[0].Map} filter {ranking[0].Filter})");
            }
            playerLights = Atmosphere.PlayerLights(pack, atmos, drawParam, int.Parse(nId.Substring(1, 2) + nId.Substring(4, 2)));
            atmosEnv = Atmosphere.TemplateEnvId(MSB2.Read(Path.Combine(data, "map", atmosTemplate.Map, atmosTemplate.Map + ".msb")), atmosTemplate.Filter);
            atmosLog.Add($"atmos: env cubemap: {atmosTemplate.Map}'s CubeEnv_{atmosEnv:D4} (what its filter {atmosTemplate.Filter} collisions use; DS1's GI_EnvM cubes are BC6H HDR, not converted yet)");
            foreach (var (lantern, row) in playerLights) atmosLog.Add($"atmos: DS1 PointLightBank {lantern} (LanternID) → PlayerLight {row}");
            ambience = Atmosphere.Ambience(msb1, dsrRoot, o.Ds1Map, data, atmosLog); // session 11 (atmos11), rule B3: camera-relative DS1 ambience → collision AmbientSfxID
        }
        // session 11 (burg11), rule EC1 (MapObjects maps): DS1's GI env cubes as the map's own CubeEnv cubes (Ds1EnvLights)
        var ec1 = o.AllBonfires && atmos != null ? Ds1EnvLights.Plan(dsrRoot, o.Ds1Map, msb1, Path.Combine(mdir, $"e{atmosTemplate.Map[1..]}.envbnd.dcx")) : null;
        if (ec1 != null) atmosLog.AddRange(ec1.Log);
        int ci = 0;
        foreach (var part in msb1.Parts.Collisions)
        {
            if (!Placed(part) || !colModels.TryGetValue(part.ModelName, out string? name)) continue;
            var c = (MSB2.Part.Collision)tColPart.DeepCopy();
            c.Name = $"{name}_{ci++:D4}"; c.ModelName = name; c.Position = part.Position; c.Rotation = part.Rotation; c.Scale = Vector3.One;
            PlacedCollisions[(nId, part.Name)] = c.Name; // session 11 (burg11): rule CC1 finds DS1 connects' collisions
            if (atmos != null) { Atmosphere.ApplyCollision(c, atmos.ByPart[part.Name], playerLights, atmosEnv); Atmosphere.ApplyAmbience(c, part.Name, ambience); } // session 11 (atmos11): + rule B3
            // session 11 (burg11), rule SS1 (MapObjects maps): the reverb space (collision T10) is DS1's SoundSpaceType; both
            // games use the same small enum (DS1 Burg 0..4, DS2 vanilla 0..4 for 99% of collisions)
            if (o.AllBonfires) c.UnkT10 = part.SoundSpaceType;
            // rule NET2 (MapNet): the multiplayer area (collision T20 → PlayAreaParam) from DS1's PlayRegionID
            if (o.AllBonfires) c.UnkT20 = MapNet.CollisionArea(MapNet.PlayAreas(Program.DefaultDsrRoot, o.Ds1Map, nId), part.PlayRegionID);
            if (ec1 != null) c.UnkT2E = (short)ec1.CubeFor(part); // rule EC1: the DS1 spot's cube
            if (toggled.TryGetValue(part.EntityID, out int hitGroup)) c.UnkT3C = hitGroup;
            if (o.Dg1) Dg1Groups(c, part.DispGroups); // rule DG1: what DS1 shows from this collision
            msb.Parts.Collisions.Add(c);
        }

        // ---- invisible ramps over DS1's step risers (rule, user decision session 8): extra collision parts h0A_99NN
        // at the origin, built from the walkable DS1 collision placed above
        {
            var world = new List<(Vector3 A, Vector3 B, Vector3 C)>();
            var worldPart = new List<string>(); // DS1 collision part of each triangle (session 8: a ramp takes its atmosphere zone)
            foreach (var part in msb1.Parts.Collisions.Where(p => Placed(p) && !toggled.ContainsKey(p.EntityID) && colModels.ContainsKey(p.ModelName)))
            {
                var f = ds1Hi.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith($"h{part.ModelName[1..]}A", StringComparison.OrdinalIgnoreCase));
                if (f == null) continue;
                var m = Commands.ColSurvey.Place(part.Position, part.Rotation, part.Scale);
                foreach (var (a, b, c) in Commands.ColSurvey.Triangles(DCX.Decompress(f.Bytes)))
                {
                    world.Add((Vector3.Transform(a, m), Vector3.Transform(b, m), Vector3.Transform(c, m)));
                    worldPart.Add(part.Name);
                }
            }
            var rampLog = new List<string>();
            var ramps = StepRamps.Detect(world, rampLog);
            foreach (var line in rampLog) pack.Note($"{nId}: {line}");
            var rampParts = StepRamps.Parts(ramps);
            // session 11 (walk11), rule SR3: ramp models keep DS2's hAA_BBBB form (4 digits). 9900 + i for the first 100 (the
            // verified Asylum/Firelink names), then free numbers counting down from 9899 (the Burg's 3027 ramps made h01_10000
            // .. h01_10030: 5-digit model names DS2 never uses)
            var usedNums = msb1.Models.Collisions.Select(m => int.TryParse(m.Name.AsSpan(1, 4), out int v) ? v : -1).ToHashSet();
            var rampNums = Enumerable.Range(9900, 100).Concat(Enumerable.Range(0, 9900).Reverse().Where(v => !usedNums.Contains(v))).Take(rampParts.Count).ToList();
            for (int i = 0; i < rampParts.Count; i++)
            {
                string hName = $"h0{msb1.Parts.Collisions.First().ModelName[6]}_{rampNums[i]:D4}";
                foreach (var (bxf, kind) in new[] { (hi, 'h'), (lo, 'l') })
                {
                    string name = kind + hName[1..];
                    bxf.Files.Add(File_(hkxFile, name + ".hkx.dcx", DCX.Compress(StepRamps.Build(rampParts[i], hkxTemplate, name), hkxDcx)));
                }
                msb.Models.Collisions.Add(new MSB2.Model.Collision { Name = hName });
                acb.Assets.Add(new ACB.Asset.General { AbsolutePath = Repath(acbHkt.AbsolutePath).Replace(Path.GetFileName(acbHkt.AbsolutePath), hName + ".hkt"), RelativePath = $"model_hkt/{hName}.hkt" });
                var c = (MSB2.Part.Collision)tColPart.DeepCopy();
                c.Name = $"{hName}_{ci++:D4}"; c.ModelName = hName; c.Position = Vector3.Zero; c.Rotation = Vector3.Zero; c.Scale = Vector3.One;
                if (world.Count > 0)
                {
                    // the DS1 collision the ramp lies on (the triangle nearest to its centre): rule DG1 groups, rule SR3c connects
                    var mid = rampParts[i].Aggregate(Vector3.Zero, (acc, t) => acc + (t.A + t.B + t.C) / 3) / Math.Max(1, rampParts[i].Count);
                    int near = Enumerable.Range(0, world.Count).MinBy(t => Vector3.DistanceSquared(mid, (world[t].A + world[t].B + world[t].C) / 3));
                    RampBase[(nId, c.Name)] = worldPart[near];
                    if (o.Dg1) Dg1Groups(c, msb1.Parts.Collisions.First(x => x.Name == worldPart[near]).DispGroups);
                }
                if (atmos != null && world.Count > 0)
                {
                    // the ramp lies on DS1 stairs: the zone of the DS1 collision triangle nearest to its centre
                    var centre = rampParts[i].Aggregate(Vector3.Zero, (s, t) => s + (t.A + t.B + t.C) / 3) / Math.Max(1, rampParts[i].Count);
                    int best = 0; float bestD = float.MaxValue;
                    for (int t = 0; t < world.Count; t++)
                    {
                        float dd = Vector3.DistanceSquared(centre, (world[t].A + world[t].B + world[t].C) / 3);
                        if (dd < bestD) { bestD = dd; best = t; }
                    }
                    Atmosphere.ApplyCollision(c, atmos.ByPart[worldPart[best]], playerLights, atmosEnv);
                    if (ec1 != null) c.UnkT2E = (short)ec1.CubeFor(msb1.Parts.Collisions.First(p => p.Name == worldPart[best])); // rule EC1
                    Atmosphere.ApplyAmbience(c, worldPart[best], ambience); // session 11 (atmos11), rule B3
                }
                msb.Parts.Collisions.Add(c);
            }
            Console.WriteLine($"  {nId}: {ramps.Count} step ramps in {rampParts.Count} collision parts");
            foreach (var r in ramps) pack.Note($"{nId}: ramp rise {r.Rise:0.00} run {r.Run:0.00}{(r.Chained ? " (stairs)" : "")} top {r.TopA}..{r.TopB}");
        }

        // ---- the ACB lives in the map and both collision archives (as in DS2)
        byte[] acbBytes = acb.Write();
        map.Files.Insert(0, new BinderFile(tAcbFile.Flags, -1, $@"{folder}\{nId}.acb", acbBytes));
        hi.Files.Insert(0, new BinderFile(tAcbFile.Flags, -1, $@"{folder}\{nId}.acb", acbBytes));
        lo.Files.Insert(0, new BinderFile(tAcbFile.Flags, -1, $@"{folder}\{nId}.acb", acbBytes));
        foreach (var b in new[] { map, tex, gi, hi, lo }) for (int i = 0; i < b.Files.Count; i++) b.Files[i].ID = i;

        // ---- bonfire: the DS2 bonfire object at DS1's courtyard bonfire
        var tBonfire = tMsb.Parts.Objects.First(x => x.ModelName == "o00_0100");
        msb.Models.Objects.Add(new MSB2.Model.Object { Name = "o00_0100" });
        // session 11 (burg11), rule BF1: one part per DS2 bonfire of the map (index 0 = the single verified one otherwise)
        var bonfirePlan = Bonfires(dsrRoot, o, msb1);
        foreach (var bp in bonfirePlan)
        {
            var bonfire = (MSB2.Part.Object)tBonfire.DeepCopy();
            bonfire.Name = $"o00_0100_{bp.Index:D4}";
            // on the ground exactly where DS1's bonfire object (o0200) stands, with its rotation (session 7, user: "the
            // bonfires should be on the ground, not hovering": the hand-set positions were 0.5 m high, Firelink's unrotated)
            bonfire.Position = bp.Position;
            bonfire.Rotation = bp.Rotation;
            bonfire.MapObjectInstanceParamID = bp.Instance; // m18_01 → 18010650 (DS2: map + 0650, + 5 per further bonfire)
            // DS2 shows (and loads the model of) an object only while the collision under the player activates one of its
            // DrawGroups: a collision's Unk44..Unk50 words are its area bits (TB: bit 0 + one bit per collision, e.g. 0x81),
            // and our collisions all carry TB's first collision's (5). All bits = visible everywhere, as many TB objects
            // are, until DS1's own groups are ported.
            Array.Fill(bonfire.DrawGroups, 0xFFFFFFFF);
            Array.Fill(bonfire.DispGroups, 0xFFFFFFFF);
            msb.Parts.Objects.Add(bonfire);
            if (o.AllBonfires) pack.Note($"{nId}: rule BF1: DS1 bonfire {bp.Entity} at {bp.Position} → bonfire {bp.Id} \"{bp.Name}\" (instance {bp.Instance}, Travel area {bp.MenuAreaId} \"{bp.MenuAreaName}\", event group {bp.Group})");
        }

        // ---- DS1 item pickups (session 6): every MSB treasure event as DS2's item object (TB's o00_0240: objCtrlType 0; its
        // instance row's itemLotId names the ItemLotParam2_Other row, ours = the instance id) at the part the DS1 item lies on. Left out:
        // chests (no chest object ported yet) and lots whose items aren't in DS2 yet (noted in build.log).
        // Session 10 (asy-items), rule C3: every other treasure is placed, also the start-disabled ones (the return trip's
        // Peculiar Doll: event 0 disables their item at every load, as DS1's MSB flag does, and DS1's own 2005:4 enables it:
        // EmevdTranslator load-init) and the ones DS1's events switch (the 24 class corpses: DS1's 11810400 disables all and
        // enables the starting class's three, translated through rule C1). Instance ids stay append-only: the treasures
        // placed before session 10 (neither start-disabled nor a 2005:4 target) keep 6000 + 10k in MSB order, the
        // newly placed ones follow (their old skip was the name "武器"; the data rule is the event switch).
        var pickups = new List<int>();
        var startDisabledPickups = new List<int>();
        var tPickup = tMsb.Parts.Objects.FirstOrDefault(x => x.ModelName == "o00_0240");
        if (o.Pickups && tPickup != null)
        {
            var ds1 = ds1Src ??= new Ds1Source(dsrRoot);
            var ds1Lots = ds1.Params.Params["ItemLotParam"];
            var lots = pack.Param("ItemLotParam2_Other");
            var tLot = lots[tPickup.MapObjectInstanceParamID]!;
            msb.Models.Objects.Add(new MSB2.Model.Object { Name = "o00_0240" });
            int mapPrefix = int.Parse(nId.Substring(1, 2) + nId.Substring(4, 2)); // m18_01 → 1801
            var switched = TreasureSwitches(dsrRoot, o.Ds1Map);
            bool Late(MSB1.Event.Treasure t) => t.StartDisabled || msb1.Parts.GetEntries().FirstOrDefault(p => p.Name == t.TreasurePartName) is { EntityID: > 0 } tp && switched.Contains(tp.EntityID);
            // session 10 (asy-items i2), rule PK1: ids from the append-only table (PickupIds); key = the DS1 treasure part
            // (+ "#n" for a further treasure on the same part, MSB order)
            var usedIds = new HashSet<int>();
            var placedTreasures = new List<(int Inst, MSB1.Event.Treasure T, MSB1.Part Part, List<(int Item, int Num, int Reinforce)> Got)>();
            string PickupKey(MSB1.Event.Treasure t)
            {
                int n = msb1.Events.Treasures.TakeWhile(x => x != t).Count(x => x.TreasurePartName == t.TreasurePartName);
                return n == 0 ? t.TreasurePartName : $"{t.TreasurePartName}#{n}";
            }
            foreach (var t in msb1.Events.Treasures.Where(t => !Late(t)).Concat(msb1.Events.Treasures.Where(Late)))
            {
                var part = msb1.Parts.GetEntries().FirstOrDefault(p => p.Name == t.TreasurePartName);
                if (part == null || t.InChest) { if (part != null) pack.Note($"{nId}: DS1 treasure {t.Name} on {t.TreasurePartName} left out (disabled {t.StartDisabled}, chest {t.InChest})"); continue; }
                var got = new List<(int Item, int Num, int Reinforce)>();
                foreach (int l in t.ItemLots.Where(l => l > 0))
                {
                    if (ds1Lots[l] is not { } row) continue;
                    for (int i = 1; i <= 8; i++)
                    {
                        int ds1Item = Convert.ToInt32(row[$"lotItemId0{i}"].Value);
                        if (ds1Item <= 0) continue;
                        uint cat = unchecked((uint)Convert.ToInt64(row[$"lotItemCategory0{i}"].Value));
                        int ds2 = Chr.Ds1Drops.MapItem(pack, ds1, cat, ds1Item, out string kind, out int reinforce, out string how);
                        if (ds2 == 0) { pack.Note($"{nId}: DS1 treasure {t.TreasurePartName}: {kind} {ds1Item} not in DS2 yet, left out"); continue; }
                        got.Add((ds2, Math.Max(1, Convert.ToInt32(row[$"lotItemNum0{i}"].Value)), reinforce));
                    }
                }
                if (got.Count == 0) continue;
                placedTreasures.Add((PickupIds.Get(pack, nId, PickupKey(t), mapPrefix, usedIds), t, part, got));
            }
            // fix pass (rule PK1b): the pickup parts in id order, so a pickup that becomes placeable later (its new id is
            // the map's largest) is appended and never renames or moves an existing pickup's MSB part (o00_0240_NNNN and
            // its index: Firelink's shipped 19026120 stays o00_0240_0012 when 19026150 appears). The table's ids already
            // follow the shipped MSB order (6000 + 10 x index), so the shipped parts keep their names and indices.
            foreach (var (inst, t, part, got) in placedTreasures.OrderBy(x => x.Inst))
            {
                var lot = pack.AddRow("ItemLotParam2_Other", tLot, inst, $"[Lordran] {nId} {t.TreasurePartName}");
                for (int i = 1; i <= 10; i++)
                {
                    var g = i <= got.Count ? got[i - 1] : default;
                    lot[$"itemId{i}"].Value = Convert.ChangeType(i <= got.Count ? g.Item : 10, lot[$"itemId{i}"].Value.GetType());
                    lot[$"itemNum{i}"].Value = Convert.ChangeType(i <= got.Count ? g.Num : 0, lot[$"itemNum{i}"].Value.GetType());
                    lot[$"reinforceLevel{i}"].Value = Convert.ChangeType(i <= got.Count ? g.Reinforce : 0, lot[$"reinforceLevel{i}"].Value.GetType());
                    lot[$"itemWeight{i}"].Value = Convert.ChangeType(i <= got.Count ? 1f : 0f, lot[$"itemWeight{i}"].Value.GetType());
                }
                var obj = (MSB2.Part.Object)tPickup.DeepCopy();
                obj.Name = $"o00_0240_{pickups.Count:D4}";
                obj.Position = part.Position;
                obj.Rotation = Vector3.Zero;
                obj.MapObjectInstanceParamID = inst;
                Array.Fill(obj.DrawGroups, 0xFFFFFFFF);
                Array.Fill(obj.DispGroups, 0xFFFFFFFF);
                msb.Parts.Objects.Add(obj);
                // o00_0240 is DS2's "treasure corpse" (an adventurer's mummy); the MSB poses it per part (PartPose: bone
                // transforms). Without one it stood in its bind pose, unlit (run s6-9). TB's first corpse lies face down.
                if (tMsb.PartPoses.FirstOrDefault(x => x.PartName == tPickup.Name) is { } tPose)
                {
                    var pose = tPose.DeepCopy();
                    pose.PartName = obj.Name;
                    msb.PartPoses.Add(pose);
                }
                pickups.Add(inst);
                if (part.EntityID > 0) PlacedObjects[(nId, part.EntityID)] = inst;
                if (t.StartDisabled) startDisabledPickups.Add(inst);
                pack.Note($"{nId}: pickup {inst} at {part.Position} ({t.TreasurePartName}{(t.StartDisabled ? ", start-disabled" : "")}{(part.EntityID > 0 && switched.Contains(part.EntityID) ? $", treasure switched by DS1 events: entity {part.EntityID}" : "")}): " + string.Join(", ", got.Select(x => $"{x.Item} x{x.Num}")));
            }
            Console.WriteLine($"  {nId}: {pickups.Count} DS1 item pickups ({startDisabledPickups.Count} start-disabled)");
        }
        StartDisabledPickups[nId] = startDisabledPickups;

        // ---- DS1 objects as real DS2 objects (session 6: the Asylum's barred cell doors), placed like TB's door
        // (o00_0302: objCtrlType 2 = door, objCtrlId 1000 = MapObjectDoorParam) at every DS1 part of the model
        var doors = new List<int>();
        var ladders = new List<(int Inst, string Model)>(); // session 10, rule G4: ladders DS1 registers later than load (own instance rows)
        var eventLadders = EventLadders(dsrRoot, o.Ds1Map, msb1, includeKick: o.AllBonfires);
        foreach (var l in NonG4Ladders(dsrRoot, o.Ds1Map)) pack.Note($"{nId}: {l}");
        var tDoor = tMsb.Parts.Objects.FirstOrDefault(x => x.ModelName == "o00_0302");
        // session 11 (walk11), rule OC1 (maps with hand-written object lists: the Asylum, Firelink; MapObjects maps have OK1):
        // every DS1 object model with DS1 collision that no list ports and none of whose parts DS1's events address becomes
        // a static object with DS1's collision at every part (OK1's collision branch). Before, such parts were silently
        // dropped: the Asylum hall balcony walkways o8700/o8701/o8702 (2F, y 205-210: fall into the void, walk-check FAIL
        // object-missing/void) and Firelink's o1470 / o6100.
        if (!o.AllBonfires)
        {
            var ported = new HashSet<string>(o.Objects.Concat(decorPorts).Select(p => p.Ds1Obj), StringComparer.OrdinalIgnoreCase);
            var touched = EventEntities(dsrRoot, o.Ds1Map);
            var objectParam = Ds1Source.LoadParam(dsrRoot, "ObjectParam");
            foreach (var g in msb1.Parts.Objects.GroupBy(p => p.ModelName, StringComparer.OrdinalIgnoreCase))
            {
                string m = g.Key;
                if (m == "o0200" || ported.Contains(m) || DecorObjects.Contains(m) || objModels.ContainsKey(m)) continue;
                if (g.Any(p => p.EntityID > 0 && (touched.Contains(p.EntityID) || o.ScriptedDoors.Contains(p.EntityID) || PlacedObjects.ContainsKey((nId, p.EntityID))))) continue;
                if (g.Any(p => sceneryParts.Contains(p.Name))) continue;
                if (!Commands.WalkCheck.HasDs1Collision(dsrRoot, m)) continue;
                string ds2Name = MapHosts.ObjectName(nId.Substring(1, 2), m);
                if (o.Objects.Any(p => p.Ds2Name.Equals(ds2Name, StringComparison.OrdinalIgnoreCase))) continue;
                var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(dsrRoot, "obj", m + ".objbnd.dcx"))));
                bool Has(string n) => bnd.Files.Any(f => Path.GetFileName(f.Name).Equals(n, StringComparison.OrdinalIgnoreCase));
                var row = objectParam?.Rows.FirstOrDefault(r => r.ID == int.Parse(m[1..]));
                int hp = row != null ? Convert.ToInt32(row["hp"].Value) : -1, def = row != null ? Convert.ToInt32(row["defense"].Value) : 0;
                bool breakable = hp != -1 && def == 0 && Has(m + "_1.flver") && Has(m + "_1.hkx");
                bool byCollide = row != null && Convert.ToInt32(row["isBreakByPlayerCollide"].Value) != 0;
                decorPorts.Add(breakable
                    ? new ObjPorter.Options(m, ds2Name, "o00_2100", [], ObjPorter.Kind.Breakable, byCollide ? ContactBreakInstanceType : 0)
                    : new ObjPorter.Options(m, ds2Name, "o16_1230", [], ObjPorter.Kind.Decor));
                pack.Note($"{nId}: rule OC1: DS1 object {m} (collision, {g.Count()} part(s) {string.Join(", ", g.Select(p => p.Name))}, no event addresses it) → {ds2Name} {(breakable ? "breakable" : "static with DS1 collision")}");
            }
        }
        // session 11 (walk11), rule OD1: one port per DS2 model. Two rules porting the same model (the Burg's OK1 breakables
        // o1132/o1154/o1155, also ported by a world-event rule) made the per-model loop place every part twice: doubled
        // collision and z-fighting crates (MSB model list had o19_1132 ×2).
        var portList = new List<ObjPorter.Options>();
        // session 11 (burg11, round 7), rule OD1b: a model kept as a script-placed Static port (a BD1 border door) whose dropped twin was
        // the decoration port: its parts no script places (no entity: the Depths' second o1315) are placed as static decoration
        var decorTwins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var po in o.Objects.Concat(decorPorts))
        {
            if (portList.Any(p => p.Ds2Name.Equals(po.Ds2Name, StringComparison.OrdinalIgnoreCase) && p.Variant == po.Variant))
            {
                if (po.Kind == ObjPorter.Kind.Decor && portList.First(p => p.Ds2Name.Equals(po.Ds2Name, StringComparison.OrdinalIgnoreCase)).Kind == ObjPorter.Kind.Static) decorTwins.Add(po.Ds2Name);
                pack.Note($"{nId}: rule OD1: second port of {po.Ds2Name} ({po.Kind} from {po.Ds1Obj}) dropped (first: {portList.First(p => p.Ds2Name.Equals(po.Ds2Name, StringComparison.OrdinalIgnoreCase)).Kind})");
                continue;
            }
            portList.Add(po);
        }
        var failedPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fallbackPlacements = new List<Placement>(); // session 11: parts of a model whose port fell back to Static/Scenery
        if (tDoor != null)
            foreach (var po in portList)
            {
                // session 11 (burg11): a MapObjects map (AllBonfires) tolerates a model the porter can't convert: its parts
                // are left out (ST1 row) instead of failing the build; the verified maps keep failing loudly
                if (o.AllBonfires)
                {
                    // fallback chain (a stand-in, ST1): prefab / ladder → static with DS1 collision → scenery (model only);
                    // breakable → decor → scenery. A fallback keeps the part where DS1 has it (a door stays shut)
                    var chain = new List<ObjPorter.Options> { po };
                    if (po.Kind is ObjPorter.Kind.Prefab or ObjPorter.Kind.Ladder) chain.Add(po with { TemplateObj = "o16_1230", Anims = [], Kind = ObjPorter.Kind.Static });
                    if (po.Kind is ObjPorter.Kind.Breakable) chain.Add(po with { TemplateObj = "o16_1230", Anims = [], Kind = ObjPorter.Kind.Decor, InstanceType = 0 });
                    if (po.Kind != ObjPorter.Kind.Scenery) chain.Add(po with { TemplateObj = SceneryTemplate, Anims = [], Kind = ObjPorter.Kind.Scenery, InstanceType = 0 });
                    ObjPorter.Options? done = null;
                    var errors = new List<string>();
                    foreach (var c in chain)
                        try { ObjPorter.Port(pack, dsrRoot, c, materials); done = c; break; }
                        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or KeyNotFoundException or IndexOutOfRangeException or NullReferenceException or ArgumentException)
                        { errors.Add($"{c.Kind} on {c.TemplateObj}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
                    if (done == null)
                    {
                        failedPorts.Add(po.Ds2Name);
                        pack.Note($"ST1\t{nId}\tport failed\t{po.Ds1Obj}\t0\t{string.Join(" | ", errors)} — its parts are left out");
                        continue;
                    }
                    if (done != po)
                    {
                        pack.Note($"ST1\t{nId}\tport fallback\t{po.Ds1Obj}\t0\t{string.Join(" | ", errors)} → {done.Kind} on {done.TemplateObj}");
                        // a fallback Scenery/Static model is placed one by one like the prefab was; a Decor fallback of a
                        // breakable keeps the per-model loop below (instance 0)
                        if (done.Kind is ObjPorter.Kind.Static or ObjPorter.Kind.Scenery && po.Kind is not (ObjPorter.Kind.Prefab or ObjPorter.Kind.Static or ObjPorter.Kind.Scenery))
                        {
                            // a ladder or breakable without its own placements: place its parts as generic static objects
                            int pfx = int.Parse(nId.Substring(1, 2) + nId.Substring(4, 2)), k = 0;
                            msb.Models.Objects.Add(new MSB2.Model.Object { Name = po.Ds2Name });
                            foreach (var part in msb1.Parts.Objects.Where(x => x.ModelName == po.Ds1Obj && !o.ScriptedDoors.Contains(x.EntityID)))
                                fallbackPlacements.Add(new Placement(po.Ds2Name, part.Position, part.Rotation, part.EntityID > 0 ? pfx * 10000 + part.EntityID % 10000 : pfx * 10000 + 3900 + k++,
                                    PlacementKind.Generic, 0, $"(DS1 {part.Name}) port fallback {done.Kind}") { Scale = part.Scale });
                            continue;
                        }
                    }
                    if (done.Kind is ObjPorter.Kind.Prefab or ObjPorter.Kind.Static or ObjPorter.Kind.Scenery && !(done == po && decorTwins.Contains(po.Ds2Name))) { msb.Models.Objects.Add(new MSB2.Model.Object { Name = po.Ds2Name }); continue; } // rule OD1b: a decor twin goes on below
                }
                else ObjPorter.Port(pack, dsrRoot, po, materials);
                msb.Models.Objects.Add(new MSB2.Model.Object { Name = po.Ds2Name });
                int mapPrefix = int.Parse(nId.Substring(1, 2) + nId.Substring(4, 2));
                bool decorTwin = decorTwins.Contains(po.Ds2Name);
                if (po.Kind is ObjPorter.Kind.Prefab or ObjPorter.Kind.Static or ObjPorter.Kind.Scenery && !decorTwin) continue; // placed by the map's script (Placements) / scenery rule
                if (po.Variant != ObjPorter.TestVariant.None) continue;                // test variants too (session 8, AsylumScript)
                foreach (var part in msb1.Parts.Objects.Where(x => x.ModelName == po.Ds1Obj))
                {
                    if (o.ScriptedDoors.Contains(part.EntityID)) continue;
                    if (decorTwin && part.EntityID > 0) continue; // rule OD1b: the script placed the entity parts
                    // ladders: instance 0 (as DS2 places its own; the behaviour is the model's MapObjectDefaultInstanceParam row)
                    // doors: AABB0000 + the DS1 entity's last 4 digits (cell door 1811103 → 18011103), so scripts can name them
                    bool ladder = po.Kind is ObjPorter.Kind.Ladder or ObjPorter.Kind.Breakable or ObjPorter.Kind.Decor || decorTwin; // instance 0: behaviour from the model (DS2 places its jars so)
                    int inst = ladder ? 0 : part.EntityID > 0 ? mapPrefix * 10000 + part.EntityID % 10000 : mapPrefix * 10000 + 4000 + doors.Count * 10;
                    // session 10, rule G4: a ladder an event registers later gets its own row (the events hide / show it)
                    if (po.Kind == ObjPorter.Kind.Ladder && eventLadders.Contains(part.EntityID))
                    {
                        inst = mapPrefix * 10000 + part.EntityID % 10000;
                        ladders.Add((inst, po.Ds2Name));
                        PlacedObjects[(nId, part.EntityID)] = inst;
                    }
                    var obj = (MSB2.Part.Object)tDoor.DeepCopy();
                    obj.Name = $"{po.Ds2Name}_{msb.Parts.Objects.Count(x => x.ModelName == po.Ds2Name):D4}";
                    obj.ModelName = po.Ds2Name;
                    obj.Position = part.Position;
                    obj.Rotation = part.Rotation;
                    obj.MapObjectInstanceParamID = inst;
                    // UnkT04 = the bonfire the object belongs to (Majula's jars 4650 = the Far Fire, TB's door 2650):
                    // breakables are restored by resting there. Ours were TB's 2650 (copied from its door).
                    if (po.Kind is ObjPorter.Kind.Breakable or ObjPorter.Kind.Decor) obj.UnkT04 = (short)o.BonfireId;
                    Array.Fill(obj.DrawGroups, 0xFFFFFFFF);
                    Array.Fill(obj.DispGroups, 0xFFFFFFFF);
                    // rule DG1 (play-test fix): a static DS1 object no event addresses (pots, crates, decor) is active only
                    // where DS1 shows it (its DrawGroups against the collision's object-area words)
                    if (o.Dg1 && po.Kind is ObjPorter.Kind.Breakable or ObjPorter.Kind.Decor && part.EntityID <= 0 && part.DrawGroups.Any(w => w != 0))
                        for (int gw = 0; gw < 4; gw++) { obj.DrawGroups[gw] = part.DrawGroups[gw]; obj.DispGroups[gw] = part.DrawGroups[gw]; }
                    msb.Parts.Objects.Add(obj);
                    if (!ladder) doors.Add(inst);
                    if (!ladder && part.EntityID > 0) PlacedObjects[(nId, part.EntityID)] = inst;
                    pack.Note($"{nId}: object {obj.Name} (DS1 {part.Name}, entity {part.EntityID}) instance {inst} at {part.Position}");
                }
            }

        // ---- objects placed one by one (session 7): prefab/event objects, helpers, DS2 white doors
        var placed = (o.Placements?.Invoke(msb1) ?? []).Concat(o.DemoActors).Concat(scenery).Concat(fallbackPlacements).ToList();
        foreach (var pl in placed)
        {
            if (failedPorts.Contains(pl.Model)) continue; // session 11: its model failed to port (ST1 row above)
            if (!msb.Models.Objects.Any(m => m.Name == pl.Model)) msb.Models.Objects.Add(new MSB2.Model.Object { Name = pl.Model });
            var obj = (MSB2.Part.Object)(tDoor ?? tMsb.Parts.Objects[0]).DeepCopy();
            obj.Name = $"{pl.Model}_{msb.Parts.Objects.Count(x => x.ModelName == pl.Model):D4}";
            obj.ModelName = pl.Model;
            obj.Position = pl.Position;
            obj.Rotation = pl.Rotation;
            if (pl.Scale is { } scale) obj.Scale = scale;   // session 8 (track nests): DS1's part scale (DS2 scales objects too: o00_9013, o10_9290)
            obj.MapObjectInstanceParamID = pl.Instance;
            // instance-0 objects (breakables) belong to the map's bonfire like the DS1 parts above; the rest: none
            obj.UnkT04 = pl.Instance == 0 ? (short)o.BonfireId : (short)0;
            Array.Fill(obj.DrawGroups, 0xFFFFFFFF);
            Array.Fill(obj.DispGroups, 0xFFFFFFFF);
            if (pl.Groups is { } groups) for (int gw = 0; gw < 4; gw++) { obj.DrawGroups[gw] = groups[gw]; obj.DispGroups[gw] = groups[gw]; }
            msb.Parts.Objects.Add(obj);
            pack.Note($"{nId}: placed {obj.Name} instance {pl.Instance} ({pl.Kind}{(pl.CtrlId != 0 ? $" {pl.CtrlId}" : "")}) at {pl.Position} {pl.Note}");
        }

        // ---- map-level events and regions from the template (lighting, fog, env probes moved to the bonfire)
        // Things Betwixt is a night map: its light, fog and background colours are blue ((179, 202, 255) light tint, (210,
        // 222, 255) fog, (128, 190, 255) sky). DS1's Asylum is an overcast grey, so the copies get neutral greys.
        static System.Drawing.Color Grey(System.Drawing.Color c, int v) => System.Drawing.Color.FromArgb(c.A, v, v, v);
        // Session 8: DS1's own (Atmosphere.MsbEvents: DS1 main zone's light directions/colours, fog colour/distance,
        // background = fog colour); --tb-atmos keeps the session-5 greyed Things Betwixt events.
        if (atmos != null) Atmosphere.MsbEvents(msb, tMsb, atmos, drawParam, cal!);
        else
        {
            foreach (var e in tMsb.Events.Lights)
            {
                var l = (MSB2.Event.Light)e.DeepCopy();
                l.ColorT3C = Grey(l.ColorT3C, 200);
                msb.Events.Lights.Add(l);
            }
            foreach (var e in tMsb.Events.Fogs)
            {
                var f = (MSB2.Event.Fog)e.DeepCopy();
                if (f.ColorT04.R != 0 || f.ColorT04.G != 0 || f.ColorT04.B != 0) f.ColorT04 = Grey(f.ColorT04, 190);
                msb.Events.Fogs.Add(f);
            }
            foreach (var e in tMsb.Events.BGColors)
            {
                var b = (MSB2.Event.BGColor)e.DeepCopy();
                b.Color = Grey(b.Color, 150);
                msb.Events.BGColors.Add(b);
            }
        }
        foreach (var e in tMsb.Events.MapOffsets) { var m = (MSB2.Event.MapOffset)e.DeepCopy(); m.Translation = Vector3.Zero; msb.Events.MapOffsets.Add(m); }
        // env probes: one EnvLight region per CubeEnv of the envbnd; with the DS1 atmosphere the envbnd is the best-fit
        // DS2 area's (rule A3: DS1's GI_EnvM cubemaps are BC6H HDR, DS2's CubeEnv DXT5: not converted yet), so its regions
        var envMsb = atmos != null && atmosTemplate.Map != tId ? MSB2.Read(Path.Combine(data, "map", atmosTemplate.Map, atmosTemplate.Map + ".msb")) : tMsb;
        if (ec1 != null && envMsb.Regions.EnvLights.Count > 0)
            foreach (var (cubeId, at, note) in ec1.Regions) // rule EC1: one region per DS1 cube cluster, at its spots
            {
                var x = (MSB2.Region.EnvLight)envMsb.Regions.EnvLights[0].DeepCopy();
                x.UnkT00 = cubeId; x.Position = at; x.Name = $"環境ライトID{cubeId}_{note}";
                msb.Regions.EnvLights.Add(x);
            }
        else
        foreach (var r in envMsb.Regions.EnvLights) { var x = (MSB2.Region.EnvLight)r.DeepCopy(); x.Position = o.BonfirePos + new Vector3(0, 1.5f, 0); msb.Regions.EnvLights.Add(x); }
        var start = (MSB2.Region.StartPoint)tMsb.Regions.StartPoints[0].DeepCopy();
        var player = msb1.Parts.Players.FirstOrDefault();
        start.Position = player?.Position ?? o.BonfirePos;
        start.Rotation = player?.Rotation ?? Vector3.Zero;
        msb.Regions.StartPoints.Add(start);
        // DS1 map effects and (opt-in) sound regions, after every other region: their Unk08 index continues the map's
        // numbering (a region index the start point already used was duplicated before; vanilla maps have none)
        if (atmos != null)
        {
            Atmosphere.SfxRegions(msb, tMsb, msb1, atmosLog, dsrRoot, o.Ds1Map); // skylight fix pass: area emitters tiled (rule B1b)
            if (o.Ds1Sound) Atmosphere.SoundRegions(msb, tMsb, msb1, dsrRoot, data, o.Ds1Map, atmosLog);
        }
        // region indices (Unk08) unique, as in every vanilla MSB2: the env regions may come from the best-fit area,
        // the start point from the template map
        {
            var regions = msb.Regions.GetEntries().Cast<MSB2.Region>().ToList();
            short next = (short)(regions.Select(r => (int)r.Unk08).DefaultIfEmpty(-1).Max() + 1);
            var seenIdx = new HashSet<short>();
            foreach (var r in regions)
                if (!seenIdx.Add(r.Unk08)) { pack.Note($"{nId}: region {r.Name} index {r.Unk08} taken, now {next}"); r.Unk08 = next++; seenIdx.Add(r.Unk08); }
        }

        // ---- write
        void Split(BXF4 b, string bhd, string bdt) { b.Write(out byte[] h, out byte[] t); pack.AddFile("gamedata", $"model/map/{bhd}", h); pack.AddFile("gamedata", $"model/map/{bdt}", t); }
        Split(map, nId + ".mapbhd", nId + ".mapbdt");
        Split(tex, $"t{nTail}.tpfbhd", $"t{nTail}.tpfbdt");
        byte[] lowBytes = texLow.Write();
        pack.AddFile("gamedata", $"model/map/t{nTail}_low.tpfbdt", lowBytes);
        pack.AddFile("gamedata", $"model/map/t{nTail}_low.tpfbhd", lowBytes);
        Split(gi, $"g{nTail}.gibhd", $"g{nTail}.gibdt");
        Split(hi, $"h{nTail}.hkxbhd", $"h{nTail}.hkxbdt");
        Split(lo, $"l{nTail}.hkxbhd", $"l{nTail}.hkxbdt");
        pack.AddFile("gamedata", $"model/map/e{nTail}.envbnd.dcx", ec1?.Envbnd ?? File.ReadAllBytes(Path.Combine(mdir, $"e{atmosTemplate.Map[1..]}.envbnd.dcx")));
        pack.AddFile("gamedata", $"map/{nId}/{nId}.msb", msb.Write());
        var tNgp = NGP.Read(Path.Combine(data, "map", tId, tId + ".ngp"));
        var ngp = NavmeshConverter.Convert(dsrRoot, o.Ds1Map, msb1, tNgp, Vector3.Zero, out string navReport);
        if (o.NoNavmesh) { ngp.Meshes.Clear(); navReport = "navmesh: EMPTY (--no-navmesh control test)"; }
        Console.WriteLine($"  {nId} {navReport}");
        pack.AddFile("gamedata", $"map/{nId}/{nId}.ngp", ngp.Write());

        // ---- per-map params: the template's, emptied; the bonfire's instance row
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        foreach (string path in Directory.GetFiles(Path.Combine(data, "param"), $"*_{tId}.param"))
        {
            var param = PARAM.Read(path);
            bool instances = Path.GetFileName(path).StartsWith("mapobjectinstanceparam", StringComparison.OrdinalIgnoreCase);
            // Only the instance param keeps a row; the others are emptied before a paramdef is applied (the community
            // GeneratorParam def doesn't fit every vanilla row, and GeneratorLocation has none).
            var keepRaw = instances ? param.Rows.FirstOrDefault(r => r.ID == tBonfire.MapObjectInstanceParamID) : null;
            // HitGroupParam (session 8): the template's row 0 (brightness, ground materials) for each hit group of ours
            bool hitGroups = Path.GetFileName(path).StartsWith("hitgroupparam", StringComparison.OrdinalIgnoreCase) && toggled.Count > 0;
            if (hitGroups && param.ApplyParamdefCarefully(defs) && param.Rows.FirstOrDefault(r => r.ID == 0) is { } hitTemplate)
            {
                param.Rows.Clear();
                foreach (int g in toggled.Values.Distinct().Order()) param.Rows.Add(new PARAM.Row(hitTemplate) { ID = g });
                pack.AddFile("gamedata", $"param/{Path.GetFileName(path).Replace(tId, nId)}", param.Write());
                continue;
            }
            if (!instances) param.Rows.RemoveAll(_ => true);
            if (!param.ApplyParamdefCarefully(defs))
            {
                var blob = new PARAMDEF { ParamType = param.ParamType, DataVersion = param.ParamdefDataVersion, Unicode = true, FormatVersion = 104, BigEndian = false };
                blob.Fields.Add(new PARAMDEF.Field(blob, PARAMDEF.DefType.dummy8, "data") { ArrayLength = (int)param.DetectedSize });
                param.ApplyParamdef(blob);
            }
            var keep = instances ? param.Rows.FirstOrDefault(r => r.ID == tBonfire.MapObjectInstanceParamID) : null;
            var keepPickup = instances && tPickup != null ? param.Rows.FirstOrDefault(r => r.ID == tPickup.MapObjectInstanceParamID) : null;
            var keepDoor = instances && tDoor != null ? param.Rows.FirstOrDefault(r => r.ID == tDoor.MapObjectInstanceParamID) : null;
            // session 10, rule G4: a template-map ladder row (objCtrlType 4) gives the G4 rows their shape only (fix pass e2: values from the model's default row)
            var keepLadder = instances ? param.Rows.FirstOrDefault(r => Convert.ToInt32(r["objCtrlType"].Value) == 4) : null;
            var keepKind = new Dictionary<PlacementKind, PARAM.Row?>();
            if (instances)
            {
                keepKind[PlacementKind.Door] = keepDoor;
                keepKind[PlacementKind.Generic] = param.Rows.FirstOrDefault(r => Convert.ToInt32(r["objCtrlType"].Value) == 0 && Convert.ToInt32(r["objType"].Value) == 1607 && Convert.ToInt32(r["damageId0"].Value) == 0 && Convert.ToInt32(r["itemLotId"].Value) == 0);
                keepKind[PlacementKind.WhiteDoor] = param.Rows.FirstOrDefault(r => Convert.ToInt32(r["objCtrlType"].Value) == 6);
            }
            param.Rows.Clear();
            foreach (var pl in instances ? placed : [])
            {
                if (pl.Instance == 0) continue; // instance 0: the model's MapObjectDefaultInstanceParam row (session 8 test jars)
                var t = keepKind[pl.Kind] ?? throw new InvalidDataException($"{tId} has no instance row of kind {pl.Kind}");
                var r = new PARAM.Row(t) { ID = pl.Instance };
                if (pl.Kind != PlacementKind.Generic) r["objCtrlId"].Value = Convert.ChangeType(pl.CtrlId, r["objCtrlId"].Value.GetType());
                // session 10, rule WE2: DS1 objects never persist on their own (DS1 re-applies every object state from
                // flags in its load branches): every Generic row resetType 0 "On Map Reload" (TB's template row 10021200
                // says 2 "On New Journey": a prefab could keep a state DS1's script doesn't expect, e.g. the re-armed
                // boulder in state 20; enum names Smithbox's, P)
                else r["resetType"].Value = Convert.ChangeType(0, r["resetType"].Value.GetType());
                foreach (var (field, value) in pl.Fields) r[field].Value = Convert.ChangeType(value, r[field].Value.GetType());
                param.Rows.Add(r);
            }
            if (keepDoor != null)
                foreach (int inst in doors) param.Rows.Add(new PARAM.Row(keepDoor) { ID = inst });
            if (instances && ladders.Count > 0)
            {
                // fix pass e2 (finding 6): the row is the ported model's own MapObjectDefaultInstanceParam row (the one every
                // instance-0 ladder uses, in-game V: s6-13/14), field by field, so a G4 ladder differs from the verified
                // route only by its id; TB's per-map row carried TB's iblTextureId 2 (a per-map cube-map index) and resetType 2
                var shape = keepLadder ?? keep ?? throw new InvalidDataException($"{tId} has no instance row to shape rule G4's ladder rows");
                var defaults = pack.Param("MapObjectDefaultInstanceParam");
                foreach (var (inst, model) in ladders)
                {
                    var def = defaults[ObjPorter.ModelId(model)] ?? throw new InvalidDataException($"rule G4: {model} has no MapObjectDefaultInstanceParam row");
                    var r = new PARAM.Row(shape) { ID = inst };
                    foreach (var cell in r.Cells)
                        cell.Value = def[cell.Def.InternalName].Value is byte[] b ? b.Clone() : Convert.ChangeType(def[cell.Def.InternalName].Value, cell.Value.GetType());
                    param.Rows.Add(r);
                }
                pack.Note($"{nId}: rule G4: ladders registered later than load get own instance rows = their model's MapObjectDefaultInstanceParam row (objCtrlType 4): {string.Join(", ", ladders.Select(l => $"{l.Inst} ({l.Model} {ObjPorter.ModelId(l.Model)})"))}");
            }
            if (keepPickup != null)
                foreach (int inst in pickups)
                {
                    // the instance row names its ItemLotParam2_Other row in itemLotId (TB's said 10026000: Rusted Coin)
                    var r = new PARAM.Row(keepPickup) { ID = inst };
                    r["itemLotId"].Value = Convert.ChangeType(inst, r["itemLotId"].Value.GetType());
                    param.Rows.Add(r);
                }
            if (keep != null)
                foreach (var bp in bonfirePlan) // rule BF1: one instance row per bonfire, objCtrlId = its bonfire id
                {
                    var row = new PARAM.Row(keep) { ID = bp.Instance };
                    row["objCtrlId"].Value = (uint)bp.Id;
                    param.Rows.Add(row);
                }
            param.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
            pack.AddFile("gamedata", $"param/{Path.GetFileName(path).Replace(tId, nId)}", param.Write());
        }

        // ---- scripts and the rest
        pack.AddFile("gamedata", $"ezstate/event_{nId}.esd", MinimalEsd(Path.Combine(data, "ezstate", $"event_{tId}.esd"), $"event_{nId}"));
        pack.AddFile("gamedata", $"ezstate/talk_{nId}.esd", MinimalEsd(Path.Combine(data, "ezstate", $"talk_{tId}.esd"), $"talk_{nId}"));
        // session 8: DS1's draw params per zone over the best-fit DS2 area's fltparam (Atmosphere.Filter); the probe/GI
        // read-out fields stay the template map's filter 0 (TB: what LightProbes and the lightmap gain were tuned with)
        // session 8 (skylight, rule A7b): one DS2 palette per DS1 ToneCorrect row the zones use (filterresource.fltbnd)
        var palettes = atmos != null && o.Ds1Palette ? Palette.Add(pack, data, drawParam, atmos.Zones.Select(z => (int)z.ToneCorrect), atmosLog) : null;
        pack.AddFile("gamedata", $"filter/filter_{nId}.fltparam", atmos != null
            ? Atmosphere.Filter(data, atmosTemplate.Map, atmosTemplate.Filter, atmos, drawParam, cal!, atmosLog, probeMap: tId, probeFilter: 0, palettes: palettes)
            : File.ReadAllBytes(Path.Combine(data, "filter", $"filter_{tId}.fltparam")));
        pack.AddFile("gamedata", $"breakobj/{nId}.breakobj", File.ReadAllBytes(Path.Combine(data, "breakobj", $"{tId}.breakobj")));
        foreach (string kind in new[] { "talk", "bloodmes" })
        {
            var fmg = FMG.Read(Path.Combine(data, "menu", "text", "english", kind, $"{tId}.fmg"));
            fmg.Entries.Clear();
            // session 10 (asy-items), rules M1-M4: DS1's developer messages (EventLocation rows + this map's bloodmes texts)
            if (kind == "bloodmes" && o.BloodMessages)
                foreach (string line in Maps.BloodMessages.Build(pack, o, ds1Src ??= new Ds1Source(dsrRoot), defs, fmg)) pack.Note(line);
            pack.AddFile("gamedata", $"menu/text/english/{kind}/{nId}.fmg", fmg.Write());
        }
        string tSound = Path.Combine(data, "sound", $"frpg2_m{tId.Substring(1, 2)}{tId.Substring(4, 2)}.bnd");
        if (o.Ds1Sound) Atmosphere.SoundBanks(pack, dsrRoot, data, o.Ds1Map, nId, atmosLog); // session 8, opt-in: DS1's FMOD projects + banks
        else if (File.Exists(tSound)) pack.AddFile("gamedata", $"sound/frpg2_m{nId.Substring(1, 2)}{nId.Substring(4, 2)}.bnd", File.ReadAllBytes(tSound));
        foreach (string line in atmosLog) pack.Note($"{nId}: {line}");
        foreach (string line in skyLog.Concat(conv.SkyContext?.Log ?? [])) pack.Note($"{nId}: {line}");

        Register(pack, o, data, tId);

        foreach (string n in conv.Notes) pack.Note(n);
        if (MapPieceConverter.Missing.Count > 0) pack.Note($"{nId}: DS1 textures not found (stand-ins): {string.Join(", ", MapPieceConverter.Missing)}");
        Console.WriteLine($"map {nId}: {pieceModels.Count} piece models{(skyModels.Count > 0 ? $" (+{skyModels.Count} split-off sky)" : "")}, {pi} parts, {objModels.Count} decoration object models ({oi} parts), {conv.Textures.Count} textures, {colModels.Count} collision models ({ci} parts, {tris:N0} triangles), bonfire {o.BonfireId} at {o.BonfirePos}");
    }

    /// <summary>
    /// Makes the new map a real map the way DS2's DLC maps (m50_35..38) are: listed in map/WorldMapList/WorldMapList.list
    /// (the registry the save's bonfire table is filtered by: a bonfire on an unlisted map is dropped from every save on
    /// load), with its own AreaParam / NetworkAreaParam / MapObjectTorchParam rows, its own bonfire row and id (never a
    /// DS2 one), the area and bonfire names the Travel menu shows, and list/area icons (the template map's pictures
    /// until in-engine renders exist).
    /// </summary>
    /// <summary>Rule WL1: the most WorldMapList lines DS2 loads (V-code FUN_1403dc0a0, research\map-cap.md): 42 for the
    /// vanilla engine; build-pack --map-cap N (rule CAP1, session 11 cap11) raises it to the loader's map_cap.</summary>
    public static int WorldMapListCap { get; set; } = VanillaMapCap;

    /// <summary>The vanilla limit (FUN_1403dc0a0 cmp 0x2a).</summary>
    public const int VanillaMapCap = 42;

    /// <summary>Rule CAP1: the most WorldMapList lines the loader's map cap supports (src\loader\mapcap.c MAX_ALLOWED: the
    /// two 48-byte per-map stack arrays of FUN_14041bab0).</summary>
    public const int LoaderMapCapMax = 48;

    static void Register(Pack pack, Options o, string data, string tId)
    {
        int mapId = o.MapIdDecimal;
        int tMapId = int.Parse(tId[1..3] + tId[4..6] + tId[7..9] + tId[10..12]);

        // ---- the map registry (Shift-JIS text, CRLF; one "mAA_BB_CC_DD<TAB>#comment" line per map)
        // the pack's list when an earlier map was registered already (s5-36: Firelink's registration started from the
        // vanilla list, dropped the Asylum's line, and warps into the Asylum fell back to Things Betwixt's start)
        byte[] list;
        try { list = pack.GetFile("gamedata", "map/worldmaplist/worldmaplist.list"); }
        catch (KeyNotFoundException) { list = File.ReadAllBytes(Path.Combine(data, "map", "worldmaplist", "worldmaplist.list")); }
        string line = $"{o.Ds2Map}\t#{o.Ds2Map[..3]}B{int.Parse(o.Ds2Map[4..6])} Lordran: {o.AreaName}\r\n";
        if (!System.Text.Encoding.ASCII.GetString(list).Contains(o.Ds2Map))
        {
            bool endsWithNewline = list.Length > 0 && list[^1] == (byte)'\n';
            list = [.. list, .. System.Text.Encoding.ASCII.GetBytes((endsWithNewline ? "" : "\r\n") + line)];
        }
        // session 11 (burg11), rule WL1: DS2 creates no world slot at all for more than 42 lines (FUN_1403dc0a0 cmp 0x2a;
        // research\map-cap.md: raising it is not a 1-byte patch), so the build fails instead
        int listed = System.Text.Encoding.ASCII.GetString(list).Split('\n').Count(l => l.TrimStart().StartsWith('m'));
        if (listed > WorldMapListCap) throw new InvalidDataException($"rule WL1: WorldMapList would have {listed} maps; DS2 loads none above {WorldMapListCap}" +
            (WorldMapListCap == VanillaMapCap ? $" (build with --map-cap up to {LoaderMapCapMax} for a loader with map_cap, rule CAP1)" : " (--map-cap; the loader's map_cap must be at least this)"));
        pack.AddFile("gamedata", "map/worldmaplist/worldmaplist.list", list);

        // ---- per-map rows keyed by map id, copied from the template map
        foreach (string param in new[] { "AreaParam", "NetworkAreaParam", "MapObjectTorchParam" })
        {
            var template = pack.Param(param)[tMapId] ?? throw new InvalidDataException($"{param} has no row {tMapId}");
            var added = pack.AddRow(param, template, mapId, $"{o.AreaName} (from {tId})");
            if (!o.AllBonfires) continue;
            // session 11 (burg11), rules NET1 / AP1 (MapObjects maps; plan decision D-net "on"): the online profile of a DLC
            // map (Brume Tower 50360000: bloodstains, messages, phantoms, multiplay, ring / invasion search; invasions on, the
            // torch lock-on not preferred) instead of Things Betwixt's offline one; TB's camera, light and chameleon ids stay
            var dlc = pack.Param(param)[50360000];
            if (dlc == null) continue;
            string[] fields = param switch
            {
                "NetworkAreaParam" => ["enableBloodstain", "enableBloodMessage", "enableGhost", "enableMultiPlay", "enableRingSearch", "enableBreakInSearch"],
                "AreaParam" => ["intrudeEnable", "isPriorityLockOnIdTorch"],
                _ => [],
            };
            foreach (string f in fields) added[f].Value = dlc[f].Value;
            if (fields.Length > 0) pack.Note($"{o.Ds2Map}: rule {(param == "AreaParam" ? "AP1" : "NET1")}: {param} {mapId} {string.Join(", ", fields.Select(f => $"{f}={added[f].Value}"))} (from DLC row 50360000)");
        }

        // ---- the bonfire: a new row, modelled on the template map's first bonfire
        var bonfires = pack.Param("MapObjectBonfireParam");
        if (bonfires[o.BonfireId] != null) throw new InvalidDataException($"bonfire id {o.BonfireId} already exists");
        var tBonfire = bonfires.Rows.First(r => Convert.ToInt32(r["mapId"].Value) == tMapId);
        var row = pack.AddRow("MapObjectBonfireParam", tBonfire, o.BonfireId, o.BonfireName);
        row["mapId"].Value = mapId;
        int firstArea = o.AllBonfires ? Bonfires(Program.DefaultDsrRoot, o)[0].MenuAreaId : mapId; // rule BF1 sub-area
        row["menuAreaID"].Value = (uint)firstArea;
        row["eventCameraIdBegin"].Value = firstArea;     // DS2 sets it to the (sub-)area id for every bonfire
        row["menuSortID"].Value = (byte)230;             // after DS2 (1..) and its DLC (200..222); 250+ are debug rows
        // session 11 (burg11), rule NET1: a MapObjects map's bonfires join a matchmaking area like vanilla's online maps
        // (FoFG 101000, Brume 503600 = map id / 100) and allow the network-disabling item; TB's rows have 0 / 0
        if (o.AllBonfires) { row["multiPlayAreaid"].Value = mapId / 100; row["isUseDisableNetworkItem"].Value = (byte)1; }
        pack.SetText("mapname", mapId, o.AreaName);
        pack.SetText("bonfirename", o.BonfireId, o.BonfireName);

        // ---- Travel menu pictures: 256x128 DXT5 TPFs whose texture is named like the file (upper case)
        string icons = Path.Combine(data, "menu", "tex", "icon");
        // A picture of the map (data\lordran	ravel\MAP.png, e.g. an in-game screenshot crop) goes into the template
        // icon's frame: the template's torn-edge alpha is kept, its picture replaced. Without one, the template's picture.
        string picture = Path.Combine(Path.GetDirectoryName(Pack.DefaultOut)!, "travel", o.Ds2Map + ".png");
        // session 11 (burg11): a re-hosted map (rule H1) keeps the picture saved under a former host id (MapHosts)
        foreach (string former in MapHosts.Rows.FirstOrDefault(r => r.Host == o.Ds2Map)?.FormerHosts ?? [])
            if (!File.Exists(picture)) picture = Path.Combine(Path.GetDirectoryName(Pack.DefaultOut)!, "travel", former + ".png");
        var shot = File.Exists(picture) ? Textures.Png.Load(picture) : null;
        // session 11 (travel12), rule TP1: every other DS1 picture is rendered offline from DS1's own map data (TravelPictures);
        // before, every area / bonfire without a screenshot showed the template's (Things Betwixt's) picture
        var plans = Bonfires(Program.DefaultDsrRoot, o);
        Textures.Image? Rendered(BonfirePlan bp)
        {
            if (Environment.GetEnvironmentVariable("LR_NO_TRAVEL_RENDER") == "1") return null; // fast test builds
            var (img, line) = TravelPictures.ForBonfire(Program.DefaultDsrRoot, o.Ds1Map, bp);
            pack.Note($"{o.Ds2Map}: {line}");
            return img;
        }
        void Icon(string folder, string from, string to, Textures.Image? shot)
        {
            var tpf = TPF.Read(Path.Combine(icons, folder, from + ".tpf"));
            tpf.Textures[0].Name = to.ToUpperInvariant();
            if (shot == null) pack.Note($"{o.Ds2Map}: rule TP1: {to} keeps the template's picture ({from}): no screenshot, no render");
            if (shot != null)
            {
                var frame = Textures.Dds.Read(tpf.Textures[0].Bytes).DecodeTop();
                int x0 = frame.Width, y0 = frame.Height, x1 = 0, y1 = 0;
                for (int y = 0; y < frame.Height; y++) for (int x = 0; x < frame.Width; x++)
                    if (frame[x, y].A > 128) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
                var fit = shot.ResizeArea(x1 - x0 + 1, y1 - y0 + 1);
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                {
                    var c = fit[x - x0, y - y0];
                    frame[x, y] = new Textures.Rgba(c.R, c.G, c.B, frame[x, y].A);
                }
                tpf.Textures[0].Bytes = Textures.Dds.Encode(frame, Textures.BcFormat.BC3, mipmaps: false);
            }
            pack.AddFile("gamedata", $"menu/tex/icon/{folder}/{to}.tpf", tpf.Write());
        }
        // the area: the screenshot, else the area's first bonfire's render; bonfire 0: the screenshot, else its render
        var areaFirst = plans.FirstOrDefault(b => b.MenuAreaId == mapId) ?? plans[0];
        Icon("bonfire_area", $"ic_area_{tMapId:D9}", $"ic_area_{mapId:D9}", shot ?? Rendered(areaFirst));
        Icon("bonfire_list", $"ic_list_{Convert.ToInt32(tBonfire.ID):D9}", $"ic_list_{o.BonfireId:D9}", shot ?? Rendered(plans[0]));
        // session 11 (burg11), rule BF1: the map's further bonfires (vanilla's several-bonfire maps: FoFG 10655/10660/10670):
        // own rows like the first, a unique menuSortID (230 + index), Travel sub-area menuAreaID = eventCameraIdBegin = map
        // id + k (vanilla Belfry Luna 10160002, Black Gulch 10250001) with its mapname text and area icon
        foreach (var bp in plans.Where(b => b.Index > 0))
        {
            if (bonfires[bp.Id] != null) throw new InvalidDataException($"bonfire id {bp.Id} already exists");
            var r = pack.AddRow("MapObjectBonfireParam", tBonfire, bp.Id, bp.Name);
            r["mapId"].Value = mapId;
            r["menuAreaID"].Value = (uint)bp.MenuAreaId;
            r["eventCameraIdBegin"].Value = bp.MenuAreaId;
            r["menuSortID"].Value = (byte)(230 + bp.Index);
            if (o.AllBonfires) { r["multiPlayAreaid"].Value = mapId / 100; r["isUseDisableNetworkItem"].Value = (byte)1; } // rule NET1 (BF1 maps only; BF1-A keeps the template's like bonfire 0)
            pack.SetText("bonfirename", bp.Id, bp.Name);
            Icon("bonfire_list", $"ic_list_{Convert.ToInt32(tBonfire.ID):D9}", $"ic_list_{bp.Id:D9}", Rendered(bp));
            pack.Note($"registered bonfire {bp.Id} \"{bp.Name}\" in {o.Ds2Map} (Travel area {bp.MenuAreaId} \"{bp.MenuAreaName}\", sort {230 + bp.Index}; rule BF1)");
        }
        foreach (var sub in plans.Where(b => b.MenuAreaId != mapId).DistinctBy(b => b.MenuAreaId))
        {
            pack.SetText("mapname", sub.MenuAreaId, sub.MenuAreaName);
            Icon("bonfire_area", $"ic_area_{tMapId:D9}", $"ic_area_{sub.MenuAreaId:D9}", Rendered(sub)); // its first bonfire (DistinctBy keeps the first)
        }
        if (o.BannerAtlas != null) AreaBanner.Build(pack, Program.DefaultDsrRoot, o.Ds2Map, o.BannerAtlas, o.BannerLine, o.BannerColumn);
        pack.Note($"registered {o.Ds2Map} like a DLC map: WorldMapList line, Area/NetworkArea/MapObjectTorch rows, bonfire {o.BonfireId} \"{o.BonfireName}\" in \"{o.AreaName}\"");
    }

    /// <summary>The map's playable extent: DS1 object, character, player and region positions, padded by 10 m (piece
    /// bounding boxes include distant scenery: a probe grid over them ran out of memory).</summary>
    static (Vector3 Min, Vector3 Max) PieceBounds(MSB1 msb1, Dictionary<string, string> pieceModels, BXF4 map)
    {
        var pts = msb1.Parts.Objects.Select(p => p.Position).Concat(msb1.Parts.Enemies.Select(p => p.Position))
            .Concat(msb1.Parts.Players.Select(p => p.Position)).Concat(msb1.Regions.GetEntries().Select(r => r.Position)).ToList();
        var lo = pts.Aggregate(new Vector3(float.MaxValue), Vector3.Min) - new Vector3(10);
        var hi = pts.Aggregate(new Vector3(float.MinValue), Vector3.Max) + new Vector3(10);
        return (lo, hi);
    }

    static ACB.Asset.Model CloneModel(ACB.Asset.Model t, string abs, string rel, FLVER2 flver) => new()
    {
        AbsolutePath = abs, RelativePath = rel, Unk0A = t.Unk0A,
        Members = new ACB.Asset.Model.MemberList(flver.Materials.Select(m => new ACB.Asset.Model.Member { Text = m.Name, Unk04 = 0 })) { Unk00 = t.Members?.Unk00 ?? -1 },
        DrawDistance = t.DrawDistance, MeshLodRate = t.MeshLodRate, Reflectible = t.Reflectible, NormalInteraction = t.NormalInteraction,
        Unk20 = t.Unk20, RenderType = t.RenderType, DisableShadowSource = t.DisableShadowSource, DisableShadowTarget = t.DisableShadowTarget,
        Unk27 = t.Unk27, Unk28 = t.Unk28, Unk2C = t.Unk2C, FixToCamera = t.FixToCamera, Unk2E = t.Unk2E,
        LowTextureDistance = t.LowTextureDistance, CheapRenderDistance = t.CheapRenderDistance, Unk34 = t.Unk34, Unk35 = t.Unk35, Unk36 = t.Unk36, Unk37 = t.Unk37,
    };

    /// <summary>An event/talk ESD with one state machine and one idle state (placeholder until DS1 events are ported).</summary>
    /// <summary>Rule DG1: a part's DrawGroups and DispGroups (all 4 words) = the given DS1 groups; a collision also takes them
    /// as its object-area words Unk44..50 (the mask DS2 activates objects by: FUN_1403cdc90; play-test fix: DS1's static
    /// objects then load where DS1 shows them, event objects keep all bits), unless DS1's are all 0.</summary>
    static void Dg1Groups(MSB2.Part p, uint[] groups)
    {
        for (int i = 0; i < 4; i++) { p.DrawGroups[i] = groups[i]; p.DispGroups[i] = groups[i]; }
        if (p is MSB2.Part.Collision && groups.Any(w => w != 0))
        { p.Unk44 = unchecked((int)groups[0]); p.Unk48 = unchecked((int)groups[1]); p.Unk4C = unchecked((int)groups[2]); p.Unk50 = unchecked((int)groups[3]); }
    }

    static byte[] MinimalEsd(string templatePath, string name)
    {
        var t = ESD.Read(templatePath);
        var esd = new ESD(t.LongFormat, t.DarkSoulsCount) { Name = name, Unk70 = t.Unk70, Unk74 = t.Unk74, Unk78 = t.Unk78, Unk7C = t.Unk7C };
        // Two states, never one: SoulsFormats writes no dummy state after a one-state group, no vanilla DS2 ESD has one,
        // and DS2 runs no events at all from an ESD containing one (session 5: the Asylum's events never started).
        var idle = new ESD.State();
        idle.Conditions.Add(new ESD.Condition(1, Convert.FromHexString("41A1")));
        esd.StateGroups[0x7FFFFFFF] = new Dictionary<long, ESD.State> { [0] = idle, [1] = new ESD.State() };
        return esd.Write();
    }
}
