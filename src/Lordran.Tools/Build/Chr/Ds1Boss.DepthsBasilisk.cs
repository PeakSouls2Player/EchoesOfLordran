namespace Lordran.Tools.Build.Chr;

/// <summary>
/// Session 11 (track dchr11): DS1's Basilisk (c3270, the Depths' lower level m10_00: 10 Enemy parts + 1 DummyEnemy, entities 1000200 /
/// 1000210, the rest without) on the boss pipeline as a large regular (rule LR1). NpcParam 327000 (hp 84, 200 souls, turn 120, moveAnimId
/// 900, resident 91110 "curse disabled" = immune to its own breath, 7004 Upper Depths area scaling, 71110; weakPartsDamageRate 1.5 /
/// material_Weak 60 = the eyes), thinks 327000 (BattleStartDist 20) / 327001 (9) → battle goal 327000 basilisk327000Battle.
/// <para><b>Template</b>: DS2's own Basilisk network c2100 (EnemyParam 210000 "Basilisk - Things Betwixt", weapon 21000000): DS2 re-used
/// DS1's rig. Its 72-bone rig holds every one of DS1 c3270's 62 skeleton bones at the same place in the hierarchy with the same bind
/// translations (Spine1 0.1662, Spine2 0.2493, Spine3 0.3194, Neck 0.1647/-0.1888, Tail2 0.3682, LLeg2 0.2051 …); DS2 renamed the limbs
/// (DS1 "shoulder_L" / "LUpperarm1" / "Larm1" / "Lhand_Digit11" / "LLeg11" / "LLegAnkle" → DS2 "L_shoulder" / "L_Upperarm1" / "L_ForeArm1"
/// / "L_Digit11" / "L_Leg11" / "L_LegAnkle") and added 7 throat-sac bones (L/R/C_Throat01-03). Exact names shared: the 16 axial bones
/// (Pelvis, Spine1-3, Throat, Neck, Head, Jaw, Eye_L/R, Tail1-6); limb IK joints match by the limb key (rule IK2: "LLeg11" = "L_Leg11",
/// "LLegAnkle" = "L_LegAnkle", "LUpperarm1" = "L_Upperarm1"). The same body plan, the same locomotion (a frog-like crawl, walk / run /
/// turns / back + side hops) and the same two attack kinds (bite-range jump breath, mid-range breath), so no other quadruped (c2260 rat,
/// c2270 dog, c2130 lizard, c2140 boar: other proportions, no shared bone names beyond the spine) fits better.</para>
/// <para><b>Ids</b> (rule I-chr): DS2 owns EnemyParam 327000 (Dragon Skeleton) and model c3270 → EnemyParam 1,327,000, model 3280, animId
/// 328010 (decade 3280: no DS2 model / anibnd / TimeAct / sound bank, no DSR chr; not used by our other ports), behaviour 1,327,010,
/// weapon slot 132,701,000, logic 327011 / 327001 (no EnemyDefaultLogicParam / LogicComParam rows; DS2's 327001 rows are MapObject
/// params only). All checked with find-row ds2.</para>
/// <para><b>Attacks</b> (327000_battle.lua): Att3000 0-3.2 m (Act01 / Act04: ApproachTarget 2.5 → 3000; DS1 a00_3000 = the curse
/// breath, TAE bullet 2.833-4.3 s, BehaviorParam 232700100 → Bullet 32700, SpEffect 5220 registCurse 20), Att3001 5.2-8.2 m (Act02 /
/// Act03: ApproachTarget 8.5 → 3001 = the jump &amp; breath, bullet at 0.867 s, BehaviorParam 232700110 → Bullet 32701: atkPhys 60 +
/// 5221 curse 9 → impact 32702 → lingering cloud 32703: 4 s, 5222 curse 10); no combos; Act05 / ActAfter: SpinStep 701 (back) ×2, 702 / 703 (side hops), SidewayMove;
/// Interupt: FindAttack_Step / Damaged_Step (4 m). Both attacks are bullets only (no TAE type-1 hit): rule B-BUL, the curse rule CU1 in
/// <see cref="Ds1AttackParams.BulletRows"/>.</para>
/// </summary>
public static partial class Ds1Boss
{
    const string MvB = "DS1 MoveParam 900 (NpcParam 327000 moveAnimId 900): walk 200-203, run 500-503, turns 602-605, hops 700-703 (c3270 anibnd)";

    /// <summary>c2100 (DS2 Basilisk) slots → DS1 c3270 clips: the small-quadruped table plus the Basilisk's own hops (SpinStep 701 back,
    /// 702 left, 703 right, 700 forward) and its run set 500-503.</summary>
    public static readonly Ds1RigAnims.Role[] BasiliskRoles = With(QuadrupedRoles,
        new("a00_07_0012", [501, 201], "run back", "V", MvB, Loop: true), new("a00_07_0013", [502, 202], "run left", "V", MvB, Loop: true),
        new("a00_07_0014", [503, 203], "run right", "V", MvB, Loop: true),
        new("a00_07_7012", [501, 201], "run back", "V", MvB, Loop: true), new("a00_07_7013", [502, 202], "run left", "V", MvB, Loop: true),
        new("a00_07_7014", [503, 203], "run right", "V", MvB, Loop: true),
        new("a00_11_0011", [701], "hop back (short)", "V", "SpinStep 701 AI_DIR_TYPE_B"), new("a00_11_0031", [701], "hop back (long)", "V", "701"),
        new("a00_11_0012", [703], "hop right (short)", "V", "SpinStep 703 AI_DIR_TYPE_R"), new("a00_11_0032", [703], "hop right (long)", "V", "703"),
        new("a00_11_0022", [702], "hop left (short)", "V", "SpinStep 702 AI_DIR_TYPE_L"), new("a00_11_0042", [702], "hop left (long)", "V", "702"),
        new("a00_11_0010", [700, 701], "hop front", "P", "DS1 a00_0700 (not requested by the Lua)"), new("a00_11_0030", [700, 701], "hop front (long)", "P", "a00_0700"),
        new("a00_17_0020", [2202, 2200], "death 2", "P", "DS1 a00_2202"),
        new("a75_01_7100", [0], "dormant loop", "P", "no DS1 intro: every c3270 part has InitAnimID -1"), new("a75_01_7101", [0], "wake", "P", "no intro"),
        new("a75_01_7110", [0], "dormant loop", "P", "no intro"), new("a75_01_7111", [0], "wake", "P", "no intro"),
        new("a79_", [0], "throw victim", "P", "c3270 has no backstab / plunge victim clip: idle"));

    public static Spec? Basilisk => new("c3270", "c2100", 210000, "c1030", "BD_LG", "BD_LG_1bit_2side",
        Model: 3280, AnimId: 328010, EnemyParam: 1_327_000, Ds1Npc: 327000, BehaviorId: 1_327_010, WeaponParam: 132_701_000, TemplateWeaponParam: 21000000,
        HighLogic: 327011, LowLogic: 327001,
        Anim: new Ds1BossAnims.Spec("c3270", "c2100", "c3280",
            Singles: [3000, 3001],
            Chains: [],
            Roles: BasiliskRoles,
            NoReaction: []),
        AttackRange: new() { [3000] = 3.2f, [3001] = 8.2f },
        InterimCombos: [],
        Ds1Entity: 1000200)
    {
        Ds1Map = "m10_00_00_00", LargeRegular = true,
        Ai = new Ai.Boss.AiPorter.Spec("m10_00_00_00", BattleGoal: 327000, ThinkParam: 327000, TemplateUpper: 305011, TemplateLower: 305001, Upper: 327011, Lower: 327001),
    };
}
