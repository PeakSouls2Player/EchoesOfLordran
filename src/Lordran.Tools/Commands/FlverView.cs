using System.Numerics;
using Lordran.Tools.Textures;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// flver-view FILE.flv|.flver|.bnd OUT.png [front|side] [SIZE] — software render of a model's bind pose (orthographic,
/// flat-shaded by normal, z-buffered; bones as red dots), to check converted characters without the game.
/// A .bnd/.chrbnd(.dcx) renders its first FLVER.
/// </summary>
public static class FlverView
{
    public static int Run(string[] args)
    {
        string path = args[1], outPng = args[2];
        bool side = args.Length > 3 && args[3] == "side";
        int size = args.Length > 4 ? int.Parse(args[4]) : 512;
        FLVER2 f;
        if (FLVER2.Is(path)) f = FLVER2.Read(path);
        else
        {
            byte[] b = File.ReadAllBytes(path);
            if (DCX.Is(b)) b = DCX.Decompress(b);
            IEnumerable<BinderFile> files = BND4.Is(b) ? BND4.Read(b).Files : BND3.Read(b).Files;
            f = FLVER2.Read(files.First(x => x.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase) || x.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
        }
        var img = new Image(size, size);
        var depth = new float[size * size];
        Array.Fill(depth, float.MinValue);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = new Rgba(30, 30, 40, 255);
        // fixed frame: 2.2 m tall, feet at the bottom
        float scale = size / 2.2f;
        (float X, float Y, float Z) Proj(Vector3 p) => side ? (size / 2f + p.Z * scale, size - 4 - p.Y * scale, p.X) : (size / 2f - p.X * scale, size - 4 - p.Y * scale, -p.Z);
        foreach (var m in f.Meshes)
        {
            foreach (var fs in m.FaceSets.Where(x => x.Flags == FLVER2.FaceSet.FSFlags.None).Take(1))
            {
                var idx = fs.Triangulate(m.Vertices.Count < ushort.MaxValue);
                for (int i = 0; i + 2 < idx.Count; i += 3)
                {
                    var a = m.Vertices[idx[i]]; var b = m.Vertices[idx[i + 1]]; var c = m.Vertices[idx[i + 2]];
                    var n = Vector3.Normalize(a.Normal + b.Normal + c.Normal);
                    var light = Vector3.Normalize(side ? new Vector3(0.3f, 0.6f, 1) : new Vector3(-0.4f, 0.6f, -1));
                    float l = Math.Clamp(0.25f + 0.75f * Math.Max(0, Vector3.Dot(n, light)), 0, 1);
                    Tri(img, depth, Proj(a.Position), Proj(b.Position), Proj(c.Position), new Rgba((byte)(200 * l), (byte)(190 * l), (byte)(170 * l), 255));
                }
            }
        }
        var w = ChrCompare.World(f);
        for (int i = 0; i < f.Nodes.Count; i++)
        {
            var p = Proj(w[i].Translation);
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int x = (int)p.X + dx, y = (int)p.Y + dy;
                if (x >= 0 && y >= 0 && x < size && y < size) img[x, y] = new Rgba(255, 40, 40, 255);
            }
        }
        Png.Save(img, outPng);
        Console.WriteLine($"{outPng}: {f.Meshes.Count} meshes, {f.Meshes.Sum(m => m.Vertices.Count)} vertices");
        foreach (var m in f.Meshes)
        {
            var mn = m.Vertices.Aggregate(new Vector3(float.MaxValue), (a, v) => Vector3.Min(a, v.Position));
            var mx = m.Vertices.Aggregate(new Vector3(float.MinValue), (a, v) => Vector3.Max(a, v.Position));
            string colors = string.Join(" ", Enumerable.Range(0, m.Vertices.Count > 0 ? m.Vertices[0].Colors.Count : 0).Select(c =>
                $"c{c} avg ({m.Vertices.Average(v => v.Colors[c].R):0.00},{m.Vertices.Average(v => v.Colors[c].G):0.00},{m.Vertices.Average(v => v.Colors[c].B):0.00},{m.Vertices.Average(v => v.Colors[c].A):0.00})"));
            Console.WriteLine($"  '{f.Materials[m.MaterialIndex].Name}' node {m.NodeIndex} palette [{string.Join(",", m.BoneIndices.Take(6).Select(i => f.Nodes[i].Name))}] bbox {mn} .. {mx} {colors}");
        }
        return 0;
    }

    static void Tri(Image img, float[] depth, (float X, float Y, float Z) a, (float X, float Y, float Z) b, (float X, float Y, float Z) c, Rgba col)
    {
        int minX = (int)Math.Max(0, Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)))), maxX = (int)Math.Min(img.Width - 1, Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
        int minY = (int)Math.Max(0, Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)))), maxY = (int)Math.Min(img.Height - 1, Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
        float d = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (Math.Abs(d) < 1e-6f) return;
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float l1 = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / d, l2 = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / d, l3 = 1 - l1 - l2;
                if (l1 < 0 || l2 < 0 || l3 < 0) continue;
                float z = l1 * a.Z + l2 * b.Z + l3 * c.Z;
                int k = y * img.Width + x;
                if (z <= depth[k]) continue;
                depth[k] = z;
                img[x, y] = col;
            }
    }
}
