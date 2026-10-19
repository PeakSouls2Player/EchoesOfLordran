using System.Numerics;
using Lordran.Tools.Build.Chr;
using Lordran.Tools.Build.Events;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// DS1's own enemy placements for a converted map, as DS2 generators: every MSB enemy part of a ported character
/// (ChrPorter) whose DS1 NpcParam has a variant, at its DS1 position and facing. Only the first visit's enemies
/// (entity group 1, 18101xx in the Asylum): DS1 places a second set (18102xx) at the same spots for the return trip.
/// Generators clone Things Betwixt's generator 1600 (an Ogre: spawns on approach, aggroes; see EnemyTest).
/// Session 8: generator ids follow the DS1 entity (<see cref="EntityRules.Generator"/>: 1810101 → 101) instead of an
/// MSB-order counter from the model id (2500, 2501 …), so translated DS1 events address the same character; a DS1 boss
/// (DS1 GameAreaParam row) gets the boss event group (<see cref="EntityRules.BossEventGroup"/>, BossBattleParam
/// genEventGroupId1). Parts without an entity id (none in the Asylum) keep a model-based id (NewModel·10 + n).
/// Fix pass (review): Firelink's ids 210/211 equal Things Betwixt generator ids of the template param; EnemyTest keeps
/// only the row objects it created, so TB's rows are dropped (checked offline: every generator, location and regist row
/// of both maps equals the installed pack's apart from the id, and 210/211 carry our skeleton regist 29000000).
/// </summary>
public static class AsylumEnemies
{
    /// <param name="Think">The MSB part's DS1 NpcThinkParam (track enemies step 2: its battle goal becomes the DS2 AI).</param>
    public sealed record Placed(int Entity, int Generator, ChrPorter.Variant Variant, Vector3 Position, float Yaw, bool Boss, int Think = 0)
    {
        /// <summary>Session 11 (ret11), rule P3a: the MSB part's InitAnimID (the pose DS1 starts it in), -1 = none.</summary>
        public int InitAnim { get; init; } = -1;
    }

    /// <summary>Session 9 (track enemyfix), build-pack --only-enemy ID[+ID...]: isolation builds place only DS1 enemies with these
    /// EnemyParam ids (both maps; the NPCs stay). Applied here, so the event translators see the same placements. Empty = all.
    /// Chrint stage 1 (merge with the boss track): BuildPack's boss loop applies the same filter to the 1:1 bosses
    /// (<see cref="BossPlacements"/> stays pure: the report default of AsylumContext still lists the boss).</summary>
    public static HashSet<int> OnlyEnemyParams = [];

    /// <summary>Session 11 (track ret11, rule V-ret): per DS1 map, the MSB parts of ported characters left out by the last
    /// <see cref="Placements"/> call because their NpcParam has no variant (build log).</summary>
    public static readonly Dictionary<string, List<string>> Skipped = [];

    /// <summary>The generators <see cref="Build"/> places (pure: the event translator asks the same question).</summary>
    public static List<Placed> Placements(MSB1 msb, string ds1Map, IEnumerable<ChrPorter.Options> chrs, int? onlyGroup = 1, IReadOnlySet<int>? skipEntities = null,
        Func<MSB1.Part.Enemy, bool>? skipPart = null)
    {
        // DS1 entity ids: AA*100000 + BB*10000 + group*100 + n (Asylum 1810101 = group 1, the first visit; group 2 = return)
        int mapBase = int.Parse(ds1Map[1..3]) * 100000 + int.Parse(ds1Map[4..6]) * 10000;
        var areas = Ds1Source.LoadParam(Program.DefaultDsrRoot, "GameAreaParam");
        var list = new List<Placed>();
        foreach (var chr in chrs)
        {
            var variants = chr.Variants.ToDictionary(v => v.Ds1Npc);
            int n = 0;
            foreach (var e in msb.Parts.Enemies.Where(e => e.ModelName == chr.Ds1Chr))
            {
                bool inGroup = onlyGroup == null || (e.EntityID - mapBase) / 100 == onlyGroup;
                if (!inGroup || !variants.TryGetValue(e.NPCParamID, out var v)) continue;
                if (skipEntities != null && skipEntities.Contains(e.EntityID)) continue; // session 11 (burg11): placed by another rule (the map's NPC table)
                if (skipPart != null && skipPart(e)) continue;                            // session 11 (burg11, round 7): rule P2x
                if (OnlyEnemyParams.Count > 0 && !OnlyEnemyParams.Contains(v.EnemyParamId)) continue;
                int gen = e.EntityID > 0 ? EntityRules.Generator(e.EntityID) : chr.NewModel * 10 + n;
                n++;
                list.Add(new Placed(e.EntityID, gen, v, e.Position, e.Rotation.Y, e.EntityID > 0 && areas?[e.EntityID] != null, e.ThinkParamID) { InitAnim = e.InitAnimID });
            }
        }
        // session 11 (track ret11), rule V-ret: with the world-state fold (W1) the visit is the generators' business, so every
        // MSB part of a ported character is placed (onlyGroup null); parts whose NpcParam has no variant yet are listed
        Skipped[ds1Map] = [.. chrs.SelectMany(chr => msb.Parts.Enemies.Where(e => e.ModelName == chr.Ds1Chr
            && (onlyGroup == null || (e.EntityID - mapBase) / 100 == onlyGroup) && !chr.Variants.Any(v => v.Ds1Npc == e.NPCParamID))
            .Select(e => $"{e.Name} (entity {e.EntityID}, NpcParam {e.NPCParamID}): no variant")).Distinct()];
        var dup = list.GroupBy(p => p.Generator).FirstOrDefault(g => g.Count() > 1);
        if (dup != null) throw new InvalidDataException($"{ds1Map}: two DS1 characters map to generator {dup.Key} ({string.Join(", ", dup.Select(p => p.Entity))})");
        return list;
    }

    /// <summary>
    /// Session 11 (track ret11), rule P3a (the part of asy10's P3 that needs no network change): a DS1 InitAnimID whose clip
    /// already plays in one of the 1:1 network's start-up slots (c1030 template: StartupType 2 / 3 / 4 = dormant loops
    /// a75_01_7100 / 7110 / 7120, roles of <see cref="Ds1RigAnims.HollowUnarmed"/>: 7100 = DS1 a00_9000 sitting) starts the
    /// character in that slot (GeneratorRegistParam.startUpStateType, as the boss's 9000 → 2): the ladder-room Hollow 1810102
    /// (InitAnimID 9000) sits. A pose clip without a slot (9005 / 9006 / 9008 / 9010: the corridor and cell Hollows) needs P3's
    /// added start-up categories (open) and keeps standing (0), reported. When DS2's generic upper (ai103010) wakes a slot is U.
    /// </summary>
    static byte PoseStartUp(Placed p, out string note)
    {
        note = "";
        if (p.InitAnim <= 0) return 0;
        var rig = Ai.Ds1Ai.RigOf(p.Variant.EnemyParamId);
        if (rig == null) { note = $"InitAnimID {p.InitAnim}: no 1:1 network (standing)"; return 0; }
        // session 11 (chrai11), rule P3: + the added start-up types 5-7 (Ds1RigAnims.AddedStartUps, when the DS1 set has the pose)
        foreach (var (slot, type) in new[] { ("a75_01_7100", 2), ("a75_01_7110", 3), ("a75_01_7120", 4) }.Concat(Ds1RigAnims.AddedStartUps.Select(a => (a.Loop, a.Type))))
            if (rig.Roles.FirstOrDefault(r => r.Slot == slot) is { Ds1.Length: > 0 } role && role.Ds1[0] == p.InitAnim)
            {
                note = $"InitAnimID {p.InitAnim} plays in start-up slot {slot} → startUpStateType {type}";
                return (byte)type;
            }
        note = $"InitAnimID {p.InitAnim}: no start-up slot plays a00_{p.InitAnim} in c{rig.AnimId / 100:D4} (rule P3's added categories: open) → standing (0)";
        return 0;
    }

    /// <summary>
    /// Session 11 (track ret11), rule W1: the generator conditions DS1's world-state fold gives a (non-boss) DS1 character
    /// (<see cref="WorldState"/>), flags through FlagRules (F1/F2), or null when it needs none ("always": the template's
    /// unconditional start / the NPC dead-flag rule) or cannot have them (fallback: the translated 2004:5 commands stay).
    /// Pure: the event translator asks the same question (<see cref="AsylumContext.WorldOwned"/>).
    /// </summary>
    public static (EnemyTest.GenCondition Start, EnemyTest.GenCondition? Stop, WorldState.Entry Entry)? WorldConditions(string ds1Map, int entity)
    {
        if (entity <= 0) return null;
        if (!WorldState.Fold(Program.DefaultDsrRoot, ds1Map).TryGetValue(entity, out var e)) return null;
        var g = WorldState.ToGenerator(e, f => FlagRules.Map(ds1Map[..6], f) ?? FlagRules.OtherMapPersistent(ds1Map[..6], f), out _);
        return g is { } x ? (x.Start, x.Stop, e) : null;
    }

    internal static EnemyTest.Custom WithWorld(Pack pack, string ds1Map, int entity, EnemyTest.Custom c)
    {
        if (!WorldState.Fold(Program.DefaultDsrRoot, ds1Map).TryGetValue(entity, out var e)) return c;
        if (WorldConditions(ds1Map, entity) is not { } w)
        {
            if (e.Kind != "always") pack.Note($"{ds1Map} W1 entity {entity} (generator {c.NewGenerator}): {e} → no generator condition (event commands keep its state)");
            return c;
        }
        return c with { StartCondition = w.Start, StopCondition = w.Stop, WorldNote = $"DS1 entity {entity}: enabled iff {e}" };
    }

    /// <summary>A 1:1 DS1 boss placed at its MSB part (session 8, track boss step 2).</summary>
    public sealed record BossPlaced(int Entity, int Generator, Ds1Boss.Spec Spec, Vector3 Position, float Yaw);

    /// <summary>
    /// DS1's bosses the 1:1 pipeline builds (<see cref="Ds1Boss.All"/>) at their MSB parts of the map (Asylum: the Asylum Demon
    /// c2232_0000, entity 1810800, on the hall floor): generator <see cref="EntityRules.Generator"/> (800), the boss event group
    /// (BossBattleParam.genEventGroupId1), DS2's boss generator flags and the map's boss logic flag
    /// (<see cref="EnemyTest.Custom.BossLogicFlag"/>, <see cref="FlagRules.BossLogicFlag"/>). Pure: the event translator asks
    /// the same question.
    /// </summary>
    public static List<BossPlaced> BossPlacements(MSB1 msb, string ds1Map) =>
        [.. msb.Parts.Enemies.Where(e => e.EntityID > 0 && Ds1Boss.ForEntity(e.EntityID) is { } s && s.Ds1Map == ds1Map && s.Ds1Chr == e.ModelName)
            .Select(e => new BossPlaced(e.EntityID, EntityRules.Generator(e.EntityID), Ds1Boss.ForEntity(e.EntityID)!, e.Position, e.Rotation.Y))];

    /// <summary>The boss's generator (TB 1600's rows like the regulars; DS2 boss generator fields).</summary>
    public static EnemyTest.Custom BossCustom(BossPlaced b, MSB1 msb, string ds1Map)
    {
        var areas = Ds1Source.LoadParam(Program.DefaultDsrRoot, "GameAreaParam");
        var bosses = msb.Parts.Enemies.Where(p => p.EntityID > 0 && areas?[p.EntityID] != null).Select(p => p.EntityID).Distinct().Order().ToList();
        uint flag = (uint)(FlagRules.BossLogicFlag(ds1Map, bosses.IndexOf(b.Entity)) ?? 0);
        return new EnemyTest.Custom(1600, b.Generator, b.Spec.EnemyParam, b.Spec.HighLogic, b.Spec.LowLogic, b.Position, b.Yaw)
            { EventGroup = EntityRules.BossEventGroupOf(bosses.IndexOf(b.Entity)), BossLogicFlag = flag, LogicGroup = EntityRules.BossLogicGroupOf(bosses.IndexOf(b.Entity)) };   // session 11 (ret11): rule B-k
    }

    /// <param name="onlyGroup">DS1 entity group to place (Asylum: 1 = first visit), or null for every part.</param>
    /// <param name="npcs">Session 8 (track npc): the map's DS1 NPC generators (<see cref="NpcPorter"/>), written into the
    /// same generator params (one EnemyTest.Build per map: a second call would replace the files).</param>
    /// <param name="extra">further generators written with these (session 8, track boss: the 1:1 bosses, <see cref="BossCustom"/>);
    /// not returned to the event translator</param>
    /// <param name="extraEntities">DS1 entity → generator of the <paramref name="extra"/> generators the event translator should
    /// address (step 2: the 1:1 boss)</param>
    /// <returns>DS1 character entity → DS2 generator, for the event translator.</returns>
    public static Dictionary<int, int> Build(Pack pack, string dsrRoot, MapBuilder.Options map, IEnumerable<ChrPorter.Options> chrs, int? onlyGroup = 1,
        IReadOnlyList<NpcPorter.Placed>? npcs = null, IEnumerable<EnemyTest.Custom>? extra = null, IReadOnlyDictionary<int, int>? extraEntities = null,
        IReadOnlySet<int>? skipEntities = null, bool npcDeathOwns = false, Func<MSB1.Part.Enemy, bool>? skipPart = null)
    {
        var msb = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", map.Ds1Map + ".msb"));
        var placed = Placements(msb, map.Ds1Map, chrs, onlyGroup, skipEntities, skipPart);
        // track enemies step 2: each placement's DS1 think → its translated logic pair; one GeneratorRegistParam row per
        // (EnemyParam, logic pair): EnemyId·100 + k (DS1 places one NpcParam with different thinks, e.g. Firelink's sword
        // Warriors 254008 / 254011)
        var registOf = new Dictionary<(int, int, int, byte), int>();
        // session 11 (fx11), rules E-REQ / E-REQ-P: DS1's event AI requests (2004:17) on this map's placed regulars — the AI side
        // (GetEventRequest → f5903 channels) before any think is translated, the event side (channel flags on the generators,
        // EmevdTranslator) below. The Asylum keeps its build (its only request goes to the Stray Demon, rule E-REQ-B; the boss
        // pipeline translates the Asylum's bosses without channels, as Ds1Boss does).
        bool ereq = map.Ds1Map != "m18_01_00_00" && ChrPorter.UseDs1Ai;
        if (ereq) Ai.EventRequests.Register(dsrRoot, map.Ds1Map);
        var requests = ereq ? Ai.EventRequests.ByEntity(dsrRoot, map.Ds1Map) : new Dictionary<int, SortedSet<(int Slot, int V)>>();
        int nChannelFlag = 0;
        var customs = placed.Select(p =>
        {
            var logic = Ai.Ds1Ai.Logic(pack, dsrRoot, map.Ds1Map, p.Variant.EnemyParamId, p.Think);
            int[] channelFlags = [];
            if (ereq && logic != null && p.Entity > 0 && requests.ContainsKey(p.Entity) && Ai.EventRequests.ForThink(p.Think) is { Count: > 0 } ch)
            {
                var list = ch.Select(c => (c.Slot, c.V, c.K, Flag: FlagRules.AiChannelFlag(map.Ds1Map, nChannelFlag++) ?? throw new InvalidDataException($"{map.Ds1Map}: no host for AI channel flags"))).ToList();
                Ai.EventRequests.Place(map.Ds1Map, p.Entity, list);
                channelFlags = new int[3];
                foreach (var c in list) channelFlags[c.K - 1] = c.Flag;
                pack.Note($"{map.Ds2Map} E-REQ-P entity {p.Entity} (generator {p.Generator}, think {p.Think}): {string.Join(", ", list.Select(c => $"slot {c.Slot} = {c.V} → channel {c.K} flag {c.Flag}"))}");
            }
            int high = logic?.High ?? p.Variant.HighLogic, low = logic?.Low ?? p.Variant.LowLogic;
            byte su = PoseStartUp(p, out string poseNote);   // session 11 (ret11), rule P3a
            if (poseNote.Length > 0) pack.Note($"{map.Ds2Map} P3a entity {p.Entity}: {poseNote}");
            if (!registOf.TryGetValue((p.Variant.EnemyParamId, high, low, su), out int regist))
                registOf[(p.Variant.EnemyParamId, high, low, su)] = regist = p.Variant.EnemyParamId * 100 + registOf.Keys.Count(k => k.Item1 == p.Variant.EnemyParamId);
            return new EnemyTest.Custom(1600, p.Generator, p.Variant.EnemyParamId, high, low, p.Position, p.Yaw)
                { EventGroup = p.Boss ? EntityRules.BossEventGroup : 0, RegistId = regist, StartUpStateType = su, ChannelFlags = channelFlags };
        }).ToList();
        // track npc: the NPCs go into the same generator params (their regist rows keep EnemyId·100: NPC EnemyParams are
        // their own, so no enemy's EnemyId·100 + k can take them; EnemyTest.Build refuses a shared regist id anyway)
        var clash = npcs?.FirstOrDefault(n => placed.Any(p => p.Generator == n.Custom.NewGenerator));
        if (clash != null) throw new InvalidDataException($"{map.Ds1Map}: NPC {clash.Name} and an enemy share generator {clash.Custom.NewGenerator}");
        // session 9 (track npcplace): the enemies' customs carry DS1's MSB position and rotation up to here: rules P1 (DS2
        // yaw = DS1 yaw + 180) and P2 standing (root on our DS2 collision), Ds1Placement.Generator. (Here, after the clash
        // check, so the line survives other tracks' rewrites of the customs construction; the NPCs added below are placed
        // by NpcPorter; the boss track's BossCustom needs the same call: NOTES §5.)
        var probe = FloorProbe.FromPack(pack, map.Ds2Map);
        customs = [.. customs.Select(c => Ds1Placement.Generator(probe, c))];
        if (npcs != null) customs.AddRange(npcs.Select(n => n.Custom));
        // session 11 (track ret11), rule W1: DS1's world-state fold → each regular's / NPC's generator start/stop conditions
        // (bosses are added below as extras and stay unconditional: BossBattleParam + their translated events own them)
        var byGen = placed.Where(p => p.Entity > 0).ToDictionary(p => p.Generator, p => p.Entity);
        foreach (var n in npcs ?? []) byGen[n.Custom.NewGenerator] = n.Entity;
        // session 11 (burg11), rule W1-N (npcDeathOwns): an NPC whose generator carries NpcPorter's death rule (start condition 8 on
        // its dead flag = respawnDisableFlag, the talk owner's start flag esd-check holds) keeps it; W1 only for the others (Havel)
        customs = [.. customs.Select(c => byGen.TryGetValue(c.NewGenerator, out int ent) && !placed.Any(p => p.Entity == ent && p.Boss)
            && !(npcDeathOwns && c.Npc && c.DeadFlag != 0) ? WithWorld(pack, map.Ds1Map, ent, c) : c)];
        foreach (var chr in chrs)
        {
            int count = placed.Count(p => chr.Variants.Contains(p.Variant));
            pack.Note($"{map.Ds2Map}: {count} DS1 {chr.Ds1Chr} enemies placed from the MSB ({(onlyGroup == 1 ? "first visit" : "all")}); generators {string.Join(", ", placed.Where(p => chr.Variants.Contains(p.Variant)).Select(p => $"{p.Entity}→{p.Generator}"))}");
            Console.WriteLine($"  {map.Ds2Map}: {count} DS1 {chr.Ds1Chr} enemies from the MSB");
        }
        foreach (var s in Skipped.GetValueOrDefault(map.Ds1Map) ?? []) pack.Note($"{map.Ds2Map}: DS1 part {s} → not placed (rule V-ret)");
        if (extra != null)
        {
            // merge stage 5: an extra generator (the boss) must not take an enemy's or an NPC's generator
            var taken = customs.Select(c => c.NewGenerator).ToHashSet();
            var hit = extra.FirstOrDefault(c => taken.Contains(c.NewGenerator));
            if (hit != null) throw new InvalidDataException($"{map.Ds1Map}: extra generator {hit.NewGenerator} (EnemyParam {hit.EnemyId}) is already placed");
            customs.AddRange(extra);
        }
        EnemyTest.Build(pack, map, [], customs);
        var result = placed.Where(p => p.Entity > 0).ToDictionary(p => p.Entity, p => p.Generator);
        foreach (var n in npcs ?? []) result[n.Entity] = n.Custom.NewGenerator;
        foreach (var (e, g) in extraEntities ?? new Dictionary<int, int>()) result[e] = g;
        return result;
    }
}
