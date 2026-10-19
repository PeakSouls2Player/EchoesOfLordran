using Lordran.Tools.Build.Chr;
using Lordran.Tools.Build.Events;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Session 11 (burg11, BURG-PLAN M3/M4): every DS1 character of a MapObjects map (the `--map burg` rows) placed by rule, from
/// the characters `--burg-chrs` built (ChrPorter.BurgChrs, the 1:1 boss pipeline's Ds1Boss specs of the map):
///  - regulars: <see cref="AsylumEnemies.Build"/> (generator = EntityRules.Generator(entity), translated DS1 think, P1/P2
///    placement, P3a start-up, W1 world conditions, E-REQ-P channels), the map's NPC-table entities left to NpcPorter;
///  - the boss pipeline's parts (every MSB part of a spec's DS1 NpcParams, rule MB1): a part whose spec's entity has a DS1
///    GameAreaParam row (a DS1 boss, B-k index k in entity order) → boss generator (event group / logic group / logic flag of
///    k: the second Gargoyle and the stand-ins share their spec's k); the others (LR1 large regulars: Titanite Demon, dogs,
///    rats, lizard, Tusk; the scripted Hellkite) → regular generators with W1 world conditions;
///  - a DS1 InitAnimID a boss network's dormant slot plays → startUpStateType (Ds1Boss.StartUpType: the perched Hellkite 7006);
///  - parts without an entity: generator model·10 + n (AsylumEnemies' rule).
/// Patrols (DS1 MovePointNames: 2 dogs, 2 Balder Knights) are not translated (rule PT1 open: they stand at their spawn).
/// Returns DS1 entity → DS2 generator for the event translator (Ds1MapContext).
/// </summary>
public static class MapEnemies
{
    public static Dictionary<int, int> Build(Pack pack, Ds1Source ds1, MapBuilder.Options map, IReadOnlyList<NpcPorter.Placed>? npcs)
    {
        string dsr = ds1.Root;
        var msb = MSB1.Read(Path.Combine(dsr, "map", "MapStudio", map.Ds1Map + ".msb"));
        var areas = Ds1Source.LoadParam(dsr, "GameAreaParam");
        var bosses = msb.Parts.Enemies.Where(p => p.EntityID > 0 && areas?[p.EntityID] != null).Select(p => p.EntityID).Distinct().Order().ToList();
        var probe = FloorProbe.FromPack(pack, map.Ds2Map);
        FloorProbe? ds1Probe = null;
        var extra = new List<EnemyTest.Custom>();
        var extraEntities = new Dictionary<int, int>();
        var npcEntities = (npcs ?? []).Select(n => n.Entity).ToHashSet();
        // rule N-H4: a DS1 NPC part without talk (TalkID 0: Havel the Rock) that DS1 never makes hostile by event is hostile from the
        // start (DS1: its think attacks on sight); DS2's NPC AI fights while the generator's hostility flag (logicEventFlag3) is on →
        // a flag of the map's AI-channel range (99 − k, reset at load by T1-B like the channels) that the map script sets at load
        var loadOn = LoadOnFlags[map.Ds2Map] = [];
        var wakes = WakeFlags[map.Ds1Map] = [];   // session 11 (dchr11), rule WK2
        if (npcs != null)
            npcs = [.. npcs.Select(n =>
            {
                var part = msb.Parts.Enemies.FirstOrDefault(p => p.EntityID == n.Entity);
                // rule N-H5: hostile once hit (the Merchant, Andre): DS1's own flag, set by the translated event (CR3 + N-H3)
                if (part != null && n.Custom.HostileFlag == 0 && DamageHostility(dsr, map.Ds1Map, n.Entity) is int dh && FlagRules.Any(dh) is int dh2)
                {
                    pack.Note($"{map.Ds2Map} rule N-H5: NPC {n.Name} (entity {n.Entity}) turns hostile when hit: hostility flag = DS1 {dh} → {dh2}");
                    return n with { Custom = n.Custom with { HostileFlag = dh2 } };
                }
                if (part == null || part.TalkID != 0 || n.Custom.HostileFlag != 0) return n;
                int flag = FlagRules.AiChannelFlag(map.Ds1Map, 99 - loadOn.Count) ?? 0;
                if (flag == 0) return n;
                loadOn.Add(flag);
                pack.Note($"{map.Ds2Map} rule N-H4: NPC {n.Name} (entity {n.Entity}) has no DS1 talk → hostile from the start: hostility flag {flag} set at load");
                return n with { Custom = n.Custom with { HostileFlag = flag } };
            })];
        int[] nSignal = [SignalBase];   // session 11 (chrai11): rules FA1 / E-REQ-B2 flags (AI-channel block n ≥ 60; regulars from 0, N-H4 from 99 down)
        // the map's own boss-pipeline specs, and (round 7) every large regular whose DS1 NpcParams this map's parts use (the Depths' rats
        // and dogs are variants of the Burg's specs)
        bool UsedHere(Ds1Boss.Spec b) => b.LargeRegular && msb.Parts.Enemies.Any(p => p.ModelName == b.Ds1Chr && (p.NPCParamID == b.Ds1Npc || b.ExtraVariants.Any(x => x.Npc == p.NPCParamID)));
        foreach (var spec in Ds1Boss.All.Where(b => b.Ds1Map == map.Ds1Map || UsedHere(b)))
        {
            if (AsylumEnemies.OnlyEnemyParams.Count > 0 && !AsylumEnemies.OnlyEnemyParams.Contains(spec.EnemyParam)) continue;
            var eps = new Dictionary<int, int> { [spec.Ds1Npc] = spec.EnemyParam };
            foreach (var (xn, xe) in spec.ExtraVariants) eps[xn] = xe;
            int k = bosses.IndexOf(spec.Ds1Entity), n = 0;
            System.Numerics.Vector3? anchor = null;
            foreach (var part in msb.Parts.Enemies.Where(p => p.ModelName == spec.Ds1Chr && eps.ContainsKey(p.NPCParamID)))
            {
                int gen = part.EntityID > 0 ? EntityRules.Generator(part.EntityID) : spec.Model * 10 + n++;
                var c = new EnemyTest.Custom(1600, gen, eps[part.NPCParamID], spec.HighLogic, spec.LowLogic, part.Position, part.Rotation.Y);
                // rule P2w: a part with no floor under its DS1 spot (the Hellkite's perch out of the map, its fly-over double)
                // starts at the first area DS1's events warp it to that has one (1010300 → 1012310 on the bridge)
                string moved = "";
                if (part.EntityID > 0 && probe.Floor(part.Position, above: 1f, below: 60f) == null)
                {
                    var targets = WarpTargets(dsr, map.Ds1Map, msb, part.EntityID).ToList();
                    if (targets.FirstOrDefault(t => probe.Floor(t.Position, above: 1f, below: 5f) != null) is { Region: not null } wt)
                    {
                        c = c with { Position = wt.Position };
                        moved = $", rule P2w: no floor at DS1 {part.Position:0.0} → its first warp area {wt.Region} {wt.Position:0.0}";
                    }
                    // a flyer only ever warped into the air (the Hellkite's fly-over double 1010302 → 1012300): the floor below that area
                    // (its events warp it up before it is enabled)
                    else if (targets.Select(t => (t.Region, t.Position, Floor: probe.Floor(t.Position, above: 1f, below: 80f))).FirstOrDefault(t => t.Floor != null) is { Region: not null } wf)
                    {
                        c = c with { Position = wf.Position with { Y = wf.Floor!.Value.Y } };
                        moved = $", rule P2w: no floor at DS1 {part.Position:0.0} → the floor under its first warp area {wf.Region} ({wf.Floor.Value.Y:0.0})";
                    }
                    // a double of the spec's character (the Hellkite's fly-over 1010302) whose areas have no floor at all: the spec's
                    // first part placed on a floor (its events warp it before it is enabled)
                    else if (anchor is { } an)
                    {
                        c = c with { Position = an };
                        moved = $", rule P2w: no floor at DS1 {part.Position:0.0} nor under its warp areas → its character's first placed part {an:0.0}";
                    }
                }
                if (anchor == null && probe.Floor(c.Position, above: 1f, below: 5f) != null) anchor = c.Position;
                string kind;
                if (!spec.LargeRegular && k >= 0)
                {
                    c = c with { EventGroup = EntityRules.BossEventGroupOf(k), LogicGroup = EntityRules.BossLogicGroupOf(k),
                        BossLogicFlag = (uint)(FlagRules.BossLogicFlag(map.Ds1Map, k) ?? 0) };
                    kind = $"boss B-k {k}{(part.EntityID != spec.Ds1Entity ? $" (part of {spec.Ds1Entity}'s fight)" : "")}";
                    // a further part (no DS1 boss row: the second Gargoyle 1010801, the intro stand-in 1010802) gets DS1's world-state
                    // fold like a regular (the translator leaves its 2004:5 to the generator then: Ds1MapContext.WorldOwned)
                    if (part.EntityID > 0 && part.EntityID != spec.Ds1Entity) c = AsylumEnemies.WithWorld(pack, map.Ds1Map, part.EntityID, c);
                }
                else
                {
                    if (part.EntityID > 0) c = AsylumEnemies.WithWorld(pack, map.Ds1Map, part.EntityID, c);
                    kind = spec.LargeRegular ? "large regular (LR1)" : "scripted (no DS1 boss row)";
                }
                if (part.InitAnimID > 0 && Ds1Boss.StartUpType(spec, part.InitAnimID) is int su)
                    c = c with { StartUpStateType = (byte)su };
                if (part.EntityID > 0) c = AiSignals(pack, dsr, map, spec, part, gen, c, nSignal);   // session 11 (chrai11): rules FA1 / E-REQ-B2
                // session 11 (dchr11), rule WK2: a large regular that starts posed (the Depths' ceiling Slimes, InitAnimID 9000 / 9001) wakes when
                // the boss-pipeline upper logic sees its logicEventFlag1 on (its start-up wake): the next flag of the boss pipeline's AI-channel block
                // (SignalBase.., shared with chrai11's FA1 / E-REQ-B2, so the overlap check below covers it), set by the event that clears DS1's waiting animation (2004:9)
                if (spec.LargeRegular && c.StartUpStateType != 0 && part.EntityID > 0 && c.BossLogicFlag == 0
                    && FlagRules.AiChannelFlag(map.Ds1Map, nSignal[0]) is int wake)
                {
                    nSignal[0]++;
                    wakes[part.EntityID] = wake;
                    c = c with { BossLogicFlag = (uint)wake };
                    pack.Note($"{map.Ds2Map} rule WK2: {part.Name} (entity {part.EntityID}) starts posed (start-up {c.StartUpStateType}): wake flag {wake} = logicEventFlag1");
                }
                // each part its own regist row (EnemyId·100 + 50 + n: clear of the regulars' EnemyId·100 + k)
                c = c with { RegistId = c.EnemyId * 100 + 50 + extra.Count(x => x.EnemyId == c.EnemyId) };
                c = Ds1Placement.Generator(probe, c);
                extra.Add(c);
                if (part.EntityID > 0) extraEntities[part.EntityID] = gen;
                pack.Note($"{map.Ds2Map} MB1 {part.Name} (entity {part.EntityID}, NpcParam {part.NPCParamID}) → generator {gen}, EnemyParam {c.EnemyId}, {kind}, start-up {c.StartUpStateType}{(c.WorldNote is { Length: > 0 } w ? $", {w}" : "")}{moved}");
            }
        }
        // round 7: parts whose DS1 think the AI translation cannot carry yet (special-check FAIL: the attack only on branches behind
        // unsupported ai: calls) wait for the AI track instead of fighting without their DS1 attacks
        var skip = new HashSet<int>(npcEntities);
        foreach (var e in msb.Parts.Enemies.Where(e => e.EntityID > 0 && ThinksAwaitingAi.Contains(e.ThinkParamID)))
        {
            skip.Add(e.EntityID);
            pack.Note($"{map.Ds2Map}: {e.Name} (entity {e.EntityID}, think {e.ThinkParamID}) not placed: its think awaits the AI track (special-check)");
        }
        var result = AsylumEnemies.Build(pack, dsr, map, ChrPorter.BurgChrs, onlyGroup: null, npcs: npcs, extra: extra, extraEntities: extraEntities, skipEntities: skip, npcDeathOwns: true,
            skipPart: e => Void(e));
        // rule P2x: a regular DS1 places in the void (no floor under it in DS1 nor DS2: the Depths' c2500_0002, 5 m under the sewer floor)
        // is left out: it would fall out of DS2's world (DS1 never shows it either)
        bool Void(MSB1.Part.Enemy e)
        {
            if (probe.Floor(e.Position, above: 1f, below: 60f) != null) return false;
            ds1Probe ??= FloorProbe.FromDs1(dsr, map.Ds1Map);
            if (ds1Probe.Floor(e.Position, above: 1f, below: 60f) != null) return false;
            pack.Note($"{map.Ds2Map} rule P2x: {e.Name} (entity {e.EntityID}) has no floor under it in DS1 or DS2: not placed");
            return true;
        }
        // the AI-channel block's three users must not meet: regulars' E-REQ-P from 0, the boss pipeline's signals from SignalBase, N-H4 from 99 down
        // (the boss pipeline's own: FA1's and E-REQ-B2's channels of its parts)
        var bossFlags = Ai.Boss.EventMessages.Fa1All(map.Ds1Map).SelectMany(f => f.AllFlags)
            .Concat(extraEntities.Keys.SelectMany(e => Ai.EventRequests.PlacedChannels(map.Ds1Map, e).Select(x => x.Flag))).ToHashSet();
        var regularFlags = Ai.EventRequests.PlacedFlags(map.Ds1Map).Except(bossFlags).ToList();
        int? signalFirst = FlagRules.AiChannelFlag(map.Ds1Map, SignalBase), signalEnd = FlagRules.AiChannelFlag(map.Ds1Map, nSignal[0]);
        if (signalFirst is int sf && (regularFlags.Any(f => f >= sf && f < (signalEnd ?? int.MaxValue)) || loadOn.Any(f => f < (signalEnd ?? int.MaxValue) && f >= sf)))
            throw new InvalidDataException($"{map.Ds1Map}: AI-channel flags overlap (regular channels up to {(regularFlags.Count > 0 ? regularFlags.Max() : 0)}, boss-pipeline signals {sf}..{signalEnd}, N-H4 {string.Join(",", loadOn)})");
        Console.WriteLine($"  {map.Ds2Map}: {result.Count} DS1 characters placed ({extra.Count} on the boss pipeline, {npcs?.Count ?? 0} NPCs)");
        return result;
    }

    /// <summary>Session 11 (chrai11): the first AI-channel block index of the boss pipeline's request flags.</summary>
    public const int SignalBase = 60;

    /// <summary>
    /// Session 11 (track chrai11), rules FA1 / E-REQ-B2: a boss-pipeline part's AI request flags.
    /// <list type="bullet">
    /// <item>FA1 (<see cref="Ai.Boss.EventMessages.Layout"/>): one DS2 flag per signal the part receives (each forced clip DS1's events
    /// play on it, its AI-off state, each E-REQ pair on a slot its battle goal reads) and one per code bit; the bits are the generator's
    /// logicEventFlag1-4 (a part without a boss logic flag that starts awake; else lef2-4), the map script's encoder writes them
    /// (MapScript); the E-REQ pairs are placed as the part's channels with the code as k (the translator's E-REQ-P case sets them).</item>
    /// <item>E-REQ-B2: a non-boss part without FA1 whose think reads its requests (the Armored Tusk: GetEventRequest(0)) gets E-REQ-P's
    /// channel flags on logicEventFlag2-4, as the regulars (until now only AsylumEnemies' regulars had them: the Tusk's, the
    /// Hellkite's and the lizard's requests reached nothing).</item>
    /// </list>
    /// </summary>
    static EnemyTest.Custom AiSignals(Pack pack, string dsr, MapBuilder.Options map, Ds1Boss.Spec spec, MSB1.Part.Enemy part, int gen, EnemyTest.Custom c, int[] n)
    {
        int e = part.EntityID;
        var reqs = Ai.EventRequests.ByEntity(dsr, map.Ds1Map);
        int Flag() => FlagRules.AiChannelFlag(map.Ds1Map, n[0]++) ?? throw new InvalidDataException($"{map.Ds1Map}: no AI-channel flag {n[0] - 1}");
        if (Ai.Boss.EventMessages.Layout(dsr, spec) is { } lay && Ai.Boss.EventMessages.Emevd(dsr, map.Ds1Map) is { } emevd)
        {
            var mine = new HashSet<int> { e };
            var clips = Ai.Boss.EventMessages.ForcedClips(emevd, mine).Where(x => !x.AfterPartBreak && lay.ClipCode.ContainsKey(x.Clip)).Select(x => x.Clip).Distinct().Order().ToList();
            var pairs = reqs.TryGetValue(e, out var rs) ? rs.Where(lay.PairCode.ContainsKey).Order().ToList() : [];
            bool aiOff = lay.AiOffCode > 0 && Ai.Boss.EventMessages.AiStateCommands(emevd, mine);
            if (clips.Count == 0 && pairs.Count == 0 && !aiOff) { pack.Note($"{map.Ds2Map} rule FA1 {part.Name} (entity {e}): no forced clip, request or AI state of its own (no signals)"); return c; }
            var src = Ds1ChrSource.Load(dsr, spec.Ds1Chr);
            var clipFlags = clips.ToDictionary(x => x, x => Flag());
            int aiOffFlag = aiOff ? Flag() : 0;
            var pairFlags = pairs.ToDictionary(p => p, p => Flag());
            var bits = Enumerable.Range(0, lay.Bits).Select(_ => Flag()).ToArray();
            var secs = clips.ToDictionary(x => x, x => (double)src.Samples(x).Duration);
            var f = new Ai.Boss.EventMessages.Fa1Entity(e, gen, lay, clipFlags, aiOffFlag, pairFlags, bits, secs);
            Ai.Boss.EventMessages.PlaceFa1(map.Ds1Map, f);
            Ai.EventRequests.Place(map.Ds1Map, e, [.. pairs.Select(p => (p.Slot, p.V, lay.PairCode[p], pairFlags[p]))]);
            c = lay.Bits == 4 ? c with { BossLogicFlag = (uint)bits[0], ChannelFlags = [bits[1], bits[2], bits[3]] } : c with { ChannelFlags = [bits[0], bits[1], bits[2]] };
            pack.Note($"{map.Ds2Map} rule FA1 {part.Name} (entity {e}, generator {gen}): code bits {string.Join("/", bits)} = logicEventFlag{(lay.Bits == 4 ? 1 : 2)}-4; clips {string.Join(", ", clips.Select(x => $"a00_{x:D4} (code {lay.ClipCode[x]}, flag {clipFlags[x]}, {secs[x]:0.##} s)"))}"
                + (aiOff ? $"; AI off (code {lay.AiOffCode}) flag {aiOffFlag}" : "") + (pairs.Count > 0 ? $"; E-REQ {string.Join(", ", pairs.Select(p => $"slot {p.Slot} = {p.V} (code {lay.PairCode[p]}) flag {pairFlags[p]}"))}" : ""));
            return c;
        }
        // rule E-REQ-B2: a non-boss part's own E-REQ-P channels (the slots its battle goal reads)
        var areas = Ds1Source.LoadParam(dsr, "GameAreaParam");
        if (areas?[e] != null || spec.Ai == null || !reqs.TryGetValue(e, out var req)) return c;
        var read = Ai.Boss.EventMessages.ReadSlots(spec.Ai.Ds1Map, spec.Ai.BattleGoal);
        var ch = Ai.EventRequests.ForThink(part.ThinkParamID).Where(x => read.Contains(x.Slot) && req.Contains((x.Slot, x.V))).ToList();
        if (ch.Count == 0) return c;
        var list = ch.Select(x => (x.Slot, x.V, x.K, Flag: Flag())).ToList();
        Ai.EventRequests.Place(map.Ds1Map, e, list);
        var cf = new int[3];
        foreach (var x in list) cf[x.K - 1] = x.Flag;
        pack.Note($"{map.Ds2Map} rule E-REQ-B2 {part.Name} (entity {e}, generator {gen}, think {part.ThinkParamID}): {string.Join(", ", list.Select(x => $"slot {x.Slot} = {x.V} → channel {x.K} flag {x.Flag}"))}");
        return c with { ChannelFlags = cf };
    }

    /// <summary>
    /// Session 11 (burg11, round 6), rule N-H5: an NPC DS1 turns hostile when the player hits it: an event "IF Character Damaged
    /// By(X, …)" (4:1) that sets flag Y ON (2003:2) and changes X's team type (2004:44 / 2004:2), X and Y event parameters,
    /// initialised for this entity (m10_01 11010510(6230, 1401): the Undead Merchant; (6190, 1321): Andre) → Y (DS1 id), the
    /// generator's hostility flag (NpcPorter.FindHostile's HP-ratio form does not cover it). Null: none.
    /// </summary>
    public static int? DamageHostility(string dsrRoot, string ds1Map, int entity)
    {
        string path = Path.Combine(dsrRoot, "event", ds1Map + ".emevd.dcx");
        if (!File.Exists(path)) return null;
        var emevd = EMEVD.Read(path);
        static int? Param(EMEVD.Event ev, int instruction, int targetByte) =>
            ev.Parameters.FirstOrDefault(p => p.InstructionIndex == instruction && p.TargetStartByte == targetByte) is { } p ? (int)p.SourceStartByte : null;
        var inits = emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2000 && i.ID == 0 && i.ArgData.Length >= 8).ToList();
        foreach (var ev in emevd.Events)
        {
            var ins = ev.Instructions;
            int hit = ins.FindIndex(i => i.Bank == 4 && i.ID == 1 && Param(ev, ins.IndexOf(i), 4) != null);
            if (hit < 0) continue;
            int entityAt = Param(ev, hit, 4)!.Value;
            int team = ins.FindIndex(hit + 1, i => i.Bank == 2004 && i.ID is 44 or 2 && Param(ev, ins.IndexOf(i), 0) == entityAt);
            int set = ins.FindIndex(hit + 1, i => i.Bank == 2003 && i.ID == 2 && i.ArgData.Length >= 5 && i.ArgData[4] == 1 && Param(ev, ins.IndexOf(i), 0) != null);
            if (team < 0 || set < 0) continue;
            int flagAt = Param(ev, set, 0)!.Value;
            foreach (var init in inits.Where(i => BitConverter.ToUInt32(i.ArgData, 4) == ev.ID))
            {
                int Arg(int at) => 8 + at + 4 <= init.ArgData.Length ? BitConverter.ToInt32(init.ArgData, 8 + at) : int.MinValue;
                if (Arg(entityAt) == entity) return Arg(flagAt);
            }
        }
        return null;
    }

    /// <summary>Round 7: DS1 thinks whose translated AI special-check rejects (237002: the Depths' Channeler, its bullet 3007 only behind
    /// ai:ChkNearLowHpFriend / ai:IsOnNearMeshByPos, unsupported).</summary>
    /// Session 11 (dchr11, coordinator decision): empty - 237002 runs with rules AI-NF / AI-NM (approximations, special-check DEV).
    static readonly HashSet<int> ThinksAwaitingAi = [];

    /// <summary>Rule N-H4: per DS2 map, the hostility flags the map script sets at load (MapScript.TranslateEvents).</summary>
    public static readonly Dictionary<string, List<int>> LoadOnFlags = [];

    /// <summary>Session 11 (dchr11), rule WK2: per DS1 map, DS1 entity → the wake flag of a posed large regular (logicEventFlag1).</summary>
    public static readonly Dictionary<string, Dictionary<int, int>> WakeFlags = [];

    /// <summary>Rule P2w: the areas DS1's events warp a character to (2004:40/41/42 type 1, in instruction order).</summary>
    static IEnumerable<(string? Region, System.Numerics.Vector3 Position)> WarpTargets(string dsr, string ds1Map, MSB1 msb, int entity)
    {
        var defs = Commands.EmevdDump.Defs();
        var emevd = EMEVD.Read(Path.Combine(dsr, "event", ds1Map + ".emevd.dcx"));
        foreach (var x in emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2004 && i.ID is 40 or 41 or 42))
        {
            double[] v;
            try { v = EmevdTranslator.Decode(defs, x); } catch { continue; }
            if (v.Length < 3 || (int)v[0] != entity || (int)v[1] != 1) continue;
            if (msb.Regions.GetEntries().FirstOrDefault(r => r.EntityID == (int)v[2]) is { } r) yield return (r.Name, r.Position);
        }
    }
}
