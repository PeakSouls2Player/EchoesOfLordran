namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Session 11 (burg11, BURG-PLAN rule ST1): the stand-in ledger. Every fallback or left-out DS1 part of a MapObjects map
/// becomes one build-log line "ST1&lt;TAB&gt;map&lt;TAB&gt;kind&lt;TAB&gt;DS1 part&lt;TAB&gt;entity&lt;TAB&gt;why" (a pack folder holds only game
/// files, build.log and loader.ini, Pack.OutputFolderProblem); the DLC standard is 0 rows per finished area (BD1 border
/// rows excepted).
/// </summary>
public static class StandIns
{
    public static void Write(Pack pack, MapObjects plan)
    {
        foreach (var (kind, part, entity, why) in plan.StandIns)
            pack.Note($"ST1\t{plan.Ds2Map}\t{kind}\t{part}\t{entity}\t{why}");
        foreach (var w in plan.World.Where(w => w.Note.Contains("NOT baked")))
            pack.Note($"ST1\t{plan.Ds2Map}\tWE5 pose\t{w.Name}\t{w.Entity}\t{w.Note}");
        foreach (var l in plan.Log) pack.Note($"{plan.Ds2Map}: {l}");
        pack.Note($"ST1 {plan.Ds2Map}: {plan.StandIns.Count} stand-in rows ({string.Join(", ", plan.StandIns.GroupBy(s => s.Kind).Select(g => $"{g.Key} {g.Count()}"))})");
    }
}
