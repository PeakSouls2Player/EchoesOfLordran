using Lordran.Tools.Formats;
using Lordran.Tools.Textures;
using SoulsFormats;

namespace Lordran.Tools.Build;

/// <summary>
/// Session 11 (burg11, BURG-PLAN LD1): DS2 loading screens show an item from LoadingDisplayItemParam (row = ItemParam id)
/// once its globalFlagId is set (0 = always; vanilla gates its later items on progress flags 100100 / 100804). DS1's items
/// join the rotation once the player has left DS1's start map: the gate is the F1 global slot (FlagRules) of the start
/// map's departure — the DS1 event of the first ported DS1 map (MapHosts row 1, the Asylum) that warps the player to
/// another ported DS1 map (2002:4; its event flag, which the translated script sets before the warp: EmevdTranslator
/// HoistCrossMapWarps). Found in DS1's data, no flag literal; null (always shown) when there is none.
/// <para>Session 12 (load12, user: "add DS1 items and armor and descriptions"): <b>rule LD2</b>: the rotation holds every
/// item the pack adds over vanilla ItemParam (weapons, shields, catalysts, ammo, armour pieces, rings, spells, goods, keys;
/// the same set the dev kit gives), not only the weapons/ammo of session 5; the Echo of Lordran and the dev kit (mod items,
/// not DS1's) are left out. Each row needs the item's name and DS1 description (itemname / detailedexplanation, what the
/// screen prints); its icon (ic_ICONID.tpf) is checked by loading-check. Vanilla rows are never touched; DS2 itself shows
/// only items the character has owned (session 5, s5-14/15), and LD1's flag gates every DS1 row like vanilla's
/// 100100/100804.</para>
/// <para><b>Rule LD3</b> (<see cref="AddLogoLine"/>): the line <see cref="LogoLine"/> under the DARK SOULS II logo in the
/// loading screen's bottom-left corner.</para>
/// </summary>
public static class LoadingScreens
{
    /// <summary>The line drawn under the loading screen's DARK SOULS II logo (rule LD3; the user's "EOL" = the title
    /// screen's own second subtitle line, TitleScreen). Letters must occur in DS2's subtitle "SCHOLAR OF THE FIRST SIN",
    /// or be D or a space (TitleScreen's glyphs).</summary>
    public const string LogoLine = "ECHOES OF LORDRAN";
    /// <summary>Largest cap height of <see cref="LogoLine"/> as a fraction of the logo's (the title screen's second subtitle
    /// line is 0.75 of the first); a longer line is set smaller so it spans at most the logo's width, as DS2's subtitle
    /// spans its title logo.</summary>
    public const double LogoLineScale = 0.75;

    /// <summary>Rule LD2: one LoadingDisplayItemParam row (copied from a vanilla always-shown row, globalFlagId = LD1's
    /// gate) for every item the pack adds over vanilla ItemParam that has a name and a description. Returns the summary.</summary>
    public static string AddItems(Pack pack, ParamStore ds2, string dsrRoot)
    {
        var loading = pack.Param("LoadingDisplayItemParam");
        var items = pack.Param("ItemParam");
        var vanIds = ds2.Params["ItemParam"].Rows.Select(r => r.ID).ToHashSet();
        var template = loading.Rows.First(r => Convert.ToUInt32(r["globalFlagId"].Value) == 0);
        var gate = Gate(dsrRoot);
        var names = pack.Text("itemname").Entries.Where(e => !string.IsNullOrWhiteSpace(e.Text)).Select(e => e.ID).ToHashSet();
        var descs = pack.Text("detailedexplanation").Entries.Where(e => !string.IsNullOrWhiteSpace(e.Text)).Select(e => e.ID).ToHashSet();
        var byKind = new SortedDictionary<string, int>();
        var skipped = new List<string>();
        int added = 0;
        foreach (var item in items.Rows.Where(r => !vanIds.Contains(r.ID) && !Exempt.Contains(r.ID)).OrderBy(r => r.ID).ToList())
        {
            if (loading[item.ID] != null) continue;
            if (!names.Contains(item.ID) || !descs.Contains(item.ID)) { skipped.Add($"{item.ID} (no {(names.Contains(item.ID) ? "description" : "name")})"); continue; }
            var row = pack.AddRow("LoadingDisplayItemParam", template, item.ID, "DS1 item");
            row["globalFlagId"].Value = Convert.ChangeType(gate?.Ds2Flag ?? 0, row["globalFlagId"].Value.GetType());
            byKind[Kind(item.ID)] = byKind.GetValueOrDefault(Kind(item.ID)) + 1;
            added++;
        }
        foreach (string s in skipped) pack.Note($"loading screens: item {s} left out (rule LD2)");
        return $"loading screens: {added} DS1 items added to LoadingDisplayItemParam ({loading.Rows.Count} rows; " +
               string.Join(", ", byKind.Select(k => $"{k.Value} {k.Key}")) + ")" +
               (gate is { } g ? $", shown once DS1 flag {g.Ds1Flag} (DS2 {g.Ds2Flag}) is set (rule LD1)" : ", always shown (rule LD1: no departure flag found)") +
               (skipped.Count > 0 ? $"; {skipped.Count} left out without name/description: {string.Join(", ", skipped.Take(10))}" : "");
    }

    /// <summary>Pack-added items that are the mod's own, not DS1's: never in the rotation.</summary>
    public static readonly HashSet<int> Exempt = [Maps.EntryItem.ItemId, DevKit.ItemId];

    /// <summary>Item kind by DS2 id range (ds2-re-notes: weapons 1M-9M, shields 11M+, armour 21M-27M, spells 31M-35M,
    /// rings 40M-43M, goods 50M-69M, ammo 60.76M-60.96M and our 66M).</summary>
    public static string Kind(int id) => (id / 1_000_000) switch
    {
        < 10 => "weapons", < 20 => "shields", < 30 => "armour", < 40 => "spells", < 50 => "rings",
        _ when id is >= 60_760_000 and < 60_970_000 or >= 66_000_000 and < 67_000_000 => "ammo",
        _ => "goods",
    };

    public static (int Ds1Flag, int Ds2Flag)? Gate(string dsrRoot)
    {
        if (Maps.MapHosts.Rows.Count < 2) return null;
        string start = Maps.MapHosts.Rows[0].Ds1Map;
        string path = Path.Combine(dsrRoot, "event", start + ".emevd.dcx");
        if (!File.Exists(path)) return null;
        var ported = Maps.MapHosts.Rows.Skip(1).Select(r => (int.Parse(r.Ds1Map[1..3]), int.Parse(r.Ds1Map[4..6]))).ToHashSet();
        var defs = Commands.EmevdDump.Defs();
        foreach (var ev in EMEVD.Read(path).Events.Where(e => e.ID > 0).OrderBy(e => e.ID))
            foreach (var x in ev.Instructions.Where(i => i.Bank == 2002 && i.ID == 4))
            {
                double[] v;
                try { v = Events.EmevdTranslator.Decode(defs, x); } catch { continue; }
                if (v.Length < 5 || !ported.Contains(((int)v[3], (int)v[4]))) continue;
                if (Events.FlagRules.Map(start, (int)ev.ID) is int ds2) return ((int)ev.ID, ds2);
            }
        return null;
    }

    // ---- rule LD3: the logo line ----
    // menu/20.febnd.dcx = l20_01_Loading.flo + 16_Loading.tpf (book, frames) + progless_03.tpf + soul_sequence.tpf (1024x512
    // BC1: 8x4 bonfire frames of 128x128 + the "DARK SOULS II" logo inside frame 0's bottom strip). The layout draws the logo
    // from one shape whose texture rectangle (x0, y0, x1, y1 in texture pixels) is (0, 104.65, 130.5, 125.65); every row
    // under it is the next frame row, so the atlas is doubled to 1024x1024 (the FLO's texture table holds each texture's
    // size: soul_sequence 1024x512 -> 1024x1024), the logo strip is copied to the new half with the line under it, and
    // the logo shape points there with its bottom edge lowered. Like the title subtitle (TitleScreen, seen in game s10-2),
    // the drawn element grows with its texture rectangle; the bonfire frames keep their rectangles.
    // merge12 in game (docs\shots\m12-load.png): the logo VANISHED. Cause: a FLO sprite record (0x40 bytes: offset x, y,
    // scale x, y, 0, colour, 0, texture-record pointer, texture id, shape-rectangle pointer, ...) draws its rectangle at
    // (rectangle position + offset) in the layout's local space, i.e. the texture rectangle is also the screen placement:
    // the 32 frame records carry offsets (-128 col, -128 row) that bring every cell to the same spot, the logo's record
    // (-0.2, -0.05) leaves it at its cell rows. Moving the rectangle 512 down moved the logo 512 px down, off screen. The
    // fix: the logo record's y offset -= 512, so (rectangle top + offset) stays vanilla. The atlas resize itself is fine
    // (the frames kept drawing right with the 1024 table height, so UVs follow the table / texture size).
    public const string LoadingBnd = "menu/20.febnd.dcx";
    public const string LogoTexture = "soul_sequence";
    public static readonly float[] LogoRect = [0f, 104.65f, 130.5f, 125.65f];
    const int Ink = 40;

    /// <summary>Rule LD3: draws <paramref name="text"/> under the loading screen's DARK SOULS II logo. Returns the summary.</summary>
    public static string AddLogoLine(Pack pack, string text)
    {
        var (bytes, summary) = LogoBnd(pack.Ds2Data, text);
        pack.AddFile("gamedata", LoadingBnd, bytes);
        pack.Note(summary);
        return summary;
    }

    /// <summary>Rule LD3 on DS2's data folder: the new menu/20.febnd.dcx and the summary.</summary>
    public static (byte[] Bnd, string Summary) LogoBnd(string ds2Data, string text)
    {
        var bnd = BND4.Read(Path.Combine(ds2Data, "menu", "20.febnd.dcx"));
        var floFile = bnd.Files.Single(f => f.Name.EndsWith(".flo", StringComparison.OrdinalIgnoreCase));
        var texFile = bnd.Files.Single(f => Path.GetFileNameWithoutExtension(f.Name).Equals(LogoTexture, StringComparison.OrdinalIgnoreCase));
        byte[] flo = floFile.Bytes;
        int rectAt = FindRect(flo, LogoRect);
        if (rectAt < 0) throw new InvalidDataException($"loading layout: logo shape ({string.Join(", ", LogoRect)}) not found");
        int sizeAt = TextureSizeAt(flo, LogoTexture);
        int oldW = BitConverter.ToInt32(flo, sizeAt) >> 16, oldH = BitConverter.ToInt32(flo, sizeAt + 4);

        var tpf = TPF.Read(texFile.Bytes);
        var tex = tpf.Textures.Single();
        var atlas = Dds.Read(tex.Bytes).DecodeTop();
        if (atlas.Width != oldW || atlas.Height != oldH) throw new InvalidDataException($"loading layout: {LogoTexture} is {atlas.Width}x{atlas.Height}, the FLO says {oldW}x{oldH}");

        // the new atlas: the old one on top, the logo strip + the line in the new bottom half
        var grown = new Image(atlas.Width, atlas.Height * 2);
        for (int i = 0; i < grown.Pixels.Length; i++) grown.Pixels[i] = new Rgba(0, 0, 0, 255);
        Array.Copy(atlas.Pixels, grown.Pixels, atlas.Pixels.Length);
        int stripTop = (int)Math.Floor(LogoRect[1]), stripBottom = (int)Math.Ceiling(LogoRect[3]), stripRight = (int)Math.Ceiling(LogoRect[2]);
        int dy = atlas.Height;
        for (int y = stripTop; y < stripBottom; y++)
            for (int x = 0; x < stripRight; x++)
                grown[x, y + dy] = atlas[x, y];

        // logo metrics: ink rows/columns inside the shape
        bool Lit(Rgba p) => Math.Max(p.R, Math.Max(p.G, p.B)) > Ink;
        var inkRows = Enumerable.Range(stripTop, stripBottom - stripTop).Where(y => Enumerable.Range(0, stripRight).Any(x => Lit(atlas[x, y]))).ToList();
        var inkCols = Enumerable.Range(0, stripRight).Where(x => inkRows.Any(y => Lit(atlas[x, y]))).ToList();
        // caps = rows where at least a tenth of the ink columns are lit (the faint glow rows above/below are not caps)
        var capRows = inkRows.Where(y => Enumerable.Range(0, stripRight).Count(x => Lit(atlas[x, y])) >= inkCols.Count / 10).ToList();
        int capTop = capRows.First(), capBottom = capRows.Last(), capHeight = capBottom - capTop + 1;
        var peak = atlas.Crop(0, stripTop, stripRight, stripBottom - stripTop).Pixels.MaxBy(p => p.R + p.G + p.B);

        // the line: DS2's subtitle glyphs, scaled to LogoLineScale of the logo's cap height or the logo's width, whichever
        // is smaller, centred under the logo
        var titleBnd = BND4.Read(Path.Combine(ds2Data, "menu", "17.febnd.dcx"));
        var titleTpf = TPF.Read(titleBnd.Files.First(f => Path.GetFileNameWithoutExtension(f.Name).Equals("FHD_Title_Best_DC", StringComparison.OrdinalIgnoreCase)).Bytes);
        var (line, lineCapTop, lineCapHeight, _, _) = TitleScreen.SubtitleLine(Dds.Read(titleTpf.Textures[0].Bytes).DecodeTop(), text);
        int logoWidth = inkCols.Last() - inkCols.First() + 1;
        double scale = Math.Min(capHeight * LogoLineScale / lineCapHeight, logoWidth / (double)line.Width);
        var small = line.ResizeArea(Math.Max(1, (int)Math.Round(line.Width * scale)), Math.Max(1, (int)Math.Round(line.Height * scale)));
        // brightness: the glyphs' peak to the logo's peak (thin strokes lose intensity when scaled down)
        // and the atlas's background level (dark grey, not black) taken off first: the logo is black-keyed, grey would show
        // as a box around the line
        var bg = small.Pixels.OrderBy(p => p.R + p.G + p.B).ElementAt(small.Pixels.Length / 4);
        for (int i = 0; i < small.Pixels.Length; i++)
        {
            var p = small.Pixels[i];
            small.Pixels[i] = new Rgba((byte)Math.Max(0, p.R - bg.R), (byte)Math.Max(0, p.G - bg.G), (byte)Math.Max(0, p.B - bg.B), 255);
        }
        var sp = small.Pixels.MaxBy(p => p.R + p.G + p.B);
        double gain = (peak.R + peak.G + peak.B) / (double)Math.Max(1, sp.R + sp.G + sp.B);
        int smallCapTop = (int)Math.Round(lineCapTop * scale);
        int lineTop = capBottom + 1 + (int)Math.Round(capHeight * 0.45) - smallCapTop;   // gap under the logo: ~0.45 cap
        int left = (inkCols.First() + inkCols.Last()) / 2 - small.Width / 2;
        int inkBottom = 0;
        for (int y = 0; y < small.Height; y++)
            for (int x = 0; x < small.Width; x++)
            {
                var p = small[x, y];
                var q = new Rgba((byte)Math.Min(255, p.R * gain), (byte)Math.Min(255, p.G * gain), (byte)Math.Min(255, p.B * gain), 255);
                ref var d = ref grown[left + x, lineTop + y + dy];
                d = new Rgba(Math.Max(d.R, q.R), Math.Max(d.G, q.G), Math.Max(d.B, q.B), 255);
                if (Lit(q)) inkBottom = Math.Max(inkBottom, lineTop + y);
            }
        // the shape keeps the logo's margin under the new last ink row
        float margin = LogoRect[3] - (inkRows.Last() + 1);
        float newTop = LogoRect[1] + dy, newBottom = Math.Max(lineTop + small.Height, inkBottom + 1 + margin) + dy;
        if (newBottom > grown.Height) throw new InvalidDataException("loading logo line runs off the atlas");

        // BC1 blocks: our encoder is lossier than FromSoftware's on the small lettering (mean |diff| ~8 on the logo rows), so
        // only the line's blocks are encoded: the old atlas (frames, logo) and the copied logo strip keep vanilla's blocks
        // (the encoder insets its colour endpoints, so a block of lettering on black gets a grey floor: a visible box on the
        // black-keyed logo; near-black texels are marked transparent so those blocks use BC1's 3-colour mode, whose 4th
        // colour is exact black)
        for (int y = dy; y < grown.Height; y++)
            for (int x = 0; x < grown.Width; x++)
                if (grown[x, y] is { } p && Math.Max(p.R, Math.Max(p.G, p.B)) < 8) grown[x, y] = new Rgba(0, 0, 0, 0);
        var dds = Dds.Read(tex.Bytes);
        byte[] enc = Dds.Encode(grown, BcFormat.BC1, mipmaps: false);
        const int header = 128, block = 8;
        int rowBytes = grown.Width / 4 * block;
        if (dds.Format != BcFormat.BC1 || dds.Mips[0].Length != atlas.Height / 4 * rowBytes) throw new InvalidDataException($"{LogoTexture}: not a single-mip BC1 of the expected size");
        dds.Mips[0].CopyTo(enc, header);
        int stripBlocks = (stripRight + 3) / 4;
        for (int by = stripTop / 4; by * 4 + 3 < Math.Min(stripBottom, lineTop); by++)   // block rows the line does not touch
            Array.Copy(dds.Mips[0], by * rowBytes, enc, header + (by + dy / 4) * rowBytes, stripBlocks * block);
        tex.Bytes = enc;
        tex.Mipmaps = 1;
        texFile.Bytes = tpf.Write();
        BitConverter.GetBytes(newTop).CopyTo(flo, rectAt + 4);
        BitConverter.GetBytes(newBottom).CopyTo(flo, rectAt + 12);
        BitConverter.GetBytes(grown.Height).CopyTo(flo, sizeAt + 4);
        int recAt = SpriteRecordAt(flo, rectAt);
        float offY = BitConverter.ToSingle(flo, recAt + 4);
        BitConverter.GetBytes(offY - dy).CopyTo(flo, recAt + 4);   // screen placement unchanged (rectangle top + offset)
        floFile.Bytes = flo;
        string summary = $"loading logo line \"{text}\" (rule LD3): {LogoTexture} {oldW}x{oldH} -> {grown.Width}x{grown.Height}, logo shape at FLO 0x{rectAt:X} " +
                         $"({LogoRect[0]}, {LogoRect[1]}, {LogoRect[2]}, {LogoRect[3]}) -> ({LogoRect[0]}, {newTop}, {LogoRect[2]}, {newBottom}), sprite record 0x{recAt:X} y offset {offY} -> {offY - dy}; " +
                         $"logo caps {capHeight} px, line caps {Math.Round(lineCapHeight * scale, 1)} px at x {left}..{left + small.Width - 1}";
        return (bnd.Write(), summary);
    }

    /// <summary>Start of the one sprite record (offset x, y, scale x, y at +0; texture-record pointer at +0x20; rectangle
    /// pointer at +0x30) that draws the rectangle at <paramref name="rectAt"/>.</summary>
    public static int SpriteRecordAt(byte[] flo, int rectAt)
    {
        var hits = new List<int>();
        for (int o = 0x30; o + 8 <= flo.Length; o += 8)
            if (BitConverter.ToInt64(flo, o) == rectAt && BitConverter.ToSingle(flo, o - 0x28) == 1f && BitConverter.ToSingle(flo, o - 0x24) == 1f)
                hits.Add(o - 0x30);
        if (hits.Count != 1) throw new InvalidDataException($"loading layout: {hits.Count} sprite records draw rectangle 0x{rectAt:X}");
        return hits[0];
    }

    /// <summary>Offset of the 4-float rectangle equal to <paramref name="rect"/> (within 0.01), or -1.</summary>
    public static int FindRect(byte[] flo, float[] rect)
    {
        int found = -1;
        for (int o = 0; o + 16 <= flo.Length; o += 4)
            if (Enumerable.Range(0, 4).All(i => Math.Abs(BitConverter.ToSingle(flo, o + 4 * i) - rect[i]) < 0.01f))
            {
                if (found >= 0) throw new InvalidDataException($"loading layout: rectangle ({string.Join(", ", rect)}) twice (0x{found:X}, 0x{o:X})");
                found = o;
            }
        return found;
    }

    /// <summary>Offset of the "width &lt;&lt; 16" field of a texture's 24-byte record in the FLO texture table (name pointer
    /// u64, id u32, width &lt;&lt; 16 u32, height u32, u32); the height follows at +4.</summary>
    public static int TextureSizeAt(byte[] flo, string name)
    {
        byte[] key = [.. System.Text.Encoding.ASCII.GetBytes(name), 0];
        int at = flo.AsSpan().IndexOf(key);
        if (at < 0) throw new InvalidDataException($"loading layout: no texture name {name}");
        for (int o = 0; o + 24 <= flo.Length; o += 4)
            if (BitConverter.ToInt64(flo, o) == at && BitConverter.ToInt32(flo, o + 12) >> 16 is > 0 and <= 8192 && BitConverter.ToInt32(flo, o + 16) is > 0 and <= 8192)
                return o + 12;   // (the header's string-table offset equals the first name's offset too: no size there)
        throw new InvalidDataException($"loading layout: no texture record for {name}");
    }
}
