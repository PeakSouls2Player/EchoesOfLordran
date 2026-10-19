using System.Globalization;
using SoulsFormats;
using Lordran.Tools.Textures;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Rule B2b (session 9, mist track): a DS1 map effect (DSR FXR1, sfx\FRPG_SfxBnd_*.ffxbnd.dcx) rebuilt as a DS2 effect
/// (DLsE) with DS1's own structure and values, added to DS2's global effect bank. User directive (session 8/9): DS1's
/// ambience particles should match DSR; DS1's own data converted by rule, a DS2 effect only where DS1's can't be expressed.
///
/// <para><b>Evidence</b> (mist-survey, data\sandbox\mist\validation):
/// <list type="bullet">
/// <item>FXR1 node type ids are DLsE parameter type ids (1 int, 5/6 int sequences, 7 float, 11/12 float sequences,
///   37 effect call, 38 action call, 59 indexed effect, 70 tick, 79/81/85 pairs; SoulsFormats FXR1.FXNode / FFXDLSE.Param).</item>
/// <item>DS1 and DS2 share the effect "classes" 2020–2125 (pairs-common.txt: 21 class effects with identical action
///   sequences, e.g. 2121, 2101, 2023), so DS1's call trees (2121 → 2101 → 14 → 2023 emitters) are kept as they are; DS2
///   still uses 2023 (×1797) with DS1's 18-parameter signature.</item>
/// <item>DS1 compiles three kinds of actions into structs (FXR1 ActionData) that DS2 keeps as parameter lists. <b>Fix pass
///   3:</b> DS2 compiles action 71 with <b>FUN_14100c880</b> (FXClusterAppearance_Billboard.cpp, struct tag 0x47; registered
///   by FUN_140fe4e70 for type 0x47; its hooks DAT_141896928/30 are bss and never written). FUN_140bffe20
///   (SfxFxClusterAppearance_Billboard.cpp), which the earlier passes read, is the compile of DS2's action <b>15000</b>
///   (registered for 15000 only, FUN_140bff850) and maps several parameters differently. FUN_14100c880's output IS DSR's
///   FXR1 ActionData71 (dword 10 = lifetime mean, 0xb = min/mean, 0xc = (max−min)/mean ← t2 p3; 0xd AttachedBool ← p23;
///   0xe RenderDepth ← p32; 0x10/0x11 textures ← p1/p2; 0x12 orientation ← p4; 0x13 blend ← p5; 0x14 Unk10 ← p28;
///   0x1e/0x22 Scale2X/Y ← p10/p11; 0x26/0x2a/0x2e RotX/Y/Z ← p14–16; 0x32/0x36/0x3a RotSpeedX/Y/Z ← p17–22; 0x3e/0x3f
///   slices/frames ← p6/p7; 0x40 initial frame ← p8; 0x44 timeline ← p9; 0x58–0x64 Color2RGBA ← p12 × p13; 0x68 DS1R_UnkA1
///   = 5 if p2 = 0 and p30 ≥ 0, else 4 (p29 = 0) / 8 (p29 ≠ 0); 0x71–0x73 Unk16–18 ← p24–26; 0x74 Unk19 ← p35; 0x75
///   AlphaMode ← p27; 0x76 Saturation ← p30; 0x77 Unk22 = caller flag | 2·(p31 ≠ 0); Scale1 0x16/0x1a and Color1
///   0x48–0x54 come from the caller, not from 71's parameters). DSR's data obeys it (all 5278 71 structs of every DSR
///   binder): UnkA1 = 5 on exactly the 298 with texture 2 = 0 and Saturation ≥ 0, 4/8 on all others; Unk22 never has bit
///   1; lifetime 2·DurationMult + LifetimeVariance = 2 on 1468 of 1476 (life71.txt; the other 8: Duration 0 stored as
///   (0, 1), the compile's zero-mean branch) (mist-survey compile71, life71).</item>
/// <item>Action 32 (box emitter) and 55 (acceleration): the FXR1 fields line up with DS2's parameter types (stats-32/55.txt).</item>
/// </list></para>
///
/// <para><b>Rule</b>: the effect tree is converted node for node; DS1's indexed references to its own containers are
/// inlined (DS2's content effects never use ParamList2); actions 71/32/55 are rebuilt from a DS2 template use of the same
/// action (types and counts exactly DS2's, so DS2's "!!invalid parameter size!!" check holds) with DS1's values in the
/// mapped positions; DS1-only actions (104) are dropped; DS1 action 11 (no parameters; never used by DS2) becomes DS2's
/// identity action 1 (as in DS2's own mist 3011). DS1 textures become DS2 effect textures 700000 + id (free in DS2's global
/// res banks), BC7 re-encoded as DXT5 (Ds2Tex.Color). The converted effects go into title:/sfx/sfx9999_Append.ffxbnd.dcx
/// (vanilla entries unchanged) with a sfxParameter record — a map SFX instance without one is never spawned
/// (FUN_1403c8240: FUN_140bec080 lookup fails → instance invalid) — and the textures into sfx9999res_Append.ffxbnd (and
/// sfx_lq\sfx9999lqres_Append.ffxbnd). Anything outside the mapped subset throws NotSupported and the map keeps rule B1
/// (a DS2 effect of the same kind). Status P (built, not verified in game).</para>
/// <para><b>Rule B2b-id</b> (s9-10 crash fix): the converted effect does NOT keep DS1's id. DS2 loads title:/sfx effects
/// lazily and tracks them in a std::bitset&lt;30000&gt; (<see cref="LazyIdLimit"/>); DS1's 100201 crashed DS2 at the
/// Firelink load (std::out_of_range → abort). DS2 id = 20000 + the k-th free id family, k = the DS1 id's rank among all
/// of DSR's effect ids (<see cref="Ds2IdFor"/>): DS1 100201 → DS2 22955.</para>
/// </summary>
public static class FfxPorter
{
    /// <summary>DS1 effect texture id t → DS2 effect texture 700000 + t (DS2's global res banks hold 2..1000, 800000+, 900000+, 980000).</summary>
    public const int TexBase = 700000;

    /// <summary>Session 11 (atmos11), A/B lane LR_SFX_TEX=ds2: DS2's mist texture (3011 【もや】 samples 36/60/100).</summary>
    public const int SfxTexLaneTexture = 36;

    /// <summary>Largest side of a converted effect texture (build-pack --sfx-tex-max N; default 512, see WriteBanks).</summary>
    public static int TexMax = 512;

    /// <summary>DS2's res-bank loader (decomp DarkSoulsII.c 2277667–2277676) loads a ".tpf" entry only when the number in
    /// its NAME ("s%06d") is below 0xdbbb4 = 900020; the entry name is the key (BND ids are free: vanilla uses id − 2 below
    /// 6000 and id − 3002 from 800000 up). A converted texture must stay below this bound.</summary>
    public const int TexIdLimit = 900020;

    /// <summary>build-pack --sfx-dsr-alpha on|off (fix pass, A/B positive control): apply DSR's colour-multiplier ALPHA
    /// (DS1R ColorModA) or not. RGB × Mult is applied either way. Default on (DSR's own value; status U).
    /// <para>Fix pass 3: DS2's action-71 compile (FUN_14100c880) never reads p39 (it reads p1–p32 and p35 only), so the
    /// multiplier is folded into the colour curve p12 (struct 0x58–0x64, the only colour the compile reads) and p39 keeps
    /// DS2's template value. The earlier passes wrote it into p39, i.e. it had no effect in DS2.</para></summary>
    public static bool DsrAlpha = true;

    /// <summary>build-pack --sfx-noend ds1|sentinel (fix pass 3): a DS1 billboard that never ends (DSR lifetime 1666667 s =
    /// 1e8 frames / 60) keeps DS1's own lifetime (default "ds1": DS2's 71 compile stores it exactly as DSR's struct does)
    /// or gets t2(−1/30, −1/30) ("sentinel": DS2's "never ends" value of actions 15000 p3 / 15001 p4 / 70 p4; DS2's own
    /// 71s never use it, 0 of 185 — an A/B for later, status U).</summary>
    public static bool NoEndSentinel = false;

    /// <summary>Session 11 (spell11): node types 13/19/20/68 (spell-ffx probe of DS1 spell effects); off for map effects.</summary>
    public static bool SpellNodes = false;

    // ------------------------------------------------------------------ DS2 effect ids (s9-10 crash fix, rule B2b-id)

    /// <summary>DS2 parses a title:/sfx effect on its FIRST create (FUN_140bf7960, decomp DarkSoulsII.c 2278223; the bank
    /// loader sets the bank's lazy flag +0x110 on the first ".ffx" entry and loads only the classes 2000–2200 eagerly,
    /// line 2277581–2277585) and then marks the id in a std::bitset&lt;30000&gt; (bank +0xfc8; <c>cmp rcx, 0x7530</c> at
    /// 0x140bf7cd8). An id ≥ 30000 parses and registers fine, then the bitset throws std::out_of_range("invalid bitset&lt;N&gt;
    /// position") (_Xout_of_range 0x140c28924 from 0x140bf7dde); nothing catches it → terminate → abort (fast-fail 7):
    /// run s9-10, DS1 id 100201 kept as the DS2 id. Every effect in a title:/sfx bank needs an id below this.</summary>
    public const int LazyIdLimit = 30000;

    /// <summary>The block the converted effects use: DS2 id = <see cref="IdBlock"/> + b, b a FREE family (see
    /// <see cref="FamilyFree"/>). FUN_140beb670 treats ids ≤ 9999 specially when the sfx manager's +0x305 flag is set: it
    /// creates id + 20000 instead when that exists, else id plus the companion id + 10000; ids ≥ 10000 are created as they
    /// are. 20000 + b with b, 10000 + b and 20000 + b all unknown to DS2 can therefore only ever be created by our own
    /// regions. The vanilla title banks name ids ≥ 10000 with the FRPG2_64 path (format string at 0x1411ed660), which the
    /// Append's entries already use.</summary>
    public const int IdBlock = 20000;

    static SortedSet<int>? ds2Used;

    /// <summary>Every effect id DS2's title:/sfx banks know: .ffx entry names (f%07d) of sfx9999 + _Append and the ids of
    /// both sfxParameter tables (records may name effects the banks don't hold).</summary>
    public static SortedSet<int> Ds2UsedIds()
    {
        if (ds2Used != null) return ds2Used;
        ds2Used = new();
        foreach (bool append in new[] { false, true })
            foreach (var f in Bank(append).Files)
            {
                string name = Path.GetFileName(f.Name);
                if (name.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase) && int.TryParse(name[1..^4], out int id)) ds2Used.Add(id);
                else if (name.Equals("sfxParameter.sfxparam", StringComparison.OrdinalIgnoreCase))
                    foreach (var (rid, _) in Commands.MistSurvey.ReadSfxParam(f.Bytes)) ds2Used.Add(rid);
            }
        return ds2Used;
    }

    /// <summary>A family b (1..9999) is free when DS2 knows none of b, 10000 + b, 20000 + b (<see cref="IdBlock"/>). b = 0
    /// is never used: data that means "no effect" by 0 would reach 20000 through the +0x305 replacement.</summary>
    public static bool FamilyFree(int b) { var u = Ds2UsedIds(); return b > 0 && !u.Contains(b) && !u.Contains(10000 + b) && !u.Contains(20000 + b); }

    static List<int>? freeFamilies;
    public static List<int> FreeFamilies() => freeFamilies ??= Enumerable.Range(0, 10000).Where(FamilyFree).ToList();

    static List<int>? ds1Census;

    /// <summary>Every DS1 effect id (distinct .ffx entry names over all of DSR's sfx binders, ascending): the rank in this
    /// list picks the DS2 family, so a DS1 effect gets the same DS2 id in every build, whatever maps are built.</summary>
    public static List<int> Ds1Census(string dsrRoot)
    {
        if (ds1Census != null) return ds1Census;
        var ids = new SortedSet<int>();
        foreach (var path in Directory.GetFiles(Path.Combine(dsrRoot, "sfx"), "FRPG_SfxBnd_*.ffxbnd.dcx").Order(StringComparer.OrdinalIgnoreCase))
            foreach (var f in Ds1BankFiles(path))
            {
                string name = Path.GetFileName(f.Name);
                if (name.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase) && int.TryParse(name[1..^4], out int id)) ids.Add(id);
            }
        return ds1Census = ids.ToList();
    }

    /// <summary>Rule B2b-id: DS1 effect → DS2 id <see cref="IdBlock"/> + the k-th free family, k = the DS1 id's rank in
    /// <see cref="Ds1Census"/> (DS1 100201 → see validation\ids.txt). Throws NotSupported when DS1's census is larger than
    /// DS2's free families (the effect then keeps rule B1/B1d).</summary>
    public static int Ds2IdFor(string dsrRoot, int ds1Id)
    {
        var census = Ds1Census(dsrRoot);
        int k = census.BinarySearch(ds1Id);
        if (k < 0) throw new NotSupportedException($"DS1 effect {ds1Id} is in no DSR sfx binder");
        var fam = FreeFamilies();
        if (k >= fam.Count) throw new NotSupportedException($"DS1 effect {ds1Id} is #{k} of DS1's {census.Count} effects; DS2 has only {fam.Count} free id families below {LazyIdLimit}");
        int id = IdBlock + fam[k];
        if (id >= LazyIdLimit || Ds2UsedIds().Contains(id)) throw new InvalidOperationException($"DS2 id {id} for DS1 {ds1Id} is not free");
        return id;
    }

    public sealed class Converted
    {
        /// <summary>The DS2 effect id (rule B2b-id; DS1's own id is <see cref="Ds1Id"/>).</summary>
        public int Id;
        public int Ds1Id;
        public FFXDLSE Fx = null!;
        public string Source = "";
        /// <summary>DS2 texture id → (DS1 binder, DS1 texture id).</summary>
        public SortedDictionary<int, (string Binder, int Ds1Tex)> Textures = new();
        /// <summary>Longest finite particle lifetime (s), for the sfxParameter record.</summary>
        public float MaxLife;
        public List<string> Notes = new();
    }

    static readonly Dictionary<int, Converted?> done = new();

    /// <summary>Every effect converted in this build (for <see cref="WriteBanks"/>).</summary>
    public static IEnumerable<Converted> All => done.Values.OfType<Converted>().OrderBy(c => c.Id);

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ------------------------------------------------------------------ DS2 templates

    static string Ds2Sfx => Path.Combine(Program.DefaultDs2Root, "sfx");
    static IBinder? baseBank, appendBank;

    static IBinder Bank(bool append)
    {
        if (append) return appendBank ??= BND4.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(Ds2Sfx, "sfx9999_Append.ffxbnd.dcx"))));
        return baseBank ??= BND4.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(Ds2Sfx, "sfx9999.ffxbnd.dcx"))));
    }

    /// <summary>A fresh copy of a vanilla DS2 effect (each call parses the file again, so callers may edit it).</summary>
    static FFXDLSE Ds2(int id)
    {
        string name = $"f{id:D7}.ffx";
        var f = Bank(true).Files.FirstOrDefault(x => Path.GetFileName(x.Name).Equals(name, StringComparison.OrdinalIgnoreCase))
             ?? Bank(false).Files.First(x => Path.GetFileName(x.Name).Equals(name, StringComparison.OrdinalIgnoreCase));
        return FFXDLSE.Read(f.Bytes);
    }

    /// <summary>The first use of an action in a DS2 effect, as a fresh object.</summary>
    static FFXDLSE.Param38 Ds2Action(int effect, int action)
    {
        FFXDLSE.Param38? Find(FFXDLSE.Param p) => p switch
        {
            FFXDLSE.Param38 a when a.ActionID == action => a,
            FFXDLSE.Param38 a => a.ParamList.Params.Select(Find).FirstOrDefault(x => x != null),
            FFXDLSE.Param37 c => c.ParamList.Params.Select(Find).FirstOrDefault(x => x != null),
            _ => null,
        };
        var fx = Ds2(effect);
        foreach (var st in fx.Effect.StateMap.States)
            foreach (var a in st.Actions)
                foreach (var p in a.ParamList.Params)
                    if (Find(p) is { } hit) return hit;
        throw new InvalidOperationException($"DS2 effect {effect} has no action {action}");
    }

    /// <summary>Template uses: action 71 of DS2 3204 (the most common DS2 71 configuration: orientation 1, blend 2, alpha
    /// mode 1, 48 parameters [37 main]); action 32 of DS2 160 【通路に漂う煙】 (DS2's drifting-smoke box emitter: volume
    /// distribution); the identity action 1 and the state-0 → 1 trigger of DS2's mist 3011.</summary>
    const int Template71 = 3204, Template32 = 160, TemplateMist = 3011;

    // ------------------------------------------------------------------ DS1 side

    /// <summary>DS1's effect from FRPG_SfxBnd_mAA_BB, then _mAA, then _CommonEffects (as MapEffects.Ds1).</summary>
    static (FXR1? Fx, string Binder) Ds1Effect(string dsrRoot, string ds1Map, int id)
    {
        foreach (string b in new[] { $"m{ds1Map[1..6]}", $"m{ds1Map[1..3]}", "CommonEffects" })
        {
            string path = Path.Combine(dsrRoot, "sfx", $"FRPG_SfxBnd_{b}.ffxbnd.dcx");
            if (!File.Exists(path)) continue;
            var f = Ds1BankFiles(path).FirstOrDefault(x => Path.GetFileNameWithoutExtension(x.Name).Equals($"f{id:D7}", StringComparison.OrdinalIgnoreCase));
            if (f != null) return (FXR1.Read(f.Bytes), b);
        }
        return (null, "");
    }

    static readonly Dictionary<string, List<BinderFile>> ds1Banks = new(StringComparer.OrdinalIgnoreCase);
    static List<BinderFile> Ds1BankFiles(string path)
    {
        if (!ds1Banks.TryGetValue(path, out var files))
            ds1Banks[path] = files = BND3.Read(DCX.Decompress(File.ReadAllBytes(path))).Files;
        return files;
    }

    /// <summary>DS1 texture s{id:D5}.tpf: the effect's binder first, then the area binder, then CommonEffects.</summary>
    static byte[]? Ds1Texture(string dsrRoot, string binder, int id)
    {
        foreach (string b in new[] { binder, binder.Length > 3 && binder.StartsWith('m') ? binder[..3] : binder, "CommonEffects" }.Distinct())
        {
            string path = Path.Combine(dsrRoot, "sfx", $"FRPG_SfxBnd_{b}.ffxbnd.dcx");
            if (!File.Exists(path)) continue;
            var f = Ds1BankFiles(path).FirstOrDefault(x => Path.GetFileName(x.Name).Equals($"s{id:D5}.tpf", StringComparison.OrdinalIgnoreCase));
            if (f != null) return f.Bytes;
        }
        return null;
    }

    // ------------------------------------------------------------------ conversion

    /// <summary>DS1 effect <paramref name="ds1Id"/> as a DS2 effect with the same id (null: not convertible; reason logged).</summary>
    public static Converted? Convert(string dsrRoot, string ds1Map, int ds1Id, List<string> log)
    {
        if (done.TryGetValue(ds1Id, out var hit)) return hit;
        Converted? result = null;
        try
        {
            var (fxr, binder) = Ds1Effect(dsrRoot, ds1Map, ds1Id);
            if (fxr == null) throw new NotSupportedException("DS1 effect not found");
            // s9-10 crash fix: DS1's own id (100201) is ≥ 30000, DS2's lazy-load limit → rule B2b-id
            int ds2Id = Ds2IdFor(dsrRoot, ds1Id);
            var c = new Converted { Id = ds2Id, Ds1Id = ds1Id, Source = $"DS1 FRPG_SfxBnd_{binder} f{ds1Id:D7}" };
            c.Fx = Build(fxr, c, binder);
            result = c;
            log.Add($"atmos: rule B2b: DS1 effect {ds1Id} ({c.Source}) → DS2 effect {ds2Id} (rule B2b-id: {IdBlock} + free family #{Ds1Census(dsrRoot).BinarySearch(ds1Id)}): {string.Join("; ", c.Notes)}; textures {string.Join(", ", c.Textures.Select(t => $"{t.Value.Ds1Tex}→{t.Key}"))}");
        }
        catch (NotSupportedException e)
        {
            log.Add($"atmos: rule B2b: DS1 effect {ds1Id} not convertible ({e.Message}); rule B1 (same-kind DS2 effect) used");
        }
        return done[ds1Id] = result;
    }

    sealed class Ctx
    {
        public Converted C = null!;
        public string Binder = "";
        public List<FXR1.FXNode> Locals = new();
        public SortedSet<int> Actions = new();
        public SortedSet<int> Classes = new();
    }

    static FFXDLSE Build(FXR1 fxr, Converted c, string binder)
    {
        if (fxr.RootFXNode is not FXR1.FXNode.EffectCreateNode133 root) throw new NotSupportedException($"root {fxr.RootFXNode.GetType().Name}");
        if (root.Container1.FXNodes.Count != 0) throw new NotSupportedException("container 1 not empty");
        var ctx = new Ctx { C = c, Binder = binder, Locals = root.Container2.FXNodes };
        var fx = new FFXDLSE();
        var e = fx.Effect;
        e.ID = c.Id;
        // ParamList1 = DS2's per-effect settings (15 values, compiled into sfxParameter.sfxparam): DS2's mist 3011's
        // (draw distance 40 m: DS2's map ambience 25–100, -1 = none); DS1 has no such list (root unk 8 not mapped).
        e.ParamList1 = Ds2(TemplateMist).Effect.ParamList1;
        e.ParamList2 = new FFXDLSE.ParamList();
        var mistTrigger = Ds2(TemplateMist).Effect.StateMap.States[0].Triggers[0];
        foreach (var st in root.States)
        {
            var s = new FFXDLSE.State();
            foreach (var a in st.Actions)
            {
                if (a.ActionType == 104)
                {
                    c.Notes.Add($"DS1 state action 104 ({string.Join(", ", a.Container.FXNodes.Select(n => MistValue(n)))}) dropped (DS2 has no action 104)");
                    continue;
                }
                var act = new FFXDLSE.Action { ID = a.ActionType, ParamList = List(a.Container?.FXNodes ?? [], ctx) };
                if (a.Container?.ActionData != null) throw new NotSupportedException($"state action {a.ActionType} with ActionData");
                ctx.Actions.Add(a.ActionType);
                s.Actions.Add(act);
            }
            foreach (var t in st.Transitions)
            {
                int target = root.States.IndexOf(t.TargetState);
                if (target < 0) throw new NotSupportedException("transition target not a root state");
                // DS1's compiled evaluator (EmptyNode136) → DS2's own "no children left, or no parent" (3011's)
                s.Triggers.Add(new FFXDLSE.Trigger { StateIndex = target, Evaluator = mistTrigger.Evaluator });
            }
            e.StateMap.States.Add(s);
        }
        if (root.States.Sum(s => s.Transitions.Count) > 1) c.Notes.Add("more than one state transition: all take DS2's end-of-effect evaluator");
        // class 2121 (a group of 8 child effects: state 2 spawns arg59(1,0..7) with action 14) is defined in DS2's bank
        // but never called by DS2's content, which puts the children straight into action 14's 8 effect slots (3020):
        // a 2121 alone in an action 14 with its default control arguments is replaced by its children, slot for slot
        foreach (var st in e.StateMap.States)
            foreach (var a in st.Actions.Where(a => a.ID == 14))
                Unwrap2121(a.ParamList, c);
        e.ResourceSet = null!; // recomputed below (2121 may be gone)
        ctx.Classes.Clear();
        void Classes(FFXDLSE.Param p)
        {
            if (p is FFXDLSE.Param37 x) { if (x.EffectID != 0) ctx.Classes.Add(x.EffectID); foreach (var q in x.ParamList.Params) Classes(q); }
            else if (p is FFXDLSE.Param38 y) { if (y.ActionID == 14) Unwrap2121(y.ParamList, c); foreach (var q in y.ParamList.Params) Classes(q); }
        }
        foreach (var st in e.StateMap.States) foreach (var a in st.Actions) foreach (var p in a.ParamList.Params) Classes(p);
        e.ResourceSet = new FFXDLSE.ResourceSet
        {
            Vector2 = c.Textures.Keys.ToList(),
            Vector4 = ctx.Actions.Where(x => x != 0).ToList(),
            Vector5 = ctx.Classes.Where(x => x != 0).ToList(),
        };
        return fx;
    }

    /// <summary>An action-14 list [2121 {E0..E7, A8..A10 = 0, I11 = 1, I12 = 1, T13 = 0, T14 &lt; 0}, 0 × 7] → [E0..E7].</summary>
    static void Unwrap2121(FFXDLSE.ParamList l, Converted c)
    {
        if (l.Params.Count != 8) return;
        var groups = l.Params.OfType<FFXDLSE.Param37>().Where(x => x.EffectID == 2121).ToList();
        if (groups.Count != 1 || l.Params.OfType<FFXDLSE.Param37>().Count(x => x.EffectID != 0) != 1) return;
        var g = groups[0].ParamList.Params;
        bool defaults = g.Count == 15 && g.Take(8).All(x => x is FFXDLSE.Param37)
            && g.Skip(8).Take(3).All(x => x is FFXDLSE.Param38 { ActionID: 0 })
            && g[11] is FFXDLSE.Param1 { Int: 1 } && g[12] is FFXDLSE.Param1 { Int: 1 }
            && g[13] is FFXDLSE.Param70 { Tick: 0 } && g[14] is FFXDLSE.Param70 { Tick: < 0 };
        if (!defaults) throw new NotSupportedException("class 2121 with non-default control arguments");
        l.Params = g.Take(8).ToList();
        l.Unk04 = 8;
        c.Notes.Add("class 2121 group unwrapped into action 14's slots (DS2 content never calls 2121)");
    }

    static FFXDLSE.ParamList List(IEnumerable<FXR1.FXNode> nodes, Ctx ctx)
    {
        var l = new FFXDLSE.ParamList();
        foreach (var n in nodes) l.Params.Add(Param(n, ctx));
        l.Unk04 = l.Params.Count;
        return l;
    }

    static FFXDLSE.Param Param(FXR1.FXNode n, Ctx ctx)
    {
        switch (n)
        {
            case FXR1.FXNode.IntNode x: return new FFXDLSE.Param1 { Int = x.Value };
            case FXR1.FXNode.FloatNode x: return new FFXDLSE.Param7 { Float = x.Value };
            // ticks are seconds in both games; the "-1 frame" sentinel (no end) is -1/60 in DSR, -1/30 in DS2 (every
            // DS2 class call: 2023 p0/p1, 2101 p0 = -0.0333)
            case FXR1.FXNode.TickNode x: return new FFXDLSE.Param70 { Tick = Sentinel(x.Tick) };
            case FXR1.FXNode.IntSequenceNode5 x: return new FFXDLSE.Param5 { TickInts = x.Ticks.Select(t => new FFXDLSE.TickInt(t.Time, t.Value)).ToList() };
            case FXR1.FXNode.IntSequenceNode6 x: return new FFXDLSE.Param6 { TickInts = x.Ticks.Select(t => new FFXDLSE.TickInt(t.Time, t.Value)).ToList() };
            case FXR1.FXNode.FloatSequenceNode11 x: return new FFXDLSE.Param11 { TickFloats = x.Ticks.Select(t => new FFXDLSE.TickFloat(t.Time, t.Value)).ToList() };
            case FXR1.FXNode.FloatSequenceNode12 x: return new FFXDLSE.Param12 { TickFloats = x.Ticks.Select(t => new FFXDLSE.TickFloat(t.Time, t.Value)).ToList() };
            case FXR1.FXNode.Int2Node x: return new FFXDLSE.Param79 { Int1 = x.X, Int2 = x.Y };
            case FXR1.FXNode.Float2Node x: return new FFXDLSE.Param81 { Float1 = x.X, Float2 = x.Y };
            case FXR1.FXNode.Tick2Node x: return new FFXDLSE.Param85 { Tick1 = Sentinel(x.Tick1), Tick2 = Sentinel(x.Tick2) };
            case FXR1.FXNode.IndexedEffectNode x when x.Type == 2 && x.Index >= 0 && x.Index < ctx.Locals.Count:
                // DS1's reference to its own container 2: inlined (DS2's content effects never use ParamList2)
                return Param(ctx.Locals[x.Index], ctx);
            case FXR1.FXNode.EffectCallNode x:
                if (x.EffectID != 0) ctx.Classes.Add(x.EffectID);
                if (x.EffectID != 0 && x.EffectID is < 2000 or > 2199) throw new NotSupportedException($"call of content effect {x.EffectID}");
                return new FFXDLSE.Param37 { EffectID = x.EffectID, ParamList = x.Container == null ? new FFXDLSE.ParamList() : List(x.Container.FXNodes, ctx) };
            case FXR1.FXNode.ActionCallNode x: return Action(x, ctx);
            // session 11 (spell11, rule B2b-s, spell-ffx probe only): the remaining same-numbered FXR1 node / DLsE parameter
            // types DS1's spell effects use (13 float3 sequence, 19/20 colour sequences, 68 sound id), mapped 1:1 like the
            // types above. Off for map effects (their rule B2b subset is unchanged).
            case FXR1.FXNode.Float3SequenceNode13 x when SpellNodes: return new FFXDLSE.Param13 { TickFloat3s = x.Ticks.Select(t => new FFXDLSE.TickFloat3 { Tick = t.Time, Float1 = t.X, Float2 = t.Y, Float3 = t.Z }).ToList() };
            case FXR1.FXNode.ColorSequenceNode19 x when SpellNodes: return new FFXDLSE.Param19 { TickColors = x.Ticks.Select(t => new FFXDLSE.TickColor(t.Time, new FFXDLSE.PrimitiveColor(t.R, t.G, t.B, t.A))).ToList() };
            case FXR1.FXNode.ColorSequenceNode20 x when SpellNodes: return new FFXDLSE.Param20 { TickColors = x.Ticks.Select(t => new FFXDLSE.TickColor(t.Time, new FFXDLSE.PrimitiveColor(t.R, t.G, t.B, t.A))).ToList() };
            case FXR1.FXNode.SoundIDNode68 x when SpellNodes: return new FFXDLSE.Param68 { SoundID = x.SoundID };
        }
        throw new NotSupportedException($"node {n.GetType().Name}");
    }

    static float Sentinel(float t) => t < 0 && Math.Abs(t + 1 / 60f) < 1e-4f ? -1 / 30f : t;

    static FFXDLSE.Param38 Action(FXR1.FXNode.ActionCallNode a, Ctx ctx)
    {
        var data = a.Container?.ActionData;
        if (data == null)
        {
            if (a.ActionType == 11 && (a.Container?.FXNodes.Count ?? 0) == 0)
            {
                // DS1 action 11 (no parameters) is never used by DS2: DS2's identity action 1 in the same class slot (3011)
                ctx.Actions.Add(1);
                ctx.C.Notes.Add("DS1 action 11 → DS2 identity action 1");
                return Ds2Action(TemplateMist, 1);
            }
            ctx.Actions.Add(a.ActionType);
            return new FFXDLSE.Param38 { ActionID = a.ActionType, ParamList = List(a.Container?.FXNodes ?? [], ctx) };
        }
        ctx.Actions.Add(a.ActionType);
        return data switch
        {
            FXR1.FXActionData.ClusterAppearanceBillboardActionData71 d => Billboard71(d, ctx),
            FXR1.FXActionData.ClusterEmitterBoxActionData32 d => Box32(d),
            FXR1.FXActionData.ClusterMovementAccelerationActionData55 d => Accel55(d),
            _ => throw new NotSupportedException($"action {a.ActionType} ({data.GetType().Name}) has no DS2 parameter map yet"),
        };
    }

    // ---- FXR1 fields → DLsE parameters

    static FFXDLSE.Param11 Seq(FXR1.FXField f) => f switch
    {
        FXR1.FXField.ConstFloat c => new FFXDLSE.Param11 { TickFloats = [new FFXDLSE.TickFloat(0, c.Value)] },
        FXR1.FXField.FloatSequence s => new FFXDLSE.Param11 { TickFloats = s.Ticks.Select(t => new FFXDLSE.TickFloat(t.Time, t.Value)).ToList() },
        FXR1.FXField.Float3Sequence s => new FFXDLSE.Param11 { TickFloats = s.Ticks.Select(t => new FFXDLSE.TickFloat(t.Time, t.X)).ToList() },
        FXR1.FXField.Empty => new FFXDLSE.Param11 { TickFloats = [new FFXDLSE.TickFloat(0, 0)] },
        _ => throw new NotSupportedException($"field {f.GetType().Name}"),
    };

    /// <summary>A scalar field's value at time t (linear between ticks; Float3Sequence: X is the value).</summary>
    static float At(FXR1.FXField f, float t)
    {
        List<(float T, float V)> k = f switch
        {
            FXR1.FXField.ConstFloat c => [(0, c.Value)],
            FXR1.FXField.FloatSequence s => s.Ticks.Select(x => (x.Time, x.Value)).ToList(),
            FXR1.FXField.Float3Sequence s => s.Ticks.Select(x => (x.Time, x.X)).ToList(),
            FXR1.FXField.Empty => [(0, 0)],
            _ => throw new NotSupportedException($"field {f.GetType().Name}"),
        };
        if (t <= k[0].T) return k[0].V;
        for (int i = 1; i < k.Count; i++)
            if (t <= k[i].T) return k[i - 1].V + (k[i].V - k[i - 1].V) * (t - k[i - 1].T) / Math.Max(1e-6f, k[i].T - k[i - 1].T);
        return k[^1].V;
    }

    static IEnumerable<float> Times(FXR1.FXField f) => f switch
    {
        FXR1.FXField.FloatSequence s => s.Ticks.Select(x => x.Time),
        FXR1.FXField.Float3Sequence s => s.Ticks.Select(x => x.Time),
        FXR1.FXField.ConstFloat or FXR1.FXField.Empty => [0f],
        _ => throw new NotSupportedException($"field {f.GetType().Name}"),
    };

    /// <summary>FieldType25 {Base, Min, Max} = Base × (Min + rand × Max) (DS1 compile of DS2-style random ranges:
    /// rnd82(x ± v) → {x, 1 − v, 2v}; fseq × f2(a, b) → {fseq, a, b − a}; checked on DS1's and DS2's value sets).</summary>
    static (float Base, float Lo, float Hi) Range(FXR1.FXField f) => f switch
    {
        FXR1.FXField.FieldType25 r => (r.Base, r.Min, r.Min + r.Max),
        FXR1.FXField.ConstFloat c => (c.Value, 1, 1),
        FXR1.FXField.Empty => (0, 1, 1),
        _ => throw new NotSupportedException($"field {f.GetType().Name}"),
    };

    static float Mean(FXR1.FXField f) { var (b, lo, hi) = Range(f); return b * (lo + hi) / 2; }

    static FFXDLSE.Param38 Billboard71(FXR1.FXActionData.ClusterAppearanceBillboardActionData71 d, Ctx ctx)
    {
        var a = Ds2Action(Template71, 71);
        var p = a.ParamList.Params;
        if (p.Count != 48) throw new InvalidOperationException($"DS2 template 71 has {p.Count} parameters");
        int tex = TexBase + d.TextureID;
        // DS2 loads res-bank textures only below 900020 by name (TexIdLimit); outside it the effect isn't convertible
        foreach (int t in new[] { d.TextureID, d.Unk7 }.Where(t => t != 0))
            if (t < 0 || TexBase + t >= TexIdLimit) throw new NotSupportedException($"DS1 texture {t} → DS2 {TexBase + t} is outside DS2's loadable effect-texture range (< {TexIdLimit})");
        ctx.C.Textures[tex] = (ctx.Binder, d.TextureID);
        p[1] = new FFXDLSE.Param40 { TextureID = tex };
        p[2] = new FFXDLSE.Param40 { TextureID = d.Unk7 == 0 ? 0 : TexBase + d.Unk7 };
        // session 11 (atmos11), A/B lane LR_SFX_TEX=ds2 (B2b "loads, draws nothing", s9-15/16): the converted billboard
        // samples DS2's own mist texture 36 (3011's, rendered in our maps) instead of the converted DS1 texture in
        // sfx9999res_Append: sheets showing up = the texture route is the culprit; still nothing = effect/record/71 data
        if (Environment.GetEnvironmentVariable("LR_SFX_TEX") == "ds2")
        {
            p[1] = new FFXDLSE.Param40 { TextureID = SfxTexLaneTexture };
            p[2] = new FFXDLSE.Param40 { TextureID = 0 };
            ctx.C.Notes.Add($"billboard {d.TextureID}: texture → DS2 {SfxTexLaneTexture} (LR_SFX_TEX=ds2 A/B lane)");
        }
        if (d.Unk7 != 0) ctx.C.Textures[TexBase + d.Unk7] = (ctx.Binder, d.Unk7);
        // lifetime t2(min, max): Duration = mean, DurationMult = min/mean, LifetimeVariance = (max − min)/mean
        float lo = d.Duration * d.DurationMult, hi = d.Duration * (d.DurationMult + d.LifetimeVariance);
        p[3] = new FFXDLSE.Param85 { Tick1 = lo, Tick2 = hi };
        if (hi < 1e5f) ctx.C.MaxLife = Math.Max(ctx.C.MaxLife, hi);
        else if (NoEndSentinel)
        {
            // --sfx-noend sentinel (fix pass 3: opt-in A/B, U): DS2's "never ends" value of 15000 p3 / 15001 p4 / 70 p4;
            // DS2's own 71s never use it and FUN_14100c880 stores it unchanged (mean −1/30)
            p[3] = new FFXDLSE.Param85 { Tick1 = -1 / 30f, Tick2 = -1 / 30f };
            ctx.C.Notes.Add($"billboard {d.TextureID}: DS1 no-end lifetime {lo:0} s → DS2 sentinel t2(−1/30, −1/30) (--sfx-noend sentinel)");
        }
        else ctx.C.Notes.Add($"billboard {d.TextureID}: DS1 no-end lifetime {lo:0} s kept (FUN_14100c880 stores DSR's own struct values)");
        // struct 0x12 orientation (same enum; the 71 compile copies it unchanged and the draw switch handles 0..5)
        if (d.OrientationMode is < 0 or > 5) throw new NotSupportedException($"orientation {d.OrientationMode}");
        p[4] = new FFXDLSE.Param1 { Int = d.OrientationMode };
        // struct 0x13 blend: DS2 71/70/15000/15001 use only 2 and 4 (DS2's own mist 3011: 2); DS1's other modes → 2 (U)
        int blend = d.BlendMode is 2 or 4 ? d.BlendMode : 2;
        if (blend != d.BlendMode) ctx.C.Notes.Add($"billboard {d.TextureID}: DS1 blend {d.BlendMode} → DS2 {blend} (DS2 uses only 2/4)");
        p[5] = new FFXDLSE.Param1 { Int = blend };
        p[6] = new FFXDLSE.Param1 { Int = d.AnimFrameSliceCountPerRow };
        p[7] = new FFXDLSE.Param1 { Int = d.AnimFrameTotalCount };
        var (ib, ilo, ihi) = Range(d.AnimFrameInitial);
        p[8] = d.AnimFrameInitial is FXR1.FXField.Empty ? new FFXDLSE.Param79 { Int1 = 0, Int2 = 0 }
             : new FFXDLSE.Param79 { Int1 = (int)MathF.Floor(ib * ilo), Int2 = Math.Max((int)MathF.Floor(ib * ilo), (int)MathF.Ceiling(ib * ihi) - 1) };
        p[9] = new FFXDLSE.Param6
        {
            TickInts = d.AnimFrameTimeline is FXR1.FXField.Empty ? [new FFXDLSE.TickInt(0, 0)]
                : Seq(d.AnimFrameTimeline).TickFloats.Select(t => new FFXDLSE.TickInt(t.Tick, (int)MathF.Round(t.Float))).ToList(),
        };
        // size = Scale2 (over life) × the mean of DS1's per-particle random Scale1 (DS2's 71 has no per-particle scale)
        float s1x = d.Scale1X is FXR1.FXField.Empty ? 1 : Mean(d.Scale1X), s1y = d.Scale1Y is FXR1.FXField.Empty ? s1x : Mean(d.Scale1Y);
        var sx = Seq(d.Scale2X); foreach (var t in sx.TickFloats) t.Float *= s1x;
        var sy = Seq(d.Scale2Y is FXR1.FXField.Empty ? d.Scale2X : d.Scale2Y); foreach (var t in sy.TickFloats) t.Float *= s1y;
        p[10] = sx; p[11] = sy;
        // colour over life: Color2 RGBA (union of the channels' ticks) × the constant Color1 × DSR's colour multiplier
        // (fix pass 3: FUN_14100c880 reads no colour parameter but p12/p13, so DS1R ColorMod RGB × Mult — and its alpha with
        // DsrAlpha — goes in here; p39, which the earlier passes used, is never read for 71)
        var c2 = new[] { d.Color2R, d.Color2G, d.Color2B, d.Color2A };
        var c1 = new[] { d.Color1R, d.Color1G, d.Color1B, d.Color1A };
        if (c1.Any(f => f is not (FXR1.FXField.ConstFloat or FXR1.FXField.Empty))) throw new NotSupportedException("Color1 not constant");
        float[] mod = [1, 1, 1, 1];
        if (d.DS1RData is { } r)
        {
            float m = r.DS1R_ColorModMult is FXR1.FXField.Empty ? 1 : At(r.DS1R_ColorModMult, 0);
            mod = [Ch2(r.DS1R_ColorModR) * m, Ch2(r.DS1R_ColorModG) * m, Ch2(r.DS1R_ColorModB) * m, DsrAlpha ? Ch2(r.DS1R_ColorModA) : 1];
        }
        var times = c2.SelectMany(Times).Distinct().Order().ToList();
        p[12] = new FFXDLSE.Param19
        {
            TickColors = times.Select(t => new FFXDLSE.TickColor(t, new FFXDLSE.PrimitiveColor(
                At(c2[0], t) * At(c1[0], 0) * mod[0], At(c2[1], t) * At(c1[1], 0) * mod[1], At(c2[2], t) * At(c1[2], 0) * mod[2], At(c2[3], t) * At(c1[3], 0) * mod[3]))).ToList(),
        };
        // rotation (radians, as DS2's f2(0, 6.28)): range f2(Base·lo, Base·hi); speed fseq Base × random f2(lo, hi)
        FFXDLSE.Param81 Rot(FXR1.FXField f) { var (b, l, h) = Range(f); return new FFXDLSE.Param81 { Float1 = b * l, Float2 = b * h }; }
        p[14] = Rot(d.RotX); p[15] = Rot(d.RotY); p[16] = Rot(d.RotZ);
        void Spin(int i, FXR1.FXField f)
        {
            var (b, l, h) = Range(f);
            p[i] = new FFXDLSE.Param11 { TickFloats = [new FFXDLSE.TickFloat(0, b)] };
            p[i + 1] = new FFXDLSE.Param81 { Float1 = l, Float2 = h };
        }
        Spin(17, d.RotSpeedX); Spin(19, d.RotSpeedY); Spin(21, d.RotSpeedZ);
        // struct scalar slots (fix pass 3): the inverse of DS2's own 71 compile FUN_14100c880, whose output is DSR's
        // ActionData71 (see the class summary). The earlier passes followed the 15000 compile FUN_140bffe20 and put
        // RenderDepth into p28 (→ 71's slot 0x14 = Unk10) and AlphaMode into p31 (→ a flag bit of 0x77), left p32 (→ 71's
        // RenderDepth slot 0xe) at DS2's −10 and p29 (→ 0x68 = 4/8) at 0. Enumerated DS1 values that DS2's 71 never uses
        // keep DS2's template value (as blend does; noted, U).
        p[23] = new FFXDLSE.Param1 { Int = d.AttachedBool };                     // 0xd
        p[32] = new FFXDLSE.Param7 { Float = d.RenderDepth };                    // 0xe (continuous: DS1 0/−5/−10/…, DS2 0/−10/−15/−20)
        p[28] = Enum71(p, 28, d.Unk10, "Unk10", ctx);                            // 0x14
        p[24] = Enum71(p, 24, d.Unk16, "Unk16", ctx);                            // 0x71
        p[25] = Enum71(p, 25, d.Unk17, "Unk17", ctx);                            // 0x72
        p[26] = Enum71(p, 26, d.Unk18, "Unk18", ctx);                            // 0x73
        p[35] = Enum71(p, 35, d.Unk19, "Unk19", ctx);                            // 0x74 (float)
        p[27] = Enum71(p, 27, d.AlphaMode, "AlphaMode", ctx);                    // 0x75 (FUN_140fe4f10: shader 2/3 vs 4/5)
        p[30] = new FFXDLSE.Param7 { Float = d.Saturation };                     // 0x76
        p[31] = new FFXDLSE.Param1 { Int = (d.Unk22 & 2) != 0 ? 1 : 0 };        // 0x77 bit 1 (bit 0 is the caller's flag)
        // 0x68 = 5 when p2 = 0 and p30 ≥ 0 (DS2 derives it), else 4 (p29 = 0) / 8 (p29 ≠ 0)
        bool five = d.Unk7 == 0 && d.Saturation >= 0;
        p[29] = new FFXDLSE.Param1 { Int = !five && d.DS1R_UnkA1 == 8 ? 1 : 0 };
        if (five != (d.DS1R_UnkA1 == 5) || d.DS1R_UnkA1 is not (4 or 5 or 8))
            ctx.C.Notes.Add($"billboard {d.TextureID}: DSR UnkA1 {d.DS1R_UnkA1} not what the 71 compile derives ({(five ? 5 : "4/8")})");
        // p39 (cseq17) stays DS2's template: the 71 compile never reads it (DSR's multiplier is in p12 above)
        ctx.C.Notes.Add($"billboard tex {d.TextureID}: life {lo:0.##}–{(hi > 1e5 ? "∞" : hi.ToString("0.##", Inv))} s, size {sx.TickFloats[0].Float:0.##}×{sy.TickFloats[0].Float:0.##} m, frames {d.AnimFrameSliceCountPerRow}/{d.AnimFrameTotalCount}, alpha max {times.Max(t => At(c2[3], t)):0.###}{(d.DS1RData != null ? DsrAlpha ? $" × DSR {Ch2(d.DS1RData.DS1R_ColorModA):0.##} (in p12)" : $" (DSR alpha ×{Ch2(d.DS1RData.DS1R_ColorModA):0.##} NOT applied: --sfx-dsr-alpha off)" : "")}, RenderDepth {d.RenderDepth:0.##} → p32, UnkA1 {d.DS1R_UnkA1} → p29 {((FFXDLSE.Param1)p[29]).Int}");
        return a;
    }

    static Dictionary<int, HashSet<double>>? ds2Values71;

    /// <summary>The values DS2's own action-71 uses put in parameter <paramref name="i"/> (Param1 ints, Param7 floats).</summary>
    public static HashSet<double> Ds2Values71(int i)
    {
        if (ds2Values71 == null)
        {
            ds2Values71 = new();
            void Walk(FFXDLSE.Param p)
            {
                switch (p)
                {
                    case FFXDLSE.Param38 a:
                        if (a.ActionID == 71)
                            for (int k = 0; k < a.ParamList.Params.Count; k++)
                            {
                                double? v = a.ParamList.Params[k] switch { FFXDLSE.Param1 x => x.Int, FFXDLSE.Param7 x => x.Float, _ => null };
                                if (v == null) continue;
                                if (!ds2Values71.TryGetValue(k, out var s)) ds2Values71[k] = s = new();
                                s.Add(v.Value);
                            }
                        foreach (var q in a.ParamList.Params) Walk(q);
                        break;
                    case FFXDLSE.Param37 c: foreach (var q in c.ParamList.Params) Walk(q); break;
                    case FFXDLSE.Param82 n: Walk(n.Param); break;
                    case FFXDLSE.Param84 n: Walk(n.Param); break;
                }
            }
            foreach (var (_, (fx, _)) in Commands.MistSurvey.Ds2All())
                foreach (var st in fx.Effect.StateMap.States)
                    foreach (var a in st.Actions)
                        foreach (var q in a.ParamList.Params) Walk(q);
        }
        return ds2Values71.TryGetValue(i, out var set) ? set : [];
    }

    /// <summary>An enumerated DSR struct value into 71's parameter <paramref name="i"/> (same type as the template's): DS1's
    /// value when DS2's own 71s use it, else the template's value (noted).</summary>
    static FFXDLSE.Param Enum71(List<FFXDLSE.Param> p, int i, double v, string field, Ctx ctx)
    {
        bool used = Ds2Values71(i).Contains(v);
        switch (p[i])
        {
            case FFXDLSE.Param1 t:
                if (used) return new FFXDLSE.Param1 { Int = (int)v };
                if (v != t.Int) ctx.C.Notes.Add($"71 {field} {v} → DS2 {t.Int} (p{i}: DS2's 71 uses only {string.Join("/", Ds2Values71(i).Order())})");
                return new FFXDLSE.Param1 { Int = t.Int };
            case FFXDLSE.Param7 t:
                if (used) return new FFXDLSE.Param7 { Float = (float)v };
                if (v != t.Float) ctx.C.Notes.Add($"71 {field} {v} → DS2 {t.Float} (p{i}: DS2's 71 uses only {string.Join("/", Ds2Values71(i).Order())})");
                return new FFXDLSE.Param7 { Float = t.Float };
        }
        throw new InvalidOperationException($"DS2 template 71 p{i} is {p[i].GetType().Name}");
    }

    /// <summary>Rule B2b-id's inverse: the DS1 effect a converted DS2 id stands for (null: not in the block).</summary>
    public static int? Ds1IdFor(string dsrRoot, int ds2Id)
    {
        int k = FreeFamilies().BinarySearch(ds2Id - IdBlock);
        var census = Ds1Census(dsrRoot);
        return ds2Id > IdBlock && ds2Id < LazyIdLimit && k >= 0 && k < census.Count ? census[k] : null;
    }

    /// <summary>Every DSR binder holding DS1 effect <paramref name="id"/> (binder name, parsed effect).</summary>
    public static List<(string Binder, FXR1 Fx)> Ds1EffectAll(string dsrRoot, int id)
    {
        var list = new List<(string, FXR1)>();
        foreach (var path in Directory.GetFiles(Path.Combine(dsrRoot, "sfx"), "FRPG_SfxBnd_*.ffxbnd.dcx").Order(StringComparer.OrdinalIgnoreCase))
        {
            var f = Ds1BankFiles(path).FirstOrDefault(x => Path.GetFileNameWithoutExtension(x.Name).Equals($"f{id:D7}", StringComparison.OrdinalIgnoreCase));
            if (f != null) list.Add((Path.GetFileName(path)[12..^11], FXR1.Read(f.Bytes)));
        }
        return list;
    }

    static float Ch2(FXR1.FXField f) => f is FXR1.FXField.Empty ? 1 : At(f, 0);

    static FFXDLSE.Param38 Box32(FXR1.FXActionData.ClusterEmitterBoxActionData32 d)
    {
        var a = Ds2Action(Template32, 32);
        var p = a.ParamList.Params;
        if (p.Count != 15) throw new InvalidOperationException($"DS2 template 32 has {p.Count} parameters");
        p[1] = Seq(d.EmitterSizeX); p[2] = Seq(d.EmitterSizeY); p[3] = Seq(d.EmitterSizeZ);
        p[4] = Seq(d.EmitterDegree); p[5] = Seq(d.EmitterSpread);
        if (d.EmitterSpeed is not FXR1.FXField.Empty)
        {
            var (b, lo, hi) = Range(d.EmitterSpeed);
            if (Math.Abs(lo + hi - 2) > 1e-3) throw new NotSupportedException($"emitter speed range {lo}..{hi} not symmetric");
            p[6] = new FFXDLSE.Param82 { Param = new FFXDLSE.Param11 { TickFloats = [new FFXDLSE.TickFloat(0, b)] }, Float = (hi - lo) / 2 };
        }
        else p[6] = new FFXDLSE.Param82 { Param = new FFXDLSE.Param11 { TickFloats = [new FFXDLSE.TickFloat(0, 0)] }, Float = 0 };
        // ints: DS2 p10 ∈ {0,1} ← DS1 EmitterDistributionMode copied unchanged (identity; status U: no DS1/DS2 effect pair
        // uses action 32, and the DS2 box-emitter compile wasn't traced; both games' area-fill smoke/mist emitters, DS1
        // 100201 and DS2 160 【通路に漂う煙】, use 1; DS1 uses 1×242/0×55, DS2 0×82/1×27); p13 ∈ {0,2,5} ↔
        // EmitterMovementMode (DS1 0 = DS2 0)
        if (d.EmitterDistributionMode is 0 or 1) p[10] = new FFXDLSE.Param1 { Int = d.EmitterDistributionMode };
        if (d.EmitterMovementMode == 0) p[13] = new FFXDLSE.Param1 { Int = 0 };
        else throw new NotSupportedException($"emitter movement mode {d.EmitterMovementMode}");
        return a;
    }

    static FFXDLSE.Param38 Accel55(FXR1.FXActionData.ClusterMovementAccelerationActionData55 d)
    {
        float mult = d.MovementFalloffMult is FXR1.FXField.Empty ? 1 : At(d.MovementFalloffMult, 0);
        return new FFXDLSE.Param38
        {
            ActionID = 55,
            ParamList = new FFXDLSE.ParamList
            {
                Unk04 = 5,
                Params = [Seq(d.Gravity), Seq(d.MovementFalloff), new FFXDLSE.Param81 { Float1 = mult, Float2 = mult }, new FFXDLSE.Param1 { Int = 0 }, new FFXDLSE.Param7 { Float = d.PhaseShift }],
            },
        };
    }

    static string MistValue(FXR1.FXNode n) => n switch
    {
        FXR1.FXNode.FloatSequenceNode11 s => string.Join("/", s.Ticks.Select(t => t.Value.ToString("0.##", Inv))),
        FXR1.FXNode.IntNode i => i.Value.ToString(),
        _ => n.GetType().Name,
    };

    // ------------------------------------------------------------------ the banks

    /// <summary>sfxParameter record of DS2's mist 3011 with our id (64 bytes; +0x1C = longest particle life in frames at 30 fps).</summary>
    static byte[] Record(byte[] template, int id, float maxLife)
    {
        var r = (byte[])template.Clone();
        BitConverter.GetBytes(id).CopyTo(r, 0);
        if (maxLife > 0) BitConverter.GetBytes((int)MathF.Ceiling(maxLife * 30)).CopyTo(r, 0x1C);
        return r;
    }

    static byte[] InsertRecords(byte[] table, int recordSize, IEnumerable<byte[]> add)
    {
        int count = BitConverter.ToInt32(table, 8), hdr = BitConverter.ToInt32(table, 12);
        var recs = new List<byte[]>();
        for (int i = 0; i < count; i++) recs.Add(table[(hdr + i * recordSize)..(hdr + (i + 1) * recordSize)]);
        foreach (var r in add)
        {
            int id = BitConverter.ToInt32(r, 0);
            recs.RemoveAll(x => BitConverter.ToInt32(x, 0) == id);
            recs.Add(r);
        }
        recs.Sort((a, b) => BitConverter.ToInt32(a, 0).CompareTo(BitConverter.ToInt32(b, 0)));
        var outp = new byte[hdr + recs.Count * recordSize];
        Array.Copy(table, outp, hdr);
        BitConverter.GetBytes(recs.Count).CopyTo(outp, 8);
        for (int i = 0; i < recs.Count; i++) recs[i].CopyTo(outp, hdr + i * recordSize);
        if (table.Length > hdr + count * recordSize) throw new InvalidDataException("sfxparam has trailing data");
        return outp;
    }

    /// <summary>Writes title:/sfx/sfx9999_Append.ffxbnd.dcx (vanilla + the converted effects + their sfxParameter /
    /// sfxDebugParameter records) and the res_Append banks (the converted textures) into the pack. Returns the number of
    /// effects added (0: nothing written).</summary>
    public static int WriteBanks(Pack pack, string dsrRoot, List<string> log)
    {
        var list = All.ToList();
        if (list.Count == 0) return 0;
        string sfx = Path.Combine(pack.Ds2Game, "sfx");
        // ---- effects
        byte[] raw = File.ReadAllBytes(Path.Combine(sfx, "sfx9999_Append.ffxbnd.dcx"));
        byte[] dec = DCX.Decompress(raw, out DCX.CompressionInfo dcxInfo);
        var bnd = BND4.Read(dec);
        var ffxTemplate = bnd.Files.First(f => f.Name.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase));
        string dir = ffxTemplate.Name[..(ffxTemplate.Name.LastIndexOf('\\') + 1)];
        int nextId = bnd.Files.Where(f => f.Name.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase)).Max(f => f.ID) + 1;
        int insertAt = bnd.Files.FindLastIndex(f => f.Name.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase)) + 1;
        var baseParam = Bank(false).Files.First(f => f.Name.EndsWith("sfxParameter.sfxparam", StringComparison.OrdinalIgnoreCase)).Bytes;
        byte[] template3011 = Commands.MistSurvey.ReadSfxParam(baseParam).First(r => r.Id == TemplateMist).Raw;
        foreach (var c in list)
        {
            // s9-10 crash fix guards: the lazy loader's bitset<30000> (LazyIdLimit), the entry name DS2 builds for the id
            // (FUN_140bf7960: "N:\FRPG2\…\f%07d.ffx" below 10000, "N:\FRPG2_64\…" from 10000 up) and the effect's own id
            if (c.Id < 0 || c.Id >= LazyIdLimit) throw new InvalidOperationException($"DS2 effect id {c.Id} outside 0..{LazyIdLimit - 1} (lazy-load bitset)");
            if ((c.Id >= 10000) != dir.Contains("FRPG2_64", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"entry directory {dir} does not match id {c.Id}");
            if (c.Fx.Effect.ID != c.Id) throw new InvalidOperationException($"effect {c.Id}: internal id {c.Fx.Effect.ID}");
            var f = new BinderFile(ffxTemplate.Flags, nextId++, dir + $"f{c.Id:D7}.ffx", c.Fx.Write()) { CompressionInfo = ffxTemplate.CompressionInfo };
            bnd.Files.Insert(insertAt++, f);
        }
        var sp = bnd.Files.First(f => f.Name.EndsWith("sfxParameter.sfxparam", StringComparison.OrdinalIgnoreCase));
        sp.Bytes = InsertRecords(sp.Bytes, 64, list.Select(c => Record(template3011, c.Id, c.MaxLife)));
        var sd = bnd.Files.First(f => f.Name.EndsWith("sfxDebugParameter.sfxparam", StringComparison.OrdinalIgnoreCase));
        sd.Bytes = InsertRecords(sd.Bytes, 8, list.Select(c => { var r = new byte[8]; BitConverter.GetBytes(c.Id).CopyTo(r, 0); return r; }));
        pack.AddFile("title", "sfx/sfx9999_Append.ffxbnd.dcx", DCX.Compress(bnd.Write(), dcxInfo));
        // ---- textures
        var texIds = list.SelectMany(c => c.Textures).GroupBy(t => t.Key).Select(g => g.First()).OrderBy(t => t.Key).ToList();
        var resVanilla = BND4.Read(File.ReadAllBytes(Path.Combine(sfx, "sfx9999res.ffxbnd")));
        var texTemplate = resVanilla.Files.First(f => f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase));
        string texDir = texTemplate.Name[..(texTemplate.Name.LastIndexOf('\\') + 1)];
        var resNames = resVanilla.Files.Select(f => Path.GetFileName(f.Name).ToLowerInvariant()).ToHashSet();
        var tpfs = new List<(int Id, byte[] Bytes)>();
        foreach (var (texId, (binder, ds1Tex)) in texIds)
        {
            if (resNames.Contains($"s{texId:d6}.tpf")) throw new InvalidOperationException($"DS2 texture {texId} exists");
            byte[] ds1 = Ds1Texture(dsrRoot, binder, ds1Tex) ?? throw new FileNotFoundException($"DS1 texture s{ds1Tex:D5}.tpf");
            var src = TPF.Read(ds1).Textures[0];
            var (dds, format) = Ds2Tex.Color(src.Bytes);
            // budget: DS2's global effect textures are ≤ 1024×512 DXT5 (≈ 700 KB; most 128²) and the whole global res
            // bank is 4.5 MB beside an 8.25 MB "Sfx System Heap" (decomp DarkSoulsII.c line 341141; whether the banks live in it is not traced); DSR's 2048² mist sheet (5.6 MB as
            // DXT5) is shrunk to TexMax a side, keeping its frame grid (8 × 8 frames of 64² at 512)
            if (Dds.Read(dds).Width > TexMax || Dds.Read(dds).Height > TexMax)
            {
                dds = Ds2Tex.Downscale(dds, TexMax);
                format = System.Text.Encoding.ASCII.GetString(dds, 84, 4) == "DXT1" ? Ds2Tex.FormatDxt1 : Ds2Tex.FormatDxt5;
            }
            var tpf = new TPF { Platform = TPF.TPFPlatform.PC, Encoding = 2, Flag2 = 3 };
            tpf.Textures.Add(new TPF.Texture($"s{texId:D6}", format, 0, dds, TPF.TPFPlatform.PC));
            tpfs.Add((texId, tpf.Write()));
            log.Add($"atmos: rule B2b: DS1 effect texture s{ds1Tex:D5} ({src.Format}, {Dds.Read(src.Bytes).Width}×{Dds.Read(src.Bytes).Height}) → DS2 s{texId:D6}.tpf format {format}");
        }
        foreach (var (folder, name) in new[] { ("sfx", "sfx9999res_Append.ffxbnd"), ("sfx_lq", "sfx9999lqres_Append.ffxbnd") })
        {
            var res = BND4.Read(File.ReadAllBytes(Path.Combine(pack.Ds2Game, folder, name)));
            foreach (var (texId, bytes) in tpfs)
                res.Files.Add(new BinderFile(texTemplate.Flags, texId - 2, texDir + $"s{texId:D6}.tpf", bytes) { CompressionInfo = texTemplate.CompressionInfo });
            pack.AddFile("title", $"{folder}/{name}", res.Write());
        }
        log.Add($"atmos: rule B2b: {list.Count} DS1 effect(s) in title:/sfx/sfx9999_Append.ffxbnd.dcx ({string.Join(", ", list.Select(c => $"DS1 {c.Ds1Id} → DS2 {c.Id}"))}), {tpfs.Count} texture(s) in sfx9999res_Append / sfx9999lqres_Append");
        return list.Count;
    }
}
