using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// DS1 lighting, fog, post-processing, player light, ambience particles and sound in our DS2 maps (session 8, user:
/// "I want lighting and music and ambiance particles etc from ds1 to be in the ds2 integration and match the original.
/// If that's not possible take the lighting properties from a ds2 area that makes the most sense"). Evidence and
/// field meanings: <c>atmos-survey ds1|ds2|flt|env</c>, NOTES.md of the atmos sandbox.
///
/// <para><b>How both games choose the look of a spot</b> (the base of every rule here): DS1 MSB1 parts carry one byte
/// per draw bank (LightID, FogID, ScatterID, ShadowID, DofID, ToneMapID, ToneCorrectID, LanternID, LensFlareID) = a
/// row of the area's DrawParam (<see cref="Ds1DrawParam"/>); the collision under the player picks them. DS2 MSB2
/// collisions carry a FilterID (SoulsFormats UnkT13, Smithbox DS2S "Filter ID") = a value id in the map's
/// <c>filter\filter_mAA_BB_CC_DD.fltparam</c> (GPARAM "filt" v2). Verified on vanilla data: every FilterID Majula's
/// collisions use (0,1,6,7,8,9,11,12,13) is an id whose 平行ライト 有効 (parallel light enabled) is True, the unused
/// ids hold defaults. So <b>rule A1: one DS1 draw-param combination (zone) = one DS2 filter id</b> (16 ids per map,
/// 0 = the zone with the most collision triangles; more zones merge pairwise into the most alike).</para>
///
/// <para><b>DS1 bank → DS2 fltparam group</b> (DS1 field meanings: Smithbox DS1R paramdefs; DS2: group/param names
/// stored in the fltparam itself):
/// s_LightBank[LightID] (gameplay rows only, <see cref="Ds1DrawParam.StageLight"/>; what DS1 reads this second bank
/// for is U: session-8 fix pass, its light 0 follows ShadowBank's direction in only 22 of 135 DS1 collision zones, so
/// "the sun" was an Asylum-only reading) → 平行ライト slot 0, DS2's light for the map (Angle 0 = (degRotX, degRotY) in
/// radians, TB's Angle 0 (0.611,1.222) = its MSB light's (35°,70°); DiffColor 0 = RGB/255 + intensity);
/// LightBank[LightID] → slot 10 (DS2's "player light", its MSB twin is named プレイヤーライト): DS1's own row names say
/// the LightBank rows collisions select are character rows (65 of 92 named キャラ/character; map pieces select other
/// rows: 123 of 132 not named so), and a row with all three colA 0 (a10 27 'B2キャラ（日陰）', characters in shade)
/// stays hemisphere-only, which DS2 itself does in 20 of 181 used filter ids (<c>atmos-survey slot10</c>); the
/// strongest DS1 light is the key, the other two are folded into the AddHemiColor
/// terms (from above → Up, from below → Down, half intensity); hemisphere col*_u / col*_d → AddHemiColorUp/Down;
/// FogBank → フォグ (色 = RGB × colA/100 with the zone's LightScatteringBank applied at the fog end (session 8 skylight,
/// <see cref="Scatter"/>), 距離 = fogEndZ × k, α = degRotW/100, no height fog); ToneCorrectBank → カラーグレーディング
/// 変換モード 3 with our own palette LUT (skylight, <see cref="Palette"/>; the earlier 彩度 = saturation − 1 had no effect
/// in mode 3); ToneMapBank → 動的順応 (skylight, <see cref="ExposureBias"/>), Bloom有効 (bloomMul &gt; 0
/// and bloomBegin &lt; 100) and ライトシャフト有効 (lightShaftPower &gt; 0); DofBank → LensSimulation (FarStart/FarEnd,
/// near always "off" (-1,-2): DS2 measures from its camera focus, DS1 from the camera; DOF Alpha = farDofMul/100); ShadowBank → カスケードシャドウ
/// (fade start/length, Cascade 影の色 = colour + densityRatio as alpha); LensFlareID 255 (none) → レンズフレア アルファ 0;
/// PointLightBank[LanternID] → a DS2 PlayerLight row (diffuse RGB/A in percent like DS1's colA, radius = dwindleEnd)
/// named by the collision's PlayerLightSfxID (UnkT1C; TB's 100260 = PlayerLight "屋内暗い空間汎用").</para>
///
/// <para><b>Units (rule A2):</b> DS2 stores each parallel-light intensity as N/256 with N on the same authoring scale
/// DS1 uses for colA (6802 of 6874 values in all 28 vanilla fltparams; N = 25, 50, 75, 100, 150, 200, 300, 400, 500,
/// 600, 1000…, <c>atmos-survey units</c>), so <b>W = colA / 256</b> (Asylum character key 500 → 1.95; DS2 itself uses
/// 500 32× and 600 46×). Fog distances are world metres in both games and are copied (<see cref="Calibrate"/>
/// reports the medians of both games as a check). Zones are ordered by collision triangles (walkable area), so
/// filter 0 is the zone covering most of the map.</para>
///
/// <para><b>Fallback (rule A3):</b> fields DS1 has no counterpart for (SSAO, FXAA, star glare, vignette, volume fog,
/// adaptation curve, lens-flare shapes) and the env cubemaps come from the DS2 map whose main filter id is closest to
/// the DS1 map's main zone (Euclidean distance over sun/character/hemisphere colour × intensity, fog colour, log fog
/// distance, fog alpha, each dimension scaled by its spread over DS2's maps), not from Things Betwixt by default
/// (<see cref="BestTemplate"/>); every zone starts from that map's untouched main filter id. <b>Rule A3b:</b> the
/// probe/GI read-out fields (point-cloud scale/offset, GI colour scale) stay Things Betwixt's filter 0, which our own
/// probes and lightmap gain were tuned against in session 7 (<see cref="IsProbeField"/>).</para>
///
/// <para><b>Ambience:</b> DS1 places map SFX as MSB1 SFX events at regions → DS2 MSB2 SFX regions (DS2's base effects
/// all live in the global sfx9999 bank, so a DS2 effect id works in any map); DS1's FFX files are a different FXR
/// version, so each DS1 effect maps to the DS2 effect of the same kind by its developer name (530 松明 torch → 3020
/// 【松明小】; 100201 靄 mist → 3011 【もや】). DS2 also attaches an ambient effect to the player per collision
/// (AmbientSfxID, UnkT18; TB's 3620); DS1 has none, so it is cleared.</para>
///
/// <para><b>Sound (default since session 10, --no-ds1-sound; rules SND1-SND5, <see cref="Events.MapSounds"/>):</b> both
/// games ship FMOD Ex 4.44 (DS2 4.44.50 DLLs, DSR 4.44.61), both name events type letter + 9 digits (DS1 a180000003,
/// DS2 a100210001; DS2's region code builds "a%09d"/"m%09d"); DS1 MSB1 Sound events → DS2 MSB2 Sound regions
/// (<see cref="SoundRegions"/>), DS1's projects (FEV1 v0x3D0000 + FSB4) join DS2's frpg2.fevbnd in DS2's own map-bank
/// layout (<see cref="SoundBanks"/>; offline with DS2's FMOD: every Asylum region and SE event resolves, starts and is
/// audible: data\sandbox\asy-sound\validate\fmod_map.py).</para>
/// </summary>
public static class Atmosphere
{
    /// <summary>A DS1 draw-param combination and the DS2 filter id it becomes.</summary>
    public sealed class Zone
    {
        public int Filter { get; init; }
        public byte Light, Fog, Scatter, Shadow, Dof, ToneMap, ToneCorrect, Lantern, LensFlare;
        public List<string> Parts { get; } = [];
        public override string ToString() => $"filter {Filter}: light {Light} fog {Fog} scatter {Scatter} shadow {Shadow} dof {Dof} tone {ToneMap}/{ToneCorrect} lantern {Lantern} flare {LensFlare} ({Parts.Count} collisions)";
    }

    public sealed class Plan
    {
        public List<Zone> Zones { get; } = [];
        /// <summary>DS1 collision part name → zone.</summary>
        public Dictionary<string, Zone> ByPart { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Zone Main => Zones[0];
    }

    public const int MaxFilters = 16;

    static (byte, byte, byte, byte, byte, byte, byte, byte, byte) Key(MSB1.Part p) =>
        (p.LightID, p.FogID, p.ScatterID, p.ShadowID, p.DofID, p.ToneMapID, p.ToneCorrectID, p.LanternID, p.LensFlareID);

    /// <summary>Rule A1: the placed DS1 collisions grouped by their draw-param ids; the zone with the most collision
    /// triangles (<paramref name="weight"/>, i.e. walkable area; the collision count favoured the five pieces of the
    /// boss hall floor) first = filter 0.</summary>
    public static Plan Zones(IEnumerable<MSB1.Part.Collision> placed, List<string> log, Func<MSB1.Part.Collision, int>? weight = null, Ds1DrawParam? dp = null,
        Func<MSB1.Part.Collision, int>? member = null)
    {
        // session 11 (burg11, round 8), rule H2-A: a merged map (rule H2) zones its primary's collisions alone (the same filter
        // ids as when it is built by itself), then its secondaries' into the filter ids left (merged among themselves to fit;
        // with none left, each into the primary's closest zone)
        var list = placed.ToList();
        if (member != null && list.Any(p => member(p) > 0))
        {
            var prim = Zones(list.Where(p => member(p) == 0), log, weight, dp);
            var plan2 = new Plan();
            foreach (var z in prim.Zones) plan2.Zones.Add(z);
            foreach (var (k, v) in prim.ByPart) plan2.ByPart[k] = v;
            // round 9: each secondary in turn (member order) takes the filter ids left, so a later member never re-zones an
            // earlier one; with none left, its collisions join the closest zone so far
            foreach (var rest in list.Where(p => member(p) > 0).GroupBy(member).OrderBy(gr => gr.Key).Select(gr => gr.ToList()))
            {
                int room = MaxFilters - plan2.Zones.Count;
                if (room > 0)
                {
                    var sec = ZonesCapped(rest, log, weight, dp, room);
                    foreach (var z in sec.Zones)
                    {
                        var nz = new Zone { Filter = plan2.Zones.Count, Light = z.Light, Fog = z.Fog, Scatter = z.Scatter, Shadow = z.Shadow, Dof = z.Dof, ToneMap = z.ToneMap, ToneCorrect = z.ToneCorrect, Lantern = z.Lantern, LensFlare = z.LensFlare };
                        nz.Parts.AddRange(z.Parts);
                        plan2.Zones.Add(nz);
                        foreach (var p in nz.Parts) plan2.ByPart[p] = nz;
                        log.Add($"atmos: rule H2-A: secondary {nz}: {string.Join(" ", nz.Parts)}");
                    }
                }
                else
                    foreach (var p in rest)
                    {
                        var f = Feat(dp, Key(p));
                        var z = plan2.Zones.MinBy(z => Math.Sqrt(f.Zip(Feat(dp, (z.Light, z.Fog, z.Scatter, z.Shadow, z.Dof, z.ToneMap, z.ToneCorrect, z.Lantern, z.LensFlare)), (x, y) => (x - y) * (x - y)).Sum()))!;
                        z.Parts.Add(p.Name); plan2.ByPart[p.Name] = z;
                        log.Add($"atmos: rule H2-A: no filter id left: secondary collision {p.Name} joins {z}");
                    }
            }
            return plan2;
        }
        return ZonesCapped(list, log, weight, dp, MaxFilters);
    }

    static double[] Feat(Ds1DrawParam? dp, (byte, byte, byte, byte, byte, byte, byte, byte, byte) k)
    {
        if (dp == null) return [k.Item1, k.Item2];
        var z = new Zone { Light = k.Item1, Fog = k.Item2 }; // DS1's authored values (no H1; see Ds1Features)
        return Ds1Features(dp, z, new Calibration(Unit, Unit, Unit, Unit, 1, ""));
    }

    static Plan ZonesCapped(IEnumerable<MSB1.Part.Collision> placed, List<string> log, Func<MSB1.Part.Collision, int>? weight, Ds1DrawParam? dp, int maxFilters)
    {
        weight ??= _ => 1;
        var groups = placed.GroupBy(Key).Select(g => (Key: g.Key, Parts: g.ToList(), W: g.Sum(weight))).ToList();
        // More combinations than DS2's 16 filter ids: merge the two that look most alike (converted light/fog values, the
        // other bank ids as a small tie-breaker), the lighter into the heavier, until 16 remain. A rule on ids alone
        // merged the Asylum's start cell (light 33) into the boss hall.
        double[] F((byte, byte, byte, byte, byte, byte, byte, byte, byte) k)
        {
            if (dp == null) return [k.Item1, k.Item2];
            var z = new Zone { Light = k.Item1, Fog = k.Item2 }; // DS1's authored values (no H1; see Ds1Features)
            return Ds1Features(dp, z, new Calibration(Unit, Unit, Unit, Unit, 1, ""));
        }
        while (groups.Count > maxFilters)
        {
            var feats = groups.Select(g => F(g.Key)).ToList();
            (int A, int B, double D) best = (0, 1, double.MaxValue);
            for (int a = 0; a < groups.Count; a++)
                for (int b = a + 1; b < groups.Count; b++)
                {
                    var ka = groups[a].Key; var kb = groups[b].Key;
                    double d = Math.Sqrt(feats[a].Zip(feats[b], (x, y) => (x - y) * (x - y)).Sum())
                        + 0.01 * new[] { ka.Item3 != kb.Item3, ka.Item4 != kb.Item4, ka.Item5 != kb.Item5, ka.Item6 != kb.Item6, ka.Item7 != kb.Item7, ka.Item8 != kb.Item8, ka.Item9 != kb.Item9 }.Count(x => x);
                    if (d < best.D) best = (a, b, d);
                }
            var (heavy, light) = groups[best.A].W >= groups[best.B].W ? (best.A, best.B) : (best.B, best.A);
            log.Add($"atmos: zone {groups[light].Key} ({string.Join(" ", groups[light].Parts.Select(p => p.Name))}) merged into {groups[heavy].Key} (closest look, d={best.D:0.###}; DS2 has {MaxFilters} filter ids)");
            groups[heavy] = (groups[heavy].Key, groups[heavy].Parts.Concat(groups[light].Parts).ToList(), groups[heavy].W + groups[light].W);
            groups.RemoveAt(light);
        }
        var plan = new Plan();
        foreach (var g in groups.OrderByDescending(g => g.W).ThenBy(g => g.Key))
        {
            var k = g.Key;
            var zone = new Zone { Filter = plan.Zones.Count, Light = k.Item1, Fog = k.Item2, Scatter = k.Item3, Shadow = k.Item4, Dof = k.Item5, ToneMap = k.Item6, ToneCorrect = k.Item7, Lantern = k.Item8, LensFlare = k.Item9 };
            plan.Zones.Add(zone);
            foreach (var p in g.Parts) { zone.Parts.Add(p.Name); plan.ByPart[p.Name] = zone; }
        }
        foreach (var z in plan.Zones) log.Add($"atmos: {z}: {string.Join(" ", z.Parts)}");
        return plan;
    }

    /// <summary>Rule A2: DS2 intensity W = DS1 colA / 256.</summary>
    public const double Unit = 100.0 / 256;

    /// <summary>Triangles per DS1 collision model (the map's h*.hkxbhd), the zone weight.</summary>
    public static Dictionary<string, int> CollisionTriangles(string dsrRoot, string ds1Map)
    {
        string dir = Path.Combine(dsrRoot, "map", ds1Map);
        var bxf = BXF3.Read(Path.Combine(dir, $"h{ds1Map[1..]}.hkxbhd"), Path.Combine(dir, $"h{ds1Map[1..]}.hkxbdt"));
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in bxf.Files)
        {
            string stem = Path.GetFileName(f.Name);                 // h0001B1A18.hkx.dcx → model h0001B1
            int a = stem.IndexOf('A', 1);
            if (a < 0) continue;
            try { result[stem[..a]] = Commands.ColSurvey.Triangles(DCX.Decompress(f.Bytes)).Count(); } catch (Exception) { }
        }
        return result;
    }

    // ------------------------------------------------------------------ DS1 values in DS2 units

    /// <summary>A light rig in DS2 terms: key direction (radians), key diffuse/specular, hemisphere up/down (RGB 0..1,
    /// W = intensity), env reflection tint.</summary>
    public sealed record Rig(Vector2 Angle, Vector4 Diffuse, Vector4 Specular, Vector4 HemiUp, Vector4 HemiDown, Vector4 EnvSpecular);

    static double D(PARAM.Row? r, string f) => Ds1DrawParam.Get(r, f) is var v && double.IsNaN(v) ? 0 : v;
    static Vector3 Rgb(PARAM.Row? r, string suffix) => new((float)(D(r, "colR" + suffix) / 255), (float)(D(r, "colG" + suffix) / 255), (float)(D(r, "colB" + suffix) / 255));

    /// <summary>DS1 LIGHT_BANK row → DS2 rig. k = calibration of the directional intensity, kHemi of the hemisphere.</summary>
    public static Rig LightRig(PARAM.Row? r, double k, double kHemi)
    {
        var lights = Enumerable.Range(0, 3).Select(i => (I: i, A: D(r, $"colA_{i}"), X: D(r, $"degRotX_{i}"), Y: D(r, $"degRotY_{i}"), C: Rgb(r, $"_{i}"))).ToList();
        var key = lights.OrderByDescending(l => l.A).First();
        var up = new Vector4(Rgb(r, "_u"), (float)(D(r, "colA_u") / 100 * kHemi));
        var down = new Vector4(Rgb(r, "_d"), (float)(D(r, "colA_d") / 100 * kHemi));
        foreach (var l in lights.Where(l => l.I != key.I && l.A > 0))
        {
            float w = (float)(0.5 * l.A / 100 * k);
            ref Vector4 h = ref (l.X >= 0 ? ref up : ref down);
            float total = h.W + w;
            if (total > 0) h = new Vector4((new Vector3(h.X, h.Y, h.Z) * h.W + l.C * w) / total, total);
        }
        float rad = MathF.PI / 180;
        return new Rig(new Vector2((float)key.X * rad, (float)key.Y * rad),
            new Vector4(key.C, (float)(key.A / 100 * k)),
            new Vector4(Rgb(r, "_s"), (float)(D(r, "colA_s") / 100 * k)),
            up, down,
            new Vector4((float)(D(r, "envSpc_colR") / 255), (float)(D(r, "envSpc_colG") / 255), (float)(D(r, "envSpc_colB") / 255), (float)(D(r, "envSpc_colA") / 100)));
    }

    /// <summary>DS1 FOG_BANK row → DS2 fog colour (RGB × colA/100, opaque; session 8 skylight: with the zone's
    /// LIGHT_SCATTERING_BANK row applied at the fog's end distance, <see cref="Scatter"/>, rule H1), distance (fogEndZ × k),
    /// alpha (degRotW/100).</summary>
    public static (byte[] Color, float Distance, float Alpha) Fog(PARAM.Row? r, double kDist, PARAM.Row? scatter = null)
    {
        double a = D(r, "colA") / 100;
        var c = Scatter(scatter, (float)Math.Max(1, D(r, "fogEndZ"))).Apply(new Vector3((float)(D(r, "colR") * a), (float)(D(r, "colG") * a), (float)(D(r, "colB") * a)) / 255f) * 255f;
        byte C(float v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
        return ([C(c.X), C(c.Y), C(c.Z), 255], (float)(D(r, "fogEndZ") * kDist), (float)Math.Clamp(D(r, "degRotW") / 100, 0, 1));
    }

    /// <summary>
    /// Rule H1 (session 8, track skylight; user's DSR reference of Firelink: "dark, desaturated blue-green/teal scene,
    /// distant cliffs fading into blue haze"): DS1 adds aerial perspective on top of its fog (LIGHT_SCATTERING_BANK, the
    /// part's or collision's ScatterID). DSR's map shaders (<c>FRPG_Phn_Dif___BmpMulLit____Non.fpo</c>, shader-disasm)
    /// compute per pixel T = <c>exp(−d × β × distanceMul × 2.081369)</c> and an in-scatter S — Rayleigh (1 + cos²θ) plus
    /// Henyey-Greenstein Mie (1 − g)²/(1 + g² − 2g cosθ)^1.5 around the scatter sun, × the sun colour — and output
    /// <c>lerp(fogged, fogged × T × reflectance + S × (1 − T), blendCoef)</c> in gamma space. DS2 has one per-filter fog
    /// colour and no scattering, so DS1's formula is evaluated where it matters: on the fog colour at the zone fog's end
    /// distance (the DS2 fog colour = what DS1 shows for fully fogged geometry there), on the baked sky (rule S1) and on
    /// distant unlit pieces (rule S3b) at their distance. Terms: β = lsBetaRay + lsBetaMie; reflectance = rgb/255 ×
    /// reflectanceA/100; S∞ = E_sun × share / 4π with E_sun = sun rgb/255 × sunA/100: the in-scatter of the
    /// single-scattering model normalised by β (Hoffman-Preetham form, (β_R Φ_R + β_M Φ_M)/β), averaged over the view
    /// directions away from the sun — both normalised phase functions average 1/4π over the sphere, and share = (β_Ray +
    /// β_Mie (1 − |g|)) / β drops the part of a Henyey-Greenstein lobe concentrated in the sun's direction (g 0.99 = a
    /// sun halo, not a haze colour; the halo is not translated).
    /// <b>Session 9 (track skyfix), rule H1 v2:</b> the skylight pass guessed the depth scale (1e-4/m, one β for all channels)
    /// and the in-scatter normalisation (sun/4π × share); both were wrong (the Asylum mountains rendered black). DS1's own
    /// constants are now read from DSR's exe and evaluated exactly by <see cref="Ds1Scatter"/>: per-channel β = lsBetaRay ×
    /// Rayleigh β + lsBetaMie × Mie β (coefficient vectors in LightScatteringBankEntity), T = exp2(−d β distanceMul/100 ×
    /// 2.081369) (DXBC exp is base 2; fix pass), S = sun × (β'_R (1 + cos²θ) + β'_M HG(θ)) / β × (1 − T) with DS1's HG variant (1 − g²)/(1 + g − 2g cosθ)^1.5;
    /// direction-independent uses (this fog colour, the S1 sky texture, S3b textures) average S over horizontal views.
    /// </summary>
    public sealed record ScatterTerms(Vector3 T, Vector3 Reflectance, Vector3 InScatter, float Blend)
    {
        /// <summary>DS1's result <c>lerp(c, c × T × reflectance + S, blend)</c> (S includes its (1 − T)).</summary>
        public Vector3 Apply(Vector3 c) => Vector3.Lerp(c, c * T * Reflectance + InScatter, Blend);
        public static readonly ScatterTerms None = new(Vector3.One, Vector3.One, Vector3.Zero, 0);
        public override string ToString() => $"T ({T.X:0.###},{T.Y:0.###},{T.Z:0.###}) refl ({Reflectance.X:0.##},{Reflectance.Y:0.##},{Reflectance.Z:0.##}) S ({InScatter.X:0.###},{InScatter.Y:0.###},{InScatter.Z:0.###}) blend {Blend:0.##}";
    }

    /// <summary>Rule H1 v2: DS1's scattering at a distance, averaged over horizontal views (<see cref="Ds1Scatter.Horizontal"/>).</summary>
    public static ScatterTerms Scatter(PARAM.Row? scatter, float distance) => Ds1Scatter.Horizontal(scatter, distance);

    // ------------------------------------------------------------------ calibration (rule A2)

    /// <summary>Unit rule A2 (intensities) and the exposure populations (the DS1 medians of grayKeyValue / min / max
    /// adapted luminance over the ordinary collision zones of every DS1 map, and the DS2 medians of 順応感度-明 / 順応最低輝度 /
    /// 順応最高輝度 over every used, adapting DS2 filter id): logged for reference; rule A8c (skylight fix pass) uses each
    /// zone's own DS1 row, Key only when a row has no grayKeyValue.</summary>
    public sealed record Calibration(double Sun, double Chara, double Hemi, double CharaHemi, double FogDistance, string Report,
        double Key = 0.18, double Ds1Min = 0.1, double Ds1Max = 0.21, double Sens = 0.725, double Ds2Min = 0.015, double Ds2Max = 0.2);
    static Calibration? calibration;

    static double Median(List<double> v) { if (v.Count == 0) return double.NaN; v.Sort(); return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2; }

    /// <summary>The DS2 filter ids a map's ordinary collisions (hit filter 100) use, most used first.</summary>
    public static List<int> UsedFilters(MSB2 msb) =>
        msb.Parts.Collisions.Where(c => c.UnkT14 == 100).GroupBy(c => (int)c.UnkT13).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();

    static readonly Dictionary<string, (GPARAM Flt, List<int> Used)> ds2Maps = new();
    static Dictionary<string, (GPARAM Flt, List<int> Used)> Ds2Maps(string ds2Data)
    {
        if (ds2Maps.Count > 0) return ds2Maps;
        foreach (string f in Directory.GetFiles(Path.Combine(ds2Data, "filter"), "filter_m*.fltparam"))
        {
            string map = Path.GetFileNameWithoutExtension(f)["filter_".Length..];
            string msbPath = Path.Combine(ds2Data, "map", map, map + ".msb");
            if (!File.Exists(msbPath)) continue;
            var used = UsedFilters(MSB2.Read(msbPath)).Where(id => id < MaxFilters).ToList();
            if (used.Count > 0) ds2Maps[map] = (GPARAM.Read(f), used);
        }
        return ds2Maps;
    }

    /// <summary>
    /// Rule A8c (session-8 skylight fix pass; replaces A8b): DS1 auto-exposure → DS2 動的順応 + ToneMap AvgLuminance, the
    /// same exposure FORM in both games, every zone id adapting as in DS1.
    ///
    /// <para><b>DS2, verified in the exe</b> (decomp\ds2s, FUN_140b8f660 registers the ToneMap/動的順応 params into the tone
    /// map object at +0x20 AvgLuminance, +0x30 LuminanceBalance, +0x40 MaxLuminance, +0x50 Scaler, +0x64 動的順応有効, +0x68
    /// 順応速度, +0x70 感度-暗, +0x74 感度-明, +0x78 輝度中心, +0x7c 最低輝度, +0x80 最高輝度; FUN_140b93800 fills cbFilter from
    /// that block): <c>g_ToneMapInvAvgLuminance (cb6[0]) = 1 / (AvgLuminance × LuminanceBalance.rgb)</c> (divps of
    /// DAT_1410ac4a0 = (1,1,1,1)), g_ToneMapScaler = Scaler, g_ToneMapElapsedTime.x = 順応速度/60, g_ToneMapAdaptGradient =
    /// (感度-暗, 感度-明, 輝度中心) when 動的順応有効 else (1, 1, 0), g_vToneMapAdaptMinMax = (最低輝度, 最高輝度) else (0, 10).
    /// Shaders (shader-disasm, validation\ds2-tonemap-fog-shaders.txt): ColorToLuminance L = mean √luma (Rec.601);
    /// AdaptedLuminance L → lerp toward it by 1 − exp(−1.748781 × 順応速度/60), clamped to [√最低, √最高], s = c + (L² − c) ×
    /// sensitivity (感度-暗 below the centre c, 感度-明 above), exposure E = 1/s; not adapting: ResetAdaptedLuminance with
    /// the (1,1,0) gradient → E = 1; WriteFilmicToneMappingTable: display = Hejl-Dawson(x × E × cb6[0] − 0.004) × Scaler,
    /// then the palette LUT. So DS2's exposure is <c>1 / (AvgLuminance × LuminanceBalance × s(L))</c>: AvgLuminance carries
    /// the zone's absolute exposure (vanilla 20–30 on DS2's main outdoor ids, 0.6 elsewhere: 50× apart), the adaptation
    /// only a 10–20× band. A8b changed only 最低輝度/感度-明 and missed AvgLuminance; its --exposure-bias divided
    /// LuminanceBalance, which BRIGHTENS (cb6[0] = 1/(Avg × LB)), the opposite of its documentation.</para>
    ///
    /// <para><b>DS1</b> (DSR FRPG_Filter shaders): SampleLumInitial/Final = geometric mean of the scene luminance
    /// (Rec.709), CalcAdaptedLum_PBL lerps by 1 − exp(−0.029146 × k) with 0.029146 = 1.748781/60 (DS2's constant: same
    /// engine code, so DS2's 順応速度 = DS1's adaptSpeed), HDR_PBL_ColAdj E = grayKeyValue / (clamp(La, minAdaptedLum,
    /// maxAdapredLum) + 0.0001), then the curve of mode cb0[57] (0 Hable with A..F, W from cb0[54..55]; 1 Reinhard; 2
    /// extended Reinhard), gamma 1/2.2, colour matrix.</para>
    ///
    /// <para><b>Rule</b>: 動的順応有効 = true (DS1 adapts in every zone); 順応最低輝度 / 順応最高輝度 = DS1's minAdaptedLum /
    /// maxAdapredLum (the same luminance band; DS2's own 最高輝度 0.2 sits on DS1's 0.2–0.25); 輝度中心 = 0.0001 (DS2's value
    /// in 163 of 164 adapting ids, below every DS1 band) and both sensitivities 1, so s = L² as DS1's La; 順応速度 = DS1
    /// adaptSpeed; AvgLuminance = <see cref="ExposureBias"/> / (τ × grayKeyValue × LuminanceBalance) with τ = the DS2 curve
    /// input that shows what DS1's curve shows at the key (<see cref="ToneMatch"/>): then DS2's exposed value is τ × DS1's
    /// and both games display the adapted average alike. τ uses DSR mode 0 with Uncharted 2's published constants
    /// (A..F, W = 0.15, 0.5, 0.1, 0.2, 0.02, 0.3, 11.2; the exe's values are not in its plain data, P): τ(0.18) = 0.32,
    /// AvgLuminance 17 (DS2's own outdoor ids: AvgLuminance × 感度-明 = 8–15). If DSR runs mode 1 (Reinhard), τ = 0.59 and
    /// AvgLuminance 9.5 (logged as a lead). Not modelled (P): DS1 measures the geometric mean, DS2 (mean √Y)², which is
    /// larger by e^(σ²/4) for a log-normal scene (≈ 1.3–1.8 at σ = 1–1.5): if anything DS2 errs darker by that factor;
    /// and how our scene's luminance compares with DS1's (W = colA/256 and GI = (0.6 × lightmap)² ≈ 0.4× DS1 if DS1 reads
    /// colA as percent; 1× if DS1 also divides by 256) only matters outside the band. ExposureBias &gt; 1 darker
    /// (build-pack --exposure-bias). Status P.</para>
    /// </summary>
    public static double ExposureBias { get; set; } = 1.0;

    /// <summary>DSR's filmic curve, FRPG_Fil_HDR_PBL_ColAdj.fpo mode 0: Hable's operator normalised by its white point,
    /// then gamma 1/2.2. Constants: Uncharted 2's (see <see cref="ExposureBias"/>; P).</summary>
    public static double Ds1Display(double x)
    {
        const double A = 0.15, B = 0.5, C = 0.1, D = 0.2, E = 0.02, F = 0.3, W = 11.2;
        static double H(double v) => (v * (A * v + C * B) + D * E) / (v * (A * v + B) + D * F) - E / F;
        return Math.Pow(Math.Clamp(H(Math.Max(0, x)) / H(W), 0, 1), 1 / 2.2);
    }

    /// <summary>DSR's mode 1 (Reinhard on luminance, grey input), gamma 1/2.2: the alternative if the exe selects it.</summary>
    public static double Ds1DisplayReinhard(double x) => Math.Pow(Math.Clamp(x / (1 + x), 0, 1), 1 / 2.2);

    /// <summary>DS2's tone curve (ToneMap_WriteFilmicToneMappingTable × TabledToneMap's 2): Hejl-Dawson of the exposed
    /// value minus 0.004, × Scaler.</summary>
    public static double Ds2Display(double y, double scaler)
    {
        double v = Math.Max(0, y - 0.004);
        return Math.Clamp((6.2 * v * v + 0.5 * v) / (6.2 * v * v + 1.7 * v + 0.06) * scaler, 0, 1.5);
    }

    /// <summary>Rule A8c: τ = (the DS2 input whose display equals DS1's display of <paramref name="key"/>) / key, by
    /// bisection.</summary>
    public static double ToneMatch(double key, double scaler, Func<double, double>? ds1 = null)
    {
        double target = (ds1 ?? Ds1Display)(key), lo = 0, hi = 50;
        for (int i = 0; i < 80; i++) { double mid = (lo + hi) / 2; if (Ds2Display(mid, scaler) < target) lo = mid; else hi = mid; }
        return (lo + hi) / 2 / key;
    }

    /// <summary>Rule A2: the DS1 → DS2 intensity scale (W = colA/256, see below) and, as a check, the medians of both
    /// games' used zones (logged in build.log).</summary>
    public static Calibration Calibrate(string dsrRoot, string ds2Data)
    {
        if (calibration != null) return calibration;
        var d2 = (Sun: new List<double>(), Chara: new List<double>(), Hemi: new List<double>(), CharaHemi: new List<double>(), Fog: new List<double>());
        foreach (var (map, (flt, used)) in Ds2Maps(ds2Data))
            foreach (int id in used)
            {
                if (Get(flt, "平行ライト", "有効", id) is not true) continue;
                if (Get(flt, "平行ライト", "DiffColor 0", id) is Vector4 s) d2.Sun.Add(s.W);
                if (Get(flt, "平行ライト", "DiffColor 10", id) is Vector4 c) d2.Chara.Add(c.W);
                if (Get(flt, "平行ライト", "AddHemiColorUp 0", id) is Vector4 h) d2.Hemi.Add(h.W);
                if (Get(flt, "平行ライト", "AddHemiColorUp 10", id) is Vector4 h10) d2.CharaHemi.Add(h10.W);
                if (Get(flt, "フォグ", "距離", id) is float fd && fd > 1) d2.Fog.Add(fd);
            }
        // exposure population (reference): DS2's bright sensitivity over the used filter ids whose adaptation is on
        var d2Sens = Ds2Maps(ds2Data).SelectMany(kv => kv.Value.Used.Select(id => (kv.Value.Flt, Id: id)))
            .Where(x => Get(x.Flt, "動的順応", "動的順応有効", x.Id) is true && Get(x.Flt, "動的順応", "順応感度-明", x.Id) is float)
            .Select(x => (double)(float)Get(x.Flt, "動的順応", "順応感度-明", x.Id)!).ToList();
        var d1Key = new List<double>(); var d1Min = new List<double>(); var d1Max = new List<double>();
        var d2Min = Ds2Maps(ds2Data).SelectMany(kv => kv.Value.Used.Select(id => (kv.Value.Flt, Id: id)))
            .Where(x => Get(x.Flt, "動的順応", "動的順応有効", x.Id) is true && Get(x.Flt, "動的順応", "順応最低輝度", x.Id) is float)
            .Select(x => ((double)(float)Get(x.Flt, "動的順応", "順応最低輝度", x.Id)!, (double)(float)Get(x.Flt, "動的順応", "順応最高輝度", x.Id)!)).ToList();
        var d1 = (Sun: new List<double>(), Chara: new List<double>(), Hemi: new List<double>(), CharaHemi: new List<double>(), Fog: new List<double>());
        var banks = new Dictionary<string, Ds1DrawParam>();
        foreach (string f in Directory.GetFiles(Path.Combine(dsrRoot, "map", "MapStudio"), "m1*.msb"))
        {
            string area = Path.GetFileName(f).Substring(1, 2);
            if (!banks.TryGetValue(area, out var dp)) banks[area] = dp = new Ds1DrawParam(dsrRoot, area);
            var msb = H2Merge.Original(() => MSB1.Read(f)); // session 11 (burg11, round 8): every DS1 map as shipped (not rule H2's merge)
            foreach (var k in msb.Parts.Collisions.Where(c => c.HitFilterID == 8).Select(c => (c.LightID, c.FogID)).Distinct())
            {
                var s = dp.StageLight(k.LightID).Row; // gameplay rows only (no cutscene/effect rows)
                var l = dp.Row("LightBank", k.LightID);
                var g = dp.Row("FogBank", k.FogID);
                if (s != null) { d1.Sun.Add(Enumerable.Range(0, 3).Max(i => D(s, $"colA_{i}")) / 100); d1.Hemi.Add(D(s, "colA_u") / 100); }
                if (l != null) { d1.Chara.Add(Enumerable.Range(0, 3).Max(i => D(l, $"colA_{i}")) / 100); d1.CharaHemi.Add(D(l, "colA_u") / 100); }
                if (g != null && D(g, "fogEndZ") > 1) d1.Fog.Add(D(g, "fogEndZ"));
            }
            foreach (byte tmId in msb.Parts.Collisions.Where(c => c.HitFilterID == 8).Select(c => c.ToneMapID).Distinct())
                if (dp.Row("ToneMapBank", tmId) is { } tm && D(tm, "grayKeyValue") > 0)
                {
                    d1Key.Add(D(tm, "grayKeyValue")); d1Min.Add(D(tm, "minAdaptedLum")); d1Max.Add(D(tm, "maxAdapredLum"));
                }
        }
        // The scales themselves are a unit rule, not these medians: 6802 of DS2's 6874 parallel-light intensities are
        // N/256 with N on DS1's authoring scale (25, 50, 75, 100, 150, 200, 300, 400, 500, 600, 1000; DS1 colA: 10..500;
        // atmos-survey units), so W = colA / 256 (DS1's percent value read the way DS2 stores its own). The medians are
        // reported as a check (DS1 median × 100/256 lands next to DS2's). Fog distances are world metres in both games
        // (DS2's foggier median: Things Betwixt's 20 m night mist vs Majula's 2000 m) and are not scaled.
        const double unit = 100.0 / 256;
        string R(string n, List<double> a, List<double> b) => $"{n}: DS2 median {Median(new(a)):0.###} (n={a.Count}), DS1 median {Median(new(b)):0.###} (n={b.Count}) × 100/256 = {Median(new(b)) * unit:0.###}";
        string report = string.Join("; ", R("sun", d2.Sun, d1.Sun), R("character", d2.Chara, d1.Chara), R("hemisphere", d2.Hemi, d1.Hemi),
            R("character hemisphere", d2.CharaHemi, d1.CharaHemi)) + $"; fog distance: DS2 median {Median(new(d2.Fog)):0.#} m, DS1 median {Median(new(d1.Fog)):0.#} m (not scaled)";
        double key = d1Key.Count > 0 ? Median(new(d1Key)) : 0.18, sens = d2Sens.Count > 0 ? Median(new(d2Sens)) : 0.725;
        double m1 = d1Min.Count > 0 ? Median(new(d1Min)) : 0.1, x1 = d1Max.Count > 0 ? Median(new(d1Max)) : 0.21;
        double m2 = d2Min.Count > 0 ? Median(d2Min.Select(v => v.Item1).ToList()) : 0.015, x2 = d2Min.Count > 0 ? Median(d2Min.Select(v => v.Item2).ToList()) : 0.2;
        report += $"; exposure population (reference only; rule A8c uses each zone's own DS1 row): DS1 medians key {key:0.###} min {m1:0.###} max {x1:0.###} (n={d1Key.Count} map×row), DS2 medians 順応感度-明 {sens:0.###} 順応最低輝度 {m2:0.#####} 順応最高輝度 {x2:0.###} (n={d2Sens.Count} adapting used ids), bias {ExposureBias}";
        calibration = new Calibration(unit, unit, unit, unit, 1, report, key, m1, x1, sens, m2, x2);
        return calibration;
    }

    // ------------------------------------------------------------------ best-fit DS2 area (rule A3)

    static double[] Features(Vector4 sun, Vector4 chara, Vector4 hemi, byte[] fog, float fogDist, float fogAlpha) =>
    [
        sun.X * sun.W, sun.Y * sun.W, sun.Z * sun.W, chara.X * chara.W, chara.Y * chara.W, chara.Z * chara.W,
        hemi.X * hemi.W, hemi.Y * hemi.W, hemi.Z * hemi.W, fog[0] / 255.0, fog[1] / 255.0, fog[2] / 255.0,
        Math.Log10(Math.Max(1, fogDist)), fogAlpha,
    ];

    static double[]? Ds2Features(GPARAM flt, int id) =>
        Get(flt, "平行ライト", "DiffColor 0", id) is Vector4 s && Get(flt, "平行ライト", "DiffColor 10", id) is Vector4 c
        && Get(flt, "平行ライト", "AddHemiColorUp 0", id) is Vector4 h && Get(flt, "フォグ", "色", id) is byte[] fc
        && Get(flt, "フォグ", "距離", id) is float fd && Get(flt, "フォグ", "α", id) is float fa
            ? Features(s, c, h, fc, fd, fa) : null;

    /// <summary>The DS1 zone's feature vector in DS2 units. The fog colour is DS1's AUTHORED FogBank colour (RGB × colA/100),
    /// not the H1-scattered one the fltparam gets (session-8 skylight fix pass): rule A3 (which DS2 area lends the fields
    /// DS1 has no counterpart for) and the zone merge must not depend on H1's assumed depth scale
    /// (<see cref="ScatterDepthScale"/>, P). Scored with the scattered colour, Firelink's fallback flipped from Majula (d 2.47,
    /// next 2.72) to Forest of Fallen Giants by 2.37 against 2.39, changing its env cubemap, EnvLight regions, SSAO, lens flare
    /// and light-shaft settings as a side effect of a fog-colour rule. <paramref name="scattered"/> = true gives that score
    /// (logged for comparison).</summary>
    public static double[] Ds1Features(Ds1DrawParam dp, Zone z, Calibration cal, bool scattered = false)
    {
        var sun = LightRig(dp.StageLight(z.Light).Row ?? dp.Row("LightBank", z.Light), cal.Sun, cal.Hemi);
        var chara = LightRig(dp.Row("LightBank", z.Light), cal.Chara, cal.CharaHemi);
        var fog = Fog(dp.Row("FogBank", z.Fog), cal.FogDistance, scattered ? dp.Row("LightScatteringBank", z.Scatter) : null);
        return Features(sun.Diffuse, chara.Diffuse, sun.HemiUp, fog.Color, fog.Distance, fog.Alpha);
    }

    /// <summary>Rule A3: a ranking whose two best areas are closer than this (relative) is a close call: logged with the
    /// runner-up, so the switch can be tested with build-pack --atmos-template (the Asylum: Shulva 4.79 vs FoFG 4.88).</summary>
    public const double CloseCall = 0.05;

    /// <summary>Rule A3: DS2 maps ranked by distance between their main filter id and the DS1 main zone.</summary>
    public static List<(string Map, int Filter, double Distance)> BestTemplate(string ds2Data, double[] ds1)
    {
        var rows = Ds2Maps(ds2Data).Select(kv => (Map: kv.Key, Filter: kv.Value.Used[0], F: Ds2Features(kv.Value.Flt, kv.Value.Used[0])))
            .Where(r => r.F != null).ToList();
        int n = ds1.Length;
        var sd = Enumerable.Range(0, n).Select(i =>
        {
            var v = rows.Select(r => r.F![i]).ToList();
            double m = v.Average();
            return Math.Max(1e-3, Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / v.Count));
        }).ToArray();
        return rows.Select(r => (r.Map, r.Filter, Math.Sqrt(Enumerable.Range(0, n).Sum(i => Math.Pow((r.F![i] - ds1[i]) / sd[i], 2)))))
            .OrderBy(r => r.Item3).ToList();
    }

    // ------------------------------------------------------------------ fltparam

    static GPARAM.Param? Find(GPARAM g, string group, string param) =>
        g.Groups.FirstOrDefault(x => x.Name1 == group)?.Params.FirstOrDefault(p => p.Name1 == param && p.Values.Count > 0);

    /// <summary>The first value of the param with this id (of the first non-empty param of that name).</summary>
    public static object? Get(GPARAM g, string group, string param, int id)
    {
        var p = Find(g, group, param);
        if (p == null) return null;
        int i = p.ValueIDs.IndexOf(id);
        return i < 0 ? null : p.Values[i];
    }

    /// <summary>
    /// Sets every value with this id of EVERY param of that name in the group; returns how many values. DS2 stores the
    /// 4 カスケードシャドウ blocks (計算モード … ピクセルアライメントを行う) two ways (session-8 fix pass, <c>atmos-survey flt
    /// MAP カスケードシャドウ</c>): Things Betwixt / Majula / m10_14: the first block's params hold 4 values per id (n = 64)
    /// and blocks 2-4 are empty; Shulva m50_35 (the Asylum's fallback area): 4 params of the same name with 16 values each.
    /// Setting only the first param of the name left Shulva's 100 m fade in blocks 2-4.
    /// </summary>
    static int Set(GPARAM g, string group, string param, int id, object value)
    {
        int n = 0;
        foreach (var p in g.Groups.Where(x => x.Name1 == group).SelectMany(x => x.Params).Where(p => p.Name1 == param))
            for (int i = 0; i < p.Values.Count; i++)
                if (p.ValueIDs[i] == id) { p.Values[i] = value is byte[] b ? (byte[])b.Clone() : value; n++; }
        return n;
    }

    /// <summary>Copies every value of id <paramref name="from"/> in <paramref name="src"/> onto id <paramref name="to"/>
    /// in <paramref name="dst"/> (k-th to k-th). Both are reads of the same file (same groups/params), so the source stays
    /// pristine while <paramref name="dst"/> is edited (session-8 fix pass: copying inside one GPARAM let zones after the
    /// template id inherit the template id's already-DS1 values; Firelink's bloom alpha was multiplied twice).</summary>
    static void CopyId(GPARAM dst, GPARAM src, int from, int to)
    {
        for (int gi = 0; gi < dst.Groups.Count; gi++)
            for (int pi = 0; pi < dst.Groups[gi].Params.Count; pi++)
            {
                var d = dst.Groups[gi].Params[pi];
                var s = src.Groups[gi].Params[pi];
                var si = Enumerable.Range(0, s.Values.Count).Where(i => s.ValueIDs[i] == from).ToList();
                var di = Enumerable.Range(0, d.Values.Count).Where(i => d.ValueIDs[i] == to).ToList();
                for (int k = 0; k < Math.Min(si.Count, di.Count); k++)
                    d.Values[di[k]] = s.Values[si[k]] is byte[] b ? (byte[])b.Clone() : s.Values[si[k]];
            }
    }

    /// <summary>
    /// Rule A3b (session-8 fix pass): the 平行ライト fields that say how DS2 reads our own GI data — the probe cloud
    /// (PointCloud Scale/Offset 0/10 and their Slot1-3, read with LightProbes' payload) and the GI colour scale (DS1's
    /// lightmaps in g_GITexture) — plus ライトマップを無効にする stay the values of the map the probes and the lightmap gain
    /// were tuned against in game in session 7 (Things Betwixt filter 0: PointCloud Offset 0 = (0,0,0,1), Offset 10 W
    /// 0.375; LightProbes.Brightness 1.6 "TB's probe as is left characters darker", run s7-7), not the best-fit area's
    /// (Shulva filter 12: Offset 0 (0.502,0.502,0.502,0), Offset 10 W 0.125; Majula: Offset 10 W 0.195). They describe
    /// our data, not DS1's look.
    /// </summary>
    public static bool IsProbeField(string name) =>
        name.StartsWith("PointCloud ", StringComparison.Ordinal) || name.StartsWith("GI Color Scale ", StringComparison.Ordinal)
        || name.StartsWith("ライトマップを無効にする", StringComparison.Ordinal);

    static int CopyProbeFields(GPARAM dst, GPARAM probe, int probeId, int to)
    {
        int n = 0;
        const string L = "平行ライト";
        foreach (string name in dst.Groups.Where(x => x.Name1 == L).SelectMany(x => x.Params).Where(p => p.Values.Count > 0 && IsProbeField(p.Name1)).Select(p => p.Name1).Distinct().ToList())
            if (Get(probe, L, name, probeId) is { } v) n += Set(dst, L, name, to, v);
        return n;
    }

    /// <summary>
    /// The map's fltparam: the best-fit DS2 map's file, every zone's filter id first set to that map's ORIGINAL main
    /// filter id values (fallback values, rule A3; read from an untouched copy of the file), the probe/GI read-out fields
    /// from <paramref name="probeMap"/>'s filter <paramref name="probeFilter"/> (rule A3b), then the DS1-derived fields of
    /// the zone's banks written over them.
    /// </summary>
    public static byte[] Filter(string ds2Data, string templateMap, int templateFilter, Plan plan, Ds1DrawParam dp, Calibration cal, List<string> log,
        string probeMap = "m10_02_00_00", int probeFilter = 0, IReadOnlyDictionary<int, int>? palettes = null)
    {
        string path = Path.Combine(ds2Data, "filter", $"filter_{templateMap}.fltparam");
        var g = GPARAM.Read(path);
        var pristine = GPARAM.Read(path);
        var probe = GPARAM.Read(Path.Combine(ds2Data, "filter", $"filter_{probeMap}.fltparam"));
        foreach (var z in plan.Zones)
        {
            int id = z.Filter;
            CopyId(g, pristine, templateFilter, id);
            int probeValues = CopyProbeFields(g, probe, probeFilter, id);
            if (id == plan.Zones[0].Filter) log.Add($"atmos: probe/GI read-out fields (PointCloud*, GI Color Scale*, ライトマップを無効にする*) of every filter id from {probeMap} filter {probeFilter} ({probeValues} values per id; rule A3b)");
            // a stage row DS1 lacks or a cutscene/effect row at that id (Asylum LightBank 64 'character_entrance' has no
            // s_LightBank 64; s_LightBank 36 is 'PolyG-scn100100-<c0000>-cutAll'): the s_LightBank row of the most
            // similar gameplay LightBank row (Ds1DrawParam.StageLight); none at all → the fallback area's
            var (sRow, sId) = dp.StageLight(z.Light);
            if (sRow != null && sId != z.Light)
            {
                var own = dp.Row("s_LightBank", z.Light);
                log.Add($"atmos: filter {id}: s_LightBank {z.Light} {(own == null ? "missing" : $"is a cutscene/effect row '{own.Name}'")}: s_LightBank {sId} '{sRow.Name}' (LightBank {sId} is the gameplay row closest to LightBank {z.Light})");
            }
            var cRow = dp.Row("LightBank", z.Light);
            var sun = LightRig(sRow, cal.Sun, cal.Hemi);
            var chara = LightRig(cRow, cal.Chara, cal.CharaHemi);
            const string L = "平行ライト";
            Set(g, L, "有効", id, true);
            if (sRow != null)
            {
                Set(g, L, "Angle 0", id, sun.Angle);
                Set(g, L, "DiffColor 0", id, sun.Diffuse);
                Set(g, L, "SpecColor 0", id, sun.Specular);
                Set(g, L, "AddHemiColorUp 0", id, sun.HemiUp);
                Set(g, L, "AddHemiColorDown 0", id, sun.HemiDown);
            }
            else log.Add($"atmos: filter {id}: DS1 has no s_LightBank {z.Light}: sun/hemisphere from {templateMap} filter {templateFilter}");
            if (cRow != null)
            {
                Set(g, L, "Angle 10", id, chara.Angle);
                Set(g, L, "DiffColor 10", id, chara.Diffuse);
                Set(g, L, "SpecColor 10", id, chara.Specular);
                Set(g, L, "AddHemiColorUp 10", id, chara.HemiUp);
                Set(g, L, "AddHemiColorDown 10", id, chara.HemiDown);
                Set(g, L, "ライト向きを、ライト0に追従 10", id, false); // DS1's character rig has its own key direction
            }
            else log.Add($"atmos: filter {id}: DS1 has no LightBank {z.Light}: character light from {templateMap} filter {templateFilter}");

            var fog = Fog(dp.Row("FogBank", z.Fog), cal.FogDistance, dp.Row("LightScatteringBank", z.Scatter));
            Set(g, "フォグ", "色", id, fog.Color);
            Set(g, "フォグ", "距離", id, fog.Distance);
            Set(g, "フォグ", "α", id, fog.Alpha);
            Set(g, "フォグ", "上方向に減衰", id, false); // DS1 fog has no height falloff

            // rule A7b (Palette): DS1's ToneCorrect as our own palette in 変換モード 3 (PaletteFile); the HSB fields act only in
            // mode 2 and stay 0 like every vanilla id. Without a palette for the row: the identity palette (no fallback
            // area's tint: Majula's 5036 gave Firelink its yellow-green cast)
            var tc = dp.Row("ToneCorrectBank", z.ToneCorrect);
            int palette = palettes != null && palettes.TryGetValue(z.ToneCorrect, out int pid) ? pid : Palette.Identity;
            Set(g, "カラーグレーディング", "カラーグレーディング有効", id, true);
            Set(g, "カラーグレーディング", "変換モード", id, 3);
            Set(g, "カラーグレーディング", "パレットファイルID", id, palette);
            Set(g, "カラーグレーディング", "色相", id, 0f);
            Set(g, "カラーグレーディング", "彩度", id, 0f);
            Set(g, "カラーグレーディング", "明度", id, 0f);
            var tm = dp.Row("ToneMapBank", z.ToneMap);
            string exposure = "fallback";
            if (tm != null)
            {
                // rule A8c (see ExposureBias): DS1's exposure form on every zone id, adapting as in DS1
                double key = D(tm, "grayKeyValue") > 0 ? D(tm, "grayKeyValue") : cal.Key;
                float lo = (float)Math.Max(1e-4, D(tm, "minAdaptedLum")), hi = (float)Math.Max(lo, D(tm, "maxAdapredLum"));
                float scaler = Get(g, "ToneMap", "Scaler", id) is Vector4 sc ? sc.X : 1.1f;
                float balance = Get(g, "ToneMap", "LuminanceBalance", id) is Vector4 lb && lb.X > 0 ? lb.X : 1f;
                double tau = ToneMatch(key, scaler);
                float avg = (float)(ExposureBias / (tau * key * balance));
                bool wasAdapting = Get(g, "動的順応", "動的順応有効", id) is true;
                object? oldAvg = Get(g, "ToneMap", "AvgLuminance", id), oldLo = Get(g, "動的順応", "順応最低輝度", id), oldSens = Get(g, "動的順応", "順応感度-明", id);
                Set(g, "動的順応", "動的順応有効", id, true);
                Set(g, "動的順応", "順応最低輝度", id, lo);
                Set(g, "動的順応", "順応最高輝度", id, hi);
                Set(g, "動的順応", "順応輝度中心", id, (float)Math.Min(1e-4, lo));
                Set(g, "動的順応", "順応感度-暗", id, 1f);
                Set(g, "動的順応", "順応感度-明", id, 1f);
                if (D(tm, "adaptSpeed") > 0) Set(g, "動的順応", "順応速度", id, (float)D(tm, "adaptSpeed"));
                Set(g, "ToneMap", "AvgLuminance", id, avg);
                double tauR = ToneMatch(key, scaler, Ds1DisplayReinhard);
                exposure = $"exposure (A8c): adapt {lo:0.###}–{hi:0.###} sens 1/1 centre {Math.Min(1e-4, lo):0.####} speed {D(tm, "adaptSpeed"):0.#}, AvgLuminance {avg:0.##} = {ExposureBias:0.##}/(τ {tau:0.###} × key {key:0.###} × LB {balance:0.##}) (DSR Reinhard mode would give τ {tauR:0.###}, Avg {ExposureBias / (tauR * key * balance):0.##}); was {(wasAdapting ? $"adapting min {oldLo} sens {oldSens}" : "fixed exposure")}, Avg {oldAvg}; DS2 exposure now 1/(Avg × clamp(L², {lo:0.###}, {hi:0.###})) = {1 / (avg * hi):0.##}–{1 / (avg * lo):0.##}";
                bool bloom = D(tm, "bloomMul") > 0 && D(tm, "bloomBegin") < 100;
                Set(g, "Bloom", "Bloom有効", id, bloom);
                if (bloom && Get(g, "Bloom", "ブルームアルファ", id) is float ba) Set(g, "Bloom", "ブルームアルファ", id, (float)(ba * D(tm, "bloomMul") / 100));
                Set(g, "ライトシャフト", "ライトシャフト有効", id, D(tm, "lightShaftPower") > 0);
            }
            var dof = dp.Row("DofBank", z.Dof);
            if (dof != null)
            {
                Set(g, "LensSimulation", "FarStart", id, (float)D(dof, "farDofBegin"));
                Set(g, "LensSimulation", "FarEnd", id, (float)D(dof, "farDofEnd"));
                // near blur always off (-1, -2: every DS2 gameplay filter id): DS2 measures Near/Far from its camera focus
                // point (UseCameraFocus), which sits just past the player, not from the camera as DS1 does, so DS1's
                // near band (Firelink: nearDofBegin 0.8 m) blurred the player's own body (user report, session 8)
                Set(g, "LensSimulation", "NearStart", id, -1f);
                Set(g, "LensSimulation", "NearEnd", id, -2f);
                Set(g, "LensSimulation", "DOF Alpha", id, (float)Math.Clamp(D(dof, "farDofMul") / 100, 0, 1));
            }
            var sh = dp.Row("ShadowBank", z.Shadow);
            int shadowValues = 0;
            if (sh != null)
            {
                // all 4 cascade blocks of the id (Set: 4 values of one param, or 4 params of the same name)
                shadowValues = Set(g, "カスケードシャドウ", "影終了フェード開始距離[m]", id, (float)D(sh, "fadeBeginDist"));
                Set(g, "カスケードシャドウ", "影終了フェード距離[m]", id, (float)Math.Max(0.1, D(sh, "fadeDist")));
                byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
                Set(g, "カスケードシャドウ", "Cascade 影の色", id, new[] { B(D(sh, "colR")), B(D(sh, "colG")), B(D(sh, "colB")), B(D(sh, "densityRatio") * 2.55) });
            }
            if (z.LensFlare == 255) Set(g, "レンズフレア", "アルファ", id, 0f);
            log.Add($"atmos: filter {id} ← DS1 s_Light {(sRow == null ? "-" : sId)}/Light {z.Light}, fog {z.Fog}, tone {z.ToneMap}/{z.ToneCorrect}, dof {z.Dof}, shadow {z.Shadow} (fade ×{shadowValues} cascade values), bloom α {Get(g, "Bloom", "ブルームアルファ", id) as float?:0.####}: sun {(sRow == null ? "fallback" : $"{V(sun.Diffuse)} @ {Deg(sun.Angle)}")}, character {(cRow == null ? "fallback" : $"{V(chara.Diffuse)} @ {Deg(chara.Angle)}")}, hemi {(sRow == null ? "fallback" : $"{V(sun.HemiUp)}/{V(sun.HemiDown)}")}, fog #{fog.Color[0]:X2}{fog.Color[1]:X2}{fog.Color[2]:X2} {fog.Distance:0.#} m α {fog.Alpha:0.##} (scatter {z.Scatter}), {exposure}, palette {palette}{(tc == null ? " (no ToneCorrect row)" : "")}");
        }
        byte[] bytes = g.Write();
        // vanilla fltparams are padded to 16 bytes (the archive's AES block); the parser reads by offsets
        if (bytes.Length % 16 != 0) Array.Resize(ref bytes, (bytes.Length + 15) / 16 * 16);
        return bytes;
    }

    static string V(Vector4 v) => $"({v.X:0.##},{v.Y:0.##},{v.Z:0.##})×{v.W:0.###}";
    static string Deg(Vector2 a) => $"({a.X * 180 / MathF.PI:0}°,{a.Y * 180 / MathF.PI:0}°)";

    // ------------------------------------------------------------------ MSB2 events, collisions, player lights

    static System.Drawing.Color C(Vector4 v, int alpha) =>
        System.Drawing.Color.FromArgb(alpha, (int)Math.Clamp(Math.Round(v.X * 255), 0, 255), (int)Math.Clamp(Math.Round(v.Y * 255), 0, 255), (int)Math.Clamp(Math.Round(v.Z * 255), 0, 255));

    /// <summary>MSB2 Light / Fog / BGColor events from the DS1 main zone (colours and directions; the template's
    /// intensity bytes kept: the fltparam above is what DS2 lights a used filter id with, these events are the map's
    /// fallback). Light t00 10 (プレイヤーライト) ← DS1 LightBank, others ← s_LightBank; BGColor = the main fog colour
    /// (DS1 has no clear colour: its sky is geometry).</summary>
    public static void MsbEvents(MSB2 msb, MSB2 tMsb, Plan plan, Ds1DrawParam dp, Calibration cal)
    {
        var z = plan.Main;
        var sun = LightRig(dp.StageLight(z.Light).Row, cal.Sun, cal.Hemi);
        var chara = LightRig(dp.Row("LightBank", z.Light), cal.Chara, cal.CharaHemi);
        var fog = Fog(dp.Row("FogBank", z.Fog), cal.FogDistance, dp.Row("LightScatteringBank", z.Scatter));
        foreach (var e in tMsb.Events.Lights)
        {
            var l = (MSB2.Event.Light)e.DeepCopy();
            var r = l.UnkT00 == 10 ? chara : sun;
            l.UnkT04 = r.Angle.X * 180 / MathF.PI; l.UnkT08 = r.Angle.Y * 180 / MathF.PI;
            l.ColorT0C = C(r.Diffuse, l.ColorT0C.A);
            l.ColorT10 = C(r.Specular, l.ColorT10.A);
            l.ColorT34 = C(r.HemiUp, l.ColorT34.A);
            l.ColorT38 = C(r.HemiDown, l.ColorT38.A);
            l.ColorT3C = C(r.EnvSpecular, l.ColorT3C.A);
            l.UnkT40 *= r.EnvSpecular.W;
            msb.Events.Lights.Add(l);
        }
        foreach (var e in tMsb.Events.Fogs)
        {
            var f = (MSB2.Event.Fog)e.DeepCopy();
            if (f.ColorT04.R != 0 || f.ColorT04.G != 0 || f.ColorT04.B != 0)
            {
                f.ColorT04 = System.Drawing.Color.FromArgb(f.ColorT04.A, fog.Color[0], fog.Color[1], fog.Color[2]);
                if (f.UnkT08 > 0) f.UnkT08 = fog.Distance;
            }
            msb.Events.Fogs.Add(f);
        }
        foreach (var e in tMsb.Events.BGColors)
        {
            var b = (MSB2.Event.BGColor)e.DeepCopy();
            b.Color = System.Drawing.Color.FromArgb(b.Color.A, fog.Color[0], fog.Color[1], fog.Color[2]);
            msb.Events.BGColors.Add(b);
        }
    }

    /// <summary>
    /// DS1 PointLightBank[LanternID] → DS2 PlayerLight rows (regulation), id = AABB × 100 + LanternID (DS2 numbers its
    /// rows the same way: 100260 = m10_02 + 60, 101060 = m10_10 + 60; area 18/19 is unused by DS2). Template = TB's
    /// 100260 ("屋内暗い空間汎用", diffuse 120 %, specular 90 %, radius 10). DS1 colA (percent) → diffuseA as is (DS1's
    /// indoor 120 = TB's 120), specular keeps the template's 90/120 ratio, radius = dwindleEnd.
    /// </summary>
    public static Dictionary<int, int> PlayerLights(Pack pack, Plan plan, Ds1DrawParam dp, int mapPrefix)
    {
        var result = new Dictionary<int, int>();
        var param = pack.Param("PlayerLight");
        var template = param.Rows.FirstOrDefault(r => r.ID == 100260);
        if (template == null) return result;
        foreach (byte lantern in plan.Zones.Select(z => z.Lantern).Distinct())
        {
            var src = dp.Row("PointLightBank", lantern);
            if (src == null) continue;
            int id = mapPrefix * 100 + lantern;
            var row = pack.AddRow("PlayerLight", template, id, $"DS1 a{dp.Area} PointLightBank {lantern} '{src.Name}'");
            void Put(string f, double v) => row[f].Value = Convert.ChangeType(Math.Round(v, row[f].Value is float ? 3 : 0), row[f].Value.GetType());
            Put("diffuseR", D(src, "colR")); Put("diffuseG", D(src, "colG")); Put("diffuseB", D(src, "colB"));
            Put("specularR", D(src, "colR")); Put("specularG", D(src, "colG")); Put("specularB", D(src, "colB"));
            double a = Math.Clamp(D(src, "colA"), 0, 65535);
            double specRatio = Convert.ToDouble(template["specularA"].Value) / Math.Max(1, Convert.ToDouble(template["diffuseA"].Value));
            Put("diffuseA", a); Put("specularA", a * specRatio);
            row["radius"].Value = (float)D(src, "dwindleEnd");
            result[lantern] = id;
        }
        return result;
    }

    /// <summary>The env cubemap (CubeEnvID, collision UnkT2E) the best-fit DS2 area's collisions of that filter id use
    /// most (Shulva filter 12 → CubeEnv 1, 32 collisions), -1 if none.</summary>
    public static int TemplateEnvId(MSB2 template, int filter) =>
        template.Parts.Collisions.Where(c => c.UnkT13 == filter && c.UnkT2E >= 0).GroupBy(c => c.UnkT2E)
            .OrderByDescending(g => g.Count()).Select(g => (int)g.Key).DefaultIfEmpty(0).First();

    /// <summary>The DS2 collision fields of a DS1 zone: FilterID, PlayerLight row, env cubemap, no player-attached
    /// ambience.</summary>
    public static void ApplyCollision(MSB2.Part.Collision c, Zone z, IReadOnlyDictionary<int, int> playerLights, int envId = -2)
    {
        if (envId >= -1) c.UnkT2E = (short)envId; // CubeEnvID (TB: 0/1/2, -1 none) → the best-fit area's cube
        c.UnkT13 = (byte)z.Filter;   // FilterID → fltparam value id
        c.UnkT15 = 0;                // AmbientSfxAttachType
        c.UnkT18 = 0;                // AmbientSfxID (TB's 3620): DS1 attaches no ambience to the player
        if (playerLights.TryGetValue(z.Lantern, out int pl)) c.UnkT1C = pl; // PlayerLightSfxID → PlayerLight row
    }

    // ------------------------------------------------------------------ ambience SFX

    /// <summary>DS1 map effect → DS2 effect of the same kind (developer names: DS1 MSB SFX event names, DS2 SFX region
    /// names "SFXnnnn【name】"); 0 = handled elsewhere (DS1's fog-wall glows belong to the white-door objects).</summary>
    public static readonly Dictionary<int, (int Ds2, string Why)> EffectMap = new()
    {
        [530] = (3020, "DS1 松明 (torch) → DS2 3020 【松明小】 torch, small (Heide, Bastille, Shrine of Amana)"),
        [100201] = (3011, "DS1 靄 (mist, Firelink) → DS2 3011 【もや】 haze"),
        [81802] = (0, "DS1 光の壁 (fog wall glow): part of the DS2 white door"),
        [81805] = (0, "DS1 光の壁 (fog wall glow): part of the DS2 white door"),
        [81806] = (0, "DS1 光の壁 (fog wall glow): part of the DS2 white door"),
        // session 10 (asy-sound, rule SND5): the Asylum hall floor's collapse (event 11810220, 2006:3 one-shot at o8510) →
        // DS2's own one-shot debris burst, the effect DS2's collapse event in m20_10 plays with 131503 (keyed regions 10/20
        // 【天井土砂（着弾）】); a stand-in (P) until rule B2b renders DS1's own FFX
        [180100] = (114, "DS1 floor-collapse dust (event one-shot) → DS2 114 【天井土砂（着弾）】 ceiling debris impact (stand-in, P)"),
        // session 11 (atmos11), the Burg's map effects (rule B1, same kind by developer name; DS1's own FFX would be rule
        // B2b, which loads but draws nothing yet): DS1 511 蜀台 (candelabra: cone + sphere emitters, candle flames) → DS2
        // 3041 【ロウソクの炎】 candle flame (Majula, Brightstone, Heide: 29 regions); DS1 100101/100102 燃える死体（多/少）
        // (burning corpse piles: flame boxes 0.35 × 2 × 0.35 m in circles of 0.45–1 m + smoke/embers) → DS2 3063
        // 【タール炎半径1m】 ground fire of radius 1 m (DS2's fires by radius: 3061 2.5 m, 3062 1.5 m, 3063 1 m; 3025 is a
        // torch tip); all three carry DS2's light action 20002 like the torches (DS1 lights its burning spots with MSB
        // Light events instead: rule L1)
        [511] = (3041, "DS1 蜀台 (candelabra flames) → DS2 3041 【ロウソクの炎】 candle flame (Majula, Brightstone, Heide)"),
        [100101] = (3063, "DS1 燃える死体（多） (burning corpse pile, flames in a 1 m circle) → DS2 3063 【タール炎半径1m】 ground fire, radius 1 m"),
        [100102] = (3063, "DS1 燃える死体（少） (burning corpse pile, flames in a 0.45 m circle) → DS2 3063 【タール炎半径1m】 ground fire, radius 1 m (DS2's smallest ground fire)"),
        // rule B3 (session 11): DS1's camera-relative ember ambience (box 10 × 10 × 10 m, emitter movement mode 3, partial
        // follow 105: exactly the three 火の粉 effects of DSR, atmos-check ffx-survey) → DS2's player-attached ambience of
        // the same kind on the collisions (AmbientSfxID): 3602, FoFG's (21 collisions) small glowing billboards of texture 45
        // (DS2's grey 8 × 4 sprite sheet, the layout of DS1's ember sheet 805) tinted (0.78 × (5,3,1)) = orange, box 20 × 5 × 20
        [100040] = (3602, "DS1 マップに振る火の粉 (embers around the camera, the whole Burg) → DS2 3602 FoFG's player-attached ember ambience (rule B3)"),
        [140005] = (3602, "DS1 火の粉 (embers around the camera, Lost Izalith) → DS2 3602 FoFG's player-attached ember ambience (rule B3)"),
    };

    /// <summary>Rule FW2 (session 11, atmos11): a DS1 SFX event named 光の壁 (light wall) is the glow of a fog wall, which
    /// rule FW1 places as a DS2 white door (its own glow); the Asylum's 81802/81805/81806 and the Burg's 81003–81021 (19).</summary>
    public static bool IsFogWallGlow(MSB1.Event.SFX e) => e.Name.Contains("光の壁") || EffectMap.TryGetValue(e.EffectID, out var m) && m.Ds2 == 0 && m.Why.Contains("光の壁");

    /// <summary>
    /// <b>Rule B3</b> (session 11, atmos11): a DS1 map effect whose particles follow the camera (a box emitter with
    /// EmitterMovementMode 3 and the partial-follow action 105: DSR's three 火の粉 effects — the Burg's 100040 マップに振る
    /// 火の粉, Oolacile's 120058, Izalith's 140005; their MSB points lie outside the playable area, the Burg's 60 m above
    /// any floor) is DS1's map-wide ambience. DS2's counterpart is the player-attached ambience of the collision the player
    /// stands on (MSB2 collision AmbientSfxID UnkT18 + attach type UnkT15; vanilla FoFG 3602 ×21, Shaded Woods 3606 ×33,
    /// TB 3620 ×24). Every DS1 walkable collision DS1 draws the effect with (its DrawGroups intersect the SFX part's)
    /// gets the mapped DS2 effect; the attach type is the one vanilla uses with that effect.
    /// Returns DS1 collision part name → (DS2 effect, attach type).
    /// </summary>
    public static Dictionary<string, (int Id, byte Attach)> Ambience(MSB1 msb1, string dsrRoot, string ds1Map, string ds2Data, List<string> log)
    {
        var result = new Dictionary<string, (int, byte)>();
        foreach (var e in msb1.Events.SFX)
        {
            var fp = MapEffects.Ds1(dsrRoot, ds1Map, e.EffectID);
            if (fp is not { Follow: true }) continue;
            if (!EffectMap.TryGetValue(e.EffectID, out var map) || map.Ds2 == 0) { log.Add($"atmos: rule B3: DS1 SFX {e.Name} ffx {e.EffectID} follows the camera but has no DS2 ambience mapped (deviation)"); continue; }
            var part = msb1.Parts.GetEntries().FirstOrDefault(p => p.Name == e.PartName);
            uint[]? groups = part?.DrawGroups;
            byte attach = VanillaAttach(ds2Data, map.Ds2);
            int n = 0;
            foreach (var c in msb1.Parts.Collisions.Where(c => c.HitFilterID == 8))
            {
                bool drawn = groups == null || c.DrawGroups.Zip(groups).Any(z => (z.First & z.Second) != 0);
                if (!drawn || result.ContainsKey(c.Name)) continue;
                result[c.Name] = (map.Ds2, attach);
                n++;
            }
            log.Add($"atmos: rule B3: DS1 SFX {e.Name} ffx {e.EffectID} ({fp.Note}) → DS2 collision ambience {map.Ds2} attach {attach} on {n} of {msb1.Parts.Collisions.Count(c => c.HitFilterID == 8)} walkable collisions (draw groups of {e.PartName}); {map.Why}");
        }
        return result;
    }

    static readonly Dictionary<int, byte> vanillaAttach = new();

    /// <summary>The AmbientSfxAttachType (UnkT15) vanilla DS2 collisions use with an AmbientSfxID (the most common; 0 if unused).</summary>
    static byte VanillaAttach(string ds2Data, int id)
    {
        if (vanillaAttach.TryGetValue(id, out byte a)) return a;
        var counts = new Dictionary<byte, int>();
        foreach (string dir in Directory.GetDirectories(Path.Combine(ds2Data, "map")))
        {
            string p = Path.Combine(dir, Path.GetFileName(dir) + ".msb");
            if (!File.Exists(p)) continue;
            MSB2 m;
            try { m = MSB2.Read(p); } catch { continue; }
            foreach (var c in m.Parts.Collisions.Where(c => c.UnkT18 == id)) counts[c.UnkT15] = counts.GetValueOrDefault(c.UnkT15) + 1;
        }
        return vanillaAttach[id] = counts.Count == 0 ? (byte)0 : counts.MaxBy(k => k.Value).Key;
    }

    /// <summary>Rule B3: the player-attached ambience of a DS2 collision placed for a DS1 collision part.</summary>
    public static void ApplyAmbience(MSB2.Part.Collision c, string ds1Part, IReadOnlyDictionary<string, (int Id, byte Attach)> ambience)
    {
        if (!ambience.TryGetValue(ds1Part, out var a)) return;
        c.UnkT18 = a.Id;
        c.UnkT15 = a.Attach;
    }

    static int NextRegionIndex(MSB2 msb) => msb.Regions.GetEntries().Select(r => (int)((MSB2.Region)r).Unk08).DefaultIfEmpty(-1).Max() + 1;

    /// <summary>DS1 MSB1 SFX events → DS2 MSB2 SFX regions at the DS1 region (point), effect via <see cref="EffectMap"/>;
    /// an area emitter is tiled over DS1's footprint at DS1's height (rule B1b, <see cref="MapEffects"/>; skylight fix
    /// pass). <paramref name="dsrRoot"/> null = one copy per DS1 point (as before).</summary>
    public static int SfxRegions(MSB2 msb, MSB2 tMsb, MSB1 msb1, List<string> log, string? dsrRoot = null, string? ds1Map = null)
    {
        var template = tMsb.Regions.SFXs.FirstOrDefault();
        if (template == null) return 0;
        int n = 0;
        foreach (var e in msb1.Events.SFX)
        {
            // session 11 (atmos11): rule FW2 (fog-wall glows belong to the white doors) and rule B3 (camera-relative
            // ambience → collision AmbientSfxID, see Ambience) place no region
            if (IsFogWallGlow(e)) { log.Add($"atmos: DS1 SFX {e.Name} ffx {e.EffectID}: fog-wall glow, part of the DS2 white door (rule FW2)"); continue; }
            if (dsrRoot != null && ds1Map != null && MapEffects.Ds1(dsrRoot, ds1Map, e.EffectID) is { Follow: true }) { log.Add($"atmos: DS1 SFX {e.Name} ffx {e.EffectID}: camera-relative ambience, on the collisions (rule B3), no region"); continue; }
            if (!EffectMap.TryGetValue(e.EffectID, out var map)) { log.Add($"atmos: DS1 SFX {e.Name} ffx {e.EffectID} not mapped (no DS2 counterpart chosen)"); continue; }
            if (map.Ds2 == 0) { log.Add($"atmos: DS1 SFX {e.Name} ffx {e.EffectID}: {map.Why}"); continue; }
            var region = msb1.Regions.GetEntries().FirstOrDefault(r => r.Name == e.RegionName);
            if (region == null) continue;
            string why = "one copy at DS1's point";
            // session 9 (mist track): placement mode (MapEffects.Mode, --map-sfx): B1d by default (after the s9 A/B); Ds1 =
            // rule B2b (DS1's own area effect converted, FfxPorter, under a DS2 id from rule B2b-id); MistOverride = another
            // DS2 mist for A/B
            int ds2Id = MapEffects.MistOverride != 0 && map.Ds2 == 3011 ? MapEffects.MistOverride : map.Ds2;
            var fp1 = dsrRoot != null && ds1Map != null ? MapEffects.Ds1(dsrRoot, ds1Map, e.EffectID) : null;
            List<System.Numerics.Vector3> spots;
            if (MapEffects.Mode == MapEffects.SfxMode.Ds1 && fp1 is { Area: true } && FfxPorter.Convert(dsrRoot!, ds1Map!, e.EffectID, log) is { } conv)
            {
                ds2Id = conv.Id;
                spots = [region.Position];
                why = $"rule B2b: DS1's own effect converted (DS2 id {conv.Id}), one copy at DS1's point";
            }
            else if (fp1 != null)
            {
                var fp2 = MapEffects.Ds2(Program.DefaultDs2Root, ds2Id);
                bool area = fp1.Area && fp2 is { Area: true };
                spots = MapEffects.PlacementsFor(region.Position, region.Rotation.Y, fp1, fp2,
                    area ? MapEffects.LiveDs1(dsrRoot!, ds1Map!, e.EffectID) : 0, area ? MapEffects.LiveDs2(ds2Id) : 0, out why);
            }
            else spots = [region.Position];
            for (int k = 0; k < spots.Count; k++)
            {
                var s = (MSB2.Region.SFX)template.DeepCopy();
                s.Name = spots.Count > 1 ? $"SFX{ds2Id}【DS1 {e.EffectID} {e.Name} {k + 1}/{spots.Count}】" : $"SFX{ds2Id}【DS1 {e.EffectID} {e.Name}】";
                s.EffectID = ds2Id;
                s.UnkT04 = 0;
                s.Position = spots[k];
                s.Rotation = region.Rotation;
                s.Shape = new MSB.Shape.Point();
                s.Unk08 = (short)NextRegionIndex(msb);
                msb.Regions.SFXs.Add(s);
                n++;
            }
            if (spots.Count > 1 || why != "one copy at DS1's point" && e.EffectID != 530)
                log.Add($"atmos: DS1 SFX {e.Name} ffx {e.EffectID} at {region.Position} → DS2 {ds2Id}: {why} (map-sfx {MapEffects.Mode}{(MapEffects.MistOverride != 0 ? $", mist {MapEffects.MistOverride}" : "")})");
            // mist fix pass (A/B only, --sfx-control ID): a positive control 1 m above each DS1 area-effect point, a DS2
            // effect known to render in our maps (3020, the m18_01 torches) → shows whether this map's SFX regions spawn;
            // only for area effects as rule B1c sees them (DS1 AND the mapped DS2 effect are area emitters: the mists,
            // not the torches, whose DS1 530 has a box emitter but maps to DS2's point torch 3020)
            if (MapEffects.ControlEffect != 0 && fp1 is { Area: true } && MapEffects.Ds2(Program.DefaultDs2Root, map.Ds2) is { Area: true })
            {
                var c = (MSB2.Region.SFX)template.DeepCopy();
                c.Name = $"SFX{MapEffects.ControlEffect}【A/B control at DS1 {e.EffectID} {e.Name}】";
                c.EffectID = MapEffects.ControlEffect;
                c.UnkT04 = 0;
                c.Position = region.Position + new System.Numerics.Vector3(0, 1, 0);
                c.Rotation = new System.Numerics.Vector3(0, region.Rotation.Y, 0);
                c.Shape = new MSB.Shape.Point();
                c.Unk08 = (short)NextRegionIndex(msb);
                msb.Regions.SFXs.Add(c);
                n++;
                log.Add($"atmos: A/B control: DS2 {MapEffects.ControlEffect} at {c.Position} (DS1 {e.EffectID} {e.Name} point + 1 m)");
            }
        }
        log.Add($"atmos: {n} DS2 SFX regions from {msb1.Events.SFX.Count} DS1 map effects");
        LightEvents(msb1, log);
        return n;
    }

    /// <summary>DS2 effects that carry their own point light (action 20002 in sfx9999: the torches 3020/3021/3025, the
    /// candle 3041, the ground fires 3000/3061–3063; not the mist 3011 or the ambience 3602).</summary>
    public static readonly HashSet<int> LitEffects = [3020, 3021, 3025, 3041, 3000, 3061, 3062, 3063];

    /// <summary>
    /// <b>Rule L1</b> (session 11, atmos11): DS1 MSB Light events (point lights, PointLightBank rows) sit at its torches
    /// and candelabras (the Burg: all 52; the Asylum: 30 of 32). DS2 lights its torches with the effect itself (action
    /// 20002 in every DS2 fire effect, <see cref="LitEffects"/>), and has no MSB point-light event: a DS1 light within
    /// 2 m of a DS1 SFX event mapped to a lit DS2 effect is carried by that effect; any other (the Asylum's gate_light and
    /// fluted-ceiling light, Firelink's three lift lights) is a logged deviation.
    /// </summary>
    public static (int Carried, List<string> Missing) LightEvents(MSB1 msb1, List<string> log)
    {
        var regions = msb1.Regions.GetEntries().ToDictionary(r => r.Name, r => r.Position);
        var fires = msb1.Events.SFX.Where(s => EffectMap.TryGetValue(s.EffectID, out var m) && LitEffects.Contains(m.Ds2) && regions.ContainsKey(s.RegionName))
            .Select(s => (Pos: regions[s.RegionName], Ds2: EffectMap[s.EffectID].Ds2)).ToList();
        int carried = 0;
        var missing = new List<string>();
        foreach (var l in msb1.Events.Lights)
        {
            if (!regions.TryGetValue(l.RegionName ?? "", out var p)) { missing.Add($"{l.Name} (no region)"); continue; }
            var near = fires.Where(f => Vector3.Distance(f.Pos, p) <= 2f).OrderBy(f => Vector3.Distance(f.Pos, p)).ToList();
            if (near.Count > 0) { carried++; continue; }
            missing.Add($"'{l.Name}' PointLightBank {l.PointLightID} at {p}");
        }
        log.Add($"atmos: rule L1: {carried} of {msb1.Events.Lights.Count} DS1 point lights carried by the light of a DS2 fire effect within 2 m"
            + (missing.Count > 0 ? $"; not translated (deviation, DS2 has no MSB point light): {string.Join(", ", missing)}" : ""));
        return (carried, missing);
    }

    // ------------------------------------------------------------------ sound (default since session 10, --no-ds1-sound)

    /// <summary>
    /// The DS1 map's sound plan (<see cref="Events.MapSounds"/>, rules SND1/SND4/SND5) placed in the MSB2: every DS1 Sound
    /// event → an MSB2 Sound region (DS2 type 0 'a' / 1 'm', DS1's position, rotation and shape; shapes DS2 does not play
    /// (circle, cylinder, rectangle) → a point, as DS1 plays them), key UnkT08 = 100 + n for the sounds DS1's events
    /// switch (2010:3), 0 = plays while in range; every ported 2010:2 Play SE → a keyed point Sound region at the entity;
    /// every mapped 2006:3 one-shot SFX → a keyed point SFX region (UnkT04) at the entity.
    /// </summary>
    public static int SoundRegions(MSB2 msb, MSB2 tMsb, MSB1 msb1, string dsrRoot, string ds2Data, string ds1Map, List<string> log)
    {
        var template = tMsb.Regions.Sounds.FirstOrDefault();
        if (template == null) { log.Add("atmos: the template map has no Sound region to clone, DS1 sound regions not placed"); return 0; }
        string emevdPath = Path.Combine(dsrRoot, "event", ds1Map + ".emevd.dcx");
        if (!File.Exists(emevdPath)) { log.Add($"atmos: no DS1 event script {ds1Map}, DS1 sound regions not placed"); return 0; }
        var plan = Events.MapSounds.Plan(msb1, EMEVD.Read(emevdPath), Events.MapSounds.Names(dsrRoot, ds2Data, ds1Map));
        int n = 0;
        foreach (var r in plan.Regions)
        {
            var region = msb1.Regions.GetEntries().First(x => x.Name == r.RegionName);
            var s = (MSB2.Region.Sound)template.DeepCopy();
            s.Name = r.Name;
            s.UnkT00 = r.Ds2Type;
            s.SoundID = r.SoundId;
            s.UnkT08 = r.Key;
            s.Position = region.Position;
            s.Rotation = region.Rotation;
            s.Shape = region.Shape is MSB.Shape.Point or MSB.Shape.Sphere or MSB.Shape.Box ? region.Shape.DeepCopy() : new MSB.Shape.Point();
            s.Unk08 = (short)NextRegionIndex(msb);
            msb.Regions.Sounds.Add(s);
            log.Add($"atmos: sound region '{r.Name}' {r.Ds2Event}{(r.Ds2Event != r.Ds1Event ? $" (DS1 {r.Ds1Event}, rule SND3)" : "")} {s.Shape.GetType().Name}{(s.Shape.GetType() != region.Shape.GetType() ? $" (DS1 {region.Shape.GetType().Name})" : "")} "
                + (r.Key != 0 ? $"key {r.Key} (switched by DS1 events{(plan.StartIn.TryGetValue(r.Ds1Entity, out long host) ? $", ON at start: event {host}'s group starts it first" : "")})" : "plays in range"));
            n++;
        }
        foreach (var e in plan.Effects)
        {
            var s = (MSB2.Region.Sound)template.DeepCopy();
            s.Name = $"SE {e.Ds2Event}【DS1 2010:2 at {e.Entity}】";
            s.UnkT00 = 0;
            s.SoundID = int.Parse(e.Ds2Event[1..]);
            s.UnkT08 = e.Key;
            s.Position = e.Position;
            s.Rotation = Vector3.Zero;
            s.Shape = new MSB.Shape.Point();
            s.Unk08 = (short)NextRegionIndex(msb);
            msb.Regions.Sounds.Add(s);
            log.Add($"atmos: event SE {e.Ds1Event} at {e.Entity} {e.Position} → point region {e.Ds2Event} key {e.Key} (131203, rule SND4)");
            n++;
        }
        var sfxTemplate = tMsb.Regions.SFXs.FirstOrDefault();
        foreach (var o in plan.OneShots)
        {
            if (sfxTemplate == null) { log.Add("atmos: the template map has no SFX region to clone, one-shot SFX not placed"); break; }
            var x = (MSB2.Region.SFX)sfxTemplate.DeepCopy();
            x.Name = $"SFX{o.Ds2Effect}【DS1 2006:3 {o.Ds1Sfx} at {o.Entity}】";
            x.EffectID = o.Ds2Effect;
            x.UnkT04 = o.Key;
            x.Position = o.Position;
            x.Rotation = Vector3.Zero;
            x.Shape = new MSB.Shape.Point();
            x.Unk08 = (short)NextRegionIndex(msb);
            msb.Regions.SFXs.Add(x);
            log.Add($"atmos: event one-shot SFX {o.Ds1Sfx} at {o.Entity} {o.Position} → DS2 {o.Ds2Effect} key {o.Key} (131503, rule SND5)");
        }
        foreach (string note in plan.Notes) log.Add("atmos: " + note);
        return n;
    }

    /// <summary>
    /// DS1's FMOD projects for the map's area, loadable by DS2 (rule SND2; session 10 rework of session 8's opt-in), in
    /// DS2's own layout (V-data: DS2's frpg2_m.fev lives in frpg2.fevbnd and names one wave bank per map, frpg2_m1002 …
    /// frpg2_m4003, which each map's sound/frpg2_mAABB.bnd supplies as entry id 1, preloaded under "frpg2_mAABB.fsb";
    /// stream banks frpg2_smAABB are loose FSBs FMOD opens by name). The projects are the ones DS1 loads for the area
    /// (<see cref="Events.MapSounds.Ds1Projects"/>: frpg_mAA + frpg_smAA, and fdlc_m12 + fdlc_sm12 for area 12, fix pass):
    ///  - the first memory project, frpg_mAA.fev (map one-shots, object and material sounds) → frpg2.fevbnd, its wave-bank
    ///    references renamed frpg_mAA → frpg2_mAABB (the project keeps DS1's name: a project named like the resource would
    ///    make DS2 skip the map bank as already loaded, FUN_1409f1330); sound/frpg2_mAABB.bnd = DS1's frpg_mAA.fsb as
    ///    entry id 1 (no loose copy: the per-map preload is DS2's lifecycle, loaded and released with the map);
    ///  - a further memory project (fdlc_m12) → frpg2.fevbnd with DS1's bank name kept, its FSB loose in title:/sound
    ///    (DS2's map bnd has one preloaded entry; FMOD opens a bank no preload supplies from the media folder by name, the
    ///    stream banks' route; P in game);
    ///  - stream projects (frpg_smAA, fdlc_sm12: BGM 'm', ambience loops, voices) → frpg2.fevbnd unchanged apart from
    ///    SND3, their FSBs loose in title:/sound; sound/frpg2_smAABB.bnd = the stream placeholder naming the first one
    ///    (V in game s9-12 for the voices, <see cref="Talk.TalkVoices"/>, which renames its voice events on top of it);
    ///  - SND3 in our copies, each in the project that defines the name: the DS1 events the plan renames ('o' → 'a', an
    ///    alias no DS1 area project or DS2 defines, <see cref="Events.MapSounds.Renames"/>), and every event name DS2's
    ///    own projects define (frpg_m18: 8 'c' material sounds, frpg_m10: 5) → 'q' + digits (no game requests 'q': DS1
    ///    "acfopsmvxbgy", DS2 "smacpoz"/a/m/y/f/o/z/h/v), so DS2's resolver never finds DS1's copy for a DS2 sound.
    /// Limit (like the voices): one DS1 area project serves one DS2 map; a second DS2 map from the same DS1 area is refused
    /// until the area's banks are shared (the loose route; see NOTES). Returns the pack files it wrote (device, path).
    /// </summary>
    /// <summary>Session 11 (burg11), rule SND4: DS1 areas whose memory project serves more than one DS2 host in this build
    /// (area 10: Firelink m19_42 + the Burg m19_41). Their memory project keeps DS1's bank name, its FSB ships loose once in
    /// title:/sound (FMOD opens it by name at first use; it survives either map unloading) and every host's
    /// frpg2_mAABB.bnd is a placeholder (vanilla's Dummy entry, no preload): DS2 preloads a bank only under the bnd's own
    /// name and skips a second preload of a name, so one project's single bank can't be preloaded by two maps
    /// (research\fmod-share.md, V-code FUN_1409d81b0 + offline probes). Areas not listed keep the preload route
    /// (verified), so a pack without the Burg is unchanged. Set by build-pack before the maps are built.</summary>
    public static HashSet<int> SharedAreas { get; } = [];

    public static List<(string Device, string Path)> SoundBanks(Pack pack, string dsrRoot, string ds2Data, string ds1Map, string nId, List<string> log)
    {
        var written = new List<(string, string)>();
        int areaN = int.Parse(ds1Map.Substring(1, 2));
        string aabb = nId.Substring(1, 2) + nId.Substring(4, 2);
        string snd = Path.Combine(dsrRoot, "sound");
        var areaProjects = Events.MapSounds.Ds1ProjectsFor(ds1Map); // session 11 (burg11, round 8): + rule H2-S
        bool shared = SharedAreas.Contains(areaN); // rule SND4
        string m = areaProjects[0].Memory, sm = areaProjects[0].Stream, bank = shared ? areaProjects[0].Memory : $"frpg2_m{aabb}";
        if (!File.Exists(Path.Combine(snd, m + ".fev"))) { log.Add($"atmos: no DS1 sound project {m}"); return written; }
        string emevdPath = Path.Combine(dsrRoot, "event", ds1Map + ".emevd.dcx");
        var plan = File.Exists(emevdPath)
            ? Events.MapSounds.Plan(MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", ds1Map + ".msb")), EMEVD.Read(emevdPath), Events.MapSounds.Names(dsrRoot, ds2Data, ds1Map)) : null;

        // the fevbnd (accumulates over maps: Asylum + Firelink add areas 18 and 10; the voices may have added smAA already)
        string vanillaFevbnd = Path.Combine(ds2Data, "sound", "frpg2.fevbnd.dcx");
        byte[] fevBytes = pack.TryGetFile("gamedata", "sound/frpg2.fevbnd.dcx") ?? File.ReadAllBytes(vanillaFevbnd);
        var fevbnd = BND4.Read(DCX.Decompress(fevBytes, out DCX.CompressionInfo dcx));
        var ds2Names = Events.MapSounds.Ds2Index(ds2Data).Keys.ToHashSet();
        var flags = fevbnd.Files[0].Flags;
        var shipped = areaProjects.SelectMany(p => new[] { p.Memory, p.Stream }).Where(p => File.Exists(Path.Combine(snd, p + ".fev"))).ToList();
        foreach (string proj in shipped)
        {
            string name = $@"N:\FRPG2_64\SoundOutTemp\win64\{proj}\{proj}.fev";
            var existing = fevbnd.Files.FirstOrDefault(f => f.Name == name);
            byte[] src = existing?.Bytes ?? File.ReadAllBytes(Path.Combine(snd, proj + ".fev"));
            // rule SND4: a shared area's project is put in once (DS1's bank name); a later host only adds its own SND3 aliases
            if (shared && existing != null) log.Add($"atmos: DS1 sound project {proj}.fev already in frpg2.fevbnd: shared by the area's hosts (rule SND4)");
            if (proj == m && existing != null && !ContainsLstr(existing.Bytes, bank))
                throw new InvalidOperationException($"{m}.fev already serves another DS2 map (its wave bank is not {bank}): one DS1 area project per DS2 map (rule SND2 limit; see asy-sound NOTES)");
            var renames = new Dictionary<string, string>();
            var own = Commands.SoundSurvey.FevIndex([(proj, src)]).Keys.ToHashSet();
            foreach (var (from, to) in plan?.Renames ?? [])
                if (own.Contains(from))
                {
                    if (own.Contains(to)) { log.Add($"atmos: SND3 alias {from} → {to} refused: {proj} already defines {to}"); continue; }
                    renames[from] = to;
                }
            foreach (string c in own.Where(ds2Names.Contains)) renames[c] = "q" + c[1..];
            if (proj == m && !shared) renames[m] = bank;
            byte[] fev = RenameLstrs(src, renames, out var done);
            foreach (var d in done) AppliedRenames[(proj, d.Key)] = d.Value; // session 11 (snd11): SND7 copies name the current event
            if (existing != null) existing.Bytes = fev;
            else fevbnd.Files.Add(new BinderFile(flags, fevbnd.Files.Max(f => f.ID) + 1, name, fev));
            log.Add($"atmos: DS1 sound project {proj}.fev {(existing != null ? "updated" : "added")} in frpg2.fevbnd: "
                + string.Join(", ", done.GroupBy(d => d.Key == m ? "wave bank → " + bank : d.Value[0] == 'q' ? "DS2 name → q (SND3)" : "alias (SND3)")
                    .Select(g => $"{g.Key} {(g.Key.StartsWith("wave") ? g.Single().Value + $" ×{g.Single().Count}" : string.Join(" ", g.Select(d => $"{d.Key}→{d.Value}")))}")));
        }
        written.AddRange(ImportEvents(pack, fevbnd, snd, shipped, m, plan, log));
        pack.AddFile("gamedata", "sound/frpg2.fevbnd.dcx", DCX.Compress(fevbnd.Write(), dcx));
        written.Add(("gamedata", "sound/frpg2.fevbnd.dcx"));

        // frpg2_mAABB.bnd: DS1's memory wave bank as entry id 1 (DS2 preloads it under the resource name = the renamed bank)
        // frpg2_smAABB.bnd: the stream placeholder
        byte[] smRaw = File.ReadAllBytes(Path.Combine(ds2Data, "sound", "frpg2_sm1002.bnd"));
        bool smDcx = DCX.Is(smRaw);
        DCX.CompressionInfo? smInfo = null;
        if (smDcx) { smRaw = DCX.Decompress(smRaw, out DCX.CompressionInfo info); smInfo = info; }
        var smBnd = BND4.Read(smRaw);
        var dummy = smBnd.Files[0];
        var mBnd = BND4.Read(Path.Combine(ds2Data, "sound", "frpg2_m1002.bnd"));
        var mFlags = mBnd.Files[0].Flags;
        mBnd.Files.Clear();
        string mBndName = $"frpg2_m{aabb}";
        if (shared) // rule SND4: no entry 1 = no preload; the placeholder only registers a name (V-code FUN_140b05db0)
            mBnd.Files.Add(new BinderFile(dummy.Flags, dummy.ID, $@"N:\FRPG2_64\SoundOutTemp\win64\{mBndName}\{mBndName}.dm", dummy.Bytes));
        else
            mBnd.Files.Add(new BinderFile(mFlags, 1, $@"N:\FRPG2_64\SoundOutTemp\win64\{bank}\{bank}.fsb", File.ReadAllBytes(Path.Combine(snd, m + ".fsb"))));
        pack.AddFile("gamedata", $"sound/{mBndName}.bnd", mBnd.Write());
        written.Add(("gamedata", $"sound/{mBndName}.bnd"));
        smBnd.Files.Clear();
        smBnd.Files.Add(new BinderFile(dummy.Flags, dummy.ID, $@"N:\FRPG2_64\SoundOutTemp\win64\{sm}\{sm}.dm", dummy.Bytes));
        byte[] smOut = smBnd.Write();
        pack.AddFile("gamedata", $"sound/frpg2_sm{aabb}.bnd", smInfo != null ? DCX.Compress(smOut, smInfo) : smOut);
        written.Add(("gamedata", $"sound/frpg2_sm{aabb}.bnd"));
        // loose in title:/Sound (FMOD opens them by DS1's bank name): every stream bank, and every memory bank but the
        // preloaded first one
        var loose = shipped.Where(p => (shared || p != m) && File.Exists(Path.Combine(snd, p + ".fsb"))).ToList();
        foreach (string p in loose)
        {
            pack.AddFile("title", $"sound/{p}.fsb", File.ReadAllBytes(Path.Combine(snd, p + ".fsb")));
            written.Add(("title", $"sound/{p}.fsb"));
        }
        log.Add(shared
            ? $"atmos: DS1 sound banks for {nId} (rule SND4, area {areaN} shared by several hosts): {mBndName}.bnd placeholder (no preload), {m}.fsb loose under DS1's bank name, frpg2_sm{aabb}.bnd (stream {sm}), loose title/sound/{string.Join(", ", loose.Select(p => p + ".fsb"))}"
            : $"atmos: DS1 sound banks for {nId}: {bank}.bnd ({m}.fsb, {new FileInfo(Path.Combine(snd, m + ".fsb")).Length:N0} B, preloaded per map), frpg2_sm{aabb}.bnd (stream {sm}), loose title/sound/{string.Join(", ", loose.Select(p => p + ".fsb"))}");
        return written;
    }

    /// <summary>Session 11 (snd11): (DS1 project, DS1 event name) → the name our fevbnd copy now gives it (SND3 'a' alias,
    /// 'q' rename), over every map of this build (a shared area's project is renamed by several hosts).</summary>
    public static Dictionary<(string Project, string Ds1Event), string> AppliedRenames { get; } = [];

    static string FevPath(string proj) => $@"N:\FRPG2_64\SoundOutTemp\win64\{proj}\{proj}.fev";

    /// <summary>
    /// Session 11 (snd11), rules SND6 / SND7: copies DS1 events into our fevbnd copies under the names DS2 will ask for.
    ///  - SND6 (<see cref="Events.MapSounds.Imports"/>): events of DS1's global stream project frpg_smain the map's SEs name
    ///    (the boss kill sting s777777777 → its 'a' alias) → into the area memory project <paramref name="m"/>; frpg_smain's
    ///    stream bank (load mode 0x80, as in DS1) ships loose as title:/sound/frpg_smain.fsb (FMOD opens it by name, the
    ///    route of the loose stream banks).
    ///  - SND7 (<see cref="ObjectSounds.Pending"/>): a ported object's "o{model}{id}" → a copy of the DS1 event in our copy
    ///    of the area memory project when it defines it (its current name: <see cref="AppliedRenames"/>; the copy names the
    ///    same, possibly renamed, wave bank), else a copy of DSR's original of the other shipped project that defines it
    ///    (the stream project; bank by DS1's name, loose), else of frpg_main's (bank frpg_main loose, as the 1:1 characters
    ///    ship it), always into the memory project; else it stays pending (another area of the build may define it).
    /// A name any fevbnd project already defines is not added again. Returns the loose files written.
    /// </summary>
    static List<(string Device, string Path)> ImportEvents(Pack pack, BND4 fevbnd, string snd, List<string> shipped, string m, Events.MapSounds? plan, List<string> log)
    {
        var written = new List<(string, string)>();
        var defined = Commands.SoundSurvey.FevIndex(fevbnd.Files.Where(f => f.Name.EndsWith(".fev")).Select(f => (Path.GetFileNameWithoutExtension(f.Name), f.Bytes)));
        var dsr = shipped.ToDictionary(p => p, p => Commands.SoundSurvey.FevIndex([(p, File.ReadAllBytes(Path.Combine(snd, p + ".fev")))]).Keys.ToHashSet());
        var srcCache = new Dictionary<string, Formats.Fev1Project>();
        Formats.Fev1Project Dsr(string p) => srcCache.TryGetValue(p, out var f) ? f : srcCache[p] = Formats.Fev1Project.Read(File.ReadAllBytes(Path.Combine(snd, p + ".fev")));
        var mainNames = Events.MapSounds.GlobalIndex(Path.GetDirectoryName(snd)!);
        // (destination project, source: null = the destination itself, source event, new name, why)
        var jobs = new List<(string Dest, string? Src, string SrcEvent, string NewName, string Why)>();
        foreach (var i in plan?.Imports ?? [])
            if (!defined.ContainsKey(i.Ds2Event)) jobs.Add((m, i.Project, i.Ds1Event, i.Ds2Event, "SND6"));
        foreach (var i in ObjectSounds.Pending.ToList())
        {
            if (defined.ContainsKey(i.Ds2Event)) { ObjectSounds.Pending.Remove(i); continue; }
            string? own = shipped.FirstOrDefault(p => dsr[p].Contains(i.Ds1Event));
            // the area memory project itself: a copy within our copy (its wave bank may be renamed / preloaded per map); another
            // shipped project (the stream project, whose copy TalkVoices strips and renames): a copy of DSR's original into
            // the memory project (its bank keeps DS1's name and ships loose)
            if (own == m) jobs.Add((m, null, AppliedRenames.GetValueOrDefault((own, i.Ds1Event), i.Ds1Event), i.Ds2Event, $"SND7 {i.Object}"));
            else if (own != null) jobs.Add((m, own, i.Ds1Event, i.Ds2Event, $"SND7 {i.Object}"));
            else if (mainNames.TryGetValue(i.Ds1Event, out var gl) && gl.Contains("frpg_main")) jobs.Add((m, "frpg_main", i.Ds1Event, i.Ds2Event, $"SND7 {i.Object}"));
            else continue;
            ObjectSounds.Pending.Remove(i);
        }
        foreach (var g in jobs.GroupBy(j => (j.Dest, j.Src)))
        {
            var entry = fevbnd.Files.FirstOrDefault(f => f.Name == FevPath(g.Key.Dest));
            if (entry == null) { log.Add($"atmos: event copies into {g.Key.Dest}: project not in the fevbnd, {g.Count()} not added"); continue; }
            var dest = Formats.Fev1Project.Read(entry.Bytes);
            var src = g.Key.Src == null ? dest : Dsr(g.Key.Src);
            var add = new List<(string Source, string NewName)>();
            foreach (var j in g)
            {
                if (dest.EventByName(j.NewName) != null || add.Any(a => a.NewName == j.NewName)) continue;
                if (src.EventByName(j.SrcEvent) == null) { log.Add($"atmos: {j.Why}: {j.SrcEvent} not in {g.Key.Src ?? g.Key.Dest}, {j.NewName} not added"); continue; }
                add.Add((j.SrcEvent, j.NewName));
            }
            if (add.Count == 0) continue;
            entry.Bytes = dest.AddEvents(src, add);
            log.Add($"atmos: {string.Join("/", g.Select(j => j.Why.Split(' ')[0]).Distinct())} event copies into {g.Key.Dest} (from {g.Key.Src ?? "itself"}): {string.Join(", ", add.Select(a => $"{a.Source}→{a.NewName}"))}");
            if (g.Key.Src != null && pack.TryGetFile("title", $"sound/{g.Key.Src}.fsb") == null)
            {
                pack.AddFile("title", $"sound/{g.Key.Src}.fsb", File.ReadAllBytes(Path.Combine(snd, g.Key.Src + ".fsb")));
                written.Add(("title", $"sound/{g.Key.Src}.fsb"));
                log.Add($"atmos: loose title/sound/{g.Key.Src}.fsb (the copied events' wave bank, opened by name)");
            }
        }
        if (ObjectSounds.Pending.Count > 0) log.Add($"atmos: SND7 object sounds not defined by {string.Join("/", shipped)} or frpg_main (pending for another area): {string.Join(", ", ObjectSounds.Pending.Select(i => $"{i.Ds1Event}→{i.Ds2Event} ({i.Object})"))}");
        return written;
    }

    /// <summary>Renames FEV1 length-prefixed strings (u32 length incl. NUL + ASCII + NUL) that equal a key exactly; the
    /// project's own name (the first string, at 0x10 after the version and the two size fields) is never renamed. Event
    /// names of the same length keep every offset; a longer wave-bank name shifts the bytes after it (FEV1 has no
    /// absolute offsets after the header: <see cref="Talk.TalkVoices.Rename"/>, V in game s9-12).</summary>
    public static byte[] RenameLstrs(byte[] fev, IReadOnlyDictionary<string, string> renames, out List<(string Key, string Value, int Count)> done)
    {
        if (fev.Length < 0x14 || fev[0] != (byte)'F' || fev[1] != (byte)'E' || fev[2] != (byte)'V' || fev[3] != (byte)'1')
            throw new InvalidDataException("not an FMOD Designer FEV1 project");
        var counts = new Dictionary<string, int>();
        var o = new List<byte>(fev.Length + 256);
        int projectEnd = 0x14 + BitConverter.ToInt32(fev, 0x10);
        o.AddRange(fev.AsSpan(0, projectEnd).ToArray());
        int i = projectEnd;
        while (i < fev.Length)
        {
            if (i + 4 <= fev.Length)
            {
                int len = BitConverter.ToInt32(fev, i);
                if (len is >= 2 and <= 64 && i + 4 + len <= fev.Length && fev[i + 4 + len - 1] == 0)
                {
                    string s = System.Text.Encoding.ASCII.GetString(fev, i + 4, len - 1);
                    if (renames.TryGetValue(s, out var to) && s.All(ch => ch is >= ' ' and <= '~'))
                    {
                        var bytes = System.Text.Encoding.ASCII.GetBytes(to);
                        o.AddRange(BitConverter.GetBytes(bytes.Length + 1));
                        o.AddRange(bytes);
                        o.Add(0);
                        counts[s] = counts.GetValueOrDefault(s) + 1;
                        i += 4 + len;
                        continue;
                    }
                }
            }
            o.Add(fev[i++]);
        }
        done = counts.Select(c => (c.Key, renames[c.Key], c.Value)).ToList();
        return o.ToArray();
    }

    static bool ContainsLstr(byte[] fev, string s)
    {
        var b = System.Text.Encoding.ASCII.GetBytes(s);
        byte[] pat = [.. BitConverter.GetBytes(b.Length + 1), .. b, 0];
        return fev.AsSpan().IndexOf(pat) >= 0;
    }
}
