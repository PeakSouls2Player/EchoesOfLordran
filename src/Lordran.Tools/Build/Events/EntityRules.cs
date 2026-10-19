namespace Lordran.Tools.Build.Events;

/// <summary>
/// DS1 entity → DS2 id rules for characters, bosses, item lots, texts and hit groups (docs/translation-rules.md §1;
/// session 8). Pure functions, so the map builder, the enemy placer and the event translator agree without sharing state.
///
/// Evidence (vanilla DS2, session 8):
///  - characters are addressed by GeneratorLocation/GeneratorParam id in every event command (131721/131731/140111/
///    140121 …; EDD parameter names 「ジェネレータID」); ids are small per-map numbers (Things Betwixt 15-17, 210-222, 1000,
///    1600, 3000; FoFG 3150; Lost Bastille 816);
///  - BossBattleParam ids are 7-digit (1010000 The Pursuer … 5037030 Aava) and DS1 keys its boss data (GameAreaParam:
///    bonusSoul_single = the boss souls, 2000 for the Asylum Demon 1810800) by the boss character's entity id; the two id
///    sets do not overlap (DS1 1000800..1810810, DS2 1010000..5037030, no equal values);
///  - ItemLotParam2_Other (event rewards 131402, boss lots BossBattleParam.itemLotId) uses ids below 100,000,000;
///  - talk/charaname.fmg holds boss names (BossBattleParam.bossNameTextId1: 1540 Skeleton Lords, 3050 Smelter Demon);
///  - hit groups: MSB2 Collision.UnkT3C = the HitGroupParam row (every map's collision values are a subset of its
///    HitGroupParam row ids: Things Betwixt {0, 10} ⊂ {0, 1, 10}; Majula {0, 2, 3, 4, 50} = rows; m10_18 {0..4, 40, 50});
///    132153(group, 0|1) toggles them (m10_18's ship: groups 3/4) and 140401(slot, group, 0|1) tests the player on them.
/// </summary>
public static class EntityRules
{
    /// <summary>
    /// DS1 character entity → DS2 generator id. Map characters 1AABgnnn (Asylum Hollows 1810101.., the Asylum Demon
    /// 1810800, vagrants 1813900) → entity % 10000 (101, 800, 3900); DS1's global NPC entities (Oscar 6023, Snuggly 6450:
    /// below 10000) keep their id. Unique per map: DS1 map character entities use the 0xxx/3xxx blocks after the map
    /// prefix, NPC entities 6000-6999.
    /// </summary>
    public static int Generator(int ds1Entity) => ds1Entity < 10000 ? ds1Entity : ds1Entity % 10000;

    /// <summary>DS1 boss character entity (= its DS1 GameAreaParam row) → DS2 BossBattleParam row: the same id.</summary>
    public static int BossBattle(int ds1Entity) => ds1Entity;

    /// <summary>Generator event group of a boss (BossBattleParam.genEventGroupId1; vanilla bosses use 10, 20, 30).</summary>
    public const int BossEventGroup = 10;

    /// <summary>
    /// Generator logic group of a 1:1 boss (GeneratorParam.logicGroupId, byte 3; session 9, chrint stage 1, rule L1).
    /// Vanilla (every generatorparam file): of the 19 generators with the boss flag byte 0x48 = 0x6C, 11 have a logic group
    /// no other generator of their map uses (Smelter Demon m10_19 806: 150, the Pursuer m10_10 853: 250, m10_32 824 /
    /// m10_33 852: 150, m20_11 802: 240 ...); the 8 that share one are all in the two multi-boss arenas m20_21 and m50_37
    /// (a partner boss generator or up to 12 other generators). The boss-range generators 8xx without 0x6C in group 150
    /// follow the same layout: alone (m10_25 851, m10_31 842) or shared with partners/adds (m10_16 845/846/847, m10_17 800 +
    /// 3300-3303, m10_18 844 + 5220/5221, m10_34 850 + 9000-9011). 150 is not a boss-only id (non-boss generators use it in
    /// other maps: m10_10 6000/6001, m10_15 1000 ...); what the rule needs is that no other generator of the boss's map
    /// uses it. Our regulars cloned from Things Betwixt's 1600 all keep its 5, the NPCs use 20/25/30/35. A lone 1:1 boss
    /// (no adds, no partner boss) therefore gets its own group: 150 (the Smelter's, the boss template of our Asylum Demon;
    /// free in every map we build, checked by <c>ds1boss-check</c>). What DS2 does with logic groups (AI group calls) is U;
    /// the rule copies vanilla's layout. Survey: chrint <c>logs\merge\vanilla-boss-logic-groups.txt</c> (0x6C) and
    /// <c>logs\fix\vanilla-logic-group-150.txt</c> (group 150 and the 8xx/9xx generators).
    /// </summary>
    public const int BossLogicGroup = 150;

    /// <summary>Session 11 (track ret11), rule B-k (asy10 enemies.md §3): the k-th DS1 boss of a map (its GameAreaParam characters in
    /// entity order: Asylum Demon 0, Stray Demon 1) gets generator event group 10·(k+1) (vanilla 2nd bosses of a map use 20: Last
    /// Giant 1010010, Lost Sinner 1016010 … BossBattleParam.genEventGroupId1, D9) and its own logic group 150 + 10·k (rule L1 per
    /// boss: the Asylum's demons fight alone in different arenas). k = 0 equals the constants above.</summary>
    public static int BossEventGroupOf(int k) => 10 * (k + 1);
    public static int BossLogicGroupOf(int k) => BossLogicGroup + 10 * k;

    /// <summary>DS1 ItemLotParam id (a lot awarded by an event or used for boss rewards) → our ItemLotParam2_Other row.</summary>
    public static int ItemLot(int ds1Lot) => 1_000_000_000 + ds1Lot;

    /// <summary>
    /// One DS1 ItemLotParam row on its own (no chain) → ItemLotParam2_Other row (session 8, talk gifts): DS1 gives a
    /// chain row by row, each gated by its own getItemFlagId (Oscar's key lot 1080 → 1081 Big Pilgrim's Key only after an
    /// early demon kill, 1082 Estus only if not given yet), so a talk gift awards rows one at a time. Separate from
    /// <see cref="ItemLot"/> (the whole chain in one row) so both can exist for the same DS1 lot.
    /// </summary>
    public static int ItemLotRow(int ds1LotRow) => 1_100_000_000 + ds1LotRow;

    /// <summary>DS1 NPC name id (NPC_name_.fmg, boss bar names 2232 Asylum Demon) → talk/charaname.fmg id.</summary>
    public static int CharaName(int ds1NameId) => 90_000_000 + ds1NameId;

    /// <summary>First hit group id for DS1 collisions DS1's events toggle (2011:1); DS2's own groups stay below 100.</summary>
    public const int FirstHitGroup = 100;

    /// <summary>
    /// DS1 map sound events → the map's sound plan (session 10, track asy-sound: <see cref="MapSounds"/>, rules SND1-SND5).
    /// As a dictionary it is the old contract: a sound entity any 2010:3 switches → its MSB2 Sound region key (UnkT08,
    /// the id 131201 / 131202 take), 100 + n in MSB order (DS2's boss BGM regions: m10_23 101 = Skeleton Lords, started by
    /// 131201(101) with the fight). Session 8 keyed only the sounds event 0 disables; a switched sound that starts ON
    /// (Firelink's hub BGM) was unaddressable. Pure, so the region builder and the event translator agree.
    /// </summary>
    public static MapSounds SoundId(SoulsFormats.MSB1 msb1, SoulsFormats.EMEVD emevd, string? ds1Map = null) =>
        MapSounds.Plan(msb1, emevd, ds1Map != null ? MapSounds.DefaultNames(ds1Map) : null); // fix pass: the build's name space (SE placement)

    /// <summary>
    /// Key-guide helper for the k-th distinct prompt (DS1 help message) on a DS1 action-button area (session 8 fix pass:
    /// the nest 1812001 "Curl up like a ball" k 0, "Stand up" k 1): object instance AABB7000 + k·1000 + area % 1000
    /// (18017001 / 18018001; DS1's objects use AABB1nnn, item pickups AABB6nnn, entity-less doors AABB4nnn).
    /// </summary>
    public static int AreaHelper(int ds1Area, int k) =>
        ds1Area / 100000 * 1_000_000 + ds1Area / 10000 % 10 * 10000 + 7000 + k * 1000 + ds1Area % 1000;

    /// <summary>
    /// Session 10 (doors.md K1/K12): the ObjAct helper (invisible "Open" key guide) of a DS1 ObjAct prefab object: object
    /// instance AABB5000 + entity % 1000 (the far door 1811110 → 18015110). AABB5nnn is unused by every other rule (objects
    /// AABB1nnn, entity-less doors AABB4nnn, pickups AABB6nnn, area helpers AABB7nnn-9nnn); the hall door keeps the
    /// session-7 id 18011119 (in-game verified).
    /// </summary>
    public static int ObjActHelper(int ds1Object) => ds1Object / 100000 * 1_000_000 + ds1Object / 10000 % 10 * 10000 + 5000 + ds1Object % 1000;
}
