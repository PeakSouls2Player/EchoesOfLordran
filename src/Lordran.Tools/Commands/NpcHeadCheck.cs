using System.Numerics;
using Lordran.Tools.Build;
using Lordran.Tools.Build.Chr;
using Lordran.Tools.Formats;
using Lordran.Tools.Textures;
using SoulsFormats;
using static Lordran.Tools.Build.ParamCells;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (track npc12): <c>npc-head-check PACK [OUTDIR]</c>, the offline gate for rule N-HEAD1 / N-OUT1 (every DS1 human NPC the
/// pack places looks like DS1: head, face, hair, every armour slot) and, with OUTDIR, preview renders DS1 | ours.
/// <para>NPCs: every placed DS1 NPC (NpcPorter.Asylum, .Firelink, Mappings\ds1-npcs.tsv) whose NPCGenerateParam row is in the pack, and
/// every pack NPCGenerateParam row 900000-999999 must be one of them (a future NPC route that bypasses NpcPorter fails here).</para>
/// <para>Checks per c0000 NPC (H1-H9): H1 NPCGenerateParam faceType 0 / faceId -1 (no DS2 face model; faceType 1 drew no head);
/// H2 equipHeadId = NpcPlayerStatusParam.headArmor = the N-HEAD1 head piece of its NpcParam; H3 the head ArmorParam's model is
/// 8000 + NpcParam % 1000, modelGender 0, its own EquipInterfereParam row hides every DS2 face/hair group (both sexes); H4 the part
/// binders (hd_MODEL_m + _l, gamedata + partslq) exist; H5 the part holds what DS1 draws for that outfit (helm meshes when DS1 has a
/// helm, the FaceGen face and eyes unless DS1's worn pieces hide groups 12/13, the back of the head unless 15 is hidden, the hair
/// unless 6 is hidden or the style has no part): a bare-headed NPC without a face = FAIL (headless); H6 every material texture is in
/// the binder; H7 every head vertex within 0.45 m of DS2's Head bone, the face's centroid in front of it (+Z); H8 body/arm/leg = DS1's
/// pieces (N-OUT1: ported ids; empty slot -1) with parts for the NPC's sex; H9 every ArmorParam the NPC's rows name exists. Per chr NPC
/// (c2510 / c2750 / c2640): H1, the body part BD_{4500 + chr % 500} exists with meshes, the other slots are empty parts.</para>
/// </summary>
public static partial class OutfitCheck
{
    public static int NpcHeads(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: npc-head-check PACK [OUTDIR]"); return 1; }
        string packDir = args[1];
        string? outDir = args.Length > 2 ? args[2] : null;
        if (outDir != null) Directory.CreateDirectory(Path.Combine(outDir, "npcs"));
        var ds2 = ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        var c = new Ctx2 { PackDir = packDir, Mine = ParamStore.LoadDs2(Path.Combine(packDir, "title"), ExtractDs2.DefaultOut), Ds1 = new Ds1Source(Program.DefaultDsrRoot), Rig = new PlayerRig(Program.DefaultDsrRoot, ExtractDs2.DefaultOut) };
        var probe = new ArmorPorter(c.Ds1, ds2, new Pack(Program.DefaultDs2Root, ExtractDs2.DefaultOut), c.Rig);
        var faceGroups = NpcHead.Ds2FaceGroups(ExtractDs2.DefaultOut);
        var tsvMaps = File.Exists(Path.Combine(AppContext.BaseDirectory, "Mappings", "ds1-npcs.tsv"))
            ? File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Mappings", "ds1-npcs.tsv")).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split('\t')[0]).Distinct().ToList() : [];
        var placed = NpcPorter.Asylum.Concat(NpcPorter.Firelink).Concat(tsvMaps.SelectMany(NpcPorter.Table)).ToList();
        var gp = c.Mine.Params["NPCGenerateParam"];
        var sp = c.Mine.Params["NpcPlayerStatusParam"];
        var ap = c.Mine.Params["ArmorParam"];
        var inter = c.Mine.Params["EquipInterfereParam"];
        int problems = 0, npcs = 0;
        var seenIds = new HashSet<int>();
        var html = new List<string> { "<!doctype html><meta charset=utf-8><title>DS1 NPC looks</title><style>body{background:#16161a;color:#ddd;font:13px sans-serif}div{margin:10px 6px}img{display:block;max-width:100%}.bad{color:#f77}</style>",
            "<h1>DS1 human NPCs: DS1 vs ours</h1><p>Panels: DS1 (DS1's parts and FaceGen head on DS1's T bind pose, DS1 hiding) | ours (the pack's DS2 parts on DS2's A pose, DS2 culling) | head front DS1 | ours | head side DS1 | ours.</p>" };
        var headBone = HeadBone(c.Rig);
        var msbCache = new Dictionary<string, MSB1>();
        foreach (var npc in placed)
        {
            var msb = msbCache.TryGetValue(npc.Ds1Map, out var mm) ? mm : msbCache[npc.Ds1Map] = MSB1.Read(Path.Combine(c.Ds1.Root, "map", "MapStudio", npc.Ds1Map + ".msb"));
            var part = msb.Parts.Enemies.FirstOrDefault(e => e.EntityID == npc.Entity);
            if (part == null) { Console.WriteLine($"  FAIL {npc.Ds1Map} entity {npc.Entity}: no DS1 MSB character"); problems++; continue; }
            int id = NpcPorter.Id(part.NPCParamID);
            var gen = gp[id];
            if (gen == null) { Console.WriteLine($"  -    {npc.Ds1Map} {npc.Entity} {npc.Why[..Math.Min(40, npc.Why.Length)]}: not in this pack (map not built)"); continue; }
            seenIds.Add(id);
            npcs++;
            var st = sp[id];
            string name = c.Ds1.Params.RowName("NpcParam", part.NPCParamID) is { Length: > 0 } n ? n : part.Name;
            var bad = new List<string>();
            var ok = new List<string>();
            // H1
            if (I(gen, "faceType") != 0 || I(gen, "faceId") != -1) bad.Add($"H1 NPCGenerateParam faceType {I(gen, "faceType")} faceId {I(gen, "faceId")} (want 0 / -1: no DS2 face model)");
            int[] genIds = new[] { "equipHeadId", "equipBodyId", "equipArmId", "equipLegId" }.Select(f => I(gen, f)).ToArray();
            int[] stIds = st == null ? [0, 0, 0, 0] : new[] { "headArmor", "bodyArmor", "armArmor", "legArmor" }.Select(f => I(st, f)).ToArray();
            if (st == null) bad.Add($"H9 no NpcPlayerStatusParam {id}");
            // H9: every id resolves
            foreach (int a in genIds.Concat(stIds).Where(a => a > 0).Distinct())
                if (ap[a] == null) bad.Add($"H9 ArmorParam {a} missing");
            bool female;
            Image? ds1Body = null, ds1Front = null, ds1Side = null;
            if (part.ModelName == "c0000")
            {
                var init = c.Ds1.Params.Params["CharaInitParam"][part.CharaInitID]!;
                female = I(init, "npcPlayerSex") == 0;
                int model = NpcHead.Model(part.NPCParamID), headId = NpcHead.ArmorId(model);
                // H0: DS2 has no bare-head mesh (ArmorParam 11001100 = model 1001, no hd_1001 part): a bare head slot shows a head only
                // through a face model of its own (faceType 0 + an FC_ faceId, Rosabeth / Carhillion)
                if (genIds[0] <= 0 && !(I(gen, "faceType") == 0 && I(gen, "faceId") >= 0))
                    bad.Add($"H0 HEADLESS: bare head slot (equipHeadId {genIds[0]}) and no DS2 face model (faceType {I(gen, "faceType")}, faceId {I(gen, "faceId")}, preset {I(gen, "facePresetNo")}): DS2 draws no head");
                // H2
                if (genIds[0] != headId) bad.Add($"H2 equipHeadId {genIds[0]} ≠ head piece {headId}");
                if (stIds[0] != headId) bad.Add($"H2 status headArmor {stIds[0]} ≠ head piece {headId}");
                // H3
                if (ap[headId] is { } hr)
                {
                    if (I(hr, "modelId") != model || I(hr, "modelGender") != 0) bad.Add($"H3 ArmorParam {headId} model {I(hr, "modelId")}/{I(hr, "modelGender")} (want {model}/0)");
                    if (inter[I(hr, "interfereId")] is not { } ir) bad.Add($"H3 interfere row {I(hr, "interfereId")} missing");
                    else
                    {
                        var miss = faceGroups.Where(g => I(ir, $"cullingGroup{g:D2}_0") == 0 || I(ir, $"cullingGroup{g:D2}_1") == 0).ToList();
                        if (miss.Count > 0) bad.Add($"H3 interfere row {I(hr, "interfereId")} leaves DS2 face/hair groups {string.Join(",", miss)} drawn");
                    }
                }
                else bad.Add($"H3 head ArmorParam {headId} missing");
                // H4
                string file = $"hd_{model}_m";
                foreach (string rel in new[] { $"gamedata/model/parts/head/{file}.bnd", $"gamedata/model/parts/head/{file}_l.bnd", $"partslq/model_lq/parts/head/{file}.bnd", $"partslq/model_lq/parts/head/{file}_l.bnd" })
                    if (!File.Exists(Path.Combine(packDir, rel))) bad.Add($"H4 no {rel}");
                // H5-H7: what the part holds vs what DS1 draws
                var ds1Head = NpcHead.Ds1Head(c.Ds1, init, out var ds1Tex, out var info, $"NPCHEAD_{part.NPCParamID}");
                var bytes = c.PartBytes("head", file + ".bnd");
                if (bytes != null)
                {
                    var bnd = BND4.Read(bytes);
                    var flv = FLVER2.Read(bnd.Files.First(x => x.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase)).Bytes);
                    var texNames = bnd.Files.Where(x => x.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase)).SelectMany(x => TPF.Read(x.Bytes).Textures).Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    string D(FLVER2.Material m) => m.Textures.FirstOrDefault(t => t.ParamName == "g_DiffuseTexture")?.Path is { } p ? Path.GetFileNameWithoutExtension(p) : "";
                    var kinds = flv.Meshes.Select(m => D(flv.Materials[m.MaterialIndex])).ToList();
                    bool Has(string s) => kinds.Any(k => k.Contains(s, StringComparison.OrdinalIgnoreCase));
                    bool face = Has("_face0"), eyes = Has("_eye"), hair = Has("_hair"), back = Has($"FC_{(female ? "F" : "M")}_face");
                    int helmMeshes = kinds.Count(k => !k.Contains("_face0") && !k.Contains("_face1") && !k.Contains("_eye") && !k.Contains("_hair") && !k.Contains("_FC_"));
                    void Expect(string what, bool want, bool have) { if (want && !have) bad.Add($"H5 DS1 draws the {what}, our head part has none"); else if (!want && have) bad.Add($"H5 DS1 hides the {what}, our head part draws it"); }
                    Expect("FaceGen face", info.Face, face);
                    Expect("eyes", info.Eyes, eyes);
                    Expect("back of the head", info.Back, back);
                    Expect("hair", info.Hair, hair);
                    if (info.Helm && helmMeshes == 0) bad.Add("H5 DS1 helm, no helm meshes in our head part");
                    if (!info.Helm && !face) bad.Add("H5 HEADLESS: no helm and no face");
                    // H6
                    foreach (var m in flv.Materials)
                        foreach (var t in m.Textures.Where(t => t.ParamName is "g_DiffuseTexture" or "g_SpecularTexture" or "g_BumpmapTexture"))
                        {
                            string tn = Path.GetFileNameWithoutExtension(t.Path);
                            if (!texNames.Contains(tn)) bad.Add($"H6 {file}: texture {tn} not in the binder");
                        }
                    // H7
                    var verts = flv.Meshes.SelectMany(m => m.Vertices.Select(v => v.Position)).ToList();
                    float far = verts.Count == 0 ? 0 : verts.Max(p => Vector3.Distance(p, headBone));
                    if (verts.Count == 0) bad.Add("H7 head part has no vertices");
                    else if (far > 0.45f) bad.Add($"H7 a head vertex {far:0.00} m from DS2's Head bone");
                    var faceVerts = flv.Meshes.Where(m => D(flv.Materials[m.MaterialIndex]).Contains("_face0")).SelectMany(m => m.Vertices.Select(v => v.Position)).ToList();
                    if (faceVerts.Count > 0)
                    {
                        var cen = faceVerts.Aggregate(Vector3.Zero, (a, p) => a + p) / faceVerts.Count;
                        // the face sits on the side of the head opposite the back of the head, as in DS1 (no facing convention assumed)
                        var backVerts = flv.Meshes.Where(m => D(flv.Materials[m.MaterialIndex]).Contains("_FC_")).SelectMany(m => m.Vertices.Select(v => v.Position)).ToList();
                        Vector3 Mean(IEnumerable<Vector3> ps) { var l = ps.ToList(); return l.Count == 0 ? Vector3.Zero : l.Aggregate(Vector3.Zero, (a, p) => a + p) / l.Count; }
                        var d1Face = Mean(ds1Head.Meshes.Where(m => ds1Head.Materials[m.MaterialIndex].Name.StartsWith("face0")).SelectMany(m => m.Vertices.Select(v => v.Position)));
                        var d1Back = Mean(ds1Head.Meshes.Where(m => ds1Head.Materials[m.MaterialIndex].Name == "back").SelectMany(m => m.Vertices.Select(v => v.Position)));
                        if (backVerts.Count > 0 && d1Back != Vector3.Zero)
                        {
                            float ours = cen.Z - Mean(backVerts).Z, theirs = d1Face.Z - d1Back.Z;
                            if (Math.Sign(ours) != Math.Sign(theirs) || Math.Abs(ours) < 0.03f) bad.Add($"H7 face vs back of head z {ours:+0.000;-0.000} m (DS1 {theirs:+0.000;-0.000} m): face on the wrong side");
                        }
                        ok.Add($"face {faceVerts.Count} verts, centroid {cen.Y - headBone.Y:+0.000;-0.000} m / {cen.Z - headBone.Z:+0.000;-0.000} m from Head");
                    }
                    ok.Add($"head part {file}: {flv.Meshes.Count} meshes / {verts.Count} verts (helm {helmMeshes}, face {face}, eyes {eyes}, back {back}, hair {hair}{(info.HairPart == null ? $" — DS1 style {info.HairStyle} is bald" : "")}), FaceGen {info.FaceGenId} shape ≤ {info.MorphMm:0.0} mm");
                }
                else bad.Add($"H4 no part {file}.bnd");
                // H8: the other slots = DS1's pieces
                string[] fields = ["equip_Helm", "equip_Armer", "equip_Gaunt", "equip_Leg"];
                for (int s = 1; s < 4; s++)
                {
                    int d1 = I(init, fields[s]);
                    int want = d1 > 0 ? ExpectedArmor(c.Ds1, ds2, probe, d1) : -1;
                    if (genIds[s] != want) bad.Add($"H8 {fields[s]} {d1}: equip slot {s} = {genIds[s]}, want {want}");
                    if (want > 0 && stIds[s] != want) bad.Add($"H8 status slot {s} = {stIds[s]}, want {want}");
                    if (want > 0 && ap[want] is { } r && c.PartBytes(Fold[s], $"{Pfx[s]}_{I(r, "modelId")}_{(I(r, "modelGender") == 1 && female ? "f" : "m")}.bnd") == null) bad.Add($"H8 slot {s}: no part for ArmorParam {want}");
                }
                // previews: DS1 (its parts + the DS1-space head on DS1's bind pose, DS1 hiding)
                if (outDir != null)
                {
                    var ds1Meshes = new List<Mesh3>();
                    var prot = c.Ds1.Params.Params["EquipParamProtector"];
                    string[] P1 = ["HD", "BD", "AM", "LG"];
                    for (int s = 1; s < 4; s++)
                    {
                        int d1 = I(init, fields[s]);
                        var r = prot[d1 > 0 ? d1 / 1000 * 1000 : 900000 + s * 1000];
                        if (r == null) continue;
                        int pm = I(r, "equipModelId");
                        string? path = new[] { I(r, "equipModelGender") == 0 ? "A" : female ? "F" : "M", "M", "A" }.Select(g => Path.Combine(c.Ds1.Root, "parts", $"{P1[s]}_{g}_{pm:D4}.partsbnd.dcx")).FirstOrDefault(File.Exists);
                        if (path == null) continue;
                        var b3 = BND3.Read(path);
                        ds1Meshes.AddRange(Ds1Part(FLVER2.Read(b3.Files.First(x => x.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes), ArmorPorter.Ds1Textures(c.Ds1.Root, b3), info.Hidden.ToHashSet(), null, out _));
                    }
                    var headMeshes = Ds1Part(ds1Head, ds1Tex, info.Hidden.ToHashSet(), null, out _);
                    ds1Meshes.AddRange(headMeshes);
                    ds1Body = Render(ds1Meshes, PW, PH).Img;
                    var hc = Centre(headMeshes);
                    ds1Front = RenderBox(ds1Meshes, 260, hc, 0.42f, false);
                    ds1Side = RenderBox(ds1Meshes, 260, hc, 0.42f, true);
                }
            }
            else
            {
                female = npc.Female ?? false;
                int model = 4500 + int.Parse(part.ModelName[1..]) % 500;
                if (c.PartBytes("body", $"bd_{model}_{(female ? "f" : "m")}.bnd") is not { } bb) bad.Add($"chr NPC body part bd_{model} missing");
                else
                {
                    var flv = FLVER2.Read(BND4.Read(bb).Files.First(x => x.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase)).Bytes);
                    int nv = flv.Meshes.Sum(m => m.Vertices.Count);
                    if (nv < 1000) bad.Add($"chr NPC body part bd_{model} has {nv} vertices (the whole DS1 model incl. its head expected)");
                    ok.Add($"chr body bd_{model}: {flv.Meshes.Count} meshes / {nv} verts (DS1's own model with its head)");
                }
                if (outDir != null)
                {
                    var bnd = BND3.Read(Path.Combine(c.Ds1.Root, "chr", part.ModelName + ".chrbnd.dcx"));
                    var f = FLVER2.Read(bnd.Files.First(x => x.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
                    var tex = ArmorPorter.Ds1Textures(c.Ds1.Root, bnd);
                    var bhd = bnd.Files.FirstOrDefault(x => x.Name.EndsWith(".chrtpfbhd", StringComparison.OrdinalIgnoreCase));
                    string bdt = Path.Combine(c.Ds1.Root, "chr", part.ModelName + ".chrtpfbdt");
                    if (bhd != null && File.Exists(bdt))
                        foreach (var bf in BXF3.Read(bhd.Bytes, File.ReadAllBytes(bdt)).Files)
                            foreach (var x in TPF.Read(DCX.Is(bf.Bytes) ? DCX.Decompress(bf.Bytes) : bf.Bytes).Textures) tex[x.Name] = x;
                    var npcRow = c.Ds1.Params.Params["NpcParam"][part.NPCParamID]!;
                    var hiddenMasks = Enumerable.Range(0, 32).Where(k => npcRow.Cells.FirstOrDefault(x => x.Def.InternalName == $"modelDispMask{k}") is { } cell && Convert.ToInt32(cell.Value) == 0).ToHashSet();
                    var dm = Ds1Part(f, tex, hiddenMasks, null, out _);
                    ds1Body = Render(dm, PW, PH).Img;
                    var top = HeadOf(dm);
                    ds1Front = RenderBox(dm, 260, top, 0.42f, false);
                    ds1Side = RenderBox(dm, 260, top, 0.42f, true);
                }
            }
            if (outDir != null)
            {
                var om = Ours(c, genIds.Select(a => Math.Max(0, a)).ToArray(), female, true).Meshes;
                var oc = HeadOf(om);
                var panels = new List<Image> { ds1Body ?? new Image(PW, PH), Render(om, PW, PH).Img, ds1Front ?? new Image(260, 260), RenderBox(om, 260, oc, 0.42f, false), ds1Side ?? new Image(260, 260), RenderBox(om, 260, oc, 0.42f, true) };
                // the head panels are 260 tall, the body ones 300: pad to one strip
                string img = $"{npc.Ds1Map[..6]}_{part.EntityID}_{part.NPCParamID}.png";
                Png.Save(Strip(panels), Path.Combine(outDir, "npcs", img));
                html.Add($"<div><img src='npcs/{img}'><span class='{(bad.Count > 0 ? "bad" : "")}'>{System.Net.WebUtility.HtmlEncode($"{name} ({npc.Ds1Map}, entity {part.EntityID}, {part.ModelName}) {(bad.Count == 0 ? "OK" : "PROBLEMS: " + string.Join("; ", bad))}; {string.Join("; ", ok)}")}</span></div>");
            }
            if (bad.Count > 0) problems++;
            Console.WriteLine($"  {(bad.Count == 0 ? "OK  " : "FAIL")} {name} ({npc.Ds1Map}, {part.EntityID}, {part.ModelName}, {(female ? "f" : "m")}): {string.Join("; ", bad.Concat(ok))}");
        }
        // every pack NPC row is one the check knows
        foreach (var r in gp.Rows.Where(r => r.ID is >= 900_000 and < 1_000_000 && ds2.Params["NPCGenerateParam"][r.ID] == null))
            if (!seenIds.Contains(r.ID)) { problems++; Console.WriteLine($"  FAIL NPCGenerateParam {r.ID} ({c.Mine.RowName("NPCGenerateParam", r.ID)}): an NPC no placed-NPC table names (route bypasses NpcPorter: no N-HEAD1 / N-OUT1 check)"); }
        if (outDir != null) File.WriteAllLines(Path.Combine(outDir, "npc-heads.html"), html);
        Console.WriteLine($"npc-head-check {packDir}: {npcs} placed NPCs, {problems} with problems{(outDir != null ? $" → {outDir}" : "")}");
        return problems == 0 ? 0 : 2;
    }

    /// <summary>DS2's Head bone in the player skeleton's bind pose (world).</summary>
    static Vector3 HeadBone(PlayerRig rig)
    {
        var skel = new FLVER2 { Nodes = rig.Ds2Skeleton.Select(n => new FLVER.Node(n)).ToList() };
        var w = ChrCompare.World(skel);
        int i = skel.Nodes.FindIndex(n => n.Name == "Head");
        return w[i].Translation;
    }

    static Vector3 Centre(List<Mesh3> ms)
    {
        var all = ms.SelectMany(m => m.Pos).ToList();
        if (all.Count == 0) return new Vector3(0, 1.6f, 0);
        var mn = all.Aggregate(new Vector3(float.MaxValue), Vector3.Min); var mx = all.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        return (mn + mx) / 2;
    }

    /// <summary>The head region of a whole figure: the centre of the vertices above 88 % of its height.</summary>
    static Vector3 HeadOf(List<Mesh3> ms)
    {
        var all = ms.SelectMany(m => m.Pos).ToList();
        if (all.Count == 0) return new Vector3(0, 1.6f, 0);
        float top = all.Max(p => p.Y), bot = all.Min(p => p.Y);
        var head = all.Where(p => p.Y > bot + (top - bot) * 0.86f).ToList();
        var mn = head.Aggregate(new Vector3(float.MaxValue), Vector3.Min); var mx = head.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        return (mn + mx) / 2;
    }

    /// <summary>A close-up: <paramref name="size"/> px square, <paramref name="metres"/> wide around <paramref name="centre"/>, front
    /// (camera on the face side) or from the figure.s right.</summary>
    static Image RenderBox(List<Mesh3> meshes, int size, Vector3 centre, float metres, bool side)
    {
        bool facesMinusZ = true; // both bind poses face −Z (DS1 c0000 and DS2 c0001 parts: PlayerRig keeps the facing; the full-body panels agree)
        Vector3 Xf(Vector3 p)
        {
            var q = p - centre;
            if (facesMinusZ) q = new Vector3(-q.X, q.Y, -q.Z); // turn to face +Z
            if (side) q = new Vector3(-q.Z, q.Y, q.X);        // camera on the figure's right
            return q;
        }
        var img = new Image(size, size);
        var depth = new float[size * size];
        var mask = new bool[size * size];
        Array.Fill(depth, float.MinValue);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = new Rgba(38, 38, 46, 255);
        float scale = size / metres;
        var light = Vector3.Normalize(new Vector3(-0.4f, 0.5f, 1f));
        foreach (var m in meshes)
        {
            var q = m.Pos.Select(Xf).ToArray();
            // camera looks along −Z at the +Z face: screen x = −x (the figure's right on the left, as a viewer sees a face)
            var px = q.Select(p => new Vector3(size / 2f - p.X * scale, size / 2f - p.Y * scale, p.Z)).ToArray();
            var nm = m.Nrm.Select(n => { var t = facesMinusZ ? new Vector3(-n.X, n.Y, -n.Z) : n; return side ? new Vector3(-t.Z, t.Y, t.X) : t; }).ToArray();
            var mm = m with { Nrm = nm };
            for (int i = 0; i + 2 < m.Tris.Length; i += 3)
            {
                int a = m.Tris[i], b = m.Tris[i + 1], cc = m.Tris[i + 2];
                if (a >= px.Length || b >= px.Length || cc >= px.Length) continue;
                Tri(img, depth, mask, px[a], px[b], px[cc], mm, a, b, cc, light);
            }
        }
        return img;
    }
}
