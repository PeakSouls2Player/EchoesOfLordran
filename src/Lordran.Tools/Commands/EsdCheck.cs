using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// esd-check PACKDIR [MAP] — offline consistency checks of a built pack's event script for one map (default
/// m18_01_00_00), session 8 (the event translator's output can't be tested in game by the track that writes it):
///  - every EventParam row's stateMapId is a state group of event_MAP.esd, and owner rows (ownerType 1) name a
///    MapObjectInstanceParam row;
///  - no state 0 has entry commands (DS2 never runs them) and every group has at least 2 states;
///  - every condition's evaluator and every command argument decodes (ends in A1, known opcodes) and every target state
///    exists in its group;
///  - a state registers each group-condition slot (140xxx first argument) at most once, and every f140001(n) a state
///    reads is registered by that state;
///  - ids the commands name exist: EventLocation (140132, 132121, 131704, 131602 region), MapObjectInstanceParam
///    (130702, 131636, 131638, 140205, 131613, 131652, 131411, 140202, f131653), GeneratorParam (131721, 131731, 131704,
///    140111, 140121), HitGroupParam (132153), BossBattleParam (132201/132202/132204, 140522/140523, f132201),
///    ItemLotParam2_Other (131402), mapevent.fmg (130443); ItemParam for 131602's key item;
///  - fix pass (session 8): 132125's target point exists in the target map's EventLocation (a cross-map warp: the
///    nest → Firelink 3701121); 131201/131202 name a Sound region's UnkT08 in the map's MSB; a state that registers group
///    conditions reads their slots (and takes its fallback) only after the translator's settle time (f3() > s), so no
///    slot is read in the frame it is registered; the pack's own KeyGuideParam rows (the helpers' prompts) name a
///    keyguide.fmg text.
///  - session 8 (track npc, step 2): talk ESDs. EventParam rows with resourceType 1 start groups of talk_MAP.esd
///    (not event_MAP.esd) and their character owner (ownerType 2) is a GeneratorParam row; every talk group passes the
///    same state/expression checks; 132001 names an NpcTalkParam row whose chain (bNextPlay) stays inside the param and
///    whose msgIds have talk FMG lines; 130408 a KeyGuideParam row (with a keyguide.fmg text); 130461 an NpcEventParam
///    row (npcmenu.fmg name; with bShop at least one ShopLineupParam row in its range, each selling an existing item);
///    131402 an ItemLotParam2_Other row; 130441/130443 a mapevent text. Fix pass (review): every talk group reaches a
///    state that creates the talk prompt (130408) from state 0; a talk owner generator spawns at load like vanilla NPC
///    owners (start condition 0, or 8 on its dead flag = its respawnDisableFlag), not on approach; area-variable uses
///    (130121 / f130121, unverified on our maps) are counted.
/// Prints every problem and a summary; exit code 1 when anything failed.
/// </summary>
public static class EsdCheck
{
    public static int Run(string[] args)
    {
        string pack = args[1], map = args.Length > 2 ? args[2] : "m18_01_00_00";
        string Gd(string p) => Path.Combine(pack, "gamedata", p.Replace('/', Path.DirectorySeparatorChar));
        var defs = Directory.GetFiles(Path.Combine(ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        PARAM? MapParam(string name)
        {
            string f = Gd($"param/{name}_{map}.param");
            if (!File.Exists(f)) return null;
            PARAM p;
            try { p = PARAM.Read(f); }
            catch (EndOfStreamException) { return null; } // a row-less param (SoulsFormats can't read those back): no ids
            if (p.Rows.Count == 0) return null;
            try { if (p.ApplyParamdefCarefully(defs)) return p; } catch (InvalidDataException) { }
            return PARAM.Read(f); // raw (GeneratorLocation/Param): row ids are enough
        }
        HashSet<int> Ids(PARAM? p) => p?.Rows.Select(r => r.ID).ToHashSet() ?? [];

        var esd = ESD.Read(Gd($"ezstate/event_{map}.esd"));
        var events = MapParam("eventparam") ?? throw new FileNotFoundException("no EventParam");
        var locations = Ids(MapParam("eventlocation"));
        var instances = Ids(MapParam("mapobjectinstanceparam"));
        var generators = Ids(MapParam("generatorparam"));
        var hitGroups = Ids(MapParam("hitgroupparam"));
        var reg = ParamStore.LoadDs2(Path.Combine(pack, "title"), ExtractDs2.DefaultOut);
        var bosses = Ids(reg.Params.GetValueOrDefault("BossBattleParam"));
        var lots = Ids(reg.Params.GetValueOrDefault("ItemLotParam2_Other"));
        var items = Ids(reg.Params.GetValueOrDefault("ItemParam"));
        string fmgPath = Gd("menu/text/english/mapevent.fmg");
        var messages = (File.Exists(fmgPath) ? FMG.Read(fmgPath) : FMG.Read(Path.Combine(ExtractDs2.DefaultOut, "menu", "text", "english", "mapevent.fmg")))
            .Entries.Where(e => !string.IsNullOrEmpty(e.Text)).Select(e => e.ID).ToHashSet();
        // a cross-map warp's target: the other map's EventLocation (decimal map id AABBCCDD → mAA_BB_CC_DD)
        var otherLocations = new Dictionary<int, HashSet<int>>();
        HashSet<int> LocationsOf(int mapId)
        {
            if (otherLocations.TryGetValue(mapId, out var s)) return s;
            string m = $"m{mapId / 1000000:D2}_{mapId / 10000 % 100:D2}_{mapId / 100 % 100:D2}_{mapId % 100:D2}", f = Gd($"param/eventlocation_{m}.param");
            s = File.Exists(f) ? PARAM.Read(f).Rows.Select(r => r.ID).ToHashSet() : [];
            return otherLocations[mapId] = s;
        }
        string msbPath = Gd($"map/{map}/{map}.msb");
        var soundIds = File.Exists(msbPath) ? MSB2.Read(msbPath).Regions.Sounds.Where(r => r.UnkT08 != 0).Select(r => r.UnkT08).ToHashSet() : [];
        int guarded = 0, entryTests = 0;

        var problems = new List<string>();
        int conds = 0, cmds = 0, refs = 0;
        foreach (var row in events.Rows)
        {
            long gid = Convert.ToInt64(row["stateMapId"].Value);
            if (Convert.ToInt32(row["resourceType"].Value) == 1) continue; // talk rows: TalkChecks
            if (!esd.StateGroups.ContainsKey(gid)) problems.Add($"EventParam {row.ID}: stateMapId {gid} is not a group of event_{map}.esd");
            if (Convert.ToInt32(row["ownerType"].Value) == 1 && !instances.Contains(Convert.ToInt32(row["ownerId"].Value)))
                problems.Add($"EventParam {row.ID}: owner object {row["ownerId"].Value} is not in MapObjectInstanceParam");
        }
        foreach (var (gid, states) in esd.StateGroups)
        {
            if (states.Count < 2) problems.Add($"group {gid}: {states.Count} state(s)");
            if (states.TryGetValue(0, out var s0) && s0.EntryCommands.Count > 0) problems.Add($"group {gid}: state 0 has entry commands");
            if (!events.Rows.Any(r => Convert.ToInt64(r["stateMapId"].Value) == gid && Convert.ToInt32(r["resourceType"].Value) != 1) && gid < 2_000_000_000)
                problems.Add($"group {gid}: no EventParam row starts it (not a subroutine id)");
            foreach (var (sid, st) in states)
            {
                string at = $"group {gid} state {sid}";
                var registered = new List<int>();
                foreach (var c in st.EntryCommands.Concat(st.WhileCommands).Concat(st.ExitCommands))
                {
                    cmds++;
                    var a = new List<int?>();
                    foreach (var arg in c.Arguments)
                    {
                        if (!Decodes(arg)) problems.Add($"{at}: {c.CommandID} argument doesn't decode [{Convert.ToHexString(arg)}]");
                        a.Add(Const(arg));
                    }
                    int? A(int k) => k < a.Count ? a[k] : null;
                    if (c.CommandID is >= 140000 and < 150000 && A(0) is int slot) registered.Add(slot);
                    void Need(HashSet<int> set, int k, string what)
                    {
                        if (A(k) is not int v) return; // a parameter (subroutine) or expression: not checkable here
                        refs++;
                        if (!set.Contains(v)) problems.Add($"{at}: {c.CommandID} names {what} {v}, which doesn't exist");
                    }
                    switch (c.CommandID)
                    {
                        case 140132: Need(locations, 1, "EventLocation"); Need(locations, 2, "EventLocation"); break;
                        case 132121: Need(locations, 0, "EventLocation"); break;
                        case 131704: Need(generators, 0, "generator"); Need(locations, 1, "EventLocation"); break;
                        // session 10 (rule K3): the opened text may be 0 (no message: vanilla 【Lib】領域指定扉開錠 passes 0)
                        case 131602: if (A(0) is not 0) Need(locations, 0, "EventLocation"); if (A(1) is not 0) Need(items, 1, "item"); if (A(2) is not 0) Need(messages, 2, "mapevent text"); Need(messages, 3, "mapevent text"); if (A(5) is not (0 or null)) Need(items, 5, "item"); break;
                        case 140801: Need(items, 1, "item"); break; // session 10 (rule K4/K6): item possession
                        case 130702 or 131636 or 131638 or 131613 or 131652 or 131411: Need(instances, 0, "object instance"); break;
                        case 140205 or 140202: Need(instances, 1, "object instance"); break;
                        case 131721 or 131731: Need(generators, 0, "generator"); break;
                        case 140111 or 140121: Need(generators, 1, "generator"); break;
                        case 132153: Need(hitGroups, 0, "hit group"); break;
                        case 132201 or 132202 or 132204: Need(bosses, 0, "BossBattleParam row"); break;
                        case 140522 or 140523: Need(bosses, 1, "BossBattleParam row"); break;
                        case 131201 or 131202: Need(soundIds, 0, "Sound region (UnkT08)"); break;
                        case 132125 when A(2) is int target: Need(LocationsOf(target), 3, $"EventLocation of map {target}"); break;
                        case 131402: Need(lots, 0, "ItemLotParam2_Other row"); break;
                        case 130443: Need(messages, 0, "mapevent text"); if (A(4) == 2 && A(5) is not (0 or null)) Need(items, 5, "item (the %s of the text)"); break; // session 10, rule K7
                        // session 10 (asy-items): the origin prompt's YES/NO window; rule M2's event blood letters
                        // (fix pass) argument 4 = the text's parameter: 2 = %s item name of argument 5, 1 = %d, 0 = none;
                        // vanilla never passes 2 with item 0 (texts without a parameter use 0 with 0: V-data)
                        case 130441:
                            Need(messages, 0, "mapevent text");
                            if (A(3) is 2 && A(4) is 0) problems.Add($"{at}: 130441 fills %s with item 0 (argument 4 = 2, argument 5 = 0); a text without a parameter uses argument 4 = 0");
                            break;
                        case 132221: Need(locations, 0, "EventLocation (blood message)"); break;
                    }
                }
                if (registered.Count != registered.Distinct().Count()) problems.Add($"{at}: a group-condition slot is registered twice ({string.Join(",", registered)})");
                foreach (var cond in Flatten(st.Conditions))
                {
                    conds++;
                    if (!Decodes(cond.Evaluator)) problems.Add($"{at}: condition doesn't decode [{Convert.ToHexString(cond.Evaluator)}]");
                    if (cond.TargetState is long t && !states.ContainsKey(t)) problems.Add($"{at}: condition targets missing state {t}");
                    foreach (int n in GroupReads(cond.Evaluator).Where(n => !registered.Contains(n)))
                        problems.Add($"{at}: reads f140001({n}) without registering slot {n}");
                    // settle time: in a registering state, slot reads and the plain fallback wait for f3() > s
                    // rule G1 (track nestfix): except in a test (its last condition is `if 1`), which resolves in its entry
                    // iteration: f140001 evaluates a slot on demand, and DS2 never steps a registering state again unless
                    // one of its slots holds (EventSim / event-sim lint)
                    bool entryTest = st.Conditions.Count > 0 && st.Conditions[^1].Evaluator.SequenceEqual(new byte[] { 0x41, 0xA1 });
                    if (registered.Count > 0 && gid is >= 37000 and < 40000 && (GroupReads(cond.Evaluator).Any() || cond.Evaluator.SequenceEqual(new byte[] { 0x41, 0xA1 })))
                    {
                        if (ReadsStateTime(cond.Evaluator)) guarded++;
                        else if (entryTest) entryTests++;
                        else if (gid >= 38000) problems.Add($"{at}: reads a group slot (or falls back) in the frame it registers it (no f3() settle guard)");
                    }
                    foreach (int b in Calls(cond.Evaluator, 132201)) { refs++; if (!bosses.Contains(b)) problems.Add($"{at}: f132201 names BossBattleParam {b}, which doesn't exist"); }
                    foreach (var (inst, _) in ObjStateReads(cond.Evaluator)) { refs++; if (!instances.Contains(inst)) problems.Add($"{at}: f131653 names object {inst}, which doesn't exist"); }
                    foreach (var p in cond.PassCommands) { cmds++; foreach (var arg in p.Arguments) if (!Decodes(arg)) problems.Add($"{at}: pass {p.CommandID} argument doesn't decode"); }
                }
            }
        }
        // the translator's regulation rows (BossBattleParam rows vanilla doesn't have; event item lots ≥ 1e9: EntityRules)
        var vanilla = ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        string kgPath = Gd("menu/text/english/keyguide.fmg");
        var keyGuideTexts = (File.Exists(kgPath) ? FMG.Read(kgPath) : FMG.Read(Path.Combine(ExtractDs2.DefaultOut, "menu", "text", "english", "keyguide.fmg")))
            .Entries.Where(e => !string.IsNullOrEmpty(e.Text)).ToDictionary(e => e.ID, e => e.Text);
        foreach (var name in new[] { "BossBattleParam", "ItemLotParam2_Other", "KeyGuideParam" })
            if (reg.Params.GetValueOrDefault(name) is { } p && vanilla.Params.GetValueOrDefault(name) is { } v)
            {
                var vIds = v.Rows.Select(r => r.ID).ToHashSet();
                foreach (var r in p.Rows.Where(r => !vIds.Contains(r.ID) && (name != "ItemLotParam2_Other" || r.ID >= 1_000_000_000)))
                {
                    Console.WriteLine($"  row {name} {r.ID}: " + string.Join(" ", r.Cells.Where(c => c.Def.DisplayType != PARAMDEF.DefType.dummy8 && !(c.Value is 0 or 0u or (byte)0 or (ushort)0 or 0f))
                        .Take(24).Select(c => $"{c.Def.InternalName}={c.Value}")));
                    // key guides (the hall door's, the action-button areas'): the prompt text exists
                    if (name == "KeyGuideParam")
                    {
                        int t = Convert.ToInt32(r["textId"].Value);
                        refs++;
                        if (!keyGuideTexts.TryGetValue(t, out var text)) problems.Add($"KeyGuideParam {r.ID}: textId {t} is not in keyguide.fmg");
                        else Console.WriteLine($"    text {t}: \"{text}\"");
                    }
                }
            }
        // rule G1 (track nestfix): transitions DS2's group-condition gate never steps, in the translated groups (warnings:
        // the open ones are other tracks' flag/NPC waits, NOTES of track nestfix)
        var gate = Build.Events.EventSim.Lint(esd.StateGroups, esd.StateGroups.Keys.Where(g => g is >= 38000 and < 40000));
        foreach (var w in gate) Console.WriteLine("  WARN " + w);
        if (entryTests > 0 || gate.Count > 0) Console.WriteLine($"  rule G1: {entryTests} test read(s)/fallback(s) resolved in the entry iteration, {gate.Count} gate warning(s)");
        // rule G2 (track nestfix fix pass): a registering state re-entered within one run replays its first exit (vanilla: 0)
        problems.AddRange(Build.Events.EventSim.Revisits(esd.StateGroups));
        TalkChecks(map, Gd, MapParam, events, generators, reg, lots, messages, keyGuideTexts, problems);
        TalkSimChecks(pack, map, Gd, reg, problems);
        foreach (var p in problems) Console.WriteLine("  FAIL " + p);
        Console.WriteLine($"esd-check {map}: {esd.StateGroups.Count} groups, {esd.StateGroups.Sum(g => g.Value.Count)} states, {events.Rows.Count} EventParam rows, {cmds} commands, {conds} conditions ({guarded} slot reads/fallbacks behind the settle time), {refs} id references checked: {problems.Count} problem(s)");
        return problems.Count == 0 ? 0 : 1;
    }

    static IEnumerable<ESD.Condition> Flatten(IEnumerable<ESD.Condition> cs) => cs.SelectMany(c => new[] { c }.Concat(Flatten(c.Subconditions)));

    /// <summary>The map's talk ESD and what its groups name (session 8, track npc, step 2).</summary>
    static void TalkChecks(string map, Func<string, string> Gd, Func<string, PARAM?> MapParam, PARAM events, HashSet<int> generators,
        ParamStore reg, HashSet<int> lots, HashSet<int> messages, Dictionary<int, string> keyGuideTexts, List<string> problems)
    {
        string talkPath = Gd($"ezstate/talk_{map}.esd");
        if (!File.Exists(talkPath)) return;
        var talk = ESD.Read(talkPath);
        var talkRows = MapParam("npctalkparam")?.Rows.OrderBy(r => r.ID).ToList() ?? [];
        string fmgPath = Gd($"menu/text/english/talk/{map}.fmg");
        var lines = File.Exists(fmgPath) ? FMG.Read(fmgPath).Entries.Where(e => !string.IsNullOrEmpty(e.Text)).Select(e => e.ID).ToHashSet() : [];
        string menuPath = Gd("menu/text/english/npcmenu.fmg");
        var menuNames = (File.Exists(menuPath) ? FMG.Read(menuPath) : FMG.Read(Path.Combine(ExtractDs2.DefaultOut, "menu", "text", "english", "npcmenu.fmg")))
            .Entries.Where(e => !string.IsNullOrEmpty(e.Text)).Select(e => e.ID).ToHashSet();
        var windows = reg.Params.GetValueOrDefault("NpcEventParam");
        var shops = reg.Params.GetValueOrDefault("ShopLineupParam");
        var items = reg.Params.GetValueOrDefault("ItemParam");
        var keyGuides = reg.Params.GetValueOrDefault("KeyGuideParam");
        int groups = 0, states = 0, refs = 0, areaVariables = 0;
        // generator start conditions (fix pass, review): a talk owner must exist from the map load like vanilla NPC owners
        // (Saulden 100: condition 8 = his dead flag off; always-present 0), not spawn on approach (TB 1600's condition 1)
        var genRaw = new Dictionary<int, byte[]>();
        string genPath = Gd($"param/generatorparam_{map}.param");
        if (File.Exists(genPath) && !Formats.ParamFiles.Rowless(genPath)) // session 11: a map without characters has none
        {
            var gp = PARAM.Read(genPath);
            var blob = new PARAMDEF { ParamType = gp.ParamType, DataVersion = gp.ParamdefDataVersion, Unicode = true, FormatVersion = 104, BigEndian = false };
            blob.Fields.Add(new PARAMDEF.Field(blob, PARAMDEF.DefType.dummy8, "data") { ArrayLength = (int)gp.DetectedSize });
            gp.ApplyParamdef(blob);
            foreach (var r in gp.Rows) genRaw[r.ID] = (byte[])r["data"].Value;
        }
        var owners = new List<string>();
        foreach (var row in events.Rows.Where(r => Convert.ToInt32(r["resourceType"].Value) == 1))
        {
            long gid = Convert.ToInt64(row["stateMapId"].Value);
            if (!talk.StateGroups.ContainsKey(gid)) problems.Add($"EventParam {row.ID}: talk group {gid} is not in talk_{map}.esd");
            int owner = Convert.ToInt32(row["ownerId"].Value);
            if (Convert.ToInt32(row["ownerType"].Value) == 2 && !generators.Contains(owner))
                problems.Add($"EventParam {row.ID}: owner generator {owner} is not in GeneratorParam");
            if (Convert.ToInt32(row["ownerType"].Value) == 2 && genRaw.TryGetValue(owner, out var g) && g.Length >= 0x1C)
            {
                byte cond = g[0x0D];
                int flag = BitConverter.ToInt32(g, 0x10), respawnOff = BitConverter.ToInt32(g, 0x04);
                if (cond is not (0 or 8)) problems.Add($"EventParam {row.ID}: owner generator {owner} start condition {cond} (param {flag}): vanilla NPC owners spawn at load (0, or 8 = dead flag off)");
                if (cond == 8 && respawnOff != flag) problems.Add($"EventParam {row.ID}: owner generator {owner} starts on flag {flag} but its respawnDisableFlag is {respawnOff}");
                owners.Add($"{owner}: start {cond}{(cond == 8 ? $" on {flag}" : "")}, respawnDisableFlag {respawnOff}");
            }
        }
        foreach (var (gid, st) in talk.StateGroups)
        {
            if (gid == 0x7FFFFFFF) continue; // MapBuilder's placeholder machine (no NPC talks on the map)
            groups++;
            if (st.Count < 2) problems.Add($"talk group {gid}: {st.Count} state(s)");
            if (st.TryGetValue(0, out var s0) && s0.EntryCommands.Count > 0) problems.Add($"talk group {gid}: state 0 has entry commands");
            if (gid < 2_000_000_000 && !events.Rows.Any(r => Convert.ToInt64(r["stateMapId"].Value) == gid && Convert.ToInt32(r["resourceType"].Value) == 1))
                problems.Add($"talk group {gid}: no EventParam talk row starts it");
            // the talk prompt (130408) must be reachable from state 0 (fix pass, review: the merchant's wasn't)
            if (gid < 2_000_000_000 && !Build.Talk.TalkTranslator.PromptReach(st).Prompt)
                problems.Add($"talk group {gid}: no state reachable from state 0 creates the talk prompt (130408)");
            // runtime stores on our maps: area variables (130121 / f130121) are unverified there, our talks use none
            areaVariables += st.Values.Sum(s => s.EntryCommands.Count(c => c.CommandID == 130121)
                + Flatten(s.Conditions).Count(c => Esd.Expr(c.Evaluator).Contains("f130121(")));
            foreach (var (sid, s) in st)
            {
                states++;
                string at = $"talk group {gid} state {sid}";
                foreach (var cond in Flatten(s.Conditions))
                {
                    if (!Decodes(cond.Evaluator)) problems.Add($"{at}: condition doesn't decode");
                    if (cond.TargetState is long t && !st.ContainsKey(t)) problems.Add($"{at}: condition targets missing state {t}");
                    foreach (var p in cond.PassCommands) foreach (var arg in p.Arguments) if (!Decodes(arg)) problems.Add($"{at}: pass {p.CommandID} argument doesn't decode");
                }
                foreach (var c in s.EntryCommands.Concat(s.WhileCommands).Concat(s.ExitCommands))
                {
                    foreach (var arg in c.Arguments) if (!Decodes(arg)) problems.Add($"{at}: {c.CommandID} argument doesn't decode");
                    // session 11 (cov11): a conditional command (bank 5: the condition first, e.g. the covenant window variants
                    // 5:130461(cond, …)) has its arguments one further on
                    int shift = c.CommandBank == 5 ? 1 : 0;
                    int? A(int k) => k + shift < c.Arguments.Count ? Const(c.Arguments[k + shift]) : null;
                    switch (c.CommandID)
                    {
                        case 132001 when A(0) is int t:
                        {
                            refs++;
                            int at0 = talkRows.FindIndex(r => r.ID == t);
                            if (at0 < 0) { problems.Add($"{at}: 132001 names NpcTalkParam {t}, which doesn't exist"); break; }
                            for (int k = at0; k < talkRows.Count; k++)
                            {
                                int msg = Convert.ToInt32(talkRows[k]["msgId"].Value);
                                if (!lines.Contains(msg)) problems.Add($"{at}: NpcTalkParam {talkRows[k].ID} msgId {msg} has no talk FMG line");
                                if (Convert.ToInt32(talkRows[k]["bNextPlay"].Value) == 0) break;
                                if (k + 1 == talkRows.Count) problems.Add($"{at}: NpcTalkParam {talkRows[k].ID} continues (bNextPlay) past the last row");
                            }
                            break;
                        }
                        case 130408 when A(1) is int kg:
                            refs++;
                            if (keyGuides?[kg] is not { } kr) problems.Add($"{at}: 130408 names KeyGuideParam {kg}, which doesn't exist");
                            else if (!keyGuideTexts.ContainsKey(Convert.ToInt32(kr["textId"].Value))) problems.Add($"{at}: KeyGuideParam {kg} textId {kr["textId"].Value} is not in keyguide.fmg");
                            break;
                        case 130461 when A(3) is int w:
                        {
                            refs++;
                            if (windows?[w] is not { } wr) { problems.Add($"{at}: 130461 names NpcEventParam {w}, which doesn't exist"); break; }
                            if (!menuNames.Contains(Convert.ToInt32(wr["npcId"].Value))) problems.Add($"{at}: NpcEventParam {w} npcId {wr["npcId"].Value} has no npcmenu.fmg name");
                            if (Convert.ToInt32(wr["bShop"].Value) == 1)
                            {
                                long a = Convert.ToInt64(wr["shopIdBegin"].Value), b = Convert.ToInt64(wr["shopIdEnd"].Value);
                                var sold = shops?.Rows.Where(r => r.ID >= a && r.ID <= b).ToList() ?? [];
                                if (sold.Count == 0) problems.Add($"{at}: NpcEventParam {w} shop {a}..{b} has no ShopLineupParam rows");
                                foreach (var r in sold.Where(r => items?[Convert.ToInt32(r["saleItem"].Value)] == null)) problems.Add($"ShopLineupParam {r.ID}: item {r["saleItem"].Value} doesn't exist");
                            }
                            break;
                        }
                        case 131402 when A(0) is int lot:
                            refs++;
                            if (!lots.Contains(lot)) problems.Add($"{at}: 131402 names ItemLotParam2_Other {lot}, which doesn't exist");
                            break;
                        case 130441 or 130443 when A(0) is int msg2:
                            refs++;
                            if (!messages.Contains(msg2)) problems.Add($"{at}: {c.CommandID} names mapevent {msg2}, which has no text");
                            if (c.CommandID == 130441 && A(3) is 2 && A(4) is 0) problems.Add($"{at}: 130441 fills %s with item 0 (argument 4 = 2, argument 5 = 0)");
                            break;
                    }
                }
            }
        }
        Console.WriteLine($"esd-check {map} talk: {groups} talk groups, {states} states, {talkRows.Count} NpcTalkParam rows, {lines.Count} talk lines, {refs} talk references checked, prompt reachable in every group unless listed, area-variable uses {areaVariables}");
        foreach (string o in owners) Console.WriteLine($"  talk owner generator {o}");
    }

    /// <summary>
    /// Session 9 (track npctalk): every talk group run frame by frame in the talk simulator (Build/Talk/TalkSim: DS2's up
    /// to 100 transitions per frame, stopping at the first iteration without one; DS2's comparison opcodes) — "rest" at
    /// the NPC, "far", one "hit", a "death", a full "talk".
    /// Fails on a within-frame cycle, a prompt that isn't held at rest, a hit reaction with nobody hitting, or more
    /// reactions than hits (the run s9-2 failures: Oscar's 14-per-frame prompt cycle, the Firelink hit lines at rest).
    /// </summary>
    static void TalkSimChecks(string pack, string map, Func<string, string> Gd, ParamStore reg, List<string> problems)
    {
        if (!File.Exists(Gd($"ezstate/talk_{map}.esd"))) return;
        var (talk, template) = Build.Talk.TalkSim.LoadPack(pack, map, reg);
        int groups = 0, before = problems.Count;
        foreach (var (gid, group) in talk.StateGroups.OrderBy(g => g.Key))
        {
            if (gid >= 2_000_000_000) continue; // MapBuilder's placeholder machine, subroutines
            groups++;
            var info = new List<string>();
            problems.AddRange(Build.Talk.TalkSim.Check(gid, group, template, info));
            foreach (var line in info.Where(l => !l.Contains("not modelled"))) Console.WriteLine("  sim " + line);
        }
        Console.WriteLine($"esd-check {map} talk-sim: {groups} talk group(s) × 5 scenarios, {problems.Count - before} problem(s)");
    }

    /// <summary>Walks ESD expression bytecode: true when every opcode is known and it ends with A1.</summary>
    static bool Decodes(byte[] b)
    {
        for (int i = 0; i < b.Length; i++)
        {
            byte op = b[i];
            if (op < 0x80) continue;
            switch (op)
            {
                case 0x80: i += 4; break;
                case 0x81: i += 8; break;
                case 0x82: i += 4; break;
                case >= 0x84 and <= 0x8A: break;
                case >= 0x8C and <= 0x9A: break;
                case 0xA1: return i == b.Length - 1;
                case 0xA5: // string
                    while (i + 2 < b.Length && (b[i + 1] != 0 || b[i + 2] != 0)) i += 2;
                    i += 2; break;
                case 0xA6: break;
                case >= 0xA7 and <= 0xB8: break; // registers r0-r7 set/get, abort, parameter (Esd.Expr's table)
                case 0xB9 or 0xBA: break;        // subroutine call results (Things Betwixt's own bonfire group 80000 uses B9 BA)
                default: return false;
            }
        }
        return false;
    }

    /// <summary>A plain constant argument (push + A1), or null for expressions/parameters.</summary>
    static int? Const(byte[] b)
    {
        if (b.Length == 2 && b[0] < 0x80 && b[1] == 0xA1) return b[0] - 0x40;
        if (b.Length == 6 && b[0] == 0x82 && b[5] == 0xA1) return BitConverter.ToInt32(b, 1);
        return null;
    }

    /// <summary>n of every f140001(n) in an evaluator (pattern 82 E1220200 4n 85).</summary>
    static IEnumerable<int> GroupReads(byte[] b)
    {
        for (int i = 0; i + 6 < b.Length; i++)
            if (b[i] == 0x82 && BitConverter.ToInt32(b, i + 1) == 140001 && b[i + 5] < 0x80 && b[i + 6] == 0x85) yield return b[i + 5] - 0x40;
    }

    /// <summary>The evaluator contains a state-time test (f3(): push 3, call with 0 arguments).</summary>
    static bool ReadsStateTime(byte[] b)
    {
        for (int i = 0; i + 1 < b.Length; i++)
        {
            if (b[i] == 0x80 || b[i] == 0x82) { i += 4; continue; } // skip literals
            if (b[i] == 0x81) { i += 8; continue; }
            if (b[i] == 0x43 && b[i + 1] == 0x84) return true;
        }
        return false;
    }

    /// <summary>The constant first argument of every call of direct function <paramref name="id"/> with one argument.</summary>
    static IEnumerable<int> Calls(byte[] b, int id)
    {
        for (int i = 0; i + 5 < b.Length; i++)
        {
            if (b[i] != 0x82 || BitConverter.ToInt32(b, i + 1) != id) continue;
            int j = i + 5;
            if (b[j] < 0x80 && j + 1 < b.Length && b[j + 1] == 0x85) yield return b[j] - 0x40;
            else if (b[j] == 0x82 && j + 5 < b.Length && b[j + 5] == 0x85) yield return BitConverter.ToInt32(b, j + 1);
        }
    }

    /// <summary>(instance, state) of every f131653(instance, state, 0) with constant arguments.</summary>
    static IEnumerable<(int Inst, int State)> ObjStateReads(byte[] b)
    {
        for (int i = 0; i + 5 < b.Length; i++)
        {
            if (b[i] != 0x82 || BitConverter.ToInt32(b, i + 1) != 131653) continue;
            int j = i + 5;
            int? Read() { if (j >= b.Length) return null; if (b[j] < 0x80) return b[j++] - 0x40; if (b[j] == 0x82 && j + 4 < b.Length) { int v = BitConverter.ToInt32(b, j + 1); j += 5; return v; } return null; }
            if (Read() is int inst && Read() is int st) yield return (inst, st);
        }
    }
}
