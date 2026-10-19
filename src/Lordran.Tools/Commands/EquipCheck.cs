using Lordran.Tools.Build;
using Lordran.Tools.Formats;
using SoulsFormats;
using static Lordran.Tools.Build.ParamCells;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (track equip11, PLAN milestone 7): offline checks of the equipment batch in a built pack.
/// <list type="bullet">
/// <item><c>armor-check PACK</c> (rules AR1-AR7): every ported DS1 armour item (ItemParam 24M, ArmorPorter ids) has its
/// ArmorParam / ArmorReinforceParam rows (min ≤ max, DS2 +5/+10 track, a ReinforceCostParam row), an EquipInterfereParam row,
/// icon (its texture named after the item), name and description, and its part binders for both sexes (DS2 modelGender 1:
/// _m and _f; 0: one _m file both sexes use, as 93 of DS2's 121 gender-0 pieces), main and _l, gamedata and partslq, each
/// FLVER texture found in its own binder under a name that starts with the part (unique: "BD_4017_M_…"); an effect is a
/// SpEffectArmor event of the item's id with instruction kinds vanilla SpEffectArmor / SpEffectRing use; no vanilla armour
/// row or SpEffectArmor event differs from DS2's. Coverage: the item map's 197 "new" pieces.</item>
/// <item><c>spell-check PACK</c> (SP1-SP8): every ported DS1 spell (ItemParam in the 31.5M/32.5M/33.5M/34.5M blocks) has its
/// SpellParam (category of its block's school, slots 1-3, casts 1-99), every bullet / damage / child-bullet id it names, every
/// id its SpEffectSpell events name inside its block, instruction kinds vanilla SpEffectSpell uses, icon, name, description;
/// no vanilla spell row or event differs. Coverage: the 23 "new" Magic rows (ported or skipped with a reason).</item>
/// <item><c>goods-check PACK</c> (GD1-GD6): every ported DS1 goods (ItemParam 52M-55M) has icon, name, short and long text, its
/// effect event (own id or the DS2 equivalent's), boss souls a positive soul amount; no vanilla goods row or
/// SpEffectActiveItem event differs. Coverage: the 58 "new"/"system-new" goods.</item>
/// </list>
/// </summary>
public static partial class EquipCheck
{
    sealed class Ctx
    {
        public required string PackDir;
        public required ParamStore Mine, Van;
        public required BND4 MyReg, VanReg;
        public FMG? Names, Shorts, Longs;
        public int Problems;
        public void Bad(string s) { Problems++; Console.WriteLine("  PROBLEM " + s); }
        public EMEVD Script(BND4 reg, string name) => EMEVD.Read(reg.Files.First(f => Path.GetFileNameWithoutExtension(f.Name).Equals(name, StringComparison.OrdinalIgnoreCase)).Bytes);
        public string? T(FMG? f, int id) => f?.Entries.FirstOrDefault(e => e.ID == id)?.Text;
        public void Texts(int id, bool needShort)
        {
            if (string.IsNullOrWhiteSpace(T(Names, id))) Bad($"{id}: no itemname");
            if (string.IsNullOrWhiteSpace(T(Longs, id))) Bad($"{id}: no detailedexplanation");
            if (needShort && string.IsNullOrWhiteSpace(T(Shorts, id))) Bad($"{id}: no simpleexplanation");
        }
        public void Icon(int id)
        {
            string p = Path.Combine(PackDir, "gamedata", "menu", "tex", "icon", $"ic_{id:D10}.tpf");
            if (!File.Exists(p)) { Bad($"{id}: no icon ic_{id:D10}.tpf"); return; }
            var tpf = TPF.Read(p);
            if (tpf.Textures.Count != 1 || !tpf.Textures[0].Name.Contains($"{id:D10}")) Bad($"{id}: icon texture named {string.Join(",", tpf.Textures.Select(t => t.Name))}, not after the item");
        }
        /// <summary>Vanilla rows of a param (in an id range) must be unchanged in the pack.</summary>
        public void VanillaRows(string param, Func<int, bool> range, Func<int, string, bool>? allowed = null)
        {
            foreach (var v in Van.Params[param].Rows.Where(r => range(r.ID)))
            {
                var m = Mine.Params[param][v.ID];
                if (m == null) { Bad($"vanilla {param} {v.ID} missing"); continue; }
                for (int c = 0; c < v.Cells.Count; c++)
                    if (!Equals(v.Cells[c].Value, m.Cells[c].Value) && v.Cells[c].Value is not byte[] && allowed?.Invoke(v.ID, v.Cells[c].Def.InternalName) != true) { Bad($"vanilla {param} {v.ID} {v.Cells[c].Def.InternalName} changed"); break; }
            }
        }
        public int VanillaEvents(EMEVD van, EMEVD mine, string name)
        {
            foreach (var v in van.Events)
            {
                var m = mine.Events.FirstOrDefault(e => e.ID == v.ID);
                if (m == null || m.Instructions.Count != v.Instructions.Count || m.Instructions.Zip(v.Instructions).Any(z => z.First.Bank != z.Second.Bank || z.First.ID != z.Second.ID || !z.First.ArgData.SequenceEqual(z.Second.ArgData)))
                    Bad($"vanilla {name} event {v.ID} changed");
            }
            return mine.Events.Count - van.Events.Count;
        }
    }

    static Ctx? Load(string[] args, string usage)
    {
        if (args.Length < 2 || !Directory.Exists(args[1])) { Console.Error.WriteLine($"usage: {usage} PACK"); return null; }
        string pack = args[1];
        BND4 Reg(string path) => BND4.Read(Ds2RegulationCipher.Decrypt(File.ReadAllBytes(path)));
        string Fmg(string n) => Path.Combine(pack, "gamedata", "menu", "text", "english", n + ".fmg");
        FMG? Read(string n) => File.Exists(Fmg(n)) ? FMG.Read(Fmg(n)) : null;
        return new Ctx
        {
            PackDir = pack,
            Mine = ParamStore.LoadDs2(Path.Combine(pack, "title"), ExtractDs2.DefaultOut),
            Van = ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut),
            MyReg = Reg(Path.Combine(pack, "title", "enc_regulation.bnd.dcx")),
            VanReg = Reg(Path.Combine(Program.DefaultDs2Root, "enc_regulation.bnd.dcx")),
            Names = Read("itemname"), Shorts = Read("simpleexplanation"), Longs = Read("detailedexplanation"),
        };
    }

    static readonly string[] Prefix = ["", "hd", "bd", "am", "lg"], Folder = ["", "head", "body", "arm", "leg"];

    public static int Armor(string[] args)
    {
        var c = Load(args, "armor-check");
        if (c == null) return 1;
        var myScript = c.Script(c.MyReg, ArmorItems.Script);
        var kinds = c.Script(c.VanReg, ArmorItems.Script).Events.Concat(c.Script(c.VanReg, RingPorter.Script).Events)
            .SelectMany(e => e.Instructions).Select(i => (i.Bank, i.ID)).ToHashSet();
        var texOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // texture name → first binder set
        int n = 0, parts = 0, grouped = 0;
        double maxFar = 0;
        var ported = new HashSet<int>();
        // session 11 (outfit11): rig checks, AR8 culling map and rows
        var rig = new Lazy<Build.Chr.PlayerRig>(() => new Build.Chr.PlayerRig(Program.DefaultDsrRoot, ExtractDs2.DefaultOut));
        var ar8 = Build.Chr.ArmorCulling.Load();
        var ar8Groups = ar8.Values.ToHashSet();
        var ds1Prot = new Ds1Source(Program.DefaultDsrRoot).Params.Params["EquipParamProtector"];
        // items, then (session 11, outfit11) the NPC-only pieces (rules AR-N / N-OUT1: ArmorParam + parts, no item)
        var pieces = c.Mine.Params["ItemParam"].Rows.Where(r => ArmorItems.Ds1Id(r.ID) > 0 && I(r, "armorParamId") > 0).Select(r => (Item: (PARAM.Row?)r, Armor: I(r, "armorParamId")))
            .Concat(c.Mine.Params["ArmorParam"].Rows.Where(a => a.ID is >= 14_000_000 and < 14_500_000 && a.ID % 1000 / 100 == 1 && a.ID % 100 <= 3 && c.Mine.Params["ItemParam"][a.ID + 10_000_000] == null && c.Van.Params["ArmorParam"][a.ID] == null)
                .Select(a => (Item: (PARAM.Row?)null, Armor: a.ID))).ToList();
        int npcOnly = 0;
        foreach (var (item, armorId) in pieces)
        {
            n++;
            int id = item?.ID ?? armorId + 10_000_000, ds1 = ArmorItems.Ds1Id(id);
            if (item != null) ported.Add(ds1); else npcOnly++;
            var armor = c.Mine.Params["ArmorParam"][armorId];
            if (armor == null) { c.Bad($"{id}: no ArmorParam {armorId}"); continue; }
            if (c.Van.Params["ArmorParam"][armor.ID] != null) c.Bad($"{id}: ArmorParam {armor.ID} is a vanilla row");
            var rf = c.Mine.Params["ArmorReinforceParam"][I(armor, "armorReinforceId")];
            if (rf == null) c.Bad($"{id}: no ArmorReinforceParam {I(armor, "armorReinforceId")}");
            else
            {
                if (I(rf, "maxLevel") is not (5 or 10)) c.Bad($"{id}: maxLevel {I(rf, "maxLevel")}");
                if (c.Mine.Params["ReinforceCostParam"][I(rf, "reinforceCost")] == null) c.Bad($"{id}: no ReinforceCostParam {I(rf, "reinforceCost")}");
                foreach (string f in new[] { "Slash", "Thrust", "Strike", "Physical", "Magic", "Thunder", "Fire", "Dark", "Poison", "Bleeding", "Petrifaction", "Curse" })
                {
                    double lo = Get(rf, "min" + f), hi = Get(rf, "max" + f);
                    if (!double.IsFinite(lo) || !double.IsFinite(hi) || lo < 0 || lo > hi + 1e-3) c.Bad($"{id}: {f} {lo}/{hi}");
                }
                if (Get(rf, "minPhysical") <= 0) c.Bad($"{id}: no physical defence");
            }
            if (c.Mine.Params["EquipInterfereParam"][I(armor, "interfereId")] is not { } ir) c.Bad($"{id}: no EquipInterfereParam {I(armor, "interfereId")}");
            else if (ar8.Count > 0)
            {
                // AR8: the piece's own row; every mapped DS2 group set exactly when DS1's invisibleFlag is (both sexes)
                if (I(armor, "interfereId") != armor.ID) c.Bad($"{id}: interfereId {I(armor, "interfereId")}, AR8 wants its own row {armor.ID}");
                var flags = ds1Prot[ds1] is { } d1r ? Build.Chr.ArmorCulling.Ds1Flags(d1r) : [];
                foreach (var ((g, _), x) in ar8)
                    foreach (int sex in new[] { 0, 1 })
                        if ((I(ir, $"cullingGroup{x:D2}_{sex}") != 0) != flags.Contains(g)) { c.Bad($"{id}: interfere group {x}_{sex} vs DS1 invisibleFlag{g:D2}"); break; }
            }
            if (Get(armor, "weight") < 0 || Get(armor, "durability") <= 0) c.Bad($"{id}: weight {Get(armor, "weight")} durability {Get(armor, "durability")}");
            if (item != null) { c.Icon(id); c.Texts(id, false); }
            // effect
            int eff = item != null ? I(item, "specialEffectId") : -1;
            if (eff != -1)
            {
                var ev = myScript.Events.FirstOrDefault(e => e.ID == eff);
                if (eff != id || ev == null) c.Bad($"{id}: specialEffectId {eff} without its SpEffectArmor event");
                else foreach (var x in ev.Instructions.Where(x => !kinds.Contains((x.Bank, x.ID)))) c.Bad($"{id}: instruction {x.Bank}:{x.ID} not used by vanilla armour or rings");
            }
            // parts: both sexes, main + _l, gamedata + partslq
            int cat = I(armor, "armorCategory"), model = I(armor, "modelId");
            if (cat is < 1 or > 4) { c.Bad($"{id}: armorCategory {cat}"); continue; }
            if (cat != ds1 / 1000 % 10 + 1) c.Bad($"{id}: armorCategory {cat}, DS1 slot {ds1 / 1000 % 10}");
            var sexes = I(armor, "modelGender") == 1 ? new[] { "m", "f" } : new[] { "m" };
            foreach (string sex in sexes)
                foreach (string lod in new[] { "", "_l" })
                {
                    string name = $"{Prefix[cat]}_{model}_{sex}{lod}.bnd";
                    string g = Path.Combine(c.PackDir, "gamedata", "model", "parts", Folder[cat], name);
                    string q = Path.Combine(c.PackDir, "partslq", "model_lq", "parts", Folder[cat], name);
                    if (!File.Exists(g)) { c.Bad($"{id}: no gamedata part {name}"); continue; }
                    if (!File.Exists(q)) c.Bad($"{id}: no partslq part {name}");
                    parts++;
                    var bnd = BND4.Read(g);
                    var tex = bnd.Files.Where(f => f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase)).SelectMany(f => TPF.Read(f.Bytes).Textures).ToList();
                    string part = $"{Prefix[cat]}_{model}_{sex}".ToUpperInvariant();
                    foreach (var t in tex)
                    {
                        if (!t.Name.StartsWith(part + "_", StringComparison.OrdinalIgnoreCase)) c.Bad($"{id}: texture {t.Name} in {name} not named after its part {part}");
                        // one name → one part set (main and _l of the same part share it by design)
                        string owner = $"{Prefix[cat]}_{model}_{sex}";
                        if (texOwner.TryGetValue(t.Name, out var o) && o != owner) c.Bad($"{id}: texture {t.Name} in {owner} and {o}");
                        texOwner.TryAdd(t.Name, owner);
                    }
                    var flv = bnd.Files.FirstOrDefault(f => f.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase));
                    if (flv == null) { c.Bad($"{id}: {name} has no FLVER"); continue; }
                    var names = tex.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    // session 11 (outfit11): binding to DS2's player skeleton, no exploded vertices, AR8 culling groups
                    var fl = FLVER2.Read(flv.Bytes);
                    foreach (string pr in rig.Value.Check(fl)) c.Bad($"{id}: {name} {pr}");
                    var world = ChrCompare.World(fl);
                    double far = 0;
                    foreach (var m in fl.Meshes)
                        foreach (var v in m.Vertices)
                        {
                            int k = Enumerable.Range(0, 4).OrderByDescending(j => v.BoneWeights[j]).First();
                            if (v.BoneIndices[k] >= m.BoneIndices.Count) continue;
                            far = Math.Max(far, (v.Position - world[m.BoneIndices[v.BoneIndices[k]]].Translation).Length());
                            if (!float.IsFinite(v.Position.X + v.Position.Y + v.Position.Z) || Math.Abs(v.Position.X) > 1.5 || Math.Abs(v.Position.Z) > 1.5 || v.Position.Y is < -0.3f or > 2.5f) { c.Bad($"{id}: {name} vertex at {v.Position} outside a human's frame"); goto doneVerts; }
                        }
                    doneVerts:
                    if (far > 1.2) c.Bad($"{id}: {name} a vertex {far:0.00} m from its main bone (exploded?)");
                    maxFar = Math.Max(maxFar, far);
                    if (fl.Materials.Select(m => m.Name).Distinct().Count() != fl.Materials.Count) c.Bad($"{id}: {name} material names not unique");
                    foreach (var m in fl.Materials)
                    {
                        int cg = Build.Chr.ArmorCulling.Ds2Group(fl, m);
                        if (cg == 0) continue;
                        grouped++;
                        if (!ar8Groups.Contains(cg)) c.Bad($"{id}: {name} material {m.Name} culling group {cg} is not an AR8 group");
                    }
                    foreach (var m in fl.Materials)
                        foreach (var mt in m.Textures.Where(x => !string.IsNullOrEmpty(x.Path) && x.ParamName != "g_BreakBumpmapTexture"))
                            if (!names.Contains(Path.GetFileNameWithoutExtension(mt.Path))) c.Bad($"{id}: {name} material {m.Name} needs {Path.GetFileNameWithoutExtension(mt.Path)}, not in the binder");
                    if (!bnd.Files.Any(f => f.Name.EndsWith(".acb", StringComparison.OrdinalIgnoreCase))) c.Bad($"{id}: {name} has no ACB");
                }
            Console.WriteLine($"  armour {id} (DS1 {ds1}) {c.T(c.Names, id)}: model {model} {string.Join("/", sexes)}, +{(rf == null ? 0 : I(rf, "maxLevel"))}, phys {(rf == null ? 0 : Get(rf, "minPhysical")):0.#}, poise {Get(armor, "strong"):0.#}, weight {Get(armor, "weight"):0.#}{(eff != -1 ? ", effect" : "")}");
        }
        // vanilla untouched
        c.VanillaRows("ArmorParam", _ => true);
        c.VanillaRows("ArmorReinforceParam", _ => true);
        // AR8 empty slot: DS2's naked rows may set the free groups the culling map gives an empty slot (no vanilla part has them)
        var nakedGroups = Build.Chr.ArmorCulling.NakedGroups();
        var nakedRows = Enumerable.Range(0, 4).SelectMany(s => new[] { 11001100 + s, 11002100 + s }).Select(a => c.Van.Params["ArmorParam"][a] is { } r ? I(r, "interfereId") : -1).ToHashSet();
        c.VanillaRows("EquipInterfereParam", _ => true, (row, cell) => nakedRows.Contains(row) && nakedGroups.Any(g => cell.StartsWith($"cullingGroup{g:D2}_")));
        c.VanillaRows("ItemParam", i => i is >= 10_000_000 and < 30_000_000);
        int newEvents = c.VanillaEvents(c.Script(c.VanReg, ArmorItems.Script), myScript, ArmorItems.Script);
        var ds1Src = new Ds1Source(Program.DefaultDsrRoot);
        var expected = ArmorItems.NewIds().Where(d => ArmorItems.IsItem(ds1Src, d)).ToList(); // AR1: DS1's body/transformation rows aren't items
        // a piece DS2 has under another name (ItemMap.Renamed: Elite Knight Gauntlets = DS2 Elite Knight Gloves) is DS2's
        var renamed = expected.Where(d => !ported.Contains(d) && ItemMap.RenamedName(ds1Src.Text("Armor_name_", d)) != ds1Src.Text("Armor_name_", d)).ToList();
        var missing = expected.Where(d => !ported.Contains(d) && !renamed.Contains(d)).ToList();
        foreach (int d in missing) c.Bad($"DS1 armour {d} {ds1Src.Text("Armor_name_", d)} not in the pack");
        Console.WriteLine($"armor-check {c.PackDir}: {n - npcOnly} ported armour items ({ported.Intersect(expected).Count()} + {renamed.Count} DS2 duplicates / {expected.Count} of the item map's new wearable pieces{(missing.Count > 0 ? "; not in the pack: " + string.Join(" ", missing.Take(30)) + (missing.Count > 30 ? " …" : "") : "")}), {npcOnly} NPC-only pieces (AR-N / N-OUT1), {parts} part binders, {texOwner.Count} texture names, {newEvents} new SpEffectArmor events, {grouped} AR8-culled materials, max vertex-bone distance {maxFar:0.00} m, {c.Problems} problems");
        return c.Problems == 0 ? 0 : 2;
    }

    static readonly (int Block, int Category)[] SpellBlocks = [(31_500_000, 0), (32_500_000, 1), (33_500_000, 2), (34_500_000, 3)];

    public static int Spells(string[] args)
    {
        var c = Load(args, "spell-check");
        if (c == null) return 1;
        var myScript = c.Script(c.MyReg, SpellPorter.Script);
        var vanScript = c.Script(c.VanReg, SpellPorter.Script);
        var kinds = vanScript.Events.SelectMany(e => e.Instructions).Select(i => (i.Bank, i.ID)).ToHashSet();
        int n = 0;
        var names = new HashSet<string>();
        foreach (var item in c.Mine.Params["ItemParam"].Rows.Where(r => I(r, "spellParamId") > 0 && SpellBlocks.Any(b => r.ID >= b.Block && r.ID < b.Block + 500_000)))
        {
            n++;
            int id = item.ID;
            int category = SpellBlocks.First(b => id >= b.Block && id < b.Block + 500_000).Category;
            var sp = c.Mine.Params["SpellParam"][I(item, "spellParamId")];
            if (sp == null) { c.Bad($"{id}: no SpellParam"); continue; }
            if (I(item, "spellParamId") != id) c.Bad($"{id}: spellParamId {I(item, "spellParamId")}");
            if (I(sp, "spellCategory") != category) c.Bad($"{id}: spellCategory {I(sp, "spellCategory")}, block says {category}");
            if (I(sp, "equipSlotNum") is < 1 or > 3) c.Bad($"{id}: slots {I(sp, "equipSlotNum")}");
            for (int lv = 1; lv <= 10; lv++)
                if (I(sp, $"usageCountLv{lv}") is < 1 or > 99) c.Bad($"{id}: usageCountLv{lv} {I(sp, $"usageCountLv{lv}")}");
            int lo = id, hi = id + 5000;
            void Need(string param, int rowId, string why)
            {
                if (rowId <= 0) return;
                if (c.Mine.Params[param][rowId] == null) c.Bad($"{id}: {why} {param} {rowId} missing");
                else if (rowId < lo || rowId >= hi) { if (c.Van.Params[param][rowId] == null) c.Bad($"{id}: {why} {rowId} outside its block and not vanilla"); }
            }
            Need("BulletParam", I(sp, "baseSpellBulletId"), "bullet");
            Need("BulletParam", I(sp, "baseLeftHandSpellBulletId"), "left bullet");
            Need("PlayerDamageParam", I(sp, "baseSpellDamageId"), "damage");
            Need("PlayerDamageParam", I(sp, "baseLeftHandSpellDamageId"), "left damage");
            foreach (var b in c.Mine.Params["BulletParam"].Rows.Where(r => r.ID >= lo && r.ID < hi))
                foreach (string f in new[] { "", "02", "03" })
                {
                    Need("BulletParam", I(b, "childeBulletId" + f), $"bullet {b.ID} child");
                    Need("PlayerDamageParam", I(b, "childeDamageId" + f), $"bullet {b.ID} child damage");
                }
            var events = myScript.Events.Where(e => e.ID >= lo && e.ID < hi).ToList();
            foreach (var ev in events)
                foreach (var x in ev.Instructions)
                {
                    if (!kinds.Contains((x.Bank, x.ID))) c.Bad($"{id}: event {ev.ID} instruction {x.Bank}:{x.ID} not used by vanilla spells");
                    for (int o = 0; o + 4 <= x.ArgData.Length; o += 4)
                    {
                        int v = BitConverter.ToInt32(x.ArgData, o);
                        if (v >= lo && v < hi && !myScript.Events.Any(e => e.ID == v) && c.Mine.Params["BulletParam"][v] == null && c.Mine.Params["PlayerDamageParam"][v] == null)
                            c.Bad($"{id}: event {ev.ID} names {v}, which nothing in the block defines");
                    }
                }
            c.Icon(id);
            c.Texts(id, false);
            if (!names.Add(c.T(c.Names, id) ?? "")) c.Bad($"{id}: name {c.T(c.Names, id)} used twice");
            Console.WriteLine($"  spell {id} {c.T(c.Names, id)}: category {category}, int {I(sp, "intelligence")} fth {I(sp, "faith")}, slots {I(sp, "equipSlotNum")}, casts {I(sp, "usageCountLv1")}-{I(sp, "usageCountLv10")}, {c.Mine.Params["BulletParam"].Rows.Count(r => r.ID >= lo && r.ID < hi)} bullets, {c.Mine.Params["PlayerDamageParam"].Rows.Count(r => r.ID >= lo && r.ID < hi)} damages, {events.Count} events");
        }
        bool SpellRange(int i) => i is >= 31_000_000 and < 36_000_000;
        foreach (string p in new[] { "SpellParam", "BulletParam", "PlayerDamageParam", "SpellSoulConsumeParam" }) c.VanillaRows(p, _ => true);
        c.VanillaRows("ItemParam", SpellRange);
        int newEvents = c.VanillaEvents(vanScript, myScript, SpellPorter.Script);
        // coverage
        var expected = SpellPorter.NewIds();
        var ds1 = new Ds1Source(Program.DefaultDsrRoot);
        var porter = new SpellPorter(ds1, ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut), new Pack(Program.DefaultDs2Root, ExtractDs2.DefaultOut));
        int covered = 0;
        var tsv = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Mappings", "spell-templates.tsv")).Skip(1).Select(l => l.Split('\t')).ToDictionary(x => int.Parse(x[0]), x => x[1]);
        foreach (int d in expected)
        {
            if (porter.NotPorted(d, out _) is string why) { covered++; Console.WriteLine($"  DS1 {d} {ds1.Text("Magic_name_", d)}: skipped ({why})"); continue; }
            int newId = tsv.TryGetValue(d, out var school) ? SpellPorter.Id(d, school) : 0;
            if (newId > 0 && c.Mine.Params["ItemParam"][newId] != null) covered++;
            else c.Bad($"DS1 spell {d} {ds1.Text("Magic_name_", d)} not in the pack");
        }
        // session 11 (spell11): every DS1 spell (shared ones too) by DS2 rules, catalysts, shops/lots (SpellRules.cs)
        SpellRules(c, ds1, vanScript, myScript);
        Console.WriteLine($"spell-check {c.PackDir}: {n} ported spells, {covered}/{expected.Count} new DS1 spells covered (ported or skipped with a reason), {newEvents} new SpEffectSpell events, {c.Problems} problems");
        return c.Problems == 0 ? 0 : 2;
    }

    /// <summary>
    /// <c>regulation-check PACK</c>: the pack's enc_regulation is valid in the ways the game needs without running it:
    /// Smithbox's independent decryptor and ours give the same files; the same file names as vanilla; every param vanilla
    /// reads with the DS2 paramdefs reads the same way (type, row size), its row ids strictly increasing; every EMEVD
    /// script parses. Prints the params / scripts that differ from vanilla with their row / event counts.
    /// </summary>
    public static int Regulation(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: regulation-check PACK"); return 1; }
        string path = Path.Combine(args[1], "title", "enc_regulation.bnd.dcx");
        int problems = 0;
        void Bad(string s) { problems++; Console.WriteLine("  PROBLEM " + s); }
        var ours = BND4.Read(Ds2RegulationCipher.Decrypt(File.ReadAllBytes(path)));
        var theirs = BND4.Read(Ds2Regulation.Decrypt(path));
        if (ours.Files.Count != theirs.Files.Count || !ours.Files.Zip(theirs.Files).All(p => p.First.Name == p.Second.Name && p.First.Bytes.AsSpan().SequenceEqual(p.Second.Bytes)))
            Bad("our decryption and Smithbox's differ");
        var van = BND4.Read(Ds2RegulationCipher.Decrypt(File.ReadAllBytes(Path.Combine(Program.DefaultDs2Root, "enc_regulation.bnd.dcx"))));
        if (!van.Files.Select(f => f.Name).SequenceEqual(ours.Files.Select(f => f.Name))) Bad("file list differs from vanilla");
        var defs = Directory.GetFiles(Path.Combine(ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        var vanByName = van.Files.ToDictionary(f => f.Name);
        int changedParams = 0, changedScripts = 0;
        foreach (var f in ours.Files)
        {
            if (!vanByName.TryGetValue(f.Name, out var v) || v.Bytes.AsSpan().SequenceEqual(f.Bytes)) continue;
            if (f.Name.EndsWith(".emevd", StringComparison.OrdinalIgnoreCase))
            {
                try { var e = EMEVD.Read(f.Bytes); changedScripts++; Console.WriteLine($"  {Path.GetFileName(f.Name)}: {e.Events.Count} events (vanilla {EMEVD.Read(v.Bytes).Events.Count})"); }
                catch (Exception ex) { Bad($"{f.Name}: {ex.Message}"); }
                continue;
            }
            try
            {
                var pv = PARAM.Read(v.Bytes);
                var pm = PARAM.Read(f.Bytes);
                if (pv.ParamType != pm.ParamType) Bad($"{f.Name}: type {pm.ParamType} vs {pv.ParamType}");
                bool vd = pv.ApplyParamdefCarefully(defs), md = pm.ApplyParamdefCarefully(defs);
                if (vd != md) Bad($"{f.Name}: paramdef applies {md} (vanilla {vd})");
                for (int i = 1; i < pm.Rows.Count; i++)
                    if (pm.Rows[i].ID <= pm.Rows[i - 1].ID) { Bad($"{f.Name}: row ids not increasing at {pm.Rows[i].ID}"); break; }
                changedParams++;
                Console.WriteLine($"  {Path.GetFileName(f.Name)}: {pm.Rows.Count} rows (vanilla {pv.Rows.Count})");
            }
            catch (Exception ex) { Bad($"{f.Name}: {ex.Message}"); }
        }
        Console.WriteLine($"regulation-check {args[1]}: {ours.Files.Count} files, {changedParams} params and {changedScripts} scripts differ from vanilla, {problems} problems");
        return problems == 0 ? 0 : 2;
    }

    static HashSet<int>? vanillaEffectIds;

    public static int Goods(string[] args)
    {
        var c = Load(args, "goods-check");
        if (c == null) return 1;
        var myScript = c.Script(c.MyReg, GoodsPorter.ActiveItemScript);
        var vanScript = c.Script(c.VanReg, GoodsPorter.ActiveItemScript);
        int n = 0;
        var ds1 = new Ds1Source(Program.DefaultDsrRoot);
        foreach (var item in c.Mine.Params["ItemParam"].Rows.Where(r => r.ID is >= 52_000_000 and < 55_000_000 && c.Van.Params["ItemParam"][r.ID] == null))
        {
            n++;
            int id = item.ID;
            c.Icon(id);
            c.Texts(id, true);
            int eff = I(item, "specialEffectId");
            string what = "no effect";
            // a template's own effect id (Lenigrast's Key 50870000, Poison Moss 60070000) is DS2's: kept as vanilla has it
            var vanillaEffects = vanillaEffectIds ??= c.Van.Params["ItemParam"].Rows.Select(r => I(r, "specialEffectId")).Where(e => e > 0).ToHashSet();
            if (eff > 0 && eff != id && vanillaEffects.Contains(eff)) what = $"DS2 effect {eff} (its template's)";
            else if (eff > 0)
            {
                var ev = myScript.Events.FirstOrDefault(e => e.ID == eff);
                if (ev == null) c.Bad($"{id}: specialEffectId {eff} has no SpEffectActiveItem event");
                else if (eff == id)
                {
                    var souls = ev.Instructions.FirstOrDefault(x => x is { Bank: 100130, ID: 1 } && x.ArgData.Length >= 16);
                    if (souls == null || BitConverter.ToInt32(souls.ArgData, 12) <= 0) c.Bad($"{id}: own event without a soul amount");
                    what = $"boss soul, {(souls == null ? 0 : BitConverter.ToInt32(souls.ArgData, 12))} souls";
                }
                else if (vanScript.Events.All(e => e.ID != eff)) c.Bad($"{id}: specialEffectId {eff} is neither its own nor a vanilla event");
                else what = $"DS2 effect {eff}";
            }
            Console.WriteLine($"  goods {id} {c.T(c.Names, id)}: usage {I(item, "itemUsageParamId")}, {what}");
        }
        c.VanillaRows("ItemParam", i => i is >= 50_000_000 and < 70_000_000);
        int newEvents = c.VanillaEvents(vanScript, myScript, GoodsPorter.ActiveItemScript);
        var pack = new Pack(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        int covered = 0;
        foreach (int d in GoodsPorter.NewIds())
        {
            if (GoodsPorter.Ds2Duplicate(pack, ds1, d) > 0 || c.Mine.Params["ItemParam"][GoodsPorter.Id(d)] != null) covered++;
            else c.Bad($"DS1 goods {d} {ds1.Text("Item_name_", d)} not in the pack");
        }
        Console.WriteLine($"goods-check {c.PackDir}: {n} ported goods, {covered}/{GoodsPorter.NewIds().Count} new DS1 goods covered (ported or DS2's renamed duplicate), {newEvents} new SpEffectActiveItem events, {c.Problems} problems");
        return c.Problems == 0 ? 0 : 2;
    }
}
