using System.Numerics;
using Lordran.Tools.Formats.Havok;
using SoulsFormats;

namespace Lordran.Tools.Formats;

/// <summary>
/// A DS2 realtime demo (session 8, cutscene track; SCN-FORMAT.md): <c>model/scn/scnNNNNNN.scnbnd</c> (BND4) = the MQB
/// sequence + per cut <c>cutNNNN_a0000.hkx</c> (actors: characters "cMMMMVV_IIII", objects "oAA_BBBB_IIII", the camera
/// "camNNNN") and <c>cutNNNN_d0000.hkx</c> (lights "lightNNNN_…", sound/SFX emitters "seNNNN_…"/"sfxNNNN_…"), Havok 2014
/// packfiles without __types__: one skeleton + one spline-compressed animation + one binding per actor, the skeleton and
/// the binding named after the actor; the camera is one bone (forward/up axes in SCN-FORMAT.md) with one float track
/// (vertical FOV in radians); + <c>scnNNNNNN.acb</c>.
/// </summary>
public sealed class Ds2Scn
{
    public sealed class Actor
    {
        public string Name = "";
        public string[] Bones = [];
        public int[] Parents = [];
        public HkSplineAnim Anim = null!;

        /// <summary>Model-space (cut-offset-relative) transform of a bone at a frame.</summary>
        public (Vector3 T, Quaternion R) Bone(int bone, float frame)
        {
            var (t, r, _) = Anim.Sample(Track(bone), frame);
            for (int p = Parents[bone]; p >= 0; p = Parents[p])
            {
                var (pt, pr, _) = Anim.Sample(Track(p), frame);
                t = pt + Vector3.Transform(t, pr);
                r = pr * r;
            }
            return (t, r);
        }

        int Track(int bone) { int t = Array.IndexOf(Anim.TrackToBone, bone); return t < 0 ? bone : t; }
    }

    /// <summary>An actor to write: skeleton (bone names with the actor prefix, parents, reference pose) and per-frame
    /// bone-local tracks; float slots (the camera's "camNNNN_FOV") with per-frame values.</summary>
    public sealed class NewActor
    {
        public string Name = "";
        public string[] Bones = [];
        public int[] Parents = [];
        public (Vector3 T, Quaternion R, Vector3 S)[] ReferencePose = [];
        /// <summary>[bone][frame]</summary>
        public (Vector3 T, Quaternion R, Vector3 S)[][] Tracks = [];
        public string[] FloatSlots = [];
        /// <summary>[slot][frame]</summary>
        public float[][] Floats = [];
    }

    /// <summary>
    /// A demo animation file as DS2 writes them (one hkaAnimationContainer "Merged Animation Container" with per actor
    /// one hkaSkeleton, one hkaSplineCompressedAnimation (type 3, our <see cref="HkSplineEncoder"/>) whose annotation
    /// tracks carry the bone names, and one hkaAnimationBinding named after the skeleton, tracks = bones in order, float
    /// tracks = float slots in order). <paramref name="template"/>: a vanilla demo animation with reflection (scn101010
    /// cut0020_a0000.hkx) whose header and class tables are copied (<see cref="HkPackWriter"/>).
    /// </summary>
    public static byte[] WriteActors(IReadOnlyList<NewActor> actors, float frameDuration, HkPackfile template)
    {
        static float[] Qs((Vector3 T, Quaternion R, Vector3 S) x) => [x.T.X, x.T.Y, x.T.Z, 0, x.R.X, x.R.Y, x.R.Z, x.R.W, x.S.X, x.S.Y, x.S.Z, 0];
        var skeletons = new List<object?>();
        var animations = new List<object?>();
        var bindings = new List<object?>();
        foreach (var a in actors)
        {
            var skel = new HkPObj("hkaSkeleton", true)
            {
                ["name"] = a.Name,
                ["parentIndices"] = a.Parents.Select(p => (object?)(short)p).ToList(),
                ["bones"] = a.Bones.Select(b => (object?)new HkPObj("hkaBone") { ["name"] = b, ["lockTranslation"] = false }).ToList(),
                ["referencePose"] = a.ReferencePose.Select(x => (object?)Qs(x)).ToList(),
                ["referenceFloats"] = a.FloatSlots.Select(_ => (object?)0f).ToList(),
                ["floatSlots"] = a.FloatSlots.Select(s => (object?)s).ToList(),
                ["localFrames"] = new List<object?>(),
                ["partitions"] = new List<object?>(),
            };
            var enc = HkSplineEncoder.Encode(a.Tracks, a.Floats, frameDuration);
            var anim = new HkPObj("hkaSplineCompressedAnimation", true)
            {
                ["type"] = 3L,
                ["duration"] = enc.Duration,
                ["numberOfTransformTracks"] = a.Tracks.Length,
                ["numberOfFloatTracks"] = a.Floats.Length,
                ["extractedMotion"] = null,
                ["annotationTracks"] = a.Bones.Select(b => (object?)new HkPObj("hkaAnnotationTrack") { ["trackName"] = b, ["annotations"] = new List<object?>() }).ToList(),
                ["numFrames"] = enc.NumFrames,
                ["numBlocks"] = enc.NumBlocks,
                ["maxFramesPerBlock"] = enc.MaxFramesPerBlock,
                ["maskAndQuantizationSize"] = enc.MaskAndQuantizationSize,
                ["blockDuration"] = enc.BlockDuration,
                ["blockInverseDuration"] = 1 / enc.BlockDuration,
                ["frameDuration"] = enc.FrameDuration,
                ["blockOffsets"] = enc.BlockOffsets.Select(x => (object?)(uint)x).ToList(),
                ["floatBlockOffsets"] = enc.FloatBlockOffsets.Select(x => (object?)(uint)x).ToList(),
                ["transformOffsets"] = new List<object?>(),
                ["floatOffsets"] = new List<object?>(),
                ["data"] = enc.Data,
                ["endian"] = 0,
            };
            var bind = new HkPObj("hkaAnimationBinding", true)
            {
                ["originalSkeletonName"] = a.Name,
                ["animation"] = anim,
                ["transformTrackToBoneIndices"] = Enumerable.Range(0, a.Tracks.Length).Select(i => (object?)(short)i).ToList(),
                ["floatTrackToFloatSlotIndices"] = Enumerable.Range(0, a.Floats.Length).Select(i => (object?)(short)i).ToList(),
                ["partitionIndices"] = new List<object?>(),
                ["blendHint"] = 0L,
            };
            skeletons.Add(skel); animations.Add(anim); bindings.Add(bind);
        }
        var container = new HkPObj("hkaAnimationContainer", true)
        {
            ["skeletons"] = skeletons, ["animations"] = animations, ["bindings"] = bindings,
            ["attachments"] = new List<object?>(), ["skins"] = new List<object?>(),
        };
        var root = new HkPObj("hkRootLevelContainer", true)
        {
            ["namedVariants"] = new List<object?>
            {
                new HkPObj("hkRootLevelContainerNamedVariant") { ["name"] = "Merged Animation Container", ["className"] = "hkaAnimationContainer", ["variant"] = container },
            },
        };
        return new HkPackWriter(template).Write(root);
    }

    /// <summary>
    /// An object clip as DS2 objects store them (o00_6090 a00_00_0000.hkx: container with no skeleton, one
    /// hkaSplineCompressedAnimation + one binding named after the model's skeleton, annotation tracks = bone names):
    /// a decoded DS1 clip re-encoded per frame (<see cref="HkSplineEncoder"/>), its root motion left out; the bone named
    /// <paramref name="rootFrom"/> is renamed <paramref name="model"/>.
    /// </summary>
    public static byte[] WriteClip(HkSplineAnim src, string rootFrom, string model, HkPackfile template)
    {
        var tracks = Enumerable.Range(0, src.TrackCount).Select(t => Enumerable.Range(0, src.NumFrames).Select(f => src.Sample(t, f)).ToArray()).ToArray();
        var names = Enumerable.Range(0, src.TrackCount).Select(t => t < src.TrackNames.Length ? src.TrackNames[t] : $"track{t}").Select(x => x == rootFrom ? model : x).ToArray();
        var enc = HkSplineEncoder.Encode(tracks, [], src.FrameDuration);
        var anim = new HkPObj("hkaSplineCompressedAnimation", true)
        {
            ["type"] = 3L, ["duration"] = enc.Duration, ["numberOfTransformTracks"] = tracks.Length, ["numberOfFloatTracks"] = 0,
            ["extractedMotion"] = null,
            ["annotationTracks"] = names.Select(b => (object?)new HkPObj("hkaAnnotationTrack") { ["trackName"] = b, ["annotations"] = new List<object?>() }).ToList(),
            ["numFrames"] = enc.NumFrames, ["numBlocks"] = enc.NumBlocks, ["maxFramesPerBlock"] = enc.MaxFramesPerBlock,
            ["maskAndQuantizationSize"] = enc.MaskAndQuantizationSize, ["blockDuration"] = enc.BlockDuration, ["blockInverseDuration"] = 1 / enc.BlockDuration,
            ["frameDuration"] = enc.FrameDuration,
            ["blockOffsets"] = enc.BlockOffsets.Select(x => (object?)(uint)x).ToList(), ["floatBlockOffsets"] = enc.FloatBlockOffsets.Select(x => (object?)(uint)x).ToList(),
            ["transformOffsets"] = new List<object?>(), ["floatOffsets"] = new List<object?>(), ["data"] = enc.Data, ["endian"] = 0,
        };
        var bind = new HkPObj("hkaAnimationBinding", true)
        {
            ["originalSkeletonName"] = model, ["animation"] = anim,
            ["transformTrackToBoneIndices"] = src.TrackToBone.Select(i => (object?)(short)i).ToList(),
            ["floatTrackToFloatSlotIndices"] = new List<object?>(), ["partitionIndices"] = new List<object?>(), ["blendHint"] = 0L,
        };
        var container = new HkPObj("hkaAnimationContainer", true)
        {
            ["skeletons"] = new List<object?>(), ["animations"] = new List<object?> { anim }, ["bindings"] = new List<object?> { bind },
            ["attachments"] = new List<object?>(), ["skins"] = new List<object?>(),
        };
        var root = new HkPObj("hkRootLevelContainer", true)
        {
            ["namedVariants"] = new List<object?> { new HkPObj("hkRootLevelContainerNamedVariant") { ["name"] = "Merged Animation Container", ["className"] = "hkaAnimationContainer", ["variant"] = container } },
        };
        return new HkPackWriter(template).Write(root);
    }

    /// <summary>The actors of one demo animation file (skeleton + animation per binding).</summary>
    public static List<Actor> ReadActors(byte[] hkx)
    {
        var pf = new HkPackfile(hkx).AdoptTypes(Commands.ScnDump.Donors());
        var actors = new List<Actor>();
        var skeletons = pf.Objects.Where(o => o.Class == "hkaSkeleton").Select(o => pf.ReadObject(o.Offset)).ToList();
        foreach (var (off, cls) in pf.Objects.Where(o => o.Class == "hkaAnimationBinding"))
        {
            var bind = pf.ReadObject(off);
            string name = (string)bind["originalSkeletonName"]!;
            var skel = skeletons.First(s => (string?)s["name"] == name);
            var anim = (HkPObj)bind["animation"]!;
            actors.Add(new Actor
            {
                Name = name,
                Bones = ((List<object?>)skel["bones"]!).Select(b => (string)((HkPObj)b!)["name"]!).ToArray(),
                Parents = ((List<object?>)skel["parentIndices"]!).Select(x => Convert.ToInt32(x)).ToArray(),
                Anim = HkSplineAnim.FromPack(anim, bind),
            });
        }
        return actors;
    }
}
