using System.Numerics;
using Lordran.Tools.Build.Maps;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// atmos-check (session 11, track atmos11). Subcommands:
/// <list type="bullet">
/// <item><c>atmos-check flver FILE [FILE…]</c>: a FLVER (or the first FLVER of a .bnd/.objbnd/.chrbnd): per material the
///   MTD and textures, per mesh the layout, dynamic flag, palette, vertex-colour / normal / tangent statistics (the crow).</item>
/// <item><c>atmos-check row AREA BANK ID…</c>: DS1 draw-param rows (every field).</item>
/// </list>
/// </summary>
public static class AtmosCheck
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: atmos-check PACK MAP | flver FILE… | row AREA BANK ID…"); return 1; }
        return args[1] switch
        {
            "flver" => Flver(args.Skip(2).ToArray()),
            "row" => Row(args[2], args[3], args.Skip(4).Select(int.Parse).ToArray()),
            "probes" => Probes(args.Skip(2).ToArray()),
            "ffx-survey" => FfxSurvey(),
            "variants" => Variants(args[2]),
            "s4b" => S4b(args[2]),
            "render" => (AtmosRender.FixedAdaptation = args.Length <= 10 || args[10] != "adapt") is var _ && true ? AtmosRender.Run(args[2], args[3], args[4], new Vector3(float.Parse(args[5]), float.Parse(args[6]), float.Parse(args[7])), float.Parse(args[8]), float.Parse(args[9])) : 1,
            _ when args.Length >= 3 && Directory.Exists(args[1]) => Check(args[1], args[2]),
            _ => 1,
        };
    }

    static int S4b(string map)
    {
        var log = new List<string>();
        var r = Sky.VariantDrops(Program.DefaultDsrRoot, map, MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", map + ".msb")), log);
        foreach (var l in log) Console.WriteLine(l);
        Console.WriteLine($"{map}: rule S4b drops meshes from {r.Count} models");
        return 0;
    }

    // ------------------------------------------------------------------ atmos-check PACK MAP

    sealed class Report
    {
        public int Pass, Dev, Fail;
        public void Add(string status, string area, string text)
        {
            switch (status) { case "PASS": Pass++; break; case "DEV": Dev++; break; case "FAIL": Fail++; break; }
            Console.WriteLine($"  {status,-4} {area,-10} {text}");
        }
    }

    /// <summary>
    /// <c>atmos-check PACK MAP</c> (session 11, atmos11): every DS1 atmosphere element of the DS1 map a DS2 map (MAP = the
    /// DS2 host id, e.g. m19_41_00_00) carries must have a DS2 counterpart in PACK or a deviation logged in PACK\build.log.
    /// Elements: sky domes (S1/S1c), unlit background (S2), draw-group variants (S4/S4b), draw-param zones → filter ids
    /// (A1: parallel light, fog, exposure, grading palette, DOF near off), lanterns → PlayerLight, map SFX events (B1d/B3/
    /// FW2/L1), point lights (L1), wind, GI env-map points, light probes (LP2: size, leaf coverage of the walkable
    /// collision, root grid), water. PASS = counterpart present; DEV = a deviation the build logged (by rule); FAIL = neither.
    /// Exit code 1 on any FAIL.
    /// </summary>
    static int Check(string pack, string map)
    {
        string root = Program.DefaultDsrRoot;
        string ds1Map = MapHosts.Ds1Of(map) ?? map;
        var rep = new Report();
        Console.WriteLine($"== atmos-check {pack} {map} (DS1 {ds1Map})");
        string logPath = Path.Combine(pack, "build.log");
        var log = File.Exists(logPath) ? File.ReadLines(logPath).Where(l => l.StartsWith(map + ":")).Select(l => l[(map.Length + 1)..].Trim()).ToList() : [];
        if (log.Count == 0) rep.Add("FAIL", "log", $"no build.log lines for {map}");
        var msb1 = MSB1.Read(Path.Combine(root, "map", "MapStudio", ds1Map + ".msb"));
        var msb2 = MSB2.Read(Path.Combine(pack, "gamedata", "map", map, map + ".msb"));
        var dp = new Ds1DrawParam(root, ds1Map.Substring(1, 2));
        var mtds = Ds1Mtd.Load(root);

        // ---- sky domes, background, variants
        string mapbhd = Path.Combine(pack, "gamedata", "model", "map", map + ".mapbhd");
        var models = BXF4.Read(mapbhd, Path.ChangeExtension(mapbhd, ".mapbdt")).Files.Where(f => f.Name.EndsWith(".flv.dcx", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => Path.GetFileName(f.Name).Replace(".flv.dcx", "", StringComparison.OrdinalIgnoreCase), f => FLVER2.Read(DCX.Decompress(f.Bytes)), StringComparer.OrdinalIgnoreCase);
        int ds1Domes = 0, ds1Background = 0;
        string area = ds1Map.Substring(1, 2);
        foreach (var model in msb1.Models.MapPieces)
        {
            if (!msb1.Parts.MapPieces.Any(p => p.ModelName == model.Name)) continue;
            string file = Path.Combine(root, "map", ds1Map, $"{model.Name}A{area}.flver.dcx");
            if (!File.Exists(file)) continue;
            var flv = FLVER2.Read(DCX.Decompress(File.ReadAllBytes(file)));
            for (int mi = 0; mi < flv.Materials.Count; mi++)
            {
                if (!Ds1Mtd.IsSky(mtds.Get(flv.Materials[mi].MTD), flv.Materials[mi].MTD)) continue;
                var pts = flv.Meshes.Where(m => m.MaterialIndex == mi).SelectMany(m => m.Vertices).Select(v => v.Position).Where(MapPieceConverter.Sane).ToList();
                if (pts.Count == 0) continue;
                var d = pts.Aggregate(Vector3.Max) - pts.Aggregate(Vector3.Min);
                if (MathF.Max(d.X, MathF.Max(d.Y, d.Z)) >= Sky.BackgroundExtent) ds1Domes++;
                else rep.Add(models.Values.Any(f => f.Materials.Any(x => x.Name == flv.Materials[mi].Name && x.MTD.Contains("NoLit"))) ? "PASS" : "FAIL", "sky",$"DS1 {model.Name} material {mi} '{flv.Materials[mi].Name}' is a sky-MTD scrap ({MathF.Max(d.X, MathF.Max(d.Y, d.Z)):0.#} m < {Sky.BackgroundExtent} m): ordinary unlit material, not a dome (rule S1c)");
            }
            bool allUnlit = flv.Meshes.Count > 0 && flv.Meshes.All(m => MapPieceConverter.IsUnlit(flv.Materials[m.MaterialIndex].MTD));
            var all = flv.Meshes.SelectMany(m => m.Vertices).Select(v => v.Position).Where(MapPieceConverter.Sane).ToList();
            if (allUnlit && all.Count > 0 && (all.Aggregate(Vector3.Max) - all.Aggregate(Vector3.Min)) is var ext && MathF.Max(ext.X, MathF.Max(ext.Y, ext.Z)) > Sky.BackgroundExtent) ds1Background++;
        }
        var ds2Skies = models.Where(m => m.Value.Materials.Any(x => x.MTD.Contains(Sky.SkyMtd, StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var (name, f) in ds2Skies)
        {
            var e = f.Header.BoundingBoxMax - f.Header.BoundingBoxMin;
            float ext = MathF.Max(e.X, MathF.Max(e.Y, e.Z));
            rep.Add(ext >= Sky.BackgroundExtent ? "PASS" : "FAIL", "sky", $"DS2 sky model {name}: {f.Materials.Count} materials (dome {f.Materials[0].MTD.Split('\\').Last()}), {ext:0} m wide (rule S1/S1c: ≥ {Sky.BackgroundExtent} m)");
        }
        rep.Add(ds2Skies.Count >= ds1Domes && ds1Domes > 0 ? "PASS" : ds1Domes == 0 ? "DEV" : "FAIL", "sky", $"DS1 sky domes {ds1Domes} → DS2 SkyDome2[NoFog] models {ds2Skies.Count}");
        int bgLogged = log.Count(l => l.Contains("rule S2") || l.Contains("is a sky dome"));
        rep.Add(bgLogged >= ds1Background ? "PASS" : "FAIL", "background", $"DS1 all-unlit models > {Sky.BackgroundExtent} m: {ds1Background}; DS2 RenderType-2 models logged (S1/S2): {bgLogged}");
        foreach (var l in log.Where(l => l.Contains("rule S4b") || l.Contains("rule S4)"))) rep.Add("PASS", "variants", l);

        // ---- zones → filter ids
        var fltPath = Path.Combine(pack, "gamedata", "filter", $"filter_{map}.fltparam");
        var flt = GPARAM.Read(fltPath);
        var used = msb2.Parts.Collisions.Select(c => (int)c.UnkT13).Distinct().Order().ToList();
        var zoneLines = log.Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^atmos: filter \d+: light")).ToList();
        rep.Add(zoneLines.Count > 0 ? "PASS" : "FAIL", "zones", $"{zoneLines.Count} DS1 draw-param zones → DS2 filter ids (rule A1); MSB2 collisions use filter ids {string.Join(",", used)}");
        var resBytes = File.ReadAllBytes(Path.Combine(pack, "gamedata", "filter", "filterresource.fltbnd"));
        var res = BND4.Is(resBytes) ? (IBinder)BND4.Read(resBytes) : BND3.Read(resBytes);
        var resNames = res.Files.Select(f => Path.GetFileNameWithoutExtension(f.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (int id in used)
        {
            object? on = Atmosphere.Get(flt, "平行ライト", "有効", id);
            object? fog = Atmosphere.Get(flt, "フォグ", "色", id), fogD = Atmosphere.Get(flt, "フォグ", "距離", id), fogA = Atmosphere.Get(flt, "フォグ", "α", id);
            object? adapt = Atmosphere.Get(flt, "動的順応", "動的順応有効", id), avg = Atmosphere.Get(flt, "ToneMap", "AvgLuminance", id);
            object? mn = Atmosphere.Get(flt, "動的順応", "順応最低輝度", id), mx = Atmosphere.Get(flt, "動的順応", "順応最高輝度", id);
            object? mode = Atmosphere.Get(flt, "カラーグレーディング", "変換モード", id), pal = Atmosphere.Get(flt, "カラーグレーディング", "パレットファイルID", id);
            object? nearS = Atmosphere.Get(flt, "LensSimulation", "NearStart", id);
            object? diff0 = Atmosphere.Get(flt, "平行ライト", "DiffColor 0", id), diff10 = Atmosphere.Get(flt, "平行ライト", "DiffColor 10", id);
            bool palOk = pal != null && resNames.Any(n => n.Contains(Convert.ToString(pal, System.Globalization.CultureInfo.InvariantCulture) ?? "~"));
            bool ok = on is true && fog != null && adapt is true && palOk && nearS is float ns && ns < 0;
            static string S(object? o) => o switch
            {
                System.Drawing.Color c => $"#{c.R:X2}{c.G:X2}{c.B:X2}",
                byte[] b => "#" + Convert.ToHexString(b),
                Vector4 v => $"({v.X:0.##},{v.Y:0.##},{v.Z:0.##},{v.W:0.###})",
                float f => f.ToString("0.####"),
                null => "-",
                _ => o.ToString() ?? "-",
            };
            rep.Add(ok ? "PASS" : "FAIL", "zones", $"filter {id}: parallel light {S(on)} sun {S(diff0)} character {S(diff10)}; fog {S(fog)} {S(fogD)} m α {S(fogA)}; adaptation {S(adapt)} band {S(mn)}–{S(mx)} Avg {S(avg)}; grading mode {S(mode)} palette {S(pal)}{(palOk ? "" : " (NOT in filterresource.fltbnd)")}; DOF near start {S(nearS)}");
        }
        foreach (var l in log.Where(l => l.StartsWith("atmos: palette "))) rep.Add("PASS", "tone", l[7..]);
        var lanterns = log.Where(l => l.Contains("(LanternID) → PlayerLight")).ToList();
        var t1c = msb2.Parts.Collisions.GroupBy(c => c.UnkT1C).Select(g => $"{g.Key}×{g.Count()}");
        rep.Add(lanterns.Count > 0 ? "PASS" : "DEV", "lantern", $"{lanterns.Count} DS1 PointLightBank lanterns → PlayerLight rows; collisions PlayerLight ids {string.Join(" ", t1c)}");
        if (log.FirstOrDefault(l => l.Contains("env cubemap")) is { } env)
            rep.Add("DEV", "env", $"DS1 GI env-map points ({msb1.Events.Environments.Count} MSB Environment events) not converted: {env[7..]}");
        if (log.FirstOrDefault(l => l.Contains("best-fit DS2 area")) is { } a3) rep.Add("DEV", "fallback", a3[7..].Split(';')[0] + " (rule A3: fields DS1 has no counterpart for)");

        // ---- map SFX
        var regions = msb2.Regions.SFXs.ToList();
        foreach (var g in msb1.Events.SFX.GroupBy(e => e.EffectID))
        {
            var e0 = g.First();
            int placed = g.Count(e => regions.Any(r => r.Name.Contains($"DS1 {e.EffectID} {e.Name}")));
            int glow = g.Count(Atmosphere.IsFogWallGlow);
            bool b3 = MapEffects.Ds1(root, ds1Map, g.Key) is { Follow: true };
            string mapped = Atmosphere.EffectMap.TryGetValue(g.Key, out var m) ? $"→ DS2 {m.Ds2}" : "unmapped";
            if (b3)
            {
                int amb = msb2.Parts.Collisions.Count(c => Atmosphere.EffectMap.TryGetValue(g.Key, out var mm) && c.UnkT18 == mm.Ds2);
                rep.Add(amb > 0 ? "PASS" : "FAIL", "sfx", $"DS1 {g.Key} '{e0.Name}' ×{g.Count()} (camera-relative) {mapped}: collision AmbientSfxID on {amb} of {msb2.Parts.Collisions.Count} DS2 collisions (rule B3)");
            }
            else if (glow == g.Count()) rep.Add("PASS", "sfx", $"DS1 {g.Key} '{e0.Name}' ×{g.Count()}: fog-wall glow of a DS2 white door (rule FW2); white doors in the MSB: {msb2.Parts.Objects.Count(o => o.ModelName.StartsWith("o00_050"))}");
            else if (placed == g.Count())
            {
                int copies = regions.Count(r => g.Any(e => r.Name.Contains($"DS1 {e.EffectID} {e.Name}")));
                rep.Add("PASS", "sfx", $"DS1 {g.Key} '{e0.Name}' ×{g.Count()} {mapped}: {copies} DS2 SFX regions (rule B1/B1d)");
            }
            else
            {
                bool logged = log.Any(l => l.Contains($"ffx {g.Key}") && (l.Contains("not mapped") || l.Contains("deviation")));
                rep.Add(logged ? "DEV" : "FAIL", "sfx", $"DS1 {g.Key} '{e0.Name}' ×{g.Count()} {mapped}: {placed} placed{(logged ? " (deviation logged)" : "")}");
            }
        }
        if (log.FirstOrDefault(l => l.StartsWith("atmos: rule L1")) is { } l1)
            rep.Add(l1.Contains("not translated") ? "DEV" : "PASS", "lights", l1[7..]);
        else if (msb1.Events.Lights.Count > 0) rep.Add("FAIL", "lights", $"{msb1.Events.Lights.Count} DS1 point lights, no rule L1 line");
        foreach (var w in msb1.Events.Wind)
            rep.Add("DEV", "wind", $"DS1 Wind '{w.Name}' min {V(w.WindVecMin)} max {V(w.WindVecMax)}: not translated (DS2's wind fields undocumented; DS2 default = no wind)");

        // ---- light probes (LP2)
        var probes = ReadProbes(Path.Combine(pack, "gamedata", "model", "map", "g" + map[1..] + ".gibhd"));
        if (probes == null) rep.Add("FAIL", "probes", "no pointcloud.btpb");
        else
        {
            var samples = LightProbes.WalkSamples(root, ds1Map, msb1);
            var inside = samples.Where((_, i) => i % 37 == 0).ToList();
            int outside = inside.Count(s => s.X < probes.LeafMin.X || s.Y < probes.LeafMin.Y || s.Z < probes.LeafMin.Z || s.X > probes.LeafMax.X || s.Y > probes.LeafMax.Y || s.Z > probes.LeafMax.Z);
            bool rootOk = probes.RootMin.X <= probes.LeafMin.X && probes.RootMin.Y <= probes.LeafMin.Y && probes.RootMin.Z <= probes.LeafMin.Z && probes.RootMax.X >= probes.LeafMax.X && probes.RootMax.Y >= probes.LeafMax.Y && probes.RootMax.Z >= probes.LeafMax.Z;
            rep.Add(probes.Bytes < 16_000_000 && outside == 0 && rootOk ? "PASS" : "FAIL", "probes",
                $"{probes.Bytes:N0} bytes ({probes.Types}); leaves {V(probes.LeafMin)}..{V(probes.LeafMax)}, root {V(probes.RootMin)}..{V(probes.RootMax)}; walkable samples outside the leaf box: {outside} of {inside.Count} (rule LP2)");
        }

        // ---- water, NormalToAlpha fade, shafts
        int ds1Water = 0;
        foreach (var model in msb1.Models.MapPieces)
        {
            string file = Path.Combine(root, "map", ds1Map, $"{model.Name}A{area}.flver.dcx");
            if (!File.Exists(file)) continue;
            var flv = FLVER2.Read(DCX.Decompress(File.ReadAllBytes(file)));
            ds1Water += flv.Materials.Count(x => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(x.MTD.Replace('\\', '/')), @"Water|\[We\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }
        int ds2Water = models.Values.Sum(f => f.Materials.Count(x => x.MTD.Contains("Water", StringComparison.OrdinalIgnoreCase)));
        rep.Add(ds2Water >= ds1Water ? "PASS" : "FAIL", "water", $"DS1 water materials {ds1Water} → DS2 water materials {ds2Water}");
        var fades = log.Where(l => l.Contains("rule S3f")).ToList();
        foreach (var l in fades) rep.Add("PASS", "ntoa", l[5..]);
        rep.Add("PASS", "timeofday", "DS1 has no time of day (static draw params); DS2 maps none either");
        Console.WriteLine($"== {map}: {rep.Pass} pass, {rep.Dev} deviations (logged), {rep.Fail} FAIL");
        return rep.Fail > 0 ? 1 : 0;
    }

    /// <summary>DS1 map-piece parts of DIFFERENT models that place the same geometry (same transform, mesh vertex sets
    /// identical per material texture) with disjoint DrawGroups: DS1 shows one of them per spot, DS2 (no draw groups) both.</summary>
    static int Variants(string map)
    {
        string root = Program.DefaultDsrRoot, area = map.Substring(1, 2);
        var msb = MSB1.Read(Path.Combine(root, "map", "MapStudio", map + ".msb"));
        var sig = new Dictionary<string, Dictionary<string, (int N, Vector3 Sum)>>();
        foreach (var model in msb.Models.MapPieces)
        {
            string file = Path.Combine(root, "map", map, $"{model.Name}A{area}.flver.dcx");
            if (!File.Exists(file)) continue;
            var flv = FLVER2.Read(DCX.Decompress(File.ReadAllBytes(file)));
            var d = new Dictionary<string, (int, Vector3)>();
            foreach (var m in flv.Meshes)
            {
                var mat = flv.Materials[m.MaterialIndex];
                string key = Path.GetFileName(mat.MTD) + "|" + string.Join(",", mat.Textures.Select(t => Path.GetFileName(t.Path ?? "")));
                var cur = d.GetValueOrDefault(key);
                d[key] = (cur.Item1 + m.Vertices.Count, cur.Item2 + m.Vertices.Aggregate(Vector3.Zero, (s, v) => s + v.Position));
            }
            sig[model.Name] = d;
        }
        var parts = msb.Parts.MapPieces.Where(p => sig.ContainsKey(p.ModelName)).ToList();
        int pairs = 0;
        for (int i = 0; i < parts.Count; i++)
            for (int j = i + 1; j < parts.Count; j++)
            {
                var a = parts[i]; var b = parts[j];
                if (a.ModelName == b.ModelName || a.Position != b.Position || a.Rotation != b.Rotation) continue;
                var shared = sig[a.ModelName].Where(kv => sig[b.ModelName].TryGetValue(kv.Key, out var o) && o.N == kv.Value.N && Vector3.Distance(o.Sum, kv.Value.Sum) < 0.01f * Math.Max(1, kv.Value.N)).Select(kv => kv.Key).ToList();
                if (shared.Count == 0) continue;
                bool disjoint = !a.DrawGroups.Zip(b.DrawGroups).Any(z => (z.First & z.Second) != 0);
                int na = sig[a.ModelName].Values.Sum(v => v.N), nb = sig[b.ModelName].Values.Sum(v => v.N), ns = shared.Sum(k => sig[a.ModelName][k].N);
                Console.WriteLine($"{map}: {a.Name} [{string.Join(",", a.DrawGroups.Select(x => x.ToString("X8")))}] vs {b.Name} [{string.Join(",", b.DrawGroups.Select(x => x.ToString("X8")))}]: {(disjoint ? "DISJOINT" : "overlapping")} draw groups; identical geometry {ns} vertices ({shared.Count} materials) of {na} / {nb}: {string.Join("; ", shared.Take(4))}");
                pairs++;
            }
        Console.WriteLine($"{map}: {pairs} pairs of different models placing identical geometry");
        return 0;
    }

    /// <summary>Every DS1 MSB SFX event of every DSR map: its effect's emitters (type, size, distribution/movement modes),
    /// whether it has action 105 (partial follow), and the event's height above the nearest DS1 walkable collision below
    /// (none = outside the playable area): the evidence for rule B3 (camera-relative map ambience).</summary>
    static int FfxSurvey()
    {
        string root = Program.DefaultDsrRoot;
        var effects = new Dictionary<(string, int), string>();
        string Describe(string map, int id)
        {
            if (effects.TryGetValue((map[..3], id), out var s)) return s;
            s = "not found";
            foreach (string b in new[] { $"FRPG_SfxBnd_{map[..6]}", $"FRPG_SfxBnd_{map[..3]}", "FRPG_SfxBnd_CommonEffects" })
            {
                string path = Path.Combine(root, "sfx", b + ".ffxbnd.dcx");
                if (!File.Exists(path)) continue;
                var f = BND3.Read(DCX.Decompress(File.ReadAllBytes(path))).Files.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x.Name).Equals($"f{id:D7}", StringComparison.OrdinalIgnoreCase));
                if (f == null) continue;
                var fxr = FXR1.Read(f.Bytes);
                fxr.Flatten();
                var sw = new StringWriter();
                new System.Xml.Serialization.XmlSerializer(typeof(FXR1)).Serialize(sw, fxr);
                var x = System.Xml.Linq.XDocument.Parse(sw.ToString());
                System.Xml.Linq.XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
                var parts = new List<string>();
                foreach (var a in x.Descendants("ActionData"))
                {
                    string t = (string?)a.Attribute(xsi + "type") ?? "";
                    if (!t.StartsWith("ClusterEmitter")) continue;
                    string modes = $"d{(string?)a.Element("EmitterDistributionMode")}m{(string?)a.Element("EmitterMovementMode")}";
                    string size = t.Contains("Box") ? $"{(string?)a.Element("EmitterSizeX")?.Attribute("Value")}×{(string?)a.Element("EmitterSizeY")?.Attribute("Value")}×{(string?)a.Element("EmitterSizeZ")?.Attribute("Value")}" : "";
                    parts.Add($"{t.Replace("ClusterEmitter", "").Replace("ActionData", "")}{size} {modes}");
                }
                bool follow = x.Descendants("ActionData").Any(a => ((string?)a.Attribute(xsi + "type") ?? "").Contains("105"));
                s = $"{b[12..]}: {string.Join(", ", parts)}{(follow ? " +105" : "")}";
                break;
            }
            return effects[(map[..3], id)] = s;
        }
        foreach (string msbPath in Directory.GetFiles(Path.Combine(root, "map", "MapStudio"), "m*.msb").Order())
        {
            string map = Path.GetFileNameWithoutExtension(msbPath);
            MSB1 msb;
            try { msb = MSB1.Read(msbPath); } catch { continue; }
            foreach (var e in msb.Events.SFX)
            {
                var r = msb.Regions.GetEntries().FirstOrDefault(x => x.Name == e.RegionName);
                Console.WriteLine($"{map} {e.EffectID,7} '{e.Name}' part {e.PartName} at {(r == null ? "-" : V(r.Position))}: {Describe(map, e.EffectID)}");
            }
        }
        return 0;
    }

    static int Row(string area, string bank, int[] ids)
    {
        var dp = new Ds1DrawParam(Program.DefaultDsrRoot, area);
        foreach (int id in ids)
        {
            var r = dp.Row(bank, id);
            if (r == null) { Console.WriteLine($"a{area} {bank} {id}: (no row)"); continue; }
            Console.WriteLine($"a{area} {bank} {id} '{r.Name}': " + string.Join(" ", r.Cells.Where(c => !c.Def.InternalName.StartsWith("pad") && !c.Def.InternalName.StartsWith("dummy")).Select(c => $"{c.Def.InternalName}={c.Value}")));
        }
        return 0;
    }

    /// <summary>The pointcloud.btpb of a GI binder (gAA_BB_CC_DD.gibhd): its node types, the leaf grid's cell and the box
    /// every node covers together, the root grid, the record payload range (DC term) and the decompressed size.</summary>
    public sealed record ProbeInfo(int Nodes, int Leaves, int Roots, Vector3 LeafMin, Vector3 LeafMax, Vector3 RootMin, Vector3 RootMax, Vector3 Cell, long Bytes, int Records, float DcMin, float DcMax, string Types);

    public static ProbeInfo? ReadProbes(string gibhd)
    {
        var bxf = BXF4.Read(gibhd, Path.ChangeExtension(gibhd, ".gibdt"));
        var f = bxf.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith("pointcloud.btpb", StringComparison.OrdinalIgnoreCase));
        if (f == null) return null;
        byte[] b = DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes;
        int nodeCount = BitConverter.ToInt32(b, 8), blob = BitConverter.ToInt32(b, 12), nodeSize = BitConverter.ToInt32(b, 20), rec = BitConverter.ToInt32(b, 24);
        int table = 0x3C + blob;
        Vector3 lmin = new(float.MaxValue), lmax = new(float.MinValue), rmin = lmin, rmax = lmax, cell = Vector3.Zero;
        int leaves = 0, roots = 0, records = 0; float dcMin = float.MaxValue, dcMax = float.MinValue;
        var types = new Dictionary<int, int>(); string rootNote = "", navNote = "";
        for (int i = 0; i < nodeCount; i++)
        {
            int o = table + i * nodeSize;
            int type = BitConverter.ToInt32(b, o + 4), count = BitConverter.ToInt32(b, o + 8);
            types[type] = types.GetValueOrDefault(type) + 1;
            var c = new Vector3(BitConverter.ToSingle(b, o + 24), BitConverter.ToSingle(b, o + 28), BitConverter.ToSingle(b, o + 32));
            var mn = new Vector3(BitConverter.ToSingle(b, o + 36), BitConverter.ToSingle(b, o + 40), BitConverter.ToSingle(b, o + 44));
            var mx = new Vector3(BitConverter.ToSingle(b, o + 48), BitConverter.ToSingle(b, o + 52), BitConverter.ToSingle(b, o + 56));
            int recOff = BitConverter.ToInt32(b, o + 60);
            if (type == 2) { roots++; rmin = Vector3.Min(rmin, mn); rmax = Vector3.Max(rmax, mx); rootNote = $"root dims ({BitConverter.ToInt32(b, o + 12)},{BitConverter.ToInt32(b, o + 16)},{BitConverter.ToInt32(b, o + 20)}) cell {V(c)} count {count}"; }
            else if (type == 1 && navNote == "") navNote = $"type1 dims ({BitConverter.ToInt32(b, o + 12)},{BitConverter.ToInt32(b, o + 16)},{BitConverter.ToInt32(b, o + 20)}) cell {V(c)} count {count} box {V(mn)}..{V(mx)}";
            else { leaves++; lmin = Vector3.Min(lmin, mn); lmax = Vector3.Max(lmax, mx); if (type == 3) cell = c; }
            for (int k = 0; k < count; k++)
            {
                int ro = 0x3C + recOff + k * rec;
                if (ro + 6 > b.Length) break;
                float dc = (BitConverter.ToInt16(b, ro) + BitConverter.ToInt16(b, ro + 2) + BitConverter.ToInt16(b, ro + 4)) / 3f;
                dcMin = Math.Min(dcMin, dc); dcMax = Math.Max(dcMax, dc); records++;
            }
        }
        return new ProbeInfo(nodeCount, leaves, roots, lmin, lmax, rmin, rmax, cell, b.Length, records, dcMin, dcMax, string.Join(" ", types.OrderBy(t => t.Key).Select(t => $"type{t.Key}×{t.Value}")) + "; " + rootNote + "; " + navNote);
    }

    static int Probes(string[] files)
    {
        foreach (string f in files)
        {
            var p = ReadProbes(f);
            Console.WriteLine(p == null ? $"{f}: no pointcloud.btpb" :
                $"{Path.GetFileName(f)}: {p.Bytes:N0} bytes, {p.Types}, leaves box {V(p.LeafMin)}..{V(p.LeafMax)} (size {V(p.LeafMax - p.LeafMin)}), root box {V(p.RootMin)}..{V(p.RootMax)}, leaf cell {V(p.Cell)}, {p.Records:N0} records, DC {p.DcMin:0}..{p.DcMax:0}");
        }
        return 0;
    }

    static string V(Vector3 v) => $"({v.X:0.#},{v.Y:0.#},{v.Z:0.#})";

    static IEnumerable<(string Name, FLVER2 F)> Flvers(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        if (DCX.Is(b)) b = DCX.Decompress(b);
        if (FLVER2.Is(b)) { yield return (Path.GetFileName(path), FLVER2.Read(b)); yield break; }
        IBinder? bnd = BND4.Is(b) ? BND4.Read(b) : BND3.Is(b) ? BND3.Read(b) : null;
        if (bnd == null) yield break;
        foreach (var f in bnd.Files)
        {
            byte[] fb = DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes;
            if (FLVER2.Is(fb)) yield return (f.Name, FLVER2.Read(fb));
            else if (f.Name.EndsWith(".acb", StringComparison.OrdinalIgnoreCase))
            {
                string tmp = Path.Combine(Path.GetTempPath(), "atmos-check-" + Path.GetFileName(f.Name));
                File.WriteAllBytes(tmp, fb);
                Console.WriteLine($"-- {f.Name}");
                AcbDump.Run(["acb-dump", tmp]);
                File.Delete(tmp);
            }
            else if (f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase) && TPF.Is(fb))
                foreach (var t in TPF.Read(fb).Textures)
                    Console.WriteLine($"-- {f.Name}: texture '{t.Name}' format {t.Format} type {t.Type} mips {t.Mipmaps} flags1 {t.Flags1} {t.Bytes.Length} bytes");
        }
    }

    static int Flver(string[] files)
    {
        foreach (string file in files)
            foreach (var (name, flv) in Flvers(file))
            {
                Console.WriteLine($"== {file} :: {name}: {flv.Meshes.Count} meshes, {flv.Materials.Count} materials, {flv.Nodes.Count} nodes, {flv.BufferLayouts.Count} layouts, version 0x{flv.Header.Version:X}, header unk5C {flv.Header.Unk5C} unk5D {flv.Header.Unk5D} unk68 {flv.Header.Unk68} bbox {V(flv.Header.BoundingBoxMin)}..{V(flv.Header.BoundingBoxMax)}");
                for (int i = 0; i < flv.GXLists.Count; i++)
                    Console.WriteLine($"  gx {i}: term {flv.GXLists[i].TerminatorID}/{flv.GXLists[i].TerminatorLength}: " + string.Join(" ", flv.GXLists[i].Select(g => $"{g.ID}/{g.Unk04}[{g.Data.Length}]={Convert.ToHexString(g.Data.Take(32).ToArray())}")));
                for (int i = 0; i < flv.Materials.Count; i++)
                {
                    var m = flv.Materials[i];
                    Console.WriteLine($"  mat {i} '{m.Name}' {m.MTD} gx {m.GXIndex} index {m.Index} : " + string.Join(" ", m.Textures.Select(t => $"{t.ParamName}={t.Path} tiling {t.TilingTypeU}/{t.TilingTypeV}")));
                }
                for (int i = 0; i < flv.BufferLayouts.Count; i++)
                    Console.WriteLine($"  layout {i}: " + string.Join(",", flv.BufferLayouts[i].Select(l => $"{l.Semantic}{l.Index}:{l.Type}")));
                for (int i = 0; i < flv.Meshes.Count; i++)
                {
                    var me = flv.Meshes[i];
                    var vs = me.Vertices;
                    if (vs.Count == 0) { Console.WriteLine($"  mesh {i}: empty"); continue; }
                    Vector4 cm = Vector4.Zero; Vector4 cmin = new(9), cmax = new(-9);
                    int nc = 0;
                    foreach (var v in vs)
                        if (v.Colors.Count > 0)
                        {
                            var c = new Vector4(v.Colors[0].R, v.Colors[0].G, v.Colors[0].B, v.Colors[0].A);
                            cm += c; cmin = Vector4.Min(cmin, c); cmax = Vector4.Max(cmax, c); nc++;
                        }
                    if (nc > 0) cm /= nc;
                    double nlen = vs.Average(v => v.Normal.Length());
                    int zeroN = vs.Count(v => v.Normal.LengthSquared() < 1e-6f);
                    string nw = string.Join(",", vs.Select(v => v.NormalW).Distinct().Take(6));
                    double tlen = vs.Where(v => v.Tangents.Count > 0).Select(v => (double)new Vector3(v.Tangents[0].X, v.Tangents[0].Y, v.Tangents[0].Z).Length()).DefaultIfEmpty(-1).Average();
                    double wsum = vs.Average(v => (double)(v.BoneWeights[0] + v.BoneWeights[1] + v.BoneWeights[2] + v.BoneWeights[3]));
                    var fs = me.FaceSets.FirstOrDefault();
                    Console.WriteLine($"  mesh {i}: mat {me.MaterialIndex} dyn {me.Dynamic} node {me.NodeIndex} palette {me.BoneIndices.Count} vb [{string.Join(",", me.VertexBuffers.Select(x => x.LayoutIndex))}] faceset Unk06 {fs?.Unk06} cull {fs?.CullBackfaces} verts {vs.Count}: "
                        + $"colour0 mean ({cm.X:0.###},{cm.Y:0.###},{cm.Z:0.###},{cm.W:0.###}) min ({cmin.X:0.##},{cmin.Y:0.##},{cmin.Z:0.##},{cmin.W:0.##}) [{nc} with colour, {vs[0].Colors.Count} colours/vertex]; |n| {nlen:0.###} zero {zeroN}; NormalW {nw}; |t| {tlen:0.###}; weight sum {wsum:0.###}; uvs/vertex {vs[0].UVs.Count}; colour1 {(vs[0].Colors.Count > 1 ? $"({vs.Average(v => v.Colors[1].R):0.###},{vs.Average(v => v.Colors[1].G):0.###},{vs.Average(v => v.Colors[1].B):0.###},{vs.Average(v => v.Colors[1].A):0.###})" : "-")}; tangent w {string.Join(",", vs.Where(v => v.Tangents.Count > 0).Select(v => v.Tangents[0].W).Distinct().Take(3))}; n.y mean {vs.Average(v => v.Normal.Y):0.##}");
                }
            }
        return 0;
    }
}
