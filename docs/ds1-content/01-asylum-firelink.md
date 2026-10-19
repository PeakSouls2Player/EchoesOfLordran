# DS1 content census 01: Opening, Northern Undead Asylum, Firelink Shrine (+ Firelink Altar)

Source of truth: DSR data read with `lr.cmd msb1-dump`, `lr.cmd emevd-dump`, `lr.cmd dump-param dsr`, plus a scratch
SoulsFormats reader for MSB events (messages, spawn points, SFX, sounds) and enemy `InitAnimID`s. Event ids, entity ids,
flags and coordinates below are DS1 (DSR) values. Wiki knowledge is marked "(wiki)"; anything not proven by data is
marked "(unverified)". DS1 → DS2 id/flag mapping rules live in `docs/translation-rules.md`; this file only lists what exists.

Maps covered:
- `m18_01_00_00` Northern Undead Asylum (first visit + return trip with the Stray Demon). Full EMEVD dump: `data\scratch\m18_01-emevd.txt`.
- `m10_02_00_00` Firelink Shrine, its graveyard, the aqueduct towards the Undead Burg, the Fire Keeper cell, the lift chamber.
- `m18_00_00_00` only the **Firelink Altar** part (Frampt/Kaathe at the altar, Lordvessel, Lord Souls, Kiln door). The Kiln of the First Flame itself (Black Knights, Gwyn, Solaire's sign) is left to the Kiln document.

Not in these maps (linked only): Lautrec's cell (Undead Parish, `m10_01`), Laurentius' barrel (Depths, `m10_00`), Griggs' room (Lower Undead Burg, `m10_01`), Logan's cell (Sen's Fortress, `m15_00`).

---

## 0. The opening (new game)

| Step | What happens | Data |
|---|---|---|
| 1 | Title/opening movie (Age of Fire lore narration) | `movww\frpg_opening.bik` (Bink video; also `frpg_title.bik`, `frpg_goodending.bik`) |
| 2 | Character creation: name, sex, class (10), gift, physique/face | menu; class index is later tested by EMEVD `IF Player's Class` (event 11810400) |
| 3 | Load into `m18_01`; first time only: cutscene **180101** (Oscar drops a corpse through the cell's ceiling hole), then the player is warped to region **1812011** "post-cutscene start point" (-15.45, 184.70, -46.80) in the cell | event 0 lines 9-13: `if player in 18_01 and flag 11810002 OFF → Play Cutscene and Warp (180101, PlaybackMethod 8 skippable-with-fade, point 1812011)`, set **11810002** |
| 4 | Respawn point set to **1812900** (spawn point at region "game start point" 1812100, (-13.75, 184.75, -44.50), the cell) | event 0 line 13 |
| 5 | NG+ (game cycle ≥ 1, flag 705 OFF): sets 705, sets **50000082** (Estus already owned → Oscar does not give Estus again), respawn 1812900, clears Kiln flags 11807200/210/220/240 | event 50 lines 0-10 |
| 6 | Status message 10010600 "In Lordran, level up and kindle at bonfires" is shown once flag 11810000 (arrived in Lordran) is set | common.emevd event 260 (slot 0, params 11810000, 10010600, 0) |

MSB players: `c0000_0000` (no entity) and `c0000_0001` (entity 1810998), both at (-13.75, 184.75, -44.50) rot 180.

Porting notes (opening): DS2 has its own Things Betwixt start; the DS1 start needs: a "new DS1 game" entry (or a Majula/TB → Asylum warp), the corpse-drop cutscene (remo `scn180101`) or a fade replacement, the class → class-item table below (DS2 class ids differ), and the Estus flag (DS2 Estus is a different item; see translation rules §6).

---

## 1. Northern Undead Asylum — first visit (`m18_01_00_00`)

**Getting here:** new game (cell). **Exit:** Big Pilgrim's Key door → cliff → crow cutscene to Firelink (event 11810000). **Return:** from the nest on the cliff above Firelink (m10_02 event 11020020/11020021) → see §2.
**Prerequisites:** none. Keys found inside: Dungeon Cell Key (cell corpse), Undead Asylum F2 East Key (Oscar), Big Pilgrim's Key (Asylum Demon, or Oscar if the demon was killed early), Undead Asylum F2 West Key (only from Firelink, `m10_02` corpse 1020210).

Coordinates: Y up; the cell is at Y≈185, courtyard/hall at Y≈196-198, 2F at Y≈202, 3F/balcony at Y≈208-210, Stray Demon pit at Y≈182, cliff at Y≈210 Z≈-96. MapOffset event: (0, 200, 0).

### 1.1 Progression walkthrough

1. **Cell** (region 1812010 "first cell", box 5×5×5 at (-13.85, 184.5, -45.15)). Corpse `o0500_0000` (entity 1811600) holds the **Dungeon Cell Key** (lot 1810000, flag 51810000). Cell door `o8540_0003` (1811103, ObjAct 11810103, ObjActParam 8540: needs goods **2010** Dungeon Cell Key, "Used Dungeon Cell Key" 10010869). Tutorial message 9000 (camera) in the cell.
2. **Basement corridor** (X≈-14, Z -44 → -8, Y≈185). Three more cell doors `o8540_0000/0001/0002` (1811100-1811102) **never open** (event 0 disables their ObjAct). Passive Hollows behind/among them (§1.4). Messages 9001 (menu), 9002/9003 (attack/strong attack), 9004 (dash). Torches (SFX 530 ×28 across the map).
3. **Water room / ladder room** (region "damp small room 2" (-7.58, 188.18, 21.9)). Message 9006 (lock-on) at (-14.53, 190.28, 17.33), Hollow `c2500_0001` sitting (anim 9000). **Ladder `o8500_0000`** (1811140, registered in event 0 with flags 11810010/11810011) up to the courtyard. Note: `docs/asylum-acceptance.md` row 4 names o8501; the courtyard ladder is **o8500**; o8501 is the Stray Demon pit ladder.
4. **Courtyard + bonfire 1** (`o0200_0000`, entity 1811960). Messages 9019 (rest = recover HP) and 9005 (backstep/roll).
5. **Big double doors `o8541_0001`** (1811111, ObjAct 11810111, ObjActParam 8541 "Church door (large)", no key). Pushing them sets 11810112. Entering the hall region **1812996** → events **11810310** (demon drop) + **11810311** (door shuts behind, anim 3; escape gate `o8542_0000` 1811115 opens, anim 1). Message 8100 "Get away!" (entity 1813210) in the hall.
6. **Run left (east, +X) through the gate**; stepping on collision **h0006B1** (1813120, the stairs past the gate) → **11810312**: gate shuts (anim 3, temporary blocking hit h0550B1_0000 1813121 enabled during the close), respawn point → 1812961, **save**. Hall door's ObjAct disabled.
   - Optional: fight the demon now (event 11810300 records an **early kill**: before passing the courtyard fog, flag 11810090 OFF) → Demon's Great Hammer (§1.5).
7. **Stairs down → bonfire 2** (`o0200_0001`, 1811961, at (33.34, 193.15, -26.47)).
8. **East hallway** (X≈37-39, Z -26 → +21). Message 8101 "Get your Shield!" (37.15, 192.45, -21.25); **class shield corpse** in the cell on the left (37.61, 192.66, -11.85), messages 9007 (guard) and 9001; **archer Hollow** `c2500_0011` (39.00, 194.43, 7.14): when the player enters logic region 1812101 (39.0, 192.15, -9.93) it flees to region 1812102 (10.85, 201.64, 19.57) (think 250021); **class weapon corpse** at the end (38.60, 195.42, 17.00), message 9011 (switch weapons).
9. **Fog gate `o8952_0000`** (1811700, SFX 1811701 effect 81805) at (5.87, 201.78, 19.63): event **11810090** — two action buttons on a line segment: from region 1812600 "before" (text 10010403 "Traverse the white light") and from 1812601 "behind" (text 10010407 "Enter through the white light"); player anim **7410**; once used, the fog and SFX are removed for good (event flag of slot 0).
10. **2F west** (Y≈202): message 9008 (use item, entity 1813220) and 9009 (rest at bonfire = Estus, 1813221) appear only after Oscar gave Estus (11810590 or 50000082; event 11810450); message 9010 (parry) at (13.0, 208.43, -36.5) later.
11. **Stairs up: the boulder.** Entering region **1812120** (-8.0, 203.22, 7.17) → event **11810211**: Hollow `c2500_0002` (think 250004) stands at the top; boulder `o8530_0000` (1811215) rolls down (anim 0, damaging object behaviour 5110, radius 0.8, lifespan 2.5 s, flag 11810212), ends as `o8530_0001` (1811216, anim 0 = rest pose at the bottom) + rubble `o8531_0000` (1811217). It smashes the **breakable wall `o8520_0000`** (1811210, hp 1): event **11810200** (slot 1) swaps it for the broken `o8520_0001` (1811211, destruction anim) and removes `o8521_0000` (1811212). The wall can also be broken by attacks.
12. **Oscar's room** (behind the wall, (-4.91, 201.96, 21.49)): talk → 5× **Estus Flask** + **Undead Asylum F2 East Key** (§1.6). Region 1812121 "NPC dies" (-9.4, 200, 9.17).
13. **F2 East door `o8540_06`** (1811106, ObjAct 11810106, param 8537 needs goods **2012**, text 10010871) at the top of the stairs (-8.60, 208.43, -8.65).
14. **3F / balcony corridor** (Y≈208): class catalyst/talisman/flame/bow corpse at (-10.27, 208.62, -32.52) (messages 9012-9015/9024 for the class), messages 9026 (kick/jump attack), 9028 (two-hand), 9027 (critical), 9025 "Resting at bonfire revives enemies". Hollows `c2500_0003` (archer), `c2500_0009`, `c2500_0010`; **Undead (Hollow) Soldier `c2550_0000`** guarding the **F2 West door `o8540_0004`** (1811104, param 8539 needs goods **2016** F2 West Key, text 10010875) at (14.99, 208.41, -21.6) — not openable on the first visit unless the player went to Firelink and came back.
15. **Boss fog `o8950_0000`** (1811990, SFX 1811991 effect 81802, region "light wall before boss") at (3.16, 209.74, -34.26) → **balcony above the hall**; message 8102 "Plunging Attack" (4.28, 208.93, -36.53). Event 11815390: action button on region 1812998 (line segment = the fog object), text 10010403; warps the demon to **1812301** (3.0, 197.98, -27.0, the plunge target below the balcony), rotates the player to 1812997, player anim 7410, hides message 1813210, shuts gate 1811115 (anim 3) and, if the hall door was pushed open from outside (11810112), shuts it (anim 3).
16. **Asylum Demon fight** (§1.5). Kill → flag **16**; boss fog removed; hall door + gate open (anim 1); hall door ObjAct disabled.
17. **Far door `o8544_0000`** (1811110, ObjAct 11810110, param 8544 "Church door (small)" needs goods **2011** Big Pilgrim's Key; text 10010870 "Used Big Pilgrim's Key"; stays open once used, flag 11810110). Message 8103 "Good job. Go straight ahead." (1.74, 197.24, -46.46).
18. **Cliff path**: Soul of a Lost Undead corpse `o0500_0007` (12.75, 199.23, -58.95) on the left; **Snuggly's nest** `o8550_0001` (-11.20, 200.72, -69.02) on the right (§1.7); hidden message 8110 "Exchange" (on h0052B1, region "magic blood writing 1" (-5.6, 201.08, -70.6)).
19. **Cliff edge** region **1812000** (box 5×5×10 at (3.5, 210, -96)) → event **11810000**: removes the demon's backread, cutscene **180125** (crow grabs you) then **100225** (Firelink arrival) warping to **1022100** (Firelink start point) in `m10_02`; sets **11810000**; respawn → **1022960** (Firelink); save; **Achievement 28**.

Shortcut: **inner-key door `o8540_0005`** (1811105, ObjActParam 8538 "inner key": requires SpEffect **4150** "Inside Key of Universal door") at (-9.34, 196.06, -1.41) between the 2F west stairs room and the courtyard. Event **11810120** applies SpEffect 4150 for 3 s while the player stands in region **1812020** (inside, west, (-10.7, 195.5, -0.75)), or in 1812021 (outside) once 11810105 is set → opens only from the inside first. After it is opened (61810105) and the gate has shut behind the player (11810312), the courtyard hall door says **"Locked by some contraption"** (10010160) (event **11810313**), and event 0 disables its ObjAct. Before that, entering the hall again re-runs 11810311 (11810315 is cleared every load).

Vestigial regions (no EMEVD reference found): 1812311 "hammer demon falls to basement" (3.1, 195, -22), 1812302 "demon landed point 2" (in the pit, (3.0, 182.41, -22.65)), 1812320 "boss room escape" (16.5, 198, -25.6), 1812351/1812352 "player on indoor balcony 1/2". Event 11815380 (Stray Demon fog action button, text 10010403, region 1812898) and 11815090 (black-world-tendency character 1810900, which has no MSB part) exist but are never initialised.

### 1.2 Bonfires

| Bonfire | Object part | Entity | Flag | Kindling | Spawn point | Notes |
|---|---|---|---|---|---|---|
| Courtyard | `o0200_0000` (3.10, 195.91, 7.44) | 1811960 | 11810992 | standard level 0 | 1812960 (region "bonfire respawn 00" (3.10, 195.96, 5.75)) | bonfire talk dummy `c1000_0000` (1810960, NpcParam 100000 "Bonfire", talk **181000**) |
| Stairs past the gate | `o0200_0001` (33.34, 193.15, -26.47) | 1811961 | 11810984 | 0 | 1812961 (33.90, 193.15, -25.20) set by 11810312 | talk dummy `c1000_0001` (1810961, talk **181001**); collisions h0006B1/h0007B1 carry `DisableBonfireEntityID=1811961` |
| (start) | — | — | — | — | 1812900 "game start point" | cell |

No Fire Keeper in the Asylum (no kindling possible: standard kindling 0).

### 1.3 Map collisions with scripted roles
- h0006B1 (1813120): stairs past the gate, trigger for 11810312.
- h0550B1_0000 (1813121): extra hit enabled while the gate closes (prevents clipping).
- h0050B1-h0054B1 (1813100-1813104), h0091B1 (1813105): cliff/nest collisions; h0052B1 = Snuggly item hitbox (1813102).

### 1.4 Enemies (first visit)

All normal enemies respawn on rest unless noted. "Init anim" = MSB `InitAnimID` (idle/pose the character starts in).

| Sub-zone (route order) | Enemy | Model | Part(s) | Entity | NpcParam / Think | Init anim | Behaviour / notes | Drops |
|---|---|---|---|---|---|---|---|---|
| Basement corridor | Hollow (unarmed, passive) | c2500 | c2500_0000 (-14.67, 185.31, -23.07) | 1810101 | 250020 / 250006 | 9005 | passive "practice" Hollow; patrol points "巡回 不死人00-00/01" exist (-13.8,185.6,-11.4)/(-14.4,185.5,-17.4) but are not linked in the MSB | lot 25002000 (none); 20 souls, 44 HP |
| Basement corridor | Hollow (passive) | c2500 | c2500_0005 (-15.00, 185.55, -11.33) | 1810106 | 250020 / 250006 | 9006 | passive | as above |
| Basement cell (behind never-opening door 0002) | Hollow | c2500 | c2500_0006 (-8.09, 184.76, -37.92) | 1810107 | 250020 / 250006 | 9008 | caged; CharaInit 0 | as above |
| Basement cell (behind door 0001) | Hollow | c2500 | c2500_0007 (-19.66, 184.75, -34.81) | 1810108 | 250020 / 250006 | 9010 | caged; CharaInit 0 | as above |
| Ladder room | Hollow | c2500 | c2500_0001 (-14.80, 189.07, 23.66) | 1810102 | 250020 / 250006 | 9000 | sitting/slumped | as above |
| Hall (boss) | **Asylum Demon** | c2232 | c2232_0000 (3.00, 198.06, -29.54), warped to the air at load | 1810800 | 223200 / 223200 | — | boss, §1.5 | §1.5 |
| East hallway | Hollow archer | c2500 | c2500_0011 (39.00, 194.43, 7.14) | 1810112 | 250021 / 250021 | — | flees to 1812102 when the player reaches 1812101 | lot 25002100 Short Bow; 69 HP |
| 2F stairs top | Hollow (boulder pusher) | c2500 | c2500_0002 (-7.76, 208.43, -5.00) | 1810103 | 250022 / 250004 | — | stands by the boulder; broken sword | lot 25002200 Broken Straight Sword |
| 3F balcony corridor | Hollow archer | c2500 | c2500_0003 (5.68, 208.93, -36.76) | 1810104 | 250021 / 250020 | — | bow | Short Bow |
| 3F balcony corridor | Hollow (sword) | c2500 | c2500_0009 (-3.65, 208.43, -35.54), c2500_0010 (0.55, 208.93, -36.46) | 1810110, 1810111 | 250022 / 250005 | — | broken swords | Broken Straight Sword |
| 3F by F2 West door | Undead/Hollow Soldier (sword+shield) | c2550 | c2550_0000 (15.58, 208.43, -23.14) | 1810113 | 255030 / 255005 | — | guards the F2 West door | lot 25503000: Longsword + Hollow Soldier Shield, chained 25503001-04 Hollow Soldier Helm/Armor/Waistcloth, Titanite Shard (lot-chain); 85 HP, 80 souls |
| Online only | Vagrant (passive) | c3490 | c3490_0000 (14.62, 208.43, -1.15) | 1813900 | 349006 / 349000 | — | online vagrant slot | — |
| Online only | Vagrant (aggressive) ×2 | c3491 | c3491_0000, c3491_0001 (3.14, 210.11, -34.71) | 1813901, 1813902 | 349106, 349206 / 349100 | — | online vagrant slot | lots 34910600 / 34920600 (Evil / Humanity level 7) |
| Return trip only | see §2.2 | | | | | | | |

First-visit Hollows/soldier 1810101-1810113 are **disabled for good once 11810000 is set** (event 11810350).

### 1.5 Boss: Asylum Demon

| Field | Value |
|---|---|
| Model / part / entity | c2232 / c2232_0000 / **1810800**; NpcParam 223200 (HP 813, SpEffect 5300), ThinkParam 223200 (Lua battle goal 223200 in `script\m18_01_00_00.luabnd.dcx`) |
| Arena | the hall: BGM region 1812990 (box 25×10×29.4 at (3.1, 197, -18.7)), battle start 1812996 (25×8×20 at (3.1, 197, -23)), balcony 1812350 (11×8×10 at (3.2, 206.8, -28.5)); home region after landing 1812300 (3.0, 197.98, -29.54) |
| Fog walls | boss fog `o8950_0000` **1811990** (+SFX 1811991): disabled until the demon has landed, then on; courtyard→east escape gate `o8542` 1811115 and hall door 1811111 act as the "doors" of the first encounter |
| Intro (11810310) | at load (if not yet triggered and 16 OFF): fog off, HP bar off, AI off, special waiting anim **9000**, warped to **1812305** "mini-fatty air placement point" (3.59, **226.15**, -36.62). When the player enters 1812996: flag 11810314, SpEffect **4160** "falling control for enemy", clear special anims, force anim **9060** (drop/land, wait for completion), clear 4160, home = 1812300, **boss fog on**. 11810310/11810314 are reset each load until 11810312 is set (i.e. until the player escaped through the gate) |
| AI wake (11815392) | AI on when the player is in 1812996 (first drop) or in 1812990 after the drop, or after 11815393 |
| Entry (11815393) | first time in 1812996/1812350: boss-room entry notification + multiplayer buffs |
| HP bar / BGM (11815394/5) | on when 11810310 & 11815392 and player in 1812990 or 1812350: map sound 1813800 (BGM 180100001, region "hammer demon BGM") on, boss bar NameID **2232** "Asylum Demon"; off when the player leaves 1812990 (and hasn't entered the balcony fog) or the demon dies |
| Plunge setup (11815390) | entering the balcony fog: demon warped to 1812301 under the balcony (plunging-attack target), see walkthrough step 15 |
| Mechanics (wiki) | hammer swings, butt slam (jump + body slam), ground pound; weak to the plunging attack from the balcony; can be fought at the first encounter (escape optional) |
| Death (11810001) | HP ≤ 0 → sound 777777777; dead → **flag 16**, Handle Boss Defeat, boss fog + SFX off, hall door anim 1 (open), gate anim 1 if the player had passed it, hall door ObjAct off |
| Rewards | common.emevd event 970 slot 21: flag 16 → item lot **2660** (Big Pilgrim's Key, flag 50001660; chained lot 2661 Demon's Great Hammer, flag 50001661) and after 5 s lot **9000** (Humanity ×1). Souls: 2000 (wiki; NpcParam getSoul = 0 in DSR, souls come from elsewhere, unverified) |
| Early-kill rule (11810300) | on the first load sets 50000081 (Oscar's BPK lot already "taken") and 50001661 (hammer "taken"). If the demon dies while 16 OFF **and 11810090 OFF** (courtyard fog not yet passed): clears 50000081 and 50001661, sets 50001660 → the demon drops **Demon's Great Hammer** instead of the key, and Oscar hands over the Big Pilgrim's Key (lot 1081) |
| World after the kill | hall door + gate open; boss fog gone; on later loads (event 0) the demon is disabled + killed, fog/SFX removed, hall door and gate play anim 1 (open) |

### 1.6 NPCs (first visit)

| NPC | Part / entity | Params | Where | Init anim / script anims | What they do |
|---|---|---|---|---|---|
| **Oscar, Knight of Astora** | `c0000_0002` / **6023** | NpcParam 6020, Think 6020, CharaInit 6020 (Astora's Straight Sword +8, Crest Shield +5, Elite Knight helm/armor/gauntlets/leggings +6), talk **181602** (`t181602.esd`) | behind the boulder-broken wall, (-4.91, 201.96, 21.49) rot 20 | **7805** (sitting, dying against the wall); event 11815010: SpEffect **5492** "Tutorial NPC Dying", HP bar hidden 5 frames | Talk → common 911 slot 8: flag 11810590 → lot **1082** Estus Flask ×5 (flag 50000082); slot 9: 11810591 → lot **1080** Undead Asylum F2 East Key (flag 50000080; chained lot 1081 Big Pilgrim's Key, flag 50000081, only given after an early demon kill). After talking (1060 and any of 11810590/91/92) and the player walks into region 1812121 or the boss arena 1812990 → event **11810530** force-kills him (souls given). Killed by the player: NpcParam lot 6020 → F2 East Key + Big Pilgrim's Key + Estus (chain 6020-6022, same flags, so nothing is duplicated). Death (11810520): flags 1060-1074 cleared, **1073** set; later loads: corpse loot (Force Character Treasure). Humanity value base flag 8326 |
| Oscar, hollowed | `c0000_0003` / **6024** | NpcParam 6021 (lot **7020** Crest Shield, 1000 souls, SpEffect 5446), CharaInit 6021, no talk | (-3.64, 201.41, 19.60), same room | — | disabled unless flag **1061**; team "Hostile Ally". Event **11810531**: once 1073 (Oscar dead) and 11810000 (left the Asylum) → flags 1060-1074 off, **1061** on, enabled → hostile hollow Oscar on the return trip. 11810532: killed in m18_01 → 1062; corpse loot afterwards |
| Snuggly the Crow | `c1000_0002` / **6450** | NpcParam 100000, talk **181645** (`t181645.esd`) | nest (-11.48, 200.71, -69.19) | invisible talk dummy | trading, §1.7 |

Talk ESDs in `m18_01_00_00.talkesdbnd.dcx`: t181600 (stub), t181602 (Oscar), t181622 (stub), t181645 (Snuggly), t181000/t181001 (bonfires).

### 1.7 Snuggly trades (nest)

Mechanism: event **11810641** (22 slots, host only) watches region **1812200** (nest, box 2×1×2.5 at (-11.33, 200.5, -69.18)) for dropped items. Matching item & trade not done → 11815200 + `Set Snuggly Next Trade`; already done → 11815202; any other item → 11815201. Event 11810600 turns those into talk-state flags 11815100/101/102 (and clears 71810092/93). Event **11815110** spawns the reward lot at region **1812201** (-11.43, 200.80, -69.18) with hitbox 1813102 (h0052B1), once per trade-done flag 51810800…51811000.

| Give (type id) | Get (lot) | Done flag |
|---|---|---|
| Cracked Red Eye Orb (goods 111) | Purging Stone (3000) | 51810800 |
| Bloodred Moss Clump (270) | Twinkling Titanite (3010) | 51810810 |
| Purple Moss Clump (271) | Twinkling Titanite (3020) | 51810820 |
| Blooming Purple Moss Clump (272) | Twinkling Titanite (3030) | 51810830 |
| Egg Vermifuge (275) | Dragon Scale (3040) | 51810840 |
| Dung Pie (293) | Demon Titanite (3050) | 51810850 |
| Humanity (goods 350 or 500) | Ring of Sacrifice (3060) | 51810860 |
| Prism Stone (370) | Demon Titanite (3070) | 51810870 |
| Sunlight Medal (375) | White Titanite Chunk (3080) | 51810880 |
| Pendant (376) | Souvenir of Reprisal (3090) | 51810890 |
| Rubbish (380) | Titanite Chunk (3100) | 51810900 |
| Dried Finger (385) | Blue Titanite Chunk (3110) | 51810910 |
| Twin Humanities (501) | Rare Ring of Sacrifice (3120) | 51810920 |
| Pyromancy Flame (weapon 1330000) | Red Titanite Chunk (3130) | 51810930 |
| Pyromancy Flame ascended (1332000) | Red Titanite Slab (3140) | 51810940 |
| Skull Lantern (1396000) | Ring of Fog (3150) | 51810950 |
| Sunlight Maggot (armor 190000) | Old Witch's Ring (3160) | 51810960 |
| Xanthous Crown (armor 290000) | Ring of Favor and Protection (3170) | 51810970 |
| Sack (armor 560000) | Demon's Great Hammer (3180) | 51810980 |
| Ring of the Sun Princess (ring 130) | Divine Blessing (3190) | 51810990 |
| Soul of Manus (goods 711) | Pursuers (3200) | 51811000 |

### 1.8 Items / treasures (route order)

| Where | Part | Item (lot, flag) | Notes |
|---|---|---|---|
| Start cell floor | o0500_0000 (-13.60, 184.74, -45.24), entity 1811600 | **Dungeon Cell Key** (1810000, 51810000) | corpse removed once 11810000 (return trip) |
| East hallway cell (left) | o0500_0023…0032 (37.61, 192.66, -11.85), entities 1811602/604/606/608/610/612/615/618/621/624 | class **shield** (one per class) | event 11810400 enables only the player's class set |
| East hallway end | o0500_0033…0042 (38.60, 195.42, 17.00), entities 1811601/603/605/607/609/611/614/617/620/623 | class **weapon** | same |
| 3F balcony corridor west | o0500_0043…0046 (-10.27, 208.62, -32.52), entities 1811613/616/619/622 | Hunter: Short Bow (+30 Standard Arrows lot 1810221); Sorcerer: Sorcerer's Catalyst; Pyromancer: Pyromancy Flame; Cleric: Canvas Talisman | only these four classes |
| Behind F2 West door | o0500_0006 (14.45, 205.96, 4.60) | **Rusted Iron Ring** (1810060, 51810060) | needs the F2 West Key from Firelink |
| Cliff path, left | o0500_0007 (12.75, 199.23, -58.95) | Soul of a Lost Undead (1810070) | |
| Start cell (return trip only) | o0500_0008 (-13.65, 184.94, -47.26), entity 1811650, StartDisabled | **Peculiar Doll** (1810080, 51810080) | enabled by 11810350 once 11810000 |
| Dead treasure events | lots 1810020, 1810030 (no part), 1810040 on missing part o0500_0004, 1810050 on missing part o0500_0005 | lots do not exist in DSR | ignore |

Class → item table (event 11810400, slots 0-9 = class index; message flags enable the class's tutorial messages):

| Class (index) | Weapon (lot) | Shield (lot) | 3rd item (lot) | Extra messages |
|---|---|---|---|---|
| Warrior (0) | Longsword (1810100) | Heater Shield (1810110) | — | — |
| Knight (1) | Broadsword (1810120) | Tower Kite Shield (1810130) | — | — |
| Wanderer (2) | Scimitar (1810140) | Leather Shield (1810150) | — | — |
| Thief (3) | Bandit's Knife (1810160) | Target Shield (1810170) | — | — |
| Bandit (4) | Battle Axe (1810180) | Spider Shield (1810190) | — | — |
| Hunter (5) | Shortsword (1810200) | Large Leather Shield (1810210) | Short Bow (1810220) + Standard Arrow ×30 (1810221) | 9014, 9015 (bow) |
| Sorcerer (6) | Dagger (1810230) | Small Leather Shield (1810240) | Sorcerer's Catalyst (1810250) | 9012 |
| Pyromancer (7) | Hand Axe (1810260) | Cracked Round Shield (1810270) | Pyromancy Flame (1810280) | 9013 |
| Cleric (8) | Mace (1810290) | East-West Shield (1810300) | Canvas Talisman (1810310) | 9024 |
| Deprived (9) | Club (1810320) | Plank Shield (1810330) | — | — |

Key items obtained elsewhere in the area: Estus Flask ×5, F2 East Key, Big Pilgrim's Key (Oscar / demon), Demon's Great Hammer + Humanity (early kill), Humanity (demon kill, lot 9000).

### 1.9 Interactive objects & scripted events (summary)

| Object | Part / entity | What it does | Events |
|---|---|---|---|
| Cell doors (never open) | o8540_0000/01/02 (1811100-02), ObjAct 11810100-02 | ObjAct disabled at load | 0 |
| Start cell door | o8540_0003 (1811103), ObjAct 11810103 | Dungeon Cell Key 2010; anims a00_0000 open, 0001 close, 0002 fast open | 11810100 slot 3 (ObjAct flag → flag set, "Used …" dialog 10010869, ObjAct off after 2 s) |
| F2 West door | o8540_0004 (1811104), ObjAct 11810104 | key 2016 | 11810100 slot 4 (10010875) |
| Inner-key door | o8540_0005 (1811105), ObjAct 11810105 | SpEffect 4150 from inside | 11810100 slot 5, 11810120 |
| F2 East door | o8540_06 (1811106), ObjAct 11810106 | key 2012 | 11810100 slot 6 (10010871) |
| Hall door (double) | o8541_0001 (1811111), ObjAct 11810111 | push open; anims 0000 closed, 0001 open (6.7 s), 0002 open static, 0003 close | 11810111, 11810311, 11810313, 11815390, 11810001, 0 |
| Escape gate | o8542_0000 (1811115) | anim 1 rise 1.83 m, anim 3 lower | 11810311, 11810312, 11815390, 11810001, 0 |
| Far door | o8544_0000 (1811110), ObjAct 11810110 | Big Pilgrim's Key 2011 | 11810110 |
| Boulder | o8530_0000 (1811215) → o8530_0001 (1811216) + o8531 (1811217) | rolls 18 m, damaging | 11810211 |
| Breakable wall | o8520_0000 (1811210) / o8520_0001 (1811211) / o8521 (1811212) | breaks open Oscar's room | 11810200 slot 1 |
| Hall floor centre | o8510_0000 (1811200, "Church floor", hp 999), fragments o8510_0001 (1811201), o8511_0000 (1811202) | collapses on the return trip | 11810220 |
| Ladders | o8500_0000 (1811140) courtyard; o8501_0000 (1811141) Stray Demon pit | pit ladder registered only after the Stray Demon dies | 0, 11815382, 11810900 |
| Fogs | o8952 (1811700) courtyard; o8950 (1811990) boss; o8953 (1811890) Stray Demon pit | | 11810090; 11810310/11815390/11810001; 11815382/11810900 |
| Nest at the cliff | o8550 (1811300) "church door" model at (2.74, 212.68, -95.83) = return nest | enabled only after 11810000 | 11810150 |
| Snuggly nest | o8550_0001 (-11.20, 200.72, -69.02) | static | 11810641 |
| Breakables | o8400 ×2 tubs, o8403 ×2 urns (player-collision break), o8401/o8402 bags (cell area); o8410 ×43 urns, o8411 ×4 pots (hall); o8700/o8701/o8702 "church collapse" debris (balcony); o8600 ×10 "church pillar" (1811230-39, hall sides, hp 1, unscripted); o8610 ×18 "sewage column" (pit) | | none |
| Statues | o8420/8421/8422/8425 gargoyle statues (hall, balcony) | static | |
| Unknown | o8549_0000 (-14.82, 180.00, -46.74) (no ObjectParam row) | static under the cell | |

Messages (MSB, BloodMsg FMG ids): 9000 camera, 9001 menu/equipment, 9002 attack, 9003 strong attack, 9019 rest = HP, 9004 dash, 9005 backstep/roll, 9006 lock-on, 9007 guard, 9008 use item (1813220), 9009 rest = Estus (1813221), 9010 parry/riposte, 9011 switch weapons, 9025 bonfire revives enemies, 8100 "Get away!" (1813210), 8101 "Get your Shield!", 8102 plunging attack, 8103 "Good job. Go straight ahead.", class messages 9012/9013/9014/9015/9024 (1813200-1813204), 9026 kick/jump attack, 9027 critical hit, 9028 two-hand, hidden 8110 "Exchange" (nest).

Sounds/SFX: BGM 180100001 (Asylum Demon, entity 1813800), 180100002 (Stray Demon, 1813801); ambience 180000002 wind (cliff), 180000003 damp rooms ×3, 180000004 wind corridor, 180100001 "flies" at the cell; SFX 530 torches ×28; fog SFX 81802/81805/81806; floor collapse SFX 180100 + sound 851000000.

Cutscenes (remo): 180101 opening, 180125 crow pick-up, 180126 return nest (see §2), 180130 arrival from Firelink.

### 1.10 Porting notes (Asylum)
- Most of the first visit is already partly built (see `docs/asylum-acceptance.md`, `Build/Maps/AsylumScript.cs`): hall door, gate, fogs, cell/key doors, boulder. Still to do: demon intro (air placement + drop anim 9060 + SpEffect 4160), plunge warp 1812301, early-kill flag swap (50000081/50001660/50001661), boss rewards via BossBattleParam + extra lots (Humanity), hall-floor collapse, Oscar (talk, dying pose 7805, forced death after talk), Snuggly (DS2 has no drop-item trading: needs a custom ESD loop watching dropped items or a talk menu "Offer item"), crow cutscenes.
- Class-dependent corpses need a DS2 equivalent of `IF Player's Class` (DS2 class ids differ; map DS1 index 0-9 to DS2 or store the DS1 class in a flag at character creation).
- Vagrants (c3490/c3491) are an online feature; drop or keep as dormant placeholders.
- Inner-key door uses a SpEffect-gated ObjAct (4150 for 3 s); DS2 key-door helper 131602 checks items, not SpEffects: use a side test (player in region) instead.
- "Locked by some contraption" (10010160) after the shortcut is open: generic dialog on the hall door.
- Stairs everywhere need the invisible-ramp rule (user directive).

### 1.11 Area completion checklist (first visit)
- [ ] Opening movie + character creation hook; cutscene 180101 (or fade) + warp to 1812011; respawn 1812900; NG+ Estus flag 50000082.
- [ ] Cell key corpse; cell door with key 2010; never-opening cell doors 0000-0002.
- [ ] 5 passive Hollows with sit/crouch poses 9000/9005/9006/9008/9010.
- [ ] Ladder o8500; bonfire 1 (+ talk dummy 181000) and bonfire 2 (181001), spawn points 1812960/1812961.
- [ ] Hall door push/shut, demon drop (9000 → 9060, air point 1812305), gate open/shut, save on h0006B1.
- [ ] Optional early kill → hammer + Oscar gives BPK.
- [ ] Archer Hollow flee logic (1812101 → 1812102).
- [ ] Class shield/weapon/3rd-item corpses + class messages.
- [ ] Courtyard fog o8952 (one-time traverse both ways).
- [ ] Boulder roll (damaging) + breakable wall; Oscar talk/death/loot; Estus ×5 + F2 East Key.
- [ ] F2 East door (2012), inner-key shortcut door (SpEffect 4150), "Locked by some contraption".
- [ ] 3F enemies (2 sword Hollows, archer, Hollow Soldier), F2 West door (2016) + Rusted Iron Ring.
- [ ] Boss fog, plunge warp, HP bar 2232, BGM 180100001, death → flag 16, lots 2660(+2661) + 9000, doors open.
- [ ] Big Pilgrim's Key door; Soul of a Lost Undead; Snuggly nest + all 21 trades.
- [ ] Cliff region 1812000 → crow cutscenes 180125/100225 → Firelink 1022100, flag 11810000, respawn 1022960, achievement 28.
- [ ] All 27 tutorial/hint messages.

---

## 2. Northern Undead Asylum — return trip (`m18_01_00_00`, flag 11810000 ON)

**Getting here:** Firelink nest on the cliff above the shrine (region **1022120**, (-43.36, -31.35, 101.13)) → "Curl up like a ball" (10010506) → player anims 7815 → waiting 7816; after 20 s (skipped once 11020000 has been seen) cutscene **100230** + **180130**, warp to region **1812110** (3.27, 209.84, -83.48) on the Asylum cliff; flag 11020000. "Stand up" (10010507) or leaving the region cancels (anim 7817). Events m10_02 11020020/11020021.
**Leaving:** the nest `o8550` (1811300) at the cliff tip is enabled once 11810000; region **1812001** (2×2×2 at (2.67, 212, -96.18)): "Curl up like a ball" → anims 7815/7816 → (singleplayer only) cutscene **180126** warp to region **1022121** (-43.56, -30.85, 100.85) in Firelink + cutscene **100226**; "Stand up"/leave → anim 7817 (event 11810150).
**Prerequisites:** none (the Firelink nest is reachable any time; wiki: on the cliff above the shrine near the Parish elevator).

### 2.1 Progression
1. Land at the cliff → walk back through the open Big Pilgrim's door (11810110 keeps it open).
2. Return-trip Hollows on the path and in the hall (§2.2).
3. **Hall floor collapse**: standing in sphere region **1812400** (r 2.5 at (3.13, 197.36, -22.11)) as host → event **11810220**: floor o8510_0000 disabled, fragments o8510_0001 enabled and destroyed (slot 1), o8511 removed, sound 851000000, SFX 180100 → fall into the **Stray Demon pit**. On every later load the floor is restored (`Request Object Restoration` ×3 and flag reset) → it can collapse again.
4. **Stray Demon** in the pit (§2.3). Exit: ladder `o8501_0000` (1811141) registered only after the kill; fog `o8953` (1811890) blocks it during the fight.
5. The boulder is re-armed once for the return trip (11810350 clears 11810211 when 11810000 is set while in the map) → it rolls again on the 2F stairs.
6. Start cell: Peculiar Doll corpse; Black Knight in the basement corridor.
7. East hallway end: second Black Knight.
8. Hollowed Oscar in Oscar's room (if 1061).
9. Leave via the cliff nest (above).

### 2.2 Enemies (return trip; all enabled by event 11810350 when 11810000)

| Sub-zone | Enemy | Model | Part(s) | Entity | NpcParam / Think | Notes / drops |
|---|---|---|---|---|---|---|
| Cliff path beyond the far door (Z -40…-49) | Hollow ×4 | c2500 | c2500_0012 (1.49, 197.46, -49.49), _0013 (4.40, 197.29, -47.05), _0014 (1.68, 197.03, -42.72), _0015 (4.36, 198.15, -40.02) | 1810201-1810204 | 250023 / 250010 | 53 HP, 100 souls; lot 25002300 (none) |
| Hall | Hollow ×2 | c2500 | c2500_0016 (-6.08, 198.05, -10.27), c2500_0017 (9.64, 198.15, -7.64) | 1810205, 1810206 | 250025 / 250000 | lot 25002500 Broken Straight Sword |
| 2F stairs top (boulder) | Hollow | c2500 | c2500_0018 (-7.76, 208.43, -5.00) | 1810207 | 250025 / 250004 | replaces c2500_0002 |
| 3F balcony corridor | Hollow archer | c2500 | c2500_0019 (5.68, 208.93, -36.76) | 1810208 | 250024 / 250020 | lot 25002400 Short Bow |
| 3F balcony corridor | Hollow ×2 | c2500 | c2500_0020 (-3.65, 208.43, -35.54), c2500_0021 (0.55, 208.93, -36.46) | 1810209, 1810210 | 250025 / 250005 | Broken Straight Sword |
| 3F by F2 West door | Hollow Soldier (sword) | c2550 | c2550_0001 (15.58, 208.43, -23.14) | 1810211 | 255031 / 255005 | lot 25503100 Longsword + Hollow Soldier Shield, chain (armor, Titanite Shard); 150 souls |
| 3F by F2 West door | Hollow Soldier ("Spear" lot name, gives Longsword) | c2550 | c2550_0002 (12.69, 208.43, -22.37) | 1810212 | 255032 / 255011 | lot 25503200 |
| Basement corridor by the start cell | **Black Knight** (greatsword-type model c2792) | c2792 | c2792_0000 (-13.79, 185.25, -41.16) rot 180 | 1810200 | 279070 / 279000 | 355 HP, 1200 souls; **no respawn**: event 11810850 slot 0 disables it once killed and awards lot **27907000** (Black Knight Sword + Black Knight Shield; chained 27907001 Red Titanite Chunk) to living/hollow players |
| East hallway end | **Black Knight** | c2792 | c2792_0001 (38.95, 195.55, 21.11) | 1810213 | 279070 / 279000 | same, event 11810850 slot 1 |
| Stray Demon pit | **Stray Demon** | c2230 | c2230_0000 (3.31, 182.11, -9.46) | 1810810 | 223000 / 223001 | boss §2.3 |
| Oscar's room | Oscar (hollow) | c0000 | c0000_0003 | 6024 | 6021 | §1.6 |

(Vagrant slots as in §1.4.)

### 2.3 Boss: Stray Demon

| Field | Value |
|---|---|
| Model / part / entity | c2230 / c2230_0000 / **1810810**; NpcParam 223000 (HP 2100, SpEffect 5300), Think 223001; patrol points "巡回 デブデーモン00-00/01" (3.33, 182.10, -26.21) and (3.46, 182.10, -9.53) are set as its MovePointNames |
| Arena | pit under the hall: BGM region 1812890 (30×15.2×30 at (4, 182, -19)), battle start 1812896 (30×1.7×30, pit floor), damp-room ambience |
| First visit (11810320) | invincible; when loaded, AI command 10 (dormant); after 11810000: invincibility off, AI command cleared |
| Fight start (11815382) | if not dead: AI off, invincible, no HP bar until the player is in **1812896** → AI on, invincibility off, boss bar NameID **2231** "Stray Demon", fog **o8953** (1811890) + SFX 1811891 (81806) on |
| BGM | 11815384: after 16 & 11815382 & player in 1812890 → map sound 1813801 (180100002) on; 11815385: off after death |
| Death (11810900) | Handle Boss Defeat, fog/SFX off, pit ladder 1811141 registered (flags 11810012/11810013) |
| Rewards | common 970 slot 22 (flag 11810900): lot 9000 Humanity + lot 9030 Homeward Bone; NpcParam lot 22300000 **Titanite Slab** (+ wiki: Soul of the Stray Demon? — not in these lots, unverified); souls 20000 (wiki, unverified in params) |
| Mechanics (wiki) | larger, fire-based Asylum Demon variant: hammer slams with fire AoE, butt slam; the floor collapse drops the player in at low HP risk |

### 2.4 Porting notes (return trip)
- World-state switch on flag 11810000: disable 13 first-visit enemies, enable 14 return-trip enemies + Peculiar Doll corpse, hide the cell key corpse, re-arm the boulder once, enable the nest object.
- Hall floor: a breakable prefab whose break is triggered by a region (not damage) and **restored on every load**.
- Two Black Knights with event-awarded drops (not NpcParam lots) and no respawn.
- Crow travel both ways: DS2 has warps between maps (`132125`); the remo cutscenes 100230/180130/180126/100226 need replacements (fade + warp).

### 2.5 Area completion checklist (return trip)
- [ ] Firelink nest (curl up 7815/7816/7817, 20 s wait, cutscenes) → 1812110.
- [ ] Return-trip enemy set (14) + Black Knights' event drops (27907000).
- [ ] Hall floor collapse region 1812400 (restorable).
- [ ] Stray Demon: dormant/invincible before, fight start on pit floor, fog o8953, HP bar 2231, BGM 180100002, rewards, pit ladder after the kill.
- [ ] Hollowed Oscar (1061) + Crest Shield.
- [ ] Peculiar Doll in the start cell.
- [ ] Cliff nest back to Firelink (180126 → 1022121 → 100226).

---

## 3. Firelink Shrine (`m10_02_00_00`)

**Getting here:** first time by the crow from the Asylum (lands at **1022100** start point (-47.41, -59.82, 53.30) beside the bonfire). **Exits:**
| Exit | Leads to | Gate |
|---|---|---|
| Stone stairs + aqueduct (south-east, up) | Undead Burg (`m10_01`), arriving below the first Burg bonfire by the male Undead Merchant | open |
| Parish elevator `o1460` (1021000) at (-7.22, -44.54, 108.07), shaft to Y +28 | Undead Parish (`m10_01`) | locked until first used from the top (§3.5) |
| Graveyard (north, down) | Catacombs (`m13_00`) | open (skeletons) |
| Stairs past the Fire Keeper cell (down) | New Londo Ruins (`m16_00`) | open (wiki) |
| Lift chamber door `o1465` (1021465) at (-49.97, -74.00, 51.24) + call lever `o6100` (1021201) | New Londo elevator ("Abyss" lift, `m16_00`) | Lift Chamber Key 2015 by design; DSR event 50 forces it unlocked |
| Crow nest on the cliff (1022120) | Asylum return trip (`m18_01`) | open |
| Frampt (after both bells) / hole 1022111 (after the Lordvessel) | Firelink Altar (`m18_00`) | §4 |

### 3.1 Progression walkthrough
1. **Arrival** (event 11020001, first time per load when 11810000 is set): NPC backreads paused 3 s, Firelink BGM **1023800** (100200000, sphere r14 around (-49.25, -56.7, 51.05)) enabled, flag **200** set. If the player somehow enters Firelink without the Asylum, 11810000 is set here.
2. **Bonfire** (§3.2) with the Crestfallen Warrior on the steps beside it; Petrus up in the ruined church; Anastacia in the cell below.
3. **Upper ruins / church** (Y≈-53…-51, Z 78-120): chests with Morning Star, Homeward Bone ×6, Cracked Red Eye Orb ×4, Lloyd's Talisman ×4; Firebomb ×6 corpse; Soul corpses. Gesture tutorial message 9029 at (-30.0, -54.36, 76.5). Frampt's altar spot (lid o1480/o1481) at (-38.08, -56.39, 91.21); the hole into the depths below it (region 1022111). Giant Crow perched at (-46.0, -46.47, 89.26).
4. **Parish elevator** at the east end of the ruins (bottom regions 1022000/1022001, top regions 1022002/1022003 at Y 28.25).
5. **Aqueduct to the Undead Burg**: from the courtyard up the stone stairs (Domhnall later sits under the arch at (-39.5, -50, -0.9)), Hollow Warriors + a firebomb thrower on the stairs/aqueduct, Ring of Sacrifice corpse (-38.85, -49.82, -3.05); the aqueduct walkway/"sewer" tunnel (regions 下水29/30/31 from (-21, -34, 9) to (-132, -34, -55), rat and water sounds) ends at the **male Undead Merchant** (-105.99, -33.95, -44.10). F2 West Key corpse on the ledge (-24.6, -39.02, 94.0) (wiki: reached from the upper ruins).
6. **Graveyard** (Z 110-170, Y -61…-71): skeletons rising from bone piles as the player approaches, 2 Giant Skeletons near the end; Zweihander corpse; Binoculars corpse further down the path (-22.0, -76.17, 186.2) toward the Catacombs.
7. **Lower Firelink**: Anastacia's cell (Y -64.6) beside the bonfire terrace; further down (Y -74) the lift chamber door + call lever.

Event 11025150: while the player is in logic region **1022710** "place enemies can't go" (box 6.5×9×33 at (-27.1, -50.5, 6.14), the stairs into Firelink), the 6 aqueduct enemies get AI command 1 (slot 3, give up/return) → they do not follow the player into the shrine.

Vestigial: Hellkite regions 1022200 (reposition), 1022210/1022211 (attack targets near/far), 1022220 (delete), 1022225 (invincible) along the aqueduct, unused by any EMEVD (cut Hellkite appearance). Hidden pre-placed messages: 1200 "Path ahead" (-12.4, -36.83, 108.7), 1201 "Here!" (-26.3, -41.0, 111.2), 1202 "Imminent merchant..." (-38.4, -50.0, 1.0) (MSB `Hidden=True`).

### 3.2 Bonfire
| Bonfire | Part | Entity | Flag | Kindling | Spawn | Fire Keeper |
|---|---|---|---|---|---|---|
| Firelink Shrine | `o0200_0000` (-51.80, -60.60, 55.70) | 1021960 | 11020992 | registered at standard level **10**; re-registered at **30** once 11020108 is set (flags 71020022 or 71020023) | **1022960** (region 復活ポイント00 = start point) | Anastacia (6060). Talk dummy `c1000_0000` (1020960, talk **100200**; ESDs t100200/100201/100202/100210 in the talkesdbnd) |

Fire Keeper absence: event 11020105 — when 11020100 (Fire Keeper dead) → global flag **102** ON and her remains object o1475 (1021300) becomes lootable; when revived (11020101) → 102 OFF. Event 11020106: while dead and not revived, the bonfire shows "Fire Keeper is absent. Cannot light bonfire." (10010184) on "Light bonfire" (10010182).

### 3.3 Enemies

| Sub-zone (route order) | Enemy | Model | Part | Entity | NpcParam / Think | Init anim | Behaviour | Drops |
|---|---|---|---|---|---|---|---|---|
| Ruins roof | **Giant Crow** | c3510 | c3510_0000 (-46.00, -46.47, 89.26) rot -80 | 1020100 | 351000 / 351000 | — | immortal (11025050); at ≤30 % HP plays anim **7000** (flies off) and is disabled until reload | lot 35100000 (none); 1160 HP, 5000 souls |
| Aqueduct stairs | Hollow Warrior (sword+shield) | c2540 | c2540_0000 (-20.57, -40.35, 18.93) | 1020160 | 254000 / 254008 | — | retreats from 1022710 | lot 25400000 Shortsword + Cracked Round Shield; 75 HP, 60 souls |
| Aqueduct stairs | Hollow Warrior (firebombs) | c2540 | c2540_0001 (-21.10, -39.28, 13.23) | 1020161 | 254002 / 254010 | — | firebomb thrower; logic region 1022711 "where the firebomb thrower moves" (-23.36, -50.05, 15.76) | lot 25400200 Firebomb |
| Aqueduct stairs | Hollow Warrior (sword) | c2540 | c2540_0003 (-29.79, -49.61, 24.88) | 1020163 | 254000 / 254011 | — | | Shortsword + Cracked Round Shield |
| Aqueduct | Hollow Warrior (axe) | c2540 | c2540_0006 (-13.15, -39.07, 11.73) | 1020165 | 254001 / 254009 | — | | lot 25400100 Battle Axe + Cracked Round Shield |
| Aqueduct top | Hollow Warrior (sword) | c2540 | c2540_0007 (-24.72, -33.71, 9.28) | 1020166 | 254000 / 254011 | — | | as 0000 |
| Aqueduct stairs | Hollow Soldier (sword) | c2550 | c2550_0000 (-23.40, -49.67, 16.15) | 1020190 | 255010 / 255008 | — | | lot 25501000 Longsword + Hollow Soldier Shield (+ chained Hollow Soldier armor pieces, Titanite Shard) |
| Aqueduct tunnel | Small Rat | c1201 | c1201_0002 (-16.72, -33.95, 11.75) | — | 120100 / 120100 | — | | lot 12010000 Humanity (chance); 80 HP |
| Graveyard | Skeleton (scimitar) ×7 | c2900 | c2900_0000 (-41.30, -63.59, 129.48), _0001 (-38.25, -63.75, 131.21), _0002 (-40.91, -67.66, 151.01), _0004 (-52.00, -61.57, 114.92), _0006 (-48.48, -67.96, 136.17), _0010 (-29.07, -71.01, 166.02), _0011 (-29.27, -70.69, 169.14), _0014 (-40.12, -68.40, 153.26) | 1020200, 1020201, 1020202, 1020204, 1020206, 1020210, 1020211, 1020214 | 290000 / 290003 | **9001** (lying as bone pile) | event 11025200: when the player comes within radius R of the group leader, wait T s, then cancel-anim **9061** (stand up). Groups (leader: members, R m, delays s): 1020200: 1020200 0 / 1020201 1.5 (R 9); 1020202: 1020202 0 / 1020214 1.1 (R 6); 1020209: 1020209 0 / 1020210 0.8 / 1020211 1.5 / 1020212 2.2 (R 9); 1020203: 1020203 0 / 1020204 1.2 (R 4); 1020205: 1020205 0 / 1020206 0.7 (R 3) | lot 29000000 Scimitar + Red and White Round Shield; 115 HP, 50 souls |
| Graveyard | Skeleton (falchion) ×3 | c2900 | c2900_0003 (-53.07, -61.59, 111.87), _0005 (-51.47, -69.85, 140.04), _0009 (-31.07, -70.87, 168.29) | 1020203, 1020205, 1020209 | 290002 / 290004 | 9001 | group leaders (see above) | lot 29000200 Falchion + Red and White Round Shield; 98 HP |
| Graveyard end | **Giant Skeleton** ×2 | c2910 | c2910_0000 (-27.50, -70.80, 168.37), c2910_0001 (-28.45, -63.16, 147.81) | 1020212, 1020213 | 291000 / 291002 | 9001 | 1020212 rises with group 1020209 (2.2 s); 1020213 rises alone (R 3) | lot 29100000 **Murakumo** (chance); 168 HP, 500 souls |

Skeleton rise needs SpEffect 5450 "Skeleton: Resurrection from suspended" semantics (also used on Frampt, §3.4).

### 3.4 NPCs

Positions: courtyard/bonfire terrace Y≈-60; upper ruins Y≈-53; lower cell Y≈-64.6. "Hostile" = standard event **11020510** (hit to ≤90 % HP by the player, or flag already set → waits for engine flag 703, sets the NPC's hostile flag, team 9 "Hostile Ally", save). Standard death **11020530**: on HP 0 (or if the NPC is inside region 1022300 at the bottom of the lift shaft) → force death, quest flags X4…X8 cleared, X12 set; later loads loot remains (Force Character Treasure). Humanity value base flags (`Register NPC Humanity Event Value`) in brackets.

| NPC | Part / entity | Params / talk | Where | Init anim | Presence (EMEVD) | Notes |
|---|---|---|---|---|---|---|
| **Crestfallen Warrior** | c0000_0018 / **6270** | Npc/Think/CharaInit 6270, talk **102627** | steps beside the bonfire (-51.03, -59.59, 62.81) | **7810** (sitting) | present from the start; disabled at load if 1462 (dead) or 1464 (left); event 11020577: when 1460 & talk flag 11020597 and the player is **outside** Firelink → state 1464, disabled (moves on; wiki: hollow in New Londo) | hostile 1461; [8438] |
| **Petrus of Thorolund** | c0000_0014 / **6080** | 6080, talk **102608**; shop 4000-4006 (Heal, Great Heal Excerpt, Homeward, Force, Seek Guidance, Talisman, Thorolund Talisman) | upper ruins (-28.29, -53.27, 88.04) | — (ESD) | present from the start; disabled at load only in state 1192 (left with Rhea); talk gift: common 911 slot 15 flag 11020599 → lot **1140 Copper Coin** | Covenant: Way of White (wiki). Leaves with Rhea's party (11020560 → 1192, disabled); 11020564: once left (1192), on the next-but-one load he is back (state 1193, 11020692 = first-load latch); 11020565: 1193 & talk flag 11020608 → state 1194 (enabled); 11020567/569: death handling and loot (1194-1198); hostile 1195/1197 (11020502/503); [8366] |
| **Rhea of Thorolund** | c0000_0013 / **6070** | 6070, talk **102607**; shop 4200-4209 (Divine Blessing, Heal, Great Heal, Great Heal Excerpt, Homeward, Force, Wrath of the Gods, Seek Guidance, Magic Barrier, Talisman) | upper ruins (-24.29, -53.22, 94.89) rot -165 | **7880** (kneeling/praying) | enabled when 1171. 11020559: 1170 & **11010902** (m10_01 progress flag, unverified meaning) → Rhea 1171, Vince 1211, Nico 1221 all enabled | 11020560: when none are hostile and talk flag 11020600, or 11300005 (Catacombs flag) → Rhea 1173, Petrus 1192, Vince 1216, Nico 1226 all disabled (they leave for the Catacombs/Tomb, wiki). 11020501: hitting any of the four (after 11010902, before they leave) makes all four hostile (1176/1197/1213/1223); [8358] |
| Vince / Nico of Thorolund | c0000_0015 / **6090**, c0000_0016 / **6100** | 6090 talk **102609**, 6100 talk **102610** (NpcParam names say 6090 "Nico", CharaInit says 6090 "Vince"; unverified which) | (-24.36, -53.25, 91.57), (-22.78, -53.27, 89.60) | — | enabled when 1211 / 1221 | see Rhea; [8374], [8382] |
| **Anastacia of Astora** (Fire Keeper) | c2750_0000 / **6060** + talk dummy c1000_0001 (no entity, talk **102606**) at (-53.17, -64.63, 50.15) | NpcParam 275000 (HP 32), no shop (reverse-hollowing / kindling via ESD) | cell below the terrace (-53.07, -64.60, 52.00) | — | always present; immortal (11020110) | Murder/revival chain: 11020555: 1140 & Lautrec not 1574/1575 & ((812 & 813) or 11500200) & alive → **1141**; 11020110: on 1141 → EzState command **1702** (she is killed), flag **11020100** (Fire Keeper dead → flag 102). 11020556: 1141 & **810** → 1146; 11020557: 1146 & talk flag **11020609** → 1142, and 11020110 sends EzState **1701** (revive) → waits for event message 1701 → **11020101** (revived). 11020558: 1142 & 11800200 → 1145. Remains o1475 (1021300): lot **2030** Dingy Hood + 2031 Dingy Robe + 2032 Dingy Gloves + 2033 Blood-Stained Skirt (flag 50001030) + 2034 **Black Eye Orb** (flag 50001031) while dead. Interpretations (wiki): 1141 = killed by Lautrec after the second bell; Black Eye Orb → invade Lautrec in Anor Londo; Fire Keeper Soul from Lautrec (lot 2060) → revive her |
| **Knight Lautrec of Carim** | c0000_0019 / **6301** | NpcParam 6300, CharaInit 6300, talk **102630**; gift on release: common 911 slot 16 flag 11020607 → lot **1150 Sunlight Medal** | lower terrace (-59.34, -64.62, 43.54) | **7825** (sitting) | freed in the Undead Parish cell (`m10_01`): 11020587: 1571 & player in Firelink → 1572 enabled (+11020690, 11020691). 11020588: while 1572/1573/1577: random 1-in-3 (11025120-122) "away" → 1573 disabled; otherwise back to 1572 (if 11020691) or 1577. 11020589: (1570 or 1577) & boss flag **3** (unverified) & in Firelink → 1572 enabled. 11020410: (1572/1573/1577) & ((812 & 813) or 11500200) → **1578** disabled (leaves to kill Anastacia / invades in Anor Londo, wiki) | killed: lot 2060 Fire Keeper Soul (flag 50001060) (wiki: from his Anor Londo invasion); hostile 1574; [8462] |
| **Laurentius of the Great Swamp** | c0000_0017 / **6131** | NpcParam 6130 (param name says "Griggs - Undead Burg"; CharaInit + talk show Laurentius), talk **102613**; shop 3000-3004 (Fireball, Fire Orb, Combustion, Iron Flesh, Flash Sweat); rescue gift common 911 slot 12: 11020602 → lot **1110 Pyromancy Flame** | ledge north of the bonfire (-46.95, -56.35, 66.41) | **7835** (sitting) | rescued from the barrel in the Depths: 11020574: 1251 & in Firelink → 1252 enabled. 11020575: 1252 & talk flags 11020102 & 11020103 & alive → 1256 (wiki: goes hollow in Blighttown after learning Chaos pyromancies, unverified mapping) | hostile 1253; [8390] |
| **Griggs of Vinheim** | c0000_0012 / **6041** | NpcParam 6040, talk **102604**; shop 2000-2021 (Soul Arrow, Great Soul Arrow, Heavy Soul Arrow, Great Heavy Soul Arrow, Magic Weapon, Magic Shield, Aural Decoy, Fall Control, Bellowing/Lingering Dragoncrest Ring, Sorcerer's Catalyst, Homing Soulmass, Soul Spear — some rows are Logan's stock, the param names mix them) | east of the bonfire (-30.57, -58.93, 57.43) | — | rescued in the Lower Undead Burg: 11020552: 1111 & in Firelink → 1112 enabled. 11020553: 1112 & 11020694 (Logan left) → 1113 enabled. 11020554: 1113 & 723 & player in `m15_00` → 1117, disabled (wiki: leaves after Logan, later hollow; unverified) | hostile 1114; [8342] |
| **Big Hat Logan** | c0000_0011 / **6031** | NpcParam 6030, talk **102603** | next to Griggs (-27.09, -58.86, 59.28) | **7830** (sitting) | rescued in Sen's Fortress: 11020550: 1091 & 11500594 → 1092 enabled. 11020551: 1092 & talk flag 11020603 & player outside Firelink → 1093, disabled, **11020694** (gone to the Duke's Archives) | hostile 1096; [8334] |
| **Domhnall of Zena** | c0000_0003 / **6261** | NpcParam/CharaInit 6260, talk **102626**; shop 1500-1584 (his "collection": gear of dead NPCs, incl. Crystal weapons, Artorias/Ornstein sets, Master Key, Bottomless Box…) | under the aqueduct arch (-39.53, -50.00, -0.86) | **7835** | moves from the Depths: 11020412: 1430 & **11010700** (Burg bell rung) & **11400200** (Blighttown bell rung) → 1431 enabled | 11020413: killed → 1434 off, 1435; hostile 1434; [8430] |
| **Siegmeyer of Catarina** | c0000_0020 / **6287** | NpcParam 6282, CharaInit 6282, Think 6280, talk **102628** | by the bonfire (-54.70, -60.17, 53.59) | — | 11020579: 1494 & **11510590** (Anor Londo talk flag) → 1497 enabled (wiki: rests at Firelink after Anor Londo) | hostile 1512/1513; [8446] |
| **Sieglinde of Catarina** | c0000_0021 / **6292** | NpcParam 6290, talk **102629** | by the bonfire (-54.17, -60.12, 52.30) | — | 11020583: 1542 & **11700593** (Duke's Archives rescue) & Siegmeyer not hostile → 1543 enabled; 11020584: 1543 & 11020605 → 1544 disabled (leaves); 11020585: 1544 & **11410593** (Demon Ruins/Izalith) → 1545 enabled; 11020586: 1545 & 11020606 → disabled | hostile 1547; [8454] |
| **Patches the Hyena** | c0000_0022 / **6322** | NpcParam/CharaInit 6321, Think 6320, talk **102632**; shop 1600-1617 (Eye of Death, Divine Blessing, Prism Stone, Humanity, Twin Humanities, Heal, Great Heal Excerpt, Crescent Axe, Mace, Canvas/Thorolund Talisman, Masks of the Father/Mother/Child, Cleric set) | upper ruins west (-55.21, -54.49, 90.82) | **7840** (crouching) | 11020411: 1624 & boss flag **7** (unverified: Nito) & in Firelink, not hostile/dead (1622/1625/1627/1628) → 1626 enabled | hostile 1627; [8478] |
| **Ingward** | c0000_0024 / **6181** | 6180, talk **102618**; shop 2400-2401 (Resist Curse, Transient Curse) | (-25.15, -55.24, 68.24) | — | 11020576: 1312 & **11600593** (New Londo talk flag) & in Firelink → 1313 enabled (unverified in-game; wiki usually has him stay in New Londo) | hostile 1314; [8406] |
| **Kingseeker Frampt** | c5330_0000 / **6330** | NpcParam 533000 (HP 11120), talk **102633**; no shop list (eats items for souls via ESD) | altar ruins (-38.40, -56.03, 88.78) | **7003** | disabled if **15** (Gwyn killed); otherwise immortal, gravity off, and disabled at load in states 1640 (not yet woken) and 1647 (gone). 11020420: (1640 & 11010700) or (1640 & 11400200) or 1641 → state **1641**, enabled with waiting anim **9000** (one bell rung: still below the lid); 11020421: 1641 & both bells → **1642**, enabled with waiting **7003** (awake). 11020350: when **both bells** are rung, the lid `o1480` (1021480, "Large door", hits 1023500/1023502, map part 1023501) is replaced by `o1481` (1021481, hit 1023510) → Frampt emerges. 11020422: 1642 & 710 & alive → **1643**; 11020423: 1643 & 820 & 11800100 & alive → **1644**; 11020424: 1649 & 11020598 → **1647**, anim **7005** (burrows away), disabled; 11020425: in Firelink & (11020598, or hit to ≤90 % while the player is >15 m away) → 7005, disabled. 11026210: when the player is >30 m away and Frampt is in 1643/1644/1645 (and not 830): random 1-in-3 **nap** (flag 1650, waiting **9001**); waking when hit: SpEffect 5450, waiting 7003, anim **9061**. 11026200: 1643/1644/1645 & flag **820** (talk request) → 830, Frampt-at-altar 6331 enabled, cutscene **100240** (Frampt swallows the player) warp to **1802110** (`m18_00` altar) + cutscene **180040**, flag 822 | Lordvessel quest (wiki): talk after both bells → seek the Lordvessel in Anor Londo |
| Undead Merchant (male) | c2510_0000 / **6240** | NpcParam 251000 (HP 420), talk **102624**; shop 1100-1133 (Orange Guidance Soapstone, Repair Powder, Throwing Knife, Firebomb, Lloyd's Talisman, **Residence Key**, Repairbox, Dagger, Shortsword, Scimitar, Rapier, Hand Axe, Club, Reinforced Club, Spear, Short Bow, East-West/Small Leather/Buckler/Leather/Heater/Warrior's Round shields, arrows, bolts, Chain set, Leather Gauntlets, Bottomless Box) | end of the aqueduct tunnel, Undead Burg side (-105.99, -33.95, -44.10) | — | always (11020504): if attacked → team Enemy, home region **1022700** "moss merchant flees to" (-114.37, -33.95, -49.56), AI command 1 until he reaches it (runs back into his alcove), flag 1411; on later loads hostile ones are warped there | death 11020530 slot 8 (1410-1413); lot 25100000 (none) |
| (dummy) | c1000_0002 / 6450 | talk **102645** (`t102645.esd` is a 300-byte stub) | graveyard end (-23.57, -70.30, 167.91) | — | — | vestigial (reuses Snuggly's entity id) |

Talk ESDs present (`m10_02_00_00.talkesdbnd.dcx`): t102600 (stub), t102602 (stub), t102603, t102604, t102606, t102607, t102608, t102609, t102610, t102613, t102618, t102624, t102626, t102627, t102628, t102629, t102630, t102632, t102633, t102645 (stub), bonfires t100200/100201/100202/100210.

### 3.5 Items / treasures (route order)

| Where | Part | Item (lot, flag) |
|---|---|---|
| Courtyard near the bonfire | o0500_0000 (-39.37, -58.16, 53.44) | **Humanity ×3** (1020000, 51020000) |
| Aqueduct stairs | o0500_0012 (-27.15, -50.08, 17.70) | Soul of a Lost Undead (1020120) |
| Aqueduct under the arch | o0500_0013 (-38.85, -49.82, -3.05) | **Ring of Sacrifice** (1020130) |
| Aqueduct top / tunnel | o0500_0004 (-38.00, -33.70, 2.10) | Soul of a Lost Undead (1020040) |
| Aqueduct tunnel | o0500_0014 (-15.50, -33.73, 12.55) | Soul of a Lost Undead (1020140) |
| Upper ruins | o0500_0005 (-35.40, -49.54, 78.70) | Soul of a Lost Undead (1020050) |
| Upper ruins | o0500_0009 (-48.80, -54.40, 78.82) | Soul of a Lost Undead (1020090) |
| Ruins (by the Thorolund spot) | o0500_0011 (-21.50, -54.57, 83.20) | Firebomb ×6 (1020110) |
| High ledge | o0500_0006 (-14.60, -41.64, 94.05) | Soul of a Lost Undead (1020060) |
| Ledge above the ruins | o0500_0015 (-24.60, -39.02, 94.00) | **Undead Asylum F2 West Key** (1020210, 51020210) |
| Church chest | o0110_0003 (-19.50, -51.26, 101.10), ObjAct 11020703 | Cracked Red Eye Orb ×4 (1020200) |
| Church chest | o0110_0001 (-27.50, -51.07, 104.00), ObjAct 11020701 | **Morning Star** (1020180) |
| Church chest | o0110_0002 (-27.20, -51.58, 109.00), ObjAct 11020702 | Homeward Bone ×6 (1020190) |
| Church chest | o0110_0000 (-30.10, -53.54, 119.60), ObjAct 11020700 | Lloyd's Talisman ×4 (1020070) |
| Graveyard | o0500_0001 (-55.15, -61.48, 112.55) | Large Soul of a Lost Undead (1020010) |
| Graveyard | o0500_0002 (-51.00, -69.42, 138.20) | **Caduceus Round Shield** (1020020) |
| Graveyard | o0500_0003 (-40.20, -65.77, 141.90) | Large Soul of a Lost Undead (1020030) |
| Graveyard | o0500_0008 (-27.50, -62.87, 147.80) | **Winged Spear** (1020160) |
| Graveyard end | o0500_0007 (-23.70, -70.30, 167.50) | **Zweihander** (1020150) |
| Path below the graveyard | o0500_0010 (-22.00, -76.17, 186.20) | **Binoculars** (1020170) |
| Fire Keeper cell (event) | o1475_0000 (-53.20, -64.50, 49.50), entity 1021300 | Anastacia's remains: Dingy Hood/Robe/Gloves, Blood-Stained Skirt, Black Eye Orb (lots 2030-2034) — only while she is dead |
| **Relief chest** | o0110_0004 (-38.40, -55.14, 104.44), entity 1021690, ObjAct 11020704 + 28 hidden treasure parts o0020_0000…0027 (1021700-1021727) at the same spot | key re-issue (below) |

Chests (11020700, 4 slots): opening plays anim 0 and enables the treasure after 10 frames; opened state persists.

**Relief chest (key re-issue system):** event 50 sets 50004000-50004070 ON every load (nothing to give). For 71 key/important items (event 11020800, slots 0-70): if the item's original pickup flag (X0) is ON, flag X4 (703 for most, or a quest flag) is OFF, and the player **does not own** the item (inventory or Bottomless Box) → 11025300; when the relief chest is opened (11020899), treasures 1021700-1021707 are enabled and rerolled, and the matching lot flag (50004000+slot) is cleared so the chest gives the item again. Covered items: all keys 2001-2022 (Basement, Crest of Artorias, Cage, Archive keys, Blighttown, New Londo, Annex, Dungeon Cell, Big Pilgrim's, F2 East, Seal, Depths, Lift Chamber, F2 West, Mystery, Sewer Chamber, Watchtower Basement, Archive Prison Extra, Residence, Crest), Lord Souls/shards 2500-2503, Lordvessel 2510, Broken Pendant 2520, rings 138 Covenant of Artorias / 139 Orange Charred Ring, soapstones/orbs 100-118, Estus Flask +0…+7 (200-214, chained via 8131-8137 upgrade flags), Peculiar Doll 384, Darksign 117, Dragon Eye 114, Dragon Head/Torso Stone 377/378, embers 800-813, Weapon/Armor Smithbox 2600/2601, Repairbox 2602, Rite of Kindling 2607, Bottomless Box 2608, Master Key 2100, Crest Key 2022 etc. In normal play the chest stays empty.

### 3.6 Interactive objects & scripted events

| Object | Part / entity | Behaviour | Events |
|---|---|---|---|
| Parish elevator | o1460_0000 (1021000), ObjectParam 1460 "church-based elevator" | 11020300: at load, if used before: anim 11 (at bottom) or 12 (at top) per 11020302; first time the player stands on the **top** plate 1022003 → flag 11020300, 11020302, anim 0 (descend). Then 11020301 (singleplayer only, waits for network approval 3 s): standing on plates 1022000 (bottom, car below)… → anim 2 (up) / anim 1 (down), then anims 21/22 (return) when the player steps off; plates 1022000/1022001 (bottom Y -44.65), 1022002/1022003 (top Y 28.25) | 0, 11020300, 11020301 |
| Lift chamber door | o1465_0000 (1021465), ObjectParam 1465 "iron lattice door", ObjAct 11020120 (ObjActParam default) | key **2015 Lift Chamber Key** ("Used Lift Chamber Key" 10010874; "Used Master Key" 10010883 when opened with item 2100 **Master Key**); DSR event 50 sets 11020120 + 61020120 ON every load → the key interaction is always disabled (treated as opened; physical state = object default, unverified) | 11020120 |
| Lift call lever | o6100_0000 (1021201), ObjAct 11020202, ObjActParam **6101** "m16_00 Abyss general purpose lever" (text 10010501) | calls the New Londo elevator (logic lives in `m16_00`) | (m16_00) |
| Frampt's lid | o1480 (1021480) → o1481 (1021481) | swap after both bells | 11020350 |
| Hole to the altar | region **1022111** (box 7×1×10 at (-37.93, -75.0, 91.11)) | falling in: if flag **710** (Lordvessel placed, unverified) → cutscene **180060**, warp to 1802110 (`m18_00` altar); else **player dies** | 11020351; 11020352 disables hits 1023600/1023601 once 710 |
| Crow nest | region 1022120 | to the Asylum (§2) | 11020020, 11020021 |
| Chests | o0110_0000…0003 (1021650-1021653) | | 11020700 |
| Relief chest | o0110_0004 (1021690) | | 11020800, 11020899 |
| Statue | o1470_0000 (-35.55, -55.20, 105.65), "Princess statue" (Gwynevere); the altar map places o8320_0000 at the same spot | static | — |
| Breakables | o1495 ×7 ("mimicry urn"), o1496 ×8, o1497 ×10 urns (hp 1) around the ruins/terrace | | — |

Ambience/sound: BGM 100200000 (1023800, enabled only after 11810000), wind 100100001 (region 風１), strong wind 100000003 ×2 (aqueduct, cliff below the graveyard), water 109900000 ×3 (aqueduct tunnel). Wind SFX on m9000B2_0000 (constant 0.2 m/s X).

Cutscenes referenced: 100225 (arrival), 100226 (return from the Asylum), 100230 (Firelink → Asylum), 100240 (Frampt carries you), 100250 (back from the altar), 180060 (hole to the altar). Remo files present but not referenced by any DSR EMEVD: 100200, 100201, 100202, 100203, 100210, 100220, 100235, 180100, 180110 (possibly talk-ESD-triggered or unused, unverified).

### 3.7 Porting notes (Firelink)
- Hub map with ~16 NPC slots driven by quest flag ranges (1090-1659) plus other maps' flags (bells 11010700/11400200, rescues 11500594/11700593/11600593/11510590, bosses 3/7/15). Needs: DS1 flag ranges reserved globally (translation rules §1), the generic "hostile at 90 % HP" and "NPC death → loot remains" events, and DS1 talk ESDs translated (t1026xx).
- NPC idle poses come from MSB `InitAnimID` (7003, 7810, 7825, 7830, 7835, 7840, 7880) and EMEVD waiting anims (Frampt 9000/9001/7003/7005/9061); these are c0000 (player-rig) TAE ids → the 1:1 animation pipeline must cover player-rig NPC sitting/kneeling clips.
- Frampt (c5330) is a huge NPC with gravity off, immortality, random naps and a swallow-warp; Kaathe at the altar uses the same model.
- Fire Keeper: DS2 has no kindling or Fire Keeper absence; needs a DS1 bonfire layer (kindling level, flag 102 "Fire Keeper absent" blocking the bonfire, Fire Keeper Soul revival via talk).
- Skeleton bone-pile rise (InitAnim 9001 → cancel anim 9061 by distance + delay): EMEVD `Set Special Animation(s)`, needs a DS2 AI/ESD equivalent.
- Merchant flee to a home region on aggression.
- Giant Crow: immortal decoration that flies off at 30 % HP.
- Elevator o1460: two-plate DS1 elevator logic (`IF in area` + object anims 1/2/11/12/21/22), first unlocked from the top.
- Relief chest: optional (DS2 has no key loss); keep or drop.
- Hole into the altar = instant death unless flag 710; Frampt swallow warp; return landing 1022110 (-39.75, -54.83, 81.71) "landing point from the great tomb".
- Vestigial Hellkite regions and hidden dev messages can be ignored.

### 3.8 Area completion checklist (Firelink)
- [ ] Arrival point 1022100, spawn 1022960, BGM gated by 11810000, flag 200.
- [ ] Bonfire (kindling 10→30, Fire Keeper absent rule, talk dummy 100200).
- [ ] Anastacia (cell, talk dummy, murder 1702 / revive 1701, remains + Black Eye Orb).
- [ ] Crestfallen Warrior (7810), Petrus (+Copper Coin, leaves/returns), Rhea/Vince/Nico (arrive on 11010902, leave on 11020600/11300005, group hostility), Laurentius (7835, +Pyromancy Flame on rescue), Griggs, Logan (7830), Domhnall (7835, both bells), Siegmeyer, Sieglinde, Lautrec (7825, random presence, leaves, Sunlight Medal), Patches (7840), Ingward, male Undead Merchant (flee), Frampt (lid swap, naps, swallow warp, burrow away).
- [ ] Shops: Petrus, Rhea, Laurentius, Griggs/Logan, Domhnall, Patches, Ingward, Undead Merchant.
- [ ] Aqueduct enemies (5 Hollow Warriors incl. firebomb thrower, 1 Hollow Soldier, rat) + "don't follow into Firelink" region.
- [ ] Graveyard: 10 Skeletons + 2 Giant Skeletons with bone-pile rise groups.
- [ ] Giant Crow on the roof.
- [ ] 16 corpses + 4 chests (+ relief chest system, optional).
- [ ] Parish elevator (top-unlock), lift chamber door + lever (New Londo lift), crow nest to the Asylum, hole to the altar (710), Frampt warp (820).
- [ ] Messages: gesture tutorial 9029 (+3 hidden dev messages optional).
- [ ] Ambience: wind, water, BGM.

---

## 4. Firelink Altar (altar part of `m18_00_00_00`)

**Getting here:** Frampt's swallow (cutscene 100240 + 180040, landing region **1802110** "great-tomb warp landing" (40.89, -65.18, 103.74)) or the Firelink hole after flag 710 (cutscene 180060). **Back to Firelink:** 11806200: Frampt states 1643/1644/1645 & talk flag **824** → cutscene **180050** warp to **1022110** + cutscene **100250**, Frampt-at-altar (6331) disabled, 830 off. Kaathe equivalent: 11806201 (1672/1673/1674 & 825) → cutscene **180051** warp to **1602110** (`m16_00` New Londo) + **160050**, Kaathe disabled, 831 off. **Onward:** the Kiln door `o8300` (1801101) → Kiln of the First Flame (other document).

| Element | Data |
|---|---|
| Altar bonfire | `o8310` (1801960) at (53.31, -63.34, 106.81); talk dummy c1000_0000 (1800960, talk **180100**). Not a bonfire until the Lordvessel is placed: 11800101 "No Lordvessel" (10010174) when examined without item 2510 |
| Lordvessel | 11800100 (host, owns goods **2510**): "Place Lordvessel" (10010105) on the altar → cutscene **180015**, Lordvessel object `o8311` (1801110) shown, bonfire registered (kindling 10), item removed. Enables bonfire warping (engine) |
| Lord Souls | 11800200 (on flag 756 = offer request from the bonfire menu, player anim 7697): removes each owned Lord Soul/shard **2500, 2501, 2502, 2503** and sets 11800201-11800204; 11800230 (4 slots) plays SFX 180005-180008 on the vessel per count and player anim 7701 (loop) while not all four; 11800210: SFX per count on load; all four → 11800211, cutscene **180010**, Kiln door `o8300` (1801101) anim 1 (opens), bonfire re-registered kindling 30 |
| Kiln door | `o8300` (1801101) at (58.02, -63.43, 107.98); 11800220: before 11800210, examining region 1802100 "seal gate won't open" (5×2×1.4) → "Locked by some contraption" (10010160) or, with flag 11805110, "No Lord Souls to offer" (10010171) |
| Kingseeker Frampt at the altar | c5330_0016 / **6331**, NpcParam 533000, talk **180633**, Init anim **7007**; immortal; enabled only while 830; quest states via 11800530/531/537/539/541 (flags 1640-1649) |
| Darkstalker Kaathe at the altar | c5330_0017 / **6341**, NpcParam 533001, talk **180634**, Init anim **7003**; immortal; enabled while 831; 11800533/534/538/540/542 (flags 1670-1679) |
| After Gwyn (flag 15) | altar bonfire and vessel hidden, `o8312` (1801111) shown (burnt/ended state); Frampt/Kaathe events skipped |
| Statue | `o8320_0000` at Firelink coordinates (-35.55, -55.20, 105.65) (same spot as m10_02 `o1470`) |
| Corpse | o0503_0000 "blacksmith corpse" (42.30, -65.14, 101.40), lot 1800070 missing in DSR (dead treasure) |

Porting notes (altar): Lordvessel/Lord-Soul placement needs bonfire-menu hooks (flag 756 is set by the bonfire talk ESD), the Lordvessel's DS1 "warp unlocked" engine rule (DS2 warps are always on: the separate Lordran travel page should only unlock DS1 warps after 11800100), and the Kiln door opening after four souls.

Checklist (altar):
- [ ] Landing points 1802110 (from Frampt/hole) and 1022110 (back).
- [ ] Altar bonfire + Lordvessel placement (180015) + "No Lordvessel".
- [ ] Lord Soul offering (4 items, SFX 180005-180008, cutscene 180010) → Kiln door opens.
- [ ] Frampt (7007) and Kaathe (7003) at the altar with their warps (180050/100250, 180051/160050).
- [ ] Post-Gwyn state (flag 15).

---

## 5. Cross-area flag summary (for the global flag map)

| Flag | Meaning (as used here) |
|---|---|
| 16 | Asylum Demon dead |
| 15 | Gwyn dead (Frampt/Kaathe/altar off) |
| 3, 7 | boss flags used as NPC conditions (Lautrec, Patches) — identities unverified |
| 102 | Firelink Fire Keeper dead (bonfire unusable) |
| 200 | first visit to Firelink done |
| 703 | engine flag gating NPC hostility / relief chest (meaning unverified) |
| 705 | NG+ Asylum init done |
| 710 | Lordvessel placed (hole to the altar safe) (unverified name, behaviour from 11020351/352) |
| 756 | "offer Lord Souls" request |
| 810, 812, 813, 820, 822, 824, 825, 830, 831 | Anastacia/Lautrec/Frampt/Kaathe story flags (812 & 813 likely both Bells of Awakening; unverified) |
| 11810000 | left the Asylum (arrived in Lordran) |
| 11810002 | opening cutscene seen |
| 11810090 | courtyard fog passed |
| 11810110 / 11810111-112 | far door opened / hall door pushed |
| 11810211 | boulder rolled |
| 11810220 | hall floor collapsed (reset each load) |
| 11810310-315 | demon intro / hall sequence |
| 11810590-592 | Oscar talk results |
| 11810900 | Stray Demon dead |
| 11010700, 11400200 | Bell of Awakening rung (Parish, Blighttown) |
| 11010902, 11300005, 11500594, 11510590, 11600593, 11700593, 11410593, 11500200 | other maps' progress/talk flags used as NPC move conditions |
| 1060-1074 | Oscar states (1061 hollow, 1073 dead) |
| 1090-1099, 1110-1119, 1140-1169, 1170-1229, 1250-1259, 1310-1319, 1410-1413, 1430-1459, 1460-1464, 1490-1514, 1540-1569, 1570-1599, 1620-1629, 1640-1650, 1670-1679 | NPC state ranges: Logan, Griggs, Anastacia, Thorolund group, Laurentius, Ingward, Undead Merchant, Domhnall, Crestfallen, Siegmeyer, Sieglinde, Lautrec, Patches, Frampt, Kaathe |
| 50000080-82, 50001660-61, 50001030-31, 50000110, 50000140, 50000150, 50001060 | one-time item-lot flags (Oscar gifts, demon drops, Anastacia remains, Laurentius, Petrus, Lautrec gifts, Fire Keeper Soul) |
| 51810xxx / 51020xxx | map treasure flags |
| 8326-8478 | NPC humanity value base flags |

Tool notes: `msb1-dump` has no MSB event categories (messages, spawn points, SFX, sounds, enemy InitAnimID); these came from a scratch SoulsFormats reader (session scratchpad `g01\msbev`). `ds1-text` searches all FMGs and can return the wrong one for ids shared between FMGs (e.g. blood messages 9000+); look those up per FMG.
