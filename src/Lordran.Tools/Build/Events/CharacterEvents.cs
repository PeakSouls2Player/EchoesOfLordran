using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Events;

/// <summary>
/// Session 10, asy-events item e3 (COVERAGE G3/G2/G5/G5b): map-agnostic DS1 → DS2 rules for event instructions on
/// characters and on the player's game state, read from DS1's own data (SpEffectParam, the map EMEVDs, MSB SpawnPoint
/// events). <see cref="EmevdTranslator"/> applies them through <see cref="EmevdTranslator.IContext"/>; <c>npc-events</c>
/// prints the census over every DSR map. Rules (docs/translation-rules.md §4, NOTES e3):
///  - SP1 2004:8 Set SpEffect on a character: a DS1 effect that only changes HP once by a rate of the maximum (instant:
///    effectEndurance 0; changeHpRate ≠ 0; every other effect field at its paramdef default) → 130317(gen, 100 − rate)
///    「ジェネレータ指定でHP割合を設定」 (DS2 handler 0x14046275a: HP = max(1, trunc(maxHP × arg × 0.01)), V-code);
///  - HB 2004:30 Set Character HP Bar Display → no command (DS2 has no event command for a character's own bar);
///  - N1 2004:4 Force Character Death(souls) → 131721(gen, 1) (+ 132211(souls) when DS1 hands the player the souls);
///  - N3 an NPC death event (a DS1 NPC's HP ≤ 0 / dead test reaching the MAIN wait, then flag writes: <see cref="DeathWait"/>)
///    waits <see cref="DeathSettle"/> after the death test before its writes, so the NPC's talk machine (which carries the same death and its drop rows) goes first;
///  - N4 2004:37 Force Character Treasure → no command (DS2 hands the drops out at the death: NpcPorter routes);
///  - G5 3:17 IF Game Cycle(completed c) → f132241() 「ゲーム周回数取得」 compared with c + 1 (DS2 counts the playthrough
///    in progress: vanilla m10_02 「ゲーム2周目以上か？」 = f132241() &gt;= 2);
///  - G5b 4:12 IF Player's Covenant(DS1 covenant n): superseded in session 11 (cov11) by rule K2 (Build/Talk/Covenants:
///    DS1 covenants join DS2 ids by role, the test is f130361() == id); before: never holds (no DS1 covenant joinable);
///  - G2 2003:23 Set Player Respawn Point → no command: DS2 respawns at the last rested bonfire, which only the engine's
///    rest writes (gap, reported per point with its DS1 position).
/// </summary>
public static class CharacterEvents
{
    /// <summary>Rule N3: seconds an NPC death event waits after its death test before writing (the talk machine's death
    /// branch, which also hands out the drop rows, runs every frame while the NPC exists: 6 frames at 30 fps is ample).</summary>
    public const float DeathSettle = 0.2f;

    /// <summary>DS1 SpEffectParam fields that describe how/where an effect applies, not what it does (targets, icon,
    /// visuals, categories): ignored by the "only an HP change" test of rule SP1.</summary>
    static readonly HashSet<string> Inert =
    [
        "iconId", "effectTargetSelf", "effectTargetFriend", "effectTargetEnemy", "effectTargetPlayer", "effectTargetAI",
        "effectTargetLive", "effectTargetGhost", "effectTargetWhiteGhost", "effectTargetBlackGhost", "effectTargetAttacker",
        "dispIconNonactive", "useSpEffectEffect", "bAdjustMagicAblity", "bAdjustFaithAblity", "bGameClearBonus",
        "vfxId", "accumuOverFireId", "accumuOverVal", "accumuUnderFireId", "accumuUnderVal", "accumuVal", "eye_angX",
        "eye_angY", "addDeceasedLv", "vowType0", "vowType1", "vowType2", "vowType3", "vowType4", "vowType5", "vowType6",
        "vowType7", "vowType8", "vowType9", "vowType10", "vowType11", "vowType12", "vowType13", "vowType14", "vowType15",
        "spCategory", "categoryPriority", "saveCategory", "pad1", "pad2", "pad3", "pad4", "pad5", "pad6", "pad7", "pad8",
        "pad_1", "pad_2", "pad_3", "pad_4", "pad_5", "pad_6", "pad_7", "pad_8", "pad_9",
    ];

    /// <summary>DS1 SpEffectParam row (cached per install), null when it doesn't exist.</summary>
    public static PARAM.Row? SpEffect(string dsrRoot, int id) => Ds1Source.LoadParam(dsrRoot, "SpEffectParam")?.Rows.FirstOrDefault(r => r.ID == id);

    /// <summary>The fields of a DS1 SpEffectParam row whose value differs from the field's neutral value, without the inert
    /// ones. Neutral = the value most DS1 SpEffectParam rows carry (DSR's paramdef defaults are not neutral: rate fields
    /// default to 0 there while every effect that leaves them alone holds 1).</summary>
    public static List<string> Effective(PARAM.Row r)
    {
        var neutral = Neutral(r);
        return r.Cells.Where(c => !Inert.Contains(c.Def.InternalName) && c.Value is not byte[] && Key(c.Value) != neutral.GetValueOrDefault(c.Def.InternalName))
            .Select(c => c.Def.InternalName).ToList();
    }

    static Dictionary<string, string>? neutral;
    static Dictionary<string, string> Neutral(PARAM.Row sample)
    {
        if (neutral != null) return neutral;
        var p = Ds1Source.LoadParam(Program.DefaultDsrRoot, "SpEffectParam");
        var rows = p?.Rows ?? [sample];
        neutral = sample.Cells.Where(c => c.Value is not byte[]).Select(c => c.Def.InternalName).ToDictionary(f => f,
            f => rows.Select(r => Key(r[f]!.Value)).GroupBy(k => k).OrderByDescending(g => g.Count()).First().Key);
        return neutral;
    }

    static string Key(object v) => v is float or double ? Convert.ToDouble(v).ToString("R") : v?.ToString() ?? "";

    static double Get(PARAM.Row r, string f) => Convert.ToDouble(r[f]!.Value);

    /// <summary>Rule SP1: the DS2 HP percentage (of the maximum) a DS1 SpEffect leaves on a character at full HP, when the
    /// effect is an instant HP-rate change and nothing else; null otherwise (with <paramref name="why"/>). DS1 changeHpRate
    /// is the percent of the maximum HP the effect takes (negative heals): "Tutorial NPC Dying" 5492 = 99.9 → 0.1 %.</summary>
    public static float? HpPercent(string dsrRoot, int id, out string why)
    {
        var r = SpEffect(dsrRoot, id);
        if (r == null) { why = $"DS1 SpEffect {id} doesn't exist"; return null; }
        var eff = Effective(r);
        if (Get(r, "effectEndurance") != 0) { why = $"DS1 SpEffect {id} lasts {Get(r, "effectEndurance")} s (not instant)"; return null; }
        if (!eff.Contains("changeHpRate")) { why = $"DS1 SpEffect {id} changes no HP rate ({Describe(eff)})"; return null; }
        var rest = eff.Where(f => f is not ("changeHpRate" or "effectEndurance")).ToList();
        if (rest.Count > 0) { why = $"DS1 SpEffect {id} also changes {Describe(rest)}"; return null; }
        double rate = Get(r, "changeHpRate");
        double pct = Math.Clamp(100 - rate, 0, 100); // a heal past the maximum (5450: −100) is a full heal
        why = $"DS1 SpEffect {id} (changeHpRate {rate:0.###}, instant, nothing else) → HP {pct:0.###} % of the maximum";
        return (float)pct;
    }

    static string Describe(List<string> fields) => fields.Count == 0 ? "no effect fields" : string.Join(", ", fields.Take(6)) + (fields.Count > 6 ? $" … ({fields.Count})" : "");

    /// <summary>Rule N3: the index of an NPC death event's MAIN wait on its death test in its (substituted) instructions:
    /// a death test of a DS1 NPC (entity 6000-6999: the characters with a talk machine) — "IF HP Ratio(X, ≤ or ==, 0)" (4:2,
    /// comparison 5 / 0, ratio 0) or "IF Character Dead(X)" (4:0, state 1) — as the MAIN wait itself (Oscar 11810520) or in a
    /// condition group that a later "IF Condition Group(MAIN, PASS, …)" reads, directly (hollow Oscar 11810532) or through
    /// other groups (review e3: Petrus 11020567/11020569, MAIN = OR_01(AND_01(…, HP ≤ 0), AND_02(flag))), followed by an
    /// event flag write (2003:2 / 2003:22); -1 when the event isn't one. Census (npc-events): every NpcPorter.FindDeath
    /// shape plus Firelink's two-branch deaths.</summary>
    public static int DeathWait(IReadOnlyList<EMEVD.Instruction> ins, Func<EMEVD.Instruction, double[]> v) => DeathWait(ins, v, out _);

    /// <summary>As above, with the character the death test names.</summary>
    public static int DeathWait(IReadOnlyList<EMEVD.Instruction> ins, Func<EMEVD.Instruction, double[]> v, out int entity)
    {
        entity = 0;
        // a MAIN wait (EMEDF condition banks 0-5 and 11 with result group 0) other than a group read: the groups reset there
        static bool MainOther(EMEVD.Instruction y, double[] w) => y.Bank is 0 or 1 or 2 or 3 or 4 or 5 or 11 && !(y.Bank == 0 && y.ID == 0) && w.Length > 0 && (int)w[0] == 0;
        for (int i = 0; i < ins.Count; i++)
        {
            var a = v(ins[i]);
            bool death = ins[i] switch
            {
                { Bank: 4, ID: 2 } => a.Length >= 4 && (int)a[2] is 5 or 0 && a[3] == 0,
                { Bank: 4, ID: 0 } => a.Length >= 3 && a[2] != 0,
                _ => false,
            };
            if (!death || (int)a[1] is < 6000 or >= 7000) continue;
            int wait = (int)a[0] == 0 ? i : -1;
            if (wait < 0)
            {
                var feeds = new HashSet<int> { (int)a[0] };
                for (int k = i + 1; k < ins.Count && wait < 0; k++)
                {
                    var g = v(ins[k]);
                    if (ins[k] is { Bank: 0, ID: 0 } && g.Length >= 3 && g[1] != 0 && feeds.Contains((int)g[2]))
                    {
                        if ((int)g[0] == 0) wait = k; else feeds.Add((int)g[0]);
                    }
                    else if (MainOther(ins[k], g) || ins[k] is { Bank: 0, ID: 0 } && g.Length >= 1 && (int)g[0] == 0) break;
                }
                if (wait < 0) continue;
            }
            if (ins.Skip(wait + 1).Any(y => y is { Bank: 2003, ID: 2 or 22 })) { entity = (int)a[1]; return wait; }
        }
        return -1;
    }

    /// <summary>Rule G5: DS2 game cycle for DS1's "completed game cycles" c (DS1 NG = 0 completed; DS2 counts the
    /// playthrough in progress, NG = 1: vanilla m10_02 group 2147483620 state 「ゲーム2周目以上か？」 tests f132241() &gt;= 2).</summary>
    public static int Ds2Cycle(int ds1Completed) => ds1Completed + 1;

    /// <summary>Rule G2 (report): the DS1 respawn point an entity names (MSB SpawnPoint event → its region).</summary>
    public static (string Name, Vector3 Position, float Yaw)? RespawnPoint(MSB1 msb, int entity)
    {
        var sp = msb.Events.SpawnPoints.FirstOrDefault(s => s.EntityID == entity);
        if (sp == null) return null;
        var rg = msb.Regions.GetEntries().FirstOrDefault(r => r.Name == sp.SpawnPointName);
        return rg == null ? (sp.Name, Vector3.Zero, 0) : (sp.Name, rg.Position, rg.Rotation.Y);
    }

    /// <summary>DS1 covenant names by index (DSR menu order; EMEDF "CovenantIndex").</summary>
    public static string Covenant(int n) => n switch
    {
        0 => "none", 1 => "Way of White", 2 => "Princess's Guard", 3 => "Warrior of Sunlight", 4 => "Darkwraith",
        5 => "Path of the Dragon", 6 => "Gravelord Servant", 7 => "Forest Hunter", 8 => "Darkmoon Blade", 9 => "Chaos Servant",
        _ => $"covenant {n}",
    };

    /// <summary>
    /// Census of the instructions these rules cover over every DSR map script (Initialize Event arguments substituted):
    /// per instruction the uses, and for 2004:8 the SP1 class of each effect, for 2004:30 whether the bar comes back in
    /// the same run (a transient hide) or stays off, for 2004:4 the souls flag, for 2003:23 the point's DS1 position.
    /// </summary>
    public static List<string> Census(string dsrRoot, Dictionary<(int, int), (string Name, int[] Types)> defs)
    {
        var lines = new List<string>();
        var maps = Directory.GetFiles(Path.Combine(dsrRoot, "event"), "m*.emevd.dcx").Select(f => Path.GetFileName(f)[..^".emevd.dcx".Length]).Order().ToList();
        var count = new SortedDictionary<string, int>();
        var sp = new SortedDictionary<int, (int Uses, string Class, HashSet<string> Maps)>();
        var bar = new List<string>();
        var respawn = new List<string>();
        var cycles = new List<string>();
        var covenants = new List<string>();
        var deaths = new List<string>();
        foreach (string map in maps)
        {
            MSB1? msb = null;
            string msbPath = Path.Combine(dsrRoot, "map", "MapStudio", map + ".msb");
            if (File.Exists(msbPath)) try { msb = MSB1.Read(msbPath); } catch (InvalidDataException) { } // (a few DSR MSBs don't parse)
            foreach (var (id, ins) in WorldEvents.Instances(dsrRoot, map))
            {
                double[] V(EMEVD.Instruction x) => EmevdTranslator.Decode(defs, x);
                if (DeathWait(ins, V) >= 0) deaths.Add($"{map} {id}");
                for (int i = 0; i < ins.Count; i++)
                {
                    var x = ins[i];
                    string key = (x.Bank, x.ID) switch
                    {
                        (2004, 8) => "2004:8 Set SpEffect", (2004, 30) => "2004:30 HP bar", (2004, 37) => "2004:37 Force Character Treasure",
                        (2004, 4) => "2004:4 Force Character Death", (2004, 45) => "2004:45 NPC humanity", (2003, 23) => "2003:23 Set Player Respawn Point",
                        (3, 17) => "3:17 IF Game Cycle", (1003, 17) or (1003, 18) => "SKIP/END IF Game Cycle", (4, 12) => "4:12 IF Player's Covenant",
                        _ => "",
                    };
                    if (key == "") continue;
                    count[key] = count.GetValueOrDefault(key) + 1;
                    var v = V(x);
                    switch (x.Bank, x.ID)
                    {
                        case (2004, 8):
                        {
                            int e = (int)v[1];
                            var hp = HpPercent(dsrRoot, e, out string why);
                            string cls = hp != null ? $"SP1 HP set {hp:0.###} %" : (int)v[0] == EmevdTranslator.Player ? $"on the player: {why}" : $"no rule: {why}";
                            var cur = sp.TryGetValue(e, out var had) ? had : (Uses: 0, Class: cls, Maps: new HashSet<string>());
                            cur.Maps.Add(map);
                            sp[e] = (cur.Uses + 1, cls, cur.Maps);
                            break;
                        }
                        case (2004, 30):
                        {
                            int e = (int)v[0];
                            bool on = v[1] != 0;
                            bool back = !on && ins.Skip(i + 1).Any(y => y is { Bank: 2004, ID: 30 } && V(y) is var w && (int)w[0] == e && w[1] != 0);
                            bar.Add($"{map} {id}: chr {e} {(on ? "on" : back ? "off, back on later in the run (transient)" : "off (stays off in this run)")}");
                            break;
                        }
                        case (2003, 23):
                        {
                            var p = msb != null ? RespawnPoint(msb, (int)v[0]) : null;
                            respawn.Add($"{map} {id}: point {(int)v[0]}" + (p is { } q ? $" = {q.Name} ({q.Position.X:0.##}, {q.Position.Y:0.##}, {q.Position.Z:0.##}) yaw {q.Yaw:0}" : " (not in this map's MSB)"));
                            break;
                        }
                        case (3, 17):
                            cycles.Add($"{map} {id}: completed {Cmp((int)v[1])} {(int)v[2]} → f132241() {Cmp((int)v[1])} {Ds2Cycle((int)v[2])}");
                            break;
                        case (4, 12):
                            covenants.Add($"{map} {id}: covenant {(int)v[1]} ({Covenant((int)v[1])})");
                            break;
                    }
                }
            }
        }
        lines.Add($"DSR map scripts: {maps.Count}");
        foreach (var (k, n) in count) lines.Add($"  {k}: {n} uses");
        lines.Add($"2004:8 by effect ({sp.Count} effects):");
        foreach (var (e, s) in sp) lines.Add($"  {e}: {s.Uses}× in {string.Join(" ", s.Maps.Order())} — {s.Class}");
        lines.Add($"2004:30 HP bar ({bar.Count}):");
        lines.AddRange(bar.Select(b => "  " + b));
        lines.Add($"NPC death events (rule N3 shape, {deaths.Count}): {string.Join(", ", deaths)}");
        lines.Add($"2003:23 respawn points ({respawn.Count}):");
        lines.AddRange(respawn.Select(b => "  " + b));
        lines.Add($"3:17 game cycle ({cycles.Count}):");
        lines.AddRange(cycles.Select(b => "  " + b));
        lines.Add($"4:12 covenant ({covenants.Count}):");
        lines.AddRange(covenants.Select(b => "  " + b));
        return lines;
    }

    /// <summary>DS1 comparison type (EMEDF ComparisonType) as text.</summary>
    public static string Cmp(int c) => c switch { 0 => "==", 1 => "!=", 2 => ">", 3 => "<", 4 => ">=", 5 => "<=", _ => $"cmp{c}" };

    /// <summary>DS1 comparison type → DS2 ESD comparison opcode (rulebook §7: 0x91 &lt;, 0x92 &gt;, 0x93 &lt;=, 0x94 &gt;=;
    /// 0x95 ==, 0x96 !=), null when unknown.</summary>
    public static byte? EsdCmp(int c) => c switch { 0 => 0x95, 1 => 0x96, 2 => 0x92, 3 => 0x91, 4 => 0x94, 5 => 0x93, _ => null };
}
