using System.Numerics;
using Lordran.Tools.Build;
using Lordran.Tools.Build.Chr;
using Lordran.Tools.Formats;
using Lordran.Tools.Textures;
using SoulsFormats;
using static Lordran.Tools.Build.ParamCells;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (track outfit11): offline renders of outfits (textured software rasteriser, orthographic front view, 2.2 m frame).
/// A <see cref="Mesh3"/> list comes from our pack's part binders (DS2 culling groups applied from the worn pieces' interfere rows
/// and DS2's naked rows for empty slots) or from DS1's own parts (DS1 display groups applied from the worn pieces'
/// invisibleFlags and DS1's bare rows), on DS1's bind pose or re-bound to DS2's (<see cref="PlayerRig"/>).
/// </summary>
public static partial class OutfitCheck
{
    public sealed record Mesh3(Vector3[] Pos, Vector3[] Nrm, Vector2[] Uv, int[] Tris, Image? Tex, bool Alpha);

    static readonly Dictionary<string, Image?> texCache = new();

    static Image? Decode(string key, Func<byte[]?> bytes)
    {
        if (texCache.TryGetValue(key, out var img)) return img;
        try { var b = bytes(); img = b == null ? null : Dds.Read(b).DecodeTop(); }
        catch { img = null; }
        if (texCache.Count > 400) texCache.Clear();
        return texCache[key] = img;
    }

    static int[] Lod0(FLVER2.Mesh m)
    {
        var fs = m.FaceSets.FirstOrDefault(f => f.Flags == FLVER2.FaceSet.FSFlags.None) ?? m.FaceSets[0];
        return (fs.TriangleStrip ? fs.Triangulate(true) : fs.Indices).ToArray();
    }

    /// <summary>Our part binder's meshes; materials whose DS2 culling group is in <paramref name="hidden"/> are left out.</summary>
    public static List<Mesh3> OursPart(byte[] bndBytes, HashSet<int> hidden, out int culled)
    {
        var bnd = BND4.Read(bndBytes);
        var tex = bnd.Files.Where(f => f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase)).SelectMany(f => TPF.Read(f.Bytes).Textures)
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var f = FLVER2.Read(bnd.Files.First(x => x.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase)).Bytes);
        var list = new List<Mesh3>();
        culled = 0;
        foreach (var m in f.Meshes)
        {
            var mat = f.Materials[m.MaterialIndex];
            if (hidden.Contains(ArmorCulling.Ds2Group(f, mat)) && ArmorCulling.Ds2Group(f, mat) != 0) { culled++; continue; }
            string? d = mat.Textures.FirstOrDefault(t => t.ParamName == "g_DiffuseTexture")?.Path;
            string dn = d == null ? "" : Path.GetFileNameWithoutExtension(d);
            var img = tex.TryGetValue(dn, out var t) ? Decode("ours:" + bnd.GetHashCode() + dn, () => t.Headerize()) : null;
            list.Add(new Mesh3(m.Vertices.Select(v => v.Position).ToArray(), m.Vertices.Select(v => v.Normal).ToArray(),
                m.Vertices.Select(v => v.UVs.Count > 0 ? new Vector2(v.UVs[0].X, v.UVs[0].Y) : Vector2.Zero).ToArray(), Lod0(m), img,
                mat.MTD.Contains("Alp", StringComparison.OrdinalIgnoreCase)));
        }
        return list;
    }

    /// <summary>A DS1 part / chr FLVER's meshes; "#NN#" materials with NN in <paramref name="hidden"/> left out. With
    /// <paramref name="rig"/>: re-bound to DS2's bind pose.</summary>
    public static List<Mesh3> Ds1Part(FLVER2 f, Dictionary<string, TPF.Texture> tex, HashSet<int> hidden, PlayerRig? rig, out int culled)
    {
        bool Keep(FLVER2.Mesh m, FLVER2.Material mat) => ArmorCulling.Ds1Group(mat.Name) is int g && (g < 0 || !hidden.Contains(g));
        culled = f.Meshes.Count(m => !Keep(m, f.Materials[m.MaterialIndex]));
        var list = new List<Mesh3>();
        Image? Tex(FLVER2.Material mat)
        {
            string? d = mat.Textures.FirstOrDefault(t => t.ParamName == "g_Diffuse" && !string.IsNullOrEmpty(t.Path))?.Path;
            string dn = d == null ? "" : Path.GetFileNameWithoutExtension(d);
            return tex.TryGetValue(dn, out var t) ? Decode("ds1:" + dn, () => t.Headerize()) : null;
        }
        if (rig == null)
        {
            foreach (var m in f.Meshes.Where(m => Keep(m, f.Materials[m.MaterialIndex])))
            {
                var mat = f.Materials[m.MaterialIndex];
                list.Add(new Mesh3(m.Vertices.Select(v => v.Position).ToArray(), m.Vertices.Select(v => v.Normal).ToArray(),
                    m.Vertices.Select(v => v.UVs.Count > 0 ? new Vector2(v.UVs[0].X, v.UVs[0].Y) : Vector2.Zero).ToArray(), Lod0(m), Tex(mat), PlayerRig.KindOf(mat) == PlayerRig.Kind.Alpha));
            }
            return list;
        }
        var conv = rig.Convert(f, Keep, _ => (1, 1, 1, [new FLVER.VertexColor(1f, 1f, 1f, 1f)]), out _);
        foreach (var m in conv)
        {
            var mat = f.Materials[m.Ds1Material];
            list.Add(new Mesh3(m.Vertices.Select(v => v.Position).ToArray(), m.Vertices.Select(v => v.Normal).ToArray(),
                m.Vertices.Select(v => v.UVs.Count > 0 ? new Vector2(v.UVs[0].X, v.UVs[0].Y) : Vector2.Zero).ToArray(), m.Triangles.ToArray(), Tex(mat), m.Kind == PlayerRig.Kind.Alpha));
        }
        return list;
    }

    /// <summary>Renders meshes front-on (camera on −Z looking +Z, as flver-view's "front"), or from behind.</summary>
    public static (Image Img, bool[] Mask, float[] Depth) Render(List<Mesh3> meshes, int w, int h, bool back = false)
    {
        var img = new Image(w, h);
        var depth = new float[w * h];
        var mask = new bool[w * h];
        Array.Fill(depth, float.MinValue);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = new Rgba(38, 38, 46, 255);
        float scale = h / 2.25f;
        var light = Vector3.Normalize(new Vector3(back ? 0.4f : -0.4f, 0.5f, back ? 1f : -1f));
        foreach (var m in meshes)
        {
            var px = m.Pos.Select(p => back ? new Vector3(w / 2f + p.X * scale, h - 4 - p.Y * scale, p.Z) : new Vector3(w / 2f - p.X * scale, h - 4 - p.Y * scale, -p.Z)).ToArray();
            for (int i = 0; i + 2 < m.Tris.Length; i += 3)
            {
                int a = m.Tris[i], b = m.Tris[i + 1], c = m.Tris[i + 2];
                if (a >= px.Length || b >= px.Length || c >= px.Length) continue;
                Tri(img, depth, mask, px[a], px[b], px[c], m, a, b, c, light);
            }
        }
        return (img, mask, depth);
    }

    static void Tri(Image img, float[] depth, bool[] mask, Vector3 A, Vector3 B, Vector3 C, Mesh3 m, int ia, int ib, int ic, Vector3 light)
    {
        int minX = (int)Math.Max(0, Math.Floor(Math.Min(A.X, Math.Min(B.X, C.X)))), maxX = (int)Math.Min(img.Width - 1, Math.Ceiling(Math.Max(A.X, Math.Max(B.X, C.X))));
        int minY = (int)Math.Max(0, Math.Floor(Math.Min(A.Y, Math.Min(B.Y, C.Y)))), maxY = (int)Math.Min(img.Height - 1, Math.Ceiling(Math.Max(A.Y, Math.Max(B.Y, C.Y))));
        float d = (B.Y - C.Y) * (A.X - C.X) + (C.X - B.X) * (A.Y - C.Y);
        if (Math.Abs(d) < 1e-6f || maxX - minX > img.Width || maxY - minY > img.Height) return;
        var n = m.Nrm[ia] + m.Nrm[ib] + m.Nrm[ic];
        n = n.LengthSquared() > 1e-9f ? Vector3.Normalize(n) : Vector3.UnitZ;
        float l = Math.Clamp(0.35f + 0.65f * Math.Abs(Vector3.Dot(n, light)), 0, 1);
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float l1 = ((B.Y - C.Y) * (x - C.X) + (C.X - B.X) * (y - C.Y)) / d, l2 = ((C.Y - A.Y) * (x - C.X) + (A.X - C.X) * (y - C.Y)) / d, l3 = 1 - l1 - l2;
                if (l1 < 0 || l2 < 0 || l3 < 0) continue;
                float z = l1 * A.Z + l2 * B.Z + l3 * C.Z;
                int k = y * img.Width + x;
                if (z <= depth[k]) continue;
                Rgba col = new(180, 170, 150, 255);
                if (m.Tex != null)
                {
                    var uv = l1 * m.Uv[ia] + l2 * m.Uv[ib] + l3 * m.Uv[ic];
                    float u = uv.X - MathF.Floor(uv.X), v = uv.Y - MathF.Floor(uv.Y);
                    col = m.Tex[Math.Clamp((int)(u * m.Tex.Width), 0, m.Tex.Width - 1), Math.Clamp((int)(v * m.Tex.Height), 0, m.Tex.Height - 1)];
                    if (m.Alpha && col.A < 128) continue;
                }
                depth[k] = z;
                mask[k] = true;
                img[x, y] = new Rgba((byte)(col.R * l), (byte)(col.G * l), (byte)(col.B * l), 255);
            }
    }

    /// <summary>Panels side by side (with a gap).</summary>
    public static Image Strip(IList<Image> panels, int gap = 4)
    {
        int w = panels.Sum(p => p.Width) + gap * (panels.Count - 1), h = panels.Max(p => p.Height);
        var o = new Image(w, h);
        for (int i = 0; i < o.Pixels.Length; i++) o.Pixels[i] = new Rgba(10, 10, 12, 255);
        int x0 = 0;
        foreach (var p in panels)
        {
            for (int y = 0; y < p.Height; y++) for (int x = 0; x < p.Width; x++) o[x0 + x, y] = p[x, y];
            x0 += p.Width + gap;
        }
        return o;
    }

    /// <summary>Visible-surface agreement of two renders of the same geometry: the share of covered pixels (either) where both
    /// are covered and the front-most surface is within 1 cm. A mesh that pokes through (shown here, hidden there) or a hole
    /// lowers it; a silhouette IoU would miss both.</summary>
    public static double Agree((Image Img, bool[] Mask, float[] Depth) a, (Image Img, bool[] Mask, float[] Depth) b, float scalePxPerM)
    {
        int ok = 0, u = 0;
        for (int k = 0; k < a.Mask.Length; k++)
        {
            if (!a.Mask[k] && !b.Mask[k]) continue;
            u++;
            if (a.Mask[k] && b.Mask[k] && Math.Abs(a.Depth[k] - b.Depth[k]) <= 0.01f) ok++;
        }
        return u == 0 ? 1 : (double)ok / u;
    }

    static readonly string[] Pfx = ["hd", "bd", "am", "lg"], Fold = ["head", "body", "arm", "leg"];

    /// <summary>A worn outfit: per slot the ArmorParam id (0 = empty → DS2's naked piece), on our side.</summary>
    public sealed class Ctx2
    {
        public required string PackDir;
        public required ParamStore Mine;
        public required Ds1Source Ds1;
        public required PlayerRig Rig;
        public byte[]? PartBytes(string folder, string file)
        {
            string p = Path.Combine(PackDir, "gamedata", "model", "parts", folder, file);
            if (File.Exists(p)) return File.ReadAllBytes(p);
            p = Path.Combine(ExtractDs2.DefaultOut, "model", "parts", folder, file);
            return File.Exists(p) ? File.ReadAllBytes(p) : null;
        }
    }

    /// <summary>Our side of an outfit: meshes (worn pieces + naked fill when <paramref name="fill"/>), DS2 groups hidden, notes.</summary>
    public static (List<Mesh3> Meshes, HashSet<int> Hidden, List<string> Notes) Ours(Ctx2 c, int[] armorIds, bool female, bool fill)
    {
        var notes = new List<string>();
        var ap = c.Mine.Params["ArmorParam"];
        var inter = c.Mine.Params["EquipInterfereParam"];
        var rows = new PARAM.Row?[4];
        for (int s = 0; s < 4; s++) rows[s] = armorIds[s] > 0 ? ap[armorIds[s]] : ap[11001100 + s + (female ? 1000 : 0)];
        var hidden = new HashSet<int>();
        int sex = female ? 1 : 0;
        foreach (var r in rows.OfType<PARAM.Row>())
            if (inter[I(r, "interfereId")] is { } ir)
                for (int g = 1; g < 64; g++) if (I(ir, $"cullingGroup{g:D2}_{sex}") != 0) hidden.Add(g);
        var meshes = new List<Mesh3>();
        for (int s = 0; s < 4; s++)
        {
            if (rows[s] is not { } r) { notes.Add($"slot {s}: ArmorParam {armorIds[s]} missing"); continue; }
            if (armorIds[s] <= 0 && !fill) continue;
            int model = I(r, "modelId");
            string file = $"{Pfx[s]}_{model}_{(I(r, "modelGender") == 1 && female ? "f" : "m")}.bnd";
            var b = c.PartBytes(Fold[s], file);
            if (b == null) { if (armorIds[s] > 0) notes.Add($"slot {s}: no part {file}"); continue; }
            meshes.AddRange(OursPart(b, hidden, out int culled));
            if (culled > 0) notes.Add($"{file}: {culled} meshes culled");
        }
        return (meshes, hidden, notes);
    }

    /// <summary>DS1's side of an outfit (DS1 EquipParamProtector ids per slot, 0 = empty → DS1's bare row): meshes on DS1's bind
    /// pose or re-bound to DS2's (<paramref name="rebind"/>), DS1 groups hidden.</summary>
    public static (List<Mesh3> Meshes, HashSet<int> Hidden, List<string> Notes) Ds1(Ctx2 c, int[] ds1Ids, bool female, bool fill, bool rebind)
    {
        var notes = new List<string>();
        var prot = c.Ds1.Params.Params["EquipParamProtector"];
        var rows = new PARAM.Row?[4];
        for (int s = 0; s < 4; s++) rows[s] = prot[ds1Ids[s] > 0 ? ds1Ids[s] / 1000 * 1000 : 900000 + s * 1000];
        var hidden = new HashSet<int>();
        foreach (var r in rows.OfType<PARAM.Row>()) hidden.UnionWith(ArmorCulling.Ds1Flags(r));
        var meshes = new List<Mesh3>();
        string[] P1 = ["HD", "BD", "AM", "LG"];
        for (int s = 0; s < 4; s++)
        {
            if (rows[s] is not { } r) continue;
            if (ds1Ids[s] <= 0 && !fill) continue;
            int model = I(r, "equipModelId");
            string dir = Path.Combine(c.Ds1.Root, "parts");
            string? path = new[] { I(r, "equipModelGender") == 0 ? "A" : female ? "F" : "M", "M", "A" }
                .Select(g => Path.Combine(dir, $"{P1[s]}_{g}_{model:D4}.partsbnd.dcx")).FirstOrDefault(File.Exists);
            if (path == null) { notes.Add($"DS1 slot {s}: no part for model {model}"); continue; }
            var bnd = BND3.Read(path);
            var f = FLVER2.Read(bnd.Files.First(x => x.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
            meshes.AddRange(Ds1Part(f, ArmorPorter.Ds1Textures(c.Ds1.Root, bnd), hidden, rebind ? c.Rig : null, out int culled));
            if (culled > 0) notes.Add($"DS1 {Path.GetFileName(path)}: {culled} meshes hidden");
        }
        return (meshes, hidden, notes);
    }

    const int PW = 220, PH = 300;

    /// <summary>One outfit, both sexes: [DS1 male | ours male | DS1 female | ours female] (DS1 on its T bind pose, ours on DS2's
    /// A pose, empty slots filled with each game's bare body), and the silhouette agreement of the worn pieces alone with DS1's
    /// re-bound to DS2's pose (DS1 groups hidden by DS1's flags vs ours by our interfere rows).</summary>
    public static (Image Img, double IoUM, double IoUF, List<string> Notes) Outfit(Ctx2 c, int[] ours, int[] ds1, bool hasFemale = true)
    {
        var panels = new List<Image>();
        var notes = new List<string>();
        var iou = new double[2];
        foreach (bool female in new[] { false, true })
        {
            var d1 = Ds1(c, ds1, female, fill: true, rebind: false);
            var o = Ours(c, ours, female, fill: true);
            panels.Add(Render(d1.Meshes, PW, PH).Img);
            panels.Add(Render(o.Meshes, PW, PH).Img);
            notes.AddRange(d1.Notes.Concat(o.Notes).Select(n => (female ? "F " : "M ") + n));
            // silhouette check: worn pieces only, both on DS2's pose
            // visible surfaces of the worn pieces alone, both on DS2's pose, front and back (2x resolution)
            var dm = Ds1(c, ds1, female, fill: false, rebind: true).Meshes;
            var om = Ours(c, ours, female, fill: false).Meshes;
            iou[female ? 1 : 0] = Math.Min(Agree(Render(dm, PW * 2, PH * 2), Render(om, PW * 2, PH * 2), 0), Agree(Render(dm, PW * 2, PH * 2, back: true), Render(om, PW * 2, PH * 2, back: true), 0));
        }
        return (Strip(panels), iou[0], iou[1], notes);
    }

    /// <summary>
    /// <c>outfit-sheet PACK OUTDIR</c>: every ported DS1 armour set of the pack (items + NPC-only pieces), both sexes, DS1 vs ours
    /// (sets\SET.png, index.html, overview PNGs), silhouette IoU of the worn pieces (DS1 re-bound with DS1's hiding vs ours with
    /// DS2's culling); a set under 0.93 is listed as a PROBLEM.
    /// </summary>
    public static int Sheet(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: outfit-sheet PACK OUTDIR"); return 1; }
        string packDir = args[1], outDir = args[2];
        Directory.CreateDirectory(Path.Combine(outDir, "sets"));
        var c = new Ctx2 { PackDir = packDir, Mine = ParamStore.LoadDs2(Path.Combine(packDir, "title"), ExtractDs2.DefaultOut), Ds1 = new Ds1Source(Program.DefaultDsrRoot), Rig = new PlayerRig(Program.DefaultDsrRoot, ExtractDs2.DefaultOut) };
        var ap = c.Mine.Params["ArmorParam"];
        var pieces = ap.Rows.Where(a => a.ID is >= 14_000_000 and < 14_500_000 && a.ID % 1000 / 100 == 1 && a.ID % 100 <= 3)
            .Select(a => (Armor: a.ID, Ds1: ArmorItems.Ds1Id(a.ID + 10_000_000))).Where(p => p.Ds1 > 0 && c.Ds1.Params.Params["EquipParamProtector"][p.Ds1] != null).ToList();
        var html = new List<string> { "<!doctype html><meta charset=utf-8><title>Armour contact sheet</title><style>body{background:#16161a;color:#ddd;font:13px sans-serif}div{display:inline-block;margin:6px;vertical-align:top}img{display:block;width:448px}.bad{color:#f77}</style>",
            "<h1>DS1 armour in DS2: contact sheet</h1><p>Per set: DS1 male (own T bind pose) | ours male (DS2 A pose) | DS1 female | ours female. Empty slots: each game's bare body. agree = share of covered pixels (front and back, worn pieces only) where DS1's visible surface (re-bound to DS2's pose, DS1 hiding) and ours (DS2 culling) coincide within 1 cm.</p>" };
        int problems = 0;
        var thumbs = new List<Image>();
        foreach (var set in pieces.GroupBy(p => p.Ds1 / 10000).OrderBy(g => g.Key))
        {
            int[] ours = new int[4], ds1 = new int[4];
            foreach (var p in set) { ours[p.Ds1 / 1000 % 10] = p.Armor; ds1[p.Ds1 / 1000 % 10] = p.Ds1; }
            string name = string.Join(" / ", set.Select(p => c.Ds1.Text("Armor_name_", p.Ds1) is { Length: > 0 } n ? n : $"({p.Ds1})"));
            var (img, m, f, notes) = Outfit(c, ours, ds1);
            string file = $"set{set.Key:D3}.png";
            Png.Save(img, Path.Combine(outDir, "sets", file));
            thumbs.Add(img);
            bool bad = m < 0.93 || f < 0.93;
            if (bad) { problems++; Console.WriteLine($"  PROBLEM set {set.Key} {name}: surface agreement m {m:0.000} f {f:0.000}"); }
            Console.WriteLine($"  set {set.Key} {name}: agree m {m:0.000} f {f:0.000}{(notes.Count > 0 ? "; " + string.Join("; ", notes) : "")}");
            html.Add($"<div><img src='sets/{file}'><span class='{(bad ? "bad" : "")}'>set {set.Key}: {System.Net.WebUtility.HtmlEncode(name)}<br>agree m {m:0.000} f {f:0.000}</span></div>");
        }
        File.WriteAllLines(Path.Combine(outDir, "index.html"), html);
        // overview PNGs: 4 sets per row, 8 rows per sheet
        for (int sheet = 0; sheet * 32 < thumbs.Count; sheet++)
        {
            var rowsImg = thumbs.Skip(sheet * 32).Take(32).Chunk(4).Select(ch => Strip(ch.ToList(), 12)).ToList();
            int w = rowsImg.Max(r => r.Width), h = rowsImg.Sum(r => r.Height) + 12 * (rowsImg.Count - 1);
            var o = new Image(w, h);
            int y0 = 0;
            foreach (var r in rowsImg) { for (int y = 0; y < r.Height; y++) for (int x = 0; x < r.Width; x++) o[x, y0 + y] = r[x, y]; y0 += r.Height + 12; }
            Png.Save(o, Path.Combine(outDir, $"armour-sheet-{sheet + 1}.png"));
        }
        Console.WriteLine($"outfit-sheet {packDir}: {pieces.Count} pieces in {pieces.GroupBy(p => p.Ds1 / 10000).Count()} sets → {outDir}, {problems} problems");
        return problems == 0 ? 0 : 2;
    }

    /// <summary>The ArmorParam a DS1 armour id should be on our side: DS2's for a shared piece (item map) or a renamed duplicate,
    /// else the ported id (ArmorPorter's rule).</summary>
    static int ExpectedArmor(Ds1Source ds1, ParamStore ds2, ArmorPorter probe, int ds1Id)
    {
        int b = ds1Id / 1000 * 1000;
        // rule N-OUT1 (session 11, outfit11): an NPC wears DS1's own mesh: the ported ids whenever DS1 has a part for the piece
        if (ds1.Params.Params["EquipParamProtector"][b] is { } r)
        {
            string[] P1 = ["HD", "BD", "AM", "LG"];
            int model = I(r, "equipModelId");
            if (new[] { "A", "M", "F" }.Any(g => File.Exists(Path.Combine(ds1.Root, "parts", $"{P1[b / 1000 % 10]}_{g}_{model:D4}.partsbnd.dcx")))) return ArmorPorter.ArmorParamId(b);
        }
        var map = File.ReadLines(ItemMap.DefaultOut).Skip(1).Select(l => l.Split('\t')).FirstOrDefault(c => c[0] == "EquipParamProtector" && c[1] == b.ToString());
        if (map != null && map[4] == "shared" && int.TryParse(map[5], out int item) && ds2.Params["ItemParam"][item] is { } it) return I(it, "armorParamId");
        if (probe.Ds2Duplicate(b, ds1.Text("Armor_name_", b)) is { } dup && ds2.Params["ItemParam"][dup.Id] is { } it2) return I(it2, "armorParamId");
        return ArmorPorter.ArmorParamId(b);
    }

    /// <summary>
    /// <c>outfit-npcs PACK OUTDIR</c>: every DS1 human NPC of the built maps' DS1 MSBs (m18_01 Asylum, m10_02 Firelink, m10_01
    /// Burg/Parish: c0000 parts and the human-rig chr NPCs c2510 / c2750 / c2640): DS1's outfit (CharaInitParam equip_Helm/Armer/
    /// Gaunt/Leg, npcPlayerSex) vs the pack's NPCGenerateParam / NpcPlayerStatusParam rows of the NPC (placed ones) or what its
    /// pieces resolve to (not placed: quest arrivals, M3), every piece's part present for the NPC's sex, renders DS1 | ours.
    /// </summary>
    public static int Npcs(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: outfit-npcs PACK OUTDIR"); return 1; }
        string packDir = args[1], outDir = args[2];
        Directory.CreateDirectory(Path.Combine(outDir, "npcs"));
        var ds2 = ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        var c = new Ctx2 { PackDir = packDir, Mine = ParamStore.LoadDs2(Path.Combine(packDir, "title"), ExtractDs2.DefaultOut), Ds1 = new Ds1Source(Program.DefaultDsrRoot), Rig = new PlayerRig(Program.DefaultDsrRoot, ExtractDs2.DefaultOut) };
        var probe = new ArmorPorter(c.Ds1, ds2, new Pack(Program.DefaultDs2Root, ExtractDs2.DefaultOut), c.Rig);
        var placed = NpcPorter.Asylum.Concat(NpcPorter.Firelink).Concat(NpcPorter.Table("m10_01_00_00")).ToList();
        var html = new List<string> { "<!doctype html><meta charset=utf-8><title>NPC outfits</title><style>body{background:#16161a;color:#ddd;font:13px sans-serif}div{display:inline-block;margin:6px;vertical-align:top;width:460px}img{display:block;width:448px}.bad{color:#f77}td,th{padding:2px 6px;border-bottom:1px solid #333}</style>",
            "<h1>DS1 human NPCs: DS1 outfit vs ours</h1><p>Panels: DS1 (own bind pose, DS1 hiding) | ours (DS2 A pose, DS2 culling). Not placed = present in the DS1 map but arriving by quest / not yet placed (M3); their pieces are checked as they would resolve.</p>" };
        var table = new List<string> { "| NPC (DS1 map, entity, NpcParam) | DS1 set (CharaInitParam) | ours (NPCGenerateParam / status) | status |", "|---|---|---|---|" };
        int problems = 0;
        string[] fields = ["equip_Helm", "equip_Armer", "equip_Gaunt", "equip_Leg"], gen = ["equipHeadId", "equipBodyId", "equipArmId", "equipLegId"], st = ["headArmor", "bodyArmor", "armArmor", "legArmor"];
        var seen = new HashSet<int>();
        foreach (string map in new[] { "m18_01_00_00", "m10_02_00_00", "m10_01_00_00" })
        {
            var msb = MSB1.Read(Path.Combine(c.Ds1.Root, "map", "MapStudio", map + ".msb"));
            foreach (var part in msb.Parts.Enemies.Where(e => e.ModelName is "c0000" or "c2510" or "c2750" or "c2640"))
            {
                var isPlaced = placed.FirstOrDefault(p => p.Ds1Map == map && p.Entity == part.EntityID);
                if (isPlaced == null && !seen.Add(part.NPCParamID * 10 + (map == "m10_01_00_00" ? 1 : 0))) continue;
                string name = c.Ds1.Params.RowName("NpcParam", part.NPCParamID) is { Length: > 0 } n ? n : part.Name;
                string who = $"{name} ({map[..6]}, {part.EntityID}, {part.NPCParamID})";
                int id = NpcPorter.Id(part.NPCParamID);
                var genRow = isPlaced != null ? c.Mine.Params["NPCGenerateParam"][id] : null;
                var stRow = isPlaced != null ? c.Mine.Params["NpcPlayerStatusParam"][id] : null;
                var notes = new List<string>();
                bool bad = false;
                int[] ds1Ids = new int[4], expect = new int[4], ours = new int[4];
                bool female;
                string ds1Set, oursSet;
                if (part.ModelName == "c0000")
                {
                    var init = c.Ds1.Params.Params["CharaInitParam"][part.CharaInitID]!;
                    female = I(init, "npcPlayerSex") == 0;
                    for (int s = 0; s < 4; s++)
                    {
                        ds1Ids[s] = Math.Max(0, I(init, fields[s]));
                        expect[s] = ds1Ids[s] > 0 ? ExpectedArmor(c.Ds1, ds2, probe, ds1Ids[s]) : -1;
                    }
                    ds1Set = string.Join(", ", ds1Ids.Select(d => d > 0 ? $"{(c.Ds1.Text("Armor_name_", d / 1000 * 1000) is { Length: > 0 } t ? t : "(unnamed)")} {d}" : "-"));
                }
                else
                {
                    female = isPlaced?.Female ?? false;
                    int model = 4500 + int.Parse(part.ModelName[1..]) % 500;
                    for (int s = 0; s < 4; s++) expect[s] = 10_000_000 + model * 1000 + (female ? 100 : 120) + s;
                    ds1Set = $"{part.ModelName} own model (NpcParam {part.NPCParamID}, display masks)";
                }
                if (isPlaced != null)
                {
                    if (genRow == null || stRow == null) { notes.Add($"no NPCGenerateParam/NpcPlayerStatusParam {id}"); bad = true; }
                    else
                    {
                        for (int s = 0; s < 4; s++)
                        {
                            ours[s] = I(genRow, gen[s]);
                            int stv = I(stRow, st[s]);
                            if (ours[s] != expect[s]) { notes.Add($"{gen[s]} {ours[s]} ≠ expected {expect[s]}"); bad = true; }
                            if (expect[s] > 0 && stv != expect[s]) { notes.Add($"status {st[s]} {stv} ≠ {expect[s]}"); bad = true; }
                        }
                        if (I(genRow, "gender") != (female ? 1 : 0)) { notes.Add($"gender {I(genRow, "gender")}"); bad = true; }
                    }
                }
                else ours = expect.Select(e => e > 0 && c.Mine.Params["ArmorParam"][e] != null ? e : e > 0 ? -2 : -1).ToArray();
                // parts present for the sex, culling coherent
                for (int s = 0; s < 4; s++)
                {
                    if (ours[s] == -2) { notes.Add($"slot {s}: ArmorParam {expect[s]} not in the pack (piece not ported in this pack)"); bad |= isPlaced != null; continue; }
                    if (ours[s] <= 0) continue;
                    var r = c.Mine.Params["ArmorParam"][ours[s]];
                    if (r == null) { notes.Add($"slot {s}: ArmorParam {ours[s]} missing"); bad = true; continue; }
                    string file = $"{Pfx[s]}_{I(r, "modelId")}_{(I(r, "modelGender") == 1 && female ? "f" : "m")}.bnd";
                    if (c.PartBytes(Fold[s], file) == null) { notes.Add($"slot {s}: no part {file}"); bad = true; }
                }
                oursSet = string.Join(", ", ours.Select(o => o > 0 ? $"{o}" : o == -2 ? "not in pack" : "-"));
                // renders
                Image dsImg;
                if (part.ModelName == "c0000") dsImg = Render(Ds1(c, ds1Ids, female, true, false).Meshes, PW, PH).Img;
                else
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
                    dsImg = Render(Ds1Part(f, tex, hiddenMasks, null, out _), PW, PH).Img;
                }
                var ourImg = ours.Any(o => o > 0) ? Render(Ours(c, ours.Select(o => Math.Max(0, o)).ToArray(), female, true).Meshes, PW, PH).Img : new Image(PW, PH);
                string file2 = $"{map[..6]}_{part.EntityID}_{part.NPCParamID}.png";
                Png.Save(Strip([dsImg, ourImg]), Path.Combine(outDir, "npcs", file2));
                string status = isPlaced == null ? (ours.Any(o => o == -2) ? "not placed; pieces not all in this pack" : "not placed (quest arrival / M3); outfit resolves") : bad ? "MISMATCH" : "OK";
                if (isPlaced != null && bad) problems++;
                Console.WriteLine($"  {who} [{(female ? "f" : "m")}]: DS1 {ds1Set} → ours {oursSet}: {status}{(notes.Count > 0 ? " (" + string.Join("; ", notes) + ")" : "")}");
                table.Add($"| {who}{(isPlaced != null ? "" : " (not placed)")} | {ds1Set} | {oursSet} | {status}{(notes.Count > 0 ? ": " + string.Join("; ", notes) : "")} |");
                html.Add($"<div><img src='npcs/{file2}'><span class='{(bad && isPlaced != null ? "bad" : "")}'>{System.Net.WebUtility.HtmlEncode(who)} [{(female ? "female" : "male")}]<br>DS1: {System.Net.WebUtility.HtmlEncode(ds1Set)}<br>ours: {oursSet}<br>{System.Net.WebUtility.HtmlEncode(status)}</span></div>");
            }
        }
        File.WriteAllLines(Path.Combine(outDir, "npcs.html"), html);
        File.WriteAllLines(Path.Combine(outDir, "npc-table.md"), table);
        Console.WriteLine($"outfit-npcs {packDir}: {table.Count - 2} NPCs, {problems} placed NPCs with problems → {outDir}");
        return problems == 0 ? 0 : 2;
    }

    /// <summary>Pixels set in <paramref name="a"/> but not <paramref name="b"/> (fraction of a's).</summary>
    public static double Extra(bool[] a, bool[] b)
    {
        int n = 0, x = 0;
        for (int k = 0; k < a.Length; k++) { if (a[k]) { n++; if (!b[k]) x++; } }
        return n == 0 ? 0 : (double)x / n;
    }
}
