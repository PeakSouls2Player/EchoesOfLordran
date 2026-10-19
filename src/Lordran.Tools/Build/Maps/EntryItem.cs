using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// The entry to Lordran (user's choice, session 5): a special item like a Homeward Bone, kept in the inventory and never
/// used up, that warps the player to DS1's start point in the Undead Asylum cell. Pure data, no map edits:
///  - ItemParam row ItemId cloned from the Aged Feather (60355000, the infinite Homeward Bone: ItemUsageParam 2210 is
///    not consumed, can't be dropped);
///  - its special effect = SpEffectActiveItem.emevd event ItemId, the Aged Feather's script with the warp instruction
///    100130:0 changed from (0, -1, -1) "return to last bonfire" to (1, map, EventLocation) "warp to that map at that
///    location" (the form the Crushed Eye Orb uses: 100130:0(1, 10040000, 300020) = Majula location 300020);
///  - the destination: EventLocation row GiantTree.EntryLocationId (3700000) in the Asylum at DS1's player start;
///  - ItemLotParam2_Other row LotId holding the item (the loader gives it when the character has none);
///  - name and texts in itemname / simpleexplanation / detailedexplanation.
/// </summary>
public static class EntryItem
{
    public const int ItemId = 60357000;   // goods, right after the Aged Feather (60355000) and before the Darksign (60360000)
    public const int LotId = 60357000;    // ItemLotParam2_Other
    const int Template = 60355000;        // Aged Feather
    const int DarksignGoods = 117;        // DS1 EquipParamGoods: Darksign (its icon)
    const int LotTemplate = 60007200;     // a one-item event lot (Illusory Ring of a Conqueror)
    /// <summary>Session 11 (fix11, user decision "option 2"): true (default, build-pack --entry firelink) = the Echo lands at
    /// Firelink Shrine's arrival point (MapLinks.FirelinkArrival, where the crow drops the player) and the Asylum is DS1's second
    /// visit (return trip, through the Firelink nest); false (--entry cell) = the old destination, DS1's start point in the
    /// Asylum cell. EmevdTranslator (rule XM1) and AsylumScript (rule XM1-R) apply only to the cell entry.</summary>
    public static bool ToFirelink = true;
    public const string Name = "Echo of Lordran";
    static string Simple => ToFirelink ? "Travel to Firelink Shrine in Lordran" : "Awaken in the Undead Asylum of Lordran";
    static string Detailed => ToFirelink ? DetailedFirelink : DetailedCell;
    const string DetailedFirelink = "A faint echo of a land long past.\nTravel to Firelink Shrine, where the\nUndead of Lordran gather by the fire.\nCan be used repeatedly.\n\nIn an age long gone, the Undead were\nlocked away in the north to await the end\nof the world. One of them escaped.";
    const string DetailedCell = "A faint echo of a land long past.\nAwaken in the cell of the Northern Undead\nAsylum, where the tale of Lordran begins.\nCan be used repeatedly.\n\nIn an age long gone, the Undead were\nlocked away in the north to await the end\nof the world. One of them was chosen.";

    public static void Build(Pack pack, Ds1Source ds1, MapBuilder.Options asylum, Vector3 cellPos, Vector3 cellRot)
    {
        var items = pack.Param("ItemParam");
        if (items[ItemId] != null) throw new InvalidDataException($"ItemParam already has {ItemId}");
        var item = pack.AddRow("ItemParam", items[Template]!, ItemId, Name);
        item["specialEffectId"].Value = ItemId;
        // icon: DS1's Darksign (goods 117) on the Aged Feather's icon canvas
        int darksignIcon = Convert.ToInt32(ds1.Params.Params["EquipParamGoods"][DarksignGoods]!["iconId"].Value);
        item["iconId"].Value = ItemId;
        WeaponPorter.PortIcon(ds1, pack, darksignIcon, ItemId, Template);

        // ---- the effect: the Aged Feather's script, warping to the Asylum cell instead of the last bonfire
        var script = pack.RegulationScript("SpEffectActiveItem");
        if (script.Events.Any(e => e.ID == ItemId)) throw new InvalidDataException($"SpEffectActiveItem already has event {ItemId}");
        var feather = script.Events.First(e => e.ID == Template);
        var ev = new EMEVD.Event(ItemId, feather.RestBehavior);
        int warps = 0;
        // --entry firelink (default): Firelink's arrival point (its EventLocation row is written by MapLinks with the Firelink map;
        // warp-check WT1 proves it exists in the built pack); --entry cell: the Asylum cell
        int destMap = ToFirelink ? MapBuilder.Firelink.MapIdDecimal : asylum.MapIdDecimal;
        int destPoint = ToFirelink ? MapLinks.FirelinkArrival : GiantTree.EntryLocationId;
        foreach (var ins in feather.Instructions)
        {
            byte[] args = (byte[])ins.ArgData.Clone();
            if (ins.Bank == 100130 && ins.ID == 0)
            {
                args = new byte[12];
                BitConverter.GetBytes(1).CopyTo(args, 0);                    // mode 1: map + EventLocation
                BitConverter.GetBytes(destMap).CopyTo(args, 4);              // 19420000 (Firelink) or 18010000 (cell)
                BitConverter.GetBytes(destPoint).CopyTo(args, 8);
                warps++;
            }
            ev.Instructions.Add(new EMEVD.Instruction(ins.Bank, ins.ID, args) { Layer = ins.Layer });
        }
        if (warps != 1) throw new InvalidDataException($"the Aged Feather's script has {warps} warp instructions, expected 1");
        script.Events.Add(ev);
        script.Events.Sort((a, b) => a.ID.CompareTo(b.ID));

        // ---- the lot the loader gives
        var lots = pack.Param("ItemLotParam2_Other");
        if (lots[LotId] != null) throw new InvalidDataException($"ItemLotParam2_Other already has {LotId}");
        var lot = pack.AddRow("ItemLotParam2_Other", lots[LotTemplate]!, LotId, Name);
        lot["itemId1"].Value = (uint)ItemId;
        lot["itemNum1"].Value = (byte)1;

        pack.SetText("itemname", ItemId, Name);
        pack.SetText("simpleexplanation", ItemId, Simple);
        pack.SetText("detailedexplanation", ItemId, Detailed);

        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        GiantTree.AddEntryLocation(pack, asylum, cellPos, cellRot, defs);
        pack.Note($"Lordran entry item {ItemId} \"{Name}\" (Aged Feather clone; effect 100130:0(1, {destMap}, {destPoint})), lot {LotId}; destination {(ToFirelink ? "Firelink Shrine (--entry firelink: the Asylum is the return trip)" : $"the Asylum cell at {cellPos} (--entry cell)")}");
        Console.WriteLine($"Lordran entry: item {ItemId} \"{Name}\" → map {destMap} location {destPoint} ({(ToFirelink ? "Firelink Shrine" : $"the Asylum cell, {cellPos}")})");
    }
}
