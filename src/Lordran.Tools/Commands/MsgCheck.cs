using System.Numerics;
using Lordran.Tools.Build.Events;
using Lordran.Tools.Build.Maps;
using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 10 (asy-items): offline checks of the DS1 developer messages (rules M0-M4, <see cref="BloodMessages"/>) and the
/// origin prompt (rule C2, <see cref="OriginPrompt"/>) in a built pack.
/// msg-check PACKDIR [MAP] (default m18_01_00_00): every placed DS1 message of the map's DS1 source has its EventLocation
/// row (category 12 visible / 22 hidden with --ds1-hidden-messages; another visible category is reported, for an A/B
/// build), categoryId = DS1 id, memoryBlockId = the memory block (MSB2 Collision.UnkT12) of the collision under it (M0),
/// a bloodmes text without any DS1 tag left, its category-19 box (same block), a DS2 floor 0–0.6 m under it (FloorProbe on
/// the pack's collision); no other row uses the message id ranges; every 132221 of the map's event ESD names a placed
/// message row; left-out (hidden) messages are listed. Exit 1 on any problem.
/// origin-check: DS1's opening (<see cref="OriginPrompt.OpeningFlag"/>) over every DS1 map's event script (expected: only
/// m18_01, flag 11810002), and the pack's prompt group when PACKDIR is given.
/// </summary>
public static class MsgCheck
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: msg-check PACKDIR [MAP]"); return 1; }
        string pack = args[1], map = args.Length > 2 ? args[2] : "m18_01_00_00";
        string ds1Map = FlagRules.Host.FirstOrDefault(h => h.Value == map).Key is string k ? k + "_00_00" : map;
        string Gd(string p) => Path.Combine(pack, "gamedata", p.Replace('/', Path.DirectorySeparatorChar));
        var defs = Directory.GetFiles(Path.Combine(ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        var loc = PARAM.Read(Gd($"param/eventlocation_{map}.param"));
        if (!loc.ApplyParamdefCarefully(defs)) throw new InvalidDataException("no paramdef fits EventLocation");
        var fmg = FMG.Read(Gd($"menu/text/english/bloodmes/{map}.fmg"));
        var texts = fmg.Entries.Where(e => !string.IsNullOrEmpty(e.Text)).ToDictionary(e => e.ID, e => e.Text);
        var probe = FloorProbe.FromFolder(Path.Combine(pack, "gamedata"), map);
        var msb2 = MSB2.Read(Gd($"map/{map}/{map}.msb"));
        var blockOf = msb2.Parts.Collisions.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => (int)g.First().UnkT12);
        var blocks = blockOf.Values.ToHashSet();
        var msgs = BloodMessages.Collect(MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", ds1Map + ".msb")),
            File.Exists(Path.Combine(Program.DefaultDsrRoot, "event", ds1Map + ".emevd.dcx")) ? EMEVD.Read(Path.Combine(Program.DefaultDsrRoot, "event", ds1Map + ".emevd.dcx")) : null);
        var problems = new List<string>();
        int Int(PARAM.Row r, string f) => Convert.ToInt32(r[f].Value);
        Vector3 Pos(PARAM.Row r) => new(Convert.ToSingle(r["PositionX"].Value), Convert.ToSingle(r["PositionY"].Value), Convert.ToSingle(r["PositionZ"].Value));
        var mine = new HashSet<int>();
        int placed = 0, leftOut = 0;
        Console.WriteLine($"msg-check {pack} {map} (DS1 {ds1Map}): {msgs.Count} DS1 messages; collision memory blocks {string.Join(",", blocks.Order())}");
        foreach (var m in msgs)
        {
            string at = $"message {m.Id} ({m.Name})";
            // a hidden message is placed only by --ds1-hidden-messages (rule M4): judged by the pack itself
            if (m.Hidden && loc[m.Location] == null) { Console.WriteLine($"  left out (M4 off, hidden): {m.Id} {m.Name}"); leftOut++; continue; }
            placed++;
            mine.Add(m.Location); mine.Add(m.Box);
            if (loc[m.Location] is not { } row) { problems.Add($"{at}: no EventLocation row {m.Location}"); continue; }
            int cat = Int(row, "categoryType"), want = m.Hidden ? BloodMessages.HiddenCategory : 12;
            if (m.Hidden ? cat != want : cat is not (12 or 13 or 22)) problems.Add($"{at}: row {m.Location} categoryType {cat} (expected {want})");
            if (Int(row, "categoryId") != m.Id) problems.Add($"{at}: row {m.Location} categoryId {Int(row, "categoryId")}");
            if (!texts.TryGetValue(m.Id, out var text)) problems.Add($"{at}: bloodmes/{map}.fmg has no text {m.Id}");
            else if (text.Contains("<?")) problems.Add($"{at}: text keeps a DS1 tag: {text}");
            var p = Pos(row);
            var f = probe.Floor(p, above: 0.6f, below: 3f);
            int block = Int(row, "memoryBlockId");
            if (!blocks.Contains(block)) problems.Add($"{at}: memoryBlockId {block} is no collision block of {map} (M0)");
            if (f is { } fb && blockOf.TryGetValue(fb.Part, out int under) && under != block) problems.Add($"{at}: memoryBlockId {block}, the collision under it ({fb.Part}) is block {under} (M0)");
            if (loc[m.Box] is not { } box || Int(box, "categoryType") != BloodMessages.ProhibitedCategory) problems.Add($"{at}: no category-19 box {m.Box}");
            else
            {
                if (Vector3.Distance(Pos(box) + new Vector3(0, 0.5f, 0), p) > 0.01f) problems.Add($"{at}: box {m.Box} not under the message");
                if (Int(box, "memoryBlockId") != block) problems.Add($"{at}: box {m.Box} memoryBlockId {Int(box, "memoryBlockId")} ≠ {block}");
            }
            string floor = f is { } h ? $"floor {h.Y:0.00} (gap {p.Y - h.Y:+0.00;-0.00}, {h.Part})" : "no floor";
            if (f is not { } fh || p.Y - fh.Y > 0.6f || p.Y - fh.Y < -0.05f) problems.Add($"{at}: {floor} under {p}");
            Console.WriteLine($"  {(m.Hidden ? "M4" : m.Toggled ? "M2" : "M1")} {m.Id,5} row {m.Location} cat {cat}{(cat != want ? $" (rule: {want})" : "")} block {block} box {m.Box} at {p:0.00} rot {Convert.ToSingle(row["RotationY"].Value):0} {floor}: \"{text?.Replace("\n", " / ")}\"");
        }
        foreach (var r in loc.Rows.Where(r => r.ID is >= BloodMessages.VisibleBase and < BloodMessages.HiddenBoxBase + 1000 && !mine.Contains(r.ID)))
            problems.Add($"EventLocation {r.ID} lies in the message id ranges but is no message row");
        // the event script: every 132221 names a placed message row (rule M2)
        var esd = ESD.Read(Gd($"ezstate/event_{map}.esd"));
        var rows = msgs.Where(m => mine.Contains(m.Location)).Select(m => m.Location).ToHashSet();
        int uses = 0;
        var toggledUsed = new HashSet<int>();
        foreach (var (gid, states) in esd.StateGroups)
            foreach (var (sid, st) in states)
                foreach (var c in st.EntryCommands.Where(c => c.CommandID == 132221))
                {
                    uses++;
                    if (EsdCalls.Parse(c.Arguments[0]) is not EsdCalls.Const { V: var v }) { problems.Add($"group {gid} state {sid}: 132221 with a non-constant row"); continue; }
                    if (!rows.Contains((int)v)) problems.Add($"group {gid} state {sid}: 132221 names {v}, not a placed message row");
                    else toggledUsed.Add((int)v);
                }
        var toggled = msgs.Where(m => m.Toggled && rows.Contains(m.Location)).ToList();
        Console.WriteLine($"  {placed} placed, {leftOut} hidden left out; {toggled.Count} toggled by DS1 events, 132221 uses {uses} on {toggledUsed.Count} rows"
            + (toggled.Any(m => !toggledUsed.Contains(m.Location)) ? $" (no translated toggle yet for {string.Join(", ", toggled.Where(m => !toggledUsed.Contains(m.Location)).Select(m => m.Id))}: their DS1 events don't translate in this build)" : ""));
        foreach (var p in problems) Console.WriteLine("  FAIL " + p);
        Console.WriteLine($"msg-check {map}: {problems.Count} problem(s)");
        return problems.Count == 0 ? 0 : 1;
    }

    /// <summary>origin-check [PACKDIR]: DS1's opening over every DS1 map; with a pack, its prompt group(s) and texts.</summary>
    public static int Origin(string[] args)
    {
        var defs = EmevdDump.Defs();
        int found = 0;
        foreach (var f in Directory.GetFiles(Path.Combine(Program.DefaultDsrRoot, "event"), "m*.emevd.dcx").Order())
        {
            string m = Path.GetFileName(f)[..12];
            if (OriginPrompt.OpeningFlag(EMEVD.Read(f), defs, m) is int flag) { Console.WriteLine($"  DS1 opening: {m} event 0, flag {flag} → DS2 flag {FlagRules.Map(m[..6], flag)?.ToString() ?? "none"}"); found++; }
        }
        Console.WriteLine($"origin-check: {found} DS1 map(s) with an opening; class flags: " + string.Join(", ", Enumerable.Range(0, OriginPrompt.Classes).Select(c => $"{OriginPrompt.ClassName(c)} {FlagRules.Ds1Origin(c)?.ToString() ?? "NONE"}")));
        int bad = found == 1 ? 0 : 1;
        if (args.Length > 1 && Directory.Exists(args[1]))
        {
            string pack = args[1];
            var me = FMG.Read(Path.Combine(pack, "gamedata", "menu", "text", "english", "mapevent.fmg")).Entries.Where(e => e.Text != null).ToDictionary(e => e.ID, e => e.Text);
            foreach (var esdPath in Directory.GetFiles(Path.Combine(pack, "gamedata", "ezstate"), "event_*.esd"))
            {
                var esd = ESD.Read(esdPath);
                if (!esd.StateGroups.TryGetValue(OriginPrompt.Group, out var g)) continue;
                var asks = g.OrderBy(s => s.Key).SelectMany(s => s.Value.EntryCommands).Where(c => c.CommandID == 130441).Select(c => EsdCalls.Parse(c.Arguments[0]) is EsdCalls.Const { V: var v } ? (int)v : -1).ToList();
                var sets = g.SelectMany(s => s.Value.EntryCommands).Where(c => c.CommandID == 130101).Select(c => (EsdCalls.Parse(c.Arguments[0]) as EsdCalls.Const)?.V).ToList();
                Console.WriteLine($"  {Path.GetFileName(esdPath)}: group {OriginPrompt.Group}: {g.Count} states, {asks.Count} YES/NO windows, {sets.Count} class-flag writes");
                foreach (int t in asks)
                {
                    if (me.TryGetValue(t, out var text)) Console.WriteLine($"    mapevent {t}: \"{text.Replace("\n", " / ")}\"");
                    else { Console.WriteLine($"    FAIL mapevent {t} missing"); bad++; }
                }
                if (asks.Count is not (0 or OriginPrompt.Classes)) { Console.WriteLine($"    FAIL {asks.Count} windows (expected {OriginPrompt.Classes}, or 0 with --ds1-origin)"); bad++; }
            }
        }
        return bad == 0 ? 0 : 1;
    }
}
