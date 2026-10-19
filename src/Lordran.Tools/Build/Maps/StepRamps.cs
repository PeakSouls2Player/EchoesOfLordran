using System.Numerics;
using Lordran.Tools.Formats.Havok;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// DS1 step risers → invisible ramp collision (rule decided by the user, session 8: "use invisible ramps"; DS2's player
/// step height stays vanilla). DS1's player climbs 0.1–0.45 m steps; DS2's doesn't (DS2 builds its stairs as ramps under
/// the visible steps), so the Asylum hall's aisles and stairs blocked the player (user report, session 8).
///
/// Detection, from DS1's walkable collision (world space): a riser is a vertical face 0.08–0.45 m tall with walkable
/// floor at its foot on one side and at its top on the other. Coplanar riser triangles merge into segments. Each
/// segment gets a wedge whose top face runs from its top edge down to the floor <c>run</c> metres in front, where run =
/// the distance to the next lower riser (a staircase: the wedges chain into one plane through the step nosings) or
/// <see cref="LoneRun"/> for a single step, never steeper than <see cref="MaxSlope"/>. The wedges' top faces become
/// extra DS2 map collision parts (CustomParamStorageExtendedMeshShape with our own MOPP, ≤ <see cref="MaxTrisPerPart"/>
/// triangles each, split spatially), placed at the origin.
/// </summary>
public static class StepRamps
{
    const float MinRise = 0.08f, MaxRise = 0.45f, LoneRun = 0.55f, MaxSlope = 0.84f /* 40° */, MaxRun = 0.75f, MaxChainSlope = 1.0f /* 45° (rule SR2) */;
    const int MaxTrisPerPart = 48;

    public sealed record Ramp(Vector3 TopA, Vector3 TopB, Vector3 BotA, Vector3 BotB, float Rise, float Run, bool Chained);

    record Seg(Vector3 N, float D, float Y0, float Y1, float A, float B)
    {
        public Vector3 T => new(-N.Z, 0, N.X);
        public Vector3 At(float s, float y) => T * s + N * D + new Vector3(0, y, 0);
    }

    /// <param name="tris">walkable collision triangles in world space</param>
    public static List<Ramp> Detect(IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> tris, List<string>? log = null)
    {
        // floors: near-horizontal triangles on a 1 m xz grid for point queries
        var floors = new Dictionary<(int, int), List<int>>();
        var risers = new List<int>();
        for (int i = 0; i < tris.Count; i++)
        {
            var (a, b, c) = tris[i];
            var n = Vector3.Cross(b - a, c - a);
            if (n.Length() < 1e-7f) continue;
            n = Vector3.Normalize(n);
            if (MathF.Abs(n.Y) > 0.9f)
            {
                var lo = Vector3.Min(a, Vector3.Min(b, c)); var hi = Vector3.Max(a, Vector3.Max(b, c));
                for (int x = (int)MathF.Floor(lo.X); x <= (int)MathF.Floor(hi.X); x++)
                    for (int z = (int)MathF.Floor(lo.Z); z <= (int)MathF.Floor(hi.Z); z++)
                    {
                        if (!floors.TryGetValue((x, z), out var l)) floors[(x, z)] = l = new();
                        l.Add(i);
                    }
            }
            else if (MathF.Abs(n.Y) < 0.15f)
            {
                float h = MathF.Max(a.Y, MathF.Max(b.Y, c.Y)) - MathF.Min(a.Y, MathF.Min(b.Y, c.Y));
                if (h >= MinRise && h <= MaxRise) risers.Add(i);
            }
        }
        // is there floor at (x, z) within tol of height y?
        bool FloorAt(Vector3 p, float tol = 0.06f)
        {
            if (!floors.TryGetValue(((int)MathF.Floor(p.X), (int)MathF.Floor(p.Z)), out var l)) return false;
            foreach (int i in l)
            {
                var (a, b, c) = tris[i];
                if (HeightOn(a, b, c, p.X, p.Z) is float y && MathF.Abs(y - p.Y) <= tol) return true;
            }
            return false;
        }
        // diagnostics: the floor height closest to p.Y at (x, z), if any
        float? NearestFloor(Vector3 p)
        {
            if (!floors.TryGetValue(((int)MathF.Floor(p.X), (int)MathF.Floor(p.Z)), out var l)) return null;
            float? best = null;
            foreach (int i in l)
                if (HeightOn(tris[i].A, tris[i].B, tris[i].C, p.X, p.Z) is float y && (best == null || MathF.Abs(y - p.Y) < MathF.Abs(best.Value - p.Y))) best = y;
            return best;
        }

        // riser triangles → segments: group by plane (undirected normal, offset) and height band, merge lateral intervals
        var groups = new Dictionary<(int, int, int, int), List<(float A, float B)>>();
        var keyPlane = new Dictionary<(int, int, int, int), (Vector3 N, float D, float Y0, float Y1)>();
        foreach (int i in risers)
        {
            var (a, b, c) = tris[i];
            var n = Vector3.Cross(b - a, c - a); n.Y = 0; n = Vector3.Normalize(n);
            float ang = MathF.Atan2(n.Z, n.X);
            if (ang < 0) { ang += MathF.PI; n = -n; }            // undirected: angle in [0, π)
            if (ang >= MathF.PI - 0.01f) { ang = 0; n = -n; }
            float d = Vector3.Dot(n, (a + b + c) / 3);
            float y0 = MathF.Min(a.Y, MathF.Min(b.Y, c.Y)), y1 = MathF.Max(a.Y, MathF.Max(b.Y, c.Y));
            var key = ((int)MathF.Round(ang / 0.035f), (int)MathF.Round(d / 0.03f), (int)MathF.Round(y0 / 0.03f), (int)MathF.Round(y1 / 0.03f));
            var t = new Vector3(-n.Z, 0, n.X);
            float s0 = MathF.Min(Vector3.Dot(t, a), MathF.Min(Vector3.Dot(t, b), Vector3.Dot(t, c)));
            float s1 = MathF.Max(Vector3.Dot(t, a), MathF.Max(Vector3.Dot(t, b), Vector3.Dot(t, c)));
            if (!groups.TryGetValue(key, out var list)) { groups[key] = list = new(); keyPlane[key] = (n, d, y0, y1); }
            list.Add((s0, s1));
        }
        var segs = new List<Seg>();
        foreach (var (key, list) in groups)
        {
            var (n, d, y0, y1) = keyPlane[key];
            list.Sort((p, q) => p.A.CompareTo(q.A));
            float a0 = list[0].A, b0 = list[0].B;
            void Flush() { if (b0 - a0 >= 0.6f) segs.Add(new Seg(n, d, y0, y1, a0, b0)); }
            foreach (var (s0, s1) in list.Skip(1))
            {
                if (s0 <= b0 + 0.05f) b0 = MathF.Max(b0, s1);
                else { Flush(); a0 = s0; b0 = s1; }
            }
            Flush();
        }

        // orient each segment (normal → the low side) and keep real steps: floor at the foot in front, at the top behind
        var steps = new List<Seg>();
        foreach (var s in segs)
        {
            float rise = s.Y1 - s.Y0;
            Seg? kept = null;
            foreach (int sign in new[] { 1, -1 })
            {
                var o = sign == 1 ? s : s with { N = -s.N, D = -s.D, A = -s.B, B = -s.A };
                int hits = 0, samples = 0;
                for (float f = 0.2f; f <= 0.8f; f += 0.3f)
                {
                    float lat = o.A + (o.B - o.A) * f;
                    samples++;
                    if (FloorAt(o.At(lat, o.Y0) + o.N * 0.12f) && FloorAt(o.At(lat, o.Y1) - o.N * 0.12f)) hits++;
                }
                if (hits * 2 >= samples) { kept = o; break; }
            }
            if (kept != null) steps.Add(kept);
            else
            {
                float mid = (s.A + s.B) / 2;
                string F(Vector3 p) => FloorAt(p) ? "y" : NearestFloor(p) is float y ? $"{y - p.Y:+0.00;-0.00}" : "-";
                log?.Add($"riser at {s.At(mid, s.Y1)} ({rise:0.00} m, width {s.B - s.A:0.00}) not a step: +N foot {F(s.At(mid, s.Y0) + s.N * 0.12f)} top {F(s.At(mid, s.Y1) - s.N * 0.12f)}; -N foot {F(s.At(mid, s.Y0) - s.N * 0.12f)} top {F(s.At(mid, s.Y1) + s.N * 0.12f)}");
            }
        }

        // duplicates: DS1 often has the same step in two overlapping collision parts (Firelink's broken stairs): one segment
        // per (direction, plane, heights), lateral ranges merged
        var uniq = new List<Seg>();
        foreach (var s in steps)
        {
            int k = uniq.FindIndex(u => Vector3.Dot(u.N, s.N) > 0.99f && MathF.Abs(Ahead(u, s)) < 0.03f && MathF.Abs(u.Y0 - s.Y0) < 0.03f
                && MathF.Abs(u.Y1 - s.Y1) < 0.03f && Overlap(u, s) > -0.05f);
            if (k < 0) uniq.Add(s);
            else uniq[k] = uniq[k] with { A = MathF.Min(uniq[k].A, s.A), B = MathF.Max(uniq[k].B, s.B) };
        }
        steps = uniq;

        // run: the nearest parallel riser in front (lower down the stairs) that overlaps laterally: a staircase's wedges then
        // chain into one surface through the nosings, and no wedge overhangs the step below (session 8: Firelink's broken
        // stairs, treads 0.16–0.35 m, got 0.55 m wedges whose overhanging edges were lips DS2's player can't climb = an
        // invisible wall, user report). Lone steps: LoneRun, capped at MaxSlope.
        var ramps = new List<Ramp>();
        var runs = new float[steps.Count]; var chainedOf = new bool[steps.Count]; var nextOf = new int[steps.Count];
        for (int si = 0; si < steps.Count; si++)
        {
            var s = steps[si];
            float rise = s.Y1 - s.Y0, run = LoneRun;
            bool chained = false;
            int next = -1;
            for (int oi = 0; oi < steps.Count; oi++)
            {
                var o = steps[oi];
                // o: parallel, and its top at s's foot (the next step down). Session 11 (walk11), rule SR2b: "any lower riser" picked
                // risers of a flight one turn below / a lower floor (spiral towers, switchbacks: a riser 2-3 m under s, 0.10 m
                // "ahead") as the run limit: 0.10 m wedges = walls (the Burg tower stairs, 272 wedges steeper than 45°)
                if (oi == si || Vector3.Dot(o.N, s.N) < 0.95f || MathF.Abs(o.Y1 - s.Y0) > 0.08f) continue;
                float dist = Ahead(s, o);                        // > 0: o lies in front of s (its low side)
                if (dist < 0.05f || dist > MaxRun) continue;
                float overlap = Overlap(s, o);
                if (overlap < 0.2f && overlap < 0.5f * MathF.Min(o.B - o.A, s.B - s.A)) continue;
                if (dist < run) { run = dist; chained = MathF.Abs(o.Y1 - s.Y0) <= 0.08f; next = oi; }
            }
            // a chained wedge ends at the next nosing (the stairs' own slope, steep pairs averaged by the next wedges); a lone
            // step is never steeper than MaxSlope unless a lower riser in front limits it
            if (!chained && run >= LoneRun) run = MathF.Max(run, rise / MaxSlope);
            runs[si] = run; chainedOf[si] = chained; nextOf[si] = run >= LoneRun ? -1 : next;
        }
        // session 11 (walk11), rule SR2: a wedge steeper than MaxChainSlope (45°) is no ramp but a wall (DS2's player can't
        // walk up it: walk-check "invisible-wall" on the Burg's stairs, 280 of 3027 wedges with 0.05-0.13 m runs between a
        // riser and a second riser just in front of it: a nosing lip or a double riser). Such a wedge continues over the
        // next riser down to that riser's own wedge end (one wedge across both steps), up to 3 risers, the least steep
        // result kept; the lower wedge stays underneath (it is below the merged face everywhere). Spiral stairs (risers
        // converging on the axis) can stay steep: counted in the log.
        int merged = 0, stillSteep = 0;
        IEnumerable<string> Chain(int i)
        {
            for (int k = 0; k < 4 && i >= 0; k++, i = nextOf[i]) yield return $"[{steps[i].Y1 - steps[i].Y0:0.00}/{runs[i]:0.00}]";
        }
        for (int si = 0; si < steps.Count; si++)
        {
            var s = steps[si];
            float run = runs[si], bottom = s.Y0, rise = s.Y1 - s.Y0;
            bool chained = chainedOf[si];
            if (rise / run > MaxChainSlope && nextOf[si] >= 0)
            {
                float bestSlope = rise / run, bestRun = run, bestBottom = bottom;
                int cur = nextOf[si]; float dist = run;
                for (int k = 0; k < 3 && cur >= 0; k++)
                {
                    float r = dist + runs[cur], b = steps[cur].Y0, slope = (s.Y1 - b) / r;
                    if (slope < bestSlope) { bestSlope = slope; bestRun = r; bestBottom = b; }
                    if (slope <= MaxChainSlope || nextOf[cur] < 0) break;
                    dist += runs[cur]; cur = nextOf[cur];
                }
                if (bestRun != run) { merged++; run = bestRun; bottom = bestBottom; chained = true; }
                if (bestSlope > MaxChainSlope) { stillSteep++; log?.Add($"rule SR2: still steep: riser top {s.At((s.A + s.B) / 2, s.Y1)} rise {rise:0.00} run {runs[si]:0.00} width {s.B - s.A:0.00}, chain {string.Join(" → ", Chain(si))}"); }
            }
            const float lift = 0.005f;
            var topA = s.At(s.A, s.Y1 + lift); var topB = s.At(s.B, s.Y1 + lift);
            var botA = s.At(s.A, bottom) + s.N * run; var botB = s.At(s.B, bottom) + s.N * run;
            ramps.Add(new Ramp(topA, topB, botA, botB, s.Y1 - bottom, run, chained));
        }
        log?.Add($"rule SR2: {merged} wedges steeper than 45° continued over the next riser(s), {stillSteep} still steeper than 45°");
        // o measured in s's own frame (session 8: comparing the planes' D offsets from the world origin was wrong twice:
        // its sign made "in front" mean "behind", so no staircase ever chained, and a 1° normal difference between two
        // copies of a step moves D by tens of cm at Firelink's coordinates, so duplicates never merged)
        static float Ahead(Seg s, Seg o) => Vector3.Dot(s.N, (o.At(o.A, 0) + o.At(o.B, 0)) / 2 - s.At((s.A + s.B) / 2, 0));
        static float Overlap(Seg s, Seg o)
        {
            float a = Vector3.Dot(s.T, o.At(o.A, 0)), b = Vector3.Dot(s.T, o.At(o.B, 0));
            return MathF.Min(s.B, MathF.Max(a, b)) - MathF.Max(s.A, MathF.Min(a, b));
        }
        log?.Add($"step ramps: {risers.Count} riser triangles → {segs.Count} segments → {steps.Count} steps ({ramps.Count(r => r.Chained)} in staircases)");
        return ramps;
    }

    static float? HeightOn(Vector3 a, Vector3 b, Vector3 c, float x, float z)
    {
        float d = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
        if (MathF.Abs(d) < 1e-9f) return null;
        float l1 = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / d;
        float l2 = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / d;
        float l3 = 1 - l1 - l2;
        const float e = -0.02f;
        if (l1 < e || l2 < e || l3 < e) return null;
        return l1 * a.Y + l2 * b.Y + l3 * c.Y;
    }

    /// <summary>Ramp top faces as triangles (normals up), grouped into spatially compact parts.</summary>
    public static List<List<(Vector3 A, Vector3 B, Vector3 C)>> Parts(IReadOnlyList<Ramp> ramps)
    {
        var all = new List<(Vector3, Vector3, Vector3)>();
        foreach (var r in ramps)
        {
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                if (Vector3.Cross(b - a, c - a).Y < 0) (b, c) = (c, b);
                all.Add((a, b, c));
            }
            Tri(r.TopA, r.TopB, r.BotB);
            Tri(r.TopA, r.BotB, r.BotA);
        }
        var parts = new List<List<(Vector3, Vector3, Vector3)>>();
        void Split(List<(Vector3 A, Vector3 B, Vector3 C)> set)
        {
            if (set.Count <= MaxTrisPerPart) { if (set.Count > 0) parts.Add(set); return; }
            var cs = set.Select(t => (t.A + t.B + t.C) / 3).ToList();
            var ext = cs.Aggregate(new Vector3(float.MinValue), Vector3.Max) - cs.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
            Func<Vector3, float> axis = ext.X >= ext.Z ? v => v.X : v => v.Z;
            var sorted = set.OrderBy(t => axis((t.A + t.B + t.C) / 3)).ToList();
            int half = sorted.Count / 2 & ~1;                  // keep each ramp's two triangles together
            Split(sorted.Take(half).ToList()); Split(sorted.Skip(half).ToList());
        }
        Split(all);
        return parts;
    }

    /// <summary>
    /// A DS2 map collision packfile holding these triangles (one subpart, stone), built from a converted collision file
    /// of the same map as template (CollisionConverter output: same classes, materials, flags).
    /// </summary>
    public static byte[] Build(IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> tris, HkPackfile template, string name, int material = 19)
    {
        var tRoot = template.ReadRoot();
        var tVariant = (HkPObj)tRoot.List("namedVariants")[0]!;
        var tPhys = (HkPObj)tVariant["variant"]!;
        var tSys = (HkPObj)tPhys.List("systems")[0]!;
        var tRb = (HkPObj)tSys.List("rigidBodies")[0]!;
        var tMopp = (HkPObj)tRb.Get("collidable")["shape"]!;
        var tCode = (HkPObj)tMopp["code"]!;
        var tMesh = (HkPObj)tMopp.Get("child")["childShape"]!;

        var verts = new List<Vector3>();
        var idx = new List<object?>();
        foreach (var (a, b, c) in tris)
        {
            foreach (var v in new[] { a, b, c })
            {
                int k = verts.FindIndex(x => Vector3.DistanceSquared(x, v) < 1e-8f);
                if (k < 0) { k = verts.Count; verts.Add(v); }
                idx.Add((ushort)k);
            }
            idx.Add((ushort)0);
        }
        if (verts.Count > ushort.MaxValue) throw new InvalidOperationException("too many ramp vertices for 16-bit indices");
        var min = verts.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
        var max = verts.Aggregate(new Vector3(float.MinValue), Vector3.Max);

        var sp = ((HkPObj)tMesh.List("trianglesSubparts")[0]!).Clone();
        sp["numTriangleShapes"] = (long)tris.Count;
        sp["numVertices"] = (long)verts.Count;
        sp["vertexStriding"] = 16L; sp["indexStriding"] = 8L; sp["stridingType"] = 2L; sp["flipAlternateTriangles"] = 0L;
        sp["triangleOffset"] = 0L;
        sp["extrusion"] = new float[4];
        var st = ((HkPObj)tMesh.List("meshstorage")[0]!).Clone();
        st["vertices"] = verts.Select(v => (object?)new[] { v.X, v.Y, v.Z, 0f }).ToList();
        st["indices8"] = new List<object?>();
        st["indices16"] = idx;
        st["indices32"] = new List<object?>();
        foreach (string f in new[] { "materialIndices", "materials", "namedMaterials", "materialIndices16" }) st[f] = new List<object?>();
        var mp = ((HkPObj)tMesh.List("materialArray")[0]!).Clone();
        mp["vertexDataBuffer"] = new List<object?>();
        mp["vertexDataStride"] = 0L;
        mp["primitiveDataBuffer"] = new List<object?> { (byte)0, (byte)material, (byte)0, (byte)11 };
        mp["materialNameData"] = 0L;

        var mesh = tMesh.Clone();
        mesh["embeddedTrianglesSubpart"] = sp.Clone();
        mesh["trianglesSubparts"] = new List<object?> { sp };
        mesh["meshstorage"] = new List<object?> { st };
        mesh["materialArray"] = new List<object?> { mp };
        mesh["shapesSubparts"] = new List<object?>();
        mesh["shapestorage"] = new List<object?>();
        mesh["weldingInfo"] = new List<object?>();
        var half = (max - min) / 2; var center = (max + min) / 2;
        const float radius = 0.05f;
        mesh["aabbHalfExtents"] = new[] { half.X + radius, half.Y + radius, half.Z + radius, radius };
        mesh["aabbCenter"] = new[] { center.X, center.Y, center.Z, 0f };
        mesh["cachedNumChildShapes"] = (long)tris.Count;
        mesh["triangleRadius"] = radius;
        mesh["numBitsForSubpartIndex"] = 12L;

        var code = tCode.Clone();
        var (offset, data) = Mopp(tris, min - new Vector3(radius), max + new Vector3(radius));
        code.Get("info")["offset"] = offset;
        code["data"] = data;
        code["buildType"] = 0L;

        var mopp = tMopp.Clone();
        mopp["code"] = code;
        mopp.Get("child")["childShape"] = mesh;
        var rb = tRb.Clone();
        rb["name"] = name;
        rb.Get("collidable")["shape"] = mopp;
        rb.Get("motion").Get("motionState")["objectRadius"] = verts.Max(v => v.Length());
        var sys = tSys.Clone(); sys["rigidBodies"] = new List<object?> { rb };
        var phys = tPhys.Clone(); phys["systems"] = new List<object?> { sys };
        var variant = tVariant.Clone(); variant["variant"] = phys;
        var root = tRoot.Clone(); root["namedVariants"] = new List<object?> { variant };
        return new HkPackWriter(template).Write(root);
    }

    /// <summary>
    /// Our own MOPP (Havok's BV-tree bytecode) for a few dozen triangles: a binary tree of split-plane nodes
    /// <c>[0x10+axis, leftMax, rightMin, leftCodeLength]</c> (axis 0 x / 1 y / 2 z; the left child holds primitives whose
    /// quantised coordinate is ≤ leftMax, the right ≥ rightMin, both may run) ending in terminals 0x30+i (i &lt; 32),
    /// <c>0x50 i</c> (8-bit) or <c>0x51 hi lo</c>. Coordinates are quantised to 24 bits over the AABB (info.offset = min,
    /// w = 0xFE0000 / largest extent) and compared on their top byte, rounded outward, so the tree is conservative.
    /// Format read from DS2's own files (o00_0901's ladder list shape: <c>10 2A 20 09 …</c>; TB h99_3100's wall).
    /// </summary>
    public static (float[] Offset, byte[] Code) Mopp(IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> tris, Vector3 min, Vector3 max)
    {
        var ext = max - min;
        float w = 0xFE0000 / MathF.Max(ext.X, MathF.Max(ext.Y, ext.Z));
        float Q(float v, int axis) => (v - (axis == 0 ? min.X : axis == 1 ? min.Y : min.Z)) * w;
        float C(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
        int Lo(int i, int axis) { var (a, b, c) = tris[i]; return Math.Clamp((int)MathF.Floor(Q(MathF.Min(C(a, axis), MathF.Min(C(b, axis), C(c, axis))), axis) / 65536f), 0, 255); }
        int Hi(int i, int axis) { var (a, b, c) = tris[i]; return Math.Clamp((int)MathF.Ceiling(Q(MathF.Max(C(a, axis), MathF.Max(C(b, axis), C(c, axis))), axis) / 65536f), 0, 255); }

        List<byte> Node(List<int> set)
        {
            if (set.Count == 1)
            {
                int id = set[0];
                return id < 32 ? [(byte)(0x30 + id)] : id < 256 ? [0x50, (byte)id] : [0x51, (byte)(id >> 8), (byte)id];
            }
            // split on the axis with the largest centroid spread, at the median
            int best = 0; float spread = -1;
            for (int ax = 0; ax < 3; ax++)
            {
                var c = set.Select(i => (Lo(i, ax) + Hi(i, ax)) / 2f).ToList();
                if (c.Max() - c.Min() > spread) { spread = c.Max() - c.Min(); best = ax; }
            }
            var sorted = set.OrderBy(i => Lo(i, best) + Hi(i, best)).ToList();
            var left = sorted.Take(sorted.Count / 2).ToList(); var right = sorted.Skip(sorted.Count / 2).ToList();
            var lc = Node(left); var rc = Node(right);
            if (lc.Count > 255) throw new InvalidOperationException("MOPP subtree too large for an 8-bit jump");
            var code = new List<byte> { (byte)(0x10 + best), (byte)left.Max(i => Hi(i, best)), (byte)right.Min(i => Lo(i, best)), (byte)lc.Count };
            code.AddRange(lc); code.AddRange(rc);
            return code;
        }
        var all = Node(Enumerable.Range(0, tris.Count).ToList());
        while (all.Count % 16 != 0) all.Add(0xCD);
        return (new[] { min.X, min.Y, min.Z, w }, all.ToArray());
    }
}
