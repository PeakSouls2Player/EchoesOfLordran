using System.Globalization;
using System.Numerics;
using Lordran.Tools.Formats.Havok;
using SoulsFormats;

namespace Lordran.Tools.Build.Events;

/// <summary>
/// Session 10 (sandbox asy-events, from the asy10 world-events prototype): the world-event object rules of
/// docs/translation-rules.md §3/§4 (WE1-WE5, WE8, C11), read from DS1's own data only, so they hold for every DS1 map:
///  - WE1 <see cref="Parts"/>: every DS1 object part whose entity a map event names in an object instruction (2005:1/2/3/
///    4/7/8/9, 2003:18, 5:0/5:1; literal or through Initialize Event arguments) and that no other rule places (doors and
///    ObjActs, ladders, treasures, decoration, the map script's own objects) is a DS2 object instance AABB0000 + entity %
///    10000. Its kind comes from DS1's objbnd: a model another rule ported as a prefab → that prefab (DS1's swap copies,
///    e.g. the boulder's rest copy o8530_0001); NAME_1 fragments → Breakable; a collision → Static; none → Scenery.
///  - WE2: DS1 objects never persist on their own (DS1 re-applies every object state from flags in its load branches):
///    their instance rows are resetType 0 "On Map Reload" (MapBuilder writes it for every Generic row).
///  - WE3 <see cref="Durability"/>: DS1 ObjectParam hp/defense → DS2 instance type: hp ≥ 999 and defense ≥ 999 → 1561 +
///    onlyEventCanBreak (only events break it: the hall floor); hp &gt; 0 and defense above every ordinary DS1 player attack
///    (<see cref="OrdinaryPlayerAtkObj"/>) → 1561 "giants only" (Oscar's wall); hp &gt; 0, defense 0 → the jar's 1906.
///  - WE4 <see cref="Damaging"/>: DS1's damaging object (2005:9) → the object's own SystemDamageParam/DamageCtrlParam rows
///    (DS1 dummy, radius, atkObj; AsylumScript/MapBuilder write them).
///  - WE4b <see cref="Hits"/>: what DS1's damaging object breaks while its animation plays (DS1's keyframed path × sphere ×
///    lifespan vs every part with ObjectParam hp &gt; 0 and defense &lt; atkObj), with DS1's contact time, so the DS2 event
///    breaks it (131613) then instead of relying on DS2's hit detection.
///  - C11 <see cref="PrefabRequests"/> / WE5 <see cref="BakedPose"/>: DS1's 2005:7 "Reproduce Object Animation" (= show
///    the animation's end pose at once). On a part DS1 also animates (2003:18 somewhere, or an MSB ObjAct, whose ObjAct
///    plays objAnimId), i.e. a DS2 prefab: request the state the prefab rests in after that animation when the template's
///    state machine reacts to a direct request of it (pfbbin condition 2205 "requested state == V"), else the fallback:
///    request the play state (the animation replays to its end; o10_1001 doors/gates: 70). On a part DS1 only
///    reproduces (never played, no ObjAct) and that no other rule places: its placement bakes the end pose and the
///    command is a no-op, whatever DS2 model it gets (the boulder's rest copy o8530_0001: the boulder's prefab model in
///    its initial state 10 = bind pose, at the baked transform; C2: never bake an ObjAct part or one another rule places).
///  - WE8 <see cref="Drop"/>: scripted floor breaks keep DS1's outcome: a DS2 no-fall-damage-once (130307) only when
///    the drop below the broken floor reaches DS2's lethal fall height (ChrMoveParam 100 fallingDistDead).
/// </summary>
public static class WorldEvents
{
    // ================================================================ DS1 object instructions
    /// <summary>Byte offset of the object entity argument per DS1 instruction (DSR layout: bytes packed, ints aligned).</summary>
    static readonly Dictionary<(int Bank, int Id), int> EntityArg = new()
    {
        [(2005, 1)] = 0, [(2005, 2)] = 0, [(2005, 3)] = 0, [(2005, 4)] = 0, [(2005, 7)] = 0, [(2005, 8)] = 0,
        [(2003, 18)] = 0, [(2005, 9)] = 4, [(5, 0)] = 4, [(5, 1)] = 4,
    };

    public sealed record Use(int Bank, int Id, int Entity, EMEVD.Instruction Ins, long Event);

    static readonly Dictionary<string, List<Use>> uses = [];

    /// <summary>Every (instruction, object entity) of the map's events, Initialize Event arguments resolved (the
    /// instruction is the substituted copy).</summary>
    public static List<Use> Uses(string dsrRoot, string ds1Map)
    {
        if (uses.TryGetValue(ds1Map, out var done)) return done;
        var list = new List<Use>();
        string path = Path.Combine(dsrRoot, "event", ds1Map + ".emevd.dcx");
        if (!File.Exists(path)) return uses[ds1Map] = list;
        var emevd = EMEVD.Read(path);
        var inits = emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2000 && i.ID == 0 && i.ArgData.Length >= 8).ToList();
        foreach (var ev in emevd.Events)
            for (int k = 0; k < ev.Instructions.Count; k++)
            {
                var ins = ev.Instructions[k];
                if (!EntityArg.TryGetValue((ins.Bank, ins.ID), out int at) || ins.ArgData.Length < at + 4) continue;
                var ps = ev.Parameters.Where(q => q.InstructionIndex == k).ToList();
                if (ps.Count == 0) { list.Add(new(ins.Bank, ins.ID, BitConverter.ToInt32(ins.ArgData, at), ins, ev.ID)); continue; }
                foreach (var init in inits.Where(i => BitConverter.ToUInt32(i.ArgData, 4) == ev.ID))
                {
                    var y = new EMEVD.Instruction(ins.Bank, ins.ID, (byte[])ins.ArgData.Clone());
                    foreach (var p in ps)
                        if (8 + p.SourceStartByte + p.ByteCount <= init.ArgData.Length)
                            Array.Copy(init.ArgData, 8 + p.SourceStartByte, y.ArgData, p.TargetStartByte, p.ByteCount);
                    list.Add(new(y.Bank, y.ID, BitConverter.ToInt32(y.ArgData, at), y, ev.ID));
                }
            }
        list.RemoveAll(u => u.Entity <= 0);
        return uses[ds1Map] = list;
    }

    /// <summary>WE1: object parts the map's events address.</summary>
    public static HashSet<int> EventObjects(string dsrRoot, string ds1Map, MSB1 msb1)
    {
        var parts = msb1.Parts.Objects.Where(p => p.EntityID > 0).Select(p => p.EntityID).ToHashSet();
        return Uses(dsrRoot, ds1Map).Select(u => u.Entity).Where(parts.Contains).ToHashSet();
    }

    /// <summary>DS1 plays an animation on the entity somewhere in the map (2003:18 Force Animation Playback).</summary>
    public static bool Played(string dsrRoot, string ds1Map, int e) => Uses(dsrRoot, ds1Map).Any(u => (u.Bank, u.Id, u.Entity) == (2003, 18, e));
    /// <summary>DS1 reproduces an animation's end pose on the entity (2005:7).</summary>
    public static bool Reproduced(string dsrRoot, string ds1Map, int e) => Uses(dsrRoot, ds1Map).Any(u => (u.Bank, u.Id, u.Entity) == (2005, 7, e));
    /// <summary>The part has an MSB ObjAct: its ObjAct plays the ObjActParam's objAnimId, i.e. something animates it (C2).</summary>
    public static bool HasObjAct(MSB1 msb1, int e) => msb1.Parts.Objects.FirstOrDefault(p => p.EntityID == e) is { } part && msb1.Events.ObjActs.Any(a => a.ObjActPartName == part.Name);

    // ================================================================ WE1-WE3 placement
    public enum Kind { Prefab, Breakable, Static, Scenery }

    /// <summary>A WE1 part: DS2 model, kind, instance, placement and instance-row fields (WE2 resetType, WE3 objType).</summary>
    public sealed record Part(int Entity, string Name, string Ds1Model, string Ds2Model, Kind Kind, int Instance, Vector3 Position,
        Vector3 Rotation, Vector3 Scale, Dictionary<string, object> Fields, string Note)
    {
        /// <summary>WE5: the placement carries the baked end pose (and the 2005:7 is a no-op).</summary>
        public bool Baked { get; init; }
    }

    /// <summary>DS2 instance type "汎用-重量∞-質量中-巨人だけ破壊" (hp 1, breakAction 10 never by contact, objAttackAttr 100:
    /// only giant attacks); with onlyEventCanBreak only events break it (vanilla 10143000/05).</summary>
    public const int GiantsOnlyType = 1561;
    /// <summary>DS2's jar instance type (o00_2100's MapObjectDefaultInstanceParam: breakAction 4, a roll breaks it).</summary>
    public const int JarType = 1906;
    /// <summary>Instance type of TB's generic event row (10021200/10021400).</summary>
    public const int GenericType = 1607;
    public const string BreakableTemplate = "o00_2100", StaticTemplate = "o16_1230", SceneryTemplate = "o02_8500";

    static readonly Dictionary<string, List<Part>> partsCache = [];

    /// <summary>
    /// WE1: the map's event-addressed object parts no other rule places. <paramref name="prefabModels"/>: DS1 model → DS2
    /// prefab model the map's script ports (its parts that no script places become instances of it: DS1's swap copies);
    /// <paramref name="otherModels"/>: DS1 models another rule places at every part (doors, ladders, decoration);
    /// <paramref name="otherEntities"/>: entities the map's script places itself. Parts with an MSB ObjAct (the ObjAct
    /// / door rules), treasure parts (the item-pickup rule) and animated models without a prefab port are left out and
    /// listed in <paramref name="log"/>.
    /// </summary>
    public static List<Part> Parts(string dsrRoot, string ds1Map, string ds2Map, MSB1 msb1, IReadOnlyDictionary<string, string> prefabModels,
        IReadOnlySet<string> otherModels, IReadOnlySet<int> otherEntities, List<string>? log = null)
    {
        string key = $"{ds1Map}>{ds2Map}";
        if (log == null && partsCache.TryGetValue(key, out var cached)) return cached;
        var result = new List<Part>();
        int prefix = int.Parse(ds2Map[1..3] + ds2Map[4..6]);
        var treasureParts = msb1.Events.Treasures.Select(t => t.TreasurePartName).ToHashSet();
        foreach (int e in EventObjects(dsrRoot, ds1Map, msb1).Order())
        {
            var part = msb1.Parts.Objects.First(p => p.EntityID == e);
            string m = part.ModelName;
            string Skip(string why) { log?.Add($"WE1 {part.Name} (entity {e}): left to {why}"); return why; }
            if (otherEntities.Contains(e)) { Skip("the map's script"); continue; }
            if (otherModels.Contains(m)) { Skip("the model's own rule (door, ladder, decoration)"); continue; }
            if (treasureParts.Contains(part.Name)) { Skip("the item-pickup rule (treasure part)"); continue; }
            if (HasObjAct(msb1, e)) { Skip("the ObjAct / door rules (MSB ObjAct)"); continue; }
            int inst = prefix * 10000 + e % 10000;
            var fields = new Dictionary<string, object> { ["resetType"] = 0 }; // WE2
            // WE5 (C11): a part DS1 only reproduces (2005:7, never 2003:18, no ObjAct) shows the clip's end pose: its
            // placement bakes it, whatever DS2 model it gets. Fix pass (COVERAGE C16, binding): only when DS1 keeps the part
            // hidden until its reproduce (HiddenAtLoad: disabled in every fresh load-time walk before any enable/reproduce),
            // so the baked pose is never on screen before DS1 would show it (the Kiln door 1801101 in m18_00 is not: left
            // unbaked, reported; world-events rev. 2's pose copy is needed there). A prefab copy (the bake keeps the prefab
            // in its initial state) only when that state is the bind pose AND DS1's clip starts at the bind pose
            // (BindPoseStart: first frame of the mesh track = identity), so bind pose × the end transform = DS1's end pose
            (Vector3 Position, Vector3 Rotation)? baked = null;
            string bakeNote = "";
            bool isPrefab = prefabModels.TryGetValue(m, out var prefab);
            if (Reproduced(dsrRoot, ds1Map, e) && !Played(dsrRoot, ds1Map, e))
            {
                int anim = Uses(dsrRoot, ds1Map).Where(u => (u.Bank, u.Id, u.Entity) == (2005, 7, e)).Select(u => BitConverter.ToInt32(u.Ins.ArgData, 4)).First();
                var hidden = HiddenAtLoad(dsrRoot, ds1Map, e);
                if (hidden != true)
                    bakeNote = $", WE5 NOT baked: DS1 {(hidden == false ? "shows it at load" : "never hides it at load")} (C16: needs the pose copy, U; the 2005:7 has no rule)";
                else if (isPrefab && BindPoseStart(dsrRoot, part, anim) != true)
                    bakeNote = $", WE5 NOT baked: prefab copy whose a00_{anim:D4} does not start at the bind pose (C16, U)";
                else
                {
                    baked = BakedPose(dsrRoot, part, anim);
                    bakeNote = baked != null ? $", WE5 end pose of a00_{anim:D4} baked (hidden at load{(isPrefab ? "; prefab copy: clip starts at the bind pose" : "")})" : $", WE5: no a00_{anim:D4} to bake (DS1 pose kept)";
                }
            }
            if (isPrefab)
            {
                result.Add(new(e, part.Name, m, prefab!, Kind.Prefab, inst, baked?.Position ?? part.Position, baked?.Rotation ?? part.Rotation, part.Scale, fields,
                    $"swap copy of the prefab {prefab}{bakeNote}") { Baked = baked != null });
                continue;
            }
            string bndPath = Path.Combine(dsrRoot, "obj", m + ".objbnd.dcx");
            if (!File.Exists(bndPath)) { Skip("nothing: no objbnd"); continue; }
            var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(bndPath)));
            bool Has(string n) => bnd.Files.Any(f => Path.GetFileName(f.Name).Equals(n, StringComparison.OrdinalIgnoreCase));
            bool animated = bnd.Files.Any(f => f.Name.EndsWith(".anibnd", StringComparison.OrdinalIgnoreCase));
            if (animated && Played(dsrRoot, ds1Map, e)) { Skip("nothing yet: an event plays its animation and no prefab rule ports the model (U)"); continue; }
            Kind kind = Has(m + "_1.flver") && Has(m + "_1.hkx") ? Kind.Breakable : Has(m + ".hkx") ? Kind.Static : Kind.Scenery;
            string ds2Model = Maps.MapHosts.ObjectName(ds2Map.Substring(1, 2), m);
            var pos = baked?.Position ?? part.Position;
            var rot = baked?.Rotation ?? part.Rotation;
            string note = $"{kind} ({(kind switch { Kind.Breakable => "DS1 fragments", Kind.Static => "DS1 collision", _ => "model only" })}){bakeNote}";
            if (kind == Kind.Breakable && Durability(dsrRoot, m) is var (type, onlyEvent, why))
            {
                fields["objType"] = type;
                if (onlyEvent) fields["onlyEventCanBreak"] = 1;
                note += $", WE3 {why}";
            }
            result.Add(new(e, part.Name, m, ds2Model, kind, inst, pos, rot, part.Scale, fields, note) { Baked = baked != null });
        }
        foreach (var p in result) log?.Add($"WE1 {p.Name} (entity {p.Entity}) → {p.Ds2Model} instance {p.Instance}: {p.Note}");
        return partsCache[key] = result;
    }

    /// <summary>The ObjPorter template per WE1 kind (prefab copies use the prefab's own port).</summary>
    public static string? Template(Kind k) => k switch { Kind.Breakable => BreakableTemplate, Kind.Static => StaticTemplate, Kind.Scenery => SceneryTemplate, _ => null };

    /// <summary>
    /// WE3: DS1 ObjectParam (row = model id) → DS2 instance type for an event-addressed breakable: (type, only events break
    /// it, reason). DS1's ObjectParam: hp, defense; DS1 breaks a part when an attack's atkObj exceeds its defense.
    /// </summary>
    public static (int Type, bool OnlyEvent, string Why) Durability(string dsrRoot, string ds1Model)
    {
        var row = Ds1Source.LoadParam(dsrRoot, "ObjectParam")?[int.Parse(ds1Model[1..])];
        if (row == null) return (JarType, false, "no DS1 ObjectParam row: jar type");
        int hp = Convert.ToInt32(row["hp"].Value), def = Convert.ToInt32(row["defense"].Value);
        int ordinary = OrdinaryPlayerAtkObj(dsrRoot);
        if (hp >= 999 && def >= 999) return (GiantsOnlyType, true, $"hp {hp} defense {def}: only events break it → {GiantsOnlyType} + onlyEventCanBreak");
        if (hp > 0 && def > ordinary) return (GiantsOnlyType, false, $"hp {hp} defense {def} > every ordinary DS1 player attack (atkObj ≤ {ordinary}) → {GiantsOnlyType} (giants only)");
        if (hp > 0 && def == 0) return (JarType, false, $"hp {hp} defense 0 → jar type {JarType}");
        return (JarType, false, $"hp {hp} defense {def}: no DS2 class yet (U) → jar type {JarType}");
    }

    static int? ordinaryAtkObj;
    /// <summary>
    /// The strongest object attack an ordinary DS1 player attack has: the largest AtkParam_Pc.atkObj used by at least 1 %
    /// of the rows (DSR: 0 ×96, 10 ×190, 80 ×209, 90 ×1, 100 ×9 of 1397 → 80; the rare 90/100 rows are summon and throw
    /// commons).
    /// </summary>
    public static int OrdinaryPlayerAtkObj(string dsrRoot)
    {
        if (ordinaryAtkObj is int v) return v;
        var p = Ds1Source.LoadParam(dsrRoot, "AtkParam_Pc");
        if (p == null) return (ordinaryAtkObj = 80).Value;
        var counts = p.Rows.GroupBy(r => Convert.ToInt32(r["atkObj"].Value)).ToDictionary(g => g.Key, g => g.Count());
        return (ordinaryAtkObj = counts.Where(c => c.Value * 100 >= p.Rows.Count).Select(c => c.Key).DefaultIfEmpty(80).Max()).Value;
    }

    // ================================================================ WE5 baked pose
    /// <summary>DS1 MSB part transform (MapBuilder's convention: scale, Z, X, Y rotation, translation; row vectors).</summary>
    public static Matrix4x4 PartMatrix(MSB1.Part p) =>
        Matrix4x4.CreateScale(p.Scale)
        * Matrix4x4.CreateRotationZ(p.Rotation.Z * MathF.PI / 180) * Matrix4x4.CreateRotationX(p.Rotation.X * MathF.PI / 180) * Matrix4x4.CreateRotationY(p.Rotation.Y * MathF.PI / 180)
        * Matrix4x4.CreateTranslation(p.Position);

    /// <summary>
    /// WE5: the part's placement showing the last frame of its DS1 animation a00_ANIM: part matrix × the local transform of
    /// the model's first animated track (the mesh's node) at the last frame → position + Euler degrees in the part
    /// convention (X = asin(−M32), Y = atan2(M31, M33), Z = atan2(M12, M22)). Null when the objbnd has no such clip.
    /// Self-test: the boulder's rest copy o8530_0001 → (-12.363, 201.917, 18.086) / (48.6, 152.4, -111.4) (asy10 bake_rest.py).
    /// </summary>
    public static (Vector3 Position, Vector3 Rotation)? BakedPose(string dsrRoot, MSB1.Part.Object part, int anim)
    {
        string bndPath = Path.Combine(dsrRoot, "obj", part.ModelName + ".objbnd.dcx");
        if (!File.Exists(bndPath)) return null;
        var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(bndPath)));
        var abf = bnd.Files.FirstOrDefault(f => f.Name.EndsWith(".anibnd", StringComparison.OrdinalIgnoreCase));
        if (abf == null) return null;
        var hkx = BND3.Read(abf.Bytes).Files.FirstOrDefault(f => Path.GetFileName(f.Name).Equals($"a00_{anim:D4}.hkx", StringComparison.OrdinalIgnoreCase));
        if (hkx == null) return null;
        var a = new HkSplineAnim(new HkTagfile(hkx.Bytes));
        var flv = FLVER2.Read(bnd.Files.First(f => f.ID == 200).Bytes);
        // the track of the node the meshes are bound to (DS1 rigid object meshes: one bone each; the boulder's o1330_00)
        var meshNodes = flv.Meshes.SelectMany(ms => ms.BoneIndices.Count > 0 ? ms.BoneIndices : [ms.NodeIndex])
            .Where(i => i >= 0 && i < flv.Nodes.Count).Select(i => flv.Nodes[i].Name).ToHashSet();
        int track = Enumerable.Range(0, a.TrackNames.Length).Where(t => meshNodes.Contains(a.TrackNames[t])).DefaultIfEmpty(-1).First();
        if (track < 0) return null;
        var (T, R, S) = a.Sample(track, a.NumFrames - 1);
        var m = Matrix4x4.CreateScale(S) * Matrix4x4.CreateFromQuaternion(R) * Matrix4x4.CreateTranslation(T) * PartMatrix(part);
        float x = MathF.Asin(Math.Clamp(-m.M32, -1, 1)), y = MathF.Atan2(m.M31, m.M33), z = MathF.Atan2(m.M12, m.M22);
        const float D = 180 / MathF.PI;
        return (m.Translation, new Vector3(x * D, y * D, z * D));
    }

    /// <summary>
    /// WE5 prefab-copy condition (fix pass, C16): DS1's clip a00_ANIM starts at the bind pose on the mesh track (frame 0 =
    /// identity within 1 cm / 0.1°), so a prefab copy in its initial state (bind pose) at the baked end transform shows DS1's
    /// end pose. Null when there is no such clip/track. The boulder's rest copy: frame 0 t = (-0.003, 0, 0), r = identity.
    /// </summary>
    public static bool? BindPoseStart(string dsrRoot, MSB1.Part.Object part, int anim)
    {
        string bndPath = Path.Combine(dsrRoot, "obj", part.ModelName + ".objbnd.dcx");
        if (!File.Exists(bndPath)) return null;
        var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(bndPath)));
        var abf = bnd.Files.FirstOrDefault(f => f.Name.EndsWith(".anibnd", StringComparison.OrdinalIgnoreCase));
        var hkx = abf == null ? null : BND3.Read(abf.Bytes).Files.FirstOrDefault(f => Path.GetFileName(f.Name).Equals($"a00_{anim:D4}.hkx", StringComparison.OrdinalIgnoreCase));
        if (hkx == null) return null;
        var a = new HkSplineAnim(new HkTagfile(hkx.Bytes));
        var flv = FLVER2.Read(bnd.Files.First(f => f.ID == 200).Bytes);
        var meshNodes = flv.Meshes.SelectMany(ms => ms.BoneIndices.Count > 0 ? ms.BoneIndices : [ms.NodeIndex])
            .Where(i => i >= 0 && i < flv.Nodes.Count).Select(i => flv.Nodes[i].Name).ToHashSet();
        int track = Enumerable.Range(0, a.TrackNames.Length).Where(t => meshNodes.Contains(a.TrackNames[t])).DefaultIfEmpty(-1).First();
        if (track < 0) return null;
        var (T, R, S) = a.Sample(track, 0);
        return T.Length() < 0.01f && MathF.Abs(R.W) > 0.99999f && Vector3.Distance(S, Vector3.One) < 0.001f;
    }

    static readonly Dictionary<string, List<(long Event, List<EMEVD.Instruction> Ins)>> instancesCache = [];
    /// <summary>Every run of the map's events: an event with parameters once per Initialize Event (arguments substituted),
    /// one without parameters as it is.</summary>
    public static List<(long Event, List<EMEVD.Instruction> Ins)> Instances(string dsrRoot, string ds1Map)
    {
        if (instancesCache.TryGetValue(ds1Map, out var done)) return done;
        var list = new List<(long, List<EMEVD.Instruction>)>();
        string path = Path.Combine(dsrRoot, "event", ds1Map + ".emevd.dcx");
        if (File.Exists(path))
        {
            var emevd = EMEVD.Read(path);
            var inits = emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2000 && i.ID == 0 && i.ArgData.Length >= 8).ToList();
            foreach (var ev in emevd.Events)
            {
                if (ev.Parameters.Count == 0) { list.Add((ev.ID, ev.Instructions.ToList())); continue; }
                foreach (var init in inits.Where(i => BitConverter.ToUInt32(i.ArgData, 4) == ev.ID))
                {
                    var copy = new List<EMEVD.Instruction>();
                    for (int k = 0; k < ev.Instructions.Count; k++)
                    {
                        var ins = ev.Instructions[k];
                        var y = new EMEVD.Instruction(ins.Bank, ins.ID, (byte[])ins.ArgData.Clone());
                        foreach (var p in ev.Parameters.Where(q => q.InstructionIndex == k))
                            if (8 + p.SourceStartByte + p.ByteCount <= init.ArgData.Length && p.TargetStartByte + p.ByteCount <= y.ArgData.Length)
                                Array.Copy(init.ArgData, 8 + p.SourceStartByte, y.ArgData, p.TargetStartByte, p.ByteCount);
                        copy.Add(y);
                    }
                    list.Add((ev.ID, copy));
                }
            }
        }
        return instancesCache[ds1Map] = list;
    }

    /// <summary>
    /// WE5 gate (fix pass, COVERAGE C16): DS1's first object state of part <paramref name="e"/> on a fresh world. Every event
    /// run is walked from its first instruction with every event flag OFF (a fresh world: SKIP IF flag OFF taken, END IF
    /// flag OFF ends), through SKIP n, non-MAIN condition registrations and plain commands, up to its first MAIN wait / END /
    /// a test the walk can't decide (multiplayer, condition groups, batches: the walk stops there, conservatively). True =
    /// some run disables the part (2005:3 state 0) before anything shows it and no run shows it first (2005:3 state 1,
    /// 2005:7, 2005:1/8); false = some run shows it first; null = no run touches it at load (it keeps its MSB visibility, i.e.
    /// it is on screen before the reproduce). Asylum: the boulder's rest copy 1811216 → true (11810211 [0] SKIP 3 IF own
    /// flag OFF → [4] disable); m18_00's Kiln door 1801101 → null (11800210 reproduces it only after the Lord Souls).
    /// </summary>
    public static bool? HiddenAtLoad(string dsrRoot, string ds1Map, int e)
    {
        var defs = Commands.EmevdDump.Defs();
        bool hid = false;
        foreach (var (_, ins) in Instances(dsrRoot, ds1Map))
        {
            for (int i = 0; i < ins.Count;)
            {
                var x = ins[i];
                double[] v;
                try { v = EmevdTranslator.Decode(defs, x); } catch { break; }
                int Ent(int k) => k < v.Length ? (int)v[k] : 0;
                bool stop = false;
                switch (x.Bank, x.ID)
                {
                    case (1003, 1): i += v[1] == 0 ? 1 + (int)v[0] : 1; continue;     // SKIP IF flag: every flag OFF
                    case (1003, 2): if (v[1] == 0) stop = true; break;                // END IF flag OFF: ends
                    case (1000, 3): i += 1 + (int)v[0]; continue;                       // SKIP n
                    case (1000, 4): stop = true; break;                                 // END / RESTART
                    case (2005, 3) when Ent(0) == e:
                        if (v[1] != 0) return false;
                        hid = true; stop = true; break;
                    case (2005, 7) or (2005, 1) or (2005, 8) when Ent(0) == e: return false;
                    case (0 or 1 or 3 or 4 or 5 or 11, _): if (v.Length == 0 || (int)v[0] == 0) stop = true; break; // a MAIN wait
                    case (1000, _) or (1001, _) or (1003, _): stop = true; break;      // undecidable tests / waits
                }
                if (stop) break;
                i++;
            }
        }
        return hid ? true : null;
    }

    // ================================================================ WE4 / WE4b damaging objects
    /// <summary>DS1's 2005:9 on the entity: (flag, dummy, behaviour, target type, radius, lifespan) + the attack's
    /// AtkParam_Npc row (atkObj, atkPhys), or null.</summary>
    public sealed record Damage(int Dummy, int Behavior, int Target, float Radius, float Lifespan, int AtkId, int AtkObj, int AtkPhys);

    public static Damage? Damaging(string dsrRoot, string ds1Map, int entity)
    {
        var dmg = Uses(dsrRoot, ds1Map).Where(u => (u.Bank, u.Id, u.Entity) == (2005, 9, entity)).Select(u => u.Ins).FirstOrDefault();
        if (dmg == null) return null;
        var v = EmevdTranslator.Decode(Commands.EmevdDump.Defs(), dmg); // flag, entity, dummy, behavior, target, radius, lifespan, repeat
        int behavior = (int)v[3];
        var beh = Ds1Source.LoadParam(dsrRoot, "BehaviorParam")?[behavior];
        int atkId = beh != null ? Convert.ToInt32(beh["refId"].Value) : -1;
        var atk = atkId >= 0 ? Ds1Source.LoadParam(dsrRoot, "AtkParam_Npc")?[atkId] : null;
        return new Damage((int)v[2], behavior, (int)v[4], (float)v[5], (float)v[6], atkId,
            atk != null ? Convert.ToInt32(atk["atkObj"].Value) : 0, atk != null ? Convert.ToInt32(atk["atkPhys"].Value) : 0);
    }

    public sealed record Hit(int Target, float Seconds, float Distance, int AtkObj, int Defense, string Note);

    /// <summary>Session 11 (merge11, rule WE4c): the duration (s) of DS1 object <paramref name="model"/>'s clip a00_ANIM, or null.</summary>
    public static float? ClipSeconds(string dsrRoot, string model, int anim)
    {
        string path = Path.Combine(dsrRoot, "obj", model + ".objbnd.dcx");
        if (!File.Exists(path)) return null;
        var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(path)));
        var anibndFile = bnd.Files.FirstOrDefault(f => f.Name.EndsWith(".anibnd", StringComparison.OrdinalIgnoreCase));
        var hkx = anibndFile == null ? null : BND3.Read(anibndFile.Bytes).Files.FirstOrDefault(f => Path.GetFileName(f.Name).Equals($"a00_{anim:D4}.hkx", StringComparison.OrdinalIgnoreCase));
        return hkx == null ? null : (float)new HkSplineAnim(new HkTagfile(hkx.Bytes)).Duration;
    }

    /// <summary>WE4b: what DS1's damaging object on <paramref name="entity"/> breaks while animation <paramref name="anim"/>
    /// plays (first contact time per target), from DS1's own data. Swap-in parts (some event enables them with 2005:3
    /// state 1: DS1 keeps them disabled until then, e.g. o8520_0001) are not targets.</summary>
    public static List<Hit> Hits(string dsrRoot, string ds1Map, MSB1 msb1, int entity, int anim, List<string>? log = null)
    {
        var result = new List<Hit>();
        var part = msb1.Parts.Objects.FirstOrDefault(p => p.EntityID == entity);
        if (Damaging(dsrRoot, ds1Map, entity) is not { } d || part == null) return result;
        log?.Add($"2005:9 on {entity}: dummy {d.Dummy}, behaviour {d.Behavior} → AtkParam_Npc {d.AtkId} atkObj {d.AtkObj}, radius {d.Radius}, lifespan {d.Lifespan} s");
        if (d.AtkObj <= 0) return result;
        var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(dsrRoot, "obj", part.ModelName + ".objbnd.dcx"))));
        var flv = FLVER2.Read(bnd.Files.First(f => f.ID == 200).Bytes);
        var dummy = flv.Dummies.FirstOrDefault(x => x.ReferenceID == d.Dummy);
        if (dummy == null) { log?.Add($"  no dummy {d.Dummy}"); return result; }
        string node = flv.Nodes[Math.Max(0, (int)dummy.ParentBoneIndex)].Name;
        var anibndFile = bnd.Files.FirstOrDefault(f => f.Name.EndsWith(".anibnd", StringComparison.OrdinalIgnoreCase));
        if (anibndFile == null) return result;
        var hkx = BND3.Read(anibndFile.Bytes).Files.FirstOrDefault(f => Path.GetFileName(f.Name).Equals($"a00_{anim:D4}.hkx", StringComparison.OrdinalIgnoreCase));
        if (hkx == null) { log?.Add($"  no animation a00_{anim:D4}"); return result; }
        var a = new HkSplineAnim(new HkTagfile(hkx.Bytes));
        int track = Array.IndexOf(a.TrackNames, node);
        log?.Add($"  dummy on node {node} = track {track}; animation {a.Duration:0.###} s, {a.NumFrames} frames");
        var world = PartMatrix(part);
        var op = Ds1Source.LoadParam(dsrRoot, "ObjectParam");
        var swapIn = Uses(dsrRoot, ds1Map).Where(u => (u.Bank, u.Id) == (2005, 3) && u.Ins.ArgData.Length >= 5 && u.Ins.ArgData[4] == 1).Select(u => u.Entity).ToHashSet();
        var cands = new List<(MSB1.Part.Object P, int Def, Matrix4x4 Inv, Vector3 Min, Vector3 Max)>();
        foreach (var t in msb1.Parts.Objects.Where(p => p.EntityID > 0 && p.EntityID != entity && !swapIn.Contains(p.EntityID)))
        {
            var row = op?[int.Parse(t.ModelName[1..])];
            if (row == null) continue;
            int hp = Convert.ToInt32(row["hp"].Value), def = Convert.ToInt32(row["defense"].Value);
            if (hp <= 0 || def >= d.AtkObj) continue;
            string tb = Path.Combine(dsrRoot, "obj", t.ModelName + ".objbnd.dcx");
            if (!File.Exists(tb)) continue;
            var tf = FLVER2.Read(BND3.Read(DCX.Decompress(File.ReadAllBytes(tb))).Files.First(f => f.ID == 200).Bytes);
            if (!Matrix4x4.Invert(PartMatrix(t), out var inv)) continue;
            cands.Add((t, def, inv, tf.Header.BoundingBoxMin, tf.Header.BoundingBoxMax));
        }
        float end = Math.Min(d.Lifespan, a.Duration);
        foreach (var c in cands)
            for (float time = 0; time <= end + 1e-4f; time += 1f / 60)
            {
                var (T, R, S) = track >= 0 ? a.Sample(track, Math.Min(time / a.FrameDuration, a.NumFrames - 1)) : (Vector3.Zero, Quaternion.Identity, Vector3.One);
                var local = Matrix4x4.CreateScale(S) * Matrix4x4.CreateFromQuaternion(R) * Matrix4x4.CreateTranslation(T);
                var centre = Vector3.Transform(Vector3.Transform(dummy.Position, local), world);
                var q = Vector3.Transform(centre, c.Inv);
                float dist = Vector3.Distance(q, Vector3.Clamp(q, c.Min, c.Max));
                if (dist <= d.Radius)
                {
                    result.Add(new Hit(c.P.EntityID, time, dist, d.AtkObj, c.Def,
                        $"{c.P.Name} (defense {c.Def} < atkObj {d.AtkObj}): sphere centre ({centre.X:0.00}, {centre.Y:0.00}, {centre.Z:0.00}) {dist:0.00} m from its box at {time:0.00} s"));
                    break;
                }
            }
        foreach (var h in result) log?.Add("  hit " + h.Note);
        return result;
    }

    // ================================================================ C11 prefab requests
    static readonly Dictionary<string, HashSet<int>> requests = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// C11: the states a DS2 prefab template's state machine (prefabeditor/TEMPLATE.pfbbin) changes to on an event request
    /// (131636): every transition condition record (u32 2, u32 2205, u32 0, f32 state). Survey (vanilla, session 10):
    /// o10_1001 [70, 80], o10_9240 [20, 70], o29_1001 / o15_1060 / o35_0702 [10, 30, 70, 80], o17_1060 [20, 70]; vanilla
    /// requests rest states directly in its reproduce machines (m10_29 【再現】…ギミック扉 131636(o29_1001, 30), m10_17
    /// 【再現】毒水低下 131636(o17_1060, 20), m50_35 【再現】フタ付き昇降機_起動 131636(o35_0702, 30)); the rest state's entry
    /// action 5008(anim, 16, 0) holds that animation's end pose (o10_9240 state 20: 5008(1, 16, 0)), so a direct request
    /// shows the end pose without playing the animation (P: action meaning read from the data pattern).
    /// </summary>
    public static IReadOnlySet<int> PrefabRequests(string ds2Data, string template)
    {
        if (requests.TryGetValue(template, out var done)) return done;
        var set = new HashSet<int>();
        string path = Path.Combine(ds2Data, "prefabeditor", template + ".pfbbin");
        if (File.Exists(path))
        {
            var b = File.ReadAllBytes(path);
            for (int i = 0; i + 16 <= b.Length; i += 4)
                if (BitConverter.ToUInt32(b, i) == 2 && BitConverter.ToUInt32(b, i + 4) == 2205 && BitConverter.ToUInt32(b, i + 8) == 0)
                {
                    float v = BitConverter.ToSingle(b, i + 12);
                    if (v == MathF.Round(v) && v is >= 0 and < 1000) set.Add((int)v);
                }
        }
        return requests[template] = set;
    }

    // ================================================================ WE8 scripted drops
    static float? deadFall;
    static readonly Dictionary<string, Maps.FloorProbe> probes = [];
    /// <summary>DS2's lethal fall height for the player (ChrMoveParam 100 "Player Character" fallingDistDead: 19.5 m).</summary>
    public static float DeadFallHeight()
    {
        if (deadFall is float f) return f;
        try
        {
            var store = Formats.ParamStore.LoadDs2(Program.DefaultDs2Root, Commands.ExtractDs2.DefaultOut);
            if (store.Params.TryGetValue("ChrMoveParam", out var p) && p[100] is { } row)
                return (deadFall = Convert.ToSingle(row["fallingDistDead"].Value, CultureInfo.InvariantCulture)).Value;
        }
        catch { }
        return (deadFall = 19.5f).Value;
    }

    /// <summary>
    /// WE8: the fall when DS1 breaks the floor object <paramref name="entity"/> under the player: the part's height (DS1
    /// floor objects stand on their surface: o8510 at 197.51) minus the highest DS1 walkable map collision (hit filter 8)
    /// below its centre. Null when nothing is below (a fall out of the world: DS1 kills too).
    /// </summary>
    public static float? Drop(string dsrRoot, string ds1Map, MSB1 msb1, int entity)
    {
        var part = msb1.Parts.Objects.FirstOrDefault(p => p.EntityID == entity);
        if (part == null) return null;
        if (!probes.TryGetValue(ds1Map, out var probe)) probes[ds1Map] = probe = Maps.FloorProbe.FromDs1(dsrRoot, ds1Map);
        var below = probe.Down(part.Position.X, part.Position.Z).Where(h => h.Y < part.Position.Y - 0.5f && h.NormalY >= 0.5f).Select(h => h.Y).DefaultIfEmpty(float.NaN).Max();
        return float.IsNaN(below) ? null : part.Position.Y - below;
    }
}
