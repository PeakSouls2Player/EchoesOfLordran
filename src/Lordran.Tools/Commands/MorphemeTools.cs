using System.Globalization;
using Lordran.Tools.Build.Chr;
using Lordran.Tools.Formats.Havok;
using Lordran.Tools.Formats.Morpheme;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Morpheme (DS2 animation) commands, session 8 (track chr: the 1:1 DS1 character pipeline):
/// <list type="bullet">
/// <item><c>nsa-dump SRC [CLIP] [--frames 0,10,..] [--ch 0,1,..]</c>: header, channel lists, factors, trajectory and
/// decoded values of one clip (SRC = .nsa file, or a folder / .anibnd(.dcx) with CLIP = name substring).</item>
/// <item><c>nsa-roundtrip SRC [--ref DIR] [--out DIR] [-v]</c>: every clip of SRC (folder or anibnd): layout parse →
/// build, decode → encode with the clip's own settings (template) and with our own (fresh); byte comparison with the
/// original, decoded error of the fresh clip, and with --ref byte comparison against NAME.template.nsa /
/// NAME.fresh.nsa (validate\py_nsa_ref.py output).</item>
/// <item><c>nsa-compare A.nsa B.nsa</c>: byte identity and decoded max differences.</item>
/// <item><c>nmb-rig-dump SRC [--events [NAME]] [--files] [--net] [--skeleton-json OUT]</c>: bundles, rig bones (bind
/// pose, channel), channel map, event tracks, file-name table, rig references inside the network; the rig as a
/// skeleton.json (the input format of nmb-rig-replace).</item>
/// <item><c>nmb-roundtrip PATH...</c>: every NMB in the paths (.nmb, .anibnd(.dcx), folders): bundle re-emit and
/// write(read()) of every rig, channel map and event track, byte compared.</item>
/// <item><c>nmb-rig-replace TEMPLATE SKELETON.json OUT.nmb [--remap-by-name]</c>: rig + channel map replacement with
/// the network patches (ds1_rig_into_nmb.py equivalent with --remap-by-name).</item>
/// <item><c>ds1-anim-to-nsa SKELETON.hkx ANIM.hkx... OUTDIR [--fps F] [--no-root-motion] [--samples-json]
/// [--nmb TEMPLATE]</c>: DS1 clips → NSA (+ skeleton.json, optional full-precision sample JSON, optional NMB with the
/// DS1 rig put into TEMPLATE by name).</item>
/// </list>
/// </summary>
public static class MorphemeTools
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>(name, bytes) of every file in SRC: a folder (*.nsa, *.nmb), a .nsa/.nmb file, or a BND (.anibnd, .dcx).</summary>
    public static List<(string Name, byte[] Bytes)> LoadFiles(string src)
    {
        if (Directory.Exists(src))
            return Directory.GetFiles(src).Where(f => f.EndsWith(".nsa", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.Ordinal).Select(f => (Path.GetFileName(f), File.ReadAllBytes(f))).ToList();
        byte[] bytes = File.ReadAllBytes(src);
        if (src.EndsWith(".nsa", StringComparison.OrdinalIgnoreCase) || src.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase))
            return [(Path.GetFileName(src), bytes)];
        if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
        var files = BND4.Is(bytes) ? BND4.Read(bytes).Files : BND3.Read(bytes).Files;
        return files.Select(f => (Path.GetFileName(f.Name.Replace('\\', '/')), f.Bytes)).OrderBy(f => f.Item1, StringComparer.Ordinal).ToList();
    }

    static string Opt(string[] args, string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault() ?? "";
    static IEnumerable<string> Positional(string[] args, params string[] valued)
    {
        for (int i = 1; i < args.Length; i++)
        {
            if (valued.Contains(args[i])) { i++; continue; }
            if (args[i].StartsWith("--") || args[i] == "-v") continue;
            yield return args[i];
        }
    }
    static string F(double v, string fmt = "0.######") => v.ToString(fmt, Inv);
    static string Sg(float v) { string s = v.ToString("0.0000", Inv); return s.StartsWith('-') ? s : "+" + s; }
    static string Fa(float[] f) => string.Join(", ", f.Select(x => F(x, "0.#########")));

    // ------------------------------------------------------------------ nsa-dump
    public static int NsaDump(string[] args)
    {
        var pos = Positional(args, "--frames", "--ch").ToList();
        if (pos.Count == 0) { Console.Error.WriteLine("usage: nsa-dump SRC [CLIP] [--frames 0,10] [--ch 0,1]"); return 1; }
        var files = LoadFiles(pos[0]).Where(f => f.Name.EndsWith(".nsa", StringComparison.OrdinalIgnoreCase)).ToList();
        if (pos.Count > 1) files = files.Where(f => f.Name.Contains(pos[1], StringComparison.OrdinalIgnoreCase)).ToList();
        if (files.Count == 0) { Console.Error.WriteLine("no clip"); return 1; }
        var (name, d) = files[0];
        var n = NsaFile.Parse(d);
        var c = NsaCodec.Decode(n);
        Console.WriteLine($"{name}: {d.Length} bytes, {F(n.Duration, "0.####")} s, {F(n.Fps, "0.###")} fps, {c.Frames} frames, {n.ChannelCount} channels, sections {n.Sections.Count}, u14 {n.U14}");
        Console.WriteLine($"  static T {n.StaticT.Count}: [{string.Join(",", n.StaticT)}]");
        Console.WriteLine($"  static R {n.StaticR.Count}: [{string.Join(",", n.StaticR)}]");
        Console.WriteLine($"  dynamic T {n.DynT.Count}: [{string.Join(",", n.DynT)}]");
        Console.WriteLine($"  dynamic R {n.DynR.Count}: [{string.Join(",", n.DynR)}]");
        Console.WriteLine($"  static tFac [{Fa(n.Static.TFac)}]  rFac [{Fa(n.Static.RFac)}]");
        Console.WriteLine($"  tInit [{Fa(n.TInit)}]");
        for (int i = 0; i < n.TFactors.Count; i++) Console.WriteLine($"  tF[{i}] [{Fa(n.TFactors[i])}]");
        for (int i = 0; i < n.RFactors.Count; i++) Console.WriteLine($"  rF[{i}] [{Fa(n.RFactors[i])}]");
        if (n.Root is { } r)
        {
            Console.WriteLine($"  trajectory: {r.N} samples @ {F(r.Fps, "0.###")} fps, pos {(r.Pos == null ? "constant" : "animated")} fac [{Fa(r.PosFac)}], rot {(r.Rot == null ? "constant" : "animated")} fac [{Fa(r.RotFac)}]");
            var e = c.RootPos[^1]; var q = c.RootRot[^1];
            double yaw = 2 * Math.Atan2(q.Z, q.W) * 180 / Math.PI;
            Console.WriteLine($"    end: pos ({F(e.X, "0.####")}, {F(e.Y, "0.####")}, {F(e.Z, "0.####")}) yaw about Z {F(yaw, "0.##")} deg");
        }
        else Console.WriteLine($"  no trajectory segment (tail {n.Tail?.Length} bytes)");
        var frames = Opt(args, "--frames") is { Length: > 0 } fs ? fs.Split(',').Select(int.Parse).ToList() : [0, (c.Frames - 1) / 2, c.Frames - 1];
        var chans = Opt(args, "--ch") is { Length: > 0 } cs ? cs.Split(',').Select(int.Parse).ToList() : Enumerable.Range(0, Math.Min(n.ChannelCount, 6)).ToList();
        foreach (int ch in chans)
        {
            Console.WriteLine($"  ch {ch} ({(c.StaticT[ch] ? "T static" : "T dyn")}, {(c.StaticR[ch] ? "R static" : "R dyn")})");
            foreach (int f in frames.Where(f => f >= 0 && f < c.Frames))
            {
                var p = c.Pos[ch][f]; var q = c.Rot[ch][f];
                Console.WriteLine($"     f{f,-4} t=({F(p.X)}, {F(p.Y)}, {F(p.Z)}) q(wxyz)=({F(q.W)}, {F(q.X)}, {F(q.Y)}, {F(q.Z)})");
            }
        }
        return 0;
    }

    // ------------------------------------------------------------------ nsa-roundtrip
    public static int NsaRoundtrip(string[] args)
    {
        var pos = Positional(args, "--ref", "--out").ToList();
        if (pos.Count == 0) { Console.Error.WriteLine("usage: nsa-roundtrip SRC [--ref DIR] [--out DIR] [-v]"); return 1; }
        string refDir = Opt(args, "--ref"), outDir = Opt(args, "--out");
        bool verbose = args.Contains("-v");
        if (outDir != "") Directory.CreateDirectory(outDir);
        var clips = LoadFiles(pos[0]).Where(f => f.Name.EndsWith(".nsa", StringComparison.OrdinalIgnoreCase)).ToList();
        int layout = 0, tmpl = 0, tmplFloats = 0, freshSize = 0, freshSplit = 0, refT = 0, refF = 0, refN = 0;
        double ep = 0, er = 0, rp = 0, rr = 0;
        long sizeO = 0, sizeF = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var fails = new List<string>();
        foreach (var (name, d) in clips)
        {
            var n = NsaFile.Parse(d);
            bool lay = n.Build().AsSpan().SequenceEqual(d);
            if (args.Contains("--survey"))
                Console.WriteLine($"  survey {name}: static tc {n.Static.TRaw.Count} rc {n.Static.RRaw.Count} pT 0x{n.Static.ParsedPT:x} pR 0x{n.Static.ParsedPR:x} sections {n.Sections.Count} root {(n.Root != null ? 1 : 0)}");
            layout += lay ? 1 : 0;
            var clip = NsaCodec.Decode(n);
            byte[] bt = NsaEncoder.Encode(clip, n);
            bool ti = bt.AsSpan().SequenceEqual(d);
            tmpl += ti ? 1 : 0;
            if (!ti)
            {
                var ct = NsaCodec.Decode(NsaFile.Parse(bt));
                if (NsaCodec.SameFloats(clip, ct)) tmplFloats++;
            }
            byte[] bf = NsaEncoder.Encode(clip);
            var m = NsaFile.Parse(bf);
            var cf = NsaCodec.Decode(m);
            var (a, b, c2, d2) = NsaCodec.Compare(clip, cf);
            ep = Math.Max(ep, a); er = Math.Max(er, b); rp = Math.Max(rp, c2); rr = Math.Max(rr, d2);
            freshSize += bf.Length == d.Length ? 1 : 0;
            freshSplit += n.StaticT.SequenceEqual(m.StaticT) && n.StaticR.SequenceEqual(m.StaticR) && n.DynT.SequenceEqual(m.DynT) && n.DynR.SequenceEqual(m.DynR) ? 1 : 0;
            sizeO += d.Length; sizeF += bf.Length;
            string stem = Path.GetFileNameWithoutExtension(name);
            if (outDir != "")
            {
                File.WriteAllBytes(Path.Combine(outDir, stem + ".template.nsa"), bt);
                File.WriteAllBytes(Path.Combine(outDir, stem + ".fresh.nsa"), bf);
            }
            string refNote = "";
            if (refDir != "")
            {
                string pt = Path.Combine(refDir, stem + ".template.nsa"), pf = Path.Combine(refDir, stem + ".fresh.nsa");
                if (File.Exists(pt) && File.Exists(pf))
                {
                    refN++;
                    bool st = File.ReadAllBytes(pt).AsSpan().SequenceEqual(bt), sf = File.ReadAllBytes(pf).AsSpan().SequenceEqual(bf);
                    refT += st ? 1 : 0; refF += sf ? 1 : 0;
                    refNote = $" ref: template {(st ? "SAME" : "DIFF")} fresh {(sf ? "SAME" : "DIFF")}";
                    if (!st || !sf) fails.Add($"{name}:{refNote}{FirstDiff(File.ReadAllBytes(sf ? pt : pf), sf ? bt : bf)}");
                }
                else refNote = " ref: missing";
            }
            if (verbose || !lay || !ti)
                Console.WriteLine($"  {name}: layout {(lay ? "SAME" : "DIFF")} template {(ti ? "SAME" : "DIFF")} fresh size {d.Length}->{bf.Length} "
                    + $"err pos {a * 1000:0.###} mm rot {b:0.####} deg root {c2 * 1000:0.###} mm {d2:0.####} deg{refNote}");
        }
        int N = clips.Count;
        Console.WriteLine($"{pos[0]}: {N} clips in {sw.Elapsed.TotalSeconds:0.0} s");
        Console.WriteLine($"  layout parse->build byte-identical      {layout}/{N}");
        Console.WriteLine($"  template re-encode byte-identical       {tmpl}/{N} (+{tmplFloats} differing only where the floats decode identically)");
        Console.WriteLine($"  fresh re-encode: same size {freshSize}/{N}, same static/dynamic split {freshSplit}/{N}, total size {sizeO} -> {sizeF}");
        Console.WriteLine($"  fresh vs original (decoded): max pos err {ep * 1000:0.####} mm, rot {er:0.#####} deg, root pos {rp * 1000:0.####} mm, root rot {rr:0.#######} deg");
        if (refDir != "")
        {
            Console.WriteLine($"  vs reference {refDir}: template identical {refT}/{refN}, fresh identical {refF}/{refN}");
            foreach (var f in fails.Take(20)) Console.WriteLine("    " + f);
        }
        return 0;
    }

    static string FirstDiff(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) if (a[i] != b[i]) return $" (len {a.Length}/{b.Length}, first diff at 0x{i:X})";
        return $" (len {a.Length}/{b.Length})";
    }

    // ------------------------------------------------------------------ nsa-compare
    public static int NsaCompare(string[] args)
    {
        byte[] a = File.ReadAllBytes(args[1]), b = File.ReadAllBytes(args[2]);
        var na = NsaFile.Parse(a); var nb = NsaFile.Parse(b);
        var ca = NsaCodec.Decode(na); var cb = NsaCodec.Decode(nb);
        Console.WriteLine($"{Path.GetFileName(args[1])} vs {Path.GetFileName(args[2])}: bytes {(a.AsSpan().SequenceEqual(b) ? "IDENTICAL" : "different" + FirstDiff(a, b))}");
        Console.WriteLine($"  A: {ca.Frames} frames @ {F(na.Fps)} fps, {na.ChannelCount} ch, static T/R {na.StaticT.Count}/{na.StaticR.Count}, dyn T/R {na.DynT.Count}/{na.DynR.Count}, trajectory {(na.Root == null ? "none" : $"pos {(na.Root.Pos == null ? "const" : "anim")} rot {(na.Root.Rot == null ? "const" : "anim")}")}");
        Console.WriteLine($"  B: {cb.Frames} frames @ {F(nb.Fps)} fps, {nb.ChannelCount} ch, static T/R {nb.StaticT.Count}/{nb.StaticR.Count}, dyn T/R {nb.DynT.Count}/{nb.DynR.Count}, trajectory {(nb.Root == null ? "none" : $"pos {(nb.Root.Pos == null ? "const" : "anim")} rot {(nb.Root.Rot == null ? "const" : "anim")}")}");
        if (ca.Frames != cb.Frames || ca.ChannelCount != cb.ChannelCount) { Console.WriteLine("  frame/channel counts differ"); return 1; }
        double maxQ = 0;   // max abs difference of quaternion components (sign aligned)
        int worstCh = -1;  double worstP = 0;
        for (int ch = 0; ch < ca.ChannelCount; ch++)
            for (int f = 0; f < ca.Frames; f++)
            {
                var qa = ca.Rot[ch][f]; var qb = cb.Rot[ch][f];
                if (MorphemeMath.Dot(qa, qb) < 0) qb = qb.Neg();
                for (int k = 0; k < 4; k++) maxQ = Math.Max(maxQ, Math.Abs(qa[k] - qb[k]));
                for (int k = 0; k < 3; k++) { double e = Math.Abs(ca.Pos[ch][f][k] - cb.Pos[ch][f][k]); if (e > worstP) { worstP = e; worstCh = ch; } }
            }
        var (ep, er, rp, rr) = NsaCodec.Compare(ca, cb);
        Console.WriteLine($"  decoded max diff: translation {ep:0.#######} m (ch {worstCh}), rotation {er:0.######} deg (quaternion component {maxQ:0.#######}), trajectory pos {rp:0.#######} m, rot {rr:0.######} deg");
        double tol = Opt(args, "--tol") is { Length: > 0 } t ? double.Parse(t, Inv) : 1e-4;
        bool ok = ep <= tol && maxQ <= tol && rp <= tol;
        Console.WriteLine($"  within {tol:0.#####}: {(ok ? "YES" : "NO")}");
        return 0;
    }

    // ------------------------------------------------------------------ nmb-rig-dump
    static (string Name, byte[] Bytes) LoadNmb(string src)
    {
        var f = LoadFiles(src).FirstOrDefault(x => x.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase));
        if (f.Bytes == null) throw new FileNotFoundException("no .nmb in " + src);
        return f;
    }

    public static int NmbRigDump(string[] args)
    {
        var pos = Positional(args, "--skeleton-json").ToList();
        if (pos.Count == 0) { Console.Error.WriteLine("usage: nmb-rig-dump SRC [--events [NAME]] [--files] [--net] [--skeleton-json OUT]"); return 1; }
        var (name, d) = LoadNmb(pos[0]);
        var nmb = NmbFile.Read(d);
        Console.WriteLine($"{name}: {d.Length} bytes, {nmb.Bundles.Count} bundles: " + string.Join(", ", nmb.Bundles.GroupBy(b => b.Type).OrderBy(g => g.Key).Select(g => $"type {g.Key} x{g.Count()}")));
        foreach (var b in nmb.Bundles.Where(b => b.Type is not (3 or 4 or 5)))
            Console.WriteLine($"  type {b.Type,2} @0x{b.Offset:X} data@0x{b.DataOffset:X} size 0x{b.Data.Length:X} align {b.Align} id 0x{b.AssetId:X8} guid {Convert.ToHexString(b.Guid).ToLowerInvariant()}");
        foreach (var rb in nmb.Bundles.Where(b => b.Type == 1))
        {
            var r = NmbRig.Read(rb.Data);
            Console.WriteLine($"rig 0x{rb.AssetId:X8}: {r.Names.Count} bones, trajectory {r.Traj} ({r.Names[r.Traj]}), character root {r.Root} ({r.Names[r.Root]})");
            var maps = nmb.Bundles.Where(b => b.Type == 2).Select(b => NmbChannelMap.Read(b.Data)).ToList();
            var m = maps.FirstOrDefault(x => x.Used.Length == r.Names.Count) ?? maps.FirstOrDefault();
            var r2a = new Dictionary<int, int>();
            if (m != null) for (int c = 0; c < m.AnimToRig.Count; c++) r2a.TryAdd(m.AnimToRig[c], c);
            for (int i = 0; i < r.Names.Count; i++)
            {
                int p = r.Parents[i];
                var t = r.BindPos[i]; var q = r.BindRot[i];
                Console.WriteLine($"  {i,3} ch={(r2a.TryGetValue(i, out int ch) ? ch.ToString() : "-"),3} {r.Names[i],-24} parent={p,3} {(p >= 0 ? r.Names[p] : ""),-22} "
                    + $"pos=({Sg(t[0])},{Sg(t[1])},{Sg(t[2])}) rot(xyzw)=({Sg(q[0])},{Sg(q[1])},{Sg(q[2])},{Sg(q[3])})");
            }
        }
        foreach (var mb in nmb.Bundles.Where(b => b.Type == 2))
        {
            var m = NmbChannelMap.Read(mb.Data);
            bool ident = m.AnimToRig.Select((v, c) => v == c + 1).All(x => x);
            Console.WriteLine($"channel map 0x{mb.AssetId:X8}: type {m.MapType}, {m.AnimToRig.Count} channels{(ident ? " (channel c = rig bone c+1)" : ": " + string.Join(",", m.AnimToRig))}, unused rig bones [{string.Join(",", m.Used.Select((u, i) => (u, i)).Where(x => !x.u).Select(x => x.i))}]");
        }
        var tracks = nmb.Bundles.Where(b => b.Type is 3 or 4 or 5).ToList();
        Console.WriteLine($"event tracks: {tracks.Count} (" + string.Join(", ", tracks.Select(b => NmbEventTrack.Read(b.Data, b.Type).Name).GroupBy(x => x).OrderByDescending(g => g.Count()).Take(14).Select(g => $"{g.Key} x{g.Count()}")) + ")");
        if (args.Contains("--events"))
        {
            string flt = args.SkipWhile(a => a != "--events").Skip(1).FirstOrDefault(a => !a.StartsWith("--")) ?? "";
            foreach (var b in tracks)
            {
                var t = NmbEventTrack.Read(b.Data, b.Type);
                if (flt != "" && !t.Name.Contains(flt)) continue;
                Console.WriteLine($"  T{t.BundleType} id={t.TrackId} ch={t.ChannelId} asset=0x{b.AssetId:X8} {t.Name} ud={t.UserData} " + string.Join("; ", t.Events.Select(e => $"{e.Start:0.###}+{e.Duration:0.###}={e.Value}")));
            }
        }
        if (Opt(args, "--skeleton-json") is { Length: > 0 } sj)
        {
            // the first rig + channel map in the nmb-rig-replace / ds1-anim-to-nsa skeleton.json format
            var r = NmbRig.Read(nmb.Bundles[nmb.IndexOf(1)].Data);
            var m = NmbChannelMap.Read(nmb.Bundles[nmb.IndexOf(2)].Data);
            new MorphemeSkeleton
            {
                Names = r.Names, Parents = r.Parents, Traj = r.Traj, Root = r.Root, AnimToRig = m.AnimToRig,
                BindPos = r.BindPos.Select(p => new double[] { p[0], p[1], p[2] }).ToList(),
                BindRot = r.BindRot.Select(q => new double[] { q[0], q[1], q[2], q[3] }).ToList(),
            }.Save(sj);
            Console.WriteLine($"wrote {sj}");
        }
        if (args.Contains("--files") && nmb.IndexOf(13) is int fi and >= 0)
        {
            var fn = NmbFileNames.Read(nmb.Bundles[fi].Data);
            for (int i = 0; i < fn.Anims.Count; i++)
                Console.WriteLine($"  [{i}] {fn.Anims[i]} | {(i < fn.Sources.Count ? fn.Sources[i] : "")}");
        }
        if (args.Contains("--net") && nmb.IndexOf(10) is int ni and >= 0 && nmb.IndexOf(1) is int ri and >= 0)
        {
            int nr = NmbRig.Read(nmb.Bundles[ri].Data).Names.Count;
            var refs = NmbRigReplace.NetworkRigRefs(nmb.Bundles[ni].Data, nr);
            Console.WriteLine($"network rig references (rig {nr} bones): RIG attribs {refs.RigAttr.Count}, bone-index fields {refs.IndexFields.Count}, bone lists {refs.BoneLists.Count}, per-bone arrays {refs.PerBone.Count}, unhandled (mirror) {refs.Unhandled.Count}");
            var net = nmb.Bundles[ni].Data;
            foreach (int o in refs.RigAttr) Console.WriteLine($"  RIG attrib @0x{o:x}: rig id 0x{NmbFile.U32(net, o + 16):X8}, rig data size 0x{NmbFile.U64(net, o + 0x18):x}");
            Console.WriteLine($"  network header u32 at 0x118 = {NmbFile.U32(net, 0x118)}");
        }
        return 0;
    }

    // ------------------------------------------------------------------ nmb-roundtrip
    public static int NmbRoundtrip(string[] args)
    {
        var inputs = new List<string>();
        foreach (var p in Positional(args))
        {
            if (Directory.Exists(p))
                inputs.AddRange(Directory.GetFiles(p, "*", SearchOption.AllDirectories).Where(f => f.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".anibnd.dcx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".anibnd", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.Ordinal));
            else inputs.Add(p);
        }
        int files = 0, rebuilt = 0, full = 0, rigs = 0, rigOk = 0, maps = 0, mapOk = 0, tracks = 0, trackOk = 0;
        foreach (var path in inputs)
        {
            foreach (var (name, d) in LoadFiles(path).Where(f => f.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)))
            {
                files++;
                var nmb = NmbFile.Read(d);
                bool same = nmb.Rebuild().AsSpan().SequenceEqual(d);
                rebuilt += same ? 1 : 0;
                var repl = new Dictionary<int, byte[]>();
                var bad = new List<string>();
                for (int i = 0; i < nmb.Bundles.Count; i++)
                {
                    var b = nmb.Bundles[i];
                    byte[]? w = b.Type switch
                    {
                        1 => NmbRig.Read(b.Data).Write(),
                        2 => NmbChannelMap.Read(b.Data).Write(),
                        3 or 4 or 5 => NmbEventTrack.Read(b.Data, b.Type).Write(),
                        _ => null,
                    };
                    if (w == null) continue;
                    bool ok = w.AsSpan().SequenceEqual(b.Data);
                    if (b.Type == 1) { rigs++; rigOk += ok ? 1 : 0; }
                    else if (b.Type == 2) { maps++; mapOk += ok ? 1 : 0; }
                    else { tracks++; trackOk += ok ? 1 : 0; }
                    if (!ok) bad.Add($"bundle {i} type {b.Type}");
                    repl[i] = w;
                }
                bool fullSame = nmb.Rebuild(repl).AsSpan().SequenceEqual(d);
                full += fullSame ? 1 : 0;
                if (!same || !fullSame || bad.Count > 0)
                    Console.WriteLine($"  {Path.GetFileName(path)}/{name}: rebuild {(same ? "SAME" : "DIFF")}, all re-serialised {(fullSame ? "SAME" : "DIFF")} {string.Join(", ", bad.Take(5))}");
            }
        }
        Console.WriteLine($"NMB files {files}: bundle re-emit byte-identical {rebuilt}/{files}; with every rig/map/track re-serialised byte-identical {full}/{files}");
        Console.WriteLine($"  rigs write(read) identical {rigOk}/{rigs}, channel maps {mapOk}/{maps}, event tracks {trackOk}/{tracks}");
        return 0;
    }

    // ------------------------------------------------------------------ nmb-rig-replace
    public static int NmbRigReplaceCmd(string[] args)
    {
        var pos = Positional(args).ToList();
        if (pos.Count < 3) { Console.Error.WriteLine("usage: nmb-rig-replace TEMPLATE(.nmb|.anibnd.dcx) SKELETON.json OUT.nmb [--remap-by-name]"); return 1; }
        var (_, d) = LoadNmb(pos[0]);
        var nmb = NmbFile.Read(d);
        var skel = MorphemeSkeleton.Load(pos[1]);
        var log = new List<string>();
        if (args.Contains("--mapped"))
        {
            // session 8 (track chr step 2): the rebuild-style replacement used for the 1:1 DS1 characters (same bone count);
            // with the template's own skeleton it must reproduce the template (self-check)
            var tmplRig = NmbRig.Read(nmb.Bundles[nmb.IndexOf(1)].Data);
            var match = NmbRigReplace.MatchByName(tmplRig.Names, skel.Names, Build.Chr.Ds1RigAnims.Ds2ToDs1Fallback);
            byte[] mapped = NmbRigReplace.ReplaceMapped(nmb, skel, match, log);
            File.WriteAllBytes(pos[2], mapped);
            foreach (var l in log) Console.WriteLine(l);
            int diff = 0, first = -1;
            for (int i = 0; i < Math.Min(mapped.Length, d.Length); i++) if (mapped[i] != d[i]) { diff++; if (first < 0) first = i; }
            Console.WriteLine($"wrote {pos[2]} ({mapped.Length} bytes); vs template: {(mapped.AsSpan().SequenceEqual(d) ? "byte-identical" : $"{diff} bytes differ (first 0x{first:X}), length {mapped.Length}/{d.Length}")}");
            return 0;
        }
        List<(int, int)>? remap = null;
        if (args.Contains("--remap-by-name"))
        {
            var tmpl = NmbRig.Read(nmb.Bundles[nmb.IndexOf(1)].Data);
            var (rm, unmatched) = NmbRigReplace.RemapByName(tmpl, skel);
            remap = rm;
            Console.WriteLine($"template rig {tmpl.Names.Count} bones, new rig {skel.Names.Count} bones, remapped {rm.Count}; template bones without a match: [{string.Join(", ", unmatched)}]");
        }
        byte[] outBytes = NmbRigReplace.Replace(nmb, skel, remap, log, out _);
        File.WriteAllBytes(pos[2], outBytes);
        foreach (var l in log) Console.WriteLine(l);
        var again = NmbFile.Read(outBytes);
        Console.WriteLine($"wrote {pos[2]} ({outBytes.Length} bytes); re-parse: {again.Bundles.Count} bundles, rig {NmbRig.Read(again.Bundles[again.IndexOf(1)].Data).Names.Count} bones, round trip {(again.Rebuild().AsSpan().SequenceEqual(outBytes) ? "identical" : "DIFFERENT")}");
        return 0;
    }

    // ------------------------------------------------------------------ ds1-anim-to-nsa
    public static int Ds1AnimToNsa(string[] args)
    {
        var pos = Positional(args, "--fps", "--nmb").ToList();
        if (pos.Count < 3) { Console.Error.WriteLine("usage: ds1-anim-to-nsa SKELETON.hkx ANIM.hkx... OUTDIR [--fps F] [--no-root-motion] [--samples-json] [--nmb TEMPLATE]"); return 1; }
        string skelPath = pos[0], outDir = pos[^1];
        var anims = pos.Skip(1).Take(pos.Count - 2).ToList();
        double? fps = Opt(args, "--fps") is { Length: > 0 } fs ? double.Parse(fs, Inv) : null;
        bool rootMotion = !args.Contains("--no-root-motion");
        Directory.CreateDirectory(outDir);
        var sk = Ds1AnimBridge.ReadSkeleton(new HkTagfile(ReadHkx(skelPath)));
        MorphemeSkeleton? skel = null;
        foreach (var ap in anims)
        {
            var tag = new HkTagfile(ReadHkx(ap));
            var samples = Ds1AnimBridge.Sample(sk, tag);
            var res = Ds1AnimBridge.Bridge(samples, fps, rootMotion);
            skel = res.Skeleton;
            string stem = Path.GetFileNameWithoutExtension(ap);
            if (args.Contains("--samples-json")) File.WriteAllText(Path.Combine(outDir, stem + ".samples.json"), Ds1AnimBridge.SamplesJson(samples));
            var nf = NsaEncoder.EncodeFile(res.Anim);
            byte[] data = nf.Build();
            File.WriteAllBytes(Path.Combine(outDir, stem + ".nsa"), data);
            // self check: decode and compare with the input samples
            var back = NsaCodec.Decode(NsaFile.Parse(data));
            var (ep, er, rp, rr) = NsaCodec.Compare(res.Anim, back);
            Console.WriteLine($"{stem}: {res.Ds1Bones} DS1 bones ({res.Tracks} tracks, {res.MissingTracks} bones on the reference pose) -> rig {res.Skeleton.Names.Count} bones / {res.Anim.ChannelCount} channels, "
                + $"{res.Frames} frames @ {F(res.Fps, "0.####")} fps, {data.Length} bytes NSA (static T/R {nf.StaticT.Count}/{nf.StaticR.Count}, dynamic T/R {nf.DynT.Count}/{nf.DynR.Count})");
            Console.WriteLine($"  root motion: {(res.HasRootMotion ? $"end ({F(res.RootEnd.X, "0.###")}, {F(res.RootEnd.Y, "0.###")}, {F(res.RootEnd.Z, "0.###")}) m, yaw {F(res.RootEndYawDeg, "0.#")} deg; trajectory pos {(nf.Root!.Pos == null ? "constant" : "animated")}, rot {(nf.Root.Rot == null ? "constant" : "animated")}" : "none")}");
            Console.WriteLine($"  re-decoded vs input: max pos err {ep * 1000:0.###} mm, rot {er:0.####} deg, trajectory {rp * 1000:0.###} mm / {rr:0.####} deg");
        }
        if (skel != null)
        {
            skel.Save(Path.Combine(outDir, "skeleton.json"));
            Console.WriteLine($"skeleton.json: {skel.Names.Count} rig bones");
            string tmplPath = Opt(args, "--nmb");
            if (tmplPath != "")
            {
                var (tn, td) = LoadNmb(tmplPath);
                var nmb = NmbFile.Read(td);
                var tmpl = NmbRig.Read(nmb.Bundles[nmb.IndexOf(1)].Data);
                var (rm, unmatched) = NmbRigReplace.RemapByName(tmpl, skel);
                var log = new List<string>();
                byte[] outBytes = NmbRigReplace.Replace(nmb, skel, rm, log, out _);
                string outNmb = Path.Combine(outDir, Path.GetFileNameWithoutExtension(tn) + "_ds1rig.nmb");
                File.WriteAllBytes(outNmb, outBytes);
                Console.WriteLine($"{Path.GetFileName(outNmb)}: template {tmpl.Names.Count} bones, remapped {rm.Count}, unmatched [{string.Join(", ", unmatched)}]");
                foreach (var l in log) Console.WriteLine("  " + l);
            }
        }
        return 0;
    }

    // ------------------------------------------------------------------ anibnd-reencode
    /// <summary><c>anibnd-reencode SRC.anibnd.dcx OUT.anibnd.dcx [--mode fresh|template|none] [--only SUBSTR]</c>: the
    /// same binder with every NSA (or those whose name contains SUBSTR) re-encoded by our encoder (fresh = own
    /// quantisation, template = the clip's own settings, none = repack only, the control). In-game test of the encoder's
    /// output: DS2 loads the anibnd by the EnemyParam's animId (c1030 for the ported Asylum Hollows, run s5-26).</summary>
    public static int AnibndReencode(string[] args)
    {
        var pos = Positional(args, "--mode", "--only").ToList();
        if (pos.Count < 2) { Console.Error.WriteLine("usage: anibnd-reencode SRC.anibnd.dcx OUT.anibnd.dcx [--mode fresh|template|none] [--only SUBSTR]"); return 1; }
        string mode = Opt(args, "--mode") is { Length: > 0 } m ? m : "fresh", only = Opt(args, "--only");
        byte[] raw = File.ReadAllBytes(pos[0]);
        DCX.CompressionInfo? comp = null;
        if (DCX.Is(raw)) raw = DCX.Decompress(raw, out comp);
        var bnd = BND4.Read(raw);
        int n = 0; double ep = 0, er = 0;
        foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".nsa", StringComparison.OrdinalIgnoreCase)))
        {
            if (mode == "none" || (only != "" && !f.Name.Contains(only, StringComparison.OrdinalIgnoreCase))) continue;
            var nsa = NsaFile.Parse(f.Bytes);
            var clip = NsaCodec.Decode(nsa);
            byte[] enc = NsaEncoder.Encode(clip, mode == "template" ? nsa : null);
            var (a, b, _, _) = NsaCodec.Compare(clip, NsaCodec.Decode(NsaFile.Parse(enc)));
            ep = Math.Max(ep, a); er = Math.Max(er, b);
            f.Bytes = enc; n++;
        }
        byte[] outBytes = bnd.Write();
        if (comp != null) outBytes = DCX.Compress(outBytes, comp);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pos[1]))!);
        File.WriteAllBytes(pos[1], outBytes);
        // check: the written binder re-reads with the same file list and contents
        var back = BND4.Read(DCX.Is(outBytes) ? DCX.Decompress(outBytes) : outBytes);
        bool same = back.Files.Count == bnd.Files.Count && back.Files.Zip(bnd.Files).All(z => z.First.Name == z.Second.Name && z.First.ID == z.Second.ID && z.First.Bytes.AsSpan().SequenceEqual(z.Second.Bytes));
        Console.WriteLine($"{pos[1]}: {bnd.Files.Count} files, {n} clips re-encoded ({mode}), max decoded change {ep * 1000:0.###} mm / {er:0.####} deg, {outBytes.Length} bytes{(comp != null ? $" ({comp.Type})" : "")}; re-read {(same ? "OK" : "MISMATCH")}");
        return 0;
    }

    static byte[] ReadHkx(string path)
    {
        var b = File.ReadAllBytes(path);
        return DCX.Is(b) ? DCX.Decompress(b) : b;
    }
}
