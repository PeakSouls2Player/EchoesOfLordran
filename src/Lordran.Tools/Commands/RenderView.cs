using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// render-view SOURCE EYEX EYEY EYEZ YAWDEG PITCHDEG OUT.png [FOV] — a quick z-buffered software render of map geometry
/// from a viewpoint, to check map conversions without the game. SOURCE: ds1:mAA_BB_CC_DD (DSR's MSB + map piece FLVERs)
/// or pack:mAA_BB_CC_DD (the converted map in the content pack). Front faces (DS2 winding: geometric normal against the
/// vertex normal) are shaded gray by lighting, back faces red; face sets that don't cull back faces are shaded blue-gray
/// on both sides. Yaw 0 looks along -Z (DS1/DS2 "rotation Y 180" = facing -Z… yaw is measured like the MSB: facing =
/// (-sin yaw, -cos yaw)), pitch > 0 looks up.
/// </summary>
public static class RenderView
{
    public static int Run(string[] args)
    {
        string source = args[1];
        var eye = new Vector3(float.Parse(args[2]), float.Parse(args[3]), float.Parse(args[4]));
        float yaw = float.Parse(args[5]) * MathF.PI / 180, pitch = float.Parse(args[6]) * MathF.PI / 180;
        string outPath = args[7];
        float fov = (args.Length > 8 && !args[8].StartsWith("--") ? float.Parse(args[8]) : 75) * MathF.PI / 180;
        const int W = 960, H = 540;

        var tris = new List<(Vector3 A, Vector3 B, Vector3 C, Vector3 N, bool TwoSided)>();
        void Add(FLVER2 flver, Matrix4x4 m)
        {
            foreach (var mesh in flver.Meshes)
            {
                var fs = mesh.FaceSets.FirstOrDefault(f => f.Flags == FLVER2.FaceSet.FSFlags.None);
                if (fs == null) continue;
                var idx = fs.Triangulate(mesh.Vertices.Count < ushort.MaxValue);
                for (int i = 0; i + 2 < idx.Count; i += 3)
                {
                    var va = mesh.Vertices[idx[i]]; var vb = mesh.Vertices[idx[i + 1]]; var vc = mesh.Vertices[idx[i + 2]];
                    var a = Vector3.Transform(va.Position, m); var b = Vector3.Transform(vb.Position, m); var c = Vector3.Transform(vc.Position, m);
                    if (float.IsNaN(a.X + b.X + c.X) || Vector3.DistanceSquared(a, eye) > 120 * 120) continue;
                    var n = Vector3.TransformNormal(new Vector3(va.Normal.X + vb.Normal.X + vc.Normal.X, va.Normal.Y + vb.Normal.Y + vc.Normal.Y, va.Normal.Z + vb.Normal.Z + vc.Normal.Z), m);
                    tris.Add((a, b, c, Vector3.Normalize(n), !fs.CullBackfaces));
                }
            }
        }
        static Matrix4x4 Place(Vector3 pos, Vector3 rotDeg, Vector3 scale)
        {
            const float d = MathF.PI / 180;
            return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationZ(rotDeg.Z * d) * Matrix4x4.CreateRotationX(rotDeg.X * d) * Matrix4x4.CreateRotationY(rotDeg.Y * d) * Matrix4x4.CreateTranslation(pos);
        }
        string map = source[(source.IndexOf(':') + 1)..];
        if (source.StartsWith("ds1:"))
        {
            var msb = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", map + ".msb"));
            string area = map.Substring(1, 2);
            foreach (var part in msb.Parts.MapPieces)
            {
                string file = Path.Combine(Program.DefaultDsrRoot, "map", map, $"{part.ModelName}A{area}.flver.dcx");
                if (File.Exists(file)) Add(FLVER2.Read(DCX.Decompress(File.ReadAllBytes(file))), Place(part.Position, part.Rotation, part.Scale));
            }
        }
        else
        {
            // session 11 (burg11): --pack DIR renders a sandbox pack instead of the installed one
            string packDir = args.SkipWhile(a => a != "--pack").Skip(1).FirstOrDefault() ?? Build.Pack.DefaultOut;
            string dir = Path.Combine(packDir, "gamedata", "model", "map");
            var bxf = BXF4.Read(Path.Combine(dir, map + ".mapbhd"), Path.Combine(dir, map + ".mapbdt"));
            var msb = MSB2.Read(Path.Combine(packDir, "gamedata", "map", map, map + ".msb"));
            foreach (var part in msb.Parts.MapPieces)
            {
                var file = bxf.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith(part.ModelName + ".flv", StringComparison.OrdinalIgnoreCase));
                if (file != null) Add(FLVER2.Read(DCX.Decompress(file.Bytes)), Place(part.Position, part.Rotation, part.Scale));
            }
        }

        // camera: forward from yaw/pitch (yaw 0 = -Z, like an MSB rotation of 180)
        var fwd = Vector3.Normalize(new Vector3(-MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), -MathF.Cos(yaw) * MathF.Cos(pitch)));
        var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
        var up = Vector3.Cross(right, fwd);
        float f = 1 / MathF.Tan(fov / 2), aspect = W / (float)H;
        var depth = new float[W * H]; Array.Fill(depth, float.MaxValue);
        var color = new byte[W * H * 3];
        for (int i = 0; i < W * H; i++) { color[i * 3] = 30; color[i * 3 + 1] = 40; color[i * 3 + 2] = 60; } // sky
        var light = Vector3.Normalize(new Vector3(0.4f, 0.8f, 0.3f));
        int drawn = 0;
        foreach (var t in tris)
        {
            Vector3 V(Vector3 p) { var d = p - eye; return new Vector3(Vector3.Dot(d, right), Vector3.Dot(d, up), Vector3.Dot(d, fwd)); }
            var a = V(t.A); var b = V(t.B); var c = V(t.C);
            if (a.Z < 0.05f || b.Z < 0.05f || c.Z < 0.05f) continue; // no near-plane clipping: skip triangles crossing it
            Vector2 P(Vector3 v) => new((v.X / v.Z * f / aspect * 0.5f + 0.5f) * W, (0.5f - v.Y / v.Z * f * 0.5f) * H);
            var pa = P(a); var pb = P(b); var pc = P(c);
            float area = (pb.X - pa.X) * (pc.Y - pa.Y) - (pb.Y - pa.Y) * (pc.X - pa.X);
            if (MathF.Abs(area) < 1e-6f) continue;
            var geo = Vector3.Cross(t.B - t.A, t.C - t.A);
            bool facingCamera = Vector3.Dot(geo, eye - t.A) < 0; // DS winding: front = geometric normal against vertex normal
            float shade = 0.35f + 0.65f * MathF.Max(0, Vector3.Dot(t.N, light));
            (byte r, byte g, byte bl) col = t.TwoSided ? ((byte)(110 * shade), (byte)(140 * shade), (byte)(200 * shade))
                : facingCamera ? ((byte)(200 * shade), (byte)(200 * shade), (byte)(190 * shade)) : ((byte)(230 * shade), (byte)(40 * shade), (byte)(40 * shade));
            int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(pa.X, MathF.Min(pb.X, pc.X)))), x1 = Math.Min(W - 1, (int)MathF.Ceiling(MathF.Max(pa.X, MathF.Max(pb.X, pc.X))));
            int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(pa.Y, MathF.Min(pb.Y, pc.Y)))), y1 = Math.Min(H - 1, (int)MathF.Ceiling(MathF.Max(pa.Y, MathF.Max(pb.Y, pc.Y))));
            if (x1 - x0 > 4 * W || y1 - y0 > 4 * H) continue;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float w0 = ((pb.X - px) * (pc.Y - py) - (pb.Y - py) * (pc.X - px)) / area;
                    float w1 = ((pc.X - px) * (pa.Y - py) - (pc.Y - py) * (pa.X - px)) / area;
                    float w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    float z = 1 / (w0 / a.Z + w1 / b.Z + w2 / c.Z);
                    int k = y * W + x;
                    if (z >= depth[k]) continue;
                    depth[k] = z;
                    color[k * 3] = col.r; color[k * 3 + 1] = col.g; color[k * 3 + 2] = col.bl;
                }
            drawn++;
        }
        var img = new Textures.Image(W, H);
        for (int i = 0; i < W * H; i++) img.Pixels[i] = new Textures.Rgba(color[i * 3], color[i * 3 + 1], color[i * 3 + 2], 255);
        Textures.Png.Save(img, outPath);
        Console.WriteLine($"{source}: {tris.Count} triangles within 120 m, {drawn} drawn → {outPath}");
        return 0;
    }
}
