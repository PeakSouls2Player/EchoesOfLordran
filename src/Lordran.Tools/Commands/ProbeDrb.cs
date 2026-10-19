using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// probe-drb FILE [name-filter] — lists a DRB's textures, then every dialog/element whose name contains the
/// filter, with its sprite region and texture (to find where DS1 item icons live in the menu atlases).
/// </summary>
public static class ProbeDrb
{
    public static int Run(string[] args)
    {
        byte[] bytes = File.ReadAllBytes(args[1]);
        bool remastered = DCX.Is(bytes); // the DCX copy is DSR's own layout; the loose one is PTDE's
        if (remastered) bytes = DCX.Decompress(bytes);
        var drb = DRB.Read(bytes, remastered ? DRB.DRBVersion.DarkSoulsRemastered : DRB.DRBVersion.DarkSouls);
        string filter = args.Length > 2 ? args[2] : "";

        Console.WriteLine($"textures: {string.Join(", ", drb.Textures.Select((t, i) => $"[{i}] {t.Name}"))}");

        if (filter.StartsWith("tex:"))
        {
            // Group every sprite drawing from textures whose name starts with the given prefix, by dialog.
            string prefix = filter[4..];
            var sprites = drb.Dlgs.SelectMany(d => d.Dlgos.Select(o => (Dlg: d.Name, Dlgo: o)))
                .Where(x => x.Dlgo.Shape is DRB.Shape.SpriteBase s && s.TextureIndex >= 0 && s.TextureIndex < drb.Textures.Count
                            && drb.Textures[s.TextureIndex].Name.StartsWith(prefix))
                .ToList();
            foreach (var g in sprites.GroupBy(x => x.Dlg).OrderByDescending(g => g.Count()).Take(25))
            {
                var samples = g.Take(4).Select(x =>
                {
                    var s = (DRB.Shape.SpriteBase)x.Dlgo.Shape;
                    return $"{x.Dlgo.Name}:{drb.Textures[s.TextureIndex].Name}({s.TexLeftEdge},{s.TexTopEdge})-({s.TexRightEdge},{s.TexBottomEdge})";
                });
                Console.WriteLine($"  {g.Key} ({g.Count()}): {string.Join("  ", samples)}");
            }
            return 0;
        }

        int shown = 0;
        foreach (var dlg in drb.Dlgs)
        {
            foreach (var dlgo in dlg.Dlgos)
            {
                string name = $"{dlg.Name}/{dlgo.Name}";
                if (!name.Contains(filter, StringComparison.OrdinalIgnoreCase) || ++shown > 40)
                    continue;
                string detail = dlgo.Shape is DRB.Shape.SpriteBase s && s.TextureIndex >= 0 && s.TextureIndex < drb.Textures.Count
                    ? $"tex[{s.TextureIndex}]={drb.Textures[s.TextureIndex].Name} ({s.TexLeftEdge},{s.TexTopEdge})-({s.TexRightEdge},{s.TexBottomEdge})"
                    : dlgo.Shape.GetType().Name;
                Console.WriteLine($"  {name}  {detail}");
            }
        }
        Console.WriteLine($"{shown} matches");
        return 0;
    }
}
