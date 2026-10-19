using SoulsFormats;
using static Lordran.Tools.Build.Ai.Esb;

namespace Lordran.Tools.Build.Ai;

/// <summary>
/// Splits a generated battle machine into state groups that stay inside vanilla's envelope (session 8, track enemies fix pass,
/// rule AI10b): the 485 vanilla AI ESDs have at most 93 states per group (validate\esd_envelope.py), our largest battle
/// machines had 282 (400 once regions were capped). The machine's state graph without its think state is acyclic (decision
/// regions lead down to sub-goal queues, every queue and every interrupt reaction ends at think). <b>Units</b> are the largest
/// states whose reach (everything below them) fits one group; the <b>upper</b> states above them (the top of the think tree)
/// go into the main group, breadth first, and into upper sub-groups when they do not fit. Units are packed into closed groups
/// (the group whose reach grows least by a unit takes it, so units sharing queues and tails share a group; a state reached from
/// units in several groups is copied into each). An edge from an upper group to a unit or an upper state placed elsewhere goes
/// to a <b>stub</b>, a state that calls the group holding it (<c>6:G(k)</c>, k = which of G's entries, as vanilla's 2147483644
/// dispatches on p0) and waits; "back to think" inside a sub-group is a return (<c>7:-1(0)</c>), so the stub's caller goes
/// back to think in turn. Random slots are per logic, not per call (vanilla: 249 groups read slots only their callers or
/// siblings roll), so a roll made in one group is read in another. Conditions shared by several states stay shared inside one
/// group only (as in vanilla: 36197 shared conditions, none across groups). Nesting: weapon group → main → (upper sub-group) →
/// unit group → goal machine (→ Attack from ApproachStep): at most 7 levels, vanilla goes to 8 (validate\esd_calldepth.py).
/// <para>Cost: a stub adds two transitions on the way in (stub, dispatcher) and one per level on the way back. Alternatives
/// measured on the Undead Soldier's 400 states (255005): breadth-first bins 28 groups / 1265 copies + 632 stubs; shared tails
/// called instead of copied 16 groups / 650 + 628; think regions vs queues 21 groups / 474 + 1120; this scheme 16 groups /
/// 1059 + 268.</para>
/// </summary>
public static class GroupSplit
{
    /// <summary>States per group (vanilla max 93).</summary>
    public const int MaxStates = 90;
    /// <summary>Entry tests per dispatcher state (vanilla max 31 conditions per state, one is the chain to the next).</summary>
    public const int MaxDispatch = 28;

    public sealed class Result
    {
        public long Main;
        public readonly Dictionary<long, Dictionary<long, ESD.State>> Groups = [];
        /// <summary>(group, state) → the original state it copies (stubs, dispatchers and returns have none).</summary>
        public readonly Dictionary<(long Group, long State), long> OrigOf = [];
        public readonly Dictionary<long, List<(long Group, long State)>> Copies = [];
        /// <summary>(group, stub state) → (callee group, entry number k, 1-based; 0 = the callee has one entry and takes no argument).</summary>
        public readonly Dictionary<(long Group, long State), (long Callee, int Entry)> Stubs = [];
        /// <summary>Sub-group → its entries (original state ids), k = index + 1.</summary>
        public readonly Dictionary<long, List<long>> Entries = [];
        public readonly Dictionary<long, long> ReturnStateOf = [];
        public long Think;
        public int MaxGroupStates => Groups.Values.Max(g => g.Count);
    }

    sealed class Bin
    {
        public long Id;
        public bool Main;
        public readonly List<long> Entries = [];
        public List<long> Members = [];
        public HashSet<long> Outlined = [];
    }

    static IEnumerable<ESD.Condition> Flat(IEnumerable<ESD.Condition> cs) => cs.SelectMany(c => new[] { c }.Concat(Flat(c.Subconditions)));

    /// <param name="states">The compiled machine (ids 0..N-1, state 0 → think).</param>
    /// <param name="think">The think state (everything ends there).</param>
    /// <param name="newGroupId">Hands out ids for the extra groups.</param>
    public static Result Split(Dictionary<long, ESD.State> states, long think, long mainId, Func<long> newGroupId)
    {
        var res = new Result { Main = mainId };
        IEnumerable<long> Succ(long s) => Flat(states[s].Conditions).Select(c => c.TargetState).OfType<long>().Where(t => t != think && t != 0).Distinct();
        if (states.Count <= MaxStates)
        {
            res.Groups[mainId] = states;
            foreach (var k in states.Keys) { res.OrigOf[(mainId, k)] = k; res.Copies[k] = [(mainId, k)]; }
            res.Think = think;
            return res;
        }
        var closureCache = new Dictionary<long, HashSet<long>>();
        HashSet<long> Closure(long v)
        {
            if (closureCache.TryGetValue(v, out var hit)) return hit;
            var set = new HashSet<long> { v };
            foreach (var t in Succ(v)) set.UnionWith(Closure(t));
            return closureCache[v] = set;
        }
        List<long> Bfs(IEnumerable<long> from, Func<long, bool>? through = null)
        {
            var order = new List<long>(); var seen = new HashSet<long>(); var q = new Queue<long>();
            foreach (var e in from) if (seen.Add(e)) q.Enqueue(e);
            while (q.Count > 0) { var s = q.Dequeue(); order.Add(s); if (through != null && !through(s)) continue; foreach (var t in Succ(s)) if (seen.Add(t)) q.Enqueue(t); }
            return order;
        }
        int Dispatchers(int entries) => entries <= 1 ? 1 : (entries + MaxDispatch - 1) / MaxDispatch;

        // Upper nodes: states whose reach does not fit one group (the top of the think tree). Units: the states below them that
        // do (whole subtrees with their sub-goal queues). Units are packed into closed groups (no stubs inside: one level of
        // nesting below the upper groups), the group whose reach grows least taking each, so units sharing queues and tails
        // share a group; upper nodes go into the main group (and, when they do not fit, upper sub-groups), with a stub per unit.
        var entry = Flat(states[think].Conditions).Select(c => c.TargetState).OfType<long>().Single(t => t != think);
        int unitMax = MaxStates - 2;   // a unit group: dispatcher + return
        bool Upper(long v) => Closure(v).Count > unitMax;
        var upper = Bfs([entry], Upper).Where(Upper).ToList();
        var units = upper.SelectMany(Succ).Where(t => !Upper(t)).Distinct().OrderByDescending(t => Closure(t).Count).ThenBy(t => t).ToList();
        var bins = new List<Bin>();
        var entryOf = new Dictionary<long, (Bin Bin, int K)>();   // original state → the bin that has it as an entry
        var unitBins = new List<(Bin Bin, HashSet<long> Set)>();
        foreach (var u in units)
        {
            var cu = Closure(u);
            if (cu.Count > unitMax) throw new InvalidDataException($"battle machine split: unit {u} reaches {cu.Count} states (more than one group)");
            (Bin Bin, HashSet<long> Set)? best = null;
            int bestGrowth = int.MaxValue;
            foreach (var ub in unitBins)
            {
                int union = ub.Set.Count + cu.Count(x => !ub.Set.Contains(x));
                if (union > MaxStates - 1 - Dispatchers(ub.Bin.Entries.Count + 1)) continue;
                if (union - ub.Set.Count < bestGrowth) { bestGrowth = union - ub.Set.Count; best = ub; }
            }
            if (best == null) { best = (new Bin { Id = newGroupId() }, new HashSet<long>()); unitBins.Add(best.Value); bins.Add(best.Value.Bin); }
            best.Value.Bin.Entries.Add(u);
            best.Value.Set.UnionWith(cu);
            entryOf[u] = (best.Value.Bin, best.Value.Bin.Entries.Count);
        }
        foreach (var (ub, set) in unitBins) { ub.Members = Bfs(ub.Entries).Where(set.Contains).ToList(); ub.Outlined = []; }
        // upper groups: breadth first over upper nodes; a unit or an upper node left out → stub
        var main = new Bin { Id = mainId, Main = true };
        main.Entries.Add(entry);
        bins.Insert(0, main);
        var work = new Queue<Bin>([main]);
        while (work.Count > 0)
        {
            var b = work.Dequeue();
            int capacity = b.Main ? MaxStates - 2 : MaxStates - 1 - Dispatchers(b.Entries.Count);
            var order = Bfs(b.Entries, Upper).Where(Upper).ToList();
            bool fit = false;
            for (int limit = order.Count; limit >= b.Entries.Count; limit--)
            {
                var members = order.Take(limit).Union(b.Entries).ToList();
                var set = members.ToHashSet();
                var outlined = members.SelectMany(Succ).Where(t => !set.Contains(t)).ToHashSet();
                if (members.Count + outlined.Count <= capacity) { b.Members = members; b.Outlined = outlined; fit = true; break; }
            }
            if (!fit) throw new InvalidDataException($"battle machine split: no fit for an upper group with entries {string.Join(",", b.Entries)}");
            // outlined upper nodes → upper sub-groups; a sub-group takes entries while they and their stubs fit
            Bin? open = null;
            int Need(IEnumerable<long> es) { var e = es.ToHashSet(); return e.Count + e.SelectMany(Succ).Where(x => !e.Contains(x)).Distinct().Count() + Dispatchers(e.Count) + 1; }
            foreach (var t in order.Where(b.Outlined.Contains))
            {
                if (entryOf.ContainsKey(t)) continue;
                if (open == null || Need([.. open.Entries, t]) > MaxStates) { open = new Bin { Id = newGroupId() }; bins.Add(open); work.Enqueue(open); }
                open.Entries.Add(t);
                entryOf[t] = (open, open.Entries.Count);
            }
            foreach (var t in b.Outlined) if (!entryOf.ContainsKey(t)) throw new InvalidDataException($"battle machine split: state {t} outlined but placed nowhere");
        }

        // materialise
        foreach (var b in bins)
        {
            var g = new Dictionary<long, ESD.State>();
            var ids = new Dictionary<long, long>();
            long next = 0;
            var dispatch = new List<long>();
            if (b.Main) { ids[0] = next++; ids[think] = next++; }
            else for (int i = 0; i < Dispatchers(b.Entries.Count); i++) dispatch.Add(next++);
            foreach (var m in b.Members) if (!ids.ContainsKey(m)) ids[m] = next++;
            var stubOf = new Dictionary<long, long>();
            foreach (var t in b.Outlined.OrderBy(t => t)) stubOf[t] = next++;
            long ret = b.Main ? -1 : next++;
            long back = b.Main ? ids[think] : ret;
            long Map(long t) => t == think ? back : ids.TryGetValue(t, out long x) ? x : stubOf.TryGetValue(t, out long y) ? y
                : throw new InvalidDataException($"battle machine split: state {t} neither placed nor stubbed in group {b.Id}");
            var condCopy = new Dictionary<ESD.Condition, ESD.Condition>(ReferenceEqualityComparer.Instance);
            ESD.Condition CopyCond(ESD.Condition c)
            {
                if (condCopy.TryGetValue(c, out var done)) return done;
                var n = new ESD.Condition { Evaluator = [.. c.Evaluator], TargetState = c.TargetState is long t ? Map(t) : null };
                foreach (var p in c.PassCommands) n.PassCommands.Add(CopyCmd(p));
                foreach (var s in c.Subconditions) n.Subconditions.Add(CopyCond(s));
                return condCopy[c] = n;
            }
            foreach (var (orig, nid) in ids)
            {
                var src = states[orig];
                var st = new ESD.State();
                st.EntryCommands.AddRange(src.EntryCommands.Select(CopyCmd));
                st.WhileCommands.AddRange(src.WhileCommands.Select(CopyCmd));
                st.ExitCommands.AddRange(src.ExitCommands.Select(CopyCmd));
                foreach (var c in src.Conditions) st.Conditions.Add(CopyCond(c));
                g[nid] = st;
                res.OrigOf[(b.Id, nid)] = orig;
                if (!res.Copies.TryGetValue(orig, out var list)) res.Copies[orig] = list = [];
                list.Add((b.Id, nid));
            }
            foreach (var (t, sid) in stubOf)
            {
                var (callee, k) = entryOf[t];
                bool single = callee.Entries.Count == 1;
                var call = single ? Call(callee.Id) : Call(callee.Id, E.I(k));
                g[sid] = S([call], null, [.. Returned(back)]);
                res.Stubs[(b.Id, sid)] = (callee.Id, single ? 0 : k);
            }
            if (!b.Main)
            {
                if (b.Entries.Count == 1) g[dispatch[0]] = S(null, null, Go(ids[b.Entries[0]]));
                else
                    for (int i = 0; i < dispatch.Count; i++)
                    {
                        var conds = new List<ESD.Condition>();
                        for (int k = i * MaxDispatch; k < Math.Min(b.Entries.Count, (i + 1) * MaxDispatch); k++)
                            conds.Add(Go(ids[b.Entries[k]], E.P(0).Eq(k + 1)));
                        conds.Add(Go(i + 1 < dispatch.Count ? dispatch[i + 1] : ret));
                        g[dispatch[i]] = S(null, null, [.. conds]);
                    }
                g[ret] = ReturnState(0);
                res.Entries[b.Id] = [.. b.Entries];
                res.ReturnStateOf[b.Id] = ret;
            }
            else res.Think = ids[think];
            if (g.Count > MaxStates + 3) throw new InvalidDataException($"battle machine split: group {b.Id} has {g.Count} states");
            if (!g.Keys.Order().SequenceEqual(Enumerable.Range(0, g.Count).Select(i => (long)i))) throw new InvalidDataException($"battle machine split: group {b.Id} ids not 0..N-1");
            res.Groups[b.Id] = g;
        }
        return res;
    }

    static ESD.CommandCall CopyCmd(ESD.CommandCall c) => new(c.CommandBank, c.CommandID, c.Arguments.Select(a => (byte[])a.Clone()).ToArray());
}
