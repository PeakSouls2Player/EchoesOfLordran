using SoulsFormats;
using Lordran.Tools.Build.Events;

namespace Lordran.Tools.Commands;

/// <summary>
/// sound-check DS1MAP [DS2MAP] [--pack DIR] [--banks DIR] — session 10 (track asy-sound): checks the DS1 map sound
/// rules SND1-SND5. A DS1 map without its own EMEVD (m12_00_00_01) has no plan: reported, exit 0 (fix pass).
///  1. the plan (<see cref="MapSounds"/>): regions, keys, ON-at-start sounds (with the DS1 event whose group starts them),
///     event SEs, one-shot SFX, notes; SND1 load-time assertion (fix pass): no ON-at-start sound has a 2010:3 off in the
///     unconditional prefix of event 0, 50 or an event they initialise, and each is started by the first load-time event
///     that switches it (or event 0, noted);
///  2. the translator on synthetic events built from the plan (with the Asylum / Firelink context): every 2010:3 of a
///     keyed sound → 131201/131202(key), every placed 2010:2 → 131203(key), every placed 2006:3 → 131503(key), and each
///     ON-at-start sound started (131201) in its host event's group before that group's own switching;
///  2b. name reachability against DSR's own projects, no pack needed (fix pass, SND2/SND3): the projects DS1 loads for
///     the area (<see cref="MapSounds.Ds1Projects"/>, fdlc for area 12) with the plan's renames and the 'q' renames
///     applied: every region / SE event name is defined by exactly one of them (a name DS1 defines nowhere is noted);
///  --banks DIR: runs <see cref="Build.Maps.Atmosphere.SoundBanks"/> for this map alone into DIR (gamedata\sound,
///     title\sound) for the FMOD harness (validate\fmod_map.py), e.g. a DS1 DLC map no DS2 map builds yet;
///  3. with --pack: the built MSB2 holds every planned Sound / SFX region (type, id, key, shape point/sphere/box), the
///     map's event ESD addresses only keys that exist (131201/131202/131203 ⊂ Sound keys, 131503 ⊂ SFX keys), every
///     region / SE event name is defined by exactly one project of the pack's frpg2.fevbnd and its wave bank is
///     reachable (a preloaded per-map bank sound/NAME.bnd with entry id 1, or a loose title/sound/NAME.fsb), and no DS1
///     project in the fevbnd defines a name DS2's own projects define (SND3).
/// Exit 1 on any failure.
/// </summary>
public static class SoundCheck
{
    public static int Run(string[] args)
    {
        string ds1Map = args.Length > 1 ? args[1] : "m18_01_00_00";
        string ds2Map = args.Length > 2 && !args[2].StartsWith("--") ? args[2] : Lordran.Tools.Build.Maps.MapHosts.Of(ds1Map)?.Host ?? ds1Map; // session 11 (burg11): the MapHosts host (rule MH1)
        string? packDir = args.SkipWhile(a => a != "--pack").Skip(1).FirstOrDefault();
        string dsr = Program.DefaultDsrRoot;
        string? banksDir = args.SkipWhile(a => a != "--banks").Skip(1).FirstOrDefault();
        string ds2Data = Path.GetFullPath(Path.Combine("data", "ds2"));
        var msb1 = MSB1.Read(Path.Combine(dsr, "map", "MapStudio", ds1Map + ".msb"));
        string emevdPath = Path.Combine(dsr, "event", ds1Map + ".emevd.dcx");
        // fix pass: as Atmosphere.SoundRegions, a DS1 map without its own event script gets no plan (no regions placed)
        if (!File.Exists(emevdPath))
        {
            Console.WriteLine($"# {ds1Map}: no DS1 event script ({Path.GetFileName(emevdPath)}): no plan, nothing placed (open issue 6)");
            Console.WriteLine("sound-check: OK (no plan)");
            return 0;
        }
        var emevd = EMEVD.Read(emevdPath);
        var plan = MapSounds.Plan(msb1, emevd, MapSounds.Names(dsr, ds2Data, ds1Map));
        int fails = 0;
        void Fail(string s) { fails++; Console.WriteLine("FAIL " + s); }

        Console.WriteLine($"# plan {ds1Map}: {plan.Regions.Count} regions ({plan.Regions.Count(r => r.Key != 0)} keyed, ON at start: {string.Join(",", plan.StartIn.Select(kv => $"{kv.Key}@ev{kv.Value}"))}), {plan.Effects.Count} event SEs, {plan.OneShots.Count} one-shot SFX, renames {string.Join(" ", plan.Renames.Select(r => r.Key + "→" + r.Value))}");
        foreach (var r in plan.Regions) Console.WriteLine($"  region '{r.Name}' DS1 {r.Ds1Event} → type {r.Ds2Type} {r.Ds2Event} key {r.Key}{(r.Ds1Entity > 0 ? $" entity {r.Ds1Entity}" : "")}");
        foreach (var e in plan.Effects) Console.WriteLine($"  SE {e.Ds1Event} at {e.Entity} → {e.Ds2Event} key {e.Key} at {e.Position}");
        foreach (var o in plan.OneShots) Console.WriteLine($"  one-shot SFX {o.Ds1Sfx} at {o.Entity} → DS2 {o.Ds2Effect} SFX key {o.Key}");
        foreach (var n in plan.Notes) Console.WriteLine($"  note: {n}");
        if (plan.Regions.Select(r => r.Key).Where(k => k != 0).Concat(plan.Effects.Select(e => e.Key)).GroupBy(k => k).Any(g => g.Count() > 1)) Fail("duplicate Sound keys");

        // 1b. SND1 load-time assertion (fix pass), recomputed here from the EMEVD walk, not from the plan's sets
        var load = MapSounds.LoadTimeEvents(emevd);
        static bool Off(EMEVD.Instruction x, int e) => x.Bank == 2010 && x.ID == 3 && x.ArgData.Length >= 5 && BitConverter.ToInt32(x.ArgData, 0) == e && x.ArgData[4] == 0;
        foreach (var (e, host) in plan.StartIn)
        {
            // the critic's assertion: no off in the unconditional prefix (before any condition, wait, skip or END) of event 0, 50
            // or an event they initialise; and the stricter one the plan uses (an off DS1 runs on every path at the start)
            foreach (var l in load)
            {
                for (int i = 0; i < l.Ins.Count && l.Ins[i].Bank >= 2000; i++)
                    if (Off(l.Ins[i], e)) Fail($"ON-at-start sound {e}: event {l.Event} [{i}] switches it off before any condition (off at load)");
                if (MapSounds.AlwaysAtLoad(l.Ins).Any(x => Off(x, e))) Fail($"ON-at-start sound {e}: event {l.Event} switches it off on every path at load");
            }
            // load-time decisions: offs an event may run before its first wait (they race a start in another group)
            var decide = load.Where(l => MapSounds.ReachedAtLoad(l.Ins).Any(x => Off(x, e))).Select(l => l.Event).Distinct().ToList();
            var later = load.Where(l => !decide.Contains(l.Event) && l.Ins.Any(x => Off(x, e))).Select(l => l.Event).Distinct().ToList();
            bool noted = plan.Notes.Any(n => n.Contains($"sound {e} ON at start"));
            if (host != 0 && !load.Any(l => l.Event == host && l.Ins.Any(x => x.Bank == 2010 && x.ID == 3 && x.ArgData.Length >= 4 && BitConverter.ToInt32(x.ArgData, 0) == e))) Fail($"ON-at-start sound {e}: host event {host} does not switch it");
            if (host == 0 && !noted && decide.Any(o => o != 0)) Fail($"ON-at-start sound {e}: started in event 0 while load-time events {string.Join(",", decide)} decide it");
            Console.WriteLine($"  ON at start {e} (key {plan[e]}): started first in event {host}'s group; offs before a wait: {(decide.Count == 0 ? "none" : string.Join(",", decide))}"
                + (decide.Count(o => o != host) > 0 ? $" (vs {string.Join(",", decide.Where(o => o != host))} the order is not fixed, P)" : "")
                + (later.Count > 0 ? $"; offs after a wait: {string.Join(",", later)}" : ""));
        }

        // 2. the translator on synthetic events
        var ds1 = new Build.Ds1Source(dsr);
        EmevdTranslator.IContext ctx = ds1Map == FirelinkContext.Map ? new FirelinkContext(null, ds1, null, plan) : new AsylumContext(null, ds1, null, sounds: plan);
        var synth = new EMEVD { Format = emevd.Format };
        var ev0 = new EMEVD.Event(0);
        var probe = new EMEVD.Event(99990001);
        foreach (int e in plan.Keys)
        {
            probe.Instructions.Add(new EMEVD.Instruction(2010, 3, [.. BitConverter.GetBytes(e), 1, 0, 0, 0]));
            probe.Instructions.Add(new EMEVD.Instruction(2010, 3, [.. BitConverter.GetBytes(e), 0, 0, 0, 0]));
        }
        foreach (var e in plan.Effects) probe.Instructions.Add(new EMEVD.Instruction(2010, 2, [.. BitConverter.GetBytes(e.Entity), .. BitConverter.GetBytes(e.Type), .. BitConverter.GetBytes(e.Id)]));
        foreach (var o in plan.OneShots) probe.Instructions.Add(new EMEVD.Instruction(2006, 3, [.. BitConverter.GetBytes(0), .. BitConverter.GetBytes(o.Entity), .. BitConverter.GetBytes(-1), .. BitConverter.GetBytes(o.Ds1Sfx)]));
        ev0.Instructions.Add(new EMEVD.Instruction(2000, 0, [.. BitConverter.GetBytes(0), .. BitConverter.GetBytes(99990001)]));
        synth.Events.Add(ev0);
        synth.Events.Add(probe);
        // SND1 hosts: a stand-in of each deciding event holding its DS1 off (the translator must start the sound first)
        foreach (var h in plan.StartIn.GroupBy(kv => kv.Value).Where(g => g.Key != 0))
        {
            var hev = new EMEVD.Event(h.Key);
            foreach (var kv in h) hev.Instructions.Add(new EMEVD.Instruction(2010, 3, [.. BitConverter.GetBytes(kv.Key), 0, 0, 0, 0]));
            if (h.Key != 50) ev0.Instructions.Add(new EMEVD.Instruction(2000, 0, [.. BitConverter.GetBytes(0), .. BitConverter.GetBytes((int)h.Key)]));
            synth.Events.Add(hev);
        }
        var t = new EmevdTranslator(synth, ctx, EmevdDump.Defs());
        int g = 1;
        t.TranslateMap((id, slot) => g++);
        var cmds = t.Results.Where(r => r.States != null).SelectMany(r => r.States!.Values.SelectMany(s => s.EntryCommands)
            .Select(c => (r.EventId, c.CommandID, Arg: c.Arguments.Count > 0 ? Esd.Expr(c.Arguments[0]) : ""))).ToList();
        foreach (var r in t.Results.Where(r => r.States == null)) Fail($"synthetic event {r.EventId} did not translate: {string.Join("; ", r.Missing)}");
        foreach (var (e, key) in plan)
        {
            if (!cmds.Any(c => c.EventId == 99990001 && c.CommandID == 131201 && c.Arg == key.ToString())) Fail($"2010:3 ({e}, 1) → no 131201({key})");
            if (!cmds.Any(c => c.EventId == 99990001 && c.CommandID == 131202 && c.Arg == key.ToString())) Fail($"2010:3 ({e}, 0) → no 131202({key})");
        }
        foreach (var e in plan.Effects)
            if (!cmds.Any(c => c.CommandID == 131203 && c.Arg == e.Key.ToString())) Fail($"2010:2 {e.Ds1Event} at {e.Entity} → no 131203({e.Key})");
        foreach (var o in plan.OneShots)
            if (!cmds.Any(c => c.CommandID == 131503 && c.Arg == o.Key.ToString())) Fail($"2006:3 {o.Ds1Sfx} at {o.Entity} → no 131503({o.Key})");
        foreach (var (e, host) in plan.StartIn)
        {
            var hr = t.Results.FirstOrDefault(r => r.EventId == host && r.States != null);
            long? StateOf(int cmd) => hr?.States!.Where(s => s.Value.EntryCommands.Any(c => c.CommandID == cmd && c.Arguments.Count > 0 && Esd.Expr(c.Arguments[0]) == plan[e].ToString())).Select(s => (long?)s.Key).Min();
            long? on = StateOf(131201), off = StateOf(131202);
            if (on == null) Fail($"ON-at-start sound {e} not started by event {host}'s group");
            else if (off != null && off < on) Fail($"ON-at-start sound {e}: event {host}'s group switches it off (state {off}) before the start (state {on})");
        }
        Console.WriteLine($"# translator (synthetic): {cmds.Count(c => c.CommandID is 131201 or 131202 or 131203 or 131503)} sound/SFX commands: "
            + string.Join(" ", cmds.Where(c => c.CommandID is 131201 or 131202 or 131203 or 131503).Select(c => $"{(c.EventId != 99990001 ? $"ev{c.EventId}:" : "")}{c.CommandID}({c.Arg})")));

        // 2b. name reachability against DSR's own projects (fix pass: SND2 per DS1 area incl. fdlc, SND3 renames per project)
        {
            int areaN = int.Parse(ds1Map[1..3]);
            string snd = Path.Combine(dsr, "sound");
            var projects = MapSounds.Ds1ProjectsFor(ds1Map).SelectMany(p => new[] { p.Memory, p.Stream }).Where(p => File.Exists(Path.Combine(snd, p + ".fev"))).ToList();
            var ds2Names = MapSounds.Ds2Index(ds2Data);
            var all = MapSounds.Ds1AreaIndex(dsr);
            Dictionary<string, List<string>>? globals = null;
            var sim = new Dictionary<string, HashSet<string>>();
            foreach (string pj in projects)
            {
                var names = SoundSurvey.FevIndex([(pj, File.ReadAllBytes(Path.Combine(snd, pj + ".fev")))]).Keys.ToHashSet();
                foreach (var (from, to) in plan.Renames)
                    if (names.Contains(from) && !names.Contains(to)) { names.Remove(from); names.Add(to); }
                names.RemoveWhere(ds2Names.ContainsKey); // the 'q' renames
                sim[pj] = names;
            }
            // session 11 (snd11), SND6: the copies of DS1's global frpg_smain events go into the area memory project
            if (projects.Count > 0)
                foreach (var i in plan.Imports)
                {
                    if (!SoundSurvey.FevIndex([(i.Project, File.ReadAllBytes(Path.Combine(snd, i.Project + ".fev")))]).ContainsKey(i.Ds1Event)) Fail($"SND6 import {i.Ds1Event}: not in DS1's {i.Project}");
                    else sim[projects[0]].Add(i.Ds2Event);
                }
            int ok = 0, nowhere = 0;
            foreach (var (name, ds1Name) in plan.Regions.Select(r => (r.Ds2Event, r.Ds1Event)).Concat(plan.Effects.Select(e => (e.Ds2Event, e.Ds1Event))).Distinct())
            {
                var owners = sim.Where(kv => kv.Value.Contains(name)).Select(kv => kv.Key).ToList();
                if (owners.Count == 1) { ok++; continue; }
                if (owners.Count > 1) { Fail($"{name} (DS1 {ds1Name}): defined by {string.Join("+", owners)} (ambiguous)"); continue; }
                globals ??= SoundSurvey.FevIndex(SoundSurvey.Ds1Projects(dsr).Where(p => p.Item1 is "frpg" or "fdlc"));
                if (ds2Names.ContainsKey(name)) Fail($"{name} (DS1 {ds1Name}): DS2 defines it (the DS1 copy is q-renamed)");
                else if (!all.ContainsKey(ds1Name) && !globals.ContainsKey(ds1Name)) { nowhere++; Console.WriteLine($"  note: {ds1Name}: DS1 defines it in no project (silent in DS1 too)"); }
                else Fail($"{name} (DS1 {ds1Name}): defined by none of {string.Join(", ", projects)} (DS1: {string.Join("+", all.GetValueOrDefault(ds1Name) ?? ["frpg/fdlc global only"])})");
            }
            Console.WriteLine($"# names (DSR projects {string.Join(", ", projects)}): {ok} reachable, {nowhere} defined nowhere in DS1");
        }

        // --banks: the bank layer alone, for the FMOD harness
        if (banksDir != null)
        {
            var pack = new Build.Pack(Program.DefaultDs2Root, ds2Data);
            var log = new List<string>();
            var files = Build.Maps.Atmosphere.SoundBanks(pack, dsr, ds2Data, ds1Map, ds2Map, log);
            foreach (var (dev, path) in files)
            {
                string o = Path.Combine(banksDir, dev, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(o)!);
                File.WriteAllBytes(o, pack.GetFile(dev, path));
            }
            foreach (string l in log) Console.WriteLine("  " + l);
            Console.WriteLine($"# banks: {files.Count} files → {banksDir}");
        }

        // 3. the pack
        if (packDir != null)
        {
            var msb2 = MSB2.Read(Path.Combine(packDir, "gamedata", "map", ds2Map, ds2Map + ".msb"));
            var sounds = msb2.Regions.Sounds;
            var sfx = msb2.Regions.SFXs;
            foreach (var r in plan.Regions)
            {
                var s = sounds.FirstOrDefault(x => x.Name == r.Name && x.SoundID == r.SoundId && x.UnkT00 == r.Ds2Type && x.UnkT08 == r.Key);
                if (s == null) Fail($"region '{r.Name}' {r.Ds2Event} key {r.Key} not in the MSB2");
                else if (s.Shape is not (MSB.Shape.Point or MSB.Shape.Sphere or MSB.Shape.Box)) Fail($"region '{r.Name}' shape {s.Shape.GetType().Name}: DS2 plays point/sphere/box only");
            }
            foreach (var e in plan.Effects)
                if (!sounds.Any(x => x.UnkT08 == e.Key && x.UnkT00 == 0 && x.SoundID == int.Parse(e.Ds2Event[1..]))) Fail($"SE region key {e.Key} {e.Ds2Event} not in the MSB2");
            foreach (var o in plan.OneShots)
                if (!sfx.Any(x => x.UnkT04 == o.Key && x.EffectID == o.Ds2Effect)) Fail($"SFX region key {o.Key} effect {o.Ds2Effect} not in the MSB2");
            var soundKeys = sounds.Select(x => x.UnkT08).Where(k => k != 0).ToHashSet();
            var sfxKeys = sfx.Select(x => x.UnkT04).Where(k => k != 0).ToHashSet();
            Console.WriteLine($"# MSB2 {ds2Map}: {sounds.Count} Sound regions (keys {string.Join(",", soundKeys.Order())}), {sfx.Count} SFX regions (keys {string.Join(",", sfxKeys.Order())})");
            // the event ESD's keys
            string esdPath = Path.Combine(packDir, "gamedata", "ezstate", $"event_{ds2Map}.esd");
            if (File.Exists(esdPath))
            {
                var esd = ESD.Read(esdPath);
                var used = esd.StateGroups.SelectMany(gp => gp.Value.Values).SelectMany(st => st.EntryCommands)
                    .Where(c => c.CommandID is 131201 or 131202 or 131203 or 131503).Select(c => (c.CommandID, Arg: Esd.Expr(c.Arguments[0]))).Distinct().ToList();
                foreach (var (id, arg) in used)
                    if (int.TryParse(arg, out int k) && !(id == 131503 ? sfxKeys : soundKeys).Contains(k)) Fail($"event ESD {id}({k}): no such {(id == 131503 ? "SFX" : "Sound")} region key");
                Console.WriteLine($"# event ESD uses: {string.Join(" ", used.Select(u => $"{u.CommandID}({u.Arg})"))}");
            }
            // FMOD names
            var fevbnd = BND4.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(packDir, "gamedata", "sound", "frpg2.fevbnd.dcx"))));
            var vanilla = BND4.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine("data", "ds2", "sound", "frpg2.fevbnd.dcx"))));
            var vanillaNames = vanilla.Files.Select(f => f.Name).ToHashSet();
            var ds2Idx = SoundSurvey.FevIndex(vanilla.Files.Where(f => f.Name.EndsWith(".fev")).Select(f => (Path.GetFileNameWithoutExtension(f.Name), f.Bytes)));
            var ours = fevbnd.Files.Where(f => f.Name.EndsWith(".fev") && !vanillaNames.Contains(f.Name)).ToList();
            var ourIdx = SoundSurvey.FevIndex(ours.Select(f => (Path.GetFileNameWithoutExtension(f.Name), f.Bytes)));
            Console.WriteLine($"# fevbnd: {fevbnd.Files.Count} entries, ours: {string.Join(", ", ours.Select(f => Path.GetFileNameWithoutExtension(f.Name) + $" {f.Bytes.Length} B"))}");
            var collide = ourIdx.Keys.Where(ds2Idx.ContainsKey).ToList();
            if (collide.Count > 0) Fail($"DS1 projects define {collide.Count} names DS2 defines: {string.Join(" ", collide.Take(12))}");
            foreach (string name in plan.Regions.Select(r => r.Ds2Event).Concat(plan.Effects.Select(e => e.Ds2Event)).Distinct())
            {
                if (!ourIdx.TryGetValue(name, out var owners)) { Fail($"{name}: defined by no DS1 project in the fevbnd"); continue; }
                if (owners.Count > 1) Fail($"{name}: defined by {string.Join("+", owners)}");
                var proj = ours.First(f => Path.GetFileNameWithoutExtension(f.Name) == owners[0]);
                // wave banks = the bank-like strings after the header's project name (FEV1: version, two sizes, lstr name)
                int body = 0x14 + BitConverter.ToInt32(proj.Bytes, 0x10);
                var banks = System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(proj.Bytes, body, proj.Bytes.Length - body), @"(?<=[\x00-\x40])frpg2?_[a-z]*m(?:[0-9]{2,4}|ain)(?=\x00)")
                    .Select(m => m.Value).Distinct().ToList();
                bool reach = banks.Count > 0 && banks.All(b => File.Exists(Path.Combine(packDir, "gamedata", "sound", b + ".bnd")) && BND4.Read(Path.Combine(packDir, "gamedata", "sound", b + ".bnd")).Files.Any(f => f.ID == 1)
                    || File.Exists(Path.Combine(packDir, "title", "sound", b + ".fsb")));
                if (!reach) Fail($"{name} ({owners[0]}): wave bank(s) {string.Join(",", banks)} neither preloaded per map nor loose");
                Console.WriteLine($"  {name}: {owners[0]}, wave bank {string.Join(",", banks)} {(reach ? "reachable" : "MISSING")}");
            }
            // session 11 (snd11), rule SND7: the pack's object TAEs of this DS2 map's area (timeact/obj/oAA_*.tae): every
            // 502000 sound names an event one DS1 project of the fevbnd defines (kind 100: "o" + model × 1000 + id; 0xFF:
            // "a" + id), whose wave banks are reachable; no template sound event (502001/502002) left
            string objDir = Path.Combine(packDir, "gamedata", "timeact", "obj");
            int objEvents = 0, objTaes = 0;
            foreach (string f in Directory.Exists(objDir) ? Directory.GetFiles(objDir, $"o{ds2Map.Substring(1, 2)}_*.tae") : [])
            {
                string obj = Path.GetFileNameWithoutExtension(f);
                int modelNo = int.Parse(obj.Substring(1, 2) + obj.Substring(4, 4));
                var tae = new Formats.Ds2Tae(File.ReadAllBytes(f));
                objTaes++;
                foreach (long a in tae.Records.Keys)
                    foreach (var e in tae.EventsFull(a, 16))
                    {
                        if (e.Type is 502001 or 502002) Fail($"{obj} anim {a}: template sound event {e.Type} left (rule SND7)");
                        if (e.Type != 502000) continue;
                        int id = BitConverter.ToInt32(e.Params, 0);
                        string? nm = e.Params[4] switch { 100 => $"o{modelNo * 1000 + id:D9}", 0xFF => $"a{id:D9}", _ => null };
                        if (nm == null) { Fail($"{obj} anim {a}: 502000 kind {e.Params[4]} (rule SND7 writes 100 / 0xFF)"); continue; }
                        objEvents++;
                        if (!ourIdx.TryGetValue(nm, out var own) || own.Count != 1) { Fail($"{obj} anim {a}: {nm} defined by {(own == null ? "no DS1 project" : string.Join("+", own))} of the fevbnd"); continue; }
                        var pj = ours.First(x => Path.GetFileNameWithoutExtension(x.Name) == own[0]).Bytes;
                        var bad = Build.Chr.Ds1ChrSound.Fev1Header(pj).Banks.Select(b => b.Name).Where(b => !(File.Exists(Path.Combine(packDir, "title", "sound", b + ".fsb"))
                            || File.Exists(Path.Combine(packDir, "gamedata", "sound", b + ".bnd")) && BND4.Read(Path.Combine(packDir, "gamedata", "sound", b + ".bnd")).Files.Any(x => x.ID == 1))).ToList();
                        if (bad.Count > 0) Fail($"{obj}: {nm} ({own[0]}): wave bank(s) {string.Join(",", bad)} unreachable");
                    }
            }
            Console.WriteLine($"# object sounds (SND7): {objTaes} object TAEs of o{ds2Map.Substring(1, 2)}_*, {objEvents} DS1 sound events, each named once in the fevbnd with reachable banks unless FAILed above");
        }
        Console.WriteLine(fails == 0 ? "sound-check: OK" : $"sound-check: {fails} failure(s)");
        return fails == 0 ? 0 : 1;
    }
}
