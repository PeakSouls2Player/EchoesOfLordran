using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// The entry to Lordran: a Giant's Tree in Things Betwixt (m10_02), past the three old women near the first bonfire.
/// Touching it starts DS1 from its first moment: the player is warped into the Undead Asylum cell (DS1's own start point).
///
/// It is DS2's Memory-of-the-Giants entrance, cloned from the Forest of Fallen Giants (session 4 RE):
///  - object o00_9070 (a giant grown into a tree; MapObjectInstanceParam objType 1607) with its instance row;
///  - EventParam row 37000 (bootType 2, ownerType 1, owner = the tree's instance id) running ESD group 37000, which calls
///    FoFG's memory routine: 131652 (show the tree's key guide) → 140205 "player examines the object" (+ multiplayer and
///    Ashen Mist Heart checks) → 130613 (no multiplayer) → 132125 "warp the player via polydrama"(demo, 0, map, location, 0);
///  - the destination is an EventLocation row of the target map (here m18_01_00_00, row EntryLocationId at the cell).
/// Changes from FoFG's copy: no Ashen Mist Heart check (Lordran is open from the start), and FoFG's own memory flags
/// (105300-105302) are not touched. Vanilla Things Betwixt files round-trip through SoulsFormats without content changes
/// (MSB byte-identical; ESD layout-only; per-map params lose only a 4-12 byte trailer, which the game doesn't need).
/// </summary>
public static class GiantTree
{
    public const string Map = "m10_02_00_00";
    const string SourceMap = "m10_10_00_00";
    const int SourceInstance = 10101520;      // FoFG's Memory of Vammar tree (o00_9070_0001)
    const int SourceGroup = 37010;            // its EventParam row / ESD group
    public const int TreeInstance = 10021800; // our tree in Things Betwixt (TB already uses 10021510)
    public const int Group = 37000;
    public const int EntryLocationId = 3700000; // EventLocation row in the Asylum: the cell
    const int DemoId = 0;                       // polydrama played on entry (FoFG: 101050 "Entering Memory of Vammar"); 0 = none
    // Near Fire Keepers' Dwelling (bonfire at -179.6, 12.0, -140.4; the old women's house at -191, 15.6, -153.8): on the far
    // side of the bonfire from the house.
    static readonly Vector3 TreePos = new(-172.5f, 12.0f, -132.5f);
    static readonly Vector3 TreeRot = new(0, 200, 0);

    // FoFG's tree object o00_9070 has no textures of its own: its materials use the Forest of Fallen Giants' *map* textures
    // (m10_10_00_00\tex\tree_11_d …), which never load in another map. Placed in Things Betwixt as is, the map load never
    // finishes and restarts every ~32 s (session 4). So the tree is a self-contained copy, o00_9071, carrying its textures
    // like the bonfire's archive does (raw NAME.tpf entries + ACB Texture assets). FoFG's own tree stays untouched.
    const string SourceModel = "o00_9070", TreeModel = "o00_9071";

    public static void Build(Pack pack, MapBuilder.Options asylum, Vector3 cellPos, Vector3 cellRot)
    {
        string data = pack.Ds2Data;
        byte[] obj = BuildTreeObject(data);
        pack.AddFile("gamedata", $"model/obj/{TreeModel}.bnd", obj);
        pack.AddFile("objlq", $"model_lq/obj/{TreeModel}.bnd", obj); // the low-quality path (FUN_1404904d0) uses the same object

        // ---- the tree object in Things Betwixt's MSB
        var msb = MSB2.Read(Path.Combine(data, "map", Map, Map + ".msb"));
        var src = MSB2.Read(Path.Combine(data, "map", SourceMap, SourceMap + ".msb"));
        var srcTree = src.Parts.Objects.First(o => o.MapObjectInstanceParamID == SourceInstance);
        if (!msb.Models.Objects.Any(m => m.Name == TreeModel))
            msb.Models.Objects.Add(new MSB2.Model.Object { Name = TreeModel });
        var tree = (MSB2.Part.Object)srcTree.DeepCopy();
        tree.ModelName = TreeModel;
        tree.Name = $"{TreeModel}_0000";
        tree.Position = TreePos;
        tree.Rotation = TreeRot;
        tree.MapObjectInstanceParamID = TreeInstance;
        Array.Fill(tree.DrawGroups, 0xFFFFFFFF); // visible from every collision (see MapBuilder: object draw groups)
        Array.Fill(tree.DispGroups, 0xFFFFFFFF);
        msb.Parts.Objects.Add(tree);
        pack.AddFile("gamedata", $"map/{Map}/{Map}.msb", msb.Write());

        // ---- per-map params: the tree's instance row and the event that watches it
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        PARAM Load(string name, string map)
        {
            var p = PARAM.Read(Path.Combine(data, "param", $"{name.ToLowerInvariant()}_{map}.param"));
            if (!p.ApplyParamdefCarefully(defs)) throw new InvalidDataException($"no paramdef fits {name}_{map}");
            return p;
        }
        void AddRow(PARAM p, PARAM.Row template, int id, Action<PARAM.Row> edit)
        {
            if (p[id] != null) throw new InvalidDataException($"{p.ParamType} already has row {id}");
            var row = new PARAM.Row(template) { ID = id };
            edit(row);
            p.Rows.Add(row);
            p.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
        }
        var inst = Load("MapObjectInstanceParam", Map);
        AddRow(inst, Load("MapObjectInstanceParam", SourceMap)[SourceInstance], TreeInstance, _ => { });
        pack.AddFile("gamedata", $"param/MapObjectInstanceParam_{Map}.param", inst.Write());
        var events = Load("EventParam", Map);
        AddRow(events, Load("EventParam", SourceMap)[SourceGroup], Group, r => { r["stateMapId"].Value = (uint)Group; r["ownerId"].Value = (uint)TreeInstance; });
        pack.AddFile("gamedata", $"param/EventParam_{Map}.param", events.Write());

        // ---- the script: FoFG's memory routine, renumbered into Things Betwixt's event ESD
        var esd = ESD.Read(Path.Combine(data, "ezstate", $"event_{Map}.esd"));
        var srcEsd = ESD.Read(Path.Combine(data, "ezstate", $"event_{SourceMap}.esd"));
        if (esd.StateGroups.ContainsKey(Group)) throw new InvalidDataException($"event_{Map} already has group {Group}");
        // Subroutine groups are 0x7FFFFFxx ids called with command bank 6; copy the call tree below Things Betwixt's lowest.
        var srcTop = srcEsd.StateGroups[SourceGroup];
        var tree6 = new List<long>();
        void Collect(Dictionary<long, ESD.State> g)
        {
            foreach (var c in g.Values.SelectMany(AllCommands).Where(c => c.CommandBank == 6))
                if (!tree6.Contains(c.CommandID)) { tree6.Add(c.CommandID); Collect(srcEsd.StateGroups[c.CommandID]); }
        }
        Collect(srcTop);
        long next = esd.StateGroups.Keys.Where(k => k > 0x7F000000).DefaultIfEmpty(0x80000000L).Min() - 1;
        var renumber = tree6.ToDictionary(id => id, _ => next--);
        Dictionary<long, ESD.State> Copy(Dictionary<long, ESD.State> g) => g.ToDictionary(kv => kv.Key, kv => CopyState(kv.Value, renumber));
        foreach (var (oldId, newId) in renumber) esd.StateGroups[newId] = Copy(srcEsd.StateGroups[oldId]);
        var top = Copy(srcTop);
        esd.StateGroups[Group] = top;

        // Arguments of the call into the memory routine: (object, demo, 0, map, location, flag).
        var call = top.Values.SelectMany(s => s.EntryCommands).Single(c => c.CommandBank == 6);
        int mapIdDecimal = asylum.MapIdDecimal;
        call.Arguments = [Int(TreeInstance), Int(DemoId), Int(0), Int(mapIdDecimal), Int(EntryLocationId), Int(0)];

        // No Ashen Mist Heart: in the "examined" check (group calling 140801 = item check), go straight to "enter".
        var check = esd.StateGroups[renumber.First(kv => srcEsd.StateGroups[kv.Key].Values.SelectMany(AllCommands).Any(c => c.CommandBank == 1 && c.CommandID == 140801)).Value];
        long itemState = check.First(s => s.Value.EntryCommands.Any(c => c.CommandID == 140801)).Key;
        long enterState = check.First(s => s.Value.Conditions.Any(c => c.TargetState == null && c.PassCommands.Any(p => p.CommandBank == 7 && p.Arguments.Count == 1 && p.Arguments[0].SequenceEqual(Int(0))))).Key;
        foreach (var s in check.Values)
            foreach (var c in s.Conditions.Where(c => c.TargetState == itemState))
                c.TargetState = enterState;
        // FoFG's memory flags: the entry routine sets the caller's flag and resets/waits on 105300-105302. Those states
        // (only 130101 "set flag" / 140301 "flag condition" commands) are bypassed: every transition into one goes on to
        // the state it would have led to.
        var enter = esd.StateGroups[renumber.First(kv => srcEsd.StateGroups[kv.Key].Values.SelectMany(AllCommands).Any(c => c.CommandBank == 6 && srcEsd.StateGroups[c.CommandID].Values.SelectMany(AllCommands).Any(x => x.CommandID == 132125))).Value];
        bool FlagOnly(long id) => enter[id].EntryCommands.Count > 0 && enter[id].EntryCommands.All(c => c.CommandBank == 1 && c.CommandID is 130101 or 140301);
        long Resolve(long id) { for (int guard = 0; FlagOnly(id) && guard < 16; guard++) id = enter[id].Conditions[0].TargetState!.Value; return id; }
        foreach (var s in enter.Values)
            foreach (var c in s.Conditions.Where(c => c.TargetState != null))
                c.TargetState = Resolve(c.TargetState!.Value);
        pack.AddFile("gamedata", $"ezstate/event_{Map}.esd", esd.Write());

        AddEntryLocation(pack, asylum, cellPos, cellRot, defs);

        pack.Note($"Giant's Tree: {tree.Name} at {TreePos} in {Map} (instance {TreeInstance}, event {Group}, subroutines {string.Join(",", renumber.Values)}) → {asylum.Ds2Map} location {EntryLocationId} at {cellPos}");
        Console.WriteLine($"Giant's Tree in Things Betwixt at {TreePos} → {asylum.Ds2Map} (the cell, {cellPos})");
    }

    /// <summary>
    /// The warp destination: EventLocation row EntryLocationId in the Asylum at DS1's start point (the cell), a point
    /// location (formType 1) like the memories' arrival points. MapBuilder writes the Asylum's EventLocation empty
    /// (SoulsFormats can't read a row-less param back), so it is rebuilt from the template map's, cleared.
    /// </summary>
    public static void AddEntryLocation(Pack pack, MapBuilder.Options asylum, Vector3 cellPos, Vector3 cellRot, List<PARAMDEF> defs)
    {
        PARAM Load(string map)
        {
            var p = PARAM.Read(Path.Combine(pack.Ds2Data, "param", $"eventlocation_{map}.param"));
            if (!p.ApplyParamdefCarefully(defs)) throw new InvalidDataException($"no paramdef fits EventLocation_{map}");
            return p;
        }
        var memory = Load("m20_10_00_00")[3700000];
        // session 10 (asy-items): the rows written so far are kept (MapBuilder's blood messages, BloodMessages); this used to
        // start from the template map's param, cleared, and so wiped them (MapLinks.Locations: the pack's param or, while
        // it is still row-less, the template's cleared)
        var loc = MapLinks.Locations(pack, asylum, defs);
        loc.Rows.RemoveAll(x => x.ID == EntryLocationId || x.ID == EntryLocationId + 10);
        var r = new PARAM.Row(memory) { ID = EntryLocationId };
        r["PositionX"].Value = cellPos.X; r["PositionY"].Value = cellPos.Y; r["PositionZ"].Value = cellPos.Z;
        // session 9 (track npcplace), rule P1: DS1's start point rot 180 faces the cell door (+Z) in DS1; DS2's yaw θ faces
        // compass θ, so the DS2 yaw is DS1's + 180
        r["RotationX"].Value = cellRot.X; r["RotationY"].Value = Ds1Placement.Yaw(cellRot.Y); r["RotationZ"].Value = cellRot.Z;
        loc.Rows.Add(r);
        // EntryLocationId + 10: in front of the map's bonfire, facing it (test warps: autopilot "warp 18010000 3700010")
        var b = new PARAM.Row(memory) { ID = EntryLocationId + 10 };
        b["PositionX"].Value = asylum.BonfirePos.X; b["PositionY"].Value = asylum.BonfirePos.Y; b["PositionZ"].Value = asylum.BonfirePos.Z + 1.0f; // 1 m: within the rest prompt's reach (1.8 m was not)
        b["RotationX"].Value = 0f; b["RotationY"].Value = 180f; b["RotationZ"].Value = 0f;
        loc.Rows.Add(b);
        loc.Rows.Sort((x, y) => x.ID.CompareTo(y.ID));
        pack.AddFile("gamedata", $"param/EventLocation_{asylum.Ds2Map}.param", loc.Write());
    }

    /// <summary>o00_9070 → o00_9071 with the Forest of Fallen Giants map textures it uses packed into its own archive.</summary>
    static byte[] BuildTreeObject(string data)
    {
        var bnd = BND4.Read(Path.Combine(data, "model", "obj", SourceModel + ".bnd"));
        var flvFile = bnd.Files.First(f => f.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase));
        var flver = FLVER2.Read(flvFile.Bytes);
        string texDir = $@"N:\FRPG2\data\Model\obj\{TreeModel}\tex\";
        var textures = new SortedSet<string>(StringComparer.OrdinalIgnoreCase); // file names, e.g. BD_c3091_s.pxt
        foreach (var t in flver.Materials.SelectMany(m => m.Textures))
        {
            if (!t.Path.Contains(@"\Model\map\", StringComparison.OrdinalIgnoreCase)) continue; // e.g. the common dummy128 detail map
            string file = t.Path[(t.Path.LastIndexOf('\\') + 1)..];
            textures.Add(file);
            t.Path = texDir + file;
        }
        flvFile.Bytes = flver.Write();

        // The textures, from the map they were made for (BXF4 of NAME.tpf.dcx), stored raw as NAME.tpf like o00_0100.bnd.
        var mapTex = BXF4.Read(Path.Combine(data, "model", "map", "t10_10_00_00.tpfbhd"), Path.Combine(data, "model", "map", "t10_10_00_00.tpfbdt"));
        int nextId = bnd.Files.Max(f => f.ID) + 1;
        var acbFile = bnd.Files.First(f => f.Name.EndsWith(".acb", StringComparison.OrdinalIgnoreCase));
        foreach (string file in textures)
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            var src = mapTex.Files.FirstOrDefault(f => Path.GetFileName(f.Name).StartsWith(stem + ".tpf", StringComparison.OrdinalIgnoreCase))
                      ?? throw new InvalidDataException($"texture {stem} not in t10_10_00_00");
            byte[] tpf = DCX.Is(src.Bytes) ? DCX.Decompress(src.Bytes) : src.Bytes;
            bnd.Files.Insert(bnd.Files.IndexOf(acbFile), new BinderFile(flvFile.Flags, nextId++, stem + ".tpf", tpf));
        }

        // ACB: our paths, plus a Texture asset per packed texture (as o00_0100.acb lists its own).
        var acb = ACB.Read(acbFile.Bytes);
        foreach (var a in acb.Assets)
        {
            a.AbsolutePath = a.AbsolutePath?.Replace(SourceModel, TreeModel);
            a.RelativePath = a.RelativePath?.Replace(SourceModel, TreeModel);
        }
        foreach (string file in textures)
            acb.Assets.Add(new ACB.Asset.Texture { AbsolutePath = $@"N:\FRPG2_64\data\model\obj\{TreeModel}\tex\{file}", RelativePath = $"tex/{file}" });
        acbFile.Bytes = acb.Write();

        foreach (var f in bnd.Files)
            f.Name = f.Name.Replace(SourceModel, TreeModel);
        for (int i = 0; i < bnd.Files.Count; i++) bnd.Files[i].ID = i;
        return bnd.Write();
    }

    static IEnumerable<ESD.CommandCall> AllCommands(ESD.State s)
    {
        IEnumerable<ESD.CommandCall> Cond(ESD.Condition c) => c.PassCommands.Concat(c.Subconditions.SelectMany(Cond));
        return s.EntryCommands.Concat(s.ExitCommands).Concat(s.WhileCommands).Concat(s.Conditions.SelectMany(Cond));
    }

    static ESD.State CopyState(ESD.State s, Dictionary<long, long> renumber)
    {
        ESD.CommandCall C(ESD.CommandCall c) => new(c.CommandBank, c.CommandBank == 6 && renumber.TryGetValue(c.CommandID, out long n) ? (int)n : c.CommandID,
            c.Arguments.Select(a => (byte[])a.Clone()).ToArray());
        ESD.Condition Cd(ESD.Condition c)
        {
            var copy = new ESD.Condition { TargetState = c.TargetState, Evaluator = (byte[])c.Evaluator.Clone() };
            copy.PassCommands.AddRange(c.PassCommands.Select(C));
            copy.Subconditions.AddRange(c.Subconditions.Select(Cd));
            return copy;
        }
        var state = new ESD.State();
        state.EntryCommands.AddRange(s.EntryCommands.Select(C));
        state.ExitCommands.AddRange(s.ExitCommands.Select(C));
        state.WhileCommands.AddRange(s.WhileCommands.Select(C));
        state.Conditions.AddRange(s.Conditions.Select(Cd));
        return state;
    }

    /// <summary>An ESD integer argument: 0x40+n for 0..63, else 0x82 + int32; then 0xA1 (end).</summary>
    static byte[] Int(int v) => v is >= 0 and < 64 ? [(byte)(0x40 + v), 0xA1] : [0x82, .. BitConverter.GetBytes(v), 0xA1];
}
