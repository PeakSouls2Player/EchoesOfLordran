using System.Globalization;
using System.Numerics;
using Lordran.Tools.Build.Chr;
using Lordran.Tools.Build.Maps;
using Lordran.Tools.Formats.Morpheme;
using Lordran.Tools.Textures;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// pose-scene NPC OUT.png [--pack DIR] [--frame F] [--geom vis|col] [--views front,side,east,player] [--stand-in]
/// (session 9, track npcpose): an offline render of a seated DS1 NPC in its map, DS1 next to ours, to compare the idle
/// pose against the geometry without the game. Panels per view (columns):
///  1. DS1: DSR's map (visible pieces, or its collision with --geom col) with DS1's own skeleton posed by DS1's idle clip
///     (decoded Havok, Ds1AnimBridge) at DS1's MSB position and rotation (white tubes).
///  2. ours: the pack's map (pieces or collision) with the NPC's DS2 armour/weapon parts skinned by the clip the NPC
///     network (c8001 in the pack) holds in the slot, at the pack's generator position and yaw (rule P1: θ+180, the DS2
///     body faces model −Z and the engine adds the half turn), plus that skeleton (yellow); DS1's skeleton overlaid in grey.
///  3. (--stand-in) the same parts posed by the DS2 clip c0001 has in the slot (vanilla, e.g. Saulden's a77_03_0010), at
///     the same root: what a c0001-network NPC (animId 100) shows there.
/// What this does not show: DS2's run-time layers above the clip (head look SM 8290, uneven-terrain IK 8286, foot lock
/// 8284/8285, guard/stance layers) — NOTES.md lists them; the in-game test compares.
/// Space: Morpheme character space (Z up, forward −Y) → FLVER model space by (x, y, z) → (x, z, y) (Ds1AnimBridge), the
/// model → world by RotY(DS1 θ) + position (DS1 MSB; = DS2 yaw − 180 under rule P1).
/// </summary>
public static class PoseScene
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public sealed record Npc(string Key, string Ds1Map, string Ds2Map, int Entity, int Generator, int Ds1Anim, string Slot,
        string[] Parts, string? RightWeapon, string? LeftWeapon, string Ds1Chr = "c0000");

    /// <summary>Parts: "ds2:" = vanilla data\ds2\model\parts, "pack:" = the pack's gamedata\model\parts.</summary>
    public static readonly Npc[] Npcs =
    [
        new("oscar", "m18_01_00_00", "m18_01_00_00", 6023, 6023, 7805, "a77_03_0010",
            ["ds2:head/hd_1060_m.bnd", "ds2:body/bd_1060_m.bnd", "ds2:arm/am_1060_m.bnd", "ds2:leg/lg_1060_m.bnd"],
            "pack:weapon/wp_6209_m.bnd", "pack:shield/sd_8056_m.bnd"),
        new("cw", "m10_02_00_00", Lordran.Tools.Build.Maps.MapHosts.Host("m10_02_00_00"), 6270, 6270, 7810, "a77_04_0210",
            ["pack:body/bd_4017_m.bnd", "pack:arm/am_4017_m.bnd", "pack:leg/lg_4017_m.bnd"],
            "ds2:weapon/wp_1220_m.bnd", "pack:shield/sd_8050_m.bnd"),
        // pass 3: Anastacia (DS1 c2750, own body re-bound as a part; DS1 column = her default idle a00_0000) on slot 13,
        // which under I1c holds Oscar's a00_7805 (her stand-in)
        new("anastacia", "m10_02_00_00", Lordran.Tools.Build.Maps.MapHosts.Host("m10_02_00_00"), 6060, 6060, 0, "a77_03_0010",
            ["pack:body/bd_4750_f.bnd"], null, null, "c2750"),
    ];

    const int PW = 520, PH = 400;

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: pose-scene oscar|cw|anastacia OUT.png [--pack DIR] [--frame F] [--geom vis|col] [--views front,side,east,player] [--stand-in]"); return 2; }
        var npc = Npcs.FirstOrDefault(n => n.Key == args[1]) ?? throw new ArgumentException($"unknown NPC {args[1]}");
        string outPng = args[2];
        string pack = CliArgs.Option(args, "--pack", out _) ?? Build.Pack.DefaultOut;
        string gamedata = Path.Combine(pack, "gamedata");
        int frame = int.Parse(CliArgs.Option(args, "--frame", out _) ?? "0", Inv);
        bool col = (CliArgs.Option(args, "--geom", out _) ?? "vis") == "col";
        // --stand-in-clip NAME (pass 2): the third column plays this vanilla c0001 clip (name prefix, e.g. a00_00_0020 =
        // BattleStandby) instead of the slot's: what an NPC shows when its network never leaves BasicMove
        string? standClip = CliArgs.Option(args, "--stand-in-clip", out _);
        bool standIn = args.Contains("--stand-in") || standClip != null, backfaces = args.Contains("--backfaces");
        // --cut H: drop map triangles more than H m over the root (ceilings, upper floors) for top views
        float cut = float.Parse(CliArgs.Option(args, "--cut", out _) ?? "1000", Inv);
        var views = (CliArgs.Option(args, "--views", out _) ?? "front,side,east").Split(';', ',').Aggregate(new List<string>(), (l, x) => { if (l.Count > 0 && l[^1].StartsWith("cam:") && l[^1].Count(ch => ch == ',') < 5) l[^1] += "," + x; else l.Add(x); return l; }).ToArray();
        string dsr = Program.DefaultDsrRoot, ds2 = ExtractDs2.DefaultOut;

        // ---- DS1: MSB part, clip
        var msb1 = MSB1.Read(Path.Combine(dsr, "map", "MapStudio", npc.Ds1Map + ".msb"));
        var part = msb1.Parts.Enemies.First(e => e.EntityID == npc.Entity);
        var ds1Src = npc.Ds1Chr == "c0000" ? NpcIdle.LoadC0000(dsr, [npc.Ds1Anim]) : Ds1ChrSource.Load(dsr, npc.Ds1Chr);
        var ds1Clip = Ds1AnimBridge.Bridge(ds1Src.Samples(npc.Ds1Anim), 30);
        var ds1Pose = MorphemePose.From(Ds1RigAnims.WithDs2Names(ds1Clip.Skeleton));
        int f1 = Math.Clamp(frame, 0, ds1Clip.Anim.Frames - 1);
        var (ds1P, _) = ds1Pose.World(ds1Clip.Anim, f1, false);
        var ds1World = Matrix4x4.CreateRotationY(part.Rotation.Y * MathF.PI / 180) * Matrix4x4.CreateTranslation(part.Position);

        // ---- ours: generator row (pack param + map offset), slot clip in the pack's NPC network, vanilla stand-in
        var (genPos, genYaw) = Generator(gamedata, npc.Ds2Map, npc.Generator);
        var oursWorld = Matrix4x4.CreateRotationY((genYaw + 180) * MathF.PI / 180) * Matrix4x4.CreateTranslation(genPos);
        // the pack's NPC network: c0001.anibnd (rule I1c, human route) or c8001.anibnd (own route / A/B packs)
        // track npcpose2 (rule I1d): the NPC's own template network (c8002 / c8003) first: its slot clip = the loop leaf's
        // (template c7240 leaf 1004, a75_01_7110); the stand-in column is then the vanilla template's clip on that leaf
        var own = NpcIdle.Nets.FirstOrDefault(x => x.Ds1Anim == npc.Ds1Anim && npc.Ds1Chr == "c0000");
        string? ownPath = own != null ? Path.Combine(gamedata, "morpheme4", "chr", $"{own.Name}.anibnd.dcx") : null;
        string net, slotClip = npc.Slot, standNet = Path.Combine(ds2, "morpheme4", "chr", "c0001.anibnd.dcx");
        if (ownPath != null && File.Exists(ownPath))
        {
            net = ownPath;
            slotClip = LeafClip(ownPath, own!.LoopLeaf);
            standNet = Path.Combine(ds2, "morpheme4", "chr", $"{own.Template}.anibnd.dcx");
        }
        else
            net = new[] { "c0001", $"c{NpcIdle.Anibnd:D4}" }.Select(n => Path.Combine(gamedata, "morpheme4", "chr", $"{n}.anibnd.dcx")).FirstOrDefault(File.Exists)
                ?? Path.Combine(gamedata, "morpheme4", "chr", $"{NpcIdle.AnibndName}.anibnd.dcx");
        var (rig, map, clip, clipName) = LoadClip(File.Exists(net) ? net : Path.Combine(ds2, "morpheme4", "chr", "c0001.anibnd.dcx"), slotClip);
        var (_, _, orig, origName) = LoadClip(standNet, standClip ?? slotClip);
        if (standIn) Console.WriteLine($"  stand-in column: vanilla {Path.GetFileName(standNet)} {origName} ({orig.Frames} frames)");
        var pose2 = MorphemePose.From(rig, map);
        int f2 = Math.Clamp(frame, 0, clip.Frames - 1), f0 = Math.Clamp(frame, 0, orig.Frames - 1);
        var (p2, q2) = pose2.World(clip, f2, false);
        var (po, qo) = pose2.World(orig, f0, false);

        Console.WriteLine($"{npc.Key}: DS1 {part.Name} at ({F(part.Position)}) θ {part.Rotation.Y:0.#}, a00_{npc.Ds1Anim} frame {f1}/{ds1Clip.Anim.Frames}; ours generator {npc.Generator} at ({F(genPos)}) yaw {genYaw:0.#}, {Path.GetFileName(net)} {clipName} frame {f2}/{clip.Frames} ({clip.Frames} frames, {(File.Exists(net) ? "pack network" : "no pack network: c0001")})");
        // pelvis / feet / head in world space, both
        foreach (var (who, pose, P, W) in new[] { ("DS1", ds1Pose, ds1P, ds1World), ("ours", pose2, p2, oursWorld), ("stand-in", pose2, po, oursWorld) })
        {
            string J(string n) { int i = pose.Index(n); return i < 0 ? "-" : F(Vector3.Transform(ToFlver(P[i]), W)); }
            Console.WriteLine($"  {who,-8} world: pelvis ({J("Pelvis")}) head ({J("Head")}) feet ({J("L_Foot")}) ({J("R_Foot")}) hands ({J("L_Hand")}) ({J("R_Hand")})");
        }

        // ---- geometry
        var geo1 = col ? Ds1Collision(dsr, npc.Ds1Map, part.Position) : Ds1Pieces(dsr, npc.Ds1Map, part.Position);
        var geo2 = col ? PackCollision(pack, npc.Ds2Map, genPos) : PackPieces(gamedata, npc.Ds2Map, genPos);
        geo1 = geo1.Where(x => (x.A.Y + x.B.Y + x.C.Y) / 3 <= part.Position.Y + cut).ToList();
        geo2 = geo2.Where(x => (x.A.Y + x.B.Y + x.C.Y) / 3 <= genPos.Y + cut).ToList();
        Console.WriteLine($"  geometry ({(col ? "collision" : "visible pieces")} within 14 m): DS1 {geo1.Count} triangles, ours {geo2.Count}");
        // floor checks under the pelvis/feet (collision) — the in-game contact
        var probe2 = FloorProbe.FromFolder(pack, npc.Ds2Map);
        var probe1 = FloorProbe.FromDs1(dsr, npc.Ds1Map);   // every DS1 hit filter (the pack ports filter 8)
        string Under(FloorProbe p, Vector3 w)
        {
            var fl = p.Floor(w + new Vector3(0, 0.4f, 0), 1.0f, 3f);
            return fl is { } h ? $"{h.Y:0.00} ({w.Y - h.Y:+0.00;-0.00} m, {h.Part})" : "none within 3 m";
        }
        Console.WriteLine($"  root ({F(genPos)}): pack floor {Under(probe2, genPos)}; DS1 floor {Under(probe1, part.Position)}");
        foreach (var (who, P) in new[] { ("ours", p2), ("stand-in", po) })
            foreach (var j in new[] { "Pelvis", "L_Foot", "R_Foot" })
            {
                var w = Vector3.Transform(ToFlver(P[pose2.Index(j)]), oursWorld);
                Console.WriteLine($"  {who,-8} {j,-7} y {w.Y:0.00}: pack floor {Under(probe2, w)}{(who == "ours" ? $"; DS1 floor {Under(probe1, w - genPos + part.Position)}" : "")}");
            }

        // ---- the posed DS2 parts (ours and the stand-in)
        var meshes = LoadParts(npc, gamedata, ds2);
        var ours = Skin(meshes, rig, pose2, p2, q2, oursWorld);
        var stand = standIn ? Skin(meshes, rig, pose2, po, qo, oursWorld) : null;

        // ---- cameras: around our root (the DS1 panel uses the same eye/target shifted by DS1 − ours: the same frame)
        var fwd = Vector3.Normalize(new Vector3(MathF.Sin(genYaw * MathF.PI / 180), 0, MathF.Cos(genYaw * MathF.PI / 180)));
        var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
        var focus = genPos + new Vector3(0, 0.35f, 0);
        int cols = standIn ? 3 : 2;
        var sheet = new Image(PW * cols, PH * views.Length);
        for (int v = 0; v < views.Length; v++)
        {
            (Vector3 Eye, Vector3 Target) cam = views[v] switch
            {
                "front" => (focus + fwd * 2.6f + new Vector3(0, 0.55f, 0), focus),
                "side" => (focus + right * 2.6f + fwd * 0.4f + new Vector3(0, 0.25f, 0), focus),
                "east" => (focus + new Vector3(2.9f, 1.25f, -1.1f), focus),
                // cam:EX,EY,EZ,TX,TY,TZ = an explicit world camera (eye, target), e.g. an in-game shot's viewpoint
                var c when c.StartsWith("cam:") => Cam(c[4..]),
                "player" => (focus + fwd * 3.2f + right * 0.6f + new Vector3(0, 1.55f, 0), focus),
                _ => throw new ArgumentException($"view {views[v]}"),
            };
            var shift = part.Position - genPos;
            var img1 = Panel(geo1, null, ds1Pose, ds1P, ds1World, null, cam.Eye + shift, cam.Target + shift, new Rgba(235, 235, 235, 255), backfaces);
            var img2 = Panel(geo2, ours, pose2, p2, oursWorld, (ds1Pose, ds1P, ds1World, -shift), cam.Eye, cam.Target, new Rgba(250, 220, 90, 255), backfaces);
            Blit(sheet, img1, 0, v * PH);
            Blit(sheet, img2, PW, v * PH);
            if (stand != null) Blit(sheet, Panel(geo2, stand, pose2, po, oursWorld, null, cam.Eye, cam.Target, new Rgba(120, 220, 255, 255), backfaces), 2 * PW, v * PH);
        }
        Png.Save(sheet, outPng);
        Console.WriteLine($"wrote {outPng} ({views.Length} views × {cols} panels: DS1 | ours{(standIn ? " | DS2 stand-in" : "")}; {(col ? "collision" : "visible pieces")})");
        return 0;
    }

    static (Vector3, Vector3) Cam(string s)
    {
        var v = s.Split(',').Select(x => float.Parse(x, Inv)).ToArray();
        return (new Vector3(v[0], v[1], v[2]), new Vector3(v[3], v[4], v[5]));
    }

    static string F(Vector3 v) => $"{v.X.ToString("0.00", Inv)}, {v.Y.ToString("0.00", Inv)}, {v.Z.ToString("0.00", Inv)}";

    public static Vector3 ToFlver(V3 p) => new((float)p.X, (float)p.Z, (float)p.Y);

    /// <summary>The generator's position (param + the map's MapOffset, as spot-check) and yaw.</summary>
    static (Vector3 Pos, float Yaw) Generator(string gamedata, string map, int id)
    {
        var offset = MSB2.Read(Path.Combine(gamedata, "map", map, map + ".msb")).Events.MapOffsets.FirstOrDefault()?.Translation ?? Vector3.Zero;
        var p = PARAM.Read(Path.Combine(gamedata, "param", $"generatorlocation_{map}.param"));
        var blob = new PARAMDEF { ParamType = p.ParamType, DataVersion = p.ParamdefDataVersion, Unicode = true, FormatVersion = 104, BigEndian = false };
        blob.Fields.Add(new PARAMDEF.Field(blob, PARAMDEF.DefType.dummy8, "data") { ArrayLength = (int)p.DetectedSize });
        p.ApplyParamdef(blob);
        var row = p.Rows.First(r => r.ID == id);
        var d = (byte[])row["data"].Value;
        return (new Vector3(BitConverter.ToSingle(d, 0), BitConverter.ToSingle(d, 4), BitConverter.ToSingle(d, 8)) + offset, BitConverter.ToSingle(d, 16));
    }

    /// <summary>The NSA name (prefix without extension) the anim leaf plays in an anibnd's network (rule I1d's loop leaf).</summary>
    public static string LeafClip(string anibnd, int leaf)
    {
        var bnd = BND4.Read(DCX.Decompress(File.ReadAllBytes(anibnd)));
        var nmb = NmbFile.Read(bnd.Files.First(f => f.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)).Bytes);
        var net = new NmbNetwork(nmb.Bundles[nmb.IndexOf(10)].Data);
        var names = NmbFileNames.Read(nmb.Bundles[nmb.IndexOf(13)].Data).Anims;
        var sa = net.GetNode(leaf).Attribs.First(a => a.Type == 21);
        return Path.GetFileNameWithoutExtension(names[net.ClipIndex(sa)]);
    }

    /// <summary>Rig, the slot clip's channel map (by channel count, as npc-idle's sheet) and the decoded slot clip.</summary>
    public static (NmbRig Rig, NmbChannelMap Map, NsaAnim Clip, string Name) LoadClip(string anibnd, string slot)
    {
        var bnd = BND4.Read(DCX.Decompress(File.ReadAllBytes(anibnd)));
        var nmb = NmbFile.Read(bnd.Files.First(f => f.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)).Bytes);
        var rig = NmbRig.Read(nmb.Bundles[nmb.IndexOf(1)].Data);
        var file = bnd.Files.First(f => Path.GetFileName(f.Name.Replace('\\', '/')).StartsWith(slot, StringComparison.OrdinalIgnoreCase) && f.Name.EndsWith(".nsa", StringComparison.OrdinalIgnoreCase));
        var clip = NsaCodec.Decode(NsaFile.Parse(file.Bytes));
        var map = nmb.Bundles.Where(b => b.Type == 2).Select(b => NmbChannelMap.Read(b.Data)).First(m => m.AnimToRig.Count == clip.ChannelCount);
        return (rig, map, clip, Path.GetFileName(file.Name.Replace('\\', '/')));
    }

    // ------------------------------------------------------------------ geometry

    /// <summary>Cull = the face set culls back faces (a back face then is invisible in game: drawn red with --backfaces).</summary>
    public readonly record struct Tri(Vector3 A, Vector3 B, Vector3 C, Rgba Col, bool Cull = false);

    static bool Near(Vector3 a, Vector3 b, Vector3 c, Vector3 at, float r)
    {
        var g = (a + b + c) / 3;
        return MathF.Abs(g.X - at.X) <= r && MathF.Abs(g.Z - at.Z) <= r && MathF.Abs(g.Y - at.Y) <= r;
    }

    static List<Tri> FromProbe(FloorProbe p, Vector3 at, Rgba col) =>
        p.Triangles.Where(t => Near(t.A, t.B, t.C, at, 14)).Select(t => new Tri(t.A, t.B, t.C, col)).ToList();

    static List<Tri> Ds1Collision(string dsr, string map, Vector3 at) => FromProbe(FloorProbe.FromDs1(dsr, map, c => c.HitFilterID == 8), at, new Rgba(150, 200, 150, 255));
    static List<Tri> PackCollision(string pack, string map, Vector3 at) => FromProbe(FloorProbe.FromFolder(pack, map), at, new Rgba(150, 200, 150, 255));
    static List<Tri> Ds1Pieces(string dsr, string map, Vector3 at)
    {
        var msb = MSB1.Read(Path.Combine(dsr, "map", "MapStudio", map + ".msb"));
        string area = map.Substring(1, 2);
        var result = new List<Tri>();
        foreach (var part in msb.Parts.MapPieces)
        {
            string file = Path.Combine(dsr, "map", map, $"{part.ModelName}A{area}.flver.dcx");
            if (!File.Exists(file)) continue;
            var flver = FLVER2.Read(DCX.Decompress(File.ReadAllBytes(file)));
            var m = FloorProbe.Place(part.Position, part.Rotation, part.Scale);
            foreach (var mesh in flver.Meshes)
            {
                var fs = mesh.FaceSets.FirstOrDefault(f => f.Flags == FLVER2.FaceSet.FSFlags.None);
                if (fs == null) continue;
                var idx = fs.Triangulate(true);
                for (int i = 0; i + 2 < idx.Count; i += 3)
                {
                    if (idx[i] >= mesh.Vertices.Count || idx[i + 1] >= mesh.Vertices.Count || idx[i + 2] >= mesh.Vertices.Count) continue;
                    var a = Vector3.Transform(mesh.Vertices[idx[i]].Position, m); var b = Vector3.Transform(mesh.Vertices[idx[i + 1]].Position, m); var c = Vector3.Transform(mesh.Vertices[idx[i + 2]].Position, m);
                    if (float.IsFinite(a.X + b.X + c.X) && Near(a, b, c, at, 14)) result.Add(new Tri(a, b, c, new Rgba(190, 180, 165, 255), fs.CullBackfaces));
                }
            }
        }
        return result;
    }

    /// <summary>The pack's visible map pieces near a point (RenderView's loader: MSB2 map pieces × the mapbhd/bdt FLVERs).</summary>
    static List<Tri> PackPieces(string gamedata, string map, Vector3 at)
    {
        string dir = Path.Combine(gamedata, "model", "map");
        var bxf = BXF4.Read(Path.Combine(dir, map + ".mapbhd"), Path.Combine(dir, map + ".mapbdt"));
        var msb = MSB2.Read(Path.Combine(gamedata, "map", map, map + ".msb"));
        var result = new List<Tri>();
        foreach (var part in msb.Parts.MapPieces)
        {
            var file = bxf.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith(part.ModelName + ".flv", StringComparison.OrdinalIgnoreCase));
            if (file == null) continue;
            var flver = FLVER2.Read(DCX.Decompress(file.Bytes));
            var m = FloorProbe.Place(part.Position, part.Rotation, part.Scale);
            foreach (var mesh in flver.Meshes)
            {
                var fs = mesh.FaceSets.FirstOrDefault(f => f.Flags == FLVER2.FaceSet.FSFlags.None);
                if (fs == null) continue;
                var idx = fs.Triangulate(mesh.Vertices.Count < ushort.MaxValue);
                for (int i = 0; i + 2 < idx.Count; i += 3)
                {
                    if (idx[i] >= mesh.Vertices.Count || idx[i + 1] >= mesh.Vertices.Count || idx[i + 2] >= mesh.Vertices.Count) continue;
                    var a = Vector3.Transform(mesh.Vertices[idx[i]].Position, m); var b = Vector3.Transform(mesh.Vertices[idx[i + 1]].Position, m); var c = Vector3.Transform(mesh.Vertices[idx[i + 2]].Position, m);
                    if (float.IsFinite(a.X + b.X + c.X) && Near(a, b, c, at, 14)) result.Add(new Tri(a, b, c, new Rgba(190, 180, 165, 255), fs.CullBackfaces));
                }
            }
        }
        return result;
    }

    // ------------------------------------------------------------------ parts, skinning

    sealed record PartMesh(FLVER2 Flver, Matrix4x4[] InvBind, string? AttachBone);

    static List<PartMesh> LoadParts(Npc npc, string gamedata, string ds2)
    {
        string Resolve(string spec) => spec.StartsWith("ds2:") ? Path.Combine(ds2, "model", "parts", spec[4..].Replace('/', '\\'))
            : Path.Combine(gamedata, "model", "parts", spec[5..].Replace('/', '\\'));
        var list = new List<PartMesh>();
        void Add(string spec, string? attach)
        {
            string path = Resolve(spec);
            if (!File.Exists(path)) { Console.WriteLine($"  part {spec}: missing ({path})"); return; }
            byte[] b = File.ReadAllBytes(path);
            if (DCX.Is(b)) b = DCX.Decompress(b);
            var bnd = BND4.Read(b);
            var f = bnd.Files.FirstOrDefault(x => x.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase) && !x.Name.EndsWith("_s.flv", StringComparison.OrdinalIgnoreCase));
            if (f == null) { Console.WriteLine($"  part {spec}: no .flv"); return; }
            var flv = FLVER2.Read(f.Bytes);
            var bind = ChrCompare.World(flv);
            list.Add(new PartMesh(flv, bind.Select(m => Matrix4x4.Invert(m, out var i) ? i : Matrix4x4.Identity).ToArray(), attach));
        }
        foreach (var p in npc.Parts) Add(p, null);
        if (npc.RightWeapon != null) Add(npc.RightWeapon, "R_Weapon");
        if (npc.LeftWeapon != null) Add(npc.LeftWeapon, "L_Weapon");
        return list;
    }

    /// <summary>Skinned world-space triangles of the parts for one pose (PoseView's skinning: inverse bind × the rig
    /// bone's animated FLVER-space matrix; weapons ride their hand's weapon bone).</summary>
    static List<Tri> Skin(List<PartMesh> parts, NmbRig rig, MorphemePose pose, V3[] wp, Q4[] wq, Matrix4x4 world)
    {
        var anim = new Matrix4x4[rig.Names.Count];
        for (int r = 0; r < anim.Length; r++)
        {
            var q = wq[r]; var p = wp[r];
            var qf = new Quaternion((float)-q.X, (float)-q.Z, (float)-q.Y, (float)q.W);
            anim[r] = Matrix4x4.CreateFromQuaternion(qf) * Matrix4x4.CreateTranslation((float)p.X, (float)p.Z, (float)p.Y);
        }
        var tris = new List<Tri>();
        foreach (var part in parts)
        {
            var flv = part.Flver;
            Matrix4x4[] skin;
            if (part.AttachBone != null)
            {
                int r = rig.Names.IndexOf(part.AttachBone);
                var m = (r >= 0 ? anim[r] : Matrix4x4.Identity) * world;
                skin = Enumerable.Repeat(m, Math.Max(1, flv.Nodes.Count)).ToArray();
            }
            else
            {
                // a FLVER node the rig lacks (cloth, accessory nodes) rides its nearest ancestor that the rig has: its
                // animated world = bind(node) · bind(anc)⁻¹ · anim(anc), so its skin matrix is the ancestor's
                skin = new Matrix4x4[flv.Nodes.Count];
                for (int i = 0; i < flv.Nodes.Count; i++)
                {
                    int a = i, r = -1;
                    while (a >= 0 && (r = rig.Names.IndexOf(flv.Nodes[a].Name)) < 0) a = flv.Nodes[a].ParentIndex;
                    skin[i] = (r >= 0 ? part.InvBind[a] * anim[r] : Matrix4x4.Identity) * world;
                }
            }
            var col = part.AttachBone != null ? new Rgba(170, 170, 185, 255) : new Rgba(200, 120, 70, 255);
            foreach (var m in flv.Meshes)
            {
                var sv = m.Vertices.Select(v =>
                {
                    if (part.AttachBone != null) return Vector3.Transform(v.Position, skin[0]);
                    var acc = Vector3.Zero; float ws = 0;
                    for (int j = 0; j < 4; j++)
                    {
                        float w = v.BoneWeights[j];
                        if (w <= 0) continue;
                        int node = m.BoneIndices.Count > 0 ? m.BoneIndices[v.BoneIndices[j]] : v.BoneIndices[j];
                        if (node < 0 || node >= skin.Length) continue;
                        acc += w * Vector3.Transform(v.Position, skin[node]); ws += w;
                    }
                    if (ws <= 0)
                    {
                        // rigid meshes pick their node by NormalW (session 7 object rule) or the mesh's node
                        int node = m.BoneIndices.Count > 0 ? m.BoneIndices[Math.Clamp(v.NormalW, 0, m.BoneIndices.Count - 1)] : Math.Clamp(m.NodeIndex, 0, skin.Length - 1);
                        return Vector3.Transform(v.Position, skin[Math.Clamp(node, 0, skin.Length - 1)]);
                    }
                    return acc / ws;
                }).ToList();
                var fs = m.FaceSets.FirstOrDefault(x => x.Flags == FLVER2.FaceSet.FSFlags.None);
                if (fs == null) continue;
                var idx = fs.Triangulate(m.Vertices.Count < ushort.MaxValue);
                for (int i = 0; i + 2 < idx.Count; i += 3)
                    if (idx[i] < sv.Count && idx[i + 1] < sv.Count && idx[i + 2] < sv.Count)
                        tris.Add(new Tri(sv[idx[i]], sv[idx[i + 1]], sv[idx[i + 2]], col));
            }
        }
        return tris;
    }

    // ------------------------------------------------------------------ rendering

    static readonly (string A, string B)[] Limbs =
    [
        ("Root", "Pelvis"), ("Pelvis", "Spine"), ("Spine", "Spine1"), ("Spine1", "Neck"), ("Neck", "Head"),
        ("Spine1", "L_Clavicle"), ("L_Clavicle", "L_UpperArm"), ("L_UpperArm", "L_Forearm"), ("L_Forearm", "L_Hand"),
        ("Spine1", "R_Clavicle"), ("R_Clavicle", "R_UpperArm"), ("R_UpperArm", "R_Forearm"), ("R_Forearm", "R_Hand"),
        ("Pelvis", "L_Thigh"), ("L_Thigh", "L_Calf"), ("L_Calf", "L_Foot"), ("L_Foot", "L_Toe0"),
        ("Pelvis", "R_Thigh"), ("R_Thigh", "R_Calf"), ("R_Calf", "R_Foot"), ("R_Foot", "R_Toe0"),
    ];

    sealed class Raster(int w, int h)
    {
        public readonly Image Img = Fill(w, h);
        public readonly float[] Depth = Enumerable.Repeat(float.MaxValue, w * h).ToArray();
        static Image Fill(int w, int h) { var i = new Image(w, h); for (int k = 0; k < i.Pixels.Length; k++) i.Pixels[k] = new Rgba(28, 32, 44, 255); return i; }
    }

    static Image Panel(List<Tri> geo, List<Tri>? body, MorphemePose pose, V3[] P, Matrix4x4 world,
        (MorphemePose Pose, V3[] P, Matrix4x4 World, Vector3 Shift)? ghost, Vector3 eye, Vector3 target, Rgba boneCol, bool backfaces = false)
    {
        // the games' view convention (DirectX, left-handed): looking along +Z, +X is screen right
        var fwd = Vector3.Normalize(target - eye);
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, fwd));
        var up = Vector3.Cross(fwd, right);
        float f = 1 / MathF.Tan(50 * MathF.PI / 360), aspect = PW / (float)PH;
        var light = Vector3.Normalize(new Vector3(0.3f, 0.8f, 0.5f));
        var r = new Raster(PW, PH);
        Vector3 V(Vector3 p) { var d = p - eye; return new Vector3(Vector3.Dot(d, right), Vector3.Dot(d, up), Vector3.Dot(d, fwd)); }
        (float X, float Y) S(Vector3 v) => ((v.X / v.Z * f / aspect * 0.5f + 0.5f) * PW, (0.5f - v.Y / v.Z * f * 0.5f) * PH);
        void DrawTris(List<Tri> tris)
        {
            foreach (var t in tris)
            {
                var a = V(t.A); var b = V(t.B); var c = V(t.C);
                if (a.Z < 0.05f || b.Z < 0.05f || c.Z < 0.05f) continue;
                var pa = S(a); var pb = S(b); var pc = S(c);
                float area = (pb.X - pa.X) * (pc.Y - pa.Y) - (pb.Y - pa.Y) * (pc.X - pa.X);
                if (MathF.Abs(area) < 1e-6f) continue;
                var n = Vector3.Cross(t.B - t.A, t.C - t.A);
                if (n.LengthSquared() < 1e-12f) continue;
                n = Vector3.Normalize(n);
                float shade = 0.3f + 0.7f * MathF.Abs(Vector3.Dot(n, light));
                var col = new Rgba((byte)(t.Col.R * shade), (byte)(t.Col.G * shade), (byte)(t.Col.B * shade), 255);
                int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(pa.X, MathF.Min(pb.X, pc.X)))), x1 = Math.Min(PW - 1, (int)MathF.Ceiling(MathF.Max(pa.X, MathF.Max(pb.X, pc.X))));
                int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(pa.Y, MathF.Min(pb.Y, pc.Y)))), y1 = Math.Min(PH - 1, (int)MathF.Ceiling(MathF.Max(pa.Y, MathF.Max(pb.Y, pc.Y))));
                if (x1 - x0 > 4 * PW || y1 - y0 > 4 * PH) continue;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float w0 = ((pb.X - px) * (pc.Y - py) - (pb.Y - py) * (pc.X - px)) / area;
                        float w1 = ((pc.X - px) * (pa.Y - py) - (pc.Y - py) * (pa.X - px)) / area;
                        float w2 = 1 - w0 - w1;
                        if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                        float z = 1 / (w0 / a.Z + w1 / b.Z + w2 / c.Z);
                        int k = y * PW + x;
                        if (z >= r.Depth[k]) continue;
                        r.Depth[k] = z; r.Img.Pixels[k] = col;
                    }
            }
        }
        void Line3(Vector3 a3, Vector3 b3, Rgba col, int thick, bool depthTest)
        {
            var a = V(a3); var b = V(b3);
            if (a.Z < 0.05f || b.Z < 0.05f) return;
            var pa = S(a); var pb = S(b);
            int n = (int)MathF.Max(MathF.Abs(pb.X - pa.X), MathF.Abs(pb.Y - pa.Y)) + 1;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                float x = pa.X + (pb.X - pa.X) * t, y = pa.Y + (pb.Y - pa.Y) * t;
                float z = 1 / ((1 - t) / a.Z + t / b.Z);
                for (int dx = -thick / 2; dx <= thick / 2; dx++)
                    for (int dy = -thick / 2; dy <= thick / 2; dy++)
                    {
                        int xi = (int)x + dx, yi = (int)y + dy;
                        if (xi < 0 || yi < 0 || xi >= PW || yi >= PH) continue;
                        int k = yi * PW + xi;
                        if (depthTest && z - 0.04f >= r.Depth[k]) continue;
                        r.Img.Pixels[k] = col;
                    }
            }
        }
        void Skeleton(MorphemePose ps, V3[] pp, Matrix4x4 w, Vector3 shift, Rgba col, int thick, bool depthTest)
        {
            foreach (var (ja, jb) in Limbs)
            {
                int ia = ps.Index(ja), ib = ps.Index(jb);
                if (ia < 0 || ib < 0) continue;
                Line3(Vector3.Transform(ToFlver(pp[ia]), w) + shift, Vector3.Transform(ToFlver(pp[ib]), w) + shift, col, thick, depthTest);
            }
        }
        DrawTris(geo);
        if (body != null) DrawTris(body);
        if (ghost is { } g) Skeleton(g.Pose, g.P, g.World, g.Shift, new Rgba(140, 140, 140, 255), 1, false);
        Skeleton(pose, P, world, Vector3.Zero, boneCol, body == null ? 5 : 1, body == null);
        // the root (MSB / generator position): red cross
        var root = Vector3.Transform(Vector3.Zero, world);
        Line3(root - new Vector3(0.15f, 0, 0), root + new Vector3(0.15f, 0, 0), new Rgba(255, 60, 60, 255), 1, false);
        Line3(root - new Vector3(0, 0, 0.15f), root + new Vector3(0, 0, 0.15f), new Rgba(255, 60, 60, 255), 1, false);
        return r.Img;
    }

    static void Blit(Image dst, Image src, int ox, int oy)
    {
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                dst.Pixels[(oy + y) * dst.Width + ox + x] = src.Pixels[y * src.Width + x];
        // panel border
        for (int x = 0; x < src.Width; x++) { dst.Pixels[oy * dst.Width + ox + x] = new Rgba(70, 70, 80, 255); }
        for (int y = 0; y < src.Height; y++) { dst.Pixels[(oy + y) * dst.Width + ox] = new Rgba(70, 70, 80, 255); }
    }
}
// pose-scene: offline evidence renderer (npcpose)
