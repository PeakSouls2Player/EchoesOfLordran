using System.Text.Json;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// emevd-dump FILE.emevd.dcx [EVENT...] — DS1 (DSR) event scripts with instruction names and typed arguments, from
/// DarkScript3's ds1-common.emedf.json (reference\emedf). Parameter substitutions (X0_4 = event arg at byte 0, 4 bytes)
/// are shown in place of the argument they replace. Session 7: reading the Asylum Demon sequence.
/// </summary>
public static class EmevdDump
{
    record Arg(string Name, int Type, string? Enum);
    record Instr(string Name, List<Arg> Args);

    /// <summary>emevd-stats DIR — how often each instruction is used over every *.emevd.dcx in DIR (DSR's event folder),
    /// with its name and the number of maps using it: the DS1 → DS2 event translator's work list.</summary>
    public static int Stats(string[] args)
    {
        var (instrs, _) = Load();
        var count = new Dictionary<(int, int), int>();
        var maps = new Dictionary<(int, int), HashSet<string>>();
        foreach (var f in Directory.GetFiles(args[1], "*.emevd.dcx"))
        {
            var e = EMEVD.Read(f);
            string m = Path.GetFileName(f).Split('.')[0];
            foreach (var ev in e.Events)
                foreach (var ins in ev.Instructions)
                {
                    var k = (ins.Bank, ins.ID);
                    count[k] = count.GetValueOrDefault(k) + 1;
                    if (!maps.TryGetValue(k, out var hs)) maps[k] = hs = [];
                    hs.Add(m);
                }
        }
        foreach (var (k, n) in count.OrderByDescending(x => x.Value))
            Console.WriteLine($"{n,6} {maps[k].Count,3} maps  {k.Item1,4}:{k.Item2,-3} {(instrs.TryGetValue(k, out var d) ? d.Name : "?")}");
        return 0;
    }

    public static int Run(string[] args)
    {
        var emevd = EMEVD.Read(args[1]);
        var want = args.Skip(2).Select(long.Parse).ToHashSet();
        var (instrs, enums) = Load();
        foreach (var ev in emevd.Events)
        {
            if (want.Count > 0 && !want.Contains(ev.ID)) continue;
            Console.WriteLine($"event {ev.ID} ({ev.RestBehavior})");
            for (int i = 0; i < ev.Instructions.Count; i++)
            {
                var ins = ev.Instructions[i];
                var subs = ev.Parameters.Where(p => p.InstructionIndex == i).ToDictionary(p => (int)p.TargetStartByte);
                string text;
                if (instrs.TryGetValue((ins.Bank, ins.ID), out var def))
                {
                    var parts = new List<string>();
                    int off = 0;
                    foreach (var a in def.Args)
                    {
                        int size = a.Type switch { 0 or 3 => 1, 1 or 4 => 2, _ => 4 };
                        off = (off + size - 1) / size * size;
                        string v;
                        if (subs.TryGetValue(off, out var p)) v = $"X{p.SourceStartByte}_{p.ByteCount}";
                        else if (off + size > ins.ArgData.Length) v = "?";
                        else
                        {
                            long n = a.Type switch
                            {
                                0 => ins.ArgData[off], 1 => BitConverter.ToUInt16(ins.ArgData, off), 2 => BitConverter.ToUInt32(ins.ArgData, off),
                                3 => (sbyte)ins.ArgData[off], 4 => BitConverter.ToInt16(ins.ArgData, off), 5 => BitConverter.ToInt32(ins.ArgData, off), _ => 0,
                            };
                            v = a.Type == 6 ? BitConverter.ToSingle(ins.ArgData, off).ToString("0.###") : n.ToString();
                            if (a.Enum != null && enums.TryGetValue(a.Enum, out var e) && e.TryGetValue(n.ToString(), out var en)) v += $"({en})";
                        }
                        parts.Add($"{Short(a.Name)}={v}");
                        off += size;
                    }
                    text = $"{def.Name}({string.Join(", ", parts)})";
                    if (ins.Bank == 2000 && ins.ID == 0 && ins.ArgData.Length > 12) text += " +[" + string.Join(", ", Enumerable.Range(3, ins.ArgData.Length / 4 - 3).Select(k => BitConverter.ToInt32(ins.ArgData, k * 4))) + "]";
                }
                else
                {
                    var ints = Enumerable.Range(0, ins.ArgData.Length / 4).Select(k => BitConverter.ToInt32(ins.ArgData, k * 4));
                    text = $"?({string.Join(", ", ints)})" + (subs.Count > 0 ? " subs " + string.Join(",", subs.Values.Select(p => $"@{p.TargetStartByte}=X{p.SourceStartByte}_{p.ByteCount}")) : "");
                }
                Console.WriteLine($"  [{i,3}] {ins.Bank,4}:{ins.ID,-3} {text}");
            }
        }
        return 0;
    }

    static string Short(string s) => new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray());

    /// <summary>emevd-translate MAP [--all-chr] [--sound] [--dump EVENT…] — runs the DS1 → DS2 event translator on a DS1 map (Asylum
    /// context) and reports which events translate and which rules are missing for the rest. Characters: the default
    /// enemy placement (AsylumEnemies' rule); --all-chr pretends every DS1 character has its generator (session 8: shows the
    /// character/boss rules on events whose characters aren't ported yet). --dump prints the translated groups of those
    /// events (ESD states with decoded expressions).</summary>
    public static int Translate(string[] args)
    {
        string map = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : "m18_01_00_00";
        bool allChr = args.Contains("--all-chr");
        // session 10 (asy-events e2): --hall-door-template T = build-pack's TEST option (COVERAGE CV5 A/B) for the report
        if (args.SkipWhile(a => a != "--hall-door-template").Skip(1).FirstOrDefault() is string hallT) Build.Maps.AsylumScript.HallTemplate = hallT;
        var dump = args.SkipWhile(a => a != "--dump").Skip(1).TakeWhile(a => !a.StartsWith("--")).Select(long.Parse).ToHashSet();
        var names = Defs();
        var msb1 = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", map + ".msb"));
        _ = Build.Maps.AsylumScript.Regions(msb1).ToList();
        var ds1 = new Build.Ds1Source(Program.DefaultDsrRoot);
        var emevd = EMEVD.Read(Path.Combine(Program.DefaultDsrRoot, "event", map + ".emevd.dcx"));
        Dictionary<int, int>? gens = allChr
            ? msb1.Parts.Enemies.Where(e => e.EntityID > 0).Select(e => e.EntityID).Concat([6023, 6024, 6450]).Distinct().ToDictionary(e => e, Build.Events.EntityRules.Generator)
            : null;
        // session 9 (track firelink): Firelink's script with its own context (DS2 m19_02); every other map: the Asylum's.
        // Firelink: --chr E,E… adds DS1 characters to the default placement (the npc track's NPCs: 6270,6080,6060,6240)
        var extraChr = args.SkipWhile(a => a != "--chr").Skip(1).Take(1).SelectMany(a => a.Split(',')).Select(int.Parse).ToList();
        // session 10 (asy-sound): --sound = the map's sound plan as the build passes it (rules SND1/SND4/SND5: keyed BGM
        // regions, event SEs, one-shot SFX, ON-at-start sounds)
        var sounds = args.Contains("--sound") ? Build.Events.EntityRules.SoundId(msb1, emevd, map) : null;
        Build.Events.EmevdTranslator.IContext ctx = map == Build.Events.FirelinkContext.Map
            ? new Build.Events.FirelinkContext(null, ds1, gens?.Values, sounds, extraCharacters: extraChr) : new Build.Events.AsylumContext(null, ds1, gens, sounds: sounds);
        var t = new Build.Events.EmevdTranslator(emevd, ctx, names) { G4Diagnostic = args.Contains("--g4-any") }; // session 10: --g4-any = rule G4 diagnostic (never in a build)
        int g = 38000;
        t.TranslateMap((id, slot) => g++);
        // session 8 step 2: boss rewards. Chrint stage 1 review: only with the Asylum's context, as the build does (AsylumScript
        // translates common.emevd's 970; FirelinkScript translates no common events), so the report stays the build's
        if (ctx is Build.Events.AsylumContext)
            t.TranslateCommon(EMEVD.Read(Path.Combine(Program.DefaultDsrRoot, "event", "common.emevd.dcx")), (id, slot) => g++, [970]);
        Console.WriteLine(t.Report());
        if (t.TemporaryFlags.Count > 0) // session 9 (fix pass): rule T1
            Console.WriteLine($"T1 temporary flags: {string.Join(", ", t.TemporaryFlags)}; event 0 clears {t.TemporaryFlags.Min}..{t.TemporaryFlags.Max}: {t.TemporaryFlagsReset}");
        foreach (var r in t.Results.Where(r => dump.Contains(r.EventId) && r.States != null))
        {
            Console.WriteLine($"--- event {r.EventId} slot {r.Slot} → group {r.GroupId}");
            foreach (var (sid, st) in r.States!.OrderBy(s => s.Key))
            {
                Console.WriteLine($"  state {sid}");
                foreach (var c in st.EntryCommands) Console.WriteLine($"    entry {c.CommandBank}:{c.CommandID}({string.Join(", ", c.Arguments.Select(Esd.Expr))})");
                foreach (var c in st.Conditions)
                {
                    Console.WriteLine($"    if {Esd.Expr(c.Evaluator)} -> {c.TargetState}");
                    foreach (var p in c.PassCommands) Console.WriteLine($"      pass {p.CommandBank}:{p.CommandID}({string.Join(", ", p.Arguments.Select(Esd.Expr))})");
                }
            }
        }
        Console.WriteLine($"{t.Results.Count(r => r.States != null)} of {t.Results.Count} event instances translate{(allChr ? " (--all-chr: every DS1 character assumed placed)" : "")}");
        foreach (var (m, n) in t.Results.Where(r => r.States == null).SelectMany(r => r.Missing.Distinct()).GroupBy(x => x).Select(gp => (gp.Key, gp.Count())).OrderByDescending(x => x.Item2))
            Console.WriteLine($"  missing x{n}: {m}");
        return 0;
    }

    /// <summary>DS1 instruction definitions for the translator: name and argument types.</summary>
    public static Dictionary<(int, int), (string Name, int[] Types)> Defs() =>
        Load().Item1.ToDictionary(k => k.Key, v => (v.Value.Name, v.Value.Args.Select(a => a.Type).ToArray()));

    /// <summary>Session 11 (burg11, rule H2): DS1 instruction definitions with argument names (H2Merge finds entity arguments).</summary>
    public static Dictionary<(int, int), (string Name, string[] ArgNames, int[] Types)> DefsWithArgNames() =>
        Load().Item1.ToDictionary(k => k.Key, v => (v.Value.Name, v.Value.Args.Select(a => a.Name).ToArray(), v.Value.Args.Select(a => a.Type).ToArray()));

    static (Dictionary<(int, int), Instr>, Dictionary<string, Dictionary<string, string>>) Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "reference", "emedf", "ds1-common.emedf.json");
        if (!File.Exists(path)) path = Path.Combine(Environment.CurrentDirectory, "reference", "emedf", "ds1-common.emedf.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var instrs = new Dictionary<(int, int), Instr>();
        foreach (var c in doc.RootElement.GetProperty("main_classes").EnumerateArray())
        {
            int bank = c.GetProperty("index").GetInt32();
            foreach (var i in c.GetProperty("instrs").EnumerateArray())
                instrs[(bank, i.GetProperty("index").GetInt32())] = new Instr(i.GetProperty("name").GetString()!,
                    i.GetProperty("args").EnumerateArray().Select(a => new Arg(a.GetProperty("name").GetString()!, a.GetProperty("type").GetInt32(),
                        a.TryGetProperty("enum_name", out var en) && en.ValueKind == JsonValueKind.String ? en.GetString() : null)).ToList());
        }
        var enums = new Dictionary<string, Dictionary<string, string>>();
        foreach (var e in doc.RootElement.GetProperty("enums").EnumerateArray())
            enums[e.GetProperty("name").GetString()!] = e.GetProperty("values").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        return (instrs, enums);
    }
}
