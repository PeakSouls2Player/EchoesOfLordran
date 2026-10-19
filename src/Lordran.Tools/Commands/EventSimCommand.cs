using System.Globalization;
using System.Numerics;
using Lordran.Tools.Build.Events;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 9 (track nestfix): the offline event simulator (<see cref="EventSim"/>).
///   event-sim lint PACK MAP [GROUP…]                       rule G1 (transitions the gate never steps) + rule G2
///                                                          (registering states re-entered within one run)
///   event-sim lint-files ESD…                              G2 census of event ESD files (vanilla: data\ds2\ezstate)
///   event-sim lint-translate MAP [--all-chr]               G1 + G2 over the translator's output (no pack)
///   event-sim run PACK MAP --groups G,G [--spot N=X,Y,Z]… [--flag F=V]… [--item ITEM=N]… [--answers YES,NO,…] [--reverse] [--trace] [--no-records] -- STEP…
///   event-sim nests PACK [--trace] [--reverse] [--no-records]   the nest scenarios with checks (Firelink 11020020/21,
///                                                          Asylum 11810150) and the courtyard fog 11810090
///   event-sim world PACK [--trace]                         session 10: the Asylum's world events with checks (C1 floor
///                                                          collapse offline / as guest, boulder + wall, reload, G11)
///   event-sim npcs PACK [--trace]                          session 10 (e3): Oscar's death chain (N1/N3/SP1), event 50's
///                                                          NG+ branch (G5) and the covenant event (G5b) with checks
/// run: --guest = the player is a guest (f130602 = 1; rule C1).
/// Steps: 'spot NAME' | 'pos X Y Z' | 'press' | 'sleep S' | 'frames N' | 'shot NAME' | 'reload' | 'flag ID V'.
/// --no-records = the first fix pass's model without DS2's per-run records (comparison only).
/// </summary>
public static class EventSimCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: event-sim lint|lint-files|lint-translate|run|nests …"); return 1; }
        switch (args[1])
        {
            case "lint":
            {
                var esd = ESD.Read(Path.Combine(args[2], "gamedata", "ezstate", $"event_{args[3]}.esd"));
                var only = args.Length > 4 ? args.Skip(4).Select(long.Parse).ToHashSet() : null;
                var p = EventSim.Lint(esd.StateGroups, only);
                var r = EventSim.Revisits(esd.StateGroups, only);
                foreach (var l in p.Concat(r)) Console.WriteLine("  " + l);
                Console.WriteLine($"event-sim lint {args[3]}: G1 {p.Count} transition(s) the group-condition gate never steps; G2 {r.Count} registering state(s) re-entered within one run");
                return 0;
            }
            case "lint-files":
            {
                var files = args.Skip(2).SelectMany(a => a.Contains('*') ? Directory.GetFiles(Path.GetDirectoryName(a)!, Path.GetFileName(a)) : [a]).OrderBy(f => f).ToList();
                int totReg = 0, totConds = 0, totG2 = 0, totStates = 0;
                foreach (var f in files)
                {
                    var esd = ESD.Read(f);
                    var regStates = esd.StateGroups.Values.SelectMany(g => g.Values).Where(s => s.EntryCommands.Any(c => c.CommandBank == 1 && c.CommandID is >= 140000 and < 150000)).ToList();
                    int conds = regStates.Sum(s => s.EntryCommands.Count(c => c.CommandBank == 1 && c.CommandID is >= 140000 and < 150000));
                    var r = EventSim.Revisits(esd.StateGroups);
                    foreach (var l in r) Console.WriteLine($"  {Path.GetFileName(f)} {l}");
                    totReg += regStates.Count; totConds += conds; totG2 += r.Count; totStates += esd.StateGroups.Sum(g => g.Value.Count);
                    Console.WriteLine($"{Path.GetFileName(f)}: {esd.StateGroups.Count} groups, {regStates.Count} registering states ({conds} conditions), G2 {r.Count}");
                }
                Console.WriteLine($"event-sim lint-files: {files.Count} file(s), {totStates} states, {totReg} registering states ({totConds} conditions), G2 {totG2} re-entered within one run");
                return 0;
            }
            case "run":
            {
                var o = new EventSim.Options();
                var groups = new List<long>();
                int i = 4;
                for (; i < args.Length && args[i] != "--"; i++)
                    switch (args[i])
                    {
                        case "--groups": groups.AddRange(args[++i].Split(',').Select(long.Parse)); break;
                        case "--spot": { var kv = args[++i].Split('='); o.Spots[kv[0]] = V(kv[1]); break; }
                        case "--flag": { var kv = args[++i].Split('='); o.Flags[int.Parse(kv[0])] = int.Parse(kv[1]); break; }
                        case "--reverse": o.Reverse = true; break;
                        case "--trace": o.Trace = true; break;
                        case "--no-records": o.Records = false; break;
                        case "--guest": o.Guest = true; break; // session 10 (rule C1)
                        case "--fps": o.Fps = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                        // session 10 (asy-items): YES/NO answers in order (YES/NO/CANCEL or 1/2/3), e.g. the origin prompt
                        case "--item": { var kv = args[++i].Split('='); o.Inventory[int.Parse(kv[0])] = int.Parse(kv[1]); break; } // session 10 (asy-items i2): inventory for f131401
                        case "--answers": foreach (var a in args[++i].Split(',')) o.Answers.Enqueue(a.ToUpperInvariant() switch { "YES" or "1" => 1, "NO" or "2" => 2, _ => 3 }); break;
                        default: throw new ArgumentException(args[i]);
                    }
                var sim = new EventSim(args[2], args[3], groups, o);
                Console.Write(sim.Describe());
                sim.Run(args.Skip(i + 1));
                Print(sim);
                return 0;
            }
            case "nests": return Nests(args[2], args.Contains("--trace"), args.Contains("--reverse"), !args.Contains("--no-records"));
            case "world": return World(args[2], args.Contains("--trace"));
            case "doors": return Doors(args[2], args.Contains("--trace"));
            case "npcs": return Npcs(args[2], args.Contains("--trace"));
            case "return": return Return(args[2], args.Contains("--trace")); // session 11 (ret11): the return trip's characters
            case "burg-bosses": return BurgBosses(args[2], args.Contains("--trace")); // session 11 (burg11, M4): the Burg's boss fights
            case "depths-bosses": return DepthsBosses(args[2], args.Contains("--trace")); // session 11 (dchr11): the Gaping Dragon fight
            case "idle": { int si = Array.IndexOf(args, "--save"); var skip = new HashSet<int>(si > 0 ? [si, si + 1, si + 2] : []); return Idle(args[2], args.Skip(3).Where((a, k) => !a.StartsWith("--") && !skip.Contains(k + 3)).ToList(), args.Contains("--trace"), si > 0 ? args[si + 1] : null, si > 0 ? int.Parse(args[si + 2]) : 0); } // session 11 (fix11): spawn + idle 60 s, no warp / no flag change after load
            case "lint-translate": // event-sim lint-translate MAP [--all-chr]: rules G1 + G2 over the translator's output (no pack)
            {
                string map = args[2];
                var msb1 = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", map + ".msb"));
                _ = Build.Maps.AsylumScript.Regions(msb1).ToList();
                var ds1 = new Build.Ds1Source(Program.DefaultDsrRoot);
                var emevd = EMEVD.Read(Path.Combine(Program.DefaultDsrRoot, "event", map + ".emevd.dcx"));
                Dictionary<int, int>? gens = args.Contains("--all-chr")
                    ? msb1.Parts.Enemies.Where(e => e.EntityID > 0).Select(e => e.EntityID).Concat([6023, 6024, 6450]).Distinct().ToDictionary(e => e, EntityRules.Generator)
                    : null;
                EmevdTranslator.IContext ctx = map == FirelinkContext.Map ? new FirelinkContext(null, ds1, gens?.Values) : new AsylumContext(null, ds1, gens);
                var t = new EmevdTranslator(emevd, ctx, EmevdDump.Defs());
                int gid = 38000;
                t.TranslateMap((id, slot) => gid++);
                // chrint stage 1 fix pass: the build's common.emevd instances too (AsylumScript translates event 970 = the
                // boss rewards after the map's events; FirelinkScript translates no common events), as emevd-translate does
                if (ctx is AsylumContext)
                    t.TranslateCommon(EMEVD.Read(Path.Combine(Program.DefaultDsrRoot, "event", "common.emevd.dcx")), (id, slot) => gid++, [970]);
                var all = new Dictionary<long, Dictionary<long, ESD.State>>(); // t.Results holds the map and the common instances
                foreach (var r in t.Results.Where(r => r.States != null)) all[r.EventId * 100 + r.Slot] = r.States!;
                var p = EventSim.Lint(all);
                var rv = EventSim.Revisits(all);
                foreach (var l in p.Concat(rv)) Console.WriteLine("  " + l);
                int reruns = all.Values.Count(g => g.Values.Any(s => s.EntryCommands.Any(c => c.CommandID == 130002)));
                Console.WriteLine($"event-sim lint-translate {map}{(gens != null ? " --all-chr" : "")}: {all.Count} translated event instances (group = event·100 + slot), G1 {p.Count} transition(s) the group-condition gate never steps, G2 {rv.Count} registering state(s) re-entered within one run; {reruns} event(s) re-execute (130002)");
                return 0;
            }
            default: Console.Error.WriteLine($"unknown event-sim mode {args[1]}"); return 1;
        }
    }

    static Vector3 V(string s) { var p = s.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray(); return new(p[0], p[1], p[2]); }

    /// <summary>
    /// Session 10 (sandbox asy-events): the Asylum's world events on a built pack, with checks — rule C1 (the return-trip
    /// floor collapse 11810220 waits on DS1 "Host" = f130602() == 0: it must fire with NO session, and not for a guest),
    /// WE1/WE4b/C11 (boulder 11810211 + Oscar's wall 11810200: the roll breaks the wall at DS1's 2.25 s, the rest copy is
    /// shown at its baked end pose with no state request (WE5), the rubble shows; reload = no roll, wall gone), WE6 restore of the floor at the next
    /// load, and G11 (the first return-trip load re-arms the boulder once: 11810350's fold clears 11810211 while its own
    /// flag is off, before the boulder group reads it, in either group order).
    /// </summary>
    static int World(string pack, bool trace)
    {
        var ck = new Checks();
        var esd = ESD.Read(Path.Combine(pack, "gamedata", "ezstate", "event_m18_01_00_00.esd"));
        static int A(byte[] b) => (int)EventSim.Evaluate(b, (_, _) => 0);
        long Group(Func<ESD.CommandCall, bool> pred, string what)
        {
            var g = esd.StateGroups.FirstOrDefault(kv => kv.Value.Values.Any(s => s.EntryCommands.Any(pred)));
            if (g.Value == null) throw new InvalidDataException($"event_m18_01_00_00.esd: no group {what}");
            return g.Key;
        }
        int F(int ds1) => Build.Events.FlagRules.Map("m18_01", ds1) ?? throw new InvalidDataException($"no DS2 flag for {ds1}");
        const int Boulder = 18011215, Copy = 18011216, Rubble = 18011217, Wall = 18011210, WallCopy = 18011211, Behind = 18011212,
            Floor = 18011200, FloorCopy = 18011201, Under = 18011202;
        int fBoulder = F(11810211), fWall = F(11810201), fLeft = F(11810000), fFloor = F(11810220), fSwitch = F(11810350);
        long gBoulder = Group(c => c.CommandID == 131636 && A(c.Arguments[0]) == Boulder && A(c.Arguments[1]) == 70, "requesting the boulder's roll");
        long gWall = Group(c => c.CommandID == 140202 && A(c.Arguments[1]) == Wall, "waiting for the wall's break");
        long gFloor = Group(c => c.CommandID == 140132 && A(c.Arguments[1]) == 3701400, "waiting in the floor region 3701400");
        long gSwitch = Group(c => c.CommandID == 130101 && A(c.Arguments[0]) == fBoulder && A(c.Arguments[1]) == 0, "clearing the boulder flag (11810350)");
        Console.WriteLine($"groups: boulder {gBoulder}, wall {gWall}, floor {gFloor}, world switch {gSwitch}; flags 11810211={fBoulder} 11810201={fWall} 11810000={fLeft} 11810220={fFloor} 11810350={fSwitch}");
        EventSim Sim(long[] groups, (int Flag, int V)[] flags, bool guest = false, bool reverse = false)
        {
            var o = new EventSim.Options { Trace = trace, Guest = guest, Reverse = reverse };
            o.Spots["hall"] = new(3.13f, 197.7f, -30.0f);
            o.Spots["floor"] = new(3.13f, 197.7f, -22.11f);
            o.Spots["stairs"] = new(-8.0f, 201.0f, 14.0f);
            o.Spots["trigger"] = new(-8.0f, 203.4f, 7.17f);
            foreach (var (f, v) in flags) o.Flags[f] = v;
            return new EventSim(pack, "m18_01_00_00", groups, o);
        }
        void Run(string name, EventSim sim, string[] steps, Action<EventSim> check)
        {
            Console.WriteLine($"=== {name}");
            if (name.StartsWith("W1 ") || name.StartsWith("W2 ")) Console.Write(sim.Describe());
            sim.Run(steps);
            Print(sim);
            ck.Begin(name[..name.IndexOf(' ')], sim);
            check(sim);
        }
        bool Hidden(EventSim s, int inst) => s.Objects.TryGetValue(inst, out var v) && v.On == 0;
        bool Shown(EventSim s, int inst) => !s.Objects.TryGetValue(inst, out var v) || v.On != 0;
        string Obj(EventSim s, int inst) => $"{(Hidden(s, inst) ? "hidden" : "shown")}{(s.Broken.Contains(inst) ? ", broken" : "")}{(s.Requests.TryGetValue(inst, out var r) ? $", state {r.State}" : "")}";

        // ---- C1: the return-trip floor (11810220: IF 11810000 AND Host AND player in 1812400)
        string[] floorSteps = ["spot hall", "sleep 1", "shot before", "spot floor", "sleep 1", "shot after"];
        Run("W1 floor collapse, return trip, OFFLINE (no session): DS1 Host = not a guest", Sim([gFloor], [(fLeft, 1)]), floorSteps, s =>
        {
            ck.Expect($"floor {Floor} hidden, copy {FloorCopy} shown + broken, under-layer {Under} hidden (got {Obj(s, Floor)} / {Obj(s, FloorCopy)} / {Obj(s, Under)})",
                Hidden(s, Floor) && Shown(s, FloorCopy) && s.Broken.Contains(FloorCopy) && Hidden(s, Under));
            ck.Expect($"flag 11810220 ({fFloor}) set (got {s.Flag(fFloor)})", s.Flag(fFloor) == 1);
            ck.Expect($"no 130307: the 14.9 m drop is below DS2's lethal 19.5 m (rule WE8)", !s.Log.Any(l => l.Contains("130307")));
        });
        Run("W1g same as a GUEST (f130602 = 1): DS1 Client never runs the host-only collapse", Sim([gFloor], [(fLeft, 1)], guest: true), floorSteps, s =>
            ck.Expect($"no collapse (got copy {Obj(s, FloorCopy)}, flag {s.Flag(fFloor)})", !s.Broken.Contains(FloorCopy) && s.Flag(fFloor) == 0));
        Run("W1f first visit (11810000 off): no collapse", Sim([gFloor], []), floorSteps, s =>
            ck.Expect($"no collapse (got copy {Obj(s, FloorCopy)}, flag {s.Flag(fFloor)})", !s.Broken.Contains(FloorCopy) && s.Flag(fFloor) == 0 && Hidden(s, FloorCopy)));
        Run("W1r collapse, then reload away from the hole: DS1 restores the floor (flag off, 131652 ×3)", Sim([gFloor], [(fLeft, 1)]),
            [.. floorSteps, "spot hall", "reload", "sleep 1", "shot reloaded"], s =>
            {
                ck.Expect($"after the reload: floor shown, copy hidden, nothing broken, flag 11810220 off (got {Obj(s, Floor)} / {Obj(s, FloorCopy)}, flag {s.Flag(fFloor)})",
                    Shown(s, Floor) && Hidden(s, FloorCopy) && !s.Broken.Contains(FloorCopy) && s.Flag(fFloor) == 0);
            });

        // ---- WE1/WE4b/C11: boulder + wall, first visit and reload
        string[] rollSteps = ["spot stairs", "sleep 1", "shot top", "spot trigger", "sleep 2", "shot rolling", "sleep 1", "shot wall-hit", "sleep 5", "shot rested", "spot stairs", "sleep 1"];
        Run("W2 boulder rolls on the stair landing, breaks Oscar's wall at DS1's 2.25 s, rest copy + rubble shown", Sim([gBoulder, gWall], []), rollSteps, s =>
        {
            var roll = s.RequestLog.FirstOrDefault(r => r.Instance == Boulder && r.State == 70);
            var brk = s.BreakLog.FirstOrDefault(b => b.Instance == Wall);
            double dt = roll.Frame > 0 && brk.Frame > 0 ? (brk.Frame - roll.Frame) / s.Fps : double.NaN;
            ck.Expect($"roll requested only after entering the trigger (got {(roll.Frame > 0 ? $"frame {roll.Frame}" : "none")})", roll.Frame > 60);
            ck.Expect($"wall {Wall} broken {dt:0.00} s after the roll (DS1 2.25 s ± 0.1)", Math.Abs(dt - 2.25) <= 0.1);
            ck.Expect($"boulder hidden, rest copy shown with no state request (baked end pose, WE5), rubble shown (got {Obj(s, Boulder)} / {Obj(s, Copy)} / {Obj(s, Rubble)})",
                Hidden(s, Boulder) && Shown(s, Copy) && !s.Requests.ContainsKey(Copy) && Shown(s, Rubble));
            ck.Expect($"wall {Wall} hidden, copy {WallCopy} shown + broken, behind-the-wall {Behind} hidden (got {Obj(s, Wall)} / {Obj(s, WallCopy)} / {Obj(s, Behind)})",
                Hidden(s, Wall) && Shown(s, WallCopy) && s.Broken.Contains(WallCopy) && Hidden(s, Behind));
            ck.Expect($"flags 11810211 {s.Flag(fBoulder)} and 11810201 {s.Flag(fWall)} set", s.Flag(fBoulder) == 1 && s.Flag(fWall) == 1);
        });
        Run("W3 reload after the roll: no roll, rest copy shown (baked, no request), wall parts hidden", Sim([gBoulder, gWall], [(fBoulder, 1), (fWall, 1)]),
            ["spot trigger", "sleep 6", "shot reloaded"], s =>
            {
                ck.Expect($"no roll request (got {string.Join(", ", s.RequestLog.Where(r => r.Instance == Boulder).Select(r => r.State))})", !s.RequestLog.Any(r => r.Instance == Boulder));
                ck.Expect($"boulder hidden, rest copy shown, no request (got {Obj(s, Boulder)} / {Obj(s, Copy)})", Hidden(s, Boulder) && !s.Requests.ContainsKey(Copy) && Shown(s, Copy));
                ck.Expect($"wall, copy and behind-the-wall hidden (got {Obj(s, Wall)} / {Obj(s, WallCopy)} / {Obj(s, Behind)})", Hidden(s, Wall) && Hidden(s, WallCopy) && Hidden(s, Behind));
                ck.Expect($"WE4b-L idempotent: nothing broken at this load (the wall's own load branch hid it first; got {string.Join(", ", s.BreakLog.Select(b => b.Instance))})", s.BreakLog.Count == 0);
            });
        // ---- WE4b-L (fix pass): a save whose boulder flag is on but whose wall never broke (main's stand-in 37340 set
        // 11810211 when no wall existed): the boulder's load-time end branch re-applies the hit, the wall's event completes
        foreach (bool reverse in new[] { false, true })
            Run($"W7{(reverse ? "r" : "")} old save: 11810211 on, 11810201 off (main's 37340){(reverse ? ", groups in reverse order" : "")}: the wall breaks at load, 11810200 completes",
                Sim([gBoulder, gWall], [(fBoulder, 1), (fWall, 0)], reverse: reverse),
                ["spot stairs", "sleep 3", "shot loaded", "reload", "sleep 3", "shot reloaded"], s =>
                {
                    ck.Expect($"wall {Wall} broken once (got {string.Join(", ", s.BreakLog.Select(b => $"{b.Instance}@{b.Frame}"))})", s.BreakLog.Count(b => b.Instance == Wall) == 1);
                    ck.Expect($"11810200's own flag 11810201 ({fWall}) set (got {s.Flag(fWall)})", s.Flag(fWall) == 1);
                    ck.Expect($"after the reload: wall, copy and behind-the-wall hidden, no roll (got {Obj(s, Wall)} / {Obj(s, WallCopy)} / {Obj(s, Behind)})",
                        Hidden(s, Wall) && Hidden(s, WallCopy) && Hidden(s, Behind) && !s.RequestLog.Any(r => r.Instance == Boulder));
                });

        // ---- G11: the return trip
        foreach (bool reverse in new[] { false, true })
            Run($"W4{(reverse ? "r" : "")} G11 first return-trip load, departure continuation not run (11810211 still on, 11810350 off){(reverse ? ", groups in reverse order" : "")}: the boulder rolls once more",
                Sim([gBoulder, gWall, gSwitch], [(fBoulder, 1), (fWall, 1), (fLeft, 1), (fSwitch, 0)], reverse: reverse),
                ["spot stairs", "sleep 1", "shot loaded", "spot trigger", "sleep 6", "shot rolled"], s =>
                {
                    ck.Expect($"11810350 own flag set at load end (got {s.Flag(fSwitch)})", s.Flag(fSwitch) == 1);
                    ck.Expect($"boulder re-armed: rest copy / rubble hidden before the roll, roll requested in the trigger and the boulder not hidden before its roll (got requests {string.Join(", ", s.RequestLog.Select(r => $"{r.Instance}:{r.State}"))})",
                        s.RequestLog.Any(r => r.Instance == Boulder && r.State == 70) && s.Log.TakeWhile(l => !l.Contains($"object {Boulder} state 70")).Any(l => l.Contains($"object {Copy} hidden")) && s.Log.TakeWhile(l => !l.Contains($"object {Boulder} state 70")).Any(l => l.Contains($"object {Rubble} hidden")) && !s.Log.TakeWhile(l => !l.Contains($"object {Boulder} state 70")).Any(l => l.Contains($"object {Boulder} hidden")));
                    ck.Expect($"the wall stays gone: no break request on the hidden wall (DS1 can't hit a disabled part; got {string.Join(", ", s.BreakLog.Select(b => b.Instance))})", !s.BreakLog.Any(b => b.Instance == Wall));
                    ck.Expect($"flag 11810211 set again after the roll (got {s.Flag(fBoulder)})", s.Flag(fBoulder) == 1);
                });
        Run("W5 G11 later return-trip load (11810350 own flag on): no second re-arm", Sim([gBoulder, gSwitch], [(fBoulder, 1), (fLeft, 1), (fSwitch, 1)]),
            ["spot trigger", "sleep 6", "shot loaded"], s =>
                ck.Expect($"no roll, 11810211 still on (got requests {string.Join(", ", s.RequestLog.Select(r => $"{r.Instance}:{r.State}"))}, flag {s.Flag(fBoulder)})",
                    !s.RequestLog.Any(r => r.Instance == Boulder) && s.Flag(fBoulder) == 1));
        Run("W6 G11 live: the boulder rolled, then the crow's hoisted 11810000 while the map is still loaded → 11810211 cleared at once",
            Sim([gSwitch], [(fBoulder, 1)]), ["spot stairs", "sleep 1", "flag " + fLeft + " 1", "sleep 0.5", "shot left"], s =>
                ck.Expect($"11810211 off, 11810350 own flag on (got {s.Flag(fBoulder)}, {s.Flag(fSwitch)})", s.Flag(fBoulder) == 0 && s.Flag(fSwitch) == 1));

        Console.WriteLine($"event-sim world: {ck.Pass + ck.Fail} checks, {ck.Fail} failed");
        foreach (var f in ck.Failed) Console.WriteLine("  failed: " + f);
        return ck.Fail == 0 ? 0 : 1;
    }

    /// <summary>
    /// Session 10 (sandbox asy-events, e2): the Asylum's doors and ladders on a built pack. Static checks: K3 (the inner
    /// door's lock is 131602(inside region, 0, 0, 1101, flag, 0) on a group owned by the door; no group grants / polls the
    /// "inside key"), K2 locks of the other key doors, K12 helper positions in the MSB, G4 (the pit ladder's own instance
    /// row, objCtrlType 4). Simulated (K1/K6/K7/K10′, the far door o8544 1811110): without the Big Pilgrim's Key the helper's
    /// "Open" fails with "It's locked" and re-arms; with it the door plays its open state 70, DS1's ObjAct flag sets and
    /// DS1's own 11810110 shows "Used %s" with the key; at the next load the door is requested straight into its rest state
    /// 30 (open, no swing) and the prompt stays off. The hall door's ObjAct group (no requirement) as a regression.
    /// </summary>
    static int Doors(string pack, bool trace)
    {
        var ck = new Checks();
        string map = "m18_01_00_00";
        var esd = ESD.Read(Path.Combine(pack, "gamedata", "ezstate", $"event_{map}.esd"));
        static int A(byte[] b) => (int)EventSim.Evaluate(b, (_, _) => 0);
        int F(int ds1) => FlagRules.Map("m18_01", ds1) ?? throw new InvalidDataException($"no DS2 flag for {ds1}");
        IEnumerable<(long G, ESD.CommandCall C)> All() => esd.StateGroups.SelectMany(g => g.Value.Values.SelectMany(s => s.EntryCommands).Select(c => (g.Key, c)));
        long Group(Func<ESD.CommandCall, bool> pred, string what) =>
            All().FirstOrDefault(x => pred(x.C)) is var f && f.C != null ? f.G : throw new InvalidDataException($"event_{map}.esd: no group {what}");
        const int FarDoor = 18011110, FarHelper = 18015110, HallDoor = 18011111, HallHelper = 18011119, Bpk = 54011000, Ladder = 18011141;
        int fFar = F(11810110), fHall = F(11810111);

        // ---- static: locks (K2/K3), the grant event (K3), helpers (K12), the pit ladder (G4)
        var events = PARAM.Read(Path.Combine(pack, "gamedata", "param", $"eventparam_{map}.param"));
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        events.ApplyParamdefCarefully(defs);
        int OwnerOf(long g) => events.Rows.Where(r => Convert.ToInt64(r["stateMapId"].Value) == g).Select(r => Convert.ToInt32(r["ownerType"].Value) == 1 ? Convert.ToInt32(r["ownerId"].Value) : 0).FirstOrDefault();
        var locks = All().Where(x => x.C.CommandID == 131602).Select(x => (x.G, Args: x.C.Arguments.Select(A).ToArray())).ToList();
        Console.WriteLine("=== static");
        foreach (var l in locks) Console.WriteLine($"  lock group {l.G} owner {OwnerOf(l.G)}: 131602({string.Join(", ", l.Args)})");
        var inner = locks.FirstOrDefault(l => OwnerOf(l.G) == 18011105);
        ck.Begin("S1", null!);
        ck.Expect($"K3 inner door 18011105: 131602(3701020 inside 1812020, 0 item, 0 no 'Used' text, 1101 'Does not open from this side', its state flag {F(61810105)}, 0) (got {(inner.Args == null ? "none" : string.Join(", ", inner.Args))})",
            inner.Args is [3701020, 0, 0, 1101, var fl, 0] && fl == F(61810105));
        ck.Expect("K3: no group registers the outside box 3701021 or grants the inside key (11810120 not emitted)", !All().Any(x => x.C.CommandID == 140132 && A(x.C.Arguments[1]) == 3701021));
        foreach (var (owner, item, flag) in new[] { (18011103, 54010000, 61810103), (18011104, 54016000, 61810104), (18011106, 54012000, 61810106) })
        {
            var k = locks.FirstOrDefault(l => OwnerOf(l.G) == owner);
            ck.Expect($"K2 key door {owner}: 131602(0, {item}, 1005, 1105, {F(flag)}, 0) (got {(k.Args == null ? "none" : string.Join(", ", k.Args))})", k.Args is [0, var it, 1005, 1105, var fl2, 0] && it == item && fl2 == F(flag));
        }
        var msb = MSB2.Read(Path.Combine(pack, "gamedata", "map", map, map + ".msb"));
        Vector3? At(int inst) => msb.Parts.Objects.FirstOrDefault(p => p.MapObjectInstanceParamID == inst)?.Position;
        foreach (var (inst, want, what) in new[] { (FarHelper, new Vector3(3.19f, 198.09f, -32.79f), "far-door helper"), (HallHelper, new Vector3(3.19f, 198.09f, -3.20f), "hall-door helper"), (18011118, new Vector3(3.19f, 198.09f, -3.20f), "hall-door action-button helper") })
            ck.Expect($"K12 {what} {inst} at ({want.X}, {want.Y}, {want.Z}) ±0.03 (got {At(inst)?.ToString() ?? "none"})", At(inst) is Vector3 p && Vector3.Distance(p, want) <= 0.03f);
        var far = msb.Parts.Objects.FirstOrDefault(p => p.MapObjectInstanceParamID == FarDoor);
        ck.Expect($"K1 far door placed: model o18_8544 at DS1's o8544_0000 (3.19, 198.08, -33.42) rot 180 (got {far?.ModelName} {far?.Position} {far?.Rotation})",
            far is { ModelName: "o18_8544" } && Vector3.Distance(far.Position, new Vector3(3.19f, 198.08f, -33.42f)) < 0.02f);
        ck.Expect("K10′: the far door's state machine is o15_1060's (prefabeditor/o18_8544.pfbbin present)", File.Exists(Path.Combine(pack, "gamedata", "prefabeditor", "o18_8544.pfbbin")));
        var instParam = PARAM.Read(Path.Combine(pack, "gamedata", "param", $"mapobjectinstanceparam_{map}.param"));
        instParam.ApplyParamdefCarefully(defs);
        var lrow = instParam.Rows.FirstOrDefault(r => r.ID == Ladder);
        var lpart = msb.Parts.Objects.FirstOrDefault(p => p.MapObjectInstanceParamID == Ladder);
        ck.Expect($"G4 pit ladder 1811141: part o18_8501 with instance {Ladder}, row objCtrlType 4 / objType 195 / resetType 0 (got {lpart?.ModelName}, row {(lrow == null ? "none" : $"{lrow["objCtrlType"].Value}/{lrow["objType"].Value}/{lrow["resetType"].Value}")})",
            lpart?.ModelName == "o18_8501" && lrow != null && Convert.ToInt32(lrow["objCtrlType"].Value) == 4 && Convert.ToInt32(lrow["objType"].Value) == 195 && Convert.ToInt32(lrow["resetType"].Value) == 0);
        // fix pass e2 (finding 6): the G4 row is the model's own MapObjectDefaultInstanceParam row (the verified instance-0
        // route) apart from its id
        var reg = Formats.ParamStore.LoadDs2(Path.Combine(pack, "title"), ExtractDs2.DefaultOut);
        var drow = reg.Params["MapObjectDefaultInstanceParam"][188501];
        var diff = lrow == null || drow == null ? ["row missing"] : lrow.Cells.Where(c => !Equals(c.Value is byte[] b ? Convert.ToHexString(b) : c.Value,
            drow[c.Def.InternalName].Value is byte[] b2 ? Convert.ToHexString(b2) : drow[c.Def.InternalName].Value)).Select(c => $"{c.Def.InternalName} {c.Value} vs {drow![c.Def.InternalName].Value}").ToList();
        ck.Expect($"G4 row {Ladder} = MapObjectDefaultInstanceParam 188501 (o18_8501's default row) field by field (differences: {(diff.Count == 0 ? "none" : string.Join(", ", diff))})", diff.Count == 0);
        // fix pass e2 (finding 2): the far door's closed state 10 holds clip 10002's end frame: the model carries DS1's
        // closed rest pose as a00_01_0002 (ObjActRules.RestClip: o8544 a00_0000)
        var farBnd = BND4.Read(Path.Combine(pack, "gamedata", "model", "obj", "o18_8544.bnd"));
        var clips = farBnd.Files.Select(f => Path.GetFileName(f.Name)).Where(n => n.StartsWith("a00_", StringComparison.OrdinalIgnoreCase)).Order().ToList();
        ck.Expect($"K10′ far door o18_8544 carries a00_01_0001 (open) and a00_01_0002 (closed rest pose) (got {string.Join(", ", clips)})",
            clips.Contains("a00_01_0001.hkx", StringComparer.OrdinalIgnoreCase) && clips.Contains("a00_01_0002.hkx", StringComparer.OrdinalIgnoreCase));
        var pool = msb.Parts.Objects.FirstOrDefault(p => p.ModelName == "o18_8500");
        ck.Expect($"G4: the pool ladder 1811140 (registered at load by event 0) keeps instance 0 (got {pool?.MapObjectInstanceParamID})", pool?.MapObjectInstanceParamID == 0);
        var shows = All().Where(x => x.C.CommandID == 131636 && A(x.C.Arguments[0]) == Ladder).Select(x => $"{x.G}:{A(x.C.Arguments[1])}").ToList();
        Console.WriteLine($"  G4 ladder state requests in this pack: {(shows.Count == 0 ? "none (registering events 11815382 / 11810900 need the Stray Demon: pending, ladder climbable as before)" : string.Join(", ", shows))}");
        ck.Expect("G4: hide and show come together (no hide without a show, no show without the load-time hide)",
            shows.Count == 0 || shows.Any(s => s.EndsWith(":30")) && shows.Any(s => s.EndsWith(":10")));

        // ---- simulated: the far door (K1/K6/K7/K10′) and the hall door (regression)
        long gFarAct = Group(c => c.CommandID == 140205 && A(c.Arguments[1]) == FarHelper, "examining the far-door helper");
        long gFarEv = Group(c => c.CommandID == 131636 && A(c.Arguments[0]) == FarDoor && A(c.Arguments[1]) == 30, "reproducing the far door open (11810110)");
        long gHallAct = Group(c => c.CommandID == 140205 && A(c.Arguments[1]) == HallHelper, "examining the hall-door helper");
        Console.WriteLine($"groups: far-door ObjAct {gFarAct}, 11810110 {gFarEv}, hall-door ObjAct {gHallAct}; flags 11810110={fFar} 11810111={fHall}");
        EventSim Sim(long[] groups, (int Flag, int V)[] flags, bool bpk = false)
        {
            var o = new EventSim.Options { Trace = trace };
            o.Spots["far"] = new(3.19f, 198.1f, -32.3f);
            o.Spots["hall"] = new(3.13f, 197.7f, -22.0f);
            o.Spots["courtyard"] = new(3.19f, 196.0f, -2.6f);
            foreach (var (f, v) in flags) o.Flags[f] = v;
            if (bpk) o.Items.Add(Bpk);
            return new EventSim(pack, map, groups, o);
        }
        void Run(string name, EventSim sim, string[] steps, Action<EventSim> check)
        {
            Console.WriteLine($"=== {name}");
            Console.Write(sim.Describe());
            sim.Run(steps);
            Print(sim);
            ck.Begin(name[..name.IndexOf(' ')], sim);
            check(sim);
        }
        bool Dialog(EventSim s, params int[] args) => s.Dialogs.Any(d => d.Args.SequenceEqual(args));
        string Req(EventSim s, int i) => string.Join(", ", s.RequestLog.Where(r => r.Instance == i).Select(r => r.State));
        string[] use = ["spot hall", "sleep 1", "shot away", "spot far", "sleep 1", "shot prompt", "press", "sleep 0.5", "shot used", "sleep 1", "shot later"];
        // fix pass e2 (finding 5, rule K6 key source): the far door is locked only when the build sources the Big Pilgrim's
        // Key (or the TEST option --test-key-locks kept the lock); otherwise its prompt opens it and no "Used <key>" shows
        bool locked = esd.StateGroups[gFarAct].Values.SelectMany(st => st.EntryCommands).Any(c => c.CommandID == 140801 && A(c.Arguments[1]) == Bpk);
        Console.WriteLine($"far door: {(locked ? "LOCKED (Big Pilgrim's Key sourced, or --test-key-locks)" : "unlocked by rule K6 key source (no source of the Big Pilgrim's Key in this build)")}");
        if (!locked)
        {
            ck.Begin("S2", null!);
            ck.Expect("K6 key source: no \"Used Big Pilgrim's Key\" (130443 …, 2, BPK) left anywhere in the map's ESD",
                !All().Any(x => x.C.CommandID == 130443 && x.C.Arguments.Count > 5 && A(x.C.Arguments[4]) == 2 && A(x.C.Arguments[5]) == Bpk));
            Run("D1u far door, no source for the key in this build: the prompt opens it (no lock-in), no dialog", Sim([gFarAct, gFarEv], []), use, s =>
            {
                ck.Prompt("away", "none");
                ck.Prompt("prompt", "\"Open\"");
                ck.Expect($"door state 70 requested once (got {Req(s, FarDoor)}), flag 11810110 set (got {s.Flag(fFar)})", Req(s, FarDoor) == "70" && s.Flag(fFar) == 1);
                ck.Expect($"no dialog (got {string.Join(" | ", s.Dialogs.Select(d => string.Join(",", d.Args)))})", s.Dialogs.Count == 0);
                ck.Prompt("later", "none");
            });
        }
        else
        {
        Run("D1 far door without the Big Pilgrim's Key: \"It's locked\", door shut, prompt re-arms", Sim([gFarAct, gFarEv], []), use, s =>
        {
            ck.Prompt("away", "none");
            ck.Prompt("prompt", "\"Open\"");
            ck.Expect($"dialog 130443(1105 \"It's locked\", 0, 0, 190, 0, 0, 0) (got {string.Join(" | ", s.Dialogs.Select(d => string.Join(",", d.Args)))})", Dialog(s, 1105, 0, 0, 190, 0, 0, 0));
            ck.Expect($"no state request on the door (got {Req(s, FarDoor)}), flag 11810110 off (got {s.Flag(fFar)})", !s.RequestLog.Any(r => r.Instance == FarDoor) && s.Flag(fFar) == 0);
            ck.Prompt("later", "\"Open\"");
        });
        Run("D2 far door WITH the key: opens (70), DS1's ObjAct flag, \"Used <key>\" by DS1's 11810110 (rule K7)", Sim([gFarAct, gFarEv], [], bpk: true), use, s =>
        {
            ck.Expect($"door state 70 requested once (got {Req(s, FarDoor)})", Req(s, FarDoor) == "70");
            ck.Expect($"flag 11810110 ({fFar}) set (got {s.Flag(fFar)})", s.Flag(fFar) == 1);
            ck.Expect($"dialog 130443(1005, 0, 0, 190, 2, {Bpk}, 0) = \"Used Big Pilgrim's Key\" (got {string.Join(" | ", s.Dialogs.Select(d => string.Join(",", d.Args)))})", Dialog(s, 1005, 0, 0, 190, 2, Bpk, 0));
            ck.Expect("no \"It's locked\"", !s.Dialogs.Any(d => d.Args[0] == 1105));
            ck.Prompt("later", "none");
        });
        }
        Run("D3 reload after opening: the door is put straight into its rest state 30 (open, no swing: rule K10′), no prompt", Sim([gFarAct, gFarEv], [(fFar, 1)]),
            ["spot far", "sleep 1", "shot loaded", "press", "sleep 1", "shot pressed"], s =>
            {
                ck.Expect($"door requests: 30 only (got {Req(s, FarDoor)})", Req(s, FarDoor) == "30");
                ck.Prompt("loaded", "none");
                ck.Expect("no dialog", s.Dialogs.Count == 0);
            });
        Run("D4 hall door (no requirement; regression of the V ObjAct group with the K12 helper)", Sim([gHallAct], []),
            ["spot courtyard", "sleep 1", "shot prompt", "press", "sleep 0.5", "shot used"], s =>
            {
                ck.Prompt("prompt", "\"Open\"");
                ck.Expect($"hall door state 70 (got {Req(s, HallDoor)}), flag 11810111 set (got {s.Flag(fHall)})", Req(s, HallDoor) == "70" && s.Flag(fHall) == 1);
                ck.Prompt("used", "none");
            });

        Console.WriteLine($"event-sim doors: {ck.Pass + ck.Fail} checks, {ck.Fail} failed");
        foreach (var f in ck.Failed) Console.WriteLine("  failed: " + f);
        return ck.Fail == 0 ? 0 : 1;
    }

    /// <summary>
    /// Session 11 (track ret11): the return trip's characters on a built pack. Black Knight 11810850 slot 0 (rule L-chain: the
    /// kill awards 1027907000 (BK Sword 20 % / Shield 5 %) and then 1027907001 (Titanite Chunk, sure); its own flag set; a reload
    /// awards nothing again), the Stray Demon 11815382 / 11810900 (rule B-k, B-inv, BR1, E-REQ-B, G4: first visit and a return
    /// load outside the pit: logic flag off, no battle; on the pit floor: logic flag on, 132201, the pit fog shown; the kill:
    /// 132202, fog hidden, the pit ladder shown after event 0 hid it), hollow Oscar 11810531 (1073 ∧ 11810000 → 1061, rule W1's
    /// judge-5 literal).
    /// </summary>
    /// <summary>
    /// Session 11 (burg11, M4): the Burg's boss fights on a built pack (m10_01 → its host): for the Taurus Demon, the Capra Demon
    /// and the Bell Gargoyles, the player walks into DS1's trigger areas → the boss's logic flag (B-k) / battle start 132201;
    /// the kill → 132202 and DS1's defeat flag; the Gargoyles' second at 60 % (static: its group requests the battle gauge);
    /// the Hellkite and the Titanite Demon: their generators are placed (static). Groups are found by their commands.
    /// </summary>
    static int BurgBosses(string pack, bool trace)
    {
        var ck = new Checks();
        const string ds1Map = "m10_01_00_00";
        string map = Build.Maps.MapHosts.Host(ds1Map);
        var esd = ESD.Read(Path.Combine(pack, "gamedata", "ezstate", $"event_{map}.esd"));
        static int A(byte[] b) => (int)EventSim.Evaluate(b, (_, _) => 0);
        int F(int ds1) => FlagRules.Map("m10_01", ds1) ?? FlagRules.OtherMapPersistent("m10_01", ds1) ?? throw new InvalidDataException($"no DS2 flag for {ds1}");
        IEnumerable<(long G, ESD.CommandCall C)> All() => esd.StateGroups.SelectMany(g => g.Value.Values.SelectMany(s => s.EntryCommands.Concat(s.Conditions.SelectMany(k => k.PassCommands))).Select(c => (g.Key, c)));
        long Group(Func<ESD.CommandCall, bool> pred, string what) => All().FirstOrDefault(x => pred(x.C)) is var f && f.C != null ? f.G : throw new InvalidDataException($"event_{map}.esd: no group {what}");
        long[] Groups(Func<ESD.CommandCall, bool> pred) => [.. All().Where(x => pred(x.C)).Select(x => x.G).Distinct()];
        var msb1 = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", ds1Map + ".msb"));
        System.Numerics.Vector3 R(int e) => msb1.Regions.GetEntries().First(r => r.EntityID == e).Position + new System.Numerics.Vector3(0, 0.3f, 0);
        EventSim Sim(long[] groups, (int Flag, int V)[] flags, params (string Name, int Region)[] spots)
        {
            var o = new EventSim.Options { Trace = trace };
            foreach (var (n, r) in spots) o.Spots[n] = R(r);
            o.Spots["away"] = R(1012890) + new System.Numerics.Vector3(0, 300, 0);
            foreach (var (f, v) in flags) o.Flags[f] = v;
            return new EventSim(pack, map, groups, o);
        }
        void Run(string name, EventSim sim, string[] steps, Action<EventSim> check)
        {
            Console.WriteLine($"=== {name}");
            sim.Run(steps);
            Print(sim);
            ck.Begin(name[..name.IndexOf(' ')], sim);
            check(sim);
        }
        bool Has(EventSim s, int cmd, int battle) => s.BossLog.Any(b => b.Cmd == cmd && b.Battle == battle);
        string Log(EventSim s) => string.Join(", ", s.BossLog.Select(b => $"{b.Cmd}({b.Battle})"));

        // ---- Taurus Demon (B-k 0): wake 11015382, bar + BGM 11015384, defeat 11010901
        const int Taurus = 1010700, Capra = 1010750, Garg = 1010800;
        int tLogic = FlagRules.BossLogicFlag("m10_01", 0)!.Value, cLogic = FlagRules.BossLogicFlag("m10_01", 1)!.Value, gLogic = FlagRules.BossLogicFlag("m10_01", 2)!.Value;
        long[] tGroups = [.. Groups(c => c.CommandID == 130101 && c.Arguments.Count > 1 && A(c.Arguments[0]) == tLogic), Group(c => c.CommandID == 132201 && A(c.Arguments[0]) == Taurus, "starting the Taurus Demon's battle (11015384)"),
            Group(c => c.CommandID == 132202 && A(c.Arguments[0]) == Taurus, "handling the Taurus Demon's defeat (11010901)")];
        Console.WriteLine($"Taurus groups {string.Join(",", tGroups)}; logic flags {tLogic}/{cLogic}/{gLogic}");
        Run("B1 Taurus Demon: onto the bridge (1012701) → awake (logic flag), into 1012890 → battle; the kill → defeat + 11010901",
            Sim(tGroups.Distinct().ToArray(), [], ("wake", 1012701), ("bar", 1012890)),
            ["spot away", "sleep 1", "shot before", "spot wake", "sleep 8", "spot bar", "sleep 2", "shot fight", "bosskill 1010700", "kill 700", "sleep 3", "shot won"], s =>
            {
                ck.Expect($"logic flag {tLogic} on (got {s.Flag(tLogic)})", s.Flag(tLogic) == 1);
                ck.Expect($"battle 132201({Taurus}) (got {Log(s)})", Has(s, 132201, Taurus));
                ck.Expect($"defeat 132202({Taurus}) (got {Log(s)})", Has(s, 132202, Taurus));
                ck.Expect($"11010901 ({F(11010901)}) set (got {s.Flag(F(11010901))})", s.Flag(F(11010901)) == 1);
            });

        // ---- Capra Demon (B-k 1): the door 1012886 (11015373), the arena 1012880 (11015372: AI + bar), defeat 11010902
        long[] cGroups = [.. Groups(c => c.CommandID == 130101 && c.Arguments.Count > 1 && A(c.Arguments[0]) == cLogic), Group(c => c.CommandID == 132201 && A(c.Arguments[0]) == Capra, "starting the Capra Demon's battle (11015372)"),
            Group(c => c.CommandID == 132202 && A(c.Arguments[0]) == Capra, "handling the Capra Demon's defeat (11010902)"),
            .. Groups(c => c.CommandID == 130101 && c.Arguments.Count > 1 && A(c.Arguments[0]) == F(11015373))];
        Run("B2 Capra Demon: through the door (1012886) into the arena (1012880) → AI on + battle; the kill → defeat + 11010902",
            Sim(cGroups.Distinct().ToArray(), [], ("door", 1012886), ("arena", 1012880)),
            ["spot away", "sleep 1", "spot door", "sleep 2", "spot arena", "sleep 2", "shot fight", "bosskill 1010750", "kill 750", "sleep 3"], s =>
            {
                ck.Expect($"battle 132201({Capra}) (got {Log(s)})", Has(s, 132201, Capra));
                ck.Expect($"logic flag {cLogic} on (got {s.Flag(cLogic)})", s.Flag(cLogic) == 1);
                ck.Expect($"defeat 132202({Capra}) (got {Log(s)})", Has(s, 132202, Capra));
                ck.Expect($"11010902 ({F(11010902)}) set (got {s.Flag(F(11010902))})", s.Flag(F(11010902)) == 1);
            });

        // ---- Bell Gargoyles (B-k 2): after the intro (11010000) and the fog (11015393), the roof 1012990 → AI + bar; both dead → flag 3
        long[] gGroups = [.. Groups(c => c.CommandID == 130101 && c.Arguments.Count > 1 && A(c.Arguments[0]) == gLogic), Group(c => c.CommandID == 132201 && A(c.Arguments[0]) == Garg, "starting the Gargoyles' battle (11015392)"),
            Group(c => c.CommandID == 132202 && A(c.Arguments[0]) == Garg, "handling the Gargoyles' defeat (11010001)"),
            .. Groups(c => c.CommandID == 130101 && c.Arguments.Count > 1 && A(c.Arguments[0]) == F(11015393) && A(c.Arguments[1]) == 1)];
        Run("B3 Bell Gargoyles: after the intro (11010000), past 1012996 (11015393) onto the roof (1012990) → battle; both killed → defeat + flag 3",
            Sim(gGroups.Distinct().ToArray(), [(F(11010000), 1)], ("stairs", 1012996), ("roof", 1012990)),
            ["spot away", "sleep 1", "spot stairs", "sleep 2", "spot roof", "sleep 2", "shot fight", "bosskill 1010800", "kill 800", "kill 801", "sleep 3"], s =>
            {
                ck.Expect($"battle 132201({Garg}) (got {Log(s)})", Has(s, 132201, Garg));
                ck.Expect($"logic flag {gLogic} on (got {s.Flag(gLogic)})", s.Flag(gLogic) == 1);
                ck.Expect($"defeat 132202({Garg}) (got {Log(s)})", Has(s, 132202, Garg));
                ck.Expect($"flag 3 ({F(3)}) set (got {s.Flag(F(3))})", s.Flag(F(3)) == 1);
            });

        // ---- static: the second Gargoyle (11015396), the Hellkite, the Titanite Demon
        ck.Begin("S", null!);
        int gargStarts = All().Where(x => x.C.CommandID == 132201 && A(x.C.Arguments[0]) == Garg).Select(x => x.G).Distinct().Count();
        ck.Expect($"B4 the second Gargoyle's group (11015396) joins the battle too: 132201({Garg}) in {gargStarts} groups (≥ 2)", gargStarts >= 2);
        var gens = PARAM.Read(Path.Combine(pack, "gamedata", "param", $"generatorparam_{map}.param")).Rows.Select(r => r.ID).ToHashSet();
        foreach (var (what, gen) in new[] { ("B5 Hellkite Drake (1010300) generator", 300), ("B5 its fly-over double (1010302)", 302), ("B6 Titanite Demon generator", Build.Events.EntityRules.Generator(msb1.Parts.Enemies.First(e => e.ModelName == "c2300").EntityID)),
                ("B1 Taurus generator", 700), ("B2 Capra generator", 750), ("B2 Capra's dogs", 500), ("B3 Gargoyles 800/801", 801) })
            ck.Expect($"{what} {gen} placed", gens.Contains(gen));
        BurgBossesChrai(pack, map, esd, ck, Sim, Run, F, Print);
        Console.WriteLine($"event-sim burg-bosses: {ck.Pass + ck.Fail} checks, {ck.Fail} failed");
        foreach (var f in ck.Failed) Console.WriteLine("  failed: " + f);
        return ck.Fail == 0 ? 0 : 1;
    }
    /// <summary>
    /// Session 11 (track chrai11): the Burg bosses' scripted behaviours on a built pack. B7 the Taurus Demon's leaps (rule EM1: its AI
    /// writes TAE message 700 / 710 on board 7 → 11015386 warps it to 1012741 / 1012740; the AI's message machines write those codes);
    /// B8 the Hellkite's breath (rule HB1: 11010200 ×10, the breath message → the breath bullet on the dragon); B9 the Hellkite's
    /// fly-overs (rule FA1: the encoder turns a forced clip's request flag into the code bits, the AI's acknowledgement clears it;
    /// 11010791's intro raises a00_7005's request; the AI's dispatcher acknowledges and requests each clip's action); B10 the tail cuts
    /// (rule TC1: the part break → 11015397 / 11015301 award; BossParam / BossPartsParam / BossPartsModelParam rows, the hit part on
    /// the ragdoll bodies).
    /// </summary>
    static void BurgBossesChrai(string pack, string map, ESD esd, Checks ck, Func<long[], (int Flag, int V)[], (string Name, int Region)[], EventSim> sim,
        Action<string, EventSim, string[], Action<EventSim>> run, Func<int, int> F, Action<EventSim> print)
    {
        string dsr = Program.DefaultDsrRoot;
        static int A(byte[] b) => (int)EventSim.Evaluate(b, (_, _) => 0);
        IEnumerable<(long G, ESD.State S)> States() => esd.StateGroups.SelectMany(g => g.Value.Values.Select(s => (g.Key, s)));
        long[] GroupsWith(Func<ESD.CommandCall, bool> p) => [.. States().Where(x => x.S.EntryCommands.Concat(x.S.Conditions.SelectMany(c => c.PassCommands)).Any(p)).Select(x => x.G).Distinct().Order()];
        int Loc(int e) => Ds1MapContext.RegionId(e);
        var gd = Path.Combine(pack, "gamedata");
        bool Writes(string aiFile, int slot, int code) => File.Exists(Path.Combine(gd, "ezstate", aiFile))
            && ESD.Read(Path.Combine(gd, "ezstate", aiFile)).StateGroups.Values.SelectMany(g => g.Values).SelectMany(s => s.EntryCommands)
                .Any(c => c.CommandBank == 1 && c.CommandID == 5300 && c.Arguments.Count == 2 && A(c.Arguments[0]) == slot && A(c.Arguments[1]) == code);

        // ---- B7 Taurus Demon leaps (EM1)
        int c700 = Build.Ai.Boss.EventMessages.Code(dsr, "c2250", 700) ?? -1, c710 = Build.Ai.Boss.EventMessages.Code(dsr, "c2250", 710) ?? -1;
        long[] leap = [.. GroupsWith(c => c.CommandID == 131704 && A(c.Arguments[0]) == 700).Where(g => esd.StateGroups[g].Values.SelectMany(s => s.EntryCommands).Any(c => c.CommandID == 140171))];
        ck.Begin("S7", null!);
        ck.Expect($"B7 11015386 translated: {leap.Length} group(s) warp the Taurus (700) on a board test 140171", leap.Length >= 1);
        ck.Expect($"B7 the Taurus AI (ai225001) writes message 700 (code {c700}) and 710 (code {c710}) on board 7", Writes("ai225001.esd", 7, c700) && Writes("ai225001.esd", 7, c710));
        if (leap.Length > 0)
            run("B7 Taurus Demon leaps (EM1): board 7 = 700's code → warp to 1012741; = 710's → 1012740 (DS1 11015386)", sim(leap, [], []),
                ["sleep 1", $"board 700 7 {c700}", "sleep 0.5", "board 700 7 0", "sleep 1", $"board 700 7 {c710}", "sleep 0.5", "board 700 7 0", "sleep 1"], s =>
                {
                    // DS1's event restarts while the message lasts (the same warp again: idempotent), so consecutive repeats count once
                    var all = s.Warps.Where(x => x.Gen == 700).Select(x => x.Point).ToList();
                    var w = all.Where((p, i) => i == 0 || all[i - 1] != p).ToList();
                    ck.Expect($"warps of 700: {string.Join(", ", all)} → in order [{string.Join(", ", w)}] = [{Loc(1012741)}, {Loc(1012740)}]", w.SequenceEqual([Loc(1012741), Loc(1012740)]));
                });

        // ---- B8 Hellkite breath (HB1 over EM1)
        long[] breath = GroupsWith(c => c.CommandID == 131821 && A(c.Arguments[0]) == 300);
        ck.Begin("S8", null!);
        ck.Expect($"B8 11010200 ×10 translated: {breath.Length} groups attach the breath bullet to the Hellkite (300)", breath.Length == 10);
        int c10 = Build.Ai.Boss.EventMessages.Code(dsr, "c3430", 10) ?? -1;
        ck.Expect($"B8 the Hellkite AI (ai343001) writes message 10 (a00_3000's breath, code {c10}) on board 7", Writes("ai343001.esd", 7, c10));
        var g10 = breath.FirstOrDefault(g => esd.StateGroups[g].Values.SelectMany(s => s.EntryCommands).Any(c => c.CommandID == 140171 && A(c.Arguments[3]) == c10 && A(c.Arguments[4]) == 0));
        int bullet = breath.Length > 0 ? esd.StateGroups[breath[0]].Values.SelectMany(s => s.EntryCommands).First(c => c.CommandID == 131821).Arguments.Select(A).ElementAt(1) : 0;
        if (g10 != 0)
            run("B8 Hellkite breath (HB1): board 7 = message 10's code (a00_3000) → the breath bullet; message gone → the event re-arms", sim([g10], [], []),
                ["sleep 1", $"board 300 7 {c10}", "sleep 0.3", "board 300 7 0", "sleep 1", $"board 300 7 {c10}", "sleep 0.3", "board 300 7 0", "sleep 1"], s =>
                    ck.Expect($"breath bullets on 300: {s.Bullets.Count(b => b.Gen == 300 && b.Bullet == bullet)} of bullet {bullet} (2: once per message)", s.Bullets.Count(b => b.Gen == 300 && b.Bullet == bullet) == 2));
        else ck.Expect($"B8 a breath group tests message 10's code {c10}", false);

        // ---- B9 Hellkite fly-overs (FA1)
        ck.Begin("S9", null!);
        long enc = esd.StateGroups.Keys.Where(g => g is >= Build.Maps.MapScript.Fa1EncoderGroup and < Build.Maps.MapScript.Fa1EncoderGroup + 20)
            .FirstOrDefault(g => esd.StateGroups[g].Values.SelectMany(s => s.EntryCommands).Any(c => c.CommandID == 140171 && A(c.Arguments[1]) == 300));
        ck.Expect($"B9 the Hellkite (300) has a request encoder group (got {enc})", enc != 0);
        if (enc != 0)
        {
            var g = esd.StateGroups[enc];
            // E (state 1): request flag → the code's state 10 + code; the code-0 state writes every bit 0
            var req = g[1].Conditions.Where(c => c.TargetState is >= 11).ToDictionary(c => (int)c.TargetState!.Value - 10, c => (int)EventSim.Evaluate(c.Evaluator[1..^5], (_, _) => 0));
            var bits = g[10].EntryCommands.Where(c => c.CommandID == 130101).Select(c => A(c.Arguments[0])).ToList();
            var layout = Build.Ai.Boss.EventMessages.Layout(dsr, Build.Chr.Ds1Boss.HellkiteDrake)!;
            int c7004 = layout.ClipCode[7004], c7005 = layout.ClipCode[7005];
            ck.Expect($"B9 encoder: {bits.Count} code bits, request flags for codes {string.Join(",", req.Keys)} (a00_7004 = {c7004}, a00_7005 = {c7005})", bits.Count == layout.Bits && req.ContainsKey(c7004) && req.ContainsKey(c7005));
            if (req.ContainsKey(c7004) && bits.Count > 0)
                run($"B9 Hellkite fly-over (FA1): a00_7004's request flag → code {c7004} on the bits; the AI's acknowledgement (board 6) clears the request, code back to 0", sim([enc], [], []),
                    ["sleep 0.5", $"flag {req[c7004]} 1", "sleep 0.5", "shot requested", $"board 300 6 {c7004}", "sleep 0.5", "shot acked"], s =>
                    {
                        int Code() => bits.Select((b, k) => s.Flag(b) << k).Sum();
                        ck.Expect($"request flag {req[c7004]} cleared by the acknowledgement (got {s.Flag(req[c7004])}), code now {Code()} (0)", s.Flag(req[c7004]) == 0 && Code() == 0);
                        ck.Expect($"while requested the bits held {c7004}", s.Log.Any(l => l.Contains($"flag {bits[2]} = {(c7004 >> 2) & 1}")) || (c7004 >> 2 & 1) == 0);
                    });
            // the events: 11010791's intro raises a00_7005's request; 11010805's fly-overs raise a00_7004 / 7008 / 7009 / 7011
            long[] intro = GroupsWith(c => c.CommandID == 130101 && req.TryGetValue(c7005, out int f5) && A(c.Arguments[0]) == f5 && A(c.Arguments[1]) == 1);
            long[] fly = GroupsWith(c => c.CommandID == 130101 && req.TryGetValue(c7004, out int f4) && A(c.Arguments[0]) == f4 && A(c.Arguments[1]) == 1);
            ck.Expect($"B9 a00_7005 (intro 11010791) requested by {intro.Length} group(s), a00_7004 (fly-over 11010805) by {fly.Length}", intro.Length >= 1 && fly.Length >= 1);
            ck.Expect("B9 the Hellkite AI (ai343001) acknowledges a00_7004 / 7005 on board 6", Writes("ai343001.esd", 6, c7004) && Writes("ai343001.esd", 6, c7005));
            if (intro.Length > 0)
                run("B9b Hellkite intro (DS1 11010791): the fly-over double has passed (11010790), the player on the bridge (1012305) → warp to 1012302 and the a00_7005 request", sim([intro[0]], [(F(11010790), 1)], [("bridge", 1012305)]),
                    ["sleep 1", "spot bridge", "sleep 2"], s =>
                    {
                        ck.Expect($"a00_7005 requested (flag {req[c7005]} set: got {s.Flag(req[c7005])} or cleared by the encoder later)", s.Log.Any(l => l.Contains($"flag {req[c7005]} = 1")));
                        ck.Expect($"warp to 1012302 ({Loc(1012302)}): {string.Join(", ", s.Warps.Select(w => $"{w.Gen}→{w.Point}"))}", s.Warps.Any(w => w.Gen == 300 && w.Point == Loc(1012302)));
                    });
        }

        // ---- B10 tail cuts (TC1)
        ck.Begin("S10", null!);
        long[] cuts = [.. States().Where(x => x.S.Conditions.Any(c => Contains(c.Evaluator, 130310))).Select(x => x.G).Distinct()];
        ck.Expect($"B10 the tail-cut groups test the part break f130310 ({cuts.Length} groups)", cuts.Length >= 2);
        foreach (var (gen, what) in new[] { (800, "Bell Gargoyle 1010800 (11015397 → Gargoyle Tail Axe)"), (300, "Hellkite 1010300 (11015301 → Drake Sword)") })
        {
            long gg = cuts.FirstOrDefault(c => esd.StateGroups[c].Values.SelectMany(s => s.Conditions).Any(k => Contains(k.Evaluator, 130310) && Contains(k.Evaluator, gen)));
            if (gg == 0) { ck.Expect($"B10 {what}: a group tests its part break", false); continue; }
            run($"B10 {what}: the part break → the award", sim([gg], [(F(11015392), 1)], []), ["sleep 3", "hp 300 50", $"partbreak {gen}", "sleep 4"], s =>
                ck.Expect($"awarded lots: {string.Join(", ", s.Awards)}", s.Awards.Count >= 1));
        }
        // params and model
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        var reg = BND4.Read(Formats.Ds2Regulation.Decrypt(Path.Combine(pack, "title", "enc_regulation.bnd.dcx")));
        PARAM? P(string n) { var f = reg.Files.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x.Name).Equals(n, StringComparison.OrdinalIgnoreCase)); if (f == null) return null; var p = PARAM.Read(f.Bytes); return p.ApplyParamdefCarefully(defs) ? p : null; }
        var bossP = P("BossParam"); var partsP = P("BossPartsParam"); var modelP = P("BossPartsModelParam");
        foreach (int ep in new[] { 343000, 535000 })   // (the second Gargoyle 535001 has no DS1 part: 11015398 only masks hits)
        {
            var b = bossP?[ep]; var pr = partsP?[ep * 100 + 7]; var mr = modelP?[ep * 100 + 1];
            ck.Expect($"B10 EnemyParam {ep}: BossParam part1Hp {b?["part1Hp"].Value}, BossPartsParam {ep * 100 + 7} hpCategory {pr?["hpCategory"].Value}, BossPartsModelParam {ep * 100 + 1} culling {mr?["baseCulling"].Value}→{mr?["breakCulling"].Value}",
                b != null && Convert.ToInt32(b["part1Hp"].Value) > 0 && pr != null && Convert.ToInt32(pr["hpCategory"].Value) == 1 && mr != null && Convert.ToInt32(mr["baseCulling"].Value) != Convert.ToInt32(mr["breakCulling"].Value));
        }
        foreach (var chr in new[] { "c3430", "c5350" })
        {
            var bnd = BND4.Read(Path.Combine(gd, "model", "chr", chr + ".bnd"));
            var pwv = bnd.Files.FirstOrDefault(f => f.Name.EndsWith("_Ragdoll.hkxpwv", StringComparison.OrdinalIgnoreCase))?.Bytes ?? [];
            int bodies = pwv.Length >= 8 ? BitConverter.ToUInt16(pwv, 6) : 0, tail = 0;
            for (int i = 0; i < bodies; i++) if (24 + 132 * i + 132 <= pwv.Length && BitConverter.ToUInt16(pwv, 24 + 132 * i + 130) == 7) tail++;
            ck.Expect($"B10 {chr}: {tail} of {bodies} ragdoll bodies carry hit part 7 (the tail)", tail > 0);
        }
    }

    /// <summary>
    /// Session 11 (dchr11): the Depths' boss fight on a built pack (m10_00 → its host). The Gaping Dragon (B-k 0, entity 1000800):
    /// the player enters DS1's arena area 1002999 → 11005393 (entry) → 11005392 (DS1: cutscene 100000, enable, flag 11000000, AI on,
    /// boss bar) = the boss's logic flag on + battle start 132201; the kill → 11000001: 132202 (Handle Boss Defeat) and DS1's flag 2.
    /// A reload after the kill (flag 2 on): no battle. Static: the generators of the Depths' new characters are placed.
    /// </summary>
    static int DepthsBosses(string pack, bool trace)
    {
        var ck = new Checks();
        const string ds1Map = "m10_00_00_00";
        string map = Build.Maps.MapHosts.Host(ds1Map);
        var esd = ESD.Read(Path.Combine(pack, "gamedata", "ezstate", $"event_{map}.esd"));
        static int A(byte[] b) => (int)EventSim.Evaluate(b, (_, _) => 0);
        int F(int ds1) => FlagRules.Map("m10_00", ds1) ?? FlagRules.OtherMapPersistent("m10_00", ds1) ?? throw new InvalidDataException($"no DS2 flag for {ds1}");
        IEnumerable<(long G, ESD.CommandCall C)> All() => esd.StateGroups.SelectMany(g => g.Value.Values.SelectMany(s => s.EntryCommands.Concat(s.Conditions.SelectMany(k => k.PassCommands))).Select(c => (g.Key, c)));
        long Group(Func<ESD.CommandCall, bool> pred, string what) => All().FirstOrDefault(x => pred(x.C)) is var f && f.C != null ? f.G : throw new InvalidDataException($"event_{map}.esd: no group {what}");
        long[] Groups(Func<ESD.CommandCall, bool> pred) => [.. All().Where(x => pred(x.C)).Select(x => x.G).Distinct()];
        var msb1 = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", ds1Map + ".msb"));
        System.Numerics.Vector3 R(int e) => msb1.Regions.GetEntries().First(r => r.EntityID == e).Position + new System.Numerics.Vector3(0, 0.3f, 0);
        EventSim Sim(long[] groups, (int Flag, int V)[] flags, params (string Name, int Region)[] spots)
        {
            var o = new EventSim.Options { Trace = trace };
            foreach (var (n, r) in spots) o.Spots[n] = R(r);
            o.Spots["away"] = R(1002999) + new System.Numerics.Vector3(0, 300, 0);
            foreach (var (f, v) in flags) o.Flags[f] = v;
            return new EventSim(pack, map, groups, o);
        }
        void Run(string name, EventSim sim, string[] steps, Action<EventSim> check)
        {
            Console.WriteLine($"=== {name}");
            sim.Run(steps);
            Print(sim);
            ck.Begin(name[..name.IndexOf(' ')], sim);
            check(sim);
        }
        bool Has(EventSim s, int cmd, int battle) => s.BossLog.Any(b => b.Cmd == cmd && b.Battle == battle);
        string Log(EventSim s) => string.Join(", ", s.BossLog.Select(b => $"{b.Cmd}({b.Battle})"));

        const int Dragon = 1000800;
        int logic = FlagRules.BossLogicFlag("m10_00", 0)!.Value;
        long[] groups = [.. Groups(c => c.CommandID == 130101 && c.Arguments.Count > 1 && A(c.Arguments[0]) == logic),
            Group(c => c.CommandID == 132201 && A(c.Arguments[0]) == Dragon, "starting the Gaping Dragon's battle (11005392)"),
            Group(c => c.CommandID == 132202 && A(c.Arguments[0]) == Dragon, "handling the Gaping Dragon's defeat (11000001)"),
            .. Groups(c => c.CommandID == 130101 && c.Arguments.Count > 1 && A(c.Arguments[0]) == F(11005393) && A(c.Arguments[1]) == 1)];
        Console.WriteLine($"Gaping Dragon groups {string.Join(",", groups.Distinct())}; logic flag {logic}");
        Run("D1 Gaping Dragon: into the arena (1002999) → entry 11005393, AI on (logic flag) + battle 132201; the kill → defeat 132202 + DS1 flag 2",
            Sim(groups.Distinct().ToArray(), [], ("arena", 1002999)),
            ["spot away", "sleep 1", "shot before", "spot arena", "sleep 4", "shot fight", "bosskill 1000800", "kill 800", "sleep 3", "shot won"], s =>
            {
                ck.Expect($"entry flag 11005393 ({F(11005393)}) set (got {s.Flag(F(11005393))})", s.Flag(F(11005393)) == 1);
                ck.Expect($"logic flag {logic} on (got {s.Flag(logic)})", s.Flag(logic) == 1);
                ck.Expect($"battle 132201({Dragon}) (got {Log(s)})", Has(s, 132201, Dragon));
                ck.Expect($"defeat 132202({Dragon}) (got {Log(s)})", Has(s, 132202, Dragon));
                ck.Expect($"flag 2 ({F(2)}) set (got {s.Flag(F(2))})", s.Flag(F(2)) == 1);
            });
        Run("D2 reload after the kill (flag 2 on): in the arena no battle starts",
            Sim(groups.Distinct().ToArray(), [(F(2), 1)], ("arena", 1002999)),
            ["spot away", "sleep 1", "spot arena", "sleep 4", "shot after"], s =>
            {
                ck.Expect($"no battle 132201({Dragon}) (got {Log(s)})", !Has(s, 132201, Dragon));
                ck.Expect($"logic flag {logic} off (got {s.Flag(logic)})", s.Flag(logic) == 0);
            });

        // ---- rule TC1 (chrai11) + TC1-E: the tail cut (11005396 after the fog 11005390): DS1 part 5260's HP ≤ 0 = the DS2 part break of the
        // Dragon's generator (f130310) → the Dragon King Greataxe lot 52610000 (the break reaction's message 400: rule EM1-r)
        static bool HasFn(byte[] b, int fn) { for (int i = 0; i + 5 <= b.Length; i++) if (b[i] == 0x82 && BitConverter.ToInt32(b, i + 1) == fn) return true; return false; }
        int dGen = Build.Events.EntityRules.Generator(Dragon);
        long[] cut = [.. esd.StateGroups.Where(g => g.Value.Values.SelectMany(s => s.Conditions).Any(k => HasFn(k.Evaluator, 130310) && HasFn(k.Evaluator, dGen))).Select(g => g.Key)];
        Run("D5 Gaping Dragon tail cut: after the fog (11005390) the part break of generator " + dGen + " → the Dragon King Greataxe award (lot 52610000)",
            Sim(cut, [(F(11005390), 1)]),
            ["sleep 3", $"partbreak {dGen}", "sleep 5"], s =>
            {
                ck.Expect($"tail-cut group found ({cut.Length})", cut.Length > 0);
                ck.Expect($"awarded lots: {string.Join(", ", s.Awards)}", s.Awards.Count >= 1);
            });

        // ---- rule WK2: the first ceiling Slime (1000100) drops when the player enters DS1's area 1002100 (11005100 slot 0: 2004:9 clears its
        // waiting animation) → its wake flag = its generator's logicEventFlag1 (GeneratorParam +0x5C)
        var genDefs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(x => PARAMDEF.XmlDeserialize(x)).ToList();
        var gp = PARAM.Read(Path.Combine(pack, "gamedata", "param", $"generatorparam_{map}.param"));
        gp.ApplyParamdefCarefully(genDefs);
        int wake = Convert.ToInt32(gp[Build.Events.EntityRules.Generator(1000100)]!["logicEventFlag1"].Value);
        long[] sGroups = Groups(c => c.CommandID == 130101 && c.Arguments.Count > 1 && A(c.Arguments[0]) == wake);
        Run("D4 ceiling Slime 1000100: the player enters 1002100 → its wake flag (logicEventFlag1, rule WK2) on",
            Sim(sGroups, [], ("under", 1002100)),
            ["spot away", "sleep 1", "spot under", "sleep 2", "shot dropped"], s =>
            {
                ck.Expect($"wake group found ({sGroups.Length})", sGroups.Length > 0);
                ck.Expect($"wake flag {wake} on (got {s.Flag(wake)})", s.Flag(wake) == 1);
            });

        // ---- static: the generators of the Depths' new characters (rule MB1 / regulars)
        ck.Begin("S", null!);
        var gens = PARAM.Read(Path.Combine(pack, "gamedata", "param", $"generatorparam_{map}.param")).Rows.Select(r => r.ID).ToHashSet();
        foreach (var (what, entity) in new[] { ("D3 Gaping Dragon", Dragon), ("D3 Giant Rat", 1000500), ("D3 Butcher (chopping)", 1000099), ("D3 Butcher", 1000090), ("D3 Butcher (black phantom)", 1000905),
                ("D3 Slime", 1000100), ("D3 Basilisk", 1000200) })
        {
            int gen = Build.Events.EntityRules.Generator(entity);
            ck.Expect($"{what} (entity {entity}) generator {gen} placed", gens.Contains(gen));
        }
        Console.WriteLine($"event-sim depths-bosses: {ck.Pass + ck.Fail} checks, {ck.Fail} failed");
        foreach (var f in ck.Failed) Console.WriteLine("  failed: " + f);
        return ck.Fail == 0 ? 0 : 1;
    }

    static bool Contains(byte[] b, int fn)
    {
        for (int i = 0; i + 5 <= b.Length; i++) if (b[i] == 0x82 && BitConverter.ToInt32(b, i + 1) == fn) return true;
        return false;
    }

    static int Return(string pack, bool trace)
    {
        var ck = new Checks();
        const string map = "m18_01_00_00";
        var esd = ESD.Read(Path.Combine(pack, "gamedata", "ezstate", $"event_{map}.esd"));
        static int A(byte[] b) => (int)EventSim.Evaluate(b, (_, _) => 0);
        int F(int ds1) => FlagRules.Map("m18_01", ds1) ?? FlagRules.OtherMapPersistent("m18_01", ds1) ?? throw new InvalidDataException($"no DS2 flag for {ds1}");
        IEnumerable<(long G, ESD.CommandCall C)> All() => esd.StateGroups.SelectMany(g => g.Value.Values.SelectMany(s => s.EntryCommands.Concat(s.Conditions.SelectMany(k => k.PassCommands))).Select(c => (g.Key, c)));
        long Group(Func<ESD.CommandCall, bool> pred, string what) => All().FirstOrDefault(x => pred(x.C)) is var f && f.C != null ? f.G : throw new InvalidDataException($"event_{map}.esd: no group {what}");
        const int Bk0 = 200, Sd = 810, SdBattle = 1810810, Ladder = 18011141, PitFog = 18011890;
        int fLeft = F(11810000), fBk0 = F(11810850), f1073 = F(1073), f1061 = F(1061);
        int fLogic = FlagRules.BossLogicFlag("m18_01", 1)!.Value;
        long gBk = Group(c => c.CommandID == 140111 && c.Arguments.Count > 1 && A(c.Arguments[1]) == Bk0, "waiting for the Black Knight 200's death (11810850 slot 0)");
        long gFight = Group(c => c.CommandID == 132201 && A(c.Arguments[0]) == SdBattle, "starting the Stray Demon's battle (11815382)");
        long gDeath = Group(c => c.CommandID == 132202 && A(c.Arguments[0]) == SdBattle, "handling the Stray Demon's defeat (11810900)");
        long gHide = Group(c => c.CommandID == 131636 && A(c.Arguments[0]) == Ladder && A(c.Arguments[1]) == 30, "hiding the pit ladder at load (event 0, rule G4)");
        long gOscar = Group(c => c.CommandID == 130101 && A(c.Arguments[0]) == f1061 && A(c.Arguments[1]) == 1, "setting 1061 (11810531)");
        Console.WriteLine($"groups: BK slot 0 {gBk}, Stray Demon fight {gFight} / death {gDeath}, event 0 {gHide}, hollow Oscar {gOscar}; flags 11810000={fLeft} 11810850={fBk0} SD logic={fLogic} 1073={f1073} 1061={f1061}");
        var msb1 = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", map + ".msb"));
        var floor = msb1.Regions.GetEntries().First(r => r.EntityID == 1812896).Position;
        EventSim Sim(long[] groups, (int Flag, int V)[] flags)
        {
            var o = new EventSim.Options { Trace = trace };
            o.Spots["hall"] = new(3.13f, 197.7f, -30.0f);
            o.Spots["pit"] = floor + new Vector3(0, 0.5f, 0);
            o.Spots["corridor"] = new(-13.8f, 185.3f, -38.0f);
            foreach (var (f, v) in flags) o.Flags[f] = v;
            return new EventSim(pack, map, groups, o);
        }
        void Run(string name, EventSim sim, string[] steps, Action<EventSim> check)
        {
            Console.WriteLine($"=== {name}");
            sim.Run(steps);
            Print(sim);
            ck.Begin(name[..name.IndexOf(' ')], sim);
            check(sim);
        }
        int Lot(int ds1Row) => EntityRules.ItemLot(ds1Row);

        // ---- Black Knight (W1 + L-chain)
        Run("R1 return trip: the Black Knight in the corridor dies → DS1's two lot rows, each its own award; own flag set", Sim([gBk], [(fLeft, 1)]),
            ["spot corridor", "sleep 1", "shot alive", "kill 200", "sleep 1", "shot dead"], s =>
            {
                ck.Expect($"awards {Lot(27907000)} (BK Sword 20 % / Shield 5 %) then {Lot(27907001)} (Titanite Chunk, sure) (got {string.Join(", ", s.Awards)})",
                    s.Awards.SequenceEqual([Lot(27907000), Lot(27907001)]));
                ck.Expect($"11810850 slot 0's own flag {fBk0} set (the generator's start condition 8/{fBk0} keeps it away; got {s.Flag(fBk0)})", s.Flag(fBk0) == 1);
            });
        Run("R2 reload after the kill: nothing awarded again", Sim([gBk], [(fLeft, 1), (fBk0, 1)]), ["spot corridor", "sleep 2", "kill 200", "sleep 1"], s =>
            ck.Expect($"no award (got {string.Join(", ", s.Awards)})", s.Awards.Count == 0));

        // ---- Stray Demon (B-k, B-inv, BR1, E-REQ-B, G4)
        Run("R3 first visit (11810000 off), the player in the pit: the demon stays asleep (no battle, logic flag off)", Sim([gFight, gDeath, gHide], []),
            ["spot hall", "sleep 1", "spot pit", "sleep 2", "shot pit"], s =>
            {
                ck.Expect($"no boss battle command (got {string.Join(", ", s.BossLog.Select(b => b.Cmd))})", s.BossLog.Count == 0);
                ck.Expect($"logic flag {fLogic} off (got {s.Flag(fLogic)})", s.Flag(fLogic) == 0);
                ck.Expect($"pit ladder {Ladder} hidden by event 0 (got {(s.Requests.TryGetValue(Ladder, out var r) ? r.State : -1)})", s.Requests.TryGetValue(Ladder, out var r2) && r2.State == 30);
            });
        Run("R4 return trip: outside the pit nothing; on the pit floor the battle starts (logic flag, 132201, fog); the kill: 132202, fog off, ladder shown",
            Sim([gFight, gDeath, gHide], [(fLeft, 1)]),
            ["spot hall", "sleep 1", "shot hall", "spot pit", "sleep 1", "shot fight", "bosskill 1810810", "kill 810", "sleep 1", "shot won"], s =>
            {
                var hall = s.Shots.First(x => x.Name == "hall");
                var start = s.BossLog.FirstOrDefault(b => b.Cmd == 132201);
                ck.Expect($"no battle while the player is in the hall (first 132201 at frame {start.Frame}, hall shot frame {hall.Frame})", start.Frame > hall.Frame);
                ck.Expect($"battle started 132201({SdBattle}) and logic flag {fLogic} set on the pit floor (got {string.Join(", ", s.BossLog.Select(b => $"{b.Cmd}({b.Battle})"))}; flag {s.Flag(fLogic)})",
                    s.BossLog.Any(b => b.Cmd == 132201 && b.Battle == SdBattle) && s.Flag(fLogic) == 1);
                ck.Expect($"defeat 132202({SdBattle}) after the kill (got {string.Join(", ", s.BossLog.Select(b => $"{b.Cmd}({b.Battle})"))})", s.BossLog.Any(b => b.Cmd == 132202 && b.Battle == SdBattle));
                ck.Expect($"pit fog {PitFog} hidden after the kill (got {(s.Objects.TryGetValue(PitFog, out var fo) ? fo.On : -1)})", s.Objects.TryGetValue(PitFog, out var fo2) && fo2.On == 0);
                ck.Expect($"pit ladder {Ladder} shown (state 10) after the kill (got {(s.Requests.TryGetValue(Ladder, out var r) ? r.State : -1)})", s.Requests.TryGetValue(Ladder, out var r3) && r3.State == 10);
            });

        // ---- hollow Oscar (W1 literal 1061)
        Run("R5 Oscar dead (1073) and the return trip: 11810531 sets 1061 (hollow Oscar's start condition, judge 5)", Sim([gOscar], [(f1073, 1), (fLeft, 1)]), ["sleep 1"], s =>
            ck.Expect($"1061 ({f1061}) set (got {s.Flag(f1061)})", s.Flag(f1061) == 1));
        Run("R6 Oscar dead, still the first visit: 1061 stays off", Sim([gOscar], [(f1073, 1)]), ["sleep 1"], s =>
            ck.Expect($"1061 ({f1061}) off (got {s.Flag(f1061)})", s.Flag(f1061) == 0));

        // ---- session 11 (ret11), user bug "Echo → Asylum in the return-trip state" (rules XM1 / XM1-R)
        int fFar = F(11810110), fSwitch = F(11810350);
        string fl = Build.Maps.MapHosts.Host("m10_02");
        var fesd = ESD.Read(Path.Combine(pack, "gamedata", "ezstate", $"event_{fl}.esd"));
        int fArrived = FlagRules.Any(11020001) ?? throw new InvalidDataException("no DS2 flag for 11020001");
        long gArrive = fesd.StateGroups.FirstOrDefault(g => g.Value.Values.Any(s => s.EntryCommands.Any(c => c.CommandID == 130101 && A(c.Arguments[0]) == fArrived && A(c.Arguments[1]) == 1))).Key;
        if (gArrive == 0) throw new InvalidDataException($"event_{fl}.esd: no group setting {fArrived} (11020001)");
        Console.WriteLine($"Firelink {fl}: arrival group {gArrive} (DS1 11020001); repair group 37397; flags 11810110={fFar} 11810350={fSwitch}");
        var fo = new EventSim.Options { Trace = trace };
        // session 11 (fix11): the pack's entry mode. --entry cell builds the XM1-R repair group 37397 (and drops DS1's Firelink
        // fallback, rule XM1); --entry firelink (default) keeps the fallback: arriving in Firelink marks the Asylum as left
        bool cellEntry = ESD.Read(Path.Combine(pack, "gamedata", "ezstate", "event_m18_01_00_00.esd")).StateGroups.ContainsKey(37397);
        Console.WriteLine($"entry mode: {(cellEntry ? "cell (XM1 + XM1-R)" : "firelink (DS1's fallback kept, no XM1-R)")}");
        if (!cellEntry)
        {
            Run("R7f Firelink entry: a character arrives in Firelink before the Asylum: DS1's fallback (11020001 [2]-[4]) marks the Asylum as left → the return trip",
                new EventSim(pack, fl, [gArrive], fo), ["sleep 5"], s =>
                    ck.Expect($"11810000 ({fLeft}) set by the arrival (got {s.Flag(fLeft)})", s.Flag(fLeft) == 1));
            Run("R8f Firelink entry: the Asylum keeps a fallback 11810000 (no XM1-R repair group 37397)", Sim([], [(fLeft, 1), (fSwitch, 1)]), ["sleep 1"], s =>
                ck.Expect($"11810000 ({fLeft}) still on (got {s.Flag(fLeft)})", s.Flag(fLeft) == 1));
            Console.WriteLine($"event-sim return: {ck.Pass + ck.Fail} checks, {ck.Fail} failed");
            foreach (var f in ck.Failed) Console.WriteLine("  failed: " + f);
            return ck.Fail == 0 ? 0 : 1;
        }
        Run("R7 XM1: a character arrives in Firelink by DS2 travel before the Asylum: DS1's fallback no longer marks the Asylum as left",
            new EventSim(pack, fl, [gArrive], fo), ["sleep 5"], s =>
                ck.Expect($"11810000 ({fLeft}) stays off (DS1's 11020001 [4] would set it; got {s.Flag(fLeft)})", s.Flag(fLeft) == 0));
        Run("R8 XM1-R: the user's save (11810000 on from that fallback, far door 11810110 never opened): the Asylum load repairs it → first visit",
            Sim([37397], [(fLeft, 1), (fSwitch, 1)]), ["sleep 1"], s =>
                ck.Expect($"11810000 ({fLeft}) and 11810350 ({fSwitch}) cleared (got {s.Flag(fLeft)}, {s.Flag(fSwitch)})", s.Flag(fLeft) == 0 && s.Flag(fSwitch) == 0));
        Run("R9 XM1-R: a real departure (far door opened, then the crow): the return trip stays", Sim([37397], [(fLeft, 1), (fFar, 1), (fSwitch, 1)]), ["sleep 1"], s =>
            ck.Expect($"11810000 ({fLeft}) still on (got {s.Flag(fLeft)})", s.Flag(fLeft) == 1 && s.Flag(fSwitch) == 1));

        Console.WriteLine($"event-sim return: {ck.Pass + ck.Fail} checks, {ck.Fail} failed");
        foreach (var f in ck.Failed) Console.WriteLine("  failed: " + f);
        return ck.Fail == 0 ? 0 : 1;
    }

    /// <summary>
    /// Session 10 (sandbox asy-events, e3): the Asylum's character / game-state events on a built pack (CharacterEvents).
    /// Static: Oscar's HP set 130317(gen, 0.1f) (SP1, 11815010), his forced death 131721(gen, 1) + 132211(DS1 getSoul ×
    /// SoulScale) (N1, 11810530), the death event's settle (N3, 11810520), event 50's game-cycle test f132241() &gt;= 2 (G5).
    /// Simulated: the player kills Oscar (dead flag 1073 only after the N3 settle, state block 1060-1074 off), the forced
    /// death after his talk when the player walks into 1812121 (souls once), the reload after a death (nothing replays),
    /// the HP set per load (T1), event 50 in NG / NG+ / NG+ as a guest / NG+ reloaded, 11810050 (session 11, rule K2: only for a Darkmoon Blade = DS2 Blue Sentinels).
    /// </summary>
    static int Npcs(string pack, bool trace)
    {
        var ck = new Checks();
        const string map = "m18_01_00_00";
        var esd = ESD.Read(Path.Combine(pack, "gamedata", "ezstate", $"event_{map}.esd"));
        static int A(byte[] b) => (int)EventSim.Evaluate(b, (_, _) => 0);
        static double Af(byte[] b) => EventSim.Evaluate(b, (_, _) => 0);
        int F(int ds1) => FlagRules.Map("m18_01", ds1) ?? FlagRules.OtherMapPersistent("m18_01", ds1) ?? throw new InvalidDataException($"no DS2 flag for {ds1}");
        IEnumerable<(long G, ESD.CommandCall C)> All() => esd.StateGroups.SelectMany(g => g.Value.Values.SelectMany(s => s.EntryCommands).Select(c => (g.Key, c)));
        long? Group(Func<ESD.CommandCall, bool> pred) => All().FirstOrDefault(x => pred(x.C)) is var f && f.C != null ? f.G : null;
        static bool Calls(byte[] b, int fn)
        {
            for (int i = 0; i + 5 <= b.Length; i++) if (b[i] == 0x82 && BitConverter.ToInt32(b, i + 1) == fn) return true;
            return false;
        }

        // ---- static
        Console.WriteLine("=== static");
        var hp = All().Where(x => x.C.CommandID == 130317).Select(x => (x.G, Gen: A(x.C.Arguments[0]), Pct: Af(x.C.Arguments[1]))).ToList();
        foreach (var h in hp) Console.WriteLine($"  group {h.G}: 130317({h.Gen}, {h.Pct:0.###})");
        ck.Begin("S", null!);
        ck.Expect($"SP1 (11815010): exactly one 130317(Oscar, 0.1) (DS1 SpEffect 5492 changeHpRate 99.9) (got {hp.Count})", hp.Count == 1 && Math.Abs(hp[0].Pct - 0.1) < 1e-4);
        if (hp.Count != 1) { Console.WriteLine($"event-sim npcs: {ck.Pass + ck.Fail} checks, {ck.Fail} failed (Oscar's events not in this pack)"); return 1; }
        int oscar = hp[0].Gen;
        long gHp = hp[0].G;
        long? gForce = Group(c => c.CommandID == 131721 && A(c.Arguments[0]) == oscar);
        long? gDeath = Group(c => c.CommandID == 140111 && A(c.Arguments[1]) == oscar);
        long? g50 = esd.StateGroups.FirstOrDefault(g => g.Value.Values.Any(s => s.Conditions.Any(c => Calls(c.Evaluator, EmevdTranslator.FnGameCycle)))).Key is long k50 && k50 != 0 ? k50 : null;
        long? gCov = Group(c => c.CommandID == 130101 && A(c.Arguments[0]) == F(11810050) && A(c.Arguments[1]) == 1);
        Console.WriteLine($"Oscar generator {oscar}; groups: HP set (11815010) {gHp}, forced death (11810530) {gForce}, death (11810520) {gDeath}, event 50 {g50}, 11810050 {gCov}");
        ck.Expect("groups present: 11810520 (140111 on Oscar), 11810530 (131721 on Oscar), event 50 (f132241), 11810050", gForce != null && gDeath != null && g50 != null && gCov != null);
        if (gForce == null || gDeath == null || g50 == null || gCov == null) { Console.WriteLine($"event-sim npcs: {ck.Pass + ck.Fail} checks, {ck.Fail} failed"); return 1; }
        var force = esd.StateGroups[gForce.Value].Values.Select(s => s.EntryCommands).FirstOrDefault(e => e.Any(c => c.CommandID == 131721));
        int soulsCmd = force?.Where(c => c.CommandID == 132211).Select(c => A(c.Arguments[0])).FirstOrDefault() ?? 0;
        int forceArg = force?.Where(c => c.CommandID == 131721).Select(c => A(c.Arguments[1])).First() ?? -1;
        var ds1 = new Build.Ds1Source(Program.DefaultDsrRoot);
        var msb1 = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", map + ".msb"));
        var npc = msb1.Parts.Enemies.First(p => p.EntityID == 6023);
        int souls = Build.Chr.Ds1Calibration.Souls(ds1.Params.Params["NpcParam"][npc.NPCParamID]!);   // bal11 rule BAL2
        ck.Expect($"N1 (11810530): 131721(Oscar, 1) + 132211(DS1 NpcParam {npc.NPCParamID} getSoul × SoulScale = {souls}) in one state (got 131721 arg {forceArg}, 132211({soulsCmd}))", forceArg == 1 && soulsCmd == souls && souls > 0);
        bool settle = esd.StateGroups[gDeath.Value].Values.Any(s => s.Conditions.Any(c => Math.Abs(EventSim.Evaluate(c.Evaluator, (f, _) => f == 3 ? 0.21 : 0) - 1) < 1e-9 && EventSim.Evaluate(c.Evaluator, (f, _) => f == 3 ? 0.19 : 0) == 0));
        ck.Expect($"N3 (11810520): a {CharacterEvents.DeathSettle} s state-time wait after the death test", settle);

        int f1060 = F(1060), f1073 = F(1073), f590 = F(11810590), fOwn520 = F(11810520), fOwn530 = F(11810530), fHpOwn = F(11815010);
        int f705 = F(705), fEstus = F(50000082), fCov = F(11810050), fAl36 = F(71510036), fAl37 = F(71510037);
        int[] kiln = [F(11807200), F(11807210), F(11807220), F(11807240)];
        int[] block = Enumerable.Range(1060, 15).Select(F).ToArray();
        Console.WriteLine($"flags: 1060-1074 {block[0]}..{block[^1]}, 1073 {f1073}, 11810590 {f590}, 705 {f705}, 50000082 {fEstus}, Kiln {string.Join("/", kiln)}, 71510036/37 {fAl36}/{fAl37}");
        Vector3 leave = new EventSim(pack, map, [], new EventSim.Options()).RegionSpot(3701121) ?? throw new InvalidDataException("no region 3701121 (DS1 1812121)");
        EventSim Sim(long[] groups, (int Flag, int V)[] flags, int cycle = 1, bool guest = false, int[]? dead = null, int covenant = 0)
        {
            var o = new EventSim.Options { Trace = trace, Cycle = cycle, Guest = guest, Covenant = covenant };
            o.Spots["cell"] = new(0, 0, 0);
            o.Spots["leave"] = leave;
            foreach (var (f, v) in flags) o.Flags[f] = v;
            foreach (int d in dead ?? []) o.Dead.Add(d);
            return new EventSim(pack, map, groups, o);
        }
        void Run(string name, EventSim sim, string[] steps, Action<EventSim> check)
        {
            Console.WriteLine($"=== {name}");
            sim.Run(steps);
            Print(sim);
            ck.Begin(name[..name.IndexOf(' ')], sim);
            check(sim);
        }
        string Block(EventSim s) => string.Join("", block.Select(f => s.Flag(f)));
        string BlockAfterDeath = string.Join("", block.Select(f => f == f1073 ? 1 : 0));
        long[] chain = [gDeath.Value, gForce.Value, gHp];

        // ---- G3: Oscar's death chain
        Run("O1 the player kills Oscar (no forced death): 1060-1074 off, 1073 on, after the N3 settle", Sim(chain, [(f1060, 1)]),
            ["spot cell", "sleep 1", "kill " + oscar, "sleep 0.5"], s =>
            {
                ck.Expect($"state block = 1073 only (got {Block(s)}, want {BlockAfterDeath}), 11810520's own flag on (got {s.Flag(fOwn520)})", Block(s) == BlockAfterDeath && s.Flag(fOwn520) == 1);
                ck.Expect($"no forced death, no souls from the event (got {s.ForcedDeaths.Count}, {s.Souls})", s.ForcedDeaths.Count == 0 && s.Souls == 0);
                ck.Expect($"SP1: one HP set 130317({oscar}, 0.1) at load (got {string.Join(", ", s.HpSets.Select(h => $"{h.Gen}:{h.Percent:0.###}@{h.Frame}"))})", s.HpSets.Count == 1 && s.HpSets[0].Gen == oscar && s.HpSets[0].Frame <= s.Fps);
            });
        Run("O1e same, 0.1 s after the death: the dead flag still off (N3: the talk machine's death branch goes first)", Sim(chain, [(f1060, 1)]),
            ["spot cell", "sleep 1", "kill " + oscar, "sleep 0.1"], s =>
                ck.Expect($"1073 off, 1060 on (got {s.Flag(f1073)}, {s.Flag(f1060)})", s.Flag(f1073) == 0 && s.Flag(f1060) == 1));
        Run("O2 after his talk (11810590) the player leaves through 1812121: forced death with souls, then the death event", Sim(chain, [(f1060, 1), (f590, 1)]),
            ["spot cell", "sleep 1", "shot stay", "spot leave", "sleep 1"], s =>
            {
                ck.Expect($"one forced death of {oscar}, after the player left (got {string.Join(", ", s.ForcedDeaths.Select(d => $"{d.Gen}@{d.Frame}"))})", s.ForcedDeaths.Count == 1 && s.ForcedDeaths[0].Gen == oscar && s.ForcedDeaths[0].Frame > s.Fps);
                ck.Expect($"souls +{souls} once (got {s.Souls})", s.Souls == souls);
                ck.Expect($"state block = 1073 only (got {Block(s)}), own flags 11810530 / 11810520 on (got {s.Flag(fOwn530)}, {s.Flag(fOwn520)})", Block(s) == BlockAfterDeath && s.Flag(fOwn530) == 1 && s.Flag(fOwn520) == 1);
            });
        Run("O2n not talked to (11810590-92 off): leaving does not kill him", Sim(chain, [(f1060, 1)]),
            ["spot cell", "sleep 1", "spot leave", "sleep 1"], s =>
                ck.Expect($"no forced death, block unchanged (got {s.ForcedDeaths.Count}, {Block(s)})", s.ForcedDeaths.Count == 0 && s.Flag(f1060) == 1 && s.Flag(f1073) == 0));
        var afterDeath = block.Select(f => (f, f == f1073 ? 1 : 0)).Concat([(fOwn520, 1), (fOwn530, 1), (f590, 1)]).ToArray();
        Run("O3 the next load after his death: nothing replays (no second death, no souls, block kept)", Sim(chain, afterDeath, dead: [oscar]),
            ["spot leave", "sleep 1.5"], s =>
                ck.Expect($"no forced death, souls 0, block = 1073 only (got {s.ForcedDeaths.Count}, {s.Souls}, {Block(s)})", s.ForcedDeaths.Count == 0 && s.Souls == 0 && Block(s) == BlockAfterDeath));
        Run("O4 SP1 per load: event 0's T1 clears 11815010's temporary flag, the reload sets Oscar's HP again", Sim([gHp], []),
            ["sleep 1", "flag " + fHpOwn + " 0", "reload", "sleep 1"], s =>
                ck.Expect($"two HP sets 0.1 % (got {s.HpSets.Count}: {string.Join(", ", s.HpSets.Select(h => $"{h.Percent:0.###}"))})", s.HpSets.Count == 2 && s.HpSets.All(h => h.Gen == oscar && Math.Abs(h.Percent - 0.1) < 1e-4)));

        // ---- G5: event 50 (NG+), G5b: 11810050 (covenant)
        var kilnOn = kiln.Select(f => (f, 1)).ToArray();
        string Kiln(EventSim s) => string.Join("", kiln.Select(f => s.Flag(f)));
        Run("G1 NG (DS2 cycle 1): event 50's NG+ branch does not run", Sim([g50.Value], kilnOn, cycle: 1), ["sleep 1"], s =>
            ck.Expect($"705 off, 50000082 off, Kiln flags kept 1111 (got {s.Flag(f705)}, {s.Flag(fEstus)}, {Kiln(s)})", s.Flag(f705) == 0 && s.Flag(fEstus) == 0 && Kiln(s) == "1111"));
        Run("G2 NG+ (DS2 cycle 2 = DS1 1 completed): 705 and 50000082 on, Kiln flags off; reloaded, it doesn't run again", Sim([g50.Value], kilnOn, cycle: 2),
            ["sleep 1", "shot first", .. kiln.Select(f => $"flag {f} 1"), "reload", "sleep 1"], s =>
            {
                ck.Expect($"705 on, 50000082 on (got {s.Flag(f705)}, {s.Flag(fEstus)})", s.Flag(f705) == 1 && s.Flag(fEstus) == 1);
                ck.Expect($"after the reload the Kiln flags (set again) stay 1111 (got {Kiln(s)})", Kiln(s) == "1111");
                ck.Expect("the first run cleared them (log: Kiln flag = 0)", s.Log.Any(l => l.Contains($"flag {kiln[0]} = 0")));
            });
        Run("G3 NG+ as a guest (rule C1 Client): nothing", Sim([g50.Value], kilnOn, cycle: 2, guest: true), ["sleep 1"], s =>
            ck.Expect($"705 off, Kiln 1111 (got {s.Flag(f705)}, {Kiln(s)})", s.Flag(f705) == 0 && Kiln(s) == "1111"));
        // session 11 (cov11): rule K2 replaces G5b ("never holds"): DS1 Darkmoon Blade (8) = DS2 Blue Sentinels (covenants.tsv)
        int darkmoon = Build.Talk.Covenants.Of(8)?.Ds2 ?? -1;
        Run("G4 11810050 (K2) in NG+, not a Darkmoon Blade (DS2 covenant 0): Anor Londo's 71510036/37 stay off", Sim([gCov.Value], [], cycle: 2), ["sleep 1"], s =>
            ck.Expect($"71510036/37 off, own flag on (got {s.Flag(fAl36)}, {s.Flag(fAl37)}, {s.Flag(fCov)})", s.Flag(fAl36) == 0 && s.Flag(fAl37) == 0 && s.Flag(fCov) == 1));
        Run($"G4b 11810050 (K2) a Darkmoon Blade (DS2 covenant {darkmoon}): Anor Londo's 71510036/37 on", Sim([gCov.Value], [], cycle: 2, covenant: darkmoon), ["sleep 1"], s =>
            ck.Expect($"71510036/37 on, own flag on (got {s.Flag(fAl36)}, {s.Flag(fAl37)}, {s.Flag(fCov)})", s.Flag(fAl36) == 1 && s.Flag(fAl37) == 1 && s.Flag(fCov) == 1));
        Run("G4c 11810050 (K2) in another DS2 covenant (Heirs of the Sun 1): stay off", Sim([gCov.Value], [], cycle: 2, covenant: 1), ["sleep 1"], s =>
            ck.Expect($"71510036/37 off (got {s.Flag(fAl36)}, {s.Flag(fAl37)})", s.Flag(fAl36) == 0 && s.Flag(fAl37) == 0));

        Console.WriteLine($"event-sim npcs: {ck.Pass + ck.Fail} checks, {ck.Fail} failed");
        foreach (var f in ck.Failed) Console.WriteLine("  failed: " + f);
        return ck.Fail == 0 ? 0 : 1;
    }

    static void Print(EventSim sim)
    {
        foreach (var l in sim.Log) Console.WriteLine("    " + l);
        if (sim.Items.Count + sim.Messages.Count > 0) // session 10 (asy-items)
            Console.WriteLine($"  final: items {string.Join(" ", sim.Items.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"))}; messages {string.Join(" ", sim.Messages.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"))}; objects {string.Join(" ", sim.Objects.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value.On}"))}");
        if (sim.Inventory.Count + sim.Awards.Count > 0) // session 10 (asy-items i2)
            Console.WriteLine($"  final: inventory {string.Join(" ", sim.Inventory.OrderBy(k => k.Key).Select(k => $"{k.Key}x{k.Value}"))}; lots awarded {string.Join(" ", sim.Awards)}; windows {string.Join(" ", sim.Windows.Select(w => $"{w.Text}:{(w.Answer == 1 ? "YES" : w.Answer == 2 ? "NO" : "cancel")}"))}");
        Console.WriteLine("  timeline:");
        foreach (var l in sim.Timeline) Console.WriteLine("    " + l);
        var g2 = sim.G2Total;
        Console.WriteLine($"  G2: replayed slot reads {g2.Replays}, gate bypasses {g2.Bypasses}, registering states re-entered in a run {g2.Revisits}, re-executes {g2.Restarts}");
        if (sim.Unmodelled.Count > 0) Console.WriteLine("  unmodelled: " + string.Join(", ", sim.Unmodelled.OrderBy(x => x)));
    }

    /// <summary>
    /// Session 11 (fix11, user bug "Echo → Asylum: pose, then back to the last DS2 bonfire"), check IDLE1: for each map, the
    /// player spawns at each arrival spot (EventLocation point rows 3700000-3700099 = the entry / Echo spots, 3700400 + 10·i = in
    /// front of bonfire i (MapScript.BonfireSpot), plus
    /// every point row a script warps to) and stands still 60 s with every event group of the map running: no warp at all,
    /// and no event flag change after the load settles (5 s: per-load folds, the origin prompt answered NO by the sim).
    /// Asylum: once as a first visit and once as a return trip (DS1 11810000's flag on).
    /// </summary>
    static int Idle(string pack, List<string> maps, bool trace, string? save = null, int slot = 0)
    {
        var ck = new Checks();
        if (maps.Count == 0) maps = ["m18_01_00_00", Build.Maps.MapHosts.Host("m10_02")];
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        var targets = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase); // every 132125 target of the pack
        foreach (var f in Directory.GetFiles(Path.Combine(pack, "gamedata", "ezstate"), "event_*.esd"))
        {
            ESD e; try { e = ESD.Read(f); } catch { continue; }
            foreach (var c in e.StateGroups.Values.SelectMany(g => g.Values).SelectMany(s => s.EntryCommands).Where(c => c.CommandBank == 1 && c.CommandID == 132125 && c.Arguments.Count >= 4))
            {
                int m = (int)EventSim.Evaluate(c.Arguments[2], (_, _) => 0), p = (int)EventSim.Evaluate(c.Arguments[3], (_, _) => 0);
                string mn = $"m{m / 1000000:D2}_{m / 10000 % 100:D2}_{m / 100 % 100:D2}_{m % 100:D2}";
                if (!targets.TryGetValue(mn, out var s)) targets[mn] = s = [];
                s.Add(p);
            }
        }
        int? fLeft = FlagRules.Map("m18_01", 11810000);
        var saveFlags = save != null ? SaveFlags.Read(save, slot) : null;
        if (saveFlags != null) Console.WriteLine($"save {save} slot {slot}: {saveFlags.Count} global flags on (blocks 10, 20)");
        foreach (var map in maps)
        {
            string esdPath = Path.Combine(pack, "gamedata", "ezstate", $"event_{map}.esd"), locPath = Path.Combine(pack, "gamedata", "param", $"eventlocation_{map}.param");
            if (!File.Exists(esdPath) || !File.Exists(locPath)) { Console.WriteLine($"{map}: not in the pack (skipped)"); continue; }
            var groups = ESD.Read(esdPath).StateGroups.Keys.Where(g => g < 80000).ToList(); // 80000+ = DS2's library / bonfire-rest groups
            var loc = PARAM.Read(locPath);
            if (!loc.ApplyParamdefCarefully(defs)) throw new InvalidDataException($"eventlocation_{map}: no paramdef");
            targets.TryGetValue(map, out var tgt);
            var spots = loc.Rows.Where(r => Convert.ToInt32(r["formType"].Value) == 1 && ((r.ID >= 3700000 && r.ID < 3700100) || (r.ID >= Build.Maps.MapScript.BonfireSpot && r.ID < Build.Maps.MapSeams.SeamSpot && r.ID % 10 == 0) || (tgt?.Contains(r.ID) ?? false)))
                .Select(r => (r.ID, Pos: new Vector3(Convert.ToSingle(r["PositionX"].Value), Convert.ToSingle(r["PositionY"].Value), Convert.ToSingle(r["PositionZ"].Value)))).ToList();
            var flagSets = new List<(string Name, (int F, int V)[] Flags)> { ("first visit", []) };
            if (map == "m18_01_00_00" && fLeft is int fl) flagSets.Add(("return trip", [(fl, 1)]));
            // the failing in-game sequence (run s11-b7): the user's save carries a fallback 11810000 + 11810350 with the far door
            // 11810110 never opened; group 37397 (rule XM1-R) repairs it at load, then the crow group must stay at its cliff-region wait
            if (map == "m18_01_00_00" && fLeft is int fl2 && FlagRules.Map("m18_01", 11810350) is int fSw) flagSets.Add(("user save s11-b7 (stale 11810000 + 11810350)", [(fl2, 1), (fSw, 1)]));
            // --save SL2 SLOT: the character's real global flags (DS2 blocks 10 and 20, Ds2FlagStore) as one more state
            if (saveFlags != null) flagSets.Add(($"save {Path.GetFileName(save)} slot {slot}", saveFlags.Select(kv => (kv.Key, kv.Value)).ToArray()));
            foreach (var (id, pos) in spots)
                foreach (var (fsName0, fs) in flagSets)
                foreach (int cov in Enumerable.Range(0, 10)) // every DS2 covenant (f130361; cov11 rule K2 tests it at load)
                {
                    string fsName = $"{fsName0}, covenant {cov}";
                    var o = new EventSim.Options { Trace = trace, Covenant = cov };
                    foreach (var (f, v) in fs) o.Flags[f] = v;
                    o.Spots["s"] = pos + new Vector3(0, 0.05f, 0);
                    var sim = new EventSim(pack, map, groups, o);
                    ck.Begin($"IDLE1 {map} {id} {fsName}", sim);
                    Console.WriteLine($"=== IDLE1 {map}: spawn at {id} ({pos.X:0.##}, {pos.Y:0.##}, {pos.Z:0.##}), {fsName}, idle 60 s ({groups.Count} groups)");
                    sim.Run(["spot s", "sleep 60"]);
                    if (trace) Print(sim);
                    var warps = sim.Log.Where(l => l.Contains("warp requested")).ToList();
                    var late = sim.Log.Where(l => l.Contains(": flag ") && double.TryParse(l.TrimStart().Split('s')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double t) && t > 5).ToList();
                    foreach (var l in warps.Concat(late).Take(5)) Console.WriteLine("    " + l.Trim());
                    ck.Expect($"{map} {id} ({fsName}): no warp while idle (got {warps.Count})", warps.Count == 0);
                    ck.Expect($"{map} {id} ({fsName}): no flag change after the 5 s load window (got {late.Count})", late.Count == 0);
                }
        }
        Console.WriteLine($"event-sim idle: {ck.Pass + ck.Fail} checks, {ck.Fail} failed");
        foreach (var f in ck.Failed) Console.WriteLine("  failed: " + f);
        return ck.Fail == 0 ? 0 : 1;
    }

    /// <summary>Pass/fail checks of one scenario (printed as CHECK lines; the summary counts them).</summary>
    sealed class Checks
    {
        public int Pass, Fail;
        public readonly List<string> Failed = [];
        string scenario = "";
        EventSim? sim;
        public void Begin(string name, EventSim s) { scenario = name; sim = s; }
        public void Expect(string what, bool ok)
        {
            if (ok) Pass++; else { Fail++; Failed.Add($"{scenario}: {what}"); }
            Console.WriteLine($"  CHECK {(ok ? "pass" : "FAIL")}: {what}");
        }
        public EventSim.ShotInfo Shot(string name) => sim!.Shots.Last(s => s.Name == name);
        public void Prompt(string shot, string prompt) { var s = Shot(shot); Expect($"{shot} prompt {prompt} (got {s.Prompt}{(s.Warped ? ", warped" : "")})", s.Prompt == prompt && !s.Warped); }
        public void Warped(string shot, bool warped) { var s = Shot(shot); Expect($"{shot} {(warped ? "warped" : "not warped")} (got {(s.Warped ? "warped" : "here")})", s.Warped == warped); }
        /// <summary>The loading screen came d s (± 1) after the last press on the helper (the curl).</summary>
        public void LoadingAfter(int helper, double d)
        {
            var press = sim!.Presses.Where(p => p.Helper == helper && (sim.LoadingFrame < 0 || p.Frame <= sim.LoadingFrame)).Select(p => p.Frame).DefaultIfEmpty(-1).Max();
            double t = sim.LoadingFrame < 0 || press < 0 ? double.NaN : (sim.LoadingFrame - press) / sim.Fps;
            Expect($"loading {d:0.#} s (±1) after the last press on {helper} (got {(double.IsNaN(t) ? "no loading" : $"{t:0.00} s")})", Math.Abs(t - d) <= 1);
        }
        public void NoReplay()
        {
            var g = sim!.G2Total;
            Expect($"rule G2: no replayed slot read, gate bypass or re-entered registering state (got {g.Replays}/{g.Bypasses}/{g.Revisits}; re-executes {g.Restarts})", g.Replays == 0 && g.Bypasses == 0 && g.Revisits == 0);
        }
    }

    const string CurlUp = "\"Curl up like a ball\"", StandUp = "\"Stand up\"";

    /// <summary>
    /// The nest runs (the s9-3 in-game protocol, test A of the firelink track): Firelink 38000 (event 0: rule T1's reset),
    /// 38004 (11020020), 38005 (11020021) on a fresh save (142000000 off); the Asylum's 38003 (11810150) after the cliff
    /// flag 101000000; the courtyard fog (11810090). Spots: Firelink nest = warp 3701121 (-43.56, -30.85, 100.85); the
    /// walkway (-39.0, -30.85, 103.25, firelink NOTES test E); "edge" 1.77 m north of the nest box's centre (outside the
    /// 2 m box, inside the 2.41 m prompt sphere; sim only); the Asylum cliff landing 3701110 and the nest (2.67, 212.6,
    /// -96.18); the fog's boxes 3704400 (east) / 3704401 (west).
    /// </summary>
    static int Nests(string pack, bool trace, bool reverse, bool records)
    {
        var ck = new Checks();
        int CurlHelper = Lordran.Tools.Build.Maps.MapHosts.Aabb(Lordran.Tools.Build.Maps.MapHosts.Host("m10_02_00_00")) * 10000 + 7120; // "Curl up like a ball" (Stand up = AABB8120); session 11: AABB of the H1 host (19427120)
        EventSim Fl()
        {
            var o = new EventSim.Options { Trace = trace, Reverse = reverse, Records = records };
            o.Spots["nest"] = new(-43.56f, -30.85f, 100.85f);
            o.Spots["walkway"] = new(-39.0f, -30.85f, 103.25f);
            o.Spots["edge"] = new(-43.36f, -30.85f, 102.9f);
            o.Spots["shrine"] = new(-47.41f, -59.82f, 53.30f);
            o.Flags[142000000] = 0;
            return new EventSim(pack, Lordran.Tools.Build.Maps.MapHosts.Host("m10_02_00_00"), [38000, 38004, 38005], o);
        }
        string[] Burst(string prefix)
        {
            var l = new List<string>();
            for (int k = 1; k <= 6; k++) { if (k > 1) l.Add("sleep 0.15"); l.Add($"shot {prefix}{k}"); }
            return [.. l];
        }
        var runs = new (string Name, string[] Steps, Action Check)[]
        {
            ("A  s9-3 protocol + burst: curl → stand up → (burst, 3 s, 25 s) → curl → 20 s → warp",
                ["spot nest", "sleep 3", "shot fl-nest-1", "press", "sleep 3", "shot fl-nest-2", "press", "sleep 1", .. Burst("fl-burst-"), "sleep 3",
                 "shot fl-nest-3", "sleep 25", "shot fl-nest-4", "press", "sleep 12", "shot fl-nest-5", "sleep 12", "shot fl-nest-6", "sleep 10", "shot fl-nest-7"],
                () =>
                {
                    ck.Prompt("fl-nest-1", CurlUp); ck.Prompt("fl-nest-2", StandUp);
                    for (int k = 1; k <= 6; k++) ck.Prompt($"fl-burst-{k}", CurlUp);
                    ck.Prompt("fl-nest-3", CurlUp); ck.Prompt("fl-nest-4", CurlUp); ck.Prompt("fl-nest-5", StandUp);
                    ck.LoadingAfter(CurlHelper, 20.5); ck.Warped("fl-nest-7", true); ck.NoReplay();
                }),
            ("A2 the in-game s9-3 sequence: curl, stand up, then a third RETURN (curl) → warp",
                ["spot nest", "sleep 3", "press", "sleep 3", "press", "sleep 3", "shot after-stand-3s", "sleep 25", "shot after-stand-28s", "press", "sleep 3", "shot after-3rd-return", "sleep 22", "shot +25s"],
                () => { ck.Prompt("after-stand-3s", CurlUp); ck.Prompt("after-stand-28s", CurlUp); ck.Prompt("after-3rd-return", StandUp); ck.LoadingAfter(CurlHelper, 20.5); ck.Warped("+25s", true); ck.NoReplay(); }),
            ("E  leave while curled (DS1: stand up, no warp), re-enter, curl again → warp",
                ["spot nest", "sleep 3", "press", "sleep 3", "shot curled", "spot walkway", "sleep 1", "shot walked-out", "sleep 25", "shot walkway+26s",
                 "spot nest", "sleep 2", "shot back-in", "press", "sleep 21", "shot +21s"],
                () => { ck.Prompt("curled", StandUp); ck.Prompt("walked-out", "none"); ck.Prompt("walkway+26s", "none"); ck.Prompt("back-in", CurlUp); ck.LoadingAfter(CurlHelper, 20.5); ck.Warped("+21s", true); ck.NoReplay(); }),
            ("E2 walk to the walkway with the prompt up (not curled), come back, curl → warp",
                ["spot nest", "sleep 2", "shot prompt", "spot walkway", "sleep 2", "shot away", "spot nest", "sleep 2", "shot back", "press", "sleep 1", "shot curled", "sleep 21", "shot +22s"],
                () => { ck.Prompt("prompt", CurlUp); ck.Prompt("away", "none"); ck.Prompt("back", CurlUp); ck.Prompt("curled", StandUp); ck.LoadingAfter(CurlHelper, 20.5); ck.Warped("+22s", true); ck.NoReplay(); }),
            ("E3 step out of the nest box but stay in the prompt sphere (sim only): DS1 shows no prompt there; back in, curl",
                ["spot nest", "sleep 2", "shot prompt", "spot edge", "sleep 1", "shot edge", "press", "sleep 1", "shot edge-press", "spot nest", "sleep 1", "shot back", "press", "sleep 1", "shot curled"],
                () => { ck.Prompt("prompt", CurlUp); ck.Prompt("edge", "none"); ck.Prompt("edge-press", "none"); ck.Prompt("back", CurlUp); ck.Prompt("curled", StandUp); ck.NoReplay(); }),
            ("F  curl, reload the map (rule T1), 25 s: no warp; curl again → warp",
                ["spot nest", "sleep 3", "press", "sleep 3", "shot fl-tmp-1", "reload", "sleep 3", "shot fl-tmp-2", "sleep 25", "shot fl-tmp-3", "press", "sleep 21", "shot +21s"],
                () => { ck.Prompt("fl-tmp-1", StandUp); ck.Prompt("fl-tmp-2", CurlUp); ck.Prompt("fl-tmp-3", CurlUp); ck.LoadingAfter(CurlHelper, 20.5); ck.Warped("+21s", true); ck.NoReplay(); }),
            ("G  spam: curl, stand up, curl, stand up 0.5 s apart, then curl and wait",
                ["spot nest", "sleep 2", "press", "sleep 0.5", "press", "sleep 0.5", "press", "sleep 0.5", "press", "sleep 0.5", "shot after-4", "press", "sleep 21", "shot +21s"],
                () => { ck.Prompt("after-4", CurlUp); ck.LoadingAfter(CurlHelper, 20.5); ck.Warped("+21s", true); ck.NoReplay(); }),
        };
        foreach (var (name, steps, check) in runs)
        {
            var sim = Fl();
            Console.WriteLine($"=== Firelink {name}");
            if (name.StartsWith("A ")) Console.Write(sim.Describe());
            sim.Run(steps);
            Print(sim);
            ck.Begin(name[..2].Trim(), sim);
            check();
        }
        // W (informational, rule G2's open point): one curl/stand-up, then 140 walk-away cycles (each re-executes the event,
        // the run number +1), then curl: past 128 runs the curl run's records count as "newer" again (byte window)
        {
            var sim = Fl();
            var steps = new List<string> { "spot nest", "sleep 2", "press", "sleep 2", "press", "sleep 1" };
            for (int k = 0; k < 140; k++) steps.AddRange(["spot walkway", "sleep 0.5", "spot nest", "sleep 0.7"]);
            steps.AddRange(["shot w-back", "press", "sleep 2", "shot w-curled", "sleep 20", "shot w-+22s"]);
            Console.WriteLine("=== Firelink W  (informational) curl + stand up, 140 walk-away cycles, curl → warp?");
            sim.Run(steps);
            foreach (var l in sim.Timeline) Console.WriteLine("    " + l);
            var g2 = sim.G2Total;
            Console.WriteLine($"  G2: replayed slot reads {g2.Replays}, gate bypasses {g2.Bypasses}, registering states re-entered in a run {g2.Revisits}, re-executes {g2.Restarts}; loading {(sim.LoadingFrame >= 0 ? $"at {sim.LoadingFrame / sim.Fps:0.00} s" : "none")}");
        }

        // the Asylum's nest (11810150): curl → warp at once (single player: DS1's [7] SKIP IF multiplayer, no stand-up)
        EventSim As(bool cliffFlag, bool sessionHost = false, int members = 0)
        {
            var o = new EventSim.Options { Trace = trace, Reverse = reverse, Records = records, SessionHost = sessionHost, Members = members };
            o.Spots["cliff"] = new(3.27f, 209.84f, -83.48f);
            o.Spots["nest"] = new(2.67f, 212.6f, -96.18f);
            // session 10: the cliff flag 11810000 by rule F1 (was the pre-flags-fix id 101000000, which no group reads any more:
            // H and H2 failed on main too)
            o.Flags[Build.Events.FlagRules.Map("m18_01", 11810000)!.Value] = cliffFlag ? 1 : 0;
            var esd = ESD.Read(Path.Combine(pack, "gamedata", "ezstate", "event_m18_01_00_00.esd"));
            // the nest group: the one that arms the "Curl up" helper 18017001
            long g = esd.StateGroups.First(kv => kv.Value.Values.Any(s => s.EntryCommands.Any(c => c.CommandID == 140205
                && EventSim.Evaluate(c.Arguments[1], (_, _) => 0) == 18017001))).Key;
            return new EventSim(pack, "m18_01_00_00", [g], o);
        }
        var asRuns = new (string Name, bool Flag, string[] Steps, Action Check)[]
        {
            // fix pass (rule C1): DS1's [7] SKIP IF Multiplayer = other players present; a host alone in an open session
            // (f130601 = 1, no members) still warps; with a partner (members 1) DS1 skips the warp (stand-up branch)
            ("H4 host alone in an open session: curl → warp to Firelink",
                true, ["spot nest", "sleep 2", "shot in-nest", "press", "sleep 2", "shot +2s"],
                () => { ck.Prompt("in-nest", CurlUp); ck.Warped("+2s", true); }),
            ("H5 host with a partner (members 1): curl → no warp (DS1 Multiplayer)",
                true, ["spot nest", "sleep 2", "shot in-nest", "press", "sleep 2", "shot +2s"],
                () => { ck.Prompt("in-nest", CurlUp); ck.Warped("+2s", false); }),
            ("H  after the cliff flag: walk into the nest, curl → warp to Firelink",
                true, ["spot cliff", "sleep 2", "shot cliff", "spot nest", "sleep 2", "shot in-nest", "press", "sleep 2", "shot +2s"],
                () => { ck.Prompt("cliff", "none"); ck.Prompt("in-nest", CurlUp); ck.Warped("+2s", true); ck.NoReplay(); }),
            ("H2 leave the nest with the prompt up, come back, curl → warp",
                true, ["spot nest", "sleep 2", "shot in-nest", "spot cliff", "sleep 2", "shot away", "spot nest", "sleep 2", "shot back", "press", "sleep 2", "shot +2s"],
                () => { ck.Prompt("in-nest", CurlUp); ck.Prompt("away", "none"); ck.Prompt("back", CurlUp); ck.Warped("+2s", true); ck.NoReplay(); }),
            ("H3 before the cliff flag: no prompt in the nest",
                false, ["spot nest", "sleep 3", "shot nest-before", "press", "sleep 2", "shot +2s"],
                () => { ck.Prompt("nest-before", "none"); ck.Prompt("+2s", "none"); ck.NoReplay(); }),
        };
        foreach (var (name, flag, steps, check) in asRuns)
        {
            var sim = As(flag, name.StartsWith("H4") || name.StartsWith("H5"), name.StartsWith("H5") ? 1 : 0);
            Console.WriteLine($"=== Asylum {name}");
            if (name.StartsWith("H ")) Console.Write(sim.Describe());
            sim.Run(steps);
            Print(sim);
            ck.Begin(name[..2].Trim(), sim);
            check();
        }

        // the courtyard fog (11810090): the crossing detector between EventLocation 3704400 (east) and 3704401 (west)
        EventSim Fog(out int fogFlag)
        {
            var o = new EventSim.Options { Trace = trace, Reverse = reverse, Records = records };
            o.Spots["farE"] = new(10.5f, 200.9f, 19.6f);
            o.Spots["east"] = new(7.2f, 200.9f, 19.6f);
            o.Spots["west"] = new(4.4f, 200.9f, 19.6f);
            o.Spots["farW"] = new(1.5f, 200.9f, 19.6f);
            var esd = ESD.Read(Path.Combine(pack, "gamedata", "ezstate", "event_m18_01_00_00.esd"));
            var (g, states) = esd.StateGroups.First(kv => kv.Value.Values.Any(s => s.EntryCommands.Any(c => c.CommandID == 140132
                && EventSim.Evaluate(c.Arguments[1], (_, _) => 0) == 3704400)));
            fogFlag = states.TryGetValue(9000, out var end) && end.EntryCommands.FirstOrDefault(c => c.CommandID == 130101) is { } f
                ? (int)EventSim.Evaluate(f.Arguments[0], (_, _) => 0) : 0;
            return new EventSim(pack, "m18_01_00_00", [g], o);
        }
        var fogRuns = new (string Name, string[] Steps, Action<EventSim, int> Check)[]
        {
            ("FE cross east → west (s8-5's direction)", ["spot farE", "sleep 1", "spot east", "sleep 1", "spot west", "sleep 2", "shot crossed"],
                (s, f) => { Hidden(s, f, "crossed", true); }),
            ("FW cross west → east", ["spot farW", "sleep 1", "spot west", "sleep 1", "spot east", "sleep 2", "shot crossed"],
                (s, f) => { Hidden(s, f, "crossed", true); }),
            ("FA approach east, walk away, come back, cross west", ["spot farE", "sleep 1", "spot east", "sleep 1", "spot farE", "sleep 2", "shot away", "spot east", "sleep 1", "spot west", "sleep 2", "shot crossed"],
                (s, f) => { Hidden(s, f, "away", false); Hidden(s, f, "crossed", true); }),
            ("FS approach east, walk away, come round to the west box (no crossing), then cross east",
                ["spot farE", "sleep 1", "spot east", "sleep 1", "spot farE", "sleep 2", "spot farW", "sleep 2", "spot west", "sleep 2", "shot west-side", "spot east", "sleep 2", "shot crossed"],
                (s, f) => { Hidden(s, f, "west-side", false); Hidden(s, f, "crossed", true); }),
        };
        void Hidden(EventSim s, int flag, string shot, bool hidden)
        {
            int at = s.Shots.Last(x => x.Name == shot).Frame;
            bool hid = s.Objects.Values.Any(v => v.On == 0 && v.Frame <= at);
            bool set = s.Flag(flag) != 0; // (the flag as of the end of the run: set only by the traversal)
            ck.Expect($"{shot}: fog {(hidden ? $"hidden, flag {flag} set" : "still up")} (got {(hid ? "hidden" : "up")}{(hidden ? $", flag {(set ? 1 : 0)}" : "")})", hid == hidden && (!hidden || set));
        }
        foreach (var (name, steps, check) in fogRuns)
        {
            var sim = Fog(out int flag);
            Console.WriteLine($"=== Courtyard fog {name}");
            if (name.StartsWith("FE")) Console.Write(sim.Describe());
            sim.Run(steps);
            Print(sim);
            ck.Begin(name[..2], sim);
            check(sim, flag);
            ck.NoReplay();
        }

        Console.WriteLine($"event-sim nests{(records ? "" : " --no-records")}: {ck.Pass + ck.Fail} checks, {ck.Fail} failed");
        foreach (var f in ck.Failed) Console.WriteLine("  failed: " + f);
        return ck.Fail == 0 ? 0 : 1;
    }
}
