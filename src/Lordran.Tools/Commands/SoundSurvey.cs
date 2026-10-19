using System.Text.RegularExpressions;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// sound-survey [DS2_DATA] — evidence for the map sound rules SND1-SND4 (session 10, track asy-sound): every DS1 map's
/// MSB1 Sound events (DS1 type letter, region shape, entity, which DSR FMOD project defines the event, whether an EMEVD
/// 2010:3 switches it and whether DS1 has it off at map start: <see cref="Build.Events.MapSounds.OffAtStart(EMEVD)"/>), every DS1 2010:2 Play SE (entity kind, type letter,
/// defining project, whether the 'a'-letter alias name is free in DS1 and DS2), every 2006:3/2006:4 one-shot SFX, and DS2's
/// own MSB2 Sound regions (type, shape, key UnkT08) for comparison. Event parameters are substituted from every 2000:0
/// initialiser of the event (DSR stores byte args in 4-byte slots).
/// </summary>
public static class SoundSurvey
{
    /// <summary>DS1's sound type letters (DSR FUN_140255520: "acfopsmvxbgy"[type]).</summary>
    public const string Ds1Letters = "acfopsmvxbgy";

    static readonly Regex EventName = new(@"(?<![0-9A-Za-z])[a-z][0-9]{9}(?![0-9A-Za-z])", RegexOptions.Compiled);

    /// <summary>Event-like names (letter + 9 digits) of every FMOD project in a folder / binder → project names.</summary>
    public static Dictionary<string, List<string>> FevIndex(IEnumerable<(string Project, byte[] Bytes)> projects)
    {
        var idx = new Dictionary<string, List<string>>();
        foreach (var (p, b) in projects)
            foreach (var n in EventName.Matches(System.Text.Encoding.Latin1.GetString(b)).Select(m => m.Value).Distinct())
                (idx.TryGetValue(n, out var l) ? l : idx[n] = []).Add(p);
        return idx;
    }

    public static IEnumerable<(string, byte[])> Ds1Projects(string dsrRoot) =>
        Directory.GetFiles(Path.Combine(dsrRoot, "sound"), "*.fev").Select(f => (Path.GetFileNameWithoutExtension(f), File.ReadAllBytes(f)));

    public static IEnumerable<(string, byte[])> Ds2Projects(string ds2Data)
    {
        var bnd = BND4.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(ds2Data, "sound", "frpg2.fevbnd.dcx"))));
        return bnd.Files.Where(f => f.Name.EndsWith(".fev", StringComparison.OrdinalIgnoreCase)).Select(f => (Path.GetFileNameWithoutExtension(f.Name), f.Bytes));
    }

    public static IEnumerable<(long Event, EMEVD.Instruction Ins)> Resolved(EMEVD emevd) => Build.Events.MapSounds.Resolved(emevd);

    public static int Run(string[] args)
    {
        string dsr = Program.DefaultDsrRoot;
        string ds2Data = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "..", "..", "ds2");
        if (!Directory.Exists(Path.Combine(ds2Data, "sound"))) ds2Data = Path.GetFullPath(Path.Combine("data", "ds2"));
        var ds1Idx = FevIndex(Ds1Projects(dsr));
        var ds2Idx = FevIndex(Ds2Projects(ds2Data));
        string Where(string n) => ds1Idx.TryGetValue(n, out var l) ? string.Join("+", l.Where(p => p != "frpg" && p != "frpg_stationary").DefaultIfEmpty("frpg-only")) : "-";

        var shapeCount = new Dictionary<string, int>();
        var typeCount = new Dictionary<char, int>();
        var projCount = new Dictionary<string, int>();
        int total = 0, keyed = 0, keyedOnAtStart = 0, disabledNotKeyed = 0;
        var seCount = new Dictionary<string, int>();
        Console.WriteLine("# DS1 MSB1 Sound events (map: name type id shape entity | project | switched by 2010:3 | off at load (SND1))");
        foreach (string msbPath in Directory.GetFiles(Path.Combine(dsr, "map", "MapStudio"), "m*.msb").OrderBy(f => f))
        {
            string map = Path.GetFileNameWithoutExtension(msbPath);
            MSB1 msb;
            try { msb = MSB1.Read(msbPath); } catch (Exception e) { Console.WriteLine($"# {map}: unreadable ({e.GetType().Name}), skipped"); continue; }
            string evPath = Path.Combine(dsr, "event", map + ".emevd.dcx");
            var emevd = File.Exists(evPath) ? EMEVD.Read(evPath) : null;
            var resolved = emevd != null ? Resolved(emevd).ToList() : [];
            var switched = resolved.Where(r => r.Ins.Bank == 2010 && r.Ins.ID == 3).Select(r => BitConverter.ToInt32(r.Ins.ArgData, 0)).ToHashSet();
            // fix pass (SND1): off at load = a 2010:3 off in the unconditional prefix of event 0, 50 or an event they initialise
            var offAtStart = emevd != null ? Build.Events.MapSounds.OffAtStart(emevd) : [];
            string area = map.Substring(1, 2);
            foreach (var s in msb.Events.Sounds)
            {
                total++;
                char t = s.SoundType >= 0 && s.SoundType < Ds1Letters.Length ? Ds1Letters[s.SoundType] : '?';
                typeCount[t] = typeCount.GetValueOrDefault(t) + 1;
                var region = msb.Regions.GetEntries().FirstOrDefault(r => r.Name == s.RegionName);
                string shape = region?.Shape.GetType().Name ?? "none";
                shapeCount[shape] = shapeCount.GetValueOrDefault(shape) + 1;
                string name = $"{t}{s.SoundID:D9}";
                string proj = Where(name);
                string projKind = proj == "-" ? "missing" : proj.Contains($"frpg_sm{area}") ? "smAA" : proj.Contains($"frpg_m{area}") ? "mAA" : proj.Contains($"frpg_xm{area}") ? "xmAA" : proj;
                projCount[$"{t}:{projKind}"] = projCount.GetValueOrDefault($"{t}:{projKind}") + 1;
                bool sw = s.EntityID > 0 && switched.Contains(s.EntityID), off = s.EntityID > 0 && offAtStart.Contains(s.EntityID);
                if (sw) keyed++;
                if (sw && !off) keyedOnAtStart++;
                if (off && !sw) disabledNotKeyed++;
                Console.WriteLine($"{map} {s.Name} {name} {shape} e{s.EntityID} | {proj} | {(sw ? "switched" : "-")} | {(off ? "off-at-start" : "-")}{(sw && !off ? "  ** keyed but ON at start" : "")}{(ds2Idx.ContainsKey(name) ? "  ** DS2 defines " + name : "")}");
            }
            if (emevd == null) continue;
            // 2010:2 Play SE and one-shot SFX
            var parts = msb.Parts.GetEntries().Where(p => p.EntityID > 0).GroupBy(p => p.EntityID).ToDictionary(g => g.Key, g => g.First());
            var regions = msb.Regions.GetEntries().Where(r => r.EntityID > 0).GroupBy(r => r.EntityID).ToDictionary(g => g.Key, g => g.First());
            foreach (var (ev, x) in resolved.Where(r => r.Ins.Bank == 2010 && r.Ins.ID == 2).Distinct())
            {
                int ent = BitConverter.ToInt32(x.ArgData, 0), type = BitConverter.ToInt32(x.ArgData, 4), id = BitConverter.ToInt32(x.ArgData, 8);
                char t = type >= 0 && type < Ds1Letters.Length ? Ds1Letters[type] : '?';
                string kind = ent == 10000 ? "player" : parts.TryGetValue(ent, out var p) ? p.GetType().Name : regions.TryGetValue(ent, out var r) ? "Region." + r.GetType().Name : "unknown";
                string name = $"{t}{id:D9}", alias = $"a{id:D9}";
                seCount[$"{t}/{kind}"] = seCount.GetValueOrDefault($"{t}/{kind}") + 1;
                Console.WriteLine($"SE {map} event {ev} 2010:2 entity {ent} ({kind}) {name} | {Where(name)} | alias {alias}: DS1 {Where(alias)}, DS2 {(ds2Idx.ContainsKey(alias) ? "TAKEN" : "free")}");
            }
            foreach (var (ev, x) in resolved.Where(r => r.Ins.Bank == 2006 && r.Ins.ID is 3 or 4).Distinct())
                Console.WriteLine($"SFX {map} event {ev} 2006:{x.ID} ({string.Join(", ", Enumerable.Range(0, x.ArgData.Length / 4).Select(k => BitConverter.ToInt32(x.ArgData, k * 4)))})");
        }
        Console.WriteLine($"# DS1 totals: {total} sound events; types {string.Join(", ", typeCount.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))}");
        Console.WriteLine($"# shapes {string.Join(", ", shapeCount.Select(k => $"{k.Key} {k.Value}"))}");
        Console.WriteLine($"# defining project by type {string.Join(", ", projCount.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))}");
        Console.WriteLine($"# switched by 2010:3: {keyed}; of them ON at map start: {keyedOnAtStart}; disabled at start but never switched on: {disabledNotKeyed}");
        Console.WriteLine($"# 2010:2 Play SE by type/entity kind: {string.Join(", ", seCount.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))}");

        // DS2's own sound regions
        var ds2Shapes = new Dictionary<string, int>();
        var ds2Types = new Dictionary<int, int>();
        int ds2Keyed = 0, ds2Total = 0;
        foreach (string f in Directory.GetFiles(Path.Combine(ds2Data, "map"), "*.msb", SearchOption.AllDirectories).OrderBy(f => f))
        {
            MSB2 m;
            try { m = MSB2.Read(f); } catch { continue; }
            foreach (var s in m.Regions.Sounds)
            {
                ds2Total++;
                ds2Types[s.UnkT00] = ds2Types.GetValueOrDefault(s.UnkT00) + 1;
                string sh = s.Shape.GetType().Name;
                ds2Shapes[sh] = ds2Shapes.GetValueOrDefault(sh) + 1;
                if (s.UnkT08 != 0) { ds2Keyed++; Console.WriteLine($"DS2 {Path.GetFileNameWithoutExtension(f)} '{s.Name}' type {s.UnkT00} id {s.SoundID} key {s.UnkT08} {sh}"); }
            }
        }
        Console.WriteLine($"# DS2 vanilla: {ds2Total} sound regions; types {string.Join(", ", ds2Types.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))}; shapes {string.Join(", ", ds2Shapes.Select(k => $"{k.Key} {k.Value}"))}; keyed (UnkT08 != 0) {ds2Keyed}");
        return 0;
    }
}
