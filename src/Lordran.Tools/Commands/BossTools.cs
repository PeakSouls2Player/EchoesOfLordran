using Lordran.Tools.Formats;
// merge stage 5: the boss track's network / TAE code lives in its own namespaces (the enemies track has
// classes of the same names in Formats.Morpheme / Formats); see MERGE-NOTES.md "Unification backlog"
using Ds2TaeFile = Lordran.Tools.Formats.Boss.Ds2TaeFile;

namespace Lordran.Tools.Commands;

/// <summary>
/// Commands of the 1:1 boss pipeline (session 8, track boss: the Asylum Demon):
/// <list type="bullet">
/// <item><c>tae-ds2-roundtrip [DIR|FILE...]</c> (default data\ds2\timeact): every DS2 TAE read into <see cref="Ds2TaeFile"/>
/// and written back, byte compared.</item>
/// </list>
/// </summary>
public static partial class BossTools
{
    /// <summary><c>boss-bind-compare [DS1CHR] [CLIP]</c>: per DS1 bone, the local translation length in the DS1 FLVER bind,
    /// the Havok reference pose and the clip's frame 0 (bridged), and whether the clip tracks the bone - which pose do the
    /// DS1 clips and the mesh share?</summary>
    public static int BindCompare(string[] args)
    {
        string chr = args.Length > 1 ? args[1] : "c2232";
        int clipId = args.Length > 2 ? int.Parse(args[2]) : 0;
        var src = Build.Chr.Ds1ChrSource.Load(Program.DefaultDsrRoot, chr);
        var flv = SoulsFormats.FLVER2.Read(SoulsFormats.BND3.Read(Path.Combine(Program.DefaultDsrRoot, "chr", chr + ".chrbnd.dcx")).Files.First(f => f.Name.EndsWith(".flver")).Bytes);
        var sk = src.Skeleton;
        var smp = src.Samples(clipId);
        var w = ChrCompare.World(flv);
        int tracked = 0;
        double sumF = 0, sumH = 0, sumA = 0;
        for (int i = 0; i < sk.Names.Count; i++)
        {
            int fi = flv.Nodes.FindIndex(n => n.Name.Trim() == sk.Names[i]);
            int p = sk.Parents[i];
            int fp = p >= 0 ? flv.Nodes.FindIndex(n => n.Name.Trim() == sk.Names[p]) : -1;
            double lf = fi >= 0 && fp >= 0 ? System.Numerics.Vector3.Distance(w[fi].Translation, w[fp].Translation) : double.NaN;
            double lh = Math.Sqrt(sk.RefPos[i].Sum(v => (double)v * v));
            bool tr = smp.Tracks.TryGetValue(i, out var t) && t.Count > 0;
            double la = tr ? Math.Sqrt(t[0].T.Sum(v => (double)v * v)) : double.NaN;
            if (tr) tracked++;
            if (lf > 0.05 && tr) { sumF += lf; sumH += lh; sumA += la; }
            if (i < 12 || args.Contains("-v")) Console.WriteLine($"{i,3} {sk.Names[i],-22} flver {lf,8:0.000} havokRef {lh,8:0.000} clip{clipId}f0 {(tr ? la.ToString("0.000") : "untracked"),9}  ratio ref/flver {lh / lf,6:0.000} clip/flver {la / lf,6:0.000}");
        }
        Console.WriteLine("DS1 dummy ids (count × id @ attach bones): " + string.Join(", ", flv.Dummies.GroupBy(d => d.ReferenceID).OrderBy(g => g.Key)
            .Select(g => $"{g.Count()}×{g.Key}@{string.Join("/", g.Select(d => d.AttachBoneIndex >= 0 ? flv.Nodes[d.AttachBoneIndex].Name : "-").Distinct().Take(3))}")));
        Console.WriteLine($"{chr} clip {clipId}: {tracked}/{sk.Names.Count} bones tracked; summed bone lengths (bones > 5 cm, tracked): flver {sumF:0.000}, havok ref {sumH:0.000} ({sumH / sumF:0.000}x), clip frame 0 {sumA:0.000} ({sumA / sumF:0.000}x)");
        return 0;
    }

    /// <summary>Session 11 (track ret11), rule B-k: the boss a ds1boss-* command works on: <c>--boss KEY</c> (DS1 chr c2232 /
    /// c2230 or anim set c2210 / c2190), else the build output's boss.txt (written by ds1boss-build), else the Asylum Demon.</summary>
    internal static Build.Chr.Ds1Boss.Spec SpecArg(string[] args, string? dir)
    {
        int bi = Array.IndexOf(args, "--boss");
        string? key = bi > 0 && bi + 1 < args.Length ? args[bi + 1] : null;
        if (key == null && dir != null && File.Exists(Path.Combine(dir, "boss.txt"))) key = File.ReadAllLines(Path.Combine(dir, "boss.txt"))[0].Split(' ')[0];
        if (key == null) return Build.Chr.Ds1Boss.AsylumDemon;
        return Build.Chr.Ds1Boss.ByKey(key) ?? throw new ArgumentException($"no 1:1 boss {key} (have: {string.Join(", ", Build.Chr.Ds1Boss.All.Select(s => s.Ds1Chr))})");
    }

    /// <summary>Session 11 (ret11): the boss ds1boss-check is checking (set from <see cref="SpecArg"/>).</summary>
    internal static Build.Chr.Ds1Boss.Spec CheckSpec = Build.Chr.Ds1Boss.AsylumDemon;

    public static int Ds1BossBuild(string[] args)
    {
        string outDir = args.Length > 1 ? args[1] : throw new ArgumentException("usage: ds1boss-build OUTDIR");
        var ds1 = new Build.Ds1Source(Program.DefaultDsrRoot);
        var pack = new Build.Pack(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        var spec = SpecArg(args, null);   // session 11 (ret11): --boss KEY (default the Asylum Demon)
        var r = Build.Chr.Ds1Boss.Build(pack, Program.DefaultDsrRoot, ds1, spec);
        foreach (var (path, bytes) in r.Files)
        {
            string p = Path.Combine(outDir, "gamedata", path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllBytes(p, bytes);
            Console.WriteLine($"  {path} ({bytes.Length:N0} bytes)");
        }
        // merge9 stage 6: DS1's loose wave banks (pack device "title", chrsound rule S2) and the _snd sound log
        foreach (var (path, bytes) in r.TitleFiles)
        {
            string p = Path.Combine(outDir, "title", path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllBytes(p, bytes);
            Console.WriteLine($"  title/{path} ({bytes.Length:N0} bytes)");
        }
        File.WriteAllLines(Path.Combine(outDir, "sound.txt"), r.SoundLog);
        File.WriteAllLines(Path.Combine(outDir, "report.txt"), r.Report);
        File.WriteAllText(Path.Combine(outDir, "boss.txt"), $"{spec.Ds1Chr} {spec.N} anim c{spec.AnimId / 100:D4} entity {spec.Ds1Entity}{Environment.NewLine}");   // session 11 (ret11): which boss (ds1boss-check reads it)
        var rows = new List<string>
        {
            "| DS2 slot (c3050 NSA / new entry) | DS2 TAE id(s) | DS2 s | DS2 root end | DS1 clip | DS1 s | DS1 root end | role | status | DS1 TAE events |",
            "|---|---|---|---|---|---|---|---|---|---|",
        };
        rows.AddRange(Build.Chr.Ds1BossAnims.TableRows(r.Anims));
        File.WriteAllLines(Path.Combine(outDir, "roles.md"), rows);
        File.WriteAllLines(Path.Combine(outDir, "tracks.txt"), r.Anims.Clips.OrderBy(c => c.Name, StringComparer.Ordinal).SelectMany(c =>
            new[] { $"{c.Name} <- {(c.Ds1Id == -1 ? "bind" : Build.Chr.Ds1ChrSource.ClipName(c.Ds1Id))} {c.Role} (DS1 cancel {c.Cancel:0.###}, death {c.DeathState:0.###}){(c.NoAttack ? ", no attack" : "")}; steps {c.Steps}" }
            .Concat(c.TrackLog.Order(StringComparer.Ordinal).Select(t => "    " + t))));
        File.WriteAllLines(Path.Combine(outDir, "leaves.txt"), r.Anims.Leaves.Select(l => l.ToString()));
        File.WriteAllLines(Path.Combine(outDir, "damage.txt"), r.DamageRows.Select(d => $"{d.Id}: {d.Note}"));
        r.Anims.Rig.Save(Path.Combine(outDir, "skeleton.json"));
        foreach (var l in r.Report) Console.WriteLine(l);
        return 0;
    }

    public static int TaeRoundtrip(string[] args)
    {
        var paths = args.Skip(1).Where((a, i) => !a.StartsWith('-') && (i == 0 || args[i] != "--out")).ToList();
        if (paths.Count == 0) paths.Add(@"data\ds2\timeact");
        var files = paths.SelectMany(p => Directory.Exists(p) ? Directory.GetFiles(p, "*.tae", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal) : (IEnumerable<string>)[p]).ToList();
        int ok = 0, anims = 0, events = 0, shown = 0;
        foreach (var f in files)
        {
            var d = File.ReadAllBytes(f);
            try
            {
                var t = Ds2TaeFile.Read(d);
                anims += t.Animations.Count; events += t.Animations.Sum(a => a.Events.Count);
                var w = t.Write();
                if (args.Contains("--out")) File.WriteAllBytes(args[Array.IndexOf(args, "--out") + 1], w);
                if (w.AsSpan().SequenceEqual(d)) { ok++; continue; }
                int k = 0; while (k < Math.Min(w.Length, d.Length) && w[k] == d[k]) k++;
                if (shown++ < 15) Console.WriteLine($"  {f}: DIFF ({d.Length} -> {w.Length} bytes, first difference 0x{k:x})");
            }
            catch (Exception e) { if (shown++ < 15) Console.WriteLine($"  {f}: EXCEPTION {e.GetType().Name}: {e.Message}"); }
        }
        Console.WriteLine($"{files.Count} DS2 TAE files: write(read) byte-identical {ok}/{files.Count} ({anims} animations, {events} events)");
        return ok == files.Count ? 0 : 1;
    }
}
