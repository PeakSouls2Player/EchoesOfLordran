using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// cull-survey MAP [MAP...] — face sets with and without CullBackfaces in a map's models: DS2 maps (data\ds2\model\map\
/// mAA_BB_CC_DD.mapbhd) or the pack's converted maps (pack:mAA_BB_CC_DD), with the materials of non-culled ones.
/// </summary>
public static class CullSurvey
{
    public static int Run(string[] args)
    {
        foreach (string arg in args.Skip(1))
        {
            bool fromPack = arg.StartsWith("pack:");
            string map = fromPack ? arg[5..] : arg;
            string dir = fromPack ? Path.Combine(Build.Pack.DefaultOut, "gamedata", "model", "map") : Path.Combine(ExtractDs2.DefaultOut, "model", "map");
            var bxf = BXF4.Read(Path.Combine(dir, map + ".mapbhd"), Path.Combine(dir, map + ".mapbdt"));
            int culled = 0, open = 0;
            var openMtds = new Dictionary<string, int>();
            foreach (var f in bxf.Files.Where(f => f.Name.EndsWith(".flv.dcx", StringComparison.OrdinalIgnoreCase)))
            {
                var flver = FLVER2.Read(DCX.Decompress(f.Bytes));
                foreach (var mesh in flver.Meshes)
                    foreach (var fs in mesh.FaceSets.Where(fs => fs.Flags == FLVER2.FaceSet.FSFlags.None))
                    {
                        if (fs.CullBackfaces) culled++;
                        else { open++; string mtd = Path.GetFileName(flver.Materials[mesh.MaterialIndex].MTD); openMtds[mtd] = openMtds.GetValueOrDefault(mtd) + 1; }
                    }
            }
            Console.WriteLine($"{arg}: {culled} culled face sets, {open} not culled; header version {FLVER2.Read(DCX.Decompress(bxf.Files.First(f => f.Name.EndsWith(".flv.dcx")).Bytes)).Header.Version:X}");
            foreach (var (mtd, n) in openMtds.OrderByDescending(x => x.Value).Take(12)) Console.WriteLine($"   not culled: {n,4} × {mtd}");
        }
        return 0;
    }
}
