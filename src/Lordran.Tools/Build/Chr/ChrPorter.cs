using System.Numerics;
using System.Text;
using Lordran.Tools.Commands;
using Lordran.Tools.Textures;
using SoulsFormats;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// A DS1 character's look on a DS2 character's body (first step towards DS1 enemies; session 5). The DS1 mesh is
/// re-bound onto a DS2 template skeleton (same bone names: DS1 "L Clavicle" = DS2 "L_Clavicle"), so the template's
/// Morpheme animations, TimeAct, AI, hit dummies and ragdoll drive it unchanged — DS1 content, DS2 rules. Real DS1
/// animations need an NSA encoder + Morpheme network (PLAN.md), which this does not attempt.
/// <para>Re-binding: DS1 binds in a T-pose, DS2 in an A-pose. Each DS1 bone b maps to a DS2 bone m(b); a vertex
/// skinned to b moves as p' = P2[m(b)] + R_b (p − P1[b]) (P = bind-pose joint positions, R_b = the rotation turning
/// DS1's bone direction (joint → child joint) into DS2's; bones without a direction child take their parent's R),
/// blended by the vertex weights. Twist around a bone isn't matched (shortest-arc rotation).</para>
/// <para>Output (model id <c>NewModel</c>, e.g. 2500 → c2500): model/chr/cNNNN.bnd (template bnd: converted .flv and
/// _s.flv, ACB/physics/ragdoll with the id patched), cNNNN.texbnd (template texture names, DS1 pixels), and EnemyParam
/// row <c>EnemyParamId</c> = the template variant's row with modelId = NewModel (animId — which also picks the
/// animations, TimeAct and sounds — AI and stats stay DS2's). Verified in game (run s5-26): spawns, idles, attacks.</para>
/// </summary>
public static partial class ChrPorter   // session 11 (dchr11): partial, the Depths' Options live in ChrPorter.Depths.cs
{
    /// <summary>
    /// One DS1 NPC variant → a new EnemyParam row (copy of a template variant: its animId picks the moveset, and the
    /// variant shows the weapon meshes on its GX list) and the generator logic ids to use with it.
    /// </summary>
    public sealed record Variant(int Ds1Npc, int TemplateEnemyParam, int EnemyParamId, int HighLogic, int LowLogic, string Note);

    /// <param name="WeaponMaterials">DS1 weapon/shield mesh material ("#00#"…) → the template material it takes over
    /// (and whose GX list decides which variants show it); DS1 weapon meshes not listed are dropped.</param>
    public sealed record Options(string Ds1Chr, string Ds1TexChr, string TemplateChr, int NewModel,
        string BodyMaterial, string EdgeMaterial, Dictionary<string, string> WeaponMaterials, string[] KeepTemplateMeshes, Variant[] Variants)
    {
        /// <summary>Colour grade of the body diffuse (<see cref="Ds2Tex.ColorGraded"/>); 1/1 = DS1's texture unchanged.</summary>
        public float BodySaturation { get; init; } = 1f;
        public float BodyBrightness { get; init; } = 1f;
        /// <summary>The 1:1 port (session 8): these variants use DS1's own skeleton, clips and ragdoll (<see cref="Ds1RigChr"/>)
        /// instead of the template's body and animations. Null = interim template port only.</summary>
        public OneToOne? Rig { get; init; }
        /// <summary>Further 1:1 builds of the same DS1 model (track enemies, session 8): one per DS1 move set / attack layer (the
        /// armed variants), each with its own anibnd/TAE (animId) and possibly a shared model.</summary>
        public OneToOne[] MoreRigs { get; init; } = [];
        public IEnumerable<OneToOne> Rigs => (Rig != null ? new[] { Rig } : []).Concat(MoreRigs);
    }

    /// <summary>
    /// 1:1 port of some variants (session 8, track chr step 2): model id <c>Model</c> (DS1 body on DS1 bones), animId
    /// <c>AnimId</c> (default Model·100: anibnd cMMMM, timeact cMMMM00) built from the template network/TAE of EnemyParam
    /// animId <c>TemplateAnimId</c>; the listed EnemyParam rows are pointed at it. Roles: the clip-slot role table.
    /// </summary>
    public sealed record OneToOne(int Model, int[] EnemyParams, Ds1RigAnims.Role[] Roles, int TemplateAnimId)
    {
        public int AnimId { get; init; } = Model * 100;
        /// <summary>Track enemies (session 8): the DS1 attack layer of this anim set (<see cref="Ds1Attacks"/>); null = no attacks
        /// (the unarmed Hollow: its attack slots fall back to the DS1 idle, rule C12c).</summary>
        public Ds1Attacks.Plan? Attacks { get; init; }
        /// <summary>DS2 network to start from (default: the Options' template character).</summary>
        public string? TemplateNetwork { get; init; }
        /// <summary>DS1 weapon/shield materials drawn on this model → the template material (and so the culling id) each takes
        /// over (<see cref="Ds1RigModel.Build"/>).</summary>
        public Dictionary<string, string> WeaponMeshes { get; init; } = [];
        /// <summary>The armed variants on this anim set: their weapon, behaviour, combo, damage and bullet params.</summary>
        public Ds1AttackParams.ArmedVariant[] Armed { get; init; } = [];
        /// <summary>False when another 1:1 build of the same Options writes model c{Model} (shared model, own anim set).</summary>
        public bool OwnModel { get; init; } = true;
        /// <summary>Session 11 (ret11), rule RF1: template slots whose role names no clip this DS1 character has play its DS1 idle
        /// (a00_0000), reported, instead of stopping the build (characters without ladder / lying / parry sets: the Black Knight).</summary>
        public bool IdleFallback { get; init; }
    }

    /// <summary>Every DS1 character spec with 1:1 builds (ds1rig-build / ds1rig-check --chr).</summary>
    public static Options[] All => [AsylumHollow, UndeadSoldier, HollowWarrior, Skeleton, BlackKnight, .. BurgOnly, .. DepthsOnly]; // session 11 (ret11): + BlackKnight; (burgchr11): + the Burg's new characters

    /// <summary>
    /// Session 11 (track burgchr11), build-pack --burg-chrs: the characters of DS1 m10_01 (Undead Burg, Lower Burg, Parish). The Burg's
    /// NpcParams of DS1 models the Asylum / Firelink already port join those Options' variants and 1:1 rigs (research r1: every Burg
    /// battle goal is byte-identical to the one the rig's plan was written from, every behaviorVariationId equals the plan's), the
    /// new models are <see cref="BurgOnly"/>. EnemyParam ids by rule I-chr (NpcParam id; the Burg sword Hollow's 250000 is the Asylum
    /// unarmed Hollow's EnemyParam, so it takes 250060). Off (default): every Options is exactly the pre-Burg one (outputs unchanged).
    /// Nothing is placed in the Burg here (M3); build-pack --burg-chr-test places them on a test spot in the Asylum.
    /// </summary>
    public static bool Burg { get; set; }
    static T[] IfBurg<T>(T first, params T[] rest) => Burg ? [first, .. rest] : [];
    /// <summary>Session 11 (burg11, round 7): build-pack --map depths with --burg-chrs adds the Depths' (DS1 m10_00) variants of the
    /// ported characters (Hollows 250030/031/033; LR1 rats and dogs: Ds1Boss). Off: every Options as before.</summary>
    public static bool Depths { get; set; }
    internal static T[] IfDepths<T>(T first, params T[] rest) => Depths ? [first, .. rest] : [];

    /// <summary>Session 11 (burgchr11): the Burg's DS1 models no other map ports (built with --burg-chrs).</summary>
    public static Options[] BurgOnly => Burg ? [BlackKnightGreatsword, BalderKnight, HeavyKnight, Channeler, UndeadAssassin, MerchantHostile] : [];

    /// <summary>Session 11 (burgchr11): every Options with Burg variants (the existing ones + <see cref="BurgOnly"/>).</summary>
    public static Options[] BurgChrs => [AsylumHollow, UndeadSoldier, HollowWarrior, BlackKnight, .. BurgOnly, .. DepthsOnly];   // session 11 (dchr11): + the Depths' new regulars

    /// <summary>Session 11 (track ret11): the DS1 characters the Asylum places from its MSB (every visit, rule V-ret).</summary>
    public static Options[] AsylumChrs => [AsylumHollow, UndeadSoldier, BlackKnight];

    /// <summary>The 1:1 build whose anibnd is <paramref name="key"/> ("c2501" = animId 250100, "c2542" = 254200, ...).</summary>
    public static (Options O, OneToOne Rig) FindRig(string key)
    {
        // session 11 (burgchr11): an anim set only the Burg build has (c2553 crossbow, c2770 greatsword BK) switches the Burg variants on
        if (!Burg && !All.SelectMany(o => o.Rigs).Any(r => $"c{r.AnimId / 100:D4}" == key)) { Burg = true; Console.WriteLine($"{key}: a Burg anim set (ChrPorter.Burg on)"); }
        if (!Depths && !All.SelectMany(o => o.Rigs).Any(r => $"c{r.AnimId / 100:D4}" == key)) { Depths = true; Console.WriteLine($"{key}: a Depths anim set (ChrPorter.Depths on)"); }   // session 11 (dchr11)
        foreach (var o in All)
            foreach (var r in o.Rigs)
                if ($"c{r.AnimId / 100:D4}" == key) return (o, r);
        throw new ArgumentException($"no 1:1 character with anim set {key} (have: {string.Join(", ", All.SelectMany(o => o.Rigs).Select(r => $"c{r.AnimId / 100:D4}"))})");
    }

    /// <summary>build-pack --no-ds1-rig: build every variant with the interim template port (fallback / A-B tests).</summary>
    public static bool UseDs1Rig { get; set; } = true;
    /// <summary>Track enemies step 2: the 1:1 characters run their DS1 battle goal translated to DS2 AI (<see cref="Ai.Ds1Ai"/>);
    /// build-pack --no-ds1-ai keeps the DS2 template AI with step 1's interim aliases (A/B test).</summary>
    public static bool UseDs1Ai { get; set; } = true; // on again: the s8-6 spawn crash was HkTagToPack dropping C-array pointers (ragdoll constraint entities); fixed, verified run s8-7

    /// <summary>DS2 params with a row keyed by c1030's model id (find-row ds2 1030, session 8; map/debug params left out).
    /// Rule S11 (chrint stage 4): <b>no SoundFootParam row for a 1:1 character.</b> DS2 reads SoundFootParam only in
    /// FUN_1404a4d90 (row getter FUN_1402e9ee0 = slot 1 of the sound param holder loaded in FUN_1402e96a0, l.576771), the
    /// name builder FUN_14016f900 ("%s%09d" of material + (model·100 + kind)·1000, kind = param_2 + 1), which has three
    /// engine callers: FUN_14031dc40 (foot contact, kind 1/2; reached from FUN_14031d490 and the TAE foot events
    /// 2401/2402, handler FUN_140326f20; l.616900), FUN_140310960 (velocity-gated fall landing, kind 4; l.606304) and
    /// FUN_140372620 (kind 3, with an SFX via FUN_14016fa80; DS1's own kind-3 names are slide/drag sounds; l.682673).
    /// The row only picks WHOSE set all three play (type 0 "Player" = the player's set: all 42 vanilla rows are type 0;
    /// type 1 = row.modelId's; no row = the model's own id). A 1:1 character's foot, slide and landing sounds are DS1's
    /// TAE events (rules S3/S4): DS1's TAEs play the same c{model}{kind}{material} names (c2500 kinds 1-3, c2540 kinds
    /// 1/2/4 incl. landing in 650030xx, c2550 1-3, c2900 1-4; DS1's c2500/c2550 projects define no kind-4 name at all).
    /// With c1030's cloned type-0 row every engine foot contact, landing and slide would add DS2's PLAYER sounds
    /// (the doubling risk of merge9); without a row the names fall into the model's own id (c2501…/c2541…), which no
    /// project defines → the engine's three kinds are silent and only DS1's TAE sounds play (V-code for DS2; whether
    /// DS1's engine played kind 3/4 for enemies on its own is U: no DS1 decompile). c2551 / c2901 never had a row (their
    /// templates have none).</summary>
    static readonly string[] ConfigByModel = ["ChrPointWindParam", "DamageDeadMotionWeightParam", "DamageEffectSeParam", "DamageMotionLadderWeightParam",
        "DamageMotionRopeWeightParam", "DamageMotionWeightParam", "DeadDamageMotionWeightParam", "EnemyAttackMoveParam", "EnemyColorParam", "EnemyCullingParam",
        "LockOnParam"];
    /// <summary>DS2 params with a row keyed by the template variant's animId (find-row ds2 103000; EnemyParam's own
    /// commonId/moveId references and coincidental ids left out).</summary>
    static readonly string[] ConfigByAnim = ["ChrFullBodySfxParam", "EnemyBehaviorParam", "EnemyBehaviorSecondParam", "EnemyBehaviorThirdParam",
        "EnemyComboBehaviorParam", "EnemyDefaultLogicParam"];

    /// <summary>
    /// DS1's Undead Asylum Hollow (c2500, textures c2509) on DS2's Hollow Infantry (c1030). DS1 NPC 250020 (no weapon)
    /// → 103000 Unarmed; 250022 (modelDispMask0: broken straight sword) → 103010 Bandit's Knife, the DS1 sword on the
    /// knife's material/GX list (WP_1010_M) so only that variant shows it; 250021 (mask2: bow) → 103040 Bow with DS2's
    /// own bow mesh (WP_4200_M; DS1's bow string bones don't map onto DS2's bow rig yet). Logic ids from No-man's
    /// Wharf's Hollow Infantry generators (archers there use high logic 900010).
    /// </summary>
    /// Body grade (session 6, user: "too pink"): DS1's c2509 skin averages R98 G70 B67 and read pink-red under DS2's
    /// light; saturation 0.45 × brightness 0.85 gives ≈ R74 G63 B62 (DS2's Hollow Infantry: R71 G59 B50).
    /// Session 8: the unarmed variant (EnemyParam 250000) is 1:1 — DS1's body on DS1's skeleton with DS1's clips, model
    /// c2501 / animId 250100 (<see cref="Ds1RigChr"/>, roles <see cref="Ds1RigAnims.HollowUnarmed"/>, template network and
    /// TAE of c1030 / 103000 Unarmed). The sword and bow variants stay on the interim template port (c2500 on c1030's body
    /// and animations) until their attacks are mapped.
    public static Options AsylumHollow => new("c2500", "c2509", "c1030", 2500,
        BodyMaterial: "BD_LG", EdgeMaterial: "BD_LG_1bit_2side", WeaponMaterials: new() { ["#00#"] = "WP_1010_M" }, KeepTemplateMeshes: ["WP_4200_M"],
        Variants:
        [
            new(250020, 103000, 250000, 103010, 103000, "unarmed"),
            new(250022, 103010, 250010, 103010, 103000, "broken straight sword"),
            new(250021, 103040, 250040, 900010, 103000, "bow"),
            // session 11 (track ret11), rule V-ret: the return trip's Hollows (entities 1810201-1810210; W1 places them when 11810000)
            new(250023, 103010, 250023, 103010, 103000, "torch (return trip)"),
            new(250024, 103040, 250024, 900010, 103000, "bow (return trip)"),
            new(250025, 103010, 250025, 103010, 103000, "broken straight sword (return trip)"),
            // session 11 (burgchr11): the Burg's Hollows (DS1 m10_01; NpcParam 250000 → EnemyParam 250060, rule I-chr)
            .. IfBurg<Variant>(
                new Variant(250000, 103010, 250060, 103010, 103000, "broken straight sword (Burg)"),
                new Variant(250050, 103010, 250050, 103010, 103000, "broken straight sword (Burg, Burg scaling 7003)"),
                new Variant(250053, 103010, 250053, 103010, 103000, "broken straight sword (Burg black phantom: SpEffect 7100)"),
                new Variant(250001, 103010, 250001, 103010, 103000, "torch (Burg)"),
                new Variant(250052, 103040, 250052, 900010, 103000, "bow (Burg)")),
            // session 11 (burg11, round 7): the Depths' Hollows (DS1 m10_00, scaling 7004): sword as 250050, torch as 250001, torch phantom
            .. IfDepths<Variant>(
                new Variant(250030, 103010, 250030, 103010, 103000, "broken straight sword (Depths)"),
                new Variant(250031, 103010, 250031, 103010, 103000, "torch (Depths)"),
                new Variant(250033, 103010, 250033, 103010, 103000, "torch (Depths black phantom: SpEffect 7100)")),
        ]) { BodySaturation = 0.45f, BodyBrightness = 0.85f, Rig = new OneToOne(2501, [250000], Ds1RigAnims.HollowUnarmed, 103000), MoreRigs = [AsylumHollowSword, AsylumHollowBow, AsylumHollowTorch] };

    /// <summary>
    /// Track enemies (session 8): the Asylum's broken-sword Hollow 1:1 (DS1 NpcParam 250022 → EnemyParam 250010) as model
    /// c2502 (DS1's body + the DS1 broken sword #00# on its own "sword" bone = R_Weapon, drawn through c1030's WP1 material:
    /// culling 20) / animId 250200 (anibnd c2502, TAE c250200; c1030's network on the DS1 rig, the unarmed Hollow's clip roles,
    /// plus DS1's attacks). DS1 move set: animIdOffset 0 (a00_3000-3008), behaviorVariationId 25000, NpcThinkParam 250000 →
    /// battle goal 250000_battle.lua (m18_01): ranges 3000 0-1.7, 3003 0-1.8, 3004 2.5-4, 3005 4-6, 3006 6-7.5, 3008 0-1 m;
    /// act2 = 3000 | 3000→3001 | 3000→3001→3002 | 3000→3001→3003 (10/20/50/20 %), act7 = lunge 3004 / 3005 / 3006, each 50 %
    /// with the follow-up 3007. Interim aliases for the DS2 Hollow Infantry AI (ai103000 requests 10, 11, 12, 50, 51): 10 →
    /// 3000 (it approaches to 2.2 m), 11 → 3004 (2.3-3.4 m), 12 → 3003; 50 / 51 = DS1 chains 0 / 1.
    /// </summary>
    public static OneToOne AsylumHollowSword => new(2502, [250010, 250025, .. IfBurg(250060, 250050, 250053), .. IfDepths(250030)], Ds1RigAnims.HollowUnarmed, 103000) // session 11 (ret11): + 250025 (return trip)
    {
        WeaponMeshes = AsylumHollowWeapons,
        Attacks = new Ds1Attacks.Plan(0, 25000, 0,
        [
            new(3000, "small slash (小攻撃（剣）)", 1.7f, "250000_battle act2 (Att3000 0-1.7); BehaviorParam 225000100"),
            new(3001, "small slash 2", 0, "act2 combo stage 2; 225000110"),
            new(3002, "small slash 3", 0, "act2 combo stage 3; 225000120"),
            new(3003, "two-handed slash (両手で力任せに斬る)", 1.8f, "act3 defAct01 (Att3003 0-1.8); act2 stage 3; 225000130"),
            new(3004, "short lunge (飛び掛り（短距離）)", 4f, "act7 (Att3004 2.5-4); 225000140"),
            new(3005, "middle lunge", 6f, "act7 (Att3005 4-6); 225000150"),
            new(3006, "long lunge", 7.5f, "act7 (Att3006 6-7.5); 225000150"),
            new(3007, "follow-up slash (追い討ち斬り)", 0, "act7 ComboFinal after 3004/3005/3006; 225000170"),
            new(3008, "flailing, 6 hits (ぶんぶん振り回し)", 1f, "act1 defAct01 (Att3008 0-1); 225000180"),
            .. Steps(701, 702, 703),
        ],
        [
            new([3000, 3001, 3002], "act2 50 % (10 % single, 20 % two: stop odds of this chain)"),
            new([3000, 3001, 3003], "act2 20 %"),
            new([3004, 3007], "act7 3004 + ComboFinal 3007 (50 %)"),
            new([3005, 3007], "act7 3005 + 3007 (50 %)"),
            new([3006, 3007], "act7 3006 + 3007 (50 %)"),
        ],
        [
            new(10, 3000, "ai103000 action 10 (approach 2.2 m)"),
            new(11, 3004, "ai103000 action 11 (approach 2.3-3.4 m)"),
            new(12, 3003, "ai103000 action 12 (approach 2.2 m)"),
        ]),
        Armed = [new(250010, 250022, [20], 103010, "broken straight sword"), new(250025, 250025, [20], 103010, "broken straight sword (return trip)"),
            .. IfBurg<Ds1AttackParams.ArmedVariant>(new Ds1AttackParams.ArmedVariant(250060, 250000, [20], 103010, "broken straight sword (Burg)"), new Ds1AttackParams.ArmedVariant(250050, 250050, [20], 103010, "broken straight sword (Burg)"),
                new Ds1AttackParams.ArmedVariant(250053, 250053, [20], 103010, "broken straight sword (Burg black phantom)")),
            .. IfDepths<Ds1AttackParams.ArmedVariant>(new Ds1AttackParams.ArmedVariant(250030, 250030, [20], 103010, "broken straight sword (Depths)"))],
    };

    /// <summary>
    /// Track enemies step 2 (rule E21): DS1's step EzStates as no-hit attack categories. The battle goals play SpinStep 701 / 702 /
    /// 703 (back / left / right: GetWellSpace_Act's backstep and sidestep, the interrupts' Damaged_Step, FindAttack_Step,
    /// RebByOpGuard_Step) and the skeletons ApproachStep / SpinStep 700 (forward); the template network (c1030) has no step state
    /// machine (requests Step / Dodge exist, no SM_Avoid), so each step clip a00_0700-0703 gets an SM_Attack category of its own
    /// (action number from <see cref="Ds1Attacks.Plan.ActionOf"/>), its root motion and DS1 TAE timing like any attack.
    /// </summary>
    static Ds1Attacks.Attack[] Steps(params int[] ids) => [.. ids.Select(id => new Ds1Attacks.Attack(id, id switch
    {
        700 => "step forward (a00_0700)", 701 => "back step (a00_0701)", 702 => "side step left (a00_0702)", _ => "side step right (a00_0703)",
    }, 0, id == 700 ? "290000/290004_battle ApproachStep 700, SpinStep 700 (AI_DIR_TYPE_F)" : $"SpinStep {id} (GetWellSpace_Act / Damaged_Step / FindAttack_Step / RebByOpGuard_Step, aiCommon common_func2.lua)"))];

    /// <summary>Model c2502's DS1 weapons: the broken sword #00# (bone "sword" = R_Weapon) on c1030's WP1 material (culling 20),
    /// the short bow #02# (bones bpw_00 = L_Weapon, bow_string, bow_01, sword) on WP_4200_M (culling 23). DS1's arrow #01# (in the
    /// right hand while drawing; DS1 switches it with TAE draw-mask events, which have no DS2 counterpart we know) is left out.</summary>
    static Dictionary<string, string> AsylumHollowWeapons => new() { ["#00#"] = "WP1_c1030", ["#01#"] = "WP2_c1030", ["#02#"] = "WP_4200_M" }; // session 11 (ret11): + the torch #01# (modelDispMask1 of NpcParam 250023; 70 verts on R_Weapon) on WP2 (culling 21)

    /// <summary>
    /// Track enemies (session 8): the Asylum's archer Hollows 1:1 (DS1 NpcParam 250021 → EnemyParam 250040) on model c2502 (bow #02#
    /// only: culling 23) with anim set c2503 / animId 250300: DS1 animIdOffset 20000, whose only own clips are a02_3000 (draw and
    /// shoot) and a02_3001 (shoot again); idle and locomotion fall back to a00 as DS1 does. TAE Bullet event at 1.767 s, dummy 30,
    /// judge 300 → BehaviorParam 225000300 → an arrow (DS2 Hollow Infantry arrow 110304010 as visual/ballistic template). Battle
    /// goal 250005_battle.lua: Shoot_Act(3000, 3001, 1-4 shots) = Attack 3000, or ComboAttack 3000 → ComboRepeat 3001 … →
    /// ComboFinal 3001, then Wait / LeaveTarget 20 m. Chain [3000, 3001, 3001]: the network's longest route chain has 3 stages, so
    /// DS1's 4th shot is cut (P). Interim aliases: 10 / 11 / 12 / 20 (the DS2 archer's shot action) → 3000.
    /// </summary>
    public static OneToOne AsylumHollowBow => new(2502, [250040, 250024, .. IfBurg(250052)], Ds1RigAnims.HollowUnarmed, 103000) // session 11 (ret11): + 250024 (return trip)
    {
        AnimId = 250300,
        OwnModel = false,
        WeaponMeshes = AsylumHollowWeapons,
        Attacks = new Ds1Attacks.Plan(20000, 25000, 0,
        [
            new(3000, "draw and shoot (a02_3000)", 0, "250005_battle Shoot_Act FirstAttID; TAE Bullet 1.767 s dummy 30 judge 300 → BehaviorParam 225000300"),
            new(3001, "shoot again (a02_3001)", 0, "Shoot_Act ComAttID (ComboRepeat / ComboFinal)"),
            .. Steps(701, 702, 703),
        ],
        [
            new([3000, 3001, 3001], "Shoot_Act with 2-3 shots (DS1 also 4: cut, the template's longest route chain has 3 stages)"),
        ],
        [
            new(10, 3000, "ai103000 action 10"),
            new(11, 3000, "ai103000 action 11"),
            new(12, 3000, "ai103000 action 12"),
            new(20, 3000, "DS2 archer shot action (EnemyBehaviorThirdParam 103040 action 0)"),
        ]),
        Armed = [new(250040, 250021, [23], 103040, "bow"), new(250024, 250024, [23], 103040, "bow (return trip)"), .. IfBurg<Ds1AttackParams.ArmedVariant>(new Ds1AttackParams.ArmedVariant(250052, 250052, [23], 103040, "bow (Burg)"))],
    };

    /// <summary>
    /// Session 11 (track ret11), rule V-ret + R-off: the return trip's torch Hollows 1:1 (DS1 NpcParam 250023 → EnemyParam
    /// 250023, entities 1810201-1810204 on the cliff path) on model c2502 (the torch #01# on R_Weapon, drawn through c1030's WP2
    /// material: culling 21) with anim set c2504 / animId 250400: DS1 animIdOffset 10000 = c2500's a01 set, the only offset set
    /// that owns locomotion (idle 10000, walk 10200-10203, run 10500, turns 10602-10605, steps 10701-10703, guard 12000); roles
    /// resolve id + 10000 first and fall back to a00 (get-up 1771, damage, death), as DS1 does (rule R-off). Battle goal
    /// 250001_battle.lua (Husi_Torch250001Battle, NpcThinkParam 250010): act1 defAct01 3006 (1.35-1.6 m), act2 {2 m, 30 / 70} =
    /// 3000 (30 %) | 3000→3001 (70 %, ComboAttack + ComboFinal), act3 defAct03 3002 (2-6 m), act7 = Approach_and_Attack 3005
    /// (6.7-8.2 m) / 3004 (4.6-6.1 m) / 3003 (2.9-3.9 m); interrupts Damaged_Step / RebByOpGuard_Step 701-703, MissSwing 3000.
    /// behaviorVariationId 25000 (NpcParam 250023: the Hollows' BehaviorParam set). Interim aliases (--no-ds1-ai only): 10 → 3000,
    /// 11 → 3002, 12 → 3003.
    /// </summary>
    public static OneToOne AsylumHollowTorch => new(2502, [250023, .. IfBurg(250001), .. IfDepths(250031, 250033)], Ds1RigAnims.HollowUnarmed, 103000)
    {
        AnimId = 250400,
        OwnModel = false,
        WeaponMeshes = AsylumHollowWeapons,
        Attacks = new Ds1Attacks.Plan(10000, 25000, 0,
        [
            new(3000, "torch swing (a01_3000)", 2f, "250001_battle act2 (Att3000 0-2); MissSwing_Attack 3000"),
            new(3001, "torch swing 2 (a01_3001)", 0, "act2 ComboFinal after 3000 (70 %)"),
            new(3002, "a01_3002", 6f, "act3 defAct03 (Att3002 2-6)"),
            new(3003, "a01_3003", 3.9f, "act7 (Att3003 2.9-3.9)"),
            new(3004, "a01_3004", 6.1f, "act7 (Att3004 4.6-6.1)"),
            new(3005, "a01_3005", 8.2f, "act7 (Att3005 6.7-8.2)"),
            new(3006, "a01_3006", 1.6f, "act1 defAct01 (Att3006 1.35-1.6)"),
            .. Steps(701, 702, 703),
        ],
        [
            new([3000, 3001], "act2 HumanCommon_Approach_and_ComboAtk {2, 0, 30, 70}: ComboAttack 3000 + ComboFinal 3001 (70 %)"),
        ],
        [
            new(10, 3000, "ai103000 action 10"),
            new(11, 3002, "ai103000 action 11"),
            new(12, 3003, "ai103000 action 12"),
        ]),
        Armed = [new(250023, 250023, [21], 103010, "torch"), .. IfBurg<Ds1AttackParams.ArmedVariant>(new Ds1AttackParams.ArmedVariant(250001, 250001, [21], 103010, "torch (Burg)")),
            .. IfDepths<Ds1AttackParams.ArmedVariant>(new Ds1AttackParams.ArmedVariant(250031, 250031, [21], 103010, "torch (Depths)"), new Ds1AttackParams.ArmedVariant(250033, 250033, [21], 103010, "torch (Depths black phantom)"))],
    };

    /// <summary>
    /// DS1's Undead Soldier (c2550: the armoured Asylum/Burg "Hollow Soldier", textures in its chrtpfbdt) on DS2's Hollow
    /// Soldier (c1020). DS1 255030/255031 (masks 1+2: shield + sword WP_A_0201) → 102030 Estoc & Shield (sword on the
    /// Estoc's WP_1400_M, shield on SD_c1020); 255032 (masks 0+1: lance WP_A_0700 + shield) → 102000 Spear & Shield
    /// (lance on the spear's WP_3220_M). DS1's bow (#03#) isn't used in the Asylum.
    /// </summary>
    public static Options UndeadSoldier => new("c2550", "c2550", "c1020", 2550,
        BodyMaterial: "BD_LG", EdgeMaterial: "BD_LG 1bitA 2side",
        WeaponMaterials: new() { ["#00#"] = "WP_3220_M", ["#01#"] = "SD_c1020", ["#02#"] = "WP_1400_M" }, KeepTemplateMeshes: [],
        Variants:
        [
            new(255030, 102030, 255030, 102010, 102000, "sword & shield"),
            new(255031, 102030, 255031, 102010, 102000, "sword & shield"),
            new(255032, 102000, 255032, 102010, 102000, "lance & shield"),
            new(255010, 102030, 255010, 102010, 102000, "sword & shield (Firelink)"),
            // session 11 (burgchr11): the Burg's soldiers (255000/001/002 keep DS1's Firelink scaling 7002, 25504x the Burg's 7003; 255043/044 black phantoms)
            .. IfBurg<Variant>(
                new Variant(255000, 102000, 255000, 102010, 102000, "lance & shield (Burg)"), new Variant(255040, 102000, 255040, 102010, 102000, "lance & shield (Burg)"),
                new Variant(255043, 102000, 255043, 102010, 102000, "lance & shield (Burg black phantom)"),
                new Variant(255001, 102030, 255001, 102010, 102000, "longsword & shield (Burg)"), new Variant(255041, 102030, 255041, 102010, 102000, "longsword & shield (Burg)"),
                new Variant(255044, 102030, 255044, 102010, 102000, "longsword & shield (Burg black phantom)"),
                new Variant(255002, 102040, 255002, 102010, 102000, "crossbow (Burg)"), new Variant(255042, 102040, 255042, 102010, 102000, "crossbow (Burg)")),
        ]) { Rig = UndeadSoldierSword, MoreRigs = [UndeadSoldierLance, .. IfBurg(UndeadSoldierCrossbow)] };

    /// <summary>Model c2551's DS1 weapons (template c1020's materials and culling ids): lance #00# → WP_3220_M (25), heater
    /// shield #01# → SD_c1020 (23), longsword #02# → WP_1400_M (24); DS1's crossbow #03# is not used by these NPCs.</summary>
    static Dictionary<string, string> UndeadSoldierWeapons => Burg
        ? new() { ["#00#"] = "WP_3220_M", ["#01#"] = "SD_c1020", ["#02#"] = "WP_1400_M", ["#03#"] = "WP_4210_M" }   // session 11 (burgchr11): + the crossbow #03# (789 verts on R_weapon2 / bow_string / Bow) on c1020's bow material (culling 22)
        : new() { ["#00#"] = "WP_3220_M", ["#01#"] = "SD_c1020", ["#02#"] = "WP_1400_M" };

    /// <summary>
    /// Track enemies (session 8): the Undead (Hollow) Soldier with longsword and shield 1:1 (DS1 NpcParam 255010 Firelink aqueduct,
    /// 255030 / 255031 Asylum 3F → EnemyParam 255010 / 255030 / 255031) as model c2551 (DS1's body on its 107-bone rig incl. the
    /// cloth bones, which DS1 simulates with c2550_c.hkx and DS2 here only animates; template model c1020 for the archive, its
    /// cloth dropped) / animId 255100: c1030's network resized 55 → 107 bones (c1030's role table applies; c1020's network
    /// would need its own). DS1 animIdOffset 10000 (a01_3000-3005: the longsword set; 3009 shield push and 3200 come from a00, as
    /// DS1 falls back), behaviorVariationId 25500, NpcThinkParam 255001 → 255001_battle.lua: act1 defAct01 3003 (0-2 m, guard
    /// 75 %), act2 3000 | 3000→3001 | 3000→3001→3002 | 3000→3001→3004, act3 3004, act4 3009 / 3004 (guard break), act5 3200
    /// (NonspinningAttack), act7 3005 (2.5-4.1 m). Interim aliases for the DS2 Hollow Soldier AI (ai102000 requests 10, 13, 15,
    /// 16, 17, 20, 22, 50, 51): 10 → 3000, 13 → 3005, 15 → 3003, 16 → 3004, 17 → 3005; 20 / 22 (DS2's crossbow / firebomb)
    /// stay disabled.
    /// </summary>
    public static OneToOne UndeadSoldierSword => new(2551, [255010, 255030, 255031, .. IfBurg(255001, 255041, 255044)], Ds1RigAnims.HollowUnarmed, 103000)
    {
        TemplateNetwork = "c1030",
        WeaponMeshes = UndeadSoldierWeapons,
        Attacks = new Ds1Attacks.Plan(10000, 25500, 0,
        [
            new(3000, "small slash 1, longsword (小攻撃１（ロングソード）)", 2.1f, "255001_battle act2 (Att3000 0-2.1); a01_3000; BehaviorParam 225500300"),
            new(3001, "small slash 2, longsword", 0, "act2 stage 2; 225500310"),
            new(3002, "final thrust (最終段突き)", 0, "act2 stage 3; 225500320"),
            new(3003, "thrusts (連続突き)", 2f, "act1 defAct01 (Att3003 0-2); 225500330"),
            new(3004, "charged thrust (溜めて突き)", 2f, "act3 (Att3004 0-2); act2 stage 3; 225500340"),
            new(3005, "running thrust (助走突き)", 4.1f, "act7 (Att3005 2.5-4.1); 225500350"),
            new(3009, "shield push (盾プッシュ)", 1.8f, "act4 defAct04 GBA (Att3009 0-1.8); a00_3009; 225500190"),
            new(3200, "stance (no hit, a00_3200)", 0, "act5 NonspinningAttack 3200"),
            .. Steps(701, 702, 703),
        ],
        [
            new([3000, 3001, 3002], "act2"),
            // step 2: DS1's act2 arm 3000→3001→3004 is dead code (255001_battle.lua: "elseif fate <= 85" then "elseif fate <= 80"), the
            // translator never reaches it; defAct04 (Approach_and_GuardBreak_Act: GuardBreakAttack 3009 → ComboFinal 3004, low HP) needs this chain
            new([3009, 3004], "defAct04 GuardBreakAttack 3009 + ComboFinal 3004 (act4, HP ≤ 40 %)"),
        ],
        [
            new(10, 3000, "ai102000 action 10 (approach 2.8 m)"),
            new(13, 3005, "ai102000 action 13 (approach 4 m)"),
            new(15, 3003, "ai102000 action 15"),
            new(16, 3004, "ai102000 action 16 (approach 3.2 m)"),
            new(17, 3005, "ai102000 action 17 (approach 5.1 m)"),
        ]),
        Armed =
        [
            new(255010, 255010, [24, 23], 102030, "longsword & shield (Firelink)"),
            new(255030, 255030, [24, 23], 102030, "longsword & shield"),
            new(255031, 255031, [24, 23], 102030, "longsword & shield"),
            .. IfBurg<Ds1AttackParams.ArmedVariant>(new Ds1AttackParams.ArmedVariant(255001, 255001, [24, 23], 102030, "longsword & shield (Burg)"), new Ds1AttackParams.ArmedVariant(255041, 255041, [24, 23], 102030, "longsword & shield (Burg)"),
                new Ds1AttackParams.ArmedVariant(255044, 255044, [24, 23], 102030, "longsword & shield (Burg black phantom)")),
        ],
    };

    /// <summary>
    /// Track enemies (session 8): the Asylum's lance Undead Soldier 1:1 (DS1 NpcParam 255032 → EnemyParam 255032: lance #00# +
    /// shield #01# = culling 25 + 23) on model c2551 with anim set c2552 / animId 255200: DS1 animIdOffset 0 (a00_3000-3009, the
    /// lance set), NpcThinkParam 255011 → 255000_battle.lua (m18_01): 3000 | 3000→3001 | 3000→3001→3002, 3003 / 3003→3004,
    /// 3005 / 3005→3006, 3007 (AttackTunableSpin), 3008, 3009 shield push, 3200. Interim aliases: 10 → 3000, 13 → 3005, 15 →
    /// 3003, 16 → 3007, 17 → 3008.
    /// </summary>
    public static OneToOne UndeadSoldierLance => new(2551, [255032, .. IfBurg(255000, 255040, 255043)], Ds1RigAnims.HollowUnarmed, 103000)
    {
        AnimId = 255200,
        OwnModel = false,
        TemplateNetwork = "c1030",
        WeaponMeshes = UndeadSoldierWeapons,
        Attacks = new Ds1Attacks.Plan(0, 25500, 0,
        [
            new(3000, "small thrust 1, lance (小攻撃１（槍）)", 2.8f, "255000_battle (Att3000 0-2.8); BehaviorParam 225500100"),
            new(3001, "small thrust 2, lance", 0, "chain stage 2; 225500110"),
            new(3002, "small thrust 3, lance", 0, "chain stage 3; 225500120"),
            new(3003, "sweep 1 (薙ぎ払う１)", 2f, "AttackTunableSpin (Att3003 0-2); 225500130"),
            new(3004, "sweep 2", 0, "ComboFinal after 3003; 225500140"),
            new(3005, "stepping thrust (踏み込み突き)", 4.4f, "(Att3005 2.5-4.4); 225500150"),
            new(3006, "follow-up (追い討ち)", 0, "ComboFinal after 3005; 225500160"),
            new(3007, "two-handed thrusts (両手で連続突き)", 3.15f, "AttackTunableSpin (Att3007 0-3.15); 225500170"),
            new(3008, "two-handed stepping thrust", 4.5f, "AttackTunableSpin (Att3008 2.5-4.5); 225500180"),
            new(3009, "shield push (盾プッシュ)", 1.8f, "(Att3009 0-1.8); 225500190"),
            new(3200, "stance (no hit)", 0, "NonspinningAttack 3200"),
            .. Steps(701, 702, 703),
        ],
        [
            new([3000, 3001, 3002], "OnIf / Activate"),
            new([3003, 3004], "ComboAttackTunableSpin 3003 + ComboFinal 3004"),
            new([3005, 3006], "ComboAttack 3005 + ComboFinal 3006"),
        ],
        [
            new(10, 3000, "ai102000 action 10"),
            new(13, 3005, "ai102000 action 13"),
            new(15, 3003, "ai102000 action 15"),
            new(16, 3007, "ai102000 action 16"),
            new(17, 3008, "ai102000 action 17"),
        ]),
        Armed = [new(255032, 255032, [25, 23], 102000, "lance & shield"), .. IfBurg<Ds1AttackParams.ArmedVariant>(new Ds1AttackParams.ArmedVariant(255000, 255000, [25, 23], 102000, "lance & shield (Burg)"),
            new Ds1AttackParams.ArmedVariant(255040, 255040, [25, 23], 102000, "lance & shield (Burg)"), new Ds1AttackParams.ArmedVariant(255043, 255043, [25, 23], 102000, "lance & shield (Burg black phantom)"))],
    };

    /// <summary>
    /// Session 11 (track ret11): DS1's Black Knight (c2792, sword + shield; the Asylum return trip's 1810200 / 1810213,
    /// NpcParam 279070: 355 HP, 1200 souls, itemLotId -1 = drops only through event 11810850) on DS2's Hollow Soldier archive
    /// (c1020) as a regular 1:1 rig: model c2792 (DS1 c2792 is free in DS2; D13), animId 279200 (anibnd c2792, TAE c279200,
    /// sound bank decade 2790 = the DSR-only BK family's own), c1030's network resized 55 → the BK's rig (as the Undead
    /// Soldier: c1030's role table). The DS1 meshes have no "#NN#" display groups (sword `00` on R_weapon, shield on L_Shield),
    /// so they are body meshes, always drawn. Interim model id 2790 (the template port every Options builds; no variant uses
    /// it). DS1 move set: animIdOffset 0 (a00_3000-3007), behaviorVariationId 27900, NpcThinkParam 279000 → battle goal
    /// 279000_battle.lua (BlackKnight_Sword279000Battle): act1 AttackTunableSpin 3005 (0-2.7 m), act2 3000→3001 | 3000→3001→3002 |
    /// 3000→3001→3002→3004 | 3000→3001→3002→3003 (25 % each; the 4-stage chains are cut to 3 stages: the template's longest
    /// route chain, as the archer's), act4 GuardBreakTunable 3006 → ComboFinal 3002 (vs a guarding target), act5 3007
    /// (4.1-6.2 m, move attack), act6 SpinStep 702 → 3004 / 703 → 3007, ActAfter steps 701-703; interrupts MissSwing 3000 /
    /// 3005, UseItem 3000 / 3007. Interim aliases (--no-ds1-ai only): 10 → 3000, 13 → 3007, 15 → 3005, 16 → 3006, 17 → 3007.
    /// </summary>
    public static Options BlackKnight => new("c2792", "c2799", "c1020", 2790,   // textures: the BK family's c2799.chrtpfbdt
        BodyMaterial: "BD_LG", EdgeMaterial: "BD_LG 1bitA 2side", WeaponMaterials: [], KeepTemplateMeshes: [],
        Variants: [new(279070, 102030, 279070, 102010, 102000, "Black Knight (return trip)"), .. IfBurg<Variant>(new Variant(279000, 102030, 279000, 102010, 102000, "Black Knight (Burg)"))])
    {
        Rig = new OneToOne(2792, [279070, .. IfBurg(279000)], Ds1RigAnims.HollowUnarmed, 103000)
        {
            AnimId = 279200,
            IdleFallback = true,   // rule RF1: the BK has no ladder, lying, parry or bleed clips
            TemplateNetwork = "c1030",
            Attacks = new Ds1Attacks.Plan(0, 27900, 0,
            [
                new(3000, "sword combo 1 (a00_3000)", 3.2f, "279000_battle act2 ComboAttackTunableSpin 3000 (Att3000 0-3.2)"),
                new(3001, "sword combo 2", 0, "act2 stage 2"),
                new(3002, "sword combo 3", 0, "act2 stage 3; act4 ComboFinal after 3006"),
                new(3003, "sword combo finisher A", 0, "act2 stage 4 (session 11 burgchr11: rule CH4, 4-stage chain)"),
                new(3004, "sword combo finisher B", 0, "act2 stage 4 (rule CH4); act6 after SpinStep 702 (rule CH5: slot-0 variant)"),
                new(3005, "thrust (a00_3005)", 2.7f, "act1 AttackTunableSpin 3005 (Att3005 0-2.7)"),
                new(3006, "guard break (a00_3006)", 1.6f, "act4 GuardBreakTunable 3006 (Att3006 0-1.6)"),
                new(3007, "running attack (a00_3007)", 6.2f, "act5 (Att3007 4.1-6.2); act6 after SpinStep 703"),
                .. Steps(701, 702, 703),
            ],
            [
                // session 11 (burgchr11): rule CH4 (a route stage added to the c1030 chain clone) makes DS1's 4-stage chains whole; the
                // 2- and 3-stage arms (25 % each) are prefixes of the first ("stop after N"); rule CH5 gives a00_3002 a slot-1 TimeAct
                // variant for act4's GuardBreakTunable 3006 + ComboFinal 3002 (stage 3 of the act2 chains, stage 2 here)
                new([3000, 3001, 3002, 3004], "act2 ComboAttackTunableSpin 3000 → ComboRepeat 3001 → ComboRepeat 3002 → ComboFinal 3004 (25 %; prefixes 3000→3001 / →3002 25 % each)"),
                new([3000, 3001, 3002, 3003], "act2 ComboAttack 3000 → 3001 → 3002 → ComboFinal 3003 (25 %)"),
                new([3006, 3002], "act4 GuardBreakTunable 3006 (isComboAttack) → ComboFinal 3002"),
            ],
            [
                new(10, 3000, "ai102000 action 10"),
                new(13, 3007, "ai102000 action 13"),
                new(15, 3005, "ai102000 action 15"),
                new(16, 3006, "ai102000 action 16"),
                new(17, 3007, "ai102000 action 17"),
            ], TurnRate: 120f),   // session 11 (burgchr11): DS1 NpcParam 279070 / 279000 turnVellocity 120 (was the 180 default)
            Armed = [new(279070, 279070, [], 102030, "sword & shield"), .. IfBurg<Ds1AttackParams.ArmedVariant>(new Ds1AttackParams.ArmedVariant(279000, 279000, [], 102030, "sword & shield (Burg)"))],
        },
    };

    /// <summary>
    /// Session 11 (track burgchr11): the Burg's crossbow Undead Soldiers (DS1 NpcParam 255002 / 255042: animIdOffset 20000, modelDispMask3 =
    /// the crossbow #03#) on model c2551 with anim set c2553 / animId 255300 (research r1). Battle goals 255006 (thinks 255004 / 255007 /
    /// 255030, bow only) and 255002 (think 255002, the hybrid; m10_01 only): Shoot_Act 3000 → 3001 … (2-5 shots) and 3002 → 3003 … (think
    /// 255004; 255002 50 %), 3200 stance at HP ≤ 40 %, Damaged_Step / FindAttack_Step 701-703. DS1 a02_3000 / 3002 fire at 1.233 s, 3001 /
    /// 3003 reload and fire at 2.6 s, all from dummy 20 (bone "Arrow") with judge 400 → BehaviorParam 225500400 → Bullet 25500 (the
    /// Hollow arrow's ballistics, AtkParam_Npc 25500 phys 120). Shots have no range (as the archer's). Chains: a ShootNum of 4-5 (255006
    /// act01/02) uses the rule CH4 4-stage chain (5th shot cut: EnemyComboBehaviorParam holds 4 stages). Not covered (reported): goal
    /// 255002's melee mode (a00_3011 applies SpEffect 5401 = animIdOffset 10000 and switches the mesh to the #02# longsword; the a01
    /// longsword ids would collide with the a02 shot ids; the AI translator assumes HasSpecialEffectId false, so it is never entered).
    /// DS2 param donor: EnemyParam 102040 Hollow Soldier (Bow).
    /// </summary>
    public static OneToOne UndeadSoldierCrossbow => new(2551, [255002, 255042], Ds1RigAnims.HollowUnarmed, 103000)
    {
        AnimId = 255300,
        OwnModel = false,
        TemplateNetwork = "c1030",
        WeaponMeshes = UndeadSoldierWeapons,
        Attacks = new Ds1Attacks.Plan(20000, 25500, 0,
        [
            new(3000, "crossbow shot (a02_3000)", 0, "255002/255006_battle Shoot_Act FirstAttID 3000; TAE Bullet 1.233 s dummy 20 judge 400 → BehaviorParam 225500400 → Bullet 25500"),
            new(3001, "reload and shoot (a02_3001)", 0, "Shoot_Act ComAttID (ComboRepeat / ComboFinal); TAE Bullet 2.6 s dummy 20 judge 400"),
            new(3002, "crossbow shot B (a02_3002)", 0, "Shoot_Act FirstAttID 3002 (think 255004; 255002 50 %); TAE Bullet 1.233 s"),
            new(3003, "reload and shoot B (a02_3003)", 0, "Shoot_Act ComAttID after 3002; TAE Bullet 2.6 s"),
            new(3200, "stance (no hit, a00_3200)", 0, "255002 act10 / 255006 act04 NonspinningAttack 3200 (HP ≤ 40 %)"),
            .. Steps(701, 702, 703),
        ],
        [
            new([3000, 3001, 3001, 3001], "Shoot_Act 2-4 shots (prefixes; 255006 ShootNum 5: 5th shot cut, rule CH4's 4 stages)"),
            new([3002, 3003, 3003, 3003], "Shoot_Act variant B"),
        ],
        [
            new(10, 3000, "ai102000 action 10"),
            new(13, 3000, "ai102000 action 13"),
            new(20, 3000, "DS2 bow soldier shot (EnemyBehaviorThirdParam 102040 action 0)"),
        ]),
        Armed = [new(255002, 255002, [22], 102040, "crossbow (Burg)"), new(255042, 255042, [22], 102040, "crossbow (Burg)")],
    };

    /// <summary>
    /// Session 11 (track burgchr11): DS1's greatsword Black Knight (c2793, NpcParam 279001: 355 HP, 800 souls, drops only through EMEVD
    /// 11010860 lot 27900100; entity 1010351 in the Burg, think 279001 → 279001_battle.lua BlackKnight_Lar_Sw279001Battle) as a regular
    /// 1:1 rig like the sword Black Knight: model c2793 (free in DS2), anim set c2770 / animId 277010 (decade 2770: free in DS2 and DSR,
    /// its own sound bank - the BK family's decade 2790 holds c2792's project, rule S10), c1030's network resized to its 66-bone rig,
    /// textures c2799 (rule MT1). DS1 animIdOffset 10000 but c2793 has no a01 clips: everything is a00 (rule R-off, plan offset 0). The
    /// greatsword is display group #01# (595 verts, NpcParam modelDispMask1) → c1020's WP_1800_M (culling 21). Attacks 3000-3014 from the
    /// Lua (ranges Att* maxima); chains (7 combo rows, prefixes cover the 2-stage arms): act2 3000→3001→3002→3003 / →3009 (rule CH4
    /// 4-stage), 3000→3001→3004 / →3008, 3000→3003, 3000→3009, act6 3005→3006→3002; not a row (played as single attacks, reported):
    /// act6 3005→3006→3004 / →3008 and act4 GuardBreakTunable 3012 → 3014 (IsTargetGuard is assumed false: never chosen).
    /// </summary>
    public static Options BlackKnightGreatsword => new("c2793", "c2799", "c1020", 2770,
        BodyMaterial: "BD_LG", EdgeMaterial: "BD_LG 1bitA 2side", WeaponMaterials: [], KeepTemplateMeshes: [],
        Variants: [new(279001, 102010, 279001, 102010, 102000, "Black Knight greatsword (Burg)")])
    {
        Rig = new OneToOne(2793, [279001], Ds1RigAnims.HollowUnarmed, 103000)
        {
            AnimId = 277010,
            IdleFallback = true,
            TemplateNetwork = "c1030",
            WeaponMeshes = new() { ["#01#"] = "WP_1800_M" },
            Attacks = new Ds1Attacks.Plan(0, 27900, 0,
            [
                new(3000, "swing from right 1 (右から払う１)", 3.6f, "279001_battle act2 ComboAttackTunableSpin 3000 (Att3000 0-3.6)"),
                new(3001, "swing from left 1 (左から払う１)", 0, "act2 stage 2"),
                new(3002, "swing from right 2 (右から払う２)", 0, "act2 / act6 stage 3"),
                new(3003, "spin and overhead (１回転して振り下ろす)", 0, "act2 ComboFinal stage 2 / stage 4"),
                new(3004, "overhead (上から振り下ろす)", 0, "act2 / act6 stage 3"),
                new(3005, "overhead, single (上から振り下ろす・単)", 4.1f, "act6 ComboAttackTunableSpin 3005 (Att3005 0-4.1); MissSwing / UseItem"),
                new(3006, "swing from left 3 (左から払う３)", 0, "act6 stage 2"),
                new(3007, "thrust, single (突く・単)", 3.7f, "act5 Attack 3007 (Att3007 1.6-3.7)"),
                new(3008, "thrust, combo (突く・コンボ)", 0, "act2 / act6 stage 3"),
                new(3009, "spin and thrust (１回転して突く)", 0, "act2 stage 2 / stage 4"),
                new(3010, "2-3 steps and overhead (２、３歩踏み込んで振り下ろす)", 8.3f, "act1 AttackTunableSpin 3010 (Att3010 6.5-8.3); UseItem"),
                new(3011, "2-3 steps and thrust (２、３歩踏み込んで突く)", 6.7f, "defAct03 3011 (4.4-6.7)"),
                new(3012, "shield bash (盾で殴る)", 1.7f, "act4 GuardBreakTunable 3012 (IsTargetGuard: assumed false)"),
                new(3013, "one-handed upthrust (竜狩り・片手突き上げ)", 3f, "act7 Attack 3013 (Att3013 0-3)"),
                new(3014, "two-handed upswing (竜狩り・両手振り上げ)", 3f, "act8 Attack 3014 (Att3014 0-3); act4 ComboFinal after 3012"),
                .. Steps(701, 702, 703),
            ],
            [
                new([3000, 3001, 3002, 3003], "act2 3000 → 3001 → 3002 → 3003 (15 %; prefixes 3000→3001 9 %, 3000→3001→3002 13 %)"),
                new([3000, 3001, 3002, 3009], "act2 → 3009 (15 %)"),
                new([3000, 3001, 3004], "act2 (13 %)"),
                new([3000, 3001, 3008], "act2 (13 %)"),
                new([3000, 3003], "act2 (12 %)"),
                new([3000, 3009], "act2 (9 %)"),
                new([3005, 3006, 3002], "act6 (23 %; prefix 3005→3006 30 %)"),
            ],
            [
                new(10, 3000, "ai102000 action 10"),
                new(13, 3010, "ai102000 action 13"),
                new(15, 3005, "ai102000 action 15"),
                new(16, 3012, "ai102000 action 16"),
                new(17, 3011, "ai102000 action 17"),
            ], TurnRate: 120f),
            Armed = [new(279001, 279001, [21], 102010, "greatsword (Burg)")],
        },
    };

    // ================================================================== session 11 (track burgchr11): the Burg's / Parish's new humanoids
    // Specs from research r2 (work\research\r2: Lua paths, TAE dumps, NpcParam / SpEffect / Bullet rows). Ids by rule I-chr: model = DS1
    // chr id (DS2 has no c23xx-c29xx model, anibnd, TAE or bank), EnemyParam = NpcParam id, anim decade = the model's (own FMOD
    // project per decade, rule S10), interim template-port model = DS1 id + 9 (unused by any variant; the Merchant 2518: c2519 is
    // DS1's merchant texture chr). Not covered and reported: parry 3100's riposte / backstab throws 3110 (no clip: an engine state),
    // the 3200 heal stance's heal, guard-break acts (IsTargetGuard assumed false), the Channeler's ally buff SpEffects.

    /// <summary>
    /// DS1's Balder Knight (c2560: Parish; NpcParam 256000 side sword + Balder shield, 256002 rapier + buckler, 256004 the rapier black
    /// phantom; behaviorVariationId 25600) on c1030's archive and network (116 bones incl. 61 cloth bones, animated only). Display
    /// groups: #00# side sword → WP1 (culling 20), #02# rapier → WP2 (21), #03# Balder shield → "SD cull24" (24), #04# buckler →
    /// WP_4200_M (23), #07# the cape (two meshes on ~37 cloth bones; NpcParam modelDispMask7 on for every variant) → the body
    /// material (culling 0 = always drawn; it is body, not a weapon). #01# crossbow: no m10_01 NpcParam shows it (goal 256001 unused).
    /// </summary>
    public static Options BalderKnight => new("c2560", "c2560", "c1030", 2569,
        BodyMaterial: "BD_LG", EdgeMaterial: "BD_LG_1bit_2side",
        WeaponMaterials: new() { ["#00#"] = "WP1_c1030", ["#02#"] = "WP2_c1030", ["#03#"] = "SD cull24", ["#04#"] = "WP_4200_M" }, KeepTemplateMeshes: [],
        Variants:
        [
            new(256000, 103020, 256000, 103010, 103000, "side sword & Balder shield (Parish)"),
            new(256002, 102030, 256002, 103010, 103000, "rapier & buckler (Parish)"),
            new(256004, 102030, 256004, 103010, 103000, "rapier & buckler (black phantom)"),
        ]) { Rig = BalderSword, MoreRigs = [BalderRapier] };

    static Dictionary<string, string> BalderWeapons => new() { ["#00#"] = "WP1_c1030", ["#02#"] = "WP2_c1030", ["#03#"] = "SD cull24", ["#04#"] = "WP_4200_M", ["#07#"] = "BD_LG" };

    /// <summary>Balder Knight, side sword (NpcParam 256000, thinks 256000 / 256003 / 256008 → 256000_battle; animIdOffset 0): anim set
    /// c2560 / 256000. defAct02 3000 → 3001 → 3002 (10 / 30 / 60 %), act3 3003, act7 3004, act1 3005 (two hits), act4 GuardBreakAttack
    /// 3006 → 3003 (only vs a guarding target; 3006 has no JT 23), act8 3200 at HP ≤ 40 %.</summary>
    public static OneToOne BalderSword => new(2560, [256000], Ds1RigAnims.HollowUnarmed, 103000)
    {
        AnimId = 256000,
        WeaponMeshes = BalderWeapons,
        Attacks = new Ds1Attacks.Plan(0, 25600, 0,
        [
            new(3000, "small slash 1 (小攻撃１)", 2.2f, "256000_battle defAct02 {2.2, 75, 10, 30} (Att3000 0-2.2); BehaviorParam 225600100"),
            new(3001, "small slash 2 (小攻撃２)", 0, "defAct02 stage 2; 225600110"),
            new(3002, "small slash 3 (小攻撃３)", 0, "defAct02 stage 3; 225600120"),
            new(3003, "two-handed strong attack (両手で強攻撃)", 2.4f, "act3 defAct03 (Att3003 0-2.4); 225600130"),
            new(3004, "2-3 steps and thrust (２、３歩前進して突き)", 4.5f, "act7 (Att3004 2.5-4.5); 225600140"),
            new(3005, "double slash (２連続斬り)", 2.3f, "act1 defAct01 (Att3005 0-2.3); 2 hits j150; 225600150"),
            new(3006, "shield push (盾プッシュ)", 1.1f, "act4 GuardBreakAttack 3006 → ComboFinal 3003 (vs a guarding target); 225600160"),
            new(3200, "heal stance (no hit; a00_3200)", 0, "act8 NonspinningAttack 3200 at HP ≤ 40 %"),
            .. Steps(701, 702, 703),
        ],
        [
            new([3000, 3001, 3002], "defAct02: 10 % single, 30 % two, 60 % three"),
            new([3006, 3003], "act4 GuardBreakAttack (isComboAttack) → ComboFinal 3003 (3006 has no JT 23: the stage starts at 3006's earliest cancel)"),
        ],
        [new(10, 3000, "ai103000 action 10"), new(11, 3004, "ai103000 action 11"), new(12, 3003, "ai103000 action 12")]),
        Armed = [new(256000, 256000, [20, 24], 103020, "side sword & Balder shield")],
    };

    /// <summary>Balder Knight, rapier (NpcParams 256002 / 256004, thinks 256002 / 256007 → 256002_battle; animIdOffset 20000 = a02
    /// 23000-23005 and the a02 guard set): anim set c2562 / 256200. defAct02 3000 → 3001 → 3002, act7 3003, act1 3004 (JT 24), act3
    /// 3005 (guarding), Parry_Act 3100 (not translated: GOAL_COMMON_Parry has no DS2 machine; its clip is in the plan), act4 3200.</summary>
    public static OneToOne BalderRapier => new(2560, [256002, 256004], Ds1RigAnims.HollowUnarmed, 103000)
    {
        AnimId = 256200,
        OwnModel = false,
        WeaponMeshes = BalderWeapons,
        Attacks = new Ds1Attacks.Plan(20000, 25600, 0,
        [
            new(3000, "thrust 1, rapier (突き１（レイピア）)", 2.1f, "256002_battle defAct02 {2.1, 75, 10, 30}; a02_3000; 225600300"),
            new(3001, "thrust 2 (突き２)", 0, "defAct02 stage 2; 225600310"),
            new(3002, "thrust 3 (突き３)", 0, "defAct02 stage 3; 225600320"),
            new(3003, "running full thrust (助走をつけて思いきり突く)", 5.5f, "act7 (Att3003 3.5-5.5); 225600330"),
            new(3004, "backstep thrust (バックステップ突き)", 2.4f, "act1 defAct01 (Att3004 1.5-2.4); JT 24 1.10-1.43 s; 225600340"),
            new(3005, "thrust while guarding (ガードしながら突く)", 1.3f, "act3 defAct03 (Att3005 0-1.3); 225600350"),
            new(3100, "parry (パリィ; a00_3100)", 0, "Parry_Act GOAL_COMMON_Parry 3100 (0-damage probe j900: the hit row does no damage, P)"),
            new(3200, "heal stance (no hit; a00_3200)", 0, "act4 NonspinningAttack 3200 at HP ≤ 40 %"),
            .. Steps(701, 702, 703),
        ],
        [new([3000, 3001, 3002], "defAct02: 10 / 30 / 60 %")],
        [new(10, 3000, "ai103000 action 10"), new(11, 3003, "ai103000 action 11"), new(12, 3004, "ai103000 action 12")]),
        Armed = [new(256002, 256002, [21, 23], 102030, "rapier & buckler"), new(256004, 256004, [21, 23], 102030, "rapier & buckler (black phantom)")],
    };

    /// <summary>
    /// DS1's Heavy Knight / "Berenike" knight (c2570: Parish 1010320 NpcParam 257001 great mace + tower shield, black phantom 1010905
    /// 257002 greatsword + tower shield; behaviorVariationId 25700, 310 HP, turnVellocity 120, superArmorDurability 130, resident 5333 =
    /// every hit plays the medium reaction once the poise breaks, 5360 = none while it holds) on c1020's archive (rule MT1: two body
    /// diffuse textures, c2570.chrtpfbdt) and c1030's network resized to its 89-bone rig. #00# greatsword → WP_1400_M (24), #02# great
    /// mace → WP_3220_M (25), #01# tower shield → SD_c1020 (23); its weapon bone "R weapon" is DS2's R_Weapon (Ds2BoneName). No steps,
    /// ladder or throw-victim clips (rule RF1).
    /// </summary>
    public static Options HeavyKnight => new("c2570", "c2570", "c1020", 2579,
        BodyMaterial: "BD_LG", EdgeMaterial: "BD_LG 1bitA 2side",
        WeaponMaterials: new() { ["#00#"] = "WP_1400_M", ["#01#"] = "SD_c1020", ["#02#"] = "WP_3220_M" }, KeepTemplateMeshes: [],
        Variants:
        [
            new(257001, 102030, 257001, 102010, 102000, "great mace & tower shield (Parish)"),
            new(257002, 102030, 257002, 102010, 102000, "greatsword & tower shield (black phantom)"),
        ]) { Rig = HeavyKnightMace, MoreRigs = [HeavyKnightSword] };

    static Dictionary<string, string> HeavyKnightWeapons => new() { ["#00#"] = "WP_1400_M", ["#01#"] = "SD_c1020", ["#02#"] = "WP_3220_M" };

    /// <summary>Heavy Knight, great mace (NpcParam 257001, think 257001 → 257001_battle; animIdOffset 10000 = a01_3000-3004, the rest
    /// a00): anim set c2570 / 257000. act2 3000 → 3001 (60 %), act3 3002, act9 3003 → 3004 (60 %), act7 3005, act4 GuardBreakTunable
    /// 3007 (vs guard) → Wait → 3002, act5 3200. a01_3005 is a TAE-only entry (imports a00_3005's motion with judge 250): the plan plays
    /// a00_3005 with its own TAE (judge 150; reported).</summary>
    public static OneToOne HeavyKnightMace => new(2570, [257001], Ds1RigAnims.HollowUnarmed, 103000)
    {
        AnimId = 257000,
        IdleFallback = true,
        TemplateNetwork = "c1030",
        WeaponMeshes = HeavyKnightWeapons,
        Attacks = new Ds1Attacks.Plan(10000, 25700, 0,
        [
            new(3000, "downswing 1, great mace (振り下ろし１（大メイス）)", 3.2f, "257001_battle act2 (Att3000 0-3.2); a01_3000; 225700200"),
            new(3001, "downswing 2 (振り下ろし２)", 0, "act2 ComboFinal; 225700210"),
            new(3002, "charged full downswing (溜めて思い切り振り下ろし)", 3.3f, "act3 AttackTunableSpin (Att3002 0-3.3); 225700220"),
            new(3003, "sweep 1, great mace (薙ぎ払い１（大メイス）)", 3.2f, "act9 (approach Att3000 3.2); 225700230"),
            new(3004, "sweep 2", 0, "act9 ComboFinal; 225700240"),
            new(3005, "stepping attack (踏み込み攻撃)", 7.8f, "act7 (Att3005 6-7.8); a00_3005 (a01_3005 is TAE-only)"),
            new(3007, "shield bash (盾で殴る; a00_3007)", 1.2f, "act4 GuardBreakTunable (vs a guarding target); 225700170"),
            new(3200, "heal stance (no hit)", 0, "act5 NonspinningAttack 3200 at HP ≤ 40 % (TAE SpEffect 5163)"),
        ],
        [new([3000, 3001], "act2 60 %"), new([3003, 3004], "act9 60 %")],
        [new(10, 3000, "ai102000 action 10"), new(13, 3005, "ai102000 action 13"), new(15, 3003, "ai102000 action 15"), new(16, 3002, "ai102000 action 16"), new(17, 3005, "ai102000 action 17")], TurnRate: 120f),
        Armed = [new(257001, 257001, [25, 23], 102030, "great mace & tower shield")],
    };

    /// <summary>Heavy Knight, greatsword (black phantom NpcParam 257002, think 257000 → 257000_battle; animIdOffset 0): anim set c2571 /
    /// 257100. act1 3000 → 3002 (60 %), act3 3001, act2 3003 → 3004 (60 %), act7 3005, act4 GuardBreakTunable 3007 → 3006, act5 3200.</summary>
    public static OneToOne HeavyKnightSword => new(2570, [257002], Ds1RigAnims.HollowUnarmed, 103000)
    {
        AnimId = 257100,
        OwnModel = false,
        IdleFallback = true,
        TemplateNetwork = "c1030",
        WeaponMeshes = HeavyKnightWeapons,
        Attacks = new Ds1Attacks.Plan(0, 25700, 0,
        [
            new(3000, "downswing from upper right, greatsword (右上から振り下ろし（大剣）)", 3.3f, "257000_battle act1 (Att3000 0-3.3); 225700100"),
            new(3001, "charged overhead downswing (溜めて真上から振り下ろし)", 3.9f, "act3 AttackTunableSpin (Att3001 0-3.9); 225700110"),
            new(3002, "sweep from left (左から払い)", 0, "act1 ComboFinal; 225700120"),
            new(3003, "sweep 1 (薙ぎ払い１)", 3f, "act2 (Att3003 0-3); 225700130"),
            new(3004, "sweep 2 (薙ぎ払い２)", 0, "act2 ComboFinal; 225700140"),
            new(3005, "stepping downswing (踏み込んで振り下ろし)", 7.8f, "act7 (Att3005 6-7.8); 225700150"),
            new(3006, "shield bash then slash (盾殴りから斬る)", 0, "act4 ComboFinal after 3007; 225700160"),
            new(3007, "shield bash (盾で殴る)", 1.2f, "act4 GuardBreakTunable (vs a guarding target); 225700170"),
            new(3200, "heal stance (no hit)", 0, "act5 at HP ≤ 40 %"),
        ],
        [new([3000, 3002], "act1 60 %"), new([3003, 3004], "act2 60 %"), new([3007, 3006], "act4 GuardBreakTunable → ComboFinal 3006")],
        [new(10, 3000, "ai102000 action 10"), new(13, 3005, "ai102000 action 13"), new(15, 3003, "ai102000 action 15"), new(16, 3001, "ai102000 action 16"), new(17, 3005, "ai102000 action 17")], TurnRate: 120f),
        Armed = [new(257002, 257002, [24, 23], 102030, "greatsword & tower shield (black phantom)")],
    };

    /// <summary>
    /// DS1's Channeler (c2370, Parish 1010370, NpcParam 237000 "FourKnightD": 155 HP, 1000 souls, turnVellocity 120, resident 5308 =
    /// medium-or-heavier reactions after poise 45 breaks) on c1020's archive (rule MT1: five diffuse textures in c2370.chrtpfbdt) and
    /// c1030's network resized to its 153-bone rig (no ForeTwist bones). #00# trident (+ ribbon on 12 W_Cloth bones) → WP_3220_M (25).
    /// Sorcery: a00_3005 / 3006 fire Bullet 23700 / 23701 (magic, AtkParam_Npc 23700 / 23701 atkMag 160 / 190) from dummy 20 → DS2
    /// magic bullet visual (rule E10c); a00_3007 = the ally buff (Bullet 23702, radius 0.2 → 20 m, SpEffect 5470 self / 5471 allies:
    /// physicsAttackPowerRate 1.85 for 60 s): the clip plays, the buff is not ported (bullet SpEffects; reported). Hops 711-723.
    /// </summary>
    public static Options Channeler => new("c2370", "c2370", "c1020", 2379,
        BodyMaterial: "BD_LG", EdgeMaterial: "BD_LG 1bitA 2side", WeaponMaterials: new() { ["#00#"] = "WP_3220_M" }, KeepTemplateMeshes: [],
        Variants: [new(237000, 102000, 237000, 102010, 102000, "trident + sorcery (Parish)")])
    {
        Rig = new OneToOne(2370, [237000], Ds1RigAnims.HollowUnarmed, 103000)
        {
            AnimId = 237000,
            IdleFallback = true,
            TemplateNetwork = "c1030",
            WeaponMeshes = new() { ["#00#"] = "WP_3220_M" },
            Attacks = new Ds1Attacks.Plan(0, 23700, 0,
            [
                new(3000, "thrust 1 (突き１)", 3.1f, "237000_battle defAct02 {3.1, 0, 20, 40} (Att3000 0-3.1); 223700100"),
                new(3001, "thrust 2 (突き２)", 0, "defAct02 stage 2; 223700110"),
                new(3002, "sweep (払い)", 0, "defAct02 stage 3; 223700120"),
                new(3003, "sweep 1 (なぎ払い１)", 3.5f, "act9 defAct09 (Att3003 0-3.5); 223700130"),
                new(3004, "sweep 2 (なぎ払い２)", 0, "act9 ComboFinal 60 %; 223700140"),
                new(3005, "magic launch 1 (魔法発射１)", 4f, "act1 (Att3005 2.5-4); TAE Bullet dummy 20 judge 150 → Bullet 23700"),
                new(3006, "magic launch 2, long (魔法発射２（長い）)", 6f, "act3 (Att3006 4-6); TAE Bullet judge 160 → Bullet 23701"),
                new(3007, "ally buff (魔法だワッショイ)", 7.5f, "act5 ChkNearLowHpFriend (untranslated: never chosen); Bullet judge 170 → 23702 (SpEffect 5470 / 5471 not ported)"),
                new(3009, "one-handed spinning thrust, 3 hits (片手回転突き)", 4.1f, "act7 (Att3009 0-4.1); 223700180 ×3"),
                new(701, "back step (a00_0701)", 0, "OnIf step picker"), new(702, "side step left (a00_0702)", 0, "OnIf"), new(703, "side step right (a00_0703)", 0, "OnIf"),
                new(711, "medium hop back (a00_0711)", 0, "act4 30 % at 5-10 m"), new(712, "medium hop left (a00_0712)", 0, "OnIf"), new(713, "medium hop right (a00_0713)", 0, "OnIf"),
                new(721, "long hop back (a00_0721)", 0, "act4 40 % ≤ 5 m; act5"), new(722, "long hop left (a00_0722)", 0, "OnIf"), new(723, "long hop right (a00_0723)", 0, "OnIf"),
            ],
            [new([3000, 3001, 3002], "defAct02: 20 / 40 / 40 %"), new([3003, 3004], "act9: 40 % single, 60 % two")],
            [new(10, 3000, "ai102000 action 10"), new(13, 3006, "ai102000 action 13"), new(15, 3003, "ai102000 action 15"), new(16, 3009, "ai102000 action 16"), new(17, 3005, "ai102000 action 17")], TurnRate: 120f),
            Armed = [new(237000, 237000, [25], 102000, "trident + sorcery")],
        },
    };

    /// <summary>
    /// DS1's Undead Assassin / thief (c2520, Lower Burg; NpcParam 252000, black phantom 252001; thinks 252000 / 252010 → 252000_battle
    /// "Husi_robber") on c1030's archive and network (51 bones). #00# Bandit's Knife → WP1 (20), #01# Target Shield → "SD cull24" (24).
    /// act2 3000 → 3001 → 3002 / → 3003, 3000 → 3004; act3 3003 (→ 3004 act9); act1 dash thrust 3005; act5 throwing knife 3006 (Bullet
    /// 25200 from dummy 11); Parry_Act 3100 (not translated); act7 3200.
    /// </summary>
    public static Options UndeadAssassin => new("c2520", "c2520", "c1030", 2529,
        BodyMaterial: "BD_LG", EdgeMaterial: "BD_LG_1bit_2side", WeaponMaterials: new() { ["#00#"] = "WP1_c1030", ["#01#"] = "SD cull24" }, KeepTemplateMeshes: [],
        Variants: [new(252000, 103020, 252000, 103010, 103000, "knife & target shield (Lower Burg)"), new(252001, 103020, 252001, 103010, 103000, "knife & target shield (black phantom)")])
    {
        Rig = new OneToOne(2520, [252000, 252001], Ds1RigAnims.HollowUnarmed, 103000)
        {
            AnimId = 252000,
            WeaponMeshes = new() { ["#00#"] = "WP1_c1030", ["#01#"] = "SD cull24" },
            Attacks = new Ds1Attacks.Plan(0, 25200, 0,
            [
                new(3000, "small attack 1 (小攻撃１)", 1.5f, "252000_battle act2 (Att3000 0-1.5); 225200100"),
                new(3001, "small attack 2 (小攻撃２)", 0, "act2 stage 2; 225200110"),
                new(3002, "small attack 3 (小攻撃３)", 0, "act2 ComboFinal (40 %); 225200120"),
                new(3003, "strong attack 1 (強攻撃１)", 1.5f, "act3 defAct03 (Att3003 0-1.5); act9 → 3004; act2 final (20 %); 225200130"),
                new(3004, "strong attack 2 (強攻撃２)", 0, "act9 ComboFinal; act2 3000 → 3004 (10 %); 225200140"),
                new(3005, "dash thrust (ダッシュ突き)", 3.9f, "act1 (Att3005 3-3.9); 225200150"),
                new(3006, "throwing knife (投げナイフ)", 15f, "act5 5-15 m; TAE Bullet dummy 11 judge 160 → Bullet 25200"),
                new(3100, "parry (パリィ; a00_3100)", 0, "Parry_Act GOAL_COMMON_Parry 3100 (0-damage probe j900)"),
                new(3200, "heal stance (no hit)", 0, "act7 at HP ≤ 40 %"),
                .. Steps(701, 702, 703),
            ],
            [
                new([3000, 3001, 3002], "act2 40 % (+ 30 % stopping at 3001)"), new([3000, 3001, 3003], "act2 20 %"),
                new([3000, 3004], "act2 10 %"), new([3003, 3004], "act9"),
            ],
            [new(10, 3000, "ai103000 action 10"), new(11, 3005, "ai103000 action 11"), new(12, 3003, "ai103000 action 12")]),
            Armed = [new(252000, 252000, [20, 24], 103010, "Bandit's Knife & Target Shield"), new(252001, 252001, [20, 24], 103010, "Bandit's Knife & Target Shield (black phantom)")],
        },
    };

    /// <summary>
    /// DS1's Undead Merchant as an enemy (c2510, NpcParam 251001: 360 HP, Uchigatana sure drop flag 51010960; entity 6230, think 251000
    /// → 251000_battle "HusibitoKojiki", InitAnimID 9000 sitting; textures c2519 = NpcParam normalChangeTexChrId) on c1030's archive and
    /// network (56-bone hunched rig). DS1 makes the talk NPC hostile by event (11010510); NpcPorter ports the talk NPC (c0001 body), this
    /// is the 1:1 fighting body: which one M3 places when hostile is a decision (D2-like). #00# Uchigatana ("#00# 日本刀 ") → WP1 (20).
    /// defAct02 3000 → 3001 (70 %), act1 3002, act3 3003, act7 leap 3004.
    /// </summary>
    public static Options MerchantHostile => new("c2510", "c2519", "c1030", 2518,
        BodyMaterial: "BD_LG", EdgeMaterial: "BD_LG_1bit_2side", WeaponMaterials: new() { ["#00# 日本刀 "] = "WP1_c1030" }, KeepTemplateMeshes: [],
        Variants: [new(251001, 103010, 251001, 103010, 103000, "uchigatana (hostile Undead Merchant)")])
    {
        Rig = new OneToOne(2510, [251001], Ds1RigAnims.HollowUnarmed, 103000)
        {
            AnimId = 251000,
            IdleFallback = true,
            WeaponMeshes = new() { ["#00# 日本刀 "] = "WP1_c1030" },
            Attacks = new Ds1Attacks.Plan(0, 25100, 0,
            [
                new(3000, "slash 1 (斬撃１)", 2.3f, "251000_battle defAct02 {2.3, 0, 30, 70} (Att3000 0-2.3); 225100100"),
                new(3001, "slash 2 (斬撃２)", 0, "defAct02 ComboFinal 70 %; 225100110"),
                new(3002, "two-handed slash (両手斬り)", 2.6f, "act1 defAct01 (Att3002 0-2.6); 225100120"),
                new(3003, "two-handed thrust (両手突き)", 2.2f, "act3 defAct03 (Att3003 0-2.2); 225100130"),
                new(3004, "leaping attack (飛び掛り)", 3.8f, "act7 (Att3004 0-3.8); JT 24; 225100140"),
                .. Steps(701, 702, 703),
            ],
            [new([3000, 3001], "defAct02: 30 % single, 70 % two")],
            [new(10, 3000, "ai103000 action 10"), new(11, 3004, "ai103000 action 11"), new(12, 3002, "ai103000 action 12")]),
            Armed = [new(251001, 251001, [20], 103010, "uchigatana")],
        },
    };

    /// <summary>
    /// DS1's Hollow Warrior (c2540, textures in its chrtpfbdt: Firelink's stair hollows, also the Burg) on DS2's Hollow
    /// Infantry (c1030). DS1 materials #00# shortsword and #01# battle axe (both on R_Weapon), #02# cracked round shield.
    /// 254000 (masks 0+2: sword + shield) → 103020 Sword & Shield (sword on its WP1_c1030, shield on "SD cull24");
    /// 254001 (mask 1: axe) → 103030 Axe (WP2_c1030); 254002 (mask 0: sword; DS1's firebomb thrower) → 103020 as well
    /// (the template variant also shows its shield; firebomb throwing not ported yet). Logic as the Asylum Hollow.
    /// </summary>
    public static Options HollowWarrior => new("c2540", "c2540", "c1030", 2540,
        BodyMaterial: "BD_LG", EdgeMaterial: "BD_LG_1bit_2side",
        WeaponMaterials: new() { ["#00#"] = "WP1_c1030", ["#01#"] = "WP2_c1030", ["#02#"] = "SD cull24" }, KeepTemplateMeshes: [],
        Variants:
        [
            new(254000, 103020, 254000, 103010, 103000, "sword & shield"),
            new(254001, 103030, 254001, 103010, 103000, "axe"),
            new(254002, 103020, 254002, 103010, 103000, "sword (firebomb thrower)"),
            // session 11 (burgchr11): the Burg's Hollow Warriors (254013 / 254014 black phantoms)
            .. IfBurg<Variant>(
                new Variant(254010, 103020, 254010, 103010, 103000, "sword & shield (Burg)"), new Variant(254013, 103020, 254013, 103010, 103000, "sword & shield (Burg black phantom)"),
                new Variant(254011, 103030, 254011, 103010, 103000, "axe (Burg)"), new Variant(254014, 103030, 254014, 103010, 103000, "axe (Burg black phantom)"),
                new Variant(254012, 103020, 254012, 103010, 103000, "sword (Burg firebomb thrower)")),
        ]) { Rig = HollowWarriorSword, MoreRigs = [HollowWarriorFirebomb, HollowWarriorAxe] };

    /// <summary>DS1 c2540 attacks of the a00 move set (animIdOffset 0, behaviorVariationId 25400; BehaviorParam 2254000JJ
    /// names), shared by the sword and firebomb sets; ranges from 254000_battle (sword) / 254002_battle (firebomb).</summary>
    static Ds1Attacks.Attack[] HollowWarriorA00(bool sword) =>
    [
        new(3000, "small slash 1 (小攻撃１)", 1.6f, "254000/254002_battle defAct02 (Att3000 0-1.6); BehaviorParam 225400100"),
        new(3001, "small slash 2", 0, "defAct02 combo stage 2; 225400110"),
        new(3002, "small slash 3", 0, "defAct02 combo stage 3; 225400120"),
        new(3003, "two-handed slash 1 (両手斬り１)", sword ? 2.2f : 1.9f, "act03 (Att3003 0-2.2 sword / 0-1.9 firebomb); 225400130"),
        new(3004, "two-handed slash 2", 0, "act03 combo stage 2; 225400140"),
        new(3005, "running slash (助走斬り)", sword ? 4.2f : 3.8f, "act07 (Att3005 2.9-4.2 / 2.5-3.8); 225400150"),
        new(3006, "leaping slashes, middle (飛び掛かり連続斬り)", sword ? 6.8f : 5.5f, "act07 (Att3006 5.7-6.8 / 3.5-5.5); 225400160"),
        new(3007, "leaping slashes, far", sword ? 10f : 7.5f, "act07 (Att3007 8.4-10 / 5-7.5); 225400170"),
        new(3008, "throw the bottle = firebomb (瓶を投げる)", 5.7f, "act05 (Att3008 2-5.7); TAE Bullet event 1.467 s, dummy 1, judge 180 → BehaviorParam 225400180 → Bullet 25400 → explosion Bullet 25402 (AtkParam_Npc 25400, fire 140)"),
    ];

    /// <summary>
    /// Track enemies (session 8): Firelink's sword & shield Hollow Warrior 1:1 (DS1 NpcParam 254000 → EnemyParam 254000) as model
    /// c2541 (DS1 body, shortsword #00# on R_Weapon via c1030's WP1 material = culling 20, battle axe #01# via WP2 = 21, cracked
    /// round shield #02# on L_shield via "SD cull24" = 24; the variant shows 20 + 24, DS1's modelDispMask0/2) / animId 254100
    /// (c1030's network resized to DS1 c2540's 52-bone rig). Battle goal 254000_battle.lua (m10_02): act1 defAct01 3010
    /// (1.1-2 m), act2 defAct02 3000 | 3000→3001 | 3000→3001→3002 (5/15/80 %), act3 3003 | 3003→3004 | 3003→3004→3009, act5
    /// 3008 (weight 0), act7 3005 / 3006 / 3007 by distance. Interim aliases: 11 → 3005, 12 → 3003 (10 is DS1's 3010).
    /// </summary>
    public static OneToOne HollowWarriorSword => new(2541, [254000, .. IfBurg(254010, 254013)], Ds1RigAnims.HollowUnarmed, 103000)
    {
        WeaponMeshes = new() { ["#00#"] = "WP1_c1030", ["#01#"] = "WP2_c1030", ["#02#"] = "SD cull24" },
        Attacks = new Ds1Attacks.Plan(0, 25400, 0,
        [
            .. HollowWarriorA00(true),
            new(3009, "two-handed slash 3 (両手斬り３)", 0, "act03 ComboFinal; 225400290"),
            new(3010, "approaching slashes (連続斬り接近)", 2f, "act1 defAct01 (Att3010 1.1-2); 225400300"),
            .. Steps(701, 702, 703),
        ],
        [
            new([3000, 3001, 3002], "defAct02 80 % (5 % single, 15 % two: stop odds)"),
            new([3003, 3004, 3009], "act03 (Attack 3003 + ComboFinal 3004 = its first two stages)"),
        ],
        [
            new(11, 3005, "ai103000 action 11 (approach 2.3-3.4 m)"),
            new(12, 3003, "ai103000 action 12 (approach 2.2 m)"),
        ]),
        Armed = [new(254000, 254000, [20, 24], 103020, "sword & shield"), .. IfBurg<Ds1AttackParams.ArmedVariant>(new Ds1AttackParams.ArmedVariant(254010, 254010, [20, 24], 103020, "sword & shield (Burg)"), new Ds1AttackParams.ArmedVariant(254013, 254013, [20, 24], 103020, "sword & shield (Burg black phantom)"))],
    };

    /// <summary>
    /// Track enemies (session 8): the firebomb Hollow Warrior on Firelink's aqueduct stairs (user report: "this guy should be
    /// throwing firebombs towards the player"; DS1 NpcParam 254002 → EnemyParam 254002, DS1 c2540_0001 at (-21.10, -39.28,
    /// 13.23)) on model c2541 (sword #00# only: culling 20) with an anim set of its own, animId 254200, because the interim
    /// aliases differ: battle goal 254002_battle.lua uses 3000-3008 (no 3009/3010): act2 defAct02 3000 chain, act3 3003 |
    /// 3003→3004, act5/act6 throw 3008 (2-5.7 m; 91 % beyond 8 m after approaching to 5.7 m), act7 lunges 3005-3007. Interim
    /// aliases: 10 → 3003, 11 → 3008 (the DS2 AI requests 11 from 2.3-3.4 m, inside DS1's throw range), 12 → 3005.
    /// </summary>
    public static OneToOne HollowWarriorFirebomb => new(2541, [254002, .. IfBurg(254012)], Ds1RigAnims.HollowUnarmed, 103000)
    {
        AnimId = 254200,
        OwnModel = false,
        WeaponMeshes = new() { ["#00#"] = "WP1_c1030", ["#01#"] = "WP2_c1030", ["#02#"] = "SD cull24" },
        Attacks = new Ds1Attacks.Plan(0, 25400, 0,
        [.. HollowWarriorA00(false), .. Steps(701, 702, 703)],
        [
            new([3000, 3001, 3002], "defAct02 {1.6, 0, 5, 15}: 80 % (5 % single, 15 % two)"),
            new([3003, 3004], "act03: 40 % (60 % single 3003)"),
        ],
        [
            new(10, 3003, "ai103000 action 10 (approach 2.2 m)"),
            new(11, 3008, "ai103000 action 11 (approach 2.3-3.4 m): the firebomb throw"),
            new(12, 3005, "ai103000 action 12"),
        ]),
        Armed = [new(254002, 254002, [20], 103020, "sword (firebomb thrower)"), .. IfBurg<Ds1AttackParams.ArmedVariant>(new Ds1AttackParams.ArmedVariant(254012, 254012, [20], 103020, "sword (Burg firebomb thrower)"))],
    };

    /// <summary>
    /// Track enemies (session 8): Firelink's axe Hollow Warrior 1:1 (DS1 NpcParam 254001 → EnemyParam 254001: animIdOffset 10000 =
    /// the a01_30xx clips, battle axe #01# only = culling 21) on model c2541, animId 254300. Battle goal 254001_battle.lua:
    /// act2 defAct02 3000→3001→3002 (5/15/80 %), act3 3003 | 3003→3004 | 3003→3004→3009, act5 3008 (weight 0; DS1 has no
    /// a01_3008, so it is left out), act7 3005 (2.5-3.5 m) / 3006 (3.5-5.2) / 3007 (5-7), each 50 % with the follow-up 3010.
    /// Interim aliases: 11 → 3005, 12 → 3003 (10 is DS1's 3010).
    /// </summary>
    public static OneToOne HollowWarriorAxe => new(2541, [254001, .. IfBurg(254011, 254014)], Ds1RigAnims.HollowUnarmed, 103000)
    {
        AnimId = 254300,
        OwnModel = false,
        WeaponMeshes = new() { ["#00#"] = "WP1_c1030", ["#01#"] = "WP2_c1030", ["#02#"] = "SD cull24" },
        Attacks = new Ds1Attacks.Plan(10000, 25400, 0,
        [
            new(3000, "small slash 1, axe (小攻撃１（アクス）)", 1.6f, "254001_battle defAct02 (Att3000 0-1.6); a01_3000; BehaviorParam 225400200"),
            new(3001, "small slash 2, axe", 0, "defAct02 stage 2; 225400210"),
            new(3002, "small slash 3, axe", 0, "defAct02 stage 3; 225400220"),
            new(3003, "two-handed slash 1, axe", 1.8f, "act03 (Att3003 0-1.8); 225400230"),
            new(3004, "two-handed slash 2, axe", 0, "act03 stage 2; 225400240"),
            new(3005, "running slash", 3.5f, "act07 (Att3005 2.5-3.5); 225400250"),
            new(3006, "leap, middle (飛び掛かり（中距離）)", 5.2f, "act07 (Att3006 3.5-5.2); 225400260"),
            new(3007, "leap, far", 7f, "act07 (Att3007 5-7); 225400270"),
            new(3009, "two-handed slash 3, axe", 0, "act03 ComboFinal; 225400310"),
            new(3010, "leap follow-up, axe (飛び掛り追い討ち（アクス）)", 0, "act07 ComboFinal after 3005/3006/3007; 225400320"),
            .. Steps(701, 702, 703),
        ],
        [
            new([3000, 3001, 3002], "defAct02 80 % (5 % single, 15 % two)"),
            new([3003, 3004, 3009], "act03"),
            new([3005, 3010], "act07 3005 + ComboFinal 3010 (50 %)"),
            new([3006, 3010], "act07 3006 + 3010 (50 %)"),
            new([3007, 3010], "act07 3007 + 3010 (50 %)"),
        ],
        [
            new(11, 3005, "ai103000 action 11 (approach 2.3-3.4 m)"),
            new(12, 3003, "ai103000 action 12"),
        ]),
        Armed = [new(254001, 254001, [21], 103030, "axe"), .. IfBurg<Ds1AttackParams.ArmedVariant>(new Ds1AttackParams.ArmedVariant(254011, 254011, [21], 103030, "axe (Burg)"), new Ds1AttackParams.ArmedVariant(254014, 254014, [21], 103030, "axe (Burg black phantom)"))],
    };

    /// <summary>
    /// DS1's Skeleton (c2900, textures c2909) on DS2's Skeleton (c1320), which is DS1's skeleton rig (same bones, same
    /// bind pose; DS2 even kept the material name c2900_body). DS1 290000 (masks 0+1: sword WP_A_0400 + shield) →
    /// 132025 Estoc & Shield (sword on the Estoc's WP_1400_M, shield on SD1_c1030); 290002 (mask 3: greatsword
    /// WP_A_0401) → 132005 Falchions (greatsword on the right falchion's WP_A_0401@R_Weapon). Logic 132010/132000
    /// (Majula basement skeletons).
    /// </summary>
    public static Options Skeleton => new("c2900", "c2909", "c1320", 2900,
        BodyMaterial: "c2900_body", EdgeMaterial: "c2900_body_Towside",
        WeaponMaterials: new() { ["#00#　剣"] = "WP_1400_M", ["#01#　盾"] = "SD1_c1030", ["#03#　大剣"] = "WP_A_0401@R_Weapon" }, KeepTemplateMeshes: [],
        Variants:
        [
            new(290000, 132025, 290000, 132010, 132000, "sword & shield"),
            new(290002, 132005, 290002, 132010, 132000, "greatsword"),
        ]) { Rig = SkeletonScimitar, MoreRigs = [SkeletonFalchion] };

    /// <summary>Model c2901's DS1 weapons (template c1320's materials): scimitar "#00#　剣" → WP_1400_M (culling 21), shield
    /// "#01#　盾" → SD1_c1030 (23), large falchion "#03#　大剣" → WP_A_0401 on R_Weapon (24).</summary>
    static Dictionary<string, string> SkeletonWeapons => new() { ["#00#　剣"] = "WP_1400_M", ["#01#　盾"] = "SD1_c1030", ["#03#　大剣"] = "WP_A_0401@R_Weapon" };

    /// <summary>
    /// Track enemies (session 8): Firelink's graveyard Skeletons with scimitar and shield 1:1 (DS1 NpcParam 290000 → EnemyParam
    /// 290000: culling 21 + 23) as model c2901 (DS1 c2900 body on its 62-bone rig; template model c1320, DS2's own copy of this
    /// skeleton) / animId 290100: c1030's network resized 55 → 62 bones (c2900 has no toe nubs: the lock-foot chains' toe joint
    /// becomes the ball joint L_Toe0, NmbNetResize). DS1 animIdOffset 0, behaviorVariationId 29000, NpcThinkParam 290000 →
    /// 290000_battle.lua (m10_02): act1 3004 (0-3.1 m), act2 3000 | 3000→3001 | 3000→3001→3002, act3 3003, act4 3005, act6 3006
    /// (3-6 m) / 3007 (4-9 m), act7 defAct 3008 (3.5-6.5 m; two judges 180 / 181 = slots 0 / 1). Interim aliases for the DS2
    /// skeleton AI (ai132000 requests 10, 12, 13, 50, 51): 10 → 3000, 12 → 3003, 13 → 3006.
    /// </summary>
    public static OneToOne SkeletonScimitar => new(2901, [290000], Ds1RigAnims.HollowUnarmed, 103000)
    {
        TemplateNetwork = "c1030",
        WeaponMeshes = SkeletonWeapons,
        Attacks = new Ds1Attacks.Plan(0, 29000, 0,
        [
            new(3000, "slash 1 (曲剣_斬撃1)", 2.2f, "290000_battle act2 (Att3000 0-2.2); BehaviorParam 229000100"),
            new(3001, "slash 2", 0, "act2 stage 2; 229000110"),
            new(3002, "slash 3", 0, "act2 stage 3; 229000120"),
            new(3003, "strong slash (強斬り)", 2.2f, "act3 (Att3003 0-2.2); 229000130"),
            new(3004, "spinning slash (回転斬り)", 3.1f, "act1 (Att3004 0-3.1); 229000140"),
            new(3005, "backstep slash (バックステップ斬り)", 2.5f, "act4 (Att3005 0-2.5); 229000150"),
            new(3006, "leap (飛び込み攻撃)", 6f, "act6 (Att3006 3-6); 229000160"),
            new(3007, "far leap", 9f, "act6 (Att3007 4-9); 229000170"),
            new(3008, "running slash, 2 hits (助走斬り)", 6.5f, "act7 defAct (Att3008 3.5-6.5); 229000180 / 229000181"),
            .. Steps(700, 701, 702, 703),
        ],
        [
            new([3000, 3001, 3002], "act2"),
        ],
        [
            new(10, 3000, "ai132000 action 10 (approach 1.8 m)"),
            new(12, 3003, "ai132000 action 12"),
            new(13, 3006, "ai132000 action 13"),
        ]),
        Armed = [new(290000, 290000, [21, 23], 132025, "scimitar & shield")],
    };

    /// <summary>
    /// Track enemies (session 8): the graveyard's large-falchion Skeletons (group leaders) 1:1 (DS1 NpcParam 290002 → EnemyParam
    /// 290002: culling 24) on model c2901 with anim set c2902 / animId 290200: DS1 animIdOffset 20000 (a02_3000-3008),
    /// NpcThinkParam 290002 → 290002_battle.lua: act1 3004 | 3004→3005, act2 3000 | 3000→3001 | 3000→3001→3002, act3 3003, act6
    /// 3006 (3-6 m) / 3007 (5-10 m), act7 3008 (3.9-6.9 m). Interim aliases: 10 → 3000, 12 → 3003, 13 → 3006.
    /// </summary>
    public static OneToOne SkeletonFalchion => new(2901, [290002], Ds1RigAnims.HollowUnarmed, 103000)
    {
        AnimId = 290200,
        OwnModel = false,
        TemplateNetwork = "c1030",
        WeaponMeshes = SkeletonWeapons,
        Attacks = new Ds1Attacks.Plan(20000, 29000, 0,
        [
            new(3000, "slash 1, large falchion (大曲剣_斬撃1)", 2.7f, "290002_battle act2 (Att3000 0-2.7); a02_3000; BehaviorParam 229000300"),
            new(3001, "slash 2, large falchion", 0, "act2 stage 2; 229000310"),
            new(3002, "slash 3, large falchion", 0, "act2 stage 3; 229000320"),
            new(3003, "strong slash", 3f, "act3 (Att3003 0-3); 229000330"),
            new(3004, "sweep 1 (なぎ払い①)", 2.5f, "act1 (Att3004 0-2.5); 229000340"),
            new(3005, "sweep 2", 0, "act1 ComboFinal; 229000350"),
            new(3006, "leap", 6f, "act6 (Att3006 3-6); 229000360"),
            new(3007, "far leap", 10f, "act6 (Att3007 5-10); 229000370"),
            new(3008, "running slash, 2 hits", 6.9f, "act7 defAct (Att3008 3.9-6.9); 229000380 / 229000381"),
            .. Steps(700, 701, 702, 703),
        ],
        [
            new([3000, 3001, 3002], "act2"),
            new([3004, 3005], "act1"),
        ],
        [
            new(10, 3000, "ai132000 action 10"),
            new(12, 3003, "ai132000 action 12"),
            new(13, 3006, "ai132000 action 13"),
        ]),
        Armed = [new(290002, 290002, [24], 132005, "large falchion")],
    };

    /// <summary>DS1 bone → the DS1 bone its direction is measured to (joint → that joint), for the re-binding rotation.</summary>
    static readonly (string Bone, string Child)[] Directions =
    [
        ("Spine", "Spine1"), ("Spine1", "Neck"), ("Neck", "Head"),
        ("L Clavicle", "L UpperArm"), ("L UpperArm", "L Forearm"), ("L Forearm", "L Hand"), ("L ForeTwist", "L Hand"), ("L Hand", "L Finger1"),
        ("L Finger0", "L Finger01"), ("L Finger1", "L Finger11"), ("L Finger2", "L Finger21"),
        ("R Clavicle", "R UpperArm"), ("R UpperArm", "R Forearm"), ("R Forearm", "R Hand"), ("R ForeTwist", "R Hand"), ("R Hand", "R Finger1"),
        ("R Finger0", "R Finger01"), ("R Finger1", "R Finger11"), ("R Finger2", "R Finger21"),
        ("L Thigh", "L Calf"), ("L Calf", "L Foot"), ("LCalfTwist", "L Foot"), ("L Foot", "L Toe0"),
        ("R Thigh", "R Calf"), ("R Calf", "R Foot"), ("RCalfTwist", "R Foot"), ("R Foot", "R Toe0"),
    ];

    /// <summary>
    /// Weapon/shield bones: their meshes move rigidly with the bone (p' = p · W1⁻¹ · W2), then are turned to lie like
    /// the template's own weapon on that bone (AlignLike: DS1 binds swords pointing forward, DS2 pointing down).
    /// </summary>
    static readonly HashSet<string> RigidBones = new(StringComparer.OrdinalIgnoreCase) { "sword", "seeld", "r_weapon", "r_weapon2", "r_weapon3", "l_weapon", "l_shield" }; // ChrCompare.Norm names

    /// <summary>DS1 bones whose DS2 counterpart has another name.</summary>
    static readonly Dictionary<string, string> Renames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LCalfTwist"] = "L_Calf", ["RCalfTwist"] = "R_Calf", ["sword"] = "R_Weapon", ["seeld"] = "L_shield",
        ["R_weapon2"] = "R_Weapon", ["R_weapon3"] = "R_Weapon",
    };

    /// <summary>
    /// DS1 → DS2 enemy calibration (provisional, session 5; like the weapons' per-class ratios, but from one pair): the
    /// first enemies of each game, DS1's Asylum Hollow (HP 44, 20 souls) and DS2's Things Betwixt Undead Traveler (HP
    /// 160, 30 souls). DS2's HP are ~3-5× DS1's because DS2 weapons hit harder; souls follow DS2's level costs.
    /// </summary>
    public const double HpScale = Ds1Calibration.HpScale, SoulScale = Ds1Calibration.SoulScale;   // session 11 (burgchr11): the one calibration, Ds1Calibration

    public static void Build(Pack pack, string dsrRoot, Options o, Ds1Source? ds1 = null)
    {
        string data = pack.Ds2Data, t = o.TemplateChr, n = $"c{o.NewModel:D4}";
        string tId = t[1..], nId = n[1..];
        if (tId.Length != nId.Length) throw new InvalidOperationException("model ids must have the same digit count");

        // ---- DS1 sources
        var ds1Bnd = BND3.Read(Path.Combine(dsrRoot, "chr", o.Ds1Chr + ".chrbnd.dcx"));
        var src = FLVER2.Read(ds1Bnd.Files.First(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
        var ds1Tex = LoadDs1Textures(dsrRoot, o);

        // ---- template model archive
        var bnd = BND4.Read(Path.Combine(data, "model", "chr", t + ".bnd"));
        var flvFile = bnd.Files.First(f => f.Name.EndsWith($"{t}.flv", StringComparison.OrdinalIgnoreCase));
        var dst = Convert(src, FLVER2.Read(flvFile.Bytes), o, t, n, out string report, out var texUse, out var keepTex);
        var missing = texUse.Values.Where(x => !ds1Tex.ContainsKey(x)).Distinct().ToList();
        if (missing.Count > 0) throw new InvalidDataException($"{o.Ds1Chr}: DS1 textures not found: {string.Join(", ", missing)} (have {ds1Tex.Count})");
        // Every texture gets a name of its own (cNNNN_ prefix unless it already holds the model id). DS2 caches loaded
        // textures by name: template names (BD_LG_c1030_*, WP_1400_M_*, …) are also used by vanilla characters and the
        // player's weapon parts, and two of our characters used WP_1400_M_* with different pixels, so whichever owner
        // unloaded first released the shared texture under the others. Session 6: that was the bonfire-menu crash (DS2's
        // fatal "Invalid Unref()" on the render thread after Travel between the Asylum and Firelink).
        foreach (var m in dst.Materials)
            foreach (var x in m.Textures.Where(x => !string.IsNullOrEmpty(x.Path)))
                x.Path = UniqueTexPath(x.Path, n);
        byte[] flvBytes = dst.Write();
        // The template's cloth simulation (c1020_Cloth.hkx/.clm, ACB asset model_hkt/…_Cloth.hkt) is bound to cloth
        // meshes our model doesn't have: drop it (run s5-30: with it, the Undead Soldier rendered black and DS2 crashed).
        int cloth = bnd.Files.RemoveAll(f => Path.GetFileName(f.Name).Contains("_Cloth", StringComparison.OrdinalIgnoreCase));
        foreach (var f in bnd.Files)
        {
            string name = Path.GetFileName(f.Name);
            if (name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase)) f.Bytes = flvBytes; // main and _s (low) model
            else if (name.EndsWith(".acb", StringComparison.OrdinalIgnoreCase)) f.Bytes = RewriteAcb(f.Bytes, t, n, cloth > 0);
            else f.Bytes = ReplaceAscii(f.Bytes, t, n);
            f.Name = f.Name.Replace(t, n);
        }
        if (cloth > 0) report += $"; dropped the template's cloth ({cloth} files)";
        pack.AddFile("gamedata", $"model/chr/{n}.bnd", bnd.Write());

        // ---- textures: the template's names (the ACB lists them) with DS1 pixels where our materials use them
        pack.AddFile("gamedata", $"model/chr/{n}.texbnd", WriteTexbnd(data, t, n, o, texUse, keepTex, ds1Tex));

        // Animations (morpheme4 anibnd, c0001 extanibnd), TimeAct and the sound bank are loaded by the EnemyParam's
        // animId (run s5-26: model c2500 + animId 103010 opened c1030's), so the template's are used as they are.

        // ---- EnemyParam: each template variant with our model, DS1's HP and souls (calibrated, see HpScale)
        var enemy = pack.Param("EnemyParam");
        var npc = ds1?.Params.Params["NpcParam"];
        foreach (var v in o.Variants)
        {
            var row = pack.AddRow("EnemyParam", enemy[v.TemplateEnemyParam], v.EnemyParamId, $"[Lordran] {o.Ds1Chr} ({v.Note}) on {t}");
            row["modelId"].Value = o.NewModel;
            // session 11 (track enemy11, G7 rule R1 / P-12): DS1 revives its regulars at every rest without limit; DS2's EnemyParam
            // respawnNum caps the revives (template c1030: 12) and 0 = no limit (decompile, P-12 skeptic V0; vanilla's summons /
            // Stone Soldiers use 0)
            if (row["respawnNum"] is { } rn) rn.Value = System.Convert.ChangeType(0, rn.Value.GetType());
            if (npc?[v.Ds1Npc] is { } d1)
            {
                // session 11 (track enemy11, G12 C-AR / C-T1, CP-F5 / CP-F6): DS1's HP includes the resident area SpEffects
                // (NpcParam.spEffectID0-7 maxHpRate, e.g. 7006 x1.6 on the Firelink skeletons); DS2's per-target damage multipliers
                // damageAdjustRate* (template 0.8 / 1 / 1.495) are DS2 tuning, not DS1's: 1 (the attack side carries DS1's own rates,
                // Ds1AttackParams.ResidentRate)
                // session 11 (burgchr11): HP (C-AR resident maxHpRate, NG+ growth kept), damageAdjustRate* 1, defences, resists and stamina
                // from DS1 through the one calibration (Ds1Calibration rules C-HP / C-DEF / C-ST)
                string cal = Ds1Calibration.Apply(row, ds1!, d1);
                int hp = System.Convert.ToInt32(row["hitPoint"].Value), souls = Ds1Calibration.Souls(d1);   // bal11 rule BAL2
                row["dropSoulValue"].Value = System.Convert.ChangeType(souls, row["dropSoulValue"].Value.GetType());
                pack.Note($"EnemyParam {v.EnemyParamId}: HP {hp} (DS1 {d1["hp"].Value}), souls {souls} (DS1 {d1["getSoul"].Value}); {cal}");
                // drops: DS1's item lots (through the item map) + DS2's Lifegem chance, in a lot of its own
                var notes = new List<string>();
                var drops = Ds1Drops.Resolve(ds1!, d1, pack, notes);
                var lots = pack.Param("ItemLotParam2_Chr");
                int tLot = System.Convert.ToInt32(row["itemLotId"].Value);
                var tLotRow = lots[tLot] ?? lots[1105000];
                int lotId = v.EnemyParamId * 100;
                int count = Ds1Drops.Write(pack, tLotRow, lotId, $"[Lordran] {o.Ds1Chr} drops (DS1 {v.Ds1Npc})", drops);
                row["itemLotId"].Value = System.Convert.ChangeType(lotId, row["itemLotId"].Value.GetType());
                pack.Note($"EnemyParam {v.EnemyParamId}: drops lot {lotId} ({count} items): " + string.Join("; ", drops.Select(x => $"{x.ItemId} {x.Percent:0.#}% {x.Note}")) + (notes.Count > 0 ? " | " + string.Join("; ", notes) : ""));
            }
        }

        pack.Note($"chr {o.Ds1Chr} → {n} on {t}: {report}; EnemyParam " + string.Join(", ", o.Variants.Select(v => $"{v.EnemyParamId} ← DS1 {v.Ds1Npc} via {v.TemplateEnemyParam}")));
        Console.WriteLine($"  chr {o.Ds1Chr} → {n} on {t}: {report}");

        // ---- 1:1 variants (session 8): DS1 body + skeleton + clips + ragdoll; the rows above keep DS1's HP/souls/drops
        foreach (var rig in o.Rigs.Where(_ => UseDs1Rig))
        {
            var r = Ds1RigChr.Build(dsrRoot, data, o, rig);
            AddDs1RigFiles(pack, o, rig, r);
            foreach (int id in rig.EnemyParams)
            {
                var row = enemy[id] ?? throw new InvalidDataException($"EnemyParam {id} (1:1 variant) was not built");
                row["modelId"].Value = System.Convert.ChangeType(rig.Model, row["modelId"].Value.GetType());
                row["animId"].Value = System.Convert.ChangeType(rig.AnimId, row["animId"].Value.GetType());
                // session 11 (track enemy11, G11 rule TURN-RATE): DS1 turns at NpcParam.turnVellocity (deg/s) in every movement
                // state (its locomotion and turn clips carry no TAE 224 / JT 7); DS2 reads the EnemyMoveParam the EnemyParam's moveId
                // names. The template row (shared with vanilla enemies) is cloned to this EnemyParam's id - never edited in place -
                // and every non-zero rotation speed (walk / dash / lock-on min-max, quick turn, step, rolling) becomes DS1's rate
                var v1 = o.Variants.FirstOrDefault(v => v.EnemyParamId == id);
                if (v1 != null && npc?[v1.Ds1Npc] is { } dn && pack.HasParam("EnemyMoveParam"))
                {
                    var mp = pack.Param("EnemyMoveParam");
                    int tMove = System.Convert.ToInt32(row["moveId"].Value);
                    if (mp[tMove] is { } tm)
                    {
                        var mr = mp[id] ?? pack.AddRow("EnemyMoveParam", tm, id, $"[Lordran] {o.Ds1Chr} 1:1 {id}: EnemyMoveParam {tMove} with DS1's turn rate");
                        float rate = (float)(System.Convert.ToDouble(dn["turnVellocity"].Value) * Math.PI / 180);
                        int set = 0;
                        foreach (var cell in mr.Cells.Where(c => c.Value is float f0 && f0 != 0 && c.Def.InternalName.Contains("Rot", StringComparison.Ordinal) && c.Def.InternalName.Contains("Speed", StringComparison.Ordinal)
                                     && !c.Def.InternalName.Contains("Border") && !c.Def.InternalName.Contains("Accel") && !c.Def.InternalName.Contains("landing")))
                        { cell.Value = rate; set++; }
                        row["moveId"].Value = System.Convert.ChangeType(id, row["moveId"].Value.GetType());
                        pack.Note($"EnemyParam {id}: moveId {tMove} → {id} (clone), {set} rotation speeds = DS1 turnVellocity {dn["turnVellocity"].Value}°/s ({rate:0.####} rad/s)");
                    }
                    // G11 rule M9 (as Ds1Boss): the character capsule is DS1's NpcParam hitHeight / hitRadius, on an
                    // EnemyCommonParam clone keyed by this EnemyParam id
                    int tCommon = System.Convert.ToInt32(row["commonId"].Value);
                    if (pack.HasParam("EnemyCommonParam") && pack.Param("EnemyCommonParam")[tCommon] is { } tc)
                    {
                        var cr = pack.Param("EnemyCommonParam")[id] ?? pack.AddRow("EnemyCommonParam", tc, id, $"[Lordran] {o.Ds1Chr} 1:1 {id}: EnemyCommonParam {tCommon} with DS1's capsule");
                        double hh = System.Convert.ToDouble(dn["hitHeight"].Value), hr = System.Convert.ToDouble(dn["hitRadius"].Value);
                        if (hh > 0) { cr["fHitHeight"]!.Value = (float)hh; cr["fChrHeight"]!.Value = (float)hh; }
                        if (hr > 0) cr["fHitRadius"]!.Value = (float)hr;
                        row["commonId"].Value = System.Convert.ChangeType(id, row["commonId"].Value.GetType());
                        pack.Note($"EnemyParam {id}: commonId {tCommon} → {id} (clone), capsule {hh} m / r {hr} m (DS1 NpcParam)");
                    }
                }
            }
            // Params keyed by the template's ids (find-row ds2 1030 / 103000, session 8): the DS2 engine's per-character-type
            // setup. The skeleton c1320 has almost none of them, so they are optional, but the 1:1 port changes BOTH the model
            // id and the animId (the interim port kept the template's animId), so the template's rows are cloned to the new
            // ids rather than leaving the engine on defaults (DS2 rules). EnemyCommonParam/EnemyMoveParam are referenced by
            // EnemyParam fields (commonId/moveId, kept), coincidental ids (PlayerLight, map params) are left out.
            int tModel = int.Parse(t[1..]);
            int clones = 0;
            foreach (var (param, from, to) in ConfigByModel.Select(p => (p, tModel, rig.Model)).Concat(ConfigByAnim.Select(p => (p, rig.TemplateAnimId, rig.AnimId))))
            {
                if (!pack.HasParam(param)) continue;
                var cfg = pack.Param(param)[from];
                if (cfg == null || pack.Param(param)[to] != null) continue;
                pack.AddRow(param, cfg, to, $"[Lordran] {o.Ds1Chr} 1:1: {param} of {t} / {rig.TemplateAnimId}");
                clones++;
            }
            pack.Note($"chr {o.Ds1Chr} 1:1 → c{rig.Model:D4}: {clones} template config rows cloned ({tModel} → {rig.Model}, {rig.TemplateAnimId} → {rig.AnimId})");
            foreach (var l in r.Report) pack.Note($"chr {o.Ds1Chr} 1:1 → c{rig.Model:D4}: {l}");
            pack.Note($"chr {o.Ds1Chr} 1:1 → c{rig.Model:D4} (anim c{rig.AnimId:D6}): EnemyParam {string.Join(", ", rig.EnemyParams)}; {r.Files.Count} files");
            // track enemies: the armed variants' weapon / behaviour / combo / damage / bullet params
            if (rig.Attacks != null && r.Anims.Attacks != null && ds1 != null)
                foreach (var v in rig.Armed)
                {
                    var wr = Ds1AttackParams.Write(pack, ds1, v, rig.Attacks, r.Anims.Attacks, r.Ds1Flver, $"{o.Ds1Chr} 1:1 c{rig.AnimId / 100:D4}");
                    foreach (var l in wr.Notes) pack.Note($"chr {o.Ds1Chr} 1:1 → c{rig.AnimId / 100:D4} attacks: {l}");
                }
            Console.WriteLine($"  chr {o.Ds1Chr} 1:1 → c{rig.Model:D4} / animId {rig.AnimId}: {r.Files.Count} files, {r.Anims.Clips.Count} clip slots{(r.Anims.Attacks != null ? $", {r.Anims.Attacks.Clips.Count} DS1 attack clips in {r.Anims.Attacks.Categories.Count} categories" : "")}, EnemyParam {string.Join(", ", rig.EnemyParams)}");
        }
    }

    /// <summary>
    /// A 1:1 build's files into the pack (track chrsound fix pass, rule S10): gamedata files, the loose wave banks (device
    /// "title", rule S2) and the sound log. DS2 picks a character's sound bank by animId/100 rounded down to tens, and a bank
    /// holds one FMOD project, so a bank file (and a loose wave bank) that another 1:1 build already added must be byte-identical:
    /// the variants of one DS1 character in one decade (the sword / bow / unarmed Hollows 250100/250200/250300, the Hollow
    /// Warriors 254100/254200/254300, ...) build the same bank (it depends only on the DS1 character and the decade:
    /// <see cref="Ds1ChrSound.Bank"/>) and share it; a different bank means two DS1 characters (two projects) in one decade,
    /// which DS2 cannot load from one bank, so the build stops. Use this for every 1:1 build (one per <c>Rig</c>).
    /// </summary>
    public static void AddDs1RigFiles(Pack pack, Options o, OneToOne rig, Ds1RigChr.Result r)
    {
        foreach (var (path, bytes) in r.Files)
        {
            bool shared = path.StartsWith("sound/frpg2_c", StringComparison.OrdinalIgnoreCase);
            if (shared && pack.TryGetFile("gamedata", path) is { } had)
            {
                if (!had.AsSpan().SequenceEqual(bytes))
                    throw new InvalidDataException($"{o.Ds1Chr} 1:1 c{rig.AnimId / 100:D4}: sound bank {path} is already used by a different DS1 character in the same animId decade (one FMOD project per bank, rule S10); give one an animId in another decade or merge their DS1 projects into one (Fev1Project.AddEvents)");
                pack.Note($"chr {o.Ds1Chr} 1:1 → c{rig.AnimId / 100:D4}: sound bank {path} shared with the other builds of {o.Ds1Chr} in this decade (identical, rule S10)");
                continue;
            }
            pack.AddFile("gamedata", path, bytes);
        }
        // DS1's wave banks loose in the game folder (rule S2: DS1's FMOD project asks for them by name); the same DSR file
        // whichever 1:1 character adds it (frpg_main.fsb is listed by every project that copied DS1 common sounds)
        foreach (var (path, bytes) in r.TitleFiles)
        {
            if (pack.TryGetFile("title", path) is { } had)
            {
                if (!had.AsSpan().SequenceEqual(bytes)) throw new InvalidDataException($"{o.Ds1Chr} 1:1: loose wave bank title/{path} differs from the one already in the pack");
                continue;
            }
            pack.AddFile("title", path, bytes);
        }
        foreach (var l in r.SoundLog) pack.Note($"chr {o.Ds1Chr} 1:1 → c{rig.AnimId / 100:D4} sound: {l}");
    }

    /// <summary>
    /// merge9 stage 6: <see cref="AddDs1RigFiles"/>'s rule S10 for a 1:1 build outside the rig loop (the boss, Ds1Boss): the
    /// gamedata files, where a sound bank (sound/frpg2_cBBBB.bnd) another build already added must be byte-identical (the
    /// same DS1 character in the same decade) and a different one stops the build (two DS1 projects in one decade, which DS2
    /// cannot load from one bank); the loose wave banks (device "title", rule S2) likewise. Works in either build order:
    /// AddDs1RigFiles checks against a bank added here.
    /// </summary>
    public static void AddDs1SoundShared(Pack pack, string label, IReadOnlyDictionary<string, byte[]> files, IReadOnlyDictionary<string, byte[]> titleFiles)
    {
        foreach (var (path, bytes) in files)
        {
            bool shared = path.StartsWith("sound/frpg2_c", StringComparison.OrdinalIgnoreCase);
            if (shared && pack.TryGetFile("gamedata", path) is { } had)
            {
                if (!had.AsSpan().SequenceEqual(bytes))
                    throw new InvalidDataException($"{label}: sound bank {path} is already used by a different DS1 character in the same animId decade (one FMOD project per bank, rule S10); give one an animId in another decade or merge their DS1 projects into one (Fev1Project.AddEvents)");
                pack.Note($"{label}: sound bank {path} shared with another build of the same DS1 character in this decade (identical, rule S10)");
                continue;
            }
            pack.AddFile("gamedata", path, bytes);
        }
        foreach (var (path, bytes) in titleFiles)
        {
            if (pack.TryGetFile("title", path) is { } had)
            {
                if (!had.AsSpan().SequenceEqual(bytes)) throw new InvalidDataException($"{label}: loose wave bank title/{path} differs from the one already in the pack");
                pack.Note($"{label}: loose wave bank title/{path} already in the pack (identical DSR file)");
                continue;
            }
            pack.AddFile("title", path, bytes);
        }
    }

    /// <summary>
    /// DS1 meshes into the template FLVER (keeps its header, nodes, dummies, GX lists, buffer layouts): body meshes on
    /// the template's body material, the chosen DS1 weapon meshes on its weapon material, all on the template body
    /// mesh's display node. Each material keeps its template GX list, which decides per variant whether it shows (the
    /// body's: always; a weapon's: only in the variants holding that weapon). <c>KeepTemplateMeshes</c> are carried
    /// over unchanged with their material. <paramref name="texUse"/>: template texture name → DS1 texture name;
    /// <paramref name="keepTex"/>: template textures the kept meshes use (left as they are).
    /// </summary>
    static FLVER2 Convert(FLVER2 src, FLVER2 dst, Options o, string t, string n, out string report, out Dictionary<string, string> texUse, out HashSet<string> keepTex)
    {
        var w1 = ChrCompare.World(src); var w2 = ChrCompare.World(dst);
        var byName2 = dst.Nodes.Select((x, i) => (Name: ChrCompare.Norm(x.Name), i)).GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.First().i);
        var byName1 = src.Nodes.Select((x, i) => (x.Name, i)).GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.First().i);

        // DS1 bone → DS2 bone (by name, else the nearest mapped DS1 ancestor's)
        var map = new int[src.Nodes.Count];
        int Map(int b)
        {
            for (int x = b; x >= 0; x = src.Nodes[x].ParentIndex)
            {
                string name = Renames.TryGetValue(src.Nodes[x].Name, out var r) ? r : src.Nodes[x].Name;
                if (byName2.TryGetValue(ChrCompare.Norm(name), out int m)) return m;
            }
            return byName2[ChrCompare.Norm("Pelvis")];
        }
        for (int b = 0; b < src.Nodes.Count; b++) map[b] = Map(b);

        // re-binding rotation per DS1 bone
        var dirs = Directions.Where(d => byName1.ContainsKey(d.Bone) && byName1.ContainsKey(d.Child)).ToDictionary(d => byName1[d.Bone], d => byName1[d.Child]);
        var rot = new Quaternion?[src.Nodes.Count];
        Quaternion Rot(int b)
        {
            if (rot[b] is { } q) return q;
            Quaternion r;
            if (dirs.TryGetValue(b, out int c))
            {
                var d1 = Vector3.Normalize(w1[c].Translation - w1[b].Translation);
                var d2 = Vector3.Normalize(w2[map[c]].Translation - w2[map[b]].Translation);
                r = Arc(d1, d2);
            }
            else r = src.Nodes[b].ParentIndex >= 0 ? Rot(src.Nodes[b].ParentIndex) : Quaternion.Identity;
            rot[b] = r;
            return r;
        }

        // materials: copies of the template's
        texUse = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tUse = texUse;
        var mats = new List<FLVER2.Material>();
        int AddMaterial(string templateName, FLVER2.Material ds1)
        {
            var tm = dst.Materials[TemplateMaterial(dst, templateName, t)];
            var m = new FLVER2.Material(tm.Name.Replace(t, n), tm.MTD, 0) { GXIndex = tm.GXIndex, Index = tm.Index };
            foreach (var x in tm.Textures)
                m.Textures.Add(new FLVER2.Texture(x.ParamName, x.Path.Replace(t, n), x.TilingScale, x.TilingTypeU, x.TilingTypeV, x.Unk14, x.Unk18, x.Unk1C));
            foreach (var tex in m.Textures)
            {
                string slot = tex.ParamName switch { "g_DiffuseTexture" => "g_Diffuse", "g_SpecularTexture" => "g_Specular", "g_BumpmapTexture" => "g_Bumpmap", _ => "" };
                var s = ds1.Textures.FirstOrDefault(x => x.ParamName == slot && !string.IsNullOrEmpty(x.Path));
                if (s != null) tUse[Path.GetFileNameWithoutExtension(tex.Path)] = Path.GetFileNameWithoutExtension(s.Path);
            }
            mats.Add(m);
            return mats.Count - 1;
        }

        var tBody = dst.Meshes.First(m => dst.Materials[m.MaterialIndex].Name == o.BodyMaterial);
        int normalW = tBody.Vertices.Count > 0 ? tBody.Vertices[0].NormalW : 127;
        int layout = tBody.VertexBuffers[0].LayoutIndex;
        var meshes = new List<FLVER2.Mesh>();
        var matIndex = new Dictionary<(string, int), int>();
        int kept = 0, verts = 0;
        foreach (var sm in src.Meshes)
        {
            var sMat = src.Materials[sm.MaterialIndex];
            bool weapon = sMat.Name.StartsWith('#');
            if (weapon && !o.WeaponMaterials.ContainsKey(sMat.Name)) continue;
            string tMat = weapon ? o.WeaponMaterials[sMat.Name] : sMat.MTD.Contains("_Edge", StringComparison.OrdinalIgnoreCase) || sMat.MTD.Contains("Alp", StringComparison.OrdinalIgnoreCase) ? o.EdgeMaterial : o.BodyMaterial;
            if (!matIndex.TryGetValue((tMat, sm.MaterialIndex), out int mi)) matIndex[(tMat, sm.MaterialIndex)] = mi = AddMaterial(tMat, sMat);

            var palette = new List<int>();
            int Slot(int node) { int i = palette.IndexOf(node); if (i < 0) { palette.Add(node); i = palette.Count - 1; } return i; }
            var newVerts = new List<FLVER.Vertex>();
            foreach (var v in sm.Vertices)
            {
                var infl = new List<(int Bone, float W)>();
                for (int k = 0; k < 4; k++)
                {
                    float w = v.BoneWeights[k];
                    if (w <= 0) continue;
                    infl.Add((sm.BoneIndices.Count > 0 ? sm.BoneIndices[v.BoneIndices[k]] : v.BoneIndices[k], w));
                }
                if (infl.Count == 0) infl.Add((sm.BoneIndices.Count > 0 ? sm.BoneIndices[v.BoneIndices[0]] : sm.NodeIndex, 1f));
                float sum = infl.Sum(x => x.W);
                Vector3 pos = Vector3.Zero, nrm = Vector3.Zero, tan = Vector3.Zero;
                var dst4 = new Dictionary<int, float>();
                foreach (var (b, w0) in infl)
                {
                    float w = w0 / sum;
                    Quaternion r;
                    if (RigidBones.Contains(ChrCompare.Norm(src.Nodes[b].Name)) && Matrix4x4.Invert(w1[b], out var inv1))
                    {
                        var frame = inv1 * w2[map[b]];
                        Matrix4x4.Decompose(frame, out _, out r, out _);
                        pos += w * Vector3.Transform(v.Position, frame);
                    }
                    else
                    {
                        r = Rot(b);
                        pos += w * (w2[map[b]].Translation + Vector3.Transform(v.Position - w1[b].Translation, r));
                    }
                    nrm += w * Vector3.Transform(v.Normal, r);
                    if (v.Tangents.Count > 0) tan += w * Vector3.Transform(new Vector3(v.Tangents[0].X, v.Tangents[0].Y, v.Tangents[0].Z), r);
                    dst4[map[b]] = dst4.GetValueOrDefault(map[b]) + w;
                }
                var nv = new FLVER.Vertex(v) { Position = pos, Normal = Vector3.Normalize(nrm), NormalW = normalW };
                nv.Tangents.Clear();
                var t3 = tan.LengthSquared() > 1e-8f ? Vector3.Normalize(tan) : new Vector3(1, 0, 0);
                nv.Tangents.Add(new Vector4(t3, v.Tangents.Count > 0 ? v.Tangents[0].W : 1));
                while (nv.Colors.Count < 2) nv.Colors.Add(new FLVER.VertexColor(1f, 1f, 1f, 1f));
                var top = dst4.OrderByDescending(x => x.Value).Take(4).ToList();
                float ts = top.Sum(x => x.Value);
                nv.BoneIndices = new FLVER.VertexBoneIndices();
                nv.BoneWeights = new FLVER.VertexBoneWeights();
                for (int k = 0; k < top.Count; k++) { nv.BoneIndices[k] = Slot(top[k].Key); nv.BoneWeights[k] = top[k].Value / ts; }
                newVerts.Add(nv);
            }
            if (weapon && palette.Count == 1)
            {
                // lie like the template's own weapon on the same bone (blade down, edge forward; shield face out)
                int tmi = TemplateMaterial(dst, tMat, t);
                var reference = dst.Meshes.FirstOrDefault(m => m.MaterialIndex == tmi && m.BoneIndices.Count == 1 && m.BoneIndices[0] == palette[0]);
                if (reference != null) AlignLike(newVerts, reference.Vertices, w2[palette[0]].Translation);
            }
            var fs = sm.FaceSets.FirstOrDefault(f => f.Flags == FLVER2.FaceSet.FSFlags.None) ?? sm.FaceSets[0];
            var mesh = new FLVER2.Mesh
            {
                MaterialIndex = mi,
                NodeIndex = tBody.NodeIndex,
                BoneIndices = palette,
                Dynamic = 1,
                UseBoneWeights = tBody.UseBoneWeights,
                VertexBuffers = [new FLVER2.VertexBuffer(layout)],
                FaceSets = [new FLVER2.FaceSet(FLVER2.FaceSet.FSFlags.None, fs.TriangleStrip, fs.CullBackfaces, fs.Unk06, new List<int>(fs.Indices))],
                Vertices = newVerts,
            };
            if (tBody.BoundingBox != null)
                mesh.BoundingBox = new FLVER2.Mesh.BoundingBoxes { Min = Min(newVerts), Max = Max(newVerts), Unk = tBody.BoundingBox.Unk };
            meshes.Add(mesh);
            kept++; verts += newVerts.Count;
        }

        // template meshes kept as they are (e.g. DS2's bow for the bow variant), with their own material
        keepTex = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tm in dst.Meshes.Where(m => o.KeepTemplateMeshes.Contains(dst.Materials[m.MaterialIndex].Name)))
        {
            var tMatl = dst.Materials[tm.MaterialIndex];
            foreach (var tex in tMatl.Textures) keepTex.Add(Path.GetFileNameWithoutExtension(tex.Path).Replace(t, n));
            mats.Add(tMatl);
            tm.MaterialIndex = mats.Count - 1;
            meshes.Add(tm);
        }
        dst.Materials = mats;
        dst.Meshes = meshes;
        var all = meshes.SelectMany(m => m.Vertices).ToList();
        dst.Header.BoundingBoxMin = Min(all);
        dst.Header.BoundingBoxMax = Max(all);
        report = $"{kept} meshes, {verts} vertices, {mats.Count} materials, textures " + string.Join(", ", texUse.Select(kv => $"{kv.Key}←{kv.Value}"));
        // (Build checks every DS1 texture named here was found; a missing one would silently become a dark placeholder)
        return dst;
    }

    /// <summary>
    /// A template material by name, or "NAME@BONE" when several share the name (c1320: "WP_A_0401" on L_shield and on
    /// R_Weapon): the one whose mesh is skinned to that bone.
    /// </summary>
    internal static int TemplateMaterial(FLVER2 dst, string spec, string t)
    {
        string name = spec, bone = "";
        int at = spec.IndexOf('@');
        if (at >= 0) { name = spec[..at]; bone = ChrCompare.Norm(spec[(at + 1)..]); }
        for (int i = 0; i < dst.Materials.Count; i++)
        {
            if (dst.Materials[i].Name != name) continue;
            if (bone.Length == 0 || dst.Meshes.Any(m => m.MaterialIndex == i && m.BoneIndices.Any(b => ChrCompare.Norm(dst.Nodes[b].Name) == bone)))
                return i;
        }
        throw new InvalidDataException($"{t} has no material '{spec}'");
    }

    /// <summary>
    /// Turns a rigid weapon mesh about the grip so its principal axes (longest, middle, shortest extent) lie along the
    /// reference mesh's, each axis signed towards its farther end (a blade points away from the grip, not the pommel).
    /// </summary>
    static void AlignLike(List<FLVER.Vertex> verts, IList<FLVER.Vertex> reference, Vector3 grip)
    {
        var a = Axes(verts.Select(v => v.Position - grip).ToList());
        var r = Axes(reference.Select(v => v.Position - grip).ToList());
        // row-vector convention: v · Aᵀ = coordinates along a's axes, · R = the same coordinates along r's axes
        var am = new Matrix4x4(a[0].X, a[1].X, a[2].X, 0, a[0].Y, a[1].Y, a[2].Y, 0, a[0].Z, a[1].Z, a[2].Z, 0, 0, 0, 0, 1);
        var rm = new Matrix4x4(r[0].X, r[0].Y, r[0].Z, 0, r[1].X, r[1].Y, r[1].Z, 0, r[2].X, r[2].Y, r[2].Z, 0, 0, 0, 0, 1);
        var rot = am * rm;
        foreach (var v in verts)
        {
            v.Position = grip + Vector3.TransformNormal(v.Position - grip, rot);
            v.Normal = Vector3.Normalize(Vector3.TransformNormal(v.Normal, rot));
            if (v.Tangents.Count > 0)
            {
                var t = Vector3.Normalize(Vector3.TransformNormal(new Vector3(v.Tangents[0].X, v.Tangents[0].Y, v.Tangents[0].Z), rot));
                v.Tangents[0] = new Vector4(t, v.Tangents[0].W);
            }
        }
    }

    /// <summary>Principal axes of points (Jacobi eigen-decomposition of the covariance), longest first, each pointing
    /// towards its farther extreme; the third is the cross product of the first two (a rotation, never a mirror).</summary>
    static Vector3[] Axes(List<Vector3> pts)
    {
        var c = new double[3, 3];
        foreach (var p in pts)
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    c[i, j] += (double)p[i] * p[j];
        var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        for (int sweep = 0; sweep < 50; sweep++)
            for (int p = 0; p < 2; p++)
                for (int q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(c[p, q]) < 1e-12) continue;
                    double theta = (c[q, q] - c[p, p]) / (2 * c[p, q]);
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0) t = 1;
                    double cs = 1 / Math.Sqrt(t * t + 1), sn = t * cs;
                    for (int k = 0; k < 3; k++)
                    {
                        double ckp = c[k, p], ckq = c[k, q];
                        c[k, p] = cs * ckp - sn * ckq; c[k, q] = sn * ckp + cs * ckq;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        double cpk = c[p, k], cqk = c[q, k];
                        c[p, k] = cs * cpk - sn * cqk; c[q, k] = sn * cpk + cs * cqk;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        double vkp = v[k, p], vkq = v[k, q];
                        v[k, p] = cs * vkp - sn * vkq; v[k, q] = sn * vkp + cs * vkq;
                    }
                }
        var order = Enumerable.Range(0, 3).OrderByDescending(i => c[i, i]).ToArray();
        var axes = order.Select(i => new Vector3((float)v[0, i], (float)v[1, i], (float)v[2, i])).ToArray();
        for (int k = 0; k < 2; k++)
        {
            float max = pts.Max(p => Vector3.Dot(p, axes[k])), min = pts.Min(p => Vector3.Dot(p, axes[k]));
            if (-min > max) axes[k] = -axes[k];
        }
        axes[2] = Vector3.Normalize(Vector3.Cross(axes[0], axes[1]));
        return axes;
    }

    /// <summary>Shortest-arc rotation turning direction a into b.</summary>
    static Quaternion Arc(Vector3 a, Vector3 b)
    {
        float d = Vector3.Dot(a, b);
        if (d > 0.99999f) return Quaternion.Identity;
        if (d < -0.99999f)
        {
            var axis = Vector3.Cross(a, Vector3.UnitX);
            if (axis.LengthSquared() < 1e-6f) axis = Vector3.Cross(a, Vector3.UnitY);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        var c = Vector3.Cross(a, b);
        return Quaternion.Normalize(new Quaternion(c, 1 + d));
    }

    static Vector3 Min(IEnumerable<FLVER.Vertex> vs) => vs.Aggregate(new Vector3(float.MaxValue), (a, v) => Vector3.Min(a, v.Position));
    static Vector3 Max(IEnumerable<FLVER.Vertex> vs) => vs.Aggregate(new Vector3(float.MinValue), (a, v) => Vector3.Max(a, v.Position));

    /// <summary>Every texture of the DS1 character (its chrbnd TPFs, the texture chrbnd <c>Ds1TexChr</c>, and split
    /// chrtpfbhd/bdt archives of bigger characters) by name. Shared by the interim port and the 1:1 port (session 8).</summary>
    internal static Dictionary<string, TPF.Texture> LoadDs1Textures(string dsrRoot, Options o)
    {
        var ds1Tex = new Dictionary<string, TPF.Texture>(StringComparer.OrdinalIgnoreCase);
        foreach (string chr in new[] { o.Ds1Chr, o.Ds1TexChr })
        {
            string path = Path.Combine(dsrRoot, "chr", chr + ".chrbnd.dcx");
            if (!File.Exists(path)) continue;
            var chrbnd = BND3.Read(path);
            foreach (var f in chrbnd.Files.Where(f => f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase)))
                foreach (var tex in TPF.Read(f.Bytes).Textures) ds1Tex[tex.Name] = tex;
            // bigger characters keep their textures in a split archive: chrtpfbhd inside the chrbnd, .chrtpfbdt beside it
            var bhd = chrbnd.Files.FirstOrDefault(f => f.Name.EndsWith(".chrtpfbhd", StringComparison.OrdinalIgnoreCase));
            string bdt = Path.Combine(dsrRoot, "chr", chr + ".chrtpfbdt");
            if (bhd != null && File.Exists(bdt))
                foreach (var f in BXF3.Read(bhd.Bytes, File.ReadAllBytes(bdt)).Files.Where(f => f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase) || f.Name.EndsWith(".tpf.dcx", StringComparison.OrdinalIgnoreCase)))
                    foreach (var tex in TPF.Read(DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes).Textures) ds1Tex[tex.Name] = tex;
        }
        return ds1Tex;
    }

    /// <summary>The template's texbnd with every texture renamed for model n (<see cref="UniqueTexName"/>) and DS1 pixels
    /// where our materials use them (texUse: template texture name → DS1 texture name); kept template meshes' textures stay.</summary>
    internal static byte[] WriteTexbnd(string data, string t, string n, Options o, Dictionary<string, string> texUse, HashSet<string> keepTex, Dictionary<string, TPF.Texture> ds1Tex)
    {
        var texbnd = BND4.Read(Path.Combine(data, "model", "chr", t + ".texbnd"));
        foreach (var f in texbnd.Files)
        {
            var tpf = TPF.Read(f.Bytes);
            foreach (var tex in tpf.Textures)
            {
                string newName = tex.Name.Replace(t, n);
                string kind = newName[^2..].ToLowerInvariant(); // _d _n _s
                if (keepTex.Contains(newName)) { tex.Name = UniqueTexName(newName, n); continue; } // a kept template mesh's texture
                (byte[] Bytes, byte Format) px;
                if (texUse.TryGetValue(newName, out string? ds1Name) && ds1Tex.TryGetValue(ds1Name, out var s))
                    px = kind switch
                    {
                        "_n" => (Ds2Tex.Normal(s.Bytes), Ds2Tex.FormatNormal),
                        "_s" => (Ds2Tex.Specular(s.Bytes), Ds2Tex.FormatDxt1),
                        _ when newName.StartsWith(o.BodyMaterial, StringComparison.OrdinalIgnoreCase) && (o.BodySaturation != 1f || o.BodyBrightness != 1f)
                            => Ds2Tex.ColorGraded(s.Bytes, o.BodySaturation, o.BodyBrightness),
                        _ => Ds2Tex.Color(s.Bytes),
                    };
                else
                    px = kind switch { "_n" => (Ds2Tex.FlatNormal(), Ds2Tex.FormatNormal), "_s" => (Ds2Tex.Solid(0, 32, 126), Ds2Tex.FormatDxt1), _ => (Ds2Tex.Solid(60, 55, 50), Ds2Tex.FormatDxt1) };
                tex.Name = UniqueTexName(newName, n);
                tex.Bytes = px.Bytes;
                tex.Format = px.Format;
                tex.Mipmaps = (byte)Ds2Tex.MipCount(px.Bytes);
            }
            f.Bytes = tpf.Write();
            f.Name = UniqueTexPath(f.Name.Replace(t, n), n);
        }
        return texbnd.Write();
    }

    /// <summary>The template's ACB for model n: ids replaced, texture assets renamed (<see cref="UniqueTexPath"/>), the
    /// cloth asset dropped when the cloth files are.</summary>
    internal static byte[] RewriteAcb(byte[] bytes, string t, string n, bool dropCloth)
    {
        var acb = ACB.Read(ReplaceAscii(bytes, t, n));
        if (dropCloth) acb.Assets.RemoveAll(a => a is ACB.Asset.General g && g.AbsolutePath.Contains("_Cloth", StringComparison.OrdinalIgnoreCase));
        foreach (var a in acb.Assets.OfType<ACB.Asset.Texture>())
        {
            a.AbsolutePath = UniqueTexPath(a.AbsolutePath, n);
            if (!string.IsNullOrEmpty(a.RelativePath)) a.RelativePath = UniqueTexPath(a.RelativePath, n);
        }
        return acb.Write();
    }

    /// <summary>A copy of the template's material <paramref name="templateName"/> for model n, recording in
    /// <paramref name="texUse"/> which DS1 texture each of its diffuse/specular/normal slots takes from <paramref name="ds1"/>.</summary>
    internal static FLVER2.Material MaterialFromTemplate(FLVER2 template, string templateName, FLVER2.Material ds1, string t, string n, Dictionary<string, string> texUse)
    {
        var tm = template.Materials[TemplateMaterial(template, templateName, t)];
        var m = new FLVER2.Material(tm.Name.Replace(t, n), tm.MTD, 0) { GXIndex = tm.GXIndex, Index = tm.Index };
        foreach (var x in tm.Textures)
            m.Textures.Add(new FLVER2.Texture(x.ParamName, x.Path.Replace(t, n), x.TilingScale, x.TilingTypeU, x.TilingTypeV, x.Unk14, x.Unk18, x.Unk1C));
        foreach (var tex in m.Textures)
        {
            string slot = tex.ParamName switch { "g_DiffuseTexture" => "g_Diffuse", "g_SpecularTexture" => "g_Specular", "g_BumpmapTexture" => "g_Bumpmap", _ => "" };
            var s = ds1.Textures.FirstOrDefault(x => x.ParamName == slot && !string.IsNullOrEmpty(x.Path));
            if (s != null) texUse[Path.GetFileNameWithoutExtension(tex.Path)] = Path.GetFileNameWithoutExtension(s.Path);
        }
        return m;
    }

    /// <summary>A texture name owned by model n alone: "c2550_WP_1400_M_d" (names already holding "c2550" stay).</summary>
    internal static string UniqueTexName(string name, string n) => name.Contains(n, StringComparison.OrdinalIgnoreCase) ? name : $"{n}_{name}";

    /// <summary>UniqueTexName on the file name of a path (FLVER material path, ACB asset path, texbnd entry name).</summary>
    internal static string UniqueTexPath(string path, string n)
    {
        int cut = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/')) + 1;
        string file = path[cut..];
        int dot = file.IndexOf('.');
        string stem = dot < 0 ? file : file[..dot], ext = dot < 0 ? "" : file[dot..];
        return path[..cut] + UniqueTexName(stem, n) + ext;
    }

    /// <summary>Same-length ASCII and UTF-16 replacement inside a binary file (model ids in ACB/physics/ragdoll files).</summary>
    internal static byte[] ReplaceAscii(byte[] bytes, string from, string to)
    {
        var result = (byte[])bytes.Clone();
        foreach (var enc in new Encoding[] { Encoding.ASCII, Encoding.Unicode })
        {
            byte[] a = enc.GetBytes(from), b = enc.GetBytes(to);
            for (int i = 0; i + a.Length <= result.Length; i++)
                if (result.AsSpan(i, a.Length).SequenceEqual(a)) { b.CopyTo(result, i); i += a.Length - 1; }
        }
        return result;
    }
}
