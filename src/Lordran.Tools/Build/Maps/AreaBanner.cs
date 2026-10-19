using Lordran.Tools.Textures;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// The area-name banner DS2 shows on arrival (menu/tex/icon/mapname/english/map_name_AABBCC.tpf: 1024x64 DXT5, light
/// grey text with a dark outline on transparent), made from DS1's own area-name art: DSR's English menu atlases
/// (menu/ENGLISH/menu_local.tpf.dcx) hold the names as white text lines, e.g. "Northern Undead Asylum" is the third
/// line of Menu21. The line is cut out by its alpha, scaled to DS2's banner height, greyed and outlined like DS2's.
/// </summary>
public static class AreaBanner
{
    /// <param name="column">-1: the whole width; 0/1: the left/right half (Menu20 lists the area names in two columns,
    /// e.g. line 0 = "Ash Lake" | "Firelink Shrine").</param>
    public static void Build(Pack pack, string dsrRoot, string ds2Map, string atlas, int line, int column = -1)
    {
        var tpf = TPF.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(dsrRoot, "menu", "ENGLISH", "menu_local.tpf.dcx"))));
        var src = Dds.Read(tpf.Textures.First(t => t.Name == atlas).Bytes).DecodeTop();
        if (column >= 0) src = src.Crop(column * src.Width / 2, 0, src.Width / 2, src.Height);

        // text lines = bands of rows with any visible alpha
        bool RowInk(int y) { for (int x = 0; x < src.Width; x++) if (src[x, y].A > 24) return true; return false; }
        var bands = new List<(int Top, int Bottom)>();
        for (int y = 0, start = -1; y <= src.Height; y++)
        {
            bool ink = y < src.Height && RowInk(y);
            if (ink && start < 0) start = y;
            if (!ink && start >= 0) { if (y - start > 8) bands.Add((start, y - 1)); start = -1; }
        }
        if (line >= bands.Count) throw new InvalidDataException($"{atlas} has {bands.Count} text lines, wanted line {line}");
        var (top, bottom) = bands[line];
        int left = src.Width, right = 0;
        for (int y = top; y <= bottom; y++)
            for (int x = 0; x < src.Width; x++)
                if (src[x, y].A > 24) { left = Math.Min(left, x); right = Math.Max(right, x); }
        var text = src.Crop(left, top, right - left + 1, bottom - top + 1);

        // scale to DS2's banner text height (~50 of 64 px), centre, grey text + 2 px dark outline
        const int W = 1024, H = 64, TextH = 50;
        int tw = Math.Min(W - 16, text.Width * TextH / text.Height), th = text.Height * tw / text.Width;
        var scaled = text.ResizeArea(tw, th);
        var alpha = new byte[W * H];
        int ox = (W - tw) / 2, oy = (H - th) / 2;
        for (int y = 0; y < th; y++) for (int x = 0; x < tw; x++) alpha[(oy + y) * W + ox + x] = scaled[x, y].A;
        var img = new Image(W, H);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                byte a = alpha[y * W + x];
                int outline = 0;
                for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx >= 0 && yy >= 0 && xx < W && yy < H) outline = Math.Max(outline, alpha[yy * W + xx]);
                }
                byte grey = (byte)(214 * a / 255);
                img[x, y] = new Rgba(grey, grey, grey, (byte)Math.Max(a, outline * 3 / 4));
            }

        string id = $"map_name_{ds2Map.Substring(1, 2)}{ds2Map.Substring(4, 2)}{ds2Map.Substring(7, 2)}";
        var template = TPF.Read(Path.Combine(pack.Ds2Data, "menu", "tex", "icon", "mapname", "english", "map_name_100200.tpf"));
        var t0 = template.Textures[0];
        template.Textures[0] = new TPF.Texture(id, t0.Format, t0.Flags1, Dds.Encode(img, BcFormat.BC3, mipmaps: false), TPF.TPFPlatform.PC) { Mipmaps = 1 };
        pack.AddFile("gamedata", $"menu/tex/icon/mapname/english/{id}.tpf", template.Write());
        pack.Note($"area banner {id} from DSR {atlas} line {line} ({right - left + 1}x{bottom - top + 1} px)");
    }
}
