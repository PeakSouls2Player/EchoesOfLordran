using System.Globalization;
using SoulsFormats;
using static Lordran.Tools.Build.Ai.LuaEval;

namespace Lordran.Tools.Build.Ai;

/// <summary>
/// A DS1 battle goal evaluated symbolically (session 8, track enemies step 2). Loads DS1's aiCommon (ai_define, goal_list,
/// common_func, common_func2, common_battle_func and the common goals' own Lua) plus the battle goal script from our
/// decompiled tree (<c>data\research\lua\out</c>), gives it a symbolic <c>ai</c> / <c>goal</c> (<see cref="LuaEval"/>) and
/// runs <c>_Activate</c>, <c>_Interupt</c> and <c>OnIf_N</c> along every feasible path.
/// <para><b>Sub-goals</b>: <c>goal:AddSubGoal(GOAL, life, …)</c> of a goal DS1 implements in Lua (Attack, ComboAttack,
/// ComboRepeat, ComboFinal, AttackTunableSpin, ComboAttackTunableSpin, NonspinningAttack, GuardBreakAttack …: their
/// <c>_Activate</c> only reads its params and queues GOAL_COMMON_CommonAttack) is expanded right away by running that Lua, so
/// the per-goal constants (successAngle, turnTime, turnFaceAngle, isComboAttack) come from DS1's own code. Goals whose
/// behaviour lives in the engine or in an <c>_Update</c> (CommonAttack, ApproachTarget, LeaveTarget, SidewayMove, SpinStep,
/// Wait, Turn, KeepDist, Guard, MoveToSomewhere, ApproachStep) are kept as leaves for the generated DS2 machines;
/// GOAL_COMMON_If keeps its code number (its OnIf runs when the queue reaches it).</para>
/// <para><b>Engine queries without a DS2 counterpart</b> get a fixed value, listed in <see cref="Assumptions"/> and in the
/// coverage report: IsLadderAct false, IsTargetGuard false, HasSpecialEffectId false (DS1 SpEffect ids), GetTeamOrder =
/// ROLE_TYPE_None and GetTeamRecordCount 0 (DS1's team coordination is not ported), DbgGetForceActIdx 0.</para>
/// </summary>
public sealed class Ds1Goal
{
    public static string LuaRoot => Path.Combine("data", "research", "lua", "out");

    /// <summary>Goals kept as leaves (engine-side behaviour or an _Update loop) — the DS2 machines implement these.</summary>
    public static readonly HashSet<string> NativeGoals = ["CommonAttack", "ApproachTarget", "LeaveTarget", "SidewayMove", "SpinStep", "Wait",
        "Turn", "KeepDist", "Guard", "MoveToSomewhere", "ApproachStep", "If", "Stay", "BackToHome", "TurnAround", "ApproachSettingDirection"];

    // ------------------------------------------------------------------ results
    public abstract record Effect(int Line);
    /// <summary>A queued leaf goal: name, life (seconds, may be symbolic), its params.</summary>
    public sealed record SubGoal(int Line, string Name, object? Life, List<object?> Args, string Via) : Effect(Line)
    {
        public override string ToString() => $"{Name}({F(Life)}; {string.Join(", ", Args.Select(F))})";
    }
    /// <summary>An immediate engine call during the think (SetNumber, SetTimer, SetEventMoveTarget, DoEzAction …).</summary>
    public sealed record Immediate(int Line, string Api, List<object?> Args) : Effect(Line)
    {
        public override string ToString() => $"ai:{Api}({string.Join(", ", Args.Select(F))})";
    }
    public sealed record Clear(int Line) : Effect(Line) { public override string ToString() => "ClearSubGoal"; }

    public static string F(object? o) => o switch { null => "nil", double d => d.ToString("0.###", CultureInfo.InvariantCulture), bool b => b ? "true" : "false", _ => o.ToString() ?? "?" };

    /// <summary>One feasible path: decisions taken, effects in order, the function's first return value, random rolls.</summary>
    public sealed record PathResult(List<Literal> Literals, List<Effect> Effects, object? Return, List<Rand> Rands, List<string> Notes);

    public sealed class Run
    {
        public string Function = "";
        public List<PathResult> Paths = [];
        public List<string> Errors = [];
    }

    // ------------------------------------------------------------------ setup
    public readonly int BattleGoalId;
    public readonly string GoalName;        // e.g. "HusiFight_FireBom254002Battle"
    public readonly string ScriptPath;
    public readonly PARAM.Row? Think;
    public readonly int ThinkId;
    readonly List<Lua.Chunk> chunks = [];
    readonly Dictionary<string, string> goalNames = [];   // goal id (number text) → registered name
    public readonly SortedDictionary<string, int> Assumptions = [];
    public readonly SortedDictionary<string, int> ApiUse = [];

    static readonly string[] CommonScripts = ["ai_define.lua", "goal_list.lua", "goal_list_dlc.lua", "event_list.lua", "logic_list.lua",
        "common_func.lua", "common_func2.lua", "common_battle_func.lua", "common_logic_func.lua", "npc_subgoals.lua",
        "attack.lua", "combo_attack.lua", "combo_repeat.lua", "combo_final.lua", "attack_tunable_spin.lua", "combo_attack_tunable_spin.lua",
        "nonspinning_attack.lua", "nonspinning_combo_attack.lua", "nonspinning_combo_repeat.lua", "nonspinning_combo_final.lua",
        "combo_attack_success_angle180.lua", "combo_repeat_success_angle180.lua", "combo_tunable_success_angle180.lua",
        "gaurd_break_attack.lua", "gaurd_break_tunable.lua", "continue_attack.lua", "approach_target.lua", "turn.lua", "if.lua", "use_item.lua",
        "common_attack.lua"];

    /// <param name="map">DS1 luabnd folder to take the battle goal from first (m18_01, m10_02_00_00: the map the character is
    /// placed in; DS1 ships a copy of a goal in every map that uses it).</param>
    public Ds1Goal(int battleGoalId, PARAM.Row? think, string? luaRoot = null, string? map = null)
    {
        BattleGoalId = battleGoalId;
        Think = think;
        ThinkId = think?.ID ?? 0;
        string root = luaRoot ?? LuaRoot;
        string common = Path.Combine(root, "aiCommon");
        foreach (var f in CommonScripts)
        {
            string p = Path.Combine(common, f);
            if (File.Exists(p)) chunks.Add(Lua.ParseFile(p));
        }
        string file = $"{battleGoalId:D6}_battle.lua";
        ScriptPath = Directory.GetFiles(root, file, SearchOption.AllDirectories)
            .OrderBy(p => map != null && Path.GetFileName(Path.GetDirectoryName(p)!).StartsWith(map, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.Contains("aiCommon") ? 1 : 0).ThenBy(p => p).FirstOrDefault()
            ?? throw new FileNotFoundException($"DS1 battle goal {file} not found under {root}");
        chunks.Add(Lua.ParseFile(ScriptPath));
        // the goal's registered name (REGISTER_GOAL(GOAL_X_N_Battle, "Name"))
        var vm = NewVm(null, null);
        GoalName = goalNames.Values.FirstOrDefault(n => n.EndsWith("Battle", StringComparison.Ordinal) && n.Contains(battleGoalId.ToString(CultureInfo.InvariantCulture)))
            ?? goalNames.Values.Last();
        _ = vm;
    }

    // ------------------------------------------------------------------ the symbolic ai / goal
    sealed class Recorder
    {
        public readonly List<Effect> Effects = [];
        public readonly Dictionary<double, object?> Numbers = [];
    }

    LuaEval NewVm(List<bool>? prefix, Recorder? rec)
    {
        var vm = new LuaEval(prefix);
        vm.InstallStdlib();
        void G(string name, BuiltinFn f) => vm.Globals.Set(name, new Builtin(name, f));
        G("REGISTER_GOAL", (v, a, l) => { if (a.Count >= 2 && a[0] is double id && a[1] is string n) goalNames[F(id)] = n; return []; });
        foreach (var noop in new[] { "REGISTER_GOAL_NO_UPDATE", "REGISTER_GOAL_NO_INTERUPT", "REGISTER_GOAL_UPDATE_TIME", "REGISTER_DBG_GOAL_PARAM",
                     "ENABLE_COMBO_ATK_CANCEL", "REGISTER_LOGIC_FUNC", "REGISTER_GOAL_NO_SUB_GOAL", "REGISTER_GOAL_USE_GUARD_GOAL" })
            G(noop, (v, a, l) => []);
        foreach (var c in chunks) vm.RunChunk(c);
        return vm;
    }

    LuaEval.Obj Ai(Recorder rec)
    {
        return new LuaEval.Obj("ai", (vm, name, a, line) =>
        {
            ApiUse[$"ai:{name}"] = ApiUse.GetValueOrDefault($"ai:{name}") + 1;
            double N(int i) => i < a.Count && a[i] is double d ? d : 0;
            object? A(int i) => i < a.Count ? a[i] : null;
            switch (name)
            {
                case "GetDist": return [new Query("Dist", N(0))];
                case "GetDistYSigned": return [new Query("DistY", N(0))];
                case "GetDistY": return [new Abs(new Query("DistY", N(0)))];
                case "GetRandam_Int":
                {
                    double lo = LuaEval.Number(A(0), line), hi = LuaEval.Number(A(1), line);
                    if (hi < lo) { vm.Notes.Add($"line {line}: GetRandam_Int({F(lo)}, {F(hi)}) with an empty range: taken as {F(lo)} (U: what DS1's engine returns)"); return [lo]; }
                    if (hi == lo) return [lo];
                    return [vm.NewRand(lo, hi, false, line)];
                }
                case "GetRandam_Float":
                {
                    double lo = LuaEval.Number(A(0), line), hi = LuaEval.Number(A(1), line);
                    if (hi <= lo) return [lo];
                    return [vm.NewRand(lo, hi, true, line)];
                }
                case "GetEventRequest" when EventRequests.ForThink(ThinkId).Any(c => c.Slot == N(0)):
                    // session 11 (burgchr11), rule E-REQ: the slot's requests read the generator's logicEventFlag channels
                    return [new Query("EventRequest", N(0), ThinkId)];
                case "GetEventRequest":
                    // DS1 EMEVD "Request AI…" (2004:9 Set AI ID / AI request) has no translated counterpart yet (rulebook §4; Firelink's
                    // EMEVD is not translated at all): the request is never set
                    Assume("ai:GetEventRequest = 0 (DS1 EMEVD AI requests not translated)");
                    return [0.0];
                case "GetHpRate": return [new Query("HpRate", N(0))];
                case "GetNumber": return rec.Numbers.TryGetValue(N(0), out var nv) ? [nv] : [new Query("Number", N(0))];
                case "SetNumber": rec.Numbers[N(0)] = A(1); rec.Effects.Add(new Immediate(line, name, [.. a])); return [];
                case "IsInterupt": return [new Pred("Interupt", N(0))];
                case "IsInsideTarget": return [new Pred("InsideTarget", N(0), N(1), N(2))];
                case "IsInsideTargetRegion": return [new Pred("InRegion", N(0), N(1))];
                case "IsInsideObserve": return [new Pred("InObserve", N(0))];
                // session 11 (dchr11, coordinator decision): the Channelers' two queries without a 1:1 DS2 form, as approximations (rules AI-NF / AI-NM,
                // GoalCompiler): one predicate per DS1 query kind (the back / side probes all compile to the same DS2 test, so they share one literal)
                case "ChkNearLowHpFriend": return [new Pred("NearFriend", N(1))];
                case "IsOnNearMeshByPos" when N(1) == 1: return [new Pred("NearMeshF", N(2))];
                case "IsOnNearMeshByPos": return [new Pred("NearMeshSide")];
                case "IsFinishTimer": return [new Pred("TimerDone", N(0))];
                case "GetTimer": return [new Query("Timer", N(0))];
                case "SetTimer": rec.Effects.Add(new Immediate(line, name, [.. a])); return [];
                case "SetEventMoveTarget" or "AddObserveRegion" or "AddObserveChrDmySphere" or "DoEzAction" or "SetAttackRequest" or "ChangeEquipItem" or "TurnTo":
                    rec.Effects.Add(new Immediate(line, name, [.. a])); return [];
                case "GetNpcThinkParamID": return [(double)ThinkId];
                case "GetExcelParam": return [Excel((int)N(0))];
                case "IsLadderAct": Assume("ai:IsLadderAct = false (DS2 ladders are DS2's own AI modes)"); return [false];
                case "IsTargetGuard": Assume("ai:IsTargetGuard = false (no DS2 query known for the target's guard)"); return [false];
                case "HasSpecialEffectId": Assume($"ai:HasSpecialEffectId(..., {F(A(1))}) = false (DS1 SpEffect ids have no DS2 counterpart)"); return [false];
                case "GetTeamOrder": Assume("ai:GetTeamOrder = ROLE_TYPE_None (DS1 team roles not ported)"); return [-1.0];
                case "GetTeamRecordCount": Assume("ai:GetTeamRecordCount = 0 (DS1 team coordination not ported)"); return [0.0];
                case "DbgGetForceActIdx": return [0.0];
                case "DbgSetLastActIdx" or "PrintText": return [];
                default:
                    vm.Notes.Add($"line {line}: unsupported DS1 AI call ai:{name}({string.Join(", ", a.Select(F))}) → nil");
                    return [null];
            }
        });
    }

    void Assume(string what) => Assumptions[what] = Assumptions.GetValueOrDefault(what) + 1;

    object? Excel(int type)
    {
        string? field = type switch
        {
            1 => "maxBackhomeDist", 2 => "backhomeDist", 3 => "backhomeBattleDist", 4 => "nonBattleActLife", 5 => "BattleStartDist",
            6 => "bMoveOnHearSound", 7 => "CannotMoveAction", 8 => "battleGoalID", 9 => "BackHome_LookTargetTime", 10 => "BackHome_LookTargetDist",
            11 => "BackHomeLife_OnHitEneWal", 12 => "callHelp_IsCall", 13 => "callHelp_IsReply", 14 => "callHelp_MyPeerId", 15 => "callHelp_CallPeerId",
            16 => "callHelp_DelayTime", 17 => "callHelp_CallActionId", 18 => "callHelp_ReplyBehaviorType", 19 => "callHelp_ForgetTimeByArrival",
            20 => "callHelp_MinWaitTime", 21 => "callHelp_MaxWaitTime", 22 => "callHelp_ReplyActionId", 23 => "thinkAttr_doAdmirer", _ => null,
        };
        if (field == null || Think?[field] is not { } c) return 0.0;
        return Convert.ToDouble(c.Value, CultureInfo.InvariantCulture);
    }

    LuaEval.Obj GoalObj(Recorder rec, LuaEval vmOuter, object? life, List<object?> param, string via)
    {
        LuaEval.Obj self = null!;
        self = new LuaEval.Obj("goal", (vm, name, a, line) =>
        {
            ApiUse[$"goal:{name}"] = ApiUse.GetValueOrDefault($"goal:{name}") + 1;
            switch (name)
            {
                case "AddSubGoal" or "AddSubGoal_Front":
                {
                    if (a.Count == 0 || a[0] is not double gid) { vm.Notes.Add($"line {line}: AddSubGoal with goal id {F(a.Count > 0 ? a[0] : null)} (nil: undefined goal constant) — skipped"); return [new Obj("subgoal", (v2, n2, a2, l2) => [])]; }
                    string gname = goalNames.GetValueOrDefault(F(gid)) ?? $"goal{F(gid)}";
                    var gargs = a.Skip(2).ToList();
                    if (name == "AddSubGoal_Front") vm.Notes.Add($"line {line}: AddSubGoal_Front({gname}) queued in order (front insertion only differs from AddSubGoal after ClearSubGoal)");
                    Expand(vm, rec, gname, a.Count > 1 ? a[1] : null, gargs, line, via);
                    return [new Obj("subgoal", (v2, n2, a2, l2) => [])];
                }
                case "ClearSubGoal": rec.Effects.Add(new Clear(line)); return [];
                case "GetParam": { int i = a.Count > 0 && a[0] is double d ? (int)d : 0; return [i < param.Count ? param[i] : null]; }
                case "GetLife": return [life];
                case "GetSubGoalNum": return [0.0];
                case "SetNumber" or "GetNumber" or "SetLifeEndSuccess" or "SetFailedEndOption": return [];
                case "GetBattleGoalId": return [(double)BattleGoalId];
                default:
                    vm.Notes.Add($"line {line}: unsupported goal:{name} → nil");
                    return [null];
            }
        });
        return self;
    }

    void Expand(LuaEval vm, Recorder rec, string gname, object? life, List<object?> args, int line, string via)
    {
        if (NativeGoals.Contains(gname)) { rec.Effects.Add(new SubGoal(line, gname, life, args, via)); return; }
        var act = vm.Globals.Get($"{gname}_Activate");
        if (act is Closure)
        {
            var sub = GoalObj(rec, vm, life, args, $"{via}>{gname}");
            vm.Call(act, [Ai(rec), sub], line);
            return;
        }
        vm.Notes.Add($"line {line}: goal {gname} has no Lua and no DS2 machine — skipped");
        rec.Effects.Add(new SubGoal(line, gname, life, args, via + ">unsupported"));
    }

    // ------------------------------------------------------------------ path enumeration
    /// <summary>Runs <paramref name="function"/>(ai, goal [, extra...]) on every feasible path.</summary>
    public Run Evaluate(string function, params object?[] extra)
    {
        var run = new Run { Function = function };
        var pending = new Stack<List<bool>>();
        pending.Push([]);
        int guard = 0;
        while (pending.Count > 0)
        {
            if (++guard > 20000) { run.Errors.Add("path limit (20000) reached"); break; }
            var prefix = pending.Pop();
            var rec = new Recorder();
            var vm = NewVm(prefix, rec);
            var fn = vm.Globals.Get(function);
            if (fn == null) { run.Errors.Add($"{function} is not defined"); break; }
            object? ret = null;
            try
            {
                var r = vm.Call(fn, [Ai(rec), GoalObj(rec, vm, null, [], function), .. extra], 0);
                ret = r.Count > 0 ? r[0] : null;
            }
            catch (LuaError e) { vm.Notes.Add($"Lua error: {e.Message}"); run.Errors.Add($"{function} path {string.Join("", vm.Taken.Select(t => t ? "1" : "0"))}: {e.Message}"); }
            run.Paths.Add(new PathResult([.. vm.Path], rec.Effects, ret, [.. vm.Rands], [.. vm.Notes]));
            // alternatives are discovered in path order; push in reverse so the first branch's false arm comes last (DFS)
            for (int i = vm.Alternatives.Count - 1; i >= 0; i--) pending.Push(vm.Alternatives[i]);
        }
        return run;
    }

    public string ActivateName => $"{GoalName}_Activate";
    public string InteruptName => $"{GoalName}_Interupt";
    public string OnIfName => $"OnIf_{BattleGoalId}";
    public bool HasFunction(string name) => NewVm(null, null).Globals.Get(name) is Closure;
}
