using SoulsFormats;

namespace Lordran.Tools.Build.Ai.Boss;

/// <summary>
/// DS1 Lua AI → DS2 AI ESD for one DS1 character (session 8, track boss; rulebook §5 "Lua AI"). Shared translator code for
/// every 1:1 character: the enemies track's armed regulars use the same three pieces (<see cref="LuaSyntax"/> parses our
/// decompiled DSR scripts, <see cref="Ds1AiInterpreter"/> runs DS1's battle goal symbolically into decision trees,
/// <see cref="Ds2AiEsd"/> writes them as DS2 lower-logic machines); a character supplies only its
/// <see cref="Ds2AiEsd.IContext"/> (DS1 attack ids → its DS2 actions, regions → EventLocations, home point, dummy
/// positions, capsule radii).
/// <para>Wiring (DS2 vanilla, V): GeneratorRegistParam HighLogicId / LowLogicId → EnemyDefaultLogicParam[low] {upper,
/// lower, start group}; the upper id also keys LogicComParam (perception). The template boss's upper logic is kept (DS2's
/// target acquisition, mode dispatch and start-up wake: DS2 rules) as our own file, with one DS1 value: the delay between
/// the boss's logic flag and the wake (the Smelter's 2.25 s, DS1's event plays the drop at once: <see cref="Spec.WakeDelay"/>).
/// LogicComParam perception from DS1 NpcThinkParam (eye_dist → viewing distance, eye_angX/Y → viewing angles,
/// ear_dist → hearing, maxBackhomeDist → chase distance; DS2-AI-NOTES §4, P), the rest the template's.</para>
/// </summary>
public static partial class AiPorter
{
    /// <summary>Our decompiled DSR scripts (data\research\lua\luadec50.py output of DSR's script\*.luabnd.dcx, read-only
    /// from the player's install; regenerate with <c>python luadec50.py --dir bin\MAP out\MAP</c>, DS1-AI-NOTES.md).</summary>
    public const string DecompiledLua = @"C:\EchoesOfLordran\data\research\lua\out";

    /// <param name="Ds1Map">the DS1 script folder holding the battle goal (luabnd name, e.g. "m18_01")</param>
    /// <param name="BattleGoal">DS1 NpcThinkParam.battleGoalID (223200)</param>
    /// <param name="ThinkParam">DS1 NpcThinkParam row (perception)</param>
    /// <param name="TemplateUpper">template upper logic / LogicComParam (Smelter 305011)</param>
    /// <param name="TemplateLower">template lower logic / EnemyDefaultLogicParam (Smelter 305001)</param>
    /// <param name="Upper">our upper logic id (ai{Upper}.esd, LogicComParam row, GeneratorRegistParam.HighLogicId)</param>
    /// <param name="Lower">our lower logic id (ai{Lower}.esd, EnemyDefaultLogicParam row, LowLogicId)</param>
    /// <param name="WakeDelay">seconds between the logic flag and the start-up wake (template upper: 2.25)</param>
    public sealed record Spec(string Ds1Map, int BattleGoal, int ThinkParam, int TemplateUpper, int TemplateLower, int Upper, int Lower, float WakeDelay = 0f);

    public sealed class Result
    {
        public Dictionary<string, byte[]> Files = [];
        public List<string> Report = [];
        public ESD Lower = null!, Upper = null!;
    }

    public static Result Build(Pack? pack, string ds2Data, Ds1Source ds1, Spec s, Ds2AiEsd.IContext ctx)
    {
        var res = new Result();
        var lib = new Ds1AiInterpreter(Ds1AiInterpreter.StandardFiles(DecompiledLua, s.Ds1Map)) { ThinkParamId = s.ThinkParam, BattleGoalId = s.BattleGoal,
            QueryRules = s.Ds1Map.StartsWith("m10_00", StringComparison.OrdinalIgnoreCase) };   // session 11 (dchr11): rules AI-IT / AI-TM for the Depths
        string goal = lib.GoalName(s.BattleGoal) ?? throw new InvalidDataException($"DS1 battle goal {s.BattleGoal}: no REGISTER_GOAL in {s.Ds1Map} / aiCommon");
        res.Report.Add($"DS1 battle goal {s.BattleGoal} = {goal} ({lib.Loaded.Count} Lua files: aiCommon + {s.Ds1Map})");
        var tLower = ESD.Read(Path.Combine(ds2Data, "ezstate", $"ai{s.TemplateLower}.esd"));
        var tUpper = ESD.Read(Path.Combine(ds2Data, "ezstate", $"ai{s.TemplateUpper}.esd"));
        res.Lower = Ds2AiEsd.BuildLower(tLower, lib, goal, ctx, res.Report);
        res.Upper = Upper(tUpper, s.WakeDelay, res.Report);
        foreach (var u in lib.Unsupported.Distinct()) res.Report.Add($"DS1 Lua not translated: {u}");
        res.Files[$"ezstate/ai{s.Lower}.esd"] = res.Lower.Write();
        res.Files[$"ezstate/ai{s.Upper}.esd"] = res.Upper.Write();
        if (pack != null) Params(pack, ds1, s, res.Report);
        return res;
    }

    /// <summary>The template's upper logic with DS1's wake timing: the start-up machine waits (f3() &gt; 2.25; opcode 0x92 = &gt;
    /// as DS2 runs it, chrint stage 3: the template's own bytes, only the number is replaced) after the logic flag before
    /// 1:5271 (wake); DS1's event plays the wake animation as soon as it fires (delay 0: "f3() &gt; 0" = the next frame).</summary>
    static ESD Upper(ESD t, float delay, List<string> report)
    {
        byte[] from = [0x43, 0x84, 0x80, .. BitConverter.GetBytes(2.25f), 0x92, 0x40, 0x96, 0xA1];
        byte[] to = [0x43, 0x84, .. Ez.Num(delay), 0x92, 0x40, 0x96, 0xA1];
        int n = 0;
        foreach (var (_, states) in t.StateGroups)
            foreach (var (_, st) in states)
                foreach (var c in st.Conditions)
                    if (c.Evaluator.AsSpan().SequenceEqual(from)) { c.Evaluator = to; n++; }
        report.Add($"upper logic: the template's (perception, mode dispatch, start-up wake on GeneratorParam.logicEventFlag1); wake delay 2.25 s → {delay} s on {n} condition(s)");
        if (n != 1) report.Add($"WARNING upper logic: expected one wake-delay condition, patched {n}");
        return t;
    }

    static void Params(Pack pack, Ds1Source ds1, Spec s, List<string> report)
    {
        var dl = pack.Param("EnemyDefaultLogicParam");
        var row = pack.AddRow("EnemyDefaultLogicParam", dl[s.TemplateLower] ?? throw new InvalidDataException($"EnemyDefaultLogicParam {s.TemplateLower} missing"), s.Lower, "[Lordran] DS1 AI");
        Set(row, "upperLogickId", s.Upper); Set(row, "lowerLogickId", s.Lower);
        var lc = pack.Param("LogicComParam");
        var c = pack.AddRow("LogicComParam", lc[s.TemplateUpper] ?? throw new InvalidDataException($"LogicComParam {s.TemplateUpper} missing"), s.Upper, "[Lordran] DS1 perception");
        var think = ds1.Params.Params["NpcThinkParam"][s.ThinkParam] ?? throw new InvalidDataException($"DS1 NpcThinkParam {s.ThinkParam} missing");
        double D(string f) => Convert.ToDouble(think[f].Value);
        Set(c, "viewingDistanceMax", (float)D("eye_dist")); Set(c, "viewingDistanceMin", (float)D("eye_dist"));
        Set(c, "viewingAngleLR", (float)(D("eye_angX") * Math.PI / 180)); Set(c, "viewingAngleUD", (float)(D("eye_angY") * Math.PI / 180));
        Set(c, "hearingDistance", (float)D("ear_dist"));
        Set(c, "chaseDistance", (float)Math.Min(D("maxBackhomeDist"), 9999));
        report.Add($"EnemyDefaultLogicParam {s.Lower} (from {s.TemplateLower}): upper {s.Upper}, lower {s.Lower}, start group {row["lowerStartStateId"].Value}; LogicComParam {s.Upper} (from {s.TemplateUpper}): view {D("eye_dist")} m / {D("eye_angX")}°×{D("eye_angY")}°, hearing {D("ear_dist")} m, chase {Math.Min(D("maxBackhomeDist"), 9999)} m (DS1 NpcThinkParam {s.ThinkParam})");
    }

    static void Set(PARAM.Row r, string f, object v)
    {
        var cell = r.Cells.FirstOrDefault(x => x.Def.InternalName == f) ?? throw new InvalidDataException($"{r.ID}: no field {f}");
        cell.Value = Convert.ChangeType(v, cell.Value.GetType(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
