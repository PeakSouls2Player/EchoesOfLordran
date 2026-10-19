# 04 — Sen's Fortress, Anor Londo, Painted World of Ariamis (DS1R content census)

Scope: `m15_00_00_00` (Sen's Fortress), `m15_01_00_00` (Anor Londo, incl. Darkmoon Tomb / Gwyndolin's corridor and the darkened-AL world state), `m11_00_00_00` (Painted World of Ariamis).

Sources (ground truth first):
- `lr msb1-dump <map> enemies|players|objects|regions|treasures|objacts` (DSR install, read-only). Japanese region names are translated inline where they explain an entity.
- `lr emevd-dump event\<map>.emevd.dcx` (+ `common.emevd.dcx` for boss reward/covenant award events 970/911/890).
- `lr dump-param dsr NpcParam|ItemLotParam|ShopLineupParam`, `lr ds1-text` (EventText ids).
- `msbev.exe <msb> enemies` (full enemy props: InitAnimID, DamageAnimID, MovePointNames, PlatoonID) and `msbev.exe <msb>` (MSB events section: SpawnPoints, Messages, Sounds, SFX, PseudoMultiplayer, MapOffset, Navmesh).
- Wiki (fextralife) only for names/lore cross-checks; anything only from wiki or inference is marked **(unverified)**. `ds1-text` searches every FMG, so some ids came back with text from the wrong FMG. Those texts are left out or marked.

**Idle poses and patrols (all three maps):** no enemy part in m15_00, m15_01 or m11_00 has `MovePointNames` set, and every `PointMoveType` is 0, so **there are no MSB patrol routes**. Movement comes only from AI/think params and the EMEVD warps listed below. Every part whose `InitAnimID` is not −1 is listed in that area's tables; all other parts start in the default stance (`InitAnimID`=−1, `DamageAnimID`=−1).

Conventions: positions are MSB `(x, y, z)`; **WT** = World-Tendency black-phantom variant (enabled only while pure-black tendency flag `11X0050` is ON — see porting notes); **NR** = non-respawning (EMEVD "kill once" event); `Anim N` = `ForceAnimationPlayback`. m15_00 and m15_01 share one coordinate frame (Sen's x≈60–136, AL x≈165–1860), so warp regions of one map are expressed in the other's space.

---

## 1. Sen's Fortress — `m15_00_00_00`

### 1.1 Header
| | |
|---|---|
| Entrances | (a) Main gate from the Undead Parish side (gate object `1501000`, opens when flag `11500200` is ON — set outside this map's EMEVD, by the two Bells of Awakening; exact setter unverified). Player start point `c0000_0001` entity `1500999` at (88, 13.2, 164) facing +z into the gate. (b) Return from Anor Londo by the bat-wing demon ride (AL event `11510240` → cutscene 150130/150132, warp to region `1502500` (113.2, 82.8, 255) = Iron Golem arena). (c) Bonfire warp (after Lordvessel). |
| Exits | (a) Back out the main gate. (b) Bat-wing demons to Anor Londo: action at region `1502505` "Examine ring of light" (msg 10010120) in the Iron Golem arena, only after Iron Golem dead (flag `11`) → cutscene **150000** (normal) or **150002** + DrawParam slot 2 (if AL darkened, flag `11510400`), warp to m15_01 point `1502501` (177.9, 207.2, 131.9). Awards achievement 33. |
| Prerequisites | Both bells rung (gate). Cage Key (`goods 2003`) for the cells and the shortcut-elevator lock; Master Key (`2100`) also opens cells (msg 10010883). |
| Key flags | `11` Iron Golem dead; `11500200` gate open; `11500803/806/809/812` boulder-track rotation state; `11500821/822/823` stuck-boulder (wall-breaker) states; `11500100/101` elevator state, `11500102` elevator unlocked; `11500105` one-way door; `11500110-11500116` cells opened. |

### 1.2 Progression walkthrough
1. **Gate courtyard** (y≈13–20). Gate `1501000` (88, 16.5, 178). Siegmeyer sits at the gate before it opens. Just inside: pressure plate `1501200` → arrow shooter `1501210` (trap A, region `1502200`). Two Serpent Soldiers.
2. **Cell corridor** (x 109–115, y 27.8, z 186–240): 7 cages `1501030–1501036` (Cage Key / Master Key). Logan in cage `1501032`. Corpses: Soul of a Hero, Large Soul of a Proud Knight.
3. **Pendulum-blade bridge** (x≈80, y≈37, z 224–261): 5 swinging blades `1501850–1501854` over the pit; a cross-row of 4 more (`1501855–1501858`, y 42.1, z 249). Serpent mages/soldiers on the far side; pressure-plate trap B (`1501201`/`1501211`) at the Large Titanite Shard chest. Falling off = the **Titanite Demon pit** (y≈7, 4 Titanite Demons, Scythe, ladders `1501140` → `1501142` back up).
4. **Lower level / Mimic** (y 12–21): 3 Serpent Mages in a row (y 12.2), Mimic (Lightning Spear), Covetous Gold Serpent Ring corpse, ladder `1501143`, boulder end-zone (instant-kill squeeze regions `1502811/1502812`).
5. **Boulder ramp** (x 104–128, y 26–37): first entry into region `1502110` triggers the scripted first boulder (serpent `1500110` crushed). Siegmeyer later relocates near here (`1502510`, two serpents force-killed). Checkpoint light-wall CP1 `1501700` at the ramp top (120.1, 36.8, 272). Shotel corpse at the ramp base; trap C (`1501202`) mid-ramp.
6. **Boulder turntable** (111.75, 49.2, 291.75): Rock Giant `1500101` above at (114.5, 62.76, 291.25) feeds boulders onto turntable `1501790`; the crank (help "Push lever") rotates the track between 4 exits `1501810–1501813` (3 o'clock / 9 o'clock modes, regions `1502801`/`1502800`). Correct rotation sends a boulder that breaks walls (stuck-boulder states `11500821–823`), opening the way to the **Cage Key** room (Ring of Steel Protection chest + Cage Key corpse, x 112–124, y 40, z 296–316). (Which exit opens which wall = unverified mapping; the three wall-breaker variants use objects `1501801/802/803` + hits `1503200/201/202`.)
7. **One-way door** `1501001` (102.6, 45.1, 267.9) — opens from the far side only ("Does not open from this side"); Black Sorcerer set + Hush corpse and (questline) Griggs nearby.
8. **Upper levels** (y 40–75): Balder Knights at the Flame Stoneplate Ring chest (y 53); traps D/E/F (`1501203–1501205`, E fires 4 shooters `1501214–17` at once); pendulum rows at y 60.2 (`1501859–862`) and y 67 (`1501863–866`); Checkpoint light-wall CP2 `1501702` (83, 58.6, 289.3); **bonfire** (72.5, 60, 301.3). Shortcut **elevator** `1501011` at (78, 25.2↔73.5, 255) between the lower level and this floor — first use requires the Cage Key (unlock anim 10). Tower Shield/Heavy Knight `1500201` guards the rooftop near the bonfire. Crestfallen Merchant in a tower (99.4, 72.2, 318.4).
9. **Rooftop** (y 73–90): Firebomb Giant `1500100` throws pots at 6 target regions `1502700–1502705`; surrounded by 9 breakable crates `1501100–1501108`. Prince Ricard in a cell (y 70). Chests: Divine Blessing, Rare Ring of Sacrifice. Tarkus' summon sign (spawn region `1502000`, 83.1, 80.7, 223.7).
10. **Iron Golem** fog `1501990` (83, 82.05, 255) → bridge arena along +x to (136, 83, 255). After the kill: the ring of light `1502505` → bat ride to Anor Londo.

### 1.3 Bonfires
| Name | Object / chr | Flag | Notes |
|---|---|---|---|
| Sen's Fortress | object `o0200_0002` entity `1501961` (72.5, 60, 301.3); bonfire chr `c1000_0002` entity `1500961`, talk 150001 | `11500984` | Standard kindling 0. No Firekeeper. Top floor near the shortcut elevator. |

### 1.4 Enemies (all 61 MSB enemy parts)
| Sub-zone | Enemy | Model | Count | MSB parts (entity) | Notes / drops |
|---|---|---|---|---|---|
| (system) | Trap bullet owner | c2690 | 1 | `c2690_0000` (`1500200`) | Disabled at load (event 1000000000); used as `BulletTeamEntity` for every arrow trap. **Not a real enemy.** |
| Gate courtyard | Serpent Soldier | c2690 | 2 | `c2690_0001`, `_0002` (92.5/83.5, 19.2, 199) | 500 souls; Man-serpent Greatsword (lot 26900000). |
| Cell corridor / ramp base | Serpent Soldier | c2690 | 4 | `_0004` (`1500110`, 124.2, 32.2, 247); `_0005` (`1500122`, 111.75, 27.7, 237.8, npc 269003); `_0006` (`1500120`, 127.5, 26.1, 252); `_0007` (`1500121`, 127, 26.1, 265) | `1500110`: AI off until region `1502110`, then Anim 500→603 while the first boulder rolls (scripted boulder victim, event `11500791`). `1500122`: MSB **InitAnimID 7000** (special idle pose next to object `1501020`; the visual pose is unverified). Event `11505080` clears it and plays Anim 2000 once it is damaged or its HP drops below 100%. `1500120/121`: force-killed (no souls) when Siegmeyer relocates (events `11500534/535` and on load if flags 1491/1492). |
| Blade bridge / mid | Serpent Soldier | c2690 | 4 | `_0003` (80, 25.2, 257, think 269001); `_0008` (107.9, 36.8, 272.6); `_0009` (83.4, 32.05, 289.5); — | |
| Upper floors | Serpent Soldier | c2690 | 2 | `_0010` (73.25, 48, 245.5); `_0011` (64.5, 48, 289) | |
| WT only | Serpent Soldier (black) | c2690 | 7 | `_0012`–`_0018` (`1500900`–`1500906`, npc 269004) | Pure-black World Tendency only (events `11505090–092`). |
| Ramp/Cage-Key room | Serpent Mage | c2700 | 2 | `_0000` (124.2, 42.25, 284.9); `_0011` (117.2, 40, 315.0) | 500 souls; Flamberge (lot 27000000). |
| Blade bridge | Serpent Mage | c2700 | 2 | `_0001` (89, 30.2, 249); `_0002` (95.2, 38.15, 287.6) | |
| Lower level | Serpent Mage | c2700 | 3 | `_0003`–`_0005` (74/82/87, 12.2, 256) | Row of three. |
| Upper floors | Serpent Mage | c2700 | 2 | `_0006` (64.5, 52.45, 261.2, think 270001); `_0007` (69.25, 55, 269.7) | |
| WT only | Serpent Mage (black) | c2700 | 3 | `_0008`–`_0010` (`1500907`–`1500909`) | WT. |
| Pit (y 7.2) | Titanite Demon ("Prowling Demon") | c2300 | 4 | `_0000`,`_0001` (`1500300/301`, npc 230002, 3000 souls); `_0002`,`_0003` (`1500302/303`, npc 230001, 2000 souls) | **NR** (event `11500860`). Drops Titanite Catch Pole / Demon Titanite (lots 230002x0/230003x0). Roam regions `1502733–735`, nest `1502731/732`. |
| Lower level | Mimic | c2780 | 1 | `_0000` (`1500010`, 111.0, 21, 274.25) | **NR** (`11500900`). Nest region `1502010`. Grab/kick logic events `11505010–015` (player Anim 7521 when opening). Drops Symbol of Avarice + **Lightning Spear** (lot 27800000/1). |
| Upper/roof | Balder Knight | c2560 | 4 | `_0000` (76.1, 81.8, 262.9, npc 256011 crossbow); `_0001`,`_0002` (98.2/92.2, 53, 280/264.5, npc 256010 sword); `_0003` (115.5, 73, 255, npc 256012 rapier) | 500 souls; Balder set / Rapier / Light Crossbow + shard lots 25601x00. |
| Rooftop (by bonfire) | Heavy Knight (Tower-Shield/Steel) | c2570 | 1 | `_0000` (`1500201`, 61.5, 68.4, 247.0, npc 257011) | 800 souls; Steel set / Tower Shield lots 25701100–105. Leash regions `1502721–723`. Backread removed when the bat cutscene plays. |
| Ramp top | Rock (boulder) Giant | c2860 | 1 | `_0000` (`1500101`, 114.5, 62.76, 291.25, npc 286002) | 1000 souls, Titanite Chunk. AI off until region `1502101` or damaged (`11505050`); warps back to `1502100` + Anim 9001 when idle (`11505051`). **Respawns** (not in kill-once list). |
| Rooftop | Firebomb Giant | c2860 | 1 | `_0001` (`1500100`, 78.2, 87.8, 259.6) | 3000 souls, Titanite Chunk. **NR**. Pot-throw bullets 5300–5305 at regions `1502700–705/710`; AI command 20 once all 9 crates destroyed. |
| Above gate | "Gate" Giant | c2860 | 1 | `_0003` (`1500102`, 87, 46, 190) | 3000 souls. **NR**. Fight region `1502750`. |
| Rooftop cell | Undead Prince Ricard (hostile NPC) | c0000 | 1 | `c0000_0009` (`6600`, 86.2, 70, 217.3) | 4000 souls, **Ricard's Rapier** (lot 6600). **NR**. Humanity value 8980. |
| Boss | Iron Golem | c2320 | 1 | `_0000` (`1500800`) | See 1.5. |
| Online | Vagrant | c3490/c3491 | 9 | passive `c3490_0000–0002` (`1503900/910/920`); aggressive `c3491_0000/0003` (`1503901/902`, 107.8, 7.2, 200.2), `_0001/_0004` (`1503911/912`, 95.5, 21, 260), `_0002/_0005` (`1503921/922`, 136.3, 73, 254.8) | Network-only spawns; aggressive ones come in -1/-2 variant pairs at the same spot. |
| NPCs | see 1.6 | c0000/c1000 | 6 | `c0000_0004–0008`, `c1000_0002` | |

### 1.5 Boss — Iron Golem
| | |
|---|---|
| Model / entity | `c2320_0000`, entity `1500800`, npc 232000, name id 2320 |
| Arena | Rooftop bridge from fog (83, 82, 255) to (136, 83, 255); golem spawns at (124, 83, 255) facing −x. Boss-start region `1502996`; BGM region `1502990`; "no sweep" region `1502730`; **fall-death region `1502850`** (116.5, 72, 255) — the golem can be knocked off the edge. |
| Fog | Object `1501990` (o5150) + SFX `1501991`; entry line-segment region `1502998`, walk target `1502997`, player Anim **7410** (fog walk). DSR multiplayer variants `11505843/846`. |
| Intro | No cutscene. Golem in waiting Anim **9000**, AI off until player enters `1502996` (event `11505392`); then cancel anim 9060, AI on, HP bar. Boss-room notification `11505393`. |
| Mechanics | Stagger part: NPC part 2320 (group 2, 200 HP) — when broken → EzState 1300 (topple), on event msg 1204 swaps to part 2321 (100 HP) for 15 s, then 1303 (recover); breaking 2321 → 1301 … 1304 (events `11505351/352`). Grab: hit `1503000` toggled by event messages 400/300 (`11505353`). Ring-out kill via the arena edge. |
| Helpers | **Iron Tarkus** white sign (needs Human form, within 10 m of `6510`; spawn region `1502000`; flags summon `11505031`, dismiss `11505033`; Tarkus walks into the fog with Anim 7410, `11505032`). |
| Reward | Flag `11`; common event 970 → lot **2590 Core of an Iron Golem** + lot 9000 Humanity. Achievement via "Handle Boss Defeat". SFX `1503010` spawned (ring of light). Clears flags 11807020–050. |
| World change | Fog removed; bat-ring `1502505` becomes usable; Firebomb Giant receives AI command 10. Siegmeyer/Logan state events key off flag 11 indirectly. |

### 1.6 NPCs
| NPC | Part / entity | NpcParam / talk | Where | Behaviour & questline in this area |
|---|---|---|---|---|
| Siegmeyer of Catarina | `c0000_0006` / `6280` | npc 6280, talk 150628, humanity 8446, **InitAnimID 7800** | (82.3, 16.6, 175.2), in idle pose 7800 at the closed gate | Enabled only in states 1490–1511/1513/1514 (event 50). Once gate flag `11500200` is ON he is warped to region `1502510` "Onion-knight destination" (129.6, 26.1, 265.5, ramp area) and serpents `1500120/121` are force-killed (events `11500534/535`, depending on talk flag `11500591`). Disabled (moves on to AL) on state 1491/1492 + flag `11500850`/`11500592` (`11500536`). Hostile at 90% HP after damage (flag 1512), dead 1513. |
| Big Hat Logan | `c0000_0004` / `6030` | npc 6030, talk 150603, humanity 8334 | (109, 27.85, 214.4) inside cage `1501032` | Present only in states 1090/1091/1096. Cage ObjAct (`11500112`) is **disabled until flag `11500593`** (set by his dialogue). Opening the cage with Cage/Master Key → state 1091 (freed; returns to Firelink). If 1092 + `11500594` → disabled. Hostile 1096, dead 1097. |
| Crestfallen Merchant | `c0000_0005` / `6250` | npc 6250, talk 150625, humanity 8422, **InitAnimID 7850** | Tower room (99.4, 72.2, 318.4), seated/slumped idle 7850 | Shop 5300–5329: Green Blossom, Black Firebomb, Titanite/Large/Green Titanite Shard, Thunder & Spell Stoneplate Rings, Greatsword, Greataxe, Balder Shield, Tower Shield, arrows/bolts (incl. Sniper Bolt), Catarina, Steel and Balder armour sets. States 1420 (default) / 1421 hostile / 1422 dead. |
| Iron Tarkus | `c0000_0007` / `6510` | npc 6510, talk 0, humanity 8924 | (83.1, 80.7, 223.7) | Summon-only phantom for Iron Golem (see 1.5). Disabled after flag 11 or when dismissed. |
| Griggs of Vinheim (hostile / hollowed?) | `c0000_0008` / `6043` | npc 6041, think 6040, talk 0 | (105.45, 41.25, 276.7) beside the Black Sorcerer set corpse | Team set to hostile (type 9) on load; **disabled unless flag 1117**. Event `11500533`: if Griggs state 1113 ON, 1114 OFF, flag **723** ON and player in m15 block 0 → state 1117, enable. Death → 1115 (`11500520`, flags 1110–1119). Interpretation: hollowed Griggs appearing in Sen's after Logan moves on (**unverified**). |
| Prince Ricard | `c0000_0009` / `6600` | npc 6600 | rooftop cell | Hostile mini-boss, see enemies. |
| Bonfire dummy | `c1000_0002` / `1500961` | npc 100000, talk 150001 | at bonfire | Bonfire talk/menu proxy. |

### 1.7 Items / treasures (MSB `treasures`, route order; chained lot rows `+1..` belong to the same pickup)
| Where | Item | Lot / flag | Part |
|---|---|---|---|
| Gate courtyard, by serpents | Soul of a Brave Warrior | 1500010 / 51500010 | o0500_0013 (92.4, 19.2, 202.3) |
| Cell corridor | Soul of a Hero | 1500420 | o0500_0009 (114.85, 27.85, 197.15) |
| Cell corridor | Large Soul of a Proud Knight | 1500430 | o0500_0010 (111.8, 27, 186) |
| Blade bridge start | Large Soul of a Proud Knight | 1500330 | o0500_0003 (72.0, 30.2, 217.25) |
| Blade bridge, chest (trap B) | Large Titanite Shard ×2 | 1500020 | chest o0110_0002 (97.95, 30.2, 249) |
| Blade bridge far side | Slumbering Dragoncrest Ring | 1500320 | o0500_0002 (87.6, 32.0, 263.9) |
| Lower level | Covetous Gold Serpent Ring | 1500070 | o0500_0011 (87.8, 21.7, 256.8) |
| Ramp base | **Shotel** | 1500300 | o0500_0000 (126.9, 30.7, 277.35) |
| Pit | Soul of a Brave Warrior | 1500350 | o0500_0005 (78, 7.2, 281) |
| Pit | **Scythe** | 1500360 | o0500_0006 (71, 7.5, 283) |
| Pit | (no lot row) | 1500340, 1500380, 1500370 | o0500_0004 (86.5, 7.2, 233.5), o0500_0007 (111.5, 7.2, 220), o0502_0000 (114, 7.2, 204) — empty/cut |
| Blade bridge upper | Large Soul of a Proud Knight | 1500310 | o0500_0001 (95.45, 38.1, 284.45) |
| One-way door area | **Black Sorcerer Hat/Cloak/Gauntlets/Boots + Hush** | 1500060–1500064 | o0500_0012 (106.5, 41.45, 268.75) |
| Upper floor | Large Titanite Shard ×2 | 1500080 | o0500_0014 (73.35, 48.2, 243.3) |
| Upper floor, chest (Balders) | **Flame Stoneplate Ring** | 1500090 | chest o0110_0009 (89.1, 53, 264.65) |
| Upper floor | **Sniper Crossbow** (+Sniper Bolts 1500441) | 1500440 | o0502_0003 (99, 56, 290.5) |
| Near bonfire | (no lot row) | 1500390 | o0502_0001 (68, 60.2, 291.4) |
| Rooftop | Large Soul of a Brave Warrior | 1500400 | o0500_0008 (66.7, 67, 295.6) |
| Rooftop | Large Titanite Shard ×2 | 1500410 | o0502_0002 (65.6, 70.4, 286.75) |
| Cage-Key room (boulder-opened) | **Ring of Steel Protection** | 1500000 | chest o0110_0000 (119.8, 42.2, 296.4) |
| Cage-Key room | **Cage Key** | 1500150 | o0500_0015 (122.2, 40.3, 316.4) |
| Rooftop chests by Tarkus | Divine Blessing | 1500040 | chest o0110_0004 (83.3, 77, 222.95) |
| Rooftop chests | **Rare Ring of Sacrifice** | 1500100 | chest o0110_0010 (83.3, 77, 220.55) |
| Rooftop chests | (no lot row) | 1500110, 1500120 | chests o0110_0011 (71.5, 82.2, 250.5), o0110_0012 (81.9, 80.7, 220.1) — empty/cut |
| Enemy drops | Lightning Spear (Mimic), Ricard's Rapier (Ricard), Titanite Catch Pole/Demon Titanite (Titanite Demons), Core of an Iron Golem (boss) | | |

Chest ObjActs `11500600/602/604/609/610` (event `11500600` keeps treasure disabled until opened).

### 1.8 Interactive objects & scripted events
| Object / event | Entity | What it does |
|---|---|---|
| Main gate | o5100 `1501000` | Event `11500201`: if `11500200` ON → reproduce open anim 0. Init region `1502050` outside the gate. |
| Client-only barrier | o5151 `1501994` (+SFX `1501995`) at (88, 15, 112.75) | Deleted for host/singleplayer at load; stays for clients (phantoms can't leave via the gate). |
| Checkpoint light walls | o5152 `1501700` (+SFX 1501701) ramp top; o5153 `1501702` (+1501703) before bonfire | Event `11500090` (2 slots): "Traverse the white light" from either side (regions `1502600/601`, `1502602/603`), player Anim 7410, then object disabled forever. |
| Arrow traps A–F | plates o5500/o5501 `1501200–1501205`, shooters o5510 `1501210–1501217` | Event `11505270` (6 slots): step in region `150220X` or hit the plate → plate anim 0, 3 arrows (bullet 5070, owner `1500200`) at angle param; re-arms after ≥2 other traps fired. Trap E also fires 4 shooters `1501214–17` simultaneously (`11505260`). |
| Pendulum blades | o5000 `1501850–1501866` (17) | Event `11505300`: permanent damaging object (behaviour 5040, r 0.3, every 2 s) on each blade; swing is the object's own looping anim. Rows: 5 on the main bridge (y 37.2), 4 cross-row (y 42.1), 4 at y 60.2, 4 at y 67. |
| Static hazard | o5300 `1501010` (116.1, 21, 279.5) | Damaging object behaviour 5060, r 1.2, 0.5 s tick near the boulder end-zone (**unverified** what it is visually). |
| Boulder track turntable | o5200 `1501790`, exits o5210 `1501810–813` | Event `11500850`: two action points (dummy 104 → player Anim 8020, dummy 102 → Anim 8021, help "Push lever") rotate the track ±90° (object anims 0–7; flags 803/806/809/812). `11505255`: standing in regions `1502800/801` also auto-rotates it (track drags the player). `11500840/841` sync. |
| Boulders | o5010 `1501800–1501807` | `11500790`: while Rock Giant `1500101` alive and not engaged, every ~10 s spawn boulder `1501800` (Anim 0), hit `1503210`, network-pick one of 4 slots → `11500795–798` choose path by track state → `11500700` (rolling damaging object behaviour 5050 r 1.7, lifespans 2.5/7.5/12.5/18.5 s per path) or `11500750` (path that **breaks a wall and stays**: enables static boulder `1501801/802/803`, collision `1503200/201/202`, sets `11500821/822/823` permanently). `11500791`: very first boulder scripted on entering `1502110`. `11500831`: player inside squeeze regions `1502811/812` while boulder slot running → instant death. |
| Shortcut elevator | o5301 `1501011` (78, 25.2, 255) | `11500100`: 2-floor elevator (y 25.5 ↔ 73.5), call regions `1502060–063` ("SC elevator judge"), navmesh flags `1503100–113` swapped per floor. `11500102/103`: locked until used with **Cage Key** (player Anim 7111, object anim 10, msg "Used Cage Key"; without key "It's locked"). |
| One-way door | o5140 `1501001` | `11500107` opens from dummy 100 side (player Anim 7112); `11500106` other side shows "Does not open from this side". Flag `11500105`. |
| Cells ×7 | o5310 `1501030–1501036` (ObjAct `11500110–116`) | `11500110`: ObjAct flag opens cell; msg "Used Cage Key" or "Used Master Key". Logan's cell `1501032` gated by `11500593` (`11500530`). 16 more decorative cells (o5310, no entity). |
| Firebomb crates | o5560/o5561 `1501100–1501108` | `11505055`: all 9 destroyed → Firebomb Giant AI command 20. |
| Ladders | o5400 `1501140`, o5401 `1501141`, o5404 `1501142`, o5403 `1501143` | Registered ladders (flags 11500010–017). |
| Iron Golem fog / boss logic | o5150 `1501990` | §1.5 (events `11505390–395`, `11500001`, `11505350–353`). |
| Bat ride to AL | region `1502505` | `11500210` (see header). |
| Siegmeyer/Logan/Griggs/Merchant | event 50 block | §1.6 (`11500510` hostility, `11500520` death/treasure, `11500530–536`). |

| MSB events section | — | Respawn **SpawnPoints** `1502960` (respawn 00), `1502961` (01), `1502950` (boss). Hidden developer blood **Message** id 5000 on collision h0005B0 (its text is not resolved, because ds1-text returned a wrong-FMG string). **Sounds:** boss BGM 150000000 (`1503800`, region "boss room BGM"), guillotine ambience 150000009 (in-castle region), gear sound. **SFX:** ring of light to AL 150008 (`1503010`), light walls 81500 (boss fog), 81501 (client barrier), 81502/81503 (CP1/CP2), arrow-switch glints 150004 on traps A–D (`1503500–1503503`). **Navmesh** nodebank events `1503100–1503113` belong to the elevator. |

Cutscenes: **150000** (bat ride, normal), **150002** (bat ride, darkened AL).

### 1.9 Porting notes (Sen's)
- **Boulder system** is the signature mechanic: needs a feeder giant, a 4-state rotating turntable driven by a crank, 4 paths with distinct lifespans, three permanent wall-break outcomes (persistent flags), squeeze kill-zones, and a scripted first boulder that kills a placed serpent. DS2 has rolling boulder objects (Iron Keep / Shrine of Amana-style traps are different) — expect to drive this with a translated EMEVD/ESD + custom object anims.
- **Pendulum blades**: permanent damaging objects on looping-animation objects (17); DS2 has pendulum traps natively (Huntsman's Copse/Belfry) — map model + attach hitbox.
- **Pressure-plate arrow traps** with a hidden owner chr (`1500200`) — port the owner as a disabled dummy enemy for bullet team.
- **Cage Key/Master Key dual-key cells**, Logan's cell gated by a dialogue flag.
- **Shortcut elevator with a key-locked gate** (first-use unlock).
- **Iron Golem ring-out** (fall-death region) + stagger parts; Tarkus NPC summon sign (DS2 has NPC summons natively).
- **Bat-wing transport** cutscenes both ways (150000/150002 ↔ AL 150130/150132) — plain warps with fade if cutscenes can't be ported; must respect darkened-AL DrawParam.
- **WT black phantoms** (10 entities) gated on World Tendency — DS2 has no World Tendency; decide: drop, or bind to a NG+/flag.
- Client-only barrier at the gate; checkpoint light walls (one-time fog).
- Vagrants are online-only, so skip them.
- Idle poses to reproduce: Siegmeyer 7800, Crestfallen Merchant 7850, serpent `1500122` 7000. There are no MSB patrol routes.

#### Area completion checklist — Sen's Fortress
- [ ] Gate (bell-flag gated) + client barrier
- [ ] 6 arrow traps (plates, shooters, owner dummy)
- [ ] 17 pendulum blades
- [ ] Titanite Demon pit (4, NR) + ladders out
- [ ] Mimic (Lightning Spear, NR)
- [ ] 19+12 serpent soldiers/mages (incl. scripted boulder victim, Siegmeyer-killed pair, special-idle `1500122`), 4 Balders, 1 Heavy Knight, 3 Giants (2 NR), Ricard (NR)
- [ ] Boulder giant + turntable + crank + 3 wall-break states + kill zones + first-boulder script
- [ ] Cage Key room; 7 cells; elevator with Cage-Key lock; one-way door
- [ ] 2 checkpoint light walls
- [ ] Bonfire `1501961`
- [ ] Logan (cage, flag 11500593), Siegmeyer (2 positions), Merchant (shop), Griggs (flag 1117), Tarkus sign
- [ ] Iron Golem (stagger parts, ring-out, Core + Humanity)
- [ ] Bat ride to AL (both lighting variants)
- [ ] All treasures in 1.7

---

## 2. Anor Londo — `m15_01_00_00`

### 2.1 Header
| | |
|---|---|
| Entrances | (a) Bat ride from Sen's → point `1502501` (177.9, 207.2, 131.9) on the arrival rooftop; player start part `c0000_0001` entity `1510998` (178, 207.2, 132). (b) Return from Painted World → region `1512500` (271, 96, 316) in the painting chapel (PW event `11100710`, cutscene 110035). (c) Bonfire warps (after Lordvessel). |
| Exits | (a) Bat-wing demon `1510100` at the arrival point → Sen's (`11510240`: action within 7 m, cutscene **150130** / **150132** if darkened, warp `1502500`, DrawParam 0). Attacking the bat makes it fly away permanently (Anim 9060, disabled, flag `11515050`). (b) Painting `1512510` "Paint world departure" (263, 96, 316): needs **Peculiar Doll** (goods 384) and host/singleplayer → cutscene **150135**, warp m11 `1102510`, respawn `1102511` (`11510230`). (c) Duke's Archives — not scripted in this map's EMEVD (handled elsewhere; **unverified**, see archives doc). (d) Gwyndolin's tomb (dead end). |
| Prerequisites | Iron Golem dead. Peculiar Doll for Painted World. O&S dead for Gwynevere/Lordvessel. Darkmoon Seance Ring / covenant state for Gwyndolin path (illusory wall `1511400` vanishes only after AL is darkened — see 2.8). |
| Key flags | `12` O&S dead; `120` O&S done (cleared again by killing Gwynevere); `11510000` O&S intro seen; `11510902`/`11510903` Ornstein-last / Smough-last; `11510400` **Gwynevere killed → darkened AL**; `11510900` Gwyndolin dead; `743` (set on Gwynevere kill or entering Gwyndolin's fog); `11510301/302/303` rotating-tower state, `11510305` tower used once; `11510200` main gate opened; `11510700` Lautrec Black-Eye-Orb world. |

### 2.2 Progression walkthrough
1. **Arrival rooftop** (x≈170–180, y 207): the carrier Bat-wing Demon `1510100` waits in Anim 9000 (talk prompt). Stairs down to the **first bonfire** `1511960` (169.8, 173.4, 255) with the **Darkmoon Knightess** (pre-kindled to 10 Estus).
2. **Sentinel courtyard** (x 200–245, y 179): two passive Royal Sentinels on the axis and side alcoves left/right (z≈177 and z≈332) each with 2 Sentinels + a chest (Demon Titanite / Twinkling Titanite); the right one has a **Mimic** (`1510201`) next to the chest.
3. **Rotating spiral elevator** o5600 `1511050` (243.5, 178.5, 255) — first-use animation triggered by region `1512350` ("rotating elevator, first time"). Down to y≈113–122: chest Demon Titanite, unopened chest `1510500` (no lot), client barrier `1511994`.
4. **Gargoyle walkway** (x 279–406, y 116–117, z 255): **Gargoyle #1** `1510450` (302, 117, 255). Path leads onto the roof/buttress and into the **painting chapel rafters**.
5. **Painting chapel** (x 262–343, y 96–135, z 290–330): rafters (y 125–135) with 6 Painting Guardians incl. the beam "assassin" `1510310` (drops when player enters `1512050`); chandelier `1511100` holds a corpse — breaking it drops **Great Magic Weapon** to the floor. Ladders `1511140/1511141`. Floor (y 96): 10 more Painting Guardians, **Black Iron set** corpse, **the painting** to the Painted World (`1512510`). Checkpoint light wall CP1 `1511700` (337.5, 129.2, 303.3) at the rafters exit.
6. **Rotating tower/bridge** o5610 `1511300` at (337.5, 80, 255) with 3 positions (flags 301/302/303; collision `1513301/302/303`, warp targets `1512301` 1F y93, `1512302` 2F y117, `1512303` 3F y128.6). Lever on the tower + 4 remote levers `1511301` (rafters exit, y 129), `1511302` (chapel floor exit, y 96), `1511303` (bottom, y 70, by Darkmoon Tomb), `1511304` (walkway y 117). First pull from the tower plays cutscene **150100** (rotate player −90° around the tower axis, −12 m). Routes: rafters ↔ walkway (Gargoyle #2, towards buttress) ↔ bottom (Darkmoon Tomb bonfire `1511962`).
7. **Gargoyle #2** `1510452` (384.8, 117, 255) on the walkway past the tower.
8. **Exterior / flying buttress** (x 404–470, y 109–160): 9 Bat-wing Demons patrol; buttress up to the castle; 2 Silver Knight archers (Dragonslayer Greatbow corpse (448.5, 144.1, 269.4) near them); Titanite Chunk / Soul of a Hero corpses.
9. **Main gate plaza**: 2 Royal Sentinels (438, 133.8, 247/263) before the great door `1511000` (446.5, 136.2, 255) — locked from outside ("Locked by some contraption", region `1512000`). **Giant Blacksmith** workshop at (465.9, 133.8, 295.1) through one-way door `1511200` (443, 133.8, 270.3) (opens from inside); Hawk Ring chest under the blacksmith.
10. **Castle interior / Silver Knight wing** (x 491–530, y 129–160, z 160–270): enter via the side route; checkpoint light wall CP2 `1511702` (513, 135, 162.4); **bonfire `1511961`** (504, 135, 174.6) with Solaire; 9 ObjAct doors `1511250–1511259`; Silver Knights (sword/spear/bow), Siegmeyer on a balcony, Titanite Demon room (504, 141, 219.6), **Havel's room** (4 chests + Mimic, y 129), Silver Knight set chests, Sunlight Medal chest, 2 more Mimics on the upper balconies.
11. **Main hall**: 2 Royal Sentinels (`1510412/413`, 479.7 and 512.6, 136.2, 255); lever `1511001` (453.1, 136.6, 242.6) opens the great door `1511000` from inside (shortcut to Blacksmith/bonfire). Solaire summon sign at (486.6, 142, 264.7) by the stairs.
12. **Ornstein & Smough** fog `1511990` (537.3, 142.6, 255) → arena (537–582, y 142.6). After the kill the two side fogs (`1511992`, `1511988`) drop and the side elevators `1511401/1511402` start running up to the **Princess's Guard bonfire** `1511950` (593, 161, 255) → door `1511010` (609.4, 164, 255, push Anim 7120) → **Gwynevere** (630.3, 166, 255.2) → **Lordvessel**.
13. **Darkmoon Tomb** (x 392–480, y 30–70): bonfire `1511962` (392, 69.8, 255), Ring of the Sun's Firstborn corpse (400.7, 70.5, 258.4), illusory Gwyn-statue/wall `1511400` (403.5, 70.2, 255) that breaks only after AL is darkened, kneel spot `1512410` (Darkmoon covenant), fog `1511890` (432.8, 60.2) → **Gwyndolin's infinite corridor** (y 30.2, x 440 → 1860; fog `1511892` at the corridor end y 30.2), Brass set / Sunlight Blade chests at (535, 56–58, 244–266).

### 2.3 Bonfires
| Name (descriptive) | Object | Flag | Kindling | Notes |
|---|---|---|---|---|
| Anor Londo — arrival / Darkmoon Knightess | `o0200_0000` `1511960` (169.8, 173.4, 255); chr `c1000_0000` `1510960` talk 151000; warp point `1510980` | `11510992` | **Standard level 10** (Firekeeper bonfire) | If the Knightess dies (flag `151`), using it shows "Fire Keeper is absent. Cannot light bonfire." (event `151`). |
| Anor Londo — castle (Solaire) | `o0200_0001` `1511961` (504, 135, 174.6); chr `1510961` talk 151001 | `11510984` | 0 | Solaire stands here. |
| Princess's Guard | `o0200_0002` `1511950` (593, 161, 255); chr `1510950` talk 151010; warp point `1510970` | `11510920` | 0 | Object disabled until O&S die (`11510001`), then enabled + registered. |
| Darkmoon Tomb | `o0200_0003` `1511962` (392, 69.8, 255); chr `1510962` talk 151002; warp point `1510982` | `11510976` | 0 | Reached via the rotating tower bottom. |

### 2.4 Enemies (all 101 MSB enemy parts)
| Sub-zone | Enemy | Model | Count | MSB parts (entity) | Notes / drops |
|---|---|---|---|---|---|
| Arrival | Bat-wing Demon (carrier) | c2260 | 1 | `c2260_0000` (`1510100`, 172.6, 211.3, 131) | MSB **InitAnimID 9000** (perched), think 1 (inert). It is the talk-able transport and flies off (Anim 9060) if hit. |
| Sentinel courtyard | Royal Sentinel | c2870 | 6 | `_0000` (`1510400`, 217.9, 179, 195.3), `_0001` (`1510401`, 217.5, 179, 324.5) think 287012; `_0004/_0005` (`1510404/405`, ~204, 179.4, 178); `_0008/_0009` (`1510408/409`, ~204, 179.4, 332) think 287011 | 1500 souls (npc 287010); Giant's Halberd/Giant Shield (lot 28701000). Shield = invincible NPC part 2870, projectile ×0 (`11515060`). Respawn. Disabled in darkened AL. |
| Sentinel courtyard (WT) | Royal Sentinel (black) | c2870 | 2 | `_0014`, `_0015` (`1510900/901`) | WT. |
| Courtyard right alcove | Mimic | c2780 | 1 | `_0001` (`1510201`, 207.8, 179.4, 337) | npc 278011 → Crystal Halberd (lot 27801010/11). **NR** (`11510850`). Nest `1512011`. |
| Gargoyle walkway | Gargoyle | c5351 | 2 | `_0000` (`1510450`, 302.1, 117, 255, PlatoonID 19929); `_0001` (`1510452`, 384.8, 117, 255) | 2500 souls each, **NR** (`11510860`), death awards lot 53500000 **Gargoyle's Halberd**. Tail = NPC part 5351 (65 HP): cut → tail chr appears (Anim 8100), gargoyle Anim 8000; **only `1510450`'s tail awards lot 53530000 Gargoyle Tail Axe**. Step/swoop regions `1512700–703`, return regions `1512790–792`. Disabled in darkened AL. |
| — tails | Gargoyle tail | c5353 | 2 | `_0000` (`1510451`), `_0001` (`1510453`) | Disabled until cut (`11515080`). |
| Chapel rafters | Painting Guardian | c2400 | 6 | `_0000`–`_0004`, `_0011` (y 125–135); `_0010` (`1510310`, 262.3, 135.5, 306.3) | 300 souls; Painting Guardian Sword/Throwing Knife (lot 24000000). `1510310` = beam "assassin": AI off until region `1512050` or damaged, then Anim 500 (drop) (`11515250`). Respawn. |
| Chapel floor | Painting Guardian | c2400 | 10 | `_0005`–`_0009`, `_0012`–`_0016` (y 96, x 263–326) | |
| Exterior/buttress | Bat-wing Demon | c2260 | 9 | `_0003`–`_0008`, `_0010`–`_0012` (`1510110`–`1510119` excl. 116; x 412–456, y 109–132) | 500 souls (npc 226000/226001 think). Disabled in darkened AL. |
| Main gate plaza | Royal Sentinel | c2870 | 2 | `_0002`, `_0003` (`1510402/403`, 438, 133.8, 263/247) think 287013 | |
| Main gate plaza (WT) | Royal Sentinel (black) | c2870 | 1 | `_0016` (`1510902`, 444, 136.1, 255) | WT. |
| Main hall | Royal Sentinel | c2870 | 2 | `_0012` (`1510412`, 512.6, 136.2, 255), `_0013` (`1510413`, 479.7, 136.2, 255) | npc 287000: 3000 souls, Giant's Halberd/Shield + Titanite Chunk (lot 28700000). Disabled during Lautrec invasion and in darkened AL. Return region `1512780`. |
| Buttress / balconies | Silver Knight (bow) | c2410 | 5 | `_0002` (`1510321`, 513, 151.4, 160.5); `_0005` (`1510500`, 500.1, 146.4, 266.6); `_0007` (`1510325`, 513, 158.2, 192); `_0011` (`1510327`, 491.4, 137.8, 198.2, think 241014); `_0012` (`1510328`, 495, 140.7, 163, think 241014) | 1300 souls (npc 241004), Dragonslayer Arrows (lot 24100400). `_0011/_0012` = the buttress Dragonslayer archers (**likely**). `1510500` disabled in Lautrec world & darkened AL. |
| Castle interior | Silver Knight (sword) | c2410 | 6 | `_0000` (`1510320`, 499.5, 158.4, 165.1); `_0006` (`1510324`); `_0009` (`1510300`), `_0014` (`1510301`), `_0015` (`1510302`) think 241010 (the trio Siegmeyer watches, balcony y 151.6); `_0010` (`1510326`, 513, 135, 209) | 1000 souls; Silver Knight Straight Sword + Shield (lot 24100000). |
| Castle interior | Silver Knight (spear) | c2410 | 4 | `_0001` (`1510305`, 500.35, 151.6, 208.4 — AI off until region `1512060`, damage or within 4 m, `11515251`); `_0003` (`1510322`); `_0004` (`1510323`, think 241023); `_0013` (`1510329`, think 241023) | 1000 souls; Silver Knight Spear + Shield (lot 24100300). |
| Castle interior (WT) | Silver Knight (black) | c2410 | 7 | `_0016`–`_0022` (`1510903`–`1510909`, npc 241005/241006) | WT. |
| Titanite Demon room | Titanite Demon ("Prowling Demon") | c2300 | 1 | `_0000` (`1510250`, 504, 141, 219.55, npc 230004) | 5000 souls, **NR**; Catch Pole/Demon Titanite (lot 23000400). Aggressive region `1512760`. |
| Havel's room | Mimic | c2780 | 1 | `_0000` (`1510200`, 522.3, 129, 213.3) | npc 278010 → **Occult Club** (27801000/01). NR, nest `1512010`. |
| Upper balconies | Mimic | c2780 | 2 | `_0002` (`1510202`, 501.0, 151.6, 193.2) npc 278012 → Silver Coin; `_0003` (`1510203`, 525.4, 151.6, 193.1) npc 278013 → Gold Coin | NR, nests `1512012/013`. |
| Blacksmith | Giant Blacksmith | c2860 | 1 | `_0000` (`6210`, npc 286001) | NPC — see 2.6. Lot 28600100 Blacksmith Giant Hammer on death. |
| Boss | Ornstein / Smough (+ phase-2 "giant" versions) | c5270, c5271, c2360 | 4 | `c5270_0000` (`1510800`, 578, 143.4, 253); `c2360_0000` (`1510810`, 580, 143.4, 256); `c5271_0000` (`1510801`, 572, 142.8, 255); `c2360_0001` (`1510811`, 572, 142.8, 255, npc 236001) | §2.5. |
| Princess's chamber | Gwynevere | c5310 | 1 | `_0000` (`1510600`, 630.3, 166, 255.2, talk 151611) | NPC/killable. §2.6. |
| Tomb | Dark Sun Gwyndolin | c5320 | 1 | `_0000` (`1510650`, 480, 30.2, 255) | §2.5. |
| Dark AL only | "Darkmoon Knight" heavy / light (hostile humans) | c0000 | 2 | `c0000_0012` (`6640`, 492, 136.2, 257); `c0000_0013` (`6650`, 492, 136.2, 253) | NpcParam names "Dark Moon Sword (Heavy/Light Knight)"; 5000 souls each, lots 6640/6650 (empty rows); **NR**. Enabled only when `11510400` ON (replace the main-hall Sentinels). |
| Online | Vagrant | c3490/c3491 | 9 | passive `c3490_0000/0001/0003` (`1513900/910/920`); aggressive pairs `c3491_0000/0002` (`1513901/902`), `_0001/_0004` (`1513911/912`), `_0003/_0005` (`1513921/922`) | Online only. |
| NPCs | see 2.6 | c0000/c1000 | 12 | `c0000_0003–0006, 0009–0011`, `c1000_0000–0004` | |

### 2.5 Bosses

#### Ornstein & Smough
| | |
|---|---|
| Entities | Ornstein `1510800` (npc 527000, name 5270), Smough `1510810` (npc 236000, name 2360), Super Ornstein `1510801` (npc 527100), Super Smough `1510811` (npc 236001). |
| Arena / fog | Entry fog o5860 `1511990` (+SFX 1511991), entry line region `1512998`, walk target `1512997`, start region `1512996`, BGM region `1512990`. Exit fogs o5861 `1511992`, o5862 `1511988`. Relocation point on phase change `1512850`. |
| Intro | First entry: cutscene **150140** (`11515392`; flag `11510000`), then both enabled, two HP bars. Invaders/black phantoms cannot trigger it. |
| Phases | Each is immortal while the other lives (`11515397`). Kill **Smough first** → cutscene **150120**, Ornstein killed and replaced by **Super Ornstein** `1510801`. Kill **Ornstein first** → cutscene **150121**, Smough replaced by **Super Smough** `1510811` (`11515396`). Smough has an invincible NPC part 2360 (projectile ×0) zeroed on death (`11515398/399`). |
| Rewards | Super Ornstein dies → flag `11510902` → lot 2610 **Soul of Ornstein** + Humanity, plus NpcParam drop **Leo Ring** (52710000). Super Smough dies → `11510903` → lot 2620 **Soul of Smough** + Humanity. Both set `12` and `120`. |
| World change | All three fogs removed; Princess's Guard bonfire enabled; elevators `1511401/1511402` loop; Gwynevere reachable; Solaire's/Lautrec's summon events stop (flag 12). |
| Helpers | **Solaire** white sign (`6543`, spawn `1512001`; requires Human + Solaire states 1004/1005/1006/1010/1011; flags 11515031/034); **Lautrec** sign (dialogue-driven, **unverified** — no EMEVD sign event in m15_01 for 6302). |

#### Dark Sun Gwyndolin (optional)
| | |
|---|---|
| Entity | `1510650` (npc 532000, name 5320). Starts as **Ally** team, disabled until the fight. |
| Approach | Darkmoon Tomb → fog o5867 `1511890` (432.8, 60.2, 255) (`11515380`, entry region `1512898`, target `1512897`; sets flag 743). Second fog o5868 `1511892` (432.75, 30.2) is the corridor end. |
| Intro | Entering region `1512896` → cutscene chosen by state (`11515386`): **150155** if AL darkened (host), **150161** if player is in Blade of the Darkmoon (covenant 8), else **150160**; warps to corridor start `1512900` (440, 30, 255). Phantoms entering the fog get 150151/150156. Joining the fight as a Darkmoon member **betrays the covenant** (+sin, flags 742/9002). |
| Mechanics | "Infinite corridor": on event message 600 Gwyndolin vanishes and reappears (Anim 7000) at the next of 30 warp points `1512705`, `1512710`–`1512739` spaced along x 450 → 1860 (`11510450` / `11515110`). Bloodstains from the far corridor are moved back to `1512421` after the fight (`11510140`). |
| Reward | Flag `11510900`, `4047`, `120` (if O&S dead); lot 2600 **Soul of Gwyndolin**; death cutscene **150152** (or **150157** darkened); achievement 39. |

### 2.6 NPCs
| NPC | Part / entity | Talk | Where | Behaviour / questline |
|---|---|---|---|---|
| Darkmoon Knightess ("Lady of the Darkling", Firekeeper) | `c0000_0003` / `6010` (npc 6010), **InitAnimID 7870** | 151601 | (165.6, 173.4, 259.3) by bonfire `1511960`, idle pose 7870. Her special anims are cleared when she is moved in darkened AL. | Flags 1030–1036 (1033 hostile, 1034 dead, 1031/1036 leave states). `11510501`: becomes hostile & re-enabled if Gwynevere dead (1232) or Gwyndolin hostile/dead (1241/1242). `11510531`: 1030 + flag 710 → state 1036; `11510532`: flags 711–714 all ON → state 1031 (**unverified meaning**: Lordvessel / Lord Souls placed). In darkened AL she is warped to `1512450` "place where the Darkmoon knight confronts you" (371, 117, 255 — walkway by the rotating tower). Death → flag 151 → bonfire unusable. |
| Solaire of Astora | `c0000_0006` / `6003` (npc 6002), **InitAnimID 7835** | 151600 | (506.3, 135, 173.3) at the castle bonfire, idle pose 7835 | Enabled if 1004 or 1008; `11510530`: 1001 + Undead-Burg talk flag `11010590` → 1008. Disabled while his summon sign is up. Summon copy `c0000_0011` `6543` (npc 6542, think 6543) for O&S. Region `1512770` "Sun knight doesn't want to enter the mausoleum". |
| Siegmeyer of Catarina | `c0000_0005` / `6283` (npc 6281), **InitAnimID 7850** | 151628 | Balcony (506, 151.6, 163.9), in idle pose 7850 watching 3 Silver Knights | Present in states 1493/1494 (not hostile 1512). `11510535`: Silver Knights `1510300/301/302` dead → 1494, enable. Reward via talk: flag `11510590` → lot 1010 **Tiny Being's Ring**. `11510536`: 1494 + `11510590` → disabled (moves on). |
| Knight Lautrec of Carim (invasion target) | `c0000_0004` / `6302` (npc 6301), PlatoonID 19928 (with both bodyguards) | 151630 | Main hall (464, 136.2, 255) | Disabled normally. **Black Eye Orb** (goods 115) world: the MSB **PseudoMultiplayer** event "NPC invasion, Lautrec" (HostEntity 6302, ActivateGoods 115, region `1512100` "NPC world invadable range") sets flag `11510700`. The Orb also reacts in region `1512101`. → enables Lautrec + 2 white-phantom bodyguards, 3 invasion fogs `1511750/752/754`, disables Sentinels `1510412/413` and archer `1510500`; AI waits until damaged (`11510541/543`). Lautrec death → 8102; back in your world (`11510544`): award lot 2060 **Fire Keeper Soul** + 6300 **Ring of Favor and Protection**, remove Black Eye Orb, enable corpse `1511601` "embraced" = **Embraced Armor of Favor set** (lot 1510080, (537.1, 161.2, 240.6)). |
| Lautrec's bodyguards | `c0000_0009` / `6490`, `c0000_0010` / `6500` | 0 | (467–468, 136.2, 253–257.5) | Only in the Black-Eye-Orb world; team = White Phantom. 6500 drops Man-serpent Greatsword (lot 6500). |
| Giant Blacksmith | `c2860_0000` / `6210` (npc 286001), **InitAnimID 9002** | 151621 | Workshop (465.9, 133.8, 295.1), in the 9002 smithing idle | Shop 6200–6221: Giant's Halberd/Shield, Moonlight/Dragonslayer/other arrows & bolts, Giant set, Weapon/Armor Smithbox, Repairbox, Titanite/Large/Green Shard, Twinkling Titanite; lightning/boss-weapon upgrades via ESD. Gravity/map-hit off while he stands over hidden object `1511110` (o5965) — becomes hostile (1361) or dies (1362) → object destroyed, revealing the **Hawk Ring** chest (`11515092`). EzState idle commands 1500/1501 (`11515090/091`). |
| Princess Gwynevere | `c5310_0000` / `1510600` | 151611 | (630.3, 166, 255.2) | Disabled at load, enabled by `11510400` (until killed). Talk → flag `11510592` → lot 1090 **Lordvessel**; Princess's Guard covenant (`11510595` → 1350 Ring of the Sun Princess; kneel Anim 7895/7896/7897 facing her, `11510461`). States 1230–1239 (1232 dead). **Killing her** (`11510400`): if player in Princess's Guard (covenant 2) → betray + sin; flag 743; clears 120; cutscene 150110 (Gwyndolin alive) / 150111 (Gwyndolin dead) (**branch order unverified**); then **darkened AL** (see 2.8) and lot 1090 Lordvessel awarded if not already owned. |
| Gwyndolin (covenant voice) | `c1000_0004` (no entity) | 151612 | (433.7, 60.2, 255) tomb altar | Invisible talk proxy for Blade of the Darkmoon. Kneel at `1512410` ("Kneel", `11510460`, requires state 1240, flag 743 OFF; Anim 7895→7896 loop→7897) — joins covenant (lots 1360 Blue Eye Orb, 1361 Darkmoon Blade Covenant Ring; rank 1 → 1370/1371). States 1240–1249 (1241 hostile, 1242 dead). |
| Bonfire dummies | `c1000_0000–0003` (`1510960/961/950/962`) | 151000/151001/151010/151002 | at each bonfire | |

### 2.7 Items / treasures (route order)
| Where | Item | Lot | Part / pos |
|---|---|---|---|
| Courtyard left alcove (chest) | Demon Titanite | 1510680 | o0110_0018 (200, 179.4, 173) |
| Courtyard right alcove (chest, Mimic beside) | Twinkling Titanite | 1510670 | o0110_0017 (200, 179.4, 337) |
| Below spiral elevator (chest) | Demon Titanite | 1510510 | o0110_0001 (229.9, 113.8, 250.9) |
| Below spiral elevator | (no lot) | 1510500 chest, 1510010 corpse | o0110_0000 (233.7, 121.8, 260.7), o0500_0001 (264.7, 113.8, 249.6) |
| Chapel rafters (chest) | Divine Blessing | 1510520 | o0110_0002 (339.7, 129.2, 327.4) |
| Chapel rafters | (no lot) | 1510020 | o0500_0002 (341.25, 129.2, 325.7) |
| Chapel floor (from chandelier) | **Great Magic Weapon** | 1510000 | o0500_0000 `1511600` (300.15, 96, 314.55) |
| Chapel floor | **Black Iron Helm/Armor/Gauntlets/Leggings** (+ chain rows 1510074 Greatsword, 1510075 Black Iron Greatshield per ItemLot names — verify) | 1510070–75 | o0500_0007 (262.8, 96, 293.9) |
| Darkmoon Tomb (by bonfire) | **Ring of the Sun's Firstborn** | 1510030 | o0500_0003 (400.7, 70.5, 258.4) |
| Exterior | Titanite Chunk | 1510040 | o0500_0004 (415.1, 126.75, 279.7) |
| Exterior | Soul of a Hero | 1510050 | o0500_0005 (469, 124.9, 198) |
| Buttress top | **Dragonslayer Greatbow** (+Dragonslayer Arrows 1510061) | 1510060 | o0500_0006 (448.5, 144.1, 269.4) |
| Blacksmith (chest under him) | **Hawk Ring** | 1510610 | o0110_0011 (468.9, 133.8, 296) |
| Castle interior (chest) | **Sunlight Medal ×3** | 1510690 | o0110_0019 (525, 135, 171.6) |
| Silver Knight room (chests) | **Silver Knight Helm/Armor** ; **Gauntlets/Leggings** | 1510580/581, 1510590/591 | o0110_0008 (520.7, 135, 222.2), o0110_0009 (522.8, 135, 222.2) |
| Titanite Demon area (chest) | (no lot) | 1510550 | o0110_0005 (504, 141.2, 222.5) |
| Havel's room (chests) | **Havel's Helm/Armor**; **Havel's Gauntlets/Leggings**; **Havel's Greatshield**; **Dragon Tooth** | 1510530/531, 1510540/541, 1510560 (flag 51510570), 1510570 (flag 51510560) | o0110_0003/0004/0006/0007 (516–522, 129, 206–208) |
| Havel's room | (no lot) — Mimic `1510200` stands at the 5th chest spot | 1510640 | o0110_0014 (522.3, 129, 220.5) |
| Upper balcony (chest) | Demon Titanite ×2 | 1510600 | o0110_0010 (508.1, 151.6, 181.9) |
| Upper floor | Soul of a Hero | 1510700 | o0500_0009 (508, 151.6, 224.9) |
| Above O&S entrance (only after Lautrec killed via Black Eye Orb) | **Helm of Favor / Embraced Armor of Favor / Gauntlets / Leggings of Favor** | 1510080–83 | o0500_0008 `1511601` (537.1, 161.2, 240.6) |
| Gwynevere's chamber | (no lot) | 1510630 | o0110_0013 (620.1, 164, 247.5) |
| Gwyndolin's corridor end (chests) | **Sunlight Blade**; **Brass Helm/Armor/Gauntlets/Leggings**; empty | 1510620; 1510660–63; 1510650 ("None") | o0110_0012 (534.9, 58, 255), o0110_0016 (535, 56.2, 244.35), o0110_0015 (535, 56.2, 266) |
| Rewards/drops | Lordvessel (Gwynevere), Leo Ring, Souls of Ornstein/Smough/Gwyndolin, Tiny Being's Ring, Fire Keeper Soul + Ring of Favor and Protection (Lautrec), Gargoyle's Halberd + Gargoyle Tail Axe, Occult Club / Crystal Halberd / Silver Coin / Gold Coin (Mimics), Silver Knight weapons, Dragonslayer Arrows, Blacksmith Giant Hammer | | |

Chest ObjActs `11510600–11510619` (event `11510600`).

### 2.8 Interactive objects & scripted events
| Object / event | Entity | What it does |
|---|---|---|
| Spiral elevator first use | o5600 `1511050` | `11510220`: Anim 0 once when player enters `1512350`. |
| Client barrier | o5863 `1511994` (+1511995) | Deleted unless client. |
| Checkpoint light walls | o5864 `1511700` (rafters exit), o5865 `1511702` (castle interior entry) | `11510090` (2 slots), regions `1512600/601`, `1512602/603`. |
| Chapel chandelier | o5810 `1511100` (breakable), o5800 `1511101`, o5801 `1511102`, corpse `1511600` | `11510100`: corpse hangs (looping Anim 110, glow SFX 99010); break `1511100` → chandelier falls (anims), corpse Anim 111, treasure enabled. |
| Painting | region `1512510` | `11510230` → Painted World (Peculiar Doll). |
| Rotating tower | o5610 `1511300`, ghost copy o5611 `1511310`, levers o5750 `1511301–1511304` | `11510300/310/319/320/330/340/350`: 3 stable states with collision `1513301–303`; tower lever (dummies 102/104 → player Anim 8020/8021, "Push lever") and remote levers (dummy 191 → Anim 8000) each valid only from certain states; first ever use = cutscene **150100** (rotate player around (337.5, 255) by −90°, −12 m). Region `1512310` marks "inside the tower" for the cutscene variant. Moving collision `1513310` while rotating; "Contraption does not move" while busy. MP sync re-snaps state. |
| Main great door | o5700 `1511000`, lever o5760 `1511001` (ObjAct) | `11510200`: pull lever (Anim 8021) → door opens (shortcut). `11510201`: from outside "Locked by some contraption". |
| Blacksmith door | o5710 `1511200` | `11510210/211`: one-way (opens with Anim 7110 from inside; "Does not open from this side"). |
| Breakable | o5740 `1511210` (502.9, 135, 218.7) | `11510215`: stays destroyed once broken (near Titanite Demon room; **unverified** what it hides). |
| Interior doors | o5720 `1511250–1511259` (ObjAct `11510250–259`, flags `6151027X`) | Generic doors; `11510260/11510710`: while doors 251/257/258 are still closed, standing in their threshold regions `1512250–255` gives SpEffect 4150 (to player and to Silver Knights `1510300–329`) — door AI/navigation helper (**unverified**). |
| O&S fogs | o5860–5862 `1511990/1511992/1511988` | §2.5. |
| O&S side elevators | o5630 `1511401`, o5620 `1511402` | Looping Anim 0 only after flag 12. |
| Gwynevere's door | o5730 `1511010` | `11510110`: push open (Anim 7120). |
| Darkened-AL switch | event `11510400` | On Gwynevere's death (or on load if done): DrawParam bank 1, LockCam slot 1, **map part `1513400` OFF / `1513401` ON** (sky/lighting swap), illusory object `1511400` removed. `11510130`: disables Sentinels `1510400–413`, Bat-wings `1510110–119`, Gargoyles `1510450/452`, archer `1510500`; enables Darkmoon knights `6640/6650`; moves Knightess to `1512450`. `11510131`: dying in darkened AL (host) sets the respawn to SpawnPoint `1512960` (MSB "respawn point 00"). `11510120`: with 743 + O&S dead + Gwyndolin alive, entering `1512400` "Gwyndolin's territory" or standing on hits `1513405/1513100` applies SpEffect 4501 (**unverified**: Darkmoon covenant visibility). |
| Illusory Gwyn statue/wall | o5900 `1511400` (403.5, 70.2, 255) | `11510401`: invulnerable; after `11510400`, destroyed (Anim 0) when player within 15 m. Gates the tomb corridor. |
| Gwyndolin fogs | o5867 `1511890`, o5868 `1511892` | §2.5. |
| Lautrec invasion fogs | o5869 `1511754`, o5870 `1511750`, o5871 `1511752` | §2.6 (`11510543`). |
| Ladders | o5410 `1511140`, `1511141` | Flags 11510010–013. |

| MSB events section | — | **SpawnPoints** (respawn) `1512960` (00), `1512961` (01), `1512962` (02), `1512950` (boss), `1512501` (temporary: return from the painting). **PseudoMultiplayer** "NPC invasion Lautrec" (host 6302, flag 11510700, goods 115, region `1512100`). Hidden developer blood **Messages** 5100 (Gwynevere's room collision h0024B1) and 5101 (h0005B1); their text is unresolved because of the wrong-FMG lookup. **Sounds:** O&S BGM 150100000 (`1513800`), Gwynevere BGM 150100002 (`1513801`), Gwyndolin BGM 150100001 (`1513802`), clock tower, strong wind ×5, birds. **SFX** light walls: 81510 (O&S entry), 81511/81512 (O&S exits), 81513 (client barrier), 81514/81515 (CP1/CP2), 81517/81518 (Gwyndolin fogs), 81519–81521 (Lautrec-world fogs). **Light:** a point light on Gwynevere. **Navmesh** nodebank events: rotating bridge `1513350–1513354` (00 chapel upper, 01 rampart, 02 castle, 03 chapel lower, 04 mausoleum), garden gate `1513200`, generic doors `1513250–1513259`. |

Cutscenes: 150100 (tower), 150110/150111 (Gwynevere killed), 150120/150121 (O&S phase 2), 150130/150132 (bat to Sen's), 150135 (into painting), 150140 (O&S intro), 150151/150152/150155/150156/150157/150160/150161 (Gwyndolin).

### 2.9 Porting notes (Anor Londo)
- **Two complete world states** (lit vs darkened): DrawParam bank swap, LockCam swap, map-part swap `1513400`→`1513401`, enemy roster swap, Knightess relocation, bat/painting cutscene variants. DS2 can do DrawParam/GParam switching per flag only coarsely — plan a separate MapPiece set + flag-driven enable/disable.
- **Rotating tower** with 3 states, 5 levers and a cutscene — needs a custom object with 3 animation endpoints + collision swaps; DS2 has rotating platforms (Iron Keep/Drangleic Castle elevators) but not this.
- **Gwyndolin infinite corridor**: 30 warp points far down +x (to x 1860) — huge invisible extension; bloodstain relocation event.
- **O&S two-phase swap** with immortality-while-partner-alive and per-order rewards; NPC parts for Sentinel/Smough shields (invincible part, projectile ×0).
- **Gargoyle tail cut** (only one tail drops the axe).
- **Black Eye Orb invasion** of an NPC world (Lautrec + bodyguards as white phantoms) — DS2 has no NPC-world invasion; fake with a flag-gated arena + fog walls.
- **Painting transport** both ways (Peculiar Doll check).
- Firekeeper bonfire with **kindling level 10**; "Fire Keeper absent" lock.
- Covenant joins by **kneeling** (Anim 7895–7897) for Princess's Guard and Blade of the Darkmoon; covenant betrayal on killing Gwynevere / fighting Gwyndolin.
- Mimics with nest regions and grab; Giant Blacksmith hovering over a hidden chest blocker.
- WT black phantoms; vagrants (skip).
- Idle poses to reproduce: Knightess 7870, Solaire 7835, Siegmeyer 7850, Blacksmith 9002, bat carrier 9000. There are no MSB patrol routes.

#### Area completion checklist — Anor Londo
- [ ] Arrival rooftop + bat carrier (talk → Sen's; flies off if hit)
- [ ] 4 bonfires (Knightess bonfire kindle 10; Princess's Guard gated on O&S)
- [ ] 13 Sentinels (incl. 3 WT) with shield parts; 10 Bat-wings; 17 Painting Guardians (beam assassin); 22 Silver Knights (incl. 7 WT; buttress archers; ambush spear knight; Siegmeyer's trio); Titanite Demon (NR); 4 Mimics (NR); 2 Gargoyles + tails (NR)
- [ ] Spiral elevator, chandelier corpse, rotating tower (5 levers + cutscene), main door lever, Blacksmith one-way door, Gwynevere door, 9 interior doors, 2 checkpoint walls, client barrier, ladders
- [ ] Painting → Painted World
- [ ] O&S (intro, 2 phase orders, rewards, Leo Ring), fogs, elevators, Solaire sign
- [ ] Gwynevere (Lordvessel, Princess's Guard kneel, killable → darkened AL)
- [ ] Darkened AL: lighting/map-part swap, roster swap, Darkmoon knights, Knightess move, illusory statue removal
- [ ] Darkmoon Tomb: covenant kneel, Gwyndolin infinite corridor boss, end chests
- [ ] NPCs: Knightess (Firekeeper), Solaire, Siegmeyer (Tiny Being's Ring), Blacksmith (shop + Hawk Ring blocker), Lautrec Black Eye Orb world (Fire Keeper Soul, Favor set corpse)
- [ ] All treasures in 2.7

---

## 3. Painted World of Ariamis — `m11_00_00_00`

### 3.1 Header
| | |
|---|---|
| Entrance | From AL painting (`1512510`, Peculiar Doll) → cutscene 150135 → region `1102510` "warp point from castle 2" (0, 30.4, 1007.2) — the player drops in high above; respawn `1102511`. MSB start part `c0000_0001` entity `1100999` (−19.8, 49, 954.3); bonfire warp point `1102980`. |
| Exit | Walk off the plank past Priscilla: region `1102500` "GoTo base, poly-drama" (−22.7, 61.5, 672) (`11100710`, host only, Priscilla not hostile) → requires Priscilla dead **or** flag `11105395` (player reached arena floor region `1102100` after `11105380`) → cutscene **110035** → AL `1512500` (painting chapel), DrawParam 2 if AL darkened; lot 9030 Homeward Bone once; respawn `1512960`. |
| Prerequisites | Peculiar Doll. Annex Key (found in the basement) for the Annex door. Lever in the basement opens the gate south. |
| Key flags | `4` Priscilla dead; `1690–1693` Priscilla state (1691 hostile, 1692 dead); `11100135` gate lever pulled; `11100030` shortcut door; `11100120` Annex door; `11100400` Undead Dragon dead; `11100810` Jeremiah dead; `11100700` Shiva Black-Eye-Orb world. |

### 3.2 Progression walkthrough
1. **Drop-in & bonfire** (z 944–1007): land from `1102510`; bonfire `1101960` (−23, 52.3, 944). Healing-fountain SE point `1102980`.
2. **Start terrace** (z 916–935): Hollows (3 drop from the walls when within 5 m, `11105150`), hanging corpse with **Humanity** (shoot rope `1101121` → corpse falls, `11100070`), Acid Surge corpse, one-way **shortcut door** `1101130` (−26.8, 56.7, 920.6) back to the bonfire (opens from the far side, Anim 7120). West annex (x −54…−76): 5 Hollows `1100400–404` + 3 souls. Jeremiah's invasion trigger region `1102011` is out here (−78.75, 47.7, 911.3).
3. **Buildings / rooftops** (x −20…10, y 56–77, z 857–916): Hollows (incl. climbers `1100104/105/137/138` that cling to walls until you enter `1102006/1102001/1102007`), archers, Engorged Hollows, Snow Rats, **Painting Guardian set chest**, Twin Humanities, Gold Coin, Egg Vermifuge, Velka's Rapier, Humanity on a second hanging corpse (rope `1101120`). Picking up the Soul of a Proud Knight on the roof (flag 51100260, region `1202021`) wakes the Crow Demons.
4. **Crow tower** (x −16…−43, y 77–102, z 852–884): 7 Crow Demons (4 scripted to fly between landing points A/A'/B/B' `1102020/030/040/050`); **Red Sign Soapstone** at the top.
5. **Courtyard** (−22, 55, 893): 14 **Hollow Phalanx** hidden until region `1102300` "Phalanx crumble" — they're warped into formation points `1102830–843` and wake (`11105010`). 13 crows o0150 on the "Andre" corpse fly away on entering `1102000`. **Dark Ember** corpse there (−41.3, 63.1, 887.55). Shiva/bodyguard invasion point (region `1102800`).
6. **Annex door** o1580 `1101250` (−40.7, 56.5, 877.75) — **Annex Key** (msg "Used Annex Key"). Annex tower: Mask of Velka + Black Cleric set, Vow of Silence, Soul of a Brave Warrior.
7. **Basement / sewer** (y ≈49): 9 Bonewheel Skeletons (+5 WT); **Annex Key** corpse (6.55, 49.45, 878.1); **lever** o1550 `1101150` (−22.9, 49.6, 895) "Turn handle" → player Anim 8010, cutscene **110000**, opens gates o1540 `1101160` (above, courtyard) and o1520 `1101170` (−22.7, 58.3, 844.5, south) (`11100135`); before that `1101170` says "Locked by some contraption".
8. **Checkpoint light wall CP2** o1603 `1101702` (−22.7, 58.35, 873) on the way south.
9. **Undead Dragon bridge** (z 780–840): Undead Dragon (c3420) asleep at (−23, 69.2, 782); approaching `1102070` wakes it (Anim 9060) and it moves to `1102071`; attack regions `1102120–128`. Hollows under the bridge (`1100130/132/135/136`) climb up with 0–0.6 s staggered delays when you enter `1102202`. Bloodshield, Large Soul of a Proud Knight. Two breakable braziers o1560 `1101180/181` (fire SFX `1103000/1`).
10. **Final bridge**: Heavy ("Tower") Knight `1100180` (−22.7, 68.2, 754.2) — jumps down to `1102201` when you enter `1102200` or attack it/the climbers (Anim 500, `11100420`).
11. **Priscilla** fog o1600 `1101990` (−22.7, 60.7, 714.5) → multi-level pit arena (floors y 38–68, stairs regions `1102700–708`). Priscilla starts friendly; talk or leave. Exit plank region `1102500` → back to AL. **Xanthous set** corpse (−21.4, 62.9, 677.4) appears only after Jeremiah is killed.

### 3.3 Bonfires
| Name | Object / chr | Flag | Notes |
|---|---|---|---|
| Painted World of Ariamis | `o0200_0000` `1101960` (−23, 52.3, 944); chr `c1000_0000` `1100960` talk 110000 | `11100992` | Kindling 0; no Firekeeper. |

### 3.4 Enemies (all 113 MSB enemy parts)
| Sub-zone | Enemy | Model | Count | MSB parts (entity) | Notes / drops |
|---|---|---|---|---|---|
| Start terrace | Hollow | c2500 | 3 | `_0000`–`_0002` (−26.8…−43, 56, 923–932.5) | 300 souls (npc 250010; Broken Straight Sword lot 25001000). |
| Start terrace (wall drop) | Hollow | c2500 | 3 | `_0011`–`_0013` (`1100100–102`, −40.5/−35.2, 61.2, 916.5–920.2) | MSB **InitAnimID 9013** (wall-cling pose). Map-hit, gravity and AI stay off until the player is within 5 m. Then the hollow drops (cancel anim 9060) (`11105150`). |
| Start terrace | Hollow | c2500 | 1 | `_0027` (−39, 62.2, 921.4, npc 250011) | |
| West annex | Hollow | c2500 | 5 | `_0022`–`_0026` (`1100400–404`, x −54…−75, y 47–51) | Disabled while Shiva world is active. |
| Buildings | Hollow (archer) | c2500 | 3 | `_0003` (−22.8, 66.1, 917), `_0006` (−17, 68.8, 906.5), `_0030` (−10, 73, 875.5) | npc 250012, Short Bow (25001200). |
| Buildings | Hollow | c2500 | 7 | `_0004`, `_0005` (−19/−18.5, 66–68, 908–920); `_0007`–`_0010` (x −8…−5.7, y 71–77, z 906–916.5, think 250002); `_0028` (−0.7, 76.1, 872.4, think 250002) | |
| Buildings (wall climbers) | Hollow | c2500 | 4 | `_0014` (`1100104`, 6.3, 72.1, 857.7, region `1102006`), `_0015` (`1100105`, 10.3, 65.8, 883.9, region `1102001`), `_0020`/`_0021` (`1100137/138`, −8.4/−6.8, 71.8, 873.3, region `1102007`, 0.3/0 s) | **InitAnimID 9013** (clinging/climbing pose). They hang inert (map-hit and gravity off) until the player enters the region (`11105160/170`). |
| Under dragon bridge (climbers) | Hollow | c2500 | 4 | `_0016`–`_0019` (`1100130/132/135/136`, −25…−20, 57.9, 783.7–787.1) | **InitAnimID 9013**. They climb up when the player enters `1102202` (delays 0.2/0/0.4/0.6 s). Damaging them also wakes the Tower Knight. |
| Under dragon bridge | Hollow | c2500 | 3 | `_0029` (−25.7, 58.4, 797.4, think 250002); `_0031` (−21.7, 58.4, 795.5) & `_0032` (−24, 58.5, 777.6) archers | |
| Buildings / annex | Engorged Hollow | c2840 | 11 | `_0000`–`_0010` (npc 284000 torch / 284001 fist / `_0006` 284002 special) | 400 souls. `_0006` (8.4, 56.2, 884.8, npc 284002, **InitAnimID 9005**, special idle pose) drops **Fire Surge** (lot 28400200). |
| WT | Engorged Hollow (black) | c2840 | 7 | `_0011`–`_0017` (`1100900–906`) | WT. |
| Buildings | Snow (Plague) Rat | c1203 | 11 | `_0000`–`_0010` (x −13…8, y 58–67, z 860–912) | 400 souls; Humanity (lot 12030000). |
| Crow tower | Crow Demon | c2310 | 7 | `_0000`–`_0003` (`1100300–303`) scripted; `_0004`–`_0006` (no entity) | 1000 souls; Souvenir of Reprisal (lot 23100000). MSB InitAnimID: `_0000` **9000**, `_0001` **9001** (perched poses); the rest use the default. `1100300/301`: SpEffect 4160, hover (no gravity/map hits) until the rooftop soul is taken (51100260 + region `1202021`) or player enters `1102040`; then land (anims 3010/3011, 3012/3013) and fly between landing points; `1100302/303` play 3006 on combat/region `1102050` (events `11100200`, `11105190–280`). |
| Courtyard | Hollow Phalanx | c2830 | 14 | `_0000`–`_0013` (`1100200–213`) | 500 souls; Spear + Large Leather Shield (28301000/1). Frozen/invisible until region `1102300`, warped into formation `1102830–843` (`11105010`); control regions `1102400–404`. Disabled in Shiva world. |
| Basement | Bonewheel Skeleton | c2930 | 9 | `_0000`–`_0008` (y 49.4–49.8) | 800 souls; Bonewheel Shield. |
| Basement (WT) | Bonewheel Skeleton (black) | c2930 | 5 | `_0009`–`_0013` (`1100907–911`) | WT. |
| Dragon bridge | Undead Dragon (+ legs, wings) | c3420, c3421, c3422 | 3 | `c3420_0000` (`1100170`, −23, 69.2, 782); `c3421_0000` (`1100171`, legs, immortal/invincible/no HP bar); `c3422_0000` (`1100172`, wing part) | 5000 souls, **NR** (`11100400`); Dragon Scale (34200000). MSB InitAnimID: body **9000** (asleep/lying), legs **9060**. Gravity off, default backread. Waking: region `1102070` or damage → Anim 9060 (`11105370`). Wing/part cut on event msg 400 → wing chr Anim 8100 then hidden (`11105371`). |
| Final bridge | Heavy ("Tower") Knight | c2570 | 1 | `_0000` (`1100180`, −22.7, 68.2, 754.2, npc 257020) | 1300 souls; Steel set/Tower Shield + Large Titanite Shard (25702000). Jump-down event `11100420` (region names call it "big skeleton"). |
| Boss | Crossbreed Priscilla (+ tail) | c2730, c2731 | 2 | `c2730_0000` (`1100160`, −22.75, 60.5, 697, talk 110636); `c2731_0000` (`1100161`, tail, parked at y 200) | §3.5. |
| Invaders (NPC) | King Jeremiah (black-sign NPC invader) | c0000 | 1 | `c0000_0005` (`6570`, −38.5, 55.7, 910.6) | See 3.6. Notched Whip (lot 6570). |
| Shiva world | Shiva of the East + bodyguard | c0000 | 2 | `c0000_0003` (`6312`, −24, 54.8, 895.9), `c0000_0004` (`6422`, −21.2, 54.6, 893.9) | See 3.6. |
| Online | Vagrant | c3490/c3491 | 6 | passive `c3490_0001/0002` (`1103900/910`); aggressive `c3491_0000/0001` (`1103902/901`, −5, 58.1, 867.9), `_0002/_0003` (`1103911/912`, −37.25, 49.6, 888.3) | Online only. |
| Bonfire | dummy | c1000 | 1 | `c1000_0000` (`1100960`) | |

### 3.5 Boss — Crossbreed Priscilla
| | |
|---|---|
| Entity | `1100160` (npc 273000, name 2730), talk 110636; tail chr `1100161`. |
| Arena | Fog o1600 `1101990` (+SFX 1101991), entry region `1102998`, target `1102997`, boss-logic region `1102996`; escape-through-fog regions `1102995/994`; BGM region `1102100` (floor of the pit); spawn `1102110`; waiting spots `1102750–752`; floors `1102760–764`, stairs `1102700–708`. Second fog o1601 `1101992` is always disabled (cut). |
| Start | **Non-hostile by default**: team Ally, AI off, HP bar hidden (`11105392`). Fight starts if she is damaged after entering (`11105393`), or if her state is already hostile (1691) and the player reaches `1102100`. Then team Strong Enemy, HP bar. Becoming hostile: damage while state 1690 → 1691 (`11100530`). She is immortal while the player stands in the exit region `1102500` (host) so you can't cheese-kill at the plank (`11100710`). |
| Mechanics | **Invisibility**: event msg 600 → HP bar hidden, flag `11105381`, random warp to one of 10 regions `1102810–1102819` (`11105397`); msg 700 → visible again (`11105398`). **Tail cut**: NPC part 2730 (158 HP); when broken (while visible) → Anim 8000, tail chr warped to dummy 40 and plays 8100; awards lot 27310000 **Priscilla's Dagger** (Human/Hollow only) (`11105396`). |
| Reward | Flag `4`; lot 2520 **Soul of Priscilla** + lot 9020 Twin Humanities (common 970); achievement 40; fog removed; Priscilla's state 1692 if killed (`11100531`). |
| Talk | Talk 110636 lets you leave; jumping off the plank (`1102500`) exits (see header). |

### 3.6 NPCs
| NPC | Part / entity | Behaviour |
|---|---|---|
| Crossbreed Priscilla | `1100160` | Talkable boss; states 1690–1693. |
| Xanthous King Jeremiah | `c0000_0005` / `6570` (npc 6570), humanity 8964 | `11105030`: host, Human, not yet dead, Priscilla alive → when the player enters `1102011` an **NPC black sign** invasion is placed (spawn `1102010`; flags 11105031/032); retried every 20 s. Disabled unless invading. Death → `11100810` → **Xanthous Crown/Overcoat/Gloves/Waistcloth** corpse `1101610` enabled (lots 1100150–153) + Notched Whip drop. |
| Shiva of the East + bodyguard (Black Eye Orb world) | `6312` (npc 6311), `6422` (npc 6421, white phantom) | Disabled normally. The MSB **PseudoMultiplayer** event "NPC world (Nobushi)" (HostEntity 6312, ActivateGoods **116** = Shiva's Black Eye Orb, region `1102800` in the courtyard) sets flag `11100700`, and then `11100532` runs: enable map part `1103100` + hit `1103101`, disable ladder `1101141`, enable fog `1101750`, enable Shiva + bodyguard, disable Phalanx `1100200–213` and Hollows `1100400–404`. Shiva's death (state 1606/1607) → 8110 → back home: remove Black Eye Orb, flag `11100300` → awards **Chaos Blade** (lot 2800/2810/2820 depending on flags 11400400–402), Shiva state 1764. |
| Bonfire dummy | `1100960` | talk 110000. |

### 3.7 Items / treasures (route order)
| Where | Item | Lot | Part / pos |
|---|---|---|---|
| Below arrival | (no lot) | 1100110 | o0500_0011 (−26.2, 44.2, 963.5) |
| Start terrace | (5 missing lots) | 1100000–1100004 | o0500_0000 (−43.6, 56, 922.2) |
| Start terrace (hanging, shoot rope `1101121`) | Humanity | 1100240 | o0500_0101 `1101601` (−40.55, 55.8, 923.15) |
| Start terrace | **Acid Surge** | 1100190 | o0500_0019 (−49, 52.1, 934.8) |
| Start terrace | Soul of a Proud Knight; (no lot 1100270 on same part) | 1100290 | o0500_0027 (−37.3, 62.1, 918.5) — **two MSB treasure entries share this part** |
| West annex | Soul of a Proud Knight / Large Soul of a Proud Knight / Soul of a Proud Knight | 1100280 / 1100300 / 1100310 | o0500_0026 (−75, 47.3, 911.75), o0500_0028 (−64.8, 50.1, 896.8), o0500_0029 (−76.6, 47.4, 915.2) |
| Buildings | **Twin Humanities** | 1100050 | o0500_0002 (−16.8, 73.3, 911.15) **and** o0500_0005 (5.96, 58.4, 868.9) — **same lot & flag on two corpses (only one obtainable)** |
| Buildings | Soul of a Brave Warrior | 1100030 | o0500_0003 (−12.4, 66.85, 910) |
| Buildings roof (wakes crows) | Soul of a Proud Knight | 1100260 | o0500_0024 (−14.7, 78.2, 915.1) |
| Buildings | Soul of a Proud Knight | 1100010 | o0500_0001 (−17.1, 69.2, 902.6) |
| Buildings | **Velka's Rapier** | 1100320 | o0500_0030 (−12.75, 68.5, 878.4) |
| Buildings | Soul of a Brave Warrior (+1100071 no row) | 1100070 | o0500_0007 (3.4, 77, 883.2) |
| Buildings | Gold Coin | 1100100 | o0500_0010 (7.8, 64.4, 882.5) |
| Buildings (hanging, rope `1101120`) | Humanity | 1100230 | o0500_0100 `1101600` (0.64, 54.95, 886.2) |
| Buildings | Large Soul of a Proud Knight | 1100040 | o0500_0004 (7.5, 73.3, 859.3) |
| Buildings | Egg Vermifuge | 1100250 | o0500_0023 (3.7, 76.1, 861.4) |
| Buildings (chest) | **Painting Guardian Hood/Robe/Gloves/Waistcloth** | 1100500–503 | o0110_0000 `1101650` (−0.6, 58.4, 869.3) |
| Buildings | (no lot) | 1100080 | o0500_0008 (2.2, 58.2, 875.5) |
| Courtyard | Soul of a Brave Warrior | 1100170 | o0500_0017 (−13.15, 55.4, 876.9) |
| Courtyard (crows' "Andre" corpse) | **Dark Ember** | 1100370 | o0503_0000 (−41.3, 63.1, 887.55) |
| Crow tower | Soul of a Brave Warrior ×2 | 1100340, 1100350 | o0500_0032 (−36, 70, 860.75), o0500_0033 (−19.3, 86.5, 853.1) |
| Crow tower top | **Red Sign Soapstone** (row name says Red Tearstone Ring; item is goods 101) | 1100330 | o0500_0031 (−28.6, 102, 856) |
| Annex | **Mask of Velka + Black Cleric Robe/Manchette/Tights** | 1100060–063 | o0500_0006 (−31.2, 73, 865.3) |
| Annex | **Vow of Silence** | 1100090 | o0500_0009 (−38.5, 77.4, 871.5) |
| Annex lower | (no lot) | 1100180 | o0500_0018 (−36, 56.6, 880.2) |
| Basement | Soul of a Proud Knight; Soul of a Brave Warrior | 1100120; 1100130 | o0500_0012 (−30, 49.55, 903.6); o0500_0013 (−19.7, 49.5, 890.9) |
| Basement | **Annex Key** | 1100140 | o0500_0014 (6.55, 49.45, 878.1) |
| Ring corpse | **Ring of Sacrifice** | 1100160 | o0500_0016 (2.3, 59.8, 908.7) |
| Dragon bridge | **Bloodshield** | 1100200 | o0500_0020 (−23.5, 69.2, 812.3) |
| Dragon bridge | (no lot) / Large Soul of a Proud Knight | 1100220 / 1100210 | o0500_0022 (−24, 69.2, 819.1) / o0500_0021 (−21.45, 69.4, 825.55) |
| Hanging corpse (no part) | (no lot) | 1100360 | entry "36 hanging", no MSB part |
| Past Priscilla (after Jeremiah killed) | **Xanthous Crown/Overcoat/Gloves/Waistcloth** | 1100150–153 | o0500_0015 `1101610` (−21.4, 62.9, 677.4) |
| Drops / rewards | Soul of Priscilla, Twin Humanities, Priscilla's Dagger (tail), Dragon Scale (dragon), Fire Surge (special Engorged Hollow), Notched Whip (Jeremiah), Chaos Blade (Shiva), Homeward Bone (exit, once) | | |

Not found in DSR data: **Velka's Talisman** (EquipParamWeapon 1367000) has **no ItemLot anywhere** — it is only sold by Oswald (ShopLineup 4404). ItemLot row 1100020 "[Painted World] Dried Finger" exists but **no MSB treasure references it**.

### 3.8 Interactive objects & scripted events
| Object / event | Entity | What it does |
|---|---|---|
| Shortcut door | o1500 `1101130` | `11100030/031`: one-way (from bonfire side "Does not open from this side"); player warped to `1102090`, Anim 7120. |
| Annex door | o1580 `1101250` (ObjAct `11100120`) | Annex Key → "Used Annex Key". |
| Gate lever | o1550 `1101150` | `11100135`: "Turn handle" (Anim 8010), cutscene **110000**, gates o1540 `1101160` + o1520 `1101170` anim 1; disables crow props `1101018–020`. `11100136`: `1101170` "Locked by some contraption" before. |
| Hanging corpses | ropes o1570 `1101120` / o1571 `1101121`, corpses `1101600/1101601` | `11100070`: corpse loops anim 120/125 with glow SFX 99005; break rope → falls (anim 121/126), treasure enabled. |
| Braziers | o1560 `1101180/181` (fire SFX 110003 `1103000/1` = MSB "brazier SFX 05/06") | `11100100`: breaking a brazier removes its fire SFX, and it stays destroyed. |
| Ambient crows | o0150 ×23 (`1101011–1101044`, `1101300–1101311`) | `11106200`/`11106298/299`: play anim 12/13 (fly off) and disappear when the player comes within 7 m / enters `1102000` (some gated by flags 11006200/11006205). |
| Checkpoint light wall | o1603 `1101702` | `11100090` slot 1 (regions `1102602/603`). |
| Shiva-world fog & wall | o1604 `1101750`, map part `1103100`, hit `1103101` | §3.6. |
| Ladders | o1510 `1101140`, o1511 `1101141` (registered by `11100040` unless Shiva world), o1512 `1101142`, o1513 `1101143` | |
| Boss fogs | o1600 `1101990`, o1601 `1101992` (always off) | §3.5. |
| Exit | region `1102500` | `11100710` (cutscene 110035). |
| Basement objects | o1610 `1101210/211` (−8.2/−1.6, 49.3, 889.5) | No EMEVD reference (**unverified**: drain grates). |
| MSB events section | — | **MapOffset (0, 0, +1000)**: the whole map is authored with a +1000 z offset, so every m11 position in this doc already includes it. **SpawnPoints** `1102960` (00), `1102950` (boss), `1102511` (temporary, arrival from AL). **PseudoMultiplayer** "NPC world (Nobushi)" (Shiva, goods 116, flag 11100700, region `1102800`). **Wind** event for SFX (vector (0,0,1–1.5), swing cycle 17 s). **SFX:** drifting fog 110000/110001, six braziers 110003 (05/06 breakable, see above), thirteen torches 530, light walls 81100 (boss), 81101 (after boss), 81103 (CP2), 81104 (Shiva-world wall). **Lights:** 21 point lights (6 braziers, 13 torches, 2 skylights). **Sounds:** boss BGM 110000000 (`1103800`), "grudge/spite" ambience 110000009, strong wind ×3, underground ambience 110000002. **Navmesh** events `1102040/1102041/1102042` (the ids are shared with region entity numbers; they control event navmesh and the generic door). |

### 3.9 Porting notes (Painted World)
- **Snow/fog world** with its own lighting; drop-in arrival from height and an exit via a cutscene off a plank.
- **Ambient-prop scripting**: wall-clinging hollows (gravity/map-hit toggles), crows that fly off, Phalanx that "rise" into formation (hidden → warped into 14 formation points).
- **Crow Demon flight routing** between landing points via events.
- **Undead Dragon** multi-chr (body + immortal legs + wing part).
- **Priscilla**: friendly-until-attacked boss, invisibility with random warp points, tail cut weapon, protected exit region.
- **NPC invasions**: Jeremiah (black sign NPC, placement on region entry) and Shiva's Black-Eye-Orb world (roster swap + extra map part/hit + ladder removal).
- Duplicate-lot corpses (Twin Humanities) and shared-part entries — decide whether to fix or replicate.
- **MapOffset +1000 z**: when placing PW geometry, subtract the offset or keep it consistently. Braziers and torches need point lights plus fire SFX, and two braziers are breakable.
- Idle poses to reproduce: hollow wall-clingers 9013 (11 hollows, `c2500_0011`–`_0021`), Engorged Hollow special 9005, Crow Demons 9000/9001, Undead Dragon asleep 9000 (legs 9060). There are no MSB patrol routes.

#### Area completion checklist — Painted World
- [ ] Drop-in arrival + bonfire `1101960`
- [ ] 33 Hollows (wall-drop ×3, climbers ×8, archers ×5, west annex ×5), 18 Engorged (7 WT, Fire Surge one), 11 rats, 7 Crow Demons (scripted flights), 14 Phalanx (rise script), 14 Bonewheels (5 WT), Undead Dragon (3 parts, NR), Tower Knight (jump-down), vagrants (skip)
- [ ] Shortcut door, Annex door (Annex Key), gate lever + cutscene + 2 gates, 2 hanging corpses, 2 breakables, 23 crow props, checkpoint wall, 4 ladders
- [ ] Priscilla (friendly start, invisibility warps, tail dagger, exit protection, rewards)
- [ ] Exit plank cutscene → AL (lighting variant, Homeward Bone once)
- [ ] Jeremiah NPC invasion → Xanthous set corpse
- [ ] Shiva Black-Eye-Orb world → Chaos Blade
- [ ] All treasures in 3.7

---

## 4. Cross-area summary of travel links
| From | Trigger | To |
|---|---|---|
| Undead Parish side | Gate `1501000` (bells) | Sen's start `1500999` |
| Sen's Iron Golem arena `1502505` | Golem dead; cutscene 150000/150002 | AL arrival `1502501` |
| AL arrival bat `1510100` | Talk; cutscene 150130/150132 | Sen's `1502500` |
| AL painting `1512510` | Peculiar Doll; cutscene 150135 | PW `1102510` (respawn `1102511`) |
| PW plank `1102500` | Cutscene 110035 | AL painting chapel `1512500` (respawn `1512960`) |
| AL → Duke's Archives | not in m15_01 EMEVD (**unverified**; see archives doc) | m17_00 |
