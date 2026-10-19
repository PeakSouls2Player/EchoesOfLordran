using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Session 11 (burg11, M2 leftovers), rule CH1: a DS1 chest (o0110 part with an MSB ObjAct; its MSB treasure has InChest)
/// becomes DS2's own treasure box, the vanilla pattern (V-data, FoFG Cardinal Tower chest 10105010): part o00_0200 at
/// DS1's chest (DS1 rotation, P: the two models' fronts are checked in game), MapObjectInstanceParam row objCtrlType 1
/// (treasure box), objCtrlId = its MapObjectTreasureBoxParam row (copy of FoFG 10105000: no trap), itemLotId 1 = the
/// ItemLotParam2_Other row with the instance's id (FoFG 10105010's lot is row 10105010), filled with DS1's lot through the
/// item rules (Ds1Drops.MapItem). Instance = AABB0000 + entity % 10000 (stable: DS2 keeps "opened" per instance).
/// </summary>
public static class MapChests
{
    const string Fofg = "m10_10_00_00";
    const int FofgChest = 10105010, FofgBox = 10105000;

    public static List<string> Build(Pack pack, Ds1Source ds1, MapBuilder.Options map, MapObjects plan) => Build(pack, ds1, map, plan.Chests);

    /// <summary>
    /// Session 11 (fx11), rule CH1b: rule CH1 for a map built without a MapObjects plan (the Asylum, Firelink: their own verified
    /// object code, MapBuilder left every in-chest treasure out — Firelink's four chests below the shrine with Lloyd's Talismans,
    /// the Morning Star + Talisman, Homeward Bones and Cracked Red Eye Orbs were missing): every o0110 part carrying a DS1 MSB
    /// treasure with InChest. Instance = AABB0000 + entity % 10000, or AABB9000 + the part's number for a part without an entity
    /// (Firelink's chests have none), stable between builds.
    /// </summary>
    public static List<string> Build(Pack pack, Ds1Source ds1, MapBuilder.Options map)
    {
        var msb1 = MSB1.Read(Path.Combine(ds1.Root, "map", "MapStudio", map.Ds1Map + ".msb"));
        int aabb = MapHosts.Aabb(map.Ds2Map);
        var chests = msb1.Parts.Objects.Where(p => p.ModelName == "o0110" && msb1.Events.Treasures.Any(t => t.TreasurePartName == p.Name && t.InChest))
            .Select(p => (p, p.EntityID > 0 ? aabb * 10000 + p.EntityID % 10000 : aabb * 10000 + 9000 + int.Parse(p.Name[(p.Name.LastIndexOf('_') + 1)..]))).ToList();
        return Build(pack, ds1, map, chests);
    }

    static List<string> Build(Pack pack, Ds1Source ds1, MapBuilder.Options map, List<(MSB1.Part.Object Part, int Instance)> chests)
    {
        var log = new List<string>();
        if (chests.Count == 0) return log;
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        var msb1 = MSB1.Read(Path.Combine(ds1.Root, "map", "MapStudio", map.Ds1Map + ".msb"));
        var msb = MSB2.Read(pack.GetFile("gamedata", $"map/{map.Ds2Map}/{map.Ds2Map}.msb"));
        var fofg = MSB2.Read(Path.Combine(pack.Ds2Data, "map", Fofg, Fofg + ".msb"));
        var tChest = fofg.Parts.Objects.First(o => o.MapObjectInstanceParamID == FofgChest);
        var inst = PARAM.Read(pack.GetFile("gamedata", $"param/mapobjectinstanceparam_{map.Ds2Map}.param"));
        if (!inst.ApplyParamdefCarefully(defs)) throw new InvalidDataException("MapObjectInstanceParam: no paramdef fits");
        var fInst = PARAM.Read(Path.Combine(pack.Ds2Data, "param", $"mapobjectinstanceparam_{Fofg}.param"));
        fInst.ApplyParamdefCarefully(defs);
        var boxes = PARAM.Read(Path.Combine(pack.Ds2Data, "param", $"treasureboxparam_{Fofg}.param"));
        boxes.ApplyParamdefCarefully(defs);
        var tBox = boxes[FofgBox]!;
        boxes.Rows.Clear();
        var ds1Lots = ds1.Params.Params["ItemLotParam"];
        var lots = pack.Param("ItemLotParam2_Other");
        // session 11 (fx11), rule CH2: the box plays DS1's chest sound (a model of our own; DS2's o00_0200 keeps its sounds)
        string boxModel = ObjectSounds.Enabled ? ChestModel(pack, ds1.Root, $"o{map.Ds2Map.Substring(1, 2)}_0110", log) : "o00_0200";
        if (!msb.Models.Objects.Any(m => m.Name == boxModel)) msb.Models.Objects.Add(new MSB2.Model.Object { Name = boxModel });
        foreach (var (part, id) in chests)
        {
            var treasure = msb1.Events.Treasures.FirstOrDefault(t => t.TreasurePartName == part.Name);
            var got = new List<(int Item, int Num, int Reinforce)>();
            // fx11: DS1 awards a lot as a chain, row N and N+1, N+2 … up to the first missing id (Commands.Ds1Lots; Firelink's
            // Morning Star chest 1020180 + the Talisman 1020181)
            foreach (int l0 in treasure?.ItemLots.Where(l => l > 0) ?? [])
              for (int l = l0; l < l0 + Commands.Ds1Lots.MaxChain && ds1Lots[l] != null; l++)
                if (ds1Lots[l] is { } row)
                    for (int i = 1; i <= 8; i++)
                    {
                        int item = Convert.ToInt32(row[$"lotItemId0{i}"].Value);
                        if (item <= 0) continue;
                        uint cat = unchecked((uint)Convert.ToInt64(row[$"lotItemCategory0{i}"].Value));
                        int ds2 = Chr.Ds1Drops.MapItem(pack, ds1, cat, item, out string kind, out int reinforce, out _);
                        if (ds2 == 0) { log.Add($"CH1 {part.Name}: DS1 {kind} {item} not in DS2 yet, left out"); continue; }
                        got.Add((ds2, Math.Max(1, Convert.ToInt32(row[$"lotItemNum0{i}"].Value)), reinforce));
                    }
            var lot = pack.AddRow("ItemLotParam2_Other", lots[FofgChest]!, id, $"[Lordran] {map.Ds2Map} chest {part.Name}");
            for (int i = 1; i <= 10; i++)
            {
                var g = i <= got.Count ? got[i - 1] : default;
                lot[$"itemId{i}"].Value = Convert.ChangeType(i <= got.Count ? g.Item : 10, lot[$"itemId{i}"].Value.GetType());
                lot[$"itemNum{i}"].Value = Convert.ChangeType(i <= got.Count ? g.Num : 0, lot[$"itemNum{i}"].Value.GetType());
                lot[$"reinforceLevel{i}"].Value = Convert.ChangeType(i <= got.Count ? g.Reinforce : 0, lot[$"reinforceLevel{i}"].Value.GetType());
                lot[$"bExFlag{i}"].Value = Convert.ChangeType(i <= got.Count ? 1 : 0, lot[$"bExFlag{i}"].Value.GetType());
            }
            lot["lotCount"].Value = Convert.ChangeType(Math.Max(1, got.Count), lot["lotCount"].Value.GetType());
            var r = new PARAM.Row(fInst[FofgChest]!) { ID = id };
            r["objCtrlId"].Value = Convert.ChangeType(id, r["objCtrlId"].Value.GetType());
            inst.Rows.RemoveAll(x => x.ID == id);
            inst.Rows.Add(r);
            boxes.Rows.Add(new PARAM.Row(tBox) { ID = id });
            var o = (MSB2.Part.Object)tChest.DeepCopy();
            o.ModelName = boxModel;
            o.Name = $"{boxModel}_{msb.Parts.Objects.Count(x => x.ModelName == boxModel):D4}";
            o.Position = part.Position; o.Rotation = part.Rotation;
            o.MapObjectInstanceParamID = id;
            o.UnkT04 = (short)map.BonfireId;
            Array.Fill(o.DrawGroups, 0xFFFFFFFF); Array.Fill(o.DispGroups, 0xFFFFFFFF);
            msb.Parts.Objects.Add(o);
            log.Add($"CH1 chest {part.Name} (entity {part.EntityID}) → {boxModel} instance {id}, lot {id}: {string.Join(", ", got.Select(x => $"{x.Item} x{x.Num}"))}");
        }
        inst.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
        boxes.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
        pack.AddFile("gamedata", $"param/mapobjectinstanceparam_{map.Ds2Map}.param", inst.Write());
        pack.AddFile("gamedata", $"param/treasureboxparam_{map.Ds2Map}.param", boxes.Write());
        pack.AddFile("gamedata", $"map/{map.Ds2Map}/{map.Ds2Map}.msb", msb.Write());
        foreach (var l in log) pack.Note(l);
        return log;
    }

    /// <summary>
    /// Session 11 (fx11, user: "make sure … chests openings, etc [make their correct sounds]"), rule CH2: DS2's treasure box keeps
    /// DS2's box behaviour (CH1: the player's open motion, item hand-over and the opened state are DS2's treasure-box system, paired
    /// with DS2's box clips) but plays DS1's chest sound. DS1's chest o0110 (one clip a00_0000, TAE anim 0: sound 'o' 10000001 =
    /// o010000001 of frpg_main at 0.733 s) and DS2's box o00_0200 (clip a00_01_0001 = TAE 10001 of o81_0200.tae, its MapObjectParam
    /// timeActId 810200: sounds o810200001-003). The box is cloned as o{AA}_0110 (<see cref="ObjPorter.CloneDs2"/>: model, clips,
    /// collision, MapObject* rows), its MapObjectParam.timeActId = its own model id (190110), so DS2 reads
    /// timeact/obj/o{AA}_0110.tae and names its 502000 sounds o{190110 × 1000 + id} (the handler's model = timeActId when set,
    /// decomp FUN_1403c1db0); that TAE = o81_0200.tae with DS2's sound events dropped and DS1's written by SND7
    /// (<see cref="ObjectSounds.Apply"/>), shifted by the lid's motion onset in DS2's clip minus DS1's
    /// (<see cref="ObjectSounds.MotionOnset"/>) so DS1's lid sound starts when DS2's lid starts to move. DS2's other box events
    /// (500000-500004) keep their times. Mimic-ready: only o0110 OBJECT parts are chests here; DS1's Mimic (c2780, NpcParam
    /// 278000/278010/278011) is an ENEMY part and goes through the character rules, never CH1/CH2. Off (o00_0200 as CH1) with
    /// build-pack --no-ds1-sound.
    /// </summary>
    /// <summary>Rule CH2 ordering: the chest model (and its SND7 import) before the map's sound banks are written
    /// (MapBuilder.Build → Atmosphere.SoundBanks consumes <see cref="ObjectSounds.Pending"/>); a no-op for maps without chests.</summary>
    public static void Prepare(Pack pack, string dsrRoot, MapBuilder.Options map)
    {
        if (!ObjectSounds.Enabled) return;
        var msb1 = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", map.Ds1Map + ".msb"));
        if (!msb1.Parts.Objects.Any(p => p.ModelName == "o0110" && msb1.Events.Treasures.Any(t => t.TreasurePartName == p.Name && t.InChest))) return;
        var log = new List<string>();
        ChestModel(pack, dsrRoot, $"o{map.Ds2Map.Substring(1, 2)}_0110", log);
        foreach (var l in log) pack.Note(l);
    }

    public static string ChestModel(Pack pack, string dsrRoot, string n, List<string> log)
    {
        int id = ObjPorter.ModelId(n);
        if (pack.TryGetFile("gamedata", $"model/obj/{n}.bnd") != null) return n;
        ObjPorter.CloneDs2(pack, "o00_0200", n);
        // the clone's textures under its own name (rule §3 / tex-collide: no texture shared by name with another owner; the
        // ACB already names them n_* after CloneDs2's string swap): FLVER texture paths and TPF entries o00_0200_* → n_*
        {
            var cb = BND4.Read(pack.GetFile("gamedata", $"model/obj/{n}.bnd"));
            foreach (var f in cb.Files)
            {
                string fn = Path.GetFileName(f.Name);
                if (fn.EndsWith(".flv", StringComparison.OrdinalIgnoreCase))
                {
                    var fl = FLVER2.Read(f.Bytes);
                    foreach (var mat in fl.Materials) foreach (var tex in mat.Textures) tex.Path = tex.Path.Replace("o00_0200", n, StringComparison.OrdinalIgnoreCase);
                    f.Bytes = fl.Write();
                }
                else if (fn.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase))
                {
                    var tpf = TPF.Read(f.Bytes);
                    foreach (var tex in tpf.Textures) tex.Name = tex.Name.Replace("o00_0200", n, StringComparison.OrdinalIgnoreCase);
                    f.Bytes = tpf.Write();
                    f.Name = f.Name.Replace("o00_0200", n, StringComparison.OrdinalIgnoreCase);
                }
            }
            pack.AddFile("gamedata", $"model/obj/{n}.bnd", cb.Write());
        }
        var mop = pack.Param("MapObjectParam")[id] ?? throw new InvalidDataException($"CH2: no MapObjectParam row {id} after the clone");
        long tact = Convert.ToInt64(mop["timeActId"].Value);
        string tTae = $"o{tact / 10000:D2}_{tact % 10000:D4}";
        mop["timeActId"].Value = Convert.ChangeType(id, mop["timeActId"].Value.GetType());
        pack.AddFile("gamedata", $"timeact/obj/{n}.tae", File.ReadAllBytes(Path.Combine(pack.Ds2Data, "timeact", "obj", tTae + ".tae")));
        var ds1 = BND3.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(dsrRoot, "obj", "o0110.objbnd.dcx"))));
        var anibnd = BND3.Read(ds1.Files.First(f => f.Name.EndsWith(".anibnd", StringComparison.OrdinalIgnoreCase)).Bytes);
        float? on1 = ObjectSounds.MotionOnset(new Formats.Havok.HkSplineAnim(new Formats.Havok.HkTagfile(anibnd.Files.First(f => f.Name.EndsWith("a00_0000.hkx", StringComparison.OrdinalIgnoreCase)).Bytes)));
        var box = BND4.Read(Path.Combine(pack.Ds2Data, "model", "obj", "o00_0200.bnd"));
        float? on2 = ObjectSounds.MotionOnset(ObjectSounds.Ds2Clip(box.Files.First(f => f.Name.EndsWith("a00_01_0001.hkx", StringComparison.OrdinalIgnoreCase)).Bytes));
        float shift = on1 is float a && on2 is float b ? b - a : 0;
        int written = ObjectSounds.Apply(pack, anibnd, "o0110", n, [("a00_0000", "a00_01_0001")], shift);
        log.Add($"CH2 chest model {n} (clone of o00_0200, timeActId {tact} → {id}, TAE {tTae}.tae → {n}.tae): {written} DS1 chest sound event(s), "
            + $"lid onset DS1 {on1?.ToString("0.000") ?? "none"} s / DS2 {on2?.ToString("0.000") ?? "none"} s → shift {shift:+0.000;-0.000} s");
        return n;
    }
}
