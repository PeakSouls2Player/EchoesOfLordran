using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// pack-model PACKDIR MAP MODEL... — session 8 (track nests): a converted map-piece model in a built pack
/// (gamedata\model\map\MAP.mapbhd/bdt): its meshes (vertex/face counts, bounds), materials (MTD, texture paths) and for
/// every texture whether the pack's texture archive (tMAP.tpfbhd/bdt) or GI archive (gMAP.gibhd/bdt) holds it, plus the
/// MSB2 parts that place the model (position, rotation, scale). Offline check that a DS1 piece (e.g. Firelink's nest
/// m2023B2 → m2023, Snuggly's nest o8550_0001 → m8550) is in the map with its textures.
/// </summary>
public static class PackModel
{
    public static int Run(string[] args)
    {
        if (args.Length < 4) { Console.Error.WriteLine("usage: pack-model PACKDIR MAP MODEL..."); return 1; }
        string pack = args[1], map = args[2];
        string dir = Path.Combine(pack, "gamedata", "model", "map");
        var mapBxf = BXF4.Read(Path.Combine(dir, map + ".mapbhd"), Path.Combine(dir, map + ".mapbdt"));
        string tail = map[1..];
        var texNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (bhd, bdt) in new[] { ($"t{tail}.tpfbhd", $"t{tail}.tpfbdt"), ($"g{tail}.gibhd", $"g{tail}.gibdt") })
        {
            if (!File.Exists(Path.Combine(dir, bhd))) continue;
            foreach (var f in BXF4.Read(Path.Combine(dir, bhd), Path.Combine(dir, bdt)).Files)
            {
                string n = Path.GetFileName(f.Name);
                texNames.Add(n[..n.IndexOf('.')]);
            }
        }
        var msb = MSB2.Read(Path.Combine(pack, "gamedata", "map", map, map + ".msb"));
        int problems = 0;
        foreach (string model in args.Skip(3))
        {
            var file = mapBxf.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith(model + ".flv", StringComparison.OrdinalIgnoreCase));
            if (file == null) { Console.WriteLine($"{model}: NOT in {map}.mapbdt"); problems++; continue; }
            var flv = FLVER2.Read(DCX.Is(file.Bytes) ? DCX.Decompress(file.Bytes) : file.Bytes);
            Console.WriteLine($"{model}: {flv.Meshes.Count} meshes, {flv.Materials.Count} materials, bounds {flv.Header.BoundingBoxMin} .. {flv.Header.BoundingBoxMax}");
            for (int i = 0; i < flv.Meshes.Count; i++)
            {
                var mesh = flv.Meshes[i];
                var mat = flv.Materials[mesh.MaterialIndex];
                int faces = mesh.FaceSets.FirstOrDefault(fs => fs.Flags == FLVER2.FaceSet.FSFlags.None)?.Triangulate(true).Count / 3 ?? 0;
                var tex = mat.Textures.Where(t => !string.IsNullOrEmpty(t.Path)).Select(t =>
                {
                    string n = Path.GetFileNameWithoutExtension(t.Path);
                    bool ok = texNames.Contains(n) || texNames.Contains("hq_" + n);
                    if (!ok) problems++;
                    return $"{t.ParamName}={n}{(ok ? "" : " MISSING")}";
                });
                Console.WriteLine($"  mesh {i}: {mesh.Vertices.Count} verts, {faces} faces, material '{mat.Name}' {Path.GetFileName(mat.MTD)}: {string.Join(", ", tex)}");
            }
            foreach (var p in msb.Parts.MapPieces.Where(p => p.ModelName.Equals(model, StringComparison.OrdinalIgnoreCase)))
                Console.WriteLine($"  part {p.Name}: position {p.Position} rotation {p.Rotation} scale {p.Scale}");
            if (!msb.Parts.MapPieces.Any(p => p.ModelName.Equals(model, StringComparison.OrdinalIgnoreCase))) { Console.WriteLine("  NO MSB part places it"); problems++; }
        }
        Console.WriteLine($"pack-model {map}: {problems} problem(s)");
        return problems == 0 ? 0 : 2;
    }
}
