using System.Collections;
using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Events;

/// <summary>
/// Session 10 (track asy-sound, COVERAGE G6): one DS1 map's sound plan — what the map builder places (MSB2 Sound and SFX
/// regions) and what the event translator addresses (keys) — computed purely from the DS1 map's MSB1 + EMEVD, so both
/// sides agree without sharing state (like <see cref="EntityRules"/>). As an <see cref="IReadOnlyDictionary{TKey,TValue}"/>
/// it is the old "DS1 sound entity → key" map (2010:3 → 131201/131202).
///
/// Rules (docs/translation-rules.md §6, SND1-SND5; evidence in data\sandbox\asy-sound\NOTES.md):
///  - <b>SND1 regions.</b> Every MSB1 Sound event → one MSB2 Sound region (same position, rotation, shape). DS1 type
///    letters (DSR FUN_140255520 "acfopsmvxbgy"[type]) a (0) → DS2 type 0 'a', m (6) → DS2 type 1 'm' (DS2 builds
///    "a%09d"/"m%09d" from the region's type 0/1 and sound id: FUN_1403e3600 → FUN_1404a55f0; types ≥ 2 play nothing);
///    o (3) → DS2 type 0 with the DS1 event renamed 'a' + id in our copy of DS1's project (SND3). Shapes: DS2 plays
///    point, sphere and box regions only (FUN_1403e3330: shape 0/2/5), DS1 the same three and anything else as a point
///    (DSR FUN_14044a7b0: shape 2 sphere, 5 box, else point) → other shapes become points at the region.
///    Key (MSB2 UnkT08): a sound entity any 2010:3 of the map switches gets 100 + n (MSB order); the rest 0. DS2's
///    region manager autoplays key-0 regions while the player is in range and starts / stops a key's regions on
///    131201 / 131202 (FUN_1403e2c30 / FUN_1403e2f00; vanilla m10_23: Skeleton Lords' BGM key 101). DS1 plays an MSB
///    sound from load unless an event switches it off; a switched sound with no 2010:3 off in the unconditional prefix of
///    event 0, 50 or an event they initialise is ON at map start (<see cref="OnAtStart"/>, fix pass: event 50 and the
///    initialised events count, m18_00 50 [1] Gwyn's BGM is off) and the translator starts it first in the group of the
///    load-time event that decides it (<see cref="StartIn"/>: Firelink's hub BGM 1023800 → 11020001).
///  - <b>SND4 event SEs.</b> 2010:2 Play SE(entity, type, id) of the map (event parameters substituted per initialiser)
///    → a keyed MSB2 Sound region (point, DS2 type 0) at the entity's MSB1 position, key continuing SND1's numbering,
///    and the translator emits 131203(key) 「ポイント指定でワンショットSE再生」 = stop + play that key's regions
///    (handler FUN_14047d7d0 → FUN_1403e46e0 + FUN_1403e45c0). Letters a → as is, o → the SND3 'a' alias; others
///    are not ported (noted), except:
///  - <b>SND6 global stings</b> (session 11, snd11): 's' = DS1's global stream project frpg_smain (loaded by DS1 for every
///    map; the boss kill sting s777777777 at HP 0 on 12 bosses): placed like an 'o' SE (keys after every a/o SE, so SND4's
///    keys stay put) under its 'a' alias (a777777777), whose event is a copy of frpg_smain's in our copy of the area
///    memory project (<see cref="Imports"/>, <see cref="Maps.Atmosphere.SoundBanks"/>; frpg_smain.fsb loose). An 's' name
///    DS2 defines, or one frpg_smain lacks, stays unported (noted).
///  - <b>SND5 event one-shot SFX.</b> 2006:3 Spawn Oneshot SFX(type, entity, dummy, sfx) → a keyed MSB2 SFX region
///    (UnkT04 = 100 + n) at the entity's position with the DS2 effect <see cref="Maps.Atmosphere.EffectMap"/> gives,
///    and the translator emits 131503(key) 「ポイント指定でワンショットSFX再生」 (handler → FUN_1403c8ff0 / FUN_1403c89e0:
///    clear + set the key's regions' play bit; vanilla m20_10 keys 10/20/40, 131503(p1)); an unmapped effect is noted.
/// </summary>
public sealed class MapSounds : IReadOnlyDictionary<int, int>
{
    /// <summary>A DS1 sound event placed as an MSB2 Sound region.</summary>
    public sealed record Region(string Name, string RegionName, int Ds1Entity, int Ds2Type, int SoundId, int Key, string Ds1Event, string Ds2Event);
    /// <summary>A DS1 2010:2 Play SE placed as a keyed point Sound region.</summary>
    public sealed record Effect(int Entity, int Type, int Id, int Key, string Ds1Event, string Ds2Event, Vector3 Position);
    /// <summary>A DS1 2006:3 one-shot SFX placed as a keyed point SFX region.</summary>
    public sealed record OneShot(int Entity, int Ds1Sfx, int Ds2Effect, int Key, Vector3 Position);
    /// <summary>Session 11 (snd11), rule SND6: a DS1 event of a global DS1 project (frpg_smain) an event SE names, copied
    /// under <paramref name="Ds2Event"/> into our copy of the area's memory project.</summary>
    public sealed record Import(string Project, string Ds1Event, string Ds2Event);

    public const int FirstKey = 100;
    /// <summary>DS1 sound type letters (DSR FUN_140255520).</summary>
    public const string Ds1Letters = "acfopsmvxbgy";

    public List<Region> Regions { get; } = [];
    public List<Effect> Effects { get; } = [];
    public List<OneShot> OneShots { get; } = [];
    /// <summary>SND6: global-project events the SEs name (<see cref="Import"/>).</summary>
    public List<Import> Imports { get; } = [];
    /// <summary>SND6: DS1's global stream project, loaded by DS1 for every map (DSR soundrpg_smain: s777777777 the boss
    /// kill sting, s000090010, m/v system events); its 's' events reach DS2 only as copies.</summary>
    public const string GlobalStream = "frpg_smain";
    /// <summary>SND1 (fix pass): switched sound entities ON when DS1's map has loaded (no 2010:3 off in the unconditional
    /// prefix of a load-time event) → the DS1 event whose translated group starts them first (<see cref="Host"/>).</summary>
    public Dictionary<int, long> StartIn { get; } = [];
    /// <summary>The ON-at-start switched sounds (keys of <see cref="StartIn"/>).</summary>
    public IReadOnlyCollection<int> OnAtStart => StartIn.Keys;
    /// <summary>DS1 FMOD event name → its name in our copy of DS1's project (SND3: 'o' → 'a' for regions and SEs).</summary>
    public Dictionary<string, string> Renames { get; } = [];
    public List<string> Notes { get; } = [];
    readonly Dictionary<int, int> keys = [];

    public static string Ds1Event(int type, int id) => $"{(type >= 0 && type < Ds1Letters.Length ? Ds1Letters[type] : '?')}{id:D9}";

    /// <param name="names">fix pass (SND2/SND3): the map's FMOD name space (<see cref="Names"/>): the names an 'a' alias
    /// must avoid and the names the projects DS1 loads for the area define. Keys never depend on it; null = plain 'a' + id
    /// aliases and every SE placed (report tools that only read keys).</param>
    public static MapSounds Plan(MSB1 msb1, EMEVD emevd, NameSpace? names = null)
    {
        var p = new MapSounds { taken = names?.Taken };
        var resolved = Resolved(emevd).ToList();
        var switched = resolved.Where(r => r.Ins.Bank == 2010 && r.Ins.ID == 3 && r.Ins.ArgData.Length >= 5).Select(r => BitConverter.ToInt32(r.Ins.ArgData, 0)).ToHashSet();
        var load = LoadTimeEvents(emevd);
        var offAtStart = OffAtStart(load);
        int key = FirstKey;
        foreach (var e in msb1.Events.Sounds)
        {
            string ds1 = Ds1Event(e.SoundType, e.SoundID);
            int ds2Type = e.SoundType switch { 0 => 0, 3 => 0, 6 => 1, _ => -1 };
            if (ds2Type < 0) { p.Notes.Add($"sound {e.Name} {ds1}: DS1 type {e.SoundType} has no DS2 region type (a/m only), not placed"); continue; }
            if (!msb1.Regions.GetEntries().Any(r => r.Name == e.RegionName)) { p.Notes.Add($"sound {e.Name} {ds1}: no region '{e.RegionName}', not placed"); continue; }
            string ds2 = p.Alias(ds1);
            int k = e.EntityID > 0 && switched.Contains(e.EntityID) ? ++key : 0;
            if (k != 0)
            {
                p.keys[e.EntityID] = k;
                if (!offAtStart.Contains(e.EntityID) && !p.StartIn.ContainsKey(e.EntityID)) p.StartIn[e.EntityID] = p.Host(e.EntityID, load, resolved);
            }
            if (names != null && !names.Loaded(ds1)) p.Notes.Add($"sound {e.Name} {ds1}: no project DS1 loads for the area defines it ({names.Where(ds1)}): placed, plays nothing");
            p.Regions.Add(new Region(e.Name, e.RegionName, e.EntityID, ds2Type, int.Parse(ds2[1..]), k, ds1, ds2));
        }
        // SND4: event SEs (distinct entity/type/id, first appearance). Session 11 (snd11), rule SND6: a second pass places
        // the 's' SEs of DS1's global frpg_smain (the boss kill sting s777777777 at HP 0 on 12 bosses) like an 'o' SE, under an
        // 'a' alias whose event is a copy of frpg_smain's (Import); their keys follow every a/o SE's, so SND4's keys stay put
        var ses = resolved.Where(r => r.Ins.Bank == 2010 && r.Ins.ID == 2 && r.Ins.ArgData.Length >= 12).ToList();
        foreach (bool pass2 in new[] { false, true })
        foreach (var (ev, x) in ses)
        {
            int ent = BitConverter.ToInt32(x.ArgData, 0), type = BitConverter.ToInt32(x.ArgData, 4), id = BitConverter.ToInt32(x.ArgData, 8);
            if (p.Effects.Any(f => f.Entity == ent && f.Type == type && f.Id == id)) continue;
            string ds1 = Ds1Event(type, id);
            bool global = type == 5 && (names == null || (names.Global(ds1) == GlobalStream && !names.Ds2(ds1)));
            if (global != pass2) continue;
            if (type is not (0 or 3) && !global) { p.NoteOnce($"SE {ds1} at {ent} (event {ev}): DS1 type '{ds1[0]}' is not ported ({(type == 5 ? $"not an event of DS1's {GlobalStream}, or DS2 defines the name" : "DS2 one-shot regions name 'a' events")})"); continue; }
            if (Position(msb1, ent) is not Vector3 pos) { p.NoteOnce($"SE {ds1} at {ent} (event {ev}): entity not in the MSB, not placed"); continue; }
            int seKey = ++key; // reserved whether placed or not: every caller numbers the keys alike
            // fix pass (SND2): an SE no project DS1 loads for the area defines would resolve to nothing in DS2 (DS1 plays it
            // from its global frpg.fev or a neighbouring area's project): not placed, the translator drops the 2010:2
            if (names != null && !global && !names.Loaded(ds1)) { p.NoteOnce($"SE {ds1} at {ent} (event {ev}): no project DS1 loads for the area defines it ({names.Where(ds1)}), not placed"); continue; }
            string alias = p.Alias(ds1);
            if (global && !p.Imports.Any(i => i.Ds1Event == ds1)) p.Imports.Add(new Import(GlobalStream, ds1, alias));
            p.Effects.Add(new Effect(ent, type, id, seKey, ds1, alias, pos));
        }
        // SND5: event one-shot SFX
        int sfxKey = FirstKey;
        foreach (var (ev, x) in resolved.Where(r => r.Ins.Bank == 2006 && r.Ins.ID == 3 && r.Ins.ArgData.Length >= 16))
        {
            int ent = BitConverter.ToInt32(x.ArgData, 4), dummy = BitConverter.ToInt32(x.ArgData, 8), sfx = BitConverter.ToInt32(x.ArgData, 12);
            if (p.OneShots.Any(o => o.Entity == ent && o.Ds1Sfx == sfx)) continue;
            if (!Maps.Atmosphere.EffectMap.TryGetValue(sfx, out var m) || m.Ds2 == 0) { p.NoteOnce($"one-shot SFX {sfx} at {ent} (event {ev}): no DS2 effect mapped (Atmosphere.EffectMap), not placed"); continue; }
            if (Position(msb1, ent) is not Vector3 pos) { p.NoteOnce($"one-shot SFX {sfx} at {ent} (event {ev}): entity not in the MSB, not placed"); continue; }
            if (dummy != -1) p.Notes.Add($"one-shot SFX {sfx} at {ent}: DS1 dummy {dummy} → the entity's origin (P)");
            p.OneShots.Add(new OneShot(ent, sfx, m.Ds2, ++sfxKey, pos));
        }
        return p;
    }

    /// <summary>SND3: the DS2-playable name of a DS1 event ('a'/'m' as is; others 'a' + id, recorded as a rename). Fix pass:
    /// when 'a' + id is already an event of a DS1 area project or of DS2 (DS1 m12: o120000011 → a120000011 is frpg_sm12's
    /// frog-lake loop; m12_01: o120199999 → a120199999 is fdlc_sm12's), the alias's first id digit becomes 9, 8, … 0 (first
    /// free; same length, so no FEV offsets move) and the plan notes it.</summary>
    string Alias(string ds1)
    {
        if (ds1[0] is 'a' or 'm') return ds1;
        if (Renames.TryGetValue(ds1, out var known)) return known;
        string a = "a" + ds1[1..];
        bool Used(string n) => taken?.Invoke(n) == true || Renames.Values.Contains(n);
        if (Used(a))
        {
            string? alt = "9876543210".Where(d => d != ds1[1]).Select(d => $"a{d}{ds1[2..]}").FirstOrDefault(n => !Used(n));
            Notes.Add($"SND3: alias {a} of {ds1} is taken (a DS1 area project or DS2 defines it) → {alt ?? "none free, " + a + " kept"}");
            a = alt ?? a;
        }
        Renames[ds1] = a;
        return a;
    }
    Func<string, bool>? taken;

    // ---- SND1 load-time state (fix pass: DS1's own load behaviour, not event 0 alone)
    /// <summary>An event DS1 runs at map load: event 0 and 50 (no arguments) and every event they initialise (2000:0), with
    /// that initialiser's parameters substituted. These are exactly the events <see cref="EmevdTranslator.TranslateMap"/>
    /// translates (one result per event and slot).</summary>
    public sealed record LoadEvent(long Event, int Slot, List<EMEVD.Instruction> Ins);

    public static List<LoadEvent> LoadTimeEvents(EMEVD emevd)
    {
        var byId = emevd.Events.GroupBy(e => e.ID).ToDictionary(g => g.Key, g => g.First());
        var calls = new List<(long Id, int Slot, byte[] Args)>();
        foreach (long root in new long[] { 0, 50 })
        {
            if (!byId.TryGetValue(root, out var ev)) continue;
            calls.Add((root, 0, []));
            foreach (var x in ev.Instructions.Where(x => x.Bank == 2000 && x.ID == 0 && x.ArgData.Length >= 8))
                calls.Add((BitConverter.ToUInt32(x.ArgData, 4), BitConverter.ToInt32(x.ArgData, 0), x.ArgData[8..]));
        }
        return calls.DistinctBy(c => (c.Id, c.Slot)).Where(c => byId.ContainsKey(c.Id)).Select(c =>
        {
            var ev = byId[c.Id];
            var ins = ev.Instructions.Select((x, i) =>
            {
                var y = new EMEVD.Instruction(x.Bank, x.ID, (byte[])x.ArgData.Clone());
                foreach (var q in ev.Parameters.Where(q => q.InstructionIndex == i))
                    if (q.SourceStartByte + q.ByteCount <= c.Args.Length && q.TargetStartByte + q.ByteCount <= y.ArgData.Length)
                        Array.Copy(c.Args, q.SourceStartByte, y.ArgData, q.TargetStartByte, q.ByteCount);
                return y;
            }).ToList();
            return new LoadEvent(c.Id, c.Slot, ins);
        }).ToList();
    }

    /// <summary>The instructions a DS1 event runs on every path when it starts, before it first waits or may end: its
    /// commands from the top, through condition-group registrations (a condition with a group other than MAIN does not
    /// wait) and over skips (a conditional SKIP n makes only its n lines conditional; SKIP n unconditionally passes them
    /// by), stopping at the first MAIN condition, WAIT, END or conditional END, or at a conditional skip whose range holds
    /// one of those. m18_00 event 50 [1] (Gwyn's BGM off), m17_00 11705384 [1] and event 0 [85] (after skips that end
    /// before it) are such; 11400800 [2] (inside "SKIP 5 IF flag OFF") and 11705102 [2] are conditional.</summary>
    public static IEnumerable<EMEVD.Instruction> AlwaysAtLoad(IReadOnlyList<EMEVD.Instruction> ins)
    {
        var (_, always) = WalkStart(ins);
        return ins.Where((x, i) => always[i]);
    }

    /// <summary>The instructions a DS1 event may run when it starts, before its first wait (MAIN condition or WAIT): its
    /// load-time decisions, conditional or not (11400800 [2], 11705102 [2], 11020001 [20] after its "in m10_02" check
    /// that passes at once is not one: [3] is a MAIN condition).</summary>
    public static IEnumerable<EMEVD.Instruction> ReachedAtLoad(IReadOnlyList<EMEVD.Instruction> ins)
    {
        var (reach, _) = WalkStart(ins);
        return ins.Where((x, i) => reach[i]);
    }

    /// <summary>The event's start as a forward-only graph (DS1 skips only jump forward): reach = reachable before any
    /// wait or END; always = reachable and no reachable earlier instruction can end, wait, or skip over it.</summary>
    static (bool[] Reach, bool[] Always) WalkStart(IReadOnlyList<EMEVD.Instruction> ins)
    {
        static bool CondSkip(EMEVD.Instruction x) => (x.Bank, x.ID) is (1000, 1) or (1000, 5) or (1000, 7) or (1003, 1) or (1003, 3) or (1003, 5) or (1003, 7) or (1005, 1);
        static bool CondEnd(EMEVD.Instruction x) => Ends.Contains((x.Bank, x.ID)) && (x.Bank, x.ID) != (1000, 4);
        static int N(EMEVD.Instruction x) => x.ArgData.Length > 0 ? x.ArgData[0] : 0;
        int count = ins.Count;
        var reach = new bool[count + 1];
        var always = new bool[count + 1];
        reach[0] = count > 0;
        int bypassTo = -1;   // the furthest target of a reachable skip so far (the lines before it may be skipped)
        bool stopped = false;
        for (int i = 0; i < count; i++)
        {
            if (!reach[i]) continue;
            var x = ins[i];
            always[i] = !stopped && i >= bypassTo;
            if (x.Bank >= 2000) { reach[i + 1] = true; continue; }
            bool wait = x.Bank < 1000 ? x.ArgData.Length == 0 || (sbyte)x.ArgData[0] == 0 : x.Bank == 1001;
            if (x.Bank < 1000 && !wait) { reach[i + 1] = true; continue; }                  // condition registration
            if ((x.Bank, x.ID) == (1000, 3)) { int t = Math.Min(count, i + 1 + N(x)); reach[t] = true; bypassTo = Math.Max(bypassTo, t); continue; }
            if (CondSkip(x)) { int t = Math.Min(count, i + 1 + N(x)); reach[i + 1] = reach[t] = true; bypassTo = Math.Max(bypassTo, t); continue; }
            if (CondEnd(x)) { reach[i + 1] = true; stopped = true; continue; }               // the lines after it are conditional
            stopped = true;                                                                  // wait, END, other flow: no load-time successor
        }
        return (reach, always);
    }

    static bool IsSoundSwitch(EMEVD.Instruction x, int? entity = null, int? state = null) =>
        x.Bank == 2010 && x.ID == 3 && x.ArgData.Length >= 5 && (entity == null || BitConverter.ToInt32(x.ArgData, 0) == entity) && (state == null || x.ArgData[4] == state);

    /// <summary>SND1 (fix pass): the sound entities DS1 has OFF when the map has loaded — a 2010:3 (e, 0) that event 0,
    /// event 50 or an event they initialise runs on every path at its start (<see cref="AlwaysAtLoad"/>; m18_00 event 50
    /// [1] Gwyn's BGM 1803800; m17_00 11705384 [1] the Archives event-boss BGM 1703801; m17_00 event 0 [85] 1703800).</summary>
    public static HashSet<int> OffAtStart(IEnumerable<LoadEvent> load) =>
        load.SelectMany(l => AlwaysAtLoad(l.Ins)).Where(x => IsSoundSwitch(x, state: 0)).Select(x => BitConverter.ToInt32(x.ArgData, 0)).ToHashSet();
    public static HashSet<int> OffAtStart(EMEVD emevd) => OffAtStart(LoadTimeEvents(emevd));

    /// <summary>END instructions (end type in byte 0; 1 = Restart): 1000:2/4/6/8, 1003:2/4/6/8, 1005:2 (DS1 EMEDF).</summary>
    static readonly HashSet<(int, int)> Ends = [(1000, 2), (1000, 4), (1000, 6), (1000, 8), (1003, 2), (1003, 4), (1003, 6), (1003, 8), (1005, 2)];

    /// <summary>SND1 (fix pass): the DS1 event whose translated group starts an ON-at-start sound. DS1's sound is ON by
    /// default and the load-time events (event 0, 50, then their initialisers in order) that switch it OFF before their
    /// first wait (<see cref="ReachedAtLoad"/>; then any that switch it off, then any that switch it) decide from there (conditional offs such as 11400800 "boss already dead → off, END", the siren's 11705102 "off unless
    /// 61700105"), so the start runs first in the group of the first of them that cannot restart: start, then DS1's own
    /// logic, in a fixed order whether or not other groups run first. Restartable = an END type 1 in it or a 2003:8
    /// restart of it (the start would replay: the siren's 11705101 is passed over for 11705102). No such off-switching
    /// event → the first non-restartable one that switches it at all, else event 0 (noted).</summary>
    long Host(int entity, List<LoadEvent> load, List<(long Event, EMEVD.Instruction Ins)> resolved)
    {
        bool Restarts(LoadEvent l) => l.Event != 0 && (l.Ins.Any(x => Ends.Contains((x.Bank, x.ID)) && x.ArgData.Length > 0 && x.ArgData[0] == 1)
            || resolved.Any(r => r.Ins.Bank == 2003 && r.Ins.ID == 8 && r.Ins.ArgData.Length >= 9 && BitConverter.ToInt32(r.Ins.ArgData, 0) == l.Event && r.Ins.ArgData[8] == 1));
        var host = load.FirstOrDefault(l => ReachedAtLoad(l.Ins).Any(x => IsSoundSwitch(x, entity, 0)) && !Restarts(l))
            ?? load.FirstOrDefault(l => l.Ins.Any(x => IsSoundSwitch(x, entity, 0)) && !Restarts(l))
            ?? load.FirstOrDefault(l => l.Ins.Any(x => IsSoundSwitch(x, entity)) && !Restarts(l));
        if (host != null) return host.Event;
        var any = load.Where(l => l.Ins.Any(x => IsSoundSwitch(x, entity))).Select(l => l.Event).ToList();
        Notes.Add(any.Count == 0 ? $"SND1: sound {entity} ON at start, no load-time event switches it → started in event 0"
            : $"SND1: sound {entity} ON at start, its load-time events {string.Join(",", any)} can restart → started in event 0 (order vs them not fixed, P)");
        return 0;
    }

    /// <summary>The ON-at-start sounds whose start the translated group of this DS1 event carries (SND1).</summary>
    public IEnumerable<int> StartedIn(long ds1Event) => StartIn.Where(kv => kv.Value == ds1Event).Select(kv => kv.Key);

    // ---- SND2 projects + SND3 name space
    /// <summary>SND2 (fix pass): the FMOD projects DS1 loads for a map area, (memory, stream) pairs in DS1's order: DSR
    /// FUN_14044b3a0 loads frpg_m%02d / frpg_sm%02d / frpg_xm%02d per area, and fdlc_m%02d / fdlc_sm%02d ("sndmap_dlc")
    /// when FUN_1400c48c0(area) holds, i.e. area 12 (both m12_00 Darkroot and m12_01 Oolacile: every m12_01 BGM is
    /// fdlc_sm12's). xm ('y' events) is not ported (open issue 5).</summary>
    public static List<(string Memory, string Stream)> Ds1Projects(int area) =>
        area == 12 ? [($"frpg_m{area:D2}", $"frpg_sm{area:D2}"), ($"fdlc_m{area:D2}", $"fdlc_sm{area:D2}")] : [($"frpg_m{area:D2}", $"frpg_sm{area:D2}")];

    /// <summary>Session 11 (burg11, round 8), rule H2-S: the projects a DS1 MAP loads — its area's, and for a merged map
    /// (rule H2) every member area's after them (the primary's first memory project stays the map's first).</summary>
    public static List<(string Memory, string Stream)> Ds1ProjectsFor(string ds1Map)
    {
        var list = Ds1Projects(int.Parse(ds1Map.Substring(1, 2)));
        if (Maps.H2Merge.GroupOf(ds1Map) is { } g && g.Primary.StartsWith(ds1Map[..6], StringComparison.OrdinalIgnoreCase))
            foreach (var s in g.Secondaries)
                foreach (var p in Ds1Projects(int.Parse(s.Substring(1, 2))).Where(p => !list.Contains(p)))
                    list.Add(p);
        return list;
    }

    static readonly Dictionary<string, Dictionary<string, List<string>>> fevCache = [];
    /// <summary>Event-like names of every DS1 area project (frpg/fdlc _mAA/_smAA/_xmAA) → projects (cached per DSR root).</summary>
    public static Dictionary<string, List<string>> Ds1AreaIndex(string dsrRoot)
    {
        string k = "dsr:" + dsrRoot;
        if (!fevCache.TryGetValue(k, out var idx))
            fevCache[k] = idx = Commands.SoundSurvey.FevIndex(Commands.SoundSurvey.Ds1Projects(dsrRoot)
                .Where(p => System.Text.RegularExpressions.Regex.IsMatch(p.Item1, @"^(frpg|fdlc)_(s|x)?m\d\d$")));
        return idx;
    }
    /// <summary>Session 11 (snd11, SND6): event-like names of DS1's global projects frpg_smain and frpg_main (cached).</summary>
    public static Dictionary<string, List<string>> GlobalIndex(string dsrRoot)
    {
        string k = "dsrglobal:" + dsrRoot;
        if (!fevCache.TryGetValue(k, out var idx))
            fevCache[k] = idx = Commands.SoundSurvey.FevIndex(Commands.SoundSurvey.Ds1Projects(dsrRoot).Where(p => p.Item1 is GlobalStream or "frpg_main"));
        return idx;
    }
    /// <summary>Event-like names of DS2's own projects (vanilla frpg2.fevbnd; cached).</summary>
    public static Dictionary<string, List<string>> Ds2Index(string ds2Data)
    {
        string k = "ds2:" + ds2Data;
        if (!fevCache.TryGetValue(k, out var idx)) fevCache[k] = idx = Commands.SoundSurvey.FevIndex(Commands.SoundSurvey.Ds2Projects(ds2Data));
        return idx;
    }
    /// <summary>A DS1 map's FMOD name space (SND2/SND3): Taken = names an 'a' alias must avoid (any DS1 area project or
    /// DS2 defines them); Loaded = a DS1 event name some project DS1 loads for the area (<see cref="Ds1Projects"/>) defines
    /// and our copy keeps (an 'a'/'m' name DS2 also defines is q-renamed there); Where = the DS1 projects defining it.</summary>
    public sealed record NameSpace(Func<string, bool> Taken, Func<string, bool> Loaded, Func<string, string> Where)
    {
        /// <summary>Session 11 (snd11, SND6): the DS1 global project defining a name (frpg_smain), or null.</summary>
        public Func<string, string?> Global { get; init; } = _ => null;
        /// <summary>Session 11 (snd11, SND6): DS2's own projects define the name.</summary>
        public Func<string, bool> Ds2 { get; init; } = _ => false;
    }

    public static NameSpace Names(string dsrRoot, string ds2Data, string ds1Map)
    {
        var a = Ds1AreaIndex(dsrRoot); var b = Ds2Index(ds2Data);
        var loaded = Ds1ProjectsFor(ds1Map).SelectMany(p => new[] { p.Memory, p.Stream }).ToHashSet(); // + rule H2-S
        var g = GlobalIndex(dsrRoot);
        return new NameSpace(
            n => a.ContainsKey(n) || b.ContainsKey(n),
            n => a.TryGetValue(n, out var l) && l.Any(loaded.Contains) && !(n[0] is 'a' or 'm' && b.ContainsKey(n)),
            n => a.TryGetValue(n, out var l) ? string.Join("+", l) : g.TryGetValue(n, out var gl) ? string.Join("+", gl) : "only DS1's global frpg/fdlc projects, or none")
        { Global = n => g.TryGetValue(n, out var gl) && gl.Contains(GlobalStream) ? GlobalStream : null, Ds2 = b.ContainsKey };
    }
    /// <summary><see cref="Names"/> from the default DSR install and the extracted DS2 data (data\ds2).</summary>
    public static NameSpace DefaultNames(string ds1Map) => Names(Program.DefaultDsrRoot, Commands.ExtractDs2.DefaultOut, ds1Map);

    void NoteOnce(string s) { if (!Notes.Contains(s)) Notes.Add(s); }

    /// <summary>The MSB1 position of a part or region entity, or null.</summary>
    public static Vector3? Position(MSB1 msb1, int entity) =>
        msb1.Parts.GetEntries().FirstOrDefault(p => p.EntityID == entity) is { } part ? part.Position
        : msb1.Regions.GetEntries().FirstOrDefault(r => r.EntityID == entity) is { } reg ? reg.Position : null;

    /// <summary>The key a DS1 2010:2 Play SE was placed under, or null.</summary>
    public int? EffectKey(int entity, int type, int id) => Effects.FirstOrDefault(f => f.Entity == entity && f.Type == type && f.Id == id)?.Key;
    /// <summary>The SFX key a DS1 2006:3 one-shot was placed under, or null.</summary>
    public int? OneShotKey(int entity, int sfx) => OneShots.FirstOrDefault(o => o.Entity == entity && o.Ds1Sfx == sfx)?.Key;

    /// <summary>Every instruction of the map's EMEVD with its event parameters substituted once per initialiser
    /// (instructions without parameters once; DSR stores byte arguments in 4-byte slots).</summary>
    public static IEnumerable<(long Event, EMEVD.Instruction Ins)> Resolved(EMEVD emevd)
    {
        var inits = new Dictionary<long, List<byte[]>>();
        foreach (var ev in emevd.Events)
            foreach (var x in ev.Instructions.Where(x => x.Bank == 2000 && x.ID == 0 && x.ArgData.Length >= 8))
                (inits.TryGetValue(BitConverter.ToInt32(x.ArgData, 4), out var l) ? l : inits[BitConverter.ToInt32(x.ArgData, 4)] = []).Add(x.ArgData[8..]);
        foreach (var ev in emevd.Events)
            for (int i = 0; i < ev.Instructions.Count; i++)
            {
                var x = ev.Instructions[i];
                var ps = ev.Parameters.Where(p => p.InstructionIndex == i).ToList();
                if (ps.Count == 0) { yield return (ev.ID, x); continue; }
                foreach (var args in inits.GetValueOrDefault(ev.ID) ?? [])
                {
                    var y = new EMEVD.Instruction(x.Bank, x.ID, (byte[])x.ArgData.Clone());
                    foreach (var p in ps)
                        if (p.SourceStartByte + p.ByteCount <= args.Length && p.TargetStartByte + p.ByteCount <= y.ArgData.Length)
                            Array.Copy(args, p.SourceStartByte, y.ArgData, p.TargetStartByte, p.ByteCount);
                    yield return (ev.ID, y);
                }
            }
    }

    // ---- IReadOnlyDictionary: switched sound entity → key (the old EntityRules.SoundId contract)
    public int this[int k] => keys[k];
    public IEnumerable<int> Keys => keys.Keys;
    public IEnumerable<int> Values => keys.Values;
    public int Count => keys.Count;
    public bool ContainsKey(int k) => keys.ContainsKey(k);
    public bool TryGetValue(int k, out int v) => keys.TryGetValue(k, out v);
    public IEnumerator<KeyValuePair<int, int>> GetEnumerator() => keys.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
