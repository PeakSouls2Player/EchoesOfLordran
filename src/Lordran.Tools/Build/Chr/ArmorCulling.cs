using Lordran.Tools.Formats;
using SoulsFormats;
using static Lordran.Tools.Build.ParamCells;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// Session 11 (track outfit11), rule AR8 "display groups": DS1's cross-piece hiding on DS2's culling groups.
/// <para>DS1 (`outfit-survey --groups/--flags`): a part mesh whose material is named "#NN#" belongs to display group NN and is
/// hidden while any equipped piece (any slot, the bare-body rows 900000-903000 for an empty slot) has
/// EquipParamProtector.invisibleFlagNN: gauntlets (flags 43-46) hide a body's sleeve ends (#43-#46), long bodies (37-42) the
/// legs' tops (#37-#42, in 84 male leg parts), bodies (14-19, 24, 25, 28) a helm's lower rim, helms (20-25, 30, 31) a body's
/// hood/collar and (0-13, 15) DS1's hair / face. DS2 does the same with 64 culling groups: a part material's GX list item
/// ID 1 / 102 holds the group (data[0]; 0 = always drawn), EquipInterfereParam.cullingGroupNN_0/_1 (male/female) of an
/// equipped piece hides it (face/hair parts use 28, 29, 51-63).</para>
/// <para>Rule: every (DS1 group, carrying slot) pair gets its own DS2 group (Mappings\armor-culling.tsv, made by
/// `outfit-survey --culling-map` from the DS1/DS2 data, never edited by hand): DS2's face/hair groups and 0 excluded;
/// hard: DS2's naked row of every slot hides it exactly when DS1's bare-body row of that slot hides the DS1 group (an empty
/// slot behaves as in DS1); preferred: a DS2 group DS2's own parts of the same slot carry and DS2's pieces of the slots that
/// hide it in DS1 hide (a vanilla DS2 piece worn with a DS1 piece then hides / keeps the mesh as its own kind would). The
/// converted part's mesh goes into that group; the piece's own interfere row (id = its ArmorParam id) = AR4's vanilla row
/// (DS2 face/hair groups, neck/elbow/waist seams, groups AR8 doesn't map) with every mapped group set exactly from the
/// piece's DS1 invisibleFlags (both sexes). Within DS1 pieces the hiding is then DS1's 1:1.</para>
/// </summary>
public static class ArmorCulling
{
    public static string MapPath => Path.Combine(AppContext.BaseDirectory, "Mappings", "armor-culling.tsv");
    static readonly string[] Prefix = ["HD", "BD", "AM", "LG"];

    /// <summary>(DS1 group, slot 0-3) → DS2 culling group.</summary>
    public static Dictionary<(int Group, int Slot), int> Load()
    {
        var map = new Dictionary<(int, int), int>();
        if (!File.Exists(MapPath)) return map;
        foreach (var c in File.ReadLines(MapPath).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split('\t')).Where(c => c.Length >= 3 && int.TryParse(c[0], out _)))
            map[(int.Parse(c[0]), Array.IndexOf(Prefix, c[1]))] = int.Parse(c[2]);
        return map;
    }

    /// <summary>Free DS2 groups AR8 gave to a DS1 group an empty slot hides: (DS2 group, slots whose naked rows set it).</summary>
    public static List<(int Group, List<int> Slots)> LoadNaked() => !File.Exists(MapPath) ? [] :
        File.ReadLines(MapPath).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split('\t'))
            .Where(c => c.Length >= 8 && c[7].StartsWith("naked:"))
            .Select(c => (int.Parse(c[2]), c[7]["naked:".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => Array.IndexOf(Prefix, p)).ToList())).ToList();

    /// <summary>AR8 for an empty slot: DS2's naked rows (male and female, ArmorParam 11001100+slot / 11002100+slot) get the
    /// free groups DS1's bare row of that slot hides. Free groups: no vanilla part carries them and no vanilla row sets them, so
    /// nothing vanilla changes look. Returns the rows changed.</summary>
    public static List<string> ApplyNaked(Pack pack)
    {
        var done = new List<string>();
        foreach (var (g, slots) in LoadNaked())
            foreach (int s in slots)
                foreach (int armorId in new[] { Naked(s), Naked(s) + 1000 })
                {
                    // female naked pieces point at row 0 (shared with ArmorParam 0-2300, no player piece): free groups only, harmless
                    int rowId = I(pack.Param("ArmorParam")[armorId]!, "interfereId");
                    if (pack.Param("EquipInterfereParam")[rowId] is not { } row) continue;
                    foreach (int sex in new[] { 0, 1 }) Set(row, $"cullingGroup{g:D2}_{sex}", 1);
                    done.Add($"EquipInterfereParam {rowId} (naked {Prefix[s]} {armorId}) group {g}");
                }
        return done;
    }

    /// <summary>The DS2 groups <see cref="ApplyNaked"/> may set in vanilla rows (the checks allow exactly these).</summary>
    public static HashSet<int> NakedGroups() => LoadNaked().Select(n => n.Group).ToHashSet();

    /// <summary>A DS1 part material's display group ("#NN#..."), or -1.</summary>
    public static int Ds1Group(string? materialName)
    {
        var m = System.Text.RegularExpressions.Regex.Match(materialName ?? "", @"^#(\d+)#");
        return m.Success ? int.Parse(m.Groups[1].Value) : -1;
    }

    /// <summary>A DS2 part material's culling group: its GX list's item ID 1 / 102, first data byte (0 = always drawn).</summary>
    public static int Ds2Group(FLVER2 f, FLVER2.Material m) =>
        m.GXIndex >= 0 && m.GXIndex < f.GXLists.Count && f.GXLists[m.GXIndex].FirstOrDefault(i => i.ID == "1" && i.Unk04 == 102) is { Data.Length: > 0 } it ? it.Data[0] : 0;

    /// <summary>A copy of a GX list with its culling item (ID 1 / 102) set to <paramref name="group"/>.</summary>
    public static FLVER2.GXList WithGroup(FLVER2.GXList template, int group)
    {
        var l = new FLVER2.GXList();
        foreach (var i in template)
        {
            var data = (byte[])i.Data.Clone();
            if (i.ID == "1" && i.Unk04 == 102 && data.Length > 0) data[0] = (byte)group;
            l.Add(new FLVER2.GXItem(i.ID, i.Unk04, data));
        }
        return l;
    }

    /// <summary>DS1 EquipParamProtector invisibleFlag00-47 set on a row.</summary>
    public static List<int> Ds1Flags(PARAM.Row r) => Enumerable.Range(0, 48).Where(i => r[$"invisibleFlag{i:D2}"] is { } c && Convert.ToInt32(c.Value) != 0).ToList();

    /// <summary>DS2 naked ArmorParam ids per slot (male).</summary>
    static int Naked(int slot) => 11001100 + slot;

    /// <summary>The rule's evidence and the map (writes armor-culling.tsv to <paramref name="outPath"/>).</summary>
    public static List<string> Compute(Ds1Source ds1, ParamStore ds2, string ds2Data, string outPath)
    {
        var log = new List<string>();
        // DS1 carriers: (group, slot) → parts
        var carry = new Dictionary<(int G, int S), int>();
        foreach (string p in Directory.GetFiles(Path.Combine(ds1.Root, "parts"), "*.partsbnd.dcx"))
        {
            int s = Array.IndexOf(Prefix, Path.GetFileName(p)[..2].ToUpperInvariant());
            if (s < 0) continue;
            foreach (var bf in BND3.Read(p).Files.Where(x => x.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)))
                foreach (int g in FLVER2.Read(bf.Bytes).Materials.Select(m => Ds1Group(m.Name)).Where(g => g >= 0).Distinct())
                    carry[(g, s)] = carry.GetValueOrDefault((g, s)) + 1;
        }
        // DS1 hiders: group → slot → pieces; bare rows
        var protector = ds1.Params.Params["EquipParamProtector"];
        var hide = new Dictionary<int, Dictionary<int, int>>();
        foreach (var r in protector.Rows.Where(r => r.ID % 1000 == 0 && ArmorItems.IsItem(ds1, r.ID)))
            foreach (int f in Ds1Flags(r))
            {
                var d = hide.TryGetValue(f, out var x) ? x : hide[f] = new();
                d[r.ID / 1000 % 10] = d.GetValueOrDefault(r.ID / 1000 % 10) + 1;
            }
        var bare = Enumerable.Range(0, 4).Select(s => Ds1Flags(protector[900000 + s * 1000]!).ToHashSet()).ToArray();
        // DS2 carriers / hiders / naked
        var ds2Carry = new Dictionary<int, HashSet<int>>();
        var faceGroups = new HashSet<int> { 0 };
        foreach (var (folder, s) in new[] { ("head", 0), ("body", 1), ("arm", 2), ("leg", 3), ("face", -1) })
            foreach (string p in Directory.GetFiles(Path.Combine(ds2Data, "model", "parts", folder), "*.bnd").Where(p => !p.EndsWith("_l.bnd")))
            {
                BND4 b;
                try { b = BND4.Read(p); } catch { continue; }
                foreach (var bf in b.Files.Where(x => x.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase)))
                {
                    var f = FLVER2.Read(bf.Bytes);
                    foreach (int g in f.Materials.Select(m => Ds2Group(f, m)))
                    {
                        if (s < 0) faceGroups.Add(g);
                        else (ds2Carry.TryGetValue(g, out var h) ? h : ds2Carry[g] = new()).Add(s);
                    }
                }
            }
        var inter = ds2.Params["EquipInterfereParam"];
        var ds2Hide = new Dictionary<int, HashSet<int>>();
        foreach (var a in ds2.Params["ArmorParam"].Rows)
            if (inter[I(a, "interfereId")] is { } r)
                for (int g = 0; g < 64; g++)
                    if (I(r, $"cullingGroup{g:D2}_0") != 0) (ds2Hide.TryGetValue(g, out var h) ? h : ds2Hide[g] = new()).Add(I(a, "armorCategory") - 1);
        var ds2Bare = Enumerable.Range(0, 4).Select(s => inter[I(ds2.Params["ArmorParam"][Naked(s)]!, "interfereId")] is { } r
            ? Enumerable.Range(0, 64).Where(g => I(r, $"cullingGroup{g:D2}_0") != 0).ToHashSet() : new HashSet<int>()).ToArray();
        log.Add($"DS1 bare rows hide: {string.Join(" | ", bare.Select((b, s) => $"{Prefix[s]} {string.Join(",", b.Order())}"))}");
        log.Add($"DS2 naked rows hide: {string.Join(" | ", ds2Bare.Select((b, s) => $"{Prefix[s]} {string.Join(",", b.Order())}"))}");
        log.Add($"DS2 face/hair groups (excluded): {string.Join(",", faceGroups.Order())}");
        // groups no vanilla part carries and no vanilla row hides: AR8 may set them in DS2's naked rows (no vanilla effect)
        var free = Enumerable.Range(1, 63).Where(x => !faceGroups.Contains(x) && !ds2Carry.ContainsKey(x) && !ds2Hide.ContainsKey(x)).ToList();
        var nakedRows = Enumerable.Range(0, 4).Select(s => I(ds2.Params["ArmorParam"][Naked(s)]!, "interfereId")).ToArray();
        log.Add($"DS2 free groups: {string.Join(",", free)}; naked interfere rows {string.Join(",", nakedRows)} (female {string.Join(",", Enumerable.Range(0, 4).Select(s => I(ds2.Params["ArmorParam"][Naked(s) + 1000]!, "interfereId")))}), shared with " +
            string.Join(",", ds2.Params["ArmorParam"].Rows.Where(a => nakedRows.Contains(I(a, "interfereId")) || I(a, "interfereId") == 0).Select(a => $"{a.ID}→{I(a, "interfereId")}")) +
            $"; EquipInterfereParam row 0 exists: {inter[0] != null}");

        var used = new HashSet<int>(faceGroups);
        var lines = new List<string>
        {
            "# Session 11 (outfit11), rule AR8: DS1 display group (material #NN#) on a part of slot SLOT -> DS2 culling group. Generated by",
            "# `lr outfit-survey --culling-map` from DS1 parts/params and vanilla DS2 parts/params; do not edit by hand.",
            "# ds1_group\tslot\tds2_group\tds1_parts\tds1_hidden_by\tds2_carried_by\tds2_hidden_by\tnote",
        };
        foreach (var ((g, s), n) in carry.OrderByDescending(x => x.Value).ThenBy(x => x.Key.G).ThenBy(x => x.Key.S))
        {
            var hiders = hide.GetValueOrDefault(g) ?? new();
            // an empty slot h (≠ the part's own: an empty slot has no part of ours) hides the mesh as DS1's bare row does
            bool BareOk(int x) => Enumerable.Range(0, 4).Where(h => h != s).All(h => ds2Bare[h].Contains(x) == bare[h].Contains(g));
            var bareSlots = Enumerable.Range(0, 4).Where(h => h != s && bare[h].Contains(g)).ToList();
            int Score(int x) =>
                (ds2Carry.GetValueOrDefault(x)?.Contains(s) == true ? 8 : 0)
                + hiders.Keys.Count(h => ds2Hide.GetValueOrDefault(x)?.Contains(h) == true) * 4
                - (ds2Hide.GetValueOrDefault(x)?.Count(h => !hiders.ContainsKey(h)) ?? 0) * 2   // DS2 slots that would hide it but DS1's don't
                - (ds2Carry.GetValueOrDefault(x)?.Count(c => c != s) ?? 0);                     // DS2 meshes of other slots our rows would hide
            var candidates = Enumerable.Range(1, 63).Where(x => !used.Contains(x) && !free.Contains(x)).ToList();
            var hard = candidates.Where(BareOk).ToList();
            string note = "";
            int pick;
            if (hard.Count > 0) pick = hard.OrderByDescending(Score).ThenBy(x => x).First();
            else if (free.FirstOrDefault(x => !used.Contains(x)) is int fx and > 0)
            {
                // no DS2 group is hidden by exactly DS1's empty slots: a free group, set in DS2's naked rows of those slots
                pick = fx;
                note = $"naked:{string.Join(",", bareSlots.Select(h => Prefix[h]))}";
            }
            else { pick = candidates.OrderByDescending(Score).ThenBy(x => x).First(); note = "no DS2 group matches the bare rows: best effort"; }
            used.Add(pick);
            string Set(IEnumerable<int> xs) => string.Join(",", xs.Order().Select(x => Prefix[x]));
            lines.Add($"{g}\t{Prefix[s]}\t{pick}\t{n}\t{string.Join(",", hiders.OrderBy(h => h.Key).Select(h => $"{Prefix[h.Key]}×{h.Value}"))}{(Enumerable.Range(0, 4).Any(h => bare[h].Contains(g)) ? " bare:" + Set(Enumerable.Range(0, 4).Where(h => bare[h].Contains(g))) : "")}\t{Set(ds2Carry.GetValueOrDefault(pick) ?? new())}\t{Set(ds2Hide.GetValueOrDefault(pick) ?? new())}\t{note}");
        }
        File.WriteAllLines(outPath, lines);
        log.AddRange(lines);
        return log;
    }

    /// <summary>
    /// AR8 interfere row for a ported piece: a copy of AR4's vanilla row (<paramref name="vanillaId"/>) as row
    /// <paramref name="rowId"/> with every mapped DS2 group set from the DS1 flags. Returns the groups it set.
    /// </summary>
    public static List<int> InterfereRow(Pack pack, Dictionary<(int Group, int Slot), int> map, int vanillaId, int rowId, IReadOnlyCollection<int> ds1Flags, string name)
    {
        var row = pack.AddRow("EquipInterfereParam", pack.Param("EquipInterfereParam")[vanillaId]!, rowId, name);
        var on = new SortedSet<int>();
        foreach (var ((g, _), x) in map)
        {
            bool hidden = ds1Flags.Contains(g);
            // a DS2 group several DS1 pairs never share (one pair per group), so the DS1 flag decides it
            foreach (int sex in new[] { 0, 1 }) Set(row, $"cullingGroup{x:D2}_{sex}", hidden ? 1 : 0);
            if (hidden) on.Add(x);
        }
        return on.ToList();
    }
}
