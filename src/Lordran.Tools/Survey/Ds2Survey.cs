using Lordran.Tools.Formats;
using SoulsFormats;
using SoulsFormats.Formats.Morpheme.NSA;
using SoulsFormats.Formats.Morpheme;

namespace Lordran.Tools.Survey;

/// <summary>
/// The DS2 counterpart of <see cref="DsrSurvey"/>: one sample of every format the port writes into,
/// read from the extracted copy (extract-ds2) plus the game folder's loose regulation file.
/// </summary>
public static class Ds2Survey
{
    public const string SmithboxDs2Defs = @"C:\EchoesOfLordran\tools\smithbox\Assets\PARAM\DS2S\Defs";

    public static void Run(string data, string game)
    {
        Console.WriteLine($"# DS2 SOTFS format survey\ndata: {data}\ngame: {game}\n");
        var defs = Directory.GetFiles(SmithboxDs2Defs, "*.xml").Select(path => PARAMDEF.XmlDeserialize(path)).ToList();

        Section("Regulation (gameplay params)", () => Regulation(game, defs));
        Section("Per-map params (Majula m10_04_00_00)", () => MapParams(data, "m10_04_00_00", defs));
        Section("Map: Majula (m10_04_00_00)", () => Map(data, "m10_04_00_00"));
        Section("Character: player (c0001)", () => Character(data, "c0001"));
        Section("Character: c1000", () => Character(data, "c1000"));
        Section("TimeAct", () => TimeAct(data));
        Section("EzState (AI, events, talk)", () => EzState(data));
        Section("Equipment parts", () => Parts(data));
        Section("Menu textures", () => Menu(data));
        Section("Effects + sound", () => Fx(data, game));
    }

    static void Section(string title, Action body)
    {
        Console.WriteLine($"## {title}");
        try { body(); }
        catch (Exception e) { Console.WriteLine($"  ERROR {e.GetType().Name}: {e.Message}"); }
        Console.WriteLine();
    }

    static void Regulation(string game, List<PARAMDEF> defs)
    {
        string path = Path.Combine(game, "enc_regulation.bnd.dcx");
        byte[] plain = Ds2Regulation.Decrypt(path);
        Console.WriteLine($"  enc_regulation.bnd.dcx: decrypted via Smithbox's Andre.SoulsFormats → {plain.Length:N0} bytes");
        var bnd = BND4.Read(plain);
        int applied = 0;
        foreach (var file in bnd.Files)
        {
            PARAM param;
            try { param = PARAM.Read(file.Bytes); }
            catch (Exception e)
            {
                Console.WriteLine($"  {Path.GetFileNameWithoutExtension(file.Name),-36} UNREADABLE ({file.Bytes.Length} bytes): {e.Message}");
                continue;
            }
            bool ok = param.ApplyParamdefCarefully(defs);
            if (ok) applied++;
            Console.WriteLine($"  {Path.GetFileNameWithoutExtension(file.Name),-36} type={param.ParamType,-34} rows={param.Rows.Count,6} def={(ok ? "ok" : "MISSING")}");
        }
        Console.WriteLine($"  {bnd.Files.Count} params, {applied} with paramdefs applied");
    }

    static void MapParams(string data, string mapId, List<PARAMDEF> defs)
    {
        foreach (string file in Directory.GetFiles(Path.Combine(data, "param"), $"*_{mapId}.param"))
        {
            var param = PARAM.Read(file);
            bool ok = param.ApplyParamdefCarefully(defs);
            Console.WriteLine($"  {Path.GetFileNameWithoutExtension(file),-44} type={param.ParamType,-30} rows={param.Rows.Count,5} def={(ok ? "ok" : "MISSING")}");
        }
    }

    static void Map(string data, string mapId)
    {
        string bhd = Path.Combine(data, "model", "map", mapId + ".mapbhd");
        var bxf = BXF4.Read(bhd, Path.ChangeExtension(bhd, ".mapbdt"));
        foreach (var g in bxf.Files.GroupBy(f => Ext(f.Name)).OrderBy(g => g.Key))
            Console.WriteLine($"  {g.Count(),4} x {g.Key}");

        var piece = bxf.Files.First(f => f.Name.Contains(".flv"));
        Flver(Path.GetFileName(piece.Name), FLVER2.Read(Decompress(piece.Bytes)));
        foreach (var hkx in bxf.Files.Where(f => f.Name.Contains(".hkx")).Take(2))
            Console.WriteLine($"  {Path.GetFileName(hkx.Name)}: {HkxInfo.Describe(Decompress(hkx.Bytes))}");
        foreach (var tpf in bxf.Files.Where(f => f.Name.Contains(".tpf")).Take(1))
        {
            var t = TPF.Read(Decompress(tpf.Bytes));
            Console.WriteLine($"  {Path.GetFileName(tpf.Name)}: platform={t.Platform} {t.Textures.Count} textures formats={string.Join(",", t.Textures.Select(x => x.Format).Distinct())}");
        }

        var msb = MSB2.Read(Path.Combine(data, "map", mapId, mapId + ".msb"));
        var p = msb.Parts;
        Console.WriteLine($"  MSB2: mapPieces={p.MapPieces.Count} objects={p.Objects.Count} collisions={p.Collisions.Count} navmeshes={p.Navmeshes.Count} connectCollisions={p.ConnectCollisions.Count}");
        Console.WriteLine($"        regions={msb.Regions.GetEntries().Count} events={msb.Events.GetEntries().Count} models={msb.Models.GetEntries().Count}");

        var ngp = NGP.Read(Path.Combine(data, "map", mapId, mapId + ".ngp"));
        Console.WriteLine($"  NGP: version={ngp.Version} meshes={ngp.Meshes.Count}");
    }

    static void Character(string data, string chrId)
    {
        var bnd = BND4.Read(Path.Combine(data, "model", "chr", chrId + ".bnd"));
        Console.WriteLine($"  {chrId}.bnd: {string.Join(", ", bnd.Files.Select(f => Path.GetFileName(f.Name)))}");
        foreach (var f in bnd.Files)
        {
            byte[] b = Decompress(f.Bytes);
            if (FLVER2.Is(b)) Flver(Path.GetFileName(f.Name), FLVER2.Read(b));
            else if (f.Name.Contains(".hkx")) Console.WriteLine($"  {Path.GetFileName(f.Name)}: {HkxInfo.Describe(b)}");
        }

        string tex = Path.Combine(data, "model", "chr", chrId + ".texbnd");
        if (File.Exists(tex))
        {
            var texbnd = BND4.Read(tex);
            var tpfs = texbnd.Files.Where(f => TPF.Is(Decompress(f.Bytes))).Select(f => TPF.Read(Decompress(f.Bytes))).ToList();
            Console.WriteLine($"  {chrId}.texbnd: {tpfs.Sum(t => t.Textures.Count)} textures, formats={string.Join(",", tpfs.SelectMany(t => t.Textures).Select(t => t.Format).Distinct())}");
        }

        string ani = Path.Combine(data, "morpheme4", "chr", chrId + ".anibnd.dcx");
        if (!File.Exists(ani)) return;
        var anibnd = BND4.Read(ani);
        Console.WriteLine($"  {chrId}.anibnd: " + string.Join(", ", anibnd.Files.GroupBy(f => Ext(f.Name)).Select(g => $"{g.Count()} x {g.Key}")));

        var nmbFile = anibnd.Files.FirstOrDefault(f => f.Name.EndsWith(".nmb"));
        if (nmbFile != null)
        {
            var nmb = new NMB(new BinaryReaderEx(false, nmbFile.Bytes));
            Console.WriteLine($"  {Path.GetFileName(nmbFile.Name)}: bundles={nmb.bundles?.Count} rigs={nmb.rig.Count} animMaps={nmb.animMapping.Count} eventTracks={nmb.eventTracks.Count}");
        }
        var nsaFile = anibnd.Files.FirstOrDefault(f => f.Name.EndsWith(".nsa"));
        if (nsaFile != null)
        {
            var nsa = new NSA(new BinaryReaderEx(false, nsaFile.Bytes));
            var h = nsa.header;
            Console.WriteLine($"  {Path.GetFileName(nsaFile.Name)}: duration={h.duration:0.###}s fps={h.fps} bones={h.boneCount} animBones={h.animBoneCount} " +
                              $"static T/R={nsa.staticTranslationIndices.Count}/{nsa.staticRotationIndices.Count} dynamic T/R={nsa.dynamicTranslationIndices.Count}/{nsa.dynamicRotationIndicies.Count}");
        }
    }

    static void TimeAct(string data)
    {
        string dir = Path.Combine(data, "timeact", "chr");
        foreach (var g in Directory.GetFiles(dir).GroupBy(f => Path.GetFileNameWithoutExtension(f).Split('_').Last()).OrderBy(g => g.Key))
            Console.WriteLine($"  {g.Count(),4} x *_{g.Key}.tae");
        foreach (string file in Directory.GetFiles(dir, "c1000*").Take(3))
        {
            var tae = TAE.Read(file);
            var types = tae.Animations.SelectMany(a => a.Events).Select(e => e.Type).Distinct().Order().ToList();
            Console.WriteLine($"  {Path.GetFileName(file)}: format={tae.Format} anims={tae.Animations.Count} events={tae.Animations.Sum(a => a.Events.Count)} types=[{string.Join(",", types)}]");
        }
    }

    static void EzState(string data)
    {
        string dir = Path.Combine(data, "ezstate");
        foreach (var g in Directory.GetFiles(dir, "*.esd").GroupBy(f => Path.GetFileName(f).StartsWith("ai") ? "ai*" : Path.GetFileName(f).Split('_')[0] + "_*").OrderBy(g => g.Key))
            Console.WriteLine($"  {g.Count(),4} x {g.Key}.esd");
        foreach (string name in new[] { "ai100000.esd", "event_m10_04_00_00.esd", "talk_m10_04_00_00.esd" })
        {
            var esd = ESD.Read(Path.Combine(dir, name));
            Console.WriteLine($"  {name}: longFormat={esd.LongFormat} stateGroups={esd.StateGroups.Count} states={esd.StateGroups.Values.Sum(g => g.Count)}");
        }
    }

    static void Parts(string data)
    {
        foreach (string file in new[] { @"weapon\wp_1000_m.bnd", @"body\" + Path.GetFileName(Directory.GetFiles(Path.Combine(data, @"model\parts\body"))[0]) })
        {
            var bnd = BND4.Read(Path.Combine(data, "model", "parts", file));
            foreach (var f in bnd.Files.Where(f => FLVER2.Is(Decompress(f.Bytes))))
                Flver(file, FLVER2.Read(Decompress(f.Bytes)));
        }
    }

    static void Menu(string data)
    {
        foreach (string sub in new[] { "", "mapname", "bonfire_list", "bonfire_area" })
        {
            string dir = Path.Combine(data, "menu", "tex", "icon", sub);
            string? file = Directory.GetFiles(dir, "*.tpf*").FirstOrDefault();
            if (file == null) continue;
            var tpf = TPF.Read(file);
            var t = tpf.Textures[0];
            Console.WriteLine($"  icon/{sub,-13} {Path.GetFileName(file)}: {tpf.Textures.Count} tex, format={t.Format}, {t.Bytes.Length:N0} bytes");
        }
        Console.WriteLine($"  english FMGs: {string.Join(", ", Directory.GetFiles(Path.Combine(data, @"menu\text\english"), "*.fmg").Select(Path.GetFileNameWithoutExtension).Take(12))} ...");
    }

    static void Fx(string data, string game)
    {
        foreach (string file in Directory.GetFiles(Path.Combine(game, "sfx")))
            Console.WriteLine($"  sfx: {Path.GetFileName(file)} {new FileInfo(file).Length / 1024:N0} KB");
        Console.WriteLine($"  sound: {string.Join(", ", Directory.GetFiles(Path.Combine(game, "sound")).GroupBy(Ext).Select(g => $"{g.Count()} x {g.Key}"))}");
    }

    static void Flver(string name, FLVER2 f)
    {
        var mtds = f.Materials.Select(m => Path.GetFileName(m.MTD)).Distinct().ToList();
        Console.WriteLine($"  {name}: FLVER 0x{f.Header.Version:X} meshes={f.Meshes.Count} verts={f.Meshes.Sum(m => m.Vertices.Count)} " +
                          $"nodes={f.Nodes.Count} layouts={f.BufferLayouts.Count} mtds={mtds.Count} [{string.Join(", ", mtds.Take(4))}{(mtds.Count > 4 ? ", ..." : "")}]");
    }

    static byte[] Decompress(byte[] bytes) => DCX.Is(bytes) ? DCX.Decompress(bytes) : bytes;

    static string Ext(string path)
    {
        string name = Path.GetFileName(path);
        int dot = name.IndexOf('.');
        return dot < 0 ? "(none)" : name[dot..];
    }
}
