using System.Numerics;
using System.Text.RegularExpressions;
using Lordran.Tools.Build.Chr;
using Lordran.Tools.Build.Maps;
using SoulsFormats;

namespace Lordran.Tools.Build.Events;

/// <summary>
/// Session 11 (burg11, BURG-PLAN M2 track mapctx, first cut): the map knowledge of <see cref="EmevdTranslator"/> for any DS1
/// map ported by MapHosts + MapObjects (the `--map burg` rows), with no per-map literal. The generic members of
/// <see cref="AsylumContext"/> (text, item lots, ObjAct requirements, hit regions, ladders, world events, prefab states) are
/// re-implemented over the map's own data; the Asylum and Firelink keep their verified contexts. Ids:
///  - flags: FlagRules (the map's own + F1x persistent flags of other maps);
///  - objects: <see cref="MapObjects"/> instances (K1/K1e/K1p prefabs, WE1, BD1) and MapBuilder.PlacedObjects (pickups,
///    G4 ladders); prefab states by rule C11 (AsylumScript.PrefabStates' template table, data);
///  - regions: rule ID2 EventLocation 3710000 + entity % 10000 (0 collisions on m10_01; the 3701000 + entity % 1000 rule of
///    the Asylum/Firelink collides 6 times there); hit regions 3720000 + entity % 10000 (boxes over the collision);
///  - helpers: MapObjects (ObjAct AABB5nnn, object buttons AABB3nnn, areas AABB7nnn-9nnn);
///  - characters: the generators the pack placed; bosses: none placed yet (M4);
///  - other maps: a DS1 point in a ported map whose host this pack built → (host id, ID2 of the point) when that map's
///    EventLocation has the row; otherwise null.
/// </summary>
public sealed class Ds1MapContext : EmevdTranslator.IContext
{
    readonly Pack? pack;
    readonly Ds1Source ds1;
    readonly MSB1 msb1;
    readonly EMEVD emevd;
    readonly Dictionary<(int, int), (string Name, int[] Types)> defs;
    readonly IReadOnlyDictionary<int, int> generators;
    readonly IReadOnlyDictionary<int, int> sounds;
    readonly MapObjects plan;
    readonly string ds2Map, aab;
    readonly HashSet<int> regionEntities;
    readonly Dictionary<int, int?> lots = [];
    readonly Dictionary<int, List<(int Lot, int? Flag)>?> lotRows = [];
    readonly HashSet<int> noted = [];

    /// <summary>Rule ID2: the EventLocation of a DS1 region of a MapObjects map.</summary>
    public static int RegionId(int ds1Entity) => 3710000 + ds1Entity % 10000;
    /// <summary>Boxes over DS1 collisions ("standing on hit"): 3720000 + entity % 10000.</summary>
    public static int HitRegionId(int ds1Entity) => 3720000 + ds1Entity % 10000;

    /// <summary>Rule-made boxes (hit regions) the script writes after translating: (id, position, rotation, size).</summary>
    public List<(int Id, Vector3 Pos, Vector3 Rot, Vector3 Size)> ExtraRegions { get; } = [];
    /// <summary>DS1 texts the text rule added (mapevent id → text), written by the script only when a group shows them.</summary>
    public Dictionary<int, string> PendingTexts { get; } = [];

    public Ds1MapContext(Pack? pack, Ds1Source ds1, MapObjects plan, IReadOnlyDictionary<int, int>? generators = null, IReadOnlyDictionary<int, int>? sounds = null)
    {
        this.pack = pack; this.ds1 = ds1; this.plan = plan;
        Ds1Map = plan.Ds1Map; ds2Map = plan.Ds2Map; aab = Ds1Map[..6];
        msb1 = MSB1.Read(Path.Combine(ds1.Root, "map", "MapStudio", Ds1Map + ".msb"));
        emevd = EMEVD.Read(Path.Combine(ds1.Root, "event", Ds1Map + ".emevd.dcx"));
        defs = Commands.EmevdDump.Defs();
        this.generators = generators ?? new Dictionary<int, int>();
        this.sounds = sounds ?? new Dictionary<int, int>();
        regionEntities = msb1.Regions.GetEntries().Where(r => r.EntityID > 0).Select(r => r.EntityID).ToHashSet();
    }

    public string Ds1Map { get; }
    public bool ResetsTemporaryFlags => true;                              // rule T1 (as both verified maps)
    public bool PlayerPoseIsCosmetic => true;                              // rule K6b
    public bool StandInsAreNoOps => true;                                  // ST1 opt-in (absent fog SFX, navmesh flags)
    public bool CharacterRulesM3 => true;                                  // session 11 (burg11, M3/M4): rules CR1-CR10
    public int? GoodsItem(int q) => pack != null ? GoodsPorter.Port(pack, ds1, q) : GoodsPorter.Id(q);   // rule IT1
    public bool NetworkWaitsAreNoOps => true;                              // rule N1
    public bool NestedInits => true;                                       // rule I1
    public int? Flag(int f) => FlagRules.Map(aab, f) ?? FlagRules.OtherMapPersistent(aab, f);

    // ---------------------------------------------------------------- objects
    public int? Object(int e)
    {
        if (plan.Instances.TryGetValue(e, out int i)) return i;
        return MapBuilder.PlacedObjects.TryGetValue((ds2Map, e), out int inst) ? inst : null;
    }

    bool IsTreasurePart(MSB1.Part part) => msb1.Events.Treasures.Any(t => t.TreasurePartName == part.Name);

    public bool ObjectAbsent(int e)
    {
        var part = msb1.Parts.Objects.FirstOrDefault(p => p.EntityID == e);
        if (part == null) return !regionEntities.Contains(e) && !msb1.Parts.GetEntries().Any(p => p.EntityID == e);
        if (Object(e) != null) return false;
        if (plan.FogTwins.Contains(e)) return true; // round 9, rule FW1b: a multiplayer twin wall, not placed
        if (IsTreasurePart(part)) return true;
        return !msb1.Parts.Objects.Any(p => p.EntityID > 0 && p.EntityID != e && Object(p.EntityID) != null && Vector3.Distance(p.Position, part.Position) < 0.05f);
    }

    /// <summary>WE4 damaging objects (the barrel 1010103's roll) have no damage rows here yet: 2005:9 → reported, the
    /// object rolls without hurting (stand-in, ST1).</summary>
    public bool ObjectDamages(int e) => plan.Damaging.ContainsKey(e) && Object(e) != null; // rule WE4 (MapScript writes the rows)

    string? PrefabModel(int e) => msb1.Parts.Objects.FirstOrDefault(p => p.EntityID == e) is { } part && Object(e) != null && plan.PrefabModels.ContainsKey(part.ModelName) ? part.ModelName : null;

    (int Play, int After, string Template)? Anim(int e, int anim)
    {
        if (PrefabModel(e) is not { } m) return null;
        var port = plan.Ports.First(p => p.Ds1Obj == m && p.Kind == ObjPorter.Kind.Prefab);
        anim = plan.AnimAlias.GetValueOrDefault((m.ToLowerInvariant(), anim), anim);
        var clip = port.Anims.FirstOrDefault(a => a.Ds1Anim.Equals($"a00_{anim:D4}", StringComparison.OrdinalIgnoreCase)).Ds2Anim;
        if (clip == null || clip.Length < 11) return null;
        int id = int.Parse(clip[4..6]) * 10000 + int.Parse(clip[7..11]);
        return AsylumScript.PrefabStates.TryGetValue(port.TemplateObj, out var t) && t.TryGetValue(id, out var s) ? (s.Play, s.After, port.TemplateObj) : null;
    }
    public int? ObjectAnimState(int e, int anim) => Anim(e, anim)?.Play;
    public int? ObjectAnimEndState(int e, int anim) => Anim(e, anim)?.After;
    public int? ObjectRestState(int e, int anim) => Anim(e, anim) is { } s && WorldEvents.PrefabRequests(Commands.ExtractDs2.DefaultOut, s.Template).Contains(s.After) ? s.After : null;
    public bool ObjectPoseBaked(int e, int anim) => plan.World.Any(p => p.Entity == e && p.Baked);
    public IReadOnlyList<(int Instance, float Seconds)> ObjectHits(int e, int anim)
    {
        var log = new List<string>();
        var hits = WorldEvents.Hits(ds1.Root, Ds1Map, msb1, e, anim, log);
        foreach (var l in log) { if (pack != null) pack.Note("WE4b " + l); }
        return hits.Where(h => Object(h.Target) != null).Select(h => (Object(h.Target)!.Value, h.Seconds)).ToList();
    }
    public bool BreakNeedsFallGuard(int e) => WorldEvents.Drop(ds1.Root, Ds1Map, msb1, e) is float d && d >= WorldEvents.DeadFallHeight();

    public int? ActionHelper(int e, int message) =>
        plan.Buttons.FirstOrDefault(b => b.Entity == e && b.Msg == message)?.Helper
        ?? plan.Areas.FirstOrDefault(b => b.Area == e && b.Message == message)?.Helper;
    public int? ObjActHelper(int e) => plan.Acts.FirstOrDefault(a => a.Entity == e)?.Helper;

    public int? Sound(int e) => sounds.TryGetValue(e, out int s) ? s : null;
    public int? SoundEffect(int e, int type, int id) => (sounds as MapSounds)?.EffectKey(e, type, id);
    public int? OneShotSfx(int e, int sfx) => (sounds as MapSounds)?.OneShotKey(e, sfx);
    public IEnumerable<int> SoundsStartedIn(long ev) => (sounds as MapSounds)?.StartedIn(ev) ?? [];
    /// <summary>Rule FW1: a light wall's SFX entity (wall + 1) → its white door.</summary>
    public int? SfxObject(int e) => plan.SfxDoors.TryGetValue(e, out int d) ? d : null;
    /// <summary>Rule FW1 (AsylumContext.FogCrossing's rule): boxes 2.5 m deep on each side of the wall's plane, 5 m wide, 4 m
    /// high from 1 m below the wall's origin, ids 3730000 + (wall % 10000)·2 + (0: +normal side, 1: −normal side).</summary>
    public (int From, int To)? FogCrossing(int fog, int area)
    {
        if (!plan.WhiteDoors.Any(w => w.Wall == fog)) return null;
        var wall = msb1.Parts.Objects.FirstOrDefault(p => p.EntityID == fog);
        var region = msb1.Regions.GetEntries().FirstOrDefault(r => r.EntityID == area);
        if (wall == null || region == null) return null;
        float yaw = wall.Rotation.Y * MathF.PI / 180;
        var n = new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw));
        int side = Vector3.Dot(region.Position - wall.Position, n) >= 0 ? 1 : -1;
        int Box(int s)
        {
            int id = 3730000 + fog % 10000 * 2 + (s > 0 ? 0 : 1);
            if (!ExtraRegions.Any(r => r.Id == id))
                ExtraRegions.Add((id, wall.Position + n * (1.25f * s) - new Vector3(0, 1f, 0), new Vector3(0, wall.Rotation.Y, 0), new Vector3(5f, 4f, 2.5f)));
            return id;
        }
        return (Box(side), Box(-side));
    }

    Dictionary<int, int>? eventLadders;
    public IReadOnlyDictionary<int, int> EventLadders => eventLadders ??= MapBuilder.EventLadders(ds1.Root, Ds1Map, msb1, includeKick: true)
        .Where(e => MapBuilder.PlacedObjects.ContainsKey((ds2Map, e))).ToDictionary(e => e, e => MapBuilder.PlacedObjects[(ds2Map, e)]);
    public int? Ladder(int e) => EventLadders.TryGetValue(e, out int i) ? i : null;

    public int? MessageItem(int m)
    {
        var match = Regex.Match(ds1.Text("Event_text_", m) is { Length: > 0 } t ? t : ds1.AnyText(m), @"<\?gdsparam@(\d+)\?>");
        if (!match.Success) return null;
        int goods = int.Parse(match.Groups[1].Value);
        return pack != null ? GoodsPorter.Port(pack, ds1, goods) : GoodsPorter.Id(goods);
    }

    // ---------------------------------------------------------------- regions
    public int? Region(int e) => regionEntities.Contains(e) ? RegionId(e) : null;
    public int? Point(int e) => Region(e);

    public int? HitRegion(int e)
    {
        var part = msb1.Parts.Collisions.FirstOrDefault(p => p.EntityID == e);
        if (part == null) return null;
        int id = HitRegionId(e);
        if (ExtraRegions.Any(r => r.Id == id)) return id;
        string d1 = Path.Combine(ds1.Root, "map", Ds1Map);
        var hi = BXF3.Read(Path.Combine(d1, $"h{Ds1Map[1..]}.hkxbhd"), Path.Combine(d1, $"h{Ds1Map[1..]}.hkxbdt"));
        var f = hi.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith($"h{part.ModelName[1..]}A", StringComparison.OrdinalIgnoreCase));
        if (f == null) return null;
        var m = Commands.ColSurvey.Place(part.Position, part.Rotation, part.Scale);
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (var (a, b, c) in Commands.ColSurvey.Triangles(DCX.Decompress(f.Bytes)))
            foreach (var p in new[] { a, b, c }) { var w = Vector3.Transform(p, m); min = Vector3.Min(min, w); max = Vector3.Max(max, w); }
        if (min.X > max.X) return null;
        ExtraRegions.Add((id, new Vector3((min.X + max.X) / 2, min.Y - 1f, (min.Z + max.Z) / 2), Vector3.Zero, new Vector3(max.X - min.X, max.Y - min.Y + 3f, max.Z - min.Z)));
        return id;
    }
    public int? HitGroup(int e) => null;                                   // toggled collisions: left out (no --hit-groups here)

    // ---------------------------------------------------------------- texts
    static FMG? ds2MapEvent;
    /// <summary>The Asylum's text rule: DS2's own mapevent entry for the same English text, else DS1's text as 18000000 +
    /// id % 1000000 (written only when a translated group shows it: <see cref="PendingTexts"/>).</summary>
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
        PendingTexts[id] = text;
        return id;
    }
    public bool MessageAbsent(int e) => msb1.Events.Messages.Any(x => x.EntityID == e) && MessageLocation(e) == null;
    public int? MessageLocation(int e) => BloodMessages.Location(ds1.Root, Ds1Map, e);
    public IEnumerable<int> TreasuresDisabledAtLoad => MapBuilder.StartDisabledPickups.TryGetValue(ds2Map, out var l) ? l : [];

    // ---------------------------------------------------------------- ObjActs (K1 prefab route, K6 groups)
    MSB1.Event.ObjAct? ObjAct(int flag) => msb1.Events.ObjActs.FirstOrDefault(a => a.ObjActEntityID == flag);
    MSB1.Part.Object? ObjActPart(MSB1.Event.ObjAct a) => msb1.Parts.Objects.FirstOrDefault(p => p.Name == a.ObjActPartName);
    public int? ObjActFlag(int f) => ObjAct(f) is { } a && ObjActPart(a) is { } p && ObjActHelper(p.EntityID) != null ? Flag(f) : null;
    public bool ObjActAbsent(int f) => ObjAct(f) is { } a && ObjActPart(a) is { } p && (plan.BorderDoors.Contains(p.EntityID) || ObjActHelper(p.EntityID) == null);
    /// <summary>Every ObjAct here takes the prefab route (K1g): the requirement is the K6 group's, no DS2 door lock.</summary>
    public EmevdTranslator.ObjActLock? Lock(int f) => null;
    public bool SpEffectGatesObjAct(int sp) => msb1.Events.ObjActs.Any(a => ObjActPart(a) is { } p
        && ds1.Params.Params["ObjActParam"][a.ObjActParamID >= 0 ? a.ObjActParamID : int.Parse(p.ModelName[1..])] is { } row
        && Convert.ToInt32(row["spQualifiedType"].Value) == 2 && Convert.ToInt32(row["spQualifiedId"].Value) == sp);

    // ---------------------------------------------------------------- characters
    public int? Generator(int e) => generators.TryGetValue(e, out int g) ? g : null;
    public int? CharacterSouls(int e)
    {
        var part = msb1.Parts.Enemies.FirstOrDefault(p => p.EntityID == e);
        if (part == null || ds1.Params.Params["NpcParam"][part.NPCParamID] is not { } row) return null;
        return Chr.Ds1Calibration.Souls(row);   // bal11 rule BAL2
    }
    public bool IsBoss(int e) => e > 10000 && ds1.Params.Params["GameAreaParam"][e] != null;

    // ---------------------------------------------------------------- session 11 (burg11, M3/M4): AsylumContext's character and
    // boss rules (W1, N-H2, B-k, BossBattleParam, logic flag, start-up, wake) over this map's data, as the Asylum verified them
    /// <summary>Rule W1: a placed non-boss character whose generator carries DS1's world-state fold (MapEnemies / AsylumEnemies).</summary>
    public bool WorldOwned(int e) => Generator(e) != null && !IsBoss(e) && AsylumEnemies.WorldConditions(Ds1Map, e) != null;
    /// <summary>Rule N-H2: a placed NPC whose generator carries DS1's load-time hostility.</summary>
    public bool NpcHostile(int e) => e is > 0 and < 10000 && Generator(e) != null
        && (NpcPorter.HostileAtLoad(ds1.Root, Ds1Map, e) ?? NpcPorter.FindHostile(ds1.Root, Ds1Map, e) ?? MapEnemies.DamageHostility(ds1.Root, Ds1Map, e)) is int f && FlagRules.Any(f) != null;   // + rules N-H3 / N-H5: hostile on attack
    List<int> BossEntities => bossEntities ??= msb1.Parts.Enemies.Where(p => p.EntityID > 0 && IsBoss(p.EntityID)).Select(p => p.EntityID).Distinct().Order().ToList();
    List<int>? bossEntities;
    readonly HashSet<int> bossesBuilt = [];
    readonly Dictionary<(int, int), double?> wakeClips = [];

    /// <summary>BossBattleParam row (id = DS1 entity), AsylumContext.BossBattle's rule: Smelter Demon's 1019000 cloned, souls from
    /// GameAreaParam (BAL2), the boss's NpcParam drops, killed flag = the flag DS1's defeat event sets before Handle Boss Defeat,
    /// generator event group of the boss's B-k index, name = DS1's HP-bar NameID.</summary>
    public int? BossBattle(int e, int nameId)
    {
        // rule MB1: a further part of a boss's fight (the second Bell Gargoyle 1010801) → its spec's boss battle (one battle, the
        // second HP-bar name as bossNameTextId2)
        if (!IsBoss(e)) return SpecOf(e) is { } s && s.Ds1Entity != e && IsBoss(s.Ds1Entity) && Generator(e) != null ? BossBattle(s.Ds1Entity, nameId) : null;
        int id = EntityRules.BossBattle(e);
        if (pack == null) return id;
        var bb = pack.Param("BossBattleParam");
        if (bossesBuilt.Add(id) && bb[id] == null)
        {
            var row = pack.AddRow("BossBattleParam", bb[1019000]!, id, $"[Lordran] DS1 boss {e}");
            var area = ds1.Params.Params["GameAreaParam"][e]!;
            Set(row, "soulValue", Chr.Ds1Calibration.BossSouls(area));
            int lot = BossLot(e);
            Set(row, "itemLotId", lot); Set(row, "itemLotIdMultiPlay", lot);
            int killed = KilledFlag(e) ?? 0;
            Set(row, "killedEventId", killed); Set(row, "onceKilledEventId", killed);
            Set(row, "bonfireEventValueId", 0); Set(row, "bonfireId", 0);
            Set(row, "genEventGroupId1", EntityRules.BossEventGroupOf(BossEntities.IndexOf(e))); Set(row, "genEventGroupId2", 0); Set(row, "genEventGroupId3", 0);
            Set(row, "hpDispType2", 0); Set(row, "hpDispType3", 0);
            Set(row, "bossNameTextId1", 0); Set(row, "bossNameTextId2", 0); Set(row, "bossNameTextId3", 0);
            pack.Note($"{ds2Map}: BossBattleParam {id} (DS1 boss {e}, B-k {BossEntities.IndexOf(e)}): souls {row["soulValue"].Value} (DS1 GameAreaParam {area["bonusSoul_single"].Value}), lot {lot}, killed flag {killed}");
        }
        if (nameId > 0 && bb[id] is { } r2)
        {
            int text = EntityRules.CharaName(nameId);
            pack.SetText("talk/charaname", text, ds1.Text("NPC_name_", nameId));
            if (Convert.ToInt32(r2["bossNameTextId1"].Value) == 0) Set(r2, "bossNameTextId1", text);
            else if (Convert.ToInt32(r2["bossNameTextId1"].Value) != text && Convert.ToInt32(r2["bossNameTextId2"].Value) == 0) { Set(r2, "bossNameTextId2", text); Set(r2, "hpDispType2", 1); }
        }
        return id;
    }

    int BossLot(int e)
    {
        var part = msb1.Parts.Enemies.FirstOrDefault(p => p.EntityID == e);
        if (part == null || pack == null || ds1.Params.Params["NpcParam"][part.NPCParamID] is not { } npc) return 0;
        var notes = new List<string>();
        var drops = Ds1Drops.Resolve(ds1, npc, pack, notes);
        foreach (var n in notes) pack.Note($"boss {e} drops: {n}");
        if (drops.Count == 0) return 0;
        int id = EntityRules.ItemLot(Convert.ToInt32(npc["itemLotId_1"].Value));
        if (pack.Param("ItemLotParam2_Other")[id] == null) WriteLot(id, $"[Lordran] DS1 boss {e} drops", drops.Select(d => (d.ItemId, d.Num, d.Reinforce, d.Percent)).ToList());
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

    /// <summary>Rule T1-B: every placed boss's logic flag (reset at load).</summary>
    public IEnumerable<int> BossLogicFlags => BossEntities.Select(LogicFlag).OfType<int>().Distinct().Order();
    /// <summary>The boss's logic flag (FlagRules.BossLogicFlag: the map's k-th DS1 boss in entity order).</summary>
    public int? LogicFlag(int e) => IsBoss(e) && Generator(e) != null ? FlagRules.BossLogicFlag(aab, BossEntities.IndexOf(e)) : null;
    /// <summary>The 1:1 boss's start-up state for a DS1 waiting animation (Ds1Boss.StartUpType).</summary>
    public int? StartUpState(int e, int anim) => SpecOf(e) is { } s && Generator(e) != null ? Ds1Boss.StartUpType(s, anim) : null;
    /// <summary>DS1 length of the 1:1 boss's start-up wake clip when <paramref name="anim"/> is it.</summary>
    public double? WakeClip(int e, int anim)
    {
        if (wakeClips.TryGetValue((e, anim), out var d)) return d;
        double? r = null;
        if (SpecOf(e) is { } s && Generator(e) != null && (Ds1Boss.WakeClip(s, 2) == anim || Ds1Boss.WakeClip(s, 3) == anim))
            try { r = Ds1ChrSource.Load(ds1.Root, s.Ds1Chr).Samples(anim).Duration; }
            catch (KeyNotFoundException) { r = null; if (pack != null) pack.Note($"{ds2Map}: wake clip a00_{anim} of {s.Ds1Chr} not in its anibnd: no wake wait"); }
        return wakeClips[(e, anim)] = r;
    }
    /// <summary>The boss-pipeline spec of a part (its own entity, or a further part of a spec's DS1 NpcParams: MapEnemies MB1).</summary>
    Ds1Boss.Spec? SpecOf(int e) => Ds1Boss.ForEntity(e) ?? (msb1.Parts.Enemies.FirstOrDefault(p => p.EntityID == e) is { } part
        ? Ds1Boss.All.FirstOrDefault(s => s.Ds1Map == Ds1Map && s.Ds1Chr == part.ModelName && (s.Ds1Npc == part.NPCParamID || s.ExtraVariants.Any(x => x.Npc == part.NPCParamID))) : null);
    public bool FallControl(int sp) => ds1.Params.Params["SpEffectParam"][sp] is { } row && Convert.ToInt32(row["stateInfo"].Value) == 47;

    // ---------------------------------------------------------------- session 11 (track chrai11): rules EM1 / FA1 / HB1
    /// <summary>Rule EM1: a placed boss-pipeline part whose translated AI plays a clip sending the message (one of its DS1 attacks the
    /// pipeline built, a TAE-only attack the battle goal requests (EM1-n), or an FA1 clip) → (generator, the message's board code).</summary>
    public (int Generator, int Code)? EventMessage(int e, int msg)
    {
        if (SpecOf(e) is not { Ai: not null } s || Generator(e) is not int g) return null;
        var fa1 = Ai.Boss.EventMessages.Layout(ds1.Root, s);
        var src = sources.TryGetValue(s.Ds1Chr, out var cs) ? cs : sources[s.Ds1Chr] = Ds1ChrSource.Load(ds1.Root, s.Ds1Chr);
        bool sent = Ai.Boss.EventMessages.Of(ds1.Root, s.Ds1Chr).Where(m => m.Message == msg).Any(m =>
            s.Anim.Singles.Contains(m.Anim) && src.Has(m.Anim) || fa1?.ClipCode.ContainsKey(m.Anim) == true || m.Anim is >= 3000 and < 3030 && !src.Has(m.Anim));
        return sent && Ai.Boss.EventMessages.Code(ds1.Root, s.Ds1Chr, msg) is int code ? (g, code) : null;
    }
    readonly Dictionary<string, Ds1ChrSource> sources = [];

    /// <summary>Rule EM1-r: every clip sending the message is the part's NPC-part break reaction (a clip DS1 forces right after an NPC
    /// part HP test: TC1's, played by DS2's part break).</summary>
    public bool ReactionMessage(int e, int msg)
    {
        if (SpecOf(e) is not { } s || Generator(e) == null) return false;
        var breakClips = Ai.Boss.EventMessages.ForcedClips(emevd, new HashSet<int> { e }).Where(x => x.AfterPartBreak).Select(x => x.Clip).ToHashSet();
        // session 11 (dchr11), rule EM1-r2: DS1 may request the break reaction through the AI instead of a forced clip (the Gaping Dragon's
        // 11005396: 4:6 part HP ≤ 0 → 2004:6 EzState request 1001 = a00_8000): the clip the spec's DS2 break slot (a00_16_8107, rule TC1) plays
        // counts as the part-break reaction when the map's events wait for that character's part HP
        if (s.Anim.Roles.FirstOrDefault(r => r.Slot == "a00_16_8107") is { Ds1.Length: > 0 } br && br.Ds1[0] > 0
            && emevd.Events.SelectMany(ev => ev.Instructions).Any(i => i.Bank == 4 && i.ID == 6 && i.ArgData.Length >= 8 && BitConverter.ToInt32(i.ArgData, 4) == e))
            breakClips.Add(br.Ds1[0]);
        var clips = Ai.Boss.EventMessages.Of(ds1.Root, s.Ds1Chr).Where(m => m.Message == msg).Select(m => m.Anim).ToList();
        return clips.Count > 0 && clips.All(breakClips.Contains);
    }

    /// <summary>Rule FA1: the part's request flag and DS1 length of a forced clip it plays as an action (MapEnemies placed them).</summary>
    public (int Flag, double Seconds)? ForcedClip(int e, int anim) =>
        Ai.Boss.EventMessages.Fa1Of(Ds1Map, e) is { } f && f.ClipFlags.TryGetValue(anim, out int fl) ? (fl, f.ClipSeconds[anim]) : null;
    public int? AiOffFlag(int e) => Ai.Boss.EventMessages.Fa1Of(Ds1Map, e) is { AiOffFlag: > 0 } f ? f.AiOffFlag : null;

    /// <summary>Rule HB1: the breath bullet of the part's spec (Ds1Boss built it).</summary>
    public (int Generator, int Bullet)? Breath(int e, int obj, int anim) =>
        SpecOf(e) is { } s && Generator(e) is int g && Ds1Boss.BreathBulletId(ds1.Root, ds1, s) is int b && (pack == null || pack.Param("EnemyBulletParam")[b] != null) ? (g, b) : null;

    // ---------------------------------------------------------------- items (AsylumContext's rules)
    public int? RingItem(int sp) => RingPorter.ItemForSpEffect(pack, ds1, sp);
    public int? ItemLotFlag(int lot) => ds1.Params.Params["ItemLotParam"][lot] is { } r && Convert.ToInt32(r["getItemFlagId"].Value) is int f and > 0 ? f : null;

    public int? ItemLot(int lot)
    {
        if (lots.TryGetValue(lot, out var done)) return done;
        var dl = ds1.Params.Params["ItemLotParam"];
        var rows = new List<PARAM.Row>();
        for (int k = lot; rows.Count < 8 && dl[k] is { } r; k++) rows.Add(r);
        int? result = null;
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
            if (pack != null && pack.Param("ItemLotParam2_Other")[result.Value] == null) WriteLot(result.Value, $"[Lordran] DS1 item lot {lot}", items);
        }
        return lots[lot] = result;
    }

    public List<(int Lot, int? Flag)>? ItemLotRows(int lot)
    {
        if (lotRows.TryGetValue(lot, out var done)) return done;
        var dl = ds1.Params.Params["ItemLotParam"];
        var rows = new List<PARAM.Row>();
        for (int k = lot; rows.Count < 8 && dl[k] is { } r; k++) rows.Add(r);
        List<(int, int?)>? result = null;
        if (rows.Any(r => Convert.ToInt32(r["getItemFlagId"].Value) > 0))
        {
            result = [];
            foreach (var r in rows)
            {
                var entries = Enumerable.Range(1, 8).Select(i => (Id: Convert.ToInt32(r[$"lotItemId0{i}"].Value), Cat: unchecked((uint)Convert.ToInt64(r[$"lotItemCategory0{i}"].Value)),
                    Points: Convert.ToDouble(r[$"lotItemBasePoint0{i}"].Value), Num: Convert.ToInt32(r[$"lotItemNum0{i}"].Value))).Where(x => x.Points > 0 && x.Id > 0).ToList();
                var items = new List<(int Item, int Num, int Reinforce, float Percent)>();
                double total = entries.Sum(x => x.Points);
                foreach (var x in entries)
                {
                    int reinforce = 0, ds2 = pack != null ? Ds1Drops.MapItem(pack, ds1, x.Cat, x.Id, out _, out reinforce, out _) : x.Id;
                    if (ds2 == 0) { pack?.Note($"item lot {lot} row {r.ID}: DS1 item {x.Id} not in DS2 yet, left out"); continue; }
                    items.Add((ds2, Math.Max(1, x.Num), reinforce, entries.Count == 1 ? 100f : (float)(100 * x.Points / total)));
                }
                if (items.Count == 0) continue;
                int id = EntityRules.ItemLot(r.ID);
                if (pack != null && pack.Param("ItemLotParam2_Other")[id] == null) WriteLot(id, $"[Lordran] DS1 item lot {r.ID}", items);
                int f = Convert.ToInt32(r["getItemFlagId"].Value);
                result.Add((id, f > 0 ? Flag(f) : null));
            }
        }
        return lotRows[lot] = result;
    }

    void WriteLot(int id, string name, List<(int Item, int Num, int Reinforce, float Percent)> items)
    {
        var row = pack!.AddRow("ItemLotParam2_Other", pack.Param("ItemLotParam2_Other")[60042000]!, id, name);
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
    /// <summary>A point of another ported DS1 map whose host is in this pack and whose EventLocation already has the point
    /// (written by that map's own region rule): its host id + row; otherwise null (no warp).</summary>
    public (int Map, int Point)? MapPoint(int area, int block, int point)
    {
        string target = $"m{area:D2}_{block:D2}_00_00";
        if (MapHosts.Of(target) is not { } row || pack == null) return null;
        int id = row.Option == "--map burg" ? RegionId(point) : AsylumScript.RegionId(point);
        try
        {
            var p = PARAM.Read(pack.GetFile("gamedata", $"param/eventlocation_{row.Host}.param"));
            if (p.Rows.Any(r => r.ID == id)) return (row.HostId, id);
        }
        catch (Exception) { }
        return null;
    }

    static void Set(PARAM.Row row, string field, object v) => row[field].Value = Convert.ChangeType(v, row[field].Value.GetType());
}
