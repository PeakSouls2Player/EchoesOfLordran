using System.Globalization;
using System.Text;
using Lordran.Tools.Formats.Havok;
using Lordran.Tools.Formats.Morpheme;
using static Lordran.Tools.Formats.Morpheme.MorphemeMath;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// DS1 Havok skeleton + spline animation → DS2 Morpheme NSA channels and rig (session 8; the C# port of the research
/// prototype data\research\morpheme\ds1_bridge.py, with full float precision instead of that script's 3-decimal text
/// input, and with root motion, which the prototype did not bridge). Part of the 1:1 DS1 character pipeline (user
/// directive, session 7: DS1 skeletons and animations, no DS2 stand-ins).
/// <para><b>Rig</b> (DS2 convention, NSA-FORMAT.md §4): <c>CharacterWorldSpaceTM, Character, Trajectory</c> + the DS1
/// bones in DS1 order (DS1 bone i → rig bone i+3 → NSA channel i+2); DS1 root bones are parented to Character;
/// trajectory bone 2, character root 3 (DS1 "Master", which in DS1 sits at pelvis height). Channel c drives rig bone
/// c+1; channels 0/1 (Character, Trajectory) are identity.</para>
/// <para><b>Space</b>: DS1 Havok skeleton space has the FLVER axes (Y up, toes towards -Z). DS2's engine maps Morpheme
/// character space to FLVER model space by the reflection (x, y, z) → (x, z, y) (measured on c1030: residual 0.0 mm,
/// flver_space.py), so every DS1 bone-local transform is conjugated by that reflection: t → (x, z, y),
/// q (w,x,y,z) → (w, -x, -z, -y). Result: feet at z ≈ 0, toes towards -Y, left side +X, like the DS2 rigs.</para>
/// <para><b>Root motion</b>: DS1 extracts it into <c>hkaDefaultAnimatedReferenceFrame.referenceFrameSamples</c>
/// (x, y, z, yaw about +Y; up (0,1,0)); it becomes the NSA trajectory (Morpheme space: Z up, forward -Y) as position
/// (x, z, y) and rotation about +Z by -yaw (the reflection of a +Y rotation), written as (cos(yaw/2), 0, 0, 0 - sin(yaw/2))
/// so a zero yaw gives +0.0 components. Samples are taken at the clip frames (linear interpolation over the reference
/// frame's duration when its sample count differs from the frame count).</para>
/// <para><b>Precision</b>: translations are the decoder's float32 values; rotations are normalised in double (as the
/// Python does after parsing); bones without a track keep their (normalised) reference pose. Encoding is
/// <see cref="NsaEncoder"/> fresh mode.</para>
/// </summary>
public static class Ds1AnimBridge
{
    public sealed class Ds1Skeleton
    {
        public List<string> Names = [];
        public List<int> Parents = [];
        public List<float[]> RefPos = [];   // x, y, z
        public List<float[]> RefRot = [];   // x, y, z, w (Havok order)
    }

    public sealed class Result
    {
        public NsaAnim Anim = null!;
        public MorphemeSkeleton Skeleton = null!;
        public int Frames, Ds1Bones, Tracks, MissingTracks;
        public double Fps;
        public bool HasRootMotion;
        public V3 RootEnd;
        public double RootEndYawDeg;
    }

    // ------------------------------------------------------------------ Havok readers
    static HkTagfile.HkObj? Find(HkTagfile.HkObj o, string type, HashSet<HkTagfile.HkObj> seen)
    {
        if (!seen.Add(o)) return null;
        if (o.Type.Name == type) return o;
        foreach (var v in o.Fields.Values)
            foreach (var c in v is HkTagfile.HkObj h ? [h] : v is System.Collections.IEnumerable e and not string and not byte[] ? e.OfType<HkTagfile.HkObj>() : [])
                if (Find(c, type, seen) is { } r) return r;
        return null;
    }
    public static HkTagfile.HkObj? Find(HkTagfile tag, string type) => Find(tag.Root!, type, new(ReferenceEqualityComparer.Instance));

    static float[] Floats(object? v) => ((System.Collections.IEnumerable)v!).Cast<object>().Select(Convert.ToSingle).ToArray();

    /// <summary>hkaSkeleton of a DSR Skeleton.hkx tagfile: bone names, parents, reference pose.</summary>
    public static Ds1Skeleton ReadSkeleton(HkTagfile tag)
    {
        var s = Find(tag, "hkaSkeleton") ?? throw new InvalidDataException("no hkaSkeleton");
        var sk = new Ds1Skeleton
        {
            Parents = ((System.Collections.IEnumerable)s["parentIndices"]!).Cast<object>().Select(Convert.ToInt32).ToList(),
            Names = ((System.Collections.IEnumerable)s["bones"]!).Cast<HkTagfile.HkObj>().Select(b => (b["name"] as string ?? "").Trim()).ToList(),
        };
        foreach (var t in ((System.Collections.IEnumerable)s["referencePose"]!).Cast<HkTagfile.HkObj>())
        {
            sk.RefPos.Add(Floats(t["translation"])[..3]);
            sk.RefRot.Add(Floats(t["rotation"])[..4]);
        }
        return sk;
    }

    /// <summary>hkaDefaultAnimatedReferenceFrame of the clip: samples (x, y, z, yaw) and duration; null if none.</summary>
    public static (List<float[]> Samples, float Duration, float[] Up)? ReadReferenceFrame(HkTagfile tag)
    {
        var rf = Find(tag, "hkaDefaultAnimatedReferenceFrame");
        if (rf?["referenceFrameSamples"] is not System.Collections.IEnumerable samples) return null;
        var list = samples.Cast<object>().Select(Floats).ToList();
        return (list, Convert.ToSingle(rf["duration"]), Floats(rf["up"]));
    }

    // ------------------------------------------------------------------ conversion
    /// <summary>FLVER/Havok (Y up) → Morpheme character space (Z up): swap Y and Z.</summary>
    public static V3 MirrorT(V3 t) => new(t.X, t.Z, t.Y);
    /// <summary>The same reflection applied to a rotation (S R S): (w, x, y, z) → (w, -x, -z, -y).</summary>
    public static Q4 MirrorQ(Q4 q) => new(q.W, -q.X, -q.Z, -q.Y);

    /// <summary>Sampled DS1 input (full precision): per bone per frame translation (x,y,z) and rotation (x,y,z,w) as
    /// float32, reference frame samples, frame count, duration. Shared by the converter and the JSON export used for the
    /// Python cross-check.</summary>
    public sealed class Ds1Samples
    {
        public Ds1Skeleton Skeleton = null!;
        public int Frames;
        public float Duration;
        public Dictionary<int, List<(float[] T, float[] R)>> Tracks = [];   // DS1 bone → frames
        public List<float[]>? RefFrame;   // (x, y, z, yaw) per sample
        public float RefFrameDuration;
        public float[] RefFrameUp = [0, 1, 0, 0];
    }

    public static Ds1Samples Sample(Ds1Skeleton sk, HkTagfile animTag)
    {
        var a = new HkSplineAnim(animTag);
        var s = new Ds1Samples { Skeleton = sk, Frames = a.NumFrames, Duration = a.Duration };
        for (int t = 0; t < a.TrackCount; t++)
        {
            var frames = new List<(float[], float[])>();
            for (int f = 0; f < a.NumFrames; f++)
            {
                var (T, R, _) = a.Sample(t, f);
                frames.Add(([T.X, T.Y, T.Z], [R.X, R.Y, R.Z, R.W]));
            }
            s.Tracks[a.TrackToBone[t]] = frames;
        }
        if (ReadReferenceFrame(animTag) is { } rf)
        {
            s.RefFrame = rf.Samples; s.RefFrameDuration = rf.Duration; s.RefFrameUp = rf.Up;
            // the yaw → about-Z mapping assumes Havok's up axis +Y (every DS1 clip checked in session 8: c2500, c2232)
            if (rf.Up.Length < 3 || rf.Up[0] != 0 || rf.Up[1] != 1 || rf.Up[2] != 0)
                throw new NotSupportedException($"reference frame up axis ({string.Join(", ", rf.Up)}) is not +Y");
        }
        return s;
    }

    /// <summary>Reference frame sample at clip frame f: exact when the sample count equals the frame count, else linear
    /// interpolation at time f·duration/(frames-1) over the reference frame's evenly spaced samples.</summary>
    static double[] RefSample(Ds1Samples s, int f)
    {
        var rf = s.RefFrame!;
        if (rf.Count == s.Frames) return rf[f].Select(v => (double)v).ToArray();
        if (rf.Count == 1) return rf[0].Select(v => (double)v).ToArray();
        double time = s.Frames > 1 ? f * (double)s.Duration / (s.Frames - 1) : 0;
        double u = s.RefFrameDuration > 0 ? time / s.RefFrameDuration * (rf.Count - 1) : 0;
        int i = Math.Clamp((int)Math.Floor(u), 0, rf.Count - 2);
        double fr = Math.Clamp(u - i, 0, 1);
        return Enumerable.Range(0, 4).Select(k => rf[i][k] + (rf[i + 1][k] - rf[i][k]) * fr).ToArray();
    }

    public static Result Bridge(Ds1Samples s, double? fpsOverride = null, bool rootMotion = true)
    {
        var sk = s.Skeleton;
        int nf = s.Frames, nb = sk.Names.Count;
        double fps = fpsOverride ?? (nf - 1) / (double)s.Duration;
        // ---- rig
        var names = new List<string> { "CharacterWorldSpaceTM", "Character", "Trajectory" };
        names.AddRange(sk.Names);
        var parents = new List<int> { -1, 0, 1 };
        parents.AddRange(sk.Parents.Select(p => p >= 0 ? p + 3 : 1));
        var refPos = sk.RefPos.Select(p => new V3(p[0], p[1], p[2])).ToList();
        var refRot = sk.RefRot.Select(q => Normalize(new Q4(q[3], q[0], q[1], q[2]))).ToList();
        var skel = new MorphemeSkeleton
        {
            Names = names, Parents = parents,
            BindPos = [[0, 0, 0], [0, 0, 0], [0, 0, 0]],
            BindRot = [[0, 0, 0, 1], [0, 0, 0, 1], [0, 0, 0, 1]],
            Traj = 2, Root = 3, AnimToRig = [.. Enumerable.Range(1, names.Count - 1)],
        };
        for (int i = 0; i < nb; i++)
        {
            var t = MirrorT(refPos[i]); var q = MirrorQ(refRot[i]);
            skel.BindPos.Add([t.X, t.Y, t.Z]);
            skel.BindRot.Add([q.X, q.Y, q.Z, q.W]);
        }
        // ---- channels (channel c drives rig bone c + 1)
        int nch = names.Count - 1;
        var pos = new V3[nch][]; var rot = new Q4[nch][];
        int missing = 0;
        for (int c = 0; c < nch; c++)
        {
            int b = c + 1;
            pos[c] = new V3[nf]; rot[c] = new Q4[nf];
            if (b < 3)
            {
                for (int f = 0; f < nf; f++) { pos[c][f] = new V3(0.0, 0.0, 0.0); rot[c][f] = Q4.Identity; }
                continue;
            }
            int i = b - 3;
            s.Tracks.TryGetValue(i, out var src);
            if (src == null || src.Count == 0) missing++;
            for (int f = 0; f < nf; f++)
            {
                V3 t; Q4 q;
                if (src != null && src.Count > 0)
                {
                    var (T, R) = src[f];
                    t = new V3(T[0], T[1], T[2]);
                    q = Normalize(new Q4(R[3], R[0], R[1], R[2]));
                }
                else { t = refPos[i]; q = refRot[i]; }
                var mt = MirrorT(t);
                pos[c][f] = new V3(F32(mt.X), F32(mt.Y), F32(mt.Z));
                rot[c][f] = MirrorQ(q);
            }
        }
        // ---- trajectory (root motion)
        var rootPos = new V3[nf]; var rootRot = new Q4[nf];
        bool hasRoot = rootMotion && s.RefFrame is { Count: > 0 };
        for (int f = 0; f < nf; f++)
        {
            if (!hasRoot) { rootPos[f] = new V3(0.0, 0.0, 0.0); rootRot[f] = Q4.Identity; continue; }
            var r = RefSample(s, f);
            rootPos[f] = new V3(F32(r[0]), F32(r[2]), F32(r[1]));
            double half = r[3] / 2.0;
            rootRot[f] = new Q4(Math.Cos(half), 0.0, 0.0, 0.0 - Math.Sin(half));
        }
        var anim = new NsaAnim
        {
            Fps = fps, ChannelCount = nch, Pos = pos, Rot = rot, RootPos = rootPos, RootRot = rootRot,
            Duration = (nf - 1) / fps,
        };
        var last = hasRoot ? RefSample(s, nf - 1) : [0, 0, 0, 0];
        return new Result
        {
            Anim = anim, Skeleton = skel, Frames = nf, Fps = fps, Ds1Bones = nb, Tracks = s.Tracks.Count, MissingTracks = missing,
            HasRootMotion = hasRoot, RootEnd = rootPos[^1], RootEndYawDeg = last[3] * 180.0 / Math.PI,
        };
    }

    // ------------------------------------------------------------------ JSON export (Python cross-check input)
    static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string Arr(IEnumerable<float> v) => "[" + string.Join(",", v.Select(x => R(x))) + "]";

    /// <summary>The full-precision DS1 input as JSON (validate\py_ds1_bridge_full.py reads it): skeleton names,
    /// parents, reference pose (t xyz, r xyzw), frames, duration, tracks {bone: [[t],[r]] per frame}, reference frame.</summary>
    public static string SamplesJson(Ds1Samples s)
    {
        var sb = new StringBuilder();
        sb.Append("{\"names\":[").Append(string.Join(",", s.Skeleton.Names.Select(n => System.Text.Json.JsonSerializer.Serialize(n)))).Append("],");
        sb.Append("\"parents\":[").Append(string.Join(",", s.Skeleton.Parents)).Append("],");
        sb.Append("\"ref_pos\":[").Append(string.Join(",", s.Skeleton.RefPos.Select(Arr))).Append("],");
        sb.Append("\"ref_rot\":[").Append(string.Join(",", s.Skeleton.RefRot.Select(Arr))).Append("],");
        sb.Append("\"frames\":").Append(s.Frames).Append(",\"duration\":").Append(R(s.Duration)).Append(',');
        sb.Append("\"tracks\":{").Append(string.Join(",", s.Tracks.OrderBy(k => k.Key).Select(kv =>
            $"\"{kv.Key}\":[" + string.Join(",", kv.Value.Select(fr => "[" + Arr(fr.T) + "," + Arr(fr.R) + "]")) + "]"))).Append("},");
        sb.Append("\"ref_frame\":").Append(s.RefFrame == null ? "null" : "[" + string.Join(",", s.RefFrame.Select(Arr)) + "]");
        sb.Append(",\"ref_frame_duration\":").Append(R(s.RefFrameDuration)).Append('}');
        return sb.ToString();
    }
}
