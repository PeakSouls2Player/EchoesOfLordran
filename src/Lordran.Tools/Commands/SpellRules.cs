using Lordran.Tools.Build;
using Lordran.Tools.Build.Maps;
using Lordran.Tools.Formats;
using SoulsFormats;
using static Lordran.Tools.Build.ParamCells;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (spell11): spell-check's rules over EVERY DS1 player spell (the 70 Magic rows with casts and a slot: shared
/// ones as DS2 has them, DS1-only ones as SpellPorter made them), DS1's catalysts and DS1's spell shops / lots:
/// <list type="bullet">
/// <item><b>SC1 resolve</b>: the DS1 spell is a DS2 item (item map shared / renamed duplicate, or SP3 id) with a SpellParam,
/// casts 1-99 and slots ≥ 1; every bullet / damage / child row it names exists.</item>
/// <item><b>SC2 school</b>: DS1 ezStateBehaviorType 0 sorcery → DS2 category 0, except DS1's Dark family 3710-3740 → 3
/// (hex: DS2 made Dark Orb / Dark Fog hexes); 1 miracle → 1; 2 pyromancy → 2.</item>
/// <item><b>SC3 requirements</b>: a DS1 Int requirement stays an Int requirement, a DS1 Faith requirement a Faith one; a
/// sorcery/miracle gets no requirement in the other stat (hexes need both: DS2's rule for its own Dark Orb).</item>
/// <item><b>SC4 element</b>: the DS2 damage rows' element (PlayerDamageParam.damageType01: 0 phys, 1 magic, 2 lightning,
/// 3 fire, 4 dark) is one of the DS1 bullet chain's attack elements (AtkParam_Pc atkPhys/Mag/Fire/Thun), dark for a hex.
/// A ported spell that differs is a problem; a shared one is DS2's own (listed).</item>
/// <item><b>SC5 calibration</b>: DS2 strength (damageRate) / DS1 attack per school, shared spells' median; a ported spell
/// beyond ×3 of its school's median is a problem.</item>
/// <item><b>SC6 cast style</b>: DS1 Magic.refType (cast motion) → the DS2 casting animations DS2 gives the shared spells of
/// that motion; a ported spell whose body animation isn't among them, or whose DS1 motion no shared spell has, is listed
/// (DS1 cast animations are c0001 content: additive-animation plan in NOTES).</item>
/// <item><b>SC7 effects / sounds</b>: every SFX id the spell's SpellParam, bullets, damage rows and events name exists in
/// DS2's title sfx banks (vanilla or the pack's sfx9999_Append) and is below 30000 (rule B2b-id); every sound id is one a
/// vanilla spell row uses.</item>
/// <item><b>SC8 catalysts</b>: each DS1 catalyst (weaponCategory 8) is a DS2 weapon whose WeaponTypeParam allows DS1's
/// schools (enableMagic → sorcery + hex, enableMiracle → miracle, enableSorcery → pyromancy), with DS1's Int/Faith
/// requirements and DS1's spell power (rule CAT1) in the schools' elements.</item>
/// <item><b>SC9 shops / lots</b>: every DS1 ShopLineupParam row and ItemLotParam entry that hands out a spell (goods id =
/// Magic id) resolves to the pack's item; a built shop row (ShopLineupParam 90M + DS1 row) sells exactly it.</item>
/// <item><b>SC10 lightning weapons</b>: a DS1 weapon with lightning attack keeps a DS2 lightning rate.</item>
/// </list>
/// </summary>
public static partial class EquipCheck
{
    static readonly string[] SfxFieldsSpell = ["sfx0Id", "sfx1Id"];
    static readonly string[] SfxFieldsBullet = ["bulletSfxId", "muzzleFlashSfxId", "explosionSfxId", "attachSfxId", "hitbackSfxId"];

    static void SpellRules(Ctx c, Ds1Source ds1, EMEVD vanScript, EMEVD myScript)
    {
        var magic = ds1.Params.Params["Magic"];
        var players = magic.Rows.Where(r => r.ID < 10000 && I(r, "maxQuantity") > 0 && I(r, "slotLength") > 0).ToList();
        var mapRows = File.ReadLines(ItemMap.DefaultOut).Skip(1).Select(l => l.Split('\t')).Where(x => x[0] == "Magic").ToDictionary(x => int.Parse(x[1]));
        var tsv = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Mappings", "spell-templates.tsv")).Skip(1)
            .Select(l => l.Split('\t')).Where(x => x.Length > 5).ToDictionary(x => int.Parse(x[0]));
        bool Shared(int d) => mapRows.TryGetValue(d, out var m) && m[4] == "shared";
        PARAM.Row? Spell(ParamStore s, int item) => s.Params["ItemParam"][item] is { } it ? s.Params["SpellParam"][I(it, "spellParamId")] : null;
        int Anim(PARAM.Row s) => I(s, "single1HNormalAnimIdSpellCasting");
        int Expected(PARAM.Row m) => I(m, "ezStateBehaviorType") switch { 0 => m.ID is >= 3710 and < 3750 ? 3 : 0, 1 => 1, 2 => 2, _ => -1 };
        // the block a spell's rows live in: ported 5000 ids, vanilla 10000
        (int Lo, int Hi) Block(int d2) => d2 % 1_000_000 >= 500_000 ? (d2, d2 + 5000) : (d2, d2 + 10000);

        // SC6: DS1 cast motion → DS2 casting animations of the shared spells with that motion
        var styles = new Dictionary<int, HashSet<int>>();
        foreach (var m in players.Where(m => Shared(m.ID)))
            if (Spell(c.Van, int.Parse(mapRows[m.ID][5])) is { } s2)
            {
                if (!styles.TryGetValue(I(m, "refType"), out var set)) styles[I(m, "refType")] = set = new();
                set.Add(Anim(s2));
            }

        // SC7: effect ids DS2's title banks know (vanilla + the pack's appended), sound ids vanilla spell rows use
        var sfxKnown = new HashSet<int>(FfxPorter.Ds2UsedIds());
        string append = Path.Combine(c.PackDir, "title", "sfx", "sfx9999_Append.ffxbnd.dcx");
        if (File.Exists(append))
            foreach (var f in BND4.Read(DCX.Decompress(File.ReadAllBytes(append))).Files)
            {
                string fn = Path.GetFileName(f.Name);
                if (fn.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase) && int.TryParse(fn[1..^4], out int fid)) sfxKnown.Add(fid);
            }
        // DS2's resident common bank (sfxcommon.ffxbnd: Homeward's 5220 …)
        string common = Path.Combine(Program.DefaultDs2Root, "sfx", "sfxcommon.ffxbnd");
        if (File.Exists(common))
            foreach (var f in BND4.Read(common).Files)
            {
                string fn = Path.GetFileName(f.Name);
                if (fn.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase) && int.TryParse(fn[1..^4], out int fid)) sfxKnown.Add(fid);
            }
        // DS2 creates an id ≤ 9999 as id + 20000 when that exists, else id + its companion id + 10000 (FUN_140beb670, rule
        // B2b-id R6)
        // … and an id a vanilla spell event already plays is DS2's own (Homeward's 5220 sits in no title bank: map/menu bank)
        var vanillaEventSfx = vanScript.Events.SelectMany(e => e.Instructions).Where(x => x is { Bank: 100040, ID: 1 } && x.ArgData.Length >= 4).Select(x => BitConverter.ToInt32(x.ArgData, 0)).ToHashSet();
        bool Known(int id) => sfxKnown.Contains(id) || vanillaEventSfx.Contains(id) || id <= 9999 && (sfxKnown.Contains(id + 20000) || sfxKnown.Contains(id + 10000));
        var seKnown = c.Van.Params["SpellParam"].Rows.SelectMany(r => new[] { I(r, "se0Id"), I(r, "se1Id"), I(r, "shootSeCategory") }).ToHashSet();

        var ratios = new Dictionary<int, List<double>>(); // school → shared DS2 strength / DS1 atk
        var rows = new List<(PARAM.Row M, int D2, PARAM.Row S, double Atk, double Str, string Note)>();
        var anims = new List<string>();
        int resolved = 0;
        foreach (var m in players)
        {
            string name = ds1.Text("Magic_name_", m.ID);
            int d2 = SpellPorter.Ds2ItemFor(m.ID);
            var s = d2 > 0 ? Spell(c.Mine, d2) : null;
            if (s == null) { c.Bad($"SC1 DS1 spell {m.ID} {name}: no DS2 spell (item {d2})"); continue; }
            resolved++;
            bool ported = !Shared(m.ID);
            string tag = $"DS1 {m.ID} {name} → {d2}{(ported ? "" : " (DS2's own)")}";
            // SC1
            if (I(s, "usageCountLv1") is < 1 or > 99 || I(s, "equipSlotNum") < 1) c.Bad($"SC1 {tag}: casts {I(s, "usageCountLv1")} / slots {I(s, "equipSlotNum")}");
            var (lo, hi) = Block(d2);
            var bullets = new Queue<int>(new[] { I(s, "baseSpellBulletId"), I(s, "baseLeftHandSpellBulletId") }.Where(x => x > 0));
            var seen = new HashSet<int>();
            var damages = new HashSet<int>(new[] { I(s, "baseSpellDamageId"), I(s, "baseLeftHandSpellDamageId") }.Where(x => x > 0));
            while (bullets.Count > 0)
            {
                int b = bullets.Dequeue();
                if (!seen.Add(b)) continue;
                var br = c.Mine.Params["BulletParam"][b];
                if (br == null) { c.Bad($"SC1 {tag}: BulletParam {b} missing"); continue; }
                foreach (string f in new[] { "", "02", "03" })
                {
                    if (I(br, "childeBulletId" + f) > 0) bullets.Enqueue(I(br, "childeBulletId" + f));
                    if (I(br, "childeDamageId" + f) > 0) damages.Add(I(br, "childeDamageId" + f));
                }
                foreach (string f in SfxFieldsBullet)
                    if (I(br, f) is > 0 and var sfx && (!Known(sfx) || sfx >= FfxPorter.LazyIdLimit)) c.Bad($"SC7 {tag}: bullet {b} {f} {sfx} is no DS2 title effect (< 30000)");
            }
            foreach (int d in damages)
                if (c.Mine.Params["PlayerDamageParam"][d] == null) c.Bad($"SC1 {tag}: PlayerDamageParam {d} missing");
            // SC2
            int cat = I(s, "spellCategory");
            if (cat != Expected(m)) c.Bad($"SC2 {tag}: DS2 category {cat}, DS1 school says {Expected(m)}");
            // SC3
            int rInt = I(s, "intelligence"), rFth = I(s, "faith"), dInt = I(m, "requirementIntellect"), dFth = I(m, "requirementFaith");
            if (dInt > 0 && rInt == 0) c.Bad($"SC3 {tag}: DS1 Int {dInt} lost");
            if (dFth > 0 && rFth == 0) c.Bad($"SC3 {tag}: DS1 Faith {dFth} lost");
            if (cat == 0 && rFth > 0) c.Bad($"SC3 {tag}: sorcery with a Faith requirement {rFth}");
            if (cat == 1 && rInt > 0) c.Bad($"SC3 {tag}: miracle with an Int requirement {rInt}");
            if (cat == 3 && dInt > 0 && (rInt == 0 || rFth == 0)) c.Bad($"SC3 {tag}: hex without both requirements ({rInt}/{rFth})");
            // SC7
            foreach (string f in SfxFieldsSpell)
                if (I(s, f) is > 0 and var sfx && (!Known(sfx) || sfx >= FfxPorter.LazyIdLimit)) c.Bad($"SC7 {tag}: SpellParam {f} {sfx} is no DS2 title effect");
            foreach (string f in new[] { "se0Id", "se1Id" })
                if (I(s, f) > 0 && !seKnown.Contains(I(s, f))) c.Bad($"SC7 {tag}: {f} {I(s, f)} is no vanilla spell sound");
            foreach (var ev in myScript.Events.Where(e => e.ID >= lo && e.ID < hi))
                foreach (var x in ev.Instructions.Where(x => x is { Bank: 100040, ID: 1 } && x.ArgData.Length >= 4))
                    if (BitConverter.ToInt32(x.ArgData, 0) is > 0 and var sfx && (!Known(sfx) || sfx >= FfxPorter.LazyIdLimit)) c.Bad($"SC7 {tag}: event {ev.ID} 100040:1 effect {sfx} is no DS2 title effect");
            // SC4 element / SC5 strength
            var dmgRows = c.Mine.Params["PlayerDamageParam"].Rows.Where(r => r.ID >= lo && r.ID < hi && (Get(r, "damageRate") > 0 || Get(r, "damage01") > 0)).ToList();
            var ds2El = dmgRows.Select(r => I(r, "damageType01")).Where(t => t is >= 0 and <= 4).ToHashSet();
            double atk = 0;
            string note = "";
            int atkBullet = tsv.TryGetValue(m.ID, out var trow) && trow[5].StartsWith("atk=") ? int.Parse(trow[5][4..].Split(',')[0]) : I(m, "refCategory") == 1 ? I(m, "refId") : 0;
            if (atkBullet > 0)
            {
                var (p, mg, fi, th) = SpellPorter.Ds1AtkParts(ds1, atkBullet);
                atk = p + mg + fi + th;
                // a stream whose bullets each hit counts 1 s (SpellPorter.Ds1Atk, SP4)
                if (ds1.Params.Params["Bullet"][atkBullet] is { } fb && Get(fb, "shootInterval") > 0 && I(fb, "isUseSharedHitList") == 0) atk /= Get(fb, "shootInterval");
                var ds1El = new HashSet<int>();
                if (p > 0) ds1El.Add(0); if (mg > 0) ds1El.Add(1); if (th > 0) ds1El.Add(2); if (fi > 0) ds1El.Add(3);
                if (cat == 3) ds1El.Add(4);
                if (ds2El.Count > 0 && ds1El.Count > 0 && !ds2El.Overlaps(ds1El))
                {
                    string msg = $"SC4 {tag}: DS2 element {string.Join("/", ds2El.Order())}, DS1 {string.Join("/", ds1El.Order())} (0 phys 1 magic 2 lightning 3 fire 4 dark)";
                    if (ported && !(trow?[5].Contains("noeffect") ?? false)) c.Bad(msg); else note = msg[4..] + " — DS2's own spell";
                }
            }
            double str = dmgRows.Select(r => Get(r, "damageRate")).DefaultIfEmpty(0).Max();
            if (str <= 0) str = dmgRows.Select(r => Get(r, "damage01")).DefaultIfEmpty(0).Max() / 100.0;
            if (!ported && atk > 0 && str > 0) { if (!ratios.TryGetValue(cat, out var l)) ratios[cat] = l = new(); l.Add(str / atk); }
            rows.Add((m, d2, s, atk, str, note));
            // SC6 cast style (ported spells: the body's DS2 animation against the DS2 animations of DS1's motion)
            if (ported)
            {
                int rt = I(m, "refType");
                string body = trow != null && trow[4] != "-" ? $"body {trow[4]} {c.Van.RowName("SpellParam", int.Parse(trow[4]))}" : $"template {trow?[2]} {(trow != null ? c.Van.RowName("SpellParam", int.Parse(trow[2])) : "")}";
                if (!styles.TryGetValue(rt, out var set))
                    anims.Add($"{name}: DS1 motion {rt} is DS1-only (no shared spell casts with it); DS2 cast = {body} anim {Anim(s)}");
                else if (!set.Contains(Anim(s)))
                    anims.Add($"{name}: DS1 motion {rt} = DS2 anims {string.Join("/", set.Order())} (shared spells: {string.Join(", ", players.Where(x => Shared(x.ID) && I(x, "refType") == rt).Select(x => ds1.Text("Magic_name_", x.ID)))}); DS2 cast = {body} anim {Anim(s)}");
            }
        }
        // SC5
        var median = ratios.ToDictionary(k => k.Key, k => k.Value.Order().ElementAt(k.Value.Count / 2));
        Console.WriteLine("  spell rules (SC1-SC7): DS1 spell → DS2 item: school, req Int/Fth (DS1), casts, slots, element, strength/DS1 atk (school median)");
        foreach (var (m, d2, s, atk, str, note) in rows)
        {
            int cat = I(s, "spellCategory");
            string cal = "";
            if (atk > 0 && str > 0 && median.TryGetValue(cat, out double med))
            {
                // a ported spell with a DS1 anchor is calibrated on its anchor (SP4), else on the school
                string refName = "school median";
                if (!Shared(m.ID) && tsv.TryGetValue(m.ID, out var tr) && tr[3] != "-" && rows.FirstOrDefault(x => x.M.ID == int.Parse(tr[3])) is { M: not null } a && a.Atk > 0 && a.Str > 0)
                { med = a.Str / a.Atk; refName = $"anchor {ds1.Text("Magic_name_", a.M.ID)}"; }
                double q = str / atk / med;
                cal = $", strength {str:0.###} / DS1 atk {atk:0} = {q:0.00}× {refName}";
                if (!Shared(m.ID) && (q > 3 || q < 1 / 3.0)) c.Bad($"SC5 DS1 {m.ID} {ds1.Text("Magic_name_", m.ID)}: strength {q:0.00}× its {refName}");
            }
            Console.WriteLine($"    {m.ID} {ds1.Text("Magic_name_", m.ID)} → {d2} {(Shared(m.ID) ? "shared" : "ported")}: cat {cat}, req {I(s, "intelligence")}/{I(s, "faith")} (DS1 {I(m, "requirementIntellect")}/{I(m, "requirementFaith")}), casts {I(s, "usageCountLv1")}-{I(s, "usageCountLv10")} (DS1 {I(m, "maxQuantity")}), slots {I(s, "equipSlotNum")} (DS1 {I(m, "slotLength")}){cal}{(note.Length > 0 ? "; " + note : "")}");
        }
        Console.WriteLine($"  SC6 cast style: {anims.Count} DS1-only spells cast with a DS2 animation of another DS1 motion:");
        foreach (string a in anims) Console.WriteLine("    " + a);

        // SC8 catalysts
        var w1 = ds1.Params.Params["EquipParamWeapon"];
        var weaponRows = File.ReadLines(ItemMap.DefaultOut).Skip(1).Select(l => l.Split('\t')).Where(x => x[0] == "EquipParamWeapon").ToDictionary(x => int.Parse(x[1]));
        int cats = 0;
        foreach (var w in w1.Rows.Where(r => I(r, "weaponCategory") == 8 && r.ID % 1000 == 0 && weaponRows.ContainsKey(r.ID)))
        {
            var kind = WeaponPorter.KindOf(w);
            if (kind == WeaponPorter.Kind.Lantern) continue;
            string name = ds1.Text("Weapon_name_", w.ID);
            int item = weaponRows[w.ID][4] == "shared" ? int.Parse(weaponRows[w.ID][5]) : WeaponPorter.Ids(w.ID, kind).Item;
            var it = c.Mine.Params["ItemParam"][item];
            var wp = it == null ? null : c.Mine.Params["WeaponParam"][I(it, "weaponParamId")];
            if (wp == null) { c.Bad($"SC8 catalyst {w.ID} {name}: no DS2 weapon {item}"); continue; }
            cats++;
            var type = c.Mine.Params["WeaponTypeParam"][I(wp, "weaponTypeId")];
            var re = c.Mine.Params["WeaponReinforceParam"][I(wp, "weaponReinforceId")];
            if (type == null || re == null) { c.Bad($"SC8 catalyst {name}: type/reinforce missing"); continue; }
            if (I(w, "enableMagic") > 0 && (I(type, "allowMagic") == 0 || I(type, "allowDarkMagic") == 0)) c.Bad($"SC8 {name}: casts DS1 sorceries, DS2 type {I(wp, "weaponTypeId")} doesn't allow sorcery + hex");
            if (I(w, "enableMiracle") > 0 && I(type, "allowMiracle") == 0) c.Bad($"SC8 {name}: casts DS1 miracles, DS2 type doesn't allow miracles");
            if (I(w, "enableSorcery") > 0 && I(type, "allowPyromancy") == 0) c.Bad($"SC8 {name}: casts DS1 pyromancies, DS2 type doesn't allow pyromancy");
            bool shared = weaponRows[w.ID][4] == "shared";
            if (!shared && (I(wp, "requiredIntelligence") != I(w, "properMagic") || I(wp, "requiredFaith") != I(w, "properFaith")))
                c.Bad($"SC8 {name}: requirements Int {I(wp, "requiredIntelligence")} Fth {I(wp, "requiredFaith")}, DS1 {I(w, "properMagic")}/{I(w, "properFaith")}");
            string el = I(type, "allowPyromancy") > 0 ? "Fire" : I(type, "allowMiracle") > 0 ? "Thunder" : "Magic";
            double min = Get(re, "minimum" + el), max = Get(re, "maximum" + el);
            string power = "";
            if (!shared)
            {
                int anchorId = kind == WeaponPorter.Kind.Chime ? WeaponPorter.Ds1Talisman : kind == WeaponPorter.Kind.Staff ? WeaponPorter.Ds1SorcerersCatalyst : WeaponPorter.Ds1PyromancyFlame;
                int ds2Anchor = kind == WeaponPorter.Kind.Chime ? 4010000 : kind == WeaponPorter.Kind.Staff ? 3800000 : 5400000;
                var aRe = c.Van.Params["WeaponReinforceParam"][I(c.Van.Params["WeaponParam"][ds2Anchor]!, "weaponReinforceId")]!;
                double want = kind == WeaponPorter.Kind.Flame
                    ? Get(aRe, "maximum" + el) * WeaponPorter.Ds1FlamePower(w) / WeaponPorter.Ds1FlamePower(w1[WeaponPorter.Ds1LastLevel(w1, anchorId)]!)
                    : Get(aRe, "minimum" + el) * WeaponPorter.Ds1CatalystPower(ds1, w) / WeaponPorter.Ds1CatalystPower(ds1, w1[anchorId]!);
                if (Math.Abs(min - want) > Math.Max(1.5, want * 0.02)) c.Bad($"SC8 {name}: spell-buff base {el} {min}, CAT1 wants {want:0}");
                power = $", CAT1 DS1 {WeaponPorter.Ds1SpellCorrect(w)} → {el} {min}..{max}";
            }
            Console.WriteLine($"    catalyst {w.ID} {name} → {item} {(shared ? "shared" : kind.ToString())}: type {I(wp, "weaponTypeId")} sorcery {I(type, "allowMagic")} miracle {I(type, "allowMiracle")} pyro {I(type, "allowPyromancy")} hex {I(type, "allowDarkMagic")}/{I(type, "allowDarkMiracle")}, req Int {I(wp, "requiredIntelligence")} Fth {I(wp, "requiredFaith")}{power}");
        }

        // SC10 lightning weapons (Faith in DS2 scales lightning)
        int thunder = 0;
        foreach (var w in w1.Rows.Where(r => I(r, "weaponCategory") != 8 && r.ID % 100 == 0 && Get(r, "attackBaseThunder") > 0))
        {
            int baseId = w.ID / 1000 * 1000;
            if (!weaponRows.TryGetValue(baseId, out var wr) || wr[4] != "new" || w.ID != baseId) continue;
            var kind = WeaponPorter.KindOf(w);
            int item = WeaponPorter.Ids(baseId, kind).Item;
            if (c.Mine.Params["ItemParam"][item] is not { } it || c.Mine.Params["WeaponParam"][I(it, "weaponParamId")] is not { } wp) continue;
            var re = c.Mine.Params["WeaponReinforceParam"][I(wp, "weaponReinforceId")]!;
            thunder++;
            if (Get(re, "thunderRate") <= 0) c.Bad($"SC10 {ds1.Text("Weapon_name_", baseId)}: DS1 lightning {Get(w, "attackBaseThunder")}, DS2 thunderRate 0");
        }

        // SC9 shops and lots
        var spellIds = players.Select(m => m.ID).ToHashSet();
        var shops = ds1.Params.Params["ShopLineupParam"];
        var myShop = c.Mine.Params["ShopLineupParam"];
        int shopRows = 0, built = 0;
        var byShop = new SortedDictionary<int, List<string>>();
        foreach (var r in shops.Rows.Where(r => I(r, "equipType") == 3 && spellIds.Contains(I(r, "equipId"))))
        {
            shopRows++;
            int d1 = I(r, "equipId"), d2 = SpellPorter.Ds2ItemFor(d1);
            if (d2 <= 0 || c.Mine.Params["ItemParam"][d2] == null) c.Bad($"SC9 DS1 shop row {r.ID} {ds1.Text("Magic_name_", d1)}: no DS2 item");
            int key = r.ID / 100 * 100;
            // a DS1 shop block the pack's maps built (any row of it present) must carry every spell row of it
            bool blockBuilt = myShop.Rows.Any(x => x.ID >= Build.Talk.TalkBuilder.ShopRow(key) && x.ID < Build.Talk.TalkBuilder.ShopRow(key + 100));
            if (myShop[Build.Talk.TalkBuilder.ShopRow(r.ID)] is { } sr)
            {
                built++;
                if (I(sr, "saleItem") != d2) c.Bad($"SC9 shop row {sr.ID} sells {I(sr, "saleItem")}, DS1 {d1} {ds1.Text("Magic_name_", d1)} is {d2}");
            }
            else if (blockBuilt) c.Bad($"SC9 DS1 shop row {r.ID} {ds1.Params.RowName("ShopLineupParam", r.ID)}: its shop is built but the spell row is missing");
            if (!byShop.TryGetValue(key, out var l)) byShop[key] = l = new();
            l.Add(ds1.Text("Magic_name_", d1) + (blockBuilt ? (myShop[Build.Talk.TalkBuilder.ShopRow(r.ID)] != null ? " [built]" : " [MISSING]") : ""));
        }
        foreach (var (k, l) in byShop)
            Console.WriteLine($"    DS1 shop {k} ({ds1.Params.RowName("ShopLineupParam", shops.Rows.First(r => r.ID >= k && I(r, "equipType") == 3 && spellIds.Contains(I(r, "equipId"))).ID)}…): {l.Count} spells: {string.Join(", ", l)}");
        int lotEntries = 0;
        foreach (var r in ds1.Params.Params["ItemLotParam"].Rows)
            for (int i = 1; i <= 8; i++)
                if ((uint)I(r, $"lotItemCategory0{i}") == 0x40000000u && spellIds.Contains(I(r, $"lotItemId0{i}")))
                {
                    lotEntries++;
                    int d1 = I(r, $"lotItemId0{i}");
                    if (SpellPorter.Ds2ItemFor(d1) is var d2 && (d2 <= 0 || c.Mine.Params["ItemParam"][d2] == null)) c.Bad($"SC9 DS1 lot {r.ID} {ds1.Text("Magic_name_", d1)}: no DS2 item");
                }
        Console.WriteLine($"  spell rules: {resolved}/{players.Count} DS1 player spells resolved; {cats} DS1 catalysts; {thunder} DS1 lightning weapons; {shopRows} DS1 shop rows ({built} built in this pack's maps), {lotEntries} DS1 lot entries hand out spells");
    }

    /// <summary>
    /// <c>spell-ffx [--all]</c> (session 11, spell11): can rule B2b (FfxPorter, DS1 FXR1 → DS2 DLsE, ids by rule B2b-id &lt; 30000)
    /// convert the DS1 spells' own effects? For every ported DS1 spell (or every DS1 player spell with --all) it walks the DS1
    /// bullet chain (HitBulletID) and tries each sfxId_Bullet / sfxId_Hit / sfxId_Flick; prints convertible / why not.
    /// Read-only (nothing written).
    /// </summary>
    public static int SpellFfx(string[] args)
    {
        var ds1 = new Ds1Source(Program.DefaultDsrRoot);
        var magic = ds1.Params.Params["Magic"];
        var bullets = ds1.Params.Params["Bullet"];
        var tsv = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Mappings", "spell-templates.tsv")).Skip(1).Select(l => l.Split('\t')).Where(x => x.Length > 5).Select(x => int.Parse(x[0])).ToHashSet();
        bool all = args.Contains("--all");
        FfxPorter.SpellNodes = args.Contains("--nodes");
        int ok = 0, bad = 0;
        foreach (var m in magic.Rows.Where(r => r.ID < 10000 && I(r, "maxQuantity") > 0 && (all || tsv.Contains(r.ID))))
        {
            var ids = new List<(int Bullet, string Field, int Sfx)>();
            int b = I(m, "refCategory") == 1 ? I(m, "refId") : 0;
            for (int d = 0; d < 6 && b > 0 && bullets[b] is { } br; d++, b = I(br, "HitBulletID"))
                foreach (string f in new[] { "sfxId_Bullet", "sfxId_Hit", "sfxId_Flick" })
                    if (I(br, f) > 0) ids.Add((br.ID, f, I(br, f)));
            var parts = new List<string>();
            foreach (var (bid, f, sfx) in ids)
            {
                var log = new List<string>();
                var conv = FfxPorter.Convert(Program.DefaultDsrRoot, "m99_99_00_00", sfx, log);
                if (conv != null) { ok++; parts.Add($"{bid}.{f} {sfx} → DS2 {conv.Id} OK ({conv.Notes.Count} notes)"); }
                else { bad++; parts.Add($"{bid}.{f} {sfx} NOT ({string.Join("; ", log.Select(l => l.Replace("atmos: rule B2b: ", "")))})"); }
            }
            Console.WriteLine($"{m.ID} {ds1.Text("Magic_name_", m.ID)} (DS1 motion {I(m, "refType")}, hand sfx variation {I(m, "sfxVariationId")}): {(parts.Count == 0 ? "no bullet effects" : string.Join(" | ", parts))}");
        }
        Console.WriteLine($"spell-ffx: {ok} convertible, {bad} not");
        return 0;
    }
}
