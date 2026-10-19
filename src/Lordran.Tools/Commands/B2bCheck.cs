using System.Text;
using SoulsFormats;
using Lordran.Tools.Build.Maps;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 9 (mist track, s9-10 crash fix): offline checks that reproduce what DS2's effect runtime validates when it
/// loads title:/sfx banks and spawns a map SFX region (decomp DarkSoulsII.c; addresses at the preferred base 0x140000000).
/// <list type="bullet">
/// <item><c>mist-survey ids</c>: DS2's title-bank effect ids, the free id families, DS1's effect census and the DS2 id
/// rule B2b-id gives each DS1 map-SFX effect.</item>
/// <item><c>mist-survey b2b-check PACK</c>: every runtime rule below against a pack (exit 2 on any failure).</item>
/// </list>
/// Runtime rules (R1–R10):
/// R1 lazy-load bitset: FUN_140bf7960 marks a loaded effect in std::bitset&lt;30000&gt; → a title-bank effect id ≥ 30000
///    throws out_of_range → terminate → abort (run s9-10). R2 entry name: the same function looks the effect up by the
///    name "N:\FRPG2[_64]\data\Sfx\Output\Effect\f%07d.ffx" (FRPG2_64 from 10000 up). R3 the effect's own DLsE id = the
///    name's. R4 classes: the bank loader parses only 2000–2200 eagerly (2277581–2277585), the effect's class calls must be
///    DS2 classes in that range. R5 sfxParameter: FUN_140bf9dc0 binary-searches the tables (sorted, 64-byte records,
///    header 32) — a region effect without a record is never spawned (FUN_1403c8240). R6 id variants: FUN_140beb670
///    (manager +0x305) turns ids ≤ 9999 into id + 20000 / id + 10000 → an added id's family must be unknown to DS2.
///    R7 textures: the res-bank loader takes a .tpf entry only when the number in its name is below 900020
///    (2277667–2277676); every texture the effect lists must be there (res_Append and lqres_Append). R8 parameter
///    signatures (the compiles' "!!invalid parameter size!!" family): each action/class call uses a DS2 signature
///    (<see cref="MistSurvey"/> CheckEffect). R9 vanilla entries byte-identical. R10 every SFX region of the pack's MSBs
///    names an effect that exists (title banks) with a record, below R1's limit.
/// Fix pass 3: "R1/R2 exe constants" fails unless the limit and entry-name formats read from the exe equal the ones the
/// converter uses; R2 compares with the exe's format filled in; R6 also fails outside rule B2b-id's block (20001..29999,
/// family b ≥ 1); R12 compiles every converted action 71 as DS2 does (FUN_14100c880, <see cref="Compile71"/>) and compares
/// the struct's scalar slots with DSR's own ActionData71 of the DS1 source (only the converter's documented substitutions
/// may differ). <c>mist-survey compile71</c> prints the evidence that FUN_14100c880's struct is DSR's ActionData71.
/// </summary>
public static class B2bCheck
{
    static string Exe => Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\..\..\..\bin\ds2s\DarkSoulsII.unpacked.exe");

    /// <summary>What <see cref="ExeFacts"/> read: the bitset limit compared at 0x140bf7cd8 and the two entry-name formats
    /// (null: the exe or the expected instruction isn't there — R1/R2 then fail, the constants can't be confirmed).</summary>
    sealed record Facts(uint? Limit, string? FmtLow, string? FmtHigh);

    /// <summary>The runtime's own constants, read from the unpacked exe (evidence, not assumptions); R1/R2 compare them with
    /// the constants the converter uses (<see cref="FfxPorter.LazyIdLimit"/>, <see cref="NameFor"/>).</summary>
    static Facts ExeFacts(List<string> lines)
    {
        string exe = Path.GetFullPath(Exe);
        if (!File.Exists(exe)) exe = Path.Combine(@"C:\EchoesOfLordran\bin\ds2s", "DarkSoulsII.unpacked.exe");
        if (!File.Exists(exe)) { lines.Add("exe facts: unpacked exe not found"); return new(null, null, null); }
        var d = File.ReadAllBytes(exe);
        int pe = BitConverter.ToInt32(d, 0x3c), nsec = BitConverter.ToUInt16(d, pe + 6), opt = BitConverter.ToUInt16(d, pe + 20);
        long imageBase = BitConverter.ToInt64(d, pe + 24 + 24);
        int Off(long va)
        {
            long rva = va - imageBase;
            for (int i = 0; i < nsec; i++)
            {
                int o = pe + 24 + opt + i * 40;
                uint vsize = BitConverter.ToUInt32(d, o + 8), sva = BitConverter.ToUInt32(d, o + 12), rsize = BitConverter.ToUInt32(d, o + 16), raw = BitConverter.ToUInt32(d, o + 20);
                if (rva >= sva && rva < sva + rsize) return (int)(raw + rva - sva);
            }
            throw new InvalidDataException($"{va:x} not in the file");
        }
        string W(long va) { int o = Off(va); int e = o; while (d[e] != 0 || d[e + 1] != 0) e += 2; return Encoding.Unicode.GetString(d, o, e - o); }
        string A(long va) { int o = Off(va); int e = o; while (d[e] != 0) e++; return Encoding.ASCII.GetString(d, o, e - o); }
        int c = Off(0x140bf7cd8);
        string cmp = BitConverter.ToString(d, c, 7);
        // 48 81 F9 imm32 = cmp rcx, imm32
        bool isCmp = d[c] == 0x48 && d[c + 1] == 0x81 && d[c + 2] == 0xF9;
        uint lim = BitConverter.ToUInt32(d, c + 3);
        string fl = W(0x1411ed600), fh = W(0x1411ed660);
        lines.Add($"exe facts ({Path.GetFileName(exe)}): 0x140bf7cd8 {cmp} = {(isCmp ? $"cmp rcx, 0x{lim:x} ({lim})" : "NOT the expected cmp rcx, imm32")} → bitset limit; throw message '{A(0x1410b1010)}'");
        lines.Add($"  entry-name formats: id < 10000 '{fl}', id ≥ 10000 '{fh}'");
        return new(isCmp ? lim : null, fl, fh);
    }

    static string NameFor(int id) => id < 10000 ? $@"N:\FRPG2\data\Sfx\Output\Effect\f{id:D7}.ffx" : $@"N:\FRPG2_64\data\Sfx\Output\Effect\f{id:D7}.ffx";

    /// <summary>The exe's format for <paramref name="id"/>'s range, filled in as the runtime's swprintf would ("%07d").</summary>
    static string? ExeName(Facts f, int id) => (id < 10000 ? f.FmtLow : f.FmtHigh)?.Replace("%07d", id.ToString("D7"));

    static int? NameId(string name)
    {
        string n = Path.GetFileName(name);
        return n.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase) && n.Length == 12 && int.TryParse(n[1..8], out int id) ? id : null;
    }

    // ------------------------------------------------------------------ ids

    public static int Ids()
    {
        string dsr = Program.DefaultDsrRoot;
        var used = FfxPorter.Ds2UsedIds();
        var fam = FfxPorter.FreeFamilies();
        Console.WriteLine($"DS2 title banks know {used.Count} effect ids (entry names + sfxParameter records): {used.Count(i => i < 10000)} below 10000, {used.Count(i => i is >= 10000 and < 20000)} in 10000–19999, {used.Count(i => i is >= 20000 and < 30000)} in 20000–29999, {used.Count(i => i >= 30000)} ≥ 30000 (records only: {string.Join(" ", used.Where(i => i >= 30000).Take(12))}{(used.Count(i => i >= 30000) > 12 ? " …" : "")})");
        var ffxIds = new SortedSet<int>();
        foreach (var bank in new[] { "sfx9999.ffxbnd.dcx", "sfx9999_Append.ffxbnd.dcx" })
            foreach (var f in MistSurvey.ReadBank(Path.Combine(MistSurvey.SfxDir(), bank)).Files)
                if (NameId(f.Name) is int id) ffxIds.Add(id);
        Console.WriteLine($"  .ffx entries: {ffxIds.Count} distinct ids {ffxIds.Min}..{ffxIds.Max}; ≥ 30000: {ffxIds.Count(i => i >= 30000)} (every vanilla title-bank effect obeys R1)");
        var runs = new List<(int A, int B)>();
        foreach (int b in fam) { if (runs.Count > 0 && runs[^1].B == b - 1) runs[^1] = (runs[^1].A, b); else runs.Add((b, b)); }
        Console.WriteLine($"free families b (b, 10000+b, 20000+b unknown to DS2): {fam.Count} of 10000 in {runs.Count} runs; longest {string.Join(", ", runs.OrderByDescending(r => r.B - r.A).Take(5).Select(r => $"{r.A}–{r.B}"))}; first {fam.FirstOrDefault()} → DS2 id {FfxPorter.IdBlock + fam.FirstOrDefault()}");
        var census = FfxPorter.Ds1Census(dsr);
        Console.WriteLine($"DS1 census: {census.Count} distinct effect ids over {Directory.GetFiles(Path.Combine(dsr, "sfx"), "FRPG_SfxBnd_*.ffxbnd.dcx").Length} DSR binders ({census.Min()}..{census.Max()}); fits: {census.Count <= fam.Count}; the last DS1 effect → DS2 {FfxPorter.IdBlock + fam[census.Count - 1]}");
        Console.WriteLine($"DS1 100201 (Firelink mist 靄): census #{census.BinarySearch(100201)} → DS2 {FfxPorter.Ds2IdFor(dsr, 100201)}");
        // the DS1 map-SFX effects (MSB1 SFX events) and their DS2 ids
        var msbIds = new SortedDictionary<int, SortedSet<string>>();
        foreach (var msbPath in Directory.GetFiles(Path.Combine(dsr, "map", "MapStudio"), "m1*.msb").Order())
        {
            var msb = MSB1.Read(msbPath);
            foreach (var e in msb.Events.SFX)
            {
                if (!msbIds.TryGetValue(e.EffectID, out var maps)) msbIds[e.EffectID] = maps = new();
                maps.Add(Path.GetFileNameWithoutExtension(msbPath)[..6]);
            }
        }
        Console.WriteLine($"DS1 MSB SFX events reference {msbIds.Count} distinct effects; rule B2b-id:");
        foreach (var (id, maps) in msbIds)
        {
            string ds2;
            try { ds2 = FfxPorter.Ds2IdFor(dsr, id).ToString(); } catch (Exception e) { ds2 = $"— ({e.Message})"; }
            Console.WriteLine($"  DS1 {id,7} → DS2 {ds2}  ({string.Join(" ", maps)})");
        }
        return 0;
    }

    // ------------------------------------------------------------------ density (rule B1d's derivation)

    /// <summary>density [DS1MAP DS1ID DS2ID]: for each placement mode, the copies per DS1 point and, sampled on a 0.25 m
    /// grid, how many DS2 emitters cover each spot of DS1's footprint (min / mean / at the centre / at the edge), the live
    /// DS2 particles inside DS1's footprint (vs DS1's own), the ground the DS2 emitters cover outside it, and the emitter
    /// height above DS1's point.</summary>
    public static int Density(string ds1Map, int ds1Id, int ds2Id)
    {
        string dsr = Program.DefaultDsrRoot;
        var fp1 = MapEffects.Ds1(dsr, ds1Map, ds1Id)!;
        var fp2 = MapEffects.Ds2(Program.DefaultDs2Root, ds2Id)!;
        float live1 = MapEffects.LiveDs1(dsr, ds1Map, ds1Id), live2 = MapEffects.LiveDs2(ds2Id);
        Console.WriteLine($"DS1 {ds1Id} ({fp1.Note}), live {live1:0.#}; DS2 {ds2Id} ({fp2.Note}), live {live2:0.#} per copy; DS1 density {live1 / (fp1.Width * fp1.Depth):0.###}/m²");
        var saved = MapEffects.Mode;
        foreach (var (mode, label) in new[] { (MapEffects.SfxMode.B1b, "B1b (s9-1, not seen)"), (MapEffects.SfxMode.B1c, "B1c = old default `pack` (s9-9, next to nothing)"), (MapEffects.SfxMode.B1cUp, "B1c-up = ab1b (not run)"), (MapEffects.SfxMode.Dense, "dense = ab1 (s9-8, clear soft grey wisps)"), (MapEffects.SfxMode.B1d, "B1d = interim-pack (new default)") })
        {
            MapEffects.Mode = mode;
            var spots = MapEffects.PlacementsFor(System.Numerics.Vector3.Zero, 0, fp1, fp2, live1, live2, out _);
            float h1x = fp1.Width / 2, h1z = fp1.Depth / 2, h2x = fp2.Width / 2, h2z = fp2.Depth / 2;
            const float step = 0.25f;
            int Cover(float x, float z) => spots.Count(s => Math.Abs(x - s.X) <= h2x && Math.Abs(z - s.Z) <= h2z);
            var inside = new List<int>();
            int spillCells = 0, edge = 0, edgeN = 0;
            float ext = h1x + h2x + 10;
            for (float x = -ext + step / 2; x < ext; x += step)
                for (float z = -ext + step / 2; z < ext; z += step)
                {
                    int c = Cover(x, z);
                    bool inFp = Math.Abs(x) <= h1x && Math.Abs(z) <= h1z;
                    if (inFp) { inside.Add(c); if (Math.Abs(x) > h1x - 0.5f || Math.Abs(z) > h1z - 0.5f) { edge += c; edgeN++; } }
                    else if (c > 0) spillCells++;
                }
            double mean = inside.Average();
            double particles = mean * fp1.Width * fp1.Depth * live2 / (fp2.Width * fp2.Depth);
            float dy = spots.Count > 0 ? spots[0].Y : 0;
            Console.WriteLine($"{label}: {spots.Count} copies per DS1 point; emitters covering a spot of DS1's footprint: min {inside.Min()}, mean {mean:0.##}, centre {Cover(0.01f, 0.01f)}, outer 0.5 m band {(double)edge / edgeN:0.##}; live DS2 particles inside {particles:0.#} = {particles / live1:0.##}× DS1 ({particles / (fp1.Width * fp1.Depth):0.###}/m²); ground covered outside DS1's footprint {spillCells * step * step:0} m²; emitter {dy + fp2.OffsetY:+0.##;-0.##;0} m above DS1's point (DS1's own emitter {fp1.OffsetY:+0.##} m)");
        }
        // fix pass 3 (review: B1d's 25-copy cap): larger square DS1 footprints at DS1 100201's particle density
        Console.WriteLine($"Larger DS1 footprints (square W1 × W1, DS1 {ds1Id}'s density and height, DS2 {ds2Id} {fp2.Width} × {fp2.Depth} m): copies per DS1 point / min and mean emitters over a spot of DS1's footprint / share of DS1's footprint under no emitter");
        foreach (float w in new[] { 15f, 20f, 25f, 30f, 40f, 50f, 60f })
        {
            var f1 = fp1 with { Width = w, Depth = w };
            float l1 = live1 * w * w / (fp1.Width * fp1.Depth);
            var cells = new List<string>();
            foreach (var (mode, label) in new[] { (MapEffects.SfxMode.B1b, "B1b"), (MapEffects.SfxMode.B1c, "B1c"), (MapEffects.SfxMode.B1d, "B1d") })
            {
                MapEffects.Mode = mode;
                var spots = MapEffects.PlacementsFor(System.Numerics.Vector3.Zero, 0, f1, fp2, l1, live2, out string why);
                var xz = spots.Select(s => (s.X, s.Z)).ToList();
                float h2x = fp2.Width / 2 + 1e-3f, h2z = fp2.Depth / 2 + 1e-3f;
                var cover = new List<int>();
                for (float x = -w / 2 + 0.125f; x < w / 2; x += 0.25f)
                    for (float z = -w / 2 + 0.125f; z < w / 2; z += 0.25f)
                        cover.Add(xz.Count(s => Math.Abs(x - s.X) <= h2x && Math.Abs(z - s.Z) <= h2z));
                cells.Add($"{label} {spots.Count} / {cover.Min()} / {cover.Average():0.##} / {MapEffects.Uncovered(xz, f1, fp2):0%}{(why.Contains("CAPPED") ? " (capped)" : "")}");
            }
            Console.WriteLine($"  W1 {w,2} m: {string.Join(" | ", cells)}");
        }
        MapEffects.Mode = saved;
        return 0;
    }

    // ------------------------------------------------------------------ pack-cmp

    /// <summary>pack-cmp A B: files only in A / only in B / different bytes; for a different MSB2, whether everything but
    /// the SFX regions is identical (both written again without their SFX regions) and the SFX regions by effect id.</summary>
    public static int PackCmp(string a, string b)
    {
        var fa = Directory.GetFiles(a, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(a, p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fb = Directory.GetFiles(b, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(b, p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var onlyA = fa.Except(fb).Order().ToList();
        var onlyB = fb.Except(fa).Order().ToList();
        var diff = fa.Intersect(fb).Where(r => !File.ReadAllBytes(Path.Combine(a, r)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(b, r)))).Order().ToList();
        Console.WriteLine($"{a} ({fa.Count} files) vs {b} ({fb.Count} files): {diff.Count} differ, {onlyA.Count} only in A, {onlyB.Count} only in B");
        foreach (var r in onlyA) Console.WriteLine($"  only in A: {r}");
        foreach (var r in onlyB) Console.WriteLine($"  only in B: {r}");
        foreach (var r in diff)
        {
            Console.WriteLine($"  differs: {r}");
            if (!r.EndsWith(".msb", StringComparison.OrdinalIgnoreCase)) continue;
            var ma = MSB2.Read(Path.Combine(a, r));
            var mb = MSB2.Read(Path.Combine(b, r));
            string Sfx(MSB2 m) => string.Join(" ", m.Regions.SFXs.GroupBy(s => s.EffectID).OrderBy(g => g.Key).Select(g => $"{g.Key}×{g.Count()}"));
            Console.WriteLine($"    SFX regions A: {Sfx(ma)} | B: {Sfx(mb)}");
            ma.Regions.SFXs.Clear(); mb.Regions.SFXs.Clear();
            bool same = ma.Write().AsSpan().SequenceEqual(mb.Write());
            Console.WriteLine($"    everything but the SFX regions (rewritten without them): {(same ? "identical" : "DIFFERENT")}");
        }
        return 0;
    }

    // ------------------------------------------------------------------ R11: field-by-field value ranges

    static IEnumerable<(string Key, double V)> Scalars(FFXDLSE.Param p)
    {
        switch (p)
        {
            case FFXDLSE.Param1 x: yield return ("int", x.Int); break;
            case FFXDLSE.Param7 x: yield return ("float", x.Float); break;
            case FFXDLSE.Param70 x: yield return ("tick", x.Tick); break;
            case FFXDLSE.Param85 x: yield return ("t2.lo", x.Tick1); yield return ("t2.hi", x.Tick2); break;
            case FFXDLSE.Param79 x: yield return ("i2.a", x.Int1); yield return ("i2.b", x.Int2); break;
            case FFXDLSE.Param81 x: yield return ("f2.a", x.Float1); yield return ("f2.b", x.Float2); break;
            case FFXDLSE.Param11 x when x.TickFloats.Count > 0:
                yield return ("fseq.keys", x.TickFloats.Count); yield return ("fseq.min", x.TickFloats.Min(t => t.Float)); yield return ("fseq.max", x.TickFloats.Max(t => t.Float)); yield return ("fseq.lastTime", x.TickFloats.Max(t => t.Tick)); break;
            case FFXDLSE.Param12 x when x.TickFloats.Count > 0:
                yield return ("fseq12.keys", x.TickFloats.Count); yield return ("fseq12.min", x.TickFloats.Min(t => t.Float)); yield return ("fseq12.max", x.TickFloats.Max(t => t.Float)); break;
            case FFXDLSE.Param5 x when x.TickInts.Count > 0: yield return ("iseq.keys", x.TickInts.Count); yield return ("iseq.min", x.TickInts.Min(t => t.Int)); yield return ("iseq.max", x.TickInts.Max(t => t.Int)); break;
            case FFXDLSE.Param6 x when x.TickInts.Count > 0: yield return ("iseq.keys", x.TickInts.Count); yield return ("iseq.min", x.TickInts.Min(t => t.Int)); yield return ("iseq.max", x.TickInts.Max(t => t.Int)); yield return ("iseq.lastTime", x.TickInts.Max(t => t.Tick)); break;
            case FFXDLSE.Param19 x when x.TickColors.Count > 0:
                yield return ("cseq.keys", x.TickColors.Count); yield return ("cseq.rgbMax", x.TickColors.Max(t => Math.Max(t.Color.R, Math.Max(t.Color.G, t.Color.B)))); yield return ("cseq.aMax", x.TickColors.Max(t => t.Color.A)); yield return ("cseq.lastTime", x.TickColors.Max(t => t.Tick)); break;
            case FFXDLSE.Param17 x when x.TickColors.Count > 0:
                yield return ("cseq17.rgbMax", x.TickColors.Max(t => Math.Max(t.Color.R, Math.Max(t.Color.G, t.Color.B)))); yield return ("cseq17.aMin", x.TickColors.Min(t => t.Color.A)); break;
            case FFXDLSE.Param82 x: foreach (var s in Scalars(x.Param)) yield return ("rnd." + s.Key, s.V); yield return ("rnd.var", x.Float); break;
        }
    }

    static Dictionary<string, (double Min, double Max, int N)>? ds2Ranges;

    static void Calls(FFXDLSE fx, Action<string, FFXDLSE.ParamList> visit)
    {
        void Walk(FFXDLSE.Param p)
        {
            switch (p)
            {
                case FFXDLSE.Param37 c: if (c.EffectID != 0) visit($"class {c.EffectID}", c.ParamList); foreach (var q in c.ParamList.Params) Walk(q); break;
                case FFXDLSE.Param38 a: if (a.ActionID != 0) visit($"action {a.ActionID}", a.ParamList); foreach (var q in a.ParamList.Params) Walk(q); break;
                case FFXDLSE.Param82 n: Walk(n.Param); break;
                case FFXDLSE.Param84 n: Walk(n.Param); break;
            }
        }
        foreach (var st in fx.Effect.StateMap.States)
            foreach (var a in st.Actions) { visit($"state action {a.ID}", a.ParamList); foreach (var p in a.ParamList.Params) Walk(p); }
    }

    static string Sig(FFXDLSE.ParamList l) => $"{l.Unk04}:" + string.Join(",", l.Params.Select(p => p.GetType().Name.Replace("Param", "")));

    /// <summary>Our scalar fields outside [min, max] of DS2's own uses of the same call (id + signature) and position.</summary>
    static (int N, List<string> Outside) ValueRanges(FFXDLSE fx)
    {
        if (ds2Ranges == null)
        {
            ds2Ranges = new();
            foreach (var (_, (vfx, _)) in MistSurvey.Ds2All())
                Calls(vfx, (who, l) =>
                {
                    string sig = Sig(l);
                    for (int i = 0; i < l.Params.Count; i++)
                        foreach (var (k, v) in Scalars(l.Params[i]))
                        {
                            string key = $"{who} [{sig}] p{i} {k}";
                            ds2Ranges[key] = ds2Ranges.TryGetValue(key, out var r) ? (Math.Min(r.Min, v), Math.Max(r.Max, v), r.N + 1) : (v, v, 1);
                        }
                });
        }
        int n = 0;
        var outside = new List<string>();
        Calls(fx, (who, l) =>
        {
            string sig = Sig(l);
            for (int i = 0; i < l.Params.Count; i++)
                foreach (var (k, v) in Scalars(l.Params[i]))
                {
                    n++;
                    string key = $"{who} [{sig}] p{i} {k}";
                    if (!ds2Ranges.TryGetValue(key, out var r)) { outside.Add($"{who} p{i} {k} = {v:0.####}: DS2 never has this field"); continue; }
                    if (v < r.Min - 1e-4 || v > r.Max + 1e-4) outside.Add($"{who} p{i} {k} = {v:0.####} (DS2 {r.Min:0.####}..{r.Max:0.####} over {r.N} uses)");
                }
        });
        return (n, outside.Distinct().ToList());
    }

    // ------------------------------------------------------------------ R12: DS2's 71 compile vs DSR's struct (fix pass 3)

    /// <summary>Struct slot names of DS2's action-71 struct (tag 0x47) = DSR's FXR1 ActionData71 (dword index).</summary>
    static readonly (int Slot, string Name, int Param)[] Slots71 =
    [
        (0xa, "lifetime mean (Duration)", 3), (0xb, "min/mean (DurationMult)", 3), (0xc, "(max−min)/mean (LifetimeVariance)", 3),
        (0xd, "AttachedBool", 23), (0xe, "RenderDepth", 32), (0x10, "TextureID", 1), (0x11, "texture 2 (Unk7)", 2),
        (0x12, "OrientationMode", 4), (0x13, "BlendMode", 5), (0x14, "Unk10", 28), (0x3e, "frame slices per row", 6),
        (0x3f, "frame count", 7), (0x68, "DS1R_UnkA1 (4/5/8)", 29), (0x71, "Unk16", 24), (0x72, "Unk17", 25), (0x73, "Unk18", 26),
        (0x74, "Unk19", 35), (0x75, "AlphaMode", 27), (0x76, "Saturation", 30), (0x77, "Unk22 bit 1", 31),
    ];

    static double Num(FFXDLSE.Param p) => p switch
    {
        FFXDLSE.Param1 x => x.Int, FFXDLSE.Param7 x => x.Float, FFXDLSE.Param40 x => x.TextureID, _ => double.NaN,
    };

    /// <summary>The scalar slots FUN_14100c880 (DS2's action-71 compile, decomp DarkSoulsII.c 3127151–3127566) writes from
    /// a 71 parameter list: 0xa–0xc ← t2 p3 (mean = lo + (hi − lo)/2; 0xb = lo/mean, 0xc = (hi − lo)/mean; |mean| ≤ 2^-23 →
    /// 0, 1), 0xd ← p23, 0xe ← p32, 0x10 ← p1, 0x11 ← p2, 0x12 ← p4, 0x13 ← p5, 0x14 ← p28, 0x3e ← p6, 0x3f ← p7,
    /// 0x68 = 5 if p2 = 0 and p30 ≥ 0 else (p29 = 0 ? 4 : 8), 0x71–0x73 ← p24–p26, 0x74 ← p35 (count ≥ 36, else 0),
    /// 0x75 ← p27, 0x76 ← p30, 0x77 = caller flag | 2·(p31 ≠ 0) (only bit 1 here).</summary>
    public static Dictionary<int, double> Compile71(List<FFXDLSE.Param> q)
    {
        var s = new Dictionary<int, double>();
        var t = (FFXDLSE.Param85)q[3];
        float lo = t.Tick1, hi = t.Tick2, mean = (hi - lo) * 0.5f + lo;
        s[0xa] = mean;
        if (Math.Abs(mean - 0f) > 1.1920929e-07f) { s[0xb] = lo / mean; s[0xc] = (hi - lo) / mean; } else { s[0xb] = 0; s[0xc] = 1; }
        foreach (var (slot, _, p) in Slots71.Where(x => x.Slot is >= 0xd and <= 0x3f || x.Slot is >= 0x71 and <= 0x76)) s[slot] = Num(q[p]);
        if (q.Count < 0x24) s[0x74] = 0;
        s[0x68] = Num(q[2]) == 0 && Num(q[30]) >= 0 ? 5 : Num(q[29]) == 0 ? 4 : 8;
        s[0x77] = Num(q[31]) != 0 ? 1 : 0;
        return s;
    }

    /// <summary>The same slots as DSR stores them in FXR1 ActionData71.</summary>
    public static Dictionary<int, double> Dsr71(FXR1.FXActionData.ClusterAppearanceBillboardActionData71 d) => new()
    {
        [0xa] = d.Duration, [0xb] = d.DurationMult, [0xc] = d.LifetimeVariance, [0xd] = d.AttachedBool, [0xe] = d.RenderDepth,
        [0x10] = d.TextureID, [0x11] = d.Unk7, [0x12] = d.OrientationMode, [0x13] = d.BlendMode, [0x14] = d.Unk10,
        [0x3e] = d.AnimFrameSliceCountPerRow, [0x3f] = d.AnimFrameTotalCount, [0x68] = d.DS1R_UnkA1,
        [0x71] = d.Unk16, [0x72] = d.Unk17, [0x73] = d.Unk18, [0x74] = d.Unk19, [0x75] = d.AlphaMode, [0x76] = d.Saturation,
        [0x77] = (d.Unk22 >> 1) & 1,
    };

    static bool Same(double a, double b) => a == b || Math.Abs(a - b) <= 1e-4 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));

    /// <summary>Compares the compiled slots with DSR's; returns (unexplained differences, lines). A difference is
    /// "by rule" only for the converter's documented substitutions: texture ids 700000 + t, DS1 blends DS2 never uses → 2,
    /// enumerated tail values DS2's 71 never uses → a DS2 value (Enum71), the no-end sentinel (--sfx-noend sentinel).</summary>
    static (int Bad, List<string> Text) Compare71(Dictionary<int, double> ours, Dictionary<int, double> dsr)
    {
        int bad = 0;
        var text = new List<string>();
        bool sentinel = dsr[0xa] >= 1e5 && Same(ours[0xa], -1 / 30.0);
        foreach (var (slot, name, p) in Slots71)
        {
            double o = ours[slot], r = dsr[slot];
            string why;
            if (slot is 0x10 or 0x11 && o == (r == 0 ? 0 : FfxPorter.TexBase + r)) why = Same(o, r) ? "=" : $"by rule (texture {FfxPorter.TexBase} + t)";
            else if (Same(o, r)) why = "=";
            else if (slot == 0x13 && r is not (2 or 4) && o == 2) why = "by rule (DS1 blend DS2 never uses → 2)";
            else if (slot is 0x14 or >= 0x71 and <= 0x75 && !FfxPorter.Ds2Values71(p).Contains(r) && FfxPorter.Ds2Values71(p).Contains(o)) why = $"by rule (DS2's 71 never uses {r} in p{p}: {string.Join("/", FfxPorter.Ds2Values71(p).Order())})";
            else if (slot is >= 0xa and <= 0xc && sentinel) why = "SENTINEL (--sfx-noend sentinel: not DS1's own lifetime)";
            else { why = "UNEXPLAINED"; bad++; }
            if (why != "=") text.Add($"         0x{slot:x2} {name} (p{p}): DSR {r:0.####} / ours {o:0.####}: {why}");
        }
        return (bad, text);
    }

    /// <summary>R12: every action 71 of a converted effect, compiled as FUN_14100c880 does, must give DSR's own struct
    /// for its DS1 counterpart (rule B2b-id inverse → DS1 id; matched by texture).</summary>
    static int Compile71Check(FFXDLSE fx, int nid, List<string> lines)
    {
        string dsr = Program.DefaultDsrRoot;
        int? ds1 = FfxPorter.Ds1IdFor(dsr, nid);
        if (ds1 == null) { lines.Add($"FAIL R12 71 compile {nid}: no DS1 effect by rule B2b-id"); return 1; }
        var sources = FfxPorter.Ds1EffectAll(dsr, ds1.Value);
        var ours = new List<List<FFXDLSE.Param>>();
        Calls(fx, (who, l) => { if (who == "action 71") ours.Add(l.Params); });
        int fails = 0;
        foreach (var q in ours)
        {
            int tex = (int)Num(q[1]) - FfxPorter.TexBase;
            var comp = Compile71(q);
            var cand = sources.SelectMany(s => s.Fx.AllFXActionDatas.OfType<FXR1.FXActionData.ClusterAppearanceBillboardActionData71>().Where(d => d.TextureID == tex).Select(d => (s.Binder, D: d))).ToList();
            if (cand.Count == 0) { lines.Add($"FAIL R12 71 compile {nid} tex {tex}: no DS1 billboard with that texture in f{ds1:D7}"); fails++; continue; }
            var best = cand.Select(c => (c.Binder, R: Compare71(comp, Dsr71(c.D)))).OrderBy(c => c.R.Bad).First();
            bool ok = best.R.Bad == 0;
            if (!ok) fails++;
            lines.Add($"{(ok ? "PASS" : "FAIL")} R12 71 compile {nid} (DS1 {ds1} billboard tex {tex}, DSR {best.Binder}): FUN_14100c880 on our parameters vs DSR's ActionData71, {Slots71.Length} scalar slots: {Slots71.Length - best.R.Text.Count} equal, {best.R.Text.Count - best.R.Bad} by rule, {best.R.Bad} unexplained");
            lines.AddRange(best.R.Text);
        }
        if (ours.Count == 0) lines.Add($"INFO R12 71 compile {nid}: no action 71");
        return fails;
    }

    /// <summary>compile71: evidence that DS2's 71 compile FUN_14100c880 produces DSR's ActionData71 — the registrations in
    /// the exe, DSR's derived slots against the compile's formulas over every DSR binder, and the value sets of each
    /// DSR slot next to the DS2 parameter the compile reads into it.</summary>
    public static int Compile71Survey()
    {
        var lines = new List<string>();
        string exe = Path.GetFullPath(Exe);
        if (!File.Exists(exe)) exe = Path.Combine(@"C:\EchoesOfLordran\bin\ds2s", "DarkSoulsII.unpacked.exe");
        var d = File.ReadAllBytes(exe);
        int pe = BitConverter.ToInt32(d, 0x3c), nsec = BitConverter.ToUInt16(d, pe + 6), opt = BitConverter.ToUInt16(d, pe + 20);
        long imageBase = BitConverter.ToInt64(d, pe + 24 + 24);
        int Off(long va)
        {
            long rva = va - imageBase;
            for (int i = 0; i < nsec; i++)
            {
                int o = pe + 24 + opt + i * 40;
                uint sva = BitConverter.ToUInt32(d, o + 12), rsize = BitConverter.ToUInt32(d, o + 16), raw = BitConverter.ToUInt32(d, o + 20);
                if (rva >= sva && rva < sva + rsize) return (int)(raw + rva - sva);
            }
            throw new InvalidDataException($"{va:x} not in the file");
        }
        // registrations: FUN_140fe4e70 (type 0x47 → FUN_140fe4ec0 → FUN_14100c880) and FUN_140bff850 (15000 → FUN_140bffe20)
        foreach (var (va, what) in new[] { (0x140fe4e70L, "71 registration FUN_140fe4e70"), (0x140bff850L, "15000 registration FUN_140bff850"), (0x140fe4ec0L, "71 create FUN_140fe4ec0") })
        {
            // the first "mov ecx, imm32" (B9 imm32) and the first call (E8 rel32) within 0x40 bytes: the type id and the
            // registration call (FUN_140a33220) / the compile
            int o = Off(va);
            int mov = -1;
            for (int i = o; i < o + 0x40 && mov < 0; i++) if (d[i] == 0xB9 && d[i + 3] == 0 && d[i + 4] == 0) mov = i;
            int call = Array.FindIndex(d, o, 0x40, b => b == 0xE8);
            // or "xor eax, eax" (33 C0) … "lea ecx, [rax + imm8]" (8D 48 imm8), as FUN_140fe4e70 does
            int lea = -1;
            if (d[o + 4] == 0x33 && d[o + 5] == 0xC0) for (int i = o + 6; i < o + 0x40 && lea < 0; i++) if (d[i] == 0x8D && d[i + 1] == 0x48) lea = i;
            string imm = mov >= 0 ? $"mov ecx, 0x{BitConverter.ToUInt32(d, mov + 1):x} ({BitConverter.ToUInt32(d, mov + 1)}) at 0x{va + (mov - o):x}"
                : lea >= 0 ? $"xor eax, eax; lea ecx, [rax + 0x{d[lea + 2]:x}] = type {d[lea + 2]} at 0x{va + (lea - o):x}" : "no type immediate";
            string tgt = call >= 0 ? $"call 0x{va + (call - o) + 5 + BitConverter.ToInt32(d, call + 1):x} at 0x{va + (call - o):x}" : "";
            lines.Add($"{what} @0x{va:x}: {imm}; {tgt}");
        }
        // DSR's struct against the compile's derived slots
        int five = 0, fiveBad = 0, other = 0, otherBad = 0, total = 0;
        var unk22 = new SortedDictionary<int, int>();
        var sets = new SortedDictionary<int, SortedDictionary<double, int>>();
        foreach (var path in Directory.GetFiles(Path.Combine(Program.DefaultDsrRoot, "sfx"), "FRPG_SfxBnd_*.ffxbnd.dcx").Order())
            foreach (var f in BND3.Read(DCX.Decompress(File.ReadAllBytes(path))).Files.Where(f => f.Name.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase)))
            {
                FXR1 fxr;
                try { fxr = FXR1.Read(f.Bytes); } catch { continue; }
                foreach (var a in fxr.AllFXActionDatas.OfType<FXR1.FXActionData.ClusterAppearanceBillboardActionData71>())
                {
                    total++;
                    bool f5 = a.Unk7 == 0 && a.Saturation >= 0;
                    if (f5) { five++; if (a.DS1R_UnkA1 != 5) fiveBad++; } else { other++; if (a.DS1R_UnkA1 is not (4 or 8)) otherBad++; }
                    unk22[a.Unk22] = unk22.GetValueOrDefault(a.Unk22) + 1;
                    foreach (var (slot, v) in Dsr71(a))
                    {
                        if (!sets.TryGetValue(slot, out var s)) sets[slot] = s = new();
                        double k = Math.Round(v, 4);
                        s[k] = s.GetValueOrDefault(k) + 1;
                    }
                }
            }
        lines.Add($"DSR action-71 structs over every DSR binder: {total} (duplicates across binders counted)");
        lines.Add($"  0x68 DS1R_UnkA1: compile gives 5 when texture 2 = 0 and Saturation ≥ 0: {five} such structs, {fiveBad} with UnkA1 ≠ 5; the other {other}: {otherBad} with UnkA1 ∉ {{4, 8}}");
        lines.Add($"  0x77 Unk22 values: {string.Join("  ", unk22.Select(kv => $"{kv.Key}×{kv.Value}"))} (bit 1 = p31 ≠ 0; bit 0 = the caller's flag)");
        foreach (var (slot, name, p) in Slots71)
        {
            if (slot is 0xa or 0xb or 0xc or 0x10 or 0x11) continue;
            var s = sets[slot];
            string ds2 = string.Join("/", FfxPorter.Ds2Values71(p).Order().Select(v => v.ToString("0.####")));
            lines.Add($"  0x{slot:x2} {name}: DSR {string.Join("  ", s.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{kv.Key:0.####}×{kv.Value}"))}{(s.Count > 8 ? $"  (+{s.Count - 8})" : "")} | DS2 71 p{p}: {(ds2.Length > 0 ? ds2 : "(not a scalar)")}");
        }
        foreach (var l in lines) Console.WriteLine(l);
        return fiveBad == 0 && otherBad == 0 && !unk22.Keys.Any(k => (k & 2) != 0) ? 0 : 2;
    }

    // ------------------------------------------------------------------ b2b-check

    public static int Run(string pack)
    {
        var lines = new List<string>();
        int fails = 0;
        void Rule(string rule, bool ok, string text) { lines.Add($"{(ok ? "PASS" : "FAIL")} {rule}: {text}"); if (!ok) fails++; }
        var facts = ExeFacts(lines);
        // fix pass 3: the constants the converter and this checker use must be the exe's own
        Rule("R1/R2 exe constants", facts.Limit == FfxPorter.LazyIdLimit && ExeName(facts, 9999) == NameFor(9999) && ExeName(facts, 10000) == NameFor(10000),
            $"exe limit {(facts.Limit?.ToString() ?? "unread")} vs LazyIdLimit {FfxPorter.LazyIdLimit}; exe names for 9999/10000 '{ExeName(facts, 9999) ?? "unread"}' / '{ExeName(facts, 10000) ?? "unread"}' vs NameFor '{NameFor(9999)}' / '{NameFor(10000)}'");
        string sfx = Path.Combine(pack, "title", "sfx");
        string ap = Path.Combine(sfx, "sfx9999_Append.ffxbnd.dcx");
        var vanillaAppend = MistSurvey.ReadBank(Path.Combine(MistSurvey.SfxDir(), "sfx9999_Append.ffxbnd.dcx"));
        var vanillaBase = MistSurvey.ReadBank(Path.Combine(MistSurvey.SfxDir(), "sfx9999.ffxbnd.dcx"));
        IBinder append = File.Exists(ap) ? MistSurvey.ReadBank(ap) : vanillaAppend;
        lines.Add($"pack {pack}: {(File.Exists(ap) ? $"title:/sfx/sfx9999_Append.ffxbnd.dcx ({append.Files.Count} entries, vanilla {vanillaAppend.Files.Count})" : "no title:/sfx bank (vanilla banks)")}");
        var vanillaKeys = vanillaAppend.Files.Select(f => $"{f.ID}|{f.Name}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = append.Files.Where(f => !vanillaKeys.Contains($"{f.ID}|{f.Name}")).ToList();
        var used = FfxPorter.Ds2UsedIds();

        // R1 over every title-bank effect (vanilla and added)
        var allFfx = vanillaBase.Files.Concat(append.Files).Where(f => NameId(f.Name) != null).ToList();
        var over = allFfx.Where(f => NameId(f.Name) >= FfxPorter.LazyIdLimit).Select(f => Path.GetFileName(f.Name)).ToList();
        Rule("R1 lazy-load bitset<30000>", over.Count == 0, over.Count == 0 ? $"all {allFfx.Count} title-bank effects have ids < {FfxPorter.LazyIdLimit}" : $"{string.Join(", ", over)} ≥ {FfxPorter.LazyIdLimit}: std::out_of_range at the first spawn → abort (s9-10)");

        // the added effects
        var addedFx = new List<(BinderFile F, FFXDLSE Fx, int NameId)>();
        foreach (var f in added.Where(f => f.Name.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase)))
        {
            int nid = NameId(f.Name) ?? -1;
            FFXDLSE fx;
            try { fx = FFXDLSE.Read(f.Bytes); }
            catch (Exception e) { Rule($"parse {Path.GetFileName(f.Name)}", false, e.Message); continue; }
            addedFx.Add((f, fx, nid));
            // R2 name (the exe's own format, filled in for the id; R1/R2 exe constants above checks NameFor agrees)
            bool nameOk = f.Name.Equals(ExeName(facts, nid) ?? NameFor(nid), StringComparison.OrdinalIgnoreCase) && ExeName(facts, nid) != null;
            int dup = append.Files.Count(x => x.Name.Equals(f.Name, StringComparison.OrdinalIgnoreCase)) + vanillaBase.Files.Count(x => x.Name.Equals(f.Name, StringComparison.OrdinalIgnoreCase));
            Rule($"R2 entry name {nid}", nameOk && dup == 1, $"'{f.Name}' {(nameOk ? "= the runtime's name" : $"≠ '{NameFor(nid)}'")}, {dup} entr{(dup == 1 ? "y" : "ies")} with that name in the title banks");
            // R3 own id
            Rule($"R3 DLsE id {nid}", fx.Effect.ID == nid, $"effect id {fx.Effect.ID}, name id {nid}");
            // R4 classes
            var classes = new SortedSet<int>();
            void Walk(FFXDLSE.Param p)
            {
                switch (p)
                {
                    case FFXDLSE.Param37 c: if (c.EffectID != 0) classes.Add(c.EffectID); foreach (var q in c.ParamList.Params) Walk(q); break;
                    case FFXDLSE.Param38 a: foreach (var q in a.ParamList.Params) Walk(q); break;
                    case FFXDLSE.Param82 n: Walk(n.Param); break;
                    case FFXDLSE.Param84 n: Walk(n.Param); break;
                }
            }
            foreach (var st in fx.Effect.StateMap.States) foreach (var a in st.Actions) foreach (var p in a.ParamList.Params) Walk(p);
            var ds2Classes = vanillaBase.Files.Concat(vanillaAppend.Files).Select(x => NameId(x.Name)).OfType<int>().Where(i => i is >= 2000 and <= 2200).ToHashSet();
            var badCls = classes.Where(c => c is < 2000 or > 2200 || !ds2Classes.Contains(c)).ToList();
            Rule($"R4 classes {nid}", badCls.Count == 0, $"calls [{string.Join(" ", classes)}]{(badCls.Count > 0 ? $"; not an eagerly loaded DS2 class: {string.Join(" ", badCls)}" : ": all DS2 classes 2000–2200 (eagerly loaded)")}; resource set classes [{string.Join(" ", fx.Effect.ResourceSet.Vector5)}] {(fx.Effect.ResourceSet.Vector5.Order().SequenceEqual(classes) ? "match" : "DIFFER")}");
            if (!fx.Effect.ResourceSet.Vector5.Order().SequenceEqual(classes)) fails++;
            // R6 family (fix pass 3: rule B2b-id's own block — 20000 + b, b ≥ 1, below the bitset limit — and a free family)
            int b = nid % 10000;
            var fam = new[] { b, 10000 + b, 20000 + b }.Where(i => i != nid && used.Contains(i)).ToList();
            bool inBlock = nid > FfxPorter.IdBlock && nid < FfxPorter.LazyIdLimit && b >= 1;
            Rule($"R6 id family {nid}", inBlock && fam.Count == 0 && !used.Contains(nid),
                !inBlock ? $"id {nid} outside rule B2b-id's block {FfxPorter.IdBlock + 1}..{FfxPorter.LazyIdLimit - 1} (b = {b}{(b == 0 ? ": family 0 excluded" : "")})"
                : fam.Count > 0 || used.Contains(nid) ? $"DS2 knows {string.Join(" ", fam.Append(used.Contains(nid) ? nid : -1).Where(i => i >= 0))}"
                : $"family {b} (= {b}, {10000 + b}, {20000 + b}) unknown to DS2: only our regions create {nid}");
            // R12 the 71 compile (FUN_14100c880) reproduced on our parameters = DSR's own ActionData71 struct
            fails += Compile71Check(fx, nid, lines);
            // R8 signatures
            var probs = MistSurvey.CheckEffectPublic(fx);
            Rule($"R8 parameter signatures {nid}", probs.Count == 0, probs.Count == 0 ? "every action/class call uses a signature DS2's own effects use; write→read→write identical" : string.Join("; ", probs.Take(5)));
            // R11 (report): every scalar field against DS2's own range for the same call and position
            var (n11, outside) = ValueRanges(fx);
            lines.Add($"INFO R11 field ranges {nid}: {n11} scalar fields compared with DS2's uses of the same action/class signature; {outside.Count} outside DS2's range{(outside.Count > 0 ? ":" : "")}");
            foreach (var o in outside) lines.Add($"       {o}");
        }
        if (addedFx.Count == 0) lines.Add("(no added effects)");

        // R5 tables
        var spF = append.Files.First(f => f.Name.EndsWith("sfxParameter.sfxparam", StringComparison.OrdinalIgnoreCase)).Bytes;
        var sdF = append.Files.First(f => f.Name.EndsWith("sfxDebugParameter.sfxparam", StringComparison.OrdinalIgnoreCase)).Bytes;
        var baseSp = vanillaBase.Files.First(f => f.Name.EndsWith("sfxParameter.sfxparam", StringComparison.OrdinalIgnoreCase)).Bytes;
        bool TableOk(byte[] t, string magic, int size, out List<int> ids, out string text)
        {
            int count = BitConverter.ToInt32(t, 8), hdr = BitConverter.ToInt32(t, 12);
            ids = Enumerable.Range(0, count).Select(i => BitConverter.ToInt32(t, hdr + i * size)).ToList();
            bool strictly = ids.Zip(ids.Skip(1)).All(p => p.First < p.Second);
            string m = Encoding.ASCII.GetString(t, 0, 4);
            text = $"magic {m} v{BitConverter.ToInt32(t, 4)}, {count} records, header {hdr}, size {t.Length} = {hdr} + {count}×{size}: {t.Length == hdr + count * size}, strictly ascending {strictly}";
            return m == magic && hdr == 32 && t.Length == hdr + count * size && strictly;
        }
        bool spOk = TableOk(spF, "sfxp", 64, out var spIds, out string spText);
        bool sdOk = TableOk(sdF, "sfxd", 8, out var sdIds, out string sdText);
        TableOk(baseSp, "sfxp", 64, out var baseIds, out _);
        var missing = addedFx.Where(a => !spIds.Contains(a.NameId) || !sdIds.Contains(a.NameId)).Select(a => a.NameId).ToList();
        Rule("R5 sfxParameter / sfxDebugParameter (binary search)", spOk && sdOk && missing.Count == 0, $"{spText}; debug: {sdText}; added effects without a record: {(missing.Count == 0 ? "none" : string.Join(" ", missing))}");
        foreach (var a in addedFx)
        {
            int i = spIds.IndexOf(a.NameId);
            if (i < 0) continue;
            var r = spF.AsSpan(32 + i * 64, 64).ToArray();
            lines.Add($"     record {a.NameId}: draw distance {BitConverter.ToSingle(r, 4)} m, flags {r[0x10]} {r[0x11]} {r[0x12]} {r[0x13]}, +0x1C {BitConverter.ToInt32(r, 0x1C)} frames");
        }

        // R7 textures
        var vres = MistSurvey.ReadBank(Path.Combine(MistSurvey.SfxDir(), "sfx9999res.ffxbnd")).Files.Select(f => Path.GetFileName(f.Name).ToLowerInvariant()).ToHashSet();
        foreach (var (folder, name) in new[] { ("sfx", "sfx9999res_Append.ffxbnd"), ("sfx_lq", "sfx9999lqres_Append.ffxbnd") })
        {
            string rp = Path.Combine(pack, "title", folder, name);
            var res = File.Exists(rp) ? BND4.Read(File.ReadAllBytes(rp)) : null;
            var names = res?.Files.ToDictionary(f => Path.GetFileName(f.Name).ToLowerInvariant(), f => f) ?? new();
            foreach (var a in addedFx)
                foreach (int t in a.Fx.Effect.ResourceSet.Vector2)
                {
                    string tn = $"s{t:D6}.tpf";
                    bool present = names.TryGetValue(tn, out var tf);
                    string tpfText = "";
                    bool tpfOk = false;
                    if (tf != null)
                    {
                        try
                        {
                            var tpf = TPF.Read(tf.Bytes);
                            var tx = tpf.Textures[0];
                            var dds = Textures.Dds.Read(tx.Bytes);
                            tpfOk = tx.Name.Equals($"s{t:D6}", StringComparison.OrdinalIgnoreCase) && dds.Width > 0;
                            tpfText = $", TPF '{tx.Name}' format {tx.Format} {dds.Width}×{dds.Height}";
                        }
                        catch (Exception e) { tpfText = $", TPF unreadable: {e.Message}"; }
                    }
                    Rule($"R7 texture {t} ({folder}/{name})", t < FfxPorter.TexIdLimit && present && tpfOk && !vres.Contains(tn),
                        $"{tn}: name number {(t < FfxPorter.TexIdLimit ? "<" : "≥")} {FfxPorter.TexIdLimit}, {(present ? "present" : "MISSING")}{tpfText}{(vres.Contains(tn) ? ", COLLIDES with a vanilla res entry" : "")}");
                }
        }

        // R9 vanilla entries
        int same = 0, changed = 0;
        var byKey = append.Files.GroupBy(f => $"{f.ID}|{f.Name}", StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var v in vanillaAppend.Files.Where(f => !f.Name.EndsWith(".sfxparam", StringComparison.OrdinalIgnoreCase)))
            if (byKey.TryGetValue($"{v.ID}|{v.Name}", out var m) && m.Flags == v.Flags && m.Bytes.AsSpan().SequenceEqual(v.Bytes)) same++; else changed++;
        var vSp = vanillaAppend.Files.First(f => f.Name.EndsWith("sfxParameter.sfxparam", StringComparison.OrdinalIgnoreCase)).Bytes;
        TableOk(vSp, "sfxp", 64, out var vSpIds, out _);
        int keptRecs = Enumerable.Range(0, vSpIds.Count).Count(i => { int j = spIds.IndexOf(vSpIds[i]); return j >= 0 && spF.AsSpan(32 + j * 64, 64).SequenceEqual(vSp.AsSpan(32 + i * 64, 64)); });
        Rule("R9 vanilla Append entries", changed == 0 && keptRecs == vSpIds.Count, $"{same} effect entries byte-identical (id, name, flags, bytes), {changed} changed/missing; sfxParameter records unchanged {keptRecs}/{vSpIds.Count}");

        // R10 regions
        var known = vanillaBase.Files.Concat(append.Files).Select(f => NameId(f.Name)).OfType<int>().ToHashSet();
        var recs = baseIds.Concat(spIds).ToHashSet();
        string[] msbs = Directory.Exists(Path.Combine(pack, "gamedata", "map")) ? Directory.GetFiles(Path.Combine(pack, "gamedata", "map"), "*.msb", SearchOption.AllDirectories) : [];
        foreach (var msbPath in msbs.Order())
        {
            MSB2 msb;
            try { msb = MSB2.Read(msbPath); } catch { continue; }
            var sfxs = msb.Regions.SFXs;
            if (sfxs.Count == 0) continue;
            var groups = sfxs.GroupBy(s => s.EffectID).OrderBy(g => g.Key).ToList();
            foreach (var g in groups)
            {
                int id = g.Key;
                bool ok = known.Contains(id) && recs.Contains(id) && id < FfxPorter.LazyIdLimit;
                Rule($"R10 {Path.GetFileNameWithoutExtension(msbPath)} SFX {id} ×{g.Count()}", ok,
                    $"{(known.Contains(id) ? "effect in a title bank" : "NO title-bank effect")}, {(recs.Contains(id) ? "record" : "NO record (never spawned)")}, id {(id < FfxPorter.LazyIdLimit ? "<" : "≥")} {FfxPorter.LazyIdLimit}");
            }
        }

        foreach (var l in lines) Console.WriteLine(l);
        Console.WriteLine(fails == 0 ? "B2B RUNTIME CHECK OK" : $"B2B RUNTIME CHECK: {fails} failure(s)");
        return fails == 0 ? 0 : 2;
    }
}
