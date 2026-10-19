using Lordran.Tools.Build.Chr;
using SoulsFormats;

namespace Lordran.Tools.Build.Ai.Boss;

/// <summary>
/// Session 11 (track chrai11), rules EM1 / FA1: the two AI ↔ event channels the DS1 scripted bosses need (all derived from DS1's
/// own data, pure: the boss build, the map placement and the event translator ask the same questions).
/// <para><b>EM1</b> (DS1 TAE event messages): a DS1 clip sends "Send Ez State Request" N (TAE event 231, TAE.Template.DS1.xml)
/// while it plays and the map's events wait for it with 4:8 IF Character Has Event Message(entity, N). DS2's counterpart is the
/// AI's logic EzState flag: the AI ESD writes board slot k with <c>1:5300(k, v)</c> (decompile: AI command 0x14b4 = 5300 →
/// FUN_1404265c0; FUN_14042a110 copies the AI's 8 board values into the logic object +0x1c, 6 bits each, read by FUN_1401f62e0)
/// and the event ESD compares it with <c>140171(group, generator, k, v, 0 = equal / 1 = not equal)</c> (EDD
/// 「ロジックのEzStateフラグを比較」, case 0x1fd43/0x1fd44 = 130371/130372 the function forms; 43 vanilla uses: m10_14 2147483453
/// registers (0, gen, 7, 2, 1) and (1, gen, 7, 2, 0) in one state; the m10_15 preset names the arguments "logic flag id, compare
/// value"). Board slot <see cref="Slot"/> = 7: the boss template's mode protocol (ai305001 / ai305011) writes 0, 1, 2, 3, 5; vanilla
/// events read 7 most. Each message a character's clips send gets a code 1..63 (<see cref="Code"/>: 6-bit board values); the
/// translated boss AI plays an attack whose DS1 clip sends N through its own message machine (Ds2AiEsd: the code at the
/// message's TAE start, 0 at its end, clip time from the attack request: P), and the event translator turns 4:8(entity, N,
/// TRUE / FALSE) into 140171(…, 7, code, 0 / 1).</para>
/// <para><b>FA1</b> (event-forced character clips): DS2's events cannot play a clip on a character. The clips DS1's events force
/// on a 1:1 boss-pipeline character (2003:18, its parameterised inits resolved; a value the event itself skips by "SKIP IF
/// Comparison(X == v)" left out; the clips after an NPC-part break are TC1's) become actions of its own (attack categories
/// after its DS1 attacks, <see cref="Fa1Layout.Category"/>), and the event → AI request travels as ONE code over the
/// generator's logicEventFlag bits (f5903(0..3) = lef1-4 read as a binary number J; lef1 only when the part is no B-k boss
/// and starts awake: then nothing else reads it): 0 = none, then the think's E-REQ pairs (slots its battle goal reads), the
/// AI-off state (DS1 2004:1 on the character: DS2's pause would stop the clips too), then the forced clips. The map script's
/// encoder group (MapScript) turns the event's per-signal flags into J; the AI's dispatcher plays clip c when J = code(c) and
/// its board slot <see cref="AckSlot"/> is not code(c) yet, writing code(c) there first (the ack the encoder clears the
/// request on: 140171(gen, 6, code, 0)).</para>
/// </summary>
public static class EventMessages
{
    /// <summary>The AI board slot that carries the TAE event message code (EM1).</summary>
    public const int Slot = 7;
    /// <summary>The AI board slot the FA1 dispatcher acknowledges a forced clip in.</summary>
    public const int AckSlot = 6;
    /// <summary>TAE event type 231 "Send Ez State Request" (DS1).</summary>
    public const int TaeType = 231;
    /// <summary>Rule EM1: the shortest time a message code stays on the board (s): DS1's windows are 0.1 s (3 frames); the AI
    /// and the event ESD are separate machines (P), so a code is held at least this long.</summary>
    public const double MinHold = 0.2;

    /// <summary>Every TAE 231 event of a DS1 character: (animation id, message, start s, end s).</summary>
    public static List<(int Anim, int Message, double Start, double End)> Of(Ds1ChrSource src) =>
        src.Tae == null ? [] : [.. src.Tae.Animations.SelectMany(a => a.Events.Where(e => e.Type == TaeType)
            .Select(e => ((int)a.ID, BitConverter.ToInt32(e.GetParameterBytes(false), 0), (double)e.StartTime, (double)e.EndTime)))
            .OrderBy(x => x.Item2).ThenBy(x => x.Item1)];

    static readonly Dictionary<string, List<(int Anim, int Message, double Start, double End)>> byChr = [];

    /// <summary>The character's messages (cached by DS1 chr name).</summary>
    public static List<(int Anim, int Message, double Start, double End)> Of(string dsrRoot, string chr)
    {
        lock (byChr)
        {
            if (!byChr.TryGetValue(chr, out var l)) byChr[chr] = l = Of(Ds1ChrSource.Load(dsrRoot, chr));
            return l;
        }
    }

    /// <summary>Rule EM1: the board code of message <paramref name="message"/> of <paramref name="chr"/>: 1 + its rank among the
    /// character's distinct message ids (ascending), or null when no clip sends it.</summary>
    public static int? Code(string dsrRoot, string chr, int message)
    {
        var ids = Of(dsrRoot, chr).Select(m => m.Message).Distinct().Order().ToList();
        int i = ids.IndexOf(message);
        if (i < 0) return null;
        if (i + 1 > 63) throw new InvalidDataException($"rule EM1: {chr} sends {ids.Count} distinct TAE messages; the board holds 6-bit values (1..63)");
        return i + 1;
    }

    /// <summary>The messages a map's events wait for (4:8 IF Character Has Event Message, any entity).</summary>
    public static HashSet<int> Read(string dsrRoot, string ds1Map)
    {
        var res = new HashSet<int>();
        var e = Emevd(dsrRoot, ds1Map);
        if (e == null) return res;
        foreach (var ev in e.Events)
            for (int k = 0; k < ev.Instructions.Count; k++)
            {
                var x = ev.Instructions[k];
                if (x.Bank != 4 || x.ID != 8 || x.ArgData.Length < 12) continue;
                if (ev.Parameters.Any(p => p.InstructionIndex == k && p.TargetStartByte == 8))
                    foreach (var v in ParamValues(e, ev, k, 8)) res.Add(v);
                else res.Add(BitConverter.ToInt32(x.ArgData, 8));
            }
        return res;
    }

    /// <summary>Rule EM1: the message windows of a DS1 clip that the map's events read, in time order, each held at least
    /// <see cref="MinHold"/> (code, start, end).</summary>
    public static List<(int Code, double Start, double End)> Windows(string dsrRoot, string chr, string ds1Map, int clip)
    {
        var read = Read(dsrRoot, ds1Map);
        return [.. Of(dsrRoot, chr).Where(m => m.Anim == clip && read.Contains(m.Message)).OrderBy(m => m.Start)
            .Select(m => (Code(dsrRoot, chr, m.Message)!.Value, m.Start, Math.Max(m.End, m.Start + MinHold)))];
    }

    // ================================================================== FA1
    /// <summary>The FA1 layout of one boss-pipeline character (all its MSB parts): the forced clips (sorted), their codes and attack
    /// categories, the E-REQ pairs coded in the same number, the AI-off code (0 = none), the number of logicEventFlag bits.</summary>
    public sealed record Fa1Layout(IReadOnlyList<int> Clips, IReadOnlyDictionary<int, int> ClipCode, IReadOnlyDictionary<int, int> Category,
        IReadOnlyList<(int Slot, int V)> Pairs, IReadOnlyDictionary<(int Slot, int V), int> PairCode, int AiOffCode, int Bits, IReadOnlyList<int> Dropped, string Note)
    {
        public int Max => (1 << Bits) - 1;
        public bool IsClipCode(int j) => ClipCode.Values.Contains(j);
    }

    static readonly Dictionary<string, Fa1Layout?> layouts = [];

    /// <summary>
    /// Rule FA1: the layout for a boss spec, or null when DS1's events force no clip on its parts (or it has no translated AI).
    /// <paramref name="bits"/>: 4 when the parts carry no boss logic flag and start awake (lef1 free), else 3 (lef2-4).
    /// </summary>
    public static Fa1Layout? Layout(string dsrRoot, Ds1Boss.Spec s)
    {
        lock (layouts)
        {
            string key = $"{s.Ds1Chr}/{s.Ds1Map}";
            if (layouts.TryGetValue(key, out var done)) return done;
            Fa1Layout? res = null;
            // the Asylum keeps its verified build (its events have no character rules, CharacterRulesM3 off)
            if (s.Ai != null && s.Ds1Map != "m18_01_00_00" && Emevd(dsrRoot, s.Ds1Map) is { } emevd)
            {
                var msb = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", s.Ds1Map + ".msb"));
                var npcs = new HashSet<int> { s.Ds1Npc };
                foreach (var (xn, _) in s.ExtraVariants) npcs.Add(xn);
                var parts = msb.Parts.Enemies.Where(p => p.ModelName == s.Ds1Chr && npcs.Contains(p.NPCParamID) && p.EntityID > 0).ToList();
                var ents = parts.Select(p => p.EntityID).ToHashSet();
                var src = Ds1ChrSource.Load(dsrRoot, s.Ds1Chr);
                var forced = ForcedClips(emevd, ents);
                var wake = s.Anim.Roles.Where(r => r.Slot.StartsWith("a75_01_")).SelectMany(r => r.Ds1).ToHashSet();
                var clips = forced.Where(c => c.Clip > 0 && src.Has(c.Clip) && !c.AfterPartBreak && !wake.Contains(c.Clip)).Select(c => c.Clip).Distinct().Order().ToList();
                var notes = forced.Where(c => !(c.Clip > 0 && src.Has(c.Clip) && !c.AfterPartBreak && !wake.Contains(c.Clip))).Select(c => c.Clip).Distinct().Order()
                    .Select(c => $"{c} ({(!src.Has(c) ? "not in the DS1 anibnd" : wake.Contains(c) ? "a start-up slot plays it" : "the NPC-part break's reaction: TC1")})").ToList();
                if (clips.Count > 0)
                {
                    // E-REQ pairs coded in J: the requests these parts receive on slots the battle goal reads
                    var read = ReadSlots(s.Ai.Ds1Map, s.Ai.BattleGoal);
                    var reqs = EventRequests.ByEntity(dsrRoot, s.Ds1Map);
                    var pairs = ents.Where(reqs.ContainsKey).SelectMany(e => reqs[e]).Where(p => read.Contains(p.Slot)).Distinct().Order().ToList();
                    bool aiOff = AiStateCommands(emevd, ents);
                    var areas = Ds1Source.LoadParam(dsrRoot, "GameAreaParam");
                    bool boss = ents.Any(e => areas?[e] != null);
                    bool dormant = parts.Any(p => p.InitAnimID > 0 && Ds1Boss.StartUpType(s, p.InitAnimID) is > 0);
                    int bits = boss || dormant ? 3 : 4;
                    int max = (1 << bits) - 1;
                    var pairCode = pairs.Select((p, i) => (p, i + 1)).ToDictionary(t => t.p, t => t.Item2);
                    int aiOffCode = aiOff ? pairs.Count + 1 : 0;
                    int first = pairs.Count + (aiOff ? 1 : 0) + 1;
                    var kept = clips.Take(Math.Max(0, max - first + 1)).ToList();
                    var dropped = clips.Skip(kept.Count).ToList();
                    var clipCode = kept.Select((c, i) => (c, first + i)).ToDictionary(t => t.c, t => t.Item2);
                    // attack categories: after the spec's own (A − 3000 and chains 30+), the lowest free values 0..29
                    var used = s.Anim.Singles.Select(a => a - 3000).ToHashSet();
                    var cat = new Dictionary<int, int>();
                    int next = 0;
                    foreach (int c in kept)
                    {
                        while (used.Contains(next)) next++;
                        if (next > 29) throw new InvalidDataException($"rule FA1: {s.Ds1Chr}: no free attack category for forced clip {c}");
                        cat[c] = next; used.Add(next); next++;
                    }
                    string note = $"rule FA1: {s.Ds1Chr} parts {string.Join(", ", ents.Order())}: {bits} logicEventFlag bits (lef{(bits == 4 ? "1" : "2")}-4{(boss ? ", B-k boss: lef1 is the logic flag" : dormant ? ", dormant start-up: lef1 wakes it" : "")}); code 0 none"
                        + (pairs.Count > 0 ? $"; E-REQ {string.Join(", ", pairs.Select(p => $"slot {p.Slot} = {p.V} → {pairCode[p]}"))}" : "")
                        + (aiOff ? $"; AI off → {aiOffCode}" : "")
                        + $"; forced clips {string.Join(", ", kept.Select(c => $"a00_{c:D4} → {clipCode[c]} (category {cat[c]})"))}"
                        + (dropped.Count > 0 ? $"; NOT carried (no code left, CR8 stays): {string.Join(", ", dropped)}" : "")
                        + (notes.Count > 0 ? $"; left out: {string.Join(", ", notes)}" : "");
                    res = new Fa1Layout(kept, clipCode, cat, pairs, pairCode, aiOffCode, bits, dropped, note);
                }
            }
            layouts[key] = res;
            return res;
        }
    }

    /// <summary>
    /// Rule FA1, the event side of one placed part (MapEnemies): its generator, the layout, the DS2 flags of its signals (one per
    /// forced clip it receives, the AI-off state, each E-REQ pair it receives; all in the map's AI-channel block, reset at load
    /// like the E-REQ channels, rule T1-B) and of the code bits (GeneratorParam.logicEventFlag BitBase+1 … BitBase+Bits, read by
    /// the AI as f5903(BitBase + k)), with the DS1 length of each clip (an event that waits for the clip waits that long).
    /// </summary>
    public sealed record Fa1Entity(int Entity, int Generator, Fa1Layout Layout, IReadOnlyDictionary<int, int> ClipFlags, int AiOffFlag,
        IReadOnlyDictionary<(int Slot, int V), int> PairFlags, int[] BitFlags, IReadOnlyDictionary<int, double> ClipSeconds)
    {
        public int BitBase => Layout.Bits == 4 ? 0 : 1;
        public IEnumerable<int> AllFlags => ClipFlags.Values.Concat(PairFlags.Values).Concat(BitFlags).Append(AiOffFlag).Where(f => f != 0);
    }

    static readonly Dictionary<(string Map, int Entity), Fa1Entity> fa1Placed = [];
    public static void PlaceFa1(string ds1Map, Fa1Entity e) { lock (fa1Placed) fa1Placed[(ds1Map[..6], e.Entity)] = e; }
    public static Fa1Entity? Fa1Of(string ds1Map, int entity) { lock (fa1Placed) return fa1Placed.TryGetValue((ds1Map[..6], entity), out var e) ? e : null; }
    public static List<Fa1Entity> Fa1All(string ds1Map) { lock (fa1Placed) return [.. fa1Placed.Where(kv => kv.Key.Map == ds1Map[..6]).Select(kv => kv.Value).OrderBy(e => e.Entity)]; }

    /// <summary>The slots a DS1 battle goal reads with ai:GetEventRequest(n) (constant arguments in its Lua file).</summary>
    public static HashSet<int> ReadSlots(string ds1Map, int battleGoal)
    {
        var res = new HashSet<int>();
        string dir = Path.Combine(AiPorter.DecompiledLua, ds1Map);
        string file = Path.Combine(dir, $"{battleGoal:D6}_battle.lua");
        if (!File.Exists(file)) return res;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), @"GetEventRequest\(\s*(\d*)\s*\)"))
            res.Add(m.Groups[1].Value.Length > 0 ? int.Parse(m.Groups[1].Value) : 0);   // GetEventRequest() = slot 0
        return res;
    }

    /// <summary>The DS2 attack category of an FA1 clip of this spec, or null.</summary>
    public static int? ClipCategory(string dsrRoot, Ds1Boss.Spec s, int clip) => Layout(dsrRoot, s) is { } l && l.Category.TryGetValue(clip, out int c) ? c : null;

    /// <summary>(entity, clip, after an NPC-part HP test in the same event) of every 2003:18 Force Animation Playback on the
    /// entities, parameterised instructions resolved through their events' 2000:0 initialisations; a parameterised clip value the
    /// event skips itself (1000:5 SKIP IF Comparison(Equal, the same parameter, v) right before it) is left out.</summary>
    public static List<(int Entity, int Clip, bool AfterPartBreak)> ForcedClips(EMEVD emevd, IReadOnlySet<int> entities)
    {
        var res = new List<(int, int, bool)>();
        foreach (var ev in emevd.Events)
            for (int k = 0; k < ev.Instructions.Count; k++)
            {
                var x = ev.Instructions[k];
                if (x.Bank != 2003 || x.ID != 18 || x.ArgData.Length < 8) continue;
                bool afterBreak = ev.Instructions.Take(k).Any(i => i.Bank == 4 && i.ID == 6);
                var pe = ev.Parameters.FirstOrDefault(p => p.InstructionIndex == k && p.TargetStartByte == 0);
                var pa = ev.Parameters.FirstOrDefault(p => p.InstructionIndex == k && p.TargetStartByte == 4);
                // a value the event skips this instruction for (11010805: SKIP IF X8 == 7006)
                var skip = new HashSet<int>();
                if (pa != null && k > 0 && ev.Instructions[k - 1] is { Bank: 1000, ID: 5 } sk && sk.ArgData.Length >= 12 && (sbyte)sk.ArgData[1] == 0 && sk.ArgData[0] >= 1
                    && ev.Parameters.FirstOrDefault(p => p.InstructionIndex == k - 1 && p.TargetStartByte == 4) is { } pl && pl.SourceStartByte == pa.SourceStartByte)
                    skip.Add(BitConverter.ToInt32(sk.ArgData, 8));
                if (pe == null && pa == null)
                {
                    int e = BitConverter.ToInt32(x.ArgData, 0);
                    if (entities.Contains(e)) res.Add((e, BitConverter.ToInt32(x.ArgData, 4), afterBreak));
                    continue;
                }
                foreach (var init in Inits(emevd, ev.ID))
                {
                    int e = pe != null ? Arg(init, pe.SourceStartByte) : BitConverter.ToInt32(x.ArgData, 0);
                    int c = pa != null ? Arg(init, pa.SourceStartByte) : BitConverter.ToInt32(x.ArgData, 4);
                    if (entities.Contains(e) && !skip.Contains(c)) res.Add((e, c, afterBreak));
                }
            }
        return res;
    }

    /// <summary>Does any event set the AI state (2004:1) of one of the entities?</summary>
    public static bool AiStateCommands(EMEVD emevd, IReadOnlySet<int> entities)
    {
        foreach (var ev in emevd.Events)
            for (int k = 0; k < ev.Instructions.Count; k++)
            {
                var x = ev.Instructions[k];
                if (x.Bank != 2004 || x.ID != 1 || x.ArgData.Length < 4) continue;
                var pe = ev.Parameters.FirstOrDefault(p => p.InstructionIndex == k && p.TargetStartByte == 0);
                if (pe == null ? entities.Contains(BitConverter.ToInt32(x.ArgData, 0)) : Inits(emevd, ev.ID).Any(i => entities.Contains(Arg(i, pe.SourceStartByte)))) return true;
            }
        return false;
    }

    static IEnumerable<EMEVD.Instruction> Inits(EMEVD emevd, long id) =>
        emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2000 && i.ID == 0 && i.ArgData.Length >= 8 && BitConverter.ToUInt32(i.ArgData, 4) == id);

    static int Arg(EMEVD.Instruction init, long src) => 8 + src + 4 <= init.ArgData.Length ? BitConverter.ToInt32(init.ArgData, 8 + (int)src) : int.MinValue;

    static IEnumerable<int> ParamValues(EMEVD emevd, EMEVD.Event ev, int k, int targetByte)
    {
        var p = ev.Parameters.First(pp => pp.InstructionIndex == k && pp.TargetStartByte == targetByte);
        foreach (var init in Inits(emevd, ev.ID)) yield return Arg(init, p.SourceStartByte);
    }

    static readonly Dictionary<string, EMEVD?> emevds = [];
    public static EMEVD? Emevd(string dsrRoot, string ds1Map)
    {
        string map = ds1Map.Length == 6 ? ds1Map + "_00_00" : ds1Map;
        lock (emevds)
        {
            if (!emevds.TryGetValue(map, out var e))
            {
                string p = Path.Combine(dsrRoot, "event", map + ".emevd.dcx");
                emevds[map] = e = File.Exists(p) ? EMEVD.Read(p) : null;
            }
            return e;
        }
    }
}
