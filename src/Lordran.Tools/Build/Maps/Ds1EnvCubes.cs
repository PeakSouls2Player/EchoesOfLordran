using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// DS1's environment-diffuse cubes as linear HDR (session 9, track skyfix): DSR <c>map\mAA\GI_EnvM_mAA.tpfbhd</c>
/// (GI_EnvDif_* per MSB1 Environment event "GIマップスポットNN") and <c>other\envlight.tpf</c> (EnvDif_def_NNN), both
/// BC6H_UF16, decoded with <see cref="Textures.Bc6h"/> (validated against Pfim's clamped decode). DS1's
/// FRPG_NormalToAlpha shaders add <c>cube(normal) × EnvDifMapMulCol</c> to the hemisphere ambient they multiply the
/// diffuse by (rule S3n, <see cref="Ds1Unlit"/>).
/// </summary>
public sealed class Ds1EnvCubes
{
    /// <summary>One cube: 6 faces of size² linear RGB (D3D face order +X −X +Y −Y +Z −Z).</summary>
    public sealed class Cube(string name, int size, Vector3[][] faces)
    {
        public string Name { get; } = name;
        public Vector3 FaceMean(int f) => faces[f].Aggregate(Vector3.Zero, (a, b) => a + b) / faces[f].Length;

        /// <summary>D3D cube lookup (nearest texel) in direction <paramref name="d"/>.</summary>
        public Vector3 Sample(Vector3 d)
        {
            float ax = MathF.Abs(d.X), ay = MathF.Abs(d.Y), az = MathF.Abs(d.Z);
            int f; float sc, tc, ma;
            if (ax >= ay && ax >= az) { f = d.X >= 0 ? 0 : 1; ma = ax; sc = d.X >= 0 ? -d.Z : d.Z; tc = -d.Y; }
            else if (ay >= az) { f = d.Y >= 0 ? 2 : 3; ma = ay; sc = d.X; tc = d.Y >= 0 ? d.Z : -d.Z; }
            else { f = d.Z >= 0 ? 4 : 5; ma = az; sc = d.Z >= 0 ? d.X : -d.X; tc = -d.Y; }
            if (ma <= 0) return Vector3.Zero;
            int x = Math.Clamp((int)((sc / ma + 1) / 2 * size), 0, size - 1), y = Math.Clamp((int)((tc / ma + 1) / 2 * size), 0, size - 1);
            return faces[f][y * size + x];
        }
    }

    readonly Dictionary<string, Cube> cubes = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, Ds1EnvCubes> cache = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<(string Name, Vector3[] FaceMeans)> All() =>
        cubes.Values.OrderBy(c => c.Name).Select(c => (c.Name, Enumerable.Range(0, 6).Select(c.FaceMean).ToArray()));

    public Cube? Get(string name) => cubes.GetValueOrDefault(name);

    /// <summary>The GI env-diffuse cube of a map's Environment event index (GI_EnvDif_mAA_BB_00NN or the like).</summary>
    public Cube? GiDiffuse(string ds1Map, int spot) =>
        cubes.Values.FirstOrDefault(c => c.Name.Contains("Dif", StringComparison.OrdinalIgnoreCase) && c.Name.Contains(ds1Map[..6]) && c.Name.EndsWith($"{spot:D4}"));

    public static Ds1EnvCubes Load(string dsrRoot, string area)
    {
        if (cache.TryGetValue(area, out var hit)) return hit;
        var r = new Ds1EnvCubes();
        string dir = Path.Combine(dsrRoot, "map", area);
        string bhd = Path.Combine(dir, $"GI_EnvM_{area}.tpfbhd");
        if (File.Exists(bhd))
            foreach (var f in BXF3.Read(bhd, Path.ChangeExtension(bhd, ".tpfbdt")).Files)
                r.Add(TPF.Read(DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes));
        string env = Path.Combine(dsrRoot, "other", "envlight.tpf");
        if (File.Exists(env)) r.Add(TPF.Read(env));
        return cache[area] = r;
    }

    void Add(TPF tpf)
    {
        foreach (var t in tpf.Textures)
        {
            if (!t.Name.Contains("Dif", StringComparison.OrdinalIgnoreCase)) continue; // diffuse cubes only (specular: 256², 8 mips)
            var d = Commands.SkyCheck.DdsInfo(t.Bytes);
            if (d.Dxgi != 95 || d.Faces != 6) continue;
            int fb = Textures.Bc6h.FaceBytes(d.W, d.Mips);
            var faces = Enumerable.Range(0, 6).Select(f => Textures.Bc6h.Decode(t.Bytes.AsSpan(d.Offset + f * fb), d.W, d.H)).ToArray();
            cubes[t.Name] = new Cube(t.Name, d.W, faces);
        }
    }
}
