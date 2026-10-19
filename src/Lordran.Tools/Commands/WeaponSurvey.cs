using Lordran.Tools.Build;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// weapon-survey [out.tsv] — one line per DS1-only weapon (item map "new"): class, moveset and special-attack category,
/// behaviours, on-hit and passive effects, catalyst/upgrade flags, and the model's structure (meshes, materials,
/// textures, dummies). A weapon is flagged "special" when its special-attack category differs from the most common one
/// among DS1 weapons with the same moveset, or when it has effects/flags that DS1 uses for unique behaviour.
/// </summary>
public static class WeaponSurvey
{
    public static int Run(string[] args)
    {
        string outPath = args.Length > 1 ? args[1] : Path.Combine(Build.Pack.DefaultOut, "..", "weapon-survey.tsv");
        var ds1 = new Ds1Source(Program.DefaultDsrRoot);
        var weapons = ds1.Params.Params["EquipParamWeapon"];
        var newIds = File.ReadLines(ItemMap.DefaultOut).Skip(1).Select(l => l.Split('\t'))
            .Where(c => c[0] == "EquipParamWeapon" && c[4] == "new").Select(c => int.Parse(c[1])).ToList();

        double Get(PARAM.Row r, string f) => Convert.ToDouble(r[f].Value);
        // Base weapons only (upgrade/infusion variants share the moveset); the usual special-attack category per moveset.
        var baseRows = weapons.Rows.Where(r => r.ID % 1000 == 0 && Get(r, "equipModelId") > 0).ToList();
        var usualSpAtk = baseRows.GroupBy(r => Get(r, "wepmotionCategory"))
            .ToDictionary(g => g.Key, g => g.GroupBy(r => Get(r, "spAtkcategory")).OrderByDescending(x => x.Count()).First().Key);

        var lines = new List<string>
        {
            string.Join('\t', "ds1_id", "name", "weaponCategory", "wepmotion", "spAtk", "usualSpAtk", "behaviorVar", "onHit", "resident",
                "reinforceType", "isCustom", "magic", "dark_hand", "lantern", "vs_ghost", "dragonslayer", "elements", "special", "model",
                "meshes", "materials", "textures", "dummies"),
        };
        foreach (int id in newIds)
        {
            var w = weapons[id];
            string name = ds1.Text("Weapon_name_", id);
            int spAtk = (int)Get(w, "spAtkcategory"), motion = (int)Get(w, "wepmotionCategory");
            int usual = (int)usualSpAtk.GetValueOrDefault(motion, -1);
            string onHit = string.Join("+", new[] { "spEffectBehaviorId0", "spEffectBehaviorId1", "spEffectBehaviorId2" }.Select(f => (int)Get(w, f)).Where(v => v > 0));
            string resident = string.Join("+", new[] { "residentSpEffectId", "residentSpEffectId1", "residentSpEffectId2" }.Select(f => (int)Get(w, f)).Where(v => v > 0));
            string magic = string.Join("", new[] { ("enableMagic", "M"), ("enableSorcery", "S"), ("enableMiracle", "F"), ("enableVowMagic", "P") }.Where(p => Get(w, p.Item1) > 0).Select(p => p.Item2));
            string elements = string.Join("+", new[] { ("attackBaseMagic", "mag"), ("attackBaseFire", "fire"), ("attackBaseThunder", "lit") }.Where(p => Get(w, p.Item1) > 0).Select(p => $"{p.Item2}{Get(w, p.Item1)}"));
            var special = new List<string>();
            if (spAtk != usual) special.Add($"spAtk {spAtk}≠{usual}");
            if (onHit.Length > 0) special.Add("on-hit effect");
            if (Get(w, "isDarkHand") > 0) special.Add("dark hand");
            if (Get(w, "lanternWep") > 0) special.Add("lantern");
            if (Get(w, "isVersusGhostWep") > 0) special.Add("hits ghosts");
            if (Get(w, "isDragonSlayer") > 0) special.Add("dragonslayer");

            string model = $"WP_A_{(int)Get(w, "equipModelId"):D4}";
            string meshes = "-", materials = "-", textures = "-", dummies = "-";
            try
            {
                var parts = ds1.Parts(model);
                var flver = FLVER2.Read(parts.Files.First(f => f.Name.EndsWith(model + ".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
                meshes = flver.Meshes.Count.ToString();
                materials = string.Join("|", flver.Materials.Select(m => m.MTD.Split('\\').Last()));
                textures = string.Join("|", parts.Files.Where(f => f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(f => TPF.Read(f.Bytes).Textures).Select(t => t.Name));
                dummies = string.Join(",", flver.Dummies.Select(d => d.ReferenceID).Distinct().Order());
            }
            catch (Exception e) when (e is FileNotFoundException or InvalidOperationException)
            {
                model += " (MISSING)";
            }
            lines.Add(string.Join('\t', id, name, Get(w, "weaponCategory"), motion, spAtk, usual, Get(w, "behaviorVariationId"), onHit, resident,
                Get(w, "reinforceTypeId"), Get(w, "isCustom"), magic, Get(w, "isDarkHand"), Get(w, "lanternWep"), Get(w, "isVersusGhostWep"),
                Get(w, "isDragonSlayer"), elements, string.Join("; ", special), model, meshes, materials, textures, dummies));
        }
        File.WriteAllLines(outPath, lines);
        Console.WriteLine($"{newIds.Count} weapons → {Path.GetFullPath(outPath)}");
        return 0;
    }
}
