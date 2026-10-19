using SoulsFormats;

namespace Lordran.Tools.Build.Events;

/// <summary>
/// Every DS1 (DSR) event flag the game's data references (session 9, track npcflags): event scripts (all maps + common:
/// flag-typed instruction arguments with Initialize Event parameters resolved, flag ranges, event values as their bits,
/// every event's own id and id + slot), talk scripts (SetEventFlag, GetEventFlag, AddTalkListData), DS1 params
/// (ItemLotParam getItemFlagId / cumulateNumFlagId, ShopLineupParam eventFlag, ObjActParam spQualifiedPassEventFlag) and
/// MSB ObjAct events. Used to size and check <see cref="FlagRules"/> (every class must land in DS2 storage) and by
/// `flag-survey ds1`. DS1's own data never changes, so the census is a fixed input.
/// </summary>
public static class Ds1FlagCensus
{
    /// <summary>(bank, id) → flag roles by argument index: F flag, T+I typed flag (type 0 id, 1 event id, 2 event id + slot),
    /// R range start/end, V event value (base, bits), C count range (type, start, end).</summary>
    static readonly Dictionary<(int, int), (char Role, int[] Args)[]> Roles = new()
    {
        [(3, 0)] = [('T', [2, 3])], [(3, 1)] = [('Q', [2, 3, 4])], [(3, 10)] = [('Q', [1, 2, 3])],
        [(3, 12)] = [('V', [1, 2])], [(3, 20)] = [('V', [1, 2]), ('V', [4, 5])], [(5, 2)] = [('F', [1])],
        [(1003, 0)] = [('T', [1, 2])], [(1003, 1)] = [('T', [2, 3])], [(1003, 2)] = [('T', [2, 3])],
        [(1003, 3)] = [('Q', [2, 3, 4])], [(1003, 4)] = [('Q', [2, 3, 4])],
        [(2003, 2)] = [('F', [0])], [(2003, 9)] = [('F', [0])], [(2003, 17)] = [('R', [0, 1])], [(2003, 22)] = [('R', [0, 1])],
        [(2003, 25)] = [('F', [3]), ('F', [4])], [(2003, 31)] = [('V', [0, 1])], [(2003, 32)] = [('V', [0, 1])],
        [(2003, 33)] = [('F', [0])], [(2003, 34)] = [('F', [2])], [(2003, 42)] = [('V', [0, 2]), ('V', [1, 2])],
        [(2003, 43)] = [('V', [0, 1])], [(2004, 32)] = [('R', [3, 4])], [(2004, 45)] = [('W', [1])],
        [(2005, 9)] = [('F', [0])], [(2005, 12)] = [('F', [0])], [(2007, 12)] = [('V', [2, 3])],
        [(2009, 0)] = [('F', [0]), ('F', [1])], [(2009, 1)] = [('F', [0]), ('F', [2])], [(2009, 2)] = [('F', [0])],
        [(2009, 3)] = [('F', [0])], [(2009, 5)] = [('F', [0])],
        // 2003:8 Set Event State(event id, slot, state): with state 1 (Restart) the target (id + slot) gets a restart
        // request flag (EmevdTranslator rule R9); recorded with Source "restart …" (not a DS1 flag of its own)
        [(2003, 8)] = [('X', [0, 1, 2])],
    };

    public const string RestartSource = "restart";

    public sealed record Use(int Flag, string Source);

    /// <summary>All uses; a flag can appear many times (one entry per source kind and file, deduplicated).</summary>
    public static List<Use> Collect(string dsrRoot, Action<string>? warn = null)
    {
        var uses = new HashSet<Use>();
        var defs = Commands.EmevdDump.Defs();
        string evDir = Path.Combine(dsrRoot, "event");
        var files = Directory.GetFiles(evDir, "*.emevd.dcx").Order().ToList();
        var common = files.FirstOrDefault(f => Path.GetFileName(f).StartsWith("common", StringComparison.OrdinalIgnoreCase));
        EMEVD? commonEmevd = common != null ? EMEVD.Read(common) : null;
        foreach (string f in files)
        {
            string map = Path.GetFileName(f).Split('.')[0];
            var emevd = f == common ? commonEmevd! : EMEVD.Read(f);
            Emevd(emevd, map, defs, commonEmevd, uses, warn);
        }
        foreach (var (map, talk, bytes) in Commands.Ds1Talk.AllTalks())
        {
            ESD esd;
            try { esd = ESD.Read(bytes); } catch { warn?.Invoke($"talk t{talk} ({map}) unreadable"); continue; }
            foreach (var (_, states) in esd.StateGroups)
                foreach (var (_, s) in states)
                {
                    foreach (var c in s.EntryCommands.Concat(s.WhileCommands).Concat(s.ExitCommands)) TalkCommand(c, $"talk t{talk}", uses);
                    foreach (var cond in Flatten(s.Conditions))
                    {
                        foreach (var p in cond.PassCommands) TalkCommand(p, $"talk t{talk}", uses);
                        foreach (var call in EsdCalls.Calls(cond.Evaluator))
                            if (call.Fn == 15 && call.Args.Length > 0 && call.Args[0] is EsdCalls.Const k) uses.Add(new((int)k.V, $"talk t{talk} GetEventFlag"));
                    }
                }
        }
        var ps = Formats.ParamStore.LoadDsr(dsrRoot);
        void Field(string param, string field)
        {
            if (!ps.Params.TryGetValue(param, out var p)) { warn?.Invoke($"DS1 param {param} missing"); return; }
            foreach (var row in p.Rows)
            {
                var cell = row.Cells.FirstOrDefault(c => c.Def.InternalName == field);
                if (cell != null && Convert.ToInt64(cell.Value) is > 0 and var v) uses.Add(new((int)v, $"{param}.{field}"));
            }
        }
        foreach (string fld in new[] { "getItemFlagId", "cumulateNumFlagId" }) Field("ItemLotParam", fld);
        Field("ShopLineupParam", "eventFlag");
        Field("ObjActParam", "spQualifiedPassEventFlag");
        foreach (string msbPath in Directory.GetFiles(Path.Combine(dsrRoot, "map", "MapStudio"), "*.msb"))
        {
            MSB1 msb;
            try { msb = MSB1.Read(msbPath); } catch { continue; }
            foreach (var o in msb.Events.ObjActs)
                if (o.EventFlagID > 0) uses.Add(new(o.EventFlagID, $"msb ObjAct {Path.GetFileNameWithoutExtension(msbPath)}"));
            foreach (var o in msb.Events.PseudoMultiplayers)
                if (o.EventFlagID > 0) uses.Add(new(o.EventFlagID, $"msb PseudoMultiplayer {Path.GetFileNameWithoutExtension(msbPath)}"));
        }
        return uses.ToList();
    }

    static void TalkCommand(ESD.CommandCall c, string where, HashSet<Use> uses)
    {
        foreach (var arg in c.Arguments)
            foreach (var call in EsdCalls.Calls(arg))
                if (call.Fn == 15 && call.Args.Length > 0 && call.Args[0] is EsdCalls.Const k) uses.Add(new((int)k.V, where + " GetEventFlag"));
        if (c.CommandBank != 1) return;
        int idx = c.CommandID switch { 11 => 0, 19 => 2, _ => -1 };
        if (idx < 0 || c.Arguments.Count <= idx) return;
        if (EsdCalls.Parse(c.Arguments[idx]) is EsdCalls.Const v && v.V > 0) uses.Add(new((int)v.V, where + (c.CommandID == 11 ? " SetEventFlag" : " AddTalkListData")));
    }

    static IEnumerable<ESD.Condition> Flatten(IEnumerable<ESD.Condition> cs) => cs.SelectMany(c => new[] { c }.Concat(Flatten(c.Subconditions)));

    static void Emevd(EMEVD emevd, string map, Dictionary<(int, int), (string Name, int[] Types)> defs, EMEVD? common,
        HashSet<Use> uses, Action<string>? warn)
    {
        var byId = emevd.Events.ToDictionary(e => e.ID);
        // event-local: param byte offset (source) → role, filled while scanning; resolved at Initialize Event sites
        var paramRoles = new Dictionary<long, List<(char Role, int Src, int Partner, int PartnerSrc)>>();
        foreach (var ev in emevd.Events)
        {
            uses.Add(new((int)ev.ID, $"event id {map}"));
            for (int i = 0; i < ev.Instructions.Count; i++)
            {
                var ins = ev.Instructions[i];
                if (!Roles.TryGetValue((ins.Bank, ins.ID), out var roles) || !defs.TryGetValue((ins.Bank, ins.ID), out var def)) continue;
                var offs = Offsets(def.Types);
                var subs = ev.Parameters.Where(p => p.InstructionIndex == i).ToDictionary(p => (int)p.TargetStartByte, p => (int)p.SourceStartByte);
                long? Val(int arg) => arg < offs.Length && offs[arg] + Size(def.Types[arg]) <= ins.ArgData.Length
                    ? Read(ins.ArgData, offs[arg], def.Types[arg]) : null;
                int? Sub(int arg) => arg < offs.Length && subs.TryGetValue(offs[arg], out int s) ? s : null;
                foreach (var (role, args) in roles)
                {
                    string src = $"emevd {map} e{ev.ID} {ins.Bank}:{ins.ID}";
                    switch (role)
                    {
                        case 'F':
                            if (Sub(args[0]) is int s0) AddRole(paramRoles, ev.ID, ('F', s0, -1, -1));
                            else if (Val(args[0]) is long f and > 0) uses.Add(new((int)f, src));
                            break;
                        case 'T': // typed: type arg, id arg
                            {
                                long type = Sub(args[0]) is null ? Val(args[0]) ?? 0 : 0;
                                if (type == 0)
                                {
                                    if (Sub(args[1]) is int s1) AddRole(paramRoles, ev.ID, ('F', s1, -1, -1));
                                    else if (Val(args[1]) is long f2 and > 0) uses.Add(new((int)f2, src));
                                }
                                break; // types 1/2 = the event's own id (+ slot): added per Initialize Event
                            }
                        case 'Q': // typed range: type, start, end
                        case 'R':
                            {
                                int a0 = role == 'Q' ? args[1] : args[0], a1 = role == 'Q' ? args[2] : args[1];
                                if (role == 'Q' && (Sub(args[0]) is not null || (Val(args[0]) ?? 0) != 0)) break;
                                int? sa = Sub(a0), sb = Sub(a1);
                                if (sa is int x && sb is int y) AddRole(paramRoles, ev.ID, ('R', x, -1, y));
                                else if (sa is null && sb is null && Val(a0) is long lo and > 0 && Val(a1) is long hi && hi >= lo && hi - lo < 1000)
                                    for (long f3 = lo; f3 <= hi; f3++) uses.Add(new((int)f3, src + " range"));
                                else if (sa is int x2 && Val(a1) is long hi2) AddRole(paramRoles, ev.ID, ('S', x2, (int)hi2, -2));
                                else if (sb is int y2 && Val(a0) is long lo2) { uses.Add(new((int)lo2, src + " range start")); AddRole(paramRoles, ev.ID, ('F', y2, -1, -1)); }
                                break;
                            }
                        case 'X': // Set Event State(event, slot, state 1 = Restart)
                            {
                                if (Sub(args[2]) is null && Val(args[2]) != 1) break;
                                int? si = Sub(args[0]), ss = Sub(args[1]);
                                long id = si is null ? Val(args[0]) ?? 0 : 0, sl = ss is null ? Val(args[1]) ?? 0 : 0;
                                if (si is null && ss is null) { if (id > 0) uses.Add(new((int)(id + sl), $"{RestartSource} {src}")); }
                                else AddRole(paramRoles, ev.ID, ('X', si ?? -1, ss ?? -1, (int)(si is null ? id : sl)));
                                break;
                            }
                        case 'V': // event value: base, bits
                        case 'W':
                            {
                                long bits = role == 'V' && args.Length > 1 ? (Sub(args[1]) is null ? Val(args[1]) ?? 1 : 8) : 8;
                                if (Sub(args[0]) is int sv) AddRole(paramRoles, ev.ID, ('V', sv, (int)bits, -1));
                                else if (Val(args[0]) is long b and > 0) for (long q = 0; q < Math.Max(1, bits); q++) uses.Add(new((int)(b + q), src + " value"));
                                break;
                            }
                    }
                }
            }
        }
        // Initialize Event sites: event own flags (id + slot) and parameter flags
        foreach (var ev in emevd.Events)
            foreach (var ins in ev.Instructions.Where(x => x.Bank == 2000 && x.ID == 0 && x.ArgData.Length >= 8))
            {
                int slot = BitConverter.ToInt32(ins.ArgData, 0);
                long target = BitConverter.ToUInt32(ins.ArgData, 4);
                var pargs = ins.ArgData[8..];
                uses.Add(new((int)(target + slot), $"event id+slot {map}"));
                if (!byId.ContainsKey(target) && common?.Events.All(e => e.ID != target) != false) warn?.Invoke($"{map}: Initialize Event {target} not found");
                if (!paramRoles.TryGetValue(target, out var roles)) continue;
                foreach (var (role, src, partner, psrc) in roles)
                {
                    int P(int at) => at + 4 <= pargs.Length ? BitConverter.ToInt32(pargs, at) : 0;
                    string where = $"emevd {map} init e{target} slot {slot}";
                    switch (role)
                    {
                        case 'F': if (P(src) > 0) uses.Add(new(P(src), where)); break;
                        case 'R': { int lo = P(src), hi = P(psrc); if (lo > 0 && hi >= lo && hi - lo < 1000) for (int f = lo; f <= hi; f++) uses.Add(new(f, where + " range")); break; }
                        case 'S': { int lo = P(src); if (lo > 0 && partner >= lo && partner - lo < 1000) for (int f = lo; f <= partner; f++) uses.Add(new(f, where + " range")); break; }
                        case 'V': { int b = P(src); if (b > 0) for (int q = 0; q < Math.Max(1, partner); q++) uses.Add(new(b + q, where + " value")); break; }
                        case 'X': // restart target: (id source or -1, slot source or -1, the constant half)
                            {
                                // (src = id's parameter or -1, partner = slot's parameter or -1, psrc = the constant one)
                                long id = src >= 0 ? P(src) : psrc, sl = partner >= 0 ? P(partner) : (src >= 0 ? psrc : 0);
                                if (id > 0) uses.Add(new((int)(id + sl), $"{RestartSource} {where}"));
                                break;
                            }
                    }
                }
            }
    }

    static void AddRole(Dictionary<long, List<(char, int, int, int)>> d, long ev, (char, int, int, int) r)
    {
        if (!d.TryGetValue(ev, out var l)) d[ev] = l = [];
        l.Add(r);
    }

    static int Size(int type) => type switch { 0 or 3 => 1, 1 or 4 => 2, _ => 4 };

    static int[] Offsets(int[] types)
    {
        var offs = new int[types.Length];
        int off = 0;
        for (int i = 0; i < types.Length; i++)
        {
            int size = Size(types[i]);
            off = (off + size - 1) / size * size;
            offs[i] = off;
            off += size;
        }
        return offs;
    }

    static long Read(byte[] d, int off, int type) => type switch
    {
        0 => d[off], 1 => BitConverter.ToUInt16(d, off), 2 => BitConverter.ToUInt32(d, off), 3 => (sbyte)d[off],
        4 => BitConverter.ToInt16(d, off), 5 => BitConverter.ToInt32(d, off), _ => 0,
    };
}

/// <summary>
/// ESD expression bytecode → a small tree, enough to find function calls and their constant / parameter arguments
/// (session 9, track npcflags: flag censuses of DS1 talk and DS2 event/talk scripts). Opcodes as in Commands.Esd.Expr.
/// </summary>
public static class EsdCalls
{
    public abstract record Node;
    public sealed record Const(long V) : Node;
    public sealed record Param(int I) : Node;
    public sealed record Call(long Fn, Node[] Args) : Node;
    public sealed record Other(Node[] Kids) : Node;

    public static Node? Parse(byte[] b) => Run(b, null);

    public static List<Call> Calls(byte[] b) { var l = new List<Call>(); Run(b, l); return l; }

    static Node? Run(byte[] b, List<Call>? calls)
    {
        var st = new Stack<Node>();
        Node Pop() => st.Count > 0 ? st.Pop() : new Other([]);
        for (int i = 0; i < b.Length; i++)
        {
            byte op = b[i];
            if (op < 0x80) st.Push(new Const(op - 0x40));
            else if (op == 0x80) { float f = BitConverter.ToSingle(b, i + 1); st.Push(f == Math.Floor(f) && Math.Abs(f) < 2e9 ? new Const((long)f) : new Other([])); i += 4; }
            else if (op == 0x81) { double d = BitConverter.ToDouble(b, i + 1); st.Push(d == Math.Floor(d) && Math.Abs(d) < 4e9 ? new Const((long)d) : new Other([])); i += 8; }
            else if (op == 0x82) { st.Push(new Const(BitConverter.ToInt32(b, i + 1))); i += 4; }
            else if (op >= 0x84 && op <= 0x8A)
            {
                var a = new Node[op - 0x84];
                for (int k = a.Length - 1; k >= 0; k--) a[k] = Pop();
                var id = Pop();
                var c = new Call(id is Const ic ? ic.V : -1, a);
                calls?.Add(c);
                st.Push(c);
            }
            else if (op == 0x8D) { var x = Pop(); st.Push(x is Const xc ? new Const(-xc.V) : new Other([x])); }
            else if (op >= 0x8C && op <= 0x9A) { var r = Pop(); var l = Pop(); st.Push(new Other([l, r])); }
            else if (op == 0xA1) break;
            else if (op == 0xA5)
            {
                int j = i + 1;
                while (j + 1 < b.Length && (b[j] | b[j + 1]) != 0) j += 2;
                i = j + 1;
                st.Push(new Other([]));
            }
            else if (op == 0xA6 || op == 0xB7 || (op >= 0xA7 && op <= 0xAE)) { /* no-op / abort / register store (value stays) */ }
            else if (op >= 0xAF && op <= 0xB6) st.Push(new Other([]));
            else if (op == 0xB8) { var x = Pop(); st.Push(x is Const pc ? new Param((int)pc.V) : new Other([x])); }
            else st.Push(new Other([]));
        }
        return st.Count > 0 ? st.Peek() : null;
    }
}
