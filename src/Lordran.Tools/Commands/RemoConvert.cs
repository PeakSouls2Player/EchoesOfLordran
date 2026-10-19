using System.Numerics;
using Lordran.Tools.Build.Cutscenes;
using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// remo-convert OUTDIR [SCENE...] [--npc-actors | --pack DIR] (session 8, cutscene track) — converts DS1 cutscenes (default: the crow scenes
/// <see cref="Cutscenes.Crow"/>) into DS2 demos in OUTDIR (scnNNNNNN.scnbnd + a notes file) and checks each result
/// offline: the scnbnd reads back (BND4, ACB), our MQB re-reads and rewrites byte-identical, every cut animation decodes
/// (HkPackfile + our spline decoder) and its samples match what was encoded (largest position / rotation error per
/// actor), the camera still looks where DS1's did (mean cosine between the DS2 camera's -Y axis and the direction to
/// each retargeted character's head, same test as on vanilla demos).
/// --npc-actors (fix pass): DS1's human NPCs join as demo characters (R3b, <see cref="Cutscenes.HumanActors"/>) without
/// build-pack's check that the map registers them, to check their conversion before the npc track is merged.
/// --pack DIR: instead, build-pack's own choices against an already built pack (read only): the DemoParam area
/// (<see cref="Cutscenes.DemoArea"/>) from DIR's map MSB and the NPCs DIR's map GeneratorRegistParam registers.
/// </summary>
public static class RemoConvert
{
    public static int Run(string[] args)
    {
        string outDir = args[1];
        Directory.CreateDirectory(outDir);
        bool npcActors = args.Contains("--npc-actors");
        int pi = Array.IndexOf(args, "--pack");
        string? packDir = pi > 0 ? args[pi + 1] : null;
        var ids = args.Skip(2).Where(a => !a.StartsWith("--") && a != packDir).ToList();
        var tpl = RemoConverter.Templates.Load(ExtractDs2.DefaultOut);
        var scenes = ids.Count > 0 ? Cutscenes.Crow.Where(s => ids.Contains(s.Ds1Scene.ToString())).ToList() : Cutscenes.Crow.ToList();
        int bad = 0;
        foreach (var s0 in scenes)
        {
            var pre = new List<string>();
            Func<int, string?>? gate = npcActors ? null : _ => "remo-convert without --npc-actors or --pack";
            if (packDir != null)
            {
                string map = s0.Ds2Map, data = Path.Combine(packDir, "gamedata");
                var (block, group, why) = Cutscenes.DemoArea(File.ReadAllBytes(Path.Combine(data, "map", map, map + ".msb")), map, 2);
                Console.WriteLine($"DemoParam area for DS1 scn{s0.Ds1Scene} (pack {packDir}): block {block}, group {group}: {why}");
                string reg = Path.Combine(data, "param", $"generatorregistparam_{map}.param");
                var bytes = File.Exists(reg) ? File.ReadAllBytes(reg) : null;
                gate = id => Cutscenes.Unregistered(bytes, map, id, block);
            }
            var s = s0 with { HumanActors = Cutscenes.HumanActors(Program.DefaultDsrRoot, s0, gate, pre) };
            var r = RemoConverter.Convert(Program.DefaultDsrRoot, s, tpl);
            r.Notes.InsertRange(0, pre);
            string path = Path.Combine(outDir, $"scn{r.Demo:D6}.scnbnd");
            File.WriteAllBytes(path, r.ScnBnd);
            Console.WriteLine($"DS1 scn{s.Ds1Scene} ({s.Ds1Map}) → DS2 demo {r.Demo} ({s.Ds2Map}): {r.Cuts.Count} cuts, origin {r.Origin}, {r.ScnBnd.Length:N0} bytes → {path}");
            foreach (var n in r.Notes) Console.WriteLine($"  note: {n}");
            File.WriteAllLines(Path.ChangeExtension(path, ".notes.txt"), r.Notes);
            bad += Check(r, s);
        }
        Console.WriteLine(bad == 0 ? "all checks passed" : $"{bad} checks FAILED");
        return bad == 0 ? 0 : 2;
    }

    static int Check(RemoConverter.Result r, RemoConverter.Scene s)
    {
        // the retargeted characters: DS1 part → DS2 actor (the player, and the NPCs the scene names)
        var humans = new List<(string Ds1, string Ds2)> { ("c0000_0000", RemoConverter.PlayerActor) };
        if (s.HumanActors != null) humans.AddRange(s.HumanActors.Select(kv => (kv.Key, kv.Value)));
        int bad = 0;
        void Report(bool ok, string what) { Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}"); if (!ok) bad++; }
        var bnd = BND4.Read(r.ScnBnd);
        var mqbBytes = bnd.Files.First(f => f.Name.EndsWith(".mqb")).Bytes;
        Report(Ds2Mqb.Read(mqbBytes).Write().AsSpan().SequenceEqual(mqbBytes), $"MQB re-reads and rewrites byte-identical ({mqbBytes.Length} bytes, {r.Mqb.Resources.Count} resources, {r.Mqb.Cuts.Count} cuts)");
        try { var m = MQB.Read(mqbBytes); Report(m.Cuts.Count == r.Mqb.Cuts.Count, $"SoulsFormats reads our MQB ({m.Cuts.Count} cuts)"); }
        catch (Exception e) { Report(false, $"SoulsFormats reads our MQB: {e.Message}"); }
        var acb = ACB.Read(bnd.Files.First(f => f.Name.EndsWith(".acb")).Bytes);
        Report(acb.Assets.Count == 2 * r.Cuts.Count + 1, $"ACB reads back ({acb.Assets.Count} assets)");
        foreach (var (cutName, hkx, actors) in r.Cuts)
        {
            var back = Ds2Scn.ReadActors(bnd.Files.First(f => f.Name == $"{cutName}_a0000.hkx").Bytes);
            Report(back.Count == actors.Count, $"{cutName}: {back.Count} actors decode ({string.Join(", ", back.Select(a => $"{a.Name} {a.Bones.Length} bones {a.Anim.NumFrames} frames"))})");
            foreach (var a in actors)
            {
                var b = back.FirstOrDefault(x => x.Name == a.Name);
                if (b == null) { Report(false, $"{cutName} {a.Name} missing"); continue; }
                float dp = 0, dr = 0, df = 0;
                int frames = a.Tracks[0].Length;
                for (int t = 0; t < a.Tracks.Length; t++)
                    for (int f = 0; f < frames; f++)
                    {
                        var (T, R, _) = b.Anim.Sample(t, f);
                        dp = MathF.Max(dp, Vector3.Distance(T, a.Tracks[t][f].T));
                        dr = MathF.Max(dr, 1 - MathF.Abs(Quaternion.Dot(R, Quaternion.Normalize(a.Tracks[t][f].R))));
                    }
                for (int k = 0; k < a.Floats.Length; k++)
                    for (int f = 0; f < frames; f++) df = MathF.Max(df, MathF.Abs(b.Anim.SampleFloat(k, f) - a.Floats[k][f]));
                Report(dp < 0.01f && dr < 1e-4f && df < 1e-3f, $"{cutName} {a.Name}: decoded samples match (max position error {dp * 1000:0.##} mm, rotation 1-|dot| {dr:0.#e0}, float {df:0.#e0})");
            }
            // retarget: the DS2 character's joints where DS1's were (same scene space; the two rigs have near-equal bone lengths)
            var ds1Cut = r.Remo.Cuts.First(c => c.Name == cutName);
            var cam = back.FirstOrDefault(x => x.Name.StartsWith("cam"));
            foreach (var (ds1Name, ds2Name) in humans)
            {
                var ds1Part = ds1Cut.Parts.FirstOrDefault(p => p.Name == ds1Name);
                var ds2Actor = back.FirstOrDefault(x => x.Name == ds2Name);
                if (ds1Part == null || ds2Actor == null) continue;
                string who = ds2Name == RemoConverter.PlayerActor ? "player" : $"{ds2Name} (DS1 {ds1Name})";
                var sb = new System.Text.StringBuilder();
                bool ok = true;
                foreach (var joint in new[] { "Pelvis", "Head", "L_Hand", "R_Hand", "L_Foot", "R_Foot" })
                {
                    int b1 = Array.IndexOf(ds1Cut.BoneNames, ds1Name + "_" + joint), b2 = Array.IndexOf(ds2Actor.Bones, ds2Name + "_" + joint);
                    if (b1 < 0 || b2 < 0) continue;
                    float max = 0, sum = 0; int n = ds2Actor.Anim.NumFrames;
                    for (int f = 0; f < n; f++)
                    {
                        float d = Vector3.Distance(ds1Cut.BoneInRoot(b1, f).T, ds2Actor.Bone(b2, f).T);
                        max = MathF.Max(max, d); sum += d / n;
                    }
                    sb.Append($" {joint} {sum * 100:0.#}/{max * 100:0.#}");
                    if (max > 0.25f) ok = false;
                }
                Report(ok, $"{cutName} {who} retarget: DS2 joints vs DS1's (mean/max cm):{sb}");
                // the camera still looks at that character as DS1's did (DS1's own value: work\inview.py over remo-dump --csv)
                if (cam != null)
                {
                    int head = Array.FindIndex(ds2Actor.Bones, x => x.EndsWith("_Head"));
                    double sum = 0; int n = cam.Anim.NumFrames;
                    for (int f = 0; f < n; f++)
                    {
                        var (ct, cr) = cam.Bone(0, f);
                        var (ht, _) = ds2Actor.Bone(head, f);
                        sum += Vector3.Dot(Vector3.Transform(-Vector3.UnitY, cr), Vector3.Normalize(ht - ct)) / n;
                    }
                    Console.WriteLine($"  info {cutName}: DS2 camera -Y vs the retargeted {who}'s head: mean cosine {sum:0.###}");
                }
            }
        }
        return bad;
    }
}
