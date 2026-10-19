using System.Numerics;
using Lordran.Tools.Build.Maps;
using Lordran.Tools.Textures;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// <c>atmos-check render PACK MAP OUT.png EYEX EYEY EYEZ YAW PITCH</c> (session 11, atmos11): the generic form of
/// <c>sky-check render</c> (which is the Asylum cliff only). An offline render of a map's SKY and DISTANT UNLIT background
/// (every DS1 map-piece model whose meshes are all unlit and span ≥ 300 m, sky domes included; the pack's SkyDome2/NoLit
/// models placed by its MSB2 that span ≥ 300 m) from a viewpoint, two panels side by side: left DS1 (DSR data through
/// DS1's shaders: texture × vertex colour × MTD colour, the part's fog + exact scattering, DS1's Hable tone curve at the
/// viewpoint zone's ToneMapBank key/band with the frame's geometric-mean adaptation, ToneCorrect), right the pack
/// ((texture × vertex colour)², RGBM skies, the viewpoint zone's DS2 fog on non-sky pieces, A8c exposure with DS2's
/// (mean √Y)² estimator, Hejl-Dawson, the zone's palette sampled as the game does). No lit geometry, nearest texel, no
/// bloom: numbers for the sky's brightness and colour, not a game frame. Prints per panel the sky-dome and background
/// pixel means (display 0–255), their hue and the frame's adaptation.
/// </summary>
public static class AtmosRender
{
    const int W = 480, H = 270;
    /// <summary>true (default): both panels adapted to the zone's band minimum (sky-only frames have no lit scene to adapt
    /// to); false (last argument "adapt"): each panel adapts on its own frame.</summary>
    public static bool FixedAdaptation = true;

    sealed record Tri(Vector3[] P, Vector2[] Uv, Vector4[] C, int Layer);
    sealed class Layer
    {
        public required Image Tex;
        public float RgbmRange;
        public int Blend;
        public Vector3 Mul = Vector3.One;
        public PARAM.Row? Fog, Scatter;
        public bool Ds2Fog, Dome;
        public string Name = "";
    }

    public static int Run(string pack, string map, string outPng, Vector3 eye, float yawDeg, float pitchDeg)
    {
        string root = Program.DefaultDsrRoot;
        string ds1Map = MapHosts.Ds1Of(map) ?? map, area = ds1Map.Substring(1, 2);
        var msb1 = MSB1.Read(Path.Combine(root, "map", "MapStudio", ds1Map + ".msb"));
        var dp = new Ds1DrawParam(root, area);
        var col = ViewZone.CollisionUnder(root, ds1Map, msb1, eye);
        if (col == null) { Console.WriteLine($"no walkable DS1 collision under {eye}"); return 1; }
        var tm = dp.Row("ToneMapBank", col.ToneMapID)!; var tc = dp.Row("ToneCorrectBank", col.ToneCorrectID)!;
        float key = (float)Ds1DrawParam.Get(tm, "grayKeyValue"), mn = (float)Ds1DrawParam.Get(tm, "minAdaptedLum"), mx = (float)Ds1DrawParam.Get(tm, "maxAdapredLum");
        // the DS2 zone: the MSB2 collision placed for that DS1 collision (same model name stem) → filter id → fltparam fog
        var msb2 = MSB2.Read(Path.Combine(pack, "gamedata", "map", map, map + ".msb"));
        var c2 = msb2.Parts.Collisions.FirstOrDefault(c => c.ModelName.EndsWith("_" + col.ModelName.Substring(1, 4), StringComparison.OrdinalIgnoreCase) && c.Position == col.Position)
            ?? msb2.Parts.Collisions.FirstOrDefault(c => c.ModelName.EndsWith("_" + col.ModelName.Substring(1, 4), StringComparison.OrdinalIgnoreCase));
        int filter = c2?.UnkT13 ?? 0;
        var flt = GPARAM.Read(Path.Combine(pack, "gamedata", "filter", $"filter_{map}.fltparam"));
        var fogC = Atmosphere.Get(flt, "フォグ", "色", filter) switch { System.Drawing.Color fc => new Vector3(fc.R, fc.G, fc.B) / 255f, byte[] fb when fb.Length >= 3 => new Vector3(fb[0], fb[1], fb[2]) / 255f, _ => Vector3.Zero };
        float fogA = Atmosphere.Get(flt, "フォグ", "α", filter) is float fa ? fa : 0;
        Console.WriteLine($"viewpoint {eye} yaw {yawDeg} pitch {pitchDeg} on DS1 {col.Name}: ToneMap {col.ToneMapID} '{tm.Name}' key {key} band {mn}–{mx}, ToneCorrect {col.ToneCorrectID} '{tc.Name}'; DS2 filter {filter} fog ({fogC.X:0.###},{fogC.Y:0.###},{fogC.Z:0.###}) α {fogA:0.##}");
        float yaw = yawDeg * MathF.PI / 180, pitch = pitchDeg * MathF.PI / 180;

        var panels = new List<(string Label, Image Img)>();
        // ---- DS1
        {
            var (tris, layers) = Ds1Scene(root, ds1Map, msb1, dp, Ds1Mtd.Load(root), new Ds1MapTextures(root, ds1Map[..3]));
            var (lin, top) = Raster(tris, layers, eye, yaw, pitch, ds1: true, fogC, fogA);
            LinStats(lin, top, layers);
            double sumLog = 0; foreach (var c in lin) sumLog += Math.Log(Math.Max(1e-5, 0.2126 * c.X + 0.7152 * c.Y + 0.0722 * c.Z));
            float la = (float)Math.Exp(sumLog / lin.Length);
            float e1 = key / (Math.Clamp(FixedAdaptation ? mn : la, mn, mx) + 1e-4f); // fixed: both sides adapted to the band minimum (sky-only frames have no lit scene to adapt to)
            var img = Show(lin, c => Palette.Correct(new Vector3(Disp(c.X * e1), Disp(c.Y * e1), Disp(c.Z * e1)), tc));
            panels.Add(("DS1", img));
            Console.WriteLine($"DS1 (DSR data, DS1 shaders): {tris.Count} triangles in {layers.Count} layers; La {la:0.####} → E {e1:0.###}");
            Stats("DS1", img, top, layers);
        }
        // ---- the pack
        {
            var (tris, layers) = PackScene(pack, map, msb2);
            var (lin, top) = Raster(tris, layers, eye, yaw, pitch, ds1: false, fogC, fogA);
            LinStats(lin, top, layers);
            double sum = 0; foreach (var c in lin) sum += Math.Sqrt(Math.Max(0, 0.299 * c.X + 0.587 * c.Y + 0.114 * c.Z));
            float l2 = (float)Math.Pow(sum / lin.Length, 2);
            float exposure = Tau(key) * key / Math.Clamp(FixedAdaptation ? mn : l2, mn, mx);
            var lut = PaletteLut.FromPack(pack, area, col.ToneCorrectID);
            var img = Show(lin, c =>
            {
                var h = Vector3.Min(new Vector3(Hd(exposure * c.X - 0.004f), Hd(exposure * c.Y - 0.004f), Hd(exposure * c.Z - 0.004f)) * 1.1f, Vector3.One);
                return lut != null ? lut.Sample(h) : Palette.Correct(h, tc);
            });
            panels.Add(("pack", img));
            Console.WriteLine($"pack {pack}: {tris.Count} triangles in {layers.Count} layers; L² {l2:0.####} → exposure {exposure:0.####}, palette {lut?.Name ?? "analytic (not in the pack)"}");
            Stats("pack", img, top, layers);
        }
        var sheet = new Image(W * 2 + 4, H);
        for (int i = 0; i < panels.Count; i++)
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) sheet.Pixels[y * sheet.Width + i * (W + 4) + x] = panels[i].Img.Pixels[y * W + x];
        Png.Save(sheet, outPng);
        Console.WriteLine($"wrote {outPng} (left DS1, right pack)");
        return 0;
    }

    static void LinStats(Vector3[] lin, int[] top, List<Layer> layers)
    {
        Vector3 s = Vector3.Zero; int n = 0;
        for (int i = 0; i < lin.Length; i++) if (top[i] >= 0 && layers[top[i]].Dome) { s += lin[i]; n++; }
        Console.WriteLine(n == 0 ? "  linear sky-dome mean: -" : $"  linear sky-dome mean ({s.X / n:0.####},{s.Y / n:0.####},{s.Z / n:0.####}) over {n} px (scene-referred, before exposure)");
    }

    static void Stats(string label, Image img, int[] top, List<Layer> layers)
    {
        Vector3 dome = Vector3.Zero, bg = Vector3.Zero; int nd = 0, nb = 0;
        for (int i = 0; i < top.Length; i++)
        {
            if (top[i] < 0) continue;
            var p = img.Pixels[i]; var v = new Vector3(p.R, p.G, p.B);
            if (layers[top[i]].Dome) { dome += v; nd++; } else { bg += v; nb++; }
        }
        string S(Vector3 s, int n) => n == 0 ? "-" : $"({s.X / n:0},{s.Y / n:0},{s.Z / n:0}) mean {(s.X + s.Y + s.Z) / 3 / n:0.0} hue {Hue(s / n):0}°";
        Console.WriteLine($"  stats {label}: sky-dome px {nd} {S(dome, nd)} | background px {nb} {S(bg, nb)}");
    }

    static float Hue(Vector3 c)
    {
        float mx = MathF.Max(c.X, MathF.Max(c.Y, c.Z)), mn = MathF.Min(c.X, MathF.Min(c.Y, c.Z)), d = mx - mn;
        if (d < 1e-3f) return 0;
        float h = mx == c.X ? (c.Y - c.Z) / d % 6 : mx == c.Y ? (c.Z - c.X) / d + 2 : (c.X - c.Y) / d + 4;
        return (h * 60 + 360) % 360;
    }

    static float Hable(float x) => (x * (0.15f * x + 0.05f) + 0.004f) / (x * (0.15f * x + 0.5f) + 0.06f) - 0.02f / 0.3f;
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

    static Matrix4x4 World(Vector3 pos, Vector3 rot, Vector3 scale) => Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationZ(rot.Z * MathF.PI / 180)
        * Matrix4x4.CreateRotationX(rot.X * MathF.PI / 180) * Matrix4x4.CreateRotationY(rot.Y * MathF.PI / 180) * Matrix4x4.CreateTranslation(pos);

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
                yield return new Tri(vs.Select(P).ToArray(), vs.Select(v => v.UVs.Count > 0 ? new Vector2(v.UVs[0].X, v.UVs[0].Y) : Vector2.Zero).ToArray(),
                    vs.Select(v => v.Colors.Count > 0 ? new Vector4(v.Colors[0].R, v.Colors[0].G, v.Colors[0].B, v.Colors[0].A) : Vector4.One).ToArray(), layer);
            }
        }
    }

    const float Distant = 300f;

    static (List<Tri>, List<Layer>) Ds1Scene(string root, string ds1Map, MSB1 msb, Ds1DrawParam dp, Ds1Mtd mtds, Ds1MapTextures tex)
    {
        var tris = new List<Tri>(); var layers = new List<Layer>();
        var cache = new Dictionary<string, Image>();
        string area = ds1Map.Substring(1, 2);
        foreach (var part in Sky.DistinctPlacements(msb.Parts.MapPieces, []))
        {
            string file = Path.Combine(root, "map", ds1Map, $"{part.ModelName}A{area}.flver.dcx");
            if (!File.Exists(file)) continue;
            var flv = FLVER2.Read(DCX.Decompress(File.ReadAllBytes(file)));
            var unlitMats = Enumerable.Range(0, flv.Materials.Count).Where(i => MapPieceConverter.IsUnlit(flv.Materials[i].MTD)).ToHashSet();
            if (unlitMats.Count == 0) continue;
            var pts = flv.Meshes.Where(m => unlitMats.Contains(m.MaterialIndex)).SelectMany(m => m.Vertices).Select(v => v.Position).Where(MapPieceConverter.Sane).ToList();
            if (pts.Count == 0) continue;
            var e = pts.Aggregate(Vector3.Max) - pts.Aggregate(Vector3.Min);
            if (MathF.Max(e.X, MathF.Max(e.Y, e.Z)) < Distant) continue;
            var layerOf = new Dictionary<int, int>();
            for (int mi = 0; mi < flv.Materials.Count; mi++)
            {
                var sm = flv.Materials[mi];
                var info = mtds.Get(sm.MTD);
                string t = Path.GetFileNameWithoutExtension(sm.Textures.FirstOrDefault(x => x.ParamName == "g_Diffuse")?.Path?.Replace('\\', '/') ?? "");
                if (!cache.TryGetValue(t, out var img)) cache[t] = img = tex.Dds(t) is byte[] b ? Dds.Read(b).DecodeTop() : new Image(1, 1);
                if (!unlitMats.Contains(mi)) continue;
                int blend = (info?.BlendMode ?? 0) & ~32; // +32 = DS1's light-scattered variant (sky 32 = opaque, shafts 36 = additive)
                blend = blend is 1 or 2 or 4 ? blend : blend == 0 ? 0 : 2;
                layerOf[mi] = layers.Count;
                layers.Add(new Layer { Tex = img, Blend = blend, Mul = info?.Color ?? Vector3.One, Fog = dp.Row("FogBank", part.FogID), Scatter = dp.Row("LightScatteringBank", part.ScatterID),
                    Name = $"{part.ModelName}/{mi}", Dome = Ds1Mtd.IsSky(info, sm.MTD) });
            }
            tris.AddRange(Triangles(flv, World(part.Position, part.Rotation, part.Scale), mi => layerOf.GetValueOrDefault(mi, -1), nodes: true));
        }
        return (tris, layers);
    }

    static (List<Tri>, List<Layer>) PackScene(string pack, string map, MSB2 msb)
    {
        var tris = new List<Tri>(); var layers = new List<Layer>();
        string dir = Path.Combine(pack, "gamedata", "model", "map");
        var mapBxf = BXF4.Read(Path.Combine(dir, map + ".mapbhd"), Path.Combine(dir, map + ".mapbdt"));
        string t0 = "t" + map[1..];
        var texBxf = BXF4.Read(Path.Combine(dir, t0 + ".tpfbhd"), Path.Combine(dir, t0 + ".tpfbdt"));
        var cache = new Dictionary<string, (Image, float)>();
        var flvers = new Dictionary<string, FLVER2?>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in msb.Parts.MapPieces)
        {
            if (!flvers.TryGetValue(part.ModelName, out var flv))
            {
                var f = mapBxf.Files.FirstOrDefault(x => Path.GetFileName(x.Name).Equals(part.ModelName + ".flv.dcx", StringComparison.OrdinalIgnoreCase));
                flv = f == null ? null : FLVER2.Read(DCX.Decompress(f.Bytes));
                if (flv != null)
                {
                    bool unlit = flv.Materials.Count > 0 && flv.Meshes.Any(m => Path.GetFileName(flv.Materials[m.MaterialIndex].MTD) is var mtd && (mtd.StartsWith("SkyDome", StringComparison.OrdinalIgnoreCase) || mtd.StartsWith("NoLit", StringComparison.OrdinalIgnoreCase)));
                    var e = flv.Header.BoundingBoxMax - flv.Header.BoundingBoxMin;
                    if (!unlit || MathF.Max(e.X, MathF.Max(e.Y, e.Z)) < Distant) flv = null;
                }
                flvers[part.ModelName] = flv;
            }
            if (flv == null) continue;
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
                layers.Add(new Layer { Tex = ti.Item1, RgbmRange = ti.Item2, Blend = blend, Ds2Fog = !sky, Name = $"{part.ModelName}/{mi}", Dome = sky && mi == 0 });
            }
            tris.AddRange(Triangles(flv, World(part.Position, part.Rotation, part.Scale), mi => layerOf.GetValueOrDefault(mi, -1), nodes: false));
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
        var top = Enumerable.Repeat(-1, W * H).ToArray();
        (float X, float Y, float Z) Project(Vector3 p) { var d = p - eye; float z = Vector3.Dot(d, fwd); return (Vector3.Dot(d, right) / z * f / aspect, Vector3.Dot(d, up) / z * f, z); }
        Vector4 Shade(Layer l, Vector3 p, Vector2 uv, Vector4 vc)
        {
            var t = Sample(l.Tex, uv);
            if (ds1)
            {
                var c = new Vector3(t.X, t.Y, t.Z) * new Vector3(vc.X, vc.Y, vc.Z) * l.Mul;
                c = Ds1Scatter.Toward(l.Scatter, p, eye).Apply(Vector3.Lerp(c, Sky.FogColor(l.Fog), Sky.FogAmount(l.Fog, Vector3.Distance(p, eye))));
                return new Vector4(MathF.Pow(MathF.Max(c.X, 0), 2.2f), MathF.Pow(MathF.Max(c.Y, 0), 2.2f), MathF.Pow(MathF.Max(c.Z, 0), 2.2f), t.W * vc.W);
            }
            var rgb = (l.RgbmRange > 0 ? new Vector3(t.X, t.Y, t.Z) * t.W * l.RgbmRange : new Vector3(t.X, t.Y, t.Z)) * new Vector3(vc.X, vc.Y, vc.Z);
            var lin = rgb * rgb;
            if (l.Ds2Fog) lin = Vector3.Lerp(lin, zoneFog, zoneFogAlpha);
            return new Vector4(lin, l.RgbmRange > 0 ? 1 : t.W * vc.W);
        }
        void Draw(Tri tri, bool blendPass)
        {
            var poly = new List<(Vector3 P, Vector2 Uv, Vector4 C)> { (tri.P[0], tri.Uv[0], tri.C[0]), (tri.P[1], tri.Uv[1], tri.C[1]), (tri.P[2], tri.Uv[2], tri.C[2]) };
            float Zf(Vector3 p) => Vector3.Dot(p - eye, fwd) - 1;
            var clipped = new List<(Vector3 P, Vector2 Uv, Vector4 C)>();
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                float za = Zf(a.P), zb = Zf(b.P);
                if (za >= 0) clipped.Add(a);
                if ((za >= 0) != (zb >= 0)) { float k = za / (za - zb); clipped.Add((Vector3.Lerp(a.P, b.P, k), Vector2.Lerp(a.Uv, b.Uv, k), Vector4.Lerp(a.C, b.C, k))); }
            }
            for (int i = 1; i + 1 < clipped.Count; i++)
                DrawClipped(new Tri([clipped[0].P, clipped[i].P, clipped[i + 1].P], [clipped[0].Uv, clipped[i].Uv, clipped[i + 1].Uv], [clipped[0].C, clipped[i].C, clipped[i + 1].C], tri.Layer), blendPass);
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
                    float iz = w0 / s[0].Z + w1 / s[1].Z + w2 / s[2].Z;
                    float a0 = w0 / s[0].Z / iz, a1 = w1 / s[1].Z / iz, a2 = w2 / s[2].Z / iz;
                    float z = 1 / iz;
                    int o = y * W + x;
                    if (z >= depth[o]) continue;
                    var c = Shade(l, t.P[0] * a0 + t.P[1] * a1 + t.P[2] * a2, t.Uv[0] * a0 + t.Uv[1] * a1 + t.Uv[2] * a2, t.C[0] * a0 + t.C[1] * a1 + t.C[2] * a2);
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
        if (Environment.GetEnvironmentVariable("LR_RENDER_OPAQUE") != "1") foreach (var t in tris.Where(t => layers[t.Layer].Blend is 2 or 4).OrderByDescending(t => Vector3.Distance((t.P[0] + t.P[1] + t.P[2]) / 3, eye))) Draw(t, true);
        return (color, top);
    }
}
