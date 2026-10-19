using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Texts for the Lordran / Drangleic pages of the bonfire Travel menu (session 8; user: "I want the DS1 bonfires to have
/// their own separate Travel page, so it's clear whether a teleport is Lordran or Drangleic"). The paging itself is the
/// loader's (<c>src\loader\travel.c</c>: LB/RB switch the area grid between DS2's areas and the areas holding a DS1
/// bonfire, 40000..49999); this adds what the player reads, in DS2's own menu texts:
/// <list type="bullet">
/// <item>The area grid's key guide, bofire.fmg <see cref="PromptId"/> (89901, vanilla "⑤：Select ⑲：Done ⑳：Back"): the LB/RB
/// command "⑨⑦：Drangleic/Lordran" is inserted after "Select" (session 11: LB shows Drangleic, RB Lordran, in this order). DS2 shows it through menu text kind 0x0b (= bofire, the 12th name in the exe's FMG
/// name table at 0x141585e70; npcMenu, kind 0x0c, has its own 89901) from the Top's open (0x1400d5480, after the help
/// line 89001 "Select area to travel to") and when the bonfire list closes (List slot 29, 0x1400d5680); no other code
/// loads 89901 of kind 0x0b (decompile search for 0x15f2d). The LB/RB glyph pair and the colon are taken from DS2's own
/// LB/RB command text, common.fmg 10011 "⑨⑦：Toggle" (the vanilla RB key-guide entry 0x14160c058, mask 0x80000; ⑨ is
/// LB and ⑦ RB as in mapevent 3005 "⑨： Attack (L weapon)" / 3003 "⑦： Attack (R weapon)").</item>
/// <item>The page names for the window title, bofire.fmg <see cref="PageNameBase"/> + page (89010 "Drangleic", 89011
/// "Lordran"; unused ids in vanilla). The loader reads them with the game's text lookup (0x140503620, kind 0x0b) and
/// falls back to the same English names when a pack without them is installed.</item>
/// </list>
/// Only the English FMGs are written, like every other text the pack adds (Pack.Text).
/// The loader's page rule is "an area is Lordran when one of its bonfires has an id in <see cref="Ds1BonfireMin"/>..
/// <see cref="Ds1BonfireMax"/>" (MapBuilder.LordranBonfireId gives 40000..48995; DS2's own ids are 2650..37685 and
/// debug 62650), so the build checks it: every bonfire row the pack adds is in that range and no vanilla one is.
/// </summary>
public static class TravelPages
{
    public const int PromptId = 89901;
    public const int PageNameBase = 89010; // + 0 Drangleic, + 1 Lordran (travel.c PAGE_NAME_TEXT)
    const int LbRbToggleText = 10011;      // common.fmg: "⑨⑦：Toggle"
    public const string Drangleic = "Drangleic";
    public const string Lordran = "Lordran";
    public const int Ds1BonfireMin = 40000, Ds1BonfireMax = 49999; // travel.c DS1_BONFIRE_MIN/MAX

    public static string Build(Pack pack, PARAM vanillaBonfires)
    {
        var vanillaIds = vanillaBonfires.Rows.Select(r => r.ID).ToHashSet();
        var wrongVanilla = vanillaIds.Where(id => id is >= Ds1BonfireMin and <= Ds1BonfireMax).ToList();
        var added = pack.Param("MapObjectBonfireParam").Rows.Where(r => !vanillaIds.Contains(r.ID)).Select(r => r.ID).ToList();
        var wrongAdded = added.Where(id => id is < Ds1BonfireMin or > Ds1BonfireMax).ToList();
        if (wrongVanilla.Count > 0 || wrongAdded.Count > 0)
            throw new InvalidDataException($"Travel pages: the loader puts bonfires {Ds1BonfireMin}..{Ds1BonfireMax} on the Lordran page, but vanilla rows " +
                $"[{string.Join(", ", wrongVanilla)}] are inside and added rows [{string.Join(", ", wrongAdded)}] outside that range");

        var bofire = pack.Text("bofire");
        string prompt = bofire.Entries.FirstOrDefault(e => e.ID == PromptId)?.Text
            ?? throw new InvalidDataException($"bofire.fmg has no {PromptId} (the Travel grid's key guide)");
        foreach (int id in new[] { PageNameBase, PageNameBase + 1 })
            if (bofire.Entries.Any(e => e.ID == id && !string.IsNullOrEmpty(e.Text)))
                throw new InvalidDataException($"bofire.fmg {id} is already used; pick other page-name ids (and travel.c PAGE_NAME_TEXT)");

        // DS2's LB/RB key-guide text, read without adding common.fmg to the pack
        var common = FMG.Read(Path.Combine(pack.Ds2Data, "menu", "text", "english", "common.fmg"));
        string toggle = common.Entries.FirstOrDefault(e => e.ID == LbRbToggleText)?.Text
            ?? throw new InvalidDataException($"common.fmg has no {LbRbToggleText} (LB/RB \"Toggle\")");
        int colon = toggle.IndexOf('：');
        if (colon <= 0) throw new InvalidDataException($"common.fmg {LbRbToggleText} \"{toggle}\" has no glyphs before '：'");
        string command = $"{toggle[..(colon + 1)]}{Drangleic}/{Lordran}"; // session 11: LB (left glyph) = Drangleic, RB = Lordran (travel.c)

        // after the first command: the second command starts at the first " " followed by a button glyph (① U+2460 .. ⑳ U+2473)
        int second = Enumerable.Range(1, Math.Max(0, prompt.Length - 1)).FirstOrDefault(i => prompt[i - 1] == ' ' && prompt[i] >= '①' && prompt[i] <= '⑳');
        string updated = prompt.Contains(command) ? prompt
            : second > 0 ? prompt[..second] + command + " " + prompt[second..]
            : prompt + " " + command;

        pack.SetText("bofire", PromptId, updated);
        pack.SetText("bofire", PageNameBase, Drangleic);
        pack.SetText("bofire", PageNameBase + 1, Lordran);
        string summary = $"Travel pages: bofire {PromptId} \"{prompt}\" -> \"{updated}\"; page names {PageNameBase} \"{Drangleic}\", {PageNameBase + 1} \"{Lordran}\"; " +
            $"Lordran bonfires [{string.Join(", ", added)}] all in {Ds1BonfireMin}..{Ds1BonfireMax} (LB/RB paging: loader travel.c)";
        pack.Note(summary);
        return summary;
    }
}
