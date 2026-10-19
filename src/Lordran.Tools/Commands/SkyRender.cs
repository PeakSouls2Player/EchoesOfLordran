using System.Numerics;
using Lordran.Tools.Build.Maps;
using Lordran.Tools.Textures;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// sky-check render OUT.png OLDPACK NEWPACK [EYEX EYEY EYEZ YAW PITCH] (session 9, track skyfix): an offline render of the
/// Asylum's unlit background (sky dome m9100B1, mountain ring m9000B1, cloud ring m5201B1) from the cliff top, four
/// panels: DS1 (DSR data through DS1's shaders: texture × vertex colour × MTD colour, part fog, exact scattering, Hable,
/// ToneCorrect), then DS2 from each pack ((texture × vertex colour)², zone fog, A8c exposure, Hejl-Dawson, palette):
/// OLDPACK with the A7b palette, NEWPACK with A7b + A7c (tone match), NEWPACK with A7b only. Unlit pieces only (no
/// ground), nearest texel, no bloom: a comparison of the background's brightness against the sky, not a game frame.
/// Adaptation of each panel from its own frame (DS1 geometric mean, DS2 (mean √luma)²) within the zone's band.
/// </summary>
public static class SkyRender
{
    const int W = 480, H = 270;
    static readonly string[] Models = ["m9100B1", "m9000B1", "m5201B1"];

    sealed record Tri(Vector3[] P, Vector2[] Uv, Vector4[] C, int Layer, float Depth);
    sealed class Layer
    {
        public required Image Tex;
        public float RgbmRange;            // > 0: DS2 RGBM sky texture
        public int Blend;                  // 0 opaque, 1 alpha test, 2 alpha blend, 4 additive
        public Vector3 Mul = Vector3.One;  // DS1 MTD colour
        public PARAM.Row? Fog, Scatter;
        public bool Ds2Fog;                // DS2 zone fog applies (not on SkyDome2[NoFog])
        public string Name = "";
        public bool Mountain, Dome;        // fix pass: masks for the statistics
    }

    /// <summary>
    /// Fix pass: <c>render OUT.png OLDPACK NEWPACK [ABPACK] [EYEX EYEY EYEZ YAW PITCH]</c>. Every DS2 panel goes through
    /// its pack's own palette f718025 as the game samples it (<see cref="PaletteLut"/>: 16³ trilinear), not the analytic
    /// curve; the fourth panel is ABPACK (its geometry and palette) or, without it, NEWPACK's geometry with an in-memory
    /// A7b-only palette. Statistics per panel over the mountain pixels (DS1 m9000B1 / DS2 m9000 alpha-tested materials)
    /// and the sky-dome pixels.
    /// </summary>
    public static int Run(string[] args)
    {
        string outPng = args[2], oldPack = args[3], newPack = args[4];
        string? abPack = args.Length > 5 && Directory.Exists(args[5]) ? args[5] : null;
        int a0 = abPack != null ? 6 : 5;
        var eye = args.Length > a0 + 2 ? new Vector3(float.Parse(args[a0]), float.Parse(args[a0 + 1]), float.Parse(args[a0 + 2])) : new Vector3(3.27f, 211.5f, -83.48f);
        float yaw = (args.Length > a0 + 3 ? float.Parse(args[a0 + 3]) : 0) * MathF.PI / 180, pitch = (args.Length > a0 + 4 ? float.Parse(args[a0 + 4]) : 6) * MathF.PI / 180;
        string root = Program.DefaultDsrRoot, map = "m18_01_00_00";
        var msb = MSB1.Read(Path.Combine(root, "map", "MapStudio", map + ".msb"));
        var dp = new Ds1DrawParam(root, "18");
        var mtds = Ds1Mtd.Load(root);
        var ds1Tex = new Ds1MapTextures(root, "m18");
        var placed = Sky.DistinctPlacements(msb.Parts.MapPieces, []);
        // the cliff zone (filter 0): DS1 ToneMap 25 / ToneCorrect 25 (atmos-survey spot), DS2 zone fog black α 0.2
        var tm = dp.Row("ToneMapBank", 25)!; var tc = dp.Row("ToneCorrectBank", 25)!;
        float key = (float)Ds1DrawParam.Get(tm, "grayKeyValue"), mn = (float)Ds1DrawParam.Get(tm, "minAdaptedLum"), mx = (float)Ds1DrawParam.Get(tm, "maxAdapredLum");
        var zoneFog = Vector3.Zero; float zoneFogAlpha = 0.2f;

        var panels = new List<(string Label, Image Img)>();
        void Stats(string label, Image img, int[] top, List<Layer> layers)
        {
            var mt = new List<float>(); double sky = 0; int ns = 0;
            for (int i = 0; i < top.Length; i++)
            {
                if (top[i] < 0) continue;
                var p = img.Pixels[i]; float g = (p.R + p.G + p.B) / 3f;
                if (layers[top[i]].Mountain) mt.Add(g);
                else if (layers[top[i]].Dome) { sky += g; ns++; }
            }
            mt.Sort();
            float P(double q) => mt.Count == 0 ? float.NaN : mt[Math.Min(mt.Count - 1, (int)(q * mt.Count))];
            Console.WriteLine($"  stats {label}: sky-dome px {ns} mean {(ns > 0 ? sky / ns : double.NaN):0.0} | mountain px {mt.Count} mean {(mt.Count > 0 ? mt.Average() : float.NaN):0.0} p10 {P(0.1):0.0} p50 {P(0.5):0.0} p90 {P(0.9):0.0} p99 {P(0.99):0.0} <5/255 {(mt.Count > 0 ? 100.0 * mt.Count(x => x < 5) / mt.Count : double.NaN):0.0}%");
        }
        // ---- DS1
        {
            var (tris, layers) = Ds1Scene(msb, placed, dp, mtds, ds1Tex, root, map);
            var (lin, top) = Raster(tris, layers, eye, yaw, pitch, ds1: true, zoneFog, zoneFogAlpha);
            double sumLog = 0; foreach (var c in lin) sumLog += Math.Log(Math.Max(1e-5, Lum709(c)));
            float la = (float)Math.Exp(sumLog / lin.Length);
            float e1 = key / (Math.Clamp(la, mn, mx) + 1e-4f);
            var img = Show(lin, c => Palette.Correct(Ds1Display(c * e1), tc));
            panels.Add(($"DS1 (La {la:0.###}, E {e1:0.##})", img));
            Console.WriteLine($"DS1: La {la:0.####} → E {e1:0.###}");
            Stats("DS1", img, top, layers);
        }
        var a7bOnly = PaletteLut.Build(ExtractDs2.DefaultOut, tc, toneMatch: false);
        var lanes = new List<(string Label, string Pack, PaletteLut? Lut)>
        {
            ("before: OLDPACK, its palette", oldPack, PaletteLut.FromPack(oldPack, "18", 25)),
            ("after: NEWPACK, its palette", newPack, PaletteLut.FromPack(newPack, "18", 25)),
            abPack != null ? ("A/B: ABPACK, its palette", abPack, PaletteLut.FromPack(abPack, "18", 25)) : ("NEWPACK with an A7b-only palette", newPack, a7bOnly),
        };
        foreach (var (label, pack, lut) in lanes)
        {
            var (tris, layers) = PackScene(pack, placed, mtds, msb, root, map);
            if (tris.Count == 0) { Console.WriteLine($"{label}: no geometry from {pack}"); continue; }
            if (lut == null) Console.WriteLine($"{label}: palette f718025 not found in {pack}: analytic A7b used");
            var (lin, top) = Raster(tris, layers, eye, yaw, pitch, ds1: false, zoneFog, zoneFogAlpha);
            double sum = 0; foreach (var c in lin) sum += Math.Sqrt(Math.Max(0, 0.299 * c.X + 0.587 * c.Y + 0.114 * c.Z));
            float l2 = (float)Math.Pow(sum / lin.Length, 2);
            float tau = Tau(key);
            float exposure = tau * key / Math.Clamp(l2, mn, mx);   // = 1 / (AvgLuminance × LB × s), Avg = 1/(τ key LB) (A8c)
            var img = Show(lin, c =>
            {
                var h = new Vector3(Hd(exposure * c.X - 0.004f), Hd(exposure * c.Y - 0.004f), Hd(exposure * c.Z - 0.004f)) * 1.1f;
                h = Vector3.Min(h, Vector3.One);
                return lut != null ? lut.Sample(h) : Palette.Correct(h, tc);
            });
            panels.Add(($"{label} (L² {l2:0.###}, exp {exposure:0.###})", img));
            Console.WriteLine($"{label} [{pack}]: L² {l2:0.####} → exposure {exposure:0.####}, palette {lut?.Name ?? "analytic"}");
            Stats(label, img, top, layers);
        }
        // 2×2 sheet with a strip of black between panels; the label goes to stdout (no font)
        var sheet = new Image(W * 2 + 4, H * 2 + 4);
        for (int i = 0; i < panels.Count; i++)
        {
            int ox = (i % 2) * (W + 4), oy = (i / 2) * (H + 4);
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) sheet.Pixels[(oy + y) * sheet.Width + ox + x] = panels[i].Img.Pixels[y * W + x];
            Console.WriteLine($"panel {i} ({(i % 2 == 0 ? "left" : "right")}, {(i < 2 ? "top" : "bottom")}): {panels[i].Label}");
        }
        Png.Save(sheet, outPng);
        // mean display value of the mountain band vs the sky band per panel (rows where the mountains are)
        return 0;
    }

    static float Lum709(Vector3 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
    static float Hable(float x) => (x * (0.15f * x + 0.05f) + 0.004f) / (x * (0.15f * x + 0.5f) + 0.06f) - 0.02f / 0.3f;
    static Vector3 Ds1Display(Vector3 x) => new(Disp(x.X), Disp(x.Y), Disp(x.Z));
    static float Disp(float x) => MathF.Pow(Math.Clamp(Hable(x) / Hable(11.2f), 0, 1), 1 / 2.2f);
    static float Hd(float x) { x = MathF.Max(0, x); return x * (6.2f * x + 0.5f) / (x * (6.2f * x + 1.7f) + 0.06f); }
    static float Tau(float key)
    {
        float target = Disp(key), lo = 0, hi = 2;
        for (int i = 0; i < 60; i++) { float m = (lo + hi) / 2; if (MathF.Min(1, Hd(m - 0.004f) * 1.1f) < target) lo = m; else hi = m; }
        return lo / key;
    }

    static Image Show(Vector3[] lin, Func<Vector3, Vector3> display)
    {
        var img = new Image(W, H);
        for (int i = 0; i < lin.Length; i++)
        {
            var d = Vector3.Clamp(display(lin[i]), Vector3.Zero, Vector3.One) * 255;
            img.Pixels[i] = new Rgba((byte)MathF.Round(d.X), (byte)MathF.Round(d.Y), (byte)MathF.Round(d.Z), 255);
        }
        return img;
    }

    static Matrix4x4 PartWorld(MSB1.Part p) => Matrix4x4.CreateScale(p.Scale) * Matrix4x4.CreateRotationZ(p.Rotation.Z * MathF.PI / 180)
        * Matrix4x4.CreateRotationX(p.Rotation.X * MathF.PI / 180) * Matrix4x4.CreateRotationY(p.Rotation.Y * MathF.PI / 180) * Matrix4x4.CreateTranslation(p.Position);

    static Matrix4x4[] NodeWorld(FLVER2 flv) => flv.Nodes.Select(n => { var m = n.ComputeLocalTransform(); for (int q = n.ParentIndex, g = 0; q >= 0 && g < 64; q = flv.Nodes[q].ParentIndex, g++) m *= flv.Nodes[q].ComputeLocalTransform(); return m; }).ToArray();

    static IEnumerable<Tri> Triangles(FLVER2 flv, Matrix4x4 world, Func<int, int> layerOf, bool nodes)
    {
        var nw = nodes ? NodeWorld(flv) : [];
        foreach (var mesh in flv.Meshes)
        {
            int layer = layerOf(mesh.MaterialIndex);
            if (layer < 0) continue;
            var fs = mesh.FaceSets.FirstOrDefault(f => f.Flags == FLVER2.FaceSet.FSFlags.None) ?? mesh.FaceSets[0];
            var idx = fs.Triangulate(allowPrimitiveRestarts: true);
            Vector3 P(FLVER.Vertex v)
            {
                var m = Matrix4x4.Identity;
                if (nodes) { int node = mesh.BoneIndices.Count > 0 ? mesh.BoneIndices[Math.Clamp(v.BoneIndices[0], 0, mesh.BoneIndices.Count - 1)] : mesh.NodeIndex; if (node >= 0 && node < nw.Length) m = nw[node]; }
                return Vector3.Transform(Vector3.Transform(v.Position, m), world);
            }
            for (int i = 0; i + 2 < idx.Count; i += 3)
            {
                var vs = new[] { mesh.Vertices[idx[i]], mesh.Vertices[idx[i + 1]], mesh.Vertices[idx[i + 2]] };
                if (vs.Any(v => !MapPieceConverter.Sane(v.Position))) continue;
                var p = vs.Select(P).ToArray();
                yield return new Tri(p, vs.Select(v => v.UVs.Count > 0 ? new Vector2(v.UVs[0].X, v.UVs[0].Y) : Vector2.Zero).ToArray(),
                    vs.Select(v => v.Colors.Count > 0 ? new Vector4(v.Colors[0].R, v.Colors[0].G, v.Colors[0].B, v.Colors[0].A) : Vector4.One).ToArray(), layer, 0);
            }
        }
    }

    static (List<Tri>, List<Layer>) Ds1Scene(MSB1 msb, List<MSB1.Part.MapPiece> placed, Ds1DrawParam dp, Ds1Mtd mtds, Ds1MapTextures tex, string root, string map)
    {
        var tris = new List<Tri>(); var layers = new List<Layer>();
        var cache = new Dictionary<string, Image>();
        foreach (string model in Models)
        {
            var part = placed.FirstOrDefault(p => p.ModelName == model);
            string file = Path.Combine(root, "map", map, $"{model}A18.flver.dcx");
            if (part == null || !File.Exists(file)) continue;
            var flv = FLVER2.Read(DCX.Decompress(File.ReadAllBytes(file)));
            var layerOf = new Dictionary<int, int>();
            for (int mi = 0; mi < flv.Materials.Count; mi++)
            {
                var sm = flv.Materials[mi];
                if (!MapPieceConverter.IsUnlit(sm.MTD)) continue;
                var info = mtds.Get(sm.MTD);
                string t = Path.GetFileNameWithoutExtension(sm.Textures.FirstOrDefault(x => x.ParamName == "g_Diffuse")?.Path?.Replace('\\', '/') ?? "");
                if (!cache.TryGetValue(t, out var img)) cache[t] = img = tex.Dds(t) is byte[] b ? Dds.Read(b).DecodeTop() : new Image(1, 1);
                layerOf[mi] = layers.Count;
                int blend = info?.BlendMode is 32 ? 0 : info?.BlendMode ?? 0;
                layers.Add(new Layer { Tex = img, Blend = blend, Mul = info?.Color ?? Vector3.One, Fog = dp.Row("FogBank", part.FogID), Scatter = dp.Row("LightScatteringBank", part.ScatterID), Name = $"{model}/{mi}",
                    Mountain = model == "m9000B1" && blend == 1, Dome = model == "m9100B1" && blend == 0 });
            }
            tris.AddRange(Triangles(flv, PartWorld(part), mi => layerOf.GetValueOrDefault(mi, -1), nodes: true));
        }
        return (tris, layers);
    }

    static (List<Tri>, List<Layer>) PackScene(string pack, List<MSB1.Part.MapPiece> placed, Ds1Mtd mtds, MSB1 msb, string root, string map)
    {
        var tris = new List<Tri>(); var layers = new List<Layer>();
        string dir = Path.Combine(pack, "gamedata", "model", "map");
        var mapBxf = BXF4.Read(Path.Combine(dir, "m18_01_00_00.mapbhd"), Path.Combine(dir, "m18_01_00_00.mapbdt"));
        var texBxf = BXF4.Read(Path.Combine(dir, "t18_01_00_00.tpfbhd"), Path.Combine(dir, "t18_01_00_00.tpfbdt"));
        var cache = new Dictionary<string, (Image, float)>();
        foreach (string model in Models)
        {
            var part = placed.FirstOrDefault(p => p.ModelName == model);
            string ds2 = $"m{model.Substring(1, 4)}";
            var f = mapBxf.Files.FirstOrDefault(x => Path.GetFileName(x.Name).Equals(ds2 + ".flv.dcx", StringComparison.OrdinalIgnoreCase));
            if (part == null || f == null) continue;
            var flv = FLVER2.Read(DCX.Decompress(f.Bytes));
            var layerOf = new Dictionary<int, int>();
            for (int mi = 0; mi < flv.Materials.Count; mi++)
            {
                var m = flv.Materials[mi];
                string mtd = Path.GetFileName(m.MTD);
                bool sky = mtd.StartsWith("SkyDome2", StringComparison.OrdinalIgnoreCase);
                if (!sky && !mtd.StartsWith("NoLit", StringComparison.OrdinalIgnoreCase)) continue;
                string t = Path.GetFileNameWithoutExtension(m.Textures.FirstOrDefault(x => x.ParamName == "g_DiffuseTexture")?.Path?.Replace('\\', '/') ?? "");
                if (!cache.TryGetValue(t, out var ti))
                {
                    var tf = texBxf.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith(t + ".tpf", StringComparison.OrdinalIgnoreCase));
                    if (tf == null) ti = (new Image(1, 1), 0);
                    else
                    {
                        var tt = TPF.Read(DCX.Is(tf.Bytes) ? DCX.Decompress(tf.Bytes) : tf.Bytes).Textures[0];
                        ti = (Dds.Read(tt.Bytes).DecodeTop(), tt.Format == 33 && tt.FloatStruct?.Values.Count > 0 ? tt.FloatStruct.Values[0] : 0);
                    }
                    cache[t] = ti;
                }
                int blend = mtd.Contains("_Add") ? 4 : mtd.Contains("_Alp_1bit") ? 1 : mtd.Contains("_Alp") ? 2 : 0;
                layerOf[mi] = layers.Count;
                layers.Add(new Layer { Tex = ti.Item1, RgbmRange = ti.Item2, Blend = blend, Ds2Fog = !sky, Name = $"{ds2}/{mi}", Mountain = ds2 == "m9000" && blend == 1, Dome = sky });
            }
            tris.AddRange(Triangles(flv, PartWorld(part), mi => layerOf.GetValueOrDefault(mi, -1), nodes: false));
        }
        return (tris, layers);
    }

    static Vector4 Sample(Image img, Vector2 uv)
    {
        float u = uv.X - MathF.Floor(uv.X), v = uv.Y - MathF.Floor(uv.Y);
        int x = Math.Clamp((int)(u * img.Width), 0, img.Width - 1), y = Math.Clamp((int)(v * img.Height), 0, img.Height - 1);
        var p = img.Pixels[y * img.Width + x];
        return new Vector4(p.R, p.G, p.B, p.A) / 255f;
    }

    static (Vector3[] Color, int[] Top) Raster(List<Tri> tris, List<Layer> layers, Vector3 eye, float yaw, float pitch, bool ds1, Vector3 zoneFog, float zoneFogAlpha)
    {
        var fwd = new Vector3(-MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), -MathF.Cos(yaw) * MathF.Cos(pitch));
        var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
        var up = Vector3.Cross(right, fwd);
        float fov = 70 * MathF.PI / 180, f = 1 / MathF.Tan(fov / 2), aspect = (float)W / H;
        var color = new Vector3[W * H];
        var depth = Enumerable.Repeat(float.MaxValue, W * H).ToArray();
        var top = Enumerable.Repeat(-1, W * H).ToArray(); // the opaque/alpha-tested layer seen at each pixel
        (float X, float Y, float Z) Project(Vector3 p) { var d = p - eye; float z = Vector3.Dot(d, fwd); return (Vector3.Dot(d, right) / z * f / aspect, Vector3.Dot(d, up) / z * f, z); }
        Vector4 Shade(Layer l, Vector3 p, Vector2 uv, Vector4 vc)
        {
            var t = Sample(l.Tex, uv);
            if (ds1)
            {
                var c = new Vector3(t.X, t.Y, t.Z) * new Vector3(vc.X, vc.Y, vc.Z) * l.Mul;
                float d = Vector3.Distance(p, eye);
                c = Ds1Scatter.Toward(l.Scatter, p, eye).Apply(Vector3.Lerp(c, Sky.FogColor(l.Fog), Sky.FogAmount(l.Fog, d)));
                var lin = new Vector3(MathF.Pow(MathF.Max(c.X, 0), 2.2f), MathF.Pow(MathF.Max(c.Y, 0), 2.2f), MathF.Pow(MathF.Max(c.Z, 0), 2.2f));
                return new Vector4(lin, t.W * vc.W);
            }
            var rgb = l.RgbmRange > 0 ? new Vector3(t.X, t.Y, t.Z) * t.W * l.RgbmRange : new Vector3(t.X, t.Y, t.Z);
            rgb *= new Vector3(vc.X, vc.Y, vc.Z);
            var lin2 = rgb * rgb;
            if (l.Ds2Fog) lin2 = Vector3.Lerp(lin2, zoneFog, zoneFogAlpha); // distant: the zone fog is saturated
            return new Vector4(lin2, l.RgbmRange > 0 ? 1 : t.W * vc.W);
        }
        void Draw(Tri tri, bool blendPass)
        {
            // clip against the near plane (z ≥ 1 m in front of the eye): the sky dome's big triangles reach behind the camera
            var poly = new List<(Vector3 P, Vector2 Uv, Vector4 C)> { (tri.P[0], tri.Uv[0], tri.C[0]), (tri.P[1], tri.Uv[1], tri.C[1]), (tri.P[2], tri.Uv[2], tri.C[2]) };
            float Zf(Vector3 p) => Vector3.Dot(p - eye, fwd) - 1;
            var clipped = new List<(Vector3 P, Vector2 Uv, Vector4 C)>();
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                float za = Zf(a.P), zb = Zf(b.P);
                if (za >= 0) clipped.Add(a);
                if ((za >= 0) != (zb >= 0))
                {
                    float k = za / (za - zb);
                    clipped.Add((Vector3.Lerp(a.P, b.P, k), Vector2.Lerp(a.Uv, b.Uv, k), Vector4.Lerp(a.C, b.C, k)));
                }
            }
            for (int i = 1; i + 1 < clipped.Count; i++)
                DrawClipped(new Tri([clipped[0].P, clipped[i].P, clipped[i + 1].P], [clipped[0].Uv, clipped[i].Uv, clipped[i + 1].Uv], [clipped[0].C, clipped[i].C, clipped[i + 1].C], tri.Layer, 0), blendPass);
        }
        void DrawClipped(Tri t, bool blendPass)
        {
            var l = layers[t.Layer];
            var s = t.P.Select(Project).ToArray();
            if (s.Any(v => v.Z < 0.999f)) return;
            var px = s.Select(v => new Vector2((v.X * 0.5f + 0.5f) * W, (0.5f - v.Y * 0.5f) * H)).ToArray();
            int x0 = Math.Max(0, (int)MathF.Floor(px.Min(p => p.X))), x1 = Math.Min(W - 1, (int)MathF.Ceiling(px.Max(p => p.X)));
            int y0 = Math.Max(0, (int)MathF.Floor(px.Min(p => p.Y))), y1 = Math.Min(H - 1, (int)MathF.Ceiling(px.Max(p => p.Y)));
            float area = (px[1].X - px[0].X) * (px[2].Y - px[0].Y) - (px[2].X - px[0].X) * (px[1].Y - px[0].Y);
            if (MathF.Abs(area) < 1e-6f) return;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var q = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = ((px[1].X - q.X) * (px[2].Y - q.Y) - (px[2].X - q.X) * (px[1].Y - q.Y)) / area;
                    float w1 = ((px[2].X - q.X) * (px[0].Y - q.Y) - (px[0].X - q.X) * (px[2].Y - q.Y)) / area;
                    float w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    // perspective-correct weights
                    float iz = w0 / s[0].Z + w1 / s[1].Z + w2 / s[2].Z;
                    float a0 = w0 / s[0].Z / iz, a1 = w1 / s[1].Z / iz, a2 = w2 / s[2].Z / iz;
                    float z = 1 / iz;
                    int o = y * W + x;
                    if (z >= depth[o]) continue;
                    var p = t.P[0] * a0 + t.P[1] * a1 + t.P[2] * a2;
                    var uv = t.Uv[0] * a0 + t.Uv[1] * a1 + t.Uv[2] * a2;
                    var vc = t.C[0] * a0 + t.C[1] * a1 + t.C[2] * a2;
                    var c = Shade(l, p, uv, vc);
                    if (l.Blend == 1) { if (c.W < 0.5f) continue; color[o] = new Vector3(c.X, c.Y, c.Z); depth[o] = z; top[o] = t.Layer; }
                    else if (l.Blend == 0) { color[o] = new Vector3(c.X, c.Y, c.Z); depth[o] = z; top[o] = t.Layer; }
                    else if (blendPass)
                    {
                        float a = Math.Clamp(c.W, 0, 1);
                        color[o] = l.Blend == 4 ? color[o] + new Vector3(c.X, c.Y, c.Z) * a : Vector3.Lerp(color[o], new Vector3(c.X, c.Y, c.Z), a);
                    }
                }
        }
        foreach (var t in tris.Where(t => layers[t.Layer].Blend is 0 or 1)) Draw(t, false);
        foreach (var t in tris.Where(t => layers[t.Layer].Blend is 2 or 4).OrderByDescending(t => Vector3.Distance((t.P[0] + t.P[1] + t.P[2]) / 3, eye))) Draw(t, true);
        return (color, top);
    }
}
