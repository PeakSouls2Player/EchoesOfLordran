using System.Globalization;
using System.Numerics;
using Lordran.Tools.Build.Maps;
using Lordran.Tools.Textures;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// travel-render ENTITY OUT.png [EYEX EYEY EYEZ YAW PITCH FOV EXPOSURE] — session 11 (travel12), rule TP1: the offline
/// render of a DS1 bonfire's Travel picture (<see cref="TravelPictures"/>) at 360x200, from its Mappings\travel-views.tsv
/// row (or the automatic view, or the given one). ENTITY = the DS1 bonfire entity (e.g. 1811961); its DS1 map is
/// mAA_B0 from the entity's first three digits.
/// travel-render ENTITY OUT.png --sweep [DIST] [HEIGHT] — a sheet of 8 views around the bonfire (yaw 0..315), to pick one.
/// </summary>
public static class TravelRender
{
    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: travel-render ENTITY OUT.png [EYEX EYEY EYEZ YAW PITCH FOV EXPOSURE] | --sweep [DIST] [HEIGHT]"); return 1; }
        int entity = int.Parse(args[1]);
        string ds1Map = $"m{entity / 100000:D2}_{entity / 10000 % 10:D2}_00_00";
        string root = Program.DefaultDsrRoot;
        var msb = MSB1.Read(Path.Combine(root, "map", "MapStudio", ds1Map + ".msb"));
        var b = msb.Parts.Objects.FirstOrDefault(p => p.EntityID == entity) ?? throw new InvalidDataException($"{ds1Map} has no part with entity {entity}");
        float F(int i) => float.Parse(args[i], CultureInfo.InvariantCulture);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (args.Length > 3 && args[3] == "--sweep")
        {
            float dist = args.Length > 4 ? F(4) : 5, height = args.Length > 5 ? F(5) : 2;
            const int w = 360, h = 200;
            var sheet = new Image(w * 4 + 12, h * 2 + 4);
            for (int k = 0; k < 8; k++)
            {
                float yaw = k * 45, r = yaw * MathF.PI / 180;
                var facing = new Vector3(-MathF.Sin(r), 0, -MathF.Cos(r));
                var view = new TravelPictures.View(ds1Map, entity, b.Position - facing * dist + new Vector3(0, height, 0), yaw, -14, 70, 1, "sweep");
                var img = TravelPictures.Render(root, view, w, h, b.Position, b.Rotation);
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) sheet[(k % 4) * (w + 4) + x, (k / 4) * (h + 4) + y] = img[x, y];
                Console.WriteLine($"panel {k}: eye {view.Eye.X:0.00} {view.Eye.Y:0.00} {view.Eye.Z:0.00} yaw {yaw}");
            }
            Png.Save(sheet, args[2]);
        }
        else
        {
            var view = args.Length >= 10
                ? new TravelPictures.View(ds1Map, entity, new Vector3(F(3), F(4), F(5)), F(6), F(7), F(8), F(9), "command line")
                : TravelPictures.ViewFor(ds1Map, entity, b.Position, b.Rotation);
            var img = TravelPictures.Render(root, view, 360, 200, b.Position, b.Rotation);
            Png.Save(img, args[2]);
            Console.WriteLine($"{ds1Map} bonfire {entity} at {b.Position}: view eye {view.Eye} yaw {view.Yaw} pitch {view.Pitch} fov {view.Fov} exposure {view.Exposure} ({view.Note})");
        }
        Console.WriteLine($"wrote {args[2]} in {sw.Elapsed.TotalSeconds:0.0} s");
        return 0;
    }
}
