using System.Numerics;
using Lordran.Tools.Build.Maps;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 12 (fog12), check FOG-CHECK: every DS2 white door (o00_050x) of a built pack's map, side by side with DS1.
/// <code>fog-check PACK MAP... [--verbose]</code>
/// Per door: its MapObjectWhiteDoorParam row (category, front / back key guide, pass flag) and, for every side with a key
/// guide (front = the placed door's local −Z, the Asylum's verified calibration; back = +Z), the traversal DS2 walks
/// (rule FW1-T: start 0.9 m in front of the plane on the entry side, end 2.0 m past it — the Asylum courtyard crossing seen in
/// game went 0.88 m → 1.82 m — plus the 0.35 m body radius) over the collision of every MAP given (seams):
/// <list type="bullet">
/// <item>FLOOR: a walkable floor (normal Y ≥ 0.5) under the start, every 0.1 m of the path and the end, within 0.6 m up /
/// 1.5 m down of the door's base, steps ≤ 0.45 m between samples;</item>
/// <item>CLEAR: no collision triangle within the body radius ahead at 0.6 / 1.1 / 1.6 m over the floor, centre and ±0.3 m;</item>
/// <item>DS1: the DS1 light wall's own crossings (3:18 / 3:19 action areas, BossFogs.Crossings) are offered (a guide on each
/// side DS1 lets the player cross from), a boss entrance (rule FW1-B) is category 1 with the front only and DS1's action area
/// on its front, a boss fog DS1 never crosses (rule FW1-X) is category 2 without guides, and no DS2 side is offered that
/// DS1 never crosses (rule FW1-S).</item>
/// </list>
/// </summary>
public static class FogCheck
{
    const float Before = 0.9f, After = 2.0f, Radius = 0.35f, StepMax = 0.45f, Up = 0.6f, Down = 1.5f, Head = 1.9f;

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ESD, HashSet<int>> intCache = new();

    /// <summary>Every int an event script pushes (0x82 + int32 in command arguments and condition expressions).</summary>
    static HashSet<int> EsdInts(ESD esd) => intCache.GetValue(esd, e =>
    {
        var set = new HashSet<int>();
        void Scan(byte[] b) { for (int i = 0; i + 4 < b.Length; i++) if (b[i] == 0x82) set.Add(BitConverter.ToInt32(b, i + 1)); }
        foreach (var st in e.StateGroups.Values.SelectMany(g => g.Values))
        {
            foreach (var c in st.EntryCommands.Concat(st.ExitCommands).Concat(st.WhileCommands).Concat(st.Conditions.SelectMany(k => k.PassCommands)))
                foreach (var a in c.Arguments) Scan(a);
            foreach (var k in st.Conditions) Scan(k.Evaluator);
        }
        return set;
    });

    /// <summary>Instances some state of the event script hides (130702(instance, 0)).</summary>
    static HashSet<int> EsdHides(ESD esd)
    {
        var set = new HashSet<int>();
        foreach (var st in esd.StateGroups.Values.SelectMany(g => g.Values))
            foreach (var c in st.EntryCommands.Concat(st.ExitCommands).Concat(st.WhileCommands).Concat(st.Conditions.SelectMany(k => k.PassCommands)))
                if (c.CommandID == 130702 && c.Arguments.Count > 1 && c.Arguments[0].Length >= 5 && c.Arguments[0][0] == 0x82
                    && Build.Events.EventSim.Evaluate(c.Arguments[1], (_, _) => 0) == 0)
                    set.Add(BitConverter.ToInt32(c.Arguments[0], 1));
        return set;
    }

    /// <summary>
    /// fog-check --ds1-survey: the FW1 rules over EVERY DS1 map (built or not): each light wall (object whose entity + 1 is an
    /// SFX) classified as DS1 single-player shows it — boss entrance (FW1-B/F), boss far wall (FW1-X), one-shot wall (FW1-P),
    /// multiplayer-only (FW1-M: not placed), plain barrier, or a twin at an earlier wall's spot (FW1b) — with walls the rules
    /// can't classify flagged (a boss entrance without a DS1 action area, a crossed wall that is also host-disabled).
    /// </summary>
    static int Survey()
    {
        var defs = EmevdDump.Defs();
        int odd = 0;
        var totals = new Dictionary<string, int>();
        foreach (var ev in Directory.GetFiles(Path.Combine(Program.DefaultDsrRoot, "event"), "m*.emevd.dcx").OrderBy(f => f))
        {
            string m = Path.GetFileName(ev)[..12];
            string msbPath = Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", m + ".msb");
            if (!File.Exists(msbPath)) continue;
            MSB1 msb1; EMEVD emevd;
            try { msb1 = MSB1.Read(msbPath); emevd = EMEVD.Read(ev); } catch (Exception e) { Console.WriteLine($"{m}: unreadable ({e.GetType().Name}), skipped"); continue; }
            var sfx = msb1.Events.SFX.Select(s => s.EntityID).Where(e => e > 0).ToHashSet();
            var walls = msb1.Parts.Objects.Where(p => p.EntityID > 0 && sfx.Contains(p.EntityID + 1)).ToList();
            if (walls.Count == 0) continue;
            var crossings = BossFogs.Crossings(emevd, defs);
            var host = BossFogs.HostDisabled(emevd, defs);
            Dictionary<int, BossFogs.Fog> boss;
            try { boss = BossFogs.Of(Program.DefaultDsrRoot, msb1, emevd, defs, walls.Select(w => w.EntityID).ToHashSet()); }
            catch (Exception e) { Console.WriteLine($"{m}: boss fogs unreadable ({e.Message})"); boss = []; }
            var kept = new List<Vector3>();
            var rows = new List<string>();
            foreach (var w in walls)
            {
                string k;
                if (kept.Any(q => Vector3.Distance(q, w.Position) < 0.1f)) k = "twin (FW1b, not placed)";
                else if (boss.TryGetValue(w.EntityID, out var b)) k = b.Exit ? $"boss {b.Boss} far wall (FW1-X)" : $"boss {b.Boss} entrance (FW1-B, front → area {b.EntryArea}{(b.Turned ? ", turned" : "")})";
                else if (crossings.ContainsKey(w.EntityID)) k = "one-shot (FW1-P)";
                else if (host.Contains(w.EntityID)) k = "multiplayer-only (FW1-M, not placed)";
                else k = "plain barrier (placed, no key guide)";
                bool flag = (boss.TryGetValue(w.EntityID, out var bb) && !bb.Exit && bb.EntryArea == 0) || (crossings.ContainsKey(w.EntityID) && host.Contains(w.EntityID) && !boss.ContainsKey(w.EntityID)) || k.StartsWith("plain");
                if (flag) odd++;
                if (!k.StartsWith("twin") && !k.StartsWith("multi")) kept.Add(w.Position);
                string cls = k.Split(' ')[0] == "boss" ? (k.Contains("FW1-X") ? "FW1-X" : "FW1-B") : k.Contains("FW1-P") ? "FW1-P" : k.Contains("FW1-M") ? "FW1-M" : k.StartsWith("twin") ? "FW1b" : "barrier";
                totals[cls] = totals.GetValueOrDefault(cls) + 1;
                rows.Add($"  {w.EntityID} {w.ModelName}: {k}{(flag ? "  << CHECK" : "")}");
            }
            Console.WriteLine($"{m}: {walls.Count} light walls");
            rows.ForEach(Console.WriteLine);
        }
        Console.WriteLine($"fog-check --ds1-survey: {string.Join(", ", totals.OrderBy(t => t.Key).Select(t => $"{t.Key} {t.Value}"))}; {odd} walls to check by hand");
        return 0;
    }

    public static int Run(string[] args)
    {
        if (args.Contains("--ds1-survey")) return Survey();
        var pos = args.Skip(1).Where(a => !a.StartsWith("--")).ToList();
        if (pos.Count < 2) { Console.WriteLine("usage: fog-check PACK MAP... [--verbose]"); return 2; }
        bool verbose = args.Contains("--verbose");
        string pack = pos[0];
        var maps = pos.Skip(1).ToList();
        string title = Path.Combine(pack, "title");
        var store = Formats.ParamStore.LoadDs2(File.Exists(Path.Combine(title, "enc_regulation.bnd.dcx")) ? title : Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        var wdp = store.Params["MapObjectWhiteDoorParam"];
        var tris = new List<FloorProbe.Tri>();
        foreach (var m in maps)
            try { tris.AddRange(FloorProbe.FromFolder(pack, m).Triangles); }
            catch (FileNotFoundException e) { Console.WriteLine($"{m}: no collision in the pack ({e.Message})"); }
        var probe = new FloorProbe(tris);
        var defs = EmevdDump.Defs();
        int fail = 0, pass = 0, warn = 0;
        void Expect(string what, bool ok) { if (ok) pass++; else fail++; if (!ok || verbose) Console.WriteLine($"  {(ok ? "pass" : "FAIL")}: {what}"); }

        foreach (var map in maps)
        {
            string msbPath = Path.Combine(pack, "gamedata", "map", map, map + ".msb");
            if (!File.Exists(msbPath)) { Console.WriteLine($"{map}: not in the pack (skipped)"); continue; }
            var msb2 = MSB2.Read(msbPath);
            var doors = msb2.Parts.Objects.Where(o => o.ModelName.StartsWith("o00_050", StringComparison.OrdinalIgnoreCase)).ToList();
            // DS1 side (the H2 read hook is active when PACK holds lordran-h2\merge.tsv)
            string? ds1 = MapHosts.Ds1Of(map);
            Dictionary<int, MSB1.Part.Object> walls = [];
            Dictionary<int, List<int>> crossings = [];
            Dictionary<int, BossFogs.Fog> boss = [];
            HashSet<int> hostDisabled = [];
            MSB1? msb1 = null;
            if (ds1 != null && File.Exists(Path.Combine(Program.DefaultDsrRoot, "event", ds1 + ".emevd.dcx")))
            {
                msb1 = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", ds1 + ".msb"));
                var emevd = EMEVD.Read(Path.Combine(Program.DefaultDsrRoot, "event", ds1 + ".emevd.dcx"));
                var sfx = msb1.Events.SFX.Select(s => s.EntityID).Where(e => e > 0).ToHashSet();
                int aabb = MapHosts.Aabb(map);
                foreach (var p in msb1.Parts.Objects.Where(p => p.EntityID > 0 && sfx.Contains(p.EntityID + 1)))
                    walls.TryAdd(aabb * 10000 + p.EntityID % 10000, p);
                crossings = BossFogs.Crossings(emevd, defs);
                hostDisabled = BossFogs.HostDisabled(emevd, defs);
                boss = BossFogs.Of(Program.DefaultDsrRoot, msb1, emevd, defs, walls.Values.Select(w => w.EntityID).ToHashSet());
            }
            string esdPath = Path.Combine(pack, "gamedata", "ezstate", $"event_{map}.esd");
            ESD? esd = File.Exists(esdPath) ? ESD.Read(esdPath) : null;
            Console.WriteLine($"=== {map} (DS1 {ds1 ?? "-"}): {doors.Count} white doors");
            foreach (var d in doors.OrderBy(d => d.MapObjectInstanceParamID))
            {
                int inst = d.MapObjectInstanceParamID;
                var row = wdp[inst];
                if (row == null) { Expect($"{map} {d.Name} instance {inst}: a MapObjectWhiteDoorParam row", false); continue; }
                int cat = Convert.ToInt32(row["whiteDoorCategory"].Value);
                long front = Convert.ToInt64(row["frontKeyGuideParamId"].Value), back = Convert.ToInt64(row["backKeyGuideParamId"].Value);
                int passFlag = Convert.ToInt32(row["passFlagId"].Value);
                walls.TryGetValue(inst, out var wall);
                string kind = wall == null ? "no DS1 wall" : boss.TryGetValue(wall.EntityID, out var bf) ? (bf.Exit ? $"boss {bf.Boss} far wall (FW1-X)" : $"boss {bf.Boss} entrance (FW1-B)") : "light wall (FW1)";
                Console.WriteLine($"{map} {d.Name} inst {inst} at ({d.Position.X:0.##}, {d.Position.Y:0.##}, {d.Position.Z:0.##}) yaw {d.Rotation.Y:0.#}: DS1 {(wall == null ? "-" : $"{wall.Name} {wall.EntityID}")} = {kind}; cat {cat} front {front} back {back} pass {passFlag}");
                double yaw = d.Rotation.Y * Math.PI / 180;
                var z = new Vector3((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw));   // local +Z
                // DS1 consistency
                if (wall != null && msb1 != null)
                {
                    var areas = crossings.GetValueOrDefault(wall.EntityID) ?? [];
                    var sides = areas.Select(a => msb1.Regions.GetEntries().FirstOrDefault(r => r.EntityID == a))
                        .Where(r => r != null).Select(r => (Id: r!.EntityID, Side: MathF.Sign(Vector3.Dot(z, r.Position - d.Position)))).ToList();
                    bool frontNeeded = sides.Any(s => s.Side < 0), backNeeded = sides.Any(s => s.Side > 0);
                    string got = string.Join(", ", sides.Select(s => $"{s.Id} on the {(s.Side < 0 ? "front" : "back")}"));
                    if (boss.TryGetValue(wall.EntityID, out var b) && b.Exit)
                        Expect($"{map} {inst}: DS1 never crosses boss wall {wall.EntityID} → exit fog (category 2, no key guide) (rule FW1-X; got cat {cat}, front {front}, back {back})", cat == 2 && front == 0 && back == 0);
                    else if (boss.TryGetValue(wall.EntityID, out b))
                    {
                        Expect($"{map} {inst}: boss entrance = category 1, front guide only (rule FW1-B; got cat {cat}, front {front}, back {back})", cat == 1 && front != 0 && back == 0);
                        Expect($"{map} {inst}: DS1's action area is on the door's front (rule FW1-F; DS1 areas: {got})", sides.Count > 0 && sides.All(s => s.Side < 0));
                    }
                    else
                    {
                        if (areas.Count > 0 && esd != null)
                        {
                            // rule FW1-P: a one-shot wall DS1 crosses = DS2's native one-shot fog (category 5 + pass flag, the
                            // engine sets the flag and removes the door), and no event crossing detector hides it mid-walk
                            int bx = 3730000 + wall.EntityID % 10000 * 2;
                            var detectors = EsdInts(esd).Where(v => v == bx || v == bx + 1).Distinct().ToList();
                            bool waits = passFlag != 0 && EsdInts(esd).Contains(passFlag);
                            Expect($"{map} {inst}: one-shot wall {wall.EntityID} = native one-shot fog (rule FW1-P: category 5, pass flag set, the event waits for it, no crossing boxes {bx}/{bx + 1}; got cat {cat}, pass {passFlag}, event reads it {waits}, boxes used {string.Join(",", detectors)})",
                                cat == 5 && passFlag != 0 && waits && detectors.Count == 0);
                        }
                        Expect($"{map} {inst}: every side DS1 crosses from has a key guide (rule FW1-S; DS1 areas: {got}; front {front}, back {back})", (!frontNeeded || front != 0) && (!backNeeded || back != 0));
                        Expect($"{map} {inst}: no key guide on a side DS1 never crosses from (rule FW1-S; DS1 areas: {got}; front {front}, back {back})", (front == 0 || frontNeeded) && (back == 0 || backNeeded));
                    }
                }
                // geometry of each offered traversal
                foreach (var (side, guide) in new[] { ("front", front), ("back", back) })
                {
                    if (guide == 0) continue;
                    var nIn = side == "front" ? -z : z;          // the entry side's outward normal
                    var dir = -nIn;                               // walking direction
                    var start = d.Position + nIn * Before;
                    float len = Before + After;
                    var msgs = new List<string>();
                    float? lastY = null; float maxStep = 0, minHead = 99f, headAt = 0; string? blocked = null, noFloor = null;
                    for (float t = 0; t <= len + 1e-3f; t += 0.1f)
                    {
                        var p = start + dir * t;
                        var f = probe.Floor(new Vector3(p.X, d.Position.Y + Up, p.Z), 0f, Up + Down);
                        if (f == null) { noFloor ??= $"no floor {t - Before:+0.0;-0.0} m from the plane"; lastY = null; continue; }
                        if (lastY is float ly) maxStep = MathF.Max(maxStep, MathF.Abs(f.Value.Y - ly));
                        lastY = f.Value.Y;
                        var above = probe.Down(p.X, p.Z).Where(h => h.Y > f.Value.Y + 0.05f).Select(h => h.Y - f.Value.Y).DefaultIfEmpty(99f).Min();
                        if (above < minHead) { minHead = above; headAt = t - Before; }
                        if (blocked == null)
                            foreach (float h in new[] { 0.6f, 1.1f, 1.6f })
                                foreach (float l in new[] { -0.3f, 0f, 0.3f })
                                {
                                    var lat = Vector3.Normalize(new Vector3(-dir.Z, 0, dir.X)) * l;
                                    var o = new Vector3(p.X, f.Value.Y + h, p.Z) + lat;
                                    if (probe.Ray(o, dir, Radius + 0.1f) is float hit && hit <= Radius)
                                    { blocked = $"collision {t + hit - Before:+0.00;-0.00} m from the plane at {h} m over the floor (lateral {l:+0.0;-0.0})"; break; }
                                }
                    }
                    if (verbose)
                    {
                        // the approach: floor heights (relative to the door's base) from 5 m out on the entry side to 3 m past the plane
                        var prof = new List<string>();
                        for (float t = -5f; t <= 3.01f; t += 0.5f)
                        {
                            var p = d.Position + dir * t;
                            var f = probe.Floor(new Vector3(p.X, d.Position.Y + 2.5f, p.Z), 0f, 6f);
                            prof.Add($"{t:+0.0;-0.0}:{(f is { } ff ? (ff.Y - d.Position.Y).ToString("+0.00;-0.00") : "--")}");
                        }
                        Console.WriteLine($"    {side} approach profile (m from the plane : floor): {string.Join(" ", prof)}");
                    }
                    var fs = probe.Floor(new Vector3(start.X, d.Position.Y + Up, start.Z), 0f, Up + Down);
                    var end = start + dir * len;
                    var fe = probe.Floor(new Vector3(end.X, d.Position.Y + Up, end.Z), 0f, Up + Down);
                    string desc = $"{map} {inst} {side} traversal ({start.X:0.##}, {start.Z:0.##}) → ({end.X:0.##}, {end.Z:0.##}): floor start {(fs is { } a ? $"{a.Y - d.Position.Y:+0.00;-0.00}" : "none")}, end {(fe is { } e ? $"{e.Y - d.Position.Y:+0.00;-0.00}" : "none")}, max step {maxStep:0.00}";
                    // FLOOR is a warning: a fog can open onto a drop (the Asylum boss fog over the plunge balcony, verified in game)
                    if (noFloor != null || maxStep > StepMax) { warn++; Console.WriteLine($"  WARN: {desc}: not on the floor all the way (FLOOR){(noFloor != null ? " — " + noFloor : "")}"); } else Expect($"{desc}: on the floor all the way (FLOOR)", true);
                    Expect($"{desc}: nothing in the way (CLEAR){(blocked != null ? " — " + blocked : "")}", blocked == null);
                    Expect($"{desc}: headroom ≥ {Head} m (HEAD; min {(minHead > 90 ? "open" : $"{minHead:0.00} m at {headAt:+0.0;-0.0} m from the plane")})", minHead >= Head);
                }
            }
            // rule FW1-M / user 10/19 ("fog walls should only exist where they exist in DS1"): every DS1 light wall of the map
            // exists in DS2 exactly when offline single-player DS1 shows it at a fresh load: boss fogs (until the kill: pass flag
            // = killed flag) and one-shot walls (until crossed: native pass flag) exist; multiplayer-only walls (event 0 disables
            // them for the host, nothing crosses them) and twins at an earlier wall's spot do not. A placed door the event script
            // hides with 130702 is visible-but-not-blocking or blocking-but-unseen in game (10/19), so only a pass flag may remove one.
            if (msb1 != null)
            {
                var placedWalls = new List<Vector3>();
                foreach (var (winst, w) in walls.OrderBy(kv => msb1.Parts.Objects.IndexOf(kv.Value)))
                {
                    bool isBoss = boss.ContainsKey(w.EntityID), crossed = crossings.ContainsKey(w.EntityID);
                    bool twin = placedWalls.Any(q => Vector3.Distance(q, w.Position) < 0.1f);
                    bool expected = !twin && (isBoss || crossed || !hostDisabled.Contains(w.EntityID));
                    if (expected) placedWalls.Add(w.Position);
                    var door = doors.FirstOrDefault(x => x.MapObjectInstanceParamID == winst);
                    string why = twin ? "twin of an earlier wall" : isBoss ? "boss fog, until the kill" : crossed ? "one-shot wall, until crossed" : hostDisabled.Contains(w.EntityID) ? "multiplayer-only: event 0 disables it for the host" : "plain DS1 barrier";
                    Expect($"{map} DS1 light wall {w.EntityID} ({why}): {(expected ? "exists" : "absent")} offline (got {(door != null ? $"door {winst}" : "none")})", expected == (door != null));
                    if (door == null || wdp[winst] is not { } r) continue;
                    int pf = Convert.ToInt32(r["passFlagId"].Value);
                    if (isBoss || crossed) Expect($"{map} {winst}: removed by its pass flag (killed / crossed), not by an event hide (pass {pf})", pf != 0);
                    if (esd != null && !isBoss && !crossed) Expect($"{map} {winst}: a permanent barrier is never hidden by the event script", !EsdHides(esd).Contains(winst));
                }
            }
        }
        Console.WriteLine($"fog-check: {pass + fail} checks, {fail} failed, {warn} warnings");
        return fail == 0 ? 0 : 1;
    }
}
