# Lordran in Drangleic — DS1 rebuilt inside DS2 SOTFS, as a Seamless Co-op extension

**Scope (decided 2026-10-02):** all of Dark Souls 1 (from the user's DSR install) runs inside the DS2 SOTFS engine, alongside all of DS2. That means every map, enemy, boss, NPC, item, drop, weapon, armor piece, ring, and spell, all playable in co-op through Seamless Co-op (DS2SC 0.0.4). DS3 is out of scope.

**Design rule: DS1 content, DS2 rules.** Menus, HUD, stats, leveling, upgrades, infusions, healing, hollowing, and online all come from DS2. DS1 content is translated into those systems. For example, DS1 weapons upgrade through DS2's blacksmith menus, use DS2's 9 stats, and go through DS2's infusion list. If DS2 code can't express a DS1 behavior, we add engine code in `lordran.dll`.

**One game, not two.** One character, one save, one inventory, one level. DS2's bonfire travel menu lists Lordran's bonfires next to Drangleic's. DS1 bosses, NPCs, and quests progress in the same world state as DS2's.

**How players reach Lordran (decided by the user 2026-10-02):** in **Things Betwixt**, past the three witches (Fire Keeper crones) near the first bonfire, a **Giant's Tree / memory** works like DS2's Memories of the Giants and sends the player "into the past". They wake in the **Northern Undead Asylum cell**, at the start of DS1. From then on, the **bonfire warp menu has a Drangleic / Lordran switch**. Engine work: reuse the Memory-of-the-Giants object and its warp event in Things Betwixt (`m10_02`), and change the warp menu (DS2 menus show pre-rendered area-name/bonfire icons).

## Architecture

```
ds2sc_launcher.exe ──launches──▶ DarkSoulsII.exe (unmodified, archives untouched — no UXM)
                                   ├─ SeamlessCoop\ds2sc.dll   (Yui's mod: untouched, closed source, black box)
                                   ├─ dinput8.dll              (our proxy → loads lordran.dll)
                                   │    └─ lordran.dll         (file redirection + our engine code)
                                   └─ lordran\                 (content pack generated from the player's own DSR install;
                                                                 lordran.dll makes DS2 look here before its archives)
```

**No UXM (decided 2026-10-02).** DS2's install stays pristine. We read DS2's data with our own extractor (`extract-ds2`). `lordran.dll` hooks DS2's virtual file system (see `docs/ds2-re-notes.md`) so files in `lordran\` override archive files, the way Mod Engine works for later games. Uninstalling means removing `dinput8.dll`. Steam updates and verification can't break it, and co-op partners don't need to unpack 20 GB.

- **The content pack is built, not shipped.** Every co-op player needs the same DS1 content. Redistributing FromSoftware's DSR assets isn't OK, so the product is a converter. It reads each player's own DSR install and writes the DS2 content pack, which gives everyone identical output from the same converter version.
- **Seamless Co-op stays untouched.** We don't hook the same functions it does, we respect its `.co2` saves, and we avoid the param IDs it injects (found by comparing memory with and without it loaded).

## Converters (the core of the project)

Formats come from `docs/survey-dsr.txt` (DSR side, measured) and UXM's DS2 file dictionary (DS2 side, until the unpack lands).

| Converter | DSR in → DS2 out | Notes / difficulty |
|---|---|---|
| Models | FLVER 0x2000C (maps, chr, obj, parts) → DS2 FLVER | **Map pieces done (session 3, `Build/Maps/MapPieceConverter.cs`)**: DS1 MTD → a DS2 map MTD from a catalogue of DS2's own usage; DS1 lightmap → DS2 GI texture; BC5/BC7 → DXT5nm/DXT1/DXT5. Renders in game. Chr/obj/parts next |
| Collision | Havok **2015 tagfile** → Havok **2014 packfile** | **Done (session 3, `Build/Maps/CollisionConverter.cs`)**: same Havok classes on both sides (MOPP + FromSoft extended mesh), so the MOPP code is copied verbatim. Own tagfile reader and reflection-driven packfile writer in `Formats/Havok/`. Walls block in game |
| Navmesh | NVM + MCG/MCP → **NGP** | SoulsFormats reads/writes both sides |
| Skeletons + animations | Havok 2015 skeletons/anims → **NaturalMotion Morpheme 4** (NSA anims + NMB networks) | **Hardest piece.** SoulsFormats only *reads* NSA, so we write an NSA encoder plus a Morpheme network per character. Armor is re-skinned to DS2's player skeleton |
| TimeAct (TAE) | DS1 TAE → SOTFS TAE | SoulsFormats does both. Smithbox ships event templates for both games → event-type mapping table |
| AI | DS1 Lua (compiled luabnd) → **DS2 ESD state machines** (`ezstate/ai*.esd`) | A translation, not a port. Decompile DS1 Lua, then generate ESD |
| Params | 41 DS1 tables → DS2 tables (198 paramdefs in Smithbox) | Weapons, armor, rings, spells, NPCs, attacks, effects, drops, shops; stats/scaling rebalanced to DS2 |
| Text + icons | FMG + menu textures → DS2 FMG + icon sheets | Shown in DS2's menus |
| Sound | FMOD Designer (289 .fev/.fsb) → same tech | DS2 loads one .fev project; lordran.dll loads DS1's projects through the FMOD Ex API |
| Events | DS1 EMEVD → **DS2 ESD** (`ezstate/event_mXX.esd`) | Doors, levers, elevators, boss fog, pickups, quests |
| Maps | MSB1 → MSB2 + DS2 per-map params | DS2 places enemies through per-map generator params, not the MSB. **Standalone map builder (session 3, `Build/Maps/MapBuilder.cs`)** writes every per-map file DS2 opens; m18_01_00_00 is built, not yet loaded in game |

## Milestones

| # | Milestone | Done when |
|---|---|---|
| 0 | Setup | Workspace, save backups, tools, DRM-stripped exe, Seamless installed, Ghidra on DS2 (**done**) |
| 1 | Loader + compatibility gate | `lordran.dll` redirects one changed file (e.g. an item name) from `lordran\`, and it works together with Seamless in a co-op session. **Mostly done 2026-10-02:** loader v2.3 serves the regulation (CWC tag fixed), FMGs, and models, and boots under `ds2sc_launcher.exe`. Still needed: an in-game check and a real co-op session |
| 2 | Formats survey | DSR vs DS2 versions of every format above documented, with a sample of each read and written |
| 3 | One weapon | A DS1 weapon (e.g. Balder Side Sword) in DS2: model, stats, icon, text, upgradeable at a DS2 blacksmith. **Built 2026-10-02** (`lr.cmd build-pack --weapons 204000 --test-shop`); **In game 2026-10-02:** bought for 1 soul (Maughlin/Shalquoir test shop) and equipped in hand. The DS1 model, icon, and text load from the pack (loader v2.4). Left to check: upgrading at McDuff |
| 4 | Undead Asylum + entry | The Things Betwixt Giant's Tree warps to the Asylum cell. The Asylum (m18_01) is walkable with collision, and its bonfire appears under "Lordran" in the warp menu. (Changed from Firelink-first by the user's entry decision.) **Session 3:** geometry and collision work in game (overlaid on Majula). **Session 4:** m18_01_00_00 loads as its own DLC-style map (WorldMapList registration, own bonfire 48150 in Travel, working bonfire); the warp mechanism into a map is known (`132125`). Open: editing Things Betwixt broke its events (HANDOFF FIRST), so the temporary entry ("Peer into the fire?" at the witches' hut fireplace) isn't working yet; the Giant's Tree is on hold |
| 5 | One enemy | A DS1 Hollow in the Asylum: animations, hitboxes, AI, drops, death |
| 6 | Asylum in co-op | Two players through Seamless: sync, enemy, tree entry, bonfire travel |
| 7 | Batch: equipment | All weapons, shields, armor, rings, spells, consumables. **Weapons/shields/catalysts: 99/99 ported 2026-10-02** (`build-pack --weapons all`); ammo, armor, rings, spells, and goods to go. Ammo: 8/8 (6 shared). Weapon specials layer 2: 10 weapons get DS1 passive/on-hit effects through DS2's SpEffectWeapon scripts. DS1 specials are layered (params now, animations with the encoder) |
| 8 | Batch: maps | All 18 map blocks, connected as in DS1 |
| 9 | Batch: enemies + bosses | All enemy types, then bosses (fog walls, HP bars, phases) |
| 10 | NPCs + quests | Merchants, blacksmiths, covenant NPCs, quest lines, dialogue/voice |
| 11 | Full playthrough | Asylum → Kiln (+ Artorias DLC) in co-op |

## Equipment conversion (findings 2026-10-02, via `lr.cmd dump-param`)
- DS1 keeps a weapon in one row (`EquipParamWeapon`, e.g. 204000 Balder Side Sword: model 202, icon 10, 80 phys, STR 10/DEX 14, scaling 13/85). DS2 splits it into a chain: `ItemParam` (icon, price, sort) → `WeaponParam` (model, requirements, weight, durability) → `WeaponReinforceParam` (min/max attack per element, +10 cap, stats-affect curve, upgrade cost), `WeaponActionCategoryParam` (moveset), and `WeaponTypeParam`.
- **Balance by calibration.** At least 59 weapons exist in both games (more once shields, bows, and catalysts are matched). Per weapon class, a DS1 value is scaled by the ratio of DS2/DS1 values for shared weapons (e.g. Longsword 80 → 112 phys), so converted items land where DS2's balance would put them.
- **Duplicates → DS2's version (decided 2026-10-02).** When a DS1 item also exists in DS2 (Longsword, Zweihander, Moonlight Greatsword…), Lordran's drops, shops, and starting gear use DS2's existing item. Only DS1-only items are added as new DS2 items, with DS1 models, converted stats, DS1 names and descriptions, and icons. The converter keeps a DS1 ID → DS2 ID map covering both cases.

### Item map (`lr.cmd item-map` → `data\lordran\item-map.tsv`)
Every DS1 base item gets exactly one outcome:

| Outcome | Count | Meaning |
|---|---|---|
| shared | 233 | Exists in DS2 under the same name and kind → use DS2's item |
| new | 418 | DS1-only: 114 weapons/shields, 197 armor, 29 rings, 23 spells, 55 goods (boss souls, Lordran keys, quest items) |
| via-spell | 72 | Goods entries that are the inventory side of spells → follow the spell mapping |
| system-map / -new / -drop | 29 / 3 / 5 | DS1 mechanics translated to DS2 rules (`src\Lordran.Tools\Mappings\system-items.tsv`): Humanity → Human Effigy, Fire Keeper Soul → Sublime Bone Dust, Rite of Kindling → Estus Flask Shard, embers → infusion stones, coloured titanite → DS2 titanite, Demon Titanite → Twinkling Titanite |
| skip-gesture | 15 | DS2 has its own gestures |
| decide-online | 10 | DS1 multiplayer/covenant tools (Red/Blue Eye Orb, Book of the Guilty…). Default: drop, because Seamless Co-op provides co-op/invasion items. To be confirmed with covenants |

Upgrade levels and infusion variants of DS1 weapons/armor map through their base item (DS2's own +10 and infusion system applies).

## DSR map blocks (from `DARK SOULS REMASTERED\map`)
m18_01 Undead Asylum · m10_02 Firelink Shrine · m10_01 Undead Burg/Parish · m10_00 Depths · m14_00 Blighttown/Quelaag · m12_00 Darkroot (00 + 01 DLC variant) · m15_00 Sen's Fortress · m15_01 Anor Londo · m11_00 Painted World · m16_00 New Londo/Valley of Drakes · m17_00 Duke's Archives/Crystal Cave · m13_00 Catacombs · m13_01 Tomb of Giants · m13_02 Great Hollow/Ash Lake · m14_01 Demon Ruins/Lost Izalith · m12_01 Oolacile (DLC) · m18_00 Kiln of the First Flame

## Rules
- Play modded games offline or only through Seamless sessions.
- Back up saves (`.sl2` and Seamless `.co2`) before every test.
- Keep game files, converted assets, and decompiled code local. Ship only our own code and converters.

## Layout
- `bin\` — exe copies (`DarkSoulsII.exe` = pristine original; `*.unpacked.exe` = SteamStub removed, for Ghidra)
- `ghidra\` — `run_all.bat` (DS2, then DSR), project dbs, logs; `decomp\` — exported C
- `saves-backup\<timestamp>\` — save backups
- `reference\ds2-seamless-coop-0.0.4\` — the Seamless zip as provided
- `tools\` — Ghidra 12.1.4, JDK 21, Steamless, UXM, Smithbox

## Before the mass migration (session 8)
- **Source checklist: `docs/ds1-content/README.md`** (DS1 census, one file per area group, completion checklists = the migration tracker; mark V/P/U as things are ported; fix wrong claims with evidence). Map facts: Darkroot = `m12_00_00_01.msb`; `m16_00` also holds the Valley of Drakes and the Darkroot Basin bonfire/elevator; the Firelink Altar is in `m18_00`; the README's cut content is not ported.
- **Tool gaps (Lordran.Tools):** `msb1-dump`: enemy InitAnimID / DamageAnimID / MovePointNames / PlatoonID, the MSB events section (spawn points, messages, SFX, sounds, generators, PseudoMultiplayer) and multi-row item lots · `ds1-text`: an FMG/category argument (ids collide across FMGs) · `param-grep`, `find-row`, `emevd-grep`: crash on missing/misordered arguments · a DS1 talk ESD extractor (`script\talk\*.talkesdbnd.dcx`).
- **Atmosphere (user, session 8):** DS1 lighting, music, ambient sound and ambience particles replicated per map; the best-matching DS2 area only where DS1's can't be expressed.
