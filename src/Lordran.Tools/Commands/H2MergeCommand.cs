using Lordran.Tools.Build.Maps;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (burg11, round 8): <c>h2-merge PRIMARY SECONDARY...</c> — rule H2's merged DS1 map (H2Merge) without a build:
/// the merge report, the H2-E entity moves and the merged map's bonfires (rule BF1 with rule H2-P).
/// </summary>
public static class H2MergeCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: h2-merge PRIMARY SECONDARY..."); return 1; }
        H2Merge.Activate(Program.DefaultDsrRoot, args.Skip(2).Select(s => (args[1], s)));
        foreach (var l in H2Merge.Report) Console.WriteLine(l);
        foreach (var (a, b) in H2Merge.EntityMap.Where(kv => kv.Key % 10000 != kv.Value % 10000).OrderBy(kv => kv.Key)) Console.WriteLine($"  moved {a} → {b}");
        var row = MapHosts.Of(args[1]) ?? throw new InvalidDataException($"{args[1]} has no row");
        var built = new HashSet<string>(MapHosts.Rows.Select(r => r.Ds1Map));
        var o = MapBuilder.ForDs1(row, Program.DefaultDsrRoot, built);
        foreach (var b in MapBuilder.Bonfires(Program.DefaultDsrRoot, o))
            Console.WriteLine($"bonfire {b.Id} entity {b.Entity} at {b.Position}: \"{b.Name}\", Travel area {b.MenuAreaId} \"{b.MenuAreaName}\"");
        return 0;
    }
}

/// <summary>Session 11 (burg11, round 9): <c>dg-census MAP...</c> — the DS1 draw-group bits each map's parts use (DrawGroups ∪
/// DispGroups of pieces, objects, collisions; connects separately) and the overlap between the maps (rule DG1 / H2-G).</summary>
public static class DgCensusCommand
{
    public static int Run(string[] args)
    {
        var sets = new List<(string Map, HashSet<int> Bits, HashSet<int> ConnectBits)>();
        foreach (string m in args.Skip(1))
        {
            var msb = SoulsFormats.MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", m + ".msb"));
            HashSet<int> Bits(IEnumerable<SoulsFormats.MSB1.Part> parts) => parts.SelectMany(p => new[] { p.DrawGroups, p.DispGroups }).SelectMany(g => g.SelectMany((w, i) => Enumerable.Range(0, 32).Where(b => (w >> b & 1) != 0).Select(b => i * 32 + b))).ToHashSet();
            var own = Bits(msb.Parts.GetEntries().Where(p => p is not SoulsFormats.MSB1.Part.ConnectCollision));
            var con = Bits(msb.Parts.ConnectCollisions);
            sets.Add((m, own, con));
            Console.WriteLine($"{m}: {own.Count} bits used by its parts ({string.Join(",", own.Order())}); connects {con.Count}; {msb.Parts.MapPieces.Count} pieces, {msb.Parts.Collisions.Count} collisions, {msb.Parts.ConnectCollisions.Count} connects");
        }
        Console.WriteLine($"union {sets.SelectMany(s => s.Bits).Distinct().Count()} bits, sum {sets.Sum(s => s.Bits.Count)} (128 available)");
        return 0;
    }
}

/// <summary>Session 11 (burg11, round 9 fix): <c>uv-survey DS1MAP|PACK DS2MAP</c> — UV ranges of a DS1 map's pieces (or a
/// pack map's converted pieces): meshes with a UV beyond DS2's short UV range (±32 = 32767/1024) are listed.</summary>
public static class UvSurveyCommand
{
    public static int Run(string[] args)
    {
        var models = new List<(string Name, SoulsFormats.FLVER2 F)>();
        if (args.Length > 2 && !args[2].StartsWith("--"))
        {
            string dir = Directory.Exists(Path.Combine(args[1], "gamedata")) ? Path.Combine(args[1], "gamedata", "model", "map") : Path.Combine(args[1], "model", "map");
            var bxf = SoulsFormats.BXF4.Read(Path.Combine(dir, args[2] + ".mapbhd"), Path.Combine(dir, args[2] + ".mapbdt"));
            foreach (var f in bxf.Files.Where(f => f.Name.EndsWith(".flv.dcx", StringComparison.OrdinalIgnoreCase)))
                models.Add((Path.GetFileName(f.Name), SoulsFormats.FLVER2.Read(SoulsFormats.DCX.Decompress(f.Bytes))));
        }
        else
        {
            string dir = Path.Combine(Program.DefaultDsrRoot, "map", args[1]);
            foreach (string f in Directory.GetFiles(dir, "*.flver.dcx"))
                models.Add((Path.GetFileName(f), SoulsFormats.FLVER2.Read(SoulsFormats.DCX.Decompress(File.ReadAllBytes(f)))));
        }
        int over = 0, meshes = 0;
        foreach (var (name, f) in models)
            foreach (var (m, mi) in f.Meshes.Select((m, i) => (m, i)))
            {
                meshes++;
                if (m.Vertices.Count == 0) continue;
                int uvN = m.Vertices[0].UVs.Count;
                for (int k = 0; k < uvN; k++)
                {
                    float mx = m.Vertices.Max(v => Math.Max(Math.Abs(v.UVs[k].X), Math.Abs(v.UVs[k].Y)));
                    if (mx > (args.Contains("--16") ? 15.99f : 31.99f))
                    {
                        over++;
                        var layout = f.BufferLayouts[m.VertexBuffers[0].LayoutIndex].Where(x => x.Semantic == SoulsFormats.FLVER.LayoutSemantic.UV).Select(x => x.Type.ToString());
                        Console.WriteLine($"{name} mesh {mi} UV{k}: max |uv| {mx:0.0} ({m.Vertices.Count} vertices; {Path.GetFileName(f.Materials[m.MaterialIndex].MTD)}; layout UV {string.Join(",", layout)})");
                    }
                }
            }
        Console.WriteLine($"uv-survey: {models.Count} models, {meshes} meshes, {over} UV channels beyond ±32");
        return 0;
    }
}

/// <summary>Session 11 (burg11, fix round): <c>tex-pairs PACK MAP_A MAP_B</c> — texture entries (t, t_low, g archives) the two
/// maps share by name: identical or different bytes, and the inner TPF texture names.</summary>
public static class TexPairsCommand
{
    public static int Run(string[] args)
    {
        string dir = Path.Combine(args[1], "gamedata", "model", "map");
        Dictionary<string, byte[]> Load(string map)
        {
            var d = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            string tail = map[1..];
            foreach (var (bhd, bdt) in new[] { ($"t{tail}.tpfbhd", $"t{tail}.tpfbdt"), ($"g{tail}.gibhd", $"g{tail}.gibdt") })
                foreach (var f in SoulsFormats.BXF4.Read(Path.Combine(dir, bhd), Path.Combine(dir, bdt)).Files)
                    d[$"{bhd[0]}:{Path.GetFileName(f.Name)}"] = f.Bytes;
            foreach (var f in SoulsFormats.BND4.Read(Path.Combine(dir, $"t{tail}_low.tpfbdt")).Files) d[$"low:{Path.GetFileName(f.Name)}"] = f.Bytes;
            return d;
        }
        var a = Load(args[2]); var b = Load(args[3]);
        var shared = a.Keys.Intersect(b.Keys).ToList();
        int diff = 0;
        foreach (var k in shared.Order())
            if (!a[k].AsSpan().SequenceEqual(b[k])) { diff++; if (diff <= 30) Console.WriteLine($"DIFFERENT {k}"); }
        // inner names (what DS2 caches by)
        HashSet<string> Inner(Dictionary<string, byte[]> d) => d.Values.SelectMany(x => { try { return SoulsFormats.TPF.Read(SoulsFormats.DCX.Is(x) ? SoulsFormats.DCX.Decompress(x) : x).Textures.Select(t => t.Name); } catch { return []; } }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ia = Inner(a); var ib = Inner(b);
        Console.WriteLine($"tex-pairs {args[2]} {a.Count} entries / {ia.Count} textures, {args[3]} {b.Count} / {ib.Count}; shared entry names {shared.Count} ({diff} with different bytes); shared inner texture names {ia.Intersect(ib).Count()}");
        return 0;
    }
}

/// <summary>Session 11 (burg11, fix round): <c>tex-census DIR...</c> — per map: t / g archive entries, inner textures, sizes and
/// formats (DIR = a pack's or DS2's model\map folder).</summary>
public static class TexCensusCommand
{
    public static int Run(string[] args)
    {
        foreach (string dir in args.Skip(1))
            foreach (string bhd in Directory.GetFiles(dir, "t*.tpfbhd").Where(f => !f.Contains("_low")).Order())
            {
                var bxf = SoulsFormats.BXF4.Read(bhd, Path.ChangeExtension(bhd, ".tpfbdt"));
                var texs = bxf.Files.SelectMany(f => SoulsFormats.TPF.Read(SoulsFormats.DCX.Is(f.Bytes) ? SoulsFormats.DCX.Decompress(f.Bytes) : f.Bytes).Textures).ToList();
                long bytes = texs.Sum(t => (long)t.Bytes.Length);
                var fmts = texs.GroupBy(t => t.Format).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}:{g.Count()}");
                Console.WriteLine($"{Path.GetFileName(bhd)}: {bxf.Files.Count} entries, {texs.Count} textures, {bytes / 1048576.0:0.0} MB; formats {string.Join(" ", fmts)}");
            }
        return 0;
    }
}

/// <summary><c>flver-version FILE.flv.dcx|BHD</c> — FLVER header versions (fix round).</summary>
public static class FlverVersionCommand
{
    public static int Run(string[] args)
    {
        var bxf = SoulsFormats.BXF4.Read(args[1], Path.ChangeExtension(args[1], ".mapbdt"));
        foreach (var g in bxf.Files.Where(f => f.Name.EndsWith(".flv.dcx", StringComparison.OrdinalIgnoreCase)).Take(400)
            .GroupBy(f => SoulsFormats.FLVER2.Read(SoulsFormats.DCX.Decompress(f.Bytes)).Header.Version))
            Console.WriteLine($"version 0x{g.Key:X}: {g.Count()} models");
        return 0;
    }
}

/// <summary>Session 11 (burg11, play-test fix, rule UV1 check): <c>uv-check PACK DS2MAP DS1MAP</c> — every converted map piece
/// mesh against its DS1 mesh, triangle by triangle: within each triangle the UV differences must equal DS1's and the
/// fractional UV of its first corner must match (an integer shift samples the same texels). Meshes whose triangle count
/// differs (dropped / split / merged meshes) are skipped and counted.</summary>
public static class UvCheckCommand
{
    public static int Run(string[] args)
    {
        string dir = Path.Combine(args[1], "gamedata", "model", "map");
        var bxf = SoulsFormats.BXF4.Read(Path.Combine(dir, args[2] + ".mapbhd"), Path.Combine(dir, args[2] + ".mapbdt"));
        string ds1Dir = Path.Combine(Program.DefaultDsrRoot, "map", args[3]);
        var ds1Files = Directory.GetFiles(ds1Dir, "m*.flver.dcx").ToDictionary(f => Path.GetFileName(f)[..5], f => f, StringComparer.OrdinalIgnoreCase);
        int meshes = 0, checkedMeshes = 0, skipped = 0, badMeshes = 0, badTris = 0;
        foreach (var f in bxf.Files.Where(f => f.Name.EndsWith(".flv.dcx", StringComparison.OrdinalIgnoreCase)))
        {
            string model = Path.GetFileName(f.Name)[..5];
            if (!ds1Files.TryGetValue(model, out var ds1File)) continue;
            var a = SoulsFormats.FLVER2.Read(SoulsFormats.DCX.Decompress(f.Bytes));
            var b = SoulsFormats.FLVER2.Read(SoulsFormats.DCX.Decompress(File.ReadAllBytes(ds1File)));
            var ds1Meshes = b.Meshes.Where(m => m.Vertices.All(v => float.IsFinite(v.Position.X) && MathF.Abs(v.Position.X) < 1e6f)).ToList();
            for (int mi = 0; mi < a.Meshes.Count; mi++)
            {
                meshes++;
                var ma = a.Meshes[mi];
                var ia = ma.FaceSets[0].Triangulate(ma.Vertices.Count < ushort.MaxValue);
                var mb = mi < ds1Meshes.Count ? ds1Meshes[mi] : null;
                var fsb = mb?.FaceSets.FirstOrDefault(x => x.Flags == SoulsFormats.FLVER2.FaceSet.FSFlags.None);
                var ib = fsb?.Triangulate(true);
                if (mb == null || ib == null || ib.Count != ia.Count || ma.Vertices.Count == 0 || mb.Vertices.Count == 0) { skipped++; continue; }
                checkedMeshes++;
                int k = 0; // DS1 diffuse UV0 vs DS2 UV0
                int bad = 0;
                for (int t = 0; t + 2 < ia.Count; t += 3)
                {
                    var A = new[] { ma.Vertices[ia[t]].UVs[k], ma.Vertices[ia[t + 1]].UVs[k], ma.Vertices[ia[t + 2]].UVs[k] };
                    var B = new[] { mb.Vertices[ib[t]].UVs[k], mb.Vertices[ib[t + 1]].UVs[k], mb.Vertices[ib[t + 2]].UVs[k] };
                    static float Frac(float x) => x - MathF.Floor(x);
                    bool ok = Enumerable.Range(1, 2).All(j => MathF.Abs((A[j] - A[0]).X - (B[j] - B[0]).X) < 0.01f && MathF.Abs((A[j] - A[0]).Y - (B[j] - B[0]).Y) < 0.01f)
                        && Math.Min(MathF.Abs(Frac(A[0].X) - Frac(B[0].X)), 1 - MathF.Abs(Frac(A[0].X) - Frac(B[0].X))) < 0.01f
                        && Math.Min(MathF.Abs(Frac(A[0].Y) - Frac(B[0].Y)), 1 - MathF.Abs(Frac(A[0].Y) - Frac(B[0].Y))) < 0.01f;
                    if (!ok) bad++;
                }
                if (bad > 0) { badMeshes++; badTris += bad; if (badMeshes <= 15) Console.WriteLine($"MISMAPPED {model} mesh {mi}: {bad} of {ia.Count / 3} triangles"); }
            }
        }
        Console.WriteLine($"uv-check {args[2]} vs {args[3]}: {meshes} meshes, {checkedMeshes} compared ({skipped} skipped: no 1:1 DS1 mesh), {badMeshes} with mismapped UV0 ({badTris} triangles)");
        return badMeshes > 0 ? 1 : 0;
    }
}

/// <summary>Session 11 (burg11, play-test fix): <c>zone-pieces DS1MAP COLLISION...</c> — DS1 map-piece meshes inside the box
/// of the given collision parts (± 2 m): model, mesh, MTD, lightmap or not.</summary>
public static class ZonePiecesCommand
{
    public static int Run(string[] args)
    {
        string map = args[1];
        var names = args.Skip(2).ToHashSet();
        var probe = Build.Maps.FloorProbe.FromDs1(Program.DefaultDsrRoot, map, c => names.Contains(c.Name));
        if (probe.Count == 0) { Console.WriteLine("no triangles"); return 1; }
        var pts = probe.Triangles.SelectMany(t => new[] { t.A, t.B, t.C }).ToList();
        var lo = pts.Aggregate(new System.Numerics.Vector3(float.MaxValue), System.Numerics.Vector3.Min) - new System.Numerics.Vector3(2);
        var hi = pts.Aggregate(new System.Numerics.Vector3(float.MinValue), System.Numerics.Vector3.Max) + new System.Numerics.Vector3(2);
        Console.WriteLine($"box {lo} .. {hi}");
        var msb = SoulsFormats.MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", map + ".msb"));
        string area = map.Substring(1, 2);
        foreach (var part in msb.Parts.MapPieces)
        {
            string file = Path.Combine(Program.DefaultDsrRoot, "map", map, $"{part.ModelName}A{area}.flver.dcx");
            if (!File.Exists(file)) continue;
            var f = SoulsFormats.FLVER2.Read(SoulsFormats.DCX.Decompress(File.ReadAllBytes(file)));
            var m = Build.Maps.FloorProbe.Place(part.Position, part.Rotation, part.Scale);
            for (int i = 0; i < f.Meshes.Count; i++)
            {
                var vs = f.Meshes[i].Vertices;
                if (vs.Count == 0) continue;
                int inside = vs.Count(v => { var p = System.Numerics.Vector3.Transform(v.Position, m); return p.X >= lo.X && p.Y >= lo.Y && p.Z >= lo.Z && p.X <= hi.X && p.Y <= hi.Y && p.Z <= hi.Z; });
                if (inside * 2 < vs.Count) continue;
                var mat = f.Materials[f.Meshes[i].MaterialIndex];
                bool lm = mat.Textures.Any(t => t.ParamName == "g_Lightmap" && !string.IsNullOrEmpty(t.Path));
                Console.WriteLine($"{part.Name} mesh {i}: {Path.GetFileName(mat.MTD)} {(lm ? "lightmap" : "NO lightmap")} {vs.Count} vertices ({inside} inside)");
            }
        }
        return 0;
    }
}

/// <summary><c>vcolor DS1MAP MODEL [MESH]</c> — DS1 vertex colour statistics per mesh (fix round: baked lighting?).</summary>
public static class VColorCommand
{
    public static int Run(string[] args)
    {
        string file = Path.Combine(Program.DefaultDsrRoot, "map", args[1], $"{args[2]}A{args[1].Substring(1, 2)}.flver.dcx");
        var f = SoulsFormats.FLVER2.Read(SoulsFormats.DCX.Decompress(File.ReadAllBytes(file)));
        for (int i = 0; i < f.Meshes.Count; i++)
        {
            var vs = f.Meshes[i].Vertices;
            if (vs.Count == 0 || vs[0].Colors.Count == 0) { Console.WriteLine($"mesh {i}: no colours"); continue; }
            var c = vs.Select(v => v.Colors[0]).ToList();
            Console.WriteLine($"mesh {i} {Path.GetFileName(f.Materials[f.Meshes[i].MaterialIndex].MTD)}: R {c.Average(x => x.R):0.00} ({c.Min(x => x.R):0.00}..{c.Max(x => x.R):0.00}) G {c.Average(x => x.G):0.00} B {c.Average(x => x.B):0.00} A {c.Average(x => x.A):0.00} ({c.Min(x => x.A):0.00}..{c.Max(x => x.A):0.00}); textures {string.Join(",", f.Materials[f.Meshes[i].MaterialIndex].Textures.Select(t => t.ParamName + "=" + Path.GetFileName(t.Path)))}");
        }
        return 0;
    }
}

/// <summary><c>overlap-check DS1MAP_A DS1MAP_B</c> — map-piece triangles of A that lie on a triangle of B (same plane within
/// 2 cm, centroid inside it): geometry both DS1 maps carry at their seam, which DS1 shows one at a time by draw groups;
/// drawn together they z-fight. Grouped by A piece and B piece.</summary>
public static class OverlapCheckCommand
{
    public static int Run(string[] args)
    {
        var pairs = Pairs(args[1], args[2]);
        foreach (var (k, v) in pairs.OrderByDescending(kv => kv.Value).Take(20)) Console.WriteLine($"{k.Item1} on {k.Item2}: {v} triangles");
        Console.WriteLine($"overlap-check {args[1]} vs {args[2]}: {pairs.Values.Sum()} coplanar triangles of A on B in {pairs.Count} piece pairs");
        return 0;
    }

    /// <summary>(DS1 piece part of A, DS1 piece part of B) → coplanar triangles of A lying on B.</summary>
    public static Dictionary<(string, string), int> Pairs(string mapA, string mapB)
    {
        List<(System.Numerics.Vector3 A, System.Numerics.Vector3 B, System.Numerics.Vector3 C, string Part)> Tris(string map)
        {
            var probe = Build.Maps.FloorProbe.FromDs1Pieces(Program.DefaultDsrRoot, map, System.Numerics.Vector3.Zero, 1e6f);
            return probe.Triangles.Select(t => (t.A, t.B, t.C, t.Part)).ToList();
        }
        var a = Tris(mapA); var b = Tris(mapB);
        var grid = new Dictionary<(int, int, int), List<int>>();
        static (int, int, int) K(System.Numerics.Vector3 p) => ((int)MathF.Floor(p.X / 2), (int)MathF.Floor(p.Y / 2), (int)MathF.Floor(p.Z / 2));
        for (int i = 0; i < b.Count; i++)
        {
            var lo = System.Numerics.Vector3.Min(b[i].A, System.Numerics.Vector3.Min(b[i].B, b[i].C)); var hi = System.Numerics.Vector3.Max(b[i].A, System.Numerics.Vector3.Max(b[i].B, b[i].C));
            if ((hi - lo).Length() > 60) continue;
            for (int x = K(lo).Item1; x <= K(hi).Item1; x++) for (int y = K(lo).Item2; y <= K(hi).Item2; y++) for (int z = K(lo).Item3; z <= K(hi).Item3; z++)
                    (grid.TryGetValue((x, y, z), out var l) ? l : grid[(x, y, z)] = []).Add(i);
        }
        var pairs = new Dictionary<(string, string), int>();
        foreach (var t in a)
        {
            var c = (t.A + t.B + t.C) / 3;
            var n = System.Numerics.Vector3.Cross(t.B - t.A, t.C - t.A);
            if (n.Length() < 1e-6f) continue;
            n = System.Numerics.Vector3.Normalize(n);
            if (!grid.TryGetValue(K(c), out var list)) continue;
            foreach (int j in list)
            {
                var u = b[j];
                var m = System.Numerics.Vector3.Cross(u.B - u.A, u.C - u.A);
                if (m.Length() < 1e-6f) continue;
                m = System.Numerics.Vector3.Normalize(m);
                if (MathF.Abs(System.Numerics.Vector3.Dot(n, m)) < 0.999f || MathF.Abs(System.Numerics.Vector3.Dot(c - u.A, m)) > 0.02f) continue;
                // centroid inside u (barycentric on the plane)
                var v0 = u.C - u.A; var v1 = u.B - u.A; var v2 = c - u.A;
                float d00 = System.Numerics.Vector3.Dot(v0, v0), d01 = System.Numerics.Vector3.Dot(v0, v1), d02 = System.Numerics.Vector3.Dot(v0, v2), d11 = System.Numerics.Vector3.Dot(v1, v1), d12 = System.Numerics.Vector3.Dot(v1, v2);
                float inv = 1 / (d00 * d11 - d01 * d01), s1 = (d11 * d02 - d01 * d12) * inv, s2 = (d00 * d12 - d01 * d02) * inv;
                if (s1 < 0 || s2 < 0 || s1 + s2 > 1) continue;
                var key = (t.Part, u.Part);
                pairs[key] = pairs.GetValueOrDefault(key) + 1;
                break;
            }
        }
        return pairs;
    }
}

/// <summary>Session 11 (burg11, play-test fix): <c>seam-zfight PACK DS2_A DS2_B</c> — the pieces both DS1 maps carry at their
/// seam (<see cref="OverlapCheckCommand"/>) must not be drawn together: for every collision of A, A's pieces drawn from it
/// (DrawGroups ∩ its DispGroups) and B's pieces its connects to B load (DrawGroups ∩ connect mask); a coplanar A/B pair
/// both drawn = z-fighting ("glitched textures"). DS1's own count (its DispGroups / its connects' DispGroups) is printed
/// beside: the pack must not draw more pairs than DS1.</summary>
public static class SeamZfightCommand
{
    static bool Any(uint[] a, uint[] b) { for (int i = 0; i < 4; i++) if ((a[i] & b[i]) != 0) return true; return false; }
    public static int Run(string[] args)
    {
        string pack = args[1], a2 = args[2], b2 = args[3];
        string a1 = Build.Maps.MapHosts.Ds1Of(a2)!, b1 = Build.Maps.MapHosts.Ds1Of(b2)!;
        var pairs = OverlapCheckCommand.Pairs(a1, b1);
        SoulsFormats.MSB2 Msb2(string m) => SoulsFormats.MSB2.Read(Path.Combine(pack, "gamedata", "map", m, m + ".msb"));
        SoulsFormats.MSB1 Msb1(string m) => SoulsFormats.MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", m + ".msb"));
        var ma2 = Msb2(a2); var mb2 = Msb2(b2); var ma1 = Msb1(a1); var mb1 = Msb1(b1);
        // DS1 part → its DS2 piece (model digits + position)
        SoulsFormats.MSB2.Part.MapPiece? Ds2(SoulsFormats.MSB2 m2, SoulsFormats.MSB1.Part p1) => m2.Parts.MapPieces.FirstOrDefault(p => p.ModelName.Length >= 5 && p.ModelName.Substring(1, 4) == p1.ModelName.Substring(1, 4) && System.Numerics.Vector3.Distance(p.Position, p1.Position) < 0.05f);
        var pairParts = pairs.Keys.Select(k => (A1: ma1.Parts.MapPieces.First(p => p.Name == k.Item1), B1: mb1.Parts.MapPieces.First(p => p.Name == k.Item2))).ToList();
        var pair2 = pairParts.Select(x => (x.A1, x.B1, A2: Ds2(ma2, x.A1), B2: Ds2(mb2, x.B1))).ToList();
        int packHits = 0, ds1Hits = 0, cols = 0;
        string hostBytes = $"{b2.Substring(1, 2)},{b2.Substring(4, 2)}";
        foreach (var c2 in ma2.Parts.Collisions)
        {
            var conns = ma2.Parts.ConnectCollisions.Where(k => k.CollisionName == c2.Name && $"{k.MapID[0]:D2},{k.MapID[1]:D2}" == hostBytes).ToList();
            if (conns.Count == 0) continue;
            cols++;
            var mask = new uint[4];
            foreach (var k in conns) { mask[0] |= unchecked((uint)k.Unk44); mask[1] |= unchecked((uint)k.Unk48); mask[2] |= unchecked((uint)k.Unk4C); mask[3] |= unchecked((uint)k.Unk50); }
            int hits = pair2.Count(x => x.A2 != null && x.B2 != null && Any(x.A2.DrawGroups, c2.DispGroups) && Any(x.B2.DrawGroups, mask));
            packHits += hits;
            // DS1: the DS1 collision under it and its DS1 connects to B
            var c1 = ma1.Parts.Collisions.FirstOrDefault(c => c.ModelName.Length >= 7 && c2.ModelName.EndsWith(c.ModelName.Substring(1, 4)) && System.Numerics.Vector3.Distance(c.Position, c2.Position) < 0.05f)
                // a step ramp (rules SR3c / DG1: its base collision's groups and connects): the DS1 collision it copies
                ?? ma1.Parts.Collisions.FirstOrDefault(c => c.DispGroups.SequenceEqual(c2.DispGroups) && ma1.Parts.ConnectCollisions.Any(k => k.CollisionName == c.Name));
            int d1 = 0;
            if (c1 != null)
            {
                var m1 = new uint[4];
                foreach (var k in ma1.Parts.ConnectCollisions.Where(k => k.CollisionName == c1.Name && k.MapID[0] == int.Parse(b1.Substring(1, 2)) && k.MapID[1] == int.Parse(b1.Substring(4, 2))))
                {
                    var kd = Build.Maps.H2Merge.MapGroups(b1, k.DispGroups); // a merged target: its rule H2-G bits (as the MSB the layer serves)
                    for (int i = 0; i < 4; i++) m1[i] |= kd[i];
                }
                d1 = pair2.Count(x => Any(x.A1.DrawGroups, c1.DispGroups) && Any(x.B1.DrawGroups, m1));
                ds1Hits += d1;
            }
            if (hits > d1) Console.WriteLine($"Z-FIGHT {c2.Name}: {hits} coplanar {a2}/{b2} piece pairs drawn together (DS1: {d1})");
        }
        Console.WriteLine($"seam-zfight {a2} → {b2}: {pairs.Count} coplanar piece pairs ({pairs.Values.Sum()} triangles); {cols} connect-bearing collisions: pack draws {packHits} pair-instances together, DS1 {ds1Hits}");
        return packHits > ds1Hits ? 1 : 0;
    }
}

/// <summary>Session 11 (burg11, play-test fix): <c>floor-owner PACK MAP_A MAP_B [--steps FILE] [--grid X0 Z0 X1 Z1]</c> — where
/// the floor under the player belongs to the other map: every 0.5 m over A's walkable collision (or along a ds2-steps
/// route), the top floor of A vs B; a B floor within 0.3 m of (or above) A's = the game may stand the player on B's
/// collision (current map B: A drawn only as B's neighbour, by B's connects).</summary>
public static class FloorOwnerCommand
{
    public static int Run(string[] args)
    {
        string pack = args[1], a = args[2], b = args[3];
        var pa = Build.Maps.FloorProbe.FromFolder(pack, a);
        var pb = Build.Maps.FloorProbe.FromFolder(pack, b);
        var cells = new Dictionary<string, (int N, System.Numerics.Vector3 P, string Floor)>();
        int samples = 0, stolen = 0;
        // every 0.5 m over A's walkable triangles (their top floors)
        var lo = pa.Triangles.SelectMany(t => new[] { t.A, t.B, t.C }).Aggregate(new System.Numerics.Vector3(float.MaxValue), System.Numerics.Vector3.Min);
        var hi = pa.Triangles.SelectMany(t => new[] { t.A, t.B, t.C }).Aggregate(new System.Numerics.Vector3(float.MinValue), System.Numerics.Vector3.Max);
        for (float x = lo.X; x <= hi.X; x += 0.5f)
            for (float z = lo.Z; z <= hi.Z; z += 0.5f)
                foreach (var h in pa.Down(x, z).Where(h => h.NormalY >= 0.5f))
                {
                    samples++;
                    // B's floors near this A floor
                    var hb = pb.Down(x, z).Where(q => q.NormalY >= 0.5f && q.Y > h.Y - 0.05f && q.Y < h.Y + 1.8f).ToList();
                    if (hb.Count == 0) continue;
                    stolen++;
                    string key = $"{h.Part}|{hb[0].Part}";
                    var cur = cells.GetValueOrDefault(key);
                    cells[key] = (cur.N + 1, cur.N == 0 ? new System.Numerics.Vector3(x, h.Y, z) : cur.P, hb[0].Part);
                }
        foreach (var (k, v) in cells.OrderByDescending(kv => kv.Value.N).Take(40))
            Console.WriteLine($"{k.Split('|')[0]} (A) under {k.Split('|')[1]} (B): {v.N} samples, e.g. {v.P}");
        Console.WriteLine($"floor-owner {a} vs {b}: {samples} floor samples of A, {stolen} with a floor of B at or up to 1.8 m above");
        return 0;
    }
}

/// <summary>Session 11 (burg11, play-test fix): <c>route-draw PACK STEPS X Y Z MAP...</c> — walks a ds2-steps route (goto X Z,
/// from X Y Z) over the maps' collision, every 1 m: the collision under the player (its map = the current map), the
/// pieces and objects DS2 activates (own: piece DrawGroups ∩ collision DispGroups, objects ∩ its Unk44..50 area; each
/// neighbour its connects target: DrawGroups ∩ the OR of their masks) — the draw / object budget along the walk.</summary>
public static class RouteDrawCommand
{
    static bool Any(uint[] a, uint[] b) { for (int i = 0; i < 4; i++) if ((a[i] & b[i]) != 0) return true; return false; }
    public static int Run(string[] args)
    {
        string pack = args[1];
        var pts = File.ReadLines(args[2]).Where(l => l.StartsWith("goto ")).Select(l => l.Split(' ')).Select(c => new System.Numerics.Vector2(float.Parse(c[1]), float.Parse(c[2]))).ToList();
        var start = new System.Numerics.Vector3(float.Parse(args[3]), float.Parse(args[4]), float.Parse(args[5]));
        var maps = args.Skip(6).ToList();
        var msbs = maps.ToDictionary(m => m, m => SoulsFormats.MSB2.Read(Path.Combine(pack, "gamedata", "map", m, m + ".msb")));
        var probes = maps.ToDictionary(m => m, m => Build.Maps.FloorProbe.FromFolder(pack, m));
        float y = start.Y; var prev = new System.Numerics.Vector2(start.X, start.Z);
        int steps = 0, maxP = 0, maxO = 0; long sumP = 0, sumO = 0; string? lastKey = null;
        foreach (var target in pts)
        {
            float len = System.Numerics.Vector2.Distance(prev, target);
            for (float s = 0; s <= len; s += 1f)
            {
                var q = System.Numerics.Vector2.Lerp(prev, target, len == 0 ? 0 : s / len);
                // floor nearest the current height (≤ 1 m up, ≤ 3 m down), any map
                string? cm = null; string? cpart = null; float best = float.MaxValue;
                foreach (var m in maps)
                    foreach (var h in probes[m].Down(q.X, q.Y).Where(h => h.NormalY >= 0.5f && h.Y <= y + 1f && h.Y >= y - 3f))
                        if (MathF.Abs(h.Y - y) < best || (MathF.Abs(MathF.Abs(h.Y - y) - best) < 0.05f && h.Y > y)) { best = MathF.Abs(h.Y - y); cm = m; cpart = h.Part; }
                if (cm == null) continue;
                y = y - best <= y ? probes[cm].Down(q.X, q.Y).Where(h => h.Part == cpart).Select(h => h.Y).DefaultIfEmpty(y).First() : y;
                var col = msbs[cm].Parts.Collisions.FirstOrDefault(c => c.Name == cpart);
                if (col == null) continue;
                var area = new[] { unchecked((uint)col.Unk44), unchecked((uint)col.Unk48), unchecked((uint)col.Unk4C), unchecked((uint)col.Unk50) };
                int pieces = msbs[cm].Parts.MapPieces.Count(p => Any(p.DrawGroups, col.DispGroups));
                int objects = msbs[cm].Parts.Objects.Count(o => Any(o.DrawGroups, area));
                var nb = new List<string>();
                foreach (var g in msbs[cm].Parts.ConnectCollisions.Where(k => k.CollisionName == col.Name).GroupBy(k => $"m{k.MapID[0]:D2}_{k.MapID[1]:D2}_{k.MapID[2]:D2}_{k.MapID[3]:D2}"))
                {
                    if (!msbs.TryGetValue(g.Key, out var t)) continue;
                    var mask = new uint[4];
                    foreach (var k in g) { mask[0] |= unchecked((uint)k.Unk44); mask[1] |= unchecked((uint)k.Unk48); mask[2] |= unchecked((uint)k.Unk4C); mask[3] |= unchecked((uint)k.Unk50); }
                    int np = t.Parts.MapPieces.Count(p => Any(p.DrawGroups, mask)), no = t.Parts.Objects.Count(o => Any(o.DrawGroups, mask));
                    pieces += np; objects += no; nb.Add($"{g.Key}: {np} pieces {no} objects");
                }
                steps++; sumP += pieces; sumO += objects; maxP = Math.Max(maxP, pieces); maxO = Math.Max(maxO, objects);
                string key = $"{cm} {col.Name}";
                if (key != lastKey) Console.WriteLine($"  {q.X,8:0.0} {y,7:0.0} {q.Y,8:0.0}  {key}: {pieces} pieces, {objects} objects{(nb.Count > 0 ? " (incl. " + string.Join("; ", nb) + ")" : "")}");
                lastKey = key;
            }
            prev = target;
        }
        Console.WriteLine($"route-draw: {steps} samples: pieces max {maxP} mean {(double)sumP / Math.Max(1, steps):0}, objects max {maxO} mean {(double)sumO / Math.Max(1, steps):0}");
        return 0;
    }
}
