using System.Globalization;
using System.Numerics;
using System.Text;
using Lordran.Tools.Build.Chr;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Session 11 (track burgchr11), build-pack --burg-chrs: every character type of DS1 m10_01 (the Undead Burg, Lower Burg, Undead
/// Parish; DS2 host m19_41) as 1:1 DS2 characters WITHOUT placing anything in the Burg map (placement is the M3 track's):
/// <list type="number">
/// <item>the regulars: the Burg variants of the ported rigs and the new rigs (<see cref="ChrPorter.BurgChrs"/>, built here unless the
/// Asylum / Firelink build already did), the 1:1 bosses of m10_01 (<see cref="Ds1Boss.All"/> entries with Ds1Map m10_01);</item>
/// <item>the AI of every MSB part of a ported character: its DS1 think translated from m10_01's Lua (<see cref="Ai.Ds1Ai.Logic"/>);</item>
/// <item><c>lordran-chr\burg-placements.txt</c>: per MSB part the generator row M3 needs (entity → generator id by
/// <see cref="Events.EntityRules.Generator"/>, EnemyParam, logic pair, start-up type by rule P3a/P3, DS1 position / yaw, W1 world
/// condition, notes) and the parts not covered (no variant / no rig yet), so the placement track only has to write the rows.</item>
/// </list>
/// build-pack --burg-chr-test [ID+ID…] puts the selected Burg EnemyParams (default: one of each anim set) in the Asylum courtyard
/// instead of the Asylum's enemies (as --chr-test), each with the AI of its first Burg MSB part, for offline checks and in-game tests.
/// </summary>
public static class BurgChrs
{
    public const string Ds1Map = "m10_01_00_00";
    /// <summary>Session 11 (dchr11): the Depths (built with --map depths; its characters join --burg-chrs).</summary>
    public const string DepthsMap = "m10_00_00_00";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public sealed record Entry(string Part, int Entity, string Model, int Npc, int EnemyParam, int Think, (int High, int Low)? Logic, int InitAnim, Vector3 Position, float Yaw, string World, string Note);

    /// <summary>Builds the Burg characters into the pack and returns the placement table (also written to lordran-chr).</summary>
    /// <summary>Rules P3a/P3b (as AsylumEnemies.PoseStartUp, which M3's Burg placement applies): the start-up slot whose dormant loop
    /// plays the part's DS1 InitAnimID → GeneratorRegistParam.startUpStateType, or "standing (0)" with the reason.</summary>
    static string StartUpNote(int enemyParam, int initAnim)
    {
        var rig = Ai.Ds1Ai.RigOf(enemyParam);
        if (rig == null) return $"InitAnimID {initAnim}: no 1:1 network → startUpStateType 0";
        foreach (var (slot, type) in new[] { ("a75_01_7100", 2), ("a75_01_7110", 3), ("a75_01_7120", 4) }.Concat(Ds1RigAnims.AddedStartUps.Select(a => (a.Loop, a.Type))))   // session 11 (chrai11): rule P3
            if (rig.Roles.FirstOrDefault(r => r.Slot == slot) is { Ds1.Length: > 0 } role && role.Ds1[0] == initAnim)
                return $"InitAnimID {initAnim} → startUpStateType {type} ({slot})";
        return $"InitAnimID {initAnim}: no start-up slot plays it (P3 added categories open) → startUpStateType 0 (standing)";
    }

    public static List<Entry> Build(Pack pack, Ds1Source ds1, string dsrRoot)
    {
        var enemy = pack.Param("EnemyParam");
        Ai.EventRequests.Register(dsrRoot, Ds1Map);   // rule E-REQ (session 11): before any of the map's thinks is translated
        foreach (var o in ChrPorter.BurgChrs)
            if (enemy[o.Variants[0].EnemyParamId] == null) ChrPorter.Build(pack, dsrRoot, o, ds1);
        // bosses of m10_01 (not placed: their generators, BossBattleParam and events are M3's)
        // session 11 (dchr11): + the Depths' (m10_00) specs when --map depths is built (Ds1Boss.All holds them only then; placed by MapEnemies, rule MB1)
        foreach (var b in Ds1Boss.All.Where(b => b.Ds1Map == Ds1Map || b.Ds1Map == DepthsMap))
        {
            if (Maps.AsylumEnemies.OnlyEnemyParams.Count > 0 && !Maps.AsylumEnemies.OnlyEnemyParams.Contains(b.EnemyParam)) continue;
            var br = Ds1Boss.Build(pack, dsrRoot, ds1, b);
            ChrPorter.AddDs1SoundShared(pack, $"{b.Ds1Chr} 1:1 boss c{b.AnimId / 100:D4}", br.Files, br.TitleFiles);
            foreach (var l in br.Report) pack.Note($"burg boss {b.N}: {l}");
            foreach (var l in br.SoundLog) pack.Note($"burg boss {b.N} sound: {l}");
            Console.WriteLine($"  burg boss: {b.Ds1Chr} 1:1 → {b.N} / animId {b.AnimId}, EnemyParam {b.EnemyParam}, logic {b.HighLogic}/{b.LowLogic} ({br.Files.Count} files; not placed: M3)");
        }

        var msb = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", Ds1Map + ".msb"));
        var placed = Maps.AsylumEnemies.Placements(msb, Ds1Map, ChrPorter.BurgChrs, onlyGroup: null);
        var list = new List<Entry>();
        foreach (var p in placed)
        {
            var part = msb.Parts.Enemies.First(e => e.EntityID == p.Entity && p.Entity > 0 || e.Position == p.Position && e.NPCParamID == p.Variant.Ds1Npc);
            string note = "";
            (int, int)? logic = null;
            try { logic = Ai.Ds1Ai.Logic(pack, dsrRoot, Ds1Map, p.Variant.EnemyParamId, p.Think); }
            catch (Exception e) { note = $"AI not translated: {e.Message}"; pack.Note($"burg AI {part.Name} think {p.Think}: FAILED {e.Message}"); }
            string world = "";
            try { world = Maps.AsylumEnemies.WorldConditions(Ds1Map, p.Entity) is { } w ? $"start {w.Start} stop {(w.Stop?.ToString() ?? "-")} ({w.Entry})" : "always / event-owned"; }
            catch (Exception e) { world = $"W1 fold failed: {e.Message}"; }
            if (p.InitAnim > 0) note = (note.Length > 0 ? note + "; " : "") + StartUpNote(p.Variant.EnemyParamId, p.InitAnim);
            list.Add(new Entry(part.Name, p.Entity, part.ModelName, p.Variant.Ds1Npc, p.Variant.EnemyParamId, p.Think, logic, p.InitAnim, p.Position, p.Yaw, world, note));
        }
        // large regulars on the boss pipeline (rule LR1: the Titanite Demon, the dogs): every MSB part of their DS1 NpcParams
        foreach (var b in Ds1Boss.All.Where(b => b.Ds1Map == Ds1Map && b.LargeRegular))
        {
            var eps = new Dictionary<int, int> { [b.Ds1Npc] = b.EnemyParam };
            foreach (var (xn, xe) in b.ExtraVariants) eps[xn] = xe;
            foreach (var e in msb.Parts.Enemies.Where(e => e.ModelName == b.Ds1Chr && eps.ContainsKey(e.NPCParamID)))
            {
                string world = "";
                try { world = Maps.AsylumEnemies.WorldConditions(Ds1Map, e.EntityID) is { } w ? $"start {w.Start} stop {(w.Stop?.ToString() ?? "-")} ({w.Entry})" : "always / event-owned"; }
                catch (Exception ex) { world = $"W1 fold failed: {ex.Message}"; }
                list.Add(new Entry(e.Name, e.EntityID, e.ModelName, e.NPCParamID, eps[e.NPCParamID], e.ThinkParamID, (b.HighLogic, b.LowLogic), e.InitAnimID, e.Position, e.Rotation.Y, world,
                    e.ThinkParamID == (b.Ai?.ThinkParam ?? 0) ? "large regular (rule LR1, boss pipeline)" : $"large regular; its think {e.ThinkParamID} differs from the spec's {b.Ai?.ThinkParam} (perception only)"));
            }
        }
        // the m10_01 parts no Burg build covers yet
        var covered = list.Select(p => p.Position).ToHashSet();
        var bossChrs = Ds1Boss.All.Where(b => b.Ds1Map == Ds1Map).SelectMany(b => b.ExtraVariants.Select(x => (b.Ds1Chr, x.Npc)).Append((b.Ds1Chr, b.Ds1Npc))).ToHashSet();
        var missing = msb.Parts.Enemies.Where(e => !covered.Contains(e.Position) && !bossChrs.Contains((e.ModelName, e.NPCParamID)) && e.ModelName is not ("c1000" or "c0000"))
            .GroupBy(e => (e.ModelName, e.NPCParamID)).Select(g => $"{g.Key.ModelName} NpcParam {g.Key.NPCParamID}: {g.Count()} parts ({string.Join(", ", g.Take(4).Select(e => e.EntityID > 0 ? e.EntityID.ToString(Inv) : e.Name))}{(g.Count() > 4 ? ", …" : "")})").ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# burgchr11: the Burg's (DS1 m10_01 → DS2 m19_41) 1:1 characters, NOT placed. One line per MSB part: what its generator row needs (M3).");
        sb.AppendLine("# part\tentity\tgenerator (EntityRules)\tmodel\tDS1 NpcParam\tEnemyParam\tDS1 think\tlogic high/low\tInitAnimID\tDS1 position\tDS1 yaw (DS2 = +180, rule P1)\tW1 world condition\tnote");
        foreach (var e in list)
            sb.AppendLine(string.Join('\t', e.Part, e.Entity, e.Entity > 0 ? Events.EntityRules.Generator(e.Entity).ToString(Inv) : "-", e.Model, e.Npc, e.EnemyParam, e.Think,
                e.Logic is { } l ? $"{l.Item1}/{l.Item2}" : "template", e.InitAnim, $"{e.Position.X.ToString("0.00", Inv)} {e.Position.Y.ToString("0.00", Inv)} {e.Position.Z.ToString("0.00", Inv)}", e.Yaw.ToString("0.#", Inv), e.World, e.Note));
        sb.AppendLine();
        sb.AppendLine("# bosses (1:1 boss pipeline; generator / BossBattleParam / events are M3's):");
        // every MSB part of a boss spec's DS1 NpcParams (the Hellkite's bridge body 1010300 and its fly-over double 1010302, the
        // Gargoyles 1010800 / 1010801 and the intro stand-in 1010802): entity, EnemyParam, InitAnimID, DS1 position / yaw, B-k index
        var areas = Ds1Source.LoadParam(dsrRoot, "GameAreaParam");
        var bossEntities = msb.Parts.Enemies.Where(p => p.EntityID > 0 && areas?[p.EntityID] != null).Select(p => p.EntityID).Distinct().Order().ToList();
        foreach (var b in Ds1Boss.All.Where(b => b.Ds1Map == Ds1Map && !b.LargeRegular))
        {
            var eps = new Dictionary<int, int> { [b.Ds1Npc] = b.EnemyParam };
            foreach (var (xn, xe) in b.ExtraVariants) eps[xn] = xe;
            foreach (var e in msb.Parts.Enemies.Where(e => e.ModelName == b.Ds1Chr && eps.ContainsKey(e.NPCParamID)))
                sb.AppendLine($"boss\t{e.EntityID}\t{b.Ds1Chr}\tEnemyParam {eps[e.NPCParamID]}\tmodel c{b.Model:D4}\tanimId {b.AnimId}\tlogic {b.HighLogic}/{b.LowLogic}\tInitAnimID {e.InitAnimID}"
                    + $"\tDS1 pos {e.Position.X.ToString("0.00", Inv)} {e.Position.Y.ToString("0.00", Inv)} {e.Position.Z.ToString("0.00", Inv)} yaw {e.Rotation.Y.ToString("0.#", Inv)}\tB-k {bossEntities.IndexOf(e.EntityID)}\t{e.Name}");
        }
        sb.AppendLine();
        // rule E-REQ: the AI channels the events must drive (2004:17(e, v, slot) → set the entity's generator logicEventFlag(k+1)
        // flag, clear the slot's other channels; v = -1 → clear the slot's channels)
        sb.AppendLine("# AI event requests (rule E-REQ; DS1 2004:17 → generator logicEventFlag(k+1), read by the AI as f5903(k)):");
        sb.AppendLine("# ereq	DS1 entity	DS1 think	slot	command	k	GeneratorParam field");
        var reqs = Ai.EventRequests.ByEntity(dsrRoot, Ds1Map);
        foreach (var e in msb.Parts.Enemies.Where(e => reqs.ContainsKey(e.EntityID)).OrderBy(e => e.EntityID))
            foreach (var (slot, v, k) in Ai.EventRequests.ForThink(e.ThinkParamID).Where(c => reqs[e.EntityID].Contains((c.Slot, c.V))))
                sb.AppendLine($"ereq	{e.EntityID}	{e.ThinkParamID}	{slot}	{v}	{k}	logicEventFlag{k + 1}");
        sb.AppendLine();
        sb.AppendLine("# m10_01 parts of characters not ported yet:");
        foreach (var m in missing) sb.AppendLine("missing\t" + m);
        pack.AddFile("lordran-chr", "burg-placements.txt", Encoding.UTF8.GetBytes(sb.ToString()));
        Console.WriteLine($"  burg chrs: {list.Count} MSB parts covered ({list.Count(e => e.Logic != null)} with the translated DS1 AI), {missing.Count} character groups not ported yet (lordran-chr\\burg-placements.txt)");
        return list;
    }

    /// <summary>--burg-chr-test: the selected Burg EnemyParams (or one of each anim set) as test generators in front of the map's bonfire,
    /// each with the AI of its first Burg MSB part. Ids: 9100 + k (not the default placements').</summary>
    public static List<EnemyTest.Custom> TestPlacements(Pack pack, string dsrRoot, MapBuilder.Options map, List<Entry> entries, IReadOnlyCollection<int> only)
    {
        var pick = (only.Count > 0 ? entries.Where(e => only.Contains(e.EnemyParam)) : entries.GroupBy(e => Ai.Ds1Ai.RigOf(e.EnemyParam)?.AnimId ?? e.EnemyParam).Select(g => g.First()))
            .DistinctBy(e => e.EnemyParam).ToList();
        var nav = NGP.Read(pack.GetFile("gamedata", $"map/{map.Ds2Map}/{map.Ds2Map}.ngp"));
        var o = new List<EnemyTest.Custom>();
        for (int k = 0; k < pick.Count; k++)
        {
            var e = pick[k];
            float x = map.BonfirePos.X + (k % 5 - 2) * 2.5f, z = map.BonfirePos.Z - 5 - k / 5 * 2.5f;
            float y = EnemyTest.NavHeight(nav, x, z, map.BonfirePos.Y) ?? map.BonfirePos.Y;
            var v = ChrPorter.BurgChrs.SelectMany(c => c.Variants).First(vv => vv.EnemyParamId == e.EnemyParam);
            o.Add(new EnemyTest.Custom(1600, 9100 + k, e.EnemyParam, e.Logic?.High ?? v.HighLogic, e.Logic?.Low ?? v.LowLogic, new Vector3(x, y + 0.1f, z), 0) { RegistId = e.EnemyParam * 100 + 90 });
            Console.WriteLine($"  burg chr test: generator {9100 + k} EnemyParam {e.EnemyParam} (DS1 {e.Npc}, {e.Model}, think {e.Think}) at ({x:0.0}, {z:0.0})");
        }
        return o;
    }
}
