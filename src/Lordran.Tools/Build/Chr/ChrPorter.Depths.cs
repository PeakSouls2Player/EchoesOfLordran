namespace Lordran.Tools.Build.Chr;

/// <summary>
/// Session 11 (track dchr11): the Depths' (DS1 m10_00) new regulars on the 1:1 rig pipeline (build-pack --map depths --burg-chrs).
/// Off (no --map depths): nothing here is built and every other Options is unchanged.
/// </summary>
public static partial class ChrPorter
{
    /// <summary>Session 11 (dchr11): the Depths' DS1 models no other map ports.</summary>
    public static Options[] DepthsOnly => Burg && Depths ? [.. new[] { Butcher }.OfType<Options>()] : [];

    /// <summary>
    /// DS1's Butcher (c2660 "不死人（コック）", Depths: parts 1000099 (InitAnimID 9000) and 1000090 = NpcParam 266000, 1000905 = 266001 the
    /// black phantom (SpEffect 7100, drawType 2, 150 souls); 480 HP, turnVellocity 180, superArmorDurability 100, resident 5308 / 5360;
    /// think 266000 → 266000_battle "Husi_Chef266000Battle"; behaviorVariationId 26600, animIdOffset 0) on c1030's archive and network:
    /// a standard 55-node biped (Pelvis … Toe0, ForeTwist / CalfTwist, L_weapon / R_weapon) with one body texture set (c2660 _n _s), so
    /// the Hollow Infantry archive as the Undead Assassin / hostile Merchant (no MT1 need for c1020). Display groups (NpcParam
    /// modelDispMask0 = modelDispMask1 = 1 on both rows): #00# / #01# = the cleaver (c2660_WP_A_0252 textures) and the meat hook
    /// (c2660_stick) → WP1_c1030 (culling 20) and WP2_c1030 (21), both drawn on every variant. Ids by rule I-chr: model 2660, interim
    /// template-port model 2669 (DS1 id + 9), anim set c2660 / 266000 (decade 2660 free), EnemyParams 266000 / 266001 (DS2 has no rows).
    /// Start-up: DS1 a00_9000 (the chopping-at-the-table loop, 3.07 s, JT 7 whole length) is the template slot a75_01_7100's clip
    /// (HollowUnarmed) → startUpStateType 2 by rule P3a; its exit a75_01_7101 plays DS1's own cancel clip a00_9060 (EMEVD 11005050:
    /// 2004:9 CancelAnimation 9060 for 1000099 on player within 7 m / hit / object 1001400 broken / 1000110 in combat), not 1771.
    /// </summary>
    public static Options? Butcher => new("c2660", "c2660", "c1030", 2669,
        BodyMaterial: "BD_LG", EdgeMaterial: "BD_LG_1bit_2side", WeaponMaterials: ButcherWeapons, KeepTemplateMeshes: [],
        Variants:
        [
            new(266000, 103010, 266000, 103010, 103000, "cleaver & meat hook (Depths)"),
            new(266001, 103010, 266001, 103010, 103000, "cleaver & meat hook (Depths black phantom: SpEffect 7100)"),
        ])
    {
        Rig = new OneToOne(2660, [266000, 266001], ButcherRoles, 103000)
        {
            AnimId = 266000,
            IdleFallback = true,
            WeaponMeshes = ButcherWeapons,
            Attacks = new Ds1Attacks.Plan(0, 26600, 0,
            [
                new(3000, "cleaver downswing 1 (包丁振り下ろし１)", 3.2f, "266000_battle act2 (Att3000 0-3.2), Interupt GuardBreak / UseItem / Shoot_2dist near; JT 23 1.1-3.17; 226600100"),
                new(3001, "cleaver downswing 2 (包丁振り下ろし２)", 0, "act2 ComboRepeat / ComboFinal; JT 23 1.33-2.67; 226600110"),
                new(3002, "trot and downswing (小走りして振り下ろす)", 7.5f, "act7 defAct07 (Att3002 5.5-7.5); Interupt UseItem / Shoot_2dist far; 226600120"),
                new(3003, "charged downswing (溜めて振り下ろす)", 3.3f, "act3 defAct03 (Att3003 0-3.3); 226600130"),
                new(3004, "cleaver flailing (包丁を振り回す)", 2.6f, "act2 ComboFinal (50 %); Interupt GuardBreak_Act (dist 2.6) single Attack; 226600140"),
                new(3005, "hook grab attempt (フックで捕獲しようとする)", 0.8f, "act4 Approach_and_Attack_Act (Att3005 0-0.8); 226600150"),
                new(3006, "charged downswing, combo (溜めて振り下ろす・コンボ)", 0, "act2 ComboFinal (20 %); 226600160"),
                new(3007, "two-handed smash down (両手で叩き下ろす)", 0.8f, "act1 defAct01 (Att3007 0-0.8); 226600170"),
                .. Steps(701),
            ],
            [
                new([3000, 3001], "act2 fate 11-30: ComboAttack 3000 → ComboFinal 3001"),
                new([3000, 3001, 3004], "act2 fate 31-80: 3000 → ComboRepeat 3001 → ComboFinal 3004"),
                new([3000, 3001, 3006], "act2 fate 81-100: 3000 → ComboRepeat 3001 → ComboFinal 3006"),
            ],
            [new(10, 3000, "ai103000 action 10"), new(11, 3002, "ai103000 action 11"), new(12, 3003, "ai103000 action 12")]),
            Armed =
            [
                new(266000, 266000, [20, 21], 103010, "cleaver & meat hook"),
                new(266001, 266001, [20, 21], 103010, "cleaver & meat hook (black phantom)"),
            ],
        },
    };

    static Dictionary<string, string> ButcherWeapons => new() { ["#00#"] = "WP1_c1030", ["#01#"] = "WP2_c1030" };

    /// <summary>The Hollow role table with the start-up exit a75_01_7101 on DS1's own cancel clip a00_9060 (the 9000 pose's exit,
    /// EMEVD 11005050); c2660 has no a00_1771.</summary>
    static Ds1RigAnims.Role[] ButcherRoles =>
    [
        .. Ds1RigAnims.HollowUnarmed.Select(r => r.Slot == "a75_01_7101"
            ? r with { Ds1 = [9060, 1770], Name = "wake from the chopping pose", Evidence = "DS1 a00_9060 (1.4 s): EMEVD 11005050 2004:9 CancelAnimation 9060 for part 1000099 (InitAnimID 9000)" }
            : r),
    ];
}
