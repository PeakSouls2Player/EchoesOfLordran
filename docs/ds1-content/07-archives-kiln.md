# 07 — The Duke's Archives, Archive Tower prison, Crystal Cave, Firelink Altar, Kiln of the First Flame

DS1 content census for "Lordran in DS2". Ground truth: DSR `m17_00_00_00` / `m18_00_00_00` MSB + EMEVD (`./src/lr.cmd msb1-dump`, `emevd-dump`), `common.emevd`, DSR params (`dump-param dsr`), talk ESDs extracted from `script/talk/m17_00_00_00.talkesdbnd.dcx` / `m18_00_00_00.talkesdbnd.dcx`. Wiki text (darksouls.wikidot.com, ludo.guide) was used only to confirm route order; data wins where they disagree. "(unverified)" = not proven by the data.

Map / place-name ids (Place_name FMG): **1700 The Duke's Archives**, **1701 Crystal Cave** (both inside map `m17_00_00_00`), **1801 Firelink Altar**, **1800 Kiln of the First Flame** (both inside map `m18_00_00_00`). Collisions carry `mapName=1700 / -1701 / 1800 / -1801` (negative sign on the sub-area names; meaning unverified, probably "no banner").

Notation: part names are MSB part names (`c2800_0006`), entity ids are MSB EntityIDs, `ev NNN` = EMEVD event id, flags are DS1 event flags. Positions are DS1 map-local (x, y, z).

---

## Global flags used across these areas

| Flag | Meaning (from EMEVD) |
|---|---|
| 710 | Player has (ever) owned the Lordvessel (goods 2510) — common ev 710. Unlocks bonfire **Warp** menu (status msg 10010620 "By the power of the Lordvessel, you may now warp between bonfires") and the Firelink "hole" drop to the altar. |
| 11800100 | **Lordvessel placed** at Firelink Altar (m18 ev 11800100). Opens the Archives seal (m17 ev 11700085), advances Frampt/Kaathe. |
| 11800201 / 202 / 203 / 204 | Lord soul offered: 2500 Lord Soul (Nito) / 2501 Lord Soul (Bed of Chaos) / 2502 Bequeathed Lord Soul Shard (Four Kings) / 2503 Bequeathed Lord Soul Shard (Seath). |
| 11800210 / 11800211 | All four offered → Kiln gate opened. |
| 702 | Player is in m18_00 and the Kiln is not yet open (common ev 702) → shows "Offer souls to Lordvessel" on the altar bonfire menu. |
| 756 | Set by the altar bonfire ESD (t180100) when the player picks "Offer souls to Lordvessel" → m18 ev 11800200 consumes the souls. |
| 14 | **Seath killed**. |
| 15 | **Gwyn killed**. |
| 20 / 21 | Ending chosen: Link the Fire / Dark Lord. |
| 830 / 831 | Frampt / Kaathe is present at the Firelink Altar (temporary; cleared by common ev 822/823 when you leave m18_00). |
| 820 / 821 | Talk choice "take me to the altar" (Frampt at Firelink / Kaathe in the Abyss). 824 / 825 = "take me back". |
| 11700050 / 11800050 | Pure-Black World Tendency active in m17 / m18 (enables Black Phantom enemy sets). |
| 703 | Used as a "wait" gate before NPCs turn hostile / before black-phantom boss entry (common; meaning not resolved here). |

---

# AREA 1 — The Duke's Archives (`m17_00_00_00`, place 1700)

## 1. Header
- **Map**: `m17_00_00_00` (shared with the Crystal Cave).
- **Entrance**: from Anor Londo (first Anor Londo bonfire side, past a Sentinel) through the **"King's seal"** (王の封印) — object `o7907_0000` (entity **1701710**, SFX 1701711) at (220.35, 199.9, 400.9).
  - Prerequisite: **Lordvessel placed** (flag 11800100). Before that, ev 11700085: action "Examine brilliant light" (10010410) at region **1702610** → status message 10010630 **"Sealed by the Great Lord's power"**. After 11800100 the seal object + SFX are deleted on map load and flag 11700086 is set.
- **Exits**: Anor Londo (back through the seal corridor); Crystal Cave (down from the crystal golem garden, same map); prison tower (forced by dying to the first Seath encounter); Seath's arena bonfire / any bonfire by warp.
- **Main gates**: Archive Tower Cell Key (2004), Archive Prison Extra Key (2020), Archive Tower Giant Cell Key (2006), Archive Tower Giant Door Key (2005). Seath must "kill" you once (the capture) to reach the prison.

## 2. Progression walkthrough (route order)
1. **Seal corridor (y≈200)**: candle-lit hall (SFX m2070 candle points) from the seal at z≈400 to the entrance room; **2 Armored Tusks** patrol (boar "return-to-nest" regions 1702750-1702752). Entrance room with **bonfire 1701962** and a row of 8 crystal "statue" spots (`o0101_0000..0007`, entity 1701900-1701907, registered as Crystal Statues — online cursed-player statues).
2. **Entrance gondola (lift)** `o7241` (entity **1701130**) at (244.2, 202.5, 488.7), y≈202 → y≈275. Levers: bottom `o7201_03` (1701131, ObjAct **11700220**), top `o7201_06` (1701132, ObjAct **11700221**). You can also ride it with the action button on the platform ("Pull lever" 10010501). State flag **11700102**; events ev 11700160 slot1 / ev 11700105 slot2 / ev 11700090 slots 2-3. Enemy-blocking hit **1703101** toggled by region 1702781 (ev 11700150 slot1). Client-only "multi" fog `o7902` (1701994) at the top.
3. **First room / library hall 1 (y 274-310)**: Crystal Hollow swordsmen + archers; **Channeler** (teleports); **Mimic** on the 2nd floor; first **rotating staircase** (o7230_00/o7231 entity 1701200/1701201) — turn its handle to rotate both rotating staircases (shared flag 11700205). Items: Soul of a Brave Warrior under the staircase, Twinkling Titanite on the 2nd-floor balcony, Twinkling Titanite chest (left corridor), Avelyn chest on top of a bookshelf (reach via rotated stairs), Twinkling Titanite chest (y299).
4. **Seath gondola** `o7240` (entity **1701100**) at (301.1, 288.4, 545.6), y≈288 → y≈362. Levers: bottom `o7201_01` (1701101, ObjAct **11700210**), top `o7201_05` (1701102, ObjAct **11700211**); state flag **11700100**; enemy-blocking hit **1703100** / region 1702780; client-only fog `o7908` (1701996) at top ("Seath elevator" crystal-shard SFX at the top).
5. **Top floor (y 362-389)**: the **Crystal Knight** NPC enemy (Crystalline armor, 3000 souls, never respawns) and a **Crystal Lizard** on the stairs to the event fog `o7901` (1701890).
6. **First Seath encounter** (event boss, unwinnable) → you die → respawn in the **Archive Tower cell** (see Bosses §5a).
7. **Prison (Archive Tower)**: hit the sleeping 1-HP **Serpent Soldier** outside your cell door through the bars → **Archive Tower Cell Key** → open cell door 00 → entering the Pisaca alert zone plays cutscene **170010** (a serpent man starts the siren "gramophone") → siren on, Pisacas alerted, two Serpent Soldiers run up to the Giant Door. Spiral ramp down past cells 02-05 to the **Pisaca nest** (y≈229) with **Big Hat Logan's giant cell** (door 06). Cell 05 area: **Archive Prison Extra Key**. Extra Key opens door 01 (back door of your own cell → drop to the Soul of a Brave Warrior and on to the gramophone room) and door 03 (hollow **Rhea**'s cell). Gramophone room (y≈241.8): Serpent Mage + 2 Serpent Soldiers, **gramophone lever** (turns the siren off), chest with **Archive Tower Giant Door Key**. Ladder `o7300` up to the **Giant Door** `o7400` (y≈292.7). Maiden set + White Seance Ring corpse is a drop-off from the spiral ramp above the cells.
8. **Giant Door** (key 2005) → balcony (y≈292.8, crystal hollows) → **library hall 2** (second rotating staircase, 2 Channelers incl. one high up, **bonfire 1701960** on the far-right balcony, Strong Magic Shield chest at the top).
9. Slide down ladder `o7303` → pull lever `o7201_00` → **moving bookshelf 00** (`o7250`, 1701120) opens a secret ground-floor door → small room (Blue Titanite Chunk chest; later Logan's spot + his Sage Robe chest) → large room with **4 chests** (Mimic → Falchion[Enchanted]; Crystal Ember; **Archive Tower Giant Cell Key**; Channeler set).
10. Pull lever `o7201_04` → **movable staircase** (`o7200/o7199`, 1701110/1701109) lowers (one-time) → lower corridor (y≈271.7): Prism Stone x20 chest, one crystal hollow, **white-light fog "CP2"** `o7906` (1701706; disappears permanently once crossed, ev 11700083).
11. Wooden platforms / ladder `o7302` down into the **Crystal Golem garden** (y 244-267): golems, Golden Crystal Golem (Sieglinde, quest-gated), Crystalline set corpse, Blue Titanite Chunk. Continue down the crystal pit into the **Crystal Cave** (Area 2).
12. Return trips: Giant Cell Key → free Logan (door 06). After Seath dies the Giant Door also opens by itself (ev 11700140 AND_02).

## 3. Bonfires
| Bonfire obj | Entity | Flag | Location | Bonfire-host chr (talk ESD) | Respawn point | Notes |
|---|---|---|---|---|---|---|
| `o0200_0003` | **1701962** | 11700976 | Archives entrance room after the seal corridor (230.0, 199.98, 482.0) | `c1000_0003` 1700962 (t170002) | 復活ポイント02 (232.85, 199.98, 477.15) | First Archives bonfire. Collisions h0100B0-h0102B0 have disableBonfire=1701962. |
| `o0200_0001` | **1701961** | 11700984 | **Inside your prison cell** (Archive Tower Cell) (378.40, 269.78, 551.70) | `c1000_0001` 1700961 (t170001) | 復活ポイント01 (377.0, 269.78, 555.25); capture respawn = MSB SpawnPoint event 復活イベント（一時用） entity **1702900** → region 復活ポイント（一時用） (377.37, 269.78, 553.62) | Reached by dying to the event Seath. |
| `o0200_0000` | **1701960** | 11700992 | Library hall 2, far-right balcony (350.08, 277.59, 594.38) | `c1000_0000` 1700960 (t170000) | 復活ポイント00 (349.11, 277.59, 593.34) | After the Giant Door. |
| `o0200_0002` | **1701950** | 11700920 | **Seath's arena (Crystal Cave)** (95.65, 134.05, 864.18) | `c1000_0002` 1700950 (t170010) | 復活ポイントboss (100.0, 134.05, 861.0) | **Disabled until Seath dies** (ev 11700001: SFX 90014, enable after 2 s, register). |
All four are registered in ev 0 with `SetStandardKindlingLevel=0` (normal kindling). MSB SpawnPoint events: 1702960 / 1702961 / 1702962 / 1702950 → the four 復活ポイント regions above. Official warp-menu names not resolvable from data (unverified).

### Idle poses / patrols (MSB enemy InitAnimID, DamageAnimID, MovePointNames, PlatoonID — via msbev reader)
| Part(s) | Entity | InitAnimID | Other | Meaning |
|---|---|---|---|---|
| `c0000_0002` Big Hat Logan (cell) | 6032 | **7830** | — | NPC idle pose in the giant cell (sitting/slumped; exact pose unverified). |
| `c2690_0000` key-guard Serpent Soldier | 1700100 | **7000** | — | Sleeping/dozing pose outside your cell (wiki: "sleeping guard"). |
| `c3330_0003`, `c3330_0004` passive Pisacas | 1700150/151 | **9000** | — | Cowering/"frightened" idle. |
| `c3230_0000/0001/0002` Moonlight Butterflies | 1700350/351/352 | **10000** | DamageAnimID **13000**; MovePointNames = logic カゲロウ龍1/2/3移動ポイント00 | Perched until approached (ev 11705160 swaps in 13000 = take-off), then fly to their move point. |
| `c2690_0014/0015` fleeing Serpent Soldiers | 1700103/102 | -1 | **PointMoveType=1, PlatoonID 17000** | Grouped pair (platoon) used for the run to the Giant Door. |
| everyone else in m17 (Seath, Rhea, Sieglinde, Crystal Knight, hollow Logan, Channelers, hollows, golems, Pisacas, clams, lizards, mimics, tusks) | | -1 | no move points / platoons | Default idle; Pisaca ramp patrols come from AI/think + regions 1702710-1702749, not from MSB move points. |

## 4. Enemies (every MSB enemy part in `m17_00_00_00`; Crystal Cave parts listed under Area 2)
Respawn: everything respawns unless listed as non-respawning (ev 11700810 "dead stays dead": slots for 6610, 1700450-453, 1700190/191, 1700501/502, 1700150/151; Mimics via ev 11700900).

**Model key**: c2800 Crystal Hollow (NpcParam 280000 = sword, lot 28000000 Crystal Straight Sword/Caduceus Kite Shield; 280001 = **bow**, lot 28000100 Longbow; hp 85/62; 400 souls); c2370 Channeler (237001/237003, lot 23700100 Channeler's Trident, 800 souls); c2690 Serpent Soldier (269001/269002); c2700 Serpent Mage (270001, lot 27000100 "Flamberge", 2000 souls); c2710 Crystal Golem (271000/271002, lot 27100000 Blue Titanite Chunk, 1200 souls); c2711 Golden Crystal Golem (271101, no drop, 2500 souls); c2780 Mimic (278020/278021); c3300 Crystal Lizard (330010); c3330 Pisaca (333000-333003); c3461 Armored Tusk - Reinforced (346100); c3490/c3491 Vagrants (online-only); c5290 Seath; c1000 bonfire hosts.

### 4.1 Seal corridor / entrance room (y≈200)
| Enemy | Model | Part | Entity | Notes |
|---|---|---|---|---|
| Armored Tusk (Reinforced) | c3461 | c3461_0000 (253.4, 199.95, 434.0), c3461_0001 (225.25, 199.99, 469.47) | 1700190, 1700191 | NpcParam 346100, hp 256, 1000 souls, drop lot 34610000 **Fang Boar Helm**. **Non-respawning** (ev 11700810 slots 5/6). Leash regions イノシシ巣に帰る領域 1702750-1702752. |
| Bonfire host | c1000 | c1000_0003 | 1700962 | talk 170002 (bonfire ESD). |

### 4.2 Entrance-lift top / first room (y 274-277.6)
| Enemy | Model | Parts | Notes |
|---|---|---|---|
| Crystal Hollow (sword) | c2800 | c2800_0006 (261.6,277.1,505.4), _0007 (265.8,274.9,499.3), _0009 (243.3,274.9,498.9), _0010 (269.6,277.6,526.1), _0011 (272.8,277.6,508.6), _0046 (268.7,277.6,504.2) | 6; _0046 think 280002 (others 280000). |
| Crystal Hollow (bow) | c2800 | c2800_0008 (263.4,277.6,508.8) | 1. |
| Crystal Golem | c2710 | c2710_0010 (269.90, 277.59, 503.39), entity **1700510**, think 271002 | **DLC Broken Pendant fallback**: ev 11700700 — if the player owns the DLC, this golem is dead, flag 11200530 (Darkroot) ON and flag 1121 (Dusk state) OFF → awards lot **27100200 Broken Pendant** once (flag 11700700). Interpretation (unverified): failsafe if Dusk was freed but the Darkroot pendant golem was missed. |

### 4.3 Library hall 1 (rotating staircase A, y 277-310)
| Enemy | Model | Parts | Notes |
|---|---|---|---|
| Channeler | c2370 | c2370_0000 entity **1700300** (280.0, 277.59, 524.2) | NpcParam 237001. Teleports (ev 11705201 slots 0-2: on AI event-message 1000 → warp to 六目ワープ point 1702301 / 1702306 / 1702305, set home, anim 7000). Buffs nearby enemies (Channeler dance). |
| Crystal Hollow (sword) | c2800 | y277: _0012 (305.5,277.6,534.8), _0019 (294.1,277.6,538.4); y288: _0015 (306.1,288.6,532.8), _0020, _0021, _0022, _0023 (285-305, 288.6, 511-518), _0028 (317.5,288.6,543.0); y299: _0034 (306.8,299.6,540.9); y310: _0038 (273.9,310.6,537.1) | 9; _0019-_0023, _0028, _0034, _0038 think 280002. The four on the y288 balcony guard the Twinkling Titanite corpse. |
| Crystal Hollow (bow) | c2800 | _0013 (294.5,281.4,536.6), _0014 (295.6,283.6,541.4), _0024 (288.7,288.6,550.2), _0035 (299.9,299.6,525.1), _0036 (293.8,310.6,523.5), _0037 (291.6,310.6,554.7), _0045 (316.5,310.7,541.7) | 7. |
| Mimic | c2780 | c2780_0001 entity **1700401** (273.07, 288.62, 508.23) | NpcParam 278021, hp 496, 3000 souls; drop 27802010 Symbol of Avarice (0.1%) + 27802011 **Knight Shield [Crystal]**. Nest region 1702011. Non-respawning (ev 11700900 slot1). Mimic scripts: ev 11705010 (grab on "Open": player anim 7521), 11705020/30/40 (Lloyd's Talisman sleep, SpEffects 5420-5424), 11705050 (return to nest in singleplayer), 11705060 (client sync). |
| BP Channeler | c2370 | c2370_0002 **1700900** (301.35,288.62,512.09), c2370_0003 **1700901** (284.98,310.64,510.18) | NpcParam 237003. **Pure-Black World Tendency only** (ev 11705080/81/82; AI command 1 on load ev 11705240). |

### 4.4 Seath gondola top / top floor (y 362-375)
| Enemy | Model | Part | Entity | Notes |
|---|---|---|---|---|
| **Crystal Knight** | c0000 | c0000_0005 (313.60, 362.55, 524.20) rot -160 | **6610** | NpcParam/CharaInit 6610 "Crystal Knight": Crystalline Helm/Gauntlets/Leggings (130000/132000/133000), Crystal Greatsword 304000, shield 1471005; 3000 souls; no item lot (ItemLot 6610 does not exist). **Never respawns** (ev 11700810 slot0); humanity event value base 8988. Little-known human enemy (wikidot: "crystal-armored NPC, 3000 souls"). |
| Crystal Lizard | c3300 | c3300_0000 (300.58, 372.82, 509.60) | 1700450 | On the stairs to the event fog. Drops lot 33001000 **Twinkling Titanite** (+ chained random material) on kill; non-respawning (ev 11700810 slot1). |

### 4.5 First Seath arena (y≈388-395)
| Enemy | Model | Part | Entity | Notes |
|---|---|---|---|---|
| Seath the Scaleless (event, invincible) | c5290 | c5290_0000 (295.5, 389.0, 538.0) rot 48 | **1700700** | NpcParam 529001; see §5a. Removed once the real fight has started (flag 11700000). |
| Big Hat Logan (Hollow) | c0000 | c0000_0006 (282.87, 388.44, 527.10) | **6033** | NpcParam/CharaInit 6032 (Tin Crystallization Catalyst). Disabled unless flag 1099; team Hostile (9). See §6. |

### 4.6 Prison tower (Archive Tower) — route order, top cell level down to the Pisaca nest
| Enemy | Model | Part | Entity | Notes |
|---|---|---|---|---|
| **Serpent Soldier (key guard)** | c2690 | c2690_0000 (378.48, 269.54, 557.75) rot -70 | **1700100** | NpcParam 269001: **hp 1**, lot 26900100 **Archive Tower Cell Key** (pickup flag 51700990). Sleeping guard just outside cell door 00; ev 11700130 disables his map collision and gravity (stands in place); once 51700990 is set he is disabled/killed forever. |
| Crystal Hollow (sword) | c2800 | c2800_0016 (377.7,269.8,571.6), _0017 (377.8,269.8,578.6), _0018 (379.6,269.8,577.4) | — | think 280002; beside cell door 01 (the "three respawning crystal soldiers" of the prison). |
| Serpent Soldier ("fleeing") | c2690 | c2690_0015 (386.82,267.93,548.96), c2690_0014 (398.79,264.31,541.98) | **1700102**, **1700103** | NpcParam 269002 (lot 26900200 Man-serpent Greatsword), think 269010, MSB point=1. ev 11705140: once flag 11705101 is set (alarm/escape event) → home set to nests 1702150/1702151 at the Giant Door (387,292.5,584) and AI cmd 10 → they run up to the Giant Door; command cleared on arrival. |
| Crystal Hollow (sword) | c2800 | c2800_0000 (396.64, 264.46, 534.22) | **1700200** | Inside cell 02 (door 02). Region 結晶不死人が急襲00 1702200 ("crystal hollow ambush") at the door. |
| Rhea of Thorolund (Hollow) | c0000 | c0000_0003 (433.05, 259.78, 553.79) | **6073** | Cell 03 (Extra Key). See §6. |
| Crystal Hollow (sword) | c2800 | c2800_0003 (431.2,249.9,579.3), c2800_0004 (427.7,249.9,584.0) | — | Inside cell 04 (door 04). Ambush region 1702201 at door 03. |
| Crystal Hollow (sword) | c2800 | c2800_0005 (417.0, 244.6, 593.5) | — | Inside cell 05 (door 05), with the Archive Prison Extra Key corpse. |
| Serpent Mage | c2700 | c2700_0000 (376.45, 241.85, 535.31) | **1700101** | Gramophone room; NpcParam 270001 (lot 27000100). |
| Serpent Soldier | c2690 | c2690_0002 (382.85,241.81,544.62), c2690_0003 (389.06,241.81,539.74) | — | Gramophone room; NpcParam 269002. |
| **Pisaca** | c3330 | c3330_0000 1700110 (379.66,229.57,544.31); _0001 1700111 (385.43,229.57,539.67); _0002 1700112 (376.11,229.58,537.40); _0005 1700120 (376.96,229.58,535.60); _0006 1700116 (379.90,229.58,542.97); _0007 1700117 (378.01,229.58,540.92); _0008 1700118 (384.69,229.58,538.00); _0009 1700113 (383.27,229.58,536.50); _0010 1700114 (381.25,229.58,539.94); _0012 1700115 (380.50,229.58,534.17) | 10 | Pisaca nest (ビシャーチャの巣) floor. NpcParam 333000/333003 (lot 33300000 **Humanity**), think 333000-333003. **Alarm-driven AI** (ev 11705110, slots 0-19): alarm ON + player in alert zone 1702100 or nest 1702101 → AI cmd 1 (alert / roam the spiral ramp; patrol point groups 巡回ポイント 第1-4グループ regions 1702710-1702749); alarm ON + player away → cmd 2; alarm OFF → cmd 3. "Return-to-nest" zones A/B/C 1702760-62 → nests 1702770-72. Pisaca grabs inflict **curse** (DS1 mechanic). |
| Pisaca (passive / "frightened") | c3330 | c3330_0003 (378.81,229.58,531.47), c3330_0004 (376.49,229.59,531.47) | **1700150**, **1700151** | NpcParam 333001 (hp 320, lot 33300100 **Soothing Sunlight**) / 333002 (hp 260, lot 33300200 **Bountiful Sunlight**); think 333005; wander frightened-move points 1702745-1702748; **non-respawning** (ev 11700810 slots 12/13). |
| BP Pisaca | c3330 | c3330_0020 (387.5,229.57,541.1), c3330_0021 (382.0,229.57,546.6) | **1700908**, **1700909** | NpcParam 333003; Pure-Black WT only. |
| Big Hat Logan (sane, imprisoned) | c0000 | c0000_0002 (371.0, 229.59, 532.8) | **6032** | Giant cell (door 06). See §6. |
| Bonfire host | c1000 | c1000_0001 | 1700961 | Cell bonfire. |

### 4.7 Giant-door balcony (y≈292.8)
| Crystal Hollow (sword) | c2800 | c2800_0025 (378.5,292.8,599.4), c2800_0044 (360.5,292.8,577.3) | _0044 think 280002 |
|---|---|---|---|
| Crystal Hollow (bow) | c2800 | c2800_0026 (356.0,292.8,576.7) | |

### 4.8 Library hall 2 (rotating staircase B, y 277-310)
| Enemy | Model | Parts | Notes |
|---|---|---|---|
| Channeler | c2370 | c2370_0001 **1700301** (335.57,299.62,560.74); c2370_0006 **1700302** (320.26,310.64,578.92) | Teleport points: 1700301 → 1702311-1702314 (ev 11705201 slots 10-13), 1700302 → 1702321/1702322 (slots 20-21). 1700302 is the high one (reach by jumping across rotated stairs). |
| Crystal Hollow (sword) | c2800 | _0031 (326.6,277.6,569.1), _0032 (326.6,277.6,574.0), _0030 (308.4,288.6,569.0), _0027 (327.9,294.3,558.1) | _0027 think 280002. |
| Crystal Hollow (bow) | c2800 | _0033 (321.2,283.5,575.2), _0029 (340.1,288.6,565.1), _0042 (314.4,299.7,597.8), _0039 (347.6,310.7,572.6) | |
| BP Channeler | c2370 | c2370_0004 **1700902** (313.94,299.65,588.08), c2370_0005 **1700903** (339.39,310.64,594.76) | Pure-Black WT only. |
| Vagrants | c3490/c3491 | c3490_0001 1703900 (322.12,277.59,566.04) passive 349013; c3491_0000 1703902 / c3491_0001 1703901 (340.99,283.45,575.71) aggressive 349213/349113 | Online-only (spawn from other worlds' dropped items). |
| Bonfire host | c1000 | c1000_0000 1700960 | |

### 4.9 Secret rooms / lower corridor (y 271-277)
| Mimic | c2780 | c2780_0000 **1700400** (304.38, 277.59, 615.92) | NpcParam 278020; drop 27802000 Symbol of Avarice (0.1%) + 27802001 **Falchion [Enchanted]**; nest 1702010; non-respawning. Disguised as the 4th chest of the large secret room. |
|---|---|---|---|
| Crystal Hollow (sword) | c2800 | c2800_0043 (301.35, 271.80, 614.47) | think 280002; lower corridor by the Prism Stone chest. |

### 4.10 Crystal Golem garden (y 244-267)
| Enemy | Model | Parts | Notes |
|---|---|---|---|
| Crystal Golem | c2710 | _0000 (294.4,257.1,662.0), _0001 (377.8,263.3,667.8), _0002 (295.5,244.7,694.4), _0004 (391.3,263.6,616.1), _0005 (325.1,265.9,607.2), _0008 (299.4,246.5,693.3), _0009 (388.7,263.6,619.8), _0011 (291.3,265.7,627.5), _0012 (312.4,250.3,733.9), _0013 (385.3,264.4,617.2), _0014 (354.0,267.2,588.9) | 11; NpcParam 271000; think 271000 (0000,0002,0008,0011) or 271001 (the rest). Crystal-breath / ground slam; knockback off ledges. _0012's exact zone (garden south edge vs cave descent) unverified. |
| **Golden Crystal Golem (Sieglinde)** | c2711 | c2711_0000 (346.46, 264.53, 648.11) | **1700500**; NpcParam 271101 (hp 544, 2500 souls, no drop). **Disabled unless Sieglinde quest state** (ev 11700538); drawn with DispMask bit 1 ON (Sieglinde visible inside). |
| Sieglinde of Catarina | c0000 | c0000_0004 (same spot) | **6291**; see §6. |
| BP Crystal Golem | c2710 | _0015 **1700904** (290.0,265.0,630.6), _0016 **1700905** (322.3,266.9,605.2), _0017 **1700906** (347.7,259.3,676.0), _0018 **1700907** (350.5,259.8,678.0) | NpcParam 271002; Pure-Black WT only. |

## 5. Bosses

### 5a. Seath the Scaleless — first encounter (scripted loss / "capture")
- **Model/part**: `c5290_0000`, entity **1700700**, NpcParam 529001 (teamType 1 = boss), NameID **5290** "Seath the Scaleless".
- **Arena**: top of the Archives, region ボス部屋(イベント) BGM **1702890** (box 52×35×48 at (289.7, 388.0, 533.4)).
- **Fog**: `o7901_0000` entity **1701890** (SFX 1701891) at (272.24, 388.39, 517.39). Enter: ev 11705380 (host, "Traverse the white light", entry line 1702898, face 1702897, player anim **7410**); ev 11705381 white phantoms (after 11705386). **Two-way**: ev 11705382 "Leave through the white light" (10010404) at line 1702895 → face 1702894, anim 7410, hide health bar.
- ev 11705386: entering 1702896 → boss-room notification + multiplayer buffs on 1700700.
- ev 11705383: AI enabled + boss bar while the player is inside 1702890; restarts (AI off) when everyone leaves.
- ev 11705384: BGM map-sound **1703801** while inside.
- ev 11705385: **Character immortality ON** + SpEffects **5441/5442/5443** ("White Dragon Seath invincible SFX display 1-3"). When flag **11700000** (real fight begun in the Crystal Cave) is ON: Seath 1700700, arena object `o7510` (**1701050**, (296.76, 388.15, 540.71)) and the fog 1701890 are disabled.
- **Capture**: ev 11705100 — host dies inside 1702890 → `Set Player Respawn Point` **1702900** (inside the prison cell) + save. Souls/humanity are lost normally (bloodstain in the arena) unless a Ring of Sacrifice is worn.
- **Items in this arena** (collect when the event Seath is gone or during the fight): chest `o0110_0003` (ObjAct 11700603) **Large Magic Ember**; corpse `o0500_0004` (293.07, 395.69, 512.09) **Soul of a Great Hero**.

### 5b. Seath the Scaleless — real fight (Crystal Cave)
- **Model/parts**: `c5290_0001` entity **1700800** (107.32, 134.05, 852.27), NpcParam 529000 (hp from param 2210; souls per wiki 60,000 (unverified; NpcParam getSoul=0)); tail `c5291_0000` entity **1700801** (105.38, 138.85, 855.58), NpcParam 529100 "Seath's Tail" (disabled until cut).
- **Arena**: BGM box **1702990** (70×50×70 at (106.1, 133.0, 849.8)); in-engine-cutscene trigger **1702999**; boss-start line 1702996; NoSignZone 1704050 in front of the fog.
- **Primordial Crystal**: `o7500_0000` entity **1701800** at (84.86, 134.55, 872.93) (behind Seath).
- **Fog**: `o7900_0000` entity **1701990** (SFX 1701991) at (135.25, 136.5, 829.7). **Not present on the first visit**: ev 11700000 disables it until the intro.
- **Intro (ev 11700000, Restart, runs once)**: Seath disabled, crystal invulnerable → host enters 1702999 → boss notification, buffs, flags 11705390/11705391/11705393 ON (black-phantom invaders wait for flag 703) → **cutscene 170000** (Play Cutscene and Warp Player to point **1702801**; unskippable in multiplayer) → crystal becomes destructible, fog 1701990 + SFX spawned, Seath short-warped to **1702800** and enabled. (Wiki: the Maneater clams in the antechamber can follow you in on this first entry because there's no fog yet.)
- **Later entries**: ev 11705390 (host, line 1702998 → face 1702997, anim 7410), 11705391 (white phantoms), 11705393 (start box 1702996 → notification/buffs), 11705392 (AI on + boss bar NameID 5290 when inside 1702990), 11705394/95 (BGM **1703800**).
- **Immortality mechanic (ev 11705396)**: Seath gets SpEffects **5440** (HP auto-recovery) + **5441-5443** + immortality; object-following SFX **170004** on the crystal (dummypoly 100). When `Object Destroyed 1701800` → SFX removed, Seath plays anim **7000**; when Seath sends event-message **500** → SpEffects cleared, immortality OFF → Seath can now die. Breaking the crystal is mandatory.
- **Tail cut (ev 11705397)**: `Create NPC Part` id **5290**, part HP **330** on Seath. When part HP ≤ 0 while Seath alive → Seath anim **8000**; on event-message **400** → tail chr 1700801 warped to Seath dummypoly **150** and enabled, Seath DispMask bit 1 OFF / HitMask bit 1 ON (tail mesh hidden), tail anim **8100**, AI command 20 → awards lot **52910000 Moonlight Greatsword** (living/hollow player only). If reloaded after the cut: tail stays hidden.
- **Status**: Seath's breath/crystal attacks inflict **Curse** (DS1; common ev 757 shows status msg 10010660 the first time you're cursed).
- **Death (ev 11700001)**: flag **14**, Handle Boss Defeat, **achievement 38**, fog removed, bonfire **1701950** appears (SFX 90014, registered flag 11700920).
- **Rewards** (common ev 970 slot 19, flag 14): lot **2640 Bequeathed Lord Soul Shard (goods 2503)** + lot 9000 Humanity (5 s later). (Seath's own NpcParam item lots 52900000/52900100 are empty.)
- **World changes after flag 14**: Giant Door auto-opens (ev 11700140); Logan advances (ev 11700532/11700533); m18 Lordvessel can take soul 2503.
- **DSR-only**: ev 11705843/11705846 (param flag 14): after Seath is dead, during a multiplayer session the boss fog 1701990 is re-shown and walking through it runs DSR instruction 2003[47] (purpose unverified; post-boss co-op fog).

## 6. NPCs (Archives)

### Big Hat Logan (questline end)
- **Parts**: sane `c0000_0002` entity **6032** (NpcParam 6031 / CharaInit 6031 "Big Hat Logan - Duke's Archives", Logan's Catalyst) talk **170603**; hollow `c0000_0006` entity **6033** (NpcParam/CharaInit 6032 "(Hollow)", Tin Crystallization Catalyst), talk 0. Humanity event base 8334 (both). Death lot **7030 Big Hat** (+ chained 7031 Tin Crystallization Catalyst, 7032 White Dragon Breath) — both NpcParams use 7030.
- **State flags 1090-1109** (common ev 50 defaults 1090). Arrival from Sen's Fortress/Firelink = **1092** (other docs).
- ev 50: 6032 disabled unless any of 1093-1096; if **11700594** ON → warp 6032 to library point **1702501** (300.2, 277.59, 594.25) on load; 6033 disabled unless **1099**, team Hostile Ally.
- **ev 11700530**: 1092 & player inside prison zone **1702410** & not hostile (1096 OFF) → state **1093** (imprisoned), flag 11020694 (Firelink side) ON, enable 6032, cell-door flag 61700320 OFF, giant-cell door ObjAct 1701506 re-armed (ObjActParam 27401 index 0/1 on, 2/3 off).
- **ev 11700531**: 1093 & giant cell door opened (ObjAct flag **61700320**, Giant Cell Key) & alive → **1094**, 11700594 ON.
- **ev 11700532**: (1094 & host & player outside 1702410) **or** (1093 & Seath dead) → **1095**, enable, warp to **1702501**, set home (he now stands in the small secret room off library hall 2).
- **Shop** (ESD t170603 opens ShopLineupParam **5000-5099**; Paramdex labels them "Griggs - Logan in DA"): Soul Arrow, Great Soul Arrow, Heavy Soul Arrow, Great Heavy Soul Arrow, Homing Soulmass, Soul Spear, Magic Weapon, Magic Shield, **Homing Crystal Soulmass, Crystal Soul Spear, Crystal Magic Weapon**. Sold-out flags 11707000-11707070 (basic eight) and 11707090/11707100/11707110 (crystal three). Common ev 721: all eight bought → flag 721; then all three crystal bought → flag **728**.
- **ev 11700533 (goes hollow)**: 1095 & **Seath dead (14)** & **728** & **11700592** (set by Logan's ESD after a dialogue step) & not hostile → batch 1090-1109 OFF, **1099** ON, disable 6032, enable **6033** (hollow, hostile, at the first Seath arena — region ロガーン敵対場所 1702502 (285.05, 388.39, 529.24)), flag **11700595** → enables chest `o0110_0014` (entity 1701664, at his old spot (299.98, 277.59, 593.79)) = lot 1700640 **Sage Robe + Traveling Gloves + Traveling Boots + Logan's Catalyst** (lots 1700640-1700643).
- ev 11700510 (slot0): damage to ≤90% HP by player → wait 703 → flag **1096** hostile, team 9, save.
- ev 11700520 (slots 0/1): death → batch off, **1097** dead; on reload forced treasure drop.
- Duplicate unused ESD **t170643** (identical to t170603; no MSB character uses talk 170643).

### Sieglinde of Catarina
- `c0000_0004` entity **6291**, NpcParam 6290, CharaInit 6290 (Catarina gear), talk **170629**. Humanity event base 8454. State flags 1540-1569.
- ev 50: enabled only if 1542 or 1547.
- **ev 11700538**: golden golem 1700500 disabled; if (1547 OFF & **1540** & Siegmeyer alive/not hostile: 1512 OFF, 1513 OFF, any of 1501-1511 ON & golem alive) or 1541 → **1541** (trapped), golem enabled with DispMask bit 1 ON.
- **ev 11700539**: 1541 & golem 1700500 dead → **1542**; Sieglinde warped to golem dummypoly 101 and enabled (freed).
- **ev 11700540**: after her dialogue sets **11700593** → she leaves on the next load (disabled; continues at Firelink — other doc).
- ev 11700510 slot2 (hostile 1547), 11700520 slot2 (death → 1548).
- **Unused**: ESD **t170628** (Siegmeyer dialogue in the Archives, checks Siegmeyer flags 1495/1496/1512/1513, sets 11700591 → common ev 911 awards lot 1020 Pierce Shield) — no MSB character uses talk 170628 (cut content).

### Rhea of Thorolund (Hollow)
- `c0000_0003` entity **6073**, NpcParam 6070, CharaInit **6071** "Rhea of Thorolund (Hollow)", talk 0. Team forced **Hostile Ally (9)** in ev 50.
- **ev 11700545**: (1175 Rhea back at Undead Parish & **724** (common ev 724: all Parish dialogue flags 11017050-11017130 heard) & 1176 OFF & 1179 OFF) or 1181 → **1181**, enabled in **cell 03** (Extra Key).
- Death (ev 11700520 slot3) → 1177; NpcParam lot **6070 Ivory Talisman** (+ chained 6071 Pendant, 6072 Humanity).

### Crystal Knight (6610) — hostile human enemy, see §4.4.

### Bonfire hosts — c1000 parts 1700960/1700961/1700962/1700950 (talk t170000/t170001/t170002/t170010 = standard bonfire menus).

## 7. Items / treasures (route order; MSB treasure + ItemLotParam; consecutive lot rows are awarded together)
| # | Part / obj | Position | Lot → item(s) | Flag | Where |
|---|---|---|---|---|---|
| 1 | corpse `o0500_0000` | (291.98, 277.59, 536.33) | 1700000 Soul of a Brave Warrior | 51700000 | Library hall 1 ground floor, under rotating staircase A |
| 2 | corpse `o0500_0001` | (304.21, 288.90, 512.80) | 1700010 **Twinkling Titanite** | 51700010 | Hall 1, y288 balcony (4 hollows guard it) |
| 3 | chest `o0110_0001` (1701651) | (264.43, 288.62, 517.56) | 1700510 **Twinkling Titanite** | 51700510 | Hall 1 y288 left corridor |
| 4 | Mimic 1700401 | (273.07, 288.62, 508.23) | 27802011 **Knight Shield [Crystal]** | — | Hall 1 y288 |
| 5 | chest `o0110_0015` (1701665) | (272.84, 283.70, 526.39) | 1700020 **Avelyn** | 51700020 | On top of a bookshelf in hall 1 (needs rotated stairs/jump) |
| 6 | chest `o0110_0002` (1701652) | (296.13, 299.62, 550.34) | 1700520 **Twinkling Titanite** | 51700520 | Hall 1 y299 |
| 7 | chest `o0110_0003` (1701653) | (288.58, 388.41, 533.79) | 1700530 **Large Magic Ember** | 51700530 | First Seath arena |
| 8 | corpse `o0500_0004` | (293.07, 395.69, 512.09) | 1700040 Soul of a Great Hero | 51700040 | First Seath arena |
| 9 | Serpent Soldier 1700100 drop | (378.48, 269.54, 557.75) | 26900100 **Archive Tower Cell Key** (2004) | 51700990 | Outside your cell |
| 10 | corpse `o0504_0000` | (374.46, 279.95, 557.55) | 1700070-1700074 **Maiden Hood/Robe/Gloves/Skirt + White Seance Ring** | 51700070 | Above the cells, drop-off from the spiral ramp |
| 11 | corpse `o0500_0006` | (379.07, 259.64, 563.63) | 1700060 Soul of a Brave Warrior | 51700060 | Below your cell via back door 01 drop |
| 12 | corpse `o0500_0021` | (407.29, 244.81, 596.06) | 1700210 **Archive Prison Extra Key** (2020) | 51700210 | Cell 05 area, low ramp |
| 13 | corpse `o0500_0008` | (434.42, 259.79, 553.45) | 1700080 Large Soul of a Brave Warrior | 51700080 | Hollow Rhea's cell 03 |
| 14 | chest `o0110_0013` (1701663) | (373.23, 241.82, 532.03) | 1700630 **Archive Tower Giant Door Key** (2005) | 51700630 | Gramophone room |
| 15 | Pisaca 1700150 / 1700151 | nest floor | 33300100 **Soothing Sunlight** / 33300200 **Bountiful Sunlight** | — | Passive Pisacas |
| 16 | Pisaca (regular) drops | nest floor | 33300000 Humanity (chance) | — | |
| 17 | corpse `o0504_0001` | (357.00, 229.60, 513.10) | 1700200 **Fire Keeper Soul** | 51700200 | Logan's giant cell (door 06) |
| 18 | chest `o0110_0004` (1701654) | (340.80, 310.64, 594.15) | 1700540 **Strong Magic Shield** | 51700540 | Top of library hall 2 |
| 19 | corpse `o0500_0012` | (360.00, 266.25, 586.96) | 1700120 Blue Titanite Chunk | 51700120 | Below hall-2 balcony / garden edge |
| 20 | chest `o0110_0006` (1701656) | (299.95, 277.59, 590.30) | 1700560 Blue Titanite Chunk | 51700560 | Small secret room (bookshelf 00) |
| 21 | chest `o0110_0014` (1701664) | (299.98, 277.59, 593.79) | 1700640-1700643 **Sage Robe, Traveling Gloves, Traveling Boots, Logan's Catalyst** | 51700640 | Small secret room; **object disabled until 11700595** (Logan hollowed) |
| 22 | chest `o0110_0008` (1701658) | (309.39, 277.59, 600.36) | 1700580-1700583 **Channeler set** (Six-Eyed Helm, Robe, Gauntlets, Waistcloth) | 51700580 | Large secret room |
| 23 | chest `o0110_0009` (1701659) | (300.59, 277.59, 606.94) | 1700590 **Archive Tower Giant Cell Key** (2006) | 51700590 | Large secret room |
| 24 | chest `o0110_0010` (1701660) | (313.24, 277.59, 610.24) | 1700600 **Crystal Ember** | 51700600 | Large secret room |
| 25 | Mimic 1700400 | (304.38, 277.59, 615.92) | 27802001 **Falchion [Enchanted]** | — | Large secret room (4th "chest") |
| 26 | chest `o0110_0016` (1701666) | (297.22, 271.66, 615.35) | 1700050 Prism Stone ×20 | 51700050 | Lower corridor after the movable stairs |
| 27 | corpse `o0500_0007` | (352.58, 260.50, 675.69) | 1700650-1700653 **Crystalline Helm/Armor/Gauntlets/Leggings** | 51700650 | Golem garden |
| 28 | Crystal Golem 1700510 (DLC failsafe) | (269.90, 277.59, 503.39) | 27100200 **Broken Pendant** (conditions §4.2) | ev flag 11700700 | First room |
| 29 | Crystal Lizard 1700450 | top stairs | 33001000 Twinkling Titanite | — | |
- All chests use ev 11700600 (slot n ↔ chest `o0110_00nn`, ObjAct flags 11700601-11700616): treasure becomes takeable 10 frames after the open ObjAct; on reload opened chests show anim 0 with treasure enabled.
- Orphan treasure entries (no MSB part and/or missing lot): corpses `o0500_0002/0003/0005/0009/0010/0011/0013/0014/0019`, chests `o0110_0005/0007/0011/0012` (lots 1700030/1700090/1700100/1700110/1700130/1700140/1700190/1700550/1700570/1700610/1700620 don't exist) — leftovers, nothing to port.

## 8. Interactive objects & scripted events (Archives)
| Thing | Object / entity | Event(s) | Behaviour |
|---|---|---|---|
| King's seal (entrance) | `o7907_0000` 1701710, SFX 1701711, region 1702610 | 11700085 | Removed permanently once 11800100 (Lordvessel placed); else "Sealed by the Great Lord's power". |
| Entrance gondola | `o7241` 1701130; levers 1701131 / 1701132 (ObjAct 11700220/11700221) | 11700160 s1, 11700105 s2, 11700090 s2-3, 11700150 s1 | Two-stop lift y202↔y275; player anim 8000 on the platform button; lift anims 0/2 (start) then 1/3 (arrive, 300/344 frames); lever at the end where the lift already is → "Contraption does not move" (10010170). Enemy wall hit 1703101. |
| Seath gondola | `o7240` 1701100; levers 1701101 / 1701102 (ObjAct 11700210/11700211) | 11700160 s0, 11700105 s0, 11700090 s0-1, 11700150 s0 | y288↔y362, same logic; enemy wall hit 1703100. |
| Rotating staircases (×2, linked) | A: `o7230_00` 1701200 + moving mesh `o7231` 1701201 (286.4, 277.5, 530.6); B: `o7230_01` 1701210 + `o7231_01` 1701211 (327.5, 277.5, 571.6) | 11705150, 11700200 s0/s1 (flag 11700205) | "Turn handle" (10010503) on either staircase's base (dummypoly 192) → player anim **8010**, both staircases rotate (anim 0 one way / 2 back; 1/3 settle; 180-frame moving mesh), collisions swap (A: hits 1703200↔1703201, B: 1703210↔1703211), navmesh flags 1703010-1703013 toggled. Opens different balconies/shortcuts; route to Avelyn and the high Channeler. |
| Moving bookshelf 00 (secret door) | `o7250` 1701120, lever `o7201_00` 1701121 (ObjAct 11700120) | 11700120 s0 (flag 11700120) | Lever opens the secret ground-floor door to the small + large secret rooms (anim 0, permanent). |
| Moving bookshelf 01 | `o7251` 1701125 (no lever part; ObjAct 11700125 has no MSB part) | 11700120 s5 (flag 11700125) | Opens (anim 1) when the **Giant Door** is opened (flag 11700140). |
| Movable staircase | `o7200_00` 1701110 + moving `o7199` 1701109, lever `o7201_04` 1701111 (ObjAct 11700110) | 11700110 | One-time: lowers the stairs to the lower corridor (hit 1703220 → 1703221). |
| CP2 white-light fog | `o7906_0000` 1701706 (SFX 1701707), regions 1702606/1702607 | 11700083 | Crossable from either side ("Traverse"/"Enter through the white light", player anim 7410); deleted permanently after first crossing. |
| Event-boss fog | `o7901_0000` 1701890 | 11705380/81/82/86 | See §5a (two-way). |
| Boss fog | `o7900_0000` 1701990 | 11700000, 11705390/91/93 | See §5b. |
| Client-only fogs | `o7902_0000` 1701994 (SFX 1701995) top of entrance lift; `o7908_0000` 1701996 (SFX 1701997) top of Seath lift | ev 0 | Disabled for the host (only phantoms see them). Flag 405 is set ON for the host in ev 0 and OFF once the Giant Door is opened (purpose unverified — likely multiplayer gating of the prison section). |
| Cell doors (ObjAct "Door") | 00 `o7401_00` 1701500 (378.42, 269.85, 556.50) — your cell, front; 01 `o7401_01` 1701501 (378.90, 269.86, 573.20) — your cell, back; 02 `o7401_02` 1701502 (y264.5); 03 `o7401_03` 1701503 (y259.8); 04 `o7401_04` 1701504 (y249.9); 05 `o7401_05` 1701505 (y244.6); 06 `o7401_06` 1701506 (372.82, 229.59, 532.79) — Logan's giant cell | 11700300 slots 0-6 | ObjAct flags 61700300/61700311/61700302/61700313/61700304/61700305/**61700320**. Key per door (from the "Used <key>" message): **Cell Key 2004** → 00, 02, 04, 05; **Prison Extra Key 2020** (ObjActParam 17401) → 01, 03; **Giant Cell Key 2006** (ObjActParam 27401) → 06. Navmesh 牢屋扉00-06 regions. |
| Giant Door | `o7400` 1701300 (385.57, 292.66, 584.27) | 11700140 / 11700141 | With **Giant Door Key 2005** from the prison side: "Open" (dummypoly 100) → player anim **7120**, door anim 0, "Used Archive Tower Giant Door Key"; also opens automatically when Seath is dead (14). Without key: "It's locked" (both sides, dp 100/101). |
| Alarm / gramophone | machine `o7404` 1701400 (390.46, 241.80, 550.03); gramophone `o7402` 1701410 (386.14, 229.59, 545.34); lever `o7202` 1701401 (ObjAct **11705105**, loop ObjAct toggling flag **61700105**) | 11705101/02/03/08, ev 0 | **Escape trigger**: host, cell-key flag 51700990, 11700133 OFF, player in alert cylinder **1702100** → first time: **cutscene 170010** (serpent man winds the gramophone), gramophone anim 0, flags 11700002 + alarm **61700105** ON. Alarm ON → siren map-sound **1703500**, machine anim 0 then loop 1. Pulling the lever while ON → siren off, machine anim 2, flag **11700133** (no auto-retrigger); pulling while OFF → back on. ev 0: if the map loads with the player inside his cell (1702410 + 1702402/1702403) the alarm flag is cleared. Region サイレン (siren) box covers the tower. |
| Ladders | 1701140 `o7300` (387.4, 276.7, 582.0) prison → Giant Door level; 1701141 `o7301` (394.7, 229.5, 541.7) Pisaca nest up; 1701142 `o7302` (303.7, 264.7, 630.5) garden; 1701143 `o7303` (321.9, 277.6, 594.4) hall 2 → secret-door floor | ev 0 Register Ladder (flags 11700010-11700017) | |
| Chests ×16 | `o0110_0001..0016` 1701651-1701666 | 11700600 s1-16 | §7. |
| Crystal statues | `o0101_0000..0007` 1701900-1701907 | ev 0 Register Statue (type 1 Crystal) | Online statues of cursed players. |
| Bonfire spawn | `o0200_0002` 1701950 | 11700001 | §3. |
| Developer messages (MSB Message events) | 魔法血文字3 (271.30, 388.45, 532.70) on h0035B0; 魔法血文字4 (284.00, 184.05, 724.61) and 5 (222.29, 157.62, 736.58) on h0073B0 | MSB | Blood_writing FMG: **7000 "Try resignation"** (hidden flag set; first Seath arena), **7001 "Path ahead"**, **7002 "Imminent fall..."** (Crystal Cave, invisible-path hints). Points 魔法血文字1/2 have no message event. |
| Sounds (MSB Sound events) | 1703801 (event-Seath BGM, SoundID 170000000, box 1702890); 1703800 (Seath BGM, same id, box 1702990); **1703500 siren** (SoundID 170000002, region サイレン); crystal-area ambience 170000001 (box 結晶エリアサウンド) | EMEVD toggles 1703500/1703800/1703801 | |
| Key map SFX (MSB) | seal 81707 (1701711); fogs: boss 81700 (1701991), event boss 81701 (1701891), multi 81702 (1701995) / 81708 (1701997), CP2 81706 (1701707); crystal shards 170001 attached to lift **o7240**, 170002 in the cave; m2491 crystal pops 170005-170012 and 170020; mist m2530 170003 by Seath | | |
| Escape-check regions | PCが脱獄した判定 1702400 / (飛び降り) 1702401 | none found | Unreferenced in m17 EMEVD (leftover). |

## 9. Porting notes (Archives)
- **Lordvessel seal gating** needs a cross-map flag (11800100) and a "sealed light" object with an examine prompt.
- **Scripted unwinnable boss + forced respawn**: DS2 needs (a) an immortal Seath variant with regen/invincible VFX, (b) a "die here → respawn point = cell" override (DS2 has no `SetPlayerRespawnPoint` equivalent in the same form — likely needs a death-hook warp), (c) the two-way event fog.
- **Prison system**: 7 keyed doors with three different keys, a 1-HP sleeping key-guard hittable through bars, the siren/gramophone toggle with map-sound and AI-state switching for 14 Pisacas (DS2 AI commands needed), escaping NPCs who relocate to another home point.
- **Pisaca curse** and **Seath curse**: DS2 has petrification/curse buildup; map DS1 Curse (halved HP until cured) to a custom SpEffect.
- **Rotating staircases** (two linked, collision + navmesh swap, moving mesh) and **elevators with two levers each** (DS2 has lifts; collision swap via hit enable/disable).
- **Secret bookshelf doors** (lever-driven), **one-time movable staircase**.
- **Channeler teleport** to fixed points on AI event messages + Channeler buffs.
- **Mimics** (grab on open, Lloyd's Talisman sleep, return-to-nest).
- **World Tendency black phantoms** (10 extra enemies) — optional; DS2 has no world tendency.
- **Golden golem with NPC inside** (DispMask) — needs a visual of Sieglinde in crystal; NPC spawns at golem's dummypoly on death.
- **NPC quest flags** from other maps: Logan (Sen's/Firelink 1090-1109), Rhea (Parish 1170-1189, flag 724), Sieglinde/Siegmeyer (1490-1569), Dusk (DLC Broken Pendant failsafe).
- Logan's hollowing is gated by **buying out his shop** (shop sold-out flags → common 721/728) + a dialogue flag + Seath dead — DS2 shop "sold-out" flags must be mirrored.
- Online-only: Vagrants, crystal statues, black invasion points 1704000/1704001/1704010/1704011 — optional.

## 10. Area completion checklist — Duke's Archives
- [ ] Seal corridor: King's seal (flag 11800100), 2 Armored Tusks (non-respawn, Fang Boar Helm), entrance bonfire 1701962, crystal-statue row.
- [ ] Entrance lift (2 levers + ride button), enemy wall, phantom fog.
- [ ] Library hall 1: 16 crystal hollows (9 sword / 7 bow) + first-room 7 hollows, Channeler 1700300 + 3 teleport points, Mimic 1700401, crystal golem 1700510 (+Broken Pendant failsafe), rotating staircase A, items #1-#6.
- [ ] Seath lift; Crystal Knight 6610; Crystal Lizard 1700450; event fog.
- [ ] Event Seath (immortal) + capture respawn into the cell; arena items #7-#8; hollow-Logan spot.
- [ ] Prison: cell bonfire 1701961; 1-HP key guard; 7 cell doors / 3 keys; siren cutscene 170010 + gramophone toggle; 14 Pisacas (2 passive miracle droppers, 2 BP) with alarm AI; serpent soldiers (2 fleeing, 2 + mage in gramophone room); crystal hollows in cells; Rhea (hollow) cell; Logan's giant cell + Fire Keeper Soul; Maiden set; Extra Key; Giant Door Key; ladders; Giant Door.
- [ ] Balcony (3 hollows) → library hall 2: 2 Channelers (+teleports), 8 hollows, rotating staircase B (linked to A), bonfire 1701960, Strong Magic Shield, vagrants.
- [ ] Ladder → bookshelf-00 lever → secret rooms: 4 chests incl. Giant Cell Key + Mimic 1700400; Logan's library spot 1702501 and later Sage-Robe chest.
- [ ] Movable staircase lever, Prism Stone chest, CP2 one-time fog.
- [ ] Garden: 11 golems (+4 BP), Sieglinde golden golem quest, Crystalline set, Blue Titanite Chunk.
- [ ] NPC quests: Logan (states 1093→1094→1095→1099, shop 5000-5022), Sieglinde (1540→1541→1542→leave), Rhea (1181).

---

# AREA 2 — Crystal Cave (`m17_00_00_00`, place 1701)

## 1. Header
- Same map as the Archives (`m17_00_00_00`); collisions h0060-h0082 carry place name 1701.
- **Entrance**: down the crystal pit from the end of the Golem garden (y≈244 → 215 → ~165).
- **Exits**: back up to the garden; after Seath, the **4th invisible path** lets you walk back out (wiki) or warp from bonfire 1701950.
- **Prerequisite**: none beyond reaching the garden (Seath's first encounter already done in normal progression; not strictly required by EMEVD).

## 2. Progression walkthrough
1. Descend from the garden past a Crystal Golem (`c2710_0006`, y≈215) and Moonlight (crystal) butterflies.
2. Cross **four invisible crystal bridges** — collision-only geometry with no visible mesh; the only cue is falling crystal "snowflakes" (map SFX m2491 "bursting" points 弾ける01-08 and "non-hitting" points 当たらない01-07, plus 舞う結晶破片 結晶洞窟内 crystal-shard SFX near the boss area). Wiki: Prism Stones / arrows / orange-soapstone messages used to probe them.
3. Golden Crystal Golems on narrow paths (knockback deaths), Crystal Lizards, items (Humanity, Blue Titanite Slab/Chunk, Soul of a Hero).
4. Antechamber with six **Man-eater Clams**; first visit has **no fog** — walking into the in-engine-cutscene box triggers Seath's intro (§5b above).
5. Seath's arena: break the Primordial Crystal, (optionally) cut the tail, kill Seath; bonfire 1701950 appears.

## 3. Bonfires
- **1701950** (Seath's arena) — appears only after Seath dies (see Area 1 §3). No other bonfire in the cave.

## 4. Enemies (Crystal Cave parts of m17)
| Enemy | Model | Parts / entity | Notes |
|---|---|---|---|
| Crystal Golem | c2710 | c2710_0006 (274.37, 215.36, 731.31) | Descent from the garden. |
| Moonlight Butterfly (crystal) | c3230 | c3230_0000 **1700350** (262.91,186.93,701.35); c3230_0001 **1700351** (234.53,165.04,743.48); c3230_0002 **1700352** (183.66,143.36,743.54) | NpcParam 323001 (hp 502, 400 souls). ev 11705160: dormant until the player is within **3 m** (350, 351) / **10 m** (352), then CancelAnimation **13000**. ev 11705280 (350, 351): on death award lot **32300100** (≈10 % Blue Titanite Chunk). Flight AI: カゲロウ龍 attack box 1702700 + move points. |
| Golden Crystal Golem | c2711 | c2711_0001 **1700501** (221.60,150.74,734.00); c2711_0002 **1700502** (238.94,165.09,726.23) | NpcParam 271101 (hp 544, 2500 souls, lot 27110100 none). **Non-respawning** (ev 11700810 slots 10/11). |
| Crystal Lizard | c3300 | c3300_0001/0002/0003 **1700451/1700452/1700453** (≈231.2, 160.1-160.5, 725.6-727.9) | NpcParam 330010; non-respawning; each drops lot 33001000 Twinkling Titanite (ev 11700810 slots 2-4). |
| Vagrants | c3490/c3491 | c3490_0002 1703910 (248.22,164.39,748.64) passive; c3491_0002 1703911 / c3491_0003 1703912 (231.12,166.20,696.56) aggressive | Online-only. |
| Man-eater Clam | c3250 | c3250_0000 **1700250** (151.64,144.14,777.71); _0001 **1700251** (158.73,141.05,813.18); _0002 1700252 (151.66,139.68,815.74); _0003 1700253 (146.97,139.63,808.05); _0004 1700254 (135.46,136.71,823.46); _0005 1700255 (147.02,137.82,825.33) | NpcParam 325000 (hp 296, 1000 souls, lot 32500000 Twinkling Titanite / Purging Stone). ev 11705270 (250, 251 only): closed-shell wait anim **9000** until player within **12 m** or hit → open anim **9060**. |
| **Seath the Scaleless** | c5290 | c5290_0001 **1700800** | §5b (Area 1). |
| Seath's Tail | c5291 | c5291_0000 **1700801** | Spawned on tail cut. |
| Bonfire host | c1000 | c1000_0002 1700950 | |

## 5. Bosses
- **Seath the Scaleless** — documented in Area 1 §5b (intro cutscene **170000**, Primordial Crystal 1701800, tail part 5290/HP 330 → Moonlight Greatsword, flag 14, Bequeathed Lord Soul Shard + Humanity, bonfire 1701950).

## 6. NPCs
- None (no talkers, no summon signs; NoSignZone 1704050 at the fog).

## 7. Items
| Part | Position | Lot → item | Flag |
|---|---|---|---|
| corpse `o0500_0015` | (277.74, 197.36, 691.96) | 1700150 **Humanity** | 51700150 |
| corpse `o0500_0016` | (255.90, 159.68, 682.50) | 1700160 **Blue Titanite Slab** | 51700160 |
| corpse `o0500_0017` | (224.67, 151.91, 739.95) | 1700170 Blue Titanite Chunk | 51700170 |
| corpse `o0500_0018` | (176.20, 144.60, 750.58) | 1700180 Soul of a Hero | 51700180 |
| Seath tail cut | — | 52910000 **Moonlight Greatsword** | — |
| Seath kill | — | 2640 **Bequeathed Lord Soul Shard** + 9000 Humanity | 14 |
| Crystal Lizards ×3, butterflies ×2 | — | Twinkling Titanite / Blue Titanite Chunk (chance) | — |

## 8. Interactive objects & scripted events
- Primordial Crystal `o7500_0000` **1701800** (destructible only after intro; invulnerable before; ev 11700000/11705396).
- Boss fog `o7900_0000` **1701990** (ev 11700000/11705390/91/93 + DSR 11705843/46).
- Bonfire `o0200_0002` **1701950** (ev 11700001).
- Cutscene **170000** (Seath intro, warp to 1702801; Seath placed at 1702800).
- Boss BGM map-sound **1703800**; area ambience 結晶エリアサウンド box.
- Invisible-path SFX points (map SFX m2491, effects 170005-170012 "bursting" and 170020 "non-hitting") — visual only.
- Developer messages on the invisible paths: Blood_writing 7001 "Path ahead" and 7002 "Imminent fall..." (MSB Message events on collision h0073B0).
- Butterflies idle with InitAnimID 10000 (perched), DamageAnimID 13000, each with one MSB move point (カゲロウ龍N移動ポイント00).

## 9. Porting notes (Crystal Cave)
- **Invisible bridges**: port DS1 collision for the paths *without* a render mesh (DS2 supports collision-only hkx); keep the falling-crystal SFX so the paths stay discoverable. Bottomless falls on both sides.
- Seath needs: crystal-linked immortality + regen, breakable crystal object, severable tail with a separate character, **curse** buildup, no-fog first entry with an in-engine intro.
- Golden golems/narrow ledges: knockback physics differ in DS2 — test ledge deaths.
- Clam "closed until near" special idle.

## 10. Area completion checklist — Crystal Cave
- [ ] Descent golem, 3 butterflies (dormant radii, Blue Titanite drops), 2 golden golems (non-respawn), 3 crystal lizards (non-respawn), 3 vagrants.
- [ ] 4 invisible bridges (collision only) + crystal-flake SFX.
- [ ] 4 corpses (Humanity, Blue Titanite Slab, Chunk, Soul of a Hero).
- [ ] 6 Man-eater clams (2 with closed-shell idle).
- [ ] Seath: no-fog first entry + cutscene 170000, Primordial Crystal, tail cut, curse, death rewards, post-death bonfire, fog for re-entry.

---

# AREA 3 — Firelink Altar (`m18_00_00_00`, place 1801)

## 1. Header
- **Map**: `m18_00_00_00` (the altar and the Kiln share this map; the Undead Asylum is `m18_01`).
- **Ways in** (all land at region **1802110** 大墓ワープ着地ポイント (40.89, -65.18, 103.74)):
  1. **Frampt** (Firelink Shrine, m10_02 ev **11026200**): Frampt state 1643/1644/1645 + talk choice flag **820** → flag **830**, Frampt 6331 enabled at the altar, **cutscene 100240** (Frampt carries you) → warp → **cutscene 180040**; flag 822.
  2. **Kaathe** (the Abyss, m16_00 ev **11606200**): Kaathe state 1672/1673/1674 + flag **821** → flag **831**, Kaathe 6341 enabled at the altar, **cutscene 160040** → warp → **cutscene 180041**; flag 823.
  3. **The hole in Firelink Shrine** (m10_02 ev **11020351**, region 大墓への穴 **1022111** at (-37.93, -75.0, 91.11) in m10_02): falling in **with flag 710** (Lordvessel obtained) → **cutscene 180060** → warp to the altar; **without 710 the player is killed** (Force Character Death). Hits 1023600/1023601 in m10_02 are removed once 710 is set (ev 11020352).
- **Ways out**: talk to Frampt → flag **824** → ev 11806200: **cutscene 180050** → warp to Firelink (m10_02 point **1022110**) → **cutscene 100250**, Frampt disabled, 830 OFF. Talk to Kaathe → flag **825** → ev 11806201: **cutscene 180051** → warp to the Abyss (m16_00 point **1602110**) → **cutscene 160050**, 831 OFF. Bonfire warp (altar bonfire). Down the stairs into the Kiln once opened.
- **Prerequisite**: the Lordvessel (goods **2510**, from Gwynevere in Anor Londo — other doc).

## 2. Progression walkthrough
1. Arrive at the altar (torches 松明(大)/(小), "fire place" 火の場).
2. **Place the Lordvessel** (ev **11800100**): host with goods 2510, action "**Place Lordvessel**" (10010105) on the altar `o8310` (entity **1801960**, dummypoly 150) → **cutscene 180015** → placed-vessel object `o8311` (**1801110**) enabled → the altar becomes a bonfire (flag **11800992**, standard kindling value 10) → Lordvessel removed from inventory → flag **11800100** (opens the Archives seal; Frampt/Kaathe advance). Without it: "No Lordvessel" (10010174, ev 11800101).
3. **Fill the Lordvessel**: rest at the altar bonfire → menu "**Offer souls to Lordvessel**" (text 15000175; visible while flag 702) → ESD t180100 sets **756** (or "No Lord Souls to offer" 10010171) → ev **11800200**: player anim **7697**, every held lord soul is consumed: **2500 → 11800201, 2501 → 11800202, 2502 → 11800203, 2503 → 11800204**; ev 11800230 slots: vessel flame SFX **180005 / 180006 / 180007 / 180008** for 1 / 2 / 3 / 4 souls (dummypoly 100 on 1801960); kneel loop anim **7701** unless complete.
4. **All four** (ev **11800210**): flag **11800211**, 756 OFF, **cutscene 180010** (the Kiln opens), sealed gate `o8300` (**1801101**, (58.02, -63.43, 107.98)) plays anim 1 (open), bonfire re-registered (kindling value 30). Before that, the gate shows "Locked by some contraption" (10010160) at region **1802100** (ev 11800220).
5. Walk through the gate and down the long staircase (soul-stream SFX 階段_ソウルの流れ 上/下) → Kiln (Area 4).

## 3. Bonfires
| Obj | Entity | Flag | Location | Host chr | Notes |
|---|---|---|---|---|---|
| `o8310` (altar / Lordvessel) | **1801960** | 11800992 | (53.31, -63.34, 106.81) | `c1000_0000` **1800960** talk **180100** (t180100: level up, attune, reverse hollowing, kindle, **Warp** (gated by 710), **Offer souls to Lordvessel**) | Registered only after the Lordvessel is placed (kindling param 10), re-registered at 30 after the Kiln opens (meaning of 10/30 unverified). After Gwyn dies: altar 1801960 + placed vessel 1801110 disabled, post-Gwyn vessel `o8312` (**1801111**) shown, altar SFX removed. Respawn point 復活ポイント00 (50.30, -63.27, 106.10). |

## 4. Enemies
- **Black Knight (Ghost)** ×6 — `c2791_0000..0005`, entity **1800100-1800105**, NpcParam **279100** (hp 355, 0 souls, lot 27910000 none) on the staircase below the gate: (60.15,-64.40,130.85), (69.29,-66.99,129.00), (76.60,-69.53,134.23), (85.59,-72.16,131.26), (92.62,-74.53,136.35), (101.47,-77.08,134.35). ev **11806100** slots 0-5: warped to their start regions 黒騎士移動1-6 (**1802000-1802005**), **AI disabled**, looping walk anim **200**; when one reaches the goal box 黒騎士移動GOAL (**1802006**) it is warped back → an endless spectral procession of Black Knights marching down to the Kiln. Non-combat ambience.
- Box region 騎士 (96.45, -90.09, 117.18, 40×40×20) carries MSB Sound event **180000009** (SoundType 6) — the staircase/knight-procession music zone.
- Altar ambience (MSB): large torches 松明(大)_00-03 SFX 180000, small torches 松明(小) ×28 SFX 530, stair soul-stream SFX **180010** (階段_ソウルの流れ 上/下).

## 5. Bosses — none.

## 6. NPCs
| NPC | Part / entity | Model | Talk | Where | Behaviour |
|---|---|---|---|---|---|
| **Kingseeker Frampt** | `c5330_0016` **6331** | c5330 (primordial serpent) | **180633** | (32.54, -65.18, 101.27), MSB **InitAnimID 7007** (idle pose at the altar) | NpcParam 533000 (hp 11120, 5000 souls, lot 53300000 none). Immortal (ev 50). Present only while **830**. States 1640-1649: ev 11800530 **1643 → 1644** when Lordvessel placed with Frampt present; ev 11800531 **1644 → 1645** when the Kiln opens with Frampt present; ev 11800537 (1642/1643, Lordvessel placed **without** Frampt) → **1649** (he later leaves Firelink — m10_02 ev 11020424). ev 11800539: if attacked (≤90 % HP, player beyond 15 m) or flag 11020598 → **1647**, anim **7009**, gone for good. ESD: soul-trading (feeds items for souls), "take me back" (824). ev 11800541 toggles his enable with 830. |
| **Darkstalker Kaathe** | `c5330_0017` **6341** | c5330 | **180634** | (34.78, -64.93, 97.60), MSB **InitAnimID 7003** | NpcParam 533001. Immortal. Present only while **831**. ev 11800533 **1672 → 1673** (Lordvessel placed with Kaathe); ev 11800534 **1673 → 1674** (Kiln opened with Kaathe); ev 11800538 (1671/1672, placed without him) → **1678**; ev 11800540 attacked or flag 11600590 → **1677**, anim **7005**, gone. ESD includes covenant change / offering menus (Darkwraith; details unverified) and "take me back" (825). |
| Bonfire host | `c1000_0000` **1800960** | c1000 | 180100 | altar | Altar bonfire menu. |
- Unused talk ESDs in the m18 binder: t180000/t180001/t180002/t180010 (bonfire menus with no MSB host), t180600 (300 bytes, stub).

## 7. Items — none at the altar (all altar-area treasure entries are orphaned).

## 8. Interactive objects & scripted events
| Thing | Object / entity | Event | Behaviour |
|---|---|---|---|
| Altar / Lordvessel | `o8310` 1801960, `o8311` 1801110 (placed), `o8312` 1801111 (post-Gwyn) | 11800100/101, 11800200, 11800230, 11800210, 11800001 | Place → fill → open Kiln → extinguished after Gwyn. |
| Sealed gate | `o8300` **1801101** | 11800210, 11800220 | Opens (anim 1) when all 4 souls placed; region 1802100 shows "Locked by some contraption" before. |
| Cutscenes | **180015** (place Lordvessel), **180010** (Lordvessel filled / Kiln opens), **180040/180041** (arrive with Frampt/Kaathe), **180050/180051** (leave with Frampt/Kaathe), **180060** (fall through Firelink hole); Firelink-side **100240/100250**, Abyss-side **160040/160050** | | All are remo cutscenes (`remo/scnNNNNNN.remobnd.dcx`). |
| Client-only fog | `o8051_0000` **1801994** (SFX 1801995) at the stair foot (110.60, -79.13, 120.90) | ev 0 | Disabled for the host; phantoms only. |
| Firelink hole | m10_02 region 1022111 / landing 1022110 | m10_02 11020351/11020352 | See header. |

## 9. Porting notes (Firelink Altar)
- Needs **cross-map transport cutscenes** with serpents (Frampt mouth-carry 100240/180040, Kaathe 160040/180041) — DS2 has no remo; replace with fade + warp + optional custom animation.
- The altar is a **bonfire whose menu gains a custom option** ("Offer souls to Lordvessel") — in DS2 a talk/ESD-driven bonfire or an action-button object next to the bonfire.
- Lord-soul consumption (4 goods ids), staged flame VFX (4 stages), gate opening animation, kneel animation 7697/7701.
- Frampt/Kaathe are giant c5330 serpents — no DS2 equivalent model; needs a ported model or a stand-in.
- The **Firelink hole death-unless-Lordvessel** rule.
- DSR key-item recovery (m10_02 ev 11020800 slots 21-25) also covers the four lord souls and the Lordvessel (flags 50004021-50004025) and the Archive Prison Extra Key (50004019) — they reappear at Firelink if lost (mechanism details: Firelink doc).

## 10. Area completion checklist — Firelink Altar
- [ ] Three entry routes (Frampt, Kaathe, hole) + two exit routes; landing point.
- [ ] Altar: Place Lordvessel (cutscene, flag 11800100, bonfire registration), offer lord souls (4 flags, 4 VFX stages), Kiln opening cutscene + gate.
- [ ] Altar bonfire menu (Warp gated by 710).
- [ ] Frampt and Kaathe presence logic (830/831), their state advances (1643-1649 / 1672-1678), leave-on-attack.
- [ ] 6 ghost Black Knights procession loop.
- [ ] Phantom-only fog at the stair foot.

---

# AREA 4 — Kiln of the First Flame (`m18_00_00_00`, place 1800)

## 1. Header
- **Entrance**: down the staircase from the opened Firelink Altar gate (flag 11800210). Player MSB start point `c0000_0000/0001` (121.65, -79.12, 124.27) at the stair foot.
- **Exit**: none during play except back up the stairs / warp; after Gwyn, the endings.
- **Prerequisite**: Lordvessel placed and filled with all four lord souls.

## 2. Progression walkthrough
1. Stair foot (phantom fog) → the **ash "desert"** (砂漠 GI point (199.5, -97.8, 111.4)) with ruined pillars/debris (`o8150_0000..0009`, decorative), wind ambience (風の音 box).
2. Black Knights along the path (respawning); Black Knight armour corpse at (351, -101, 112.6); **Solaire's summon sign** spot (347.7, -98.0, 157.3) if his quest allows.
3. **Spiral staircase** (螺旋階段 GI (414.4, -110.7, 144.9)) with two Black Knights → **Gwyn's fog** `o8050` (1801990).
4. **Gwyn, Lord of Cinder** in the ember-strewn arena (ボス部屋残り火 SFX points 01-10) beside the dead bonfire.
5. After Gwyn: rest at the arena bonfire → **Link the Fire**, or walk out of the arena → **Dark Lord**. Both start NG+.

## 3. Bonfires
| Obj | Entity | Location | Notes |
|---|---|---|---|
| `o0200_0001` | **1801950** | (387.09, -117.60, 189.77) — Gwyn's arena centre | Disabled until Gwyn dies (ev 11800001: SFX 90014, enabled after 2 s). **Not a registered bonfire** — its only interaction is "**Link the Fire**" (10010108, ev 20). |

## 4. Enemies
| Enemy | Model | Part | Entity | Notes |
|---|---|---|---|---|
| Black Knight (sword) | c2790 | c2790_0001 (187.33, -96.42, 118.35) | **1800200** | NpcParam 279050 (hp 355, 2000 souls) lot 27905000 BK Sword + BK Shield / 27905001 Titanite Chunk. |
| Black Knight (greatsword/UGS) | c2790 | c2790_0002 (247.10, -100.94, 111.45) | **1800201** | 279051 → 27905100 BK Greatsword + Shield / Red Titanite Chunk. |
| Black Knight (sword) | c2790 | c2790_0003 (339.02, -100.99, 140.57) | **1800202** | 279050. |
| Black Knight (greataxe) | c2790 | c2790_0004 (414.15, -111.25, 143.11) | **1800203** | 279052 → 27905200 BK Greataxe + Shield / White Titanite Chunk. Spiral stair. |
| Black Knight (halberd) | c2790 | c2790_0005 (431.56, -113.24, 160.29) | **1800204** | 279053 → 27905300 BK Halberd + Shield / Blue Titanite Chunk. Near the fog. |
| BP Black Knights | c2790 | c2790_0006 1800900 (181.46,-96.41,114.93, 279061 UGS); _0007 1800901 (261.59,-96.76,103.39, 279062 axe); _0008 1800902 (342.82,-100.83,113.77, 279063 halberd); _0009 1800903 (385.05,-105.66,135.01, 279060 sword); _0010 1800904 (435.85,-118.31,169.71, 279061 UGS) | | **Pure-Black World Tendency only** (ev 11805090/91/92, flag 11800050). |
| Vagrants | c3490/c3491 | c3490_0000 1803900 (238.0,-96.2,131.2) passive; c3491_0000 1803901 / c3491_0001 1803902 (318.74,-101.24,134.61) aggressive (lots 34911400 / 34921400) | | Online-only. |
| **Gwyn, Lord of Cinder** | c5370 | c5370_0000 (386.02, -117.40, 190.79) rot -60 | **1800800** | §5. |
| Knight Solaire (summon) | c0000 | c0000_0002 (347.70, -98.00, 157.32) | **6544** | §6. |
- **All Kiln enemies (incl. BP and vagrants) are disabled + killed once Gwyn is dead** (ev 11800002).
- MSB idle/patrol data: no InitAnimID or move points on any Kiln enemy (Gwyn, Solaire, Black Knights all default). Each normal Black Knight shares a **PlatoonID** with its Black-Phantom twin: 1800200+1800900 = 48928, 1800201+1800901 = 48929, 1800202+1800902 = 48930, 1800203+1800903 = 48931, 1800204+1800904 = 48932.
- Black Knights respawn (no stay-dead events). Ghost Black Knights: Area 3 §4.

## 5. Boss — Gwyn, Lord of Cinder
- **Part**: `c5370_0000`, entity **1800800**, NpcParam **537000** (hp 1550 per param; souls per wiki 70,000 — NpcParam getSoul=0, unverified), teamType 1, NameID **5370**.
- **Arena**: BGM sphere **1802990** (r 40 around (386.28, -117.5, 189.08)); boss start box **1802996** (418.1, -116, 169.5); move target 1802997.
- **Fog**: `o8050_0000` **1801990** (SFX 1801991) at (419.77, -115.74, 168.55). ev **11805390** host walk-through ("Traverse the white light", line 1802998 → face 1802997, anim 7410); ev 11805391 white phantoms; ev 11805393 entry notification + multiplayer buffs; ev 11805392 AI ON + boss bar 5370 when in 1802996; ev 11805394/95 BGM map-sound **1803800**.
- **No intro cutscene** (none in EMEVD); Gwyn engages immediately. Mechanics (DS1): fire-imbued greatsword combos, grab, can be **parried** (riposte); no phases/parts in event data.
- **Death (ev 11800001)**: flag **15**, Handle Boss Defeat, fog removed, host respawn point set to **1802130** + save (MSB SpawnPoint 復活イベントLast 1802130 → region 大墓ワープ着地ポイント(クリア直前) entity 1802120 at (398.39, -117.22, 178.85), inside the arena), flags 11807200/11807210/11807220/11807240 cleared, altar bonfire + vessel swapped to the extinguished state, arena bonfire **1801950** appears.
- **Reward** (common ev 970 slot 20, flag 15): lot **2650 Soul of Gwyn, Lord of Cinder** (goods 702). No humanity lot.
- Common ev 690: every boss flag 2-15 increments event value at flag 600 (boss kill counter used elsewhere).

### Endings / NG+
- **Link the Fire** (ev **20**): host, flag 15, action "**Link the Fire**" (10010108) on 1801950 → `Increment Game Cycle` → **cutscene 180000** (skippable) → **achievement 1** → flag **20**.
- **Dark Lord** (ev **21**): host, flag 15, player **leaves sphere 1802990** while in m18_00 → **cutscene 180001** (Kaathe and the serpents bow) → `Increment Game Cycle` → **achievement 2** → flag **21**. Same cutscene regardless of whether Frampt or Kaathe guided you (Frampt/Kaathe "variants" only change who is at the altar and their dialogue/states 1645 vs 1674 and who stays at Firelink/Abyss).
- **NG+ transition**: `Increment Game Cycle` is engine-level; after the ending the game restarts from the Undead Asylum (`m18_01`) with cycle+1. NG+-specific re-acquisition guards live in common ev 8090/8200/8300 (set treasure flags if the player already owns unique items when cycle ≥ 1) — none reference m17/m18 lots.
- Both ending cutscenes are remo files `scn180000` / `scn180001` (no separate movie folder in DSR).

## 6. NPCs
- **Knight Solaire of Astora** — `c0000_0002`, entity **6544**, NpcParam/CharaInit 6544 (teamType 7 = white phantom), sign spawn region **1802050**. Humanity event base 8310.
  - ev 11800550: (1003 ON & 1004 OFF & flag **11410595** (Lost Izalith outcome; see Izalith doc)) → batch 1000-1029 OFF, **1012** ON (= Solaire waits at the Kiln).
  - ev **11805030** (and DSR variant **11805029**, which additionally requires a DSR multiplayer-count condition and SpEffect 28 "Dried Finger"): host, **human** (Alive type), 1012 ON, Gwyn alive, Solaire within 30 m → `Place NPC Summon Sign` (white) for 6544 at 1802050, summon flag **11805031**, dismissal **11805033**. ev 11805990: sign erased (DSR 2003:49) while 11805031 is on. If not summoned, 6544 is disabled.
  - ev **11805032**: once summoned and the boss fight starts (11805393) → AI cmd 10 (walk to the fog), within 1.5 m of the fog he turns to 1802997 and plays **7410** (walk through).
- **Unused**: regions 太陽騎士の侵入判定 **1802200** / 侵入ポイント **1802201** ("Sun Knight invasion trigger/point") — no EMEVD reference (cut hollow-Solaire invasion?). Event 11805200 (dummy 1800999 reacting to hit 1803000) and events 11805100 / 11800510 / 11800520 are defined but never initialized.

## 7. Items
| Part | Position | Lot → item | Flag |
|---|---|---|---|
| corpse `o0500_0005` | (351.00, -101.18, 112.60) | 1800050-1800053 **Black Knight Helm / Armor / Gauntlets / Leggings** | 51800050 |
| Black Knight drops | — | BK weapons + shield, titanite chunks (lots above) | — |
| Gwyn | — | 2650 Soul of Gwyn, Lord of Cinder | 15 |
- Orphan treasure entries: corpses `o0500_0000-0004/0006`, blacksmith corpse `o0503_0000` (lots 1800000-1800070 don't exist).

## 8. Interactive objects & scripted events
| Thing | Entity | Event | Behaviour |
|---|---|---|---|
| Gwyn fog | `o8050_0000` 1801990 | 11805390/91/93, 11800001 | §5. |
| Arena bonfire (ending) | `o0200_0001` 1801950 | 11800001, 20 | §3/§5. |
| Phantom fog | `o8051_0000` 1801994 | ev 0 | Client-only. |
| Debris | `o8150_0000..0009` | — | Decorative ash/rubble (no events). |
| `o8320_0000` | (-35.55, -55.20, 105.65) | — | No event reference (unidentified; near the Firelink-hole column). |
| Black invasion points | 1804000 (118.0,-79.18,122.56), 1804001 (238.4,-100.06,110.9), 1804002 (424.9,-116.6,165.5) | engine | Online invasions. |
| Cutscenes | 180000 (Link the Fire), 180001 (Dark Lord) | 20 / 21 | |
| Sounds / SFX (MSB) | Gwyn BGM 1803800 (SoundID 180000000, sphere 1802990); wind ambience 180000001 (box 風の音); arena embers ボス部屋残り火_01-10 SFX 180000 (part m3300B0); fogs 81800 (1801991) / 81801 (1801995) | | |

## 9. Porting notes (Kiln)
- Ash-desert terrain with sparse respawning Black Knights (DS2 has no c2790 — port model/anims or substitute); ghost-knight procession needs AI-off looping walk + teleport-back.
- Gwyn: parryable boss, fire buffed weapon; ember VFX in arena; BGM switch.
- **Endings**: one bonfire with a single "Link the Fire" action, and an *area-exit* trigger for the Dark Lord ending; both must end the run and roll NG+. DS2's own NG+ (ascetic/bonfire-based) differs — needs a scripted "credits → NG+ restart at Asylum" path.
- Solaire's conditional NPC summon (human-only, quest-state flag 1012) — DS2 supports NPC summon signs.
- After Gwyn: disable all Kiln enemies, swap altar to extinguished state.
- Pure-Black WT phantoms optional.

## 10. Area completion checklist — Kiln of the First Flame
- [ ] Stair foot spawn + phantom fog; ash field with debris and wind ambience.
- [ ] 5 Black Knights (sword ×2, UGS, axe, halberd) + 5 BP variants; 3 vagrants.
- [ ] Black Knight set corpse.
- [ ] Solaire summon sign (flag 1012, human, ev 11805029/30/32).
- [ ] Spiral staircase → Gwyn fog → Gwyn (NameID 5370, BGM 1803800).
- [ ] Gwyn death: flag 15, Soul of Gwyn, enemies cleared, bonfire 1801950, altar extinguished, respawn point.
- [ ] Endings: Link the Fire (bonfire action, cutscene 180000, ach 1) / Dark Lord (leave arena, cutscene 180001, ach 2); Increment Game Cycle → NG+ at the Asylum.

---

## Appendix — key event index
| Map | Event | Purpose |
|---|---|---|
| m17 | 0 / 50 | Bonfires, ladders, statues, phantom fogs, alarm reset, NPC enables (Logan, Sieglinde, Rhea, Crystal Knight humanity) |
| m17 | 11700085 | Archives seal (Lordvessel) |
| m17 | 11700083 | CP2 one-time fog |
| m17 | 11700105 / 11700160 / 11700090 / 11700150 | Gondolas (lifts) + enemy walls |
| m17 | 11705150 / 11700200 | Rotating staircases |
| m17 | 11700110 | Movable staircase |
| m17 | 11700120 | Moving bookshelves |
| m17 | 11700140 / 11700141 | Giant Door |
| m17 | 11700300 | Cell doors "Used key" messages |
| m17 | 11705101-11705110 | Escape cutscene 170010, siren/gramophone, Pisaca alarm AI |
| m17 | 11700130 | Key-guard serpent soldier |
| m17 | 11705140 | Fleeing serpent soldiers |
| m17 | 11705380-11705386, 11705100 | First Seath encounter + capture respawn |
| m17 | 11700000, 11705390-11705397, 11700001, 11705843/46 | Real Seath (intro 170000, crystal, tail, death, DSR fog) |
| m17 | 11705200/11705201/11705240 | Channeler teleports |
| m17 | 11705010-11705060, 11700900 | Mimics |
| m17 | 11705080-11705082 | Black World Tendency enemies |
| m17 | 11705160 / 11705270 / 11705280 | Butterflies / clams / butterfly drops |
| m17 | 11700810 | Non-respawning enemies + lizard drops |
| m17 | 11700600 | Chests |
| m17 | 11700700 | Broken Pendant failsafe (DLC) |
| m17 | 11700510-11700545 | Logan / Sieglinde / Rhea quest steps |
| m18 | 11800100/101 | Place Lordvessel |
| m18 | 11800200 / 11800230 / 11800210 / 11800220 | Offer lord souls, flame stages, open Kiln, gate message |
| m18 | 11806100 | Ghost Black Knight procession |
| m18 | 11800530-11800542, 11806200/201 | Frampt/Kaathe states and return trips |
| m18 | 11805390-11805395, 11800001, 11800002 | Gwyn fight, death, enemy cleanup |
| m18 | 20 / 21 | Link the Fire / Dark Lord endings |
| m18 | 11800550, 11805029-11805032, 11805990 | Solaire summon |
| m18 | 11805090-11805092 | Black World Tendency Black Knights |
| m10_02 | 11026200 / 11020351 / 11020352 | Frampt transport / Firelink hole to altar |
| m16_00 | 11606200 | Kaathe transport to altar |
| common | 710, 702, 721/728, 724, 970 s19/s20, 822/823 | Lordvessel owned, altar offer option, Logan shop buy-out, Rhea dialogue, Seath/Gwyn rewards, Frampt/Kaathe presence reset |
