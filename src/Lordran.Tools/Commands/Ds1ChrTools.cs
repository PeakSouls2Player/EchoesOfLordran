using System.Globalization;
using System.Numerics;
using Lordran.Tools.Build.Chr;
using Lordran.Tools.Formats.Havok;
using Lordran.Tools.Formats.Morpheme;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Commands for the 1:1 DS1 character pipeline (session 8, track chr step 2; see Build/Chr/Ds1Rig*.cs):
/// <list type="bullet">
/// <item><c>clip-survey SRC [--ds1 SKELETON] [--filter A00_]</c>: per clip of a DS1 anibnd (DSR chr/cNNNN.anibnd.dcx) or a
/// DS2 anibnd: frames, duration, root motion (end position in Morpheme space, yaw), character-root / pelvis / head
/// heights at the start and end, loop closure (largest bone-rotation change from the first to the last frame) and the
/// stance-foot speed with the trajectory applied. The evidence for the DS1 → DS2 clip role table.</item>
/// <item><c>clip-vs-bind</c>, <c>param-row</c>, <c>tae-roundtrip</c>, <c>steps-calib</c>: evidence lookups (see each method).</item>
/// <item><c>ds1rig-build OUTDIR</c> / <c>ds1rig-check OUTDIR [-v] [--pack DIR]</c>: the 1:1 unarmed Asylum Hollow without a pack,
/// and its validation (rig, channel map, clips, SourceAnim attribs, FLVER vs rig, ragdoll, per-clip foot planting vs the
/// DS1 source, TAEs, a built pack's files and EnemyParam rows).</item>
/// <item><c>pose-view MODEL.bnd ANIBND CLIP OUT.png</c>: the model skinned by a clip the way DS2 binds it, rendered.</item>
/// </list>
/// </summary>
public static class Ds1ChrTools
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static string F(double v, string f = "0.###") => v.ToString(f, Inv);

    public sealed record ClipStats(string Name, int Frames, double Fps, double Duration, V3 RootEnd, double YawDeg,
        double RootZ0, double RootZ1, double PelvisZ1, double HeadZ0, double HeadZ1, double MinFootZ, double LoopDeg, double StanceSpeed, double FootDrift)
    {
        public V3 HeadEnd, HeadLean; public double FaceZ;
    }

    /// <summary>Stats of one decoded clip on its rig (names: character root, pelvis, head, L/R foot).</summary>
    public static ClipStats Stats(string name, NsaAnim a, MorphemePose pose, string root, string pelvis, string head, string lfoot, string rfoot)
    {
        int ri = pose.Index(root), pi = pose.Index(pelvis), hi = pose.Index(head), li = pose.Index(lfoot), rf = pose.Index(rfoot);
        int nf = a.Frames;
        var w0 = pose.World(a, 0, false); var w1 = pose.World(a, nf - 1, false);
        double loop = 0;
        for (int c = 2; c < a.ChannelCount; c++) loop = Math.Max(loop, MorphemeMath.AngleDeg(a.Rot[c][0], a.Rot[c][nf - 1]));
        double minFoot = double.MaxValue, stance = 0, drift = 0;
        V3? pl = null, pr = null;
        for (int f = 0; f < nf; f++)
        {
            var w = pose.World(a, f, true);
            minFoot = Math.Min(minFoot, Math.Min(w.Pos[li].Z, w.Pos[rf].Z));
            if (pl is { } l && pr is { } r)
            {
                double sl = MorphemePose.Dist(l, w.Pos[li]) * a.Fps, sr = MorphemePose.Dist(r, w.Pos[rf]) * a.Fps;
                stance += Math.Min(sl, sr);
            }
            pl = w.Pos[li]; pr = w.Pos[rf];
        }
        // drift of the lower foot between the first and last frame without trajectory (in-place clips: should be ~0)
        drift = Math.Min(MorphemePose.Dist(w0.Pos[li], w1.Pos[li]), MorphemePose.Dist(w0.Pos[rf], w1.Pos[rf]));
        var q = a.RootRot[^1];
        double yaw = 2 * Math.Atan2(q.Z, q.W) * 180 / Math.PI;
        // head: end offset from the start (trajectory applied), largest horizontal lean, and where the face points at the end
        var h0 = pose.World(a, 0, true); var h1 = pose.World(a, nf - 1, true);
        V3 lean = new(0, 0, 0); double best = -1;
        for (int f = 0; f < nf; f++)
        {
            var w = pose.World(a, f, false);
            double dx = w.Pos[hi].X - w0.Pos[hi].X, dy = w.Pos[hi].Y - w0.Pos[hi].Y;
            if (dx * dx + dy * dy > best) { best = dx * dx + dy * dy; lean = new V3(dx, dy, w.Pos[hi].Z - w0.Pos[hi].Z); }
        }
        // face direction: the bind-pose forward (-Y) carried by the head's rotation change since frame 0
        var dq = MorphemeMath.Mul(h1.Rot[hi], MorphemeMath.Conj(h0.Rot[hi]));
        var face = MorphemeMath.Rotate(dq, new V3(0, -1, 0));
        return new ClipStats(name, nf, a.Fps, (nf - 1) / a.Fps, a.RootPos[^1], yaw, w0.Pos[ri].Z, w1.Pos[ri].Z, w1.Pos[pi].Z, w0.Pos[hi].Z, w1.Pos[hi].Z,
            minFoot, loop, nf > 1 ? stance / (nf - 1) : 0, drift)
        { HeadEnd = new V3(h1.Pos[hi].X - h0.Pos[hi].X, h1.Pos[hi].Y - h0.Pos[hi].Y, h1.Pos[hi].Z - h0.Pos[hi].Z), HeadLean = lean, FaceZ = face.Z };
    }

    public static string Line(ClipStats s) =>
        $"{s.Name,-34} {s.Frames,4}f {F(s.Fps, "0.##"),5}fps {F(s.Duration, "0.000"),6}s root({F(s.RootEnd.X, "0.00"),6},{F(s.RootEnd.Y, "0.00"),6},{F(s.RootEnd.Z, "0.00"),6}) yaw {F(s.YawDeg, "0"),5} "
        + $"rootZ {F(s.RootZ0, "0.00")}->{F(s.RootZ1, "0.00")} pelvisZ1 {F(s.PelvisZ1, "0.00")} headZ {F(s.HeadZ0, "0.00")}->{F(s.HeadZ1, "0.00")} footZmin {F(s.MinFootZ, "0.00")} loop {F(s.LoopDeg, "0")}deg stance {F(s.StanceSpeed, "0.00")}m/s drift {F(s.FootDrift, "0.000")}"
        + $" headEnd({F(s.HeadEnd.X, "0.00")},{F(s.HeadEnd.Y, "0.00")}) lean({F(s.HeadLean.X, "0.00")},{F(s.HeadLean.Y, "0.00")},{F(s.HeadLean.Z, "0.00")}) faceZ {F(s.FaceZ, "0.00")}";

    /// <summary><c>param-row dsr|ds2 PARAM ID...</c>: every non-zero field of the rows (evidence lookups, e.g. the DS1
    /// SpEffect a TAE event adds).</summary>
    public static int ParamRow(string[] args)
    {
        var store = args[1].ToLowerInvariant() == "dsr" ? Formats.ParamStore.LoadDsr(Program.DefaultDsrRoot) : Formats.ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        foreach (int id in args.Skip(3).Select(int.Parse))
        {
            var row = store.Params[args[2]][id];
            if (row == null) { Console.WriteLine($"{id}: no row"); continue; }
            Console.WriteLine($"{id} {store.RowName(args[2], id)}: " + string.Join(" ", row.Cells.Where(c => Convert.ToString(c.Value, Inv) is not ("0" or "-1" or "")).Select(c => $"{c.Def.InternalName}={(c.Value is byte[] b ? Convert.ToHexString(b) : Convert.ToString(c.Value, Inv))}")));
        }
        return 0;
    }

    /// <summary><c>clip-vs-bind ANIBND CLIP [OTHER]</c>: per channel, the largest rotation difference of CLIP's frames from
    /// the rig bind pose (and from OTHER's frame 0): tells full-pose clips from bind-relative (additive) ones.</summary>
    public static int ClipVsBind(string[] args)
    {
        var files = MorphemeTools.LoadFiles(args[1]);
        var nmb = NmbFile.Read(files.First(f => f.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)).Bytes);
        var rig = NmbRig.Read(nmb.Bundles[nmb.IndexOf(1)].Data);
        var map = NmbChannelMap.Read(nmb.Bundles[nmb.IndexOf(2)].Data);
        NsaAnim Clip(string sub) => NsaCodec.Decode(NsaFile.Parse(files.First(f => f.Name.Contains(sub, StringComparison.OrdinalIgnoreCase) && f.Name.EndsWith(".nsa")).Bytes));
        var a = Clip(args[2]);
        var o = args.Length > 3 ? Clip(args[3]) : null;
        double sumB = 0, sumO = 0;
        for (int c = 0; c < a.ChannelCount; c++)
        {
            int b = map.AnimToRig[c];
            var bq = new Q4(rig.BindRot[b][3], rig.BindRot[b][0], rig.BindRot[b][1], rig.BindRot[b][2]);
            double db = 0, dO = 0;
            for (int f = 0; f < a.Frames; f++)
            {
                db = Math.Max(db, MorphemeMath.AngleDeg(a.Rot[c][f], bq));
                if (o != null) dO = Math.Max(dO, MorphemeMath.AngleDeg(a.Rot[c][f], o.Rot[c][0]));
            }
            sumB += db; sumO += dO;
            Console.WriteLine($"  ch {c,2} {rig.Names[b],-16} vs bind {F(db, "0.0"),6} deg{(o != null ? $"  vs other f0 {F(dO, "0.0"),6} deg" : "")}  t=({F(a.Pos[c][0].X)},{F(a.Pos[c][0].Y)},{F(a.Pos[c][0].Z)}) bindT=({F(rig.BindPos[b][0])},{F(rig.BindPos[b][1])},{F(rig.BindPos[b][2])})");
        }
        Console.WriteLine($"mean max angle vs bind {F(sumB / a.ChannelCount, "0.0")} deg{(o != null ? $", vs other {F(sumO / a.ChannelCount, "0.0")} deg" : "")}");
        return 0;
    }

    /// <summary><c>tae-roundtrip FILE...</c>: SoulsFormats TAE read → write byte comparison (DS2 SOTFS TAEs are written by
    /// the 1:1 pipeline, so the writer has to be exact on vanilla files first).</summary>
    public static int TaeRoundtrip(string[] args)
    {
        int ok = 0, n = 0;
        foreach (var path in args.Skip(1).SelectMany(p => Directory.Exists(p) ? Directory.GetFiles(p, "*.tae") : [p]))
        {
            n++;
            byte[] a = File.ReadAllBytes(path);
            try
            {
                byte[] b = TAE.Read(a).Write();
                bool same = a.AsSpan().SequenceEqual(b);
                ok += same ? 1 : 0;
                if (!same)
                {
                    int i = 0; while (i < Math.Min(a.Length, b.Length) && a[i] == b[i]) i++;
                    Console.WriteLine($"  {Path.GetFileName(path)}: DIFF (len {a.Length}/{b.Length}, first diff 0x{i:X})");
                }
            }
            catch (Exception e) { Console.WriteLine($"  {Path.GetFileName(path)}: {e.GetType().Name} {e.Message}"); }
        }
        Console.WriteLine($"TAE round trip byte-identical {ok}/{n}");
        return 0;
    }

    /// <summary><c>ds1rig-build OUTDIR</c>: builds the 1:1 unarmed Asylum Hollow (ChrPorter.AsylumHollow.Rig) without a
    /// pack: every file under OUTDIR/gamedata/..., the build report (report.txt) and the clip role table (roles.md).</summary>
    public static int Ds1RigBuild(string[] args)
    {
        string outDir = args.Length > 1 ? args[1] : throw new ArgumentException("usage: ds1rig-build OUTDIR [--chr c2501|c2502|c2541|c2542]");
        string key = args.SkipWhile(a => a != "--chr").Skip(1).FirstOrDefault() ?? "c2501";
        var (o, rig) = Build.Chr.ChrPorter.FindRig(key);
        // a build sharing another one's model still writes it here, so the standalone checks see the model it is drawn with
        var r = Build.Chr.Ds1RigChr.Build(Program.DefaultDsrRoot, ExtractDs2.DefaultOut, o, rig.OwnModel ? rig : rig with { OwnModel = true });
        foreach (var (device, files) in new[] { ("gamedata", r.Files), ("title", r.TitleFiles) })
            foreach (var (path, bytes) in files)
            {
                string p = Path.Combine(outDir, device, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllBytes(p, bytes);
                Console.WriteLine($"  {device}/{path} ({bytes.Length:N0} bytes)");
            }
        File.WriteAllLines(Path.Combine(outDir, "report.txt"), r.Report);
        // track chrsound: per TAE animation, the DS1 sound / effect events written and those not played
        File.WriteAllLines(Path.Combine(outDir, "sound.txt"), r.SoundLog);
        var rows = new List<string>
        {
            "| DS2 slot (c1030 NSA) | DS2 TAE id(s) | DS2 s | DS2 root end | DS1 clip | DS1 s | DS1 root end | role | status | DS1 TAE events |",
            "|---|---|---|---|---|---|---|---|---|---|",
        };
        rows.AddRange(Build.Chr.Ds1RigAnims.TableRows(r.Anims));
        File.WriteAllLines(Path.Combine(outDir, "roles.md"), rows);
        File.WriteAllLines(Path.Combine(outDir, "steps.txt"), r.Anims.Clips.OrderBy(c => c.Name, StringComparer.Ordinal).Select(c => $"{c.Name} <- {(c.Ds1Id == -1 ? "bind" : Build.Chr.Ds1ChrSource.ClipName(c.Ds1Id))}: {c.Steps}"));
        // fix pass: every event track each slot rewrote, DS2's events → ours (rules C11/C12)
        File.WriteAllLines(Path.Combine(outDir, "tracks.txt"), r.Anims.Clips.OrderBy(c => c.Name, StringComparer.Ordinal).SelectMany(c =>
            new[] { $"{c.Name} <- {(c.Ds1Id == -1 ? "bind" : Build.Chr.Ds1ChrSource.ClipName(c.Ds1Id))} (DS1 cancel {F(c.Cancel, "0.###")}, death {F(c.DeathState, "0.###")}, no-gravity {string.Join(" ", c.NoGravity.Select(w => $"{F(w.S, "0.###")}-{F(w.E, "0.###")}"))}{(c.NoAttack ? ", no attack" : "")})" }
            .Concat(c.TrackLog.Order(StringComparer.Ordinal).Select(t => "    " + t))));
        r.Anims.Rig.Save(Path.Combine(outDir, "skeleton.json"));
        File.WriteAllLines(Path.Combine(outDir, "chr.txt"), [$"{key} {o.Ds1Chr} model c{rig.Model:D4} animId {rig.AnimId}"]);
        // track enemies: the DS1 attack layer (categories, clips, every rewritten track, the TimeAct events)
        if (r.Anims.Attacks is { } at)
        {
            var lines = new List<string> { $"# DS1 attacks of {key} ({o.Ds1Chr}, animIdOffset {rig.Attacks!.AnimOffset})", "" };
            foreach (var c in at.Categories) lines.Add($"category {c.Value}: SM {c.Sm} {c.What}: stages {string.Join(" ", c.Stages.Select(st => $"leaf {st.Leaf} = {Build.Chr.Ds1ChrSource.ClipName(st.Clip)}"))}");
            lines.Add("");
            foreach (var co in at.Clips.Values.OrderBy(c => c.Clip))
            {
                var t = co.Timing;
                lines.Add($"{Build.Chr.Ds1ChrSource.ClipName(co.Clip)} → {co.Name} (clip {co.Index}, TAE {co.TaeId}, {t.Duration:0.###} s, hit slot {co.HitIndex}): hits [{string.Join(" ", t.Hits.Select(h => $"{h.S:0.###}-{h.E:0.###} j{h.Judge}"))}] bullets [{string.Join(" ", t.Bullets.Select(b => $"{b.S:0.###} dmy{b.Dummy} j{b.Judge}"))}] continuation [{string.Join(" ", t.Continuation.Select(w => $"{w.S:0.###}-{w.E:0.###}"))}] cancels move {t.MoveCancel:0.###} dodge {t.DodgeCancel:0.###} attack {t.AttackCancel:0.###}; no-turn [{string.Join(" ", t.NoTurn.Select(w => $"{w.S:0.###}-{w.E:0.###}"))}]");
                foreach (var tl in co.TrackLog) lines.Add("    " + tl);
                var ta = at.TaeAnims.First(a => a.Id == co.TaeId);
                lines.Add($"    TAE {co.TaeId} ({ta.Frames} frames): " + string.Join(" ", ta.Events.Select(e => $"{e.Type}@{e.Start:0.###}-{e.End:0.###}")));
            }
            File.WriteAllLines(Path.Combine(outDir, "attacks.txt"), lines);
        }
        foreach (var l in r.Report) Console.WriteLine(l);
        return 0;
    }

    /// <summary>
    /// <c>ds1rig-check OUTDIR</c>: validation of a ds1rig-build output.
    /// <list type="number">
    /// <item>anibnd: rig bone count / names = skeleton.json, channel map = bone c+1, every NSA decodes with 54 channels,
    /// every anim node's SourceAnim duration and trajectory end equal its NSA, TimeAct ids;</item>
    /// <item>FLVER (.flv in the bnd): loads with SoulsFormats, nodes 0..53 = rig bones 1..54 by name and parent, mesh palettes
    /// only on rig bones, flvpwv node count;</item>
    /// <item>ragdoll: animation skeleton = rig minus CharacterWorldSpaceTM, mapper indices in range; every constraint's two
    /// entities set and among its ragdoll instance's / physics system's bodies, the instance's ragdoll constraints with their
    /// 3 motors, cone limits at DS2's runtime offset 56, DS2 npData/dispatchType (session 8: the s8-6 crash);</item>
    /// <item>per clip: the decoded output NSA vs the DS1 source bridged at full precision (no quantisation): largest L/R
    /// foot world-position difference (root motion applied) over all frames; locomotion loops against the source started
    /// at its left touchdown, and that phase shift checked independently (ball paths = the original cycle re-based);</item>
    /// <item>event tracks as built (fix pass, session 8), read back from the NMB: every sync track partitions [0,1]; in
    /// every blend with a locomotion loop the children have equal sync counts and sync event k is the same foot's
    /// touchdown (measured on the NSA); every foot-lock window keeps the ball joint within the lock tolerance; no-attack
    /// slots carry no attack-family event and return to base at once; DamageDuration / return to base / DieControl follow
    /// the DS1 TAE flags;</item>
    /// <item>TAE: SoulsFormats reads the edited files; lengths = DS1 frames at 30 fps.</item>
    /// </list>
    /// </summary>
    public static int Ds1RigCheck(string[] args)
    {
        string dir = args[1];
        string gd = Path.Combine(dir, "gamedata");
        var skel = MorphemeSkeleton.Load(Path.Combine(dir, "skeleton.json"));
        int fails = 0;
        void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {what}"); if (!ok) fails++; }
        // track enemies (session 8): which 1:1 character this output is (chr.txt by ds1rig-build; older outputs: the unarmed Hollow)
        string key = File.Exists(Path.Combine(dir, "chr.txt")) ? File.ReadAllLines(Path.Combine(dir, "chr.txt"))[0].Split(' ')[0] : "c2501";
        var (chrO, chrRig) = Build.Chr.ChrPorter.FindRig(key);
        Console.WriteLine($"{key}: DS1 {chrO.Ds1Chr}, model c{chrRig.Model:D4}, animId {chrRig.AnimId}{(chrRig.Attacks != null ? $", DS1 attacks (animIdOffset {chrRig.Attacks.AnimOffset})" : "")}");
        // ---- 1. anibnd
        string anibnd = Directory.GetFiles(Path.Combine(gd, "morpheme4", "chr"), "*.anibnd.dcx").Single();
        var files = MorphemeTools.LoadFiles(anibnd);
        var nmbBytes = files.First(f => f.Name.EndsWith(".nmb")).Bytes;
        var nmb = NmbFile.Read(nmbBytes);
        var rig = NmbRig.Read(nmb.Bundles[nmb.IndexOf(1)].Data);
        var map = NmbChannelMap.Read(nmb.Bundles[nmb.IndexOf(2)].Data);
        Console.WriteLine($"{Path.GetFileName(anibnd)}: {files.Count} files, NMB {nmbBytes.Length} bytes, {nmb.Bundles.Count} bundles");
        Check(rig.Names.SequenceEqual(skel.Names) && rig.Parents.SequenceEqual(skel.Parents), $"rig = DS1 rig ({rig.Names.Count} bones, trajectory {rig.Traj} {rig.Names[rig.Traj]}, root {rig.Root} {rig.Names[rig.Root]})");
        Check(map.AnimToRig.Count == rig.Names.Count - 1 && map.AnimToRig.Select((v, c) => v == c + 1).All(x => x), $"channel map: {map.AnimToRig.Count} channels, channel c = rig bone c+1");
        var net = new NmbNetwork(nmb.Bundles[nmb.IndexOf(10)].Data);
        Check(net.U32(0x118) == rig.Names.Count, $"network header bone count {net.U32(0x118)}");
        Check(nmb.Rebuild().AsSpan().SequenceEqual(nmbBytes), "NMB bundles re-emit byte-identically");
        // session 9 (track enemyfix): the bounds / invariants the DS2 runtime relies on (0 on all 173 vanilla networks)
        var rc = NmbRuntimeCheck.Check(nmbBytes, Path.GetFileName(anibnd), 2);
        Check(rc.Violations == 0, $"runtime check (nmb-runtime-check, {NmbRuntimeCheck.Rules.Length} rules): {rc.Violations} violations"
            + (rc.Violations > 0 ? " - " + string.Join("; ", rc.Samples.Where(kv => !kv.Key.StartsWith('W')).SelectMany(kv => kv.Value.Select(s => $"{kv.Key} {s}")).Take(4)) : ""));
        var fn = NmbFileNames.Read(nmb.Bundles[nmb.IndexOf(13)].Data).Anims;
        var clips = new Dictionary<string, NsaAnim>();
        int decoded = 0;
        foreach (var (name, b) in files.Where(f => f.Name.EndsWith(".nsa")))
        {
            try { var a = NsaCodec.Decode(NsaFile.Parse(b)); if (a.ChannelCount == rig.Names.Count - 1) decoded++; clips[name] = a; }
            catch (Exception e) { Console.WriteLine($"    {name}: {e.Message}"); }
        }
        Check(decoded == clips.Count && clips.Count == files.Count - 1, $"every NSA decodes with {rig.Names.Count - 1} channels: {decoded}/{files.Count - 1}");
        int nodes = 0, durOk = 0, trajOk = 0;
        foreach (var node in net.Nodes().Where(x => x.Type == 104))
        {
            var sa = NmbNetwork.Find(node, 21); if (sa == null) continue;
            int ci = net.ClipIndex(sa);
            string nm = fn[ci];
            var a = clips.FirstOrDefault(kv => kv.Key.Equals(nm, StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(kv.Key) == nm).Value;
            if (a == null) continue;
            nodes++;
            double d = (a.Frames - 1) / a.Fps;
            if (Math.Abs(net.ClipDuration(sa) - d) < 1e-4) durOk++;
            var e = a.RootPos[^1];
            if (Math.Abs(net.F32(sa.Offset + 0x40) - e.X) < 1e-4 && Math.Abs(net.F32(sa.Offset + 0x44) - e.Y) < 1e-4 && Math.Abs(net.F32(sa.Offset + 0x48) - e.Z) < 1e-4) trajOk++;
        }
        Check(durOk == nodes, $"SourceAnim durations = NSA durations on {durOk}/{nodes} anim nodes");
        Check(trajOk == nodes, $"SourceAnim trajectory ends = NSA trajectory ends on {trajOk}/{nodes} anim nodes");
        // ---- 2. FLVER
        string bndPath = Directory.GetFiles(Path.Combine(gd, "model", "chr"), "*.bnd").Single();
        var bnd = BND4.Read(bndPath);
        var flv = FLVER2.Read(bnd.Files.First(f => f.Name.EndsWith(".flv") && !f.Name.EndsWith("_s.flv")).Bytes);
        var rigBones = skel.Names.Skip(1).ToList();
        bool namesOk = flv.Nodes.Take(rigBones.Count).Select(x => x.Name).SequenceEqual(rigBones);
        bool parentsOk = Enumerable.Range(0, rigBones.Count).All(i => flv.Nodes[i].ParentIndex == skel.Parents[i + 1] - 1);
        Check(namesOk && parentsOk, $"FLVER {flv.Nodes.Count} nodes: nodes 0..{rigBones.Count - 1} = rig bones 1..{rigBones.Count} (names {(namesOk ? "equal" : "DIFFER")}, parents {(parentsOk ? "equal" : "DIFFER")})");
        Check(flv.Meshes.All(m => m.BoneIndices.All(b => b >= 0 && b < rigBones.Count)), $"{flv.Meshes.Count} meshes skinned only to rig bones ({flv.Meshes.Sum(m => m.Vertices.Count)} vertices)");
        var w = ChrCompare.World(flv);
        var rp = MorphemePose.From(skel).BindWorld();
        double maxd = 0; string worst = "";
        // DS1 FLVER nodes flagged Disabled (nubs, c2900's last finger joints) carry no skin in DS1; Ds1RigModel keeps their DS1
        // local transform under the rig parent (rule C2), which may differ from the Havok reference pose: excluded, listed
        var ds1Flv0 = FLVER2.Read(BND3.Read(Path.Combine(Program.DefaultDsrRoot, "chr", chrO.Ds1Chr + ".chrbnd.dcx")).Files.First(f => f.Name.EndsWith(".flver")).Bytes);
        var disabled = ds1Flv0.Nodes.Where(nd => nd.Flags.HasFlag(FLVER.Node.NodeFlags.Disabled)).Select(nd => Build.Chr.Ds1RigAnims.Ds2BoneName(nd.Name.Trim())).ToHashSet();
        var skinned = flv.Meshes.SelectMany(m => m.BoneIndices).Distinct().Select(b => flv.Nodes[b].Name).ToHashSet();
        var disabledOff = new List<string>();
        for (int i = 0; i < rigBones.Count; i++)
        {
            var r3 = rp[i + 1];
            double dd = System.Numerics.Vector3.Distance(w[i].Translation, new System.Numerics.Vector3((float)r3.X, (float)r3.Z, (float)r3.Y));
            if (disabled.Contains(rigBones[i]) && !rigBones[i].EndsWith("Nub")) { if (dd > 0.005) disabledOff.Add($"{rigBones[i]} {dd * 1000:0.0} mm{(skinned.Contains(rigBones[i]) ? " SKINNED" : "")}"); continue; }
            if (dd > maxd && !rigBones[i].EndsWith("Nub")) { maxd = dd; worst = rigBones[i]; }
        }
        // session 11 (burgchr11): a DS1 model whose own FLVER bind is off its Havok reference pose (c2520's R_weapon: 19.5 mm in DS1's
        // data) keeps DS1's FLVER bind (Ds1RigModel: DS1 skins with its FLVER bind, animates with the Havok pose): then the check is
        // that our nodes ARE DS1's FLVER bind
        var w0 = ChrCompare.World(ds1Flv0);
        double maxOwn = 0;
        for (int i = 0; i < rigBones.Count; i++)
        {
            int d0 = ds1Flv0.Nodes.FindIndex(nd => Build.Chr.Ds1RigAnims.Ds2BoneName(nd.Name.Trim()) == rigBones[i]);
            if (d0 < 0 || disabled.Contains(rigBones[i]) || rigBones[i].EndsWith("Nub")) continue;
            maxOwn = Math.Max(maxOwn, System.Numerics.Vector3.Distance(w[i].Translation, w0[d0].Translation));
        }
        Check((maxd < 0.005 || maxOwn < 1e-4) && !disabledOff.Any(x => x.EndsWith("SKINNED")), $"FLVER bind = DS1's FLVER bind (max {maxOwn * 1000:0.000} mm); vs rig bind (Havok reference pose) max {maxd * 1000:0.00} mm ({worst}; nubs excluded){(disabledOff.Count > 0 ? $"; DS1-disabled skinless nodes off the reference pose (DS1's own FLVER bind kept): {string.Join(", ", disabledOff)}" : "")}");
        var pwv = bnd.Files.First(f => f.Name.EndsWith(".flvpwv")).Bytes;
        Check(BitConverter.ToUInt16(pwv, 6) == flv.Nodes.Count && pwv.Length == 20 + 148 * flv.Nodes.Count, $"flvpwv: {BitConverter.ToUInt16(pwv, 6)} records for {flv.Nodes.Count} nodes");
        // ---- 3. ragdoll
        var hk = new Formats.Havok.HkPackfile(bnd.Files.First(f => f.Name.EndsWith("_Ragdoll.hkx")).Bytes);
        var sk = hk.Objects.Where(x => x.Class == "hkaSkeleton").Select(x => hk.ReadObject(x.Offset)).ToList();
        var anim = sk.First(s => ((System.Collections.IEnumerable)s["bones"]!).Cast<Formats.Havok.HkPObj>().Any(b => b["name"] as string == "Pelvis"));
        var an = anim.List("bones").Cast<Formats.Havok.HkPObj>().Select(b => b["name"] as string).ToList();
        Check(an.SequenceEqual(rigBones), $"ragdoll animation skeleton '{anim["name"]}' = rig minus CharacterWorldSpaceTM ({an.Count} bones)");
        int bad = 0, total = 0;
        foreach (var mp in hk.Objects.Where(x => x.Class == "hkaSkeletonMapper").Select(x => hk.ReadObject(x.Offset)))
        {
            var d = mp.Get("mapping");
            int na = ((System.Collections.IList)((Formats.Havok.HkPObj)d["skeletonA"]!)["bones"]!).Count, nbb = ((System.Collections.IList)((Formats.Havok.HkPObj)d["skeletonB"]!)["bones"]!).Count;
            foreach (var s in d.List("simpleMappings").Cast<Formats.Havok.HkPObj>()) { total++; if ((short)s["boneA"]! >= na || (short)s["boneB"]! >= nbb) bad++; }
        }
        Check(bad == 0, $"skeleton mappers: {total} simple mappings, all indices in range");
        // constraints (session 8, crash s8-6: the converter wrote every C-array pointer null, so all 36 constraints had
        // entities [null, null] and DS2's hkaRagdollInstance::clone crashed in the hkpConstraintInstance constructor)
        var hkObjs = hk.Objects.Select(x => hk.ReadObject(x.Offset)).ToList();
        bool Linked(HashSet<object?> bodies, Formats.Havok.HkPObj c) => c["entities"] is System.Collections.IList en && en.Count == 2
            && en[0] is Formats.Havok.HkPObj ea && en[1] is Formats.Havok.HkPObj eb && !ReferenceEquals(ea, eb) && bodies.Contains(ea) && bodies.Contains(eb);
        var ri = hkObjs.First(x => x.Class == "hkaRagdollInstance");
        var riBodies = new HashSet<object?>(ri.List("rigidBodies"), ReferenceEqualityComparer.Instance);
        var riCons = ri.List("constraints").Cast<Formats.Havok.HkPObj>().ToList();
        Check(riCons.Count > 0 && riCons.All(c => Linked(riBodies, c)), $"ragdoll instance: {riCons.Count(c => Linked(riBodies, c))}/{riCons.Count} constraints with both entities set, distinct and among its {riBodies.Count} rigid bodies");
        int sysCons = 0, sysOk = 0;
        foreach (var ps in hkObjs.Where(x => x.Class == "hkpPhysicsSystem"))
        {
            var set = new HashSet<object?>(ps.List("rigidBodies"), ReferenceEqualityComparer.Instance);
            foreach (var c in ps.List("constraints").Cast<Formats.Havok.HkPObj>()) { sysCons++; if (Linked(set, c)) sysOk++; }
        }
        Check(sysOk == sysCons, $"physics systems: {sysOk}/{sysCons} constraints with both entities set, distinct and among the system's rigid bodies");
        var riRag = riCons.Select(c => c["data"]).OfType<Formats.Havok.HkPObj>().Where(d => d.Class == "hkpRagdollConstraintData").ToList();
        int motored = riRag.Count(d => d.Get("atoms").Get("ragdollMotors")["motors"] is System.Collections.IList m && m.Count == 3 && m.Cast<object?>().All(x => x is Formats.Havok.HkPObj));
        Check(motored == riRag.Count, $"ragdoll instance: {motored}/{riRag.Count} ragdoll constraints with 3 motors (DS2 and DS1 alike)");
        var allRag = hkObjs.Where(x => x.Class == "hkpRagdollConstraintData").ToList();
        int cone56 = allRag.Count(d => System.Convert.ToInt64(d.Get("atoms").Get("coneLimit")["memOffsetToAngleOffset"] ?? 0L) == 56);
        Check(cone56 == allRag.Count, $"ragdoll constraint data: cone limit memOffsetToAngleOffset 56 (DS2's Havok 2014 runtime) on {cone56}/{allRag.Count}");
        var rbs = hkObjs.Where(x => x.Class == "hkpRigidBody").ToList();
        int npOk = rbs.Count(b => (System.Convert.ToInt64(b["npData"] ?? 0L) & 0xFFFFFFFFL) == 0xFFFFFFFFL);
        var shapes = hkObjs.Where(x => x["dispatchType"] is { } v && v is not string).ToList();
        int dispOk = shapes.Count(x => System.Convert.ToInt64(x["dispatchType"]) == 4);
        Check(npOk == rbs.Count && dispOk == shapes.Count, $"DS2 physics conventions: npData 0xFFFFFFFF on {npOk}/{rbs.Count} bodies, dispatchType 4 on {dispOk}/{shapes.Count} shapes");
        // ---- 4. foot positions vs the DS1 source (locomotion loops: vs the source started at its left touchdown)
        var ds1 = Build.Chr.Ds1ChrSource.Load(Program.DefaultDsrRoot, chrO.Ds1Chr);
        // session 11 (ret11): DS1 foot dummies (rule C11c loop starts / TAE footfalls) and the anim set's animIdOffset (rule R-off)
        var ds1Feet = Build.Chr.Ds1RigChr.FootDummies(FLVER2.Read(BND3.Read(Path.Combine(Program.DefaultDsrRoot, "chr", chrO.Ds1Chr + ".chrbnd.dcx")).Files.First(x => x.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes));
        int roff = chrRig.Attacks?.AnimOffset ?? 0;
        var loopK = new Dictionary<string, (int Id, int K)>();
        var pose = MorphemePose.From(skel);
        // the DS1 attack clips (track enemies): NSA files a65_XX_YYYY_untitled_<crc> = DS1 clip XX*10000+YYYY (Ds1Attacks naming)
        int AttackClip(string nm)
        {
            var m = System.Text.RegularExpressions.Regex.Match(nm, @"^a65_(\d\d)_(\d{4})_untitled_[0-9a-f]+(\.nsa)?$");
            if (!m.Success) return -1;
            int c = int.Parse(m.Groups[1].Value) * 10000 + int.Parse(m.Groups[2].Value);
            return chrRig.Attacks != null && ds1.Has(c) ? c : -1;
        }
        int lf = pose.Index("L_Foot"), rf = pose.Index("R_Foot");
        int lb = pose.Index(Build.Chr.Ds1RigAnims.LockJointL), rb = pose.Index(Build.Chr.Ds1RigAnims.LockJointR);
        var roleOf = chrRig.Roles;
        double worstPos = 0, worstShift = 0; string worstClip = "";
        int compared = 0, loops = 0;
        var shiftSeen = new HashSet<int>();
        var cmpSeen = new HashSet<(int, bool)>();
        var srcOf = new Dictionary<string, NsaAnim>();   // full-precision DS1 source of each slot, as the build saw it
        foreach (var (name, a) in clips.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            int ac = AttackClip(name);
            var role = ac >= 0 ? new Build.Chr.Ds1RigAnims.Role("", [ac], "DS1 attack", "P", "") : Build.Chr.Ds1RigAnims.RoleFor(roleOf, name);
            // session 11 (ret11), rule R-off: slot roles resolve with the anim set's animIdOffset (attack clips are already absolute)
            int id = role == null ? int.MinValue : (ac >= 0 ? role.Ds1 : Build.Chr.Ds1RigAnims.Offset(role.Ds1, ds1, chrRig.Attacks?.AnimOffset ?? 0)).FirstOrDefault(x => x == -1 || ds1.Has(x), int.MinValue);
            if (id < 0) continue;
            var orig = ds1.Bridge(id).Anim;
            var src = orig;
            if (role!.Loop)
            {
                int k = Build.Chr.Ds1RigAnims.LoopStart(orig, pose, ds1, id, ds1Feet);
                loopK[name] = (id, k);
                src = Build.Chr.Ds1RigAnims.PhaseShift(orig, k);
                // independent check of the phase shift: the shifted clip's ball paths = the original's cyclic path from frame k,
                // re-based on the original's trajectory transform at k
                if (shiftSeen.Add(id))
                {
                    loops++;
                    var tk = orig.RootPos[k]; var qk = MorphemeMath.Conj(orig.RootRot[k]);
                    foreach (int j in new[] { lb, rb })
                    {
                        var po = Build.Chr.Ds1RigAnims.JointPath(orig, pose, j, true);
                        for (int f = 0; f < src.Frames; f++)
                        {
                            var want = MorphemeMath.Rotate(qk, new V3(po[k + f].X - tk.X, po[k + f].Y - tk.Y, po[k + f].Z - tk.Z));
                            worstShift = Math.Max(worstShift, MorphemePose.Dist(want, pose.World(src, f, true).Pos[j]));
                        }
                    }
                }
            }
            srcOf[name] = src;
            if (!cmpSeen.Add((id, role.Loop))) continue;
            if (src.Frames != a.Frames) { Console.WriteLine($"    {name}: frame count {a.Frames} vs DS1 {src.Frames}"); fails++; continue; }
            double dp = 0;
            for (int f = 0; f < a.Frames; f++)
            {
                var wo = pose.World(a, f, true); var ws = pose.World(src, f, true);
                dp = Math.Max(dp, Math.Max(MorphemePose.Dist(wo.Pos[lf], ws.Pos[lf]), MorphemePose.Dist(wo.Pos[rf], ws.Pos[rf])));
            }
            compared++;
            if (dp > worstPos) { worstPos = dp; worstClip = Build.Chr.Ds1ChrSource.ClipName(id) + (role.Loop ? " (loop)" : ""); }
            if (args.Contains("-v")) Console.WriteLine($"    {Build.Chr.Ds1ChrSource.ClipName(id)}{(role.Loop ? " loop" : "")} ({name[..11]}): feet vs DS1 max {dp * 1000:0.00} mm");
        }
        Check(worstPos < 0.005, $"foot positions vs DS1 source on {compared} DS1 clips: max {worstPos * 1000:0.00} mm ({worstClip})");
        Check(worstShift < 1e-6, $"locomotion loops started at the left touchdown: {loops} DS1 clips, ball paths = the original cycle re-based, max {worstShift * 1000:0.000000} mm");

        // ---- 5. event tracks as built (fix pass): read back from the NMB, per anim node
        var trackOf = new Dictionary<uint, NmbEventTrack>();
        foreach (var b in nmb.Bundles.Where(b => b.Type is 3 or 4 or 5)) trackOf[b.AssetId] = NmbEventTrack.Read(b.Data, b.Type);
        var nodeClip = new Dictionary<int, string>();
        var nodeTracks = new Dictionary<int, List<(Build.Chr.Ds1RigAnims.TrackKind Kind, NmbEventTrack T, uint Id)>>();
        foreach (var node in net.Nodes().Where(x => x.Type == 104))
        {
            var sa = NmbNetwork.Find(node, 21); var et = NmbNetwork.Find(node, 23);
            if (sa == null) continue;
            nodeClip[node.Index] = fn[net.ClipIndex(sa)];
            var l = nodeTracks[node.Index] = [];
            if (et == null) continue;
            int si = net.SyncTrackIndex(sa), disc = 0;
            foreach (var (g, tid, _) in net.Tracks(et))
            {
                bool isSync = g == 0 && disc++ == si;
                if (trackOf.TryGetValue(tid, out var t)) l.Add((Build.Chr.Ds1RigAnims.KindOf(t, isSync), t, tid));
            }
        }
        NsaAnim ClipFor(string nm) => clips.First(kv => kv.Key.Equals(nm, StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(kv.Key) == nm).Value;
        Build.Chr.Ds1RigAnims.Role? RoleOfClip(string nm) => AttackClip(nm) is int ac and >= 0 ? new Build.Chr.Ds1RigAnims.Role("", [ac], "DS1 attack", "P", "")
            : Build.Chr.Ds1RigAnims.RoleFor(roleOf, nm) is { } rr ? rr with { Ds1 = Build.Chr.Ds1RigAnims.Offset(rr.Ds1, ds1, roff) } : null;   // session 11 (ret11): rule R-off

        // 5a. sync tracks partition [0,1] with no gap, no event shorter than 2 frames
        int syncTracks = 0, syncBad = 0; string syncWorst = "";
        foreach (var (ni, l) in nodeTracks)
            foreach (var (kind, t, _) in l.Where(x => x.Kind == Build.Chr.Ds1RigAnims.TrackKind.Sync))
            {
                syncTracks++;
                var a = ClipFor(nodeClip[ni]); double P = a.Frames - 1;
                var ev = t.Events.OrderBy(e => e.Start).ToList();
                bool ok = ev.Count > 0 && Math.Abs(ev[0].Start) < 1e-4 && Math.Abs(ev[^1].Start + ev[^1].Duration - 1) < 1e-4
                    && ev.Zip(ev.Skip(1)).All(p => Math.Abs(p.First.Start + p.First.Duration - p.Second.Start) < 1e-4)
                    && ev.All(e => e.Duration * P >= Math.Min(2, P) - 1e-3);
                if (!ok) { syncBad++; syncWorst = $"{nodeClip[ni][..11]} ({P} frame intervals) {string.Join(" ", ev.Select(e => $"{e.Start:0.###}+{e.Duration:0.###}"))}"; if (args.Contains("-v")) Console.WriteLine("    bad sync: " + syncWorst); }
            }
        Check(syncBad == 0, $"sync tracks: {syncTracks} node references, every one partitions [0,1] with no gap and no event under 2 frames (or the whole clip){(syncBad > 0 ? $" - {syncBad} bad, e.g. {syncWorst}" : "")}");

        // 5b. blends: anim children of every Blend2/BlendN (through PlaySpeed/pass-through/mirror/filter wrappers) that include
        // a locomotion loop have equal sync counts, and at each sync event the same foot touches down in every loop child
        List<int> Leaves(int i, int depth)
        {
            var n = net.GetNode(i);
            if (n.Type == 104) return nodeTracks.ContainsKey(i) ? [i] : [];
            if (depth > 3 || n.Type is not (125 or 134 or 135 or 105)) return [];
            return n.Children.SelectMany(c => Leaves(c, depth + 1)).ToList();
        }
        int blends = 0, blendBad = 0; var blendNotes = new List<string>();
        var footSeq = new Dictionary<string, string>();
        string FeetAtSync(int leaf)
        {
            string nm = nodeClip[leaf];
            if (footSeq.TryGetValue(nm, out var s)) return s;
            var a = ClipFor(nm); int P = a.Frames - 1;
            var syncT = nodeTracks[leaf].First(x => x.Kind == Build.Chr.Ds1RigAnims.TrackKind.Sync).T;
            var dl = Build.Chr.Ds1RigAnims.Touchdowns(Build.Chr.Ds1RigAnims.JointPath(a, pose, lb, true), P, true, a.Fps).Select(r => r.A).ToList();
            var dr = Build.Chr.Ds1RigAnims.Touchdowns(Build.Chr.Ds1RigAnims.JointPath(a, pose, rb, true), P, true, a.Fps).Select(r => r.A).ToList();
            // touchdowns measured on the output NSA itself, independent of the tracks
            s = string.Concat(syncT.Events.OrderBy(e => e.Start).Select(e => { int f = (int)Math.Round(e.Start * P); return dl.Contains(f) ? 'L' : dr.Contains(f) ? 'R' : '-'; }));
            // session 11 (ret11), rule C11c: a loop whose motion does not show alternating touchdowns is measured on DS1's own TAE
            // footfalls (shifted by the loop start the build chose), within 2 frames
            if (s.Contains('-') && loopK.FirstOrDefault(kv => kv.Key.Equals(nm, StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(kv.Key) == nm) is { Key: not null } lk)
            {
                var tf = Build.Chr.Ds1RigAnims.TaeFootfalls(ds1.TaeAnim(lk.Value.Id), ds1Feet).Select(h => (F: ((h.Frame - lk.Value.K) % P + P) % P, h.Foot)).ToList();
                s = string.Concat(syncT.Events.OrderBy(e => e.Start).Select(e => { int f = (int)Math.Round(e.Start * P); var hit = tf.FirstOrDefault(h => Math.Min(Math.Abs(h.F - f), P - Math.Abs(h.F - f)) <= 2); return hit.Foot == default ? '-' : hit.Foot; }));
            }
            return footSeq[nm] = s;
        }
        foreach (var node in net.Nodes().Where(x => x.Type is 107 or 108))
        {
            var leaves = node.Children.SelectMany(c => Leaves(c, 0)).Distinct().Where(x => nodeTracks[x].Any(t => t.Kind == Build.Chr.Ds1RigAnims.TrackKind.Sync)).ToList();
            if (leaves.Count < 2 || !leaves.Any(x => RoleOfClip(nodeClip[x])?.Loop == true)) continue;
            blends++;
            var counts = leaves.Select(x => nodeTracks[x].First(t => t.Kind == Build.Chr.Ds1RigAnims.TrackKind.Sync).T.Events.Count).Distinct().ToList();
            var seqs = leaves.Where(x => RoleOfClip(nodeClip[x])?.Loop == true).Select(FeetAtSync).Distinct().ToList();
            bool ok = counts.Count == 1 && seqs.Count == 1 && seqs[0].All(c => c != '-') && seqs[0].StartsWith('L');
            if (!ok) blendBad++;
            if (blendNotes.Count < 12) blendNotes.Add($"node {node.Index}: {leaves.Count} children, sync counts {string.Join("/", counts)}, loop feet {string.Join("/", seqs)}");
        }
        Check(blendBad == 0 && blends > 0, $"locomotion blends: {blends} Blend2/BlendN nodes with a loop child; all children have equal sync counts and every loop child's sync event k is the same foot's touchdown (measured on the NSA){(blendBad > 0 ? $" - {blendBad} bad" : "")}");
        if (args.Contains("-v")) foreach (var n in blendNotes) Console.WriteLine("    " + n);

        // 5c. foot-lock windows: the ball joint stays within the lock tolerance of its window-start position, in the output NSA
        double worstBall = 0, worstBallSrc = 0, worstAnkle = 0; string worstLock = "", worstAnkleAt = ""; int windows = 0;
        var lockSeen = new HashSet<(uint, string)>();
        foreach (var (ni, l) in nodeTracks)
            foreach (var (kind, t, tid) in l.Where(x => x.Kind is Build.Chr.Ds1RigAnims.TrackKind.LeftFoot or Build.Chr.Ds1RigAnims.TrackKind.RightFoot))
            {
                string nm = nodeClip[ni];
                if (!lockSeen.Add((tid, nm))) continue;
                var a = ClipFor(nm); int P = a.Frames - 1;
                bool cyc = RoleOfClip(nm)?.Loop == true;
                int ball = kind == Build.Chr.Ds1RigAnims.TrackKind.LeftFoot ? lb : rb, ankle = kind == Build.Chr.Ds1RigAnims.TrackKind.LeftFoot ? lf : rf;
                var pb = Build.Chr.Ds1RigAnims.JointPath(a, pose, ball, cyc); var pa = Build.Chr.Ds1RigAnims.JointPath(a, pose, ankle, cyc);
                V3[]? ps = srcOf.TryGetValue(nm, out var src) ? Build.Chr.Ds1RigAnims.JointPath(src, pose, ball, cyc) : null;
                foreach (var (s, d, _) in t.Events)
                {
                    if (d <= 0) continue;
                    if (RoleOfClip(nm)?.Ds1.FirstOrDefault() == -1) continue;   // additive bind-pose slots: static, both feet locked
                    windows++;
                    int fa = (int)Math.Round(s * P), fb = (int)Math.Round((s + d) * P);
                    if (!cyc) fb = Math.Min(fb, P);
                    double db = Build.Chr.Ds1RigAnims.WindowDrift(pb, fa, fb);
                    if (db > worstBall) { worstBall = db; worstLock = $"{nm[..11]} {kind} {s:0.###}+{d:0.###}"; }
                    if (ps != null) worstBallSrc = Math.Max(worstBallSrc, Build.Chr.Ds1RigAnims.WindowDrift(ps, fa, fb));
                    double dA = Build.Chr.Ds1RigAnims.WindowDrift(pa, fa, fb);
                    if (dA > worstAnkle) { worstAnkle = dA; worstAnkleAt = $"{nm[..11]} {kind} {s:0.###}+{d:0.###}"; }
                }
            }
        Check(worstBall <= Build.Chr.Ds1RigAnims.LockTolerance + 1e-4, $"foot-lock windows: {windows} windows; ball joint drift from the window start at most {worstBall * 1000:0.0} mm in the output NSA ({worstLock}), {worstBallSrc * 1000:0.0} mm in the full-precision DS1 source (tolerance {Build.Chr.Ds1RigAnims.LockTolerance * 1000:0} mm; ankle drift, the foot rolling or pivoting about the ball, up to {worstAnkle * 1000:0.0} mm: {worstAnkleAt})");

        // 5d. no-attack slots: no attack-family event left, return to base over the whole clip
        int naSlots = 0, naBad = 0; string naNote = "";
        foreach (var (ni, l) in nodeTracks)
        {
            var role = RoleOfClip(nodeClip[ni]);
            if (role?.NoAttack != true) continue;
            naSlots++;
            bool attackLeft = l.Any(x => x.T.Events.Any(e => e.Value >= 100 && e.Value < 200));
            bool rtbOk = l.Where(x => x.Kind == Build.Chr.Ds1RigAnims.TrackKind.ReturnToBase || x.T.Events.Any(e => e.Value == 80) && x.T.Events.All(e => e.Value == 80))
                .All(x => x.T.Events.Count == 1 && x.T.Events[0].Start == 0 && Math.Abs(x.T.Events[0].Duration - 1) < 1e-6);
            if (attackLeft || !rtbOk) { naBad++; naNote = nodeClip[ni][..11]; }
        }
        Check(naBad == 0, $"no-attack slots: {naSlots} node references, no event value 100-199 on any track, return-to-base (80) over the whole clip where the node has that track{(naBad > 0 ? $" - {naBad} bad, e.g. {naNote}" : "")}");

        // 5e. DS1 TAE gameplay flags → tracks: DamageDuration ends at DS1's AI cancel, return to base never before it,
        // DieControl starts at DS1's Death State / Throw Death Transition
        int tChecked = 0, tBad = 0; string tNote = "";
        foreach (var (ni, l) in nodeTracks)
        {
            string nm = nodeClip[ni];
            var role = RoleOfClip(nm);
            int id = role?.Ds1.FirstOrDefault(x => x == -1 || ds1.Has(x), int.MinValue) ?? int.MinValue;
            if (id < 0 || role!.NoAttack) continue;
            var tae = ds1.TaeAnim(id); var a = ClipFor(nm); double dur = (a.Frames - 1) / a.Fps;
            var j78 = Build.Chr.Ds1RigAnims.JumpTable(tae, 78); var j79 = Build.Chr.Ds1RigAnims.JumpTable(tae, 79); var j23 = Build.Chr.Ds1RigAnims.JumpTable(tae, 23);
            var cl = j78.Count > 0 ? j78 : j79.Count > 0 ? j79 : j23;
            double cancel = cl.Count > 0 ? Math.Clamp(cl[0].S / dur, 0, 1) : -1;
            var dj = Build.Chr.Ds1RigAnims.JumpTable(tae, 12).Concat(Build.Chr.Ds1RigAnims.JumpTable(tae, 69)).OrderBy(x => x.S).ToList();
            double death = dj.Count > 0 ? Math.Clamp(dj[0].S / dur, 0, 1) : -1;
            foreach (var (kind, t, tid) in l)
            {
                bool shared = nodeTracks.Any(kv => kv.Key != ni && nodeClip[kv.Key] != nm && kv.Value.Any(x => x.Id == tid));
                bool? ok = kind switch
                {
                    Build.Chr.Ds1RigAnims.TrackKind.Damage when cancel >= 0 => shared ? t.Events[0].Duration >= cancel - 1e-4 : Math.Abs(t.Events[0].Duration - cancel) < 1e-4,
                    Build.Chr.Ds1RigAnims.TrackKind.ReturnToBase when cancel >= 0 => t.Events.All(e => e.Start >= cancel - 1e-4),
                    Build.Chr.Ds1RigAnims.TrackKind.Die when death >= 0 => shared ? t.Events[0].Start >= death - 1e-4 : Math.Abs(t.Events[0].Start - death) < 1e-4,
                    _ => null,
                };
                if (ok == null) continue;
                tChecked++;
                if (ok == false) { tBad++; tNote = $"{nm[..11]} {kind} {string.Join(" ", t.Events.Select(e => $"{e.Start:0.###}+{e.Duration:0.###}"))} (DS1 cancel {cancel:0.###}, death {death:0.###})"; }
            }
        }
        Check(tBad == 0 && tChecked > 0, $"DS1 TAE flags → tracks: {tChecked} track references (DamageDuration end = DS1 AI Movement Cancel, return to base ≥ it, DieControl = DS1 Death State / Throw Death Transition; shared tracks: the merged window covers each slot's){(tBad > 0 ? $" - {tBad} bad, e.g. {tNote}" : "")}");
        // ---- 6. TAE
        foreach (var tf in Directory.GetFiles(Path.Combine(gd, "timeact", "chr"), "*.tae"))
        {
            try
            {
                var tae = TAE.Read(tf);
                Check(true, $"{Path.GetFileName(tf)}: SoulsFormats reads {tae.Animations.Count} animations, {tae.Animations.Sum(x => x.Events.Count)} events");
            }
            catch (Exception e) { Check(false, $"{Path.GetFileName(tf)}: {e.Message}"); }
        }
        // ---- 6b. sound (track chrsound, rules S1-S10), recomputed here from the DS1 sources, not from the build's tables.
        // merge9 stage 4: generic over the 1:1 characters (the track checked c2501 only): the DS1 character, its decade's bank,
        // the template's vanilla bank and the DS1 attack clips' own TAE animations (track enemies) come from the rig checked.
        {
            string dsrSnd = Path.Combine(Program.DefaultDsrRoot, "sound");
            var mainFev = Formats.Fev1Project.Read(File.ReadAllBytes(Path.Combine(dsrSnd, "frpg_main.fev")));
            int chrId = int.Parse(chrO.Ds1Chr[1..], Inv), bankChr = Build.Chr.Ds1ChrSound.BankChr(chrRig.AnimId);
            string proj = $"frpg_{chrO.Ds1Chr}", tChr = chrO.TemplateChr, bankName = Build.Chr.Ds1ChrSound.BankName(bankChr);
            // the events DS1's own project defines: an own-range TAE id it lacks plays nothing in DS1 and stays silent here
            var ownNames = Formats.Fev1Project.Read(File.ReadAllBytes(Path.Combine(dsrSnd, proj + ".fev"))).Events.Select(e => e.Name).ToHashSet();
            // the copies the bank must hold, by the build's own procedure (Ds1ChrSound.Bank): every sound id any DS1 TAE
            // animation names (floor / armor material 'x' / 'b' → 'c' material 1), except own-range ids DS1's project defines,
            // in (type, id) order; frpg_main must define it as a simple (rule S5) or layered (rule S5c, chrint stage 4) event;
            // name = its own (character range, rule S5b) or c{DS1 chr}9{last 4 digits} (common); a name already taken (DS1's own
            // events, then earlier copies) is not copied (c2900: own-range c290099999 takes the name before the common
            // c999999999 would)
            var usedAll = ds1.Tae!.Animations.SelectMany(x => x.Events).Where(e => e.Type is 128 or 129 or 130).Select(e =>
            {
                var p = e.GetParameterBytes(false);
                int type = BitConverter.ToInt32(p, 0), sid = BitConverter.ToInt32(p, 4);
                if (type is 8 or 9) { type = 1; sid = sid / 10 * 10 + 1; }
                return (Type: type, Sid: sid);
            }).Where(e => e.Type is >= 0 and < 12 && !(e.Type == 1 && e.Sid / 100000 == chrId && ownNames.Contains($"c{e.Sid:D9}"))).Distinct().OrderBy(x => x.Type).ThenBy(x => x.Sid).ToList();
            var takenNames = new HashSet<string>(ownNames);
            var commonCopy = new Dictionary<(int, int), int>(); var ownCopy = new HashSet<int>(); var expectCopy = new Dictionary<string, string>();
            foreach (var (type, sid) in usedAll)
            {
                bool ownRange = type == 1 && sid / 100000 == chrId;
                string src = $"{"acfopsmvxbgy"[type]}{sid:D9}";
                var me = mainFev.EventByName(src);
                if (me == null || (mainFev.SoundDefOf(me) < 0 && mainFev.LayerSoundDefOffsets(me) == null)) continue;
                string nn = ownRange ? src : Build.Chr.Ds1ChrSound.CommonName(chrId, sid);
                if (!takenNames.Add(nn)) continue;
                expectCopy[nn] = src;
                if (ownRange) ownCopy.Add(sid); else commonCopy[(type, sid)] = int.Parse(nn[1..], Inv);
            }
            // a DS1 common sound plays from the character's project under the name of its copy (rule S5)
            int? CommonId(int type, int sid) => commonCopy.TryGetValue((type, sid), out int c) ? c : null;
            // rule S5b/S5c: a character-range id DS1 keeps in frpg_main is copied under its own name
            bool MainSimple(int sid) => ownCopy.Contains(sid);
            // TAE ids → owning slot, as the network names them (TimeAct track values; the slot whose name encodes the id owns
            // it, else the first in name order); the DS1 attack clips' leaves name their own ids (65xxxxxx)
            var owner = new Dictionary<int, string>();
            foreach (var (ni, l) in nodeTracks.OrderBy(x => nodeClip[x.Key], StringComparer.Ordinal))
                foreach (var (_, t, _) in l.Where(x => x.T.Name == "TimeAct"))
                    foreach (var ev in t.Events)
                    {
                        string nm = nodeClip[ni];
                        if (!owner.TryGetValue(ev.Value, out var cur) || (Build.Chr.Ds1RigAnims.NameId(nm) == ev.Value && Build.Chr.Ds1RigAnims.NameId(cur) != ev.Value)) owner[ev.Value] = nm;
                    }
            string sndPath = Directory.GetFiles(Path.Combine(gd, "timeact", "chr"), "*_snd.tae").Single();
            string sfxPath = Directory.GetFiles(Path.Combine(gd, "timeact", "chr"), "*_sfx.tae").Single();
            var snd = new Formats.Ds2Tae(File.ReadAllBytes(sndPath));
            var sfx = new Formats.Ds2Tae(File.ReadAllBytes(sfxPath));
            var sndSf = TAE.Read(sndPath);
            int animsChecked = 0, evOk = 0, evBad = 0, emptyOk = 0, sfxEmpty = 0, sfxNamed = 0, sfOk = 0, commonEvents = 0, attackAnims = 0, attackEvents = 0, ownSilent = 0; string sndNote = "";
            var names = new HashSet<string>();
            foreach (var (tid, nm) in owner.OrderBy(x => x.Key))
            {
                if (!snd.Records.ContainsKey(tid)) continue;
                animsChecked++;
                var role = RoleOfClip(nm);
                bool attack = AttackClip(nm) >= 0;
                int id = role?.Ds1.FirstOrDefault(x => x == -1 || ds1.Has(x), int.MinValue) ?? int.MinValue;
                var a = ClipFor(nm);
                int k = 0;
                if (id >= 0 && role!.Loop) k = Build.Chr.Ds1RigAnims.LoopStart(ds1.Bridge(id).Anim, pose, ds1, id, ds1Feet);
                int P = a.Frames - 1;
                // expected: every DS1 128/129/130 event of the character's own range (type c, or x / b → c material 1) with its
                // own id, every DS1 common sound frpg_main defines with the id of its copy; at (f - k) mod P
                var want = new List<(float S, float E, int Id, int Dummy)>();
                var tae = id >= 0 ? ds1.TaeAnim(id) : null;
                foreach (var e in tae?.Events.Where(e => e.Type is 128 or 129 or 130) ?? [])
                {
                    var p = e.GetParameterBytes(false);
                    int type = BitConverter.ToInt32(p, 0), sid = BitConverter.ToInt32(p, 4), dmy = e.Type == 128 ? -1 : BitConverter.ToInt32(p, 8);
                    if (type is 8 or 9) { type = 1; sid = sid / 10 * 10 + 1; }   // floor / armor material: the 'c' event of material 1 (rule S4)
                    if (type == 1 && sid / 100000 == chrId) { if (!ownNames.Contains($"c{sid:D9}") && !MainSimple(sid)) { ownSilent++; continue; } }
                    else if (CommonId(type, sid) is int cid) { sid = cid; commonEvents++; }
                    else continue;
                    float s = e.StartTime, en = Math.Min(e.EndTime, (float)(P / a.Fps));
                    if (k != 0)
                    {
                        int f = (int)Math.Round(e.StartTime * a.Fps), len = (int)Math.Round((e.EndTime - e.StartTime) * a.Fps), g = ((f - k) % P + P) % P;
                        s = (float)(g / a.Fps); en = (float)(Math.Min(g + len, P) / a.Fps);
                    }
                    want.Add((s, en, sid, dmy));
                }
                want = [.. want.OrderBy(x => x.S).ThenBy(x => x.Id)];
                var got = snd.EventsFull(tid).OrderBy(x => x.Start).ThenBy(x => BitConverter.ToInt32(x.Params, 0)).ToList();
                if (attack) { attackAnims++; attackEvents += got.Count; }
                if (want.Count == 0) { if (got.Count == 0) emptyOk++; else { evBad++; sndNote = $"{tid}: {got.Count} events, DS1 has none"; } }
                else if (got.Count != want.Count) { evBad++; sndNote = $"{tid} <- {nm[..11]}: {got.Count} events, DS1 {want.Count}"; }
                else
                    for (int i = 0; i < want.Count; i++)
                    {
                        var g = got[i]; var w2 = want[i];
                        bool ok = g.Type == 201 && g.Unk04 == 1 && BitConverter.ToInt32(g.Params, 0) == w2.Id && BitConverter.ToInt16(g.Params, 10) == (short)w2.Dummy
                            && g.Params[12] == 1 && g.Params[13] == 1 && BitConverter.ToUInt16(g.Params, 16) == 0xFFFF && Math.Abs(g.Start - w2.S) < 1e-4 && Math.Abs(g.End - w2.E) < 1e-4;
                        if (ok) { evOk++; names.Add($"c{w2.Id:D9}"); } else { evBad++; sndNote = $"{tid} <- {nm[..11]} event {i}: got {g.Type} id {BitConverter.ToInt32(g.Params, 0)} @{g.Start:0.###}-{g.End:0.###}, want c{w2.Id:D9} @{w2.S:0.###}-{w2.E:0.###}"; }
                    }
                // SoulsFormats reads the same events (type, times)
                var sfa = sndSf.Animations.FirstOrDefault(x => x.ID == tid);
                if (sfa != null && sfa.Events.Count == got.Count && sfa.Events.OrderBy(x => x.StartTime).Zip(got).All(z => z.First.Type == z.Second.Type && Math.Abs(z.First.StartTime - z.Second.Start) < 1e-6)) sfOk++;
                // _sfx: no DS1 effect has a DS2 counterpart yet (rule S7: the effect map names none) → every named animation is empty
                if (sfx.Records.ContainsKey(tid)) { sfxNamed++; if (sfx.EventCount(tid) == 0) sfxEmpty++; }
            }
            Check(evBad == 0 && evOk > 0, $"_snd ({Path.GetFileName(sndPath)}): {animsChecked} animations named by clips{(attackAnims > 0 ? $" ({attackAnims} of them the DS1 attack clips' own, {attackEvents} events)" : "")}; {evOk} DS1 sound events as 201 at DS1's times (loops: (f - k) mod P; {commonEvents} of them DS1 common sounds named by their copy), {emptyOk} animations without DS1 sounds empty; {ownSilent} character-range DS1 events in neither {proj} nor frpg_main (as a simple or layered event, rules S5b/S5c) left silent{(evBad > 0 ? $" - {evBad} bad, e.g. {sndNote}" : "")}");
            Check(sfOk == animsChecked, $"_snd: SoulsFormats reads the same events on {sfOk}/{animsChecked} animations");
            Check(sfxEmpty == sfxNamed, $"_sfx ({Path.GetFileName(sfxPath)}): {sfxEmpty}/{sfxNamed} named animations without events (no DS1 effect of {chrO.Ds1Chr} mapped, rule S7)");
            // the bank: DS2's name (animId/100 rounded to tens), id 0 = DS1's FEV, id 1 = DS1's FSB, layout as the template's vanilla bank
            string bankPath = Directory.GetFiles(Path.Combine(gd, "sound"), "frpg2_c*.bnd").Single();
            byte[] bankRaw = File.ReadAllBytes(bankPath);
            var van = File.ReadAllBytes(Path.Combine(ExtractDs2.DefaultOut, "sound", $"frpg2_{tChr}.bnd"));
            bool dcxSame = DCX.Is(bankRaw) == DCX.Is(van);
            var bk = BND4.Read(DCX.Is(bankRaw) ? DCX.Decompress(bankRaw) : bankRaw);
            var vb = BND4.Read(DCX.Is(van) ? DCX.Decompress(van) : van);
            byte[] fev = File.ReadAllBytes(Path.Combine(dsrSnd, proj + ".fev"));
            var origBanks = Build.Chr.Ds1ChrSound.Fev1Header(fev).Banks;
            string idOneName = origBanks.Any(x => x.Name == proj) ? proj : origBanks[^1].Name;
            byte[] fsb = File.ReadAllBytes(Path.Combine(dsrSnd, idOneName + ".fsb"));
            bool layout = dcxSame && bk.Version == vb.Version && bk.Format == vb.Format && bk.Unicode == vb.Unicode && bk.Extended == vb.Extended && bk.Unk04 == vb.Unk04 && bk.Unk05 == vb.Unk05
                && bk.Files.All(f => f.Flags == vb.Files[0].Flags);
            Check(Path.GetFileName(bankPath) == bankName + ".bnd", $"bank file {Path.GetFileName(bankPath)} (animId {chrRig.AnimId} / 100 = {chrRig.AnimId / 100}, rounded down to tens = {bankChr}: the name DS2 opens, run s8-6)");
            Check(layout, $"bank layout = vanilla frpg2_{tChr}.bnd (DCX {DCX.Is(bankRaw)}, BND4 version {bk.Version}, format {bk.Format}, unicode {bk.Unicode}, entry flags {vb.Files[0].Flags})");
            var e0 = bk.Files.FirstOrDefault(f => f.ID == 0); var e1 = bk.Files.FirstOrDefault(f => f.ID == 1);
            Check(bk.Files.Count == 2 && e0 != null && e1 != null && e1.Bytes.AsSpan().SequenceEqual(fsb), $"bank entries: id 0 = the project ('{e0?.Name}', {e0?.Bytes.Length:N0} B), id 1 = DSR {idOneName}.fsb ({fsb.Length:N0} B)");
            // the project: DS1's own with frpg_main's events copied in (rule S5), checked against both DSR projects
            var orig = Formats.Fev1Project.Read(fev);
            var ext = Formats.Fev1Project.Read(e0!.Bytes);
            bool origKept = ext.Events.Count >= orig.Events.Count && orig.Events.Select((e, i) => fev.AsSpan(e.Start, e.End - e.Start).SequenceEqual(e0.Bytes.AsSpan(ext.Events[i].Start, ext.Events[i].End - ext.Events[i].Start))).All(x => x)
                && orig.SoundDefs.Select((d, i) => fev.AsSpan(d.Start, d.End - d.Start).SequenceEqual(e0.Bytes.AsSpan(ext.SoundDefs[i].Start, ext.SoundDefs[i].End - ext.SoundDefs[i].Start))).All(x => x);
            Check(origKept, $"project: DS1's {orig.Events.Count} events and {orig.SoundDefs.Count} sound definitions byte-identical, first and in order");
            // expected copies (expectCopy, above: the build's procedure, rules S5 / S5b / S5c)
            int copiedOk = 0, copied = ext.Events.Count - orig.Events.Count, layeredCopies = 0; string copyNote = "";
            for (int i = orig.Events.Count; i < ext.Events.Count; i++)
            {
                var ne = ext.Events[i];
                if (!expectCopy.TryGetValue(ne.Name, out var srcName)) { copyNote = $"{ne.Name}: not an expected copy"; continue; }
                var src = mainFev.EventByName(srcName)!;
                // the definition index fields: one (simple event, name+0xb1) or one per layer (rule S5c)
                List<int> offs = mainFev.SoundDefOf(src) >= 0 ? [src.NameAt + 0xb1] : mainFev.LayerSoundDefOffsets(src)!;
                if (offs.Count > 1) layeredCopies++;
                var idx = offs.Select(o => o - src.Start).ToHashSet();
                // same bytes as frpg_main's event apart from the name (+8..+17), GUID (+19..+34) and the definition indices
                bool bytesOk = src.End - src.Start == ne.End - ne.Start
                    && Enumerable.Range(0, ne.End - ne.Start).All(k => (k >= 8 && k < 35) || idx.Contains(k) || idx.Contains(k - 1) || mainFev.Bytes[src.Start + k] == e0.Bytes[ne.Start + k]);
                // every layer's definition = frpg_main's (name and waves)
                bool defOk = bytesOk && offs.All(o =>
                {
                    var sd = ext.SoundDefs[BitConverter.ToUInt16(e0.Bytes, ne.Start + (o - src.Start))];
                    var sd0 = mainFev.SoundDefs[BitConverter.ToUInt16(mainFev.Bytes, o)];
                    return sd.Name == sd0.Name && sd.Waves.SequenceEqual(sd0.Waves);
                });
                if (bytesOk && defOk) copiedOk++; else copyNote = $"{ne.Name} <- {srcName}: bytes {(bytesOk ? "ok" : "DIFFER")}, definitions {(defOk ? "ok" : "DIFFER")}";
            }
            Check(copied == expectCopy.Count && copiedOk == copied, $"project: {copied} events copied from DSR frpg_main.fev ({layeredCopies} layered, rule S5c) = the {expectCopy.Count} DS1 common sounds (and character-range events DS1 keeps in frpg_main, rule S5b) {chrO.Ds1Chr}'s TAE names ({string.Join(" ", expectCopy.OrderBy(x => x.Key).Select(x => $"{x.Value}→{x.Key}"))}): bytes equal but name / GUID / definition indices, every definition = frpg_main's with its waves{(copyNote != "" ? " - " + copyNote : "")}");
            var (ca, cb) = Formats.Fev1Project.Counters(ext.SoundDefs);
            Check(ext.Header == (ca, cb), $"project header A/B {ext.Header.A}/{ext.Header.B} = Σ align4(definition name + 1) + 24 / + 32 per waveform ({ext.SoundDefs.Count} definitions, {ext.SoundDefs.Sum(d => d.Waves.Count)} waveforms)");
            var (projName, banks) = Build.Chr.Ds1ChrSound.Fev1Header(e0.Bytes);
            // the wave banks: DS1's own list, plus frpg_main once a common sound was copied (Fev1Project.AddEvents adds its entry)
            var wantBanks = origBanks.Select(b => b.Name).Concat(copied > 0 ? ["frpg_main"] : Array.Empty<string>()).Distinct().Order().ToList();
            var hashOf = wantBanks.ToDictionary(b => b, b => Build.Chr.Ds1ChrSound.Fsb4Hash(File.ReadAllBytes(Path.Combine(dsrSnd, b + ".fsb"))));
            Check(banks.Select(b => b.Name).Order().SequenceEqual(wantBanks) && banks.All(b => hashOf.TryGetValue(b.Name, out var h) && h == b.Hash), $"project '{projName}' wave banks {string.Join(", ", banks.Select(b => $"{b.Name} {b.Hash:X16}"))} = DS1's list{(copied > 0 ? " + frpg_main" : "")}, each = the DSR FSB4 hash");
            var defined = Build.Chr.Ds1ChrSound.Fev1EventNames(e0.Bytes);
            var missing = names.Where(n => !defined.Contains(n)).ToList();
            Check(names.Count > 0 && missing.Count == 0, $"every FMOD event the _snd TAE names ({names.Count}: {string.Join(" ", names.Order())}) is defined in the bank's project ({defined.Count} events){(missing.Count > 0 ? $" - missing {string.Join(" ", missing)}" : "")}");
            foreach (string bank in wantBanks)
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(dsrSnd, bank + ".fsb"));
                string loose = Path.Combine(dir, "title", "sound", bank + ".fsb");
                Check(File.Exists(loose) && File.ReadAllBytes(loose).AsSpan().SequenceEqual(bytes), $"loose wave bank title/sound/{bank}.fsb = DSR's ({bytes.Length:N0} B; FMOD opens DS1's banks by their own names, rules S2/S9)");
            }
            // ---- fix pass: rule S1b (project name = resource name), S10 (one bank per DS1 character per decade), S7 writer
            string resName = Path.GetFileNameWithoutExtension(bankPath);
            var ds2Projects = Build.Chr.Ds1ChrSound.Ds2ProjectNames(ExtractDs2.DefaultOut);
            Check(projName == resName && ext.Name == resName && e0.Name.EndsWith($@"\{resName}\{resName}.fev") && e1!.Name.EndsWith($@"\{resName}\{resName}.fsb"),
                $"rule S1b: the project's FMOD name '{projName}' = the bank's resource name '{resName}' (DS2 registers a project by getInfo's name and de-duplicates / releases banks by the resource name); entries named after the project ('{e0.Name}', '{e1?.Name}'; vanilla names its fsb entry after its fevbnd project, ...\\frpg2_c1\\frpg2_c1030.fsb; DS2 reads entries by id)");
            var objections = Build.Chr.Ds1ChrSound.BankChecks(ExtractDs2.DefaultOut, bankChr);
            Check(ds2Projects.Count > 0 && objections.Count == 0, $"rule S1b/S10: decade {bankChr} is free: no DS2 bank {bankName}.bnd, not 2250, none of DS2's {ds2Projects.Count} fevbnd projects ({string.Join(", ", ds2Projects.Order().Take(4))}, ...) is named {resName}{(objections.Count > 0 ? " - " + string.Join("; ", objections) : "")}");
            var renamedBack = Formats.Fev1Project.WithName(e0.Bytes, proj);
            var rbk = Formats.Fev1Project.Read(renamedBack);
            Check(renamedBack.Length == e0.Bytes.Length - (resName.Length - proj.Length) && rbk.Events.Count == ext.Events.Count && rbk.Events.Select((e, i) => renamedBack.AsSpan(e.Start, e.End - e.Start).SequenceEqual(e0.Bytes.AsSpan(ext.Events[i].Start, ext.Events[i].End - ext.Events[i].Start))).All(x => x),
                $"rule S1b: renaming changes only the header name (renamed back to DS1's '{proj}': {renamedBack.Length:N0} B, all {rbk.Events.Count} events byte-identical, A/B {rbk.Header.A}/{rbk.Header.B})");
            // S10: the bank depends only on the DS1 character and the decade: rebuilt twice from the DSR sources = the build's
            var b1 = Build.Chr.Ds1ChrSound.Bank(Program.DefaultDsrRoot, ExtractDs2.DefaultOut, chrO.Ds1Chr, bankChr, tChr, ds1.Tae);
            var b2 = Build.Chr.Ds1ChrSound.Bank(Program.DefaultDsrRoot, ExtractDs2.DefaultOut, chrO.Ds1Chr, bankChr, tChr, ds1.Tae);
            Check(b1.Bank.AsSpan().SequenceEqual(bankRaw) && b2.Bank.AsSpan().SequenceEqual(bankRaw) && b1.Commons.Count == b2.Commons.Count,
                $"rule S10: the bank rebuilt twice from DS1 {chrO.Ds1Chr} + decade {bankChr} = the build's bank byte for byte ({bankRaw.Length:N0} B, {b1.Commons.Count} common sounds), so every 1:1 build of {chrO.Ds1Chr} in decade {bankChr} ({string.Join(", ", chrO.Rigs.Where(x => Build.Chr.Ds1ChrSound.BankChr(x.AnimId) == bankChr).Select(x => x.AnimId))}) yields the same file");
            // S10: the pack rule (ChrPorter.AddDs1RigFiles): same DS1 character in the decade → shared; another DS1 character's
            // project in the same decade → refused (the other one: DS1 c2540, or c2500 when checking c2540)
            {
                var pk = new Build.Pack(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
                var rigA = chrRig; var rigB = chrRig with { AnimId = bankChr * 100 + (chrRig.AnimId / 100 % 10 == 9 ? 800 : 900) };
                Build.Chr.Ds1RigChr.Result R(int animId, byte[] bank)
                {
                    var res = new Build.Chr.Ds1RigChr.Result { Files = { [$"sound/{resName}.bnd"] = bank, [$"timeact/chr/c{animId:D6}_snd.tae"] = [1] } };
                    foreach (var (path, bytes) in b1.Loose) res.TitleFiles[path] = bytes;
                    return res;
                }
                Build.Chr.ChrPorter.AddDs1RigFiles(pk, chrO, rigA, R(rigA.AnimId, bankRaw));
                bool sharedOk;
                try { Build.Chr.ChrPorter.AddDs1RigFiles(pk, chrO, rigB, R(rigB.AnimId, b1.Bank)); sharedOk = pk.TryGetFile("gamedata", $"sound/{resName}.bnd") is { } kept && kept.AsSpan().SequenceEqual(bankRaw) && pk.TryGetFile("gamedata", $"timeact/chr/c{rigB.AnimId:D6}_snd.tae") != null; }
                catch (InvalidDataException) { sharedOk = false; }
                string otherChr = chrO.Ds1Chr == "c2540" ? "c2500" : "c2540";
                var ds1b = Build.Chr.Ds1ChrSource.Load(Program.DefaultDsrRoot, otherChr);
                var other = Build.Chr.Ds1ChrSound.Bank(Program.DefaultDsrRoot, ExtractDs2.DefaultOut, otherChr, bankChr, tChr, ds1b.Tae);
                bool refused;
                try { Build.Chr.ChrPorter.AddDs1RigFiles(pk, chrO, rigA with { AnimId = rigB.AnimId - 100 }, R(rigB.AnimId - 100, other.Bank)); refused = false; }
                catch (InvalidDataException) { refused = true; }
                Check(sharedOk && refused, $"rule S10: AddDs1RigFiles shares the bank between two {chrO.Ds1Chr} builds of decade {bankChr} ({rigA.AnimId}, {rigB.AnimId}: identical, kept once) and refuses DS1 {otherChr}'s project in the same decade ({other.Bank.Length:N0} B bank): {(sharedOk ? "shared" : "NOT shared")}, {(refused ? "refused" : "NOT refused")}");
                // genericity: c2540's own bank in its decade (2540, the enemies track's 2541xx-2543xx) passes the same rules
                var c2540 = otherChr == "c2540" ? ds1b : Build.Chr.Ds1ChrSource.Load(Program.DefaultDsrRoot, "c2540");
                var own = Build.Chr.Ds1ChrSound.Bank(Program.DefaultDsrRoot, ExtractDs2.DefaultOut, "c2540", 2540, "c1030", c2540.Tae);
                var ownProj = Formats.Fev1Project.Read(own.Fev);
                var (oa, ob) = Formats.Fev1Project.Counters(ownProj.SoundDefs);
                Check(ownProj.Name == "frpg2_c2540" && ownProj.Header == (oa, ob) && Build.Chr.Ds1ChrSound.BankChecks(ExtractDs2.DefaultOut, 2540).Count == 0 && own.Loose.Count >= 1,
                    $"generic: DS1 c2540 (Hollow Warrior) bank for decade 2540 builds: project {ownProj.Name}, {ownProj.Events.Count} events ({own.Commons.Count} DS1 common sounds copied), A/B = formula, loose {string.Join(", ", own.Loose.Keys)}, decade free");
                var free = Build.Chr.Ds1ChrSound.FreeDecades(ExtractDs2.DefaultOut, 2230);
                var taken = Build.Chr.Ds1ChrSound.BankChecks(ExtractDs2.DefaultOut, 2230);
                Check(taken.Count > 0 && free.Count > 0 && free.All(d => Build.Chr.Ds1ChrSound.BankChecks(ExtractDs2.DefaultOut, d).Count == 0),
                    $"rule S10 (boss track): decade 2230 (DS2's own bank; why merge9 stage 6 moved the Asylum Demon c2232 from animId 223210 to 221010) is refused ({string.Join("; ", taken)}); free decades nearby: {string.Join(", ", free)}");
            }
            // S7 writer: no DS1 effect is mapped, so the writer is exercised with a test map on a copy of the built _sfx TAE: every
            // FFX id of the DS1 idle a00_0000's and walk a00_0200's 109 (slot effect: c2500's eye glow on dummy 199, the two eye
            // dummies) and 112 (floor dust on the feet) events → DS2 1700; the result compared with the vanilla 2100 layout
            // (validate\sfx_survey.py: params 48 B, +12 = 1, +14/+15 = 0, unk 0, group type 2100; +0 = DS1's slot for slot
            // effects, -1 for one-shots; +13 = 1 exactly on dummy ids the DS1 model has more than once; one frame from the DS1 start)
            {
                var ds1Flv = FLVER2.Read(BND3.Read(Path.Combine(Program.DefaultDsrRoot, "chr", chrO.Ds1Chr + ".chrbnd.dcx")).Files.First(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
                var multi = ds1Flv.Dummies.GroupBy(d => (int)d.ReferenceID).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
                var idle = ClipFor(owner.TryGetValue(0, out var idleSlot) ? idleSlot : owner.First().Value); var walkSlot = owner.FirstOrDefault(x => x.Key == 30010).Value;
                var walk = walkSlot != null ? ClipFor(walkSlot) : idle;
                int walkShift = walkSlot != null && ds1.Has(200) ? Build.Chr.Ds1RigAnims.CycleStart(ds1.Bridge(200).Anim, pose) : 0;
                var testFfx = new[] { 0, 200 }.SelectMany(c => ds1.TaeAnim(c)?.Events ?? []).Where(x => x.Type is 109 or 112).Select(x => BitConverter.ToInt32(x.GetParameterBytes(false), 0)).ToHashSet();
                var testMap = testFfx.ToDictionary(f => f, _ => (1700, "test"));
                var tIdle = Build.Chr.Ds1ChrSound.Translate(ds1.TaeAnim(0), chrId, 0, idle.Frames, idle.Fps, null, testMap, multi);
                var tWalk = Build.Chr.Ds1ChrSound.Translate(ds1.TaeAnim(200), chrId, walkShift, walk.Frames, walk.Fps, null, testMap, multi);
                // expected per DS1 event (independently of the writer): slot / every-dummy / start; other DS1 one-shot / slot effect
                // kinds whose FFX id the test map happens to name are translated too and expected likewise
                var ds1Fx = new Dictionary<long, List<(float S, int Slot, int Dummy)>>();
                int slotEvents = 0;
                foreach (var (tid, did, shift, a) in new[] { (0L, 0, 0, idle), (30010L, 200, walkShift, walk) })
                {
                    var l = new List<(float, int, int)>();
                    float At(float start) { int f = (int)Math.Round(start * a.Fps), P = a.Frames - 1; return (float)((shift == 0 ? f : ((f - shift) % P + P) % P) / a.Fps); }
                    // 109 {FFX, dummy, slot} / 112 {FFX, dummy}: read from the raw parameters (TAE.Template.DS1.xml)
                    foreach (var ev in ds1.TaeAnim(did)?.Events.Where(x => x.Type is 109 or 112) ?? [])
                    {
                        var pr = ev.GetParameterBytes(false);
                        int slot = ev.Type == 109 ? BitConverter.ToInt32(pr, 8) : -1;
                        l.Add((At(ev.StartTime), slot, BitConverter.ToInt32(pr, 4)));
                        if (slot >= 0) slotEvents++;
                    }
                    // other DS1 spawn kinds whose FFX id the test map happens to name (the writer translates them too)
                    foreach (var fx in Build.Chr.Ds1ChrSound.Effects(ds1.TaeAnim(did)).Where(x => x.Event is not (109 or 112) && testMap.ContainsKey(x.Ffx)))
                    {
                        var (type, keepSlot) = Build.Chr.Ds1ChrSound.Ds2Effect(fx.Event);
                        if (type == 0) continue;
                        l.Add((At(fx.Start), keepSlot ? fx.Slot : -1, fx.Dummy));
                        if (keepSlot && fx.Slot >= 0) slotEvents++;
                    }
                    ds1Fx[tid] = [.. l.OrderBy(x => x.Item1).ThenBy(x => x.Item3)];
                }
                var sfxTest = new Formats.Ds2Tae(File.ReadAllBytes(sfxPath));
                sfxTest.SetEvents(0, tIdle.Sfx, Build.Chr.Ds1ChrSound.SfxGroup);
                if (sfxTest.Records.ContainsKey(30010)) sfxTest.SetEvents(30010, tWalk.Sfx, Build.Chr.Ds1ChrSound.SfxGroup);
                var bytes = sfxTest.Bytes;
                int wOk = 0, wBad = 0, nEvery = 0, nSlot = 0; string wNote = "";
                foreach (long tAnim in new long[] { 0, 30010 })
                {
                    if (!sfxTest.Records.TryGetValue(tAnim, out int rec)) continue;
                    long pEv = BitConverter.ToInt64(bytes, rec), pGr = BitConverter.ToInt64(bytes, rec + 8);
                    int ne = BitConverter.ToInt32(bytes, rec + 32);
                    var want = ds1Fx[tAnim];
                    if (ne == 0) { if (want.Count != 0) { wBad++; wNote = $"anim {tAnim}: 0 events, DS1 {want.Count}"; } continue; }
                    var datas = Enumerable.Range(0, ne).Select(k => BitConverter.ToInt64(bytes, (int)pEv + 24 * k + 16)).ToList();
                    var got = Enumerable.Range(0, ne).Select(k => (S: BitConverter.ToSingle(bytes, (int)BitConverter.ToInt64(bytes, (int)pEv + 24 * k)), E: BitConverter.ToSingle(bytes, (int)BitConverter.ToInt64(bytes, (int)pEv + 24 * k + 8)), D: (int)datas[k]))
                        .Select((x, k) => (x.S, x.E, x.D, Next: k + 1 < ne ? datas[k + 1] : pGr)).OrderBy(x => x.S).ThenBy(x => BitConverter.ToInt32(bytes, (int)BitConverter.ToInt64(bytes, x.D + 8) + 8)).ToList();
                    long groupData = BitConverter.ToInt64(bytes, (int)pGr + 16);
                    if (got.Count != want.Count) { wBad++; wNote = $"anim {tAnim}: {got.Count} events, DS1 {want.Count}"; continue; }
                    for (int k = 0; k < ne; k++)
                    {
                        var (gs, ge, d, next) = got[k]; var wx = want[k];
                        int type = BitConverter.ToInt32(bytes, d), unk = BitConverter.ToInt32(bytes, d + 4);
                        long pp = BitConverter.ToInt64(bytes, d + 8);
                        int size = (int)(next - pp), slot = BitConverter.ToInt32(bytes, (int)pp), dummy = BitConverter.ToInt32(bytes, (int)pp + 8);
                        bool every = multi.Contains(wx.Dummy);
                        bool ok = type == 2100 && unk == 0 && pp == d + 16 && size == 48 && slot == wx.Slot && dummy == wx.Dummy && BitConverter.ToInt32(bytes, (int)pp + 4) == 1700
                            && bytes[pp + 12] == 1 && bytes[pp + 13] == (every ? 1 : 0) && bytes[pp + 14] == 0 && bytes[pp + 15] == 0
                            && Math.Abs(gs - wx.S) < 1e-4 && Math.Abs(ge - gs - 1 / 30f) < 1e-4 && BitConverter.ToInt32(bytes, (int)groupData) == 2100;
                        if (ok) { wOk++; if (every) nEvery++; if (slot >= 0) nSlot++; }
                        else { wBad++; wNote = $"anim {tAnim} event {k}: type {type} unk {unk} params {size} B slot {slot} (DS1 {wx.Slot}) dummy {dummy} +13 {bytes[pp + 13]} @{gs:0.###}-{ge:0.###} (DS1 @{wx.S:0.###})"; }
                    }
                }
                bool sfReads;
                try { var sf = TAE.Read(bytes); sfReads = sf.Animations.First(a => a.ID == 0).Events.Count == tIdle.Sfx.Count && (!sfxTest.Records.ContainsKey(30010) || sf.Animations.First(a => a.ID == 30010).Events.Count == tWalk.Sfx.Count); } catch { sfReads = false; }
                Check(wBad == 0 && (wOk > 0 || testFfx.Count == 0) && nSlot == slotEvents && sfReads,   // session 11 (burgchr11): vacuous for a character without DS1 effect events in idle / walk (c2370)
                    $"rule S7 writer (test map {string.Join("/", testFfx.Order())} → DS2 1700, in memory only): idle {tIdle.Sfx.Count} + walk {tWalk.Sfx.Count} DS1 effect events → {wOk} DS2 2100 events in the vanilla layout (params 48 B, +12 = 1, +14/+15 = 0, unk 0, group 2100), one frame at DS1's (loop-shifted) start; {nSlot} keep DS1's slot (slot effects), the rest slot -1 (one-shots, 112); {nEvery} spawn at every dummy of their id (+13 = 1: DS1 dummy ids used twice: {string.Join(",", multi.Order())}); SoulsFormats reads them{(wBad > 0 ? $" - {wBad} bad, e.g. {wNote}" : "")}");
            }
        }
        // ---- 7. a built pack (--pack DIR): the 1:1 files are in it, EnemyParam 250000 points at them
        string packDir = args.SkipWhile(a => a != "--pack").Skip(1).FirstOrDefault() ?? "";
        if (packDir != "")
        {
            foreach (string device in new[] { "gamedata", "title" })
            {
                string root = Path.Combine(dir, device);
                if (!Directory.Exists(root)) continue;
                foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(root, f);
                    string pf = Path.Combine(packDir, device, rel);
                    Check(File.Exists(pf) && File.ReadAllBytes(pf).AsSpan().SequenceEqual(File.ReadAllBytes(f)), $"pack has {device}/{rel.Replace('\\', '/')} ({(File.Exists(pf) ? new FileInfo(pf).Length : 0):N0} bytes, identical to the build's)");
                }
            }
            var reg = BND4.Read(Formats.Ds2Regulation.Decrypt(Path.Combine(packDir, "title", "enc_regulation.bnd.dcx")));
            var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
            var ep = PARAM.Read(reg.Files.First(f => Path.GetFileNameWithoutExtension(f.Name) == "EnemyParam").Bytes);
            ep.ApplyParamdefCarefully(defs);
            foreach (int id in chrRig.EnemyParams)
            {
                var row = ep[id];
                Check(row != null && Convert.ToInt32(row["modelId"].Value) == chrRig.Model && Convert.ToInt32(row["animId"].Value) == chrRig.AnimId, $"pack EnemyParam {id} = model {chrRig.Model}, animId {chrRig.AnimId} (has {row?["modelId"].Value} / {row?["animId"].Value})");
            }
            if (chrRig.Attacks != null) Ds1AttackCheck.Pack(reg, defs, chrRig, Check);
        }
        // ---- 8. the DS1 attack layer (track enemies)
        if (chrRig.Attacks != null) Ds1AttackCheck.Network(dir, chrO, chrRig, ds1, skel, Check);
        Console.WriteLine(fails == 0 ? "ALL CHECKS PASSED" : $"{fails} CHECK(S) FAILED");
        return fails == 0 ? 0 : 2;
    }

    /// <summary><c>steps-calib ANIBND</c>: our foot-plant detector (Ds1RigAnims.Planted) run on a DS2 anibnd's own clips
    /// and compared with DS2's authored LeftSteps/RightSteps windows (intersection over union of the window coverage), for
    /// a few speed/height thresholds: calibration evidence for the DS1 → DS2 foot-window rule.</summary>
    public static int StepsCalib(string[] args)
    {
        var files = MorphemeTools.LoadFiles(args[1]);
        var nmb = NmbFile.Read(files.First(f => f.Name.EndsWith(".nmb")).Bytes);
        var rig = NmbRig.Read(nmb.Bundles[nmb.IndexOf(1)].Data);
        var map = NmbChannelMap.Read(nmb.Bundles[nmb.IndexOf(2)].Data);
        var pose = MorphemePose.From(rig, map);
        int lf = pose.Index("L_Foot"), rf = pose.Index("R_Foot");
        var net = new NmbNetwork(nmb.Bundles[nmb.IndexOf(10)].Data);
        var fn = NmbFileNames.Read(nmb.Bundles[nmb.IndexOf(13)].Data).Anims;
        var tracks = new Dictionary<uint, NmbEventTrack>();
        foreach (var b in nmb.Bundles.Where(b => b.Type is 3 or 4 or 5)) tracks[b.AssetId] = NmbEventTrack.Read(b.Data, b.Type);
        var authored = new Dictionary<int, (List<(float, float, int)> L, List<(float, float, int)> R)>();
        foreach (var node in net.Nodes().Where(x => x.Type == 104))
        {
            var sa = NmbNetwork.Find(node, 21); var et = NmbNetwork.Find(node, 23);
            if (sa == null || et == null) continue;
            int ci = net.ClipIndex(sa);
            if (authored.ContainsKey(ci)) continue;
            List<(float, float, int)>? L = null, R = null;
            foreach (var (_, tid, _) in net.Tracks(et))
                if (tracks.TryGetValue(tid, out var t)) { if (t.Name == "LeftSteps") L = t.Events; else if (t.Name == "RightSteps") R = t.Events; }
            if (L != null && R != null) authored[ci] = (L, R);
        }
        double Cover(List<(float S, float D, int V)> ev, double x) => ev.Any(e => x >= e.S - 1e-6 && x <= e.S + e.D + 1e-6) ? 1 : 0;
        var clipsOf = new Dictionary<int, NsaAnim>();
        foreach (var ci in authored.Keys)
        {
            var a = NsaCodec.Decode(NsaFile.Parse(files.First(f => f.Name.Equals(fn[ci], StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(f.Name) == fn[ci]).Bytes));
            if (a.Frames >= 3) clipsOf[ci] = a;
        }
        Console.WriteLine("(1) the session-8 step-2 detector (speed + height only):");
        foreach (var (spd, hgt) in new[] { (0.2, 0.03), (0.35, 0.05), (0.5, 0.05), (0.5, 0.08), (0.8, 0.08) })
        {
            double inter = 0, uni = 0; int n = 0;
            foreach (var (ci, (L, R)) in authored)
            {
                if (!clipsOf.TryGetValue(ci, out var a)) continue;
                var pl = Build.Chr.Ds1RigAnims.Planted(a, pose, lf, spd, hgt); var pr = Build.Chr.Ds1RigAnims.Planted(a, pose, rf, spd, hgt);
                double last = a.Frames - 1;
                for (int f = 0; f < a.Frames; f++)
                {
                    double x = f / last;
                    bool dl = pl.Any(r => f >= r.A && f <= r.B), dr = pr.Any(r => f >= r.A && f <= r.B);
                    bool al = Cover(L, x) > 0, ar = Cover(R, x) > 0;
                    inter += (dl && al ? 1 : 0) + (dr && ar ? 1 : 0); uni += (dl || al ? 1 : 0) + (dr || ar ? 1 : 0);
                }
                n++;
            }
            Console.WriteLine($"  speed < {spd} m/s, height < min + {hgt} m: {n} clips with authored windows, coverage IoU {inter / Math.Max(1, uni):0.000}");
        }

        // (2) fix pass: how still DS2 keeps each candidate lock joint inside its own authored windows (drift from the joint's
        // position at the window start, trajectory applied; a cyclic clip's window may run past its end into the next cycle)
        bool Cyclic(NsaAnim a)
        {
            int P = a.Frames - 1;
            for (int c = 2; c < a.ChannelCount; c++) if (MorphemeMath.AngleDeg(a.Rot[c][0], a.Rot[c][P]) > 2.0) return false;
            return true;
        }
        static string Dist(List<double> v) { if (v.Count == 0) return "-"; var s = v.Order().ToList(); double Q(double q) => s[Math.Min(s.Count - 1, (int)(q * s.Count))] * 1000; return $"median {Q(0.5):0.0} mm, p75 {Q(0.75):0.0}, p90 {Q(0.9):0.0}, max {s[^1] * 1000:0.0} ({v.Count} windows)"; }
        Console.WriteLine("(2) drift of each chain joint inside DS2's authored LeftSteps/RightSteps windows (LOCK_FOOT_CHAIN: thigh, calf, ankle, ball, toe):");
        foreach (var (jl, jr) in new[] { ("L_Foot", "R_Foot"), ("L_Toe0", "R_Toe0"), ("L_Toe0Nub", "R_Toe0Nub") })
        {
            int il = pose.Index(jl), ir = pose.Index(jr);
            if (il < 0 || ir < 0) continue;
            var drift = new List<double>();
            var perClip = new List<string>();
            foreach (var (ci, (L, R)) in authored)
            {
                if (!clipsOf.TryGetValue(ci, out var a)) continue;
                bool cyc = Cyclic(a); int P = a.Frames - 1;
                double worst = 0;
                foreach (var (ev, j) in new[] { (L, il), (R, ir) })
                {
                    var path = Build.Chr.Ds1RigAnims.JointPath(a, pose, j, cyc);
                    foreach (var (s, d, _) in ev)
                    {
                        if (d <= 0) continue;
                        int fa = (int)Math.Round(s * P), fb = (int)Math.Round((s + d) * P);
                        if (!cyc) fb = Math.Min(fb, P);
                        double dr = Build.Chr.Ds1RigAnims.WindowDrift(path, fa, fb);
                        drift.Add(dr); worst = Math.Max(worst, dr);
                    }
                }
                perClip.Add($"{fn[ci][..Math.Min(30, fn[ci].Length)]}{(cyc ? " (cyclic)" : "")} {worst * 1000:0.0}");
            }
            Console.WriteLine($"  {jl}/{jr}: {Dist(drift)}");
            if (args.Contains("-v")) Console.WriteLine("     worst per clip (mm): " + string.Join("; ", perClip));
        }

        // (3) fix pass: the windows our rule builds (Ds1RigAnims.LockWindows) on the same clips, per lock joint and tolerance
        Console.WriteLine("(3) Ds1RigAnims.LockWindows (touchdown = speed < 0.35 m/s and within 5 cm of the lowest point; window = while within tol of the touchdown position):");
        foreach (var (jl, jr) in new[] { ("L_Toe0", "R_Toe0"), ("L_Foot", "R_Foot") })
            foreach (double tol in new[] { 0.01, 0.015, 0.02, 0.03 })
            {
                int il = pose.Index(jl), ir = pose.Index(jr);
                double inter = 0, uni = 0; var drift = new List<double>(); int n = 0;
                foreach (var (ci, (L, R)) in authored)
                {
                    if (!clipsOf.TryGetValue(ci, out var a)) continue;
                    bool cyc = Cyclic(a); int P = a.Frames - 1; n++;
                    foreach (var (ev, j) in new[] { (L, il), (R, ir) })
                    {
                        var path = Build.Chr.Ds1RigAnims.JointPath(a, pose, j, cyc);
                        var w = Build.Chr.Ds1RigAnims.LockWindows(path, P, cyc, a.Fps, tol: tol);
                        foreach (var (s, e) in w) drift.Add(Build.Chr.Ds1RigAnims.WindowDrift(path, s, e));
                        int frames = cyc ? P : P + 1;
                        for (int f = 0; f < frames; f++)
                        {
                            bool dw = w.Any(x => (f >= x.A && f <= x.B) || (cyc && f + P >= x.A && f + P <= x.B));
                            double xf = (double)f / P;
                            bool aw = ev.Any(e => e.Item2 > 0 && ((xf >= e.Item1 - 1e-6 && xf <= e.Item1 + e.Item2 + 1e-6) || (cyc && xf + 1 >= e.Item1 - 1e-6 && xf + 1 <= e.Item1 + e.Item2 + 1e-6)));
                            inter += dw && aw ? 1 : 0; uni += dw || aw ? 1 : 0;
                        }
                    }
                }
                Console.WriteLine($"  {jl}/{jr} tol {tol * 1000:0} mm: {n} clips, coverage IoU {inter / Math.Max(1, uni):0.000}; drift in our windows {Dist(drift)}");
            }
        return 0;
    }

    /// <summary>
    /// <c>pose-view MODEL.bnd ANIBND CLIP OUT.png [--frames 0,10,20] [--side] [--size N]</c>: the model skinned by a clip,
    /// bound the way DS2 binds it (FLVER node ← Morpheme rig bone of the same name; rig pose mapped to FLVER space by the
    /// reflection (x, y, z) → (x, z, y); linear blend skinning with the FLVER bind), rendered like flver-view, one tile per
    /// frame (no root motion). Offline check for "stretched / T-posed" before the game.
    /// </summary>
    public static int PoseView(string[] args)
    {
        var pos = args.Skip(1).Where((a, i) => !a.StartsWith("--") && (i == 0 || !args[i].StartsWith("--frames") && !args[i].StartsWith("--size") && !args[i].StartsWith("--height"))).ToList();
        string modelPath = args[1], anibnd = args[2], clipName = args[3], outPng = args[4];
        bool side = args.Contains("--side");
        int size = args.SkipWhile(a => a != "--size").Skip(1).Select(int.Parse).FirstOrDefault(320);
        byte[] mb = File.ReadAllBytes(modelPath);
        if (DCX.Is(mb)) mb = DCX.Decompress(mb);
        var flv = FLVER2.Read(BND4.Read(mb).Files.First(f => f.Name.EndsWith(".flv") && !f.Name.EndsWith("_s.flv")).Bytes);
        var files = MorphemeTools.LoadFiles(anibnd);
        var nmb = NmbFile.Read(files.First(f => f.Name.EndsWith(".nmb")).Bytes);
        var rig = NmbRig.Read(nmb.Bundles[nmb.IndexOf(1)].Data);
        var map = NmbChannelMap.Read(nmb.Bundles[nmb.IndexOf(2)].Data);
        var pose = MorphemePose.From(rig, map);
        var clip = NsaCodec.Decode(NsaFile.Parse(files.First(f => f.Name.Contains(clipName, StringComparison.OrdinalIgnoreCase) && f.Name.EndsWith(".nsa")).Bytes));
        var frames = args.SkipWhile(a => a != "--frames").Skip(1).FirstOrDefault() is { } fs ? fs.Split(',').Select(int.Parse).ToList() : [0, clip.Frames / 4, clip.Frames / 2, 3 * clip.Frames / 4, clip.Frames - 1];
        var bind = ChrCompare.World(flv);
        var inv = bind.Select(m => Matrix4x4.Invert(m, out var i) ? i : Matrix4x4.Identity).ToArray();
        var rigIndex = flv.Nodes.Select(n => rig.Names.IndexOf(n.Name)).ToArray();
        int bound = rigIndex.Count(i => i >= 0);
        var sheet = new Textures.Image(size * frames.Count, size);
        for (int i = 0; i < sheet.Pixels.Length; i++) sheet.Pixels[i] = new Textures.Rgba(30, 30, 40, 255);
        var depth = new float[sheet.Pixels.Length];
        Array.Fill(depth, float.MinValue);
        // --height H (track boss): the drawn height in metres (default 2.2, a human; the Asylum Demon needs ~6)
        float height = args.SkipWhile(a => a != "--height").Skip(1).Select(float.Parse).FirstOrDefault(2.2f);
        float scale = size / height;
        double maxStretch = 0;
        var ratios = new List<double>();
        for (int k = 0; k < frames.Count; k++)
        {
            int f = Math.Clamp(frames[k], 0, clip.Frames - 1);
            var (wp, wq) = pose.World(clip, f, false);
            var skin = new Matrix4x4[flv.Nodes.Count];
            for (int i = 0; i < flv.Nodes.Count; i++)
            {
                int r = rigIndex[i];
                if (r < 0) { skin[i] = Matrix4x4.Identity; continue; }
                var q = wq[r]; var p = wp[r];
                var qf = new System.Numerics.Quaternion((float)-q.X, (float)-q.Z, (float)-q.Y, (float)q.W); // S q S: (w, -x, -z, -y)
                var anim = Matrix4x4.CreateFromQuaternion(qf) * Matrix4x4.CreateTranslation((float)p.X, (float)p.Z, (float)p.Y);
                skin[i] = inv[i] * anim;
            }
            float ox = k * size;
            (float X, float Y, float Z) Proj(Vector3 v) => side ? (ox + size / 2f + v.Z * scale, size - 4 - v.Y * scale, v.X) : (ox + size / 2f - v.X * scale, size - 4 - v.Y * scale, -v.Z);
            foreach (var m in flv.Meshes)
            {
                var sv = m.Vertices.Select(v =>
                {
                    var acc = Vector3.Zero; var nrm = Vector3.Zero; float ws = 0;
                    for (int j = 0; j < 4; j++)
                    {
                        float wgt = v.BoneWeights[j];
                        if (wgt <= 0) continue;
                        int node = m.BoneIndices.Count > 0 ? m.BoneIndices[v.BoneIndices[j]] : v.BoneIndices[j];
                        acc += wgt * Vector3.Transform(v.Position, skin[node]);
                        nrm += wgt * Vector3.TransformNormal(v.Normal, skin[node]);
                        ws += wgt;
                    }
                    return ws > 0 ? (acc / ws, Vector3.Normalize(nrm)) : (v.Position, v.Normal);
                }).ToList();
                // edge stretch vs bind (rigid parts should keep their edge lengths)
                foreach (var fsx in m.FaceSets.Where(x => x.Flags == FLVER2.FaceSet.FSFlags.None).Take(1))
                {
                    var idx = fsx.Triangulate(m.Vertices.Count < ushort.MaxValue);
                    for (int i = 0; i + 2 < idx.Count; i += 3)
                    {
                        var (a, na) = sv[idx[i]]; var (b, nb) = sv[idx[i + 1]]; var (c, nc) = sv[idx[i + 2]];
                        float l0 = Vector3.Distance(m.Vertices[idx[i]].Position, m.Vertices[idx[i + 1]].Position), l1 = Vector3.Distance(a, b);
                        if (l0 > 0.01f) { ratios.Add(l1 / l0); maxStretch = Math.Max(maxStretch, l1 / l0); } // edges of 1 cm or more
                        var nn = Vector3.Normalize(na + nb + nc);
                        var light = Vector3.Normalize(side ? new Vector3(0.3f, 0.6f, 1) : new Vector3(-0.4f, 0.6f, -1));
                        float lt = Math.Clamp(0.25f + 0.75f * Math.Max(0, Vector3.Dot(nn, light)), 0, 1);
                        TriClip(sheet, depth, Proj(a), Proj(b), Proj(c), new Textures.Rgba((byte)(200 * lt), (byte)(190 * lt), (byte)(170 * lt), 255), ox, ox + size);
                    }
                }
            }
        }
        Textures.Png.Save(sheet, outPng);
        Console.WriteLine($"{outPng}: {frames.Count} frames of {clipName} ({clip.Frames} frames @ {F(clip.Fps)} fps), {bound}/{flv.Nodes.Count} FLVER nodes bound to rig bones by name, edge length vs bind (edges ≥ 1 cm): max {F(maxStretch, "0.00")}x, 99th percentile {F(ratios.Count > 0 ? ratios.Order().ElementAt((int)(ratios.Count * 0.99)) : 0, "0.00")}x");
        return 0;
    }

    static void TriClip(Textures.Image img, float[] depth, (float X, float Y, float Z) a, (float X, float Y, float Z) b, (float X, float Y, float Z) c, Textures.Rgba col, float x0, float x1)
    {
        int minX = (int)Math.Max(x0, Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)))), maxX = (int)Math.Min(x1 - 1, Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
        int minY = (int)Math.Max(0, Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)))), maxY = (int)Math.Min(img.Height - 1, Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
        float d = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (Math.Abs(d) < 1e-6f) return;
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float l1 = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / d, l2 = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / d, l3 = 1 - l1 - l2;
                if (l1 < 0 || l2 < 0 || l3 < 0) continue;
                float z = l1 * a.Z + l2 * b.Z + l3 * c.Z;
                int k = y * img.Width + x;
                if (z <= depth[k]) continue;
                depth[k] = z;
                img[x, y] = col;
            }
    }

    /// <summary>
    /// <c>ds1-chr-bank DS1CHR ANIMID OUTDIR</c> (track chrsound fix pass, rules S1-S10): the DS2 sound bank a 1:1 build of
    /// any DS1 character at that animId would ship (gamedata/sound/frpg2_cBBBB.bnd + title/sound/*.fsb), without building
    /// the rest of the character, so validate\fmod_play.py / fmod_resolve.py can check another character's DS1 project
    /// (e.g. c2540, the Hollow Warrior of the enemies track) before its 1:1 port exists. Refuses decades S10 refuses.
    /// </summary>
    public static int Ds1ChrBank(string[] args)
    {
        if (args.Length < 4) { Console.Error.WriteLine("usage: ds1-chr-bank DS1CHR ANIMID OUTDIR"); return 1; }
        string chr = args[1], outDir = args[3];
        int animId = int.Parse(args[2], Inv), bankChr = Ds1ChrSound.BankChr(animId);
        var why = Ds1ChrSound.BankChecks(ExtractDs2.DefaultOut, bankChr);
        if (why.Count > 0)
        {
            Console.Error.WriteLine($"decade {bankChr}: {string.Join("; ", why)}; free decades near it: {string.Join(", ", Ds1ChrSound.FreeDecades(ExtractDs2.DefaultOut, bankChr))}");
            return 2;
        }
        var src = Ds1ChrSource.Load(Program.DefaultDsrRoot, chr);
        var b = Ds1ChrSound.Bank(Program.DefaultDsrRoot, ExtractDs2.DefaultOut, chr, bankChr, "c1030", src.Tae);
        string bankPath = Path.Combine(outDir, "gamedata", "sound", Ds1ChrSound.BankName(bankChr) + ".bnd");
        Directory.CreateDirectory(Path.GetDirectoryName(bankPath)!);
        File.WriteAllBytes(bankPath, b.Bank);
        foreach (var (path, bytes) in b.Loose)
        {
            string p = Path.Combine(outDir, "title", path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllBytes(p, bytes);
        }
        foreach (var l in b.Report) Console.WriteLine(l);
        return 0;
    }

    public static int ClipSurvey(string[] args)
    {
        string src = args[1];
        string filter = args.SkipWhile(a => a != "--filter").Skip(1).FirstOrDefault() ?? "";
        byte[] raw = File.ReadAllBytes(src);
        if (DCX.Is(raw)) raw = DCX.Decompress(raw);
        if (BND3.Is(raw))
        {
            var ds1 = Ds1ChrSource.FromAnibnd(BND3.Read(raw));
            foreach (int id in ds1.ClipIds)
            {
                string name = Ds1ChrSource.ClipName(id);
                if (filter != "" && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                var res = ds1.Bridge(id);
                var pose = MorphemePose.From(res.Skeleton);
                var s = Stats(name, res.Anim, pose, "Master", "Pelvis", "Head", "L Foot", "R Foot");
                string tae = ds1.Tae?.Animations.FirstOrDefault(x => x.ID == id) is { } ta ? $" tae[{string.Join(",", ta.Events.Select(e => e.Type).Distinct().OrderBy(x => x))}]" : " (no TAE entry)";
                Console.WriteLine(Line(s) + tae);
            }
            return 0;
        }
        var files = BND4.Read(raw).Files;
        var nmbBytes = files.First(f => f.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)).Bytes;
        var nmb = NmbFile.Read(nmbBytes);
        var rig = NmbRig.Read(nmb.Bundles[nmb.IndexOf(1)].Data);
        var map = NmbChannelMap.Read(nmb.Bundles[nmb.IndexOf(2)].Data);
        var p2 = MorphemePose.From(rig, map);
        string Pick(params string[] c) => c.First(x => rig.Names.Contains(x));
        string root = Pick("Root", "Master"), pel = Pick("Pelvis"), head = Pick("Head"), lf = Pick("L_Foot", "L Foot"), rf2 = Pick("R_Foot", "R Foot");
        foreach (var f in files.Where(f => f.Name.EndsWith(".nsa", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(f.Name.Replace('\\', '/'));
            if (filter != "" && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            var a = NsaCodec.Decode(NsaFile.Parse(f.Bytes));
            Console.WriteLine(Line(Stats(name.Length > 34 ? name[..34] : name, a, p2, root, pel, head, lf, rf2)));
        }
        return 0;
    }
}
