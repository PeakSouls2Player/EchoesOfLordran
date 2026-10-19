using SoulsFormats;
using static Lordran.Tools.Build.ParamCells;

namespace Lordran.Tools.Build;

/// <summary>
/// DS1 weapon special effects → DS2. DS1 attaches SpEffectParam rows to a weapon (residentSpEffectId* while equipped,
/// spEffectBehaviorId* on hit). DS2 keeps item effects as EMEVD scripts in the regulation: SpEffectWeapon.emevd holds one
/// event per item (event id = item id, ItemParam.specialEffectId = that id). The opcodes, decoded with items that exist
/// in both games (Ring of Steel Protection, the Quartz and -bite rings, Chloranthy, Ring of Restoration, Butcher's Knife):
/// <list type="table">
/// <item><term>100090:4 (s32 0, s16 kind, s16 value)</term><description>flat bonus: 0 physical, 1 magic, 2 lightning,
/// 3 fire, 4 dark defense; 5 poison, 6 bleed, 8 curse, 9 petrify resistance. Same units as DS1's *Diffence (Steel
/// Protection: +50 in both games).</description></item>
/// <item><term>100090:1 (s32 0, f32 rate)</term><description>stamina regeneration multiplier (Chloranthy ×1.15).</description></item>
/// <item><term>100050:0 (s32 1, s32 sfx, amount, f32, s32 mode)</term><description>HP restoration: mode 0 = every
/// interval (Ring of Restoration: 3 HP / 1.5 s, s32 amount), mode 1 = on hit (Butcher's Knife: f32 8.0, 0.25).</description></item>
/// <item><term>100130:2 (s32 icon, 0, 0)</term><description>status icon while equipped (20 defense, 30 resistance,
/// 10 stamina, 120 regeneration).</description></item>
/// </list>
/// Bleed/poison/toxic on hit is a weapon stat in DS2 and is handled by WeaponPorter. Not yet mapped (logged):
/// DS1 special states (stateInfo, e.g. Tin Crystallization's 203), the Dark Hand, the Skull Lantern's light,
/// ghost-hitting and dragonslayer bonuses, and projectile R2s.
/// </summary>
public sealed class SpecialsPorter
{
    const string Script = "SpEffectWeapon";
    readonly Ds1Source ds1;
    readonly Pack pack;

    public SpecialsPorter(Ds1Source ds1, Pack pack)
    {
        this.ds1 = ds1;
        this.pack = pack;
    }

    /// <summary>Adds the weapon's DS2 effect event if DS1 gives it any mappable effect; returns a summary or null.</summary>
    public string? Apply(PARAM.Row ds1Weapon, int itemId, PARAM.Row item)
    {
        var effects = ds1.Params.Params["SpEffectParam"];
        PARAM.Row? Effect(string field) => I(ds1Weapon, field) is > 0 and var id ? effects[id] : null;
        var resident = new[] { "residentSpEffectId", "residentSpEffectId1", "residentSpEffectId2" }.Select(Effect).OfType<PARAM.Row>().ToList();
        var onHit = new[] { "spEffectBehaviorId0", "spEffectBehaviorId1", "spEffectBehaviorId2" }.Select(Effect).OfType<PARAM.Row>().ToList();

        var body = new List<EMEVD.Instruction>();
        var notes = new List<string>();
        int icon = 0;

        foreach (var e in resident)
        {
            foreach (var (field, kind, label) in new[] { ("physicsDiffence", 0, "phys"), ("magicDiffence", 1, "magic"), ("thunderDiffence", 2, "lightning"), ("fireDiffence", 3, "fire") })
                if (I(e, field) > 0) { body.Add(Bonus(kind, I(e, field))); notes.Add($"+{I(e, field)} {label} def"); icon = icon == 0 ? 20 : icon; }

            // DS1 multiplies status resistance; DS2 adds a flat amount. The -bite rings anchor the scale: DS1 ×4 = DS2 +15
            // (poison, bleed) / +40 (curse). Toxic folds into poison, as DS2 has no separate toxic resistance on gear.
            double poison = Math.Max(Get(e, "registPoizonChangeRate"), Get(e, "registIllnessChangeRate"));
            foreach (var (rate, kind, full, label) in new[] { (poison, 5, 15, "poison"), (Get(e, "registBloodChangeRate"), 6, 15, "bleed"), (Get(e, "registCurseChangeRate"), 8, 40, "curse") })
                if (rate > 1.001)
                {
                    int value = Math.Max(1, (int)Math.Round((rate - 1) / 3 * full));
                    body.Add(Bonus(kind, value));
                    notes.Add($"+{value} {label} res");
                    icon = icon == 0 ? 30 : icon;
                }

            if (Get(e, "staminaRecoverChangeSpeed") > 0)
            {
                float rate = (float)(1 + Get(e, "staminaRecoverChangeSpeed") / 45.0); // DS1 base recovery is 45/s
                body.Add(new EMEVD.Instruction(100090, 1, Args(w => { w.Write(0); w.Write(rate); })));
                notes.Add($"stamina regen ×{rate:0.00}");
                icon = icon == 0 ? 10 : icon;
            }

            if (Get(e, "changeHpPoint") < 0) // negative damage = healing per tick (1 s in DS1)
            {
                int perTick = (int)Math.Round(-Get(e, "changeHpPoint") * 1.5);
                body.Add(new EMEVD.Instruction(100050, 0, Args(w => { w.Write(1); w.Write(850); w.Write(perTick); w.Write(1.5f); w.Write(0); })));
                notes.Add($"regen {perTick} HP / 1.5 s");
                icon = icon == 0 ? 120 : icon;
            }

            if (I(e, "stateInfo") is not (0 or 50 or 75))
                notes.Add($"UNMAPPED special state {I(e, "stateInfo")} (SpEffect {e.ID})");
        }

        foreach (var e in onHit.Where(e => Get(e, "changeHpPoint") < 0 && Get(e, "registBlood") == 0 && Get(e, "poizonAttackPower") == 0))
        {
            float heal = (float)-Get(e, "changeHpPoint");
            body.Add(new EMEVD.Instruction(100050, 0, Args(w => { w.Write(1); w.Write(852); w.Write(heal); w.Write(0.25f); w.Write(1); })));
            notes.Add($"+{heal} HP on hit");
        }

        if (I(ds1Weapon, "isDarkHand") > 0) notes.Add("UNMAPPED Dark Hand");
        if (I(ds1Weapon, "lanternWep") > 0) notes.Add("UNMAPPED lantern light");
        if (I(ds1Weapon, "isVersusGhostWep") > 0) notes.Add("UNMAPPED hits ghosts");
        if (I(ds1Weapon, "isDragonSlayer") > 0) notes.Add("UNMAPPED dragonslayer");

        if (body.Count == 0)
            return notes.Count > 0 ? string.Join(", ", notes) : null;

        body.Insert(0, new EMEVD.Instruction(100130, 2, Args(w => { w.Write(icon); w.Write(0); w.Write(0); })));
        var evd = pack.RegulationScript(Script);
        evd.Events.RemoveAll(ev => ev.ID == itemId);
        var ev = new EMEVD.Event(itemId);
        ev.Instructions.AddRange(body);
        evd.Events.Add(ev);
        evd.Events.Sort((a, b) => a.ID.CompareTo(b.ID));
        Set(item, "specialEffectId", itemId);
        return string.Join(", ", notes);
    }

    static EMEVD.Instruction Bonus(int kind, int value) =>
        new(100090, 4, Args(w => { w.Write(0); w.Write((short)kind); w.Write((short)value); }));

    static byte[] Args(Action<BinaryWriter> write)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        write(w);
        return ms.ToArray();
    }
}
