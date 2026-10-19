using System.Numerics;
using Lordran.Tools.Build.Maps;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// atmos-survey (session 8, atmosphere track): the data behind DS1 vs DS2 lighting, fog, post-processing, sound and
/// ambience particles.
///   atmos-survey ds1 MAP [--banks]        DS1 collisions' draw-param ids (Light/Fog/Scatter/LensFlare/Shadow/Dof/
///                                         ToneMap/ToneCorrect/Lantern/Lod, env-map spot, sound space), the bank rows
///                                         they pick (--banks: every field), MSB1 Sound / SFX / Wind / Environment /
///                                         Light events with their regions.
///   atmos-survey ds2 MAP|all|FILE.msb     DS2 MSB2 Light / Fog / BGColor / Shadow events, collision atmosphere fields
///                                         (Smithbox DS2S names: T10 reverb, T11 delay, T13 FilterID, T15 ambient-SFX
///                                         attach, T18 AmbientSfxID, T1C PlayerLightSfxID, T26/T27 GameBrightness,
///                                         T2E CubeEnvID?), Sound / SFX / EnvLight regions.
///   atmos-survey flt MAP|FILE [GROUP]     a DS2 fltparam (GPARAM "filt" v2): groups, params, value ids, values.
///   Session-8 fix pass: lightbanks AREA|all (what LightBank / s_LightBank hold and who selects their rows), slot10 x
///   (DS2's own hemisphere-only character lights), flt-diff (Filter's fallback order on a built fltparam), zonebox
///   DS1MAP (test positions per filter id).
/// </summary>
public static class AtmosSurvey
{
    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.WriteLine("usage: atmos-survey ds1 MAP [--banks] | ds2 MAP|all|FILE.msb | flt MAP|FILE [GROUP]"); return 1; }
        return args[1] switch
        {
            "ds1" => Ds1(args[2], args.Contains("--banks")),
            "ds2" => Ds2(args[2]),
            "flt" => Flt(args[2], args.Length > 3 ? args[3] : null),
            "env" => Env(args[2]),
            "flt-roundtrip" => FltRoundTrip(args[2]),
            "plan" => PlanFor(args[2]),
            "units" => Units(),
            "envtest" => EnvTest(),
            "compare" => Compare(args[2], args[3], args.Length > 4 ? args[4] : null),
            "spot" => Spot(args[2], args.Skip(3).Select(float.Parse).ToArray()),
            "lightbanks" => LightBanks(args[2]),
            "zonebox" => ZoneBox(args[2]),
            "slot10" => Slot10(),
            "flt-diff" => FltDiff(args[2], args[3], int.Parse(args[4]), args.Length > 6 ? args[5] : null, args.Length > 6 ? int.Parse(args[6]) : 0, args.Length > 7 ? int.Parse(args[7]) : 16),
            _ => 1,
        };
    }

    /// <summary>
    /// flt-diff PACK_FLT TEMPLATE_MAP TEMPLATE_ID [PROBE_MAP PROBE_ID [ZONES]] (session-8 fix pass): checks Atmosphere.Filter's
    /// fallback order on a built fltparam. Every value of every zone filter id (0..ZONES-1, default 16) is compared with the vanilla template map's
    /// TEMPLATE_ID value (k-th value of the id ↔ k-th, block by block); probe/GI read-out fields
    /// (Atmosphere.IsProbeField) with PROBE_MAP's PROBE_ID instead. Prints the params that differ (= written from DS1)
    /// with the ids where they differ, and every probe field that does not match.
    /// </summary>
    static int FltDiff(string packFlt, string templateMap, int templateId, string? probeMap, int probeId, int zones)
    {
        var ours = GPARAM.Read(packFlt);
        var t = GPARAM.Read(Path.Combine(ExtractDs2.DefaultOut, "filter", $"filter_{templateMap}.fltparam"));
        var pr = probeMap == null ? null : GPARAM.Read(Path.Combine(ExtractDs2.DefaultOut, "filter", $"filter_{probeMap}.fltparam"));
        static List<object> Vals(GPARAM.Param p, int id) => Enumerable.Range(0, p.Values.Count).Where(i => p.ValueIDs[i] == id).Select(i => p.Values[i]).ToList();
        static bool Eq(object a, object b) => a is byte[] x && b is byte[] y ? x.AsSpan().SequenceEqual(y) : Equals(a, b);
        int probeBad = 0, probeChecked = 0, same = 0;
        for (int gi = 0; gi < ours.Groups.Count; gi++)
        {
            var og = ours.Groups[gi]; var tg = t.Groups.First(x => x.Name1 == og.Name1);
            for (int pi = 0; pi < og.Params.Count; pi++)
            {
                var op = og.Params[pi];
                if (op.Values.Count == 0) continue;
                int block = og.Params.Take(pi).Count(x => x.Name1 == op.Name1);
                var tp = tg.Params.Where(x => x.Name1 == op.Name1).ElementAtOrDefault(block);
                if (pr != null && og.Name1 == "平行ライト" && Atmosphere.IsProbeField(op.Name1))
                {
                    var want = Atmosphere.Get(pr, og.Name1, op.Name1, probeId);
                    var bad = Enumerable.Range(0, zones).Where(id => Vals(op, id).Any(v => want == null || !Eq(v, want))).ToList();
                    probeChecked++;
                    if (bad.Count > 0) { probeBad++; Console.WriteLine($"  PROBE MISMATCH {og.Name1}/{op.Name1}: ids {string.Join(",", bad)}"); }
                    continue;
                }
                if (tp == null) { Console.WriteLine($"  {og.Name1}/{op.Name1}[{block}]: not in the template"); continue; }
                var tv = Vals(tp, templateId);
                var diff = Enumerable.Range(0, zones).Where(id => { var v = Vals(op, id); return v.Count != tv.Count || v.Zip(tv).Any(x => !Eq(x.First, x.Second)); }).ToList();
                if (diff.Count == 0) { same++; continue; }
                Console.WriteLine($"  differs from {templateMap} id {templateId}: {og.Name1}/{op.Name1}{(block > 0 ? $" (block {block + 1})" : "")} at ids {string.Join(",", diff)}");
            }
        }
        Console.WriteLine($"{same} params equal the template id {templateId} at every zone filter id 0-{zones - 1}; probe fields checked {probeChecked}, mismatching {probeBad}");
        return probeBad == 0 ? 0 : 1;
    }

    /// <summary>slot10 x (session 8 fix pass): in every vanilla DS2 map, the used filter ids' character light (平行ライト
    /// DiffColor 10 W) and hemisphere (AddHemiColorUp/Down 10 W): does DS2 itself light characters with hemisphere only
    /// (W 0) anywhere, as DS1's LightBank 27 'B2キャラ（日陰）' (all colA 0) asks for Firelink's shade?</summary>
    static int Slot10()
    {
        int used = 0, zero = 0;
        foreach (string f in Directory.GetFiles(Path.Combine(ExtractDs2.DefaultOut, "filter"), "filter_m*.fltparam").OrderBy(x => x))
        {
            string map = Path.GetFileNameWithoutExtension(f)["filter_".Length..];
            string msbPath = Path.Combine(ExtractDs2.DefaultOut, "map", map, map + ".msb");
            if (!File.Exists(msbPath)) continue;
            var g = GPARAM.Read(f);
            var ids = Atmosphere.UsedFilters(MSB2.Read(msbPath)).Where(id => id < Atmosphere.MaxFilters).ToList();
            var cells = new List<string>();
            foreach (int id in ids)
            {
                if (Atmosphere.Get(g, "平行ライト", "有効", id) is not true) continue;
                var d = Atmosphere.Get(g, "平行ライト", "DiffColor 10", id) as Vector4?;
                var u = Atmosphere.Get(g, "平行ライト", "AddHemiColorUp 10", id) as Vector4?;
                var dn = Atmosphere.Get(g, "平行ライト", "AddHemiColorDown 10", id) as Vector4?;
                used++; if (d?.W == 0) zero++;
                cells.Add($"{id}:{d?.W:0.###}/{u?.W:0.###}/{dn?.W:0.###}");
            }
            Console.WriteLine($"{map}: id:DiffColor10 W/AddHemiUp10 W/AddHemiDown10 W  {string.Join("  ", cells)}");
        }
        Console.WriteLine($"{zero} of {used} used, enabled filter ids have DiffColor 10 W = 0");
        return 0;
    }

    /// <summary>
    /// lightbanks AREA|all (session 8 fix pass): what DS1's two light banks hold and who selects their rows, to decide
    /// what s_LightBank is. Per area: every LightBank / s_LightBank row that differs from the bank's default row
    /// (name, active lights = colA &gt; 0 with direction, hemisphere A), whether the s_ row's name is English (DSR) and
    /// whether it is a cutscene/SFX row (<see cref="Ds1DrawParam.IsNonGameplayRow"/>); per map of the area, which LightIDs
    /// the ordinary collisions (hit filter 8), map pieces, objects and enemies use; and for each collision LightID
    /// whether s_ light 0 points along the ShadowBank row those collisions use.
    /// </summary>
    static int LightBanks(string which)
    {
        string root = Program.DefaultDsrRoot;
        var areas = which == "all"
            ? Directory.GetFiles(Path.Combine(root, "param", "DrawParam"), "a*_DrawParam.parambnd.dcx").Select(f => Path.GetFileName(f).Substring(1, 2)).Distinct().OrderBy(x => x).ToList()
            : [which];
        double D(PARAM.Row? r, string f) => Ds1DrawParam.Get(r, f);
        string Lights(PARAM.Row r) => string.Join(" ", Enumerable.Range(0, 3).Where(i => D(r, $"colA_{i}") > 0).Select(i => $"L{i}({D(r, $"degRotX_{i}")},{D(r, $"degRotY_{i}")})A{D(r, $"colA_{i}")}")) is var s && s.Length > 0 ? s : "no lights";
        bool Same(PARAM.Row a, PARAM.Row b) => a.Cells.Zip(b.Cells).All(c => Equals(c.First.Value, c.Second.Value));
        var totals = new SortedDictionary<string, int>();
        void Count(string k) => totals[k] = totals.GetValueOrDefault(k) + 1;
        foreach (string area in areas)
        {
            Ds1DrawParam dp;
            try { dp = new Ds1DrawParam(root, area); } catch (Exception ex) { Console.WriteLine($"== a{area}: {ex.Message}"); continue; }
            Console.WriteLine($"== a{area}");
            var maps = new List<(string Name, MSB1 Msb)>();
            foreach (string f in Directory.GetFiles(Path.Combine(root, "map", "MapStudio"), $"m{area}_*.msb"))
                try { maps.Add((Path.GetFileNameWithoutExtension(f), MSB1.Read(f))); }
                catch (Exception ex) { Console.WriteLine($"  {Path.GetFileName(f)}: not read ({ex.GetType().Name})"); } // m99 test map
            var colIds = new HashSet<int>(); var pieceIds = new HashSet<int>();
            foreach (var (name, msb) in maps)
            {
                string U(IEnumerable<MSB1.Part> parts) => string.Join(" ", parts.GroupBy(p => (int)p.LightID).OrderBy(g => g.Key).Select(g => $"{g.Key}x{g.Count()}"));
                var cols = msb.Parts.Collisions.Where(c => c.HitFilterID == 8).ToList();
                foreach (var c in cols) colIds.Add(c.LightID);
                foreach (var p in msb.Parts.MapPieces) pieceIds.Add(p.LightID);
                Console.WriteLine($"  {name}: collisions {U(cols)} | pieces {U(msb.Parts.MapPieces)} | objects {U(msb.Parts.Objects)} | enemies {U(msb.Parts.Enemies)}");
                foreach (var g in cols.GroupBy(c => (c.LightID, c.ShadowID)).OrderBy(g => g.Key))
                {
                    var s = dp.Row("s_LightBank", g.Key.LightID); var sh = dp.Row("ShadowBank", g.Key.ShadowID);
                    if (s == null || sh == null) { Console.WriteLine($"    collision L{g.Key.LightID}/Sh{g.Key.ShadowID}: s_ row {(s == null ? "missing" : "ok")}, shadow row {(sh == null ? "missing" : "ok")}"); Count(s == null ? "collision LightID without s_ row" : "collision ShadowID without row"); continue; }
                    bool along = D(s, "degRotX_0") == D(sh, "lightDegRotX") && D(s, "degRotY_0") == D(sh, "lightDegRotY");
                    Count(along ? "collision zones: s_ L0 = ShadowBank direction" : "collision zones: s_ L0 != ShadowBank direction");
                    if (!along) Console.WriteLine($"    collision L{g.Key.LightID}/Sh{g.Key.ShadowID}: s_ '{s.Name}' L0 ({D(s, "degRotX_0")},{D(s, "degRotY_0")}) vs shadow '{sh.Name}' ({D(sh, "lightDegRotX")},{D(sh, "lightDegRotY")})");
                }
            }
            foreach (string bank in new[] { "LightBank", "s_LightBank" })
            {
                if (!dp.Banks.TryGetValue(bank, out var p)) continue;
                var def = p.Rows.FirstOrDefault(r => r.ID == 0);
                foreach (var r in p.Rows.Where(r => r.ID != 0 && (def == null || !Same(r, def) || !string.IsNullOrEmpty(r.Name))))
                {
                    string use = (colIds.Contains(r.ID) ? "C" : "-") + (pieceIds.Contains(r.ID) ? "P" : "-");
                    bool english = !string.IsNullOrEmpty(r.Name) && r.Name.All(ch => ch < 0x80);
                    bool nonGame = Ds1DrawParam.IsNonGameplayRow(r);
                    var twin = dp.Row(bank == "LightBank" ? "s_LightBank" : "LightBank", r.ID);
                    bool chara = System.Text.RegularExpressions.Regex.IsMatch(r.Name ?? "", "キャラ|chara|character", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (bank == "LightBank" && colIds.Contains(r.ID)) Count($"LightBank rows used by collisions: name {(chara ? "says character (キャラ/character)" : "does not say character")}");
                    if (bank == "LightBank" && pieceIds.Contains(r.ID)) Count($"LightBank rows used by map pieces: name {(chara ? "says character (キャラ/character)" : "does not say character")}");
                    if (bank == "s_LightBank")
                    {
                        Count($"s_ rows (non-default): specular colA_s {(D(r, "colA_s") == 100 ? "= 100" : "!= 100")}");
                        Count($"s_ rows (non-default): {(english ? "English name" : "Japanese/empty name")}");
                        if (nonGame) Count("s_ rows: cutscene/SFX name");
                        if (colIds.Contains(r.ID)) Count(nonGame ? "s_ rows used by collisions: cutscene/SFX" : "s_ rows used by collisions: gameplay name");
                        if (colIds.Contains(r.ID) && !nonGame) Count(D(r, "colA_1") == 0 && D(r, "colA_2") == 0 ? "gameplay s_ rows used by collisions: one light (L0)" : "gameplay s_ rows used by collisions: 2-3 lights");
                    }
                    else if (colIds.Contains(r.ID))
                    {
                        bool dark = Enumerable.Range(0, 3).All(i => D(r, $"colA_{i}") <= 0);
                        Count(dark ? "LightBank rows used by collisions: no directional light (all colA 0)" : "LightBank rows used by collisions: lit");
                    }
                    Console.WriteLine($"  {bank,-11} {r.ID,3} [{use}] {(nonGame ? "NONGAME " : "")}'{r.Name}' {Lights(r)} hemi u{D(r, "colA_u")} d{D(r, "colA_d")} spc{D(r, "colA_s")} env {D(r, "envDif_colA")}/{D(r, "envSpc_colA")}{(twin == null ? $" (no {(bank == "LightBank" ? "s_" : "")}LightBank twin)" : "")}");
                }
            }
        }
        Console.WriteLine("== totals");
        foreach (var (k, v) in totals) Console.WriteLine($"  {k}: {v}");
        return 0;
    }

    /// <summary>
    /// compare DS1MAP PACK_FLTPARAM [PACK_MSB]: per filter id of our map, DS1's source values (the zone's bank rows, read
    /// from DSR) next to the values read back from the built fltparam, and Things Betwixt's filter 0 (what every
    /// collision of the session-7 pack used). With the pack MSB, the zone of each filter id comes from the collisions'
    /// FilterIDs and their DS1 parts (name prefix); otherwise from a fresh plan.
    /// </summary>
    static int Compare(string map, string fltPath, string? msbPath)
    {
        string root = Program.DefaultDsrRoot, data = ExtractDs2.DefaultOut;
        var msb1 = MSB1.Read(Path.Combine(root, "map", "MapStudio", map + ".msb"));
        var dp = new Ds1DrawParam(root, map.Substring(1, 2));
        var tris = Atmosphere.CollisionTriangles(root, map);
        var plan = Atmosphere.Zones(msb1.Parts.Collisions.Where(c => c.HitFilterID == 8 && !c.DisableStart), [], p => tris.GetValueOrDefault(p.ModelName, 1), dp);
        var ours = GPARAM.Read(fltPath);
        var tb = GPARAM.Read(Path.Combine(data, "filter", "filter_m10_02_00_00.fltparam"));
        if (msbPath != null)
        {
            var used = MSB2.Read(msbPath).Parts.Collisions.GroupBy(c => (int)c.UnkT13).ToDictionary(g => g.Key, g => g.Count());
            Console.WriteLine("pack MSB collisions per FilterID: " + string.Join(" ", used.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}")));
        }
        static string F(object? o) => o switch
        {
            float f => f.ToString("0.###"),
            Vector2 v => $"({v.X * 180 / Math.PI:0},{v.Y * 180 / Math.PI:0})°",
            Vector4 v => $"({v.X:0.##},{v.Y:0.##},{v.Z:0.##})×{v.W:0.###}",
            byte[] b => "#" + Convert.ToHexString(b),
            bool b => b ? "on" : "off",
            null => "-",
            _ => o.ToString() ?? "",
        };
        double D(PARAM.Row? r, string f) => Ds1DrawParam.Get(r, f);
        Console.WriteLine("| filter | quantity | DS1 source (DSR DrawParam) | rule | ours (pack fltparam) | session 7 (TB filter 0) |");
        Console.WriteLine("|---|---|---|---|---|---|");
        foreach (var z in plan.Zones)
        {
            int id = z.Filter;
            var (s, sId) = dp.StageLight(z.Light); var l = dp.Row("LightBank", z.Light); var g = dp.Row("FogBank", z.Fog);
            var tc = dp.Row("ToneCorrectBank", z.ToneCorrect); var tm = dp.Row("ToneMapBank", z.ToneMap); var dof = dp.Row("DofBank", z.Dof); var sh = dp.Row("ShadowBank", z.Shadow);
            void Row(string q, string ds1, string rule, object? o, object? t) => Console.WriteLine($"| {id} | {q} | {ds1} | {rule} | {F(o)} | {F(t)} |");
            string key(PARAM.Row? r) { if (r == null) return "no row"; int k = Enumerable.Range(0, 3).OrderByDescending(i => D(r, $"colA_{i}")).First(); return $"light {k}: ({D(r, $"degRotX_{k}")},{D(r, $"degRotY_{k}")})° rgb({D(r, $"colR_{k}")},{D(r, $"colG_{k}")},{D(r, $"colB_{k}")}) A {D(r, $"colA_{k}")}"; }
            Row("map light (平行ライト 0) angle+diffuse", $"s_LightBank {sId}{(sId != z.Light ? $" (for LightBank {z.Light}: s_ row {(dp.Row("s_LightBank", z.Light) is { } own ? $"'{own.Name}' is a cutscene/effect row" : "missing")})" : "")} '{s?.Name}' {key(s)}", "deg→rad, rgb/255, A/256", $"{F(Atmosphere.Get(ours, "平行ライト", "Angle 0", id))} {F(Atmosphere.Get(ours, "平行ライト", "DiffColor 0", id))}", $"{F(Atmosphere.Get(tb, "平行ライト", "Angle 0", 0))} {F(Atmosphere.Get(tb, "平行ライト", "DiffColor 0", 0))}");
            Row("character (平行ライト 10)", $"LightBank {z.Light} '{l?.Name}' {key(l)}", "key light; fill → hemi", $"{F(Atmosphere.Get(ours, "平行ライト", "Angle 10", id))} {F(Atmosphere.Get(ours, "平行ライト", "DiffColor 10", id))}", $"{F(Atmosphere.Get(tb, "平行ライト", "Angle 10", 0))} {F(Atmosphere.Get(tb, "平行ライト", "DiffColor 10", 0))}");
            Row("hemisphere up/down (AddHemi 0)", s == null ? "no row" : $"u rgb({D(s, "colR_u")},{D(s, "colG_u")},{D(s, "colB_u")}) A {D(s, "colA_u")} / d A {D(s, "colA_d")}", "A/256 (+fill)", $"{F(Atmosphere.Get(ours, "平行ライト", "AddHemiColorUp 0", id))} / {F(Atmosphere.Get(ours, "平行ライト", "AddHemiColorDown 0", id))}", $"{F(Atmosphere.Get(tb, "平行ライト", "AddHemiColorUp 0", 0))} / {F(Atmosphere.Get(tb, "平行ライト", "AddHemiColorDown 0", 0))}");
            var scat = dp.Row("LightScatteringBank", z.Scatter);
            Row("fog colour", g == null ? "no row" : $"FogBank {z.Fog} '{g.Name}' rgb({D(g, "colR")},{D(g, "colG")},{D(g, "colB")}) A {D(g, "colA")}; scatter {z.Scatter} '{scat?.Name}' at {D(g, "fogEndZ")} m: {Atmosphere.Scatter(scat, (float)Math.Max(1, D(g, "fogEndZ")))}", "rgb×A/100, then DS1 scattering at the fog end (H1, skylight)", Atmosphere.Get(ours, "フォグ", "色", id), Atmosphere.Get(tb, "フォグ", "色", 0));
            Row("fog distance / alpha", g == null ? "no row" : $"begin {D(g, "fogBeginZ")} end {D(g, "fogEndZ")} m, max density {D(g, "degRotW")} %", "end m, density/100", $"{F(Atmosphere.Get(ours, "フォグ", "距離", id))} m / {F(Atmosphere.Get(ours, "フォグ", "α", id))}", $"{F(Atmosphere.Get(tb, "フォグ", "距離", 0))} m / {F(Atmosphere.Get(tb, "フォグ", "α", 0))}");
            Row("colour grading mode / palette (hsb)", tc == null ? "no row" : $"ToneCorrect {z.ToneCorrect} '{tc.Name}' hue {D(tc, "hue")} sat {D(tc, "saturation")} bright {D(tc, "brightnessR")} (contrast {D(tc, "contrastR")})", "mode 3 + own palette 7AARRR (A7b, skylight)", $"{F(Atmosphere.Get(ours, "カラーグレーディング", "変換モード", id))} / {F(Atmosphere.Get(ours, "カラーグレーディング", "パレットファイルID", id))} ({F(Atmosphere.Get(ours, "カラーグレーディング", "色相", id))}/{F(Atmosphere.Get(ours, "カラーグレーディング", "彩度", id))}/{F(Atmosphere.Get(ours, "カラーグレーディング", "明度", id))})", $"{F(Atmosphere.Get(tb, "カラーグレーディング", "変換モード", 0))} / {F(Atmosphere.Get(tb, "カラーグレーディング", "パレットファイルID", 0))} ({F(Atmosphere.Get(tb, "カラーグレーディング", "色相", 0))}/{F(Atmosphere.Get(tb, "カラーグレーディング", "彩度", 0))}/{F(Atmosphere.Get(tb, "カラーグレーディング", "明度", 0))})");
            Row("exposure: adapt on / min–max / bright sensitivity / ToneMap AvgLuminance", tm == null ? "no row" : $"ToneMap {z.ToneMap} grayKey {D(tm, "grayKeyValue")} adapted {D(tm, "minAdaptedLum")}–{D(tm, "maxAdapredLum")} speed {D(tm, "adaptSpeed")}", "DS1's band and speed, sensitivities 1, Avg = 1/(τ·key) (A8c, skylight fix pass)", $"{F(Atmosphere.Get(ours, "動的順応", "動的順応有効", id))} / {F(Atmosphere.Get(ours, "動的順応", "順応最低輝度", id))}–{F(Atmosphere.Get(ours, "動的順応", "順応最高輝度", id))} / {F(Atmosphere.Get(ours, "動的順応", "順応感度-明", id))} / {F(Atmosphere.Get(ours, "ToneMap", "AvgLuminance", id))}", $"{F(Atmosphere.Get(tb, "動的順応", "動的順応有効", 0))} / {F(Atmosphere.Get(tb, "動的順応", "順応最低輝度", 0))}–{F(Atmosphere.Get(tb, "動的順応", "順応最高輝度", 0))} / {F(Atmosphere.Get(tb, "動的順応", "順応感度-明", 0))} / {F(Atmosphere.Get(tb, "ToneMap", "AvgLuminance", 0))}");
            Row("bloom / light shafts", tm == null ? "no row" : $"ToneMap {z.ToneMap} '{tm.Name}' bloomBegin {D(tm, "bloomBegin")} mul {D(tm, "bloomMul")}, shaftPower {D(tm, "lightShaftPower")}", "mul>0 & begin<100; power>0", $"{F(Atmosphere.Get(ours, "Bloom", "Bloom有効", id))} / {F(Atmosphere.Get(ours, "ライトシャフト", "ライトシャフト有効", id))}", $"{F(Atmosphere.Get(tb, "Bloom", "Bloom有効", 0))} / {F(Atmosphere.Get(tb, "ライトシャフト", "ライトシャフト有効", 0))}");
            Row("depth of field far", dof == null ? "no row" : $"Dof {z.Dof} '{dof.Name}' far {D(dof, "farDofBegin")}..{D(dof, "farDofEnd")} m ×{D(dof, "farDofMul")}%", "start, end, alpha", $"{F(Atmosphere.Get(ours, "LensSimulation", "FarStart", id))}..{F(Atmosphere.Get(ours, "LensSimulation", "FarEnd", id))} α {F(Atmosphere.Get(ours, "LensSimulation", "DOF Alpha", id))}", $"{F(Atmosphere.Get(tb, "LensSimulation", "FarStart", 0))}..{F(Atmosphere.Get(tb, "LensSimulation", "FarEnd", 0))} α {F(Atmosphere.Get(tb, "LensSimulation", "DOF Alpha", 0))}");
            Row("shadow fade / colour", sh == null ? "no row" : $"Shadow {z.Shadow} '{sh.Name}' fade {D(sh, "fadeBeginDist")}+{D(sh, "fadeDist")} m rgb({D(sh, "colR")},{D(sh, "colG")},{D(sh, "colB")}) {D(sh, "densityRatio")}%", "fade m; rgb + density·2.55", $"{F(Atmosphere.Get(ours, "カスケードシャドウ", "影終了フェード開始距離[m]", id))}+{F(Atmosphere.Get(ours, "カスケードシャドウ", "影終了フェード距離[m]", id))} {F(Atmosphere.Get(ours, "カスケードシャドウ", "Cascade 影の色", id))}", $"{F(Atmosphere.Get(tb, "カスケードシャドウ", "影終了フェード開始距離[m]", 0))}+{F(Atmosphere.Get(tb, "カスケードシャドウ", "影終了フェード距離[m]", 0))} {F(Atmosphere.Get(tb, "カスケードシャドウ", "Cascade 影の色", 0))}");
            // session-8 fix pass: every cascade block, bloom alpha, the probe/GI read-out fields (rule A3b)
            Row("shadow fade start, every cascade value of the id", sh == null ? "no row" : $"fadeBeginDist {D(sh, "fadeBeginDist")} m", "all 4 blocks", All(ours, "カスケードシャドウ", "影終了フェード開始距離[m]", id), All(tb, "カスケードシャドウ", "影終了フェード開始距離[m]", 0));
            Row("bloom alpha", tm == null ? "no row" : $"bloomMul {D(tm, "bloomMul")}", "fallback α × mul/100 (once)", Atmosphere.Get(ours, "Bloom", "ブルームアルファ", id), Atmosphere.Get(tb, "Bloom", "ブルームアルファ", 0));
            Row("probe read-out: PointCloud Offset 0 / Offset 10 / GI Color Scale 0", "(our probes / DS1 lightmaps)", "TB filter 0 (A3b)", $"{F(Atmosphere.Get(ours, "平行ライト", "PointCloud Offset 0", id))} / {F(Atmosphere.Get(ours, "平行ライト", "PointCloud Offset 10", id))} / {F(Atmosphere.Get(ours, "平行ライト", "GI Color Scale 0", id))}", $"{F(Atmosphere.Get(tb, "平行ライト", "PointCloud Offset 0", 0))} / {F(Atmosphere.Get(tb, "平行ライト", "PointCloud Offset 10", 0))} / {F(Atmosphere.Get(tb, "平行ライト", "GI Color Scale 0", 0))}");
        }
        return 0;
        string All(GPARAM g, string group, string param, int id) => string.Join(",", g.Groups.Where(x => x.Name1 == group).SelectMany(x => x.Params).Where(p => p.Name1 == param)
            .SelectMany(p => Enumerable.Range(0, p.Values.Count).Where(i => p.ValueIDs[i] == id).Select(i => F(p.Values[i]))));
    }

    /// <summary>spot DS1MAP X Y Z [X Y Z ...]: the ordinary DS1 collision under each point (highest triangle below it,
    /// within 6 m) and the DS2 filter id its zone gets (for in-game test positions).</summary>
    static int Spot(string map, float[] xyz)
    {
        string root = Program.DefaultDsrRoot;
        var msb = MSB1.Read(Path.Combine(root, "map", "MapStudio", map + ".msb"));
        var dp = new Ds1DrawParam(root, map.Substring(1, 2));
        var tris = Atmosphere.CollisionTriangles(root, map);
        var placed = msb.Parts.Collisions.Where(c => c.HitFilterID == 8 && !c.DisableStart).ToList();
        var plan = Atmosphere.Zones(placed, [], p => tris.GetValueOrDefault(p.ModelName, 1), dp);
        string dir = Path.Combine(root, "map", map);
        var bxf = BXF3.Read(Path.Combine(dir, $"h{map[1..]}.hkxbhd"), Path.Combine(dir, $"h{map[1..]}.hkxbdt"));
        var world = new List<(Vector3 A, Vector3 B, Vector3 C, string Part)>();
        foreach (var part in placed)
        {
            var f = bxf.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith($"h{part.ModelName[1..]}A", StringComparison.OrdinalIgnoreCase));
            if (f == null) continue;
            var m = ColSurvey.Place(part.Position, part.Rotation, part.Scale);
            foreach (var (a, b, c) in ColSurvey.Triangles(DCX.Decompress(f.Bytes)))
                world.Add((Vector3.Transform(a, m), Vector3.Transform(b, m), Vector3.Transform(c, m), part.Name));
        }
        for (int i = 0; i + 2 < xyz.Length; i += 3)
        {
            var p = new Vector3(xyz[i], xyz[i + 1], xyz[i + 2]);
            (float Y, string Part)? best = null;
            foreach (var t in world)
            {
                // barycentric test in XZ, height of the triangle plane at the point
                float d = (t.B.Z - t.C.Z) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Z - t.C.Z);
                if (Math.Abs(d) < 1e-6f) continue;
                float u = ((t.B.Z - t.C.Z) * (p.X - t.C.X) + (t.C.X - t.B.X) * (p.Z - t.C.Z)) / d;
                float v = ((t.C.Z - t.A.Z) * (p.X - t.C.X) + (t.A.X - t.C.X) * (p.Z - t.C.Z)) / d;
                if (u < 0 || v < 0 || u + v > 1) continue;
                float y = u * t.A.Y + v * t.B.Y + (1 - u - v) * t.C.Y;
                if (y > p.Y + 0.5f || y < p.Y - 6) continue;
                if (best == null || y > best.Value.Y) best = (y, t.Part);
            }
            Console.WriteLine(best == null ? $"{V(p)}: no ordinary DS1 collision below"
                : $"{V(p)}: on {best.Value.Part} (y {best.Value.Y:0.00}) → filter {plan.ByPart[best.Value.Part].Filter} ({plan.ByPart[best.Value.Part]})");
        }
        return 0;
    }

    /// <summary>zonebox DS1MAP (session-8 fix pass): per zone (= DS2 filter id) the world box of its DS1 collisions and a
    /// floor point (centre of the upward-facing triangle nearest the box centre), for in-game test positions.</summary>
    static int ZoneBox(string map)
    {
        string root = Program.DefaultDsrRoot;
        var msb = MSB1.Read(Path.Combine(root, "map", "MapStudio", map + ".msb"));
        var dp = new Ds1DrawParam(root, map.Substring(1, 2));
        var tris = Atmosphere.CollisionTriangles(root, map);
        var placed = msb.Parts.Collisions.Where(c => c.HitFilterID == 8 && !c.DisableStart).ToList();
        var plan = Atmosphere.Zones(placed, [], p => tris.GetValueOrDefault(p.ModelName, 1), dp);
        string dir = Path.Combine(root, "map", map);
        var bxf = BXF3.Read(Path.Combine(dir, $"h{map[1..]}.hkxbhd"), Path.Combine(dir, $"h{map[1..]}.hkxbdt"));
        foreach (var z in plan.Zones)
        {
            var world = new List<(Vector3 A, Vector3 B, Vector3 C)>();
            foreach (var part in placed.Where(p => z.Parts.Contains(p.Name)))
            {
                var f = bxf.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith($"h{part.ModelName[1..]}A", StringComparison.OrdinalIgnoreCase));
                if (f == null) continue;
                var m = ColSurvey.Place(part.Position, part.Rotation, part.Scale);
                foreach (var (a, b, c) in ColSurvey.Triangles(DCX.Decompress(f.Bytes)))
                    world.Add((Vector3.Transform(a, m), Vector3.Transform(b, m), Vector3.Transform(c, m)));
            }
            if (world.Count == 0) { Console.WriteLine($"filter {z.Filter}: no triangles"); continue; }
            var lo = world.Select(t => Vector3.Min(t.A, Vector3.Min(t.B, t.C))).Aggregate(Vector3.Min);
            var hi = world.Select(t => Vector3.Max(t.A, Vector3.Max(t.B, t.C))).Aggregate(Vector3.Max);
            var mid = (lo + hi) / 2;
            var floor = world.Where(t => Vector3.Normalize(Vector3.Cross(t.B - t.A, t.C - t.A)) is var n && Math.Abs(n.Y) > 0.8f)
                .Select(t => (t.A + t.B + t.C) / 3).OrderBy(c => (c.X - mid.X) * (c.X - mid.X) + (c.Z - mid.Z) * (c.Z - mid.Z)).Cast<Vector3?>().FirstOrDefault();
            Console.WriteLine($"filter {z.Filter} (light {z.Light} fog {z.Fog}): box {V(lo)}..{V(hi)}, floor point {(floor == null ? "-" : V(floor.Value))} ({string.Join(" ", z.Parts)})");
        }
        return 0;
    }

    /// <summary>envtest x: DS2 CubeEnv pixel statistics per face (is alpha used, RGBM?) and what Pfim makes of a DS1
    /// BC6H cubemap (size, stride, value range).</summary>
    static int EnvTest()
    {
        foreach (string map in new[] { "m10_02_00_00", "m10_04_00_00" })
        {
            var bnd = BND4.Read(Path.Combine(ExtractDs2.DefaultOut, "model", "map", $"e{map[1..]}.envbnd.dcx"));
            var t = TPF.Read(bnd.Files[0].Bytes).Textures[0];
            byte[] b = t.Bytes;
            int w = BitConverter.ToInt32(b, 16), h = BitConverter.ToInt32(b, 12), mips = BitConverter.ToInt32(b, 28);
            int faceBytes = 0; for (int m = 0, s = w; m < mips; m++, s = Math.Max(1, s / 2)) faceBytes += Math.Max(1, (s + 3) / 4) * Math.Max(1, (s + 3) / 4) * 16;
            Console.WriteLine($"{map} {t.Name}: {w}x{h} mips {mips} faceBytes {faceBytes} total {b.Length - 128} (6 faces = {6 * faceBytes})");
            for (int f = 0; f < 6; f++)
            {
                var img = Textures.Bcn.Decode(Textures.BcFormat.BC3, w, h, b.AsSpan(128 + f * faceBytes, w * h));
                var px = img.Pixels;
                Console.WriteLine($"  face {f}: R {px.Average(p => p.R):0} G {px.Average(p => p.G):0} B {px.Average(p => p.B):0} A min {px.Min(p => p.A)} max {px.Max(p => p.A)} mean {px.Average(p => p.A):0}");
            }
        }
        string dir = Path.Combine(Program.DefaultDsrRoot, "map", "m18");
        var bxf = BXF3.Read(Path.Combine(dir, "GI_EnvM_m18.tpfbhd"), Path.Combine(dir, "GI_EnvM_m18.tpfbdt"));
        var tex = TPF.Read(DCX.Decompress(bxf.Files.First(f => f.Name.Contains("m18_01_0000")).Bytes)).Textures;
        foreach (var t in tex)
        {
            using var img = Pfim.Pfimage.FromStream(new MemoryStream(t.Bytes));
            Console.WriteLine($"DS1 {t.Name}: Pfim {img.Width}x{img.Height} format {img.Format} stride {img.Stride} data {img.DataLen} bytes, mipmaps {img.MipMaps?.Length}");
            Console.WriteLine($"  first bytes {Convert.ToHexString(img.Data, 0, 32)}");
        }
        return 0;
    }

    /// <summary>units x: the intensity (W) of every colour of every 平行ライト param in every vanilla fltparam, times 256:
    /// how many are whole numbers (DS2 stores authored integer intensities as N/256), and which N occur.</summary>
    static int Units()
    {
        int all = 0, whole = 0;
        var ns = new SortedDictionary<int, int>();
        var odd = new List<string>();
        foreach (string f in Directory.GetFiles(Path.Combine(ExtractDs2.DefaultOut, "filter"), "*.fltparam"))
            foreach (var p in GPARAM.Read(f).Groups.Where(g => g.Name1 == "平行ライト").SelectMany(g => g.Params).Where(p => p.Type == GPARAM.ParamType.Float4 && p.Name1.Contains("Color")))
                foreach (var v in p.Values.Cast<Vector4>())
                {
                    all++;
                    double n = v.W * 256;
                    if (Math.Abs(n - Math.Round(n)) < 0.15 * Math.Max(1, n / 100)) { whole++; ns[(int)Math.Round(n)] = ns.GetValueOrDefault((int)Math.Round(n)) + 1; }
                    else if (odd.Count < 12) odd.Add($"{Path.GetFileName(f)} {p.Name1} W={v.W}");
                }
        Console.WriteLine($"{whole}/{all} intensities are whole multiples of 1/256");
        Console.WriteLine("N (count): " + string.Join(" ", ns.Select(kv => $"{kv.Key}({kv.Value})")));
        foreach (string o in odd) Console.WriteLine("  not whole: " + o);
        return 0;
    }

    /// <summary>plan DS1MAP: what MapBuilder's atmosphere translation will do (zones of the ordinary DS1 collisions,
    /// calibration, best-fit DS2 areas, converted values per zone) without building a pack.</summary>
    static int PlanFor(string map)
    {
        string root = Program.DefaultDsrRoot, data = ExtractDs2.DefaultOut;
        var msb = MSB1.Read(Path.Combine(root, "map", "MapStudio", map + ".msb"));
        var dp = new Ds1DrawParam(root, map.Substring(1, 2));
        var log = new List<string>();
        var tris = Atmosphere.CollisionTriangles(root, map);
        var plan = Atmosphere.Zones(msb.Parts.Collisions.Where(c => c.HitFilterID == 8 && !c.DisableStart), log, p => tris.GetValueOrDefault(p.ModelName, 1), dp);
        var cal = Atmosphere.Calibrate(root, data);
        foreach (string l in log) Console.WriteLine(l);
        Console.WriteLine($"calibration: {cal.Report}");
        var ranking = Atmosphere.BestTemplate(data, Atmosphere.Ds1Features(dp, plan.Main, cal));
        Console.WriteLine("best-fit DS2 areas: " + string.Join(", ", ranking.Take(10).Select(r => $"{r.Map}/{r.Filter} d={r.Distance:0.00}")));
        Console.WriteLine($"  Things Betwixt: d={ranking.FirstOrDefault(r => r.Map == "m10_02_00_00").Distance:0.00}");
        foreach (var z in plan.Zones)
        {
            var sun = Atmosphere.LightRig(dp.StageLight(z.Light).Row, cal.Sun, cal.Hemi);
            var ch = Atmosphere.LightRig(dp.Row("LightBank", z.Light), cal.Chara, cal.CharaHemi);
            var fog = Atmosphere.Fog(dp.Row("FogBank", z.Fog), cal.FogDistance);
            Console.WriteLine($"  filter {z.Filter}: sun {sun.Diffuse} angle {sun.Angle} hemi {sun.HemiUp}/{sun.HemiDown}; character {ch.Diffuse} angle {ch.Angle}; fog {Convert.ToHexString(fog.Color)} {fog.Distance:0.#} α {fog.Alpha:0.##}");
        }
        return 0;
    }

    /// <summary>flt-roundtrip x: every vanilla fltparam read and rewritten by SoulsFormats' GPARAM, compared byte for byte
    /// (the builder edits values in place, so the writer must reproduce DS2's layout).</summary>
    static int FltRoundTrip(string outDir)
    {
        int same = 0, all = 0;
        foreach (string f in Directory.GetFiles(Path.Combine(ExtractDs2.DefaultOut, "filter"), "*.fltparam"))
        {
            byte[] a = File.ReadAllBytes(f), b = GPARAM.Read(a).Write();
            bool eq = a.AsSpan().SequenceEqual(b);
            if (!eq && Directory.Exists(outDir)) File.WriteAllBytes(Path.Combine(outDir, Path.GetFileName(f)), b);
            all++; if (eq) same++;
            int diff = eq ? -1 : Enumerable.Range(0, Math.Min(a.Length, b.Length)).FirstOrDefault(i => a[i] != b[i], Math.Min(a.Length, b.Length));
            Console.WriteLine($"  {Path.GetFileName(f)} {a.Length} -> {b.Length} {(eq ? "identical" : $"DIFFERENT (first at 0x{diff:X})")}");
        }
        Console.WriteLine($"{same}/{all} identical");
        return same == all ? 0 : 1;
    }

    /// <summary>env AREA|MAP: DS1 GI_EnvM_mAA cubemaps (DSR map\mAA) or a DS2 envbnd's CubeEnv TPFs.</summary>
    static int Env(string which)
    {
        void Tpf(string label, TPF tpf)
        {
            foreach (var t in tpf.Textures)
            {
                byte[] b = t.Bytes;
                bool isDds = b.Length > 128 && b[0] == 'D' && b[1] == 'D' && b[2] == 'S';
                string fourcc = isDds ? System.Text.Encoding.ASCII.GetString(b, 84, 4) : "?";
                int w = isDds ? BitConverter.ToInt32(b, 16) : 0, h = isDds ? BitConverter.ToInt32(b, 12) : 0;
                bool cube = isDds && ((BitConverter.ToInt32(b, 112) & 0x200) != 0 || (fourcc == "DX10" && (BitConverter.ToInt32(b, 136) & 4) != 0));
                string dxgi = fourcc == "DX10" ? $" dxgi={BitConverter.ToInt32(b, 128)}" : "";
                Console.WriteLine($"  {label} {t.Name,-28} fmt={t.Format} type={t.Type} mips={t.Mipmaps} flags1={t.Flags1} {w}x{h} fourcc={fourcc}{dxgi} cube={cube} bytes={b.Length}");
            }
        }
        if (which.Length == 2)
        {
            string dir = Path.Combine(Program.DefaultDsrRoot, "map", "m" + which);
            var bxf = BXF3.Read(Path.Combine(dir, $"GI_EnvM_m{which}.tpfbhd"), Path.Combine(dir, $"GI_EnvM_m{which}.tpfbdt"));
            foreach (var f in bxf.Files) Tpf(Path.GetFileName(f.Name), TPF.Read(DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes));
        }
        else
        {
            var bnd = BND4.Read(Path.Combine(ExtractDs2.DefaultOut, "model", "map", $"e{which[1..]}.envbnd.dcx"));
            foreach (var f in bnd.Files) Tpf(f.Name, TPF.Read(f.Bytes));
        }
        return 0;
    }

    static string V(Vector3 v) => $"({v.X:0.00}, {v.Y:0.00}, {v.Z:0.00})";
    static string Shape(MSB.Shape s) => s switch
    {
        MSB.Shape.Box b => $"box {b.Width:0.##}x{b.Height:0.##}x{b.Depth:0.##}",
        MSB.Shape.Sphere sp => $"sphere r={sp.Radius:0.##}",
        MSB.Shape.Cylinder cy => $"cyl r={cy.Radius:0.##} h={cy.Height:0.##}",
        MSB.Shape.Circle ci => $"circle r={ci.Radius:0.##}",
        MSB.Shape.Rectangle re => $"rect {re.Width:0.##}x{re.Depth:0.##}",
        _ => s.GetType().Name,
    };

    static int Ds1(string map, bool banks)
    {
        string root = Program.DefaultDsrRoot;
        var msb = MSB1.Read(Path.Combine(root, "map", "MapStudio", map + ".msb"));
        var dp = new Ds1DrawParam(root, map.Substring(1, 2));
        Console.WriteLine($"== {map}: draw banks in a{dp.Area}_DrawParam: " + string.Join(", ", dp.Banks.Select(b => $"{b.Key}({b.Value.Rows.Count} rows)")));
        Console.WriteLine("-- collisions: name hitFilter light fog scatter flare shadow dof tone toneCorr lantern lod envSpot sound pos");
        foreach (var c in msb.Parts.Collisions)
            Console.WriteLine($"  {c.Name,-14} hf={c.HitFilterID,-2} L={c.LightID,-3} F={c.FogID,-3} S={c.ScatterID,-3} LF={c.LensFlareID,-3} Sh={c.ShadowID,-3} D={c.DofID,-3} TM={c.ToneMapID,-3} TC={c.ToneCorrectID,-3} Ln={c.LanternID,-3} Lod={c.LodParamID,-3} env={c.EnvLightMapSpotIndex,-3} snd={c.SoundSpaceType} refl={c.ReflectPlaneHeight:0.##} {V(c.Position)}");
        var ids = new (string Bank, Func<MSB1.Part, byte> Id)[]
        {
            ("LightBank", p => p.LightID), ("FogBank", p => p.FogID), ("LightScatteringBank", p => p.ScatterID), ("LensFlareBank", p => p.LensFlareID),
            ("ShadowBank", p => p.ShadowID), ("DofBank", p => p.DofID), ("ToneMapBank", p => p.ToneMapID), ("ToneCorrectBank", p => p.ToneCorrectID),
        };
        Console.WriteLine("-- id usage over ordinary collisions (hit filter 8), most used first; map pieces too");
        foreach (var (bank, id) in ids)
        {
            var use = Ds1DrawParam.Usage(msb, id);
            var pieces = msb.Parts.MapPieces.GroupBy(p => (int)id(p)).Select(g => $"{g.Key}x{g.Count()}");
            Console.WriteLine($"  {bank,-20} collisions: {string.Join(" ", use.Select(u => $"{u.Id}x{u.Parts}"))}   pieces: {string.Join(" ", pieces)}");
        }
        Console.WriteLine("-- bank rows used");
        foreach (var (bank, id) in ids.Append(("s_LightBank", p => p.LightID)).Append(("PointLightBank", p => p.LanternID)))
            foreach (int rowId in msb.Parts.Collisions.Select(c => (int)id(c)).Distinct().OrderBy(x => x))
            {
                var row = dp.Row(bank, rowId);
                if (row == null) { Console.WriteLine($"  {bank} {rowId}: (no row)"); continue; }
                string cells = banks ? string.Join(" ", row.Cells.Where(c => !c.Def.InternalName.StartsWith("pad") && !c.Def.InternalName.StartsWith("dummy")).Select(c => $"{c.Def.InternalName}={c.Value}")) : "";
                Console.WriteLine($"  {bank} {rowId} '{row.Name}' {cells}");
            }
        Console.WriteLine("-- MSB1 events");
        string Reg(string? name)
        {
            var r = msb.Regions.GetEntries().FirstOrDefault(x => x.Name == name);
            return r == null ? (name ?? "") : $"{r.Name} {V(r.Position)} rot={r.Rotation.Y:0} {Shape(r.Shape)}";
        }
        foreach (var e in msb.Events.Sounds) Console.WriteLine($"  Sound {e.Name,-24} type={e.SoundType} id={e.SoundID} entity={e.EntityID} region={Reg(e.RegionName)}");
        foreach (var e in msb.Events.SFX) Console.WriteLine($"  SFX   {e.Name,-24} ffx={e.EffectID} entity={e.EntityID} region={Reg(e.RegionName)} part={e.PartName}");
        foreach (var e in msb.Events.Wind) Console.WriteLine($"  Wind  {e.Name,-24} min={V(e.WindVecMin)} max={V(e.WindVecMax)} region={Reg(e.RegionName)}");
        foreach (var e in msb.Events.Environments) Console.WriteLine($"  Env   {e.Name,-24} {e.UnkT00} {e.UnkT04} {e.UnkT08} {e.UnkT0C} {e.UnkT10} {e.UnkT14} region={Reg(e.RegionName)}");
        foreach (var e in msb.Events.Lights) Console.WriteLine($"  Light {e.Name,-24} pointLight={e.PointLightID} region={Reg(e.RegionName)} part={e.PartName}");
        return 0;
    }

    static int Ds2(string which)
    {
        string data = ExtractDs2.DefaultOut;
        IEnumerable<string> files = which == "all"
            ? Directory.GetDirectories(Path.Combine(data, "map")).Select(d => Path.Combine(d, Path.GetFileName(d) + ".msb")).Where(File.Exists)
            : [which.EndsWith(".msb", StringComparison.OrdinalIgnoreCase) ? which : Path.Combine(data, "map", which, which + ".msb")];
        foreach (string f in files)
        {
            var msb = MSB2.Read(f);
            Console.WriteLine($"== {Path.GetFileNameWithoutExtension(f)}");
            static string C(System.Drawing.Color c) => $"({c.R},{c.G},{c.B},{c.A})";
            foreach (var l in msb.Events.Lights)
                Console.WriteLine($"  Light '{l.Name}' id={l.EventID} t00={l.UnkT00} ang0=({l.UnkT04:0.##},{l.UnkT08:0.##}) dif0={C(l.ColorT0C)} spc0={C(l.ColorT10)} ang1=({l.UnkT1C:0.##},{l.UnkT20:0.##}) dif1={C(l.ColorT24)} spc1={C(l.ColorT28)} hemiU={C(l.ColorT34)} hemiD={C(l.ColorT38)} envRefl={C(l.ColorT3C)} envStr={l.UnkT40:0.###} trackCam={l.UnkT44}");
            foreach (var g in msb.Events.Fogs)
                Console.WriteLine($"  Fog '{g.Name}' id={g.EventID} t00={g.UnkT00} col={C(g.ColorT04)} dist={g.UnkT08:0.##} hBase={g.UnkT0C:0.##} hDens={g.UnkT10:0.####} upper={g.UnkT14} alpha={g.UnkT15} contrast={g.UnkT16}");
            foreach (var b in msb.Events.BGColors) Console.WriteLine($"  BGColor '{b.Name}' id={b.EventID} col={C(b.Color)}");
            foreach (var s in msb.Events.Shadows) Console.WriteLine($"  Shadow '{s.Name}' id={s.EventID} {s.UnkT04:0.##} {s.UnkT08:0.##} {s.UnkT0C:0.##} {s.UnkT14:0.##} {s.UnkT18:0.##} {s.UnkT20:0.##} col={C(s.ColorT24)}");
            var cols = msb.Parts.Collisions;
            string H(Func<MSB2.Part.Collision, object> k) => string.Join(" ", cols.GroupBy(k).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}x{g.Count()}"));
            Console.WriteLine($"  collisions {cols.Count}: reverb(T10) {H(c => c.UnkT10)} | delay(T11) {H(c => c.UnkT11)} | filter(T13) {H(c => c.UnkT13)} | hit(T14) {H(c => c.UnkT14)} | sfxAttach(T15) {H(c => c.UnkT15)} | ambSfx(T18) {H(c => c.UnkT18)} | plLight(T1C) {H(c => c.UnkT1C)} | bright(T26,T27) {H(c => $"{c.UnkT26},{c.UnkT27}")} | T28 {H(c => c.UnkT28)} | T2C {H(c => c.UnkT2C)} | T2E {H(c => c.UnkT2E)} | T30 {H(c => c.UnkT30)} | T35 {H(c => c.UnkT35)} | T36 {H(c => c.UnkT36)}");
            foreach (var r in msb.Regions.Sounds) Console.WriteLine($"  Sound '{r.Name}' type={r.UnkT00} id={r.SoundID} t08={r.UnkT08} {V(r.Position)} {Shape(r.Shape)}");
            foreach (var r in msb.Regions.SFXs) Console.WriteLine($"  SFX '{r.Name}' ffx={r.EffectID} t04={r.UnkT04} {V(r.Position)} {Shape(r.Shape)}");
            foreach (var r in msb.Regions.EnvLights) Console.WriteLine($"  EnvLight '{r.Name}' t00={r.UnkT00} {r.UnkT04:0.##} {r.UnkT08:0.##} {V(r.Position)} {Shape(r.Shape)}");
        }
        return 0;
    }

    static int Flt(string which, string? group)
    {
        string path = File.Exists(which) ? which : Path.Combine(ExtractDs2.DefaultOut, "filter", $"filter_{which}.fltparam");
        var g = GPARAM.Read(path);
        Console.WriteLine($"== {Path.GetFileName(path)} game={g.Game} groups={g.Groups.Count} unk14={g.Unk14} unk40={g.Unk40} unk3s={g.Unk3s.Count}");
        foreach (var grp in g.Groups.Where(x => group == null || x.Name2 == group || x.Name1 == group))
        {
            Console.WriteLine($"-- {grp.Name2} ({grp.Name1}) params={grp.Params.Count}");
            foreach (var p in grp.Params)
            {
                static string Val(object o) => o switch
                {
                    float f => f.ToString("0.####"),
                    Vector2 v => $"({v.X:0.###},{v.Y:0.###})",
                    Vector3 v => $"({v.X:0.###},{v.Y:0.###},{v.Z:0.###})",
                    Vector4 v => $"({v.X:0.###},{v.Y:0.###},{v.Z:0.###},{v.W:0.###})",
                    System.Drawing.Color c => $"#({c.R},{c.G},{c.B},{c.A})",
                    byte[] b => Convert.ToHexString(b),
                    _ => o?.ToString() ?? "null",
                };
                Console.WriteLine($"   {p.Name1}|{p.Name2,-20} {p.Type,-8} n={p.Values.Count} " + string.Join(" ", p.Values.Select((v, i) => $"[{p.ValueIDs[i]}]{Val(v)}")));
            }
        }
        return 0;
    }
}
