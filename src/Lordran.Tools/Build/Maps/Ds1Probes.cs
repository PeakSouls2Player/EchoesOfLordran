using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Session 11 (burg11, BURG-PLAN LP1; MapObjects maps): the light-probe records (LightProbes, pointcloud.btpb: per probe a
/// DC triplet and three directional triplets, s16) from DS1's GI env-diffuse cubes instead of one neutral record:
///  - per DS1 GI spot (MSB1 Environment event → GI_EnvDif cube, Ds1Unlit.EnvSpots): the cube's mean radiance M and its
///    dipole D (L(ω) ≈ M + D·ω: D = 3·mean(L·ω), solid-angle weighted over the faces);
///  - record = s·(M, Dx, Dy, Dz) per RGB channel; the slot order follows the neutral record, whose third triplet is the
///    strong one (TB's sky-lit probe: light from above = +Y) — so X, Y, Z (P: the order of X and Z and the signs are
///    inferred from that record only, checked in game);
///  - s: the median spot's M luminance → the neutral record's DC luminance (LightProbes.Neutral, the level characters were
///    tuned to in session 7), so relative brightness between DS1 spots is kept;
///  - a probe takes the nearest spot's record.
/// </summary>
public sealed class Ds1Probes
{
    readonly List<(Vector3 Pos, short[] Rec)> spots = [];
    public string Note { get; private set; } = "";

    public short[] At(Vector3 p) => spots.MinBy(s => Vector3.DistanceSquared(s.Pos, p)).Rec;

    public static Ds1Probes? For(string dsrRoot, string ds1Map, MSB1 msb1)
    {
        var env = Ds1Unlit.EnvSpots.For(dsrRoot, ds1Map, msb1);
        if (env.Spots.Count == 0) return null;
        var fits = new List<(Vector3 Pos, Vector3 M, Vector3 Dx, Vector3 Dy, Vector3 Dz)>();
        const int n = 16;
        foreach (var (_, pos, cube) in env.Spots)
        {
            Vector3 m = Vector3.Zero, dx = Vector3.Zero, dy = Vector3.Zero, dz = Vector3.Zero; float wsum = 0;
            for (int f = 0; f < 6; f++)
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float sc = (x + 0.5f) / n * 2 - 1, tc = (y + 0.5f) / n * 2 - 1;
                        var d = Vector3.Normalize(Ds1EnvLights.Direction(f, sc, tc));
                        float w = 1 / MathF.Pow(1 + sc * sc + tc * tc, 1.5f);
                        var l = cube.Sample(d);
                        m += w * l; dx += w * d.X * l; dy += w * d.Y * l; dz += w * d.Z * l; wsum += w;
                    }
            fits.Add((pos, m / wsum, 3 * dx / wsum, 3 * dy / wsum, 3 * dz / wsum));
        }
        static float Lum(Vector3 v) => (v.X + v.Y + v.Z) / 3;
        var neutral = LightProbes.Neutral;
        float targetDc = (neutral[0] + neutral[1] + neutral[2]) / 3f;
        float median = fits.Select(f => Lum(f.M)).Order().ElementAt(fits.Count / 2);
        float s = median > 0 ? targetDc / median : 1;
        var r = new Ds1Probes();
        static short Q(float v) => (short)Math.Clamp((int)MathF.Round(v), short.MinValue, short.MaxValue);
        foreach (var f in fits)
            r.spots.Add((f.Pos, [Q(s * f.M.X), Q(s * f.M.Y), Q(s * f.M.Z), Q(s * f.Dx.X), Q(s * f.Dx.Y), Q(s * f.Dx.Z),
                Q(s * f.Dy.X), Q(s * f.Dy.Y), Q(s * f.Dy.Z), Q(s * f.Dz.X), Q(s * f.Dz.Y), Q(s * f.Dz.Z)]));
        var dcs = r.spots.Select(x => (x.Rec[0] + x.Rec[1] + x.Rec[2]) / 3).Order().ToList();
        r.Note = $"rule LP1: light probes from {fits.Count} DS1 GI spots (nearest spot's cube: mean + dipole), scale {s:0.##} (median DC → neutral {targetDc:0}); DC range {dcs[0]}..{dcs[^1]}, median {dcs[dcs.Count / 2]}";
        return r;
    }
}
