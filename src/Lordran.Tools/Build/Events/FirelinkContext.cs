using System.Numerics;
using Lordran.Tools.Build.Chr;
using Lordran.Tools.Build.Maps;
using SoulsFormats;

namespace Lordran.Tools.Build.Events;

/// <summary>
/// Firelink Shrine's map knowledge for <see cref="EmevdTranslator"/> (session 9, track firelink): DS1 m10_02_00_00 →
/// DS2 m19_02_00_00 (rulebook §1: DS1's m10_BB ids are Things Betwixt's in DS2, so Firelink is area 19). The same rules as
/// <see cref="AsylumContext"/>, with the DS2 map's own ids:
///  - flags: <see cref="FlagRules"/> (session 9, track npcflags: persistent DS1 flags in DS2's global blocks, temporary
///    ones in m19_02's map-slot block; the old prefix 142 had no DS2 storage). Firelink's events read and write the flags of
///    every map this build ports (<see cref="FlagMaps"/>: the Asylum's 11810000 "left the Asylum", the nest's 11020000 …);
///  - objects: what the map builder placed in m19_02 (<see cref="MapBuilder.PlacedObjects"/>); a DS1 object part the map
///    doesn't place, or a DS1 entity no part, region or event of the MSB carries (event 11020105's 1021961: DS1's command
///    is a no-op itself), is absent (commands no-ops, conditions never pass);
///  - regions: EventLocation 3701000 + entity % 1000 (the Asylum's region rule; Firelink's EventLocation param is its own);
///  - action-button areas (the nest 1022120): key-guide helpers by <see cref="FirelinkScript.AreaButtons"/> (the Asylum's
///    area rule with Firelink's AABB 1902: instances 19027120 "Curl up like a ball", 19028120 "Stand up");
///  - characters: the generators the build placed in m19_02 (EntityRules.Generator ids, read from the pack's
///    GeneratorParam: the enemies now, the npc track's NPCs once merged); report mode: AsylumEnemies' placement rule for
///    Firelink's ported enemies;
///  - other maps: DS1 m18_01 points → the Asylum (DS2 m18_01_00_00), EventLocation 3701000 + entity % 1000, written as
///    points by MapLinks (<see cref="FirelinkScript.AsylumWarpTargets"/>: the nest's landing 1812110 → 3701110);
///  - cutscenes: <see cref="Cutscenes.Cutscenes.Demo"/> (C2 ids: 100230 → 190230, 180130 → 180130).
/// With <paramref name="pack"/> null (the emevd-translate report) nothing is written.
/// </summary>
public sealed class FirelinkContext : EmevdTranslator.IContext
{
    public const string Map = "m10_02_00_00";
    /// <summary>Session 11 (burg11): the host by rule H1 (MapHosts; m19_42, was m19_02).</summary>
    public static readonly string Ds2Map = MapHosts.Host(Map);
    /// <summary>The DS1 maps this build ports (their flags translate): Firelink and the Asylum.</summary>
    static readonly string[] FlagMaps = ["m10_02", "m18_01"];

    readonly Pack? pack;
    readonly Ds1Source ds1;
    readonly MSB1 msb1;
    readonly HashSet<int> generators;
    readonly IReadOnlyDictionary<int, int> sounds;
    readonly HashSet<int> entities;
    AsylumContext? shared;

    /// <param name="generators">DS2 generator ids placed in m19_02 (the pack's GeneratorParam rows), null = the report's
    /// default placement rule</param>
    /// <param name="sounds">DS1 sound entity → DS2 sound id of the Sound regions the build placed (EntityRules.SoundId),
    /// null = none</param>
    /// <param name="extraCharacters">report only: DS1 character entities to count as placed as well (emevd-translate --chr)</param>
    public FirelinkContext(Pack? pack, Ds1Source ds1, IEnumerable<int>? generators = null, IReadOnlyDictionary<int, int>? sounds = null,
        IEnumerable<int>? extraCharacters = null)
    {
        this.pack = pack; this.ds1 = ds1;
        msb1 = MSB1.Read(Path.Combine(ds1.Root, "map", "MapStudio", Map + ".msb"));
        this.generators = (generators ?? AsylumEnemies.Placements(msb1, Map, [ChrPorter.Skeleton, ChrPorter.UndeadSoldier, ChrPorter.HollowWarrior], onlyGroup: null)
            .Select(p => p.Generator)).Concat((extraCharacters ?? []).Select(EntityRules.Generator)).ToHashSet();
        this.sounds = sounds ?? new Dictionary<int, int>();
        entities = msb1.Parts.GetEntries().Select(p => p.EntityID).Concat(msb1.Regions.GetEntries().Select(r => r.EntityID))
            .Concat(msb1.Events.GetEntries().Select(e => e.EntityID)).Where(e => e > 0).ToHashSet();
    }

    /// <summary>The generator ids the pack placed in m19_02 (GeneratorParam rows, read raw: no paramdef fits).</summary>
    public static HashSet<int> PlacedGenerators(Pack pack) =>
        pack.TryGetFile("gamedata", $"param/generatorparam_{Ds2Map}.param") is { } b ? PARAM.Read(b).Rows.Select(r => r.ID).ToHashSet() : [];

    /// <summary>The Asylum's context for the map-independent rules it implements (DS1 text → mapevent, DS1 item lots →
    /// ItemLotParam2_Other): one rule, both maps.</summary>
    AsylumContext Shared => shared ??= new AsylumContext(pack, ds1, new Dictionary<int, int>(), firelink: true);

    public string Ds1Map => Map;
    public int? Demo(int ds1Scene) => Cutscenes.Cutscenes.Demo(ds1Scene);
    /// <summary>Rule T1 (fix pass): Firelink's DS1 temporary flags 11025000..11025999 (FlagRules F2: m19_02's map-slot block)
    /// are reset at every load (the nest's "curled up" 11025060 must not survive a quit, a death or a reload).</summary>
    public bool ResetsTemporaryFlags => true;

    public int? Flag(int f)
    {
        foreach (var m in FlagMaps)
            if (FlagRules.Map(m, f) is int v) return v;
        return null;
    }

    // ---------------------------------------------------------------- objects
    public int? Object(int e) => MapBuilder.PlacedObjects.TryGetValue((Ds2Map, e), out int inst) ? inst : Lift(e)?.Instance;
    /// <summary>Session 11 (burg11), rule LF1: a DS1 lift of this map (Lifts; placed by MapBuilder.Firelink's Placements).</summary>
    Lifts.Lift? Lift(int e) => FirelinkScript.Enabled ? Lifts.Of(ds1.Root, Map, Ds2Map, e) : null;

    public bool ObjectAbsent(int e)
    {
        if (Object(e) != null) return false;
        if (!entities.Contains(e)) return true; // no DS1 part carries it: DS1's own command does nothing
        var part = msb1.Parts.Objects.FirstOrDefault(p => p.EntityID == e);
        if (part == null) return false;
        return !msb1.Parts.Objects.Any(p => p.EntityID > 0 && p.EntityID != e && Object(p.EntityID) != null && Vector3.Distance(p.Position, part.Position) < 0.05f);
    }

    public bool ObjectDamages(int e) => false;
    // session 11 (burg11), rule LF1: the lift's prefab states (Lifts.States); no other event-driven objects in Firelink yet
    public int? ObjectAnimState(int e, int anim) => Lift(e) is { } l && Lifts.States(l, anim) is { } s ? s.Play : null;
    public int? ObjectAnimEndState(int e, int anim) => Lift(e) is { } l && Lifts.States(l, anim) is { } s ? s.RestState : null;
    public int? ObjectRestState(int e, int anim) => Lift(e) is { } l && Lifts.States(l, anim) is { } s ? s.RestState : null;
    public bool ObjectAnimIsRest(int e, int anim) => Lift(e) is { } l && Lifts.States(l, anim) is { Play: null };
    /// <summary>Rule I1 (session 11): the lift's 11020301, initialised by 11020300 once its first ride is done.</summary>
    public bool NestedInits => true;
    /// <summary>Rule N1 (session 11): 11020301's network approval wait passes at once (offline / host).</summary>
    public bool NetworkWaitsAreNoOps => true;
    public int? ActionHelper(int e, int message) => FirelinkScript.AreaButtons().FirstOrDefault(b => b.Area == e && b.Message == message) is { } b ? b.Helper : null;
    public int? Sound(int e) => sounds.TryGetValue(e, out int s) ? s : null;
    // session 10 (asy-sound, rules SND1/SND4/SND5): the map's sound plan (EntityRules.SoundId → MapSounds) when the build
    // placed DS1's sound (--ds1-sound default); a plain dictionary (report tools) = regions only
    public int? SoundEffect(int e, int type, int id) => (sounds as MapSounds)?.EffectKey(e, type, id);
    public int? OneShotSfx(int e, int sfx) => (sounds as MapSounds)?.OneShotKey(e, sfx);
    public IEnumerable<int> SoundsStartedIn(long ev) => (sounds as MapSounds)?.StartedIn(ev) ?? [];
    public int? ObjActHelper(int e) => null;
    public int? SfxObject(int e) => null;                    // no light walls in Firelink

    // ---------------------------------------------------------------- regions
    public int? Region(int e) => e is >= 1022000 and < 1023000 && msb1.Regions.GetEntries().Any(r => r.EntityID == e) ? FirelinkScript.RegionId(e) : null;
    public int? Point(int e) => Region(e);
    public int? HitRegion(int e) => null;                    // Firelink's script has no "standing on hit"
    public int? HitGroup(int e) => null;                     // toggled collisions are left out (no --hit-groups for Firelink)
    public (int From, int To)? FogCrossing(int fog, int area) => null;

    // ---------------------------------------------------------------- texts, ObjActs
    AsylumContext? dryText;
    /// <summary>DS1 texts the rule added (mapevent id → DS1 text), written by <see cref="FirelinkScript.Build"/> only when a
    /// translated group shows them (the translator asks for texts of events that end up untranslated too).</summary>
    public Dictionary<int, string> PendingTexts { get; } = [];

    /// <summary>The Asylum's text rule (DS2's own entry for the same English text, else DS1's text as 18000000 + id %
    /// 1000000), without writing: see <see cref="PendingTexts"/>.</summary>
    public int? Message(int m)
    {
        dryText ??= new AsylumContext(null, ds1, new Dictionary<int, int>(), firelink: true);
        var id = dryText.Message(m);
        if (id is int t && t == 18000000 + m % 1000000)
            PendingTexts[t] = ds1.Text("Event_text_", m) is { Length: > 0 } s ? s : ds1.AnyText(m) ?? "";
        return id;
    }
    /// <summary>Session 10 (asy-items): rules M2/M4/C3 as in <see cref="AsylumContext"/> (Firelink has no event-toggled
    /// message and no start-disabled treasure outside its chests today).</summary>
    public bool MessageAbsent(int e) => msb1.Events.Messages.Any(x => x.EntityID == e) && MessageLocation(e) == null;
    public int? MessageLocation(int e) => BloodMessages.Location(ds1.Root, Map, e);
    public IEnumerable<int> TreasuresDisabledAtLoad => MapBuilder.StartDisabledPickups.TryGetValue(Ds2Map, out var l) ? l : [];

    MSB1.Event.ObjAct? ObjAct(int flag) => msb1.Events.ObjActs.FirstOrDefault(a => a.ObjActEntityID == flag);
    public int? ObjActFlag(int f) => null;
    public EmevdTranslator.ObjActLock? Lock(int f) => null;  // no DS1 door of Firelink is ported yet (the lift chamber door o1465)
    /// <summary>An ObjAct on an object the map doesn't place (chests o0110, the lift chamber door o1465, the New Londo lift
    /// lever o6100): its flag never comes.</summary>
    public bool ObjActAbsent(int f) => ObjAct(f) is { } a && msb1.Parts.Objects.FirstOrDefault(p => p.Name == a.ObjActPartName) is { } part && ObjectAbsent(part.EntityID);
    public bool SpEffectGatesObjAct(int sp) => false;

    // ---------------------------------------------------------------- characters
    public int? Generator(int e)
    {
        if (e == EmevdTranslator.Player || e <= 0) return null;
        int g = EntityRules.Generator(e);
        return generators.Contains(g) ? g : null;
    }

    public bool IsBoss(int e) => e > 10000 && ds1.Params.Params["GameAreaParam"][e] != null;
    /// <summary>Firelink has no DS1 boss; a boss would need its BossBattleParam rule (AsylumContext) first.</summary>
    public int? BossBattle(int e, int nameId) => null;
    public int? ItemLot(int lot) => Shared.ItemLot(lot);
    /// <summary>Session 10 (asy-items i2 fix pass), rule R5 (see AsylumContext).</summary>
    public int? RingItem(int sp) => RingPorter.ItemForSpEffect(pack, ds1, sp);

    // forward-compatible with the boss track's IContext (session 8, track boss step 2): Firelink has no boss, no wake
    // clips, no falling-control SpEffect and awards no flagged lots; public methods with the interface's signatures
    // implement those members once the boss track's EmevdTranslator is merged
    public int? LogicFlag(int e) => null;
    public int? StartUpState(int e, int anim) => null;
    public double? WakeClip(int e, int anim) => null;
    public bool FallControl(int sp) => false;
    public List<(int Lot, int? Flag)>? ItemLotRows(int lot) => null;

    // ---------------------------------------------------------------- other maps
    /// <summary>DS1 m18_01 (the Asylum, DS2 m18_01_00_00) points by the region rule (written as points by MapLinks:
    /// <see cref="FirelinkScript.AsylumWarpTargets"/>); other maps (the Firelink Altar m18_00) aren't built.</summary>
    public (int Map, int Point)? MapPoint(int area, int block, int point)
    {
        if (area != 18 || block != 1) return null;
        return FirelinkScript.AsylumWarpTargets().Any(t => t.Entity == point) ? (MapBuilder.Asylum.MapIdDecimal, AsylumScript.RegionId(point)) : null;
    }
}
