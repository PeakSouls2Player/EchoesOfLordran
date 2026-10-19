using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (walk11): walk-probe MAPDIR MAP — render/visibility settings of a DS2 map as the game reads them (ACB model
/// settings vs FLVER bounds, MSB part groups), for a pack (PACK\gamedata) or the extracted vanilla data (data\ds2).
/// Research tool behind walk-check's draw/LOD rules.
/// </summary>
public static class WalkProbe
{
    /// <summary>Surface samples of a triangle every ~step m (vertices, interior grid).</summary>
    public static IEnumerable<Vector3> Samples(Vector3 a, Vector3 b, Vector3 c, float step)
    {
        float len = MathF.Max(Vector3.Distance(a, b), MathF.Max(Vector3.Distance(b, c), Vector3.Distance(a, c)));
        int n = Math.Clamp((int)(len / step) + 1, 1, 400);
        for (int u = 0; u <= n; u++)
            for (int v = 0; v <= n - u; v++)
                yield return a + (b - a) * ((float)u / n) + (c - a) * ((float)v / n);
    }

    sealed class Vox
    {
        readonly HashSet<(int, int, int)> set = [];
        readonly float cell;
        public Vox(float cell) { this.cell = cell; }
        (int, int, int) K(Vector3 p) => ((int)MathF.Floor(p.X / cell), (int)MathF.Floor(p.Y / cell), (int)MathF.Floor(p.Z / cell));
        public void Add(Vector3 p) => set.Add(K(p));
        public bool Near(Vector3 p, int r = 1)
        {
            var (x, y, z) = K(p);
            for (int dx = -r; dx <= r; dx++) for (int dy = -r; dy <= r; dy++) for (int dz = -r; dz <= r; dz++)
                if (set.Contains((x + dx, y + dy, z + dz))) return true;
            return false;
        }
    }

    /// <summary>hl DS1MAP | hl-ds2 DS2MAP: per collision model, how DS1's / DS2's low (l) collision relates to the high (h)
    /// one: area of l farther than ~0.3 m from any h surface (l-only = walls/floors only the low set has) and h walkable
    /// area with no l nearby.</summary>
    static int HighLow(string[] args)
    {
        bool ds2 = args[0] == "hl-ds2";
        string map = args[1];
        var pairs = new List<(string Name, List<(Vector3 A, Vector3 B, Vector3 C)> H, List<(Vector3 A, Vector3 B, Vector3 C)> L)>();
        if (!ds2)
        {
            string d1 = Path.Combine(Program.DefaultDsrRoot, "map", map);
            var h = BXF3.Read(Path.Combine(d1, $"h{map[1..]}.hkxbhd"), Path.Combine(d1, $"h{map[1..]}.hkxbdt"));
            var l = BXF3.Read(Path.Combine(d1, $"l{map[1..]}.hkxbhd"), Path.Combine(d1, $"l{map[1..]}.hkxbdt"));
            foreach (var hf in h.Files)
            {
                string n = Path.GetFileName(hf.Name); n = n[1..n.IndexOf('A')];
                var lf = l.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith("l" + n + "A", StringComparison.OrdinalIgnoreCase));
                List<(Vector3, Vector3, Vector3)> T(BinderFile f) { try { return ColSurvey.Triangles(DCX.Decompress(f.Bytes)); } catch { return []; } }
                pairs.Add((n, T(hf), lf == null ? [] : T(lf)));
            }
        }
        else
        {
            string dir = Path.Combine(ExtractDs2.DefaultOut, "model", "map");
            var h = BXF4.Read(Path.Combine(dir, $"h{map[1..]}.hkxbhd"), Path.Combine(dir, $"h{map[1..]}.hkxbdt"));
            var l = BXF4.Read(Path.Combine(dir, $"l{map[1..]}.hkxbhd"), Path.Combine(dir, $"l{map[1..]}.hkxbdt"));
            foreach (var hf in h.Files.Where(f => f.Name.EndsWith(".hkx.dcx")))
            {
                string n = Path.GetFileName(hf.Name); n = n[1..n.IndexOf('.')];
                var lf = l.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith("l" + n + ".", StringComparison.OrdinalIgnoreCase));
                List<(Vector3, Vector3, Vector3)> T(BinderFile f) { try { return Build.Maps.FloorProbe.Ds2Triangles(DCX.Decompress(f.Bytes)); } catch { return []; } }
                pairs.Add((n, T(hf), lf == null ? [] : T(lf)));
            }
        }
        double tH = 0, tL = 0, tLonly = 0, tHnoL = 0, tSpace = 0;
        foreach (var (name, hs, ls) in pairs)
        {
            var vh = new Vox(0.25f); var vl = new Vox(0.25f);
            foreach (var t in hs) foreach (var p in Samples(t.A, t.B, t.C, 0.2f)) vh.Add(p);
            foreach (var t in ls) foreach (var p in Samples(t.A, t.B, t.C, 0.2f)) vl.Add(p);
            double aL = 0, aLonly = 0, aH = 0, aHnoL = 0;
            foreach (var (a, b, c) in ls)
            {
                float ar = Vector3.Cross(b - a, c - a).Length() / 2; aL += ar;
                var s = Samples(a, b, c, 0.5f).ToList(); int off = s.Count(p => !vh.Near(p));
                aLonly += ar * off / Math.Max(1, s.Count);
            }
            foreach (var (a, b, c) in hs)
            {
                var nn = Vector3.Cross(b - a, c - a); float ar = nn.Length() / 2;
                if (ar < 1e-6f || MathF.Abs(Vector3.Normalize(nn).Y) < 0.6f) continue;
                aH += ar;
                var s = Samples(a, b, c, 0.5f).ToList(); int off = s.Count(p => !vl.Near(p));
                aHnoL += ar * off / Math.Max(1, s.Count);
            }
            // l-only area inside the player's space: 0.3-1.7 m above an h floor (a wall/ceiling the player would hit if
            // its layer collided with l)
            var hprobe = new Build.Maps.FloorProbe(hs.Select(t => new Build.Maps.FloorProbe.Tri(t.A, t.B, t.C, name)));
            double inSpace = 0;
            foreach (var (a, b, c) in ls)
            {
                float ar = Vector3.Cross(b - a, c - a).Length() / 2;
                var s = Samples(a, b, c, 0.5f).ToList();
                int k = s.Count(p => !vh.Near(p) && hprobe.Down(p.X, p.Z).Any(f => f.NormalY > 0.7f && p.Y - f.Y > 0.3f && p.Y - f.Y < 1.7f));
                inSpace += ar * k / Math.Max(1, s.Count);
            }
            if (inSpace > 2) Console.WriteLine($"  {name,-10} l-only inside the player's space over an h floor: {inSpace:0} m²");
            tSpace += inSpace;
            tH += aH; tL += aL; tLonly += aLonly; tHnoL += aHnoL;
            if (aLonly > 5 || aHnoL > 5)
                Console.WriteLine($"  {name,-10} h {hs.Count,6} tris  l {ls.Count,6} tris  l-only {aLonly,8:0} m² of {aL,8:0}  h-floor without l {aHnoL,8:0} m² of {aH,8:0}");
        }
        Console.WriteLine($"{map}: l area {tL:0} m², l-only {tLonly:0} m² ({100 * tLonly / Math.Max(1, tL):0.0}%); h floor {tH:0} m², without l {tHnoL:0} m² ({100 * tHnoL / Math.Max(1, tH):0.0}%); l-only in the player's space {tSpace:0} m²");
        return 0;
    }

    public static int Run(string[] args)
    {
        if (args.Length > 3 && args[1] == "pieces")
        {
            // walk-probe pieces PACK MAP MODEL...: world bounds of the parts of these map-piece models
            string gd = Path.Combine(args[2], "gamedata"), map = args[3];
            var bxf = BXF4.Read(Path.Combine(gd, "model", "map", map + ".mapbhd"), Path.Combine(gd, "model", "map", map + ".mapbdt"));
            var msb = MSB2.Read(Path.Combine(gd, "map", map, map + ".msb"));
            foreach (string model in args.Skip(4))
            {
                var f = bxf.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith(model + ".", StringComparison.OrdinalIgnoreCase));
                if (f == null) { Console.WriteLine($"{model}: not in the archive"); continue; }
                var h = FLVER2.Read(DCX.Decompress(f.Bytes)).Header;
                foreach (var p in msb.Parts.MapPieces.Where(p => p.ModelName == model))
                {
                    var m = ColSurvey.Place(p.Position, p.Rotation, p.Scale);
                    var a = Vector3.Transform(h.BoundingBoxMin, m); var b = Vector3.Transform(h.BoundingBoxMax, m);
                    Console.WriteLine($"{p.Name}: world box {Vector3.Min(a, b):0.0} .. {Vector3.Max(a, b):0.0}, centre {(a + b) / 2:0.0}");
                }
            }
            return 0;
        }
        if (args.Length > 1 && args[1] is "hl" or "hl-ds2") return HighLow(args[1..]);
        if (args.Length > 1 && args[1] == "bodies")
        {
            // walk-probe bodies DS1MAP: rigid bodies / systems / shapes per DS1 h and l collision file (the converter and
            // ColSurvey read systems[0].rigidBodies[0] only)
            string map = args[2];
            string d1 = Path.Combine(Program.DefaultDsrRoot, "map", map);
            foreach (char kind in "hl")
            {
                var bxf = BXF3.Read(Path.Combine(d1, $"{kind}{map[1..]}.hkxbhd"), Path.Combine(d1, $"{kind}{map[1..]}.hkxbdt"));
                int files = 0, multi = 0;
                foreach (var f in bxf.Files)
                {
                    files++;
                    try
                    {
                        var tag = new Formats.Havok.HkTagfile(DCX.Decompress(f.Bytes));
                        static Formats.Havok.HkTagfile.HkObj O(object? v) => (Formats.Havok.HkTagfile.HkObj)v!;
                        static List<object?> L(object? v) => (List<object?>)v!;
                        var nv = L(tag.Root!["namedVariants"]);
                        var systems = L(O(O(nv[0])["variant"])["systems"]);
                        var bodies = systems.Select(s => L(O(s)["rigidBodies"])).ToList();
                        var shapes = bodies.SelectMany(b => b).Select(rb => O(O(rb)["collidable"])["shape"] is Formats.Havok.HkTagfile.HkObj sh ? sh.Type.Name + (sh.Type.Name == "hkpMoppBvTreeShape" ? "/" + O(O(sh)["child"])["childShape"] switch { Formats.Havok.HkTagfile.HkObj c => c.Type.Name, _ => "?" } : "") : "null").ToList();
                        if (nv.Count > 1 || systems.Count > 1 || bodies.Any(b => b.Count > 1) || shapes.Any(s => !s.EndsWith("CustomParamStorageExtendedMeshShape")))
                        {
                            multi++;
                            Console.WriteLine($"  {Path.GetFileName(f.Name)}: variants {nv.Count}, systems {systems.Count}, bodies [{string.Join(",", bodies.Select(b => b.Count))}], shapes {string.Join(",", shapes)}");
                        }
                    }
                    catch (Exception e) { Console.WriteLine($"  {Path.GetFileName(f.Name)}: {e.GetType().Name} {e.Message}"); multi++; }
                }
                Console.WriteLine($"{map} {kind}: {files} files, {multi} not a single MOPP mesh body");
            }
            return 0;
        }
        if (args.Length > 1 && args[1] == "at")
        {
            // walk-probe at DS1MAP X Y Z [PACK]: what is under a point in DS1 (every filter) and DS2
            string map = args[2];
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var p = new Vector3(float.Parse(args[3], ci), float.Parse(args[4], ci), float.Parse(args[5], ci));
            var all = Build.Maps.FloorProbe.FromDs1(Program.DefaultDsrRoot, map);
            Console.WriteLine($"DS1 {map} collision (all filters) under {p}:");
            foreach (var h in all.Down(p.X, p.Z).Where(h => MathF.Abs(h.Y - p.Y) < 6)) Console.WriteLine($"  y {h.Y:0.00} n {h.NormalY:0.00} {h.Part}");
            var msb = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", map + ".msb"));
            foreach (var o in msb.Parts.Objects.Where(o => Vector2.Distance(new(o.Position.X, o.Position.Z), new(p.X, p.Z)) < 6))
                Console.WriteLine($"  DS1 object {o.Name} {o.ModelName} at {o.Position} entity {o.EntityID}");
            var under = all.Down(p.X, p.Z);
            foreach (var c in msb.Parts.Collisions.Where(c => under.Any(h => h.Part.StartsWith(c.Name + " "))))
                Console.WriteLine($"  DS1 collision {c.Name} filter {c.HitFilterID} entity {c.EntityID} disableStart {c.DisableStart} draw [{string.Join(",", c.DrawGroups.Select(x => x.ToString("X")))}] disp [{string.Join(",", c.DispGroups.Select(x => x.ToString("X")))}]");
            if (args.Length > 6)
                foreach (var h2 in Directory.GetDirectories(Path.Combine(args[6], "gamedata", "map")).Select(Path.GetFileName).Where(n => n!.StartsWith('m')))
                    try { foreach (var h in Build.Maps.FloorProbe.FromFolder(args[6], h2!).Down(p.X, p.Z).Where(h => MathF.Abs(h.Y - p.Y) < 6)) Console.WriteLine($"  DS2 {h2} y {h.Y:0.00} n {h.NormalY:0.00} {h.Part}"); } catch { }
            return 0;
        }
        string root = args[1];
        string gamedata = Directory.Exists(Path.Combine(root, "gamedata")) ? Path.Combine(root, "gamedata") : root;
        foreach (string map in args.Skip(2))
        {
            string mdir = Path.Combine(gamedata, "model", "map");
            var bxf = BXF4.Read(Path.Combine(mdir, map + ".mapbhd"), Path.Combine(mdir, map + ".mapbdt"));
            var acb = ACB.Read(bxf.Files.First(f => f.Name.EndsWith(".acb", StringComparison.OrdinalIgnoreCase)).Bytes);
            var msb = MSB2.Read(Path.Combine(gamedata, "map", map, map + ".msb"));
            Console.WriteLine($"== {map}: {acb.Assets.Count} ACB assets, {msb.Parts.MapPieces.Count} pieces, {msb.Parts.Collisions.Count} collisions");
            var bounds = new Dictionary<string, (Vector3 Min, Vector3 Max)>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in bxf.Files.Where(f => f.Name.EndsWith(".flv.dcx", StringComparison.OrdinalIgnoreCase)))
            {
                var flv = FLVER2.Read(DCX.Decompress(f.Bytes));
                string n = Path.GetFileName(f.Name); n = n[..n.IndexOf('.')];
                bounds[n] = (flv.Header.BoundingBoxMin, flv.Header.BoundingBoxMax);
            }
            foreach (var g in acb.Assets.OfType<ACB.Asset.Model>().GroupBy(m => $"draw={m.DrawDistance} lod={m.MeshLodRate} lowTex={m.LowTextureDistance} cheap={m.CheapRenderDistance} render={m.RenderType} u20={m.Unk20} u27={m.Unk27} u28={m.Unk28} u2c={m.Unk2C} u2e={m.Unk2E} u34={m.Unk34}"))
                Console.WriteLine($"  ACB {g.Count(),4} × {g.Key}");
            // extents vs draw distances
            var ext = bounds.Select(b => (b.Key, Size: (b.Value.Max - b.Value.Min).Length())).OrderByDescending(x => x.Size).ToList();
            Console.WriteLine($"  model diagonal: max {ext.FirstOrDefault().Size:0} m ({ext.FirstOrDefault().Key}), median {ext.ElementAtOrDefault(ext.Count / 2).Size:0} m, > 100 m: {ext.Count(x => x.Size > 100)}");
            foreach (var g in msb.Parts.MapPieces.GroupBy(p => $"draw[{string.Join(",", p.DrawGroups.Select(x => x.ToString("X8")))}] disp[{string.Join(",", p.DispGroups.Select(x => x.ToString("X8")))}] u44 {p.Unk44} t00 {p.UnkT00} t02 {p.UnkT02}").OrderByDescending(g => g.Count()).Take(8))
                Console.WriteLine($"  piece {g.Count(),4} × {g.Key}");
            foreach (var g in msb.Parts.Collisions.GroupBy(p => $"draw[{string.Join(",", p.DrawGroups.Select(x => x.ToString("X8")))}] disp[{string.Join(",", p.DispGroups.Select(x => x.ToString("X8")))}] area[{p.Unk44:X8},{p.Unk48:X8},{p.Unk4C:X8},{p.Unk50:X8}]").OrderByDescending(g => g.Count()).Take(8))
                Console.WriteLine($"  coll  {g.Count(),4} × {g.Key}");
            foreach (var g in msb.Parts.Collisions.GroupBy(p => $"t00 {p.UnkT00} t12 {p.UnkT12} t13 {p.UnkT13} t14 {p.UnkT14} t15 {p.UnkT15} t17 {p.UnkT17} t18 {p.UnkT18} t26 {p.UnkT26} t27 {p.UnkT27} t28 {p.UnkT28} t36 {p.UnkT36} t40 {p.UnkT40} t44 {p.UnkT44}").OrderByDescending(g => g.Count()).Take(6))
                Console.WriteLine($"  collT {g.Count(),4} × {g.Key}");
            Console.WriteLine($"  connects {msb.Parts.ConnectCollisions.Count}");
            // big models: their ACB distances
            var acbBy = acb.Assets.OfType<ACB.Asset.Model>().ToDictionary(a => Path.GetFileNameWithoutExtension(a.AbsolutePath).ToLowerInvariant(), a => a);
            foreach (var (n, size) in ext.Where(x => x.Size > 60 && float.IsFinite(x.Size)).Take(12))
                if (acbBy.TryGetValue(n.ToLowerInvariant(), out var a))
                    Console.WriteLine($"  big {n} {size:0} m: draw {a.DrawDistance} lowTex {a.LowTextureDistance} cheap {a.CheapRenderDistance} lod {a.MeshLodRate} render {a.RenderType}");
        }
        return 0;
    }
}
