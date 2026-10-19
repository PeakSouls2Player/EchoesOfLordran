using System.Numerics;
using System.Text;
using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Survey;

/// <summary>
/// survey-map dsr|ds2 MAPID [--parts OUT.tsv] — everything a map is made of on one side: MSB contents, map piece models
/// (materials, textures, vertex layouts), texture archives, collision (Havok container + classes), navmesh.
/// </summary>
public static class MapSurvey
{
    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: survey-map dsr|ds2 MAPID [--parts OUT.tsv]"); return 1; }
        string? partsOut = args.SkipWhile(a => a != "--parts").Skip(1).FirstOrDefault();
        if (args[1] == "dsr") Dsr(Program.DefaultDsrRoot, args[2], partsOut);
        else Ds2(Commands.ExtractDs2.DefaultOut, args[2], partsOut);
        return 0;
    }

    static byte[] Dcx(byte[] b) => DCX.Is(b) ? DCX.Decompress(b) : b;

    static string V(Vector3 v) => $"{v.X:0.##},{v.Y:0.##},{v.Z:0.##}";

    // ---------------------------------------------------------------- DSR
    static void Dsr(string root, string mapId, string? partsOut)
    {
        string area = mapId.Substring(0, 3);
        Console.WriteLine($"# DSR map {mapId}");
        foreach (string dir in new[] { "MapStudio", "MapStudioNew" })
        {
            string path = Path.Combine(root, "map", dir, mapId + ".msb");
            if (File.Exists(path)) Console.WriteLine($"  {dir}\\{mapId}.msb {new FileInfo(path).Length:N0} bytes {File.GetLastWriteTime(path):yyyy-MM-dd}");
        }
        var msb = MSB1.Read(Path.Combine(root, "map", "MapStudio", mapId + ".msb"));
        var p = msb.Parts;
        Console.WriteLine($"  parts: mapPieces={p.MapPieces.Count} objects={p.Objects.Count} enemies={p.Enemies.Count} players={p.Players.Count} collisions={p.Collisions.Count} navmeshes={p.Navmeshes.Count} dummyObjects={p.DummyObjects.Count} dummyEnemies={p.DummyEnemies.Count} connectCollisions={p.ConnectCollisions.Count}");
        Console.WriteLine($"  regions={msb.Regions.GetEntries().Count} events={msb.Events.GetEntries().Count}");
        foreach (var g in msb.Models.GetEntries().GroupBy(m => m.GetType().Name))
            Console.WriteLine($"  models {g.Key}: {g.Count()} — {string.Join(" ", g.Select(m => m.Name).Take(60))}");
        foreach (var g in msb.Events.GetEntries().GroupBy(e => e.GetType().Name))
            Console.WriteLine($"  events {g.Key}: {g.Count()}");
        foreach (var g in msb.Regions.GetEntries().GroupBy(e => e.Shape.GetType().Name))
            Console.WriteLine($"  regions shape {g.Key}: {g.Count()}");

        var box = Bounds(msb.Parts.MapPieces.Select(m => m.Position));
        Console.WriteLine($"  map piece positions: min {V(box.Min)} max {V(box.Max)}");
        foreach (var e in p.Enemies)
            Console.WriteLine($"  enemy {e.Name,-12} model={e.ModelName} npc={e.NPCParamID} think={e.ThinkParamID} talk={e.TalkID} init={e.CharaInitID} entity={e.EntityID} pos={V(e.Position)} rot={V(e.Rotation)} col={e.CollisionName} anim={e.InitAnimID}");
        foreach (var o in p.Objects)
            Console.WriteLine($"  object {o.Name,-14} model={o.ModelName} entity={o.EntityID} pos={V(o.Position)} col={o.CollisionName} initAnim={o.InitAnimID}");
        foreach (var pl in p.Players)
            Console.WriteLine($"  player {pl.Name} entity={pl.EntityID} pos={V(pl.Position)} rot={V(pl.Rotation)}");
        foreach (var c in p.Collisions.Take(8))
            Console.WriteLine($"  collision {c.Name} model={c.ModelName} pos={V(c.Position)} hitFilter={c.HitFilterID} sound={c.SoundSpaceType} mapName={c.MapNameID} playRegion={c.PlayRegionID} lockCam={c.LockCamParamID1}/{c.LockCamParamID2}");
        if (partsOut != null) WritePartsTsv(partsOut, msb);

        string mapDir = Path.Combine(root, "map", mapId);
        var flvers = Directory.GetFiles(mapDir, "*.flver.dcx");
        var mtds = new Dictionary<string, int>();
        var layouts = new Dictionary<string, int>();
        var textures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long verts = 0, faces = 0;
        foreach (string f in flvers)
        {
            var flver = FLVER2.Read(Dcx(File.ReadAllBytes(f)));
            foreach (var m in flver.Materials)
            {
                mtds[Path.GetFileName(m.MTD)] = mtds.GetValueOrDefault(Path.GetFileName(m.MTD)) + 1;
                foreach (var t in m.Textures) if (!string.IsNullOrEmpty(t.Path)) textures.Add(Path.GetFileNameWithoutExtension(t.Path));
            }
            foreach (var l in flver.BufferLayouts)
            {
                string key = string.Join(" ", l.Select(lm => $"{lm.Semantic}{lm.Index}:{lm.Type}"));
                layouts[key] = layouts.GetValueOrDefault(key) + 1;
            }
            verts += flver.Meshes.Sum(m => m.Vertices.Count);
            faces += flver.Meshes.Sum(m => m.FaceSets.Count == 0 ? 0 : m.FaceSets[0].Indices.Count / 3);
        }
        Console.WriteLine($"  {flvers.Length} map piece FLVERs, {verts:N0} vertices, ~{faces:N0} triangles (first faceset, triangle-list estimate)");
        var sample = FLVER2.Read(Dcx(File.ReadAllBytes(flvers[0])));
        Console.WriteLine($"  sample {Path.GetFileName(flvers[0])}: version 0x{sample.Header.Version:X} unicode={sample.Header.Unicode} bbox {V(sample.Header.BoundingBoxMin)}..{V(sample.Header.BoundingBoxMax)}");
        foreach (var m in sample.Materials)
        {
            Console.WriteLine($"    material '{m.Name}' {m.MTD}");
            foreach (var t in m.Textures) Console.WriteLine($"      {t.ParamName,-24} {t.Path}");
        }
        foreach (var mesh in sample.Meshes)
            Console.WriteLine($"    mesh mat={mesh.MaterialIndex} verts={mesh.Vertices.Count} facesets={string.Join(",", mesh.FaceSets.Select(fs => $"{fs.Flags}/{(fs.TriangleStrip ? "strip" : "list")}/{fs.Indices.Count}"))} node={mesh.NodeIndex} bones={mesh.BoneIndices.Count}");
        Console.WriteLine("  MTDs: " + string.Join(", ", mtds.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}×{kv.Value}")));
        foreach (var (k, n) in layouts.OrderByDescending(kv => kv.Value)) Console.WriteLine($"  layout ×{n}: {k}");
        Console.WriteLine($"  {textures.Count} distinct texture names referenced");

        // Texture archives for the area: mAA_XXXX.tpfbhd (+ GI_EnvM), and loose map/tx.
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string bhd in Directory.GetFiles(Path.Combine(root, "map", area), "*.tpfbhd"))
        {
            var bxf = BXF3.Read(bhd, Path.ChangeExtension(bhd, ".tpfbdt"));
            var formats = new Dictionary<byte, int>();
            int hits = 0;
            foreach (var f in bxf.Files)
            {
                var tpf = TPF.Read(Dcx(f.Bytes));
                foreach (var t in tpf.Textures)
                {
                    formats[t.Format] = formats.GetValueOrDefault(t.Format) + 1;
                    if (textures.Contains(t.Name)) { found.Add(t.Name); hits++; }
                }
            }
            Console.WriteLine($"  {Path.GetFileName(bhd)}: {bxf.Files.Count} tpfs, formats {string.Join(" ", formats.Select(kv => $"{kv.Key}×{kv.Value}"))}, {hits} used by {mapId}");
        }
        var missing = textures.Where(t => !found.Contains(t)).ToList();
        Console.WriteLine($"  textures not in the area archives: {missing.Count} ({string.Join(" ", missing.Take(25))})");

        foreach (string bhd in Directory.GetFiles(mapDir, "*.hkxbhd"))
        {
            var bxf = BXF3.Read(bhd, Path.ChangeExtension(bhd, ".hkxbdt"));
            Console.WriteLine($"  {Path.GetFileName(bhd)}: {bxf.Files.Count} files, e.g. {bxf.Files[0].Name}: {HkxInfo.Describe(Dcx(bxf.Files[0].Bytes))}");
        }
        var nvmbnd = BND3.Read(Path.Combine(mapDir, mapId + ".nvmbnd.dcx"));
        int tris = 0;
        foreach (var f in nvmbnd.Files) tris += NVM.Read(f.Bytes).Triangles.Count;
        Console.WriteLine($"  nvmbnd: {nvmbnd.Files.Count} NVMs, {tris:N0} triangles; e.g. {nvmbnd.Files[0].Name}");
        var mcg = MCG.Read(Path.Combine(mapDir, mapId + ".mcg"));
        var mcp = MCP.Read(Path.Combine(mapDir, mapId + ".mcp"));
        Console.WriteLine($"  MCG: nodes={mcg.Nodes.Count} edges={mcg.Edges.Count}; MCP: rooms={mcp.Rooms.Count}");
        foreach (string other in Directory.GetFiles(mapDir).Where(f => !f.EndsWith(".flver.dcx")))
            Console.WriteLine($"  file {Path.GetFileName(other)} {new FileInfo(other).Length:N0}");
    }

    static void WritePartsTsv(string path, MSB1 msb)
    {
        var sb = new StringBuilder("type\tname\tmodel\tpos\trot\tscale\tentity\textra\n");
        foreach (var part in msb.Parts.GetEntries())
        {
            string extra = part switch
            {
                MSB1.Part.EnemyBase e => $"npc={e.NPCParamID} think={e.ThinkParamID} talk={e.TalkID} init={e.CharaInitID} col={e.CollisionName}",
                MSB1.Part.ObjectBase o => $"col={o.CollisionName} initAnim={o.InitAnimID}",
                MSB1.Part.Collision c => $"hitFilter={c.HitFilterID} sound={c.SoundSpaceType} mapName={c.MapNameID} playRegion={c.PlayRegionID}",
                MSB1.Part.ConnectCollision cc => $"col={cc.CollisionName} map={string.Join("_", cc.MapID)}",
                _ => ""
            };
            sb.Append($"{part.GetType().Name}\t{part.Name}\t{part.ModelName}\t{V(part.Position)}\t{V(part.Rotation)}\t{V(part.Scale)}\t{part.EntityID}\t{extra}\n");
        }
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine($"  parts written to {path}");
    }

    static (Vector3 Min, Vector3 Max) Bounds(IEnumerable<Vector3> points)
    {
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        foreach (var v in points) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
        return (min, max);
    }

    // ---------------------------------------------------------------- DS2
    static void Ds2(string data, string mapId, string? partsOut)
    {
        string tail = mapId.Substring(1);
        Console.WriteLine($"# DS2 map {mapId}");
        var msb = MSB2.Read(Path.Combine(data, "map", mapId, mapId + ".msb"));
        var p = msb.Parts;
        Console.WriteLine($"  parts: mapPieces={p.MapPieces.Count} objects={p.Objects.Count} collisions={p.Collisions.Count} navmeshes={p.Navmeshes.Count} connectCollisions={p.ConnectCollisions.Count}");
        foreach (var g in msb.Models.GetEntries().GroupBy(m => m.GetType().Name))
            Console.WriteLine($"  models {g.Key}: {g.Count()} — {string.Join(" ", g.Select(m => m.Name).Take(40))}");
        foreach (var g in msb.Events.GetEntries().GroupBy(e => e.GetType().Name))
            Console.WriteLine($"  events {g.Key}: {g.Count()}");
        foreach (var g in msb.Regions.GetEntries().GroupBy(e => e.GetType().Name))
            Console.WriteLine($"  regions {g.Key}: {g.Count()}");
        var box = Bounds(p.MapPieces.Select(m => m.Position));
        Console.WriteLine($"  map piece positions: min {V(box.Min)} max {V(box.Max)}");
        foreach (var m in p.MapPieces.Take(5))
            Console.WriteLine($"  mappiece {m.Name} model={m.ModelName} pos={V(m.Position)} rot={V(m.Rotation)} scale={V(m.Scale)} draw={string.Join(",", m.DrawGroups.Select(x => x.ToString("X8")))} disp={string.Join(",", m.DispGroups.Select(x => x.ToString("X8")))} u44={m.Unk44} u64={m.Unk64} u6c={m.Unk6C} u6e={m.Unk6E} t00={m.UnkT00} t02={m.UnkT02}");
        foreach (var o in p.Objects.Take(5))
            Console.WriteLine($"  object {o.Name} model={o.ModelName} pos={V(o.Position)} instParam={o.MapObjectInstanceParamID} t04={o.UnkT04}");
        foreach (var c in p.Collisions.Take(5))
            Console.WriteLine($"  collision {c.Name} model={c.ModelName} pos={V(c.Position)} t00={c.UnkT00} t04={c.UnkT04} t08={c.UnkT08} t0c={c.UnkT0C} t10..17={c.UnkT10},{c.UnkT11},{c.UnkT12},{c.UnkT13},{c.UnkT14},{c.UnkT15},{c.UnkT17} t18={c.UnkT18} t1c={c.UnkT1C} t20={c.UnkT20} t26={c.UnkT26} t27={c.UnkT27} t28={c.UnkT28} t2c={c.UnkT2C} t2e={c.UnkT2E} t30={c.UnkT30} t35={c.UnkT35} t36={c.UnkT36} t3c={c.UnkT3C} t40={c.UnkT40} t44={c.UnkT44}");
        foreach (var n in p.Navmeshes.Take(3))
            Console.WriteLine($"  navmesh {n.Name} model={n.ModelName} pos={V(n.Position)} t00={n.UnkT00} t04={n.UnkT04} t08={n.UnkT08} t0c={n.UnkT0C}");
        foreach (var cc in p.ConnectCollisions)
            Console.WriteLine($"  connect {cc.Name} col={cc.CollisionName} map={string.Join("_", cc.MapID)} t08={cc.UnkT08} t0c={cc.UnkT0C}");
        if (partsOut != null)
        {
            var sb = new StringBuilder("type\tname\tmodel\tpos\trot\tscale\n");
            foreach (var part in p.GetEntries()) sb.Append($"{part.GetType().Name}\t{part.Name}\t{part.ModelName}\t{V(part.Position)}\t{V(part.Rotation)}\t{V(part.Scale)}\n");
            File.WriteAllText(partsOut, sb.ToString());
        }

        string mdir = Path.Combine(data, "model", "map");
        var map = BXF4.Read(Path.Combine(mdir, mapId + ".mapbhd"), Path.Combine(mdir, mapId + ".mapbdt"));
        Console.WriteLine($"  {mapId}.mapbhd: {map.Files.Count} files; first: {string.Join(" ", map.Files.Take(6).Select(f => $"[{f.ID}]{f.Name}"))}");
        var mtds = new Dictionary<string, int>();
        var layouts = new Dictionary<string, int>();
        foreach (var f in map.Files.Where(f => f.Name.Contains(".flv")))
        {
            var flver = FLVER2.Read(Dcx(f.Bytes));
            foreach (var m in flver.Materials) mtds[Path.GetFileName(m.MTD)] = mtds.GetValueOrDefault(Path.GetFileName(m.MTD)) + 1;
            foreach (var l in flver.BufferLayouts)
            {
                string key = string.Join(" ", l.Select(lm => $"{lm.Semantic}{lm.Index}:{lm.Type}"));
                layouts[key] = layouts.GetValueOrDefault(key) + 1;
            }
        }
        var flvFile = map.Files.First(f => f.Name.Contains(".flv"));
        var s = FLVER2.Read(Dcx(flvFile.Bytes));
        Console.WriteLine($"  sample {flvFile.Name}: version 0x{s.Header.Version:X} unicode={s.Header.Unicode} bbox {V(s.Header.BoundingBoxMin)}..{V(s.Header.BoundingBoxMax)} nodes={s.Nodes.Count}");
        foreach (var m in s.Materials)
        {
            Console.WriteLine($"    material '{m.Name}' {m.MTD} gx={m.GXIndex}");
            foreach (var t in m.Textures) Console.WriteLine($"      {t.ParamName,-24} {t.Path}");
        }
        foreach (var mesh in s.Meshes)
            Console.WriteLine($"    mesh mat={mesh.MaterialIndex} verts={mesh.Vertices.Count} facesets={string.Join(",", mesh.FaceSets.Select(fs => $"{fs.Flags}/{(fs.TriangleStrip ? "strip" : "list")}/{fs.Indices.Count}"))} node={mesh.NodeIndex} bones={mesh.BoneIndices.Count}");
        foreach (var g in s.GXLists.Select((l, i) => (l, i)))
            Console.WriteLine($"    gxlist[{g.i}]: {string.Join(" ", g.l.Select(it => $"{it.ID}/{it.Unk04}/{it.Data.Length}"))}");
        Console.WriteLine("  MTDs: " + string.Join(", ", mtds.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}×{kv.Value}")));
        foreach (var (k, n) in layouts.OrderByDescending(kv => kv.Value)) Console.WriteLine($"  layout ×{n}: {k}");
        var mte = map.Files.FirstOrDefault(f => f.Name.Contains(".mte"));
        if (mte != null)
        {
            byte[] mb = Dcx(mte.Bytes);
            Console.WriteLine($"  sample {mte.Name}: {mb.Length} bytes, magic '{Encoding.ASCII.GetString(mb, 0, 4)}'");
            Console.WriteLine(Hex(mb, 0, Math.Min(mb.Length, 256)));
        }

        foreach (string kind in new[] { "t", "h", "l", "g" })
        {
            string bhd = Directory.GetFiles(mdir, $"{kind}{tail}.*bhd").FirstOrDefault() ?? "";
            if (bhd == "") continue;
            string bdt = bhd.Substring(0, bhd.Length - 3) + "bdt";
            var bxf = BXF4.Read(bhd, bdt);
            Console.WriteLine($"  {Path.GetFileName(bhd)}: {bxf.Files.Count} files; first: {string.Join(" ", bxf.Files.Take(4).Select(f => $"[{f.ID}]{f.Name}"))}");
            var first = Dcx(bxf.Files[0].Bytes);
            if (kind == "t")
            {
                var formats = new Dictionary<byte, int>();
                foreach (var f in bxf.Files) foreach (var t in TPF.Read(Dcx(f.Bytes)).Textures) formats[t.Format] = formats.GetValueOrDefault(t.Format) + 1;
                Console.WriteLine($"    texture formats: {string.Join(" ", formats.Select(kv => $"{kv.Key}×{kv.Value}"))}");
                var tpf = TPF.Read(first);
                foreach (var t in tpf.Textures.Take(3)) Console.WriteLine($"    '{t.Name}' format={t.Format} type={t.Type} mips={t.Mipmaps} flags1={t.Flags1} platform={tpf.Platform} enc={tpf.Encoding} flag2={tpf.Flag2}");
            }
            else if (kind is "h" or "l")
            {
                Console.WriteLine($"    {HkxInfo.Describe(first)}");
                Console.WriteLine($"    classes: {string.Join(", ", HkxPackfileClasses(first))}");
            }
            else Console.WriteLine($"    first file {first.Length} bytes magic '{Encoding.ASCII.GetString(first, 0, 4)}'");
        }
        string env = Path.Combine(mdir, $"e{tail}.envbnd.dcx");
        if (File.Exists(env))
        {
            var bnd = BND4.Read(env);
            Console.WriteLine($"  e{tail}.envbnd.dcx: {string.Join(" ", bnd.Files.Select(f => $"{f.Name}({f.Bytes.Length})"))}");
        }
        var ngp = NGP.Read(Path.Combine(data, "map", mapId, mapId + ".ngp"));
        Console.WriteLine($"  NGP {ngp.Version}: meshes={ngp.Meshes.Count} A={ngp.StructAs.Count} B={ngp.StructBs.Count} C={ngp.StructCs.Count} D={ngp.StructDs.Count} unk1C={ngp.Unk1C}");
    }

    /// <summary>Class names listed in a Havok packfile's __classnames__ section.</summary>
    public static List<string> HkxPackfileClasses(byte[] b)
    {
        var names = new List<string>();
        if (BitConverter.ToUInt32(b, 0) != 0x57E0E057) return names;
        int sectionCount = BitConverter.ToInt32(b, 0x14);
        int fileVersion = BitConverter.ToInt32(b, 0x0C);
        int headerSize = 0x40;
        if (fileVersion >= 11) headerSize += 0x10 + BitConverter.ToInt16(b, 0x3E);
        int sectionSize = fileVersion >= 11 ? 0x40 : 0x30;
        for (int i = 0; i < sectionCount; i++)
        {
            int sh = headerSize + i * sectionSize;
            string name = Encoding.ASCII.GetString(b, sh, 19).TrimEnd('\0', '\xff');
            if (name != "__classnames__") continue;
            int start = BitConverter.ToInt32(b, sh + 0x14);
            int end = BitConverter.ToInt32(b, sh + 0x18); // local fixups offset = end of data for this section
            int pos = start;
            while (pos + 5 < start + end)
            {
                uint sig = BitConverter.ToUInt32(b, pos);
                if (b[pos + 4] != 0x09) break;
                int s = pos + 5, e = s;
                while (b[e] != 0) e++;
                names.Add(Encoding.ASCII.GetString(b, s, e - s) + $"#{sig:X8}");
                pos = e + 1;
            }
        }
        return names;
    }

    static string Hex(byte[] b, int start, int len)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < len; i += 16)
        {
            sb.Append($"    {start + i:X4}: ");
            for (int j = 0; j < 16 && i + j < len; j++) sb.Append($"{b[start + i + j]:X2} ");
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }
}
