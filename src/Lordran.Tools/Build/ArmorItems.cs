using Lordran.Tools.Build.Chr;
using Lordran.Tools.Formats;
using SoulsFormats;
using static Lordran.Tools.Build.ParamCells;

namespace Lordran.Tools.Build;

/// <summary>
/// Session 11 (track equip11, PLAN milestone 7): DS1-only armour as DS2 armour items. <see cref="ArmorPorter"/> (track npc,
/// session 8) already makes a DS1 piece's DS2 part models (both sexes, DS2 player rig, unique texture names), ItemParam /
/// ArmorParam / ArmorReinforceParam rows (copies of the nearest shared piece), DS1 icon, name and description. This
/// finishes them as items under DS2's rules (rules AR1-AR7, docs\translation-rules.md):
/// <list type="bullet">
/// <item><b>AR1 which pieces</b>: every EquipParamProtector base row the item map calls "new" (197), except pieces DS2 has under
/// another name (ArmorPorter.Ds2Duplicate: those resolve to DS2's piece) and the 14 rows DS1 itself never hands out (sortId -1,
/// isDeposit 0: the bare-body Head/Body/Arms/Legs 900000-903000, "No Travel Gear" 950000-953000, the Dragon form 1000000-1003000
/// and the Egg head 1010000/1020000, transformation parts, <see cref="IsItem"/>). <c>build-pack --armor all|ID+ID</c>.</item>
/// <item><b>AR2 calibration</b> (like WeaponPorter): every number is DS1's, times the median DS2/DS1 ratio over the pieces
/// both games share (item map "shared", same slot), compared at full upgrade (DS1 base × ReinforceParamProtector at the
/// type's last level; DS2 max*): physical, magic, fire, lightning; DS2 dark from DS1 magic; slash/strike/thrust as DS2
/// physical × (1 + k·DS1 modifier/100), k fitted on the shared pieces; poison from DS1 poison/toxic, bleed from bleed,
/// DS2 petrification and curse from DS1 curse (each its own ratio; 0 when no shared piece has it); poise (DS2 "strong")
/// from DS1 saDurability; weight, durability; price and sale price from DS1 sellValue.</item>
/// <item><b>AR3 upgrades</b> (DS2's system): DS1 "Normal" +10 (reinforceType 0) → DS2 +10 titanite (the ArmorPorter template's
/// cost row if it is a +10 row, else the slot's most common shared +10 row); DS1 "Unique" +5 (100) → DS2 +5 twinkling (the
/// slot's most common vanilla +5 twinkling row); DS1 pieces that can't be reinforced (materialSetId -1, the boss sets) → DS2
/// +5 twinkling too (every DS2 player piece is upgradable; WeaponPorter's rule for non-upgradable weapons).
/// +0 = calibrated full-upgrade value / the DS2 track's median growth (max/min over vanilla rows of that track).</item>
/// <item><b>AR4 hidden parts</b>: EquipInterfereParam (what the piece hides of the body, face and hair) is the shared DS2 piece's
/// whose DS1 counterpart has the nearest DS1 invisibleFlag00-47 set (same slot); vanilla rows are pointed at, never
/// changed.</item>
/// <item><b>AR5 effects</b>: DS1 residentSpEffectId 1-3 (except 6300 "Super Armor" and the field-less 62xx rows every heavy
/// piece carries) go through RingPorter's SpEffect table into a SpEffectArmor event (id = item id, ItemParam
/// specialEffectId): Mask of the Father max load ×1.05, Mother max HP ×1.1, Child stamina regeneration. The rest
/// (Thorns' roll damage, Crowns' attack rates, Sunlight Maggot's light) is logged UNMAPPED.</item>
/// <item><b>AR6 text</b>: DS1 name and long description (DS2 armour has no short description); DS1 icon.</item>
/// <item><b>AR7 sort</b>: ItemParam sortId 3000 + DS1 sortId (after DS2's pieces, DS1 sets together in DS1 order).</item>
/// </list>
/// Calibration runs over every DS1 armour item in the pack (<see cref="Finish"/>), so the NPC outfits' pieces get the
/// same numbers whether or not --armor is given.
/// </summary>
public static class ArmorItems
{
    public const string Script = "SpEffectArmor";

    /// <summary>The "new" DS1 armour base ids of the item map.</summary>
    public static List<int> NewIds() => File.ReadLines(Commands.ItemMap.DefaultOut).Skip(1).Select(l => l.Split('\t'))
        .Where(c => c[0] == "EquipParamProtector" && c[4] == "new").Select(c => int.Parse(c[1])).ToList();

    /// <summary>AR1: whether DS1 hands the piece out as an item (sortId -1 + isDeposit 0 = body/transformation parts).</summary>
    public static bool IsItem(Ds1Source ds1, int ds1Id) => ds1.Params.Params["EquipParamProtector"][ds1Id] is { } r && !(I(r, "sortId") < 0 && I(r, "isDeposit") == 0);

    /// <summary>A ported armour item id (ArmorPorter.ItemId) → its DS1 base id, or 0.</summary>
    public static int Ds1Id(int itemId)
    {
        if (itemId is < 24_000_000 or >= 25_000_000 || itemId % 1000 / 100 != 1 || itemId % 100 > 3) return 0;
        int model = (itemId - 20_000_000) / 1000;
        return (model - 4000) * 10000 + itemId % 10 * 1000;
    }

    /// <summary>Ports the given DS1 pieces (rule AR1); returns item ids and failures.</summary>
    public static (List<(int Ds1, int Item, string How)> Done, List<string> Failed) PortAll(Pack pack, Ds1Source ds1, ArmorPorter armour, IEnumerable<int> ids)
    {
        var done = new List<(int, int, string)>();
        var failed = new List<string>();
        foreach (int id in ids.Select(i => i / 1000 * 1000).Distinct())
        {
            if (!IsItem(ds1, id)) { pack.Note($"armour {id} {ds1.Text("Armor_name_", id)}: not an item in DS1 (sortId -1, isDeposit 0), skipped (rule AR1)"); continue; }
            try
            {
                var (item, how) = armour.Resolve(id);
                done.Add((id, item, how));
            }
            catch (Exception e)
            {
                failed.Add($"{id} {ds1.Text("Armor_name_", id)}: {e.GetType().Name}: {e.Message}");
                pack.Note($"FAILED armour {id}: {e}");
            }
        }
        return (done, failed);
    }

    sealed record Pair(PARAM.Row Ds1, PARAM.Row Item, PARAM.Row Armor, PARAM.Row Reinforce);

    static readonly string[] Ds2Phys = ["Slash", "Strike", "Thrust"];
    static readonly string[] Ds1Phys = ["defenseSlash", "defenseBlow", "defenseThrust"];

    /// <summary>Rules AR2-AR7 over every ported DS1 armour item in the pack. Returns one report line per piece.</summary>
    public static List<string> Finish(Pack pack, Ds1Source ds1, ParamStore ds2)
    {
        var report = new List<string>();
        var items = pack.Param("ItemParam");
        var mine = items.Rows.Where(r => Ds1Id(r.ID) > 0 && I(r, "armorParamId") > 0).ToList();
        if (mine.Count == 0 && !pack.Param("ArmorParam").Rows.Any(a => a.ID is >= 14_000_000 and < 14_500_000 && ds2.Params["ArmorParam"][a.ID] == null)) return report;
        var protector = ds1.Params.Params["EquipParamProtector"];
        var shared = File.ReadLines(Commands.ItemMap.DefaultOut).Skip(1).Select(l => l.Split('\t'))
            .Where(c => c[0] == "EquipParamProtector" && c[4] == "shared" && c[5].Length > 0)
            .Select(c =>
            {
                var d1 = protector[int.Parse(c[1])];
                var it = ds2.Params["ItemParam"][int.Parse(c[5])];
                var ar = it == null ? null : ds2.Params["ArmorParam"][I(it, "armorParamId")];
                var rf = ar == null ? null : ds2.Params["ArmorReinforceParam"][I(ar, "armorReinforceId")];
                return d1 == null || it == null || ar == null || rf == null ? null : new Pair(d1, it, ar, rf);
            }).OfType<Pair>().ToList();
        var rates = ds1.Params.Params["ReinforceParamProtector"];
        PARAM.Row? MaxRate(PARAM.Row d1)
        {
            if (I(d1, "materialSetId") < 0) return null; // DS1 can't reinforce it: its full strength is +0
            int t = I(d1, "reinforceTypeId");
            return Enumerable.Range(0, 16).Select(k => rates[t + 15 - k]).FirstOrDefault(r => r != null);
        }
        double D1Max(PARAM.Row d1, string field, string rate) => Get(d1, field) * (MaxRate(d1) is { } r ? Get(r, rate) : 1);
        double D1Phys(PARAM.Row d1) => D1Max(d1, "defensePhysics", "physicsDefRate");
        double D1Magic(PARAM.Row d1) => D1Max(d1, "defenseMagic", "magicDefRate");
        double D1Fire(PARAM.Row d1) => D1Max(d1, "defenseFire", "fireDefRate");
        double D1Thunder(PARAM.Row d1) => D1Max(d1, "defenseThunder", "thunderDefRate");
        double D1Poison(PARAM.Row d1) => Math.Max(D1Max(d1, "resistPoison", "resistPoisonRate"), D1Max(d1, "resistDisease", "resistDiseaseRate"));
        double D1Bleed(PARAM.Row d1) => D1Max(d1, "resistBlood", "resistBloodRate");
        double D1Curse(PARAM.Row d1) => D1Max(d1, "resistCurse", "resistCurseRate");

        // DS2 upgrade tracks: growth (max/min) per track, from vanilla rows
        var vanillaRf = ds2.Params["ArmorReinforceParam"].Rows.Where(r => Get(r, "minPhysical") > 0).ToList();
        double Growth(Func<PARAM.Row, bool> track) => Median(vanillaRf.Where(track).Select(r => Get(r, "maxPhysical") / Get(r, "minPhysical")));
        double growth10 = Growth(r => I(r, "maxLevel") == 10), growth5 = Growth(r => I(r, "maxLevel") == 5 && I(r, "reinforceCost") / 10_000_000 == 3);
        int Slot(int ds1Id) => ds1Id / 1000 % 10;
        int MostCommonCost(IEnumerable<PARAM.Row> rows) => rows.GroupBy(r => I(r, "reinforceCost")).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key).FirstOrDefault();
        // slot of a vanilla ArmorReinforceParam row = its ArmorParam's armorCategory (1 head … 4 legs)
        var rfSlot = ds2.Params["ArmorParam"].Rows.GroupBy(a => I(a, "armorReinforceId")).ToDictionary(g => g.Key, g => I(g.First(), "armorCategory") - 1);
        var twinklingRows = vanillaRf.Where(r => I(r, "maxLevel") == 5 && I(r, "reinforceCost") / 10_000_000 == 3).ToList();

        // phys modifier factor k (DS2 slash/physical − 1 = k · DS1 slash/100), fitted on the shared pieces
        var k = new double[3];
        for (int p = 0; p < 3; p++)
        {
            int pi = p;
            k[p] = Median(shared.Where(s => Get(s.Ds1, Ds1Phys[pi]) != 0 && Get(s.Reinforce, "maxPhysical") > 0)
                .Select(s => (Get(s.Reinforce, "max" + Ds2Phys[pi]) / Get(s.Reinforce, "maxPhysical") - 1) / (Get(s.Ds1, Ds1Phys[pi]) / 100.0)), 1);
        }
        // spread of the calibration: per slot, the shared pieces' DS2 max physical / DS1 max physical (median, quartiles)
        string Spread(int s)
        {
            var r = shared.Where(p => Slot(p.Ds1.ID) == s && D1Phys(p.Ds1) > 0).Select(p => Get(p.Reinforce, "maxPhysical") / D1Phys(p.Ds1)).Order().ToArray();
            return r.Length == 0 ? "-" : $"{r[r.Length / 2]:0.##} [{r[r.Length / 4]:0.##}-{r[r.Length * 3 / 4]:0.##}] n{r.Length}";
        }
        report.Add($"calibration: {shared.Count} shared pieces; physical ratio head {Spread(0)}, body {Spread(1)}, arms {Spread(2)}, legs {Spread(3)}; growth +10 ×{growth10:0.###}, +5 twinkling ×{growth5:0.###}; slash/strike/thrust k {k[0]:0.##}/{k[1]:0.##}/{k[2]:0.##}");

        var script = pack.RegulationScript(Script);
        foreach (var item in mine)
        {
            int ds1Id = Ds1Id(item.ID);
            var src = protector[ds1Id];
            if (src == null) { report.Add($"{item.ID}: no DS1 row {ds1Id}"); continue; }
            int slot = Slot(ds1Id);
            var basis = shared.Where(s => Slot(s.Ds1.ID) == slot).ToList();
            if (basis.Count == 0) basis = shared;
            double R(Func<Pair, double> ds2v, Func<PARAM.Row, double> ds1v) =>
                Median(basis.Select(s => (A: ds2v(s), B: ds1v(s.Ds1))).Where(t => t.A > 0 && t.B > 0).Select(t => t.A / t.B), 0);
            var armor = pack.Param("ArmorParam")[I(item, "armorParamId")]!;
            var rf = pack.Param("ArmorReinforceParam")[I(armor, "armorReinforceId")]!;

            // AR3: upgrade track
            int ds1Type = I(src, "reinforceTypeId");
            bool upgradable = I(src, "materialSetId") >= 0;
            string track;
            double growth;
            if (!upgradable || ds1Type >= 100)
            {
                // DS2 has no unreinforceable player armour: DS1's "Unique" +5 and its unreinforceable (boss) pieces take DS2's
                // +5 twinkling track, as WeaponPorter does for +5 and non-upgradable weapons
                var rows = twinklingRows.Where(r => rfSlot.GetValueOrDefault(r.ID, -1) == slot).ToList();
                Set(rf, "maxLevel", 5); Set(rf, "reinforceCost", MostCommonCost(rows.Count > 0 ? rows : twinklingRows));
                growth = growth5; track = upgradable ? "+5 twinkling" : "+5 twinkling (DS1: not reinforceable)";
            }
            else
            {
                if (I(rf, "maxLevel") != 10)
                {
                    var rows = basis.Select(b => b.Reinforce).Where(r => I(r, "maxLevel") == 10).ToList();
                    Set(rf, "maxLevel", 10); Set(rf, "reinforceCost", MostCommonCost(rows));
                }
                growth = growth10; track = "+10 titanite";
            }

            // AR2: defences at full upgrade, then +0
            double phys = D1Phys(src) * R(s => Get(s.Reinforce, "maxPhysical"), D1Phys);
            var values = new Dictionary<string, double>
            {
                ["Physical"] = phys,
                ["Magic"] = D1Magic(src) * R(s => Get(s.Reinforce, "maxMagic"), D1Magic),
                ["Fire"] = D1Fire(src) * R(s => Get(s.Reinforce, "maxFire"), D1Fire),
                ["Thunder"] = D1Thunder(src) * R(s => Get(s.Reinforce, "maxThunder"), D1Thunder),
                ["Dark"] = D1Magic(src) * R(s => Get(s.Reinforce, "maxDark"), D1Magic),
                ["Poison"] = D1Poison(src) * R(s => Get(s.Reinforce, "maxPoison"), D1Poison),
                ["Bleeding"] = D1Bleed(src) * R(s => Get(s.Reinforce, "maxBleeding"), D1Bleed),
                ["Petrifaction"] = D1Curse(src) * R(s => Get(s.Reinforce, "maxPetrifaction"), D1Curse),
                ["Curse"] = D1Curse(src) * R(s => Get(s.Reinforce, "maxCurse"), D1Curse),
            };
            for (int p = 0; p < 3; p++) values[Ds2Phys[p]] = Math.Max(0, phys * (1 + k[p] * Get(src, Ds1Phys[p]) / 100.0));
            foreach (var (f, v) in values)
            {
                Set(rf, "max" + f, Math.Round(v, 1));
                Set(rf, "min" + f, Math.Round(v / growth, 1));
            }

            // AR2: poise, weight, durability, price
            Set(armor, "strong", Math.Round(Get(src, "saDurability") * R(s => Get(s.Armor, "strong"), d => Get(d, "saDurability")), 1));
            Set(armor, "weight", Math.Round(Get(src, "weight") * R(s => Get(s.Armor, "weight"), d => Get(d, "weight")), 1));
            Set(armor, "durability", Math.Max(1, Math.Round(Get(src, "durability") * R(s => Get(s.Armor, "durability"), d => Get(d, "durability")))));
            double sell = Math.Max(0, Get(src, "sellValue"));
            Set(item, "price", Math.Round(sell * R(s => Get(s.Item, "price"), d => Get(d, "sellValue"))));
            Set(item, "salePrice", Math.Round(sell * R(s => Get(s.Item, "salePrice"), d => Get(d, "sellValue"))));
            Set(item, "sortId", 3000 + I(src, "sortId"));

            // AR4: hidden parts from the shared piece with the nearest DS1 hide flags; AR8 (session 11, outfit11): the piece's own
            // interfere row = that row with every DS1 display group's DS2 culling group set from the DS1 flags
            var near = Interfere(pack, basis, src, armor);

            // AR5: DS1 resident effects → SpEffectArmor event
            var notes = new List<string>();
            var body = new List<EMEVD.Instruction>();
            foreach (string f in new[] { "residentSpEffectId", "residentSpEffectId2", "residentSpEffectId3" })
            {
                int sp = I(src, f);
                if (sp <= 0 || sp == 6300 || sp / 100 == 62) continue; // common poise row / field-less heavy-armour rows
                var e = ds1.Params.Params["SpEffectParam"][sp];
                if (e == null) continue;
                var ins = RingPorter.Translate(pack, ds1, src, e, notes, out _) ?? [];
                body.AddRange(ins);
            }
            script.Events.RemoveAll(ev => ev.ID == item.ID);
            if (body.Count > 0)
            {
                var ev = new EMEVD.Event(item.ID);
                ev.Instructions.AddRange(body);
                script.Events.Add(ev);
                script.Events.Sort((a, b) => a.ID.CompareTo(b.ID));
                Set(item, "specialEffectId", item.ID);
            }
            else Set(item, "specialEffectId", -1);

            string line = $"armour {ds1Id} {ds1.Text("Armor_name_", ds1Id)} → item {item.ID}: {track}; phys {Get(rf, "minPhysical"):0.#}/{Get(rf, "maxPhysical"):0.#} " +
                $"mag {Get(rf, "minMagic"):0.#} fire {Get(rf, "minFire"):0.#} lgt {Get(rf, "minThunder"):0.#} dark {Get(rf, "minDark"):0.#} poi {Get(rf, "minPoison"):0.#} " +
                $"bld {Get(rf, "minBleeding"):0.#} pet {Get(rf, "minPetrifaction"):0.#} crs {Get(rf, "minCurse"):0.#}; poise {Get(armor, "strong"):0.#}, weight {Get(armor, "weight"):0.#}, " +
                $"dur {Get(armor, "durability"):0.#}, price {Get(item, "price"):0.#}; hides as {ds2.RowName("ItemParam", near.Item.ID)}" +
                (notes.Count > 0 ? "; effects: " + string.Join(", ", notes) : "");
            report.Add(line);
            pack.Note(line);
        }
        // AR8 for NPC-only pieces (rule AR-N: an ArmorParam without an item, hollow Oscar's 2350000-2353000): their own interfere row too
        foreach (var armor in pack.Param("ArmorParam").Rows.Where(a => a.ID is >= 14_000_000 and < 14_500_000 && a.ID % 1000 / 100 == 1 && a.ID % 100 <= 3
                     && items[a.ID + 10_000_000] == null && ds2.Params["ArmorParam"][a.ID] == null).ToList())
        {
            int ds1Id = Ds1Id(armor.ID + 10_000_000);
            if (protector[ds1Id] is not { } src) continue;
            var basis = shared.Where(s => Slot(s.Ds1.ID) == Slot(ds1Id)).ToList();
            var near = Interfere(pack, basis.Count > 0 ? basis : shared, src, armor);
            string line = $"armour {ds1Id} (NPC-only ArmorParam {armor.ID}, rule AR-N): AR8 interfere row {I(armor, "interfereId")} from {ds2.RowName("ItemParam", near.Item.ID)}'s";
            report.Add(line);
            pack.Note(line);
        }
        foreach (string line in ArmorCulling.ApplyNaked(pack)) { report.Add("AR8 empty slot: " + line); pack.Note("AR8 empty slot: " + line); }
        return report;
    }

    /// <summary>AR4 + AR8: the shared piece with the nearest DS1 hide flags (DS1's hair/face flags 0-15 first: the vanilla row's
    /// face/hair groups are what AR8 keeps), and the piece's own EquipInterfereParam row (id = its ArmorParam id).</summary>
    static Pair Interfere(Pack pack, List<Pair> basis, PARAM.Row src, PARAM.Row armor)
    {
        var hide = Flags(src);
        int Miss(PARAM.Row d1, int lo, int hi) => Flags(d1).Zip(hide).Skip(lo).Take(hi - lo).Count(z => z.First != z.Second);
        var near = basis.OrderBy(s => Miss(s.Ds1, 0, 16)).ThenBy(s => Miss(s.Ds1, 16, 48))
            .ThenBy(s => Math.Abs(Get(s.Ds1, "defensePhysics") - Get(src, "defensePhysics"))).First();
        var map = AR8Map.Value;
        if (map.Count == 0) { Set(armor, "interfereId", I(near.Armor, "interfereId")); return near; } // no culling map: AR4 alone
        ArmorCulling.InterfereRow(pack, map, I(near.Armor, "interfereId"), armor.ID, ArmorCulling.Ds1Flags(src), $"[Lordran] AR8 {armor.ID}");
        Set(armor, "interfereId", armor.ID);
        return near;
    }

    static readonly Lazy<Dictionary<(int Group, int Slot), int>> AR8Map = new(ArmorCulling.Load);

    static int[] Flags(PARAM.Row d1) => Enumerable.Range(0, 48).Select(i => d1[$"invisibleFlag{i:D2}"] is { } c ? Convert.ToInt32(c.Value) : 0).ToArray();

    static double Median(IEnumerable<double> xs, double fallback = 1)
    {
        var a = xs.Where(double.IsFinite).Order().ToArray();
        return a.Length == 0 ? fallback : a[a.Length / 2];
    }
}
