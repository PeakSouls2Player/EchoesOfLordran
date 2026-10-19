using System.Numerics;
using System.Text;
using Lordran.Tools.Formats.Havok;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// scn-dump FILE [--hkx] [--roundtrip] (session 8, cutscene track) — a DS2 realtime demo (<c>model/scn/scnNNNNNN.scnbnd</c>
/// or a bare <c>.mqb</c>): the binder's files, then the MQB ("MovieSequencer Binary", SoulsFormats MQB, DS2 SOTFS version
/// 0xCA): resources (name, parent, Unk48, path, parameters), cuts (duration in frames at the MQB's frame rate),
/// timelines and events (resource, start, duration, parameters, transforms). <c>--hkx</c> also summarises each cut
/// animation (skeleton names, bone counts, animation lengths, binding names); <c>--roundtrip</c> checks that SoulsFormats
/// writes the MQB back byte-identical. FILE may be a directory: every scnbnd in it (round-trip summary only).
/// </summary>
public static class ScnDump
{
    public static int Run(string[] args)
    {
        string path = args[1];
        bool hkx = args.Contains("--hkx"), rt = args.Contains("--roundtrip");
        Raw = args.Contains("--raw");
        if (Directory.Exists(path))
        {
            int ok = 0, bad = 0;
            foreach (var f in Directory.GetFiles(path, "*.scnbnd").OrderBy(x => x))
            {
                var (mqbName, mqbBytes) = Mqb(File.ReadAllBytes(f));
                string r = RoundTrip(mqbBytes, out var m);
                if (r == "identical") ok++; else bad++;
                Console.WriteLine($"{Path.GetFileName(f)}: {mqbName} v{m?.Version} {m?.Framerate} fps, {m?.Resources.Count} resources, {m?.Cuts.Count} cuts ({string.Join(", ", m?.Cuts.Select(c => $"{c.Name} {c.Duration}f") ?? [])}): {r}");
            }
            Console.WriteLine($"round trip: {ok} identical, {bad} different");
            if (args.Contains("--hkx-roundtrip"))
            {
                // every demo animation file read with adopted reflection and rewritten by HkPackWriter
                int hok = 0, hsame = 0, hbad = 0;
                foreach (var f in Directory.GetFiles(path, "*.scnbnd").OrderBy(x => x))
                    foreach (var h in BND4.Read(f).Files.Where(x => x.Name.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase)))
                    {
                        string r = HkxRoundTrip(h.Bytes);
                        if (r == "identical") hok++; else if (r.StartsWith("objects identical")) hsame++; else { hbad++; Console.WriteLine($"  {Path.GetFileName(f)} {h.Name}: {r}"); }
                    }
                Console.WriteLine($"hkx round trip: {hok} byte-identical, {hsame} with identical objects (layout differs), {hbad} different");
                bad += hbad;
            }
            return bad == 0 ? 0 : 2;
        }
        byte[] bytes = File.ReadAllBytes(path);
        if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
        BND4? bnd = BND4.Is(bytes) ? BND4.Read(bytes) : null;
        if (bnd != null)
        {
            Console.WriteLine($"BND4 version={bnd.Version} format={bnd.Format} unicode={bnd.Unicode} extended={bnd.Extended} files={bnd.Files.Count}");
            foreach (var f in bnd.Files) Console.WriteLine($"  id={f.ID,-3} flags={f.Flags} {f.Bytes.Length,10:N0}  {f.Name}");
        }
        var mqbFile = bnd?.Files.First(f => f.Name.EndsWith(".mqb", StringComparison.OrdinalIgnoreCase)).Bytes ?? bytes;
        var mqb = MQB.Read(mqbFile);
        Print(mqb);
        if (rt) Console.WriteLine($"round trip: {RoundTrip(mqbFile, out _)}");
        int oi = Array.IndexOf(args, "--out");
        if (oi > 0) { File.WriteAllBytes(args[oi + 1], Formats.Ds2Mqb.Read(mqbFile).Write()); File.WriteAllBytes(args[oi + 1] + ".orig", mqbFile); }
        if (args.Contains("--camcheck") && bnd != null) CamCheck(bnd);
        int di = Array.IndexOf(args, "--dump-hkx");
        if (di > 0 && bnd != null) // one demo animation file's full object dump (reflection adopted)
            Console.WriteLine(new HkPackfile(bnd.Files.First(f => f.Name.EndsWith(args[di + 1], StringComparison.OrdinalIgnoreCase)).Bytes).AdoptTypes(Donors()).Dump(int.Parse(args.ElementAtOrDefault(di + 2) ?? "8")));
        if (hkx && bnd != null)
            foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase)))
                Console.WriteLine($"{f.Name}: {HkxSummary(f.Bytes)}");
        return 0;
    }

    static (string Name, byte[] Bytes) Mqb(byte[] scnbnd)
    {
        if (DCX.Is(scnbnd)) scnbnd = DCX.Decompress(scnbnd);
        var f = BND4.Read(scnbnd).Files.First(x => x.Name.EndsWith(".mqb", StringComparison.OrdinalIgnoreCase));
        return (f.Name, f.Bytes);
    }

    /// <summary>A demo animation read (reflection adopted from <see cref="Donors"/>) and rewritten by HkPackWriter.</summary>
    public static string HkxRoundTrip(byte[] hkx)
    {
        try
        {
            var pf = new HkPackfile(hkx).AdoptTypes(Donors());
            var back = new HkPackWriter(pf).Write(pf.ReadRoot());
            if (back.AsSpan().SequenceEqual(hkx)) return "identical";
            // same objects? (the dump without offsets, which legitimately move; as hkx-dump --roundtrip)
            static List<string> Lines(HkPackfile h) => h.Dump(100000).Split('\n').Skip(5)
                .Select(l => System.Text.RegularExpressions.Regex.Replace(l, @"@0x[0-9A-F]+|→0x[0-9A-F]+", "")).ToList();
            bool same = Lines(pf).SequenceEqual(Lines(new HkPackfile(back).AdoptTypes(Donors())));
            return same ? "objects identical (layout differs)" : $"OBJECTS DIFFER (sizes {hkx.Length} → {back.Length})";
        }
        catch (Exception e) { return $"FAILED ({e.GetType().Name}: {e.Message})"; }
    }

    /// <summary>The MQB read with SoulsFormats (for the dump) and rewritten by our own codec (<see cref="Formats.Ds2Mqb"/>).</summary>
    public static string RoundTrip(byte[] mqbBytes, out MQB? mqb)
    {
        mqb = null;
        try { mqb = MQB.Read(mqbBytes); } catch (Exception) { }
        try
        {
            var back = Formats.Ds2Mqb.Read(mqbBytes).Write();
            if (back.AsSpan().SequenceEqual(mqbBytes)) return "identical";
            int i = 0; while (i < back.Length && i < mqbBytes.Length && back[i] == mqbBytes[i]) i++;
            return $"DIFFERENT (sizes {mqbBytes.Length} → {back.Length}, first difference at 0x{i:X})";
        }
        catch (Exception e) { return $"FAILED ({e.GetType().Name}: {e.Message})"; }
    }

    public static void Print(MQB mqb)
    {
        Console.WriteLine($"MQB '{mqb.Name}' version {mqb.Version} ({(uint)mqb.Version:X}) bigEndian={mqb.BigEndian} framerate={mqb.Framerate} resourceDir='{mqb.ResourceDirectory}'");
        Console.WriteLine($"resources ({mqb.Resources.Count}):");
        for (int i = 0; i < mqb.Resources.Count; i++)
        {
            var r = mqb.Resources[i];
            Console.WriteLine($"  [{i}] '{r.Name}' parent={r.ParentIndex} unk48={r.Unk48} path='{r.Path}'");
            foreach (var p in r.Parameters) Console.WriteLine($"      {Param(p)}");
        }
        foreach (var c in mqb.Cuts)
        {
            Console.WriteLine($"cut '{c.Name}' unk44={c.Unk44} duration={c.Duration} frames ({c.Duration / mqb.Framerate:0.##} s), {c.Timelines.Count} timelines");
            for (int t = 0; t < c.Timelines.Count; t++)
            {
                var tl = c.Timelines[t];
                Console.WriteLine($"  timeline {t} unk10={tl.Unk10} params={tl.Parameters.Count} events={tl.Events.Count}");
                foreach (var p in tl.Parameters) Console.WriteLine($"      {Param(p)}");
                foreach (var e in tl.Events)
                {
                    string res = e.ResourceIndex >= 0 && e.ResourceIndex < mqb.Resources.Count ? mqb.Resources[e.ResourceIndex].Name : "-";
                    Console.WriteLine($"    event id={e.ID} res={e.ResourceIndex} ({res}) unk08={e.Unk08} start={e.StartFrame} dur={e.Duration} unk14={e.Unk14} unk18={e.Unk18} unk1C={e.Unk1C} unk20={e.Unk20} unk28={e.Unk28} params={e.Parameters.Count} transforms={e.Transforms.Count}");
                    foreach (var p in e.Parameters) Console.WriteLine($"        {Param(p)}");
                    foreach (var x in e.Transforms)
                        Console.WriteLine($"        transform f{x.Frame} T={x.Translation} R={x.Rotation} S={x.Scale} u10={x.Unk10} u1C={x.Unk1C} u34={x.Unk34} u40={x.Unk40} u58={x.Unk58} u64={x.Unk64}");
                }
            }
        }
    }

    static string Param(MQB.Parameter p)
    {
        string v = p.Value switch
        {
            byte[] b => $"bytes[{b.Length}] {Convert.ToHexString(b.Take(64).ToArray())}{(b.Length > 64 ? "…" : "")}",
            string s => $"\"{s}\"",
            null => "null",
            _ => p.Value.ToString() ?? "",
        };
        var sb = new StringBuilder($"{p.Name}: {p.Type}×{p.MemberCount} = {v}");
        foreach (var s in p.Sequences)
            sb.Append($" | seq {s.ValueType} pt{s.PointType} idx{s.ValueIndex}: {string.Join(" ", s.Points.Select(x => $"({x.Value}@{x.Unk08}{(s.PointType == 2 ? $",{x.Unk10},{x.Unk14}" : "")})"))}");
        return sb.ToString();
    }

    /// <summary>
    /// The camera-axis check (SCN-FORMAT.md): per cut, which local axis of the camera bone points at the characters'
    /// heads (mean cosine over all frames) and which one is closest to world up.
    /// </summary>
    static void CamCheck(BND4 bnd)
    {
        var axes = new (string Name, Vector3 V)[] { ("+X", Vector3.UnitX), ("-X", -Vector3.UnitX), ("+Y", Vector3.UnitY), ("-Y", -Vector3.UnitY), ("+Z", Vector3.UnitZ), ("-Z", -Vector3.UnitZ) };
        foreach (var f in bnd.Files.Where(f => f.Name.EndsWith("_a0000.hkx", StringComparison.OrdinalIgnoreCase)))
        {
            var actors = Formats.Ds2Scn.ReadActors(f.Bytes);
            foreach (var a in actors)
            {
                var (t0, _) = a.Bone(Math.Min(1, a.Bones.Length - 1), 0);
                var (t1, _) = a.Bone(Math.Min(1, a.Bones.Length - 1), a.Anim.NumFrames - 1);
                Console.WriteLine($"{f.Name} actor {a.Name} ({a.Bones.Length} bones, {a.Anim.NumFrames} frames): {a.Bones[Math.Min(1, a.Bones.Length - 1)]} f0 ({t0.X:0.##}, {t0.Y:0.##}, {t0.Z:0.##}) → ({t1.X:0.##}, {t1.Y:0.##}, {t1.Z:0.##})");
                for (int b = 0; b < Math.Min(3, a.Bones.Length); b++)
                    foreach (var fr in new[] { 0, a.Anim.NumFrames - 1 })
                    {
                        var (lt, lr, ls) = a.Anim.Sample(b, fr);
                        Console.WriteLine($"      local {a.Bones[b]} f{fr}: T=({lt.X:0.###}, {lt.Y:0.###}, {lt.Z:0.###}) R=({lr.X:0.###}, {lr.Y:0.###}, {lr.Z:0.###}, {lr.W:0.###}) S={ls.X:0.##}");
                    }
            }
            var cam = actors.FirstOrDefault(a => a.Name.StartsWith("cam"));
            if (cam == null) continue;
            foreach (var a in actors.Where(a => a != cam))
            {
                int head = Array.FindIndex(a.Bones, b => b.EndsWith("_Head"));
                if (head < 0) continue;
                var look = new double[6]; var up = new double[6]; int n = cam.Anim.NumFrames;
                for (int fr = 0; fr < n; fr++)
                {
                    var (ct, cr) = cam.Bone(0, fr);
                    var (ht, _) = a.Bone(head, fr);
                    var d = Vector3.Normalize(ht - ct);
                    for (int k = 0; k < 6; k++) { var v = Vector3.Transform(axes[k].V, cr); look[k] += Vector3.Dot(v, d) / n; up[k] += v.Y / n; }
                }
                int bl = Array.IndexOf(look, look.Max()), bu = Array.IndexOf(up, up.Max());
                Console.WriteLine($"{f.Name} {a.Name}: look {axes[bl].Name} (mean cos {look[bl]:0.###}), up {axes[bu].Name} (mean {up[bu]:0.###}); FOV {cam.Anim.SampleFloat(0, 0) * 180 / MathF.PI:0.#}°–{cam.Anim.SampleFloat(0, n - 1) * 180 / MathF.PI:0.#}°");
            }
        }
    }

    static bool Raw;
    static HkPackfile[]? donors;
    /// <summary>Reflection donors for DS2's type-less demo animations: the FoFG bird object's animation and skeleton
    /// (o00_6090, itself a demo actor of scn101010).</summary>
    public static HkPackfile[] Donors()
    {
        if (donors != null) return donors;
        var bnd = BND4.Read(Path.Combine(ExtractDs2.DefaultOut, "model", "obj", "o00_6090.bnd"));
        donors = [new HkPackfile(bnd.Files.First(f => f.Name.EndsWith("a00_00_0000.hkx")).Bytes), new HkPackfile(bnd.Files.First(f => f.Name.EndsWith("skeleton.hkx")).Bytes)];
        return donors;
    }

    /// <summary>A DS2 packfile's skeletons/animations/bindings, or a DS1 tagfile's, in one line each.</summary>
    public static string HkxSummary(byte[] b)
    {
        try
        {
            var pf = new HkPackfile(b).AdoptTypes(Donors());
            var sb = new StringBuilder($"packfile {pf.ContentsVersion}:");
            foreach (var (off, cls) in pf.Objects)
            {
                var o = pf.ReadObject(off);
                switch (cls)
                {
                    case "hkaSkeleton":
                        var bones = (List<object?>?)o["bones"] ?? [];
                        sb.Append($"\n    skeleton '{o["name"]}' {bones.Count} bones: {string.Join(", ", bones.Take(12).Select(x => ((HkPObj)x!)["name"]))}{(bones.Count > 12 ? ", …" : "")}");
                        break;
                    case "hkaSplineCompressedAnimation" or "hkaInterleavedUncompressedAnimation":
                        sb.Append($"\n    {cls} duration={o["duration"]} tracks={o["numberOfTransformTracks"]} floatTracks={o["numberOfFloatTracks"]} frames={o["numFrames"]} blocks={o["numBlocks"]} type={o["type"]}");
                        if (o["annotationTracks"] is List<object?> at)
                            sb.Append($" annotations={at.Count}: {string.Join(", ", at.Take(6).Select(x => ((HkPObj)x!)["trackName"]))}");
                        if (cls == "hkaSplineCompressedAnimation" && Convert.ToInt32(o["numberOfTransformTracks"]) <= 2)
                        {
                            sb.Append($"\n      maxFramesPerBlock={o["maxFramesPerBlock"]} maskAndQuantizationSize={o["maskAndQuantizationSize"]} blockOffsets=[{string.Join(",", (List<object?>)o["blockOffsets"]!)}] floatBlockOffsets=[{string.Join(",", (List<object?>)o["floatBlockOffsets"]!)}] data={((List<object?>)o["data"]!).Count} bytes");
                            if (Raw) sb.Append($"\n      data: {Convert.ToHexString(((List<object?>)o["data"]!).Select(x => Convert.ToByte(x)).Take(400).ToArray())}");
                            try
                            {
                                var anim = HkSplineAnim.FromPack(o, null);
                                for (int k = 0; k < 5; k++)
                                {
                                    float f = k * (anim.NumFrames - 1) / 4f;
                                    var (t, r, _) = anim.Sample(0, f);
                                    string fl = anim.FloatTrackCount > 0 ? $" float0={anim.SampleFloat(0, f):0.####}" : "";
                                    sb.Append($"\n      f{f:0.#}: T=({t.X:0.###}, {t.Y:0.###}, {t.Z:0.###}) R=({r.X:0.####}, {r.Y:0.####}, {r.Z:0.####}, {r.W:0.####}){fl}");
                                }
                            }
                            catch (Exception e) { sb.Append($"\n      (decode failed: {e.Message})"); }
                        }
                        break;
                    case "hkaAnimationBinding":
                        var map = (List<object?>?)o["transformTrackToBoneIndices"] ?? [];
                        sb.Append($"\n    binding skeleton='{o["originalSkeletonName"]}' tracks={map.Count} blend={o["blendHint"]}");
                        break;
                }
            }
            return sb.ToString();
        }
        catch (InvalidDataException)
        {
            var tag = new HkTagfile(b);
            return $"tagfile root {tag.Root?.Type.Name}";
        }
    }
}
