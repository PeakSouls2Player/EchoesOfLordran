using Lordran.Tools.Formats;
using SoulsFormats;
using static Lordran.Tools.Build.ParamCells;

namespace Lordran.Tools.Build;

/// <summary>
/// Ports a DS1-only arrow or bolt (EquipParamWeapon categories 13/14) into DS2's ammunition chain:
/// ItemParam (arrowParamId) → ArrowParam → BulletParam / PlayerDamageParam rows (8 slots: light/heavy shot × 4
/// variants, ids = item + 0…70; only the slots the template has are copied).
/// <list type="bullet">
/// <item>Template: the nearest DS2 ammo of the same class (arrow / greatarrow / bolt) and element, by expected attack.</item>
/// <item>Attack shown in menus (ArrowParam menu*Attack) is DS1's, rescaled by the shared pairs (Wood/Fire/Poison Arrow,
/// Wood/Heavy/Lightning Bolt): physical per class, elements over all. Greatarrows have no shared pair and are anchored
/// on DS1's basic greatarrow (Dragonslayer Arrow) = DS2's (Iron Greatarrow).</item>
/// <item>Hit damage in the damage rows (damage01..03 by damageType: 0 physical, 1 magic, 2 lightning, 3 fire, 4 dark)
/// is the template's, scaled per element by new/template menu attack.</item>
/// </list>
/// The dragonslayer bonus and the Feather Arrow's range are DS1 specials for the next pass.
/// </summary>
public sealed class AmmoPorter
{
    public enum Kind { Arrow = 1, Greatarrow = 2, Bolt = 4 }

    readonly Ds1Source ds1;
    readonly ParamStore ds2;
    readonly Pack pack;
    readonly List<(PARAM.Row Ds1, PARAM.Row Arrow)> shared;
    readonly Dictionary<int, string> mapNames;

    static readonly int[] Slots = { 0, 10, 20, 30, 40, 50, 60, 70 };
    static readonly (string Ds1, string Menu, int DamageType)[] Elements =
    {
        ("attackBasePhysics", "menuPhysicalAttack", 0), ("attackBaseMagic", "menuMagicAttack", 1),
        ("attackBaseThunder", "menuThunderAttack", 2), ("attackBaseFire", "menufireAttack", 3),
    };
    const int Ds1BasicGreatarrow = 2007000, Ds2IronGreatarrow = 60850000;

    public AmmoPorter(Ds1Source ds1, ParamStore ds2, Pack pack, string itemMapPath)
    {
        this.ds1 = ds1;
        this.ds2 = ds2;
        this.pack = pack;
        var map = File.ReadLines(itemMapPath).Skip(1).Select(l => l.Split('\t')).Where(c => c[0] == "EquipParamWeapon").ToList();
        mapNames = map.ToDictionary(c => int.Parse(c[1]), c => c[2]);
        shared = map.Where(c => c[4] == "shared" && IsAmmo(ds1.Params.Params["EquipParamWeapon"][int.Parse(c[1])]))
            .Select(c => (Ds1: ds1.Params.Params["EquipParamWeapon"][int.Parse(c[1])]!, Arrow: ds2.Params["ArrowParam"][int.Parse(c[5])]))
            .Where(p => p.Arrow != null).Select(p => (p.Ds1, p.Arrow!)).ToList();
    }

    public sealed record Result(int Ds1Id, string Name, int ItemId, int TemplateItemId, string Notes);

    public static bool IsAmmo(PARAM.Row? w) => w != null && Convert.ToInt32(w["weaponCategory"].Value) is 13 or 14;

    static Kind KindOf(int ds1Id, PARAM.Row w) =>
        Convert.ToInt32(w["weaponCategory"].Value) == 14 ? Kind.Bolt
        : ds1Id is 2007000 or 2008000 ? Kind.Greatarrow // DS1 files greatarrows with arrows: Dragonslayer Arrow, Gough's Great Arrow
        : Kind.Arrow;

    /// <summary>
    /// Arrows 66,000,000 + (id − 2,000,000)/1000 × 10,000; bolts 66,100,000 + (id − 2,100,000)/1000 × 10,000. DS2's 60M goods
    /// block (its own ammo is 60,760,000–60,960,000) has no room; 66M–69M is empty and still the goods bucket (id / 10M = 6).
    /// The ammo type itself comes from ItemParam.itemTypeParamId (1100 arrow, 1130 greatarrow, 1160 bolt), copied from the template.
    /// </summary>
    public static int ItemId(int ds1Id) => ds1Id < 2_100_000 ? 66_000_000 + (ds1Id - 2_000_000) / 1000 * 10_000 : 66_100_000 + (ds1Id - 2_100_000) / 1000 * 10_000;

    public Result Port(int ds1Id)
    {
        var src = ds1.Params.Params["EquipParamWeapon"][ds1Id] ?? throw new KeyNotFoundException($"DS1 ammo {ds1Id}");
        string name = mapNames.TryGetValue(ds1Id, out var n) ? n : ds1.Text("Weapon_name_", ds1Id);
        var kind = KindOf(ds1Id, src);
        int itemId = ItemId(ds1Id);
        EnsureFree(itemId);

        // Expected DS2 menu attack per element.
        var target = Elements.ToDictionary(e => e.Menu, e => Get(src, e.Ds1) * Ratio(kind, e));

        // Template: same class, same element set, nearest attack.
        var template = ds2.Params["ArrowParam"].Rows
            .Where(r => r.ID is >= 60_000_000 and < 61_000_000 && I(r, "arrowType") == (int)kind)
            .OrderBy(r => Elements.Count(e => (Get(r, e.Menu) > 0) != (target[e.Menu] > 0) && e.DamageType != 0))
            .ThenBy(r => Elements.Sum(e => Math.Abs(Get(r, e.Menu) - target[e.Menu])))
            .First();
        int tId = template.ID;

        var arrow = pack.AddRow("ArrowParam", template, itemId, name);
        foreach (var e in Elements)
            Set(arrow, e.Menu, Math.Round(target[e.Menu]));
        // Slot ids point at the template's own rows (template + 0…70); move them onto ours, leave anything else alone.
        foreach (string field in new[] { "BulletId", "DamageId" })
            foreach (string prefix in new[] { "light", "heavy" })
                for (int v = 0; v < 4; v++)
                {
                    int old = I(template, $"{prefix}{field}{v}");
                    if (old >= tId && old < tId + 100)
                        Set(arrow, $"{prefix}{field}{v}", old - tId + itemId);
                }

        int bullets = 0, damages = 0;
        foreach (int off in Slots)
        {
            if (ds2.Params["BulletParam"][tId + off] is { } b) { pack.AddRow("BulletParam", b, itemId + off, name); bullets++; }
            if (ds2.Params["PlayerDamageParam"][tId + off] is { } d)
            {
                var row = pack.AddRow("PlayerDamageParam", d, itemId + off, name);
                for (int k = 1; k <= 3; k++)
                {
                    int type = I(row, $"damageType0{k}");
                    var e = Elements.FirstOrDefault(x => x.DamageType == type && Get(row, $"damage0{k}") != 0);
                    if (e.Menu == null || Get(template, e.Menu) <= 0) continue;
                    Set(row, $"damage0{k}", Math.Round(Get(row, $"damage0{k}") * target[e.Menu] / Get(template, e.Menu)));
                }
                damages++;
            }
        }

        var tItem = ds2.Params["ItemParam"][tId]!;
        var item = pack.AddRow("ItemParam", tItem, itemId, name);
        Set(item, "iconId", itemId);
        Set(item, "arrowParamId", itemId);
        pack.SetText("itemname", itemId, name);
        pack.SetText("detailedexplanation", itemId, ds1.LoreText("Weapon_long_desc_", ds1Id));
        WeaponPorter.PortIcon(ds1, pack, I(src, "iconId"), itemId, tId);

        string notes = $"{kind}, template {tId} ({ds2.RowName("ItemParam", tId)}), " +
                       string.Join("/", Elements.Where(e => target[e.Menu] > 0).Select(e => $"{e.Menu[4..7]}{Math.Round(target[e.Menu])}")) +
                       $", {bullets} bullets, {damages} damage rows" + (I(src, "isDragonSlayer") > 0 ? ", dragonslayer (special pass)" : "");
        pack.Note($"ammo {ds1Id} {name} → item {itemId}: {notes}");
        return new Result(ds1Id, name, itemId, tId, notes);
    }

    // DS2 menu attack per DS1 attack: physical per class from the shared pairs of that class; elements from all pairs.
    double Ratio(Kind kind, (string Ds1, string Menu, int DamageType) e)
    {
        if (kind == Kind.Greatarrow && e.DamageType == 0)
            return Get(ds2.Params["ArrowParam"][Ds2IronGreatarrow]!, "menuPhysicalAttack") / Get(ds1.Params.Params["EquipParamWeapon"][Ds1BasicGreatarrow]!, "attackBasePhysics");
        var pairs = e.DamageType == 0
            ? shared.Where(p => KindOf(p.Ds1.ID, p.Ds1) == kind && Get(p.Ds1, e.Ds1) > 0)
            : shared.Where(p => Elements.Any(x => x.DamageType != 0 && Get(p.Ds1, x.Ds1) > 0));
        var ratios = e.DamageType == 0
            ? pairs.Select(p => Get(p.Arrow, e.Menu) / Get(p.Ds1, e.Ds1)).ToList()
            : pairs.SelectMany(p => Elements.Where(x => x.DamageType != 0 && Get(p.Ds1, x.Ds1) > 0).Select(x => Get(p.Arrow, x.Menu) / Get(p.Ds1, x.Ds1))).ToList();
        return ratios.Count == 0 ? 1 : ratios.Average();
    }

    void EnsureFree(int itemId)
    {
        if (ds2.Params["ItemParam"][itemId] != null || ds2.Params["ArrowParam"][itemId] != null)
            throw new InvalidOperationException($"ammo id {itemId} already used in DS2");
        foreach (int off in Slots)
            if (ds2.Params["BulletParam"][itemId + off] != null || ds2.Params["PlayerDamageParam"][itemId + off] != null)
                throw new InvalidOperationException($"bullet/damage id {itemId + off} already used in DS2");
    }
}
