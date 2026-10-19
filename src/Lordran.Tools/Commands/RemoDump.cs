using System.Numerics;
using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// remo-dump SCENE|FILE [--extract DIR] [--frames N] (session 8, cutscene track) — a DS1 cutscene (DSR
/// <c>remo\scnNNNNNN.remobnd.dcx</c>; SCENE = the number, e.g. 180125): per cut the merged Havok animation (parts =
/// MSB part names that prefix the bones, bone counts, which parts move), the decoded camera (<c>camera_win32.sibcam</c>:
/// frames, position/target/FOV samples) and the scene TAE's events. <c>--extract DIR</c> writes the binder's files with
/// their cut folders.
/// </summary>
public static class RemoDump
{
    /// <summary>
    /// remo-dump --survey: the conversion rules checked on every DS1 cutscene (R1: a scene's map parts share one MSB
    /// position = the scene origin; R2: every cut's animation is end − start + 1 frames of its sibcam; which part kinds
    /// the scenes animate). DS1 map of a scene = mAA_BB from its id (Darkroot's 12_00 scenes: m12_00_00_01 too).
    /// </summary>
    static int Survey()
    {
        int scenes = 0, originOk = 0, framesOk = 0, cuts = 0, noMapPart = 0, votesAgree = 0, votesTotal = 0;
        var kinds = new Dictionary<string, int>();
        foreach (var path in Directory.GetFiles(Path.Combine(Program.DefaultDsrRoot, "remo"), "scn*.remobnd.dcx").OrderBy(p => p))
        {
            Ds1Remo remo;
            try { remo = Ds1Remo.Read(path); }
            catch (Exception e) { Console.WriteLine($"{Path.GetFileName(path)}: unreadable ({e.Message})"); continue; }
            scenes++;
            string map = Build.Cutscenes.Cutscenes.SceneMap(remo.SceneId);
            var msbPaths = new[] { map, map[..^2] + "01" }.Select(m => Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", m + ".msb")).Where(File.Exists).ToList();
            var parts = msbPaths.SelectMany(p => { var m = SoulsFormats.MSB1.Read(p); return m.Parts.MapPieces.Cast<SoulsFormats.MSB1.Part>().Concat(m.Parts.Collisions); })
                .GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.First().Position);
            // R1 (RemoConverter.SceneOrigin): the most common "MSB position − bone at frame 0" of the scene's map parts
            var mp = msbPaths.SelectMany(p => { var m = SoulsFormats.MSB1.Read(p); return m.Parts.MapPieces.Cast<SoulsFormats.MSB1.Part>().Concat(m.Parts.Collisions); })
                .GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.First());
            var origin = Build.Cutscenes.RemoConverter.SceneOrigin(remo, mp, out int agree, out int total);
            var origins = total == 0 ? [] : agree == total ? new List<System.Numerics.Vector3> { origin } : [origin, origin + System.Numerics.Vector3.One];
            votesAgree += agree; votesTotal += total;
            if (agree < total && (float)agree / total < 0.75f) Console.WriteLine($"{Path.GetFileName(path)} ({map}): origin {origin} from only {agree} of {total} map parts");
            if (origins.Count == 1) originOk++; else if (origins.Count == 0) noMapPart++;
            bool fr = true;
            foreach (var c in remo.Cuts)
            {
                cuts++;
                if (c.Camera != null && c.Camera.EndFrame - c.Camera.StartFrame + 1 != c.Animation.NumFrames)
                {
                    fr = false;
                    Console.WriteLine($"  {Path.GetFileName(path)} {c.Name}: animation {c.Animation.NumFrames} frames, sibcam {c.Camera.StartFrame}..{c.Camera.EndFrame} ({c.Camera.EndFrame - c.Camera.StartFrame + 1}), records {c.Camera.Frames.Count}");
                }
                foreach (var p in c.Parts.Where(p => !parts.ContainsKey(p.Name)))
                {
                    string k = p.Name == "c0000_0000" ? "player c0000_0000" : p.Name.StartsWith("c0000_") ? "human NPC c0000_xxxx" : p.Name[0] switch { 'c' => "character", 'o' => "object", 'd' => "dummy", 'h' or 'm' => "map part not in MSB", _ => "other" };
                    kinds[k] = kinds.GetValueOrDefault(k) + 1;
                }
            }
            if (fr) framesOk++;
            if (!fr) Console.WriteLine($"{Path.GetFileName(path)} ({map}): frame count ≠ sibcam range");
        }
        Console.WriteLine($"{scenes} scenes, {cuts} cuts: all map parts agree on the origin in {originOk}, no map part {noMapPart}, some disagree in {scenes - originOk - noMapPart}; map parts agreeing with their scene's origin: {votesAgree} of {votesTotal}; animation length = sibcam range in {framesOk} scenes (the rest: static 2-frame cuts)");
        Console.WriteLine("animated parts (cut × part): " + string.Join(", ", kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}")));
        return 0;
    }

    public static int Run(string[] args)
    {
        if (args[1] == "--survey") return Survey();
        string a = args[1];
        string path = File.Exists(a) ? a : Path.Combine(Program.DefaultDsrRoot, "remo", $"scn{a}.remobnd.dcx");
        var remo = Ds1Remo.Read(path);
        int xi = Array.IndexOf(args, "--extract");
        if (xi > 0)
        {
            foreach (var f in remo.Bnd.Files)
            {
                string o = Path.Combine(args[xi + 1], f.Name.TrimStart('\\').Replace('\\', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(o)!);
                File.WriteAllBytes(o, f.Bytes);
            }
        }
        int fi = Array.IndexOf(args, "--frames");
        int samples = fi > 0 ? int.Parse(args[fi + 1]) : 5;
        Console.WriteLine($"{Path.GetFileName(path)}: scene {remo.SceneId}, {remo.Cuts.Count} cuts, {remo.Bnd.Files.Count} files");
        foreach (var c in remo.Cuts)
        {
            var anim = c.Animation;
            Console.WriteLine($"cut {c.Name}: {anim.NumFrames} frames ({anim.Duration:0.###} s at {1 / anim.FrameDuration:0.#} fps), {c.Parts.Count} parts");
            foreach (var p in c.Parts)
            {
                var rootT = c.PartWorld(p.Name, 0);
                var endT = c.PartWorld(p.Name, anim.NumFrames - 1);
                Console.WriteLine($"  part {p.Name,-12} bones {p.Bones.Count,3} (first track {p.Bones[0]}) root f0 {Fmt(rootT.T)} yaw {Yaw(rootT.R):0.#} → f{anim.NumFrames - 1} {Fmt(endT.T)} yaw {Yaw(endT.R):0.#}");
            }
            if (c.Camera is { Frames.Count: > 0 } cam)
            {
                Console.WriteLine($"  camera '{cam.Name}': frames {cam.StartFrame}..{cam.EndFrame}, {cam.Frames.Count} records ({cam.Frames[0].Index}..{cam.Frames[^1].Index}), {cam.Fov.Count} FOV keys (rest {cam.RestFov * 180 / MathF.PI:0.#} deg)");
                for (int k = 0; k < samples; k++)
                {
                    int f = samples == 1 ? 0 : (int)Math.Round(k * (anim.NumFrames - 1) / (double)(samples - 1));
                    var (pos, rot) = cam.At(cam.StartFrame + f);
                    Console.WriteLine($"    f{f,-4} pos {Fmt(pos)} rot(deg) {Fmt(rot * (180 / MathF.PI))} fov {cam.FovAt(cam.StartFrame + f) * 180 / MathF.PI:0.##} deg");
                }
            }
        }
        if (args.Contains("--bones"))
            foreach (var c in remo.Cuts)
                foreach (var p in c.Parts)
                    Console.WriteLine($"{c.Name} {p.Name}: {string.Join(" ", p.Bones.Select(b => $"{b}:{c.BoneNames[b]}<{c.Parents[b]}"))}");
        int ci = Array.IndexOf(args, "--csv");
        if (ci > 0)
        {
            // per frame: camera position/rotation/FOV and the scene position of every part root and head bone (for the
            // camera-convention check in REMO-FORMAT.md)
            using var w = new StreamWriter(args[ci + 1]);
            bool zero = args.Contains("--cam-zero"); // test: camera frame = animation frame (default: start frame + animation frame)
            w.WriteLine("cut,frame,cx,cy,cz,rx,ry,rz,fov,bone,bx,by,bz");
            foreach (var c in remo.Cuts.Where(c => c.Camera != null))
                for (int f = 0; f < c.Animation.NumFrames; f++)
                {
                    int cf = zero ? f : c.Camera!.StartFrame + f;
                    var (pos, rot) = c.Camera!.At(cf);
                    foreach (var b in Enumerable.Range(0, c.BoneNames.Length).Where(b => c.Parents[b] < 0 || c.BoneNames[b].EndsWith("_Head") || c.BoneNames[b].EndsWith("_Neck")))
                    {
                        var (bt, _) = c.BoneInRoot(b, f);
                        w.WriteLine(string.Join(",", c.Name, f, pos.X, pos.Y, pos.Z, rot.X, rot.Y, rot.Z, c.Camera!.FovAt(cf), c.BoneNames[b], bt.X, bt.Y, bt.Z));
                    }
                }
        }
        if (remo.Tae is { } tae)
        {
            Console.WriteLine($"tae: {tae.Animations.Count} animations");
            foreach (var an in tae.Animations)
            {
                Console.WriteLine($"  anim {an.ID}: {an.Events.Count} events");
                foreach (var e in an.Events.OrderBy(e => e.StartTime))
                    Console.WriteLine($"    {e.StartTime,7:0.###}-{e.EndTime,7:0.###} type {e.Type,4} {Convert.ToHexString(e.GetParameterBytes(false))}");
            }
        }
        return 0;
    }

    static string Fmt(Vector3 v) => $"({v.X:0.##}, {v.Y:0.##}, {v.Z:0.##})";
    static float Yaw(Quaternion q) => MathF.Atan2(2 * (q.W * q.Y + q.X * q.Z), 1 - 2 * (q.Y * q.Y + q.X * q.X)) * 180 / MathF.PI;
}
