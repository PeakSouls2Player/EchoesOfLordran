using Lordran.Tools.Build.Events;
using SoulsFormats;
using static Lordran.Tools.Build.Talk.TalkExpr;

namespace Lordran.Tools.Build.Talk;

/// <summary>
/// DS1 covenants in DS2 (session 11, track cov11; design data\sandbox\covenants\DESIGN.md, rules K1-K8 of
/// docs\translation-rules.md). Data: <c>Mappings\covenants.tsv</c> (the user's decisions D1-D3 in one table). DS1's own talk
/// ESDs and EMEVD keep their flow; this class is the rule set the talk translator (<see cref="TalkTranslator"/>) and the
/// event translator (<see cref="EmevdTranslator"/>) call for every covenant construct, so a covenant NPC works once its map
/// is ported (no per-NPC authoring).
///
/// DS2 facts used (decompile <c>decomp\ds2s</c> + vanilla scripts; NOTES "engine details"):
///  - covenant = PlayerStatus +0x1ad, ids 0-9 only; 130381(id) joins (id 0 leaves; the handler sets "left covenant k" flag
///    200899 + k), f130361() reads it, f130362(id) its level 0-3 (VowParam thresholds of DS2's contribution);
///  - 130382(id, n) adds contribution ONLY while id is the current covenant (FUN_140203af0: +0x1ad == id, then
///    FUN_14038bb00 + level recompute FUN_140204270; V-code), so an offering at a DS1 altar raises the DS2 rank by DS2 rules;
///  - global event variables (130122 set, 130124 add, f130122 get): 256 int32 per character, ids 1-255 (FUN_140479fc0 /
///    FUN_14047a130 / FUN_140479fe0 reject 0 and ≥ count; the store is built with 0x100 in FUN_14047a1d0), written into the
///    same 0x21c8-byte event record as the event flags (FUN_1402e3a70 → FUN_14047a1c0 → record +0xde4: saved, V-code);
///    vanilla scripts and NpcEventParam use ids ≤ 63 and 200-243 (census in NOTES), so DS1's counters take 150-156;
///  - the NPC window (130461 + NpcEventParam) returns f130452() == 5 for its pledge button (bPledge) and 6 for its votive
///    button (bVotive), and does nothing itself: vanilla talks join (130381) and take the offering (131401 「アイテム消費」 +
///    130382) in their own states (talk_m10_17 g7870: ret 6 → join routine 2147483605, ret 4 → offering 2147483611;
///    Saulden g2147483590 the same), so DS1's own join / offer states run after the button, with DS1's dialogs (rule K1b);
///  - "phantom present" = f130661(0, 1) &gt;= 1 (vanilla join routine talk_m10_17 g2147483606 state 3 → mapevent 1311).
/// </summary>
public static class Covenants
{
    public sealed record Row(int Ds1, string Name, int Ds2, string Ds2Name, string? SubFlag, bool SubFlagOn, int PointStat, int CounterVar,
        string Offering, IReadOnlyDictionary<int, int> KillRank, string Why);

    public const int FnCovenant = 130361, FnLevel = 130362, CmdJoin = 130381, CmdContribution = 130382,
        CmdVarAdd = 130124, FnVar = 130122, CmdConsume = 131401, FnItemCount = 131401, FnMultiplayer = 130661;
    /// <summary>DS1 player stats (talk ChangePlayerStat / ComparePlayerStat): 10 humanity, 11 current covenant, 14 Forest
    /// Hunter kill rank, 15 points of the current covenant (add only), 31 the covenant the join dialog offers (UI).</summary>
    public const int StatHumanity = 10, StatCovenant = 11, StatKillRank = 14, StatPoints = 15, StatOffered = 31;
    /// <summary>DS1 goods 500 Humanity (its offering and "no humanity" test go through the item rule: Human Effigy).</summary>
    public const int Ds1Humanity = 500;
    /// <summary>Group of the sub-flag housekeeping event (<see cref="AddHousekeeping"/>).</summary>
    public const int HousekeepingGroup = 37430;

    static List<Row>? rows;
    static string gate = "counter";

    public static IReadOnlyList<Row> Rows => rows ??= Load(Path.Combine(AppContext.BaseDirectory, "Mappings", "covenants.tsv"));
    /// <summary>D3: "counter" (DS1 counts) or "level" (DS2 levels).</summary>
    public static string Gate { get { _ = Rows; return gate; } }
    public static Row? Of(int ds1) => Rows.FirstOrDefault(r => r.Ds1 == ds1);
    public static Row? ByPointStat(int stat) => Rows.FirstOrDefault(r => r.PointStat == stat);

    static List<Row> Load(string path)
    {
        var list = new List<Row>();
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var c = line.Split('\t');
            if (c[0] == "option") { if (c.Length > 2 && c[1] == "gate") gate = c[2].Trim(); continue; }
            if (c.Length < 10 || !int.TryParse(c[0], out int n)) continue;
            string? sub = c[4] is "-" or "" ? null : c[4][1..];
            var kill = new Dictionary<int, int>();
            if (c[8] != "-")
                foreach (var kv in c[8].Split(','))
                    if (kv.Split(':') is [var k, var v]) kill[int.Parse(k)] = int.Parse(v);
            list.Add(new Row(n, c[1], int.Parse(c[2]), c[3], sub, c[4].StartsWith('+'), c[5] == "-" ? 0 : int.Parse(c[5]),
                c[6] == "-" ? 0 : int.Parse(c[6]), c[7], kill, c[9]));
        }
        if (gate is not ("counter" or "level")) throw new InvalidDataException($"covenants.tsv: option gate {gate} (counter | level)");
        foreach (var r in list)
        {
            if (r.Ds2 is < 0 or > 9) throw new InvalidDataException($"covenants.tsv: DS1 {r.Ds1} → DS2 id {r.Ds2}: DS2 holds ids 0-9 only");
            if (r.CounterVar is < 0 or > 255 || r.CounterVar is (> 0 and <= 63) or (>= 200)) throw new InvalidDataException($"covenants.tsv: counter variable {r.CounterVar} (1-255, clear of vanilla's ≤ 63 and 200+)");
        }
        return list;
    }

    /// <summary>The synthetic flags of the table (sub-identity keys, rule C1's S rows of the flag slot table).</summary>
    public static IEnumerable<string> SubFlagKeys => Rows.Select(r => r.SubFlag).OfType<string>().Distinct();
    public static int? SubFlag(string key) => FlagSlots.Current.Synthetic.TryGetValue(key, out int g) ? g : null;

    static TalkExpr Current => F(FnCovenant);

    /// <summary>
    /// Rule K2: "the player is in DS1 covenant n" as a DS2 expression: f130361() == ds2 id, plus the sub-identity flag when
    /// the DS2 id is shared (Princess's Guard / Warrior of Sunlight). n = 0 ("none"): the DS2 ids no DS1 covenant maps to
    /// (0 and Company of Champions 8 by default). Null when the table has no row or the flag no slot.
    /// </summary>
    public static TalkExpr? Member(int n)
    {
        if (n == 0)
        {
            var used = Rows.Select(r => r.Ds2).ToHashSet();
            var free = Enumerable.Range(0, 10).Where(d => !used.Contains(d)).Select(d => B(Eq, Current, N(d))).ToList();
            return free.Count == 0 ? N(0) : OrAll(free);
        }
        if (Of(n) is not { } row) return null;
        var test = B(Eq, Current, N(row.Ds2));
        if (row.SubFlag == null) return test;
        if (SubFlag(row.SubFlag) is not int f) return null;
        return B(And, test, B(row.SubFlagOn ? Ne : Eq, F(6, N(f)), N(0)));
    }

    /// <summary>DS1 covenant comparisons (ComparePlayerStat(11, op, v), DS1/EMEDF comparison codes 0 == 1 != 2 &gt; 3 &lt;
    /// 4 &gt;= 5 &lt;=) → the DS1 covenants that satisfy it, as a disjunction of <see cref="Member"/>.</summary>
    public static TalkExpr? CovenantCompare(int op, int v)
    {
        var set = Enumerable.Range(0, 10).Where(n => Cmp(op, n, v) is true).ToList();
        if (Cmp(op, 0, v) == null) return null;
        if (set.Count == 10) return N(1);
        if (set.Count == 0) return N(0);
        var terms = new List<TalkExpr>();
        foreach (int n in set) { if (Member(n) is not { } m) return null; terms.Add(m); }
        return OrAll(terms);
    }

    static bool? Cmp(int op, int a, int b) => op switch { 0 => a == b, 1 => a != b, 2 => a > b, 3 => a < b, 4 => a >= b, 5 => a <= b, _ => null };
    public static byte? EsdOp(int op) => op switch { 0 => Eq, 1 => Ne, 2 => Gt, 3 => Lt, 4 => Ge, 5 => Le, _ => null };

    /// <summary>Rule K1b: DS1's join command ChangePlayerStat(11, 5, n) → 130381(ds2 id) (0 = leave) + every sub-identity flag
    /// (on for the joined covenant's own key, off otherwise). DS2's 130381 itself drops the former covenant and sets its
    /// "left covenant" flag (200899 + old id), as DS2's own pledges do.</summary>
    public static List<(int Bank, int Cmd, TalkExpr[] Args)>? Join(int n)
    {
        var row = n == 0 ? null : Of(n);
        if (n != 0 && row == null) return null;
        var list = new List<(int, int, TalkExpr[])> { (1, CmdJoin, [N(row?.Ds2 ?? 0)]) };
        foreach (var key in SubFlagKeys)
        {
            if (SubFlag(key) is not int f) return null;
            list.Add((1, 130101, [N(f), N(row != null && row.SubFlag == key && row.SubFlagOn ? 1 : 0)]));
        }
        return list;
    }

    /// <summary>Rule K3: DS1's ChangePlayerStat(15, 0, k) (+k points to the current covenant) → per candidate DS1 covenant
    /// with a point stat: 130382(ds2 id, k) (DS2 adds only to the current id: its rank by DS2 rules) and, with gate counter,
    /// 5:130124(member?, counter, k) (the DS1 count, kept only for the covenant the player is in).</summary>
    public static List<(int Bank, int Cmd, TalkExpr[] Args)> Points(IEnumerable<Row> candidates, int k)
    {
        var list = new List<(int, int, TalkExpr[])>();
        var cands = candidates.Where(r => r.PointStat > 0).ToList();
        foreach (int ds2 in cands.Select(r => r.Ds2).Distinct()) list.Add((1, CmdContribution, [N(ds2), N(k)]));
        if (Gate == "counter")
            foreach (var r in cands)
                if (Member(r.Ds1) is { } m) list.Add((5, CmdVarAdd, [m, N(r.CounterVar), N(k)]));
        return list;
    }

    /// <summary>Rule K4: a DS1 point count (f60(stat, N) "the points reached N"; ComparePlayerStat(stat, op, N)) → the
    /// counter (gate counter) or the DS2 level of the covenant (gate level: N 10/30/80/100 → level 1/2/3/3, ≥).</summary>
    public static TalkExpr? PointCompare(int stat, byte op, int value)
    {
        if (ByPointStat(stat) is not { } r) return null;
        if (Gate == "counter") return B(op, F(FnVar, N(r.CounterVar)), N(value));
        int level = value >= 80 ? 3 : value >= 30 ? 2 : value >= 10 ? 1 : 0;
        return B(Ge, F(FnLevel, N(r.Ds2)), N(level));
    }

    /// <summary>Rule K4 (Forest Hunter): ComparePlayerStat(14, op, k) (DS1 kill rank) → the DS2 level of the row with a
    /// kill_rank map (k → k').</summary>
    public static TalkExpr? KillRank(byte op, int k)
    {
        var r = Rows.FirstOrDefault(x => x.KillRank.Count > 0);
        if (r == null || !r.KillRank.TryGetValue(k, out int lv)) return null;
        return B(op, F(FnLevel, N(r.Ds2)), N(lv));
    }

    /// <summary>DS1 f52() "a phantom is present" (join refused, 10010747) → DS2's own test in its join routine.</summary>
    public static TalkExpr PhantomPresent => B(Ge, F(FnMultiplayer, N(0), N(1)), N(1));

    /// <summary>Rule K7: DS1's kneel flags 840-849 (the talk sets one, common event 840 plays the kneel and clears it, the
    /// talk waits for it to clear): DS2's pledge has no kneel and nothing would clear them → not written, read as 0 (the
    /// kneel is over at once).</summary>
    public static bool IsKneelFlag(int ds1Flag) => ds1Flag is >= 840 and <= 849;

    // ------------------------------------------------------------------ mirror flags (common events 870 / 715)
    /// <summary>A DS1 condition of a mirror event.</summary>
    public sealed record Ds1Cond(string Kind, int A, int B, bool On);

    static Dictionary<int, List<Ds1Cond>>? mirrors;

    /// <summary>
    /// Rule K2b (mirror flags): a DS1 common event that only keeps a flag equal to a condition (sets it OFF, waits for the
    /// condition (MAIN or AND_01 + MAIN), sets it ON, waits for it to fail, clears it, restarts: event 870 = F850 + n
    /// "in covenant n", event 715 = "may offer the Soul of Gwyn") is not ported as an event: every read of the flag is the
    /// condition itself (no event host, no save state, no lag). Read from DS1's common.emevd by shape; the conditions it
    /// supports: 3:0 flag, 3:4 / 3:16 item, 4:12 covenant. Null = not a mirror flag.
    /// </summary>
    public static IReadOnlyList<Ds1Cond>? Mirror(int ds1Flag)
    {
        mirrors ??= LoadMirrors(Program.DefaultDsrRoot);
        return mirrors.TryGetValue(ds1Flag, out var l) ? l : null;
    }

    static Dictionary<int, List<Ds1Cond>> LoadMirrors(string dsrRoot)
    {
        var result = new Dictionary<int, List<Ds1Cond>>();
        string path = Path.Combine(dsrRoot, "event", "common.emevd.dcx");
        if (!File.Exists(path)) return result;
        var common = EMEVD.Read(path);
        var defs = Commands.EmevdDump.Defs();
        var byId = common.Events.ToDictionary(e => e.ID);
        if (!byId.TryGetValue(0, out var root)) return result;
        foreach (var init in root.Instructions.Where(i => i.Bank == 2000 && i.ID == 0 && i.ArgData.Length >= 8))
        {
            long id = BitConverter.ToUInt32(init.ArgData, 4);
            if (!byId.TryGetValue(id, out var ev)) continue;
            byte[] args = init.ArgData[8..];
            var ins = ev.Instructions.Select((x, i) => EmevdTranslator.Substitute(ev, i, x, args)).ToList();
            if (ins.Count < 3 || ins[^1] is not { Bank: 1000, ID: 4 } || EmevdTranslator.Decode(defs, ins[^1]) is not [1, ..]) continue; // ends: Restart
            int k = 0;
            int? cleared = null;
            if (ins[0] is { Bank: 2003, ID: 2 } && EmevdTranslator.Decode(defs, ins[0]) is [var f0, 0, ..]) { cleared = (int)f0; k = 1; }
            var conds = new List<Ds1Cond>();
            int? flag = null;
            for (; k < ins.Count; k++)
            {
                var x = ins[k];
                var v = EmevdTranslator.Decode(defs, x);
                if ((x.Bank, x.ID) is (2003, 2)) { if (v is [var f, 1, ..]) flag = (int)f; break; }
                if ((x.Bank, x.ID) is (0, 0) && v is [0, 1, 1, ..]) continue; // IF Condition Group(MAIN, PASS, AND_01)
                if (v.Length == 0 || (int)v[0] is not (0 or 1)) { conds = null; break; }
                switch (x.Bank, x.ID)
                {
                    case (3, 0) when v.Length >= 4 && v[2] == 0: conds.Add(new Ds1Cond("flag", (int)v[3], 0, v[1] != 0)); break;
                    case (3, 4) or (3, 16) when v.Length >= 4: conds.Add(new Ds1Cond("item", (int)v[1], (int)v[2], v[3] != 0)); break;
                    case (4, 12) when v.Length >= 2: conds.Add(new Ds1Cond("covenant", (int)v[1], 0, true)); break;
                    default: conds = null; break;
                }
                if (conds == null) break;
            }
            if (conds == null || conds.Count == 0 || flag is not int fl || cleared is int c0 && c0 != fl) continue;
            // the rest must clear the flag again (870: Set OFF after the condition fails; 715 clears it at the top of each
            // restart), else it is a one-way latch, not a mirror
            bool clears = cleared == fl || ins.Skip(k + 1).Any(x => x is { Bank: 2003, ID: 2 } && EmevdTranslator.Decode(defs, x) is [var f1, 0, ..] && (int)f1 == fl);
            if (!clears) continue;
            result[fl] = conds;
        }
        return result;
    }

    // ------------------------------------------------------------------ events
    /// <summary>Rule K2 for EMEVD 4:12 IF Player's Covenant(n): the bytes of <see cref="Member"/> (no closing 0xA1, the
    /// event translator's Cond.Expr), or null.</summary>
    public static byte[]? MemberBytes(int n) => Member(n) is { } e ? e.Write()[..^1] : null;

    /// <summary>Rule K6: EMEVD 2004:38 Betray Character's Current Covenant → leave (130381(0), DS2 sets "left covenant") and
    /// clear the sub-identity flags. Each item: (command, args).</summary>
    public static List<(int Cmd, int[] Args)>? Betray()
    {
        var list = new List<(int, int[])> { (CmdJoin, [0]) };
        foreach (var key in SubFlagKeys)
        {
            if (SubFlag(key) is not int f) return null;
            list.Add((130101, [f, 0]));
        }
        return list;
    }

    /// <summary>
    /// The sub-identity housekeeping (rule K2, D2-A): a sub-identity flag only means something while the player is in its
    /// DS2 covenant, so an event clears it whenever the current DS2 covenant is another (the player left or switched
    /// through DS2's own NPCs, or a DS2 betrayal): f130361() != id &amp;&amp; flag → flag off. Ownerless group in each Lordran
    /// map's event script. No-op when the table has no sub flags.
    /// </summary>
    public static void AddHousekeeping(Pack pack, ESD esd, PARAM events, PARAM.Row ownerless, string ds2Map)
    {
        var subs = Rows.Where(r => r.SubFlag != null).GroupBy(r => r.SubFlag!).ToList();
        if (subs.Count == 0) return;
        if (esd.StateGroups.ContainsKey(HousekeepingGroup) || events.Rows.Any(r => r.ID == HousekeepingGroup))
            throw new InvalidDataException($"{ds2Map}: ESD group / EventParam row {HousekeepingGroup} is already used");
        var states = new Dictionary<long, ESD.State>();
        states[0] = new ESD.State();
        states[0].Conditions.Add(new ESD.Condition(1, N(1).Write()) { TargetState = 1 });
        var wait = new ESD.State();
        states[1] = wait;
        long next = 2;
        foreach (var g in subs)
        {
            if (SubFlag(g.Key) is not int f) throw new InvalidDataException($"covenants.tsv: sub flag {g.Key} has no S row in {FlagSlots.FileName}");
            int ds2 = g.First().Ds2;
            var clear = new ESD.State();
            clear.EntryCommands.Add(new ESD.CommandCall(1, 130101, N(f).Write(), N(0).Write()));
            clear.Conditions.Add(new ESD.Condition(1, N(1).Write()) { TargetState = 1 });
            states[next] = clear;
            wait.Conditions.Add(new ESD.Condition(next, B(And, B(Ne, Current, N(ds2)), B(Ne, F(6, N(f)), N(0))).Write()) { TargetState = next });
            pack.Note($"{ds2Map}: covenant sub flag {g.Key} ({f}) cleared whenever the DS2 covenant is not {ds2} (group {HousekeepingGroup}, rule K2 / D2-A)");
            next++;
        }
        esd.StateGroups[HousekeepingGroup] = states;
        var row = new PARAM.Row(ownerless) { ID = HousekeepingGroup };
        row["stateMapId"].Value = Convert.ChangeType(HousekeepingGroup, row["stateMapId"].Value.GetType());
        events.Rows.Add(row);
    }

    // ------------------------------------------------------------------ items
    /// <summary>DS2 Rat Tail (Rat King's offering: stack 99, no use), the template of a DS1 covenant offering ported as a
    /// new item (Eye of Death: its "use" sends enemies into other worlds, dropped offline).</summary>
    const int OfferingTemplate = 62140000;

    /// <summary>DS1 goods the table names as a covenant offering (3:id).</summary>
    public static bool IsOffering(int ds1Goods) => Rows.Any(r => r.Offering.StartsWith($"3:{ds1Goods} "));

    /// <summary>Ports a DS1 covenant offering the item map lists as new (system-new: Eye of Death) once per pack, with the
    /// GoodsPorter id scheme and the Rat Tail as template (stackable, not usable, sellable like DS2's offerings).</summary>
    public static int PortOffering(Pack pack, Ds1Source ds1, int ds1Id)
    {
        int id = GoodsPorter.Id(ds1Id);
        var items = pack.Param("ItemParam");
        if (items[id] != null) return id;
        if (ds1.Params.Params["EquipParamGoods"][ds1Id] is not { } src) throw new InvalidDataException($"DS1 goods {ds1Id} not found");
        string name = ds1.Text("Item_name_", ds1Id);
        var item = pack.AddRow("ItemParam", items[OfferingTemplate]!, id, $"[Lordran] {name}");
        item["iconId"].Value = Convert.ChangeType(id, item["iconId"].Value.GetType());
        WeaponPorter.PortIcon(ds1, pack, Convert.ToInt32(src["iconId"].Value), id, OfferingTemplate);
        pack.SetText("itemname", id, name);
        pack.SetText("simpleexplanation", id, ds1.Text("Item_description_", ds1Id));
        pack.SetText("detailedexplanation", id, ds1.LoreText("Item_long_desc_", ds1Id));
        pack.Note($"goods {ds1Id} {name} → item {id} (covenant offering, template Rat Tail {OfferingTemplate}; DS1's use effect dropped)");
        return id;
    }
}
