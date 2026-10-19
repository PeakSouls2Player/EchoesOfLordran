using Lordran.Tools.Build.Events;
using SoulsFormats;
using static Lordran.Tools.Build.ParamCells;

namespace Lordran.Tools.Build;

/// <summary>
/// Session 10 (asy-items, item i2), rule G9 "RingPorter": a DS1 ring DS2 lacks (item map "new") → a DS2 ring item.
/// <list type="bullet">
/// <item><b>R0 duplicates</b>: a DS1 ring DS2 has under another spelling is DS2's ring (ItemMap.Renamed, e.g. DS1
/// "Cloranthy Ring" = DS2 40020000 "Chloranthy Ring", same effect: stamina recovery), checked at build time against DS2's
/// ring names (itemname.fmg 40M-42M), like ArmorPorter.Ds2Duplicate.</item>
/// <item><b>R1 ids</b>: ItemParam / RingParam / SpEffectRing event = 43,000,000 + DS1 id × 1000 (Rusted Iron Ring 125 →
/// 43,125,000). DS2's rings are 40M-42M (id / 10M = 4, ItemParam.itemTypeParamId 40); 43M-49M is empty in vanilla.</item>
/// <item><b>R2 item</b>: ItemParam + RingParam cloned from a DS2 template ring (weight, durability, repair cost, price,
/// usage = DS2's rules); the template is the DS2 ring that is the evidence for the DS1 ring's main effect (R4), else the
/// Life Ring (the plainest ring). Name / short / long text from DS1 (Accessory_name_ / _description_ / _long_desc_),
/// DS1 icon on the template's icon canvas (WeaponPorter.PortIcon).</item>
/// <item><b>R3 effects</b>: DS2 keeps item effects as EMEVD events in the regulation script SpEffectRing (event id = item
/// id = ItemParam.specialEffectId; docs\ds2-re-notes.md). The DS1 ring's SpEffectParam row (EquipParamAccessory.refId)
/// is translated field by field with the table below; every row of the table is anchored on DS2 rings whose text
/// states the same effect (V-data: their SpEffectRing events, `probe-speffect SpEffectRing`). No new effect ids: the
/// event id is the ring's item id, and the only effect (SFX) ids written are the ones copied from vanilla events
/// (all &lt; 30000, rule B2b-id).</item>
/// <item><b>R4 hard-coded DS1 states</b> (SpEffectParam.stateInfo, behaviour inside the DS1 exe): a state whose DS2
/// counterpart is a whole DS2 ring with the same described effect copies that ring's event (and uses it as the R2
/// template). Everything else is ported without an effect and logged "UNMAPPED" (text and icon still DS1's).</item>
/// <item><b>R3b one unit</b> (fix pass): a ring whose benefit is UNMAPPED keeps none of its drawbacks (max HP / stamina /
/// load ×r &lt; 1, damage taken ×r &gt; 1): Dusk Crown Ring (state 194 +50% casts UNMAPPED, max HP ×0.5) is ported
/// without an effect instead of as a pure penalty. A pure drawback ring (Calamity Ring: damage ×2, its state handled)
/// keeps it.</item>
/// <item><b>R5 event-tested rings</b> (fix pass): DS1 events test a ring by its SpEffect (`4:5 IF Character Has
/// SpEffect(10000, refId)`: Covenant of Artorias 2200 in the Abyss, m16_00; Ring of Sacrifice 2130/2131 "ring broke",
/// common 758/759; covenant rings m12_01/m15_01). The census (<see cref="EventTests"/>, common + every DS1 map) is logged
/// per ported ring; the EMEVD translator turns such a test on the player into DS2's equipped-item test
/// `f131403(ring) &gt; 0` / `== 0` (EmevdTranslator case (4, 5), IContext.RingItem = <see cref="ItemForSpEffect"/>); a
/// test on another character is UNMAPPED (DS2 has no equipment test on other characters). `ring-census` prints it.</item>
/// </list>
/// Generalises to all 41 DS1 rings (29 "new"): the porter reads only DS1 data; `--rings all` ports every "new" ring,
/// otherwise rings are ported on demand when a lot, treasure or trade needs one (Ds1Drops.MapItem). What a ring does is
/// either SpEffect fields (R3/R4, the rest logged UNMAPPED per ring) or DS1 event logic (R5); the DS1 exe's own
/// hard-coded uses of an accessory id (none known besides stateInfo) and talk-ESD tests are not covered (U).
/// </summary>
public static class RingPorter
{
    public const string Script = "SpEffectRing";
    const int LifeRing = 40010000;

    public static int Id(int ds1Id) => 43_000_000 + ds1Id * 1000;

    // R4: DS1 stateInfo → the DS2 ring whose event is copied (V-data: texts)
    //   159 Ring of Sacrifice "Lose nothing upon death, but ring breaks" → 40310000 Ring of Life Protection "Nullifies
    //       death, but breaks" (1000:3 kind 2 + 100110:3 + icon 80)
    //   160 Rare Ring of Sacrifice "Die, lose nothing, nullify curse, ring breaks" → 40300000 Ring of Soul Protection
    //       "Nullifies death and petrification, but breaks" (1000:3 kind 3; DS2's curse status is petrification)
    //    54 Slumbering Dragoncrest "Wearer produces no sound" → 40460000 Slumbering Dragoncrest Ring "Masks the sounds"
    static readonly Dictionary<int, (int Ring, string Why)> StateRings = new()
    {
        [159] = (40310000, "Ring of Life Protection: nullifies death, breaks"),
        [160] = (40300000, "Ring of Soul Protection: nullifies death and petrification (DS2 curse), breaks"),
        [54] = (40460000, "Slumbering Dragoncrest Ring: masks sounds"),
    };

    /// <summary>The DS2 ring that is the same item as the DS1 ring under another spelling (rule R0), or 0.</summary>
    public static int Ds2Duplicate(Pack pack, Ds1Source ds1, int ds1Id)
    {
        string name = ds1.Text("Accessory_name_", ds1Id);
        string ds2Name = Commands.ItemMap.RenamedName(name);
        if (string.Equals(ds2Name, name, StringComparison.OrdinalIgnoreCase)) return 0;
        var e = pack.Text("itemname").Entries.FirstOrDefault(x => x.ID is >= 40_000_000 and < 43_000_000 && string.Equals(x.Text, ds2Name, StringComparison.OrdinalIgnoreCase));
        return e?.ID ?? 0;
    }

    /// <summary>Ports the DS1 ring once per pack (or returns DS2's duplicate); returns the DS2 item id.</summary>
    public static int Port(Pack pack, Ds1Source ds1, int ds1Id)
    {
        if (Ds2Duplicate(pack, ds1, ds1Id) is int dup and > 0)
        {
            pack.Note($"ring {ds1Id} {ds1.Text("Accessory_name_", ds1Id)} = DS2 ring {dup} (rule R0, renamed)");
            return dup;
        }
        int id = Id(ds1Id);
        var items = pack.Param("ItemParam");
        if (items[id] != null) return id; // already ported in this pack
        var acc = ds1.Params.Params["EquipParamAccessory"][ds1Id] ?? throw new InvalidDataException($"DS1 ring {ds1Id} not found");
        string name = ds1.Text("Accessory_name_", ds1Id);
        var effect = I(acc, "refId") is > 0 and var r ? ds1.Params.Params["SpEffectParam"][r] : null;

        var notes = new List<string>();
        List<EMEVD.Instruction> body = effect == null ? new() : Translate(pack, ds1, acc, effect, notes, out _) ?? new();
        int template = LifeRing;
        if (effect != null && I(effect, "stateInfo") is var st && StateRings.TryGetValue(st, out var sr)) template = sr.Ring;

        var tItem = pack.Param("ItemParam")[template]!;
        var item = pack.AddRow("ItemParam", tItem, id, $"[Lordran] {name}");
        Set(item, "iconId", id);
        Set(item, "ringParamId", id);
        Set(item, "specialEffectId", body.Count > 0 ? id : -1);
        Set(item, "sortId", 1000 + ds1Id); // after DS2's rings (sortId ≤ 840), in DS1 order
        pack.AddRow("RingParam", pack.Param("RingParam")[template]!, id, $"[Lordran] {name}");
        WeaponPorter.PortIcon(ds1, pack, I(acc, "iconId"), id, template);
        pack.SetText("itemname", id, name);
        pack.SetText("simpleexplanation", id, ds1.Text("Accessory_description_", ds1Id));
        pack.SetText("detailedexplanation", id, ds1.LoreText("Accessory_long_desc_", ds1Id));

        if (body.Count > 0)
        {
            var evd = pack.RegulationScript(Script);
            evd.Events.RemoveAll(ev => ev.ID == id);
            var ev = new EMEVD.Event(id);
            ev.Instructions.AddRange(body);
            evd.Events.Add(ev);
            evd.Events.Sort((a, b) => a.ID.CompareTo(b.ID));
        }
        if (I(acc, "isEquipOutBrake") != 0) notes.Add("UNMAPPED breaks when removed (no DS2 ring breaks on unequip)");
        // R5: DS1 events that test this ring (its SpEffect) translate to DS2's equipped-item test on this item
        if (EventTests(ds1).TryGetValue(ds1Id, out var tests))
        {
            var onPlayer = tests.Where(t => t.Player).GroupBy(t => t.Map).Select(g => $"{g.Key} ×{g.Count()}").ToList();
            int other = tests.Count(t => !t.Player);
            notes.Add($"event-tested (rule R5, 4:5 → f131403({id}) > 0): " + (onPlayer.Count > 0 ? string.Join(", ", onPlayer) : "none on the player")
                + (other > 0 ? $"; UNMAPPED ×{other} on other characters ({string.Join(", ", tests.Where(t => !t.Player).Select(t => $"{t.Map} {t.Event}: {(t.Entity < 0 ? "unresolved" : t.Entity)}").Distinct())})" : ""));
        }
        pack.Note($"ring {ds1Id} {name} → item {id} (template {template}): {(notes.Count > 0 ? string.Join(", ", notes) : "no effect")}");
        return id;
    }

    /// <summary>R3/R4: the DS1 SpEffect row as SpEffectRing instructions; notes say what each one came from.</summary>
    internal static List<EMEVD.Instruction>? Translate(Pack pack, Ds1Source ds1, PARAM.Row acc, PARAM.Row e, List<string> notes, out int? evidence)
    {
        evidence = null;
        var body = new List<EMEVD.Instruction>();
        int icon = 0;
        int state = I(e, "stateInfo");
        // R3b (fix pass): the ring is one unit: drawbacks are dropped when a benefit can't be ported
        var penalties = new List<(EMEVD.Instruction Ins, string Note)>();
        var unmappedBenefits = new List<string>();
        void Benefit(string note) { notes.Add(note); unmappedBenefits.Add(note); }

        // R4: a whole DS2 ring with the same described effect
        if (StateRings.TryGetValue(state, out var sr))
        {
            var src = pack.RegulationScript(Script).Events.First(x => x.ID == sr.Ring);
            foreach (var x in src.Instructions) body.Add(new EMEVD.Instruction(x.Bank, x.ID, x.ArgData.ToArray()));
            notes.Add($"state {state} = DS2 {sr.Ring} {sr.Why}");
            evidence = sr.Ring;
            state = 0; // handled
        }

        // flat defense (Ring of Steel Protection: DS1 physicsDiffence 50 = DS2 100090:4 kind 0 +50, shared ring)
        foreach (var (field, kind, label) in new[] { ("physicsDiffence", 0, "phys"), ("magicDiffence", 1, "magic"), ("thunderDiffence", 2, "lightning"), ("fireDiffence", 3, "fire") })
            if (I(e, field) > 0) { body.Add(Bonus(kind, I(e, field))); notes.Add($"+{I(e, field)} {label} def"); icon = icon == 0 ? 20 : icon; }

        // status resistance (-bite rings, shared: DS1 ×4 = DS2 +15 poison/bleed, +40 curse; SpecialsPorter's rule)
        double poison = Math.Max(Get(e, "registPoizonChangeRate"), Get(e, "registIllnessChangeRate"));
        foreach (var (rate, kind, full, label) in new[] { (poison, 5, 15, "poison"), (Get(e, "registBloodChangeRate"), 6, 15, "bleed"), (Get(e, "registCurseChangeRate"), 8, 40, "curse") })
            if (rate > 1.001)
            {
                int value = Math.Max(1, (int)Math.Round((rate - 1) / 3 * full));
                body.Add(Bonus(kind, value));
                notes.Add($"+{value} {label} res");
                icon = icon == 0 ? 30 : icon;
            }

        // stamina regeneration (Chloranthy, shared; SpecialsPorter's rule, DS1 base 45/s)
        if (Get(e, "staminaRecoverChangeSpeed") > 0)
        {
            float rate = (float)(1 + Get(e, "staminaRecoverChangeSpeed") / 45.0);
            body.Add(new EMEVD.Instruction(100090, 1, Args(w => { w.Write(0); w.Write(rate); })));
            notes.Add($"stamina regen ×{rate:0.00}");
            icon = icon == 0 ? 10 : icon;
        }

        // max HP / stamina / equip load multipliers: 1000:1 kind 7 / 8 / 9 (V-data: Life Ring "Raises maximum HP" = kind 7
        // only, Royal Soldier's Ring "Increases maximum load" = kind 9 only, First Dragon Ring "Increases HP, stamina, and
        // maximum load" = kinds 7, 8, 9)
        foreach (var (field, kind, label) in new[] { ("maxHpRate", 7, "max HP"), ("maxStaminaRate", 8, "max stamina"), ("equipWeightChangeRate", 9, "max load") })
            if (Math.Abs(Get(e, field) - 1) > 0.001)
            {
                var ins = Rate(kind, (float)Get(e, field));
                string n = $"{label} ×{Get(e, field):0.##}";
                body.Add(ins);
                notes.Add(n);
                if (Get(e, field) < 1) penalties.Add((ins, n));
            }

        // attunement slots: 1000:0 kind 0x14 +n (V-data: Southern Ritual Band "Allows attunement of additional spells" +1/2/3)
        if (I(e, "changeMagicSlot") > 0) { body.Add(Flat(0x14, I(e, "changeMagicSlot"))); notes.Add($"+{I(e, "changeMagicSlot")} attunement slot"); }

        // poise: 1000:0 kind 0 +n (V-data: Ring of Giants "Increases poise" +10/20/30, icon 70); DS1's poise value as is (P)
        if (Get(e, "changeSuperArmorPoint") > 0)
        {
            body.Add(Flat(0, (int)Get(e, "changeSuperArmorPoint")));
            notes.Add($"+{Get(e, "changeSuperArmorPoint")} poise");
            icon = icon == 0 ? 70 : icon;
            if (state == 155) state = 0; // DS1 Wolf Ring's poise state = the value itself
        }

        // detection by sight: 1000:8 f32 factor (V-data: Redeye Ring "Easier to be detected by enemies" = 2.0); DS1
        // sightSearchEnemyCut is the percent cut (Ring of Fog 70 → ×0.3). DS1's transparency (state 8) has no DS2 ring.
        if (Get(e, "sightSearchEnemyCut") > 0)
        {
            float f = (float)Math.Max(0, (100 - Get(e, "sightSearchEnemyCut")) / 100);
            body.Add(new EMEVD.Instruction(1000, 8, Args(w => w.Write(f))));
            notes.Add($"sight detection ×{f:0.##}");
            if (state == 8) { Benefit("UNMAPPED state 8 (transparent body)"); state = 0; }
        }

        // damage taken: all DS1 damage-cut rates equal r → 1000:1 kind 0x1A ×r (P: Ring of the Embedded "take increased
        // damage" ×1.085; Calamity Ring ×2)
        var cuts = new[] { "slashDamageCutRate", "blowDamageCutRate", "thrustDamageCutRate", "neutralDamageCutRate", "magicDamageCutRate", "fireDamageCutRate", "thunderDamageCutRate" }.Select(f => Get(e, f)).ToList();
        if (cuts.All(c => Math.Abs(c - cuts[0]) < 0.001) && Math.Abs(cuts[0] - 1) > 0.001)
        {
            var ins = Rate(0x1A, (float)cuts[0]);
            string n = $"damage taken ×{cuts[0]:0.##}";
            body.Add(ins);
            notes.Add(n);
            if (cuts[0] > 1) penalties.Add((ins, n));
            if (state == 237) state = 0;
        }

        // thrust counter attacks: DS1 state 197 + physicsAttackRate → 1000:1 kind 0x0B (V-data: Old Leo Ring "Strengthens
        // thrust weapon counter attacks" ×1.125; DS1 Leo Ring "Boosts counter attacks of thrust weapons" ×1.4)
        if (state == 197 && Math.Abs(Get(e, "physicsAttackRate") - 1) > 0.001)
        {
            body.Add(Rate(0x0B, (float)Get(e, "physicsAttackRate")));
            notes.Add($"thrust counter ×{Get(e, "physicsAttackRate"):0.##}");
            state = 0;
        }

        // HP regeneration per tick (SpecialsPorter's rule, Ring of Restoration)
        if (Get(e, "changeHpPoint") < 0)
        {
            int perTick = (int)Math.Round(-Get(e, "changeHpPoint") * 1.5);
            body.Add(new EMEVD.Instruction(100050, 0, Args(w => { w.Write(1); w.Write(850); w.Write(perTick); w.Write(1.5f); w.Write(0); })));
            notes.Add($"regen {perTick} HP / 1.5 s");
            icon = icon == 0 ? 120 : icon;
        }

        // R4 leftovers and DS1 mechanics without a DS2 ring (logged; the ring keeps its DS1 text)
        if (state is not (0 or 50 or 75)) Benefit($"UNMAPPED state {state} (SpEffect {e.ID})");
        if (I(e, "spCategory") > 0 && body.Count == 0)
        {
            // a DS1 "category override" ring (Rusted Iron Ring: category 1000 priority 3): while equipped, DS1's SpEffects of
            // the same category with a worse priority (larger number) don't apply. Those are terrain effects (bog/water);
            // the override goes with the terrain rule that ports them.
            int cat = I(e, "spCategory"), prio = I(e, "categoryPriority");
            var beaten = ds1.Params.Params["SpEffectParam"].Rows.Where(x => x.ID != e.ID && I(x, "spCategory") == cat && I(x, "categoryPriority") > prio).Select(x => x.ID).ToList();
            Benefit($"UNMAPPED category {cat} priority {prio} override: cancels DS1 SpEffects {string.Join("/", beaten)} (terrain; rule goes with their port)");
        }
        if (I(e, "throwCondition") > 0) Benefit($"UNMAPPED critical bonus (throwCondition {I(e, "throwCondition")})");
        if (Get(e, "maxDurability") > 0) Benefit($"UNMAPPED weapon durability (maxDurability {Get(e, "maxDurability")})");
        foreach (string f in new[] { "physicsAttackPowerRate", "magicAttackPowerRate", "fireAttackPowerRate", "thunderAttackPowerRate", "physicsDiffenceRate", "magicDiffenceRate", "fireDiffenceRate", "thunderDiffenceRate" })
            if (Math.Abs(Get(e, f) - 1) > 0.001)
            {
                string n = $"UNMAPPED {f} ×{Get(e, f):0.##}";
                if (Get(e, f) > 1) Benefit(n); else notes.Add(n); // a rate below 1 is a drawback (nothing to drop)
                break;
            }
        if (I(e, "fireAttackPower") > 0) Benefit("UNMAPPED fire attack power");

        // R3b: a ring with an UNMAPPED benefit keeps none of its drawbacks (no pure-penalty ring under DS1's text)
        if (unmappedBenefits.Count > 0)
            foreach (var (ins, n) in penalties)
            {
                body.Remove(ins);
                notes.Remove(n);
                notes.Add($"drawback {n} left out (rule R3b: its benefit is UNMAPPED)");
            }

        if (body.Count > 0 && evidence == null && icon > 0)
            body.Insert(0, new EMEVD.Instruction(100130, 2, Args(w => { w.Write(icon); w.Write(0); w.Write(0); })));
        return body;
    }

    // ---------------------------------------------------------------- R5: rings tested by DS1 events
    /// <summary>One DS1 `4:5 IF Character Has SpEffect` on a ring's SpEffect: map (file stem), event, instruction, the
    /// tested character (10000 = the player; -1 = a parameter no Initialize Event resolves), should-have.</summary>
    public sealed record EventTest(string Map, long Event, int Index, int Entity, bool Has)
    {
        public bool Player => Entity == 10000;
    }

    static readonly Dictionary<string, Dictionary<int, List<int>>> ringsBySp = [];
    static readonly Dictionary<string, (Dictionary<int, List<EventTest>> Tests, List<string> Unresolved)> census = [];

    /// <summary>DS1 SpEffect id → the DS1 rings (EquipParamAccessory rows) whose refId it is.</summary>
    public static Dictionary<int, List<int>> RingsBySpEffect(Ds1Source ds1)
    {
        if (ringsBySp.TryGetValue(ds1.Root, out var d)) return d;
        d = ds1.Params.Params["EquipParamAccessory"].Rows.Where(r => I(r, "refId") > 0).GroupBy(r => I(r, "refId"))
            .ToDictionary(g => g.Key, g => g.Select(r => r.ID).ToList());
        return ringsBySp[ds1.Root] = d;
    }

    /// <summary>R5 census over DS1's common.emevd and every map script: DS1 ring id → its 4:5 tests (a parameterised
    /// entity is resolved through every Initialize Event 2000:0/2000:6 of that event, over all scripts).</summary>
    public static Dictionary<int, List<EventTest>> EventTests(Ds1Source ds1) => Census(ds1).Tests;

    /// <summary>4:5 instructions whose SpEffect argument is itself a parameter (the census can't tell if it is a ring's).</summary>
    public static List<string> UnresolvedEventTests(Ds1Source ds1) => Census(ds1).Unresolved;

    static (Dictionary<int, List<EventTest>> Tests, List<string> Unresolved) Census(Ds1Source ds1)
    {
        if (census.TryGetValue(ds1.Root, out var c)) return c;
        var bySp = RingsBySpEffect(ds1);
        var defs = Commands.EmevdDump.Defs();
        var files = Directory.GetFiles(Path.Combine(ds1.Root, "event"), "*.emevd.dcx").Order()
            .Select(f => (Map: Path.GetFileName(f).Split('.')[0], Evd: EMEVD.Read(f))).ToList();
        // Initialize Event (2000:0 slot, event, args… / 2000:6 common: same layout) argument blocks per event id
        var inits = files.SelectMany(f => f.Evd.Events).SelectMany(e => e.Instructions)
            .Where(x => x.Bank == 2000 && x.ID is 0 or 6 && x.ArgData.Length >= 8)
            .GroupBy(x => (long)BitConverter.ToUInt32(x.ArgData, 4)).ToDictionary(g => g.Key, g => g.Select(x => x.ArgData).ToList());
        var tests = new Dictionary<int, List<EventTest>>();
        var unresolved = new List<string>();
        foreach (var (map, evd) in files)
            foreach (var ev in evd.Events)
                for (int i = 0; i < ev.Instructions.Count; i++)
                {
                    var x = ev.Instructions[i];
                    if (x is not { Bank: 4, ID: 5 }) continue;
                    var v = EmevdTranslator.Decode(defs, x);
                    var pars = ev.Parameters.Where(p => p.InstructionIndex == i).ToList();
                    if (pars.Any(p => p.TargetStartByte == 8)) { unresolved.Add($"{map} {ev.ID} [{i}]"); continue; }
                    if (!bySp.TryGetValue((int)v[2], out var rings)) continue;
                    var entities = new List<int>();
                    if (pars.FirstOrDefault(p => p.TargetStartByte == 4) is { } ep)
                    {
                        foreach (var a in inits.GetValueOrDefault(ev.ID) ?? [])
                            if (8 + ep.SourceStartByte + 4 <= a.Length) entities.Add(BitConverter.ToInt32(a, 8 + (int)ep.SourceStartByte));
                        if (entities.Count == 0) entities.Add(-1);
                    }
                    else entities.Add((int)v[1]);
                    foreach (int ring in rings)
                        foreach (int ent in entities.Distinct())
                        {
                            if (!tests.TryGetValue(ring, out var l)) tests[ring] = l = [];
                            l.Add(new EventTest(map, ev.ID, i, ent, v[3] != 0));
                        }
                }
        return census[ds1.Root] = (tests, unresolved);
    }

    /// <summary>R5: the DS2 ring item that stands for the DS1 ring whose SpEffect this is (DS2's own ring through the item
    /// map, or the ported ring, ported on demand), or null (not a ring's SpEffect, shared by several rings, or no DS2 item).
    /// With <paramref name="pack"/> null (translator report): the item map's DS2 ring, or R1's id for a "new" ring.</summary>
    public static int? ItemForSpEffect(Pack? pack, Ds1Source ds1, int sp)
    {
        if (sp <= 0 || !RingsBySpEffect(ds1).TryGetValue(sp, out var rings) || rings.Count != 1) return null;
        int ds1Id = rings[0];
        if (pack != null)
        {
            int id = Chr.Ds1Drops.MapItem(pack, ds1, 0x20000000, ds1Id, out _, out _, out _);
            return id > 0 ? id : null;
        }
        return Chr.Ds1Drops.Mapping("EquipParamAccessory", ds1Id) is { } m ? m.Ds2 > 0 ? m.Ds2 : m.Result == "new" ? Id(ds1Id) : null : null;
    }

    static EMEVD.Instruction Bonus(int kind, int value) =>
        new(100090, 4, Args(w => { w.Write(0); w.Write((short)kind); w.Write((short)value); }));

    static EMEVD.Instruction Rate(int kind, float rate) =>
        new(1000, 1, Args(w => { w.Write(kind); w.Write(0); w.Write(rate); }));

    static EMEVD.Instruction Flat(int kind, int value) =>
        new(1000, 0, Args(w => { w.Write(kind); w.Write(0); w.Write(value); }));

    static byte[] Args(Action<BinaryWriter> write)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        write(w);
        return ms.ToArray();
    }
}
