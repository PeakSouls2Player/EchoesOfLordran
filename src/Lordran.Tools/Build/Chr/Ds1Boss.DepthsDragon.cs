namespace Lordran.Tools.Build.Chr;

/// <summary>
/// Session 11 (track dchr11): DS1's Gaping Dragon (c5260, the Depths' boss, DS1 m10_00 entity 1000800; NpcParam 526000: 2589 HP, turn
/// 30°/s, hitHeight 12 / hitRadius 5.9, weight 1500, resident 5300 = never a hit reaction + 90101 + 80000 + 5151, partsDamageType 1,
/// display group #00# = the tail (modelDispMask0; event 11005396 switches DispMask bit 0 off when the tail is cut)) on DS2's Guardian
/// Dragon boss network (c2120, as the Hellkite: a four-limbed dragon boss network with walk / back-walk / turn loops and a single
/// moveset). Think 526000 → battle goal 526000 (Donsyoku526000Battle): Act1 3000 (≤ 4, behind-right), Act2 3001 (1.5), Act3 3002 (4),
/// Act4 3003 (6) 50 % single / 50 % → ComboFinal 3004, Act5 3005 / 3006 by the tail's part damage (GetPartsDmg 0 = PARTS_DMG_FINAL
/// → 3006), Act6 3010 (−6..−3: behind), Act7 3007 (5, TunableSpin), Act8 3008 (25: the vomit, timer 1 30 s, HP ≤ 60 %), Act9 3009
/// (8-14), Act10 approach 25 + 3003 → 3004; Interupt: GetEventRequest(0) == 1 (events 11005398: NPC part 5262 hit → AI command 1)
/// → 3003 / 3003 → 3004 (rule E-REQ). DS1 clips: 0 idle, 200 / 201 walk F / B, 600 / 601, 602-605 turns, 1500 airborne, 1750
/// landing, 2080 (empty TAE), 2200 death, 2250 dead pose, 3000-3010 attacks, 3300-3323 the grab throws (ThrowParam 526000-526002,
/// not ported: rule GD-THROW), 8000 the tail-cut reaction (TAE 231 event message 400 at 0 s = DS1 event 11005396's cue for the cut
/// tail actor c5261, not ported here: TC1), a10_0000 / a10_0001 (no TAE events, named by no event; the intro is cutscene 100000).
/// </summary>
public static partial class Ds1Boss
{
    const string GdMv = "DS1 MoveParam 100 (NpcParam 526000 moveAnimId 100): walkF 200, walkB 201, turnL 602, turnR 603, largeTurnL 604, largeTurnR 605 (c5260 has no 202 / 203 / 500-503)";

    public static readonly Ds1RigAnims.Role[] GapingDragonRoles = With([.. QuadrupedRoles.Where(r => !r.Slot.StartsWith("a00_16_"))],
        new("a00_16_", [0], "damage reaction → none", "P", "resident SpEffect 5300: DS1's Gaping Dragon never plays a hit reaction (its 2080 set has no TAE events)"),
        // session 11 (dchr11), rule TC1 (chrai11's, as the Hellkite): c2120's tail break reaction plays DS1's tail-cut clip a00_8000 (its TAE
        // message 400 is 11005396's cue: rule EM1-r)
        new("a00_16_8107", [8000, 0], "tail cut (part break 07)", "P", "DS1 a00_8000: 11005396 requests it (2004:6 command 1001) when tail part 5260's HP reaches 0"),
        new("a00_01_0011", [603, 0], "turn right 90", "V", GdMv, SyncEvents: -1), new("a00_01_0012", [605, 603, 0], "turn right 180", "V", GdMv, SyncEvents: -1),
        new("a00_03_0071", [605, 603, 0], "walk turn 180 (right)", "P", GdMv), new("a00_03_7071", [604, 602, 0], "walk turn 180 (left)", "P", GdMv),
        new("a00_06_0012", [201, 200], "walk back", "V", GdMv, Loop: true), new("a00_06_7012", [201, 200], "walk back", "V", GdMv, Loop: true),
        new("a00_06_0013", [200], "walk left", "P", "no DS1 side walk (MoveParam walkL 202 missing in c5260) → walk forward", Loop: true),
        new("a00_06_0014", [200], "walk right", "P", "as 0013", Loop: true),
        new("a00_06_7013", [200], "walk left", "P", "as 0013", Loop: true), new("a00_06_7014", [200], "walk right", "P", "as 0013", Loop: true),
        new("a00_11_", [0], "step", "P", "DS1 c5260 has no step clip (its Lua has no SpinStep): idle"),
        new("a00_13_", [1500, 0], "jump / airborne", "P", "DS1 a00_1500"), new("a00_14_", [1750, 0], "landing", "P", "DS1 a00_1750"),
        new("a75_01_7100", [0], "dormant loop (start-up type 2)", "P", "no DS1 waiting clip: the intro is cutscene 100000 (event 11005392), the dragon is enabled after it"),
        new("a75_01_7101", [0], "wake (start-up type 2)", "P", "as 7100"),
        new("a75_01_7110", [0], "dormant loop (start-up type 3)", "P", "as 7100"), new("a75_01_7111", [0], "wake (start-up type 3)", "P", "as 7100"),
        new("a79_", [0], "throw victim", "P", "no DS1 victim clip"),
        new("a00_17_", [2200], "death", "V", "DS1 a00_2200 (TAE JT 0 '12' at 5.733 s)"));

    public static Spec? GapingDragon => new Spec("c5260", "c2120", 212000, "c1030", "BD_LG", "BD_LG_1bit_2side",
        Model: 5260, AnimId: 526010, EnemyParam: 526000, Ds1Npc: 526000, BehaviorId: 526010, WeaponParam: 52_600_000, TemplateWeaponParam: 21200000,
        HighLogic: 526011, LowLogic: 526001,
        Anim: new Ds1BossAnims.Spec("c5260", "c2120", "c5260",
            Singles: [3000, 3001, 3002, 3003, 3004, 3005, 3006, 3007, 3008, 3009, 3010],
            Chains: [[3003, 3004]],
            Roles: GapingDragonRoles,
            NoReaction: ["a00_16_0", "a00_16_1", "a00_16_80", "a00_09_"]),   // rule TC1: every damage slot but the tail break a00_16_8107
        AttackRange: new() { [3000] = 4f, [3001] = 1.5f, [3002] = 4f, [3003] = 6f, [3004] = 6f, [3005] = 8f, [3006] = 8f, [3007] = 5f, [3008] = 25f, [3009] = 14f, [3010] = 0.5f },
        InterimCombos: [],
        Ds1Entity: 1000800)
    {
        Ds1Map = "m10_00_00_00",
        WeaponMeshes = new() { ["#00#"] = "BD_LG" },   // the tail drawn as body until the tail cut exists (TC1)
        Ai = new Ai.Boss.AiPorter.Spec("m10_00_00_00", BattleGoal: 526000, ThinkParam: 526000, TemplateUpper: 305011, TemplateLower: 305001, Upper: 526011, Lower: 526001),
    };
}
