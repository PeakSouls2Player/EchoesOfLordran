using SoulsFormats;

namespace Lordran.Tools.Build.Events;

/// <summary>
/// Session 11 (track ret11), rule <b>W1 world-state fold</b> (asy10 enemies.md §3 W1; prototype `world-fold` in
/// asy10\scratch-enemies): which DS1 characters DS1's load-time events leave enabled, as a function of persistent event
/// flags, and the DS2 GeneratorParam start/stop condition that reproduces it.
/// <para>Method: DS1's load events 0 and 50 plus every event they initialise (slot arguments substituted) run to completion
/// or to their first wait that does not pass, once per assignment of the flags those events test (a truth table); Change
/// Character Enable State (2004:5) records the state, flag sets (2003:2 / 2003:22) update the assignment as DS1 would. A MAIN
/// flag wait that does not pass during the load is DS1's <i>live switch</i>: its continuation is run separately with the flag
/// as DS1 later sets it, and a character it disables gets a <b>stop condition</b> on that literal (D4: DS2 despawns an active
/// generator whose stop condition holds). Each character's table is matched against DS2's seven judge forms
/// (GENERATOR_JUDGE_ENUM 0-6 over ≤ 3 literals; condition 7 = flag ON, 8 = flag OFF: decompile FUN_1404125f0, V-code).</para>
/// <para>Bosses are excluded by the caller (BossBattleParam.killedEventId and the translated boss events own them; vanilla
/// bosses start unconditionally); a support of more than 3 flags or no matching form = fallback (no condition: the
/// translated event commands stay). Asylum result (V offline = the prototype's table): 11 first-visit characters
/// <c>8/11810000</c> + stop <c>7/11810000</c>, 12 return-trip <c>7/11810000</c>, the Black Knights judge 1 <c>7/11810000
/// 8/11810850|851</c>, hollow Oscar judge 5 <c>7/1073 7/11810000 7/1061</c>, everyone else "always".</para>
/// </summary>
public static class WorldState
{
    /// <summary>A DS1 flag literal (On = the flag must be ON).</summary>
    public sealed record Lit(int Flag, bool On)
    {
        public override string ToString() => $"{(On ? 7 : 8)}/{Flag}";
    }

    /// <summary>A DS2 judge form over DS1 literals: judge 0 = ①, 1 = ①&amp;②, 2 = ①|②, 3 = ①&amp;②&amp;③, 4 = ①|②|③,
    /// 5 = (①&amp;②)|③, 6 = (①|②)&amp;③.</summary>
    public sealed record Form(int Judge, IReadOnlyList<Lit> Lits)
    {
        public override string ToString() => $"judge {Judge}: {string.Join(" ", Lits)}";
    }

    /// <summary>One character's fold: Kind "always" (no DS1 load event toggles it), "never", "condition" (Start set; Stop =
    /// the live switches that disable it) or "fallback" (no DS2 form: event commands stay).</summary>
    public sealed record Entry(int Entity, string Kind, string Ds1Text, Form? Start, IReadOnlyList<Lit> Stop)
    {
        /// <summary>The generator conditions own the character's enable state (rule W1: 2004:5 on it → no command).</summary>
        public bool Owned => Kind is "condition" or "never";
        public override string ToString() => Kind == "condition" ? $"{Ds1Text} → start {Start}{(Stop.Count > 0 ? $", stop {string.Join(" ", Stop.Select(s => $"judge 0: {s}"))}" : "")}" : $"{Ds1Text} ({Kind})";
    }

    sealed record Inst(EMEVD.Event Ev, int Slot, byte[] Args);

    static readonly Dictionary<string, IReadOnlyDictionary<int, Entry>> cache = [];

    /// <summary>The fold of every MSB character with an entity of <paramref name="ds1Map"/> (cached per map).</summary>
    public static IReadOnlyDictionary<int, Entry> Fold(string dsrRoot, string ds1Map)
    {
        if (cache.TryGetValue(ds1Map, out var c)) return c;
        var defs = Commands.EmevdDump.Defs();
        var emevd = EMEVD.Read(Path.Combine(dsrRoot, "event", ds1Map + ".emevd.dcx"));
        var msb = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", ds1Map + ".msb"));
        return cache[ds1Map] = Fold(emevd, msb, defs);
    }

    public static IReadOnlyDictionary<int, Entry> Fold(EMEVD emevd, MSB1 msb, Dictionary<(int, int), (string Name, int[] Types)> defs)
    {
        var events = emevd.Events.GroupBy(e => (long)e.ID).ToDictionary(g => g.Key, g => g.First());
        var chars = msb.Parts.Enemies.Where(e => e.EntityID > 0).Select(e => e.EntityID).Distinct().ToList();

        double[] V(EMEVD.Instruction x) => EmevdTranslator.Decode(defs, x);
        var subCache = new Dictionary<(long, int, string), List<EMEVD.Instruction>>();
        List<EMEVD.Instruction> Sub(Inst i)
        {
            var key = (i.Ev.ID, i.Slot, Convert.ToHexString(i.Args));
            if (subCache.TryGetValue(key, out var l)) return l;
            return subCache[key] = [.. i.Ev.Instructions.Select((x, k) =>
            {
                var d = (byte[])x.ArgData.Clone();
                foreach (var p in i.Ev.Parameters.Where(p => p.InstructionIndex == k))
                    if (p.SourceStartByte + p.ByteCount <= i.Args.Length && p.TargetStartByte + p.ByteCount <= d.Length)
                        Array.Copy(i.Args, p.SourceStartByte, d, p.TargetStartByte, p.ByteCount);
                return new EMEVD.Instruction(x.Bank, x.ID, d);
            })];
        }
        Inst? Init(EMEVD.Instruction x)
        {
            if (x.ArgData.Length < 8) return null;
            int slot = BitConverter.ToInt32(x.ArgData, 0); long id = BitConverter.ToUInt32(x.ArgData, 4);
            return events.TryGetValue(id, out var ev) ? new Inst(ev, slot, x.ArgData[8..]) : null;
        }
        static int Flag(Inst i, double type, double id) => (int)type switch { 1 => (int)i.Ev.ID, 2 => (int)i.Ev.ID + i.Slot, _ => (int)id };

        // the load closure: events 0 and 50 and everything they initialise (with args)
        var roots = new[] { 0L, 50L }.Where(events.ContainsKey).Select(id => new Inst(events[id], 0, [])).ToList();
        var closure = new List<Inst>(); var seen = new HashSet<(long, int)>(); var todo = new Queue<Inst>(roots);
        while (todo.Count > 0)
        {
            var i = todo.Dequeue();
            if (!seen.Add((i.Ev.ID, i.Slot))) continue;
            closure.Add(i);
            foreach (var x in Sub(i).Where(x => (x.Bank, x.ID) is (2000, 0)))
                if (Init(x) is { } n) todo.Enqueue(n);
        }
        var toggling = closure.Where(i => Sub(i).Any(x => (x.Bank, x.ID) is (2004, 5))).ToList();
        var tested = new SortedSet<int>();
        foreach (var i in closure.Where(i => i.Ev.ID is 0 or 50 || toggling.Contains(i)))
            foreach (var x in Sub(i))
            {
                if ((x.Bank, x.ID) is (1003, 1) or (1003, 2) && V(x) is { Length: >= 4 } v) tested.Add(Flag(i, v[2], v[3]));
                if ((x.Bank, x.ID) is (3, 0) && V(x) is { Length: >= 4 } w) tested.Add(Flag(i, w[2], w[3]));
            }
        var allFlags = tested.ToList();
        var initBy = new Dictionary<(long, int), List<(Inst Parent, int Index)>>();
        foreach (var i in closure)
        {
            var ins = Sub(i);
            for (int k = 0; k < ins.Count; k++)
                if ((ins[k].Bank, ins[k].ID) is (2000, 0) && Init(ins[k]) is { } n)
                    (initBy.TryGetValue((n.Ev.ID, n.Slot), out var l) ? l : initBy[(n.Ev.ID, n.Slot)] = []).Add((i, k));
        }
        IEnumerable<int> Tests(Inst i, int upTo) => Sub(i).Take(upTo).Select(V).Zip(Sub(i).Take(upTo))
            .Where(p => (p.Second.Bank, p.Second.ID) is (1003, 1) or (1003, 2) or (3, 0) && p.First.Length >= 4).Select(p => Flag(i, p.First[2], p.First[3]));
        // per-character cone (scales to maps with many quest flags): the flags tested in the instances that toggle the
        // character, plus those its initialisers test before the Initialize Event that starts it (recursively)
        List<int> Cone(int entity)
        {
            var c = new SortedSet<int>(); var vis = new HashSet<(long, int)>();
            var q = new Queue<(Inst, int)>(toggling.Where(i => Sub(i).Any(x => (x.Bank, x.ID) is (2004, 5) && (int)V(x)[0] == entity)).Select(i => (i, int.MaxValue)));
            while (q.Count > 0)
            {
                var (i, upTo) = q.Dequeue();
                foreach (var fl in Tests(i, upTo)) c.Add(fl);
                if (!vis.Add((i.Ev.ID, i.Slot))) continue;
                foreach (var (p, k) in initBy.GetValueOrDefault((i.Ev.ID, i.Slot)) ?? []) q.Enqueue((p, k));
            }
            return [.. c];
        }

        (Dictionary<int, bool[]>, Dictionary<int, HashSet<(int Flag, bool State)>>) Eval(List<int> flags)
        {
            var table = new Dictionary<int, bool[]>();
            var liveOff = new Dictionary<int, HashSet<(int Flag, bool State)>>();
            int rows = 1 << flags.Count;
            foreach (var e in chars) table[e] = new bool[rows];
            for (int r = 0; r < rows; r++)
            {
                var fl = new Dictionary<int, bool>();
                for (int b = 0; b < flags.Count; b++) fl[flags[b]] = (r >> b & 1) != 0;
                var en = chars.ToDictionary(e => e, e => true);
                var blocked = new List<(Inst I, int Pc, int Flag, bool State)>();
                var queue = new Queue<(Inst, int)>(roots.Select(i => (i, 0)));
                var started = new HashSet<(long, int)>();
                void RunFrom(Inst i, int pc, Dictionary<int, bool> f, Dictionary<int, bool> enabled, Queue<(Inst, int)> q, List<(Inst, int, int, bool)>? blk)
                {
                    var ins = Sub(i);
                    var groups = new Dictionary<int, List<bool?>>();
                    while (pc < ins.Count)
                    {
                        var x = ins[pc]; var v = V(x);
                        switch ((x.Bank, x.ID))
                        {
                            case (1003, 1): if ((f.GetValueOrDefault(Flag(i, v[2], v[3])) ? 1 : 0) == (int)v[1]) pc += (int)v[0]; break;
                            case (1003, 2): if ((f.GetValueOrDefault(Flag(i, v[2], v[3])) ? 1 : 0) == (int)v[1]) return; break;
                            case (1003, 5): break;  // SKIP IF multiplayer state: the fold is the host's (single player) view
                            case (1003, 7): break;  // SKIP IF player in/out map: the player is inside
                            case (1000, 3): pc += (int)v[0]; break;
                            case (1000, 4): return;
                            case (2004, 5): if (v.Length >= 2 && enabled.ContainsKey((int)v[0])) enabled[(int)v[0]] = v[1] != 0; break; // session 11 (burg11): an entity no MSB enemy part carries (m10_01 1010904: a character group / other part) is not a character of the fold
                            case (2003, 2): { int id = (int)v[0]; f[id] = (int)v[1] switch { 0 => false, 1 => true, _ => !f.GetValueOrDefault(id) }; break; }
                            case (2003, 22): for (long id = (long)v[0]; id <= (long)v[1] && id - (long)v[0] < 100000; id++) if (f.ContainsKey((int)id) || flags.Contains((int)id)) f[(int)id] = v[2] != 0; break;
                            case (2000, 0): if (Init(x) is { } n && started.Add((n.Ev.ID, n.Slot))) q.Enqueue((n, 0)); break;
                            case (3, 0):
                            {
                                bool ok = (f.GetValueOrDefault(Flag(i, v[2], v[3])) ? 1 : 0) == (int)v[1];
                                if ((int)v[0] == 0) { if (!ok) { blk?.Add((i, pc, Flag(i, v[2], v[3]), (int)v[1] != 0)); return; } }
                                else (groups.TryGetValue((int)v[0], out var l) ? l : groups[(int)v[0]] = []).Add(ok);
                                break;
                            }
                            case (0, 0):
                            {
                                int target = (int)v[2]; bool want = v[1] != 0;
                                var l = groups.GetValueOrDefault(target) ?? [];
                                bool val = target > 0 ? l.Count > 0 && l.All(b => b == true) : l.Any(b => b == true);
                                if ((int)v[0] == 0) { if (val != want) return; }
                                else (groups.TryGetValue((int)v[0], out var l2) ? l2 : groups[(int)v[0]] = []).Add(val == want);
                                break;
                            }
                            default:
                                // any other condition: into a group = unknown; into MAIN = a wait that doesn't pass during the load
                                if (x.Bank is 0 or 1 or 3 or 4 or 5 or 11 && v.Length > 0)
                                {
                                    if ((int)v[0] == 0) return;
                                    (groups.TryGetValue((int)v[0], out var l3) ? l3 : groups[(int)v[0]] = []).Add(null);
                                }
                                break;
                        }
                        pc++;
                    }
                }
                foreach (var root in roots) started.Add((root.Ev.ID, root.Slot));
                while (queue.Count > 0) { var (i, pc) = queue.Dequeue(); RunFrom(i, pc, fl, en, queue, blocked); }
                foreach (var (e, b) in en) table[e][r] = b;
                // live switch: let each blocked MAIN flag wait pass (its flag set as DS1 sets it later in the load)
                foreach (var (bi, bpc, bf, bs) in blocked)
                {
                    var f2 = new Dictionary<int, bool>(fl) { [bf] = bs };
                    var en2 = new Dictionary<int, bool>(en);
                    RunFrom(bi, bpc, f2, en2, new Queue<(Inst, int)>(), null);
                    foreach (var (e, b) in en2) if (en[e] && !b) (liveOff.TryGetValue(e, out var s) ? s : liveOff[e] = []).Add((bf, bs));
                }
            }
            return (table, liveOff);
        }

        var tables = new Dictionary<int, (List<int> Flags, bool[] T, HashSet<(int, bool)>? Live)>();
        if (allFlags.Count <= 14)
        {
            var (tb, lv) = Eval(allFlags);
            foreach (var e in chars) tables[e] = (allFlags, tb[e], lv.GetValueOrDefault(e));
        }
        else
            foreach (var e in chars)
            {
                var cone = Cone(e);
                if (cone.Count > 14) { tables[e] = (cone, [], null); continue; }
                var (tb, lv) = Eval(cone);
                tables[e] = (cone, tb[e], lv.GetValueOrDefault(e));
            }

        var result = new Dictionary<int, Entry>();
        foreach (var (e, (flags, t, live)) in tables)
        {
            if (t.Length == 0) { result[e] = new Entry(e, "fallback", $"cone of {flags.Count} flags (> 14)", null, []); continue; }
            int rows = 1 << flags.Count;
            var support = Enumerable.Range(0, flags.Count).Where(b => Enumerable.Range(0, rows).Any(r => t[r] != t[r ^ (1 << b)])).ToList();
            var stop = live?.Select(s => new Lit(s.Item1, s.Item2)).OrderBy(s => s.Flag).ToList() ?? [];
            if (support.Count == 0) { result[e] = new Entry(e, t[0] ? "always" : "never", t[0] ? "always" : "never", null, stop); continue; }
            if (support.Count > 3) { result[e] = new Entry(e, "fallback", $"{support.Count} flags", null, []); continue; }
            var m = Match(t, support, flags, rows);
            result[e] = m == null ? new Entry(e, "fallback", $"{support.Count} flags (no form)", null, []) : new Entry(e, "condition", m.Value.Text, m.Value.Form, stop);
        }
        return result;
    }

    /// <summary>The first DS2 judge form (fewest literals) whose value equals the table on every assignment.</summary>
    static (string Text, Form Form)? Match(bool[] t, List<int> support, List<int> flags, int rows)
    {
        var lits = support.SelectMany(b => new[] { (b, true), (b, false) }).ToList();
        static bool L((int B, bool On) l, int r) => ((r >> l.B & 1) != 0) == l.On;
        string N((int B, bool On) l) => $"{(l.On ? "" : "!")}{flags[l.B]}";
        var forms = new List<(int Judge, int Arity, Func<bool[], bool> F, Func<string[], string> S)>
        {
            (0, 1, a => a[0], s => s[0]),
            (1, 2, a => a[0] && a[1], s => $"{s[0]} & {s[1]}"),
            (2, 2, a => a[0] || a[1], s => $"{s[0]} | {s[1]}"),
            (3, 3, a => a[0] && a[1] && a[2], s => $"{s[0]} & {s[1]} & {s[2]}"),
            (4, 3, a => a[0] || a[1] || a[2], s => $"{s[0]} | {s[1]} | {s[2]}"),
            (5, 3, a => a[0] && a[1] || a[2], s => $"({s[0]} & {s[1]}) | {s[2]}"),
            (6, 3, a => (a[0] || a[1]) && a[2], s => $"({s[0]} | {s[1]}) & {s[2]}"),
        };
        IEnumerable<List<(int, bool)>> Pick(int k) => k == 0 ? [[]] : Pick(k - 1).SelectMany(pre => lits.Where(l => !pre.Any(q => q.Item1 == l.Item1)).Select(l => pre.Append(l).ToList()));
        foreach (var f in forms)
            foreach (var choice in Pick(f.Arity))
                if (Enumerable.Range(0, rows).All(r => f.F([.. choice.Select(l => L(l, r))]) == t[r]))
                    return (f.S([.. choice.Select(N)]), new Form(f.Judge, [.. choice.Select(l => new Lit(flags[l.Item1], l.Item2))]));
        return null;
    }

    /// <summary>
    /// The DS2 generator conditions of a fold entry, flags through <paramref name="flag"/> (FlagRules F1/F2): null when the
    /// entry needs none ("always"/"fallback") or a flag has no DS2 storage (the caller reports it; the generator keeps its
    /// template start and the event commands stay).
    /// </summary>
    public static (Maps.EnemyTest.GenCondition Start, Maps.EnemyTest.GenCondition? Stop)? ToGenerator(Entry e, Func<int, int?> flag, out string why)
    {
        why = "";
        if (e.Kind == "never") return (new Maps.EnemyTest.GenCondition(0, 0, 0, 0, 0, 0, 0), null); // condition 0 = false
        if (e.Kind != "condition" || e.Start == null) { why = e.Kind; return null; }
        var ids = new List<(byte C, int P)>();
        foreach (var l in e.Start.Lits)
        {
            if (flag(l.Flag) is not int f) { why = $"DS1 flag {l.Flag} has no DS2 storage"; return null; }
            ids.Add(((byte)(l.On ? 7 : 8), f));
        }
        while (ids.Count < 3) ids.Add((0, 0));
        var start = new Maps.EnemyTest.GenCondition((byte)e.Start.Judge, ids[0].C, ids[1].C, ids[2].C, ids[0].P, ids[1].P, ids[2].P);
        Maps.EnemyTest.GenCondition? stop = null;
        if (e.Stop.Count == 1)
        {
            if (flag(e.Stop[0].Flag) is not int sf) { why = $"DS1 flag {e.Stop[0].Flag} (stop) has no DS2 storage"; return null; }
            stop = new Maps.EnemyTest.GenCondition(0, (byte)(e.Stop[0].On ? 7 : 8), 0, 0, sf, 0, 0);
        }
        else if (e.Stop.Count > 1) { why = $"{e.Stop.Count} live switches disable it (one stop literal supported)"; return null; }
        return (start, stop);
    }
}
