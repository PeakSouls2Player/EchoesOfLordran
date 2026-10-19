using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 10 (asy-items i2) checks:
/// <list type="bullet">
/// <item><c>ring-check PACK</c> (rule G9): every ported ring (ItemParam 43M-49M with a ringParamId) has its RingParam row,
/// icon file, name/short/long text in the pack's FMGs, and — when specialEffectId is set — an SpEffectRing event of the
/// same id whose instructions are all kinds vanilla SpEffectRing uses, with every effect (SFX) id below 30000 (rule
/// B2b-id); no vanilla ring row (ItemParam / RingParam 40M-42M) and no vanilla SpEffectRing event differs from DS2's.</item>
/// <item><c>pickup-ids PACK [--write-table FILE]</c> (rule PK1): lists the build's "PK1 new pickup id" notes (treasures
/// whose id isn't in Mappings/pickup-ids.tsv yet); --write-table appends them to FILE.</item>
/// </list>
/// </summary>
public static class ItemsCheck
{
    public static int Rings(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: ring-check PACK"); return 1; }
        string pack = args[1];
        var mine = ParamStore.LoadDs2(Path.Combine(pack, "title"), ExtractDs2.DefaultOut);
        var van = ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        Script Load(string reg) => new(BND4.Read(Ds2RegulationCipher.Decrypt(File.ReadAllBytes(reg))));
        var myRing = Load(Path.Combine(pack, "title", "enc_regulation.bnd.dcx")).Get("SpEffectRing");
        var vanRing = Load(Path.Combine(Program.DefaultDs2Root, "enc_regulation.bnd.dcx")).Get("SpEffectRing");
        var kinds = vanRing.Events.SelectMany(e => e.Instructions).Select(i => (i.Bank, i.ID)).ToHashSet();
        string Fmg(string n) => Path.Combine(pack, "gamedata", "menu", "text", "english", n + ".fmg");
        FMG? Read(string n) => File.Exists(Fmg(n)) ? FMG.Read(Fmg(n)) : null;
        var names = Read("itemname"); var shorts = Read("simpleexplanation"); var longs = Read("detailedexplanation");
        string? T(FMG? f, int id) => f?.Entries.FirstOrDefault(e => e.ID == id)?.Text;
        int problems = 0, rings = 0;
        void Bad(string s) { problems++; Console.WriteLine("  PROBLEM " + s); }

        foreach (var row in mine.Params["ItemParam"].Rows.Where(r => r.ID is >= 43_000_000 and < 50_000_000 && Convert.ToInt32(r["ringParamId"].Value) != -1))
        {
            rings++;
            int id = row.ID, eff = Convert.ToInt32(row["specialEffectId"].Value);
            if (Convert.ToInt32(row["ringParamId"].Value) != id) Bad($"{id}: ringParamId {row["ringParamId"].Value}");
            if (mine.Params["RingParam"][id] == null) Bad($"{id}: no RingParam row");
            if (!File.Exists(Path.Combine(pack, "gamedata", "menu", "tex", "icon", $"ic_{id:D10}.tpf"))) Bad($"{id}: no icon ic_{id:D10}.tpf");
            string? name = T(names, id);
            if (string.IsNullOrEmpty(name)) Bad($"{id}: no itemname");
            if (string.IsNullOrEmpty(T(shorts, id))) Bad($"{id}: no simpleexplanation");
            if (string.IsNullOrEmpty(T(longs, id))) Bad($"{id}: no detailedexplanation");
            string effect = "no effect";
            if (eff != -1)
            {
                var ev = myRing.Events.FirstOrDefault(e => e.ID == eff);
                if (eff != id) Bad($"{id}: specialEffectId {eff}");
                if (ev == null) Bad($"{id}: no SpEffectRing event {eff}");
                else
                {
                    foreach (var i in ev.Instructions)
                    {
                        if (!kinds.Contains((i.Bank, i.ID))) Bad($"{id}: instruction {i.Bank}:{i.ID} not used by any vanilla ring");
                        if (i.Bank == 100040 && i.ArgData.Length >= 4 && BitConverter.ToInt32(i.ArgData, 0) >= 30000) Bad($"{id}: effect id {BitConverter.ToInt32(i.ArgData, 0)} >= 30000");
                    }
                    effect = string.Join(" ", ev.Instructions.Select(i => $"{i.Bank}:{i.ID}[{Convert.ToHexString(i.ArgData)}]"));
                }
            }
            Console.WriteLine($"  ring {id} {name}: RingParam weight {mine.Params["RingParam"][id]?["weight"].Value} durability {mine.Params["RingParam"][id]?["durability"].Value}; {effect}");
        }
        // vanilla untouched
        foreach (string p in new[] { "ItemParam", "RingParam" })
            foreach (var v in van.Params[p].Rows.Where(r => r.ID is >= 40_000_000 and < 43_000_000))
            {
                var m = mine.Params[p][v.ID];
                if (m == null) { Bad($"vanilla {p} {v.ID} missing"); continue; }
                for (int c = 0; c < v.Cells.Count; c++)
                    if (!Equals(v.Cells[c].Value, m.Cells[c].Value) && v.Cells[c].Value is not byte[]) { Bad($"vanilla {p} {v.ID} {v.Cells[c].Def.InternalName} changed"); break; }
            }
        foreach (var v in vanRing.Events)
        {
            var m = myRing.Events.FirstOrDefault(e => e.ID == v.ID);
            if (m == null || m.Instructions.Count != v.Instructions.Count || m.Instructions.Zip(v.Instructions).Any(z => z.First.Bank != z.Second.Bank || z.First.ID != z.Second.ID || !z.First.ArgData.SequenceEqual(z.Second.ArgData)))
                Bad($"vanilla SpEffectRing event {v.ID} changed");
        }
        Console.WriteLine($"ring-check {pack}: {rings} ported rings, {myRing.Events.Count - vanRing.Events.Count} new SpEffectRing events, {problems} problems");
        return problems == 0 ? 0 : 2;
    }

    sealed record Script(BND4 Bnd)
    {
        public EMEVD Get(string name) => EMEVD.Read(Bnd.Files.First(f => Path.GetFileNameWithoutExtension(f.Name).Equals(name, StringComparison.OrdinalIgnoreCase)).Bytes);
    }

    public static int PickupIds(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: pickup-ids PACK [--write-table FILE]"); return 1; }
        var lines = File.ReadLines(Path.Combine(args[1], "build.log")).Where(l => l.StartsWith("PK1 new pickup id\t")).Select(l => l.Split('\t')).ToList();
        foreach (var c in lines) Console.WriteLine($"  new: {c[1]} {c[2]} → {c[3]}");
        int w = Array.IndexOf(args, "--write-table");
        if (w > 0 && w + 1 < args.Length && lines.Count > 0)
        {
            File.AppendAllText(args[w + 1], string.Concat(lines.Select(c => $"{c[1]}\t{c[2]}\t{c[3]}\n"))); // LF, like the table
            Console.WriteLine($"appended {lines.Count} rows to {args[w + 1]}");
        }
        Console.WriteLine($"pickup-ids {args[1]}: {lines.Count} pickup ids not in the table (rule PK1)");
        return 0;
    }

    /// <summary>
    /// <c>ring-census</c> (fix pass, rule R5): every DS1 `4:5 IF Character Has SpEffect` whose SpEffect is a ring's
    /// (EquipParamAccessory.refId), over common.emevd and every DS1 map script, per ring: the DS2 item it maps to (item map /
    /// R1), the tests on the player and on other characters; 4:5s with a parameterised SpEffect are listed (the census can't
    /// classify them). Then a self-test: for every tested ring, a one-instruction DS1 event (4:5 MAIN on the player, should
    /// have 1 and 0) through the event translator (Asylum context, report mode) and its compiled wait.
    /// </summary>
    public static int RingCensus(string[] args)
    {
        var ds1 = new Build.Ds1Source(Program.DefaultDsrRoot);
        var tests = Build.RingPorter.EventTests(ds1);
        var bySp = Build.RingPorter.RingsBySpEffect(ds1);
        int onPlayer = 0, other = 0, problems = 0;
        foreach (var (ring, list) in tests.OrderBy(k => k.Key))
        {
            var acc = ds1.Params.Params["EquipParamAccessory"][ring]!;
            int sp = Convert.ToInt32(acc["refId"].Value);
            int? item = Build.RingPorter.ItemForSpEffect(null, ds1, sp);
            var map = Build.Chr.Ds1Drops.Mapping("EquipParamAccessory", ring);
            Console.WriteLine($"ring {ring} {ds1.Text("Accessory_name_", ring)} (SpEffect {sp}{(bySp[sp].Count > 1 ? $", shared by rings {string.Join("/", bySp[sp])}" : "")}) → DS2 {(item?.ToString() ?? "none")} ({map?.Result ?? "not in item map"})");
            foreach (var g in list.GroupBy(t => (t.Map, t.Event)))
                Console.WriteLine($"  {g.Key.Map} event {g.Key.Event}: " + string.Join(", ", g.Select(t => $"[{t.Index}] {(t.Player ? "player" : t.Entity < 0 ? "unresolved parameter" : $"chr {t.Entity}")} {(t.Has ? "has" : "has not")}")));
            onPlayer += list.Count(t => t.Player);
            other += list.Count(t => !t.Player);
            if (item == null) { problems++; Console.WriteLine("  PROBLEM no DS2 item for this ring (R5 can't translate its tests)"); }
        }
        var unresolved = Build.RingPorter.UnresolvedEventTests(ds1);
        Console.WriteLine($"census: {tests.Count} rings tested by DS1 events, {onPlayer} tests on the player (R5), {other} on other characters (UNMAPPED); {unresolved.Count} 4:5 with a parameterised SpEffect{(unresolved.Count > 0 ? ": " + string.Join(", ", unresolved) : "")}");

        // self-test through the translator
        var evd = new EMEVD();
        var root = new EMEVD.Event(0);
        evd.Events.Add(root);
        long next = 900000;
        var names = new Dictionary<long, string>();
        foreach (int ring in tests.Keys.Order())
        {
            int sp = Convert.ToInt32(ds1.Params.Params["EquipParamAccessory"][ring]!["refId"].Value);
            foreach (bool has in new[] { true, false })
            {
                long id = next++;
                var a = new byte[16];
                BitConverter.GetBytes(0).CopyTo(a, 0);           // MAIN
                BitConverter.GetBytes(10000).CopyTo(a, 4);       // the player
                BitConverter.GetBytes(sp).CopyTo(a, 8);
                a[12] = has ? (byte)1 : (byte)0;
                var ev = new EMEVD.Event(id);
                ev.Instructions.Add(new EMEVD.Instruction(4, 5, a));
                evd.Events.Add(ev);
                var init = new byte[12];
                BitConverter.GetBytes(0).CopyTo(init, 0);
                BitConverter.GetBytes((uint)id).CopyTo(init, 4);
                root.Instructions.Add(new EMEVD.Instruction(2000, 0, init));
                names[id] = $"ring {ring} SpEffect {sp} {(has ? "has" : "has not")}";
            }
        }
        var ctx = new Build.Events.AsylumContext(null, ds1);
        var t = new Build.Events.EmevdTranslator(evd, ctx, EmevdDump.Defs());
        int gid = 39000;
        t.TranslateMap((_, _) => gid++);
        foreach (var r in t.Results.Where(r => r.EventId != 0))
        {
            var waits = r.States?.Values.SelectMany(s => s.Conditions).Select(c => Esd.Expr(c.Evaluator)).Where(e => e.Contains("131403")).Distinct().ToList() ?? [];
            bool ok = r.States != null && waits.Count == 1;
            if (!ok) problems++;
            Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} {names[r.EventId]}: {(r.States == null ? "SKIP " + string.Join("; ", r.Missing) : string.Join(" | ", waits))}");
        }
        Console.WriteLine($"ring-census: {problems} problems");
        return problems == 0 ? 0 : 2;
    }

    /// <summary>
    /// <c>k1b-check</c> (fix pass): rule K1b′ through the event translator on one-instruction DS1 events (Asylum context,
    /// report mode, Oscar 6024 and the Asylum Demon 1810800 placed): `4:2 HP ratio` on a non-boss NPC with ≤ 0 / == 0 →
    /// 140111 (death), ≤ 0.5 → 140121 (HP compare, unchanged), on the boss ≤ 0 → 140522 (boss defeated). Before K1b′ every
    /// non-boss form was 140121(g, 0, 5|0).
    /// </summary>
    public static int K1bCheck(string[] args)
    {
        var ds1 = new Build.Ds1Source(Program.DefaultDsrRoot);
        var cases = new (int Entity, int Cmp, float Ratio, string Want)[]
        {
            (6024, 5, 0f, "140111"), (6024, 0, 0f, "140111"), (6024, 5, 0.5f, "140121"), (6024, 2, 0f, "140121"), (1810800, 5, 0f, "140522"),
        };
        var evd = new EMEVD();
        var root = new EMEVD.Event(0);
        evd.Events.Add(root);
        for (int k = 0; k < cases.Length; k++)
        {
            var a = new byte[16];
            BitConverter.GetBytes(0).CopyTo(a, 0);                    // MAIN
            BitConverter.GetBytes(cases[k].Entity).CopyTo(a, 4);
            a[8] = (byte)cases[k].Cmp;                                 // comparison (u8 in a 4-byte slot)
            BitConverter.GetBytes(cases[k].Ratio).CopyTo(a, 12);
            var ev = new EMEVD.Event(910000 + k);
            ev.Instructions.Add(new EMEVD.Instruction(4, 2, a));
            evd.Events.Add(ev);
            var init = new byte[12];
            BitConverter.GetBytes((uint)(910000 + k)).CopyTo(init, 4);
            root.Instructions.Add(new EMEVD.Instruction(2000, 0, init));
        }
        var gens = new Dictionary<int, int> { [6024] = Build.Events.EntityRules.Generator(6024), [1810800] = Build.Events.EntityRules.Generator(1810800) };
        var t = new Build.Events.EmevdTranslator(evd, new Build.Events.AsylumContext(null, ds1, gens), EmevdDump.Defs());
        int gid = 39100, problems = 0;
        t.TranslateMap((_, _) => gid++);
        foreach (var r in t.Results.Where(r => r.EventId != 0))
        {
            var c = cases[r.EventId - 910000];
            var cmds = r.States?.Values.SelectMany(s => s.EntryCommands).Where(x => x.CommandID is 140111 or 140121 or 140522 or 140523)
                .Select(x => $"{x.CommandID}({string.Join(", ", x.Arguments.Select(Esd.Expr))})").Distinct().ToList() ?? [];
            bool ok = cmds.Count == 1 && cmds[0].StartsWith(c.Want + "(");
            if (!ok) problems++;
            Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} 4:2 chr {c.Entity} cmp {c.Cmp} ratio {c.Ratio} → {(r.States == null ? "SKIP " + string.Join("; ", r.Missing) : string.Join(" | ", cmds))} (want {c.Want})");
        }
        Console.WriteLine($"k1b-check: {problems} problems");
        return problems == 0 ? 0 : 2;
    }
}
