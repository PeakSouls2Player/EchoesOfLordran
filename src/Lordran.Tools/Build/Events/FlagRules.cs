namespace Lordran.Tools.Build.Events;

/// <summary>
/// DS1 event flags → DS2 event flags (docs/translation-rules.md §1). Session 9 (track npcflags): every DS1 flag lands in
/// storage DS2 really has (<see cref="Ds2FlagStore"/>, read from the executable) with DS1's lifetime:
///  - **F1 persistent DS1 flags** (globals 0..9999, global item flags 5000nnnn, map flags 1AABnnnn except 5nnn, map item
///    5AABnnnn, ObjAct 6AABnnnn, talk 7AABnnnn) → a bit of DS2's **global blocks 10 / 20** (flags 100000..109999 /
///    200000..209999: saved, visible from every map, as DS1's flags are) from the append-only slot table
///    <see cref="FlagSlots"/> (`Mappings/ds1-flag-slots.tsv`), in runs no vanilla script, param or engine constant uses.
///    DS1's lifetime offline and for the host; **online DS2 keeps these blocks per player** (Ds2FlagStore.Syncs = false:
///    a guest's write is dropped, the host's doesn't reach guests) while vanilla's own world progress sits in synced map
///    blocks: open (NOTES U-coop), no other DS2 storage is both persistent for every DS1 map and synced. The table puts
///    DS1 flags one by one: a DS1 event value (2003:31/32/42/43, 3:12/3:20, 2007:12 over consecutive flags) must be
///    translated bit by bit through this rule (5 value groups straddle two pool runs; `flag-survey ds1` lists them);
///  - **F2 temporary DS1 flags** (1AAB5000..1AAB5999, DS1 resets them whenever the map loads: rule T1) → the **map-slot
///    block (k = 1) of the DS2 map hosting that DS1 map** first: P·10^6 + 10000 + slot (P = <see cref="Ds2FlagStore.Prefix"/>,
///    200 bits, not touched by DS2's rest), then overflow into the reset block k = 2 (P·10^6 + 20000 + slot − 200, also
///    zeroed by DS2's world reset at a rest: a different lifetime from k = 1; only lists over 200 reach it: none of the
///    ported maps, m12_01 207 and m13_00 202 when ported, U); the map's translated event 0 clears them at every load
///    (EmevdTranslator T1), so neither the save nor a slot eviction can leave one set. The blocks exist while that DS2
///    map is among the last 3 loaded maps (Ds2FlagStore); a vanilla map with the same prefix (Things Betwixt for m19_02)
///    shares them then, so temporary slots must stay off its flags (flag-check);
///  - **F3 restart requests** (rule R9, 2003:8 Restart of event id + slot) → the same temporary list (slot after the T rows);
///  - DS1 flags missing from the table, and temporary flags of a DS1 map no DS2 map hosts, have no rule (null): the
///    callers report them instead of writing a flag DS2 would drop.
/// Before (sessions 7-9): prefix·10^6 + nnnn (101xxxxxx Asylum, 142xxxxxx Firelink, 109xxxxxx globals): DS2 has blocks of
/// only 200 flags per map, so every id with nnnn ≥ 200 and every 109/14x id read 0 and dropped writes (run s9-12: Oscar's
/// 101700050 did not stick and his second talk replayed the first meeting).
/// <see cref="Map"/> translates only the given map's own flags (and globals), as the event translator expects;
/// <see cref="Any"/> takes a flag of any DS1 map (talk translator, NPC porter).
/// </summary>
public static class FlagRules
{
    /// <summary>DS1 map (mAA_BB) → the DS2 map its content is built into (MapBuilder.Options: Asylum m18_01 → m18_01,
    /// Firelink m10_02 → m19_02). Temporary flags live in the host's blocks; several DS1 maps in one DS2 map share its
    /// 400 temporary bits in this order. Add a line when a DS1 map is ported.
    /// Session 11 (burg11, rule MH1): from <see cref="Maps.MapHosts"/> (Mappings/ds1-maps.tsv; rule H1: Firelink m10_02 →
    /// m19_42, the Burg m10_01 → m19_41), in table order.</summary>
    public static readonly Dictionary<string, string> Host = Maps.MapHosts.FlagHosts();

    /// <summary>"AAB" (flag digits 2-4) of a DS1 map: m18_01 → "181".</summary>
    static string Aab(string ds1Map) => ds1Map.Substring(1, 2) + ds1Map.Substring(4, 2)[1..];

    static bool IsGlobal(int flag) => flag is >= 0 and < 10000 || flag is >= 50_000_000 and < 50_010_000;
    /// <summary>
    /// A boss's DS2 logic flag (GeneratorParam.logicEventFlag1; session 8, track boss step 2): DS2 numbers it 1 BB 020080 per
    /// map (Smelter 119020080, Ruin Sentinels 116020080, Executioner's Chariot 123020080), so a Lordran map's k-th DS1 boss
    /// (GameAreaParam rows of the map in entity order: Asylum Demon 0, Stray Demon 1) gets prefix·1e6 + 20080 + k (above every
    /// translated DS1 map flag, prefix·1e6 + 0..9999).
    /// </summary>
    /// <summary>Session 11 (merge11, enemy-audit G4): a DS2 flag of <see cref="BossLogicFlag"/>'s form (prefix·1e6 + 20080..20089
    /// of a hosting map): DS2's own boss logic flags sit in block k = 2 (vanilla Smelter 119020080: V-data), so the
    /// "DS1 temporary flag in k2" rule of flag-check does not apply to them (they are reset at load by rule T1-B).</summary>
    /// Session 13 (seam13): also rule BB1's battle latches (20090..20099), reset at load the same way.
    public static bool IsBossLogicFlag(long f) => f % 1_000_000 is >= 20080 and < 20100 && Host.Values.Any(h => Ds2FlagStore.Prefix(h) == f / 1_000_000);
    /// <summary>Session 13 (seam13), rule BB1: a boss battle's latch "the battle was started this load" = its boss logic flag + 10
    /// (prefix·1e6 + 20090 + k, block k2 beside the logic flags, reset at load by rule T1-B). Gates the fight's death / HP tests.</summary>
    public static int BattleLatchFlag(int bossLogicFlag) => bossLogicFlag + 10;
    public static int? BossLogicFlag(string ds1Map, int k) => Host.TryGetValue(ds1Map[..6], out var ds2) ? Ds2FlagStore.Prefix(ds2) * 1_000_000 + 20080 + k : null; // session 11 merge: main's prefix = Ds2FlagStore.Prefix of the host map (m18_01 → 101, as chrint's table)

    /// <summary>Session 11 (fx11), rule E-REQ-P: the n-th AI request channel flag of a DS1 map's placed characters (a generator's
    /// logicEventFlag2-4): prefix·1e6 + 20100 + n of the host (block k2, after the boss logic flags 20080-20089; n &lt; 100), or
    /// null when no DS2 map hosts the DS1 map.</summary>
    public static int? AiChannelFlag(string ds1Map, int n)
    {
        if (n is < 0 or >= 100) throw new ArgumentOutOfRangeException(nameof(n), $"rule E-REQ-P: {ds1Map} needs more than 100 AI request channel flags");
        return Host.TryGetValue(ds1Map[..6], out var ds2) ? Ds2FlagStore.Prefix(ds2) * 1_000_000 + 20100 + n : null;
    }
    /// <summary>Rule E-REQ-P: a flag of <see cref="AiChannelFlag"/>'s form (k2 block like the boss logic flags: reset at load by T1-B
    /// and at a rest, when DS1 respawns the characters and their AI requests are gone too).</summary>
    public static bool IsAiChannelFlag(long f) => f % 1_000_000 is >= 20100 and < 20200 && Host.Values.Any(h => Ds2FlagStore.Prefix(h) == f / 1_000_000);

    public static int? Map(string ds1Map, int flag)
    {
        if (IsGlobal(flag)) return Persistent(flag);
        string s = flag.ToString();
        // session 11 (burg11, round 8), rule H2: a merged map's flags are the primary's own (Maps.H2Merge)
        if (s.Length != 8 || !Maps.H2Merge.Aabs(ds1Map).Contains(s.Substring(1, 3))) return null;
        return FlagSlots.IsTemporary(flag) ? Temporary(flag) : Persistent(flag);
    }

    /// <summary>Session 10 (asy-events e3), rule F1x: a PERSISTENT flag of another DS1 map (map 1AABnnnn, item 5AABnnnn,
    /// ObjAct 6AABnnnn, talk 7AABnnnn; not 1AAB5nnn) read or written by this map's events: F1 puts it in the global blocks,
    /// visible from every map as in DS1 (DS1's m18_01 event 50 clears the Kiln's 11807200… at a new cycle, 11810050 sets
    /// Anor Londo's talk flags 71510036/37). Temporary flags of another map stay null (that map's load resets them; nothing
    /// hosts them here). Null for this map's own flags and globals (<see cref="Map"/> takes those).</summary>
    public static int? OtherMapPersistent(string ds1Map, int flag)
    {
        string s = flag.ToString();
        if (IsGlobal(flag) || s.Length != 8 || s[0] is not ('1' or '5' or '6' or '7') || Maps.H2Merge.Aabs(ds1Map).Contains(s.Substring(1, 3)) || FlagSlots.IsTemporary(flag)) return null;
        return Persistent(flag);
    }

    /// <summary>A DS1 flag of any DS1 map (talk translator, NPC porter): globals as <see cref="Map"/>, map flags by their own AAB.</summary>
    public static int? Any(int flag)
    {
        if (IsGlobal(flag)) return Persistent(flag);
        return FlagSlots.IsTemporary(flag) ? Temporary(flag) : Persistent(flag);
    }

    /// <summary>Session 10 (asy-items), rule C1: the synthetic persistent flag "the DS1 class is c" (S row
    /// "ds1-origin:c" of the slot table; <see cref="FlagSlots.SyntheticKeys"/>), or null when the table has no row.</summary>
    public static int? Ds1Origin(int c) => FlagSlots.Current.Synthetic.TryGetValue($"ds1-origin:{c}", out int g) ? g : null;

    /// <summary>Rule F3: the DS2 flag of the restart request of DS1 event <paramref name="ds1EventFlag"/> (id + slot).</summary>
    public static int? Restart(string ds1Map, int ds1EventFlag) =>
        FlagSlots.Current.Restart.TryGetValue(ds1EventFlag, out int slot) ? TempId(FlagSlots.Aab(ds1EventFlag) ?? Aab(ds1Map), slot) : null;

    /// <summary>Every temporary DS2 flag (F2 and F3) of a DS1 map's list: what its translated event 0 clears at load
    /// (rule T1), whichever script (event or talk) uses them.</summary>
    public static IEnumerable<int> TemporaryIds(string ds1Map)
    {
        var aabs = Maps.H2Merge.Aabs(ds1Map).ToHashSet(); // rule H2: the merged maps' temporaries too
        var t = FlagSlots.Current;
        foreach (var (f, slot) in t.Temp.Concat(t.Restart))
            if (FlagSlots.Aab(f) is string aab && aabs.Contains(aab) && TempId(aab, slot) is int id) yield return id;
    }

    static int? Persistent(int flag) => FlagSlots.Current.Global.TryGetValue(flag, out int g) ? g : null;

    static int? Temporary(int flag) => FlagSlots.Current.Temp.TryGetValue(flag, out int slot) ? TempId(FlagSlots.Aab(flag)!, slot) : null;

    /// <summary>Slot of a DS1 map's temporary list → DS2 flag in its host's k = 1 block (slots 0..199), then k = 2 (200..399).</summary>
    static int? TempId(string aab, int slot)
    {
        string ds1Map = $"m{aab[..2]}_0{aab[2]}";
        if (!Host.TryGetValue(ds1Map, out var ds2Map)) return null;
        int s = slot;
        foreach (var (m, h) in Host) // earlier DS1 maps hosted by the same DS2 map come first
        {
            if (m == ds1Map) break;
            if (h == ds2Map) s += FlagSlots.Current.TempCount.GetValueOrDefault(Aab(m));
        }
        if (s < Ds2FlagStore.MapBlockFlags) return Ds2FlagStore.MapBlockBase(ds2Map, 1) + s;
        // round 9 (rule F2b): k2 overflow stays below the boss logic / AI channel range (k2 bits 80+)
        if (s < Ds2FlagStore.MapBlockFlags + 80) return Ds2FlagStore.MapBlockBase(ds2Map, 2) + s - Ds2FlagStore.MapBlockFlags;
        return null;
    }

    /// <summary>The DS2 map whose blocks hold a DS2 flag this rule made (temporary ones), or null for global ones.</summary>
    public static string? HostOf(long ds2Flag)
    {
        foreach (var h in Host.Values.Distinct())
            if (Ds2FlagStore.Lookup(ds2Flag, [h]) is Ds2FlagStore.Kind.MapReset or Ds2FlagStore.Kind.MapSlot) return h;
        return null;
    }
}
