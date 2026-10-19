using SoulsFormats;
using static Lordran.Tools.Build.Ai.Boss.Ds1AiInterpreter;

namespace Lordran.Tools.Build.Ai.Boss;

/// <summary>
/// DS1 battle goal (decision trees from <see cref="Ds1AiInterpreter"/>) → DS2 lower-logic AI ESD (session 8, track boss).
/// DS2's AI ESDs are DS1's AI framework in EzState form (DS2-AI-NOTES.md): an upper logic (perception, mode dispatch,
/// start-up) calls lower-logic mode groups, the lower logic thinks in machines that call each other (6:id(args), result
/// ?B9), and the queries DS1's Lua makes have DS2 functions with the same meaning. The translator keeps the template
/// boss's DS2 parts (upper logic, mode protocol, DS2's own attack/step machines) and generates the DS1 parts:
/// <list type="bullet">
/// <item>the think machine (DS1 battle goal _Activate: the if/elseif ladder, act weights, Common_Battle_Activate's weighted
/// choice) <see cref="Battle"/>; one machine per act (DS1 MyAct_n, with the act's GetWellSpace odds and the after-act call at
/// its end); the after-act machine; the interrupt handler (DS1 _Interupt) called from every waiting state when one of the
/// interrupts it tests fires;</item>
/// <item>DS1 rolls (GetRandam) at the point DS1 rolls them (1:1(slot, lo, hi), floats ×100), DS1's tests as conditions in
/// DS1's order (ESD tests conditions top to bottom, first match wins: the ladder's first-match semantics).</item>
/// </list>
/// Query rules (vanilla evidence in data\sandbox\boss\research\ai-survey.txt; P unless noted):
/// <list type="bullet">
/// <item>GetDist(TARGET_ENE_0) → f7001() (every DS2 AI, attack-range bands); GetDistYSigned → f7007() (bands -2..1.5);</item>
/// <item>GetRandam_Int(a,b) → 1:1(slot, a, b) / f1(slot) (DS2's own weighted mode choice uses exactly this);</item>
/// <item>GetNumber/SetNumber(i) → f5074(i) / 1:5750(i, v) (DS2's per-AI counters: 1:5750(0, f5074(0) + 1));</item>
/// <item>IsInsideTargetRegion(TARGET_ENE_0, region) → f7070(EventLocation, 0) (Ruin Sentinels ai325000: f7070 = the target
/// in a box, f7075 = itself: they climb their ledge (2147483616) when f7070(ledge box) and not f7075, jump down
/// (2147483615) in the opposite case);</item>
/// <item>IsInterupt(n) → f5050(n) (the same numbering as DS1's INTERUPT_*: 2/3 damaged, 7 guard begin, 12 item use, 13/14
/// battle area, 17 rebound: e.g. the Smelter strafes on f5050(7) and drops its spacing on 2/3/17); in the handler the
/// interrupt that triggered it is the machine parameter p0;</item>
/// <item>IsInsideObserve(slot) of AddObserveChrDmySphere(slot, target, dummy, r): DS2 has no dummy-poly sphere query we can
/// name, so the sphere is tested from the target's distance, bearing and side (f7001, f7002, f7003(0, 179)) against the
/// DS1 dummy's position in the model (U: f7003's side convention; f7001 taken as the capsule-surface distance like DS1's
/// GetDist, whose attack bands start at 0 m on a 2.5 m-radius demon);</item>
/// <item>native goals: CommonAttack → DS2's own attack machine (template 2147483646(action, range, angle, 0, stop1..3, t):
/// request, combo-stage stop odds, abort rules) after DS1's turn phase (while 1:5903 until f7002() &lt; face angle or the
/// turn time); a DS1 combo chain (ComboAttack → ComboRepeat → ComboFinal) is one DS2 combo action, a prefix of a longer
/// chain stops at its last stage (stop odds 101 = always); MoveToSomewhere on the target → walk 1:5505 / run 1:5535 (the
/// template's approach commands) until f7001() ≤ range or the life; on POINT_INITIAL / POINT_EVENT → 1:7005(point, -1,
/// 100, 0, 0) (the Sentinels' move-to-point) until f7072(point, range) == 0; Wait → state time; Turn → 1:5903 until the
/// face angle (DS1 default 10°); SidewayMove → 1:5565(dir, …) (the template's strafe); SpinStep → the template's step
/// machine 2147483645.</item>
/// </list>
/// Fix pass (review, session 8): the generated machines are compacted into DS2's shape (<see cref="Compact"/>: decision
/// chains into one state, identical states shared; proved equivalent per machine) and the header is the template's.
/// </summary>
public sealed class Ds2AiEsd
{
    public interface IContext
    {
        /// <summary>DS1 attack (EzState) id → DS2 action number, or null.</summary>
        int? SingleAction(int ds1Attack);
        /// <summary>A DS1 combo chain (or a prefix of one) → (DS2 combo action, stages of that combo row), or null.</summary>
        (int Action, int Stages)? Chain(IReadOnlyList<int> ds1Attacks);
        /// <summary>DS1 region entity → DS2 EventLocation id, or null.</summary>
        int? Location(int ds1Region);
        /// <summary>POINT_INITIAL: the character's home (DS1 2004:13 Set Character Home region) as an EventLocation.</summary>
        int? HomePoint { get; }
        /// <summary>DS1 dummy poly → (lateral offset, offset behind the centre) in the model, metres.</summary>
        (double Lateral, double Behind)? Dummy(int id);
        double SelfRadius { get; }
        double TargetRadius { get; }
        /// <summary>Session 11 (G10): the DS1 side-walk speed (m/s) of the character's own a00_0202 / a00_0203 root motion
        /// (SidewayMove swept-angle exit), ≤ 0 = unknown.</summary>
        double SideWalkSpeed => -1;
        /// <summary>Session 11 (track chrai11), rule EM1: the TAE message windows (board code, start s, end s) of a DS1 clip whose
        /// messages the map's events wait for (<see cref="EventMessages.Windows"/>; empty: none).</summary>
        IReadOnlyList<(int Code, double Start, double End)> Messages(int ds1Clip) => [];
        /// <summary>Session 11 (track chrai11), rule FA1: the character's event-forced clips and request code (null: none).</summary>
        EventMessages.Fa1Layout? Fa1 => null;
    }

    public const long Think = 2147483600, InterruptHandler = 2147483599, After = 2147483598;
    public static long ActMachine(int act) => 2147483598 - act;
    /// <summary>Template library machines (the Smelter's lower logic ai305001): attack request, back step, mode sync.</summary>
    public const long LibAttack = 2147483646, LibStep = 2147483645, LibSync = 2147483642, LibInit = 2147483643;

    readonly IContext ctx;
    readonly List<string> report;
    readonly HashSet<int> interrupts = [];
    readonly Dictionary<int, (int Target, int Dummy, double Radius)> observes = [];

    Ds2AiEsd(IContext ctx, List<string> report) { this.ctx = ctx; this.report = report; }

    /// <summary>Session 11 (dchr11), rule AI-TM: DS1 timer index → DS2 timer slot (Ds2AiMachines.TimerSlotOrder of the template lower: the
    /// slots it resets least first) and the seconds of the goal script's SetTimer(index, t) (IsFinishTimer(i) ⇔ f2(slot) ≥ t; GoalCompiler's rule).</summary>
    readonly Dictionary<int, int> timerSlot = [];
    readonly Dictionary<int, double> timerSec = [];

    void Timers(ESD template, Ds1AiInterpreter lib, string goal)
    {
        var order = Ds2AiMachines.TimerSlotOrder(template);
        var src = lib.Sources.FirstOrDefault(s => s.Text.Contains($"\"{goal}\"", StringComparison.Ordinal));
        if (src.Text == null) return;
        foreach (System.Text.RegularExpressions.Match mt in System.Text.RegularExpressions.Regex.Matches(src.Text, @"ai:SetTimer\(\s*(\d+)\s*,\s*([0-9.]+)\s*\)"))
        {
            int i = int.Parse(mt.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            double t = double.Parse(mt.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (timerSec.TryGetValue(i, out double old) && old != t) report.Add($"rule AI-TM: DS1 timer {i} set to {old} and {t} s: {Math.Max(old, t)} s used");
            timerSec[i] = Math.Max(t, timerSec.GetValueOrDefault(i));
        }
        foreach (int i in timerSec.Keys.Order())
        {
            if (timerSlot.Count >= order.Count) { report.Add($"rule AI-TM: DS1 timer {i}: no DS2 timer slot left (never finished)"); continue; }
            timerSlot[i] = order[timerSlot.Count].Slot;
            report.Add($"rule AI-TM: DS1 timer {i} (SetTimer {timerSec[i]} s) → DS2 timer slot {timerSlot[i]} (the template lower resets it {order[timerSlot.Count - 1].Resets}×)");
        }
    }

    /// <summary>A report line once (the think's paths repeat the same goals).</summary>
    void Once(string line) { if (!report.Contains(line)) report.Add(line); }

    static void Collect(SExpr e, HashSet<int> into)
    {
        switch (e)
        {
            case SInterrupt i: into.Add(i.Id); break;
            case SBin bin: Collect(bin.L, into); Collect(bin.R, into); break;
            case SNot n: Collect(n.E, into); break;
        }
    }

    /// <summary>Mode group 20 as the template's, without its anger-mode test: board 1 = 45 (mode step done), board 2 = 38,
    /// sync (2147483642), think, again. Session 11 (chrai11, rule FA1): a character with event-forced clips thinks through its
    /// dispatcher (<see cref="Fa1Machine"/>).</summary>
    static Dictionary<long, ESD.State> BattleMode(long think = Think) => new()
    {
        [0] = S([], (Ez.True, 3)),
        [1] = S([Ez.Cmd(5300, 1, 45), Ez.Cmd(5300, 2, 38)], (Ez.True, 3)),
        [3] = Call(LibSync, [], 4),
        [4] = Call(think, [], 1),
    };

    // ================================================================== session 11 (track chrai11): rules EM1 / FA1
    /// <summary>Rule EM1 message machines (one per DS1 clip) from this id down; the FA1 dispatcher at <see cref="Fa1Machine"/>.
    /// Below the act machines (<see cref="ActMachine"/>, ≥ 2147483578), above AI-SPLIT's ids (min − 1).</summary>
    public const long MsgMachineBase = 2147483540, Fa1Machine = 2147483560;
    readonly Dictionary<int, long> msgMachines = [];
    readonly Dictionary<long, Dictionary<long, ESD.State>> extraMachines = [];

    /// <summary>
    /// Rule EM1: an attack whose DS1 clip sends TAE messages the events wait for runs its own copy of the template's attack request
    /// (LibAttack states 1-2: 1:5232, wait f5000; 1:5204(action, 0), abort on f5010) and then writes each message's board code
    /// (<see cref="EventMessages.Slot"/>) from its start to its end (times from the request: the clip starts with it, P) and 0
    /// after, before waiting for the action's end (f5001() != 1, checked only from 0.5 s on, as LibAttack). A chain's clips keep
    /// LibAttack (reported).
    /// </summary>
    long MsgMachine(int action, int clip, IReadOnlyList<(int Code, double Start, double End)> wins)
    {
        if (msgMachines.TryGetValue(clip, out long id)) return id;
        id = MsgMachineBase - msgMachines.Count;
        msgMachines[clip] = id;
        var w = wins.OrderBy(x => x.Start).ToList();
        for (int i = 1; i < w.Count; i++)
            if (w[i].Start < w[i - 1].End) { report.Add($"rule EM1: a00_{clip:D4} message windows overlap (code {w[i - 1].Code} until {w[i - 1].End:0.###} s, code {w[i].Code} from {w[i].Start:0.###} s): the second starts when the first ends"); w[i] = (w[i].Code, w[i - 1].End, Math.Max(w[i].End, w[i - 1].End + EventMessages.MinHold)); }
        var st = new Dictionary<long, ESD.State>();
        long next = 1;
        long New() => next++;
        long s1 = New(), s2 = New(), ret = New(), abort = New(), clr = New();
        var wi = w.Select(_ => New()).ToList(); var ci = w.Select(_ => New()).ToList();
        static byte[] Done(double minTime) => minTime > 0
            ? Ez.End(Ez.And(Ez.Cmp(Ez.Fn(5001), Ez.Ne, Ez.I(1)), Ez.Cmp(Ez.Fn(3), Ez.Ge, Ez.Num(Math.Round(minTime, 3)))))
            : Ez.End(Ez.Cmp(Ez.Fn(5001), Ez.Ne, Ez.I(1)));
        bool noAct = action < 0;   // rule EM1-n: the messages only (no clip to request)
        byte[] AtStart(double t) => Ez.End(Ez.Cmp(Ez.Fn(3), Ez.Ge, Ez.Num(Math.Round(t, 3))));
        st[0] = S([], (Ez.True, s1));
        st[s1] = noAct ? S([], (AtStart(w[0].Start), wi[0]))
            : S([Ez.Cmd(5232)], (Ez.End(Ez.Cmp(Ez.Fn(5000), Ez.Ne, Ez.I(0))), s2), (Ez.End(Ez.Cmp(Ez.Fn(3), Ez.Gt, Ez.I(5))), ret));
        if (!noAct) st[s2] = S([Ez.Cmd(5204, action, 0)], (Ez.End(Ez.Cmp(Ez.Fn(5010), Ez.Ne, Ez.I(0))), abort), (AtStart(w[0].Start), wi[0]));
        for (int i = 0; i < w.Count; i++)
        {
            // the action's end only counts from 0.5 s after the request (LibAttack state 2 → 3)
            var wConds = noAct ? new List<(byte[], long)>() : new List<(byte[], long)> { (Done(0.5 - w[i].Start), clr) };
            wConds.Add((AtStart(w[i].End - w[i].Start), ci[i]));
            st[wi[i]] = S([Ez.Cmd(5300, EventMessages.Slot, w[i].Code)], [.. wConds]);
            if (i + 1 < w.Count)
                st[ci[i]] = noAct ? S([Ez.Cmd(5300, EventMessages.Slot, 0)], (AtStart(Math.Max(0, w[i + 1].Start - w[i].End)), wi[i + 1]))
                    : S([Ez.Cmd(5300, EventMessages.Slot, 0)], (Done(0.5 - w[i].End), ret), (AtStart(Math.Max(0, w[i + 1].Start - w[i].End)), wi[i + 1]));
            else
                st[ci[i]] = S([Ez.Cmd(5300, EventMessages.Slot, 0)], (noAct ? Ez.True : Done(0.5 - w[i].End), ret));
        }
        st[clr] = S([Ez.Cmd(5300, EventMessages.Slot, 0)], (Ez.True, ret));
        st[abort] = S([Ez.Cmd(5208), Ez.Cmd(5300, EventMessages.Slot, 0)], (Ez.End(Ez.Cmp(Ez.Fn(5001), Ez.Ne, Ez.I(1))), ret));
        var r = new ESD.State(); var rc = NoTarget(Ez.True); rc.PassCommands.Add(new ESD.CommandCall(7, -1, Ez.End(Ez.I(0)))); r.Conditions.Add(rc);
        st[ret] = r;
        extraMachines[id] = st;
        report.Add($"rule EM1: action {action} (DS1 a00_{clip:D4}) → message machine {id}: {string.Join(", ", w.Select(x => $"board {EventMessages.Slot} = {x.Code} {x.Start:0.###}-{x.End:0.###} s"))}");
        return id;
    }

    /// <summary>Rule FA1: J = Σ 2^k · (f5903(base + k) ≠ 0), the request code on the generator's logicEventFlag bits.</summary>
    static byte[] Fa1Code(EventMessages.Fa1Layout l)
    {
        int bas = l.Bits == 4 ? 0 : 1;
        byte[] sum = Ez.I(0);
        for (int k = 0; k < l.Bits; k++) sum = [.. sum, .. Ez.Mul(Ez.I(1 << k), Ez.Cmp(Ez.Fn(5903, Ez.I(bas + k)), Ez.Ne, Ez.I(0))), 0x8C];
        return sum;
    }

    /// <summary>
    /// Rule FA1: the dispatcher the battle mode calls instead of the think: a forced clip whose code is J and not yet acknowledged
    /// (board <see cref="EventMessages.AckSlot"/>) is acknowledged (board = its code) and played as its action (the EM1 message
    /// machine when it sends messages, else the template's attack request); an acknowledged clip code or the AI-off code holds
    /// still (0.25 s, DS1's AI off: the character only plays what the events force); a stale acknowledgement is cleared first;
    /// otherwise the DS1 think runs (with the E-REQ pairs decoded from J, <see cref="Expr"/>).
    /// </summary>
    long Fa1Dispatcher(EventMessages.Fa1Layout l)
    {
        var st = new Dictionary<long, ESD.State>();
        long next = 1;
        long New() => next++;
        long decide = New(), rst = New(), idle = New(), think = New(), ret = New();
        byte[] j = Fa1Code(l), ack = Ez.Fn(5900, Ez.I(EventMessages.AckSlot));
        var conds = new List<(byte[], long)> { (Ez.End(Ez.And(Ez.Cmp(ack, Ez.Ne, Ez.I(0)), Ez.Cmp(j, Ez.Ne, ack))), rst) };
        foreach (int c in l.Clips)
        {
            int code = l.ClipCode[c], action = l.Category[c];
            long a = New(), p = New();
            conds.Add((Ez.End(Ez.And(Ez.Cmp(j, Ez.Eq, Ez.I(code)), Ez.Cmp(ack, Ez.Ne, Ez.I(code)))), a));
            st[a] = S([Ez.Cmd(5300, EventMessages.AckSlot, code)], (Ez.True, p));
            var w = ctx.Messages(c);
            long done = New();   // the clip played: the acknowledgement ends with it (a later request of the same clip plays again)
            st[p] = w.Count > 0 ? Call(MsgMachine(action, c, w), [], done)
                : Call(LibAttack, [Ez.End(Ez.I(action)), Ez.End(Ez.I(999)), Ez.End(Ez.I(180)), Ez.End(Ez.I(0)), Ez.End(Ez.I(0)), Ez.End(Ez.I(0)), Ez.End(Ez.I(0)), Ez.End(Ez.F(0.5f))], done);
            st[done] = S([Ez.Cmd(5300, EventMessages.AckSlot, 0)], (Ez.True, ret));
        }
        int lo = l.ClipCode.Values.Min(), hi = l.ClipCode.Values.Max();
        conds.Add((Ez.End(Ez.And(Ez.Cmp(j, Ez.Ge, Ez.I(lo)), Ez.Cmp(j, Ez.Le, Ez.I(hi)))), idle));
        if (l.AiOffCode > 0) conds.Add((Ez.End(Ez.Cmp(j, Ez.Eq, Ez.I(l.AiOffCode))), idle));
        conds.Add((Ez.True, think));
        st[0] = S([], (Ez.True, decide));
        st[decide] = S([], [.. conds]);
        st[rst] = S([Ez.Cmd(5300, EventMessages.AckSlot, 0)], (Ez.True, decide));
        st[idle] = S([], (Ez.End(Ez.Cmp(Ez.Fn(3), Ez.Ge, Ez.F(0.25f))), ret));
        st[think] = Call(Think, [], ret);
        var r = new ESD.State(); var rc = NoTarget(Ez.True); rc.PassCommands.Add(new ESD.CommandCall(7, -1, Ez.End(Ez.I(0)))); r.Conditions.Add(rc);
        st[ret] = r;
        extraMachines[Fa1Machine] = st;
        report.Add($"rule FA1: dispatcher {Fa1Machine} on J = Σ 2^k·f5903({(l.Bits == 4 ? 0 : 1)}+k) ({l.Bits} bits): {string.Join(", ", l.Clips.Select(c => $"{l.ClipCode[c]} → action {l.Category[c]} (DS1 a00_{c:D4})"))}{(l.AiOffCode > 0 ? $", {l.AiOffCode} = AI off" : "")}{(l.Pairs.Count > 0 ? $", E-REQ {string.Join(", ", l.Pairs.Select(p => $"{l.PairCode[p]} = slot {p.Slot} {p.V}"))}" : "")}; ack board {EventMessages.AckSlot}");
        return Fa1Machine;
    }

    // ================================================================== trees
    sealed class Node
    {
        public List<Effect> Effects = [];
        public SExpr? Cond;
        public Node? T, F;
        public V? Return;
        public bool Leaf;
    }

    static Node Tree(List<RunPath> paths)
    {
        var root = new Node();
        foreach (var p in paths)
        {
            var n = root; int i = 0;
            foreach (var e in p.Stream)
            {
                if (e is EDecision d)
                {
                    if (n.Cond == null) n.Cond = d.Cond;
                    else if (n.Cond.ToString() != d.Cond.ToString()) throw new InvalidDataException($"paths diverge inconsistently at {d.Cond}");
                    n = d.Value ? n.T ??= new Node() : n.F ??= new Node();
                    i = 0;
                    continue;
                }
                if (i < n.Effects.Count) { i++; continue; } // a shared prefix (runs are deterministic up to their next decision)
                if (n.Cond != null) throw new InvalidDataException("effect after a decision in a shared node");
                n.Effects.Add(e); i++;
            }
            n.Leaf = true; n.Return = p.Return;
        }
        return root;
    }

    // ================================================================== machine compile
    sealed class M
    {
        public readonly Dictionary<long, ESD.State> States = [];
        public long Next = 1;
        public readonly Dictionary<int, int> Slots = [];
        public int NextSlot = 1;
        public HashSet<int> Read = [];
        public int? EventTarget;
        public bool InHandler, ReturnTrue;
        public string Name = "";
        public long Add(ESD.State s) { long id = Next++; States[id] = s; return id; }
    }

    Dictionary<long, ESD.State> Machine(List<RunPath> paths, string name, bool waitIdle = false, bool inHandler = false, bool returnTrue = false)
    {
        var m = new M { InHandler = inHandler, ReturnTrue = returnTrue, Name = name };
        foreach (var p in paths)
            foreach (var e in p.Stream)
            {
                if (e is EDecision d) Reads(d.Cond, m.Read);
                if (e is ESetNumber sn) Reads(sn.Value, m.Read);
                if (e is EGoal g) { if (g.Life is VSym ls) Reads(ls.E, m.Read); foreach (var a in g.Args) if (a is VSym s) Reads(s.E, m.Read); }
            }
        AssignSlots(paths, m);
        var root = Tree(paths);
        long first = Compile(root, m);
        if (waitIdle)
        {
            // DS1 re-activates the battle goal when its sub-goals are done; DS2's think machines first wait for the current
            // action to end (template 2147483641 state 0: f5001() != 1)
            long w = m.Add(S([], (Ez.End(Ez.Cmp(Ez.Fn(5001), Ez.Ne, Ez.I(1))), first)));
            first = w;
        }
        m.States[0] = S([], (Ez.True, first));
        return m.States;
    }

    /// <summary>
    /// Roll slots by liveness: per path a roll lives from its roll to its last read; two rolls that are ever live at once get
    /// different slots (greedy colouring in order of first roll) from DS2's 1-3 (see <see cref="Slot"/>). DS1's _Interupt
    /// keeps its own "fate" alive across the helpers' rolls (the item-use punish reads it last).
    /// </summary>
    void AssignSlots(List<RunPath> paths, M m)
    {
        var live = new Dictionary<int, HashSet<int>>(); // roll → rolls it interferes with
        var order = new List<int>();
        foreach (var p in paths)
        {
            var rolledAt = new Dictionary<int, int>(); var lastRead = new Dictionary<int, int>();
            for (int i = 0; i < p.Stream.Count; i++)
            {
                var e = p.Stream[i];
                var reads = new HashSet<int>();
                if (e is EDecision d) Reads(d.Cond, reads);
                if (e is ESetNumber sn) Reads(sn.Value, reads);
                if (e is EGoal g) { if (g.Life is VSym ls) Reads(ls.E, reads); foreach (var a in g.Args) if (a is VSym vs) Reads(vs.E, reads); }
                foreach (int r in reads) lastRead[r] = i;
                if (e is ERoll roll && m.Read.Contains(roll.Id)) { rolledAt[roll.Id] = i; if (!order.Contains(roll.Id)) order.Add(roll.Id); }
            }
            foreach (var (a, ia) in rolledAt)
                foreach (var (b, ib) in rolledAt)
                    if (a != b && lastRead.TryGetValue(a, out int la) && ib > ia && ib <= la)
                    {
                        (live.TryGetValue(a, out var sa) ? sa : live[a] = []).Add(b);
                        (live.TryGetValue(b, out var sb) ? sb : live[b] = []).Add(a);
                    }
        }
        foreach (int r in order)
        {
            var taken = live.TryGetValue(r, out var n) ? n.Where(m.Slots.ContainsKey).Select(x => m.Slots[x]).ToHashSet() : [];
            int slot = Enumerable.Range(1, 3).FirstOrDefault(sl => !taken.Contains(sl));
            if (slot == 0) { report.Add($"WARNING {m.Name}: roll {r} interferes with three live rolls; DS2 has 3 free roll slots"); slot = 1; }
            m.Slots[r] = slot;
        }
    }

    static void Reads(SExpr e, HashSet<int> into)
    {
        switch (e)
        {
            case SRand r: into.Add(r.Id); break;
            case SBin b: Reads(b.L, into); Reads(b.R, into); break;
            case SNot n: Reads(n.E, into); break;
        }
    }

    long Compile(Node n, M m)
    {
        long tail;
        if (n.Cond != null)
        {
            long t = n.T != null ? Compile(n.T, m) : ReturnState(m, VNil.I);
            long f = n.F != null ? Compile(n.F, m) : ReturnState(m, VNil.I);
            tail = m.Add(S([], (Ez.End(Expr(n.Cond, m)), t), (Ez.True, f)));
        }
        else tail = ReturnState(m, n.Return ?? VNil.I);
        // effects backwards: each becomes states leading to `tail`; commands gather into the next state's entry
        var pending = new List<ESD.CommandCall>();
        var effects = n.Effects;
        for (int k = effects.Count - 1; k >= 0; k--)
        {
            var e = effects[k];
            switch (e)
            {
                case ERoll r:
                    if (!m.Read.Contains(r.Id)) break; // a roll nothing reads (DS1's guard roll against Odds_Guard 0)
                    pending.Insert(0, Ez.Cmd(1, Slot(m, r.Id), (int)Math.Round(r.IsFloat ? r.Lo * 100 : r.Lo), (int)Math.Round(r.IsFloat ? r.Hi * 100 : r.Hi)));
                    break;
                case ESetNumber sn:
                    pending.Insert(0, new ESD.CommandCall(1, 5750, Ez.I(sn.Slot).Concat(Ez.A1).ToArray(), Ez.End(Expr(sn.Value, m))));
                    break;
                case ETimer et when timerSlot.TryGetValue(et.Index, out int tslot):   // session 11 (dchr11), rule AI-TM: SetTimer → 1:2(slot) reset
                    pending.Insert(0, Ez.Cmd(2, tslot));
                    break;
                case ETimer:
                case EEventMoveTarget:
                case EObserve:
                case EClear:
                    break;
                case ECallAct ca:
                    tail = Flush(m, pending, tail);
                    if (ca.Act > 0) tail = m.Add(Call(ActMachine(ca.Act), [], tail));
                    break;
                case ECallAfter:
                    tail = Flush(m, pending, tail);
                    tail = m.Add(Call(After, [], tail));
                    break;
                case EGoal g:
                {
                    tail = Flush(m, pending, tail);
                    // a combo chain: this goal is the last of consecutive CommonAttacks whose earlier ones are combo attacks
                    if (g.Goal == "CommonAttack")
                    {
                        int st = k;
                        while (st > 0 && effects[st - 1] is EGoal { Goal: "CommonAttack" } pg && Flag(pg.Args, 6)) st--;
                        var chain = effects.Skip(st).Take(k - st + 1).Cast<EGoal>().ToList();
                        tail = Attack(chain, m, tail);
                        k = st;
                        break;
                    }
                    // the event move target of this goal (SetEventMoveTarget earlier in the same node)
                    m.EventTarget = effects.Take(k).OfType<EEventMoveTarget>().LastOrDefault()?.Region ?? m.EventTarget;
                    tail = Goal(g, m, tail);
                    break;
                }
            }
        }
        return Flush(m, pending, tail);
    }

    static long Flush(M m, List<ESD.CommandCall> pending, long next)
    {
        if (pending.Count == 0) return next;
        var s = S([.. pending], (Ez.True, next));
        pending.Clear();
        return m.Add(s);
    }

    long ReturnState(M m, V ret)
    {
        // DS1's return values: acts return GetWellSpace odds (handled before), the after-act and interrupt return true when
        // they replaced the goal plan; machines return 1 for true, 0 otherwise
        int v = ret is VBool { B: true } ? 1 : 0;
        var s = new ESD.State();
        var c = NoTarget(Ez.True);
        c.PassCommands.Add(new ESD.CommandCall(7, -1, Ez.End(Ez.I(v))));
        s.Conditions.Add(c);
        return m.Add(s);
    }

    static int Slot(M m, int rollId)
    {
        if (!m.Slots.TryGetValue(rollId, out int s))
        {
            // DS2 AIs roll into slots 0-3 only (1:1(0..3, …) in all 485 vanilla AI ESDs); the template's attack machine
            // rolls into 0 at each combo stage, so DS1 rolls use 1-3 (no DS1 value is read after an attack call)
            s = m.NextSlot;
            m.NextSlot = m.NextSlot >= 3 ? 1 : m.NextSlot + 1;
            m.Slots[rollId] = s;
        }
        return s;
    }

    static bool Flag(List<V> args, int i) => i < args.Count && args[i] is VBool { B: true };
    static double Num(V v, double dflt) => v is VNum n ? n.X : dflt;

    // ---------------------------------------------------------------- goals
    long Attack(List<EGoal> chain, M m, long next, bool forceSingle = false)
    {
        var ids = chain.Select(g => (int)Num(g.Args[0], -1)).ToList();
        int action; int stop = 0;
        // session 11 (burgchr11, rule B2c): a DS1 chain the network cannot hold as a combo (no route category of its length, more
        // than 4 damage slots) plays its clips as consecutive single attacks, as DS1's ComboAttack / ComboFinal goals would without
        // the combo window
        if (!(ids.Count == 1 && !Flag(chain[0].Args, 6)) && !forceSingle && ctx.Chain(ids) == null && ids.All(i => ctx.SingleAction(i) != null))
        {
            report.Add($"{m.Name}: DS1 chain {string.Join("→", ids)} has no DS2 combo: played as single attacks (rule B2c)");
            long n2 = next;
            for (int i = chain.Count - 1; i >= 0; i--) n2 = Attack([chain[i]], m, n2, forceSingle: true);
            return n2;
        }
        if (forceSingle || ids.Count == 1 && !Flag(chain[0].Args, 6))
        {
            // session 11 (chrai11), rule EM1-n: a DS1 "attack" with a TAE but no clip (the Hellkite's 3011: message 700 at 0 s, the
            // take-off request DS1's event 11015307 answers) is its messages only: the codes are written for their windows, no action
            if (ctx.SingleAction(ids[0]) is null && ctx.Messages(ids[0]) is { Count: > 0 } only)
                return m.Add(Call(MsgMachine(-1, ids[0], only), [], next));
            if (ctx.SingleAction(ids[0]) is not int a) { report.Add($"{m.Name}: DS1 attack {ids[0]} has no DS2 action (skipped)"); return next; }
            action = a;
        }
        else
        {
            if (ctx.Chain(ids) is not { } c) { report.Add($"{m.Name}: DS1 chain {string.Join("→", ids)} has no DS2 combo (skipped)"); return next; }
            action = c.Action;
            if (ids.Count < c.Stages) stop = ids.Count;
        }
        // DS2's attack machine: (action, abort range, abort angle, 0, stop after stage 1/2/3 (%, "r <= p" with r 1..100: the
        // template's bytes 0x93; 101 = always, 0 = never), abort
        // check time); DS1's attack goals have no distance/angle abort (999 / 180), stops only for chain prefixes
        int s1 = stop == 1 ? 101 : 0, s2 = stop == 2 ? 101 : 0, s3 = stop == 3 ? 101 : 0;
        // session 11 (chrai11), rule EM1: a single attack whose DS1 clip sends messages the events read → its message machine
        bool single = forceSingle || ids.Count == 1 && !Flag(chain[0].Args, 6);
        var wins = single ? ctx.Messages(ids[0]) : [];
        if (!single && ids.Any(i => ctx.Messages(i).Count > 0)) report.Add($"{m.Name}: rule EM1: chain {string.Join("→", ids)} plays as a combo: its clips' TAE messages are not carried");
        long call = wins.Count > 0 ? m.Add(Call(MsgMachine(action, ids[0], wins), [], next))
            : m.Add(Call(LibAttack, [Ez.End(Ez.I(action)), Ez.End(Ez.I(999)), Ez.End(Ez.I(180)), Ez.End(Ez.I(0)), Ez.End(Ez.I(s1)), Ez.End(Ez.I(s2)), Ez.End(Ez.I(s3)), Ez.End(Ez.F(0.5f))], next));
        // DS1 CommonAttack's turn phase (turnTime, turnFaceAngle of the first goal: Attack 1.5 s / 20°, Nonspinning 0 s)
        double turnTime = Num(chain[0].Args.ElementAtOrDefault(4) ?? VNil.I, 0), face = Num(chain[0].Args.ElementAtOrDefault(5) ?? VNil.I, 180);
        // session 11 (chrai11), rule AI-TP: an attack aimed at an event point (POINT_EVENT 102 / POINT_INITIAL 100: the Taurus Demon's
        // leaps) has no DS2 turn toward a point: no turn phase (DS2's 1:5903 turns toward the AI target, the player)
        if (chain[0].Args.ElementAtOrDefault(1) is VNum { X: 100 or 102 } pt) { if (turnTime > 0) Once($"{m.Name}: rule AI-TP: DS1 {ids[0]} turns toward point {pt.X} first: no DS2 turn toward a point (no turn phase)"); return call; }
        if (turnTime > 0 && face < 180)
        {
            var turn = S([], (Ez.End(Ez.Cmp(Ez.Fn(7002), Ez.Lt, Ez.Num(face))), call), (Ez.End(Ez.Cmp(Ez.Fn(3), Ez.Ge, Ez.Num(turnTime))), call));
            turn.WhileCommands.Add(Ez.Cmd(5903));
            return m.Add(turn);
        }
        return call;
    }

    long Goal(EGoal g, M m, long next)
    {
        var a = g.Args;
        switch (g.Goal)
        {
            case "MoveToSomewhere":
            {
                int target = (int)Num(a.ElementAtOrDefault(0) ?? VNil.I, 0);
                double range = Num(a.ElementAtOrDefault(2) ?? VNil.I, 1);
                bool walk = a.ElementAtOrDefault(4) is VBool { B: true };
                if (target == 0)
                {
                    var st = Waiting(m, next, g.Life, Ez.End(Ez.Cmp(Ez.Fn(7001), Ez.Le, Ez.Num(range))));
                    st.WhileCommands.Add(walk ? Ez.Cmd(5505, 0, 10, 10, 1, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0) : Ez.Cmd(5535, 0, 10, 10, 1, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
                    return m.Add(st);
                }
                int? point = target == 100 ? ctx.HomePoint : target == 102 && m.EventTarget is int r ? ctx.Location(r) : null;
                if (point is not int loc) { report.Add($"{m.Name}: MoveToSomewhere to target {target} has no DS2 point (skipped)"); return next; }
                var mv = Waiting(m, next, g.Life, Ez.End(Ez.Cmp(Ez.Fn(7072, Ez.I(loc), Ez.Num(range)), Ez.Ne, Ez.I(1))));
                mv.EntryCommands.Add(Ez.Cmd(7302));
                mv.WhileCommands.Add(Ez.Cmd(7005, loc, -1, 100, 0, 0));
                mv.ExitCommands.Add(new ESD.CommandCall(5, 7301, Ez.End(Ez.Cmp(Ez.Fn(7302), Ez.Ne, Ez.I(1)))));
                return m.Add(mv);
            }
            case "Wait":
                return m.Add(Waiting(m, next, g.Life, null));
            case "Turn":
            {
                // session 11 (chrai11), rule AI-TP: a turn toward an event point (POINT_EVENT 102 / POINT_INITIAL 100) has no DS2
                // command (1:5903 turns toward the AI target): no turn (the Taurus Demon's leap faces its warp point by DS1's event)
                if (a.ElementAtOrDefault(0) is VNum { X: 100 or 102 } tp) { Once($"{m.Name}: rule AI-TP: Turn toward point {tp.X}: no DS2 turn toward a point (skipped)"); return next; }
                double ang = Num(a.ElementAtOrDefault(1) ?? VNil.I, 0);
                if (ang <= 0) ang = 10; // turn.lua: angle_range <= 0 → 10
                var st = Waiting(m, next, g.Life, Ez.End(Ez.Cmp(Ez.Fn(7002), Ez.Lt, Ez.Num(ang))));
                st.WhileCommands.Add(Ez.Cmd(5903));
                return m.Add(st);
            }
            case "SidewayMove":
            {
                // session 11 (track enemy11, G10 rule M1 / B11): DS1 SidewayMove(life, target, side, angle) circles the target
                // to a side by an arc. Vanilla's strafe form of 1:5565 (Smelter ai305001: arg0 0, direction flag 3 / 4, a14 2), the
                // DS1 side choosing the flag (as Ds2AiMachines Kind.Sideway: side >= 1 → flag 4, else 3), and the arc as an exit
                // f3() >= f7001() · angle · π/180 / v with v = the character's own DS1 side-walk speed (the old command was
                // vanilla's back-away: flag 2, arg0 1, a14 3, and the rolled angle was never read)
                var dir = a.ElementAtOrDefault(1) ?? new VNum(0);
                var angArg = a.ElementAtOrDefault(2) ?? new VNum(45);
                byte[] ang = angArg is VNum an ? Ez.Num(an.X) : Expr(((VSym)angArg).E, m);
                double v = ctx.SideWalkSpeed;
                long Strafe(int d)
                {
                    byte[]? arc = v > 0 ? Ez.End(Ez.Cmp(Ez.Fn(3), Ez.Ge, Ez.Mul(Ez.Mul(Ez.Fn(7001), ang), Ez.F((float)(Math.PI / 180 / v))))) : null;
                    var st = Waiting(m, next, g.Life, arc);
                    int flag = d >= 1 ? 4 : 3;
                    var args = new object[] { 0, 10, 10, 0.5f, 0.5f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0 };
                    args[5 + flag] = 1;
                    st.WhileCommands.Add(Ez.Cmd(5565, args));
                    return m.Add(st);
                }
                if (dir is VNum dn) return Strafe((int)dn.X);
                long r1 = Strafe(1), r0 = Strafe(0);
                return m.Add(S([], (Ez.End(Ez.Cmp(Expr(((VSym)dir).E, m), Ez.Eq, Ez.I(1))), r1), (Ez.True, r0)));
            }
            case "LeaveTarget":
            {
                // session 11 (burgchr11, rule AI-LEAVE): DS1 LeaveTarget(life, target, dist, …) walks away from the target until dist: vanilla's
                // back-away form of 1:5565 (arg0 1, direction flag 2, a14 3: the command M1 replaced for SidewayMove), ending at f7001() ≥ dist
                // or the goal's life
                double dist = Num(a.ElementAtOrDefault(1) ?? VNil.I, 3);
                var st = Waiting(m, next, g.Life, Ez.End(Ez.Cmp(Ez.Fn(7001), Ez.Ge, Ez.Num(dist))));
                var args = new object[] { 1, 10, 10, 0.5f, 0.5f, 0, 0, 1, 0, 0, 0, 0, 0, 0, 3, 0, 0 };
                st.WhileCommands.Add(Ez.Cmd(5565, args));
                return m.Add(st);
            }
            case "SpinStep":
                // DS2's step machine (template 2147483645 with the template's back-step arguments): every DS1 step direction
                // plays the demon's one step clip (a00_0701) through the network's step slots
            {
                // session 11 (burgchr11, rule AI-STEP): the step's direction = DS1's EzState (701 back, 702 left, 703 right) as the
                // library call's direction argument p2 → 1:5221's first direction (vanilla survey of 1:5221: 180 back, 90 / 270 the sides,
                // 160/200 and 210/150 back arcs; which side is 90 is P: taken clockwise from the front, 90 = right). The Capra Demon's
                // side steps 702 / 703 (the Asylum / Stray Demon only have 701: unchanged)
                int ez = a.ElementAtOrDefault(0) is VNum ezn ? (int)ezn.X : 701;
                int dirDeg = ez switch { 702 => 270, 703 => 90, _ => 180 };
                return m.Add(Call(LibStep, [Ez.End(Ez.I(0)), Ez.End(Ez.I(0)), Ez.End(Ez.I(dirDeg)), Ez.End(Ez.I(-1)), Ez.End(Ez.I(-1)), Ez.End(Ez.I(5))], next));
            }
            default:
                report.Add($"{m.Name}: native goal {g.Goal} has no DS2 rule (skipped)");
                return next;
        }
    }

    /// <summary>A waiting state: leaves at its life (state time f3()) or when <paramref name="done"/> holds; outside the
    /// interrupt handler, the interrupts DS1's _Interupt tests are watched first (f5050(n) → the handler; when it reacts,
    /// this machine returns, else the waiting state starts again).</summary>
    ESD.State Waiting(M m, long next, V life, byte[]? done)
    {
        var st = new ESD.State();
        if (!m.InHandler && m.Name != "interrupt")
            foreach (int n in interrupts.Order())
            {
                long handlerCall = m.Next++; // id reserved; filled below
                long abort = ReturnState(m, VNil.I);
                var call = new ESD.State();
                call.EntryCommands.Add(new ESD.CommandCall(6, (int)(uint)InterruptHandler, Ez.End(Ez.I(n))));
                var c = NoTarget(Ez.End(Ez.Cmp(Ez.B9, Ez.Ne, Ez.BA)));
                c.Subconditions.Add(new ESD.Condition(abort, Ez.End(Ez.Cmp(Ez.B9, Ez.Eq, Ez.I(1)))));
                c.Subconditions.Add(new ESD.Condition(-1, Ez.True)); // patched: back to the waiting state
                call.Conditions.Add(c);
                m.States[handlerCall] = call;
                st.Conditions.Add(new ESD.Condition(handlerCall, Ez.End(Ez.Cmp(Ez.Fn(5050, Ez.I(n)), Ez.Ne, Ez.I(0)))));
                pendingBack.Add((m, handlerCall, st));
            }
        if (done != null) st.Conditions.Add(new ESD.Condition(next, done));
        byte[] lifeTest = life switch
        {
            VNum ln => Ez.End(Ez.Cmp(Ez.Fn(3), Ez.Ge, Ez.Num(ln.X))),
            VSym ls when ls.E is SRand { IsFloat: true } r => Ez.End(Ez.Cmp(Ez.Mul(Ez.Fn(3), Ez.I(100)), Ez.Ge, Ez.Fn(1, Ez.I(Slot(m, r.Id))))),
            VSym ls => Ez.End(Ez.Cmp(Ez.Fn(3), Ez.Ge, Expr(ls.E, m))),
            _ => Ez.End(Ez.Cmp(Ez.Fn(3), Ez.Ge, Ez.I(10))),
        };
        st.Conditions.Add(new ESD.Condition(next, lifeTest));
        return st;
    }

    readonly List<(M Machine, long CallState, ESD.State Waiting)> pendingBack = [];

    /// <summary>Patches the interrupt calls' "back to the waiting state" targets once the waiting states have their ids.</summary>
    void PatchBack()
    {
        foreach (var (m, call, waiting) in pendingBack)
        {
            long id = m.States.First(kv => ReferenceEquals(kv.Value, waiting)).Key;
            m.States[call].Conditions[0].Subconditions[1].TargetState = id;
        }
        pendingBack.Clear();
    }

    // ---------------------------------------------------------------- expressions
    byte[] Expr(SExpr e, M m)
    {
        switch (e)
        {
            case SConst c: return Ez.Num(c.X);
            case SDist d: return d.Y ? Ez.Fn(7007) : Ez.Fn(7001);
            case SRand r: return r.IsFloat ? Ez.Div(Ez.Fn(1, Ez.I(Slot(m, r.Id))), Ez.I(100)) : Ez.Fn(1, Ez.I(Slot(m, r.Id)));
            case SNumber n: return Ez.Fn(5074, Ez.I(n.Slot));
            case SEventRequest er when ctx.Fa1 is { } fl:
            {
                // session 11 (chrai11), rule FA1: the E-REQ pairs are codes of the request number J: Σ v · (J == code(slot, v))
                byte[] sum = Ez.I(0);
                foreach (var p in fl.Pairs.Where(p => p.Slot == er.Slot))
                    sum = [.. sum, .. Ez.Mul(Ez.I(p.V), Ez.Cmp(Fa1Code(fl), Ez.Eq, Ez.I(fl.PairCode[p]))), 0x8C];
                return sum;
            }
            case SEventRequest er:
            {
                // session 11 (burgchr11), rule E-REQ: Σ v · (f5903(k) != 0)
                byte[] sum = Ez.I(0);
                foreach (var (_, v, k) in EventRequests.ForThink(er.Think).Where(c => c.Slot == er.Slot))
                    sum = [.. sum, .. Ez.Mul(Ez.I(v), Ez.Cmp(Ez.Fn(5903, Ez.I(k)), Ez.Ne, Ez.I(0))), 0x8C];
                return sum;
            }
            case SHpRate: return Ez.Div(Ez.Fn(5200), Ez.I(100));
            case SRegion rg:
                if (ctx.Location(rg.Region) is not int loc) { report.Add($"{m.Name}: DS1 region {rg.Region} has no EventLocation (never inside)"); return Ez.I(0); }
                // session 11 (chrai11), rule AI-SELF: IsInsideTargetRegion(TARGET_SELF −1, region) → f7075(location, 0) (the Ruin Sentinels'
                // "itself in the box", ai325000; until now the self test read the target's f7070)
                if (rg.Target == -1) return Ez.Cmp(Ez.Fn(7075, Ez.I(loc), Ez.I(0)), Ez.Ne, Ez.I(0));
                if (rg.Target != 0) report.Add($"{m.Name}: region test on target {rg.Target} (only TARGET_ENE_0 / TARGET_SELF have a rule)");
                return Ez.Cmp(Ez.Fn(7070, Ez.I(loc), Ez.I(0)), Ez.Ne, Ez.I(0));
            case SInterrupt it:
                return m.InHandler ? Ez.Cmp(Ez.Param(0), Ez.Eq, Ez.I(it.Id)) : Ez.Cmp(Ez.Fn(5050, Ez.I(it.Id)), Ez.Ne, Ez.I(0));
            case SObserve o: return Observe(o.Slot, m);
            case SSpEffect sp: report.Add($"{m.Name}: HasSpecialEffectId({sp.Id}) has no DS2 rule (false)"); return Ez.I(0);
            case SBearing: return Ez.Fn(7002);                                                              // session 11 (dchr11), rule AI-IT
            case SRightSide: return Ez.Cmp(Ez.Fn(7003, Ez.I(0), Ez.I(179)), Ez.Ne, Ez.I(0));                // right = f7003(0, 179) ≠ 0 (U, the observe rule)
            case STimerDone td:
                // session 11 (dchr11), rule AI-TM: f2(slot) ≥ t; a timer the goal never sets is finished (DS1's timers start expired)
                if (!timerSlot.TryGetValue(td.Index, out int tsl)) { report.Add($"{m.Name}: IsFinishTimer({td.Index}) without a SetTimer / slot: taken as finished"); return Ez.I(1); }
                return Ez.Cmp(Ez.Fn(2, Ez.I(tsl)), Ez.Ge, Ez.Num(timerSec[td.Index]));
            case SNot n: return Ez.Cmp(Expr(n.E, m), Ez.Eq, Ez.I(0));
            case SBin b:
            {
                byte op = b.Op switch
                {
                    "<" => Ez.Lt, ">" => Ez.Gt, "<=" => Ez.Le, ">=" => Ez.Ge, "==" => Ez.Eq, "~=" => Ez.Ne,
                    "+" => 0x8C, "-" => 0x8E, "*" => 0x8F, "/" => 0x90, _ => throw new InvalidDataException($"operator {b.Op}"),
                };
                return [.. Expr(b.L, m), .. Expr(b.R, m), op];
            }
        }
        throw new InvalidDataException($"no DS2 expression for {e}");
    }

    /// <summary>
    /// DS1 IsInsideObserve(slot) for a dummy-poly sphere (dummy D, radius r): the target (capsule-surface distance d =
    /// f7001(), bearing θ = f7002() 0-180°, side f7003(0, 179)) is inside when its centre lies within r of D. Per 10°
    /// bearing bin on D's side the centre distances that do (|c·u − D| &lt; r, u the bin's middle direction) give a
    /// surface-distance interval: centre distance − own radius − the target's.
    /// </summary>
    byte[] Observe(int slot, M m)
    {
        if (!observes.TryGetValue(slot, out var o) || ctx.Dummy(o.Dummy) is not { } dm) { report.Add($"{m.Name}: observe sphere {slot} has no dummy position (never inside)"); return Ez.I(0); }
        double lat = dm.Lateral, fwd = -dm.Behind, r = o.Radius, off = ctx.SelfRadius + ctx.TargetRadius;
        byte[]? any = null;
        int bins = 0;
        for (int a = 0; a < 180; a += 10)
        {
            double th = (a + 5) * Math.PI / 180;
            // target centre at distance c along (|sin θ|·side(lat), cos θ): (c sinθ − |lat|)² + (c cosθ − fwd)² < r²
            double bq = Math.Abs(lat) * Math.Sin(th) + fwd * Math.Cos(th), cq = lat * lat + fwd * fwd - r * r, disc = bq * bq - cq;
            if (disc <= 0) continue;
            double c1 = bq - Math.Sqrt(disc), c2 = bq + Math.Sqrt(disc);
            double d1 = Math.Max(0, c1 - off), d2 = c2 - off;
            if (d2 <= 0) continue;
            var term = Ez.And(Ez.And(Ez.Cmp(Ez.Fn(7002), Ez.Ge, Ez.I(a)), Ez.Cmp(Ez.Fn(7002), Ez.Lt, Ez.I(a + 10))),
                Ez.And(Ez.Cmp(Ez.Fn(7001), Ez.Ge, Ez.Num(Math.Round(d1, 2))), Ez.Cmp(Ez.Fn(7001), Ez.Lt, Ez.Num(Math.Round(d2, 2)))));
            any = any == null ? term : Ez.Or(any, term);
            bins++;
        }
        if (any == null) { report.Add($"{m.Name}: observe sphere {slot} (dummy {o.Dummy}, r {r}) is inside the character's own capsule (never inside)"); return Ez.I(0); }
        // side: DS1 model space faces -Z with the character's right at -X (DS1 "R Foot" dummy 3 at x -1.53); f7003(0, 179) != 0
        // taken as "on the right" (U)
        var side = Ez.Cmp(Ez.Fn(7003, Ez.I(0), Ez.I(179)), lat < 0 ? Ez.Ne : Ez.Eq, Ez.I(0));
        observeNotes.Add($"observe {slot}: dummy {o.Dummy} (lateral {lat:0.##}, ahead {fwd:0.##}) r {r}: {bins} bearing bins, side {(lat < 0 ? "right" : "left")}");
        return Ez.And(side, any);
    }

    readonly List<string> observeNotes = [];

    // ================================================================== compaction (session 8, track boss fix pass)
    /// <summary>Largest state group in DS2's own AI ESDs: ai820004, 93 states (all 485 vanilla AI ESDs; the next largest
    /// have 47; data\sandbox\boss\review-scratch\esd_calldepth.py). Every generated machine stays at or below this.</summary>
    public const int MaxStates = 90;
    /// <summary>Deepest machine call chain in DS2's own AI ESDs, counting the mode group as 1: 8 (ai102100, ai611000; 145
    /// files reach 6, 18 reach 7, the template ai305001 5).</summary>
    public const int MaxCallDepth = 8;

    /// <summary>
    /// A generated machine in the shape DS2 writes its machines (review finding, session 8 fix pass: the think machine was
    /// one group of 670 states and the interrupt handler 151, where vanilla's largest group has 93):
    /// <list type="number">
    /// <item>decision chains: a state whose last condition is the unconditional fallback to a state that only tests (no
    /// commands, no other way in) takes over that state's conditions, so DS1's if/elseif ladder, the weighted choice's
    /// cascade and the roll before it are one state (vanilla: the Smelter's mode choice ai305001 2147483638 rolls 1:1(0, 1,
    /// 100) in its entry and tests the f1(0) bands in the same state's conditions; ESD takes the first condition that holds
    /// as Lua's if/elseif takes the first branch);</item>
    /// <item>identical states are one state (partition refinement: same commands, same conditions, targets in the same
    /// class): the per-path leaves "call act n, return" and the returns are shared.</item>
    /// </list>
    /// What the machine does is unchanged: <see cref="Equivalent"/> proves it for every machine (bisimulation of the
    /// original and the compacted machine with decision chains unfolded) and the build fails otherwise. States are
    /// renumbered from 0 in visiting order.
    /// </summary>
    public static Dictionary<long, ESD.State> Compact(Dictionary<long, ESD.State> machine)
    {
        var m = Clone(machine);
        MergeDecisionChains(m);
        m = Minimize(m);
        MergeDecisionChains(m);
        return Renumber(m);
    }

    static IEnumerable<ESD.Condition> FlatConds(ESD.Condition c) => new[] { c }.Concat(c.Subconditions.SelectMany(FlatConds));
    static IEnumerable<long> Targets(ESD.State s) => s.Conditions.SelectMany(FlatConds).Where(c => c.TargetState is long).Select(c => c.TargetState!.Value);
    static bool Commandless(ESD.State s) => s.EntryCommands.Count == 0 && s.WhileCommands.Count == 0 && s.ExitCommands.Count == 0;
    static bool IsFallback(ESD.Condition c) => c.TargetState is long && c.Evaluator.AsSpan().SequenceEqual(Ez.True) && c.PassCommands.Count == 0 && c.Subconditions.Count == 0;

    static Dictionary<long, ESD.State> Clone(Dictionary<long, ESD.State> m) => m.ToDictionary(kv => kv.Key, kv => CloneState(kv.Value));
    static ESD.State CloneState(ESD.State s)
    {
        var n = new ESD.State();
        n.EntryCommands.AddRange(s.EntryCommands); n.WhileCommands.AddRange(s.WhileCommands); n.ExitCommands.AddRange(s.ExitCommands);
        n.Conditions.AddRange(s.Conditions.Select(CloneCond));
        return n;
    }
    static ESD.Condition CloneCond(ESD.Condition c)
    {
        var n = new ESD.Condition { TargetState = c.TargetState, Evaluator = c.Evaluator };
        n.PassCommands.AddRange(c.PassCommands);
        n.Subconditions.AddRange(c.Subconditions.Select(CloneCond));
        return n;
    }

    /// <summary>Rule 1 of <see cref="Compact"/>; state 0 (DS2's start state: only "if 1 → first") is left alone.</summary>
    static void MergeDecisionChains(Dictionary<long, ESD.State> m)
    {
        for (bool changed = true; changed;)
        {
            changed = false;
            var preds = m.Values.SelectMany(Targets).GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
            foreach (var (id, a) in m.OrderBy(kv => kv.Key))
            {
                if (id == 0 || a.Conditions.Count == 0 || a.WhileCommands.Count > 0 || a.ExitCommands.Count > 0 || !IsFallback(a.Conditions[^1])) continue;
                long b = a.Conditions[^1].TargetState!.Value;
                if (b == 0 || b == id || !m.TryGetValue(b, out var bs) || preds.GetValueOrDefault(b) != 1 || !Commandless(bs) || bs.Conditions.Count == 0) continue;
                a.Conditions.RemoveAt(a.Conditions.Count - 1);
                a.Conditions.AddRange(bs.Conditions);
                m.Remove(b);
                changed = true;
                break;
            }
        }
    }

    static string Sig(ESD.CommandCall c) => $"{c.CommandBank}:{c.CommandID}({string.Join(",", c.Arguments.Select(Convert.ToHexString))})";
    static string Sig(ESD.Condition c, Func<long, string> target) =>
        $"[{Convert.ToHexString(c.Evaluator)}>{(c.TargetState is long t ? target(t) : "-")}|{string.Join(";", c.PassCommands.Select(Sig))}|{string.Join("", c.Subconditions.Select(s => Sig(s, target)))}]";
    static string Sig(ESD.State s, Func<long, string> target) =>
        $"E{string.Join(";", s.EntryCommands.Select(Sig))}W{string.Join(";", s.WhileCommands.Select(Sig))}X{string.Join(";", s.ExitCommands.Select(Sig))}C{string.Join("", s.Conditions.Select(c => Sig(c, target)))}";

    /// <summary>Rule 2 of <see cref="Compact"/> (Moore's partition refinement over the reachable states; state 0 keeps a class
    /// of its own).</summary>
    static Dictionary<long, ESD.State> Minimize(Dictionary<long, ESD.State> m)
    {
        var order = Reachable(m);
        // classes as short ids ("c12") so the next round's signatures stay short; state 0 is "S0"
        Dictionary<long, string> Ids(Dictionary<long, string> keys)
        {
            var ids = keys.Where(kv => kv.Key != 0).Select(kv => kv.Value).Distinct().Select((k, i) => (k, i)).ToDictionary(p => p.k, p => $"c{p.i}");
            return keys.ToDictionary(kv => kv.Key, kv => kv.Key == 0 ? "S0" : ids[kv.Value]);
        }
        var cls = Ids(order.ToDictionary(s => s, s => s == 0 ? "" : Sig(m[s], _ => "*")));
        int count = cls.Values.Distinct().Count();
        while (true)
        {
            var next = Ids(order.ToDictionary(s => s, s => s == 0 ? "" : cls[s] + "#" + Sig(m[s], t => cls.TryGetValue(t, out var k) ? k : "?")));
            int n = next.Values.Distinct().Count();
            cls = next;
            if (n == count) break;
            count = n;
        }
        var rep = new Dictionary<string, long>();
        foreach (long s in order) rep.TryAdd(cls[s], s);
        var result = new Dictionary<long, ESD.State>();
        foreach (var (k, s) in rep)
        {
            var st = m[s];
            foreach (var c in st.Conditions.SelectMany(FlatConds)) if (c.TargetState is long t) c.TargetState = rep[cls[t]];
            result[s] = st;
        }
        return result;
    }

    /// <summary>States reachable from state 0, in visiting order (breadth first, conditions in order).</summary>
    static List<long> Reachable(Dictionary<long, ESD.State> m)
    {
        var seen = new HashSet<long> { 0 }; var order = new List<long> { 0 };
        for (int i = 0; i < order.Count; i++)
            foreach (long t in Targets(m[order[i]]))
                if (m.ContainsKey(t) && seen.Add(t)) order.Add(t);
        return order;
    }

    static Dictionary<long, ESD.State> Renumber(Dictionary<long, ESD.State> m)
    {
        var order = Reachable(m);
        var map = order.Select((s, i) => (s, i)).ToDictionary(p => p.s, p => (long)p.i);
        var r = new Dictionary<long, ESD.State>();
        foreach (long s in order)
        {
            foreach (var c in m[s].Conditions.SelectMany(FlatConds)) if (c.TargetState is long t) c.TargetState = map[t];
            r[map[s]] = m[s];
        }
        return r;
    }

    /// <summary>
    /// The proof behind <see cref="Compact"/>: two machines do the same when their start states are bisimilar, where a
    /// state is compared by its commands and its condition list with decision chains unfolded (a fallback to a state that
    /// only tests is replaced by that state's conditions, as many times as it applies), and the targets pairwise.
    /// </summary>
    public static bool Equivalent(Dictionary<long, ESD.State> a, Dictionary<long, ESD.State> b, out string why)
    {
        List<ESD.Condition> Unfold(Dictionary<long, ESD.State> m, long id)
        {
            var list = m[id].Conditions.ToList();
            var seen = new HashSet<long> { id };
            while (list.Count > 0 && IsFallback(list[^1]) && list[^1].TargetState is long t && t != 0 && m.TryGetValue(t, out var ts)
                && Commandless(ts) && ts.Conditions.Count > 0 && seen.Add(t))
            {
                list.RemoveAt(list.Count - 1);
                list.AddRange(ts.Conditions);
            }
            return list;
        }
        static string Local(ESD.State s, List<ESD.Condition> conds) => Sig(new ESD.State { EntryCommands = s.EntryCommands, WhileCommands = s.WhileCommands, ExitCommands = s.ExitCommands, Conditions = conds }, _ => "*");
        static void Pairs(List<ESD.Condition> x, List<ESD.Condition> y, List<(long, long)> into)
        {
            for (int i = 0; i < x.Count; i++)
            {
                if (x[i].TargetState is long tx && y[i].TargetState is long ty) into.Add((tx, ty));
                Pairs(x[i].Subconditions, y[i].Subconditions, into);
            }
        }
        var seenPairs = new HashSet<(long, long)>();
        var work = new Stack<(long, long)>();
        work.Push((0, 0));
        while (work.Count > 0)
        {
            var (x, y) = work.Pop();
            if (!seenPairs.Add((x, y))) continue;
            if (!a.ContainsKey(x) || !b.ContainsKey(y)) { why = $"state {x} / {y} missing"; return false; }
            // state 0 is the start: compare by its target only (it only says "if 1 → first")
            var cx = x == 0 ? a[x].Conditions : Unfold(a, x);
            var cy = y == 0 ? b[y].Conditions : Unfold(b, y);
            if (Local(a[x], cx) != Local(b[y], cy)) { why = $"original state {x} and compacted state {y} differ"; return false; }
            var next = new List<(long, long)>();
            Pairs(cx, cy, next);
            foreach (var p in next) work.Push(p);
        }
        why = $"{seenPairs.Count} state pairs";
        return true;
    }

    // ================================================================== small builders
    /// <summary>A condition without a target state: its pass commands run (a return 7:-1(v)) or its subconditions decide.</summary>
    static ESD.Condition NoTarget(byte[] evaluator) => new() { Evaluator = evaluator };

    static ESD.State S(IEnumerable<ESD.CommandCall> entry, params (byte[] If, long Then)[] conds)
    {
        var s = new ESD.State();
        s.EntryCommands.AddRange(entry);
        foreach (var (c, t) in conds) s.Conditions.Add(new ESD.Condition(t, c));
        return s;
    }

    /// <summary>A state that calls machine <paramref name="id"/> and goes on when it returns (vanilla: entry 6:id(args),
    /// condition ?B9 != ?BA with a subcondition).</summary>
    static ESD.State Call(long id, byte[][] args, long next)
    {
        var s = new ESD.State();
        s.EntryCommands.Add(new ESD.CommandCall(6, (int)(uint)id, args));
        var c = NoTarget(Ez.End(Ez.Cmp(Ez.B9, Ez.Ne, Ez.BA)));
        c.Subconditions.Add(new ESD.Condition(next, Ez.True));
        s.Conditions.Add(c);
        return s;
    }

    /// <summary>
    /// The lower logic: the template's library machines and init mode group 10, mode group 20 (the battle loop: sync, think)
    /// and its aliases 30/40/100/110/120 (the template's other modes; our groups never request them, kept so a stray dispatch
    /// still thinks), and the generated machines.
    /// </summary>
    public static ESD BuildLower(ESD template, Ds1AiInterpreter lib, string goal, IContext ctx, List<string> report)
    {
        var b = new Ds2AiEsd(ctx, report);
        if (lib.QueryRules) b.Timers(template, lib, goal);   // session 11 (dchr11), rule AI-TM
        var esd = BuildWith(b, template, lib, goal);
        b.PatchBack();
        report.AddRange(b.observeNotes.Distinct());
        // the generated machines in DS2's shape: compacted (each proved equivalent), sizes and call depth against vanilla's
        var generated = esd.StateGroups.Keys.Where(g => g is >= 2_000_000_000 && g <= Think).ToList();
        var sizes = new List<string>();
        foreach (long g in generated)
        {
            var before = esd.StateGroups[g];
            var after = Compact(before);
            if (!Equivalent(before, after, out string why)) throw new InvalidDataException($"AI machine {g}: compaction changed the machine ({why})");
            esd.StateGroups[g] = after;
            sizes.Add($"{g} {before.Count} → {after.Count}");
        }
        report.Add($"compaction (decision chains + identical states, each proved equivalent): {string.Join(", ", sizes)}");
        foreach (long g in generated.ToList()) generated.AddRange(Split(esd, g, report));   // session 11 (burgchr11): rule AI-SPLIT
        CheckSizes(esd, generated, report);
        int depth = CallDepth(esd, out var chain);
        report.Add($"largest group {esd.StateGroups.Max(g => g.Value.Count)} states (limit {MaxStates}; vanilla max 93); deepest call chain {depth} (vanilla max {MaxCallDepth}): {string.Join(" → ", chain)}");
        if (depth > MaxCallDepth) report.Add($"WARNING call chain {depth} deeper than any vanilla AI ESD ({MaxCallDepth})");
        return esd;
    }

    /// <summary>
    /// Session 11 (track burgchr11), rule AI-SPLIT: a compacted machine above <see cref="MaxStates"/> (the Taurus Demon's think: 101
    /// states) gives regions to sub-machines until it fits. A region = every state reachable from an entry state e (not the start
    /// state 0) whose returns (7:-1) all return one value v and which fits a machine itself; its states are copied into a new
    /// machine (state 0 = "if 1 → e", as every generated machine) and e becomes a call of
    /// it followed by "return v"; the caller drops the states only e led to (shared ones stay). What the machine does is unchanged (the region's paths run in the callee and end in the same
    /// return); the call chain grows by one (vanilla allows 8). The region dropping most states first; machine ids below the lowest generated one.
    /// </summary>
    static List<long> Split(ESD esd, long g, List<string> report)
    {
        var made = new List<long>();
        var m = esd.StateGroups[g];
        if (m.Count <= MaxStates) return made;
        static IEnumerable<ESD.Condition> All(ESD.State s) => s.Conditions.SelectMany(FlatConds);
        static string? RetOf(ESD.State s) => All(s).SelectMany(c => c.PassCommands).Where(c => c.CommandBank == 7 && c.CommandID == -1).Select(c => Convert.ToHexString(c.Arguments[0])).FirstOrDefault();
        for (int guard = 0; m.Count > MaxStates && guard < 64; guard++)
        {
            if (made.Count > 0) m = Compact(Renumber(m));
            var preds = new Dictionary<long, HashSet<long>>();
            foreach (var (id, st) in m) foreach (long t in Targets(st)) { if (!preds.TryGetValue(t, out var l)) preds[t] = l = []; l.Add(id); }
            HashSet<long> Region(long e)
            {
                var r = new HashSet<long> { e }; var q = new Queue<long>(); q.Enqueue(e);
                while (q.Count > 0) foreach (long t in Targets(m[q.Dequeue()])) if (m.ContainsKey(t) && r.Add(t)) q.Enqueue(t);
                return r;
            }
            // the states the main machine loses when e becomes a call: those reachable only through e (fixpoint)
            HashSet<long> Owned(long e, HashSet<long> r)
            {
                var own = new HashSet<long> { e };
                for (bool ch = true; ch;)
                {
                    ch = false;
                    foreach (long x in r) if (!own.Contains(x) && preds.GetValueOrDefault(x)?.All(own.Contains) == true) { own.Add(x); ch = true; }
                }
                return own;
            }
            (long E, HashSet<long> R, HashSet<long> Own, string V)? best = null;
            foreach (long e in m.Keys.Where(k => k != 0))
            {
                var r = Region(e);
                if (r.Contains(0) || r.Count > MaxStates - 1) continue;
                var rets = r.Select(x => RetOf(m[x])).Where(v => v != null).Distinct().ToList();
                if (rets.Count != 1) continue;
                var own = Owned(e, r);
                if (own.Count < 3) continue;
                if (best == null || own.Count > best.Value.Own.Count) best = (e, r, own, rets[0]!);
            }
            if (best == null) { report.Add($"rule AI-SPLIT: machine {g} ({m.Count} states): no region with one return value fits a machine"); break; }
            long nid = esd.StateGroups.Keys.Where(k => k >= 2_000_000_000).Min() - 1;
            var (e0, reg, owned, v) = best.Value;
            // the callee: copies of the region's states (states shared with the rest stay in the caller too), state 0 = "if 1 → e"
            var sub = new Dictionary<long, ESD.State> { [0] = S([], (Ez.True, e0)) };
            foreach (long x in reg) sub[x] = CloneState(m[x]);
            esd.StateGroups[nid] = Compact(Renumber(sub));
            foreach (long x in owned) m.Remove(x);
            long retId = m.Keys.Max() + 1;
            var ret = new ESD.State(); var rc = NoTarget(Ez.True);
            rc.PassCommands.Add(new ESD.CommandCall(7, -1, Convert.FromHexString(v)));
            ret.Conditions.Add(rc);
            m[retId] = ret;
            m[e0] = Call(nid, [], retId);
            made.Add(nid);
            report.Add($"rule AI-SPLIT: machine {g}: the {reg.Count} states from state {e0} → machine {nid} (returns {v}; {owned.Count} leave the caller, {reg.Count - owned.Count} shared ones copied); {m.Count} left");
        }
        esd.StateGroups[g] = made.Count > 0 ? Compact(Renumber(m)) : Renumber(m);   // the caller compacted again (its new return may equal an old one)
        return made;
    }

    /// <summary>A generated machine still above <see cref="MaxStates"/> after compaction is reported (ds1boss-check fails on
    /// it). Splitting it into sub-machines would cost call depth (each sub-machine is one more 6: level; the demon's chain is
    /// already 6 of vanilla's 8), so it is left to the character that needs it: the Asylum Demon's largest machine is 38.</summary>
    static void CheckSizes(ESD esd, List<long> generated, List<string> report)
    {
        foreach (long g in generated) if (esd.StateGroups[g].Count > MaxStates) report.Add($"WARNING machine {g}: {esd.StateGroups[g].Count} states after compaction (DS2's largest group: 93)");
    }

    /// <summary>The deepest chain of machine calls (6:id) from a mode group (id &lt; 2,000,000,000 counts as 1).</summary>
    public static int CallDepth(ESD esd, out List<long> chain)
    {
        var memo = new Dictionary<long, List<long>>();
        List<long> Deepest(long g, HashSet<long> stack)
        {
            if (memo.TryGetValue(g, out var done)) return done;
            if (!esd.StateGroups.TryGetValue(g, out var states) || !stack.Add(g)) return [];
            var callees = states.Values.SelectMany(s => s.EntryCommands.Concat(s.WhileCommands).Concat(s.ExitCommands)).Where(c => c.CommandBank == 6).Select(c => (long)(uint)c.CommandID).Distinct();
            var best = callees.Select(c => Deepest(c, stack)).OrderByDescending(l => l.Count).FirstOrDefault() ?? [];
            stack.Remove(g);
            return memo[g] = [g, .. best];
        }
        chain = esd.StateGroups.Keys.Where(g => g < 2_000_000_000).Select(g => Deepest(g, [])).OrderByDescending(l => l.Count).FirstOrDefault() ?? [];
        return chain.Count;
    }

    /// <param name="goal">the battle goal's REGISTER_GOAL name (223200_battle.lua: "MiniGreaterDemon223200Battle"): its
    /// _Activate and _Interupt are the think and the interrupt handler; acts and the after-act come from the Lua's own
    /// registrations (REGIST_FUNC, atkAfterFunc).</param>
    static ESD BuildWith(Ds2AiEsd b, ESD template, Ds1AiInterpreter lib, string goal)
    {
        // the header as the template's: name and Unk70..7C (non-zero in all 538 vanilla ESDs; MapBuilder copies them for
        // our event ESDs, which load; review finding, session 8 fix pass: they were left 0)
        var esd = new ESD(template.LongFormat, template.DarkSoulsCount)
        {
            Name = template.Name, Unk70 = template.Unk70, Unk74 = template.Unk74, Unk78 = template.Unk78, Unk7C = template.Unk7C,
        };
        foreach (long id in new[] { 10L, LibAttack, LibStep, LibSync, LibInit })
        {
            if (!template.StateGroups.TryGetValue(id, out var g)) throw new InvalidDataException($"template AI ESD lacks group {id}");
            esd.StateGroups[id] = g;
        }
        foreach (long mode in new long[] { 20, 30, 40, 100, 110, 120 }) esd.StateGroups[mode] = BattleMode();
        var intPaths = lib.Global(goal + "_Interupt") is VFunc ? lib.Paths(goal + "_Interupt") : [];
        foreach (var p in intPaths) foreach (var d in p.Stream.OfType<EDecision>()) Collect(d.Cond, b.interrupts);
        var think = lib.Paths(goal + "_Activate", "activate");
        foreach (var p in think) foreach (var o in p.Stream.OfType<EObserve>()) b.observes[o.Slot] = (o.Target, o.Dummy, o.Radius);
        esd.StateGroups[Think] = b.Machine(think, "think", waitIdle: true);
        var calls = think.SelectMany(p => p.Stream.OfType<ECallAct>()).Where(c => c.Act > 0).DistinctBy(c => c.Act).OrderBy(c => c.Act).ToList();
        var acts = calls.Select(c => c.Act).ToList();
        foreach (var c in calls)
            esd.StateGroups[ActMachine(c.Act)] = b.Machine(c.Extra is { } ex ? lib.Paths(c.Function, "act", ex) : lib.Paths(c.Function, "act"), c.Function);   // session 11 (ret11): rule AI-D1 default acts take defFuncParamTbl[N]
        string? afterFn = calls.Select(c => c.AfterFunction).FirstOrDefault(f => f != null);
        esd.StateGroups[After] = afterFn != null ? b.Machine(lib.Paths(afterFn), afterFn) : b.Machine([new RunPath([], VNil.I)], "after-act (none)");
        esd.StateGroups[InterruptHandler] = b.Machine(intPaths.Count > 0 ? intPaths : [new RunPath([], new VBool(false))], "interrupt", inHandler: true, returnTrue: true);
        // session 11 (chrai11), rules FA1 / EM1: the forced-clip dispatcher between the battle mode and the think; the message machines
        if (b.ctx.Fa1 is { } fa1)
        {
            long disp = b.Fa1Dispatcher(fa1);
            foreach (long mode in new long[] { 20, 30, 40, 100, 110, 120 }) esd.StateGroups[mode] = BattleMode(disp);
        }
        foreach (var (id, st) in b.extraMachines) esd.StateGroups[id] = st;
        b.report.Add($"think machine {Think}: {think.Count} DS1 paths; acts {string.Join(", ", acts)} → machines {string.Join(", ", acts.Select(ActMachine))}; after-act {After}; interrupt handler {InterruptHandler} (DS1 _Interupt, {intPaths.Count} paths) on f5050({string.Join("/", b.interrupts.Order())})");
        return esd;
    }
}

/// <summary>EzState expression bytecode (the encoding <see cref="Lordran.Tools.Commands.Esd.Expr"/> decodes): pushes, calls
/// (0x84 + argument count), comparisons, logic, A1 end.</summary>
public static class Ez
{
    public static readonly byte[] True = [0x41, 0xA1];
    public static readonly byte[] A1 = [0xA1];
    public static readonly byte[] B9 = [0xB9], BA = [0xBA];
    /// <summary>Comparison opcodes as DS2 runs them (evaluator table PTR_FUN_1415b2920, right operand popped first; rulebook §7,
    /// V s9-12): 0x91 &lt;, 0x92 &gt;, 0x93 ≤, 0x94 ≥. Until chrint stage 3 this table read Le 0x91 / Ge 0x92 / Lt 0x93 / Gt 0x94
    /// (the community labels), so the demon's DS1 "≤" ran as "&lt;" (the act rolls fate ≤ cumulative weight lost one point
    /// each, <c>f5074(1) &lt;= 2</c> ran as <c>&lt; 2</c>) and "≥" as "&gt;". Same constants as <see cref="E.OpLt"/> ….</summary>
    public const byte Lt = E.OpLt, Gt = E.OpGt, Le = E.OpLe, Ge = E.OpGe, Eq = 0x95, Ne = 0x96;
    public static byte[] I(int v) => v is >= -64 and < 64 ? [(byte)(0x40 + v)] : [0x82, .. BitConverter.GetBytes(v)];
    public static byte[] F(float f) => [0x80, .. BitConverter.GetBytes(f)];
    /// <summary>An integral value as an int push, anything else as a float.</summary>
    public static byte[] Num(double x) => x == Math.Round(x) && Math.Abs(x) < 1e9 ? I((int)x) : F((float)x);
    public static byte[] Fn(int id, params byte[][] args) => [.. I(id), .. args.SelectMany(a => a), (byte)(0x84 + args.Length)];
    public static byte[] Param(int n) => [.. I(n), 0xB8];
    public static byte[] Cmp(byte[] a, byte op, byte[] b) => [.. a, .. b, op];
    public static byte[] And(byte[] a, byte[] b) => [.. a, .. b, 0x98];
    public static byte[] Or(byte[] a, byte[] b) => [.. a, .. b, 0x99];
    public static byte[] Mul(byte[] a, byte[] b) => [.. a, .. b, 0x8F];
    public static byte[] Div(byte[] a, byte[] b) => [.. a, .. b, 0x90];
    public static byte[] End(byte[] e) => [.. e, 0xA1];
    public static ESD.CommandCall Cmd(int id, params object[] args) => new(1, id, args.Select(a => End(a switch
    {
        int i => I(i), float f => F(f), double d => F((float)d), byte[] b => b, _ => throw new ArgumentException($"command argument {a}"),
    })).ToArray());
}
