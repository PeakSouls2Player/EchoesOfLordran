using System.Numerics;
using Lordran.Tools.Formats;
using Lordran.Tools.Textures;
using SoulsFormats;
using static Lordran.Tools.Build.ParamCells;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// Session 11 (track npc12), rule N-HEAD1: a DS1 human (c0000) NPC's head as DS1 draws it, baked into one NPC-only DS2 head part.
/// <para>Why (user report s11: "one of the NPCs in Firelink didn't have a head"): DS2 has no head mesh for a bare player head
/// (ArmorParam 11001100 "[Body] Head" = model 1001, no hd_1001 part): the head is the face model alone. NpcPorter's interim
/// dressing gave bare-headed NPCs (Crestfallen Warrior, Petrus) NPCGenerateParam faceType 1 + facePresetNo 3 + equipHeadId -1.
/// DS2's NPCGenerateParam loader FUN_14035a650 never reads facePresetNo; faceType ≠ 0 with equipType 0 selects face mode 4
/// (FUN_1403493c0: face id forced 0, the mode of an NPC without a row, dressed from summon data), and no vanilla NPC combines
/// it with a bare head (all 35 faceType-1 rows, phantoms, wear helms; the bare-headed Rosabeth / Carhillion use faceType 0
/// with their own FC_7630 / FC_7660 face model). So no face model loads: no head.</para>
/// <para>DS1 draws a c0000 head from five pieces (DSR parts + facegen\FaceGen.fgbnd): the helm (if any), FC_{g}_0000 (back of the
/// head and ears, material "#15#", FC_{g}_face textures), the FaceGen face (FG_A_0000 = face.flver: "#12#" face, "#13#"
/// inner face, "#12#" eyes) shaped by the NPC's FaceGenParam (<see cref="Ds1FaceGen"/>, rule N-FACE1), and the hair
/// HR_{g}_{hairStyle:D4} ("#6#"; no file = bald: DS1 has no HR_M_0009, Petrus' style 9). Any worn piece's
/// EquipParamProtector.invisibleFlagNN (bare-body rows 900000-903000 for empty slots) hides group NN.</para>
/// <para>Rule N-HEAD1: those pieces, with DS1's hiding for the NPC's fixed outfit applied, are one DS1-space model re-bound to
/// DS2's player rig (<see cref="PlayerRig"/>) as part HD_{8000 + NpcParam % 1000}_M (modelGender 0: one file for either sex)
/// with an NPC-only ArmorParam (10,000,000 + model·1000 + 100, numbers copied from the helm's / DS2's bare head; rule AR-N: no
/// item) that NPCGenerateParam.equipHeadId and NpcPlayerStatusParam.headArmor point at. Its EquipInterfereParam row (same id)
/// = the helm's own row (AR8) or DS2's bare-head row, plus every DS2 face/hair culling group hidden, and NPCGenerateParam
/// faceType 0 / faceId -1 (no DS2 face model: 18 vanilla rows): DS2 draws no face of its own over DS1's. The helm's meshes keep
/// their AR8 groups (bodies hiding a helm rim behave as in DS1). Colours by DS1's shaders (FRPG_Phn_DifSpcBmpMul: face =
/// lerp(FaceGen colour at UV0, detail map FG_{g}_face_org_d at UV1, vertex alpha), baked into the detail UV; eyes
/// FRPG_Phn_FaceEye: rgb · lerp(1, eyeColour, texture alpha); hair: Ps_Hair g_useHairColor → the mask texture tinted to
/// FaceGenParam hairColor (mean-preserving)). Items the NPC drops are unchanged (lots).</para>
/// </summary>
public static class NpcHead
{
    public const int ModelBase = 8000;
    static readonly string[] Prefix = ["HD", "BD", "AM", "LG"];

    /// <summary>The head part's DS2 model for a DS1 c0000 NPC (NpcParam 6000-6999, the c0000 NPC rows): 8000-8999 (no vanilla
    /// part model uses 8xxx; armour sets 4000-4264, chr NPC bodies 4500+).</summary>
    public static int Model(int ds1NpcParam) => ds1NpcParam is >= 6000 and < 7000 ? ModelBase + ds1NpcParam % 1000
        : throw new InvalidDataException($"rule N-HEAD1: DS1 NpcParam {ds1NpcParam} outside the c0000 NPC rows 6000-6999 (head model range)");

    public static int ArmorId(int model) => 10_000_000 + model * 1000 + 100;

    /// <summary>DS1 groups the NPC's worn set hides (each slot's piece, or DS1's bare row of an empty slot).</summary>
    public static HashSet<int> Ds1Hidden(Ds1Source ds1, PARAM.Row init)
    {
        var prot = ds1.Params.Params["EquipParamProtector"];
        string[] fields = ["equip_Helm", "equip_Armer", "equip_Gaunt", "equip_Leg"];
        var hidden = new HashSet<int>();
        for (int s = 0; s < 4; s++)
        {
            int id = I(init, fields[s]);
            var r = prot[id > 0 ? id / 1000 * 1000 : 900000 + s * 1000];
            if (r != null) hidden.UnionWith(ArmorCulling.Ds1Flags(r));
        }
        return hidden;
    }

    /// <summary>What a baked head holds (for notes and the checks).</summary>
    public sealed record Info(int FaceGenId, int HairStyle, string? HairPart, bool Helm, bool Face, bool Back, bool Hair, bool Eyes, double MorphMm, IReadOnlyCollection<int> Hidden);

    /// <summary>The NPC's DS1 head in DS1's bind-pose space: one FLVER (helm + back of head + FaceGen face/eyes + hair, DS1's
    /// hiding applied to the head pieces), its textures (DS1's, plus the baked face / eye / hair colour), and what it holds.</summary>
    public static FLVER2 Ds1Head(Ds1Source ds1, PARAM.Row init, out Dictionary<string, TPF.Texture> tex, out Info info, string key)
    {
        bool female = I(init, "npcPlayerSex") == 0;
        string g = female ? "F" : "M";
        var hidden = Ds1Hidden(ds1, init);
        int fgId = I(init, "npcPlayerFaceGenId");
        var fgRow = ds1.Params.Params["FaceGenParam"][fgId] ?? ds1.Params.Params["FaceGenParam"][1000]!; // 1000 "Common Face"
        tex = new Dictionary<string, TPF.Texture>(StringComparer.OrdinalIgnoreCase);
        var dst = new FLVER2();
        dst.Header.Version = 0x2000C;
        string parts = Path.Combine(ds1.Root, "parts");

        // the helm (its own "#NN#" groups stay: AR8 maps them at run time)
        int helm = I(init, "equip_Helm");
        bool hasHelm = false;
        if (helm > 0 && ds1.Params.Params["EquipParamProtector"][helm / 1000 * 1000] is { } hr)
        {
            int model = I(hr, "equipModelId");
            string? path = new[] { I(hr, "equipModelGender") == 0 ? "A" : g, "M", "A" }.Select(x => Path.Combine(parts, $"HD_{x}_{model:D4}.partsbnd.dcx")).FirstOrDefault(File.Exists);
            if (path != null)
            {
                var bnd = BND3.Read(path);
                var f = FLVER2.Read(bnd.Files.First(x => x.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
                foreach (var (k, v) in ArmorPorter.Ds1Textures(ds1.Root, bnd)) tex.TryAdd(k, v);
                Append(dst, f, (_, _) => true, m => m);
                hasHelm = true;
            }
        }
        bool Visible(FLVER2.Material m) => ArmorCulling.Ds1Group(m.Name) is int grp && (grp < 0 || !hidden.Contains(grp));
        // DS1's face/hair groups are decided here (the outfit is fixed): drawn always on our side, so no "#NN#" name reaches AR8
        static FLVER2.Material Plain(FLVER2.Material m, string name, string? mtd = null, Dictionary<string, string>? texSwap = null)
        {
            var c = new FLVER2.Material(name, mtd ?? m.MTD, 0);
            foreach (var t in m.Textures)
            {
                string p = t.Path;
                if (texSwap != null && texSwap.TryGetValue(t.ParamName, out var sw)) p = $@"N:\npc12\{sw}.tga";
                c.Textures.Add(new FLVER2.Texture(t.ParamName, p, Vector2.One, 0, 0, 0, 0, 0));
            }
            return c;
        }

        // back of the head, ears (FC_{g}_0000, "#15#")
        bool back = false;
        string fcPath = Path.Combine(parts, $"FC_{g}_0000.partsbnd.dcx");
        if (File.Exists(fcPath))
        {
            var bnd = BND3.Read(fcPath);
            var f = FLVER2.Read(bnd.Files.First(x => x.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
            foreach (var (k, v) in ArmorPorter.Ds1Textures(ds1.Root, bnd)) tex.TryAdd(k, v);
            back = f.Materials.Any(Visible);
            Append(dst, f, (_, m) => Visible(m), m => Plain(m, "back"));
        }

        // the FaceGen face (rule N-FACE1) and eyes
        var fg = Ds1FaceGen.Load(ds1.Root);
        var geo = Enumerable.Range(0, 50).Select(i => I(fgRow, $"faceGeoData{i:D2}")).ToList();
        var txc = Enumerable.Range(0, 50).Select(i => I(fgRow, $"faceTexData{i:D2}")).ToList();
        var face = fg.Morph(geo, out double morphMm);
        bool faceVisible = false, eyes = false;
        if (face.Meshes.Count > 0)
        {
            var gen = fg.FaceColour(txc, female);
            Image Dec(string n) => Dds.Read(fg.Textures[n].Headerize()).DecodeTop();
            var org = Dec(female ? "FG_f_face_org_d" : "FG_m_face_org_d");
            var genS = Dec("FG_face_s");
            var orgS = Dec("fg_face_org_s");
            // eye colour: rgb · lerp(1, eyeColour, alpha) (FRPG_Phn_FaceEye)
            var eye = Dec("FG_eye");
            var eyeCol = new Vector3(I(fgRow, "eyeColor_R"), I(fgRow, "eyeColor_G"), I(fgRow, "eyeColor_B")) / 255f;
            var eyeOut = new Image(eye.Width, eye.Height);
            for (int i = 0; i < eye.Pixels.Length; i++)
            {
                var p = eye.Pixels[i];
                float a = p.A / 255f;
                var m = Vector3.One + a * (eyeCol - Vector3.One);
                eyeOut.Pixels[i] = new Rgba(B(p.R * m.X), B(p.G * m.Y), B(p.B * m.Z), p.A);
            }
            string eyeName = $"{key}_eye";
            tex[eyeName] = Tex(eyeName, eyeOut, eyeOut.Pixels.Any(p => p.A < 250));
            tex[eyeName + "_s"] = fg.Textures["FG_eye_s"] is { } es ? new TPF.Texture(eyeName + "_s", es.Format, es.Flags1, es.Bytes, TPF.TPFPlatform.PC) : tex[eyeName];
            string nrm = "FG_face_org_n";
            tex[nrm] = fg.Textures[nrm];
            for (int mi = 0; mi < face.Meshes.Count; mi++)
            {
                var mesh = face.Meshes[mi];
                var mat = face.Materials[mesh.MaterialIndex];
                if (!Visible(mat)) continue;
                bool isEye = mat.MTD.Contains("Eye", StringComparison.OrdinalIgnoreCase) || mat.Textures.Any(t => t.ParamName == "g_Diffuse" && t.Path.Contains("FG_eye", StringComparison.OrdinalIgnoreCase));
                FLVER2.Material nm;
                if (isEye)
                {
                    eyes = true;
                    bool alpha = mat.MTD.Contains("Alp", StringComparison.OrdinalIgnoreCase);
                    nm = new FLVER2.Material(alpha ? "eye_alpha" : "eye", alpha ? @"N:\FRPG\npc12\Ps_Eye_Alp.mtd" : @"N:\FRPG\npc12\Ps_Eye.mtd", 0);
                    nm.Textures.Add(new FLVER2.Texture("g_Diffuse", $@"N:\npc12\{eyeName}.tga", Vector2.One, 0, 0, 0, 0, 0));
                    nm.Textures.Add(new FLVER2.Texture("g_Specular", $@"N:\npc12\{eyeName}_s.tga", Vector2.One, 0, 0, 0, 0, 0));
                }
                else
                {
                    faceVisible = true;
                    // the face's two UV sets → the detail UV (UV1) only; colour = lerp(FaceGen colour(UV0), detail(UV1), vertex alpha)
                    bool detailOnly = mesh.Vertices.All(v => v.Colors.Count == 0 || v.Colors[0].A >= 0.99f);
                    string dn = $"{key}_face{mi}";
                    tex[dn] = Tex(dn, detailOnly ? org : BakeLerp(mesh, gen, org), false);
                    tex[dn + "_s"] = Tex(dn + "_s", detailOnly ? orgS : BakeLerp(mesh, genS, orgS), false);
                    nm = new FLVER2.Material($"face{mi}", @"N:\FRPG\npc12\Ps_Body[DSB].mtd", 0); // a skin material (PlayerRig.Kind.Skin)
                    nm.Textures.Add(new FLVER2.Texture("g_Diffuse", $@"N:\npc12\{dn}.tga", Vector2.One, 0, 0, 0, 0, 0));
                    nm.Textures.Add(new FLVER2.Texture("g_Specular", $@"N:\npc12\{dn}_s.tga", Vector2.One, 0, 0, 0, 0, 0));
                    nm.Textures.Add(new FLVER2.Texture("g_Bumpmap", $@"N:\npc12\{nrm}.tga", Vector2.One, 0, 0, 0, 0, 0));
                    foreach (var v in mesh.Vertices)
                        if (v.UVs.Count > 1) v.UVs = [v.UVs[1]];
                }
                foreach (var v in mesh.Vertices)
                    if (v.Colors.Count > 0) v.Colors[0] = new FLVER.VertexColor(1f, 1f, 1f, 1f);
                AppendMesh(dst, face, mesh, nm, n => n == "Head" ? n : "Head"); // eye bones move with the head in a static bake
            }
        }

        // hair (HR_{g}_{style}, "#6#"): the mask tinted to the hair colour
        int style = I(fgRow, "hairStyle");
        string? hairPart = null;
        bool hair = false;
        string hrPath = Path.Combine(parts, $"HR_{g}_{style:D4}.partsbnd.dcx");
        if (File.Exists(hrPath))
        {
            hairPart = $"HR_{g}_{style:D4}";
            var bnd = BND3.Read(hrPath);
            var f = FLVER2.Read(bnd.Files.First(x => x.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
            var htex = ArmorPorter.Ds1Textures(ds1.Root, bnd);
            foreach (var (k, v) in htex) tex.TryAdd(k, v);
            var col = new Vector3(I(fgRow, "hairColor_R"), I(fgRow, "hairColor_G"), I(fgRow, "hairColor_B"));
            var swaps = new Dictionary<string, string>();
            foreach (string d in f.Materials.Select(m => m.Textures.FirstOrDefault(t => t.ParamName == "g_Diffuse")?.Path).OfType<string>().Select(Path.GetFileNameWithoutExtension).OfType<string>().Distinct())
            {
                if (!htex.TryGetValue(d, out var t)) continue;
                var img = Dds.Read(t.Headerize()).DecodeTop();
                var opaque = img.Pixels.Where(p => p.A >= 128).ToList();
                var mean = opaque.Count > 0 ? new Vector3((float)opaque.Average(p => p.R), (float)opaque.Average(p => p.G), (float)opaque.Average(p => p.B)) : new Vector3(128);
                var o = new Image(img.Width, img.Height);
                for (int i = 0; i < img.Pixels.Length; i++)
                {
                    var p = img.Pixels[i];
                    o.Pixels[i] = new Rgba(B(p.R / Math.Max(1, mean.X) * col.X), B(p.G / Math.Max(1, mean.Y) * col.Y), B(p.B / Math.Max(1, mean.Z) * col.Z), p.A);
                }
                string hn = $"{key}_hair{swaps.Count}";
                tex[hn] = Tex(hn, o, true);
                swaps[d] = hn;
            }
            hair = f.Materials.Any(Visible);
            Append(dst, f, (_, m) => Visible(m), m =>
            {
                string? d = m.Textures.FirstOrDefault(t => t.ParamName == "g_Diffuse")?.Path is { } pth ? Path.GetFileNameWithoutExtension(pth) : null;
                return Plain(m, "hair", null, d != null && swaps.TryGetValue(d, out var s) ? new Dictionary<string, string> { ["g_Diffuse"] = s } : null);
            });
        }
        info = new Info(fgRow.ID, style, hairPart, hasHelm, faceVisible, back, hair, eyes, morphMm, hidden);
        return dst;
    }

    static byte B(float v) => (byte)Math.Clamp(MathF.Round(v), 0, 255);

    static TPF.Texture Tex(string name, Image img, bool alpha) =>
        new(name, alpha ? Ds2Tex.FormatDxt5 : Ds2Tex.FormatDxt1, 0, Dds.Encode(img, alpha ? BcFormat.BC3 : BcFormat.BC1), TPF.TPFPlatform.PC);

    /// <summary>Copies a source FLVER's meshes (filtered) into <paramref name="dst"/>: its nodes appended, bone palettes offset.</summary>
    static void Append(FLVER2 dst, FLVER2 src, Func<FLVER2.Mesh, FLVER2.Material, bool> keep, Func<FLVER2.Material, FLVER2.Material> material)
    {
        int nodeBase = dst.Nodes.Count;
        dst.Nodes.AddRange(src.Nodes.Select(n => new FLVER.Node(n)));
        foreach (var m in src.Meshes)
        {
            var mat = src.Materials[m.MaterialIndex];
            if (!keep(m, mat)) continue;
            dst.Materials.Add(material(mat));
            dst.Meshes.Add(new FLVER2.Mesh
            {
                MaterialIndex = dst.Materials.Count - 1, NodeIndex = Math.Max(0, m.NodeIndex) + nodeBase, Dynamic = m.Dynamic,
                BoneIndices = m.BoneIndices.Select(b => b + nodeBase).ToList(), FaceSets = m.FaceSets, VertexBuffers = m.VertexBuffers, Vertices = m.Vertices,
            });
        }
    }

    /// <summary>One mesh with its own material; node names renamed by <paramref name="rename"/>.</summary>
    static void AppendMesh(FLVER2 dst, FLVER2 src, FLVER2.Mesh m, FLVER2.Material mat, Func<string, string> rename)
    {
        int nodeBase = dst.Nodes.Count;
        dst.Nodes.AddRange(src.Nodes.Select(n => new FLVER.Node(n) { Name = rename(n.Name) }));
        dst.Materials.Add(mat);
        dst.Meshes.Add(new FLVER2.Mesh
        {
            MaterialIndex = dst.Materials.Count - 1, NodeIndex = Math.Max(0, m.NodeIndex) + nodeBase, Dynamic = m.Dynamic,
            BoneIndices = m.BoneIndices.Select(b => b + nodeBase).ToList(), FaceSets = m.FaceSets, VertexBuffers = m.VertexBuffers, Vertices = m.Vertices,
        });
    }

    /// <summary>DS1's FaceGen blend baked into the detail UV (UV1): per texel lerp(a(UV0), b(UV1), vertex alpha), rasterised per
    /// triangle in UV1 space (wrapped); texels no triangle covers take b, then a 6-texel dilation of the covered ones.</summary>
    public static Image BakeLerp(FLVER2.Mesh mesh, Image a, Image b)
    {
        int w = b.Width, h = b.Height;
        var outImg = new Image(w, h);
        var covered = new bool[w * h];
        var fs = mesh.FaceSets.FirstOrDefault(f => f.Flags == FLVER2.FaceSet.FSFlags.None) ?? mesh.FaceSets[0];
        var tris = fs.TriangleStrip ? fs.Triangulate(true) : new List<int>(fs.Indices);
        for (int i = 0; i + 2 < tris.Count; i += 3)
        {
            var vs = new[] { mesh.Vertices[tris[i]], mesh.Vertices[tris[i + 1]], mesh.Vertices[tris[i + 2]] };
            if (vs.Any(v => v.UVs.Count < 2)) continue;
            var P = vs.Select(v => new Vector2(v.UVs[1].X * w, v.UVs[1].Y * h)).ToArray();
            float d = (P[1].Y - P[2].Y) * (P[0].X - P[2].X) + (P[2].X - P[1].X) * (P[0].Y - P[2].Y);
            if (Math.Abs(d) < 1e-6f) continue;
            int x0 = (int)MathF.Floor(P.Min(p => p.X)) - 1, x1 = (int)MathF.Ceiling(P.Max(p => p.X)) + 1;
            int y0 = (int)MathF.Floor(P.Min(p => p.Y)) - 1, y1 = (int)MathF.Ceiling(P.Max(p => p.Y)) + 1;
            if (x1 - x0 > w || y1 - y0 > h) continue;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float l1 = ((P[1].Y - P[2].Y) * (px - P[2].X) + (P[2].X - P[1].X) * (py - P[2].Y)) / d;
                    float l2 = ((P[2].Y - P[0].Y) * (px - P[2].X) + (P[0].X - P[2].X) * (py - P[2].Y)) / d;
                    float l3 = 1 - l1 - l2;
                    const float e = -0.02f;
                    if (l1 < e || l2 < e || l3 < e) continue;
                    var uv0 = l1 * Uv(vs[0], 0) + l2 * Uv(vs[1], 0) + l3 * Uv(vs[2], 0);
                    float al = l1 * Alpha(vs[0]) + l2 * Alpha(vs[1]) + l3 * Alpha(vs[2]);
                    var ca = Sample(a, uv0);
                    int wx = ((x % w) + w) % w, wy = ((y % h) + h) % h;
                    var cb = b[wx, wy];
                    outImg[wx, wy] = new Rgba(B(ca.X + (cb.R - ca.X) * al), B(ca.Y + (cb.G - ca.Y) * al), B(ca.Z + (cb.B - ca.Z) * al), 255);
                    covered[wy * w + wx] = true;
                }
        }
        // dilation (bilinear filtering at the islands' borders), then the detail map elsewhere
        for (int it = 0; it < 6; it++)
        {
            var next = (bool[])covered.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (covered[y * w + x]) continue;
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h || !covered[ny * w + nx]) continue;
                        outImg[x, y] = outImg[nx, ny]; next[y * w + x] = true; break;
                    }
                }
            covered = next;
        }
        for (int k = 0; k < covered.Length; k++) if (!covered[k]) outImg.Pixels[k] = b.Pixels[k];
        return outImg;
    }

    static Vector2 Uv(FLVER.Vertex v, int set) => new(v.UVs[set].X, v.UVs[set].Y);
    static float Alpha(FLVER.Vertex v) => v.Colors.Count > 0 ? v.Colors[0].A : 1f;

    /// <summary>Bilinear sample (wrapped) → 0-255 floats.</summary>
    static Vector3 Sample(Image img, Vector2 uv)
    {
        float u = uv.X * img.Width - 0.5f, v = uv.Y * img.Height - 0.5f;
        int x0 = (int)MathF.Floor(u), y0 = (int)MathF.Floor(v);
        float fx = u - x0, fy = v - y0;
        Vector3 P(int x, int y) { var p = img[((x % img.Width) + img.Width) % img.Width, ((y % img.Height) + img.Height) % img.Height]; return new Vector3(p.R, p.G, p.B); }
        return Vector3.Lerp(Vector3.Lerp(P(x0, y0), P(x0 + 1, y0), fx), Vector3.Lerp(P(x0, y0 + 1), P(x0 + 1, y0 + 1), fx), fy);
    }

    /// <summary>Rows waiting for <see cref="FinishRows"/> (after ArmorItems.Finish made the helm's own interfere row).</summary>
    static readonly List<(int Armor, int HelmArmor, bool Female, string Name)> pending = new();

    /// <summary>
    /// Rule N-HEAD1 for one placed c0000 NPC: bakes its DS1 head into part HD_{model}_M (through <paramref name="armour"/>, written by
    /// its Flush) and its NPC-only ArmorParam; returns the ArmorParam id for NPCGenerateParam.equipHeadId / NpcPlayerStatusParam.headArmor.
    /// <paramref name="helmArmor"/>: the ArmorParam the NPC's DS1 helm resolves to (N-OUT1), -1 without a helm.
    /// </summary>
    public static int Build(Pack pack, Ds1Source ds1, ParamStore ds2, ArmorPorter armour, int ds1NpcParam, PARAM.Row init, int helmArmor, List<string> notes)
    {
        int model = Model(ds1NpcParam);
        int armorId = ArmorId(model);
        string part = $"HD_{model}_M";
        if (ds2.Params["ArmorParam"][armorId] != null) throw new InvalidOperationException($"rule N-HEAD1: ArmorParam {armorId} exists in DS2");
        if (File.Exists(Path.Combine(pack.Ds2Data, "model", "parts", "head", part.ToLowerInvariant() + ".bnd"))) throw new InvalidOperationException($"rule N-HEAD1: DS2 has a part {part}");
        string key = $"NPCHEAD_{ds1NpcParam}";
        var head = Ds1Head(ds1, init, out var tex, out var info, key);
        armour.AddPart(head, key, part, ArmorPorter.Slot.Head, tex, ds1Groups: true);
        bool female = I(init, "npcPlayerSex") == 0;
        var template = helmArmor > 0 && pack.Param("ArmorParam")[helmArmor] is { } h ? h : pack.Param("ArmorParam")[11001100 + (female ? 1000 : 0)]!;
        var row = pack.AddRow("ArmorParam", template, armorId, $"[Lordran] N-HEAD1 head of DS1 NpcParam {ds1NpcParam}");
        Set(row, "armorId", armorId);
        Set(row, "modelId", model);
        Set(row, "modelGender", 0);
        Set(row, "interfereId", armorId);
        pending.RemoveAll(p => p.Armor == armorId);
        pending.Add((armorId, helmArmor, female, $"DS1 NpcParam {ds1NpcParam}"));
        notes.Add($"head (N-HEAD1): part {part} = {(info.Helm ? $"helm {I(init, "equip_Helm")} + " : "")}" +
            $"{(info.Back ? "back of head FC_" + (female ? "F" : "M") + "_0000 + " : "")}{(info.Face ? $"FaceGen face {info.FaceGenId} (shape max {info.MorphMm:0.0} mm) + " : "")}{(info.Eyes ? "eyes + " : "")}" +
            $"{(info.Hair ? $"hair {info.HairPart}" : info.HairPart == null ? $"no hair (DS1 style {info.HairStyle} has no part: bald)" : $"hair {info.HairPart} hidden")}; DS1 hidden groups {string.Join(",", info.Hidden.Order())} → ArmorParam {armorId}");
        return armorId;
    }

    /// <summary>DS2's face/hair culling groups (every group a vanilla parts/face FLVER uses, except 0).</summary>
    public static HashSet<int> Ds2FaceGroups(string ds2Data)
    {
        if (faceGroups != null) return faceGroups;
        var set = new HashSet<int>();
        foreach (string p in Directory.GetFiles(Path.Combine(ds2Data, "model", "parts", "face"), "*.bnd").Where(p => !p.EndsWith("_l.bnd")))
        {
            BND4 b;
            try { b = BND4.Read(p); } catch { continue; }
            foreach (var bf in b.Files.Where(x => x.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase)))
            {
                var f = FLVER2.Read(bf.Bytes);
                foreach (var m in f.Materials) { int grp = ArmorCulling.Ds2Group(f, m); if (grp > 0) set.Add(grp); }
            }
        }
        return faceGroups = set;
    }
    static HashSet<int>? faceGroups;

    /// <summary>The head pieces' EquipInterfereParam rows (after ArmorItems.Finish): the helm's own row (AR8) or DS2's bare-head row,
    /// every DS2 face/hair group hidden for both sexes; the ArmorParam numbers refreshed from the (now calibrated) helm.</summary>
    public static List<string> FinishRows(Pack pack)
    {
        var lines = new List<string>();
        var ap = pack.Param("ArmorParam");
        var groups = Ds2FaceGroups(pack.Ds2Data);
        foreach (var (armorId, helm, female, name) in pending)
        {
            var row = ap[armorId];
            if (row == null) continue;
            var helmRow = helm > 0 ? ap[helm] : null;
            if (helmRow != null)
            {
                // the helm's (calibrated) numbers, our model / gender / interfere row
                foreach (var cell in helmRow.Cells) if (cell.Def.InternalName is not ("armorId" or "modelId" or "modelGender" or "interfereId")) row[cell.Def.InternalName].Value = cell.Value;
            }
            int srcRow = helmRow != null ? I(helmRow, "interfereId") : I(ap[11001100 + (female ? 1000 : 0)]!, "interfereId");
            var inter = pack.Param("EquipInterfereParam");
            var src = inter[srcRow] ?? inter[I(ap[11001100]!, "interfereId")]!;
            var r = pack.AddRow("EquipInterfereParam", src, armorId, $"[Lordran] N-HEAD1 {name}");
            foreach (int grp in groups) foreach (int sex in new[] { 0, 1 }) Set(r, $"cullingGroup{grp:D2}_{sex}", 1);
            Set(row, "interfereId", armorId);
            lines.Add($"N-HEAD1 {name}: ArmorParam {armorId} interfere row {armorId} = row {srcRow} + DS2 face/hair groups {string.Join(",", groups.Order())} hidden");
        }
        pending.Clear();
        return lines;
    }
}
