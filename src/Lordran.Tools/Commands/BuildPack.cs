using Lordran.Tools.Build.Chr;
using Lordran.Tools.Build;
using Lordran.Tools.Build.Maps;
using Lordran.Tools.Formats;

namespace Lordran.Tools.Commands;

/// <summary>
/// build-pack [--weapons id+id+...|all] [--ammo all] [--test-shop] [--test-shop-items id+id+...] [--no-title] [--out DIR]
///            [--map asylum [--entry item|fireplace|tree] [--no-asylum-enemies] [--no-boss]] (tests: --event-test, --enemy-test [GEN+...],
///            --chr-test, --no-navmesh, --only-enemy ID+ID) [--tb-atmos] [--no-ds1-sound] [--cutscenes [--no-cutscene-npcs]] [--no-ds1-sky]
///            [--no-ds1-palette] [--exposure-bias X] [--atmos-template MAP=AREA[:FILTER]]
/// Builds the Lordran content pack from vanilla DS2 + the player's DSR install.
/// Session 8 (atmosphere): the DS1 maps get DS1's lighting, fog, post-processing, player light and map effects
/// (Atmosphere; --tb-atmos = Things Betwixt's greyed values as before); DS1's FMOD projects, banks and sound regions
/// (BGM, ambient loops, event SEs and one-shot SFX: rules SND1-SND5, Build.Events.MapSounds) are on by default since
/// session 10 (track asy-sound; --no-ds1-sound = Things Betwixt's map bank and no sound regions, as before; --ds1-sound
/// is still accepted).
/// Session 8 (cutscene track): with --map asylum and Firelink, --cutscenes makes DS1's crow cutscenes (scn180125/100225
/// cliff, 180126/100226 nest) DS2 demos played by the map warps (Build.Cutscenes). Opt-in until the main session's cliff
/// and nest runs pass in game (fix pass: the cliff/nest warps are the only way to Firelink, and a demo that fails to
/// load would hang them; the plain warps are verified, s5-37); without it (or with --no-cutscenes) the warps are plain
/// as before. --no-cutscene-npcs leaves DS1's human NPCs out of the demos (they join only when the map registers them).
/// Session 8 (track skylight): DS1 skies as DS2 sky domes with DS1's baked look and DS1's colour grading as DS2 palettes
/// (Sky, Palette); --no-ds1-sky / --no-ds1-palette = the earlier NoLit[D] skies / the identity palette, for A/B tests.
/// Skylight fix pass: --exposure-bias X multiplies the translated ToneMap AvgLuminance (rule A8c; DS2's exposure is
/// 1/(AvgLuminance × LuminanceBalance), FUN_140b93800: X &gt; 1 darker); --atmos-template m19_02_00_00=m10_10_00_00:0 pins
/// the DS2 area a DS1 map's fallback fields come from (rule A3 A/B).
/// --weapons all ports every DS1-only weapon, shield, bow and catalyst in the item map (not Fists, DS1's "unarmed"
/// placeholder, and not ammunition, which has its own param chain). A weapon that fails is reported and skipped.
/// --ammo all ports every DS1-only arrow and bolt (AmmoPorter).
/// The title screen gets "ECHOES OF LORDRAN" under "SCHOLAR OF THE FIRST SIN" (TitleScreen) unless --no-title.
/// The loading screen gets "ECHOES OF LORDRAN" (LoadingScreens.LogoLine) under its DARK SOULS II logo (rule LD3) unless --no-title.
/// --test-shop adds every ported item to Maughlin the Armourer and Sweet Shalquoir (Majula) for 1 soul (testing only).
/// --test-shop-items adds existing DS2 items (ItemParam ids, e.g. 50870000 Lenigrast's Key) to that shop as well.
/// --dev-kit (session 11, devkit11; TEST ONLY, never default): the "Lordran Dev Kit" item and lots holding every item the
/// pack added (Build/DevKit.cs; the loader's devkit.c gives the kit once and the lots when it is used; devkit-check PACK).
/// --map-preview overlays every DS1 Undead Asylum map piece on Majula, the Asylum courtyard bonfire on The Far Fire
/// (MapPreview; visual test of the map-piece converter, session 3).
/// --map asylum builds the Undead Asylum as its own map m18_01_00_00 (MapBuilder) and the entry to it: by default the
/// "Echo of Lordran" item (EntryItem: an infinite Homeward Bone that warps to the cell; the loader gives it), or with
/// --entry fireplace / --entry tree the older Things Betwixt entries (they edit Things Betwixt's event files).
/// With --map, the Travel menu texts for the loader's Lordran / Drangleic pages (TravelPages: the LB/RB key guide and the
/// page names; session 8); on by default since session 11, --no-travel-pages leaves the vanilla texts.
/// Session 11 (track gest11): DS1's 15 gestures on a Lordran page of the gesture menu (Build/Chr/Ds1Gestures, rules
/// GS1-GS4; the loader's gesture.c pages with LB/RB): additive c0001.anibnd + c000100 TAEs (proved vanilla-identical,
/// guard I1d-G), names, icons, key-guide hint, lordran\gestures.ini; on by default, --no-gestures leaves them out,
/// --test-gestures shows every DS1 gesture as learned (loader table only; nothing is written to a save).
/// Vanilla bonfire rows are never changed: the game rebuilds every save's bonfire table from MapObjectBonfireParam on
/// load and drops (with its lit level) any bonfire whose map isn't registered, so repointing a DS2 bonfire damages saves
/// (session 4: the removed --asylum-travel-test hook cost the user's MP The Far Fire). The build fails if one differs.
/// With --map, DS1's NPCs present at the start (session 8, track npc: Oscar in the Asylum; the Crestfallen Warrior,
/// Petrus, Anastacia and the Undead Merchant at Firelink) stand at their DS1 spots as DS2 human NPCs in their DS1 outfits
/// (NpcPorter; DS1-only armour converted by ArmorPorter); --no-npcs leaves them out.
/// Session 8 (track npc, step 2): their DS1 talk scripts translated into each map's talk ESD (TalkBuilder /
/// TalkTranslator: talk rows, talk FMG, NPC windows with DS1's shops, DS1's gifts); --no-talk leaves the NPCs mute;
/// DS1's speech projects are added by default (session 9, run s9-12: verified; --no-ds1-voices leaves them out)  with the voice events renamed to DS2's names (TalkVoices).
/// Session 8, track boss step 2: the 1:1 bosses (Ds1Boss: DS1's Asylum Demon c2232) are placed by default at their DS1 MSB
/// parts with DS1's own AI (Build/Ai) and DS1's boss events translated (the drop, boss bar, fog, defeat, rewards);
/// --no-boss leaves them out (the fallback if the boss's files crash the map): no boss files or generator, the boss events
/// stay untranslated (boss absent); DS1's reward event 970 still translates (inert: flag 16 can't be set without the boss).
/// --boss-test (step 1) is accepted as a no-op alias.
/// --out must be a pack folder (default data\lordran\pack; sandboxes data\sandbox\NAME\pack): Pack.OutputFolderProblem
/// refuses anything else before the build starts, because the output folder is emptied (session 8).
/// </summary>
public static class BuildPack
{
    const int FistsId = 900000;
    const int TestLotBase = 66990000; // ItemLotParam2_Other test lots (--test-shop)

    public static int Run(string[] args)
    {
        string outDir = Pack.DefaultOut;
        var weapons = new List<int>();
        var shopItems = new List<int>();
        bool testShop = false, allWeapons = false, allAmmo = false, allRings = false, title = true, mapPreview = false, asylum = false, eventTest = false, noNavmesh = false, chrTest = false, asylumEnemies = true, firelink = true, asylumScript = true, ds1Atmosphere = true, ds1Sound = true, travelPages = true, hitGroups = false, cutscenes = false, cutsceneNpcs = true, ds1Sky = true, ds1Palette = true, npcs = true, talk = true, ds1Voices = true, bloodMessages = true;
        var atmosTemplates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // DS2 map id → "mAA_BB_CC_DD[:filter]"
        bool boss = true;
        bool burg = false; // session 11 (burg11, BURG-PLAN M1): --map burg = DS1's Undead Burg + Parish as m19_41, joined to Firelink
        bool demonRuins = false; // session 11 (burg11, round 9): --map demon-ruins = DS1's Demon Ruins + Lost Izalith (m14_01), a third H2 member of m19_40
        bool blighttown = false; // session 11 (burg11, round 8): --map blighttown = DS1's Blighttown (m14_00) packed into the Depths' host (rule H2)
        bool depths = false; // session 11 (burg11, round 7): --map depths = DS1's Depths (m10_00) as m19_40, joined to the Burg
        bool burgChrs = false; List<int>? burgChrTest = null; // session 11 (burgchr11): --burg-chrs / --burg-chr-test [ID+ID…]
        List<int> enemyTest = [];
        int chrTestOnly = 0;
        string entry = "item";
        List<int>? armorIds = null, spellIds = null, goodsIds = null; // session 11 (equip11): --armor / --spells / --goods all|ID+ID
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                // session 11 (track equip11, PLAN milestone 7): DS1-only armour (rules AR1-AR7), spells (SP1-SP8), goods (GD1-GD6)
                case "--armor" or "--armour": armorIds = args[++i] == "all" ? ArmorItems.NewIds() : args[i].Split('+').Select(int.Parse).ToList(); break;
                case "--spells": spellIds = args[++i] == "all" ? SpellPorter.NewIds() : args[i].Split('+').Select(int.Parse).ToList(); break;
                case "--goods": goodsIds = args[++i] == "all" ? GoodsPorter.NewIds() : args[i].Split('+').Select(int.Parse).ToList(); break;
                case "--weapons" when args[i + 1] == "all": i++; allWeapons = true; break;
                case "--weapons": weapons.AddRange(args[++i].Split('+').Select(int.Parse)); break;
                case "--test-shop": testShop = true; break;
                case "--dev-kit": Build.DevKit.Enabled = true; break; // session 11 (devkit11), TEST ONLY: the "Lordran Dev Kit" (every ported item; loader devkit.c gives it)
                case "--no-title": title = false; break;
                case "--ammo" when args[i + 1] == "all": i++; allAmmo = true; break;
                case "--rings" when args[i + 1] == "all": i++; allRings = true; break; // session 10 (asy-items i2, rule G9): every DS1-only ring (default: on demand, when a lot, treasure or trade needs one)
                case "--test-shop-items": shopItems.AddRange(args[++i].Split('+').Select(int.Parse)); break;
                case "--out": outDir = args[++i]; break;
                case "--balance": Build.Chr.Ds1Balance.TablePath = Path.GetFullPath(args[++i]); break; // session 11 (bal11): another balance table (default Mappingsalance.tsv; rules BAL1-BAL3)
                case "--balance-strength": Build.Chr.Ds1Balance.StrengthOverride = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break; // session 11 (bal11): rule BAL1 strength c (overrides the table's)
                case "--map-preview": mapPreview = true; break;
                case "--giant-tree": entry = "tree"; break;
                case "--event-test": eventTest = true; break;
                case "--no-navmesh": noNavmesh = true; break; // control test: empty navmesh
                case "--no-asylum-script": asylumScript = false; break; // without the Asylum's doors/gate/fogs/boulder script (session 7)
                case "--hall-door-template": AsylumScript.HallTemplate = args[++i]; break; // session 10 (asy-events e2), TEST ONLY: COVERAGE CV5 A/B (o15_1060: the hall door and gate take a direct request of their rest state 30)
                case "--test-key-locks": Build.Events.KeySources.KeepAllLocks = true; break; // session 10 (asy-events fix pass e2), TEST ONLY: keep every K6 key lock even when the build has no source for the key (rule K6 key source)
                case "--chr-test": chrTest = true; break;     // test: DS1 Asylum Hollow variants on a DS2 body, in the courtyard
                case "--chr-test-only": chrTest = true; chrTestOnly = int.Parse(args[++i]); break; // test: only this EnemyParam id
                case "--only-enemy": AsylumEnemies.OnlyEnemyParams = [.. args[++i].Split('+').Select(int.Parse)]; break; // session 9 (track enemyfix): isolation build, only these DS1 EnemyParam ids are placed (Asylum + Firelink, regulars and 1:1 bosses; NPCs stay)
                case "--no-firelink": firelink = false; break;
                case "--no-firelink-script": FirelinkScript.Enabled = false; break; // session 9 (track firelink, fix pass): Firelink without its translated DS1 events (crash A/B)
                // session 8 (cutscene track): DS1's crow cutscenes as DS2 demos, opt-in until verified in game (fix pass)
                case "--cutscenes": cutscenes = true; break;
                case "--no-cutscenes": cutscenes = false; break;
                case "--no-cutscene-npcs": cutsceneNpcs = false; break; // the demos without DS1's human NPCs (bisecting)
                case "--tb-atmos": ds1Atmosphere = false; break; // session 8: Things Betwixt's greyed light/fog/filter/env as before
                case "--ds1-sound": ds1Sound = true; break;      // session 8 opt-in; default since session 10 (asy-sound, G6)
                case "--no-ds1-sound": ds1Sound = false; break;  // session 10: TB's map bank, no DS1 sound regions (A/B)
                case "--no-ds1-sky": ds1Sky = false; break;      // session 8 (skylight): skies as NoLit[D] map pieces, as before
                case "--no-ds1-palette": ds1Palette = false; break; // session 8 (skylight): identity palette instead of DS1's ToneCorrect
                case "--exposure-bias": Build.Maps.Atmosphere.ExposureBias = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break; // session 8 (skylight fix pass): multiplies the translated ToneMap AvgLuminance on every DS1 zone id (>1 darker), the in-game calibration knob of rule A8c
                case "--atmos-template": { var kv = args[++i].Split('=', 2); atmosTemplates[kv[0]] = kv[1]; break; } // skylight fix pass: pin the fallback DS2 area of a DS1 map (rule A3 A/B), e.g. m19_02_00_00=m10_10_00_00:0
                case "--map-sfx": // session 9 (mist track): placement of DS1's area map effects: b1d (default, rule B1d) | b1c | b1b | b1c-up | dense | ds1 (rule B2b: DS1's own effect converted; B1d where it can't be) | ds2=ID (A/B)
                    if (!Build.Maps.MapEffects.ParseMode(args[++i])) { Console.Error.WriteLine($"--map-sfx {args[i]}: b1d | b1c | b1b | b1c-up | dense | ds1 | ds2=ID"); return 1; }
                    break;
                case "--sfx-tex-max": Build.Maps.FfxPorter.TexMax = int.Parse(args[++i]); break; // session 9 (mist track): largest side of a converted DS1 effect texture (default 512)
                case "--sfx-control": Build.Maps.MapEffects.ControlEffect = int.Parse(args[++i]); break; // mist fix pass (A/B only): DS2 effect (3020) 1 m above each DS1 area-effect point = positive control for map SFX spawning
                case "--sfx-dsr-alpha": // mist fix pass (A/B): on (default) | off = rule B2b without DSR's colour-multiplier alpha (fix pass 3: folded into 71's p12)
                    {
                        string v = args[++i].ToLowerInvariant();
                        if (v is not ("on" or "off")) { Console.Error.WriteLine($"--sfx-dsr-alpha {args[i]}: on | off"); return 1; }
                        Build.Maps.FfxPorter.DsrAlpha = v == "on";
                        break;
                    }
                case "--sfx-noend": // mist fix pass 3 (A/B for later): ds1 (default: DS1's own never-ending lifetime) | sentinel (DS2's t2(-1/30,-1/30))
                    {
                        string v = args[++i].ToLowerInvariant();
                        if (v is not ("ds1" or "sentinel")) { Console.Error.WriteLine($"--sfx-noend {args[i]}: ds1 | sentinel"); return 1; }
                        Build.Maps.FfxPorter.NoEndSentinel = v == "sentinel";
                        break;
                    }
                case "--travel-pages": travelPages = true; break; // the default since session 11 (the loader with travel.c builds); kept for old command lines
                case "--no-travel-pages": travelPages = false; break; // session 11: vanilla Travel texts (no LB/RB key guide, no page names)
                case "--no-gestures": Build.Chr.Ds1Gestures.Enabled = false; break; // session 11 (gest11): no DS1 gesture page content (vanilla c0001 / c000100 / gesture texts)
                case "--test-gestures": Build.Chr.Ds1Gestures.TestUnlockAll = true; break; // session 11 (gest11), TEST: every DS1 gesture shows as learned (gestures.ini unlock_all=1)
                case "--no-asylum-enemies": asylumEnemies = false; break; // default: DS1's first-visit Asylum Hollows at their MSB positions
                case "--no-npcs": npcs = false; break; // session 8 (track npc): default places DS1's start NPCs (Oscar; Firelink's Crestfallen Warrior, Petrus, Anastacia, Undead Merchant)
                case "--no-talk": talk = false; break;      // session 8 (track npc, step 2): the NPCs without their translated DS1 talk
                case "--no-npc-idle": Build.Chr.NpcIdle.Enabled = false; break; // session 9 (track npcidle): NPCs keep animId 100 + DS2's stand-in startup state (A/B)
                case "--npc-ds2-layers": Build.Chr.NpcIdle.Ds1Layers = false; break; // session 9 (track npcpose): keep DS2's head look + uneven-terrain IK on the DS1 idle slots (A/B of rules I6b/I9)
                case "--npc-foot-ik-off": Build.Chr.NpcIdle.FootIkOffShared = true; break; // npcpose pass 3 (A/B): rule I9 also on the human route (shared IK node: player + vanilla NPC startup states lose foot IK)
                case "--npc-idle-human": Build.Chr.NpcIdle.Route = Build.Chr.NpcIdle.IdleRoute.Human; break; // track npcpose2 (A/B of rule I1d): rule I1c, ships the global c0001.anibnd + c000100 TAEs (collateral: Saulden, Maughlin's 4th state)
                case "--npc-template-ai": // track npcpose2 (A/B of rule AI-N1): vanilla = the template's own logic pair (no AI files)
                    if (i + 1 >= args.Length || args[i + 1] != "vanilla") { Console.Error.WriteLine("--npc-template-ai vanilla"); return 1; }
                    Build.Chr.NpcAi.VanillaTemplateAi = true; i++; break;
                case "--npc-idle-own-network": Build.Chr.NpcIdle.Route = Build.Chr.NpcIdle.IdleRoute.Own; break; // track npcpose pass 2 (A/B of rule I1c): the old c8001 route (animId 800100: DS2 spawns a generic NPC)
                case "--npc-idle-probe": Build.Chr.NpcIdle.Route = Build.Chr.NpcIdle.IdleRoute.Own; Build.Chr.NpcIdle.ProbeIdle = args[++i]; break; // A/B: + c8001's BasicMove idles = this c0001 clip (e.g. a00_16_0750)
                case "--ds1-voices": ds1Voices = true; break; // session 8 (track npc, step 2): DS1 speech banks, voice events renamed
                case "--no-ds1-voices": ds1Voices = false; break; // session 9: voices are the default (run s9-12: without a speech bank DS2 skips or stalls voiced talk rows)
                // session 8: DS1's event-toggled collisions (the gate panel h0550B1) in hit groups, switched by the translated
                // events with 132153. Opt-in (fix pass) until an in-game run shows 132153 removes the panel at load; the
                // default leaves them out as before (verified s8-2: the open gate is passable)
                // session 10 (asy-items): rule C2 test preset (the DS1 class 0-9 set at every Asylum load, no origin prompt);
                // rules M1/M2/M4 (BloodMessages): --msg-cat N (A/B of the visible messages' EventLocation category, default
                // 12), --ds1-hidden-messages (DS1's Seek Guidance messages as DS2 Guidance rows, cat 22), --no-blood-messages
                case "--test-flags": AsylumScript.TestFlags.AddRange(args[++i].Split(',', '+').Select(int.Parse)); break; // session 10 (asy-events; asy-items i2: -F clears F), TEST ONLY: DS1 flags set at every Asylum load (e.g. 11810000 = the return trip)
                case "--ds1-origin":
                    {
                        if (!int.TryParse(args[++i], out int c) || c is < 0 or >= Build.Events.OriginPrompt.Classes) { Console.Error.WriteLine($"--ds1-origin {args[i]}: 0-9 (DS1 class: 0 Warrior … 9 Deprived)"); return 1; }
                        Build.Events.OriginPrompt.Preset = c;
                        break;
                    }
                case "--msg-cat": BloodMessages.VisibleCategory = int.Parse(args[++i]); break;        // A/B only (default 12)
                case "--ds1-hidden-messages": BloodMessages.HiddenMessages = true; break;             // rule M4 (default off: user decision)
                case "--no-blood-messages": bloodMessages = false; BloodMessages.Enabled = false; break;
                case "--hit-groups": hitGroups = true; break;
                case "--no-hit-groups": hitGroups = false; break;
                case "--ds1-rig": ChrPorter.UseDs1Rig = true; break; // session 8: the 1:1 unarmed Hollow (DS1 body/skeleton/animations); on by default since run s8-7
                case "--no-ds1-rig": ChrPorter.UseDs1Rig = false; break;
                case "--no-ds1-ai": ChrPorter.UseDs1Ai = false; break; // session 8 (track enemies step 2): DS2 template AI + interim aliases instead of the translated DS1 AI
                case "--ds1-ai-no-attack-interrupts": Lordran.Tools.Build.Ai.GoalCompiler.BattleInterrupts = false; break; // session 11 (G5 AI7c): the pre-AI7c interrupt set (Attack / Approach / Turn not interruptible), A/B test
                case "--ds1-ai-unread-interrupts": Lordran.Tools.Build.Ai.Ds2AiMachines.UnreadInterrupts = true; break; // fix pass: also map DS1 interrupts 6/8/11 (no vanilla DS2 AI reads f5050 6/8/11), in-game A/B test
                case "--boss-test": Console.WriteLine("--boss-test: the 1:1 Asylum Demon is placed by default now (session 8, track boss step 2)"); break;
                case "--no-boss": boss = false; break; // session 8, track boss step 2: leave the 1:1 bosses out
                case "--enemy-test":
                    enemyTest = [3000];
                    if (i + 1 < args.Length && args[i + 1].Split('+').All(s => int.TryParse(s, out _))) enemyTest = args[++i].Split('+').Select(int.Parse).ToList();
                    break;
                case "--entry" when args[i + 1] is "item" or "fireplace" or "tree": entry = args[++i]; break;
                // session 11 (fix11, user decision): where the Echo of Lordran lands. firelink (default) = Firelink Shrine's arrival point;
                // the Asylum is DS1's second visit (return trip, via the nest): DS1's Firelink fallback 11020001 (sets 11810000) is kept
                // (no rule XM1) and the XM1-R repair is not built. cell = the old entry (the Asylum cell, first visit; XM1 + XM1-R)
                case "--entry" when args[i + 1] is "firelink" or "cell": EntryItem.ToFirelink = args[++i] == "firelink"; break;
                case "--debug-markers": DebugMarkers.Enabled = true; break; // session 11 (fix11): every Lordran cross-map warp (132125) first sets its own marker flag 209980 + n (off by default)
                case "--map" when args[i + 1] == "asylum": i++; asylum = true; break;
                case "--map" when args[i + 1] == "burg": i++; burg = true; break;
                case "--map" when args[i + 1] == "depths": i++; depths = true; break;
                case "--map" when args[i + 1] == "blighttown": i++; blighttown = true; break;
                case "--map" when args[i + 1] == "demon-ruins": i++; demonRuins = true; break;
                case "--map-cap": // session 11 (cap11), rule CAP1: WorldMapList up to N lines, for a loader with map_cap >= N (src\loader\mapcap.c)
                    {
                        if (!int.TryParse(args[++i], out int n) || n < MapBuilder.VanillaMapCap || n > MapBuilder.LoaderMapCapMax) { Console.Error.WriteLine($"--map-cap {args[i]}: {MapBuilder.VanillaMapCap}..{MapBuilder.LoaderMapCapMax}"); return 1; }
                        MapBuilder.WorldMapListCap = n;
                        Build.Events.Ds2FlagStore.MapSlotsWithPersistentBlock = n;
                        Console.WriteLine($"--map-cap {n}: needs the loader's map cap (loader.ini map_cap={n} or more; DarkSoulsII.exe 1.0.3.0)");
                        break;
                    }
                // session 11 (burgchr11): the Burg's (DS1 m10_01) characters as 1:1 DS2 characters, not placed in the Burg (M3); --burg-chr-test
                // [ID+ID…] also puts them (default one per anim set) in the Asylum courtyard instead of the Asylum's enemies (test only)
                case "--burg-chrs": burgChrs = true; ChrPorter.Burg = true; break;
                case "--burg-chr-test":
                    burgChrs = true; ChrPorter.Burg = true; burgChrTest = [];
                    if (i + 1 < args.Length && args[i + 1].Split('+').All(x => int.TryParse(x, out _))) burgChrTest = args[++i].Split('+').Select(int.Parse).ToList();
                    break; // session 11 (burg11): needs --map asylum (Firelink is the Burg's neighbour)
                case "--asylum-travel-test":
                    Console.Error.WriteLine("--asylum-travel-test was removed: repointing a DS2 bonfire damages saves (see HANDOFF, session 4)");
                    return 1;
                default: Console.Error.WriteLine($"unknown option {args[i]}"); return 1;
            }
        }
        // session 8: Pack.Write empties the output folder, so a wrong --out (a sandbox root, data\sandbox, a source folder) is
        // refused here, before the build, as well as in Write itself
        if (Pack.OutputFolderProblem(outDir) is string outProblem)
        {
            Console.Error.WriteLine($"refusing --out {Path.GetFullPath(outDir)}: {outProblem}");
            return 1;
        }

        if (Build.Chr.Ds1Gestures.Enabled && Build.Chr.NpcIdle.Route == Build.Chr.NpcIdle.IdleRoute.Human)
        {
            Console.Error.WriteLine("--npc-idle-human edits c0001.anibnd too (its DS1 idle slots); build it with --no-gestures (session 11, gest11)");
            return 1;
        }
        if (Build.Chr.Ds1Gestures.TestUnlockAll && !Build.Chr.Ds1Gestures.Enabled) { Console.Error.WriteLine("--test-gestures needs the DS1 gestures (drop --no-gestures)"); return 1; }
        if (burg && (!asylum || !firelink)) { Console.Error.WriteLine("--map burg needs --map asylum with Firelink (the Burg's neighbour; BURG-PLAN M1)"); return 1; }
        if (depths && !burg) { Console.Error.WriteLine("--map depths needs --map burg (the Depths' neighbour)"); return 1; }
        ChrPorter.Depths = depths && burgChrs;   // round 7: the Depths' variants of the ported characters
        // the MapHosts options this build ports by the generic route (rows in table order)
        var hostOptions = new HashSet<string> { "--map burg" };
        if (depths) hostOptions.Add("--map depths");
        if (blighttown && !depths) { Console.Error.WriteLine("--map blighttown needs --map depths (its H2 host and neighbour)"); return 1; }
        if (blighttown) hostOptions.Add("--map blighttown");
        if (demonRuins && !blighttown) { Console.Error.WriteLine("--map demon-ruins needs --map blighttown (its H2 host member and neighbour)"); return 1; }
        if (demonRuins) hostOptions.Add("--map demon-ruins");
        // session 11 (burg11, round 8), rule H2: DS1 maps of this build that share a host are merged into the host's first
        // row (H2Merge: one DS1 map for every module); the others are built as part of it
        var h2Pairs = MapHosts.Rows.Where(r => hostOptions.Contains(r.Option)).GroupBy(r => r.Host)
            .Where(g => g.Count() > 1).SelectMany(g => g.Skip(1).Select(s => (g.First().Ds1Map, s.Ds1Map))).ToList();
        if (h2Pairs.Count > 0)
        {
            H2Merge.Activate(Program.DefaultDsrRoot, h2Pairs);
            foreach (var l in H2Merge.Report) Console.WriteLine("  " + l);
        }
        // session 11 (burg11), rule SND4: a DS1 area whose sound project serves several DS2 hosts of this build ships it loose
        Atmosphere.SharedAreas.Clear();
        if (burg) Atmosphere.SharedAreas.Add(10); // Firelink m10_02 + the Burg m10_01
        ObjectSounds.Enabled = ds1Sound; // session 11 (snd11), rule SND7: the object TAE sounds need the DS1 projects SoundBanks ships
        Console.WriteLine($"  {Build.Chr.Ds1Balance.Summary()}");   // session 11 (bal11): rules BAL1-BAL3
        var ds1 = new Ds1Source(Program.DefaultDsrRoot);
        var ds2 = ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        var pack = new Pack(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        if (H2Merge.Active) { H2Merge.Save(pack); foreach (var l in H2Merge.Report) pack.Note(l); } // rule H2: the checks re-activate the same merge
        var porter = new WeaponPorter(ds1, ds2, pack, ItemMap.DefaultOut, Path.Combine(AppContext.BaseDirectory, "Mappings", "weapon-templates.tsv"));

        if (allWeapons)
            weapons.AddRange(File.ReadLines(ItemMap.DefaultOut).Skip(1).Select(l => l.Split('\t'))
                .Where(c => c[0] == "EquipParamWeapon" && c[4] == "new").Select(c => int.Parse(c[1]))
                .Where(id => id != FistsId && ds1.Params.Params["EquipParamWeapon"][id] is { } w && Convert.ToInt32(w["weaponCategory"].Value) is not (13 or 14)));
        var ported = new List<WeaponPorter.Result>();
        var failed = new List<string>();
        foreach (int id in weapons.Distinct())
        {
            try
            {
                var r = porter.Port(id);
                ported.Add(r);
                Console.WriteLine($"{r.Ds1Id} {r.Name} → item {r.ItemId}, model {r.ModelId}; {r.Notes}");
            }
            catch (Exception e)
            {
                failed.Add($"{id} {ds1.Text("Weapon_name_", id)}: {e.GetType().Name}: {e.Message}");
                pack.Note($"FAILED weapon {id}: {e}");
            }
        }
        var ammo = new List<AmmoPorter.Result>();
        if (allAmmo)
        {
            var ammoPorter = new AmmoPorter(ds1, ds2, pack, ItemMap.DefaultOut);
            foreach (int id in File.ReadLines(ItemMap.DefaultOut).Skip(1).Select(l => l.Split('\t'))
                         .Where(c => c[0] == "EquipParamWeapon" && c[4] == "new").Select(c => int.Parse(c[1]))
                         .Where(id => AmmoPorter.IsAmmo(ds1.Params.Params["EquipParamWeapon"][id])))
            {
                try
                {
                    var r = ammoPorter.Port(id);
                    ammo.Add(r);
                    Console.WriteLine($"{r.Ds1Id} {r.Name} → item {r.ItemId}; {r.Notes}");
                }
                catch (Exception e)
                {
                    failed.Add($"{id} {ds1.Text("Weapon_name_", id)}: {e.GetType().Name}: {e.Message}");
                    pack.Note($"FAILED ammo {id}: {e}");
                }
            }
        }
        if (allRings)
            foreach (int id in File.ReadLines(ItemMap.DefaultOut).Skip(1).Select(l => l.Split('\t'))
                         .Where(c => c[0] == "EquipParamAccessory" && c[4] == "new").Select(c => int.Parse(c[1])))
                RingPorter.Port(pack, ds1, id);
        Console.WriteLine($"weapons: {ported.Count} ported, ammo: {ammo.Count} ported, {failed.Count} failed");
        foreach (string f in failed)
            Console.WriteLine("  FAILED " + f);

        int entryItem = 0;
        ArmorPorter? npcArmour = null; // session 11 (equip11)
        if (asylum)
        {
            // session 8 (cutscene track): DS1's crow cutscenes as DS2 demos (--cutscenes, with Firelink; default: none);
            // planned first: the maps place the demo objects, the event translator passes the demo ids to 132125
            Build.Cutscenes.Cutscenes.Planned.Clear();
            Build.Cutscenes.Cutscenes.NpcActors = cutsceneNpcs;
            if (cutscenes && firelink) Build.Cutscenes.Cutscenes.Plan();
            else if (cutscenes) Console.WriteLine("  --cutscenes: needs Firelink (the crow scenes end there); built without cutscenes");
            var o = MapBuilder.Asylum with { NoNavmesh = noNavmesh, Ds1Atmosphere = ds1Atmosphere, Ds1Sound = ds1Sound, HitGroups = hitGroups, BloodMessages = bloodMessages,
                Ds1Sky = ds1Sky, Ds1Palette = ds1Palette, AtmosTemplate = atmosTemplates.GetValueOrDefault(MapBuilder.Asylum.Ds2Map),
                DemoActors = Build.Cutscenes.Cutscenes.DemoActors(Program.DefaultDsrRoot, MapBuilder.Asylum.Ds2Map) };
            if (!asylumScript) o = o with { Objects = o.Objects.Where(x => x.Kind is not (ObjPorter.Kind.Prefab or ObjPorter.Kind.Static)).ToArray(), Placements = null, ScriptedDoors = [], HitGroups = false };
            if (!asylumScript) o = o with { Scenery = false }; // track nests: no script → no event-shown scenery (the cliff nest)
            MapBuilder.Build(pack, Program.DefaultDsrRoot, o);
            var firelinkMap = firelink ? MapBuilder.Firelink with { Ds1Atmosphere = ds1Atmosphere, Ds1Sound = ds1Sound, BloodMessages = bloodMessages,
                Ds1Sky = ds1Sky, Ds1Palette = ds1Palette, AtmosTemplate = atmosTemplates.GetValueOrDefault(MapBuilder.Firelink.Ds2Map),
                DemoActors = Build.Cutscenes.Cutscenes.DemoActors(Program.DefaultDsrRoot, MapBuilder.Firelink.Ds2Map) } : null; // default: Firelink Shrine too, reached by the crow (--no-firelink: none)
            Dictionary<int, int>? generators = null; // DS1 character entity → generator (session 8: the event translator's character rule)
            if (enemyTest.Count > 0) asylumEnemies = false; // the TB enemy tests write their own generator params
            // session 8 (track npc): DS1's NPCs on DS2's human-NPC body (c0001 + converted DS1 armour parts, PlayerRig/
            // ArmorPorter/NpcPorter); their generators go into the same generator params as the map's enemies
            var rig = npcs ? new PlayerRig(Program.DefaultDsrRoot, ExtractDs2.DefaultOut) : null;
            var armour = rig != null ? new ArmorPorter(ds1, ds2, pack, rig) : null;
            npcArmour = armour; // session 11 (equip11): --armor reuses this porter (one port per piece, one ACB per set)
            List<NpcPorter.Placed>? asylumNpcs = null;
            if (rig != null && armour != null && enemyTest.Count == 0 && !chrTest && burgChrTest == null)
                asylumNpcs = NpcPorter.Build(pack, ds1, ds2, armour, rig, o, NpcPorter.Asylum, talk);
            if (chrTest || asylumEnemies)
            {
                // DS1's Asylum enemies on DS2 bodies (ChrPorter), one EnemyParam per DS1 variant
                ChrPorter.Options[] chrs = ChrPorter.AsylumChrs; // session 11 (ret11): + the Black Knight
                foreach (var c in chrs) ChrPorter.Build(pack, Program.DefaultDsrRoot, c, ds1);
                // the 1:1 bosses (session 8, track boss step 2): DS1's Asylum Demon (Ds1Boss) at its MSB part c2232_0000 (entity
                // 1810800 → generator 800), DS1's AI translated, a DS2 boss generator (event group 10, logic flag 101020080);
                // DS1's boss events (translated with the map's script below) place it in the air and drop it into the hall
                var bossGen = new List<EnemyTest.Custom>();
                var bossEntities = new Dictionary<int, int>();
                if (boss && !chrTest && burgChrTest == null)
                {
                    var msbA = SoulsFormats.MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", o.Ds1Map + ".msb"));
                    foreach (var b in AsylumEnemies.BossPlacements(msbA, o.Ds1Map))
                    {
                        // chrint stage 1 (enemyfix integration): --only-enemy isolates the bosses too (list 223200 for the demon alone);
                        // a skipped boss is absent for the event translator as with --no-boss (no generator → its events stay untranslated)
                        if (AsylumEnemies.OnlyEnemyParams.Count > 0 && !AsylumEnemies.OnlyEnemyParams.Contains(b.Spec.EnemyParam)) continue;
                        var br = Ds1Boss.Build(pack, Program.DefaultDsrRoot, ds1, b.Spec);
                        // merge9 stage 6: the boss's DS1 sound bank and loose wave banks go through the same sharing check as
                        // the 1:1 regulars' (ChrPorter.AddDs1RigFiles, chrsound rule S10): one FMOD project per bank decade
                        ChrPorter.AddDs1SoundShared(pack, $"{b.Spec.Ds1Chr} 1:1 boss c{b.Spec.AnimId / 100:D4}", br.Files, br.TitleFiles);
                        foreach (var l in br.Report) pack.Note($"boss {b.Spec.N}: {l}");
                        foreach (var l in br.SoundLog) pack.Note($"boss {b.Spec.N} sound: {l}");
                        // session 9 (npcplace NOTES §5, chrint stage 1): rules P1 (DS2 yaw = DS1 yaw + 180) and P2 standing for the
                        // boss generator too: DS1 MSB yaw 180 → DS2 0 (faces the hall entrance; its DS1 drop then lands at z −28.4)
                        var custom = Ds1Placement.Generator(FloorProbe.FromPack(pack, o.Ds2Map), AsylumEnemies.BossCustom(b, msbA, o.Ds1Map));
                        bossGen.Add(custom);
                        bossEntities[b.Entity] = b.Generator;
                        Console.WriteLine($"  boss: {b.Spec.Ds1Chr} 1:1 → {b.Spec.N} / animId {b.Spec.AnimId}, EnemyParam {b.Spec.EnemyParam}, logic {b.Spec.HighLogic}/{b.Spec.LowLogic}, generator {b.Generator} at {custom.Position} yaw {custom.Yaw} (DS1 {b.Yaw}), logic group {custom.LogicGroup}, logic flag {custom.BossLogicFlag} ({br.Files.Count} files)");
                    }
                }
                if (chrTestOnly != 0) // test: the single enemy dies to one hit (the test character's weapon is broken), to check death + drops
                {
                    var testEnemy = pack.Param("EnemyParam")[chrTestOnly];
                    foreach (var cell in testEnemy.Cells.Where(x => x.Def.InternalName.StartsWith("hitPoint")))
                        cell.Value = Convert.ChangeType(1, cell.Value.GetType());
                    // and its first DS1 drop always falls (Ds1Drops writes DS1's items first), so the pickup can be seen
                    var lot = pack.Param("ItemLotParam2_Chr")[Convert.ToInt32(testEnemy["itemLotId"].Value)];
                    if (lot != null) lot["itemWeight1"].Value = 100f;
                }
                // session 11 (burgchr11): --burg-chrs builds the Burg's characters (no placement in the Burg); --burg-chr-test places a
                // selection in the courtyard instead of the Asylum's enemies (generators 9100+, test only)
                List<Build.Maps.BurgChrs.Entry>? burgEntries = burgChrs ? Build.Maps.BurgChrs.Build(pack, ds1, Program.DefaultDsrRoot) : null;
                if (burgChrTest != null && burgEntries != null)
                    EnemyTest.Build(pack, o, [], Build.Maps.BurgChrs.TestPlacements(pack, Program.DefaultDsrRoot, o, burgEntries, burgChrTest));
                else if (!chrTest) // default: DS1's own first-visit enemies at their MSB positions (--no-asylum-enemies: none)
                    generators = AsylumEnemies.Build(pack, Program.DefaultDsrRoot, o, chrs, onlyGroup: null, npcs: asylumNpcs, extra: bossGen, extraEntities: bossEntities); // session 11 (ret11): rule V-ret, every visit (W1 conditions decide)
                else // --chr-test: one row per character, 5 and 2.5 m in front of the bonfire, 2.5 m apart (test only)
                {
                    var nav = SoulsFormats.NGP.Read(pack.GetFile("gamedata", $"map/{o.Ds2Map}/{o.Ds2Map}.ngp"));
                    EnemyTest.Build(pack, o, [], chrs.SelectMany((c, row) => c.Variants.DistinctBy(v => v.TemplateEnemyParam).Select((v, k) =>
                    {
                        float x = o.BonfirePos.X + 1.5f + (k - 1) * 2.5f, z = o.BonfirePos.Z - 5 + row * 2.5f;
                        float y = EnemyTest.NavHeight(nav, x, z, o.BonfirePos.Y) ?? o.BonfirePos.Y;
                        // track enemies step 2: the variant's DS1 AI with the think of its first DS1 placement in the Asylum (any visit)
                        var logic = Build.Ai.Ds1Ai.DefaultThink(Program.DefaultDsrRoot, o.Ds1Map, v.Ds1Npc) is int th && (chrTestOnly == 0 || v.EnemyParamId == chrTestOnly)
                            ? Build.Ai.Ds1Ai.Logic(pack, Program.DefaultDsrRoot, o.Ds1Map, v.EnemyParamId, th) : null;
                        return new EnemyTest.Custom(1600, 9000 + row * 10 + k, /* test ids: not the default placements' (a save remembers kills per generator) */ v.EnemyParamId, logic?.High ?? v.HighLogic, logic?.Low ?? v.LowLogic, new System.Numerics.Vector3(x, y + 0.1f, z), 0);
                    })).Where(c => chrTestOnly == 0 || c.EnemyId == chrTestOnly).ToList());
                }
            }
            else if (enemyTest.Count > 0) // --enemy-test [GEN+GEN...]: Things Betwixt enemies in the courtyard, in a row 5 m in front of the bonfire, 3 m apart (test only)
            {
                var nav = SoulsFormats.NGP.Read(pack.GetFile("gamedata", $"map/{o.Ds2Map}/{o.Ds2Map}.ngp"));
                var placements = enemyTest.Select((gen, k) =>
                {
                    float x = o.BonfirePos.X + (k - (enemyTest.Count - 1) / 2f) * 3, z = o.BonfirePos.Z - 5;
                    float y = EnemyTest.NavHeight(nav, x, z, o.BonfirePos.Y)
                        ?? (nav.Meshes.Count == 0 ? o.BonfirePos.Y : throw new InvalidOperationException($"enemy test spot ({x}, {z}) is off the navmesh"));
                    return (gen, new System.Numerics.Vector3(x, y + 0.1f, z), 0f);
                }).ToList();
                EnemyTest.Build(pack, o, placements);
            }
            else if (asylumNpcs is { Count: > 0 }) // --no-asylum-enemies: the NPCs alone
            {
                EnemyTest.Build(pack, o, [], asylumNpcs.Select(n => n.Custom).ToList());
                generators = asylumNpcs.ToDictionary(n => n.Entity, n => n.Custom.NewGenerator);
            }
            // the event script after the enemies (session 8): translated DS1 events address characters by their generators
            // DS1 BGM switches (2010:3), event SEs (2010:2) and one-shot SFX (2006:3) need the regions the atmosphere placed
            // (DS1 sound on and DS1 atmosphere: the regions are placed with it); same plan both sides (MapSounds)
            IReadOnlyDictionary<int, int>? sounds = ds1Sound && ds1Atmosphere
                ? Build.Events.EntityRules.SoundId(SoulsFormats.MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", o.Ds1Map + ".msb")),
                    SoulsFormats.EMEVD.Read(Path.Combine(Program.DefaultDsrRoot, "event", o.Ds1Map + ".emevd.dcx")), o.Ds1Map)
                : null;
            var asylumTranslated = AsylumEvents.Build(pack, o, eventTest, firelinkMap, asylumScript ? ds1 : null, generators, sounds); // --event-test: a message window on arrival (test only)
            // session 8 (track npc, step 2): the NPCs' DS1 talk, after the event script (its EventParam file gets the talk rows)
            if (talk && asylumNpcs is { Count: > 0 })
                foreach (string line in Build.Talk.TalkBuilder.Build(pack, ds1, o, asylumNpcs, asylumTranslated, ds1Voices)) Console.WriteLine("  " + line);
            // The entry warps to DS1's own start point (the cell). Default (user's choice, session 5): the "Echo of Lordran"
            // item, an infinite Homeward Bone whose effect warps to the cell (EntryItem; no Things Betwixt edits).
            // --entry fireplace: "Peer into the fire?" in the old women's house (edits Things Betwixt's event files);
            // --entry tree / --giant-tree: the Giant's Tree (on hold: Things Betwixt reloaded every 32 s with it).
            var start = SoulsFormats.MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", o.Ds1Map + ".msb")).Parts.Players[0];
            switch (entry)
            {
                case "tree": GiantTree.Build(pack, o, start.Position, start.Rotation); break;
                case "fireplace": FireplaceEntry.Build(pack, o, start.Position, start.Rotation); break;
                default: EntryItem.Build(pack, ds1, o, start.Position, start.Rotation); entryItem = EntryItem.ItemId; break;
            }
            if (firelinkMap != null)
            {
                MapChests.Prepare(pack, Program.DefaultDsrRoot, firelinkMap); // session 11 (fx11), rule CH2: the chest model + its sound before the map's banks
                MapBuilder.Build(pack, Program.DefaultDsrRoot, firelinkMap);
                foreach (var l in MapChests.Build(pack, ds1, firelinkMap)) Console.WriteLine("  " + l); // session 11 (fx11), rule CH1b: Firelink's four chests
                AsylumEvents.Build(pack, firelinkMap, false); // its bonfire's flag-reset event
                var firelinkNpcs = rig != null && armour != null ? NpcPorter.Build(pack, ds1, ds2, armour, rig, firelinkMap, NpcPorter.Firelink, talk) : null;
                if (asylumEnemies) // Firelink's DS1 enemies: the graveyard skeletons, the Undead Soldier
                {
                    ChrPorter.Build(pack, Program.DefaultDsrRoot, ChrPorter.Skeleton, ds1);
                    if (pack.Param("EnemyParam")[ChrPorter.HollowWarrior.Variants[0].EnemyParamId] == null) ChrPorter.Build(pack, Program.DefaultDsrRoot, ChrPorter.HollowWarrior, ds1);   // session 11 (burgchr11): --burg-chrs may have built it already
                    AsylumEnemies.Build(pack, Program.DefaultDsrRoot, firelinkMap, [ChrPorter.Skeleton, ChrPorter.UndeadSoldier, ChrPorter.HollowWarrior], onlyGroup: null, npcs: firelinkNpcs);
                }
                else if (firelinkNpcs is { Count: > 0 })
                    EnemyTest.Build(pack, firelinkMap, [], firelinkNpcs.Select(n => n.Custom).ToList());
                // Firelink's DS1 events aren't translated yet (AsylumEvents wrote only its bonfire event): every DS1 event
                // counts as not running for the gift-chain rule
                if (talk && firelinkNpcs is { Count: > 0 })
                    foreach (string line in Build.Talk.TalkBuilder.Build(pack, ds1, firelinkMap, firelinkNpcs, null, ds1Voices)) Console.WriteLine("  " + line);
                MapLinks.Build(pack, o, firelinkMap);          // after EntryItem: adds to the Asylum's EventLocation
                FirelinkScript.Build(pack, ds1, firelinkMap);  // session 9 (track firelink): DS1's Firelink events, translated (after its characters and MapLinks)
                // session 11 (burg11, BURG-PLAN M1): the MapHosts rows of --map burg (the Undead Burg + Parish, DS1 m10_01) as their
                // hosts, geometry + bonfires (rule BF1), joined on foot to every built neighbour (MapSeams: rules CC1 / SB1)
                var seamMaps = new List<MapBuilder.Options> { firelinkMap };
                foreach (var row in burg ? MapHosts.Rows.Where(r => hostOptions.Contains(r.Option) && !H2Merge.IsSecondary(r.Ds1Map)) : [])
                {
                    Build.Ai.Ds1Ai.CurrentMap = row.Host; // session 11 (burg11, round 7): rule DL1
                    // session 11 (burg11, BURG-PLAN M2): objects by rule (MapObjects), pickups, the NPCs present at the start
                    // (Mappings\ds1-npcs.tsv) with their talk, the DS1 events translated (Ds1MapContext)
                    var built = new HashSet<string> { o.Ds1Map, firelinkMap.Ds1Map };
                    foreach (var r2 in MapHosts.Rows.Where(r => hostOptions.Contains(r.Option))) built.Add(r2.Ds1Map);
                    var burgMap = MapBuilder.ForDs1(row, Program.DefaultDsrRoot, built) with { Ds1Atmosphere = ds1Atmosphere, Ds1Sound = ds1Sound,
                        Ds1Sky = ds1Sky, Ds1Palette = ds1Palette, NoNavmesh = noNavmesh };
                    burgMap = burgMap with { AtmosTemplate = atmosTemplates.GetValueOrDefault(burgMap.Ds2Map) };
                    MapChests.Prepare(pack, Program.DefaultDsrRoot, burgMap); // session 11 (fx11), rule CH2: the chest model + its sound before the map's banks
                    MapBuilder.Build(pack, Program.DefaultDsrRoot, burgMap);
                    var plan = MapObjects.For(Program.DefaultDsrRoot, row.Ds1Map, row.Host, built);
                    MapChests.Build(pack, ds1, burgMap, plan); // rule CH1 (session 11, M2 leftovers)
                    var burgNpcs = rig != null && armour != null ? NpcPorter.Build(pack, ds1, ds2, armour, rig, burgMap, NpcPorter.Table(row.Ds1Map), talk) : null;
                    Dictionary<int, int>? burgGenerators = null;
                    // session 11 (burg11, BURG-PLAN M3/M4): with --burg-chrs every DS1 character of the map is placed by rule
                    // (MapEnemies: regulars, the boss pipeline's parts, the NPC table's NPCs in the same generator params)
                    if (burgChrs && burgChrTest == null)
                        burgGenerators = MapEnemies.Build(pack, ds1, burgMap, burgNpcs);
                    else if (burgNpcs is { Count: > 0 })
                    {
                        EnemyTest.Build(pack, burgMap, [], burgNpcs.Select(n => n.Custom).ToList());
                        burgGenerators = burgNpcs.ToDictionary(n => n.Entity, n => n.Custom.NewGenerator);
                    }
                    MapScript.Build(pack, burgMap, MapBuilder.Bonfires(Program.DefaultDsrRoot, burgMap));
                    var burgTranslated = MapScript.TranslateEvents(pack, ds1, burgMap, plan, burgGenerators);
                    // talk: the area's speech project already serves Firelink: rule SND4v (session 11, TalkVoices) gives the
                    // Burg its own renamed copy of the project
                    if (talk && burgNpcs is { Count: > 0 })
                        foreach (string line in Build.Talk.TalkBuilder.Build(pack, ds1, burgMap, burgNpcs, burgTranslated, ds1Voices)) Console.WriteLine("  " + line);
                    if (burgNpcs is { Count: > 0 }) MapScript.NpcSpots(pack, burgMap, burgNpcs);
                    MapNet.Build(pack, burgMap); // rule NET2 (session 11 burg11): play areas + DS1 invasion points
                    StandIns.Write(pack, plan);
                    seamMaps.Add(burgMap);
                }
                Build.Ai.Ds1Ai.CurrentMap = null;
                // the seams between every built map (session 11, round 7: once, after all generic hosts; with --map burg alone the
                // same call as before: [Firelink, Burg])
                if (seamMaps.Count > 1)
                    foreach (var line in MapSeams.Build(pack, Program.DefaultDsrRoot, seamMaps)) Console.WriteLine("  " + line);
            }
            armour?.Flush(); // the NPCs' converted DS1 armour parts (one ACB per set, written once every part is known)
            if (Build.Cutscenes.Cutscenes.Planned.Count > 0)
                Build.Cutscenes.Cutscenes.Build(pack, Program.DefaultDsrRoot); // the crow model, scnbnds, DemoParam rows (after the maps' NPCs: R3b reads their GeneratorRegistParam rows)
            // the Travel menu's Lordran / Drangleic pages (loader travel.c): LB/RB in the key guide, page names for the title
            if (travelPages)
                Console.WriteLine(TravelPages.Build(pack, ds2.Params["MapObjectBonfireParam"]));
        }

        // session 11 (track equip11, PLAN milestone 7): DS1-only armour, spells and goods as DS2 items (the test shop sells them)
        var equipItems = EquipBatch(pack, ds1, ds2, npcArmour, armorIds, spellIds, goodsIds);

        // session 11 (track gest11): DS1's gestures (rules GS1-GS4): additive c0001 / c000100 content, texts, icons, the loader's table
        if (Build.Chr.Ds1Gestures.Enabled)
        {
            var gr = Build.Chr.Ds1Gestures.Ensure(pack, ds1);
            Console.WriteLine($"  DS1 gestures: {Build.Chr.Ds1Gestures.All.Length} on the Lordran gesture page (categories {Build.Chr.Ds1Gestures.CategoryBase}..{Build.Chr.Ds1Gestures.Category(Build.Chr.Ds1Gestures.All[^1])}); {gr.Report.First(l => l.StartsWith("verify: ADDITIVE") || l.StartsWith("verify: ") && l.Contains("PROBLEMS"))}{(Build.Chr.Ds1Gestures.TestUnlockAll ? "; TEST: all shown as learned" : "")}");
        }

        // session 10 (asy-events fix pass e2): rule K6 key source, once every map, event script, character and talk is in
        // the pack and before the TEST shop (never a source): a key lock whose key the build gives nowhere is unlocked
        foreach (var line in Build.Events.KeySources.Apply(pack)) { Console.WriteLine("  " + line); pack.Note(line); }

        // session 11 (devkit11), TEST ONLY (--dev-kit): the kit + lots of every item the pack added (after K6: never a key source)
        if (Build.DevKit.Enabled) Console.WriteLine("  " + Build.DevKit.Build(pack, ds2));

        // session 9 (mist track): DS1 map effects converted 1:1 (rule B2b, --map-sfx ds1) go into DS2's global effect bank
        // (title:/sfx/sfx9999_Append.ffxbnd.dcx + res_Append); nothing is written when none was converted
        {
            var fxLog = new List<string>();
            if (Build.Maps.FfxPorter.WriteBanks(pack, Program.DefaultDsrRoot, fxLog) > 0)
                foreach (var l in fxLog) { Console.WriteLine("  " + l); pack.Note(l); }
        }

        if (testShop || shopItems.Count > 0)
        {
            // Majula merchants available on a fresh save. An NPC's rows are one contiguous block (Maughlin 76100000..76100266,
            // Shalquoir 77700200..77700607 in vanilla); test rows go right after the block's last row, copied from a row
            // that needs no event flag, and are made always-available and unlimited.
            var shops = new (string Npc, int Template)[] { ("Maughlin the Armourer", 76100000), ("Sweet Shalquoir", 77700400) };
            var shop = pack.Param("ShopLineupParam");
            // session 10 (asy-items i2 fix pass): items the build ported (e.g. RingPorter's 43M rings) count too
            var missing = shopItems.Where(id => pack.Param("ItemParam")[id] == null).ToList();
            if (missing.Count > 0) { Console.Error.WriteLine($"not in the pack's ItemParam: {string.Join(", ", missing)}"); return 1; }
            var stock = (testShop ? ported.Select(p => (p.ItemId, p.Name)).Concat(ammo.Select(a => (a.ItemId, a.Name))).Concat(equipItems) : [])
                .Concat(shopItems.Select(id => (ItemId: id, Name: ds2.RowName("ItemParam", id) is { Length: > 0 } n ? n
                    : pack.Text("itemname").Entries.FirstOrDefault(e => e.ID == id)?.Text is { Length: > 0 } pn ? pn : $"item {id}")))
                .ToList();
            if (testShop && entryItem != 0) stock.Add((entryItem, EntryItem.Name)); // fallback if the loader can't give it
            // Test lots (autopilot "givelot 66990000+i"): one per test-shop item, to hand items over without a shop trip.
            var lots = pack.Param("ItemLotParam2_Other");
            for (int i = 0; i < stock.Count; i++)
                if (lots[TestLotBase + i] == null)
                {
                    var lot = pack.AddRow("ItemLotParam2_Other", lots[60007200]!, TestLotBase + i, $"[Lordran test] {stock[i].Name}");
                    lot["itemId1"].Value = (uint)stock[i].ItemId;
                    lot["itemNum1"].Value = (byte)1;
                }
            Console.WriteLine($"test lots: {TestLotBase}..{TestLotBase + stock.Count - 1} (autopilot givelot)");
            foreach (var (npc, templateId) in shops)
            {
                int block = templateId / 100000 * 100000;
                int next = shop.Rows.Where(r => r.ID >= block && r.ID < block + 100000).Max(r => r.ID) + 1;
                for (int i = 0; i < stock.Count; i++)
                {
                    var row = pack.AddRow("ShopLineupParam", shop[templateId], next + i, $"[Lordran test] {stock[i].Name}");
                    row["saleItem"].Value = stock[i].ItemId;
                    // The game charges ItemParam.price × priceRate; ShopLineupParam.price is not the price (vanilla rows
                    // hold 10 there). 1.2 instead of 1.0 so the product lands on 1 whether the game truncates or rounds.
                    int itemPrice = Convert.ToInt32(pack.Param("ItemParam")[stock[i].ItemId]["price"].Value);
                    row["priceRate"].Value = itemPrice > 1 ? 1.2f / itemPrice : 1f;
                    row["saleFlag"].Value = -1;
                    row["saleEndFlag"].Value = -1;
                    row["unitSales"].Value = (sbyte)-1;
                    Console.WriteLine($"test shop: {stock[i].Name} ({stock[i].ItemId}) at {npc} (Majula) for 1 soul, row {next + i}");
                }
            }
        }

        // Loading screens (user request, session 4): DS2 picks the item it shows (icon, name, description) from
        // LoadingDisplayItemParam (row id = ItemParam id, globalFlagId 0 = always; 205 vanilla rows use 0, the rest use
        // progress flags 100100/100804). Session 11 (burg11) rule LD1: DS1's rows wait for the departure from DS1's start map.
        // Session 12 (load12) rule LD2: every item the pack adds (armour, rings, spells, goods too; was weapons + ammo only).
        Console.WriteLine(Build.LoadingScreens.AddItems(pack, ds2, Program.DefaultDsrRoot));
        // TEST (LR_STEP_UP=height): enable DS2's unused player step-up (ChrMoveParam 100 stepUpFeasible* are 0 in vanilla:
        // DS2 stairs are collision ramps; DS1's 0.24 m stair risers block the player, session 7). Not in the default pack.
        if (float.TryParse(Environment.GetEnvironmentVariable("LR_STEP_UP"), System.Globalization.CultureInfo.InvariantCulture, out float stepUp))
        {
            var mv = pack.Param("ChrMoveParam")[100]!;
            mv["stepUpFeasibleHeightMax"].Value = stepUp;
            mv["stepUpFeasibleHeightMin"].Value = 0.05f;
            mv["stepUpFeasibleAngleMax"].Value = Convert.ChangeType(60, mv["stepUpFeasibleAngleMax"].Value.GetType());
            mv["stepUpLightAnimSpeedScale"].Value = 1f;
            mv["stepUpHeavyAnimSpeedScale"].Value = 1f;
            pack.Note($"TEST: player step-up enabled up to {stepUp} m (ChrMoveParam 100)");
            Console.WriteLine($"TEST: player step-up {stepUp} m");
        }

        if (title)
        {
            Console.WriteLine(TitleScreen.AddSubtitleLine(pack, "ECHOES OF LORDRAN"));
            // session 12 (load12, user: "add EOL below DARKSOULSII in the loading screen bottom corner"), rule LD3
            Console.WriteLine(Build.LoadingScreens.AddLogoLine(pack, Build.LoadingScreens.LogoLine));
        }
        if (mapPreview)
            MapPreview.Build(pack, Program.DefaultDsrRoot, MapPreview.AsylumOnMajula);
        // Guard (chrint stage-2 fix pass, rule I1b pack-wide): nmb-ids's pack check on the pack's anibnds + the vanilla ones
        // they don't replace: no network-def id shared, no new content under an id another anibnd uses (other tracks' clones
        // included). The 1:1 anibnds draw through one registry (NmbIdentity.For), so this is the backstop.
        {
            var anibnds = pack.Files("gamedata", "morpheme4/chr/").Where(f => f.Path.EndsWith(".anibnd.dcx", StringComparison.OrdinalIgnoreCase)
                && !f.Path.EndsWith(".extanibnd.dcx", StringComparison.OrdinalIgnoreCase)).Select(f => (Path.GetFileName(f.Path), f.Bytes)).ToList();
            if (anibnds.Count > 0)
            {
                var idLog = new List<string>();
                int idProblems = NmbIdsCmd.CheckPackFiles(anibnds, Path.Combine(pack.Ds2Data, "morpheme4", "chr"), idLog.Add);
                foreach (var l in idLog) pack.Note($"nmb-ids: {l}");
                Console.WriteLine($"  nmb-ids (rule I1b, {anibnds.Count} pack anibnds): {idLog[^1]}");
                if (idProblems > 0)
                {
                    foreach (var l in idLog) Console.Error.WriteLine("  " + l);
                    Console.Error.WriteLine($"refusing to write the pack: {idProblems} Morpheme asset-id problems (rule I1b)");
                    return 1;
                }
            }
        }
        // Guard: every vanilla bonfire row must be byte-identical to DS2's (see the summary).
        var vanillaBonfires = ds2.Params["MapObjectBonfireParam"];
        var packBonfires = pack.Param("MapObjectBonfireParam");
        foreach (var v in vanillaBonfires.Rows)
        {
            var p = packBonfires[v.ID];
            if (p == null || !v.Cells.Select(c => c.Value).SequenceEqual(p.Cells.Select(c => c.Value)))
            {
                Console.Error.WriteLine($"refusing to write the pack: vanilla bonfire row {v.ID} was changed or removed");
                return 1;
            }
        }

        // Guard I1d-G (track npcpose2): with the default NPC route (rule I1d, own networks) the pack must not ship the player's
        // network or TAEs: they are global (the player, every animId-100 human NPC, every Seamless peer loads them).
        if (Build.Chr.NpcIdle.Route == Build.Chr.NpcIdle.IdleRoute.Template)
        {
            var shared = new[] { "morpheme4/chr/c0001.anibnd.dcx", "timeact/chr/c000100_pl.tae", "timeact/chr/c000100_sfx.tae", "timeact/chr/c000100_snd.tae" }
                .Where(p => pack.TryGetFile("gamedata", p) != null).ToList();
            // session 11 (gest11, rule GS1): the one exception is the DS1 gestures' build, whose files were proved purely additive
            // (Ds1Gestures.VerifyAdditive: every vanilla clip, node, category, bundle and TAE animation byte-identical); the pack
            // must hold exactly those bytes (nothing else edited them afterwards)
            var gest = Build.Chr.Ds1Gestures.Of(pack);
            var foreign = shared.Where(p => gest == null || !gest.Files.TryGetValue(p, out var b) || !b.AsSpan().SequenceEqual(pack.TryGetFile("gamedata", p))).ToList();
            if (foreign.Count > 0)
            {
                Console.Error.WriteLine($"refusing to write the pack (guard I1d-G): the default NPC route ships no shared human file but the DS1 gestures' verified additive ones, and the pack holds {string.Join(", ", foreign)}");
                return 1;
            }
            pack.Note(shared.Count == 0 ? "guard I1d-G: no c0001.anibnd / c000100 TAEs in the pack (rule I1d)"
                : $"guard I1d-G: {string.Join(", ", shared)} = the DS1 gestures' verified additive build (rule GS1)");
        }

        // A running DS2 serves files from the pack (and a test run may have a test save swapped in): never rewrite it then.
        // An agent sandbox pack (data\sandbox\NAME\...pack, Pack.IsSandboxPack; session 8) is never read by the game, so it
        // may be built meanwhile.
        if (!Pack.IsSandboxPack(outDir) && System.Diagnostics.Process.GetProcessesByName("DarkSoulsII").Length > 0)
        {
            Console.Error.WriteLine("refusing to write the pack while DarkSoulsII is running (end the test run with its stop file first)");
            return 1;
        }
        // session 11 (bal11): balance-check's manifest (every EnemyParam calibrated from a DS1 NpcParam)
        if (!Build.Chr.Ds1Balance.Manifest.IsEmpty) pack.AddFile("lordran-chr", Build.Chr.Ds1Balance.ManifestName, Build.Chr.Ds1Balance.ManifestBytes());
        if (DebugMarkers.Enabled) foreach (var l in DebugMarkers.Apply(pack)) Console.WriteLine("  " + l);
        pack.Write(outDir);
        return 0;
    }

    /// <summary>
    /// Session 11 (track equip11, PLAN milestone 7): --armor / --spells / --goods (all or ID+ID). Armour reuses the NPC
    /// outfits' ArmorPorter when the maps made one (one port per piece, one ACB per set); ArmorItems.Finish calibrates every
    /// DS1 armour item in the pack (rules AR2-AR7), NPC pieces included, also without --armor. Returns the items made here
    /// (for the test shop). A piece that fails is reported and skipped, like a weapon.
    /// </summary>
    static List<(int ItemId, string Name)> EquipBatch(Pack pack, Ds1Source ds1, ParamStore ds2, ArmorPorter? npcArmour, List<int>? armorIds, List<int>? spellIds, List<int>? goodsIds)
    {
        var made = new List<(int, string)>();
        var failed = new List<string>();
        if (armorIds is { Count: > 0 })
        {
            var armour = npcArmour ?? new ArmorPorter(ds1, ds2, pack, new PlayerRig(Program.DefaultDsrRoot, ExtractDs2.DefaultOut));
            var (done, f) = ArmorItems.PortAll(pack, ds1, armour, armorIds);
            armour.Flush();
            failed.AddRange(f);
            foreach (var d in done.Where(d => d.How == "ported")) made.Add((d.Item, ds1.Text("Armor_name_", d.Ds1)));
            Console.WriteLine($"armour: {done.Count(d => d.How == "ported")} ported, {done.Count(d => d.How != "ported")} DS2 duplicates, {f.Count} failed");
        }
        foreach (string line in ArmorItems.Finish(pack, ds1, ds2))
            Console.WriteLine("  " + line);
        if (spellIds is { Count: > 0 })
        {
            var porter = new SpellPorter(ds1, ds2, pack);
            int n = 0;
            foreach (int id in spellIds.Distinct())
            {
                if (porter.NotPorted(id, out int dup) is string why)
                {
                    Console.WriteLine($"  spell {id} {ds1.Text("Magic_name_", id)}: not ported: {why}");
                    pack.Note($"spell {id}: not ported: {why}");
                    continue;
                }
                try
                {
                    var r = porter.Port(id);
                    made.Add((r.ItemId, r.Name));
                    n++;
                    Console.WriteLine($"  spell {r.Ds1Id} {r.Name} → item {r.ItemId}: {r.Notes}");
                }
                catch (Exception e)
                {
                    failed.Add($"spell {id} {ds1.Text("Magic_name_", id)}: {e.GetType().Name}: {e.Message}");
                    pack.Note($"FAILED spell {id}: {e}");
                }
            }
            Console.WriteLine($"spells: {n} ported");
        }
        if (goodsIds is { Count: > 0 })
        {
            int n = 0;
            foreach (int id in goodsIds.Distinct())
            {
                try
                {
                    var (item, how) = GoodsPorter.PortGoods(pack, ds1, id);
                    if (item == GoodsPorter.Id(id)) { made.Add((item, ds1.Text("Item_name_", id))); n++; }
                    Console.WriteLine($"  goods {id} {ds1.Text("Item_name_", id)} → item {item}: {how}");
                }
                catch (Exception e)
                {
                    failed.Add($"goods {id} {ds1.Text("Item_name_", id)}: {e.GetType().Name}: {e.Message}");
                    pack.Note($"FAILED goods {id}: {e}");
                }
            }
            Console.WriteLine($"goods: {n} ported");
        }
        foreach (string f in failed) Console.WriteLine("  FAILED " + f);
        return made;
    }
}
