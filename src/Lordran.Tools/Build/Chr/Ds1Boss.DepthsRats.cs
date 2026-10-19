namespace Lordran.Tools.Build.Chr;

/// <summary>Session 11 (track dchr11, agent rats): the Depths' (DS1 m10_00) Rat c1200 and Giant Rat c1202 on the boss pipeline as large
/// regulars (rule LR1), the Burg's Small Rat c1201 (<see cref="SmallRat"/>) as the model: DS2's Corpse Rat network c2260 resized to the
/// DS1 rig, <see cref="QuadrupedRoles"/>.</summary>
public static partial class Ds1Boss
{
    /// <summary>
    /// DS1's Rat (c1200, NpcParam 120000: 100 HP, turn 180°/s, MoveParam 900; 6 MSB parts c1200_0000-0005 in the Depths, no entity, think
    /// 120000 ×3 / 120010 ×3 → battle goal 120000 "Inunezumi120000Battle"). c1200 is c1201's body at 1.43× (the same 54 FLVER nodes,
    /// 47 dummies and bone names/order, own bind pose) and the same 61 clips plus a00_0606 (named by no MoveParam / Lua: unused), so the
    /// Small Rat's template c2260 and role table serve 1:1. Ids: model 1200 (free in DS2), animId 119010 (decade 1190: no DS2 bank /
    /// model / anibnd / TimeAct; 1200 is the Small Rat's), EnemyParam 120000 (rule I-chr: no DS2 EnemyParam or EnemyParam-keyed config
    /// row), behaviour 120010, weapon slot 12,000,000 (DS2 has a WeaponParam 12000000, not an EnemyEquipWeaponParam one), logic 120011 /
    /// 120001. Attacks 3000-3003 (Att*_Dist_max 1.2 / 1.4 / 3.2 / 1.2), each 50 % single, 50 % ComboAttack → ComboFinal 3001 / 3000 /
    /// 3000 / 3000; Interupt: FindAttack_Step / RebByOpGuard_Step (back step 701).
    /// </summary>
    public static Spec? Rat => new("c1200", "c2260", 226000, "c1030", "BD_LG", "BD_LG_1bit_2side",
        Model: 1200, AnimId: 119010, EnemyParam: 120000, Ds1Npc: 120000, BehaviorId: 120010, WeaponParam: 12_000_000, TemplateWeaponParam: 22600000,
        HighLogic: 120011, LowLogic: 120001,
        Anim: new Ds1BossAnims.Spec("c1200", "c2260", "c1200", Singles: [3000, 3001, 3002, 3003], Chains: [[3000, 3001], [3001, 3000], [3002, 3000], [3003, 3000]], Roles: QuadrupedRoles, NoReaction: []),
        AttackRange: new() { [3000] = 1.2f, [3001] = 1.4f, [3002] = 3.2f, [3003] = 1.2f },
        InterimCombos: [],
        Ds1Entity: -1200)
    {
        Ds1Map = "m10_00_00_00", LargeRegular = true,
        // think 120000 and 120010 differ only in BattleStartDist (20 / 9) and goalAction_ToCaution/ToFind (2 / 1), none of which the AI port
        // reads (perception: eye / ear / chase are equal): one spec, 120000
        Ai = new Ai.Boss.AiPorter.Spec("m10_00_00_00", BattleGoal: 120000, ThinkParam: 120000, TemplateUpper: 305011, TemplateLower: 305001, Upper: 120011, Lower: 120001),
    };

    /// <summary>
    /// DS1's Giant Rat c1202: as <see cref="MediumDamageRoles"/> (resident SpEffect 5333 maps every damage level to medium a00_2004-2007,
    /// 5360 suppresses reactions while its super armour 100 holds; c1202 has no small / large / knock-back / blow clips), the plunge victim
    /// set 2520-2522 (QuadrupedRoles' a79_ picks 2520), the Capra Demon's no-reaction slots (falls, downs, parries, guard).
    /// </summary>
    public static readonly Ds1RigAnims.Role[] GiantRatRoles = With(With(QuadrupedRoles, MediumDamageRoles),
        new("a00_16_06", [2004], "fall start", "P", "5333: every level → medium (no DS1 blow clip)"),
        new("a00_16_07", [2004], "down / get up", "P", "no DS1 lying set: medium F"),
        new("a00_16_1", [2004, 2005], "parried / bleed / stagger", "P", "medium F (5333)"));

    /// <summary>
    /// DS1's Giant Rat (c1202, NpcParam 120200: 495 HP, poise 100, resident 5333 / 5360, disableRespawn, 1000 souls; MSB c1202_0000 = entity
    /// 1000500, think 120200 → battle goal 120200 "BigInunezumi120200Battle"). Rig: 44 bones incl. Neck1 and the clavicles (c1201 lacks
    /// them as deform bones; c2260 has them too: resize clean, 50 → 52 animation bones), 5 meshes / 4 materials (no "#NN#" display group: 0
    /// meshes left out), 109 dummies (88 × id 200
    /// on the body and tail: SFX points; the build keeps 4 per id → 24), textures in c1202.chrtpfbdt (NpcParam normalChangeTexChrId unset → its own). Ids: model 1202 (free),
    /// animId 122010 (decade 1220 free in DS2), EnemyParam 120200, behaviour 120210, weapon slot 12,020,000, logic 120211 / 120201. Attacks
    /// 3000-3006 (Lua: AttackTunableSpin 3000-3003 / 3006 after Approach_Act, NonspinningAttack 3004 / 3005 = the right / left rear attacks
    /// by IsInsideTarget R/L 90 ∧ B 170 within 3.5 m); no combos; GetWellSpace: 30 % SpinStep 701. Regions 1002700-1002704 (observe areas,
    /// event move target 1002703: the rat holds its den until the player is in 1002704) are DS1 map regions the coordinator must place.
    /// </summary>
    public static Spec? GiantRat => new("c1202", "c2260", 226000, "c1030", "BD_LG", "BD_LG_1bit_2side",
        Model: 1202, AnimId: 122010, EnemyParam: 120200, Ds1Npc: 120200, BehaviorId: 120210, WeaponParam: 12_020_000, TemplateWeaponParam: 22600000,
        HighLogic: 120211, LowLogic: 120201,
        Anim: new Ds1BossAnims.Spec("c1202", "c2260", "c1202",
            Singles: [3000, 3001, 3002, 3003, 3004, 3005, 3006],
            Chains: [],
            Roles: GiantRatRoles,
            NoReaction: ["a00_16_06", "a00_16_07", "a00_16_10", "a00_16_11", "a00_09_"]),
        AttackRange: new() { [3000] = 2.1f, [3001] = 1.6f, [3002] = 1.7f, [3003] = 1.6f, [3004] = 3.5f, [3005] = 3.5f, [3006] = 7.3f },
        InterimCombos: [],
        Ds1Entity: 1000500)
    {
        Ds1Map = "m10_00_00_00", LargeRegular = true,
        Ai = new Ai.Boss.AiPorter.Spec("m10_00_00_00", BattleGoal: 120200, ThinkParam: 120200, TemplateUpper: 305011, TemplateLower: 305001, Upper: 120211, Lower: 120201),
    };
}
