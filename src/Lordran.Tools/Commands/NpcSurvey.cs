using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// npc-survey [--all] — how DS2 builds its human NPCs (session 8, track npc). For every vanilla map's
/// GeneratorRegistParam row whose EnemyParam uses model 1 (c0001 = the player body + equipment parts; non-human
/// NPCs use their own cNNNN model): the generator(s) that use it, its position, the talk/event groups attached to the
/// generator (EventParam ownerType 2 = character, ownerId = GeneratorParam/Location id; resourceType 1 = talk ESD
/// group, 0 = event ESD group), the regist row (logic ids = ai ESDs, startUpStateType = the AI's idle/start state),
/// EnemyParam (animId, npcStatusId), NPCGenerateParam (row = EnemyParam id: chrId, equipment, face, skin) and
/// NpcPlayerStatusParam (row = npcStatusId). Ends with a startUpStateType census. --all also lists non-human NPCs
/// (EnemyParam categoryType 4).
/// </summary>
public static class NpcSurvey
{
    public static int Run(string[] args)
    {
        bool all = args.Contains("--all");
        var store = ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        var enemy = store.Params["EnemyParam"];
        var gen = store.Params.GetValueOrDefault("NPCGenerateParam");
        var nps = store.Params.GetValueOrDefault("NpcPlayerStatusParam");
        var census = new Dictionary<int, List<string>>();
        foreach (var (name, regist) in store.Params.Where(p => p.Key.StartsWith("generatorregistparam_", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Key))
        {
            string map = name["generatorregistparam_".Length..];
            var genParam = store.Params.GetValueOrDefault("generatorparam_" + map);
            var evParam = store.Params.GetValueOrDefault("eventparam_" + map);
            var locRaw = Path.Combine(ExtractDs2.DefaultOut, "param", $"generatorlocation_{map}.param");
            var loc = File.Exists(locRaw) ? PARAM.Read(locRaw) : null;
            if (loc != null)
            {
                var blob = new PARAMDEF { ParamType = loc.ParamType, DataVersion = loc.ParamdefDataVersion, Unicode = true, FormatVersion = 104, BigEndian = false };
                blob.Fields.Add(new PARAMDEF.Field(blob, PARAMDEF.DefType.dummy8, "data") { ArrayLength = (int)loc.DetectedSize });
                loc.ApplyParamdef(blob);
            }
            // generatorparam has no fitting def in most maps: read the regist id raw at +0x08
            var genRaw = Path.Combine(ExtractDs2.DefaultOut, "param", $"generatorparam_{map}.param");
            var gens = new List<(int Id, int Regist, byte[] Data)>();
            if (File.Exists(genRaw))
            {
                var g = PARAM.Read(genRaw);
                var blob = new PARAMDEF { ParamType = g.ParamType, DataVersion = g.ParamdefDataVersion, Unicode = true, FormatVersion = 104, BigEndian = false };
                blob.Fields.Add(new PARAMDEF.Field(blob, PARAMDEF.DefType.dummy8, "data") { ArrayLength = (int)g.DetectedSize });
                g.ApplyParamdef(blob);
                foreach (var r in g.Rows) { var d = (byte[])r["data"].Value; gens.Add((r.ID, BitConverter.ToInt32(d, 8), d)); }
            }
            foreach (var r in regist.Rows)
            {
                int eid = Convert.ToInt32(r["EnemyId"].Value);
                var e = enemy.Rows.FirstOrDefault(x => x.ID == eid);
                if (e == null) continue;
                int model = Convert.ToInt32(e["modelId"].Value), cat = Convert.ToInt32(e["categoryType"].Value);
                bool human = model == 1;
                if (!human && !(all && cat == 4)) continue;
                int startUp = Convert.ToInt32(r["startUpStateType"].Value);
                string ename = store.RowName("EnemyParam", eid);
                if (human) (census.TryGetValue(startUp, out var l) ? l : census[startUp] = new()).Add($"{ename} ({eid}, {map})");
                Console.WriteLine($"== {map} regist {r.ID}: EnemyParam {eid} '{ename}' model c{model:D4} animId {e["animId"].Value} npcStatusId {e["npcStatusId"].Value} category {cat} group {e["groupType"].Value}; logic {r["HighLogicId"].Value}/{r["LowLogicId"].Value} startUpStateType {startUp}");
                foreach (var g in gens.Where(g => g.Regist == r.ID))
                {
                    string pos = "";
                    var lr = loc?.Rows.FirstOrDefault(x => x.ID == g.Id);
                    if (lr != null)
                    {
                        var d = (byte[])lr["data"].Value;
                        pos = $" at ({BitConverter.ToSingle(d, 0):0.##}, {BitConverter.ToSingle(d, 4):0.##}, {BitConverter.ToSingle(d, 8):0.##}) yaw {BitConverter.ToSingle(d, 16):0.#} inherit {BitConverter.ToInt32(d, 28)}";
                    }
                    string gt = genParam != null && genParam.Rows.FirstOrDefault(x => x.ID == g.Id) is { } gr
                        ? $" respawnDisable {gr["respawnDisableFlag"].Value} start {gr["startConditionJudgeType"].Value}/{gr["startConditionCondition1"].Value}:{gr["startConditionParam1"].Value} bootLowStateId {gr["bootLowStateId"].Value} logicGroup {gr["logicGroupId"].Value} eventGroup {gr["eventGroupId"].Value} spGenerateType {gr["spGenerateType"].Value} endEventId {gr["endEventId"].Value}"
                        : $" (raw: byte0-3 {g.Data[0]}/{g.Data[1]}/{g.Data[2]}/{g.Data[3]})";
                    Console.WriteLine($"   generator {g.Id}{pos}{gt}");
                    if (evParam != null)
                        foreach (var ev in evParam.Rows.Where(x => Convert.ToInt32(x["ownerType"].Value) == 2 && Convert.ToInt32(x["ownerId"].Value) == g.Id))
                            Console.WriteLine($"     EventParam {ev.ID}: stateMapId {ev["stateMapId"].Value} resourceType {ev["resourceType"].Value} ({(Convert.ToInt32(ev["resourceType"].Value) == 1 ? "talk" : "event")}) bootType {ev["bootType"].Value} playerDead {ev["bPlayerDeadPlaying"].Value} demo {ev["bDemoPlaying"].Value}");
                }
                if (human && gen?.Rows.FirstOrDefault(x => x.ID == eid) is { } ng)
                    Console.WriteLine("   NPCGenerateParam: " + string.Join(" ", ng.Cells.Where(c => !c.Def.InternalName.StartsWith("dummy")).Select(c => $"{c.Def.InternalName}={Fmt(c.Value)}")));
                else if (human) Console.WriteLine("   NPCGenerateParam: (no row)");
                if (human && nps?.Rows.FirstOrDefault(x => x.ID == Convert.ToInt32(e["npcStatusId"].Value)) is { } ps)
                    Console.WriteLine("   NpcPlayerStatusParam: " + string.Join(" ", ps.Cells.Where(c => c.Def.InternalName is "genderType" or "faceType" or "faceId" or "bodyType" or "livingType"
                        || c.Def.InternalName.EndsWith("Armor") || c.Def.InternalName.Contains("Weapon")).Select(c => $"{c.Def.InternalName}={Fmt(c.Value)}")));
            }
        }
        Console.WriteLine("== startUpStateType census (human NPCs)");
        foreach (var (k, v) in census.OrderBy(x => x.Key))
            Console.WriteLine($"  {k,3}: {v.Count,3}  {string.Join("; ", v.Distinct().Take(12))}");
        return 0;
    }

    static string Fmt(object v) => v switch { float f => f.ToString("0.###"), byte[] b => Convert.ToHexString(b), _ => v?.ToString() ?? "" };
}
