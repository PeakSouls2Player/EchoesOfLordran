using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// bad-verts DS1MAP — map piece meshes with non-finite or absurd (|coord| > 1e5) vertex positions (DS1 effect meshes,
/// e.g. flies/"Division", keep packed data there). Such vertices poison a computed bounding box.
/// </summary>
public static class BadVerts
{
    public static int Run(string[] args)
    {
        string map = args[1], area = map.Substring(1, 2);
        var msb = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", map + ".msb"));
        foreach (var model in msb.Models.MapPieces)
        {
            string f = Path.Combine(Program.DefaultDsrRoot, "map", map, $"{model.Name}A{area}.flver.dcx");
            if (!File.Exists(f)) continue;
            var flver = FLVER2.Read(DCX.Decompress(File.ReadAllBytes(f)));
            for (int i = 0; i < flver.Meshes.Count; i++)
            {
                var mesh = flver.Meshes[i];
                int bad = mesh.Vertices.Count(v => !Build.Maps.MapPieceConverter.Sane(v.Position));
                if (bad > 0)
                    Console.WriteLine($"{model.Name} mesh {i}: {bad}/{mesh.Vertices.Count} bad vertices, mtd {Path.GetFileName(flver.Materials[mesh.MaterialIndex].MTD)}");
            }
        }
        return 0;
    }
}
