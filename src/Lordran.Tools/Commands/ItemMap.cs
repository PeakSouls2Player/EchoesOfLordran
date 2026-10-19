using System.Text;
using System.Text.RegularExpressions;
using Lordran.Tools.Formats;

namespace Lordran.Tools.Commands;

/// <summary>
/// item-map [--out FILE]
/// Builds the DS1 → DS2 item table. Per the "duplicates use DS2's version" decision, a DS1 base item
/// whose name matches a DS2 item of the same kind maps to that DS2 item; everything else is "new" and
/// will be added by the converters. Upgrade levels and infusion variants map through their base item.
/// </summary>
public static partial class ItemMap
{
    public const string DefaultOut = @"C:\EchoesOfLordran\data\lordran\item-map.tsv";

    enum Kind { Weapon, Armor, Ring, Spell, Goods }

    // DS1 param → item kind, and how to recognise the base row of an upgrade/infusion family.
    static readonly (string Param, Kind Kind, Func<int, bool> IsBase)[] Ds1Sources =
    {
        ("EquipParamWeapon", Kind.Weapon, id => id % 1000 == 0),     // +N = id+N, infusions = +100 steps
        ("EquipParamProtector", Kind.Armor, id => id >= 10000 && id % 1000 == 0), // +N = id+N; ids < 10000 are hair/face parts
        ("EquipParamAccessory", Kind.Ring, _ => true),
        ("Magic", Kind.Spell, _ => true),
        ("EquipParamGoods", Kind.Goods, _ => true),
    };

    static readonly HashSet<string> Ds1OnlineItems = new(StringComparer.OrdinalIgnoreCase)
    {
        "Red Eye Orb", "Blue Eye Orb", "Orange Guidance Soapstone", "Book of the Guilty", "Eye of Death",
        "Servant Roster", "Indictment", "Souvenir of Reprisal", "Dragon Scale", "Sunlight Medal",
        "Dried Finger", "Purple Coward's Crystal", "Cracked Red Eye Orb",
    };

    // DS1 mechanics that DS2's rules replace; each needs a deliberate DS2 translation, not a straight copy.
    static readonly HashSet<string> Ds1SystemItems = new(StringComparer.OrdinalIgnoreCase)
    {
        // session 9 (rule G1, user decision): "Estus Flask" has a DS2 namesake but its gifts become DS2's Estus Flask Shard,
        // so the system list is consulted before the name match (no other entry here has a DS2 namesake)
        "Estus Flask", "Estus Flask - Drained", "Humanity", "Twin Humanities", "Fire Keeper Soul", "Rite of Kindling",
        "Bottomless Box", "Weapon Smithbox", "Armor Smithbox", "Repairbox", "Lordvessel", "Transient Curse",
        "Purging Stone",
    };

    // Same item, renamed between the games (DS1 name → DS2 name).
    static readonly Dictionary<string, string> Renamed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Wooden Arrow"] = "Wood Arrow",
        // session 8 (track npc): DS2's Elite Knight set (model 1060, same design as DS1's 9500) names its arms piece
        // "Gloves"; ArmorPorter.Ds2Duplicate finds it at build time as well (set-checked), this keeps item-map.tsv in step
        ["Elite Knight Gauntlets"] = "Elite Knight Gloves",
        // session 10 (asy-items i2, RingPorter rule R0): DS1 spells it "Cloranthy" (EquipParamAccessory 104, "Boosts stamina
        // recovery speed"), DS2 "Chloranthy" (40020000, "Raises stamina recovery speed"); RingPorter.Ds2Duplicate also
        // finds it at build time, this keeps item-map.tsv in step when it is regenerated
        ["Cloranthy Ring"] = "Chloranthy Ring",
        // session 11 (track equip11, rules SP1 / GD1): same item, DS2 spelling. DS1 "Seek Guidance" (5400, reveals the
        // hidden help messages) is DS2's miracle "Guidance" (32220000); DS1 "Elizabeth's Mushroom" (230, HP regeneration) is
        // DS2's "Elizabeth Mushroom" (60035000). SpellPorter.NotPorted / GoodsPorter.Ds2Duplicate also check at build time
        ["Seek Guidance"] = "Guidance",
        ["Elizabeth's Mushroom"] = "Elizabeth Mushroom",
    };

    /// <summary>The DS2 name of a DS1 item that DS2 renamed (else the name itself). Session 10 (RingPorter R0).</summary>
    public static string RenamedName(string ds1Name) => Renamed.GetValueOrDefault(ds1Name, ds1Name);

    /// <summary>Non-"new" handling for DS1 goods, or null if the item is simply added.</summary>
    static string? GoodsCategory(int id, string name) => id switch
    {
        >= 3000 and < 6000 => "via-spell",      // inventory side of a spell; follows the Magic mapping (same id)
        >= 9000 and < 10000 => "gesture-page",  // session 11 (gest11): DS1 gestures are no DS2 items: the loader's Lordran gesture page + flags (Build/Chr/Ds1Gestures, rules GS1-GS4)
        >= 800 and < 1200 => "system",          // embers and DS1-only titanite: map onto DS2's upgrade materials
        _ when Ds1OnlineItems.Contains(name) => "decide-online",
        _ when Ds1SystemItems.Contains(name) => "system",
        _ => null,
    };

    public static int Run(string[] args)
    {
        string outPath = args.Length > 2 && args[1] == "--out" ? args[2] : DefaultOut;
        var systemItems = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Mappings", "system-items.tsv")).Skip(1)
            .Select(l => l.Split('\t'))
            .ToDictionary(c => c[0], c => (Action: c[1], Ds2Id: c[2], Ds2Name: c[3]), StringComparer.OrdinalIgnoreCase);
        var dsr = ParamStore.LoadDsr(Program.DefaultDsrRoot);
        var ds2 = ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);

        // DS2: every obtainable thing is an ItemParam row; its kind is whichever sub-param it points at.
        var ds2ByKind = new Dictionary<Kind, Dictionary<string, (int Id, string Name)>>();
        foreach (Kind k in Enum.GetValues<Kind>()) ds2ByKind[k] = new();
        foreach (var row in ds2.Params["ItemParam"].Rows)
        {
            string name = ds2.RowName("ItemParam", row.ID);
            if (name.Length == 0 || name.StartsWith("NPC") || name.StartsWith("Test") || name.StartsWith("Unauthorized"))
                continue;
            // Ammunition (arrowParamId) counts as a weapon: DS1 keeps arrows and bolts in EquipParamWeapon.
            Kind kind = (int)row["weaponParamId"].Value != -1 || (int)row["arrowParamId"].Value != -1 ? Kind.Weapon
                : (int)row["armorParamId"].Value != -1 ? Kind.Armor
                : (int)row["ringParamId"].Value != -1 ? Kind.Ring
                : (int)row["spellParamId"].Value != -1 ? Kind.Spell
                : Kind.Goods;
            ds2ByKind[kind].TryAdd(Normalize(name), (row.ID, name)); // first (lowest) id wins
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        var tsv = new StringBuilder("ds1_param\tds1_id\tds1_name\tkind\tresult\tds2_item_id\tds2_name\n");
        foreach (var (param, kind, isBase) in Ds1Sources)
        {
            int shared = 0, added = 0;
            foreach (var row in dsr.Params[param].Rows)
            {
                string name = dsr.RowName(param, row.ID);
                if (!isBase(row.ID) || name.Length == 0 || name.StartsWith("Unused") || name.Contains('[') || name.Contains('+'))
                    continue;
                if (kind == Kind.Goods && Ds1SystemItems.Contains(name) && systemItems.TryGetValue(name, out var sys0))
                    tsv.Append($"{param}\t{row.ID}\t{name}\t{kind}\tsystem-{sys0.Action}\t{sys0.Ds2Id}\t{sys0.Ds2Name}\n");
                else if (ds2ByKind[kind].TryGetValue(Normalize(Renamed.GetValueOrDefault(name, name)), out var match))
                {
                    tsv.Append($"{param}\t{row.ID}\t{name}\t{kind}\tshared\t{match.Id}\t{match.Name}\n");
                    shared++;
                }
                else if (kind == Kind.Goods && GoodsCategory(row.ID, name) is string category)
                {
                    // session 11 (cov11): an online item with a system-items.tsv row is decided (DESIGN 3.4: 1 map, 1 new, 8 drop)
                    if (category is "system" or "decide-online" && systemItems.TryGetValue(name, out var sys))
                        tsv.Append($"{param}\t{row.ID}\t{name}\t{kind}\tsystem-{sys.Action}\t{sys.Ds2Id}\t{sys.Ds2Name}\n");
                    else
                        tsv.Append($"{param}\t{row.ID}\t{name}\t{kind}\t{category}\t\t\n");
                }
                else
                {
                    tsv.Append($"{param}\t{row.ID}\t{name}\t{kind}\tnew\t\t\n");
                    added++;
                }
            }
            Console.WriteLine($"{param,-22} {kind,-7} shared with DS2: {shared,4}   new: {added,4}");
        }
        File.WriteAllText(outPath, tsv.ToString());
        Console.WriteLine($"wrote {outPath}");
        return 0;
    }

    static string Normalize(string name) => NonAlnum().Replace(name.ToLowerInvariant(), "");

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex NonAlnum();
}
