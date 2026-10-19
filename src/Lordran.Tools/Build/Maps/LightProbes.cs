using System.Numerics;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// DS2 `pointcloud.btpb` (GI light probes, in the map's gibhd) for a converted map, session 7. Objects (and anything not
/// lightmapped) are lit from these probes; our maps had Things Betwixt's, whose positions are map-local to TB (MSB
/// position minus TB's MapOffset (-498, 30, -260)) and so nowhere near our geometry: every object rendered black.
///
/// Format (decoded by a session-7 subagent; its Python parser rewrites 5 vanilla files byte-identically):
/// header 15 × u32 (2, 1, nodeCount, blobSize, 0, 64 node size, 28 record size, 8 × 0); data blob at 0x3C; node table
/// after it (nodeCount × 64 bytes: u32 blockOff, u32 type, u32 count, u32 dims[3], f32 cell[3], f32 bbox min[3] max[3],
/// u32 recordsOff; offsets from 0x3C). Type 3 = grid leaf (cell (3, 1.5, 3), count = dims product), type 2 = coarse root
/// grid (cell (150, 100, 150), always last), type 1 = navmesh leaf (not written here). Record 28 bytes: 12 × s16 = four
/// RGB triplets of first-order spherical harmonics (DC, then directional; the third ≈ light from above), u16, u16 0.
/// Here: a regular grid over the map's box, every probe one neutral sky-lit payload (TB node 1357 record 23). DS1's own
/// lighting isn't baked into probes yet (later: sample DS1's lightmaps).
/// </summary>
public static class LightProbes
{
    static readonly short[] Payload = [316, 338, 336, 12, 13, 13, 364, 389, 387, 7, 7, 7];
    static readonly Vector3 Cell = new(3f, 1.5f, 3f), RootCell = new(150f, 100f, 150f);
    /// <summary>Payload scale: TB's probe as is left characters darker than before (run s7-7); 1.6 brighter.</summary>
    public static float Brightness = 1.6f;

    /// <summary>The neutral record (TB node 1357 record 23 × <see cref="Brightness"/>): DC, then three directional triplets.</summary>
    public static short[] Neutral => Payload.Select(v => (short)Math.Clamp((int)MathF.Round(v * Brightness), short.MinValue, short.MaxValue)).ToArray();

    /// <summary>Session 11 (burg11), rule LP1: <paramref name="payloadAt"/> gives each probe its own record (12 × s16) from its
    /// position (grid leaves: cell centres, x fastest, then y, then z as the dims; the root grid: its cell centres); null =
    /// the neutral record everywhere (the session-7 behaviour).</summary>
    public static byte[] Make(Vector3 lo, Vector3 hi, int chunk = 4, Func<Vector3, short[]>? payloadAt = null)
    {
        var nodes = new List<(int Type, int[] Dims, Vector3 Cell, Vector3 Min, Vector3 Max)>();
        int[] n = [Ceil((hi.X - lo.X) / Cell.X), Ceil((hi.Y - lo.Y) / Cell.Y), Ceil((hi.Z - lo.Z) / Cell.Z)];
        for (int cz = 0; cz < n[2]; cz += chunk)
            for (int cy = 0; cy < n[1]; cy += chunk)
                for (int cx = 0; cx < n[0]; cx += chunk)
                {
                    int[] d = [Math.Min(chunk, n[0] - cx), Math.Min(chunk, n[1] - cy), Math.Min(chunk, n[2] - cz)];
                    var mn = lo + new Vector3(cx * Cell.X, cy * Cell.Y, cz * Cell.Z);
                    nodes.Add((3, d, Cell, mn, mn + new Vector3(d[0] * Cell.X, d[1] * Cell.Y, d[2] * Cell.Z)));
                }
        int[] rd = [Math.Max(1, (int)MathF.Round((hi.X - lo.X) / RootCell.X)), Math.Max(1, (int)MathF.Round((hi.Y - lo.Y) / RootCell.Y)), Math.Max(1, (int)MathF.Round((hi.Z - lo.Z) / RootCell.Z))];
        nodes.Add((2, rd, RootCell, lo, hi));

        var blob = new MemoryStream();
        var bw = new BinaryWriter(blob);
        var table = new MemoryStream();
        var tw = new BinaryWriter(table);
        foreach (var (type, dims, cell, min, max) in nodes)
        {
            int count = dims[0] * dims[1] * dims[2];
            int off = (int)blob.Length;
            for (int i = 0; i < count; i++)
            {
                if (payloadAt != null)
                {
                    var at = min + new Vector3((i % dims[0] + 0.5f) * cell.X, (i / dims[0] % dims[1] + 0.5f) * cell.Y, (i / (dims[0] * dims[1]) + 0.5f) * cell.Z);
                    foreach (var v in payloadAt(at)) bw.Write(v);
                }
                else
                foreach (var v in Payload) bw.Write((short)Math.Clamp((int)MathF.Round(v * Brightness), short.MinValue, short.MaxValue));
                bw.Write((ushort)0); bw.Write((ushort)0);
            }
            tw.Write(0); tw.Write(type); tw.Write(count);
            foreach (var x in dims) tw.Write(x);
            tw.Write(cell.X); tw.Write(cell.Y); tw.Write(cell.Z);
            tw.Write(min.X); tw.Write(min.Y); tw.Write(min.Z); tw.Write(max.X); tw.Write(max.Y); tw.Write(max.Z);
            tw.Write(off);
        }
        var o = new MemoryStream();
        var w = new BinaryWriter(o);
        foreach (int v in new[] { 2, 1, nodes.Count, (int)blob.Length, 0, 64, 28, 0, 0, 0, 0, 0, 0, 0, 0 }) w.Write(v);
        w.Write(blob.ToArray());
        w.Write(table.ToArray());
        return o.ToArray();
    }

    static int Ceil(float x) => Math.Max(1, (int)MathF.Ceiling(x));

    // ------------------------------------------------------------------ rule LP2 (session 11, atmos11)

    /// <summary>Rule LP2: DS2 clamps its root probe grid to ±800 m on every axis (vanilla: 11 of 28 maps' root boxes end
    /// exactly at ±800; none beyond).</summary>
    public const float RootLimit = 800f;
    /// <summary>Rule LP2: the root grid reaches this far past the leaves (3 root cells across, 2 up/down): vanilla's root
    /// boxes reach 140–600 m past their leaves (FoFG −800..748 around leaves −327..−9).</summary>
    public static readonly Vector3 RootMargin = new(450f, 200f, 450f);
    /// <summary>Leaf chunk = <see cref="LeafChunk"/>³ cells of <see cref="Cell"/> (12 × 6 × 12 m), as <see cref="Make"/>.</summary>
    public const int LeafChunk = 4;

    /// <summary>
    /// <b>Rule LP2</b> (session 11, atmos11): the probe cloud is laid out like vanilla's — sparse leaf grids (type 3, cell
    /// 3 × 1.5 × 3, chunks of 4³) only where characters and objects can be (around DS1's walkable collision: every chunk a
    /// box [p − (3,1,3), p + (3,6,3)] touches, p = points on the collision triangles at ≤ 3 m spacing), and one coarse root
    /// grid (type 2, cell 150 × 100 × 150) over the leaves grown by <see cref="RootMargin"/>, clamped to ±800 m, for
    /// everything else (the crow over the mountains, demo actors). Evidence (<c>atmos-check probes</c>, all 28 vanilla
    /// gibhd): vanilla leaves cover 1–6 k chunks near the navmesh, the root box is 2–8× the leaf box (FoFG root
    /// (−800,−133,−800)..(748,251,781) around leaves (−327,−51,−149)..(−9,7,52)); files 55 KB–2.1 MB. LP1 (session 7)
    /// filled the whole box of every MSB object/enemy/region position with leaves and made the root the same box: the
    /// Burg's parked objects at y −839 and the Hellkite's flight regions at z −343 made a 370 MB cloud (12.8 M records),
    /// and nothing outside the box (a demo actor in the sky) had any probe. Every probe keeps LP1's neutral payload.
    /// </summary>
    public static byte[] MakeSparse(IReadOnlyList<Vector3> samples, out string note, Func<Vector3, short[]>? payloadAt = null)
    {
        if (samples.Count == 0) throw new ArgumentException("rule LP2: no walkable collision samples", nameof(samples));
        var lo = samples.Aggregate(new Vector3(float.MaxValue), Vector3.Min) - new Vector3(3, 1, 3);
        var chunk = Cell * LeafChunk;
        var occupied = new HashSet<(int, int, int)>();
        foreach (var p in samples)
        {
            var a = (p - new Vector3(3, 1, 3) - lo) / chunk;
            var b = (p + new Vector3(3, 6, 3) - lo) / chunk;
            for (int x = (int)MathF.Floor(a.X); x <= (int)MathF.Floor(b.X); x++)
                for (int y = (int)MathF.Floor(a.Y); y <= (int)MathF.Floor(b.Y); y++)
                    for (int z = (int)MathF.Floor(a.Z); z <= (int)MathF.Floor(b.Z); z++)
                        occupied.Add((x, y, z));
        }
        var nodes = new List<(int Type, int[] Dims, Vector3 Cell, Vector3 Min, Vector3 Max)>();
        foreach (var (x, y, z) in occupied.OrderBy(c => c.Item3).ThenBy(c => c.Item2).ThenBy(c => c.Item1))
        {
            var mn = lo + new Vector3(x, y, z) * chunk;
            nodes.Add((3, [LeafChunk, LeafChunk, LeafChunk], Cell, mn, mn + chunk));
        }
        var leafLo = nodes.Select(n => n.Min).Aggregate(Vector3.Min);
        var leafHi = nodes.Select(n => n.Max).Aggregate(Vector3.Max);
        var rlo = Vector3.Clamp(leafLo - RootMargin, new Vector3(-RootLimit), new Vector3(RootLimit));
        var rhi = Vector3.Clamp(leafHi + RootMargin, new Vector3(-RootLimit), new Vector3(RootLimit));
        int[] rd = [Math.Max(1, (int)MathF.Round((rhi.X - rlo.X) / RootCell.X)), Math.Max(1, (int)MathF.Round((rhi.Y - rlo.Y) / RootCell.Y)), Math.Max(1, (int)MathF.Round((rhi.Z - rlo.Z) / RootCell.Z))];
        nodes.Add((2, rd, RootCell, rlo, rhi));
        note = $"rule LP2: {occupied.Count} leaf chunks over {leafLo}..{leafHi}, root grid {rd[0]}×{rd[1]}×{rd[2]} over {rlo}..{rhi}";
        return Write(nodes, payloadAt);
    }

    static byte[] Write(List<(int Type, int[] Dims, Vector3 Cell, Vector3 Min, Vector3 Max)> nodes, Func<Vector3, short[]>? payloadAt = null) // session 11 merge: LP1 payloads on LP2 nodes
    {
        var blob = new MemoryStream();
        var bw = new BinaryWriter(blob);
        var table = new MemoryStream();
        var tw = new BinaryWriter(table);
        foreach (var (type, dims, cell, min, max) in nodes)
        {
            int count = dims[0] * dims[1] * dims[2];
            int off = (int)blob.Length;
            for (int i = 0; i < count; i++)
            {
                if (payloadAt != null)
                {
                    var at = min + new Vector3((i % dims[0] + 0.5f) * cell.X, (i / dims[0] % dims[1] + 0.5f) * cell.Y, (i / (dims[0] * dims[1]) + 0.5f) * cell.Z);
                    foreach (var v in payloadAt(at)) bw.Write(v);
                }
                else
                foreach (var v in Payload) bw.Write((short)Math.Clamp((int)MathF.Round(v * Brightness), short.MinValue, short.MaxValue));
                bw.Write((ushort)0); bw.Write((ushort)0);
            }
            tw.Write(0); tw.Write(type); tw.Write(count);
            foreach (var x in dims) tw.Write(x);
            tw.Write(cell.X); tw.Write(cell.Y); tw.Write(cell.Z);
            tw.Write(min.X); tw.Write(min.Y); tw.Write(min.Z); tw.Write(max.X); tw.Write(max.Y); tw.Write(max.Z);
            tw.Write(off);
        }
        var o = new MemoryStream();
        var w = new BinaryWriter(o);
        foreach (int v in new[] { 2, 1, nodes.Count, (int)blob.Length, 0, 64, 28, 0, 0, 0, 0, 0, 0, 0, 0 }) w.Write(v);
        w.Write(blob.ToArray());
        w.Write(table.ToArray());
        return o.ToArray();
    }

    /// <summary>Rule LP2's samples: points on DS1's walkable collision (hit filter 8, every part incl. ones DS1's events
    /// enable later) at ≤ 3 m spacing, world coordinates.</summary>
    public static List<Vector3> WalkSamples(string dsrRoot, string ds1Map, SoulsFormats.MSB1 msb1, bool floorsOnly = false)
    {
        string dir = Path.Combine(dsrRoot, "map", ds1Map);
        var bxf = SoulsFormats.BXF3.Read(Path.Combine(dir, $"h{ds1Map[1..]}.hkxbhd"), Path.Combine(dir, $"h{ds1Map[1..]}.hkxbdt"));
        var list = new List<Vector3>();
        foreach (var part in msb1.Parts.Collisions.Where(c => c.HitFilterID == 8))
        {
            var f = bxf.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith($"h{part.ModelName[1..]}A", StringComparison.OrdinalIgnoreCase));
            if (f == null) continue;
            var m = Commands.ColSurvey.Place(part.Position, part.Rotation, part.Scale);
            foreach (var (a0, b0, c0) in Commands.ColSurvey.Triangles(SoulsFormats.DCX.Decompress(f.Bytes)))
            {
                var a = Vector3.Transform(a0, m); var b = Vector3.Transform(b0, m); var c = Vector3.Transform(c0, m);
                if (floorsOnly && Vector3.Cross(b - a, c - a) is var cr && (cr.LengthSquared() < 1e-12f || MathF.Abs(cr.Y) / cr.Length() < 0.7f)) continue; // rule S3f: floors (slope < 45°) only
                float longest = MathF.Max(Vector3.Distance(a, b), MathF.Max(Vector3.Distance(b, c), Vector3.Distance(c, a)));
                int n = Math.Clamp((int)MathF.Ceiling(longest / 3f), 1, 64);
                for (int i = 0; i <= n; i++)
                    for (int j = 0; i + j <= n; j++)
                        list.Add(a + (b - a) * (i / (float)n) + (c - a) * (j / (float)n));
            }
        }
        return list;
    }
}
