using System.Globalization;
using System.Reflection;
using System.Text;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// mist-survey (session 9, mist track: why Firelink's ground mist is not visible; DS1 FFX → DS2 effect route).
/// <list type="bullet">
/// <item><c>banks [GAME]</c>: DS2's global effect banks (title:/sfx: sfx9999, _Append, res, res_Append, sfxcommon) — entries, id ranges, overlaps</item>
/// <item><c>actions</c>: every action id / effect-class id (Param37) used by DS2's banks, with counts and parameter signatures</item>
/// <item><c>ds2 ID…</c>: compact tree dump of DS2 effects (one line per parameter)</item>
/// <item><c>ds1 BINDER ID…</c>: compact tree dump of DS1 (DSR FXR1) effects; BINDER = 10_02, 10, CommonEffects</item>
/// <item><c>regions [ID…]</c>: every vanilla DS2 MSB SFX region (all fields) with the given effect ids (default 3010 3011 3012)</item>
/// <item><c>tex ID…</c>: where DS2 keeps an effect texture id (res bank entry, TPF format/size)</item>
/// </list>
/// </summary>
public static class MistSurvey
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static int Run(string[] args)
    {
        string what = args.Length > 1 ? args[1] : "banks";
        switch (what)
        {
            case "banks": return Banks(args.Length > 2 ? args[2] : Program.DefaultDs2Root);
            case "actions": return Actions();
            case "ds2":
                foreach (var id in args.Skip(2).Select(int.Parse))
                {
                    var fx = Ds2Effect(id, out string where);
                    Console.WriteLine($"=== DS2 effect {id} ({where})");
                    if (fx != null) Console.Write(Tree(fx));
                }
                return 0;
            case "ds1":
                foreach (var id in args.Skip(3).Select(int.Parse))
                {
                    var fxr = Ds1Effect(args[2], id, out string where);
                    Console.WriteLine($"=== DS1 effect {id} ({where})");
                    if (fxr != null) Console.Write(Ds1Tree(fxr));
                }
                return 0;
            case "regions": return Regions(args.Skip(2).Select(int.Parse).DefaultIfEmpty(-1).ToArray());
            case "tex": return Tex(args.Skip(2).Select(int.Parse).ToArray());
            case "ds1-bank": return Ds1Bank(args[2]);
            case "life71":
            {
                // DS1 ActionData71 lifetime fields = compiled t2(min, max): DurationMult = min/mean, LifetimeVariance = (max-min)/mean → 2·Mult + Var = 2
                var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(Ds1BankPath(args.Length > 2 ? args[2] : "CommonEffects"))));
                int ok = 0, bad = 0;
                foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase)))
                {
                    FXR1 fxr;
                    try { fxr = FXR1.Read(f.Bytes); } catch { continue; }
                    foreach (var d in fxr.AllFXActionDatas.OfType<FXR1.FXActionData.ClusterAppearanceBillboardActionData71>())
                        if (Math.Abs(2 * d.DurationMult + d.LifetimeVariance - 2) < 1e-3) ok++;
                        else { bad++; if (bad <= 5) Console.WriteLine($"  {Path.GetFileName(f.Name)}: Duration {d.Duration} Mult {d.DurationMult} Var {d.LifetimeVariance}"); }
                }
                Console.WriteLine($"DS1 action 71 lifetime rule 2·DurationMult + LifetimeVariance = 2: {ok} hold, {bad} don't");
                return 0;
            }
            case "convert": // convert DS1MAP [dsr-alpha-off] [noend-sentinel] ID… (= build-pack --sfx-dsr-alpha off / --sfx-noend sentinel)
                if (args.Skip(3).Contains("dsr-alpha-off")) Build.Maps.FfxPorter.DsrAlpha = false;
                if (args.Skip(3).Contains("noend-sentinel")) Build.Maps.FfxPorter.NoEndSentinel = true;
                return ConvertCheck(args[2], args.Skip(3).Where(a => a is not ("dsr-alpha-off" or "noend-sentinel")).Select(int.Parse).ToArray());
            case "bank-check": return BankCheck(args[2]);
            case "roundtrip":
            {
                int same = 0, diff = 0;
                foreach (var bank in new[] { "sfx9999.ffxbnd.dcx", "sfx9999_Append.ffxbnd.dcx" })
                    foreach (var f in ReadBank(Path.Combine(SfxDir(), bank)).Files.Where(f => f.Name.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase)))
                    {
                        byte[] w = FFXDLSE.Read(f.Bytes).Write();
                        if (w.AsSpan().SequenceEqual(f.Bytes)) same++;
                        else { diff++; if (diff <= 5) Console.WriteLine($"  differs: {bank} {Path.GetFileName(f.Name)} ({f.Bytes.Length} → {w.Length} bytes)"); }
                    }
                Console.WriteLine($"FFXDLSE read → write: {same} byte-identical, {diff} different");
                return diff == 0 ? 0 : 2;
            }
            case "pl2":
                foreach (var (id, (fx, where)) in Ds2All().OrderBy(kv => kv.Key))
                {
                    if (fx.Effect.ParamList2.Params.Count == 0) continue;
                    var st0 = fx.Effect.StateMap.States.FirstOrDefault();
                    string acts = st0 == null ? "" : string.Join(" ", st0.Actions.Select(a => $"{a.ID}[{string.Join(",", a.ParamList.Params.Select(ParamValue))}]"));
                    Console.WriteLine($"{id}: PL2 [{string.Join(" ", fx.Effect.ParamList2.Params.Select(ParamValue))}] state0 {acts} triggers {string.Join(",", st0?.Triggers.Select(t => $"{t.StateIndex}:{t.Evaluator?.GetType().Name}") ?? [])}");
                }
                return 0;
            case "texstat": return TexStat(args.Skip(2).ToArray());
            case "stats": return Stats(int.Parse(args[2]), args.Length > 3 ? args[3] : "CommonEffects", args.Length > 4 ? args[4] : null);
            case "ground": return Ground(args[2], args.Skip(3).ToArray());
            case "sfxparam": return SfxParam(args.Skip(2).Select(int.Parse).ToArray());
            case "pairs": return Pairs(args.Length > 2 ? args[2] : "CommonEffects");
            case "mtb-reads": return MtbReads(args.Skip(2).Select(int.Parse).ToArray());
            // s9-10 crash fix: DS2 effect ids (rule B2b-id) and the runtime checks R1–R10 (B2bCheck)
            case "ids": return B2bCheck.Ids();
            case "b2b-check": return B2bCheck.Run(args[2]);
            case "pack-cmp": return B2bCheck.PackCmp(args[2], args[3]);
            case "compile71": return B2bCheck.Compile71Survey(); // fix pass 3: DS2's 71 compile FUN_14100c880 = DSR's ActionData71
            case "fx-diff": return FxDiff(args[2], args[3], args.Skip(4).Select(int.Parse).ToArray()); // fix pass 3: fx-diff BANK_A BANK_B ID [ID_B]
            case "density": return B2bCheck.Density(args.Length > 2 ? args[2] : "m10_02_00_00", args.Length > 3 ? int.Parse(args[3]) : 100201, args.Length > 4 ? int.Parse(args[4]) : 3011);
            case "dsr-alpha":
            {
                // fix pass: DSR's colour-multiplier alpha (DS1R ColorModA, first value) over every action-71 struct of a DS1 binder
                var hist = new SortedDictionary<float, (int N, SortedSet<string> Fx)>();
                int none = 0;
                foreach (var b in args.Skip(2).DefaultIfEmpty("CommonEffects"))
                {
                    var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(Ds1BankPath(b))));
                    foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase)))
                    {
                        FXR1 fxr;
                        try { fxr = FXR1.Read(f.Bytes); } catch { continue; }
                        foreach (var d in fxr.AllFXActionDatas.OfType<FXR1.FXActionData.ClusterAppearanceBillboardActionData71>())
                        {
                            if (d.DS1RData == null) { none++; continue; }
                            float a = d.DS1RData.DS1R_ColorModA switch
                            {
                                FXR1.FXField.ConstFloat c => c.Value,
                                FXR1.FXField.FloatSequence s when s.Ticks.Count > 0 => s.Ticks[0].Value,
                                _ => 1,
                            };
                            a = MathF.Round(a, 3);
                            if (!hist.TryGetValue(a, out var e)) hist[a] = e = (0, new());
                            if (e.Fx.Count < 12) e.Fx.Add($"{b}:{Path.GetFileNameWithoutExtension(f.Name)}");
                            hist[a] = (e.N + 1, e.Fx);
                        }
                    }
                }
                Console.WriteLine($"DSR action-71 ColorModA (first value; Empty = 1): {hist.Values.Sum(v => v.N)} structs with DS1R data, {none} without");
                foreach (var (a, (n, fx)) in hist) Console.WriteLine($"  {a,6:0.###} ×{n}: e.g. {string.Join(" ", fx)}");
                return 0;
            }
        }
        Console.WriteLine("mist-survey banks|actions|ds2 ID…|ds1 BINDER ID…|regions [ID…]|tex ID…");
        return 1;
    }

    // ------------------------------------------------------------------ banks

    public static string SfxDir(string game = Program.DefaultDs2Root) => Path.Combine(game, "sfx");

    public static IBinder ReadBank(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        if (DCX.Is(b)) b = DCX.Decompress(b);
        return BND4.Is(b) ? BND4.Read(b) : BND3.Read(b);
    }

    static int Banks(string game)
    {
        var ids = new Dictionary<string, HashSet<string>>();
        foreach (var path in Directory.GetFiles(SfxDir(game)).Concat(Directory.Exists(Path.Combine(game, "sfx_lq")) ? Directory.GetFiles(Path.Combine(game, "sfx_lq")) : []))
        {
            byte[] raw = File.ReadAllBytes(path);
            bool dcx = DCX.Is(raw);
            byte[] b = dcx ? DCX.Decompress(raw) : raw;
            string fmt = BND4.Is(b) ? "BND4" : BND3.Is(b) ? "BND3" : Encoding.ASCII.GetString(b, 0, Math.Min(4, b.Length));
            Console.WriteLine($"{Path.GetFileName(path)}: {raw.Length:N0} bytes{(dcx ? $" (dcx → {b.Length:N0})" : "")}, {fmt}");
            if (fmt is not ("BND4" or "BND3")) continue;
            IBinder bnd = fmt == "BND4" ? BND4.Read(b) : BND3.Read(b);
            if (bnd is BND4 b4) Console.WriteLine($"  BND4 format {b4.Format} version '{b4.Version}' unicode {b4.Unicode} extended {b4.Extended} unk04 {b4.Unk04} unk05 {b4.Unk05}");
            var exts = bnd.Files.GroupBy(f => Path.GetExtension(f.Name).ToLowerInvariant()).Select(g => $"{g.Key} ×{g.Count()}");
            Console.WriteLine($"  {bnd.Files.Count} files: {string.Join(", ", exts)}; first: {string.Join(" | ", bnd.Files.Take(3).Select(f => $"#{f.ID} {f.Name} flags {f.Flags}"))}");
            Console.WriteLine($"  last: {string.Join(" | ", bnd.Files.TakeLast(2).Select(f => $"#{f.ID} {f.Name} {f.Bytes.Length}"))}");
            var set = bnd.Files.Select(f => Path.GetFileName(f.Name).ToLowerInvariant()).ToHashSet();
            foreach (var (other, oset) in ids)
            {
                int both = set.Intersect(oset).Count();
                if (both > 0) Console.WriteLine($"  {both} entry names also in {other}");
            }
            ids[Path.GetFileName(path)] = set;
            // file-id ranges by extension
            foreach (var g in bnd.Files.GroupBy(f => Path.GetExtension(f.Name).ToLowerInvariant()))
                Console.WriteLine($"  {g.Key}: ids {g.Min(f => f.ID)}..{g.Max(f => f.ID)}, sizes {g.Min(f => f.Bytes.Length)}..{g.Max(f => f.Bytes.Length)}");
        }
        return 0;
    }

    static Dictionary<int, (FFXDLSE Fx, string Where)>? ds2All;

    /// <summary>Every DS2 global-bank effect by id (sfx9999 then _Append; an _Append entry wins, as the game loads it
    /// after the base bank).</summary>
    public static Dictionary<int, (FFXDLSE Fx, string Where)> Ds2All()
    {
        if (ds2All != null) return ds2All;
        ds2All = new();
        foreach (var bank in new[] { "sfx9999.ffxbnd.dcx", "sfx9999_Append.ffxbnd.dcx" })
        {
            var bnd = ReadBank(Path.Combine(SfxDir(), bank));
            foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var fx = FFXDLSE.Read(f.Bytes);
                    ds2All[fx.Effect.ID] = (fx, $"{bank} #{f.ID} {Path.GetFileName(f.Name)}");
                }
                catch (Exception e) { Console.Error.WriteLine($"{bank} {f.Name}: {e.Message}"); }
            }
        }
        return ds2All;
    }

    public static FFXDLSE? Ds2Effect(int id, out string where)
    {
        if (Ds2All().TryGetValue(id, out var hit)) { where = hit.Where; return hit.Fx; }
        where = "not found";
        return null;
    }

    // ------------------------------------------------------------------ action survey

    static int Actions()
    {
        var actions = new SortedDictionary<int, (int N, Dictionary<string, int> Sigs, SortedSet<int> Fx)>();
        var classes = new SortedDictionary<int, (int N, Dictionary<string, int> Sigs, SortedSet<int> Fx)>();
        var tops = new SortedDictionary<int, int>();
        var paramList1 = new Dictionary<string, int>();
        void Add(SortedDictionary<int, (int N, Dictionary<string, int> Sigs, SortedSet<int> Fx)> d, int id, FFXDLSE.ParamList pl, int fx)
        {
            if (!d.TryGetValue(id, out var e)) d[id] = e = (0, new(), new());
            string sig = string.Join(",", pl.Params.Select(p => p.GetType().Name.Replace("Param", "")));
            e.Sigs[sig] = e.Sigs.GetValueOrDefault(sig) + 1;
            if (e.Fx.Count < 12) e.Fx.Add(fx);
            d[id] = (e.N + 1, e.Sigs, e.Fx);
        }
        void Walk(FFXDLSE.Param p, int fx)
        {
            switch (p)
            {
                case FFXDLSE.Param37 c: Add(classes, c.EffectID, c.ParamList, fx); foreach (var q in c.ParamList.Params) Walk(q, fx); break;
                case FFXDLSE.Param38 a: Add(actions, a.ActionID, a.ParamList, fx); foreach (var q in a.ParamList.Params) Walk(q, fx); break;
                case FFXDLSE.Param82 n: Walk(n.Param, fx); break;
                case FFXDLSE.Param84 n: Walk(n.Param, fx); break;
            }
        }
        foreach (var (id, (fx, _)) in Ds2All())
        {
            string s1 = string.Join(",", fx.Effect.ParamList1.Params.Select(ParamValue));
            paramList1[s1] = paramList1.GetValueOrDefault(s1) + 1;
            foreach (var st in fx.Effect.StateMap.States)
                foreach (var a in st.Actions)
                {
                    tops[a.ID] = tops.GetValueOrDefault(a.ID) + 1;
                    foreach (var p in a.ParamList.Params) Walk(p, id);
                }
        }
        Console.WriteLine($"{Ds2All().Count} DS2 effects (sfx9999 + _Append)");
        Console.WriteLine("state actions: " + string.Join(" ", tops.Select(kv => $"{kv.Key}×{kv.Value}")));
        Console.WriteLine("\nEffect ParamList1 (top 15 value sets):");
        foreach (var kv in paramList1.OrderByDescending(kv => kv.Value).Take(15)) Console.WriteLine($"  ×{kv.Value}: {kv.Key}");
        foreach (var (title, d) in new[] { ("effect classes (Param37 ids)", classes), ("actions (Param38 ids)", actions) })
        {
            Console.WriteLine($"\n{title}:");
            foreach (var (id, e) in d)
            {
                Console.WriteLine($"  {id}: ×{e.N} in e.g. {string.Join(" ", e.Fx)}; {e.Sigs.Count} signature(s)");
                foreach (var s in e.Sigs.OrderByDescending(s => s.Value).Take(3)) Console.WriteLine($"      ×{s.Value} [{s.Key.Split(',').Length - (s.Key.Length == 0 ? 1 : 0)}] {s.Key}");
            }
        }
        return 0;
    }

    // ------------------------------------------------------------------ dumps

    static string F(float v) => v.ToString("0.####", Inv);

    static string Col(FFXDLSE.PrimitiveColor c) => $"({F(c.R)},{F(c.G)},{F(c.B)},{F(c.A)})";

    /// <summary>One-line value of a non-container DLsE parameter.</summary>
    public static string ParamValue(FFXDLSE.Param p) => p switch
    {
        FFXDLSE.Param1 x => $"i{x.Int}",
        FFXDLSE.Param2 x => $"i[{string.Join(" ", x.Ints)}]",
        FFXDLSE.Param5 x => $"iseq5[{string.Join(" ", x.TickInts.Select(t => $"{F(t.Tick)}:{t.Int}"))}]",
        FFXDLSE.Param6 x => $"iseq6[{string.Join(" ", x.TickInts.Select(t => $"{F(t.Tick)}:{t.Int}"))}]",
        FFXDLSE.Param7 x => $"f{F(x.Float)}",
        FFXDLSE.Param9 x => $"fseq9[{string.Join(" ", x.TickFloats.Select(t => $"{F(t.Tick)}:{F(t.Float)}"))}]",
        FFXDLSE.Param11 x => $"fseq11[{string.Join(" ", x.TickFloats.Select(t => $"{F(t.Tick)}:{F(t.Float)}"))}]",
        FFXDLSE.Param12 x => $"fseq12[{string.Join(" ", x.TickFloats.Select(t => $"{F(t.Tick)}:{F(t.Float)}"))}]",
        FFXDLSE.Param13 x => $"f3seq13[{string.Join(" ", x.TickFloat3s.Select(t => $"{F(t.Tick)}:({F(t.Float1)},{F(t.Float2)},{F(t.Float3)})"))}]",
        FFXDLSE.Param15 x => $"col{Col(x.Color)}",
        FFXDLSE.Param17 x => $"cseq17[{string.Join(" ", x.TickColors.Select(t => $"{F(t.Tick)}:{Col(t.Color)}"))}]",
        FFXDLSE.Param18 x => $"cseq18[{string.Join(" ", x.TickColors.Select(t => $"{F(t.Tick)}:{Col(t.Color)}"))}]",
        FFXDLSE.Param19 x => $"cseq19[{string.Join(" ", x.TickColors.Select(t => $"{F(t.Tick)}:{Col(t.Color)}"))}]",
        FFXDLSE.Param20 x => $"cseq20[{string.Join(" ", x.TickColors.Select(t => $"{F(t.Tick)}:{Col(t.Color)}"))}]",
        FFXDLSE.Param21 x => $"c3seq21[{x.TickColor3s.Count}]",
        FFXDLSE.Param40 x => $"tex{x.TextureID}",
        FFXDLSE.Param41 x => $"p41({x.Unk04})",
        FFXDLSE.Param44 x => $"arg44({x.Unk04},{x.ArgIndex})",
        FFXDLSE.Param45 x => $"arg45({x.Unk04},{x.ArgIndex})",
        FFXDLSE.Param46 x => $"arg46({x.Unk04},{x.ArgIndex})",
        FFXDLSE.Param47 x => $"arg47({x.Unk04},{x.ArgIndex})",
        FFXDLSE.Param59 x => $"arg59({x.Unk04},{x.ArgIndex})",
        FFXDLSE.Param60 x => $"arg60({x.Unk04},{x.ArgIndex})",
        FFXDLSE.Param66 x => $"arg66({x.Unk04},{x.ArgIndex})",
        FFXDLSE.Param68 x => $"sound{x.SoundID}",
        FFXDLSE.Param69 x => $"p69({x.Unk04})",
        FFXDLSE.Param70 x => $"tick{F(x.Tick)}",
        FFXDLSE.Param71 x => $"arg71({x.Unk04},{x.ArgIndex})",
        FFXDLSE.Param79 x => $"i2({x.Int1},{x.Int2})",
        FFXDLSE.Param81 x => $"f2({F(x.Float1)},{F(x.Float2)})",
        FFXDLSE.Param82 x => $"rnd82({ParamValue(x.Param)}±{F(x.Float)})",
        FFXDLSE.Param83 x => $"c2{Col(x.Color1)}{Col(x.Color2)}",
        FFXDLSE.Param84 x => $"p84({ParamValue(x.Param)},{Col(x.Color)})",
        FFXDLSE.Param85 x => $"t2({F(x.Tick1)},{F(x.Tick2)})",
        FFXDLSE.Param87 x => $"arg87({x.Unk04},{x.ArgIndex})",
        FFXDLSE.Param37 x => $"effect{x.EffectID}",
        FFXDLSE.Param38 x => $"action{x.ActionID}",
        _ => p.GetType().Name,
    };

    public static string Tree(FFXDLSE fx)
    {
        var sb = new StringBuilder();
        var e = fx.Effect;
        sb.AppendLine($"ParamList1 [{e.ParamList1.Unk04}]: {string.Join(" ", e.ParamList1.Params.Select(ParamValue))}");
        sb.AppendLine($"ParamList2 [{e.ParamList2.Unk04}]: {string.Join(" ", e.ParamList2.Params.Select(ParamValue))}");
        for (int s = 0; s < e.StateMap.States.Count; s++)
        {
            var st = e.StateMap.States[s];
            sb.AppendLine($"state {s}: {st.Actions.Count} actions, {st.Triggers.Count} triggers");
            foreach (var a in st.Actions)
            {
                sb.AppendLine($"  action {a.ID} [{a.ParamList.Unk04}]");
                foreach (var (p, i) in a.ParamList.Params.Select((p, i) => (p, i))) TreeParam(sb, p, 2, i);
            }
            foreach (var t in st.Triggers) sb.AppendLine($"  trigger → state {t.StateIndex}: {t.Evaluator?.GetType().Name}");
        }
        var r = e.ResourceSet;
        sb.AppendLine($"resources: v1 [{string.Join(" ", r.Vector1)}] textures [{string.Join(" ", r.Vector2)}] v3 [{string.Join(" ", r.Vector3)}] actions [{string.Join(" ", r.Vector4)}] classes [{string.Join(" ", r.Vector5)}]");
        return sb.ToString();
    }

    static void TreeParam(StringBuilder sb, FFXDLSE.Param p, int depth, int index)
    {
        string pad = new(' ', depth * 2);
        switch (p)
        {
            case FFXDLSE.Param37 c:
                sb.AppendLine($"{pad}{index}: effect {c.EffectID} [{c.ParamList.Unk04}] ({c.ParamList.Params.Count} params)");
                foreach (var (q, i) in c.ParamList.Params.Select((q, i) => (q, i))) TreeParam(sb, q, depth + 1, i);
                break;
            case FFXDLSE.Param38 a:
                bool simple = a.ParamList.Params.All(q => q is not (FFXDLSE.Param37 or FFXDLSE.Param38));
                if (simple)
                    sb.AppendLine($"{pad}{index}: action {a.ActionID} [{a.ParamList.Unk04}] ({a.ParamList.Params.Count}): " +
                        string.Join(" ", a.ParamList.Params.Select((q, i) => $"{i}={ParamValue(q)}")));
                else
                {
                    sb.AppendLine($"{pad}{index}: action {a.ActionID} [{a.ParamList.Unk04}] ({a.ParamList.Params.Count} params)");
                    foreach (var (q, i) in a.ParamList.Params.Select((q, i) => (q, i))) TreeParam(sb, q, depth + 1, i);
                }
                break;
            default:
                sb.AppendLine($"{pad}{index}: {ParamValue(p)}");
                break;
        }
    }

    // ------------------------------------------------------------------ DS1 (DSR FXR1)

    public static FXR1? Ds1Effect(string binder, int id, out string where)
    {
        string path = Path.Combine(Program.DefaultDsrRoot, "sfx", $"FRPG_SfxBnd_{(binder.StartsWith("Common") ? binder : "m" + binder)}.ffxbnd.dcx");
        where = path;
        if (!File.Exists(path)) { where += " (missing)"; return null; }
        var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(path)));
        var f = bnd.Files.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x.Name).Equals($"f{id:D7}", StringComparison.OrdinalIgnoreCase));
        if (f == null) { where += " (no entry)"; return null; }
        where = $"{Path.GetFileName(path)} #{f.ID} {f.Name} {f.Bytes.Length} bytes";
        return FXR1.Read(f.Bytes);
    }

    public static string Ds1Tree(FXR1 fxr)
    {
        var sb = new StringBuilder();
        Ds1Node(sb, fxr.RootFXNode, 0, 0, new HashSet<object>());
        return sb.ToString();
    }

    static void Ds1Node(StringBuilder sb, FXR1.FXNode n, int depth, int index, HashSet<object> seen)
    {
        string pad = new(' ', depth * 2);
        switch (n)
        {
            case FXR1.FXNode.EffectCreateNode133 c:
                sb.AppendLine($"{pad}{index}: create133 effect {c.EffectID} unk {c.Unk}, {c.States.Count} states");
                Ds1Container(sb, "container1", c.Container1, depth + 1, seen);
                Ds1Container(sb, "container2", c.Container2, depth + 1, seen);
                foreach (var (s, i) in c.States.Select((s, i) => (s, i)))
                    sb.AppendLine($"{pad}  state {i}: {s.GetType().Name}");
                break;
            case FXR1.FXNode.EffectCreateNode134 c:
                sb.AppendLine($"{pad}{index}: create134 effect {c.EffectID} unk {c.Unk} ({c.Nodes.Count} nodes)");
                foreach (var (q, i) in c.Nodes.Select((q, i) => (q, i))) Ds1Node(sb, q, depth + 1, i, seen);
                break;
            case FXR1.FXNode.EffectCallNode c:
                sb.AppendLine($"{pad}{index}: effect {c.EffectID} unk {c.Unk}" + (c.Container == null ? "" : $" ({c.Container.FXNodes.Count} params, flags {c.Container.UnkFlag1}/{c.Container.UnkFlag2}/{c.Container.UnkFlag3})"));
                if (c.Container != null) foreach (var (q, i) in c.Container.FXNodes.Select((q, i) => (q, i))) Ds1Node(sb, q, depth + 1, i, seen);
                break;
            case FXR1.FXNode.ActionCallNode a:
                sb.AppendLine($"{pad}{index}: action {a.ActionType} unk {a.Unk} ({a.Container?.FXNodes.Count} params{(a.Container?.ActionData != null ? $", data {a.Container.ActionData.GetType().Name}" : "")}{(a.Container?.Modifier != null ? $", modifier {a.Container.Modifier.GetType().Name}" : "")})");
                if (a.Container != null)
                {
                    foreach (var (q, i) in a.Container.FXNodes.Select((q, i) => (q, i))) Ds1Node(sb, q, depth + 1, i, seen);
                    if (a.Container.ActionData != null) sb.Append(Ds1Fields(a.Container.ActionData, pad + "    "));
                    if (a.Container.Modifier != null) sb.Append(Ds1Fields(a.Container.Modifier, pad + "    "));
                }
                break;
            default:
                sb.AppendLine($"{pad}{index}: {Ds1Value(n)}");
                break;
        }
    }

    static void Ds1Container(StringBuilder sb, string name, FXR1.FXContainer? c, int depth, HashSet<object> seen)
    {
        if (c == null) return;
        string pad = new(' ', depth * 2);
        sb.AppendLine($"{pad}{name}: {c.FXNodes.Count} nodes, flags {c.UnkFlag1}/{c.UnkFlag2}/{c.UnkFlag3}");
        foreach (var (q, i) in c.FXNodes.Select((q, i) => (q, i))) Ds1Node(sb, q, depth + 1, i, seen);
    }

    /// <summary>A DS1 leaf node's value, by reflection (public fields).</summary>
    public static string Ds1Value(object n)
    {
        var t = n.GetType();
        var parts = t.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => f.Name != "XID")
            .Select(f => $"{f.Name}={Ds1Plain(f.GetValue(n))}");
        return $"{t.Name.Replace("Node", "")}({string.Join(" ", parts)})";
    }

    static string Ds1Plain(object? v) => v switch
    {
        null => "null",
        float f => F(f),
        string s => s,
        System.Collections.IEnumerable list => "[" + string.Join(" ", list.Cast<object>().Select(Ds1Plain)) + "]",
        _ when v.GetType().IsPrimitive || v is Enum => v.ToString()!,
        _ => "{" + string.Join(" ", v.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => f.Name != "XID").Select(f => $"{f.Name}:{Ds1Plain(f.GetValue(v))}"))
             + string.Join(" ", v.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0 && p.Name != "XID" && p.Name != "Type" && p.DeclaringType != typeof(FXR1.XIDable)).Select(p => $"{p.Name}:{Ds1Plain(p.GetValue(v))}")) + "}",
    };

    /// <summary>Every public field/property of a DS1 ActionData / Modifier, one per line.</summary>
    public static string Ds1Fields(object data, string pad)
    {
        var sb = new StringBuilder();
        var t = data.GetType();
        foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.Instance))
        {
            object? v = m switch
            {
                FieldInfo f => f.GetValue(data),
                PropertyInfo p when p.GetIndexParameters().Length == 0 && p.CanRead => p.GetValue(data),
                _ => typeof(void),
            };
            if (v is Type || m.Name is "XID" or "ParentContainer" || m is PropertyInfo { Name: "Type" }) continue;
            if (v is FXR1.FXField fld) sb.AppendLine($"{pad}{m.Name} = {FieldText(fld)}");
            else if (v is not Type) sb.AppendLine($"{pad}{m.Name} = {Ds1Plain(v)}");
        }
        return sb.ToString();
    }

    public static string FieldText(FXR1.FXField f) => $"{f.GetType().Name}{Ds1Plain(f)}";

    // ------------------------------------------------------------------ vanilla SFX regions

    static int Regions(int[] ids)
    {
        if (ids.Length == 1 && ids[0] == -1) ids = [3010, 3011, 3012];
        string mapRoot = Path.Combine(ExtractDs2.DefaultOut, "map");
        int n = 0;
        var byMap = new SortedDictionary<string, int>();
        foreach (var msbPath in Directory.GetFiles(mapRoot, "*.msb", SearchOption.AllDirectories).OrderBy(x => x))
        {
            MSB2 msb;
            try { msb = MSB2.Read(msbPath); } catch { continue; }
            string map = Path.GetFileNameWithoutExtension(msbPath);
            byMap[map] = msb.Regions.SFXs.Count;
            foreach (var s in msb.Regions.SFXs.Where(s => ids.Contains(s.EffectID)))
            {
                n++;
                Console.WriteLine($"{map}: EffectID {s.EffectID} UnkT04 {s.UnkT04} Unk08 {s.Unk08} Unk0E {s.Unk0E} shape {s.Shape.GetType().Name} pos {s.Position} rot {s.Rotation} '{s.Name}'");
            }
        }
        Console.WriteLine($"{n} regions; SFX regions per map: {string.Join(" ", byMap.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key[1..6]}:{kv.Value}"))}");
        // all distinct UnkT04 / Unk0E / shapes over every SFX region
        var all = Directory.GetFiles(mapRoot, "*.msb", SearchOption.AllDirectories).SelectMany(p => { try { return MSB2.Read(p).Regions.SFXs; } catch { return []; } }).ToList();
        Console.WriteLine($"all {all.Count} vanilla SFX regions: UnkT04 {{{string.Join(" ", all.GroupBy(s => s.UnkT04).Select(g => $"{g.Key}×{g.Count()}"))}}}, Unk0E {{{string.Join(" ", all.GroupBy(s => s.Unk0E).Select(g => $"{g.Key}×{g.Count()}"))}}}, shapes {{{string.Join(" ", all.GroupBy(s => s.Shape.GetType().Name).Select(g => $"{g.Key}×{g.Count()}"))}}}");
        return 0;
    }

    // ------------------------------------------------------------------ DS1 banks and DS1/DS2 pairs

    public static string Ds1BankPath(string binder) =>
        Path.Combine(Program.DefaultDsrRoot, "sfx", $"FRPG_SfxBnd_{(binder.StartsWith("Common") || binder.StartsWith("Patch") || binder.StartsWith("c") ? binder : "m" + binder)}.ffxbnd.dcx");

    static int Ds1Bank(string binder)
    {
        var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(Ds1BankPath(binder))));
        Console.WriteLine($"{Ds1BankPath(binder)}: {bnd.Files.Count} files; {string.Join(", ", bnd.Files.GroupBy(f => Path.GetExtension(f.Name).ToLowerInvariant()).Select(g => $"{g.Key} ×{g.Count()}"))}");
        foreach (var f in bnd.Files)
        {
            string info = "";
            if (TPF.Is(f.Bytes))
            {
                var tpf = TPF.Read(f.Bytes);
                info = string.Join("; ", tpf.Textures.Select(t => $"{t.Name} fmt {t.Format} type {t.Type} mips {t.Mipmaps} {DdsSize(t.Bytes)}"));
            }
            Console.WriteLine($"  #{f.ID} {f.Name} {f.Bytes.Length} {info}");
        }
        return 0;
    }

    /// <summary>Action ids in depth-first order (DS1).</summary>
    static void Ds1Actions(FXR1.FXNode n, List<int> acts, List<int> fx, HashSet<object> seen)
    {
        if (!seen.Add(n)) return;
        switch (n)
        {
            case FXR1.FXNode.EffectCreateNode133 c:
                foreach (var st in c.States)
                    foreach (var a in st.Actions)
                    {
                        acts.Add(a.ActionType);
                        if (a.Container != null) foreach (var q in a.Container.FXNodes) Ds1Actions(q, acts, fx, seen);
                    }
                foreach (var q in c.Container1.FXNodes.Concat(c.Container2.FXNodes)) Ds1Actions(q, acts, fx, seen);
                break;
            case FXR1.FXNode.EffectCreateNode134 c:
                foreach (var q in c.Nodes) Ds1Actions(q, acts, fx, seen);
                break;
            case FXR1.FXNode.EffectCallNode c:
                fx.Add(c.EffectID);
                if (c.Container != null) foreach (var q in c.Container.FXNodes) Ds1Actions(q, acts, fx, seen);
                break;
            case FXR1.FXNode.ActionCallNode a:
                acts.Add(a.ActionType);
                if (a.Container != null) foreach (var q in a.Container.FXNodes) Ds1Actions(q, acts, fx, seen);
                break;
        }
    }

    static void Ds2Actions(FFXDLSE.Param p, List<int> acts, List<int> fx)
    {
        switch (p)
        {
            case FFXDLSE.Param37 c: fx.Add(c.EffectID); foreach (var q in c.ParamList.Params) Ds2Actions(q, acts, fx); break;
            case FFXDLSE.Param38 a: acts.Add(a.ActionID); foreach (var q in a.ParamList.Params) Ds2Actions(q, acts, fx); break;
            case FFXDLSE.Param82 n: Ds2Actions(n.Param, acts, fx); break;
            case FFXDLSE.Param84 n: Ds2Actions(n.Param, acts, fx); break;
        }
    }

    public static List<int> Ds2ActionList(FFXDLSE fx, out List<int> classes)
    {
        var acts = new List<int>(); classes = new List<int>();
        foreach (var st in fx.Effect.StateMap.States)
            foreach (var a in st.Actions)
            {
                acts.Add(a.ID);
                foreach (var p in a.ParamList.Params) Ds2Actions(p, acts, classes);
            }
        return acts;
    }

    /// <summary>DS1 effects whose id also exists in DS2's global banks: same action sequence? (empirical field mapping)</summary>
    static int Pairs(string binder)
    {
        var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(Ds1BankPath(binder))));
        int same = 0, both = 0;
        var ds1Acts = new Dictionary<string, int>();
        foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase)))
        {
            FXR1 fxr;
            try { fxr = FXR1.Read(f.Bytes); } catch { continue; }
            int id = int.Parse(Path.GetFileNameWithoutExtension(f.Name)[1..]);
            var acts = new List<int>(); var cls = new List<int>();
            Ds1Actions(fxr.RootFXNode, acts, cls, new HashSet<object>(ReferenceEqualityComparer.Instance));
            foreach (var a in acts.Distinct()) ds1Acts[a.ToString()] = ds1Acts.GetValueOrDefault(a.ToString()) + 1;
            var ds2 = Ds2Effect(id, out _);
            if (ds2 == null) continue;
            both++;
            var a2 = Ds2ActionList(ds2, out var cls2);
            // DS2's state actions (79/5…) are extra: compare the Param38 actions only
            var p2 = a2.Where(x => x != 0).ToList(); var p1 = acts.Where(x => x != 0).ToList();
            bool eq = p1.Count > 0 && p1.SequenceEqual(p2.Skip(Math.Max(0, p2.Count - p1.Count)));
            if (eq) same++;
            if (eq || both <= 40)
                Console.WriteLine($"{id}: {(eq ? "SAME" : "diff")} DS1 [{string.Join(" ", p1)}] classes [{string.Join(" ", cls.Distinct())}] | DS2 [{string.Join(" ", p2)}] classes [{string.Join(" ", cls2.Distinct())}]");
        }
        Console.WriteLine($"{both} ids in both, {same} with the same action sequence");
        Console.WriteLine($"DS1 {binder} action usage (effects): {string.Join(" ", ds1Acts.OrderBy(kv => int.Parse(kv.Key)).Select(kv => $"{kv.Key}×{kv.Value}"))}");
        return 0;
    }

    // ------------------------------------------------------------------ per-position value statistics of one action

    /// <summary>stats ACTION [DS1BINDER] — DS2: value histogram per parameter position of every use of the action;
    /// DS1: value histogram per ActionData field (or per FXNode position) of every use in the DS1 binder.</summary>
    static int Stats(int action, string binder, string? only = null)
    {
        // fix pass: "stats ACTION BINDER pN" — every distinct DS2 value at parameter N (no top-7 cut), with counts
        int onlyPos = only != null && only.StartsWith('p') && int.TryParse(only[1..], out int op) ? op : -1;
        var pos = new SortedDictionary<int, Dictionary<string, int>>();
        int uses = 0;
        void Walk(FFXDLSE.Param p)
        {
            switch (p)
            {
                case FFXDLSE.Param37 c: foreach (var q in c.ParamList.Params) Walk(q); break;
                case FFXDLSE.Param38 a:
                    if (a.ActionID == action)
                    {
                        uses++;
                        for (int i = 0; i < a.ParamList.Params.Count; i++)
                        {
                            if (!pos.TryGetValue(i, out var d)) pos[i] = d = new();
                            string v = ParamValue(a.ParamList.Params[i]);
                            if (v.Length > 60) v = v[..60] + "…";
                            d[v] = d.GetValueOrDefault(v) + 1;
                        }
                    }
                    foreach (var q in a.ParamList.Params) Walk(q);
                    break;
                case FFXDLSE.Param82 n: Walk(n.Param); break;
                case FFXDLSE.Param84 n: Walk(n.Param); break;
            }
        }
        foreach (var (_, (fx, _)) in Ds2All())
            foreach (var st in fx.Effect.StateMap.States)
                foreach (var a in st.Actions)
                    foreach (var p in a.ParamList.Params) Walk(p);
        Console.WriteLine($"DS2 action {action}: {uses} uses");
        if (onlyPos >= 0)
        {
            if (pos.TryGetValue(onlyPos, out var all))
                foreach (var kv in all.OrderByDescending(kv => kv.Value)) Console.WriteLine($"  p{onlyPos}: {kv.Key} ×{kv.Value}");
            return 0;
        }
        foreach (var (i, d) in pos)
            Console.WriteLine($"  p{i}: {string.Join("  ", d.OrderByDescending(kv => kv.Value).Take(7).Select(kv => $"{kv.Key}×{kv.Value}"))}{(d.Count > 7 ? $"  (+{d.Count - 7} more)" : "")}");

        // DS1
        var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(Ds1BankPath(binder))));
        var fields = new Dictionary<string, Dictionary<string, int>>();
        var order = new List<string>();
        int ds1Uses = 0;
        void Ds1Walk(FXR1.FXNode n, HashSet<object> seen)
        {
            if (!seen.Add(n)) return;
            switch (n)
            {
                case FXR1.FXNode.EffectCreateNode133 c:
                    foreach (var st in c.States) foreach (var a in st.Actions) if (a.Container != null) foreach (var q in a.Container.FXNodes) Ds1Walk(q, seen);
                    foreach (var q in c.Container1.FXNodes.Concat(c.Container2.FXNodes)) Ds1Walk(q, seen);
                    break;
                case FXR1.FXNode.EffectCallNode c:
                    if (c.Container != null) foreach (var q in c.Container.FXNodes) Ds1Walk(q, seen);
                    break;
                case FXR1.FXNode.ActionCallNode a:
                    if (a.ActionType == action && a.Container != null)
                    {
                        ds1Uses++;
                        void Add(string k, string v) { if (!fields.TryGetValue(k, out var d)) { fields[k] = d = new(); order.Add(k); } if (v.Length > 60) v = v[..60] + "…"; d[v] = d.GetValueOrDefault(v) + 1; }
                        for (int i = 0; i < a.Container.FXNodes.Count; i++) Add($"node{i}", Ds1Value(a.Container.FXNodes[i]));
                        if (a.Container.ActionData != null)
                            foreach (var line in Ds1Fields(a.Container.ActionData, "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                            {
                                int eq = line.IndexOf(" = ");
                                if (eq > 0) Add(line[..eq].Trim(), line[(eq + 3)..].Trim());
                            }
                    }
                    if (a.Container != null) foreach (var q in a.Container.FXNodes) Ds1Walk(q, seen);
                    break;
            }
        }
        foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase)))
        {
            try { Ds1Walk(FXR1.Read(f.Bytes).RootFXNode, new HashSet<object>(ReferenceEqualityComparer.Instance)); } catch { }
        }
        Console.WriteLine($"\nDS1 {binder} action {action}: {ds1Uses} uses");
        foreach (var k in order)
        {
            var d = fields[k];
            Console.WriteLine($"  {k}: {string.Join("  ", d.OrderByDescending(kv => kv.Value).Take(7).Select(kv => $"{kv.Key}×{kv.Value}"))}{(d.Count > 7 ? $"  (+{d.Count - 7} more)" : "")}");
        }
        return 0;
    }

    // ------------------------------------------------------------------ navmesh ground height under points

    /// <summary>ground NGP|MSB [x,y,z …] — navmesh heights under each point (all faces whose XZ triangle holds it);
    /// with an MSB path and no points, every SFX region of that MSB (its NGP next to it).</summary>
    static int Ground(string file, string[] pts)
    {
        string ngpPath = Path.ChangeExtension(file, ".ngp");
        var points = new List<(string Name, System.Numerics.Vector3 P)>();
        if (file.EndsWith(".msb", StringComparison.OrdinalIgnoreCase) && pts.Length == 0)
            foreach (var s in MSB2.Read(file).Regions.SFXs) points.Add(($"{s.Name} (fx {s.EffectID})", s.Position));
        foreach (var p in pts)
        {
            var v = p.Split(',').Select(x => float.Parse(x, Inv)).ToArray();
            points.Add((p, new(v[0], v.Length > 2 ? v[1] : float.NaN, v[^1])));
        }
        var ngp = NGP.Read(ngpPath);
        foreach (var (name, p) in points)
        {
            var hs = new List<float>();
            foreach (var m in ngp.Meshes)
                foreach (var f in m.Faces)
                {
                    var a = m.Vertices[f.V1]; var b = m.Vertices[f.V2]; var c = m.Vertices[f.V3];
                    float d = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
                    if (Math.Abs(d) < 1e-9) continue;
                    float l1 = ((b.Z - c.Z) * (p.X - c.X) + (c.X - b.X) * (p.Z - c.Z)) / d;
                    float l2 = ((c.Z - a.Z) * (p.X - c.X) + (a.X - c.X) * (p.Z - c.Z)) / d;
                    float l3 = 1 - l1 - l2;
                    if (l1 < -1e-4 || l2 < -1e-4 || l3 < -1e-4) continue;
                    hs.Add(l1 * a.Y + l2 * b.Y + l3 * c.Y);
                }
            string near = "";
            if (hs.Count == 0)
            {
                float best = float.MaxValue; System.Numerics.Vector3 bv = default;
                foreach (var m in ngp.Meshes) foreach (var v in m.Vertices) { float dd = MathF.Sqrt((v.X - p.X) * (v.X - p.X) + (v.Z - p.Z) * (v.Z - p.Z)); if (dd < best) { best = dd; bv = v; } }
                near = $" (no face under it; nearest vertex {best:0.##} m away at y {bv.Y:0.##})";
            }
            string rel = float.IsNaN(p.Y) || hs.Count == 0 ? "" : $" → point is {p.Y - hs.Where(h => h <= p.Y + 3).DefaultIfEmpty(hs.Min()).Max():+0.##;-0.##} m above the floor below it";
            Console.WriteLine($"{name}: y {p.Y:0.##}; navmesh heights [{string.Join(" ", hs.Select(h => h.ToString("0.##", Inv)))}]{rel}{near}");
        }
        return 0;
    }

    // ------------------------------------------------------------------ sfxParameter.sfxparam

    /// <summary>sfxParameter.sfxparam ("sfxp" v5): header 32 bytes (magic, version, count, header size), then 64-byte
    /// records = effect id + 60 bytes of per-effect settings (compared here with the effect's DLsE ParamList1).</summary>
    public static List<(int Id, byte[] Raw)> ReadSfxParam(byte[] b)
    {
        int count = BitConverter.ToInt32(b, 8), hdr = BitConverter.ToInt32(b, 12);
        var list = new List<(int, byte[])>();
        for (int i = 0; i < count; i++)
        {
            int at = hdr + i * 64;
            if (at + 64 > b.Length) break;
            list.Add((BitConverter.ToInt32(b, at), b[at..(at + 64)]));
        }
        return list;
    }

    static string RecordText(byte[] r)
    {
        var sb = new StringBuilder();
        for (int o = 4; o < 64; o += 4)
        {
            int i = BitConverter.ToInt32(r, o); float f = BitConverter.ToSingle(r, o);
            sb.Append(o == 4 || o >= 0x20 ? F(f) : (r[o + 1] | r[o + 2] | r[o + 3]) != 0 && i > 0xFFFF ? $"[{r[o]} {r[o + 1]} {r[o + 2]} {r[o + 3]}]" : i.ToString());
            sb.Append(' ');
        }
        return sb.ToString();
    }

    static int SfxParam(int[] ids)
    {
        foreach (var bank in new[] { "sfx9999.ffxbnd.dcx", "sfx9999_Append.ffxbnd.dcx" })
        {
            var bnd = ReadBank(Path.Combine(SfxDir(), bank));
            foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".sfxparam", StringComparison.OrdinalIgnoreCase)))
            {
                var b = f.Bytes;
                Console.WriteLine($"{bank} {Path.GetFileName(f.Name)}: magic {Encoding.ASCII.GetString(b, 0, 4)} v{BitConverter.ToInt32(b, 4)} count {BitConverter.ToInt32(b, 8)} header {BitConverter.ToInt32(b, 12)} size {b.Length} (= header + count × {(b.Length - BitConverter.ToInt32(b, 12)) / (double)Math.Max(1, BitConverter.ToInt32(b, 8)):0.##})");
                if (!f.Name.Contains("sfxParameter")) continue;
                var recs = ReadSfxParam(b);
                Console.WriteLine($"  ids {recs.Min(r => r.Id)}..{recs.Max(r => r.Id)}, sorted {recs.Select(r => r.Id).SequenceEqual(recs.Select(r => r.Id).Order())}, effects in this bank without a record: {bnd.Files.Count(x => x.Name.EndsWith(".ffx") && !recs.Any(r => $"f{r.Id:D7}.ffx".Equals(Path.GetFileName(x.Name), StringComparison.OrdinalIgnoreCase)))}, records without an effect here: {recs.Count(r => !bnd.Files.Any(x => Path.GetFileName(x.Name).Equals($"f{r.Id:D7}.ffx", StringComparison.OrdinalIgnoreCase)))}");
                // compare records with the ParamList1 of the effect (same bank)
                int shown = 0, match = 0, mism = 0;
                foreach (var (id, raw) in recs)
                {
                    var fx = bnd.Files.FirstOrDefault(x => Path.GetFileName(x.Name).Equals($"f{id:D7}.ffx", StringComparison.OrdinalIgnoreCase));
                    string pl = "";
                    if (fx != null)
                    {
                        var e = FFXDLSE.Read(fx.Bytes);
                        pl = string.Join(" ", e.Effect.ParamList1.Params.Select(ParamValue));
                        float p0 = e.Effect.ParamList1.Params.Count > 0 && e.Effect.ParamList1.Params[0] is FFXDLSE.Param7 p7 ? p7.Float : float.NaN;
                        if (p0 == BitConverter.ToSingle(raw, 4)) match++; else mism++;
                    }
                    if (ids.Contains(id) || shown++ < 6)
                        Console.WriteLine($"  {id}: {RecordText(raw)}| PL1 {pl}");
                }
                Console.WriteLine($"  record float@4 == ParamList1[0]: {match} equal, {mism} differ");
                // distinct value sets per 4-byte column
                for (int o = 4; o < 64; o += 4)
                {
                    var vals = recs.Select(r => o == 4 || o >= 0x20 ? F(BitConverter.ToSingle(r.Raw, o)) : BitConverter.ToInt32(r.Raw, o).ToString("X8")).GroupBy(v => v).OrderByDescending(g => g.Count()).Take(8);
                    Console.WriteLine($"  +0x{o:X2}: {string.Join(" ", vals.Select(g => $"{g.Key}×{g.Count()}"))}");
                }
            }
        }
        return 0;
    }

    // ------------------------------------------------------------------ rule B2b checks

    /// <summary>Every (action id → parameter type signatures) and (class id → signatures) DS2's own banks use.</summary>
    static (Dictionary<int, HashSet<string>> Actions, Dictionary<int, HashSet<string>> Classes) Ds2Signatures()
    {
        var acts = new Dictionary<int, HashSet<string>>();
        var cls = new Dictionary<int, HashSet<string>>();
        void Walk(FFXDLSE.Param p)
        {
            switch (p)
            {
                case FFXDLSE.Param37 c:
                    if (!cls.TryGetValue(c.EffectID, out var s1)) cls[c.EffectID] = s1 = new();
                    s1.Add(Sig(c.ParamList));
                    foreach (var q in c.ParamList.Params) Walk(q);
                    break;
                case FFXDLSE.Param38 a:
                    if (!acts.TryGetValue(a.ActionID, out var s2)) acts[a.ActionID] = s2 = new();
                    s2.Add(Sig(a.ParamList));
                    foreach (var q in a.ParamList.Params) Walk(q);
                    break;
                case FFXDLSE.Param82 n: Walk(n.Param); break;
                case FFXDLSE.Param84 n: Walk(n.Param); break;
            }
        }
        foreach (var (_, (fx, _)) in Ds2All())
            foreach (var st in fx.Effect.StateMap.States)
                foreach (var a in st.Actions)
                {
                    if (!acts.TryGetValue(-a.ID, out var s)) acts[-a.ID] = s = new(); // state actions: negative keys
                    s.Add(Sig(a.ParamList));
                    foreach (var p in a.ParamList.Params) Walk(p);
                }
        return (acts, cls);
    }

    static string Sig(FFXDLSE.ParamList l) => $"{l.Unk04}:" + string.Join(",", l.Params.Select(p => p.GetType().Name.Replace("Param", "")));

    /// <summary>Checks a converted effect against DS2: every action/class call has a parameter signature DS2 itself uses
    /// (type ids, count and the list's main count), state actions exist in DS2, textures listed, write → read → write identical.</summary>
    public static List<string> CheckEffectPublic(FFXDLSE fx) => CheckEffect(fx);

    static List<string> CheckEffect(FFXDLSE fx)
    {
        var (acts, cls) = Ds2Signatures();
        var problems = new List<string>();
        void Walk(FFXDLSE.Param p, string path)
        {
            switch (p)
            {
                case FFXDLSE.Param37 c:
                    if (c.EffectID != 0 && (!cls.TryGetValue(c.EffectID, out var s1) || !s1.Contains(Sig(c.ParamList))))
                        problems.Add($"{path}: class {c.EffectID} signature {Sig(c.ParamList)} not used by DS2 ({(cls.TryGetValue(c.EffectID, out var k1) ? string.Join(" | ", k1.Take(2)) : "class unknown")})");
                    for (int i = 0; i < c.ParamList.Params.Count; i++) Walk(c.ParamList.Params[i], $"{path}/{c.EffectID}[{i}]");
                    break;
                case FFXDLSE.Param38 a:
                    if (a.ActionID != 0 && (!acts.TryGetValue(a.ActionID, out var s2) || !s2.Contains(Sig(a.ParamList))))
                        problems.Add($"{path}: action {a.ActionID} signature {Sig(a.ParamList)} not used by DS2 ({(acts.TryGetValue(a.ActionID, out var k2) ? string.Join(" | ", k2.Take(2)) : "action unknown")})");
                    for (int i = 0; i < a.ParamList.Params.Count; i++) Walk(a.ParamList.Params[i], $"{path}/a{a.ActionID}[{i}]");
                    break;
            }
        }
        for (int s = 0; s < fx.Effect.StateMap.States.Count; s++)
            foreach (var a in fx.Effect.StateMap.States[s].Actions)
            {
                if (!acts.TryGetValue(-a.ID, out var ss) || !ss.Contains(Sig(a.ParamList)))
                    problems.Add($"state {s} action {a.ID} signature {Sig(a.ParamList)} not used by DS2 ({(acts.TryGetValue(-a.ID, out var k) ? string.Join(" | ", k.Take(3)) : "unknown")})");
                foreach (var (p, i) in a.ParamList.Params.Select((p, i) => (p, i))) Walk(p, $"state{s}/a{a.ID}[{i}]");
            }
        byte[] w1 = fx.Write();
        byte[] w2 = FFXDLSE.Read(w1).Write();
        if (!w1.AsSpan().SequenceEqual(w2)) problems.Add("write → read → write differs");
        return problems;
    }

    /// <summary>convert DS1MAP ID… — rule B2b on its own: the converted DS2 effect tree, the conversion notes and the checks.</summary>
    static int ConvertCheck(string ds1Map, int[] ids)
    {
        if (!ds1Map.StartsWith('m')) ds1Map = "m" + ds1Map;
        if (ds1Map.Length < 12) ds1Map += "_00_00";
        int bad = 0;
        foreach (int id in ids)
        {
            var log = new List<string>();
            var c = Build.Maps.FfxPorter.Convert(Program.DefaultDsrRoot, ds1Map, id, log);
            foreach (var l in log) Console.WriteLine(l);
            if (c == null) { bad++; continue; }
            Console.Write(Tree(c.Fx));
            var problems = CheckEffect(c.Fx);
            Console.WriteLine(problems.Count == 0 ? $"CHECK OK: {id}: every action/class signature is one DS2 uses; {c.Fx.Write().Length} bytes; write→read→write identical" : $"CHECK {problems.Count} problem(s):");
            foreach (var p in problems) Console.WriteLine("  " + p);
            bad += problems.Count;
        }
        return bad == 0 ? 0 : 2;
    }

    /// <summary>bank-check PACK — the pack's title:/sfx banks against vanilla: every vanilla entry byte-identical (after
    /// decompression), the added effects parse and pass <see cref="CheckEffect"/>, sfxParameter/sfxDebugParameter sorted with a
    /// record per added effect, res_Append textures decode.</summary>
    /// <summary>fx-diff BANK_A BANK_B ID [ID_B] (fix pass 3): parameter-level differences between effect ID in bank A and
    /// effect ID_B (default ID) in bank B (a .ffxbnd[.dcx] or a pack folder: its title\sfx\sfx9999_Append.ffxbnd.dcx),
    /// walking both trees in parallel (path, parameter index, A → B).</summary>
    static int FxDiff(string a, string b, int[] ids)
    {
        FFXDLSE Load(string bank, int id)
        {
            if (Directory.Exists(bank)) bank = Path.Combine(bank, "title", "sfx", "sfx9999_Append.ffxbnd.dcx");
            var f = ReadBank(bank).Files.First(x => Path.GetFileName(x.Name).Equals($"f{id:D7}.ffx", StringComparison.OrdinalIgnoreCase));
            return FFXDLSE.Read(f.Bytes);
        }
        int idA = ids[0], idB = ids.Length > 1 ? ids[1] : ids[0];
        var fa = Load(a, idA);
        var fb = Load(b, idB);
        int n = 0;
        void Cmp(FFXDLSE.ParamList la, FFXDLSE.ParamList lb, string path)
        {
            if (la.Params.Count != lb.Params.Count || la.Unk04 != lb.Unk04) { Console.WriteLine($"  {path}: list {la.Unk04}/{la.Params.Count} → {lb.Unk04}/{lb.Params.Count}"); n++; return; }
            for (int i = 0; i < la.Params.Count; i++)
            {
                var (pa, pb) = (la.Params[i], lb.Params[i]);
                if (pa is FFXDLSE.Param37 ca && pb is FFXDLSE.Param37 cb && ca.EffectID == cb.EffectID) Cmp(ca.ParamList, cb.ParamList, $"{path}/{ca.EffectID}[{i}]");
                else if (pa is FFXDLSE.Param38 aa && pb is FFXDLSE.Param38 ab && aa.ActionID == ab.ActionID) Cmp(aa.ParamList, ab.ParamList, $"{path}/a{aa.ActionID}[{i}]");
                else if (ParamValue(pa) != ParamValue(pb)) { Console.WriteLine($"  {path} p{i}: {ParamValue(pa)} → {ParamValue(pb)}"); n++; }
            }
        }
        if (fa.Effect.ID != fb.Effect.ID) { Console.WriteLine($"  effect id: {fa.Effect.ID} → {fb.Effect.ID}"); n++; }
        Cmp(fa.Effect.ParamList1, fb.Effect.ParamList1, "ParamList1");
        for (int s = 0; s < Math.Min(fa.Effect.StateMap.States.Count, fb.Effect.StateMap.States.Count); s++)
            for (int k = 0; k < Math.Min(fa.Effect.StateMap.States[s].Actions.Count, fb.Effect.StateMap.States[s].Actions.Count); k++)
                Cmp(fa.Effect.StateMap.States[s].Actions[k].ParamList, fb.Effect.StateMap.States[s].Actions[k].ParamList, $"state{s}/a{fa.Effect.StateMap.States[s].Actions[k].ID}");
        string ra = string.Join(" ", fa.Effect.ResourceSet.Vector2) + " | " + string.Join(" ", fa.Effect.ResourceSet.Vector4) + " | " + string.Join(" ", fa.Effect.ResourceSet.Vector5);
        string rb = string.Join(" ", fb.Effect.ResourceSet.Vector2) + " | " + string.Join(" ", fb.Effect.ResourceSet.Vector4) + " | " + string.Join(" ", fb.Effect.ResourceSet.Vector5);
        if (ra != rb) { Console.WriteLine($"  resources: {ra} → {rb}"); n++; }
        Console.WriteLine($"fx-diff f{idA:D7} ({a}) → f{idB:D7} ({b}): {n} difference(s); sizes {fa.Write().Length} → {fb.Write().Length} bytes");
        return 0;
    }

    static int BankCheck(string pack)
    {
        int problems = 0;
        string ap = Path.Combine(pack, "title", "sfx", "sfx9999_Append.ffxbnd.dcx");
        if (!File.Exists(ap)) { Console.WriteLine($"{ap}: not in the pack (no converted effects)"); return 0; }
        var vanilla = ReadBank(Path.Combine(SfxDir(), "sfx9999_Append.ffxbnd.dcx"));
        byte[] packRaw = File.ReadAllBytes(ap);
        var mine = BND4.Read(DCX.Decompress(packRaw, out DCX.CompressionInfo ci));
        var vci = DCX.Decompress(File.ReadAllBytes(Path.Combine(SfxDir(), "sfx9999_Append.ffxbnd.dcx")), out DCX.CompressionInfo vinfo);
        Console.WriteLine($"{ap}: {packRaw.Length:N0} bytes, DCX {ci.GetType().Name} (vanilla {vinfo.GetType().Name}), {mine.Files.Count} files (vanilla {vanilla.Files.Count})");
        var byName = mine.Files.GroupBy(f => $"{f.ID}|{f.Name}", StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var dupNames = vanilla.Files.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => $"{Path.GetFileName(g.Key)} ×{g.Count()} (ids {string.Join("/", g.Select(x => x.ID))})").ToList();
        if (dupNames.Count > 0) Console.WriteLine($"  vanilla has duplicate entry names: {string.Join(", ", dupNames)}");
        int same = 0;
        foreach (var v in vanilla.Files)
        {
            if (!byName.TryGetValue($"{v.ID}|{v.Name}", out var m)) { Console.WriteLine($"  MISSING vanilla {v.Name}"); problems++; continue; }
            if (v.Name.EndsWith(".sfxparam", StringComparison.OrdinalIgnoreCase)) continue;
            if (m.ID != v.ID || m.Flags != v.Flags || !m.Bytes.AsSpan().SequenceEqual(v.Bytes)) { Console.WriteLine($"  CHANGED vanilla {v.Name}"); problems++; }
            else same++;
        }
        Console.WriteLine($"  vanilla entries identical (id, flags, bytes): {same} of {vanilla.Files.Count(f => !f.Name.EndsWith(".sfxparam"))}");
        var added = mine.Files.Where(f => !vanilla.Files.Any(v => v.ID == f.ID && v.Name.Equals(f.Name, StringComparison.OrdinalIgnoreCase))).ToList();
        var ids = new List<int>();
        foreach (var f in added)
        {
            var fx = FFXDLSE.Read(f.Bytes);
            ids.Add(fx.Effect.ID);
            var p = CheckEffect(fx);
            Console.WriteLine($"  added #{f.ID} {Path.GetFileName(f.Name)} effect {fx.Effect.ID} flags {f.Flags} {f.Bytes.Length} bytes: {(p.Count == 0 ? "signatures OK" : $"{p.Count} problem(s)")}; textures [{string.Join(" ", fx.Effect.ResourceSet.Vector2)}]");
            foreach (var x in p) Console.WriteLine("    " + x);
            problems += p.Count;
            if (Path.GetFileName(f.Name) != $"f{fx.Effect.ID:D7}.ffx") { Console.WriteLine("    name/id mismatch"); problems++; }
        }
        if (mine.Files.Select(f => f.ID).Distinct().Count() != mine.Files.Count) { Console.WriteLine("  duplicate BND ids"); problems++; }
        foreach (var (name, size) in new[] { ("sfxParameter.sfxparam", 64), ("sfxDebugParameter.sfxparam", 8) })
        {
            var t = mine.Files.First(f => f.Name.EndsWith(name, StringComparison.OrdinalIgnoreCase)).Bytes;
            var tv = vanilla.Files.First(f => f.Name.EndsWith(name, StringComparison.OrdinalIgnoreCase)).Bytes;
            int count = BitConverter.ToInt32(t, 8), hdr = BitConverter.ToInt32(t, 12);
            var recIds = Enumerable.Range(0, count).Select(i => BitConverter.ToInt32(t, hdr + i * size)).ToList();
            bool sorted = recIds.SequenceEqual(recIds.Order());
            var vRecs = Enumerable.Range(0, BitConverter.ToInt32(tv, 8)).Select(i => tv.AsSpan(hdr + i * size, size).ToArray()).ToList();
            int vKept = vRecs.Count(r => Enumerable.Range(0, count).Any(i => t.AsSpan(hdr + i * size, size).SequenceEqual(r)));
            bool all = ids.All(recIds.Contains);
            Console.WriteLine($"  {name}: {count} records (vanilla {vRecs.Count}), sorted {sorted}, vanilla records unchanged {vKept}/{vRecs.Count}, added effects present {all}, size {t.Length} = {hdr} + {count}×{size}: {t.Length == hdr + count * size}");
            if (!sorted || !all || vKept != vRecs.Count || t.Length != hdr + count * size) problems++;
            foreach (int id in ids)
            {
                int i = recIds.IndexOf(id);
                if (i >= 0 && size == 64) Console.WriteLine($"    record {id}: {RecordText(t[(hdr + i * 64)..(hdr + i * 64 + 64)])}");
            }
        }
        foreach (var (folder, name) in new[] { ("sfx", "sfx9999res_Append.ffxbnd"), ("sfx_lq", "sfx9999lqres_Append.ffxbnd") })
        {
            string rp = Path.Combine(pack, "title", folder, name);
            if (!File.Exists(rp)) { Console.WriteLine($"  {rp}: missing"); problems++; continue; }
            var res = BND4.Read(File.ReadAllBytes(rp));
            var vres = BND4.Read(File.ReadAllBytes(Path.Combine(Program.DefaultDs2Root, folder, name)));
            Console.WriteLine($"  {folder}/{name}: {res.Files.Count} files (vanilla {vres.Files.Count}); header {res.Version}/{res.Format}/unicode {res.Unicode} (vanilla {vres.Version}/{vres.Format}/{vres.Unicode})");
            foreach (var f in res.Files)
            {
                var tpf = TPF.Read(f.Bytes);
                var tx = tpf.Textures[0];
                var dds = Textures.Dds.Read(tx.Bytes);
                var img = dds.DecodeTop();
                double a = img.Pixels.Average(px => px.A) / 255;
                Console.WriteLine($"    #{f.ID} {Path.GetFileName(f.Name)} flags {f.Flags} compression {f.CompressionInfo?.GetType().Name} (vanilla res entries: {BND4.Read(File.ReadAllBytes(Path.Combine(Program.DefaultDs2Root, "sfx", "sfx9999res.ffxbnd"))).Files[0].CompressionInfo?.GetType().Name}): TPF {tpf.Platform}/{tpf.Encoding}/{tpf.Flag2}, '{tx.Name}' format {tx.Format} {dds.Width}×{dds.Height} {dds.Format} mips {dds.Mips.Count}, mean alpha {a:0.000}");
            }
        }
        Console.WriteLine(problems == 0 ? "BANK CHECK OK" : $"BANK CHECK: {problems} problem(s)");
        return problems == 0 ? 0 : 2;
    }

    // ------------------------------------------------------------------ action 15001: the parameters DS2's compile reads

    /// <summary>mtb-reads ID… (fix pass) — every action-15001 use in the given DS2 effects, side by side, restricted to the
    /// parameters DS2's multi-texture-billboard compile reads (FUN_140c04350, SfxFxClusterAppearance_MultiTextureBillboard.cpp,
    /// decomp DarkSoulsII.c 2286618–2287470): main p1–p48 (p47/p48 only when the main count is ≥ 48/49; p0 comes from
    /// the caller) and tail [2], [10]–[15], [17], [18], [21], [22] (tail [k] = parameter main + k). Positions where the
    /// uses differ are marked; the unread tail positions are listed separately.</summary>
    static int MtbReads(int[] ids)
    {
        var uses = new List<(string Name, FFXDLSE.ParamList L)>();
        void Walk(FFXDLSE.Param p, int fx)
        {
            switch (p)
            {
                case FFXDLSE.Param37 c: foreach (var q in c.ParamList.Params) Walk(q, fx); break;
                case FFXDLSE.Param38 a:
                    if (a.ActionID == 15001) uses.Add(($"{fx}#{uses.Count(u => u.Name.StartsWith($"{fx}#")) + 1}", a.ParamList));
                    foreach (var q in a.ParamList.Params) Walk(q, fx);
                    break;
            }
        }
        foreach (int id in ids)
        {
            var fx = Ds2Effect(id, out _);
            if (fx == null) { Console.WriteLine($"{id}: not found"); continue; }
            foreach (var st in fx.Effect.StateMap.States) foreach (var a in st.Actions) foreach (var p in a.ParamList.Params) Walk(p, id);
        }
        int[] tailRead = [2, 10, 11, 12, 13, 14, 15, 17, 18, 21, 22];
        Console.WriteLine("15001 uses: " + string.Join("  ", uses.Select(u => $"{u.Name} (main {u.L.Unk04}, total {u.L.Params.Count})")));
        string V(FFXDLSE.ParamList l, int i) => i < l.Params.Count ? ParamValue(l.Params[i]) : "—";
        Console.WriteLine("read by the compile:");
        for (int m = 1; m <= 48; m++)
        {
            var vals = uses.Select(u => m < u.L.Unk04 ? V(u.L, m) : "(not main)").ToList();
            Console.WriteLine($"  {(vals.Distinct().Count() > 1 ? "≠" : " ")} p{m,-3} {string.Join(" | ", vals)}");
        }
        foreach (int k in tailRead)
        {
            var vals = uses.Select(u => $"p{u.L.Unk04 + k}={V(u.L, u.L.Unk04 + k)}").ToList();
            Console.WriteLine($"  {(uses.Select(u => V(u.L, u.L.Unk04 + k)).Distinct().Count() > 1 ? "≠" : " ")} tail[{k}] {string.Join(" | ", vals)}");
        }
        Console.WriteLine("NOT read by the compile (tail positions):");
        int maxTail = uses.Max(u => u.L.Params.Count - u.L.Unk04);
        for (int k = 0; k < maxTail; k++)
        {
            if (tailRead.Contains(k)) continue;
            var vals = uses.Select(u => $"p{u.L.Unk04 + k}={V(u.L, u.L.Unk04 + k)}").ToList();
            Console.WriteLine($"  {(uses.Select(u => V(u.L, u.L.Unk04 + k)).Distinct().Count() > 1 ? "≠" : " ")} tail[{k}] {string.Join(" | ", vals)}");
        }
        return 0;
    }

    // ------------------------------------------------------------------ texture statistics

    /// <summary>texstat ds2:ID | ds1:BINDER:ID … — size, format, mean RGB / alpha, alpha coverage (A ≥ 32) and mean
    /// alpha × luminance (how much a texel brightens an alpha-blended billboard), per texture (and per 8×8 frame cell of
    /// the first row of frames for animated sheets).</summary>
    static int TexStat(string[] specs)
    {
        foreach (var spec in specs)
        {
            var parts = spec.Split(':');
            byte[]? tpfBytes = null;
            if (parts[0] == "ds2")
            {
                foreach (var bank in new[] { "sfx9999res.ffxbnd", "sfx9999res_Append.ffxbnd" })
                {
                    var bnd = ReadBank(Path.Combine(SfxDir(), bank));
                    var f = bnd.Files.FirstOrDefault(x => Path.GetFileName(x.Name).Equals($"s{int.Parse(parts[1]):D6}.tpf", StringComparison.OrdinalIgnoreCase));
                    if (f != null) { tpfBytes = f.Bytes; break; }
                }
            }
            else
            {
                var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(Ds1BankPath(parts[1]))));
                var f = bnd.Files.FirstOrDefault(x => Path.GetFileName(x.Name).Equals($"s{int.Parse(parts[2]):D5}.tpf", StringComparison.OrdinalIgnoreCase));
                tpfBytes = f?.Bytes;
            }
            if (tpfBytes == null) { Console.WriteLine($"{spec}: not found"); continue; }
            var tex = TPF.Read(tpfBytes).Textures[0];
            var img = Textures.Dds.Read(tex.Bytes).DecodeTop();
            double r = 0, g = 0, b = 0, a = 0, al = 0; int cover = 0;
            foreach (var p in img.Pixels)
            {
                r += p.R; g += p.G; b += p.B; a += p.A;
                al += p.A / 255.0 * (0.299 * p.R + 0.587 * p.G + 0.114 * p.B) / 255.0;
                if (p.A >= 32) cover++;
            }
            int n = img.Pixels.Length;
            Console.WriteLine($"{spec}: {tex.Name} {img.Width}×{img.Height} fmt {tex.Format}; mean RGB ({r / n:0},{g / n:0},{b / n:0}) A {a / n:0.0} ({a / n / 255:0.000}); A≥32 on {100.0 * cover / n:0.0}%; mean A×lum {al / n:0.000}");
        }
        return 0;
    }

    // ------------------------------------------------------------------ textures

    static int Tex(int[] ids)
    {
        foreach (var bank in new[] { "sfx9999res.ffxbnd", "sfx9999res_Append.ffxbnd" })
        {
            var bnd = ReadBank(Path.Combine(SfxDir(), bank));
            Console.WriteLine($"{bank}: {bnd.Files.Count} files; ext {string.Join(", ", bnd.Files.GroupBy(f => Path.GetExtension(f.Name).ToLowerInvariant()).Select(g => $"{g.Key} ×{g.Count()}"))}");
            foreach (var f in bnd.Files.Take(3)) Console.WriteLine($"  #{f.ID} {f.Name} {f.Bytes.Length} flags {f.Flags}");
            foreach (var id in ids)
                foreach (var f in bnd.Files.Where(f => Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(f.Name)).TrimStart('s', 'S').TrimStart('0') == id.ToString()
                                                     || f.Name.Contains($"{id:D5}") || f.ID == id))
                {
                    string info = "";
                    try
                    {
                        if (TPF.Is(f.Bytes))
                        {
                            var tpf = TPF.Read(f.Bytes);
                            info = $"TPF platform {tpf.Platform} encoding {tpf.Encoding} flag2 {tpf.Flag2}: " + string.Join("; ", tpf.Textures.Select(t => $"{t.Name} fmt {t.Format} type {t.Type} mips {t.Mipmaps} flags1 {t.Flags1} {t.Bytes.Length} bytes {DdsSize(t.Bytes)}"));
                        }
                    }
                    catch (Exception e) { info = e.Message; }
                    Console.WriteLine($"  tex {id}: #{f.ID} {f.Name} {f.Bytes.Length} bytes {info}");
                }
        }
        return 0;
    }

    static string DdsSize(byte[] b) => b.Length > 20 && b[0] == 'D' && b[1] == 'D' ? $"{BitConverter.ToInt32(b, 16)}×{BitConverter.ToInt32(b, 12)} fourcc {Encoding.ASCII.GetString(b, 84, 4)}" : "";
}
