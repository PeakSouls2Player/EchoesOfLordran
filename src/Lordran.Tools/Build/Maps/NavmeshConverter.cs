using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// DS1 navmeshes (map\mAA_BB_00_00\*.nvmbnd: one NVM per MSB navmesh part) → one DS2 NGP mesh. NGP layout (worked
/// out from all 28 vanilla files with <c>ngp-dump</c>, session 5):
/// <list type="bullet">
/// <item>File Unk1C = navId&lt;&lt;24 | 0xFFFFFF; mesh Unk00 = navId&lt;&lt;24 | mesh&lt;&lt;17 | 0x1FFFF (navId: per map, TB 0,
/// Majula 1 … 0x25); mesh Unk08 = a per-file constant (copied from the template); Unk30/Unk32 = this mesh's range in
/// StructA (0/-1 when the file has no inter-mesh links; m50_38 and m20_26 are single meshes with StructA..D empty).</item>
/// <item>Faces wind clockwise seen from above (DS1's NVMs counter-clockwise). Face Unk06/08/0A = the edges V1-V2, V2-V3,
/// V3-V1 as edge*2 + 1 if the edge runs the other way. Struct2 per face = flags (7 = ordinary floor, ~85%).</item>
/// <item>Struct4 = undirected edges in first-seen order: (from, to, next edge, prev edge in the first face, next, prev in
/// the second face or 32767, first face, second face or 32767).</item>
/// <item>Struct5 = face tree: axis X at even depth, Z at odd; Left = faces reaching ≥ split, Right = faces reaching ≤
/// split (straddling faces in both); leaves ≈ 28 faces, depth ≤ 11.</item>
/// </list>
/// </summary>
public static class NavmeshConverter
{
    const short None = 32767;
    /// <summary>Rule NV-H2: faces per mesh before a member's parts split (edges ≈ 1.5 × faces must stay below 16383).</summary>
    const int MaxFacesPerMesh = 9000;

    public static NGP Convert(string dsrRoot, string ds1Map, MSB1 msb1, NGP template, Vector3 mapOffset, out string report)
    {
        var bnd = BND3.Read(Path.Combine(dsrRoot, "map", ds1Map, ds1Map + ".nvmbnd.dcx"));
        var nvms = bnd.Files.ToDictionary(f => Path.GetFileNameWithoutExtension(f.Name)[..7], f => NVM.Read(f.Bytes), StringComparer.OrdinalIgnoreCase);

        // session 11 (burg11, round 9), rule NV-H2: one NGP mesh per group of DS1 navmesh parts — the members of a merged map
        // (rule H2) apart, and a member's parts split in MSB order where one mesh would pass NGP's 16-bit edge / vertex ids
        // (≈ 9000 faces per mesh); a map that fits keeps its single mesh (mesh index 0, as before)
        var groups = new List<List<MSB1.Part.Navmesh>>();
        foreach (var memberParts in msb1.Parts.Navmeshes.GroupBy(x => H2Merge.MemberOf(ds1Map, x.Name)))
        {
            var cur = new List<MSB1.Part.Navmesh>(); int curFaces = 0;
            foreach (var x in memberParts)
            {
                int n = nvms.TryGetValue(x.ModelName, out var nv) ? nv.Triangles.Count : 0;
                if (cur.Count > 0 && curFaces + n > MaxFacesPerMesh) { groups.Add(cur); cur = []; curFaces = 0; }
                cur.Add(x); curFaces += n;
            }
            if (cur.Count > 0) groups.Add(cur);
        }
        if (groups.Count == 0) groups.Add([]);
        var meshes = new List<NGP.Mesh>();
        var reports = new List<string>();
        for (int gi = 0; gi < groups.Count; gi++) { meshes.Add(One(groups[gi], gi, out string r)); reports.Add(r); }
        report = groups.Count == 1 ? "navmesh: " + reports[0] : $"navmesh (rule NV-H2): {groups.Count} meshes: " + string.Join("; ", reports.Select((r, gi) => $"[{gi}] {r}"));
        var ngp = new NGP { BigEndian = false, Version = template.Version, Unk1C = template.Unk1C, StructAs = new(), StructBs = new(), StructCs = new(), StructDs = new(), Meshes = meshes };
        return ngp;

        NGP.Mesh One(List<MSB1.Part.Navmesh> group, int meshIndex, out string meshReport)
        {
            // ---- welded vertices (1 cm) and faces, world → map-local
            var verts = new List<Vector3>();
            var index = new Dictionary<(long, long, long), int>();
            int Vert(Vector3 v)
            {
                var k = ((long)MathF.Round(v.X * 100), (long)MathF.Round(v.Y * 100), (long)MathF.Round(v.Z * 100));
                if (!index.TryGetValue(k, out int i)) { i = verts.Count; verts.Add(v); index[k] = i; }
                return i;
            }
            var faces = new List<(int A, int B, int C)>();
            int skipped = 0, parts = 0;
            foreach (var part in group)
            {
                if (!nvms.TryGetValue(part.ModelName, out var nvm)) continue;
                parts++;
                var m = Matrix4x4.CreateScale(part.Scale)
                    * Matrix4x4.CreateRotationZ(part.Rotation.Z * MathF.PI / 180) * Matrix4x4.CreateRotationX(part.Rotation.X * MathF.PI / 180)
                    * Matrix4x4.CreateRotationY(part.Rotation.Y * MathF.PI / 180) * Matrix4x4.CreateTranslation(part.Position - mapOffset);
                foreach (var t in nvm.Triangles)
                {
                    if ((t.Flags & (NVM.TriangleFlags.DEGENERATE | NVM.TriangleFlags.DISABLE | NVM.TriangleFlags.INSIDE_WALL)) != 0) { skipped++; continue; }
                    int a = Vert(Vector3.Transform(nvm.Vertices[t.VertexIndex1], m));
                    int b = Vert(Vector3.Transform(nvm.Vertices[t.VertexIndex2], m));
                    int c = Vert(Vector3.Transform(nvm.Vertices[t.VertexIndex3], m));
                    if (a == b || b == c || a == c) { skipped++; continue; }
                    faces.Add((a, c, b)); // DS1 counter-clockwise → DS2 clockwise
                }
            }

            // ---- edges
            var mesh = new NGP.Mesh();
            int navId = (int)((uint)template.Unk1C >> 24);
            mesh.Unk00 = navId << 24 | meshIndex << 17 | 0x1FFFF;
            mesh.Unk08 = template.Meshes[0].Unk08;
            mesh.Unk30 = 0; mesh.Unk32 = -1;
            mesh.Vertices.AddRange(verts);
            var edgeOf = new Dictionary<(int, int), int>();
            var edges = new List<NGP.Struct4>();
            int nonManifold = 0;
            for (int f = 0; f < faces.Count; f++)
            {
                var (a, b, c) = faces[f];
                var he = new[] { (a, b), (b, c), (c, a) };
                var ids = new int[3]; var rev = new bool[3]; var second = new bool[3];
                for (int i = 0; i < 3; i++)
                {
                    var (p, q) = he[i];
                    var key = p < q ? (p, q) : (q, p);
                    if (edgeOf.TryGetValue(key, out int e) && edges[e].Unk0E == None && edges[e].Unk00 == q)
                    {
                        ids[i] = e; rev[i] = true; second[i] = true;
                        edges[e].Unk0E = (short)f;
                        edgeOf.Remove(key); // a third face on this edge gets its own (border) edge
                    }
                    else
                    {
                        if (edgeOf.ContainsKey(key)) nonManifold++;
                        ids[i] = edges.Count;
                        edges.Add(new NGP.Struct4 { Unk00 = (short)p, Unk02 = (short)q, Unk08 = None, Unk0A = None, Unk0C = (short)f, Unk0E = None });
                        edgeOf[key] = ids[i];
                    }
                }
                for (int i = 0; i < 3; i++)
                {
                    var e = edges[ids[i]];
                    short next = (short)ids[(i + 1) % 3], prev = (short)ids[(i + 2) % 3];
                    if (second[i]) { e.Unk08 = next; e.Unk0A = prev; } else { e.Unk04 = next; e.Unk06 = prev; }
                }
                mesh.Faces.Add(new NGP.Face
                {
                    V1 = (short)a, V2 = (short)b, V3 = (short)c,
                    Unk06 = (short)(ids[0] * 2 + (rev[0] ? 1 : 0)), Unk08 = (short)(ids[1] * 2 + (rev[1] ? 1 : 0)), Unk0A = (short)(ids[2] * 2 + (rev[2] ? 1 : 0)),
                });
                mesh.Struct2s.Add(7);
            }
            if (edges.Count * 2 + 1 > short.MaxValue || verts.Count > short.MaxValue) throw new InvalidOperationException("navmesh too big for one NGP mesh");
            mesh.Struct4s.AddRange(edges);
            mesh.BoundingBoxMin = verts.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
            mesh.BoundingBoxMax = verts.Aggregate(new Vector3(float.MinValue), Vector3.Max);

            // ---- face tree
            (float Lo, float Hi) Range(int f, int axis)
            {
                var (a, b, c) = faces[f];
                float x = Axis(verts[a], axis), y = Axis(verts[b], axis), z = Axis(verts[c], axis);
                return (Math.Min(x, Math.Min(y, z)), Math.Max(x, Math.Max(y, z)));
            }
            int leaves = 0, depthMax = 0;
            NGP.Struct5 Node(List<int> set, int depth)
            {
                depthMax = Math.Max(depthMax, depth);
                int axis = depth % 2 == 0 ? 0 : 2;
                var centres = set.Select(f => { var r = Range(f, axis); return (r.Lo + r.Hi) / 2; }).OrderBy(x => x).ToList();
                float split = centres[centres.Count / 2];
                var node = new NGP.Struct5 { Unk00 = split };
                if (set.Count > 24 && depth < 11)
                {
                    var left = set.Where(f => Range(f, axis).Hi >= split).ToList();
                    var right = set.Where(f => Range(f, axis).Lo <= split).ToList();
                    if (left.Count < set.Count && right.Count < set.Count && left.Count > 0 && right.Count > 0)
                    {
                        node.Left = Node(left, depth + 1);
                        node.Right = Node(right, depth + 1);
                        return node;
                    }
                }
                leaves++;
                node.FaceIndices = set.Select(f => (short)f).ToList();
                return node;
            }
            mesh.Struct5Root = Node(Enumerable.Range(0, faces.Count).ToList(), 0);

            // connected components (faces joined across shared edges), for the report
            var comp = new int[faces.Count]; Array.Fill(comp, -1);
            var sizes = new List<int>();
            for (int s = 0; s < faces.Count; s++)
            {
                if (comp[s] >= 0) continue;
                int n = 0; var stack = new Stack<int>(); stack.Push(s); comp[s] = sizes.Count;
                while (stack.Count > 0)
                {
                    int f = stack.Pop(); n++;
                    foreach (short r in new[] { mesh.Faces[f].Unk06, mesh.Faces[f].Unk08, mesh.Faces[f].Unk0A })
                    {
                        var e = edges[r / 2];
                        int other = e.Unk0C == f ? e.Unk0E : e.Unk0C;
                        if (other != None && comp[other] < 0) { comp[other] = sizes.Count; stack.Push(other); }
                    }
                }
                sizes.Add(n);
            }
            meshReport = $"{parts} DS1 navmesh parts → 1 mesh, {verts.Count} vertices, {faces.Count} faces ({skipped} skipped), {edges.Count} edges ({nonManifold} non-manifold), tree {leaves} leaves depth {depthMax}, " +
                $"{sizes.Count} connected pieces (largest {string.Join(", ", sizes.OrderByDescending(x => x).Take(6))})";
            return mesh;
        }
    }


    static float Axis(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
}
