using System.Text.RegularExpressions;
using Lordran.Tools.Build;
using Lordran.Tools.Build.Maps;
using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (fx11): <c>obj-check PACK MAP</c> — every DS1 object behaviour of the DS1 map a DS2 host map carries has a DS2
/// counterpart in the pack, or a logged deviation (<see cref="Deviations"/>). Per DS1 object model of the map's MSB:
/// <list type="bullet">
/// <item><b>breakables</b> (DS1 ObjectParam hp ≠ −1, defense 0, fragments NAME_1.flver/.hkx): every DS1 part placed as the DS2
/// model o{AA}_{BBBB}; its archive has the intact model + body and the fragment model + bodies; GDParam.isBreakable 1; the DS2
/// break trigger from DS1's (rule potroll-3: isBreakByPlayerCollide 1 → InstanceType 1150, breakAction 0 = any contact; 0 →
/// 1906, breakAction 4 = rolling / jumping / falling into it breaks it, walking does not; any hit breaks both) through
/// MapObjectDefaultInstanceParam.objType; the break sound (rule OBJ-SND1: no SoundObjectBreakParam row, o{model×1000} defined
/// once by our fevbnd = DS1's o{BBBB}00000, or DS1 defines none); the break effect (OBJ-SFX1: GDParam.sfxIdBreak0 = DS1
/// breakSfxId by rule B2b, −1 = none); item drops (a DS1 treasure on a breakable part); respawn (DS1 breakables come back on
/// every load; DS2 instance-0 objects too: reported).</item>
/// <item><b>animated objects</b> (doors, gates, levers, the boulder, the barrel): their DS1 clip sounds are on the DS2 TAE (rule
/// SND7: the DS1 TAE 128/129 'o'/'a' events of every ported clip = the DS2 TAE's 502000 events of the clip it became).</item>
/// <item><b>chests</b> (o0110 parts): a DS2 treasure box at each (rule CH1) playing DS1's chest sound (rule CH2: model
/// o{AA}_0110, TAE 502000 = DS1's o010000001 copy); Mimics (c2780 enemy parts) are characters, never chests.</item>
/// <item><b>static objects with DS1 collision</b> (Decor) and the rest: placed, or a logged deviation.</item>
/// </list>
/// Exit 1 on any FAIL. DEV lines are known deviations with their reason.
/// </summary>
public static class ObjCheck
{
    /// <summary>Known deviations: (DS1 model or "*", topic) → reason. Kept short; each names its rule / open item.</summary>
    static readonly Dictionary<(string Model, string Topic), string> Deviations = new()
    {
        // DS1 objects with hp and a defense (church pillars o8600/o8610, the hall wall o8520, the "church collapse" pieces
        // o8700-o8702: defense 90-199) break only under attacks whose AtkParam atkObj reaches the defense, in the Asylum the
        // Asylum Demon's; the player's weapons never do. Ported static (Decor / WE1 Static) until the DS2 enemy→object damage
        // rule (MapObjectInstanceTypeParam durability × the attack's object attribute) is worked out: enemy-audit G8, open.
        [("*", "enemy-break")] = "only a strong enemy attack (DS1 atkObj ≥ defense: the Asylum Demon) breaks it in DS1; static in DS2 (enemy-audit G8 open: DS2 enemy→object damage rule)",
        // defense 999 / hp 999: no attack breaks it; DS1's events do (the hall floor o8510/o8511: 11810220's collapse, WE1/WE6)
        [("*", "event-break")] = "no attack breaks it in DS1 (defense 999); DS1's events do: the world-event rule WE1/WE6 (2005:1 / 2005:8 → 130702 on the DS2 object)",
        [("*", "respawn")] = "DS1 breakables reappear on every map load; DS2's instance-0 breakables (jars) too (no instance row, resetType default) — P in game",
        [("*", "fragment-sound")] = "DS1 has no per-fragment sound event; DS2's fragment bodies use the DS2 physics material — none ported",
    };

    static readonly Regex PortLine = new(@"^object (o\d{4}) → (o\d\d_\d{4}) \(template (o\d\d_\d{4})\):.* animations (.*)$");
    static readonly Regex Snd7Line = new(@"^object sound \(SND7\) (o\d{4}) → (o\d\d_\d{4}): (\d+) DS1 sound events");

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: obj-check PACK MAP (DS2 host map, e.g. m19_42_00_00)"); return 1; }
        string pack = args[1], ds2Map = args[2];
        string ds1Map = MapHosts.Ds1Of(ds2Map) ?? ds2Map;
        string dsr = Program.DefaultDsrRoot, aa = ds2Map.Substring(1, 2);
        string msbPath = Path.Combine(pack, "gamedata", "map", ds2Map, ds2Map + ".msb");
        if (!File.Exists(msbPath)) { Console.WriteLine($"obj-check: {ds2Map} is not in this pack: nothing to check"); return 0; }
        var msb1 = MSB1.Read(Path.Combine(dsr, "map", "MapStudio", ds1Map + ".msb"));
        var msb2 = MSB2.Read(msbPath);
        var objParam = Ds1Source.LoadParam(dsr, "ObjectParam")!;
        var ps = ParamStore.LoadDs2(Path.Combine(pack, "title"), ExtractDs2.DefaultOut);
        var inst = LoadPackParam(pack, $"param/mapobjectinstanceparam_{ds2Map}.param");
        string[] build = File.Exists(Path.Combine(pack, "build.log")) ? File.ReadAllLines(Path.Combine(pack, "build.log")) : [];
        var fevbnd = BND4.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(pack, "gamedata", "sound", "frpg2.fevbnd.dcx"))));
        var vanillaFev = BND4.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine("data", "ds2", "sound", "frpg2.fevbnd.dcx")))).Files.Select(f => f.Name).ToHashSet();
        var ours = SoundSurvey.FevIndex(fevbnd.Files.Where(f => f.Name.EndsWith(".fev") && !vanillaFev.Contains(f.Name)).Select(f => (Path.GetFileNameWithoutExtension(f.Name), f.Bytes)));
        var ds1Fev = SoundSurvey.FevIndex(SoundSurvey.Ds1Projects(dsr));
        bool soundOn = ours.Count > 0;

        int fails = 0, devs = 0, oks = 0;
        void Fail(string m) { fails++; Console.WriteLine("  FAIL " + m); }
        void Ok(string m) { oks++; Console.WriteLine("  ok   " + m); }
        void Dev(string m) { devs++; Console.WriteLine("  DEV  " + m); }
        string? DevFor(string model, string topic) => Deviations.GetValueOrDefault((model, topic)) ?? Deviations.GetValueOrDefault(("*", topic));

        var treasureParts = msb1.Events.Treasures.Where(t => t.TreasurePartName != null).GroupBy(t => t.TreasurePartName!).ToDictionary(g => g.Key, g => g.First());
        var objActParts = msb1.Events.ObjActs.Select(a => a.ObjActPartName).Where(n => n != null).ToHashSet();
        var ds2Parts = msb2.Parts.Objects.ToList();
        var ports = build.Select(l => PortLine.Match(l)).Where(m => m.Success).GroupBy(m => m.Groups[2].Value).ToDictionary(g => g.Key, g => g.Last());
        var snd7 = build.Select(l => Snd7Line.Match(l)).Where(m => m.Success).GroupBy(m => m.Groups[2].Value).ToDictionary(g => g.Key, g => int.Parse(g.Last().Groups[3].Value));
        Console.WriteLine($"obj-check {ds2Map} (DS1 {ds1Map}): {msb1.Parts.Objects.Count} DS1 object parts, {msb1.Models.Objects.Count} models; pack MSB {ds2Parts.Count} object parts; DS1 sound projects in the fevbnd: {(soundOn ? "yes" : "no (--no-ds1-sound: sound rules skipped)")}");

        var breakAction = ps.Params["MapObjectBreakActionParam"];
        var itype = ps.Params["MapObjectInstanceTypeParam"];
        foreach (var g in msb1.Parts.Objects.GroupBy(p => p.ModelName).OrderBy(g => g.Key))
        {
            string m = g.Key, n = MapHosts.ObjectName(aa, m);   // rule ON1 (a vanilla DS2 name is never reused)
            int id = ObjPorter.ModelId(n);
            var row = objParam[int.Parse(m[1..])];
            string bndPath = Path.Combine(dsr, "obj", m + ".objbnd.dcx");
            var bnd = File.Exists(bndPath) ? BND3.Read(DCX.Decompress(File.ReadAllBytes(bndPath))) : null;
            bool Has(string f) => bnd?.Files.Any(x => Path.GetFileName(x.Name).Equals(f, StringComparison.OrdinalIgnoreCase)) == true;
            int hp = row != null ? Convert.ToInt32(row["hp"].Value) : -1, def = row != null ? Convert.ToInt32(row["defense"].Value) : 0;
            bool breakable = hp != -1 && def == 0 && Has(m + ".hkx") && Has(m + "_1.flver") && Has(m + "_1.hkx");
            var placedHere = ds2Parts.Where(p => p.ModelName == n).ToList();
            Console.WriteLine($"- {m} ({g.Count()} DS1 parts; ObjectParam hp {hp} defense {def}{(row != null ? $" material {row["materialId"].Value} collide-break {row["isBreakByPlayerCollide"].Value} breakSfx {row["breakSfxId"].Value}" : " (no ObjectParam row)")}) → {n}: {placedHere.Count} DS2 parts");

            if (m == "o0110")
            {
                Chests(g.ToList());
                continue;
            }
            if (breakable) { Breakable(m, n, id, g.ToList(), row!, placedHere); continue; }

            // a model ported with clips (doors, gates, levers, boulder, barrel): SND7
            // the DS2 model of this DS1 model in this map's area (prefab ports may take another number: K1 door templates)
            var port = ports.GetValueOrDefault(n) ?? ports.Values.FirstOrDefault(x => x.Groups[1].Value == m && x.Groups[2].Value.StartsWith($"o{aa}_"));
            if (port != null && port.Groups[4].Value.Trim().Length > 0 && bnd != null && soundOn)
            {
                string ds2n = port.Groups[2].Value;
                var anims = port.Groups[4].Value.Split(", ", StringSplitOptions.RemoveEmptyEntries).Select(s => s.Split('→')).Where(s => s.Length == 2).ToList();
                var anibnd = bnd.Files.FirstOrDefault(f => f.Name.EndsWith(".anibnd", StringComparison.OrdinalIgnoreCase));
                var tae1 = anibnd != null ? BND3.Read(anibnd.Bytes).Files.FirstOrDefault(f => f.Name.EndsWith(".tae", StringComparison.OrdinalIgnoreCase)) : null;
                int want = 0;
                if (tae1 != null)
                {
                    var t1 = TAE.Read(tae1.Bytes);
                    foreach (var a in anims)
                        want += t1.Animations.FirstOrDefault(x => x.ID == int.Parse(a[0].Split('_')[^1]))?.Events.Count(e => e.Type is 128 or 129 && BitConverter.ToInt32(e.GetParameterBytes(false), 0) is 0 or 3) ?? 0;
                }
                string taePath = Path.Combine(pack, "gamedata", "timeact", "obj", ds2n + ".tae");
                int have = 0;
                if (File.Exists(taePath))
                {
                    var t2 = new Ds2Tae(File.ReadAllBytes(taePath));
                    foreach (var a in anims)
                    {
                        var p = a[1].Split('_');
                        long aid = int.Parse(p[^2]) * 10000 + int.Parse(p[^1]);
                        if (t2.Records.ContainsKey(aid)) have += t2.EventsFull(aid, 16).Count(e => e.Type == ObjectSounds.Ds2SoundEvent);
                    }
                }
                if (want == have) Ok($"{m} → {ds2n}: clips {string.Join(", ", anims.Select(a => $"{a[0]}→{a[1]}"))}: {want} DS1 sound events = {have} DS2 502000 events (SND7{(want == 0 ? ": DS1's clips are silent" : "")})");
                else Fail($"{m} → {ds2n}: DS1 clips carry {want} sound events, the DS2 TAE {have} (rule SND7)");
            }
            else if (port != null && port.Groups[4].Value.Trim().Length > 0 && !soundOn)
                Dev($"{m}: DS1 clip sounds not ported (pack built with --no-ds1-sound)");

            if (hp != -1 && def >= 999)
            {
                bool eventPart = g.Any(p => p.EntityID > 0 && build.Any(l => l.Contains($"entity {p.EntityID}") && (l.Contains("WE1") || l.Contains("world-event"))));
                if (eventPart) Ok($"{m}: {DevFor(m, "event-break")}");
                else Dev($"{m}: defense {def}: {DevFor(m, "event-break")} (no WE1 placement line found for its parts)");
            }
            else if (hp != -1 && def > 0)
            {
                if (DevFor(m, "enemy-break") is { } why) Dev($"{m}: defense {def}, hp {hp}: {why}");
                else Fail($"{m}: DS1 breaks it by attacks with atkObj ≥ {def} (enemies), no DS2 rule and no logged deviation");
            }
            if (placedHere.Count == 0 && port == null)
            {
                string? why = build.FirstOrDefault(l => l.Contains(m) && (l.Contains("left out") || l.Contains("stand-in") || l.Contains("skipped") || l.Contains("prefab") || l.Contains("ladder") || l.Contains("white door") || l.Contains("FW1") || l.Contains("BF1")));
                if (m == "o0200") Ok($"{m}: DS1 bonfire → DS2 bonfire (rule BF1)");
                else if (why != null) Dev($"{m}: not placed as {n}: {Short(why)}");
                else if (g.All(p => treasureParts.ContainsKey(p.Name))) Ok($"{m}: item corpse(s) (treasure parts → DS2 item pickups)");
                else Dev($"{m}: no part named {n} (another rule's model: prefab / ladder / white door / scenery under another name); see build.log");
            }
            else if (placedHere.Count > 0)
            {
                int expect = g.Count(p => !treasureParts.ContainsKey(p.Name));
                if (placedHere.Count >= expect) Ok($"{m}: {placedHere.Count} DS2 parts for {expect} DS1 parts");
                else Dev($"{m}: {placedHere.Count} DS2 parts for {expect} DS1 parts (the others by another rule or left out; see build.log)");
            }
        }

        // Mimic-ready: DS1 Mimics are enemy parts (c2780), never chests
        var mimics = msb1.Parts.Enemies.Where(e => e.ModelName == "c2780").ToList();
        if (mimics.Count > 0) Dev($"{mimics.Count} DS1 Mimic(s) (c2780 enemy parts {string.Join(", ", mimics.Select(e => e.EntityID))}): character rules, not ported yet; no chest is placed for them");
        else Ok("no DS1 Mimic (c2780) in this map; chests are o0110 object parts only (rule CH2 Mimic-ready)");
        if (DevFor("*", "respawn") is { } rs) Dev($"respawn: {rs}");
        Console.WriteLine($"obj-check {ds2Map}: {oks} ok, {devs} deviations, {fails} FAIL");
        return fails == 0 ? 0 : 1;

        // ---------------------------------------------------------------- breakables
        void Breakable(string m, string n, int id, List<MSB1.Part.Object> parts, PARAM.Row row, List<MSB2.Part.Object> placed)
        {
            var nonTreasure = parts.Where(p => !treasureParts.ContainsKey(p.Name)).ToList();
            if (placed.Count >= nonTreasure.Count && nonTreasure.Count > 0) Ok($"{m}: {placed.Count} breakable parts placed for {nonTreasure.Count} DS1 parts");
            else if (nonTreasure.Count == 0) Ok($"{m}: every DS1 part carries a treasure (item rule)");
            else
            {
                string? why = build.FirstOrDefault(l => l.Contains(m) && (l.Contains("left out") || l.Contains("WE1")));
                if (placed.Count > 0 || why != null) Dev($"{m}: {placed.Count} of {nonTreasure.Count} parts as {n}{(why != null ? ": " + Short(why) : " (others are world-event parts)")}");
                else Fail($"{m}: breakable, {nonTreasure.Count} DS1 parts, none placed as {n}");
            }
            if (placed.Count == 0) return;
            // rule ST1: the port fell back (a DS1 collision class no DS2 template has): a logged deviation, static in DS2
            if (build.FirstOrDefault(l => l.StartsWith($"ST1\t{ds2Map}\tport fallback\t{m}\t")) is { } st1) { Dev($"{n}: breakable port fell back (rule ST1): {Short(st1.Split('\t').Last())}"); return; }
            if (File.Exists(Path.Combine("data", "ds2", "model", "obj", n + ".bnd"))) Fail($"{n}: a vanilla DS2 model of the same name exists (rule ON1)");
            string bnd2 = Path.Combine(pack, "gamedata", "model", "obj", n + ".bnd");
            if (!File.Exists(bnd2)) { Fail($"{n}: model/obj/{n}.bnd missing"); return; }
            var files = BND4.Read(bnd2).Files.Select(f => Path.GetFileName(f.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var need = new[] { n + ".flv", n + "_h.hkx", n + "_1.flv", n + "_1_h.hkx" }.Where(f => !files.Contains(f)).ToList();
            if (need.Count == 0) Ok($"{n}: intact model + body and fragment model + bodies in the archive (DS1 {m}.flver/.hkx, {m}_1.flver/_1.hkx)");
            else Fail($"{n}: archive lacks {string.Join(", ", need)}");
            var gd = ps.Params["MapObjectGDParam"][id];
            if (gd == null || Convert.ToInt32(gd["isBreakable"].Value) != 1) Fail($"{n}: MapObjectGDParam {id} {(gd == null ? "missing" : "isBreakable 0")}");
            // break trigger
            bool collide = Convert.ToInt32(row["isBreakByPlayerCollide"].Value) != 0;
            int wantType = collide ? MapBuilder.ContactBreakInstanceType : 1906;
            var di = ps.Params["MapObjectDefaultInstanceParam"][id];
            int instanced = placed.Count(p => p.MapObjectInstanceParamID != 0);
            int type = di != null ? Convert.ToInt32(di["objType"].Value) : -1;
            if (type != wantType) Fail($"{n}: DefaultInstance objType {type}, rule potroll-3 wants {wantType} (DS1 isBreakByPlayerCollide {(collide ? 1 : 0)})");
            else
            {
                var it = itype[type];
                int ba = it != null ? Convert.ToInt32(it["breakAction"].Value) : -1;
                Ok($"{n}: break trigger objType {type} (breakAction {ba}{(ba is >= 0 ? " " + Actions(ba, breakAction) : "")}) = DS1 {(collide ? "breaks on any contact (walking into it)" : "breaks when rolled / jumped into, not when walked into")}; any hit breaks it (hp {it?["hp"].Value})");
            }
            if (instanced > 0)
                foreach (var p in placed.Where(p => p.MapObjectInstanceParamID != 0))
                {
                    var ir = inst?[p.MapObjectInstanceParamID];
                    if (ir == null) { Fail($"{p.Name}: instance {p.MapObjectInstanceParamID} has no MapObjectInstanceParam row"); continue; }
                    if ((Convert.ToInt32(ir["objType"].Value) is int ot && ot != 0 && ot != wantType)) Dev($"{p.Name}: instance {p.MapObjectInstanceParamID} objType {ot} (world-event part: DS1's event breaks it)");
                    if (Convert.ToByte(ir["onlyEventCanBreak"].Value) != 0) Dev($"{p.Name}: instance {p.MapObjectInstanceParamID} onlyEventCanBreak (DS1's event breaks it)");
                }
            // break sound
            if (soundOn)
            {
                string ds1Ev = ObjectSounds.BreakEvent(m), ds2Ev = $"o{id * 1000:D9}";
                bool ds1Has = ds1Fev.ContainsKey(ds1Ev);
                if (ps.Params["SoundObjectBreakParam"][id] is { } sb) Fail($"{n}: SoundObjectBreakParam has a row {id} (type {sb["type"].Value}): DS2 would play its generic o00000000{sb["type"].Value}, not DS1's {ds1Ev}");
                else if (!ds1Has) Dev($"{n}: DS1 defines no break sound {ds1Ev} (DS1 breaks it silently): none ported");
                else if (!ours.TryGetValue(ds2Ev, out var own)) Fail($"{n}: break sound {ds2Ev} (DS1 {ds1Ev}) defined by no project of the pack's fevbnd: silent break (rule OBJ-SND1)");
                else if (own.Count != 1) Fail($"{n}: break sound {ds2Ev} defined by {string.Join("+", own)}");
                else Ok($"{n}: break sound {ds2Ev} = DS1 {ds1Ev} ({own[0]}; DS2 break → o{{model×1000}}, no SoundObjectBreakParam row)");
            }
            else Dev($"{n}: break sound not ported (--no-ds1-sound): DS2 plays o{id * 1000:D9}, undefined = silent");
            // break effect
            int ds1Sfx = Convert.ToInt32(row["breakSfxId"].Value), have = gd != null ? Convert.ToInt32(gd["sfxIdBreak0"].Value) : -2;
            if (ds1Sfx < 0) { if (have == -1) Ok($"{n}: break effect none (DS1 breakSfxId -1: fragments only; sfxIdBreak0 -1)"); else Fail($"{n}: DS1 has no break effect, sfxIdBreak0 {have} (rule OBJ-SFX1)"); }
            else if (have == FfxPorter.Ds2IdFor(dsr, ds1Sfx)) Ok($"{n}: break effect DS1 {ds1Sfx} → DS2 {have} (rule B2b)");
            else if (build.FirstOrDefault(l => l.Contains($"DS1 effect {ds1Sfx} not convertible")) is { } nc)
                Dev($"{n}: DS1 break effect {ds1Sfx} not convertible ({Short(nc)}): DS2's jar dust {have} stands in (rule B1)");
            else Fail($"{n}: DS1 break effect {ds1Sfx}, sfxIdBreak0 {have} (want {FfxPorter.Ds2IdFor(dsr, ds1Sfx)}, rule OBJ-SFX1)");
            // item drops
            var withItems = parts.Where(p => treasureParts.ContainsKey(p.Name)).ToList();
            if (withItems.Count == 0) Ok($"{m}: DS1 breakables of this model drop nothing (no MSB treasure on them; DS1 ObjectParam has no lot)");
            else Dev($"{m}: {withItems.Count} part(s) carry a DS1 treasure ({string.Join(", ", withItems.Select(p => p.Name))}): the item-pickup rule places a pickup at the spot, the part itself is not placed breakable");
        }

        // ---------------------------------------------------------------- chests
        void Chests(List<MSB1.Part.Object> parts)
        {
            // a chest = an o0110 part with an in-chest MSB treasure (rules CH1 / CH1b); an empty one (Firelink's 救済用 rescue chest
            // o0110_0004: no treasure, its ObjAct event never initialised) is reported
            var inChest = msb1.Events.Treasures.Where(t => t.InChest && t.TreasurePartName != null).Select(t => t.TreasurePartName!).ToHashSet();
            var chests = parts.Where(p => inChest.Contains(p.Name)).ToList();
            foreach (var p in parts.Where(p => !inChest.Contains(p.Name))) Dev($"{p.Name}: o0110 without an in-chest treasure (DS1 rescue / unused chest): no box placed");
            string model = soundOn ? $"o{aa}_0110" : "o00_0200";
            var boxes = ds2Parts.Where(p => p.ModelName == model).ToList();
            if (boxes.Count >= chests.Count) Ok($"o0110: {chests.Count} DS1 chest(s) → {boxes.Count} DS2 treasure box(es) {model} (rule CH1{(soundOn ? "/CH2" : "")})");
            else Fail($"o0110: {chests.Count} DS1 chests, {boxes.Count} DS2 boxes {model}");
            foreach (var b in boxes)
            {
                var ir = inst?[b.MapObjectInstanceParamID];
                if (ir == null || Convert.ToInt32(ir["objCtrlType"].Value) != 1) Fail($"{b.Name}: instance {b.MapObjectInstanceParamID} {(ir == null ? "has no row" : $"objCtrlType {ir["objCtrlType"].Value}")} (treasure box = 1)");
                else if (ps.Params["ItemLotParam2_Other"][b.MapObjectInstanceParamID] == null) Fail($"{b.Name}: no ItemLotParam2_Other row {b.MapObjectInstanceParamID}");
                else Ok($"{b.Name}: treasure box, lot {b.MapObjectInstanceParamID}");
            }
            if (!soundOn || boxes.Count == 0) return;
            int id = ObjPorter.ModelId(model);
            var mop = ps.Params["MapObjectParam"][id];
            if (mop == null || Convert.ToInt32(mop["timeActId"].Value) != id) { Fail($"{model}: MapObjectParam timeActId {(mop == null ? "row missing" : mop["timeActId"].Value)} (rule CH2: {id})"); return; }
            string taePath = Path.Combine(pack, "gamedata", "timeact", "obj", model + ".tae");
            if (!File.Exists(taePath)) { Fail($"{model}: timeact/obj/{model}.tae missing"); return; }
            var t2 = new Ds2Tae(File.ReadAllBytes(taePath));
            var evs = t2.Records.Keys.SelectMany(a => t2.EventsFull(a, 16).Select(e => (a, e))).Where(x => x.e.Type is 502000 or 502001 or 502002).ToList();
            var names = evs.Where(x => x.e.Type == 502000 && x.e.Params[4] == 100).Select(x => $"o{id * 1000 + BitConverter.ToInt32(x.e.Params, 0):D9}").ToList();
            if (evs.Any(x => x.e.Type != 502000 || x.e.Params[4] != 100)) Fail($"{model}: DS2 box sound events left in its TAE");
            else if (names.Count == 0) Fail($"{model}: no DS1 chest sound on its TAE");
            else foreach (var nm in names)
                if (!ours.TryGetValue(nm, out var own) || own.Count != 1) Fail($"{model}: {nm} not defined once by our fevbnd");
                else Ok($"{model}: open clip plays {nm} = DS1's chest sound ({own[0]}) at {evs.First(x => $"o{id * 1000 + BitConverter.ToInt32(x.e.Params, 0):D9}" == nm).e.Start:0.###} s (rule CH2)");
        }
    }

    /// <summary>What a DS2 breakAction row does for the player (kind 3 = DS2's jar behaviour for the player, potroll notes).</summary>
    static string Actions(int breakAction, PARAM table)
    {
        // exe table (potroll NOTES point 5): row for chr kind 1..7 per breakAction 1..9; 0 and 10 unchanged
        int[][] t = [[3, 2, 1, 1, 1, 1, 1], [4, 3, 2, 1, 1, 1, 1], [5, 4, 3, 2, 1, 1, 1], [6, 5, 4, 3, 2, 1, 1], [7, 6, 5, 4, 3, 2, 1], [8, 7, 6, 5, 4, 3, 1], [9, 8, 7, 6, 5, 4, 1], [9, 9, 8, 7, 6, 5, 1], [9, 9, 9, 8, 7, 6, 1]];
        int row = breakAction is 0 or 10 || breakAction > 9 ? breakAction : t[breakAction - 1][2];
        var r = table[row];
        if (r == null) return $"→ row {row} (no MapObjectBreakActionParam row)";
        return $"→ MapObjectBreakActionParam {row} for the player (kind 3): " + string.Join(" ", new[] { "walk", "dash", "rolling", "jump", "fall" }.Select(f => $"{f} {(Convert.ToInt32(r[f].Value) != 0 ? "breaks" : "no")}"));
    }

    static PARAM? LoadPackParam(string pack, string rel)
    {
        string p = Path.Combine(pack, "gamedata", rel.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(p)) return null;
        var defs = Directory.GetFiles(Path.Combine(ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(x => PARAMDEF.XmlDeserialize(x)).ToList();
        var param = PARAM.Read(p);
        return param.ApplyParamdefCarefully(defs) ? param : null;
    }

    static string Short(string s) => s.Length > 140 ? s[..140] + "…" : s.Trim();
}
