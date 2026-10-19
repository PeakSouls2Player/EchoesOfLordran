using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (track devkit11). <c>devkit-check PACK</c>: a --dev-kit pack (Build.DevKit) gives every ported DS1 item:
/// every ItemParam row the pack adds over vanilla (weapons, shields, catalysts, ammo, armour, rings, spells, goods, keys;
/// the Echo of Lordran and the kit itself excepted) is in some dev-kit lot; every lot has Licia's give-all shape
/// (lotDropType 3, lotCount = used slots ≤ 7, weight 1 + bExFlag 1 per used slot, empty slots id 10 / weight 0), item ids
/// in the pack's ItemParam, counts 1..99 (equipment 1); the kit (a consumable whose effect event exists, maxNum 1), its lot
/// and lordran\devkit.ini (the loader's table: kit, kit lot, every content lot in order) agree.
/// <c>devkit-check --census</c>: the vanilla SpEffectActiveItem script's opcodes (no item-lot award among them: why the
/// loader awards the lots) and the vanilla ItemLotParam2_Other lot shapes.
/// </summary>
public static class DevKitCheck
{
    public static int Run(string[] args)
    {
        if (args.Length >= 2 && args[1] == "--census") return Census();
        if (args.Length < 2) { Console.Error.WriteLine("usage: devkit-check PACK | devkit-check --census"); return 1; }
        string packDir = args[1];
        int problems = 0;
        void Bad(string s) { problems++; Console.WriteLine("  PROBLEM " + s); }

        var defs = Directory.GetFiles(Path.Combine(ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        var mine = BND4.Read(Ds2RegulationCipher.Decrypt(File.ReadAllBytes(Path.Combine(packDir, "title", "enc_regulation.bnd.dcx"))));
        var van = BND4.Read(Ds2RegulationCipher.Decrypt(File.ReadAllBytes(Path.Combine(Program.DefaultDs2Root, "enc_regulation.bnd.dcx"))));
        PARAM P(BND4 b, string name)
        {
            var p = PARAM.Read(b.Files.First(f => Path.GetFileNameWithoutExtension(f.Name).Equals(name, StringComparison.OrdinalIgnoreCase)).Bytes);
            p.ApplyParamdefCarefully(defs);
            return p;
        }
        var items = P(mine, "ItemParam");
        var vanItems = P(van, "ItemParam");
        var lots = P(mine, "ItemLotParam2_Other");
        var vanIds = vanItems.Rows.Select(r => r.ID).ToHashSet();

        if (items[Build.DevKit.ItemId] == null) { Bad($"no dev-kit item {Build.DevKit.ItemId} (built without --dev-kit?)"); Report(packDir, problems); return 2; }
        var kit = items[Build.DevKit.ItemId]!;
        var script = EMEVD.Read(mine.Files.First(f => Path.GetFileNameWithoutExtension(f.Name).Equals(Build.GoodsPorter.ActiveItemScript, StringComparison.OrdinalIgnoreCase)).Bytes);
        int effect = Convert.ToInt32(kit["specialEffectId"].Value);
        if (!script.Events.Any(e => e.ID == effect)) Bad($"the kit's effect event {effect} is not in {Build.GoodsPorter.ActiveItemScript}");
        if (Convert.ToInt32(kit["maxNum"].Value) != 1) Bad($"the kit's maxNum is {kit["maxNum"].Value}, not 1");
        if (Convert.ToInt32(kit["itemUsageParamId"].Value) != Convert.ToInt32(vanItems[60010000]!["itemUsageParamId"].Value)) Bad("the kit is not used like the Lifegem (consumed on use)");

        // the lots: Licia's give-all shape
        var given = new Dictionary<int, int>(); // item → total count over the lots
        var kitLots = lots.Rows.Where(r => r.ID >= Build.DevKit.LotBase && r.ID < Build.DevKit.LotBase + Build.DevKit.MaxLots).ToList();
        foreach (var lot in kitLots)
        {
            int n = Convert.ToInt32(lot["lotCount"].Value);
            if (Convert.ToInt32(lot["lotDropType"].Value) != 3) Bad($"lot {lot.ID}: lotDropType {lot["lotDropType"].Value}, not 3 (give all)");
            if (n is < 1 or > Build.DevKit.PerLot) Bad($"lot {lot.ID}: lotCount {n}");
            for (int k = 1; k <= 10; k++)
            {
                uint id = Convert.ToUInt32(lot[$"itemId{k}"].Value);
                int num = Convert.ToInt32(lot[$"itemNum{k}"].Value);
                float w = Convert.ToSingle(lot[$"itemWeight{k}"].Value);
                int ex = Convert.ToInt32(lot[$"bExFlag{k}"].Value);
                if (k > n)
                {
                    if (id != 10 || num != 0 || w != 0 || ex != 0) Bad($"lot {lot.ID} slot {k}: not an empty slot (id {id}, num {num}, weight {w})");
                    continue;
                }
                if (items[(int)id] == null) Bad($"lot {lot.ID} slot {k}: item {id} not in the pack's ItemParam");
                if (num is < 1 or > 99 || num > 1 && id is < 50_000_000 or >= 70_000_000) Bad($"lot {lot.ID} slot {k}: count {num} for item {id}");
                if (w != 1 || ex != 1) Bad($"lot {lot.ID} slot {k}: weight {w} / bExFlag {ex}, not 1 / 1");
                given[(int)id] = given.GetValueOrDefault((int)id) + num;
            }
        }

        // every ported item (ItemParam rows the pack adds) is given; the Echo and the kit are not lot content
        var exempt = new HashSet<int> { Build.DevKit.ItemId, Build.Maps.EntryItem.ItemId };
        var ported = items.Rows.Where(r => !vanIds.Contains(r.ID) && !exempt.Contains(r.ID)).Select(r => r.ID).ToList();
        var missing = ported.Where(id => !given.ContainsKey(id)).ToList();
        foreach (int id in missing.Take(40)) Bad($"ported item {id} {items[id]!.Name} is in no dev-kit lot");
        if (missing.Count > 40) Bad($"... and {missing.Count - 40} more");
        var extra = given.Keys.Where(id => vanIds.Contains(id)).ToList(); // vanilla extras (e.g. --test-shop-items) are fine

        // the loader's table
        string ini = Path.Combine(packDir, "lordran", "devkit.ini");
        if (!File.Exists(ini)) Bad("no lordran\\devkit.ini (the loader would not give the kit)");
        else
        {
            var kv = File.ReadAllLines(ini).Where(l => l.Contains('=') && !l.StartsWith(';')).Select(l => l.Split('=', 2)).ToDictionary(x => x[0].Trim(), x => x[1].Trim());
            if (kv.GetValueOrDefault("item") != Build.DevKit.ItemId.ToString() || kv.GetValueOrDefault("lot") != Build.DevKit.KitLot.ToString()) Bad($"devkit.ini item/lot {kv.GetValueOrDefault("item")}/{kv.GetValueOrDefault("lot")} != {Build.DevKit.ItemId}/{Build.DevKit.KitLot}");
            if (lots[Build.DevKit.KitLot] is not { } kl || Convert.ToUInt32(kl["itemId1"].Value) != Build.DevKit.ItemId) Bad($"kit lot {Build.DevKit.KitLot} does not hold the kit");
            int count = int.TryParse(kv.GetValueOrDefault("count"), out int c) ? c : -1;
            var listed = Enumerable.Range(0, Math.Max(0, count)).Select(i => int.TryParse(kv.GetValueOrDefault($"lot{i}"), out int v) ? v : -1).ToList();
            if (kv.GetValueOrDefault("version") != "1" || !listed.SequenceEqual(kitLots.Select(l => l.ID))) Bad($"devkit.ini lists {count} lots, the pack has {kitLots.Count} (or version != 1)");
        }

        static string Kind(int id) => (id / 1_000_000) switch { < 11 => "weapons", < 20 => "shields", < 30 => "armour", < 40 => "spells", < 50 => "rings", _ => "goods/ammo" };
        var byKind = ported.GroupBy(Kind).Select(g => $"{g.Key} {g.Count()}");
        Console.WriteLine($"  {ported.Count} ported items ({string.Join(", ", byKind)}), {kitLots.Count} dev-kit lots, {given.Count} distinct items given ({extra.Count} vanilla extras), {missing.Count} missing");
        Report(packDir, problems);
        return problems == 0 ? 0 : 2;
    }

    static void Report(string packDir, int problems) => Console.WriteLine($"devkit-check {packDir}: {problems} problems");

    static int Census()
    {
        var van = BND4.Read(Ds2RegulationCipher.Decrypt(File.ReadAllBytes(Path.Combine(Program.DefaultDs2Root, "enc_regulation.bnd.dcx"))));
        var script = EMEVD.Read(van.Files.First(f => Path.GetFileNameWithoutExtension(f.Name).Equals(Build.GoodsPorter.ActiveItemScript, StringComparison.OrdinalIgnoreCase)).Bytes);
        foreach (var g in script.Events.SelectMany(e => e.Instructions.Select(i => (e, i))).GroupBy(x => (x.i.Bank, x.i.ID)).OrderBy(g => g.Key))
        {
            var (e, i) = g.First();
            Console.WriteLine($"{g.Key.Bank}:{g.Key.ID}  {g.Count(),4} uses  e.g. event {e.ID} args {BitConverter.ToString(i.ArgData)}");
        }
        // ItemLotParam2_Other: lotDropType × used slots × weights (which rows give every slot)
        var defs = Directory.GetFiles(Path.Combine(ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        var lots = PARAM.Read(van.Files.First(f => Path.GetFileNameWithoutExtension(f.Name) == "ItemLotParam2_Other").Bytes);
        lots.ApplyParamdefCarefully(defs);
        foreach (var g in lots.Rows.GroupBy(r => (T: Convert.ToInt32(r["lotDropType"].Value), C: Convert.ToInt32(r["lotCount"].Value), N: Enumerable.Range(1, 10).Count(k => Convert.ToUInt32(r[$"itemId{k}"].Value) is not (0 or 10)))).OrderBy(g => g.Key))
        {
            var r = g.First();
            Console.WriteLine($"lot dropType {g.Key.T} count {g.Key.C} slots {g.Key.N}: {g.Count()} rows, e.g. {r.ID} weights {string.Join(",", Enumerable.Range(1, g.Key.N).Select(k => r[$"itemWeight{k}"].Value))} nums {string.Join(",", Enumerable.Range(1, g.Key.N).Select(k => r[$"itemNum{k}"].Value))}");
        }
        return 0;
    }
}
