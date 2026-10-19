using SoulsFormats;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// Session 11 (track burgchr11, FIX-PLAN G12 rules E9u / C-HP / C-DEF / C-ST / E9c / NS1): the ONE DS1 → DS2 combat calibration
/// every 1:1 character uses (regulars: <see cref="ChrPorter"/> / <see cref="Ds1AttackParams"/>; bosses: <see cref="Ds1Boss"/>, which
/// had its own constants: damage ×1, stamina ×10, knockback ×2.78, no resident SpEffects).
/// <para><b>Anchor</b> (the first enemy of each game, as before): DS1's Asylum Hollow (NpcParam 250020 HP 44 / 20 souls; the sword
/// Hollow 250022: def_phys 77, def_mag 58, def_fire 54, def_thunder 58, resist 66 / 66 / 66, stamina 50; its slash AtkParam_Npc
/// 250000: atkPhys 100, atkStam 20, knockbackDist 0.45, dmgLevel 1) against DS2's Things Betwixt Undead Traveler (EnemyParam 115006:
/// HP 160, 30 souls, armorDefenseValue 248, magic 15, miracle / pyromancy / dark 30, poison 30, bleeding 10; its dagger slash
/// EnemyDamageParam 111501010: damage01 146, staminaDamage 260, knockBackPower 0.5). Every DS1 value is scaled by its own field's
/// anchor ratio (P: a linear map per field; DS2's damage formula is not DS1's, the in-game protocol is combat-params U1/U7/U8).</para>
/// <list type="bullet">
/// <item><b>E9u</b>: enemy attacks: damage ×146/100, stamina damage ×260/20, knockback ×0.5/0.45 (bosses and regulars alike).</item>
/// <item><b>C-HP</b>: HP = DS1 hp × resident maxHpRate × 160/44 (NG+ cycles keep the template's growth); souls ×30/20.</item>
/// <item><b>C-DEF</b>: armorDefenseValue = DS1 def_phys × resident physicsDiffenceRate × 248/77; magic ×15/58 (DS2's dark = DS1's
/// magic: DS1 has no dark); pyromancy = fire ×30/54; miracle = lightning ×30/58; poison / bleeding = DS1 resist × resident regist
/// rate × 30/66 / 10/66 (DS1's toxic and curse have no anchor: template kept).</item>
/// <item><b>C-ST</b>: staminaPoint = DS1 stamina × resident maxStaminaRate × 1740/50 (DS2's stamina pool against DS2's stamina
/// damage; the anchor's ratio 34.8 is the same order as the stamina-damage ratio 13 × DS2's 2.7× larger guard drain, P).</item>
/// <item><b>E9c</b>: the damage category (DS2 DamageCtrlParam: the player's reaction) per DS1 AtkParam: a clone of the template's
/// category with DS1's damage level (<see cref="MotionOf"/>), physical attribute, poise damage (atkSuperArmor; 999 → 9999) and
/// unblockable flag; no camera / pad rumble (DS1 shakes the camera from TAE events, G9).</item>
/// <item><b>NS1</b>: resident SpEffects: the numeric rates above fold into the params (C-AR); the damage-level remaps
/// (spCategory 1001, <see cref="Remaps"/>): SA-conditional ones (5360, 5364) = DS2 poise from superArmorDurability; an always-on
/// "every level none" (5300) = stunDisableFlag 1 (<see cref="NeverReacts"/>); other always-on ones keep DS2 stun (role tables per character).</item>
/// </list>
/// </summary>
public static class Ds1Calibration
{
    public const double HpScale = 160.0 / 44, SoulScale = 30.0 / 20;
    public const double DamageScale = 146.0 / 100, StaminaScale = 260.0 / 20, KnockbackScale = 0.5 / 0.45;
    public const double PhysDefScale = 248.0 / 77, MagDefScale = 15.0 / 58, FireDefScale = 30.0 / 54, ThunDefScale = 30.0 / 58;
    public const double PoisonScale = 30.0 / 66, BleedScale = 10.0 / 66, StaminaPoolScale = 1740.0 / 50;

    static double D(PARAM.Row r, string f) => Convert.ToDouble(r[f]?.Value ?? 0);
    static void Set(PARAM.Row row, string field, double v)
    {
        var c = row[field] ?? throw new InvalidDataException($"param {row.ID}: no field {field}");
        c.Value = c.Value switch
        {
            float => (float)v, double => v,
            _ => Convert.ChangeType(Math.Round(v), c.Value.GetType()),
        };
    }

    /// <summary>The product of SpEffectParam <paramref name="field"/> over the NPC's resident SpEffects (rule C-AR).</summary>
    public static double Resident(Ds1Source ds1, PARAM.Row npc, string field) => Ds1AttackParams.ResidentRate(ds1, npc, field);

    // ------------------------------------------------------------------ session 11 (track bal11): BALANCE.md option A (Ds1Balance, Mappings\balance.tsv)

    /// <summary>BAL1: the strength c of the area-factor compression (1 + c·(rate − 1) on 7001-7015's maxHpRate / *DiffenceRate).</summary>
    public static double AreaCompression => Ds1Balance.Strength;
    /// <summary>BAL2: getSoul's scale for an NPC with area SpEffect <paramref name="tier"/> (0 = none).</summary>
    public static double TierSoulScale(int tier) => Ds1Balance.SoulScale(tier);
    /// <summary>BAL3: the attack scale of area SpEffect <paramref name="tier"/> after the monotone clamp (1 without a tier).</summary>
    public static double TierDamage(Ds1Source ds1, int tier) => tier == 0 ? 1 : Ds1Balance.AppliedDamage(ds1, tier);
    /// <summary>BAL2: the DS2 souls of a regular / NPC (DS1 NpcParam getSoul × its tier's scale). The ONE soul formula: ChrPorter, NpcPorter,
    /// Ds1Boss extras / large regulars and the event translator's force-death souls (rule N1) must agree.</summary>
    public static int Souls(PARAM.Row npc) => (int)Math.Round(D(npc, "getSoul") * TierSoulScale(Ds1Balance.AreaTier(npc)));
    /// <summary>BAL2: a boss's DS2 souls (DS1 GameAreaParam bonusSoul_single × boss_souls).</summary>
    public static int BossSouls(PARAM.Row gameArea) => (int)Math.Round(D(gameArea, "bonusSoul_single") * Ds1Balance.BossSouls);

    /// <summary>C-HP / C-DEF / C-ST / C-T1 on one EnemyParam row from its DS1 NpcParam; returns a report line.</summary>
    public static string Apply(PARAM.Row ep, Ds1Source ds1, PARAM.Row npc, bool hp = true)
    {
        Ds1Balance.Record(ep.ID, npc.ID);   // bal11: balance-check's manifest
        var notes = new List<string>();
        if (hp)
        {
            double h = D(npc, "hp") * Resident(ds1, npc, "maxHpRate") * HpScale;
            double baseHp = D(ep, "hitPoint");
            foreach (var cell in ep.Cells.Where(c => c.Def.InternalName.StartsWith("hitPoint")))
                cell.Value = Convert.ChangeType(Math.Round(baseHp > 0 ? h * Convert.ToDouble(cell.Value) / baseHp : h), cell.Value.GetType());
            notes.Add($"HP {Math.Round(h)}");
            if (Ds1Balance.AreaTier(npc) is int tier && tier != 0) notes.Add($"rule BAL1: area SpEffect {tier} folded as 1 + {AreaCompression:0.###}·(rate − 1) (hp ×{Resident(ds1, npc, "maxHpRate"):0.###}, DS1 ×{Ds1AttackParams.RawResidentRate(ds1, npc, "maxHpRate"):0.###})");
        }
        foreach (var f in new[] { "damageAdjustRate", "damageAdjustRatePlayer", "damageAdjustRateEnemy", "damageAdjustRateNeutral" })
            if (ep[f] != null) Set(ep, f, 1);
        (string Ds1, string Rate, string Ds2, double Scale)[] defs =
        [
            ("def_phys", "physicsDiffenceRate", "armorDefenseValue", PhysDefScale),
            ("def_mag", "magicDiffenceRate", "armorMagicDefenseValue", MagDefScale),
            ("def_mag", "magicDiffenceRate", "armorDarkMagicDefenseValue", MagDefScale),
            ("def_fire", "fireDiffenceRate", "armorPyromancyDefenseValue", FireDefScale),
            ("def_thunder", "thunderDiffenceRate", "armorMiracleDefenseValue", ThunDefScale),
            ("resist_poison", "registPoizonChangeRate", "armorPoisonDefenseValue", PoisonScale),
            ("resist_blood", "registBloodChangeRate", "armorBleedingDefenseValue", BleedScale),
        ];
        foreach (var (d1, rate, d2, k) in defs)
        {
            if (ep[d2] == null || npc[d1] == null) continue;
            double v = D(npc, d1) * Resident(ds1, npc, rate) * k;
            Set(ep, d2, v);
        }
        notes.Add($"defence phys {ep["armorDefenseValue"]?.Value} mag {ep["armorMagicDefenseValue"]?.Value} fire {ep["armorPyromancyDefenseValue"]?.Value} thun {ep["armorMiracleDefenseValue"]?.Value} poison {ep["armorPoisonDefenseValue"]?.Value} bleed {ep["armorBleedingDefenseValue"]?.Value} (DS1 {D(npc, "def_phys")}/{D(npc, "def_mag")}/{D(npc, "def_fire")}/{D(npc, "def_thunder")}/{D(npc, "resist_poison")}/{D(npc, "resist_blood")} × resident {Resident(ds1, npc, "physicsDiffenceRate"):0.###})");
        // NS1 (poise): DS1's SA-conditional damage-level remaps (SpEffect 5364 / 5360, stateInfo 120: no reaction while the super
        // armour holds) are DS2's poise: poiseValue = DS1 superArmorDurability (×1, as E9c's poise damage = atkSuperArmor; P)
        if (ep["poiseValue"] != null && D(npc, "superArmorDurability") > 0)
        {
            Set(ep, "poiseValue", D(npc, "superArmorDurability"));
            notes.Add($"poise {D(npc, "superArmorDurability")} (DS1 superArmorDurability)");
        }
        if (ep["staminaPoint"] != null && D(npc, "stamina") > 0)
        {
            Set(ep, "staminaPoint", D(npc, "stamina") * Resident(ds1, npc, "maxStaminaRate") * StaminaPoolScale);
            notes.Add($"stamina {ep["staminaPoint"].Value} (DS1 {D(npc, "stamina")})");
        }
        return "rule C-HP/C-DEF/C-ST: " + string.Join(", ", notes);
    }

    // ------------------------------------------------------------------ E9c: per-attack damage category

    /// <summary>DS1 AtkParam dmgLevel (enum order of SpEffectParam's dmgLv_* fields: 0 None, 1 S, 2 M, 3 L, 4 BlowM, 5 Push,
    /// 6 Strike, 7 BlowS, 8 Min, 9 Uppercut, 10 BlowLL, 11 Breath) → DS2 DamageCtrlParam.damageMotion (vanilla survey of 3228 rows by
    /// name: 1 minimal, 2 weak, 3 strong, 4 very strong (oversized strong hits), 5 launch / tackle / strike-away, 6 big launch
    /// (bosses' scoop-ups), 8 fall / slam (plunging and swing-down attacks), 13 throw, 23 no reaction). P per level.</summary>
    public static int MotionOf(int dmgLevel) => dmgLevel switch
    {
        0 => 23, 1 => 2, 2 => 3, 3 => 4, 4 => 5, 5 => 5, 6 => 8, 7 => 5, 8 => 1, 9 => 6, 10 => 5, 11 => 5,
        _ => 3,
    };

    /// <summary>DS1 atkAttribute (0 slash, 1 strike, 2 thrust, 3 standard, 4 none/"standard" of NPC attacks) → DS2 attackType (0 slash,
    /// 1 thrust, 2 strike, 3 none: vanilla row names "Physical Slash / Strike(=thrust) / Strike(blunt)").</summary>
    public static int AttackTypeOf(int atkAttribute) => atkAttribute switch { 0 => 0, 1 => 2, 2 => 1, _ => 3 };

    /// <summary>Session 12 (bomb12, rule E10f): the attack attribute of a DS1 bullet's hit. DS1's Bullet row carries its own
    /// atkAttribute (same enum) next to spAttribute, and the bullet's hit takes it, not the AtkParam_Npc's (P: DS1 param
    /// semantics; 593 of 632 DSR bullets say 4 "none", e.g. the Hollow Warrior's firebomb 25400 and its fire explosion 25402,
    /// whose AtkParam_Npc 25400 says 0 "slash"). Out-of-range values fall back to the AtkParam's. Used only for hits without
    /// physical damage (<see cref="ElementalOnly"/>): DS2's elemental bullets are attackType 3 (its firebomb explosion
    /// 200300120, the player's 99993020, Navlaan's dark bullet 81074500), while DS2's arrows are thrust (543602113) and DS1's
    /// arrows say 3 "standard", which has no DS2 physical counterpart (physical bullets: unchanged, open).</summary>
    public static int BulletAttribute(PARAM.Row bullet, PARAM.Row atk) =>
        bullet["atkAttribute"] is { } c && Convert.ToInt32(c.Value) is int a && a is >= 0 and <= 4 ? a : Convert.ToInt32(atk["atkAttribute"]?.Value ?? 4);

    /// <summary>Rule E10f scope: a DS1 AtkParam with no physical damage and some magic / fire / lightning damage.</summary>
    public static bool ElementalOnly(PARAM.Row atk) => D(atk, "atkPhys") == 0 && (D(atk, "atkMag") > 0 || D(atk, "atkFire") > 0 || D(atk, "atkThun") > 0);

    /// <summary>The [Lordran] DamageCtrlParam row for DS1 AtkParam <paramref name="atk"/> (id 950,000,000 + AtkParam id), cloned from
    /// <paramref name="templateCategory"/> (the DS2 template damage row's); returns its id.</summary>
    /// <param name="attribute">session 12 (bomb12, rule E10f): the DS1 attack attribute when it is not the AtkParam's own (a bullet's
    /// hit, <see cref="BulletAttribute"/>); one that maps to another DS2 attack type gets its own row 960,000,000 + AtkParam id</param>
    public static int Category(Pack pack, PARAM.Row atk, int templateCategory, string family, int? attribute = null)
    {
        int own = Convert.ToInt32(atk["atkAttribute"]?.Value ?? 4), attr = attribute ?? own;
        int id = (AttackTypeOf(attr) != AttackTypeOf(own) ? 960_000_000 : 950_000_000) + atk.ID;
        var p = pack.Param("DamageCtrlParam");
        if (p[id] != null) return id;
        var t = p[templateCategory] ?? p[541401101]!;
        int lv = Convert.ToInt32(atk["dmgLevel"]?.Value ?? 1);
        double sa = D(atk, "atkSuperArmor");
        var r = pack.AddRow("DamageCtrlParam", t, id, $"[Lordran] {family} DS1 AtkParam_Npc {atk.ID} (dmgLevel {lv}, attr {attr}, SA {sa})");
        Set(r, "damageMotion", MotionOf(lv));
        Set(r, "attackType", AttackTypeOf(attr));
        Set(r, "poiseDamage", sa >= 999 ? 9999 : sa);
        if (Convert.ToInt32(atk["disableGuard"]?.Value ?? 0) != 0) Set(r, "guardBreak", 3);   // DS1 unblockable → DS2 "Guard Not Allowed" (vanilla 81060040)
        else if (Convert.ToInt32(r["guardBreak"].Value) == 3) Set(r, "guardBreak", 0);
        foreach (var f in new[] { "cameraRumble", "padRumble" }) if (r[f] != null) Set(r, f, 0);
        if (r["rumbleTime"] != null) Set(r, "rumbleTime", 0);
        return id;
    }

    // ------------------------------------------------------------------ NS1: resident damage-level remaps

    /// <summary>
    /// NS1: DS1's damage-animation-change SpEffects (spCategory 1001; SpEffectParam dmgLv_None … dmgLv_Breath, one value per incoming
    /// DS1 damage level). The values are a reaction enum ordered by severity, read from the effects' names (P): 0 = no change, 1 =
    /// none (5300 "[all invalid]", 5360 "at SA [All without animation]"), 3 = small (5329 "[small only]"), 4 = medium (5333 "[medium
    /// only]"; 5308 "[middle or more]" raises S / M / Min to 4 and keeps L 5 / blows 6-9), 5 = large. stateInfo 120 = only while the
    /// super armour holds (5360, the Black Knight's 5364: blow-aways → 3 under SA) = DS2's poise (Apply: poiseValue); stateInfo 0 =
    /// always. Returns the always-on remaps as (DS1 level → reaction value).
    /// </summary>
    public static Dictionary<int, int> Remaps(Ds1Source ds1, PARAM.Row npc)
    {
        var o = new Dictionary<int, int>();
        string[] lv = ["dmgLv_None", "dmgLv_S", "dmgLv_M", "dmgLv_L", "dmgLv_BlowM", "dmgLv_Push", "dmgLv_Strike", "dmgLv_BlowS", "dmgLv_Min", "dmgLv_Uppercut", "dmgLv_BlowLL", "dmgLv_Breath"];
        var sp = ds1.Params.Params["SpEffectParam"];
        for (int k = 0; k < 8; k++)
            if (npc[$"spEffectID{k}"] is { } c && Convert.ToInt32(c.Value) > 0 && sp[Convert.ToInt32(c.Value)] is { } row && Convert.ToInt32(row["spCategory"]?.Value ?? 0) == 1001
                && Convert.ToInt32(row["stateInfo"]?.Value ?? 0) == 0)
                for (int l = 0; l < lv.Length; l++)
                    if (row[lv[l]] is { } f && Convert.ToInt32(f.Value) is int to && to > 0) o[l] = to;
        return o;
    }

    /// <summary>NS1: a resident (always-on) remap turns every damage level into "none" (value 1): the character never plays a hit
    /// reaction (the Asylum / Stray Demon's 5300) → EnemyParam.stunDisableFlag 1. Any other remap keeps DS2's stun (the character
    /// flinches once its poise breaks, then plays the remapped reaction: Taurus small, Capra / Titanite Demon / Heavy Knight medium).</summary>
    public static bool NeverReacts(Ds1Source ds1, PARAM.Row npc)
    {
        var r = Remaps(ds1, npc);
        return r.Count == 12 && r.Values.All(v => v == 1);
    }
}
