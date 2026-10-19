using Lordran.Tools.Formats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 11 (track burgchr11): <c>burg-chr-check PACK</c> - every Burg character a --burg-chrs pack builds is complete. Reads the
/// pack's <c>lordran-chr\burg-placements.txt</c> (no file: the pack has no Burg characters, PASS) and, per MSB part and per boss:
/// the EnemyParam row; its model (model/chr/cMMMM.bnd + .texbnd), anim set (morpheme4 anibnd, timeact _pl/_sfx/_snd), sound bank
/// (frpg2_c{animId/100 rounded down to tens}.bnd); the EnemyEquipWeaponParam of its weapon slot and that weapon's behaviour rows
/// (EnemyBehavior / Second / Third / Combo); its drop lot; the DS1 AI pair (ai{high}.esd, ai{low}.esd, LogicComParam high). The
/// parts of characters not ported yet are listed (informational).
/// </summary>
public static class BurgChrCheck
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: burg-chr-check PACK"); return 1; }
        string pack = args[1];
        string manifest = Path.Combine(pack, "lordran-chr", "burg-placements.txt");
        if (!File.Exists(manifest)) { Console.WriteLine("burg-chr-check: no Burg characters in this pack (built without --burg-chrs): nothing to check"); return 0; }
        var ps = ParamStore.LoadDs2(Path.Combine(pack, "title"), ExtractDs2.DefaultOut);
        string gd = Path.Combine(pack, "gamedata");
        bool Has(string rel) => File.Exists(Path.Combine(gd, rel.Replace('/', Path.DirectorySeparatorChar)));
        var problems = new List<string>();
        var checkedEp = new HashSet<int>();
        int parts = 0, ais = 0, ereqs = 0;
        void Enemy(int ep, string who)
        {
            if (!checkedEp.Add(ep)) return;
            var row = ps.Params["EnemyParam"][ep];
            if (row == null) { problems.Add($"{who}: EnemyParam {ep} missing"); return; }
            int model = Convert.ToInt32(row["modelId"].Value), anim = Convert.ToInt32(row["animId"].Value);
            foreach (var f in new[] { $"model/chr/c{model:D4}.bnd", $"model/chr/c{model:D4}.texbnd", $"morpheme4/chr/c{anim / 100:D4}.anibnd.dcx",
                         $"timeact/chr/c{anim:D6}_pl.tae", $"timeact/chr/c{anim:D6}_sfx.tae", $"timeact/chr/c{anim:D6}_snd.tae", $"sound/frpg2_c{anim / 1000 * 10:D4}.bnd" })
                if (!Has(f)) problems.Add($"{who}: EnemyParam {ep} needs {f}");
            int slot = Convert.ToInt32(row["defaultEquipWeapon"].Value);
            int wid = row[$"equipWeaponAId0{slot}"] is { } wc ? Convert.ToInt32(wc.Value) : 0;
            var w = wid > 0 ? ps.Params["EnemyEquipWeaponParam"][wid] : null;
            if (w == null) problems.Add($"{who}: EnemyParam {ep} weapon slot {slot} → EnemyEquipWeaponParam {wid} missing");
            else
            {
                int beh = Convert.ToInt32(w["behaviorId"].Value);
                foreach (var p in new[] { "EnemyBehaviorParam", "EnemyBehaviorSecondParam", "EnemyBehaviorThirdParam", "EnemyComboBehaviorParam" })
                    if (ps.Params[p][beh] == null) problems.Add($"{who}: EnemyParam {ep} behaviour {beh} has no {p} row");
            }
            uint lot = Convert.ToUInt32(row["itemLotId"].Value);
            if (lot > 0 && ps.Params["ItemLotParam2_Chr"][(int)lot] == null) problems.Add($"{who}: EnemyParam {ep} drop lot {lot} missing");
        }
        void Ai(int high, int low, string who)
        {
            ais++;
            if (!Has($"ezstate/ai{high:D6}.esd")) problems.Add($"{who}: ai{high:D6}.esd missing");
            if (!Has($"ezstate/ai{low:D6}.esd")) problems.Add($"{who}: ai{low:D6}.esd missing");
            if (ps.Params["LogicComParam"][high] == null) problems.Add($"{who}: LogicComParam {high} missing");
        }
        var missing = new List<string>();
        foreach (var line in File.ReadLines(manifest))
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var c = line.Split('\t');
            if (c[0] == "missing") { missing.Add(c[1]); continue; }
            if (c[0] == "ereq") { ereqs++; continue; }   // rule E-REQ channel table (M3 events), informational
            if (c[0] == "boss")
            {
                int ep = int.Parse(c[3].Split(' ')[1]);
                Enemy(ep, $"boss {c[2]}");
                var lg = c[6].Split(' ')[1].Split('/');
                Ai(int.Parse(lg[0]), int.Parse(lg[1]), $"boss {c[2]}");
                continue;
            }
            parts++;
            Enemy(int.Parse(c[5]), $"{c[0]} ({c[3]} NpcParam {c[4]})");
            if (c[7] == "template") problems.Add($"{c[0]}: no translated DS1 AI (think {c[6]})");
            else { var lg = c[7].Split('/'); Ai(int.Parse(lg[0]), int.Parse(lg[1]), $"{c[0]} think {c[6]}"); }
        }
        foreach (var p in problems.Take(40)) Console.WriteLine("  FAIL " + p);
        Console.WriteLine($"  {parts} MSB parts, {checkedEp.Count} EnemyParams, {ais} AI pairs checked, {ereqs} E-REQ channels listed; m10_01 character groups not ported yet ({missing.Count}): {string.Join("; ", missing)}");
        Console.WriteLine($"burg-chr-check: {(problems.Count == 0 ? "all Burg characters complete" : $"{problems.Count} problems")}");
        return problems.Count == 0 ? 0 : 1;
    }
}
