using SoulsFormats;
using Lordran.Tools.Build.Events;

namespace Lordran.Tools.Commands;

/// <summary>
/// objact-rules [MAP…] [--check] — session 10 (asy-events e2): the DS1 ObjAct and ladder rules (Build.Events.ObjActRules)
/// over DSR's own data, map by map (default: every DSR map): per MSB ObjAct its K1 kind, requirement (ObjActParam), K3
/// side region for SpEffect doors, K12 helper pose for prefab kinds; per ladder registration G4's class (at load / later).
/// --check: the K3 unit check of doors.md §6 (all five DS1 SpEffect doors pair with their "inside" region: m10_01
/// o1314_0000 → 1012401, m15_01 o5720_0001/0007/0008 → 1512251/1512253/1512255, m18_01 o8540_0005 → 1812020); exit 1 on
/// a mismatch. Read-only; nothing is built.
/// </summary>
public static class ObjActReport
{
    public static int Run(string[] args)
    {
        string root = Program.DefaultDsrRoot;
        var maps = args.Skip(1).Where(a => !a.StartsWith("--")).ToList();
        if (maps.Count == 0) maps = Directory.GetFiles(Path.Combine(root, "map", "MapStudio"), "m1*.msb").Select(f => Path.GetFileNameWithoutExtension(f)).Order().ToList();
        var defs = EmevdDump.Defs();
        var param = Build.Ds1Source.LoadParam(root, "ObjActParam")!;
        var kinds = new Dictionary<ObjActRules.Kind, int>();
        var inner = new Dictionary<(string Map, string Part), int?>();
        int later = 0, load = 0, laterOther = 0;
        var ds1 = new Build.Ds1Source(root);
        string Ds1Text(int m) { try { var t = ds1.Text("Event_text_", m); return string.IsNullOrEmpty(t) ? ds1.AnyText(m) ?? "?" : t; } catch { return "?"; } }
        foreach (var map in maps)
        {
            string ev = Path.Combine(root, "event", map + ".emevd.dcx");
            if (!File.Exists(ev)) continue;
            var msb1 = MSB1.Read(Path.Combine(root, "map", "MapStudio", map + ".msb"));
            var emevd = EMEVD.Read(ev);
            Console.WriteLine($"== {map}");
            foreach (var a in msb1.Events.ObjActs)
            {
                if (ObjActRules.Of(msb1, param, a) is not { } r) { Console.WriteLine($"  ObjAct {a.Name}: no part / ObjActParam row"); continue; }
                var k = ObjActRules.KindOf(a, r.Part);
                kinds[k] = kinds.GetValueOrDefault(k) + 1;
                string req = r.Type switch { 0 => "none", 1 => $"goods {r.Id}", 2 => $"SpEffect {r.Id}", _ => $"type {r.Type} id {r.Id}" }
                    + (r.Type2 != 0 ? $" OR type {r.Type2} id {r.Id2} (K4)" : "") + (r.Pass > 0 && r.Pass != r.A ? $"; pass flag {r.Pass} ≠ A (K5)" : "");
                string extra = "";
                if (r.Type == 2)
                {
                    var log = new List<string>();
                    int? reg = ObjActRules.InnerRegion(emevd, defs, r.A, r.Id, log);
                    inner[(map, r.Part.Name)] = reg;
                    extra = $"; K3 inside region {(reg?.ToString() ?? "none")} [{string.Join("; ", log)}]";
                }
                if (k == ObjActRules.Kind.Prefab)
                {
                    var (pos, _, note) = ObjActRules.HelperPose(root, r.Part, r.DummyId);
                    extra += $"; {note}";
                }
                Console.WriteLine($"  {r.Part.Name,-14} A {r.A} S {r.S} row {r.ParamRow}: {k} ({a.ObjActState}); requirement {req}; anim {r.AnimId} dummy {r.DummyId}{extra}");
            }
            var loadReg = ObjActRules.LoadRegisteredLadders(emevd, defs);
            var lateAll = ObjActRules.LaterLadders(emevd, defs);
            var lateReg = lateAll.Values.Where(l => l.NotG4 == null).ToList();
            var lateOther = lateAll.Values.Where(l => l.NotG4 != null).ToList();
            load += loadReg.Count; later += lateReg.Count; laterOther += lateOther.Count;
            if (loadReg.Count + lateAll.Count > 0)
                Console.WriteLine($"  ladders: at load {loadReg.Count} [{string.Join(", ", loadReg.Order())}]; later (rule G4) {lateReg.Count} [{string.Join(", ", lateReg.OrderBy(x => x.Entity).Select(x => $"{x.Entity} by {string.Join("/", x.By)}"))}]");
            foreach (var l in lateOther.OrderBy(x => x.Entity))
                Console.WriteLine($"  ladder {l.Entity} registered later by {string.Join("/", l.By)}, NOT G4: {l.NotG4}");
            // fix pass e2 (finding 4): K1's Door half decides the lock route only; the parts become DS2 doors by the
            // map's port list (MapBuilder: the Asylum's o8540). The Door-kind models a map's port list must cover:
            var doorModels = msb1.Events.ObjActs.Select(a => ObjActRules.Of(msb1, param, a)).Where(r => r != null && ObjActRules.KindOf(r.Act, r.Part) == ObjActRules.Kind.Door)
                .Select(r => r!.Part.ModelName).Distinct().Order().ToList();
            if (doorModels.Count > 0) Console.WriteLine($"  K1 Door-kind models (need a door port): {string.Join(", ", doorModels)}");
            var prompts = msb1.Events.ObjActs.Select(a => ObjActRules.Of(msb1, param, a)).Where(r => r != null && ObjActRules.KindOf(r.Act, r.Part) == ObjActRules.Kind.Prefab)
                .GroupBy(r => (r!.Prompt.Msg, r.Prompt.Dist, r.Prompt.Angle, r.Prompt.PlayerAnim));
            foreach (var g in prompts)
                Console.WriteLine($"  K6 prompt: msg {g.Key.Msg} \"{Ds1Text(g.Key.Msg)}\" reach {g.Key.Dist:0.##} m, cone {g.Key.Angle}°, player anim {g.Key.PlayerAnim} (not played: deviation): {string.Join(", ", g.Select(r => r!.Part.Name))}");
        }
        Console.WriteLine($"objact-rules: {kinds.Values.Sum()} ObjActs ({string.Join(", ", kinds.Select(k => $"{k.Key} {k.Value}"))}); ladders registered at load {load}, later {later + laterOther} (rule G4 {later}, not G4 {laterOther})");
        if (!args.Contains("--check")) return 0;
        var expect = new (string Map, string Part, int Region)[]
        {
            ("m10_01_00_00", "o1314_0000", 1012401), ("m15_01_00_00", "o5720_0001", 1512251), ("m15_01_00_00", "o5720_0007", 1512253),
            ("m15_01_00_00", "o5720_0008", 1512255), ("m18_01_00_00", "o8540_0005", 1812020),
        };
        int bad = 0;
        foreach (var (m, p, want) in expect)
        {
            bool ok = inner.TryGetValue((m, p), out var got) && got == want;
            if (!ok) bad++;
            Console.WriteLine($"  CHECK {(ok ? "pass" : "FAIL")}: K3 {m} {p} → {want} (got {(inner.TryGetValue((m, p), out var g2) ? g2?.ToString() ?? "none" : "not listed")})");
        }
        Console.WriteLine($"objact-rules --check: {expect.Length} K3 door(s), {bad} failed");
        return bad == 0 ? 0 : 1;
    }
}
