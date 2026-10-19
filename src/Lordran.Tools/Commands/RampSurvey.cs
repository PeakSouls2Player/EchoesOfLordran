using System.Numerics;
using Lordran.Tools.Build.Maps;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// ramp-survey mAA_BB_CC_DD [--near X Y Z R] — runs the step-ramp rule (<see cref="StepRamps"/>) on a DS1 map's walkable
/// collision (HitFilterID 8) and prints the detected steps/ramps, the collision parts they'd go into, and a MOPP
/// self-test (every triangle must be returned by a query at its own centroid; our interpreter of the split/terminal
/// opcodes StepRamps writes).
/// </summary>
public static class RampSurvey
{
    public static int Run(string[] args)
    {
        string map = args[1], root = Program.DefaultDsrRoot;
        int ni = Array.IndexOf(args, "--near");
        Vector3? near = ni > 0 ? new Vector3(float.Parse(args[ni + 1]), float.Parse(args[ni + 2]), float.Parse(args[ni + 3])) : null;
        float nr = ni > 0 ? float.Parse(args[ni + 4]) : 0;
        var msb = MSB1.Read(Path.Combine(root, "map", "MapStudio", map + ".msb"));
        string d1 = Path.Combine(root, "map", map);
        var hi = BXF3.Read(Path.Combine(d1, $"h{map[1..]}.hkxbhd"), Path.Combine(d1, $"h{map[1..]}.hkxbdt"));
        var world = new List<(Vector3, Vector3, Vector3)>();
        foreach (var part in msb.Parts.Collisions.Where(p => p.HitFilterID == 8 && !p.DisableStart))
        {
            var f = hi.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith($"h{part.ModelName[1..]}A", StringComparison.OrdinalIgnoreCase));
            if (f == null) continue;
            var m = ColSurvey.Place(part.Position, part.Rotation, part.Scale);
            foreach (var (a, b, c) in ColSurvey.Triangles(DCX.Decompress(f.Bytes)))
                world.Add((Vector3.Transform(a, m), Vector3.Transform(b, m), Vector3.Transform(c, m)));
        }
        // --tris X Y Z R: the collision triangles within R of a point (centroid, normal y, height span), to see why a riser
        // was rejected (session 8: Firelink's broken stairs)
        int ti = Array.IndexOf(args, "--tris");
        if (ti > 0)
        {
            var at = new Vector3(float.Parse(args[ti + 1]), float.Parse(args[ti + 2]), float.Parse(args[ti + 3]));
            float r = float.Parse(args[ti + 4]);
            foreach (var (a, b, c) in world)
            {
                var g = (a + b + c) / 3;
                if (Vector3.Distance(g, at) > r) continue;
                var n = Vector3.Cross(b - a, c - a);
                if (n.Length() < 1e-7f) continue;
                n = Vector3.Normalize(n);
                float ylo = MathF.Min(a.Y, MathF.Min(b.Y, c.Y)), yhi = MathF.Max(a.Y, MathF.Max(b.Y, c.Y));
                Console.WriteLine($"tri ({g.X:0.00},{g.Y:0.00},{g.Z:0.00}) n=({n.X:0.00},{n.Y:0.00},{n.Z:0.00}) y {ylo:0.00}..{yhi:0.00} area {Vector3.Cross(b - a, c - a).Length() / 2:0.000}");
            }
            return 0;
        }
        var log = new List<string>();
        var ramps = StepRamps.Detect(world, log);
        foreach (var l in log.Where(l => near == null || !l.StartsWith("riser"))) Console.WriteLine(l);
        foreach (var r in ramps.Where(r => near == null || Vector3.Distance((r.TopA + r.TopB) / 2, near.Value) <= nr))
            Console.WriteLine($"ramp rise {r.Rise:0.00} run {r.Run:0.00} {(r.Chained ? "stairs" : "lone  ")} top ({r.TopA.X:0.00},{r.TopA.Y:0.00},{r.TopA.Z:0.00})..({r.TopB.X:0.00},{r.TopB.Y:0.00},{r.TopB.Z:0.00}) foot ({r.BotA.X:0.00},{r.BotA.Z:0.00})");
        var parts = StepRamps.Parts(ramps);
        int bad = 0;
        foreach (var tris in parts)
        {
            var min = tris.SelectMany(t => new[] { t.A, t.B, t.C }).Aggregate(new Vector3(float.MaxValue), Vector3.Min) - new Vector3(0.05f);
            var max = tris.SelectMany(t => new[] { t.A, t.B, t.C }).Aggregate(new Vector3(float.MinValue), Vector3.Max) + new Vector3(0.05f);
            var (off, code) = StepRamps.Mopp(tris, min, max);
            for (int i = 0; i < tris.Count; i++)
            {
                var c = (tris[i].A + tris[i].B + tris[i].C) / 3;
                var got = Query(code, off, c);
                if (!got.Contains(i)) bad++;
            }
        }
        Console.WriteLine($"{ramps.Count} ramps, {parts.Count} parts ({string.Join(",", parts.Select(p => p.Count))} triangles), MOPP self-test: {(bad == 0 ? "OK" : bad + " triangles missed")}");
        return bad == 0 ? 0 : 1;
    }

    /// <summary>Point query through our MOPP subset: 0x10-0x12 split nodes, 0x30+ / 0x50 / 0x51 terminals.</summary>
    static HashSet<int> Query(byte[] code, float[] off, Vector3 p)
    {
        int[] q = [(int)((p.X - off[0]) * off[3]) >> 16, (int)((p.Y - off[1]) * off[3]) >> 16, (int)((p.Z - off[2]) * off[3]) >> 16];
        var hits = new HashSet<int>();
        void Run(int pc)
        {
            byte op = code[pc];
            if (op is >= 0x10 and <= 0x12)
            {
                int ax = op - 0x10;
                if (q[ax] <= code[pc + 1]) Run(pc + 4);
                if (q[ax] >= code[pc + 2]) Run(pc + 4 + code[pc + 3]);
            }
            else if (op is >= 0x30 and < 0x50) hits.Add(op - 0x30);
            else if (op == 0x50) hits.Add(code[pc + 1]);
            else if (op == 0x51) hits.Add(code[pc + 1] << 8 | code[pc + 2]);
            else throw new InvalidDataException($"opcode {op:X2}");
        }
        Run(0);
        return hits;
    }
}
