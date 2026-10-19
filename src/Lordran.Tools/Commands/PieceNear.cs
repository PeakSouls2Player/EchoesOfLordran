using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// piece-near DS1MAP X Y Z R — which DS1 map pieces have geometry within R of a world point, in DSR's original FLVERs
/// and in the converted pack (model\map\mAA_BB_CC_DD.mapbhd/bdt), counted per mesh: vertices and faces near the point.
/// Shows where the conversion lost geometry.
/// </summary>
public static class PieceNear
{
    public static int Run(string[] args)
    {
        string map = args[1];
        var at = new Vector3(float.Parse(args[2]), float.Parse(args[3]), float.Parse(args[4]));
        float r = float.Parse(args[5]);
        var msb = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", map + ".msb"));
        string area = map.Substring(1, 2);
        Console.WriteLine("DS1 (DSR) map pieces:");
        var ds1Near = new Dictionary<string, int>();
        foreach (var part in msb.Parts.MapPieces)
        {
            string f = Path.Combine(Program.DefaultDsrRoot, "map", map, $"{part.ModelName}A{area}.flver.dcx");
            if (!File.Exists(f)) continue;
            var flver = FLVER2.Read(DCX.Decompress(File.ReadAllBytes(f)));
            var m = Transform(part.Position, part.Rotation, part.Scale);
            for (int i = 0; i < flver.Meshes.Count; i++)
            {
                var mesh = flver.Meshes[i];
                int near = mesh.Vertices.Count(v => Vector3.Distance(Vector3.Transform(v.Position, m), at) <= r);
                if (near == 0) continue;
                var mat = flver.Materials[mesh.MaterialIndex];
                Console.WriteLine($"  {part.Name,-12} mesh {i,-3} near-verts={near,-5} of {mesh.Vertices.Count,-6} dynamic={mesh.Dynamic} facesets={string.Join("/", mesh.FaceSets.Select(fs => $"{fs.Flags}:{fs.Indices.Count}{(fs.CullBackfaces ? "c" : "")}"))} mtd={Path.GetFileName(mat.MTD)}");
                ds1Near[part.ModelName] = ds1Near.GetValueOrDefault(part.ModelName) + near;
            }
        }
        string ds2Map = map; // the Asylum keeps its DS1 id
        string bhd = Path.Combine(Build.Pack.DefaultOut, "gamedata", "model", "map", ds2Map + ".mapbhd");
        if (!File.Exists(bhd)) { Console.WriteLine("no converted map in the pack"); return 0; }
        var bxf = BXF4.Read(bhd, Path.ChangeExtension(bhd, ".mapbdt"));
        var msb2 = MSB2.Read(Path.Combine(Build.Pack.DefaultOut, "gamedata", "map", ds2Map, ds2Map + ".msb"));
        Console.WriteLine("converted (pack):");
        foreach (var part in msb2.Parts.MapPieces)
        {
            var file = bxf.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith(part.ModelName + ".flv", StringComparison.OrdinalIgnoreCase));
            if (file == null) { Console.WriteLine($"  {part.Name}: model {part.ModelName} not in the archive"); continue; }
            var flver = FLVER2.Read(DCX.Decompress(file.Bytes));
            var m = Transform(part.Position, part.Rotation, part.Scale);
            for (int i = 0; i < flver.Meshes.Count; i++)
            {
                var mesh = flver.Meshes[i];
                int near = mesh.Vertices.Count(v => Vector3.Distance(Vector3.Transform(v.Position, m), at) <= r);
                if (near == 0) continue;
                var mat = flver.Materials[mesh.MaterialIndex];
                Console.WriteLine($"  {part.Name,-12} mesh {i,-3} near-verts={near,-5} of {mesh.Vertices.Count,-6} facesets={string.Join("/", mesh.FaceSets.Select(fs => $"{fs.Flags}:{fs.Indices.Count}{(fs.CullBackfaces ? "c" : "")}"))} mtd={Path.GetFileName(mat.MTD)}");
            }
        }
        return 0;
    }

    // FromSoftware MSB rotations are degrees, applied Z, X, Y (as in DSMapStudio).
    static Matrix4x4 Transform(Vector3 pos, Vector3 rotDeg, Vector3 scale)
    {
        const float d = MathF.PI / 180;
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationZ(rotDeg.Z * d) * Matrix4x4.CreateRotationX(rotDeg.X * d) * Matrix4x4.CreateRotationY(rotDeg.Y * d) * Matrix4x4.CreateTranslation(pos);
    }
}
