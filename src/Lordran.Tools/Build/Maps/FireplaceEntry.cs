using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// The (temporary) entry to Lordran, the user's choice in session 4 while the Giant's Tree is on hold: walk up to the
/// fireplace inside the old women's house in Things Betwixt and "Peer into the fire?" (YES/NO); YES warps the player to
/// DS1's start point in the Undead Asylum cell. No NPC (the crones' presence depends on DS2's story state) and no object
/// (a new object made Things Betwixt reload endlessly).
///
/// Built from Things Betwixt's own event patterns: 140132 "player inside an EventLocation region" (as TB's region 500000),
/// 130441 YES/NO window (mapevent.fmg text), and DS2's map-to-map warp 132125 "warp the player via polydrama"(demo, 0,
/// map, EventLocation id, 0) that the Memories of the Giants use (see GiantTree). Changes: Things Betwixt's event ESD (one
/// new group), its EventParam (one row) and EventLocation (one region), and mapevent.fmg (one line).
/// </summary>
public static class FireplaceEntry
{
    public const string Map = "m10_02_00_00";
    const int Group = 37100;               // ESD group and EventParam row
    const int OwnerlessTemplateRow = 7000; // a map-level TB event (bootType 2, ownerType 0)
    const int RegionTemplateRow = 700000;  // a TB box region (formType 3, categoryType 10)
    public const int Region = 3710000;     // our EventLocation row in Things Betwixt
    public const int MessageId = 9900;     // mapevent.fmg (YES/NO texts live there: 1002 "Use %s?")
    const string Question = "Peer into the fire?";
    // In front of the fireplace (where the user stood in game, session 4): world (-185.52, 13.68, -153.20) minus Things
    // Betwixt's MapOffset (-498, 30, -260); EventLocation positions are map-local.
    static readonly Vector3 RegionPos = new(312.48f, -16.32f, 106.80f);
    static readonly Vector3 RegionSize = new(1.6f, 2.5f, 1.6f);

    public static void Build(Pack pack, MapBuilder.Options asylum, Vector3 cellPos, Vector3 cellRot)
    {
        string data = pack.Ds2Data;
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        PARAM Load(string name)
        {
            var p = PARAM.Read(Path.Combine(data, "param", $"{name.ToLowerInvariant()}_{Map}.param"));
            if (!p.ApplyParamdefCarefully(defs)) throw new InvalidDataException($"no paramdef fits {name}_{Map}");
            return p;
        }
        PARAM.Row AddRow(PARAM p, int templateId, int id)
        {
            if (p[id] != null) throw new InvalidDataException($"{p.ParamType} already has row {id}");
            var row = new PARAM.Row(p[templateId] ?? throw new InvalidDataException($"{p.ParamType} has no row {templateId}")) { ID = id };
            p.Rows.Add(row);
            p.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
            return row;
        }

        // ---- the region in front of the fireplace
        var locations = Load("EventLocation");
        var region = AddRow(locations, RegionTemplateRow, Region);
        region["PositionX"].Value = RegionPos.X; region["PositionY"].Value = RegionPos.Y; region["PositionZ"].Value = RegionPos.Z;
        region["ScaleX"].Value = RegionSize.X; region["ScaleY"].Value = RegionSize.Y; region["ScaleZ"].Value = RegionSize.Z;
        pack.AddFile("gamedata", $"param/EventLocation_{Map}.param", locations.Write());

        // ---- the script and the event that runs it
        var esd = ESD.Read(Path.Combine(data, "ezstate", $"event_{Map}.esd"));
        if (esd.StateGroups.ContainsKey(Group)) throw new InvalidDataException($"event_{Map} already has group {Group}");
        esd.StateGroups[Group] = PeerIntoTheFire(asylum.MapIdDecimal, GiantTree.EntryLocationId);
        pack.AddFile("gamedata", $"ezstate/event_{Map}.esd", esd.Write());
        var events = Load("EventParam");
        var ev = AddRow(events, OwnerlessTemplateRow, Group);
        ev["stateMapId"].Value = (uint)Group;
        pack.AddFile("gamedata", $"param/EventParam_{Map}.param", events.Write());

        if (pack.Text("mapevent").Entries.Any(e => e.ID == MessageId && !string.IsNullOrEmpty(e.Text)))
            throw new InvalidDataException($"mapevent.fmg already uses {MessageId}");
        pack.SetText("mapevent", MessageId, Question);

        GiantTree.AddEntryLocation(pack, asylum, cellPos, cellRot, defs);
        pack.Note($"Lordran entry: the fireplace in the old women's house, Things Betwixt (region {Region} at {RegionPos}, ESD group {Group}, mapevent {MessageId}) → {asylum.Ds2Map} location {GiantTree.EntryLocationId} at {cellPos}");
        Console.WriteLine($"Lordran entry: \"{Question}\" at the fireplace in Things Betwixt → {asylum.Ds2Map} (the cell, {cellPos})");
    }

    /// <summary>
    /// 0: wait until the player is inside the region (140132 … 1) → 1: YES/NO window (130441, mapevent text) → 2: YES → 4,
    /// otherwise 3: wait until the player has left the region (140132 … 0) → 0. 4: no multiplayer (130613) and warp
    /// (132125) → 5: done. Condition bytecode is Things Betwixt's own.
    /// </summary>
    static Dictionary<long, ESD.State> PeerIntoTheFire(int mapId, int locationId)
    {
        byte[] Group0 = Convert.FromHexString("82E12202004085A6A1");       // f140001(0): group condition 0 met
        byte[] WindowClosed = Convert.FromHexString("8276FD010084A64096A1"); // f130422() == 0: YES/NO window gone
        byte[] Cancelled = Convert.FromHexString("8275FD010084A743954096A1");
        byte[] No = Convert.FromHexString("AF42954096A1");
        byte[] Yes = Convert.FromHexString("AF41954096A1");
        byte[] WarpDone = Convert.FromHexString("820504020084A64096A1");
        ESD.CommandCall C(int id, params byte[][] args) => new(1, id, args);
        ESD.State S(IEnumerable<ESD.CommandCall> entry, params (byte[] If, long Then)[] conds)
        {
            var s = new ESD.State();
            s.EntryCommands.AddRange(entry);
            foreach (var (cond, then) in conds) s.Conditions.Add(new ESD.Condition(then, cond));
            return s;
        }
        return new Dictionary<long, ESD.State>
        {
            [0] = S([C(140132, Int(0), Int(Region), Int(Region), Int(1))], (Group0, 1)),
            // session 10 fix pass (asy-items): a text without a parameter = vanilla's (text, 0, -1, 0, 0, 0) (was (…, 4, 220,
            // 2, 0, 0): %s = the name of item 0, which vanilla never does; see OriginPrompt)
            [1] = S([C(130441, Int(MessageId), Int(0), Int(-1), Int(0), Int(0), Int(0))], (WindowClosed, 2)),
            [2] = S([], (Cancelled, 3), (No, 3), (Yes, 4)),
            [3] = S([C(140132, Int(0), Int(Region), Int(Region), Int(0))], (Group0, 0)),
            [4] = S([C(130613, Int(1)), C(132125, Int(0), Int(0), Int(mapId), Int(locationId), Int(0))], (WarpDone, 5)),
            [5] = S([]),
        };
    }

    static byte[] Int(int v) => v is >= 0 and < 64 ? [(byte)(0x40 + v), 0xA1] : [0x82, .. BitConverter.GetBytes(v), 0xA1];
}
