using SoulsFormats;

namespace Lordran.Tools.Formats;

/// <summary>
/// DS1 (DSR, English) text tables with their identity kept: which msgbnd (item/menu), BND file id and FMG name each
/// entry came from. Session 8 (census tool gap): DS1 text ids collide across FMGs (blood-writing tutorial message 9000,
/// item names, conversation lines and event texts share number ranges), so a lookup must name its FMG.
/// Evidence: DSR's menu.msgbnd.dcx holds 23 FMGs and item.msgbnd.dcx 36; the PTDE patch ids 100-124 repeat the base
/// files 1-92 under the same names (e.g. menu 1 and 104 are both Conversation_.fmg, 283,968 bytes). Identical
/// duplicates are folded into one table that lists both ids ("menu 1/104").
/// </summary>
public sealed class Ds1Fmgs
{
    /// <summary>One FMG: msgbnd ("item"/"menu"), the BND file ids holding it, the file name stem and the table.</summary>
    public sealed record Table(string Bnd, List<int> Ids, string Name, FMG Fmg, Dictionary<int, string> ById)
    {
        public string Label => $"{Name} ({Bnd} {string.Join("/", Ids)})";
        public string? Get(int id) => ById.TryGetValue(id, out var t) && !string.IsNullOrEmpty(t) ? t : null;
    }

    /// <summary>Tables in Ds1Source.AnyText's search order: item.msgbnd first, then menu.msgbnd, each in BND order.</summary>
    public List<Table> Tables { get; } = [];

    /// <summary>
    /// Friendly categories → FMG name stems. The stems are DSR's English file names (msg\ENGLISH\*.msgbnd.dcx).
    /// </summary>
    public static readonly Dictionary<string, string[]> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["talk"] = ["Conversation_"], ["conversation"] = ["Conversation_"],
        ["blood"] = ["Blood_writing_"], ["message"] = ["Blood_writing_"], ["msg"] = ["Blood_writing_"], ["tutorial"] = ["Blood_writing_"],
        ["event"] = ["Event_text_"], ["subtitle"] = ["Movie_subtitles_"], ["movie"] = ["Movie_subtitles_"],
        ["dialogue"] = ["Dialogue_"], ["dialog"] = ["Dialogue_"], ["system"] = ["System_message_win32_"],
        ["keyguide"] = ["Key_guide_"], ["help"] = ["Single_line_help_", "Item_help_"],
        ["menu"] = ["Ingame_menu_", "Menu_general_text_", "Menu_others_"], ["tags"] = ["Text_display_tag_list_", "System_specific_tags_win32_"],
        ["npc"] = ["NPC_name_"], ["boss"] = ["NPC_name_"], ["place"] = ["Place_name_"], ["area"] = ["Place_name_"],
        ["goods"] = ["Item_name_"], ["item"] = ["Item_name_"], ["weapon"] = ["Weapon_name_"], ["armor"] = ["Armor_name_"],
        ["protector"] = ["Armor_name_"], ["ring"] = ["Accessory_name_"], ["accessory"] = ["Accessory_name_"],
        ["magic"] = ["Magic_name_"], ["spell"] = ["Magic_name_"], ["feature"] = ["Feature_name_"],
        ["names"] = ["Item_name_", "Weapon_name_", "Armor_name_", "Accessory_name_", "Magic_name_", "Feature_name_", "NPC_name_", "Place_name_"],
        ["desc"] = ["Item_description_", "Weapon_description_", "Armor_description_", "Accessory_description_", "Magic_description_", "Feature_description_"],
        ["long"] = ["Item_long_desc_", "Weapon_long_desc_", "Armor_long_desc_", "Accessory_long_desc_", "Magic_long_desc_", "Feature_long_desc_"],
    };

    static readonly Dictionary<string, Ds1Fmgs> cache = new(StringComparer.OrdinalIgnoreCase);

    public static Ds1Fmgs Load(string dsrRoot, string language = "ENGLISH")
    {
        string key = dsrRoot + "|" + language;
        if (!cache.TryGetValue(key, out var fmgs)) cache[key] = fmgs = new Ds1Fmgs(dsrRoot, language);
        return fmgs;
    }

    Ds1Fmgs(string root, string language)
    {
        foreach (string bnd in new[] { "item", "menu" })
            foreach (var f in BND3.Read(Path.Combine(root, "msg", language, bnd + ".msgbnd.dcx")).Files.OrderBy(f => f.ID))
            {
                string name = Path.GetFileNameWithoutExtension(f.Name);
                var fmg = FMG.Read(f.Bytes);
                var byId = new Dictionary<int, string>();
                foreach (var e in fmg.Entries) byId.TryAdd(e.ID, e.Text);
                var same = Tables.FirstOrDefault(t => t.Bnd == bnd && t.Name == name && SameEntries(t.ById, byId));
                if (same != null) same.Ids.Add(f.ID);
                else Tables.Add(new Table(bnd, [f.ID], name, fmg, byId));
            }
    }

    static bool SameEntries(Dictionary<int, string> a, Dictionary<int, string> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var t) && t == kv.Value);

    /// <summary>
    /// Tables selected by a category ("talk", "blood", "event"…), an FMG name ("Blood_writing", substring, any case),
    /// a BND id ("107", "menu:30") or null (all). Unknown selectors return an empty list.
    /// </summary>
    public List<Table> Select(string? selector)
    {
        if (string.IsNullOrEmpty(selector)) return Tables;
        if (Categories.TryGetValue(selector, out var stems))
            return Tables.Where(t => stems.Contains(t.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        string bnd = "";
        string s = selector;
        int colon = s.IndexOf(':');
        if (colon > 0) { bnd = s[..colon]; s = s[(colon + 1)..]; }
        if (int.TryParse(s, out int id))
            return Tables.Where(t => t.Ids.Contains(id) && (bnd == "" || t.Bnd.Equals(bnd, StringComparison.OrdinalIgnoreCase))).ToList();
        return Tables.Where(t => t.Name.Contains(s.Trim('_'), StringComparison.OrdinalIgnoreCase)
            && (bnd == "" || t.Bnd.Equals(bnd, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>Every table with a non-empty text for the id, in search order.</summary>
    public IEnumerable<(Table Table, string Text)> Hits(int id, IEnumerable<Table>? tables = null)
    {
        foreach (var t in tables ?? Tables)
            if (t.Get(id) is { } text) yield return (t, text);
    }

    /// <summary>The text of an id in one category/FMG (first table that has it), or null.</summary>
    public string? Get(string selector, int id) => Hits(id, Select(selector)).Select(h => h.Text).FirstOrDefault();

    /// <summary>One line for console output.</summary>
    public static string OneLine(string text) => text.Replace("\r", "").Replace("\n", " / ");
}
