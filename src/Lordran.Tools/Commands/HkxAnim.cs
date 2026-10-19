using System.Numerics;
using Lordran.Tools.Formats.Havok;

namespace Lordran.Tools.Commands;

/// <summary>hkx-anim FILE.hkx [FRAMES...] [--moving] — a DS1 (2015 tagfile) spline-compressed animation decoded:
/// per track the bone-local translation, rotation (quaternion + yaw/pitch/roll degrees) and scale at the given frames
/// (default first, middle, last); --moving lists only tracks that change.</summary>
public static class HkxAnim
{
    public static int Run(string[] args)
    {
        var a = new HkSplineAnim(new HkTagfile(File.ReadAllBytes(args[1])));
        bool moving = args.Contains("--moving");
        var frames = args.Skip(2).Where(x => !x.StartsWith("--")).Select(float.Parse).ToList();
        if (frames.Count == 0) frames = [0, (a.NumFrames - 1) / 2f, a.NumFrames - 1];
        Console.WriteLine($"{Path.GetFileName(args[1])}: {a.Duration:0.###} s, {a.NumFrames} frames, {a.NumBlocks} blocks of {a.MaxFramesPerBlock}, {a.TrackCount} tracks");
        for (int t = 0; t < a.TrackCount; t++)
        {
            var s = frames.Select(f => a.Sample(t, f)).ToList();
            bool moves = s.Any(x => Vector3.Distance(x.T, s[0].T) > 1e-4f || MathF.Abs(Quaternion.Dot(x.R, s[0].R)) < 0.99999f);
            if (moving && !moves) continue;
            string name = t < a.TrackNames.Length ? a.TrackNames[t] : $"track{t}";
            Console.WriteLine($"  [{t}] {name} (bone {a.TrackToBone[t]}){(moves ? " MOVES" : "")}");
            for (int i = 0; i < frames.Count; i++)
            {
                var (T, R, S) = s[i];
                Console.WriteLine($"     f{frames[i],-6:0.#} t=({T.X:0.###}, {T.Y:0.###}, {T.Z:0.###}) r=({R.X:0.####}, {R.Y:0.####}, {R.Z:0.####}, {R.W:0.####}) ypr=({Ypr(R)}) s=({S.X:0.##}, {S.Y:0.##}, {S.Z:0.##})");
            }
        }
        return 0;
    }

    static string Ypr(Quaternion q)
    {
        float yaw = MathF.Atan2(2 * (q.W * q.Y + q.X * q.Z), 1 - 2 * (q.Y * q.Y + q.X * q.X));
        float pitch = MathF.Asin(Math.Clamp(2 * (q.W * q.X - q.Z * q.Y), -1, 1));
        float roll = MathF.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.X * q.X + q.Z * q.Z));
        const float d = 180 / MathF.PI;
        return $"{yaw * d:0.#}, {pitch * d:0.#}, {roll * d:0.#}";
    }
}
