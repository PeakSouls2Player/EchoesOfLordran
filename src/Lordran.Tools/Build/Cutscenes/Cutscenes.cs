using System.Numerics;
using Lordran.Tools.Build.Maps;
using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Build.Cutscenes;

/// <summary>
/// The DS1 cutscenes the pack converts (session 8, cutscene track: the crow) and what the rest of the build needs from
/// them: the DS2 demo id of a DS1 scene (the event translator's 132125 arguments), the demo-only objects the maps place
/// (the crow), the DemoParam rows and scnbnd files.
/// DS1's crow scenes (m18_01 E11810000 / E11810150, NOTES.md): 180125 the crow takes the player from the cliff (Asylum)
/// + 100225 drops him at Firelink; 180126 from the nest + 100226 at Firelink's ledge.
/// </summary>
public static class Cutscenes
{
    /// <summary>DS1's Giant Crow c3510 as a DS2 demo object (DS2's own carrying bird is the object o00_6090, scn101010).</summary>
    public const string CrowModel = "o18_3510";

    static readonly Dictionary<int, string> CrowActors = new() { [3510] = CrowModel };

    public static readonly RemoConverter.Scene[] Crow =
    [
        new(180125, "m18_01_00_00", "m18_01_00_00", CrowActors),
        new(100225, "m10_02_00_00", Lordran.Tools.Build.Maps.MapHosts.Host("m10_02_00_00"), CrowActors),
        new(180126, "m18_01_00_00", "m18_01_00_00", CrowActors),
        new(100226, "m10_02_00_00", Lordran.Tools.Build.Maps.MapHosts.Host("m10_02_00_00"), CrowActors),
        // session 9 (track firelink): Firelink's nest → the Asylum (m10_02 E11020021): 100230 the crow takes the player
        // from the nest (Firelink) + 180130 drops him on the Asylum cliff (C2 ids 190230 / 180130)
        new(100230, "m10_02_00_00", Lordran.Tools.Build.Maps.MapHosts.Host("m10_02_00_00"), CrowActors),
        new(180130, "m18_01_00_00", "m18_01_00_00", CrowActors),
    ];

    /// <summary>DS1 scenes this build converts: build-pack fills it (<see cref="Plan"/>) before the maps and event
    /// scripts are built (the translator's 132125 arguments, the maps' demo objects); empty = no cutscenes.</summary>
    public static readonly HashSet<int> Planned = [];

    public static void Plan() { foreach (var s in Crow) Planned.Add(s.Ds1Scene); }

    /// <summary>DS1 human NPCs as demo characters when the demo's map registers them (<see cref="HumanActors"/>);
    /// build-pack --no-cutscene-npcs turns it off (to tell an NPC actor problem from the rest of a demo in game).</summary>
    public static bool NpcActors = true;

    /// <summary>DS1 scene → DS2 demo id, for the scenes this build converts (null: not converted → demo 0).</summary>
    public static int? Demo(int ds1Scene) => Planned.Contains(ds1Scene) && Crow.FirstOrDefault(s => s.Ds1Scene == ds1Scene) is { } s ? s.Demo : null;

    /// <summary>Draw/display groups of a demo-only object: group 110 alone (FoFG's o00_6090_0000: [0, 0, 0, 0x4000]).</summary>
    static readonly uint[] DemoGroups = [0, 0, 0, 0x4000];

    /// <summary>
    /// Session 9 (crow track): in-game A/B builds of the crow, chosen by the environment variable LR_CROW_AB at build time
    /// (never set for a normal build; the pack's build.log names the variant): "groups" = the crow's parts on every draw
    /// group (FFFFFFFF×4 like our other objects), so it also stands at its MSB spots outside the demos (Firelink: DS1's
    /// perch on the ruins at (−46.0, −46.5, 89.3)) — shows whether the converted model renders at all; "rebase" = each
    /// cut's offset moved to the crow (its root where it comes nearest the camera), every actor's root re-expressed so world poses
    /// are unchanged (vanilla cut offsets sit near the action: the bird object's origin ≤ 37 m from the camera; ours
    /// 84-251 m, mostly behind it) — tells whether the object is culled by its far origin; "prefab" = the vanilla bird's
    /// object kind (its prefab state machine shipped, GDParam.isPrefab kept); "noalign" = DS1's FLVER node tree (no rule
    /// R3c: the s8-8 model with the isPrefab cleanup) — the control for R3c. A build with LR_CROW_AB set prints a warning.
    /// </summary>
    public static string CrowTest => (Environment.GetEnvironmentVariable("LR_CROW_AB") ?? "").Trim().ToLowerInvariant();

    static readonly uint[] AllGroups = [0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF];

    /// <summary>
    /// The demo objects a DS2 map must place for the planned scenes that play in it (R3): per DS1 character part a scene
    /// animates as a DS2 object, an MSB object part named exactly like the demo's actor ("o18_3510_0000"), generic
    /// instance row AABB·10000 + model number, groups 110 only (invisible outside demos), at the DS1 part's own MSB
    /// position (DS1: the crow's DummyEnemy c3510_0000 at the Asylum origin, its Enemy part at Firelink).
    /// </summary>
    public static List<MapBuilder.Placement> DemoActors(string dsrRoot, string ds2Map)
    {
        var list = new List<MapBuilder.Placement>();
        foreach (var s in Crow.Where(s => s.Ds2Map == ds2Map && Planned.Contains(s.Ds1Scene)))
        {
            var msb = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", s.Ds1Map + ".msb"));
            foreach (var (model, obj) in s.ObjectActors)
                foreach (var part in msb.Parts.GetEntries().Where(p => p.Name.StartsWith($"c{model:D4}_")))
                {
                    if (list.Any(x => x.Model == obj)) continue; // one demo part per model and map (the MQB names instance 0000)
                    int inst = int.Parse(ds2Map[1..3] + ds2Map[4..6]) * 10000 + model;
                    list.Add(new MapBuilder.Placement(obj, part.Position, part.Rotation, inst, MapBuilder.PlacementKind.Generic, Note: $"(demo-only: DS1 {part.Name} of scn{s.Ds1Scene})") { Groups = DemoGroups });
                    if (CrowTest == "groups") list[^1] = list[^1] with { Groups = AllGroups, Note = $"(TEST LR_CROW_AB=groups: every draw group; DS1 {part.Name} of scn{s.Ds1Scene})" };
                }
        }
        return list;
    }

    /// <summary>The DS1 map a scene plays in, from its id (AABBNN → mAA_BB_00_00).</summary>
    public static string SceneMap(int ds1Scene) => $"m{ds1Scene / 10000:D2}_{ds1Scene / 100 % 100:D2}_00_00";

    /// <summary>DemoParam template (R5): Things Betwixt's waking demo 100200 (addGroupMask 110 = demo-only objects; no
    /// IBL, filter or light/SFX overrides). Its groupMask/memoryBlock are then set for the demo's map by
    /// <see cref="DemoArea"/> (C2b).</summary>
    public const int DemoParamTemplate = 100200;

    /// <summary>
    /// C2b: DemoParam.memoryBlock and groupMask name the DS2 map area the demo plays in. memoryBlock = an MSB2
    /// collision's memory-block id (SoulsFormats Collision.UnkT12, Smithbox "MemoryBlockID"), groupMask = a draw group
    /// the collisions of that block carry. Evidence (fix pass, work\memblk\demoblk.py over every vanilla demo's map):
    /// 32 of 35 DemoParam rows match exactly (Things Betwixt's waking 100200: block 1 / group 2 = the h01 collisions; the
    /// hut demos 1002xx: block 2 / group 8 = the h02 collisions; FoFG's bird 101010: block 8 / group 22; Last Giant 101020:
    /// block 10 / group 60, the only block carrying group 60; Memory of the King 503810: its single collision, block 1 /
    /// group 1); the 3 others are Drangleic Castle's throne endings 202130-202150 (blocks 9/16 with no matching group in
    /// m20_21, so they play elsewhere). It is not a size class: 100230 (10 MB) uses block 2, 101010 (277 KB) block 8.
    /// Same numbering as a demo character's GeneratorRegistParam row (9 of 9 vanilla demo characters registered with
    /// paramdef bit "memoryBlock(N+7)" for their demo's block N, see <see cref="RegistBlockBit"/>).
    /// Our maps' collisions are all block 1 with groups {1, 2, 3, 4, 7, 14} (copied from Things Betwixt's h01_0200), so
    /// the rule keeps 100200's block 1 / group 2; a block our maps don't have (2, 8) would name an area with nothing in it.
    /// Rule: block = the most common collision block of the map (ties: the lowest); group = the template's if every
    /// collision of that block carries it, else the group most of them carry (ties: the lowest).
    /// </summary>
    public static (int Block, int Group, string Why) DemoArea(byte[] msbBytes, string ds2Map, int templateGroup)
    {
        var msb = MSB2.Read(msbBytes);
        var cols = msb.Parts.Collisions;
        if (cols.Count == 0) throw new InvalidDataException($"{ds2Map}: no collisions, so no memory block for its demos");
        var blocks = cols.GroupBy(c => (int)c.UnkT12).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();
        var inBlock = blocks[0].ToList();
        static IEnumerable<int> Groups(uint[] g) => Enumerable.Range(0, 32 * g.Length).Where(b => (g[b / 32] >> (b % 32) & 1) != 0);
        int group = inBlock.All(c => Groups(c.DrawGroups).Contains(templateGroup)) ? templateGroup
            : inBlock.SelectMany(c => Groups(c.DrawGroups)).GroupBy(b => b).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key).DefaultIfEmpty(templateGroup).First();
        int pieces = msb.Parts.MapPieces.Count, showing = msb.Parts.MapPieces.Count(p => Groups(p.DrawGroups).Contains(group));
        string why = $"{ds2Map}: memory block {blocks[0].Key} ({inBlock.Count} of {cols.Count} collisions{(blocks.Count > 1 ? $"; others {string.Join(", ", blocks.Skip(1).Select(g => $"{g.Key}×{g.Count()}"))}" : "")}), "
            + $"draw group {group} ({inBlock.Count(c => Groups(c.DrawGroups).Contains(group))} of them carry it; {showing} of {pieces} map pieces)";
        return (blocks[0].Key, group, why);
    }

    /// <summary>
    /// GeneratorRegistParam's memory-block bits: the Smithbox paramdef names them memoryBlock1..16 after
    /// bAnotherWorldGenerate (byte 13 bit 0), but the game's block N is the paramdef's memoryBlock(N+7), i.e. bit N+7 of
    /// the bits from byte 13 (block 1 = byte 14 bit 0). Evidence: Things Betwixt's collisions are blocks 1-3 and its
    /// characters use memoryBlock8-10; the hut demos' characters (DemoParam block 2) memoryBlock9, the waking/hut player
    /// model 805000 memoryBlock8+9 (blocks 1 and 2), Gilligan 704510 (Majula 100450, block 2) 8-11, m10_17's 101710
    /// (block 8) 10+15, the Old Iron King 607000 (101910, block 6) 13, Velstadt 333000 (202410, block 3) 10; and
    /// session 5's finding that only memoryBlock8 loads characters in our maps, whose collisions are all block 1.
    /// </summary>
    public static bool RegistBlockBit(byte[] row, int block) =>
        block is >= 1 and <= 16 && row.Length > 13 + (block + 7) / 8 && (row[13 + (block + 7) / 8] >> ((block + 7) % 8) & 1) != 0;

    /// <summary>
    /// R3b: DS1's human NPCs (MSB characters on the player model c0000 other than the player, e.g. Firelink's Crestfallen
    /// Warrior c0000_0018 in 100225) as DS2 demo characters. A DS2 demo character actor is named "c&lt;EnemyParam id:D6&gt;_&lt;n&gt;"
    /// (exe string DemoChr_c%06d; vanilla 704510 "Laddersmith Gilligan (Cutscene Model)", 705000 Strowen, 805000 "Player
    /// Character (Cutscene Model)", bosses 626000/627000/333000 …) and that EnemyParam is registered in the map's
    /// GeneratorRegistParam for the demo's memory block (17 of 17 vanilla demo characters have such a row; the cutscene
    /// models' rows have logic -1/-1). Our NPCs: EnemyParam = the npc track's rule (NpcPorter.Id: 900,000 + DS1 NpcParam %
    /// 100,000; Crestfallen 6270 → c906270_0000, model 1 = c0001 dressed by NPCGenerateParam), motion = DS1's retargeted
    /// onto c0001 as the player's (same DS1 c0000 → DS2 c0001 rigs). An NPC joins a demo only when the pack registers
    /// that EnemyParam in the demo's map with the demo's block (the npc track's NPC rows: memoryBlock8 = block 1), so a
    /// build without the NPCs keeps the demo valid; the left-out NPC is listed in the notes.
    /// P: vanilla demo characters are dedicated cutscene models, never a dressed c0001 NPC, so whether DemoChr dresses a
    /// model-1 EnemyParam from NPCGenerateParam is unproven (in game: --no-cutscene-npcs separates it). Fallbacks: a
    /// dedicated cutscene model (his parts baked into one chr, as the npc track's Anastacia body) or a demo object with
    /// DS1's skeleton 1:1 (the crow route).
    /// </summary>
    public static Dictionary<string, string> HumanActors(string dsrRoot, RemoConverter.Scene s, Func<int, string?>? registered, List<string> notes)
    {
        var map = new Dictionary<string, string>();
        var msb = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", s.Ds1Map + ".msb"));
        var parts = Ds1Remo.Read(Path.Combine(dsrRoot, "remo", $"scn{s.Ds1Scene}.remobnd.dcx")).Cuts.SelectMany(c => c.Parts).Select(p => p.Name).ToHashSet();
        foreach (var e in msb.Parts.Enemies.Where(e => e.ModelName == "c0000" && e.Name != "c0000_0000" && parts.Contains(e.Name)))
        {
            int id = NpcEnemyParam(e.NPCParamID);
            string? missing = registered?.Invoke(id);
            if (missing != null) { notes.Add($"scn{s.Ds1Scene}: DS1 NPC {e.Name} (NpcParam {e.NPCParamID} → EnemyParam {id}) left out: {missing}"); continue; }
            map[e.Name] = $"c{id:D6}_0000";
        }
        return map;
    }

    /// <summary>The npc track's NPC id rule (NpcPorter.Id, data\sandbox\npc): DS1 NpcParam → EnemyParam = NPCGenerateParam.</summary>
    public static int NpcEnemyParam(int ds1NpcParam) => 900_000 + ds1NpcParam % 100_000;

    /// <summary>
    /// Converts the scenes, adds model/scn/scnNNNNNN.scnbnd and a DemoParam row per demo. Run after the maps and their
    /// characters (the MSBs must place the demo objects, <see cref="MapBuilder.Options.DemoActors"/>; the generator
    /// params decide which human NPCs join, <see cref="HumanActors"/>).
    /// </summary>
    public static List<RemoConverter.Result> Build(Pack pack, string dsrRoot)
    {
        // the crow model: DS1's c3510 as a demo object on DS2's demo bird o00_6090 (DS1 a00_0000 / a00_7000 as the
        // object's two clips, AnimList 0000/0001; the demo drives its bones itself)
        var materials = Ds2MapMaterials.Load(pack.Ds2Data);
        var variant = CrowTest switch
        {
            "" or "groups" or "rebase" => ObjPorter.TestVariant.None,
            "prefab" => ObjPorter.TestVariant.DemoPrefab,
            "noalign" => ObjPorter.TestVariant.DemoNoAlign,
            var v => throw new ArgumentException($"LR_CROW_AB={v}: expected groups, rebase, prefab or noalign"),
        };
        if (CrowTest != "")
        {
            pack.Note($"cutscene: TEST build LR_CROW_AB={CrowTest} (crow A/B variant; never install as the default pack)");
            Console.WriteLine($"  WARNING: LR_CROW_AB={CrowTest} is set: this is a crow A/B TEST pack, not a default pack (unset LR_CROW_AB for a normal build)");
        }
        RemoConverter.TestRebaseOnObject = CrowTest == "rebase";
        ObjPorter.Port(pack, dsrRoot, new ObjPorter.Options("c3510", CrowModel, "o00_6090", [("a00_0000", "a00_00_0000"), ("a00_7000", "a00_00_0001")], ObjPorter.Kind.DemoActor) { Variant = variant }, materials);
        var tpl = RemoConverter.Templates.Load(pack.Ds2Data, dsrRoot);
        var demoParam = pack.Param("DemoParam");
        var template = demoParam[DemoParamTemplate]!;
        var results = new List<RemoConverter.Result>();
        foreach (var s0 in Crow.Where(s => Planned.Contains(s.Ds1Scene)))
        {
            var (block, group, why) = DemoArea(pack.GetFile("gamedata", $"map/{s0.Ds2Map}/{s0.Ds2Map}.msb"), s0.Ds2Map, System.Convert.ToInt32(template["groupMask"].Value));
            var notes = new List<string>();
            var regist = pack.TryGetFile("gamedata", $"param/generatorregistparam_{s0.Ds2Map}.param");
            var s = s0 with { HumanActors = NpcActors ? HumanActors(dsrRoot, s0, id => Unregistered(regist, s0.Ds2Map, id, block), notes)
                : HumanActors(dsrRoot, s0, _ => "build-pack --no-cutscene-npcs", notes) };
            var r = RemoConverter.Convert(dsrRoot, s, tpl);
            r.Notes.InsertRange(0, notes);
            pack.AddFile("gamedata", $"model/scn/scn{r.Demo:D6}.scnbnd", r.ScnBnd);
            var row = pack.AddRow("DemoParam", template, r.Demo, $"[Lordran] DS1 scn{s.Ds1Scene} in {s.Ds2Map}");
            row["memoryBlock"].Value = (sbyte)block;
            row["groupMask"].Value = (sbyte)group;
            pack.Note($"cutscene: DS1 scn{s.Ds1Scene} → demo {r.Demo} ({s.Ds2Map}): {r.Cuts.Count} cuts, origin {r.Origin}, {r.ScnBnd.Length:N0} bytes; DemoParam {why}");
            foreach (var n in r.Notes) pack.Note($"cutscene: {n}");
            results.Add(r);
            Console.WriteLine($"  cutscene DS1 scn{s.Ds1Scene} → demo {r.Demo} ({r.Cuts.Count} cuts; memory block {block}, group {group}{(s.HumanActors!.Count > 0 ? "; NPCs " + string.Join(", ", s.HumanActors.Values) : "")})");
        }
        return results;
    }

    /// <summary>Why EnemyParam <paramref name="id"/> can't be a demo character in <paramref name="ds2Map"/> (null: it can):
    /// the map's GeneratorRegistParam (<paramref name="bytes"/>: the pack's gamedata param/generatorregistparam_MAP.param)
    /// must register it for the demo's block. The npc track writes that row together with the NPC's EnemyParam /
    /// NPCGenerateParam rows; reading the map file touches no regulation param.</summary>
    public static string? Unregistered(byte[]? bytes, string ds2Map, int id, int block)
    {
        if (bytes == null) return $"{ds2Map} has no GeneratorRegistParam";
        var p = PARAM.Read(bytes);
        var blob = new PARAMDEF { ParamType = p.ParamType, DataVersion = p.ParamdefDataVersion, Unicode = true, FormatVersion = 104, BigEndian = false };
        blob.Fields.Add(new PARAMDEF.Field(blob, PARAMDEF.DefType.dummy8, "data") { ArrayLength = (int)p.DetectedSize });
        p.ApplyParamdef(blob);
        var rows = p.Rows.Select(r => (byte[])r["data"].Value).Where(d => BitConverter.ToInt32(d, 0) == id).ToList();
        if (rows.Count == 0) return $"EnemyParam {id} isn't registered in {ds2Map}'s GeneratorRegistParam (the npc track's NPC isn't in this build)";
        return rows.Any(d => RegistBlockBit(d, block)) ? null : $"{ds2Map}'s GeneratorRegistParam rows for {id} don't cover memory block {block}";
    }
}
