using System.Numerics;
using System.Text;
using Lordran.Tools.Formats;
using Lordran.Tools.Textures;
using SoulsFormats;
using static Lordran.Tools.Build.ParamCells;

namespace Lordran.Tools.Build;

/// <summary>
/// Ports a DS1-only weapon, shield, bow or catalyst into DS2: model (gamedata + partslq binders), textures, icon,
/// name/description, and the ItemParam → WeaponParam → WeaponReinforceParam chain.
/// <list type="bullet">
/// <item>Template (moveset, weapon type, menus): the closest DS2 weapon of the same class. For melee, shields and bows
/// that is the nearest weapon shared by both games with the same DS1 moveset; catalysts pick the nearest DS2 staff,
/// chime or flame by requirements. <c>Mappings/weapon-templates.tsv</c> overrides any choice.</item>
/// <item>Numbers are DS1's, rescaled per class by the median DS2/DS1 ratio over the shared weapons (attack per element,
/// guard %, stability, status resistance, bleed/poison buildup, durability, price).</item>
/// <item>Upgrades follow DS2 rules mapped from the DS1 upgrade class: +15 → +10 titanite; +5 dragon (dragon scale) →
/// +5 Petrified Dragon Bone; other +5 and non-upgradable catalysts → +5 Twinkling Titanite. Infusable only if DS1 has
/// infusion rows for it.</item>
/// </list>
/// DS1 special attacks (unique R2s, projectiles) need DS1 animations and are not ported here; see weapon-survey.tsv.
/// </summary>
public sealed class WeaponPorter
{
    public enum Kind { Melee, Shield, Bow, Crossbow, Staff, Chime, Flame, Lantern }

    readonly Ds1Source ds1;
    readonly ParamStore ds2;
    readonly Pack pack;
    readonly List<Pair> shared;
    readonly Dictionary<int, int> templateOverrides;
    readonly Dictionary<int, string> mapNames;
    readonly Dictionary<int, int> ds1ReinforceMax;
    readonly SpecialsPorter specials;

    // DS2 reference rows: weapon types of the catalyst classes, and upgrade profiles.
    const int Ds2SorcerersStaff = 3800000, Ds2ClericsChime = 4010000, Ds2PyromancyFlame = 5400000;
    const int ProfileTwinkling = 1390;   // Ivory Straight Sword: +5, Twinkling Titanite
    const int ProfileDragon = 1350;      // Black Dragon Sword: +5, Petrified Dragon Bone
    const int ProfileNormalWeapon = 1220, ProfileNormalShield = 11000; // Longsword, Buckler: +10

    sealed record Pair(PARAM.Row Ds1, PARAM.Row Item, PARAM.Row Weapon, PARAM.Row Reinforce, PARAM.Row Stats);

    public WeaponPorter(Ds1Source ds1, ParamStore ds2, Pack pack, string itemMapPath, string templatesPath)
    {
        this.ds1 = ds1;
        this.ds2 = ds2;
        this.pack = pack;
        specials = new SpecialsPorter(ds1, pack);
        var map = File.ReadLines(itemMapPath).Skip(1).Select(l => l.Split('\t')).Where(c => c[0] == "EquipParamWeapon").ToList();
        mapNames = map.ToDictionary(c => int.Parse(c[1]), c => c[2]);
        shared = map.Where(c => c[4] == "shared").Select(c =>
            {
                var d1 = ds1.Params.Params["EquipParamWeapon"][int.Parse(c[1])];
                var item = ds2.Params["ItemParam"][int.Parse(c[5])];
                if (d1 == null || item == null || I(item, "weaponParamId") <= 0) return null;
                var w = ds2.Params["WeaponParam"][I(item, "weaponParamId")];
                var r = w == null ? null : ds2.Params["WeaponReinforceParam"][I(w, "weaponReinforceId")];
                var s = r == null ? null : ds2.Params["WeaponStatsAffectParam"][I(r, "statsAffectId")];
                return w == null || r == null || s == null ? null : new Pair(d1, item, w, r, s);
            }).OfType<Pair>().ToList();
        templateOverrides = File.Exists(templatesPath)
            ? File.ReadLines(templatesPath).Where(l => l.Length > 0 && char.IsDigit(l[0])).Select(l => l.Split('\t'))
                .ToDictionary(c => int.Parse(c[0]), c => int.Parse(c[1]))
            : new();
        ds1ReinforceMax = ds1.Params.Params["ReinforceParamWeapon"].Rows
            .GroupBy(r => r.ID / 100 * 100).ToDictionary(g => g.Key, g => g.Max(r => r.ID - g.Key));
    }

    public sealed record Result(int Ds1Id, string Name, int ItemId, int ModelId, int TemplateItemId, string Notes);

    public static Kind KindOf(PARAM.Row w) => I(w, "weaponCategory") switch
    {
        12 => Kind.Shield,
        10 => Kind.Bow,
        11 => Kind.Crossbow,
        8 when I(w, "lanternWep") > 0 => Kind.Lantern,
        8 when I(w, "enableMagic") > 0 => Kind.Staff,
        8 when I(w, "enableMiracle") > 0 => Kind.Chime,
        8 when I(w, "enableSorcery") > 0 => Kind.Flame,
        _ => Kind.Melee,
    };

    /// <summary>
    /// DS2 ids. Weapons keep item = 7,000,000 + DS1 id (the Balder Side Sword's 7204000 is in a save), model 6000 + id/1000.
    /// Shields live in DS2's shield range: item 17,000,000 + id (18.4M–18.5M), model 8000+. DLC ids (9,0xx,000) get
    /// 7,9xx,xxx / 69xx for weapons and 18,0xx,000 / 89xx for shields.
    /// </summary>
    public static (int Item, int Model) Ids(int ds1Id, Kind kind)
    {
        bool dlc = ds1Id >= 9_000_000;
        int local = dlc ? ds1Id - 9_000_000 : ds1Id;
        if (kind == Kind.Shield)
            return dlc ? (18_000_000 + local, 8900 + local / 1000) : (17_000_000 + ds1Id, 8000 + (ds1Id - 1_400_000) / 1000);
        return dlc ? (7_900_000 + local, 6900 + local / 1000) : (7_000_000 + ds1Id, 6000 + ds1Id / 1000);
    }

    public Result Port(int ds1Id)
    {
        var src = ds1.Params.Params["EquipParamWeapon"][ds1Id] ?? throw new KeyNotFoundException($"DS1 weapon {ds1Id}");
        string name = mapNames.TryGetValue(ds1Id, out var n) ? n : ds1.Text("Weapon_name_", ds1Id);
        var kind = KindOf(src);
        var (itemId, modelId) = Ids(ds1Id, kind);

        // An overridden template (Skull Lantern → Club) also sets the class to calibrate against.
        var basis = templateOverrides.TryGetValue(ds1Id, out int forcedTemplate) ? BasisOfDs2Type(forcedTemplate) : Basis(src, kind);
        var tItem = ds2.Params["ItemParam"][ChooseTemplate(ds1Id, src, kind, basis)];
        var tWeapon = ds2.Params["WeaponParam"][I(tItem, "weaponParamId")];
        var tReinforce = ds2.Params["WeaponReinforceParam"][I(tWeapon, "weaponReinforceId")];
        int templateModel = I(tWeapon, "weaponModelId");

        // Weapons without a mesh in DS1 (the Pyromancy Flame is an effect, the Dark Hand is the bare arm) keep the template's.
        bool hasModel = HasMesh(src);
        // DS2 already uses a few ids of the scheme (6310, 6450): those go to 77xx (7700 + DS1 id/1000 % 100).
        if (hasModel && ds2.Params["WeaponParam"].Rows.Any(r => I(r, "weaponModelId") == modelId))
            modelId = 7700 + ds1Id / 1000 % 100;
        if (!hasModel) modelId = templateModel;
        // Reinforce id = model id, unless DS2 already uses it (6310, 6450): then 60000 + DS1 id/1000 (DLC: 60010+).
        int reinforceId = hasModel ? modelId : Ids(ds1Id, kind).Model;
        if (ds2.Params["WeaponReinforceParam"][reinforceId] != null)
            reinforceId = 60000 + (ds1Id >= 9_000_000 ? ds1Id - 9_000_000 : ds1Id) / 1000;
        EnsureFree(itemId, reinforceId, hasModel ? modelId : -1);

        var notes = new List<string> { $"{kind}", $"template {tItem.ID} ({ds2.RowName("ItemParam", tItem.ID)})", $"basis {basis.Count}" };

        // --- reinforce: damage per element, guard, stability, status, upgrades, scaling -------------------
        var reinforce = pack.AddRow("WeaponReinforceParam", tReinforce, reinforceId, name);
        var profile = UpgradeProfile(src, kind, tReinforce, out string profileNote);
        notes.Add(profileNote);
        foreach (string f in new[] { "maxLevel", "reinforceCost" })
            reinforce[f].Value = profile[f].Value;
        double maxRatio = MaxRatio(profile);
        if (!Infusable(ds1Id))
            Set(reinforce, "attrSpec", 0);

        if (kind is Kind.Staff or Kind.Chime or Kind.Flame)
        {
            // Catalysts: the bash and spell schools come from the DS2 template (its rates and spell-buff curve).
            double atk = Get(tReinforce, "minimumPhysical");
            foreach (string e in Elements) { Set(reinforce, "minimum" + e, atk); Set(reinforce, "maximum" + e, Math.Round(atk * maxRatio)); }
            // session 11 (spell11), rule CAT1: the spell schools' elements (the template's rated ones: staff magic + dark,
            // chime lightning + dark, flame fire) carry DS1's own spell power
            var (spellMin, spellMax, catNote) = CatalystPower(src, kind, ds1Id, tReinforce, atk, maxRatio);
            foreach (string e in Elements.Where(e => e != "Physical" && Get(tReinforce, e.ToLowerInvariant() + "Rate") > 0))
            {
                Set(reinforce, "minimum" + e, Math.Round(spellMin));
                Set(reinforce, "maximum" + e, Math.Round(spellMax));
            }
            notes.Add(catNote);
        }
        else
        {
            // Calibrated on fully upgraded attack in both games, then +0 = max / the DS2 upgrade track's growth. DS1 boss and
            // dragon weapons carry nearly all their power at +0 while DS2's grow, so comparing +0 values would overrate them.
            double atkRatio = Median(basis.Select(p => Ds2MaxTotal(p.Reinforce) / Ds1MaxTotal(p.Ds1))) / maxRatio;
            var ds1Max = Ds1Max(src);
            double physical = ds1Max[0] * atkRatio;
            var parts = new (string Ds2, double Value)[]
            {
                ("Physical", physical), ("Magic", ds1Max[1] * atkRatio), ("Fire", ds1Max[2] * atkRatio), ("Thunder", ds1Max[3] * atkRatio), ("Dark", 0),
            };
            foreach (var (e, v) in parts)
            {
                double baseValue = v > 0 ? v : physical; // vanilla repeats the physical value in unused elements
                Set(reinforce, "minimum" + e, Math.Round(baseValue));
                Set(reinforce, "maximum" + e, Math.Round(baseValue * maxRatio));
                Set(reinforce, (e == "Physical" ? "physical" : e.ToLowerInvariant()) + "Rate", v > 0 ? 100 : 0);
            }
            notes.Add("atk " + string.Join("/", parts.Where(p => p.Value > 0).Select(p => $"{p.Ds2[..3]}{Math.Round(p.Value)}")));
            Set(reinforce, "statsAffectId", NearestStatsAffect(src, basis));
        }

        // Guard % per element, stability, status resistance (any weapon can block).
        foreach (var (d1, d2) in new[] { ("physGuardCutRate", "physical"), ("magGuardCutRate", "magic"), ("fireGuardCutRate", "fire"), ("thunGuardCutRate", "thunder") })
            Set(reinforce, d2, Math.Clamp(Get(src, d1) * Ratio(basis, p => Get(p.Reinforce, d2), p => Get(p.Ds1, d1)), 0, 100));
        Set(reinforce, "dark", Math.Round((Get(reinforce, "magic") + Get(reinforce, "fire")) / 2));
        double stability = Get(src, "staminaGuardDef") * Ratio(basis, p => Get(p.Reinforce, "stabilityRate0"), p => Get(p.Ds1, "staminaGuardDef"));
        double growth = Get(tReinforce, "stabilityRate0") > 0 ? Get(tReinforce, "stabilityRate10") / Get(tReinforce, "stabilityRate0") : 1;
        for (int lv = 0; lv <= 10; lv++)
            Set(reinforce, $"stabilityRate{lv}", Math.Round(stability * (1 + (growth - 1) * lv / 10.0), 1));
        foreach (var (d1, d2) in new[] { ("poisonGuardResist", "poison"), ("bloodGuardResist", "bleeding"), ("curseGuardResist", "curse") })
            Set(reinforce, d2, Math.Clamp(Get(src, d1) * Ratio(basis, p => Get(p.Reinforce, d2), p => Get(p.Ds1, d1)), 0, 100));

        // Bleed / poison buildup from DS1's on-hit effects.
        foreach (var (field, ds2Element) in new[] { ("registBlood", "Bleeding"), ("poizonAttackPower", "Poison") })
        {
            if (kind is Kind.Staff or Kind.Chime or Kind.Flame) break; // catalysts' DS1 effects aren't hit statuses
            // DS2 gear has no separate toxic, so DS1 toxic (Gravelord Sword) adds to poison buildup.
            double buildup = OnHit(src, field) + (field == "poizonAttackPower" ? OnHit(src, "registIllness") : 0);
            if (buildup <= 0) continue;
            double ratio = StatusRatio(field, ds2Element);
            Set(reinforce, "minimum" + ds2Element, Math.Round(buildup * ratio));
            Set(reinforce, "maximum" + ds2Element, Math.Round(buildup * ratio * 1.25));
            Set(reinforce, ds2Element.ToLowerInvariant() + "Rate", 100);
            notes.Add($"{ds2Element.ToLowerInvariant()} {buildup}→{Math.Round(buildup * ratio)}");
        }

        // --- weapon / item ----------------------------------------------------------------------------
        double durRatio = Median(basis.Select(p => Get(p.Weapon, "durability") / Math.Max(1, Get(p.Ds1, "durability"))));
        double priceRatio = Median(basis.Select(p => Get(p.Item, "price") / Math.Max(1, Get(p.Ds1, "basicPrice"))));
        var weapon = pack.AddRow("WeaponParam", tWeapon, itemId, name);
        if (weapon["weaponId"] != null) Set(weapon, "weaponId", itemId);
        Set(weapon, "weaponModelId", modelId);
        Set(weapon, "weaponReinforceId", reinforceId);
        Set(weapon, "requiredStrength", Get(src, "properStrength"));
        Set(weapon, "requiredDexterity", Get(src, "properAgility"));
        Set(weapon, "requiredIntelligence", Get(src, "properMagic"));
        Set(weapon, "requiredFaith", Get(src, "properFaith"));
        Set(weapon, "weight", Get(src, "weight"));
        Set(weapon, "durability", Math.Max(1, Math.Round(Get(src, "durability") * durRatio)));

        var item = pack.AddRow("ItemParam", tItem, itemId, name);
        Set(item, "iconId", itemId);
        Set(item, "weaponParamId", itemId);
        Set(item, "price", Math.Round(Get(src, "basicPrice") * priceRatio));
        Set(item, "salePrice", Math.Round(Get(src, "sellValue") * priceRatio));
        // The template's own passive (ItemParam.specialEffectId → its SpEffectWeapon event) doesn't belong to the DS1
        // weapon; bows keep theirs, which is the bow's range factor. DS1 passives/on-hit effects come from SpecialsPorter.
        if (kind is not (Kind.Bow or Kind.Crossbow))
            Set(item, "specialEffectId", -1);
        if (specials.Apply(src, itemId, item) is { } special)
            notes.Add("specials: " + special);

        pack.SetText("itemname", itemId, name);
        pack.SetText("detailedexplanation", itemId, ds1.LoreText("Weapon_long_desc_", ds1Id));
        PortIcon(ds1, pack, I(src, "iconId"), itemId, tItem.ID);
        if (hasModel)
            PortModel($"WP_A_{I(src, "equipModelId"):D4}", modelId, templateModel, kind == Kind.Shield);
        else
            notes.Add("no DS1 mesh: template model");

        string summary = string.Join(", ", notes);
        pack.Note($"weapon {ds1Id} {name} → item {itemId}, model {modelId}: {summary}");
        return new Result(ds1Id, name, itemId, modelId, tItem.ID, summary);
    }

    // ---- template and calibration ------------------------------------------------------------------

    static readonly string[] Elements = { "Physical", "Magic", "Thunder", "Fire", "Dark" };

    List<Pair> Basis(PARAM.Row src, Kind kind)
    {
        var sameMotion = shared.Where(p => I(p.Ds1, "wepmotionCategory") == I(src, "wepmotionCategory") && KindOf(p.Ds1) == kind).ToList();
        if (sameMotion.Count > 0) return sameMotion;
        var sameCategory = shared.Where(p => I(p.Ds1, "weaponCategory") == I(src, "weaponCategory")).ToList();
        if (sameCategory.Count > 0) return sameCategory;
        return shared.Where(p => KindOf(p.Ds1) == Kind.Melee).ToList();
    }

    List<Pair> BasisOfDs2Type(int ds2ItemId)
    {
        int type = I(ds2.Params["WeaponParam"][I(ds2.Params["ItemParam"][ds2ItemId], "weaponParamId")], "weaponTypeId");
        var sameType = shared.Where(p => I(p.Weapon, "weaponTypeId") == type).ToList();
        return sameType.Count > 0 ? sameType : shared.Where(p => KindOf(p.Ds1) == Kind.Melee).ToList();
    }

    int ChooseTemplate(int ds1Id, PARAM.Row src, Kind kind, List<Pair> basis)
    {
        if (templateOverrides.TryGetValue(ds1Id, out int forced)) return forced;
        int reference = kind switch
        {
            Kind.Staff => Ds2SorcerersStaff, Kind.Chime => Ds2ClericsChime, Kind.Flame or Kind.Lantern => Ds2PyromancyFlame, _ => 0,
        };
        if (reference != 0)
        {
            int type = I(ds2.Params["WeaponParam"][reference], "weaponTypeId");
            return ds2.Params["ItemParam"].Rows
                .Where(r => r.ID < 10_000_000 && I(r, "weaponParamId") > 0 && ds2.Params["WeaponParam"][I(r, "weaponParamId")] is { } w && I(w, "weaponTypeId") == type)
                .Select(r => (r.ID, W: ds2.Params["WeaponParam"][I(r, "weaponParamId")]))
                .OrderBy(t => Math.Abs(Get(t.W, "requiredIntelligence") - Get(src, "properMagic")) + Math.Abs(Get(t.W, "requiredFaith") - Get(src, "properFaith"))
                              + 3 * Math.Abs(Get(t.W, "weight") - Get(src, "weight")))
                .First().ID;
        }
        // Melee, shields, bows: a DS2 player weapon of the basis' dominant weapon type with a plain moveset, nearest by
        // expected fully-upgraded attack and weight. Ties between types go to the type with more plain weapons (the
        // standard class rather than an oddity like Murakumo's curved greatswords).
        var playerWeapons = ds2.Params["ItemParam"].Rows
            .Where(r => (r.ID < 6_000_000 || r.ID is >= 11_000_000 and < 12_000_000) && I(r, "weaponParamId") > 0)
            .Select(r => (Item: r, W: ds2.Params["WeaponParam"][I(r, "weaponParamId")]))
            .Where(t => t.W != null && ds2.Params["WeaponReinforceParam"][I(t.W, "weaponReinforceId")] != null)
            .Select(t => (t.Item, W: t.W!)).ToList();
        bool strict = kind is Kind.Melee or Kind.Shield;
        int dominantType = basis.GroupBy(p => I(p.Weapon, "weaponTypeId"))
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => playerWeapons.Count(t => I(t.W, "weaponTypeId") == g.Key && PlainMoveset(t.W)))
            .First().Key;
        double expected = Ds1MaxTotal(src) * Median(basis.Select(p => Ds2MaxTotal(p.Reinforce) / Ds1MaxTotal(p.Ds1)));
        var candidates = playerWeapons.Where(t => I(t.W, "weaponTypeId") == dominantType && (!strict || PlainMoveset(t.W))).ToList();
        if (candidates.Count == 0)
            return basis.OrderBy(p => Math.Abs(Ds1Total(p.Ds1) - Ds1Total(src)) + 10 * Math.Abs(Get(p.Ds1, "weight") - Get(src, "weight"))).First().Item.ID;
        return candidates.OrderBy(c => Math.Abs(Ds2MaxTotal(ds2.Params["WeaponReinforceParam"][I(c.W, "weaponReinforceId")]!) - expected) / Math.Max(1, expected)
                                      + 0.2 * Math.Abs(Get(c.W, "weight") - Get(src, "weight"))).First().Item.ID;
    }

    /// <summary>
    /// True when every attack in the weapon's moveset (WeaponActionCategoryParam atkId*) is an ordinary 10xxxxxx attack.
    /// DS2 marks special attacks there: 20xxxxxx ids (the Moonlight Greatsword's magic wave) or placeholders such as
    /// Channeler's Trident's 1 (its buff dance). A template must not bring its own special into a DS1 weapon.
    /// </summary>
    bool PlainMoveset(PARAM.Row weapon)
    {
        var moveset = ds2.Params["WeaponActionCategoryParam"][I(weapon, "weaponActionCategoryId")];
        // Only the attack slots (one/two-handed, dual, left: normal, strong, dash, roll, step, …); guard slots hold 1 and
        // ripostes/backstabs 21–23M in every moveset.
        // Ordinary weapons of a class share their attack ids, so an id no other moveset uses is that weapon's own special
        // (some specials, e.g. projectile slashes, use ordinary-range ids).
        return moveset != null && AttackSlots(moveset)
            .All(v => v <= 0 || (v is >= 10_000_000 and < 20_000_000 && AttackIdUsers.GetValueOrDefault(v) >= 2));
    }

    static IEnumerable<long> AttackSlots(PARAM.Row moveset) => moveset.Cells
        .Where(c => c.Def.InternalName is var n && (n.StartsWith("atkIdSingle") || n.StartsWith("atkIdDual") || n.StartsWith("atkIdLeft")))
        .Select(c => Convert.ToInt64(c.Value));

    // Number of movesets that use each attack id.
    Dictionary<long, int>? attackIdUsers;
    Dictionary<long, int> AttackIdUsers => attackIdUsers ??= ds2.Params["WeaponActionCategoryParam"].Rows
        .SelectMany(r => AttackSlots(r).Distinct()).GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>DS1 attack per element at full upgrade: base × ReinforceParamWeapon rate at the type's max level.</summary>
    double[] Ds1Max(PARAM.Row w)
    {
        int rt = I(w, "reinforceTypeId");
        var rate = ds1.Params.Params["ReinforceParamWeapon"][rt + ds1ReinforceMax.GetValueOrDefault(rt / 100 * 100, 0)];
        double R(string f) => rate == null ? 1 : Get(rate, f);
        return new[]
        {
            Get(w, "attackBasePhysics") * R("physicsAtkRate"), Get(w, "attackBaseMagic") * R("magicAtkRate"),
            Get(w, "attackBaseFire") * R("fireAtkRate"), Get(w, "attackBaseThunder") * R("thunderAtkRate"),
        };
    }

    double Ds1MaxTotal(PARAM.Row w) => Ds1Max(w).Sum();

    static double Ds2MaxTotal(PARAM.Row r) =>
        Elements.Sum(e => Get(r, "maximum" + e) * Get(r, (e == "Physical" ? "physical" : e.ToLowerInvariant()) + "Rate") / 100);

    PARAM.Row UpgradeProfile(PARAM.Row src, Kind kind, PARAM.Row template, out string note)
    {
        int ds1Max = ds1ReinforceMax.GetValueOrDefault(I(src, "reinforceTypeId") / 100 * 100, 0);
        bool catalyst = I(src, "weaponCategory") == 8 && kind != Kind.Flame;
        bool premade = I(src, "materialSetId") < 0; // pre-made crystal weapons and talismans can't be reinforced in DS1
        var reinforce = ds2.Params["WeaponReinforceParam"];
        if (I(src, "reinforceTypeId") / 100 * 100 == 1100) { note = "+5 dragon bone"; return reinforce[ProfileDragon]; }
        if (ds1Max <= 5 || catalyst || premade) { note = "+5 twinkling"; return reinforce[ProfileTwinkling]; }
        note = "+10 titanite";
        if (I(template, "maxLevel") == 10) return template;
        return reinforce[kind == Kind.Shield ? ProfileNormalShield : ProfileNormalWeapon];
    }

    // max/min attack at full upgrade, from DS2 weapons on the same upgrade track.
    double MaxRatio(PARAM.Row profile)
    {
        int level = I(profile, "maxLevel"), costFamily = I(profile, "reinforceCost") / 10_000_000;
        return Median(ds2.Params["WeaponReinforceParam"].Rows
            .Where(r => I(r, "maxLevel") == level && I(r, "reinforceCost") / 10_000_000 == costFamily && Get(r, "minimumPhysical") > 0)
            .Select(r => Get(r, "maximumPhysical") / Get(r, "minimumPhysical")));
    }

    /// <summary>DS1 catalyst anchors of rule CAT1: the DS1 catalyst whose DS2 counterpart is the class template (both
    /// games' starting catalysts: Sorcerer's Catalyst ↔ Sorcerer's Staff, Talisman ↔ Cleric's Sacred Chime; the Pyromancy
    /// Flame is shared: DS1 +15 ↔ DS2 +10).</summary>
    public const int Ds1SorcerersCatalyst = 1300000, Ds1Talisman = 1360000, Ds1PyromancyFlame = 1330000;

    /// <summary>DS1 spell power of a catalyst (Magic Adjust ∝ correctMagic / correctFaith; DS1 flames grow by upgrade rows
    /// id + 100·k, each with its own correctMagic).</summary>
    public static double Ds1SpellCorrect(PARAM.Row w) => Math.Max(Get(w, "correctMagic"), Get(w, "correctFaith"));

    /// <summary>DS1 CalcCorrectGraph grow % at stat <paramref name="s"/> (stages stageMaxVal0-4 → stageMaxGrowVal0-4, curve
    /// exponent adjPt_maxGrowVal: t^a for a &gt; 0, 1 − (1 − t)^−a for a &lt; 0; the Souls games' correction graph).</summary>
    public static double Ds1Grow(PARAM.Row g, double s)
    {
        for (int i = 0; i < 4; i++)
        {
            double v0 = Get(g, $"stageMaxVal{i}"), v1 = Get(g, $"stageMaxVal{i + 1}");
            if (s > v1 && i < 3) continue;
            double t = v1 > v0 ? Math.Clamp((s - v0) / (v1 - v0), 0, 1) : 1, a = Get(g, $"adjPt_maxGrowVal{i}");
            double tt = a > 0 ? Math.Pow(t, a) : a < 0 ? 1 - Math.Pow(1 - t, -a) : t;
            return Get(g, $"stageMaxGrowVal{i}") + (Get(g, $"stageMaxGrowVal{i + 1}") - Get(g, $"stageMaxGrowVal{i}")) * tt;
        }
        return Get(g, "stageMaxGrowVal4");
    }

    /// <summary>Reference stat levels of rule CAT1 (DS1 caster builds: the DS1 spells' Int/Faith requirements span 10-50).</summary>
    public static readonly int[] CatalystRefStats = [20, 30, 40, 50];

    /// <summary>DS1 catalyst spell power (Magic Adjust, P: correct × (1 + grow(stat) / 100) on the weapon's correctType
    /// graph), averaged over <see cref="CatalystRefStats"/>: Logan's (graph 11), Darkmoon (13), Ivory/Sunlight (16) and
    /// Thorolund/Oolacile (15) grow differently from the plain catalyst (14) and the Talisman (12).</summary>
    public static double Ds1CatalystPower(Ds1Source ds1, PARAM.Row w)
    {
        var g = ds1.Params.Params["CalcCorrectGraph"][I(w, "correctType")];
        double c = Ds1SpellCorrect(w);
        return g == null ? c : CatalystRefStats.Average(s => c * (1 + Ds1Grow(g, s) / 100));
    }

    /// <summary>DS1 pyromancy flame power: 100 + correctMagic (P).</summary>
    public static double Ds1FlamePower(PARAM.Row w) => 100 + Get(w, "correctMagic");

    /// <summary>DS1 flame's last upgrade row (id + 100·k).</summary>
    public static int Ds1LastLevel(PARAM ds1Weapons, int id) { int k = 0; while (k < 20 && ds1Weapons[id + (k + 1) * 100] != null) k++; return id + k * 100; }

    /// <summary>
    /// Rule CAT1 (session 11, spell11): DS2 spell buff comes from the catalyst's attack in the spell schools' elements (the
    /// template's rated elements: staff magic + dark, chime lightning + dark, flame fire), so a DS1 catalyst's own spell power
    /// is the template's × DS1(catalyst) / DS1(anchor): staves by max(correctMagic, correctFaith) / Sorcerer's Catalyst's,
    /// chimes by max(correctFaith, correctMagic) / Talisman's, each × (1 + its correctType graph's grow % / 100) averaged over stats 20-50; a flame's +0 and top values by DS1's correctMagic at +0 and at
    /// its last upgrade row (DS1 flame power = 100 + correctMagic) / the DS1 Pyromancy Flame's at +15, against DS2's Pyromancy Flame at +10 (its maximum). DS2
    /// scales magic with Int and lightning with Faith, so a DS1 catalyst that scaled with the other stat (Tin Darkmoon
    /// Catalyst: Faith sorceries; Velka's Talisman: Int miracles) keeps its power but scales by DS2's stat (logged).
    /// </summary>
    (double Min, double Max, string Note) CatalystPower(PARAM.Row src, Kind kind, int ds1Id, PARAM.Row tReinforce, double atk, double maxRatio)
    {
        var w = ds1.Params.Params["EquipParamWeapon"];
        double own = Ds1SpellCorrect(src);
        string stat = kind == Kind.Staff && Get(src, "correctFaith") > Get(src, "correctMagic") ? " (DS1 Faith scaling → DS2 Int: staves scale magic with Int)"
                    : kind == Kind.Chime && Get(src, "correctMagic") > Get(src, "correctFaith") ? " (DS1 Int scaling → DS2 Faith: chimes scale lightning with Faith)" : "";
        if (kind == Kind.Flame)
        {
            // DS1 pyromancy power = 100 + the flame's correctMagic (a +0 flame, correctMagic 0, casts at full base power; P)
            double anchorTop = Ds1FlamePower(w[Ds1LastLevel(w, Ds1PyromancyFlame)]!);
            double top = Ds1FlamePower(w[Ds1LastLevel(w, ds1Id)]!), ownFlame = Ds1FlamePower(src);
            string fire = Elements.First(e => e != "Physical" && Get(tReinforce, e.ToLowerInvariant() + "Rate") > 0);
            double ds2Top = Get(tReinforce, "maximum" + fire);
            if (anchorTop <= 0 || own <= 0) return (atk, Math.Round(atk * maxRatio), "CAT1: no DS1 flame power, template's kept");
            return (ds2Top * ownFlame / anchorTop, ds2Top * top / anchorTop, $"CAT1 flame power DS1 {ownFlame}..{top} / Pyromancy Flame +15 {anchorTop} × DS2 flame +10 {ds2Top}");
        }
        int anchorId = kind == Kind.Chime ? Ds1Talisman : Ds1SorcerersCatalyst;
        double anchor = Ds1CatalystPower(ds1, w[anchorId]!);
        own = Ds1CatalystPower(ds1, src);
        if (anchor <= 0 || own <= 0) return (atk, Math.Round(atk * maxRatio), "CAT1: no DS1 spell power, template's kept");
        double p = own / anchor;
        return (atk * p, atk * p * maxRatio, $"CAT1 spell power ×{p:0.###} (DS1 {Ds1SpellCorrect(src)} graph {I(src, "correctType")}: {own:0.#} / {ds1.Text("Weapon_name_", anchorId)} {anchor:0.#}, mean at stat {string.Join("/", CatalystRefStats)}){stat}");
    }

    bool Infusable(int ds1Id) => Enumerable.Range(1, 9).Any(k => ds1.Params.Params["EquipParamWeapon"][ds1Id + k * 100] != null);

    bool HasMesh(PARAM.Row src)
    {
        try
        {
            string model = $"WP_A_{I(src, "equipModelId"):D4}";
            var parts = ds1.Parts(model);
            var file = parts.Files.FirstOrDefault(f => f.Name.EndsWith(model + ".flver", StringComparison.OrdinalIgnoreCase));
            return file != null && FLVER2.Read(file.Bytes).Meshes.Count > 0;
        }
        catch (FileNotFoundException) { return false; }
    }

    double OnHit(PARAM.Row src, string field)
    {
        var effects = ds1.Params.Params["SpEffectParam"];
        return new[] { "spEffectBehaviorId0", "spEffectBehaviorId1", "spEffectBehaviorId2" }
            .Select(f => I(src, f)).Where(id => id > 0).Select(id => effects[id]).Where(e => e != null).Sum(e => Get(e!, field));
    }

    // DS2 buildup per DS1 buildup, over shared weapons that have the status in both games.
    double StatusRatio(string ds1Field, string ds2Element)
    {
        var ratios = shared.Select(p => (Ds1: OnHit(p.Ds1, ds1Field), Ds2: Get(p.Reinforce, ds2Element.ToLowerInvariant() + "Rate") > 0 ? Get(p.Reinforce, "minimum" + ds2Element) : 0))
            .Where(t => t.Ds1 > 0 && t.Ds2 > 0).Select(t => t.Ds2 / t.Ds1).ToList();
        // No shared DS1/DS2 poison weapon: buildup systems are alike, so poison borrows the bleed scale.
        if (ratios.Count == 0 && ds1Field != "registBlood")
            return StatusRatio("registBlood", "Bleeding");
        return ratios.Count > 0 ? Median(ratios) : 1;
    }

    int NearestStatsAffect(PARAM.Row src, List<Pair> basis)
    {
        double scale = Median(basis.Select(p => (Get(p.Stats, "physicalByStrength0") + Get(p.Stats, "physicalByDexterity0")) /
                                                Math.Max(1, Get(p.Ds1, "correctStrength") + Get(p.Ds1, "correctAgility"))));
        double ts = Get(src, "correctStrength") * scale, td = Get(src, "correctAgility") * scale;
        return ds2.Params["WeaponStatsAffectParam"].Rows
            .Where(r => r.ID % 10 == 0 && ds2.RowName("WeaponStatsAffectParam", r.ID).Contains("[Basic]"))
            .OrderBy(r => Math.Pow(Get(r, "physicalByStrength0") - ts, 2) + Math.Pow(Get(r, "physicalByDexterity0") - td, 2))
            .First().ID;
    }

    static double Ds1Total(PARAM.Row w) =>
        Get(w, "attackBasePhysics") + Get(w, "attackBaseMagic") + Get(w, "attackBaseFire") + Get(w, "attackBaseThunder");

    static double Ds2Total(PARAM.Row r) =>
        Elements.Sum(e => Get(r, "minimum" + e) * Get(r, (e == "Physical" ? "physical" : e.ToLowerInvariant()) + "Rate") / 100);

    static double Ratio(List<Pair> basis, Func<Pair, double> ds2, Func<Pair, double> ds1) =>
        Median(basis.Where(p => ds1(p) > 0).Select(p => ds2(p) / ds1(p)));

    static double Median(IEnumerable<double> xs)
    {
        var a = xs.Where(double.IsFinite).Order().ToArray();
        return a.Length == 0 ? 1 : a[a.Length / 2];
    }

    void EnsureFree(int itemId, int reinforceId, int modelId)
    {
        if (ds2.Params["ItemParam"][itemId] != null) throw new InvalidOperationException($"ItemParam {itemId} already exists in DS2");
        if (ds2.Params["WeaponReinforceParam"][reinforceId] != null) throw new InvalidOperationException($"WeaponReinforceParam {reinforceId} already exists in DS2");
        if (modelId >= 0 && ds2.Params["WeaponParam"].Rows.Any(r => I(r, "weaponModelId") == modelId)) throw new InvalidOperationException($"weapon model {modelId} already used in DS2");
    }

    // ---- icon --------------------------------------------------------------------------------------

    /// <summary>DS1 icon onto the template item's DS2 icon canvas (size and texture-name prefix come from the template).</summary>
    internal static void PortIcon(Ds1Source ds1, Pack pack, int ds1IconId, int iconId, int templateIconId)
    {
        var tpf = TPF.Read(Path.Combine(pack.Ds2Data, "menu", "tex", "icon", $"ic_{templateIconId:D10}.tpf"));
        var tex = tpf.Textures[0];
        var size = Dds.Read(tex.Bytes);
        var icon = ds1.Icon(ds1IconId);
        int width = size.Width, height = (int)Math.Round(icon.Height * (double)size.Width / icon.Width);
        if (height > size.Height) (width, height) = ((int)Math.Round(icon.Width * (double)size.Height / icon.Height), size.Height);
        var canvas = icon.Resize(width, height).PadTo(size.Width, size.Height);
        tex.Name = tex.Name.Replace($"{templateIconId:D10}", $"{iconId:D10}");
        tex.Bytes = Dds.Encode(canvas, BcFormat.BC3, mipmaps: false);
        tex.Mipmaps = 1;
        pack.AddFile("gamedata", $"menu/tex/icon/ic_{iconId:D10}.tpf", tpf.Write());
    }

    // ---- model -------------------------------------------------------------------------------------

    void PortModel(string ds1Parts, int modelId, int templateModel, bool shield)
    {
        var parts = ds1.Parts(ds1Parts);
        var mainFlver = FLVER2.Read(parts.Files.First(f => f.Name.EndsWith(ds1Parts + ".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
        var lodFile = parts.Files.FirstOrDefault(f => f.Name.EndsWith(ds1Parts + "_1.flver", StringComparison.OrdinalIgnoreCase));
        var lodFlver = lodFile != null ? FLVER2.Read(lodFile.Bytes) : mainFlver;

        // One texture set per model; names don't always match the model (WP_M_0231, a shared _n), so go by suffix.
        var all = parts.Files.Where(f => f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase)).SelectMany(f => TPF.Read(f.Bytes).Textures).ToList();
        TPF.Texture Pick(string suffix) => all.First(t => suffix == "" ? !t.Name.EndsWith("_n", StringComparison.OrdinalIgnoreCase) && !t.Name.EndsWith("_s", StringComparison.OrdinalIgnoreCase)
                                                                       : t.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        var textures = new Dictionary<string, (byte[] Bytes, byte Format)>
        {
            ["_d"] = Ds2Tex.Color(Pick("").Bytes), ["_s"] = Ds2Tex.Color(Pick("_s").Bytes), ["_n"] = (Ds2Tex.Normal(Pick("_n").Bytes), Ds2Tex.FormatNormal),
        };

        string prefix = shield ? "SD" : "WP", folder = shield ? "shield" : "weapon";
        string tName = $"{prefix}_{templateModel}_M", nName = $"{prefix}_{modelId}_M";
        string dir = Path.Combine(pack.Ds2Data, "model", "parts", folder);
        foreach (string path in Directory.GetFiles(dir, $"{prefix.ToLowerInvariant()}_{templateModel}_m*.bnd"))
        {
            string suffix = Path.GetFileNameWithoutExtension(path)[$"{prefix}_{templateModel}_m".Length..]; // "", "_a", "_l"
            var bnd = BND4.Read(path);
            foreach (var file in bnd.Files)
            {
                string fname = Path.GetFileName(file.Name);
                if (fname.EndsWith(".flv", StringComparison.OrdinalIgnoreCase))
                    file.Bytes = ConvertFlver(suffix == "_l" ? lodFlver : mainFlver, FLVER2.Read(file.Bytes), tName, nName).Write();
                else if (fname.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase))
                {
                    var t = TPF.Read(file.Bytes);
                    var tex = t.Textures[0];
                    string texKind = tex.Name[tName.Length..];                  // "_d", "_n", "_s"; some templates add "_em" etc.
                    // Slots DS1 has no texture for (emissive, …) get a black one, not the template's.
                    var (texBytes, texFormat) = textures.TryGetValue(texKind, out var known) ? known : (Dds.Encode(new Image(4, 4), BcFormat.BC1, mipmaps: false), (byte)0);
                    tex.Name = nName + texKind;
                    tex.Bytes = texBytes;
                    tex.Format = texFormat;
                    tex.Mipmaps = (byte)Dds.Read(texBytes).Mips.Count;
                    file.Bytes = t.Write();
                }
                else if (fname.EndsWith(".acb", StringComparison.OrdinalIgnoreCase))
                    file.Bytes = ReplaceModelId(file.Bytes, templateModel, modelId);
                file.Name = file.Name.Replace(templateModel.ToString(), modelId.ToString());
            }
            byte[] bytes = bnd.Write();
            string name = $"{prefix.ToLowerInvariant()}_{modelId}_m{suffix}.bnd";
            pack.AddFile("gamedata", $"model/parts/{folder}/{name}", bytes);
            pack.AddFile("partslq", $"model_lq/parts/{folder}/{name}", bytes);
        }
    }

    /// <summary>
    /// DS1 geometry into a copy of the DS2 template FLVER: keeps DS2's header, material (W[Ibl][DSB]), buffer layouts,
    /// GX lists and node layout; replaces meshes and bounds. The template's dummies (grip, hit capsule points, trails,
    /// effect anchors) are stretched with the model: each axis is scaled by new/old extent on that side of the grip.
    /// </summary>
    static FLVER2 ConvertFlver(FLVER2 src, FLVER2 dst, string tName, string nName)
    {
        foreach (var m in dst.Materials)
        {
            m.Name = m.Name.Replace(tName, nName);
            foreach (var t in m.Textures) t.Path = t.Path.Replace(tName, nName);
        }
        dst.Nodes[0].Name = dst.Nodes[0].Name.Replace(tName, nName);

        var tMesh = dst.Meshes[0];
        var tVerts = dst.Meshes.SelectMany(m => m.Vertices).ToList();
        Vector3 tMin = tVerts.Count > 0 ? Min(tVerts) : dst.Header.BoundingBoxMin, tMax = tVerts.Count > 0 ? Max(tVerts) : dst.Header.BoundingBoxMax;
        int normalW = tMesh.Vertices.Count > 0 ? tMesh.Vertices[0].NormalW : 127;
        var meshes = new List<FLVER2.Mesh>();
        foreach (var sMesh in src.Meshes)
        {
            var faceSet = sMesh.FaceSets.FirstOrDefault(f => f.Flags == FLVER2.FaceSet.FSFlags.None) ?? sMesh.FaceSets[0];
            var mesh = new FLVER2.Mesh
            {
                MaterialIndex = 0,
                NodeIndex = tMesh.NodeIndex,
                BoneIndices = new List<int>(tMesh.BoneIndices),
                Dynamic = tMesh.Dynamic,
                UseBoneWeights = tMesh.UseBoneWeights,
                VertexBuffers = new List<FLVER2.VertexBuffer> { new FLVER2.VertexBuffer(tMesh.VertexBuffers[0].LayoutIndex) },
                FaceSets = new List<FLVER2.FaceSet>
                {
                    new FLVER2.FaceSet(FLVER2.FaceSet.FSFlags.None, faceSet.TriangleStrip, faceSet.CullBackfaces, faceSet.Unk06, new List<int>(faceSet.Indices)),
                },
                Vertices = sMesh.Vertices.Select(v =>
                {
                    var nv = new FLVER.Vertex(v);
                    while (nv.Colors.Count < 2) nv.Colors.Add(new FLVER.VertexColor(1f, 1f, 1f, 1f));
                    while (nv.Tangents.Count < 1) nv.Tangents.Add(new Vector4(1, 0, 0, 1));
                    nv.NormalW = normalW;
                    return nv;
                }).ToList(),
            };
            if (tMesh.BoundingBox != null)
                mesh.BoundingBox = new FLVER2.Mesh.BoundingBoxes { Min = Min(mesh.Vertices), Max = Max(mesh.Vertices), Unk = tMesh.BoundingBox.Unk };
            meshes.Add(mesh);
        }
        dst.Meshes = meshes;

        var verts = meshes.SelectMany(m => m.Vertices).ToList();
        Vector3 min = Min(verts), max = Max(verts);
        dst.Header.BoundingBoxMin = min;
        dst.Header.BoundingBoxMax = max;
        dst.Nodes[0].BoundingBoxMin = min;
        dst.Nodes[0].BoundingBoxMax = max;

        static float Stretch(float p, float oldMin, float oldMax, float newMin, float newMax)
        {
            if (p >= 0) return oldMax > 1e-4f ? p * newMax / oldMax : p;
            return oldMin < -1e-4f ? p * newMin / oldMin : p;
        }
        foreach (var d in dst.Dummies)
            d.Position = new Vector3(Stretch(d.Position.X, tMin.X, tMax.X, min.X, max.X),
                                     Stretch(d.Position.Y, tMin.Y, tMax.Y, min.Y, max.Y),
                                     Stretch(d.Position.Z, tMin.Z, tMax.Z, min.Z, max.Z));
        return dst;
    }

    static Vector3 Min(IEnumerable<FLVER.Vertex> vs) => vs.Aggregate(new Vector3(float.MaxValue), (a, v) => Vector3.Min(a, v.Position));
    static Vector3 Max(IEnumerable<FLVER.Vertex> vs) => vs.Aggregate(new Vector3(float.MinValue), (a, v) => Vector3.Max(a, v.Position));

    static byte[] ReplaceModelId(byte[] bytes, int from, int to)
    {
        if (from.ToString().Length != to.ToString().Length)
            throw new InvalidOperationException("model ids must have the same digit count to patch in place");
        var result = (byte[])bytes.Clone();
        foreach (var enc in new Encoding[] { Encoding.ASCII, Encoding.Unicode })
        {
            byte[] a = enc.GetBytes(from.ToString()), b = enc.GetBytes(to.ToString());
            for (int i = 0; i + a.Length <= result.Length; i++)
                if (result.AsSpan(i, a.Length).SequenceEqual(a))
                    b.CopyTo(result, i);
        }
        return result;
    }
}
