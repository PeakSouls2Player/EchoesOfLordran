using System.Text;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Session 11 (burg11, round 8), rule H2: several DS1 maps packed into one DS2 host (Mappings\ds1-maps.tsv: the rows of one
/// host; the first built row is the PRIMARY, the others are merged into it). The pipeline builds one DS2 map from one DS1
/// map, so the merge happens on the DS1 side: the primary's DS1 files are served as the union of the group's files through a
/// virtual file layer (<see cref="SFUtil.ReadHook"/>: SoulsFile / BXF3 path reads; <see cref="Exists"/> for the few
/// File.Exists sites), and every module then sees ONE DS1 map:
///  - H2-M  MSB: the secondaries' models, parts, regions and events are appended to the primary's. Map-piece, collision
///          and navmesh models are renamed to free numbers (m/h/n 8000+, block digit kept: their files are looked up by
///          name); a part, region or event whose name the primary already uses gets the next free index (NAME_nnnn) or a
///          suffix; every name reference (models, collisions, move points, event parts/regions, generators, ObjActs, spawn
///          points, navmesh regions) follows. The ConnectCollisions between members are dropped (one map now); a
///          secondary's MapOffset events are dropped (all DS1 maps share world coordinates: checked equal).
///  - H2-E  entities: a secondary's entity AAB xxxx (e.g. Blighttown 140 0110) becomes the primary's AAB with the same local
///          number when the primary does not use it, else the first free local of the same thousand (the type digit:
///          0/3 characters, 1 objects, 2 regions) from n500 up — so every rule that derives ids from entity % 10000
///          (instances, generators, EventLocation, hit regions) stays collision-free inside the host.
///  - H2-V  event script: the secondaries' events are appended (ids are per-map 1AAB… and never collide; asserted); event 0
///          and 50 bodies are concatenated. Entity arguments (an argument whose EMEDF name contains "Entity", or is
///          Character / Object) and event parameters that reach one (fixpoint through 2000:0 initialisations, common.emevd
///          included) are remapped by H2-E.
///  - H2-B  binders: collision (h/l hkxbhd), navmesh (nvmbnd) and talk (talkesdbnd) union with the renamed entries;
///          map-piece flvers are served from the secondary's folder under their new names.
/// Flags need no merge: FlagRules accepts the whole group's AABs for the primary (<see cref="Aabs"/>) and stacks their
/// temporary lists in the host's blocks (rule F2, row order). The pack records the groups (lordran-h2\merge.tsv) so the
/// checks (separate processes) activate the same layer (<see cref="LoadFrom"/>).
/// NOT merged (logged, later work): DS1 draw params (a secondary's LightID / FogID … index its own area's banks; the merged
/// map reads the primary's area), per-area SFX / sound banks of the secondary's area.
/// </summary>
public static class H2Merge
{
    public sealed record Group(string Primary, IReadOnlyList<string> Secondaries);

    static readonly Dictionary<string, Group> byMember = new(StringComparer.OrdinalIgnoreCase); // "mAA_BB" of every member
    static readonly Dictionary<string, Lazy<byte[]>> files = new(StringComparer.OrdinalIgnoreCase); // virtual rel path → bytes
    static readonly Dictionary<int, int> entityMap = [];
    static readonly Dictionary<(string Primary, string Part), string> partMember = []; // (primary "mAA_BB", merged secondary part) → its DS1 map
    static string root = "";
    public static readonly List<string> Report = [];

    public static bool Active => byMember.Count > 0;
    public static IReadOnlyCollection<Group> Groups => byMember.Values.Distinct().ToList();
    /// <summary>Rule H2-E: a secondary's entity → its id in the merged map (other ids unchanged).</summary>
    public static IReadOnlyDictionary<int, int> EntityMap => entityMap;
    public static int Entity(int e) => entityMap.TryGetValue(e, out int m) ? m : e;

    static readonly Dictionary<string, int[]> bitMaps = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Rule H2-G: a member map's DS1 draw-group words in the merged map's bit space (identity for a primary or an
    /// unmerged map).</summary>
    public static uint[] MapGroups(string ds1Map, uint[] groups)
    {
        if (!bitMaps.TryGetValue(ds1Map[..6], out var map)) return (uint[])groups.Clone();
        var r = new uint[4];
        for (int b = 0; b < 128; b++)
            if ((groups[b / 32] >> (b % 32) & 1) != 0 && map[b] >= 0) r[map[b] / 32] |= 1u << (map[b] % 32);
        return r;
    }
    static void MapGroupsInPlace(string ds1Map, uint[] groups) { var r = MapGroups(ds1Map, groups); Array.Copy(r, groups, 4); }

    static readonly Dictionary<string, int> drawOffsets = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Rule H2-D: the id offset of a secondary's draw-param rows (and its parts' LightID / FogID …) in the merged map.</summary>
    public static int DrawOffset(string secondary) => drawOffsets.GetValueOrDefault(secondary[..6]);

    /// <summary>Reads without the layer (global DS1 surveys that must see every map as shipped).</summary>
    public static T Original<T>(Func<T> read)
    {
        var hook = SFUtil.ReadHook;
        SFUtil.ReadHook = null;
        try { return read(); } finally { SFUtil.ReadHook = hook; }
    }

    /// <summary>Rule H2-P: the member DS1 map a part of the merged MSB came from (<paramref name="ds1Map"/> itself for its
    /// own parts and for unmerged maps). Rules that look at "this part's own map" (bonfire places, ...) keep to its member.</summary>
    public static string MemberOf(string ds1Map, string partName) => partMember.GetValueOrDefault((ds1Map[..6], partName), ds1Map);

    /// <summary>Rule H2-V2: the member rank of a DS1 event id / flag 1AABnnnn (0 primary, 1.. secondaries), null when its AAB
    /// is none of the group's.</summary>
    public static int? FlagMemberRank(string ds1Map, long id)
    {
        if (id < 10_000_000 || id >= 100_000_000 || GroupOf(ds1Map) is not { } g) return null;
        string aab = id.ToString().Substring(1, 3);
        if (aab == Aab(g.Primary)) return 0;
        int k = g.Secondaries.ToList().FindIndex(x => Aab(x) == aab);
        return k < 0 ? null : k + 1;
    }

    /// <summary>Rule H2-P: 0 for the primary's own parts, 1.. for the k-th secondary's (orderings that number things per map
    /// keep the primary's numbers: bonfire ids, ...).</summary>
    public static int MemberRank(string ds1Map, string partName) =>
        partMember.TryGetValue((ds1Map[..6], partName), out var s) && GroupOf(ds1Map) is { } g ? 1 + g.Secondaries.ToList().FindIndex(x => x.Equals(s, StringComparison.OrdinalIgnoreCase)) : 0;

    /// <summary>The primary a secondary DS1 map is merged into (full id), or null (not a secondary).</summary>
    public static string? PrimaryOf(string ds1Map) =>
        byMember.TryGetValue(ds1Map[..6], out var g) && !g.Primary.StartsWith(ds1Map[..6], StringComparison.OrdinalIgnoreCase) ? g.Primary : null;

    public static bool IsSecondary(string ds1Map) => PrimaryOf(ds1Map) != null;

    /// <summary>The group of a member (primary or secondary), or null.</summary>
    public static Group? GroupOf(string ds1Map) => byMember.GetValueOrDefault(ds1Map[..6]);

    /// <summary>Flag AABs ("100", "140") whose flags are the DS1 map's own: its group's when it is a primary.</summary>
    public static IEnumerable<string> Aabs(string ds1Map)
    {
        yield return Aab(ds1Map);
        if (byMember.TryGetValue(ds1Map[..6], out var g) && g.Primary.StartsWith(ds1Map[..6], StringComparison.OrdinalIgnoreCase))
            foreach (var s in g.Secondaries) yield return Aab(s);
    }

    static string Aab(string m) => m.Substring(1, 2) + m.Substring(4, 2)[1..];
    static int AabInt(string m) => int.Parse(Aab(m));

    /// <summary>File.Exists for the DS1 paths the merged map adds (renamed map-piece flvers).</summary>
    public static bool Exists(string path) => File.Exists(path) || Virtual(path) != null;

    /// <summary>File.ReadAllBytes through the layer.</summary>
    public static byte[] ReadAllBytes(string path) => Virtual(path) ?? File.ReadAllBytes(path);

    static byte[]? Virtual(string path)
    {
        if (files.Count == 0 || root.Length == 0) return null;
        string full;
        try { full = Path.GetFullPath(path); } catch { return null; }
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
        string rel = full[root.Length..].Replace('\\', '/').TrimStart('/');
        return files.TryGetValue(rel, out var f) ? f.Value : null;
    }

    /// <summary>Records the groups in the pack (lordran-h2\merge.tsv) for the checks.</summary>
    public static void Save(Pack pack)
    {
        var sb = new StringBuilder("# rule H2 (session 11 burg11): primary DS1 map <TAB> secondary DS1 map merged into it (H2Merge)\n");
        foreach (var g in Groups) foreach (var s in g.Secondaries) sb.Append($"{g.Primary}\t{s}\n");
        pack.AddFile("lordran-h2", "merge.tsv", Encoding.UTF8.GetBytes(sb.ToString()));
    }

    /// <summary>Activates the layer a pack was built with (a command given the pack folder); false when it has none.</summary>
    public static bool LoadFrom(string dsrRoot, string packDir)
    {
        string f = Path.Combine(packDir, "lordran-h2", "merge.tsv");
        if (!File.Exists(f) || Active) return false;
        var pairs = File.ReadLines(f).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split('\t')).Where(c => c.Length >= 2).Select(c => (c[0], c[1])).ToList();
        if (pairs.Count == 0) return false;
        Activate(dsrRoot, pairs);
        return true;
    }

    /// <summary>Builds the merged DS1 files of every group and installs the read hook.</summary>
    public static void Activate(string dsrRoot, IEnumerable<(string Primary, string Secondary)> pairs)
    {
        root = Path.GetFullPath(dsrRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        foreach (var grp in pairs.GroupBy(p => p.Primary, StringComparer.OrdinalIgnoreCase))
        {
            var g = new Group(grp.Key, grp.Select(p => p.Secondary).ToList());
            byMember[g.Primary[..6]] = g;
            foreach (var s in g.Secondaries) byMember[s[..6]] = g;
        }
        SFUtil.ReadHook = Virtual;
        foreach (var g in Groups) Merge(dsrRoot, g);
    }

    // ------------------------------------------------------------------------------------------------ the merge
    static void Merge(string dsr, Group g)
    {
        string p = g.Primary, pAa = p.Substring(1, 2);
        // the original files (read before any virtual file of this group exists)
        string MsbPath(string m) => Path.Combine(dsr, "map", "MapStudio", m + ".msb");
        var msb = MSB1.Read(MsbPath(p));
        var emevdPath = Path.Combine(dsr, "event", p + ".emevd.dcx");
        var emevd = EMEVD.Read(emevdPath);
        var talkPath = Path.Combine(dsr, "script", "talk", p + ".talkesdbnd.dcx");
        var talk = File.Exists(talkPath) ? BND3.Read(talkPath) : null;
        string HPath(string m, char k, string ext) => Path.Combine(dsr, "map", m, $"{k}{m[1..]}.{ext}");
        var hi = BXF3.Read(HPath(p, 'h', "hkxbhd"), HPath(p, 'h', "hkxbdt"));
        var lo = BXF3.Read(HPath(p, 'l', "hkxbhd"), HPath(p, 'l', "hkxbdt"));
        var nvm = BND3.Read(Path.Combine(dsr, "map", p, p + ".nvmbnd.dcx"));
        int pAab = AabInt(p);

        // ---- H2-G (session 11 burg11, round 9, rule DG1): every member's draw-group bits in one 128-bit space. A member's
        // bits are first reduced to classes (bits that every part of the member carries alike — in DrawGroups and in
        // DispGroups — are interchangeable: any piece/collision test gives the same answer), then the members' classes take
        // consecutive bits, primary first. Too many classes in all: the member's two most alike classes (fewest parts
        // telling them apart) merge until it fits (over-draw, never a hole).
        var secondaryMsbs = g.Secondaries.ToDictionary(x => x, x => MSB1.Read(MsbPath(x)), StringComparer.OrdinalIgnoreCase);
        var members = new[] { (Name: p, Msb: msb) }.Concat(secondaryMsbs.Select(kv => (Name: kv.Key, Msb: kv.Value))).ToList();
        var memberClasses = new List<List<(List<int> Bits, HashSet<int> Parts)>>();
        foreach (var (mName, mMsb) in members)
        {
            var parts = mMsb.Parts.GetEntries().Where(x => x is not MSB1.Part.ConnectCollision).ToList();
            var sig = new Dictionary<int, HashSet<int>>(); // bit → parts carrying it (2i draw, 2i+1 disp)
            for (int pi = 0; pi < parts.Count; pi++)
                foreach (var (arr, k) in new[] { (parts[pi].DrawGroups, 0), (parts[pi].DispGroups, 1) })
                    for (int b = 0; b < 128; b++)
                        if ((arr[b / 32] >> (b % 32) & 1) != 0) (sig.TryGetValue(b, out var set) ? set : sig[b] = []).Add(2 * pi + k);
            memberClasses.Add(sig.GroupBy(kv => string.Join(",", kv.Value.Order())).Select(gr => (Bits: gr.Select(kv => kv.Key).ToList(), Parts: gr.First().Value)).ToList());
        }
        // quotas: every member keeps its classes when they fit; else a share of 128 by class count (largest remainders)
        int total = memberClasses.Sum(c => c.Count);
        var quota = memberClasses.Select(c => c.Count).ToArray();
        if (total > 128)
        {
            var exact = memberClasses.Select(c => 128.0 * c.Count / total).ToArray();
            quota = exact.Select(e => Math.Max(1, (int)Math.Floor(e))).ToArray();
            foreach (int k in Enumerable.Range(0, quota.Length).OrderByDescending(k => exact[k] - Math.Floor(exact[k])))
                if (quota.Sum() < 128) quota[k]++;
        }
        int nextBit = 0;
        for (int mi = 0; mi < members.Count; mi++)
        {
            var classes = memberClasses[mi];
            int before = classes.Count, merged = 0;
            while (classes.Count > quota[mi] && classes.Count > 1)
            {
                (int A, int B, int D) best = (0, 1, int.MaxValue);
                for (int a = 0; a < classes.Count; a++)
                    for (int b = a + 1; b < classes.Count; b++)
                    {
                        int d = classes[a].Parts.Count + classes[b].Parts.Count - 2 * classes[a].Parts.Intersect(classes[b].Parts).Count();
                        if (d < best.D) best = (a, b, d);
                    }
                classes[best.A] = (classes[best.A].Bits.Concat(classes[best.B].Bits).ToList(), classes[best.A].Parts.Union(classes[best.B].Parts).ToHashSet());
                classes.RemoveAt(best.B);
                merged++;
            }
            var map = Enumerable.Repeat(-1, 128).ToArray();
            int first = nextBit;
            foreach (var c in classes) { foreach (int b in c.Bits) map[b] = nextBit; nextBit++; }
            bitMaps[members[mi].Name[..6]] = map;
            Report.Add($"rule H2-G: {members[mi].Name}: {classes.Sum(c => c.Bits.Count)} draw-group bits in {before} classes{(merged > 0 ? $", {merged} merged to fit (over-draw)" : "")} → merged bits {first}..{nextBit - 1}");
        }
        foreach (var x in msb.Parts.GetEntries().Where(x => x is not MSB1.Part.ConnectCollision)) { MapGroupsInPlace(p, x.DrawGroups); MapGroupsInPlace(p, x.DispGroups); }

        int? nextDrawId = null;
        var areaOffsets = new Dictionary<string, int>();
        foreach (string s in g.Secondaries)
        {
            string sAa = s.Substring(1, 2);
            int sAab = AabInt(s);
            var sm = secondaryMsbs[s];
            foreach (var x in sm.Parts.GetEntries().Where(x => x is not MSB1.Part.ConnectCollision)) { MapGroupsInPlace(s, x.DrawGroups); MapGroupsInPlace(s, x.DispGroups); }
            var log = new List<string>();

            // ---- H2-M models: map pieces / collisions / navmeshes renamed to free numbers (block digit kept)
            var modelRename = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var usedNum = new Dictionary<char, HashSet<int>>();
            foreach (var m in msb.Models.GetEntries().Concat(sm.Models.GetEntries()))
                if (m.Name.Length >= 5 && int.TryParse(m.Name.AsSpan(1, 4), out int n))
                    (usedNum.TryGetValue(char.ToLowerInvariant(m.Name[0]), out var set) ? set : usedNum[char.ToLowerInvariant(m.Name[0])] = []).Add(n);
            string NewModel(string old)
            {
                char k = char.ToLowerInvariant(old[0]);
                var set = usedNum.TryGetValue(k, out var x) ? x : usedNum[k] = [];
                int n = 8000;
                while (set.Contains(n)) n++;
                if (n > 9999) throw new InvalidDataException($"rule H2-M: no free {k} model number for {s} {old}");
                set.Add(n);
                return $"{old[0]}{n:D4}{old[5..]}";
            }
            foreach (var m in sm.Models.MapPieces) modelRename[m.Name] = NewModel(m.Name);
            foreach (var m in sm.Models.Collisions) modelRename[m.Name] = NewModel(m.Name);
            foreach (var m in sm.Models.Navmeshes) modelRename[m.Name] = NewModel(m.Name);
            foreach (var m in sm.Models.MapPieces) { m.Name = modelRename[m.Name]; msb.Models.MapPieces.Add(m); }
            foreach (var m in sm.Models.Collisions) { m.Name = modelRename[m.Name]; msb.Models.Collisions.Add(m); }
            foreach (var m in sm.Models.Navmeshes) { m.Name = modelRename[m.Name]; msb.Models.Navmeshes.Add(m); }
            var pModels = msb.Models.GetEntries().Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var m in sm.Models.Objects.Where(m => !pModels.Contains(m.Name))) msb.Models.Objects.Add(m);
            foreach (var m in sm.Models.Enemies.Where(m => !pModels.Contains(m.Name))) msb.Models.Enemies.Add(m);
            foreach (var m in sm.Models.Players.Where(m => !pModels.Contains(m.Name))) msb.Models.Players.Add(m);
            log.Add($"rule H2-M: {s} → {p}: {modelRename.Count} map-piece / collision / navmesh models renamed ({string.Join(", ", modelRename.Take(3).Select(kv => $"{kv.Key}→{kv.Value}"))} …)");

            // ---- H2-M names of parts, regions, events
            Dictionary<string, string> Rename<T>(IEnumerable<T> pEntries, IEnumerable<T> sEntries, Func<T, string> name, Func<T, string?> prefixRename)
            {
                var used = pEntries.Select(name).ToHashSet(StringComparer.Ordinal);
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var e in sEntries)
                {
                    string old = name(e), n = prefixRename(e) ?? old;
                    if (used.Contains(n))
                    {
                        int us = n.LastIndexOf('_');
                        if (us > 0 && n.Length - us - 1 == 4 && int.TryParse(n.AsSpan(us + 1), out int idx))
                        {
                            string stem = n[..(us + 1)];
                            while (used.Contains(n)) n = $"{stem}{++idx:D4}";
                        }
                        else { int k = 1; string b = n; while (used.Contains(n)) n = $"{b}_h2{k++}"; }
                    }
                    used.Add(n);
                    map[old] = n;
                }
                return map;
            }
            var partRename = Rename(msb.Parts.GetEntries(), sm.Parts.GetEntries(), x => x.Name,
                x => modelRename.TryGetValue(x.ModelName, out var nm) && x.Name.StartsWith(x.ModelName, StringComparison.OrdinalIgnoreCase) ? nm + x.Name[x.ModelName.Length..] : null);
            var regionRename = Rename(msb.Regions.GetEntries(), sm.Regions.GetEntries(), x => x.Name, _ => null);
            var eventRename = Rename(msb.Events.GetEntries(), sm.Events.GetEntries(), x => x.Name, _ => null);
            string? P(string? n) => n == null ? null : partRename.GetValueOrDefault(n, n);
            string? R(string? n) => n == null ? null : regionRename.GetValueOrDefault(n, n);
            int renamedParts = partRename.Count(kv => kv.Key != kv.Value), renamedRegions = regionRename.Count(kv => kv.Key != kv.Value), renamedEvents = eventRename.Count(kv => kv.Key != kv.Value);

            // ---- H2-E entities
            var pLocals = new HashSet<int>();
            void Own(int e) { if (e > 0 && e / 10000 == pAab) pLocals.Add(e % 10000); }
            foreach (var x in msb.Parts.GetEntries()) Own(x.EntityID);
            foreach (var x in msb.Regions.GetEntries()) Own(x.EntityID);
            foreach (var x in msb.Events.GetEntries()) Own(x.EntityID);
            var sEntities = sm.Parts.GetEntries().Select(x => x.EntityID).Concat(sm.Regions.GetEntries().Select(x => x.EntityID))
                .Concat(sm.Events.GetEntries().Select(x => x.EntityID)).Where(e => e > 0 && e / 10000 == sAab).Distinct().Order().ToList();
            var taken = new HashSet<int>(pLocals);
            var keep = sEntities.Where(e => !pLocals.Contains(e % 10000)).ToList();
            foreach (var e in keep) { taken.Add(e % 10000); entityMap[e] = pAab * 10000 + e % 10000; }
            int moved = 0;
            foreach (var e in sEntities.Where(e => pLocals.Contains(e % 10000)))
            {
                int block = e % 10000 / 1000 * 1000;
                int local = Enumerable.Range(500, 500).Concat(Enumerable.Range(0, 500)).Select(i => block + i).FirstOrDefault(l => !taken.Contains(l), -1);
                if (local < 0) throw new InvalidDataException($"rule H2-E: no free local in {block}..{block + 999} for {s} entity {e}");
                taken.Add(local);
                entityMap[e] = pAab * 10000 + local;
                moved++;
            }
            log.Add($"rule H2-E: {s}: {sEntities.Count} entities → {p}'s AAB {pAab} ({sEntities.Count - moved} keep their local number, {moved} moved to a free local of their thousand)");

            // ---- apply to the secondary's entries, then append
            int sOffsets = 0;
            var pOffset = msb.Events.MapOffsets.FirstOrDefault();
            foreach (var mo in sm.Events.MapOffsets)
                if (pOffset == null ? mo.Position != System.Numerics.Vector3.Zero || mo.Degree != 0 : mo.Position != pOffset.Position || mo.Degree != pOffset.Degree)
                    throw new InvalidDataException($"rule H2-M: {s}'s MapOffset {mo.Position} / {mo.Degree} differs from {p}'s: the maps do not share world coordinates");
                else sOffsets++;
            sm.Events.MapOffsets.Clear();
            // ---- H2-D draw params: the secondary's ids move above every id of the primary's area banks
            // (one block per AREA: two secondaries of one area — Blighttown + Demon Ruins, a14 — share theirs; the primary's
            // own area needs none)
            int pMax = new Ds1DrawParam(dsr, pAa).Banks.Values.SelectMany(b => b.Rows).Max(r => r.ID);
            int sMax = new Ds1DrawParam(dsr, sAa).Banks.Values.SelectMany(b => b.Rows).Max(r => r.ID);
            nextDrawId ??= pMax + 1;
            int off = sAa == pAa ? 0 : areaOffsets.TryGetValue(sAa, out int known) ? known : nextDrawId.Value;
            if (sMax + off > 255) throw new InvalidDataException($"rule H2-D: {s}'s draw-param ids (max {sMax}) do not fit at +{off} in a byte");
            if (sAa != pAa && !areaOffsets.ContainsKey(sAa)) { areaOffsets[sAa] = off; nextDrawId = off + sMax + 1; }
            drawOffsets[s[..6]] = off;
            static byte B(byte v, int o) => (byte)(v + o);
            int pEnvCount = msb.Events.Environments.Count;
            foreach (var x in sm.Parts.GetEntries())
            {
                x.LightID = B(x.LightID, off); x.FogID = B(x.FogID, off); x.ScatterID = B(x.ScatterID, off); x.LensFlareID = B(x.LensFlareID, off);
                x.ShadowID = B(x.ShadowID, off); x.DofID = B(x.DofID, off); x.ToneMapID = B(x.ToneMapID, off); x.ToneCorrectID = B(x.ToneCorrectID, off);
                x.LanternID = B(x.LanternID, off);
            }
            log.Add($"rule H2-D: {s}: draw-param ids +{off} (area {sAa} rows 0..{sMax} after area {pAa}'s 0..{pMax})");
            foreach (var x in sm.Parts.GetEntries())
            {
                x.Name = partRename[x.Name];
                if (modelRename.TryGetValue(x.ModelName, out var nm)) x.ModelName = nm;
                x.EntityID = Entity(x.EntityID);
                switch (x)
                {
                    case MSB1.Part.ObjectBase o: o.CollisionName = P(o.CollisionName); break;
                    case MSB1.Part.EnemyBase en:
                        en.CollisionName = P(en.CollisionName);
                        for (int i = 0; i < en.MovePointNames.Length; i++) en.MovePointNames[i] = R(en.MovePointNames[i])!;
                        break;
                    case MSB1.Part.Collision c:
                        if (c.EnvLightMapSpotIndex >= 0) c.EnvLightMapSpotIndex = (short)(c.EnvLightMapSpotIndex + pEnvCount); // the k-th Environment event: after the primary's
                        c.DisableBonfireEntityID = Entity(c.DisableBonfireEntityID);
                        for (int i = 0; i < c.VagrantEntityIDs.Length; i++) c.VagrantEntityIDs[i] = Entity(c.VagrantEntityIDs[i]);
                        break;
                    case MSB1.Part.ConnectCollision cc: cc.CollisionName = P(cc.CollisionName); break;
                }
            }
            foreach (var x in sm.Regions.GetEntries()) { x.Name = regionRename[x.Name]; x.EntityID = Entity(x.EntityID); }
            // the play regions follow their invasion points (DS1: region AAB·1000 + n ↔ point entity AAB·10000 + 4000 + n, rule
            // NET2): a point moved by H2-E takes its region along; a region without a point keeps its id (no other map has it)
            int regionsMoved = 0;
            foreach (var c in sm.Parts.Collisions.Where(c => c.PlayRegionID > 0 && c.PlayRegionID / 1000 == sAab))
                if (entityMap.TryGetValue(sAab * 10000 + 4000 + c.PlayRegionID % 1000, out int pt))
                { int r = pt / 10000 * 1000 + pt % 1000; if (r != c.PlayRegionID) regionsMoved++; c.PlayRegionID = r; }
            if (regionsMoved > 0) log.Add($"rule H2-E: {s}: {regionsMoved} collision play-region references follow their invasion points");
            foreach (var x in sm.Events.GetEntries())
            {
                x.Name = eventRename[x.Name];
                x.PartName = P(x.PartName); x.RegionName = R(x.RegionName); x.EntityID = Entity(x.EntityID);
                switch (x)
                {
                    case MSB1.Event.Treasure t: t.TreasurePartName = P(t.TreasurePartName); break;
                    case MSB1.Event.Generator gen:
                        for (int i = 0; i < gen.SpawnPointNames.Length; i++) gen.SpawnPointNames[i] = R(gen.SpawnPointNames[i])!;
                        for (int i = 0; i < gen.SpawnPartNames.Length; i++) gen.SpawnPartNames[i] = P(gen.SpawnPartNames[i])!;
                        break;
                    case MSB1.Event.ObjAct oa: oa.ObjActPartName = P(oa.ObjActPartName); oa.ObjActEntityID = Entity(oa.ObjActEntityID); break;
                    case MSB1.Event.SpawnPoint sp: sp.SpawnPointName = R(sp.SpawnPointName); break;
                    case MSB1.Event.Navmesh nv: nv.NavmeshRegionName = R(nv.NavmeshRegionName); break;
                    case MSB1.Event.PseudoMultiplayer pm: pm.HostEntityID = Entity(pm.HostEntityID); break;
                }
            }
            // the connects between members join two halves of one map now
            static bool To(MSB1.Part.ConnectCollision c, string m) => c.MapID.Length >= 2 && c.MapID[0] == int.Parse(m.Substring(1, 2)) && c.MapID[1] == int.Parse(m.Substring(4, 2));
            // rule H2-G: a dropped connect's groups (the neighbour member's, in its merged bits) join its collision's DispGroups:
            // standing there still shows what DS1 showed of the neighbour
            int ored = 0;
            void Join(MSB1 owner, MSB1.Part.ConnectCollision c, string target)
            {
                if (owner.Parts.Collisions.FirstOrDefault(x => x.Name == c.CollisionName) is not { } col) return;
                var add = MapGroups(target, c.DispGroups);
                for (int i = 0; i < 4; i++) col.DispGroups[i] |= add[i];
                ored++;
            }
            foreach (var c in msb.Parts.ConnectCollisions.Where(c => To(c, s)).ToList()) Join(msb, c, s);
            foreach (var c in sm.Parts.ConnectCollisions.ToList())
                if (g.Secondaries.Append(p).FirstOrDefault(m => To(c, m)) is { } tgt) Join(sm, c, tgt);
            if (ored > 0) log.Add($"rule H2-G: {s}: {ored} connects between members joined into their collisions' DispGroups");
            int droppedP = msb.Parts.ConnectCollisions.RemoveAll(c => To(c, s));
            int droppedS = sm.Parts.ConnectCollisions.RemoveAll(c => g.Secondaries.Append(p).Any(m => To(c, m)));
            foreach (var x in sm.Parts.GetEntries()) { msb.Parts.Add(x); partMember[(p[..6], x.Name)] = s; }
            foreach (var x in sm.Regions.GetEntries()) msb.Regions.Add(x);
            foreach (var x in sm.Events.GetEntries()) msb.Events.Add(x);
            log.Add($"rule H2-M: {s}: {sm.Parts.GetEntries().Count} parts ({renamedParts} renamed), {sm.Regions.GetEntries().Count} regions ({renamedRegions} renamed), {sm.Events.GetEntries().Count} events ({renamedEvents} renamed) appended; {droppedP} + {droppedS} ConnectCollisions between the members dropped; {sOffsets} MapOffset event(s) equal to {p}'s dropped");

            // ---- H2-V event script
            var sPath = Path.Combine(dsr, "event", s + ".emevd.dcx");
            if (File.Exists(sPath))
            {
                var se = EMEVD.Read(sPath);
                var ids = emevd.Events.Select(e => e.ID).ToHashSet();
                int appended = 0;
                // the constructors: the secondary's event initialisations (2000:0) go after ALL the primary's (the end of its
                // event 50, created when missing), so the primary's events keep their translated group numbers; event 0's
                // other instructions (start-disabled collisions, bonfire registration …) stay in event 0
                var p0 = emevd.Events.First(e => e.ID == 0);
                var p50 = emevd.Events.FirstOrDefault(e => e.ID == 50);
                if (p50 == null) { p50 = new EMEVD.Event(50, p0.RestBehavior); emevd.Events.Insert(emevd.Events.IndexOf(p0) + 1, p50); ids.Add(50); }
                foreach (var ev in se.Events.Where(e => e.ID is 0 or 50).OrderBy(e => e.ID))
                {
                    if (ev.Parameters.Count > 0) throw new InvalidDataException($"rule H2-V: {s} event {ev.ID} has parameters");
                    var inits = ev.Instructions.Where(x => (x.Bank, x.ID) is (2000, 0)).ToList();
                    p50.Instructions.AddRange(inits);
                    p0.Instructions.AddRange(ev.Instructions.Where(x => !inits.Contains(x)));
                    log.Add($"rule H2-V: {s} event {ev.ID}: {inits.Count} event initialisations after {p}'s (end of event 50), {ev.Instructions.Count - inits.Count} other instructions appended to {p}'s event 0");
                }
                foreach (var ev in se.Events)
                {
                    if (ev.ID is 0 or 50) continue;
                    if (!ids.Add(ev.ID)) throw new InvalidDataException($"rule H2-V: event {ev.ID} of {s} exists in {p} too");
                    emevd.Events.Add(ev);
                    appended++;
                }
                log.Add($"rule H2-V: {s}: {appended} events appended");
            }

            // ---- H2-B binders
            string Renamed(string entryName, char kind)
            {
                string f = Path.GetFileName(entryName);                // h0004B0A14.hkx.dcx
                string model = $"{kind}{f.Substring(1, 6)}";            // h0004B0
                string key = kind == 'l' ? $"h{f.Substring(1, 6)}" : model;
                if (!modelRename.TryGetValue(key, out var nm)) return "";
                string rest = f[7..];                                   // A14.hkx.dcx
                if (rest.StartsWith($"A{sAa}", StringComparison.OrdinalIgnoreCase)) rest = $"A{pAa}" + rest[3..];
                return Path.Combine(Path.GetDirectoryName(entryName) ?? "", $"{kind}{nm[1..]}{rest}").Replace($"{s}", p, StringComparison.OrdinalIgnoreCase);
            }
            void Union(IBinder target, IBinder src, char kind, string what)
            {
                int n = 0, skipped = 0, id = target.Files.Count == 0 ? 0 : target.Files.Max(f => f.ID) + 1;
                foreach (var f in src.Files)
                {
                    string name = Renamed(f.Name, kind);
                    if (name.Length == 0) { skipped++; continue; }
                    target.Files.Add(new BinderFile(f.Flags, id++, name, f.Bytes));
                    n++;
                }
                log.Add($"rule H2-B: {what}: {n} entries of {s} added{(skipped > 0 ? $", {skipped} without a model in the MSB left out" : "")}");
            }
            var shi = BXF3.Read(HPath(s, 'h', "hkxbhd"), HPath(s, 'h', "hkxbdt"));
            var slo = BXF3.Read(HPath(s, 'l', "hkxbhd"), HPath(s, 'l', "hkxbdt"));
            Union(hi, shi, 'h', "collision (h)");
            Union(lo, slo, 'l', "collision (l)");
            Union(nvm, BND3.Read(Path.Combine(dsr, "map", s, s + ".nvmbnd.dcx")), 'n', "navmesh");
            var sTalk = Path.Combine(dsr, "script", "talk", s + ".talkesdbnd.dcx");
            if (File.Exists(sTalk))
            {
                talk ??= new BND3();
                var names = talk.Files.Select(f => Path.GetFileName(f.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                int id = talk.Files.Count == 0 ? 0 : talk.Files.Max(f => f.ID) + 1, n = 0;
                foreach (var f in BND3.Read(sTalk).Files.Where(f => names.Add(Path.GetFileName(f.Name))))
                { talk.Files.Add(new BinderFile(f.Flags, id++, f.Name, f.Bytes)); n++; }
                log.Add($"rule H2-B: talk: {n} talk ESDs of {s} added");
            }
            // map-piece flvers under their new names
            foreach (var (old, nm) in modelRename.Where(kv => kv.Key[0] is 'm' or 'M'))
            {
                string real = Path.Combine(dsr, "map", s, $"{old}A{sAa}.flver.dcx");
                if (File.Exists(real)) files[$"map/{p}/{nm}A{pAa}.flver.dcx"] = new Lazy<byte[]>(() => File.ReadAllBytes(real));
            }
            Report.AddRange(log);
        }

        // ---- H2-V entity arguments of the merged script
        RemapEntities(dsr, emevd, out string remapNote);
        Report.Add($"rule H2-V: {p}: {remapNote}");

        var msbBytes = msb.Write();
        files[$"map/MapStudio/{p}.msb"] = new Lazy<byte[]>(() => msbBytes);
        var emevdBytes = emevd.Write();
        files[$"event/{p}.emevd.dcx"] = new Lazy<byte[]>(() => emevdBytes);
        if (talk != null) { var tb = talk.Write(); files[$"script/talk/{p}.talkesdbnd.dcx"] = new Lazy<byte[]>(() => tb); }
        foreach (var (bxf, k) in new[] { (hi, 'h'), (lo, 'l') })
        {
            bxf.Write(out byte[] bhd, out byte[] bdt);
            files[$"map/{p}/{k}{p[1..]}.hkxbhd"] = new Lazy<byte[]>(() => bhd);
            files[$"map/{p}/{k}{p[1..]}.hkxbdt"] = new Lazy<byte[]>(() => bdt);
        }
        var nvmBytes = nvm.Write();
        files[$"map/{p}/{p}.nvmbnd.dcx"] = new Lazy<byte[]>(() => nvmBytes);
    }

    /// <summary>Rule H2-V: entity arguments and the event parameters that reach them, remapped by <see cref="EntityMap"/>.</summary>
    static void RemapEntities(string dsr, EMEVD emevd, out string note)
    {
        var defs = Commands.EmevdDump.DefsWithArgNames();
        // argument byte offsets of every instruction (natural alignment, as EmevdTranslator.Decode)
        var layout = new Dictionary<(int, int), List<(int Off, int Size, bool Entity)>>();
        foreach (var (key, d) in defs)
        {
            var l = new List<(int, int, bool)>();
            int off = 0;
            for (int i = 0; i < d.Types.Length; i++)
            {
                int t = d.Types[i], size = t is 0 or 3 ? 1 : t is 1 or 4 ? 2 : 4;
                off = (off + size - 1) / size * size;
                string n = d.ArgNames[i];
                bool named = n.Contains("Entity", StringComparison.OrdinalIgnoreCase) && !n.Contains("Type", StringComparison.OrdinalIgnoreCase)
                    || n is "Character" or "Object" or "Entity";
                bool ent = size == 4 && (t == 2 || t == 5) && named;
                l.Add((off, size, ent));
                off += size;
            }
            layout[key] = l;
        }
        bool IsEntityArg(EMEVD.Instruction x, long byteOff) =>
            layout.TryGetValue((x.Bank, x.ID), out var l) && l.Any(a => a.Off == byteOff && a.Entity);
        // events (this script + common.emevd): which parameter source bytes reach an entity argument (fixpoint over 2000:0)
        var all = emevd.Events.ToList();
        string common = Path.Combine(dsr, "event", "common.emevd.dcx");
        if (File.Exists(common)) all.AddRange(EMEVD.Read(common).Events.Where(e => !emevd.Events.Any(x => x.ID == e.ID)));
        var byId = all.GroupBy(e => e.ID).ToDictionary(g => g.Key, g => g.First());
        var entityParams = new HashSet<(long Event, long Src)>();
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var ev in all)
                foreach (var prm in ev.Parameters)
                {
                    if (prm.InstructionIndex < 0 || prm.InstructionIndex >= ev.Instructions.Count || entityParams.Contains((ev.ID, prm.SourceStartByte))) continue;
                    var x = ev.Instructions[(int)prm.InstructionIndex];
                    bool ent = IsEntityArg(x, prm.TargetStartByte)
                        || (x.Bank, x.ID) is (2000, 0) && prm.TargetStartByte >= 8 && x.ArgData.Length >= 8 && entityParams.Contains((BitConverter.ToUInt32(x.ArgData, 4), prm.TargetStartByte - 8));
                    if (ent && entityParams.Add((ev.ID, prm.SourceStartByte))) changed = true;
                }
        }
        int args = 0, inits = 0;
        void Fix(byte[] a, int off, ref int count)
        {
            if (off + 4 > a.Length) return;
            int v = BitConverter.ToInt32(a, off);
            if (entityMap.TryGetValue(v, out int m)) { BitConverter.GetBytes(m).CopyTo(a, off); count++; }
        }
        foreach (var ev in emevd.Events)
            foreach (var x in ev.Instructions)
            {
                if (layout.TryGetValue((x.Bank, x.ID), out var l))
                    foreach (var a in l.Where(a => a.Entity)) Fix(x.ArgData, a.Off, ref args);
                if ((x.Bank, x.ID) is (2000, 0) && x.ArgData.Length >= 8)
                {
                    long callee = BitConverter.ToUInt32(x.ArgData, 4);
                    foreach (var (_, src) in entityParams.Where(ep => ep.Event == callee)) Fix(x.ArgData, 8 + (int)src, ref inits);
                }
            }
        note = $"{args} entity arguments + {inits} event-initialisation entity values remapped (rule H2-E; {entityParams.Count} entity parameters found)";
    }
}
