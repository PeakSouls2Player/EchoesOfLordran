using System.Text;
using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Build;

/// <summary>
/// Session 11 (track devkit11; user request "a fully maxed out character with every item so I can speed run through all
/// the levels and test everything"). <c>build-pack --dev-kit</c> only (never in the default build): the "Lordran Dev Kit"
/// goods item and the item lots holding every DS1 item the build ported.
///  - The kit: ItemParam row <see cref="ItemId"/> cloned from the Lifegem (60010000: a vanilla consumable, usage 1610,
///    its own SpEffectActiveItem event = a small heal, pointed at, never changed), maxNum 1, price 0, own name / texts.
///  - Content lots <see cref="LotBase"/>.. (ItemLotParam2_Other): DS2's "give every slot" lot shape, copied from Licia's
///    gift lot 60006000 (lotDropType 3, lotCount = used slots, weight 1 and bExFlag 1 per used slot, empty slots id 10 /
///    weight 0), at most <see cref="PerLot"/> items per lot (vanilla's largest give-all lot). Goods (ids 50M-69M: keys,
///    consumables, ammo) × min(maxNum, 99), everything else × 1; DS2 sends what the inventory can't hold to the item box.
///  - Why the loader awards them, not the kit's item script: the vanilla SpEffectActiveItem script has no item-lot opcode
///    (devkit-check --census: none of its opcodes awards a lot; 131402 is an ESD command, map events only). So the loader
///    (src\loader\devkit.c, table <c>lordran\devkit.ini</c>) does it through the game's own pickup path (the Echo's
///    give-lot function, RVA 0x198E50): it gives the kit lot <see cref="KitLot"/> once to a character without a kit, then
///    watches the kit count; when a kit is used up (count 1 → 0) it gives every content lot (paced) and a new kit. Using the
///    kit again refills (stackables up to their caps, the rest to the item box).
/// devkit-check PACK proves every ported item (ItemParam rows the pack adds, Echo and kit excepted) is in some lot.
/// </summary>
public static class DevKit
{
    public static bool Enabled { get; set; }
    public const int ItemId = 60358000;   // goods, after the Echo of Lordran (60357000)
    public const int KitLot = 60358000;   // ItemLotParam2_Other: the kit itself (the loader gives it)
    public const int LotBase = 66980000;  // content lots (the test shop's lots are 66990000+)
    public const int MaxLots = 1000;
    public const int PerLot = 7;          // vanilla's largest give-all lot (Licia 60006000) holds 7
    const int Template = 60010000;        // Lifegem
    const int LotTemplate = 60006000;     // Licia's gift: lotDropType 3, every slot given
    const int OneItemLot = 60007200;      // a one-item event lot (as EntryItem)
    public const string TableDevice = "lordran", TablePath = "devkit.ini";
    public const string Name = "Lordran Dev Kit";
    const string Simple = "TEST BUILD: every item of Lordran";
    const string Detailed = "A developer's satchel, for testing only.\nUse it to receive every item ported from\nLordran: weapons, shields, catalysts, armour,\nrings, spells, goods and ammunition.\nWhat the inventory cannot hold is sent\nto the item box. A new kit follows.";

    /// <summary>Builds the kit, its lot and the content lots over every ItemParam row the pack adds (call after every
    /// porter and before the test shop). Returns the summary line.</summary>
    public static string Build(Pack pack, ParamStore ds2)
    {
        var items = pack.Param("ItemParam");
        var vanIds = ds2.Params["ItemParam"].Rows.Select(r => r.ID).ToHashSet();
        if (items[ItemId] != null || vanIds.Contains(ItemId)) throw new InvalidDataException($"ItemParam already has {ItemId}");
        var lots = pack.Param("ItemLotParam2_Other");
        if (lots[KitLot] != null) throw new InvalidDataException($"ItemLotParam2_Other already has {KitLot}");
        if (lots.Rows.Any(r => r.ID >= LotBase && r.ID < LotBase + MaxLots)) throw new InvalidDataException($"ItemLotParam2_Other already has rows in {LotBase}..{LotBase + MaxLots - 1}");

        // the content: every item the pack adds over vanilla, in id order (weapons, shields, armour, spells, rings, goods, ammo)
        var content = items.Rows.Where(r => !vanIds.Contains(r.ID) && r.ID != Maps.EntryItem.ItemId).Select(r => r.ID).OrderBy(id => id).ToList();

        // the kit
        var kit = pack.AddRow("ItemParam", items[Template]!, ItemId, Name);
        Set(kit, "maxNum", 1);
        Set(kit, "price", 0);
        Set(kit, "salePrice", 0);
        pack.SetText("itemname", ItemId, Name);
        pack.SetText("simpleexplanation", ItemId, Simple);
        pack.SetText("detailedexplanation", ItemId, Detailed);
        var kl = pack.AddRow("ItemLotParam2_Other", lots[OneItemLot]!, KitLot, Name);
        kl["itemId1"].Value = (uint)ItemId;
        kl["itemNum1"].Value = (byte)1;

        // the content lots
        var lotIds = new List<int>();
        for (int i = 0; i * PerLot < content.Count; i++)
        {
            if (i >= MaxLots) throw new InvalidDataException($"more than {MaxLots * PerLot} items");
            var chunk = content.Skip(i * PerLot).Take(PerLot).ToList();
            int id = LotBase + i;
            var lot = pack.AddRow("ItemLotParam2_Other", lots[LotTemplate]!, id, $"[Lordran dev kit] {i + 1}");
            lot["lotDropType"].Value = (byte)3;
            lot["lotCount"].Value = (byte)chunk.Count;
            for (int k = 1; k <= 10; k++)
            {
                bool used = k <= chunk.Count;
                lot[$"itemId{k}"].Value = used ? (uint)chunk[k - 1] : 10u;
                lot[$"itemNum{k}"].Value = used ? (byte)Count(items[chunk[k - 1]]!) : (byte)0;
                lot[$"itemWeight{k}"].Value = used ? 1f : 0f;
                lot[$"bExFlag{k}"].Value = used ? (byte)1 : (byte)0;
                lot[$"reinforceLevel{k}"].Value = (byte)0;
                lot[$"attribute{k}"].Value = (byte)0;
            }
            lotIds.Add(id);
        }

        // the loader's table
        var ini = new StringBuilder();
        ini.Append("; Lordran Dev Kit (build-pack --dev-kit; TEST BUILDS ONLY). Read by the loader's devkit.c.\r\n");
        ini.Append($"[devkit]\r\nversion=1\r\nitem={ItemId}\r\nlot={KitLot}\r\ninterval_ms=250\r\ncount={lotIds.Count}\r\n");
        for (int i = 0; i < lotIds.Count; i++) ini.Append($"lot{i}={lotIds[i]}\r\n");
        pack.AddFile(TableDevice, TablePath, Encoding.ASCII.GetBytes(ini.ToString()));

        string line = $"dev kit: item {ItemId} \"{Name}\" (Lifegem clone), kit lot {KitLot}, {content.Count} ported items in {lotIds.Count} lots {LotBase}..{LotBase + lotIds.Count - 1} (<= {PerLot} each), lordran\\{TablePath}";
        pack.Note(line);
        return line;
    }

    /// <summary>Goods (50M-69M: keys, consumables, ammo) × min(maxNum, 99); weapons, armour, spells, rings × 1.</summary>
    static int Count(PARAM.Row item) => item.ID is >= 50_000_000 and < 70_000_000 ? Math.Clamp(Convert.ToInt32(item["maxNum"].Value), 1, 99) : 1;

    static void Set(PARAM.Row row, string field, double v) => row[field].Value = Convert.ChangeType(v, row[field].Value.GetType());
}
