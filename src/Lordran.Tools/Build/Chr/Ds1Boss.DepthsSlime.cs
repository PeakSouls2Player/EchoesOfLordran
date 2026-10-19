namespace Lordran.Tools.Build.Chr;

public static partial class Ds1Boss
{
    // ---------------------------------------------------------------- Slime (c3200, DS1 m10_00: 11 MSB parts, entities 1000100-1000107 + 3 without)
    /// <summary>
    /// Session 11 (dchr11): DS1's Slime ("うごめく腐肉", NpcParam 320000: 102 HP, turn 10°/s, phys def 898, weight 10, resident 90000 =
    /// status immunity, moveAnimId 100) on the boss pipeline as a large regular (rule LR1). Template network: DS2's Corrosive Egg
    /// Insect c6720 (rule SL-T: no DS2 slime; c6720 is a legless slow crawler with walk / side-walk / small knock-back reactions, and
    /// the only DS2 crawler whose SM_StartUp has types 2 AND 3 with a drop wake (a75_01_7101 / 7111: root −5 m, ds2-root-survey);
    /// its attack bands (a65_00, a65_10-13) take DS1's six single attacks by cloning; the Lua has no chains). None of DS1's 73 node
    /// names (master, a000, b000-b007, c000-c011, d000-d011, e000-e011, f000-f007, g000, h000-h010) is a c6720 bone: every network
    /// joint (head look Head, leg IK / foot lock *_Leg_*, Spawn) maps to the root, as for the Gargoyles (rule SL-R, P).
    /// Clips (DS1 c3200.anibnd): 0 idle, 200-203 walk F/B/L/R (no run, turn or step clips: MoveParam 100's 500-503 / 602-605 / 70x
    /// are absent), 1500 falling (judge 100 drop hit), 1750 landing (7 s), 2000-2002 small hits, 2200 death, 2250 dead pose,
    /// 3000-3005 attacks, 3300-3313 the drop-grab throw set (ThrowParam 320000: atk 3300 / def 9200, AtkParam 320000 throwFlag 1:
    /// not ported, rule SL-THR), 9000 ceiling wait (8 s loop, body raised 1.6 m, drips Bullet 32000 via TAE type 2), 9060 release
    /// (body stretches down from the ceiling; ends in 1500's pose). TAE 9001 = 9000's motion (importHkx 9000) with the drips at
    /// other times (a desynchronised copy).
    /// Start-up (rule SL-S): MSB InitAnimID 9000 (1000100, 102-106) → start-up type 2 (a75_01_7100 dormant = 9000, 7101 wake = 9060);
    /// 9001 (1000101, 1000107) → type 3 (7110 = 9001 → its motion 9000, 7111 = 9060). DS1 drives the drop by event 11005100
    /// (slots 0-7, gravity + map hits off while waiting; player in region 1002100/101/102/103/107 or damage → wait 0-0.9 s →
    /// gravity + map hits on, waiting animation cleared): the DS2 side needs that event (wake + gravity), not the AI.
    /// Attacks (320000_battle.lua "Funiku320000Battle"): target closer than AtkDist 1 → 55 %: 3000 front / 3001 back / 3004 left /
    /// 3005 right (IsInsideTarget 40°) else 3002 (all-round); 45 %: approach to 1.5 then 3003 (AttackTunableSpin); farther: 30 %
    /// wait 2-4 s, else approach 0.8. Interrupt: inside observe region 1002800 → back to POINT_INITIAL (0.5), wait 2.
    /// </summary>
    public static readonly Ds1RigAnims.Role[] SlimeRoles = With(QuadrupedRoles,
        new("a00_00_4000", [2250, 0], "ragdoll loop pose", "P", "DS1 a00_2250: dead pose"),
        new("a00_13_", [1500, 0], "jump / airborne", "P", "DS1 a00_1500: falling (the ceiling drop's fall; TAE judge 100 = the drop hit)"),
        new("a00_14_", [1750, 0], "landing", "P", "DS1 a00_1750: landing after the drop (7 s, starts in 1500's stretched pose)"),
        new("a75_01_7100", [9000, 0], "dormant loop (start-up type 2)", "P", "DS1 a00_9000: waiting on the ceiling (MSB InitAnimID 9000; event 11005100 holds gravity / map hits off)"),
        new("a75_01_7101", [9060, 0], "wake (start-up type 2)", "P", "DS1 a00_9060: release from the ceiling (DS1 90X0 → 90X0+60), then 1500 fall / 1750 landing"),
        new("a75_01_7110", [9001, 9000, 0], "dormant loop (start-up type 3)", "P", "DS1 TAE 9001 = 9000's motion with desynchronised drips (MSB InitAnimID 9001)"),
        new("a75_01_7111", [9060, 0], "wake (start-up type 3)", "P", "as a75_01_7101"),
        new("a75_01_712", [0], "start-up type 4 (unused)", "P", "no DS1 counterpart: idle"),
        new("a75_01_713", [0], "start-up type 5 (unused)", "P", "no DS1 counterpart: idle"),
        new("a79_", [0], "throw victim", "P", "no DS1 throw-victim clip: idle"));

    public static Spec? Slime => new("c3200", "c6720", 672000, "c1030", "BD_LG", "BD_LG_1bit_2side",
        Model: 3200, AnimId: 320010, EnemyParam: 320000, Ds1Npc: 320000, BehaviorId: 320010, WeaponParam: 32_000_000, TemplateWeaponParam: 67200000,
        HighLogic: 320011, LowLogic: 320001,
        Anim: new Ds1BossAnims.Spec("c3200", "c6720", "c3200",
            Singles: [3000, 3001, 3002, 3003, 3004, 3005],
            Chains: [],
            Roles: SlimeRoles,
            NoReaction: []),
        // the Lua's distances: every attack only below AtkDist 1; 3003 after ApproachTarget 1.5
        AttackRange: new() { [3000] = 1f, [3001] = 1f, [3002] = 1f, [3003] = 1.5f, [3004] = 1f, [3005] = 1f },
        InterimCombos: [],
        Ds1Entity: 1000100)
    {
        Ds1Map = "m10_00_00_00",
        LargeRegular = true,
        // display groups #01# / #02# stay hidden (NpcParam 320000 sets no modelDispMask bit): WeaponMeshes empty
        Ai = new Ai.Boss.AiPorter.Spec("m10_00_00_00", BattleGoal: 320000, ThinkParam: 320000, TemplateUpper: 305011, TemplateLower: 305001, Upper: 320011, Lower: 320001),
    };
}
