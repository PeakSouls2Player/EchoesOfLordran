using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// DS1 (DSR) ItemLotParam lots read the way the game awards them. Session 8 (census tool gap: msb1-dump printed only the
/// first row of a lot).
/// <para>
/// <b>Chain:</b> a lot id N awards row N and then the consecutive rows N+1, N+2 … up to the first id with no row. Evidence:
/// the Hollow Soldier lot 25503000 (Longsword/Hollow Soldier Shield) is followed by rows 25503001-25503004 (helm, armor,
/// waistcloth, Titanite Shard) and 25503005 does not exist; the Asylum Demon reward lot 2660 (Big Pilgrim's Key, flag
/// 50001660) is followed by 2661 (Demon's Great Hammer, flag 50001661), and DS1's early-kill event 11810300 pre-sets
/// 50001661 to suppress the hammer, which only makes sense if 2661 is awarded together with 2660. Status P (not yet
/// observed in game).
/// </para>
/// <para>
/// <b>Row:</b> one weighted pick among the 8 slots by lotItemBasePoint0N (slot id 0 / category -1 = "nothing"), e.g.
/// 25503000 = nothing 97, Longsword 2, shield 1. A row with getItemFlagId set is skipped once the flag is on.
/// Categories: 0 weapon, 0x10000000 armor, 0x20000000 ring, 0x40000000 goods.
/// </para>
/// </summary>
public static class Ds1Lots
{
    public const int MaxChain = 64;

    /// <summary>The rows awarded for a lot id: N and its consecutive successors (empty if row N is missing).</summary>
    public static List<PARAM.Row> Chain(ParamStore store, int lotId)
    {
        var rows = new List<PARAM.Row>();
        if (lotId <= 0 || !store.Params.TryGetValue("ItemLotParam", out var lots)) return rows;
        for (int id = lotId; rows.Count < MaxChain && lots[id] is { } row; id++) rows.Add(row);
        return rows;
    }

    static long L(PARAM.Row r, string f) => System.Convert.ToInt64(r[f].Value);

    public static string Category(long cat) => cat switch
    {
        0 => "weapon", 0x10000000 => "armor", 0x20000000 => "ring", 0x40000000 => "goods", -1 => "none", _ => $"cat{cat:X}",
    };

    static string ParamOf(long cat) => cat switch
    {
        0 => "EquipParamWeapon", 0x10000000 => "EquipParamProtector", 0x20000000 => "EquipParamAccessory", _ => "EquipParamGoods",
    };

    /// <summary>"id 'name' [slot | slot] flag F" for one row: weights as percent of the row total.</summary>
    public static string Row(ParamStore store, PARAM.Row r)
    {
        var slots = Enumerable.Range(1, 8).Select(i => (Id: L(r, $"lotItemId0{i}"), Cat: L(r, $"lotItemCategory0{i}"), N: L(r, $"lotItemNum0{i}"),
            W: L(r, $"lotItemBasePoint0{i}"), Flag: L(r, $"getItemFlagId0{i}"))).Where(s => s.W > 0 || s.Id > 0).ToList();
        long total = slots.Sum(s => s.W);
        string Slot((long Id, long Cat, long N, long W, long Flag) s)
        {
            string what = s.Id <= 0 || s.Cat == -1 ? "nothing" : $"{Category(s.Cat)} {s.Id} '{store.RowName(ParamOf(s.Cat), (int)s.Id)}' x{s.N}";
            string w = slots.Count > 1 || s.W != total ? $" {(total > 0 ? 100.0 * s.W / total : 0):0.#}%" : "";
            return what + w + (s.Flag > 0 ? $" slotFlag {s.Flag}" : "");
        }
        long flag = L(r, "getItemFlagId"), cumFlag = L(r, "cumulateNumFlagId"), cumMax = L(r, "cumulateNumMax");
        return $"{r.ID} '{store.RowName("ItemLotParam", r.ID)}' [{string.Join(" | ", slots.Select(Slot))}] flag {flag}" +
            (cumFlag > 0 ? $" cumulate {cumFlag}/{cumMax}" : "");
    }

    /// <summary>Every row of the lot's chain, one per line, prefixed; "LOT[missing]" when row N does not exist.</summary>
    public static IEnumerable<string> Describe(ParamStore store, int lotId, string indent)
    {
        var chain = Chain(store, lotId);
        if (chain.Count == 0) { yield return $"{indent}lot {lotId} [missing]"; yield break; }
        for (int i = 0; i < chain.Count; i++)
            yield return $"{indent}{(i == 0 ? "lot " : "  +")}{Row(store, chain[i])}";
    }
}
