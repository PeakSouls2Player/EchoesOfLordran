using System.Text;
using Lordran.Tools.Build.Chr;
using SoulsFormats;

namespace Lordran.Tools.Build.Ai;

/// <summary>
/// The translated DS1 AI in a pack (session 8, track enemies step 2): for each placed 1:1 character, its DS1 think (the MSB
/// part's ThinkParamID → NpcThinkParam → battle goal) becomes a DS2 logic pair (<see cref="AiTranslator"/>) — files
/// <c>gamedata\ezstate\ai{High}.esd</c> / <c>ai{Low}.esd</c>, a LogicComParam row High, EnemyDefaultLogicParam[animId]
/// pointing at the pair — and the generator's GeneratorRegistParam row carries High / Low. The DS2 template logic is the
/// Hollow Infantry's (upper 103010, lower 103000) for every 1:1 character: their networks are all c1030's, and the
/// template lower's non-battle machines drive that network (DS2 rules for searching, chasing, ladders).
/// <c>lordran-ai\manifest.txt</c> (for <c>ai-check</c>) and <c>lordran-ai\coverage-THINK.txt</c> go into the pack.
/// </summary>
public static class Ds1Ai
{
    /// <summary>Session 11 (burg11, rule DL1): the host map being built (BuildPack's host loop), null outside it.</summary>
    public static string? CurrentMap { get; set; }
    public const int TemplateHigh = 103010, TemplateLow = 103000;

    sealed class State
    {
        public readonly Dictionary<(int Think, int AnimId), (int High, int Low)> Done = [];
        public readonly Dictionary<int, string> DefaultOwner = [];   // rule DL1: anim set → the host map that set its default logic
        public readonly Dictionary<int, (byte[] Bytes, int AnimId)> Lowers = [];
        public readonly StringBuilder Manifest = new("# low\thigh\tbattle machine groups (main first)\tmachines\tanim set key\tDS1 think\tbattle goal\n");
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Pack, State> states = new();

    /// <summary>The 1:1 build that drives EnemyParam <paramref name="enemyParam"/> (null: the variant is an interim template port, DS2 AI).</summary>
    public static ChrPorter.OneToOne? RigOf(int enemyParam) =>
        ChrPorter.UseDs1Rig ? ChrPorter.All.SelectMany(o => o.Rigs).FirstOrDefault(r => r.EnemyParams.Contains(enemyParam)) : null;

    /// <summary>DS1's think for a variant without a placement in this map (test placements): the first MSB part of the map
    /// with that NpcParam (any entity group), else null.</summary>
    public static int? DefaultThink(string dsrRoot, string ds1Map, int npcParam)
    {
        var msb = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", ds1Map + ".msb"));
        var e = msb.Parts.Enemies.FirstOrDefault(x => x.NPCParamID == npcParam && x.ThinkParamID > 0);
        return e?.ThinkParamID;
    }

    /// <summary>Translate (once per think and anim set) and emit; returns the logic ids for the generator, or null when the
    /// character keeps the DS2 template AI (no 1:1 build, --no-ds1-ai, no think).</summary>
    public static (int High, int Low)? Logic(Pack pack, string dsrRoot, string ds1Map, int enemyParam, int think)
    {
        if (!ChrPorter.UseDs1Ai || think <= 0) return null;
        var rig = RigOf(enemyParam);
        if (rig == null) return null;
        var st = states.GetOrCreateValue(pack);
        if (st.Done.TryGetValue((think, rig.AnimId), out var done)) return done;
        var thinkRow = Ds1Source.LoadParam(dsrRoot, "NpcThinkParam")?[think] ?? throw new InvalidDataException($"DS1 NpcThinkParam {think} missing");
        var res = AiTranslator.Translate(thinkRow, AiTranslator.MapOf(rig.Attacks), TemplateHigh, TemplateLow, Path.Combine(pack.Ds2Data, "ezstate"), null, ds1Map[..6]);
        string key = $"c{rig.AnimId / 100:D4}";
        // one lower per think: a second anim set on the same think must produce the same machine (same action numbers)
        if (st.Lowers.TryGetValue(res.Low, out var prev))
        {
            if (!prev.Bytes.AsSpan().SequenceEqual(res.LowerEsd))
                throw new InvalidDataException($"DS1 think {think} translates differently for anim sets c{prev.AnimId / 100:D4} and {key} (different action numbers): lower id ai{res.Low:D6} cannot serve both");
        }
        else
        {
            st.Lowers[res.Low] = (res.LowerEsd, rig.AnimId);
            pack.AddFile("gamedata", $"ezstate/ai{res.Low:D6}.esd", res.LowerEsd);
            st.Manifest.Append($"{res.Low}\t{res.High}\t{string.Join(",", res.BattleGroups)}\t{string.Join(",", res.Machines.Values)}\t{key}\t{think}\t{res.BattleGoal}\n");
            pack.AddFile("lordran-ai", "manifest.txt", Encoding.UTF8.GetBytes(st.Manifest.ToString()));
        }
        if (pack.TryGetFile("gamedata", $"ezstate/ai{res.High:D6}.esd") == null)
        {
            pack.AddFile("gamedata", $"ezstate/ai{res.High:D6}.esd", res.UpperEsd);
            var lcp = pack.Param("LogicComParam");
            if (lcp[res.High] != null) throw new InvalidDataException($"LogicComParam {res.High} already exists (vanilla id?)");
            var row = pack.AddRow("LogicComParam", lcp[TemplateHigh]!, res.High, $"[Lordran] DS1 think {think} (battle goal {res.BattleGoal}) on {TemplateHigh}");
            var notes = new List<string>();
            AiTranslator.LogicRow(row, thinkRow, notes);
            foreach (var n in notes) pack.Note($"DS1 AI: {n}");
        }
        // EnemyDefaultLogicParam of the anim set (cloned from the template's by ChrPorter): the same pair
        // session 11 (burg11, round 7) rule DL1: a later host map (the Depths) never re-points the default pair a former host
        // map (the Burg) set, so adding a map leaves the maps built before it unchanged
        // (the pre-loop character port, owner "", counts as a former map)
        bool ownedElsewhere = CurrentMap != null && st.DefaultOwner.TryGetValue(rig.AnimId, out var owner) && owner != CurrentMap;
        if (!ownedElsewhere) st.DefaultOwner[rig.AnimId] = CurrentMap ?? "";
        if (!ownedElsewhere && pack.HasParam("EnemyDefaultLogicParam") && pack.Param("EnemyDefaultLogicParam")[rig.AnimId] is { } dl)
        {
            dl["upperLogickId"].Value = Convert.ChangeType(res.High, dl["upperLogickId"].Value.GetType());
            dl["lowerLogickId"].Value = Convert.ChangeType(res.Low, dl["lowerLogickId"].Value.GetType());
        }
        pack.AddFile("lordran-ai", $"coverage-{think}.txt", Encoding.UTF8.GetBytes(AiTranslator.CoverageText(res)));
        pack.Note($"DS1 AI: {res.Report[0]}");
        foreach (var l in res.Report.Skip(1).Where(l => l.StartsWith("template patch") || l.Contains("(DS1 battle goal)"))) pack.Note($"DS1 AI {key}: {l}");
        Console.WriteLine($"  DS1 AI: think {think} → goal {res.BattleGoal} → ai{res.High:D6} / ai{res.Low:D6} ({key}: {res.Coverage.Paths} paths, {res.Coverage.States} states, actions [{string.Join(", ", res.Coverage.ActionsUsed.Order())}], combos [{string.Join(", ", res.Coverage.ChainsUsed.Order().Select(r => 50 + r))}])");
        st.Done[(think, rig.AnimId)] = (res.High, res.Low);
        return (res.High, res.Low);
    }
}
