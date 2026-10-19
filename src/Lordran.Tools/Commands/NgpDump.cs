using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// ngp-dump MAP|FILE [--mesh N] [--all] — DS2 navmesh (NGP) structure survey for writing our own: header structs A..D,
/// per-mesh fields, and checks of hypotheses about the unknown fields (face Unk06/08/0A = neighbour faces, Struct5 = a
/// split tree over the faces).
/// </summary>
public static class NgpDump
{
    public static int Run(string[] args)
    {
        string a = args[1];
        if (a == "--survey") return Survey();
        if (a == "--ds1") return Ds1(args[2]);
        string path = File.Exists(a) ? a : Path.Combine(ExtractDs2.DefaultOut, "map", a, a + ".ngp");
        var ngp = NGP.Read(path);
        int only = args.Contains("--mesh") ? int.Parse(args[Array.IndexOf(args, "--mesh") + 1]) : -1;
        bool all = args.Contains("--all");
        Console.WriteLine($"{Path.GetFileName(path)}: version {ngp.Version}, meshes {ngp.Meshes.Count}, A {ngp.StructAs.Count}, B {ngp.StructBs.Count}, C {ngp.StructCs.Count}, D {ngp.StructDs.Count}, Unk1C {ngp.Unk1C}");

        int shown = all ? int.MaxValue : 12;
        Console.WriteLine("-- StructA (Unk00 xyz, Unk0C, Unk10, 14..22)");
        foreach (var s in ngp.StructAs.Take(shown))
            Console.WriteLine($"  {V(s.Unk00)} {s.Unk0C:0.###} {s.Unk10} | {s.Unk14} {s.Unk16} {s.Unk18} {s.Unk1A} {s.Unk1C} {s.Unk1E} {s.Unk20} {s.Unk22}");
        Console.WriteLine("-- StructB");
        foreach (var s in ngp.StructBs.Take(shown)) Console.WriteLine($"  {s.Unk00} {s.Unk04} {s.Unk08}");
        Console.WriteLine("-- StructC: " + string.Join(" ", ngp.StructCs.Take(shown * 3).Select(x => $"{x:X8}")));
        Console.WriteLine("-- StructD: " + string.Join(" ", ngp.StructDs.Take(shown * 3)));

        for (int mi = 0; mi < ngp.Meshes.Count; mi++)
        {
            if (only >= 0 && mi != only) continue;
            var m = ngp.Meshes[mi];
            var bbMin = m.Vertices.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
            var bbMax = m.Vertices.Aggregate(new Vector3(float.MinValue), Vector3.Max);
            // neighbour hypothesis: Unk06 = across V1-V2, Unk08 = V2-V3, Unk0A = V3-V1 (-1 = none)
            int nbOk = 0, nbBad = 0, nbNone = 0, borderEdges = 0;
            var edgeFaces = new Dictionary<(int, int), List<int>>();
            for (int f = 0; f < m.Faces.Count; f++)
            {
                var fc = m.Faces[f];
                foreach (var (p, q) in new[] { (fc.V1, fc.V2), (fc.V2, fc.V3), (fc.V3, fc.V1) })
                {
                    var k = p < q ? (p, q) : (q, p);
                    if (!edgeFaces.TryGetValue(k, out var l)) edgeFaces[k] = l = new();
                    l.Add(f);
                }
            }
            for (int f = 0; f < m.Faces.Count; f++)
            {
                var fc = m.Faces[f];
                var e = new[] { (fc.V1, fc.V2, fc.Unk06), (fc.V2, fc.V3, fc.Unk08), (fc.V3, fc.V1, fc.Unk0A) };
                foreach (var (p, q, nb) in e)
                {
                    var k = p < q ? (p, q) : (q, p);
                    int other = edgeFaces[k].FirstOrDefault(x => x != f, -1);
                    if (other < 0) borderEdges++;
                    if (nb == -1) nbNone++;
                    else if (nb == other) nbOk++;
                    else nbBad++;
                }
            }
            int depth = Depth(m.Struct5Root), nodes = Count(m.Struct5Root), leafFaces = LeafFaces(m.Struct5Root);
            if (only < 0 && !all && mi >= 6)
            {
                if (mi == 6) Console.WriteLine("  ... (--all or --mesh N for the rest)");
                continue;
            }
            Console.WriteLine($"== mesh {mi}: Unk00 {m.Unk00} Unk08 {m.Unk08} Unk30 {m.Unk30} Unk32 {m.Unk32} verts {m.Vertices.Count} faces {m.Faces.Count} s4 {m.Struct4s.Count}");
            Console.WriteLine($"   bbox {V(m.BoundingBoxMin)}..{V(m.BoundingBoxMax)} computed {V(bbMin)}..{V(bbMax)}");
            Console.WriteLine($"   neighbours: ok {nbOk} bad {nbBad} none {nbNone} (border edges {borderEdges})");
            Console.WriteLine($"   tree: nodes {nodes} depth {depth} leaf face refs {leafFaces}; root split {m.Struct5Root.Unk00}");
            Console.WriteLine("   struct2 (per face): " + string.Join(" ", m.Struct2s.Take(24).Select(x => $"{x:X}")) + "  distinct: " + string.Join(",", m.Struct2s.Distinct().Take(16).Select(x => $"{x:X}")));
            Console.WriteLine("   faces: " + string.Join(" | ", m.Faces.Take(6).Select(f => $"{f.V1},{f.V2},{f.V3} {f.Unk06},{f.Unk08},{f.Unk0A}")));
            foreach (var s in m.Struct4s.Take(all ? 1000 : 8))
                Console.WriteLine($"   s4: {s.Unk00} {s.Unk02} {s.Unk04} {s.Unk06} {s.Unk08} {s.Unk0A} {s.Unk0C} {s.Unk0E}");
            if (only >= 0) DumpTree(m.Struct5Root, 0, m);
        }
        return 0;
    }

    /// <summary>
    /// Every vanilla NGP: header ids, mesh Unk00/Unk08, face flag histogram, leaf sizes, and a check of the tree rule
    /// "axis = X at even depth, Z at odd; Left holds faces reaching ≥ split, Right faces reaching ≤ split".
    /// </summary>
    static int Survey()
    {
        foreach (var f in Directory.GetFiles(Path.Combine(ExtractDs2.DefaultOut, "map"), "*.ngp", SearchOption.AllDirectories).OrderBy(x => x))
        {
            var ngp = NGP.Read(f);
            var flags = new Dictionary<int, int>();
            int ok = 0, bad = 0, okSwap = 0, badSwap = 0, maxLeaf = 0, maxDepth = 0, maxFaces = 0, maxEdges = 0;
            var unk08 = new HashSet<int>();
            foreach (var m in ngp.Meshes)
            {
                unk08.Add(m.Unk08);
                maxFaces = Math.Max(maxFaces, m.Faces.Count); maxEdges = Math.Max(maxEdges, m.Struct4s.Count);
                foreach (var x in m.Struct2s) flags[x] = flags.GetValueOrDefault(x) + 1;
                maxDepth = Math.Max(maxDepth, Depth(m.Struct5Root));
                void Walk(NGP.Struct5 n, int d)
                {
                    if (n == null) return;
                    if (n.FaceIndices != null) maxLeaf = Math.Max(maxLeaf, n.FaceIndices.Count);
                    int axis = d % 2 == 0 ? 0 : 2;
                    float Lo(int fi) { var fc = m.Faces[fi]; return Math.Min(C(m.Vertices[fc.V1], axis), Math.Min(C(m.Vertices[fc.V2], axis), C(m.Vertices[fc.V3], axis))); }
                    float Hi(int fi) { var fc = m.Faces[fi]; return Math.Max(C(m.Vertices[fc.V1], axis), Math.Max(C(m.Vertices[fc.V2], axis), C(m.Vertices[fc.V3], axis))); }
                    foreach (int fi in All(n.Left)) { if (Hi(fi) >= n.Unk00 - 1e-3f) ok++; else bad++; if (Lo(fi) <= n.Unk00 + 1e-3f) okSwap++; else badSwap++; }
                    foreach (int fi in All(n.Right)) { if (Lo(fi) <= n.Unk00 + 1e-3f) ok++; else bad++; if (Hi(fi) >= n.Unk00 - 1e-3f) okSwap++; else badSwap++; }
                    Walk(n.Left, d + 1); Walk(n.Right, d + 1);
                }
                Walk(m.Struct5Root, 0);
            }
            Console.WriteLine($"{Path.GetFileNameWithoutExtension(f)} meshes {ngp.Meshes.Count,3} A {ngp.StructAs.Count,3} Unk1C {ngp.Unk1C:X8} mesh0 {ngp.Meshes[0].Unk00:X8} Unk08 {string.Join(",", unk08.Select(x => x.ToString("X")))} " +
                $"maxFaces {maxFaces} maxEdges {maxEdges} leaf≤{maxLeaf} depth≤{maxDepth} rule ok {ok} bad {bad} (swapped ok {okSwap} bad {badSwap}) flags " +
                string.Join(",", flags.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Key:X}:{kv.Value}")));
        }
        return 0;
    }

    /// <summary>A DS1 map's navmeshes: nvmbnd files, triangle counts and flags, and the MSB navmesh parts placing them.</summary>
    static int Ds1(string map)
    {
        string dir = Path.Combine(Program.DefaultDsrRoot, "map", map);
        var bnd = BND3.Read(Path.Combine(dir, map + ".nvmbnd.dcx"));
        var msb = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", map + ".msb"));
        foreach (var p in msb.Parts.Navmeshes)
            Console.WriteLine($"part {p.Name} model {p.ModelName} pos {V(p.Position)} rot {V(p.Rotation)} scale {V(p.Scale)} entity {p.EntityID}");
        int total = 0;
        foreach (var f in bnd.Files)
        {
            var nvm = NVM.Read(f.Bytes);
            total += nvm.Triangles.Count;
            var fl = nvm.Triangles.GroupBy(t => t.Flags).OrderByDescending(g => g.Count()).Take(6).Select(g => $"{g.Key}:{g.Count()}");
            var min = nvm.Vertices.Aggregate(new Vector3(float.MaxValue), Vector3.Min); var max = nvm.Vertices.Aggregate(new Vector3(float.MinValue), Vector3.Max);
            Console.WriteLine($"{Path.GetFileName(f.Name)}: verts {nvm.Vertices.Count} tris {nvm.Triangles.Count} entities {nvm.Entities.Count} extent {V(min)}..{V(max)} flags {string.Join(" ", fl)}");
        }
        Console.WriteLine($"total triangles {total}");
        int up = 0, down = 0;
        foreach (var f in bnd.Files)
        {
            var nvm = NVM.Read(f.Bytes);
            foreach (var t in nvm.Triangles)
            {
                var n = Vector3.Cross(nvm.Vertices[t.VertexIndex2] - nvm.Vertices[t.VertexIndex1], nvm.Vertices[t.VertexIndex3] - nvm.Vertices[t.VertexIndex1]);
                if (n.Y > 0) up++; else down++;
            }
        }
        int vup = 0, vdown = 0;
        var tb = NGP.Read(Path.Combine(ExtractDs2.DefaultOut, "map", "m10_02_00_00", "m10_02_00_00.ngp"));
        foreach (var m in tb.Meshes)
            foreach (var t in m.Faces)
            {
                var n = Vector3.Cross(m.Vertices[t.V2] - m.Vertices[t.V1], m.Vertices[t.V3] - m.Vertices[t.V1]);
                if (n.Y > 0) vup++; else vdown++;
            }
        Console.WriteLine($"winding (cross(v2-v1, v3-v1).Y > 0): DS1 up {up} down {down}; DS2 TB up {vup} down {vdown}");
        return 0;
    }

    static float C(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
    static IEnumerable<int> All(NGP.Struct5 n) => n == null ? Enumerable.Empty<int>() : (n.FaceIndices ?? new List<short>()).Select(x => (int)x).Concat(All(n.Left)).Concat(All(n.Right));

    static string V(Vector3 v) => $"({v.X:0.##},{v.Y:0.##},{v.Z:0.##})";
    static int Depth(NGP.Struct5 n) => n == null ? 0 : 1 + Math.Max(Depth(n.Left), Depth(n.Right));
    static int Count(NGP.Struct5 n) => n == null ? 0 : 1 + Count(n.Left) + Count(n.Right);
    static int LeafFaces(NGP.Struct5 n) => n == null ? 0 : (n.FaceIndices?.Count ?? 0) + LeafFaces(n.Left) + LeafFaces(n.Right);

    static void DumpTree(NGP.Struct5 n, int d, NGP.Mesh m)
    {
        if (n == null || d > 6) return;
        string faces = n.FaceIndices == null ? "" : $" faces[{n.FaceIndices.Count}] " + string.Join(",", n.FaceIndices.Take(10));
        string ext = "";
        if (n.FaceIndices is { Count: > 0 })
        {
            var vs = n.FaceIndices.SelectMany(i => new[] { m.Vertices[m.Faces[i].V1], m.Vertices[m.Faces[i].V2], m.Vertices[m.Faces[i].V3] }).ToList();
            ext = $" ext {V(vs.Aggregate(new Vector3(float.MaxValue), Vector3.Min))}..{V(vs.Aggregate(new Vector3(float.MinValue), Vector3.Max))}";
        }
        Console.WriteLine($"{new string(' ', 4 + d * 2)}split {n.Unk00:0.###}{faces}{ext}");
        DumpTree(n.Left, d + 1, m);
        DumpTree(n.Right, d + 1, m);
    }
}
