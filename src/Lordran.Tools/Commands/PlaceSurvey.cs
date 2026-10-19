using System.Numerics;
using Lordran.Tools.Build.Maps;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 9 (track npcplace): evidence for the character placement rules (facing, height).
/// <list type="bullet">
/// <item>`place-survey skel FILE…` — a character model's forward axis in model space (bind pose: toe − foot of both
/// feet, head over pelvis) for DS1 chr/parts files (.chrbnd.dcx/.partsbnd.dcx), DS2 c0001/parts (.bnd) or a pack's chr.</item>
/// <item>`place-survey ds1 MAP ENTITY…` — DS1 characters: MSB position/rotation/InitAnimID, DSR's collision under them
/// (every hit filter) and DSR's visible map pieces under them, wall distances around them in 24 directions (filter 8),
/// and what lies in the direction each yaw convention says they face.</item>
/// <item>`place-survey ds2 MAP GEN…` — vanilla DS2 generators: GeneratorLocation position/yaw, regist startUpStateType,
/// DS2's own collision under them, wall distances and floor drops in 24 directions.</item>
/// </list>
/// Directions are compass angles a = atan2(x, z) in degrees (0 = +Z, 90 = +X), the convention DS2 builds a location's
/// rotation with: GeneratorLocation/EventLocation degrees → FUN_1401c2e00 (DirectXMath RollPitchYaw quaternion) → the
/// character's rotation matrix, whose third row (sin θ, 0, cos θ) is the model's +Z axis in the world.
/// </summary>
public static class PlaceSurvey
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("place-survey skel FILE… | ds1 MAP ENTITY… | ds2 MAP GEN…"); return 1; }
        return args[1] switch
        {
            "skel" => Skel(args.Skip(2).ToArray()),
            "ds1" => Ds1(args[2], args.Skip(3).Select(int.Parse).ToArray()),
            "ds2" => Ds2(args[2], args.Skip(3).Select(int.Parse).ToArray()),
            "anim" => Anim(args[2], args[3], args.Skip(4).Select(int.Parse).ToArray()),
            "census" => Census(args[2]),
            "seats" => Seats(),
            "regions" => Regions(args.Length > 2 ? args[2] : null),
            "clips" => Clips(),
            _ => throw new ArgumentException(args[1]),
        };
    }

    // ---------------------------------------------------------------- DS1 clip pose: where does the body face?
    /// <summary>`place-survey anim CHR CLIPBND CLIP…` — DS1 clip frame 0 (and the last frame) posed on DS1's Havok skeleton
    /// (`chr\CHR.anibnd.dcx` Skeleton.hkx; clips from `chr\CLIPBND.anibnd.dcx`): toes − feet, head and pelvis in model
    /// space, so an idle that turns or offsets the body (DS1 InitAnimID sits) is visible. Model −Z = compass 180.</summary>
    static int Anim(string chr, string clipBnd, int[] clips)
    {
        string root = Program.DefaultDsrRoot;
        var skel = Build.Chr.Ds1ChrSource.Load(root, chr).Skeleton;
        // clip archives without a skeleton (c0000_a00_md …): the clips by file name, posed on CHR's skeleton
        var clipFiles = BND3.Read(Path.Combine(root, "chr", clipBnd + ".anibnd.dcx")).Files
            .ToDictionary(f => Path.GetFileName(f.Name.Replace('\\', '/')).ToLowerInvariant(), f => DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes);
        var src = new
        {
            Has = (Func<int, bool>)(id => clipFiles.ContainsKey(Build.Chr.Ds1ChrSource.ClipName(id) + ".hkx")),
            Samples = (Func<int, Build.Chr.Ds1AnimBridge.Ds1Samples>)(id => Build.Chr.Ds1AnimBridge.Sample(skel, new Formats.Havok.HkTagfile(clipFiles[Build.Chr.Ds1ChrSource.ClipName(id) + ".hkx"]))),
        };
        int Bone(params string[] names) => skel.Names.FindIndex(n => names.Any(x => string.Equals(n.Replace(' ', '_'), x, StringComparison.OrdinalIgnoreCase)));
        int lt = Bone("L_Toe0"), lf = Bone("L_Foot"), rt = Bone("R_Toe0"), rf = Bone("R_Foot"), head = Bone("Head"), pelvis = Bone("Pelvis"), master = Bone("Master");
        foreach (int id in clips)
        {
            if (!src.Has(id)) { Console.WriteLine($"{chr} clip {id}: not in {clipBnd}"); continue; }
            var s = src.Samples(id);
            foreach (int f in new[] { 0, s.Frames - 1 }.Distinct())
            {
                var world = new Matrix4x4[skel.Names.Count];
                for (int b = 0; b < skel.Names.Count; b++)
                {
                    float[] t = skel.RefPos[b], r = skel.RefRot[b];
                    if (s.Tracks.TryGetValue(b, out var tr)) { t = tr[Math.Min(f, tr.Count - 1)].T; r = tr[Math.Min(f, tr.Count - 1)].R; }
                    var local = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(new Quaternion(r[0], r[1], r[2], r[3]))) * Matrix4x4.CreateTranslation(t[0], t[1], t[2]);
                    int p = skel.Parents[b];
                    world[b] = p >= 0 && p < b ? local * world[p] : local;
                }
                Vector3 P(int b) => b >= 0 ? world[b].Translation : Vector3.Zero;
                var fwd = (lt >= 0 && lf >= 0 ? P(lt) - P(lf) : Vector3.Zero) + (rt >= 0 && rf >= 0 ? P(rt) - P(rf) : Vector3.Zero);
                fwd.Y = 0;
                string rootMotion = s.RefFrame is { Count: > 0 } rfm ? $" root motion sample {string.Join(", ", rfm[Math.Min(f * rfm.Count / Math.Max(1, s.Frames), rfm.Count - 1)].Select(v => v.ToString("0.###")))}" : "";
                float lowest = Enumerable.Range(0, skel.Names.Count).Min(b => world[b].Translation.Y);
                Console.WriteLine($"{chr} {Build.Chr.Ds1ChrSource.ClipName(id)} frame {f}/{s.Frames - 1}: toes−feet compass {(fwd.Length() > 1e-4f ? Compass(fwd).ToString("0.#") : "?")}; head {P(head):0.##} pelvis {P(pelvis):0.##} master {P(master):0.##} feet {P(lf):0.##} {P(rf):0.##} toes {P(lt):0.##} {P(rt):0.##} lowest bone y {lowest:0.##}{rootMotion}");
            }
        }
        return 0;
    }

    public static float Compass(Vector3 d) => MathF.Atan2(d.X, d.Z) * 180 / MathF.PI;
    public static Vector3 Dir(float compassDeg) => new(MathF.Sin(compassDeg * MathF.PI / 180), 0, MathF.Cos(compassDeg * MathF.PI / 180));
    static float Norm(float a) { a %= 360; if (a > 180) a -= 360; if (a <= -180) a += 360; return a; }

    // ---------------------------------------------------------------- skeleton forward axis
    static FLVER2 LoadFlver(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        if (DCX.Is(b)) b = DCX.Decompress(b);
        if (BND3.Is(b)) return FLVER2.Read(BND3.Read(b).Files.First(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase) || f.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase)).Bytes);
        if (BND4.Is(b))
        {
            var f = BND4.Read(b).Files.First(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase) || f.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase) || f.Name.EndsWith(".flv.dcx", StringComparison.OrdinalIgnoreCase));
            return FLVER2.Read(DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes);
        }
        return FLVER2.Read(b);
    }

    /// <summary>Bind-pose world positions of a FLVER's nodes (local transforms composed up the parent chain).</summary>
    public static Vector3[] NodeWorld(FLVER2 f)
    {
        var world = new Matrix4x4[f.Nodes.Count];
        var done = new bool[f.Nodes.Count];
        Matrix4x4 W(int i)
        {
            if (done[i]) return world[i];
            var local = f.Nodes[i].ComputeLocalTransform();
            int p = f.Nodes[i].ParentIndex;
            world[i] = p >= 0 && p < f.Nodes.Count && p != i ? local * W(p) : local;
            done[i] = true;
            return world[i];
        }
        return Enumerable.Range(0, f.Nodes.Count).Select(i => Vector3.Transform(Vector3.Zero, W(i))).ToArray();
    }

    /// <summary>Model-space forward of a humanoid rig: mean of (toe − foot) of both feet, flattened (null if not found).</summary>
    public static Vector3? ModelForward(FLVER2 f, out string how)
    {
        var w = NodeWorld(f);
        int Find(params string[] names) { foreach (var n in names) { int i = f.Nodes.FindIndex(b => string.Equals(b.Name, n, StringComparison.OrdinalIgnoreCase)); if (i >= 0) return i; } return -1; }
        // DS1 enemy rigs name feet "L Foot"/"L Toe0" or "L_Foot"/"L_Toe0" (a toe child of the foot)
        int Like(string side, string part) => f.Nodes.FindIndex(b => b.Name.Replace(" ", "_").StartsWith($"{side}_{part}", StringComparison.OrdinalIgnoreCase));
        var pairs = new[] { (Find("L_Toe0", "L_Toe") is int lt and >= 0 ? lt : Like("L", "Toe"), Find("L_Foot") is int lf and >= 0 ? lf : Like("L", "Foot")),
                            (Find("R_Toe0", "R_Toe") is int rt and >= 0 ? rt : Like("R", "Toe"), Find("R_Foot") is int rf and >= 0 ? rf : Like("R", "Foot")) };
        var v = Vector3.Zero; int n = 0;
        foreach (var (toe, foot) in pairs)
            if (toe >= 0 && foot >= 0) { v += w[toe] - w[foot]; n++; }
        int head = Find("Head"), pelvis = Find("Pelvis");
        how = $"toes−feet over {n} feet" + (head >= 0 && pelvis >= 0 ? $"; Head {w[head]:0.###} Pelvis {w[pelvis]:0.###}" : "");
        if (n == 0) return null;
        v.Y = 0;
        return v.Length() > 1e-4f ? Vector3.Normalize(v) : null;
    }

    static int Skel(string[] files)
    {
        foreach (string path in files)
        {
            var f = LoadFlver(path);
            var fwd = ModelForward(f, out string how);
            Console.WriteLine($"{Path.GetFileName(path)}: {f.Nodes.Count} nodes; model forward {(fwd is { } v ? $"({v.X:0.###}, {v.Z:0.###}) = compass {Compass(v):0.#}°" : "?")} ({how})");
            if (fwd == null) Console.WriteLine("   nodes: " + string.Join(", ", f.Nodes.Select(n => n.Name)));
        }
        return 0;
    }

    // ---------------------------------------------------------------- DS1 characters in their map
    static int Ds1(string map, int[] entities)
    {
        string root = Program.DefaultDsrRoot;
        var msb = MSB1.Read(Path.Combine(root, "map", "MapStudio", map + ".msb"));
        var all = FloorProbe.FromDs1(root, map);
        var walls = FloorProbe.FromDs1(root, map, p => p.HitFilterID == 8 && !p.DisableStart);
        var bonfire = msb.Parts.Objects.FirstOrDefault(o => o.ModelName == "o0200");
        foreach (int entity in entities)
        {
            var e = msb.Parts.Enemies.FirstOrDefault(x => x.EntityID == entity);
            if (e == null) { Console.WriteLine($"{entity}: no MSB character"); continue; }
            Console.WriteLine($"== {e.Name} ({e.ModelName}) entity {entity} npc {e.NPCParamID} pos {e.Position:0.###} rot {e.Rotation:0.###} InitAnimID {e.InitAnimID} collision part {e.CollisionName}");
            Console.WriteLine("   DS1 collision under it (all filters; y, |normal y|, part):");
            foreach (var h in all.Down(e.Position.X, e.Position.Z).Where(h => MathF.Abs(h.Y - e.Position.Y) < 6))
                Console.WriteLine($"     y {h.Y:0.###} ({h.Y - e.Position.Y:+0.###;-0.###} vs root) n {h.NormalY:0.##} {h.Part}");
            var pieces = FloorProbe.FromDs1Pieces(root, map, e.Position, 4);
            Console.WriteLine("   DS1 visible map pieces under it:");
            foreach (var h in pieces.Down(e.Position.X, e.Position.Z).Where(h => MathF.Abs(h.Y - e.Position.Y) < 6))
                Console.WriteLine($"     y {h.Y:0.###} ({h.Y - e.Position.Y:+0.###;-0.###} vs root) n {h.NormalY:0.##} {h.Part}");
            // footprint: pieces/collision heights within 0.6 m around the root
            float pieceMax = float.MinValue, colMax = float.MinValue;
            for (float dx = -0.6f; dx <= 0.61f; dx += 0.2f) for (float dz = -0.6f; dz <= 0.61f; dz += 0.2f)
            {
                foreach (var h in pieces.Down(e.Position.X + dx, e.Position.Z + dz)) if (h.Y < e.Position.Y + 0.3f && h.NormalY > 0.3f) { pieceMax = MathF.Max(pieceMax, h.Y); break; }
                foreach (var h in walls.Down(e.Position.X + dx, e.Position.Z + dz)) if (h.Y < e.Position.Y + 0.3f && h.NormalY > 0.5f) { colMax = MathF.Max(colMax, h.Y); break; }
            }
            Console.WriteLine($"   within 0.6 m: highest visible surface under root+0.3 {pieceMax:0.###}, highest filter-8 floor {colMax:0.###}");
            Console.WriteLine("   filter-8 wall distance by compass direction (root +0.5 / +1.0 m; '-' = none within 4 m):");
            var line = new List<string>();
            for (int a = -180; a < 180; a += 15)
            {
                float? d1 = walls.Ray(e.Position + new Vector3(0, 0.5f, 0), Dir(a), 4), d2 = walls.Ray(e.Position + new Vector3(0, 1.0f, 0), Dir(a), 4);
                line.Add($"{a,4}:{(d1 is float x ? x.ToString("0.00") : "-")}/{(d2 is float y ? y.ToString("0.00") : "-")}");
            }
            foreach (var chunk in line.Chunk(6)) Console.WriteLine("     " + string.Join("  ", chunk));
            float yaw = e.Rotation.Y;
            Console.WriteLine($"   conventions for rot {yaw}: model +Z → compass {Norm(yaw):0.#}; model −Z → compass {Norm(yaw + 180):0.#}; mirrored (−θ) +Z → {Norm(-yaw):0.#}, −Z → {Norm(180 - yaw):0.#}");
            if (bonfire != null)
            {
                var d = bonfire.Position - e.Position;
                Console.WriteLine($"   bonfire {bonfire.Name} at {bonfire.Position:0.##}: compass {Compass(d):0.#}°, {new Vector2(d.X, d.Z).Length():0.##} m");
            }
        }
        return 0;
    }

    // ---------------------------------------------------------------- every DS1 character of a map: which way is open?
    /// <summary>`place-survey census MAP` — every DS1 MSB character (not the c1000 talk dummies): the filter-8 wall distance
    /// at root +1.0 m in the direction DS1's model −Z faces (compass θ+180) and in the opposite one (θ), and the walkable
    /// floor 1.5 m ahead in both. A character placed by a designer faces open space; counting the characters whose
    /// direction ends at a wall within 1 m tests the convention over the whole map.</summary>
    static int Census(string map)
    {
        string root = Program.DefaultDsrRoot;
        var msb = MSB1.Read(Path.Combine(root, "map", "MapStudio", map + ".msb"));
        var walls = FloorProbe.FromDs1(root, map, p => p.HitFilterID == 8 && !p.DisableStart);
        int n = 0, blockedMinusZ = 0, blockedPlusZ = 0;
        Console.WriteLine($"{"part",-14} {"model",-6} {"entity",8} {"rot",6} {"init",5} | −Z (θ+180): wall@1m / floor@1.5m | +Z (θ): wall / floor");
        foreach (var e in msb.Parts.Enemies.Where(e => e.ModelName != "c1000"))
        {
            float a1 = Norm(e.Rotation.Y + 180), a2 = Norm(e.Rotation.Y);
            float? w1 = walls.Ray(e.Position + new Vector3(0, 1, 0), Dir(a1), 4), w2 = walls.Ray(e.Position + new Vector3(0, 1, 0), Dir(a2), 4);
            var f1 = walls.Floor(e.Position + Dir(a1) * 1.5f, 0.8f, 3); var f2 = walls.Floor(e.Position + Dir(a2) * 1.5f, 0.8f, 3);
            n++;
            if (w1 is < 1f) blockedMinusZ++;
            if (w2 is < 1f) blockedPlusZ++;
            static string W(float? w) => w is float x ? x.ToString("0.00") : "  - ";
            static string F(FloorProbe.Hit? h, float y) => h is { } x ? (x.Y - y).ToString("+0.0;-0.0") : "void";
            Console.WriteLine($"{e.Name,-14} {e.ModelName,-6} {e.EntityID,8} {e.Rotation.Y,6:0.#} {e.InitAnimID,5} | {W(w1),6} / {F(f1, e.Position.Y),5} | {W(w2),6} / {F(f2, e.Position.Y),5}");
        }
        Console.WriteLine($"{map}: {n} characters; wall within 1 m straight ahead: DS1 model −Z (θ+180) {blockedMinusZ}, model +Z (θ) {blockedPlusZ}");
        return 0;
    }

    // ---------------------------------------------------------------- every DS1 sitter: where is its seat (rule P2)?
    /// <summary>`place-survey seats` — every DS1 MSB character of every map with an InitAnimID 78xx (c0000 sits/kneels):
    /// the clip's frame-0 pelvis (Ds1Placement.ClipSeat), the seat point with DS1's transform, and DSR's own collision
    /// (all filters) under it: the highest surface within 0.2 m and the pelvis height over it. Tests rule P2 on DS1's
    /// data: a sitter's pelvis rests a constant ~0.1 m over the collision under it.</summary>
    static int Seats()
    {
        string root = Program.DefaultDsrRoot;
        var probes = new Dictionary<string, FloorProbe>();
        foreach (string path in Directory.GetFiles(Path.Combine(root, "map", "MapStudio"), "m*.msb").OrderBy(p => p))
        {
            string map = Path.GetFileNameWithoutExtension(path);
            MSB1 msb;
            try { msb = MSB1.Read(path); } catch (InvalidDataException) { Console.WriteLine($"{map}: not an MSB1 this reader takes, skipped"); continue; }
            foreach (var e in msb.Parts.Enemies.Where(e => e.InitAnimID is >= 7800 and < 7900 && e.ModelName == "c0000"))
            {
                var seatLocal = Ds1Placement.ClipSeat(root, "c0000", e.InitAnimID);
                if (seatLocal is not { } s) { Console.WriteLine($"{map} {e.Name} entity {e.EntityID} init {e.InitAnimID}: clip missing"); continue; }
                if (!probes.TryGetValue(map, out var probe)) probes[map] = probe = FloorProbe.FromDs1(root, map, p => !p.DisableStart);
                var seat = e.Position + Ds1Placement.Rotate(s, e.Rotation.Y);
                float? top = null;
                for (int k = -1; k < 8; k++)
                {
                    var q = k < 0 ? seat : seat + new Vector3(MathF.Sin(k * MathF.PI / 4), 0, MathF.Cos(k * MathF.PI / 4)) * 0.2f;
                    if (probe.Floor(q, above: 0.3f, below: 0.6f) is { } h && (top == null || h.Y > top)) top = h.Y;
                }
                Console.WriteLine($"{map} {e.Name,-12} entity {e.EntityID,8} init {e.InitAnimID}: pelvis {s:0.00} → seat {seat:0.00}; DS1 surface {(top is float t ? t.ToString("0.00") : "none")}; pelvis over it {(top is float t2 ? (seat.Y - t2).ToString("+0.00;-0.00") : "-")}; root over it {(top is float t3 ? (e.Position.Y - t3).ToString("+0.00;-0.00") : "-")}");
            }
        }
        return 0;
    }

    // ---------------------------------------------------------------- DS1 regions: which are character placements (P1)?
    /// <summary>`place-survey regions [MAP]` — session 9 fix pass, evidence for rule P1 on regions: every DS1 region with an
    /// entity id of every map (or MAP), whether a DS1 placement instruction names it (Ds1Placement.PlacementUses, all of
    /// DSR's event scripts) and whether it has DS1's point-marker shape; and, for the named ones, the filter-8 wall at +1 m
    /// within 1 m ahead under θ+180 (DS1 model −Z) and under θ, as the character census does.</summary>
    static int Regions(string? only)
    {
        string root = Program.DefaultDsrRoot;
        var uses = Ds1Placement.PlacementUses(root);
        int total = 0, used = 0, usedMarker = 0, markers = 0, markerUsed = 0, wallMinusZ = 0, wallPlusZ = 0, probed = 0;
        int mMinusZ = 0, mPlusZ = 0, mProbed = 0; // the same for every point marker (named or not)
        var usedOther = new List<string>(); var markerUnused = new List<string>();
        foreach (string path in Directory.GetFiles(Path.Combine(root, "map", "MapStudio"), "m*.msb").OrderBy(p => p))
        {
            string map = Path.GetFileNameWithoutExtension(path);
            if (only != null && map != only) continue;
            MSB1 msb;
            try { msb = MSB1.Read(path); } catch (InvalidDataException) { continue; }
            FloorProbe? walls = null;
            foreach (var r in msb.Regions.GetEntries().Where(r => r.EntityID > 0))
            {
                total++;
                bool marker = Ds1Placement.IsPointMarker(r), named = uses.TryGetValue(r.EntityID, out var how);
                string shape = r.Shape switch { MSB.Shape.Box b => $"Box({b.Width:0.##}, {b.Height:0.##}, {b.Depth:0.##})", MSB.Shape.Sphere s => $"Sphere({s.Radius:0.##})", MSB.Shape.Cylinder c => $"Cylinder({c.Radius:0.##}, {c.Height:0.##})", var s => s.GetType().Name };
                if (marker) { markers++; if (named) markerUsed++; else markerUnused.Add($"{map} {r.EntityID} {r.Name} {shape} rot {r.Rotation.Y:0.#}"); }
                if (!named && !marker) continue;
                walls ??= FloorProbe.FromDs1(root, map, p => p.HitFilterID == 8 && !p.DisableStart);
                float? w1 = walls.Ray(r.Position + new Vector3(0, 1, 0), Dir(Norm(r.Rotation.Y + 180)), 4), w2 = walls.Ray(r.Position + new Vector3(0, 1, 0), Dir(Norm(r.Rotation.Y)), 4);
                if (marker) { mProbed++; if (w1 is < 1f) mMinusZ++; if (w2 is < 1f) mPlusZ++; }
                if (!named) continue;
                used++;
                if (marker) usedMarker++; else usedOther.Add($"{map} {r.EntityID} {r.Name} {shape} rot {r.Rotation.Y:0.#}: {string.Join(" | ", how!)}");
                probed++;
                if (w1 is < 1f) wallMinusZ++;
                if (w2 is < 1f) wallPlusZ++;
                Console.WriteLine($"{map} {r.EntityID,8} {r.Name,-24} {shape,-22} rot {r.Rotation.Y,6:0.#} wall@1m θ+180 {(w1 is float a ? a.ToString("0.00") : "  - ")} θ {(w2 is float b2 ? b2.ToString("0.00") : "  - ")} | {string.Join(" | ", how!)}");
            }
        }
        Console.WriteLine($"{total} DS1 regions with an entity id; {used} named by a DS1 placement instruction: {usedMarker} of them point markers, {usedOther.Count} other shapes");
        foreach (var s in usedOther) Console.WriteLine("  named, not a marker: " + s);
        Console.WriteLine($"{markers} point markers: {markerUsed} named by a placement instruction, {markerUnused.Count} not:");
        foreach (var s in markerUnused) Console.WriteLine("  marker, not named: " + s);
        Console.WriteLine($"named regions with a wall within 1 m straight ahead (filter 8, +1 m): θ+180 {wallMinusZ}, θ {wallPlusZ} (of {probed})");
        Console.WriteLine($"point markers (named or not) with a wall within 1 m straight ahead: θ+180 {mMinusZ}, θ {mPlusZ} (of {mProbed})");
        return 0;
    }

    // ---------------------------------------------------------------- DS1 c0000 78xx idles: rule P3's input (fix pass)
    /// <summary>`place-survey clips` — every c0000 InitAnim clip 7800-7899 DSR has (c0000_a00_hi/md/lo): frame-0 pelvis and
    /// head (Ds1Placement.ClipPose, model space: up +Y, behind +Z) and rule P3's verdict (sit = pelvis &lt; 0.6 m and head ≥
    /// 0.3 m over the pelvis).</summary>
    static int Clips()
    {
        string root = Program.DefaultDsrRoot;
        for (int id = 7800; id < 7900; id++)
        {
            if (Ds1Placement.ClipPose(root, "c0000", id) is not { } p) continue;
            Console.WriteLine($"a00_{id}: pelvis {p.Pelvis:0.00} head {p.Head:0.00} head−pelvis {p.Head.Y - p.Pelvis.Y:+0.00;-0.00} → {(Ds1Placement.Sits(p) ? "sits (13)" : "stands (0)")}");
        }
        return 0;
    }

    // ---------------------------------------------------------------- vanilla DS2 generators
    static int Ds2(string map, int[] gens)
    {
        string data = ExtractDs2.DefaultOut;
        var probe = FloorProbe.FromFolder(data, map);
        Console.WriteLine($"{map}: {probe.Count} DS2 collision triangles");
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        var loc = PARAM.Read(Path.Combine(data, "param", $"generatorlocation_{map}.param"));
        var gen = PARAM.Read(Path.Combine(data, "param", $"generatorparam_{map}.param"));
        var reg = PARAM.Read(Path.Combine(data, "param", $"generatorregistparam_{map}.param"));
        reg.ApplyParamdefCarefully(defs);
        // MapOffset: per-map params are map-local, the collision parts are MSB (world) positions: world = MapOffset + local
        var offset = MSB2.Read(Path.Combine(data, "map", map, map + ".msb")).Events.MapOffsets.FirstOrDefault()?.Translation ?? Vector3.Zero;
        Console.WriteLine($"   MapOffset {offset}");
        foreach (int g in gens)
        {
            var lr = loc.Rows.FirstOrDefault(r => r.ID == g);
            var gr = gen.Rows.FirstOrDefault(r => r.ID == g);
            if (lr == null || gr == null) { Console.WriteLine($"generator {g}: missing"); continue; }
            byte[] l = RawBytes(loc, lr); byte[] gb = RawBytes(gen, gr);
            var pos = new Vector3(BitConverter.ToSingle(l, 0), BitConverter.ToSingle(l, 4), BitConverter.ToSingle(l, 8)) + offset;
            var rot = new Vector3(BitConverter.ToSingle(l, 12), BitConverter.ToSingle(l, 16), BitConverter.ToSingle(l, 20));
            int regist = BitConverter.ToInt32(gb, 8);
            var rr = reg[regist];
            string startUp = rr != null ? $"enemy {rr["EnemyId"].Value} startUp {rr["startUpStateType"].Value} logic {rr["HighLogicId"].Value}/{rr["LowLogicId"].Value}" : $"regist {regist} missing";
            Console.WriteLine($"== generator {g}: pos {pos:0.###} rot {rot:0.###} unk18 {l[24]} ({startUp})");
            foreach (var h in probe.Down(pos.X, pos.Z).Where(h => MathF.Abs(h.Y - pos.Y) < 6))
                Console.WriteLine($"     floor y {h.Y:0.###} ({h.Y - pos.Y:+0.###;-0.###} vs root) n {h.NormalY:0.##} {h.Part}");
            Console.WriteLine("   by compass direction: wall distance at root +0.5/+1.0 m ('-' none within 4 m) | floor drop 1.5 m away (vs root):");
            var line = new List<string>();
            for (int a = -180; a < 180; a += 15)
            {
                float? d1 = probe.Ray(pos + new Vector3(0, 0.5f, 0), Dir(a), 4), d2 = probe.Ray(pos + new Vector3(0, 1.0f, 0), Dir(a), 4);
                var q = pos + Dir(a) * 1.5f;
                var f = probe.Floor(q, 1.0f, 30);
                line.Add($"{a,4}:{(d1 is float x ? x.ToString("0.0") : "-")}/{(d2 is float y ? y.ToString("0.0") : "-")}|{(f is { } ff ? (ff.Y - pos.Y).ToString("+0.0;-0.0") : "void")}");
            }
            foreach (var chunk in line.Chunk(6)) Console.WriteLine("     " + string.Join("  ", chunk));
            Console.WriteLine($"   rot {rot.Y}: model +Z faces compass {Norm(rot.Y):0.#} (DS2's location rule)");
            foreach (float a in new[] { Norm(rot.Y), Norm(rot.Y + 180) })
                Console.WriteLine($"   along compass {a,6:0.#}: floor vs root at 1..12 m: " + string.Join(" ", Enumerable.Range(1, 12).Select(k =>
                    probe.Floor(pos + Dir(a) * k + new Vector3(0, 2, 0), 0, 60) is { } f ? (f.Y - pos.Y).ToString("+0.0;-0.0") : "void"))
                    + $" | wall at +1.0 m: {(probe.Ray(pos + new Vector3(0, 1, 0), Dir(a), 12) is float w ? w.ToString("0.0") : "-")}");
        }
        return 0;
    }

    static byte[] RawBytes(PARAM p, PARAM.Row r)
    {
        if (p.AppliedParamdef == null)
        {
            var blob = new PARAMDEF { ParamType = p.ParamType, DataVersion = p.ParamdefDataVersion, Unicode = true, FormatVersion = 104, BigEndian = false };
            blob.Fields.Add(new PARAMDEF.Field(blob, PARAMDEF.DefType.dummy8, "data") { ArrayLength = (int)p.DetectedSize });
            p.ApplyParamdef(blob);
        }
        return (byte[])r["data"].Value;
    }
}
