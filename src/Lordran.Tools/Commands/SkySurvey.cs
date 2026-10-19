using System.Numerics;
using System.Text.RegularExpressions;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// sky-survey (session 8, track skylight): how each game draws its sky and its unlit background pieces.
///   sky-survey ds1 MAP            DS1 map pieces with unlit ([..n]) materials: MTD, textures, bounds, the MSB1 parts
///                                 that place them (draw/disp groups, LightID/FogID), the MTD's shader and params.
///   sky-survey ds2 MAP|all [big]  DS2 map pieces whose materials use SkyDome*.mtd (and with "big" every piece larger
///                                 than 600 m): MTD, textures, bounds, the ACB model settings (FixToCamera, DrawDistance,
///                                 RenderType, shadows), the MSB2 parts placing them (all fields).
///   sky-survey mtd ds1|ds2 SUBSTR… shader + params of DS1 (DSR Mtd.mtdbnd) or DS2 (allmaterialbnd) MTDs.
/// </summary>
public static class SkySurvey
{
    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.WriteLine("usage: sky-survey ds1 MAP | ds2 MAP|all [big] | mtd ds1|ds2 SUBSTR..."); return 1; }
        return args[1] switch
        {
            "ds1" => Ds1(args[2]),
            "ds2" => Ds2(args[2], args.Contains("big")),
            "mtd" => Mtd(args[2], args.Skip(3).ToArray()),
            "bank" => Bank(args[2], args[3], args.Skip(4).Select(int.Parse).ToArray()),
            "tex" => TexStats(args[2], args.Skip(3).ToArray()),
            "tone" => Tone(),
            "ds2tex" => Ds2Tex(args[2], args.Skip(3).ToArray()),
            "sfxnames" => SfxNames(args.Skip(2).ToArray()),
            "pack" => PackCheck(args[2], args.Length > 3 ? args[3] : null),
            "wind" => WindSurvey(),
            "ffx" => FfxDump(args[2], args.Skip(3).ToArray()),
            _ => 1,
        };
    }

    /// <summary>
    /// wind x (skylight fix pass): every DS1 MSB1 Wind event (all DSR maps: vecMin/vecMax, unk0C/unk1C, swing cycles and
    /// powers, the part it names) and every DS2 MSB2 Wind region (all vanilla maps: shape, UnkT00..UnkT18, Unk08/Unk0E):
    /// the evidence for translating DS1's map wind (it drives DS1's particle effects) into DS2's.
    /// </summary>
    static int WindSurvey()
    {
        Console.WriteLine("== DS1 MSB1 Wind events");
        foreach (string f in Directory.GetFiles(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio"), "m*.msb").Order())
        {
            MSB1 msb;
            try { msb = MSB1.Read(f); } catch { continue; }
            foreach (var w in msb.Events.Wind)
                Console.WriteLine($"  {Path.GetFileNameWithoutExtension(f)} '{w.Name}' part {w.PartName} region {w.RegionName} vecMin {V3(w.WindVecMin)} unk0C {w.UnkT0C} vecMax {V3(w.WindVecMax)} unk1C {w.UnkT1C} swingCycle ({w.WindSwingCycle0},{w.WindSwingCycle1},{w.WindSwingCycle2},{w.WindSwingCycle3}) swingPow ({w.WindSwingPow0},{w.WindSwingPow1},{w.WindSwingPow2},{w.WindSwingPow3})");
        }
        Console.WriteLine("== DS2 MSB2 Wind regions");
        int n = 0;
        foreach (string dir in Directory.GetDirectories(Path.Combine(ExtractDs2.DefaultOut, "map")).Order())
        {
            string map = Path.GetFileName(dir), msbPath = Path.Combine(dir, map + ".msb");
            if (!File.Exists(msbPath)) continue;
            MSB2 msb;
            try { msb = MSB2.Read(msbPath); } catch { continue; }
            foreach (var w in msb.Regions.Winds)
            {
                n++;
                string shape = w.Shape switch { MSB.Shape.Box b => $"Box({b.Width},{b.Depth},{b.Height})", MSB.Shape.Sphere s => $"Sphere({s.Radius})", MSB.Shape.Cylinder c => $"Cyl({c.Radius},{c.Height})", var s => s.GetType().Name };
                Console.WriteLine($"  {map} '{w.Name}' pos {V(w.Position)} rot {V(w.Rotation)} {shape} u08 {w.Unk08} u0E {w.Unk0E} t00 {w.UnkT00} t04 {w.UnkT04} t08 {w.UnkT08} t0C {w.UnkT0C} t10 {w.UnkT10} t14 {w.UnkT14} t18 {w.UnkT18}");
            }
        }
        Console.WriteLine($"  {n} DS2 wind regions");
        return 0;
    }

    /// <summary>
    /// ffx ds2 ID… | ds1 BINDER ID… (skylight fix pass; BINDER = 10, 10_02 or CommonEffects): a DS2 effect from the global bank (title:/sfx/sfx9999.ffxbnd.dcx,
    /// read-only) as FFXDLSE, or a DSR map effect (sfx\FRPG_SfxBnd_mAA.ffxbnd.dcx) as FXR1, serialised to XML in the
    /// working directory's validation\ folder (ffx-ds2-ID.xml / ffx-ds1-ID.xml), with a summary: the numbers of every
    /// class, the referenced resource ids (textures/models) and every float in the file's parameter lists (to compare
    /// emitter extents, particle sizes, lifetimes and colours of DS1's mist with DS2's candidates).
    /// </summary>
    static int FfxDump(string game, string[] rest)
    {
        Directory.CreateDirectory("validation");
        if (game == "ds2")
        {
            string bank = Path.Combine(Program.DefaultDs2Root, "sfx", "sfx9999.ffxbnd.dcx");
            var bnd = BND4.Read(DCX.Decompress(File.ReadAllBytes(bank)));
            foreach (string id in rest)
            {
                var f = bnd.Files.FirstOrDefault(x => Path.GetFileName(x.Name).Equals($"f{int.Parse(id):D7}.ffx", StringComparison.OrdinalIgnoreCase));
                if (f == null) { Console.WriteLine($"{id}: not in sfx9999"); continue; }
                var ffx = FFXDLSE.Read(f.Bytes);
                string xml = Path.Combine("validation", $"ffx-ds2-{id}.xml");
                using (var w = new StreamWriter(xml)) ffx.XmlSerialize(w);
                Summarise($"DS2 {id} ({f.Bytes.Length} bytes)", File.ReadAllText(xml), xml);
            }
            return 0;
        }
        string area = rest[0];
        string path = Path.Combine(Program.DefaultDsrRoot, "sfx", $"FRPG_SfxBnd_{(char.IsDigit(area[0]) ? "m" : "")}{area}.ffxbnd.dcx"); // 10, 10_02, CommonEffects
        var bnd1 = BND3.Read(DCX.Decompress(File.ReadAllBytes(path)));
        foreach (string id in rest.Skip(1))
        {
            var f = bnd1.Files.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x.Name).Equals($"f{int.Parse(id):D7}", StringComparison.OrdinalIgnoreCase));
            if (f == null) { Console.WriteLine($"{id}: not in {Path.GetFileName(path)} (have {string.Join(" ", bnd1.Files.Take(20).Select(x => Path.GetFileName(x.Name)))} ...)"); continue; }
            string xml = Path.Combine("validation", $"ffx-ds1-{id}.xml");
            FXR1.Read(f.Bytes).WriteToXml(Path.GetFullPath(xml));
            Summarise($"DS1 {id} {Path.GetFileName(f.Name)} ({f.Bytes.Length} bytes)", File.ReadAllText(xml), xml);
        }
        return 0;
    }

    static void Summarise(string title, string xml, string file)
    {
        var classes = Regex.Matches(xml, @"xsi:type=""([^""]+)""").Select(m => m.Groups[1].Value).GroupBy(x => x).OrderByDescending(g => g.Count());
        var floats = Regex.Matches(xml, @">(-?\d+\.\d+(?:E[-+]?\d+)?)<").Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var resources = Regex.Matches(xml, @"<(?:int|Int32|ResourceID|TextureID|Texture\w*|Model\w*)>(\d{3,})</").Select(m => m.Groups[1].Value).Distinct().Take(30);
        Console.WriteLine($"== {title} → {file}: {xml.Length} chars of XML");
        Console.WriteLine($"  classes: {string.Join(", ", classes.Take(25).Select(g => $"{g.Key}×{g.Count()}"))}");
        Console.WriteLine($"  candidate resource ids: {string.Join(" ", resources)}");
        if (floats.Count > 0)
            Console.WriteLine($"  floats: {floats.Count}, |max| {floats.Max(Math.Abs):0.###}, distinct (first 60): {string.Join(" ", floats.Distinct().Take(60).Select(x => x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)))}");
    }

    /// <summary>ds2tex tAA_BB_CC_DD NAME…: a DS2 map texture's TPF entry (format, flags, FloatStruct) and RGBA stats
    /// (the SkyDome2 shaders decode rgb × alpha × a texture constant).</summary>
    static int Ds2Tex(string archive, string[] names)
    {
        string dir = Path.Combine(ExtractDs2.DefaultOut, "model", "map");
        var bxf = BXF4.Read(Path.Combine(dir, archive + ".tpfbhd"), Path.Combine(dir, archive + ".tpfbdt"));
        foreach (string n in names)
        {
            var f = bxf.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith(n + ".tpf", StringComparison.OrdinalIgnoreCase));
            if (f == null) { Console.WriteLine($"{n}: not in {archive}"); continue; }
            var tpf = TPF.Read(DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes);
            foreach (var t in tpf.Textures)
            {
                string fs = t.FloatStruct == null ? "none" : $"unk00 {t.FloatStruct.Unk00} values [{string.Join(",", t.FloatStruct.Values)}]";
                var img = Textures.Dds.Read(t.Bytes).DecodeTop();
                double a = img.Pixels.Average(p => p.A), r = img.Pixels.Average(p => p.R), g = img.Pixels.Average(p => p.G), b = img.Pixels.Average(p => p.B);
                Console.WriteLine($"{n}: tpf enc {tpf.Encoding} flag2 {tpf.Flag2}; tex '{t.Name}' format {t.Format} type {t.Type} mips {t.Mipmaps} flags1 {t.Flags1} floatStruct {fs}; dds {System.Text.Encoding.ASCII.GetString(t.Bytes, 84, 4)} {Textures.Dds.Read(t.Bytes).Format} {Textures.Dds.Read(t.Bytes).Width}x{Textures.Dds.Read(t.Bytes).Height}; mean RGBA ({r:0},{g:0},{b:0},{a:0}) min A {img.Pixels.Min(p => p.A)} max A {img.Pixels.Max(p => p.A)}");
            }
        }
        return 0;
    }

    /// <summary>sfxnames [x | KEYWORD…]: every DS2 MSB2 SFX region (effect id, developer name, map, count, shape), grouped
    /// by effect id; with keywords only names containing one of them. The developer names ("SFX3011【もや】") are how a
    /// DS1 effect finds its DS2 counterpart by kind.</summary>
    static int SfxNames(string[] keys)
    {
        keys = keys.Where(k => k != "x").ToArray();
        var rows = new List<(int Id, string Name, string Map, string Shape, float Y)>();
        foreach (string dir in Directory.GetDirectories(Path.Combine(ExtractDs2.DefaultOut, "map")).Order())
        {
            string map = Path.GetFileName(dir), msbPath = Path.Combine(dir, map + ".msb");
            if (!File.Exists(msbPath)) continue;
            MSB2 msb;
            try { msb = MSB2.Read(msbPath); } catch { continue; }
            foreach (var s in msb.Regions.SFXs) rows.Add((s.EffectID, s.Name, map, s.Shape.GetType().Name, s.Position.Y));
        }
        foreach (var g in rows.GroupBy(r => r.Id).OrderBy(g => g.Key))
        {
            var names = g.Select(r => r.Name).Distinct().ToList();
            if (keys.Length > 0 && !names.Any(n => keys.Any(k => n.Contains(k, StringComparison.OrdinalIgnoreCase)))) continue;
            Console.WriteLine($"{g.Key,8} ×{g.Count(),-4} maps {string.Join(",", g.Select(r => r.Map[1..6]).Distinct().Take(8))} shapes {string.Join(",", g.Select(r => r.Shape).Distinct())} names: {string.Join(" | ", names.Take(4))}");
        }
        return 0;
    }

    /// <summary>
    /// pack PACKDIR [MAP]: reads back what the skylight rules wrote into a built pack — per DS1 map: the models whose
    /// materials use SkyDome2/NoLit MTDs (material, vertex colour means, ACB settings), each sky texture's TPF entry
    /// (format, FloatStruct range) and decoded HDR mean (rgb × a × range, then squared = DS2 linear), part counts;
    /// the fltparam per used filter id (動的順応, カラーグレーディング, フォグ); filterresource.fltbnd: our palettes and
    /// whether every vanilla entry is byte-identical to DS2's.
    /// </summary>
    static int PackCheck(string packDir, string? only)
    {
        string gd = Path.Combine(packDir, "gamedata");
        foreach (string mapDir in Directory.GetDirectories(Path.Combine(gd, "map")).Where(d => Path.GetFileName(d).StartsWith("m1")))
        {
            string map = Path.GetFileName(mapDir);
            if (only != null && map != only) continue;
            Console.WriteLine($"== {map}");
            string md = Path.Combine(gd, "model", "map");
            var bxf = BXF4.Read(Path.Combine(md, map + ".mapbhd"), Path.Combine(md, map + ".mapbdt"));
            var acb = ACB.Read(bxf.Files.First(f => f.Name.EndsWith(".acb", StringComparison.OrdinalIgnoreCase)).Bytes);
            var tex = BXF4.Read(Path.Combine(md, $"t{map[1..]}.tpfbhd"), Path.Combine(md, $"t{map[1..]}.tpfbdt"));
            var msb = MSB2.Read(Path.Combine(mapDir, map + ".msb"));
            int skyTex = 0;
            foreach (var f in bxf.Files.Where(f => f.Name.EndsWith(".flv.dcx", StringComparison.OrdinalIgnoreCase)))
            {
                var flv = FLVER2.Read(DCX.Decompress(f.Bytes));
                string model = Path.GetFileName(f.Name).Replace(".flv.dcx", "");
                // sky/unlit models, and every m9xxx model (DS1's background range; rule S1b's lit remainder of a split sky)
                if (!flv.Materials.Any(m => Path.GetFileName(m.MTD).StartsWith("SkyDome") || Path.GetFileName(m.MTD).StartsWith("NoLit")) && !model.StartsWith("m9")) continue;
                var a = acb.Assets.OfType<ACB.Asset.Model>().FirstOrDefault(x => Path.GetFileNameWithoutExtension(x.AbsolutePath) == model);
                var size = flv.Header.BoundingBoxMax - flv.Header.BoundingBoxMin;
                Console.WriteLine($"  {model} size {V(size)} parts {msb.Parts.MapPieces.Count(p => p.ModelName == model)} acb render {a?.RenderType} noShadow {a?.DisableShadowSource}/{a?.DisableShadowTarget} fixCam {a?.FixToCamera}");
                foreach (var (m, i) in flv.Materials.Select((m, i) => (m, i)))
                {
                    var cols = flv.Meshes.Where(me => me.MaterialIndex == i).SelectMany(me => me.Vertices).Where(v => v.Colors.Count > 0).Select(v => v.Colors[0]).ToList();
                    string diff = Path.GetFileNameWithoutExtension(m.Textures.FirstOrDefault(t => t.ParamName == "g_DiffuseTexture")?.Path ?? "");
                    Console.WriteLine($"    {Path.GetFileName(m.MTD),-26} {diff,-28} vc0 mean {(cols.Count > 0 ? $"({cols.Average(c => c.R):0.###},{cols.Average(c => c.G):0.###},{cols.Average(c => c.B):0.###},{cols.Average(c => c.A):0.###})" : "-")}");
                    if (!Path.GetFileName(m.MTD).StartsWith("SkyDome")) continue;
                    var tf = tex.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith(diff + ".tpf", StringComparison.OrdinalIgnoreCase));
                    if (tf == null) { Console.WriteLine("      texture MISSING"); continue; }
                    var t = TPF.Read(DCX.Is(tf.Bytes) ? DCX.Decompress(tf.Bytes) : tf.Bytes).Textures[0];
                    float range = t.FloatStruct?.Values.FirstOrDefault() ?? float.NaN;
                    var img = Textures.Dds.Read(t.Bytes).DecodeTop();
                    var hdr = img.Pixels.Select(p => new Vector3(p.R, p.G, p.B) / 255f * (p.A / 255f) * range).ToList();
                    var mean = hdr.Aggregate(Vector3.Zero, (x, y) => x + y) / hdr.Count;
                    var lin = hdr.Select(x => x * x).Aggregate(Vector3.Zero, (x, y) => x + y) / hdr.Count;
                    Console.WriteLine($"      texture '{t.Name}' format {t.Format} {img.Width}x{img.Height} mips {t.Mipmaps} FloatStruct {t.FloatStruct?.Unk00} [{string.Join(",", t.FloatStruct?.Values ?? [])}]: decoded mean (rgb·a·R) {V3(mean)}, DS2 linear mean (squared) {V3(lin)}");
                    skyTex++;
                }
            }
            Console.WriteLine($"  sky textures checked: {skyTex}; map pieces placed: {msb.Parts.MapPieces.Count}");
            foreach (var g in msb.Regions.SFXs.GroupBy(s => s.EffectID).OrderBy(g => g.Key))
                Console.WriteLine($"  SFX {g.Key} ×{g.Count()}: {string.Join(" | ", g.Take(9).Select(s => $"{s.Name} {V(s.Position)}"))}{(g.Count() > 9 ? " …" : "")}");
            Console.WriteLine($"  env regions {msb.Regions.EnvLights.Count}, wind regions {msb.Regions.Winds.Count}");
            var flt = GPARAM.Read(Path.Combine(gd, "filter", $"filter_{map}.fltparam"));
            foreach (int id in msb.Parts.Collisions.Select(c => (int)c.UnkT13).Distinct().Order())
            {
                object? G(string g, string p) => Build.Maps.Atmosphere.Get(flt, g, p, id);
                Console.WriteLine($"  filter {id}: adapt {G("動的順応", "動的順応有効")} {G("動的順応", "順応最低輝度")}–{G("動的順応", "順応最高輝度")} centre {G("動的順応", "順応輝度中心")} sens {G("動的順応", "順応感度-暗")}/{G("動的順応", "順応感度-明")} speed {G("動的順応", "順応速度")} | ToneMap Avg {G("ToneMap", "AvgLuminance")} LB {G("ToneMap", "LuminanceBalance")} Scaler {G("ToneMap", "Scaler")} | grading {G("カラーグレーディング", "変換モード")} palette {G("カラーグレーディング", "パレットファイルID")} hsb {G("カラーグレーディング", "色相")}/{G("カラーグレーディング", "彩度")}/{G("カラーグレーディング", "明度")} | fog {(G("フォグ", "色") is byte[] fc ? Convert.ToHexString(fc) : "-")} {G("フォグ", "距離")} m α {G("フォグ", "α")}");
            }
        }
        string fltbnd = Path.Combine(gd, "filter", "filterresource.fltbnd");
        if (File.Exists(fltbnd) && only == null)
        {
            var ours = BND4.Read(fltbnd);
            var van = BND4.Read(Path.Combine(ExtractDs2.DefaultOut, "filter", "filterresource.fltbnd"));
            int same = 0, diff = 0;
            foreach (var vf in van.Files)
            {
                var of = ours.Files.FirstOrDefault(x => x.Name == vf.Name);
                if (of != null && (of.Bytes.AsSpan().SequenceEqual(vf.Bytes) || vf.Name.EndsWith(".acb"))) same++; else { diff++; Console.WriteLine($"  vanilla entry changed/missing: {vf.Name}"); }
            }
            var vacb = ACB.Read(van.Files.First(f => f.Name.EndsWith(".acb")).Bytes);
            var oacb = ACB.Read(ours.Files.First(f => f.Name.EndsWith(".acb")).Bytes);
            bool acbPrefix = vacb.Assets.Count <= oacb.Assets.Count && vacb.Assets.Zip(oacb.Assets).All(z => z.First.AbsolutePath == z.Second.AbsolutePath && z.First.RelativePath == z.Second.RelativePath);
            Console.WriteLine($"== filterresource.fltbnd: {ours.Files.Count} entries (vanilla {van.Files.Count}); vanilla entries identical {same}, changed {diff}; Filter.acb {oacb.Assets.Count} assets, the vanilla {vacb.Assets.Count} unchanged in order: {acbPrefix}");
            foreach (var f in ours.Files.Where(f => !van.Files.Any(v => v.Name == f.Name)))
            {
                var t = TPF.Read(f.Bytes).Textures[0];
                var acbHit = oacb.Assets.FirstOrDefault(a => a.AbsolutePath.Contains(Path.GetFileNameWithoutExtension(f.Name)));
                byte[] d = t.Bytes;
                (int R, int G, int B) Lut(int r, int g, int b) { int o = 128 + ((g + 16 * (b / 4)) * 64 + r + 16 * (b % 4)) * 4; return (d[o + 2], d[o + 1], d[o]); }
                Console.WriteLine($"  + {f.Name} id {f.ID}: tex '{t.Name}' format {t.Format} {d.Length} bytes; acb {acbHit?.RelativePath ?? "MISSING"}; LUT grey 0/5/10/15 → {Lut(0, 0, 0)} {Lut(5, 5, 5)} {Lut(10, 10, 10)} {Lut(15, 15, 15)}, red15 {Lut(15, 0, 0)}, green15 {Lut(0, 15, 0)}, blue15 {Lut(0, 0, 15)}");
            }
        }
        return 0;
    }

    static string V3(Vector3 v) => $"({v.X:0.###},{v.Y:0.###},{v.Z:0.###})";

    static double Median(IEnumerable<double> v) { var l = v.Order().ToList(); return l.Count == 0 ? double.NaN : l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2; }

    /// <summary>
    /// tone x: the auto-exposure and grading settings of every DS1 collision zone (ToneMapBank grayKeyValue,
    /// min/maxAdaptedLum, adaptSpeed; ToneCorrectBank) and of every used DS2 filter id (動的順応: 順応最低輝度/最高輝度/
    /// 輝度中心/感度-暗/感度-明/速度; カラーグレーディング 変換モード/パレット), with medians: the calibration data for the
    /// exposure rule.
    /// </summary>
    static int Tone()
    {
        string root = Program.DefaultDsrRoot;
        var banks = new Dictionary<string, Build.Maps.Ds1DrawParam>();
        var d1 = new List<(string Map, int Tm, double Key, double Min, double Max, double Speed)>();
        var tc = new List<(string Map, int Id, string Row)>();
        foreach (string f in Directory.GetFiles(Path.Combine(root, "map", "MapStudio"), "m1*.msb").Order())
        {
            string map = Path.GetFileNameWithoutExtension(f), area = map.Substring(1, 2);
            if (!banks.TryGetValue(area, out var dp)) banks[area] = dp = new Build.Maps.Ds1DrawParam(root, area);
            var msb = MSB1.Read(f);
            foreach (var g in msb.Parts.Collisions.Where(c => c.HitFilterID == 8).GroupBy(c => (c.ToneMapID, c.ToneCorrectID)))
            {
                var r = dp.Row("ToneMapBank", g.Key.ToneMapID);
                if (r != null) d1.Add((map, g.Key.ToneMapID, Build.Maps.Ds1DrawParam.Get(r, "grayKeyValue"), Build.Maps.Ds1DrawParam.Get(r, "minAdaptedLum"), Build.Maps.Ds1DrawParam.Get(r, "maxAdapredLum"), Build.Maps.Ds1DrawParam.Get(r, "adaptSpeed")));
                var t = dp.Row("ToneCorrectBank", g.Key.ToneCorrectID);
                if (t != null) tc.Add((map, g.Key.ToneCorrectID, string.Join(" ", t.Cells.Where(c => c.Def.InternalName != "sfxMultiplier" && !c.Def.InternalName.StartsWith("pad")).Select(c => $"{c.Def.InternalName}={c.Value}"))));
            }
        }
        Console.WriteLine("== DS1 ToneMapBank rows used by ordinary collisions (map, row: grayKey min max speed)");
        foreach (var x in d1.Distinct()) Console.WriteLine($"  {x.Map} tm {x.Tm}: key {x.Key:0.###} min {x.Min:0.###} max {x.Max:0.###} speed {x.Speed:0.##}");
        var u1 = d1.Distinct().ToList();
        Console.WriteLine($"  medians over {u1.Count}: key {Median(u1.Select(x => x.Key)):0.###} min {Median(u1.Select(x => x.Min)):0.###} max {Median(u1.Select(x => x.Max)):0.###} max/min {Median(u1.Select(x => x.Max / Math.Max(1e-6, x.Min))):0.##}");
        Console.WriteLine("== DS1 ToneCorrectBank rows used by ordinary collisions");
        foreach (var x in tc.Distinct()) Console.WriteLine($"  {x.Map} tc {x.Id}: {x.Row}");

        Console.WriteLine("== DS2 used filter ids: 動的順応 (on min max centre sensDark sensBright speed) | カラーグレーディング (mode palette)");
        var d2 = new List<(double Min, double Max, double Sens)>();
        string data = ExtractDs2.DefaultOut;
        foreach (string f in Directory.GetFiles(Path.Combine(data, "filter"), "filter_m*.fltparam").Order())
        {
            string map = Path.GetFileNameWithoutExtension(f)["filter_".Length..];
            string msbPath = Path.Combine(data, "map", map, map + ".msb");
            if (!File.Exists(msbPath)) continue;
            var g = GPARAM.Read(f);
            foreach (int id in Build.Maps.Atmosphere.UsedFilters(MSB2.Read(msbPath)).Where(i => i < 16))
            {
                object? V(string grp, string p) => Build.Maps.Atmosphere.Get(g, grp, p, id);
                Console.WriteLine($"  {map} id {id}: {V("動的順応", "動的順応有効")} {V("動的順応", "順応最低輝度")} {V("動的順応", "順応最高輝度")} {V("動的順応", "順応輝度中心")} {V("動的順応", "順応感度-暗")} {V("動的順応", "順応感度-明")} {V("動的順応", "順応速度")} | {V("カラーグレーディング", "変換モード")} {V("カラーグレーディング", "パレットファイルID")} | ToneMap Avg {V("ToneMap", "AvgLuminance")} Scaler {V("ToneMap", "Scaler")}");
                if (V("動的順応", "動的順応有効") is true && V("動的順応", "順応最低輝度") is float mn && V("動的順応", "順応最高輝度") is float mx && V("動的順応", "順応感度-明") is float s)
                    d2.Add((mn, mx, s));
            }
        }
        Console.WriteLine($"  medians over {d2.Count} adapting ids: min {Median(d2.Select(x => x.Min)):0.#####} max {Median(d2.Select(x => x.Max)):0.###} sensBright {Median(d2.Select(x => x.Sens)):0.###} max/min {Median(d2.Select(x => x.Max / Math.Max(1e-6, x.Min))):0.#}");
        return 0;
    }

    /// <summary>tex AREA NAME…: DS1 map textures (DSR map\mAA tpfbhd + loose tpf): format, size, mean/min/max RGBA of the top mip.</summary>
    static int TexStats(string area, string[] names)
    {
        var src = new Build.Maps.Ds1MapTextures(Program.DefaultDsrRoot, area.StartsWith('m') ? area : "m" + area);
        foreach (string n in names)
        {
            var bytes = src.Dds(n);
            if (bytes == null) { Console.WriteLine($"{n}: not found"); continue; }
            var dds = Textures.Dds.Read(bytes);
            var img = dds.DecodeTop();
            double[] sum = new double[4]; byte[] mn = [255, 255, 255, 255], mx = [0, 0, 0, 0];
            foreach (var p in img.Pixels)
            {
                byte[] c = [p.R, p.G, p.B, p.A];
                for (int i = 0; i < 4; i++) { sum[i] += c[i]; mn[i] = Math.Min(mn[i], c[i]); mx[i] = Math.Max(mx[i], c[i]); }
            }
            int N = img.Pixels.Length;
            Console.WriteLine($"{n}: {img.Width}x{img.Height} {dds.Format} mean RGBA ({sum[0] / N:0},{sum[1] / N:0},{sum[2] / N:0},{sum[3] / N:0}) min ({mn[0]},{mn[1]},{mn[2]},{mn[3]}) max ({mx[0]},{mx[1]},{mx[2]},{mx[3]})");
        }
        return 0;
    }

    /// <summary>bank AREA BANK [ID…]: rows of a DS1 area draw bank (a10 FogBank 22 23 …), every field.</summary>
    static int Bank(string area, string bank, int[] ids)
    {
        var dp = new Build.Maps.Ds1DrawParam(Program.DefaultDsrRoot, area);
        if (!dp.Banks.TryGetValue(bank, out var p)) { Console.WriteLine($"a{area}: no {bank} (have {string.Join(", ", dp.Banks.Keys)})"); return 1; }
        foreach (var r in p.Rows.Where(r => ids.Length == 0 || ids.Contains(r.ID)))
            Console.WriteLine($"a{area} {bank} {r.ID} '{r.Name}' " + string.Join(" ", r.Cells.Select(c => $"{c.Def.InternalName}={c.Value}")));
        return 0;
    }

    static string V(Vector3 v) => $"({v.X:0.#},{v.Y:0.#},{v.Z:0.#})";

    /// <summary>A DS1 unlit material: channel bracket ending in 'n' ([Dn], [DSBn]) — DS1's own "no lighting" marker.</summary>
    public static bool Ds1Unlit(string mtd)
    {
        string n = Path.GetFileNameWithoutExtension(mtd.Replace('\\', '/'));
        var m = Regex.Match(n, @"\[([^\]]*)\]");
        return m.Success && m.Groups[1].Value.EndsWith('n') || n.Contains("Sky", StringComparison.OrdinalIgnoreCase);
    }

    static int Ds1(string map)
    {
        string root = Program.DefaultDsrRoot;
        var msb = MSB1.Read(Path.Combine(root, "map", "MapStudio", map + ".msb"));
        var mtds = BND3.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(root, "mtd", "Mtd.mtdbnd.dcx")))).Files
            .GroupBy(f => Path.GetFileName(f.Name), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Bytes, StringComparer.OrdinalIgnoreCase);
        var seenMtd = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string area = map.Substring(1, 2);
        foreach (var model in msb.Models.MapPieces)
        {
            string file = Path.Combine(root, "map", map, $"{model.Name}A{area}.flver.dcx");
            if (!File.Exists(file)) continue;
            var f = FLVER2.Read(DCX.Decompress(File.ReadAllBytes(file)));
            var unlit = f.Materials.Select((m, i) => (m, i)).Where(x => Ds1Unlit(x.m.MTD)).ToList();
            if (unlit.Count == 0) continue;
            var pos = f.Meshes.Where(me => unlit.Any(u => u.i == me.MaterialIndex)).SelectMany(me => me.Vertices).Select(v => v.Position)
                .Where(p => float.IsFinite(p.X) && MathF.Abs(p.X) < 1e5f).ToList();
            var min = pos.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
            var max = pos.Aggregate(new Vector3(float.MinValue), Vector3.Max);
            int unlitMeshes = f.Meshes.Count(me => unlit.Any(u => u.i == me.MaterialIndex));
            Console.WriteLine($"{model.Name}: {unlitMeshes}/{f.Meshes.Count} meshes unlit, {pos.Count} vertices, unlit bounds {V(min)}..{V(max)} size {V(max - min)}");
            foreach (var (m, i) in unlit)
            {
                Console.WriteLine($"    mat {i} '{m.Name}' {Path.GetFileName(m.MTD)} : {string.Join(", ", m.Textures.Where(t => t.Path.Length > 0).Select(t => $"{t.ParamName}={Path.GetFileNameWithoutExtension(t.Path)}"))}");
                var meshes = f.Meshes.Where(me => me.MaterialIndex == i).ToList();
                var cols = meshes.SelectMany(me => me.Vertices).Where(v => v.Colors.Count > 0).Select(v => v.Colors[0]).ToList();
                if (cols.Count > 0)
                    Console.WriteLine($"        vertex colour 0 mean ({cols.Average(c => c.R):0.##},{cols.Average(c => c.G):0.##},{cols.Average(c => c.B):0.##},{cols.Average(c => c.A):0.##}) min A {cols.Min(c => c.A):0.##} max A {cols.Max(c => c.A):0.##}");
                seenMtd.Add(Path.GetFileName(m.MTD));
            }
            foreach (var p in msb.Parts.MapPieces.Where(p => p.ModelName == model.Name))
                Console.WriteLine($"    part {p.Name} pos {V(p.Position)} rot {V(p.Rotation)} scale {V(p.Scale)} draw [{string.Join(",", p.DrawGroups.Select(x => x.ToString("X8")))}] disp [{string.Join(",", p.DispGroups.Select(x => x.ToString("X8")))}] light {p.LightID} fog {p.FogID} scatter {p.ScatterID} tone {p.ToneMapID}/{p.ToneCorrectID} lod {p.LodParamID} isShadowSrc {p.IsShadowSrc} isShadowDest {p.IsShadowDest} isShadowOnly {p.IsShadowOnly} drawByReflectCam {p.DrawByReflectCam} drawOnlyReflectCam {p.DrawOnlyReflectCam} disablePointLightEffect {p.DisablePointLightEffect}");
        }
        Console.WriteLine();
        foreach (string name in seenMtd.Order())
            if (mtds.TryGetValue(name, out var bytes)) PrintMtd(name, MTD.Read(bytes));
            else Console.WriteLine($"{name}: not in Mtd.mtdbnd");
        return 0;
    }

    static void PrintMtd(string name, MTD mtd)
    {
        Console.WriteLine($"{name}: shader={mtd.ShaderPath} desc='{mtd.Description}'");
        foreach (var t in mtd.Textures) Console.WriteLine($"    tex {t.Type,-26} uv={t.UVNumber} idx={t.ShaderDataIndex} {t.Path}");
        foreach (var p in mtd.Params) Console.WriteLine($"    {p.Name,-30} {p.Type,-8} {(p.Value is float[] fa ? string.Join(",", fa) : p.Value is int[] ia ? string.Join(",", ia) : p.Value)}");
    }

    static int Mtd(string game, string[] subs)
    {
        IEnumerable<BinderFile> files = game == "ds1"
            ? BND3.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(Program.DefaultDsrRoot, "mtd", "Mtd.mtdbnd.dcx")))).Files
            : Unbind.Open(Path.Combine(ExtractDs2.DefaultOut, "material", "allmaterialbnd.bnd")).Files;
        foreach (var f in files)
        {
            string n = Path.GetFileName(f.Name);
            if (!n.EndsWith(".mtd", StringComparison.OrdinalIgnoreCase) || subs.Length > 0 && !subs.Any(s => n.Contains(s, StringComparison.OrdinalIgnoreCase))) continue;
            PrintMtd(n, MTD.Read(DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes));
        }
        return 0;
    }

    static int Ds2(string which, bool big)
    {
        string dir = Path.Combine(ExtractDs2.DefaultOut, "model", "map");
        var maps = which == "all" ? Directory.GetFiles(dir, "m*.mapbhd").Select(p => Path.GetFileNameWithoutExtension(p)).Order().ToList() : [which];
        var acbFields = new Dictionary<string, int>();
        foreach (string map in maps)
        {
            string bhd = Path.Combine(dir, map + ".mapbhd");
            if (!File.Exists(bhd)) continue;
            var bxf = BXF4.Read(bhd, Path.ChangeExtension(bhd, ".mapbdt"));
            var acbFile = bxf.Files.FirstOrDefault(f => f.Name.EndsWith(".acb", StringComparison.OrdinalIgnoreCase));
            var acb = acbFile != null ? ACB.Read(acbFile.Bytes) : null;
            string msbPath = Path.Combine(ExtractDs2.DefaultOut, "map", map, map + ".msb");
            var msb = File.Exists(msbPath) ? MSB2.Read(msbPath) : null;
            foreach (var file in bxf.Files.Where(f => f.Name.EndsWith(".flv.dcx", StringComparison.OrdinalIgnoreCase)))
            {
                FLVER2 flv;
                try { flv = FLVER2.Read(DCX.Decompress(file.Bytes)); } catch { continue; }
                string model = Path.GetFileName(file.Name).Replace(".flv.dcx", "", StringComparison.OrdinalIgnoreCase);
                bool sky = flv.Materials.Any(m => Path.GetFileName(m.MTD).StartsWith("SkyDome", StringComparison.OrdinalIgnoreCase));
                var size = flv.Header.BoundingBoxMax - flv.Header.BoundingBoxMin;
                bool isBig = big && Math.Max(size.X, Math.Max(size.Y, size.Z)) > 600;
                if (!sky && !isBig) continue;
                Console.WriteLine($"{map} {model}: {(sky ? "SKY" : "big")} bounds {V(flv.Header.BoundingBoxMin)}..{V(flv.Header.BoundingBoxMax)} size {V(size)}, {flv.Meshes.Count} meshes, {flv.Meshes.Sum(m => m.Vertices.Count)} vertices");
                foreach (var (m, i) in flv.Materials.Select((m, i) => (m, i)))
                {
                    var meshes = flv.Meshes.Where(me => me.MaterialIndex == i).ToList();
                    var cols = meshes.SelectMany(me => me.Vertices).Where(v => v.Colors.Count > 0).Select(v => v.Colors[0]).ToList();
                    string col = cols.Count > 0 ? $" vc0 mean ({cols.Average(c => c.R):0.##},{cols.Average(c => c.G):0.##},{cols.Average(c => c.B):0.##},{cols.Average(c => c.A):0.##})" : "";
                    var fs = meshes.SelectMany(me => me.FaceSets).FirstOrDefault();
                    Console.WriteLine($"    mat {i} '{m.Name}' {Path.GetFileName(m.MTD)}{col} cull {fs?.CullBackfaces} : {string.Join(", ", m.Textures.Select(t => $"{t.ParamName}={Path.GetFileNameWithoutExtension(t.Path)} ×{t.TilingScale.X:0.##} tile {(int)t.TilingTypeU}/{(int)t.TilingTypeV} u14 {t.Unk14:0.###} u18 {t.Unk18:0.###} u1C {t.Unk1C:0.###}"))}");
                }
                var asset = acb?.Assets.OfType<ACB.Asset.Model>().FirstOrDefault(a => Path.GetFileNameWithoutExtension(a.AbsolutePath).Equals(model, StringComparison.OrdinalIgnoreCase));
                if (asset != null)
                {
                    string s = $"draw={asset.DrawDistance} lodRate={asset.MeshLodRate} refl={asset.Reflectible} normInter={asset.NormalInteraction} u20={asset.Unk20} render={asset.RenderType} noShadowSrc={asset.DisableShadowSource} noShadowDst={asset.DisableShadowTarget} u27={asset.Unk27} u28={asset.Unk28} u2c={asset.Unk2C} fixCam={asset.FixToCamera} u2e={asset.Unk2E} lowTex={asset.LowTextureDistance} cheap={asset.CheapRenderDistance} u34={asset.Unk34} u35={asset.Unk35} u36={asset.Unk36} u37={asset.Unk37} u0A={asset.Unk0A}";
                    Console.WriteLine($"    acb {s}");
                    if (sky) acbFields[s] = acbFields.GetValueOrDefault(s) + 1;
                }
                else Console.WriteLine("    acb: no model asset");
                foreach (var p in msb?.Parts.MapPieces.Where(p => p.ModelName.Equals(model, StringComparison.OrdinalIgnoreCase)) ?? [])
                    Console.WriteLine($"    part {p.Name} pos {V(p.Position)} rot {V(p.Rotation)} scale {V(p.Scale)} t00 {p.UnkT00} t02 {p.UnkT02} draw [{string.Join(",", p.DrawGroups.Select(x => x.ToString("X8")))}] disp [{string.Join(",", p.DispGroups.Select(x => x.ToString("X8")))}] u44 {p.Unk44} u48 {p.Unk48} u4C {p.Unk4C} u50 {p.Unk50} u64 {p.Unk64} u6C {p.Unk6C} u6E {p.Unk6E}");
            }
        }
        if (acbFields.Count > 0)
        {
            Console.WriteLine("\nACB settings of sky pieces (count):");
            foreach (var (s, n) in acbFields.OrderByDescending(kv => kv.Value)) Console.WriteLine($"  {n}× {s}");
        }
        return 0;
    }
}
