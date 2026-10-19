using System.Numerics;

namespace Lordran.Tools.Formats.Havok;

/// <summary>
/// Decoder for Havok's hkaSplineCompressedAnimation (DS1/DSR animations, 2015 tagfiles), session 7: the DS1 half of
/// the animation pipeline (DS1 clips → DS2 Morpheme NSA) and the check of what DS1 object clips do.
///
/// Layout (one block per maxFramesPerBlock frames, blocks share their edge frame): at blockOffsets[b], one 4-byte mask per
/// transform track (quantisation byte: bits 0-1 position, 2-5 rotation, 6-7 scale; then position / rotation / scale
/// type bytes: bits 0-2 static X/Y/Z, bits 4-6 spline X/Y/Z; rotation: low nibble static, high nibble spline), padded
/// to 4; then per track: position (spline: u16 numItems, u8 degree, numItems+degree+2 u8 knots, pad 4, per axis min/max
/// floats for spline axes or one float for static axes, (numItems+1) control points of u8/u16 per spline axis, pad 4;
/// static only: one float per static axis), rotation (spline: header + knots, padded to the quantisation's alignment,
/// (numItems+1) packed quaternions; static: one packed quaternion; then pad 4), scale (like position). Knots are frame
/// numbers within the block; values come from de Boor evaluation of the B-spline.
/// </summary>
public sealed class HkSplineAnim
{
    public float Duration;
    public int NumFrames, NumBlocks, MaxFramesPerBlock, TrackCount;
    public float FrameDuration;
    public string[] TrackNames = [];
    /// <summary>Bone index per track (hkaAnimationBinding.transformTrackToBoneIndices).</summary>
    public int[] TrackToBone = [];
    readonly byte[] data;
    readonly int[] blockOffsets;
    readonly Track[][] blocks; // [block][track]

    sealed class Channel
    {
        public int Degree = -1;
        public float[] Knots = [];
        public List<float[]> Points = []; // control points (spline) or one value (static); null channel = default
    }
    sealed class Track { public Channel? Pos, Rot, Scale; public bool[] PosSpline = new bool[3], PosStatic = new bool[3], ScaleSpline = new bool[3], ScaleStatic = new bool[3]; }

    public HkSplineAnim(HkTagfile tag)
    {
        var anim = FindAnimation(tag.Root!) ?? throw new InvalidDataException("no hkaSplineCompressedAnimation");
        Duration = Convert.ToSingle(anim["duration"]);
        NumFrames = Convert.ToInt32(anim["numFrames"]);
        NumBlocks = Convert.ToInt32(anim["numBlocks"]);
        MaxFramesPerBlock = Convert.ToInt32(anim["maxFramesPerBlock"]);
        FrameDuration = Convert.ToSingle(anim["frameDuration"]);
        TrackCount = Convert.ToInt32(anim["numberOfTransformTracks"]);
        FloatTrackCount = Convert.ToInt32(anim["numberOfFloatTracks"] ?? 0);
        data = ((System.Collections.IEnumerable)anim["data"]!).Cast<object>().Select(x => (byte)(Convert.ToInt64(x) & 0xFF)).ToArray();
        blockOffsets = ((System.Collections.IEnumerable)anim["blockOffsets"]!).Cast<object>().Select(x => Convert.ToInt32(x)).ToArray();
        floatBlockOffsets = anim["floatBlockOffsets"] is System.Collections.IEnumerable fbo ? fbo.Cast<object>().Select(x => Convert.ToInt32(x)).ToArray() : [];
        if (anim["annotationTracks"] is System.Collections.IEnumerable at)
            TrackNames = at.Cast<HkTagfile.HkObj>().Select(t => t["trackName"] as string ?? "").ToArray();
        TrackToBone = Enumerable.Range(0, TrackCount).ToArray();
        if (FindBinding(tag.Root!) is { } bind && bind["transformTrackToBoneIndices"] is System.Collections.IEnumerable map)
            TrackToBone = map.Cast<object>().Select(x => Convert.ToInt32(x)).ToArray();
        blocks = new Track[NumBlocks][];
        for (int b = 0; b < NumBlocks; b++) blocks[b] = ReadBlock(blockOffsets[b]);
        floatBlocks = ReadFloatBlocks();
    }

    /// <summary>Session 8 (cutscene track): the same decoder over a DS2 packfile object (demo animations,
    /// <see cref="HkPackfile.AdoptTypes"/>), including float tracks (a demo camera's FOV).</summary>
    public static HkSplineAnim FromPack(HkPObj anim, HkPObj? binding) => new(anim, binding);

    HkSplineAnim(HkPObj anim, HkPObj? binding)
    {
        Duration = Convert.ToSingle(anim["duration"]);
        NumFrames = Convert.ToInt32(anim["numFrames"]);
        NumBlocks = Convert.ToInt32(anim["numBlocks"]);
        MaxFramesPerBlock = Convert.ToInt32(anim["maxFramesPerBlock"]);
        FrameDuration = Convert.ToSingle(anim["frameDuration"]);
        TrackCount = Convert.ToInt32(anim["numberOfTransformTracks"]);
        FloatTrackCount = Convert.ToInt32(anim["numberOfFloatTracks"]);
        data = ((List<object?>)anim["data"]!).Select(x => Convert.ToByte(x)).ToArray();
        blockOffsets = ((List<object?>)anim["blockOffsets"]!).Select(x => Convert.ToInt32(x)).ToArray();
        floatBlockOffsets = ((List<object?>)anim["floatBlockOffsets"]!).Select(x => Convert.ToInt32(x)).ToArray();
        if (anim["annotationTracks"] is List<object?> at)
            TrackNames = at.Select(t => ((HkPObj)t!)["trackName"] as string ?? "").ToArray();
        TrackToBone = binding?["transformTrackToBoneIndices"] is List<object?> map ? map.Select(x => Convert.ToInt32(x)).ToArray() : Enumerable.Range(0, TrackCount).ToArray();
        blocks = new Track[NumBlocks][];
        for (int b = 0; b < NumBlocks; b++) blocks[b] = ReadBlock(blockOffsets[b]);
        floatBlocks = ReadFloatBlocks();
    }

    public int FloatTrackCount;
    readonly int[] floatBlockOffsets = [];
    readonly Channel?[][] floatBlocks = [];

    /// <summary>
    /// Float tracks of each block: one mask byte per float track right after the block's transform masks (the mask
    /// area = align4(4·transform tracks + float tracks) = maskAndQuantizationSize; vanilla DS2 cameras: 0x12 = spline
    /// (0x10) with 16-bit control points), the data at blockOffsets[b] + floatBlockOffsets[b]: per track a spline (u16
    /// numItems, u8 degree, knots, pad 4, min/max floats, u16 control points, pad 4) or a static float (low nibble).
    /// Session 8: checked on the vanilla demo cameras (cam0020 of scn101010: clamped cubic, 33 points, 0.10-0.39).
    /// </summary>
    Channel?[][] ReadFloatBlocks()
    {
        var r = new Channel?[NumBlocks][];
        for (int b = 0; b < NumBlocks; b++)
        {
            r[b] = new Channel?[FloatTrackCount];
            if (FloatTrackCount == 0 || b >= floatBlockOffsets.Length) continue;
            // the masks sit in the block's mask area after the transform masks; the data at floatBlockOffsets[b]
            var masks = new byte[FloatTrackCount];
            for (int i = 0; i < FloatTrackCount; i++) masks[i] = data[blockOffsets[b] + 4 * TrackCount + i];
            int p = blockOffsets[b] + floatBlockOffsets[b];
            for (int i = 0; i < FloatTrackCount; i++)
            {
                byte m = masks[i];
                if ((m & 0xF0) != 0)
                {
                    var c = ReadVec(ref p, [true, false, false], [false, false, false], 1);
                    r[b][i] = c;
                }
                else if ((m & 0x0F) != 0)
                {
                    var v = F(ref p);
                    r[b][i] = new Channel { Points = [[v, 0, 0]] };
                }
            }
        }
        return r;
    }

    /// <summary>A float track's value at a frame (0 when the track has no data).</summary>
    public float SampleFloat(int track, float frame)
    {
        frame = Math.Clamp(frame, 0, NumFrames - 1);
        int b = Math.Min((int)(frame / (MaxFramesPerBlock - 1)), NumBlocks - 1);
        float local = frame - b * (MaxFramesPerBlock - 1);
        return Eval(floatBlocks[b][track], local, 3) is { } v ? v[0] : 0f;
    }

    static HkTagfile.HkObj? FindAnimation(HkTagfile.HkObj o, HashSet<HkTagfile.HkObj>? seen = null) => Find(o, "hkaSplineCompressedAnimation", seen ?? new(ReferenceEqualityComparer.Instance));
    static HkTagfile.HkObj? FindBinding(HkTagfile.HkObj o) => Find(o, "hkaAnimationBinding", new(ReferenceEqualityComparer.Instance));
    static HkTagfile.HkObj? Find(HkTagfile.HkObj o, string type, HashSet<HkTagfile.HkObj> seen)
    {
        if (!seen.Add(o)) return null;
        if (o.Type.Name == type) return o;
        foreach (var v in o.Fields.Values)
            foreach (var c in v is HkTagfile.HkObj h ? [h] : v is System.Collections.IEnumerable e and not string and not byte[] ? e.OfType<HkTagfile.HkObj>() : [])
                if (Find(c, type, seen) is { } r) return r;
        return null;
    }

    Track[] ReadBlock(int start)
    {
        int p = start;
        var masks = new (byte Q, byte P, byte R, byte S)[TrackCount];
        for (int t = 0; t < TrackCount; t++) { masks[t] = (data[p], data[p + 1], data[p + 2], data[p + 3]); p += 4; }
        p += FloatTrackCount; // one mask byte per float track follows the transform masks (DS2 demo cameras, session 8)
        Align(ref p, 4);
        var tracks = new Track[TrackCount];
        for (int t = 0; t < TrackCount; t++)
        {
            var m = masks[t];
            var tr = new Track();
            int posQ = m.Q & 3, rotQ = (m.Q >> 2) & 0xF, scaleQ = (m.Q >> 6) & 3;
            for (int a = 0; a < 3; a++) { tr.PosStatic[a] = (m.P & (1 << a)) != 0; tr.PosSpline[a] = (m.P & (0x10 << a)) != 0; tr.ScaleStatic[a] = (m.S & (1 << a)) != 0; tr.ScaleSpline[a] = (m.S & (0x10 << a)) != 0; }
            tr.Pos = ReadVec(ref p, tr.PosSpline, tr.PosStatic, posQ);
            tr.Rot = ReadRot(ref p, m.R, rotQ);
            tr.Scale = ReadVec(ref p, tr.ScaleSpline, tr.ScaleStatic, scaleQ);
            tracks[t] = tr;
        }
        return tracks;
    }

    static void Align(ref int p, int a) { if (a > 1) p = (p + a - 1) / a * a; }
    float F(ref int p) { float v = BitConverter.ToSingle(data, p); p += 4; return v; }

    Channel? ReadVec(ref int p, bool[] spline, bool[] stat, int quant)
    {
        if (spline.Any(x => x))
        {
            var c = new Channel();
            int n = BitConverter.ToUInt16(data, p); p += 2;
            c.Degree = data[p++];
            int k0 = p; c.Knots = Enumerable.Range(0, n + c.Degree + 2).Select(i => (float)data[k0 + i]).ToArray();
            p += n + c.Degree + 2;
            Align(ref p, 4);
            var min = new float[3]; var max = new float[3]; var fixedV = new float[3];
            for (int a = 0; a < 3; a++)
            {
                if (spline[a]) { min[a] = F(ref p); max[a] = F(ref p); }
                else if (stat[a]) fixedV[a] = F(ref p);
            }
            for (int i = 0; i <= n; i++)
            {
                var v = new float[3];
                for (int a = 0; a < 3; a++)
                {
                    if (!spline[a]) { v[a] = fixedV[a]; continue; }
                    float q = quant == 0 ? data[p++] / 255f : BitConverter.ToUInt16(data, p) / 65535f;
                    if (quant != 0) p += 2;
                    v[a] = min[a] + (max[a] - min[a]) * q;
                }
                c.Points.Add(v);
            }
            Align(ref p, 4);
            return c;
        }
        if (stat.Any(x => x))
        {
            var v = new float[3];
            for (int a = 0; a < 3; a++) if (stat[a]) v[a] = F(ref p);
            return new Channel { Points = [v] };
        }
        return null;
    }

    static int RotAlign(int q) => q switch { 0 => 4, 1 => 1, 2 => 2, 3 => 1, 4 => 2, 5 => 4, _ => 1 };
    static int RotSize(int q) => q switch { 0 => 4, 1 => 5, 2 => 6, 3 => 3, 4 => 2, 5 => 16, _ => throw new NotSupportedException($"rotation quantisation {q}") };

    Channel? ReadRot(ref int p, byte types, int quant)
    {
        bool spline = (types & 0xF0) != 0, stat = (types & 0x0F) != 0;
        if (!spline && !stat) return null;
        var c = new Channel();
        int n = 0;
        if (spline)
        {
            n = BitConverter.ToUInt16(data, p); p += 2;
            c.Degree = data[p++];
            int k0 = p; c.Knots = Enumerable.Range(0, n + c.Degree + 2).Select(i => (float)data[k0 + i]).ToArray();
            p += n + c.Degree + 2;
        }
        Align(ref p, RotAlign(quant));
        for (int i = 0; i <= n; i++) { c.Points.Add(Quat(p, quant)); p += RotSize(quant); }
        Align(ref p, 4);
        return c;
    }

    float[] Quat(int p, int quant)
    {
        switch (quant)
        {
            case 2: // THREECOMP48: 3 x u16, 15 bits each; the dropped component's index from the top bits, its sign from z's top bit
            {
                const int mask = (1 << 15) - 1;
                const float fractal = 0.000043161f;
                int x = BitConverter.ToUInt16(data, p), y = BitConverter.ToUInt16(data, p + 2), z = BitConverter.ToUInt16(data, p + 4);
                int shift = ((y >> 14) & 2) | ((x >> 15) & 1);
                bool neg = (z >> 15) != 0;
                float a = ((x & mask) - (mask >> 1)) * fractal, b = ((y & mask) - (mask >> 1)) * fractal, c = ((z & mask) - (mask >> 1)) * fractal;
                float w = MathF.Sqrt(MathF.Max(0, 1 - a * a - b * b - c * c));
                if (neg) w = -w;
                return Place(shift, a, b, c, w);
            }
            case 1: // THREECOMP40: 40 bits, 12 per component, 2 bits index, 1 bit sign
            {
                ulong v = 0;
                for (int i = 0; i < 5; i++) v |= (ulong)data[p + i] << (8 * i);
                const ulong mask = (1 << 12) - 1;
                const float fractal = 0.000345436f;
                float a = (long)((v & mask)) - (long)(mask >> 1), b = (long)((v >> 12) & mask) - (long)(mask >> 1), c = (long)((v >> 24) & mask) - (long)(mask >> 1);
                a *= fractal; b *= fractal; c *= fractal;
                int shift = (int)((v >> 36) & 3);
                bool neg = ((v >> 38) & 1) != 0;
                float w = MathF.Sqrt(MathF.Max(0, 1 - a * a - b * b - c * c));
                if (neg) w = -w;
                return Place(shift, a, b, c, w);
            }
            case 5: return [BitConverter.ToSingle(data, p), BitConverter.ToSingle(data, p + 4), BitConverter.ToSingle(data, p + 8), BitConverter.ToSingle(data, p + 12)];
            default: throw new NotSupportedException($"rotation quantisation {quant}");
        }
    }

    /// <summary>The three stored components fill the slots other than <paramref name="shift"/>; w goes there.</summary>
    static float[] Place(int shift, float a, float b, float c, float w)
    {
        var q = new float[4];
        int k = 0;
        float[] s = [a, b, c];
        for (int i = 0; i < 4; i++) q[i] = i == shift ? w : s[k++];
        return q;
    }

    /// <summary>Bone-local transform of a track at a frame (fractional frames allowed).</summary>
    public (Vector3 T, Quaternion R, Vector3 S) Sample(int track, float frame)
    {
        frame = Math.Clamp(frame, 0, NumFrames - 1);
        int b = Math.Min((int)(frame / (MaxFramesPerBlock - 1)), NumBlocks - 1);
        float local = frame - b * (MaxFramesPerBlock - 1);
        var tr = blocks[b][track];
        var t = Eval(tr.Pos, local, 3) is { } pv ? new Vector3(pv[0], pv[1], pv[2]) : Vector3.Zero;
        var s = Eval(tr.Scale, local, 3) is { } sv ? new Vector3(sv[0], sv[1], sv[2]) : Vector3.One;
        var r = Eval(tr.Rot, local, 4) is { } rv ? Quaternion.Normalize(new Quaternion(rv[0], rv[1], rv[2], rv[3])) : Quaternion.Identity;
        return (t, r, s);
    }

    static float[]? Eval(Channel? c, float u, int dim)
    {
        if (c == null) return null;
        if (c.Degree < 0 || c.Points.Count == 1) return c.Points[0];
        int n = c.Points.Count - 1, p = c.Degree;
        // span k with knots[k] <= u < knots[k+1], clamped to [p, n]
        int k = p;
        while (k < n && u >= c.Knots[k + 1]) k++;
        var d = new float[p + 1][];
        for (int j = 0; j <= p; j++) d[j] = (float[])c.Points[Math.Clamp(k - p + j, 0, n)].Clone();
        for (int r = 1; r <= p; r++)
            for (int j = p; j >= r; j--)
            {
                int i = k - p + j;
                float denom = c.Knots[i + p - r + 1] - c.Knots[i];
                float alpha = denom == 0 ? 0 : (u - c.Knots[i]) / denom;
                for (int a = 0; a < dim; a++) d[j][a] = (1 - alpha) * d[j - 1][a] + alpha * d[j][a];
            }
        return d[p];
    }
}
