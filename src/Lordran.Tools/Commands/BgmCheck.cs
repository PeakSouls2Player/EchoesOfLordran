using SoulsFormats;
using Lordran.Tools.Build.Events;

namespace Lordran.Tools.Commands;

/// <summary>
/// bgm-check DS1MAP [DS2MAP] --pack DIR — session 11 (snd11): every DS1 BGM of the map (MSB1 Sound type 6 'm': area and
/// boss music) and every boss kill sting (rule SND6: 2010:2 's' SEs) is reachable in the built pack:
///  1. the MSB2 holds its Sound region (type 1 for BGM, the plan's id and key; SE: a type-0 point region of its key);
///  2. its FMOD event is defined by exactly one DS1 project of the pack's frpg2.fevbnd, and every wave bank that project
///     names is reachable (a per-map bnd with entry id 1, or a loose title/sound FSB);
///  3. an unkeyed BGM region plays while the player is inside it (DS2's region manager autoplays key 0: reachable); a keyed
///     one must be started: some group of the map's event ESD has a state, reachable from the group's state 0 over any
///     transition, whose entry commands hold 131201(key) — and 131202(key) when DS1 ever switches it off; a sting needs
///     131203(key). When no group does, the DS1 events that would (2010:3 (e, 1) / 2010:2) are looked up in the pack's
///     build.log: skipped only because their boss is not placed in this build ("absent (no generator yet)") → PENDING
///     (the rule is generic: the boss's events translate once a boss track places it), otherwise FAIL.
/// Exit 1 on any FAIL.
/// </summary>
public static class BgmCheck
{
    public static int Run(string[] args)
    {
        if (args.Length < 2 || !args.Contains("--pack")) { Console.Error.WriteLine("usage: bgm-check DS1MAP [DS2MAP] --pack DIR"); return 2; }
        string ds1Map = args[1];
        string ds2Map = args.Length > 2 && !args[2].StartsWith("--") ? args[2] : Build.Maps.MapHosts.Of(ds1Map)?.Host ?? ds1Map;
        string packDir = args.SkipWhile(a => a != "--pack").Skip(1).First();
        string dsr = Program.DefaultDsrRoot, ds2Data = Path.GetFullPath(Path.Combine("data", "ds2"));
        var msb1 = MSB1.Read(Path.Combine(dsr, "map", "MapStudio", ds1Map + ".msb"));
        string emevdPath = Path.Combine(dsr, "event", ds1Map + ".emevd.dcx");
        if (!File.Exists(emevdPath)) { Console.WriteLine($"bgm-check: OK (no DS1 event script for {ds1Map}: no plan)"); return 0; }
        var emevd = EMEVD.Read(emevdPath);
        var plan = MapSounds.Plan(msb1, emevd, MapSounds.Names(dsr, ds2Data, ds1Map));
        var resolved = MapSounds.Resolved(emevd).ToList();
        int fails = 0, pending = 0, ok = 0;
        void Fail(string s) { fails++; Console.WriteLine("FAIL " + s); }

        var msb2 = MSB2.Read(Path.Combine(packDir, "gamedata", "map", ds2Map, ds2Map + ".msb"));
        var esd = ESD.Read(Path.Combine(packDir, "gamedata", "ezstate", $"event_{ds2Map}.esd"));
        // (command, arg) → the groups that reach a state running it
        var reached = new Dictionary<(int, string), List<long>>();
        foreach (var (gid, states) in esd.StateGroups)
        {
            var seen = new HashSet<long>(); var q = new Queue<long>();
            long start = states.ContainsKey(0) ? 0 : states.Keys.Min();
            q.Enqueue(start); seen.Add(start);
            while (q.Count > 0)
            {
                var st = states[q.Dequeue()];
                foreach (var c in st.EntryCommands.Concat(st.WhileCommands).Concat(st.ExitCommands).Concat(st.Conditions.SelectMany(AllConds).SelectMany(x => x.PassCommands)))
                    if (c.CommandID is 131201 or 131202 or 131203 && c.Arguments.Count > 0)
                    {
                        var k = (c.CommandID, Esd.Expr(c.Arguments[0]));
                        if (!reached.TryGetValue(k, out var l)) reached[k] = l = [];
                        if (!l.Contains(gid)) l.Add(gid);
                    }
                foreach (var c in st.Conditions.SelectMany(AllConds))
                    if (c.TargetState is long t && states.ContainsKey(t) && seen.Add(t)) q.Enqueue(t);
            }
        }
        // the map's block of the pack's build.log: "  OK   event X slot S → group G" / "  SKIP event X slot S: reasons"
        var block = MapBlock(Path.Combine(packDir, "build.log"), ds2Map);
        string Why(long ev) => block.FirstOrDefault(l => (l.StartsWith("  OK") || l.StartsWith("  SKIP") || l.StartsWith("  FAIL")) && l.Contains($" event {ev} slot ")) is string l ? l.Trim() : $"event {ev}: not in build.log";

        // FMOD names of the pack
        var fevbnd = BND4.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(packDir, "gamedata", "sound", "frpg2.fevbnd.dcx"))));
        var vanilla = BND4.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(ds2Data, "sound", "frpg2.fevbnd.dcx")))).Files.Select(f => f.Name).ToHashSet();
        var ours = fevbnd.Files.Where(f => f.Name.EndsWith(".fev") && !vanilla.Contains(f.Name)).ToList();
        var ourIdx = SoundSurvey.FevIndex(ours.Select(f => (Path.GetFileNameWithoutExtension(f.Name), f.Bytes)));
        string? Fmod(string name)
        {
            if (!ourIdx.TryGetValue(name, out var owners)) return $"{name}: defined by no DS1 project in the fevbnd";
            if (owners.Count > 1) return $"{name}: defined by {string.Join("+", owners)}";
            var proj = ours.First(f => Path.GetFileNameWithoutExtension(f.Name) == owners[0]).Bytes;
            var banks = Build.Chr.Ds1ChrSound.Fev1Header(proj).Banks.Select(b => b.Name).ToList();
            var missing = banks.Where(b => !(File.Exists(Path.Combine(packDir, "title", "sound", b + ".fsb"))
                || File.Exists(Path.Combine(packDir, "gamedata", "sound", b + ".bnd")) && BND4.Read(Path.Combine(packDir, "gamedata", "sound", b + ".bnd")).Files.Any(f => f.ID == 1))).ToList();
            return missing.Count > 0 ? $"{name} ({owners[0]}): wave bank(s) {string.Join(",", missing)} neither preloaded per map nor loose" : null;
        }

        // a keyed sound: started / stopped by a reachable state, else the DS1 events that would and why they did not
        string Reach(int cmd, int key, IEnumerable<long> ds1Events, out bool isPending)
        {
            isPending = false;
            if (reached.TryGetValue((cmd, key.ToString()), out var g)) return $"{cmd}({key}) in group {string.Join(",", g)}";
            var evs = ds1Events.Distinct().ToList();
            var whys = evs.Select(Why).ToList();
            isPending = evs.Count > 0 && whys.All(w => w.Contains("no generator yet") || w.Contains("(no DS1 boss data)"));
            return $"no reachable {cmd}({key}); DS1 events {string.Join(",", evs)}: {string.Join(" | ", whys)}";
        }

        Console.WriteLine($"# bgm-check {ds1Map} → {ds2Map} ({packDir})");
        foreach (var r in plan.Regions.Where(r => r.Ds2Type == 1))
        {
            var s = msb2.Regions.Sounds.FirstOrDefault(x => x.Name == r.Name && x.UnkT00 == 1 && x.SoundID == r.SoundId && x.UnkT08 == r.Key);
            if (s == null) { Fail($"BGM '{r.Name}' {r.Ds2Event} key {r.Key}: no MSB2 Sound region"); continue; }
            if (Fmod(r.Ds2Event) is string fm) { Fail($"BGM '{r.Name}' {fm}"); continue; }
            if (r.Key == 0) { ok++; Console.WriteLine($"OK   BGM '{r.Name}' {r.Ds2Event}: region {s.Shape.GetType().Name} autoplays in range"); continue; }
            var starters = resolved.Where(x => x.Ins.Bank == 2010 && x.Ins.ID == 3 && x.Ins.ArgData.Length >= 5 && BitConverter.ToInt32(x.Ins.ArgData, 0) == r.Ds1Entity && x.Ins.ArgData[4] == 1).Select(x => x.Event).ToList();
            if (plan.StartIn.TryGetValue(r.Ds1Entity, out long host)) starters.Add(host);
            var stoppers = resolved.Where(x => x.Ins.Bank == 2010 && x.Ins.ID == 3 && x.Ins.ArgData.Length >= 5 && BitConverter.ToInt32(x.Ins.ArgData, 0) == r.Ds1Entity && x.Ins.ArgData[4] == 0).Select(x => x.Event).ToList();
            string on = Reach(131201, r.Key, starters, out bool onPending);
            string off = stoppers.Count == 0 ? "DS1 never stops it" : Reach(131202, r.Key, stoppers.Where(e => e != 0), out bool offPending);
            bool onOk = on.StartsWith("1312"), offOk = stoppers.Count == 0 || off.StartsWith("1312");
            string line = $"BGM '{r.Name}' {r.Ds2Event} key {r.Key} (entity {r.Ds1Entity}): start {on}; stop {off}";
            if (onOk && offOk) { ok++; Console.WriteLine("OK   " + line); }
            else if (!onOk && onPending || onOk && !offOk && off.Contains("no generator yet")) { pending++; Console.WriteLine("PENDING (boss not placed in this build) " + line); }
            else Fail(line);
        }
        // SND6 kill stings
        foreach (var e in plan.Effects.Where(e => e.Type == 5))
        {
            if (!msb2.Regions.Sounds.Any(x => x.UnkT08 == e.Key && x.UnkT00 == 0 && x.SoundID == int.Parse(e.Ds2Event[1..]))) { Fail($"sting {e.Ds2Event} key {e.Key}: no MSB2 Sound region"); continue; }
            if (Fmod(e.Ds2Event) is string fm) { Fail($"sting {fm}"); continue; }
            var evs = resolved.Where(x => x.Ins.Bank == 2010 && x.Ins.ID == 2 && x.Ins.ArgData.Length >= 12 && BitConverter.ToInt32(x.Ins.ArgData, 0) == e.Entity && BitConverter.ToInt32(x.Ins.ArgData, 4) == e.Type && BitConverter.ToInt32(x.Ins.ArgData, 8) == e.Id).Select(x => x.Event);
            string st = Reach(131203, e.Key, evs, out bool p2);
            string line = $"sting {e.Ds1Event}→{e.Ds2Event} key {e.Key} at {e.Entity}: {st}";
            if (st.StartsWith("1312")) { ok++; Console.WriteLine("OK   " + line); }
            else if (p2) { pending++; Console.WriteLine("PENDING (boss not placed in this build) " + line); }
            else Fail(line);
        }
        Console.WriteLine(fails == 0 ? $"bgm-check: OK ({ok} reachable, {pending} pending a boss placement)" : $"bgm-check: {fails} failure(s) ({ok} reachable, {pending} pending)");
        return fails == 0 ? 0 : 1;
    }

    static IEnumerable<ESD.Condition> AllConds(ESD.Condition c) => new[] { c }.Concat(c.Subconditions.SelectMany(AllConds));

    /// <summary>The translator's lines of one map in a pack's build.log: from the last "  OK   event 0 slot 0" before the
    /// "{ds2Map} script:" summary up to it.</summary>
    static List<string> MapBlock(string buildLog, string ds2Map)
    {
        if (!File.Exists(buildLog)) return [];
        var lines = File.ReadAllLines(buildLog);
        int end = Array.FindIndex(lines, l => l.StartsWith(ds2Map + " script:"));
        if (end < 0) return [];
        int start = Array.FindLastIndex(lines, end, l => l.StartsWith("  OK   event 0 slot 0"));
        return lines[Math.Max(0, start)..end].ToList();
    }
}
