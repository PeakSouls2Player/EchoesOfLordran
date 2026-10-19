using System.Numerics;
using Lordran.Tools.Formats.Havok;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Session 9 (track npcplace): ray casts over a map's collision triangles, for the placement rules (character height,
/// test-warp spots) and their offline checks (`spot-check`, `place-survey`).
/// Sources: <see cref="FromDs2Map"/> = the DS2 collision a pack holds (MSB2 collision parts placed with their transform
/// over the `h` archive's packfiles: our converted DS1 filter-8 collision and the step ramps h0A_99NN, exactly what the
/// game loads); <see cref="FromDs1"/> = DSR's own collision (every hit filter, for the DS1 side of the evidence);
/// <see cref="FromDs1Pieces"/> = DSR's visible map pieces (what the player sees, with or without collision).
/// </summary>
public sealed class FloorProbe
{
    public readonly record struct Tri(Vector3 A, Vector3 B, Vector3 C, string Part);
    public readonly record struct Hit(float Y, float NormalY, string Part);

    readonly List<Tri> tris;
    readonly Dictionary<(int, int), List<int>> grid = [];
    const float Cell = 2f;
    public int Count => tris.Count;
    public IReadOnlyList<Tri> Triangles => tris;

    public FloorProbe(IEnumerable<Tri> triangles)
    {
        tris = triangles.Where(t => Sane(t.A) && Sane(t.B) && Sane(t.C)).ToList();
        for (int i = 0; i < tris.Count; i++)
        {
            var t = tris[i];
            var lo = Vector3.Min(t.A, Vector3.Min(t.B, t.C)); var hi = Vector3.Max(t.A, Vector3.Max(t.B, t.C));
            for (int x = Key(lo.X); x <= Key(hi.X); x++)
                for (int z = Key(lo.Z); z <= Key(hi.Z); z++)
                {
                    if (!grid.TryGetValue((x, z), out var l)) grid[(x, z)] = l = [];
                    l.Add(i);
                }
        }
    }

    static int Key(float v) => (int)MathF.Floor(v / Cell);
    static bool Sane(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z) && p.Length() < 1e5f;

    /// <summary>Every triangle the vertical line through (x, z) crosses, highest first (normal Y &gt; 0 = faces up).</summary>
    public List<Hit> Down(float x, float z)
    {
        var hits = new List<Hit>();
        if (!grid.TryGetValue((Key(x), Key(z)), out var list)) return hits;
        foreach (int i in list)
        {
            var (a, b, c, part) = (tris[i].A, tris[i].B, tris[i].C, tris[i].Part);
            float d = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
            if (MathF.Abs(d) < 1e-9f) continue;
            float l1 = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / d, l2 = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / d, l3 = 1 - l1 - l2;
            const float e = -1e-5f;
            if (l1 < e || l2 < e || l3 < e) continue;
            var n = Vector3.Cross(b - a, c - a);
            float ny = n.Length() > 1e-9f ? MathF.Abs(Vector3.Normalize(n).Y) : 0;
            hits.Add(new Hit(l1 * a.Y + l2 * b.Y + l3 * c.Y, ny, part));
        }
        hits.Sort((p, q) => q.Y.CompareTo(p.Y));
        // the same surface listed twice (overlapping DS1 parts, both sides of a shared edge): keep one per 1 cm
        var unique = new List<Hit>();
        foreach (var h in hits) if (unique.Count == 0 || unique[^1].Y - h.Y > 0.01f) unique.Add(h);
        return unique;
    }

    /// <summary>The floor a character standing at <paramref name="p"/> rests on: the highest walkable surface (|normal Y| ≥
    /// <paramref name="minNormalY"/>, i.e. ≤ ~60° steep) at most <paramref name="above"/> over p.Y and at most
    /// <paramref name="below"/> under it, or null when there is none (nothing to land on: the character falls).</summary>
    public Hit? Floor(Vector3 p, float above = 0.6f, float below = 50f, float minNormalY = 0.5f)
    {
        foreach (var h in Down(p.X, p.Z))
            if (h.NormalY >= minNormalY && h.Y <= p.Y + above && h.Y >= p.Y - below) return h;
        return null;
    }

    /// <summary>Distance along a horizontal ray from <paramref name="o"/> in direction <paramref name="dir"/> (XZ, any
    /// length) to the first triangle within <paramref name="max"/>, or null.</summary>
    public float? Ray(Vector3 o, Vector3 dir, float max)
    {
        dir = Vector3.Normalize(dir);
        float? best = null;
        var seen = new HashSet<int>();
        for (float s = 0; s <= max + Cell; s += Cell * 0.5f)
        {
            var q = o + dir * MathF.Min(s, max);
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                if (grid.TryGetValue((Key(q.X) + dx, Key(q.Z) + dz), out var list))
                    foreach (int i in list)
                        if (seen.Add(i) && Intersect(o, dir, tris[i]) is float t && t <= max && (best == null || t < best)) best = t;
        }
        return best;
    }

    /// <summary>Möller–Trumbore, both faces.</summary>
    static float? Intersect(Vector3 o, Vector3 d, Tri tri)
    {
        var e1 = tri.B - tri.A; var e2 = tri.C - tri.A;
        var p = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-9f) return null;
        float inv = 1 / det;
        var s = o - tri.A;
        float u = Vector3.Dot(s, p) * inv;
        if (u < 0 || u > 1) return null;
        var q = Vector3.Cross(s, e1);
        float v = Vector3.Dot(d, q) * inv;
        if (v < 0 || u + v > 1) return null;
        float t = Vector3.Dot(e2, q) * inv;
        return t >= 0 ? t : null;
    }

    /// <summary>MSB part transform (shared by DS1 and DS2 parts: scale, Z, X, Y, translation; as ColSurvey.Place).</summary>
    public static Matrix4x4 Place(Vector3 pos, Vector3 rotDeg, Vector3 scale) => Commands.ColSurvey.Place(pos, rotDeg, scale);

    /// <summary>
    /// The DS2 map collision of <paramref name="ds2Map"/> as the game loads it from a pack: `map/MAP/MAP.msb` collision
    /// parts × `model/map/hAA_BB_CC_DD.hkxbhd/bdt` packfiles (hkpMoppBvTreeShape → CustomParamStorageExtendedMeshShape
    /// mesh storage: float4 vertices, u16 indices × 4 per triangle). <paramref name="file"/> reads a gamedata-relative path
    /// (null = missing). Includes our step ramps (extra parts h0A_99NN at the origin).
    /// </summary>
    public static FloorProbe FromDs2Map(Func<string, byte[]?> file, string ds2Map)
    {
        byte[] msbBytes = file($"map/{ds2Map}/{ds2Map}.msb") ?? throw new FileNotFoundException($"map/{ds2Map}/{ds2Map}.msb");
        var msb = MSB2.Read(msbBytes);
        string tail = ds2Map[1..];
        var bhd = file($"model/map/h{tail}.hkxbhd") ?? throw new FileNotFoundException($"model/map/h{tail}.hkxbhd");
        var bdt = file($"model/map/h{tail}.hkxbdt") ?? throw new FileNotFoundException($"model/map/h{tail}.hkxbdt");
        var bxf = BXF4.Read(bhd, bdt);
        var models = new Dictionary<string, List<(Vector3, Vector3, Vector3)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in bxf.Files.Where(f => f.Name.EndsWith(".hkx.dcx", StringComparison.OrdinalIgnoreCase)))
        {
            string name = Path.GetFileName(f.Name);
            name = name[..name.IndexOf('.')];
            models[name] = Ds2Triangles(DCX.Decompress(f.Bytes));
        }
        var result = new List<Tri>();
        foreach (var part in msb.Parts.Collisions)
        {
            if (!models.TryGetValue(part.ModelName, out var local)) continue;
            var m = Place(part.Position, part.Rotation, part.Scale);
            foreach (var (a, b, c) in local) result.Add(new Tri(Vector3.Transform(a, m), Vector3.Transform(b, m), Vector3.Transform(c, m), part.Name));
        }
        return new FloorProbe(result);
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], FloorProbe> packCache = new();

    /// <summary>The collision a pack under construction holds for the map (MapBuilder has written it); one probe per
    /// written h-archive (cached on its bytes: NpcPorter, AsylumEnemies and MapLinks share it).</summary>
    public static FloorProbe FromPack(Pack pack, string ds2Map)
    {
        var key = pack.TryGetFile("gamedata", $"model/map/h{ds2Map[1..]}.hkxbhd") ?? throw new FileNotFoundException($"the pack has no collision for {ds2Map} yet");
        return packCache.GetValue(key, _ => FromDs2Map(p => pack.TryGetFile("gamedata", p), ds2Map));
    }

    /// <summary>A built pack folder (PACK\gamedata\…), or the extracted vanilla game data (data\ds2: files in place).</summary>
    public static FloorProbe FromFolder(string root, string ds2Map)
    {
        string gamedata = Directory.Exists(Path.Combine(root, "gamedata")) ? Path.Combine(root, "gamedata") : root;
        return FromDs2Map(p => File.Exists(Path.Combine(gamedata, p)) ? File.ReadAllBytes(Path.Combine(gamedata, p)) : null, ds2Map);
    }

    /// <summary>Triangles of a DS2 collision packfile (every rigid body's MOPP mesh storage), in the body's frame.</summary>
    public static List<(Vector3, Vector3, Vector3)> Ds2Triangles(byte[] packfile)
    {
        var tris = new List<(Vector3, Vector3, Vector3)>();
        var root = new HkPackfile(packfile).ReadRoot();
        var sys = (HkPObj)((HkPObj)root.List("namedVariants")[0]!)["variant"]!;
        foreach (HkPObj s in sys.List("systems"))
            foreach (HkPObj rb in s.List("rigidBodies"))
            {
                if (rb.Get("collidable")["shape"] is not HkPObj mopp || mopp.Class != "hkpMoppBvTreeShape") continue;
                var mesh = (HkPObj)mopp.Get("child")["childShape"]!;
                foreach (HkPObj st in mesh.List("meshstorage"))
                {
                    var verts = st.List("vertices").Select(v => { var a = (float[])v!; return new Vector3(a[0], a[1], a[2]); }).ToList();
                    var idx = st.List("indices16").Select(x => Convert.ToInt32(x)).ToList();
                    if (idx.Count == 0 && st["indices32"] is List<object?> i32) idx = i32.Select(x => Convert.ToInt32(x)).ToList();
                    int step = idx.Count % 4 == 0 ? 4 : 3;
                    for (int t = 0; t + 2 < idx.Count; t += step)
                        if (idx[t] < verts.Count && idx[t + 1] < verts.Count && idx[t + 2] < verts.Count) tris.Add((verts[idx[t]], verts[idx[t + 1]], verts[idx[t + 2]]));
                }
            }
        return tris;
    }

    /// <summary>DSR's own map collision (`map\MAP\hMAP.hkxbhd`), every MSB collision part that <paramref name="keep"/>
    /// accepts (default: all, any hit filter); the part name carries the filter: "h0013B1_0000 f8".</summary>
    public static FloorProbe FromDs1(string dsrRoot, string ds1Map, Func<MSB1.Part.Collision, bool>? keep = null)
    {
        var msb = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", ds1Map + ".msb"));
        string d1 = Path.Combine(dsrRoot, "map", ds1Map);
        var hi = BXF3.Read(Path.Combine(d1, $"h{ds1Map[1..]}.hkxbhd"), Path.Combine(d1, $"h{ds1Map[1..]}.hkxbdt"));
        var cache = new Dictionary<string, List<(Vector3 A, Vector3 B, Vector3 C)>>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Tri>();
        foreach (var part in msb.Parts.Collisions)
        {
            if (keep != null && !keep(part)) continue;
            if (!cache.TryGetValue(part.ModelName, out var local))
            {
                var f = hi.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith($"h{part.ModelName[1..]}A", StringComparison.OrdinalIgnoreCase));
                try { local = f == null ? [] : Commands.ColSurvey.Triangles(DCX.Decompress(f.Bytes)); }
                catch (Exception) { local = []; } // a part without a mesh-storage body (some DS1 maps): no triangles
                cache[part.ModelName] = local;
            }
            var m = Place(part.Position, part.Rotation, part.Scale);
            string tag = $"{part.Name} f{part.HitFilterID}{(part.DisableStart ? " disabled" : "")}";
            foreach (var (a, b, c) in local) result.Add(new Tri(Vector3.Transform(a, m), Vector3.Transform(b, m), Vector3.Transform(c, m), tag));
        }
        return new FloorProbe(result);
    }

    /// <summary>DSR's visible map pieces near a point (within <paramref name="radius"/> m in XZ), triangles in world space.</summary>
    public static FloorProbe FromDs1Pieces(string dsrRoot, string ds1Map, Vector3 near, float radius)
    {
        var msb = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", ds1Map + ".msb"));
        string area = ds1Map.Substring(1, 2);
        var result = new List<Tri>();
        foreach (var part in msb.Parts.MapPieces)
        {
            string file = Path.Combine(dsrRoot, "map", ds1Map, $"{part.ModelName}A{area}.flver.dcx");
            if (!H2Merge.Exists(file)) continue; // session 11 (burg11, round 8): rule H2-B
            var flver = FLVER2.Read(DCX.Decompress(H2Merge.ReadAllBytes(file)));
            var m = Place(part.Position, part.Rotation, part.Scale);
            foreach (var mesh in flver.Meshes)
            {
                var fs = mesh.FaceSets.FirstOrDefault(f => f.Flags == FLVER2.FaceSet.FSFlags.None);
                if (fs == null) continue;
                var idx = fs.Triangulate(true);
                for (int i = 0; i + 2 < idx.Count; i += 3)
                {
                    if (idx[i] >= mesh.Vertices.Count || idx[i + 1] >= mesh.Vertices.Count || idx[i + 2] >= mesh.Vertices.Count) continue;
                    var a = Vector3.Transform(mesh.Vertices[idx[i]].Position, m);
                    var b = Vector3.Transform(mesh.Vertices[idx[i + 1]].Position, m);
                    var c = Vector3.Transform(mesh.Vertices[idx[i + 2]].Position, m);
                    var g = (a + b + c) / 3;
                    if (MathF.Abs(g.X - near.X) > radius || MathF.Abs(g.Z - near.Z) > radius) continue;
                    result.Add(new Tri(a, b, c, part.Name));
                }
            }
        }
        return new FloorProbe(result);
    }
}
