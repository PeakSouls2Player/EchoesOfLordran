using System.Globalization;
using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Events;

/// <summary>
/// Offline frame simulator for DS2 EVENT ESDs (session 9, track nestfix): runs state groups of a built pack's
/// event_MAP.esd frame by frame with DS2's runtime semantics in a scripted world (player position → EventLocation regions
/// and key-guide ranges, RETURN presses on key-guide helpers, flags, warps, map reloads) and reports what the player
/// would see (prompts) and what happens (transitions, flags, warps). Also <see cref="Lint"/> (rule G1: transitions the
/// engine never steps) and <see cref="Revisits"/> (rule G2: registering states re-entered within one run).
///
/// DS2's runtime as modelled (decompile DarkSoulsII.exe 1.0.3; V = read from code):
///  - the event runner FUN_140196b80, once per event per frame, by status (owner +0x2d): 4 running, 5 ending
///    (FUN_1401968f0 destroys the machine, then 1 = idle), 6 re-executing (FUN_1401968f0, then the start check
///    0x140196990 = `mov al,1; ret` and FUN_140196850: a new machine at state 0 and a new run number, status 4; the machine
///    is not stepped in that frame). 130001 → 5 (FUN_140196a20), 130002 → 6 (FUN_140196a40) (V).
///  - G1 the group-condition gate (V): in status 4 the machine is stepped only if (container = owner+0x18; +0x2e is the
///    online bypass, never set offline: FUN_140183960 needs a session, 0x140460797) FUN_140183620 or FUN_140183cb0 says
///    so. FUN_140183cb0: true for an empty container, else it re-evaluates every registered group, sets the slot mask
///    (+0x2a) and returns mask != 0. FUN_140183620 (G2 below): once after a registration or record write (+0x2f), true
///    when a record for the container's path exists in the current run.
///  - the step: the holder's vtable +0x10 = FUN_140b1f490 → machine->Update(100) (FUN_1409859c0): up to 100 iterations
///    per frame; each takes the first condition whose evaluator is non-zero (subconditions as in TalkSim) → exit, pass
///    commands (FUN_1409925d0), the new state is current, the state-change callback [machine+0x110] runs, then the new
///    state's entry commands: chained transitions run within one frame (V, track npctalk).
///  - G2 the state-change callback (V): registered by the holder (FUN_140b1f230: vtable +0x58 ← FUN_140b1f2f0 →
///    holder vtable 0x1410ef368 slot 4 = FUN_140458c20 → FUN_14046e780). It resets the state timer (FUN_140491290), sets
///    the path (holder +0xa0[depth] = the current state's index, FUN_140984b20; our groups call no routines, so the path
///    is the state), then, if the container has registrations, FUN_140183d40 → FUN_140184010 writes a record (path of the
///    state that registered = +0x24, the run number owner+0x2e, the slot mask +0x2a; for the same path the first record of
///    a run is kept, an older run's is overwritten) and only then FUN_140183b60 clears the registrations (the records,
///    +0x18, stay until the event owner is destroyed, FUN_1401833c0). The new state's entry registers (FUN_140470a40 →
///    FUN_1401834a0: path = holder +0xa0, +0x2f = 1).
///  - f140001(n) (0x140460565): FUN_140183c30 evaluates slot n if it was not evaluated in this tick (new groups always:
///    their tick is -1, FUN_140183150) and ORs it into the mask; then FUN_1401836a0 returns the RECORDED bit when a record
///    for the container's path exists in the current run (seq window 0x80, see below), else the live mask bit. Slots
///    below 8 are the OR of their conditions, 8+ the AND (FUN_1401832b0 / FUN_140183260, V). So a state re-entered in
///    one run replays its first exit (FUN_140183620 steps it once even if no slot holds).
///  - run numbers (V): a byte, 0xff at construction; FUN_140196850 sets the newest record's run if it is "newer" than the
///    current one (FUN_1401835a0; byte comparisons with a 0x80 window), else current + 1; 0xff becomes 0. Records are never
///    pruned, so after a wrap an old record can count as "newer" again (U in game).
///  - 140205(slot, instance) "examined" = the object's handle is in the event manager's per-frame list (+0x70 → +0x68
///    → +0x30, FUN_1404705e0 / FUN_140189810), which is emptied at every frame end (FUN_14044ee60 → FUN_140189840,
///    called from 0x1401bf730): true only in the frame of the press (V). The press needs the helper's prompt on screen.
///  - 131638(instance, 1|0) disables/enables the prefab helper's key-guide entries at once (FUN_1401d8870 →
///    FUN_140453c70); the entries live as long as the prefab state (FUN_1401d7ca0 adds, FUN_1401d8970 removes); the
///    helper o04_9300 has one state, so its prompt can be re-enabled any number of times (V; vanilla: Majula's obelisk
///    helpers 10041000/10041010, groups 12000/12010, re-arm 140205 after every examine through 130002).
/// Assumptions (U): f3() = time since the state was entered (0 in its entry frame); a prompt appears the frame after
/// 131638(…, 0) and only while the player is within the KeyGuideParam sphere (width_radius, around the helper's MSB
/// position; dummy 190 offset ignored); EventLocation boxes are bottom-centred (AsylumScript.Regions), rotated by
/// RotationY, with a ±0.5 m vertical tolerance; 132125's load starts <see cref="Options.WarpFrames"/> frames after the
/// command (then the map's events stop); groups run in ascending id order within a frame (--reverse checks order).
/// </summary>
public sealed class EventSim
{
    public sealed class Options
    {
        public double Fps = 60;
        public int Iterations = 100;
        public int WarpFrames = 30;
        public bool Reverse;
        public bool Trace;
        /// <summary>Model DS2's per-run records (rule G2). Off = the first fix pass's model (for comparison only).</summary>
        public bool Records = true;
        /// <summary>Session 10 (rule C1): the player is a guest in someone's world (f130602 = 1, f130601 = 0); default
        /// offline (both 0: decompile cases 0x1fe29/0x1fe2a of FUN_14045c6a0 without a session).</summary>
        public bool Guest;
        /// <summary>Fix pass (rule C1): the player hosts an open session (f130601 = 1) with <see cref="Members"/> other
        /// players (f130661(0, 1)); default offline (0 / 0).</summary>
        public bool SessionHost;
        public int Members;
        /// <summary>Session 10: seconds a prefab's play state (70+) takes before f131653 reports its rest state (the
        /// boulder's clip: 4.333 s).</summary>
        public double AnimSeconds = 4.4;
        public readonly Dictionary<int, int> Flags = [];
        /// <summary>Session 10 (rules K4/K6): DS2 items the player holds (140801 「所持アイテム数比較」 in its vanilla "holds
        /// it" form (slot, item, 1, 3, 1, 1, 0)).</summary>
        public readonly HashSet<int> Items = [];
        public readonly Dictionary<string, Vector3> Spots = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Session 10 (asy-items): answers to the YES/NO windows (130441) in order, 1 YES / 2 NO / 3 cancel; when
        /// they run out: NO (DS2's pre-selected button). A window stays open <see cref="WindowFrames"/> frames.</summary>
        public readonly Queue<int> Answers = [];
        public int WindowFrames = 10;
        /// <summary>Session 10 (asy-items i2, rule S1): the player's inventory (DS2 item id → count) for f131401 / 131401.</summary>
        public readonly Dictionary<int, int> Inventory = [];
        /// <summary>Session 10 (e3, rule G5): DS2's game cycle f132241() (the playthrough in progress: NG = 1).</summary>
        public int Cycle = 1;
        /// <summary>Session 10 (e3): generators whose character is dead when the run starts (it stays dead over loads).</summary>
        public readonly HashSet<int> Dead = [];
        /// <summary>Session 11 (cov11): the player's DS2 covenant (f130361; 130381 changes it).</summary>
        public int Covenant;
        /// <summary>Session 11 (chrai11), rules EM1 / FA1: an AI's logic EzState board ((generator, slot) → value: 140171 reads it; step
        /// 'board GEN SLOT V'), generators whose part broke (f130310; step 'partbreak GEN') and HP percentages (140121; step 'hp GEN PCT',
        /// default 100).</summary>
        public readonly Dictionary<(int Gen, int Slot), int> Board = [];
        public readonly HashSet<int> PartBroken = [];
        public readonly Dictionary<int, double> Hp = [];
    }

    sealed record Region(int Id, Vector3 Pos, float Yaw, Vector3 Size, bool Box);
    sealed record Helper(int Instance, Vector3 Pos, int KeyGuide, float Radius, string Text);
    public sealed record ShotInfo(string Name, int Frame, double Time, List<string> Prompts, bool Warped, string States)
    {
        public string Prompt => Prompts.Count == 0 ? "none" : string.Join(" + ", Prompts);
    }

    readonly Dictionary<long, Dictionary<long, ESD.State>> groups;
    readonly Options o;
    readonly Dictionary<int, Region> regions = [];
    readonly Dictionary<int, Helper> helpers = [];
    readonly List<long> run;

    // world
    int frame;
    Vector3 player;
    bool pressPending;
    readonly Dictionary<int, int> flags;
    readonly Dictionary<int, bool> kgDisabled = [];          // 131638 state per helper (default: enabled)
    readonly HashSet<int> examined = [];                     // this frame's examine records (handles)
    readonly HashSet<int> shownLastFrame = [];               // prompts on screen (as of the end of the previous frame)
    int warpAt = -1; string warpTo = ""; bool unloaded;
    /// <summary>130702(instance, 0|1) as last set (fog walls: 0 = hidden), and the frame it was set.</summary>
    public readonly Dictionary<int, (int On, int Frame)> Objects = [];
    /// <summary>Session 10 (asy-items): 131411(instance, 0|1) item enable and 132221(row, 0|1) blood-message state as last set.</summary>
    public readonly Dictionary<int, int> Items = [], Messages = [];
    /// <summary>Session 10: the YES/NO window (130441): open from frame winFrom to winTo, then its result (f130421).</summary>
    int winFrom = -1, winTo = -1, winAnswer;
    public readonly List<(int Text, int Answer)> Windows = [];
    /// <summary>Session 10 (asy-items i2): the inventory (f131401 / 131401 consume) and the lots awarded (131402).</summary>
    public readonly Dictionary<int, int> Inventory = [];
    public readonly List<int> Awards = [];
    /// <summary>Session 10: 131636(instance, state) as last requested (state, frame), every request in order, and the
    /// objects broken by 131613 (restored by 131652); a map load re-creates every object (rule WE2: resetType 0).</summary>
    public readonly Dictionary<int, (int State, int Frame)> Requests = [];
    public readonly List<(int Instance, int State, int Frame)> RequestLog = [];
    public readonly HashSet<int> Broken = [];
    /// <summary>Session 10: every 130443 dialog (arguments, frame).</summary>
    public readonly List<(int[] Args, int Frame)> Dialogs = [];
    /// <summary>Session 10 (e3, rules N1/N3/SP1): dead generators (step 'kill GEN', 131721; 140111 reads them; they stay
    /// dead over loads), every forced death 131721 (generator, frame), every HP set 130317 (generator, percent, frame),
    /// the souls 132211 handed to the player.</summary>
    public readonly HashSet<int> Dead = [];
    /// <summary>Session 11 (ret11): boss battles won (step 'bosskill BATTLE': f140522 / 「ボス戦_終了判定」 holds) and the boss battle
    /// commands issued (132201 start / 132202 defeat / 132204 gauge off: (command, battle, frame)).</summary>
    public readonly HashSet<int> BossDefeated = [];
    public readonly List<(int Cmd, int Battle, int Frame)> BossLog = [];
    public readonly List<(int Gen, int Frame)> ForcedDeaths = [];
    public readonly List<(int Gen, double Percent, int Frame)> HpSets = [];
    /// <summary>Session 11 (chrai11): 131704 warps (generator, EventLocation), 131821 bullets (generator, bullet).</summary>
    public readonly List<(int Gen, int Point, int Frame)> Warps = [];
    public readonly List<(int Gen, int Bullet, int Frame)> Bullets = [];
    public int Souls;
    public readonly List<(int Instance, int Frame)> BreakLog = [];
    public int Flag(int f) => flags.GetValueOrDefault(f);
    public int Frames => frame;
    bool Active(int inst) => !Objects.TryGetValue(inst, out var ob) || ob.On != 0;

    sealed class Machine
    {
        public long Group;
        public long State;
        public int Entered;                                  // frame the state was entered
        public readonly SortedDictionary<int, List<ESD.CommandCall>> Slots = [];
        public bool Ended, Rerun;                            // status 5 / 6 (130001 / 130002)
        public int Steps, Gated;
        // rule G2: the container's records (+0x18) by path, the run number (owner +0x2e), the registering path (+0x24),
        // the dirty byte (+0x2f) and the live slot mask (+0x2a)
        public byte Seq = 0xff;
        public readonly Dictionary<long, (byte Seq, int Mask)> Records = [];
        public long? Path;
        public bool Dirty;
        public int Mask;
        public int Replays, Bypasses, Restarts, Revisits;
    }
    readonly List<Machine> machines = [];
    Machine? cur;

    public readonly List<string> Log = [];
    public readonly List<string> Timeline = [];
    public readonly List<ShotInfo> Shots = [];
    public readonly HashSet<string> Unmodelled = [];
    /// <summary>Frames of the presses that hit a prompt, with its helper, and the frame the loading screen came.</summary>
    public readonly List<(int Frame, int Helper)> Presses = [];
    public int LoadingFrame { get; private set; } = -1;
    public double Fps => o.Fps;
    /// <summary>Rule G2 counters over all machines of the run (records that differed from the live slot / gate bypasses
    /// / registering states entered with a record of the current run / re-executes).</summary>
    public (int Replays, int Bypasses, int Revisits, int Restarts) G2 => (machines.Sum(m => m.Replays), machines.Sum(m => m.Bypasses),
        machines.Sum(m => m.Revisits), machines.Sum(m => m.Restarts));
    int g2Replays, g2Bypasses, g2Revisits, g2Restarts; // carried over map reloads

    public EventSim(string pack, string map, IEnumerable<long> groupIds, Options options)
    {
        o = options;
        flags = new Dictionary<int, int>(o.Flags);
        foreach (var (k, v) in o.Inventory) Inventory[k] = v;
        Dead.UnionWith(o.Dead);
        string Gd(string p) => System.IO.Path.Combine(pack, "gamedata", p.Replace('/', System.IO.Path.DirectorySeparatorChar));
        groups = ESD.Read(Gd($"ezstate/event_{map}.esd")).StateGroups;
        run = groupIds.Where(groups.ContainsKey).OrderBy(g => g).ToList();
        if (o.Reverse) run.Reverse();
        foreach (var g in groupIds.Where(g => !groups.ContainsKey(g))) Log.Add($"group {g} not in event_{map}.esd");

        var defs = Directory.GetFiles(System.IO.Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        var loc = PARAM.Read(Gd($"param/eventlocation_{map}.param"));
        if (!loc.ApplyParamdefCarefully(defs)) throw new InvalidDataException($"eventlocation_{map}: no paramdef");
        float F(PARAM.Row r, string f) => Convert.ToSingle(r[f].Value, CultureInfo.InvariantCulture);
        foreach (var r in loc.Rows)
        {
            var size = new Vector3(F(r, "ScaleX"), F(r, "ScaleY"), F(r, "ScaleZ"));
            regions[r.ID] = new Region(r.ID, new Vector3(F(r, "PositionX"), F(r, "PositionY"), F(r, "PositionZ")), F(r, "RotationY"), size,
                Convert.ToInt32(r["formType"].Value) != 1);
        }

        // helpers: every instance a 131638 / 140205 of the run groups names → MSB2 position + its prefab's key guide
        var used = run.SelectMany(g => groups[g].Values).SelectMany(s => s.EntryCommands.Concat(s.Conditions.SelectMany(c => c.PassCommands)))
            .Where(c => c.CommandID is 131638 or 140205).Select(c => (int)ConstArg(c.CommandID == 131638 ? c.Arguments[0] : c.Arguments[1])).ToHashSet();
        var msb = MSB2.Read(Gd($"map/{map}/{map}.msb"));
        var reg = BND4.Read(Formats.Ds2Regulation.Decrypt(System.IO.Path.Combine(pack, "title", "enc_regulation.bnd.dcx")));
        PARAM? kgp = null;
        foreach (var f in reg.Files.Where(f => System.IO.Path.GetFileNameWithoutExtension(f.Name).Equals("KeyGuideParam", StringComparison.OrdinalIgnoreCase)))
        {
            kgp = PARAM.Read(f.Bytes);
            if (!kgp.ApplyParamdefCarefully(defs)) kgp = null;
        }
        var kgText = FMG.Read(Gd("menu/text/english/keyguide.fmg"));
        foreach (int inst in used)
        {
            var part = msb.Parts.Objects.FirstOrDefault(p => p.MapObjectInstanceParamID == inst);
            if (part == null) { Log.Add($"helper {inst}: no MSB part"); continue; }
            int kg = KeyGuideOf(pack, part.ModelName);
            var row = kgp?[kg];
            float radius = row != null ? Convert.ToSingle(row["width_radius"].Value, CultureInfo.InvariantCulture) : 1.5f;
            int textId = row != null ? Convert.ToInt32(row["textId"].Value) : 0;
            string text = kgText.Entries.FirstOrDefault(e => e.ID == textId)?.Text ?? $"keyguide {textId}";
            helpers[inst] = new Helper(inst, part.Position, kg, radius, text.Contains(':') ? text[(text.IndexOf(':') + 1)..] : text);
        }
    }

    /// <summary>The KeyGuideParam id inside a prefab helper (the u32 the build swapped in: CloneDs2's 10049040 slot).</summary>
    static int KeyGuideOf(string pack, string model)
    {
        string p = System.IO.Path.Combine(pack, "gamedata", "prefabeditor", model + ".pfbbin");
        if (!File.Exists(p)) return 0;
        var b = File.ReadAllBytes(p);
        // o04_9300.pfbbin keeps its key-guide id at 0x268, followed by the guide type 0x22 (34)
        for (int i = 0; i + 8 <= b.Length; i += 4)
            if (BitConverter.ToInt32(b, i + 4) == 0x22 && BitConverter.ToInt32(b, i) > 1000) return BitConverter.ToInt32(b, i);
        return 0;
    }

    public string Describe()
    {
        var sb = new System.Text.StringBuilder();
        var regionIds = run.SelectMany(g => groups[g].Values).SelectMany(s => s.EntryCommands).Where(c => c.CommandID == 140132)
            .Select(c => (int)ConstArg(c.Arguments[1])).Distinct().OrderBy(r => r);
        foreach (int id in regionIds)
            sb.AppendLine(regions.TryGetValue(id, out var r) ? $"region {id}: {(r.Box ? "box" : "point")} at ({r.Pos.X:0.##}, {r.Pos.Y:0.##}, {r.Pos.Z:0.##}) yaw {r.Yaw:0.#} size ({r.Size.X:0.##}, {r.Size.Y:0.##}, {r.Size.Z:0.##})" : $"region {id}: not in EventLocation");
        foreach (var h in helpers.Values) sb.AppendLine($"helper {h.Instance}: at ({h.Pos.X:0.##}, {h.Pos.Y:0.##}, {h.Pos.Z:0.##}), KeyGuideParam {h.KeyGuide} \"{h.Text}\" r {h.Radius:0.##} m");
        foreach (var (name, p) in o.Spots)
        {
            var inR = regions.Values.Where(r => r.Box && Inside(r, p)).Select(r => r.Id);
            var inK = helpers.Values.Where(h => InRange(h, p)).Select(h => h.Instance);
            sb.AppendLine($"spot {name} ({p.X:0.##}, {p.Y:0.##}, {p.Z:0.##}): in regions [{string.Join(", ", inR)}], in prompt range of [{string.Join(", ", inK)}]");
        }
        return sb.ToString();
    }

    static bool Inside(Region r, Vector3 p)
    {
        var d = p - r.Pos;
        float a = -r.Yaw * MathF.PI / 180f;
        float x = d.X * MathF.Cos(a) - d.Z * MathF.Sin(a), z = d.X * MathF.Sin(a) + d.Z * MathF.Cos(a);
        return MathF.Abs(x) <= r.Size.X / 2 + 1e-3f && MathF.Abs(z) <= r.Size.Z / 2 + 1e-3f && d.Y >= -0.5f && d.Y <= r.Size.Y + 0.5f;
    }
    static bool InRange(Helper h, Vector3 p)
    {
        var d = p - h.Pos;
        return MathF.Sqrt(d.X * d.X + d.Z * d.Z) <= h.Radius && MathF.Abs(d.Y) <= h.Radius;
    }
    /// <summary>Session 10 (e3): a spot inside an EventLocation region (its origin, 0.1 m up), null when the map has none.</summary>
    public Vector3? RegionSpot(int id) => regions.TryGetValue(id, out var r) ? r.Pos + new Vector3(0, r.Box ? 0.1f : 0, 0) : null;
    bool InRegion(int id) => regions.TryGetValue(id, out var r) && (r.Box ? Inside(r, player) : Vector3.Distance(r.Pos, player) < 0.5f);
    bool Shown(int inst) => helpers.TryGetValue(inst, out var h) && !kgDisabled.GetValueOrDefault(inst) && InRange(h, player);

    // ------------------------------------------------------------------ scenario
    public void Start()
    {
        Objects.Clear(); // a load places every object as the map has it
        // fix pass (asy-items): likewise a load rebuilds every pickup's item (enabled unless taken) and every cat-12 message
        // (shown): only this load's 131411/132221 decide the final state, so a reload path that skips them shows up
        Items.Clear(); Messages.Clear();
        Requests.Clear(); Broken.Clear(); // session 10 (WE2): object states don't survive a load either
        foreach (var m in machines) { g2Replays += m.Replays; g2Bypasses += m.Bypasses; g2Revisits += m.Revisits; g2Restarts += m.Restarts; }
        machines.Clear();
        foreach (var g in run) { var m = new Machine { Group = g }; NewRun(m); machines.Add(m); }
        kgDisabled.Clear();
        shownLastFrame.Clear();
        warpAt = -1; unloaded = false;
    }
    /// <summary>Totals of the G2 counters over every map load of the run.</summary>
    public (int Replays, int Bypasses, int Revisits, int Restarts) G2Total => (g2Replays + G2.Replays, g2Bypasses + G2.Bypasses, g2Revisits + G2.Revisits, g2Restarts + G2.Restarts);

    /// <summary>FUN_140196850: a new machine at state 0 and the next run number (the container's records stay).</summary>
    void NewRun(Machine m)
    {
        byte curSeq = m.Seq, newest = 0xff;
        foreach (var (rs, _) in m.Records.Values) // FUN_1401835a0: the newest record "at or after" the current run
            if (rs != 0xff && NotOlder(rs, curSeq) && (newest == 0xff || NotOlder(rs, newest))) newest = rs;
        byte next = newest; // FUN_140196850 as written (0xff = no record: taken, as 0, when the current run is 0x80 or more)
        if (curSeq != 0xff && !(curSeq < next ? next - curSeq < 0x80 : curSeq - next > 0x7f)) next = (byte)(curSeq + 1);
        m.Seq = next == 0xff ? (byte)0 : next;
        m.State = 0; m.Entered = frame; m.Slots.Clear(); m.Mask = 0; m.Path = null; m.Dirty = false; m.Rerun = false;
    }
    /// <summary>DS2's run-number comparison (FUN_1401835a0 / FUN_1401836a0 / FUN_140184010): a is b or newer, within 0x80.</summary>
    static bool NotOlder(byte a, byte b) => a >= b ? a - b < 0x80 : b - a > 0x7f;

    /// <summary>Steps: 'spot NAME' | 'pos X Y Z' | 'press' | 'sleep S' | 'frames N' | 'shot NAME' | 'reload' |
    /// 'flag ID V' | 'kill GEN' (session 10, e3: the generator's character dies).</summary>
    public void Run(IEnumerable<string> steps)
    {
        Start();
        foreach (var raw in steps)
        {
            var a = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (a.Length == 0) continue;
            switch (a[0])
            {
                case "spot": player = o.Spots[a[1]]; Note($"player → {a[1]}"); break;
                case "pos": player = new Vector3(P(a[1]), P(a[2]), P(a[3])); Note($"player → ({a[1]}, {a[2]}, {a[3]})"); break;
                case "press": pressPending = true; Frame(); break;
                case "sleep": for (int n = (int)Math.Round(P(a[1]) * o.Fps); n > 0; n--) Frame(); break;
                case "frames": for (int n = int.Parse(a[1]); n > 0; n--) Frame(); break;
                case "shot": Shot(a.Length > 1 ? a[1] : "shot"); break;
                case "reload": Note("map reload (warp to the same map)"); Start(); break;
                case "flag": flags[int.Parse(a[1])] = int.Parse(a[2]); break;
                case "item": Inventory[int.Parse(a[1])] = int.Parse(a[2]); Note($"inventory: item {a[1]} x{a[2]}"); break;
                case "kill": if (Dead.Add(int.Parse(a[1]))) Note($"generator {a[1]} killed by the player"); break; // session 10 (e3)
                case "bosskill": if (BossDefeated.Add(int.Parse(a[1]))) Note($"boss battle {a[1]} won by the player"); break; // session 11 (ret11)
                case "board": o.Board[(int.Parse(a[1]), int.Parse(a[2]))] = int.Parse(a[3]); Note($"AI board of generator {a[1]}: slot {a[2]} = {a[3]}"); break; // session 11 (chrai11)
                case "partbreak": if (o.PartBroken.Add(int.Parse(a[1]))) Note($"generator {a[1]}: part 1 broken"); break;
                case "hp": o.Hp[int.Parse(a[1])] = P(a[2]); Note($"generator {a[1]}: HP {a[2]} %"); break;
                default: throw new ArgumentException($"step '{raw}'");
            }
        }
    }
    static float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    void Shot(string name)
    {
        var prompts = helpers.Keys.Where(Shown).Select(h => $"\"{helpers[h].Text}\"").ToList();
        string where = unloaded ? $"WARPED ({warpTo})" : $"({player.X:0.##}, {player.Y:0.##}, {player.Z:0.##})";
        string states = string.Join(", ", machines.Select(m => $"{m.Group}:{(m.Ended ? "end" : m.State.ToString())}"
            + (m.Replays + m.Bypasses + m.Revisits > 0 ? $"[G2 replays {m.Replays}, bypasses {m.Bypasses}, revisits {m.Revisits}]" : "")));
        var info = new ShotInfo(name, frame, frame / o.Fps, prompts, unloaded, states);
        Shots.Add(info);
        Timeline.Add($"{T(frame)} shot {name}: prompt {info.Prompt}; player {where}; states {states}");
    }
    string T(int f) => $"{f / o.Fps,6:0.00}s f{f,-5}";
    void Note(string s) { Log.Add($"{T(frame)} {s}"); }

    void Frame()
    {
        frame++;
        if (unloaded) { pressPending = false; return; }
        // the player's input (a press acts on the prompt the screen showed)
        examined.Clear();
        if (pressPending)
        {
            pressPending = false;
            var on = shownLastFrame.Where(Shown).OrderBy(h => h).ToList();
            if (on.Count == 0) Note("RETURN: no prompt on screen (nothing happens)");
            else
            {
                examined.Add(on[0]);
                Presses.Add((frame, on[0]));
                Note($"RETURN on \"{helpers[on[0]].Text}\" (helper {on[0]}){(on.Count > 1 ? $" [also shown: {string.Join(", ", on.Skip(1))}]" : "")}");
            }
        }
        foreach (var m in machines.ToList())
        {
            if (m.Ended || unloaded) continue;
            cur = m;
            // status 6 (130002 last frame): destroyed and restarted, not stepped in this frame
            if (m.Rerun) { m.Restarts++; NewRun(m); Note($"group {m.Group}: re-executed (run {m.Seq})"); continue; }
            // the gate: FUN_140183620 (a record of this run for the registering path, once after +0x2f), else
            // FUN_140183cb0 (no registrations, or a registered slot holds)
            bool bypass = false;
            if (o.Records && m.Dirty)
            {
                m.Dirty = false;
                if (Recorded(m) is not null) { bypass = true; m.Bypasses++; if (o.Trace) Note($"group {m.Group} state {m.State}: gate bypass (record of run {m.Seq})"); }
            }
            if (!bypass && m.Slots.Count > 0)
            {
                m.Mask = 0;
                foreach (var s in m.Slots.Keys) if (SlotValue(s)) m.Mask |= 1 << s;
                if (m.Mask == 0) { m.Gated++; continue; }
            }
            m.Steps++;
            for (int it = 0; it < o.Iterations && !m.Ended; it++)
            {
                if (!groups[m.Group].TryGetValue(m.State, out var st)) { Note($"group {m.Group}: state {m.State} missing"); m.Ended = true; break; }
                var c = Find(st.Conditions);
                if (c == null) break; // nothing fires: later iterations would see the same
                foreach (var x in st.ExitCommands) Cmd(x);
                foreach (var x in c.PassCommands) Cmd(x);
                long next = c.TargetState ?? m.State;
                if (o.Trace) Note($"group {m.Group}: {m.State} → {next}");
                StateChange(m);
                m.State = next; m.Entered = frame;
                if (groups[m.Group].TryGetValue(next, out var ns))
                {
                    if (ns.Conditions.Count == 0 && ns.EntryCommands.Count == 0) { m.Ended = true; Note($"group {m.Group}: ended (terminal state {next})"); }
                    foreach (var x in ns.EntryCommands) { Cmd(x); if (m.Ended) break; }
                    if (o.Records && m.Path == next && Recorded(m) is not null) { m.Revisits++; Note($"group {m.Group}: G2 state {next} re-entered in run {m.Seq} (its first exit replays)"); }
                }
            }
        }
        cur = null;
        if (warpAt >= 0 && frame >= warpAt && !unloaded) { unloaded = true; LoadingFrame = frame; Note($"LOADING SCREEN: warp to {warpTo}; this map's events stop"); }
        shownLastFrame.Clear();
        foreach (var h in helpers.Keys) if (Shown(h)) shownLastFrame.Add(h);
    }

    /// <summary>FUN_14046e780 after a transition: record the registering state's exit (FUN_140183d40 → FUN_140184010; the
    /// first per path and run kept, an older run's overwritten), then clear the registrations (FUN_140183b60).</summary>
    void StateChange(Machine m)
    {
        if (o.Records && m.Slots.Count > 0 && m.Path is long p)
        {
            if (!(m.Records.TryGetValue(p, out var r) && r.Seq != 0xff && NotOlder(r.Seq, m.Seq)))
            {
                m.Records[p] = (m.Seq, m.Mask);
                m.Dirty = true;
                if (o.Trace) Note($"group {m.Group}: record state {p} run {m.Seq} mask {m.Mask:X}");
            }
        }
        m.Slots.Clear(); m.Mask = 0; m.Path = null;
    }

    /// <summary>The record of the current run for the registering path (FUN_1401836a0's lookup), or null.</summary>
    int? Recorded(Machine m) => m.Path is long p && m.Records.TryGetValue(p, out var r) && r.Seq != 0xff && NotOlder(r.Seq, m.Seq) ? r.Mask : null;

    ESD.Condition? Find(IEnumerable<ESD.Condition> conds)
    {
        foreach (var c in conds)
        {
            if (Eval(c.Evaluator) == 0) continue;
            if (c.TargetState != null || c.Subconditions.Count == 0) return c;
            if (Find(c.Subconditions) is { } sub) return sub;
        }
        return null;
    }

    bool SlotValue(int slot)
    {
        var list = cur!.Slots[slot];
        return slot < 8 ? list.Any(Cond) : list.All(Cond);
    }

    bool Cond(ESD.CommandCall c)
    {
        var a = c.Arguments.Select(Eval).ToArray();
        int A(int k) => k < a.Length ? (int)a[k] : 0;
        switch (c.CommandID)
        {
            case 140132: return InRegion(A(1)) == (A(3) != 0);
            case 140205: return examined.Contains(A(1));
            case 140301: return (flags.GetValueOrDefault(A(1)) != 0) == (A(2) != 0);
            case 140021: return true;
            case 140202: return Broken.Contains(A(1)) == (A(2) != 0);  // session 10: 【OBJ】破壊状態判定 (destroyed 1 / intact 0)
            case 140209: return Active(A(1)) == (A(2) != 0);           // session 10: 【OBJ】アクティブ判定 (130702 shown/hidden)
            case 140602: return !o.Guest == (A(1) != 0);               // session 10: EventConditionNet_IsHost mode 0 (net object assumed)
            case 140801: return o.Items.Contains(A(1)) || Inventory.GetValueOrDefault(A(1)) > 0;               // session 10 (K4/K6): holds the item (vanilla form (s, item, 1, 3, 1, 1, 0))
            case 140111: return Dead.Contains(A(1));                    // session 10 (e3): 「キャラ死亡判定」 the generator's character is dead
            case 140522: return BossDefeated.Contains(A(1));            // session 11 (ret11): 「ボス戦_終了判定」 the boss battle was won (step bosskill)
            case 140171: return (o.Board.GetValueOrDefault((A(1), A(2))) == A(3)) == (A(4) == 0);   // session 11 (chrai11): logic EzState flag (gen, slot, v, 0 = equal)
            case 140121:                                                // HP ratio (gen, percent, DS1 comparison 0 = … 5 <=)
            {
                double hp = o.Hp.GetValueOrDefault(A(1), 100), p = A(2);
                return A(3) switch { 0 => hp == p, 1 => hp != p, 2 => hp > p, 3 => hp < p, 4 => hp >= p, 5 => hp <= p, _ => false };
            }
            default: Unmodelled.Add($"group condition {c.CommandID}"); return false;
        }
    }

    void Cmd(ESD.CommandCall c)
    {
        var a = c.Arguments.Select(Eval).ToArray();
        int A(int k) => k < a.Length ? (int)a[k] : 0;
        var m = cur!;
        if (c.CommandID is >= 140000 and < 150000)
        {
            if (!m.Slots.TryGetValue(A(0), out var l)) m.Slots[A(0)] = l = [];
            l.Add(c);
            m.Path = m.State; m.Dirty = true; // FUN_1401834a0: the container's path = the registering state, +0x2f
            return;
        }
        switch (c.CommandID)
        {
            case 130101:
                if (flags.GetValueOrDefault(A(0)) != A(1)) Note($"group {m.Group} state {m.State}: flag {A(0)} = {A(1)}");
                flags[A(0)] = A(1);
                break;
            case 130131:
                for (int f = A(0); f <= A(1) && A(1) - A(0) < 100000; f++) flags[f] = A(2);
                Note($"group {m.Group}: flags {A(0)}..{A(1)} = {A(2)}");
                break;
            case 131638:
                if (kgDisabled.GetValueOrDefault(A(0)) != (A(1) != 0))
                    Note($"group {m.Group} state {m.State}: prompt {(A(1) != 0 ? "OFF" : "ON")} \"{(helpers.TryGetValue(A(0), out var h) ? h.Text : A(0).ToString())}\"");
                kgDisabled[A(0)] = A(1) != 0;
                break;
            case 132125:
                warpTo = $"map {A(2)} point {A(3)} (demos {A(0)}/{A(1)})";
                if (warpAt < 0) warpAt = frame + o.WarpFrames;
                Note($"group {m.Group} state {m.State}: 132125 warp requested → {warpTo}");
                break;
            case 130001: m.Ended = true; Note($"group {m.Group}: 130001 end"); break;
            case 130381: Note($"group {m.Group} state {m.State}: covenant {o.Covenant} → {A(0)}"); o.Covenant = A(0); break; // session 11 (cov11), rule K6
            case 130002: m.Rerun = true; if (o.Trace) Note($"group {m.Group} state {m.State}: 130002 re-execute (next frame)"); break;
            case 130702:
                if (!Objects.TryGetValue(A(0), out var ob) || ob.On != A(1)) Note($"group {m.Group} state {m.State}: object {A(0)} {(A(1) != 0 ? "shown" : "hidden")}");
                Objects[A(0)] = (A(1), frame);
                break;
            case 131411:
                if (Items.GetValueOrDefault(A(0), -1) != A(1)) Note($"group {m.Group} state {m.State}: item of object {A(0)} {(A(1) != 0 ? "enabled" : "disabled")}");
                Items[A(0)] = A(1);
                break;
            case 132221:
                if (Messages.GetValueOrDefault(A(0), -1) != A(1)) Note($"group {m.Group} state {m.State}: blood message {A(0)} {(A(1) != 0 ? "shown" : "hidden")}");
                Messages[A(0)] = A(1);
                break;
            case 130441:
                winFrom = frame; winTo = frame + o.WindowFrames;
                winAnswer = o.Answers.Count > 0 ? o.Answers.Dequeue() : 2;
                Windows.Add((A(0), winAnswer));
                Note($"group {m.Group} state {m.State}: YES/NO window {A(0)} → {(winAnswer switch { 1 => "YES", 2 => "NO", _ => "cancel" })}");
                break;
            case 131401: // session 10 (asy-items i2): consume
                Inventory[A(0)] = Math.Max(0, Inventory.GetValueOrDefault(A(0)) - A(1));
                Note($"group {m.Group} state {m.State}: 131401 consume item {A(0)} x{A(1)} (left {Inventory[A(0)]})");
                break;
            case 131402:
                Awards.Add(A(0));
                Note($"group {m.Group} state {m.State}: 131402 award lot {A(0)}");
                break;
            case 131636: // session 10: prefab state request
                Requests[A(0)] = (A(1), frame);
                RequestLog.Add((A(0), A(1), frame));
                Note($"group {m.Group} state {m.State}: object {A(0)} state {A(1)} requested");
                break;
            case 131613: // session 10: break every object with an instance id in [first, last] (handler 0x140464ea1; that it has a
                         // break component, i.e. a breakable model, is checked by `world-events PACK`)
                foreach (int inst in Objects.Keys.Concat(Requests.Keys).Concat([A(0), A(1)]).Distinct().Where(i => i >= A(0) && i <= A(1)))
                    if (Broken.Add(inst)) { BreakLog.Add((inst, frame)); Note($"group {m.Group} state {m.State}: object {inst} BROKEN"); }
                break;
            case 131652: // session 10: restore
                if (Broken.Remove(A(0))) Note($"group {m.Group} state {m.State}: object {A(0)} restored");
                break;
            case 130307:
                Note($"group {m.Group} state {m.State}: no fall damage once (130307)");
                break;
            case 130443: // session 10: dialogs are part of the checks (K6 fail text, K7 "Used %s")
                Dialogs.Add((a.Select(x => (int)x).ToArray(), frame));
                Note($"group {m.Group} state {m.State}: dialog 130443({string.Join(", ", a)})");
                break;
            case 131602: // session 10: a DS2 door lock (the door itself enforces it; not modelled)
                Note($"group {m.Group} state {m.State}: door lock 131602({string.Join(", ", a)})");
                break;
            case 131721: // session 10 (e3, rule N1): forced death (the character removed, counted as dead)
                ForcedDeaths.Add((A(0), frame));
                Dead.Add(A(0));
                Note($"group {m.Group} state {m.State}: 131721 forced death of generator {A(0)}");
                break;
            case 132211: // session 10 (e3, rule N1): souls to the player
                Souls += A(0);
                Note($"group {m.Group} state {m.State}: 132211 +{A(0)} souls");
                break;
            case 130317: // session 10 (e3, rule SP1): HP set to a percentage of the maximum
                HpSets.Add((A(0), a.Length > 1 ? a[1] : 0, frame));
                Note($"group {m.Group} state {m.State}: 130317 generator {A(0)} HP {(a.Length > 1 ? a[1] : 0):0.###} %");
                break;
            case 132201: case 132202: case 132204: // session 11 (ret11): boss battle start / defeat / gauge off
                BossLog.Add((c.CommandID, A(0), frame));
                Note($"group {m.Group} state {m.State}: {c.CommandID} boss battle {A(0)}");
                break;
            case 130613: case 131731:
                if (o.Trace) Note($"group {m.Group}: {c.CommandID}({string.Join(", ", a)})");
                break;
            case 131704: // session 11 (chrai11): generator start position / warp (rules CR15, EM1's leaps)
                Warps.Add((A(0), A(1), frame));
                Note($"group {m.Group} state {m.State}: 131704 generator {A(0)} warped to {A(1)}");
                break;
            case 131821: // session 11 (chrai11), rule HB1: bullet attached to the generator's character
                Bullets.Add((A(0), A(1), frame));
                Note($"group {m.Group} state {m.State}: 131821 bullet {A(1)} on generator {A(0)}");
                break;
            default: Unmodelled.Add($"command {c.CommandID}"); break;
        }
    }

    double Fn(int id, double[] a)
    {
        int A(int k) => k < a.Length ? (int)a[k] : 0;
        switch (id)
        {
            case 3: return (frame - cur!.Entered) / o.Fps;
            case 6: return flags.GetValueOrDefault(A(0)) != 0 ? 1 : 0;
            case 140001:
            {
                // FUN_140183c30 (live, ORed into the mask), then FUN_1401836a0 (this run's record first)
                var m = cur!;
                bool live = m.Slots.ContainsKey(A(0)) && SlotValue(A(0));
                if (live) m.Mask |= 1 << A(0);
                if (o.Records && Recorded(m) is int rec)
                {
                    bool r = (rec >> A(0) & 1) != 0;
                    if (r != live) { m.Replays++; if (o.Trace) Note($"group {m.Group} state {m.State}: f140001({A(0)}) replayed {(r ? 1 : 0)} (live {(live ? 1 : 0)})"); }
                    return r ? 1 : 0;
                }
                return (m.Mask >> A(0) & 1) != 0 ? 1 : 0;
            }
            case 132101: return warpAt >= 0 && frame >= warpAt ? 1 : 0;
            case 130422: return winFrom >= 0 && frame >= winFrom && frame < winTo ? 1 : 0;   // session 10: YES/NO window shown
            case 130421: return winTo >= 0 && frame >= winTo ? winAnswer : 0;                 // its result once closed
            case 131401: return Inventory.GetValueOrDefault(A(0)); // session 10 (asy-items i2): items held (f131401(item, 1, 1, 0))
            case 130602: return o.Guest ? 1 : 0; // session 10 (rule C1): guest user, 0 offline
            case 132241: return o.Cycle;         // session 10 (e3, rule G5): 「ゲーム周回数取得」 the playthrough in progress
            case 130361: return o.Covenant;    // session 11 (cov11): the player's DS2 covenant (rule K2)
            case 130601: return o.SessionHost && !o.Guest ? 1 : 0; // rule C1: host of a session, 0 offline (and for a guest)
            case 130661: return A(0) == 0 && o.SessionHost || o.Guest ? o.Members : 0; // rule C1: other players (0 offline)
            case 131653: // session 10: f131653(instance, state, 0) the prefab is in that state: the last request, or after a
            {            // play state (70+) ran AnimSeconds, any state below 70 (its rest)
                if (!Requests.TryGetValue(A(0), out var rq)) return A(1) == 10 ? 1 : 0;
                if (rq.State == A(1)) return 1;
                return rq.State >= 70 && A(1) < 70 && (frame - rq.Frame) / o.Fps >= o.AnimSeconds ? 1 : 0;
            }
            case 132201: return 0;
            case 130310: return o.PartBroken.Contains(A(0)) ? 1 : 0;   // session 11 (chrai11), rule TC1-E: 「部位破壊判定（ジェネレータ指定版）」
        }
        Unmodelled.Add($"function f{id}");
        return 0;
    }

    double Eval(byte[] b) => Evaluate(b, Fn);

    static double ConstArg(byte[] b) => Evaluate(b, (_, _) => 0);

    /// <summary>DS2's ESD expression evaluator: FUN_14098e040 calls (&amp;PTR_FUN_1415b2920)[opcode] for every byte up to
    /// A1 (the table is indexed by the raw opcode). 0x91 &lt;, 0x92 &gt;, 0x93 &lt;=, 0x94 &gt;= as track npctalk found (entry
    /// 0x94 = 0x1409908f0 ends in comisd/setae); entry 0xA6 (the byte after every function call) = 0x14098ee00, `ret 0`:
    /// a no-op (not "abort if false").</summary>
    public static double Evaluate(byte[] b, Func<int, double[], double> fn)
    {
        var st = new Stack<double>();
        var regs = new double[8];
        double Pop() => st.Count > 0 ? st.Pop() : 0;
        for (int i = 0; i < b.Length; i++)
        {
            byte op = b[i];
            if (op < 0x80) { st.Push(op - 0x40); continue; }
            switch (op)
            {
                case 0x80: st.Push(BitConverter.ToSingle(b, i + 1)); i += 4; break;
                case 0x81: st.Push(BitConverter.ToDouble(b, i + 1)); i += 8; break;
                case 0x82: st.Push(BitConverter.ToInt32(b, i + 1)); i += 4; break;
                case >= 0x84 and <= 0x8A:
                {
                    var args = new double[op - 0x84];
                    for (int k = args.Length - 1; k >= 0; k--) args[k] = Pop();
                    st.Push(fn((int)Pop(), args));
                    break;
                }
                case 0x8C: { double r = Pop(), l = Pop(); st.Push(l + r); break; }
                case 0x8E: { double r = Pop(), l = Pop(); st.Push(l - r); break; }
                case 0x8F: { double r = Pop(), l = Pop(); st.Push(l * r); break; }
                case 0x90: { double r = Pop(), l = Pop(); st.Push(r != 0 ? l / r : 0); break; }
                case 0x8D: st.Push(-Pop()); break;
                case >= 0x91 and <= 0x94:
                {
                    double r = Pop(), l = Pop();
                    st.Push(op switch { 0x91 => l < r, 0x92 => l > r, 0x93 => l <= r, _ => l >= r } ? 1 : 0);
                    break;
                }
                case 0x95: { double r = Pop(), l = Pop(); st.Push(l == r ? 1 : 0); break; }
                case 0x96: { double r = Pop(), l = Pop(); st.Push(l != r ? 1 : 0); break; }
                case 0x98: { double r = Pop(), l = Pop(); st.Push(l != 0 && r != 0 ? 1 : 0); break; }
                case 0x99: { double r = Pop(), l = Pop(); st.Push(l != 0 || r != 0 ? 1 : 0); break; }
                case 0xA1: return st.Count > 0 ? st.Pop() : 0;
                case 0xA5: { int j = i + 1; while (j + 1 < b.Length && (b[j] | b[j + 1]) != 0) j += 2; i = j + 1; st.Push(0); break; }
                case 0xA6: break; // DS2: a no-op (opcode table PTR_FUN_1415b2920[0xA6] = 0x14098ee00, `ret 0`)
                case >= 0xA7 and <= 0xAE: regs[op - 0xA7] = st.Count > 0 ? st.Peek() : 0; break;
                case >= 0xAF and <= 0xB6: st.Push(regs[op - 0xAF]); break;
                default: throw new NotSupportedException($"ESD opcode {op:X2} (not modelled)");
            }
        }
        return st.Count > 0 ? st.Pop() : 0;
    }

    // ------------------------------------------------------------------ lint (rules G1, G2)
    /// <summary>
    /// Rule G1 check over every state that registers group conditions: a transition that can be true while every
    /// registered slot is false, and that is not already decided in the state's entry iteration, can only fire in a frame
    /// where some other slot holds (the gate): with none, the event freezes there. Atoms: f140001 of a registered slot
    /// = 0; f3 = 0 (entry) or ∞ (later); flags and other functions both ways (up to 8 atoms). A condition after one that is
    /// always true at entry is unreachable and skipped.
    /// </summary>
    public static List<string> Lint(Dictionary<long, Dictionary<long, ESD.State>> esd, IEnumerable<long>? only = null)
    {
        var problems = new List<string>();
        foreach (var (gid, states) in esd.OrderBy(g => g.Key))
        {
            if (only != null && !only.Contains(gid)) continue;
            foreach (var (sid, st) in states.OrderBy(s => s.Key))
            {
                var regSlots = st.EntryCommands.Where(c => c.CommandID is >= 140000 and < 150000).Select(c => (int)ConstArg(c.Arguments[0])).ToHashSet();
                if (regSlots.Count == 0) continue;
                // session 11 (chrai11): a complementary 140171 pair (same generator, board slot and value; 0 = equal, 1 = not equal:
                // rules FA1 / TC1-G) always has one slot holding: the gate never closes
                if (st.EntryCommands.Where(c => c.CommandID == 140171 && c.Arguments.Count == 5).Select(c => c.Arguments.Skip(1).Select(ConstArg).ToArray())
                        .GroupBy(a => (a[0], a[1], a[2])).Any(g => g.Any(a => a[3] == 0) && g.Any(a => a[3] == 1))) continue;
                for (int k = 0; k < st.Conditions.Count; k++)
                {
                    var c = st.Conditions[k];
                    var gate = Gated(c.Evaluator);
                    if (gate == null) { problems.Add($"g{gid} s{sid} cond {k}: too many atoms to check"); continue; }
                    if (gate.Value.Gated)
                        problems.Add($"g{gid} s{sid} cond {k} → {c.TargetState}: {Commands.Esd.Expr(c.Evaluator)} can only pass while no registered slot ({string.Join(", ", regSlots.OrderBy(s => s))}) holds: never stepped (rule G1){(gate.Value.FlagWait ? " [flag/function wait]" : "")}");
                    if (gate.Value.AtEntry) break; // later conditions are unreachable at entry, and the state is left there
                }
            }
        }
        return problems;
    }

    /// <summary>
    /// Rule G2 check: a state that registers group conditions (entry 140xxx) and can be entered again within one run of
    /// its event (reachable from itself without passing a state whose entry ends the run: 130002 re-execute or 130001
    /// end) replays its first exit there (FUN_1401836a0 returns the recorded slot bits, FUN_140183620 steps it once).
    /// Returns one line per state.
    /// </summary>
    public static List<string> Revisits(Dictionary<long, Dictionary<long, ESD.State>> esd, IEnumerable<long>? only = null)
    {
        var problems = new List<string>();
        static IEnumerable<long> Targets(IEnumerable<ESD.Condition> cs) => cs.SelectMany(c => (c.TargetState is long t ? [t] : Array.Empty<long>()).Concat(Targets(c.Subconditions)));
        static bool EndsRun(ESD.State s) => s.EntryCommands.Any(c => c.CommandBank == 1 && c.CommandID is 130001 or 130002);
        foreach (var (gid, states) in esd.OrderBy(g => g.Key))
        {
            if (only != null && !only.Contains(gid)) continue;
            foreach (var (sid, st) in states.OrderBy(s => s.Key))
            {
                if (!st.EntryCommands.Any(c => c.CommandBank == 1 && c.CommandID is >= 140000 and < 150000)) continue;
                var seen = new HashSet<long>();
                var todo = new Stack<long>(Targets(st.Conditions));
                var via = new List<long>();
                bool back = false;
                while (todo.Count > 0 && !back)
                {
                    long s = todo.Pop();
                    if (s == sid) { back = true; break; }
                    if (!seen.Add(s) || !states.TryGetValue(s, out var ns) || EndsRun(ns)) continue;
                    foreach (long t in Targets(ns.Conditions)) todo.Push(t);
                }
                if (back) problems.Add($"g{gid} s{sid}: registers group conditions and is re-entered within one run (no 130002 on the way back): DS2 replays its first exit there (rule G2)");
            }
        }
        return problems;
    }

    /// <summary>
    /// Rule G1 for one condition of a state that registers group conditions: Gated = it can be true while every slot
    /// read (f140001) is 0, and is not already decided in the entry iteration (state time 0) for every value of its other
    /// atoms (flags, functions; f3 = 0 at entry, ∞ later), or it is a flag/function wait (true for some atom values,
    /// false for others, with every slot 0); AtEntry = always true in the entry iteration (a test's `if 1`). Null when the
    /// condition has more than 8 such atoms.
    /// </summary>
    public static (bool Gated, bool FlagWait, bool AtEntry)? Gated(byte[] evaluator)
    {
        var atoms = new List<(int Fn, int Arg)>();
        Evaluate(evaluator, (f, a) => { var key = (f, a.Length > 0 ? (int)a[0] : 0); if (f != 140001 && f != 3 && !atoms.Contains(key)) atoms.Add(key); return 0; });
        if (atoms.Count > 8) return null;
        bool later = false, atEntry = true, someTrue = false, someFalse = false;
        for (int mask = 0; mask < 1 << atoms.Count; mask++)
        {
            double Val(int f, double[] a, bool late)
            {
                if (f == 140001) return 0; // every registered slot false (the gated case)
                if (f == 3) return late ? 1e9 : 0;
                int i = atoms.IndexOf((f, a.Length > 0 ? (int)a[0] : 0));
                return i >= 0 && (mask >> i & 1) != 0 ? 1 : 0;
            }
            bool e = Evaluate(evaluator, (f, a) => Val(f, a, false)) != 0;
            bool l = Evaluate(evaluator, (f, a) => Val(f, a, true)) != 0;
            if (!e) atEntry = false;
            if (l && !e) later = true;
            if (l) someTrue = true; else someFalse = true;
        }
        bool flagWait = atoms.Count > 0 && someTrue && someFalse;
        return (later || flagWait, flagWait && !later, atEntry);
    }
}
