using System.Numerics;

namespace Lordran.Tools.Formats.Havok;

/// <summary>
/// Our own hkaSplineCompressedAnimation encoder (session 8, cutscene track: DS1 cutscene tracks → DS2 demo animations;
/// the layout is the one <see cref="HkSplineAnim"/> decodes, checked on DS2's vanilla demo animations, SCN-FORMAT.md).
/// Input: per frame a bone-local transform per transform track and a value per float track (the camera's FOV).
/// Output: the animation's fields. Choices (all as in vanilla DS2 demos): blocks of up to 256 frames sharing their edge
/// frame (blockDuration = 255 frames), block starts 16-aligned, mask area align4(4·tracks + floatTracks), quantisation
/// byte 0x49 (16-bit positions/scales, THREECOMP48 rotations), float masks 0x12 (spline, 16-bit) / 0x03 (static).
/// A channel that doesn't change within a block is static (and left out when it is the identity), otherwise a
/// degree-1 B-spline with one control point per frame (knots 0,0,1,…,L,L), so every frame decodes to its sample exactly
/// up to quantisation (16-bit over the block's range; rotations ≈ 4e-5).
/// </summary>
public static class HkSplineEncoder
{
    public sealed record Result(byte[] Data, int[] BlockOffsets, int[] FloatBlockOffsets, int MaskAndQuantizationSize,
        int NumFrames, int NumBlocks, int MaxFramesPerBlock, float FrameDuration)
    {
        public float BlockDuration => (MaxFramesPerBlock - 1) * FrameDuration;
        public float Duration => (NumFrames - 1) * FrameDuration;
    }

    public const int MaxFramesPerBlock = 256;
    const float Eps = 1e-5f;

    /// <param name="tracks">[track][frame] bone-local transforms (all tracks the same frame count, at least 2 frames)</param>
    /// <param name="floats">[floatTrack][frame] values</param>
    public static Result Encode(IReadOnlyList<(Vector3 T, Quaternion R, Vector3 S)[]> tracks, IReadOnlyList<float[]> floats, float frameDuration)
    {
        int frames = tracks.Count > 0 ? tracks[0].Length : floats[0].Length;
        if (frames < 2) throw new ArgumentException("an animation needs at least 2 frames");
        int nBlocks = Math.Max(1, (frames - 1 + MaxFramesPerBlock - 2) / (MaxFramesPerBlock - 1));
        int maskSize = Align(4 * tracks.Count + floats.Count, 4);
        var data = new List<byte>();
        var blockOffsets = new int[nBlocks];
        var floatOffsets = new int[nBlocks];
        // rotations sign-continuous per track (q and -q are the same rotation; splines interpolate components)
        var rots = tracks.Select(t => Continuous(t.Select(x => Quaternion.Normalize(x.R)).ToArray())).ToList();
        for (int b = 0; b < nBlocks; b++)
        {
            while (data.Count % 16 != 0) data.Add(0);
            int start = data.Count;
            blockOffsets[b] = start;
            int f0 = b * (MaxFramesPerBlock - 1), f1 = Math.Min(frames - 1, f0 + MaxFramesPerBlock - 1);
            int n = f1 - f0 + 1;
            var masks = new byte[maskSize];
            data.AddRange(masks); // filled below
            for (int t = 0; t < tracks.Count; t++)
            {
                var pos = Enumerable.Range(f0, n).Select(f => tracks[t][f].T).ToArray();
                var rot = rots[t][f0..(f1 + 1)];
                var scl = Enumerable.Range(f0, n).Select(f => tracks[t][f].S).ToArray();
                byte p = Vec(data, pos, Vector3.Zero);
                byte r = Rot(data, rot);
                byte s = Vec(data, scl, Vector3.One);
                masks[4 * t] = 0x49; masks[4 * t + 1] = p; masks[4 * t + 2] = r; masks[4 * t + 3] = s;
            }
            floatOffsets[b] = data.Count - start;
            for (int k = 0; k < floats.Count; k++)
                masks[4 * tracks.Count + k] = Float(data, floats[k][f0..(f1 + 1)]);
            for (int i = 0; i < maskSize; i++) data[start + i] = masks[i];
        }
        while (data.Count % 16 != 0) data.Add(0);
        return new Result(data.ToArray(), blockOffsets, floatOffsets, maskSize, frames, nBlocks, MaxFramesPerBlock, frameDuration);
    }

    static int Align(int v, int a) => (v + a - 1) / a * a;
    static void Pad(List<byte> d, int a) { while (d.Count % a != 0) d.Add(0); }
    static void F(List<byte> d, float v) => d.AddRange(BitConverter.GetBytes(v));
    static void U16(List<byte> d, int v) { d.Add((byte)v); d.Add((byte)(v >> 8)); }

    static void SplineHeader(List<byte> d, int n)
    {
        int last = n - 1; // control points 0..last, degree 1
        U16(d, last);
        d.Add(1);
        d.Add(0);
        for (int i = 0; i <= last; i++) d.Add((byte)i);
        d.Add((byte)last);
    }

    /// <summary>Position or scale: mask bits 0-2 static X/Y/Z, 4-6 spline X/Y/Z.</summary>
    static byte Vec(List<byte> d, Vector3[] v, Vector3 identity)
    {
        float[][] axes = [v.Select(x => x.X).ToArray(), v.Select(x => x.Y).ToArray(), v.Select(x => x.Z).ToArray()];
        float[] id = [identity.X, identity.Y, identity.Z];
        var spline = new bool[3]; var stat = new bool[3];
        for (int a = 0; a < 3; a++)
        {
            float mn = axes[a].Min(), mx = axes[a].Max();
            if (mx - mn > Eps) spline[a] = true;
            else if (MathF.Abs(axes[a][0] - id[a]) > Eps) stat[a] = true;
        }
        byte mask = 0;
        for (int a = 0; a < 3; a++) mask |= (byte)((stat[a] ? 1 << a : 0) | (spline[a] ? 0x10 << a : 0));
        if (spline.Any(x => x))
        {
            SplineHeader(d, v.Length);
            Pad(d, 4);
            var min = new float[3]; var max = new float[3];
            for (int a = 0; a < 3; a++)
            {
                if (spline[a]) { min[a] = axes[a].Min(); max[a] = axes[a].Max(); F(d, min[a]); F(d, max[a]); }
                else if (stat[a]) F(d, axes[a][0]);
            }
            for (int i = 0; i < v.Length; i++)
                for (int a = 0; a < 3; a++)
                    if (spline[a]) U16(d, (int)MathF.Round((axes[a][i] - min[a]) / (max[a] - min[a]) * 65535));
            Pad(d, 4);
        }
        else
            for (int a = 0; a < 3; a++) if (stat[a]) F(d, axes[a][0]);
        return mask;
    }

    /// <summary>Rotation: mask 0xF0 spline / 0x0F static (THREECOMP48, 2-byte aligned, track padded to 4).</summary>
    static byte Rot(List<byte> d, Quaternion[] q)
    {
        bool varies = q.Any(x => MathF.Abs(x.X - q[0].X) > 1e-6f || MathF.Abs(x.Y - q[0].Y) > 1e-6f || MathF.Abs(x.Z - q[0].Z) > 1e-6f || MathF.Abs(x.W - q[0].W) > 1e-6f);
        if (varies)
        {
            SplineHeader(d, q.Length);
            Pad(d, 2);
            foreach (var x in q) d.AddRange(ThreeComp48(x));
            Pad(d, 4);
            return 0xF0;
        }
        if (MathF.Abs(MathF.Abs(q[0].W) - 1) < 1e-6f) return 0; // identity: no channel
        Pad(d, 2);
        d.AddRange(ThreeComp48(q[0]));
        Pad(d, 4);
        return 0x0F;
    }

    /// <summary>Float track: mask 0x12 spline (16-bit) / 0x03 static, as DS2's demo cameras store their FOV.</summary>
    static byte Float(List<byte> d, float[] v)
    {
        float mn = v.Min(), mx = v.Max();
        if (mx - mn <= Eps)
        {
            F(d, v[0]);
            return 0x03;
        }
        SplineHeader(d, v.Length);
        Pad(d, 4);
        F(d, mn); F(d, mx);
        foreach (float x in v) U16(d, (int)MathF.Round((x - mn) / (mx - mn) * 65535));
        Pad(d, 4);
        return 0x12;
    }

    static Quaternion[] Continuous(Quaternion[] q)
    {
        var r = q.ToArray();
        for (int i = 1; i < r.Length; i++)
            if (Quaternion.Dot(r[i], r[i - 1]) < 0) r[i] = Quaternion.Negate(r[i]);
        return r;
    }

    /// <summary>
    /// THREECOMP48, the inverse of <see cref="HkSplineAnim"/>'s decoder: the largest component is dropped (its index in
    /// bit 15 of x (bit 0) and of y (bit 1), its sign in bit 15 of z), the other three in x/y/z order as 15-bit values
    /// v / 0.000043161 + 16383.
    /// </summary>
    public static byte[] ThreeComp48(Quaternion q)
    {
        q = Quaternion.Normalize(q);
        float[] c = [q.X, q.Y, q.Z, q.W];
        int big = 0;
        for (int i = 1; i < 4; i++) if (MathF.Abs(c[i]) > MathF.Abs(c[big])) big = i;
        bool neg = c[big] < 0;
        const int mask = (1 << 15) - 1;
        const float fractal = 0.000043161f;
        var o = new int[3];
        for (int i = 0, k = 0; i < 4; i++)
        {
            if (i == big) continue;
            o[k++] = Math.Clamp((int)MathF.Round(c[i] / fractal) + (mask >> 1), 0, mask);
        }
        int x = o[0] | ((big & 1) << 15), y = o[1] | (((big >> 1) & 1) << 15), z = o[2] | ((neg ? 1 : 0) << 15);
        return [(byte)x, (byte)(x >> 8), (byte)y, (byte)(y >> 8), (byte)z, (byte)(z >> 8)];
    }
}
