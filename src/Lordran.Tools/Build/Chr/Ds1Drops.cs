using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// DS1 enemy drops → a DS2 ItemLotParam2_Chr row (session 6). DS1: NpcParam.itemLotId_1..6 → ItemLotParam rows, each one
/// roll among up to 8 entries (lotItemId0N / lotItemCategory0N / lotItemBasePoint0N / lotItemNum0N; id 0 = nothing), so an
/// entry's chance is its base point over the row's total. DS2 (lotCount 1): one roll where itemWeightN is item N's chance in
/// percent. Items go through the DS1 → DS2 item map (data\lordran\item-map.tsv: duplicates use DS2's item) or the ids our
/// porters gave DS1-only items; items not ported yet are left out and reported. DS2-style random Lifegem drops (user
/// decision, session 3) are added at the rate of the DS2 template enemy's own lot.
/// </summary>
public static class Ds1Drops
{
    public const int Lifegem = 60010000;
    /// <summary>Lifegem chance when the template lot has none: DS2's Hollow Infantry lots 1105000-1105400 drop it at 7%.</summary>
    public const float DefaultLifegemPercent = 7f;
    const int Filler = 60510000; // DS2's lots fill unused slots with this id at weight 0

    public sealed record Drop(int ItemId, float Percent, int Num, int Reinforce, string Note);

    /// <summary>
    /// Rule G1 (user decision, session 9: "instead of Estus Flask he should give an Estus shard"): every DS1 Estus Flask
    /// that a DS1 lot hands out (goods 200-215: the flask +0…+7 and its empty forms, Oscar's gift 1082 and his drop 6022)
    /// becomes DS2's Estus Flask Shard (ItemParam 60525000, checked in the build's ItemParam: "Estus Flask Shard"), ×1 per
    /// DS1 lot row, always given (no "has none" test): the talk gift and the death drop are given once by the DS1 lot's own
    /// item flag (TalkBuilder's gift rule). DS2's own flask (60155000) stays the item a DS1 "has Estus" test reads.
    /// </summary>
    public const int EstusShard = 60525000, Ds2EstusFlask = 60155000;
    public static bool IsEstus(uint category, int id) => category == 0x40000000u && id is >= 200 and <= 215;

    static Dictionary<(string Param, int Id), (string Result, int Ds2)>? map;

    static Dictionary<(string Param, int Id), (string Result, int Ds2)> Map()
    {
        if (map != null) return map;
        map = new();
        foreach (var line in File.ReadLines(Commands.ItemMap.DefaultOut).Skip(1))
        {
            var c = line.Split('\t');
            if (c.Length < 6) continue;
            map[(c[0], int.Parse(c[1]))] = (c[4], int.TryParse(c[5], out int d) ? d : 0);
        }
        return map;
    }

    /// <summary>Session 10 (asy-items i2 fix pass): the item map's row (result, DS2 id) of a DS1 item, or null.</summary>
    public static (string Result, int Ds2)? Mapping(string param, int ds1Id) => Map().TryGetValue((param, ds1Id), out var m) ? m : null;

    /// <summary>Session 10 (asy-items i2, rule S1): how many DS2 items one DS1 system item stands for
    /// (Mappings/system-items.tsv column "quantity": Twin Humanities = 2 Human Effigies), 1 otherwise.</summary>
    public static int SystemQuantity(string ds1Name)
    {
        systemQuantity ??= File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Mappings", "system-items.tsv")).Skip(1)
            .Select(l => l.Split('\t')).Where(c => c.Length > 5 && int.TryParse(c[5], out _))
            .ToDictionary(c => c[0], c => int.Parse(c[5]), StringComparer.OrdinalIgnoreCase);
        return systemQuantity.GetValueOrDefault(ds1Name, 1);
    }
    static Dictionary<string, int>? systemQuantity;

    /// <summary>The DS1 drops of one NpcParam row, as DS2 items with percent chances.</summary>
    public static List<Drop> Resolve(Ds1Source ds1, PARAM.Row npc, Pack pack, List<string> notes) =>
        ResolveRows(ds1, Enumerable.Range(1, 6).Select(k => System.Convert.ToInt32(npc[$"itemLotId_{k}"].Value)), pack, notes);

    /// <summary>DS1 ItemLotParam rows (each one roll) as DS2 items with percent chances. Session 8 (track npc, fix pass):
    /// NpcPorter passes an NPC's whole lot chains (6020 → 6021 → 6022) without the rows its talk machine gives.</summary>
    public static List<Drop> ResolveRows(Ds1Source ds1, IEnumerable<int> lotRows, Pack pack, List<string> notes)
    {
        var lots = ds1.Params.Params["ItemLotParam"];
        var drops = new List<Drop>();
        foreach (int lotId in lotRows)
        {
            if (lotId <= 0 || lots[lotId] is not { } lot) continue;
            var entries = Enumerable.Range(1, 8).Select(i => (
                Id: System.Convert.ToInt32(lot[$"lotItemId0{i}"].Value),
                Cat: unchecked((uint)System.Convert.ToInt64(lot[$"lotItemCategory0{i}"].Value)),
                Points: System.Convert.ToDouble(lot[$"lotItemBasePoint0{i}"].Value),
                Num: System.Convert.ToInt32(lot[$"lotItemNum0{i}"].Value))).ToList();
            double total = entries.Sum(e => e.Points);
            if (total <= 0) continue;
            foreach (var e in entries.Where(e => e.Id > 0 && e.Points > 0))
            {
                float pct = (float)(100 * e.Points / total);
                int ds2 = MapItem(pack, ds1, e.Cat, e.Id, out string kind, out int reinforce, out string how);
                if (ds2 == 0)
                {
                    notes.Add($"lot {lotId}: DS1 {kind} {e.Id} ({pct:0.#}%) not in DS2 yet, left out");
                    continue;
                }
                drops.Add(new Drop(ds2, pct, IsEstus(e.Cat, e.Id) ? 1 : Math.Max(1, e.Num), reinforce, $"DS1 {kind} {e.Id} {pct:0.#}% ({how})"));
            }
        }
        return drops;
    }

    /// <summary>
    /// A DS1 item (ItemLotParam category + id) as a DS2 item id: the item map (duplicates → DS2's item), our ported
    /// weapons/shields, or DS1-only goods ported on demand (<see cref="GoodsPorter"/>). 0 = not available in DS2 yet.
    /// </summary>
    public static int MapItem(Pack pack, Ds1Source ds1, uint category, int ds1Id, out string kind, out int reinforce, out string how)
    {
        var items = pack.Param("ItemParam");
        if (IsEstus(category, ds1Id))
        {
            kind = "goods"; reinforce = 0; how = "Estus Flask → DS2 Estus Flask Shard (rule G1)";
            return items[EstusShard] != null ? EstusShard : 0;
        }
        string param;
        (param, kind) = category switch
        {
            0x00000000 => ("EquipParamWeapon", "weapon"),
            0x10000000 => ("EquipParamProtector", "armor"),
            0x20000000 => ("EquipParamAccessory", "ring"),
            _ => ("EquipParamGoods", "goods"),
        };
        // weapons and armour: upgrade level in the last two digits, DS1 infusion in the hundreds
        int baseId = kind is "weapon" or "armor" ? ds1Id / 1000 * 1000 : ds1Id;
        reinforce = kind == "weapon" ? ds1Id % 100 : 0;
        how = "";
        if (Map().TryGetValue((param, baseId), out var m))
        {
            if (m.Ds2 > 0 && items[m.Ds2] != null) { how = m.Result; return m.Ds2; }
            // DS1-only key items (goodsType 1: keys, the Peculiar Doll …) are ported on demand with a DS2 key as template
            if (kind == "goods" && m.Result == "new" && ds1.Params.Params["EquipParamGoods"][baseId] is { } g && System.Convert.ToInt32(g["goodsType"].Value) == 1)
            { how = "ported key item"; return GoodsPorter.Port(pack, ds1, baseId); }
            // session 11 (cov11): a DS1 covenant offering the item map lists as system-new (Eye of Death) is ported on demand
            if (kind == "goods" && m.Result == "system-new" && Talk.Covenants.IsOffering(baseId))
            { how = "ported covenant offering"; return Talk.Covenants.PortOffering(pack, ds1, baseId); }
            // DS1 spells are goods too (shops sell Heal as goods 5000; the item map marks them "via-spell"): the Magic
            // row's mapping (Heal 5000 → DS2 32010000), session 8 (track npc: Petrus' shop)
            if (kind == "goods" && m.Result == "via-spell" && Map().TryGetValue(("Magic", baseId), out var spell) && spell.Ds2 > 0 && items[spell.Ds2] != null)
            { how = $"spell, {spell.Result}"; return spell.Ds2; }
            // session 11 (spell11), rule SP9: a DS1-only spell (Magic "new": Griggs' Aural Decoy, Rickert's Remedy, Rhea's
            // Karmic Justice …) is ported on demand, like rings and keys
            if (kind == "goods" && m.Result == "via-spell" && Map().TryGetValue(("Magic", baseId), out var newSpell) && newSpell.Result == "new"
                && SpellPorter.PortOnDemand(pack, ds1, baseId) is int sid and > 0 && items[sid] != null)
            { how = "ported spell"; return sid; }
            // session 10 (asy-items i2), rule G9: DS1-only rings are ported on demand (RingPorter; a renamed duplicate
            // resolves to DS2's ring)
            if (kind == "ring" && m.Result == "new")
            { how = "ported ring"; return RingPorter.Port(pack, ds1, baseId); }
        }
        // ported weapons by WeaponPorter.Ids / AmmoPorter.ItemId. Session 8 (track npc) fix: the DLC candidates (9,0xx,xxx →
        // 7,9xx,xxx / 18,0xx,xxx) were tried for every id, so DS1 ammo matched DS2 items (Standard Bolt 2100000 →
        // 7,900,000 + 2,100,000 − 9,000,000 = 1,000,000 DS2's Dagger; Standard Arrow → 11,000,000); ammo has its own block
        if (kind == "weapon")
        {
            int[] candidates = baseId is >= 2_000_000 and < 2_200_000 ? [AmmoPorter.ItemId(baseId)]
                : baseId >= 9_000_000 ? [7_900_000 + baseId - 9_000_000, 18_000_000 + baseId - 9_000_000]
                : [7_000_000 + baseId, 17_000_000 + baseId];
            foreach (int cand in candidates)
                if (cand > 0 && items[cand] != null) { how = "ported"; return cand; }
        }
        return 0;
    }

    /// <summary>Writes the row: the template lot's layout, our drops, then the template's Lifegem chance (enemies; NPCs
    /// pass <paramref name="lifegem"/> false: vanilla NPC lots have no Lifegem, session 8 fix pass).</summary>
    public static int Write(Pack pack, PARAM.Row template, int rowId, string name, List<Drop> drops, bool lifegem = true)
    {
        var row = pack.AddRow("ItemLotParam2_Chr", template, rowId, name);
        float lifegemPercent = 0;
        for (int i = 1; i <= 10; i++)
            if (System.Convert.ToInt32(template[$"itemId{i}"].Value) == Lifegem) lifegemPercent += System.Convert.ToSingle(template[$"itemWeight{i}"].Value);
        var all = drops.ToList();
        if (lifegemPercent <= 0) lifegemPercent = DefaultLifegemPercent;
        if (lifegem) all.Add(new Drop(Lifegem, lifegemPercent, 1, 0, "DS2 Lifegem"));
        if (all.Count > 10) all = all.Take(10).ToList();
        for (int i = 1; i <= 10; i++)
        {
            var d = i <= all.Count ? all[i - 1] : null;
            Set(row, $"itemId{i}", d?.ItemId ?? Filler);
            Set(row, $"itemWeight{i}", d?.Percent ?? 0f);
            Set(row, $"itemNum{i}", d?.Num ?? 0);
            Set(row, $"reinforceLevel{i}", d?.Reinforce ?? 0);
            Set(row, $"attribute{i}", 0);
            Set(row, $"bExFlag{i}", 0);
        }
        Set(row, "lotCount", 1);
        return all.Count;
    }

    static void Set(PARAM.Row row, string field, object v) => row[field].Value = System.Convert.ChangeType(v, row[field].Value.GetType());
}
