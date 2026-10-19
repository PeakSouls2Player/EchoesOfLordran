using SoulsFormats;
using static Lordran.Tools.Build.ParamCells;

namespace Lordran.Tools.Build;

/// <summary>
/// DS1-only goods as new DS2 goods (session 6, first for the Asylum's keys). The item map lists 55 DS1 goods as "new"
/// (boss souls, Lordran keys, quest items); this ports them on demand: ItemParam cloned from a DS2 key item (keys can't be
/// used, dropped or sold), DS1 name / short / long description, DS1 icon on the template's canvas.
/// Ids: 52,000,000 + DS1 id × 1000 (Dungeon Cell Key 2010 → 54,010,000), clear of DS2's goods and our ammo (66M) range.
/// <para>Session 11 (track equip11, PLAN milestone 7), <see cref="PortGoods"/>, rules GD1-GD6 (docs\translation-rules.md):
/// every "new" and "system-new" goods row by kind (<c>build-pack --goods all|ID+ID</c>):</para>
/// <list type="bullet">
/// <item><b>GD1 duplicates</b>: a DS1 goods DS2 has under another spelling is DS2's (ItemMap.Renamed: Elizabeth's Mushroom
/// = DS2 Elizabeth Mushroom 60035000, same HP regeneration), checked against DS2's goods names at build time.</item>
/// <item><b>GD2 boss souls</b> (DS1 SpEffect soul &gt; 0): DS2's boss-soul rule: ItemParam cloned from Soul of the Pursuer
/// (usage "consume for souls", stack 99; DS2's other boss-soul use, trading with Straid, needs shop rows per soul and isn't
/// made), SpEffectActiveItem event = the template's 100130:1 with souls = DS1 souls × the median DS2/DS1 souls over the soul
/// items both games share (Soul of a Lost Undead … Soul of a Great Hero).</item>
/// <item><b>GD3 keys and quest items</b> (goodsType 1, or not consumable): the key template (<see cref="Port"/>).</item>
/// <item><b>GD4 consumables with a DS2 equivalent</b> (<see cref="Equivalents"/>): the DS2 item's row with its effect event
/// (pointed at, never changed): Purple / Blooming Purple Moss Clump = Poison Moss (cures poison and toxic), Help me! Carving
/// = the Hello Carving's carving effect.</item>
/// <item><b>GD5 no DS2 effect</b> (Bloodred Moss: DS2 has no bleed-buildup cure; Purging Stone: no lasting curse state; Egg
/// Vermifuge: no egg parasite; Transient Curse: no ghosts; Binoculars: no zoom tool; Silver Pendant: Seath's beam; Black Eye
/// Orb: event item): consumables take DS2's effect-less Rubbish row, tools the key row; logged UNMAPPED.</item>
/// <item><b>GD6 effect-less sellables</b> (Copper/Silver/Gold Coin, Pendant: no refId in DS1): Rubbish's row, price / sale
/// price = DS1 sellValue × the median DS2 price (salePrice) / DS1 sellValue over the shared goods.</item>
/// </list>
/// </summary>
public static class GoodsPorter
{
    const int KeyTemplate = 50870000; // Lenigrast's Key
    const int BossSoulTemplate = 64000000; // Soul of the Pursuer
    const int RubbishTemplate = 60510000; // Rubbish: no effect, stack 99
    public const string ActiveItemScript = "SpEffectActiveItem";

    public static int Id(int ds1Id) => 52_000_000 + ds1Id * 1000;

    /// <summary>GD4: DS1 goods → the DS2 item whose row and effect stand in, and why.</summary>
    static readonly Dictionary<int, (int Ds2, string Why)> Equivalents = new()
    {
        [271] = (60070000, "Poison Moss: cures poison and toxic (DS1 SpEffect 3060 cures poison and toxic)"),
        [272] = (60070000, "Poison Moss: cures poison and toxic (DS1 3070 also lowers buildup for a while: the DS2 moss's short 1000:1 window stands in)"),
        [514] = (60470000, "Hello Carving's effect (DS2 has no \"Help me!\" voice line)"),
    };

    /// <summary>GD5: DS1 goods whose effect DS2 can't express.</summary>
    static readonly Dictionary<int, string> Unmapped = new()
    {
        [270] = "cures bleeding and lowers bleed buildup: DS2 has no bleed-buildup cure",
        [274] = "cures curse: DS2 has no lasting curse state to cure",
        [275] = "cures the egg parasite: not in DS2",
        [312] = "lets the user hit ghosts: DS2 has no ghost state",
        [371] = "zoom view: DS2 has no binocular tool",
        [220] = "deflects Seath's curse beam (DS1 event/boss logic)",
        [115] = "Shiva's invasion orb (DS1 event logic)",
        [116] = "Shiva's invasion orb (DS1 event logic)",
    };

    /// <summary>Ports the DS1 goods once per pack with the key template (GD3); returns the DS2 item id.</summary>
    public static int Port(Pack pack, Ds1Source ds1, int ds1Id)
    {
        int id = Id(ds1Id);
        var items = pack.Param("ItemParam");
        if (items[id] != null) return id; // already ported in this pack
        if (ds1.Params.Params["EquipParamGoods"][ds1Id] is not { } src) throw new InvalidDataException($"DS1 goods {ds1Id} not found");
        string name = ds1.Text("Item_name_", ds1Id);
        var item = pack.AddRow("ItemParam", items[KeyTemplate]!, id, $"[Lordran] {name}");
        item["iconId"].Value = System.Convert.ChangeType(id, item["iconId"].Value.GetType());
        WeaponPorter.PortIcon(ds1, pack, System.Convert.ToInt32(src["iconId"].Value), id, KeyTemplate);
        pack.SetText("itemname", id, name);
        pack.SetText("simpleexplanation", id, ds1.Text("Item_description_", ds1Id));
        pack.SetText("detailedexplanation", id, ds1.LoreText("Item_long_desc_", ds1Id));
        pack.Note($"goods {ds1Id} {name} → item {id} (key template {KeyTemplate})");
        return id;
    }

    /// <summary>The "new" and "system-new" DS1 goods of the item map (GD1).</summary>
    public static List<int> NewIds() => File.ReadLines(Commands.ItemMap.DefaultOut).Skip(1).Select(l => l.Split('\t'))
        .Where(c => c[0] == "EquipParamGoods" && c[4] is "new" or "system-new").Select(c => int.Parse(c[1])).ToList();

    /// <summary>GD1: DS2's own item for a DS1 goods DS2 renamed, or 0.</summary>
    public static int Ds2Duplicate(Pack pack, Ds1Source ds1, int ds1Id)
    {
        string name = ds1.Text("Item_name_", ds1Id);
        string ds2Name = Commands.ItemMap.RenamedName(name);
        if (string.Equals(ds2Name, name, StringComparison.OrdinalIgnoreCase)) return 0;
        return pack.Text("itemname").Entries.FirstOrDefault(x => x.ID is >= 50_000_000 and < 70_000_000 && string.Equals(x.Text, ds2Name, StringComparison.OrdinalIgnoreCase))?.ID ?? 0;
    }

    /// <summary>GD1-GD6: ports one DS1 goods by kind; returns the DS2 item id and what was done.</summary>
    public static (int Item, string How) PortGoods(Pack pack, Ds1Source ds1, int ds1Id)
    {
        if (Ds2Duplicate(pack, ds1, ds1Id) is int dup and > 0)
        {
            pack.Note($"goods {ds1Id} {ds1.Text("Item_name_", ds1Id)} = DS2 {dup} (rule GD1, renamed)");
            return (dup, $"DS2's {dup} (GD1, renamed duplicate)");
        }
        var src = ds1.Params.Params["EquipParamGoods"][ds1Id] ?? throw new InvalidDataException($"DS1 goods {ds1Id} not found");
        int id = Id(ds1Id);
        string name = ds1.Text("Item_name_", ds1Id);
        var items = pack.Param("ItemParam");
        if (items[id] != null) return (id, "already in the pack");
        var effect = I(src, "refCategory") == 2 && I(src, "refId") > 0 ? ds1.Params.Params["SpEffectParam"][I(src, "refId")] : null;
        bool consume = I(src, "isConsume") != 0;
        string how;
        int iconTemplate;
        if (effect != null && I(effect, "soul") > 0)
        {
            // GD2 boss soul
            int souls = (int)Math.Round(I(effect, "soul") * SoulRatio(pack, ds1));
            var item = Row(pack, items, BossSoulTemplate, id, name);
            Set(item, "specialEffectId", id);
            var script = pack.RegulationScript(ActiveItemScript);
            var tev = script.Events.First(e => e.ID == BossSoulTemplate);
            var ev = new EMEVD.Event(id, tev.RestBehavior);
            foreach (var x in tev.Instructions)
            {
                var a = x.ArgData.ToArray();
                if (x is { Bank: 100130, ID: 1 } && a.Length >= 16) BitConverter.GetBytes(souls).CopyTo(a, 12);
                ev.Instructions.Add(new EMEVD.Instruction(x.Bank, x.ID, a));
            }
            script.Events.RemoveAll(e => e.ID == id);
            script.Events.Add(ev);
            script.Events.Sort((a, b) => a.ID.CompareTo(b.ID));
            how = $"boss soul (GD2): {I(effect, "soul")} DS1 souls → {souls}";
            iconTemplate = BossSoulTemplate;
        }
        else if (Equivalents.TryGetValue(ds1Id, out var eq))
        {
            Row(pack, items, eq.Ds2, id, name); // keeps the DS2 item's specialEffectId: its vanilla event
            how = $"DS2 {eq.Ds2} {eq.Why} (GD4)";
            // session 12 (bomb12, rule GD4b): DS2 throws / places a goods item's bullet by the ITEM id (BulletParam + PlayerDamageParam
            // [item id]: Firebomb 60570000, the carvings 60470000-60500000), so an equivalent whose DS2 item is such a bullet gets
            // copies of those rows under its own id (children / damage ids inside stay the DS2 item's, shared); without them using
            // the item (Help me! Carving → Hello Carving's bullet) looks up a missing BulletParam 52514000 (bullet-check P2)
            foreach (string p in new[] { "BulletParam", "PlayerDamageParam" })
                if (pack.Param(p)[eq.Ds2] is { } br && pack.Param(p)[id] == null) { pack.AddRow(p, br, id, $"[Lordran] {name}"); how += $", {p} {id} = {eq.Ds2}'s (GD4b)"; }
            iconTemplate = eq.Ds2;
        }
        else if (I(src, "goodsType") == 1 || !consume)
        {
            Port(pack, ds1, ds1Id);
            how = Unmapped.TryGetValue(ds1Id, out var u) ? $"key template, UNMAPPED effect: {u} (GD5)" : "key / quest item (GD3)";
            iconTemplate = 0; // Port made it
        }
        else
        {
            var item = Row(pack, items, RubbishTemplate, id, name);
            Set(item, "specialEffectId", -1);
            double sell = Math.Max(0, Get(src, "sellValue"));
            Set(item, "salePrice", Math.Round(sell * PriceRatio(pack, ds1, "salePrice")));
            Set(item, "price", Math.Round(sell * PriceRatio(pack, ds1, "price")));
            how = Unmapped.TryGetValue(ds1Id, out var u) ? $"no-effect consumable (Rubbish row), UNMAPPED effect: {u} (GD5)"
                : effect == null && I(src, "refId") < 0 ? "no effect in DS1 either: Rubbish row (GD6)" : $"UNMAPPED DS1 effect {I(src, "refCategory")}:{I(src, "refId")}: Rubbish row (GD5)";
            iconTemplate = RubbishTemplate;
        }
        var done = items[id]!;
        Set(done, "sortId", 3000 + Math.Min(I(src, "sortId"), 60000));
        pack.SetText("itemname", id, name);
        pack.SetText("simpleexplanation", id, ds1.Text("Item_description_", ds1Id));
        pack.SetText("detailedexplanation", id, ds1.LoreText("Item_long_desc_", ds1Id));
        if (iconTemplate != 0)
            WeaponPorter.PortIcon(ds1, pack, I(src, "iconId"), id, I(items[iconTemplate]!, "iconId"));
        pack.Note($"goods {ds1Id} {name} → item {id}: {how}");
        return (id, how);
    }

    static PARAM.Row Row(Pack pack, PARAM items, int template, int id, string name)
    {
        var item = pack.AddRow("ItemParam", items[template]!, id, $"[Lordran] {name}");
        Set(item, "iconId", id);
        return item;
    }

    static double? soulRatio;
    static readonly Dictionary<string, double> priceRatio = new();

    /// <summary>GD2: median DS2 souls / DS1 souls over the shared soul items (100130:1 souls vs DS1 SpEffect soul).</summary>
    static double SoulRatio(Pack pack, Ds1Source ds1)
    {
        if (soulRatio is double r) return r;
        var script = pack.RegulationScript(ActiveItemScript);
        var ratios = new List<double>();
        foreach (var c in File.ReadLines(Commands.ItemMap.DefaultOut).Skip(1).Select(l => l.Split('\t')).Where(c => c[0] == "EquipParamGoods" && c[4] == "shared" && c[5].Length > 0))
        {
            var g = ds1.Params.Params["EquipParamGoods"][int.Parse(c[1])];
            if (g == null || I(g, "refCategory") != 2 || ds1.Params.Params["SpEffectParam"][I(g, "refId")] is not { } e || I(e, "soul") <= 0) continue;
            var ev = script.Events.FirstOrDefault(x => x.ID == int.Parse(c[5]));
            var ins = ev?.Instructions.FirstOrDefault(x => x is { Bank: 100130, ID: 1 } && x.ArgData.Length >= 16);
            if (ins != null) ratios.Add(BitConverter.ToInt32(ins.ArgData, 12) / (double)I(e, "soul"));
        }
        var a = ratios.Order().ToArray();
        return (soulRatio = a.Length == 0 ? 1 : a[a.Length / 2]).Value;
    }

    /// <summary>GD6: median DS2 price field / DS1 sellValue over the shared goods with both set.</summary>
    static double PriceRatio(Pack pack, Ds1Source ds1, string field)
    {
        if (priceRatio.TryGetValue(field, out double r)) return r;
        var items = pack.Param("ItemParam");
        var ratios = File.ReadLines(Commands.ItemMap.DefaultOut).Skip(1).Select(l => l.Split('\t')).Where(c => c[0] == "EquipParamGoods" && c[4] == "shared" && c[5].Length > 0)
            .Select(c => (G: ds1.Params.Params["EquipParamGoods"][int.Parse(c[1])], I: items[int.Parse(c[5])]))
            .Where(t => t.G != null && t.I != null && Get(t.G, "sellValue") > 0 && Get(t.I, field) > 0)
            .Select(t => Get(t.I!, field) / Get(t.G!, "sellValue")).Order().ToArray();
        return priceRatio[field] = ratios.Length == 0 ? 1 : ratios[ratios.Length / 2];
    }
}
