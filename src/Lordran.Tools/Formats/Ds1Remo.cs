using System.Numerics;
using Lordran.Tools.Formats.Havok;
using SoulsFormats;

namespace Lordran.Tools.Formats;

/// <summary>
/// A DS1 cutscene ("remo", DSR <c>remo\scnNNNNNN.remobnd.dcx</c>; session 8, cutscene track). BND3 with per cut
/// (<c>\cutNNNN\</c>, ids 100·k + 0/1): <c>camera_win32.sibcam</c> (the camera, <see cref="Sibcam"/>) and
/// <c>hkxx64\aNNNN.hkx</c> (one Havok 2015 tagfile: a "Merged Animation Container" with ONE skeleton whose root bone is
/// named after an MSB map part (the frame of reference, e.g. h0054B1 in 180125) and whose other bones are the cut's
/// parts' bones prefixed with the MSB part name ("c0000_0000" = the player's part root, "c0000_0000_Pelvis" …), and ONE
/// spline-compressed animation over all of them); plus <c>\taeNew\x64\scnNNNNNN.tae</c> (id 10000: per cut one TAE
/// animation with the scene's SFX/sound/fade events). Parts are the MSB's DummyEnemy/DummyObject/regular parts whose
/// names match (DS1 places cutscene-only actors as dummy parts in the map).
/// </summary>
public sealed class Ds1Remo
{
    public BND3 Bnd = null!;
    public int SceneId;
    public List<Cut> Cuts = [];
    public TAE? Tae;

    public sealed class Part
    {
        public string Name = "";
        /// <summary>Track (= bone) indices of this part, part root first.</summary>
        public List<int> Bones = [];
    }

    public sealed class Cut
    {
        public string Name = "";
        public HkSplineAnim Animation = null!;
        public string[] BoneNames = [];
        public int[] Parents = [];
        public (Vector3 T, Quaternion R, Vector3 S)[] ReferencePose = [];
        public string RootName = "";
        public List<Part> Parts = [];
        public Sibcam? Camera;
        public byte[] HkxBytes = [];

        /// <summary>A bone's model-space transform (the scene's space: part roots are placed in it) at a frame.</summary>
        public (Vector3 T, Quaternion R) BoneInRoot(int bone, float frame)
        {
            var (t, r, _) = Animation.Sample(Track(bone), frame);
            int p = Parents[bone];
            while (p >= 0)
            {
                var (pt, pr, _) = Animation.Sample(Track(p), frame);
                t = pt + Vector3.Transform(t, pr);
                r = pr * r;
                p = Parents[p];
            }
            return (t, r);
        }

        public (Vector3 T, Quaternion R) PartWorld(string part, float frame) => BoneInRoot(Parts.First(x => x.Name == part).Bones[0], frame);

        int Track(int bone) => TrackOf(bone);

        /// <summary>The animation track that drives a bone (binding transformTrackToBoneIndices).</summary>
        public int TrackOf(int bone)
        {
            int t = Array.IndexOf(Animation.TrackToBone, bone);
            return t < 0 ? bone : t;
        }
    }

    public static Ds1Remo Read(string path)
    {
        var bnd = BND3.Read(path);
        var r = new Ds1Remo { Bnd = bnd };
        string name = Path.GetFileName(path);
        r.SceneId = int.Parse(name[3..9]);
        foreach (var g in bnd.Files.Where(f => f.Name.Contains("\\cut", StringComparison.OrdinalIgnoreCase))
                     .GroupBy(f => f.Name.TrimStart('\\').Split('\\')[0]).OrderBy(g => g.Key))
        {
            var hkx = g.FirstOrDefault(f => f.Name.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase));
            var cam = g.FirstOrDefault(f => f.Name.EndsWith(".sibcam", StringComparison.OrdinalIgnoreCase));
            if (hkx == null) continue;
            var c = new Cut { Name = g.Key, HkxBytes = hkx.Bytes };
            var tag = new HkTagfile(hkx.Bytes);
            c.Animation = new HkSplineAnim(tag);
            var skel = Find(tag.Root!, "hkaSkeleton") ?? throw new InvalidDataException($"{name} {g.Key}: no skeleton");
            c.BoneNames = ((List<object?>)skel["bones"]!).Select(b => (string)((HkTagfile.HkObj)b!)["name"]!).ToArray();
            c.Parents = ((List<object?>)skel["parentIndices"]!).Select(x => Convert.ToInt32(x)).ToArray();
            c.ReferencePose = ((List<object?>)skel["referencePose"]!).Select(x => Qs(x)).ToArray();
            c.RootName = c.BoneNames[0];
            // parts: the skeleton is a forest; every root bone is a part (named exactly like an MSB part); a part's
            // bones are its root's descendants (named "<part>_<bone>")
            for (int b = 0; b < c.BoneNames.Length; b++)
                if (c.Parents[b] < 0) c.Parts.Add(new Part { Name = c.BoneNames[b], Bones = [b] });
            foreach (var p in c.Parts)
                for (int b = p.Bones[0] + 1; b < c.BoneNames.Length; b++)
                    if (IsUnder(c.Parents, b, p.Bones[0])) p.Bones.Add(b);
            if (cam != null) c.Camera = Sibcam.Read(cam.Bytes);
            r.Cuts.Add(c);
        }
        var tae = bnd.Files.FirstOrDefault(f => f.Name.EndsWith(".tae", StringComparison.OrdinalIgnoreCase));
        if (tae != null) try { r.Tae = TAE.Read(tae.Bytes); } catch (Exception) { r.Tae = null; }
        return r;
    }

    /// <summary>A 2015 tagfile hkQsTransformf (translation, rotation, scale as 4-float tuples or records).</summary>
    static (Vector3 T, Quaternion R, Vector3 S) Qs(object? o)
    {
        static float[] F(object? v) => v switch
        {
            HkTagfile.HkObj h => h.Fields.Values.SelectMany(F).ToArray(),
            System.Collections.IEnumerable e and not string => e.Cast<object?>().SelectMany(F).ToArray(),
            null => [],
            _ => [Convert.ToSingle(v)],
        };
        var f = F(o);
        return (new Vector3(f[0], f[1], f[2]), Quaternion.Normalize(new Quaternion(f[4], f[5], f[6], f[7])), new Vector3(f[8], f[9], f[10]));
    }

    static bool IsUnder(int[] parents, int b, int ancestor)
    {
        for (int p = parents[b]; p >= 0; p = parents[p]) if (p == ancestor) return true;
        return false;
    }

    static HkTagfile.HkObj? Find(HkTagfile.HkObj o, string type, HashSet<HkTagfile.HkObj>? seen = null)
    {
        seen ??= new(ReferenceEqualityComparer.Instance);
        if (!seen.Add(o)) return null;
        if (o.Type.Name == type) return o;
        foreach (var v in o.Fields.Values)
            foreach (var c in v is HkTagfile.HkObj h ? [h] : v is System.Collections.IEnumerable e and not string and not byte[] ? e.OfType<HkTagfile.HkObj>() : [])
                if (Find(c, type, seen) is { } r) return r;
        return null;
    }
}

/// <summary>
/// DS1's cutscene camera (<c>camera_win32.sibcam</c>, little-endian; layout decoded in session 8, REMO-FORMAT.md):
/// header (1, 8, 12, version 0x20051014, start frame, end frame, node offset 0x84, node count 1, value table offset,
/// value table end, value count, ...); node at 0x84 (name offset -> "FreeCam01", 3, -65536, -1, -1, rest position,
/// rest rotation (Euler radians), scale, transform curve offset, 0, 0, FOV curve offset, 0); transform curve (records
/// offset, record count, 3, rotation x2) with 32-byte records [frame, position, tangent in, tangent out, rotation,
/// tangent in, tangent out, 3] whose values are indices into the value table (deduplicated float3s); FOV curve
/// (rest value, keys offset, key count, 0) with 16-byte keys [frame, radians, tangent in, tangent out]. Records are
/// baked per frame (one per frame from the start frame), so sampling is a lookup; tangents are kept for reference.
/// </summary>
public sealed class Sibcam
{
    public sealed record Frame(int Index, Vector3 Position, Vector3 Rotation);
    public sealed record FovKey(int Frame, float Value, float TangentIn, float TangentOut);
    public int StartFrame, EndFrame;
    public string Name = "";
    public Vector3 RestPosition, RestRotation;
    public List<int> Header = [];
    public List<Frame> Frames = [];
    public List<FovKey> Fov = [];
    public float RestFov;
    public byte[] Raw = [];

    public static Sibcam Read(byte[] b)
    {
        int I(int o) => BitConverter.ToInt32(b, o);
        float F(int o) => BitConverter.ToSingle(b, o);
        Vector3 V3(int o) => new(F(o), F(o + 4), F(o + 8));
        if (I(0) != 1 || I(4) != 8 || I(8) != 12) throw new InvalidDataException("not a sibcam");
        var s = new Sibcam { Raw = b, StartFrame = I(0x10), EndFrame = I(0x14) };
        for (int o = 0; o < 0x30; o += 4) s.Header.Add(I(o));
        int node = I(0x18), values = I(0x20);
        int nameOff = I(node);
        int e = nameOff; while (b[e] != 0) e++;
        s.Name = System.Text.Encoding.ASCII.GetString(b, nameOff, e - nameOff);
        s.RestPosition = V3(node + 0x14);
        s.RestRotation = V3(node + 0x20);
        int curve = I(node + 0x38), fov = I(node + 0x44);
        int recs = I(curve), count = I(curve + 4);
        Vector3 Val(int i) => V3(values + 12 * i);
        for (int k = 0; k < count; k++)
        {
            int r = recs + 32 * k;
            s.Frames.Add(new Frame(I(r), Val(I(r + 4)), Val(I(r + 16))));
        }
        s.RestFov = F(fov);
        int keys = I(fov + 4), nk = I(fov + 8);
        for (int k = 0; k < nk; k++) { int r = keys + 16 * k; s.Fov.Add(new FovKey(I(r), F(r + 4), F(r + 8), F(r + 12))); }
        return s;
    }

    /// <summary>The camera at a frame (clamped to the baked records).</summary>
    public (Vector3 Position, Vector3 Rotation) At(float frame)
    {
        if (Frames.Count == 0) return (RestPosition, RestRotation);
        int lo = 0; while (lo + 1 < Frames.Count && Frames[lo + 1].Index <= frame) lo++;
        int hi = Math.Min(lo + 1, Frames.Count - 1);
        var a = Frames[lo]; var c = Frames[hi];
        float t = c.Index == a.Index ? 0 : Math.Clamp((frame - a.Index) / (c.Index - a.Index), 0, 1);
        return (Vector3.Lerp(a.Position, c.Position, t), Vector3.Lerp(a.Rotation, c.Rotation, t));
    }

    /// <summary>FOV (radians) at a frame: Hermite between keys (tangents per frame), constant outside.</summary>
    public float FovAt(float frame)
    {
        if (Fov.Count == 0) return RestFov;
        if (frame <= Fov[0].Frame) return Fov[0].Value;
        for (int k = 0; k + 1 < Fov.Count; k++)
        {
            var a = Fov[k]; var c = Fov[k + 1];
            if (frame > c.Frame) continue;
            float span = c.Frame - a.Frame, t = (frame - a.Frame) / span;
            float h00 = 2 * t * t * t - 3 * t * t + 1, h10 = t * t * t - 2 * t * t + t, h01 = -2 * t * t * t + 3 * t * t, h11 = t * t * t - t * t;
            return h00 * a.Value + h10 * span * a.TangentOut + h01 * c.Value + h11 * span * -c.TangentIn;
        }
        return Fov[^1].Value;
    }
}
