using System.Numerics;
using Lordran.Tools.Formats;
using Lordran.Tools.Formats.Havok;
using Lordran.Tools.Textures;
using SoulsFormats;
// merge stage 5: the boss track's network / TAE code lives in its own namespaces (the enemies track has
// classes of the same names in Formats.Morpheme / Formats); see MERGE-NOTES.md "Unification backlog"
using Ds2TaeFile = Lordran.Tools.Formats.Boss.Ds2TaeFile;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// A 1:1 DS1 boss as a DS2 character (session 8, track boss, step 1; user directive: DS1 enemies and bosses 1:1 - DS1
/// skeleton, animations, attacks, timings; DS1 content, DS2 rules). First user: the Asylum Demon (DS1 c2232, NpcParam
/// 223200, 145-bone rig) → DS2 model c2232, animId 221010 (merge9 stage 6; the boss track had 223210), EnemyParam 223200.
/// <para><b>Files</b> (EnemyParam modelId → model/chr, animId → anibnd c{animId/100}, timeact c{animId}, sound
/// frpg2_c{animId/100 rounded down to tens}; rule C15 as corrected by track chrsound, S1): model/chr/c2232.bnd (the template
/// boss's archive layout: DS1 FLVER on DS1's bones, DS1 ragdoll, flvpwv, ACB with our textures; the template's "_h"
/// hit-body files are left out, see below), c2232.texbnd (DS1 textures, names c2232_*), morpheme4/chr/c2210.anibnd.dcx
/// (<see cref="Ds1BossAnims"/>; its NMB c2210.nmb), the c0001 extanibnd (a copy of the template's, if it has one; the
/// Smelter has none), timeact/chr/c221010_pl|sfx|snd.tae (<see cref="Ds2TaeFile"/>, rebuilt) and the sound bank
/// sound/frpg2_c2210.bnd = DS1's own FMOD project frpg_c2232 + wave bank (<see cref="Ds1ChrSound.Bank"/>, rules S1-S10) with
/// DS1's wave banks loose in title/sound (<see cref="Result.TitleFiles"/>).</para>
/// <para><b>animId (merge9 stage 6)</b>: DS2 opens a character's sound bank by the decade of its animId (animId/100 rounded
/// down to tens, chrsound rule S1, run s8-6), so the boss track's 223210 opened DS2's own frpg2_c2230.bnd (DS2's c2230
/// sounds, none of DS1's). The demon's anim set therefore sits in a free decade (<see cref="Ds1ChrSound.FreeDecades"/>: no
/// DS2 bank, model, anibnd or TimeAct; 2210 is the nearest; DS1's own c2210 exists only in DSR's m99 test maps, so no DS1
/// character the migration ports wants 2210). The model stays c2232 (DS2 precedent for a model outside its anim set's
/// decade: EnemyParam 689010 Charred Loyce Knight, model 6890, animId 688020 → c6880.anibnd / c688020_*.tae /
/// frpg2_c6880.bnd). Ids that do NOT follow the animId: EnemyParam 223200, BehaviorId 223210 (EnemyBehavior*/Combo rows,
/// reached through EnemyEquipWeaponParam 22321000.behaviorId, decomp FUN_1403a8fb0; vanilla Royal Rat Vanguard 226100 has
/// animId 226000 and behaviour rows 226100), the logic ids 223211/223201 (GeneratorRegistParam → EnemyDefaultLogicParam),
/// the EnemyDamageParam rows 222321000-222322601 (DS1 BehaviorParam space), the boss bar name 2232 (an FMG id).</para>
/// <para><b>Damage</b> (rule B4): DS1 TAE "Attack Behavior" (type 1, judge J) → BehaviorParam 2_VVVVV_JJJ → AtkParam_Npc;
/// each DS1 hit group (identical windows) is one DS2 damage slot: TAE event 2200 with ID = the slot (DS2's own convention:
/// Smelter combo stages carry 2200 ID 0/1/2 = AttackId01/02/03; actions DamageId00..02); per AtkParam hit sphere/capsule
/// (hit0..3, radius > 0) one EnemyDamageParam row, chained by childDamage (simultaneous shapes of one hit, DS2's form:
/// 130500110 → 130500111), damageGroup = the slot's first row. Hit shapes: DS1 capsules run between two dummies
/// (DmyPoly1 → DmyPoly2), DS2's from one dummy along its forward axis (hitModelType 1, length; Smelter 130500110: dummy 100
/// forward −Y, length 2.5 = its sword blade), so each DS1 capsule gets a dummy of its own at DmyPoly1 facing DmyPoly2
/// (ids from 150 up, free in the DS1 model). Values (P, calibrated on the Hollows: DS1 c2500 sword atkPhys 100 / atkStam 20 /
/// knockback 0.45 vs DS2 Hollow Infantry 108 / 215 / 1.25): damage01 = atkPhys, staminaDamage = atkStam × 10, knockBackPower
/// = knockbackDist × 2.78; the rest from the template boss's row.</para>
/// <para><b>AI</b>: step 1 keeps the template boss's logic (interim); step 2 translates DS1's 223200_battle.lua. To give the
/// interim AI a DS1 moveset, combo rows 0-2 are DS1's chains (rule B2, permanent) and rows 3-6 single DS1 attacks
/// (interim).</para>
/// </summary>
public static partial class Ds1Boss   // session 11 (dchr11): partial, the Depths' specs live in Ds1Boss.Depths*.cs
{
    public sealed record Spec(string Ds1Chr, string TemplateChr, int TemplateEnemyParam, string MaterialTemplateChr, string BodyMaterial, string EdgeMaterial,
        int Model, int AnimId, int EnemyParam, int Ds1Npc, int BehaviorId, int WeaponParam, int TemplateWeaponParam, int HighLogic, int LowLogic,
        Ds1BossAnims.Spec Anim, Dictionary<int, float> AttackRange, int[] InterimCombos, int Ds1Entity)
    {
        /// <summary>DS1 character holding the body textures (NpcParam.normalChangeTexChrId: c2232 → 2239, c2239.chrtpfbdt).</summary>
        public string Ds1TexChr { get; init; } = Ds1Chr;
        public string N => $"c{Model:D4}";
        /// <summary>Step 2: DS1's own AI translated (<see cref="Ai.Boss.AiPorter"/>); HighLogic/LowLogic are its upper/lower ids.</summary>
        public Ai.Boss.AiPorter.Spec? Ai { get; init; }
        /// <summary>DS1 map of the boss's MSB part and event script (regions, home point).</summary>
        public string Ds1Map { get; init; } = "m18_01_00_00";
        /// <summary>Session 11 (burgchr11): a large regular on the boss pipeline (rule LR1: a DS1 character without a GameAreaParam row,
        /// the Titanite Demon): no boss generator / BossBattleParam / B-k index; the EnemyParam pays DS1's getSoul and drops DS1's item
        /// lots (as the regulars, Ds1Drops).</summary>
        public bool LargeRegular { get; init; }
        /// <summary>Session 11 (burgchr11): further DS1 NpcParams on the same body and anim set (the Capra Demon's dogs 334003 next to
        /// the Lower Burg's 334000): each an EnemyParam clone with its own souls and drops (rule LR1).</summary>
        public (int Npc, int EnemyParam)[] ExtraVariants { get; init; } = [];
        /// <summary>Session 11 (burgchr11): DS1 display groups ("#00#" …) kept on the model → the material template's material whose
        /// GX culling id shows them (as the regulars' WeaponMeshes; empty = every "#NN#" mesh left out, the demons' case), the culling
        /// ids the main EnemyParam's weapon slot shows (DS1 NpcParam.modelDispMask) and those of each extra variant (by NpcParam).</summary>
        public Dictionary<string, string> WeaponMeshes { get; init; } = [];
        public int[] Culling { get; init; } = [];
        public Dictionary<int, int[]> ExtraCulling { get; init; } = [];
        /// <summary>Session 11 (burgchr11): the template's TimeAct set (c{id}_pl/_sfx/_snd) when it is not the template EnemyParam's id
        /// (the Giant Undead Boar: EnemyParam 214003, TAE c214000).</summary>
        public int TemplateTae { get; init; }
    }

    /// <summary>
    /// DS1's Asylum Demon (c2232) on the Smelter Demon's network (c3050: biped boss, one right-hand weapon, ground slams;
    /// NETWORK-FORMAT.md §6.1). Attack ranges = the DS1 AI's attack distance maxima (223200_battle.lua locals, DS1-AI-NOTES):
    /// 3000 9.6, 3001 8, 3002 6.6, 3003 6.4, 3004 1.7, 3005 1, 3006 1.5, 3007 11.9, 3008 0 (−3..0), 3014 8. Chains from the
    /// Lua: Act05 3000→3010 (連続A), Act02 3002→3011→3012 (連続B), Act11 3003→3013 (連続C). DS1 MSB part c2232_0000 =
    /// entity 1810800.
    /// </summary>
    public static Spec AsylumDemon => new("c2232", "c3050", 305000, "c1030", "BD_LG", "BD_LG_1bit_2side",
        // merge9 stage 6: animId 221010 (decade 2210, free in DS2) instead of the boss track's 223210 (decade 2230 = DS2's own
        // frpg2_c2230.bnd); BehaviorId stays 223210 (an independent param id, see the class comment)
        Model: 2232, AnimId: 221010, EnemyParam: 223200, Ds1Npc: 223200, BehaviorId: 223210, WeaponParam: 22321000, TemplateWeaponParam: 30500000,
        HighLogic: 223211, LowLogic: 223201,
        Anim: new Ds1BossAnims.Spec("c2232", "c3050", "c2232",
            Singles: [3000, 3001, 3002, 3003, 3004, 3005, 3006, 3007, 3008, 3009, 3010, 3011, 3012, 3013, 3014, 3020, 3021, 3022, 3023],
            Chains: [[3000, 3010], [3002, 3011, 3012], [3003, 3013]],
            Roles: AsylumDemonRoles,
            NoReaction: ["a00_16_00", "a00_16_01", "a00_16_02", "a00_16_06", "a00_16_07", "a00_16_10", "a00_16_11", "a00_16_12", "a00_16_8000", "a00_09_"]),
        AttackRange: new() { [3000] = 9.6f, [3001] = 8f, [3002] = 6.6f, [3003] = 6.4f, [3004] = 1.7f, [3005] = 1f, [3006] = 1.5f, [3007] = 11.9f, [3008] = 0.5f, [3014] = 8f, [3010] = 9.6f, [3011] = 6.6f, [3012] = 6.6f, [3013] = 6.4f },
        InterimCombos: [],
        Ds1Entity: 1810800)
    {
        Ds1TexChr = "c2239",
        // step 2: DS1 NpcThinkParam 223200 → battle goal 223200 (m18_01's 223200_battle.lua); the Smelter's upper logic and
        // library machines as the DS2 frame (template logic 305011 / 305001)
        Ai = new Ai.Boss.AiPorter.Spec("m18_01", BattleGoal: 223200, ThinkParam: 223200, TemplateUpper: 305011, TemplateLower: 305001, Upper: 223211, Lower: 223201),
    };

    const string Mv = "DS1 MoveParam 100 (NpcParam 223200 moveAnimId 100): walkF 200, walkB 201, walkL 202, walkR 203, dashF 500, turnL 602, turnR 603, largeTurnL 604, largeTurnR 605 (c2232 has no 501-503)";

    /// <summary>
    /// c3050 (Smelter Demon) slots → DS1 c2232 clips. DS1 c2232 has: 0 idle, 200-203 walk F/B/L/R, 500 run, 602-605 turns, 701
    /// back step (the only step: SpinStep 701 in the Lua), 1500 airborne, 1750/1751 landing, 2200 death, 2250 dead pose, 2520
    /// plunge-victim reaction (5.8 s, JT 67 invincible) / 2521 its death / 2522 pose, 3000-3014 + 3020-3023 attacks (SM_Attack,
    /// placed by leaf, <see cref="Ds1BossAnims"/>), 3500 rebound, 9000 waiting in the air, 9060 drop/landing (the intro).
    /// Damage reactions: DS1's demon has none (a boss that never flinches), so DS2's damage slots are "no reaction"
    /// (<see cref="Ds1BossAnims.Spec.NoReaction"/>). Walk-state slots get the loops' touchdown count as sync events (-1).
    /// </summary>
    public static readonly Ds1RigAnims.Role[] AsylumDemonRoles =
    [
        new("a00_00_0000", [0], "idle", "V", "DS1 idle a00_0000 (MoveParam stayId 0)"),
        new("a00_00_7001", [0], "idle (weapon style 1)", "V", "one DS1 idle"),
        new("a00_00_4000", [2250, 0], "ragdoll loop pose", "P", "DS1 a00_2250: dead pose (TAE 193 ragdoll)"),
        new("a00_01_0010", [0], "walk-state idle", "P", "no DS1 walk-stay clip → idle", SyncEvents: -1),
        new("a00_01_7011", [0], "walk-state idle", "P", "as a00_01_0010", SyncEvents: -1),
        new("a00_01_0011", [603], "turn right 90", "V", Mv, SyncEvents: -1),
        new("a00_01_0012", [605], "turn right 180", "V", Mv, SyncEvents: -1),
        new("a00_03_0010", [200], "walk forward", "V", Mv, Loop: true),
        new("a00_03_0011", [200], "walk (banking variant)", "P", "DS2 banking walk → DS1 walk", Loop: true),
        new("a00_03_7001", [200], "walk forward", "V", Mv, Loop: true),
        new("a00_03_0040", [0], "walk to idle", "P", "no DS1 stop clip → idle"),
        new("a00_03_7041", [0], "walk to idle", "P", "as a00_03_0040"),
        new("a00_03_0071", [605], "walk turn 180 (right)", "V", Mv),
        new("a00_03_7071", [604], "walk turn 180 (left)", "V", Mv),
        new("a00_04_0010", [500], "run forward", "V", Mv, Loop: true),
        new("a00_04_0011", [500], "run (banking variant)", "P", "DS2 banking jog → DS1 run", Loop: true),
        new("a00_04_7001", [500], "run forward", "V", Mv, Loop: true),
        new("a00_04_0071", [605], "run turn (right)", "P", "no DS1 run turn → largeTurnR"),
        new("a00_04_7071", [602], "run turn (left)", "P", "→ turnL"),
        new("a00_06_0011", [200], "strafe-set walk forward", "V", Mv, Loop: true),
        new("a00_06_0012", [201], "walk back", "V", Mv, Loop: true),
        new("a00_06_0013", [202], "walk left", "V", Mv, Loop: true),
        new("a00_06_0014", [203], "walk right", "V", Mv, Loop: true),
        new("a00_06_7011", [200], "walk forward", "V", Mv, Loop: true), new("a00_06_7012", [201], "walk back", "V", Mv, Loop: true),
        new("a00_06_7013", [202], "walk left", "V", Mv, Loop: true), new("a00_06_7014", [203], "walk right", "V", Mv, Loop: true),
        new("a00_07_0011", [500], "run forward (strafe set)", "V", Mv, Loop: true),
        new("a00_07_0012", [501, 201], "run back", "P", Mv, Loop: true),
        new("a00_07_0013", [502, 202], "run left", "P", Mv, Loop: true),
        new("a00_07_0014", [503, 203], "run right", "P", Mv, Loop: true),
        new("a00_07_7011", [500], "run forward", "V", Mv, Loop: true), new("a00_07_7012", [501, 201], "run back", "P", Mv, Loop: true),
        new("a00_07_7013", [502, 202], "run left", "P", Mv, Loop: true), new("a00_07_7014", [503, 203], "run right", "P", Mv, Loop: true),
        new("a00_09_", [0], "guard (no DS1 guard)", "P", "DS1's demon never guards → no reaction"),
        new("a00_11_", [701], "step", "P", "DS1 c2232 has one step, a00_0701 (back step; the Lua's SpinStep 701): every DS2 step direction"),
        new("a00_13_0020", [1500], "jump takeoff / airborne", "P", "DS1 a00_1500: airborne loop"),
        new("a00_13_0021", [1500], "stand jump takeoff", "P", "as a00_13_0020"),
        new("a00_13_0040", [1750], "landing", "P", "DS1 a00_1750: landing; DS2 JumpAttack slot: no attack", NoAttack: true),
        new("a00_14_0020", [1751, 1750], "heavy landing", "P", "DS1 a00_1751 (= 1750)"),
        new("a00_16_7001", [3500, 0], "attack rebound (hit back)", "P", "DS1 a00_3500: rebound, TAE JT 41 vulnerable"),
        new("a00_16_", [0], "damage reaction → none", "P", "DS1's Asylum Demon has no hit reaction clips (boss): idle, no reaction"),
        new("a00_17_", [2200], "death", "V", "DS1 a00_2200 TAE JT 12 'Death State' at 3.967 s"),
        new("a00_50_", [-1], "additive (lean / tire / turn)", "P", "bind pose = no additive change (rule as the Hollow)"),
        new("a65_", [0], "attack slot outside the plan", "P", "unreachable after rule B1 (slot 1+) or a stage without a DS1 clip → idle, no attack", NoAttack: true),
        new("a75_01_7100", [9000, 0], "dormant loop (start-up type 2)", "P", "DS1 a00_9000: waiting in the air (event 11810310 at 1812305, y 226)"),
        new("a75_01_7101", [9060, 0], "wake (start-up type 2)", "P", "DS1 a00_9060: the drop into the hall, landing"),
        new("a75_01_7110", [9000, 0], "dormant loop (start-up type 3)", "P", "as a75_01_7100"),
        new("a75_01_7111", [9060, 0], "wake (start-up type 3)", "P", "as a75_01_7101"),
        new("a79_", [2520, 0], "throw victim", "P", "DS1 a00_2520: the demon's victim reaction (plunge), JT 67 invincible"),
    ];

    /// <summary>
    /// Session 11 (track ret11): DS1's Stray Demon (c2230, NpcParam 223000: HP 2100, think 223001 → battle goal 223000
    /// GreaterDemon223000Battle; entity 1810810 in the pit under the Asylum hall, return trip) on the Asylum Demon's pipeline:
    /// the same 142-bone skeleton names/order as c2232 (own bind pose), the Smelter's network c3050 resized to it. Ids by rule
    /// I-chr (asy10 enemies.md §3): DS2 owns EnemyParam 223000 and model c2230 (Mongrel Rat) → EnemyParam 1,223,000 (= 1,000,000
    /// + DS1 id), model 2190 (first decade free in DS2 and DSR: D14), animId 219010 (decade 2190 = its own sound bank), behaviour
    /// 1,223,010, weapon slot 122,301,000, logic 223011 / 223001 (free: DS2 has ai223000 / ai223010); textures c2231
    /// (NpcParam normalChangeTexChrId). DS1 clips: 24 of its own (idle, walks, run, turns, 701, 1500, 1750, 2200, 2250,
    /// 3000-3007, 3500): the Asylum Demon's role table resolves every slot (2520-2522 / 9000 / 9060 fall back to idle by the
    /// table's own fallbacks: the Stray Demon has no intro and no plunge reaction; it starts dormant in its idle). Attacks and
    /// ranges from 223000_battle.lua (Att* maxima: 3000 12, 3002 12, 3004 19, 3005 2, 3006 10, 3007 5; chains act2
    /// defAct02 {12, 0, 20, 80} = 3000 → 3001 (80 %), act1 3002 → 3003 (90 %)). k = 1 among the Asylum's GameAreaParam
    /// bosses (rule B-k: event group 20, logic group 160, logic flag 101020081).
    /// </summary>
    public static Spec StrayDemon => new("c2230", "c3050", 305000, "c1030", "BD_LG", "BD_LG_1bit_2side",
        Model: 2190, AnimId: 219010, EnemyParam: 1_223_000, Ds1Npc: 223000, BehaviorId: 1_223_010, WeaponParam: 122_301_000, TemplateWeaponParam: 30500000,
        HighLogic: 223011, LowLogic: 223001,
        Anim: new Ds1BossAnims.Spec("c2230", "c3050", "c2190",
            Singles: [3000, 3001, 3002, 3003, 3004, 3005, 3006, 3007],
            Chains: [[3000, 3001], [3002, 3003]],
            Roles: AsylumDemonRoles,
            NoReaction: ["a00_16_00", "a00_16_01", "a00_16_02", "a00_16_06", "a00_16_07", "a00_16_10", "a00_16_11", "a00_16_12", "a00_16_8000", "a00_09_"]),
        AttackRange: new() { [3000] = 12f, [3001] = 12f, [3002] = 12f, [3003] = 12f, [3004] = 19f, [3005] = 2f, [3006] = 10f, [3007] = 5f },
        InterimCombos: [],
        Ds1Entity: 1810810)
    {
        Ds1TexChr = "c2231",
        Ai = new Ai.Boss.AiPorter.Spec("m18_01", BattleGoal: 223000, ThinkParam: 223001, TemplateUpper: 305011, TemplateLower: 305001, Upper: 223011, Lower: 223001),
    };

    // ================================================================== session 11 (track burgchr11): the Burg's (DS1 m10_01) bosses
    // Specs from research r3 (work/research/r3: Lua, TAE, NpcParam / SpEffect / GameAreaParam, resize runs). Ids by rule I-chr, checked
    // free in DS2 (incl. the EnemyParam-keyed config params: DS2 has EnemyCommonParam / EnemyMoveParam 225000, so the Taurus Demon takes
    // 1,225,000 too). B-k order of m10_01's GameAreaParam bosses: Taurus 1010700 k 0, Capra 1010750 k 1, Gargoyles 1010800 k 2. The
    // Titanite Demon has no GameAreaParam row: a large regular (rule LR1). Not placed here (M3).

    
    static Ds1RigAnims.Role[] With(Ds1RigAnims.Role[] b, params Ds1RigAnims.Role[] o) => [.. b.Where(r => o.All(x => x.Slot != r.Slot)), .. o];
    
    // ---------------------------------------------------------------- Taurus Demon (k = 0: event group 10, logic group 150, flag 141020080)
    public static readonly Ds1RigAnims.Role[] TaurusDemonRoles = With(AsylumDemonRoles,
        new("a00_16_", [2000], "damage reaction (poise break)", "P", "DS1 a00_2000, its only reaction: resident SpEffect 5329 maps every damage level to small, 5360 suppresses it while super armor (120) holds"),
        new("a75_01_7100", [9001, 0], "dormant loop (start-up type 2)", "P", "DS1 a00_9001: perched, gravity + map hit off (event 11015382: 2004:9 WaitingAnimation 9001)"),
        new("a75_01_7101", [9061, 0], "wake (start-up type 2)", "P", "DS1 a00_9061: the leap down onto the wall-walk (11015382: 2003:18 9061 on player damage or host in 1012701)"),
        new("a75_01_7110", [9000, 0], "dormant loop (start-up type 3)", "P", "DS1 a00_9000 (no DS1 event uses it)"),
        new("a75_01_7111", [9060, 0], "wake (start-up type 3)", "P", "DS1 a00_9060: landing, hit j200 'special wait release' (no DS1 event uses it)"));
    
    public static Spec TaurusDemon => new("c2250", "c3050", 305000, "c1030", "BD_LG", "BD_LG_1bit_2side",
        Model: 2180, AnimId: 218010, EnemyParam: 1_225_000, Ds1Npc: 225000, BehaviorId: 1_225_010, WeaponParam: 122_501_000, TemplateWeaponParam: 30500000,
        HighLogic: 225011, LowLogic: 225001,
        Anim: new Ds1BossAnims.Spec("c2250", "c3050", "c2180",
            Singles: [3000, 3001, 3002, 3003, 3004, 3005, 3006, 3007, 3008, 3009, 3010, 3011, 3012, 3013],
            Chains: [[3002, 3003], [3003, 3002]],
            Roles: TaurusDemonRoles,
            NoReaction: ["a00_16_06", "a00_16_07", "a00_16_10", "a00_16_11", "a00_09_"]),
        AttackRange: new() { [3000] = 3f, [3001] = 2f, [3002] = 4f, [3003] = 2.9f, [3004] = 8.5f, [3005] = 3f, [3006] = 1.1f,
            [3008] = 99f, [3009] = 99f, [3010] = 99f, [3011] = 99f, [3012] = 99f },   // 3008-3012: event-point acts (POINT_EVENT), P
        InterimCombos: [],
        Ds1Entity: 1010700)
    {
        Ds1Map = "m10_01_00_00",
        Ai = new Ai.Boss.AiPorter.Spec("m10_01_00_00", BattleGoal: 225000, ThinkParam: 225000, TemplateUpper: 305011, TemplateLower: 305001, Upper: 225011, Lower: 225001),
    };
    
    // ---------------------------------------------------------------- Capra Demon (k = 1: event group 20, logic group 160, flag 141020081)
    // shared by c2240 / c2300: resident SpEffect 5333 maps every damage level to medium (a00_2004-2007, 4 directions),
    // 5360 suppresses reactions while super armor holds (Capra 110, Titanite 200). Direction order F/B/L/R = 2004/2005/2006/2007 is P
    // (DS1 4-direction convention; verify by head lean as the Hollow table did - clip-survey crashes on these rigs).
    public static readonly Ds1RigAnims.Role[] MediumDamageRoles =
    [
        new("a00_16_0025", [2004], "light hit, front", "P", "DS1 medium F"), new("a00_16_0016", [2005], "light hit, behind", "P", "DS1 medium B"),
        new("a00_16_0015", [2007], "light hit, leans right", "P", "DS1 medium R"),
        new("a00_16_0055", [2004], "heavy, front", "P", "medium F"), new("a00_16_0065", [2004], "heavy, front", "P", "medium F"), new("a00_16_0056", [2005], "heavy, behind", "P", "medium B"),
        new("a00_16_0115", [2004], "extra-heavy, front", "P", "medium F"), new("a00_16_0125", [2004], "extra-heavy, front", "P", "medium F"), new("a00_16_0116", [2005], "extra-heavy, behind", "P", "medium B"),
        new("a00_16_0155", [2004], "knock-back", "P", "medium F"), new("a00_16_0165", [2004], "knock-back", "P", "medium F"), new("a00_16_0156", [2005], "knock-back, behind", "P", "medium B"),
        new("a00_16_0210", [2004], "blow-away (face down)", "P", "5333: every level -> medium"), new("a00_16_0220", [2004], "blow-away (face up)", "P", "as 0210"),
        new("a00_16_1230", [2005], "stagger, behind", "P", "medium B"), new("a00_16_8000", [2004], "boss hit", "P", "medium F"),
    ];
    
    public static readonly Ds1RigAnims.Role[] CapraDemonRoles = With(With(AsylumDemonRoles, MediumDamageRoles),
        new("a00_11_0011", [701], "step back (short)", "V", "DS1 SpinStep 701 AI_DIR_TYPE_B"), new("a00_11_0031", [701], "step back (long)", "V", "701"),
        new("a00_11_0012", [703], "step right (short)", "V", "DS1 SpinStep 703 AI_DIR_TYPE_R"), new("a00_11_0032", [703], "step right (long)", "V", "703"),
        new("a00_11_0022", [702], "step left (short)", "V", "DS1 SpinStep 702 AI_DIR_TYPE_L"), new("a00_11_0042", [702], "step left (long)", "V", "702"),
        new("a00_11_0010", [701], "step front-right", "P", "no DS1 forward step -> 701"), new("a00_11_0030", [701], "step front-right (long)", "P", "as 0010"),
        new("a00_14_0020", [1760, 1750], "heavy landing", "P", "DS1 a00_1760 heavy landing (2.67 s, JT41)"),
        new("a00_17_0020", [2202, 2200], "death 2", "P", "DS1 a00_2202 (JT12 1.83 s)"));
    
    public static Spec CapraDemon => new("c2240", "c3050", 305000, "c1030", "BD_LG", "BD_LG_1bit_2side",
        Model: 2150, AnimId: 215010, EnemyParam: 1_224_000, Ds1Npc: 224000, BehaviorId: 1_224_010, WeaponParam: 122_401_000, TemplateWeaponParam: 30500000,
        HighLogic: 224011, LowLogic: 224001,
        Anim: new Ds1BossAnims.Spec("c2240", "c3050", "c2150",
            Singles: [3000, 3001, 3002, 3003, 3004, 3005],
            Chains: [[3000, 3001], [3003, 3005]],
            Roles: CapraDemonRoles,
            NoReaction: ["a00_16_06", "a00_16_07", "a00_16_10", "a00_16_11", "a00_09_"]),
        AttackRange: new() { [3000] = 3.5f, [3001] = 3.5f, [3002] = 3.5f, [3003] = 3.5f, [3004] = 6.5f, [3005] = 3f },
        InterimCombos: [],
        Ds1Entity: 1010750)
    {
        Ds1Map = "m10_01_00_00",
        Ai = new Ai.Boss.AiPorter.Spec("m10_01_00_00", BattleGoal: 224000, ThinkParam: 224001, TemplateUpper: 305011, TemplateLower: 305001, Upper: 224011, Lower: 224001),
    };
    
    // ---------------------------------------------------------------- Titanite Demon (large regular: no GameAreaParam row -> NOT a B-k boss)
    public static readonly Ds1RigAnims.Role[] TitaniteDemonRoles = With(With(AsylumDemonRoles, MediumDamageRoles),
        new("a00_04_0010", [500, 200], "run forward", "P", "c2300 has no run clip -> walk"), new("a00_04_0011", [500, 200], "run (banking)", "P", "as 0010"),
        new("a00_04_7001", [500, 200], "run forward", "P", "as 0010"),
        new("a00_07_0011", [500, 200], "run forward (strafe set)", "P", "as 0010"), new("a00_07_7011", [500, 200], "run forward", "P", "as 0010"),
        new("a00_17_0020", [2202, 2200], "death 2", "P", "DS1 a00_2202 (JT12 2.33 s)"),
        new("a75_01_7100", [0], "dormant loop", "P", "no DS1 intro (a00_9000 is a 0.2 s pose; no event uses it)"), new("a75_01_7101", [0], "wake", "P", "no intro"),
        new("a75_01_7110", [0], "dormant loop", "P", "no intro"), new("a75_01_7111", [0], "wake", "P", "no intro"));
    
    public static Spec TitaniteDemon => new("c2300", "c3050", 305000, "c1030", "BD_LG", "BD_LG_1bit_2side",
        Model: 2300, AnimId: 230010, EnemyParam: 230001, Ds1Npc: 230001, BehaviorId: 230011, WeaponParam: 23_001_100, TemplateWeaponParam: 30500000,
        HighLogic: 230012, LowLogic: 230002,
        Anim: new Ds1BossAnims.Spec("c2300", "c3050", "c2300",
            Singles: [3000, 3001, 3002, 3003, 3004, 3005, 3006, 3007, 3008, 3009],
            Chains: [[3000, 3001, 3002], [3003, 3004, 3005]],
            Roles: TitaniteDemonRoles,
            NoReaction: ["a00_16_06", "a00_16_07", "a00_16_10", "a00_16_11", "a00_09_"]),
        AttackRange: new() { [3000] = 3.8f, [3001] = 3.8f, [3002] = 4f, [3003] = 8f, [3004] = 8f, [3005] = 7.5f, [3006] = 0.5f, [3007] = 8.7f, [3008] = 13.5f, [3009] = 25f },
        InterimCombos: [],
        Ds1Entity: 1010340)
    {
        Ds1Map = "m10_01_00_00",
        LargeRegular = true,
        Ai = new Ai.Boss.AiPorter.Spec("m10_01_00_00", BattleGoal: 230000, ThinkParam: 230003, TemplateUpper: 305011, TemplateLower: 305001, Upper: 230012, Lower: 230002),
    };

    // ---------------------------------------------------------------- Undead Attack Dog (c3340: Lower Burg 334000, the Capra Demon's dogs 334003)
    /// <summary>
    /// Session 11 (burgchr11): DS1's Undead Attack Dog on the boss pipeline as a large regular (rule LR1; quadruped: DS2's Stray Dog c2270
    /// network, whose skeleton holds every one of DS1 c3340's 58 bones by name, resized to it; foot plants on the hind paws, rule
    /// C11q). NpcParam 334000 (84 HP, 80 souls; DS2 owns EnemyParam 334000 / model c3340 → EnemyParam 1,334,000, model 2070 (decade
    /// free in DS2 and DSR), animId 207010) and 334003 (the Capra Demon's dogs 1010500 / 501: 150 souls, no respawn → EnemyParam
    /// 334003, same body). Battle goal 334000 "blackDog": act1 3006 / 3000 (25 % single, 75 % → ComboFinal 3004), act2 3002 / 3001
    /// (→ 3005), act3 side step + 3002, act4 steps; ranges Att3000 1.1, 3001 3.5, 3002 3.6, 3006 2.3. Hit reactions: DS1's own
    /// (2000-2040; the slot ↔ clip choice P), no resident damage-level remap.
    /// </summary>
    public static readonly Ds1RigAnims.Role[] DogRoles = With(AsylumDemonRoles,
        new("a00_16_0025", [2000], "light hit, front", "P", "DS1 a00_2000 (small)"), new("a00_16_0015", [2002, 2000], "light hit, side", "P", "DS1 a00_2002"),
        new("a00_16_0016", [2001, 2000], "light hit, behind", "P", "DS1 a00_2001"),
        new("a00_16_0055", [2010], "heavy hit", "P", "DS1 a00_2010 (medium)"), new("a00_16_0065", [2010], "heavy hit", "P", "as 0055"), new("a00_16_0056", [2011, 2010], "heavy, behind", "P", "DS1 a00_2011"),
        new("a00_16_0115", [2012, 2010], "extra-heavy", "P", "DS1 a00_2012"), new("a00_16_0125", [2012, 2010], "extra-heavy", "P", "as 0115"), new("a00_16_0116", [2013, 2011], "extra-heavy, behind", "P", "DS1 a00_2013"),
        new("a00_16_0155", [2020, 2014], "knock-back", "P", "DS1 a00_2020"), new("a00_16_0165", [2020, 2014], "knock-back", "P", "as 0155"), new("a00_16_0156", [2020, 2014], "knock-back, behind", "P", "as 0155"),
        new("a00_16_0210", [2040, 2020], "blown down", "P", "DS1 a00_2040"), new("a00_16_0220", [2030, 2040], "blown up", "P", "DS1 a00_2030"),
        new("a00_16_0610", [2040], "fall start", "P", "as 0210"), new("a00_16_0630", [2030], "fall start", "P", "as 0220"),
        new("a00_16_07", [2040], "down / get up", "P", "no DS1 lying set: the blow-down clip"),
        new("a00_16_1", [2010], "parried / bleed / stagger", "P", "DS1 medium hit"),
        new("a00_17_", [2200], "death", "V", "DS1 a00_2200 (JT 12)"),
        new("a00_11_0011", [701], "step back", "V", "SpinStep 701 (AI_DIR_TYPE_B)"), new("a00_11_0031", [701], "step back (long)", "V", "701"),
        new("a00_11_0012", [703], "step right", "V", "SpinStep 703"), new("a00_11_0032", [703], "step right (long)", "V", "703"),
        new("a00_11_0022", [702], "step left", "V", "SpinStep 702"), new("a00_11_0042", [702], "step left (long)", "V", "702"),
        new("a00_11_0010", [701], "step (front)", "P", "no DS1 forward step → 701"), new("a00_11_0030", [701], "step (front, long)", "P", "as 0010"),
        new("a75_01_7100", [9000, 0], "dormant loop", "P", "DS1 a00_9000"), new("a75_01_7101", [9060, 0], "wake", "P", "DS1 a00_9060"),
        new("a75_01_7110", [9000, 0], "dormant loop", "P", "as 7100"), new("a75_01_7111", [9060, 0], "wake", "P", "as 7101"),
        new("a79_", [0], "throw victim", "P", "no DS1 throw-victim clip: idle"));

    public static Spec UndeadAttackDog => new("c3340", "c2270", 227000, "c1030", "BD_LG", "BD_LG_1bit_2side",
        Model: 2070, AnimId: 207010, EnemyParam: 1_334_000, Ds1Npc: 334000, BehaviorId: 1_334_010, WeaponParam: 133_401_000, TemplateWeaponParam: 22700000,
        HighLogic: 334011, LowLogic: 334001,
        Anim: new Ds1BossAnims.Spec("c3340", "c2270", "c2070",
            Singles: [3000, 3001, 3002, 3004, 3005, 3006],
            Chains: [[3006, 3004], [3000, 3004], [3002, 3005], [3001, 3005]],
            Roles: DogRoles,
            NoReaction: []),
        AttackRange: new() { [3000] = 1.1f, [3001] = 3.5f, [3002] = 3.6f, [3004] = 1.1f, [3005] = 3.5f, [3006] = 2.3f },
        InterimCombos: [],
        Ds1Entity: 1010500)
    {
        Ds1Map = "m10_01_00_00",
        LargeRegular = true,
        ExtraVariants = [(334003, 334003), .. ChrPorter.IfDepths((334001, 334001), (334002, 334002))],   // round 7: + the Depths' dogs
        Ai = new Ai.Boss.AiPorter.Spec("m10_01_00_00", BattleGoal: 334000, ThinkParam: 334000, TemplateUpper: 305011, TemplateLower: 305001, Upper: 334011, Lower: 334001),
    };

    /// <summary>
    /// Session 11 (burgchr11): a role table for DS1's small quadrupeds on their DS2 twin networks (rat c1201 on Corpse Rat c2260,
    /// Crystal Lizard c3300 on c2130, Armored Tusk c3460 on Giant Undead Boar c2140): every slot lists DS1 clips most-specific first
    /// and ends on clips every such DS1 character has (idle 0, walk 200, small hit 2000 / medium 2004 / knock-back 2020, death 2200),
    /// so one table serves the three (which clip a slot plays per character: the build's role report; P).
    /// </summary>
    public static readonly Ds1RigAnims.Role[] QuadrupedRoles = With(AsylumDemonRoles,
        new("a00_01_0011", [603, 605, 0], "turn right 90", "V", Mv, SyncEvents: -1), new("a00_01_0012", [605, 603, 0], "turn right 180", "V", Mv, SyncEvents: -1),
        new("a00_03_0071", [605, 603, 0], "walk turn 180 (right)", "P", Mv), new("a00_03_7071", [604, 602, 0], "walk turn 180 (left)", "P", Mv),
        new("a00_04_0010", [500, 200], "run forward", "V", Mv, Loop: true), new("a00_04_0011", [500, 200], "run (banking)", "P", "as 0010", Loop: true), new("a00_04_7001", [500, 200], "run", "V", Mv, Loop: true),
        new("a00_04_0071", [605, 603, 0], "run turn", "P", Mv), new("a00_04_7071", [602, 604, 0], "run turn (left)", "P", Mv),
        new("a00_06_0012", [201, 200], "walk back", "P", Mv, Loop: true), new("a00_06_0013", [202, 200], "walk left", "P", Mv, Loop: true), new("a00_06_0014", [203, 200], "walk right", "P", Mv, Loop: true),
        new("a00_06_7012", [201, 200], "walk back", "P", Mv, Loop: true), new("a00_06_7013", [202, 200], "walk left", "P", Mv, Loop: true), new("a00_06_7014", [203, 200], "walk right", "P", Mv, Loop: true),
        new("a00_07_0011", [500, 200], "run forward", "P", Mv, Loop: true), new("a00_07_7011", [500, 200], "run forward", "P", Mv, Loop: true),
        new("a00_07_0012", [501, 201, 200], "run back", "P", Mv, Loop: true), new("a00_07_0013", [502, 202, 200], "run left", "P", Mv, Loop: true), new("a00_07_0014", [503, 203, 200], "run right", "P", Mv, Loop: true),
        new("a00_07_7012", [501, 201, 200], "run back", "P", Mv, Loop: true), new("a00_07_7013", [502, 202, 200], "run left", "P", Mv, Loop: true), new("a00_07_7014", [503, 203, 200], "run right", "P", Mv, Loop: true),
        new("a00_11_", [701, 0], "step", "P", "DS1 back step (SpinStep 701) where the character has one, else idle"),
        new("a00_13_", [1500, 0], "jump / airborne", "P", "DS1 a00_1500"), new("a00_14_", [1750, 0], "landing", "P", "DS1 a00_1750"),
        new("a00_16_0025", [2000, 2004], "light hit, front", "P", "DS1 small"), new("a00_16_0015", [2002, 2006, 2000, 2004], "light hit, side", "P", "DS1 small (side)"),
        new("a00_16_0016", [2001, 2005, 2000, 2004], "light hit, behind", "P", "DS1 small (behind)"),
        new("a00_16_0055", [2010, 2004, 2000], "heavy hit", "P", "DS1 medium"), new("a00_16_0065", [2010, 2004, 2000], "heavy hit", "P", "as 0055"), new("a00_16_0056", [2011, 2005, 2001, 2000], "heavy, behind", "P", "DS1 medium (behind)"),
        new("a00_16_0115", [2012, 2014, 2010, 2004, 2000], "extra-heavy", "P", "DS1 large"), new("a00_16_0125", [2012, 2014, 2010, 2004, 2000], "extra-heavy", "P", "as 0115"),
        new("a00_16_0116", [2013, 2011, 2005, 2000], "extra-heavy, behind", "P", "DS1 large (behind)"),
        new("a00_16_0155", [2020, 2014, 2000], "knock-back", "P", "DS1 a00_2020"), new("a00_16_0165", [2020, 2014, 2000], "knock-back", "P", "as 0155"), new("a00_16_0156", [2020, 2014, 2000], "knock-back", "P", "as 0155"),
        new("a00_16_0210", [2040, 2020, 2000], "blown down", "P", "DS1 a00_2040"), new("a00_16_0220", [2030, 2040, 2020, 2000], "blown up", "P", "DS1 a00_2030"),
        new("a00_16_06", [2040, 2030, 2020, 2000], "fall start", "P", "as 0210"), new("a00_16_07", [2040, 2020, 2000], "down / get up", "P", "no DS1 lying set: the blow-down clip"),
        new("a00_16_1", [2010, 2004, 2000], "parried / bleed / stagger", "P", "DS1 medium hit"), new("a00_16_7001", [3500, 0], "rebound", "P", "DS1 a00_3500 where it exists"),
        new("a00_16_8000", [2000, 2004], "boss hit", "P", "DS1 small"),
        new("a00_17_", [2200], "death", "V", "DS1 a00_2200 (JT 12)"),
        new("a79_", [2500, 2520, 0], "throw victim", "P", "DS1 backstab / plunge victim where it exists"));

    /// <summary>Session 11 (burgchr11): DS1's Small Rat (c1201, NpcParam 120101: Undead Burg sewers ×3, think 120101 → 120100_battle
    /// "SmallInunezumi"; 5 % Humanity) on DS2's Corpse Rat network c2260 (DS1's 50 bones of its 53), a large regular (rule LR1). Model
    /// c1201 (free in DS2), animId 120110 (decade 1200: DSR's other rats c1200 / c1202 would need another decade when ported). Attacks
    /// 3000-3003 (ranges 0.8 / 0.8 / 1.9 / 1), each 50 % single, 50 % → ComboFinal 3000 / 3001 (rule B2c: kept only where c2260 has
    /// route categories).</summary>
    public static Spec SmallRat => new("c1201", "c2260", 226000, "c1030", "BD_LG", "BD_LG_1bit_2side",
        Model: 1201, AnimId: 120110, EnemyParam: 120101, Ds1Npc: 120101, BehaviorId: 120111, WeaponParam: 12_010_100, TemplateWeaponParam: 22600000,
        HighLogic: 120111, LowLogic: 120101,
        Anim: new Ds1BossAnims.Spec("c1201", "c2260", "c1201", Singles: [3000, 3001, 3002, 3003], Chains: [[3000, 3001], [3001, 3000], [3002, 3000], [3003, 3000]], Roles: QuadrupedRoles, NoReaction: []),
        AttackRange: new() { [3000] = 0.8f, [3001] = 0.8f, [3002] = 1.9f, [3003] = 1f },
        InterimCombos: [],
        Ds1Entity: -1201)
    {
        Ds1Map = "m10_01_00_00", LargeRegular = true,
        ExtraVariants = ChrPorter.IfDepths((120100, 120100), (120102, 120102), (120104, 120104)),   // session 11 (burg11, round 7): the Depths' small rats
        Ai = new Ai.Boss.AiPorter.Spec("m10_01_00_00", BattleGoal: 120100, ThinkParam: 120101, TemplateUpper: 305011, TemplateLower: 305001, Upper: 120111, Lower: 120101),
    };

    /// <summary>Session 11 (burgchr11): DS1's Crystal Lizard of the Burg (c3300, NpcParam 330060, entity 1010400 in a barrel, think 330001 →
    /// 330000_battle: Runaway = LeaveTarget 100 m, Hide = NonspinningAttack 3000 (the burrow); its Twinkling Titanite is an event drop)
    /// on DS2's Crystal Lizard network c2130 (the same 34-bone rig), a large regular. DS2 owns model c3300 → model 2080 (decade free in
    /// DS2 and DSR), animId 208010.</summary>
    public static Spec CrystalLizard => new("c3300", "c2130", 213000, "c1030", "BD_LG", "BD_LG_1bit_2side",
        Model: 2080, AnimId: 208010, EnemyParam: 330060, Ds1Npc: 330060, BehaviorId: 330070, WeaponParam: 33_006_000, TemplateWeaponParam: 21300000,
        HighLogic: 330071, LowLogic: 330061,
        Anim: new Ds1BossAnims.Spec("c3300", "c2130", "c2080", Singles: [3000], Chains: [], Roles: QuadrupedRoles, NoReaction: []),
        AttackRange: new() { [3000] = 99f },
        InterimCombos: [],
        Ds1Entity: 1010400)
    {
        Ds1Map = "m10_01_00_00", LargeRegular = true,
        Ai = new Ai.Boss.AiPorter.Spec("m10_01_00_00", BattleGoal: 330000, ThinkParam: 330001, TemplateUpper: 305011, TemplateLower: 305001, Upper: 330071, Lower: 330061),
    };

    /// <summary>Session 11 (burgchr11): DS1's Armored Tusk (c3460, Parish gate 1010310, NpcParam 346000, think 346000 → 346000_battle; Fang
    /// Boar Helm 20 %) on DS2's Giant Undead Boar network c2140 (DS1's 64 bones minus the 33 armour-cloth bones are c2140's), a large
    /// regular. Model c3460 (free in DS2), animId 346010 (TAE template c214000). Attacks 3000-3009 (ranges from the Lua; 3003 the
    /// gate-aware NonspinningAttack, 3009 the charge 2.5-4 m). Not translated (reported by the AI port): IsInsideTarget (the side /
    /// rear attacks 3004-3006 by angle), the event-point acts (the church gate, E-REQ).</summary>
    public static Spec ArmoredTusk => new("c3460", "c2140", 214003, "c1030", "BD_LG", "BD_LG_1bit_2side",   // textures: c3469 (NpcParam normalChangeTexChrId)
        Model: 3460, AnimId: 346010, EnemyParam: 346000, Ds1Npc: 346000, BehaviorId: 346010, WeaponParam: 34_600_000, TemplateWeaponParam: 21400000,
        HighLogic: 346011, LowLogic: 346001,
        Anim: new Ds1BossAnims.Spec("c3460", "c2140", "c3460", Singles: [3000, 3001, 3002, 3003, 3004, 3005, 3006, 3007, 3008, 3009], Chains: [], Roles: QuadrupedRoles, NoReaction: []),
        AttackRange: new() { [3000] = 2f, [3001] = 10f, [3002] = 1.4f, [3003] = 99f, [3004] = 1.6f, [3005] = 1.6f, [3006] = 1.1f, [3007] = 3.5f, [3008] = 8f, [3009] = 4f },
        InterimCombos: [],
        Ds1Entity: 1010310)
    {
        Ds1Map = "m10_01_00_00", LargeRegular = true, TemplateTae = 214000, Ds1TexChr = "c3469",
        Ai = new Ai.Boss.AiPorter.Spec("m10_01_00_00", BattleGoal: 346000, ThinkParam: 346000, TemplateUpper: 305011, TemplateLower: 305001, Upper: 346011, Lower: 346001),
    };

    // ---------------------------------------------------------------- Bell Gargoyles (c5350: 1010800 / 1010801 + the intro stand-in 1010802; k = 2)
    /// <summary>
    /// Session 11 (burgchr11): DS1's Bell Gargoyles on DS2's Belfry Gargoyle boss network (c3240; only 26 of DS1's 126 bone names are
    /// c3240's, the resize maps the rest's network joints to the root: head look / IK of the template act on root, P). NpcParam 535000
    /// (833 HP, resident 5333 medium reactions + 5360 none under super armour 80, display groups #00# + #01#) and the second gargoyle
    /// 535001 (+ SpEffect 5490, group #02# only); battle goal 535000 for both (think 535000 / 535001). Attacks 3000-3029 incl. the
    /// aerial dives 3018 / 3020 (12-20 m: flight is the clips' own root motion with DS1's Ignore Gravity windows); chains act1 3000 →
    /// 3001 (→ 3015), act3 3004 → 3005, act8 3016 → 3029. Display groups → c1030 culling materials: #00# WP1 (20), #01# WP2 (21), #02#
    /// "SD cull24" (24). Not here (reported for M3): the tail cut (DS1 2004:22 / NPC part HP → Gargoyle Tail Axe; DS2 BossPartsParam),
    /// the severed tail actors c5352, the second gargoyle at 60 % (event 11015396) and the pair's shared logic group, the 4000-4129
    /// set (not requested by the Lua; event / SpEffect 5490 driven, P).
    /// </summary>
    public static readonly Ds1RigAnims.Role[] GargoyleRoles = With(With(AsylumDemonRoles, MediumDamageRoles),
        new("a00_11_0011", [701], "step back (short)", "V", "SpinStep 701"), new("a00_11_0031", [711, 701], "hop back (long)", "V", "SpinStep 711"),
        new("a00_11_0012", [703], "step right (short)", "V", "SpinStep 703"), new("a00_11_0032", [713, 703], "hop right (long)", "V", "SpinStep 713"),
        new("a00_11_0022", [702], "step left (short)", "V", "SpinStep 702"), new("a00_11_0042", [712, 702], "hop left (long)", "V", "SpinStep 712"),
        new("a00_11_0010", [700, 701], "step front", "P", "a00_0700"), new("a00_11_0030", [700, 701], "step front (long)", "P", "a00_0700"),
        new("a00_17_0020", [2202, 2200], "death 2", "P", "DS1 a00_2202"),
        new("a75_01_7100", [7000, 0], "dormant (perched)", "P", "DS1 a00_7000 (the intro stand-in's pose)"), new("a75_01_7101", [7001, 0], "wake", "P", "DS1 a00_7001"),
        new("a75_01_7110", [7000, 0], "dormant", "P", "as 7100"), new("a75_01_7111", [7001, 0], "wake", "P", "as 7101"));

    public static Spec BellGargoyle => new("c5350", "c3240", 324000, "c1030", "BD_LG", "BD_LG_1bit_2side",
        Model: 5350, AnimId: 535010, EnemyParam: 535000, Ds1Npc: 535000, BehaviorId: 535010, WeaponParam: 53_500_000, TemplateWeaponParam: 32400000,
        HighLogic: 535011, LowLogic: 535001,
        Anim: new Ds1BossAnims.Spec("c5350", "c3240", "c5350",
            Singles: [3000, 3001, 3003, 3004, 3005, 3006, 3008, 3009, 3010, 3011, 3012, 3014, 3015, 3016, 3017, 3018, 3020, 3022, 3026, 3027, 3029],
            Chains: [[3000, 3001, 3015], [3004, 3005], [3016, 3029]],
            Roles: GargoyleRoles,
            NoReaction: ["a00_16_06", "a00_16_07", "a00_16_10", "a00_16_11", "a00_09_"]),
        AttackRange: new() { [3000] = 7f, [3001] = 7f, [3003] = 10.3f, [3004] = 4.1f, [3005] = 4.1f, [3006] = 8f, [3008] = 3f, [3009] = 11f, [3010] = 6f, [3011] = 9f, [3012] = 6f,
            [3014] = 1.6f, [3015] = 7f, [3016] = 3.5f, [3017] = 2.5f, [3018] = 15f, [3020] = 20f, [3022] = 4.5f, [3026] = 9f, [3027] = 4f, [3029] = 3.5f },
        InterimCombos: [],
        Ds1Entity: 1010800)
    {
        Ds1Map = "m10_01_00_00",
        WeaponMeshes = new() { ["#00#"] = "WP1_c1030", ["#01#"] = "WP2_c1030", ["#02#"] = "SD cull24" },
        Culling = [20, 21],
        ExtraVariants = [(535001, 535001)],
        ExtraCulling = new() { [535001] = [24] },
        Ai = new Ai.Boss.AiPorter.Spec("m10_01_00_00", BattleGoal: 535000, ThinkParam: 535000, TemplateUpper: 305011, TemplateLower: 305001, Upper: 535011, Lower: 535001),
    };

    // ---------------------------------------------------------------- Hellkite Drake (c3430: the bridge 1010300, the fly-over double 1010302)
    /// <summary>
    /// Session 11 (burgchr11): DS1's Hellkite Drake on DS2's Guardian Dragon boss network (c2120; 22 of DS1's 135 bone names, the rest's
    /// network joints map to the root, P), the body, clips, attacks and DS1 AI of the character; what DS1 does with it is mostly its
    /// events (recorded for M2/M3: the fly-overs 7000-7019 played by 2003:18 / 2004:9, the 13 attack-pattern events with Hellkite
    /// Breath Control 2004:36 on object 1011060, the landing 7010 / take-off 7016 / flee 7013 / retreat 7017-7019 below 70 %, the
    /// tail NPC part (200 HP → Drake Sword), souls only when killed landed). NpcParam 343000: 1840 HP, turn 45°/s, resident 5300 =
    /// never a hit reaction, 5150 fire immunity. Battle goal 343000 reads GetEventRequest(1) (DS1's event channel, E-REQ: no DS2
    /// counterpart known; the translator answers 0, so only its default branch runs). Attacks 3000-3010 (the Lua's Att* ranges;
    /// 3000-3002 are the 18-40 m breaths). #00# (the tail) is drawn as body until a part-break exists (M3).
    /// </summary>
    public static Spec HellkiteDrake => new("c3430", "c2120", 212000, "c1030", "BD_LG", "BD_LG_1bit_2side",
        Model: 3430, AnimId: 343010, EnemyParam: 343000, Ds1Npc: 343000, BehaviorId: 343010, WeaponParam: 34_300_000, TemplateWeaponParam: 21200000,
        HighLogic: 343011, LowLogic: 343001,
        Anim: new Ds1BossAnims.Spec("c3430", "c2120", "c3430",
            // (DS1's 3011, the take-off request the battle goal plays on GetEventRequest(1) ≥ 1, has a TAE but no clip: rule EM1-n)
            Singles: [3000, 3001, 3002, 3003, 3004, 3005, 3006, 3007, 3008, 3009, 3010],
            Chains: [],
            Roles: With([.. QuadrupedRoles.Where(r => !r.Slot.StartsWith("a00_16_"))],
                new("a00_16_", [0], "damage reaction → none", "P", "resident SpEffect 5300: DS1's Hellkite never plays a hit reaction"),
                // session 11 (chrai11), rule TC1: c2120's tail break reaction (BossParam part 1 / hit part 7) plays DS1's tail cut a00_8000
                // (11015301: 8000 on the bridge, 8010 in flight; DS2 has one break slot)
                new("a00_16_8107", [8000, 0], "tail cut (part break 07)", "P", "DS1 a00_8000: 11015301 plays it when the tail part's HP reaches 0"),
                new("a00_01_0011", [603, 0], "turn right", "V", "DS1 a00_0603"), new("a00_01_0012", [605, 603, 0], "turn right 180", "V", "DS1 a00_0605"),
                new("a75_01_7100", [7000, 0], "dormant / perched", "P", "DS1 a00_7000"), new("a75_01_7101", [7001, 7000, 0], "wake / take off", "P", "DS1 a00_7001 is named by the EMEVD but missing in c3430 (→ 7000)"),
                new("a00_17_", [2200], "death", "V", "DS1 a00_2200")),
            // session 11 (chrai11), rule TC1: every damage slot but the tail break (a00_16_8107) is "no reaction"
            NoReaction: ["a00_16_0", "a00_16_1", "a00_16_80", "a00_09_"]),
        AttackRange: new() { [3000] = 34f, [3001] = 38f, [3002] = 40f, [3003] = 5f, [3004] = 5f, [3005] = 13f, [3006] = 13f, [3007] = 16f, [3008] = 1f, [3009] = 25f, [3010] = 5.5f },
        InterimCombos: [],
        Ds1Entity: 1010300)
    {
        Ds1Map = "m10_01_00_00",
        WeaponMeshes = new() { ["#00#"] = "BD_LG" },
        Ai = new Ai.Boss.AiPorter.Spec("m10_01_00_00", BattleGoal: 343000, ThinkParam: 343000, TemplateUpper: 305011, TemplateLower: 305001, Upper: 343011, Lower: 343001),
    };

    /// <summary>The 1:1 bosses the pipeline builds (DS1 MSB entity → spec), for the map placement and the event translator.
    /// Session 11 (ret11): + the Stray Demon.</summary>
    public static IReadOnlyList<Spec> All => [AsylumDemon, StrayDemon, .. (ChrPorter.Burg ? Burg : []), .. (ChrPorter.Burg && ChrPorter.Depths ? Depths : [])];   // session 11 (dchr11): + the Depths' (m10_00)

    /// <summary>Session 11 (dchr11): the Depths' (DS1 m10_00) 1:1 bosses and large regulars (build-pack --map depths --burg-chrs), placed by
    /// MapEnemies (rule MB1). Each spec lives in its own Ds1Boss.Depths*.cs (null = not built).</summary>
    public static Spec[] Depths => [.. new[] { Rat, GiantRat, Slime, Basilisk, GapingDragon }.OfType<Spec>()];

    /// <summary>Session 11 (burgchr11): the Burg's (m10_01) 1:1 bosses and large regulars (build-pack --burg-chrs; not placed: M3).</summary>
    public static Spec[] Burg => [TaurusDemon, CapraDemon, TitaniteDemon, UndeadAttackDog, SmallRat, CrystalLizard, ArmoredTusk, BellGargoyle, HellkiteDrake];

    /// <summary>Session 11 (ret11), rule B-k: a boss spec by its DS1 chr ("c2232", "c2230") or anim set ("c2210", "c2190").</summary>
    public static Spec? ByKey(string key)
    {
        bool Is(Spec s) => s.Ds1Chr == key || $"c{s.AnimId / 100:D4}" == key || s.N == key;
        if (!ChrPorter.Burg && Burg.Any(Is)) ChrPorter.Burg = true;   // session 11 (burgchr11): ds1boss-build / -check --boss c2250 …
        if (!ChrPorter.Depths && Depths.Any(Is)) { ChrPorter.Burg = true; ChrPorter.Depths = true; }   // session 11 (dchr11): --boss c5260 …
        return All.FirstOrDefault(Is);
    }
    public static Spec? ForEntity(int ds1Entity) => All.FirstOrDefault(s => s.Ds1Entity == ds1Entity);

    /// <summary>
    /// DS2 start-up state (GeneratorRegistParam.startUpStateType / 131702) whose dormant loop plays DS1 waiting animation
    /// <paramref name="anim"/>: the template network's SM_StartUp selects a75_01_7100/7101 on StartupType 2 and 7110/7111 on
    /// 3 (c3050.network.txt: SM 1388 transitions "StartupType in [2,2]" / "[3,3]"); the role table says which DS1 clip each
    /// slot plays (a75_01_7100 ← DS1 9000, waiting in the air). Null when no dormant slot plays it.
    /// </summary>
    public static int? StartUpType(Spec s, int anim)
    {
        foreach (var (slot, type) in new[] { ("a75_01_7100", 2), ("a75_01_7110", 3) })
            if (s.Anim.Roles.FirstOrDefault(r => r.Slot == slot) is { } r && r.Ds1.Length > 0 && r.Ds1[0] == anim) return type;
        return null;
    }

    /// <summary>The DS1 clip the start-up wake slot (a75_01_7101 for type 2) plays: DS1 9060, the drop.</summary>
    public static int? WakeClip(Spec s, int type) => s.Anim.Roles.FirstOrDefault(r => r.Slot == (type == 3 ? "a75_01_7111" : "a75_01_7101")) is { Ds1.Length: > 0 } r ? r.Ds1[0] : null;

    public sealed class Result
    {
        public Dictionary<string, byte[]> Files = [];
        /// <summary>merge9 stage 6: loose files for the game folder (pack device "title"): DS1's wave banks, which DS1's FMOD
        /// project asks FMOD for by their own names (chrsound rule S2).</summary>
        public Dictionary<string, byte[]> TitleFiles = [];
        /// <summary>merge9 stage 6: per TAE animation, the DS1 sound events written and the ones not played (as Ds1RigChr's).</summary>
        public List<string> SoundLog = [];
        /// <summary>merge9 stage 6: the sound bank (chrsound rules S1-S10).</summary>
        public Ds1ChrSound.BankResult? Bank;
        public Ds1BossAnims.Result Anims = null!;
        public Ds1RigModel.Result Model = null!;
        public List<string> Report = [];
        public List<(int Id, string Note)> DamageRows = [];
        public List<Ds1RigModel.ExtraDummy> CapsuleDummies = [];
        public Ai.Boss.AiPorter.Result? Ai;
        /// <summary>Session 11 (track chrai11), rule TC1: the tail's culling ids (shown / after the break), the DS1 bones its meshes are
        /// skinned to, and the model's dummies on them (null: no DS1 tail cut).</summary>
        public (int Base, int Break, List<string> Bones, List<int> Dummies)? Tail;
    }

    // ================================================================== session 11 (track chrai11): rules TC1 / HB1
    /// <summary>A DS1 NPC part cut (the tail): a 2004:22 Create NPC Part on a part of the spec and, in the same event, the
    /// 2004:26 Change Character DispMask(entity, bit, OFF) that hides its display group "#bit#" (m10_01 11015397: the Gargoyles 1010800 /
    /// 1010801, part 5350, HP 65, bit 0; 11015301: the Hellkite 1010300, part 3430, HP 200, bit 0).</summary>
    public sealed record TailCut(int Entity, int PartId, int GroupIndex, int Hp, int DispBit);

    public static List<TailCut> TailCuts(string dsrRoot, Spec s)
    {
        var res = new List<TailCut>();
        if (s.Ds1Map == "m18_01_00_00") return res;   // the Asylum keeps its verified build
        string path = Path.Combine(dsrRoot, "event", s.Ds1Map + ".emevd.dcx");
        if (!File.Exists(path)) return res;
        var emevd = EMEVD.Read(path);
        var defs = Commands.EmevdDump.Defs();
        var msb = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", s.Ds1Map + ".msb"));
        var npcs = s.ExtraVariants.Select(x => x.Npc).Append(s.Ds1Npc).ToHashSet();
        var ents = msb.Parts.Enemies.Where(p => p.ModelName == s.Ds1Chr && npcs.Contains(p.NPCParamID) && p.EntityID > 0).Select(p => p.EntityID).ToHashSet();
        var inits = emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2000 && i.ID == 0 && i.ArgData.Length >= 8).ToList();
        foreach (var ev in emevd.Events)
            for (int k = 0; k < ev.Instructions.Count; k++)
            {
                var x = ev.Instructions[k];
                if (x.Bank != 2004 || x.ID != 22) continue;
                var v = Events.EmevdTranslator.Decode(defs, x);
                var dm = ev.Instructions.Select(i => i.Bank == 2004 && i.ID == 26 ? Events.EmevdTranslator.Decode(defs, i) : null).FirstOrDefault(d => d != null && d.Length >= 3 && d[2] == 0);
                if (dm == null) continue;
                var pe = ev.Parameters.FirstOrDefault(p => p.InstructionIndex == k && p.TargetStartByte == 0);
                IEnumerable<int> who = pe == null ? [(int)v[0]]
                    : inits.Where(i => BitConverter.ToUInt32(i.ArgData, 4) == ev.ID && 8 + pe.SourceStartByte + 4 <= i.ArgData.Length).Select(i => BitConverter.ToInt32(i.ArgData, 8 + (int)pe.SourceStartByte));
                foreach (int e in who.Where(ents.Contains).Distinct())
                    res.Add(new TailCut(e, (int)v[1], (int)v[2], (int)v[3], (int)dm[1]));
            }
        return res;
    }

    /// <summary>Rule HB1: the DS2 breath bullet of a spec whose parts DS1's events give Hellkite Breath Control (2004:36): id
    /// rule E-ID1's EnemyRow(V, 999, 9) (session 12; was 300,000,000 + the DS1 Bullet row) of the DS1 Bullet row V = NpcParam.behaviorVariationId (the variation's breath, c3430: Bullet 34300 "A point breath
    /// 1", AtkParam_Npc 34300 "ヘルカイト_ブレス"), or null.</summary>
    public static int? BreathBulletId(string dsrRoot, Ds1Source ds1, Spec s)
    {
        if (s.Ds1Map == "m18_01_00_00") return null;
        string path = Path.Combine(dsrRoot, "event", s.Ds1Map + ".emevd.dcx");
        if (!File.Exists(path)) return null;
        var emevd = EMEVD.Read(path);
        var msb = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", s.Ds1Map + ".msb"));
        var ents = msb.Parts.Enemies.Where(p => p.ModelName == s.Ds1Chr && p.EntityID > 0).Select(p => p.EntityID).ToHashSet();
        bool any = emevd.Events.SelectMany(e => e.Instructions).Any(i => i.Bank == 2004 && i.ID == 36 && i.ArgData.Length >= 4 && ents.Contains(BitConverter.ToInt32(i.ArgData, 0)));
        if (!any || ds1.Params.Params["NpcParam"][s.Ds1Npc] is not { } npc) return null;
        int variation = Convert.ToInt32(Cell(npc, "behaviorVariationId"));
        // session 12 (ehit12), rule E-ID1: in DS2's enemy id range (was 300,000,000 + the Bullet row: the System range, never hit)
        return ds1.Params.Params["Bullet"][variation] != null ? Ds1AttackParams.EnemyRow(ds1.Params.Params["BehaviorParam"], variation, 999, 9) : null;
    }

    /// <summary>
    /// Rule HB1 (P): DS1's Hellkite Breath Control sweeps the breath's fire along an object's dummy line (o1060: 114 dummies over 100 m
    /// of bridge) while the clip plays; DS2's events place no damage along points, so the breath is DS1's breath bullet fired at the
    /// dragon (131821): the template boss's fire breath bullet (c2120 Guardian Dragon EnemyBulletParam 121200210: fire SFX 1910) with DS1's
    /// ballistics (Bullet: initVellocity, life, hitRadius → hitRadiusMax over spreadTime) and DS1's damage (its atkId_Bullet through the
    /// boss calibration, rule E9u), no DS2 child explosions, from the dummy most of DS1's breath clips aim their first hit from (c3430:
    /// 10, the mouth). Rows only when DS1's events use it (<see cref="BreathBulletId"/>).
    /// </summary>
    static void Hb1(Pack pack, string dsrRoot, Ds1Source ds1, Spec s, PARAM.Row npc, int variation, FLVER2 ds1Flver, Result res)
    {
        if (BreathBulletId(dsrRoot, ds1, s) is not int id) return;
        var bul = ds1.Params.Params["Bullet"][variation]!;
        var bp = pack.Param("EnemyBulletParam"); var dp = pack.Param("EnemyDamageParam");
        if (bp[id] != null) return;
        const int tId = 121200210;
        var tB = bp[tId] ?? throw new InvalidDataException($"rule HB1: DS2 EnemyBulletParam {tId} (Guardian Dragon fire breath) missing");
        var tD = dp[tId] ?? dp[110304010]!;
        // the dummy: the most common first-hit dummy of the clips DS1's breath events name (else 10)
        var clips = Ai.Boss.EventMessages.Of(dsrRoot, s.Ds1Chr).Select(m => m.Anim).Distinct();
        var atk = ds1.Params.Params["AtkParam_Npc"]; var beh = ds1.Params.Params["BehaviorParam"];
        var srcClips = Ds1ChrSource.Load(dsrRoot, s.Ds1Chr);
        int dummy = clips.Select(c => Ds1BossAnims.HitGroups(srcClips.TaeAnim(c)).SelectMany(g => g.Judges).FirstOrDefault(-1))
            .Where(j => j >= 0 && beh[200_000_000 + variation * 1000 + j] is { } b && atk[Convert.ToInt32(Cell(b, "refId"))] is { } a && Convert.ToInt32(Cell(a, "hit0_DmyPoly1")) is > 0 and < 256)
            .Select(j => Convert.ToInt32(Cell(atk[Convert.ToInt32(Cell(beh[200_000_000 + variation * 1000 + j]!, "refId"))]!, "hit0_DmyPoly1")))
            .GroupBy(d => d).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key).DefaultIfEmpty(10).First();
        var r = pack.AddRow("EnemyBulletParam", tB, id, $"[Lordran] {s.Ds1Chr} DS1 Bullet {variation} breath (rule HB1)");
        foreach (var f in new[] { "shootOffsetX", "shootOffsetY", "shootOffsetZ", "shootAngle", "shootAngleXZ" }) Set(r, f, 0f);
        Set(r, "initialVelocity", (float)D(bul, "initVellocity")); Set(r, "maxSpeed", (float)Math.Max(D(bul, "initVellocity"), D(bul, "maxVellocity")));
        if (D(bul, "life") > 0) Set(r, "bulletLife", (float)D(bul, "life"));
        foreach (var f in new[] { "childeBulletId", "childeDamageId", "childeBulletId02", "childeDamageId02", "childeBulletId03", "childeDamageId03" }) Set(r, f, 0);   // 0 = none (vanilla arrow 110304010)
        Set(r, "isChildeExplosion", 0);
        var d = pack.AddRow("EnemyDamageParam", tD, id, $"[Lordran] {s.Ds1Chr} DS1 Bullet {variation} breath hit (rule HB1)");
        var rates = new[] { "physicsAttackPowerRate", "magicAttackPowerRate", "fireAttackPowerRate", "thunderAttackPowerRate", "staminaAttackRate" }.Select(f => Ds1AttackParams.ResidentRate(ds1, npc, f)).ToArray();
        if (atk[I(bul, "atkId_Bullet")] is { } ba) Ds1AttackParams.AtkToDamage(ba, d, null, res.Report, $"{s.Ds1Chr} breath bullet {variation}", rates, pack, $"{s.Ds1Chr} 1:1 boss");
        double rad = D(bul, "hitRadius") > 0 ? D(bul, "hitRadius") : D(tD, "radius");
        Set(d, "radius", (float)rad);
        if (D(bul, "hitRadiusMax") > rad) { Set(d, "expandSize", (float)D(bul, "hitRadiusMax")); Set(d, "expandTime", (float)(D(bul, "spreadTime") > 0 ? D(bul, "spreadTime") : D(bul, "life"))); }
        Set(d, "hitDummyPolyType", 0); Set(d, "hitDummyPolyId", dummy);
        res.Report.Add($"rule HB1: breath bullet {id} (template {tId}, sfx {Cell(r, "bulletSfxId")}): DS1 Bullet {variation} v {D(bul, "initVellocity")} m/s, life {D(bul, "life")} s, radius {rad}→{D(bul, "hitRadiusMax")} m, AtkParam_Npc {I(bul, "atkId_Bullet")} → damage {Cell(d, "damage01")} type {Cell(d, "damageType01")}, from dummy {dummy}; fired by the events' 131821 at each breath message");
    }

    static int I(PARAM.Row r, string f) => Convert.ToInt32(Cell(r, f));

    /// <summary>
    /// Rule TC1 (params): DS2's part break for a DS1 NPC-part cut. BossParam (EnemyParam id): part 1 = the cut, HP = DS1's NPCPartHP × the
    /// body's own DS1 → DS2 HP factor (the EnemyParam's calibrated HP / DS1 NpcParam hp), no break object, no flag (DS1's award events read the
    /// break: TC1-E, f130310); BossPartsParam EnemyParam·100 + 7 (hit part 7 = tail: c2120's _h.hkxpwv Tail2-5) → hpCategory 1 (part 1);
    /// BossPartsModelParam EnemyParam·100 + 1: the tail's culling id → the break's (an id no mesh uses: the tail disappears, as DS1's
    /// DispMask off), its hit part and ragdoll part 7 off, its dummies' damage / SFX / sound off. Templates: the Guardian Dragon's rows
    /// (212000 / 21200007 / 21200001, the Hellkite's network template; its own tail cut); a template network with a break slot keeps its
    /// break animation (c2120 a00_16_8107 BreakParts07 → DS1 a00_8000), others get none (BeakAnimId 0, P).
    /// </summary>
    static void Tc1(Pack pack, string dsrRoot, Ds1Source ds1, Spec s, Result res)
    {
        if (res.Tail is not { } tail) return;
        var cuts = TailCuts(dsrRoot, s);
        var bossP = pack.Param("BossParam"); var partsP = pack.Param("BossPartsParam"); var modelP = pack.Param("BossPartsModelParam");
        var msb = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", s.Ds1Map + ".msb"));
        var eps = new Dictionary<int, int> { [s.Ds1Npc] = s.EnemyParam };
        foreach (var (xn, xe) in s.ExtraVariants) eps[xn] = xe;
        bool templateHasBreak = bossP[s.TemplateEnemyParam] != null;
        foreach (var (dsNpc, ep) in eps)
        {
            var cut = cuts.FirstOrDefault(c => msb.Parts.Enemies.Any(p => p.EntityID == c.Entity && p.NPCParamID == dsNpc));
            if (cut == null) continue;
            var npc = ds1.Params.Params["NpcParam"][dsNpc]!;
            var epRow = pack.Param("EnemyParam")[ep]!;
            double factor = D(npc, "hp") > 0 ? D(epRow, "hitPoint") / D(npc, "hp") : 1;
            int hp = Math.Max(1, (int)Math.Round(cut.Hp * factor));
            foreach (int id in new[] { ep, s.AnimId }.Distinct())
            {
                if (id == s.AnimId && ep != s.EnemyParam) continue;
                var b = bossP[id] ?? pack.AddRow("BossParam", bossP[212000]!, id, $"[Lordran] {s.Ds1Chr} DS1 NPC part cut (rule TC1)");
                if (!templateHasBreak) { Set(b, "isPartialPhysics", 0); Set(b, "part1BeakAnimId", 0); }
                Set(b, "part1Hp", hp); Set(b, "part1BreakObjId", 0); Set(b, "part1EventFlag", 0); Set(b, "part1BreakObjDummyPolyId", 0);
                for (int k = 2; k <= 4; k++) Set(b, $"part{k}Hp", 0);
            }
            var pr = partsP[ep * 100 + 7] ?? pack.AddRow("BossPartsParam", partsP[21200007]!, ep * 100 + 7, $"[Lordran] {s.Ds1Chr} tail hit part (rule TC1)");
            Set(pr, "hpCategory", 1);
            if (!templateHasBreak) Set(pr, "damageAnimId", -1);
            var mr = modelP[ep * 100 + 1] ?? pack.AddRow("BossPartsModelParam", modelP[21200001]!, ep * 100 + 1, $"[Lordran] {s.Ds1Chr} tail break model (rule TC1)");
            Set(mr, "baseCulling", tail.Base); Set(mr, "breakCulling", tail.Break);
            Set(mr, "disableRagdollParts", 7); Set(mr, "disableHitParts1", 7); Set(mr, "disableResidentSfxNo", 0);
            for (int k = 1; k <= 8; k++)
            {
                int dmy = k <= tail.Dummies.Count ? tail.Dummies[k - 1] : 0;
                Set(mr, $"disableDamageDpId{k}", dmy); Set(mr, $"disableSfxDpId{k}", dmy); Set(mr, $"disableSoundDpId{k}", dmy);
            }
            res.Report.Add($"rule TC1: EnemyParam {ep} (DS1 NpcParam {dsNpc}): DS1 part {cut.PartId} HP {cut.Hp} × {factor:0.###} = BossParam part1Hp {hp}{(templateHasBreak ? $" (break animation {Cell(bossP[ep]!, "part1BeakAnimId")} = the network's BreakParts07 slot)" : " (no break slot in the template network: no break animation)")}; BossPartsParam {ep * 100 + 7} (hit part 7 → part 1); BossPartsModelParam {ep * 100 + 1}: culling {tail.Base} → {tail.Break}, hit / ragdoll part 7 off, dummies {string.Join(",", tail.Dummies.Take(8))} off");
        }
        // the weapon slot shows the tail's culling id (a template slot with another id would hide it from the start)
        var w = pack.Param("EnemyEquipWeaponParam")[s.WeaponParam]!;
        if (Convert.ToInt32(Cell(w, "cullingId00")) != tail.Base && Convert.ToInt32(Cell(w, "cullingId01")) != tail.Base)
        {
            Set(w, Convert.ToInt32(Cell(w, "cullingId00")) <= 0 ? "cullingId00" : "cullingId01", tail.Base);
            res.Report.Add($"rule TC1: EnemyEquipWeaponParam {s.WeaponParam} shows culling {tail.Base} (the tail)");
        }
    }

    /// <summary>
    /// The translated AI's view of the boss (<see cref="Ai.Boss.Ds2AiEsd.IContext"/>, session 8 step 2): DS1 attack A → DS2 action
    /// A − 3000 when A is one of the boss's single attacks (rule B2), a DS1 chain or a prefix of one → its combo row (action
    /// 50 + k); DS1 regions → the map's EventLocations (AsylumScript's rule: every DS1 region with an entity, 3701000 + entity
    /// % 1000); POINT_INITIAL → the region DS1's event makes the boss's home (2004:13 Set Character Home on its entity:
    /// 11810310 → 1812300); dummies from DS1's FLVER (model space, DS1 faces −Z); capsule radius from DS1 NpcParam.hitRadius,
    /// the player's 0.4 m (P).
    /// </summary>
    public sealed class AiContext : Ai.Boss.Ds2AiEsd.IContext
    {
        readonly Spec s; readonly Ds1ChrSource src; readonly FLVER2 flver; readonly HashSet<int> regions; readonly int? home; readonly string dsrRoot;
        public double SelfRadius { get; }
        public double TargetRadius => 0.4;
        /// <summary>Session 11 (G10): DS1 side-walk speed = mean horizontal root displacement / duration of a00_0202 / a00_0203.</summary>
        public double SideWalkSpeed
        {
            get
            {
                var v = new List<double>();
                foreach (int id in new[] { 202, 203 })
                    if (src.Has(id))
                    {
                        var an = src.Bridge(id).Anim;
                        double d = (an.Frames - 1) / an.Fps;
                        var p0 = an.RootPos[0]; var p1 = an.RootPos[^1];
                        if (d > 0) v.Add(Math.Sqrt((p1.X - p0.X) * (p1.X - p0.X) + (p1.Y - p0.Y) * (p1.Y - p0.Y)) / d);
                    }
                return v.Count > 0 ? v.Average() : -1;
            }
        }

        public AiContext(Spec s, Ds1ChrSource src, FLVER2 flver, Ds1Source ds1, string dsrRoot)
        {
            this.s = s; this.src = src; this.flver = flver; this.dsrRoot = dsrRoot;
            var msb = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", s.Ds1Map + ".msb"));
            regions = msb.Regions.GetEntries().Where(r => r.EntityID > 0 && r.Shape is MSB.Shape.Box or MSB.Shape.Point or MSB.Shape.Sphere or MSB.Shape.Cylinder).Select(r => r.EntityID).ToHashSet();
            SelfRadius = Convert.ToDouble(ds1.Params.Params["NpcParam"][s.Ds1Npc]!["hitRadius"].Value);
            var emevd = EMEVD.Read(Path.Combine(dsrRoot, "event", s.Ds1Map + ".emevd.dcx"));
            var defs = Commands.EmevdDump.Defs();
            home = emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2004 && i.ID == 13)
                .Select(i => Events.EmevdTranslator.Decode(defs, i)).Where(v => v.Length >= 2 && (int)v[0] == s.Ds1Entity).Select(v => (int?)v[1]).FirstOrDefault();
        }

        public int? SingleAction(int a) => a is >= 3000 and < 3030 && s.Anim.Singles.Contains(a) && src.Has(a) ? a - 3000 : null;

        public (int Action, int Stages)? Chain(IReadOnlyList<int> ids)
        {
            for (int k = 0; k < s.Anim.Chains.Length && k < 7; k++)
            {
                var c = s.Anim.Chains[k];
                if (ids.Count <= c.Length && c.Take(ids.Count).SequenceEqual(ids)) return (50 + k, c.Length);
            }
            return null;
        }

        /// <summary>Session 11 (chrai11 / dchr11), rule AI-R1: the EventLocation id of a DS1 region by its map's own rule (a MapObjects map, the
        /// Burg / the Depths: rule ID2 3710000 + entity % 10000, as MapScript writes them; the Asylum / Firelink: 3701000 + entity % 1000). The
        /// Burg's boss AIs (and the Depths' Giant Rat den tests) named the Asylum's ids, which no such EventLocation row has (every region test "never inside").</summary>
        public int? Location(int e) => !regions.Contains(e) ? null
            : Maps.MapHosts.Of(s.Ds1Map) is { Option: "--map burg" or "--map depths" } ? Events.Ds1MapContext.RegionId(e) : Maps.AsylumScript.RegionId(e);
        public int? HomePoint => home is int h ? Location(h) : null;
        /// <summary>Session 11 (chrai11), rule EM1 (MapObjects maps only: the Asylum's events read no message).</summary>
        public IReadOnlyList<(int Code, double Start, double End)> Messages(int clip) => s.Ds1Map == "m18_01_00_00" ? [] : Ai.Boss.EventMessages.Windows(dsrRoot, s.Ds1Chr, s.Ds1Map, clip);
        /// <summary>Session 11 (chrai11), rule FA1.</summary>
        public Ai.Boss.EventMessages.Fa1Layout? Fa1 => Ai.Boss.EventMessages.Layout(dsrRoot, s);

        public (double Lateral, double Behind)? Dummy(int id) =>
            flver.Dummies.FirstOrDefault(d => d.ReferenceID == id) is { } d ? (d.Position.X, d.Position.Z) : null;
    }

    // ================================================================== damage plan (rule B4)

    public sealed record DamageShape(int Row, int Dummy, int ModelType, float Radius, float Length);
    public sealed record DamageSlot(int Judge, int FirstRow, List<(int Judge, int AtkId, List<DamageShape> Shapes)> Parts);

    static object? Cell(PARAM.Row r, string f) => r.Cells.FirstOrDefault(c => c.Def.InternalName == f)?.Value;
    static double D(PARAM.Row r, string f) => Convert.ToDouble(Cell(r, f) ?? 0);
    static void Set(PARAM.Row r, string f, object v)
    {
        var c = r.Cells.FirstOrDefault(x => x.Def.InternalName == f) ?? throw new InvalidDataException($"{r.ID}: no field {f}");
        c.Value = Convert.ChangeType(v, c.Value.GetType(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>EnemyDamageParam row of DS1 judge J's hit h: rule E-ID1's Ds1AttackParams.EnemyRow(V, J, h) (session 12, ehit12; was
    /// 200000000 + variation·1000 + J·10 + h: DS2's System id range, where the engine looks in SystemDamageParam and finds nothing).</summary>
    static int RowId(PARAM ds1Behavior, int variation, int judge, int hit) => Ds1AttackParams.EnemyRow(ds1Behavior, variation, judge, hit);

    /// <summary>Per DS1 judge: its AtkParam_Npc hits as DS2 shapes, creating capsule dummies on the way.</summary>
    static Dictionary<int, (int AtkId, List<DamageShape> Shapes)> Judges(Ds1Source ds1, FLVER2 flver, int variation, IEnumerable<int> judges, List<Ds1RigModel.ExtraDummy> dummies, List<string> report)
    {
        var bp = ds1.Params.Params["BehaviorParam"]; var atk = ds1.Params.Params["AtkParam_Npc"];
        var used = flver.Dummies.Select(d => (int)d.ReferenceID).ToHashSet();
        var reserved = new HashSet<int> { 9, 220, 249, 255 };
        var pairDummy = new Dictionary<(int, int), int>();
        int nextId = 150;
        int NewDummy()
        {
            while (used.Contains(nextId) || reserved.Contains(nextId)) nextId++;
            if (nextId > 254) throw new InvalidDataException("no free dummy id ≤ 254 for a hit capsule");
            used.Add(nextId); return nextId++;
        }
        FLVER.Dummy? Dmy(int id) => flver.Dummies.FirstOrDefault(d => d.ReferenceID == id);
        var res = new Dictionary<int, (int, List<DamageShape>)>();
        foreach (int j in judges.Distinct().Order())
        {
            var b = bp[200_000_000 + variation * 1000 + j];
            if (b == null) { report.Add($"WARNING judge {j}: DS1 BehaviorParam {200_000_000 + variation * 1000 + j} missing (DS1's hit does nothing either; left out)"); res[j] = (0, []); continue; }   // session 11 (burgchr11): c2250
            int atkId = Convert.ToInt32(Cell(b, "refId"));
            var a = atk[atkId] ?? throw new InvalidDataException($"DS1 AtkParam_Npc {atkId} missing");
            var shapes = new List<DamageShape>();
            for (int h = 0; h < 4; h++)
            {
                float r = (float)D(a, $"hit{h}_Radius");
                if (r <= 0) continue;
                int d1 = Convert.ToInt32(Cell(a, $"hit{h}_DmyPoly1")), d2 = Convert.ToInt32(Cell(a, $"hit{h}_DmyPoly2"));
                var p1 = Dmy(d1); var p2 = d2 > 0 ? Dmy(d2) : null;
                if (p1 == null) { report.Add($"WARNING judge {j} hit {h}: DS1 dummy {d1} not in the model"); continue; }
                if (p2 == null || d2 == d1 || Vector3.Distance(p1.Position, p2.Position) < 1e-4f)
                {
                    if (d1 > 255) { report.Add($"WARNING judge {j} hit {h}: dummy {d1} > 255"); continue; }
                    shapes.Add(new DamageShape(RowId(bp, variation, j, h), d1, 0, r, 0));
                    continue;
                }
                if (!pairDummy.TryGetValue((d1, d2), out int dm))
                {
                    pairDummy[(d1, d2)] = dm = NewDummy();
                    dummies.Add(new Ds1RigModel.ExtraDummy(dm, p1.AttachBoneIndex, p1.Position, Vector3.Normalize(p2.Position - p1.Position)));
                }
                shapes.Add(new DamageShape(RowId(bp, variation, j, h), dm, 1, r, Vector3.Distance(p1.Position, p2.Position)));
            }
            res[j] = (atkId, shapes);
        }
        report.Add($"hit capsules: {pairDummy.Count} DS1 dummy pairs → dummies {string.Join(", ", pairDummy.Select(kv => $"{kv.Key.Item1}→{kv.Key.Item2}={kv.Value}"))}");
        return res;
    }

    // ================================================================== build

    /// <summary>Session 11 (burgchr11): dummies per DS1 reference id the model keeps: 4 (the demons' rule), fewer when DS1's model has so many
    /// dummy instances that 4 per id would pass the checked range (64; vanilla ≤ 48 on every DS2 character surveyed; the Hellkite has 446 on 151
    /// nodes): the largest k whose kept count (+ the hit-capsule dummies, + 3 DS2 body ids) stays at or below 64, at least 1 (the Asylum / Stray Demon: 4, unchanged).</summary>
    static int MaxPerDummyId(FLVER2 f, int capsules)
    {
        var counts = f.Dummies.GroupBy(d => d.ReferenceID).Select(g => g.Count()).ToList();
        for (int k = 4; k > 1; k--) if (counts.Sum(c => Math.Min(c, k)) + capsules + 3 <= 64) return k;
        return 1;
    }

    /// <summary>Session 11 (burgchr11): the template row a config param is cloned from: the one keyed by the template EnemyParam (the
    /// Smelter's, Belfry Gargoyle's …), else the one its EnemyParam names (commonId / moveId / deadType: the Giant Undead Boar 214003
    /// uses EnemyCommonParam / EnemyMoveParam / DeadTypeParam 214000), else none (the param stays unset, as before).</summary>
    public static PARAM.Row? ConfigSource(Pack pack, Spec s, string name)
    {
        if (pack.Param(name)[s.TemplateEnemyParam] is { } r) return r;
        var te = pack.Param("EnemyParam")[s.TemplateEnemyParam];
        string? field = name switch { "EnemyCommonParam" => "commonId", "EnemyMoveParam" => "moveId", "DeadTypeParam" => "deadType", _ => null };
        return te != null && field != null && te[field] is { } f && pack.Param(name)[Convert.ToInt32(f.Value)] is { } r2 ? r2 : null;
    }

    /// <summary>Rule B2c (session 11, burgchr11): the spec without the chains whose stages need more than a combo row's 4 damage slots.</summary>
    public static Spec Fitted(Spec s, Ds1ChrSource src, string ds2Data)
    {
        // and (the Undead Attack Dog on the Stray Dog c2270) a template network without a route category of the chain's length
        int maxStages = Ds1BossAnims.MaxRouteStages(File.ReadAllBytes(Path.Combine(ds2Data, "morpheme4", "chr", s.TemplateChr + ".anibnd.dcx")));
        var fit = s.Anim.Chains.Where(c => c.Length <= maxStages && c.Where(src.Has).Sum(a => Ds1BossAnims.SlotCount(Ds1BossAnims.HitGroups(src.TaeAnim(a)))) <= 4).ToArray();
        return fit.Length == s.Anim.Chains.Length ? s : s with { Anim = s.Anim with { Chains = fit } };
    }

    public static Result Build(Pack pack, string dsrRoot, Ds1Source ds1, Spec s)
    {
        var res = new Result();
        string data = pack.Ds2Data, t = s.TemplateChr, n = s.N;
        // session 11 (burgchr11), rule E-REQ: the Burg's AI requests (the Asylum's stay as built: Stray Demon command 10 → M2 events)
        if (s.Ds1Map != "m18_01_00_00") Ai.EventRequests.Register(dsrRoot, s.Ds1Map);
        var src = Ds1ChrSource.Load(dsrRoot, s.Ds1Chr);
        // session 11 (burgchr11, rule B2c): a combo row holds 4 damage slots (AttackId01-04, 2200 IDs 0-3) for all its stages; a DS1
        // chain whose stages' hit groups need more (the Titanite Demon's 3000 → 3001 → 3002: 2 + 3 + 3) is played as its single
        // attacks (the AI's chain lookup then finds no row: ComboAttack → ComboFinal become consecutive attacks), reported
        var fitted = Fitted(s, src, data);
        if (fitted.Anim.Chains.Length != s.Anim.Chains.Length)
            res.Report.Add($"rule B2c: chains over 4 damage slots or longer than the template's route categories played as single attacks: {string.Join("; ", s.Anim.Chains.Except(fitted.Anim.Chains).Select(c => string.Join("→", c)))}");
        s = fitted;
        // session 11 (chrai11), rule FA1: the clips DS1's events force on the character become attack categories of their own
        if (Ai.Boss.EventMessages.Layout(dsrRoot, s) is { } fa1)
        {
            s = s with { Anim = s.Anim with { EventClips = fa1.Category.ToDictionary(kv => kv.Value, kv => kv.Key) } };
            res.Report.Add(fa1.Note);
        }
        var chrbnd = BND3.Read(Path.Combine(dsrRoot, "chr", s.Ds1Chr + ".chrbnd.dcx"));
        byte[] Ds1File(string ext) => chrbnd.Files.First(f => f.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)).Bytes;
        var ds1Flver = FLVER2.Read(Ds1File(".flver"));
        var npc = ds1.Params.Params["NpcParam"][s.Ds1Npc] ?? throw new InvalidDataException($"DS1 NpcParam {s.Ds1Npc} missing");
        int variation = Convert.ToInt32(Cell(npc, "behaviorVariationId"));

        // ---- 1. damage plan: every DS1 judge of every attack clip
        var allJudges = s.Anim.AttackClips.Concat(s.Anim.Chains.SelectMany(c => c)).Distinct().Where(src.Has)   // session 11 (chrai11): + FA1 clips' hits
            .SelectMany(a => Ds1BossAnims.HitGroups(src.TaeAnim(a)).SelectMany(g => g.Judges)).ToList();
        var judges = Judges(ds1, ds1Flver, variation, allJudges, res.CapsuleDummies, res.Report);

        // ---- 2. animations
        // DS1 foot dummies (TAE footfall effects): dummy → foot by its attach bone (c2232: 3 R Foot, 4 L Foot)
        var footDummy = ds1Flver.Dummies.Where(d => d.AttachBoneIndex >= 0 && d.AttachBoneIndex < ds1Flver.Nodes.Count)
            .Select(d => (Id: (int)d.ReferenceID, Bone: ds1Flver.Nodes[d.AttachBoneIndex].Name.Trim().Replace('_', ' ')))   // session 11 (burgchr11): c2300 spells "R_Foot"
            .Where(t => t.Bone is "L Foot" or "R Foot").GroupBy(t => t.Id).Where(g => g.Select(t => t.Bone).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Bone[0]);
        string animChr = $"c{s.AnimId / 100:D4}";
        // merge9 stage 6: the NMB is named like its anibnd (the anim set), as in every vanilla anibnd
        Ds1RigAnims.AllowEvenSync = s.Ds1Map != "m18_01_00_00";   // session 11 (burgchr11): rule C11d for the Burg's demons only (the Asylum's unchanged)
        Ds1BossAnims.Result anims;
        try
        {
            anims = Ds1BossAnims.BuildAnibnd(File.ReadAllBytes(Path.Combine(data, "morpheme4", "chr", t + ".anibnd.dcx")), src, s.Anim, footDummy, nmbName: animChr,
                i1bReserved: Formats.Morpheme.NmbIdentity.For(data));   // session 10: rule I1b (own Morpheme ids; I1c: one registry per build)
        }
        finally { Ds1RigAnims.AllowEvenSync = false; }
        res.Anims = anims; res.Report.AddRange(anims.Report);
        res.Files[$"morpheme4/chr/{animChr}.anibnd.dcx"] = anims.Anibnd;
        string ext = Path.Combine(data, "morpheme4", "chr", "c0001", $"c0001_{t}.extanibnd.dcx");
        if (File.Exists(ext)) res.Files[$"morpheme4/chr/c0001/c0001_{animChr}.extanibnd.dcx"] = File.ReadAllBytes(ext);

        // ---- 2b. sound bank (merge9 stage 6; track chrsound rules S1/S1b/S2/S5/S9/S10): DS2 opens frpg2_c{animId/100 rounded
        // down to tens}; it holds DS1's own FMOD project (frpg_c2232 + the DS1 common sounds its TAE names, renamed to the
        // bank's resource name) and wave bank, the wave banks also loose. Replaces the boss track's copy of the template's
        // bank (sound/frpg2_{animId/100}.bnd = frpg2_c2232.bnd, a name DS2 never opened). Built before the TAEs, whose _snd
        // events name the copied common sounds.
        int bankChr = Ds1ChrSound.BankChr(s.AnimId);
        var objections = Ds1ChrSound.BankChecks(data, bankChr);
        if (objections.Count > 0)
            throw new InvalidDataException($"{s.Ds1Chr} 1:1 boss animId {s.AnimId}: sound bank {Ds1ChrSound.BankName(bankChr)}.bnd (animId/100 rounded down to tens): {string.Join("; ", objections)}. Choose an animId in a free decade, e.g. {string.Join(", ", Ds1ChrSound.FreeDecades(data, bankChr).Select(d => $"{d * 100 + 10} (bank {Ds1ChrSound.BankName(d)})"))}");
        var bank = Ds1ChrSound.Bank(dsrRoot, data, s.Ds1Chr, bankChr, t, src.Tae);
        res.Bank = bank;
        res.Files[$"sound/{Ds1ChrSound.BankName(bankChr)}.bnd"] = bank.Bank;
        foreach (var (path, bytes) in bank.Loose) res.TitleFiles[path] = bytes;
        res.Report.AddRange(bank.Report);
        int ds1ChrId = int.Parse(s.Ds1Chr[1..], System.Globalization.CultureInfo.InvariantCulture);
        var multiDummies = ds1Flver.Dummies.GroupBy(d => (int)d.ReferenceID).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

        // ---- 3. TimeAct
        float turnDefault = (float)D(npc, "turnVellocity");
        foreach (var kind in new[] { "pl", "sfx", "snd" })
        {
            int tTae = s.TemplateTae > 0 ? s.TemplateTae : s.TemplateEnemyParam;
            var tae = Ds2TaeFile.Read(File.ReadAllBytes(Path.Combine(data, "timeact", "chr", $"c{tTae:D6}_{kind}.tae")));
            var rep = BuildTae(tae, anims, src, kind == "pl", turnDefault);
            if (kind == "snd") rep += "; " + SoundTae(tae, anims, src, ds1ChrId, bank, multiDummies, res.SoundLog);
            res.Files[$"timeact/chr/c{s.AnimId:D6}_{kind}.tae"] = tae.Write();
            res.Report.Add($"c{s.AnimId:D6}_{kind}.tae (from c{tTae:D6}): {rep}");
        }

        // ---- 4. model archive: the template boss's layout, DS1 content
        var bnd = BND4.Read(Path.Combine(data, "model", "chr", t + ".bnd"));
        var matBnd = BND4.Read(Path.Combine(data, "model", "chr", s.MaterialTemplateChr + ".bnd"));
        var matFlv = FLVER2.Read(matBnd.Files.First(f => f.Name.EndsWith($"{s.MaterialTemplateChr}.flv", StringComparison.OrdinalIgnoreCase)).Bytes);
        var o = new ChrPorter.Options(s.Ds1Chr, s.Ds1TexChr, s.MaterialTemplateChr, s.Model, s.BodyMaterial, s.EdgeMaterial, [], [], []);
        // session 11 (track chrai11), rule TC1 (model side): the DS1 display group a part cut hides (DispMask bit b → "#0b#") draws under
        // a culling id of its own the weapon slot shows (the group's material's when the slot shows it, else the template slot's
        // cullingId00: c2120's tail group 1), the break swaps it for an id no mesh uses; the ragdoll bodies on the group's bones are
        // hit part 7 (rule TC1-H)
        Dictionary<string, int>? cullOv = null; Dictionary<string, ushort>? hitParts = null;
        var tailCuts = TailCuts(dsrRoot, s);
        if (tailCuts.Count > 0)
        {
            string grp = $"#{tailCuts[0].DispBit:D2}#";
            var tailBones = ds1Flver.Meshes.Where(m => ds1Flver.Materials[m.MaterialIndex].Name == grp).SelectMany(m => m.BoneIndices)
                .Where(i => i >= 0 && i < ds1Flver.Nodes.Count).Distinct().Select(i => ds1Flver.Nodes[i].Name.Trim()).ToList();
            int matCull = s.WeaponMeshes.TryGetValue(grp, out var wmat) ? Ds1RigModel.CullingOf(matFlv, wmat) : -1;
            int tplCull = pack.Param("EnemyEquipWeaponParam")[s.TemplateWeaponParam] is { } tw ? Convert.ToInt32(Cell(tw, "cullingId00")) : -1;
            int baseC = matCull > 0 && s.Culling.Contains(matCull) ? matCull : s.Culling.Length == 0 && tplCull > 0 ? tplCull : 30;
            if (matCull != baseC) cullOv = new() { [grp] = baseC };
            var usedC = new HashSet<int> { 0, baseC };
            foreach (var c in s.Culling.Concat(s.ExtraCulling.Values.SelectMany(x => x)).Concat(s.WeaponMeshes.Values.Select(m => Ds1RigModel.CullingOf(matFlv, m)))) usedC.Add(c);
            int brk = Enumerable.Range(baseC + 1, 99).First(c => !usedC.Contains(c));
            hitParts = tailBones.Select(b => "Ragdoll_" + Ds1RigAnims.Ds2BoneName(b)).Concat(tailBones.Select(b => "Ragdoll_" + b)).Distinct().ToDictionary(b => b, _ => (ushort)7);
            res.Tail = (baseC, brk, tailBones, []);
            res.Report.Add($"rule TC1: DS1 part cut ({string.Join(", ", tailCuts.Select(c => $"{c.Entity} part {c.PartId} HP {c.Hp}"))}) hides {grp} on bones {string.Join(", ", tailBones)}: culling {baseC} (shown) → {brk} at the break{(cullOv != null ? $" (the group's material draws under {matCull}: own GX list with {baseC})" : "")}");
        }
        Ds1RigModel.GimbalFix = s.Ds1Map == "m10_00_00_00";   // session 11 (dchr11): rule SL-E for the Depths' builds only
        Ds1RigModel.Result model;
        try { model = Ds1RigModel.Build(ds1Flver, matFlv, anims.Rig, anims.Ds1Names, o, s.MaterialTemplateChr, n, weaponMeshes: s.WeaponMeshes.Count > 0 ? s.WeaponMeshes : null, extraDummies: res.CapsuleDummies, ds1TextureNames: true, maxPerDummyId: MaxPerDummyId(ds1Flver, res.CapsuleDummies.Count),
            cullingOverride: cullOv); }
        finally { Ds1RigModel.GimbalFix = false; }
        if (res.Tail is { } tl)
        {
            var bonesNew = tl.Bones.Select(Ds1RigAnims.Ds2BoneName).ToHashSet();
            var dummies = model.Flver.Dummies.Where(d => d.ReferenceID is > 0 and < 256 && (d.ParentBoneIndex >= 0 && d.ParentBoneIndex < model.Flver.Nodes.Count && bonesNew.Contains(model.Flver.Nodes[d.ParentBoneIndex].Name)
                || d.AttachBoneIndex >= 0 && d.AttachBoneIndex < model.Flver.Nodes.Count && bonesNew.Contains(model.Flver.Nodes[d.AttachBoneIndex].Name))).Select(d => (int)d.ReferenceID).Distinct().Order().ToList();
            res.Tail = tl with { Dummies = dummies };
        }
        res.Model = model; res.Report.AddRange(model.Report);
        byte[] flv = model.Flver.Write();
        var (templateRagdoll, ragTemplate) = RagdollTemplate(Ds1File(".hkx"), data, t, res.Report);
        // session 11 (burgchr11): c2300's chrbnd has no .hkxpwv: the body records fall back to −1/0 (as c1030's), reported
        byte[] pwv = chrbnd.Files.FirstOrDefault(f => f.Name.EndsWith(".hkxpwv", StringComparison.OrdinalIgnoreCase))?.Bytes ?? new byte[32];
        if (pwv.Length == 32 && pwv.All(b => b == 0)) res.Report.Add("DS1 chrbnd has no .hkxpwv: ragdoll body records −1/0");
        var (rag, ragPwv, ragRep) = Ds1RigModel.Ragdoll(Ds1File(".hkx"), pwv, templateRagdoll, anims.Rig, anims.Ds1Names, hitParts);   // TC1-H
        res.Report.AddRange(ragRep);
        // The template boss's "_h" files (hit bodies bound to ITS bones; 110 of 201 DS2 characters have them, the Hollow
        // Infantry template of c2501 not) are left out: DS1's hurt capsules are its ragdoll bodies (rule C16), which DS2
        // uses when there is no _h file (c2501, s8-7: hit for 58). First suspect if hits on the demon do not register.
        int dropped = bnd.Files.RemoveAll(f => Path.GetFileName(f.Name).Contains("_h.hkx", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f.Name).Contains("_Cloth", StringComparison.OrdinalIgnoreCase));
        var texNames = model.TexUse.Keys.Order(StringComparer.Ordinal).ToList();
        foreach (var f in bnd.Files)
        {
            string name = Path.GetFileName(f.Name);
            if (name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase)) f.Bytes = flv;
            else if (name.EndsWith(".acb", StringComparison.OrdinalIgnoreCase)) f.Bytes = Acb(f.Bytes, t, n, texNames);
            else if (name.EndsWith("_Ragdoll.hkx", StringComparison.OrdinalIgnoreCase)) f.Bytes = rag;
            else if (name.EndsWith("_Ragdoll.hkxpwv", StringComparison.OrdinalIgnoreCase)) f.Bytes = ragPwv;
            else if (name.EndsWith(".flvpwv", StringComparison.OrdinalIgnoreCase)) f.Bytes = model.Flvpwv;
            else f.Bytes = ChrPorter.ReplaceAscii(f.Bytes, t, n);
            f.Name = f.Name.Replace(t, n);
        }
        res.Files[$"model/chr/{n}.bnd"] = bnd.Write();
        var ds1Tex = ChrPorter.LoadDs1Textures(dsrRoot, o);
        // session 11 (ret11), rule TX1: DS1's NpcParam.normalChangeTexChrId swaps the model's body texture set for the change
        // chr's: a texture the FLVER names after another chr's set (c2230's materials name c2239 = the demons' shared set) that
        // the texture chr (c2231) holds under its own prefix with the same suffix is that one (c2239_n → c2231_n). The Asylum
        // Demon's set is c2239 itself (no change).
        foreach (var (ours, ds1Name) in model.TexUse.ToList())
        {
            if (ds1Tex.ContainsKey(ds1Name)) continue;
            var m = System.Text.RegularExpressions.Regex.Match(ds1Name, @"^c\d{4}(.*)$");
            if (m.Success && ds1Tex.ContainsKey(s.Ds1TexChr + m.Groups[1].Value))
            {
                model.TexUse[ours] = s.Ds1TexChr + m.Groups[1].Value;
                res.Report.Add($"rule TX1: texture {ds1Name} → {model.TexUse[ours]} (NpcParam normalChangeTexChrId {s.Ds1TexChr[1..]})");
            }
        }
        var missing = model.TexUse.Values.Where(v => !ds1Tex.ContainsKey(v)).Distinct().ToList();
        if (missing.Count > 0) throw new InvalidDataException($"{s.Ds1Chr}: DS1 textures not found: {string.Join(", ", missing)} (have {string.Join(", ", ds1Tex.Keys.Order())})");
        res.Files[$"model/chr/{n}.texbnd"] = Texbnd(Path.Combine(data, "model", "chr", s.MaterialTemplateChr + ".texbnd"), model.TexUse, ds1Tex);
        res.Report.Add($"archive: {bnd.Files.Count} files ({dropped} template hit-body/cloth files left out); textures: {string.Join(", ", model.TexUse.Select(kv => $"{kv.Key}←{kv.Value}"))}");

        // ---- 5. params
        Params(pack, ds1, s, npc, variation, anims, judges, src, res);
        Hb1(pack, dsrRoot, ds1, s, npc, variation, ds1Flver, res);    // session 11 (chrai11): rule HB1
        Tc1(pack, dsrRoot, ds1, s, res);                               // session 11 (chrai11): rule TC1 (params; model side below)

        // ---- 6. AI (step 2): DS1's battle goal translated into the lower logic, the template boss's upper logic kept
        if (s.Ai is { } ai)
        {
            var ar = Ai.Boss.AiPorter.Build(pack, data, ds1, ai, new AiContext(s, src, ds1Flver, ds1, dsrRoot));
            foreach (var (k, v) in ar.Files) res.Files[k] = v;
            res.Report.AddRange(ar.Report.Select(r => "AI: " + r));
            res.Ai = ar;
        }
        return res;
    }

    /// <summary>
    /// The class template for converting DS1's ragdoll (rule as for object collisions, rulebook §3: a template must hold
    /// every class the file uses): the template boss's own ragdoll if it has them all, else the first DS2 character ragdoll
    /// that does (c2232's uses hkpHingeConstraintData, which the Smelter's lacks).
    /// </summary>
    static (HkPackfile Template, string Chr) RagdollTemplate(byte[] ds1Hkx, string data, string preferred, List<string> report)
    {
        var tag = new HkTagfile(ds1Hkx);
        // hkpHingeConstraintData converts as hkpLimitedHingeConstraintData (Ds1RigModel.Ragdoll)
        var need = tag.Items.Where(i => i.IsPtr && i.Type != null).Select(i => i.Type!.Name.Replace("::", "")).Where(c => c != "hkpConvexVerticesConnectivity")
            .Select(c => c is "hkpHingeConstraintData" or "hkpBallAndSocketConstraintData" ? "hkpLimitedHingeConstraintData" : c).ToHashSet();   // session 11 (burgchr11): + ball-and-socket
        var candidates = new[] { preferred }.Concat(Directory.GetFiles(Path.Combine(data, "model", "chr"), "c*.bnd").Select(f => Path.GetFileNameWithoutExtension(f)).Order(StringComparer.Ordinal)).Distinct();
        foreach (var c in candidates)
        {
            string p = Path.Combine(data, "model", "chr", c + ".bnd");
            if (!File.Exists(p)) continue;
            var f = BND4.Read(p).Files.FirstOrDefault(x => x.Name.EndsWith("_Ragdoll.hkx", StringComparison.OrdinalIgnoreCase));
            if (f == null) continue;
            var hk = new HkPackfile(f.Bytes);
            if (need.All(hk.Classes.ContainsKey))
            {
                report.Add($"ragdoll class template: {c}_Ragdoll.hkx (has all {need.Count} classes of DS1's file: {string.Join(", ", need.Order())}){(c == preferred ? "" : $"; {preferred}'s lacks {string.Join(", ", need.Where(n => !new HkPackfile(BND4.Read(Path.Combine(data, "model", "chr", preferred + ".bnd")).Files.First(x => x.Name.EndsWith("_Ragdoll.hkx", StringComparison.OrdinalIgnoreCase)).Bytes).Classes.ContainsKey(n)))}")}");
                return (hk, c);
            }
        }
        throw new InvalidDataException($"no DS2 character ragdoll holds every class of the DS1 ragdoll: {string.Join(", ", need)}");
    }

    /// <summary>The template's ACB with ids replaced, the dropped files' assets removed and the texture assets = ours.</summary>
    static byte[] Acb(byte[] bytes, string t, string n, List<string> textures)
    {
        var acb = ACB.Read(ChrPorter.ReplaceAscii(bytes, t, n));
        acb.Assets.RemoveAll(a => a.AbsolutePath.Contains("_h.", StringComparison.OrdinalIgnoreCase) || a.AbsolutePath.Contains("_Cloth", StringComparison.OrdinalIgnoreCase));
        var proto = acb.Assets.OfType<ACB.Asset.Texture>().FirstOrDefault();
        int at = proto != null ? acb.Assets.IndexOf(proto) : acb.Assets.Count;
        acb.Assets.RemoveAll(a => a is ACB.Asset.Texture);
        if (proto != null)
        {
            string Swap(string p, string tex)
            {
                if (string.IsNullOrEmpty(p)) return p;
                int cut = Math.Max(p.LastIndexOf('\\'), p.LastIndexOf('/')) + 1;
                return p[..cut] + tex + Path.GetExtension(p);
            }
            acb.Assets.InsertRange(Math.Min(at, acb.Assets.Count), textures.Select(tex => (ACB.Asset)new ACB.Asset.Texture { AbsolutePath = Swap(proto.AbsolutePath, tex), RelativePath = Swap(proto.RelativePath, tex) }));
        }
        return acb.Write();
    }

    /// <summary>The texture archive: one TPF per texture (DS2's texbnd layout, e.g. c1030.texbnd), named
    /// <c>NAME.tpf</c>, DS1 pixels converted by kind (_n normal, _s specular, else colour).</summary>
    internal static byte[] Texbnd(string templatePath, Dictionary<string, string> texUse, Dictionary<string, TPF.Texture> ds1Tex)   // session 11 (ret11): also the regulars' rule MT1
    {
        var tb = BND4.Read(templatePath);
        var proto = tb.Files[0];
        var tpfProto = TPF.Read(proto.Bytes);
        var files = new List<BinderFile>();
        int id = 0;
        foreach (var (name, ds1Name) in texUse.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var src = ds1Tex[ds1Name];
            string kind = name[^2..].ToLowerInvariant();
            var px = kind switch
            {
                "_n" => (Ds2Tex.Normal(src.Bytes), Ds2Tex.FormatNormal),
                "_s" => (Ds2Tex.Specular(src.Bytes), Ds2Tex.FormatDxt1),
                _ => Ds2Tex.Color(src.Bytes),
            };
            var tpf = TPF.Read(proto.Bytes);
            var tex = tpf.Textures[0];
            tpf.Textures.Clear(); tpf.Textures.Add(tex);
            tex.Name = name; tex.Bytes = px.Item1; tex.Format = px.Item2; tex.Mipmaps = (byte)Ds2Tex.MipCount(px.Item1);
            string dir = proto.Name[..(Math.Max(proto.Name.LastIndexOf('\\'), proto.Name.LastIndexOf('/')) + 1)];
            files.Add(new BinderFile(proto.Flags, id++, dir + name + ".tpf", tpf.Write()));
        }
        tb.Files = files;
        return tb.Write();
    }

    /// <summary>Session 11 (fx11), rule B-BUL: a DS1 clip's bullet events (TAE type 2: dummy at +0, behaviour judge at +8) as
    /// (judge, dummy, slot), slot = the judge's index among the clip's distinct bullet judges; with the event times for the TAE.</summary>
    public static List<(int Judge, int Dummy, int Slot)> BulletSlots(TAE.Animation? a) =>
        [.. BulletEvents(a).GroupBy(b => b.Judge).Select((g, i) => (g.Key, g.First().Dummy, i))];

    public static List<(double S, double E, int Dummy, int Judge)> BulletEvents(TAE.Animation? a) =>
        a == null ? [] : [.. a.Events.Where(e => e.Type == 2 && e.GetParameterBytes(false).Length >= 12)
            .Select(e => { var p = e.GetParameterBytes(false); return ((double)e.StartTime, (double)e.EndTime, BitConverter.ToInt32(p, 0), BitConverter.ToInt32(p, 8)); })
            .OrderBy(b => b.Item1)];

    // ================================================================== TimeAct (rule B5)

    /// <summary>
    /// The TimeAct files (rule B5): non-attack TAE ids as the Hollow (length = DS1 frames at 30 fps; _pl events time-scaled by
    /// DS1/DS2 duration, dropped on no-attack slots; _sfx/_snd events dropped: DS1 effects not translated (S7); DS1 sounds are
    /// added to _snd afterwards by <see cref="SoundTae"/>, merge9 stage 6). Attack
    /// TAE ids (one per attack leaf, new or reused) hold DS1's own events only, in _pl: DS1 "Attack Behavior" hit groups →
    /// 2200 (ID = damage slot); DS1 turning → 280100 "Set Character Turn Rate" (deg/s) over the clip: 0 in DS1 "Disable
    /// Turning" (JT 7) windows, DS1 "Set Turn Speed" (224) values in theirs, NpcParam.turnVellocity elsewhere.
    /// </summary>
    static string BuildTae(Ds2TaeFile tae, Ds1BossAnims.Result anims, Ds1ChrSource src, bool gameplay, float turnDefault)
    {
        int lengths = 0, scaled = 0, cleared = 0, attack = 0, created = 0, hits = 0, turns = 0, bullets = 0, noFall = 0;
        var p2200 = tae.Animations.SelectMany(a => a.Events).FirstOrDefault(e => e.Type == 2200)?.Params ?? new byte[24];
        var p2300 = tae.Animations.SelectMany(a => a.Events).FirstOrDefault(e => e.Type == 2300)?.Params ?? new byte[p2200.Length];   // fx11, rule B-BUL
        var p280100 = tae.Animations.SelectMany(a => a.Events).FirstOrDefault(e => e.Type == 280100)?.Params ?? new byte[24];
        var protoAnim = tae.Animations.FirstOrDefault(a => a.Id >= 65_000_000) ?? tae.Animations[0];
        foreach (var (id, (d1, d2, noAttack)) in anims.TaeIds)
        {
            var a = tae.Get(id);
            if (a == null)
            {
                // a TimeAct id the template's TAE lacks (c305000 has no 79020010-79040010 / 75017100-01: DS2 tolerates it);
                // given an empty animation with DS1's length so every id the network names exists
                a = new Ds2TaeFile.Animation { Id = id, FileType = protoAnim.FileType, FileTail = (byte[])protoAnim.FileTail.Clone() };
                tae.Animations.Add(a); created++;
            }
            a.Frames = (int)Math.Round(d1 * 30); lengths++;
            if (a.Events.Count == 0) continue;
            if (!gameplay || noAttack) { Ds2TaeFile.RemoveEvents(a, _ => true); cleared++; }
            else if (d2 > 0) { foreach (var e in a.Events) { e.Start = (float)(e.Start * d1 / d2); e.End = (float)(e.End * d1 / d2); } scaled++; }
        }
        foreach (var (id, (ds1Id, slotOffset, d1)) in anims.AttackTae)
        {
            var a = tae.Get(id);
            if (a == null)
            {
                a = new Ds2TaeFile.Animation { Id = id, FileType = protoAnim.FileType, FileTail = (byte[])protoAnim.FileTail.Clone() };
                tae.Animations.Add(a); created++;
            }
            a.NameBytes = [];
            a.Frames = (int)Math.Round(d1 * 30);
            Ds2TaeFile.RemoveEvents(a, _ => true);
            attack++;
            if (!gameplay) continue;
            var ta = src.TaeAnim(ds1Id);
            var groups = Ds1BossAnims.HitGroups(ta);
            for (int g = 0; g < groups.Count; g++)
            {
                var pr = (byte[])p2200.Clone();
                BitConverter.GetBytes(slotOffset + groups[g].Slot).CopyTo(pr, 0);   // rule B4b
                Ds2TaeFile.AddEvent(a, 2200, (float)groups[g].Start, (float)groups[g].End, pr);
                hits++;
            }
            // session 11 (fx11), rule B-BUL: DS1's bullet events → 2300 (ID = the bullet slot after the stage's slot offset, as
            // Params sets BulletId; the regulars' Ds1Attacks writes the same event)
            var slots = BulletSlots(ta).ToDictionary(b => b.Judge, b => b.Slot);
            foreach (var b in BulletEvents(ta))
            {
                var pr = (byte[])p2300.Clone();
                BitConverter.GetBytes(slotOffset + slots[b.Judge]).CopyTo(pr, 0);
                Ds2TaeFile.AddEvent(a, 2300, (float)b.S, (float)b.E, pr);
                bullets++;
            }
            foreach (var (st, en, rate) in TurnSegments(ta, d1, turnDefault))
            {
                var pr = (byte[])p280100.Clone();
                BitConverter.GetBytes(rate).CopyTo(pr, 0);
                Ds2TaeFile.AddEvent(a, 280100, (float)st, (float)en, pr);
                turns++;
            }
            // session 11 (track leap12), rule LP1 as on the regulars (Ds1Attacks.NoFallEvents): DS1 JT 51 "Disable Fall Animation"
            // → 280000 (the Capra Demon's 3004, the Titanite Demon's leaps 3007 / 3008 at 22-30 m/s, the Taurus Demon's 3009, …)
            foreach (var e in Ds1Attacks.NoFallEvents(Ds1RigAnims.JumpTable(ta, 51), d1))
            {
                Ds2TaeFile.AddEvent(a, e.Type, e.Start, e.End, e.Params);
                noFall++;
            }
        }
        return $"{lengths} lengths set, {scaled} animations' events time-scaled, {cleared} cleared; {attack} attack animations with DS1's events: {hits} hits (2200), {bullets} bullets (2300, rule B-BUL), {turns} turn-rate segments (280100), {noFall} no-fall windows (280000, rule LP1); {created} animations created (new attack ids + TimeAct ids the template lacks)";
    }

    /// <summary>
    /// merge9 stage 6 (boss × chrsound): the _snd TAE gets the DS1 clips' own sound events (chrsound rules S3-S6,
    /// <see cref="Ds1ChrSound.Translate"/>, as Ds1RigAnims.EditTae does for the regulars): each TAE id a clip slot names gets the
    /// events of the DS1 clip that slot plays (loops moved with their re-cut, S6), each attack TAE id those of its DS1 attack
    /// clip (whole from frame 0, DS1's times); DS2 201 with the whole DS1 id (+13 = 1, so the name is DS1's whatever the
    /// animId), unk 1, group type 200. <see cref="BuildTae"/> has already removed the template's events on those ids. The
    /// template's (Smelter's) 201/202 sound events on the TAE ids the network no longer names are removed too: written
    /// without +13 they name c{decade}xxxxx events, which no loaded project defines once the decade is ours (rule S5b's
    /// principle: no 201 names an event no loaded project has; with the old animId they named DS2's own c2230 sounds).
    /// <see cref="Ds2TaeFile"/> rebuilds the vanilla contiguous layout (not chrsound's appended event blocks).
    /// </summary>
    static string SoundTae(Ds2TaeFile tae, Ds1BossAnims.Result anims, Ds1ChrSource src, int ds1Chr, Ds1ChrSound.BankResult bank, IReadOnlySet<int> multiDummies, List<string> log)
    {
        int withEvents = 0, events = 0, own = 0, common = 0, unknown = 0, attackAnims = 0, attackWith = 0, strippedEvents = 0, strippedAnims = 0;
        var done = new HashSet<long>();
        void Put(long id, Ds1RigAnims.ClipInfo c, int shift)
        {
            var a = tae.Get(id) ?? throw new InvalidDataException($"_snd TAE: animation {id} missing (BuildTae creates every slot / attack id)");
            done.Add(id);
            Ds2TaeFile.RemoveEvents(a, e => e.Type is 201 or 202);
            var t = c.Ds1Id >= 0 ? Ds1ChrSound.Translate(src.TaeAnim(c.Ds1Id), ds1Chr, shift, c.Frames, c.Fps, bank.Commons, multiDummies: multiDummies, ownEvents: bank.OwnEvents) : new Ds1ChrSound.Translation();
            foreach (var e in t.Snd)
            {
                Ds2TaeFile.AddEvent(a, e.Type, e.Start, e.End, e.Params);
                a.Events[^1].Unk04 = e.Unk04;
            }
            if (t.Snd.Count > 0) { withEvents++; events += t.Snd.Count; }
            own += t.Own; common += t.Common; unknown += t.Unknown;
            if (t.Snd.Count + t.Common + t.Unknown > 0)
            {
                string from = c.Ds1Id >= 0 ? Ds1ChrSource.ClipName(c.Ds1Id) : "bind";
                var shown = t.Snd.Select(e => $"{Ds1ChrSound.Ds2EventName(e.Params)}@{e.Start.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}").Concat(t.Notes.Where(n => !n.StartsWith("FFX")));
                log.Add($"snd {id} <- {from}{(shift != 0 ? $" (loop re-cut at DS1 frame {shift})" : "")}: {string.Join("; ", shown)}");
            }
        }
        foreach (var (id, c) in anims.TaeSource.OrderBy(kv => kv.Key)) Put(id, c, c.Shift);
        foreach (var (id, c) in anims.AttackSource.OrderBy(kv => kv.Key))
        {
            int before = withEvents;
            Put(id, c, 0);
            attackAnims++; if (withEvents > before) attackWith++;
        }
        foreach (var a in tae.Animations.Where(a => !done.Contains(a.Id)))
        {
            int n = a.Events.Count(e => e.Type is 201 or 202);
            if (n == 0) continue;
            Ds2TaeFile.RemoveEvents(a, e => e.Type is 201 or 202);
            strippedEvents += n; strippedAnims++;
        }
        return $"DS1 sound events → 201 on {withEvents} animations (incl. {attackWith} of the {attackAnims} attack animations): {events} events ({own} DS1 character sounds, {common} DS1 common sounds copied into the project, rule S5; {unknown} not played); template sound events removed from {strippedAnims} TAE ids the network no longer names ({strippedEvents} events)";
    }

    /// <summary>DS1 turning over a clip as (start, end, deg/s) segments: JT 7 "Disable Turning" → 0, TAE 224 "Set Turn Speed"
    /// → its value, otherwise the NpcParam's turn velocity; equal neighbours merged.</summary>
    public static List<(double S, double E, float Rate)> TurnSegments(TAE.Animation? a, double dur, float dflt)
    {
        var off = Ds1RigAnims.JumpTable(a, 7);
        var set = a?.Events.Where(e => e.Type == 224).Select(e => ((double)e.StartTime, (double)e.EndTime, BitConverter.ToSingle(e.GetParameterBytes(false), 0))).ToList() ?? [];
        var cuts = new SortedSet<double> { 0, dur };
        foreach (var (s, e) in off) { cuts.Add(Math.Clamp(s, 0, dur)); cuts.Add(Math.Clamp(e, 0, dur)); }
        foreach (var (s, e, _) in set) { cuts.Add(Math.Clamp(s, 0, dur)); cuts.Add(Math.Clamp(e, 0, dur)); }
        var c = cuts.ToList();
        var o = new List<(double, double, float)>();
        for (int i = 0; i + 1 < c.Count; i++)
        {
            double s = c[i], e = c[i + 1], mid = (s + e) / 2;
            if (e - s < 1e-6) continue;
            float r = off.Any(w => w.S <= mid && mid < w.E) ? 0f : set.Where(w => w.Item1 <= mid && mid < w.Item2).Select(w => w.Item3).DefaultIfEmpty(dflt).First();
            if (o.Count > 0 && o[^1].Item3 == r && Math.Abs(o[^1].Item2 - s) < 1e-6) o[^1] = (o[^1].Item1, e, r);
            else o.Add((s, e, r));
        }
        return o;
    }

    // ================================================================== params

    static void Params(Pack pack, Ds1Source ds1, Spec s, PARAM.Row npc, int variation, Ds1BossAnims.Result anims,
        Dictionary<int, (int AtkId, List<DamageShape> Shapes)> judges, Ds1ChrSource src, Result res)
    {
        var atkP = ds1.Params.Params["AtkParam_Npc"];
        // session 11 (burgchr11, rule C-AR for bosses): the resident SpEffects' attack rates, as the regulars'
        var atkRates = new[] { "physicsAttackPowerRate", "magicAttackPowerRate", "fireAttackPowerRate", "thunderAttackPowerRate", "staminaAttackRate" }.Select(f => Ds1AttackParams.ResidentRate(ds1, npc, f)).ToArray();
        // ---- EnemyDamageParam: one chain per DS1 hit group
        var dmg = pack.Param("EnemyDamageParam");
        var tplDmg = dmg[130500110] ?? dmg.Rows.First();
        var slotRow = new Dictionary<(int Ds1Id, int Group), int>();
        foreach (int a in s.Anim.AttackClips.Concat(s.Anim.Chains.SelectMany(c => c)).Distinct().Where(src.Has))   // session 11 (chrai11): + FA1 clips
        {
            var groups = Ds1BossAnims.HitGroups(src.TaeAnim(a));
            for (int g = 0; g < Ds1BossAnims.SlotCount(groups); g++)   // rule B4b: per damage slot (the union of its windows' judges)
            {
                var shapes = groups.Where(x => x.Slot == g).SelectMany(x => x.Judges).Distinct().SelectMany(j => judges[j].Shapes.Select(sh => (Judge: j, sh))).ToList();
                if (shapes.Count == 0) continue;
                int group = shapes[0].sh.Row;
                for (int k = 0; k < shapes.Count; k++)
                {
                    var (j, sh) = shapes[k];
                    if (dmg[sh.Row] == null)
                    {
                        var row = pack.AddRow("EnemyDamageParam", tplDmg, sh.Row, $"[Lordran] {s.Ds1Chr} judge {j}");
                        var ap = atkP[judges[j].AtkId]!;
                        // session 11 (burgchr11, rule E9u): the regulars' calibration (Ds1Calibration: damage ×1.46 incl. DS1's magic / fire /
                        // lightning parts, stamina ×13, knockback ×0.5/0.45, resident attack rates C-AR) and DS1's own damage category (E9c)
                        // instead of the boss track's damage ×1 / stamina ×10 / knockback ×2.78 and the Smelter's poise-9999 knock-down category
                        Ds1AttackParams.AtkToDamage(ap, row, null, res.Report, $"{s.Ds1Chr} judge {j}", atkRates, pack, $"{s.Ds1Chr} 1:1 boss");
                        Set(row, "hitDummyPolyType", 0); Set(row, "hitDummyPolyId", sh.Dummy); Set(row, "hitModelType", sh.ModelType);
                        Set(row, "radius", sh.Radius); Set(row, "length", sh.Length);
                        Set(row, "traceSfx", 0);
                        res.DamageRows.Add((sh.Row, $"DS1 {a} judge {j} (AtkParam_Npc {judges[j].AtkId}): phys {D(ap, "atkPhys")}, stamina {D(ap, "atkStam")}, {(sh.ModelType == 1 ? $"capsule dummy {sh.Dummy} r {sh.Radius} len {sh.Length:0.00}" : $"sphere dummy {sh.Dummy} r {sh.Radius}")}"));
                    }
                    var r2 = dmg[sh.Row]!;
                    Set(r2, "damageGroup", group);
                    Set(r2, "childDamage", k + 1 < shapes.Count ? shapes[k + 1].sh.Row : 0);
                }
                slotRow[(a, g)] = group;   // rule B4b: windows sharing a slot share its row
            }
        }
        res.Report.Add($"EnemyDamageParam: {res.DamageRows.Count} rows (template {tplDmg.ID}), {slotRow.Count} DS1 hit groups");
        int Slot(int a, int g) => slotRow.TryGetValue((a, g), out int r) ? r : -1;
        // session 11 (fx11), rule B-BUL: the DS1 bullet behaviour's rows (shared with the regulars: rule E10b)
        var bulletLog = new Ds1AttackParams.Written();
        (int Bullet, int Damage)? Bullet(int judge, int dummy, double window = 0)   // session 11 (dchr11): + the TAE window (rule CU2)
        {
            var be = ds1.Params.Params["BehaviorParam"][200000000 + variation * 1000 + judge];
            if (be == null || Convert.ToInt32(be["refType"].Value) != 1 || ds1.Params.Params["Bullet"][Convert.ToInt32(be["refId"].Value)] == null)
            {
                res.Report.Add($"rule B-BUL: {s.Ds1Chr} bullet judge {judge}: DS1 has no Bullet row behind BehaviorParam {200000000 + variation * 1000 + judge} (DS1 fires nothing)");
                return null;
            }
            return Ds1AttackParams.BulletRows(pack, ds1, variation, judge, dummy, null, bulletLog, atkRates, $"{s.Ds1Chr} 1:1 boss", window);
        }

        // ---- behaviour rows (actions 0-29 = DS1 A − 3000)
        var behParams = new[] { "EnemyBehaviorParam", "EnemyBehaviorSecondParam", "EnemyBehaviorThirdParam" };
        foreach (var bpName in behParams)
        {
            var p = pack.Param(bpName);
            var tpl = p[s.TemplateEnemyParam] ?? p.Rows.First();
            var row = pack.AddRow(bpName, tpl, s.BehaviorId, $"[Lordran] {s.Ds1Chr} actions");
            int baseN = Array.IndexOf(behParams, bpName) * 10;
            for (int k = 0; k < 10; k++)
            {
                // session 11 (chrai11), rule FA1: a category may hold an event-forced clip instead of DS1 attack 3000 + n
                int? ca = s.Anim.ClipOf(baseN + k);
                int a = ca ?? 3000 + baseN + k;
                bool has = ca != null && src.Has(a);
                var groups = has ? Ds1BossAnims.HitGroups(src.TaeAnim(a)) : [];
                for (int d = 0; d < 3; d++) Set(row, $"action{k}DamageId0{d}", d < Ds1BossAnims.SlotCount(groups) ? Slot(a, d) : -1);
                Set(row, $"action{k}BladeCtrlType", has ? 0 : 4);
                if (has)
                {
                    // DS1's bosses grab nothing here (grabs: their own rules)
                    for (int d = 0; d < 3; d++) { Set(row, $"action{k}GrabDamageId0{d}", -1); Set(row, $"action{k}BulletId0{d}", -1); }
                    // session 11 (fx11), rule B-BUL (user: "Hellkite fire, Gargoyle fire breath … are doing that"): the clip's DS1
                    // bullets (TAE type 2 → BehaviorParam refType 1) fire as the regulars' do (Ds1AttackParams.BulletRows, rule E10b),
                    // bullet slot = the order of the clip's distinct bullet judges (TAE 2300 ID, BuildTae); a slot without a hit
                    // window carries the bullet's own damage row (DamageId0s = BulletId0s, as DS2's Hollow Soldier firebomb)
                    foreach (var (judge, dummy, slot) in BulletSlots(src.TaeAnim(a)))
                    {
                        if (slot > 2) { res.Report.Add($"rule B-BUL: {s.Ds1Chr} {a} bullet judge {judge}: slot {slot} > 2 (BulletId00-02), left out"); continue; }
                        if (Bullet(judge, dummy, BulletEvents(src.TaeAnim(a)).Where(e => e.Judge == judge).Max(e => e.E - e.S)) is not { } bd) continue;
                        Set(row, $"action{k}BulletId0{slot}", bd.Bullet);
                        if (slot >= Ds1BossAnims.SlotCount(groups)) Set(row, $"action{k}DamageId0{slot}", bd.Damage);
                        res.Report.Add($"rule B-BUL: action {baseN + k} (DS1 {a}) BulletId0{slot} = {bd.Bullet} (DS1 judge {judge}, dummy {dummy})");
                    }
                    Set(row, $"action{k}AttackRange", s.Anim.EventClips.Values.Contains(a) ? 99f : s.AttackRange.GetValueOrDefault(a, 2f));   // FA1: no range
                    Set(row, $"fAction{k}AttackBackSwingSpeed", 1f); Set(row, $"fAction{k}AttackFollowSwingSpeed", 1f);
                    Set(row, $"fAction{k}AttackEstimateFutureTime", 0f);   // session 11 G2 EF0: DS1 aims at the present position
                }
            }
        }
        // ---- combo rows: 0..k = DS1 chains (rule B2), then interim singles
        {
            var p = pack.Param("EnemyComboBehaviorParam");
            var row = pack.AddRow("EnemyComboBehaviorParam", p[s.TemplateEnemyParam] ?? p.Rows.First(), s.BehaviorId, $"[Lordran] {s.Ds1Chr} chains");
            var combos = s.Anim.Chains.Select((c, k) => (Motion: 30 + k, Clips: c)).Concat(s.InterimCombos.Select(a => (Motion: a - 3000, Clips: new[] { a }))).Take(7).ToList();
            for (int k = 0; k < 7; k++)
            {
                if (k >= combos.Count) { Set(row, $"combo{k}BladeCtrlType", 4); continue; }
                var (motion, clips) = combos[k];
                Set(row, $"combo{k}AttackMotionId", motion);
                for (int r = 1; r <= 3; r++) Set(row, $"combo{k}ComboAttackRoute{r}", r < clips.Length ? 1 : 0);
                Set(row, $"combo{k}BladeCtrlType", 0);
                var ids = clips.SelectMany(a => Ds1BossAnims.HitGroups(src.TaeAnim(a)).Select(gr => gr.Slot).Distinct().Select(sl => Slot(a, sl))).ToList();
                for (int d = 1; d <= 4; d++) Set(row, $"combo{k}AttackId0{d}", d <= ids.Count ? ids[d - 1] : -1);
                // session 11 (fx11), rule B-BUL: the stages' DS1 bullets (2300 ID = the stage's slot offset + the bullet's slot, as BuildTae);
                // the template's own combo bullets are cleared (DS2 content DS1's chains do not fire)
                for (int d = 1; d <= 4; d++) Set(row, $"combo{k}BulletId0{d}", -1);
                int off = 0;
                foreach (int a in clips.Where(src.Has))
                {
                    foreach (var (judge, dummy, slot) in BulletSlots(src.TaeAnim(a)))
                    {
                        int sl = off + slot;
                        if (sl > 3) { res.Report.Add($"rule B-BUL: combo {k} DS1 {a} bullet judge {judge}: slot {sl} > 3 (BulletId01-04), left out"); continue; }
                        if (Bullet(judge, dummy, BulletEvents(src.TaeAnim(a)).Where(e => e.Judge == judge).Max(e => e.E - e.S)) is not { } bd) continue;
                        Set(row, $"combo{k}BulletId0{sl + 1}", bd.Bullet);
                        if (Convert.ToInt32(row[$"combo{k}AttackId0{sl + 1}"]!.Value) <= 0) Set(row, $"combo{k}AttackId0{sl + 1}", bd.Damage);
                        res.Report.Add($"rule B-BUL: combo {k} stage DS1 {a} BulletId0{sl + 1} = {bd.Bullet} (DS1 judge {judge}, dummy {dummy})");
                    }
                    off += Ds1BossAnims.SlotCount(Ds1BossAnims.HitGroups(src.TaeAnim(a)));
                }
                for (int d = 1; d <= 4; d++)
                {
                    Set(row, $"combo{k}AttackRange0{d}", s.AttackRange.GetValueOrDefault(clips[Math.Min(d - 1, clips.Length - 1)], 2f));
                    Set(row, $"fCombo{k}AttackBackSwingSpeed0{d}", 1f); Set(row, $"fCombo{k}AttackFollowSwingSpeed0{d}", 1f); Set(row, $"fCombo{k}EstimateFutureTime0{d}", 0f);
                }
            }
            res.Report.Add($"EnemyComboBehaviorParam {s.BehaviorId}: " + string.Join("; ", combos.Select((c, k) => $"combo {k} motion {c.Motion} [{string.Join("→", c.Clips)}]{(k >= s.Anim.Chains.Length ? " (interim)" : "")}")));
        }
        foreach (var n in bulletLog.Notes) res.Report.Add($"rule B-BUL: {n}");
        // ---- weapon (slot param; behaviorId → our behaviour rows; the template's resident lava SFX off)
        {
            var w = pack.AddRow("EnemyEquipWeaponParam", pack.Param("EnemyEquipWeaponParam")[s.TemplateWeaponParam]!, s.WeaponParam, $"[Lordran] {s.Ds1Chr} weapon slot");
            Set(w, "behaviorId", s.BehaviorId);
            for (int k = 0; k < 4; k++) Set(w, $"residentSfxId0{k}", 0);
            Set(w, "emissiveType", 0);
            // session 11 (burgchr11): the DS1 display groups this variant shows (NpcParam.modelDispMask → culling ids)
            if (s.Culling.Length > 0) for (int k = 0; k < 2; k++) Set(w, $"cullingId0{k}", k < s.Culling.Length ? s.Culling[k] : 0);
        }
        // ---- config rows: model-keyed and variant-keyed rows of the template, cloned to our ids
        int tModel = int.Parse(s.TemplateChr[1..]);
        int clones = 0;
        foreach (var name in new[] { "DamageDeadMotionWeightParam", "DamageMotionLadderWeightParam", "DamageMotionRopeWeightParam", "DamageMotionWeightParam", "DeadDamageMotionWeightParam" })
            if (pack.HasParam(name) && pack.Param(name)[tModel] is { } r && pack.Param(name)[s.Model] == null) { pack.AddRow(name, r, s.Model, $"[Lordran] {s.Ds1Chr} 1:1"); clones++; }
        foreach (var name in new[] { "BossParam", "CameraExFollowParam", "DeadTypeParam", "EnemyCommonParam", "EnemyMoveParam", "LockOnParam" })
            foreach (int id in new[] { s.EnemyParam, s.AnimId })
                if (pack.HasParam(name) && ConfigSource(pack, s, name) is { } r && pack.Param(name)[id] == null) { pack.AddRow(name, r, id, $"[Lordran] {s.Ds1Chr} 1:1"); clones++; }
        // the character capsule from DS1 (NpcParam hitHeight / hitRadius: c2232 7.5 / 2.5 m; the Smelter's EnemyCommonParam
        // has 4.5 / 1.4): EnemyCommonParam fHitHeight / fHitRadius / fChrHeight (DS2's Hollow Infantry 1.7 / 0.35)
        foreach (int id in new[] { s.EnemyParam, s.AnimId })
            if (pack.Param("EnemyCommonParam")[id] is { } cp)
            {
                Set(cp, "fHitHeight", (float)D(npc, "hitHeight")); Set(cp, "fChrHeight", (float)D(npc, "hitHeight"));
                Set(cp, "fHitRadius", (float)D(npc, "hitRadius"));
            }
        // session 11 (track enemy11, G11 rule TURN-RATE, M2 / B7): every non-zero rotation speed of the cloned EnemyMoveParam =
        // DS1 NpcParam.turnVellocity (c2232: 90°/s; the Smelter's rows turn 150-240°/s), as ChrPorter does for the regulars
        foreach (int id in new[] { s.EnemyParam, s.AnimId })
            if (pack.Param("EnemyMoveParam")[id] is { } mr)
                foreach (var cell in mr.Cells.Where(c => c.Value is float f0 && f0 != 0 && c.Def.InternalName.Contains("Rot", StringComparison.Ordinal) && c.Def.InternalName.Contains("Speed", StringComparison.Ordinal)
                             && !c.Def.InternalName.Contains("Border") && !c.Def.InternalName.Contains("Accel") && !c.Def.InternalName.Contains("landing")))
                    cell.Value = (float)(D(npc, "turnVellocity") * Math.PI / 180);
        // ---- EnemyParam
        var ep = pack.AddRow("EnemyParam", pack.Param("EnemyParam")[s.TemplateEnemyParam]!, s.EnemyParam, $"[Lordran] {s.Ds1Chr} 1:1 boss");
        Set(ep, "modelId", s.Model); Set(ep, "animId", s.AnimId);
        // session 11 (burgchr11): only where the clone exists (a template without such a row keeps its own reference)
        if (pack.Param("EnemyCommonParam")[s.EnemyParam] != null) Set(ep, "commonId", s.EnemyParam);
        if (pack.Param("EnemyMoveParam")[s.EnemyParam] != null) Set(ep, "moveId", s.EnemyParam);
        if (pack.Param("DeadTypeParam")[s.EnemyParam] != null) { Set(ep, "deadType", s.EnemyParam); Set(ep, "deadType02", s.EnemyParam); }
        Set(ep, "defaultEquipWeapon", 0);
        for (int k = 0; k < 3; k++) Set(ep, $"equipWeaponAId0{k}", s.WeaponParam);
        // session 11 (burgchr11): HP with the resident maxHpRate, defences, resists, stamina, damageAdjustRate* = the regulars' calibration
        string cal = Ds1Calibration.Apply(ep, ds1, npc);
        res.Report.Add($"EnemyParam {s.EnemyParam}: {cal}");
        int hp = (int)D(ep, "hitPoint");
        var area = ds1.Params.Params["GameAreaParam"][s.Ds1Entity];
        int souls = area != null ? Ds1Calibration.BossSouls(area) : Ds1Calibration.Souls(npc);   // bal11 rule BAL2
        // step 2: the boss event's BossBattleParam pays the souls (DS2's convention: boss EnemyParams drop 0, e.g. the
        // Smelter's 305000 vs BossBattleParam 1019000 soulValue 32000); without a boss event the EnemyParam pays them
        Set(ep, "dropSoulValue", s.Ai != null && !s.LargeRegular ? 0 : souls);
        Set(ep, "itemLotId", 0); Set(ep, "itemLotIdForGuest", 0);
        if (s.LargeRegular)
        {
            // rule LR1: DS1's own item lots (Ds1Drops, as ChrPorter's regulars), lot id EnemyParam·100
            var notes = new List<string>();
            var drops = Ds1Drops.Resolve(ds1, npc, pack, notes);
            var lots = pack.Param("ItemLotParam2_Chr");
            int lotId = s.EnemyParam * 100;
            int n = Ds1Drops.Write(pack, lots[1105000]!, lotId, $"[Lordran] {s.Ds1Chr} drops (DS1 {s.Ds1Npc})", drops);
            Set(ep, "itemLotId", lotId);
            res.Report.Add($"rule LR1: drops lot {lotId} ({n} items): " + string.Join("; ", drops.Select(x => $"{x.ItemId} {x.Percent:0.#}% {x.Note}")) + (notes.Count > 0 ? " | " + string.Join("; ", notes) : ""));
        }
        // session 11 (burgchr11, rule NS1): DS1 bosses flinch once their super armour breaks unless a resident SpEffect suppresses every
        // reaction (the Asylum / Stray Demon's 5300: all levels "none"; Taurus 5329 small, Capra / Titanite 5333 medium, + 5360 none while
        // the poise holds = DS2 poiseValue from superArmorDurability, Ds1Calibration.Apply)
        bool never = Ds1Calibration.NeverReacts(ds1, npc);
        Set(ep, "stunDisableFlag", never ? 1 : 0);
        foreach (var (xNpc, xEp) in s.ExtraVariants)
        {
            var xn = ds1.Params.Params["NpcParam"][xNpc] ?? throw new InvalidDataException($"DS1 NpcParam {xNpc} missing");
            var xr = pack.AddRow("EnemyParam", ep, xEp, $"[Lordran] {s.Ds1Chr} 1:1 (DS1 NpcParam {xNpc})");
            Ds1Calibration.Apply(xr, ds1, xn);
            int xs = Ds1Calibration.Souls(xn);   // bal11 rule BAL2
            Set(xr, "dropSoulValue", xs);
            var xd = new List<string>();
            var drops = Ds1Drops.Resolve(ds1, xn, pack, xd);
            int lot = xEp * 100;
            if (pack.Param("ItemLotParam2_Chr")[lot] == null) Ds1Drops.Write(pack, pack.Param("ItemLotParam2_Chr")[1105000]!, lot, $"[Lordran] {s.Ds1Chr} drops (DS1 {xNpc})", drops);
            Set(xr, "itemLotId", lot);
            if (s.ExtraCulling.TryGetValue(xNpc, out var xc))
            {
                // its own weapon slot row: the same behaviour, its display groups
                int wid = s.WeaponParam + 1 + Array.FindIndex(s.ExtraVariants, v => v.Npc == xNpc);
                var xw = pack.AddRow("EnemyEquipWeaponParam", pack.Param("EnemyEquipWeaponParam")[s.WeaponParam]!, wid, $"[Lordran] {s.Ds1Chr} weapon slot (DS1 NpcParam {xNpc})");
                for (int k = 0; k < 2; k++) Set(xw, $"cullingId0{k}", k < xc.Length ? xc[k] : 0);
                for (int k = 0; k < 3; k++) Set(xr, $"equipWeaponAId0{k}", wid);
            }
            res.Report.Add($"EnemyParam {xEp}: DS1 NpcParam {xNpc} on the same body / anim set / AI, souls {xs}");
        }
        res.Report.Add($"EnemyParam {s.EnemyParam} (from {s.TemplateEnemyParam}): model {s.Model}, animId {s.AnimId}, HP {hp} (DS1 {D(npc, "hp")} × {ChrPorter.HpScale:0.###}), souls {(s.Ai != null ? $"0 (BossBattleParam pays {souls})" : souls.ToString())} (DS1 GameAreaParam {s.Ds1Entity} bonusSoul_single × {Ds1Balance.BossSouls}, rule BAL2), weapon slots → {s.WeaponParam} (behaviour {s.BehaviorId}), stun {(never ? "off (a resident SpEffect suppresses every reaction)" : "on (flinches when the poise breaks)")}; {clones} config rows cloned; capsule {D(npc, "hitHeight")} × r {D(npc, "hitRadius")} m (DS1 NpcParam)");
    }
}
