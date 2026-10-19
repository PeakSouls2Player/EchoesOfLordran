using Lordran.Tools.Formats;
using SoulsFormats;
using static Lordran.Tools.Build.ParamCells;

namespace Lordran.Tools.Build;

/// <summary>
/// Session 11 (track equip11, PLAN milestone 7): DS1-only spells (item map Magic "new") as DS2 spells. Rules SP1-SP8
/// (docs\translation-rules.md):
/// <list type="bullet">
/// <item><b>SP1 which</b>: the 23 "new" Magic rows minus DS1's internal entries (maxQuantity &lt; 0 / slotLength 0: Karmic
/// Justice's explosion 5710 and its NPC copy 15710 are bullets DS1's exe fires, not spells) and spells DS2 has under
/// another name (Seek Guidance = DS2 Guidance 32220000, ItemMap.Renamed) → 21 spells. <c>build-pack --spells all|ID+ID</c>.</item>
/// <item><b>SP2 behaviour</b>: DS2 spells are ItemParam → SpellParam → BulletParam / PlayerDamageParam (+ child bullets) →
/// SpEffectSpell events (the damage/effect ids), all numbered inside the spell's 10,000-id block. DS1's bullet and effect
/// data can't be loaded by DS2 (different param layouts, DS1 SFX ids), so each spell takes the closest DS2 spell's chain as
/// template (<c>Mappings/spell-templates.tsv</c>: template, DS1 anchor, body, ops, and why), cloned into a new block with every
/// id of the template's block renumbered (rows, child-bullet fields, event ids, event arguments). When the template is of
/// another school, the SpellParam body (cast animations, category, chant/stamina) comes from a same-school DS2 spell
/// ("body"), so a sorcery is still cast with a staff, a miracle with a chime, a pyromancy with DS2's pyromancy flame.</item>
/// <item><b>SP3 ids</b>: school block + 500,000 + (DS1 id % 1000 / 10) × 5,000 (sorcery 31.5M, miracle 32.5M, pyromancy 33.5M,
/// hex 34.5M; empty in vanilla; template offsets are below 5,000).</item>
/// <item><b>SP4 numbers</b> (DS1's, calibrated like weapons): with a DS1 anchor (the DS1 spell DS2's template is the shared copy
/// of) every number is the template's × DS1(spell)/DS1(anchor): requirements, casts (usageCountLv1-10), damage (sum of the
/// DS1 bullet chain's AtkParam_Pc atkPhys/Mag/Fire/Thun), bullet life/speed/count, effect durations. Without an anchor the
/// ratio is the school's median DS2/DS1 over the spells both games share (requirements, casts, damage, durations).
/// Attunement slots = DS1 slotLength.</item>
/// <item><b>SP5 ops</b>: element=N (weapon buffs: 100080:1 / 100090:2 element field), noeffect (DS2 can't express the DS1
/// effect: the cast stays, the event keeps only its visual 100040:* instructions; logged UNMAPPED), protect (1000:3 kind 2,
/// DS2's death protection state), atk=BULLET (DS1's damage comes from another bullet).</item>
/// <item><b>SP6 text/icon</b>: DS1 name and long description (DS2 spells have no short description), DS1 icon on the template's
/// icon canvas.</item>
/// <item><b>SP7 pyromancies</b> keep DS2 spellCategory 2: DS2's pyromancy flame is their catalyst (DS1's flame is a weapon in
/// WeaponPorter).</item>
/// <item><b>SP8 vanilla</b>: no vanilla row or event is changed (spell-check verifies).</item>
/// </list>
/// </summary>
public sealed class SpellPorter
{
    public const string Script = "SpEffectSpell";

    public sealed record Template(int Ds1, string School, int Ds2, int Anchor, int Body, string Ops, string Why);
    public sealed record Result(int Ds1Id, string Name, int ItemId, int TemplateId, string Notes);

    readonly Ds1Source ds1;
    readonly ParamStore ds2;
    readonly Pack pack;
    readonly Dictionary<int, Template> templates;
    readonly Dictionary<int, int> sharedSpells; // DS1 Magic id → DS2 item/spell id

    public SpellPorter(Ds1Source ds1, ParamStore ds2, Pack pack)
    {
        this.ds1 = ds1; this.ds2 = ds2; this.pack = pack;
        templates = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Mappings", "spell-templates.tsv")).Skip(1)
            .Where(l => l.Length > 0 && char.IsDigit(l[0])).Select(l => l.Split('\t'))
            .Select(c => new Template(int.Parse(c[0]), c[1], int.Parse(c[2]), c[3] == "-" ? 0 : int.Parse(c[3]), c[4] == "-" ? 0 : int.Parse(c[4]), c[5], c[6]))
            .ToDictionary(t => t.Ds1);
        sharedSpells = File.ReadLines(Commands.ItemMap.DefaultOut).Skip(1).Select(l => l.Split('\t'))
            .Where(c => c[0] == "Magic" && c[4] == "shared" && c[5].Length > 0).ToDictionary(c => int.Parse(c[1]), c => int.Parse(c[5]));
    }

    public static List<int> NewIds() => File.ReadLines(Commands.ItemMap.DefaultOut).Skip(1).Select(l => l.Split('\t'))
        .Where(c => c[0] == "Magic" && c[4] == "new").Select(c => int.Parse(c[1])).ToList();

    static readonly Dictionary<string, (int Base, int Category)> Schools = new()
    {
        ["sorcery"] = (31_000_000, 0), ["miracle"] = (32_000_000, 1), ["pyromancy"] = (33_000_000, 2), ["hex"] = (34_000_000, 3),
    };

    public static int Id(int ds1Id, string school) => Schools[school].Base + 500_000 + ds1Id % 1000 / 10 * 5000;

    static SpellPorter? onDemand;

    /// <summary>
    /// Session 11 (spell11), rule SP9: a DS1 spell a shop, trainer or lot hands out (DS1 goods id = Magic id, item map
    /// "via-spell") whose Magic row is "new" is ported on demand, once per pack (as RingPorter / GoodsPorter.Port do for rings
    /// and keys); a renamed duplicate resolves to DS2's spell. Returns the DS2 item id, 0 when the spell isn't a player spell.
    /// </summary>
    public static int PortOnDemand(Pack pack, Ds1Source ds1, int ds1Id)
    {
        if (onDemand == null || onDemand.pack != pack || onDemand.ds1 != ds1)
            onDemand = new SpellPorter(ds1, ParamStore.LoadDs2(Program.DefaultDs2Root, Commands.ExtractDs2.DefaultOut), pack);
        if (onDemand.NotPorted(ds1Id, out int dup) is string why)
        {
            pack.Note($"spell {ds1Id} on demand: {why}");
            return dup;
        }
        var r = onDemand.Port(ds1Id);
        return r.ItemId;
    }

    /// <summary>The DS2 item a DS1 player spell is in a pack built with every spell: the item map's DS2 spell (shared /
    /// renamed duplicate) or the SP3 id of a ported one (0: not a player spell / no template row).</summary>
    public static int Ds2ItemFor(int ds1Id)
    {
        foreach (var c in File.ReadLines(Commands.ItemMap.DefaultOut).Skip(1).Select(l => l.Split('\t')))
            if (c[0] == "Magic" && c[1] == ds1Id.ToString())
            {
                if (c[4] == "shared" && c[5].Length > 0) return int.Parse(c[5]);
                break;
            }
        var row = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Mappings", "spell-templates.tsv")).Skip(1)
            .Select(l => l.Split('\t')).FirstOrDefault(c => c[0] == ds1Id.ToString());
        return row == null ? 0 : Id(ds1Id, row[1]);
    }

    /// <summary>Why a DS1 Magic row isn't ported as its own spell (SP1), or null.</summary>
    public string? NotPorted(int ds1Id, out int ds2Id)
    {
        ds2Id = 0;
        var m = ds1.Params.Params["Magic"][ds1Id];
        if (m == null) return "not a DS1 Magic row";
        if (I(m, "maxQuantity") < 0 || I(m, "slotLength") == 0) return "DS1 internal entry (no casts, no slot): not a player spell";
        string name = ds1.Text("Magic_name_", ds1Id);
        string ds2Name = Commands.ItemMap.RenamedName(name);
        if (!string.Equals(ds2Name, name, StringComparison.OrdinalIgnoreCase) &&
            ds2.Params["ItemParam"].Rows.FirstOrDefault(r => I(r, "spellParamId") > 0 && ds2.RowName("ItemParam", r.ID).Equals(ds2Name, StringComparison.OrdinalIgnoreCase)) is { } dup)
        {
            ds2Id = dup.ID;
            return $"DS2 has it as {ds2Name} ({dup.ID}): duplicate uses DS2's version";
        }
        return null;
    }

    public Result Port(int ds1Id)
    {
        var src = ds1.Params.Params["Magic"][ds1Id] ?? throw new KeyNotFoundException($"DS1 Magic {ds1Id}");
        if (!templates.TryGetValue(ds1Id, out var t)) throw new KeyNotFoundException($"no spell-templates.tsv row for {ds1Id}");
        string name = ds1.Text("Magic_name_", ds1Id);
        var (schoolBase, category) = Schools[t.School];
        int newId = Id(ds1Id, t.School);
        // session 11 (spell11): one port per pack (a shop or lot may have ported it on demand already, PortOnDemand)
        if (pack.Param("ItemParam")[newId] != null) return new Result(ds1Id, name, newId, t.Ds2, "already ported in this pack");
        int T = t.Ds2;
        var tSpell = ds2.Params["SpellParam"][T] ?? throw new KeyNotFoundException($"DS2 SpellParam {T}");
        var body = ds2.Params["SpellParam"][t.Body > 0 ? t.Body : T]!;
        if (I(body, "spellCategory") != category)
            throw new InvalidDataException($"{name}: body {body.ID} is category {I(body, "spellCategory")}, school {t.School} needs {category} (give a body)");
        foreach (string p in new[] { "ItemParam", "SpellParam", "BulletParam", "PlayerDamageParam" })
            if (pack.Param(p).Rows.Any(r => r.ID >= newId && r.ID < newId + 5000 && ds2.Params[p][r.ID] != null))
                throw new InvalidOperationException($"{p} block {newId} is used in DS2");
        var notes = new List<string> { $"template {T} {ds2.RowName("SpellParam", T)}", t.Anchor > 0 ? $"anchor DS1 {t.Anchor} {ds1.Text("Magic_name_", t.Anchor)}" : "no anchor: school calibration" };
        if (t.Body > 0) notes.Add($"body {t.Body} {ds2.RowName("SpellParam", t.Body)}");
        int Remap(long v) => v >= T && v < T + 10000 ? (int)(newId + (v - T)) : (int)v;
        bool InBlock(int id) => id >= T && id < T + 10000;
        if (Enumerable.Range(T, 10000).Any(i => i - T >= 5000 && (ds2.Params["BulletParam"][i] != null || ds2.Params["PlayerDamageParam"][i] != null)))
            throw new InvalidDataException($"template {T} uses offsets ≥ 5000");

        // --- clone the template's block: bullets, damages, soul consume, events ------------------------------------------
        var anchor = t.Anchor > 0 ? ds1.Params.Params["Magic"][t.Anchor] : null;
        var clonedDamage = new List<PARAM.Row>();
        PARAM.Row? mainBullet = null;
        foreach (string p in new[] { "BulletParam", "PlayerDamageParam", "SpellSoulConsumeParam" })
            foreach (var r in ds2.Params[p].Rows.Where(r => InBlock(r.ID)).ToList())
            {
                var row = pack.AddRow(p, r, Remap(r.ID), name);
                RemapCells(row, Remap, InBlock);
                if (p == "PlayerDamageParam") clonedDamage.Add(row);
                if (p == "BulletParam" && r.ID == I(tSpell, "baseSpellBulletId")) mainBullet = row;
            }
        var script = pack.RegulationScript(Script);
        var vanillaEvents = VanillaScript().Events.Where(e => InBlock((int)e.ID)).ToList();
        var events = new List<EMEVD.Event>();
        foreach (var ve in vanillaEvents)
        {
            var ev = new EMEVD.Event(Remap(ve.ID), ve.RestBehavior);
            foreach (var x in ve.Instructions)
            {
                var a = x.ArgData.ToArray();
                for (int o = 0; o + 4 <= a.Length; o += 4)
                {
                    uint v = BitConverter.ToUInt32(a, o);
                    if (v >= T && v < T + 10000) BitConverter.GetBytes((uint)Remap(v)).CopyTo(a, o);
                }
                ev.Instructions.Add(new EMEVD.Instruction(x.Bank, x.ID, a));
            }
            script.Events.RemoveAll(e => e.ID == ev.ID);
            script.Events.Add(ev);
            events.Add(ev);
        }
        script.Events.Sort((a, b) => a.ID.CompareTo(b.ID));
        notes.Add($"cloned {vanillaEvents.Count} events, {clonedDamage.Count} damage rows");

        // --- SpellParam ----------------------------------------------------------------------------------------------------
        var spell = pack.AddRow("SpellParam", body, newId, name);
        foreach (string f in new[] { "baseSpellBulletId", "baseSpellDamageId", "baseLeftHandSpellBulletId", "baseLeftHandSpellDamageId", "soulConsumeParamId",
                     "lockonAvailableDistanceScale", "canWaitShoot", "canPredictShot" })
            if (spell[f] != null) Set(spell, f, f.EndsWith("Id") ? Remap(I(tSpell, f)) : Get(tSpell, f));
        Set(spell, "equipSlotNum", Math.Max(1, I(src, "slotLength")));
        // requirements
        string primary = t.School is "miracle" ? "requirementFaith" : "requirementIntellect";
        foreach (string stat in new[] { "intelligence", "faith" })
        {
            double v;
            if (anchor != null && I(anchor, primary) > 0) v = Get(tSpell, stat) * Get(src, primary) / Get(anchor, primary);
            else v = Get(src, primary) * SchoolRatio(category, (d2, d1) => Get(d2, stat), d1 => Get(d1, primary));
            Set(spell, stat, Math.Round(v));
        }
        // casts
        double casts1;
        if (anchor != null && I(anchor, "maxQuantity") > 0) casts1 = Get(tSpell, "usageCountLv1") * Get(src, "maxQuantity") / Get(anchor, "maxQuantity");
        else casts1 = Get(src, "maxQuantity") * SchoolRatio(category, (d2, _) => Get(d2, "usageCountLv1"), d1 => Get(d1, "maxQuantity"));
        for (int lv = 1; lv <= 10; lv++)
            Set(spell, $"usageCountLv{lv}", Math.Clamp(Math.Round(casts1 * Get(tSpell, $"usageCountLv{lv}") / Math.Max(1, Get(tSpell, "usageCountLv1"))), 1, 99));
        notes.Add($"req int {I(spell, "intelligence")} fth {I(spell, "faith")}, slots {I(spell, "equipSlotNum")}, casts {I(spell, "usageCountLv1")}-{I(spell, "usageCountLv10")}");

        // --- damage --------------------------------------------------------------------------------------------------------
        // session 11 (spell11): ops are a comma list (atk=BULLET may sit beside other ops)
        int atkBullet = t.Ops.Split(',').FirstOrDefault(o => o.StartsWith("atk=")) is string atkOp ? int.Parse(atkOp[4..]) : I(src, "refCategory") == 1 ? I(src, "refId") : 0;
        double atkS = atkBullet > 0 ? Ds1Atk(atkBullet) : 0;
        // DS2 spell strength is PlayerDamageParam.damageRate (× the catalyst's spell buff; Soul Arrow 0.85), plus a flat damage01
        // on some rows (Wrath of the Gods' flash 300): both scale
        double mainRate = Strength(clonedDamage);
        if (atkS > 0 && mainRate > 0 && !t.Ops.Contains("noeffect"))
        {
            double factor;
            double atkA = anchor != null && I(anchor, "refCategory") == 1 ? Ds1Atk(I(anchor, "refId")) : 0;
            if (atkA > 0) factor = atkS / atkA;
            else factor = atkS * SchoolDamageRatio(category) / mainRate;
            foreach (var r in clonedDamage)
            {
                if (Get(r, "damageRate") > 0) Set(r, "damageRate", Math.Round(Get(r, "damageRate") * factor, 3));
                if (Get(r, "damage01") > 0) Set(r, "damage01", Math.Max(1, Math.Round(Get(r, "damage01") * factor)));
            }
            notes.Add($"damage ×{factor:0.###} (DS1 atk {atkS}{(atkA > 0 ? $" / anchor {atkA}" : $", school {SchoolDamageRatio(category):0.####}/atk")}) → rate {mainRate * factor:0.###}");
        }
        else if (atkS == 0 && mainRate > 0 && I(src, "refCategory") == 1 && !t.Ops.Contains("noeffect"))
            notes.Add("DS1 bullet chain has no attack: template damage kept");

        // --- bullet kinematics (op kin: the anchor is the same DS1 mechanism, both DS1 bullets) ----------------------------
        if (t.Ops.Contains("kin") && anchor != null && mainBullet != null && I(src, "refCategory") == 1 && I(anchor, "refCategory") == 1)
        {
            var bs = ds1.Params.Params["Bullet"][I(src, "refId")];
            var ba = ds1.Params.Params["Bullet"][I(anchor, "refId")];
            if (bs != null && ba != null)
            {
                double life = Ratio(Get(bs, "life"), Get(ba, "life")), speed = Ratio(Get(bs, "maxVellocity"), Get(ba, "maxVellocity"));
                Set(mainBullet, "bulletLife", Get(mainBullet, "bulletLife") * life);
                Set(mainBullet, "initialVelocity", Get(mainBullet, "initialVelocity") * speed);
                Set(mainBullet, "maxSpeed", Get(mainBullet, "maxSpeed") * speed);
                double count = Ratio(Get(bs, "numShoot"), Get(ba, "numShoot"));
                if (Math.Abs(count - 1) > 0.01) Set(mainBullet, "simultaneouslyNum", Math.Max(1, Math.Round(Get(mainBullet, "simultaneouslyNum") * count)));
                notes.Add($"bullet life ×{life:0.##}, speed ×{speed:0.##}, count ×{count:0.##}");
            }
        }

        // --- effect durations ----------------------------------------------------------------------------------------------
        double durS = Ds1Duration(src);
        if (durS > 0 && events.Any(e => Durations(e).Any()))
        {
            double durA = anchor != null ? Ds1Duration(anchor) : 0;
            // without an anchor: the template's longest duration becomes DS1's × the shared spells' DS2/DS1 ratio, the
            // others keep their proportion to it (per-level events)
            double templateMax = events.SelectMany(Durations).Select(d => (double)BitConverter.ToSingle(d.X.ArgData, d.Offset)).Max();
            double factor = durA > 0 ? durS / durA : templateMax > 0 ? durS * DurationRatio() / templateMax : 1;
            int n = 0;
            foreach (var ev in events)
                foreach (var (x, o) in Durations(ev))
                {
                    float cur = BitConverter.ToSingle(x.ArgData, o);
                    if (cur <= 0) continue;
                    BitConverter.GetBytes((float)Math.Round(cur * factor, 1)).CopyTo(x.ArgData, o);
                    n++;
                }
            notes.Add($"duration ×{factor:0.##} ({(durA > 0 ? $"DS1 {durS}/{durA} s" : $"DS1 {durS} s × shared {DurationRatio():0.##} / template {templateMax} s")}, {n} args)");
        }

        // --- ops -------------------------------------------------------------------------------------------------------------
        foreach (string op in t.Ops.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(o => o != "-"))
        {
            if (op.StartsWith("element="))
            {
                int el = int.Parse(op[8..]);
                foreach (var x in events.SelectMany(e => e.Instructions))
                {
                    if (x is { Bank: 100080, ID: 1 }) BitConverter.GetBytes(el).CopyTo(x.ArgData, 4);
                    if (x is { Bank: 100090, ID: 2 }) BitConverter.GetBytes((short)el).CopyTo(x.ArgData, 4);
                }
                notes.Add($"element → {el}");
            }
            else if (op == "noeffect")
            {
                foreach (var ev in events) ev.Instructions.RemoveAll(x => x.Bank != 100040);
                notes.Add("UNMAPPED effect: cast and visuals only");
            }
            else if (op == "protect")
            {
                foreach (var ev in events) ev.Instructions.RemoveAll(x => x.Bank != 100040);
                var main = events.FirstOrDefault(e => e.ID == newId) ?? throw new InvalidDataException("protect: no main event");
                main.Instructions.Add(new EMEVD.Instruction(1000, 3, [.. BitConverter.GetBytes(2), .. BitConverter.GetBytes(0f)]));
                notes.Add("death protection 1000:3 kind 2 (P)");
            }
            else if (op.StartsWith("resist="))
            {
                // session 11 (spell11): a timed status-resistance bonus in DS2's spell form 100090:4 [f32 s, u16 kind, u16 value]
                // (V-data: Magic Barrier 32190010 kinds 1-4, Iron Flesh 33190010 kinds 0-9, Perseverance 32250010 kinds 5-9)
                var a = op[7..].Split(':').Select(double.Parse).ToArray();
                foreach (var ev in events) ev.Instructions.RemoveAll(x => x.Bank != 100040);
                var main = events.FirstOrDefault(e => e.ID == newId) ?? throw new InvalidDataException("resist: no main event");
                main.Instructions.Add(new EMEVD.Instruction(100090, 4, [.. BitConverter.GetBytes((float)a[2]), .. BitConverter.GetBytes((ushort)a[0]), .. BitConverter.GetBytes((ushort)a[1])]));
                notes.Add($"status resistance kind {a[0]} +{a[1]} for {a[2]} s (100090:4)");
            }
            else if (op.StartsWith("dmgtype="))
            {
                // session 11 (spell11), rule SP10: DS1's own damage element on the cloned damage rows that deal damage
                // (PlayerDamageParam.damageType01 0 phys / 1 magic / 2 lightning / 3 fire / 4 dark, V-data: Soul Arrow 1,
                // Lightning Spear 2, Fireball 3, Dark Orb 4; 5+ = status/none rows stay)
                int el = int.Parse(op[8..]);
                int changed = 0;
                foreach (var r in clonedDamage.Where(r => (Get(r, "damageRate") > 0 || Get(r, "damage01") > 0) && I(r, "damageType01") is >= 0 and <= 4))
                { Set(r, "damageType01", el); changed++; }
                notes.Add($"damage element → {el} (DS1's, {changed} rows)");
            }
            else if (op.StartsWith("atk=") || op == "kin") { } // used above
            else throw new InvalidDataException($"unknown op {op}");
        }

        // --- item ------------------------------------------------------------------------------------------------------------
        var tItem = ds2.Params["ItemParam"][T]!;
        var bodyItem = ds2.Params["ItemParam"][body.ID] ?? tItem;
        var item = pack.AddRow("ItemParam", bodyItem, newId, name);
        Set(item, "spellParamId", newId);
        Set(item, "iconId", newId);
        Set(item, "price", Get(tItem, "price"));
        Set(item, "salePrice", Get(tItem, "salePrice"));
        Set(item, "sortId", 1000 + ds1Id % 1000); // after DS2's spells of the school, DS1 order
        if (I(item, "specialEffectId") > 0) Set(item, "specialEffectId", Remap(I(item, "specialEffectId")));
        pack.SetText("itemname", newId, name);
        pack.SetText("detailedexplanation", newId, ds1.LoreText("Magic_long_desc_", ds1Id));
        WeaponPorter.PortIcon(ds1, pack, I(src, "iconId"), newId, I(tItem, "iconId"));

        string summary = string.Join("; ", notes);
        pack.Note($"spell {ds1Id} {name} → item {newId}: {summary}");
        return new Result(ds1Id, name, newId, T, summary);
    }

    static void RemapCells(PARAM.Row row, Func<long, int> remap, Func<int, bool> inBlock)
    {
        foreach (var c in row.Cells)
            switch (c.Value)
            {
                case int i when inBlock(i): c.Value = remap(i); break;
                case uint u when u < int.MaxValue && inBlock((int)u): c.Value = (uint)remap(u); break;
            }
    }

    static double Ratio(double a, double b) => a > 0 && b > 0 ? a / b : 1;

    EMEVD? vanillaScript;
    EMEVD VanillaScript()
    {
        if (vanillaScript != null) return vanillaScript;
        var reg = BND4.Read(Formats.Ds2RegulationCipher.Decrypt(File.ReadAllBytes(Path.Combine(pack.Ds2Game, "enc_regulation.bnd.dcx"))));
        return vanillaScript = EMEVD.Read(reg.Files.First(f => Path.GetFileNameWithoutExtension(f.Name).Equals(Script, StringComparison.OrdinalIgnoreCase)).Bytes);
    }

    /// <summary>Duration f32 arguments of the instruction forms whose layout is known from vanilla spells (Magic Weapon
    /// 100080:1 [90 s, element, bonus], 100090:x [90 s, kind, value], Hush/Fall Control/Cast Light/SMS 1000:0/1/3/9
    /// [kind, 90 s, …]).</summary>
    static IEnumerable<(EMEVD.Instruction X, int Offset)> Durations(EMEVD.Event e)
    {
        foreach (var x in e.Instructions)
        {
            if (x.Bank == 100080 && x.ID == 1 && x.ArgData.Length >= 4) yield return (x, 0);
            else if (x.Bank == 100090 && x.ID is 2 or 4 or 5 or 9 && x.ArgData.Length >= 4) yield return (x, 0);
            else if (x.Bank == 1000 && x.ID is 0 or 1 or 3 or 9 && x.ArgData.Length >= 8) yield return (x, 4);
            else if (x.Bank == 100070 && x.ID == 0 && x.ArgData.Length == 32) yield return (x, 12); // inflict status [1, 0x368, code, s] (Profound Still)
        }
    }

    /// <summary>
    /// DS1 attack of one cast of a bullet spell: the largest AtkParam_Pc total (atkPhys + Mag + Fire + Thun) along the bullet
    /// chain (HitBulletID: Pursuers' homing mass carries a token attack, its hit bullet the real one). A stream whose bullets
    /// each hit (shootInterval &gt; 0 without a shared hit list: Fire Surge, 55 every 0.1 s while held) counts one second of
    /// the stream, as a DS2 cast is one burst; streams with a shared hit list (Fire Whip, Gravelord Sword Dance) hit once.
    /// </summary>
    double Ds1Atk(int bulletId)
    {
        var bullets = ds1.Params.Params["Bullet"];
        var first = bullets[bulletId];
        double best = 0;
        for (int depth = 0; depth < 6 && bulletId > 0; depth++)
        {
            var b = bullets[bulletId];
            if (b == null) break;
            int atk = I(b, "atkId_Bullet");
            if (atk > 0 && ds1.Params.Params["AtkParam_Pc"][atk] is { } a)
                best = Math.Max(best, Get(a, "atkPhys") + Get(a, "atkMag") + Get(a, "atkFire") + Get(a, "atkThun"));
            bulletId = I(b, "HitBulletID");
        }
        if (first != null && Get(first, "shootInterval") > 0 && I(first, "isUseSharedHitList") == 0)
            best /= Get(first, "shootInterval");
        return best;
    }

    /// <summary>DS1 attack elements along a bullet chain (HitBulletID): the largest atkPhys / atkMag / atkFire / atkThun of
    /// any AtkParam_Pc row the chain names (spell-check's element rule).</summary>
    public static (double Phys, double Mag, double Fire, double Thun) Ds1AtkParts(Ds1Source ds1, int bulletId)
    {
        var bullets = ds1.Params.Params["Bullet"];
        double p = 0, m = 0, f = 0, t = 0;
        for (int depth = 0; depth < 6 && bulletId > 0; depth++)
        {
            var b = bullets[bulletId];
            if (b == null) break;
            if (I(b, "atkId_Bullet") is > 0 and var atk && ds1.Params.Params["AtkParam_Pc"][atk] is { } a)
            { p = Math.Max(p, Get(a, "atkPhys")); m = Math.Max(m, Get(a, "atkMag")); f = Math.Max(f, Get(a, "atkFire")); t = Math.Max(t, Get(a, "atkThun")); }
            bulletId = I(b, "HitBulletID");
        }
        return (p, m, f, t);
    }

    /// <summary>DS1 effect duration: the Magic row's SpEffect, or its bullet's shooter / hit effect.</summary>
    double Ds1Duration(PARAM.Row magic)
    {
        var sp = ds1.Params.Params["SpEffectParam"];
        int refId = I(magic, "refId");
        if (I(magic, "refCategory") == 2) return sp[refId] is { } e ? Get(e, "effectEndurance") : 0;
        var b = ds1.Params.Params["Bullet"][refId];
        foreach (string f in new[] { "spEffectIDForShooter", "spEffectId0" })
            if (b != null && b[f] != null && I(b, f) > 0 && sp[I(b, f)] is { } e2 && Get(e2, "effectEndurance") > 0) return Get(e2, "effectEndurance");
        return 0;
    }

    /// <summary>Median DS2 value / DS1 value over the shared spells of a DS2 school (SP4).</summary>
    double SchoolRatio(int category, Func<PARAM.Row, PARAM.Row, double> ds2Value, Func<PARAM.Row, double> ds1Value)
    {
        var ratios = new List<double>();
        foreach (var (d1, d2) in sharedSpells)
        {
            var s2 = ds2.Params["SpellParam"][ds2.Params["ItemParam"][d2] is { } it ? I(it, "spellParamId") : d2];
            var s1 = ds1.Params.Params["Magic"][d1];
            if (s2 == null || s1 == null || I(s2, "spellCategory") != category) continue;
            double a = ds2Value(s2, s1), b = ds1Value(s1);
            if (a > 0 && b > 0) ratios.Add(a / b);
        }
        return Median(ratios, 0);
    }

    readonly Dictionary<int, double> schoolDamage = new();
    /// <summary>Median DS2 main damageRate (<see cref="Strength"/>) / DS1 bullet-chain attack over the shared damaging spells of a school (any school
    /// when the school has none).</summary>
    double SchoolDamageRatio(int category)
    {
        if (schoolDamage.TryGetValue(category, out double c)) return c;
        var all = new List<(int Cat, double R)>();
        foreach (var (d1, d2) in sharedSpells)
        {
            var s1 = ds1.Params.Params["Magic"][d1];
            var s2 = ds2.Params["SpellParam"][d2];
            if (s1 == null || s2 == null || I(s1, "refCategory") != 1) continue;
            double atk = Ds1Atk(I(s1, "refId"));
            int blockBase = d2;
            double dmg = Strength(ds2.Params["PlayerDamageParam"].Rows.Where(r => r.ID >= blockBase && r.ID < blockBase + 10000));
            if (atk > 0 && dmg > 0) all.Add((I(s2, "spellCategory"), dmg / atk));
        }
        var same = all.Where(a => a.Cat == category).Select(a => a.R).ToList();
        return schoolDamage[category] = Median(same.Count > 0 ? same : all.Select(a => a.R));
    }

    double? durationRatio;
    /// <summary>Median DS2 effect duration / DS1 effectEndurance over the shared SpEffect spells (SP4, no anchor).</summary>
    double DurationRatio()
    {
        if (durationRatio is double d) return d;
        var ratios = new List<double>();
        foreach (var (d1, d2) in sharedSpells)
        {
            var s1 = ds1.Params.Params["Magic"][d1];
            if (s1 == null) continue;
            double dur1 = Ds1Duration(s1);
            if (dur1 <= 0) continue;
            double dur2 = VanillaScript().Events.Where(e => e.ID >= d2 && e.ID < d2 + 10000).SelectMany(Durations)
                .Select(t => (double)BitConverter.ToSingle(t.X.ArgData, t.Offset)).Where(v => v > 0).DefaultIfEmpty(0).Max();
            if (dur2 > 0) ratios.Add(dur2 / dur1);
        }
        durationRatio = Median(ratios);
        return durationRatio.Value;
    }

    /// <summary>True median (mean of the middle two for an even count); <paramref name="fallback"/> when empty.</summary>
    static double Median(IEnumerable<double> xs, double fallback = 1)
    {
        var a = xs.Where(double.IsFinite).Order().ToArray();
        if (a.Length == 0) return fallback;
        return a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2;
    }

    /// <summary>A DS2 spell's strength: its largest PlayerDamageParam.damageRate (the multiplier on the catalyst's spell buff),
    /// or its largest flat damage01 when no row has a rate.</summary>
    static double Strength(IEnumerable<PARAM.Row> rows)
    {
        var l = rows.ToList();
        double rate = l.Select(r => Get(r, "damageRate")).DefaultIfEmpty(0).Max();
        return rate > 0 ? rate : l.Select(r => Get(r, "damage01")).DefaultIfEmpty(0).Max();
    }
}
