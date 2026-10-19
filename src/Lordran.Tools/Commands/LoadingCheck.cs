using Lordran.Tools.Formats;
using Lordran.Tools.Textures;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 12 (load12). <c>loading-check PACK [--png OUT]</c>: the loading screens of a built pack (Build/LoadingScreens.cs).
/// <list type="bullet">
/// <item>LD-V: every vanilla LoadingDisplayItemParam row is there with its vanilla globalFlagId.</item>
/// <item>LD2: every item the pack adds over vanilla ItemParam (Echo of Lordran and dev kit excepted) has a row; every added
/// row is such an item, gated by LD1's flag (the same for all), with a name and a description in the pack's FMGs and an
/// icon file (ic_ICONID.tpf in the pack or in DS2's menu/tex/icon).</item>
/// <item>LD3: menu/20.febnd.dcx: the logo texture's size in the FLO texture table = the texture's; the vanilla logo shape is
/// gone and one shape (0, 104.65 + old height, 130.5, below the logo) shows the logo strip unchanged (BC1 re-encode
/// tolerance) with ink under it (the line); the bonfire frames (the old atlas) unchanged. --png OUT saves the new shape's
/// texture crop. --logo-only OUT.febnd.dcx runs LD3 on DS2's data alone (no pack) and checks the result.</item>
/// </list>
/// </summary>
public static class LoadingCheck
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: loading-check PACK [--png OUT] | loading-check --logo-only OUT.febnd.dcx [--png OUT]"); return 1; }
        if (args[1] == "--logo-only" && args.Length > 2)
        {
            // rule LD3 alone on DS2's data (no pack build), then its checks
            var (bytes, summary) = Build.LoadingScreens.LogoBnd(ExtractDs2.DefaultOut, Build.LoadingScreens.LogoLine);
            File.WriteAllBytes(args[2], bytes);
            Console.WriteLine(summary);
            int n = Logo(args[2], args.SkipWhile(a => a != "--png").Skip(1).FirstOrDefault());
            Console.WriteLine($"loading-check --logo-only: {n} problems");
            return n == 0 ? 0 : 1;
        }
        string packDir = args[1];
        string? png = args.SkipWhile(a => a != "--png").Skip(1).FirstOrDefault();
        int problems = 0;
        void Bad(string s) { problems++; Console.WriteLine("  PROBLEM " + s); }

        var mine = ParamStore.LoadDs2(Path.Combine(packDir, "title"), ExtractDs2.DefaultOut);
        var van = ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        var load = mine.Params["LoadingDisplayItemParam"];
        var vanLoad = van.Params["LoadingDisplayItemParam"];
        var items = mine.Params["ItemParam"];
        var vanItems = van.Params["ItemParam"].Rows.Select(r => r.ID).ToHashSet();
        uint Flag(PARAM.Row r) => Convert.ToUInt32(r["globalFlagId"].Value);

        // LD-V
        foreach (var v in vanLoad.Rows)
            if (load[v.ID] is not { } m) Bad($"vanilla row {v.ID} missing");
            else if (Flag(m) != Flag(v)) Bad($"vanilla row {v.ID}: globalFlagId {Flag(m)}, vanilla {Flag(v)}");

        // LD2
        FMG Fmg(string n)
        {
            string p = Path.Combine(packDir, "gamedata", "menu", "text", "english", n + ".fmg");
            return FMG.Read(File.Exists(p) ? p : Path.Combine(ExtractDs2.DefaultOut, "menu", "text", "english", n + ".fmg"));
        }
        var names = Fmg("itemname").Entries.Where(e => !string.IsNullOrWhiteSpace(e.Text)).ToDictionary(e => e.ID, e => e.Text);
        var descs = Fmg("detailedexplanation").Entries.Where(e => !string.IsNullOrWhiteSpace(e.Text)).Select(e => e.ID).ToHashSet();
        var gate = Build.LoadingScreens.Gate(Program.DefaultDsrRoot);
        uint want = (uint)(gate?.Ds2Flag ?? 0);
        var vanLoadIds = vanLoad.Rows.Select(r => r.ID).ToHashSet();
        var ported = items.Rows.Where(r => !vanItems.Contains(r.ID) && !Build.LoadingScreens.Exempt.Contains(r.ID)).Select(r => r.ID).ToList();
        foreach (int id in ported.Where(id => load[id] == null)) Bad($"ported item {id} {names.GetValueOrDefault(id)} has no loading-screen row");
        var byKind = new SortedDictionary<string, int>();
        foreach (var r in load.Rows.Where(r => !vanLoadIds.Contains(r.ID)))
        {
            string what = $"row {r.ID} {names.GetValueOrDefault(r.ID)}";
            if (items[r.ID] is not { } item) { Bad($"{what}: no such item"); continue; }
            if (vanItems.Contains(r.ID)) Bad($"{what}: a vanilla item added to the rotation");
            if (Build.LoadingScreens.Exempt.Contains(r.ID)) Bad($"{what}: a mod item (not DS1's) in the rotation");
            if (Flag(r) != want) Bad($"{what}: globalFlagId {Flag(r)}, rule LD1 wants {want}");
            if (!names.ContainsKey(r.ID)) Bad($"{what}: no itemname");
            if (!descs.Contains(r.ID)) Bad($"{what}: no detailedexplanation");
            int icon = Convert.ToInt32(item["iconId"].Value);
            string ic = $"ic_{icon:D10}.tpf";
            if (!File.Exists(Path.Combine(packDir, "gamedata", "menu", "tex", "icon", ic)) && !File.Exists(Path.Combine(ExtractDs2.DefaultOut, "menu", "tex", "icon", ic)))
                Bad($"{what}: no icon {ic}");
            string k = Build.LoadingScreens.Kind(r.ID);
            byKind[k] = byKind.GetValueOrDefault(k) + 1;
        }
        Console.WriteLine($"LoadingDisplayItemParam: {load.Rows.Count} rows ({vanLoad.Rows.Count} vanilla); DS1 rows: " +
                          string.Join(", ", byKind.Select(k => $"{k.Value} {k.Key}")) + $"; gate {want}; {ported.Count} ported items");

        // LD3
        string bndPath = Path.Combine(packDir, "gamedata", Build.LoadingScreens.LoadingBnd.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(bndPath)) Bad($"no {Build.LoadingScreens.LoadingBnd} (built with --no-title?)");
        else problems += Logo(bndPath, png);

        Console.WriteLine($"loading-check {packDir}: {problems} problems");
        return problems == 0 ? 0 : 1;
    }

    static int Logo(string bndPath, string? png)
    {
        int problems = 0;
        void Bad(string s) { problems++; Console.WriteLine("  PROBLEM " + s); }
        (byte[] Flo, Image Atlas) Read(string path)
        {
            var bnd = BND4.Read(path);
            var flo = bnd.Files.Single(f => f.Name.EndsWith(".flo", StringComparison.OrdinalIgnoreCase)).Bytes;
            var tpf = TPF.Read(bnd.Files.Single(f => Path.GetFileNameWithoutExtension(f.Name).Equals(Build.LoadingScreens.LogoTexture, StringComparison.OrdinalIgnoreCase)).Bytes);
            return (flo, Dds.Read(tpf.Textures.Single().Bytes).DecodeTop());
        }
        var (flo, atlas) = Read(bndPath);
        var (vFlo, vAtlas) = Read(Path.Combine(ExtractDs2.DefaultOut, "menu", "20.febnd.dcx"));
        int sizeAt = Build.LoadingScreens.TextureSizeAt(flo, Build.LoadingScreens.LogoTexture);
        int w = BitConverter.ToInt32(flo, sizeAt) >> 16, h = BitConverter.ToInt32(flo, sizeAt + 4);
        if (w != atlas.Width || h != atlas.Height) Bad($"FLO texture table says {w}x{h}, {Build.LoadingScreens.LogoTexture} is {atlas.Width}x{atlas.Height}");
        if (flo.Length != vFlo.Length) Bad($"FLO size {flo.Length}, vanilla {vFlo.Length}");
        var r = Build.LoadingScreens.LogoRect;
        if (Build.LoadingScreens.FindRect(flo, r) >= 0) Bad("the vanilla logo shape is still there");
        int dy = vAtlas.Height;
        int at = -1; float bottom = 0;
        for (int o = 0; o + 16 <= flo.Length; o += 4)
            if (Math.Abs(BitConverter.ToSingle(flo, o) - r[0]) < 0.01f && Math.Abs(BitConverter.ToSingle(flo, o + 4) - (r[1] + dy)) < 0.01f && Math.Abs(BitConverter.ToSingle(flo, o + 8) - r[2]) < 0.01f)
            { at = o; bottom = BitConverter.ToSingle(flo, o + 12); }
        if (at < 0) { Bad($"no logo shape ({r[0]}, {r[1] + dy}, {r[2]}, ..)"); return problems; }
        // screen placement (merge12: the logo vanished when it moved): a sprite draws its rectangle at rectangle position +
        // record offset, so the logo's top-left (x0 + offset x, y0 + offset y) must be vanilla's
        int rec = Build.LoadingScreens.SpriteRecordAt(flo, at), vRec = Build.LoadingScreens.SpriteRecordAt(vFlo, Build.LoadingScreens.FindRect(vFlo, r));
        float sx = BitConverter.ToSingle(flo, at) + BitConverter.ToSingle(flo, rec), sy = BitConverter.ToSingle(flo, at + 4) + BitConverter.ToSingle(flo, rec + 4);
        float vx = r[0] + BitConverter.ToSingle(vFlo, vRec), vy = r[1] + BitConverter.ToSingle(vFlo, vRec + 4);
        if (rec != vRec || Math.Abs(sx - vx) > 0.01f || Math.Abs(sy - vy) > 0.01f) Bad($"logo drawn at ({sx}, {sy}) (record 0x{rec:X}), vanilla ({vx}, {vy}) (record 0x{vRec:X})");
        // every other byte of the FLO is vanilla, except the shape's y0/y1, the logo record's y offset and the texture height
        var allowed = new HashSet<int>(Enumerable.Range(at + 4, 4).Concat(Enumerable.Range(at + 12, 4)).Concat(Enumerable.Range(sizeAt + 4, 4)).Concat(Enumerable.Range(rec + 4, 4)));
        int changed = Enumerable.Range(0, Math.Min(flo.Length, vFlo.Length)).Count(i => flo[i] != vFlo[i] && !allowed.Contains(i));
        if (changed > 0) Bad($"{changed} other FLO bytes differ from vanilla");
        if (bottom <= r[3] + dy + 4) Bad($"logo shape bottom {bottom}: no room for a line under the logo");
        if (bottom > atlas.Height) Bad($"logo shape bottom {bottom} past the atlas ({atlas.Height})");

        double Diff(int x0, int y0, int x1, int y1, int ody)
        {
            double s = 0; int n = 0;
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++)
            { var a = atlas[x, y + ody]; var b = vAtlas[x, y]; s += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B); n += 3; }
            return s / n;
        }
        int top = (int)Math.Floor(r[1]), bot = (int)Math.Ceiling(r[3]), right = (int)Math.Ceiling(r[2]);
        double frames = Diff(0, 0, vAtlas.Width, vAtlas.Height, 0), logo = Diff(0, top, right, bot, dy);
        if (frames > 2) Bad($"bonfire frames differ from vanilla: mean |diff| {frames:F2}");
        if (logo > 2) Bad($"logo strip differs from vanilla: mean |diff| {logo:F2}");
        int lit = 0, inkTop = -1, inkBottom = -1;
        for (int y = bot + dy; y < Math.Min(atlas.Height, (int)Math.Ceiling(bottom)); y++)
            for (int x = 0; x < right; x++)
                if (Math.Max(atlas[x, y].R, Math.Max(atlas[x, y].G, atlas[x, y].B)) > 120) { lit++; if (inkTop < 0) inkTop = y; inkBottom = y; }
        if (lit < 20) Bad($"no line under the logo ({lit} bright pixels)");
        Console.WriteLine($"logo shape 0x{at:X} ({r[0]}, {r[1] + dy}, {r[2]}, {bottom}) drawn at ({sx}, {sy}) = vanilla ({vx}, {vy}); atlas {atlas.Width}x{atlas.Height}; frames diff {frames:F2}, logo diff {logo:F2}; line ink rows {inkTop}..{inkBottom} ({lit} px)");
        if (png != null)
        {
            Png.Save(atlas.Crop(0, (int)Math.Floor(r[1] + dy), right, (int)Math.Ceiling(bottom) - (int)Math.Floor(r[1] + dy)), png);
            Console.WriteLine($"saved {png}");
        }
        return problems;
    }
}
