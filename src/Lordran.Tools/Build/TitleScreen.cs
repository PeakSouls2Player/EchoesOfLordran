using Lordran.Tools.Textures;
using SoulsFormats;

namespace Lordran.Tools.Build;

/// <summary>
/// Title screen: adds a line ("ECHOES OF LORDRAN") under DS2's "SCHOLAR OF THE FIRST SIN", lettered with that
/// subtitle's own glyphs so the font, texture and glow match.
/// <list type="bullet">
/// <item>The subtitle is pre-rendered in <c>menu/17.febnd.dcx</c>: <c>FHD_Title_Best_DC</c> (1080p+) and
/// <c>Title_Best_DC</c>, both 1024×128 DXT1. Row 1 is the caps line the title shows; row 2 is an unused mixed-case
/// version, replaced here (the ™/® marks at its right end are kept).</item>
/// <item>Glyphs: the caps line is split into its 20 letters by ink columns. D doesn't occur in it, so D = H's stem
/// (crossbar removed) + O's right half. The new line is set at 75% size with the original tracking, centred.</item>
/// <item>The layout <c>l17_01_Title.flo</c> draws the caps line from shape rectangles (x0, y0, x1, y1 in texture pixels,
/// floats) (0, 0, 1024, 63.7); their bottom edge is moved down to include the new line.</item>
/// </list>
/// </summary>
public static class TitleScreen
{
    const string SubtitleLetters = "SCHOLAROFTHEFIRSTSIN";
    const int Ink = 45;          // channel level that counts as lettering on the black background
    const float OldBottom = 63.7f;

    public static string AddSubtitleLine(Pack pack, string text)
    {
        var bnd = BND4.Read(Path.Combine(pack.Ds2Data, "menu", "17.febnd.dcx"));
        int bottom = 0;
        var notes = new List<string>();
        foreach (string name in new[] { "FHD_Title_Best_DC", "Title_Best_DC" })
        {
            var file = bnd.Files.First(f => Path.GetFileNameWithoutExtension(f.Name).Equals(name, StringComparison.OrdinalIgnoreCase));
            var tpf = TPF.Read(file.Bytes);
            var tex = tpf.Textures[0];
            var image = Dds.Read(tex.Bytes).DecodeTop();
            int lineBottom = Compose(image, text);
            bottom = Math.Max(bottom, lineBottom);
            tex.Bytes = Dds.Encode(image, BcFormat.BC1, mipmaps: false);
            tex.Format = 0;
            tex.Mipmaps = 1;
            file.Bytes = tpf.Write();
            notes.Add($"{name} line bottom {lineBottom}");
        }

        var flo = bnd.Files.First(f => f.Name.EndsWith(".flo", StringComparison.OrdinalIgnoreCase));
        int patched = PatchShapeBottoms(flo.Bytes, bottom);
        if (patched == 0) throw new InvalidDataException("title layout: subtitle shape (0, 0, 1024, 63.7) not found");
        notes.Add($"{patched} shape(s) now end at y {bottom}");

        pack.AddFile("gamedata", "menu/17.febnd.dcx", bnd.Write());
        string summary = $"title subtitle \"{text}\": {string.Join(", ", notes)}";
        pack.Note(summary);
        return summary;
    }

    /// <summary>Writes the line into row 2 of a subtitle atlas; returns the line's bottom row.</summary>
    static int Compose(Image atlas, string text)
    {
        var (line, capTop, capHeight, capBottom, centre) = SubtitleLine(atlas, text);
        const double scale = 0.75;
        var small = line.ResizeArea((int)(line.Width * scale), (int)(line.Height * scale));

        // Clear row 2 (keeping the ™/® at its right end), then centre the line under the caps.
        for (int y = 58; y < atlas.Height; y++)
            for (int x = 0; x < atlas.Width; x++)
                if (!(x >= 960 && y >= 95))
                    atlas[x, y] = new Rgba(0, 0, 0, 255);
        int left = centre - small.Width / 2;
        int lineTop = capBottom + (int)Math.Round(capHeight * 0.3);
        for (int y = 0; y < small.Height && lineTop + y < atlas.Height; y++)
            for (int x = 0; x < small.Width; x++)
                atlas[left + x, lineTop + y] = Max(atlas[left + x, lineTop + y], small[x, y]);
        return lineTop + small.Height;
    }

    /// <summary>Session 12 (load12): <paramref name="text"/> set at full size in the glyphs of DS2's subtitle caps line
    /// ("SCHOLAR OF THE FIRST SIN", row 1 of a vanilla subtitle atlas, glow included). Returns the line image, the caps'
    /// top row and height inside it, and the caps line's bottom row and centre column in the atlas.</summary>
    internal static (Image Line, int CapTop, int CapHeight, int AtlasCapBottom, int AtlasCentre) SubtitleLine(Image atlas, string text)
    {
        bool Lit(int x, int y) { var p = atlas[x, y]; return Math.Max(p.R, Math.Max(p.G, p.B)) > Ink; }

        // Caps line metrics (row 1 = top half of the atlas).
        var inkRows = Enumerable.Range(0, 64).Where(y => Enumerable.Range(0, atlas.Width).Any(x => Lit(x, y))).ToList();
        int capTop = inkRows.First(), capBottom = inkRows.Last(), capHeight = capBottom - capTop + 1;
        var spans = new List<(int A, int B)>();
        for (int x = 0, start = -1; x <= atlas.Width; x++)
        {
            bool on = x < atlas.Width && Enumerable.Range(0, 64).Any(y => Lit(x, y));
            if (on && start < 0) start = x;
            if (!on && start >= 0) { spans.Add((start, x - 1)); start = -1; }
        }
        if (spans.Count != SubtitleLetters.Length)
            throw new InvalidDataException($"title subtitle: expected {SubtitleLetters.Length} glyphs, found {spans.Count}");
        var gaps = spans.Zip(spans.Skip(1), (a, b) => b.A - a.B - 1).Order().ToList();
        int letterGap = gaps[gaps.Count / 3], wordGap = gaps[^1];
        int pad = Math.Max(2, (int)Math.Round(capHeight * 0.09));   // glow around each glyph
        int top = Math.Max(0, capTop - pad - 1), rows = Math.Min(64, capBottom + pad + 2) - top;

        Image Slice(int a, int b) => atlas.Crop(a, top, b - a + 1, rows);
        var glyphs = new Dictionary<char, Image>();
        for (int i = 0; i < spans.Count; i++)
            glyphs.TryAdd(SubtitleLetters[i], Slice(spans[i].A - pad, spans[i].B + pad));
        glyphs['D'] = ComposeD(atlas, spans[SubtitleLetters.IndexOf('H')], spans[SubtitleLetters.IndexOf('O')], pad, top, rows, Lit);

        // Set the line at full size.
        var parts = new List<Image>();
        int width = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = char.ToUpperInvariant(text[i]);
            if (c == ' ') { width += wordGap - letterGap; continue; }
            if (!glyphs.TryGetValue(c, out var g)) throw new InvalidDataException($"title subtitle: no glyph for '{c}'");
            if (parts.Count > 0) width += letterGap - 2 * pad;
            parts.Add(g);
            width += g.Width;
        }
        var line = new Image(width, rows);
        int cursor = 0, part = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ') { cursor += wordGap - letterGap; continue; }
            if (part > 0) cursor += letterGap - 2 * pad;
            var g = parts[part++];
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < g.Width; x++)
                    line[cursor + x, y] = Max(line[cursor + x, y], g[x, y]);
            cursor += g.Width;
        }
        return (line, capTop - top, capHeight, capBottom, (spans[0].A + spans[^1].B) / 2);
    }

    /// <summary>D = H's left stem (serif flares kept, crossbar rows blanked) + O's right half.</summary>
    static Image ComposeD(Image atlas, (int A, int B) h, (int A, int B) o, int pad, int top, int rows, Func<int, int, bool> lit)
    {
        int capRows = Enumerable.Range(top, rows).Count(y => Enumerable.Range(h.A, h.B - h.A + 1).Any(x => lit(x, y)));
        double Coverage(int x) => Enumerable.Range(top, rows).Count(y => lit(x, y)) / (double)Math.Max(1, capRows);
        int stemStart = h.A;
        while (Coverage(stemStart) < 0.6) stemStart++;
        int stemEnd = stemStart;
        while (Coverage(stemEnd + 1) >= 0.6) stemEnd++;
        int flareEnd = stemEnd + 2;
        int middle = (h.A + h.B) / 2;
        var crossbar = Enumerable.Range(top, rows).Where(y => lit(middle, y)).ToList();
        int barTop = crossbar.Count > 0 ? crossbar.Min() - 2 : int.MaxValue, barBottom = crossbar.Count > 0 ? crossbar.Max() + 2 : int.MinValue;

        int stemLeft = h.A - pad, bowlLeft = (o.A + o.B) / 2 - 1, bowlRight = o.B + pad;
        int stemWidth = flareEnd - stemLeft + 1, bowlWidth = bowlRight - bowlLeft + 1;
        var d = new Image(stemWidth + bowlWidth, rows);
        for (int y = 0; y < rows; y++)
        {
            int sy = top + y;
            for (int x = 0; x < stemWidth; x++)
            {
                int sx = stemLeft + x;
                d[x, y] = sx > stemEnd && sy >= barTop && sy <= barBottom ? new Rgba(0, 0, 0, 255) : atlas[sx, sy];
            }
            for (int x = 0; x < bowlWidth; x++)
                d[stemWidth + x, y] = atlas[bowlLeft + x, sy];
        }
        return d;
    }

    static Rgba Max(Rgba a, Rgba b) => new(Math.Max(a.R, b.R), Math.Max(a.G, b.G), Math.Max(a.B, b.B), 255);

    /// <summary>Moves the bottom edge of every (0, 0, 1024, 63.7) shape rectangle in the layout to <paramref name="bottom"/>.</summary>
    static int PatchShapeBottoms(byte[] flo, int bottom)
    {
        int count = 0;
        for (int o = 0; o + 16 <= flo.Length; o += 4)
        {
            float x0 = BitConverter.ToSingle(flo, o), y0 = BitConverter.ToSingle(flo, o + 4);
            float x1 = BitConverter.ToSingle(flo, o + 8), y1 = BitConverter.ToSingle(flo, o + 12);
            if (Math.Abs(x0) < 0.01f && y0 == 0 && x1 == 1024f && Math.Abs(y1 - OldBottom) < 0.01f)
            {
                BitConverter.GetBytes((float)bottom).CopyTo(flo, o + 12);
                count++;
            }
        }
        return count;
    }
}
