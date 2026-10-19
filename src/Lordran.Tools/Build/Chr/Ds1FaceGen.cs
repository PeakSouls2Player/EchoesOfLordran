using System.Numerics;
using System.Text;
using Lordran.Tools.Textures;
using SoulsFormats;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// Session 11 (track npc12): DS1's FaceGen faces, evaluated offline (DS1 builds them at run time; DS2 can't).
/// <para>DS1 (`facegen\FaceGen.fgbnd`, research data\sandbox\npc\NOTES.md §4): face.tri (FRTRI003, the FaceGen base mesh),
/// face.egm (FREGM002: 50 symmetric + 30 asymmetric geometry modes, per mode a float scale + int16 xyz deltas per tri
/// vertex), face.egt (FREGT003: 50 symmetric texture modes, per mode a float scale + 128×128 signed bytes per channel,
/// planar), face.bmp / face_m.bmp (the mean colour, 128×128), eye.tri / eye.egm (the eyes follow the face shape), the
/// game mesh face.flver (= parts\FG_A_0000: face 642 + inner 332 + eye 78 + 4 vertices, bound to Head / eye bones) and
/// face.flver2tri (F2TR: per flver mesh the tri vertex of each flver vertex). An NPC's FaceGenParam row holds 50
/// geometry and 50 texture bytes: coefficient = byte · 20 / 255 − 10 (DSR FUN_140771c50 / FUN_140771a50, bounds ±10),
/// the symmetric modes only (the asymmetric ones are not driven by the character data).</para>
/// <para>Rule N-FACE1: face vertex = flver vertex + T(Σₖ cₖ · scaleₖ · egmₖ[tri vertex]) where T is the tri → flver
/// linear map fitted on the base positions (tri z is mirrored); normals re-derived from the morphed triangles; face colour
/// (128×128, FaceGen's own texture space) = mean + Σₖ cₖ · scaleₖ · egtₖ.</para>
/// </summary>
public sealed class Ds1FaceGen
{
    public FLVER2 Flver { get; private init; } = null!;
    byte[] FlverBytes { get; init; } = null!;
    public Dictionary<string, TPF.Texture> Textures { get; private init; } = null!;
    /// <summary>Per flver mesh: the tri file it maps to ("face.tri"/"eye.tri") and the tri vertex of each flver vertex.</summary>
    public List<(string Tri, ushort[] Map)> Map { get; private init; } = null!;
    public Dictionary<string, Vector3[]> TriVerts { get; private init; } = null!;
    /// <summary>EGM per tri file: symmetric modes (scale, deltas).</summary>
    public Dictionary<string, List<(float Scale, short[] D)>> Egm { get; private init; } = null!;
    public List<(float Scale, sbyte[] R, sbyte[] G, sbyte[] B)> Egt { get; private init; } = null!;
    public int EgtSize { get; private init; }
    public Image MeanMale { get; private init; } = null!;
    public Image MeanFemale { get; private init; } = null!;
    /// <summary>tri → flver linear map (3×3, row vectors) and translation, per tri file, with the fit's rms (m).</summary>
    public Dictionary<string, (Matrix4x4 M, double Rms)> Fit { get; private init; } = null!;

    static readonly Dictionary<string, Ds1FaceGen> cache = new(StringComparer.OrdinalIgnoreCase);

    public static Ds1FaceGen Load(string dsrRoot)
    {
        if (cache.TryGetValue(dsrRoot, out var c)) return c;
        var fg = BND3.Read(Path.Combine(dsrRoot, "facegen", "FaceGen.fgbnd"));
        byte[] F(string name) => fg.Files.First(f => f.Name.EndsWith("\\" + name, StringComparison.OrdinalIgnoreCase)).Bytes;
        var flv = ReadFlver(F("face.flver"));
        var tex = TPF.Read(F("facegen.tpf")).Textures.ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);
        var map = ReadF2T(F("face.flver2tri"));
        var tris = new Dictionary<string, Vector3[]>(StringComparer.OrdinalIgnoreCase) { ["face.tri"] = ReadTri(F("face.tri")), ["eye.tri"] = ReadTri(F("eye.tri")) };
        var egm = new Dictionary<string, List<(float, short[])>>(StringComparer.OrdinalIgnoreCase) { ["face.tri"] = ReadEgm(F("face.egm")), ["eye.tri"] = ReadEgm(F("eye.egm")) };
        var (egt, size) = ReadEgt(F("face.egt"));
        var fit = new Dictionary<string, (Matrix4x4, double)>(StringComparer.OrdinalIgnoreCase);
        foreach (string t in tris.Keys)
        {
            var pairs = new List<(Vector3 Tri, Vector3 Flv)>();
            for (int m = 0; m < map.Count && m < flv.Meshes.Count; m++)
                if (map[m].Tri.Equals(t, StringComparison.OrdinalIgnoreCase))
                    for (int i = 0; i < map[m].Map.Length && i < flv.Meshes[m].Vertices.Count; i++)
                        if (map[m].Map[i] < tris[t].Length) pairs.Add((tris[t][map[m].Map[i]], flv.Meshes[m].Vertices[i].Position));
            fit[t] = FitAffine(pairs);
        }
        var r = new Ds1FaceGen
        {
            Flver = flv, FlverBytes = F("face.flver"), Textures = tex, Map = map, TriVerts = tris, Egm = egm, Egt = egt, EgtSize = size,
            MeanMale = ReadBmp(F("face_m.bmp")), MeanFemale = ReadBmp(F("face.bmp")), Fit = fit,
        };
        cache[dsrRoot] = r;
        return r;
    }

    public string Describe() =>
        $"FaceGen: flver meshes {string.Join(",", Flver.Meshes.Select(m => m.Vertices.Count))}; map {string.Join(", ", Map.Select(m => $"{m.Tri}×{m.Map.Length}"))}; " +
        $"tri {string.Join(", ", TriVerts.Select(t => $"{t.Key} {t.Value.Length}"))}; egm {string.Join(", ", Egm.Select(e => $"{e.Key} {e.Value.Count} modes"))}; egt {Egt.Count}×{EgtSize}²; " +
        $"fit rms {string.Join(", ", Fit.Select(f => $"{f.Key} {f.Value.Rms * 1000:0.000} mm"))}";

    /// <summary>DS1's FaceGen game FLVER sets 0x60000000 in its vertex buffers' index (SoulsFormats reads that as Edge
    /// compression and skips the vertices); the flag is cleared in a copy before reading.</summary>
    public static FLVER2 ReadFlver(byte[] bytes)
    {
        var b = (byte[])bytes.Clone();
        int I(int at) => BitConverter.ToInt32(b, at);
        int dummies = I(0x14), materials = I(0x18), bones = I(0x1C), meshes = I(0x20), buffers = I(0x24), faceSets = I(0x50);
        int at = 0x80 + dummies * 0x40 + materials * 0x20 + bones * 0x80 + meshes * 0x30 + faceSets * 0x20;
        for (int i = 0; i < buffers; i++)
        {
            int p = at + i * 0x20;
            BitConverter.GetBytes(I(p) & ~0x60000000).CopyTo(b, p);
        }
        return FLVER2.Read(b);
    }

    /// <summary>face.flver2tri: "F2TR", version, header size 16, (entry count, entry size); entries (name offset, data offset,
    /// count); the data = u16 tri vertex per flver vertex; names UTF-16.</summary>
    static List<(string Tri, ushort[] Map)> ReadF2T(byte[] b)
    {
        if (Encoding.ASCII.GetString(b, 0, 4) != "F2TR") throw new InvalidDataException("flver2tri magic");
        int count = BitConverter.ToUInt16(b, 12), size = BitConverter.ToUInt16(b, 14);
        var list = new List<(string, ushort[])>();
        for (int e = 0; e < count; e++)
        {
            int p = 16 + e * size;
            int nameOff = BitConverter.ToInt32(b, p), dataOff = BitConverter.ToInt32(b, p + 4), n = BitConverter.ToInt32(b, p + 8);
            int end = nameOff;
            while (end + 1 < b.Length && (b[end] != 0 || b[end + 1] != 0)) end += 2;
            string name = Encoding.Unicode.GetString(b, nameOff, end - nameOff);
            var m = new ushort[n];
            for (int i = 0; i < n; i++) m[i] = BitConverter.ToUInt16(b, dataOff + i * 2);
            string file = Path.GetFileName(name.Replace('\\', '/'));
            int dot = file.IndexOf(".tri", StringComparison.OrdinalIgnoreCase);
            list.Add((dot >= 0 ? file[..(dot + 4)] : file, m));
        }
        return list;
    }

    static Vector3[] ReadTri(byte[] b)
    {
        if (Encoding.ASCII.GetString(b, 0, 8) != "FRTRI003") throw new InvalidDataException("tri magic");
        int v = BitConverter.ToInt32(b, 8);
        var r = new Vector3[v];
        for (int i = 0; i < v; i++) r[i] = new Vector3(BitConverter.ToSingle(b, 64 + i * 12), BitConverter.ToSingle(b, 68 + i * 12), BitConverter.ToSingle(b, 72 + i * 12));
        return r;
    }

    static List<(float, short[])> ReadEgm(byte[] b)
    {
        if (Encoding.ASCII.GetString(b, 0, 8) != "FREGM002") throw new InvalidDataException("egm magic");
        int v = BitConverter.ToInt32(b, 8), s = BitConverter.ToInt32(b, 12);
        var r = new List<(float, short[])>();
        int p = 64;
        for (int k = 0; k < s; k++)
        {
            float scale = BitConverter.ToSingle(b, p); p += 4;
            var d = new short[v * 3];
            for (int i = 0; i < d.Length; i++) { d[i] = BitConverter.ToInt16(b, p); p += 2; }
            r.Add((scale, d));
        }
        return r;
    }

    static (List<(float, sbyte[], sbyte[], sbyte[])>, int) ReadEgt(byte[] b)
    {
        if (Encoding.ASCII.GetString(b, 0, 8) != "FREGT003") throw new InvalidDataException("egt magic");
        int rows = BitConverter.ToInt32(b, 8), cols = BitConverter.ToInt32(b, 12), s = BitConverter.ToInt32(b, 16);
        int n = rows * cols;
        var r = new List<(float, sbyte[], sbyte[], sbyte[])>();
        int p = 64;
        for (int k = 0; k < s; k++)
        {
            float scale = BitConverter.ToSingle(b, p); p += 4;
            sbyte[] Plane() { var a = new sbyte[n]; for (int i = 0; i < n; i++) a[i] = (sbyte)b[p + i]; p += n; return a; }
            var rr = Plane(); var gg = Plane(); var bb = Plane();
            r.Add((scale, rr, gg, bb));
        }
        return (r, rows);
    }

    /// <summary>24-bit BMP (bottom-up) → image (top row first).</summary>
    static Image ReadBmp(byte[] b)
    {
        int off = BitConverter.ToInt32(b, 10), w = BitConverter.ToInt32(b, 18), h = BitConverter.ToInt32(b, 22);
        int bpp = BitConverter.ToUInt16(b, 28) / 8;
        int stride = (w * bpp + 3) & ~3;
        var img = new Image(w, Math.Abs(h));
        for (int y = 0; y < Math.Abs(h); y++)
        {
            int row = h > 0 ? Math.Abs(h) - 1 - y : y;
            for (int x = 0; x < w; x++)
            {
                int p = off + row * stride + x * bpp;
                img[x, y] = new Rgba(b[p + 2], b[p + 1], b[p], 255);
            }
        }
        return img;
    }

    /// <summary>Least-squares affine map tri → flver (row vectors: p' = p · M with M's 4th row the translation).</summary>
    static (Matrix4x4, double) FitAffine(List<(Vector3 Tri, Vector3 Flv)> pairs)
    {
        if (pairs.Count < 4) return (Matrix4x4.Identity, double.NaN);
        // normal equations: A (n×4: x y z 1), solve (AᵀA) X = Aᵀ B for each output column
        var ata = new double[4, 4];
        var atb = new double[4, 3];
        foreach (var (t, f) in pairs)
        {
            double[] a = [t.X, t.Y, t.Z, 1];
            double[] o = [f.X, f.Y, f.Z];
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++) ata[i, j] += a[i] * a[j];
                for (int j = 0; j < 3; j++) atb[i, j] += a[i] * o[j];
            }
        }
        var x = Solve(ata, atb);
        var m = new Matrix4x4(
            (float)x[0, 0], (float)x[0, 1], (float)x[0, 2], 0,
            (float)x[1, 0], (float)x[1, 1], (float)x[1, 2], 0,
            (float)x[2, 0], (float)x[2, 1], (float)x[2, 2], 0,
            (float)x[3, 0], (float)x[3, 1], (float)x[3, 2], 1);
        double se = pairs.Sum(p => (double)Vector3.DistanceSquared(Vector3.Transform(p.Tri, m), p.Flv));
        return (m, Math.Sqrt(se / pairs.Count));
    }

    static double[,] Solve(double[,] a, double[,] b)
    {
        int n = a.GetLength(0), k = b.GetLength(1);
        var m = (double[,])a.Clone(); var r = (double[,])b.Clone();
        for (int c = 0; c < n; c++)
        {
            int piv = c;
            for (int i = c + 1; i < n; i++) if (Math.Abs(m[i, c]) > Math.Abs(m[piv, c])) piv = i;
            for (int j = 0; j < n; j++) (m[c, j], m[piv, j]) = (m[piv, j], m[c, j]);
            for (int j = 0; j < k; j++) (r[c, j], r[piv, j]) = (r[piv, j], r[c, j]);
            for (int i = 0; i < n; i++)
            {
                if (i == c || m[c, c] == 0) continue;
                double f = m[i, c] / m[c, c];
                for (int j = 0; j < n; j++) m[i, j] -= f * m[c, j];
                for (int j = 0; j < k; j++) r[i, j] -= f * r[c, j];
            }
        }
        for (int i = 0; i < n; i++) for (int j = 0; j < k; j++) r[i, j] /= m[i, i];
        return r;
    }

    /// <summary>DS1's FaceGenParam byte → FaceGen coefficient (DSR FUN_140771c50).</summary>
    public static float Coefficient(int b) => b * 20f / 255f - 10f;

    /// <summary>The face FLVER morphed by the 50 geometry bytes (a copy; positions and normals of every mapped mesh).</summary>
    public FLVER2 Morph(IReadOnlyList<int> geo, out double maxShiftMm)
    {
        var f = ReadFlverCopy();
        double max = 0;
        for (int m = 0; m < f.Meshes.Count && m < Map.Count; m++)
        {
            var (tri, map) = Map[m];
            if (!Egm.TryGetValue(tri, out var modes) || !Fit.TryGetValue(tri, out var fit)) continue;
            var mesh = f.Meshes[m];
            var basePos = mesh.Vertices.Select(v => v.Position).ToArray();
            for (int i = 0; i < mesh.Vertices.Count && i < map.Length; i++)
            {
                int t = map[i];
                var d = Vector3.Zero;
                for (int k = 0; k < Math.Min(geo.Count, modes.Count); k++)
                {
                    float c = Coefficient(geo[k]) * modes[k].Scale;
                    if (c == 0) continue;
                    d += c * new Vector3(modes[k].D[t * 3], modes[k].D[t * 3 + 1], modes[k].D[t * 3 + 2]);
                }
                var dd = Vector3.TransformNormal(d, fit.M);
                mesh.Vertices[i].Position = basePos[i] + dd;
                max = Math.Max(max, dd.Length() * 1000);
            }
            // normals: the base normal turned by the change of the area-weighted triangle normal at the vertex
            var tris = Tris(mesh);
            var n0 = VertexNormals(basePos, tris);
            var n1 = VertexNormals(mesh.Vertices.Select(v => v.Position).ToArray(), tris);
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                if (n0[i].LengthSquared() < 1e-12f || n1[i].LengthSquared() < 1e-12f) continue;
                var q = Rotation(Vector3.Normalize(n0[i]), Vector3.Normalize(n1[i]));
                var v = mesh.Vertices[i];
                v.Normal = Vector3.Normalize(Vector3.Transform(v.Normal, q));
                if (v.Tangents.Count > 0) { var t4 = v.Tangents[0]; var t3 = Vector3.Transform(new Vector3(t4.X, t4.Y, t4.Z), q); v.Tangents[0] = new Vector4(t3, t4.W); }
            }
        }
        maxShiftMm = max;
        return f;
    }

    FLVER2 ReadFlverCopy() => ReadFlver(FlverBytes);

    static List<int> Tris(FLVER2.Mesh m)
    {
        var fs = m.FaceSets.FirstOrDefault(f => f.Flags == FLVER2.FaceSet.FSFlags.None) ?? m.FaceSets[0];
        return fs.TriangleStrip ? fs.Triangulate(true) : new List<int>(fs.Indices);
    }

    static Vector3[] VertexNormals(Vector3[] p, List<int> t)
    {
        var n = new Vector3[p.Length];
        for (int i = 0; i + 2 < t.Count; i += 3)
        {
            int a = t[i], b = t[i + 1], c = t[i + 2];
            if (a >= p.Length || b >= p.Length || c >= p.Length || a == b || b == c || a == c) continue;
            var fn = Vector3.Cross(p[b] - p[a], p[c] - p[a]);
            n[a] += fn; n[b] += fn; n[c] += fn;
        }
        return n;
    }

    static Quaternion Rotation(Vector3 from, Vector3 to)
    {
        float d = Vector3.Dot(from, to);
        if (d > 0.999999f) return Quaternion.Identity;
        var axis = Vector3.Cross(from, to);
        if (axis.LengthSquared() < 1e-12f) return Quaternion.Identity;
        return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(Math.Clamp(d, -1, 1)));
    }

    /// <summary>The FaceGen face colour (EGT result, 128×128 in FaceGen's texture space, top row first: EGT planes are stored
    /// bottom-up like the BMP).</summary>
    public Image FaceColour(IReadOnlyList<int> tex, bool female, float gain = 1f)
    {
        var mean = female ? MeanFemale : MeanMale;
        int n = EgtSize;
        var acc = new float[n * n * 3];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var c = mean[x * mean.Width / n, y * mean.Height / n];
                int k = (y * n + x) * 3;
                acc[k] = c.R; acc[k + 1] = c.G; acc[k + 2] = c.B;
            }
        for (int m = 0; m < Math.Min(tex.Count, Egt.Count); m++)
        {
            float c = gain * Coefficient(tex[m]) * Egt[m].Scale;
            if (c == 0) continue;
            var (_, r, g, b) = Egt[m];
            for (int y = 0; y < n; y++)
            {
                int src = (n - 1 - y) * n; // bottom-up planes
                for (int x = 0; x < n; x++)
                {
                    int k = (y * n + x) * 3;
                    acc[k] += c * r[src + x]; acc[k + 1] += c * g[src + x]; acc[k + 2] += c * b[src + x];
                }
            }
        }
        var img = new Image(n, n);
        for (int i = 0; i < n * n; i++)
            img.Pixels[i] = new Rgba((byte)Math.Clamp(MathF.Round(acc[i * 3]), 0, 255), (byte)Math.Clamp(MathF.Round(acc[i * 3 + 1]), 0, 255), (byte)Math.Clamp(MathF.Round(acc[i * 3 + 2]), 0, 255), 255);
        return img;
    }
}
