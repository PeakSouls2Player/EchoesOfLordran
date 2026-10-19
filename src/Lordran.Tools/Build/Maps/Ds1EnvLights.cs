using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Session 11 (burg11, BURG-PLAN EC1; MapObjects maps): DS1's GI environment-diffuse cubes as the DS2 map's own CubeEnv
/// cubes, instead of the best-fit DS2 area's (rule A3's envbnd, every collision on one cube).
///  - DS1: each collision names a GI map spot (MSB1 EnvLightMapSpotIndex k → the k-th Environment event "GIEL hNNNN" →
///    GI_EnvDif_…NNNN in GI_EnvM_mAA.tpfbhd,
///    64² BC6H HDR cubes; Ds1EnvCubes); the Burg uses 55 spots;
///  - DS2: model/map/eID.envbnd = CubeEnv_NNNN.tpf (64² DXT5 cube, 7 mips, face-major, alpha 255), MSB2 EnvLight regions
///    (UnkT00 = cube id, UnkT04 = intensity) and collision UnkT2E = cube id; vanilla envbnds hold 1..9 cubes (FoFG 7,
///    Huntsman's Copse 9), so DS1's spots are clustered (k-means on log face means, k ≤ <see cref="MaxCubes"/>) and each
///    cluster's cube is the texel average of its members';
///  - level: DS2 stores the cubes as 8-bit colour; the cluster cubes are gamma-encoded (x^(1/2.2)) and scaled so that the
///    median cluster's mean equals the median mean of the template area's own cubes (relative brightness between DS1 spots
///    kept; absolute level = vanilla's: P until seen in game);
///  - the EnvLight region of a cluster stands at the mean position of its DS1 spots (MSB1 Environment events), intensity
///    as the template's first region.
/// </summary>
public sealed class Ds1EnvLights
{
    public const int MaxCubes = 8;
    const int Size = 64, Mips = 7;

    readonly Dictionary<int, int> spotToCube = [];
    readonly int fallback;
    public List<(int Id, Vector3 Pos, string Note)> Regions { get; } = [];
    public byte[] Envbnd { get; private set; } = [];
    public List<string> Log { get; } = [];

    /// <summary>The DS2 cube id of a DS1 collision (its spot's cluster; unmapped spots → the cluster of the nearest spot).</summary>
    public int CubeFor(MSB1.Part.Collision c) => spotToCube.TryGetValue(c.EnvLightMapSpotIndex, out int id) ? id : NearestCube(c.Position);

    readonly List<(Vector3 Pos, int Cube)> spotPositions = [];
    int NearestCube(Vector3 p) => spotPositions.Count == 0 ? fallback : spotPositions.MinBy(s => Vector3.DistanceSquared(s.Pos, p)).Cube;

    Ds1EnvLights(int fallback) { this.fallback = fallback; }

    /// <summary>Null when DS1 has no GI cubes for the map's spots (the template's envbnd stays).</summary>
    public static Ds1EnvLights? Plan(string dsrRoot, string ds1Map, MSB1 msb1, string templateEnvbnd)
    {
        var cubes = Ds1EnvCubes.Load(dsrRoot, ds1Map[..3]);
        // a collision's EnvLightMapSpotIndex k = the k-th MSB1 Environment event (m10_01: 55 events "GIEL hNNNN", cube
        // GI_EnvDif_…NNNN by the name's number, Ds1Unlit.EnvSpots' rule), its region = the spot's position
        var envs = msb1.Events.Environments;
        var spotIds = msb1.Parts.Collisions.Select(c => c.EnvLightMapSpotIndex).Where(i => i >= 0 && i < envs.Count).Distinct().Order().ToList();
        var pos = new Dictionary<int, Vector3>();
        Ds1EnvCubes.Cube? CubeOf(int k)
        {
            var m = System.Text.RegularExpressions.Regex.Match(envs[k].Name, @"(\d+)$");
            var region = msb1.Regions.GetEntries().FirstOrDefault(x => x.Name == envs[k].RegionName);
            if (region != null) pos[k] = region.Position;
            return m.Success ? cubes.GiDiffuse(ds1Map, int.Parse(m.Groups[1].Value)) : null;
        }
        var spots = spotIds.Select(i => (Index: i, Cube: CubeOf(i))).Where(s => s.Cube != null).Select(s => (s.Index, Cube: s.Cube!)).ToList();
        if (spots.Count == 0) return null;
        var r = new Ds1EnvLights(0);
        // k-means on log face means (deterministic init: luminance quantiles)
        float[] F(Ds1EnvCubes.Cube c) => Enumerable.Range(0, 6).SelectMany(f => { var v = c.FaceMean(f); return new[] { v.X, v.Y, v.Z }; }).Select(x => MathF.Log(1e-4f + MathF.Max(0, x))).ToArray();
        var feats = spots.Select(s => F(s.Cube)).ToList();
        float Lum(int i) => feats[i].Average();
        int k = Math.Min(MaxCubes, spots.Count);
        var order = Enumerable.Range(0, spots.Count).OrderBy(Lum).ToList();
        var centers = Enumerable.Range(0, k).Select(j => (float[])feats[order[(int)((j + 0.5) * spots.Count / k)]].Clone()).ToList();
        var assign = new int[spots.Count];
        for (int it = 0; it < 20; it++)
        {
            for (int i = 0; i < spots.Count; i++)
                assign[i] = Enumerable.Range(0, k).MinBy(j => feats[i].Zip(centers[j], (a, b) => (a - b) * (a - b)).Sum());
            for (int j = 0; j < k; j++)
            {
                var members = Enumerable.Range(0, spots.Count).Where(i => assign[i] == j).ToList();
                if (members.Count == 0) continue;
                centers[j] = Enumerable.Range(0, feats[0].Length).Select(d => (float)members.Average(i => feats[i][d])).ToArray();
            }
        }
        // empty clusters dropped, ids renumbered 0.. in luminance order
        var used = Enumerable.Range(0, k).Where(j => assign.Contains(j)).OrderBy(j => centers[j].Average()).ToList();
        var cubeOf = used.Select((j, id) => (j, id)).ToDictionary(x => x.j, x => x.id);
        // cluster cubes: texel average of the members (linear), sampled along each DS2 texel's direction
        var linear = new List<Vector3[][]>();
        foreach (int j in used)
        {
            var members = Enumerable.Range(0, spots.Count).Where(i => assign[i] == j).Select(i => spots[i].Cube).ToList();
            var faces = new Vector3[6][];
            for (int f = 0; f < 6; f++)
            {
                faces[f] = new Vector3[Size * Size];
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        var d = Direction(f, (x + 0.5f) / Size * 2 - 1, (y + 0.5f) / Size * 2 - 1);
                        faces[f][y * Size + x] = members.Aggregate(Vector3.Zero, (a, c) => a + c.Sample(d)) / members.Count;
                    }
            }
            linear.Add(faces);
        }
        // level: gamma-encoded, median cluster mean = median of the template's cubes' stored means
        var tBnd = BND4.Read(DCX.Decompress(File.ReadAllBytes(templateEnvbnd), out DCX.CompressionInfo dcx));
        var tMeans = tBnd.Files.Select(f => StoredMean(TPF.Read(f.Bytes).Textures[0].Bytes)).Order().ToList();
        double target = tMeans[tMeans.Count / 2];
        var clusterMeans = linear.Select(faces => faces.SelectMany(x => x).Average(v => (v.X + v.Y + v.Z) / 3.0)).ToList();
        double medianLin = clusterMeans.Order().ElementAt(clusterMeans.Count / 2);
        double scale = medianLin > 0 ? Math.Pow(target, 2.2) / medianLin : 1;
        var tTpf = TPF.Read(tBnd.Files[0].Bytes);
        byte[] header = tTpf.Textures[0].Bytes[..128];
        var flags = tBnd.Files[0].Flags;
        tBnd.Files.Clear();
        for (int id = 0; id < linear.Count; id++)
        {
            var bytes = new List<byte>(header);
            for (int f = 0; f < 6; f++)
            {
                var img = new Textures.Image(Size, Size);
                for (int i = 0; i < Size * Size; i++)
                {
                    var v = linear[id][f][i] * (float)scale;
                    byte G(float c) => (byte)Math.Clamp(Math.Round(255 * Math.Pow(Math.Max(0, c), 1 / 2.2)), 0, 255);
                    img.Pixels[i] = new Textures.Rgba(G(v.X), G(v.Y), G(v.Z), 255);
                }
                for (int m = 0, s = Size; m < Mips; m++, s = Math.Max(1, s / 2))
                    bytes.AddRange(Textures.Bcn.Encode(Textures.BcFormat.BC3, s == Size ? img : img.Resize(s, s)));
            }
            var tpf = TPF.Read(tTpf.Write());
            tpf.Textures[0].Name = $"CubeEnv_{id:D4}";
            tpf.Textures[0].Bytes = [.. bytes];
            tBnd.Files.Add(new BinderFile(flags, id, $"CubeEnv_{id:D4}.tpf", tpf.Write()));
            int j = used[id];
            var members = Enumerable.Range(0, spots.Count).Where(i => assign[i] == j).ToList();
            foreach (int i in members) { r.spotToCube[spots[i].Index] = id; if (pos.TryGetValue(spots[i].Index, out var p)) r.spotPositions.Add((p, id)); }
            var mp = members.Where(i => pos.ContainsKey(spots[i].Index)).Select(i => pos[spots[i].Index]).ToList();
            var at = mp.Count > 0 ? mp.Aggregate(Vector3.Zero, (a, b) => a + b) / mp.Count : Vector3.Zero;
            r.Regions.Add((id, at, $"DS1 GI spots {string.Join(",", members.Select(i => spots[i].Index))}"));
            r.Log.Add($"rule EC1: CubeEnv_{id:D4} = mean of DS1 GI spots {string.Join(",", members.Select(i => spots[i].Index))} (linear mean {clusterMeans[id]:0.####} → stored {255 * Math.Pow(Math.Max(0, clusterMeans[id] * scale), 1 / 2.2):0}/255), region at {at:0.0}");
        }
        r.Log.Insert(0, $"rule EC1: {spots.Count} DS1 GI spots of {ds1Map} → {linear.Count} DS2 cubes (k-means, k ≤ {MaxCubes}); level: template {Path.GetFileName(templateEnvbnd)} cube median {255 * target:0}/255, gamma 2.2, scale {scale:0.####}");
        r.Envbnd = DCX.Compress(tBnd.Write(), dcx);
        return r;
    }

    /// <summary>The D3D cube direction of face f at (sc, tc) ∈ [−1, 1]² (inverse of Ds1EnvCubes.Cube.Sample).</summary>
    internal static Vector3 Direction(int f, float sc, float tc) => f switch
    {
        0 => new Vector3(1, -tc, -sc),
        1 => new Vector3(-1, -tc, sc),
        2 => new Vector3(sc, 1, tc),
        3 => new Vector3(sc, -1, -tc),
        4 => new Vector3(sc, -tc, 1),
        _ => new Vector3(-sc, -tc, -1),
    };

    /// <summary>Mean stored colour (0..1) of a DS2 CubeEnv texture's top mips (BC3, 128-byte header, face-major mips).</summary>
    static double StoredMean(byte[] b)
    {
        int w = BitConverter.ToInt32(b, 16), h = BitConverter.ToInt32(b, 12), mips = Math.Max(1, BitConverter.ToInt32(b, 28));
        int faceBytes = 0; for (int m = 0, s = w; m < mips; m++, s = Math.Max(1, s / 2)) faceBytes += Math.Max(1, (s + 3) / 4) * Math.Max(1, (s + 3) / 4) * 16;
        double sum = 0; int n = 0;
        for (int f = 0; f < 6 && 128 + (f + 1) * faceBytes <= b.Length; f++)
        {
            var img = Textures.Bcn.Decode(Textures.BcFormat.BC3, w, h, b.AsSpan(128 + f * faceBytes, w * h));
            foreach (var p in img.Pixels) { sum += (p.R + p.G + p.B) / 3.0 / 255; n++; }
        }
        return n == 0 ? 0 : sum / n;
    }
}
