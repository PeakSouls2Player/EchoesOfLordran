using System.Globalization;
using System.Text;
using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Build.Talk;

/// <summary>
/// Offline frame simulator for DS2 talk ESDs (session 9, track npctalk): runs one talk group of a built pack frame by
/// frame with DS2's own state-machine semantics in a scripted world (NPC HP, player distance over time, talk button,
/// lines that end after N frames, dialog and NPC-window answers, hits, deaths, flags) and reports what happens.
///
/// DS2's runtime (decompile + disassembly, DarkSoulsII.exe 1.0.3; status V-code = read from code, not yet seen in game):
///  - the holder's per-frame update calls machine->Update(100) (FUN_140b1f490 → vtable 0x14116c1d8 +0x30 =
///    FUN_1409859c0): up to 100 iterations per frame, and the loop STOPS at the first iteration that makes no transition
///    (0x140985c42 `test bpl,bpl; je` exit; FUN_1409856c0 at +0x28 is the variant without that stop, run with 1 for
///    sub-machines). Each iteration: the first condition of the current state whose evaluator is non-zero (in order; a
///    condition without a target but with subconditions tries those; a leaf without a target re-enters the current
///    state: FUN_1409923b0) → the state's exit commands, the condition's pass commands (bank 1 dispatched like entry
///    commands: FUN_1409925d0, FUN_140995af0 case 0), the target's entry commands (while the machine has no return value,
///    +0xb0 = 0x7fffffff); then the current state's while commands (FUN_140992720); then stop if there was no transition.
///    So chained transitions run within one frame, while commands run once per transition plus once on the stopping
///    iteration, and a cycle of N instantaneous transitions runs floor(100 / N) times per frame (Oscar's 7-state cycle in
///    run s9-2 → 14 key-guide creations per frame, the loader trace).
///  - state 0's entry commands run on a machine's first update only when it was built with the start flag (+0x18):
///    the talk holder builds its main machine with 0 (FUN_1404909d0 → FUN_140b1f230 → FUN_1409883a0(…, 0) →
///    FUN_140984cb0), so a talk's state-0 entry never runs; machines called from a state (FUN_140985150) get 1.
///  - comparison opcodes: 0x91 &lt;, 0x92 &gt;, 0x93 &lt;=, 0x94 &gt;= (evaluator table PTR_FUN_1415b2920; DSR the same);
///    0xA6 is a no-op (handler 0x14098ee00 = `ret 0`; DSR 0x140e9c0c0 the same); 0xB7 = if the top is 0 the result is 0
///    and evaluation stops (handler 0x14098f420 resets the stack to one int 0 and clears the instruction pointer), else
///    the value stays (DSR 0x140e9c6c0 the same).
///  - key guide (130408 → FUN_140470bc0; per-frame test FUN_140453ce0): inside the KeyGuideParam shape around the owner's
///    dummy poly dummyPolyId (else the character's matrix), and (angleType 2) the player's forward within angleDeg of the
///    direction to the guide (angleType 0 / 1: of the guide's +Z / −Z), and the player's position within posAngleDeg of
///    the guide's +Z (FUN_140001680 = atan2(|a×b|, a·b)). Row 600: 80 / 150 → no prompt in the 60° wedge behind the guide.
///  - f130311(n) = owner record byte +0x74+n (0 any attacker, 1 the player: FUN_140416d60 counts one per attack), 130351
///    zeroes both (FUN_140457c90 case 0x1fd2f); records start at 0 (FUN_14040f0f0).
/// Modelled (assumptions, U): f3() (state time) = frames since the state was entered × frame time, so 0 in the frame a
/// state is entered; f132002() = no line playing; f132003() = the player pressed the button while inside the prompt
/// region (KeyGuideParam width_radius of the row 130408 names, and its angle tests with the player at
/// <see cref="Options.Bearing"/> from the guide's +Z, facing <see cref="Options.Facing"/> off the guide; row 600 and our
/// copies have dummyPolyId −1, so the guide is the NPC's own matrix, whose +Z is its facing per the npcplace track —
/// for the key guide still U), kept until 130407/132001/132003 consume it; a line
/// (NpcTalkParam row) lasts LineFrames, bNextPlay continues with the next row in table order; dialogs/windows appear
/// the frame after they are opened and are answered AnswerFrames later from the scenario's queues.
/// </summary>
public sealed class TalkSim
{
    public sealed class Options
    {
        public double Fps = 60;
        public int Iterations = 100;
        public int LineFrames = 120;
        public int AnswerFrames = 45;
        public int ItemFrames = 60;
        public int Hp = 1320;
        public int Gender;
        /// <summary>f130311 counts at the machine start (a fresh map load: 0).</summary>
        public int StartCount;
        /// <summary>130351 zeroes the counts (decompile). False = a "stuck counter" what-if.</summary>
        public bool ResetWorks = true;
        /// <summary>Evaluate 0x91-0x94 with the old (wrong) table our tools used before session 9.</summary>
        public bool LegacyOps;
        public double DefaultRadius = 1.8;
        public readonly Dictionary<int, double> KeyGuideRadius = [];
        /// <summary>KeyGuideParam angleType / angleDeg / posAngleDeg per row (FUN_140453ce0).</summary>
        public readonly Dictionary<int, (int Type, int Deg, int PosDeg)> KeyGuideAngles = [];
        /// <summary>The player's position as an angle from the key guide's +Z (degrees; 0 = in front, 180 = behind).</summary>
        public double Bearing;
        /// <summary>The player's forward relative to the direction to the guide (degrees; 0 = facing it).</summary>
        public double Facing;
        public readonly List<(int Id, int Msg, bool Next)> TalkRows = [];
        public readonly Dictionary<int, string> Lines = [];
        public readonly Dictionary<int, List<(int Item, int Num)>> Lots = [];
        public readonly Dictionary<int, int> Inventory = [];
        public readonly Dictionary<int, int> Flags = [];
        /// <summary>Session 9 (track npcflags): the DS2 map loaded while the talk runs; flags without DS2 storage there
        /// (Events.Ds2FlagStore) read 0 and drop writes, as in game. Null = every id stores (the old model).</summary>
        public string? FlagMap;
        /// <summary>Session 11 (cov11): the player's DS2 covenant at the start (f130361), DS2 VowParam level thresholds per id
        /// (f130362), the contribution and global event variables at the start (130382 / 130122-130124).</summary>
        public int Covenant;
        public readonly Dictionary<int, int[]> VowThresholds = [];
        public readonly Dictionary<int, int> Contribution = [];
        public readonly Dictionary<int, int> Vars = [];
    }

    /// <summary>A built pack's talk world for one map: talk ESD, NpcTalkParam rows, talk FMG, KeyGuideParam radii, lots.</summary>
    public static (ESD Esd, Options Template) LoadPack(string pack, string map, ParamStore? reg = null)
    {
        string Gd(string p) => Path.Combine(pack, "gamedata", p.Replace('/', Path.DirectorySeparatorChar));
        var esd = ESD.Read(Gd($"ezstate/talk_{map}.esd"));
        var o = new Options { FlagMap = map };
        var defs = Directory.GetFiles(Path.Combine(ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        string tp = Gd($"param/npctalkparam_{map}.param");
        if (File.Exists(tp))
        {
            try
            {
                var p = PARAM.Read(tp);
                if (p.ApplyParamdefCarefully(defs))
                    foreach (var r in p.Rows.OrderBy(r => r.ID))
                        o.TalkRows.Add((r.ID, Convert.ToInt32(r["msgId"].Value), Convert.ToInt32(r["bNextPlay"].Value) != 0));
            }
            catch (EndOfStreamException) { }
        }
        string fp = Gd($"menu/text/english/talk/{map}.fmg");
        if (File.Exists(fp)) foreach (var e in FMG.Read(fp).Entries) if (e.Text != null) o.Lines[e.ID] = e.Text;
        reg ??= ParamStore.LoadDs2(Path.Combine(pack, "title"), Commands.ExtractDs2.DefaultOut);
        if (reg.Params.GetValueOrDefault("KeyGuideParam") is { } kg)
            foreach (var r in kg.Rows)
            {
                o.KeyGuideRadius[r.ID] = Convert.ToDouble(r["width_radius"].Value, CultureInfo.InvariantCulture);
                o.KeyGuideAngles[r.ID] = (Convert.ToInt32(r["angleType"].Value), Convert.ToInt32(r["angleDeg"].Value), Convert.ToInt32(r["posAngleDeg"].Value));
            }
        VowThresholds(o, reg); // session 11 (cov11)
        if (reg.Params.GetValueOrDefault("ItemLotParam2_Other") is { } lots)
            foreach (var r in lots.Rows.Where(r => r.ID >= 1_000_000_000))
            {
                var items = new List<(int, int)>();
                for (int i = 1; i <= 10; i++)
                    if (Convert.ToInt32(r[$"itemNum{i}"].Value) > 0) items.Add((Convert.ToInt32(r[$"itemId{i}"].Value), Convert.ToInt32(r[$"itemNum{i}"].Value)));
                o.Lots[r.ID] = items;
            }
        return (esd, o);
    }

    /// <summary>Session 11 (cov11): DS2's VowParam rank thresholds (Lv1-Lv3 per covenant id) for f130362.</summary>
    public static void VowThresholds(Options o, ParamStore reg)
    {
        if (reg.Params.GetValueOrDefault("VowParam") is not { } vow) return;
        foreach (var r in vow.Rows)
            o.VowThresholds[r.ID] = [Convert.ToInt32(r["requiredContributionLv1"].Value), Convert.ToInt32(r["requiredContributionLv2"].Value), Convert.ToInt32(r["requiredContributionLv3"].Value)];
    }

    // ------------------------------------------------------------------ results
    public sealed record LogEntry(int Frame, string Kind, string Text);

    public sealed class Result
    {
        public string Scenario = "";
        public int Frames;
        public int MaxTransitionsInFrame;
        /// <summary>Frames in which the machine kept cycling (a state entered 3+ times, or transitions up to the cap).</summary>
        public int CycleFrames;
        /// <summary>Frames in which a state was entered twice and the machine then stopped (went round once).</summary>
        public int RevisitFrames;
        /// <summary>130351 at the machine's start (frame 1), not a reaction.</summary>
        public int StartResets;
        /// <summary>130351 after the start; <see cref="Acks"/> counts the frames with any (= hit reactions).</summary>
        public int Resets;
        public string FirstCycle = "";
        public int PromptCreates, PromptDeletes;
        /// <summary>Longest run of frames with the prompt region up and untouched (no create/delete in the frame) while the
        /// player stands in it.</summary>
        public int LongestPromptHold;
        public int PromptHoldAtEnd;
        public int Acks;
        public int Hits;
        public readonly List<LogEntry> Log = [];
        public readonly List<string> Lines = [];
        public readonly List<string> FlagChanges = [];
        public readonly List<string> Items = [];
        public readonly List<string> Dialogs = [];
        /// <summary>Session 11 (cov11): covenant events (joins, contribution, DS1 counters, items taken) and the end state.</summary>
        public readonly List<string> Covenant = [];
        public string CovenantEnd = "";
        public readonly SortedSet<string> Unmodelled = [];
        public bool Ended;
        public bool TimedOut;
        public long FinalState;
    }

    // ------------------------------------------------------------------ the machine and the world
    readonly IReadOnlyDictionary<long, ESD.State> g;
    readonly Options o;
    readonly Result res = new();
    long cur;
    int enteredFrame;
    int frame;
    bool ended;
    // world
    double dist = 10, distTarget = 10, distStep;
    int distFramesLeft;
    int hp;
    readonly int[] count = new int[2];
    readonly Dictionary<int, int> flags;
    readonly Dictionary<int, int> inventory;
    bool keyGuide;
    int keyGuideRow;
    bool pressPending, talkRequested;
    int talkRequestedAt;
    int lineIndex = -1, lineLeft;
    bool yesNoOpen, yesNoShown; int yesNoAt, yesNoResult;
    bool okOpen, okShown; int okAt;
    bool winOpen, winShown; int winAt, winResult;
    int itemFrom = -1, itemUntil = -1;
    readonly Queue<int> answers = new(), menus = new();
    int covenant;
    readonly Dictionary<int, int> contribution, vars;
    // per-frame bookkeeping
    int createsThisFrame, deletesThisFrame;
    int holdRun;
    int lastTransitionFrame;
    int lastResetFrame = -1;

    TalkSim(IReadOnlyDictionary<long, ESD.State> group, Options options)
    {
        g = group; o = options;
        hp = o.Hp;
        count[0] = count[1] = o.StartCount;
        flags = new(o.Flags);
        inventory = new(o.Inventory);
        covenant = o.Covenant;
        contribution = new(o.Contribution);
        vars = new(o.Vars);
    }

    /// <summary>Runs a scenario script (see <see cref="Scripts"/>) on one talk group.</summary>
    public static Result Run(IReadOnlyDictionary<long, ESD.State> group, Options options, string scenario, string script, bool trace = false)
    {
        var sim = new TalkSim(group, options);
        sim.res.Scenario = scenario;
        sim.trace = trace;
        sim.Start();
        foreach (var raw in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (sim.ended) break;
            var w = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            double D(int k) => double.Parse(w[k], CultureInfo.InvariantCulture);
            int I(int k) => int.Parse(w[k], CultureInfo.InvariantCulture);
            switch (w[0])
            {
                case "dist":
                    if (w.Length >= 4 && w[2] == "over") { sim.distTarget = D(1); sim.distFramesLeft = I(3); sim.distStep = (sim.distTarget - sim.dist) / Math.Max(1, I(3)); sim.Advance(I(3)); }
                    else { sim.dist = sim.distTarget = D(1); sim.distFramesLeft = 0; }
                    break;
                case "hold": sim.Advance(I(1)); break;
                case "press": sim.pressPending = true; sim.Advance(1); break;
                case "hit": sim.count[0] = Math.Min(255, sim.count[0] + 1); sim.count[1] = Math.Min(255, sim.count[1] + 1); sim.res.Hits++; sim.Note("hit", $"player hits the NPC (counts {sim.count[0]}/{sim.count[1]})"); sim.Advance(1); break;
                case "kill": sim.hp = 0; sim.Note("kill", "NPC HP → 0"); sim.Advance(1); break;
                case "hp": sim.hp = I(1); break;
                case "flag": sim.SetFlag(I(1), I(2), "scenario"); break;
                case "answer": foreach (var a in w[1].Split(',')) sim.answers.Enqueue(a switch { "yes" => 1, "no" => 2, _ => 3 }); break;
                case "menu": foreach (var m in w[1].Split(',')) sim.menus.Enqueue(m switch { "talk" => 16, "leave" => 17, "end" => 18, "pledge" => 5, "offer" => 6, _ => 19 }); break;
                // session 11 (cov11): the player's DS2 covenant, an item count, a global event variable
                case "covenant": sim.covenant = I(1); sim.Note("covenant", $"player covenant set to {I(1)}"); break;
                case "item": sim.inventory[I(1)] = I(2); break;
                case "var": sim.vars[I(1)] = I(2); break;
                case "until-idle": sim.UntilIdle(w.Length > 1 ? I(1) : 3000); break;
                case "reload": sim.Reload(); break; // session 9 (npcflags): a map reload; persistent flags keep their DS2 storage, T1 clears the temporary lists
                default: throw new ArgumentException($"talk-sim: unknown scenario step '{raw}'");
            }
        }
        sim.res.Frames = sim.frame;
        sim.res.Ended = sim.ended;
        sim.res.FinalState = sim.cur;
        sim.res.PromptHoldAtEnd = sim.holdRun;
        sim.res.CovenantEnd = $"covenant {sim.covenant} (level {sim.Level(sim.covenant)}), contribution {string.Join(" ", sim.contribution.Where(k => k.Value != 0).OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"))}, variables {string.Join(" ", sim.vars.Where(k => k.Value != 0).OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"))}, inventory {string.Join(" ", sim.inventory.Where(k => k.Value != 0).OrderBy(k => k.Key).Select(k => $"{k.Key}x{k.Value}"))}";
        return sim.res;
    }

    bool trace;

    /// <summary>Built-in scenarios ({near} = the player's distance at the NPC).</summary>
    public static readonly Dictionary<string, string> Scripts = new()
    {
        ["rest"] = "dist 10; hold 30; dist {near} over 30; hold 300",
        ["far"] = "dist 10; hold 120",
        ["talk"] = "answer yes,yes,yes,yes; menu talk,leave,talk,leave,leave; dist 10; hold 30; dist {near} over 30; hold 120; press; until-idle 6000; "
                 + "dist 10 over 30; hold 60; dist {near} over 30; hold 120; press; until-idle 6000; hold 120",
        ["talk-no"] = "answer no,no,no,no; menu talk,leave,talk,leave,leave; dist 10; hold 30; dist {near} over 30; hold 120; press; until-idle 6000; "
                 + "dist 10 over 30; hold 60; dist {near} over 30; hold 120; press; until-idle 6000; hold 120",
        ["hit"] = "dist 10; hold 30; dist {near} over 30; hold 120; hit; until-idle 3000; hold 240",
        ["hit2"] = "dist 10; hold 30; dist {near} over 30; hold 120; hit; until-idle 3000; hold 120; hit; until-idle 3000; hold 240",
        ["death"] = "dist 10; hold 30; dist {near} over 30; hold 120; kill; hold 600",
        // session 9 (track npcflags): first talk, second talk in the same load, map reload, third talk
        // session 11 (cov11): covenant scenarios ({pledge} = the DS2 covenant id the talk joins, {votive} = the item its
        // offering takes, {other} = another DS2 covenant; TalkSim.Placeholders). cov-join: join from no covenant (the
        // first talk may ask first: YES to all); cov-member: already a member (DS1's "Already belong"); cov-switch: from
        // another covenant (DS2 drops it); cov-offer: a member with 12 offerings offers 11 times (DS1's rewards at 10, DS2's
        // rank by VowParam)
        ["cov-join"] = Yes(12) + "menu pledge,leave,leave,leave,leave,leave,leave; dist 10; hold 30; " + Rounds(6) + "hold 120",
        ["cov-member"] = "covenant {pledge}; " + Yes(12) + "menu pledge,leave,leave,leave,leave,leave,leave; dist 10; hold 30; " + Rounds(6) + "hold 120",
        ["cov-switch"] = "covenant {other}; " + Yes(12) + "menu pledge,leave,leave,leave,leave,leave,leave; dist 10; hold 30; " + Rounds(6) + "hold 120",
        ["cov-offer"] = "covenant {pledge}; item {votive} 12; answer " + string.Join(",", Enumerable.Repeat("yes", 30)) + "; menu " + string.Join(",", Enumerable.Repeat("offer", 11)) + ",leave,leave,leave; "
                 + "dist 10; hold 30; dist {near} over 30; hold 120; press; until-idle 20000; hold 120",
        ["talk-reload"] = "answer yes,yes,yes,yes,yes,yes; menu talk,leave,talk,leave,talk,leave,leave; dist 10; hold 30; dist {near} over 30; hold 120; press; until-idle 6000; "
                 + "dist 10 over 30; hold 60; dist {near} over 30; hold 120; press; until-idle 6000; hold 120; "
                 + "reload; dist 10; hold 30; dist {near} over 30; hold 120; press; until-idle 6000; hold 120",
    };

    /// <summary>Session 11 (cov11): n talk visits (walk off, come back, press, wait until idle); YES to the next n questions.</summary>
    static string Rounds(int n) => string.Concat(Enumerable.Repeat("dist 10 over 30; hold 60; dist {near} over 30; hold 120; press; until-idle 6000; ", n));
    static string Yes(int n) => "answer " + string.Join(",", Enumerable.Repeat("yes", n)) + "; ";

    /// <summary>Session 11 (cov11): values for the covenant scenarios' placeholders, read from the group: {pledge} = the
    /// first 130381 id &gt; 0 (the join), {votive} = the first item a 131401 command takes, {other} = a DS2 covenant id
    /// that is neither 0 nor {pledge}. Absent: 0.</summary>
    public static Dictionary<string, string> Placeholders(IReadOnlyDictionary<long, ESD.State> group)
    {
        int Const(byte[] b) { try { return TalkExpr.Parse(b) is TalkExpr.Num n ? n.V : 0; } catch (InvalidDataException) { return 0; } }
        var cmds = group.OrderBy(k => k.Key).SelectMany(k => k.Value.EntryCommands).Where(c => c.CommandBank == 1).ToList();
        int pledge = cmds.Where(c => c.CommandID == 130381 && c.Arguments.Count > 0).Select(c => Const(c.Arguments[0])).FirstOrDefault(v => v > 0);
        int votive = cmds.Where(c => c.CommandID == 131401 && c.Arguments.Count > 0).Select(c => Const(c.Arguments[0])).FirstOrDefault(v => v > 0);
        int other = pledge == 8 ? 9 : 8; // Company of Champions (no DS1 covenant maps to it), else Pilgrims of Dark
        return new() { ["{pledge}"] = pledge.ToString(), ["{votive}"] = votive.ToString(), ["{other}"] = other.ToString() };
    }

    void Reload()
    {
        // fix pass (npcflags): the map's translated event 0 clears its DS1 maps' temporary lists at load (rule T1)
        int cleared = 0;
        if (o.FlagMap != null)
            foreach (var (ds1Map, host) in Events.FlagRules.Host)
                if (host == o.FlagMap)
                    foreach (int f in Events.FlagRules.TemporaryIds(ds1Map))
                        if (flags.Remove(f, out int v) && v != 0) { cleared++; res.FlagChanges.Add($"@{frame} {f}=0 (reload: event 0 T1)"); }
        Note("reload", $"map reload: the talk machine starts again at state 0; persistent flags keep what DS2 stored, event 0 (T1) cleared {cleared} temporary flags");
        cur = 0; enteredFrame = frame; ended = false;
        keyGuide = false; pressPending = talkRequested = false; lineIndex = -1; lineLeft = 0;
        yesNoOpen = yesNoShown = okOpen = okShown = winOpen = winShown = false;
        itemFrom = itemUntil = -1; count[0] = count[1] = 0; hp = o.Hp; dist = distTarget = 10; distFramesLeft = 0;
        Start();
    }

    // ------------------------------------------------------------------ runtime
    void Start()
    {
        cur = 0;
        enteredFrame = 0;
        // The talk holder builds its main machine with the start flag off (FUN_140b1f230 → FUN_1409883a0(…, 0)), so
        // FUN_1409859c0 never runs state 0's entry commands for it (called machines, built with 1, would).
        if (g.TryGetValue(0, out var s0) && s0.EntryCommands.Count > 0) res.Unmodelled.Add("state 0 has entry commands (not run: a talk's main machine starts with the start flag off)");
    }

    void Advance(int frames)
    {
        for (int k = 0; k < frames && !ended; k++) Frame();
    }

    void UntilIdle(int max)
    {
        int start = frame;
        while (!ended && frame - start < max)
        {
            Frame();
            bool busy = lineIndex >= 0 || yesNoOpen || okOpen || winOpen || (itemUntil >= frame);
            if (!busy && frame - lastTransitionFrame >= 30 && frame - start >= 30) return;
        }
        if (!ended) { res.TimedOut = true; Note("timeout", $"until-idle: still busy after {max} frames (state {cur})"); }
    }

    bool InRegion => keyGuide && dist <= Radius(keyGuideRow) + 1e-6 && AnglesOk(keyGuideRow);
    double Radius(int row) => o.KeyGuideRadius.TryGetValue(row, out double r) ? r : o.DefaultRadius;

    /// <summary>FUN_140453ce0's two angle tests (V-code), in degrees relative to the guide's +Z.</summary>
    bool AnglesOk(int row)
    {
        if (!o.KeyGuideAngles.TryGetValue(row, out var a)) return true;
        static double Off(double x) { x %= 360; if (x < 0) x += 360; return x > 180 ? 360 - x : x; }
        double toGuide = o.Bearing + 180, forward = toGuide + o.Facing;
        double dir = a.Type switch { 0 => 0, 1 => 180, _ => toGuide };
        return Off(forward - dir) <= a.Deg + 1e-6 && Off(o.Bearing) <= a.PosDeg + 1e-6;
    }

    void Frame()
    {
        frame++;
        // world first (the game updates characters, then events)
        if (distFramesLeft > 0) { dist += distStep; if (--distFramesLeft == 0) dist = distTarget; }
        if (pressPending)
        {
            pressPending = false;
            if (InRegion) { talkRequested = true; talkRequestedAt = frame; Note("press", "talk button in the prompt region"); }
            else Note("press", keyGuide ? $"talk button outside the prompt region ({dist:0.00} m)" : "talk button with no prompt");
        }
        if (talkRequested && frame - talkRequestedAt > 30) talkRequested = false;
        if (lineIndex >= 0 && --lineLeft <= 0)
        {
            var row = o.TalkRows[lineIndex];
            if (row.Next && lineIndex + 1 < o.TalkRows.Count) { lineIndex++; lineLeft = o.LineFrames; LogLine(); }
            else lineIndex = -1;
        }
        if (yesNoOpen && !yesNoShown && frame > yesNoAt) yesNoShown = true;
        if (yesNoShown && frame >= yesNoAt + o.AnswerFrames)
        {
            yesNoResult = answers.Count > 0 ? answers.Dequeue() : 3;
            yesNoShown = false; yesNoOpen = false;
            res.Dialogs.Add($"@{frame} YES/NO answered {(yesNoResult switch { 1 => "yes", 2 => "no", _ => "cancel" })}");
        }
        if (okOpen && !okShown && frame > okAt) okShown = true;
        if (okShown && frame >= okAt + o.AnswerFrames) { okShown = false; okOpen = false; res.Dialogs.Add($"@{frame} OK window closed"); }
        if (winOpen && !winShown && frame > winAt) winShown = true;
        if (winShown && winResult == 0 && frame >= winAt + o.AnswerFrames)
        {
            winResult = menus.Count > 0 ? menus.Dequeue() : 19;
            res.Dialogs.Add($"@{frame} NPC window result {winResult} ({(winResult switch { 16 => "Talk", 17 or 18 => "Leave", 5 => "pledge (covenant)", 6 => "votive (offer)", _ => "backed out" })})");
        }

        // the machine: Update(100)
        createsThisFrame = deletesThisFrame = 0;
        bool hadRegion = keyGuide;
        var entered = new List<long>();
        int transitions = 0;
        for (int it = 0; it < o.Iterations && !ended; it++)
        {
            if (!g.TryGetValue(cur, out var st)) { res.Unmodelled.Add($"state {cur} missing"); ended = true; break; }
            var c = Find(st.Conditions);
            if (c != null)
            {
                foreach (var x in st.ExitCommands) Run(x);
                foreach (var x in c.PassCommands) Run(x);
                long next = c.TargetState ?? cur;
                if (trace) Note("go", $"{cur} → {next}");
                cur = next;
                enteredFrame = frame;
                entered.Add(next);
                transitions++;
                lastTransitionFrame = frame;
                if (g.TryGetValue(next, out var ns)) foreach (var x in ns.EntryCommands) { Run(x); if (ended) break; }
            }
            if (!ended && g.TryGetValue(cur, out var cs)) foreach (var x in cs.WhileCommands) Run(x);
            // FUN_1409859c0 leaves the loop at the first iteration without a transition (after that iteration's while
            // commands); only a machine that keeps transitioning reaches the 100-iteration cap
            if (c == null) break;
        }
        res.MaxTransitionsInFrame = Math.Max(res.MaxTransitionsInFrame, transitions);
        // a within-frame cycle: the machine keeps going round (a state entered 3+ times, or still moving at the cap);
        // once round and stopping (a reset on the way ends it) is a revisit
        bool spin = entered.GroupBy(s => s).Any(x => x.Count() >= 3) || transitions >= o.Iterations;
        if (!spin && entered.Count != entered.Distinct().Count()) res.RevisitFrames++;
        if (spin)
        {
            res.CycleFrames++;
            if (res.FirstCycle.Length == 0)
            {
                var seq = entered.Take(24).Select(s => s.ToString());
                res.FirstCycle = $"frame {frame}: {transitions} transitions, {string.Join("→", seq)}{(entered.Count > 24 ? "→…" : "")}";
            }
        }
        // the prompt is held when the region stayed up through the frame untouched and the player stands in it
        bool held = hadRegion && keyGuide && createsThisFrame == 0 && deletesThisFrame == 0 && InRegion;
        holdRun = held ? holdRun + 1 : 0;
        res.LongestPromptHold = Math.Max(res.LongestPromptHold, holdRun);
    }

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

    // ------------------------------------------------------------------ commands
    void Run(ESD.CommandCall c)
    {
        var a = c.Arguments.Select(Eval).ToArray();
        int bank = c.CommandBank, id = c.CommandID;
        if (bank == 5)
        {
            if (a.Length == 0 || a[0] == 0) return;
            a = a[1..];
        }
        else if (bank != 1) { res.Unmodelled.Add($"command bank {bank}:{id}"); return; }
        int A(int k) => k < a.Length ? (int)a[k] : 0;
        switch (id)
        {
            case 130001: ended = true; Note("end", "130001 end"); break;
            case 130101: SetFlag(A(0), A(1), $"state {cur}"); break;
            case 130351:
                if (o.ResetWorks) count[0] = count[1] = 0;
                if (frame <= 1) res.StartResets++;
                else
                {
                    res.Resets++;
                    if (lastResetFrame != frame) res.Acks++; // one reaction per frame (DS1 f5 then f9 in the same frame)
                    lastResetFrame = frame;
                }
                Note("reset", $"130351 hit count reset (state {cur})");
                break;
            case 130407:
                if (keyGuide) { deletesThisFrame++; res.PromptDeletes++; }
                keyGuide = false; talkRequested = false;
                break;
            case 130408:
                createsThisFrame++; res.PromptCreates++;
                keyGuide = true; keyGuideRow = A(1);
                break;
            case 132001:
            {
                int row = A(0);
                int idx = o.TalkRows.FindIndex(r => r.Id == row);
                talkRequested = false;
                if (idx < 0) { Note("line", $"132001({row}): no NpcTalkParam row"); res.Lines.Add($"@{frame} {row} (missing row)"); break; }
                lineIndex = idx; lineLeft = o.LineFrames;
                LogLine();
                break;
            }
            case 132002: if (lineIndex >= 0) Note("line", "132002 line cancelled"); lineIndex = -1; break;
            case 132003: talkRequested = false; break;
            case 131402:
            {
                int lot = A(0);
                var items = o.Lots.GetValueOrDefault(lot) ?? [];
                foreach (var (item, num) in items) inventory[item] = inventory.GetValueOrDefault(item) + num;
                res.Items.Add($"@{frame} lot {lot}: {(items.Count == 0 ? "?" : string.Join(", ", items.Select(x => $"{x.Item} x{x.Num}")))}");
                itemFrom = frame + 1; itemUntil = frame + o.ItemFrames;
                break;
            }
            case 130441: yesNoOpen = true; yesNoShown = false; yesNoAt = frame; yesNoResult = 0; res.Dialogs.Add($"@{frame} YES/NO window mapevent {A(0)}"); break;
            case 130442: yesNoOpen = yesNoShown = false; break;
            case 130443: okOpen = true; okShown = false; okAt = frame; res.Dialogs.Add($"@{frame} OK window mapevent {A(0)}"); break;
            case 130444: okOpen = okShown = false; break;
            case 130461: winOpen = true; winShown = false; winAt = frame; winResult = 0; res.Dialogs.Add($"@{frame} NPC window NpcEventParam {A(3)}"); break;
            case 130462: winOpen = winShown = false; break;
            case 130463: winResult = 0; break;
            // session 11 (cov11): covenants (DS2: 130381 join, 130382 contribution only for the current id, FUN_140203af0)
            case 130381: res.Covenant.Add($"@{frame} join {A(0)} (was {covenant})"); covenant = A(0); break;
            case 130382:
                if (A(0) == covenant) { contribution[A(0)] = contribution.GetValueOrDefault(A(0)) + A(1); res.Covenant.Add($"@{frame} contribution {A(0)} +{A(1)} = {contribution[A(0)]} (level {Level(A(0))})"); }
                else res.Covenant.Add($"@{frame} contribution {A(0)} +{A(1)} ignored (covenant {covenant})");
                break;
            case 130122: vars[A(0)] = A(1); res.Covenant.Add($"@{frame} variable {A(0)} = {A(1)}"); break;
            case 130124: vars[A(0)] = vars.GetValueOrDefault(A(0)) + A(1); res.Covenant.Add($"@{frame} variable {A(0)} +{A(1)} = {vars[A(0)]}"); break;
            case 131401: inventory[A(0)] = Math.Max(0, inventory.GetValueOrDefault(A(0)) - A(1)); res.Covenant.Add($"@{frame} item {A(0)} -{A(1)} (left {inventory[A(0)]})"); break;
            default: res.Unmodelled.Add($"command {id}"); break;
        }
    }

    /// <summary>DS2's covenant level from the contribution and VowParam (FUN_140204270).</summary>
    int Level(int id) => o.VowThresholds.TryGetValue(id, out var t) ? t.Count(x => contribution.GetValueOrDefault(id) >= x) : 0;

    void SetFlag(int f, int v, string by)
    {
        // session 9 (track npcflags): DS2 drops a write to a flag without storage (Events.Ds2FlagStore, FUN_1404750b0)
        if (o.FlagMap != null && Events.Ds2FlagStore.Lookup(f, [o.FlagMap]) == Events.Ds2FlagStore.Kind.None)
        {
            if (v != 0) res.FlagChanges.Add($"@{frame} {f}={v} DROPPED: no DS2 storage ({by})");
            return;
        }
        int old = flags.GetValueOrDefault(f);
        flags[f] = v;
        if (old != v) res.FlagChanges.Add($"@{frame} {f}={v} ({by})");
    }

    void LogLine()
    {
        var r = o.TalkRows[lineIndex];
        string text = o.Lines.TryGetValue(r.Msg, out var t) ? Formats.Ds1Fmgs.OneLine(t) : "(no text)";
        if (text.Length > 70) text = text[..70] + "…";
        res.Lines.Add($"@{frame} {r.Id} \"{text}\"");
        Note("line", $"{r.Id} {text}");
    }

    void Note(string kind, string text) => res.Log.Add(new LogEntry(frame, kind, text));

    // ------------------------------------------------------------------ functions
    double Fn(int id, double[] a)
    {
        int A(int k) => k < a.Length ? (int)a[k] : 0;
        switch (id)
        {
            case 3: return (frame - enteredFrame) / o.Fps;
            case 6: return flags.GetValueOrDefault(A(0)) != 0 ? 1 : 0;
            case 130201: return dist;
            case 130301: return hp;
            case 130302: return o.Hp > 0 ? Math.Clamp(hp * 100.0 / o.Hp, 0, 100) : 0;
            case 130305: return o.Gender;
            case 130311: return A(0) is 0 or 1 ? count[A(0)] : 0;
            case 130412: return frame >= itemFrom && frame <= itemUntil ? 1 : 0;
            case 130421: return yesNoResult;
            case 130422: return yesNoShown ? 1 : 0;
            case 130431: return okShown ? 1 : 0;
            case 130451: return winShown ? 1 : 0;
            case 130452: return winResult;
            case 130453: return 0;
            case 130602: return 0;
            case 131002: return 0;
            case 131401: return inventory.GetValueOrDefault(A(0));
            case 130361: return covenant;                         // session 11 (cov11)
            case 130362: return Level(A(0));
            case 130122: return vars.GetValueOrDefault(A(0));
            case 130661: return 0;                                // other players: offline
            case 132002: return lineIndex < 0 ? 1 : 0;
            case 132003: return talkRequested ? 1 : 0;
        }
        res.Unmodelled.Add($"function f{id}");
        return 0;
    }

    // ------------------------------------------------------------------ expressions (DS2's evaluator)
    double Eval(byte[] b)
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
                    st.Push(Fn((int)Pop(), args));
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
                    bool v = (o.LegacyOps ? op switch { 0x91 => 0x93, 0x92 => 0x94, 0x93 => 0x91, _ => 0x92 } : op) switch
                    {
                        0x91 => l < r, 0x92 => l > r, 0x93 => l <= r, _ => l >= r,
                    };
                    st.Push(v ? 1 : 0);
                    break;
                }
                case 0x95: { double r = Pop(), l = Pop(); st.Push(l == r ? 1 : 0); break; }
                case 0x96: { double r = Pop(), l = Pop(); st.Push(l != r ? 1 : 0); break; }
                case 0x98: { double r = Pop(), l = Pop(); st.Push(l != 0 && r != 0 ? 1 : 0); break; }
                case 0x99: { double r = Pop(), l = Pop(); st.Push(l != 0 || r != 0 ? 1 : 0); break; }
                case 0xA1: return st.Count > 0 ? st.Pop() : 0;
                case 0xA5: { int j = i + 1; while (j + 1 < b.Length && (b[j] | b[j + 1]) != 0) j += 2; i = j + 1; st.Push(0); break; }
                case 0xA6: break; // DS2 handler 0x14098ee00 is a bare `ret 0` (DSR 0x140e9c0c0 too): a marker, no effect
                case >= 0xA7 and <= 0xAE: regs[op - 0xA7] = st.Count > 0 ? st.Peek() : 0; break;
                case >= 0xAF and <= 0xB6: st.Push(regs[op - 0xAF]); break;
                // 0xB7 (handler 0x14098f420): top == 0 → the stack becomes one int 0 and the instruction pointer is
                // cleared (result 0); else the value stays on the stack
                case 0xB7: if (st.Count > 0 && st.Peek() == 0) return 0; break;
                default: res.Unmodelled.Add($"opcode {op:X2}"); return 0;
            }
        }
        return st.Count > 0 ? st.Pop() : 0;
    }

    // ------------------------------------------------------------------ checks (esd-check)
    /// <summary>Where the player stands to talk: 1.2 m inside DS2's 1.8 m talk region; a region widened for a DS1 talk
    /// dummy (Anastacia's 3.65 m) → its edge minus 1.75 m (the dummy's spot).</summary>
    public static double NearFor(IReadOnlyDictionary<long, ESD.State> group, Options o)
    {
        double r = group.Values.SelectMany(s => s.EntryCommands).Where(c => c.CommandID == 130408 && c.Arguments.Count > 1)
            .Select(c => TalkExpr.Parse(c.Arguments[1]) is TalkExpr.Num n && o.KeyGuideRadius.TryGetValue(n.V, out double rr) ? rr : o.DefaultRadius)
            .DefaultIfEmpty(o.DefaultRadius).Max();
        return r > 2.5 ? r - 1.75 : 1.2;
    }

    /// <summary>
    /// The standard runs of one talk group (esd-check): "rest" (approach to the NPC, stand 300 frames), "far", one "hit"
    /// at the NPC, a "death" and a full "talk" (YES answers). Problems: a within-frame cycle in any run; at rest a prompt
    /// that isn't held for the last 240 frames, or a hit reaction (count reset after the start) with nobody hitting; more
    /// hit reactions than hits. The rest goes into <paramref name="info"/>.
    /// </summary>
    public static List<string> Check(long gid, IReadOnlyDictionary<long, ESD.State> group, Options o, List<string> info)
    {
        var bad = new List<string>();
        string near = NearFor(group, o).ToString("0.##", CultureInfo.InvariantCulture);
        bool prompt = TalkTranslator.PromptReach(group).Prompt;
        foreach (var name in new[] { "rest", "far", "hit", "death", "talk" })
        {
            var r = Run(group, o, name, Scripts[name].Replace("{near}", near));
            info.Add($"talk group {gid} {Summary(r)}");
            if (r.CycleFrames > 0) bad.Add($"talk group {gid} {name}: within-frame cycle in {r.CycleFrames} frame(s) ({r.FirstCycle})");
            if (name == "rest" && prompt && r.PromptHoldAtEnd < 240) bad.Add($"talk group {gid} rest: the prompt is not held at {near} m (held {r.PromptHoldAtEnd} of the last 300 frames, created {r.PromptCreates}×)");
            if (name is "rest" or "far" && r.Acks > 0) bad.Add($"talk group {gid} {name}: {r.Acks} hit reaction(s) with nobody hitting");
            if (r.Hits > 0 && r.Acks > r.Hits) bad.Add($"talk group {gid} {name}: {r.Acks} hit reactions for {r.Hits} hit(s)");
            foreach (var u in r.Unmodelled) info.Add($"talk group {gid} {name}: not modelled: {u}");
        }
        return bad;
    }

    // ------------------------------------------------------------------ report
    public static string Summary(Result r)
    {
        var sb = new StringBuilder();
        sb.Append($"{r.Scenario}: {r.Frames} frames, max {r.MaxTransitionsInFrame} transitions/frame, within-frame cycles in {r.CycleFrames} frame(s)");
        if (r.FirstCycle.Length > 0) sb.Append($" [first: {r.FirstCycle}]");
        if (r.RevisitFrames > 0) sb.Append($", {r.RevisitFrames} frame(s) went round once");
        sb.Append($"; prompt created {r.PromptCreates}× deleted {r.PromptDeletes}×, longest hold {r.LongestPromptHold} frames, held at end {r.PromptHoldAtEnd}");
        sb.Append($"; hits {r.Hits}, hit reactions {r.Acks} (frames with a count reset after the start; {r.Resets} resets); lines {r.Lines.Count}, items {r.Items.Count}, flags changed {r.FlagChanges.Count}");
        if (r.Ended) sb.Append("; machine ended");
        if (r.TimedOut) sb.Append("; TIMED OUT waiting for idle");
        sb.Append($"; final state {r.FinalState}");
        return sb.ToString();
    }
}
