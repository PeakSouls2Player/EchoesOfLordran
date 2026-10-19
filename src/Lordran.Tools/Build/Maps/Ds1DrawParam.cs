using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// DS1's per-area draw params (session 8, atmosphere track). DSR keeps one binder per map AREA:
/// <c>param\DrawParam\aAA_DrawParam.parambnd.dcx</c> = <c>mAA_{Dof,EnvLightTex,Fog,LensFlare,LensFlareEx,Light,
/// LightScattering,PointLight,Shadow,ToneCorrect,ToneMap}Bank.param</c> + <c>sAA_LightBank.param</c> (both maps of an
/// area share it: a18 = Kiln m18_00 + Asylum m18_01). Which row lights a spot is chosen by the collision the player
/// stands on: MSB1 parts carry LightID / FogID / ScatterID / LensFlareID / ShadowID / DofID / ToneMapID /
/// ToneCorrectID (one byte each = row id in the area's bank). Paramdefs: DSR's shipped paramdefbnd, then Smithbox DS1R.
/// Field meanings (Smithbox DS1R defs, Japanese descriptions): LIGHT_BANK = three parallel lights (degRotX/Y_0..2 in
/// degrees, colR/G/B 0..255, colA = intensity in percent), hemisphere ambient (col*_u upper, col*_d lower), a
/// specular light (*_s), env diffuse/specular tints (envDif_col*, envSpc_col*, percent) and the env cubemap ids
/// (envDif, envSpc_0..3 → EnvLightTexBank); FOG_BANK = fogBeginZ/fogEndZ (metres), degRotW (max density, percent),
/// colR/G/B/A; TONE_MAP_BANK = bloom, grayKeyValue, adaptation, light shafts; TONE_CORRECT_BANK = brightness /
/// contrast per channel, saturation, hue; LIGHT_SCATTERING_BANK = sun direction/colour + Rayleigh/Mie terms.
/// </summary>
public sealed class Ds1DrawParam
{
    public string Area { get; }
    /// <summary>Bank name without the area prefix ("LightBank", "s_LightBank", "FogBank", …) → param with defs applied.</summary>
    public Dictionary<string, PARAM> Banks { get; } = new(StringComparer.OrdinalIgnoreCase);

    static List<PARAMDEF>? defs;

    public Ds1DrawParam(string dsrRoot, string area)
    {
        Area = area; // "18"
        defs ??= LoadDefs(dsrRoot);
        string path = Path.Combine(dsrRoot, "param", "DrawParam", $"a{area}_DrawParam.parambnd.dcx");
        foreach (var f in BND3.Read(path).Files)
        {
            string stem = Path.GetFileNameWithoutExtension(f.Name);        // m18_LightBank / s18_LightBank
            string bank = stem[0] == 's' ? "s_" + stem[(stem.IndexOf('_') + 1)..] : stem[(stem.IndexOf('_') + 1)..];
            var p = PARAM.Read(f.Bytes);
            var def = defs.FirstOrDefault(d => d.ParamType == p.ParamType && d.GetRowSize() == p.DetectedSize);
            if (def == null && !p.ApplyParamdefCarefully(defs)) continue;
            if (def != null) p.ApplyParamdef(def);
            Banks[bank] = p;
        }
    }

    static List<PARAMDEF> LoadDefs(string dsrRoot)
    {
        var list = BND3.Read(Path.Combine(dsrRoot, @"paramdef\paramdef.paramdefbnd.dcx")).Files.Select(f => PARAMDEF.Read(f.Bytes)).ToList();
        list.AddRange(Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS1R", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)));
        return list;
    }

    /// <summary>Session 11 (burg11, round 8), rule H2-D: the draw params of a DS1 map as the merged map reads them — its
    /// area's banks plus, for an H2 primary, each secondary's area banks at the secondary's id offset
    /// (<see cref="H2Merge.DrawOffset"/>: the merged MSB moved the secondary's LightID / FogID … by it).</summary>
    public static Ds1DrawParam ForMap(string dsrRoot, string ds1Map)
    {
        var dp = new Ds1DrawParam(dsrRoot, ds1Map.Substring(1, 2));
        if (H2Merge.GroupOf(ds1Map) is { } g && g.Primary.StartsWith(ds1Map[..6], StringComparison.OrdinalIgnoreCase))
            foreach (string s in g.Secondaries)
            {
                int off = H2Merge.DrawOffset(s);
                if (off == 0 || dp.segments.Contains(off)) continue; // the primary's area, or an area already appended
                var sdp = new Ds1DrawParam(dsrRoot, s.Substring(1, 2));
                foreach (var (bank, p) in sdp.Banks)
                    if (dp.Banks.TryGetValue(bank, out var target))
                        foreach (var r in p.Rows) target.Rows.Add(new PARAM.Row(r) { ID = r.ID + off });
                dp.segments.Add(off);
            }
        return dp;
    }

    // rule H2-D: the first id of each appended secondary (a fallback row is looked for in the id's own segment only)
    readonly List<int> segments = [];
    (int Lo, int Hi) Segment(int id)
    {
        int lo = 0, hi = int.MaxValue;
        foreach (int s in segments.Order()) { if (id >= s) lo = s; else { hi = s; break; } }
        return (lo, hi);
    }

    public PARAM.Row? Row(string bank, int id) => Banks.TryGetValue(bank, out var p) ? p.Rows.FirstOrDefault(r => r.ID == id) : null;

    /// <summary>
    /// The s_LightBank row the atmosphere translation uses for a LightID (DS2 平行ライト slot 0, rule A4). What DS1's
    /// renderer reads s_LightBank for is NOT established (U). Session-8 fix pass, <c>atmos-survey lightbanks all</c>
    /// (validation\ds1-lightbanks-all.txt): every area has it (DeS has s01..s08_LightBank too, Smithbox DES/DS1 lists),
    /// it holds the same row ids for the same purposes as LightBank (its names are English renderings of LightBank's:
    /// a10 1 'B0屋内' / 'B0_indoor', 6 'B0キャラ屋内' / 'B0_character_indoor'), both the rows map pieces select and the
    /// character rows collisions select, with re-tuned values (often one light; colA_s = 100 in 567 of 597 rows); its
    /// light 0 points along the zone's ShadowBank direction in only 22 of 135 collision zones (the Asylum's (70,-150) is
    /// one of them, which is where the earlier "sun" reading came from). Some ids hold cutscene/effect rows where
    /// LightBank holds a gameplay row: a18 36 'PolyG-scn100100-&lt;c0000&gt;-cutAll' (LightBank 36 'B1キャラ(下水)', the Stray
    /// Demon pit), a10 35 'SFX_abyss_cutscene_for_water_from_watergate' (LightBank 35 'B2キャラ（火防女洞窟）', the
    /// Firekeeper's cave), a10 36 'SFX：m15_00_for_rotating_gimmick_cloud_of_dust' (LightBank 36 'Capra_demon').
    /// So: the s_ row of the same id when it is a gameplay row (<see cref="IsNonGameplayRow"/>); otherwise (missing, as
    /// DSR's a18 LightBank 64 'character_entrance' of the Asylum courtyard, or a cutscene/effect row) the s_ row of the
    /// named gameplay LightBank row with the most similar values (sum of absolute field differences) whose s_ twin is a
    /// named gameplay row too. Session 8.
    /// </summary>
    public (PARAM.Row? Row, int Id) StageLight(int lightId)
    {
        var s = Row("s_LightBank", lightId);
        if (s != null && !IsNonGameplayRow(s)) return (s, lightId);
        var l = Row("LightBank", lightId);
        if (l == null || !Banks.TryGetValue("s_LightBank", out var sb) || !Banks.TryGetValue("LightBank", out var lb)) return (null, -1);
        double Diff(PARAM.Row a, PARAM.Row b) => a.Cells.Zip(b.Cells).Where(c => c.First.Value is short or int or float or sbyte or byte)
            .Sum(c => Math.Abs(Convert.ToDouble(c.First.Value) - Convert.ToDouble(c.Second.Value)));
        bool Usable(PARAM.Row? r) => r != null && !string.IsNullOrEmpty(r.Name) && !IsNonGameplayRow(r);
        var (lo, hi) = Segment(lightId); // rule H2-D (one segment, the whole bank, for an unmerged map)
        var best = lb.Rows.Where(r => r.ID != lightId && r.ID >= lo && r.ID < hi && Usable(r) && Usable(sb.Rows.FirstOrDefault(x => x.ID == r.ID)))
            .OrderBy(r => Diff(l, r)).FirstOrDefault();
        return best == null ? (null, -1) : (Row("s_LightBank", best.ID), best.ID);
    }

    /// <summary>
    /// A row authored for a cutscene or an effect rather than for a gameplay spot, judged by its developer name
    /// (session 8 fix pass): DSR's a18 s_LightBank 36 'PolyG-scn100100-&lt;c0000&gt;-cutAll' (a polygon-demo cutscene rig:
    /// three lights like LightBank 0 'B0基本'), a10 s_LightBank 35 'SFX_abyss_cutscene_for_water_from_watergate'.
    /// Markers: "scn" + digits (remo scene id), "PolyG", "cut", "cutscene", "SFX", "デモ", "ムービー".
    /// </summary>
    public static bool IsNonGameplayRow(PARAM.Row? row)
    {
        string n = row?.Name ?? "";
        return System.Text.RegularExpressions.Regex.IsMatch(n, @"scn\d|PolyG|cutscene|cutAll|[-_ ]cut\d|\bSFX|デモ|ムービー", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>A numeric cell as double (s16/s8/u8/f32 fields alike); NaN when the row or field is missing.</summary>
    public static double Get(PARAM.Row? row, string field)
    {
        if (row == null) return double.NaN;
        var c = row.Cells.FirstOrDefault(x => x.Def.InternalName == field);
        return c == null ? double.NaN : Convert.ToDouble(c.Value);
    }

    /// <summary>The most used value of a draw-param id over the map's ordinary collisions (HitFilterID 8) and their
    /// share; the spot the player starts on in DS1 counts too.</summary>
    public static List<(int Id, int Parts)> Usage(MSB1 msb, Func<MSB1.Part, byte> id) =>
        msb.Parts.Collisions.Where(c => c.HitFilterID == 8).GroupBy(c => (int)id(c)).Select(g => (g.Key, g.Count()))
            .OrderByDescending(x => x.Item2).ToList();
}
