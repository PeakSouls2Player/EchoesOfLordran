using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Events;

/// <summary>
/// Session 10 (sandbox asy-events, item e2 "doors + ladders"): the map-agnostic DS1 ObjAct and ladder rules of
/// data\sandbox\asy10\doors.md (K1, K3, K5, K10′, K12) and COVERAGE G4, read from DS1's own data only (MSB ObjActs, ObjActParam,
/// MSB regions, FLVER dummies, the map's EMEVD). Map contexts (AsylumContext; FirelinkContext once its doors are ported)
/// call these instead of hand-placing or hand-pairing anything.
/// </summary>
public static class ObjActRules
{
    /// <summary>
    /// The requirement model of one DS1 MSB ObjAct (doors.md §4): ObjAct entity A (the flag 5:2 waits for; DS1 sets it in
    /// its own event), state flag S (MSB EventFlagID, 0 if none), the ObjActParam row (the MSB's param id, else the model
    /// number) with its requirement (Type/Id; Type2/Id2 = an OR alternative, e.g. the Master Key), pass flag, fail text,
    /// the object animation the ObjAct plays and the FLVER dummy the player acts at.
    /// </summary>
    public sealed record Req(MSB1.Event.ObjAct Act, MSB1.Part.Object Part, int A, int S, int ParamRow, int Type, int Id, int Type2, int Id2,
        int Pass, int FailMsg, int AnimId, int DummyId, Prompt Prompt);

    /// <summary>
    /// The player side of a DS1 ObjActParam row (fix pass e2, finding 3): the prompt text (actionEnableMsgId, DS1 Event_text),
    /// the reach (validDist in cm: the ladders' rows 0 / 10000 say 150 = the ReactionDistance 1.5 m of DS1's own 3:5 on its
    /// kick ladders, V-data), the facing cone (validPlayerAngle, degrees) and the player animation DS1 plays (playerAnimId:
    /// 7110 door push, 7120 two-leaf door, 8000 lever). DS2's helper route plays no player animation (deviation, rule K6).
    /// </summary>
    public sealed record Prompt(int Msg, float Dist, int Angle, int PlayerAnim);

    public static Req? Of(MSB1 msb1, PARAM objActParam, MSB1.Event.ObjAct a)
    {
        var part = msb1.Parts.Objects.FirstOrDefault(p => p.Name == a.ObjActPartName);
        if (part == null) return null;
        int rowId = a.ObjActParamID >= 0 ? a.ObjActParamID : int.Parse(part.ModelName[1..]);
        if (objActParam[rowId] is not { } row) return null;
        int I(string f) => Convert.ToInt32(row[f].Value);
        return new Req(a, part, a.ObjActEntityID, Math.Max(0, a.EventFlagID), rowId, I("spQualifiedType"), I("spQualifiedId"), I("spQualifiedType2"),
            I("spQualifiedId2"), I("spQualifiedPassEventFlag"), I("actionFailedMsgId"), I("objAnimId"), I("objDummyId"),
            new Prompt(I("actionEnableMsgId"), I("validDist") / 100f, I("validPlayerAngle"), I("playerAnimId")));
    }

    /// <summary>Rule K1 (doors.md rev. 2): the DS2 object kind of an ObjAct part from DS1's MSB ObjAct type.</summary>
    public enum Kind { Door, Prefab, Treasure }

    /// <summary>
    /// K1: <c>ObjActState</c> Door → a DS2 door (objCtrlType 2, the cell-door route, V); Default / Loop → a prefab object +
    /// an ObjAct helper (the hall-door route, V); chests o0110 (Default ×81 in DSR) belong to the treasure rule. Census over
    /// the 35 DSR maps: 45 Door (every single-leaf swinging door), 130 Default (chests, levers, every two-leaf door: o5310,
    /// o8541, o8544), 2 Loop (asy10\scratch-doors\msb-objacts-all.txt).
    /// </summary>
    public static Kind KindOf(MSB1.Event.ObjAct a, MSB1.Part.Object part) =>
        part.ModelName == "o0110" ? Kind.Treasure : a.ObjActState == MSB1.Event.ObjAct.StateType.Door ? Kind.Door : Kind.Prefab;

    /// <summary>
    /// Rule K3 (doors.md rev. 2, "opens only from this side"): a SpEffect requirement (ObjActParam spQualifiedType 2, DS1's
    /// 4150 "Inside Key of Universal door") is a SIDE test. The grant slot paired with the door is an Initialize Event slot
    /// of the map (arguments substituted) whose event sets that SpEffect on the player (2004:8) AND tests the door's ObjAct
    /// flag A (3:0 / 1003:1 / 1003:2). Running that slot abstractly with A OFF (SKIP/END on A resolved; condition groups
    /// holding "A ON" unsatisfiable; other tests both ways) gives the regions (3:2 inside, player) that gate a reachable
    /// 2004:8 = the "no key / inside" side. DS1's "after A, from both sides" branch is DS2's unlocked door (nothing to emit).
    /// Returns the DS1 region entity when exactly one is found, else null (the reasons in <paramref name="log"/>).
    /// Checked on all five DS1 SpEffect doors: m18_01 o8540_0005 → 1812020 (11810120, AND/OR shape), m10_01 o1314_0000 →
    /// 1012401, m15_01 o5720_0001/0007/0008 → 1512251/1512253/1512255 (11010150 / 11510260: SKIP-IF-A shape).
    /// </summary>
    public static int? InnerRegion(EMEVD emevd, Dictionary<(int, int), (string Name, int[] Types)> defs, int a, int sp, List<string> log)
    {
        var found = new HashSet<int>();
        int slots = 0;
        foreach (var (ev, slot, args) in Slots(emevd))
        {
            var ins = ev.Instructions.Select((x, i) => EmevdTranslator.Substitute(ev, i, x, args)).ToList();
            bool grants = ins.Any(x => x is { Bank: 2004, ID: 8 } && V(defs, x) is var v && (int)v[0] == EmevdTranslator.Player && (int)v[1] == sp);
            bool testsA = ins.Any(x => (x.Bank, x.ID) is (3, 0) or (1003, 1) or (1003, 2) && V(defs, x) is var v && v.Length > 3 && (int)v[2] == 0 && (int)v[3] == a);
            if (!grants || !testsA) continue;
            slots++;
            var regions = new HashSet<int>();
            Walk(ins, defs, a, sp, 0, [], null, regions, log, $"event {ev.ID} slot {slot}", 0);
            log.Add($"K3: door flag {a}: grant event {ev.ID} slot {slot} → inside region(s) {(regions.Count == 0 ? "none" : string.Join(", ", regions))}");
            found.UnionWith(regions);
        }
        if (slots == 0) log.Add($"K3: no grant slot sets SpEffect {sp} and tests door flag {a} (no lock region)");
        if (found.Count > 1) log.Add($"K3: door flag {a}: several inside regions ({string.Join(", ", found)}): no rule");
        return found.Count == 1 ? found.First() : null;
    }

    /// <summary>Every (event, slot, args) the map runs: events 0 and 50 themselves and every Initialize Event they issue.</summary>
    public static IEnumerable<(EMEVD.Event Ev, int Slot, byte[] Args)> Slots(EMEVD emevd)
    {
        var byId = emevd.Events.ToDictionary(e => e.ID);
        var seen = new HashSet<(long, int)>();
        foreach (long root in new long[] { 0, 50 })
        {
            if (!byId.TryGetValue(root, out var r)) continue;
            if (seen.Add((root, 0))) yield return (r, 0, []);
            foreach (var x in r.Instructions.Where(i => i.Bank == 2000 && i.ID == 0))
            {
                long id = BitConverter.ToUInt32(x.ArgData, 4);
                int slot = BitConverter.ToInt32(x.ArgData, 0);
                if (byId.TryGetValue(id, out var ev) && seen.Add((id, slot))) yield return (ev, slot, x.ArgData[8..]);
            }
        }
    }

    // a condition term of the abstract run: a region (inside, player), always true, never true, unknown, or a group
    sealed record Term(int Region = 0, bool Sat = true, bool Unknown = false, int Group = 0);

    /// <summary>(satisfiable, regions that gate it) of a condition group under "A OFF".</summary>
    static (bool Sat, HashSet<int> Regions) Eval(Term t, Dictionary<int, List<Term>> groups, int depth = 0)
    {
        if (depth > 8) return (true, []);
        if (t.Group == 0) return (t.Sat, t.Region != 0 ? [t.Region] : []);
        if (!groups.TryGetValue(t.Group, out var list) || list.Count == 0) return (false, []);
        var parts = list.Select(x => Eval(x, groups, depth + 1)).ToList();
        if (t.Group > 0) return parts.All(p => p.Sat) ? (true, parts.SelectMany(p => p.Regions).ToHashSet()) : (false, []);
        var ok = parts.Where(p => p.Sat).ToList();
        return ok.Count > 0 ? (true, ok.SelectMany(p => p.Regions).ToHashSet()) : (false, []);
    }

    static void Walk(List<EMEVD.Instruction> ins, Dictionary<(int, int), (string Name, int[] Types)> defs, int a, int sp, int pc,
        Dictionary<int, List<Term>> groups, HashSet<int>? guard, HashSet<int> found, List<string> log, string at, int depth)
    {
        if (depth > 64) { log.Add($"K3: {at}: branch depth limit"); return; }
        for (; pc < ins.Count; pc++)
        {
            var x = ins[pc];
            var v = V(defs, x);
            int I(int k) => k < v.Length ? (int)v[k] : 0;
            Term? term = (x.Bank, x.ID) switch
            {
                (3, 0) when I(2) == 0 && I(3) == a => new Term(Sat: I(1) == 0),            // flag A ON never holds (A OFF)
                (3, 2) when I(2) == EmevdTranslator.Player && I(1) == 1 => new Term(Region: I(3)),
                (0, 0) when I(1) == 1 => new Term(Group: I(2)),
                _ when x.Bank is 0 or 1 or 3 or 4 or 5 or 11 => new Term(Unknown: true),
                _ => null,
            };
            if (term != null)
            {
                int g = I(0);
                if (g != 0) { if (!groups.TryGetValue(g, out var l)) groups[g] = l = []; l.Add(term); continue; }
                var (sat, regions) = Eval(term, groups);
                if (!sat) return;                                   // this wait never passes with A OFF
                guard = regions;
                groups = [];
                continue;
            }
            switch (x.Bank, x.ID)
            {
                case (2004, 8) when I(0) == EmevdTranslator.Player && I(1) == sp:
                    if (guard == null || guard.Count == 0) log.Add($"K3: {at}: SpEffect {sp} granted without a region wait (instruction {pc})");
                    else found.UnionWith(guard);
                    continue;
                case (1000, 3): pc += I(0); continue;                 // SKIP n
                case (1000, 4): return;                               // END / RESTART
                case (1003, 1) or (1003, 2) when I(2) == 0 && I(3) == a:
                {
                    bool takes = I(1) == 0;                           // the test holds with A OFF
                    if (!takes) continue;
                    if (x.ID == 2) return;
                    pc += I(0);
                    continue;
                }
                case (1000, 1) or (1000, 2) or (1000, 5) or (1000, 6) or (1000, 7) or (1000, 8) or (1003, _):
                {
                    // unresolved test: both ways
                    var copy = groups.ToDictionary(k => k.Key, k => k.Value.ToList());
                    if (x.ID is 1 or 3 or 5 or 7) Walk(ins, defs, a, sp, pc + 1 + I(0), copy, guard, found, log, at, depth + 1);
                    // END variants: the taken branch stops
                    continue;
                }
            }
        }
    }

    static double[] V(Dictionary<(int, int), (string Name, int[] Types)> defs, EMEVD.Instruction x) => EmevdTranslator.Decode(defs, x);

    /// <summary>
    /// Rule K12 (doors.md rev. 2): the ObjAct helper of a K1 prefab stands at the DS1 part × the model's ObjAct dummy
    /// (ObjActParam.objDummyId = FLVER dummy RefID, under its parent node), facing like the part; no such dummy → the part
    /// origin + 0.9 m along the part's local −Z (where both Asylum dummies 120 sit) and a note. o8544 dummy 120 (0, 0.01,
    /// −0.633) × yaw 180 at (3.19, 198.08, −33.42) → (3.19, 198.09, −32.79); o8541's (0, 0.01, −0.829) → (3.19, 198.09,
    /// −3.20) vs the V hand placement (−3.13): 7 cm.
    /// </summary>
    public static (Vector3 Pos, Vector3 Rot, string Note) HelperPose(string dsrRoot, MSB1.Part.Object part, int dummyId)
    {
        var place = Commands.ColSurvey.Place(part.Position, part.Rotation, part.Scale);
        string path = Path.Combine(dsrRoot, "obj", part.ModelName + ".objbnd.dcx");
        if (File.Exists(path) && BND3.Read(DCX.Decompress(File.ReadAllBytes(path))).Files.FirstOrDefault(f => f.ID == 200) is { } fl)
        {
            var flver = FLVER2.Read(fl.Bytes);
            if (flver.Dummies.FirstOrDefault(d => d.ReferenceID == dummyId) is { } d)
            {
                var world = Matrix4x4.Identity;
                for (int n = d.ParentBoneIndex; n >= 0 && n < flver.Nodes.Count; n = flver.Nodes[n].ParentIndex)
                    world *= flver.Nodes[n].ComputeLocalTransform();
                var p = Vector3.Transform(Vector3.Transform(d.Position, world), place);
                return (p, part.Rotation, $"K12: {part.Name} dummy {dummyId} local ({d.Position.X:0.###}, {d.Position.Y:0.###}, {d.Position.Z:0.###}) → ({p.X:0.##}, {p.Y:0.##}, {p.Z:0.##})");
            }
        }
        var q = Vector3.Transform(new Vector3(0, 0, -0.9f), place);
        return (q, part.Rotation, $"K12: {part.Name} has no dummy {dummyId}: part origin + 0.9 m along local −Z ({q.X:0.##}, {q.Y:0.##}, {q.Z:0.##})");
    }

    /// <summary>
    /// Rule K10′ (COVERAGE C15, binding): a K1 prefab door takes the first DS2 4-state door template whose state machine
    /// accepts a direct request of its rest state (pfbbin request records, <see cref="WorldEvents.PrefabRequests"/>), so
    /// DS1's 2005:7 "Reproduce" shows the opened door at load without replaying the swing. Candidates: o15_1060 「ギミック扉」
    /// (requests 70, 30, 80, 10; vanilla m10_29 requests 30 on its twin o29_1001) before o10_1001 (bars: 70, 80 only).
    /// </summary>
    public static readonly string[] DoorTemplates = ["o15_1060", "o10_1001"];

    public static string DoorTemplate(string ds2Data) =>
        DoorTemplates.FirstOrDefault(t => WorldEvents.PrefabRequests(ds2Data, t).Contains(30)) ?? DoorTemplates[^1];

    // ------------------------------------------------------------------ ladders (COVERAGE G4)

    /// <summary>
    /// Rule G4: DS1 makes a ladder climbable only when an event registers it (2009:0 Register Ladder). A ladder registered
    /// in the straight-line start of the map's load (event 0 / 50 before any wait, skip or end, including the straight-line
    /// start of events they initialise there, e.g. m10_01's 11010008) is climbable from load: DS2's plain ladder (instance
    /// 0, MapObjectDefaultInstanceParam objCtrlType 4) as today. Census over DSR: 96 registrations, 76 in event 0;
    /// the others: m10_01 11010008 ×12 (load subroutine) and 11010100 (the kicked ladder), m11_00 11100040, m13_00 11300001,
    /// m16_00 11600160, m18_01 11815382 + 11810900 (the Stray Demon pit ladder 1811141). DS1's DisableTop/Bottom flags of
    /// 2009:0 are never written by any DSR script (scratch\e2\ladderflags.txt check): no rule needed.
    /// </summary>
    public static HashSet<int> LoadRegisteredLadders(EMEVD emevd, Dictionary<(int, int), (string Name, int[] Types)> defs)
    {
        var byId = emevd.Events.ToDictionary(e => e.ID);
        var result = new HashSet<int>();
        // the instructions every load runs at once: from the start up to the first END-IF / wait, minus the ranges a
        // conditional SKIP may jump over (m13_01's event 0 opens with "SKIP 1 IF flag 7 OFF" and registers its nine
        // ladders right after it: unconditional); an unconditional SKIP n jumps
        void Prefix(EMEVD.Event ev, int depth)
        {
            int conditionalTo = -1;
            for (int i = 0; i < ev.Instructions.Count; i++)
            {
                var x = ev.Instructions[i];
                var v = V(defs, x);
                bool param = ev.Parameters.Any(p => p.InstructionIndex == i);
                bool sure = i > conditionalTo;
                switch (x.Bank, x.ID)
                {
                    case (1000, 3) when sure: i += (int)v[0]; continue;                                // SKIP n
                    case (1000, 3) or (1000, 1) or (1000, 5) or (1000, 7) or (1003, 1) or (1003, 3) or (1003, 5) or (1003, 7):
                        conditionalTo = Math.Max(conditionalTo, i + (int)v[0]); continue;            // SKIP n IF … (or a SKIP some path takes)
                    case (1000, _) or (1001, _) or (1003, _): return;                                // END (IF), waits: later = not at once
                }
                if (x.Bank is 0 or 1 or 3 or 4 or 5 or 11 && (v.Length == 0 || (int)v[0] == 0)) return; // a MAIN wait
                if (!sure) continue;
                if (x is { Bank: 2009, ID: 0 } && !param) result.Add((int)v[2]);
                if (x is { Bank: 2000, ID: 0 } && depth == 0 && byId.TryGetValue(BitConverter.ToUInt32(x.ArgData, 4), out var callee) && callee.ID != ev.ID)
                    Prefix(callee, depth + 1);
            }
        }
        foreach (long root in new long[] { 0, 50 }) if (byId.TryGetValue(root, out var r)) Prefix(r, 0);
        return result;
    }

    /// <summary>A ladder some event registers later than load: the registering events, the other instructions of the map
    /// that name it (bank:id), and why rule G4 does not apply (null = G4 applies).</summary>
    public sealed record LateLadder(int Entity, List<long> By, SortedSet<string> Uses, string? NotG4);

    /// <summary>
    /// Every ladder some event registers later than load, classified (fix pass e2, finding 1). Rule G4 (hide at load, show
    /// on 2009:0) holds only for a ladder DS1 addresses with 2009:0 alone: a plain ladder DS1 doesn't draw differently
    /// before registration. Any other instruction on the entity (after slot substitution, every event the map runs) means
    /// DS1 also shows / moves it, which state 30 「非表示」 (hidden) would break:
    ///  - 3:5 / 3:18 action button or 2003:18 / 2005:7 object animation on it = a KICK ladder (DS1: visible, lying flat, the
    ///    action plays its fall anim 0, then 2009:0): needs a prefab ladder route (visible, kick anim, climbable only after
    ///    2009:0); not done here, the ladder stays DS2's plain load-time ladder (climbable from load, no lock-in);
    ///  - anything else (m11_00 1101141: 2005:3 (De)activate in 11100532) = its own rule with those instructions (open).
    /// DSR census (objact-rules, 5 later ladders): G4 m13_00 1301143 (11300001), m18_01 1811141 (11815382 / 11810900);
    /// kick m10_01 1011149 (11010100), m16_00 1601142 (11600160); other m11_00 1101141 (2005:3).
    /// </summary>
    public static Dictionary<int, LateLadder> LaterLadders(EMEVD emevd, Dictionary<(int, int), (string Name, int[] Types)> defs)
    {
        var load = LoadRegisteredLadders(emevd, defs);
        var by = new Dictionary<int, List<long>>();
        var slots = Slots(emevd).ToList();
        foreach (var (ev, slot, args) in slots)
            for (int i = 0; i < ev.Instructions.Count; i++)
            {
                var x = EmevdTranslator.Substitute(ev, i, ev.Instructions[i], args);
                if (x is not { Bank: 2009, ID: 0 }) continue;
                int e = (int)V(defs, x)[2];
                if (load.Contains(e)) continue;
                if (!by.TryGetValue(e, out var l)) by[e] = l = [];
                if (!l.Contains(ev.ID)) l.Add(ev.ID);
            }
        var uses = by.Keys.ToDictionary(e => e, _ => new SortedSet<string>());
        foreach (var (ev, slot, args) in slots)
            for (int i = 0; i < ev.Instructions.Count; i++)
            {
                var x = EmevdTranslator.Substitute(ev, i, ev.Instructions[i], args);
                if ((x.Bank, x.ID) is (2009, 0) or (2000, 0)) continue;
                var v = V(defs, x);
                foreach (var e in by.Keys)
                    if (v.Any(d => d == e)) uses[e].Add($"{x.Bank}:{x.ID}");
            }
        static bool Kick(SortedSet<string> u) => u.Overlaps(["3:5", "3:18", "2003:18", "2005:7"]);
        return by.ToDictionary(k => k.Key, k => new LateLadder(k.Key, k.Value, uses[k.Key],
            uses[k.Key].Count == 0 ? null
            : Kick(uses[k.Key]) ? $"kick ladder ({string.Join(", ", uses[k.Key])} on it): needs a prefab ladder route (visible, kick anim, climbable only after 2009:0); stays DS2's plain ladder (climbable from load)"
            : $"addressed by {string.Join(", ", uses[k.Key])}: not rule G4 (needs a rule for those instructions); stays DS2's plain ladder"));
    }

    /// <summary>G4: the ladder entities some event of the map registers later than load and DS1 addresses with 2009:0 only
    /// (<see cref="LaterLadders"/>), with the events that register them.</summary>
    public static Dictionary<int, List<long>> EventRegisteredLadders(EMEVD emevd, Dictionary<(int, int), (string Name, int[] Types)> defs) =>
        LaterLadders(emevd, defs).Values.Where(l => l.NotG4 == null).ToDictionary(l => l.Entity, l => l.By);

    // ------------------------------------------------------------------ prefab rest clip (fix pass e2, finding 2)

    /// <summary>
    /// The DS1 clip a K1 prefab's closed rest state holds (the template's state 10 holds the end frame of its clip 10002,
    /// AsylumScript.PrefabStates), when no DS1 event plays a close anim on the model: the first other
    /// clip of the model's anibnd that is one pose (first frame = last frame) equal to the open clip's first frame, i.e.
    /// DS1's closed pose (o8544: a00_0000 "closed (static)"; a00_0002 is the open static pose and fails the test). Null
    /// (with the reason in <paramref name="log"/>) when there is none. Tolerance 1 mm / 0.01° per track.
    /// </summary>
    public static string? RestClip(string dsrRoot, string model, int openAnim, List<string> log)
    {
        string path = Path.Combine(dsrRoot, "obj", model + ".objbnd.dcx");
        if (!File.Exists(path)) { log.Add($"rest clip: {model}: no objbnd"); return null; }
        var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(path)));
        if (bnd.Files.FirstOrDefault(f => f.Name.EndsWith(".anibnd", StringComparison.OrdinalIgnoreCase)) is not { } af) { log.Add($"rest clip: {model}: no anibnd"); return null; }
        var clips = BND3.Read(af.Bytes).Files.Where(f => Path.GetFileName(f.Name).StartsWith("a00_", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f.Name).ToLowerInvariant(), f => f.Bytes);
        string open = $"a00_{openAnim:D4}";
        if (!clips.TryGetValue(open, out var ob)) { log.Add($"rest clip: {model}: no open clip {open}"); return null; }
        var o = new Formats.Havok.HkSplineAnim(new Formats.Havok.HkTagfile(ob));
        static bool Same((Vector3 T, Quaternion R, Vector3 S) a, (Vector3 T, Quaternion R, Vector3 S) b) =>
            Vector3.Distance(a.T, b.T) < 0.001f && MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a.R), Quaternion.Normalize(b.R))) > 0.99999f;
        foreach (var (name, bytes) in clips.Where(c => c.Key != open).OrderBy(c => c.Key))
        {
            var c = new Formats.Havok.HkSplineAnim(new Formats.Havok.HkTagfile(bytes));
            if (c.TrackCount != o.TrackCount || !c.TrackToBone.SequenceEqual(o.TrackToBone)) { log.Add($"rest clip: {model} {name}: other tracks"); continue; }
            bool still = Enumerable.Range(0, c.TrackCount).All(t => Same(c.Sample(t, 0), c.Sample(t, Math.Max(0, c.NumFrames - 1))));
            bool closed = Enumerable.Range(0, c.TrackCount).All(t => Same(c.Sample(t, 0), o.Sample(t, 0)));
            log.Add($"rest clip: {model} {name}: {(still ? "one pose" : "moves")}, {(closed ? "= " : "≠ ")}{open}'s first frame");
            if (still && closed) return name;
        }
        return null;
    }

    /// <summary>The DS2 state requests of rule G4 on a plain ladder (objCtrlType 4, no pfbbin; vanilla m10_17 group 111095
    /// 「梯子設置：非表示」 131636(10170500, 30) at load while the purchase flag is off, 「梯子設置：表示」 131636(…, 10)
    /// once it is set; Majula's laddersmith ladders 10041300/05/10 the same; V-data).</summary>
    public const int LadderShown = 10, LadderHidden = 30;
}
