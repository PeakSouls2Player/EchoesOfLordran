using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SoulsFormats;
using M = Lordran.Tools.Build.Ai.Ds2AiMachines;

namespace Lordran.Tools.Build.Ai;

/// <summary>
/// DS1 Lua AI → DS2 AI (session 8, track enemies step 2; user directive: DS1's own AI translated, not re-authored). One DS1
/// think (NpcThinkParam: logic 10000 + battle goal N) becomes a DS2 logic pair:
/// <list type="bullet">
/// <item><b>Upper logic</b> (perception / mode switching) = the DS2 template's upper ESD copied unchanged as
/// <c>ai{High}.esd</c> (only its internal name changed) with a LogicComParam row <c>High</c> holding DS1's perception numbers (<see cref="LogicRow"/>). Rule:
/// DS1's logic 10000 (COMMON_EasySetup3: no target → NonBattleAct, target → the battle goal) is DS1's generic perception logic;
/// DS2's template upper is DS2's generic one — duplicates use DS2's version, fed DS1's data.</item>
/// <item><b>Lower logic</b> = the template's lower ESD (<c>ai{Low}.esd</c>) with every weapon group's engaged modes (the states
/// that run while the upper's mode board <c>f5900(0)</c> is 10 or 17, see <see cref="PatchTemplate"/>) calling the generated
/// battle machine (<see cref="GoalCompiler"/>, split by <see cref="GroupSplit"/>) instead of the template's, plus the shared
/// machines (<see cref="Ds2AiMachines"/>) as new state groups numbered contiguously below the template's own. The other modes
/// keep DS2's behaviour — return to the generator (13 / 20), search after losing the target (12 / 14 / 18), the engine's stop
/// (11), ladders — with their DS2 attack requests switched off (action −1, which vanilla passes to these machines itself):
/// DS1 enemies attack only through their battle goal, and DS1's NonBattle / caution states never attack either.</item>
/// </list>
/// Ids: High = the DS1 think id (DS1's regular thinks are 2xxxxx; DS2 has no AI or LogicComParam in 228000-299999), Low =
/// think + 200000 (DS2 has no AI ids 428000-499999; outside DLC ranges 650000-699999 and &gt; 810000).
/// </summary>
public static class AiTranslator
{
    public static int HighId(int think) => think is >= 228000 and < 300000 ? think : throw new InvalidDataException($"DS1 think {think}: no DS2 upper-logic id rule for it yet (rule covers 228000-299999)");
    /// <summary>Lower logic per DS1 think, not per battle goal: a goal may branch on its think (250005_battle: think 250021's archer
    /// flees from region 1812101, 250020's does not).</summary>
    public static int LowId(int think) => think is >= 228000 and < 300000 ? think + 200000 : throw new InvalidDataException($"DS1 think {think}: no DS2 lower-logic id rule for it yet (rule covers 228000-299999)");

    public sealed class Output
    {
        public int Think, BattleGoal, High, Low, TemplateHigh, TemplateLow;
        public byte[] LowerEsd = [], UpperEsd = [];
        public GoalCompiler.Coverage Coverage = null!;
        public List<string> Report = [];
        public long BattleMachine;
        /// <summary>The battle machine's groups after <see cref="GroupSplit"/> (main first).</summary>
        public List<long> BattleGroups = [];
        public GroupSplit.Result Split = null!;
        /// <summary>The battle machine before the split (one group, ids 0..N-1) and its think state (for ai-verify).</summary>
        public Dictionary<long, ESD.State> Machine = [];
        public long ThinkState;
        public Dictionary<M.Kind, long> Machines = [];
        public Ds1Goal Goal = null!;
        public GoalCompiler Compiler = null!;
        public Ds1Goal.Run Activate = null!;
        public Ds1Goal.Run? Interupt;
    }

    /// <summary>Translate DS1 think <paramref name="think"/> (row of DSR's NpcThinkParam) for a character whose DS2 action
    /// numbers are <paramref name="map"/>, on DS2 template logic <paramref name="templateHigh"/> / <paramref name="templateLow"/>
    /// (ESD files in <paramref name="ezstateDir"/>).</summary>
    public static Output Translate(PARAM.Row think, GoalCompiler.ActionMap map, int templateHigh, int templateLow, string ezstateDir, string? luaRoot = null, string? ds1Map = null)
    {
        int goalId = Convert.ToInt32(think["battleGoalID"]!.Value, CultureInfo.InvariantCulture);
        var o = new Output { Think = think.ID, BattleGoal = goalId, High = HighId(think.ID), Low = LowId(think.ID), TemplateHigh = templateHigh, TemplateLow = templateLow };
        var goal = new Ds1Goal(goalId, think, luaRoot, ds1Map);
        o.Goal = goal;
        string upperPath = Path.Combine(ezstateDir, $"ai{templateHigh:D6}.esd"), lowerPath = Path.Combine(ezstateDir, $"ai{templateLow:D6}.esd");
        // the template upper unchanged but for its internal name, which every vanilla AI ESD starts with its own id
        // ("ai103010_上位：下級兵士（亡者）：汎用"); content-identical through SoulsFormats (ai-roundtrip: all 485 vanilla AI ESDs)
        var upper = ESD.Read(upperPath);
        upper.Name = $"ai{o.High:D6}_DS1 think {think.ID} perception on ai{templateHigh:D6}";
        o.UpperEsd = upper.Write();
        var lower = ESD.Read(lowerPath);

        // ids: contiguous below the template's lowest machine
        long lowest = lower.StateGroups.Keys.Where(k => k >= 2_000_000_000).DefaultIfEmpty(2147483648).Min();
        long nextId = lowest - 1;
        foreach (var kind in Enum.GetValues<M.Kind>().Where(k => k != M.Kind.MovePoint)) o.Machines[kind] = nextId--;
        o.BattleMachine = nextId--;
        // session 11 (ret11), rule A1: the move-to-point machine sits 1000 ids below the battle machine (its split groups take the ids
        // right below it), so the thinks that never move to a point keep every group id they had
        o.Machines[M.Kind.MovePoint] = o.BattleMachine - 1000;

        // DS1 → symbolic paths → battle machine
        var act = goal.Evaluate(goal.ActivateName);
        var intr = goal.HasFunction(goal.InteruptName) ? goal.Evaluate(goal.InteruptName) : null;
        var comp = new GoalCompiler(goal, map, o.Machines, M.TimerSlotOrder(lower));
        var res = comp.Compile(act, intr);
        o.Coverage = comp.Cov;
        o.Compiler = comp; o.Activate = act; o.Interupt = intr;
        // the vanilla envelope: at most 90 states per group (rule AI10b)
        o.Machine = res.States; o.ThinkState = res.Entry;
        o.Split = GroupSplit.Split(res.States, res.Entry, o.BattleMachine, () => nextId--);
        o.BattleGroups = [o.BattleMachine, .. o.Split.Groups.Keys.Where(k => k != o.BattleMachine).OrderDescending()];
        // template patch first (engaged modes → our machine, DS2's own attack requests → −1), then our groups
        o.Report.AddRange(PatchTemplate(lower, o.BattleMachine, map));
        // only the machines this think calls (the Attack machine always: ApproachStep calls it, and ai-check knows it as the highest id),
        // so a big translation stays inside vanilla's 133 groups per file
        var called = res.States.Values.SelectMany(s => s.EntryCommands).Where(c => c.CommandBank == 6).Select(c => (long)(uint)c.CommandID).ToHashSet();
        foreach (var kind in o.Machines.Keys.Where(k => k != M.Kind.Attack && !called.Contains(o.Machines[k])).ToList()) o.Machines.Remove(kind);
        foreach (var (kind, id) in o.Machines) lower.StateGroups[id] = M.Build(kind, o.Machines[M.Kind.Attack]);
        foreach (var (gid, g) in o.Split.Groups) lower.StateGroups[gid] = g;
        // its own internal name (vanilla: "ai103000_下級兵士（亡者）"), so no two of our lowers carry the template's — in case the
        // engine keys loaded scripts by that name
        lower.Name = $"ai{o.Low:D6}_DS1 think {think.ID} battle goal {goalId} on ai{templateLow:D6}";
        o.LowerEsd = lower.Write();
        o.Report.Insert(0, $"DS1 think {think.ID} → battle goal {goalId} ({Path.GetFileName(goal.ScriptPath)}, {goal.GoalName}): {act.Paths.Count} think paths, {intr?.Paths.Count ?? 0} interrupt paths → "
            + $"battle machine {o.BattleMachine} ({res.States.Count} states in {o.BattleGroups.Count} group(s) of ≤ {o.Split.MaxGroupStates}, {comp.Cov.Regions} decision regions, {comp.Cov.Leaves} sub-goal queues); upper ai{o.High:D6} = ai{templateHigh:D6}, lower ai{o.Low:D6} = ai{templateLow:D6} + {o.Machines.Count + o.BattleGroups.Count} groups ({o.Machines.Count} machines: {string.Join(", ", o.Machines.Keys)})");
        return o;
    }

    static readonly Regex ModeTest = new(@"f5900\(0\) == (\d+)\) != 1", RegexOptions.Compiled);

    /// <summary>The upper's modes whose lower states run the DS1 battle machine (fix pass, rule AI3b): 10 (battle) and 17 (the
    /// template's chase of a known target along a path, entered from 10 on f7120 / an unreachable-target test and left for 10
    /// when <c>f7310(0, 0) ∧ f7080 ≠ 1</c>, its lower machine 2147483625 = path search + 2147483622 path approach + attack). DS1
    /// runs its battle goal for as long as it has a target inside its leash (COMMON_EasySetup3 → _COMMON_SetBattleActLogic), so
    /// both are DS1 battle; a 10 ↔ 17 switch no longer restarts DS1's think. Kept as DS2's (evidence in the template dumps):
    /// 13 = return to the generator (lower 2147483599: path to home with 5801, back away facing the target with the guard up via
    /// 5566 until the target is &gt; f5662 away or f5663 s passed — LogicComParam genReturnDist 20 / genReturnTime 10 — then walk
    /// home; at home (f7010 ≤ 3) turn, wait, then search; the upper enters 13 from the leash tests f7094 ≤ 0 / f7015 &gt; 80-95
    /// (comparisons as DS2 runs the template's bytes, chrint stage 3: 0x92 = &gt;)
    /// and leaves it only when the target closes within 3 m or hits it), 20 = retreat toward home (2147483621; unreachable in
    /// ai103010), 12 / 14 = searching after losing the target (random walks), 18 = going to a noise (5807 path), 11 = the
    /// engine's stop request (5208 then stand, 3 s; on f7301(4)). DS1's own counterparts of these (backhome, caution / find
    /// goals, NonBattleAct) never attack, which is what the −1 patch gives them.</summary>
    public static readonly int[] EngagedModes = [10, 17];

    /// <summary>
    /// Template lower patch (rule AI3): (1) in the weapon groups (ids &lt; 100) the states of the engaged modes
    /// (<see cref="EngagedModes"/>: left when the mode board <c>f5900(0)</c> leaves 10 / 17) call <paramref name="battle"/>, and
    /// their mode test becomes "neither 10 nor 17" so a switch between them keeps DS1's battle machine running; (2) every other
    /// DS2 attack request in the template — constant first arguments of <c>1:5204</c> / <c>5:5204</c>, and machine-call arguments
    /// that only ever reach 5204 or a <c>pN == -1</c> test (<see cref="ActionParams"/>) — becomes −1 (the 5204 handler ignores
    /// n &lt; 0, V: case 0x1454; vanilla machines treat −1 as "no attack": ai103000 2147483633 returns at once).
    /// </summary>
    public static List<string> PatchTemplate(ESD lower, long battle, GoalCompiler.ActionMap map)
    {
        var rep = new List<string>();
        var actionParams = ActionParams(lower);
        int battles = 0, offed = 0;
        var notEngaged = E.And(EngagedModes.Select(m => E.Fn(5900, E.I(0)).Eq(m).Ne(1))).Bytes();
        // the weapon groups: ids < 100 that dispatch on the mode board (ai103000: 10-50; group 90 does not)
        foreach (var (gid, sts) in lower.StateGroups.Where(g => g.Key is > 0 and < 100 && g.Value.Values.Any(s => s.Conditions.Any(c => ModeTest.IsMatch(Commands.Esd.Expr(c.Evaluator))))).OrderBy(g => g.Key))
        {
            int found = 0;
            foreach (var (sid, st) in sts.OrderBy(s => s.Key))
            {
                var modeConds = st.Conditions.Where(c => ModeTest.Match(Commands.Esd.Expr(c.Evaluator)).Success).ToList();
                if (modeConds.Count != 1) continue;
                int mode = int.Parse(ModeTest.Match(Commands.Esd.Expr(modeConds[0].Evaluator)).Groups[1].Value);
                if (!EngagedModes.Contains(mode)) continue;
                var calls = st.EntryCommands.Where(c => c.CommandBank == 6).ToList();
                if (mode == 10 && (calls.Count != 1 || calls[0].Arguments.Count != 0)) continue;   // the battle state has exactly one machine call without arguments
                string was = string.Join("; ", st.EntryCommands.Select(c => $"{c.CommandBank}:{(c.CommandBank == 6 ? (uint)c.CommandID : (long)c.CommandID)}({c.Arguments.Count} args)"));
                st.EntryCommands.Clear();
                st.EntryCommands.Add(Esb.Call(battle));
                modeConds[0].Evaluator = notEngaged;
                rep.Add($"group {gid} state {sid}: mode {mode} [{was}] → 6:{battle}() (DS1 battle goal), left when f5900(0) is neither {string.Join(" nor ", EngagedModes)}");
                battles++; found++;
            }
            if (found != EngagedModes.Length) throw new InvalidDataException($"template lower group {gid}: {found} engaged-mode states found (expected {EngagedModes.Length})");
        }
        foreach (var (gid, sts) in lower.StateGroups.OrderBy(g => g.Key))
        {
            if (gid == battle) continue;
            foreach (var (sid, st) in sts.OrderBy(s => s.Key))
                foreach (var c in st.EntryCommands.Concat(st.WhileCommands).Concat(st.ExitCommands))
                {
                    if (c.CommandID == 5204 && c.CommandBank is 1 or 5)
                    {
                        int k = c.CommandBank == 5 ? 1 : 0;
                        if (c.Arguments.Count > k && Const(c.Arguments[k]) is int a && a >= 0)
                        {
                            c.Arguments[k] = E.I(-1).Bytes(); offed++;
                            rep.Add($"group {gid} state {sid}: DS2 request {c.CommandBank}:5204({a}) → -1");
                        }
                    }
                    else if (c.CommandBank == 6 && actionParams.TryGetValue((long)(uint)c.CommandID, out var ps))
                        foreach (int k in ps)
                            if (k < c.Arguments.Count && Const(c.Arguments[k]) is int a && a >= 0)
                            {
                                c.Arguments[k] = E.I(-1).Bytes(); offed++;
                                rep.Add($"group {gid} state {sid}: 6:{(uint)c.CommandID} attack argument p{k} = {a} → -1");
                            }
                }
        }
        rep.Add($"template patch: {battles} engaged-mode states (modes {string.Join("/", EngagedModes)}) → DS1 battle machine, {offed} DS2 attack requests → -1");
        if (battles == 0) throw new InvalidDataException("template lower logic: no battle-mode state found (f5900(0) == 10 with a machine call)");
        return rep;
    }

    /// <summary>Per machine: the parameters that are attack action numbers — used only as 5204's action, in a
    /// <c>pN == -1</c> / <c>pN != -1</c> test, or passed on to another machine's action parameter (fixed point).</summary>
    public static Dictionary<long, HashSet<int>> ActionParams(ESD esd)
    {
        // uses[(machine, k)] = list of (kind, callee, position)
        var res = new Dictionary<long, HashSet<int>>();
        var bad = new HashSet<(long, int)>();
        var passes = new List<(long M, int K, long Callee, int J)>();
        var used = new HashSet<(long, int)>();
        var reach = new HashSet<(long, int)>();
        foreach (var (gid, sts) in esd.StateGroups)
            foreach (var st in sts.Values)
            {
                foreach (var c in st.EntryCommands.Concat(st.WhileCommands).Concat(st.ExitCommands))
                    for (int j = 0; j < c.Arguments.Count; j++)
                    {
                        var b = c.Arguments[j];
                        bool isAction = c.CommandID == 5204 && (c.CommandBank == 1 && j == 0 || c.CommandBank == 5 && j == 1);
                        bool isNoneTest = c.CommandBank == 5 && j == 0 && Regex.IsMatch(Commands.Esd.Expr(b), @"^\(\(p\d+ == -1\) != 1\)$");
                        foreach (int k in ParamRefs(b))
                        {
                            used.Add((gid, k));
                            if (isAction && b.Length == 3) { reach.Add((gid, k)); continue; }
                            if (isNoneTest) continue;
                            if (c.CommandBank == 6 && b.Length == 3) { passes.Add((gid, k, (long)(uint)c.CommandID, j)); continue; }
                            bad.Add((gid, k));
                        }
                    }
                foreach (var cond in Flatten(st.Conditions))
                {
                    string e = Commands.Esd.Expr(cond.Evaluator);
                    foreach (int k in ParamRefs(cond.Evaluator))
                    {
                        used.Add((gid, k));
                        if (Regex.IsMatch(e, $@"^\(\(p{k} == -1\) [!=]= [01]\)$")) continue;
                        bad.Add((gid, k));
                    }
                }
            }
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var p in passes)
            {
                if (!bad.Contains((p.M, p.K)) && (bad.Contains((p.Callee, p.J)) || !used.Contains((p.Callee, p.J)) && !esd.StateGroups.ContainsKey(p.Callee)))
                { bad.Add((p.M, p.K)); changed = true; }
                if (reach.Contains((p.Callee, p.J)) && reach.Add((p.M, p.K))) changed = true;
            }
        }
        foreach (var (m, k) in used)
        {
            // an attack parameter: every use is an action / a −1 test / a pass-on, and some path reaches a 5204
            if (bad.Contains((m, k)) || !reach.Contains((m, k))) continue;
            if (!res.TryGetValue(m, out var set)) res[m] = set = [];
            set.Add(k);
        }
        return res;
    }

    static IEnumerable<ESD.Condition> Flatten(IEnumerable<ESD.Condition> cs) => cs.SelectMany(c => new[] { c }.Concat(Flatten(c.Subconditions)));

    static IEnumerable<int> ParamRefs(byte[] b)
    {
        for (int i = 0; i + 1 < b.Length; i++)
        {
            if (b[i] is 0x80 or 0x82) { i += 4; continue; }
            if (b[i] == 0x81) { i += 8; continue; }
            if (b[i] < 0x80 && b[i + 1] == 0xB8) yield return b[i] - 0x40;
        }
    }

    /// <summary>A 1:1 character's DS2 action numbers (step 1's attack plan; null = no attacks, e.g. the unarmed Hollow).</summary>
    public static GoalCompiler.ActionMap MapOf(Chr.Ds1Attacks.Plan? plan) => plan == null
        ? new GoalCompiler.ActionMap([], [])
        : new GoalCompiler.ActionMap(plan.Attacks.ToDictionary(a => a.Id, a => plan.ActionOf(a.Id)), [.. plan.Chains.Select((c, k) => new GoalCompiler.Chain(k, c.Ids))]);

    public static int? Const(byte[] b)
    {
        if (b.Length == 2 && b[0] < 0x80 && b[1] == 0xA1) return b[0] - 0x40;
        if (b.Length == 6 && b[0] == 0x82 && b[5] == 0xA1) return BitConverter.ToInt32(b, 1);
        return null;
    }

    /// <summary>
    /// LogicComParam row for DS1 think <paramref name="think"/> on the template's row (P):
    /// viewingDistanceMax = DS1 eye_dist; viewingDistanceMin keeps the template's Min/Max ratio (13/20: what Min means in DS2 —
    /// sight in darkness? DS2 rows also carry nightBlindRate / viewingBrightnessType — is unknown, so DS1's BattleStartDist is
    /// not mapped onto it); viewingAngleLR / UD = DS1 eye_angY / eye_angX ÷ 2 (DS1 gives the full
    /// cone, DS2 half-angles in radians: the template's 1.0472 / 0.7854 = 60° / 45°); chaseDistance = DS1 maxBackhomeDist,
    /// genReturnDist / genReturnTime = DS1 BackHome_LookTargetDist / _Time (session 11, P-07); noLOSForgetTime = DS1 SightTargetForgetTime / 30 (frames → s); item / guard-break /
    /// back-attack pursuit tables = 6 (the value of 338 of 347 vanilla rows: off — DS1 reacts through its battle goal's
    /// interrupts only). hearingDistance stays DS2's (1 on nearly every row: a factor, not DS1's ear_dist metres).
    /// </summary>
    public static void LogicRow(PARAM.Row row, PARAM.Row think, List<string> notes)
    {
        double D(string f) => Convert.ToDouble(think[f]!.Value, CultureInfo.InvariantCulture);
        void Set(string f, double v) { var c = row[f] ?? throw new InvalidDataException($"LogicComParam: no field {f}"); c.Value = Convert.ChangeType(v, c.Value.GetType(), CultureInfo.InvariantCulture); }
        double eye = D("eye_dist");
        double ratio = Convert.ToDouble(row["viewingDistanceMin"]!.Value, CultureInfo.InvariantCulture) / Math.Max(1e-3, Convert.ToDouble(row["viewingDistanceMax"]!.Value, CultureInfo.InvariantCulture));
        Set("viewingDistanceMax", eye);
        Set("viewingDistanceMin", Math.Round(eye * ratio, 2));
        Set("viewingAngleLR", D("eye_angY") / 2 * Math.PI / 180);
        Set("viewingAngleUD", D("eye_angX") / 2 * Math.PI / 180);
        Set("chaseDistance", D("maxBackhomeDist"));
        // session 11 (track enemy11, G7 P-07 / AI-F12): the template's return mode backs away facing the target until it is
        // > genReturnDist away or genReturnTime s pass - DS1's counterpart is NpcThinkParam BackHome_LookTargetDist / _Time
        // (face the target while going home if it is within that distance, for that time; 20 m / 20 s on every ported think),
        // not backhomeDist (50 m: the go-home threshold, which made the facing back-pedal always run its full time)
        Set("genReturnDist", D("BackHome_LookTargetDist"));
        Set("genReturnTime", D("BackHome_LookTargetTime"));
        Set("noLOSForgetTime", D("SightTargetForgetTime") / 30.0);
        foreach (var t in new[] { "itemPursuitTable", "guardBreakPursuitTable", "backAttackTable" }) Set(t, 6);
        notes.Add($"LogicComParam {row.ID} (DS1 think {think.ID}): sight {eye} m (min {Math.Round(eye * ratio, 2)} m; DS1 BattleStartDist {D("BattleStartDist")} m not mapped), cone {D("eye_angY")}° × {D("eye_angX")}°, chase {D("maxBackhomeDist")} m, return-facing {D("BackHome_LookTargetDist")} m / {D("BackHome_LookTargetTime")} s (BackHome_LookTarget), forget {D("SightTargetForgetTime") / 30.0:0.#} s, pursuit tables off");
    }

    static string F(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
    /// <summary>Evaluator bytes of a condition with its subconditions (what the state costs per frame while it waits).</summary>
    public static int CondBytes(ESD.Condition c) => c.Evaluator.Length + c.Subconditions.Sum(CondBytes);

    /// <summary>The coverage report of one translation: DS1 constructs translated / skipped / assumed.</summary>
    public static string CoverageText(Output o)
    {
        var sb = new StringBuilder();
        var c = o.Coverage;
        sb.AppendLine($"## DS1 think {o.Think} → battle goal {o.BattleGoal} ({o.Goal.GoalName}) → ai{o.High:D6} (upper, template ai{o.TemplateHigh:D6}) + ai{o.Low:D6} (lower, template ai{o.TemplateLow:D6})");
        sb.AppendLine($"paths {c.Paths}, decision regions {c.Regions}, sub-goal queues {c.Leaves}, battle-machine states {c.States} in {o.BattleGroups.Count} group(s) [{string.Join(", ", o.BattleGroups.Select(g => $"{g}: {o.Split.Groups[g].Count}"))}]");
        var st = o.Split.Groups.Values.SelectMany(g => g.Values).ToList();
        int Bytes(ESD.State s) => s.Conditions.Sum(CondBytes);
        sb.AppendLine($"envelope (vanilla max 93 states/group, 31 conditions and 1768 evaluator bytes per state): largest group {o.Split.MaxGroupStates} states, "
            + $"most conditions in a state {st.Max(s => s.Conditions.Count)}, most evaluator bytes in a state {st.Max(Bytes)}");
        sb.AppendLine($"interrupts compiled (DS1 check order, one per frame): [{string.Join(", ", o.Compiler.InterruptsCompiled)}]; without a DS2 form: [{string.Join(", ", o.Compiler.InterruptsWithoutDs2Form)}]"
            + (Ds2AiMachines.UnreadInterrupts ? " (--ds1-ai-unread-interrupts: 6 / 8 / 11 mapped)" : ""));
        if (o.Compiler.TimerSlots.Count > 0) sb.AppendLine($"DS1 timers → DS2 timer slots: {string.Join(", ", o.Compiler.TimerSlots.Select(kv => $"{F(kv.Key)} → {kv.Value}"))}");
        sb.AppendLine("translated:");
        foreach (var (k, v) in c.Translated) sb.AppendLine($"  {v,5}  {k}");
        sb.AppendLine("skipped (no DS2 counterpart yet):");
        foreach (var (k, v) in c.Skipped) sb.AppendLine($"  {v,5}  {k}");
        if (c.Skipped.Count == 0) sb.AppendLine("  (none)");
        sb.AppendLine("assumptions (DS1 engine queries with a fixed value):");
        foreach (var (k, v) in o.Goal.Assumptions) sb.AppendLine($"  {v,5}  {k}");
        if (o.Goal.Assumptions.Count == 0) sb.AppendLine("  (none)");
        sb.AppendLine("DS1 AI API used: " + string.Join(", ", o.Goal.ApiUse.Select(kv => $"{kv.Key}×{kv.Value}")));
        sb.AppendLine($"actions used: [{string.Join(", ", c.ActionsUsed.Order())}], combo rows used: [{string.Join(", ", c.ChainsUsed.Order().Select(r => 50 + r))}]");
        var notes = c.Notes.Distinct().ToList();
        if (notes.Count > 0) { sb.AppendLine("notes:"); foreach (var n in notes.Take(60)) sb.AppendLine("  - " + n); if (notes.Count > 60) sb.AppendLine($"  … {notes.Count - 60} more"); }
        sb.AppendLine("template patch:");
        foreach (var r in o.Report.Skip(1)) sb.AppendLine("  " + r);
        return sb.ToString();
    }
}
