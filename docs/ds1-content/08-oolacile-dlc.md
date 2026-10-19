# 08 — Artorias of the Abyss DLC (Oolacile) + Battle of Stoicism arena

**Source of truth:** DSR `map/MapStudio/m12_01_00_00.msb` (`msb1-dump`), `event/m12_01_00_00.emevd.dcx` (`emevd-dump`),
`event/common.emevd.dcx`, DSR params (NpcParam, ItemLotParam, GameAreaParam, ObjActParam, ShopLineupParam, SpEffectParam),
`script/talk/m12_01_00_00.talkesdbnd.dcx` (file list only). The DLC entry was traced in `m12_00_00_01.msb` (the DLC variant
of Darkroot that DSR loads), `m12_00_00_00.emevd.dcx` and `m17_00_00_00.emevd.dcx` (Duke's Archives).

**Map id confirmed:** everything in the DLC — Sanctuary Garden, Oolacile Sanctuary, Royal Wood, Oolacile Township,
Township Dungeon, Chasm of the Abyss **and the PvP arena** — is one map, **m12_01_00_00**. Collision place-name ids:
1210 Sanctuary Garden, 1211 Oolacile Sanctuary, 1212 Royal Wood, 1213 Oolacile Township, 1214 Chasm of the Abyss,
1215 Battle of Stoicism, 1216 Battle of Stoicism Gazebo. The `m99_80_*` maps are developer test maps, not the arena.

**MSB totals (m12_01_00_00):** 206 enemy parts (incl. 12 c1000 bonfire/talk dummies), 14 player starts, 215 map pieces,
199 collisions, 321 objects, 38 treasures (33 corpses + 5 chests), 971 regions, 6 ObjActs. Bounding box of the playable DLC:
x 790..1070, y −300..−580, z 450..975; the arena instances sit far east at x 1030..1470, y −417..−445.

**Global flags touched:** 17 = Manus defeated (global; ends DLC portal use, Chester invasion, Sif summon);
121 = set on Artorias/Kalameet kill, cleared by the Kalameet-grounding cutscene (purpose unverified);
11210000 Sanctuary Guardian dead · 11210001 Artorias dead · 11210002 Manus dead (map-local) · 11210004 Kalameet dead ·
11210021 Sif rescued · 11210300 Gough's door opened · 11210592 Gough agreed to shoot Kalameet.

**Boss soul rewards (GameAreaParam, "Handle Boss Defeat"):** Sanctuary Guardian 30,000 · Knight Artorias 50,000 ·
Manus 60,000 · Black Dragon Kalameet 60,000 (single and multi identical).

---

## 0. Gateway — getting into the past (Darkroot Basin m12_00_00_01 + Duke's Archives m17_00_00_00)

The DLC is entered from Darkroot Basin. The chain below is verified from EMEVD; the brief's "Broken Pendant from the Darkroot
crystal golem" is **wrong** — the pendant comes from a Crystal Golem in the **Duke's Archives garden**.

| Step | What the player does | Script/data |
|---|---|---|
| 1 | Kill the **Hydra** (Darkroot Basin lake). | Flag 11200801. m12_00 event 11200800: the **Golden Crystal Golem** `c2711_0000` (entity 1200200, NpcParam 271100, at (211.5,−72.3,−298.9)) is enabled only once the Hydra is dead (the alternative wait on flag 703 is the DS1 "wait forever" idiom; 703 is never set). |
| 2 | Kill the Golden Crystal Golem → **Dusk of Oolacile** (`c0000_0012`, entity 6050, talk 120605) appears where the golem stood. | Event 11200530: Dusk flag 1120 → 1121; Dusk warped to golem dummypoly 101, special waiting anim **7875** (kneeling), plays **7876** (emerge). |
| 3 | Talk to Dusk and exhaust/answer her dialogue. She leaves (anim **7877** then **8289**, disabled). | 11200531 (talk flag 11200590 → Dusk state 1122) or 11200532 (talk flag 11200591 → 1126). While 1122/1129 and no pendant: her summon sign appears at one of 4 random spots (regions 1202000–1202003, SFX 1203100–1203103; events 11200533/11205040/11205070; phantom entity 6051, anims 6951 → 7876). Death of Dusk → 1125 (11200520). |
| 4 | Go to the **Duke's Archives garden** and kill the Crystal Golem `c2710_0010` (entity **1700510**, NpcParam 271000, at (269.9, 277.6, 503.4) in m17_00_00_00). It drops the **Broken Pendant** (goods 2520, ItemLot 27100200 "[Crystal Golem - Darkroot Garden]" — misleadingly named). | m17 event 11700700: requires DLC owned (3:21), golem dead, flag 11200530 ON (Dusk freed) and 1121 OFF (Dusk already talked to / gone / dead). Restart event: waits for the golem to be alive at load, so it must be killed *after* talking to Dusk. Item flag 51700930 (once). |
| 5 | Return to Dusk's spot in Darkroot Basin. A dark portal swirl (map SFX 1202009) is visible **in singleplayer only**. Interact (region **1202008** "【DLC】過去森ワープ領域", box 4×4×4 at (222,−73,−304)). | m12_00 event 11200529: Dusk state 1122/1125/1126 + owns pendant → sets **1127**. Events 11200005 (SFX on in singleplayer, off in multiplayer) and 11200006 (action button, help text 10010100): conditions 1125/1126/1127, owns goods 2520, flag **17 OFF** (Manus alive), singleplayer. |
| 6 | **Cutscene 120002** — Manus's arm drags the player into the portal. Player warped to m12_01 point **1212510** (894.5,−329.3,451.5) "現代森からのワープ先" = Sanctuary Garden. Game saves. | `Play Cutscene and Warp Player(120002, 1212510, m12_01)`. m12_00_00_01 also contains an unused region 1202502 at Oolacile coordinates (898.5,−329.3,452.4). |

Notes: after Manus dies (flag 17) the portal no longer works; return to Oolacile only by bonfire warp (Lordvessel).
**Sif's DLC cutscene:** there is no cutscene for Sif inside m12_01. The connected cutscene is in Darkroot Garden: the Sif boss
intro plays **120000** normally but **120003** (Sif recognizes the player) if flag **11210021** (young Sif rescued in the
Chasm) is ON (m12_00 events 11200001/11200002).

---

## 1. Sanctuary Garden (place name 1210)

**Header**
- Map m12_01_00_00, south end (z 450..560, y ≈ −318..−330).
- Entrance: DLC portal warp (point 1212510; player start parts `c0000_0000/0001/0002` at (894.5,−329.33,451.5), entities −1/1210998/1210983).
- Exits: north through exit fog → slope to Oolacile Sanctuary.
- Prerequisites: Broken Pendant + Dusk state (see §0). No key needed inside.

**Progression walkthrough**
1. Arrive beside the **Sanctuary Garden bonfire**.
2. Walk east along the garden to fog **o2909_0001** (entity **1211790**, SFX 1211791, "光壁 金キメラ前") at (931,−318.6,471.4).
3. Boss arena (centre ≈ (962.8,−321,509), boss-area region 1212000 "金キメラ戦闘領域") — **Sanctuary Guardian**.
4. Exit fog **o2910_0001** (entity **1211792**, SFX 1211793) at (966.2,−317.8,530.9) — opens when the boss dies.
5. Climb north to the Oolacile Sanctuary (bonfire at z ≈ 583).

**Bonfires**
| Name | Bonfire object | c1000 dummy (talk) | Register flag | Respawn point | Notes |
|---|---|---|---|---|---|
| Sanctuary Garden | o0200_0000 entity **1211963** at (897.5,−329.3,451.5) | c1000_0000 entity 1210963 (t121000) | 11210984 | 復活ポイント00 (894.5,−329.3,451.5) | Registered unconditionally in event 0 (lit-able on arrival). Standard kindling (0). |

**Enemies**
| Sub-zone | Enemy | Model | Count | Parts / entity | Behaviour / drops |
|---|---|---|---|---|---|
| Arena | **Sanctuary Guardian** (boss) | c3471 | 1 | c3471_0000 / 1210800 (NpcParam 347100, HP 1024) | Boss — see below. |
| Arena | Sanctuary Guardian's tail | c3472 | 1 | c3472_0000 / 1210810 (347200) | Hidden tail model spawned by tail-cut event 11215006. |
| Garden (post-Artorias only) | Sanctuary Guardian (regular enemy) | c3471 | 2 | c3471_0001 / 1210801 at (950,−321,492); c3471_0002 / 1210802 at (982,−321,492) (NpcParam 347101, HP 812, 3000 souls) | **Disabled until Knight Artorias is dead** (event 11210001 enables them). AI off until the player enters 1212004 while outside 1212001/1212002, or hits one (11215007); then anim **3017** (roar) staggered 15 frames. Home switching (11215009/11217000): warp to 1212007/1212008 when the player is near the arrival point (area 1212006), to 1212010/1212011 when near the Oolacile Sanctuary (1212009); standing on hits 1213020/1213021 or area 1212003 ("安地に帰る" safe spot) sends them home (AI cmd 10) (11215008). Respawning. |
| Garden (post-Artorias) | their tails | c3472 | 2 | c3472_0001 / 1210811, c3472_0002 / 1210812 | Tail cut → **Guardian Tail** (lots 34720010 / 34720020, one-time flags 51210940 / 51210930). |

**Boss — Sanctuary Guardian** (model c3471, NpcParam 347100, health-bar name 3471)
- Arena fogs: entry **1211790** (o2909_0001), exit **1211792** (o2910_0001). Fog traverse: events 11215000 (host, anim 7410 via line segment 1212888 → face 1212887) and 11215001 (white phantom).
- Trigger: 11215002 (boss-room entry notification, MP buffs) when player in region **1212886** (931.6,−318.8,472.2); 11215003 enables AI, shows the health bar and plays anim **3017** (intro roar). BGM: map sound 1213800 (11215004/11215005).
- Mechanics: lightning/charge chimera. **Tail cut**: NPC part 3471 (part HP 200) on 1210800; when broken → tail model 1210810 warped to dummypoly 150, anims 8000 (body) / 8100 (tail), tail hit-mask, AI cmd 20; awards ItemLot **34720000 Guardian Tail** (flag 51210950).
- Death (11210000): flag 11210000, fogs 1211790/1211792 removed. Rewards: 30,000 souls, **Guardian Soul** (lot 2680) + **Humanity ×1** (lot 9000) via common event 970.
- World change: fogs gone permanently.

**NPCs:** none.

**Items:** none in the garden itself.

**Interactive objects / events:** fogs above; DSR co-op boss-fog re-enable (11215843/11215846 slot 0: in multiplayer the defeated-boss fog 1211790 reappears and can be traversed).

**Porting notes**
- Arrival point must be a one-way entry; the DLC has no physical road back (portal is one-way).
- Tail-cut via NPC part HP (DS2 has part-break support on some bosses; needs custom handling).
- The two post-Artorias Guardians are a world-state toggle keyed to the Artorias kill.

**Area completion checklist — Sanctuary Garden**
- [ ] Arrival point 1212510 + cutscene-equivalent from Darkroot portal (§0)
- [ ] Sanctuary Garden bonfire (1211963)
- [ ] Fogs 1211790 / 1211792
- [ ] Sanctuary Guardian boss (c3471) + tail cut → Guardian Tail; reward Guardian Soul + 1 Humanity + 30k souls
- [ ] Two post-Artorias Guardians (1210801/1210802) with tails, home-switching AI

---

## 2. Oolacile Sanctuary (place name 1211)

**Header**
- z 560..640, y ≈ −310..−318. Entered from the Sanctuary Garden exit fog; exits north into the Royal Wood through a
  phantom-only fog. No prerequisites.

**Progression walkthrough**
1. Bonfire inside the sanctuary; **Elizabeth** stands beside it.
2. Corpse with **Humanity** at (984.5,−310.9,583.2).
3. North exit (lantern lights o2403_15/16 at (968–972,−316.4,616)) → multiplayer-only fog **o2913** (entity 1211994, "マルチ1霊廟") at (978.7,−318.4,632.8) → Royal Wood.

**Bonfires**
| Name | Object | c1000 (talk) | Flag | Respawn |
|---|---|---|---|---|
| Oolacile Sanctuary | o0200_0001 **1211961** at (971.7,−313.9,583.3) | c1000_0001 / 1210961 (t121001) | 11210976 | player part c0000_0003 / 1210981 at (971.7,−313.7,580.3) |

**Enemies:** none (Elizabeth is an NPC).

**NPCs**
| NPC | Part / entity | Talk | Where | Notes |
|---|---|---|---|---|
| **Elizabeth** (giant mushroom) | c4140_0000 / **6750** (NpcParam 414000, HP 333, 3000 souls) | **t121675** | (958.9,−312.5,596.0) | Flags 1870–1889. Hostile → **1871** (common event 745 watches it). Death → **1872** (11210520 slot 2), drops **Elizabeth's Mushroom ×3** (lot 41400000, flag 50000520). After Manus: talk → flag 11210594 → lot **1520 Elizabeth's Mushroom ×3** (same flag 50000520, so only once). **Paradox death:** if the past Dusk is killed in the Chasm (§6), event 11210541 force-kills Elizabeth (6750) and 11210538 drops her treasure and sets 1872. Shop rows exist (ShopLineupParam 6500–6507: Repair Powder, Gold Pine Resin, Hidden Weapon, Hidden Body, Cast Light, Repair, Chameleon, Oolacile Ivory Catalyst) — availability condition in t121675 (unverified; DSR fallback for Dusk's spells). |

**Items:** Humanity ×1 (o0500_0001, lot 1210010, flag 51210010).

**Interactive:** multiplayer fog 1211994 (event 0 disables it unless the player is a client).

**Porting notes:** Elizabeth needs a giant non-humanoid NPC with talk; shop gating; paradox kill hook from §6.

**Area completion checklist — Oolacile Sanctuary**
- [ ] Oolacile Sanctuary bonfire (1211961)
- [ ] Elizabeth NPC (talk t121675, shop, post-Manus gift, hostility/death drops, paradox death)
- [ ] Humanity corpse
- [ ] Phantom-only fog 1211994

---

## 3. Royal Wood (place name 1212) — upper forest, stream, Kalameet's ravine, Artorias hub, Gough's tower

**Header**
- Upper forest: z 640..870, y −300..−318. Stream: y ≈ −344 (z 740..780). Kalameet ravine: y ≈ −372, x 790..870, z 650..850.
  Artorias hub: y ≈ −328.5, x 975..1035, z 770..820. Gough's tower top: (1054,−300.2,759).
- Entrances: from Oolacile Sanctuary (fog 1211994). Exits: Elevator 1 down to the Artorias hub; Elevator 2 (shortcut);
  Elevator 3 (Chasm shortcut); Artorias's arena → Oolacile Township.
- Prerequisites: Crest Key (from a Township Mimic) for Gough's tower; Gough's help for the grounded Kalameet fight;
  Artorias dead before Gough is loaded.

**Progression walkthrough**
1. **Scarecrow field** (z 680..750): scarecrows hidden in the grass rise (cancel anim 9060) when the player enters trap regions
   ("旧樹人の罠" 1212500/1212502/1212506) — see enemies. **Chest 00 Blue Titanite Slab** at (951.5,−312.9,740).
   Stone Guardians walk patrol loops.
2. West side: Guardian Leggings (881.9,−316.4,708.7), Gough's Great Arrow ×8 (838.4,−315.9,747.8),
   Guardian Armor (873.0,−312.1,755.1). Entering region **1212050** (881.3,−320,743.6) triggers the one-time
   **Kalameet fly-over** (flag 11210050).
3. Drop to the **stream** (y −344, "渓流"): 5 DLC Undead Attack Dogs patrol. Crystal lizard near waterfall 1 (847.6,−354,726).
   From the stream the **ravine** ("黒竜狭間の森") is entered (ladder o2402 at (860.5,−370,719.6), Register Ladder 1211110,
   flags 11210210/11210211). Before Gough grounds him, the **flying Kalameet** strafes the ravine with breath; after the
   Gough cutscene the ravine becomes the Kalameet boss arena behind fog **o2919** (1211690) at (876.6,−344.6,750.6).
   Developer bloodstains in the ravine: msg 2121 ("a dragon high above the sky, out of sight") at (820.8,−372,718) and
   msg 2122 ("If only Hawkeye were here...") at (800.2,−372,791.8) — the in-game hint to fetch Gough.
   Ravine loot: Soul of a Hero (820,−371.9,724), Soul of a Brave Warrior ×2 (818,−372,718)/(824,−372,722),
   Gough's Great Arrow ×3 (802,−372.2,804), **Chest 05 Titanite Slab** (791.9,−372.1,656.4).
4. Central/north forest: Large Soul of a Proud Knight (906,−311,774.9), Soul of a Brave Warrior (880.6,−305.6,839.7),
   **Guardian Helm** (864.0,−305.5,857.7), **Elizabeth's Mushroom** (885,−308.3,863.7),
   **Gold Coin** (961.2,−307.9,862.2) — picking it up (flag 51210030) wakes 3 ambush scarecrows (11215120).
5. East edge: Soul of a Proud Knight (980.5,−303,789.9) at the top of **Elevator 1**; **Guardian Gauntlets**
   (982.6,−301.8,773.5) by the top of Elevator 2.
6. **Elevator 1** (main route) down to the **Artorias hub** (y −328.5). Hub: Soul of a Brave Warrior (987.6,−330.5,790.5);
   **Marvelous Chester** sits by a candle at (1005.4,−330,770.3); bottom of Elevator 2 (one-way until first used from below);
   top of Elevator 3 (Chasm shortcut). Large Soul of a Brave Warrior on a lower ledge (1019.1,−344.5,774).
7. **Artorias's fog** **o2915** (1211890) at (1034.0,−330.4,810.6) → arena → exit fog **o2916** (1211892) at
   (1050.8,−330.4,839.8) → Oolacile Township bonfire (§4).
8. **Gough's tower** (after Artorias and with the Crest Key): door **o2720_0000** (entity 1211120) at (1055.4,−306.2,770.4)
   on the upper path (Soul of a Hero at (1026.9,−306.5,824.3) on that path); ladder o2790 (Register Ladder 1211111, flags
   11210212/11210213) up to Gough at (1054,−300.2,759). Talk → Gough shoots Kalameet down (cutscene 120100).

**Bonfires:** none in the Royal Wood.

**Elevators (all "Default" events with SFX 120029/120030 and an anti-fall riding hit)**
| # | Lift object | Call switches (top / bottom) | Levels | Unlock | Event / flags | Riding hit |
|---|---|---|---|---|---|---|
| E1 | o2700_0001 **1211010** (981.7,−328.5,795.5) | o2701_0011 1211011 (981.7,−303.3,798.2) / o2701_0001 1211012 (984.4,−328.5,795.5) | Royal Wood upper −303.4 ↔ hub −328.6 | none | 11210110; position flag 11210111; moving 11215221; 140 frames | h7601 1213051 (11210170 slot 1) |
| E2 | o2700_0002 **1211020** (984.0,−328.7,773.7) | o2701_0012 1211021 (986.7,−308,773.7) / o2701_0002 1211022 (986.7,−328.7,773.7) | upper −308.1 ↔ hub −328.8 | **Shortcut**: callable only after the player has stood in 1212124 at the bottom (flag 11210123) | 11210120; flags 11210121/11210122; moving 11215222 | h7602 1213052 |
| E3 | o2700_0003 **1211030** (981.0,−489.5,816.5) | o2701_0013 1211031 (983.7,−328.5,816.5) / o2701_0003 1211032 (978.3,−489.5,816.5) | hub −328.6 ↔ Chasm −489.6 | **Shortcut**: after standing in 1212134 at the bottom (flag 11210133) | 11210130; flags 11210131/11210132; moving 11215223; 240 frames | h7603 1213053 |
(E0 and E4 are in the Township, §4.) Initial resting level of each lift: unverified.

**Enemies**
| Sub-zone | Enemy | Model | Count | Parts (entity) | Behaviour / drops |
|---|---|---|---|---|---|
| Field/forest | Scarecrow — plain | c4130 | 24 | c4130_0000–0010, 0012–0014, 0018, 0021, 0024, 0026, 0028, 0029, 0031–0034 (all entity −1) | NpcParam 413000–413003 (plow, lot 41300000: Bloodred/Purple/Blooming Purple Moss, **Four-pronged Plow** 1%) and 413010–413013 (shears, lot 41301000). 128 HP, 200 souls. Respawn. |
| Field (trap 旧樹人の罠) | Scarecrow — ambush | c4130 | 5 | c4130_0016 (1210100), 0022 (1210101), 0023 (1210102), 0020 (1210103), 0017 (1210104) | MSB **InitAnimID 9000** (dormant, hidden in the grass); platoons 55467 (0016/0022/0023) and 55466 (0017/0020). AI off; cancel anim 9060 (rise). 1210100 (11215100): wakes when the player (not a black phantom) enters 1212502 or 1212500, or hits it; walks to home 1212501 for 6 s then re-plans. 1210101/1210102 (11215110 slots 0–1): wake on damage or player in 1212502; 11215115 re-homes them to 1212501. 1210103/1210104 (slots 2–3): wake within 10 m or in 1212506. |
| North forest | Scarecrow — Gold Coin ambush | c4130 | 3 | c4130_0019 (1210105), 0025 (1210106), 0027 (1210107) | MSB InitAnimID 9000 (dormant), platoon 55469. 11215120: dormant until one is hit or the Gold Coin corpse is looted (flag 51210030), then all three rise (9060). |
| Hub west | Scarecrow (gated) | c4130 | 3 | c4130_0047 (1210913), 0048 (1210914), 0049 (1210915) at (1020–1025,−330,831–839) | Black-tendency gated (see "Gated set" below). |
| Field (gated) | Scarecrow (gated) | c4130 | 5 | c4130_0039 (1210905), 0040 (1210906), 0041 (1210907), 0042 (1210908), 0046 (1210912) | Gated set. |
| Field/forest | Stone Guardian | c4120 | 7 | c4120_0000, 0001, 0002, 0003, 0005, 0006, 0008 (entity −1) | NpcParam 412000, 224 HP, 1000 souls, lot 41200000 (**Stone Greataxe** 2%). Respawn. |
| West forest | Stone Guardian — patrolling | c4120 | 2 | c4120_0004 (1210150) at (850.4,−316.2,788); c4120_0007 (1210151) at (853.4,−315,792.7) | MSB patrol routes (PointMoveType 1): 0004 walks **移動P旧土00→01→…→07** (8 points from (850,−317,788) south-west to (836,−316,749) and back east to (877.8,−311,772.4)); 0007 walks the same loop reversed (07_01, 06, 05, 04, 03_01, 02, 01, 00_01). Event 11215140: when one reaches waypoint 00/03/07 (regions 1212503/04/05 resp. 1212523/24/25) in Normal AI state it plays **7000 → 9000 → 9060** (stop-and-tend routine ~250 frames). |
| Field (gated) | Stone Guardian (gated) | c4120 | 3 | c4120_0011 (1210900), 0012 (1210901), 0013 (1210902) | NpcParam 412001. Gated set. |
| Forest | Crystal Lizard | c3300 | 2 | c3300_0001 (1210201) at (891.6,−312,791); c3300_0004 (1210204) at (954.4,−305.2,802.6) | Non-respawning (11210350 slots 1/4); **Twinkling Titanite ×2** (lots 33007000 / 33007100). |
| Stream | Crystal Lizard | c3300 | 1 | c3300_0000 (1210200) at (847.6,−354,726.2) | Non-respawning (slot 0, lot 33007200, Twinkling Titanite ×2). |
| Stream | Undead Attack Dog (DLC) | c4190 | 5 | c4190_0001–0005 (entity −1; PointMoveType 1 but no MSB MovePointNames — the stream patrol regions "logic 渓流 巡回ポイント" (8 points, (858–930,−344,740–781)) are presumably used by their AI; 0004/0005 share platoon 55479) | NpcParam 419000, 84 HP, 300 souls, no drop. 0004/0005 patrol under the hub (x ≈ 1010). |
| Upper path (z 1213) | **Kalameet — reveal fly-over** | c4510 | 1 | c4510_0000 / 1210400 at (915,−335,757) | 11210050: gravity/map-hits off, invincible, disabled; first time the player enters 1212050 → enabled, warped onto hit 1213000 at 1212051, anim **7000** (fly-past), disabled after 420 frames. One-shot. |
| Ravine | **Kalameet — flying harasser** | c4510 | 1 | c4510_0002 / 1210402 at (800,−364,850) (NpcParam 451001) | 11210051: immortal, no gravity; when the player enters 1212052 ("登場") and Gough has not grounded him (11210535 OFF): anims **7006** (arrive) → **7008**. 11210052–11210057 + 11210040–11210043: random 1-of-4 timing (flags 11210070–11210073) and position-dependent breath runs — far breath (player in 1212054/1212062 → 7010/7002 passes), near breath (1212055 → 7011/7003), otherwise 7009/7008 circles; phantoms trigger via 11215057/11215058. If somehow driven to ≤1% HP: 11210053 plays **7004** and kills him **with souls** — this also sets Kalameet-defeated 11210004 (Calamity Ring) (unverified in play). |
| Ravine | **Black Dragon Kalameet** (boss) | c4510 | 1 | c4510_0001 / 1210401 at (806,−372,790) (NpcParam 451001, HP 2000) | Disabled until the Gough cutscene. Boss — see below. |
| Ravine | Kalameet's tail | c4511 | 1 | c4511_0000 / 1210410 | Spawned by tail cut. |
| Forest | Vagrant (network) | c3490 / c3491 | 1 + 2 | c3490_0000 (1213900) at (864.5,−305.2,856.8); c3491_0000 (1213901) / c3491_0003 (1213902) at (940.5,−305.5,780–788) | DS1 Vagrant slots — only populated by the online item-drop system; not standard enemies. Evil (lot 34911300 Humanity) / Good (34921300 Twin Humanities). |

**Bosses**

*Knight Artorias* (c4100_0000, entity **1210820**, NpcParam 410000 HP 1500, name 4100)
- Arena: north of the hub, centre (1059,−330,816). Entry fog **o2915_0000 1211890** (SFX 1211891, line-segment region
  1212898 → face 1212897). Exit fog **o2916_0000 1211892** (SFX 1211893). BGM region 1212890, map sound 1213801.
- Trigger (11215012/11215013): player in **1212896** → first time only (flag 11210030) **cutscene 120110** (Artorias intro;
  skippable solo, unskippable in MP), then objects **o2800_0000 1211100 / o2801_0000 1211101** at the arena mouth are
  enabled (likely the entry gate/barrier — unverified), AI on, health bar 4100, boss-only hit **h7400B1 1213001** enabled.
  11215012 also sets 11210537.
- Mechanics: abyss-walker greatsword; dark buff (in AI). No tail. Hit 1213001 removed on death.
- Death (11210001): flags 11210001 + **121**, clears 11217060/70/80/90, fogs removed. 17 s later **Hawkeye Gough (6720)
  is put back into backread** (he is unloaded while Artorias lives) and the two post-Artorias Sanctuary Guardians appear.
  Rewards: 50,000 souls + **Soul of Artorias** (lot 2690, goods 710).
- After: Artorias's grave object **o2760_0000 1211220** (1051,−330,825.5) exists (event 50 disables it while he lives);
  **Lord's Blade Ciaran** can appear there.

*Black Dragon Kalameet* (c4510_0001 **1210401**, name 4510)
- Requires Gough: talking to Gough sets **11210592**; with Gough flag **1821** this fires **11210535**: **cutscene 120100**
  (Gough shoots Kalameet's wing), flying Kalameet 1210402 disabled, boss 1210401 enabled, fog **o2919_0000 1211690** (SFX
  1211691) at the ravine mouth spawned, collision swap h0095B1 (1213061 on, 1213060 off; remembered by flag 11210539), flag
  121 cleared. On later loads event 0 keeps fog 1211690 only while 11210592 ON and 11210004 OFF.
- Fog traverse 11215060/11215061 (line segment 1212908 → face 1212907), entry 11215062 (region **1212909** "進入完了"),
  11215066 (white-phantom delay → 11215067). Fight start 11215063: invincible until the player is in **1212906** (ravine floor,
  100×4×50) and standing on hits 1213003/1213004/1213009; then health bar 4510, hit 1213001 on. BGM region 1212900 / sound 1213803.
- AI event points (11215050): 1212060/1212061 depending on player in 1212057/1212058/1212059 (ravine thirds).
- **Tail cut** (11215051/11215052): NPC part 4510 (HP 200). While Kalameet has event message 10 active (until message 20 —
  i.e. during certain flight moves, flag 11215053), the part HP is reset to 10 (can't be cut). Cut → tail 1210410 spawned
  (anims 8000/8100), SpEffect 5434, lot **45110000 Obsidian Greatsword** (flag 51210970).
- Death (11210005 / 11210004 via either 1210401 or 1210402): flags 11210004, 11210005, 121; fog 1211690 off; hit 1213001
  off. Rewards: 60,000 souls + **Calamity Ring** (lot 2710). Gough then gives **Gough's Greatbow** (talk flag 11210593 →
  lot 1510).

**NPCs**
| NPC | Part / entity | Talk | Where | Behaviour / questline |
|---|---|---|---|---|
| **Hawkeye Gough** | c4110_0000 / **6720** (NpcParam 411000, HP 1280, 20,000 souls) | **t121672** | Top of his tower (1054,−300.2,759) | MSB **InitAnimID 9000** (seated giant with greatbow; stands with 9060 when hostile). **Unloaded (backread removed) until Artorias is dead** (11210001); afterwards streamed in/out at 80 m (11210015). Door "imprisoning Gough" needs the **Crest Key** (ObjActParam 2720: goods 2022, player anim 7110, "Locked" 10010163 otherwise; pass flag 11210300 → message "You used Crest Key" 10010884, ObjAct flag 11210650). Flags 1820–1839: 1820 initial → **1821** after the door is opened and he lives (11210530) → talk 11210592 ("shoot the dragon") → Kalameet cutscene. Hostile at ≤90% HP when hit → **1822** (11210510), AI 411001→411000 switch (11210910/11210912: combat area 1212040, returns to home 1212041 when the player leaves), plays **9060** (11210915). Death → **1823**: Gough's Helm/Armor/Gauntlets/Leggings + Greatbow (lots 41100000–41100004). After Kalameet: talk → 11210593 → **Gough's Greatbow** (lot 1510). Shop rows ShopLineupParam 6600–6611 (Dung Pie, Homeward Bone, Prism Stone, Hello/Thank you Carving, Titanite/Large Titanite/Green Titanite Shard, Gough's Great Arrow) — availability unverified. Nearby prop o2765 (1048.5,−298.7,758.5). |
| **Marvelous Chester** | c4090_0000 / **6730** (NpcParam 409000, HP 591, 9,505 souls) | **t121673** | Artorias hub, sitting by his candlestick **o2253 1211130** (1006.6,−329.9,770.2; SFX 1213150, region "スナイパーの燭台") | MSB **InitAnimID 9001** (seated idle). Flags 1840–1859: hostile **1841** (11210510 slot 1), dead **1842** (11210520 slot 1) → candlestick + flame removed (event 50; 11210544 also removes the flame if the candlestick is broken). Drops Snickering Top Hat, Chester's Long Coat, Gloves, Trousers (lots 40900000–40900003, flag 51210990). Shop ShopLineupParam 6410–6426 (Green Blossom, the three moss clumps, Throwing Knife, Firebomb, Black Firebomb, Alluring Skull, Lloyd's Talisman, Homeward Bone, Humanity, Standard/Large/Feather Arrow, Standard/Heavy/Sniper Bolt). He is disabled while his invader copy is out (§4). |
| **Lord's Blade Ciaran** | c0000_0013 / **6740** (NpcParam 6740, 10,000 souls) | **t121674** | Artorias's grave, (1050.8,−330,824.0) (region 1212352 "初期位置"); second home 1212353 near the Township bonfire ("市街地の巣"), switched by 11210543 when she crosses 1212350/1212351 | MSB **InitAnimID 7880** (mourning pose at the grave; exact pose unverified), PointMoveType 2. Disabled unless flag 1861 or 1862 (event 50). Appears (11210531) when the **host carries the Soul of Artorias (goods 710)**, Ciaran flag **1860** and Artorias is dead → grave object 1211220 enabled, flag **1861**. Trade the soul (talk flag 11210590) → **Dark Silver Tracer + Gold Tracer** (lots 1500/1501) → 1862 (11210532); she vanishes when unloaded (1862 → **1865**, 11210533; also after 11210591, 11210534). Hostile **1863** (11210510 slot 3); death **1864**: Porcelain Mask, Lord's Blade Robe/Gloves/Waistcloth, Dark Silver Tracer, Gold Tracer (lots 6740–6745). Note: the appear event contains an `END IF this-event OFF` right after its condition (DS1 idiom; exact firing behaviour unverified). |

**Items (route order; all corpses o0500 unless noted; lot = flag − 50000000)**
| Item | Where | Lot / flag |
|---|---|---|
| **Blue Titanite Slab** (chest 00, o0110_0000, ObjAct 11210600, object 1211600) | scarecrow field (951.5,−312.9,740) | 1210500 / 51210500 |
| **Guardian Leggings** | (881.9,−316.4,708.7) | 1210040 |
| Gough's Great Arrow ×8 | (838.4,−315.9,747.8) | 1210080 |
| **Guardian Armor** | (873.0,−312.1,755.1) | 1210050 |
| Large Soul of a Proud Knight | (906.0,−311.0,774.9) | 1210090 |
| Soul of a Proud Knight | E1 top (980.5,−303.0,789.9) | 1210020 |
| **Guardian Gauntlets** | E2 top (982.6,−301.8,773.5) | 1210350 |
| Soul of a Brave Warrior | (880.6,−305.6,839.7) | 1210400 |
| **Guardian Helm** | (864.0,−305.5,857.7) | 1210070 |
| **Elizabeth's Mushroom** | (885.0,−308.3,863.7) | 1210060 |
| Gold Coin (wakes ambush) | (961.2,−307.9,862.2) | 1210030 |
| Soul of a Hero | upper path to Gough's tower (1026.9,−306.5,824.3) | 1210450 |
| Large Soul of a Brave Warrior | ledge below hub (1019.1,−344.5,774.0) | 1210470 |
| Soul of a Hero | ravine (820.0,−371.9,724.0) | 1210220 |
| Soul of a Brave Warrior | ravine (818.0,−372.0,718.0) | 1210230 |
| Soul of a Brave Warrior | ravine (824.0,−372.0,722.0) | 1210240 |
| Gough's Great Arrow ×3 | ravine (802.0,−372.2,804.0) | 1210250 |
| **Titanite Slab** (chest 05, o0110_0005, ObjAct 11210605) | ravine south end (791.9,−372.1,656.4) | 1210550 |
| Soul of a Brave Warrior | hub (987.6,−330.5,790.5) | 1210390 |
| Twinkling Titanite ×2 each | 3 crystal lizards (see enemies) | 33007000/7100/7200 |
| Boss/NPC: Soul of Artorias, Calamity Ring, Obsidian Greatsword, Gough's Greatbow, Ciaran's tracers/set, Gough's set, Chester's set | see above | |

Unused lots in the param (no MSB reference): 1210530 "Chest – Soul of a Lost Undead", 1210560 "Chest – **Crest Key**"
(the DSR Crest Key comes from a Mimic, §4).

**Interactive objects & scripted events**
- Fogs 1211890/1211892 (Artorias), 1211690 (Kalameet), phantom-only fogs **1211994** (o2913, Sanctuary→Wood),
  **1211996** (o2914 at E3 top, SFX 1211997), **1211998** (o2917 (1047.8,−328.6,850.9) between the arena exit and the Township),
  **1211986** (o2920_0000 (1049.8,−328.6,843.5), arena lobby). Event 0 disables all five for the host/solo (they only exist for
  clients).
- DSR co-op boss-fog re-enable (11215843/11215846) for 1211890 and 1211690 (Kalameet variant only while grounded and alive).
- Ladders 1211110 (o2402, ravine) and 1211111 (o2790, Gough's tower).
- Gough's door o2720_0000 1211120 (ObjAct 11210650, Crest Key).
- Cutscenes: **120100** (Gough grounds Kalameet), **120110** (Artorias intro).
- Decorative/breakable plants: o2201 ×85, o2202 ×16, o2203 ×9 across the forest (type unverified); glowing-flower SFX regions
  "光る花"; lantern lights o2403_01/03/05/07/09; ravine waterfalls (SFX regions 滝1–4).

**Porting notes**
- The flying-dragon harassment (position-triggered breath passes) and the Gough "shoot him down" cutscene swap are unique;
  need a flying Kalameet actor + a grounded boss actor and a collision/arena swap.
- Gough is a giant NPC that must be unloaded until Artorias dies.
- Scarecrow grass ambushes (dormant pose + 9060 rise), Gold-Coin pickup ambush.
- Two tail cuts (Kalameet) with a "can't cut during flight" window.
- Three phantom-only fogs here (co-op limits).

**Area completion checklist — Royal Wood**
- [ ] Phantom fog 1211994 at the Sanctuary exit
- [ ] 40 scarecrows (24 plain, 5 field-trap, 3 Gold-Coin ambush, 8 gated), 12 Stone Guardians (7 plain, 2 patrolling, 3 gated)
- [ ] 3 crystal lizards, 5 stream dogs, Vagrant slots (optional)
- [ ] Kalameet fly-over (1210400), flying harasser (1210402), grounded boss (1210401) + tail (Obsidian Greatsword)
- [ ] Gough's tower door (Crest Key), ladder, Gough NPC (unloaded until Artorias dies), cutscene 120100, Greatbow reward
- [ ] Elevators E1 (main), E2 (shortcut), E3 (Chasm shortcut) with unlock regions
- [ ] Artorias boss (fogs, cutscene 120110, gate objects, hit 1213001), Soul of Artorias, grave object
- [ ] Chester NPC + candlestick, Ciaran at the grave (Soul of Artorias trade)
- [ ] All 19 Royal Wood treasures incl. Guardian set, chests 00/05

---

## 4. Oolacile Township (place name 1213) — upper streets, middle quarter, big hall, lower prison, Township Dungeon

**Header**
- Upper: x 1014..1035, y −336..−345, z 880..912 (just past the bonfire). Middle quarter: x 935..1000, y −348..−380, z 920..975.
  Big hall / attic: x 935..1000, y −379..−405, z 868..930. Lower prison: x 880..945, y −399..−405, z 920..945.
  Township Dungeon: x 860..930, y −445..−466.
- Entrance: Artorias's exit fog 1211892 → Oolacile Township bonfire. Exits: Elevator 4 → Township Dungeon → Chasm (§6);
  Battle of Stoicism lobby (§5).
- Prerequisites: Artorias defeated (his exit fog). Light source (Cast Light / Sunlight Maggot / Skull Lantern) for the two
  illusory walls.

**Progression walkthrough**
1. **Oolacile Township bonfire** right after Artorias's exit fog; the arena gazebo/lobby is next to it.
2. Upper streets: bloatheads; **Large Soul of a Proud Knight** (1014.5,−338.6,892.7). **Elevator 0** down (shortcut
   back up only after reaching the bottom).
3. Middle quarter (going west/down): bloatheads and sorcerers, a crystal lizard, **Mimic** (c2780_0001, Very good! Carving).
   **Chester invades** when the player enters 1212083 (956.1,−367.5,942.2) near the first illusory wall.
4. **Light-reacting illusory wall 0** — **o2730_0000 1211200** at (957.1,−368.1,951.0) (region 1212200; developer
   bloodstain "ヒント血文字00", message 2120 "Let there be light...", in front). It hums (SE 120199999 every 2 s) and dissolves (anim 1) only while the player is in its region
   **and** has **Cast Light** (SpEffect 620) or **Sunlight Maggot** (6950) active, or is using the **Skull Lantern**
   (event 11210200 slot 0 / flag 11210200). Behind/around it: sorcerers 1210250–1210252 and the special sorcerer 1210260,
   **chest 02 Silver Pendant**, **chest 04 Dark Orb**, two pre-opened decorative chests (o0110_0006/0007, entities
   1211606/1211607, played open in event 50), Souls of a Brave Warrior ×2 (exact side of the wall: unverified).
5. **Big hall** (two storeys, fireplace "暖炉01" at (973,−382.3,870.3), furniture o2740 ×3 / o2750 ×9): many bloatheads,
   **Mimic c2780_0000 in the attic — drops the Crest Key**. **Light wall 1** **o2732_0000 1211201** at (948.7,−379.5,889.4)
   (region 1212201, same rules, slot 1 / flag 11210201); **chest 01 Red Titanite Chunk** (934.7,−379.3,877.5), Rubbish
   (947.1,−384.9,873.5), Twin Humanities (999.8,−384.5,869.8). Ladder **o2791 1211112** (997.2,−389.4,878.4) (flags
   11210214/11210215). A corpse holding **Dark Fog** hangs in **o2795_0000 1211210** at (996.4,−386.8,883.7): break the
   hanging object and the corpse (o0500_0100, entity 1211650) drops (event 11210230: looping anim 125 → 126, SFX 99005,
   treasure enabled). Soul of a Hero (973.6,−398.4,891.7).
6. Lower prison level: chained prisoner on a leash (leash region 1212651 "鎖人が帰りたくなる"), torches 松明01–05,
   10 objects o2770_0000–0009 in two rows at (894–917,−399.9,925–945) (type unverified), Soul of a Brave Warrior
   (966.1,−404.8,923.7).
7. **Elevator 4** down to the **Oolacile Township Dungeon bonfire**; first ride sets 11210160 and warps bloathead 1210350
   to the lift's bottom landing (event 11210150, region 1212150). Dungeon room: bloatheads, sorcerer, **Dark Bead**
   (926.3,−459.4,944.0). Continue down into the Chasm (§6).

**Bonfires**
| Name | Object | c1000 (talk) | Flag | Respawn |
|---|---|---|---|---|
| Oolacile Township | o0200_0002 **1211962** at (1040.6,−332,875.9) | c1000_0002 / 1210962 (t121002) | 11210968 | c0000_0004 / 1210982 (1041.6,−332,872.9) |
| Oolacile Township Dungeon | o0200_0004 **1211964** at (862.8,−447.5,911.2) | c1000_0005 / 1210964 (t121004) | 11210960 | c0000_0015 / 1210984 (863.3,−447.5,913.1) |

**Elevators**
| # | Lift | Call switches (top / bottom) | Levels | Unlock | Event | Riding hit |
|---|---|---|---|---|---|---|
| E0 | o2700_0000 **1211000** (1007.5,−399.2,908.3) | o2701_0010 1211001 (1010.1,−344.4,907.6) / o2701_0000 1211002 (1004.4,−399.2,909.1) | upper −344.5 ↔ big hall −399.3 | **Shortcut**: after standing in 1212104 at the bottom (flag 11210103) | 11210100 + helper 11219100 (anims 0/10, flags 11210101/11210102, 11218102/11218103) | h7600 1213050 |
| E4 | o2700_0004 **1211040** (869.4,−447.5,936.0) | o2701_0014 1211041 (870.2,−404.5,939.1) / o2701_0004 1211042 (868.6,−447.5,932.9) | lower prison −404.6 ↔ Dungeon −447.6 | none | 11210140 (flag 11210141, moving 11215224, sets 11210160) | h7604 1213054 |

**Enemies**
| Sub-zone | Enemy | Model | Count | Parts (entity) | Behaviour / drops |
|---|---|---|---|---|---|
| Upper | Bloathead | c4150 | 5 | c4150_0000–0003, 0010 | NpcParam 415000 (152 HP, 400 souls, lot 41500000 Bloated Head 0.5%, humanity lot 5210). Respawn. |
| Upper | Bloathead Sorcerer (gated) | c4160 | 1 | c4160_0012 (1210918) | 416001. Gated set. |
| Middle | Bloathead | c4150 | 8 | c4150_0005, 0006, 0007, 0013, 0018, 0020, 0021, 0027 | 415000 (think 415000/415001/415010/415011 variants). |
| Middle | Bloathead (gated) | c4150 | 2 | c4150_0028 (1210916), 0029 (1210917) | 415001. Gated set. |
| Middle | Bloathead Sorcerer | c4160 | 1 | c4160_0000 | 416000 (120 HP, 700 souls; lot 41600000: Bloated Sorcerer Head 1%, **Oolacile Catalyst** 0.5%). |
| Hidden room (light wall 0) | Bloathead Sorcerer | c4160 | 3 | c4160_0004 (1210250), 0005 (1210251), 0006 (1210252) | 416000. |
| Hidden room | Bloathead Sorcerer — "Special" | c4160 | 1 | c4160_0002 (**1210260**) at (966.1,−372,973.7) | NpcParam 416010 (no normal drop); non-respawning (11210350 slot 5) and awards **I'm sorry Carving** (lot 41601000, flag 51210960). |
| Middle | Bloathead Sorcerer (gated) | c4160 | 1 | c4160_0013 (1210919) | Gated set. |
| Middle | Crystal Lizard | c3300 | 1 | c3300_0002 (1210202) at (950.3,−370.2,956.5) | Non-respawn, Twinkling Titanite ×2 (lot 33007100). |
| Middle | **Mimic** | c2780 | 1 | c2780_0001 (**1210601**) at (936,−366.5,946.2), nest 1212181 | NpcParam 278031, 496 HP, 3000 souls; lot 27803100 (Symbol of Avarice, weight 1/1000, luck-affected) + 27803101 **Very good! Carving**. Mimic scripts (slot 1 of 11215160/11215165/11215170/11215175/11215180/11210680/11215185): asleep state SpEffect 5421 → opening it grabs the player (warp, anim **7521**); damage wakes it (anims 3001/3006); Lloyd's Talisman SpEffects 3150/3151 put it to sleep (AI cmds 200/201/210/211); in singleplayer it returns to its nest asleep; treasure forced on death. |
| Middle | Marvelous Chester — **invader** | c4090 | 1 | c4090_0002 (**6732**, NpcParam 409010, 9,505 souls, no drop) at sign 1212082 (974.5,−356.2,938.2) | 11215030 slot 1: once per playthrough (flag 11215036) while Manus alive, host human, Chester NPC alive (1842 OFF), when the host enters 1212083 → NPC Chester 6730 disabled, invader enabled, "Dark spirit Marvelous Chester has invaded!" (20001100), SFX 130, anim **7000**; sets 11210536. Killed → "vanquished" (20001105) unless inside an arena region, NPC re-enabled (11210900, flag 11210901). If the host reaches the Township bonfire passage (1212084) or the Dungeon bonfire (1212085) first: anim **7001**, warps home, "returned home" (20001101), NPC re-enabled (11210905 slot 1, flag 11215033). |
| Lower | Marvelous Chester — unused invader slot | c4090 | 1 | c4090_0001 (6731) at (942.2,−401.1,925.7) | Disabled at load (event 50); its invasion slot (11215030 slot 0, trigger 1212081) is never initialised. |
| Big hall | Bloathead | c4150 | 15 | c4150_0004, 0008, 0009, 0011, 0012, 0014, 0015, 0016, 0017, 0019, 0022, 0023, 0024, 0025, 0031 | 415020 (600 souls) mostly; 0014/0015/0022 on the −405 floor. **c4150_0017 patrols** the 4-point route "Logic 大部屋屋根裏暖炉前の深淵人巡回00–03" (attic, in front of the fireplace, around (969–977,−383,872–882)). |
| Big hall | Bloathead (gated) | c4150 | 1 | c4150_0030 (1210920) | Gated set. |
| Big hall | Bloathead Sorcerer | c4160 | 6 | c4160_0003, 0007, 0008, 0009, 0011 (+0009 uses drop/teleport logic regions 1212030/1212031) | 416020 (900 souls). |
| Big hall | Bloathead Sorcerer (gated) | c4160 | 1 | c4160_0015 (1210921) | Gated set. |
| Big hall attic | **Mimic — Crest Key** | c2780 | 1 | c2780_0000 (**1210600**) at (969.8,−382.6,872.0), nest 1212180 ("大部屋屋根裏" attic) | NpcParam 278030; lot 27803000 (Symbol of Avarice 1/1000) + **27803001 Crest Key** (flag 51210981). Same mimic script set (slot 0). Network update forced for 1210601. |
| Lower prison | Chained Prisoner | c4180 | 1 | c4180_0000 (entity −1) at (882.5,−400.5,941.9) | NpcParam 418000, 512 HP, 1000 souls, **Chain Helm** 20%. Leash region 1212651. |
| Lower prison | Chained Prisoner (gated) | c4180 | 1 | c4180_0003 (1210922) | Gated set. |
| Middle/big hall | Vagrant (network) | c3490 / c3491 | 1 + 2 | c3490_0001 (1213910) (977,−372,970.8); c3491_0001 (1213911) / 0004 (1213912) (940.8,−381.7,908.8) | Online-only. |
| Dungeon | Bloathead | c4150 | 5 | c4150_0026 (**1210350**, warped to the E4 landing after the first ride), 0032–0035 | 415020. |
| Dungeon | Bloathead Sorcerer | c4160 | 1 | c4160_0001 | 416020. |
| Dungeon | Crystal Lizard | c3300 | 1 | c3300_0003 (1210203) at (890.9,−454.1,911.8) | Non-respawn, lot 33007300. |
| Dungeon | Chained Prisoner (gated) | c4180 | 1 | c4180_0005 (1210924) | Gated set. |

**NPCs:** Chester invasion above (NPC Chester himself sits in the Royal Wood hub, §3); Ciaran's second home point is by the
Township bonfire (§3).

**Items**
| Item | Where | Lot |
|---|---|---|
| Large Soul of a Proud Knight | upper (1014.5,−338.6,892.7) | 1210110 |
| Soul of a Brave Warrior | middle (974.3,−353.6,949.6) | 1210260 |
| Soul of a Brave Warrior | middle (968.1,−364.4,944.8) | 1210160 |
| **Silver Pendant** (chest 02, o0110_0002, ObjAct 11210602) | light-wall-0 area (964.4,−368.3,947.2) | 1210520 |
| **Dark Orb** (chest 04, o0110_0004, ObjAct 11210604) | hidden room (967.4,−376.0,963.6) | 1210540 |
| **Red Titanite Chunk** (chest 01, o0110_0001, ObjAct 11210601) | big hall (934.7,−379.3,877.5) | 1210510 |
| Rubbish | big hall (947.1,−384.9,873.5) | 1210210 |
| Twin Humanities | big hall (999.8,−384.5,869.8) | 1210460 |
| **Dark Fog** (hanging corpse o0500_0100) | big hall, drop by breaking o2795 1211210 (996.4,−396.7,883.7) | 1211000 |
| Soul of a Hero | big hall (973.6,−398.4,891.7) | 1210190 |
| Soul of a Brave Warrior | lower floor (966.1,−404.8,923.7) | 1210430 |
| **Crest Key** | Mimic 1210600 | 27803001 |
| Very good! Carving | Mimic 1210601 | 27803101 |
| I'm sorry Carving | special sorcerer 1210260 | 41601000 |
| **Dark Bead** | Township Dungeon (926.3,−459.4,944.0) | 1210270 |
| Twinkling Titanite ×2 | 2 crystal lizards | 33007100 / 33007300 |

**Interactive objects & scripted events**
- Illusory light walls 1211200 / 1211201 (event 11210200 slots 0/1 + hum 11210205); sound regions "光あれ！の魔法" (Cast
  Light SFX hooks) at both.
- Hanging corpse cage 1211210 → Dark Fog corpse (11210230).
- Ladder 1211112; elevators E0/E4; phantom-only fog 1211998 (o2917, Township side of the arena exit).
- Chester invasion (11215030 / 11210900 / 11210905).

**Porting notes**
- Light-only illusory walls need a "light source active in area" check (Cast Light / Sunlight Maggot / Skull Lantern → DS2
  torch is the natural analogue).
- Mimic grab / Lloyd's Talisman sleep / nest return logic.
- Scripted NPC invader (dark spirit Chester) with message popups and "returned home" if the player reaches a bonfire.
- Bloatheads drop humanity via humanityLotId (5210/5220/5230 = Humanity table lots) — DS2 has no humanity item; map to a
  Human Effigy chance or souls.

**Area completion checklist — Oolacile Township**
- [ ] Oolacile Township bonfire (1211962) and Township Dungeon bonfire (1211964)
- [ ] Elevators E0 (shortcut) and E4 (+ bloathead warp)
- [ ] 36 Bloatheads (33 normal + 3 gated; 0017 on the attic patrol route) and 14 Sorcerers here (incl. special 1210260, 3 gated; the 15th is in the Chasm)
- [ ] 2 Mimics (Crest Key / Very good! Carving), 2 crystal lizards, chained prisoner(s)
- [ ] Chester dark-spirit invasion + return/vanquish logic
- [ ] Two light-reacting illusory walls; hidden room with chests 02/04 and decorative open chests
- [ ] Hanging Dark Fog corpse; ladder 1211112; all 13 Township treasures + Dark Bead

---

## 5. Battle of Stoicism — PvP arena (place names 1215/1216, inside m12_01_00_00)

**Header / access:** lobby "Battle of Stoicism Gazebo" at (1048–1072,−328.6,840–851), between Artorias's exit fog and the
Oolacile Township bonfire — reachable once Knight Artorias is dead. No separate map. Host return point **1212777**
(1059.6,−328.4,847.2) / player part c0000_0014 (1218200).

**Lobby contents**
- 6 entry seats: objects o2710_0000–0005 + bonfire-sign objects o0200_0005–0010 (all entity **1211666**) + c1000_0006–0011
  talk dummies **t121661–t121666** (entities 1210661–1210666). Messages "NewArenaMessage2–7", "NewBoSmessage1" regions.
- Ranking board: c1000_0004 (talk **t121200**) at (1071.5,−328.6,843.8) by SFX "ランキングボード" (1072.3,−326.5,843.4);
  StreakDisplay region 1218900 (duel streak messages 60000102–60000111 via 11210875 slots, flags 7000–7749).
- Gazebo fog **o2920_0001 1211900** (SFX 1211901) at (1052.3,−328.6,847.1), region BoSFogControl 1219001: shown during
  DSR arena matchmaking (11219111/11219112), removed if the host owns a **Purple Coward's Crystal** (goods 118, 11219113).
  In multiplayer without the crystal: "Enter the Gazebo" (60000112) → warp in, anim 7410, receive **Purple Coward's
  Crystal** (lot 2200, flag 50001200) (11219114).
- Phantom fog 1211986 (o2920_0000) also at the lobby.
- Developer messages: arena entrance (2201 "sit at a Blue Bonfire to set number of combatants and search", 2202 password
  matching, 2107 "consecutive wins → Plaque of Legends"), lobby "Welcome"/NewArenaMessage set (2101, 2103, 2104, 2108,
  2109, 2110, 2203) and 60 per-arena explanation messages ("闘技場説明 血文字00-00 … 05-09", ids 2102–2119).
- MSB spawn-point events: arena return points 1218201–1218206 (Dais/Ruins × Duel/Team/BR) and one in front of the ranking board.

**Arena instances** (two sizes per mode, territory regions 60×40×60 "小" small at z≈677, 80×40×80 "大" large at z≈829)
| Mode | Small (region) | Large (region) |
|---|---|---|
| Battle Royal (4) | x 1050 (1212702) | x 1050 (1212715) |
| 2 vs 2 | x 1150 (1212701) | x 1150 (1212714) |
| 1 vs 1 | x 1250 (1212700) | x 1250 (1212713) |
| 3 vs 3 (DSR) | x 1350 (1212793) | x 1350 (1212796) |
| Battle Royal 6 (DSR) | x 1450 (1212794) | x 1450 (1212797) |
- x 1050–1250 instances have bonfire objects (o0200_0200–0212, entities 1211510–1211515), player spawns c0000_0007–0012
  (1218210–1218215) and 4 arena props each (o2780 ×24 entities 1211520–1211573; o2785 ×24 entities 1211420–1211473).
  The DSR x 1350/1450 instances only have regions (InitialSpawnEventCylinder 1217xxx, RespawnPoints 1215xxx) — geometry
  reuse unverified.
- Arena collisions h5000–h5103 (entities 1213200–1213303, place name 1215).
- Scripts 11210700–11210895, 11210401–11210439, 11219010, 11219876/7, 7998/7999: cutscene **120130** with random spawn
  warp, arena SpEffects 4610–4618 (item ban, death, provisional 1st place, no-death), 90000/90001 (all/status disabled),
  bonfire-like recovery between rounds, respawn anim 6951, messages 150000–150420 (rank/streak), 20000437 (competitor left),
  exit warp to 1218146.

**Porting notes:** DS2 has its own arena covenants; port the lobby geometry only, or wire the seats to DS2 arena matchmaking.
Low priority.

**Area completion checklist — Battle of Stoicism**
- [ ] Lobby geometry, 6 seats, ranking board, gazebo fog, Purple Coward's Crystal grant (optional)
- [ ] 10 arena instances (or a subset) — optional

---

## 6. Chasm of the Abyss (place name 1214) — Sif's prison, Alvina, Manus

**Header**
- Below the Township Dungeon: x 850..980, y −470..−580, z 770..910. Pitch-dark; guidance SFX "【SFX】誘導_00–06" mark the
  route (968,−490.5,833.8) → (959,−498.5,854) → (930,−493,877) → (870.7,−524.2,905.6), and (907.8,−459.2,921.6) at the top.
- Entrances: Township Dungeon (drop/descent). Exits: **Elevator 3** shortcut up to the Royal Wood hub (unlocks once its
  bottom region 1212134 is reached); Manus fog.
- Prerequisites: none (Sif rescue optional). A light source and the **Silver Pendant** (Township chest) are the intended
  tools (darkness; Manus's dark-bead attack).

**Progression walkthrough**
1. Descend from the Dungeon into the chasm; Humanity Phantom packs (patrol route regions "人間性巡回ルート").
2. **Alvina** (white cat) sits at (963.1,−494.8,882.3); approaching (7 m) or hitting her makes her vanish (anim **7003**)
   and reappear at 1212331 (971.8,−491.5,852.0), then at 1212332 (963.5,−489.7,813.4) next to Sif's prison, then she is gone
   for good (11210340 → 11210341 → 11210345; SFX 1213125 deleted). Her presence makes the floor tile **o2621_0000 1211250**
   at (956.6,−486.0,889.5) invulnerable; stepping on it (region 1212336) while she is still around breaks it and drops the player
   (11210347, SE 262000000) with fall protection SpEffect **4161** "Falling control for white cat" in 1212335 (11210346); once
   Alvina is gone the tile is simply absent. (Interpretation of the floor route: unverified.)
3. Breakable wall **o2620_0000 1211240** at (964.3,−489.7,811.7) (event 11210025; destroyed state remembered).
4. **Sif's prison** at (971,−489.6,772): young **Great Grey Wolf Sif** inside a magic barrier **o2630_0000 1211230**
   (SFX 1213110, region "シフの魔方陣" 1212310), guarded by six Humanity Phantoms. Killing all six (flags 11210310–11210315 →
   11210330) frees Sif: after 6 s anim **7010**, Sif vanishes, barrier and SFX removed, and Sif's treasure drops —
   **Cleansing Greatshield** (lot 45200000, flag 51210910). Event flag **11210021** = Sif rescued (used by Darkroot cutscene
   120003 and the Manus summon).
5. **Elevator 3** bottom at (978.3,−489.5,816.5) with phantom-only fog **o2912_0001 1211988** (SFX 1211989) — ride up to the
   Royal Wood hub (shortcut).
6. Loot down the west slope: Humanity (932.2,−489.8,868.7), Help me! Carving (934.5,−498.4,890.8), **Black Flame**
   (938.0,−507.3,891.0), Twin Humanities (897.0,−518.8,886.5), **White Titanite Slab** (883.5,−524.8,892.8),
   Soul of a Hero (883.8,−532.6,898.9).
7. **Manus fog** **o2911_0001 1211990** (SFX 1211991) at (862.3,−538.6,881.6) → drop into the abyss arena (y −576.8).
8. After Manus: **Chasm of the Abyss bonfire** appears in the arena; the past **Dusk** lies where Manus fell.

**Bonfires**
| Name | Object | c1000 (talk) | Flag | Respawn | Notes |
|---|---|---|---|---|---|
| Chasm of the Abyss | o0200_0003 **1211950** at (854.0,−576.8,848.0) | c1000_0003 / 1210950 (t121003) | 11210992 | c0000_0005 / 1210970 (855,−576.8,850) | Disabled until Manus dies; on the kill (11210002) oneshot SFX 90014, enabled 2 s later and registered. |

**Enemies**
| Sub-zone | Enemy | Model | Count | Parts (entity) | Behaviour / drops |
|---|---|---|---|---|---|
| Chasm | Humanity Phantom (Large) | c4170 | 4 | c4170_0000, 0010, 0012, 0015 | NpcParam 417000 (280 HP, 20 souls; lot 41700000 Humanity 10% / Twin Humanities 5%). Respawn. |
| Sif's prison | Humanity Phantom (Large) — guard | c4170 | 3 | c4170_0001 (1210300), 0002 (1210301), 0003 (1210302) | Non-respawning (11210310 slots 0–2). |
| Chasm | Humanity Phantom (Medium) | c4171 | 16 | c4171_0000–0004, 0006–0016 | 417100 (160 HP; Humanity 8% / Twin 2%). Clusters at (936–949,−486..−490,888–908), (933–943,−497..−510,860–891), (857–886,−524..−533,894–901), (950–970,−489..−492,814–854). |
| Sif's prison | Humanity Phantom (Medium) — guard | c4171 | 1 | c4171_0005 (1210305) | Non-respawning (slot 5). |
| Chasm | Humanity Phantom (Small) | c4172 | 17 | c4172_0000–0009, 0012–0017, 0020 (0020 `point=5`, patrol route) | 417200 (80 HP; Humanity 4% / Twin 1%). |
| Sif's prison | Humanity Phantom (Small) — guard | c4172 | 2 | c4172_0018 (1210303), c4172_0010 (1210304) | Non-respawning (slots 3/4). |
| Chasm | Bloathead Sorcerer | c4160 | 1 | c4160_0010 at (935.3,−490.1,871.0) (jump/teleport logic regions 1212032/1212033) | 416020. |
| Chasm | Chained Prisoner (gated) | c4180 | 1 | c4180_0006 (1210925) at (948.0,−488.5,897.5) | Gated set. |
| Chasm | Vagrant (network) | c3490 / c3491 | 1 + 2 | c3490_0002 (1213920) (924,−509.2,864.8); c3491_0002 (1213921) / 0005 (1213922) (933.8,−490.1,868.0) | Online-only. |
| Prison | **Great Grey Wolf Sif (young, captive)** | c4520 | 1 | c4520_0002 / **1210502** (NpcParam 452000) | MSB **InitAnimID 7009** (cowering in the barrier). AI off, invincible (11210021); freed as above. |
| Manus arena | **Sif summon sign** | c4520 | 1 | c4520_0001 / 1210501 (452010) at (861.4,−576.8,869.5) | See Manus. |
| Manus arena | **Sif — white phantom** | c4520 | 1 | c4520_0000 / 1210500 (452010, HP 2400) | See Manus. |
| Manus arena | **Manus, Father of the Abyss** | c4500 | 1 | c4500_0000 / **1210840** (NpcParam 450000, HP 2666) at (851.9,−576.8,856.3) | Boss. |
| Dungeon (y −494) | **Alvina** (DLC) | c4531 | 1 | c4531_0000 / **6760** (NpcParam 453100) | No gravity, immortal, no HP bar, SpEffect 5300; vanishing NPC (no talk). |
| Manus arena (post-Manus) | **Dusk of Oolacile (past)** | c0000 | 1 | c0000_0006 / **6700** (NpcParam 6700, talk **t121670**) | See NPCs. |

**Boss — Manus, Father of the Abyss** (name 4500)
- Fog **1211990** (o2911_0001; line segment 1212998 → face 1212997, events 11215020/11215021). Entering region **1212996**
  (861.3,−538.8,880.1): boss-room notification (11215022; black-phantom hosts never get MP buffs), then **cutscene 120140**
  (Manus intro) which warps the player to **1212022** (854.1,−576.8,870.5) — the fall into the abyss (11215027, flag 11210031).
  Landing region **1212021** (75×20×75) grants 2 s invincibility (11215026) and switches Manus's AI on with the health bar
  (11215023, ends if flag 17). BGM region 1212990 / map sound 1213802.
- Arena dressing: 24 breakable props **o2610 ×3, o2611 ×6, o2612 ×6, o2613 ×9** (entities 1211300–1211323, each with
  SFX 1213160–1213183) — break on hit or all at once when Manus dies (11215250); boss-room wall SFX regions "ボス部屋壁01–20"
  and humanity-sprite SFX regions "ボス部屋_人間性".
- **Sif summon** (only if 11210021 ON, Manus alive, player in human form): SFX 1213100 lit (11215044); sign 1210501 appears
  when the player stands in **1212300** (anim 7006 → loop 7007; leaves with 7008 if the player walks off or the fog event
  restarts) (11215041). Touch sign (help 50000000) → "Summoning phantom..." (140010), sign anim 7008, 10 s later Sif
  1210500 enabled as **white phantom** (team type 2), anim **7004**, message 20001110/20001111 (11215040). At ≤30% HP Sif
  gets SpEffect **5401** (weapon-change anim offset → injured limp) (11215043). If Manus dies with Sif alive: anim **7005**,
  Sif leaves; if Sif dies: "Phantom Great Grey Wolf Sif was vanquished" (20001115) (11210020).
- Mechanics: Manus's dark magic (dark bead / dark orbs) is blocked by the **Silver Pendant**; arena is unlit.
- Death (11210002): flags 11210002 + **17** (global), fog off, SFX 1213100 off, Chasm bonfire created. Rewards: 60,000
  souls + **Soul of Manus** (lot 2700, goods 711) + **Humanity ×10** (lot 9040, awarded 5 s later by common event 970).
- After: DLC portal closed; Chester invasion disabled; Elizabeth and Gough gain post-Manus talk (Elizabeth's 3 mushrooms);
  the past Dusk appears (below).

**NPCs**
| NPC | Part / entity | Talk | Where | Notes |
|---|---|---|---|---|
| **Dusk of Oolacile (past)** | c0000_0006 / **6700** | **t121670** | Warped to Manus's corpse position (dummypoly 30) | MSB **InitAnimID 7915** (lying unconscious — same anim the event loops). Disabled in event 50. 11210540 (host only): when Dusk flag **1127** (set in Darkroot when the pendant was obtained) and Manus is dead → sets **1128** (clears 1120–1139 unless 1125/1126 already set), enables her looping **7915** (lying unconscious). Immortal; each hit plays **7916** (11210542). At ≤1% HP: **7917**, disabled, dies with souls (1000) → Dusk flag **1125** (dead) **and Elizabeth (6750) is force-killed** (11210541), whose treasure then drops (11210538, flag 1872). Back in Darkroot, 1128 moves present Dusk to 1129 (summon sign returns) or 1125 (m12_00 event 11200527). |
| **Alvina** | c4531_0000 / 6760 | — | (963.1,−494.8,882.3) | MSB **InitAnimID 9000** (sitting cat). Vanishes three times (see walkthrough). Host-only events. |
| Great Grey Wolf Sif | 1210502 / 1210501 / 1210500 | — | prison / Manus arena | Rescue + summon as above. |

**Items:** see walkthrough step 6 (lots 1210330 Humanity, 1210340 Help me! Carving, 1210280 **Black Flame**, 1210440 Twin
Humanities, 1210290 **White Titanite Slab**, 1210300 Soul of a Hero) + **Cleansing Greatshield** (Sif) + Soul of Manus.

**Interactive objects & scripted events**
- Sif barrier 1211230 / SFX 1213110 (11210021); six guard kill flags (11210310 slots, 11210330 batch check).
- Breakable wall 1211240 (11210025); Alvina floor 1211250 (11210347).
- Elevator 3 bottom + phantom fog 1211988.
- Manus fog 1211990, cutscene 120140, landing invincibility, 24 breakable arena props, Sif sign region 1212300.
- DSR co-op boss-fog re-enable for 1211990 (11215843/11215846 slot 3).

**Porting notes**
- Darkness: the Chasm and Manus arena are unlit by design — DS2's torch/darkness system can carry this.
- Fall-into-arena intro (cutscene + warp + landing invincibility).
- Boss-arena summon sign (Sif) appearing only inside the arena, with a non-humanoid phantom ally and injury state.
- Paradox: killing the past Dusk kills Elizabeth.
- Humanity Phantoms (three sizes) — DS2 has no direct equivalent; candidate reskin targets needed.

**Area completion checklist — Chasm of the Abyss**
- [ ] Descent route + guidance lights; 43 Humanity Phantoms (6 non-respawning Sif guards)
- [ ] Sif's prison, barrier, rescue → Cleansing Greatshield, flag 11210021
- [ ] Alvina vanish sequence + breakable floor + fall protection
- [ ] Breakable wall 1211240; Elevator 3 shortcut + phantom fog
- [ ] 6 Chasm treasures (Black Flame, White Titanite Slab ...)
- [ ] Manus boss (fog, intro fall, 24 breakables, Sif summon, rewards incl. 10 Humanity), Chasm bonfire post-kill
- [ ] Past Dusk at Manus's corpse (+ Elizabeth paradox death)

---

## 7. Cross-area systems

**"Gated set" — 20 enemies that only spawn under Black World Tendency** (events 11215090/11215091/11215092):
entities 1210900–1210925 (existing parts: c4120_0011–0013; c4130_0039–0042, 0046–0049; c4150_0028, 0029, 0030;
c4160_0012, 0013, 0015; c4180_0003, 0005, 0006). At load they are disabled; they are enabled (flag 5000) only after flag
11210080 is set by 11215092, which requires **World Tendency (Black) > 50 while in m12_01** across six consecutive checks
(and flag 735 OFF); after 600 s with tendency ≤ 50 they are force-killed (flag 5001). DS1 has no player-facing world
tendency, so in normal DSR play these never appear (unverified in play). Their NpcParams (412001, 413004/413014, 415001,
416001, 418001) are the "+1" variants. Recommend: skip or treat as optional hard-mode spawns.

**MSB idle poses (InitAnimID), routes and platoons** (from the full MSB enemy reader; every other enemy has InitAnimID −1)
| Part | Who | InitAnimID | Route / PointMoveType |
|---|---|---|---|
| c0000_0006 (6700) | past Dusk | 7915 (lying) | PMT 2 |
| c0000_0013 (6740) | Ciaran | 7880 | PMT 2 |
| c4090_0000 (6730) | Chester | 9001 (seated) | — |
| c4110_0000 (6720) | Gough | 9000 (seated) | — |
| c4130_0016/0017/0019/0020/0022/0023/0025/0027 (1210100–1210107) | ambush scarecrows | 9000 (dormant in grass) | platoons 55466/55467/55469 |
| c4520_0002 (1210502) | captive Sif | 7009 | — |
| c4531_0000 (6760) | Alvina | 9000 | — |
| c4120_0004 / c4120_0007 | Stone Guardians | — | 移動P旧土00→07 / reversed (PMT 1) |
| c4150_0017 | Bloathead | — | 大部屋屋根裏暖炉前の深淵人巡回00–03 |
| c4190_0001–0005 | Dogs | — | PMT 1 (no named points) |
| c4172_0020 | Humanity Phantom (S) | — | PMT 5 |
| c4500_0000 | Manus | — | PMT 2 |
Platoons (group aggro): Sanctuary Guardians 0001/0002 (55471); Stone Guardians 55464/55465/55468/55472/55473/55475 shared
with nearby scarecrows; scarecrow pairs 55474/55476/55477/55478; Township bloathead pairs 55480–55483; dogs 55479.

**Other MSB events:** 13 point lights (torches 松明01–05, fireplace, candelabra, glowing flowers), 131 sounds (boss BGM
entities 1213800 Guardian / 1213801 Artorias / 1213802 Manus / 1213803 Kalameet; illusory-wall hums 1213810/1213811),
177 SFX, 24 GIEL environment probes, wind, map offset (700,−300,900), navmesh door event 1213500 (Gough's door),
bonfire respawn spawn-points 1212963/1212961/1212962/1212950/1212964, 73 messages (3 gameplay hints listed above + 70 arena).

**NPC flag ranges:** Dusk 1120–1139 (shared with Darkroot), Gough 1820–1839, Chester 1840–1859, Ciaran 1860–1869,
Elizabeth 1870–1889. Talk-result flags: 11210590 (Ciaran trade), 11210591 (Ciaran leave), 11210592 (Gough shoots Kalameet),
11210593 (Gough's Greatbow), 11210594 (Elizabeth's mushrooms). Common event 911 awards lots 1500 / 1510 / 1520 for
11210590 / 11210593 / 11210594. Common event 745 watches hostile flags 1822 / 1841 / 1863 / 1871 (sets 744).

**Cutscenes in this DLC:** 120002 (portal, in m12_00), 120100 (Gough vs Kalameet), 120110 (Artorias), 120130 (arena),
120140 (Manus). Related: 120000 / 120003 (Sif boss intro in Darkroot Garden).

**Phantom-only fog walls** (event 0 disables them for non-clients): 1211994, 1211996, 1211998, 1211988, 1211986.
**Boss fogs re-enabled in DSR co-op after the boss is dead:** 11215843/11215846 slots for 1211790, 1211890, 1211690, 1211990.

**Talk ESDs in m12_01:** t121000–t121004 (5 bonfires), t121200 (arena ranking board), t121210/t121220 (empty),
t121661–t121666 (arena seats), t121670 (past Dusk), t121671 (empty), t121672 (Gough), t121673 (Chester), t121674 (Ciaran),
t121675 (Elizabeth).

**Porting notes (DLC-wide)**
- One-way entry: DS2 bonfire warping is always available, so add an explicit DLC exit or accept warp-out; respect "portal only
  in singleplayer" and "portal closes after Manus".
- Item ids needed: Broken Pendant (2520), Crest Key (2022), Soul of Artorias (710), Soul of Manus (711), Purple Coward's
  Crystal (118), Silver Pendant (220), Elizabeth's Mushroom (230), Guardian Soul, Calamity Ring, Guardian Tail, Obsidian
  Greatsword, Cleansing Greatshield, Gough's Greatbow/Great Arrow, Dark Bead/Dark Fog/Dark Orb/Black Flame, Oolacile
  Catalyst, Four-pronged Plow, Stone Greataxe, Chain Helm, Bloated (Sorcerer) Head, Guardian/Gough/Chester/Lord's Blade sets,
  Dark Silver/Gold Tracer, carvings (Help me!, Very good!, I'm sorry).
- Unique mechanics to build: tail cuts (Guardian ×3, Kalameet), flying dragon + grounding swap, light-only illusory walls,
  mimics, scripted NPC invader, boss-arena summon sign (Sif), past/present paradox deaths, Alvina vanishing, 5 elevators
  with "unlock from below" shortcuts, darkness in the Chasm.

**Area completion checklist — DLC gateway & globals**
- [ ] Darkroot: Hydra → Golden Crystal Golem (1200200) → Dusk (6050) → talk flags → Dusk leaves (1122/1126)
- [ ] Duke's Archives Crystal Golem 1700510 → Broken Pendant (conditions above)
- [ ] Darkroot portal region 1202008 (singleplayer, pendant, Manus alive) → cutscene 120002 → 1212510
- [ ] Sif intro variant (120003) in Darkroot when 11210021
- [ ] Global flags 17 / 121 and NPC flag ranges wired
- [ ] Gated set decision (skip/optional)
