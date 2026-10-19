using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// The DS2 params behind a 1:1 DS1 character's attacks (session 8, track enemies step 1): DS1 BehaviorParam / AtkParam_Npc /
/// BulletParam → DS2 EnemyBehavior(Second/Third)Param, EnemyComboBehaviorParam, EnemyDamageParam, EnemyBulletParam, plus the
/// EnemyEquipWeaponParam row that ties a variant to them and to its DS1 weapon meshes.
/// <para><b>Chain</b> (DS2, V: decompile FUN_1403a8fb0 / FUN_14039ebe0 and the params, NETWORK-FORMAT.md §3): EnemyParam
/// .defaultEquipWeapon = slot w (= WeaponCategory_Ene the network routes on) → equipWeaponAId0w = EnemyEquipWeaponParam row
/// {behaviorId, cullingId00/01} → EnemyBehaviorParam / Second / Third [behaviorId] action n % 10 (n 0-9 / 10-19 / 20-29) and
/// EnemyComboBehaviorParam [behaviorId] combo k; the action block's BladeCtrlType 4 = slot disabled (the engine refuses the
/// request). A TimeAct 2200 event with ID i opens the hit of the current action's DamageId0i (plain action) or AttackId0(i+1)
/// (combo row), 2300 ID i fires BulletId0i / BulletId0(i+1) (Smelter c305000_pl: combo stages 1-4 carry IDs 0-3; 4114 2200
/// events in all chr TAEs). The bullet's own hit uses the action's DamageId of the same slot (DS2 Hollow Soldier firebomb:
/// action 22 DamageId00 = BulletId00 = 110201710).</para>
/// <para><b>DS1 → DS2 values</b>: damage from AtkParam_Npc atkPhys / atkMag / atkFire / atkThun into DS2 damage01..03 with
/// damageType Physical 0 / Magic 1 / Lightning 2 / Fire 3 (DAMAGE_ATTRIBUTE), scaled by <see cref="DamageScale"/>; stamina
/// damage by <see cref="StaminaScale"/>; DS1's hit sphere hit0 (radius, dummies DmyPoly1 → DmyPoly2) → DS2 hitModelType
/// Capsule 1 at DmyPoly1 with length |DmyPoly2 − DmyPoly1| (DS2 extends the capsule along the dummy's forward axis: c1030
/// knife dummy 100 forward (0,−1,0) on R_Weapon = the blade, length 0.65; DS1's dummy 1 points along the blade too), or a
/// sphere when DmyPoly2 is −1; knockback distance → knockBackPower. Everything else (damage category, stiffness, trace SFX,
/// flags) is the DS2 template row's (DS2 rules). Bullets: DS2 visuals and ballistics of the matching DS2 bullet (DS1 FFX ids
/// have no DS2 counterpart), DS1's speed, life and explosion (child bullet + its damage).</para>
/// </summary>
public static class Ds1AttackParams
{
    /// <summary>DS1 → DS2 enemy attack calibration (provisional, session 8, one pair like ChrPorter.HpScale): DS1's Asylum
    /// Hollow broken-sword slash (AtkParam_Npc 250000: atkPhys 100, atkStam 20) and DS2's first armed enemy, the Things Betwixt
    /// Undead Traveler's dagger slash (EnemyDamageParam 111501010: damage01 146, staminaDamage 260).</summary>
    public const double DamageScale = Ds1Calibration.DamageScale, StaminaScale = Ds1Calibration.StaminaScale;   // session 11 (burgchr11): one calibration (Ds1Calibration, rule E9u)

    /// <summary>One armed 1:1 variant: EnemyParam id, DS1 NpcParam, the culling ids its weapon shows (DS1 modelDispMask →
    /// template weapon materials), the DS2 template variant whose behaviour / combo / damage rows give the DS2-only fields.</summary>
    public sealed record ArmedVariant(int EnemyParamId, int Ds1Npc, int[] CullingIds, int TemplateEnemyParam, string Note);

    static void Set(PARAM.Row row, string field, object value)
    {
        var c = row[field] ?? throw new InvalidDataException($"param {row.ID}: no field {field}");
        c.Value = Convert.ChangeType(value, c.Value.GetType());
    }
    static double D(PARAM.Row r, string f) => Convert.ToDouble(r[f]?.Value ?? 0);
    static int I(PARAM.Row r, string f) => Convert.ToInt32(r[f]?.Value ?? 0);

    /// <summary>DS1 dummy position (model space) by reference id, first match.</summary>
    static Vector3? Dummy(FLVER2 f, int id) => f.Dummies.FirstOrDefault(d => d.ReferenceID == id)?.Position;

    /// <summary>Behaviour-row slot fields: "action{k}..." and "fAction{k}..." for k = slot (Behavior/Second/Third).</summary>
    static IEnumerable<PARAM.Cell> SlotCells(PARAM.Row r, int k) => r.Cells.Where(c => c.Def.InternalName.StartsWith($"action{k}") || c.Def.InternalName.StartsWith($"fAction{k}"));
    static string SlotField(string name, int from, int to) => name.StartsWith("fAction") ? $"fAction{to}{name[$"fAction{from}".Length..]}" : $"action{to}{name[$"action{from}".Length..]}";
    static IEnumerable<PARAM.Cell> ComboCells(PARAM.Row r, int k) => r.Cells.Where(c => c.Def.InternalName.StartsWith($"combo{k}"));

    public sealed class Written { public List<string> Notes = []; public int DamageRows, BulletRows; }

    /// <summary>Rule E10g (session 12, bomb12): a thrown DS1 bullet's DS2 weight (DS2's own Firebomb BulletParam 60570000);
    /// gravityValue = DS1 gravityInRange / this, so weight × gravityValue = DS1's fall in m/s² (P: product semantics).</summary>
    public const float ThrownWeight = 5f;
    // ------------------------------------------------------------------ session 12 (ehit12): rule E-ID1, DS2's damage / bullet id ranges
    /// <summary>
    /// Rule E-ID1 (session 12, track ehit12): DS2 resolves a damage or bullet id by its RANGE, not by the param a behaviour row
    /// belongs to: id &lt; 100,000,000 = PlayerDamageParam / BulletParam, [100,000,000, 200,000,000) = EnemyDamageParam /
    /// EnemyBulletParam, [200,000,000, 1,000,000,000) = SystemDamageParam / SystemBulletParam (V: decompile FUN_1404434c0
    /// l.847321-847328 picks the table by `id &lt; 1e8` / `id − 1e8 &lt; 1e8` / `id − 2e8 &lt; 8e8`; the row getter
    /// FUN_141afba53 → FUN_141c665cd the same split, l.3225140 / l.3283651; vanilla: every EnemyDamageParam row but the test row 11
    /// is in [100,000,000, 195,310,040], every EnemyBulletParam row in [100,000,010, 190,100,210], every real SystemDamageParam /
    /// SystemBulletParam row ≥ 200,001,010, every PlayerDamageParam row ≤ 62,100,010). The 1:1 rows were 100,000,000 + DS1
    /// BehaviorParam 2_VVVVV_JJJ (= 3xx,xxx,xxx), the bosses' 200,000,000 + V·1000 + J·10 + h and the breath 300,000,000 + Bullet:
    /// all in the System range, so the engine looked them up in SystemDamageParam, found nothing and no DS1 attack (melee, bullet,
    /// explosion, breath) ever hit (user report, build _base39: "DS1 enemies' attacks never hit the player").
    /// <para>Ids now: <see cref="EnemyRowBase"/> + rank(V)·10,000 + J·10 + k, rank(V) = the index of DS1 variation V among the
    /// distinct variations of DS1's NPC BehaviorParam (2_VVVVV_JJJ; 135 in DSR, fixed data → stable ids), J = the judge (0-999),
    /// k = the row's kind: 0 the judge's hit (regulars' melee, a bullet and its own hit), 1 a bullet's explosion child, 2 its
    /// lingering cloud; bosses: k = DS1 AtkParam hit h (0-3); 9 with J = 999 the breath bullet (rule HB1). DS2 blocks 134-149
    /// (×10⁶) are empty in vanilla EnemyDamageParam and EnemyBulletParam, and 135 × 10,000 ids end below 136,350,000.</para>
    /// </summary>
    public const int EnemyRowBase = 134_000_000, EnemyRowEnd = 150_000_000;
    /// <summary>Rule E-ID1: the DS2 enemy range every DS2 damage / bullet id of an enemy attack must lie in.</summary>
    public const int Ds2EnemyRangeLo = 100_000_000, Ds2EnemyRangeHi = 200_000_000;
    static int[]? variationRank;
    static int[] Variations(PARAM ds1Behavior)
    {
        var v = variationRank;
        if (v != null) return v;
        v = [.. ds1Behavior.Rows.Where(r => r.ID is >= 200_000_000 and < 300_000_000).Select(r => (r.ID - 200_000_000) / 1000).Distinct().Order()];
        if (EnemyRowBase + v.Length * 10_000L > EnemyRowEnd) throw new InvalidDataException($"rule E-ID1: {v.Length} DS1 variations do not fit [{EnemyRowBase}, {EnemyRowEnd})");
        return variationRank = v;
    }
    /// <summary>Rule E-ID1: the EnemyDamageParam / EnemyBulletParam id of DS1 variation <paramref name="variation"/>'s judge
    /// <paramref name="judge"/>, row kind <paramref name="k"/> (see <see cref="EnemyRowBase"/>).</summary>
    public static int EnemyRow(PARAM ds1Behavior, int variation, int judge, int k)
    {
        if (judge is < 0 or > 999 || k is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(judge), $"rule E-ID1: judge {judge} / kind {k}");
        int i = Array.BinarySearch(Variations(ds1Behavior), variation);
        if (i < 0) throw new InvalidDataException($"rule E-ID1: DS1 variation {variation} has no BehaviorParam rows");
        return EnemyRowBase + i * 10_000 + judge * 10 + k;
    }
    /// <summary>Rule E-ID1, inverse: (variation, judge, kind) of a 1:1 row id, or null for any other id.</summary>
    public static (int Variation, int Judge, int K)? FromEnemyRow(PARAM ds1Behavior, int id)
    {
        if (id < EnemyRowBase || id >= EnemyRowEnd) return null;
        var v = Variations(ds1Behavior); int i = (id - EnemyRowBase) / 10_000, rest = (id - EnemyRowBase) % 10_000;
        return i < v.Length ? (v[i], rest / 10, rest % 10) : null;
    }
    /// <summary>Rule E-ID2 (session 12, ehit12): the SystemDamageParam (+ DamageCtrlParam) row of DS1 damaging object
    /// <paramref name="ds1Entity"/> (DS1 2005:9, rule WE4): <see cref="ObjectRowBase"/> + the DS1 entity id, in DS2's System range
    /// (rule E-ID1's split; vanilla System rows sit in 200-349 million, blocks 380-399 million are empty in SystemDamageParam,
    /// SystemBulletParam and DamageCtrlParam). Was the object's MapObjectInstanceParam id (18011215, 19411101 …: the PLAYER range,
    /// so the engine looked in PlayerDamageParam and the boulder never hurt). Decoupled from the instance: damageId0 names it.</summary>
    public static int ObjectDamageRow(int ds1Entity) => ds1Entity is > 0 and < 20_000_000 ? ObjectRowBase + ds1Entity
        : throw new ArgumentOutOfRangeException(nameof(ds1Entity), $"rule E-ID2: DS1 entity {ds1Entity} outside 1..19,999,999");
    public const int ObjectRowBase = 380_000_000;
    /// <summary>Rule E-ID1 / E-ID2: the id range [lo, hi) the engine reads <paramref name="param"/> rows from (null: not a ranged table).</summary>
    public static (long Lo, long Hi)? RangeOf(string param) => param switch
    {
        "PlayerDamageParam" or "BulletParam" => (0, 100_000_000),
        "EnemyDamageParam" or "EnemyBulletParam" => (100_000_000, 200_000_000),
        "SystemDamageParam" or "SystemBulletParam" => (200_000_000, 1_000_000_000),
        _ => null,
    };

    /// <summary>Rule E-ID1: is <paramref name="id"/> a 1:1 row (this rule's block).</summary>
    public static bool IsEnemyRow(int id) => id is >= EnemyRowBase and < EnemyRowEnd;

    /// <summary>Write every param row of one armed variant. <paramref name="ds1Flver"/> = DS1's model (dummies for the hit
    /// capsules); <paramref name="attacks"/> = the network's attack layer of the variant's anibnd.</summary>
    public static Written Write(Pack pack, Ds1Source ds1, ArmedVariant v, Ds1Attacks.Plan plan, Ds1Attacks.Result attacks, FLVER2 ds1Flver, string chrNote)
    {
        var w = new Written();
        var npcRow = ds1.Params.Params["NpcParam"][v.Ds1Npc] ?? throw new InvalidDataException($"DS1 NpcParam {v.Ds1Npc} missing");
        int variation = I(npcRow, "behaviorVariationId");
        // session 11 (track enemy11, G12 rule C-AR / CP-F5 / CP-F6): DS1 multiplies an NPC's attack by its resident SpEffects
        // (NpcParam.spEffectID0-7: the Area-Scaling rows 7001-7007 physicsAttackPowerRate 1 / 1.125 / 1.25 / 1.6 / 1.7): folded
        // into the damage here (phys, magic, fire, lightning, stamina)
        var area = new[] { "physicsAttackPowerRate", "magicAttackPowerRate", "fireAttackPowerRate", "thunderAttackPowerRate", "staminaAttackRate" }.Select(f => ResidentRate(ds1, npcRow, f)).ToArray();
        if (area.Any(x => x != 1)) w.Notes.Add($"DS1 NpcParam {v.Ds1Npc} resident SpEffects: attack x{area[0]:0.###} (mag {area[1]:0.###}, fire {area[2]:0.###}, thun {area[3]:0.###}), stamina damage x{area[4]:0.###}");
        if (variation != plan.BehaviorVariation) w.Notes.Add($"NOTE NpcParam {v.Ds1Npc} behaviorVariationId {variation} != plan {plan.BehaviorVariation}");
        var behParam = ds1.Params.Params["BehaviorParam"]; var atk = ds1.Params.Params["AtkParam_Npc"]; var bul = ds1.Params.Params["Bullet"];
        var enemy = pack.Param("EnemyParam");
        var tEnemy = pack.Param("EnemyParam")[v.TemplateEnemyParam] ?? throw new InvalidDataException($"template EnemyParam {v.TemplateEnemyParam}");
        int tSlot = I(tEnemy, "defaultEquipWeapon");
        var tWeapon = pack.Param("EnemyEquipWeaponParam")[I(tEnemy, $"equipWeaponAId0{tSlot}")] ?? throw new InvalidDataException($"template weapon of {v.TemplateEnemyParam}");
        int tBeh = I(tWeapon, "behaviorId");
        int beh = v.EnemyParamId;
        // ---- EnemyEquipWeaponParam: behaviour row + culling ids (DS1 weapon meshes on the model)
        int weaponId = v.EnemyParamId * 100;
        var wr = pack.AddRow("EnemyEquipWeaponParam", tWeapon, weaponId, $"[Lordran] {chrNote} {v.Note}: DS1 weapon");
        Set(wr, "behaviorId", beh);
        for (int k = 0; k < 2; k++) Set(wr, $"cullingId0{k}", k < v.CullingIds.Length ? v.CullingIds[k] : 0);
        // ---- EnemyParam: weapon slot = the network's weapon category
        var er = enemy[v.EnemyParamId] ?? throw new InvalidDataException($"EnemyParam {v.EnemyParamId} not built");
        Set(er, "defaultEquipWeapon", plan.WeaponCategory);
        for (int k = 0; k < 6; k++) if (er[$"equipWeaponAId0{k}"] != null) Set(er, $"equipWeaponAId0{k}", k == plan.WeaponCategory ? weaponId : 0);

        // ---- damage rows per DS1 judge, bullets
        var dmgParam = pack.Param("EnemyDamageParam");
        int Damage(int judge, PARAM.Row template)
        {
            int beId = 200000000 + variation * 1000 + judge;
            int id = EnemyRow(behParam, variation, judge, 0);   // session 12 (ehit12), rule E-ID1: in DS2's enemy id range (was 100,000,000 + beId)
            if (dmgParam[id] != null) return id;
            var be = behParam[beId] ?? throw new InvalidDataException($"DS1 BehaviorParam {beId} missing");
            var a = atk[I(be, "refId")] ?? throw new InvalidDataException($"DS1 AtkParam_Npc {I(be, "refId")} missing (BehaviorParam {beId})");
            var r = pack.AddRow("EnemyDamageParam", template, id, $"[Lordran] {chrNote} DS1 BehaviorParam {beId} / AtkParam_Npc {a.ID}");
            AtkToDamage(a, r, ds1Flver, w.Notes, $"{beId}", area, pack, chrNote);
            w.DamageRows++;
            return id;
        }
        var tBehRows = new[] { "EnemyBehaviorParam", "EnemyBehaviorSecondParam", "EnemyBehaviorThirdParam" }.Select(p => pack.Param(p)[tBeh]).ToArray();
        // the DS2 template's first damage row (its first active action) gives the DS2-only damage fields
        PARAM.Row? tDmg = null; (int Block, int Slot) tAct = (-1, -1);
        for (int b = 0; b < 3 && tDmg == null; b++)
            for (int k = 0; k < 10 && tDmg == null; k++)
                if (tBehRows[b] != null && I(tBehRows[b]!, $"action{k}BladeCtrlType") != 4 && I(tBehRows[b]!, $"action{k}DamageId00") > 0)
                {
                    tDmg = dmgParam[I(tBehRows[b]!, $"action{k}DamageId00")];
                    if (tDmg != null) tAct = (b, k);
                }
        var comboT = pack.Param("EnemyComboBehaviorParam")[tBeh];
        if (tDmg == null && comboT != null)
            for (int k = 0; k < 7 && tDmg == null; k++) if (I(comboT, $"combo{k}AttackId01") > 0) tDmg = dmgParam[I(comboT, $"combo{k}AttackId01")];
        tDmg ??= dmgParam[110302010]!;
        if (tAct.Block < 0) { var t2 = pack.Param("EnemyBehaviorSecondParam")[103020]!; tBehRows[1] = t2; tAct = (1, 2); }   // sword & shield action 12 as the field donor
        var tActRow = tBehRows[tAct.Block]!;

        (int Bullet, int Damage) Bullet(int judge, int dummy) => BulletRows(pack, ds1, variation, judge, dummy, ds1Flver, w, area, chrNote);   // session 11 (fx11): shared with the bosses (rule B-BUL)

        // ---- behaviour rows: actions
        var actionOf = attacks.Actions.ToDictionary(x => x.Action, x => x.Id);
        string[] behParams = ["EnemyBehaviorParam", "EnemyBehaviorSecondParam", "EnemyBehaviorThirdParam"];
        int active = 0;
        for (int b = 0; b < 3; b++)
        {
            var tRow = pack.Param(behParams[b])[tBeh] ?? tActRow;
            var r = pack.AddRow(behParams[b], tRow, beh, $"[Lordran] {chrNote} {v.Note}: DS1 attacks {b * 10}-{b * 10 + 9}");
            for (int k = 0; k < 10; k++)
            {
                int n = b * 10 + k;
                if (!actionOf.TryGetValue(n, out int id)) { Set(r, $"action{k}BladeCtrlType", 4); continue; }
                foreach (var c in SlotCells(tActRow, tAct.Slot)) r[SlotField(c.Def.InternalName, tAct.Slot, k)]!.Value = c.Value;
                // session 11 (track enemy11, G2 rule SW1 / EF0): the movement and timing fields of an attack are DS1's: a DS1 clip
                // plays at 1x in both DS2 swing windows (engine: Attack_SwingSpeed = +0x98 BackSwing / +0xa8 FollowSwing by the
                // AttackSpeedControl event flags, DarkSoulsII.c l.728473-728491), and DS1 aims at the target's present position
                // (no 0.5 s lead; DS1 has no estimate-future field)
                Set(r, $"fAction{k}AttackBackSwingSpeed", 1f); Set(r, $"fAction{k}AttackFollowSwingSpeed", 1f); Set(r, $"fAction{k}AttackEstimateFutureTime", 0f);
                var co = attacks.ForAction(id);   // session 11 (burgchr11, rule CH5): a slot-0 variant when the clip is a stage-4 clip
                for (int s = 0; s < 3; s++) { Set(r, $"action{k}DamageId0{s}", -1); Set(r, $"action{k}BulletId0{s}", -1); Set(r, $"action{k}GrabDamageId0{s}", -1); }
                if (co.Timing.Judges.Count > 0)
                {
                    if (co.HitIndex > 2) throw new InvalidDataException($"{chrNote}: DS1 {id} (hit slot {co.HitIndex}) cannot be a plain action (DamageId00-02)");
                    foreach (int j in co.Timing.Judges)
                    {
                        if (co.SlotOf(j) > 2) throw new InvalidDataException($"{chrNote}: DS1 {id} judge {j} needs damage slot {co.SlotOf(j)} (plain actions have 0-2)");
                        Set(r, $"action{k}DamageId0{co.SlotOf(j)}", Damage(j, tDmg));
                    }
                }
                if (co.Timing.Bullets.Count > 0)
                {
                    var (bid, did) = Bullet(co.Timing.Bullets[0].Judge, co.Timing.Bullets[0].Dummy);
                    Set(r, $"action{k}BulletId0{co.BulletIndex}", bid);
                    if (co.Timing.Judges.Count == 0) Set(r, $"action{k}DamageId0{co.BulletIndex}", did);
                }
                Set(r, $"action{k}BladeCtrlType", 0);
                // AttackRange stays the DS2 donor's (Hollow Infantry: 50 / 2.9): what the engine or the DS2 AI does with it is
                // unknown, and the template AI drives these characters until step 2; DS1's ranges live in the plan (Attack.Range)
                active++;
            }
        }
        // ---- combo rows
        var ctT = comboT ?? pack.Param("EnemyComboBehaviorParam")[103020]!;
        int tCombo = Enumerable.Range(0, 7).FirstOrDefault(k => I(ctT, $"combo{k}BladeCtrlType") != 4 && I(ctT, $"combo{k}AttackId01") > 0);
        var donor = I(ctT, $"combo{tCombo}AttackId01") > 0 ? ctT : pack.Param("EnemyComboBehaviorParam")[103020]!;
        if (donor != ctT) tCombo = 0;
        var cr2 = pack.AddRow("EnemyComboBehaviorParam", ctT, beh, $"[Lordran] {chrNote} {v.Note}: DS1 combo chains");
        for (int k = 0; k < 7; k++)
        {
            var ch = attacks.Combos.FirstOrDefault(x => x.Row == k);
            if (ch.Ids == null) { Set(cr2, $"combo{k}BladeCtrlType", 4); continue; }
            foreach (var c in ComboCells(donor, tCombo)) cr2[$"combo{k}{c.Def.InternalName[$"combo{tCombo}".Length..]}"]!.Value = c.Value;
            Set(cr2, $"combo{k}AttackMotionId", 30 + k);
            for (int s = 1; s <= 4; s++)   // G2 SW1 / EF0 (see the action loop)
            {
                Set(cr2, $"fCombo{k}AttackBackSwingSpeed0{s}", 1f); Set(cr2, $"fCombo{k}AttackFollowSwingSpeed0{s}", 1f); Set(cr2, $"fCombo{k}EstimateFutureTime0{s}", 0f);
            }
            for (int s = 1; s <= 3; s++) Set(cr2, $"combo{k}ComboAttackRoute{s}", s < ch.Ids.Length ? 1 : 0);
            Set(cr2, $"combo{k}BladeCtrlType", 0);
            for (int s = 1; s <= 4; s++) { Set(cr2, $"combo{k}AttackId0{s}", -1); Set(cr2, $"combo{k}BulletId0{s}", -1); }
            for (int st = 0; st < ch.Ids.Length; st++)
            {
                var co = attacks.ForStage(ch.Ids, st);   // session 11 (burgchr11, rule CH5): the variant of this stage's slot
                int id = ch.Ids[st];
                foreach (int j in co.Timing.Judges)
                {
                    if (co.SlotOf(j) > 3) throw new InvalidDataException($"{chrNote}: DS1 {id} judge {j} needs combo slot {co.SlotOf(j) + 1} (AttackId01-04)");
                    Set(cr2, $"combo{k}AttackId0{co.SlotOf(j) + 1}", Damage(j, tDmg));
                }
                if (co.Timing.Bullets.Count > 0)
                {
                    var (bid, did) = Bullet(co.Timing.Bullets[0].Judge, co.Timing.Bullets[0].Dummy);
                    Set(cr2, $"combo{k}BulletId0{co.HitIndex + 1}", bid);
                    // G2 rule TB1: the bullet's own hit uses the stage's AttackId of the same slot, as on a plain action
                    // (DamageId0i = BulletId0i, DS2 Hollow Soldier firebomb action 22): without it the 2nd / 3rd arrow of a volley
                    // hits for nothing
                    if (co.Timing.Judges.Count == 0) Set(cr2, $"combo{k}AttackId0{co.HitIndex + 1}", did);
                }
            }
        }
        w.Notes.Add($"EnemyParam {v.EnemyParamId}: weapon slot {plan.WeaponCategory} = EnemyEquipWeaponParam {weaponId} (behaviour {beh}, culling [{string.Join(", ", v.CullingIds)}]); {active} active actions [{string.Join(", ", attacks.Actions.OrderBy(x => x.Action).Select(x => $"{x.Action}=DS1 {x.Id}"))}], combos [{string.Join("; ", attacks.Combos.Select(c => $"{50 + c.Row}: {string.Join("→", c.Ids)}"))}] (DS2 donors: action {tAct.Block * 10 + tAct.Slot} of {tBeh}, combo {tCombo} of {donor.ID}, damage {tDmg.ID})");
        return w;
    }

    /// <summary>
    /// Session 11 (fx11): the bullet rows of one DS1 bullet behaviour (BehaviorParam 2_VVVVV_JJJ refType 1 → Bullet), rule E10b — the
    /// regulars' local rule moved here unchanged so the 1:1 bosses share it (rule B-BUL, <see cref="Ds1Boss"/>): EnemyBulletParam +
    /// EnemyDamageParam id = rule E-ID1's EnemyRow(V, J, 0) (session 12; was 100,000,000 + the BehaviorParam id, the System range), a thrown bomb's explosion child kind 1, its cloud kind 2. An existing row is reused.
    /// </summary>
    /// <param name="window">session 11 (dchr11, rule CU2): the DS1 TAE bullet event's length (s); 0 = unknown (only used for a curse stream)</param>
    public static (int Bullet, int Damage) BulletRows(Pack pack, Ds1Source ds1, int variation, int judge, int dummy, FLVER2? ds1Flver, Written w, double[] area, string chrNote, double window = 0)
    {
        var behParam = ds1.Params.Params["BehaviorParam"]; var atk = ds1.Params.Params["AtkParam_Npc"]; var bul = ds1.Params.Params["Bullet"];
        var dmgParam = pack.Param("EnemyDamageParam");
        int beId = 200000000 + variation * 1000 + judge;
        int id = EnemyRow(behParam, variation, judge, 0);   // session 12 (ehit12), rule E-ID1 (was 100,000,000 + beId: DS2's System range)
        var be = behParam[beId] ?? throw new InvalidDataException($"DS1 BehaviorParam {beId} missing");
        var b = bul[I(be, "refId")] ?? throw new InvalidDataException($"DS1 Bullet {I(be, "refId")} missing (BehaviorParam {beId})");
        var bp = pack.Param("EnemyBulletParam");
        if (bp[id] != null) return (id, id);
        double curse = CurseOf(ds1, b);   // session 11 (dchr11, rule CU1): 0 on every bullet but a curse carrier (the Basilisk's)
        int hitBullet = I(b, "HitBulletID");
        bool thrown = hitBullet > 0 && D(b, "gravityInRange") > 0;          // a bottle / bomb: flies, explodes into its hit bullet
        // session 11 (burgchr11, rule E10c): a DS1 sorcery bullet (its own hit does magic damage and no physical) takes a DS2 magic
        // bullet's visual: Royal Sorcerer Navlaan's soul-arrow-like 177100310 (sfx 5131, P) instead of the arrow
        bool magic = !thrown && atk[I(b, "atkId_Bullet")] is { } ma && D(ma, "atkMag") > 0 && D(ma, "atkPhys") == 0;
        // session 11 (fx11, rule E10d, bosses): an elemental bullet without physical damage — its own hit or, when it does none, its
        // hit bullet's (the Stray Demon's magic bursts 22300/22305 → 22301/22306; the Gargoyles' fire breath 53500/53501; the
        // Titanite Demon's lightning 23000) — takes the same projectile visual as E10c (P: no DS2 fire / lightning enemy bullet
        // without DS2 child bullets of its own; DS1's FFX is not convertible by B2b for bullets). Regulars: unchanged (their
        // non-thrown bullets with a hit bullet: none; elemental ones are E10c's sorceries)
        if (!thrown && !magic)
        {
            var el = atk[I(b, "atkId_Bullet")] ?? (hitBullet > 0 && bul[hitBullet] is { } hb0 ? atk[I(hb0, "atkId_Bullet")] : null);
            if (el != null && D(el, "atkPhys") == 0 && (D(el, "atkMag") > 0 || D(el, "atkFire") > 0 || D(el, "atkThun") > 0)) magic = true;
        }
        int tId = thrown ? 110201710 : magic ? 177100310 : 110304010;      // DS2 Hollow Soldier firebomb / Navlaan sorcery / Hollow Infantry arrow
        if (curse > 0 && !thrown && bp[CurseBulletTemplate] != null) tId = CurseBulletTemplate;   // rule CU1: DS2's own curse projectile (Basilisk)
        var r = pack.AddRow("EnemyBulletParam", bp[tId]!, id, $"[Lordran] {chrNote} DS1 Bullet {b.ID}");
        if (tId == CurseBulletTemplate)
        {
            // rule CU1: no DS2 child unless DS1 has a hit bullet (E10e below sets it); DS1 isPenetrate = passes through characters
            var arrow = bp[110304010]!;
            foreach (var f in new[] { "childeBulletId", "childeDamageId", "isChildeExplosion", "isChildeLife" }) if (r[f] != null && arrow[f] != null) r[f]!.Value = arrow[f]!.Value;
            Set(r, "penetration", I(b, "isPenetrate") != 0 ? 1 : 0);
            // rule CU2: a DS1 bullet stream (shootInterval > 0: one bullet every interval while the TAE event lasts; a shared hit list
            // re-hits one target at most every dmgHitRecordLifeTime) → DS2 automatic shots at the larger of the two intervals, as many as
            // DS1 can hit one target in the window (DS2 Basilisk 121000330: automaticShootNum 2, automaticInterval 0.3)
            double si = D(b, "shootInterval"), rec = I(b, "isUseSharedHitList") != 0 ? D(b, "dmgHitRecordLifeTime") : 0, step = Math.Max(si, rec);
            if (si > 0 && window > 0)
            {
                int n = (int)Math.Floor(window / step + 1e-6) + 1;
                Set(r, "automaticShootNum", n); Set(r, "automaticInterval", (float)step);
                w.Notes.Add($"rule CU2: DS1 Bullet {b.ID} stream (shootInterval {si}, shared hit list {I(b, "isUseSharedHitList")}, re-hit {D(b, "dmgHitRecordLifeTime")} s) over the TAE window {window:0.###} s → {n} DS2 shots every {step:0.###} s");
            }
        }
        Set(r, "initialVelocity", D(b, "initVellocity"));
        if (D(b, "maxVellocity") > 0) Set(r, "maxSpeed", D(b, "maxVellocity"));
        if (D(b, "life") > 0) Set(r, "bulletLife", D(b, "life"));
        // session 11 (track enemy11, G3 rule E10b "DS1 ballistics, DS2 visuals"; engine semantics from the decompile:
        // FUN_1404404a0 offsets by shootOffset*, rotates by shootAngle (pitch, negative = up: DS2's lobbing throwers Undead
        // Traveler -0.14, Giant -1.309, Gyrm -0.524) / shootAngleXZ, then snaps toward the AI target within ±upDownCorrectAngle /
        // ±rightleftCorrectAngle (radians), with optional target lead (EstimateFuture flags), then random ±accuracy):
        // - acceleration = DS1 accelInRange (DS1 arrow 10 m/s + 2 m/s²: 15 m in 1.18 s), from launch
        // - no target lead and no random spread (DS1 has neither)
        // - aim correction = DS1 lockShootLimitAng on both axes; homing = DS1 homingAngle (deg/s → rad, P: per second)
        // - DS1 gravityInRange applies within dist, gravityOutRange after: a bullet whose in-range gravity is below 1 m/s² flies
        //   straight until it has covered dist (downStartTime = that time); a thrown one falls from launch
        // - a thrown bullet keeps DS2's line-of-sight launch with DS1's elevation shootAngleXZ (+20° = a lob) and no pitch
        //   correction (the correction would flatten the lob onto the target line); yaw correction as above
        // - DS2's gravity multiplier stays the template's (its base gravity is not known offline: in-game calibration)
        Set(r, "acceleration", D(b, "accelInRange"));
        Set(r, "acceleStartTime", 0f);
        Set(r, "upDownEstimateFuture", 0); Set(r, "rightLeftEstimateFuture", 0);
        Set(r, "upDownAccuracy", 0f); Set(r, "rightLeftAccuracy", 0f);
        float lockRad = (float)(D(b, "lockShootLimitAng") * Math.PI / 180);
        Set(r, "rightleftCorrectAngle", lockRad);
        Set(r, "homingAngle", (float)(D(b, "homingAngle") * Math.PI / 180));
        Set(r, "shootOffsetY", 0f);
        // DS1 shootAngleXZ = elevation (the bomb's +20°), shootAngle = horizontal offset → DS2 pitch shootAngle (negative up) /
        // yaw shootAngleXZ (sign P; 0 on every DS1 bullet in scope)
        Set(r, "shootAngle", (float)(-D(b, "shootAngleXZ") * Math.PI / 180));
        Set(r, "shootAngleXZ", (float)(D(b, "shootAngle") * Math.PI / 180));
        if (thrown)
        {
            Set(r, "upDownCorrectAngle", 0f);
            Set(r, "downStartTime", 0f);
            // session 12 (bomb12, rule E10g, P): DS1's fall (gravityInRange, m/s²) as DS2's weight × gravityValue. Every vanilla DS2
            // thrown bomb / urn lobbed at 15-55° has weight × gravityValue = 10 (the player's Firebomb / Black Firebomb / urns 5 × 2,
            // Dragon Acolyte 131700310 5 × 2, Ashen Warrior 165300510/520 10 × 1, Pate 5 × 2: `bullet-check --survey`), which is
            // DS1's own 10 for the Hollow Warrior's bottle 25400; the Hollow Soldier template's 3 × 1.7 = 5.1 belongs to its
            // near-flat throw (-2°) and let DS1's +20° lob fall at half the rate (about twice the range: over the player's head).
            // Weight 5 as DS2's own Firebomb (60570000), gravityValue = DS1 gravity / 5.
            Set(r, "weight", ThrownWeight);
            Set(r, "gravityValue", (float)(D(b, "gravityInRange") / ThrownWeight));
        }
        else
        {
            Set(r, "upDownCorrectAngle", lockRad);
            double v0 = D(b, "initVellocity"), a0 = D(b, "accelInRange"), dist = D(b, "dist");
            double tIn = D(b, "gravityInRange") < 1 && dist > 0 && v0 > 0 ? (a0 > 1e-6 ? (-v0 + Math.Sqrt(v0 * v0 + 2 * a0 * dist)) / a0 : dist / v0) : 0;
            Set(r, "downStartTime", (float)tIn);
        }
        // the bullet's own hit: DS1 atkId_Bullet (none on a firebomb bottle: zero damage, the explosion hurts)
        int ownAtk = I(b, "atkId_Bullet");
        var ownT = dmgParam[tId] ?? dmgParam[110304010]!;   // E10c: the magic visual template may have no damage row of its id
        var own = pack.AddRow("EnemyDamageParam", ownT, id, $"[Lordran] {chrNote} DS1 Bullet {b.ID} hit");
        if (ownAtk > 0 && atk[ownAtk] is { } oa) AtkToDamage(oa, own, null, w.Notes, $"bullet {b.ID}", area, pack, chrNote, b);   // bomb12 E10f: bullet attribute
        else { foreach (var f in new[] { "damage01", "damage02", "damage03", "staminaDamage" }) Set(own, f, 0); Set(own, "knockBackPower", 0f); w.Notes.Add($"DS1 Bullet {b.ID}: no atkId_Bullet → its own hit does no damage (DS1: the hit bullet {hitBullet} does)"); }
        Set(own, "radius", D(b, "hitRadius") > 0 ? D(b, "hitRadius") : D(ownT, "radius"));
        if (curse > 0)
        {
            AddCurse(own, curse, w.Notes, $"DS1 Bullet {b.ID}");
            // rule CU1 with CP-F14: the breath cone grows from hitRadius to hitRadiusMax over spreadTime (as the explosions below)
            if (D(b, "hitRadiusMax") > D(b, "hitRadius") && D(b, "spreadTime") > 0) { Set(own, "expandSize", D(b, "hitRadiusMax")); Set(own, "expandTime", D(b, "spreadTime")); }
        }
        // G3 rule E16b / TB3: the spawn point is this damage row's hitDummyPolyId on the character body model (decompile:
        // FUN_14043f1a0 stores EnemyDamageParam +0x30 type / +0x31 id on the bullet, FUN_1404404a0 → FUN_1401322c0 looks the
        // dummy up on the chr model; a missing id silently falls back to the character root at the feet) = the dummy DS1's
        // TAE bullet event names (archer a02_3000: 30 on bow_string; c2540 bottle: 1 on R_Hand), which our model carries with
        // DS1's bone, position and forward (Ds1RigModel converts DS1's dummies with their ids)
        if (dummy is < 0 or > 255) throw new InvalidDataException($"{chrNote}: DS1 Bullet {b.ID} dummy {dummy} does not fit EnemyDamageParam.hitDummyPolyId (u8)");
        if (ds1Flver != null && Dummy(ds1Flver, dummy) == null) throw new InvalidDataException($"{chrNote}: DS1 Bullet {b.ID}: the DS1 model has no dummy {dummy} (DS2 would spawn the bullet at the feet)");
        Set(own, "hitDummyPolyType", 0); Set(own, "hitDummyPolyId", dummy);
        w.BulletRows++; w.DamageRows++;
        // session 11 (fx11, rule E10e): any DS1 bullet with a hit bullet bursts into it (a thrown bomb, the Stray Demon's magic
        // bursts that fly flat or not at all): the child rows come from the firebomb's (its explosion), DS1's numbers on them
        if (hitBullet > 0 && bul[hitBullet] is { } hb)
        {
            int cid = EnemyRow(behParam, variation, judge, 1);   // rule E-ID1 kind 1 (was id + 1 = the next judge's row)
            int tChild = I(bp[110201710]!, "childeBulletId");
            // rule CU1: a curse bullet bursts into DS2's Basilisk burst (121000630, its petrifying cloud visual) instead of the firebomb's fire
            bool curseChild = curse > 0 && bp[CurseBurstTemplate] != null && dmgParam[CurseBurstTemplate] != null;
            if (curseChild) tChild = CurseBurstTemplate;
            var cr = pack.AddRow("EnemyBulletParam", bp[tChild]!, cid, $"[Lordran] {chrNote} DS1 Bullet {hb.ID} (explosion of {b.ID})");
            if (curseChild) { var arrow = bp[110304010]!; foreach (var f in new[] { "childeBulletId", "childeDamageId", "isChildeExplosion", "isChildeLife" }) if (cr[f] != null && arrow[f] != null) cr[f]!.Value = arrow[f]!.Value; }
            if (D(hb, "life") > 0) Set(cr, "bulletLife", D(hb, "life"));
            var cdT = curseChild ? dmgParam[CurseBurstTemplate]! : dmgParam[I(bp[110201710]!, "childeDamageId")]!;
            var cd = pack.AddRow("EnemyDamageParam", cdT, cid, $"[Lordran] {chrNote} DS1 Bullet {hb.ID} explosion");
            if (curseChild) for (int k = 1; k <= 3; k++) { Set(cd, $"damage0{k}", 0); Set(cd, $"damageType0{k}", 0); }   // CU1: DS1's numbers only
            if (atk[I(hb, "atkId_Bullet")] is { } ha) AtkToDamage(ha, cd, null, w.Notes, $"bullet {hb.ID}", area, pack, chrNote, hb);   // bomb12 E10f: bullet attribute
            // CP-F14: the blast grows from hitRadius to hitRadiusMax over spreadTime (DS2 EnemyDamageParam radius → expandSize
            // in expandTime; every vanilla row with growth has expandSize > radius, P: final radius)
            double rad = D(hb, "hitRadius") > 0 ? D(hb, "hitRadius") : D(hb, "hitRadiusMax");
            if (rad > 0) Set(cd, "radius", rad);
            if (D(hb, "hitRadiusMax") > rad && D(hb, "spreadTime") > 0) { Set(cd, "expandSize", D(hb, "hitRadiusMax")); Set(cd, "expandTime", D(hb, "spreadTime")); }
            Set(r, "childeBulletId", cid); Set(r, "childeDamageId", cid);
            if (CurseOf(ds1, hb) is var hc && hc > 0) AddCurse(cd, hc, w.Notes, $"DS1 Bullet {hb.ID} (explosion)");
            // session 11 (dchr11, rule CU3): the explosion's own hit bullet when it carries curse (the Basilisk's jump & breath 32701 →
            // impact 32702 → lingering curse cloud 32703: 4 s, radius 2.8, registCurse 10 every 1 s) → a third row id + 2 on DS2's own
            // lingering Basilisk cloud 121000640 (DS2's chain 121000410 → 630 → 640); DS1's life, radius and curse amount
            int gHit = I(hb, "HitBulletID");
            if (gHit > 0 && bul[gHit] is { } gb && CurseOf(ds1, gb) is var gc && gc > 0 && bp[CurseCloudTemplate] is { } gT && dmgParam[CurseCloudTemplate] is { } gdT)
            {
                int gid = EnemyRow(behParam, variation, judge, 2);   // rule E-ID1 kind 2
                var gr = pack.AddRow("EnemyBulletParam", gT, gid, $"[Lordran] {chrNote} DS1 Bullet {gb.ID} (lingering cloud of {hb.ID})");
                if (D(gb, "life") > 0) Set(gr, "bulletLife", D(gb, "life"));
                var gd = pack.AddRow("EnemyDamageParam", gdT, gid, $"[Lordran] {chrNote} DS1 Bullet {gb.ID} cloud");
                foreach (var f in new[] { "damage01", "damage02", "damage03", "damageType01", "damageType02", "damageType03", "staminaDamage" }) Set(gd, f, 0);
                double grad = D(gb, "hitRadius") > 0 ? D(gb, "hitRadius") : D(gb, "hitRadiusMax");
                if (grad > 0) Set(gd, "radius", grad);
                AddCurse(gd, gc, w.Notes, $"DS1 Bullet {gb.ID} (cloud, re-hit {D(gb, "dmgHitRecordLifeTime")} s in DS1)");
                Set(cr, "childeBulletId", gid); Set(cr, "childeDamageId", gid);
                if (cr["isChildeExplosion"] != null) Set(cr, "isChildeExplosion", 1);
                w.Notes.Add($"rule CU3: DS1 Bullet {gb.ID} (sfx {I(gb, "sfxId_Bullet")}, life {D(gb, "life")} s, radius {grad}) → EnemyBulletParam {gid} on DS2 {CurseCloudTemplate} (sfx {I(gr, "bulletSfxId")}), child of {cid}");
                w.BulletRows++; w.DamageRows++;
            }
            w.Notes.Add($"DS1 Bullet {b.ID} (sfx {I(b, "sfxId_Bullet")}) → EnemyBulletParam {id} on DS2 {tId} (sfx {I(r, "bulletSfxId")}, DS2 ballistics: gravity {D(r, "gravityValue")}, weight {D(r, "weight")}); explosion DS1 {hb.ID} (sfx {I(hb, "sfxId_Bullet")}, radius {D(hb, "hitRadius")}→{D(hb, "hitRadiusMax")} in {D(hb, "spreadTime")} s) → {cid} radius {rad}, damage {I(cd, "damage01")} type {I(cd, "damageType01")}");
            w.BulletRows++; w.DamageRows++;
        }
        else w.Notes.Add($"DS1 Bullet {b.ID} (sfx {I(b, "sfxId_Bullet")}) → EnemyBulletParam {id} on DS2 {tId} (sfx {I(r, "bulletSfxId")})");
        return (id, id);
    }

    // ------------------------------------------------------------------ session 11 (track dchr11): curse (rules CU1-CU3)
    /// <summary>Rule CU1 anchor: DS1's Basilisk breath (Bullet 32700 → SpEffect 5220 registCurse 20 per hit) against DS2's Basilisk breath
    /// (EnemyDamageParam 121000310: damage01 10, damageType01 12 Petrification): DS2 build-up = DS1 registCurse × 10/20.</summary>
    public const double CurseScale = 10.0 / 20;
    /// <summary>DS2's petrification attribute (DAMAGE_ATTRIBUTE 12: every vanilla Basilisk bullet row; DS2's status for DS1's curse).</summary>
    public const int PetrifyType = 12;
    /// <summary>DS2 Basilisk rows used as templates by CU1 / CU3: its spat petrifying projectile (sfx 1639), its burst (→ 640) and
    /// lingering cloud.</summary>
    public const int CurseBulletTemplate = 121000410, CurseBurstTemplate = 121000630, CurseCloudTemplate = 121000640;

    /// <summary>Rule CU1: a DS1 bullet's curse build-up per hit = Σ SpEffectParam.registCurse over Bullet.spEffectId0-4 and its own
    /// AtkParam_Npc's spEffectId0-4 (DS1 applies both on hit); 0 for every bullet without one.</summary>
    public static double CurseOf(Ds1Source ds1, PARAM.Row bullet)
    {
        var sp = ds1.Params.Params["SpEffectParam"];
        double c = 0;
        IEnumerable<int> Ids(PARAM.Row? r) => r == null ? [] : Enumerable.Range(0, 5).Select(k => r[$"spEffectId{k}"] is { } f ? Convert.ToInt32(f.Value) : 0).Where(x => x > 0);
        foreach (int s in Ids(bullet).Concat(Ids(ds1.Params.Params["AtkParam_Npc"][I(bullet, "atkId_Bullet")])))
            if (sp[s] is { } row && row["registCurse"] is { } f) c += Convert.ToDouble(f.Value);
        return c;
    }

    /// <summary>Rule CU1: DS1 curse build-up → the row's first free damage slot (damage0k = 0) as DS2 petrification (type 12), amount
    /// registCurse × <see cref="CurseScale"/> (DS2's mixed form: Corpse Rat 122600010 physical + petrification 15 in damage02).</summary>
    static void AddCurse(PARAM.Row r, double ds1Curse, List<string> notes, string what)
    {
        int amount = (int)Math.Round(ds1Curse * CurseScale, MidpointRounding.AwayFromZero);
        for (int k = 1; k <= 3; k++)
        {
            if (I(r, $"damage0{k}") != 0) continue;
            Set(r, $"damage0{k}", amount); Set(r, $"damageType0{k}", PetrifyType);
            notes.Add($"rule CU1: {what}: DS1 curse build-up {ds1Curse} per hit → DS2 damage0{k} {amount} type {PetrifyType} (petrification, ×{CurseScale})");
            return;
        }
        notes.Add($"rule CU1: {what}: DS1 curse {ds1Curse} left out (no free damage slot)");
    }

    /// <summary>Session 11 (merge11, boulder WE4 defect 1): the same calibration for any DS2 damage row with damage01..03 /
    /// damageType01..03 / staminaDamage / knockBackPower (SystemDamageParam of a DS1 damaging object): damage × DamageScale,
    /// stamina × StaminaScale, knockback = DS1 knockbackDist; the hit geometry stays the caller's.</summary>
    public static void DamageFromAtk(PARAM.Row ds1Atk, PARAM.Row ds2Row, List<string> notes, string what) => AtkToDamage(ds1Atk, ds2Row, null, notes, what);

    /// <summary>Session 11 (G12 C-AR): the product of SpEffectParam <paramref name="field"/> over the NPC's resident SpEffects
    /// NpcParam.spEffectID0-7 (rows without the field or with 0 count as 1).
    /// Session 11 (track bal11, rules BAL1 / BAL3, <see cref="Ds1Balance"/>): on DS1's Area Scaling SpEffects 7001-7015 only, maxHpRate and
    /// *DiffenceRate fold compressed (1 + c·(rate − 1)) and the four attack rates as the tier's effective attack factor (rate × tier
    /// damage, monotone); every other resident SpEffect and field (stamina damage, regist rates, …) stays 1:1.</summary>
    public static double ResidentRate(Ds1Source ds1, PARAM.Row npc, string field)
    {
        double r = 1;
        var sp = ds1.Params.Params["SpEffectParam"];
        for (int k = 0; k < 8; k++)
            if (npc[$"spEffectID{k}"] is { } c && Convert.ToInt32(c.Value) > 0 && sp[Convert.ToInt32(c.Value)] is { } row && row[field] is { } f && Convert.ToDouble(f.Value) > 0)
            {
                double v = Convert.ToDouble(f.Value);
                if (Ds1Balance.IsTier(row.ID))
                {
                    if (Ds1Balance.IsCompressedField(field)) v = Ds1Balance.Compress(v);                       // BAL1
                    else if (Ds1Balance.IsAttackField(field)) v = Ds1Balance.EffectiveAttack(ds1, row.ID, field);   // BAL3
                }
                r *= v;
            }
        return r;
    }

    /// <summary>Session 11 (bal11): the same product with DS1's raw rates (no BAL1 / BAL3), for reports.</summary>
    public static double RawResidentRate(Ds1Source ds1, PARAM.Row npc, string field)
    {
        double r = 1;
        var sp = ds1.Params.Params["SpEffectParam"];
        for (int k = 0; k < 8; k++)
            if (npc[$"spEffectID{k}"] is { } c && Convert.ToInt32(c.Value) > 0 && sp[Convert.ToInt32(c.Value)] is { } row && row[field] is { } f && Convert.ToDouble(f.Value) > 0)
                r *= Convert.ToDouble(f.Value);
        return r;
    }

    /// <summary>DS1 AtkParam_Npc → DS2 EnemyDamageParam values (see the class comment).</summary>
    /// <param name="pack">session 11 (burgchr11, rule E9c): with a pack, the row's damageCategory becomes a [Lordran] DamageCtrlParam
    /// clone carrying DS1's damage level, attribute, poise damage and guard flag (<see cref="Ds1Calibration.Category"/>)</param>
    /// <param name="bullet">session 12 (bomb12, rule E10f): the DS1 Bullet whose hit this is: its own atkAttribute gives the damage
    /// category's attack type (<see cref="Ds1Calibration.BulletAttribute"/>)</param>
    internal static void AtkToDamage(PARAM.Row a, PARAM.Row r, FLVER2? flver, List<string> notes, string what, double[]? area = null, Pack? pack = null, string family = "", PARAM.Row? bullet = null)
    {
        area ??= [1, 1, 1, 1, 1];
        var parts = new List<(double V, int Type)> { (D(a, "atkPhys") * area[0], 0), (D(a, "atkMag") * area[1], 1), (D(a, "atkThun") * area[3], 2), (D(a, "atkFire") * area[2], 3) }.Where(p => p.V > 0).ToList();
        for (int k = 0; k < 3; k++)
        {
            Set(r, $"damage0{k + 1}", k < parts.Count ? (int)Math.Round(parts[k].V * DamageScale) : 0);
            Set(r, $"damageType0{k + 1}", k < parts.Count ? parts[k].Type : 0);
        }
        Set(r, "staminaDamage", (int)Math.Min(65535, Math.Round(D(a, "atkStam") * area[4] * StaminaScale)));
        if (D(a, "knockbackDist") > 0) Set(r, "knockBackPower", D(a, "knockbackDist") * Ds1Calibration.KnockbackScale);   // rule E9u (burgchr11): the anchor's 0.5 / 0.45
        if (pack != null && r["damageCategory"] is { } dc)
        {
            int? attr = bullet != null && Ds1Calibration.ElementalOnly(a) ? Ds1Calibration.BulletAttribute(bullet, a) : null;   // rule E10f (bomb12)
            int cat = Ds1Calibration.Category(pack, a, Convert.ToInt32(dc.Value), family, attr);
            Set(r, "damageCategory", cat);
            int lv = I(a, "dmgLevel"); double sa = D(a, "atkSuperArmor");
            notes.Add($"{what}: rule E9c damage category {cat} (DS1 dmgLevel {lv} → DS2 damageMotion {Ds1Calibration.MotionOf(lv)}, poise damage {sa}{(attr is int x ? $", rule E10f bullet attack attribute {x} → attackType {Ds1Calibration.AttackTypeOf(x)}" : "")})");
        }
        if (flver != null)
        {
            int d1 = I(a, "hit0_DmyPoly1"), d2 = I(a, "hit0_DmyPoly2");
            double rad = D(a, "hit0_Radius");
            if (d1 >= 0 && d1 <= 255 && rad > 0)
            {
                Set(r, "hitDummyPolyType", 0);
                Set(r, "hitDummyPolyId", d1);
                Set(r, "radius", rad);
                var p1 = Dummy(flver, d1); var p2 = d2 >= 0 ? Dummy(flver, d2) : null;
                if (p1 != null && p2 != null)
                {
                    double len = Vector3.Distance(p1.Value, p2.Value);
                    var fw = flver.Dummies.First(x => x.ReferenceID == d1).Forward;
                    double ang = fw.LengthSquared() > 0 && len > 1e-4 ? Math.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(fw), Vector3.Normalize(p2.Value - p1.Value)), -1, 1)) * 180 / Math.PI : 0;
                    Set(r, "hitModelType", 1); Set(r, "length", len);
                    notes.Add($"{what}: hit capsule DS1 dummy {d1} → {d2} = {len:0.###} m, radius {rad} (dummy {d1}'s forward is {ang:0.#}° off the capsule axis)");
                }
                else { Set(r, "hitModelType", 0); Set(r, "length", 0f); }
            }
        }
        notes.Add($"{what}: DS1 AtkParam_Npc {a.ID} phys {D(a, "atkPhys")} mag {D(a, "atkMag")} fire {D(a, "atkFire")} thun {D(a, "atkThun")} stam {D(a, "atkStam")} → DS2 damage {string.Join("/", parts.Select(p => $"{Math.Round(p.V * DamageScale)}:{p.Type}"))}, stamina {Math.Round(D(a, "atkStam") * StaminaScale)}");
    }
}
