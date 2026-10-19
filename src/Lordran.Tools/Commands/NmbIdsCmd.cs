using System.Security.Cryptography;
using Lordran.Tools.Formats.Morpheme;
using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// nmb-ids PACKDIR [--vanilla DIR] [--id 0xID]: Morpheme asset-id check over every anibnd DS2 would load with the pack
/// installed (the vanilla anibnds, a pack file of the same name replacing its vanilla one, plus the pack's own).
/// <para>Why (session 9, track npcidle, rule I1b): DS2 shares NMB objects by asset id. The bundle loader FUN_1404e0f40
/// looks every bundle's id up in the global asset manager (FUN_1405f5aa0: one hash map keyed by the id alone) and, when
/// it is registered already, only adds a reference (FUN_1405f5c00) and drops this file's copy; the clip container is
/// named <c>MorphemeAnimContainer_%08x</c> after the network def's id (FUN_1404e1d10) and an existing container only
/// receives clips into its empty slots (FUN_1404deb10). So two anibnds that use one id for different content get
/// whichever was loaded first, and two anibnds with one network-def id share one NetworkDef and one clip set.
/// Registered types (FUN_1404e0f40): every bundle type below 13 except 7 (character controller) and 12 (file header);
/// 13 (file-name table: one id 0x3B6C719A in every vanilla NMB) is not registered by its id but named
/// <c>MorphemeFileNameLookUpTable_%08x</c> after the network def's id (0x1404e0fd4 / 0x1404e11b3), so it is shared
/// together with the network def.</para>
/// Reports (a) network-def ids used by more than one anibnd (always a problem when a pack file is one of them: one
/// NetworkDef, one clip container), (b) other registered ids whose content differs between anibnds: vanilla-only (the
/// baseline: vanilla shares 2,000+ ids this way, e.g. c1030 runs c0001's tracks), pack copies equal to a vanilla
/// anibnd's content ("as vanilla"), and new pack content under a shared id (a problem); exit 1 on any problem.
/// </summary>
public static class NmbIdsCmd
{
    public sealed record Entry(string File, bool Pack, uint Type, uint Id, string Hash, string Guid)
    {
        /// <summary>Session 11 (gest11): a vanilla anibnd's bundle whose file the pack replaces (c0001 with the DS1 gestures):
        /// not loaded, but its content is what that file's ids always held, so a pack copy equal to it counts as vanilla.</summary>
        public bool Replaced { get; init; }
    }

    /// <summary>Bundle types DS2's NMB loader registers in the global asset manager by id.</summary>
    public static bool Registered(uint type) => type < 13 && type != 7 && type != 12;

    /// <summary>Every bundle of the anibnd's NMB (type, id, MD5 of its data, GUID); empty when the file holds no NMB.</summary>
    public static List<Entry> Read(string path, bool pack) => ReadBytes(Path.GetFileName(path), File.ReadAllBytes(path), pack);

    /// <summary><see cref="Read"/> for an anibnd's bytes (build-pack checks its files before writing them).</summary>
    public static List<Entry> ReadBytes(string name, byte[] raw, bool pack)
    {
        if (DCX.Is(raw)) raw = DCX.Decompress(raw);
        IEnumerable<BinderFile> files = BND4.Is(raw) ? BND4.Read(raw).Files : BND3.Is(raw) ? BND3.Read(raw).Files : [];
        var nmbFile = files.FirstOrDefault(f => f.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase));
        if (nmbFile == null) return [];
        var nmb = NmbFile.Read(nmbFile.Bytes);
        return nmb.Bundles.Select(b => new Entry(name, pack, b.Type, b.AssetId, Convert.ToHexString(MD5.HashData(b.Data)), Convert.ToHexString(b.Guid))).ToList();
    }

    static readonly Dictionary<string, List<Entry>> vanillaEntries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The entries of one vanilla anibnd (cached per path).</summary>
    static List<Entry> VanillaEntries(string path)
    {
        lock (vanillaEntries)
        {
            if (!vanillaEntries.TryGetValue(path, out var e)) vanillaEntries[path] = e = Read(path, false);
            return e;
        }
    }

    /// <summary>
    /// build-pack's gate (chrint stage-2 fix pass, review findings 2/3: pack-wide I1b uniqueness checked at build time): the
    /// pack check of <see cref="Run"/> over the pack's anibnds (name, bytes) + the vanilla ones they don't replace. Returns
    /// the number of pack problems; every report line goes to <paramref name="log"/>.
    /// </summary>
    public static int CheckPackFiles(IEnumerable<(string Name, byte[] Bytes)> packAnibnds, string vanillaDir, Action<string> log)
    {
        var pack = packAnibnds.ToList();
        var names = pack.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var all = new List<Entry>();
        foreach (var p in Directory.GetFiles(vanillaDir, "*.anibnd.dcx", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.Ordinal))
            if (!names.Contains(Path.GetFileName(p))) all.AddRange(VanillaEntries(p));
            else all.AddRange(VanillaEntries(p).Select(e => e with { Replaced = true }));   // session 11 (gest11)
        foreach (var (n, b) in pack) all.AddRange(ReadBytes(n, b, true));
        log($"{all.Count(e => !e.Pack && e.Type == 10)} vanilla + {pack.Count} pack anibnds, {all.Count} NMB bundles");
        return PackProblems(all, log);
    }

    /// <summary>The anibnds DS2 would load: vanilla ones not replaced by a pack file of the same name, plus the pack's.</summary>
    public static List<(string Path, bool Pack)> Effective(string? packDir, string vanillaDir)
    {
        var pack = packDir == null ? [] : Directory.GetFiles(packDir, "*.anibnd.dcx", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith(".extanibnd.dcx", StringComparison.OrdinalIgnoreCase)).ToList();
        var packNames = pack.Select(p => Path.GetFileName(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = Directory.GetFiles(vanillaDir, "*.anibnd.dcx", SearchOption.TopDirectoryOnly)
            .Where(p => !packNames.Contains(Path.GetFileName(p))).OrderBy(p => p, StringComparer.Ordinal).Select(p => (p, false)).ToList();
        list.AddRange(pack.OrderBy(p => p, StringComparer.Ordinal).Select(p => (p, true)));
        return list;
    }

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: nmb-ids PACKDIR [--vanilla DIR] [--id 0xID] [--budget-all] | nmb-ids --refs DIR|FILE... | nmb-ids --diff OLD NEW"); return 2; }
        if (args[1] == "--refs") return Refs(args[2..]);
        if (args[1] == "--diff") return Diff(args[2], args[3]);
        string packDir = args[1];
        string vanilla = Path.Combine(ExtractDs2.DefaultOut, "morpheme4", "chr");
        uint? only = null;
        bool budgetAll = false;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--vanilla") vanilla = args[++i];
            else if (args[i] == "--id") only = Convert.ToUInt32(args[++i].Replace("0x", "", StringComparison.OrdinalIgnoreCase), 16);
            else if (args[i] == "--budget-all") budgetAll = true;
        }
        var eff = Effective(packDir, vanilla);
        var all = new List<Entry>();
        int replaced = Directory.GetFiles(vanilla, "*.anibnd.dcx").Length - eff.Count(e => !e.Pack);
        foreach (var (p, isPack) in eff) all.AddRange(Read(p, isPack));
        // session 11 (gest11): the vanilla files the pack replaces, as reference content only (Entry.Replaced)
        var effNames = eff.Where(e => e.Pack).Select(e => Path.GetFileName(e.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Directory.GetFiles(vanilla, "*.anibnd.dcx").Where(p => effNames.Contains(Path.GetFileName(p))))
            all.AddRange(Read(p, false).Select(e => e with { Replaced = true }));
        Console.WriteLine($"nmb-ids: {eff.Count(e => !e.Pack)} vanilla anibnds ({replaced} replaced by the pack), {eff.Count(e => e.Pack)} pack anibnds, {all.Count} NMB bundles");
        if (only != null)
        {
            foreach (var e in all.Where(e => e.Id == only)) Console.WriteLine($"  0x{e.Id:X8} type {e.Type} in {(e.Pack ? "PACK " : "")}{e.File} (data md5 {e.Hash[..8]}, guid {e.Guid.ToLowerInvariant()})");
            return 0;
        }
        int packProblems = PackProblems(all, Console.WriteLine);
        int over = Budget(packDir, eff, all.Where(e => !e.Replaced).ToList(), budgetAll, Console.WriteLine);
        Console.WriteLine(over == 0 ? "nmb-ids budget: no pack map over the pool" : $"nmb-ids budget: {over} pack maps over the pool");
        return packProblems == 0 && over == 0 ? 0 : 1;
    }

    /// <summary>
    /// DS2's asset-manager entry pool (rule I1p, V-code): the constructor FUN_1405f4e00 (DarkSoulsII.c l.1113297) makes it
    /// with FUN_1405f5dd0(…, {0x20, 4}, 0x3000) = 12,288 fixed entries (free count +0x14); FUN_1405f65c0 (l.1114496), which
    /// the NMB loader FUN_1404e0f40 (l.956912) calls for every bundle it registers, takes one, and with none left writes
    /// through NULL (<c>*(char*)(entry+0x1c) = 1</c>, l.1114523); FUN_1405f6f00 (l.1114896, from the unload at l.957046
    /// when the refcount FUN_1405f5260 reaches 0) gives it back. One entry per distinct registered id resident at once.
    /// Registered = a handler exists (FUN_1405f6300 registrations l.1123915-1123922: types 10, 1, 2, 4, 3, 5, 7, 11) and the
    /// loader does not skip it (7, 12): of the types NMBs hold (1, 2, 3, 4, 7, 10, 12, 13) exactly 1, 2, 3, 4, 10.
    /// </summary>
    public const int AssetPool = 0x3000;

    /// <summary>
    /// The pool budget per map (chrint stage-2 fix pass, review finding 1): the distinct registered ids of c0001 (resident
    /// from boot) + every anibnd the map's generators name (GeneratorParam +8 → GeneratorRegistParam → EnemyParam.animId /
    /// 100 = the anibnd, e.g. 250100 → c2501, 800100 → c8001), the pack's file where it has one. Maps: those with a pack
    /// GeneratorRegistParam (with <paramref name="all"/>: every vanilla map too, the baseline). WARN above 80 %, OVER at
    /// 100 %; returns the number of pack maps OVER. A lower bound: summons / invaders (c0001), event-spawned characters
    /// and a neighbouring map still resident are not counted.
    /// </summary>
    static int Budget(string packDir, List<(string Path, bool Pack)> eff, List<Entry> entries, bool all, Action<string> log)
    {
        string vparam = Path.Combine(ExtractDs2.DefaultOut, "param"), pparam = Path.Combine(packDir, "gamedata", "param");
        var packMaps = Directory.Exists(pparam) ? Directory.GetFiles(pparam, "generatorregistparam_*.param").Select(MapOf).ToHashSet() : [];
        var maps = new SortedSet<string>(packMaps, StringComparer.Ordinal);
        // session 11 (burg11), rule I1p-v: every vanilla map is counted too (logged with --budget-all only): the largest vanilla
        // roster DS2 ships (Shrine of Winter m20_21: 164 % by this whole-roster count) shows the count over-estimates what is
        // resident at once; a pack map is OVER only above both the pool and that vanilla maximum (WARN above the pool)
        var vanillaMaps = Directory.GetFiles(vparam, "generatorregistparam_*.param").Select(MapOf).ToHashSet();
        foreach (var vm in vanillaMaps) maps.Add(vm);
        if (maps.Count == 0) { log("budget: no map"); return 0; }
        string title = Path.Combine(packDir, "title");
        var store = ParamStore.LoadDs2(File.Exists(Path.Combine(title, "enc_regulation.bnd.dcx")) ? title : Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        var enemy = store.Params["EnemyParam"];
        var ids = entries.Where(e => Registered(e.Type)).GroupBy(e => e.File, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Id).ToHashSet(), StringComparer.OrdinalIgnoreCase);
        var packFiles = eff.Where(e => e.Pack).Select(e => Path.GetFileName(e.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int over = 0, warn = 0, maxPct = 0; string maxMap = "";
        var counted = new List<(string Map, int Count, bool IsPack, string Line)>();
        foreach (string map in maps)
        {
            string P(string kind) { string p = Path.Combine(pparam, $"{kind}_{map}.param"); return File.Exists(p) ? p : Path.Combine(vparam, $"{kind}_{map}.param"); }
            if (!File.Exists(P("generatorparam")) || !File.Exists(P("generatorregistparam"))) continue;
            if (Formats.ParamFiles.Rowless(P("generatorparam")) || Formats.ParamFiles.Rowless(P("generatorregistparam"))) continue; // session 11 (burg11): no characters
            var gen = RawParam(P("generatorparam"));
            var registIds = gen.Rows.Select(r => BitConverter.ToInt32((byte[])r["data"].Value, 8)).ToHashSet();
            var reg = RawParam(P("generatorregistparam"));   // raw: some vanilla rows carry bits the def drops; EnemyId = s32 at +0
            var roster = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "c0001.anibnd.dcx" };
            var missing = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var r in reg.Rows.Where(r => registIds.Contains(r.ID)))
            {
                var e = enemy[BitConverter.ToInt32((byte[])r["data"].Value, 0)];
                int anim = e == null ? 0 : Convert.ToInt32(e["animId"].Value);
                if (anim <= 0) continue;
                string a = $"c{anim / 100:D4}.anibnd.dcx";
                if (ids.ContainsKey(a)) roster.Add(a); else missing.Add($"animId {anim}");
            }
            var union = new HashSet<uint>();
            foreach (var a in roster) union.UnionWith(ids[a]);
            bool isPack = packMaps.Contains(map);
            counted.Add((map, union.Count, isPack, $"{string.Join(" ", roster.Select(a => (packFiles.Contains(a) ? "PACK " : "") + a[..5]))}{(missing.Count > 0 ? $"; no anibnd for {string.Join(", ", missing)}" : "")}"));
        }
        int vanillaMax = counted.Where(c => !c.IsPack && vanillaMaps.Contains(c.Map)).Select(c => c.Count).DefaultIfEmpty(0).Max();
        int limit = Math.Max(AssetPool, vanillaMax);
        foreach (var (map, count, isPack, roster) in counted)
        {
            int pct = (int)Math.Round(100.0 * count / AssetPool);
            string state = isPack && count > limit ? "OVER" : count >= AssetPool ? (isPack ? "WARN (over the pool, within vanilla's largest roster)" : "over the pool (vanilla)") : count > AssetPool * 8 / 10 ? "WARN" : "ok";
            if (state == "OVER") over++;
            if (state.StartsWith("WARN")) warn++;
            if (pct > maxPct) (maxPct, maxMap) = (pct, map);
            if (isPack || all)
                log($"budget {map}{(isPack ? " (pack)" : "")}: {count} / {AssetPool} pool entries ({pct} %) {state}: {roster}");
        }
        log($"budget: vanilla's largest whole-roster count {vanillaMax} ({(int)Math.Round(100.0 * vanillaMax / AssetPool)} %) = the OVER limit for pack maps (rule I1p-v)");
        log($"budget: {maps.Count} maps, highest {maxPct} % ({maxMap}), {warn} WARN (> 80 %), {over} pack maps OVER");
        return over;
    }

    /// <summary>A param with one raw "data" field per row (as spot-check's).</summary>
    static PARAM RawParam(string path)
    {
        var p = PARAM.Read(path);
        var blob = new PARAMDEF { ParamType = p.ParamType, DataVersion = p.ParamdefDataVersion, Unicode = true, FormatVersion = 104, BigEndian = false };
        blob.Fields.Add(new PARAMDEF.Field(blob, PARAMDEF.DefType.dummy8, "data") { ArrayLength = (int)p.DetectedSize });
        p.ApplyParamdef(blob);
        return p;
    }

    static string MapOf(string paramPath) => Path.GetFileNameWithoutExtension(paramPath)["generatorregistparam_".Length..];

    /// <summary>The pack problems of <see cref="Run"/>'s checks (a) and (b) over the effective entries.</summary>
    public static int PackProblems(List<Entry> all, Action<string> log)
    {
        int packProblems = 0;
        var reference = all.Where(e => e.Replaced).ToList();   // session 11 (gest11): replaced vanilla files: content reference only
        all = all.Where(e => !e.Replaced).ToList();
        // (a) network defs (type 10) shared between anibnds: one NetworkDef + one MorphemeAnimContainer for all of them
        var nets = all.Where(e => e.Type == 10).GroupBy(e => e.Id).Where(g => g.Count() > 1).ToList();
        int vanillaNets = 0;
        foreach (var g in nets)
        {
            bool pack = g.Any(e => e.Pack);
            if (pack) packProblems++; else vanillaNets++;
            if (pack) log($"SHARED NETWORK DEF 0x{g.Key:X8}: {string.Join(", ", g.Select(e => (e.Pack ? "PACK " : "") + e.File))}");
        }
        log($"network defs: {all.Count(e => e.Type == 10)}, distinct ids {all.Where(e => e.Type == 10).Select(e => e.Id).Distinct().Count()}; shared: {nets.Count} ({vanillaNets} vanilla-only)");
        // (b) registered ids whose content differs between anibnds: the first loaded wins for everybody (c0001, the
        // player's, is resident from boot, so it always wins its ids). Vanilla itself does this (the baseline: e.g. c1021,
        // c1030, c1240 … hold c0001's event-track ids with other content and so run the player's tracks). A pack copy is
        // a problem only when its content under the id is new (no vanilla anibnd holds the id with that content): then
        // what it was built with is not what it gets (always, if c0001 holds the id; else by load order), or it hands its
        // own content to the vanilla characters that load after it. Pack content equal to a vanilla anibnd's shares
        // exactly as that vanilla character does ("as vanilla").
        var conflicts = all.Where(e => Registered(e.Type) && e.Type != 10).GroupBy(e => e.Id)
            .Where(g => g.Select(e => (e.Type, e.Hash)).Distinct().Count() > 1).ToList();
        int vanillaOnly = 0, asVanilla = 0, shown = 0;
        var byFile = new SortedDictionary<string, SortedDictionary<uint, int>>(StringComparer.OrdinalIgnoreCase);
        var vanillaTypes = new SortedDictionary<uint, int>();
        foreach (var g in conflicts)
        {
            if (!g.Any(e => e.Pack))
            {
                vanillaOnly++;
                foreach (var t in g.Select(e => e.Type).Distinct()) vanillaTypes[t] = vanillaTypes.GetValueOrDefault(t) + 1;
                continue;
            }
            var vanillaVariants = g.Where(e => !e.Pack).Select(e => (e.Type, e.Hash)).ToHashSet();
            vanillaVariants.UnionWith(reference.Where(e => e.Id == g.Key).Select(e => (e.Type, e.Hash)));
            var fresh = g.Where(e => e.Pack && !vanillaVariants.Contains((e.Type, e.Hash))).ToList();
            if (fresh.Count == 0) { asVanilla++; continue; }
            packProblems++;
            foreach (var e in fresh)
            {
                if (!byFile.TryGetValue(e.File, out var t)) byFile[e.File] = t = new SortedDictionary<uint, int>();
                t[e.Type] = t.GetValueOrDefault(e.Type) + 1;
            }
            var resident = g.FirstOrDefault(e => !e.Pack && e.File.Equals("c0001.anibnd.dcx", StringComparison.OrdinalIgnoreCase));
            if (shown++ < 25)
            {
                var variants = g.GroupBy(e => (e.Type, e.Hash)).Select(v => $"[type {v.Key.Type} md5 {v.Key.Hash[..8]}: {string.Join(" ", v.Select(e => (e.Pack ? "PACK " : "") + e.File).Distinct())}]");
                log($"CONTENT CONFLICT 0x{g.Key:X8} ({(resident != null ? "c0001 holds it: the pack copy always gets the player's" : "load order decides")}): {string.Join(" ", variants)}");
            }
        }
        if (shown > 25) log($"  … {shown - 25} more");
        foreach (var (file, t) in byFile) log($"  PACK {file}: new content under shared ids, by bundle type {string.Join(", ", t.Select(kv => $"{kv.Key}: {kv.Value}"))}");
        log($"registered ids (not network defs) with different content: {conflicts.Count} = {vanillaOnly} vanilla-only (baseline{(vanillaTypes.Count > 0 ? ": by type " + string.Join(", ", vanillaTypes.Select(kv => $"{kv.Key}: {kv.Value}")) : "")}) + {asVanilla} where the pack copies equal a vanilla anibnd's + {conflicts.Count - vanillaOnly - asVanilla} with new pack content");
        log(packProblems == 0 ? "nmb-ids: 0 pack problems" : $"nmb-ids: {packProblems} pack problems");
        return packProblems;
    }

    /// <summary>
    /// nmb-ids --diff OLD NEW (anibnd files or folders of them, matched by name): what rule I1b changed. Per pair: binder
    /// files other than the NMB byte-identical; NMB bundles in the same order and types; per bundle the id kept or renamed
    /// (renamed ids must be new to OLD); every non-network bundle's data identical; the network bundle identical to OLD's
    /// once its id fields (<see cref="NmbIdentity.IdFields"/>) are mapped back ("only ids") — else its node counts are shown.
    /// </summary>
    static int Diff(string oldP, string newP)
    {
        var pairs = Directory.Exists(oldP)
            ? Directory.GetFiles(oldP, "*.anibnd.dcx").OrderBy(f => f, StringComparer.Ordinal).Select(f => (f, Path.Combine(newP, Path.GetFileName(f)))).ToList()
            : [(oldP, newP)];
        int bad = 0;
        foreach (var (a, b) in pairs)
        {
            byte[] ra = File.ReadAllBytes(a), rb = File.ReadAllBytes(b);
            if (DCX.Is(ra)) ra = DCX.Decompress(ra);
            if (DCX.Is(rb)) rb = DCX.Decompress(rb);
            var ba = BND4.Read(ra); var bb = BND4.Read(rb);
            var notes = new List<string>();
            bool filesSame = ba.Files.Count == bb.Files.Count && ba.Files.Zip(bb.Files).All(p => p.First.Name == p.Second.Name && p.First.ID == p.Second.ID
                && (p.First.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase) || p.First.Bytes.AsSpan().SequenceEqual(p.Second.Bytes)));
            if (!filesSame) notes.Add("BINDER FILES DIFFER");
            var na = NmbFile.Read(ba.Files.First(f => f.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)).Bytes);
            var nb = NmbFile.Read(bb.Files.First(f => f.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)).Bytes);
            if (na.Bundles.Count != nb.Bundles.Count || na.Bundles.Zip(nb.Bundles).Any(p => p.First.Type != p.Second.Type)) { notes.Add("BUNDLE LAYOUT DIFFERS"); bad++; Console.WriteLine($"{Path.GetFileName(a)}: {string.Join("; ", notes)}"); continue; }
            var oldIds = na.Bundles.Select(x => x.AssetId).ToHashSet();
            var back = new Dictionary<uint, uint>();
            int kept = 0, dataDiff = 0, notFresh = 0;
            var renamed = new SortedDictionary<uint, int>();
            for (int i = 0; i < na.Bundles.Count; i++)
            {
                var (x, y) = (na.Bundles[i], nb.Bundles[i]);
                if (x.AssetId == y.AssetId) kept++;
                else { back[y.AssetId] = x.AssetId; renamed[y.Type] = renamed.GetValueOrDefault(y.Type) + 1; if (oldIds.Contains(y.AssetId)) notFresh++; }
                if (y.Type != 10 && !x.Data.AsSpan().SequenceEqual(y.Data)) dataDiff++;
            }
            int ni = nb.IndexOf(10);
            byte[] xn = (byte[])nb.Bundles[ni].Data.Clone();
            foreach (var (o, _) in NmbIdentity.IdFields(xn))
                if (back.TryGetValue(BitConverter.ToUInt32(xn, o), out uint v)) BitConverter.GetBytes(v).CopyTo(xn, o);
            bool netSame = xn.AsSpan().SequenceEqual(na.Bundles[ni].Data);
            string net = netSame ? "network = OLD's but for the id fields (only ids)"
                : $"network differs beyond ids: nodes {new NmbNetwork(na.Bundles[ni].Data).NodeCount} → {new NmbNetwork(xn).NodeCount}, {na.Bundles[ni].Data.Length} → {xn.Length} bytes";
            if (dataDiff > 0) notes.Add($"{dataDiff} non-network bundles changed data");
            if (notFresh > 0) notes.Add($"{notFresh} new ids already used in OLD");
            if (dataDiff > 0 || notFresh > 0 || !filesSame) bad++;
            Console.WriteLine($"{Path.GetFileName(a)}: {na.Bundles.Count} bundles, {kept} ids kept, renamed by type {string.Join(", ", renamed.Select(kv => $"{kv.Key}: {kv.Value}"))}; binder files {(filesSame ? "identical but the NMB" : "DIFFER")}; non-network data {(dataDiff == 0 ? "identical" : "DIFFERS")}; {net}{(notes.Count > 0 ? "; " + string.Join("; ", notes) : "")}");
        }
        return bad == 0 ? 0 : 1;
    }

    /// <summary>
    /// nmb-ids --refs DIR|FILE...: where a network def names other bundles (session 10, rule I1b, <see cref="NmbIdentity"/>).
    /// Per NMB: every id field of <see cref="NmbIdentity.IdFields"/> (attribs 20 rig, 21 +0x78 channel map, 23 event tracks,
    /// 43 controller) must name a bundle of this NMB of the right type; no bundle id may occur at any other 4-aligned
    /// position of the network bundle ("stray") or inside another bundle's data ("cross"). Exit 0 when all hold.
    /// </summary>
    static int Refs(string[] paths)
    {
        var files = new List<string>();
        foreach (var p in paths)
            if (Directory.Exists(p)) files.AddRange(Directory.GetFiles(p, "*anibnd.dcx", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.Ordinal));
            else files.Add(p);
        int nmbs = 0, bad = 0, stray = 0, cross = 0, none43 = 0, shown = 0;
        var perAttrib = new SortedDictionary<int, int>();
        foreach (var f in files)
            foreach (var nmb in NmbIdentity.Nmbs(File.ReadAllBytes(f)))
            {
                int ni = nmb.IndexOf(10);
                if (ni < 0) continue;
                nmbs++;
                var byId = new Dictionary<uint, NmbFile.Bundle>();
                foreach (var b in nmb.Bundles) byId.TryAdd(b.AssetId, b);
                var x = nmb.Bundles[ni].Data;
                var fields = NmbIdentity.IdFields(x);
                var at = new HashSet<int>();
                foreach (var (o, a) in fields)
                {
                    at.Add(o);
                    uint v = BitConverter.ToUInt32(x, o);
                    if (a == 43 && v == 0) { none43++; continue; }
                    if (byId.TryGetValue(v, out var b) && NmbIdentity.FieldType(a, b.Type)) { perAttrib[a] = perAttrib.GetValueOrDefault(a) + 1; continue; }
                    bad++;
                    if (shown++ < 20) Console.WriteLine($"BAD {Path.GetFileName(f)}: attrib {a} @0x{o:X} names 0x{v:X8} ({(b == null ? "no bundle" : $"type {b.Type}")})");
                }
                for (int o = 0; o + 4 <= x.Length; o += 4)
                {
                    if (at.Contains(o)) continue;
                    uint v = BitConverter.ToUInt32(x, o);
                    if (v == 0 || !byId.TryGetValue(v, out var b)) continue;
                    stray++;
                    if (shown++ < 20) Console.WriteLine($"STRAY {Path.GetFileName(f)}: network @0x{o:X} holds 0x{v:X8} (type-{b.Type} bundle id)");
                }
                foreach (var b in nmb.Bundles.Where(b => b.Type != 10))
                    for (int o = 0; o + 4 <= b.Data.Length; o += 4)
                    {
                        uint v = BitConverter.ToUInt32(b.Data, o);
                        if (v == 0 || v == b.AssetId || !byId.TryGetValue(v, out var c)) continue;
                        cross++;
                        if (shown++ < 20) Console.WriteLine($"CROSS {Path.GetFileName(f)}: type-{b.Type} bundle 0x{b.AssetId:X8} @0x{o:X} holds 0x{v:X8} (type-{c.Type} bundle id)");
                    }
            }
        Console.WriteLine($"nmb-ids --refs: {files.Count} files, {nmbs} NMBs; id fields resolving to the right bundle type by attrib {string.Join(", ", perAttrib.Select(kv => $"{kv.Key}: {kv.Value}"))} (43 = 0: {none43}); bad {bad}, stray {stray}, cross {cross}");
        return bad == 0 && stray == 0 && cross == 0 ? 0 : 1;
    }
}
