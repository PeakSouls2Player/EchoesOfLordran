using System.Numerics;
using Lordran.Tools.Build.Maps;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// sky-check (session 9, track skyfix): evidence for the DS1 sky/background/light-shaft rules.
///   sky-check envlight                 DSR other\envlight.tpf: every texture (name, format, cube, size) and, for BC6H cubes,
///                                      per-face mean/min/max of our BC6H decode, plus Pfim's clamped decode of face 0 for
///                                      comparison (validates <see cref="Textures.Bc6h"/>).
///   sky-check scatter AREA ROW [DIST…] DS1's exact LightScatteringBank terms (<see cref="Ds1Scatter"/>) of one row at
///                                      distances: T, in-scatter S (horizontal-view average and min/max over view angles).
/// </summary>
public static class SkyCheck
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: sky-check envlight | scatter AREA ROW [DIST...]"); return 1; }
        return args[1] switch
        {
            "envlight" => EnvLight(),
            "gienv" => GiEnv(args[2]),
            "compare" => Compare(args[2], args.Length > 3 ? args[3] : null),
            "bxf" => BxfList(args[2]),
            "render" => SkyRender.Run(args),
            "display" => Display(args[2], int.Parse(args[3]), int.Parse(args[4]), float.Parse(args[5]), args.Skip(6).ToArray()),
            "scatter" => Scatter(args[2], int.Parse(args[3]), args.Skip(4).Select(float.Parse).ToArray()),
            _ => 1,
        };
    }

    /// <summary>A DDS file's top-level layout: width, height, mips, faces, DXGI format (0 if legacy), data offset.</summary>
    public static (int W, int H, int Mips, int Faces, int Dxgi, string FourCC, int Offset) DdsInfo(byte[] b)
    {
        int h = BitConverter.ToInt32(b, 12), w = BitConverter.ToInt32(b, 16), mips = Math.Max(1, BitConverter.ToInt32(b, 28));
        string four = System.Text.Encoding.ASCII.GetString(b, 84, 4);
        int caps2 = BitConverter.ToInt32(b, 112);
        int faces = (caps2 & 0x200) != 0 ? 6 : 1;
        int dxgi = 0, offset = 128;
        if (four == "DX10")
        {
            dxgi = BitConverter.ToInt32(b, 128);
            int misc = BitConverter.ToInt32(b, 136);
            if ((misc & 4) != 0) faces = 6;
            offset = 148;
        }
        return (w, h, mips, faces, dxgi, four, offset);
    }

    static int EnvLight()
    {
        string path = Path.Combine(Program.DefaultDsrRoot, "other", "envlight.tpf");
        var tpf = TPF.Read(path);
        Console.WriteLine($"{path}: {tpf.Textures.Count} textures");
        foreach (var t in tpf.Textures)
        {
            var d = DdsInfo(t.Bytes);
            Console.WriteLine($"{t.Name}: tpf format {t.Format} type {t.Type} mips {t.Mipmaps} | dds {d.W}x{d.H} mips {d.Mips} faces {d.Faces} {d.FourCC} dxgi {d.Dxgi}");
            if (d.Dxgi is not 95 || d.Faces != 6) continue;
            int faceBytes = Textures.Bc6h.FaceBytes(d.W, d.Mips);
            var all = new List<Vector3>();
            for (int f = 0; f < 6; f++)
            {
                var px = Textures.Bc6h.Decode(t.Bytes.AsSpan(d.Offset + f * faceBytes), d.W, d.H);
                all.AddRange(px);
                var mean = px.Aggregate(Vector3.Zero, (a, b) => a + b) / px.Length;
                var mn = px.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
                var mx = px.Aggregate(new Vector3(float.MinValue), Vector3.Max);
                // smoothness: mean absolute difference between horizontal neighbours (BC6H decode errors show as block noise)
                double rough = 0; int nr = 0;
                for (int y = 0; y < d.H; y++) for (int x = 1; x < d.W; x++) { rough += (px[y * d.W + x] - px[y * d.W + x - 1]).Length(); nr++; }
                Console.WriteLine($"  face {f}: mean ({mean.X:0.####},{mean.Y:0.####},{mean.Z:0.####}) min ({mn.X:0.####},{mn.Y:0.####},{mn.Z:0.####}) max ({mx.X:0.####},{mx.Y:0.####},{mx.Z:0.####}) neighbour diff {rough / Math.Max(1, nr):0.#####}");
                if (f == 0)
                {
                    try
                    {
                        using var img = Pfim.Pfimage.FromStream(new MemoryStream(t.Bytes));
                        // Pfim: 8-bit BGRA, clamped; compare where our values are < 1
                        int bpp = img.BitsPerPixel / 8, cmp = 0; double err = 0, maxErr = 0;
                        for (int y = 0; y < Math.Min(d.H, img.Height); y++)
                            for (int x = 0; x < Math.Min(d.W, img.Width); x++)
                            {
                                var ours = px[y * d.W + x];
                                if (ours.X >= 1 || ours.Y >= 1 || ours.Z >= 1) continue;
                                int o = y * img.Stride + x * bpp;
                                float pr = img.Data[o + 2] / 255f, pg = img.Data[o + 1] / 255f, pb = img.Data[o] / 255f;
                                double e = (Math.Abs(pr - ours.X) + Math.Abs(pg - ours.Y) + Math.Abs(pb - ours.Z)) / 3;
                                err += e; maxErr = Math.Max(maxErr, e); cmp++;
                            }
                        Console.WriteLine($"  face 0 vs Pfim ({img.Format}, clamped 8-bit): {cmp} texels < 1 compared, mean |diff| {err / Math.Max(1, cmp):0.####}, max {maxErr:0.####}");
                    }
                    catch (Exception ex) { Console.WriteLine($"  Pfim: {ex.Message}"); }
                }
            }
            var m = all.Aggregate(Vector3.Zero, (a, b) => a + b) / all.Count;
            Console.WriteLine($"  all faces mean ({m.X:0.####},{m.Y:0.####},{m.Z:0.####})");
        }
        return 0;
    }

    /// <summary>gienv AREA (m10, m18): DSR map\mAA\GI_EnvM_mAA cubes: every texture with its per-face means (our BC6H decode)
    /// and the MSB1 Environment events of the area's maps (name, position) that index them.</summary>
    static int GiEnv(string area)
    {
        foreach (var (name, faces) in Ds1EnvCubes.Load(Program.DefaultDsrRoot, area).All())
            Console.WriteLine($"{name}: " + string.Join(" ", faces.Select((f, i) => $"f{i} {Ds1Scatter.V(f)}")));
        foreach (string msbPath in Directory.GetFiles(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio"), $"{area}_*.msb"))
        {
            var msb = MSB1.Read(msbPath);
            int i = 0;
            foreach (var e in msb.Events.Environments)
                Console.WriteLine($"{Path.GetFileNameWithoutExtension(msbPath)} env event {i++} '{e.Name}' region {e.RegionName} pos {(msb.Regions.GetEntries().FirstOrDefault(r => r.Name == e.RegionName) is MSB1.Region r ? Ds1Scatter.V(r.Position) : "-")}");
        }
        return 0;
    }

    // ------------------------------------------------------------------ compare: DS1 vs our packs

    /// <summary>The materials the session-9 sky regressions are about (DS1 map, DS1 model, material index, DS2 map, label).</summary>
    static readonly (string Map, string Model, int Mat, string Ds2Map, string Label)[] Targets =
    [
        ("m18_01_00_00", "m9000B1", 0, "m18_01_00_00", "Asylum mountains (m19_back_ground_13)"),
        ("m18_01_00_00", "m9000B1", 1, "m18_01_00_00", "Asylum mountains (m19_back_ground_13_2, near)"),
        ("m18_01_00_00", "m9000B1", 2, "m18_01_00_00", "Asylum mountains (m19_back_ground_13_2, far)"),
        ("m18_01_00_00", "m9000B1", 4, "m18_01_00_00", "Asylum mountains (m19_back_ground_10)"),
        ("m18_01_00_00", "m9000B1", 5, "m18_01_00_00", "Asylum cloud band on the mountain ring"),
        ("m18_01_00_00", "m9100B1", 1, "m18_01_00_00", "Asylum dome clouds"),
        ("m18_01_00_00", "m5201B1", 1, "m18_01_00_00", "Asylum cloud ring (dedicated fog row 'clouds')"),
        ("m18_01_00_00", "m0001B1", 20, "m18_01_00_00", "Asylum cell light shaft (additive)"),
        ("m18_01_00_00", "m0070B1", 14, "m18_01_00_00", "Asylum courtyard light shaft (additive)"),
        ("m10_02_00_00", "m7010B2", 3, Lordran.Tools.Build.Maps.MapHosts.Host("m10_02_00_00"), "Firelink nest/ruins light shafts (additive)"),
        ("m10_02_00_00", "m2120B2", 11, Lordran.Tools.Build.Maps.MapHosts.Host("m10_02_00_00"), "Firelink upper-ruins light shafts (additive)"),
        ("m10_02_00_00", "m8000B2", 0, Lordran.Tools.Build.Maps.MapHosts.Host("m10_02_00_00"), "Firelink distant mist (436 m)"),
        ("m10_02_00_00", "m8100B2", 8, Lordran.Tools.Build.Maps.MapHosts.Host("m10_02_00_00"), "Firelink mist sheet"),
        ("m10_02_00_00", "m6100B2", 0, Lordran.Tools.Build.Maps.MapHosts.Host("m10_02_00_00"), "Firelink near mist sheet m6100 (fix pass: env-term check)"),
    ];

    sealed record TexStat(Vector3 Mean, Vector3 P10, Vector3 P90, float AlphaMean);

    static TexStat Stat(byte[] dds)
    {
        var img = Textures.Dds.Read(dds).DecodeTop();
        var px = img.Pixels.Where(p => p.A > 0).Select(p => new Vector3(p.R, p.G, p.B) / 255f).ToList();
        if (px.Count == 0) return new(Vector3.One, Vector3.One, Vector3.One, 0);
        var byLum = px.OrderBy(p => p.X + p.Y + p.Z).ToList();
        return new(px.Aggregate(Vector3.Zero, (a, b) => a + b) / px.Count, byLum[byLum.Count / 10], byLum[byLum.Count * 9 / 10], (float)img.Pixels.Average(p => p.A / 255.0));
    }

    static Vector3 Pow(Vector3 v, float e) => new(MathF.Pow(MathF.Max(v.X, 0), e), MathF.Pow(MathF.Max(v.Y, 0), e), MathF.Pow(MathF.Max(v.Z, 0), e));

    /// <summary>Our pack's version of one model material: per-vertex colours and the diffuse texture's stats.</summary>
    static (List<FLVER.VertexColor> Vc, TexStat? Tex, string TexName, string Mtd)? PackMaterial(string pack, string ds2Map, string ds2Model, int mat)
    {
        string dir = Path.Combine(pack, "gamedata", "model", "map");
        string bhd = Path.Combine(dir, ds2Map + ".mapbhd");
        if (!File.Exists(bhd)) return null;
        var f = BXF4.Read(bhd, Path.ChangeExtension(bhd, ".mapbdt")).Files.FirstOrDefault(x => Path.GetFileName(x.Name).Equals(ds2Model + ".flv.dcx", StringComparison.OrdinalIgnoreCase));
        if (f == null) return null;
        var flv = FLVER2.Read(DCX.Decompress(f.Bytes));
        if (mat >= flv.Materials.Count) return null;
        var vc = flv.Meshes.Where(m => m.MaterialIndex == mat).SelectMany(m => m.Vertices).Select(v => v.Colors.Count > 0 ? v.Colors[0] : new FLVER.VertexColor(1f, 1f, 1f, 1f)).ToList();
        string tex = Path.GetFileNameWithoutExtension(flv.Materials[mat].Textures.FirstOrDefault(t => t.ParamName == "g_DiffuseTexture")?.Path?.Replace('\\', '/') ?? "");
        string tbhd = Path.Combine(dir, "t" + ds2Map[1..] + ".tpfbhd");
        TexStat? st = null;
        if (tex.Length > 0 && File.Exists(tbhd))
        {
            var tf = BXF4.Read(tbhd, Path.ChangeExtension(tbhd, ".tpfbdt")).Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith(tex + ".tpf", StringComparison.OrdinalIgnoreCase));
            if (tf != null) st = Stat(TPF.Read(DCX.Is(tf.Bytes) ? DCX.Decompress(tf.Bytes) : tf.Bytes).Textures[0].Bytes);
        }
        return (vc, st, tex, Path.GetFileName(flv.Materials[mat].MTD));
    }

    /// <summary>
    /// compare NEWPACK [OLDPACK]: for each target material, DS1's own result (DSR data through DS1's shader formula:
    /// texture × vertex colour × MTD colour (× NormalToAlpha ambient), part fog, exact scattering, pow 2.2) against what
    /// DS2 will draw from each pack ((texture × vertex colour)², × alpha for additive), at the texture's p10/mean/p90
    /// texel (by luminance), averaged over the material's vertices. Linear values before tone mapping and before DS2's
    /// zone fog. The viewpoint is the map's bonfire (as the build's).
    /// </summary>
    static int Compare(string newPack, string? oldPack)
    {
        string root = Program.DefaultDsrRoot;
        var viewpoints = new Dictionary<string, Vector3> { ["m18_01_00_00"] = new(3.1f, 195.91f, 7.44f), ["m10_02_00_00"] = new(-51.80f, -60.60f, 55.70f) };
        var mtds = Ds1Mtd.Load(root);
        foreach (var grp in Targets.GroupBy(t => t.Map))
        {
            string map = grp.Key;
            var msb = MSB1.Read(Path.Combine(root, "map", "MapStudio", map + ".msb"));
            var dp = new Ds1DrawParam(root, map.Substring(1, 2));
            var tex = new Ds1MapTextures(root, map[..3]);
            var env = Ds1Unlit.EnvSpots.For(root, map, msb);
            var placed = Sky.DistinctPlacements(msb.Parts.MapPieces, []);
            // fix pass (rule S3d / review): DS2 fogs our pieces again with the viewpoint zone's fog (saturated ≥ 300 m)
            var vz = ViewZone.FogAt(root, map, msb, dp, viewpoints[map]);
            Console.WriteLine($"## {map}: DS2 zone fog at the bonfire: {vz?.Note ?? "none found"} (in-game lines below: (1 − α) × ours + α × colour, colour = fltparam bytes / 255 used linearly as Flver_Col[A] does; saturated only ≥ {Sky.FogBakeDistance} m)");
            foreach (var t in grp)
            {
                var part = placed.FirstOrDefault(p => p.ModelName == t.Model);
                string file = Path.Combine(root, "map", map, $"{t.Model}A{map.Substring(1, 2)}.flver.dcx");
                if (part == null || !File.Exists(file)) { Console.WriteLine($"{t.Label}: {t.Model} not placed / missing"); continue; }
                var flv = FLVER2.Read(DCX.Decompress(File.ReadAllBytes(file)));
                var sm = flv.Materials[t.Mat];
                var info = mtds.Get(sm.MTD);
                var world = Matrix4x4.CreateScale(part.Scale) * Matrix4x4.CreateRotationZ(part.Rotation.Z * MathF.PI / 180) * Matrix4x4.CreateRotationX(part.Rotation.X * MathF.PI / 180)
                    * Matrix4x4.CreateRotationY(part.Rotation.Y * MathF.PI / 180) * Matrix4x4.CreateTranslation(part.Position);
                var nodeWorld = flv.Nodes.Select((n, i) => { var m = n.ComputeLocalTransform(); for (int p = n.ParentIndex, g = 0; p >= 0 && g < 64; p = flv.Nodes[p].ParentIndex, g++) m *= flv.Nodes[p].ComputeLocalTransform(); return m; }).ToArray();
                Matrix4x4 Node(FLVER2.Mesh mesh, FLVER.Vertex v) { int node = mesh.BoneIndices.Count > 0 ? mesh.BoneIndices[Math.Clamp(v.BoneIndices[0], 0, mesh.BoneIndices.Count - 1)] : mesh.NodeIndex; return node >= 0 && node < nodeWorld.Length ? nodeWorld[node] : Matrix4x4.Identity; }
                var verts = flv.Meshes.Where(m => m.MaterialIndex == t.Mat).SelectMany(m => m.Vertices.Where(v => MapBuilderSane(v.Position)).Select(v =>
                    (P: Vector3.Transform(Vector3.Transform(v.Position, Node(m, v)), world), N: Vector3.Normalize(Vector3.TransformNormal(Vector3.TransformNormal(v.Normal, Node(m, v)), world) + new Vector3(0, 1e-6f, 0)),
                     C: v.Colors.Count > 0 ? v.Colors[0] : new FLVER.VertexColor(1f, 1f, 1f, 1f)))).ToList();
                string diffuse = Path.GetFileNameWithoutExtension(sm.Textures.FirstOrDefault(x => x.ParamName == "g_Diffuse")?.Path?.Replace('\\', '/') ?? "");
                var dsTex = tex.Dds(diffuse) is byte[] b ? Stat(b) : new TexStat(Vector3.One, Vector3.One, Vector3.One, 1);
                var fog = dp.Row("FogBank", part.FogID); var sc = dp.Row("LightScatteringBank", part.ScatterID); var light = dp.Row("LightBank", part.LightID);
                var amb = Ds1Unlit.IsNormalToAlpha(info) && light != null ? new Ds1Unlit.Ambient(light, env) : null;
                bool additive = info?.BlendMode == 4;
                var vp = viewpoints[map];
                var dists = verts.Select(v => Vector3.Distance(v.P, vp)).Order().ToList();
                float med = dists.Count > 0 ? dists[dists.Count / 2] : 0;
                var zoneFogs = msb.Parts.Collisions.Where(c => c.HitFilterID == 8).Select(c => (int)c.FogID).ToHashSet();
                bool distant = med >= Sky.FogBakeDistance || (fog != null && !zoneFogs.Contains(fog.ID)); // rule S3b v2 (as the build)
                Vector3 Ds1(Vector3 texel)
                {
                    var sum = Vector3.Zero;
                    foreach (var v in verts)
                    {
                        var c = texel * new Vector3(v.C.R, v.C.G, v.C.B) * (info?.Color ?? Vector3.One) * (amb != null ? Pow(amb.At(v.N, v.P), 1 / 2.2f) : Vector3.One);
                        // DS1 fogs/scatters every part with its own rows; near pieces are fogged by DS2's zone fog instead,
                        // so (like the build) DS1's result is compared before fog there
                        if (distant) c = Ds1Scatter.Toward(sc, v.P, vp).Apply(Vector3.Lerp(c, Sky.FogColor(fog), Sky.FogAmount(fog, Vector3.Distance(v.P, vp))));
                        sum += Pow(c, 2.2f) * (additive ? v.C.A : 1);
                    }
                    return sum / Math.Max(1, verts.Count);
                }
                static Vector3 Ours(List<FLVER.VertexColor> vc, Vector3 texel, bool add) =>
                    vc.Select(c => Pow(texel * new Vector3(c.R, c.G, c.B), 2) * (add ? c.A : 1)).Aggregate(Vector3.Zero, (a, b2) => a + b2) / Math.Max(1, vc.Count);
                Console.WriteLine($"== {t.Label}: {map} {t.Model} material {t.Mat} '{sm.Name}' {Path.GetFileName(sm.MTD)} ({info?.Shader}, blend {info?.BlendMode}), {verts.Count} vertices, median {med:0} m from the bonfire{(distant ? " (distant: part fog + scattering)" : "")}");
                Console.WriteLine($"   DS1 part {part.Name}: light {part.LightID} fog {part.FogID} '{fog?.Name}' scatter {part.ScatterID} '{sc?.Name}'; MTD colour {V3(info?.Color ?? Vector3.One)}; texture {diffuse} mean {V3(dsTex.Mean)} p10 {V3(dsTex.P10)} p90 {V3(dsTex.P90)} alpha {dsTex.AlphaMean:0.###}{(amb != null ? $"; NormalToAlpha ambient: {amb.Note}" : "")}");
                string what = additive ? "additive contribution (linear rgb × vertex alpha, × texture alpha not included)" : "linear colour";
                Console.WriteLine($"   DS1 {what}: p10 {V3(Ds1(dsTex.P10))} mean {V3(Ds1(dsTex.Mean))} p90 {V3(Ds1(dsTex.P90))}");
                foreach (var (label, pack) in new[] { ("new", newPack), ("old", oldPack) })
                {
                    if (pack == null) continue;
                    string ds2Model = $"m{t.Model.Substring(1, 4)}";
                    var pm = PackMaterial(pack, t.Ds2Map, ds2Model, t.Mat);
                    if (pm == null) { Console.WriteLine($"   {label}: {ds2Model} material {t.Mat} not found"); continue; }
                    var (vc, st, texName, mtd) = pm.Value;
                    var s = st ?? new TexStat(Vector3.One, Vector3.One, Vector3.One, 1);
                    var vcMean = vc.Aggregate(Vector3.Zero, (a, c) => a + new Vector3(c.R, c.G, c.B)) / Math.Max(1, vc.Count);
                    Console.WriteLine($"   {label} ({mtd}, texture {texName} mean {V3(s.Mean)}; vertex rgb mean {V3(vcMean)} alpha mean {vc.Average(c => c.A):0.###}): p10 {V3(Ours(vc, s.P10, additive))} mean {V3(Ours(vc, s.Mean, additive))} p90 {V3(Ours(vc, s.P90, additive))}");
                    if (vz != null && med >= Sky.FogBakeDistance && !additive)
                    {
                        Vector3 G(Vector3 x) => Vector3.Lerp(x, vz.Colour, vz.Alpha);
                        Console.WriteLine($"   {label} in game (+ DS2 zone fog α {vz.Alpha:0.##}): p10 {V3(G(Ours(vc, s.P10, additive)))} mean {V3(G(Ours(vc, s.Mean, additive)))} p90 {V3(G(Ours(vc, s.P90, additive)))}");
                    }
                }
            }
        }
        return 0;
    }

    /// <summary>
    /// display AREA TONEMAP TONECORRECT LA NAME=L…: what each game shows (0–255, grey) for scene-linear values L in one DS1
    /// zone. DS1: E = key / (clamp(La, min, max) + 1e-4), Hable (A8c's constants) / white, gamma 1/2.2, ToneCorrect (rule
    /// A7b's matrix). DS2 (rule A8c): exposure τ(key) × E (τ = the DS2 input that shows DS1's display of the key),
    /// HejlDawson(x − 0.004) × 1.1, then the palette: A7b only (DS1's matrix on DS2's display) or A7b + A7c (tone match).
    /// La is DS1's adapted luminance (a sky-dominated view clamps at max).
    /// </summary>
    static int Display(string area, int toneMap, int toneCorrect, float la, string[] values)
    {
        var dp = new Ds1DrawParam(Program.DefaultDsrRoot, area);
        var tm = dp.Row("ToneMapBank", toneMap); var tc = dp.Row("ToneCorrectBank", toneCorrect);
        if (tm == null || tc == null) { Console.WriteLine("missing row"); return 1; }
        double G(PARAM.Row r, string f) => Ds1DrawParam.Get(r, f);
        float key = (float)G(tm, "grayKeyValue"), mn = (float)G(tm, "minAdaptedLum"), mx = (float)G(tm, "maxAdapredLum");
        float e1 = key / (Math.Clamp(la, mn, mx) + 1e-4f);
        static float Hable(float x) => (x * (0.15f * x + 0.05f) + 0.004f) / (x * (0.15f * x + 0.5f) + 0.06f) - 0.02f / 0.3f;
        static float Ds1Curve(float x) => MathF.Pow(Math.Clamp(Hable(x) / Hable(11.2f), 0, 1), 1 / 2.2f);
        static float Hd(float x) { x = MathF.Max(0, x); return x * (6.2f * x + 0.5f) / (x * (6.2f * x + 1.7f) + 0.06f); }
        float ds1Key = Ds1Curve(key);
        // τ(key): DS2 input whose display equals DS1's display of the key (A8c), by bisection
        float lo = 0, hi = 2;
        for (int i = 0; i < 60; i++) { float m = (lo + hi) / 2; if (MathF.Min(1, Hd(m - 0.004f) * 1.1f) < ds1Key) lo = m; else hi = m; }
        float tau = lo / key;
        Console.WriteLine($"a{area} ToneMap {toneMap} '{tm.Name}' key {key} band {mn}–{mx}, La {la} → DS1 E {e1:0.###}; DS2 exposure τ {tau:0.####} × E = {tau * e1:0.####}; ToneCorrect {toneCorrect} '{tc.Name}'");
        // fix pass: the game samples the palette as a 16³ LUT (trilinear at display × 15), not the analytic curve
        var lutB = PaletteLut.Build(ExtractDs2.DefaultOut, tc, toneMatch: false);
        var lutC = PaletteLut.Build(ExtractDs2.DefaultOut, tc, toneMatch: true);
        Console.WriteLine("| value | scene L | DS2 table h | DS1 display | DS2 A7b only (LUT) | DS2 A7b+A7c analytic | DS2 A7b+A7c (LUT, as in game) |");
        Console.WriteLine("|---|---|---|---|---|---|---|");
        foreach (string v in values)
        {
            var parts = v.Split('=');
            float l = float.Parse(parts[1]);
            float d1 = Palette.Correct(new Vector3(Ds1Curve(e1 * l)), tc).X;
            float h = MathF.Min(1, Hd(tau * e1 * l - 0.004f) * 1.1f);
            float d2b = lutB?.Sample(new Vector3(h)).X ?? Palette.Correct(new Vector3(h), tc).X;
            float d2a = Palette.Correct(new Vector3(Palette.ToneMatch(h)), tc).X;
            float d2c = lutC?.Sample(new Vector3(h)).X ?? float.NaN;
            Console.WriteLine($"| {parts[0]} | {l:0.####} | {h * 255:0.#} | {d1 * 255:0} | {d2b * 255:0} | {d2a * 255:0} | {d2c * 255:0} |");
        }
        return 0;
    }

    /// <summary>bxf FILE.xxxbhd: every file of a BXF4 pair (name, size, MD5 of the decompressed bytes).</summary>
    static int BxfList(string bhd)
    {
        string bdt = Path.ChangeExtension(bhd, bhd.EndsWith("bhd") ? bhd[^6..^3] + "bdt" : ".bdt");
        bdt = Path.Combine(Path.GetDirectoryName(bhd)!, Path.GetFileName(bhd)[..^3] + "bdt");
        foreach (var f in BXF4.Read(bhd, bdt).Files)
        {
            byte[] b = DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes;
            Console.WriteLine($"{f.ID} {f.Name} {f.Bytes.Length} {b.Length} {Convert.ToHexString(System.Security.Cryptography.MD5.HashData(b))}");
        }
        return 0;
    }

    static bool MapBuilderSane(Vector3 p) => MapPieceConverter.Sane(p);
    static string V3(Vector3 v) => $"({v.X:0.####},{v.Y:0.####},{v.Z:0.####})";

    static int Scatter(string area, int row, float[] dists)
    {
        var dp = new Ds1DrawParam(Program.DefaultDsrRoot, area);
        var r = dp.Row("LightScatteringBank", row);
        if (r == null) { Console.WriteLine($"a{area} LightScatteringBank {row}: missing"); return 1; }
        Console.WriteLine($"a{area} LightScatteringBank {row} '{r.Name}': {Ds1Scatter.Describe(r)}");
        foreach (float d in dists.Length > 0 ? dists : [70f, 300f, 500f, 1000f, 2000f])
        {
            var avg = Ds1Scatter.Horizontal(r, d);
            var lo = Ds1Scatter.At(r, d, -1); var hi = Ds1Scatter.At(r, d, 1); var side = Ds1Scatter.At(r, d, 0);
            Console.WriteLine($"  {d,6:0} m: horizontal-average {avg} | cosθ −1 S {Ds1Scatter.V(lo.InScatter)} | 0 S {Ds1Scatter.V(side.InScatter)} | +1 S {Ds1Scatter.V(hi.InScatter)}");
        }
        return 0;
    }
}
