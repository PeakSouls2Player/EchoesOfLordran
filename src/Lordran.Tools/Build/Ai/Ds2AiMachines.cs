using SoulsFormats;
using static Lordran.Tools.Build.Ai.Esb;

namespace Lordran.Tools.Build.Ai;

/// <summary>
/// The DS2 side of the DS1 → DS2 AI translation (session 8, track enemies step 2): DS2 AI ESD commands / functions we use and
/// the shared generated machines ("aiCommon in ESD") that implement DS1's common goals. DS2's AI ESDs ship without EDDs, so
/// every meaning below comes from the DS2 executable's command handler (decompile FUN_14011b610, the lower-logic AI command
/// dispatcher, and its callees) or from consistent usage over all 485 vanilla AI ESDs (<c>ai-survey</c>,
/// <c>work\ai\survey.txt</c>). Status: V = read in the decompile, P = usage only.
/// <list type="table">
/// <item><c>1:1(slot, lo, hi)</c> roll a random int into slot 0-3 (V: FUN_1404910b0 id 1, slot &lt; 4) / <c>f1(slot)</c> read it (P)</item>
/// <item><c>1:2(slot)</c> reset timer 0-3 (V: id 2) / <c>f2(slot)</c> seconds since (P)</item>
/// <item><c>f3()</c> seconds in the current state (P, also the event translator's settle timer)</item>
/// <item><c>1:5204(action, check)</c> request action n (V: case 0x1454: n 60/61 pick from a list, n &lt; 0 = nothing, check ≠ 0 =
/// refuse when the line to the target is blocked; then the character's action request, vtable+0x80). <c>f5001()</c> = an
/// action is playing (P: every vanilla attack waits <c>f5001() != 1</c>), <c>f5000()</c> = free to act (P), <c>f5008()</c> = combo
/// stages reached (P: vanilla tests only <c>f5008() &gt; k</c> and <c>f5008() &gt;= p3</c> = "p3 stages played" (gated 4× by
/// <c>p3 &gt; 0</c>, 4× by <c>0 &gt; p3</c>); the earlier
/// "−1 before the attack starts" was read through the mislabelled opcode table, chrint stage 3; the value before the first
/// stage is U), <c>f5010()</c> = the request was refused (P),
/// <c>1:5208()</c> = end the combo after the current stage (P: Smelter stop odds), <c>1:5232()</c> before each request (P,
/// vanilla; resets the line-of-sight retry flag, V: case 0x1470 clears +0x95)</item>
/// <item>Moves (V: FUN_140123480 handles 5405/5435/5465/5505/5535/5565/5570): <c>while 1:55x5(avoidRepeat, distMin, distMax,
/// speedMin, speedMax, dir0..dir8, a14, jitter, slowTurn)</c> = walk a random distance in [distMin, distMax] at a random
/// speed in [speedMin, speedMax] in one of the enabled directions; the 55xx ids measure directions from the line to the
/// target (54xx from the own facing); 5535 / 5435 run (flag +0x1c6), 5565 / 5465 / 5570 keep facing the target while moving
/// (strafe set); dir index 1 = toward the target, 2 = away (FUN_14011d8a0 negates the vector), 3 / 4 = the sides. Argument
/// values copied from vanilla: approach walk <c>5505(0,10,10,1,1, 0,1,0,0,0,0,0,0,0, 1.5,0,0)</c> (319 uses), run
/// <c>5535(… 4,0,0)</c> (325 uses), back away facing <c>5565(0,10,10,0.5,0.5, 0,0,1,0,0,0,0,0,0, 3,0,0)</c> (154 uses), side
/// steps <c>5565(… dir 3 or 4 …, 2,0,0)</c>. <c>f5620()</c> = the move is blocked (P: vanilla then path-plans), <c>1:5800(0)</c> +
/// <c>f5600()</c> / <c>f5602()</c> = path search running / found (P), <c>while 1:5506/5536(speed, speed, 0)</c> = follow the path
/// walking / running (P), <c>f5643()</c> = room to keep moving (P: vanilla back-off exit), <c>f5605()</c> = path still valid (P).</item>
/// <item><c>while 1:5903()</c> turn toward the target (P, 1253 uses); <c>f7001()</c> distance to the target (P), <c>f7002()</c> |angle|
/// to it in degrees (P), <c>f7007()</c> its height above us (P: vanilla tests −0.5 … 1.5 for "same floor"), <c>f5200()</c> own HP %
/// (P), <c>f5050(n)</c> interrupt n happened — DS1's INTERUPT numbering (P: the argument range 0-20 matches ai_define.lua's
/// INTERUPT_* list, 7 GuardBegin / 17 ReboundByOpponentGuard abort vanilla combos, 4 FindMissile triggers vanilla
/// dodges, 12 UseItem makes the Smelter punish)</item>
/// <item><c>6:G(args)</c> call machine G (args = p0..p7, at most 8), wait <c>?B9 != ?BA</c>; <c>7:-1(v)</c> return v (V: structure of
/// every vanilla AI ESD)</item>
/// <item><c>1:5206(flag, 0)</c> guard (shield) up / down (fix pass, P: 417 vanilla uses with 1, 762 with 0, the second argument
/// always 0; vanilla reads <c>f5004()</c> right after it as "guard is up"; the template's own approach machine 2147483599 state 5
/// raises it with <c>1:5206(p5, 0)</c> and mode 13 passes p5 = 1). c1030's network plays its guard states with DS1's guard clips
/// (chr track roles: a00_09_7003 Wep03 Guard → DS1 a00_9900, guard hits → a00_2000 / a00_2004).</item>
/// </list>
/// Machines are built with ids handed in by the translator (contiguous below the template's own machines).
/// </summary>
public static class Ds2AiMachines
{
    /// <summary>
    /// DS1 interrupt numbers that are mapped onto <c>f5050(n)</c> (fix pass, rule AI5b). DS1's INTERUPT numbering (ai_define.lua
    /// 0-34) shares DS2's argument range 0-20, and vanilla's reactions to most indices fit DS1's meanings (validate\f5050_profile.py
    /// over the 485 vanilla AI ESDs, the states a condition requiring f5050(n) leads to): 1 FindAttack → guard 65 % / move away 76 %;
    /// 2 Damaged and 3 Damaged_Stranger, 7 GuardBegin, 17 ReboundByOpponentGuard → the "undisturbed" guards (720 / 995 / 774 / 714
    /// clear-tests) and combo stops (7: 39 %); 4 FindMissile → guard 38 %; 9 GuardBreak, 12 UseItem → mode switches; 16
    /// Inside_ObserveArea → attacks 56 %. Indices vanilla never reads — 6 MissSwing, 8 GuardFinish, 11 ShootReady — have no DS2
    /// evidence at all, so they count as "no DS2 form" (false) unless <see cref="UnreadInterrupts"/> is set (build-pack
    /// --ds1-ai-unread-interrupts, for an in-game A/B test). 13 / 14 (DS1 Enter/LeaveBattleArea) and 0 / 19 (FindEnemy /
    /// FriendRequestSupport) have vanilla profiles (combo stops / dodges) that do not prove DS1's meaning; none of our thinks reads them.
    /// </summary>
    public static readonly HashSet<int> VanillaReadInterrupts = [0, 1, 2, 3, 4, 5, 7, 9, 10, 12, 13, 14, 15, 16, 17, 18, 19, 20];
    /// <summary>build-pack --ds1-ai-unread-interrupts: also map DS1 interrupts 6 / 8 / 11, which no vanilla DS2 AI reads.</summary>
    public static bool UnreadInterrupts { get; set; }
    public static bool InterruptHasDs2Form(int n) => n is >= 0 and <= 20 && (UnreadInterrupts || VanillaReadInterrupts.Contains(n));

    /// <summary>
    /// DS2 timer slots for DS1 timers, best first (fix pass, rule AI5c): slots the template lower never resets (in ai103000 only
    /// slot 2: the template resets 0 ×23, 1 ×7, 3 ×53 in its search / return / approach machines, which run whenever the upper
    /// leaves the battle modes, and would restart a DS1 timer), then the least reset ones.
    /// </summary>
    public static List<(int Slot, int Resets)> TimerSlotOrder(ESD lowerTemplate)
    {
        var resets = new int[4];
        foreach (var st in lowerTemplate.StateGroups.Values.SelectMany(g => g.Values))
            foreach (var c in st.EntryCommands.Concat(st.WhileCommands).Concat(st.ExitCommands))
                if (c.CommandBank is 1 or 5 && c.CommandID == 2 && c.Arguments.Count > (c.CommandBank == 5 ? 1 : 0)
                    && AiTranslator.Const(c.Arguments[c.CommandBank == 5 ? 1 : 0]) is int k && k is >= 0 and < 4) resets[k]++;
        return [.. Enumerable.Range(0, 4).OrderBy(k => resets[k]).ThenBy(k => k).Select(k => (k, resets[k]))];
    }

    // functions (P unless noted)
    public static E Dist => E.Fn(7001);
    public static E Angle => E.Fn(7002);
    public static E Height => E.Fn(7007);
    public static E StateTime => E.Fn(3);
    public static E Rand(int slot) => E.Fn(1, E.I(slot));
    public static E Timer(int slot) => E.Fn(2, E.I(slot));
    public static E HpPercent => E.Fn(5200);
    public static E Interrupt(int n) => E.Fn(5050, E.I(n));
    static E Acting => E.Fn(5001);
    static E CanAct => E.Fn(5000);
    static E ComboStage => E.Fn(5008);
    static E Refused => E.Fn(5010);
    static E Blocked => E.Fn(5620);
    static E Room => E.Fn(5643);

    // commands
    public static ESD.CommandCall RollRandom(int slot, int lo, int hi) => C(1, E.I(slot), E.I(lo), E.I(hi));
    public static ESD.CommandCall ResetTimer(int slot) => C(2, E.I(slot));
    /// <summary><c>1:5206(flag, 0)</c>: guard up (1) / down (0).</summary>
    public static ESD.CommandCall Guard(E flag) => C(5206, flag, E.I(0));
    public static ESD.CommandCall GuardDown => Guard(E.I(0));
    static ESD.CommandCall Turn() => C(5903);
    static ESD.CommandCall TurnIf(E cond) => CIf(cond, 5903);
    static ESD.CommandCall Move(int id, float dist, float speed, int dir, float a14, int slowTurn)
    {
        var a = new List<E> { E.I(0), E.Fl(dist), E.Fl(dist), E.Fl(speed), E.Fl(speed) };
        for (int k = 0; k < 9; k++) a.Add(E.I(k == dir ? 1 : 0));
        a.Add(E.Fl(a14)); a.Add(E.I(0)); a.Add(E.I(slowTurn));
        return C(id, [.. a]);
    }
    static ESD.CommandCall WalkTo => Move(5505, 10, 1, 1, 1.5f, 0);
    static ESD.CommandCall RunTo => Move(5535, 10, 1, 1, 4, 0);
    static ESD.CommandCall BackAway => Move(5565, 10, 0.5f, 2, 3, 0);
    static ESD.CommandCall RunAway => Move(5535, 10, 1, 2, 4, 0);
    static ESD.CommandCall Strafe(int dir) => Move(5565, 10, 0.5f, dir, 2, 0);

    /// <summary>The machines a translated goal may call, with the DS1 common goal each implements.</summary>
    public enum Kind { Attack, Approach, Leave, Sideway, Wait, Turn, KeepDist, ApproachStep, Guard, MovePoint }

    public static readonly Dictionary<Kind, string> Doc = new()
    {
        [Kind.Attack] = "CommonAttack (Attack / ComboAttack / ComboRepeat / ComboFinal / *TunableSpin / NonspinningAttack / GuardBreakAttack, SpinStep, …): p0 action, p1 turn time, p2 face angle, p3 stages to play (0 = all), p4 life",
        [Kind.Approach] = "ApproachTarget → MoveToSomewhere: p0 range, p1 walk (1) / run (0), p2 life, p3 guard (DS1 guardStateId 9910 → 1)",
        [Kind.Leave] = "LeaveTarget: p0 distance, p1 walk (1, facing the target) / run (0), p2 life, p3 guard",
        [Kind.Sideway] = "SidewayMove: p0 right (≥ 1) / left, p1 angle (°), p2 life, p3 guard",
        [Kind.Wait] = "Wait / Stay: p0 life; faces the target",
        [Kind.Turn] = "Turn: p0 angle (°), p1 life",
        [Kind.KeepDist] = "KeepDist: p0 min, p1 max, p2 walk, p3 life, p4 guard",
        [Kind.ApproachStep] = "ApproachStep: p0 step action, p1 range, p2 life (repeated steps toward the target)",
        [Kind.Guard] = "Guard (DS1 GOAL_COMMON_Guard, guard state 9910): p0 life; shield up, faces the target",
        [Kind.MovePoint] = "session 11 (ret11), rule A1: ApproachTarget / MoveToSomewhere to POINT_EVENT (DS1 SetEventMoveTarget): p0 EventLocation, p1 arrival range, p2 life (1:7302 / while 1:7005(p0, -1, 100, 0, 0) until f7072(p0, p1) != 1, exit 5:7301: the boss translator's move-to-point, vanilla Ruin Sentinels' form)",
    };

    static IEnumerable<ESD.Condition> Flat(IEnumerable<ESD.Condition> cs) => cs.SelectMany(c => new[] { c }.Concat(Flat(c.Subconditions)));

    /// <summary>Builds machine <paramref name="kind"/> as state group <paramref name="id"/>; <paramref name="attackId"/> = the
    /// Attack machine's id (ApproachStep calls it).</summary>
    public static Dictionary<long, ESD.State> Build(Kind kind, long attackId)
    {
        var st = new Dictionary<long, ESD.State>();
        const long Ok = 90, Fail = 91;
        st[Ok] = ReturnState(0);
        st[Fail] = ReturnState(1);
        // Rule AI-OP (chrint stage 3 + fix pass): a comparison that implements DS1 code takes DS1's operator (goal life
        // "StateTime >= p", turn time, face angle, distance bands, the Sideway arc, SidewayMove's side); a comparison copied
        // from a vanilla DS2 pattern keeps vanilla's operator = its bytes. Vanilla-derived here: the fixed waits 0.5 / 5 s
        // (Smelter ai305001 machine 2147483646: "f3() > 0.5" request wait, "f3() > 5" fail wait), the 3 s path-search
        // timeout (ai103000 machine 2147483604: "f2(0) > 3") and the stand-and-face turn gate "5:5903(f7002() > 15)"; the
        // 485 vanilla AI ESDs compare f3() / f7002() with a constant or a parameter only by 0x92 ">" (f3() 30,050 uses,
        // f7002() 4,509; 0 ">=", work\ai3\op_survey.py, logs\ai3fix\v-op-survey.log), so these are written Gt (= the
        // stage-2 bytes, 0x92).
        switch (kind)
        {
            case Kind.Attack:
                // DS1 CommonAttack: turn toward the target for up to turnTime until within turnFaceAngle, then request the
                // EzState (here: the DS2 action of the DS1 attack / chain); structure after the Smelter's attack machine
                st[0] = S(null, null, Go(1));
                st[1] = S([C(5232)], null,
                    Go(2, E.P(1).Gt(0) & Angle.Ge(E.P(2))),
                    Go(3, CanAct.Ne(0)),
                    Go(Fail, StateTime.Gt(5)));
                st[2] = S(null, [Turn()],
                    Go(10, Angle.Lt(E.P(2))),
                    Go(10, StateTime.Ge(E.P(1))));
                st[10] = S(null, null,
                    Go(3, CanAct.Ne(0)),
                    Go(Fail, StateTime.Gt(5)));
                st[3] = S([C(5204, E.P(0), E.I(0))], null,
                    Go(4, StateTime.Gt(0.5f)),
                    Go(Fail, Refused.Ne(0)));
                // combo stage tests keep vanilla's operator (rule AI-OP): the 485 vanilla AI ESDs compare f5008() as
                // "f5008() > k" (k 0-3: 276 uses; "> p0": 50, ungated) and "f5008() >= p3" (8 uses, 2 each in the attack
                // machines of ai135000, ai308000, ai511000, ai704000): 4 gated by "p3 > 0" (= "stop after p3 stages", the
                // form used here) and 4 by "0 > p3" (a vanilla p3 < 0 mode we never emit); never "< ", "<=" or -1
                // (work\ai3\f5008_survey.py, logs\ai3\f5008-survey.txt). The Smelter's attack machine (ai305001
                // 2147483646 state 3) waits "f5008() > 0". Before chrint stage 3 these two were written Ge(0) /
                // Ge(p3 - 1) with the mislabelled table, i.e. already the bytes 0x92 (>), kept; "> p3 - 1" == ">= p3" on
                // integer stages. The gate "p3 > 0" (0 = play every stage) was Gt with the old table = 0x94 (>=, always
                // true: a chain asked to play every stage was stopped once f5008() > -1); now 0x92 as vanilla's gate
                st[4] = S(null, null,
                    Go(5, ComboStage.Gt(0)),
                    Go(Ok, Acting.Ne(1)),
                    Go(7, StateTime.Ge(E.P(4))));
                st[5] = S(null, null,
                    Go(7, E.P(3).Gt(0) & ComboStage.Gt(E.P(3) - E.I(1))),
                    Go(Ok, Acting.Ne(1)),
                    Go(7, StateTime.Ge(E.P(4))));
                st[7] = S([C(5208)], null,
                    Go(Ok, Acting.Ne(1)),
                    Go(Ok, StateTime.Gt(5)));
                break;
            case Kind.Approach:
                // DS1 ApproachTarget: MoveToSomewhere to within range, walking or running (Approach_Act decided it), life →
                // failure. Blocked → DS2's path move (Hollow Infantry ai103000 machine 2147483604 states 6-8). DS1's guardStateId
                // (9910: shield up while moving) → 1:5206(p3, 0), as the template's approach machine does with its p5
                // session 11 (track enemy11, G5 AI-F9): DS1 ApproachTarget_Update returns GOAL_RESULT_Failed when the approach is
                // guarded (guardActionId > 0) and IsInterupt(INTERUPT_Damaged = 2) - checked before arrival (approach_target.lua)
                var hitGuarded = E.P(3).Eq(1) & Interrupt(2).Ne(0);
                st[0] = S(null, null, Go(1));
                st[1] = S([Guard(E.P(3))], null,
                    Go(Fail, hitGuarded),
                    Go(Ok, Dist.Le(E.P(0))),
                    Go(2, E.P(1).Eq(1)),
                    Go(3));
                st[2] = S(null, [WalkTo],
                    Go(Fail, hitGuarded),
                    Go(Ok, Dist.Le(E.P(0))),
                    Go(4, Blocked.Ne(0)),
                    Go(Fail, StateTime.Ge(E.P(2))));
                st[3] = S(null, [RunTo],
                    Go(Fail, hitGuarded),
                    Go(Ok, Dist.Le(E.P(0))),
                    Go(4, Blocked.Ne(0)),
                    Go(Fail, StateTime.Ge(E.P(2))));
                st[4] = S([C(5800, E.I(0))], null,
                    Go(Fail, hitGuarded),
                    Go(5, E.Fn(5600).Ne(1) & E.Fn(5602).Ne(0) & E.P(1).Eq(1)),
                    Go(6, E.Fn(5600).Ne(1) & E.Fn(5602).Ne(0)),
                    Go(Fail, E.Fn(5600).Ne(1)),
                    Go(Fail, StateTime.Gt(3)));
                st[5] = S(null, [C(5506, E.I(1), E.I(1), E.I(0))],
                    Go(Fail, hitGuarded),
                    Go(Ok, Dist.Le(E.P(0))),
                    Go(Fail, Room.Ne(1)),
                    Go(Fail, E.Fn(5605).Ne(1)),
                    Go(Fail, StateTime.Ge(E.P(2))));
                st[6] = S(null, [C(5536, E.I(1), E.I(1), E.I(0))],
                    Go(Fail, hitGuarded),
                    Go(Ok, Dist.Le(E.P(0))),
                    Go(Fail, Room.Ne(1)),
                    Go(Fail, E.Fn(5605).Ne(1)),
                    Go(Fail, StateTime.Ge(E.P(2))));
                break;
            case Kind.Leave:
                // DS1 LeaveTarget: back off to the distance (walking, facing the target) or run away; no room or life → done;
                // guard as in Approach (p3)
                st[0] = S(null, null, Go(1));
                st[1] = S([Guard(E.P(3))], null,
                    Go(Ok, Dist.Ge(E.P(0))),
                    Go(2, E.P(1).Eq(1)),
                    Go(3));
                st[2] = S(null, [BackAway],
                    Go(Ok, Dist.Ge(E.P(0))),
                    Go(Ok, Room.Ne(1)),
                    Go(Fail, StateTime.Ge(E.P(2))));
                st[3] = S(null, [RunAway],
                    Go(Ok, Dist.Ge(E.P(0))),
                    Go(Ok, Blocked.Ne(0)),
                    Go(Fail, StateTime.Ge(E.P(2))));
                break;
            case Kind.Sideway:
                // DS1 SidewayMove: circle the target by an angle. DS2 has no swept-angle query: walk sideways for the arc length
                // at DS1's side-walk speed (a00_0202 / a00_0203: 0.67 / 0.99 m/s → 0.8), t = dist · angle · π/180 / 0.8 (P)
                st[0] = S(null, null, Go(1));
                st[1] = S([Guard(E.P(3))], null, Go(3, E.P(0).Ge(1)), Go(2));
                foreach (var (sid, dir) in new[] { (2L, 3), (3L, 4) })
                    st[sid] = S(null, [Strafe(dir)],
                        Go(Ok, StateTime.Ge(Dist * E.P(1) * E.Fl(0.0218f))),
                        Go(Ok, Room.Ne(1)),
                        Go(Ok, StateTime.Ge(E.P(2))));
                break;
            case Kind.Wait:
                // DS1 Wait(life, target): stand, facing the target (P)
                st[0] = S(null, null, Go(1));
                st[1] = S(null, [TurnIf(Angle.Gt(15))], Go(Ok, StateTime.Ge(E.P(0))));
                break;
            case Kind.Turn:
                // DS1 Turn: TurnTo until IsLookToTarget(angle_range), angle_range ≤ 0 → 10 (turn.lua)
                st[0] = S(null, null, Go(1));
                st[1] = S(null, [Turn()],
                    Go(Ok, Angle.Le(E.P(0))),
                    Go(Ok, StateTime.Ge(E.P(1))));
                break;
            case Kind.Guard:
                // DS1 GOAL_COMMON_Guard(life, 9910, target): stand with the shield up, facing the target, for life seconds; the
                // calling step state lowers the guard on exit (also when an interrupt abandons it)
                st[0] = S(null, null, Go(1));
                st[1] = S([Guard(E.I(1))], [TurnIf(Angle.Gt(15))], Go(Ok, StateTime.Ge(E.P(0))));
                break;
            case Kind.KeepDist:
                st[0] = S(null, null, Go(1));
                st[1] = S([Guard(E.P(4))], null,
                    Go(2, Dist.Lt(E.P(0))),
                    Go(3, Dist.Gt(E.P(1)) & E.P(2).Eq(1)),
                    Go(4, Dist.Gt(E.P(1))),
                    Go(Ok));
                st[2] = S(null, [BackAway], Go(Ok, Dist.Ge(E.P(0))), Go(Ok, Room.Ne(1)), Go(Fail, StateTime.Ge(E.P(3))));
                st[3] = S(null, [WalkTo], Go(Ok, Dist.Le(E.P(1))), Go(Fail, Blocked.Ne(0)), Go(Fail, StateTime.Ge(E.P(3))));
                st[4] = S(null, [RunTo], Go(Ok, Dist.Le(E.P(1))), Go(Fail, Blocked.Ne(0)), Go(Fail, StateTime.Ge(E.P(3))));
                break;
            case Kind.ApproachStep:
                st[0] = S(null, null, Go(1));
                st[1] = S(null, null, Go(Ok, Dist.Le(E.P(1))), Go(2));
                st[2] = S([Call(attackId, E.P(0), E.I(0), E.I(180), E.I(0), E.I(3))], null, [.. Returned(1, Fail)]);
                break;
            case Kind.MovePoint:
            {
                // session 11 (track ret11), rule A1 (asy10 enemies.md §3): DS1 ApproachTarget / MoveToSomewhere to POINT_EVENT (the
                // point ai:SetEventMoveTarget named) → the boss translator's move-to-point (Ds2AiEsd "MoveToSomewhere", vanilla Ruin
                // Sentinels ai325000): entry 1:7302, while 1:7005(point, -1, 100, 0, 0), arrived when f7072(point, range) != 1, exit
                // 5:7301 unless f7302() == 1; life → failure
                st[0] = S(null, null, Go(1));
                var mv = S([C(7302)], [C(7005, E.P(0), E.I(-1), E.I(100), E.I(0), E.I(0))],
                    Go(Ok, E.Fn(7072, E.P(0), E.P(1)).Ne(1)),
                    Go(Fail, StateTime.Ge(E.P(2))));
                mv.ExitCommands.Add(CIf(E.Fn(7302).Ne(1), 7301));
                st[1] = mv;
                break;
            }
        }
        // drop states nothing reaches (a machine without a failure exit has no state 91)
        var reached = new HashSet<long> { 0 };
        var todo = new Stack<long>([0]);
        while (todo.Count > 0)
            foreach (var c in Flat(st[todo.Pop()].Conditions))
                if (c.TargetState is long t && reached.Add(t)) todo.Push(t);
        foreach (var k in st.Keys.Where(k => !reached.Contains(k)).ToList()) st.Remove(k);
        return Renumber(st);
    }
}
