using System.Numerics;
using Lordran.Tools.Formats;
using Lordran.Tools.Textures;
using SoulsFormats;
using static Lordran.Tools.Build.ParamCells;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// DS1 armour → DS2 armour (session 8, track npc: first for the sets Firelink's and the Asylum's NPCs wear; the same
/// code ports any DS1 set). Duplicates use DS2's item (item map, plus <see cref="Ds2Duplicate"/> for pieces DS2
/// renamed: DS1 "Elite Knight Gauntlets" = DS2 "Elite Knight Gloves" 21060102). A DS1-only piece becomes:
/// <list type="bullet">
/// <item>ids: DS2 model = 4000 + DS1 id / 10000 (DS2 armour models use 1001–3340 and 5040–9950, 4000–4999 is free;
/// DS1 armour ids are set·10000 + piece·1000 with ≤ 4 pieces per set, highest set 264), ArmorParam
/// 10,000,000 + model·1000 + 100 + piece (piece = DS1 id / 1000 % 10: 0 head, 1 body, 2 arms, 3 legs, as DS2's own
/// 11060100…103), ItemParam = ArmorParam + 10,000,000 (DS2's item/armour offset). Chain Armor 171000 → model 4017,
/// ArmorParam 14017101, item 24017101.</item>
/// <item>rows: copies of the nearest shared piece's (same slot, closest DS1 physical defence: DS2's calibration for a
/// piece of that weight class): ItemParam, ArmorParam (model, gender, weight scaled by the template's DS2/DS1 weight
/// ratio), ArmorReinforceParam (the template's defences), the template's EquipInterfereParam row (what it hides on
/// other parts); DS1 name, description and icon.</item>
/// <item>parts: DS1 parts\{HD,BD,AM,LG}_{M,F,A}_{equipModelId}.partsbnd.dcx → DS2 model/parts/{head,body,arm,leg}/
/// {xx}_{model}_{m,f}.bnd and _l.bnd (+ the partslq copy), re-bound to DS2's player rig by <see cref="PlayerRig"/>;
/// DS1 gender 3 (separate M/F files) → DS2 modelGender 1 (_m and _f), 0 ("A") → 0 (one _m file for both).</item>
/// </list>
/// Textures (measured with tex-stats, session 8): DS2 part diffuse DXT1 (DXT5/1-bit when alpha-tested), normal DXT5nm
/// (format 23), specular packed like DS2's map specular (R intensity 11–50, G gloss 33–88, B ≈ 123); DSR's part
/// specular is R ≈ 193-202, G 0, B 46-71 (two channels) → R × 0.2, G = DS1 B, B = 123 (provisional). DS2 names every
/// texture after its part ("BD_4017_M_1_d"): our names are part prefix + model + the DS1 texture's own suffix, so no
/// texture name is shared between two part files (session 6's crash was a texture name owned by two resources).
/// </summary>
public sealed class ArmorPorter
{
    public enum Slot { Head = 0, Body = 1, Arm = 2, Leg = 3 }
    static readonly string[] Prefix = ["HD", "BD", "AM", "LG"];
    static readonly string[] Folder = ["head", "body", "arm", "leg"];
    /// <summary>DS2 template part per slot: no cloth (session 5: a template's cloth without its meshes crashed DS2),
    /// one skinned P[Ibl][DSB] layout; only header, dummies, TPF/ACB shapes are taken from them.</summary>
    static readonly string[] TemplatePart = ["head/hd_1060_m", "body/bd_1450_m", "arm/am_1060_m", "leg/lg_1060_m"];
    const string SkinTemplatePart = "arm/am_1001_m"; // DS2's bare arm: Human[Skin][AO_0], layout with Tangent1/UV1

    public static int Model(int ds1Id) => 4000 + ds1Id / 10000;
    public static Slot SlotOf(int ds1Id) => (Slot)(ds1Id / 1000 % 10);
    public static int ArmorParamId(int ds1Id) => 10_000_000 + Model(ds1Id) * 1000 + 100 + ds1Id / 1000 % 10;
    public static int ItemId(int ds1Id) => ArmorParamId(ds1Id) + 10_000_000;

    readonly Ds1Source ds1;
    readonly ParamStore ds2;
    readonly Pack pack;
    readonly PlayerRig rig;
    readonly Dictionary<int, (int Ds2Item, string How)> done = new();
    readonly List<(string DsName, int Ds2Item)> shared;

    public ArmorPorter(Ds1Source ds1, ParamStore ds2, Pack pack, PlayerRig rig)
    {
        this.ds1 = ds1; this.ds2 = ds2; this.pack = pack; this.rig = rig;
        shared = File.ReadLines(Commands.ItemMap.DefaultOut).Skip(1).Select(l => l.Split('\t'))
            .Where(c => c[0] == "EquipParamProtector" && c[4] == "shared" && c[5].Length > 0).Select(c => (c[1], int.Parse(c[5]))).ToList();
    }

    /// <summary>The DS2 ArmorParam id for a DS1 armour id (upgrade level ignored): DS2's for a duplicate, else ported.</summary>
    public int ArmorParam(int ds1Id) => Resolve(ds1Id).Ds2Item - 10_000_000;

    /// <summary>DS2 item for a DS1 armour piece (upgrades ignored): item map / renamed duplicate → DS2's, else ported.</summary>
    public (int Ds2Item, string How) Resolve(int ds1Id)
    {
        int baseId = ds1Id / 1000 * 1000;
        if (done.TryGetValue(baseId, out var hit)) return hit;
        var map = File.ReadLines(Commands.ItemMap.DefaultOut).Skip(1).Select(l => l.Split('\t'))
            .FirstOrDefault(c => c[0] == "EquipParamProtector" && c[1] == baseId.ToString());
        (int, string) result;
        if (map != null && map[4] == "shared" && int.TryParse(map[5], out int ds2Id)) result = (ds2Id, $"shared: DS2 {ds2Id} {map[6]}");
        else if (Ds2Duplicate(baseId, ds1.Text("Armor_name_", baseId)) is { } dup) result = (dup.Id, $"renamed duplicate: DS2 {dup.Id} {dup.Name}");
        else result = (Port(baseId), "ported");
        done[baseId] = result;
        pack.Note($"armour {baseId} {ds1.Text("Armor_name_", baseId)}: {result.Item2}");
        return result;
    }

    /// <summary>
    /// DS2's own copy of a DS1 piece DS2 renamed: same set name, the piece word swapped within one class
    /// (arms: Gauntlets/Gloves; legs: Leggings/Boots), and only when another piece of the same DS1 set is already a
    /// name-matched duplicate of a DS2 piece with the candidate's DS2 model (the whole set exists in DS2). Session 8:
    /// "Elite Knight Gauntlets" (DS1 352000) is DS2's "Elite Knight Gloves" (21060102, model 1060 like the shared Elite
    /// Knight Helm 21060100); "Leather Gauntlets" (DS1 172000, set 17 = Chain Helm/Armor/Leggings, none in DS2) is not
    /// DS2's unrelated "Leather Gloves" (21040102): a first version without the set test took it.
    /// </summary>
    public (int Id, string Name)? Ds2Duplicate(int ds1Id, string ds1Name)
    {
        string[][] classes = [["Gauntlets", "Gloves"], ["Leggings", "Boots"]];
        int sp = ds1Name.LastIndexOf(' ');
        if (sp < 0) return null;
        string set = ds1Name[..sp], piece = ds1Name[(sp + 1)..];
        int ModelOf(int item) => ds2.Params["ItemParam"][item] is { } it && ds2.Params["ArmorParam"][I(it, "armorParamId")] is { } a ? I(a, "modelId") : -1;
        var setModels = shared.Where(s => int.Parse(s.DsName) / 10000 == ds1Id / 10000).Select(s => ModelOf(s.Ds2Item)).Where(m => m > 0).ToHashSet();
        foreach (var cls in classes.Where(c => c.Contains(piece, StringComparer.OrdinalIgnoreCase)))
            foreach (string alt in cls.Where(a => !a.Equals(piece, StringComparison.OrdinalIgnoreCase)))
            {
                var row = ds2.Params["ItemParam"].Rows.FirstOrDefault(r => I(r, "armorParamId") > 0 && ds2.RowName("ItemParam", r.ID).Equals($"{set} {alt}", StringComparison.OrdinalIgnoreCase));
                if (row != null && setModels.Contains(ModelOf(row.ID))) return (row.ID, $"{set} {alt}");
            }
        return null;
    }

    /// <summary>
    /// Session 11 (outfit11), rule N-OUT1: the ArmorParam an NPC wears for a DS1 outfit piece: DS1's own mesh always. A ported
    /// piece is its item's; a piece DS2 shares (item map "shared" / renamed duplicate: Oscar's Elite Knight set, Siegmeyer's
    /// Catarina, Havel's, Patches' Black Leather) becomes an NPC-only ArmorParam (rule AR-N: ArmorParam + parts on the ported
    /// ids, no ItemParam) with the DS2 item's own numbers (ArmorParam / ArmorReinforceParam copied from it), so the NPC looks as
    /// in DS1 while the item anyone picks up stays DS2's. No DS1 part (none known) → DS2's piece.
    /// </summary>
    public int NpcArmorParam(int ds1Id, out string how)
    {
        int baseId = ds1Id / 1000 * 1000;
        var r = Resolve(baseId);
        if (r.How == "ported") { how = "ported (DS1's mesh, its item)"; return r.Ds2Item - 10_000_000; }
        if (npcLook.TryGetValue(baseId, out var hit)) { how = hit.How; return hit.Armor; }
        try
        {
            int armor = Port(baseId, npcLookOf: r.Ds2Item) - 10_000_000;
            npcLook[baseId] = (armor, how = $"N-OUT1: DS1's own mesh as NPC-only ArmorParam {armor} (numbers of DS2's {r.How})");
        }
        catch (FileNotFoundException e)
        {
            npcLook[baseId] = (r.Ds2Item - 10_000_000, how = $"{r.How} (N-OUT1 not applied: {e.Message})");
        }
        return npcLook[baseId].Armor;
    }

    readonly Dictionary<int, (int Armor, string How)> npcLook = new();

    /// <param name="npcLookOf">Rule N-OUT1: the DS2 item a shared DS1 piece resolves to; the port is then NPC-only (no ItemParam)
    /// with that item's ArmorParam / ArmorReinforceParam numbers.</param>
    int Port(int ds1Id, int npcLookOf = 0)
    {
        var src = ds1.Params.Params["EquipParamProtector"][ds1Id] ?? throw new KeyNotFoundException($"DS1 armour {ds1Id}");
        var slot = SlotOf(ds1Id);
        int model = Model(ds1Id), armorId = ArmorParamId(ds1Id), itemId = ItemId(ds1Id);
        string name = ds1.Text("Armor_name_", ds1Id);
        if (ds2.Params["ArmorParam"][armorId] != null || ds2.Params["ItemParam"][itemId] != null)
            throw new InvalidOperationException($"armour ids {armorId}/{itemId} already used in DS2");

        // template: the shared piece of the same slot with the nearest DS1 physical defence (then weight); N-OUT1: the DS2 item itself
        var t = npcLookOf > 0 ? (Ds1: src, Ds2Item: npcLookOf) : shared.Select(s => (Ds1: ds1.Params.Params["EquipParamProtector"][int.Parse(s.DsName)], s.Ds2Item))
            .Where(s => s.Ds1 != null && SlotOf(s.Ds1.ID) == slot && ds2.Params["ItemParam"][s.Ds2Item] is { } it && I(it, "armorParamId") > 0)
            .OrderBy(s => Math.Abs(Get(s.Ds1!, "defensePhysics") - Get(src, "defensePhysics")) + Math.Abs(Get(s.Ds1!, "weight") - Get(src, "weight")))
            .First();
        var tItem = ds2.Params["ItemParam"][t.Ds2Item]!;
        var tArmor = ds2.Params["ArmorParam"][I(tItem, "armorParamId")]!;
        var tReinforce = ds2.Params["ArmorReinforceParam"][I(tArmor, "armorReinforceId")]!;

        // parts
        int gender = I(src, "equipModelGender");
        int ds1Model = I(src, "equipModelId");
        var genders = new List<(string Ds1, string Ds2)>();
        if (gender == 0) genders.Add(("A", "m"));
        else
        {
            if (File.Exists(PartPath(slot, "M", ds1Model))) genders.Add(("M", "m"));
            if (File.Exists(PartPath(slot, "F", ds1Model))) genders.Add(("F", "f"));
        }
        if (genders.Count == 0) throw new FileNotFoundException($"no DS1 part for {name} ({Prefix[(int)slot]}_?_{ds1Model:D4})");
        foreach (var (g1, g2) in genders)
            PortPart(slot, g1, ds1Model, model, g2);

        // rows
        var armor = pack.AddRow("ArmorParam", tArmor, armorId, name);
        Set(armor, "armorId", armorId);
        Set(armor, "modelId", model);
        Set(armor, "modelGender", genders.Any(g => g.Ds2 == "f") ? 1 : 0);
        Set(armor, "armorReinforceId", armorId);
        Set(armor, "iconId", 0);
        double weightRatio = npcLookOf > 0 ? 1 : Get(ds1.Params.Params["EquipParamProtector"][int.Parse(shared.First(s => s.Ds2Item == t.Ds2Item).DsName)]!, "weight") is double tw && tw > 0
            ? Get(tArmor, "weight") / tw : 1;
        if (npcLookOf == 0) Set(armor, "weight", Math.Round(Get(src, "weight") * weightRatio, 1)); // N-OUT1: the DS2 item's weight as is
        pack.AddRow("ArmorReinforceParam", tReinforce, armorId, name);
        if (npcLookOf > 0)
        {
            pack.Note($"armour {ds1Id} {name} (rule N-OUT1: an NPC wears DS1's mesh; the item is DS2's {npcLookOf} {ds2.RowName("ItemParam", npcLookOf)}) → NPC-only ArmorParam {armorId} (its numbers), model {model} ({string.Join("/", genders.Select(g => g.Ds2))})");
            return itemId;
        }
        // session 11 (track ret11), rule AR-N: a DS1 piece without a DS1 name (Armor_name_ empty: an NPC's outfit that DS1 never
        // makes an item, hollow Oscar's 2350000-2353000) is the NPC's ArmorParam + parts only: no ItemParam, item texts or icon
        if (string.IsNullOrWhiteSpace(name))
        {
            pack.Note($"armour {ds1Id} (no DS1 name: an NPC outfit, rule AR-N) → ArmorParam {armorId} only, model {model} ({string.Join("/", genders.Select(g => g.Ds2))}); template {t.Ds2Item} {ds2.RowName("ItemParam", t.Ds2Item)}");
            return itemId;
        }
        var item = pack.AddRow("ItemParam", tItem, itemId, name);
        Set(item, "iconId", itemId);
        Set(item, "armorParamId", armorId);
        pack.SetText("itemname", itemId, name);
        pack.SetText("detailedexplanation", itemId, ds1.LoreText("Armor_long_desc_", ds1Id));
        WeaponPorter.PortIcon(ds1, pack, I(src, gender == 0 || genders[0].Ds1 == "M" ? "iconIdM" : "iconIdF"), itemId, I(tItem, "iconId"));
        pack.Note($"armour {ds1Id} {name} → item {itemId}, ArmorParam {armorId}, model {model} ({string.Join("/", genders.Select(g => g.Ds2))}); template {t.Ds2Item} {ds2.RowName("ItemParam", t.Ds2Item)}, weight ×{weightRatio:0.##}");
        Console.WriteLine($"  armour {ds1Id} {name} → item {itemId}, model {model}; template {t.Ds2Item} {ds2.RowName("ItemParam", t.Ds2Item)}");
        return itemId;
    }

    string PartPath(Slot slot, string g, int ds1Model) => Path.Combine(ds1.Root, "parts", $"{Prefix[(int)slot]}_{g}_{ds1Model:D4}.partsbnd.dcx");

    /// <summary>Converted parts written so far (pack paths), for the offline checks.</summary>
    public List<string> Parts { get; } = new();

    /// <summary>A converted part waiting for its set's ACB (<see cref="Flush"/>).</summary>
    sealed record PendingPart(Slot Slot, string Part, byte[] Flv, Dictionary<string, (byte[] Bytes, byte Format)> Textures, BND4 Template);
    /// <summary>Parts per set ("FB_4017_M"): DS2 ships one ACB per set, listing every part's model and textures, in each
    /// of the set's part binders (FB_1060_M.acb is the same 4,584 bytes in hd/bd/am/lg_1060_m).</summary>
    readonly Dictionary<string, List<PendingPart>> sets = new();

    void PortPart(Slot slot, string g1, int ds1Model, int model, string g2)
    {
        string ds1Name = $"{Prefix[(int)slot]}_{g1}_{ds1Model:D4}";
        var bnd = BND3.Read(PartPath(slot, g1, ds1Model));
        var flver = FLVER2.Read(bnd.Files.First(f => f.Name.EndsWith(ds1Name + ".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
        var tex = Ds1Textures(ds1.Root, bnd);
        string part = $"{Prefix[(int)slot]}_{model}_{g2.ToUpperInvariant()}";
        AddPart(flver, ds1Name, part, slot, tex, ds1Groups: true);
    }

    /// <summary>Session 11 (outfit11), rule AR8: (DS1 display group, slot) → DS2 culling group (Mappings\armor-culling.tsv).</summary>
    static readonly Lazy<Dictionary<(int Group, int Slot), int>> Culling = new(ArmorCulling.Load);

    /// <summary>The textures of a DS1 parts/chr binder plus DS1's shared skin textures (parts\Common_Body.tpf:
    /// BD_M_body, FC_M_face … that DS1's skin materials name).</summary>
    public static Dictionary<string, TPF.Texture> Ds1Textures(string dsrRoot, IBinder bnd)
    {
        var tex = new Dictionary<string, TPF.Texture>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase)))
            foreach (var x in TPF.Read(f.Bytes).Textures) tex[x.Name] = x;
        foreach (var x in TPF.Read(Path.Combine(dsrRoot, "parts", "Common_Body.tpf")).Textures) tex.TryAdd(x.Name, x);
        return tex;
    }

    /// <summary>Converts one DS1 player-rig model into a DS2 part of a set (written by <see cref="Flush"/>). A FLVER
    /// without meshes gives an empty part (one invisible degenerate triangle: DS2's own parts always have a mesh).
    /// <paramref name="rigOverride"/>: a rig whose DS1 side is the model's own skeleton (<see cref="PlayerRig.WithDs1Skeleton"/>).</summary>
    /// <param name="ds1Groups">Session 11 (outfit11), rule AR8: the model is a DS1 armour part whose "#NN#" materials are equipment
    /// display groups (a chr NPC's "#NN#" are modelDispMask groups, handled by <paramref name="keep"/>).</param>
    public void AddPart(FLVER2 src, string ds1Name, string part, Slot slot, Dictionary<string, TPF.Texture> tex, Func<FLVER2.Mesh, FLVER2.Material, bool>? keep = null,
        PlayerRig? rigOverride = null, bool ds1Groups = false)
    {
        var p = BuildPart(src, ds1Name, part, slot, tex, out string report, keep, rigOverride, ds1Groups);
        string fb = $"FB_{part[3..]}";
        (sets.TryGetValue(fb, out var l) ? l : sets[fb] = new()).RemoveAll(x => x.Part == part);
        sets[fb].Add(p);
        pack.Note($"part {ds1Name} → {part}: {report}");
    }

    /// <summary>Writes every converted part's binders (main and _l, gamedata + partslq) with its set's ACB. Safe to call
    /// again after more parts of a set were added (the set's binders are rewritten).</summary>
    public void Flush()
    {
        foreach (var (fb, parts) in sets)
        {
            byte[] acb = Acb(parts[0].Template.Files.First(f => f.Name.EndsWith(".acb", StringComparison.OrdinalIgnoreCase)).Bytes, fb,
                parts.Select(p => p.Part), parts.SelectMany(p => p.Textures.Keys).Distinct());
            foreach (var p in parts)
                foreach (bool low in new[] { false, true })
                {
                    var t = p.Template;
                    var b = new BND4 { Version = t.Version, Format = t.Format, Unicode = t.Unicode, Extended = t.Extended, Unk04 = t.Unk04, Unk05 = t.Unk05, BigEndian = t.BigEndian, BitBigEndian = t.BitBigEndian };
                    var tpfTemplate = TPF.Read(t.Files.First(f => f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase)).Bytes);
                    int id = 0;
                    b.Files.Add(new BinderFile(Binder.FileFlags.Flag1, id++, low ? $"{p.Part}_L.flv" : $"{p.Part}.flv", p.Flv));
                    foreach (var (n, (bytes, format)) in p.Textures)
                    {
                        // DS2's _l binders hold half-size copies under the same texture names (files NAME_L.tpf); ours
                        // keep the full-size pixels, as WeaponPorter's _l binders do (verified in game): a name with two
                        // pixel sets is what tex-collide flags (session 6's crash: one name, two resources)
                        byte[] px = bytes;
                        var tpf = new TPF { Platform = tpfTemplate.Platform, Encoding = tpfTemplate.Encoding, Flag2 = tpfTemplate.Flag2 };
                        tpf.Textures.Add(new TPF.Texture(n, format, 0, px, TPF.TPFPlatform.PC) { Mipmaps = (byte)Ds2Tex.MipCount(px) });
                        b.Files.Add(new BinderFile(Binder.FileFlags.Flag1, id++, low ? $"{n}_L.tpf" : $"{n}.tpf", tpf.Write()));
                    }
                    b.Files.Add(new BinderFile(Binder.FileFlags.Flag1, id++, $"{fb}.acb", acb));
                    string file = $"{p.Part.ToLowerInvariant()}{(low ? "_l" : "")}.bnd";
                    byte[] bytesOut = b.Write();
                    pack.AddFile("gamedata", $"model/parts/{Folder[(int)p.Slot]}/{file}", bytesOut);
                    pack.AddFile("partslq", $"model_lq/parts/{Folder[(int)p.Slot]}/{file}", bytesOut);
                    if (!Parts.Contains($"gamedata/model/parts/{Folder[(int)p.Slot]}/{file}")) Parts.Add($"gamedata/model/parts/{Folder[(int)p.Slot]}/{file}");
                }
        }
    }

    /// <summary>
    /// One DS1 part → a DS2 part FLVER + textures. The FLVER: DS2 template header/dummies, DS2's player skeleton
    /// nodes + one model node, our materials (DS2 MTDs by kind), two layouts (armour, skin), re-bound meshes.
    /// </summary>
    PendingPart BuildPart(FLVER2 src, string ds1Name, string part, Slot slot, Dictionary<string, TPF.Texture> tex, out string report,
        Func<FLVER2.Mesh, FLVER2.Material, bool>? keep = null, PlayerRig? rigOverride = null, bool ds1Groups = false)
    {
        string data = pack.Ds2Data;
        var tBnd = BND4.Read(Path.Combine(data, "model", "parts", TemplatePart[(int)slot] + ".bnd"));
        var tFlv = FLVER2.Read(tBnd.Files.First(f => f.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase)).Bytes);
        var skinFlv = FLVER2.Read(BND4.Read(Path.Combine(data, "model", "parts", SkinTemplatePart + ".bnd")).Files.First(f => f.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase)).Bytes);
        var armourLayout = tFlv.BufferLayouts.First(l => l.Any(m => m.Semantic == FLVER.LayoutSemantic.BoneWeights));
        var skinLayout = skinFlv.BufferLayouts.First(l => l.Any(m => m.Semantic == FLVER.LayoutSemantic.BoneWeights));
        var armourFill = MeanColors(tFlv); var skinFill = MeanColors(skinFlv);
        (int, int, int, FLVER.VertexColor[]) LayoutOf(PlayerRig.Kind k) => k == PlayerRig.Kind.Skin
            ? (Count(skinLayout, FLVER.LayoutSemantic.VertexColor), Count(skinLayout, FLVER.LayoutSemantic.UV), Count(skinLayout, FLVER.LayoutSemantic.Tangent), skinFill)
            : (Count(armourLayout, FLVER.LayoutSemantic.VertexColor), Count(armourLayout, FLVER.LayoutSemantic.UV), Count(armourLayout, FLVER.LayoutSemantic.Tangent), armourFill);
        var meshes = (rigOverride ?? rig).Convert(src, keep, LayoutOf, out string meshReport);
        const int Empty = -1; // the placeholder's "DS1 material"
        if (meshes.Count == 0)
        {
            // an empty part (a chr NPC's unused head/arm/leg slot): one degenerate triangle at the pelvis, alpha-tested
            // with a fully transparent texture, so nothing of DS2's naked body or face shows in that slot
            var (nc, nu, nt, fill) = LayoutOf(PlayerRig.Kind.Alpha);
            int pelvis = rig.Ds2Skeleton.ToList().FindIndex(n => n.Name == "Pelvis");
            var v = new FLVER.Vertex(nu, nt, nc, 1, 1, 1);
            v.Positions.Add(new Vector3(0, 0.97f, 0)); v.Normals.Add(Vector3.UnitZ); v.NormalWs.Add(0);
            for (int i = 0; i < nu; i++) v.UVs.Add(Vector3.Zero);
            for (int i = 0; i < nt; i++) v.Tangents.Add(new Vector4(1, 0, 0, 1));
            for (int i = 0; i < nc; i++) v.Colors.Add(i == 0 ? new FLVER.VertexColor(1f, 1f, 1f, 1f) : fill[Math.Min(i, fill.Length - 1)]);
            v.BoneIndices = new FLVER.VertexBoneIndices(); v.BoneWeights = new FLVER.VertexBoneWeights();
            v.BoneWeights[0] = 1;
            meshes.Add(new PlayerRig.Mesh(Empty, PlayerRig.Kind.Alpha, [pelvis], [v, new FLVER.Vertex(v), new FLVER.Vertex(v)], [0, 1, 2], true));
            meshReport = "empty part (one invisible degenerate triangle)";
        }

        var dst = new FLVER2 { Header = tFlv.Header };
        dst.Nodes = rig.Ds2Skeleton.Select(n => new FLVER.Node(n)).ToList();
        int modelNode = dst.Nodes.Count;
        dst.Nodes.Add(new FLVER.Node { Name = part, ParentIndex = -1, FirstChildIndex = -1, NextSiblingIndex = -1, PreviousSiblingIndex = -1, Scale = Vector3.One,
            BoundingBoxMin = new Vector3(float.MaxValue), BoundingBoxMax = new Vector3(float.MinValue) });
        // the template's dummies sit on skeleton nodes: re-point them by name
        dst.Dummies = tFlv.Dummies.Select(d =>
        {
            var nd = new FLVER.Dummy(d);
            nd.ParentBoneIndex = (short)(d.ParentBoneIndex >= 0 ? dst.Nodes.FindIndex(n => n.Name == tFlv.Nodes[d.ParentBoneIndex].Name) : -1);
            nd.AttachBoneIndex = (short)(d.AttachBoneIndex >= 0 ? dst.Nodes.FindIndex(n => n.Name == tFlv.Nodes[d.AttachBoneIndex].Name) : -1);
            return nd;
        }).ToList();
        dst.BufferLayouts = [armourLayout, skinLayout];
        dst.GXLists = [tFlv.GXLists[0]]; // the template's first list: display id 0 = always drawn (not body-type culled)

        // materials and textures: one DS2 material per DS1 material used
        var texOut = new Dictionary<string, (byte[] Bytes, byte Format)>(StringComparer.OrdinalIgnoreCase);
        string dir = $@"N:\FRPG2\data\Model\parts\FullBody\FB_{part[3..]}\tex\";
        var matIndex = new Dictionary<int, int>();
        dst.Materials = new List<FLVER2.Material>();
        string TexName(string ds1Tex, string suffix)
        {
            // "BD_M_9300_1" in part BD_M_9300 → "BD_4017_M_1"; another part's or a shared texture keeps its name after ours.
            // DS1's own role suffix goes (BD_M_9300_1_n → BD_4017_M_1 + DS2's _n, not _n_n)
            string name = suffix != "_d" && ds1Tex.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? ds1Tex[..^suffix.Length] : ds1Tex;
            string stem = name.StartsWith(ds1Name, StringComparison.OrdinalIgnoreCase) ? name[ds1Name.Length..] : "_" + name;
            return part + stem + suffix;
        }
        var grouped = new List<string>();
        foreach (var m in meshes)
        {
            if (matIndex.ContainsKey(m.Ds1Material)) continue;
            var sm = m.Ds1Material == Empty ? new FLVER2.Material() : src.Materials[m.Ds1Material];
            // rule AR8: a DS1 display group "#NN#" → its DS2 culling group (a GX list like the template's, culling item = group)
            int gx = 0;
            int g1 = ds1Groups && m.Ds1Material != Empty ? ArmorCulling.Ds1Group(sm.Name) : -1;
            if (g1 >= 0)
            {
                if (Culling.Value.TryGetValue((g1, (int)slot), out int g2))
                {
                    gx = dst.GXLists.FindIndex(l => l.Any(i => i.ID == "1" && i.Unk04 == 102 && i.Data.Length > 0 && i.Data[0] == g2));
                    if (gx < 0) { dst.GXLists.Add(ArmorCulling.WithGroup(tFlv.GXLists[0], g2)); gx = dst.GXLists.Count - 1; }
                    grouped.Add($"#{g1}#→{g2}");
                }
                else grouped.Add($"#{g1}# UNMAPPED (always drawn)");
            }
            var dm = new FLVER2.Material($"{part}_{dst.Materials.Count}", PlayerRig.Mtd(m.Kind), 0) { GXIndex = gx, Index = 0 };
            if (m.Ds1Material == Empty) texOut.TryAdd($"{part}_clear_d", (Transparent(), Ds2Tex.FormatDxt5));
            string Slot1(string ds1Slot) => sm.Textures.FirstOrDefault(x => x.ParamName == ds1Slot && !string.IsNullOrEmpty(x.Path)) is { } x ? Path.GetFileNameWithoutExtension(x.Path) : "";
            foreach (var (ds2Slot, ds1Slot, suffix) in new[] { ("g_DiffuseTexture", "g_Diffuse", "_d"), ("g_SpecularTexture", "g_Specular", "_s"), ("g_BumpmapTexture", "g_Bumpmap", "_n") })
            {
                string s = Slot1(ds1Slot);
                string n = m.Ds1Material == Empty && suffix == "_d" ? $"{part}_clear_d" : s.Length > 0 && tex.ContainsKey(s) ? TexName(s, suffix) : $"{part}_flat{suffix}";
                if (!texOut.ContainsKey(n))
                    texOut[n] = s.Length > 0 && tex.TryGetValue(s, out var t) ? suffix switch
                    {
                        "_n" => (Ds2Tex.Normal(t.Headerize()), Ds2Tex.FormatNormal),
                        "_s" => (PartSpecular(t.Headerize()), Ds2Tex.FormatDxt1),
                        _ => Ds2Tex.Color(t.Headerize()),
                    } : suffix switch
                    {
                        "_n" => (Ds2Tex.FlatNormal(), Ds2Tex.FormatNormal),
                        "_s" => (Ds2Tex.Solid(20, 40, 123), Ds2Tex.FormatDxt1),
                        _ => (Ds2Tex.Solid(128, 128, 128), Ds2Tex.FormatDxt1),
                    };
                dm.Textures.Add(new FLVER2.Texture(ds2Slot, dir + n + (suffix == "_s" ? ".pxt" : ".tga"), Vector2.One, 0, 0, 0, 0, 0));
            }
            if (m.Kind != PlayerRig.Kind.Skin)
                dm.Textures.Add(new FLVER2.Texture("g_BreakBumpmapTexture", @"N:\FRPG2\data\Model\parts\Common\tex\WP_damage_metal_n.tga", new Vector2(2, 2), 0, 0, 0, 0, 0));
            matIndex[m.Ds1Material] = dst.Materials.Count;
            dst.Materials.Add(dm);
        }
        dst.Meshes = meshes.Select(m => new FLVER2.Mesh
        {
            MaterialIndex = matIndex[m.Ds1Material],
            NodeIndex = modelNode,
            BoneIndices = m.Palette,
            Dynamic = 1,
            UseBoneWeights = true,
            VertexBuffers = [new FLVER2.VertexBuffer(m.Kind == PlayerRig.Kind.Skin ? 1 : 0)],
            FaceSets = [new FLVER2.FaceSet(FLVER2.FaceSet.FSFlags.None, false, m.Cull, (short)m.Palette.Count, m.Triangles)],
            Vertices = m.Vertices,
        }).ToList();
        var all = dst.Meshes.SelectMany(x => x.Vertices).ToList();
        if (all.Count > 0)
        {
            dst.Header.BoundingBoxMin = all.Aggregate(new Vector3(float.MaxValue), (a, v) => Vector3.Min(a, v.Position));
            dst.Header.BoundingBoxMax = all.Aggregate(new Vector3(float.MinValue), (a, v) => Vector3.Max(a, v.Position));
        }
        byte[] flvBytes = dst.Write();
        var problems = rig.Check(FLVER2.Read(flvBytes));
        if (problems.Count > 0) throw new InvalidDataException($"{part}: " + string.Join("; ", problems.Take(5)));
        report = $"{meshReport}, {dst.Materials.Count} materials ({string.Join(",", meshes.Select(m => m.Kind).Distinct())}), {texOut.Count} textures" +
            (grouped.Count > 0 ? $", AR8 culling groups {string.Join(" ", grouped.Distinct())}" : "");
        return new PendingPart(slot, part, flvBytes, texOut, tBnd);
    }

    /// <summary>DS2 part ACB for a set: the template's model settings, one model asset per part, every texture
    /// (no cloth asset).</summary>
    static byte[] Acb(byte[] template, string fb, IEnumerable<string> parts, IEnumerable<string> textures)
    {
        var acb = ACB.Read(template);
        acb.Assets.Clear();
        foreach (string part in parts)
        {
            var m = ACB.Read(template).Assets.OfType<ACB.Asset.Model>().First(); // a fresh copy of the settings per part
            m.AbsolutePath = $@"N:\FRPG2_64\data\model\parts\FullBody\{fb}\model_sib\{part}.sib";
            m.RelativePath = $"model_sib/{part}.sib";
            acb.Assets.Add(m);
        }
        foreach (string n in textures)
        {
            string file = n + (n.EndsWith("_s") ? ".pxt" : ".tga");
            acb.Assets.Add(new ACB.Asset.Texture { AbsolutePath = $@"N:\FRPG2_64\data\model\parts\FullBody\{fb}\tex\{file}", RelativePath = $"tex/{file}" });
        }
        return acb.Write();
    }

    /// <summary>DSR part specular (R ≈ intensity, G 0, B ≈ gloss) → DS2's packed part specular (see the class notes).</summary>
    static byte[] PartSpecular(byte[] ds1Dds)
    {
        var src = Dds.Read(ds1Dds).DecodeTop();
        var dst = new Image(src.Width, src.Height);
        for (int i = 0; i < src.Pixels.Length; i++)
        {
            var p = src.Pixels[i];
            dst.Pixels[i] = new Rgba((byte)Math.Clamp(Math.Round(p.R * 0.2), 0, 255), p.B, 123, 255);
        }
        return Dds.Encode(dst, BcFormat.BC1);
    }

    static int Count(FLVER2.BufferLayout l, FLVER.LayoutSemantic s) => l.Count(m => m.Semantic == s);

    /// <summary>A 4x4 fully transparent DXT5 (alpha-tested away): the empty part's diffuse.</summary>
    static byte[] Transparent()
    {
        var img = new Image(4, 4);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = new Rgba(0, 0, 0, 0);
        return Dds.Encode(img, BcFormat.BC3, mipmaps: false);
    }

    /// <summary>Mean of each vertex colour channel set over a template's skinned meshes (index 0 = white).</summary>
    static FLVER.VertexColor[] MeanColors(FLVER2 f)
    {
        var verts = f.Meshes.Where(m => m.Vertices.Count > 0 && m.Vertices[0].BoneWeights[0] > 0 || m.BoneIndices.Count > 0).SelectMany(m => m.Vertices).ToList();
        int n = verts.Count == 0 ? 0 : verts.Max(v => v.Colors.Count);
        var r = new FLVER.VertexColor[Math.Max(1, n)];
        r[0] = new FLVER.VertexColor(1f, 1f, 1f, 1f);
        for (int i = 1; i < n; i++)
        {
            var vs = verts.Where(v => v.Colors.Count > i).Select(v => v.Colors[i]).ToList();
            r[i] = new FLVER.VertexColor(vs.Average(c => c.A), vs.Average(c => c.R), vs.Average(c => c.G), vs.Average(c => c.B));
        }
        return r;
    }
}
