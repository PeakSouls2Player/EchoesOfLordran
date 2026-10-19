using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Commands of the boss pipeline, step 2 (session 8, track boss: the Asylum Demon's AI and DS1 boss events).
/// <list type="bullet">
/// <item><c>boss-drop-path [CLIP] [REGION]</c> (default 9060 from 1812305): DS1's intro drop as the DS2 engine would see it -
/// the clip's root motion (DS1 hkaDefaultAnimatedReferenceFrame) placed at the DS1 region the event warps the demon to,
/// and every DS1 walkable collision triangle (hit filter 8: what the map builder converts) the character capsule (DS1
/// NpcParam hitRadius / hitHeight) touches on the way. DS1's clip disables map collision (TAE JT 19) for its first 2.13 s;
/// DS2 has no animation flag we can name for that, so this says whether the path is clear without it.</item>
/// </list>
/// </summary>
public static partial class BossTools
{
    /// <summary><c>ds2-root-survey [FILTER] [--min M]</c>: DS2 clips (every anibnd in data\ds2\morpheme4\chr, NSA names containing
    /// FILTER, default "a75_") whose root motion moves the character up or down by at least M metres (default 3): which DS2
    /// characters enter by dropping or rising, and with which TimeAct (the TAE id is the clip's a&lt;AA&gt;_&lt;BB&gt;_&lt;CCCC&gt;).</summary>
    public static int RootSurvey(string[] args)
    {
        string filter = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : "a75_";
        int mi = Array.IndexOf(args, "--min");
        double min = mi > 0 ? double.Parse(args[mi + 1], System.Globalization.CultureInfo.InvariantCulture) : 3;
        foreach (var f in Directory.GetFiles(Path.Combine(ExtractDs2.DefaultOut, "morpheme4", "chr"), "c*.anibnd.dcx").OrderBy(x => x, StringComparer.Ordinal))
        {
            BND4 bnd;
            try { bnd = BND4.Read(DCX.Decompress(File.ReadAllBytes(f))); } catch { continue; }
            foreach (var e in bnd.Files.Where(x => x.Name.EndsWith(".nsa", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(x.Name.Replace('\\', '/')).Contains(filter, StringComparison.OrdinalIgnoreCase)))
            {
                Formats.Morpheme.NsaAnim a;
                try { a = Formats.Morpheme.NsaCodec.Decode(Formats.Morpheme.NsaFile.Parse(e.Bytes)); } catch { continue; }
                if (a.RootPos.Length == 0) continue;
                double z0 = a.RootPos[0].Z, z1 = a.RootPos[^1].Z, zmin = a.RootPos.Min(p => p.Z), zmax = a.RootPos.Max(p => p.Z);
                if (Math.Max(Math.Abs(z1 - z0), Math.Max(z0 - zmin, zmax - z0)) < min) continue;
                double fwd = a.RootPos[^1].Y - a.RootPos[0].Y;
                Console.WriteLine($"{Path.GetFileName(f)[..5]} {Path.GetFileName(e.Name.Replace('\\', '/')),-44} {(a.Duration ?? (a.Frames - 1) / a.Fps),6:0.00}s  dz {z1 - z0,7:0.00} (min {zmin - z0,7:0.00}, max {zmax - z0,6:0.00})  dy {fwd,6:0.00}");
            }
        }
        return 0;
    }

    /// <summary><c>tae-event-survey TYPE...</c>: where DS2's chr TAEs (data\ds2\timeact\chr\*_pl.tae) use the given event types:
    /// per type, the animation families (TAE id / 10000, e.g. 1300 = a00_13 jumps/falls, 7501 = a75_01 start-ups) with the
    /// number of uses and the share of the animation the event covers. Evidence for naming DS2's unnamed TAE events (session 8,
    /// track boss: 2600 / 110600 in DS2's dropping start-ups).</summary>
    public static int TaeEventSurvey(string[] args)
    {
        var want = args.Skip(1).Select(int.Parse).ToHashSet();
        var fam = new Dictionary<(int Type, long Family), (int N, double Cover, HashSet<string> Chr)>();
        foreach (var f in Directory.GetFiles(Path.Combine(ExtractDs2.DefaultOut, "timeact", "chr"), "c*_pl.tae").OrderBy(x => x, StringComparer.Ordinal))
        {
            Formats.Boss.Ds2TaeFile tae;
            try { tae = Formats.Boss.Ds2TaeFile.Read(File.ReadAllBytes(f)); } catch { continue; }
            foreach (var a in tae.Animations)
                foreach (var e in a.Events.Where(e => want.Contains(e.Type)))
                {
                    double len = a.Frames > 0 ? a.Frames / 30.0 : 0;
                    var key = (e.Type, a.Id / 10000);
                    if (!fam.TryGetValue(key, out var v)) v = (0, 0, []);
                    v.Chr.Add(Path.GetFileName(f)[..7]);
                    fam[key] = (v.N + 1, v.Cover + (len > 0 ? Math.Min(1, (e.End - e.Start) / len) : 0), v.Chr);
                }
        }
        foreach (var g in fam.GroupBy(k => k.Key.Type))
        {
            Console.WriteLine($"event {g.Key}: {g.Sum(x => x.Value.N)} uses");
            foreach (var (k, v) in g.OrderByDescending(x => x.Value.N).Take(25))
                Console.WriteLine($"  family {k.Family,6} ({v.Chr.Count,3} chr) x{v.N,5}  mean cover {v.Cover / v.N:P0}  e.g. {string.Join(" ", v.Chr.Take(4))}");
        }
        return 0;
    }

    /// <summary><c>nmb-clip-tracks ANIBND FILTER [--user N]</c>: the event tracks (name, userData, events as start+duration:value
    /// fractions) of every anim leaf whose clip name contains FILTER; --user N lists only tracks with that userData. How DS2's
    /// own clips carry gameplay windows such as Gravity (9600) / HitControll (9700) (session 8, track boss).</summary>
    public static int ClipTracks(string[] args)
    {
        var files = MorphemeTools.LoadFiles(args[1]);
        string filter = args.Length > 2 ? args[2] : "";
        int ui = Array.IndexOf(args, "--user");
        int? user = ui > 0 ? int.Parse(args[ui + 1]) : null;
        var nmb = Formats.Morpheme.NmbFile.Read(files.First(f => f.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)).Bytes);
        var net = new Formats.Morpheme.NmbNetwork(nmb.Bundles[nmb.IndexOf(10)].Data);
        var names = Formats.Morpheme.NmbFileNames.Read(nmb.Bundles[nmb.IndexOf(13)].Data).Anims;
        var trackBundle = new Dictionary<uint, int>();
        for (int i = 0; i < nmb.Bundles.Count; i++) if (nmb.Bundles[i].Type is 3 or 4 or 5) trackBundle[nmb.Bundles[i].AssetId] = i;
        foreach (var node in net.Nodes().Where(x => x.Type == 104))
        {
            var sa = Formats.Morpheme.NmbNetwork.Find(node, 21);
            var et = Formats.Morpheme.NmbNetwork.Find(node, 23);
            if (sa == null || et == null) continue;
            int ci = net.ClipIndex(sa);
            string clip = ci >= 0 && ci < names.Count ? names[ci] : $"clip{ci}";
            if (!clip.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            var lines = new List<string>();
            foreach (var (group, tid, _) in net.Tracks(et))
            {
                if (!trackBundle.TryGetValue(tid, out int bi)) continue;
                var tr = Formats.Morpheme.NmbEventTrack.Read(nmb.Bundles[bi].Data, nmb.Bundles[bi].Type);
                if (user != null && tr.UserData != user) continue;
                lines.Add($"    g{group} {tr.Name} (ud {tr.UserData}): {string.Join(" ", tr.Events.Select(e => $"{e.Start:0.###}+{e.Duration:0.###}:{e.Value}"))}");
            }
            Console.WriteLine($"node {node.Index} {clip} ({net.ClipDuration(sa):0.###} s)");
            foreach (var l in lines) Console.WriteLine(l);
            if (args.Contains("--raw"))
            {
                // the SourceEventTracks attrib as stored: handle size, then the bytes (16 per line)
                var raw = net.X[et.Offset..(et.Offset + (int)et.Size)];
                Console.WriteLine($"    attrib 23 @{et.Offset:X} size {et.Size}");
                for (int i = 0; i < raw.Length; i += 16) Console.WriteLine($"      +{i:X3} {Convert.ToHexString(raw[i..Math.Min(raw.Length, i + 16)])}");
            }
        }
        return 0;
    }

    /// <summary><c>ai-lua-parse [DIR]</c>: parses every decompiled DSR script (default data\research\lua\out, 558 files) with
    /// <see cref="Build.Ai.Boss.LuaSyntax"/>; lists failures.</summary>
    public static int AiLuaParse(string[] args)
    {
        string dir = args.Length > 1 ? args[1] : Build.Ai.Boss.AiPorter.DecompiledLua;
        int ok = 0, bad = 0;
        foreach (var f in Directory.GetFiles(dir, "*.lua", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            try { Build.Ai.Boss.LuaSyntax.Parse(File.ReadAllText(f)); ok++; }
            catch (Exception e) { bad++; Console.WriteLine($"FAIL {Path.GetRelativePath(dir, f)}: {e.Message}"); }
        }
        Console.WriteLine($"{ok} parsed, {bad} failed");
        return bad == 0 ? 0 : 1;
    }

    /// <summary><c>ds1-ai-paths MAP FUNCTION [activate|act]</c>: the decision paths of a DS1 AI function (symbolic run of DS1's
    /// own Lua, <see cref="Build.Ai.Boss.Ds1AiInterpreter"/>): per path its effects and decisions in order, and the return value.</summary>
    public static int Ds1AiPaths(string[] args)
    {
        var lib = new Build.Ai.Boss.Ds1AiInterpreter(Build.Ai.Boss.Ds1AiInterpreter.StandardFiles(Build.Ai.Boss.AiPorter.DecompiledLua, args[1]));
        string mode = args.Length > 3 ? args[3] : "";
        var paths = lib.Paths(args[2], mode);
        int k = 0;
        foreach (var p in paths)
        {
            Console.WriteLine($"path {k++}: return {p.Return}");
            foreach (var e in p.Stream) Console.WriteLine($"   {e}");
        }
        Console.WriteLine($"{paths.Count} paths; {lib.Loaded.Count} files loaded; unsupported: {string.Join("; ", lib.Unsupported.Distinct())}");
        return 0;
    }

    /// <summary><c>ds1boss-ai OUTDIR</c>: the Asylum Demon's translated AI alone (<see cref="Build.Ai.Boss.AiPorter"/>): the two AI
    /// ESDs (ezstate\ai223201.esd lower, ai223211.esd upper), their text dumps (*.txt, esd-dump format) and ai-report.txt.</summary>
    public static int Ds1BossAi(string[] args)
    {
        string outDir = args.Length > 1 ? args[1] : throw new ArgumentException("usage: ds1boss-ai OUTDIR");
        string root = Program.DefaultDsrRoot;
        var ds1 = new Build.Ds1Source(root);
        var pack = new Build.Pack(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        var spec = SpecArg(args, null);   // session 11 (ret11): --boss KEY (default the Asylum Demon)
        var src = Build.Chr.Ds1ChrSource.Load(root, spec.Ds1Chr);
        var flver = FLVER2.Read(BND3.Read(Path.Combine(root, "chr", spec.Ds1Chr + ".chrbnd.dcx")).Files.First(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
        var r = Build.Ai.Boss.AiPorter.Build(pack, ExtractDs2.DefaultOut, ds1, spec.Ai!, new Build.Chr.Ds1Boss.AiContext(spec, src, flver, ds1, root));
        foreach (var (path, bytes) in r.Files)
        {
            string p = Path.Combine(outDir, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllBytes(p, bytes);
            File.WriteAllText(p + ".txt", DumpEsd(ESD.Read(bytes)));
            Console.WriteLine($"  {path} ({bytes.Length:N0} bytes, {ESD.Read(bytes).StateGroups.Count} groups)");
        }
        File.WriteAllLines(Path.Combine(outDir, "ai-report.txt"), r.Report);
        foreach (var l in r.Report) Console.WriteLine(l);
        return 0;
    }

    /// <summary>An ESD as text (esd-dump's format).</summary>
    public static string DumpEsd(ESD esd)
    {
        var sb = new System.Text.StringBuilder();
        static string Cmd(ESD.CommandCall c) => $"{c.CommandBank}:{c.CommandID}({string.Join(", ", c.Arguments.Select(Esd.Expr))})";
        void Cond(ESD.Condition c, string ind)
        {
            sb.AppendLine($"{ind}if {Esd.Expr(c.Evaluator)}   [{Convert.ToHexString(c.Evaluator)}] -> {(c.TargetState?.ToString() ?? "-")}");
            foreach (var p in c.PassCommands) sb.AppendLine($"{ind}  pass {Cmd(p)}");
            foreach (var s in c.Subconditions) Cond(s, ind + "  ");
        }
        foreach (var (gid, states) in esd.StateGroups.OrderBy(g => g.Key))
        {
            sb.AppendLine($"group {gid}: {states.Count} states");
            foreach (var (sid, s) in states.OrderBy(s => s.Key))
            {
                sb.AppendLine($"  state {sid}");
                foreach (var c in s.EntryCommands) sb.AppendLine($"    entry {Cmd(c)}");
                foreach (var c in s.WhileCommands) sb.AppendLine($"    while {Cmd(c)}");
                foreach (var c in s.ExitCommands) sb.AppendLine($"    exit  {Cmd(c)}");
                foreach (var c in s.Conditions) Cond(c, "    ");
            }
        }
        return sb.ToString();
    }

    /// <summary><c>nmb-track-attrib-selftest [DIR]</c>: every SourceEventTracks attrib (type 23) of every DS2 network read and
    /// written back with <see cref="Formats.Morpheme.Boss.NmbNetEdit.WriteTrackAttrib"/> (byte compare over the attrib's handle
    /// size), and every event-track name table rebuilt over its own entries: the layout rules the new Gravity /
    /// HitControl tracks are written with.</summary>
    public static int TrackAttribSelfTest(string[] args)
    {
        string dir = args.Length > 1 ? args[1] : Path.Combine(ExtractDs2.DefaultOut, "morpheme4", "chr");
        int nets = 0, attribs = 0, same = 0, tables = 0, tablesSame = 0;
        var first = new List<string>();
        foreach (var f in Directory.GetFiles(dir, "*.anibnd.dcx").OrderBy(x => x, StringComparer.Ordinal))
        {
            BND4 bnd;
            try { bnd = BND4.Read(DCX.Decompress(File.ReadAllBytes(f))); } catch { continue; }
            var nf = bnd.Files.FirstOrDefault(x => x.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase));
            if (nf == null) continue;
            var nmb = Formats.Morpheme.NmbFile.Read(nf.Bytes);
            var x = nmb.Bundles[nmb.IndexOf(10)].Data;
            var net = new Formats.Morpheme.NmbNetwork(x);
            nets++;
            foreach (var node in net.Nodes())
                foreach (var a in node.Attribs.Where(a => a.Type == 23))
                {
                    attribs++;
                    var blob = x[a.Offset..(a.Offset + (int)a.Size)];
                    var back = Formats.Morpheme.Boss.NmbNetEdit.WriteTrackAttrib(Formats.Morpheme.Boss.NmbNetEdit.ReadTrackAttrib(blob));
                    if (back.AsSpan().SequenceEqual(blob)) same++;
                    else if (first.Count < 5) first.Add($"{Path.GetFileName(f)} node {node.Index}: {Convert.ToHexString(blob)} vs {Convert.ToHexString(back)}");
                }
            var items = Formats.Morpheme.Boss.NmbNetBlocks.BuildItems(x);
            var tab = items.First(it => it.Kind == "tracknames");
            var rebuilt = Formats.Morpheme.Boss.NmbNetBlocks.RebuildStrTab(tab.Data, Formats.Morpheme.Boss.NmbNetBlocks.StrTabEntries(tab.Data));
            tables++;
            if (tab.Data.AsSpan(0, Math.Min(tab.Data.Length, rebuilt.Length)).SequenceEqual(rebuilt.AsSpan(0, Math.Min(tab.Data.Length, rebuilt.Length))) && rebuilt.Length <= tab.Data.Length) tablesSame++;
            else if (first.Count < 5) first.Add($"{Path.GetFileName(f)}: track-name table differs");
        }
        foreach (var l in first) Console.WriteLine("  DIFF " + l);
        Console.WriteLine($"{nets} networks: SourceEventTracks attribs re-written byte-identically {same}/{attribs}; track-name tables {tablesSame}/{tables}");
        return same == attribs && tablesSame == tables ? 0 : 1;
    }

    public static int DropPath(string[] args)
    {
        int clip = args.Length > 1 ? int.Parse(args[1]) : 9060;
        int region = args.Length > 2 ? int.Parse(args[2]) : 1812305;
        const string map = "m18_01_00_00";
        string root = Program.DefaultDsrRoot;
        var msb = MSB1.Read(Path.Combine(root, "map", "MapStudio", map + ".msb"));
        var reg = msb.Regions.GetEntries().First(r => r.EntityID == region);
        var src = Build.Chr.Ds1ChrSource.Load(root, "c2232");
        var smp = src.Samples(clip);
        var npc = new Build.Ds1Source(root).Params.Params["NpcParam"][223200]!;
        float radius = Convert.ToSingle(npc["hitRadius"].Value), height = Convert.ToSingle(npc["hitHeight"].Value);
        var rf = smp.RefFrame ?? [];
        Console.WriteLine($"clip a00_{clip:D4}: {rf.Count} root samples; start {reg.Name} ({region}) at {reg.Position} yaw {reg.Rotation.Y}; capsule r {radius} h {height}");
        if (rf.Count == 0) return 1;
        float yaw = reg.Rotation.Y * MathF.PI / 180;
        var rot = Matrix4x4.CreateRotationY(yaw);
        Vector3 World(float[] s) => reg.Position + Vector3.Transform(new Vector3(s[0], s[1], s[2]), rot);

        // DS1 walkable collision (hit filter 8, as MapBuilder converts it), world triangles
        string d1 = Path.Combine(root, "map", map);
        var hi = BXF3.Read(Path.Combine(d1, $"h{map[1..]}.hkxbhd"), Path.Combine(d1, $"h{map[1..]}.hkxbdt"));
        var tris = new List<(string Part, Vector3 A, Vector3 B, Vector3 C)>();
        foreach (var part in msb.Parts.Collisions.Where(p => p.HitFilterID == 8))
        {
            var f = hi.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith($"h{part.ModelName[1..]}A", StringComparison.OrdinalIgnoreCase));
            if (f == null) continue;
            var m = ColSurvey.Place(part.Position, part.Rotation, part.Scale);
            foreach (var (a, b, c) in ColSurvey.Triangles(DCX.Decompress(f.Bytes)))
                tris.Add((part.Name, Vector3.Transform(a, m), Vector3.Transform(b, m), Vector3.Transform(c, m)));
        }
        Console.WriteLine($"{tris.Count} DS1 walkable triangles (hit filter 8)");

        // capsule (axis from p + r up to p + h - r) vs triangle: closest distance by sampling the axis
        static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return a;
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + d1 / (d1 - d3) * ab;
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + d2 / (d2 - d6) * ac;
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return b + (d4 - d3) / (d4 - d3 + (d5 - d6)) * (c - b);
            float den = 1 / (va + vb + vc);
            return a + ab * (vb * den) + ac * (vc * den);
        }
        List<(string Part, float Y)> Hits(Vector3 foot)
        {
            var hits = new List<(string, float)>();
            foreach (var (part, a, b, c) in tris)
            {
                var lo = Vector3.Min(a, Vector3.Min(b, c)); var hi2 = Vector3.Max(a, Vector3.Max(b, c));
                if (hi2.X < foot.X - radius || lo.X > foot.X + radius || hi2.Z < foot.Z - radius || lo.Z > foot.Z + radius || hi2.Y < foot.Y || lo.Y > foot.Y + height) continue;
                for (float t = radius; t <= height - radius + 1e-3f; t += 0.25f)
                {
                    var p = foot + new Vector3(0, t, 0);
                    var q = Closest(p, a, b, c);
                    if (Vector3.Distance(p, q) < radius - 0.05f) { hits.Add((part, q.Y)); break; }
                }
            }
            return hits;
        }
        int step = Math.Max(1, rf.Count / 40);
        int firstHit = -1;
        for (int i = 0; i < rf.Count; i += step)
        {
            var p = World(rf[i]);
            var h = Hits(p);
            if (h.Count > 0 && firstHit < 0) firstHit = i;
            Console.WriteLine($"  f{i,3} root ({p.X:0.00}, {p.Y:0.00}, {p.Z:0.00}){(h.Count > 0 ? $"  CONTACT {h.Count} tris: " + string.Join(", ", h.GroupBy(x => x.Part).Select(g => $"{g.Key} y {g.Min(x => x.Y):0.00}..{g.Max(x => x.Y):0.00}")) : "")}");
        }
        var end = World(rf[^1]);
        Console.WriteLine($"end ({end.X:0.00}, {end.Y:0.00}, {end.Z:0.00}); first contact at sample {firstHit}{(firstHit < 0 ? " (path clear)" : "")}");
        // a fall straight down from the start (gravity, no root motion): the first walkable surface under the capsule
        var start = World(rf[0]);
        var under = tris.Where(t => Math.Abs((t.A.X + t.B.X + t.C.X) / 3 - start.X) < radius && Math.Abs((t.A.Z + t.B.Z + t.C.Z) / 3 - start.Z) < radius
                                    && Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)) < start.Y).OrderByDescending(t => Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y))).Take(3).ToList();
        Console.WriteLine("straight below the start: " + string.Join(", ", under.Select(t => $"{t.Part} top y {Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)):0.00}")));
        // --floor X Z: the walkable surfaces (triangle heights) in the vertical column at (X, Z): where a test warp can stand
        int fi = Array.IndexOf(args, "--floor");
        if (fi > 0 && fi + 2 < args.Length)
        {
            float fx = float.Parse(args[fi + 1], System.Globalization.CultureInfo.InvariantCulture), fz = float.Parse(args[fi + 2], System.Globalization.CultureInfo.InvariantCulture);
            var hs = new List<(string Part, float Y)>();
            foreach (var (part, a, b, c) in tris)
            {
                float d = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
                if (MathF.Abs(d) < 1e-6f) continue;
                float l1 = ((b.Z - c.Z) * (fx - c.X) + (c.X - b.X) * (fz - c.Z)) / d, l2 = ((c.Z - a.Z) * (fx - c.X) + (a.X - c.X) * (fz - c.Z)) / d, l3 = 1 - l1 - l2;
                if (l1 < 0 || l2 < 0 || l3 < 0) continue;
                var nrm = Vector3.Normalize(Vector3.Cross(b - a, c - a));
                if (MathF.Abs(nrm.Y) < 0.7f) continue; // walls
                hs.Add((part, l1 * a.Y + l2 * b.Y + l3 * c.Y));
            }
            Console.WriteLine($"surfaces at ({fx}, {fz}): " + string.Join(", ", hs.OrderByDescending(h => h.Y).Select(h => $"{h.Part} y {h.Y:0.00}")));
        }
        // --slab Y0 Y1: top view (0.5 m cells, x across, z down) of the walkable triangles between two heights around the
        // drop column (# = covered, o = the falling root's column, . = free)
        int si = Array.IndexOf(args, "--slab");
        if (si > 0 && si + 2 < args.Length)
        {
            float y0 = float.Parse(args[si + 1], System.Globalization.CultureInfo.InvariantCulture), y1 = float.Parse(args[si + 2], System.Globalization.CultureInfo.InvariantCulture);
            const float cell = 0.5f; float x0 = end.X - 8, z0 = end.Z - 10; int nx = 33, nz = 41;
            var grid = new bool[nx, nz];
            foreach (var (_, a, b, c) in tris)
            {
                if (Math.Max(a.Y, Math.Max(b.Y, c.Y)) < y0 || Math.Min(a.Y, Math.Min(b.Y, c.Y)) > y1) continue;
                for (int u = 0; u <= 6; u++) for (int v = 0; u + v <= 6; v++)
                {
                    var p = a + (b - a) * (u / 6f) + (c - a) * (v / 6f);
                    int ix = (int)MathF.Floor((p.X - x0) / cell), iz = (int)MathF.Floor((p.Z - z0) / cell);
                    if (ix >= 0 && ix < nx && iz >= 0 && iz < nz) grid[ix, iz] = true;
                }
            }
            Console.WriteLine($"slab y {y0}..{y1}: x {x0:0.0}..{x0 + nx * cell:0.0} (columns), z {z0:0.0}..{z0 + nz * cell:0.0} (rows)");
            for (int iz = 0; iz < nz; iz++)
            {
                var sb = new System.Text.StringBuilder($"  z {z0 + iz * cell,7:0.0} ");
                for (int ix = 0; ix < nx; ix++)
                {
                    float cx = x0 + (ix + 0.5f) * cell, cz = z0 + (iz + 0.5f) * cell;
                    bool inCol = Math.Abs(cx - end.X) < 0.26f && cz >= -37f && cz <= end.Z + 0.25f;
                    sb.Append(grid[ix, iz] ? '#' : inCol ? 'o' : '.');
                }
                Console.WriteLine(sb);
            }
        }
        return 0;
    }
}
