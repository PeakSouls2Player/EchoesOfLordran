using System.Numerics;
using Lordran.Tools.Build.Events;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Session 11 (burg11, BURG-PLAN M2, R3 "object kinds by rule"): every DS1 object part of a map ported by MapHosts
/// (`--map burg` rows), by rule and for any map — the Asylum's hand lists (AsylumScript) generalised; the Asylum and
/// Firelink keep their own verified code. Rules (V = the same rule verified on the Asylum, P = new):
///  - FW1: a light wall (object part whose entity + 1 is a map SFX entity) is left out until its fog rule (white doors,
///    M4): a stand-in row (no fog) (P).
///  - BD1/SD0: a door at a seam to a DS1 map this build does not port stays shut: Static with DS1 collision (MapSeams).
///  - K1 (V): an MSB ObjAct part (not a chest o0110) → a prefab object on the K10′ door template (ObjActRules.DoorTemplate)
///    with DS1's ObjAct animation as the open clip (a00_01_0001) and another animation DS1 plays on that model (or its
///    closed rest clip) as the close clip (a00_01_0002), instance AABB0000 + entity % 10000, an ObjAct helper (invisible
///    key guide, AABB5000 + entity % 1000) at the ObjActParam dummy (K12), one helper model + KeyGuideParam per DS1 prompt
///    (K6). Generalisation K1g (P): Door-state ObjActs take the same prefab route here (one DS2 model per DS1 model also
///    covers the model's event-played parts; DS1's prompt side and requirements via the K6 group).
///  - K1e (P): a part DS1's events animate after an action button on the object (3:5/3:18 on an object, e.g. the
///    Lower-Burg one-way door 1011320: 11010101 "Open" → 2003:18) → the same prefab port, a helper per prompt at the
///    button's dummy (AABB3000 + entity % 1000).
///  - G4 (V): ladders (any part DS1 registers with 2009:0) → DS2's ladder of the nearest height (o00_09xx with a
///    MapObjectDefaultInstanceParam objCtrlType 4 row), plain or event-registered (MapBuilder.EventLadders).
///  - WE1 (V): the other event-addressed parts (WorldEvents.Parts: Breakable / Static / Scenery / prefab swap copies).
///  - OK1 (P, replaces the hand DecorObjects list for these maps): every other part by its model's files: DS1 fragments
///    (NAME_1.flver + NAME_1.hkx) and ObjectParam hp ≠ −1, defense 0 → Breakable on o00_2100 (contact break when
///    isBreakByPlayerCollide), DS1 collision → Decor (static, bind pose), model only → Scenery placement; skinned models
///    without collision (corpses, posed by DS1's MSB) are left out (no pose rule yet). Treasure parts are the item-pickup
///    rule's; chests (o0110) stand-in rows until a chest rule; the bonfire o0200 is BF1's.
/// </summary>
public sealed class MapObjects
{
    public sealed record Act(int Entity, MSB1.Part.Object Part, string Ds2Model, int Anim, int Dummy, int Helper, string HelperModel, int KeyGuide,
        int Msg, float Dist, ObjActRules.Req? Req, string Why);
    public sealed record AreaButton(int Area, int Message, int K, int Helper, string Model, int KeyGuide);

    public string Ds1Map { get; }
    public string Ds2Map { get; }
    public int Aabb { get; }
    string Aa => Ds2Map.Substring(1, 2);
    public List<ObjPorter.Options> Ports { get; } = [];
    public List<MapBuilder.Placement> Placements { get; } = [];
    /// <summary>DS1 entity → DS2 instance of every part this plan places with its own row.</summary>
    public Dictionary<int, int> Instances { get; } = [];
    /// <summary>DS1 model → DS2 prefab model (K1/K1e ports).</summary>
    public Dictionary<string, string> PrefabModels { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<Act> Acts { get; } = [];
    public List<Act> Buttons { get; } = [];
    public List<AreaButton> Areas { get; } = [];
    public List<WorldEvents.Part> World { get; } = [];
    public HashSet<int> FogWalls { get; } = [];
    /// <summary>Rule FW1: the wall's SFX entity (wall + 1) → its white door instance; (wall, instance, model).</summary>
    public Dictionary<int, int> SfxDoors { get; } = [];
    public List<(int Wall, int Instance, string Model)> WhiteDoors { get; } = [];
    /// <summary>Rule CH1: DS1 chests (o0110 ObjAct parts) to DS2 treasure boxes (MapChests).</summary>
    public List<(MSB1.Part.Object Part, int Instance)> Chests { get; } = [];
    /// <summary>Rule WE4: DS1 damaging objects (2005:9) placed here, entity to instance (= their damage rows).</summary>
    public Dictionary<int, int> Damaging { get; } = [];
    public HashSet<int> BorderDoors { get; } = [];
    public HashSet<string> LadderModels { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>(DS1 model lower case, DS1 animation id) → the clip that stands for it (single-clip models).</summary>
    public Dictionary<(string Model, int Anim), int> AnimAlias { get; } = [];
    public List<string> Log { get; } = [];
    /// <summary>Rule FW1b: light walls not placed because an earlier wall stands at their place (their events: absent).</summary>
    public HashSet<int> FogTwins { get; } = [];
    /// <summary>Rule ST1: stand-ins and left-out parts (kind, DS1 part, entity, why).</summary>
    public List<(string Kind, string Part, int Entity, string Why)> StandIns { get; } = [];

    public int Inst(int entity) => Aabb * 10000 + entity % 10000;

    static readonly Dictionary<string, MapObjects> cache = [];

    public static MapObjects For(string dsrRoot, string ds1Map, string ds2Map, IReadOnlySet<string> builtDs1Maps)
    {
        string key = $"{ds1Map}>{ds2Map}";
        if (cache.TryGetValue(key, out var done)) return done;
        return cache[key] = new MapObjects(dsrRoot, ds1Map, ds2Map, builtDs1Maps);
    }

    MapObjects(string dsrRoot, string ds1Map, string ds2Map, IReadOnlySet<string> built)
    {
        Ds1Map = ds1Map; Ds2Map = ds2Map; Aabb = MapHosts.Aabb(ds2Map);
        var msb1 = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", ds1Map + ".msb"));
        var emevd = EMEVD.Read(Path.Combine(dsrRoot, "event", ds1Map + ".emevd.dcx"));
        var defs = Commands.EmevdDump.Defs();
        var objActParam = Ds1Source.LoadParam(dsrRoot, "ObjActParam")!;
        var placed = new HashSet<int>();               // entities placed one by one
        var treasureParts = msb1.Events.Treasures.Select(t => t.TreasurePartName).ToHashSet();
        string Ds2(string ds1Model) => MapHosts.ObjectName(Aa, ds1Model);
        void Place(MSB1.Part.Object p, string model, int inst, string note, Dictionary<string, object>? fields = null, Vector3? pos = null, Vector3? rot = null)
        {
            // rule WE4 (the Asylum's boulder rule, generic): a part DS1 makes a damaging object (2005:9) carries the damage on
            // its own instance row (damageId0 = SystemDamageParam / DamageCtrlParam row, rule E-ID2; rows by MapScript)
            if (p.EntityID > 0 && WorldEvents.Damaging(dsrRoot, ds1Map, p.EntityID) != null)
            {
                fields = fields != null ? new Dictionary<string, object>(fields) : [];
                fields["damageId0"] = Chr.Ds1AttackParams.ObjectDamageRow(p.EntityID);   // session 12 (ehit12), rule E-ID2: System range (was inst: the Player range)
                Damaging[p.EntityID] = Chr.Ds1AttackParams.ObjectDamageRow(p.EntityID);
            }
            Placements.Add(new MapBuilder.Placement(model, pos ?? p.Position, rot ?? p.Rotation, inst, MapBuilder.PlacementKind.Generic, 0,
                $"(DS1 {p.Name}, entity {p.EntityID}) {note}") { Fields = fields ?? [], Scale = p.Scale });
            if (p.EntityID > 0) { placed.Add(p.EntityID); Instances[p.EntityID] = inst; }
        }

        // ---- FW1: light walls (stand-in: no fog until the white-door rule, M4)
        var sfx = msb1.Events.SFX.Select(s => s.EntityID).Where(e => e > 0).ToHashSet();
        // session 11 (burg11, M2 leftovers), rule FW1 (the Asylum's verified fog rule, generic): a DS1 light wall → a DS2 white
        // door (o00_050x, the one whose model width is nearest DS1's wall), instance = MapObjectWhiteDoorParam row = AABB0000 +
        // entity % 10000 (row from vanilla 100: both sides, "Enter the mist" 401 / 411, no pass flag: DS1's own events hide
        // it, 2006:1 on the wall's SFX entity (wall + 1) → 130702(door, 0), and cross it, 3:18 line segment → the crossing boxes)
        foreach (var p in msb1.Parts.Objects.Where(p => p.EntityID > 0 && sfx.Contains(p.EntityID + 1)))
        {
            // session 11 (burg11, round 9), rule FW1b: a light wall where an earlier one already stands (Demon Ruins: the boss
            // fog o4963 and its multiplayer twin o4969 at one spot) is not placed again (one DS2 door there; the twin's events,
            // multiplayer-only, find no door)
            if (WhiteDoors.Any(w => msb1.Parts.Objects.First(o => o.EntityID == w.Wall).Position is var q && Vector3.Distance(q, p.Position) < 0.1f))
            { placed.Add(p.EntityID); FogTwins.Add(p.EntityID); Log.Add($"rule FW1b: light wall {p.Name} (entity {p.EntityID}) at an earlier wall's place: not placed again"); continue; }
            FogWalls.Add(p.EntityID);
            var (model, why) = WhiteDoorModel(dsrRoot, Commands.ExtractDs2.DefaultOut, p);
            int inst = Inst(p.EntityID);
            Placements.Add(new MapBuilder.Placement(model, p.Position, p.Rotation, inst, MapBuilder.PlacementKind.WhiteDoor, inst,
                $"(DS1 {p.Name}, entity {p.EntityID}) rule FW1 white door ({why})"));
            placed.Add(p.EntityID); Instances[p.EntityID] = inst;
            SfxDoors[p.EntityID + 1] = inst;
            WhiteDoors.Add((p.EntityID, inst, model));
        }

        // ---- BD1: doors at seams to DS1 maps this build does not port (MapSeams.SeamDoors with the unbuilt targets)
        foreach (var (p, model) in MapSeams.SeamDoors(dsrRoot, ds1Map, ds2Map, msb1, target => !built.Contains(target)))
        {
            // session 11 (burg11, round 7): a chest near such a seam (the Depths' 1001650 by the Blighttown connect) is not a door (CH1)
            if (msb1.Events.ObjActs.Any(a => a.ObjActPartName == p.Name && ObjActRules.KindOf(a, p) == ObjActRules.Kind.Treasure)) continue;
            BorderDoors.Add(p.EntityID);
            if (!Ports.Any(x => x.Ds2Name == model)) Ports.Add(new ObjPorter.Options(p.ModelName, model, "o16_1230", [], ObjPorter.Kind.Static));
            Place(p, model, Inst(p.EntityID), "rule BD1/SD0: door into an unported map, kept shut (static, DS1 collision)");
            StandIns.Add(("BD1 border door", p.Name, p.EntityID, "door into a map this pack doesn't port: kept shut, DS1's prompt comes with the door rule"));
        }

        var ladderEntities = emevd.Events.SelectMany(ev => ev.Instructions).Where(i => i.Bank == 2009 && i.ID == 0)
            .Select(i => EmevdTranslator.Decode(defs, i)).Where(v => v.Length >= 3).Select(v => (int)v[2]).ToHashSet();
        var ladderModels = msb1.Parts.Objects.Where(p => ladderEntities.Contains(p.EntityID)).Select(p => p.ModelName).Distinct().ToList();

        // ---- K1 / K1g: ObjAct parts
        var prompts = new List<(int, float, int)>();
        var playedAnims = WorldEvents.Uses(dsrRoot, ds1Map).Where(u => (u.Bank, u.Id) is (2003, 18) or (2005, 7))
            .Select(u => (u.Entity, Anim: BitConverter.ToInt32(u.Ins.ArgData, 4))).ToList();
        var refused = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var noCollision = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // the DS1 anibnd's files (skeleton + clips): a prefab port needs the skeleton and its clips
        HashSet<string> AnimFiles(string model)
        {
            string path = Path.Combine(dsrRoot, "obj", model + ".objbnd.dcx");
            if (!File.Exists(path)) return [];
            var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(path)));
            var ab = bnd.Files.FirstOrDefault(f => f.Name.EndsWith(".anibnd", StringComparison.OrdinalIgnoreCase));
            // a model without DS1 collision (the rolling barrel o1330) is a prefab without a collision file (ObjPorter
            // NoCollision): the template's door body would bind to no node (world-events' body check)
            if (!bnd.Files.Any(f => Path.GetFileName(f.Name).Equals(model + ".hkx", StringComparison.OrdinalIgnoreCase))) noCollision.Add(model);
            return ab == null ? [] : BND3.Read(ab.Bytes).Files.Select(f => Path.GetFileName(f.Name).ToLowerInvariant()).ToHashSet();
        }
        bool PrefabPort(string ds1Model, int open, HashSet<int> entities)
        {
            if (PrefabModels.ContainsKey(ds1Model)) return true;
            if (refused.Contains(ds1Model)) return false;
            if (ladderModels.Contains(ds1Model)) { refused.Add(ds1Model); Log.Add($"K1 prefab port {ds1Model}: a ladder model (G4 plain ladder; DS1's kick-down animation is a stand-in)"); return false; }
            var files = AnimFiles(ds1Model);
            var clips = files.Where(f => f.StartsWith("a00_") && f.EndsWith(".hkx")).ToList();
            // a model whose anibnd holds one clip that isn't named after the ObjAct's objAnimId (the levers o1200: objAnimId 1,
            // only a00_0000.hkx; P: the TAE's animation 1 references it) plays that clip
            if (!files.Contains($"a00_{open:d4}.hkx") && clips.Count == 1 && int.TryParse(clips[0][4..8], out int only))
            {
                Log.Add($"K1 prefab port {ds1Model}: objAnimId {open} has no a00_{open:D4}.hkx; the anibnd's only clip {clips[0]} is the open clip (P)");
                AnimAlias[(ds1Model.ToLowerInvariant(), open)] = only;
                open = only;
            }
            bool skel = files.Contains("skeleton.hkx") || files.Contains("skelton.hkx");
            if (!skel || !files.Contains($"a00_{open:d4}.hkx"))
            {
                refused.Add(ds1Model);
                Log.Add($"K1 prefab port {ds1Model}: refused (anibnd {(skel ? "" : "without a skeleton ")}{(files.Contains($"a00_{open:d4}.hkx") ? "" : $"without a00_{open:D4}.hkx")}; files {string.Join(" ", files.Take(8))})");
                return false;
            }
            var parts = msb1.Parts.Objects.Where(p => p.ModelName == ds1Model && p.EntityID > 0).Select(p => p.EntityID).ToHashSet();
            var other = playedAnims.Where(u => parts.Contains(u.Entity) && u.Anim != open).Select(u => u.Anim).Distinct().ToList();
            var anims = new List<(string, string)> { ($"a00_{open:D4}", "a00_01_0001") };
            var restLog = new List<string>();
            if (other.Count > 0 && files.Contains($"a00_{other[0]:d4}.hkx")) anims.Add(($"a00_{other[0]:D4}", "a00_01_0002"));
            else if (ObjActRules.RestClip(dsrRoot, ds1Model, open, restLog) is string rest) anims.Add((rest, "a00_01_0002"));
            Log.AddRange(restLog);
            string template = ObjActRules.DoorTemplate(Commands.ExtractDs2.DefaultOut);
            Ports.Add(new ObjPorter.Options(ds1Model, Ds2(ds1Model), template, [.. anims], ObjPorter.Kind.Prefab) { NoCollision = noCollision.Contains(ds1Model) });
            PrefabModels[ds1Model] = Ds2(ds1Model);
            Log.Add($"K1 prefab port {ds1Model} → {Ds2(ds1Model)} on {template}: {string.Join(", ", anims.Select(a => $"{a.Item1} → {a.Item2}"))}");
            return true;
        }
        foreach (var a in msb1.Events.ObjActs)
        {
            if (ObjActRules.Of(msb1, objActParam, a) is not { } req || req.Part.EntityID <= 0) continue;
            int e = req.Part.EntityID;
            if (BorderDoors.Contains(e) || FogWalls.Contains(e)) continue;
            if (ObjActRules.KindOf(a, req.Part) == ObjActRules.Kind.Treasure)
            {
                // rule CH1 (session 11): a DS1 chest -> DS2's treasure box (MapChests: o00_0200, objCtrlType 1, lot = instance)
                Chests.Add((req.Part, Inst(e)));
                placed.Add(e); Instances[e] = Inst(e);
                continue;
            }
            if (!PrefabPort(req.Part.ModelName, req.AnimId, []))
            {
                StandIns.Add(("ObjAct without a prefab", req.Part.Name, e, $"ObjAct {req.A}: the model's anibnd can't make a prefab (see K1 log): left to OK1 (static)"));
                continue;
            }
            var key = (req.Prompt.Msg, req.Prompt.Dist, req.Prompt.Angle);
            if (!prompts.Contains(key)) prompts.Add(key);
            int k = prompts.IndexOf(key);
            var act = new Act(e, req.Part, Ds2(req.Part.ModelName), req.AnimId, req.DummyId, Aabb * 10000 + 5000 + e % 1000, $"o{Aa}_91{50 + k:D2}",
                Aabb * 10000 + 2000 + k, req.Prompt.Msg, Math.Max(0.5f, req.Prompt.Dist), req,
                $"K1{(ObjActRules.KindOf(a, req.Part) == ObjActRules.Kind.Door ? "g (Door-state ObjAct)" : "")} ObjAct {req.A}");
            Acts.Add(act);
            Place(req.Part, act.Ds2Model, Inst(e), $"{act.Why}: prefab on {Ports.First(p => p.Ds2Name == act.Ds2Model).TemplateObj}");
        }

        // ---- K1e: action buttons on objects (instances resolved), on animated parts DS1's events play
        var buttonMsgs = new List<int>();
        foreach (var (ev, ins) in WorldEvents.Instances(dsrRoot, ds1Map))
            foreach (var x in ins.Where(i => i.Bank == 3 && i.ID is 5 or 18))
            {
                var v = EmevdTranslator.Decode(defs, x);
                if (v.Length < 7 || (int)v[1] != 0 || (int)v[2] <= 0) continue; // object buttons with a fixed entity
                int e = (int)v[2], dummy = (int)v[4], msg = (int)v[6];
                float dist = Convert.ToSingle(v[5]);
                if (Acts.Any(a => a.Entity == e) || Buttons.Any(b => b.Entity == e && b.Msg == msg) || BorderDoors.Contains(e) || FogWalls.Contains(e)) continue;
                var part = msb1.Parts.Objects.FirstOrDefault(p => p.EntityID == e);
                if (part == null) continue;
                // session 11 (burg11, round 6), rule KL1: a button on a ladder an event registers later (the kick ladder 1011149,
                // 11010100) → its key-guide helper only; the ladder stays G4's (hidden at load, 2009:0 shows it); the kick clip is dropped
                if (MapBuilder.EventLadders(dsrRoot, ds1Map, msb1, includeKick: true).Contains(e))
                {
                    if (!buttonMsgs.Contains(msg)) buttonMsgs.Add(msg);
                    int li = buttonMsgs.IndexOf(msg);
                    Buttons.Add(new Act(e, part, Ds2(part.ModelName), 0, dummy, Aabb * 10000 + 3000 + e % 1000, $"o{Aa}_91{30 + li:D2}",
                        Aabb * 10000 + 1000 + li, msg, Math.Max(0.5f, dist), null, $"rule KL1 kick-ladder button {msg} (event {ev})"));
                    Log.Add($"KL1: action button {msg} on the event-registered ladder {part.Name} ({e}) in {ev}: helper only");
                    continue;
                }
                int? anim = playedAnims.Where(u => u.Entity == e).Select(u => (int?)u.Anim).FirstOrDefault();
                if (anim == null || !WorldEvents.Played(dsrRoot, ds1Map, e))
                {
                    Log.Add($"K1e: action button {msg} on {part.Name} ({e}) in {ev}: DS1 plays no animation on it: no rule (left to WE1)");
                    continue;
                }
                if (!PrefabPort(part.ModelName, anim.Value, [])) continue;
                if (!buttonMsgs.Contains(msg)) buttonMsgs.Add(msg);
                int idx = buttonMsgs.IndexOf(msg);
                var b = new Act(e, part, Ds2(part.ModelName), anim.Value, dummy, Aabb * 10000 + 3000 + e % 1000, $"o{Aa}_91{30 + idx:D2}",
                    Aabb * 10000 + 1000 + idx, msg, Math.Max(0.5f, dist), null, $"K1e action button {msg} (event {ev})");
                Buttons.Add(b);
                if (!placed.Contains(e)) Place(part, b.Ds2Model, Inst(e), $"{b.Why}: prefab");
            }

        // ---- G4: ladders (models found first: no prefab rule takes a ladder model)
        foreach (var model in ladderModels)
        {
            var (template, why) = LadderTemplate(dsrRoot, Commands.ExtractDs2.DefaultOut, model);
            Ports.Add(new ObjPorter.Options(model, Ds2(model), template, [], ObjPorter.Kind.Ladder));
            LadderModels.Add(model);
            Log.Add($"G4 ladder {model} → {Ds2(model)} on {template} ({why})");
        }

        // ---- K1p (P): an animated part DS1's events play (2003:18) with no ObjAct (gates, the Parish gate, the barrel, the
        // kick ladder's model…) → the model's prefab port with that animation as its open clip; WE1 below then places the
        // part as an instance of it (and C11 maps DS1's animation to the prefab state)
        foreach (var e in WorldEvents.EventObjects(dsrRoot, ds1Map, msb1).Order())
        {
            var part = msb1.Parts.Objects.First(p => p.EntityID == e);
            if (placed.Contains(e) || FogWalls.Contains(e) || BorderDoors.Contains(e) || LadderModels.Contains(part.ModelName) || WorldEvents.HasObjAct(msb1, e)) continue;
            if (!WorldEvents.Played(dsrRoot, ds1Map, e) || !playedAnims.Any(u => u.Entity == e)) continue;
            int anim = playedAnims.First(u => u.Entity == e).Anim;
            string bndPath = Path.Combine(dsrRoot, "obj", part.ModelName + ".objbnd.dcx");
            if (!File.Exists(bndPath) || !BND3.Read(DCX.Decompress(File.ReadAllBytes(bndPath))).Files.Any(f => f.Name.EndsWith(".anibnd", StringComparison.OrdinalIgnoreCase))) continue;
            PrefabPort(part.ModelName, anim, []);
        }

        // ---- WE1: the other event-addressed parts
        var other = new HashSet<int>(placed.Concat(FogWalls).Concat(BorderDoors));
        foreach (var w in WorldEvents.Parts(dsrRoot, ds1Map, ds2Map, msb1, PrefabModels, LadderModels, other, Log))
        {
            World.Add(w);
            if (w.Kind != WorldEvents.Kind.Prefab && !Ports.Any(p => p.Ds2Name == w.Ds2Model))
                Ports.Add(new ObjPorter.Options(w.Ds1Model, w.Ds2Model, WorldEvents.Template(w.Kind)!, [], w.Kind switch
                { WorldEvents.Kind.Breakable => ObjPorter.Kind.Breakable, WorldEvents.Kind.Static => ObjPorter.Kind.Static, _ => ObjPorter.Kind.Scenery }));
            var part = msb1.Parts.Objects.First(p => p.EntityID == w.Entity);
            Place(part, w.Ds2Model, w.Instance, $"WE1 world-event object: {w.Note}", new Dictionary<string, object>(w.Fields), w.Position, w.Rotation);
        }

        // ---- OK1: every other part by its model's files
        var objectParam = Ds1Source.LoadParam(dsrRoot, "ObjectParam");
        int nextFree = 4000;
        foreach (var g in msb1.Parts.Objects.GroupBy(p => p.ModelName))
        {
            string m = g.Key;
            if (m == "o0200" || LadderModels.Contains(m)) continue;              // BF1; ladders (G4: every part)
            var rest = g.Where(p => !(p.EntityID > 0 && (placed.Contains(p.EntityID) || FogWalls.Contains(p.EntityID))) && !treasureParts.Contains(p.Name)).ToList();
            if (rest.Count == 0) continue;
            if (PrefabModels.TryGetValue(m, out var prefabModel))
            {
                // the prefab model's other parts (no ObjAct, no event): the prefab in its initial (closed) state
                foreach (var p in rest)
                    Place(p, prefabModel, p.EntityID > 0 ? Inst(p.EntityID) : Aabb * 10000 + nextFree++, "OK1: part of a prefab model, nothing animates it in DS1 (initial state)");
                continue;
            }
            string bndPath = Path.Combine(dsrRoot, "obj", m + ".objbnd.dcx");
            if (!File.Exists(bndPath)) { foreach (var p in rest) StandIns.Add(("left out", p.Name, p.EntityID, "no DS1 objbnd")); continue; }
            var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(bndPath)));
            bool Has(string n) => bnd.Files.Any(f => Path.GetFileName(f.Name).Equals(n, StringComparison.OrdinalIgnoreCase));
            var row = objectParam?[int.Parse(m[1..])];
            int hp = row != null ? Convert.ToInt32(row["hp"].Value) : -1, def = row != null ? Convert.ToInt32(row["defense"].Value) : 0;
            if (Has(m + ".hkx"))
            {
                bool breakable = hp != -1 && def == 0 && Has(m + "_1.flver") && Has(m + "_1.hkx");
                bool byCollide = row != null && Convert.ToInt32(row["isBreakByPlayerCollide"].Value) != 0;
                Ports.Add(breakable
                    ? new ObjPorter.Options(m, Ds2(m), "o00_2100", [], ObjPorter.Kind.Breakable, byCollide ? MapBuilder.ContactBreakInstanceType : 0)
                    : new ObjPorter.Options(m, Ds2(m), "o16_1230", [], ObjPorter.Kind.Decor));
                Log.Add($"OK1 {m} → {Ds2(m)} {(breakable ? $"breakable (hp {hp}, contact break {byCollide})" : $"static with DS1 collision (hp {hp}, defense {def})")}: {rest.Count} part(s), instance 0");
                // MapBuilder's per-model loop places every part of a Breakable/Decor port: the parts placed otherwise are skipped
                // (MapBuilder.Options.ScriptedDoors = Scripted)
                foreach (var p in g.Except(rest).Where(p => p.EntityID > 0)) placed.Add(p.EntityID);
                continue;
            }
            var flv = bnd.Files.FirstOrDefault(f => f.ID == 200 && f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase));
            if (flv != null && FLVER2.Read(flv.Bytes) is var fl && (fl.Nodes.Count > 1 && fl.Meshes.Any(x => x.Dynamic != 0)))
            {
                foreach (var p in rest) StandIns.Add(("left out", p.Name, p.EntityID, $"{m}: skinned model without collision (a posed corpse): no pose rule yet"));
                continue;
            }
            if (!Ports.Any(p => p.Ds2Name == Ds2(m))) Ports.Add(new ObjPorter.Options(m, Ds2(m), WorldEvents.SceneryTemplate, [], ObjPorter.Kind.Scenery));
            foreach (var p in rest)
                Place(p, Ds2(m), p.EntityID > 0 ? Inst(p.EntityID) : Aabb * 10000 + nextFree++, "OK1: model without DS1 collision (scenery object)");
            Log.Add($"OK1 {m} → {Ds2(m)} scenery (no DS1 collision): {rest.Count} part(s)");
        }
        if (nextFree > 4999) throw new InvalidDataException($"{ds2Map}: rule OK1 ran out of instance ids (AABB4000-4999)");

        // ---- area action buttons (FirelinkScript.AreaButtons' rule; this map's helper models o{AA}_91NN)
        var areaMsgs = new List<int>();
        foreach (var x in emevd.Events.SelectMany(ev => ev.Instructions).Where(i => i.Bank == 3 && i.ID is 5 or 18))
        {
            var v = EmevdTranslator.Decode(defs, x);
            if (v.Length < 7 || (int)v[1] != 1 || (int)v[2] <= 0) continue;
            if (x.ID == 18 && v.Length > 9 && (int)v[9] > 0) continue;
            int area = (int)v[2], msg = (int)v[6];
            if (!areaMsgs.Contains(msg)) areaMsgs.Add(msg);
            if (Areas.Any(b => b.Area == area && b.Message == msg)) continue;
            int k = Areas.Count(b => b.Area == area);
            Areas.Add(new AreaButton(area, msg, k, Aabb * 10000 + 7000 + k * 1000 + area % 1000, $"o{Aa}_91{1 + areaMsgs.IndexOf(msg):D2}", Aabb * 10000 + msg % 10000));
        }
        if (areaMsgs.Count > 29 || buttonMsgs.Count > 20 || prompts.Count > 49) throw new InvalidDataException($"{ds2Map}: too many helper prompts for the o{Aa}_91NN ranges");
        Scripted = placed;
    }

    /// <summary>Entities placed one by one (MapBuilder.Options.ScriptedDoors: its per-model loops skip them).</summary>
    public HashSet<int> Scripted { get; private set; } = [];

    /// <summary>The helper placements (ObjAct helpers at their dummy, object-button helpers, area-button helpers).</summary>
    public IEnumerable<MapBuilder.Placement> HelperPlacements(string dsrRoot, MSB1 msb1)
    {
        foreach (var a in Acts.Concat(Buttons))
        {
            var (pos, rot, note) = ObjActRules.HelperPose(dsrRoot, a.Part, a.Dummy);
            yield return new MapBuilder.Placement(a.HelperModel, pos, rot, a.Helper, MapBuilder.PlacementKind.Generic, 0, $"{a.Why} key guide of {a.Part.Name} ({note}; KeyGuideParam {a.KeyGuide})");
        }
        foreach (var b in Areas)
        {
            var r = msb1.Regions.GetEntries().First(x => x.EntityID == b.Area);
            yield return new MapBuilder.Placement(b.Model, r.Position, r.Rotation, b.Helper, MapBuilder.PlacementKind.Generic, 0, $"(DS1 region {r.Name}, entity {b.Area}) key guide for DS1 action button {b.Message}");
        }
    }

    /// <summary>Rule FW1: DS2's white door (o00_0500..0505) whose model width (FLVER box X) is nearest DS1's wall model's.</summary>
    public static (string Model, string Why) WhiteDoorModel(string dsrRoot, string ds2Data, MSB1.Part.Object wall)
    {
        // width from the vertices (DS1's light-wall FLVERs carry no bounding box: their header box is infinite)
        static float W(FLVER2 f)
        {
            var xs = f.Meshes.SelectMany(m => m.Vertices).Select(v => v.Position.X).Where(float.IsFinite).ToList();
            return xs.Count == 0 ? 0 : xs.Max() - xs.Min();
        }
        var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(dsrRoot, "obj", wall.ModelName + ".objbnd.dcx"))));
        float w1 = W(FLVER2.Read(bnd.Files.First(f => f.ID == 200).Bytes)) * wall.Scale.X;
        // DS1's light walls are SFX on an empty FLVER (o1403: 544 bytes, no mesh): the wall's size is its collision's
        string source = "FLVER";
        if (w1 <= 0 && bnd.Files.FirstOrDefault(f => f.Name.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase)) is { } hkx)
        {
            w1 = HkxWidth(hkx.Bytes) * MathF.Max(wall.Scale.X, wall.Scale.Z);
            source = "collision";
        }
        whiteDoors ??= Enumerable.Range(0, 6).Select(i => $"o00_050{i}").Select(m => Path.Combine(ds2Data, "model", "obj", m + ".bnd")).Where(File.Exists)
            .Select(path => (Model: Path.GetFileNameWithoutExtension(path), Flv: BND4.Read(path).Files.FirstOrDefault(f => f.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.Flv != null).Select(x => (x.Model, Width: W(FLVER2.Read(x.Flv!.Bytes)))).Where(x => x.Width > 0.5f).ToList();
        var best = whiteDoors.MinBy(d => MathF.Abs(d.Width - w1));
        return (best.Model, $"DS1 {wall.ModelName} {w1:0.0} m wide ({source}) → {best.Model} {best.Width:0.0} m");
    }

    /// <summary>Rule FW1: the horizontal size of a DS1 object collision tagfile, the wider of its X and Z extents over every
    /// box (halfExtents) and vertex list in it (a light wall is thin along one axis); 0 if none is found.</summary>
    static float HkxWidth(byte[] bytes)
    {
        try
        {
            var tag = new Formats.Havok.HkTagfile(DCX.Is(bytes) ? DCX.Decompress(bytes) : bytes);
            var pts = new List<Vector3>();
            var seen = new HashSet<Formats.Havok.HkTagfile.HkObj>();
            static float[]? F(object? v) => v is List<object?> l && l.Count >= 3 && l.All(x => x is float || x is double) ? l.Select(System.Convert.ToSingle).ToArray() : v as float[];
            void Walk(object? v, string key)
            {
                switch (v)
                {
                    case Formats.Havok.HkTagfile.HkObj o when seen.Add(o):
                        foreach (var (k, f) in o.Fields) Walk(f, k);
                        break;
                    case List<object?> l when F(l) is { } a && key.EndsWith("alfExtents", StringComparison.Ordinal):
                        pts.Add(new Vector3(a[0], a[1], a[2])); pts.Add(new Vector3(-a[0], -a[1], -a[2]));
                        break;
                    case List<object?> l when F(l) is { } a && key.Contains("ertices", StringComparison.Ordinal) && key != "rotatedVertices":
                        pts.Add(new Vector3(a[0], a[1], a[2]));
                        break;
                    case List<object?> l:
                        foreach (var x in l) Walk(x, key);
                        break;
                    case float[] a when a.Length >= 3 && key.EndsWith("alfExtents", StringComparison.Ordinal):
                        pts.Add(new Vector3(a[0], a[1], a[2])); pts.Add(new Vector3(-a[0], -a[1], -a[2]));
                        break;
                    case float[] a when a.Length >= 3 && key.Contains("ertices", StringComparison.Ordinal) && key != "rotatedVertices":
                        pts.Add(new Vector3(a[0], a[1], a[2]));
                        break;
                }
            }
            Walk(tag.Root, "");
            pts = pts.Where(q => float.IsFinite(q.X) && float.IsFinite(q.Z)).ToList();
            if (pts.Count < 2) return 0;
            return MathF.Max(pts.Max(q => q.X) - pts.Min(q => q.X), pts.Max(q => q.Z) - pts.Min(q => q.Z));
        }
        catch (Exception) { return 0; }
    }
    static List<(string Model, float Width)>? whiteDoors;

    /// <summary>
    /// G4 template: the DS2 ladder (o00_09xx whose MapObjectDefaultInstanceParam row has objCtrlType 4) whose model height
    /// (FLVER bounding box) is nearest the DS1 ladder's (the Asylum's hand choice by height: 7.1 m → o00_0904 7.4 m,
    /// 6.0 m → o00_0903 6.2 m).
    /// </summary>
    public static (string Template, string Why) LadderTemplate(string dsrRoot, string ds2Data, string ds1Model)
    {
        float H(FLVER2 f) => f.Header.BoundingBoxMax.Y - f.Header.BoundingBoxMin.Y;
        var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(dsrRoot, "obj", ds1Model + ".objbnd.dcx"))));
        float h1 = H(FLVER2.Read(bnd.Files.First(f => f.ID == 200).Bytes));
        ladders ??= DS2Ladders(ds2Data);
        var best = ladders.MinBy(l => MathF.Abs(l.Height - h1));
        return (best.Model, $"DS1 {h1:0.0} m, DS2 {best.Height:0.0} m");
    }
    static List<(string Model, float Height)>? ladders;
    static List<(string Model, float Height)> DS2Ladders(string ds2Data)
    {
        var store = Formats.ParamStore.LoadDs2(Program.DefaultDs2Root, ds2Data);
        var def = store.Params["MapObjectDefaultInstanceParam"];
        var list = new List<(string, float)>();
        foreach (string path in Directory.GetFiles(Path.Combine(ds2Data, "model", "obj"), "o00_09*.bnd"))
        {
            string model = Path.GetFileNameWithoutExtension(path);
            if (def[ObjPorter.ModelId(model)] is not { } row || Convert.ToInt32(row["objCtrlType"].Value) != 4) continue;
            var b = BND4.Read(path);
            if (b.Files.FirstOrDefault(f => f.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase)) is not { } fl) continue;
            var f2 = FLVER2.Read(fl.Bytes);
            list.Add((model, f2.Header.BoundingBoxMax.Y - f2.Header.BoundingBoxMin.Y));
        }
        if (list.Count == 0) throw new InvalidDataException("no DS2 ladder template (o00_09xx, objCtrlType 4) found");
        return list;
    }
}
