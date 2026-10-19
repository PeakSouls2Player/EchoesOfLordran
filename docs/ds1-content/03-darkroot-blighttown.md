# 03 — Darkroot Garden, Darkroot Basin, Blighttown, Quelaag's Domain

DS1 content census for the "Lordran in DS2" port. Ground truth: DSR MSBs (`msb1-dump`), EMEVDs (`emevd-dump`), DSR params (`dump-param dsr`). Wiki knowledge is used only to name things; anything not confirmed by data is marked **(unverified)**.

Conventions
- Positions are DS1 world coordinates `(x, y, z)` straight from the MSB (all m10–m18 maps share one world frame, so cross-map adjacency can be read off coordinates).
- `ent` = MSB entity ID, `npc` = NpcParam row, `talk` = TalkParam/ESD id, `flag` = event flag.
- "BWT" = only enabled by the Black World Tendency event (see porting notes); "non-respawn" = killed state persisted by an event flag.
- Empty corpse = MSB treasure points at an ItemLotParam row that does not exist in DSR (`[missing]` in the dump) → the corpse gives nothing.
- Idle poses (`InitAnimID`), patrol routes (`MovePointNames`), `PlatoonID` and the MSB events section (generators, messages, spawn points, lights) come from the `msbev` reader (g01 scratch tool); see A.5b / B.5b.
- Quoted dialog/help texts come from `ds1-text`, which searches all FMGs and can return the wrong string for an id — treat them as hints.

## Map-file facts that matter first

| Area | MSB actually used by DSR | Notes |
|---|---|---|
| Darkroot Garden + Darkroot Basin | **`m12_00_00_01.msb`** (DLC variant) — not `m12_00_00_00.msb` | DSR ships both. `_01` = `_00` + DLC portal regions (`1202008` portal trigger, `1202502` arrival, `1202802` Sif post-rescue cutscene warp), Sif moved to (255.09, −15.60, −310.25), extra Basin collisions (`h0069B0_0000..0003`, `h0125B0_0000`). Enemies/objects/treasures otherwise identical. EMEVD is the single `m12_00_00_00.emevd.dcx`. **Port from `_01`.** |
| Darkroot Basin bonfire + Basin→Valley elevator | **`m16_00_00_00.msb`** (New Londo map) | Bonfire `c1000_0002` ent 1600961 / object `o0200_0002` ent 1601961 at (165.0, −76.2, −56.0), flag 11600984, talk 160001; collision `h0062B0_0000` carries mapName 1201 "Darkroot Basin" with `disableBonfire=1601961`. Elevator ("森庭エレベータ" = forest-garden elevator) object `o6100_0007` ent 1601231 top (176.6, −78.6, −84.6) ↔ bottom (176.6, −166.5, −84.6). |
| Valley of Drakes | **`m16_00_00_00.msb`** (not m14_00, not m10_00) | See Part C pointer section; belongs to the New Londo/Valley doc. New Londo's lower parts are also m16_00. |
| Blighttown + Quelaag's Domain | `m14_00_00_00.msb` (no `_01` variant) | Collisions use mapName 1400 (Blighttown) / 1401 (Quelaag's Domain). Demon Ruins is the separate `m14_01_00_00`. |

Brief corrections
- "Golem guarding Havel set in Basin": not in the data. Havel the Rock is `m10_01` (Undead Burg) ent 6580 at (59.41, −38.24, −111.39), standing right behind Darkroot's co-op boundary fog `o2902` (64.8, −38.2, −111.4) — i.e. the Watchtower basement opens into Darkroot Basin. Havel's gear drops from Havel (m10_01). The Basin golem is the **Golden Crystal Golem holding Dusk**.
- Hawkeye Gough: not in m12_00; he is in the DLC map m12_01 (reached via the Basin portal, Part A).
- "Pharis": no Pharis NPC exists in these MSBs; only the Black Bow of Pharis, dropped by Forest Hunter "Hunter" (lot 8000).
- Broken Pendant: ItemLot 27100200 is *named* "Crystal Golem – Darkroot Garden" but its pickup flag is 51700930 (m17, Duke's Archives) and nothing in m12 awards it → the Pendant comes from the Archives golem, not the Basin.

---

# PART A — Darkroot Garden & Darkroot Basin (m12_00_00_01 / EMEVD m12_00_00_00)

## A.1 Header

- Place names (collision mapName): 1200 = Darkroot Garden, 1201 = Darkroot Basin.
- Default spawn / warp-in: `c0000_0001` ent 1200999 (97.94, −5.12, 34.92) at the Undead Parish entrance.
- Entrances / exits:

| Connection | Where (DS1 coords) | Requirement |
|---|---|---|
| Undead Parish (m10_01) ↔ Darkroot Garden entry path | co-op boundary fog `o2901` ent 1201994 (87.8, −5.1, 40.8); spawn point (97.9, −5.1, 34.9) | none (route from Andre's side of the Parish, unverified exact door) |
| Undead Burg (m10_01, path under the Hellkite bridge) ↔ Darkroot Basin (Black Knight path) | co-op fog `o2903` ent 1201998 (156.0, −74.4, −30.0) | none |
| Undead Burg watchtower basement (Havel) ↔ Darkroot Basin (upper Basin) | co-op fog `o2902` ent 1201996 (64.8, −38.2, −111.4); Havel (m10_01) at (59.4, −38.2, −111.4) | door is opened with the **Watchtower Basement Key** (found in this map, top of Butterfly tower) — door is in m10_01 (unverified which side) |
| Darkroot Basin ↔ Valley of Drakes (m16_00) | elevator `o6100_0007` ent 1601231 at (172.4, −78.6, −83.8) (m16 MSB) | none |
| Darkroot Garden ↔ Darkroot Basin (forest descent) | ladders `o2401` ent 1201141 (172.2, −31.2, −145.0) and `o2400` ent 1201140 (160.5, −70.0, −180.4); co-op fog `o2907` ent 1201988 (164.5, −33.4, −172.0) | none |
| Darkroot Basin → Oolacile (DLC, m12_01) | trigger box `1202008` (222.0, −73.0, −304.0) 4×4×4 next to the Golden Golem spot; SFX region (222, −70, −304); arrival `1212510`/`1202502` in m12_01 | own **Broken Pendant** (goods 2520) + one of Dusk flags 1125/1126/1127 + Manus not dead (flag 17 OFF) + singleplayer (event 11200006) |
| Crest gate → Forest Hunter grounds / Sif | gate `o2110` ent 1201000 (248.88, −3.82, −17.31) | **Crest of Artorias** (goods 2002) |

## A.2 Sub-zones (route order)

| # | Sub-zone | Rough bounds | Content |
|---|---|---|---|
| G1 | Garden entry path | x 97–235, y −0.5…−11, z −30…+35 | Demonic Foliage, Stone Knights, Tree Lizards, Walking Tree, light-wall checkpoint, Wolf Ring, Elite Knight set, Partizan, tree-man trap |
| G2 | Moonlight Butterfly tower | tower base (184.8, −10.3, 27.3) → bridge y≈8 (x 165–200, z 15–65) → top y≈33 | Butterfly (boss), Beatrice sign, Watchtower Basement Key + Divine Ember |
| G3 | Field behind the tower | x 149–186, y ≈ −29.5, z 41–78 | Frog-Rays, Walking Tree, Stone Knight, Soul of a Proud Knight |
| G4 | Bonfire + Crest gate | (248–258, −3, −12…−17) | Darkroot Garden bonfire, Crest of Artorias gate |
| G5 | Forest Hunter grounds (behind gate) | x 228–292, y −0.7…−8, z −55…−122 | Alvina, Forest Hunters, Shiva + bodyguard (covenant only), Stone set chest |
| G6 | Mushroom grove | x 230–293, y −8…−13, z −128…−187 | Mushroom Parents/Children, Enchanted Ember chest |
| G7 | Giant Cat pocket | x 186–215, y −12…−19, z −176…−211 | 3 Great Felines (mini-boss), Large Soul of a Brave Warrior |
| G8 | Sif gate + Grave of Artorias | gate (274.96, −19.37, −265.3); arena (250–265, −15…−18, −290…−325) | Great Grey Wolf Sif, Hornet Ring |
| B1 | Garden→Basin forest descent | x 135–192, y −9…−33, z −52…−145 | Demonic Foliage swarm, Eastern set, Soul of a Brave Warrior, ladders |
| B2 | Basin entry from Undead Burg | (154–165, −35…−78, −16…−60) | Black Knight, Grass Crest Shield, Basin bonfire (m16), Valley elevator (m16), Crystal Lizard, Leather set |
| B3 | Upper Basin (Havel tower exit) | (64–135, −38…−67, −111…−146) | Crystal Golems, Large Soul of a Nameless Soldier |
| B4 | Hydra lake | (91–160, −60…−73, −170…−235) | Hydra, Knight set, Dusk's summon-sign spots |
| B5 | Golden Golem / DLC portal | (211–223, −72, −299…−307) | Golden Crystal Golem (Dusk inside), Crown of Dusk set, DLC portal |

## A.3 Progression walkthrough

**Darkroot Garden (from Undead Parish)**
1. Enter at (97.9, −5.1, 34.9). First Demonic Foliage `c2330_0000..0002` stand disguised along the path eastward.
2. Near (196, −4.5, 26) area `1202110` ("first mimic tree-man") wakes Foliage ent 1200100. Wolf Ring corpse at (198.0, −0.5, 35.2) with a Stone Knight pincer: knights 1200651 (212.6, −3.3, 34.1, wakes at 2 m) and 1200650 (wakes at 6 m) both also wake when the player enters box `1202112` (212.5, −8, 38). Large Soul of a Nameless Soldier at (209.8, −4.1, 19.4).
3. **Butterfly tower**: Beatrice's white summon sign at the tower base `1202010` (184.76, −10.34, 27.28). Climb to the bridge (y≈8). Fog `o2904` ent 1201890 (179.7, 8.1, 29.5) → fight Moonlight Butterfly on the bridge → exit fog `o2905` ent 1201892 (197.97, 8.07, 64.74) (both removed on kill). Climb to the top: corpse (199.6, 33.06, 67.3) = Watchtower Basement Key + Divine Ember + Homeward Bone; empty corpse (199.25, 34.0, 64.1). Empty corpse (202.0, 8.24, 71.8) by the far bridge end.
4. **Field behind the tower** (y ≈ −29.5): entering box `1202113` (165, −31, 70) makes 4 Frog-Rays fly in. Walking Tree 1200001 (185.0, −29.6, 41.0), Stone Knight 1200650 (171.5, −29.5, 70.0), Soul of a Proud Knight (149.2, −29.4, 70.9). (How this field links back to G1 is not encoded in events — unverified.)
5. **Main garden toward the bonfire**: Elite Knight set corpse (198.6, −7.8, 7.0) ringed by 4 Foliage (1200105–108) and Stone Knights 1200655/656; Stone Knights 1200652–654 further on. Tree Lizards 1200600 (173.4, −8.0, −29.6) and 1200602 (153.6, −8.3, 7.6) hang on trees and drop when you get within 3 m. Partizan corpse (174.8, −10.2, −29.7) is a trap: area `1202111` wakes Foliage 1200104. Walking Tree 1200000 (218.4, −10.8, −14.1).
6. **Tree-man trap**: Large Soul corpse (229.2, −7.7, −20.0) ("樹人罠"); picking it up (flag 51200170) wakes Foliage 1200101–103 around it after 0 / 0.2 / 0.8 s.
7. **Light-wall checkpoint** `o2906` ent 1201700 (217.1, −10.1, −29.0): one-time "white light" fog (event 11200090 slot 0, front/back areas 1202600/1202601, SFX 1201701). Crossing it plays anim 7410 and deletes it permanently.
8. **Bonfire** (257.5, −3.3, −11.75) and **Crest of Artorias gate** 1201000 (248.88, −3.82, −17.31). A breakable object `o2120` ent 1201300 (253.64, −3.34, −12.24) sits beside the bonfire (persisted broken via flag 11200120).
9. **Behind the gate (Forest Hunter grounds)**: 7 Forest Hunter NPC phantoms (hostile to non-members), Alvina (285.82, −0.75, −105.13), Shiva + bodyguard (covenant only). Stone set chest (285.6, −6.65, −121.9) beside 3 empty corpses. Non-member hosts standing on hitboxes 1203500–1203504 get SpEffect 4500 "Forest Bandit Territory" (makes them a target for Forest Hunter covenant invaders; event 11200200).
10. **Mushroom grove**: Enchanted Ember chest (247.9, −13.0, −159.5) guarded by 2 Mushroom Parents + 8 Children (children flee to the parent area `1202114` at ≤80% HP). Empty corpse (237.8, −12.2, −168.8).
11. **Giant Cat pocket**: 3 Great Felines (one active, two asleep until the first drops to 40% HP). Large Soul of a Brave Warrior (213.6, −15.8, −210.8).
12. **Sif**: gate `o2111` ent 1201010 (274.96, −19.37, −265.3) (plain push gate, no key; event 11200100 slot 1, action area 1202501) with boss fog `o2900` ent 1201990 at the same spot. Grave of Artorias: Artorias's sword prop `o2300` ent 1201200 (253.31, −16.57, −321.54), Hornet Ring corpse (252.29, −16.25, −324.10).

**Garden → Basin descent**
13. Leave the main garden southward through the Foliage swarm (`c2330_0012..0025`, x 143–192, z −52…−125). Eastern set corpse (135.15, −10.9, −71.9); Soul of a Brave Warrior (183.3, −32.8, −130.4); ladder 1201141 (172.2, −31.2, −145.0) → co-op fog 1201988 (164.5, −33.4, −172.0) → ladder 1201140 (160.5, −70.0, −180.4) down to the Hydra lake.

**Darkroot Basin (from Undead Burg)**
14. Through co-op fog 1201998 (156.0, −74.4, −30.0): Black Knight 1200750 (160.8, −73.6, −26.0) guards the Grass Crest Shield (161.4, −77.8, −16.4).
15. Darkroot Basin bonfire (m16 MSB) (165.0, −76.2, −56.0); elevator down to Valley of Drakes (m16) at (172.4, −78.6, −83.8).
16. Slope above: Leather set + Longbow + Feather Arrows (154.5, −54.3, −60.0), Crystal Lizard 1200400 (162.0, −35.4, −58.5).
17. **Upper Basin / Havel exit** (64.8, −38.2, −111.4): Large Soul of a Nameless Soldier (113.7, −59.9, −111.4); 4 Crystal Golems (94–134, −60…−67, −124…−146).
18. **Hydra lake**: Knight set corpse (91.3, −71.9, −181.0); Dusk's random summon-sign spots `1202000..1202003` (92–98, −71, −170…−177); Hydra body 1200010 (98.2, −71.3, −232.8); breath zones `1202720..722` ("湖獣ブレス") around (152–160, −74, −180…−191).
19. **Golden Crystal Golem** 1200200 (211.48, −72.31, −298.89), present only after the Hydra is dead or flag 703 is ON. Killing it frees Dusk at the same spot. Crown of Dusk set corpse (223.2, −71.8, −307.0) appears after Dusk has been summoned. DLC portal `1202008` (222, −73, −304).

## A.4 Bonfires

| Bonfire | Map | Parts | Event registration | Talk | Notes |
|---|---|---|---|---|---|
| Darkroot Garden | m12_00 | `c1000_0000` ent 1200961 (257.5, −2.85, −11.75); object `o0200_0000` ent 1201961 | flag 11200984, kindling level 0 | 120001 | Next to the Crest gate. No Firekeeper. |
| Darkroot Basin | **m16_00** | `c1000_0002` ent 1600961 (165.0, −76.18, −56.0); object `o0200_0002` ent 1601961 | flag 11600984, kindling 0 (m16 EMEVD) | 160001 | Respawn point region (163.5, −76.76, −54.0). Disabled by collision `h0062B0_0000` (`disableBonfire=1601961`) |

## A.5 Enemies (every MSB enemy part, 106 parts total)

NpcParam names from DSR params. Souls from NpcParam `getSoul`.

**G1 Garden entry path / main garden**
| Enemy | Model | Count | Parts (ent) | Behaviour | Drops |
|---|---|---|---|---|---|
| Demonic Foliage (npc 233000) | c2330 | 3 | 0000 (133.0, −5.8, 26.5), 0001 (168.0, −4.1, 34.0), 0002 (206.0, −3.6, 18.0) — no entity | plain disguised tree-men | moss lot 23300000 |
| Demonic Foliage | c2330 | 1 | 0003 ent 1200100 (190, −4.0, 27) | AI off until damaged or player/Beatrice enters area 1202110 (event 11205250 slot 0), wakes with anim 9060 | moss |
| Demonic Foliage | c2330 | 3 | 0004–0006 ents 1200101–103 (225–231.5, −7.5…−7.7, −18.5…−24) | wake when tree-trap corpse is looted (flag 51200170) after 0/0.2/0.8 s, or on damage (11205290 slots 0–2) | moss |
| Demonic Foliage | c2330 | 1 | 0007 ent 1200104 (192.0, −10.4, −23.7) | wakes on damage or area 1202111 (Partizan ambush) | moss |
| Demonic Foliage | c2330 | 4 | 0008–0011 ents 1200105–108 (192–200.5, −7.95…−8.6, −0.5…9.0) | wake on damage or flags 11205263/11205264 (delays 0/0.6/0/≈0.95 s); those flags are not set by the map EMEVD (unverified source) | moss |
| (dummy) Demonic Foliage | c2330 | 1 | 0026 ent 1200090 (97.78, −5.13, 34.95) | not a real enemy: `Create Bullet Owner(1200090)` in event 0, disabled by 11205382 — bullet-owner dummy at the entry | — |
| Stone Knight (npc 238000) | c2380 | 1 | 0001 ent 1200651 (212.6, −3.26, 34.1) | statue idle until player within 2 m or in area 1202112 (11205280 slot 1), wake anim 9060 | Stone Greatsword / Greatshield lot 23800000 |
| Stone Knight | c2380 | 5 | 0002 ent 1200652 (200, −8.9, −9) r6 m; 0003 ent 1200653 (176.5, −10.7, 18.5) r10; 0004 ent 1200654 (169.8, −9.1, −0.2) r8; 0005 ent 1200655 (195.1, −8.2, 6.5) r4; 0006 ent 1200656 (197.5, −8.2, 4.0) r4 | statue idle until player/Beatrice within radius (11205260) | same |
| Stone Knight (BWT, npc 238002) | c2380 | 4 | 0007–0010 ents 1200900–903: (196, −9.6, −15), (205.0, −9.1, −7.7), (178, −10.2, −18), (216.5, −10.75, −17.7) | only when Black World Tendency event fires | same |
| Tree Lizard (npc 337000) | c3370 | 2 | 0000 ent 1200600 (173.39, −8.04, −29.57); 0002 ent 1200602 (153.60, −8.30, 7.57) | no gravity/map-hit (clinging to trunk) until player within 3 m or damaged (11205230), drop with anim 9063; on SpEffect 5110 home → nest 1202710 / 1202712 (11205240) | Egg Vermifuge lot 33700000 |
| Walking Tree (npc 335000) | c3350 | 1 | 0000 ent 1200000 (218.38, −10.80, −14.09) | gravity + map-hit off until player enters 1202100 (on the Butterfly bridge), then home 1202101 (11205000); non-respawn (11200810 slot 0) | none |
| Vagrant (Aggressive) 1 / 2 | c3491 | 2 | 0001 ent 1203911, 0004 ent 1203912 (201.86, −0.59, 33.83) | Vagrant system spawn points (online/item-drop driven, unverified); two variants share a spot | Evil / Humanity lots |

**G2 Butterfly tower**
| Moonlight Butterfly (npc 323000) | c3230 | 1 | 0000 ent 1200801 (210.43, 41.04, 59.37) | boss, see A.6 | Soul of the Moonlight Butterfly (lot 2530) |
|---|---|---|---|---|---|

**G3 Field behind the tower**
| Enemy | Model | Count | Parts | Behaviour | Drops |
|---|---|---|---|---|---|
| Frog-Ray (npc 341000) | c3410 | 4 | 0001–0004 ents 1200250–253 (149–170.5, −29.5, 73.5–78.5) | disabled + no gravity until player enters 1202113 ("カエルエイ飛ぶ"), appear after 0 / 0.5 / 1.2 / 0.7 s with anim 7000 (11205190) | Green Blossom lot 34100000 |
| Frog-Ray | c3410 | 1 | 0000 (220.0, −19.79, 23.0) no entity | plain | Green Blossom |
| Walking Tree | c3350 | 1 | 0001 ent 1200001 (184.97, −29.62, 41.0) | non-respawn (11200810 slot 1) | none |
| Stone Knight | c2380 | 1 | 0000 ent 1200650 (171.5, −29.5, 70.0) | wakes at 6 m or area 1202112 | Stone gear |
| Vagrant (Passive) | c3490 | 1 | 0001 ent 1203910 (185.99, −29.84, 75.72) | Vagrant spawn point | — |

**G5 Forest Hunter grounds** (all c0000 human NPC phantoms; team type switched by covenant events, see A.7)
| Enemy | Count | Parts / ent / npc | Notes |
|---|---|---|---|
| Forest Hunter – Bandit | 1 | `c0000_0003` ent 1200300 npc 6801 (280.68, −3.82, −91.08) | respawns |
| Forest Hunter – Knight | 2 | `c0000_0004` ent 1200301 npc 6802 (255, −7.79, −55); `c0000_0009` ent 1200306 npc 6802 (230, −7.12, −95) | non-respawn (11200810 slots 7, 9) |
| Forest Hunter – Sorcerer | 1 | `c0000_0005` ent 1200302 npc 6805 (264.59, −5.53, −81.68) | respawns |
| Forest Hunter – Cleric | 1 | `c0000_0006` ent 1200303 npc 6804 (250, −7.27, −60) | respawns |
| Forest Hunter – Hunter | 1 | `c0000_0007` ent 1200304 npc 6803 (228, −6.46, −102) | non-respawn; drops **Black Bow of Pharis** (lot 8000); keeps its treasure after death (Force Character Treasure) |
| Forest Hunter – Thief | 1 | `c0000_0008` ent 1200305 npc 6806 (250, −5.57, −80) | respawns |
| Vagrant (Passive) | 1 | `c3490_0002` ent 1203920 (283.77, −2.66, −117.90) | spawn point |

**G6 Mushroom grove**
| Enemy | Model | Count | Parts | Behaviour | Drops |
|---|---|---|---|---|---|
| Mushroom Parent (npc 227000) | c2270 | 2 | 0000 ent 1200703 (245.0, −12.83, −158.0); 0001 ent 1200704 (252.0, −12.73, −161.5) | guards chest | Gold Pine Resin lot 22700000 |
| Mushroom Parent (BWT, npc 227002) | c2270 | 2 | 0002 ent 1200908 (248.11, −13.0, −161.23); 0003 ent 1200909 (293.34, −8.03, −132.31) | BWT only | same |
| Mushroom Child (npc 228000) | c2280 | 8 | 0000–0007 ents 1200705–712: (240, −11.96, −185), (242, −12.26, −187), (231, −11.71, −146), (235, −12.11, −144), (275, −10.62, −150), (280, −11.63, −155), (285, −11.02, −180), (283, −10.88, −182) | at ≤80% HP get AI command 1 → flee toward parent area 1202114 (11205150); patrol points 移動Pt0–7 (ents 1202115–1202122) | Gold Pine Resin lot 22800000 |
| Mushroom Child (BWT, npc 228002) | c2280 | 4 | 0008–0011 ents 1200904–907: (251.01, −12.47, −164.21), (243.32, −12.36, −160.60), (292.02, −8.12, −129.13), (287.93, −8.73, −131.20) | BWT only | same |

**G7 Giant Cat pocket**
| Great Feline (npc 536000) | c5360 | 3 | 0000 ent 1200350 (199.0, −12.03, −195.0); 0001 ent 1200351 (190.0, −12.38, −202.0); 0002 ent 1200352 (198.0, −11.99, −206.0) | 1200350 is awake (home = nest 1202732 when in combat); 1200351/352 sleep (waiting anim 9000, AI off) until 1200350 ≤40% HP, then wake (anim 9060) (11205180). Combat areas 1202730/1202731. Non-respawn (11200810 slots 3–5) | none (lot 53600000 empty) |
|---|---|---|---|---|---|
| Vagrant (Aggressive) 1/2 | c3491 | 2 | 0002 ent 1203921, 0005 ent 1203922 (315.17, −11.86, −221.57) | spawn point | — |

**G8 Sif arena**: Great Grey Wolf Sif `c5210_0000` ent 1200800 npc 521000 (DSR `_01`: 255.09, −15.60, −310.25).

**B1 Garden→Basin forest descent**
| Enemy | Model | Count | Parts | Behaviour |
|---|---|---|---|---|
| Demonic Foliage (npc 233001) | c2330 | 7 | 0012–0018 no entity: (191, −11.43, −73), (185, −11.34, −80), (174.5, −11.04, −77), (150, −11.0, −71), (151, −11.12, −76), (148, −11.35, −81), (143, −12.28, −84) | plain |
| Demonic Foliage | c2330 | 3 | 0020 (176.5, −13.82, −118), 0022 (168, −12.81, −125) no entity; 0019 ent 1200109 (174, −14.02, −102.5) | 1200109 wakes at 8 m (11205200 slot 0) |
| Demonic Foliage | c2330 | 4 | 0021 ent 1200110 (168, −14.4, −116) r8; 0023–0025 ents 1200111–113 (179.5, −11.49, −60.5), (184.5, −10.85, −55.5), (192, −9.26, −52) r5 | radius / damage wake (11205200) |

**B2 Basin entry from Undead Burg**
| Black Knight (npc 279010, "Glaive" variant) | c2795 | 1 | 0000 ent 1200750 (160.80, −73.59, −26.0) | non-respawn; on death awards lot 27901000 (Black Knight Halberd / Shield) + chained 27901001 (Blue Titanite Chunk) once (11200810 slot 6) |
|---|---|---|---|---|
| Crystal Lizard (npc 330040) | c3300 | 1 | 0000 ent 1200400 (162.0, −35.44, −58.5) rot (22, −135, 0) | non-respawn; awards lot 33004000 Twinkling Titanite (+33004001 random material) (11200810 slot 2) |
| Vagrant (Passive) | c3490 | 1 | 0000 ent 1203900 (154.61, −53.86, −54.51) | spawn point |

**B3 / B4 Upper Basin & Hydra lake**
| Enemy | Model | Count | Parts | Behaviour / drops |
|---|---|---|---|---|
| Crystal Golem (npc 271001) | c2710 | 4 | 0000 (94.0, −63.03, −144.5), 0001 (113.0, −67.38, −145.5), 0002 (98.0, −60.07, −125.0), 0003 (134.0, −61.95, −124.3), no entity | respawn; Blue Titanite Chunk lot 27100100; 200 souls |
| Hydra (npc 353001) | c3530 | 1 | 0000 ent 1200010 (98.19, −71.31, −232.79) | see A.6; 5000 souls |
| Hydra's Head (npc 353101) | c3531 | 7 | 0000–0006 ents 1200011–1200017, all at (119.07, −60.20, −214.52) | severed-head props: disabled until the matching neck part breaks, then warped to the neck dummy and play anim 8100 |
| Vagrant (Aggressive) 1/2 | c3491 | 2 | 0000 ent 1203901, 0003 ent 1203902 (146.97, −72.31, −180.76) | spawn point |

**B5 Golden Golem**
| Crystal Golem (Gold) (npc 271100) | c2711 | 1 | 0000 ent 1200200 (211.48, −72.31, −298.89) | disabled until Hydra dead (11200801) or flag 703; DispMask bit 0 ON when loaded; death sets 11200800 → frees Dusk. 1500 souls, lot 27110000 empty |
|---|---|---|---|---|

**NPC parts that are not enemies**: Dusk `c0000_0012` ent 6050 and `c0000_0015` ent 6051; Shiva `c0000_0002` ent 6310; Shiva's Bodyguard `c0000_0013` ent 6420; Witch Beatrice `c0000_0014` ent 6521; Alvina `c5361_0000` ent 6380; bonfire `c1000_0000` ent 1200961. See A.7.

Count check: c0000 ×12, c1000 ×1, c2270 ×4, c2280 ×12, c2330 ×27, c2380 ×11, c2710 ×4, c2711 ×1, c2795 ×1, c3230 ×1, c3300 ×1, c3350 ×2, c3370 ×2, c3410 ×5, c3490 ×3, c3491 ×6, c3530 ×1, c3531 ×7, c5210 ×1, c5360 ×3, c5361 ×1 = 106.

## A.5b Idle poses, patrol routes, platoons, MSB events (from `m12_00_00_01.msb`)

Every part not listed below has `InitAnimID=-1`, no move points, `PlatoonID=0`, `PointMoveType=0`.

| Parts | InitAnimID (MSB idle pose) | Meaning / pairing with EMEVD |
|---|---|---|
| Demonic Foliage `c2330_0003..0011`, `0019`, `0021`, `0023..0025` (ents 1200100–1200113) | **9000** | disguised-as-tree pose; EMEVD sets cancel anim **9060** to wake. Plain foliage 0000–0002, 0012–0018, 0020, 0022 have no init anim (wander normally) |
| Stone Knights `c2380_0000..0006` (ents 1200650–656) | **9000** | statue pose; wake = cancel anim 9060 (11205260/280). BWT knights 0007–0010 have no init anim |
| Tree Lizards `c3370_0000`, `c3370_0002` | **9003** | clinging-to-trunk pose; drop = cancel anim 9063 (11205230) |
| Moonlight Butterfly `c3230_0000` | **7000** | perched pose; EMEVD also sets waiting anim 7000, take-off cancel anim 7001 |
| Great Felines `c5360_0001`, `c5360_0002` (1200351/352) | **9000** | sleeping; woken by 11205180 (cancel 9060). `c5360_0000` (1200350) is awake (no init anim) |
| Alvina `c5361_0000` (6380) | **9000** | perched/sitting pose (plus gravity off, immortal) |
| Dusk 6050 / 6051 | — (MSB −1) | pose applied by EMEVD: waiting anim 7875, 7876 (rescued), 7877→8289 (leave), 6951 (summon arrival) |

Patrol routes (`MovePointNames`)
- Mushroom Children `c2280_0000..0007` share an 8-point loop `移動Pt0..移動Pt7` (regions ents 1202115–1202122: (240,−13,−181) → (234,−13,−170) → (234,−14,−150) → (253,−14,−145) → (275,−13,−150) → (287,−13,−163) → (285,−13,−180) → (263,−13,−185)); pairs start at different offsets: 0000/0001 from Pt0, 0002/0003 from Pt2, 0004/0005 from Pt4, 0006/0007 from Pt6 → they circle the Enchanted Ember chest. BWT children 0008–0011 have no route.
- Walking Tree `c3350_0001` (1200001): `動く樹 移動ポイント00_00` (184.97, −29.62, 41.0) ↔ `00_01` (189.99, −29.25, 38.15) (short pacing).

Platoons: `PlatoonID=54464` groups Stone Knights `c2380_0000`, `0001` (the pincer pair) and BWT knights `0007..0010`.

MSB events section (non-ambient)
- SpawnPoints: `復活イベント01` ent 1202961 → region 復活ポイント00 (255.50, −3.35, −12.15) (bonfire respawn); `復活イベントboss` ent 1202950 → 復活ポイントboss (256.65, −15.95, −311.51) (Sif arena respawn).
- Messages (hidden dev blood messages): MessageID **2000** (87.1, −5.79, 33.6), **2001** (219.2, −19.71, 18.8), **2002** (237.7, −12.15, −188.9).
- Treasure ×24 / ObjAct ×2 as in A.8/A.9.
- Sounds ×123 (boss BGM `1203800` SoundID 120000000 in Sif area, mid-boss BGM `1203801` SoundID 120000001 on the Butterfly bridge, frogs, lake, waterfall, wind on the heights), SFX ×94, Environment (GI) ×20, Wind ×1.

## A.6 Bosses

### Moonlight Butterfly (mid-boss of the tower bridge)
- `c3230_0000` ent 1200801, npc 323000, name text 3230. Arena = tower bridge (BGM area 1202890, 15×15×65 at (182.69, 3.0, 38.34)).
- Fog walls: entry `o2904` ent 1201890 + SFX 1201891; exit `o2905` ent 1201892 + SFX 1201893. Fog traversal: 11205380 (host, help 10010403 "Traverse the white light", anim 7410), 11205381 (white phantoms after 11205383). Co-op fog management 11205843/11205846 slot 0 (param 11200900).
- Trigger 11205383: player inside `1202896` (178.5, 7, 30.2) → boss-room notification, player gets SpEffect 5500 ("prefer host").
- Intro 11205382: HP bar hidden, AI off, waiting anim **7000** (perched) until 11205383; then cancel anim **7001** (take-off), AI on, boss bar (NameID 3230).
- Flight logic 11205120 ×10: when the Butterfly is inside air box `1202220..1202229` and has event-message 10, it is short-warped to perch point `1202180..1202189` (y 13.1). Other logic regions: PC position monitors 1202240–1202245, ground branch points 1202190–1202199, markers 1202200–1202203, perch area 1202210, attack-test box 1202700 ([TEST]).
- Summon: **Witch Beatrice** (ent 6521, npc 6520/think 6521) white sign at `1202010` (184.76, −10.34, 27.28) when host is human, within 10 m, Butterfly alive (11205030; DSR copy 11205035 also needs multiplayer-count check + SpEffect 28 "Dried Finger"). Summon flag 11205031 (sets 11200300 permanently), dismissal 11205033. Once summoned and the fight starts, she walks to `1202898` and passes the fog (anim 7410, 11205032). Sign erased via 11205990.
- Death 11200900: flag 11200900, clears SpEffect 5500, removes both fogs. Reward via common event 970 slot 5 → lot **2530 Soul of the Moonlight Butterfly**. Lot 32300000 empty.

### Great Grey Wolf Sif
- `c5210_0000` ent 1200800, npc 521000, name 5210, souls via boss handler. Boss flag **5**.
- Fog `o2900` ent 1201990 + SFX 1201991 (274.96, −19.37, −265.3); gate `o2111` ent 1201010 at the same spot. Entry region `1202998`; PC move target `1202997`; fight-start box `1202996`; BGM area `1202990`.
- First entry 11200000 (when 11210021 OFF = Sif not rescued in the DLC): player reaches the grave sphere `1202999` (253.31, −16, −320.35) → cutscene **120000** (warp to `1202801`), sword prop `o2300` ent 1201200 disabled, Sif warped to `1202800`, fog enabled → flag 11200002.
- Alternate 11200002 (when 11210021 ON = you rescued young Sif in Oolacile): cutscene **120003** (warp `1202802`, exists only in `_01` MSB) → flag 11200000.
- Fight 11205392: AI enabled after 11205393, boss bar NameID 5210. 11205396: at ≤10% HP SpEffect 5401 (limping animation offset).
- Death 11200001: flag 5, fog off. Rewards via common 970 slot 6 → lot 2540 **Covenant of Artorias** + 2541 **Soul of Sif**. Lot 52100000 empty.
- After: the Grave's Hornet Ring corpse is reachable; Sif's music areas off (11205394/95).

### Hydra (Darkroot Basin)
- Body `c3530_0000` ent 1200010; 7 neck parts created by 11205300 slots 0–6 (`Create NPC Part` HP 176 per part, part ids encoded in params 231342081…231735304; SpEffects 5430–5436 "Lake Beast Neck N Disconnected"). When a neck part reaches 0 HP: body anim 8000, head entity 1200011+n enabled at the neck dummy with anim 8100, part hidden/hit-disabled.
- Kill 11200801: all 7 heads dead OR body HP 0 → force death (souls awarded) → lot **35300100 Dragon Scale** + chained 35300101 **Dusk Crown Ring**. Non-respawn (flag 11200801); also unlocks the Golden Golem.
- Breath zones 1202720–1202722. MSB defines 7 heads (wiki often says 5 for Darkroot — verify in game).

### Mini-bosses
- **Golden Crystal Golem** (Dusk's prison) — see B5 table; event 11200800.
- **Great Felines ×3** — see G7; non-respawn.
- **Black Knight** — see B2.

## A.7 NPCs

| NPC | Part / ent / npc / talk | Where | What / animations | Quest flags & outcomes |
|---|---|---|---|---|
| **Alvina of the Darkroot Wood** (Forest Hunter covenant) | `c5361_0000` ent 6380, npc 536100, talk 120638 | (285.82, −0.75, −105.13), territory box `1202050` | Map hits off, gravity off, immortal (perched). Joining covenant (ESD) gives Cat Covenant Ring | Flags 1710–1712. Present when 1710 or 1712 ON. Damaged by player (11200538) → 1711, anim 9030, vanishes. If 1710 and covenant broken (746) → 1712 (11200539). If 1712 and talk flag 11200596 → 1711, anim 7003, vanishes (11200540). Covenant rewards (common 8200/911): 0 kills flag 11200592 → **Cat Covenant Ring** (lot 1160); 1 kill 11200593 → Divine Blessing (1170); 3 kills 11200594 → **Ring of Fog** (1180) |
| **Forest Hunters** ×7 | ents 1200300–306 (A.5) | G5 | Team logic: not in covenant → Enemy (11205058); in covenant → Ally (11205057). Attacking Alvina/any hunter while in covenant (11205060 → flags 11205060–68, or 745) → **covenant betrayal**: Betray covenant, sin +1 (flag 9001), flags 742 + 746, all hunters + Shiva hostile (11205056). | Hunter 1200304 drops Black Bow of Pharis |
| **Shiva of the East** | `c0000_0002` ent 6310, npc 6310, talk 120631 | (275.11, −3.50, −99.51) | Disabled by default; enabled only for a host who is in the Forest Hunter covenant and flags 1603/1601 OFF (11200535). Shop (ShopLineup 1700–1709: East Wood Grain Ring, Flamberge, Stone Greatsword, Demon Great Machete, Shotel, Uchigatana, Washing Pole, Demon's Greataxe, Claw, Demon's Spear — location of shop use unverified) | Flags 1600–1619 (1601 = moved to Blighttown, 1603 = hostile, 1604 = dead). Hit ≥10% → hostile together with bodyguard (11200501). Drops Murakumo, Iron Round Shield, Humanity (lots 6310–6312). Leaves Darkroot for Blighttown via 1601 (see Part B) |
| **Shiva's Bodyguard** | `c0000_0013` ent 6420, npc 6420, talk 120642 | (275.0, −4.15, −105.5) | Same enable/hostility as Shiva | Flags 1760–1769 (1763 hostile, 1764 dead). Drops **Dark Wood Grain Ring** (6420) + Humanity |
| **Dusk of Oolacile** (trapped) | `c0000_0012` ent 6050, npc 6050, talk 120605 | (211.48, −72.31, −298.89) (inside the golden golem) | Waiting anim 7875 (kneeling), 7876 on release; leave anims 7877 → 8289 | Flags 1120–1139. 1120 initial. Golem killed (11200800) & 1120 → **1121**: Dusk warped to golem (dummy 101), enabled (11200530). Talk accept (11200590) → **1122**, leaves (11200531); talk decline (11200591) → **1126**, leaves (11200532). HP 0 → **1125** (dead). |
| **Dusk (summon)** | `c0000_0015` ent 6051, npc 6050, think 6050, talk 120663 | (93.28, −70.83, −173.99) | Golden sign at one of 4 random spots `1202000..003` (SFX 1203100–103, picked by 11200533 when 1122 & no Broken Pendant, or 1129). Touching sign (help 50000000) warps her in (hitbox 1203150), arrival anim 6951 + SFX 120, waiting anim 7875 | 11205040 → 1123 (summoned, state 1122); 11205070 → 1130 (summoned, state 1129). Map load turns 1123→1122 and 1130→1129. Sells sorceries (ShopLineup 2200–2205: Hidden Weapon, Hidden Body, Cast Light, Repair, Chameleon, Oolacile Ivory Catalyst). With Broken Pendant and 1122/1125/1126 and Manus alive → 1127 (gone to Oolacile, 11200529). 1128 (set in DLC, unverified) → 1129 (back) / stays dead. |
| (Dusk's set) | corpse `o0504_0000` ent 1201600 | (223.20, −71.79, −306.98) | Disabled until flag 1123 or 1130 seen once (11200690) | **Crown of Dusk**, Antiquated Dress / Gloves / Skirt |
| **Witch Beatrice** | `c0000_0014` ent 6521, npc 6520, think 6521 | (184.76, −10.34, 27.28) | Butterfly summon only (A.6). Humanity event flag base 8932 | 11200300 = summoned at least once |
| Vagrants | c3490/c3491 (A.5) | various | online Vagrant system | — |

## A.8 Items & treasures (route order)

| Where | MSB part | Item(s) (ItemLot) | Pickup flag |
|---|---|---|---|
| G1 Wolf Ring spot (198.0, −0.51, 35.2) | o0500_0006 | **Wolf Ring** (1200060) | 51200060 |
| G1 (209.8, −4.10, 19.4) | o0500_0003 | Large Soul of a Nameless Soldier (1200030) | 51200030 |
| G2 bridge far end (202.0, 8.24, 71.8) | o0500_0000 | empty (lot 1200000 missing) | — |
| G2 tower top (199.6, 33.06, 67.3) | o0503_0000 "宝死体アンドレ00" | **Watchtower Basement Key** + **Divine Ember** + Homeward Bone (1200140–142 chain) | 51200140/141/142 |
| G2 tower top (199.25, 34.0, 64.1) | o0500_0005 | empty (1200050 missing) | — |
| G3 (149.21, −29.38, 70.91) | o0500_0014 | Soul of a Proud Knight (1200180) | 51200180 |
| G1 (198.62, −7.77, 7.0) | o0500_0007 | **Elite Knight Helm/Armor/Gauntlets/Leggings** (1200070–073) | 51200070 |
| G1 Partizan ambush (174.8, −10.24, −29.7) | o0500_0001 | **Partizan** (1200010) | 51200010 |
| G1 tree-man trap (229.22, −7.70, −20.0) | o0500_0016 "樹人罠" | Large Soul of a Nameless Soldier (1200170) — looting wakes 3 Foliage | 51200170 |
| G5 (287.5/291.0/289.25, −6.7, −119.7…−121.3) | o0500_0009/0010/0011 | empty ×3 (1200090/100/110 missing) | — |
| G5 chest (285.6, −6.65, −121.9) | o0110_0001 ent 1201651 (ObjAct 11200601) | **Stone Helm/Armor/Gauntlets/Leggings** (1200510–513) | 51200510 |
| G6 chest (247.9, −13.01, −159.5) | o0110_0000 ent 1201650 (ObjAct 11200600) | **Enchanted Ember** (1200500) | 51200500 |
| G6 (237.8, −12.21, −168.8) | o0500_0013 | empty (1200130 missing) | — |
| G7 (213.6, −15.8, −210.8) | o0500_0012 | Large Soul of a Brave Warrior (1200120) | 51200120 |
| G8 Grave (252.29, −16.25, −324.10) | o0504_0001 "墓島" | **Hornet Ring** (1200160) | 51200160 |
| B1 (135.15, −10.90, −71.9) | o0500_0008 | **Eastern Helm/Armor/Gauntlets/Leggings** (1200080–083) | 51200080 |
| B1 (183.27, −32.84, −130.39) | o0500_0015 | Soul of a Brave Warrior (1200190) | 51200190 |
| B2 (161.39, −77.79, −16.36) | o0500_0017 | **Grass Crest Shield** (1200200) | 51200200 |
| B2 (154.5, −54.31, −60.0) | o0500_0002 | **Leather Armor/Gloves/Boots + Longbow + Feather Arrows** (1200020–024) | 51200020 |
| B3 (113.66, −59.90, −111.44) | o0500_0004 | Large Soul of a Nameless Soldier (1200040) | 51200040 |
| B4 (91.28, −71.91, −181.0) | o0500_0018 | **Knight Helm/Armor/Gauntlets/Leggings** (1200210–213) | 51200210 |
| B5 (223.20, −71.79, −306.98) | o0504_0000 ent 1201600 (event-enabled) | **Crown of Dusk** + Antiquated Dress/Gloves/Skirt (1200150–153) | 51200150 |

Enemy/boss drops: Hydra → Dragon Scale + Dusk Crown Ring; Butterfly → Soul of the Moonlight Butterfly; Sif → Covenant of Artorias + Soul of Sif; Black Knight → Black Knight Halberd/Shield + Blue Titanite Chunk; Crystal Lizard → Twinkling Titanite; Crystal Golems → Blue Titanite Chunk; Forest Hunter Hunter → Black Bow of Pharis; Shiva → Murakumo etc.; Bodyguard → Dark Wood Grain Ring; Stone Knights → Stone Greatsword/Greatshield; Mushrooms → Gold Pine Resin; Frog-Rays → Green Blossom; Tree Lizards → Egg Vermifuge; Foliage → mosses.

## A.9 Interactive objects & scripted events

| Object / thing | Entity / part | Event(s) | What it does |
|---|---|---|---|
| Crest of Artorias gate | `o2110` ent 1201000, action area 1202500 | 11200100 slot 0 (flag 11200110, SFX 120020 on dummy 200, obj-SFX flag 61200500), 11200110 slot 0 | Needs goods 2002; player anim 7114, dialog 10010861 "Used Crest of Artorias", gate anim 1. Without crest: dialog 10010160 (locked). Stays open (reproduce anim 1 on reload) |
| Sif gate | `o2111` ent 1201010, action area 1202501 | 11200100 slot 1 (flag 11200111, SFX 120021), 11200110 slot 1 | Plain push gate (no key; param X16=1 skips the item check); 11200110 slot 1 shows "locked" text only when flag 703 is ON and gate not yet opened (meaning of 703 unverified) |
| Breakable by bonfire | `o2120` ent 1201300 | 11200120 | stays destroyed (flag 11200120) |
| Light-wall checkpoint | `o2906` ent 1201700, SFX 1201701, areas 1202600/1202601 | 11200090 slot 0 | one-time white fog; after crossing (anim 7410) object + SFX removed forever |
| Butterfly fogs | `o2904` 1201890 / `o2905` 1201892 | 11205380/81/83, 11200900, 11205843/846 | boss fog |
| Sif fog | `o2900` 1201990 | 11205390/91/93, 11200001, 11205843/846 slot 1 | boss fog |
| Artorias's sword prop | `o2300` ent 1201200 | event 0 + 11200000/11200002 | disabled once Sif's intro has played (Sif carries the sword) |
| Co-op boundary fogs | `o2901` 1201994, `o2902` 1201996, `o2903` 1201998, `o2907` 1201988, `o2908` 1201986 (at the Crest gate) + SFX 1201995/997/999/989/987 | event 0 | disabled for host/singleplayer; only stop phantoms |
| Ladders | `o2400` ent 1201140, `o2401` ent 1201141 | Register Ladder (flags 11200010–013) | Garden ↔ Basin descent |
| Chests | `o0110_0000` ent 1201650, `o0110_0001` ent 1201651 | 11200600 slots 0/1 (ObjAct flags 11200600/601) | open → treasure enabled after 10 frames |
| Dusk summon sign | areas 1202000–003, SFX 1203100–103 | 11200533, 11205040, 11205070 | random of 4 |
| Oolacile portal | area 1202008, SFX 1202009 | 11200005 (SFX on in singleplayer when eligible), 11200006 | action prompt 10010100 → cutscene **120002**, warp to m12_01 point 1212510, save |
| Forest Hunter territory | hitboxes 1203500–1203504 | 11200200 | non-covenant host gets SpEffect 4500 every 10 frames while standing there |
| Black World Tendency spawns | ents 1200900–909 | 11205082 (WT>50 in m12 for 6 frames → flag 11200050; after 600 s with WT≤50 → 11205085), 11205080 (enable when 11200050 ON and flag 735 OFF, sets 5000), 11205081 (11205085 or 735 → force-kill, set 5001) | extra Stone Knights/Mushrooms |
| Non-respawn handler | 11200810 slots 0–9 | Walking Trees 1200000/001, Crystal Lizard 1200400, Great Felines 1200350–352, Black Knight 1200750, Hunters 1200301/304/306 | kill once; award lot param once (human/hollow host only) |
| Map sounds | 1203800 (Sif BGM), 1203801 (Butterfly BGM) | 11205394/95, 11205384/85 | boss music on/off |
| Cutscenes | 120000 (Sif intro), 120003 (Sif intro after DLC rescue), 120002 (enter Oolacile) | — | movies |
| Blood messages (dev) | regions 魔法血文字1–3: (87.1, −5.79, 33.6), (219.2, −19.71, 18.8), (237.7, −12.15, −188.9) | — | pre-placed messages |
| Ambient | SFX ×87, insect sound ×84, birds ×12, frogs ×11, springs, waterfall, lake ambience (regions, no entity) | — | atmosphere |

## A.10 Porting notes

- **Use `m12_00_00_01.msb`** for geometry/regions; the EMEVD expects regions 1202008 / 1202502 / 1202802 that only exist there.
- **Basin bonfire + elevator live in m16_00** — whoever ports m16 must include them, or this area must import them (and the `disableBonfire` collision trick).
- **Crest of Artorias key door**, plain Sif gate, one-time light wall (DS2 has no "white light checkpoint" — use a one-shot fog gate + flag).
- **Forest Hunter covenant**: Alvina immortal/gravity-off perch, territory hitboxes (SpEffect 4500), team-type switching of 9 NPCs by covenant state, betrayal (sin, flags 742/746), kill-count rewards (Cat Covenant Ring, Divine Blessing, Ring of Fog). DS2 has no Forest Hunter covenant — needs a covenant slot or a scripted stand-in.
- **NPC summon signs**: Beatrice (white, boss), Dusk (golden sign at random of 4 spots that summons a *merchant* NPC into your world).
- **Moonlight Butterfly**: flying boss with scripted perch warps (10 air boxes → 10 perch points) and perched-idle / take-off animations 7000/7001.
- **Sif**: two intro cutscenes keyed on DLC progress (11210021), sword prop removal, ≤10% HP limp SpEffect 5401.
- **Hydra**: 7 breakable neck NPC-parts with spawned severed-head entities; ranged water breath zones.
- **Ambush AI**: disguised Foliage (wake on area/radius/loot flag/damage), Stone Knight statues, Tree Lizards clinging (no gravity), Frog-Rays hidden until triggered, sleeping Great Felines woken by an HP threshold of another cat, mushroom children fleeing to parent.
- **World tendency**: Black WT spawns (DS2 has no world tendency → map to a flag or drop).
- **Vagrants**: c3490/c3491 placements are Vagrant system spawn points; skip or stub.
- **DLC hook**: Oolacile portal (Broken Pendant from Archives golem), Dusk state machine shared with m12_01.
- Lake (Hydra) water and the Basin drop-offs are geometry; no poison/curse here.

## A.11 Area completion checklist — Darkroot

- [ ] Map from `m12_00_00_01` (+ Basin bonfire/elevator from m16_00)
- [ ] Entrances: Parish path, Undead Burg path (Black Knight), Havel basement door (Watchtower Basement Key), Valley elevator, Garden↔Basin ladders, Oolacile portal
- [ ] Bonfires: Darkroot Garden (1200961), Darkroot Basin (1600961, m16)
- [ ] Crest of Artorias gate (1201000), Sif gate (1201010), light-wall checkpoint (1201700), breakable (1201300), 2 ladders, 2 chests, 5 co-op fogs
- [ ] 106 enemy parts incl. 14 BWT/vagrant special cases, ambush scripts (11205150/180/190/200/230/240/250/260/280/290, 11205000)
- [ ] MSB idle poses (Foliage/Stone Knights/Felines/Alvina 9000, Tree Lizards 9003, Butterfly 7000), mushroom-child 8-point patrol loop, Walking Tree 2-point route, platoon 54464, respawn points 1202961/1202950, dev messages 2000–2002
- [ ] Moonlight Butterfly (fogs, perch warps, Beatrice sign), Sif (2 cutscenes, sword prop), Hydra (7 neck parts), Golden Golem, Great Felines, Black Knight, Crystal Lizard
- [ ] NPCs: Alvina + covenant, 7 Forest Hunters, Shiva + bodyguard (shop), Dusk (rescue → shop sign → set corpse), Beatrice
- [ ] 24 treasure points (8 empty) incl. Wolf Ring, Watchtower Basement Key + Divine Ember, Elite Knight, Partizan, Stone set, Enchanted Ember, Hornet Ring, Eastern set, Grass Crest Shield, Leather set, Knight set, Crown of Dusk set
- [ ] Boss/enemy drop lots (2530, 2540/2541, 35300100/101, 27901000/001, 33004000, 8000, 6310–6312, 6420/6421)
- [ ] Event flags 5, 11200900, 11200801, 11200800, Dusk 1120–1139, Alvina 1710–1712, Shiva 1600–1619/1760–1769, covenant 742/746/11205050–68

---

# PART B — Blighttown & Quelaag's Domain (m14_00_00_00)

## B.1 Header

- Place names: 1400 = Blighttown, 1401 = Quelaag's Domain.
- Player parts: `c0000_0000/0001` (−217.0, −95.0, −26.0) ent 1400999 (Depths door entry / default); `c0000_0002` ent 1400980 (143.34, −252.52, 90.13) (arrival point by the Daughter of Chaos bonfire).

| Connection | Where | Requirement |
|---|---|---|
| The Depths (m10_00) → upper Blighttown | Depths door object **1001200** (m10_00 entity); co-op fog `o4952` ent 1401994 (−215.73, −95.42, −26.03) | **Blighttown Key** (Gaping Dragon drop, lot 2500). From the Blighttown side the door says "Does not open from this side" (10010161) unless flag 590 is ON (event 11400230) |
| Valley of Drakes (m16_00) → Blighttown back cave | co-op fog `o4953` ent 1401996 (−118.0, −139.25, 44.5); cave at y≈−139 | none (unverified) |
| Blighttown swamp → The Great Hollow (m13_02) | co-op fog `o4954` ent 1401998 (−322.07, −206.44, 83.0) at the west swamp | illusory wall in DS1 (handled in m13_02 doc; unverified here) |
| Swamp → Quelaag's Domain | boss fog `o4950` ent 1401990 (14.30, −236.90, 113.50) | — |
| Quelaag's arena → bell tower / Daughter of Chaos / Demon Ruins (m14_01) | second fog `o4951` ent 1401992 (89.04, −236.91, 131.82) | Quelaag dead (fog removed) |

## B.2 Sub-zones (route order)

| # | Sub-zone | Rough bounds | Content |
|---|---|---|---|
| T1 | Upper scaffolds (from Depths) | x −200…−295, y −95…−137 | Infested Ghouls, Barbarians, Blowdarts, dogs, creaky floors, ladders, Shadow set, Iaito, first bonfire |
| T2 | Mid scaffolds | x −200…−295, y −142…−155 | Wall Hugger, Power Within, Humanity, Eagle Shield, light-wall checkpoint |
| T3 | Valley-side cave + waterwheel level | x −105…−200, y −139…−184 | Key to New Londo chest, Fire Keeper Soul, Crimson set + Remedy chest, Whip, dogs, mosquitoes, cragspiders |
| T4 | Lower scaffolds | x −200…−250, y −178…−212 | Cragspiders, Wanderer set, Dragon Scale chest |
| T5 | Swamp (poison) | y ≈ −215, x −90…−372 | Swamp bonfire, Leeches (west), boulder Barbarians (east), Mosquitoes, NPCs (Quelana, Shiva, Siegmeyer, Griggs), Mildred invasion, many corpses |
| Q1 | Quelaag's Domain approach | (−23…+14, −222…−237, 109–117) | Egg Carriers + Chaos Bugs, Mildred sign, boss fog |
| Q2 | Quelaag's arena | (17–90, −237…−243, 100–140) | Chaos Witch Quelaag (lava) |
| Q3 | Bell tower | lever (129.54, −236.4, 125.83), bell (122.61, −224.17, 125.85) | Second Bell of Awakening |
| Q4 | Daughter of Chaos chamber | (125–155, −251…−253, 70–120) | bonfire, Fair Lady, Eingyi, Chaos Servant covenant, Kirk's Thorns set, way down to Demon Ruins |

## B.3 Progression walkthrough

1. **Depths door** → (−217, −95, −26). Empty corpse (−239.25, −95.17, −31.02). Climb down ladders `o4100` 1401140 (−228.5, −110.7, −36.9) and `o4101` 1401141 (−228.3, −115.7, −33.1).
2. **First platforms** (y −121…−130): Infested Barbarians ×3, Infested Ghouls, Blowdart 1400200 (−254, −121.4, 33). Soul of a Proud Knight (−233.3, −128.7, −1.7). Ladder `o4102` 1401142 (−254.8, −130.4, 31.2).
3. **Creaky floors**: plank structure `o4200` ent 1401000 (−264.8, −125.2, 41.7) creaks/flexes (anim 1) while the player stands in areas 1402000–002 (11405100). `o4201` ent 1401001 has creak areas 1402010–012 but no event (static). Ladder `o4103` 1401143.
4. Shadow set (−249.4, −128.7, 61.2), Large Soul NS (−256.5, −131.2, 60.8), Soul PK (−253.1, −131.8, 60.5); Iaito on the outer ledge (−287.5, −132.2, 52.5); empty corpses (−276.9, −127, 48.6), (−252.0, −133.2, 70.6), (−262.7, −136.4, 75.0). Hidden Infested Ghoul 1400450 inside basket `o4820_0012` ent 1401300 (−256.9, −130.7, 76.1) — bursts out when the basket breaks or it is hit (11405200).
5. **Blighttown bonfire** (−277.5, −136.7, 72.1). Ladders `o4104`/`o4105` (1401144/145). Soul PK (−239.3, −136.0, 72.7).
6. **Mid level** (y −142…−155): second creaky structure `o4202` ent 1401002 (−272.8, −146.5, 58.2) with areas 1402020–024 (11405110). Humanity (−268.95, −150.8, 65.25); Blooming Purple Moss ×3 (−294.3, −147.2, 43.3); Soul PK (−217.3, −142.9, 75.5); Large Soul NS (−215.45, −147.8, 73.55); **Power Within** (−199.3, −148.4, 74.8) next to the **Parasitic Wall Hugger** 1400000 (−200.9, −144.9, 71.5, attack area 1402100); Eagle Shield (−221.7, −153.5, 113.2); empty (−245.2, −148.3, 77.1), (−209.0, −154.5, 62.2). Ladders `o4106`–`o4112`.
7. **Light-wall checkpoint** `o4956` ent 1401702 (−195.3, −161.0, 65.4) (11400090 slot 1, areas 1402602/603, SFX 1401703) — one-time white fog.
8. **Waterwheel level / Valley side** (y −156…−170): Whip (−197.3, −169.0, 65.8); **Mask of the Sealer + Crimson Robe/Gloves/Waistcloth + Tin Banishment Catalyst** (−190.5, −168.0, 71.8) and **Remedy chest** (−193.75, −168.1, 73.0) (wiki: reached by the branch at the top of the waterwheel; `o4000_0000` ent 1401100 at (−158.2, −178.7, 80.0) is likely the waterwheel — no event, unverified). Soul PK (−150.6, −160.7, 48.7); **Fire Keeper Soul** (−150.1, −167.6, 46.1); empty corpses (−152.1, −161.0, 69.0), (−164.9, −165.8, 65.1), (−171.0, −169.9, 83.3).
9. **Valley-side cave** (y ≈ −139): from Valley of Drakes through fog 1401996 (−118, −139, 44.5); 3 Infested Barbarians (−105…−118); **Key to New Londo Ruins chest** (−165.45, −139.1, 68.85) at the top of the cave.
10. **Lower scaffolds**: ladders `o4113`–`o4121`; **Wanderer Hood/Coat/Manchette/Boots + Falchion** (−247.6, −189.7, 90.4) by a Blowdart (−247, −190, 95.5); Cragspiders on the walls. **Dragon Scale chest** (−200.9, −212.4, 53.0) by ladder `o4122` 1401162 (−208.4, −212.4, 59.0) at swamp level.
11. **Swamp** (y −215): **Swamp bonfire** (−197.5, −215.0, 100.5). East: Large Titanite Shard (−121.65, −215.45, 100.0), empty (−100.55, −215.4, 92.35), boulder-throwing Barbarians, Quelana (−125.07, −214.84, 107.36). North: Great Club (−152.85, −214.8, 172.3), Tattered Cloth set + Heavy Boots + Poison Mist (−197.7, −215.4, 160.75), Large Soul PK (−228.7, −215.15, 152.7), (−175.2, −215.0, 121.4), (−234.3, −215.45, 104.0). West (Leech field): Green Titanite Shard (−313.4, −215.4, 97.5), **Server** (−354.0, −215.6, 100.0), Large Titanite Shard (−369.2, −214.8, 131.2); Great Hollow boundary (−322, −206, 83).
12. **Mildred invasion**: entering the 30 m sphere `1402061` around (−172.25, −215.64, 129.59) → Maneater Mildred black-sign NPC invasion at `1402060` (re-tried every 20 s until killed).
13. **Quelaag's Domain**: path east up to (−23…+14, −222…−237): 2 Egg Carriers (each spawns 5 Chaos Bugs on death). Mildred's white sign `1402050` (12.42, −237.07, 114.06) if she was killed as invader. Fog 1401990 → Quelaag.
14. **After Quelaag**: second fog 1401992 removed → bell tower: pull lever `o4560` ent 1401110 (ObjAct 11400201) → **Second Bell** rings.
15. **Daughter of Chaos**: down to (125–155, −252): bonfire 1401960 (Fair Lady's), Eingyi blocks the doorway until talked to, The Fair Lady, Kirk's Thorns set corpse (event-enabled). The route on to the Demon Ruins (m14_01) continues from here (geometry outside this MSB's events — see Demon Ruins doc).

## B.4 Bonfires

| Bonfire | Parts | Registration | Talk | Notes |
|---|---|---|---|---|
| Blighttown (upper) | `c1000_0002` ent 1400962 (−277.5, −136.22, 72.1); `o0200_0002` ent 1401962 | flag 11400976, kindling 0 | 140002 | wooden hut bonfire after the first descent |
| Blighttown swamp | `c1000_0001` ent 1400961 (−197.5, −214.54, 100.5); `o0200_0001` ent 1401961 | flag 11400984, kindling 0 | 140001 | — |
| Daughter of Chaos (Quelaag's Domain) | `c1000_0000` ent 1400960 (140.43, −252.11, 95.05); `o0200_0000` ent 1401960 | flag 11400992, **standard kindling 10** (Firekeeper bonfire; same as Firelink/Anor Londo Firekeeper bonfires) | 140000 | Fair Lady is its Firekeeper. If she dies (flag 140), using the bonfire shows 10010184 "Fire Keeper is absent. Cannot light bonfire." (event 140) |

## B.5 Enemies (every MSB enemy part, 154 parts total)

**T1/T2 Upper + mid scaffolds**
| Enemy | Model | Count | Parts (pos) | Notes / drops |
|---|---|---|---|---|
| Infested Ghoul (npc 206000–206002) | c2060 | 15 | 0000 (−264, −121.26, 25), 0001 (−261, −121.27, 20), 0002 (−236, −128.99, 0), 0003 (−246, −129.23, 23.5), 0004 (−251, −130.09, 27), 0007 (−260.96, −124.15, 48.66), 0008 (−260.18, −128.01, 51.9), 0009 (−260.65, −124.19, 53.5), 0010 (−256.61, −124.11, 51.48), 0016 (−249.1, −130.77, 80.33), 0017 (−223.75, −136.47, 82.51), 0018 (−218.87, −136.28, 83.5), 0021 (−249.82, −136.73, 86.27), 0025 (−204.5, −133.6, 74.0), 0026 (−202.5, −132.51, 53.2) | respawning |
| Infested Ghoul (hidden) | c2060 | 1 | 0012 ent 1400450 npc 206001 (−256.88, −130.37, 76.1) | warped inside basket o4820_0012 (1401300), no gravity/map-hit until basket destroyed or ghoul damaged (11405200) |
| Infested Ghoul (BWT, npc 206003–206005) | c2060 | 2 | 0006 ent 1400900 (−266.06, −121.24, 23.83); 0019 ent 1400903 (−211.42, −135.93, 88.82) | BWT only |
| Infested Ghoul | c2060 | 6 | 0005 (−202, −154.28, 64.5), 0013 (−287.76, −146.39, 53.4), 0014 (−290.47, −147.31, 53.2), 0022 (−219.53, −148.45, 97.53), 0023 (−215.84, −146.55, 81.24), 0024 (−218.72, −149.61, 83.63) | mid level |
| Infested Ghoul (BWT) | c2060 | 3 | 0011 ent 1400901 (−218.06, −151.85, 66.13); 0015 ent 1400902 (−215.91, −151.70, 68.85); 0020 ent 1400904 (−215.57, −143.16, 76.38) | BWT only |
| Infested Barbarian (Club) (npc 281000) | c2810 | 4 | 0000 (−234.6, −121.26, −12.5), 0001 (−251.45, −120.87, −1.5), 0008 (−251.89, −121.21, 4.5), 0002 (−213.0, −153.03, 66.0) | Large Club / Dung Pie (28100000/001) |
| Blowdart Hollow (npc 253002) | c2530 | 3 | 0000 ent 1400200 (−254, −121.41, 33), 0001 ent 1400201 (−223.06, −131.68, 72.2), 0002 ent 1400202 (−211.01, −151.09, 85.52) | non-respawn, one-time lot 25300200 Purple Moss (11400850); toxic darts |
| Flaming Attack Dog (npc 334100; Blighttown dogs, NpcParam community name) | c3341 | 4 | 0000 (−229.24, −136.59, 90.66), 0001 (−249.85, −149.41, 76.17), 0002 (−231.27, −136.68, 91.52), 0003 (−253.21, −149.39, 76.09) | packs of 2 |
| Parasitic Wall Hugger (npc 524000) | c5240 | 1 | 0000 ent 1400000 (−200.90, −144.93, 71.47) | non-respawn (flag 11400902 set if it is ever loaded dead; 11400900); death → EzState command 1200; attack area 1402100 "プリンスイザリス攻撃領域"; 1000 souls; no drop |
| Vagrant (Passive / Aggr. 1 / Aggr. 2) | c3490/c3491 | 3 | c3490_0000 ent 1403900 (−240.1, −128.71, 8.0); c3491_0000/0001 ents 1403901/1403902 (−210.87, −133.41, 76.72) | Vagrant spawn points |

**T3 Valley-side cave / waterwheel level**
| Enemy | Model | Count | Parts | Notes |
|---|---|---|---|---|
| Infested Barbarian (Club) | c2810 | 3 | 0010 (−118.35, −139.09, 51.22), 0011 (−104.93, −138.97, 30.45), 0012 (−110.22, −138.56, 30.85) | cave from Valley |
| Blowdart Hollow | c2530 | 5 | 0003 ent 1400203 (−158.61, −164.53, 67.1), 0005 ent 1400205 (−177.3, −159.88, 29.3), 0006 ent 1400206 (−169.7, −161.83, 48.5), 0007 ent 1400207 (−168.2, −161.83, 53.5), 0008 ent 1400208 (−152.5, −160.83, 44.1) | non-respawn |
| Flaming Attack Dog (think 334101) | c3341 | 8 | 0004 (−167.57, −166.95, 28.87), 0005 (−164.69, −168.71, 40.67), 0006 (−171.04, −167.59, 36.6), 0007 (−151.22, −167.92, 47.0), 0008 (−152.59, −167.92, 46.46), 0009 (−165.14, −170.41, 57.85), 0010 (−163.07, −171.03, 59.12), 0011 (−158.22, −183.14, 79.83) | — |
| Mosquito (npc 309000) | c3090 | 4 | 0034 (−178.14, −157.97, 77.17), 0036 (−179.36, −157.47, 79.4), 0079 (−158.21, −164.73, 82.48), 0081 (−158.12, −165.74, 75.97) | flying; fixed spawns |
| Cragspider (npc 340000) | c3400 | 4 | 0000 (−168.2, −159.47, 78.96), 0002 (−174.22, −170.32, 83.96), 0007 (−160.62, −148.23, 68.99), 0008 (−143.38, −171.98, 80.9) | wall-clinging fire spitters |

**T4 Lower scaffolds**
| Cragspider | c3400 | 8 | 0009 (−212.34, −178.42, 84.67), 0010 (−223.76, −179.15, 86.02), 0011 (−245.08, −203.97, 99.08), 0012 (−245.32, −208.15, 106.43), 0013 (−237.23, −211.48, 106.82), 0014 (−245.93, −212.63, 114.3), 0018 (−238.0, −190.92, 95.5) rot (−8,…), 0019 (−233.5, −192.14, 97.0) rot (−22,…) | tilted = on walls |
|---|---|---|---|---|
| Blowdart Hollow | c2530 | 1 | 0004 ent 1400204 (−247.0, −190.01, 95.5) | non-respawn |
| Mosquito | c3090 | 2 | 0085 (−245, −210, 117), 0086 (−240.7, −208.8, 118.8) | templates of generator 1403000 |

**T5 Swamp**
| Enemy | Model | Count | Parts | Notes |
|---|---|---|---|---|
| Leech (npc 338000) | c3380 | 23 | 0000 (−311.39, −215.35, 166.09), 0003 (−300.88, −215.59, 121.65), 0004 (−297.26, −215.59, 119.8), 0011 (−317.11, −215.35, 166.33), 0015 (−285.08, −215.6, 171.78), 0016 (−231.11, −215.63, 169.96), 0017 (−314.99, −215.57, 160.04), 0020 (−289.92, −215.65, 120.43), 0022 (−346.73, −215.64, 131.68), 0023 (−362.87, −215.36, 136.98), 0024 (−361.89, −215.38, 124.68), 0025 (−359.78, −215.41, 124.75), 0026 (−357.35, −215.45, 99.73), 0027 (−365.51, −215.42, 119.87), 0028 (−361.74, −215.49, 117.73), 0029 (−371.78, −214.86, 107.93), 0030 (−367.07, −215.36, 108.04), 0031 (−353.96, −215.64, 103.73), 0032 (−355.55, −215.59, 102.43), 0033 (−345.09, −215.59, 133.64), 0035 (−333.57, −215.63, 139.71), 0036 (−311.6, −215.63, 92.91), 0037 (−309.53, −215.63, 94.61) | west swamp; Large / Green Titanite Shard lot 33800000 |
| Infested Barbarian (Boulder) (npc 281100) | c2811 | 5 | 0000 ent 1400100 (−152.44, −214.77, 170.08), 0001 ent 1400101 (−98.61, −215.64, 121.86), 0002 ent 1400102 (−99.62, −215.64, 131.11), 0003 ent 1400103 (−90.75, −213.95, 130.07), 0004 ent 1400104 (−145.81, −214.62, 168.28) | boulder = invincible NPC part 2811 (HP 100, bullets ×0) destroyed on death (11405250); Pickaxe / Dung Pie |
| Infested Barbarian (Club) (BWT, npc 281001) | c2810 | 5 | 0003 ent 1400905 (−270.31, −215.02, 121.93), 0004 ent 1400906 (−271.37, −214.81, 128.69), 0007 ent 1400909 (−270.5, −214.77, 126.0), 0005 ent 1400907 (−92.12, −214.76, 126.88), 0006 ent 1400908 (−92.79, −214.63, 137.63) | BWT only |
| Cragspider | c3400 | 7 | 0001 (−106.74, −214.75, 89.26), 0003 (−164.96, −214.92, 104.85), 0004 (−185.86, −214.75, 107.05), 0005 (−172.65, −214.91, 121.23), 0006 (−226.24, −215.15, 114.14), 0015 (−147.52, −215.64, 73.45), 0016 (−218.03, −215.16, 119.21) | — |
| Mosquito | c3090 | 4 | 0058 (−109.51, −205.93, 106.99), 0059 (−107.97, −206.44, 107.5), 0090 (−201.7, −215, 138.2), 0091 (−205, −215, 140) | templates of generators 1403002 (0058/0059) and 1403001 (0090/0091) — see B.5b |
| Vagrant (Passive / Aggr.) | c3490/c3491 | 3 | c3490_0002 ent 1403910 (−253.06, −214.76, 145.71); c3491_0002/0003 ents 1403911/1403912 (−199.19, −215.64, 172.78) | spawn points |

**Q1 Quelaag's Domain approach**
| Egg Carrier (npc 321000) | c3210 | 2 | 0002 ent 1400402 (−17.33, −225.41, 116.18), 0003 ent 1400403 (−13.0, −227.33, 109.8) | on death 5 Chaos Bugs each are warped to dummies 100–104 and play anim 7000 (11405350 slots 3/4) |
|---|---|---|---|---|
| Chaos Bug (npc 322000) | c3220 | 10 | 0015–0019 ents 1400426–430 and 0020–0024 ents 1400431–435, all at (−19.6, −222.0, 116.32) | disabled until their carrier dies |

**Q2 Quelaag** `c5280_0000` ent 1400800 (77.26, −238.83, 124.99) — boss (B.6).

**Q4 Daughter of Chaos chamber**
| Eingyi (npc 321001) | c3210 | 1 | 0000 ent 6160, talk 140616 (136.18, −252.62, 103.47) | NPC (B.7) |
|---|---|---|---|---|
| Chaos Bug | c3220 | 5 | 0000–0004 ents 1400411–415 at (135.97, −249.64, 102.72) | spawn from Eingyi's egg-back when he dies (11405350 slot 0; with param 1 they are permanently removed once flag 11400533 is set) |
| The Fair Lady (npc 534000) | c5340 | 1 | 0000 ent 1400700, talk 140615 (134.12, −252.50, 91.97) | NPC/covenant (B.7) |

Human NPC parts: Siegmeyer `c0000_0003` ent 6282; Griggs (Hollow) `c0000_0004` ent 6132; Quelana `c0000_0005` ent 6170; Mildred `c0000_0007` ent 6530 and `c0000_0010` ent 6531; Shiva `c0000_0008` ent 6311; Shiva's Bodyguard `c0000_0009` ent 6421; bonfires `c1000_0000..0002`.

Count check: c0000 ×7, c1000 ×3, c2060 ×27, c2530 ×9, c2810 ×12, c2811 ×5, c3090 ×10, c3210 ×3, c3220 ×15, c3341 ×12, c3380 ×23, c3400 ×19, c3490 ×2, c3491 ×4, c5240 ×1, c5280 ×1, c5340 ×1 = 154.

## B.5b Idle poses, patrols, platoons, generators, MSB events (from `m14_00_00_00.msb`)

Every part not listed has `InitAnimID=-1`, no move points, `PlatoonID=0`. **No patrol routes in Blighttown.**

| Part | InitAnimID | Meaning |
|---|---|---|
| Siegmeyer `c0000_0003` (6282) | **7855** | sleeping/seated idle; EMEVD later sets cancel anim **7856** (wake) once flag 814 clears |
| Quelana `c0000_0005` (6170) | **7885** | her standing/seated NPC idle (unverified exact pose) |
| Shiva `c0000_0008` (6311) | **7870** | NPC idle pose (bodyguard 6421 has none) |
| Hidden Infested Ghoul `c2060_0012` (1400450) | **9004** | crouched inside the basket until released |
| Eingyi `c3210_0000` (6160) | **9001** | Egg Carrier variant idle (standing guard at the doorway) |
| Egg Carriers `c3210_0002`, `0003` | **9000** | idle/sitting egg-carrier pose |
| Dog `c3341_0011` (−158.22, −183.14, 79.83) | **9000** | lying/sleeping dog (others have none) |
| Quelaag, Fair Lady, Mildred | −1 | Fair Lady's posture comes from her model/AI; Mildred is spawned via sign |

Platoons: `PlatoonID=8928` = BWT Barbarians `c2810_0003`, `0004` (west swamp pair); `8929` = BWT Barbarians `c2810_0005`, `0006` (east swamp pair).

Generators (Mosquitoes are generator templates, not fixed spawns)
| Generator | Entity | Spawn template parts | Settings |
|---|---|---|---|
| 大蚊ジェネレート1 | 1403000 | `c3090_0085`, `c3090_0086` (lower scaffolds) | MaxNum 3, LimitNum 5, 1 per spawn, interval 1–3 s, initial 2 |
| 大蚊ジェネレート2 | 1403001 | `c3090_0090`, `c3090_0091` (swamp north) | same |
| 大蚊ジェネレート3 | 1403002 | `c3090_0058`, `c3090_0059` (swamp east) | same |
Event 11400100 disables each generator while any player is inside its 15 m control sphere (1402200/201/202) and re-enables it when everyone leaves → mosquitoes are topped up only out of sight. The 4 mosquitoes `c3090_0034/0036/0079/0081` are fixed spawns.

MSB events section (non-ambient)
- SpawnPoints: 1402960 → 復活ポイント00 (143.34, −252.52, 90.13) (Daughter of Chaos), 1402961 → 復活ポイント01 (−199.50, −214.71, 98.50) (swamp bonfire), 1402962 → 復活ポイント02 (−275.50, −136.73, 73.0) (upper bonfire), 1402950 → 復活ポイントboss (74.15, −238.83, 125.57) (Quelaag arena).
- Messages (hidden dev blood messages): MessageID **4000** (118.1, −251.74, 125.2), **4001** (−256.3, −136.32, 76.0), **4002** (−206.1, −142.38, 83.0).
- **93 point lights** (PointLightID 25) attached to the ladders `o4101…o4125` and scaffold props `o4404_*`, `o4405_*` (torches/lanterns along the scaffolds) — needed for Blighttown's lighting.
- Environment light points: Quelaag (クラーグ), sewer (下水内, Depths side), town (街), swamp (沼), cave (洞窟内).
- Sounds ×4, SFX ×105, Treasure ×41, ObjAct ×4 (bell lever + 3 chests).

## B.6 Boss — Chaos Witch Quelaag

- `c5280_0000` ent 1400800, npc 528000, name 5280. Boss flag **9**.
- Fogs: `o4950` ent 1401990 + SFX 1401991 (entry, (14.30, −236.90, 113.50)); `o4951` ent 1401992 + SFX 1401993 (exit, (89.04, −236.91, 131.82)). Entry region `1402998`, PC move target `1402997`, fight-start `1402996` (16.6, −238, 113.5), BGM area `1402990` (80×30×70 at (53.6, −243, 122.54)), map sound 1403800. Fog traversal 11405390/91 (anim 7410), co-op fog handling 11405843/846 (param 9).
- Intro 11405392: Quelaag disabled & AI off until 11405393 and the player is in 1402996; first time (11400000 OFF) cutscene **140000**, then enabled, boss bar NameID 5280, flag 11400000. Black phantoms/invaders entering end the script without the cutscene.
- Breakable parts: 11405396 creates NPC part **5281** (group 2, HP 300, defense SE/SFX 75); each time it breaks → forced anim **3011** (stagger), re-armed (Restart). 11405397 creates part **5280** (group 1, HP 1, SE/SFX 60) → anim **2007** (flinch) each break.
- Lava/fire: her lava pools and fire sword are attack/bullet data, not events (port with her moveset).
- Summon: **Maneater Mildred** white sign (B.7).
- Death 11400001: flag 9, both fogs off, backread removed after 3 s. Reward via common 970 slot 9 → lot **2570 Soul of Quelaag**. Lot 52800000 empty.
- After the kill: bell tower and the Daughter of Chaos chamber become reachable; Quelana's/Fair Lady questlines continue.

## B.7 NPCs

| NPC | Part / ent / npc / talk | Where | Behaviour / animations | Flags, conditions, outcomes |
|---|---|---|---|---|
| **Maneater Mildred** (invader) | `c0000_0007` ent 6530, npc 6531 | (−172.47, −214.79, 131.70) | NPC black-sign invasion (11405035): host human, not yet killed (11400901 OFF), inside sphere 1402061 → black sign at 1402060 (summon flag 11405036, dismiss 11405037), retried every 20 s | Killed → flag 11400901; drops **Butcher Knife** + Humanity (lots 6530/6531) |
| **Maneater Mildred** (summon) | `c0000_0010` ent 6531, npc 6530 | (12.42, −237.07, 114.06) | White sign at `1402050` for Quelaag (11405030; DSR variant 11405029 with multiplayer-count + Dried Finger SpEffect 28) once 11400901 ON and Quelaag alive. Summoned → walks to fog, anim 7410 (11405032). Sign erase 14005990 | summon flag 11405031, dismiss 11405033 |
| **Quelana of Izalith** | `c0000_0005` ent 6170, npc 6170, talk 140617 | (−125.07, −214.84, 107.36); territory sphere `1402400` r5 | Player inside her territory gets SpEffect 4140 "in the territory of Kurana" (11400220). Shop: pyromancies (ShopLineup 3400–3407: Fireball, Fire Orb, Great Fireball, Firestorm, Fire Whip, Combustion, Great Combustion, Undead Rapport) | Flags 1290–1309. Enabled if 1290 or 1296. Appears (1290 → **1291**) when the player owns a **Pyromancy Flame +10…+15 or Ascended +0…+5** (weapons 1331000–1331500, 1332000–1332500) (11400536). Bed of Chaos dead (flag 10) & 1291 → 1293 (11400538); 1293 + talk 11400595 → 1292 (11400537); leaves when (1290 & flag 10) or (1291 & flag 10 & 71400061 OFF) or (1292 & not loaded) (11400539). Fire Tempest gift after Bed of Chaos (lot 1300, common 911 flag 11400594); drop Fire Tempest (6170). Hostile 1294 (hit ≥10%, 11400510), dead 1295 |
| **Eingyi** | `c3210_0000` ent 6160, npc 321001, talk 140616 | (136.18, −252.62, 103.47); after-moving spot `1402300` (139.19, −252.52, 100.47), target `1402301` | Blocks the Fair Lady doorway. On talk flag 11400593 (with 1280) → **1281**, sets home 1402301, team "Friendly Enemy", AI command 10 (walk aside) until he reaches 1402300 → Ally (11400531). If 1280 is OFF at load he is placed at 1402300 directly. Shop (ShopLineup 3200, 3210–3212: Servant Roster, Egg Vermifuge, Poison Mist, Toxic Mist) | Flags 1280–1289. 1281 + covenant flag 753 → 1286 (11400532). Gifts: Egg Vermifuge (lot 1280, flag 11400591), Pyromancy Flame (lot 1290, flag 11400592). Hostile if Fair Lady dies (1272 → 1282, 11400501) or if hit (1283/1287, 11400502/503). Dead 1284 → his 5 Chaos Bugs emerge |
| **The Fair Lady** (Chaos Servant covenant) | `c5340_0000` ent 1400700, npc 534000, talk 140615 | (134.12, −252.50, 91.97); region "クラグスパイダー妹" 1402800 | Firekeeper of bonfire 1401960. Covenant index 9 joined via ESD; humanity offerings (ESD). After any damage SpEffect 5401 (alt animations, 11405000) | Flags 1270–1279 (1270 set if none set; 1272 dead). Covenant rewards (common 8200): rank 0 flag 11400596 → **Great Chaos Fireball** (1310), 11400597 → **Chaos Fire Whip** (1320), rank 2 11400598 → **Chaos Storm** (1330); 11400599 → lot 1340. Death (11400800): flag 140, sound 1403801 off, hit volumes 1403100/1403101 off; if player is a Chaos Servant → betray covenant + sin + flag 742. Drops **Fire Keeper Soul** (lot 53400000). Bonfire becomes unusable |
| **Siegmeyer of Catarina** | `c0000_0003` ent 6282, npc 6283, think 6280, talk 140628 | (−315.42, −214.91, 147.65) (west swamp) | Sleeping idle until flag 814 clears, then cancel anim **7856** (wakes) (11400554) | Flags 1490–1514. Enabled if 1501/1502/1512. Appears (→ 1501, flag 814) when 1497 & Firelink talk flag 11020592 & player in m14 (11400551). Talk 11400590 → 1502 (11400552) and gives **Pierce Shield** (lot 1040, common 911 slot 4). Hostile 1512, dead 1513; drop Speckled Stoneplate Ring (6280) |
| **Griggs of Vinheim (Hollow)** | `c0000_0004` ent 6132, npc 6131, think 6130 | (−201.55, −214.52, 144.92) | Hostile hollow ("Hostile Ally" team) | Flags 1250–1259. Enabled only if 1257; 1253 OFF & 1256 ON → 1257 (11400530). Dead 1254 (Griggs hollow-route conditions are Firelink-side, unverified) |
| **Shiva of the East** (Blighttown) | `c0000_0008` ent 6311, npc 6310 ("(Invaded)" row 6311 exists), talk 140631 | (−158.35, −215.07, 89.93) | Enabled only for a host in the Forest Hunter covenant with flag 1601, or when 1600 + talk flag 11400582 (→ 1601, 11400560). Disabled for non-members or after betrayal 746 (11400566/567) | Hit → hostile with bodyguard, betrays Forest Hunter covenant (742, 746) (11400504) |
| **Shiva's Bodyguard** (Blighttown) | `c0000_0009` ent 6421, npc 6420, talk 140642 | (−159.24, −215.0, 88.66) | as Shiva | flags 1760–1769 |
| Kirk's set | corpse `o0500_0037` ent 1401601 | (154.05, −251.42, 71.35) | Treasure enabled only when Kirk was killed in all 3 invasions: flags 11000810 (Depths), 11410810, 11410811 (Demon Ruins) — event 0 | **Helm/Armor/Gauntlets/Leggings of Thorns** (1400370–373) |

## B.8 Items & treasures (route order)

| Where | MSB part | Item(s) (ItemLot) | Flag |
|---|---|---|---|
| T1 Depths door (−239.25, −95.17, −31.02) | o0500_0001 | empty (1400010 missing) | — |
| T1 (−233.29, −128.72, −1.68) | o0500_0002 | Soul of a Proud Knight (1400020) | 51400020 |
| T1 (−249.38, −128.69, 61.17) | o0500_0004 | **Shadow Mask/Garb/Gauntlets/Leggings** (1400040–043) | 51400040 |
| T1 (−256.52, −131.17, 60.79) | o0500_0005 | Large Soul of a Nameless Soldier (1400050) | 51400050 |
| T1 (−253.10, −131.78, 60.53) | o0500_0009 | Soul of a Proud Knight (1400090) | 51400090 |
| T1 (−276.94, −127.0, 48.6) | o0500_0003 | empty (1400030 missing) | — |
| T1 ledge (−287.5, −132.19, 52.5) | o0500_0034 | **Iaito** (1400340) | 51400340 |
| T1 (−251.95, −133.23, 70.6) / (−262.7, −136.36, 75.0) | o0500_0011 / 0012 | empty (1400110/120 missing) | — |
| T1 (−239.33, −135.96, 72.71) | o0500_0010 | Soul of a Proud Knight (1400100) | 51400100 |
| T2 (−268.95, −150.78, 65.25) | o0500_0006 | **Humanity** (1400060) | 51400060 |
| T2 (−294.26, −147.15, 43.3) | o0500_0008 | Blooming Purple Moss Clump ×3 (1400080) | 51400080 |
| T2 (−245.2, −148.33, 77.09) | o0500_0007 | empty (1400070 missing) | — |
| T2 (−217.26, −142.89, 75.48) | o0500_0015 | Soul of a Proud Knight (1400150) | 51400150 |
| T2 (−215.45, −147.78, 73.55) | o0500_0014 | Large Soul of a Nameless Soldier (1400140) | 51400140 |
| T2 (−199.28, −148.41, 74.77) | o0500_0013 | **Power Within** (1400130) | 51400130 |
| T2 (−209.0, −154.52, 62.16) | o0500_0017 | empty (1400170 missing) | — |
| T2 (−221.7, −153.5, 113.18) | o0500_0016 | **Eagle Shield** (1400160) | 51400160 |
| T3 Valley cave chest (−165.45, −139.12, 68.85) | o0110_0000 ent 1401650 (ObjAct 11400600) | **Key to New Londo Ruins** (1400500) | 51400500 |
| T3 (−152.13, −160.99, 69.03) | o0500_0033 | empty (1400330 missing) | — |
| T3 (−150.60, −160.66, 48.74) | o0500_0021 | Soul of a Proud Knight (1400210) | 51400210 |
| T3 (−150.11, −167.56, 46.09) | o0504_0000 | **Fire Keeper Soul** (1400350; common event 812 watches flag 51400350) | 51400350 |
| T3 (−164.87, −165.79, 65.07) / (−171.0, −169.92, 83.26) | o0500_0022 / 0020 | empty (1400220/200 missing) | — |
| T3 (−197.26, −168.96, 65.78) | o0500_0018 | **Whip** (1400180) | 51400180 |
| T3 (−190.52, −168.0, 71.8) | o0500_0019 | **Mask of the Sealer, Crimson Robe/Gloves/Waistcloth, Tin Banishment Catalyst** (1400190–194) | 51400190/191 |
| T3 chest (−193.75, −168.13, 73.0) | o0110_0002 ent 1401652 (ObjAct 11400602) | **Remedy** (1400520) | 51400520 |
| T4 (−247.57, −189.74, 90.39) | o0500_0031 | **Wanderer Hood/Coat/Manchette/Boots + Falchion** (1400310–314) | 51400310/311 |
| T4 chest (−200.9, −212.36, 53.0) | o0110_0001 ent 1401651 (ObjAct 11400601) | **Dragon Scale** (1400510) | 51400510 |
| T5 (−234.31, −215.45, 104.0) | o0500_0032 | Large Soul of a Proud Knight (1400320) | 51400320 |
| T5 (−175.21, −215.03, 121.41) | o0500_0026 | Large Soul of a Proud Knight (1400260) | 51400260 |
| T5 (−158.93, −214.91, 91.22) | o0500_0000 (disabled) | empty (1400000 missing) | — |
| T5 (−121.65, −215.45, 100.0) | o0500_0023 | Large Titanite Shard (1400230) | 51400230 |
| T5 (−100.55, −215.36, 92.35) | o0500_0024 | empty (1400240 missing) | — |
| T5 (−152.85, −214.8, 172.3) | o0500_0025 | **Great Club** (1400250) | 51400250 |
| T5 (−197.69, −215.41, 160.75) | o0500_0027 | **Tattered Cloth Hood/Robe/Manchette + Heavy Boots + Poison Mist** (1400270–274; Poison Mist pickup flag 51000271) | 51400270 |
| T5 (−228.67, −215.15, 152.67) | o0500_0028 | Large Soul of a Proud Knight (1400280) | 51400280 |
| T5 (−313.4, −215.4, 97.5) | o0500_0029 | Green Titanite Shard (1400290) | 51400290 |
| T5 (−354.0, −215.6, 100.0) | o0500_0036 | **Server** (1400360) | 51400360 |
| T5 (−369.2, −214.81, 131.2) | o0500_0030 | Large Titanite Shard (1400300) | 51400300 |
| Q4 (154.05, −251.42, 71.35) | o0500_0037 ent 1401601 (event-enabled) | **Thorns set** (1400370–373) | 51400370 |

(The dump marks the three chests and two corpses `disabled=True`; for the chests this is normal — treasure is enabled by the chest ObjAct event.)

Enemy/NPC drops: Quelaag → Soul of Quelaag; Fair Lady → Fire Keeper Soul; Mildred → Butcher Knife; Quelana → Fire Tempest; Siegmeyer → Speckled Stoneplate Ring; Eingyi → (lot 6160 Servant Roster); Shiva/Bodyguard → Murakumo / Dark Wood Grain Ring; Leeches → Large/Green Titanite Shards; Barbarians → Large Club / Pickaxe / Dung Pie; Blowdarts → Purple Moss (one-time).

## B.9 Interactive objects & scripted events

| Object / thing | Entity | Event(s) | What it does |
|---|---|---|---|
| **Second Bell of Awakening** lever | `o4560_0000` ent 1401110 (129.54, −236.40, 125.83), ObjAct "鐘レバー" flag 11400201 | 11400200 | Pull → multiplayer event 10010 (bell SE 130300002 ×3 on `o4550_0000` ent 1401111 for everyone, 11415170) → Quelaag/Fair Lady backread off → cutscene **140010**, or **140015** if the first bell (11010700) is already rung → flag **11500200** (Sen's Fortress gate) → flag 11400200, achievement 30, lot 9030. Lever stays used after reload |
| Bell | `o4550_0000` ent 1401111 (122.61, −224.17, 125.85) | 11415170 | sound source |
| Creaky floors | `o4200_0000` ent 1401000 (areas 1402000–002); `o4202_0000` ent 1401002 (areas 1402020–024); `o4201_0000` ent 1401001 (areas 1402010–012, **no event**) | 11405100, 11405110 | play anim 1 (creak/flex) while player inside, network sync off |
| Hidden-ghoul basket | `o4820_0012` ent 1401300 | 11405200 | holds ghoul 1400450 until broken/hit |
| Breakable (Q4) | `o4570_0000` ent 1401200 (126.50, −251.63, 119.19) | 11400210 | stays destroyed (flag 11400210) (what it is: unverified) |
| Depths door (other map) | m10_00 object 1001200 | 11400230 | from this side: "Does not open from this side" unless flag 590 (then shown open) |
| Light-wall checkpoint | `o4956_0000` ent 1401702, SFX 1401703, areas 1402602/603 | 11400090 slot 1 | one-time white fog |
| Quelaag fogs | `o4950` 1401990, `o4951` 1401992 | B.6 | boss fogs |
| Co-op boundary fogs | `o4952` 1401994 (Depths), `o4953` 1401996 (Valley), `o4954` 1401998 (Great Hollow) + SFX 1401995/997/999 | event 0 | disabled unless client |
| Ladders ×31 | `o4100`…`o4125` parts, ents 1401140–1401170 | Register Ladder (flags 11400010–11400071) | — |
| Chests ×3 | ents 1401650–652 | 11400600 slots 0–2 | open → treasure |
| Waterwheel (likely) | `o4000_0000` ent 1401100 (−158.21, −178.67, 79.99) | none | ambient animated prop (unverified) |
| Mosquito generators | generators 1403000–1403002 (MSB events; templates c3090_0085/0086, 0090/0091, 0058/0059; max 3 alive, limit 5), control spheres 1402200 (−272.6, −215, 132.1), 1402201 (−205.3, −215, 140.6), 1402202 (−108.6, −215, 130.2) r15 | 11400100 slots 0–2 (flags 11405340–342) | generator **disabled while a player is inside** its sphere, re-enabled when all players leave |
| Scaffold torches | 93 MSB point lights on ladders o4101–o4125 and props o4404/o4405 | — | lighting (see B.5b) |
| Quelana territory | sphere 1402400 | 11400220 | SpEffect 4140 on player |
| Fair Lady room hits/sound | hits 1403100/1403101, sound 1403801 | 11400800 | turned off when Fair Lady dies |
| Daughter of Chaos bonfire lock | object 1401960 | 140 | after Fair Lady dies: "Fire Keeper is absent" |
| Black World Tendency spawns | ents 1400900–909 | 11405092 / 11405090 / 11405091 (flags 11400080, 11405095, 735, 5000/5001) | same scheme as Darkroot |
| Kirk's set | ent 1401601 | event 0 | enabled after Kirk's 3 deaths |
| **Unused / cut**: boss-part script for entity **1400500** (not in MSB) with NPC parts 3290–3293 and breakable props `o3910_0000..0011` ents 1401500–1401511 at (−136…−140, −215.12, 183.66) | — | 11405300/301/310/320/330 (never initialized) | dead code; props still placed in the swamp |
| **Dev leftovers** | regions [TEST] boss warp 1402098 (at the Depths entry) → 1402099 (86.9, −231.2, 510.1); cliff-cling regions 1402797–1402799 near z 440–481 | none | ignore |
| Blood messages (dev) | (118.1, −251.74, 125.2), (−256.3, −136.32, 76.0), (−206.1, −142.38, 83.0) | — | pre-placed messages |
| Ambient | SFX on scaffold pieces (o4404 ×25, o4405 ×15, o4111/o4109/o4120), drain sounds ×2 | — | atmosphere |
| Cutscenes | 140000 (Quelaag intro), 140010 / 140015 (second bell, without / with first bell) | — | movies |

Map props: o4400 ×161, o4408 ×106, o4401 ×70, o4407 ×57, o4402 ×45, o4411 ×33, o4404 ×28, o4405 ×19, o4406 ×3 (scaffold/plank pieces, many breakable), o4820 ×41 (baskets/barrels).

## B.10 Porting notes

- **Poison swamp** (lower level, y≈−215): DS1 applies poison buildup and slowdown through the swamp floor material (collision), not EMEVD — needs a DS2 poison-water material/area.
- **Toxic** from Blowdart Hollows, **fire** from Cragspiders, **lava** in Quelaag's arena (map hit material) and her moveset.
- **Verticality**: 31 ladders, flexing creaky floors, waterwheel, many fall-death drops; Blighttown is a frame-rate-heavy area (lots of breakable o44xx props).
- **NPC invasion + summon pair** (Mildred: black-sign invasion → white-sign summon) — DS2 has NPC invaders/summons; map 1:1.
- **Second Bell**: lever ObjAct + cutscene + global flag 11500200 shared with the first bell (11010700) to open Sen's Fortress.
- **Firekeeper bonfire** with kindling 10 and "Fire Keeper is absent" lockout on her death; Fire Keeper Soul drop.
- **Chaos Servant covenant** (Fair Lady, index 9): join, humanity offering ranks, rewards, betrayal on killing her; Eingyi's door-blocking walk-aside script.
- **Forest Hunter cross-link**: Shiva + bodyguard move here (flag 1601) only for covenant members.
- **Kirk** cross-map kill counter (Depths + Demon Ruins flags) gates the Thorns set.
- **Mosquito generators** (spawn while the player is away from the spot).
- **Black World Tendency** spawns (same as Darkroot).
- Entry door from the Depths is one-way (cannot be opened from Blighttown unless flag 590).
- The Demon Ruins (m14_01) continue from the Daughter of Chaos chamber; Great Hollow (m13_02) from the west swamp; Valley of Drakes (m16_00) from the back cave.

## B.11 Area completion checklist — Blighttown / Quelaag's Domain

- [ ] Map m14_00_00_00 with 3 co-op boundaries (Depths, Valley, Great Hollow) and the one-way Depths door
- [ ] Bonfires: upper Blighttown (1400962), swamp (1400961), Daughter of Chaos (1400960, kindling 10, Fair Lady lockout)
- [ ] 31 ladders, 3 creaky floors, waterwheel, hidden-ghoul basket, breakable 1401200, light-wall checkpoint 1401702, 3 chests
- [ ] Poison swamp material, lava in Quelaag's arena
- [ ] 154 enemy parts (incl. 10 BWT, 9 vagrants, 15 Chaos Bugs spawned by Egg Carriers/Eingyi, 5 boulder Barbarians with invincible boulder parts, 9 one-time Blowdarts, Wall Hugger)
- [ ] Mosquito generators 1403000–002 (templates c3090_0058/0059/0085/0086/0090/0091) with proximity disable
- [ ] MSB idle poses (Siegmeyer 7855, Quelana 7885, Shiva 7870, Eingyi 9001, Egg Carriers 9000, hidden ghoul 9004, sleeping dog 9000), platoons 8928/8929, respawn points 1402950/960/961/962, 93 scaffold point lights, dev messages 4000–4002
- [ ] Quelaag: 2 fogs, intro cutscene 140000, NPC parts 5280/5281, Mildred summon, Soul of Quelaag
- [ ] Second Bell lever + cutscenes 140010/140015 + flag 11500200
- [ ] NPCs: Mildred (invader + summon), Quelana (flame +10 gate, shop, Bed of Chaos follow-up), Eingyi (blocks, walks aside, shop, gifts), Fair Lady (covenant, Firekeeper, death effects), Siegmeyer (sleep/wake, Pierce Shield), Griggs hollow, Shiva + bodyguard (covenant), Kirk's set
- [ ] 41 treasure points (12 empty) incl. Shadow set, Iaito, Humanity, Power Within, Eagle Shield, Key to New Londo Ruins, Fire Keeper Soul, Whip, Crimson/Sealer set, Remedy, Wanderer set + Falchion, Dragon Scale, Great Club, Tattered Cloth set + Poison Mist, Server, Thorns set
- [ ] Flags 9, 140, 11400200, 11400000, Quelana 1290–1309, Eingyi 1280–1289, Fair Lady 1270–1279, Siegmeyer 1490–1514, Griggs 1250–1259, Mildred 11400901/11405031–37, covenant 742/746/753

---

# PART C — Pointer: Valley of Drakes (lives in m16_00_00_00)

Not in m14_00 or m10_00. For the m16 (New Londo / Valley) writer — data seen while checking:
- Drakes `c3520_0007, 0009, 0010, 0011, 0012, 0013` (npc 352002 "Drake", no entity) at (111–143, −171.5…−173.5, −28…−106).
- Undead Dragon `c3420_0000` ent 1600500 npc 342002 (27.74, −153.30, −59.85) beside Astora's Straight Sword (18.8, −154.72, −62.45), Dragon Crest Shield (20.35, −154.85, −63.3), Soul of a Proud Knight (22.1, −155.24, −69.7).
- Red Tearstone Ring (119.93, −152.04, −22.76); Humanity (119.55, −172.1, −110.95); Brigand Hood set (127.56, −173.5, −48.72); Composite Bow (117.15, −143.4, −20.2).
- Elevators: Darkroot Basin ↔ Valley ("森庭エレベータ", ObjAct 6101, object 1601231, areas 1602230/1602231); floodgate elevator ("水門", 1602220/1602221); cathedral elevator ("聖堂", 1602211).
- Darkroot Basin bonfire 1600961 (see Part A); the Blighttown back cave entrance is the m14 co-op fog 1401996 (−118.0, −139.25, 44.5).

---

## Sources
- DSR data: `map/MapStudio/m12_00_00_00.msb`, `m12_00_00_01.msb`, `m14_00_00_00.msb`, `m16_00_00_00.msb`, `m10_01_00_00.msb` (Havel position); `event/m12_00_00_00`, `m14_00_00_00`, `m16_00_00_00`, `common.emevd.dcx`; DSR params (NpcParam, ItemLotParam, ShopLineupParam, SpEffectParam, EquipParam*).
- Wiki cross-checks (names/locations only): [Fextralife – Giant Cat](https://darksouls.wiki.fextralife.com/Giant+Cat), [Fextralife – Blighttown](https://darksouls.wiki.fextralife.com/Blighttown), [Fextralife – Remedy](https://darksouls.wiki.fextralife.com/Remedy), [Fextralife – Valley of Drakes](https://darksouls.wiki.fextralife.com/Valley+of+Drakes).
