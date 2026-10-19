using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Survey;

/// <summary>
/// Reads one representative sample of every DSR format the port needs and prints
/// its version/shape, so each converter knows exactly what it starts from.
/// </summary>
public static class DsrSurvey
{
    public static void Run(string root)
    {
        Console.WriteLine($"# DSR format survey\nroot: {root}\n");
        Section("Params", () => Params(root));
        Section("Map: Firelink Shrine (m10_02_00_00)", () => Map(root, "m10_02_00_00", "m10"));
        Section("Character: Hollow (c2800)", () => Character(root, "c2800"));
        Section("Character: Taurus Demon (c2250)", () => Character(root, "c2250"));
        Section("Equipment parts", () => Parts(root));
        Section("Events", () => Events(root));
        Section("Scripts", () => Scripts(root));
        Section("Sound", () => Sound(root));
    }

    static void Section(string title, Action body)
    {
        Console.WriteLine($"## {title}");
        try { body(); }
        catch (Exception e) { Console.WriteLine($"  ERROR {e.GetType().Name}: {e.Message}"); }
        Console.WriteLine();
    }

    static void Params(string root)
    {
        var defs = BND3.Read(Path.Combine(root, @"paramdef\paramdef.paramdefbnd.dcx"))
            .Files.Select(f => PARAMDEF.Read(f.Bytes)).ToList();
        Console.WriteLine($"  paramdefs shipped: {defs.Count}");

        var bnd = BND3.Read(Path.Combine(root, @"param\GameParam\GameParam.parambnd.dcx"));
        foreach (var file in bnd.Files)
        {
            var param = PARAM.Read(file.Bytes);
            bool ok = param.ApplyParamdefCarefully(defs);
            Console.WriteLine($"  {Path.GetFileNameWithoutExtension(file.Name),-28} type={param.ParamType,-28} rows={param.Rows.Count,5} def={(ok ? "ok" : "MISSING")}");
        }
    }

    static void Map(string root, string mapId, string area)
    {
        string dir = Path.Combine(root, "map", mapId);
        foreach (var g in Directory.GetFiles(dir).GroupBy(Ext).OrderBy(g => g.Key))
            Console.WriteLine($"  {g.Count(),4} x {g.Key}");

        string piece = Directory.GetFiles(dir, "*.flver*").First();
        Flver(Path.GetFileName(piece), FLVER2.Read(piece));

        var msb = MSB1.Read(Path.Combine(root, @"map\MapStudio", mapId + ".msb"));
        var p = msb.Parts;
        Console.WriteLine($"  MSB1: mapPieces={p.MapPieces.Count} objects={p.Objects.Count} enemies={p.Enemies.Count} players={p.Players.Count} " +
                          $"collisions={p.Collisions.Count} navmeshes={p.Navmeshes.Count} connectCollisions={p.ConnectCollisions.Count}");
        Console.WriteLine($"        regions={msb.Regions.GetEntries().Count} events={msb.Events.GetEntries().Count} models={msb.Models.GetEntries().Count}");

        string? hkxbhd = Directory.GetFiles(dir, "h*.hkxbhd").FirstOrDefault();
        if (hkxbhd != null)
        {
            var bxf = BXF3.Read(hkxbhd, Path.ChangeExtension(hkxbhd, ".hkxbdt"));
            var first = bxf.Files[0];
            Console.WriteLine($"  collision {Path.GetFileName(hkxbhd)}: {bxf.Files.Count} hkx; {first.Name}: {HkxInfo.Describe(Decompress(first.Bytes))}");
        }

        string areaDir = Path.Combine(root, "map", area);
        foreach (string bhd in Directory.GetFiles(areaDir, "*.tpfbhd").Take(1))
        {
            var bxf = BXF3.Read(bhd, Path.ChangeExtension(bhd, ".tpfbdt"));
            var tpf = TPF.Read(bxf.Files[0].Bytes);
            Console.WriteLine($"  textures {Path.GetFileName(bhd)}: {bxf.Files.Count} tpfs; first: platform={tpf.Platform} formats={string.Join(",", tpf.Textures.Select(t => t.Format).Distinct())}");
        }
    }

    static void Character(string root, string chrId)
    {
        var chrbnd = BND3.Read(Path.Combine(root, "chr", chrId + ".chrbnd.dcx"));
        Console.WriteLine($"  chrbnd: {string.Join(", ", chrbnd.Files.Select(f => Path.GetFileName(f.Name)))}");
        foreach (var f in chrbnd.Files)
        {
            if (f.Name.EndsWith(".flver")) Flver(Path.GetFileName(f.Name), FLVER2.Read(f.Bytes));
            else if (f.Name.EndsWith(".hkx")) Console.WriteLine($"  {Path.GetFileName(f.Name)}: {HkxInfo.Describe(f.Bytes)}");
            else if (f.Name.EndsWith(".tpf"))
            {
                var tpf = TPF.Read(f.Bytes);
                Console.WriteLine($"  {Path.GetFileName(f.Name)}: {tpf.Textures.Count} textures, formats={string.Join(",", tpf.Textures.Select(t => t.Format).Distinct())}");
            }
        }

        var anibnd = BND3.Read(Path.Combine(root, "chr", chrId + ".anibnd.dcx"));
        var anims = anibnd.Files.Where(f => f.Name.EndsWith(".hkx")).ToList();
        var taes = anibnd.Files.Where(f => f.Name.EndsWith(".tae")).ToList();
        Console.WriteLine($"  anibnd: {anims.Count} hkx, {taes.Count} tae, other: {string.Join(", ", anibnd.Files.Except(anims).Except(taes).Select(f => Path.GetFileName(f.Name)))}");
        if (anims.Count > 0) Console.WriteLine($"  first anim {Path.GetFileName(anims[0].Name)}: {HkxInfo.Describe(anims[0].Bytes)}");
        foreach (var t in taes.Take(1))
        {
            var tae = TAE.Read(t.Bytes);
            int events = tae.Animations.Sum(a => a.Events.Count);
            var types = tae.Animations.SelectMany(a => a.Events).Select(e => e.Type).Distinct().Order().ToList();
            Console.WriteLine($"  {Path.GetFileName(t.Name)}: format={tae.Format} anims={tae.Animations.Count} events={events} eventTypes={types.Count} [{string.Join(",", types)}]");
        }
    }

    static void Parts(string root)
    {
        string dir = Path.Combine(root, "parts");
        foreach (var g in Directory.GetFiles(dir).GroupBy(f => Path.GetFileName(f).Split('_')[0]).OrderBy(g => g.Key))
            Console.WriteLine($"  {g.Count(),4} x {g.Key}_*");
        foreach (string prefix in new[] { "WP_A_02", "BD_M_10" })
        {
            string? file = Directory.GetFiles(dir, prefix + "*.partsbnd*").FirstOrDefault();
            if (file == null) continue;
            var bnd = BND3.Read(file);
            var flver = bnd.Files.FirstOrDefault(f => f.Name.EndsWith(".flver"));
            if (flver != null) Flver(Path.GetFileName(file), FLVER2.Read(flver.Bytes));
        }
    }

    static void Events(string root)
    {
        var files = Directory.GetFiles(Path.Combine(root, "event"), "*.emevd*");
        Console.WriteLine($"  {files.Length} emevd files");
        var evd = EMEVD.Read(files.First(f => f.Contains("m10_02_00_00")));
        Console.WriteLine($"  m10_02_00_00: format={evd.Format} events={evd.Events.Count} instructions={evd.Events.Sum(e => e.Instructions.Count)}");
    }

    static void Scripts(string root)
    {
        foreach (var g in Directory.GetFiles(Path.Combine(root, "script"), "*", SearchOption.AllDirectories).GroupBy(Ext).OrderBy(g => g.Key))
            Console.WriteLine($"  {g.Count(),4} x {g.Key}");
    }

    static void Sound(string root)
    {
        foreach (var g in Directory.GetFiles(Path.Combine(root, "sound"), "*", SearchOption.AllDirectories).GroupBy(Ext).OrderBy(g => g.Key))
            Console.WriteLine($"  {g.Count(),4} x {g.Key}");
    }

    static void Flver(string name, FLVER2 f)
    {
        var mtds = f.Materials.Select(m => Path.GetFileName(m.MTD)).Distinct().ToList();
        Console.WriteLine($"  {name}: FLVER 0x{f.Header.Version:X} meshes={f.Meshes.Count} verts={f.Meshes.Sum(m => m.Vertices.Count)} " +
                          $"nodes={f.Nodes.Count} layouts={f.BufferLayouts.Count} mtds={mtds.Count} [{string.Join(", ", mtds.Take(4))}{(mtds.Count > 4 ? ", ..." : "")}]");
    }

    static byte[] Decompress(byte[] bytes) => DCX.Is(bytes) ? DCX.Decompress(bytes) : bytes;

    static string Ext(string path)
    {
        string name = Path.GetFileName(path);
        int dot = name.IndexOf('.');
        return dot < 0 ? "(none)" : name[dot..];
    }
}
