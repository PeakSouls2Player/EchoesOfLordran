using System.Numerics;
using System.Text.RegularExpressions;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Session 10 (asy-items), rules M0-M4 (spec data\sandbox\asy10\messages-items.md, revision 2): DS1's developer ("orange
/// soapstone") messages as DS2 messages.
///
/// DS1 places them as MSB1 Event.Message entries (FMG id into menu Blood_writing_, the region's position/rotation,
/// Hidden = Seek Guidance only, an entity when an event toggles it with `2003:26 Set Visibility of Message`); DSR has
/// 139 in its game maps, 34 hidden, 8 with an entity (all in the Asylum), visible at every load until an event hides
/// them. DS2 has no MSB message type: it places developer messages as **EventLocation rows** of the map; the row's
/// categoryType picks the class (V-code, FUN_140476ec0 l.880157-880318: 12 → MapGeneralBloodMsgLocation created
/// enabled; 13 → the same, disabled; 22 → MapGeneralAdviceBloodMsgLocation, disabled, switched on only while a session
/// member has SpEffect 32220000 Guidance or 21670100 Hollow Skin: FUN_1404797b0 l.882030; 19/20 → prohibited area);
/// categoryId = the id in the map's own `menu/text/<lang>/bloodmes/<map>.fmg` (loaded per map, l.985411). Rules:
///  - **M0 (V-code + V-data)**: a row registers only while the player stands on collision of the same memory block
///    (row byte +0x2A == the collision's MSB2 UnkT12, FUN_1404764b0 → FUN_1401eab20) → memoryBlockId = the block of
///    the collision under the message (the pack's MSB2 + collision, <see cref="FloorProbe"/>); our maps: 1. The
///    templates' memoryBlockId (2/3/6) is never copied;
///  - **M1 (mechanism V; display P)**: every visible DS1 message → a categoryType <see cref="VisibleCategory"/> (12) row,
///    id 30000 + 10k (k = index among the map's visible messages in MSB order), cloned from vanilla m10_15 row 30000
///    (formType 3, 1×1×1), at the DS1 region's position with its rotation (RotationY: P, test T-M1), categoryId = DS1
///    MessageID, the text in the map's bloodmes FMG;
///  - **M0b (P)**: the row sits on the DS2 floor under the DS1 point (DS1 X/Z, the floor's Y);
///  - **M1b (P)**: a categoryType 19 box, id 45000 + 10k, 2.5×2×3 like vanilla m10_10 45000, around each (players' own
///    messages can't cover it), same memory block;
///  - **M2 (args V-code; P in game)**: a message DS1's events toggle is the same cat 12 row (visible at load = DS1);
///    `2003:26(entity, s)` → 132221(row, s ? 1 : 0) (handler 0x140468E60: row id, state → FUN_1401ead10; the bit is
///    rebuilt from the category at every map load, as DS1's MSB visibility) — EmevdTranslator via IContext.MessageLocation;
///  - **M3 (P)**: DS1's button tags → DS2's button glyphs (<see cref="Glyphs"/>); an unknown tag fails the build;
///  - **M4 (V-code + V-data; default OFF = user decision, to re-ask)**: a hidden DS1 message → categoryType 22 (DS2's own
///    Guidance / Hollow Skin reveal), id 40000 + 10h (h = index among the hidden ones), cloned from m10_10 row 40000;
///    build-pack --ds1-hidden-messages. Off: the build log lists them as left out.
/// Generalises to every DS1 map (pure MSB + EMEVD data; ≤ 73 visible / 34 hidden messages per map, so 30000/40000/45000/
/// 47000 + 10k never collide with each other or our 37xxxxx rows).
/// </summary>
public static class BloodMessages
{
    public const int VisibleBase = 30000, HiddenBase = 40000, BoxBase = 45000, HiddenBoxBase = 47000;
    /// <summary>EventLocation categoryType of visible DS1 messages (M1/M2; build-pack --msg-cat N for an A/B).</summary>
    public static int VisibleCategory { get; set; } = 12;
    public const int HiddenCategory = 22, ProhibitedCategory = 19;
    /// <summary>build-pack --ds1-hidden-messages (rule M4): place DS1's hidden messages as Guidance-only rows.</summary>
    public static bool HiddenMessages { get; set; }
    /// <summary>build-pack --no-blood-messages: no messages placed (the translator's 2003:26 become no-ops again).</summary>
    public static bool Enabled { get; set; } = true;
    static readonly Vector3 BoxSize = new(2.5f, 2f, 3f);

    /// <param name="K">index among the map's visible (or, for hidden ones, hidden) messages in MSB order</param>
    /// <param name="Toggled">a DS1 event switches it (2003:26 on its entity)</param>
    public sealed record Message(int K, int Id, int Entity, Vector3 Pos, Vector3 Rot, bool Hidden, bool Toggled, string Name)
    {
        public int Location => (Hidden ? HiddenBase : VisibleBase) + 10 * K;
        public int Box => (Hidden ? HiddenBoxBase : BoxBase) + 10 * K;
        public int Category => Hidden ? HiddenCategory : VisibleCategory;
        /// <summary>This build places it (visible ones always, hidden ones with rule M4 on).</summary>
        public bool Placed => !Hidden || HiddenMessages;
    }

    /// <summary>The map's DS1 messages (pure: MSB + the map's EMEVD), numbered in MSB order (visible and hidden apart).</summary>
    public static List<Message> Collect(MSB1 msb1, EMEVD? emevd)
    {
        var defs = Commands.EmevdDump.Defs();
        var toggled = emevd == null ? [] : emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2003 && i.ID == 26)
            .Select(i => Events.EmevdTranslator.Decode(defs, i)).Where(v => v.Length > 0).Select(v => (int)v[0]).ToHashSet();
        var list = new List<Message>();
        int k = 0, h = 0;
        foreach (var m in msb1.Events.Messages)
        {
            var r = msb1.Regions.GetEntries().FirstOrDefault(x => x.Name == m.RegionName);
            if (r == null) continue;
            bool t = m.EntityID > 0 && toggled.Contains(m.EntityID);
            list.Add(new Message(m.Hidden ? h++ : k++, m.MessageID, m.EntityID, r.Position, r.Rotation, m.Hidden, t, m.Name));
        }
        return list;
    }

    static readonly Dictionary<string, List<Message>> cache = [];
    /// <summary><see cref="Collect"/> for a DS1 map of the DSR install (cached; empty with --no-blood-messages).</summary>
    public static List<Message> Of(string dsrRoot, string ds1Map)
    {
        if (!Enabled) return [];
        if (cache.TryGetValue(ds1Map, out var l)) return l;
        string ev = Path.Combine(dsrRoot, "event", ds1Map + ".emevd.dcx");
        return cache[ds1Map] = Collect(MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", ds1Map + ".msb")), File.Exists(ev) ? EMEVD.Read(ev) : null);
    }

    /// <summary>Rule M2: the EventLocation row of a DS1 message entity this build places, or null.</summary>
    public static int? Location(string dsrRoot, string ds1Map, int entity) =>
        entity > 0 && Of(dsrRoot, ds1Map).FirstOrDefault(m => m.Entity == entity && m.Placed) is { } m ? m.Location : null;

    /// <summary>
    /// Rule M3: DS1 tag → DS2 glyph(s). DS2's pad texts (mapevent 3000-3020) use ⑦ attack R, ⑧ strong R, ⑨ attack L,
    /// ⑩ strong L, ⑪ lock-on, ⑯ menu, ⑰ camera, ⑱ move, ⑳ dash/backstep/roll, ② use item, ① two-hand, ㊤/㊦/㊧/㊨ d-pad;
    /// keyguide.fmg renders the same glyphs for pad and keyboard ("⑲:Read message" 706). DS1's `openGesture` has no
    /// DS2 button (DS2 opens gestures from the menu: ingamesystem 10331 "Open Gesture Menu") → the words (P).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Glyphs = new Dictionary<string, string>
    {
        ["attackR"] = "⑦", ["subAttackR"] = "⑧", ["attackL"] = "⑨", ["subAttackL"] = "⑩", ["spinReset"] = "⑪",
        ["cameraMove"] = "⑰", ["playerMove"] = "⑱", ["dush"] = "⑳", ["startMenuSwitch"] = "⑯", ["startItem"] = "②",
        ["changeEquStyle"] = "①", ["changeMagic"] = "㊤", ["chamgeItem"] = "㊦", ["changeArmL"] = "㊧", ["changeArmR"] = "㊨",
        ["selectUD"] = "㊤㊦", ["openGesture"] = "Gesture Menu",
    };

    /// <summary>Rule M3 over a DS1 text; throws on a tag without a rule.</summary>
    public static string ToDs2Glyphs(string ds1Text) => Regex.Replace(ds1Text, @"<\?([A-Za-z0-9_@]+)\?>", m =>
        Glyphs.TryGetValue(m.Groups[1].Value, out var g) ? g : throw new InvalidDataException($"DS1 message tag <?{m.Groups[1].Value}?> has no DS2 glyph rule (BloodMessages.Glyphs)"));

    /// <summary>
    /// Writes the map's message rows (M0/M1/M1b/M4) into its EventLocation param and the texts into the bloodmes FMG the
    /// map builder writes (after the map's MSB and collision: M0 reads them); returns the build log lines.
    /// </summary>
    public static List<string> Build(Pack pack, MapBuilder.Options o, Ds1Source ds1, List<PARAMDEF> defs, FMG bloodmes)
    {
        var log = new List<string>();
        var msgs = Of(ds1.Root, o.Ds1Map);
        if (msgs.Count == 0) return log;
        var p = MapLinks.Locations(pack, o, defs);
        var visible = MapLinks.Template(pack, defs, "m10_15_00_00", 30000);
        var hidden = MapLinks.Template(pack, defs, "m10_10_00_00", 40000);
        var box = MapLinks.Template(pack, defs, "m10_10_00_00", 45000);
        // M0: the memory block of the collision under each message (MSB2 Collision.UnkT12)
        var msb2 = MSB2.Read(pack.GetFile("gamedata", $"map/{o.Ds2Map}/{o.Ds2Map}.msb"));
        var blockOf = msb2.Parts.Collisions.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => (int)g.First().UnkT12);
        int common = msb2.Parts.Collisions.GroupBy(c => (int)c.UnkT12).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault(1);
        var probe = FloorProbe.FromPack(pack, o.Ds2Map);
        foreach (var m in msgs)
        {
            string text = ds1.Text("Blood_writing_", m.Id);
            if (!m.Placed) { log.Add($"{o.Ds2Map}: DS1 message {m.Id} \"{text}\" ({m.Name}) is hidden (Seek Guidance): left out (rule M4 off; --ds1-hidden-messages)"); continue; }
            if (string.IsNullOrEmpty(text)) throw new InvalidDataException($"{o.Ds1Map}: DS1 message {m.Id} has no Blood_writing_ text");
            string ds2Text = ToDs2Glyphs(text);
            bloodmes.Entries.RemoveAll(e => e.ID == m.Id);
            bloodmes.Entries.Add(new FMG.Entry(m.Id, ds2Text));
            var floor = probe.Floor(m.Pos, above: 0.6f, below: 3f);
            int block = floor is { } f && blockOf.TryGetValue(f.Part, out int b) ? b : common;
            // M0b (P): on the DS2 floor under the DS1 point (DS1's region height can differ from our converted collision by
            // a few cm: 9003 lay 0.11 m under it), DS1's X/Z kept
            var pos = floor is { } fl0 ? m.Pos with { Y = fl0.Y } : m.Pos;
            MapLinks.Put(p, m.Hidden ? hidden : visible, m.Location, pos, m.Rot);
            var row = p[m.Location]!;
            Set(row, "categoryType", m.Category);
            Set(row, "categoryId", m.Id);
            Set(row, "memoryBlockId", block);
            MapLinks.Put(p, box, m.Box, pos - new Vector3(0, 0.5f, 0), Vector3.Zero, BoxSize);
            Set(p[m.Box]!, "memoryBlockId", block);
            log.Add($"{o.Ds2Map}: DS1 message {m.Id} → EventLocation {m.Location} (category {m.Category}, memory block {block}{(floor is { } fl ? $" of {fl.Part}" : ": no floor found, the map's common block")}{(m.Toggled ? $", toggled by DS1 events: entity {m.Entity}" : "")}) + box {m.Box} at {pos} (DS1 y {m.Pos.Y}) rot {m.Rot.Y}: \"{ds2Text.Replace("\n", " / ")}\"");
        }
        bloodmes.Entries.Sort((a, b) => a.ID.CompareTo(b.ID));
        pack.AddFile("gamedata", $"param/eventlocation_{o.Ds2Map}.param", p.Write());
        return log;
    }

    static void Set(PARAM.Row row, string field, int v) => row[field].Value = Convert.ChangeType(v, row[field].Value.GetType());
}
