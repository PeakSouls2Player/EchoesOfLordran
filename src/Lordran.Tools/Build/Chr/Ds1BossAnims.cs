using System.Globalization;
using Lordran.Tools.Formats.Morpheme;
using SoulsFormats;
using static Lordran.Tools.Build.Chr.Ds1RigAnims;
// merge stage 5: the boss track's network / TAE code lives in its own namespaces (the enemies track has
// classes of the same names in Formats.Morpheme / Formats); see MERGE-NOTES.md "Unification backlog"
using NmbNetEdit = Lordran.Tools.Formats.Morpheme.Boss.NmbNetEdit;
using NmbNetResize = Lordran.Tools.Formats.Morpheme.Boss.NmbNetResize;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// The animation half of a 1:1 DS1 boss (session 8, track boss; first user the Asylum Demon, DS1 c2232, 145-bone rig): a DS2
/// boss network moved onto DS1's skeleton by the network re-serialiser and given one SM_Attack category per DS1 attack.
/// Generalises <see cref="Ds1RigAnims.BuildAnibnd"/> (the unarmed Hollow, same bone count, no attacks) with:
/// <list type="number">
/// <item><b>Rig</b>: <see cref="NmbNetResize.Resize"/> (any bone count; every rig-dependent attrib rebuilt, mirror by DS2's
/// bind-pose rule), checked with <see cref="NmbNetResize.CheckRigConsistency"/> (strict name-twin pairs).</item>
/// <item><b>One moveset</b> (rule B1): DS1 characters have no weapon stances, so every WeaponCategory_Ene test [0,0] becomes
/// [0,9] and the other slots' [k,k] (k ≥ 1) become unreachable [99,99]: whatever slot DS2's engine reports, the slot-0
/// branches run (Smelter: SM_Attack weapon hubs, SM_StartUp, stance changes).</item>
/// <item><b>Attack categories</b> (rule B2, NETWORK-FORMAT.md §6): DS1 attack A (3000-3029) = DS2 action n = A − 3000 =
/// AttackCategory_Ene n; the template's own weapon-0 categories are reused, missing ones cloned from a single-stage
/// category (<see cref="NmbNetEdit.AddCategory"/>); the [10,19] band is widened to [10,39] (SM_Main → SM_Attack and the band
/// hub) so categories 20-23 (DS1 3020-3023) and the chains 30+k are reachable; DS1 chain k (ComboAttack → ComboRepeat →
/// ComboFinal, from the Lua) = category 30 + k, cloned from a template category with as many route-1 stages.</item>
/// <item><b>Attack leaves</b>: stage 1 of category n plays DS1 A = 3000 + n (stage s of chain category 30 + k plays the
/// chain's s-th clip) from a file-name-table entry of its own (DS2 name rule + CRC-32/BZIP2); stages without a DS1 clip and
/// the unreachable slot-1+ leaves play DS1's idle with the no-attack tracks (rule C12c). Each attack leaf gets event-track
/// bundles of its own (<see cref="NmbNetEdit.UnshareTracks"/>, DS2's own layout: one TimeAct/HitDuration/... bundle per
/// attack clip), then its tracks come from DS1's TAE (<see cref="AttackEvents"/>, rule B3).</item>
/// <item><b>Every other slot</b>: as the Hollow (role table by NSA name, DS1 clip encoded at DS1's 30 fps, foot-lock and
/// sync tracks from DS1's motion, gameplay flags from DS1's TAE).</item>
/// </list>
/// </summary>
public static class Ds1BossAnims
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <param name="Singles">DS1 attack clips used as single attacks (category A − 3000)</param>
    /// <param name="Chains">DS1 combo chains (category 30 + index), each 2-4 clips</param>
    /// <param name="NoReaction">DS2 slot prefixes whose DS1 role is "no reaction" (a boss that never flinches): DS1 idle,
    /// damage window empty, return to base at once</param>
    public sealed record Spec(string Ds1Chr, string TemplateChr, string NewChr, int[] Singles, int[][] Chains, Role[] Roles, string[] NoReaction)
    {
        /// <summary>Session 11 (track chrai11), rule FA1: attack category → DS1 clip of the event-forced clips the character plays as
        /// actions of its own (Ai.Boss.EventMessages.Layout; set by Ds1Boss.Build), next to the DS1 attacks (category A − 3000).</summary>
        public IReadOnlyDictionary<int, int> EventClips { get; init; } = new Dictionary<int, int>();
        /// <summary>The DS1 clip of single-attack category <paramref name="n"/> (a DS1 attack 3000 + n or an FA1 clip), or null.</summary>
        public int? ClipOf(int n) => EventClips.TryGetValue(n, out int c) ? c : Singles.Contains(3000 + n) ? 3000 + n : null;
        /// <summary>(category, DS1 clip) of every single-attack category: the DS1 attacks, then the FA1 clips.</summary>
        public IEnumerable<(int Category, int Clip)> SingleCategories => Singles.Select(a => (a - 3000, a)).Concat(EventClips.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)));
        /// <summary>Every DS1 clip played as a single attack (the DS1 attacks and the FA1 clips).</summary>
        public IEnumerable<int> AttackClips => Singles.Concat(EventClips.Values);
    }

    /// <summary>One attack leaf of the plan.</summary>
    public sealed class LeafPlan
    {
        public int Node, Ds1Id = -1, Category, Stage, Chain = -1, NextValue, SlotOffset, ClipIndex = -1;
        public long TaeId;
        public bool Reachable;
        public override string ToString() => $"node {Node}: category {Category} stage {Stage + 1}{(Chain >= 0 ? $" (chain {Chain})" : "")} → {(Ds1Id >= 0 ? Ds1ChrSource.ClipName(Ds1Id) : "DS1 idle, no attack")}{(TaeId > 0 ? $", TAE {TaeId}" : "")}{(NextValue > 0 ? $", next stage on {NextValue}" : "")}";
    }

    /// <summary>DS1 hit groups of a clip: DS1 TAE type 1 "Attack Behavior" events (judge id = BehaviorParam 2_VVVVV_JJJ);
    /// judges with identical windows are one group (one DS2 damage slot, the others chained as childDamage); groups in time
    /// order (rule B4).</summary>
    /// <param name="Slot">session 11 (burgchr11, rule B4b): the damage slot (2200 ID) of the group: its index, except in a clip with
    /// more than 3 groups (session 12; was 4) (the Taurus jump 3009, the Titanite Demon's 3007 / 3008: 5 hit windows; plain actions have 3 damage ids,
    /// combo stages 4), where windows with the same judges share the slot of the first such window (they are the same hit
    /// repeated: one damage row serves every window)</param>
    public sealed record HitGroup(double Start, double End, int[] Judges)
    {
        public int Slot { get; init; }
    }

    /// <summary>Rule B4b: the number of damage slots a clip's hit groups use.</summary>
    public static int SlotCount(IReadOnlyList<HitGroup> g) => g.Count == 0 ? 0 : g.Max(x => x.Slot) + 1;

    public static List<HitGroup> HitGroups(TAE.Animation? a)
    {
        if (a == null) return [];
        var hits = a.Events.Where(e => e.Type == 1).Select(e => (S: (double)e.StartTime, E: (double)e.EndTime, J: BitConverter.ToInt32(e.GetParameterBytes(false), 8))).OrderBy(h => h.S).ThenBy(h => h.J).ToList();
        var groups = hits.GroupBy(h => (Math.Round(h.S, 3), Math.Round(h.E, 3))).OrderBy(g => g.Key.Item1)
            .Select((g, i) => new HitGroup(g.First().S, g.First().E, [.. g.Select(h => h.J).Distinct()]) { Slot = i }).ToList();
        // rule B4b: only clips with more groups than a PLAIN action has damage ids (3: DamageId00-02) change (the Asylum demons unchanged).
        // Session 12 (ehit12): was "more than 4" (a combo stage's AttackId01-04), which left a 4-window clip played as a plain action
        // with a 2200 ID 3 no DamageId names (the engine fills slots 0-2 only: decompile FUN_1403a8fb0 l.729520-729560): the Hellkite's
        // (c3430) 3010 4th hit and the Gaping Dragon's (c5260) 3002 / 3007 / 3009 4th hits never landed (hit-chain-check on _base39)
        if (groups.Count <= 3) return groups;
        var sets = new List<string>();
        return [.. groups.Select(g =>
        {
            string key = string.Join(",", g.Judges.Order());
            int k = sets.IndexOf(key); if (k < 0) { sets.Add(key); k = sets.Count - 1; }
            return g with { Slot = Math.Min(k, 2) };   // more than 3 distinct judge sets: the rest share slot 2 (its row chain = their union)
        })];
    }

    public sealed class Result
    {
        public byte[] Anibnd = [];
        public MorphemeSkeleton Rig = null!;
        public List<string> Ds1Names = [];
        public List<ClipInfo> Clips = [];
        public List<LeafPlan> Leaves = [];
        public Dictionary<int, (double Ds1, double Ds2, bool NoAttack)> TaeIds = [];
        /// <summary>Attack TAE id → (DS1 clip, slot offset, DS1 duration).</summary>
        public Dictionary<long, (int Ds1Id, int SlotOffset, double Duration)> AttackTae = [];
        /// <summary>merge9 stage 6 (boss × chrsound): the clip slot that owns each <see cref="TaeIds"/> id (DS1 clip, loop
        /// re-cut, frames, fps) and the attack entry of each <see cref="AttackTae"/> id, so the _snd TAE can carry the DS1
        /// clip's own sound events (chrsound rules S3-S6). Informational: nothing else in the build reads them.</summary>
        public Dictionary<int, ClipInfo> TaeSource = [];
        public Dictionary<long, ClipInfo> AttackSource = [];
        public List<string> Report = [];
        public List<string> NetworkProblems = [];
    }

    /// <summary>TAE id of an attack leaf: 65 CC 1 0 S 0 like DS2's a65 clips (CC = category, S = stage), e.g. 65041010.</summary>
    public static long TaeIdOf(int category, int stage) => 65_00_0000L + category * 10000L + 1000 + 10 * (stage + 1);

    /// <summary>DS1's own footfalls of a clip from its TAE: event 144 (floor effect at a dummy poly; u16 dummy at +2) on a foot
    /// dummy (<paramref name="footDummy"/>: DS1 dummy id → 'L'/'R' by its attach bone), as (frame at 30 fps, foot). c2232
    /// walk a00_0200: dummy 3 (R Foot) at 1.167 s, dummy 4 (L Foot) at 2.433 s - one gait cycle per loop.</summary>
    public static List<(int Frame, char Foot)> TaeFootfalls(TAE.Animation? a, IReadOnlyDictionary<int, char> footDummy) =>
        a?.Events.Where(e => e.Type == 144).Select(e => (F: (int)Math.Round(e.StartTime * 30), D: (int)BitConverter.ToUInt16(e.GetParameterBytes(false), 2)))
            .Where(t => footDummy.ContainsKey(t.D)).Select(t => (t.F, footDummy[t.D])).Distinct().OrderBy(t => t.F).ToList() ?? [];

    /// <param name="footDummy">DS1 dummy id → foot ('L'/'R'), for <see cref="TaeFootfalls"/></param>
    /// <param name="nmbName">merge9 stage 6: the NMB's name in the anibnd = the anim set's (c{animId/100}; every vanilla
    /// anibnd, 173/173, names its NMB like itself, e.g. c2260.anibnd → c2260.nmb, which models c2260-c2262 share); null =
    /// <see cref="Spec.NewChr"/> (the boss track's choice while model and anim set had one name). The new NSA names and
    /// track-bundle ids keep NewChr (the DS1 character).</param>
    /// <param name="i1bReserved">session 10 (rule I1b, <see cref="NmbIdentity"/>): the build's id registry (vanilla ids + ids handed out so far, rule I1c); when given, every
    /// registered bundle whose content is not the template's gets an id of its own (the network def always)</param>
    /// <summary>Session 11 (burgchr11): the most route-1 stages any weapon-0 attack category of a template network has (1 = no
    /// chain category: the Stray Dog c2270), for rule B2c.</summary>
    public static int MaxRouteStages(byte[] templateAnibnd)
    {
        byte[] raw = DCX.Is(templateAnibnd) ? DCX.Decompress(templateAnibnd) : templateAnibnd;
        var nmb = NmbFile.Read(BND4.Read(raw).Files.First(f => f.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)).Bytes);
        byte[] x = nmb.Bundles[nmb.IndexOf(10)].Data;
        var names = NmbNetEdit.NodeNames(x); var net = new NmbNetwork(x);
        return NmbNetEdit.AttackCategories(net, names).Values.Select(c => NmbNetEdit.Stages(net, names, c.Sm).Count).DefaultIfEmpty(1).Max();
    }

    public static Result BuildAnibnd(byte[] templateAnibnd, Ds1ChrSource ds1, Spec spec, IReadOnlyDictionary<int, char>? footDummy = null, string? nmbName = null, NmbIdentity.Registry? i1bReserved = null)
    {
        footDummy ??= new Dictionary<int, char>();
        var res = new Result();
        byte[] raw = templateAnibnd;
        DCX.CompressionInfo? comp = null;
        if (DCX.Is(raw)) raw = DCX.Decompress(raw, out comp);
        var bnd = BND4.Read(raw);
        var nmbFile = bnd.Files.First(f => f.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase));
        byte[] templateNmb = nmbFile.Bytes;

        // ---- 1. rig: DS1's skeleton, network resized
        var ds1Rig = ds1.Bridge(ds1.ClipIds.First()).Skeleton;
        var rig = WithDs2Names(ds1Rig);
        res.Rig = rig; res.Ds1Names = ds1Rig.Names;
        using var lockScope = LockJointsFor(rig);   // session 11 (burgchr11): rule C11q (quadrupeds' hind feet)
        if (LockJointL != "L_Toe0") res.Report.Add($"rule C11q: foot plants measured on {LockJointL} / {LockJointR}");
        var rz = NmbNetResize.Resize(nmbFile.Bytes, rig);
        res.Report.Add($"network resized {NmbRig.Read(NmbFile.Read(nmbFile.Bytes).Bundles[NmbFile.Read(nmbFile.Bytes).IndexOf(1)].Data).Names.Count} → {rig.Names.Count} bones: {string.Join(", ", rz.Report.Select(kv => $"{kv.Key} ×{kv.Value}"))}; donor joints without a target: [{string.Join(", ", rz.Unmatched)}]");
        var rigBad = NmbNetResize.CheckRigConsistency(rz.Nmb, strictNamePairs: true);
        res.Report.Add($"rig consistency: {(rigBad.Count == 0 ? "clean" : string.Join("; ", rigBad))}");
        var f = NmbFile.Read(rz.Nmb);
        int ni = f.IndexOf(10);
        byte[] x = (byte[])f.Bundles[ni].Data.Clone();
        // session 11 (crash11), rule IK1: uneven-terrain foot joints must be a parent chain of the new rig (exe+0x679119)
        res.Report.AddRange(IkChains.RepairUnevenTerrain(x, rig.Parents, rig.Names));

        // ---- 2. one moveset (rule B1) + the attack band widened to [10,39] (rule B2)
        int w0 = NmbNetEdit.SetFloatRange(x, "WeaponCategory_Ene", 0, 0, 0, 9);
        int wk = 0;
        for (int k = 1; k <= 9; k++) wk += NmbNetEdit.SetFloatRange(x, "WeaponCategory_Ene", k, k, 99, 99);
        int band = NmbNetEdit.SetFloatRange(x, "AttackCategory_Ene", 10, 19, 10, 39);
        res.Report.Add($"one moveset: WeaponCategory_Ene [0,0] → [0,9] on {w0} conditions, [k,k] (k ≥ 1) → [99,99] on {wk}; AttackCategory_Ene [10,19] → [10,39] on {band}");

        // ---- 3. categories: reuse / clone (rule B2)
        var names = NmbNetEdit.NodeNames(x);
        var net = new NmbNetwork(x);
        var cats = NmbNetEdit.AttackCategories(net, names);
        // session 11 (burgchr11): a template whose [0,9] band holds no category (the Stray Dog c2270: its attacks are 10-12) still has
        // the band SM; DS1 attacks 3000-3009 are cloned into it from a single-stage category of the other band
        // (and the Crystal Lizard c2130: categories 0 / 1 only, an empty [10,19] band)
        int? low0 = cats.Keys.Any(k => k <= 9) ? cats[cats.Keys.Where(k => k <= 9).Min()].Band : NmbNetEdit.BandOf(net, names, 0);
        int highBand = cats.Keys.Any(k => k >= 10) ? cats[cats.Keys.Where(k => k >= 10).Min()].Band : NmbNetEdit.BandOf(net, names, 10) ?? low0 ?? throw new InvalidDataException("no attack band");
        int lowBand = low0 ?? highBand;
        int StageCount(int sm) => NmbNetEdit.Stages(new NmbNetwork(x), names, sm).Count;
        int singleHigh0 = cats.Where(kv => kv.Key >= 10 && StageCount(kv.Value.Sm) == 1).Select(kv => kv.Value.Sm).FirstOrDefault(-1);
        int singleLow = cats.Where(kv => kv.Key <= 9 && StageCount(kv.Value.Sm) == 1).Select(kv => kv.Value.Sm).DefaultIfEmpty(singleHigh0).First();
        int singleHigh = singleHigh0 >= 0 ? singleHigh0 : singleLow;
        var reused = new List<int>(); var cloned = new List<string>();
        foreach (var (n, a) in spec.SingleCategories.Where(x => ds1.Has(x.Clip)))   // session 11 (chrai11): + the FA1 clips' categories
        {
            if (cats.ContainsKey(n)) { reused.Add(n); continue; }
            int tpl = n <= 9 ? singleLow : singleHigh, parent = n <= 9 ? lowBand : highBand;
            var c = NmbNetEdit.AddCategory(x, tpl, n, parent);
            x = c.Network;
            cloned.Add($"{n} (DS1 {a}) from SM {tpl} → SM {c.IdMap[tpl]}{(c.Copied.Length > 0 ? $" (rule E2b: {c.Copied})" : "")}");
            names = NmbNetEdit.NodeNames(x); cats = NmbNetEdit.AttackCategories(new NmbNetwork(x), names);
        }
        var chainCats = new Dictionary<int, int>();
        for (int k = 0; k < spec.Chains.Length; k++)
        {
            var ch = spec.Chains[k];
            if (!ch.All(ds1.Has)) { res.Report.Add($"WARNING chain {string.Join("→", ch)}: a clip is missing"); continue; }
            int val = 30 + k;
            var byStages = cats.Where(kv => kv.Key < 30).Select(kv => (kv.Key, kv.Value.Sm, N: StageCount(kv.Value.Sm))).Where(t => t.N >= ch.Length).OrderBy(t => t.N).ThenBy(t => t.Key).ToList();
            if (byStages.Count == 0) throw new InvalidDataException($"no template category with {ch.Length} route-1 stages for chain {string.Join("→", ch)}");
            var c = NmbNetEdit.AddCategory(x, byStages[0].Sm, val, highBand);
            x = c.Network;
            chainCats[k] = val;
            cloned.Add($"{val} (chain {string.Join("→", ch)}) from category {byStages[0].Key} (SM {byStages[0].Sm}, {byStages[0].N} stages) → SM {c.IdMap[byStages[0].Sm]}{(c.Copied.Length > 0 ? $" (rule E2b: {c.Copied})" : "")}");
            names = NmbNetEdit.NodeNames(x); cats = NmbNetEdit.AttackCategories(new NmbNetwork(x), names);
        }
        res.Report.Add($"attack categories: reused {string.Join(", ", reused)}; cloned {cloned.Count}: {string.Join("; ", cloned)}");

        // ---- 4. the leaf plan
        net = new NmbNetwork(x);
        var fileNames = NmbNetEdit.ReadFileNames(f.Bundles[f.IndexOf(13)].Data);
        string ClipName(int leaf) { var sa = NmbNetwork.Find(net.GetNode(leaf), 21)!; int ci = net.ClipIndex(sa); return ci < fileNames.Files.Count ? fileNames.Files[ci] : ""; }
        var chainOf = chainCats.ToDictionary(kv => kv.Value, kv => kv.Key);
        int slotsUsedBy(int a) => SlotCount(HitGroups(ds1.TaeAnim(a)));   // rule B4b
        foreach (var (n, (bandSm, sm)) in cats.OrderBy(kv => kv.Key))
        {
            var stages = NmbNetEdit.Stages(net, names, sm);
            int slotOffset = 0;
            for (int s = 0; s < stages.Count; s++)
            {
                var leaf = NmbNetEdit.Leaves(net, stages[s].State).FirstOrDefault(l => !ClipName(l).StartsWith("a00_50_", StringComparison.OrdinalIgnoreCase));
                if (leaf == 0) continue;
                var p = new LeafPlan { Node = leaf, Category = n, Stage = s, Reachable = true };
                if (chainOf.TryGetValue(n, out int k))
                {
                    p.Chain = k;
                    if (s < spec.Chains[k].Length)
                    {
                        p.Ds1Id = spec.Chains[k][s];
                        p.SlotOffset = slotOffset; slotOffset += slotsUsedBy(p.Ds1Id);
                        if (s + 1 < spec.Chains[k].Length && s + 1 < stages.Count) p.NextValue = stages[s + 1].Value is 101 or 102 ? stages[s + 1].Value : 101;
                    }
                }
                else if (s == 0 && spec.ClipOf(n) is int sc && ds1.Has(sc)) p.Ds1Id = sc;   // session 11 (chrai11): an FA1 category plays its clip
                if (p.Ds1Id >= 0) p.TaeId = TaeIdOf(n, s);
                res.Leaves.Add(p);
            }
        }
        var planned = res.Leaves.Select(l => l.Node).ToHashSet();
        res.Report.Add($"attack leaves: {res.Leaves.Count(l => l.Ds1Id >= 0)} play DS1 attacks, {res.Leaves.Count(l => l.Ds1Id < 0)} stage slots without a DS1 clip (DS1 idle, no attack)");

        // ---- 5. clips for the attack leaves: one file-name entry per DS1 clip (+ one DS1 idle for the empty stages)
        // size against DS2's humanoid (c1030 Root at 0.887 m): the foot-plant thresholds scale with it (c2232 Root 2.54 m)
        double scale = Math.Max(1, MorphemePose.From(rig).BindWorld()[rig.Root].Z / 0.887);
        res.Report.Add($"size scale {scale:0.###} (root {MorphemePose.From(rig).BindWorld()[rig.Root].Z:0.###} m / 0.887 m): foot-plant speed {PlantSpeed * scale:0.###} m/s, band {PlantHeight * scale:0.###} m, lock tolerance {LockTolerance * scale * 1000:0.#} mm");
        var encoded = new Dictionary<(int Id, bool Loop), (byte[] Bytes, NsaAnim Back, int Shift)>();
        (byte[] Bytes, NsaAnim Back, int Shift) Encode(int id, bool loop, MorphemePose pose)
        {
            if (!encoded.TryGetValue((id, loop), out var e))
            {
                var b = ds1.Bridge(id).Anim;
                int k = 0;
                if (loop)
                {
                    var hint = TaeFootfalls(ds1.TaeAnim(id), footDummy).Where(h => h.Foot == 'L').Select(h => (int?)h.Frame).FirstOrDefault();
                    k = CycleStart(b, pose, scale, hint); b = PhaseShift(b, k);
                }
                var by = NsaEncoder.EncodeFile(b).Build();
                encoded[(id, loop)] = e = (by, NsaCodec.Decode(NsaFile.Parse(by)), k);
            }
            return e;
        }
        var pose = MorphemePose.From(rig);
        var newEntries = new Dictionary<int, int>();   // DS1 id → clip index
        var protoEntry = bnd.Files.First(b => b.Name.EndsWith(".nsa", StringComparison.OrdinalIgnoreCase));
        string entryDir = protoEntry.Name[..(protoEntry.Name.Replace('/', '\\').LastIndexOf('\\') + 1)];
        // vanilla anibnds number the NSAs 0..n-1 and the NMB n (c3050: 0-121, NMB 122), in that order: the new NSAs take the
        // NMB's id onwards and the NMB moves to the end
        int nextBndId = nmbFile.ID;
        string stem = spec.NewChr;
        foreach (int id in res.Leaves.Select(l => l.Ds1Id < 0 ? 0 : l.Ds1Id).Distinct().Order())
        {
            var (bytes, _, _) = Encode(id, false, pose);
            uint crc = NmbNetEdit.Crc32Bzip2(bytes);
            string anim = $"{stem}_{Ds1ChrSource.ClipName(id)}";
            string name = NmbNetEdit.NsaName(anim, crc);
            fileNames.Files.Add(name); fileNames.Formats.Add("nsa"); fileNames.Sources.Add($"$(RootDir)\\motion_xmd\\{anim}.xmd"); fileNames.Takes.Add("untitled"); fileNames.Hashes.Add(crc);
            newEntries[id] = fileNames.Files.Count - 1;
            bnd.Files.Insert(bnd.Files.IndexOf(nmbFile), new BinderFile(protoEntry.Flags, nextBndId++, entryDir + name, bytes));
        }
        nmbFile.ID = nextBndId;
        foreach (var l in res.Leaves) { l.ClipIndex = newEntries[l.Ds1Id < 0 ? 0 : l.Ds1Id]; NmbNetEdit.SetClipIndex(x, l.Node, l.ClipIndex); }
        res.Report.Add($"file-name table: {newEntries.Count} entries added (DS1 {string.Join(", ", newEntries.Keys.Select(Ds1ChrSource.ClipName))}), names by DS2's rule with CRC-32/BZIP2 of our NSA bytes");

        // ---- 6. every attack leaf gets track bundles of its own
        var trackBundles = f.Bundles.Where(b => b.Type is 3 or 4 or 5).ToDictionary(b => b.AssetId);
        var usedIds = f.Bundles.Select(b => b.AssetId).ToHashSet();
        uint seq = 0;
        uint NewId()
        {
            while (true)
            {
                uint id = NmbNetEdit.Crc32Bzip2(System.Text.Encoding.ASCII.GetBytes($"lordran/{spec.NewChr}/track/{seq++}"));
                if (id != 0 && usedIds.Add(id)) return id;
            }
        }
        var added = new List<(uint, byte[], uint, byte[])>();
        foreach (var l in res.Leaves) added.AddRange(NmbNetEdit.UnshareTracks(x, l.Node, trackBundles, NewId));
        res.Report.Add($"event tracks: {added.Count} bundles of their own for {res.Leaves.Count} attack leaves");

        // ---- 6b. (step 2, rules C12d/C12f) DS1's "Ignore Gravity" (JT 27) and "Disable Map/Object Collision" (JT 19) windows
        // as DS2's Gravity (userData 9600, value 9610) and HitControl (9700, 9710) tracks on every leaf whose DS1 clip has
        // them and whose node has no such track: DS2 carries exactly these on its ladder clips (gravity off + map hit off
        // while climbing, c1030 a71_*) and its dropping start-ups (Royal Rat Authority a75_01_7101: Gravity 0-0.767 over its
        // 32.6 m drop; the Pursuer's 30 m dive a75_01_7111: 9610 over the clip; the Giant's c1210 start-ups: HitControl 9710
        // + Gravity 9610). The Asylum Demon: 9000/9060 (the intro: waits on the roof, jumps 28 m through the hall's rafters),
        // 3014 (mezzanine), 3023 (the jump to the balcony), 701 (back step's hop)
        {
            var extra = new Dictionary<(int Clip, int Kind), (uint Id, uint Size)>();
            var leafPlanEarly = res.Leaves.ToDictionary(l => l.Node);
            var fn13 = NmbNetEdit.ReadFileNames(f.Bundles[f.IndexOf(13)].Data);
            int addedTracks = 0; var notes = new List<string>();
            var kinds = new (int Ud, int Value, int Jt, string Name)[] { (9600, 9610, 27, "Gravity"), (9700, 9710, 19, "HitControl") };
            var allTracks = new Dictionary<uint, NmbFile.Bundle>(trackBundles);
            foreach (var a in added) allTracks[a.Item1] = new NmbFile.Bundle { Type = a.Item3, AssetId = a.Item1, Data = a.Item4 };
            var leafIds = new NmbNetwork(x).Nodes().Where(nd => nd.Type == 104).Select(nd => nd.Index).ToList();
            foreach (int leaf in leafIds)
            {
                var netNow = new NmbNetwork(x);
                var node = netNow.GetNode(leaf);
                var sa = NmbNetwork.Find(node, 21); var et = NmbNetwork.Find(node, 23);
                if (sa == null || et == null) continue;
                int ci = netNow.ClipIndex(sa);
                string clipName = ci >= 0 && ci < fn13.Files.Count ? fn13.Files[ci] : "";
                int ds1Id;
                bool loop = false;
                if (leafPlanEarly.TryGetValue(leaf, out var lp)) ds1Id = lp.Ds1Id < 0 ? 0 : lp.Ds1Id;
                else
                {
                    var role = RoleFor(spec.Roles, clipName);
                    if (role == null) continue;
                    ds1Id = role.Ds1.FirstOrDefault(v => v >= 0 && ds1.Has(v), -1);
                    loop = role.Loop;
                    if (ds1Id < 0) continue;
                }
                var tae = ds1.TaeAnim(ds1Id);
                var tracks = netNow.Tracks(et).Select(t => allTracks.TryGetValue(t.TrackId, out var b) ? NmbEventTrack.Read(b.Data, b.Type) : null).Where(t => t != null).ToList();
                foreach (var (ud, value, jt, nm) in kinds)
                {
                    var w = JumpTable(tae, jt);
                    if (w.Count == 0 || tracks.Any(t => t!.UserData == ud)) continue;
                    if (!extra.TryGetValue((ds1Id, ud), out var tb))
                    {
                        var (_, back, _) = Encode(ds1Id, loop, MorphemePose.From(rig));
                        double d1 = (back.Frames - 1) / back.Fps;
                        float Fr(double t) => (float)(d1 > 0 ? Math.Clamp(t / d1, 0, 1) : 0);
                        var proto = tracks.FirstOrDefault(t => t!.BundleType == 4) ?? throw new InvalidDataException($"node {leaf}: no duration track to copy the layout from");
                        (x, uint rid) = NmbNetEdit.EnsureTrackName(x, nm);
                        var tr = new NmbEventTrack
                        {
                            BundleType = 4, ChannelId = proto.ChannelId, UserData = ud, TrackId = (int)rid, NameBytes = System.Text.Encoding.ASCII.GetBytes(nm),
                            Events = [.. w.Select(v => (Fr(v.S), Fr(v.E) - Fr(v.S), value)).Where(e => e.Item2 > 0)],
                        };
                        byte[] data = tr.Write();
                        uint id = NewId();
                        var guid = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.ASCII.GetBytes($"lordran-track-{id:x8}"));
                        added.Add((id, guid, 4u, data));
                        allTracks[id] = new NmbFile.Bundle { Type = 4, AssetId = id, Data = data };
                        extra[(ds1Id, ud)] = tb = (id, (uint)data.Length);
                        notes.Add($"{Ds1ChrSource.ClipName(ds1Id)} {nm} {string.Join(" ", tr.Events.Select(e => $"{e.Start:0.###}+{e.Duration:0.###}:{e.Value}"))} (runtime id {rid})");
                    }
                    x = NmbNetEdit.AddLeafTracks(x, leaf, [(2, tb.Id, tb.Size)]);
                    addedTracks++;
                }
            }
            res.Report.Add($"DS1 gravity / map-collision windows → new DS2 tracks: {addedTracks} leaf references, {extra.Count} bundles: {string.Join("; ", notes)}");
        }
        NmbNetEdit.InsertTrackBundles(f, added);
        var repl = new Dictionary<int, byte[]> { [f.IndexOf(10)] = x, [f.IndexOf(13)] = NmbNetEdit.WriteFileNames(fileNames, fileNames.Pad) };
        f = NmbFile.Read(f.Rebuild(repl));

        // ---- 7. clips of the template slots by role (rule as the Hollow), the attack entries by plan
        ni = f.IndexOf(10);
        net = new NmbNetwork((byte[])f.Bundles[ni].Data.Clone());
        var leafPlan = res.Leaves.ToDictionary(l => l.Node);
        var clipByIndex = new Dictionary<int, ClipInfo>();
        var backByIndex = new Dictionary<int, NsaAnim>();
        var attackIndex = newEntries.ToDictionary(kv => kv.Value, kv => kv.Key);
        var noReaction = new HashSet<int>();
        // loops' touchdown count = the sync events of the walk-state slots (Hollow rule C11b: DS2 gives them the loops' count)
        int loopSync = 0;
        foreach (var bf in bnd.Files.Where(b => b.Name.EndsWith(".nsa", StringComparison.OrdinalIgnoreCase)))
        {
            string name = Path.GetFileName(bf.Name.Replace('\\', '/'));
            int idx = fileNames.Files.FindIndex(s => s.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) { res.Report.Add($"WARNING {name}: not in the file-name table"); continue; }
            Role role; int id;
            bool isAttack = attackIndex.TryGetValue(idx, out int aid);
            if (isAttack) { id = aid; role = new Role(name, [aid], aid == 0 ? "idle (attack stage without a DS1 clip)" : $"DS1 attack {aid}", "P", "rule B2", NoAttack: aid == 0); }
            else
            {
                role = RoleFor(spec.Roles, name) ?? throw new InvalidDataException($"no DS1 role for clip slot {name}");
                id = role.Ds1.FirstOrDefault(v => v == -1 || ds1.Has(v), int.MinValue);
                if (id == int.MinValue) throw new InvalidDataException($"{name}: none of DS1 {string.Join("/", role.Ds1)} in {spec.Ds1Chr}");
            }
            byte[] bytes; NsaAnim back; int shift = 0;
            var ds2 = isAttack ? null : NsaFile.Parse(bf.Bytes);
            if (id == -1)
            {
                var a = BindClip(rig, ds2!.Duration);
                bytes = NsaEncoder.EncodeFile(a).Build();
                back = NsaCodec.Decode(NsaFile.Parse(bytes));
            }
            else (bytes, back, shift) = Encode(id, role.Loop, pose);
            var orig = ds2 != null ? NsaCodec.Decode(ds2) : null;
            bf.Bytes = bytes;
            double d1 = (back.Frames - 1) / back.Fps;
            var tae = id >= 0 ? ds1.TaeAnim(id) : null;
            double Frac(double t) => d1 > 0 ? Math.Clamp(t / d1, 0, 1) : 0;
            var j78 = JumpTable(tae, 78); var j79 = JumpTable(tae, 79); var j23 = JumpTable(tae, 23);
            var cancelList = j78.Count > 0 ? j78 : j79.Count > 0 ? j79 : j23;
            var cancel = cancelList.Count > 0 ? cancelList[0] : (-1.0, -1.0);
            var death = JumpTable(tae, 12).Concat(JumpTable(tae, 69)).OrderBy(v => v.S).FirstOrDefault((-1.0, -1.0));
            bool nr = !isAttack && spec.NoReaction.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            if (nr) noReaction.Add(idx);
            var info = new ClipInfo
            {
                Name = name, Role = nr ? "no reaction (DS1 idle)" : role.Name, Status = role.Status, Ds1Id = id, NoAttack = role.NoAttack, Loop = role.Loop && id >= 0, Shift = shift, SyncEvents = role.SyncEvents,
                Ds2Duration = ds2?.Duration ?? d1, Ds1Duration = d1, Frames = back.Frames, Fps = back.Fps,
                Ds2RootEnd = orig?.RootPos[^1] ?? back.RootPos[^1], Ds1RootEnd = back.RootPos[^1], Ds1Tae = id >= 0 ? DescribeDs1Tae(tae) : "",
                DeathState = death.Item1 >= 0 ? Frac(death.Item1) : -1,
                Cancel = cancel.Item1 >= 0 ? Frac(cancel.Item1) : -1,
                NoGravity = JumpTable(tae, 27).Select(w => (Frac(w.S), Frac(w.E))).Where(w => w.Item2 > w.Item1).ToList(),
            };
            res.Clips.Add(info);
            clipByIndex[idx] = info; backByIndex[idx] = back;
        }
        // feet of every clip (loops first: their touchdown count is the walk-state slots' sync count)
        var feetOf = new Dictionary<int, (List<(float, float, int)> L, List<(float, float, int)> R, List<(float, float, int)> Sync, string Note)>();
        (List<(float, float, int)> L, List<(float, float, int)> R, List<(float, float, int)> Sync, string Note) Feet(int ci)
        {
            if (feetOf.TryGetValue(ci, out var f0)) return f0;
            var info = clipByIndex[ci]; var back = backByIndex[ci];
            List<(int, char)>? hint = null;
            if (info.Loop && info.Ds1Id >= 0)
            {
                int P = back.Frames - 1;
                hint = TaeFootfalls(ds1.TaeAnim(info.Ds1Id), footDummy).Select(h => (((h.Frame - info.Shift) % P + P) % P, h.Foot)).ToList();
            }
            var f = info.Ds1Id == -1 ? ([(0f, 1f, 10)], [(0f, 1f, 11)], [(0f, 1f, 0)], "") : FootEvents(back, pose, info.Loop, info.SyncEvents, scale, hint);
            feetOf[ci] = f;
            info.Steps = $"L{Ev(f.L)} R{Ev(f.R)} sync {f.Sync.Count} {Ev(f.Sync)}{(info.Loop ? $" (loop started at DS1 frame {info.Shift}, touchdowns {f.Note})" : "")}";
            return f;
        }
        foreach (var (ci, info) in clipByIndex.Where(kv => kv.Value.Loop)) loopSync = Math.Max(loopSync, Feet(ci).Item3.Count);
        foreach (var c in res.Clips.Where(c => c.SyncEvents == -1)) c.SyncEvents = Math.Max(1, loopSync);

        // ---- 8. SourceAnim + event tracks
        var trackBundle = new Dictionary<uint, int>();
        for (int i = 0; i < f.Bundles.Count; i++) if (f.Bundles[i].Type is 3 or 4 or 5) trackBundle[f.Bundles[i].AssetId] = i;
        var wishes = new Dictionary<uint, (NmbEventTrack Track, TrackKind Kind, List<(int Clip, List<(float, float, int)>? Events)> Wish)>();
        var sizeFields = new Dictionary<uint, List<int>>();
        int patched = 0;
        var timeActOf = new Dictionary<int, HashSet<int>>();
        foreach (var node in net.Nodes().Where(n => n.Type == 104))
        {
            var sa = NmbNetwork.Find(node, 21);
            if (sa == null) continue;
            int ci = net.ClipIndex(sa);
            if (!backByIndex.TryGetValue(ci, out var back)) continue;
            net.PatchSourceAnim(sa, back.RootPos[0], back.RootRot[0], back.RootPos[^1], back.RootRot[^1], (back.Frames - 1) / back.Fps);
            if (net.F32(sa.Offset + 0x80) != 0f || net.F32(sa.Offset + 0x84) != 1f) { net.PutF32(sa.Offset + 0x80, 0f); net.PutF32(sa.Offset + 0x84, 1f); }
            patched++;
            var info = clipByIndex[ci];
            leafPlan.TryGetValue(node.Index, out var plan);
            // a leaf that plays a template a65 slot outside the plan (slot-1+ subtrees, unreachable after rule B1): no attack
            bool unreachableAttack = plan == null && info.Name.StartsWith("a65_", StringComparison.OrdinalIgnoreCase);
            var et = NmbNetwork.Find(node, 23);
            if (et == null) continue;
            var feet = Feet(ci);
            var tae = info.Ds1Id >= 0 ? ds1.TaeAnim(info.Ds1Id) : null;
            int syncIndex = net.SyncTrackIndex(sa), discrete = 0;
            foreach (var (group, tid, sizeField) in net.Tracks(et))
            {
                bool isSync = group == 0 && discrete++ == syncIndex;
                if (!trackBundle.TryGetValue(tid, out int bi)) continue;
                var tb = f.Bundles[bi];
                var tr = NmbEventTrack.Read(tb.Data, tb.Type);
                if (sizeField >= 0) { if (!sizeFields.TryGetValue(tid, out var l)) sizeFields[tid] = l = []; if (!l.Contains(sizeField)) l.Add(sizeField); }
                var kind = KindOf(tr, isSync);
                List<(float, float, int)>? events;
                if (plan is { Ds1Id: >= 0 })
                    events = AttackEvents(kind, tr, info, plan, tae, feet);
                else if (plan != null || unreachableAttack)
                {
                    // attack stage without a DS1 clip: DS1 idle, no attack (rule C12c), TimeAct → the idle's TAE (id 0)
                    var noAtk = new ClipInfo { NoAttack = true, Ds1Id = info.Ds1Id };
                    events = tr.UserData == 1000 ? [(0f, 1f, 0)] : kind switch
                    {
                        TrackKind.Sync => feet.Sync, TrackKind.LeftFoot => feet.L, TrackKind.RightFoot => feet.R,
                        _ => Gameplay(kind, tr, noAtk),
                    };
                }
                else
                    events = kind switch
                    {
                        TrackKind.Sync => feet.Sync, TrackKind.LeftFoot => feet.L, TrackKind.RightFoot => feet.R,
                        TrackKind.Damage when noReaction.Contains(ci) => [],
                        TrackKind.ReturnToBase when noReaction.Contains(ci) => [(0f, 1f, 80)],
                        _ => Gameplay(kind, tr, info),
                    };
                if (!wishes.TryGetValue(tid, out var w)) wishes[tid] = w = (tr, kind, []);
                if (!w.Wish.Any(v => v.Clip == ci)) w.Wish.Add((ci, events));
            }
        }
        var newTracks = new Dictionary<uint, (NmbEventTrack Track, List<(float, float, int)> Ds2, TrackKind Kind, List<int> Clips)>();
        int merged = 0;
        foreach (var (tid, (tr, kind, wish)) in wishes)
        {
            if (wish.All(v => v.Events == null)) continue;
            var ds2Events = tr.Events.ToList();
            List<(float, float, int)>? cur = null;
            foreach (var (ci, ev) in wish)
            {
                var e = ev ?? ds2Events;
                if (cur == null) { cur = e; continue; }
                if (cur.SequenceEqual(e)) continue;
                cur = Merge(kind, cur, e, ds2Events); merged++;
            }
            if (cur!.SequenceEqual(ds2Events)) continue;
            tr.Events = cur!;
            newTracks[tid] = (tr, ds2Events, kind, wish.Select(v => v.Clip).ToList());
        }
        var repl2 = new Dictionary<int, byte[]>();
        foreach (var (tid, (tr, ds2Events, kind, cis)) in newTracks)
        {
            byte[] data = tr.Write();
            repl2[trackBundle[tid]] = data;
            if (sizeFields.TryGetValue(tid, out var fields)) foreach (int o in fields) net.PutU32(o, (uint)data.Length);
            foreach (int c in cis) clipByIndex[c].TrackLog.Add($"{tr.Name} ({kind}, ud {tr.UserData}{(cis.Count > 1 ? $", shared by {cis.Count} slots" : "")}): {Ev(ds2Events)} → {Ev(tr.Events)}");
        }
        // chain stage blends (rule B3d): DS2's stage transitions are TransitSyncEvents whose duration counts sync events of
        // the source clip (template values 0-0.5); the blend into DS1's next chain clip should last DS1's own blend (TAE type
        // 16 "Blend" at the start of the next clip, 0.333 s on every c2232 attack), so the duration becomes that time over
        // the length of the source's sync event at the hand-over (JT 23 start). A 402 transition counts sync events, a 400 one
        // seconds.
        int blends = 0;
        var blendNotes = new List<string>();
        foreach (var (val, k) in chainOf)
        {
            if (!cats.TryGetValue(val, out var cat)) continue;
            var stg = NmbNetEdit.Stages(net, names, cat.Sm);
            var d = NmbNetEdit.Sm(net, cat.Sm);
            var idx = d.States.Select((st, i) => (st.Node, i)).ToDictionary(t => (int)t.Node, t => t.i);
            for (int st = 0; st + 1 < stg.Count && st + 1 < spec.Chains[k].Length; st++)
            {
                int src = spec.Chains[k][st], dst = spec.Chains[k][st + 1];
                var srcInfo = res.Clips.First(c => c.Ds1Id == src);
                var srcTae = ds1.TaeAnim(src);
                double d1 = srcInfo.Ds1Duration;
                var hitsSrc = HitGroups(srcTae);
                double h = hitsSrc.Count > 0 ? Math.Clamp(hitsSrc[0].Start / d1, 0, 1) : 0;
                var j23 = JumpTable(srcTae, 23);
                double cAt = j23.Count > 0 ? j23[0].S / d1 : 0.8;
                double syncSec = (h > 0 && cAt >= h ? 1 - h : h > 0 ? h : 1) * d1;
                var blendEv = ds1.TaeAnim(dst)?.Events.FirstOrDefault(e => e.Type == 16);
                double blendSec = blendEv != null ? blendEv.EndTime - blendEv.StartTime : 1 / 3.0;
                foreach (var tr in d.States.Where(t => t.Src == idx[stg[st].State] && t.Dst == idx[stg[st + 1].State]))
                {
                    var tn = net.GetNode(tr.Node);
                    var a59 = NmbNetwork.Find(tn, 59);
                    if (a59 == null) continue;
                    float old = net.F32(a59.Offset + 0x10);
                    float dur = tn.Type == 402 ? (float)(blendSec / syncSec) : (float)blendSec;
                    net.PutF32(a59.Offset + 0x10, dur);
                    blends++;
                    blendNotes.Add($"{src}→{dst} transition {tr.Node} ({(tn.Type == 402 ? "sync events" : "s")}): {old:0.###} → {dur:0.###} (DS1 blend {blendSec:0.###} s, source sync event {syncSec:0.###} s)");
                }
            }
        }
        res.Report.Add($"chain stage blends: {blends} transitions set to DS1's blend time: {string.Join("; ", blendNotes)}");
        // session 11 (track enemy11, G1): the regulars' network rules on the boss network too (Ds1NetRules: NECK, HIPS, TERRAIN, LM1,
        // PB1, BL1; HB1 is c1030's)
        {
            Ds1NetRules.ClipFacts? Facts(int ci)
            {
                if (!clipByIndex.TryGetValue(ci, out var inf)) return null;
                if (inf.Ds1Id < 0) return new(-1, inf.Ds1Duration, [], -1);
                var sy = Feet(ci).Sync;
                double[] evs = sy.Count > 0 ? sy.Select(e => e.Item2 * inf.Ds1Duration).ToArray() : [inf.Ds1Duration];
                return new(inf.Ds1Id, inf.Ds1Duration, evs, Ds1NetRules.BlendSpan(ds1.TaeAnim(inf.Ds1Id)), attackIndex.ContainsKey(ci) && inf.Ds1Id > 0);
            }
            double stand = -1;
            foreach (var (ci, inf) in clipByIndex)
                if (inf.Name.StartsWith("a00_01_0010", StringComparison.OrdinalIgnoreCase) && Facts(ci) is { } sf && sf.EventLengths.Length > 0) { stand = sf.EventLengths.Average(); break; }
            var nr = Ds1NetRules.Apply(net, Facts, new Ds1NetRules.Options { Template = "c3050", StandEvent = stand, IdleBlend = Ds1NetRules.BlendSpan(ds1.TaeAnim(0)) });
            res.Report.AddRange(nr.Select(l => "G1 " + l));
        }
        repl2[ni] = net.X;
        var finalNmb = f.Rebuild(repl2);
        // TimeAct ids per clip (after the rewrite)
        var ff = NmbFile.Read(finalNmb);
        var fnet = new NmbNetwork(ff.Bundles[ff.IndexOf(10)].Data);
        var tracks2 = ff.Bundles.Where(b => b.Type is 3 or 4 or 5).ToDictionary(b => b.AssetId, b => NmbEventTrack.Read(b.Data, b.Type));
        foreach (var node in fnet.Nodes().Where(n => n.Type == 104))
        {
            var sa = NmbNetwork.Find(node, 21); var et = NmbNetwork.Find(node, 23);
            if (sa == null || et == null || !clipByIndex.TryGetValue(fnet.ClipIndex(sa), out var info)) continue;
            foreach (var (_, tid, _) in fnet.Tracks(et))
                if (tracks2.TryGetValue(tid, out var t) && t.UserData == 1000)
                    foreach (var ev in t.Events) if (!info.TimeAct.Contains(ev.Value)) info.TimeAct.Add(ev.Value);
        }
        nmbFile.Bytes = finalNmb;
        string oldStem = Path.GetFileNameWithoutExtension(nmbFile.Name.Replace('\\', '/').Split('/')[^1]);
        nmbFile.Name = nmbFile.Name.Replace(oldStem, nmbName ?? spec.NewChr);
        res.NetworkProblems = NmbNetEdit.Check(ff.Bundles[ff.IndexOf(10)].Data, fileNames.Files.Count);
        res.Report.Add($"clips: {res.Clips.Count} NSA files ({encoded.Count} distinct DS1 clips, {res.Clips.Count(c => c.Ds1Id == -1)} bind-pose additive slots, {noReaction.Count} no-reaction slots); SourceAnim attribs patched {patched}; event tracks rewritten {newTracks.Count} (shared-track merges {merged}); loop touchdowns per cycle {loopSync}");
        res.Report.Add($"network checks: {(res.NetworkProblems.Count == 0 ? "clean" : string.Join("; ", res.NetworkProblems.Take(10)))}");
        // TAE ids → durations (non-attack slots: the clip whose name encodes the id owns it, else the first)
        var attackTae = res.Leaves.Where(l => l.TaeId > 0).Select(l => l.TaeId).ToHashSet();
        foreach (var c in res.Clips.OrderBy(c => c.TimeAct.Contains(NameId(c.Name)) ? 0 : 1))
            foreach (int ta in c.TimeAct)
                if (!attackTae.Contains(ta) && res.TaeIds.TryAdd(ta, (c.Ds1Duration, c.Ds2Duration, c.NoAttack))) res.TaeSource[ta] = c;
        foreach (var l in res.Leaves.Where(l => l.TaeId > 0))
        {
            var c = res.Clips.First(c => c.Ds1Id == l.Ds1Id);
            res.AttackTae[l.TaeId] = (l.Ds1Id, l.SlotOffset, c.Ds1Duration);
            res.AttackSource[l.TaeId] = c;
        }
        if (i1bReserved != null)
        {
            var id = NmbIdentity.Apply(nmbFile.Bytes, templateNmb, nmbName ?? spec.NewChr, i1bReserved);
            if (id.Problems.Count > 0) throw new InvalidDataException($"{nmbName ?? spec.NewChr}: {id.Summary}");
            nmbFile.Bytes = id.Nmb;
            res.Report.Add(id.Summary);
        }
        byte[] outBnd = bnd.Write();
        res.Anibnd = comp != null ? DCX.Compress(outBnd, comp) : outBnd;
        return res;
    }

    /// <summary>
    /// Event-track events of an attack leaf from DS1's TAE (rule B3; DS1 JumpTable names from Smithbox's TAE.Template.DS1.xml:
    /// 23 AI Continuation Attack, 78 AI Movement Cancel, 79 AI Dodge Cancel, 86 AI Attack Cancel). Fractions of the DS1 clip.
    /// <list type="bullet">
    /// <item>TimeAct (ud 1000): the leaf's own TAE id.</item>
    /// <item>HitDuration (123): DS1's hit windows (TAE type 1 "Attack Behavior"), overlapping windows merged into one.</item>
    /// <item>Sync (Footsteps): [0, first hit), [first hit, 1] - DS2's attack-clip form (Smelter a65_10_1010 0.333 = its hit
    /// start, a65_10_1020 0.24, a65_14_1010 0.259, a65_12_1010 0.265); one event when the clip has no hit.</item>
    /// <item>AttackStateControl (discrete, 100-106): 106 over the clip (inside [100,120]: SM_Main keeps the attack); a chain
    /// stage with a next stage has the next stage's value (101 or 102, what the template's route-1 transition waits for)
    /// over DS1's continuation window (JT 23), else from DS1's earliest AI cancel.</item>
    /// <item>AttackDuration (120) and AttackCancelControl (110): [0, earliest DS1 cancel (78/79/86)); _Move (111): [0, JT 78);
    /// _Attack (112): [0, JT 86 / JT 23).</item>
    /// <item>StateControl (80, return to base): from DS1's latest AI cancel start to the end.</item>
    /// <item>Foot-lock tracks: DS1 motion (rule C11a). Everything else keeps DS2's fractions (rule P).</item>
    /// </list>
    /// </summary>
    static List<(float, float, int)>? AttackEvents(TrackKind kind, NmbEventTrack tr, ClipInfo info, LeafPlan plan, TAE.Animation? tae,
        (List<(float, float, int)> L, List<(float, float, int)> R, List<(float, float, int)> Sync, string Note) feet)
    {
        double d1 = info.Ds1Duration;
        float F(double t) => (float)(d1 > 0 ? Math.Clamp(t / d1, 0, 1) : 0);
        var hits = HitGroups(tae);
        double? First(int jt) { var w = JumpTable(tae, jt); return w.Count > 0 ? w[0].S : null; }
        var cancels = new[] { First(78), First(79), First(86) }.Where(v => v != null).Select(v => v!.Value).ToList();
        double? cancelMin = cancels.Count > 0 ? cancels.Min() : null, cancelMax = cancels.Count > 0 ? cancels.Max() : null;
        if (tr.UserData == 1000) return [(0f, 1f, (int)plan.TaeId)];
        string nm = tr.Name;
        switch (kind)
        {
            case TrackKind.Sync:
                return hits.Count > 0 && F(hits[0].Start) > 0 && F(hits[0].Start) < 1 ? [(0f, F(hits[0].Start), 0), (F(hits[0].Start), 1 - F(hits[0].Start), 0)] : [(0f, 1f, 0)];
            case TrackKind.LeftFoot: return feet.L;
            case TrackKind.RightFoot: return feet.R;
            case TrackKind.ReturnToBase:
                if (cancelMax is double cm) return [(F(cm), 1 - F(cm), 80)];
                return null;
        }
        if (nm.StartsWith("HitDuration"))
        {
            // overlapping DS1 windows (direct hit + blast) are one HitDuration window (DS2's tracks hold disjoint events)
            var u = new List<(double S, double E)>();
            foreach (var h in hits.OrderBy(h => h.Start))
                if (u.Count > 0 && h.Start <= u[^1].E) u[^1] = (u[^1].S, Math.Max(u[^1].E, h.End)); else u.Add((h.Start, h.End));
            return [.. u.Select(w => (F(w.S), F(w.E) - F(w.S), 123))];
        }
        if (nm.StartsWith("AttackStateControl"))
        {
            if (plan.NextValue == 0) return [(0f, 1f, 106)];
            var j23 = JumpTable(tae, 23);
            double c = j23.Count > 0 ? j23[0].S : cancelMin ?? d1 * 0.8, e = j23.Count > 0 ? j23[0].E : d1;
            var o = new List<(float, float, int)>();
            if (F(c) > 0) o.Add((0f, F(c), 106));
            o.Add((F(c), F(e) - F(c), plan.NextValue));
            if (F(e) < 1) o.Add((F(e), 1 - F(e), 106));
            return o;
        }
        if (nm.StartsWith("AttackDuration")) return cancelMin is double a ? [(0f, F(a), 120)] : null;
        if (nm.StartsWith("AttackCancelControl_Move")) return First(78) is double m ? [(0f, F(m), 111)] : cancelMin is double m2 ? [(0f, F(m2), 111)] : null;
        if (nm.StartsWith("AttackCancelControl_Attack")) return (First(86) ?? JumpTable(tae, 23).Select(w => (double?)w.S).FirstOrDefault() ?? cancelMin) is double at ? [(0f, F(at), 112)] : null;
        if (nm.StartsWith("AttackCancelControl")) return cancelMin is double ac ? [(0f, F(ac), 110)] : null;
        if (kind == TrackKind.Gravity) return info.NoGravity.Select(w => ((float)w.S, (float)(w.E - w.S), 9610)).ToList();
        return null;
    }

    /// <summary>The role table as Markdown rows (NOTES.md).</summary>
    public static IEnumerable<string> TableRows(Result r) => Ds1RigAnims.TableRows(new Ds1RigAnims.Result { Clips = r.Clips });
}
