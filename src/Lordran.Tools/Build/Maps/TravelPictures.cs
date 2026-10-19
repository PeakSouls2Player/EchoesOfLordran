using System.Numerics;
using Lordran.Tools.Textures;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Session 11 (travel12), rule TP1: the Travel menu pictures of the DS1 bonfires (vanilla: one 256x128 DXT5 TPF per area,
/// <c>ic_area_&lt;menuAreaID:D9&gt;</c>, and per bonfire, <c>ic_list_&lt;bonfireId:D9&gt;</c>, a ~180x100 / ~150x80 picture of the
/// place inside a torn-edge alpha frame). DSR has no such pictures, so they are rendered offline from DS1's own data: the
/// DS1 map pieces of the bonfire's DS1 map (DistinctPlacements, rule S4) with their diffuse texture x DS1 lightmap x vertex
/// colour x MTD colour (unlit / sky materials: texture x vertex colour x MTD colour), DS1's per-part FogBank fog, alpha-test
/// and blended materials, plus DS1's bonfire object o0200 and a fire glow at the bonfire. The viewpoint per bonfire is data
/// (<c>Mappings\travel-views.tsv</c>: eye, yaw, pitch, field of view, exposure); without a row an automatic view 4.5 m
/// behind the bonfire (its facing) looking past it is used. Before session 11 every DS1 area/bonfire except the Asylum and
/// Firelink (in-game screenshot crops, data\lordran\travel\HOST.png) carried the template map's (Things Betwixt's) picture.
/// </summary>
public static class TravelPictures
{
    /// <summary>A viewpoint: DS1 world coordinates (shared by all DS1 maps), yaw like the MSB (facing (-sin, -cos)), pitch
    /// up positive, horizontal field of view in degrees, exposure multiplier (1 = DS1 lightmaps as baked).</summary>
    public sealed record View(string Ds1Map, int Entity, Vector3 Eye, float Yaw, float Pitch, float Fov, float Exposure, string Note);

    static List<View>? views;

    /// <summary>Mappings/travel-views.tsv: DS1 map | DS1 bonfire entity | eye x | y | z | yaw | pitch | fov | exposure | note.</summary>
    public static List<View> Views()
    {
        if (views != null) return views;
        var list = new List<View>();
        string path = Path.Combine(AppContext.BaseDirectory, "Mappings", "travel-views.tsv");
        if (File.Exists(path))
            foreach (var line in File.ReadLines(path))
            {
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var c = line.Split('\t');
                if (c.Length < 9) throw new InvalidDataException($"travel-views.tsv: {line}");
                float F(int i) => float.Parse(c[i], System.Globalization.CultureInfo.InvariantCulture);
                list.Add(new View(c[0], int.Parse(c[1]), new Vector3(F(2), F(3), F(4)), F(5), F(6), F(7), F(8), c.Length > 9 ? c[9] : ""));
            }
        return views = list;
    }

    /// <summary>The view of a DS1 bonfire: its tsv row, else 4.5 m behind it (DS1 facing) and 1.8 m up, looking past it.</summary>
    public static View ViewFor(string ds1Map, int entity, Vector3 bonfirePos, Vector3 bonfireRot)
    {
        var row = Views().FirstOrDefault(v => v.Entity == entity && v.Ds1Map == ds1Map);
        if (row != null) return row;
        float yaw = bonfireRot.Y * MathF.PI / 180;
        var facing = new Vector3(-MathF.Sin(yaw), 0, -MathF.Cos(yaw));
        return new View(ds1Map, entity, bonfirePos - facing * 4.5f + new Vector3(0, 1.8f, 0), bonfireRot.Y, -12, 70, 1, "automatic");
    }

    /// <summary>Picture size rendered for an icon (the vanilla area frame's opaque box is ~180x100; the list frame's
    /// ~150x80; the picture is fitted into the frame by MapBuilder.Register).</summary>
    public const int PictureWidth = 360, PictureHeight = 200;

    static readonly Dictionary<string, (Image Img, string Line)> rendered = new();

    /// <summary>The DS1 map and original entity of a (possibly H2-merged) map's bonfire: the member map whose original MSB has
    /// an o0200 part at the bonfire's position (all DS1 maps share world coordinates).</summary>
    public static (string Ds1Map, MSB1.Part.Object Part) Origin(string dsrRoot, string ds1Map, MapBuilder.BonfirePlan bp)
    {
        var members = new List<string> { ds1Map };
        if (H2Merge.GroupOf(ds1Map) is { } g) members.AddRange(g.Secondaries.Prepend(g.Primary).Where(m => !members.Contains(m)));
        foreach (string m in members)
        {
            var msb = H2Merge.Original(() => MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", m + ".msb")));
            if (msb.Parts.Objects.FirstOrDefault(p => p.ModelName == "o0200" && Vector3.Distance(p.Position, bp.Position) < 0.05f) is { } part) return (m, part);
        }
        throw new InvalidDataException($"rule TP1: no DS1 o0200 part at {bp.Position} (bonfire {bp.Id}) in {string.Join(", ", members)}");
    }

    /// <summary>The rendered list picture of a DS1 bonfire (cached per DS1 map + entity) and a build-log line.</summary>
    public static (Image Img, string Line) ForBonfire(string dsrRoot, string ds1Map, MapBuilder.BonfirePlan bp)
    {
        var (map, part) = Origin(dsrRoot, ds1Map, bp);
        string key = $"{map}|{part.EntityID}";
        if (rendered.TryGetValue(key, out var hit)) return hit;
        var view = ViewFor(map, part.EntityID, part.Position, part.Rotation);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var img = Render(dsrRoot, view, PictureWidth, PictureHeight, part.Position, part.Rotation);
        string line = $"rule TP1: Travel picture of bonfire {bp.Id} \"{bp.Name}\" rendered from {map} (DS1 bonfire {part.EntityID}), view eye ({view.Eye.X:0.##}, {view.Eye.Y:0.##}, {view.Eye.Z:0.##}) yaw {view.Yaw} pitch {view.Pitch} fov {view.Fov} ({view.Note}), {Mean(img):0.0} mean, {sw.Elapsed.TotalSeconds:0.0} s";
        return rendered[key] = (img, line);
    }

    /// <summary>The mean display value of a picture (the build log's and the checks' "not blank" number).</summary>
    public static double Mean(Image img) => img.Pixels.Average(p => (p.R + p.G + p.B) / 3.0);

    sealed class Mat
    {
        public Image? Diffuse, Lightmap;
        public Vector3 Mul = Vector3.One;
        public int Blend;          // 0 opaque, 1 alpha test, 2 alpha, 4 additive
        public bool Unlit;
        public PARAM.Row? Fog;
        public Lights? Light;
    }

    /// <summary>DS1 LightBank row of the part: hemisphere up/down ambient and three directional lights (colour × colA/100).</summary>
    sealed record Lights(Vector3 Up, Vector3 Down, (Vector3 ToLight, Vector3 Color)[] Dirs)
    {
        public static Lights? From(PARAM.Row? r)
        {
            if (r == null) return null;
            static double D(PARAM.Row r, string f) => Ds1DrawParam.Get(r, f) is var x && double.IsNaN(x) ? 0 : x;
            Vector3 C(string s) => new Vector3((float)D(r, "colR" + s), (float)D(r, "colG" + s), (float)D(r, "colB" + s)) / 255f * (float)(D(r, "colA" + s) / 100);
            var dirs = Enumerable.Range(0, 3).Select(i =>
            {
                float x = (float)(D(r, $"degRotX_{i}") * Math.PI / 180), y = (float)(D(r, $"degRotY_{i}") * Math.PI / 180);
                return (Vector3.Normalize(new Vector3(-MathF.Cos(x) * MathF.Sin(y), MathF.Sin(x), -MathF.Cos(x) * MathF.Cos(y))), C($"_{i}"));
            }).ToArray();
            return new Lights(C("_u"), C("_d"), dirs);
        }
        public Vector3 At(Vector3 n)
        {
            var c = Vector3.Lerp(Down, Up, n.Y * 0.5f + 0.5f);
            foreach (var (l, col) in Dirs) c += col * MathF.Max(0, Vector3.Dot(n, l));
            return c;
        }
    }

    readonly record struct Vtx(Vector3 P, Vector2 Uv, Vector2 Lm, Vector4 C, Vector3 N = default);
    sealed record Tri(Vtx A, Vtx B, Vtx C, int Mat);

    /// <summary>Renders the DS1 scene of <paramref name="v"/> (its DS1 map; original DSR files, also while an H2 merge layer
    /// is active) to a <paramref name="w"/> x <paramref name="h"/> picture, 2x supersampled. The bonfire at
    /// <paramref name="bonfire"/> (null = none) gets DS1's o0200 model and a fire glow.</summary>
    public static Image Render(string dsrRoot, View v, int w, int h, Vector3? bonfire, Vector3 bonfireRot = default)
        => H2Merge.Original(() => RenderCore(dsrRoot, v, w, h, bonfire, bonfireRot));

    static Image RenderCore(string dsrRoot, View v, int w, int h, Vector3? bonfire, Vector3 bonfireRot)
    {
        string ds1Map = v.Ds1Map, area = ds1Map.Substring(1, 2);
        var msb = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", ds1Map + ".msb"));
        var dp = new Ds1DrawParam(dsrRoot, area);
        var mtds = Ds1Mtd.Load(dsrRoot);
        var tex = new Ds1MapTextures(dsrRoot, ds1Map[..3]);
        var images = new Dictionary<string, Image?>(StringComparer.OrdinalIgnoreCase);
        Image? Tex(string? path, Func<string, byte[]?> find)
        {
            string n = Path.GetFileNameWithoutExtension((path ?? "").Replace('\\', '/'));
            if (n.Length == 0) return null;
            if (!images.TryGetValue(n, out var img))
            {
                try { img = find(n) is byte[] b ? Dds.Read(b).DecodeTop() : null; } catch { img = null; }
                images[n] = img;
            }
            return img;
        }

        var tris = new List<Tri>();
        var mats = new List<Mat>();
        const float Near = 220f, Distant = 300f;
        void AddFlver(FLVER2 flv, Matrix4x4 world, PARAM.Row? fog, Lights? light, Func<string, byte[]?> find)
        {
            var nw = flv.Nodes.Select(n => { var m = n.ComputeLocalTransform(); for (int q = n.ParentIndex, g = 0; q >= 0 && g < 64; q = flv.Nodes[q].ParentIndex, g++) m *= flv.Nodes[q].ComputeLocalTransform(); return m; }).ToArray();
            var matIndex = new Dictionary<int, int>();
            foreach (var mesh in flv.Meshes)
            {
                if (!matIndex.TryGetValue(mesh.MaterialIndex, out int mi))
                {
                    var sm = flv.Materials[mesh.MaterialIndex];
                    var info = mtds.Get(sm.MTD);
                    int blend = (info?.BlendMode ?? 0) & ~32;
                    blend = blend is 0 or 1 or 2 or 4 ? blend : 2;
                    var diffuse = Tex(sm.Textures.FirstOrDefault(x => x.ParamName == "g_Diffuse")?.Path, find);
                    var mat = new Mat
                    {
                        Diffuse = diffuse,
                        Lightmap = Tex(sm.Textures.FirstOrDefault(x => x.ParamName == "g_Lightmap")?.Path, find),
                        Mul = info?.Color is { } c && c != Vector3.Zero ? c : Vector3.One,
                        Blend = blend, Unlit = MapPieceConverter.IsUnlit(sm.MTD), Fog = fog, Light = light,
                    };
                    // DS1's alpha-tested foliage / grates: 1-bit alpha in an opaque blend mode
                    if (mat.Blend == 0 && sm.MTD.Contains("_Alp", StringComparison.OrdinalIgnoreCase)) mat.Blend = 1;
                    matIndex[mesh.MaterialIndex] = mi = mats.Count;
                    mats.Add(mat);
                }
                var fs = mesh.FaceSets.FirstOrDefault(f => f.Flags == FLVER2.FaceSet.FSFlags.None) ?? mesh.FaceSets.FirstOrDefault();
                if (fs == null) continue;
                var idx = fs.Triangulate(allowPrimitiveRestarts: mesh.Vertices.Count < ushort.MaxValue);
                Vtx V(FLVER.Vertex x)
                {
                    var m = Matrix4x4.Identity;
                    int node = mesh.BoneIndices.Count > 0 ? mesh.BoneIndices[Math.Clamp(x.BoneIndices[0], 0, mesh.BoneIndices.Count - 1)] : mesh.NodeIndex;
                    if (node >= 0 && node < nw.Length) m = nw[node];
                    var uv = x.UVs.Count > 0 ? new Vector2(x.UVs[0].X, x.UVs[0].Y) : Vector2.Zero;
                    var lm = x.UVs.Count > 1 ? new Vector2(x.UVs[^1].X, x.UVs[^1].Y) : uv;
                    var col = x.Colors.Count > 0 ? new Vector4(x.Colors[0].R, x.Colors[0].G, x.Colors[0].B, x.Colors[0].A) : Vector4.One;
                    var nrm = Vector3.TransformNormal(Vector3.TransformNormal(new Vector3(x.Normal.X, x.Normal.Y, x.Normal.Z), m), world);
                    return new Vtx(Vector3.Transform(Vector3.Transform(x.Position, m), world), uv, lm, col, nrm.LengthSquared() > 1e-8f ? Vector3.Normalize(nrm) : Vector3.UnitY);
                }
                for (int i = 0; i + 2 < idx.Count; i += 3)
                {
                    var a = mesh.Vertices[idx[i]]; var b = mesh.Vertices[idx[i + 1]]; var c = mesh.Vertices[idx[i + 2]];
                    if (!MapPieceConverter.Sane(a.Position) || !MapPieceConverter.Sane(b.Position) || !MapPieceConverter.Sane(c.Position)) continue;
                    tris.Add(new Tri(V(a), V(b), V(c), mi));
                }
            }
        }

        foreach (var part in Sky.DistinctPlacements(msb.Parts.MapPieces, []))
        {
            string file = Path.Combine(dsrRoot, "map", ds1Map, $"{part.ModelName}A{area}.flver.dcx");
            if (!File.Exists(file)) continue;
            var flv = FLVER2.Read(DCX.Decompress(File.ReadAllBytes(file)));
            var world = Matrix4x4.CreateScale(part.Scale) * Matrix4x4.CreateRotationZ(part.Rotation.Z * MathF.PI / 180)
                * Matrix4x4.CreateRotationX(part.Rotation.X * MathF.PI / 180) * Matrix4x4.CreateRotationY(part.Rotation.Y * MathF.PI / 180) * Matrix4x4.CreateTranslation(part.Position);
            // near pieces and the big background / sky ones only
            var lo = flv.Header.BoundingBoxMin; var hi = flv.Header.BoundingBoxMax;
            var span = hi - lo;
            var center = Vector3.Transform((lo + hi) / 2, world);
            float radius = span.Length() / 2 * MathF.Max(part.Scale.X, MathF.Max(part.Scale.Y, part.Scale.Z));
            if (Vector3.Distance(center, v.Eye) - radius > Near && MathF.Max(span.X, MathF.Max(span.Y, span.Z)) < Distant) continue;
            AddFlver(flv, world, dp.Row("FogBank", part.FogID), Lights.From(dp.Row("LightBank", part.LightID)), tex.Dds);
        }
        // DS1's bonfire (o0200: the coiled sword in its ash mound), when the view shows one
        if (bonfire is Vector3 bp && File.Exists(Path.Combine(dsrRoot, "obj", "o0200.objbnd.dcx")))
        {
            var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(dsrRoot, "obj", "o0200.objbnd.dcx"))));
            var objTex = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase)))
                foreach (var t in TPF.Read(f.Bytes).Textures) objTex.TryAdd(t.Name, t.Bytes);
            var fl = bnd.Files.FirstOrDefault(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase));
            var nearest = msb.Parts.MapPieces.OrderBy(p => Vector3.Distance(p.Position, bp)).FirstOrDefault();
            if (fl != null)
                AddFlver(FLVER2.Read(fl.Bytes), Matrix4x4.CreateRotationY(bonfireRot.Y * MathF.PI / 180) * Matrix4x4.CreateTranslation(bp),
                    nearest != null ? dp.Row("FogBank", nearest.FogID) : null, nearest != null ? Lights.From(dp.Row("LightBank", nearest.LightID)) : null, n => objTex.GetValueOrDefault(n) ?? tex.Dds(n));
        }

        // ---- raster (2x supersampled), DS1-like shading in display space
        int W = w * 2, H = h * 2;
        float yaw = v.Yaw * MathF.PI / 180, pitch = v.Pitch * MathF.PI / 180;
        var fwd = new Vector3(-MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), -MathF.Cos(yaw) * MathF.Cos(pitch));
        var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
        var up = Vector3.Cross(right, fwd);
        float fx = 1 / MathF.Tan(v.Fov * MathF.PI / 360), aspect = (float)W / H;
        var color = new Vector3[W * H];
        var depth = new float[W * H];
        Array.Fill(depth, float.MaxValue);
        // background (nothing drawn): the fog colour of the nearest piece, darkened
        var bgFog = Sky.FogColor(dp.Row("FogBank", msb.Parts.MapPieces.OrderBy(p => Vector3.Distance(p.Position, v.Eye)).FirstOrDefault()?.FogID ?? 0)) * 0.6f;
        Array.Fill(color, bgFog);
        Vector4[]? debug = Environment.GetEnvironmentVariable("LR_TP_DEBUG") == "1" ? new Vector4[5] : null;

        static Vector4 Sample(Image img, Vector2 uv)
        {
            float u = uv.X - MathF.Floor(uv.X), t = uv.Y - MathF.Floor(uv.Y);
            int x = Math.Clamp((int)(u * img.Width), 0, img.Width - 1), y = Math.Clamp((int)(t * img.Height), 0, img.Height - 1);
            var p = img.Pixels[y * img.Width + x];
            return new Vector4(p.R, p.G, p.B, p.A) / 255f;
        }
        Vector4 Shade(Mat m, Vector3 p, Vtx q)
        {
            var t = m.Diffuse != null ? Sample(m.Diffuse, q.Uv) : new Vector4(0.5f, 0.5f, 0.5f, 1);
            var c = new Vector3(t.X, t.Y, t.Z);
            var lmc = m.Lightmap != null ? Sample(m.Lightmap, q.Lm) : Vector4.Zero;
            if (m.Unlit) c *= m.Mul * new Vector3(q.C.X, q.C.Y, q.C.Z);
            else
            {
                // DS1 map lighting: the baked lightmap (direct light) + the part's LightBank (hemisphere ambient + 3 directional)
                var n = q.N.LengthSquared() > 1e-8f ? Vector3.Normalize(q.N) : Vector3.UnitY;
                var light = (m.Light?.At(n) ?? new Vector3(0.35f)) + new Vector3(lmc.X, lmc.Y, lmc.Z) * 2f;
                c *= light;
            }
            float fogK = Sky.FogAmount(m.Fog, Vector3.Distance(p, v.Eye));
            if (debug != null) { debug[0] += new Vector4(t.X, t.Y, t.Z, 1); debug[1] += lmc; debug[2] += q.C; debug[3] += new Vector4(m.Mul, fogK); debug[4] += new Vector4(Sky.FogColor(m.Fog), m.Unlit ? 1 : 0); }
            c = Vector3.Lerp(c, Sky.FogColor(m.Fog), fogK);
            return new Vector4(c, t.W * q.C.W);
        }
        (float X, float Y, float Z) Project(Vector3 p) { var d = p - v.Eye; float z = Vector3.Dot(d, fwd); return (Vector3.Dot(d, right) / z * fx, Vector3.Dot(d, up) / z * fx * aspect, z); }
        Vtx Lerp(Vtx a, Vtx b, float k) => new(Vector3.Lerp(a.P, b.P, k), Vector2.Lerp(a.Uv, b.Uv, k), Vector2.Lerp(a.Lm, b.Lm, k), Vector4.Lerp(a.C, b.C, k), Vector3.Lerp(a.N, b.N, k));
        void Draw(Tri tri, bool blendPass)
        {
            var poly = new[] { tri.A, tri.B, tri.C };
            float Zf(Vtx x) => Vector3.Dot(x.P - v.Eye, fwd) - 0.1f;
            var clipped = new List<Vtx>(4);
            for (int i = 0; i < 3; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % 3];
                float za = Zf(a), zb = Zf(b);
                if (za >= 0) clipped.Add(a);
                if ((za >= 0) != (zb >= 0)) clipped.Add(Lerp(a, b, za / (za - zb)));
            }
            for (int i = 1; i + 1 < clipped.Count; i++) DrawClipped(clipped[0], clipped[i], clipped[i + 1], mats[tri.Mat], blendPass);
        }
        void DrawClipped(Vtx a, Vtx b, Vtx c, Mat m, bool blendPass)
        {
            var s = new[] { Project(a.P), Project(b.P), Project(c.P) };
            var px = s.Select(q => new Vector2((q.X * 0.5f + 0.5f) * W, (0.5f - q.Y * 0.5f) * H)).ToArray();
            int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(px[0].X, MathF.Min(px[1].X, px[2].X)))), x1 = Math.Min(W - 1, (int)MathF.Ceiling(MathF.Max(px[0].X, MathF.Max(px[1].X, px[2].X))));
            int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(px[0].Y, MathF.Min(px[1].Y, px[2].Y)))), y1 = Math.Min(H - 1, (int)MathF.Ceiling(MathF.Max(px[0].Y, MathF.Max(px[1].Y, px[2].Y))));
            if (x0 > x1 || y0 > y1) return;
            float ar = (px[1].X - px[0].X) * (px[2].Y - px[0].Y) - (px[2].X - px[0].X) * (px[1].Y - px[0].Y);
            if (MathF.Abs(ar) < 1e-6f) return;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float qx = x + 0.5f, qy = y + 0.5f;
                    float w0 = ((px[1].X - qx) * (px[2].Y - qy) - (px[2].X - qx) * (px[1].Y - qy)) / ar;
                    float w1 = ((px[2].X - qx) * (px[0].Y - qy) - (px[0].X - qx) * (px[2].Y - qy)) / ar;
                    float w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    float iz = w0 / s[0].Z + w1 / s[1].Z + w2 / s[2].Z;
                    float z = 1 / iz;
                    int o = y * W + x;
                    if (z >= depth[o]) continue;
                    float a0 = w0 / s[0].Z * z, a1 = w1 / s[1].Z * z, a2 = w2 / s[2].Z * z;
                    var q = new Vtx(a.P * a0 + b.P * a1 + c.P * a2, a.Uv * a0 + b.Uv * a1 + c.Uv * a2, a.Lm * a0 + b.Lm * a1 + c.Lm * a2, a.C * a0 + b.C * a1 + c.C * a2, a.N * a0 + b.N * a1 + c.N * a2);
                    var col = Shade(m, q.P, q);
                    var rgb = new Vector3(col.X, col.Y, col.Z);
                    if (m.Blend == 0) { color[o] = rgb; depth[o] = z; }
                    else if (m.Blend == 1) { if (col.W < 0.5f) continue; color[o] = rgb; depth[o] = z; }
                    else if (blendPass)
                    {
                        float al = Math.Clamp(col.W, 0, 1);
                        color[o] = m.Blend == 4 ? color[o] + rgb * al : Vector3.Lerp(color[o], rgb, al);
                    }
                }
        }
        tris.RemoveAll(t => mats[t.Mat].Diffuse == null); // no texture: helper / shadow geometry DS1 does not show plainly
        foreach (var t in tris.Where(t => mats[t.Mat].Blend is 0 or 1)) Draw(t, false);
        if (debug != null)
        {
            float n = debug[0].W;
            Console.WriteLine($"debug: {tris.Count} tris, {mats.Count} mats ({mats.Count(m => m.Diffuse == null)} no diffuse, {mats.Count(m => m.Lightmap == null)} no lightmap); shaded {n}: diffuse {debug[0] / n}, lightmap {debug[1] / n}, vc {debug[2] / n}, mul+fogK {debug[3] / n}, fogC+unlit {debug[4] / n}");
        }
        foreach (var t in tris.Where(t => mats[t.Mat].Blend is 2 or 4).OrderByDescending(t => Vector3.DistanceSquared((t.A.P + t.B.P + t.C.P) / 3, v.Eye))) Draw(t, true);

        // exposure: the frame's mean luminance to a fixed key (DS1 adapts its tone map to the frame too; a sunlit courtyard
        // and a dark hall both end up readable), times the view's exposure
        {
            double sum = 0;
            foreach (var c in color) sum += 0.2126 * c.X + 0.7152 * c.Y + 0.0722 * c.Z;
            float mean = (float)(sum / color.Length);
            float k = Math.Clamp(0.36f / MathF.Max(mean, 1e-3f), 0.35f, 2.5f) * v.Exposure;
            for (int i = 0; i < color.Length; i++) color[i] *= k;
            if (debug != null) Console.WriteLine($"exposure: frame mean {mean:0.###} → x{k:0.##}");
        }

        // the fire: a warm additive glow around the bonfire's flame (DS1's fire is an SFX the renderer has not got)
        if (bonfire is Vector3 fire)
        {
            var f = fire + new Vector3(0, 0.45f, 0);
            var pr = Project(f);
            if (pr.Z > 0.3f)
            {
                float cx = (pr.X * 0.5f + 0.5f) * W, cy = (0.5f - pr.Y * 0.5f) * H;
                float rad = 1.4f / pr.Z * fx * W / 2; // ~1.4 m of light
                for (int y = Math.Max(0, (int)(cy - rad)); y < Math.Min(H, (int)(cy + rad)); y++)
                    for (int x = Math.Max(0, (int)(cx - rad)); x < Math.Min(W, (int)(cx + rad)); x++)
                    {
                        float d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / rad;
                        if (d >= 1) continue;
                        int o = y * W + x;
                        bool visible = depth[o] >= pr.Z - 0.8f;
                        float k = (1 - d) * (1 - d) * (visible ? 1f : 0.35f);
                        color[o] += new Vector3(1.0f, 0.55f, 0.18f) * k * 0.9f;
                        if (visible && d < 0.22f) color[o] += new Vector3(1.0f, 0.8f, 0.45f) * (1 - d / 0.22f) * 0.8f;
                    }
            }
        }

        var big = new Image(W, H);
        for (int i = 0; i < color.Length; i++)
        {
            var c = color[i];
            // a soft shoulder instead of a hard clip (the baked lightmaps x 2 exceed 1 in lit spots)
            static byte T(float x) { x = MathF.Max(0, x); float y = x < 0.8f ? x : 0.8f + 0.2f * (1 - MathF.Exp(-(x - 0.8f) / 0.2f)); return (byte)Math.Clamp(MathF.Round(y * 255), 0, 255); }
            big.Pixels[i] = new Rgba(T(c.X), T(c.Y), T(c.Z), 255);
        }
        return big.ResizeArea(w, h);
    }
}
