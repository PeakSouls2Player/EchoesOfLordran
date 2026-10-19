using System.Numerics;
using System.Xml.Linq;
using System.Xml.Serialization;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Rule B1b (session-8 skylight fix pass; user: "Also there's fog and such", the DSR reference shows low ground mist
/// drifting around Firelink's bonfire and steps): a DS1 map effect whose emitter is an AREA (box/square/circle/sphere)
/// is placed so the DS2 effect it maps to (rule B1, <see cref="Atmosphere.EffectMap"/>) covers the same footprint at the
/// same height, both read from the effect files themselves.
///
/// <para><b>Evidence</b> (<c>sky-survey ffx</c>, validation\ffx-*.xml): DS1's FXR1 (DSR) and DS2's DLsE share the
/// effect action numbering (DS2 3011 uses actions 29/35/55/79 with the parameter layout of DS1's FXR1 classes:
/// 29 ClusterEmitterSquare (size, degree, spread), 32 ClusterEmitterBox (X, Y, Z), 35 MoveEffect (offset X, Y, Z, rotation),
/// 55 acceleration). DS1's Firelink mist 100201 靄 (FRPG_SfxBnd_m10_02, 10120 bytes) = two box emitters 15 × 1 × 15 m and
/// 15 × 2 × 15 m raised 0.5 m (the emitters' MoveEffect OffsetY 0.5 under a root move of 0), 3–3.5 × 1.8–2 m animated billboards (texture 20035, 64 frames,
/// 6.7 s); DS2's 3011 【もや】 (sfx9999, the only mist DS2 places: m10_33 ×5) = a 10 m square emitter raised 1.5 m,
/// 3010 a 5 m square at 0.5 m, 3012 a 2 × 0.25 × 2 m box, 160 【通路に漂う煙】 a 5 × 3 × 8 m box. One 3011 per DS1 point
/// covered 44 % of DS1's 15 × 15 m and floated 1 m higher: the mist the user missed. Our SFX regions are otherwise
/// identical to vanilla 3011's (Point, UnkT04 0, Unk0E −1; m10_33) and the same mechanism shows the torches (s8-4).</para>
///
/// <para><b>Rule</b>: n = ⌈DS1 width / DS2 width⌉ × ⌈DS1 depth / DS2 depth⌉ copies of the DS2 effect on a grid centred on
/// DS1's point (cell = DS1 size / n, turned by the region's Y rotation), each lowered by (DS2 offset − DS1 offset) so the
/// emitters sit at DS1's height. DS1 point/cone emitters (torches 530) keep one copy at DS1's point. Status P.</para>
/// </summary>
public static class MapEffects
{
    /// <summary>An emitter footprint: width (x), depth (z), the effect's own height offset (MoveEffect OffsetY); Area =
    /// false for point/cone emitters.</summary>
    public sealed record Footprint(float Width, float Depth, float OffsetY, bool Area, string Note)
    {
        /// <summary>Session 11 (atmos11), rule B3: the particles follow the camera (a box emitter with EmitterMovementMode 3
        /// and the partial-follow action 105; DSR: exactly its three 火の粉 ember effects).</summary>
        public bool Follow { get; init; }
    }

    static readonly Dictionary<string, Footprint?> cache = new();

    /// <summary>DS1's effect: DSR sfx\FRPG_SfxBnd_mAA_BB, then _mAA, then _CommonEffects (FXR1).</summary>
    public static Footprint? Ds1(string dsrRoot, string ds1Map, int effectId)
    {
        string key = $"ds1 {ds1Map} {effectId}";
        if (cache.TryGetValue(key, out var hit)) return hit;
        Footprint? result = null;
        foreach (string b in new[] { $"FRPG_SfxBnd_{ds1Map[..6]}", $"FRPG_SfxBnd_{ds1Map[..3]}", "FRPG_SfxBnd_CommonEffects" })
        {
            string path = Path.Combine(dsrRoot, "sfx", b + ".ffxbnd.dcx");
            if (!File.Exists(path)) continue;
            var f = BND3.Read(DCX.Decompress(File.ReadAllBytes(path))).Files
                .FirstOrDefault(x => Path.GetFileNameWithoutExtension(x.Name).Equals($"f{effectId:D7}", StringComparison.OrdinalIgnoreCase));
            if (f == null) continue;
            var fxr = FXR1.Read(f.Bytes);
            var sw = new StringWriter();
            fxr.Flatten();
            new XmlSerializer(typeof(FXR1)).Serialize(sw, fxr);
            fxr.Unflatten();
            result = Ds1Footprint(XDocument.Parse(sw.ToString()), b);
            break;
        }
        return cache[key] = result;
    }

    static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    static float Num(XElement? e) =>
        e == null ? 0 : float.TryParse((string?)e.Attribute("Value") ?? e.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0;

    static Footprint Ds1Footprint(XDocument x, string binder)
    {
        float w = 0, d = 0;
        var kinds = new List<string>();
        foreach (var a in x.Descendants("ActionData"))
        {
            string t = (string?)a.Attribute(Xsi + "type") ?? "";
            switch (t)
            {
                case "ClusterEmitterBoxActionData32": w = Math.Max(w, Num(a.Element("EmitterSizeX"))); d = Math.Max(d, Num(a.Element("EmitterSizeZ"))); kinds.Add($"box {Num(a.Element("EmitterSizeX"))}×{Num(a.Element("EmitterSizeY"))}×{Num(a.Element("EmitterSizeZ"))}"); break;
                case "ClusterEmitterSquareActionData29": w = Math.Max(w, Num(a.Element("EmitterSize"))); d = Math.Max(d, Num(a.Element("EmitterSize"))); kinds.Add($"square {Num(a.Element("EmitterSize"))}"); break;
                case "ClusterEmitterCircleActionData30" or "ClusterEmitterSphereActionData31":
                    float r = Num(a.Element("EmitterRadius")); w = Math.Max(w, 2 * r); d = Math.Max(d, 2 * r); kinds.Add($"{(t.Contains("Circle") ? "circle" : "sphere")} r {r}"); break;
                case "ClusterEmitterConeActionData28": kinds.Add("cone"); break;
            }
        }
        // the effect's height: the largest OffsetY of its MoveEffects (DS1 100201: the root move 0, each emitter's 0.5)
        float off = x.Descendants("Modifier").Where(m => (string?)m.Attribute(Xsi + "type") == "FXModifier1_MoveEffect")
            .Select(m => Num(m.Element("OffsetY"))).DefaultIfEmpty(0).MaxBy(Math.Abs);
        // session 11 (atmos11), rule B3: camera-relative ambience
        bool follow = x.Descendants("ActionData").Any(a => (string?)a.Attribute(Xsi + "type") == "ClusterEmitterBoxActionData32" && (string?)a.Element("EmitterMovementMode") == "3")
            && x.Descendants("ActionData").Any(a => ((string?)a.Attribute(Xsi + "type") ?? "").EndsWith("ActionData105"));
        return new Footprint(w, d, off, w > 0 && d > 0, $"DS1 {binder}: {(kinds.Count > 0 ? string.Join(", ", kinds) : "no emitter")}, offset Y {off}{(follow ? ", follows the camera (box movement mode 3 + action 105)" : "")}") { Follow = follow };
    }

    /// <summary>DS2's effect from the global bank (title:/sfx/sfx9999.ffxbnd.dcx in the game folder, read-only; DLsE).</summary>
    public static Footprint? Ds2(string ds2Root, int effectId)
    {
        string key = $"ds2 {effectId}";
        if (cache.TryGetValue(key, out var hit)) return hit;
        string path = Path.Combine(ds2Root, "sfx", "sfx9999.ffxbnd.dcx");
        Footprint? result = null;
        if (File.Exists(path))
        {
            ds2Bank ??= BND4.Read(DCX.Decompress(File.ReadAllBytes(path)));
            var f = ds2Bank.Files.FirstOrDefault(x => Path.GetFileName(x.Name).Equals($"f{effectId:D7}.ffx", StringComparison.OrdinalIgnoreCase));
            if (f != null)
            {
                var sw = new StringWriter();
                FFXDLSE.Read(f.Bytes).XmlSerialize(sw);
                result = Ds2Footprint(XDocument.Parse(sw.ToString()));
            }
        }
        return cache[key] = result;
    }

    static BND4? ds2Bank;

    /// <summary>The values of a DLsE action's parameter list in order (Param7 Float, Param11 first TickFloat, Param1 Int).</summary>
    static List<float> Values(XElement action) =>
        action.Element("ParamList")?.Elements("Param").Select(p => (string?)p.Attribute(Xsi + "type") switch
        {
            "Param7" => float.Parse((string)p.Attribute("Float")!, System.Globalization.CultureInfo.InvariantCulture),
            "Param11" => p.Descendants("TickFloat").Select(t => float.Parse((string)t.Attribute("Float")!, System.Globalization.CultureInfo.InvariantCulture)).FirstOrDefault(),
            "Param1" => (float)int.Parse((string)p.Attribute("Int")!),
            _ => float.NaN,
        }).ToList() ?? [];

    static Footprint Ds2Footprint(XDocument x)
    {
        var actions = x.Descendants("Param").Where(p => (string?)p.Attribute(Xsi + "type") == "Param38").ToList();
        float w = 0, d = 0, off = 0;
        var kinds = new List<string>();
        foreach (var a in actions)
        {
            var v = Values(a);
            switch ((string?)a.Attribute("ActionID"))
            {
                // the first entry of each list is a Param44 argument reference (NaN here): the DS1 field layout follows
                case "29" when v.Count > 1: w = Math.Max(w, v[1]); d = Math.Max(d, v[1]); kinds.Add($"square {v[1]}"); break;
                case "32" when v.Count > 3: w = Math.Max(w, v[1]); d = Math.Max(d, v[3]); kinds.Add($"box {v[1]}×{v[2]}×{v[3]}"); break;
                case "30" or "31" when v.Count > 1: w = Math.Max(w, 2 * v[1]); d = Math.Max(d, 2 * v[1]); kinds.Add($"circle/sphere r {v[1]}"); break;
            }
        }
        // the largest Y offset of its MoveEffect actions (35: offset X, Y, Z, rotation…; 3011: 1.5 then 0), as for DS1
        off = actions.Where(a => (string?)a.Attribute("ActionID") == "35").Select(Values)
            .Where(v => v.Count > 1 && !float.IsNaN(v[1])).Select(v => v[1]).DefaultIfEmpty(0).MaxBy(Math.Abs);
        return new Footprint(w, d, off, w > 0 && d > 0, $"DS2 sfx9999: {(kinds.Count > 0 ? string.Join(", ", kinds) : "no area emitter")}, offset Y {off}");
    }

    // ------------------------------------------------------------------ session 9 (mist track): placement modes

    /// <summary>How DS1's area map effects (mists) are placed (build-pack --map-sfx MODE).
    /// <list type="bullet">
    /// <item>B1b: ⌈W1/W2⌉ × ⌈D1/D2⌉ copies of the DS2 effect (skylight fix pass; s9-1: no mist seen with 3011).</item>
    /// <item>B1c (default): as many copies as it takes for the DS2 effect's live particles to reach DS1's (rule B1c,
    /// <see cref="LiveDs1"/>/<see cref="LiveDs2"/>), on a grid over DS1's footprint at DS1's height.</item>
    /// <item>Dense (A/B): 5 × 5 copies at the DS2 effect's own height above DS1's point (i.e. 1 m higher for 3011).</item>
    /// <item>Ds1 (A/B, rule B2b): DS1's own effect converted to a DS2 effect (<see cref="FfxPorter"/>), one copy at DS1's
    /// point (it carries DS1's own emitters); falls back to B1c when the effect can't be converted.</item>
    /// <item>B1cUp (A/B, fix pass): B1c's copy count at the DS2 effect's own height above DS1's point (separates "too few
    /// copies" from "half below the floor" when Dense shows mist and B1c doesn't).</item>
    /// <item>B1d (default since the s9 A/B; <see cref="OverlapK"/>): the DS2 effect at its own height above DS1's point,
    /// ⌈k·W1/W2⌉ × ⌈k·D1/D2⌉ copies on DS1's grid (k = 3 overlapping DS2 emitters along each axis = the A/B pack ab1, the
    /// only placement seen in game), minus the copies whose emitter lies mostly outside DS1's footprint; capped at 25
    /// spread over the whole footprint, every spot of DS1's footprint under an emitter (else B1b).</item>
    /// </list></summary>
    public enum SfxMode { B1b, B1c, Dense, Ds1, B1cUp, B1d }

    public static SfxMode Mode = SfxMode.B1d;

    /// <summary>Rule B1d's per-axis overlap: how many DS2 emitters cover a point of DS1's footprint along each axis.
    /// In game (session 9, Firelink, DS1 100201 → DS2 3011, 15 × 15 m vs 10 × 10 m): k = 3 (ab1: 5 × 5 copies 3 m apart at
    /// 3011's own height, s9-8) shows clear soft grey wisps; B1c's k = 1.5 (3 × 3 copies 5 m apart, at DS1's height, s9-9)
    /// shows next to nothing. ab1b (B1c's count at 3011's own height) was not run, so k = 3 is the lowest overlap seen
    /// working.</summary>
    public const int OverlapK = 3;

    /// <summary>Rule B1d drops a copy whose emitter puts less than this share of its area inside DS1's footprint (the
    /// spill ab1 had in its four corner copies: 42 % inside).</summary>
    public const float MinInsideShare = 0.5f;

    /// <summary>--map-sfx ds2=ID (A/B): DS1 area effects that map to DS2 3011 use this DS2 effect instead (e.g. 3010).</summary>
    public static int MistOverride;

    /// <summary>Rule B1c's upper bound on DS2 copies per DS1 point (fix pass): a dense DS1 area effect mapped to a sparse
    /// DS2 effect would otherwise give dozens of 40–400 m instances per point in the mass migration. Logged when it applies.</summary>
    public const int MaxCopiesPerPoint = 25;

    /// <summary>--sfx-control ID (A/B positive control, fix pass): one extra SFX region of this DS2 effect 1 m above each
    /// DS1 area-effect point (3020 = the torch flame that burns in our m18_01), to show that map SFX regions spawn in that
    /// map at all. 0 = none (default; never in the default pack).</summary>
    public static int ControlEffect;

    /// <summary>Parses --map-sfx: b1d (default) | b1b | b1c | b1c-up | dense | ds1 | ds2=ID (ds2=ID: rule B1c with that DS2 effect).</summary>
    public static bool ParseMode(string v)
    {
        switch (v.ToLowerInvariant())
        {
            case "b1d": Mode = SfxMode.B1d; return true;
            case "b1b": Mode = SfxMode.B1b; return true;
            case "b1c": Mode = SfxMode.B1c; return true;
            case "b1c-up": Mode = SfxMode.B1cUp; return true;
            case "dense": Mode = SfxMode.Dense; return true;
            case "ds1": Mode = SfxMode.Ds1; return true;
        }
        if (v.StartsWith("ds2=", StringComparison.OrdinalIgnoreCase) && int.TryParse(v[4..], out int id)) { Mode = SfxMode.B1c; MistOverride = id; return true; }
        return false;
    }

    /// <summary>Copies of the DS2 effect for a DS1 area effect in the current <see cref="Mode"/> (B1b: <see cref="Placements"/>).</summary>
    public static List<Vector3> PlacementsFor(Vector3 pos, float rotY, Footprint? ds1, Footprint? ds2, float live1, float live2, out string why)
    {
        if (Mode == SfxMode.B1b || ds1 is not { Area: true } || ds2 is not { Area: true })
            return Placements(pos, rotY, ds1, ds2, out why);
        // rule B2b (Mode Ds1) falls back to the default interim rule when DS1's effect can't be converted
        if (Mode is SfxMode.B1d or SfxMode.Ds1) return PlacementsB1d(pos, rotY, ds1, ds2, live1, live2, out why);
        int nx, nz;
        float dy;
        string rule;
        if (Mode == SfxMode.Dense)
        {
            nx = nz = 5;
            dy = 0;
            rule = "A/B dense: 5×5 copies at the DS2 effect's own height above DS1's point";
        }
        else
        {
            // rule B1c: n ≥ live(DS1) / live(DS2), a grid shaped like DS1's footprint, never fewer than B1b's tiling;
            // the particle-parity part is capped at MaxCopiesPerPoint (B1b's own tiling is never cut)
            int nb = Math.Max(1, (int)Math.Ceiling(ds1.Width / ds2.Width - 0.01)) * Math.Max(1, (int)Math.Ceiling(ds1.Depth / ds2.Depth - 0.01));
            int parity = live1 > 0 && live2 > 0 ? (int)Math.Ceiling(live1 / live2 - 0.01) : 0;
            bool capped = parity > MaxCopiesPerPoint;
            int n = Math.Max(nb, Math.Min(parity, MaxCopiesPerPoint));
            float aspect = ds1.Depth > 0 ? ds1.Width / ds1.Depth : 1;
            nx = Math.Max(1, (int)Math.Ceiling(MathF.Sqrt(n * aspect) - 0.01));
            nz = Math.Max(1, (int)Math.Ceiling(n / (float)nx - 0.01));
            dy = Mode == SfxMode.B1cUp ? 0 : ds1.OffsetY - ds2.OffsetY;
            rule = $"rule B1c{(Mode == SfxMode.B1cUp ? " (A/B b1c-up: at the DS2 effect's own height)" : "")}: live particles DS1 {live1:0.#} / DS2 {live2:0.#} per copy → ≥ {n} copies"
                 + (capped ? $" (CAPPED: parity asks for {parity}, at most {MaxCopiesPerPoint} per DS1 point)" : "");
        }
        float cx = ds1.Width / nx, cz = ds1.Depth / nz;
        var rot = Matrix4x4.CreateRotationY(rotY * MathF.PI / 180);
        var list = new List<Vector3>();
        for (int i = 0; i < nx; i++)
            for (int k = 0; k < nz; k++)
                list.Add(pos + Vector3.Transform(new Vector3((i - (nx - 1) / 2f) * cx, 0, (k - (nz - 1) / 2f) * cz), rot) + new Vector3(0, dy, 0));
        why = $"{rule}: {nx}×{nz}, {cx:0.##} × {cz:0.##} m apart, height {dy:+0.##;-0.##;0} m ({ds1.Note}; {ds2.Note})";
        return list;
    }

    /// <summary>Rule B1d (interim default while rule B2b is unproven in game): copies of the DS2 area effect at its OWN
    /// height above DS1's point, n = ⌈k·W1/W2⌉ × ⌈k·D1/D2⌉ (k = <see cref="OverlapK"/>) on DS1's grid (cell W1/n, turned by
    /// DS1's Y rotation), dropping copies whose emitter has less than <see cref="MinInsideShare"/> of its area inside DS1's
    /// footprint (share per axis = overlap / min(W1, W2)). When more than <see cref="MaxCopiesPerPoint"/> copies remain
    /// (fix pass 3): the full grid thinned to at most that many, spread over the whole footprint (never fewer per axis than
    /// B1b). Every spot of DS1's footprint must stay under a DS2 emitter, else rule B1b. Firelink (DS1 100201 15 × 15 m →
    /// DS2 3011 10 × 10 m): 5 × 5 − 4 corners = 21 copies per DS1 point, 3 m apart, at the point's height (3011's emitter
    /// 1.5 m up, billboards from the floor to ≈ 3.5 m).
    /// <para>Why (session-9 A/B, Firelink bonfire): ab1 = 5 × 5 copies at 3011's own height → clear soft grey wisps (s9-8);
    /// B1c = 3 × 3 copies at DS1's emitter height → next to nothing (s9-9). B1d = ab1 minus the 4 corner copies that put
    /// most of their emitter outside DS1's footprint (42 % inside): the visible configuration with less spill. It is NOT a
    /// density derived from DS1's data: k = 3 and the 50 % threshold come from the one configuration seen working; s9-9
    /// changed count and height together and ab1b (B1c's count at 3011's height) was not run, so whether fewer copies at
    /// 3011's height suffice is open, and how far B1d overshoots DSR's look is not quantified. Own height: 3011's 3–4 m
    /// billboards centred 1.5 m up start at the floor; at DS1's 0.5 m their lower 40 % is under the floor (F6).</para></summary>
    static List<Vector3> PlacementsB1d(Vector3 pos, float rotY, Footprint ds1, Footprint ds2, float live1, float live2, out string why)
    {
        int nx = Math.Max(1, (int)Math.Ceiling(OverlapK * ds1.Width / ds2.Width - 0.01));
        int nz = Math.Max(1, (int)Math.Ceiling(OverlapK * ds1.Depth / ds2.Depth - 0.01));
        float cx = ds1.Width / nx, cz = ds1.Depth / nz;
        // share of a copy's emitter (DS2 width w2 centred at c) inside DS1's [−W1/2, W1/2], relative to the smaller width
        static float Share(float c, float w1, float w2) =>
            Math.Max(0, Math.Min(c + w2 / 2, w1 / 2) - Math.Max(c - w2 / 2, -w1 / 2)) / Math.Min(w1, w2);
        var cells = new List<(float X, float Z, float Share)>();
        for (int i = 0; i < nx; i++)
            for (int k = 0; k < nz; k++)
            {
                float x = (i - (nx - 1) / 2f) * cx, z = (k - (nz - 1) / 2f) * cz;
                cells.Add((x, z, Share(x, ds1.Width, ds2.Width) * Share(z, ds1.Depth, ds2.Depth)));
            }
        int grid = cells.Count;
        var keep = cells.Where(c => c.Share >= MinInsideShare - 1e-4f).ToList();
        int bx = Math.Max(1, (int)Math.Ceiling(ds1.Width / ds2.Width - 0.01)), bz = Math.Max(1, (int)Math.Ceiling(ds1.Depth / ds2.Depth - 0.01));
        bool capped = keep.Count > MaxCopiesPerPoint;
        if (capped)
        {
            // fix pass 3 (review: the old cap kept the 25 cells nearest the centre and left up to 63 % of a large DS1
            // footprint without an emitter): at most MaxCopiesPerPoint copies spread over the WHOLE footprint — the full
            // grid, thinned along the denser axis first, never below B1b's per-axis count (cell ≤ the DS2 emitter, so every
            // spot stays covered); no corner dropping on the coarse grid. B1b's own tiling is never cut.
            int mx = nx, mz = nz;
            while (mx * mz > MaxCopiesPerPoint && (mx > bx || mz > bz))
                if (mx > bx && (mz <= bz || ds1.Width / mx <= ds1.Depth / mz)) mx--; else mz--;
            if (mx * mz > MaxCopiesPerPoint) return Placements(pos, rotY, ds1, ds2, out why);
            nx = mx; nz = mz; cx = ds1.Width / nx; cz = ds1.Depth / nz;
            keep = [];
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    float x = (i - (nx - 1) / 2f) * cx, z = (k - (nz - 1) / 2f) * cz;
                    keep.Add((x, z, Share(x, ds1.Width, ds2.Width) * Share(z, ds1.Depth, ds2.Depth)));
                }
        }
        // coverage guard (fix pass 3; replaces "never fewer copies than B1b"): every spot of DS1's footprint must lie under
        // at least one kept DS2 emitter, else rule B1b (which tiles the footprint completely)
        if (Uncovered(keep.Select(c => (c.X, c.Z)).ToList(), ds1, ds2) > 0) return Placements(pos, rotY, ds1, ds2, out why);
        // the density B1d gives inside DS1's footprint (copy-equivalents = Σ share of each copy's area inside, × DS2's live count)
        float AreaInside(float c, float w1, float w2) => Math.Max(0, Math.Min(c + w2 / 2, w1 / 2) - Math.Max(c - w2 / 2, -w1 / 2)) / w2;
        float eq = keep.Sum(c => AreaInside(c.X, ds1.Width, ds2.Width) * AreaInside(c.Z, ds1.Depth, ds2.Depth));
        float area1 = ds1.Width * ds1.Depth;
        var rot = Matrix4x4.CreateRotationY(rotY * MathF.PI / 180);
        var list = keep.Select(c => pos + Vector3.Transform(new Vector3(c.X, 0, c.Z), rot)).ToList();
        why = (capped
                ? $"rule B1d: k = {OverlapK} DS2 emitters per axis would keep {grid} cells' worth > {MaxCopiesPerPoint} → CAPPED: {nx}×{nz} grid over the whole footprint, {cx:0.##} × {cz:0.##} m apart → {list.Count} copies"
                : $"rule B1d: k = {OverlapK} DS2 emitters per axis → {nx}×{nz} grid, {cx:0.##} × {cz:0.##} m apart, {grid - keep.Count} cop{(grid - keep.Count == 1 ? "y" : "ies")} < {MinInsideShare:0%} inside DS1's footprint dropped → {list.Count} copies")
            + $", at the DS2 effect's own height (DS1's point); inside DS1's {ds1.Width:0.##} × {ds1.Depth:0.##} m: {eq * live2:0.#} live DS2 particles"
            + (live1 > 0 ? $" = {eq * live2 / live1:0.##}× DS1's {live1:0.#}" : "") + $" ({eq * live2 / area1:0.###}/m²) ({ds1.Note}; {ds2.Note})";
        return list;
    }

    /// <summary>Share of DS1's footprint (centred at 0, unrotated) that lies under none of the DS2 emitters centred at
    /// <paramref name="spots"/> (x, z), sampled on a grid of at most 0.25 m (fix pass 3: B1d's coverage guard).</summary>
    public static double Uncovered(List<(float X, float Z)> spots, Footprint ds1, Footprint ds2)
    {
        float h1x = ds1.Width / 2, h1z = ds1.Depth / 2, h2x = ds2.Width / 2 + 1e-3f, h2z = ds2.Depth / 2 + 1e-3f;
        int sx = Math.Max(4, (int)MathF.Ceiling(ds1.Width / 0.25f)), sz = Math.Max(4, (int)MathF.Ceiling(ds1.Depth / 0.25f));
        int none = 0;
        for (int i = 0; i < sx; i++)
            for (int k = 0; k < sz; k++)
            {
                float x = -h1x + (i + 0.5f) * ds1.Width / sx, z = -h1z + (k + 0.5f) * ds1.Depth / sz;
                if (!spots.Any(s => Math.Abs(x - s.X) <= h2x && Math.Abs(z - s.Z) <= h2z)) none++;
            }
        return (double)none / (sx * sz);
    }

    /// <summary>DS1's live particle count (rule B1c): per particle class 2023 (DS1/DS2 layout: p6 count per emission,
    /// p7 emission limit (−1 none), p8 interval, p10 maximum alive, p11 appearance; action 71's lifetime = Duration ×
    /// (DurationMult + LifetimeVariance/2)): min(max, count × min(limit, life / interval)). DS1 100201: 26.7 + 12.</summary>
    public static float LiveDs1(string dsrRoot, string ds1Map, int effectId)
    {
        string key = $"{ds1Map[..6]} {effectId}";
        if (!liveCache.TryGetValue(key, out float live)) liveCache[key] = live = LiveDs1Uncached(dsrRoot, ds1Map, effectId);
        return live;
    }

    static readonly Dictionary<string, float> liveCache = new();

    static float LiveDs1Uncached(string dsrRoot, string ds1Map, int effectId)
    {
        foreach (string b in new[] { $"FRPG_SfxBnd_{ds1Map[..6]}", $"FRPG_SfxBnd_{ds1Map[..3]}", "FRPG_SfxBnd_CommonEffects" })
        {
            string path = Path.Combine(dsrRoot, "sfx", b + ".ffxbnd.dcx");
            if (!File.Exists(path)) continue;
            var f = BND3.Read(DCX.Decompress(File.ReadAllBytes(path))).Files
                .FirstOrDefault(x => Path.GetFileNameWithoutExtension(x.Name).Equals($"f{effectId:D7}", StringComparison.OrdinalIgnoreCase));
            if (f == null) continue;
            float sum = 0;
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            void Walk(FXR1.FXNode n)
            {
                if (!seen.Add(n)) return;
                switch (n)
                {
                    case FXR1.FXNode.EffectCreateNode133 c: foreach (var q in c.Container1.FXNodes.Concat(c.Container2.FXNodes)) Walk(q); break;
                    case FXR1.FXNode.ActionCallNode a when a.Container != null: foreach (var q in a.Container.FXNodes) Walk(q); break;
                    case FXR1.FXNode.EffectCallNode c when c.Container != null:
                        var p = c.Container.FXNodes;
                        if (c.EffectID == 2023 && p.Count == 18 && p[6] is FXR1.FXNode.IntSequenceNode5 cnt && p[7] is FXR1.FXNode.IntNode lim
                            && p[8] is FXR1.FXNode.TickNode iv && p[10] is FXR1.FXNode.IntNode max
                            && p[11] is FXR1.FXNode.ActionCallNode { Container.ActionData: FXR1.FXActionData.ClusterAppearanceBillboardActionData71 d })
                        {
                            float life = d.Duration * (d.DurationMult + d.LifetimeVariance / 2);
                            float per = cnt.Ticks.FirstOrDefault()?.Value ?? 1;
                            float emissions = Math.Min(lim.Value >= 0 ? lim.Value : float.MaxValue, iv.Tick > 0 ? life / iv.Tick : float.MaxValue);
                            sum += Math.Min(max.Value, per * emissions);
                        }
                        foreach (var q in p) Walk(q);
                        break;
                }
            }
            Walk(FXR1.Read(f.Bytes).RootFXNode);
            return sum;
        }
        return 0;
    }

    /// <summary>DS2's live particle count per copy (rule B1c): class 2032 (p9 interval t2, p10 count, p14 maximum, p0
    /// appearance 15001/70: lifetime t2 at p4; 15000/71: at p3) and 2023 (DS1's layout). DS2 3011: min(6, 3 s / 0.5 s) = 6.</summary>
    public static float LiveDs2(int effectId)
    {
        string path = Path.Combine(Program.DefaultDs2Root, "sfx", "sfx9999.ffxbnd.dcx");
        if (!File.Exists(path)) return 0;
        ds2Bank ??= BND4.Read(DCX.Decompress(File.ReadAllBytes(path)));
        var f = ds2Bank.Files.FirstOrDefault(x => Path.GetFileName(x.Name).Equals($"f{effectId:D7}.ffx", StringComparison.OrdinalIgnoreCase));
        if (f == null) return 0;
        float Mean(FFXDLSE.Param p) => p switch { FFXDLSE.Param85 t => (t.Tick1 + t.Tick2) / 2, FFXDLSE.Param70 t => t.Tick, _ => float.NaN };
        float Life(FFXDLSE.Param p) => p is FFXDLSE.Param38 a
            ? a.ActionID switch { 15001 or 70 => Mean(a.ParamList.Params[4]), 15000 or 71 => Mean(a.ParamList.Params[3]), _ => float.NaN }
            : float.NaN;
        float sum = 0;
        void Walk(FFXDLSE.Param p)
        {
            switch (p)
            {
                case FFXDLSE.Param37 c:
                    var q = c.ParamList.Params;
                    float life = float.NaN, iv = float.NaN, per = 1, max = 0, lim = -1;
                    if (c.EffectID == 2032 && q.Count == 18)
                    { life = Life(q[0]); iv = Mean(q[9]); per = q[10] is FFXDLSE.Param5 s && s.TickInts.Count > 0 ? s.TickInts[0].Int : 1; max = q[14] is FFXDLSE.Param1 m ? m.Int : 0; lim = q[7] is FFXDLSE.Param79 l ? l.Int1 : -1; }
                    else if (c.EffectID == 2023 && q.Count == 18)
                    { life = Life(q[11]); iv = Mean(q[8]); per = q[6] is FFXDLSE.Param5 s && s.TickInts.Count > 0 ? s.TickInts[0].Int : 1; max = q[10] is FFXDLSE.Param1 m ? m.Int : 0; lim = q[7] is FFXDLSE.Param1 l ? l.Int : -1; }
                    if (max > 0 && !float.IsNaN(life) && !float.IsNaN(iv))
                        sum += Math.Min(max, per * Math.Min(lim >= 0 ? lim : float.MaxValue, iv > 0 ? life / iv : float.MaxValue));
                    foreach (var x in q) Walk(x);
                    break;
                case FFXDLSE.Param38 a: foreach (var x in a.ParamList.Params) Walk(x); break;
            }
        }
        foreach (var st in FFXDLSE.Read(f.Bytes).Effect.StateMap.States)
            foreach (var a in st.Actions)
                foreach (var p in a.ParamList.Params) Walk(p);
        return sum;
    }

    /// <summary>Rule B1b: where the DS2 copies go (world positions) for a DS1 effect at <paramref name="pos"/> turned by
    /// <paramref name="rotY"/> degrees.</summary>
    public static List<Vector3> Placements(Vector3 pos, float rotY, Footprint? ds1, Footprint? ds2, out string why)
    {
        if (ds1 is not { Area: true } || ds2 is not { Area: true })
        {
            why = $"one copy at DS1's point ({ds1?.Note ?? "DS1 effect not found"}; {ds2?.Note ?? "DS2 effect not found"})";
            return [pos];
        }
        int nx = Math.Max(1, (int)Math.Ceiling(ds1.Width / ds2.Width - 0.01)), nz = Math.Max(1, (int)Math.Ceiling(ds1.Depth / ds2.Depth - 0.01));
        float cx = ds1.Width / nx, cz = ds1.Depth / nz, dy = ds1.OffsetY - ds2.OffsetY;
        var rot = Matrix4x4.CreateRotationY(rotY * MathF.PI / 180);
        var list = new List<Vector3>();
        for (int i = 0; i < nx; i++)
            for (int k = 0; k < nz; k++)
                list.Add(pos + Vector3.Transform(new Vector3((i - (nx - 1) / 2f) * cx, 0, (k - (nz - 1) / 2f) * cz), rot) + new Vector3(0, dy, 0));
        why = $"{nx}×{nz} copies, {cx:0.##} × {cz:0.##} m apart, height {dy:+0.##;-0.##;0} m ({ds1.Note}; {ds2.Note})";
        return list;
    }
}
