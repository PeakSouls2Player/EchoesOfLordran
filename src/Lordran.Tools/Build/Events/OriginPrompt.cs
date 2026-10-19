using SoulsFormats;

namespace Lordran.Tools.Build.Events;

/// <summary>
/// Session 10 (asy-items), rules C1/C2: the DS1 class ("origin") of a DS2 character.
///
/// DS1 picks one of ten classes at character creation and its events read it with `4:11 IF Player's Class(c)` (4 uses
/// in DSR: the Asylum's class corpses 11810400 ×10 slots, common 290 Knight/Cleric, common 740 Pyromancer). DS2 can't
/// read a class in its scripts (V-code: no class getter among the 456 vanilla EDD specs; f130304 = stats,
/// FUN_140470f00), and a DS2 character never had a DS1 class. Rule C1: the class is ten synthetic persistent flags
/// (<see cref="FlagRules.Ds1Origin"/>, S rows of the slot table, global blocks 10/20). Rule C2 (user decision,
/// session 10): DS1's own new-game point asks once. Mechanism:
///  - <see cref="OpeningFlag"/> finds DS1's opening in DS1's data: in a map's event 0, a `1003:1 SKIP IF Event Flag(F
///    ON)` whose skipped span plays a cutscene in the same map (`2002:4`) and sets F (`2003:2(F, ON)`) — the Asylum's
///    first-load branch [10]-[12] (cutscene 180101, flag 11810002); `origin-check` lists it over every DS1 map (only
///    m18_01 has it);
///  - a group (<see cref="Group"/>) waits for F (set by the translated event 0 on the first load; already set on a
///    character that passed the opening before this rule existed: asked at its next Asylum load) while no class flag is
///    set, then asks with DS2's YES/NO window through DS1's ten classes in DS1's order: text = new mapevent rows
///    "<DS1 class name>\n<DS1 class description>" from DS1 Menu_others_ 132020+c / 132320+c (<see cref="TextId"/>);
///    YES → that class's flag; NO (or cancel) → the next class; NO on all ten → Deprived (DS1's last class: the corpses
///    then never stay all enabled — a design default, not DS1 data). DS2's window always pre-selects NO (rulebook §7, V
///    s9-12). The window is vanilla's form for a text without a parameter, 130441(text, 0, -1, 0, 0, 0) (15 vanilla talk
///    ESDs, e.g. talk_m10_04/14/15; mapevent 1220 "What is your choice?" in talk_m10_27, m20_24, m50_37; the talk
///    translator's form, V in game s9-12 Oscar). Argument 4 picks the text's parameter: 2 = %s, the item name of
///    argument 5 (event ESDs' "Use %s?" 130441(1002, 4|3, 220, 2, 60537000, 0)), 1 = %d, a number, 0 = none (V-data;
///    fix pass: the first build used (text, 4, 220, 2, 0, 0) = the item name of item 0, which vanilla never does)
///    → wait f130422() != 0 (shown) → f130421() 1 YES / 2 NO / 3 cancel (V-data, tb-event l.601-607);
///  - hosting (fix pass, rule C2b): the group goes into every map that has DS1's opening OR whose DS1 script reads the
///    class (4:11), keyed on "DS1's opening flag ON and no class known" (the opening flag is a global-block flag, found
///    once over all DS1 maps: <see cref="Opening"/>), so a character without a class is asked in any map whose class
///    readers would otherwise wait for ever (common 290/740 once common.emevd is translated: the maps that host them
///    call <see cref="Add"/> with readsClass);
///  - every DS1 event that reads the class starts only once the class is known (<see cref="KnownExpr"/>; the
///    translator's 4:11 rule): in DS1 the class exists before any event runs, so an event never sees "no class";
///  - build option `--ds1-origin N` (<see cref="Preset"/>, tests): no prompt; the group sets class N (and clears the
///    others) at every load, so one test copy can be rebuilt with another class.
/// Generalises: the only DS1 class reads are the 4 above; the opening exists once in DS1 (origin-check). Co-op: the
/// flags are global-block flags (per player offline; online see coop-flags D9: a guest reads the host's copy, U).
/// </summary>
public static class OriginPrompt
{
    public const int Classes = 10;
    /// <summary>The prompt's ESD group / EventParam row (rule-made groups 37xxx; 37410 is unused by every track).</summary>
    public const int Group = 37410;
    /// <summary>DS1 Menu_others_ class names 132020 + c and descriptions 132320 + c (DSR msg\ENGLISH\menu.msgbnd).</summary>
    public const int NameText = 132020, DescriptionText = 132320;
    /// <summary>Test builds (build-pack --ds1-origin N): the class to preset instead of asking; null = ask.</summary>
    public static int? Preset { get; set; }
    /// <summary>Seconds after the opening flag before the first window (the map's load fade; P).</summary>
    public const float Delay = 2f;

    /// <summary>DS1's ClassType enum (EMEDF), for labels only: the texts come from DS1's FMG.</summary>
    public static string ClassName(int c) => c switch
    {
        0 => "Warrior", 1 => "Knight", 2 => "Wanderer", 3 => "Thief", 4 => "Bandit",
        5 => "Hunter", 6 => "Sorcerer", 7 => "Pyromancer", 8 => "Cleric", 9 => "Deprived", _ => $"class {c}",
    };

    /// <summary>The mapevent row of class c's question (DS1 text rule: 18000000 + DS1 id % 1000000).</summary>
    public static int TextId(int c) => 18_000_000 + NameText + c;

    /// <summary>DS1's opening flag F of a map's event 0 (see the summary), or null.</summary>
    public static int? OpeningFlag(EMEVD emevd, Dictionary<(int, int), (string Name, int[] Types)> defs, string ds1Map)
    {
        var ev0 = emevd.Events.FirstOrDefault(e => e.ID == 0);
        if (ev0 == null) return null;
        if (ds1Map.Length < 6 || !int.TryParse(ds1Map[1..3], out int area) || !int.TryParse(ds1Map[4..6], out int block)) return null;
        var ins = ev0.Instructions;
        for (int i = 0; i < ins.Count; i++)
        {
            if (ins[i] is not { Bank: 1003, ID: 1 }) continue;
            var v = EmevdTranslator.Decode(defs, ins[i]);
            if (v.Length < 4 || (int)v[1] != 1 || (int)v[2] != 0) continue;
            int flag = (int)v[3], end = Math.Min(ins.Count, i + 1 + (int)v[0]);
            bool cutscene = false, sets = false;
            for (int k = i + 1; k < end; k++)
            {
                var w = EmevdTranslator.Decode(defs, ins[k]);
                if (ins[k] is { Bank: 2002, ID: 4 } && w.Length >= 5 && (int)w[3] == area && (int)w[4] == block) cutscene = true;
                if (ins[k] is { Bank: 2003, ID: 2 } && w.Length >= 2 && (int)w[0] == flag && (int)w[1] == 1) sets = true;
            }
            if (cutscene && sets) return flag;
        }
        return null;
    }

    /// <summary>"The DS1 class is known": f6(class 0) != 0 || … || f6(class 9) != 0 (no final A1), or null when the
    /// slot table has no S rows.</summary>
    public static byte[]? KnownExpr()
    {
        byte[]? e = null;
        for (int c = 0; c < Classes; c++)
        {
            if (FlagRules.Ds1Origin(c) is not int f) return null;
            var t = FlagExpr(f, true);
            e = e == null ? t : [.. e, .. t, 0x99];
        }
        return e;
    }

    /// <summary>The prompt group (or the preset group), or null when the map has no DS1 opening.</summary>
    public static Dictionary<long, ESD.State>? States(int openingFlag, out string note)
    {
        var known = KnownExpr() ?? throw new InvalidDataException("the flag slot table has no ds1-origin S rows (flag-survey ds1 --write-table)");
        int Cf(int c) => FlagRules.Ds1Origin(c)!.Value;
        const long Done = 9001;
        var states = new Dictionary<long, ESD.State> { [0] = S([], (True, 1)), [Done] = S([]) };
        if (Preset is int p)
        {
            states[1] = S(Enumerable.Range(0, Classes).Select(c => C(130101, Cf(c), c == p ? 1 : 0)), (True, Done));
            note = $"origin preset (--ds1-origin {p}): class {p} {ClassName(p)} set at every load, no prompt";
            return states;
        }
        // 1: class known → done; DS1's opening reached (its flag) → after the load fade, ask
        states[1] = S([], ([.. known, 0xA1], Done), ([.. FlagExpr(openingFlag, true), 0xA1], 2));
        states[2] = S([], ([.. StateTime(Delay), 0xA1], 50));
        for (int c = 0; c < Classes; c++)
        {
            // 50+c: no YES/NO window open any more (the previous answer's window has closed, so its result can't be read
            // as this one's) → 10+2c: show class c's question, wait until it is shown → 11+2c: its answer
            long closed = 50 + c, ask = 10 + 2 * c, answer = ask + 1, yes = 100 + c;
            long no = c + 1 < Classes ? 50 + c + 1 : 100 + Classes - 1; // NO on the last class → Deprived (9)
            states[closed] = S([], ([.. Call0(130422), 0x40, 0x95, 0xA1], ask));
            states[ask] = S([C(130441, TextId(c), 0, -1, 0, 0, 0)], ([.. Call0(130422), 0x40, 0x96, 0xA1], answer));
            states[answer] = S([], ([.. Call0(130421), 0x41, 0x95, 0xA1], yes), ([.. Call0(130421), 0x42, 0x95, 0xA1], no), ([.. Call0(130421), 0x43, 0x95, 0xA1], no));
            states[yes] = S([C(130101, Cf(c), 1)], (True, Done));
        }
        note = $"origin prompt: on DS1's opening flag {openingFlag} (or later, while no class is set) YES/NO through DS1's {Classes} classes (mapevent {TextId(0)}..{TextId(Classes - 1)}); all NO = Deprived";
        return states;
    }

    /// <summary>The DS1 class texts as mapevent rows (DS1 Menu_others_: name, a line break, the description).</summary>
    public static void Texts(Pack pack, Ds1Source ds1)
    {
        for (int c = 0; c < Classes; c++)
        {
            string name = ds1.Text("Menu_others_", NameText + c), desc = ds1.Text("Menu_others_", DescriptionText + c);
            if (string.IsNullOrEmpty(name)) throw new InvalidDataException($"DS1 Menu_others_ {NameText + c} (class {c} name) is empty");
            pack.SetText("mapevent", TextId(c), (name.Trim() + "\n" + desc.Trim()).Trim());
        }
    }

    /// <summary>The DS1 map and flag of DS1's opening over every DS1 map's event script (cached; null when there is none
    /// or more than one: origin-check fails then).</summary>
    public static (string Map, int Flag)? Opening(string dsrRoot)
    {
        if (opening.TryGetValue(dsrRoot, out var o)) return o;
        var defs = Commands.EmevdDump.Defs();
        var found = new List<(string Map, int Flag)>();
        foreach (var f in Directory.GetFiles(Path.Combine(dsrRoot, "event"), "m*.emevd.dcx").Order())
        {
            string m = Path.GetFileName(f)[..12];
            if (OpeningFlag(EMEVD.Read(f), defs, m) is int flag) found.Add((m, flag));
        }
        return opening[dsrRoot] = found.Count == 1 ? found[0] : null;
    }
    static readonly Dictionary<string, (string Map, int Flag)?> opening = [];

    /// <summary>Does a DS1 event script read the class (4:11 IF Player's Class)?</summary>
    public static bool ReadsClass(EMEVD emevd) => emevd.Events.Any(e => e.Instructions.Any(i => i is { Bank: 4, ID: 11 }));

    /// <summary>Adds the group + EventParam row to a map whose DS1 script has DS1's opening or reads the class (rule C2b;
    /// no-op otherwise). <paramref name="readsClass"/>: the map also hosts other class readers (translated common events).</summary>
    public static void Add(Pack pack, Ds1Source ds1, ESD esd, PARAM events, PARAM.Row ownerless, EMEVD emevd, string ds1Map, bool readsClass = false)
    {
        int? own = OpeningFlag(emevd, Commands.EmevdDump.Defs(), ds1Map);
        if (own == null && !readsClass && !ReadsClass(emevd)) return;
        var (openMap, ds1Flag) = own is int f0 ? (ds1Map, f0)
            : Opening(ds1.Root) ?? throw new InvalidDataException($"{ds1Map} reads the DS1 class but DS1's opening isn't unique (origin-check)");
        if (FlagRules.Map(openMap[..6], ds1Flag) is not int flag) { pack.Note($"{ds1Map}: DS1 opening flag {ds1Flag} has no DS2 flag: no origin prompt"); return; }
        if (esd.StateGroups.ContainsKey(Group) || events.Rows.Any(r => r.ID == Group)) throw new InvalidDataException($"{ds1Map}: ESD group / EventParam row {Group} is already used");
        var states = States(flag, out string note)!;
        if (Preset == null) Texts(pack, ds1);
        esd.StateGroups[Group] = states;
        var row = new PARAM.Row(ownerless) { ID = Group };
        row["stateMapId"].Value = Convert.ChangeType(Group, row["stateMapId"].Value.GetType());
        events.Rows.Add(row);
        pack.Note($"{ds1Map}: {note} (group {Group}; {(own != null ? "DS1's opening is here" : "the map hosts DS1 class readers")}); class flags " + string.Join(", ", Enumerable.Range(0, Classes).Select(c => $"{ClassName(c)} {FlagRules.Ds1Origin(c)}")));
    }

    // ---- ESD helpers (the translator's encodings)
    static readonly byte[] True = [0x41, 0xA1];
    static byte[] Push(int v) => v is >= -64 and < 64 ? [(byte)(0x40 + v)] : [0x82, .. BitConverter.GetBytes(v)];
    static byte[] Int(int v) => [.. Push(v), 0xA1];
    static byte[] Call0(int f) => [0x82, .. BitConverter.GetBytes(f), 0x84, 0xA6];
    static byte[] FlagExpr(int flag, bool on) => [0x46, .. Push(flag), 0x85, 0xA6, 0x40, on ? (byte)0x96 : (byte)0x95];
    static byte[] StateTime(float s) => [0x43, 0x84, 0xA6, 0x80, .. BitConverter.GetBytes(s), 0x94, 0x40, 0x96];
    static ESD.CommandCall C(int id, params int[] args) => new(1, id, args.Select(Int).ToArray());
    static ESD.State S(IEnumerable<ESD.CommandCall> entry, params (byte[] If, long Then)[] conds)
    {
        var s = new ESD.State();
        s.EntryCommands.AddRange(entry);
        foreach (var (c, t) in conds) s.Conditions.Add(new ESD.Condition(t, c));
        return s;
    }
}
