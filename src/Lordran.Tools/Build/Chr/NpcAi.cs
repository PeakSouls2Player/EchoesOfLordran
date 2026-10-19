using System.Text.RegularExpressions;
using SoulsFormats;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// <b>Rule AI-N1</b> (session 9, track npcpose2; interim, P): a DS1 human NPC on its own template network (rule I1d) gets
/// its own logic pair, a copy of the template's (c7240 → Captain Drummond's ai724010 upper / ai724000 lower) under new ids
/// (<see cref="NpcIdle.NpcNet.AiHigh"/> / <see cref="NpcIdle.NpcNet.AiLow"/> = animId + 10 / animId: DS2 has no AI ESD in
/// 800000-809999 and no LogicComParam row 800210/800310; chrint's AIs under new ids load: runs s9-6/7) — files
/// <c>ezstate/ai{Low}.esd</c> / <c>ai{High}.esd</c> (internal names changed to the file ids) + LogicComParam row High
/// (copy of the template's), the chrint emitter path.
/// <list type="bullet">
/// <item>The startup groups 2147483629 / 2147483628 stay verbatim (2147483629 switches on f5902() 0..20 and calls
/// 2147483628(p), which runs <c>while 5204(p0, 0)</c>: for Oscar 5204(3, 0), as for Drummond, whose regist row 72400000 is
/// startUpStateType 3 and who sits in vanilla).</item>
/// <item>Every other DS2 action request (constant first argument ≥ 0 of <c>1:5204</c> / <c>5:5204</c>, and every constant
/// ≥ 0 machine-call argument whose parameter <b>reaches</b> a 5204 action slot through exact <c>pN</c> passes, whatever
/// else the machine does with it: <see cref="ReachParams"/>) becomes −1 (chrint rule AI3: the 5204 handler ignores n &lt; 0,
/// DarkSoulsII.c 202405-202462 case 0x1454; vanilla machines treat −1 as "no attack"). On the generic class 5204(n) is an
/// enemy action n (P): Drummond's guard-counter states issue 5204(30, 0) / 5204(31, 0); 2147483607 state 8 calls the
/// pursuit machine 2147483614(60, 2), whose p0 is also tested <c>p0 == 60</c> but still reaches 2147483639 state 9
/// <c>1:5204(p0, 0)</c> → (−1, 2) (the build log marks such "also used" parameters).</item>
/// <item>Never Saulden's ai741000: its 5204(13) would be an enemy request 13 on this class.</item>
/// </list>
/// <c>--npc-template-ai vanilla</c> (A/B): the regist row points at the vanilla pair (no AI files; hostility then uses the
/// template's attacks as listed DS2 stand-ins).
/// </summary>
public static class NpcAi
{
    public static readonly long[] StartupGroups = [2147483629, 2147483628];

    /// <summary>build-pack --npc-template-ai vanilla: the template's own vanilla logic ids (A/B).</summary>
    public static bool VanillaTemplateAi;

    public sealed class Output
    {
        public int High, Low, TemplateHigh, TemplateLow;
        public byte[] UpperEsd = [], LowerEsd = [];
        public List<string> Report = [];
        public int Offed;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Pack, Dictionary<int, (int, int)>> done = new();

    /// <summary>The NPC's (High, Low) logic ids; emits the copy into the pack once per network.</summary>
    public static (int High, int Low) Ensure(Pack pack, NpcIdle.NpcNet n)
    {
        int tLow = n.TemplateAnimId, tHigh = n.TemplateAnimId + 10;
        if (VanillaTemplateAi)
        {
            pack.Note($"AI-N1 A/B (--npc-template-ai vanilla): {n.Who} → vanilla ai{tHigh:D6} / ai{tLow:D6} (template's attacks = listed DS2 stand-ins)");
            return (tHigh, tLow);
        }
        var d = done.GetOrCreateValue(pack);
        if (d.TryGetValue(n.AnibndId, out var ids)) return ids;
        var o = Build(Path.Combine(pack.Ds2Data, "ezstate"), n);
        if (File.Exists(Path.Combine(pack.Ds2Data, "ezstate", $"ai{o.High:D6}.esd")) || File.Exists(Path.Combine(pack.Ds2Data, "ezstate", $"ai{o.Low:D6}.esd")))
            throw new InvalidDataException($"AI-N1: ai{o.High:D6} / ai{o.Low:D6} exist in vanilla");
        pack.AddFile("gamedata", $"ezstate/ai{o.Low:D6}.esd", o.LowerEsd);
        pack.AddFile("gamedata", $"ezstate/ai{o.High:D6}.esd", o.UpperEsd);
        var lcp = pack.Param("LogicComParam");
        if (lcp[o.High] != null) throw new InvalidDataException($"LogicComParam {o.High} already exists");
        pack.AddRow("LogicComParam", lcp[tHigh] ?? throw new InvalidDataException($"LogicComParam {tHigh} missing"), o.High, $"[Lordran] {n.Who} (AI-N1, copy of {tHigh})");
        foreach (var l in o.Report) pack.Note($"AI-N1: {l}");
        Console.WriteLine($"  AI-N1 {n.Who}: ai{o.High:D6} / ai{o.Low:D6} = ai{tHigh:D6} / ai{tLow:D6} with {o.Offed} DS2 action requests → -1 (startup groups verbatim)");
        d[n.AnibndId] = (o.High, o.Low);
        return (o.High, o.Low);
    }

    public static Output Build(string ezstateDir, NpcIdle.NpcNet n)
    {
        var o = new Output { High = n.AiHigh, Low = n.AiLow, TemplateLow = n.TemplateAnimId, TemplateHigh = n.TemplateAnimId + 10 };
        var upper = ESD.Read(Path.Combine(ezstateDir, $"ai{o.TemplateHigh:D6}.esd"));
        upper.Name = $"ai{o.High:D6}_{n.Who} (AI-N1) on ai{o.TemplateHigh:D6}";
        o.UpperEsd = upper.Write();
        var lower = ESD.Read(Path.Combine(ezstateDir, $"ai{o.TemplateLow:D6}.esd"));
        foreach (long g in StartupGroups) if (!lower.StateGroups.ContainsKey(g)) throw new InvalidDataException($"ai{o.TemplateLow:D6} has no startup group {g}");
        var actionParams = ReachParams(lower, out var alsoUsed);
        foreach (var (gid, sts) in lower.StateGroups.OrderBy(g => g.Key))
        {
            if (StartupGroups.Contains(gid)) continue;
            foreach (var (sid, st) in sts.OrderBy(s => s.Key))
                foreach (var c in st.EntryCommands.Concat(st.WhileCommands).Concat(st.ExitCommands))
                {
                    if (c.CommandID == 5204 && c.CommandBank is 1 or 5)
                    {
                        int k = c.CommandBank == 5 ? 1 : 0;
                        if (c.Arguments.Count > k && Const(c.Arguments[k]) is int a && a >= 0)
                        {
                            c.Arguments[k] = Int(-1); o.Offed++;
                            o.Report.Add($"ai{o.Low:D6} group {gid} state {sid}: {c.CommandBank}:5204({a}) → -1");
                        }
                    }
                    else if (c.CommandBank == 6 && actionParams.TryGetValue((long)(uint)c.CommandID, out var ps))
                        foreach (int k in ps)
                            if (k < c.Arguments.Count && Const(c.Arguments[k]) is int a && a >= 0)
                            {
                                c.Arguments[k] = Int(-1); o.Offed++;
                                o.Report.Add($"ai{o.Low:D6} group {gid} state {sid}: 6:{(uint)c.CommandID} action argument p{k} = {a} → -1{(alsoUsed.Contains(((long)(uint)c.CommandID, k)) ? " (parameter also used outside a 5204 slot / -1 test)" : "")}");
                            }
                }
        }
        lower.Name = $"ai{o.Low:D6}_{n.Who} (AI-N1) on ai{o.TemplateLow:D6}";
        o.LowerEsd = lower.Write();
        o.Report.Insert(0, $"{n.Who}: upper ai{o.High:D6} = ai{o.TemplateHigh:D6} (name only), lower ai{o.Low:D6} = ai{o.TemplateLow:D6} with {o.Offed} DS2 action requests → -1 outside the startup groups {string.Join(", ", StartupGroups)} (verbatim); LogicComParam {o.High} = {o.TemplateHigh}'s row");
        return o;
    }

    /// <summary>Remaining action requests of an AI lower (npc-ai-check), by the same reach rule as the build
    /// (<see cref="ReachParams"/>): constant ≥ 0 action arguments outside the startup groups ("ACTIVE"), action slots fed by
    /// anything but a constant or an exact <c>pN</c> ("UNRESOLVED": the check cannot prove them off), and the startup
    /// groups' requests ("startup").</summary>
    public static List<string> ActiveRequests(ESD esd)
    {
        var list = new List<string>();
        var ap = ReachParams(esd, out _);
        foreach (var (gid, sts) in esd.StateGroups.OrderBy(g => g.Key))
            foreach (var (sid, st) in sts.OrderBy(s => s.Key))
                foreach (var c in st.EntryCommands.Concat(st.WhileCommands).Concat(st.ExitCommands))
                {
                    if (c.CommandID == 5204 && c.CommandBank is 1 or 5)
                    {
                        int k = c.CommandBank == 5 ? 1 : 0;
                        if (c.Arguments.Count <= k) continue;
                        if (Const(c.Arguments[k]) is int a && a >= 0) list.Add($"{(StartupGroups.Contains(gid) ? "startup" : "ACTIVE")} group {gid} state {sid}: {c.CommandBank}:5204({a})");
                        else if (StartupGroups.Contains(gid)) list.Add($"startup group {gid} state {sid}: {c.CommandBank}:5204({Commands.Esd.Expr(c.Arguments[k])})");
                        else if (Const(c.Arguments[k]) == null && !IsParam(c.Arguments[k])) list.Add($"UNRESOLVED group {gid} state {sid}: {c.CommandBank}:5204({Commands.Esd.Expr(c.Arguments[k])})");
                    }
                    else if (c.CommandBank == 6 && ap.TryGetValue((long)(uint)c.CommandID, out var ps))
                        foreach (int k in ps)
                        {
                            if (k >= c.Arguments.Count) continue;
                            if (Const(c.Arguments[k]) is int a && a >= 0)
                                list.Add($"{(StartupGroups.Contains(gid) ? "startup" : "ACTIVE")} group {gid} state {sid}: 6:{(uint)c.CommandID} p{k} = {a}");
                            else if (!StartupGroups.Contains(gid) && Const(c.Arguments[k]) == null && !IsParam(c.Arguments[k]))
                                list.Add($"UNRESOLVED group {gid} state {sid}: 6:{(uint)c.CommandID} p{k} = {Commands.Esd.Expr(c.Arguments[k])}");
                        }
                }
        return list;
    }

    static bool IsParam(byte[] b) => b.Length == 3 && b[0] < 0x80 && b[1] == 0xB8 && b[2] == 0xA1;

    static byte[] Int(int v) => v is >= -64 and < 64 ? [(byte)(0x40 + v), 0xA1] : [0x82, .. BitConverter.GetBytes(v), 0xA1];

    public static int? Const(byte[] b)
    {
        if (b.Length == 2 && b[0] < 0x80 && b[1] == 0xA1) return b[0] - 0x40;
        if (b.Length == 6 && b[0] == 0x82 && b[5] == 0xA1) return BitConverter.ToInt32(b, 1);
        return null;
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

    /// <summary>Per machine: the parameters that <b>reach</b> a 5204 action slot (<c>1:5204</c> argument 0 /
    /// <c>5:5204</c> argument 1 = exactly <c>pN</c>), followed back through machine calls whose argument is exactly
    /// <c>pN</c> (fixed point), whatever else the machine does with the parameter (review finding: 2147483614 tests
    /// <c>p0 == 60</c> and still passes p0 on to 2147483639 state 9 <c>1:5204(p0, 0)</c>). <paramref name="alsoUsed"/> =
    /// the reaching parameters chrint's stricter <see cref="ActionParams"/> rejects (also compared / computed with): listed
    /// in the report because −1 can change that machine's branches. Used by the AI-N1 build and npc-ai-check alike.</summary>
    public static Dictionary<long, HashSet<int>> ReachParams(ESD esd, out HashSet<(long, int)> alsoUsed)
    {
        var reach = new HashSet<(long, int)>();
        var passes = new List<(long M, int K, long Callee, int J)>();
        foreach (var (gid, sts) in esd.StateGroups)
            foreach (var st in sts.Values)
                foreach (var c in st.EntryCommands.Concat(st.WhileCommands).Concat(st.ExitCommands))
                    for (int j = 0; j < c.Arguments.Count; j++)
                    {
                        var b = c.Arguments[j];
                        if (!IsParam(b)) continue;
                        int k = b[0] - 0x40;
                        if (c.CommandID == 5204 && (c.CommandBank == 1 && j == 0 || c.CommandBank == 5 && j == 1)) reach.Add((gid, k));
                        else if (c.CommandBank == 6) passes.Add((gid, k, (long)(uint)c.CommandID, j));
                    }
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var p in passes)
                if (reach.Contains((p.Callee, p.J)) && reach.Add((p.M, p.K))) changed = true;
        }
        var strict = ActionParams(esd);
        alsoUsed = reach.Where(r => !(strict.TryGetValue(r.Item1, out var set) && set.Contains(r.Item2))).ToHashSet();
        var res = new Dictionary<long, HashSet<int>>();
        foreach (var (m, k) in reach)
        {
            if (!res.TryGetValue(m, out var set)) res[m] = set = [];
            set.Add(k);
        }
        return res;
    }

    /// <summary>Per machine: the parameters that are action numbers — used only as 5204's action, in a <c>pN == -1</c>
    /// test, or passed on to another machine's action parameter (fixed point). chrint's AiTranslator.ActionParams (rule AI3).
    /// Stricter than <see cref="ReachParams"/> (drops a parameter that is also compared with a constant); AI-N1 uses it only
    /// to mark "also used" parameters.</summary>
    public static Dictionary<long, HashSet<int>> ActionParams(ESD esd)
    {
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
            if (bad.Contains((m, k)) || !reach.Contains((m, k))) continue;
            if (!res.TryGetValue(m, out var set)) res[m] = set = [];
            set.Add(k);
        }
        return res;
    }
}
