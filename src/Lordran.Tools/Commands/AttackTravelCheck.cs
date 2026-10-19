using System.Globalization;
using System.Text.RegularExpressions;
using Lordran.Tools.Build.Chr;
using Lordran.Tools.Formats.Morpheme;
using SoulsFormats;
using BossTae = Lordran.Tools.Formats.Boss.Ds2TaeFile;

namespace Lordran.Tools.Commands;

/// <summary>
/// <c>attack-travel-check PACKDIR [--dsr DSRROOT] [--profile cXXXX:a00_3007] [--all]</c> (session 11, track leap12): every DS1
/// attack a 1:1 character (regulars and the boss pipeline) plays in a built pack travels as far as in DS1.
/// <para>User report (build _base39, Firelink m19_42): "the one way the undead guy jumps at me, he just flew crazy far off the side
/// of the cliff". The clips themselves carry DS1's root motion 1:1 (the Hollow Warrior's leaping slashes a00_3005/3006/3007:
/// 3.95 / 6.79 / 9.07 m, NSA = DS1 reference frame); what DS2 adds is its fall state: an enemy that loses the ground for the
/// engine's air time (DarkSoulsII.c FUN_140372xxx l.682682-682690) gets IsFall (chr+0x4c8 bit 12 â†’ CP IsFall, FUN_140362c40),
/// and c1030's SM_Main T[9] "ANY â†’ fall when IsFall" has no exemption for SM_Attack, so a leap that runs down a step or over an
/// edge becomes a DS2 fall with the leap's speed. DS1 marks those windows itself: TAE JumpTable 51 "Disable Fall Animation"
/// (c2540 a00_3005-3007 0.3-0.833 s, the jump of the leap). DS2's counterpart is TAE event 280000 (group 280000, handler
/// FUN_14016c150 l.268786: +1 on the physics counter +0x108, which FUN_14036da20 reads as "on the ground", so no air time and no
/// IsFall; 16 zero bytes as in vanilla, e.g. c509000 65111010) â€” rule LP1 (Ds1Attacks / Ds1Boss TimeAct).</para>
/// <list type="bullet">
/// <item>T1 travel: the pack NSA's trajectory (horizontal end distance) = the DS1 clip's reference frame within max(3 cm, 1 %).</item>
/// <item>T2 Morpheme: the anim node's stored trajectory end and duration = its NSA (Morpheme's delta source).</item>
/// <item>T3 LP1: every DS1 JT 51 window of the clip is covered (â‰¥ 95 %) by 280000 events in the leaf's TimeAct animation(s) or by a
/// 9610 Gravity window on the leaf.</item>
/// <item>T4 gravity: every DS1 JT 27 "Ignore Gravity" window is covered by a 9610 Gravity window on the leaf (rule C12d) â€” reported
/// as KNOWN for attack leaves without a Gravity track (a separate rule; the regular attack layer has none).</item>
/// </list>
/// Leaves checked: every anim node whose clip is a DS1 attack clip (regular attack layer name a65_AA_BBBB, else a clip whose
/// TimeAct animation carries a hit 2200 or bullet 2300), matched to its DS1 clip by name or by frames + trajectory end. Prints
/// one line per DS1 clip (travel, peak speed, travel inside the JT 51 windows); --all also lists clips without root motion.
/// Exit 1 on any FAIL.
/// </summary>
public static class AttackTravelCheck
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static string F(double v, string f = "0.00") => v.ToString(f, Inv);

    public sealed class Ds1Clip
    {
        public int Id; public int Frames; public double Fps, Duration;
        public V2 End; public double Travel, Path;
        public List<(double S, double E)> NoFall = [], NoGravity = [];
        public double[] Px = [], Py = [];
    }
    public readonly record struct V2(double X, double Y) { public double Len => Math.Sqrt(X * X + Y * Y); }

    static Ds1Clip Facts(Ds1ChrSource ds1, int id)
    {
        var b = ds1.Bridge(id).Anim;
        var c = new Ds1Clip { Id = id, Frames = b.Frames, Fps = b.Fps, Duration = (b.Frames - 1) / b.Fps };
        c.Px = b.RootPos.Select(p => (double)p.X).ToArray(); c.Py = b.RootPos.Select(p => (double)p.Y).ToArray();
        c.End = new(c.Px[^1] - c.Px[0], c.Py[^1] - c.Py[0]); c.Travel = c.End.Len;
        for (int f = 1; f < c.Px.Length; f++) c.Path += new V2(c.Px[f] - c.Px[f - 1], c.Py[f] - c.Py[f - 1]).Len;
        var tae = ds1.TaeAnim(id);
        c.NoFall = Ds1RigAnims.JumpTable(tae, 51); c.NoGravity = Ds1RigAnims.JumpTable(tae, 27);
        return c;
    }

    /// <summary>Horizontal distance covered between times s and e (seconds) of the sampled trajectory.</summary>
    static double Between(double[] px, double[] py, double fps, double s, double e)
    {
        int a = Math.Clamp((int)Math.Round(s * fps), 0, px.Length - 1), b = Math.Clamp((int)Math.Round(e * fps), 0, px.Length - 1);
        double d = 0; for (int f = a + 1; f <= b; f++) d += new V2(px[f] - px[f - 1], py[f] - py[f - 1]).Len;
        return d;
    }
    /// <summary>Peak horizontal speed over 0.1 s windows (m/s).</summary>
    static double Peak(double[] px, double[] py, double fps)
    {
        int w = Math.Max(1, (int)Math.Round(0.1 * fps)); double best = 0;
        for (int f = w; f < px.Length; f++) best = Math.Max(best, new V2(px[f] - px[f - w], py[f] - py[f - w]).Len * fps / w);
        return best;
    }
    static double Covered(List<(double S, double E)> want, List<(double S, double E)> have)
    {
        double tot = 0, cov = 0;
        foreach (var (s, e) in want)
        {
            tot += e - s;
            const int n = 200;
            for (int k = 0; k < n; k++) { double t = s + (e - s) * (k + 0.5) / n; if (have.Any(h => t >= h.S - 1e-3 && t <= h.E + 1e-3)) cov += (e - s) / n; }
        }
        return tot > 0 ? cov / tot : 1;
    }

    sealed record Target(string Key, string Ds1Chr, string Tae, string What);

    static List<Target> Targets()
    {
        ChrPorter.Burg = true; ChrPorter.Depths = true;
        var t = new List<Target>();
        foreach (var o in ChrPorter.All)
            foreach (var r in o.Rigs)
                t.Add(new($"c{r.AnimId / 100:D4}", o.Ds1Chr, $"c{r.AnimId}_pl.tae", "regular"));
        foreach (var s in new[] { Ds1Boss.AsylumDemon, Ds1Boss.StrayDemon }.Concat(Ds1Boss.Burg).Concat(Ds1Boss.Depths))
            t.Add(new($"c{s.AnimId / 100:D4}", s.Ds1Chr, $"c{s.AnimId}_pl.tae", "boss pipeline"));
        return [.. t.GroupBy(x => x.Key + x.Tae).Select(g => g.First())];
    }

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: attack-travel-check PACKDIR [--dsr DSRROOT] [--profile cXXXX:a00_3007] [--all]"); return 1; }
        string pack = args[1];
        string dsr = args.SkipWhile(a => a != "--dsr").Skip(1).FirstOrDefault() ?? Program.DefaultDsrRoot;
        string? profile = args.SkipWhile(a => a != "--profile").Skip(1).FirstOrDefault();
        bool all = args.Contains("--all");
        string chrDir = Path.Combine(pack, "gamedata", "morpheme4", "chr"), taeDir = Path.Combine(pack, "gamedata", "timeact", "chr");
        int fails = 0, known = 0, clipsChecked = 0, leavesChecked = 0;
        var ds1Cache = new Dictionary<string, Ds1ChrSource>();
        var nameRx = new Regex(@"^a65_(\d\d)_(\d{4})_untitled_", RegexOptions.IgnoreCase); var bossRx = new Regex(@"^c\d{4}_a(\d\d)_(\d{4})_untitled_", RegexOptions.IgnoreCase);
        foreach (var tg in Targets())
        {
            string anibnd = Path.Combine(chrDir, tg.Key + ".anibnd.dcx"), taePath = Path.Combine(taeDir, tg.Tae);
            if (!File.Exists(anibnd) || !File.Exists(taePath)) continue;
            if (!ds1Cache.TryGetValue(tg.Ds1Chr, out var ds1)) ds1Cache[tg.Ds1Chr] = ds1 = Ds1ChrSource.Load(dsr, tg.Ds1Chr);
            var files = MorphemeTools.LoadFiles(anibnd);
            var nmb = NmbFile.Read(files.First(f => f.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)).Bytes);
            var net = new NmbNetwork(nmb.Bundles[nmb.IndexOf(10)].Data);
            var clipNames = NmbFileNamesFull.Read(nmb.Bundles[nmb.IndexOf(13)].Data).Files;
            var tracks = nmb.Bundles.Where(b => b.Type is 3 or 4 or 5).GroupBy(b => b.AssetId).ToDictionary(g => g.Key, g => NmbEventTrack.Read(g.First().Data, g.First().Type));
            var tae = BossTae.Read(File.ReadAllBytes(taePath));
            var taeById = tae.Animations.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First());
            var nsa = new Dictionary<string, NsaAnim>(StringComparer.OrdinalIgnoreCase);
            NsaAnim? Clip(string name)
            {
                if (nsa.TryGetValue(name, out var a)) return a;
                var f = files.FirstOrDefault(x => x.Name.EndsWith(".nsa", StringComparison.OrdinalIgnoreCase) && (Path.GetFileName(x.Name.Replace('\\', '/')).Equals(name, StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(x.Name.Replace('\\', '/')).Equals(name, StringComparison.OrdinalIgnoreCase)));
                return nsa[name] = f.Bytes == null ? null! : NsaCodec.Decode(NsaFile.Parse(f.Bytes));
            }
            // DS1 attack-family clips (3000-3999, steps 700-799, any offset set) for fingerprint matching on the boss networks
            var ds1Facts = new Dictionary<int, Ds1Clip>();
            Ds1Clip D(int id) { if (!ds1Facts.TryGetValue(id, out var c)) ds1Facts[id] = c = Facts(ds1, id); return c; }
            var family = ds1.ClipIds.Where(id => id % 10000 is >= 3000 and < 4000 or >= 700 and < 800).ToList();
            var rows = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            var rowFail = new HashSet<string>();
            foreach (var node in net.Nodes().Where(n => n.Type == 104))
            {
                var sa = NmbNetwork.Find(node, 21); if (sa == null) continue;
                int ci = net.ClipIndex(sa); if (ci >= clipNames.Count) continue;
                string cname = clipNames[ci];
                var et = NmbNetwork.Find(node, 23);
                var taeIds = new List<long>(); var gravity = new List<(double S, double E)>();
                if (et != null)
                    foreach (var (_, tid, _) in net.Tracks(et))
                        if (tracks.TryGetValue(tid, out var tr))
                        {
                            if (tr.Name == "TimeAct" || tr.UserData == 1000) taeIds.AddRange(tr.Events.Select(e => (long)e.Value));
                            if (tr.Events.Count > 0 && tr.Events.All(e => e.Value == 9610)) gravity.AddRange(tr.Events.Select(e => ((double)e.Start, (double)(e.Start + e.Duration))));
                        }
                var anims = taeIds.Distinct().Select(i => taeById.GetValueOrDefault(i)).OfType<BossTae.Animation>().ToList();
                bool isAttack = anims.Any(a => a.Events.Any(e => e.Type is 2200 or 2300));
                var m = nameRx.Match(cname);
                if (!m.Success && (!isAttack || tg.What != "boss pipeline")) continue;   // regulars: the DS1 attack layer (Ds1Attacks names); retired template categories keep c1030 clips no action reaches
                var a2 = Clip(cname); if (a2 == null) continue;
                double dur2 = (a2.Frames - 1) / a2.Fps;
                double[] qx = a2.RootPos.Select(p => (double)p.X).ToArray(), qy = a2.RootPos.Select(p => (double)p.Y).ToArray();
                var end2 = new V2(qx[^1] - qx[0], qy[^1] - qy[0]);
                int id = -1;
                if (m.Success) id = int.Parse(m.Groups[1].Value) * 10000 + int.Parse(m.Groups[2].Value);
                else
                {
                    // the boss pipeline names its NSA files after the first DS1 clip encoded (cXXXX_aYY_ZZZZ_untitled_…) and shares one
                    // file between identical DS1 clips (c2232 a00_3000 / a00_3014): candidates = that clip + every DS1 clip with the same
                    // frame count and trajectory end; the leaf's own one = the candidate whose DS1 hit starts (TAE type 1) its TimeAct
                    // 2200 starts match best (the boss TAE writes DS1's hit groups per leaf), then the named one
                    var bm = bossRx.Match(cname);
                    int named = bm.Success ? int.Parse(bm.Groups[1].Value) * 10000 + int.Parse(bm.Groups[2].Value) : -1;
                    bool Same(int x) => ds1.Has(x) && D(x).Frames == a2.Frames && new V2(D(x).End.X - end2.X, D(x).End.Y - end2.Y).Len <= Math.Max(0.03, 0.01 * D(x).Travel);
                    var cand = (named >= 0 && ds1.Has(named) ? new[] { named } : []).Concat(family.Where(Same)).Distinct().ToList();
                    if (cand.Count == 0) { if (isAttack && end2.Len > 0.5) { fails++; Console.WriteLine($"  [FAIL] {tg.Key} {cname}: attack clip ({F(end2.Len)} m) matches no DS1 clip of {tg.Ds1Chr} by name, frames + trajectory"); } continue; }
                    var hits2 = anims.SelectMany(a => a.Events.Where(e => e.Type == 2200).Select(e => (double)e.Start)).ToList();
                    int Score(int x) => (ds1.TaeAnim(x)?.Events.Where(e => e.Type == 1).Select(e => (double)e.StartTime).Distinct() ?? []).Count(h => hits2.Any(g => Math.Abs(g - h) < 0.02))
                        - Math.Abs((ds1.TaeAnim(x)?.Events.Count(e => e.Type == 1) ?? 0) - hits2.Count);
                    id = cand.OrderByDescending(Score).ThenBy(x => x == named ? 0 : 1).First();
                }
                if (!ds1.Has(id)) { fails++; Console.WriteLine($"  [FAIL] {tg.Key} {cname}: DS1 clip {Ds1ChrSource.ClipName(id)} not in {tg.Ds1Chr}"); continue; }
                var c = D(id);
                leavesChecked++;
                var probs = new List<string>(); var notes = new List<string>();
                // T1 travel
                double dEnd = new V2(c.End.X - end2.X, c.End.Y - end2.Y).Len;
                if (dEnd > Math.Max(0.03, 0.01 * c.Travel) || Math.Abs(dur2 - c.Duration) > 0.02) probs.Add($"T1 travel DS2 {F(end2.Len)} m / {F(dur2, "0.###")} s vs DS1 {F(c.Travel)} m / {F(c.Duration, "0.###")} s (end off by {F(dEnd, "0.000")} m)");
                // T2 stored trajectory / duration
                var e2 = a2.RootPos[^1];
                if (Math.Abs(net.ClipDuration(sa) - dur2) > 1e-3 || Math.Abs(net.F32(sa.Offset + 0x40) - e2.X) > 1e-3 || Math.Abs(net.F32(sa.Offset + 0x44) - e2.Y) > 1e-3 || Math.Abs(net.F32(sa.Offset + 0x48) - e2.Z) > 1e-3)
                    probs.Add($"T2 SourceAnim duration {F(net.ClipDuration(sa), "0.###")} s / trajectory end ({F(net.F32(sa.Offset + 0x40))}, {F(net.F32(sa.Offset + 0x44))}) â‰  NSA");
                // T3 LP1 fall protection (TAE seconds = clip seconds; gravity windows are clip fractions)
                var fall = anims.SelectMany(a => a.Events.Where(e => e.Type == 280000).Select(e => ((double)e.Start, (double)e.End))).Concat(gravity.Select(g => (g.S * dur2, g.E * dur2))).ToList();
                var want = c.NoFall.Select(w => (Math.Max(0, w.S), Math.Min(c.Duration, w.E))).Where(w => w.Item2 > w.Item1).ToList();
                double cov = Covered(want, fall);
                if (want.Count > 0 && cov < 0.95) probs.Add($"T3 LP1: DS1 JT 51 'Disable Fall Animation' [{string.Join(" ", want.Select(w => $"{F(w.Item1, "0.###")}-{F(w.Item2, "0.###")}"))}] covered {F(cov * 100, "0")} % by 280000 / Gravity (TimeAct {string.Join(",", taeIds.Distinct())})");
                // T4 JT 27
                var wantG = c.NoGravity.Select(w => (Math.Max(0, w.S), Math.Min(c.Duration, w.E))).Where(w => w.Item2 > w.Item1).ToList();
                double covG = Covered(wantG, gravity.Select(g => (g.S * dur2, g.E * dur2)).ToList());
                if (wantG.Count > 0 && covG < 0.95) { notes.Add($"KNOWN T4: DS1 JT 27 'Ignore Gravity' [{string.Join(" ", wantG.Select(w => $"{F(w.Item1, "0.###")}-{F(w.Item2, "0.###")}"))}] covered {F(covG * 100, "0")} % by a 9610 Gravity window (leaf {(gravity.Count > 0 ? "has" : "has no")} Gravity track)"); known++; }
                double inLeap = c.NoFall.Sum(w => Between(c.Px, c.Py, c.Fps, w.S, w.E));
                string line = $"{tg.Key} {Ds1ChrSource.ClipName(id),-9} {(m.Success ? "" : $"({cname[..Math.Min(22, cname.Length)]}) ")}DS1 {F(c.Travel),6} m / DS2 {F(end2.Len),6} m, {F(c.Duration, "0.00")} s, peak {F(Peak(c.Px, c.Py, c.Fps), "0.0")} m/s"
                    + (c.NoFall.Count > 0 ? $", JT51 {string.Join(" ", c.NoFall.Select(w => $"{F(w.S, "0.###")}-{F(w.E, "0.###")}"))} ({F(inLeap)} m), 280000 cover {F(cov * 100, "0")} %" : "");
                string rk = $"{tg.Key}|{id:D6}"; if (!rows.TryGetValue(rk, out var list)) rows[rk] = list = [line];
                foreach (var p in probs) list.Add("    FAIL " + p);
                foreach (var n in notes) list.Add("    " + n);
                if (probs.Count > 0) { fails += probs.Count; rowFail.Add(rk); }
                if (profile != null && profile.Equals($"{tg.Key}:{Ds1ChrSource.ClipName(id)}", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"profile {profile} (DS1 reference frame, DS2 NSA trajectory; m from frame 0, 0.1 s steps):");
                    for (double t = 0; t <= c.Duration + 1e-6; t += 0.1)
                    {
                        int f1 = Math.Clamp((int)Math.Round(t * c.Fps), 0, c.Px.Length - 1), f2 = Math.Clamp((int)Math.Round(t * a2.Fps), 0, qx.Length - 1);
                        bool nf = c.NoFall.Any(w => t >= w.S && t <= w.E), have = fall.Any(w => t >= w.Item1 - 1e-3 && t <= w.Item2 + 1e-3);
                        Console.WriteLine($"  t {F(t, "0.0")}  DS1 {F(new V2(c.Px[f1] - c.Px[0], c.Py[f1] - c.Py[0]).Len)}  DS2 {F(new V2(qx[f2] - qx[0], qy[f2] - qy[0]).Len)}{(nf ? "  JT51" : "")}{(have ? "  280000" : "")}");
                    }
                }
            }
            foreach (var (k, list) in rows)
            {
                clipsChecked++;
                bool bad = rowFail.Contains(k), moves = list[0].Contains("JT51") || !list[0].Contains("DS1   0.00 m");
                if (!all && !bad && !moves) continue;
                Console.WriteLine($"  [{(bad ? "FAIL" : "ok")}] {list[0]}");
                foreach (var l in list.Skip(1).Distinct()) Console.WriteLine($"  {l}");
            }
        }
        Console.WriteLine($"attack-travel-check: {clipsChecked} DS1 attack clips on {leavesChecked} anim leaves; {fails} FAIL, {known} KNOWN (T4)");
        return fails > 0 ? 1 : 0;
    }
}
