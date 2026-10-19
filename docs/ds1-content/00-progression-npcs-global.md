# DS1 content census 00: progression, NPC questlines, covenants, world state, global mechanics (cross-map)

Scope: everything that crosses map borders. The per-map files (01.., written by other agents) cover placements, routes,
enemies and items inside each map. This file covers the critical path, the boss order, every NPC questline end to end,
covenants, world-state toggles and the global mechanics the DS2 port has to translate.

Ground truth used (DSR, read-only):
- `event\common.emevd.dcx` (global events 0/50/250/260/290/350/7xx/8xx/9xx/8090/8131/8200/8300) and every
  `event\mAA_BB_00_00.emevd.dcx`. NPC logic lives in each map's **event 50** (NPC init) and its **110x05xx** events.
- MSB enemy parts with c0000 or talk ids (`msb1-dump enemies` + the orchestrator's `msbev.exe` for InitAnimID).
- Talk ESDs unpacked from `script\talk\mAA_BB_00_00.talkesdbnd.dcx` (t1xxxxx.esd) and dumped with `esd-dump`:
  shops (`1:22(begin,end)`), flag sets (`1:11(flag,1)`), covenant joins (`1:47(31,5,N)`), gestures (`1:66(N)` + goods 90xx),
  item removal (`1:52(3,id,n)`), ember unlocks (`1:49`).
- Params: NpcParam (drop lots), ItemLotParam (resolved to item names), ShopLineupParam (resolved, with prices and
  sold-out flags), GameAreaParam (boss soul rewards, keyed by boss entity id), EquipParamGoods.
- `DarkSoulsRemastered.exe` @0x1a309c0: the bonfire **warp table** (21 entries; see section 6.8).
- FMG (`msg\ENGLISH\item/menu.msgbnd`): Place_name ids 2000-2020 = warp names; Event_text 1001xxxx = prompts.

Conventions: `F1234` = event flag 1234. `E11020501` = event id. `e6301` = entity. "state flag" = the NPC's current
step inside its flag range (exactly one flag of the range is ON; template events do `BatchSet(range, OFF); Set(new)`).
`(unverified)` = inferred, not proven by data. Anim ids are c0000 TAE ids; their meaning is inferred from where they are
used (verify in TAE before porting).

Notes that matter globally:
- **Community NpcParam names are wrong in places**: NpcParam 6130/6131 "Griggs of Vinheim - Undead Burg / (Hollow)
  Blighttown" are actually **Laurentius of the Great Swamp** (Depths barrel, Firelink, hollow in Blighttown;
  talk 100613/102613; flag range 1250-1259). NpcParam 6010 "Lady of the Darkling" = the **Darkmoon Knightess**
  (Anor Londo firekeeper). ShopLineup 5000-5022 "[Griggs - Logan in DA]" = **Logan's** shop; 2100-2102 "[Griggs - Initial]"
  = **Rickert's** shop. ItemLot 2670 "[Firesage Demon] Orange Charred Ring" is awarded by the **Centipede Demon** event.
- **Darkroot**: DSR loads `m12_00_00_01.msb` (DLC variant). Its NPC/enemy parts are identical to `_00`; only `_01` has the
  Oolacile portal region 1202008 and Sif's post-DLC intro warp point 1202802 (orchestrator note). The emevd is shared.
- **Female Undead Merchant** (Lower Undead Burg in-game) is placed in the **m10_02** MSB (e6240), not m10_01.
- Talk files with no MSB placement (cut content): t100628 / t170628 (Siegmeyer in Depths / Archives; t170628 would give
  lot 1020 Pierce Shield via F11700591), t170643 (an Archives talker), t102645 (empty; Firelink c1000 e6450).
- NpcParam 6060 "Gray Saint" is unused; the Firekeeper is c2750 e6060 (NpcParam 275000) and is talked to through an
  invisible c1000 talk dummy beside her (talk 102606).

## 0. Map table (DS1 → place names)

| DS1 map | Contents (place-name FMG) | Bosses (entity) |
|---|---|---|
| m18_01_00_00 | Northern Undead Asylum (start; revisit) | Asylum Demon c2232 (1810800), Stray Demon c2230 (1810810) |
| m10_02_00_00 | Firelink Shrine (+graveyard path to Catacombs, aqueduct, NL elevator, part of Lower Burg w/ female merchant) | — |
| m10_01_00_00 | Undead Burg, Lower Undead Burg, Undead Parish (Sunlight Altar, church, bell tower) | Taurus c2250 (1010700), Bell Gargoyles c5350 (1010800/801), Capra c2240 (1010750) |
| m10_00_00_00 | The Depths | Gaping Dragon c5260 (1000800) |
| m14_00_00_00 | Blighttown, Quelaag's Domain | Chaos Witch Quelaag c5280 (1400800) |
| m12_00_00_01 | Darkroot Garden, Darkroot Basin | Moonlight Butterfly c3230 (1200801), Sif c5210 (1200800) |
| m15_00_00_00 | Sen's Fortress | Iron Golem c2320 (1500800) |
| m15_01_00_00 | Anor Londo | Ornstein c5270/c5271 (1510800/801) + Smough c2360 (1510810/811), Gwyndolin c5320 (1510650) |
| m11_00_00_00 | Painted World of Ariamis | Crossbreed Priscilla c2730 (1100160) |
| m13_00_00_00 | The Catacombs | Pinwheel c3320 (1300800) |
| m13_01_00_00 | Tomb of the Giants | Gravelord Nito c5220 (1310800) |
| m13_02_00_00 | The Great Hollow, Ash Lake | (Everlasting Dragon c3450 1320800 is an NPC/covenant) |
| m14_01_00_00 | Demon Ruins, Lost Izalith | Ceaseless Discharge c5250 (1410600), Demon Firesage c2231 (1410400), Centipede Demon c5200 (1410700), Bed of Chaos c5400/c5401/c5230 (1410800/802/801) |
| m16_00_00_00 | New Londo Ruins, Valley of Drakes, The Abyss | Four Kings c5390 (1600800-804) |
| m17_00_00_00 | The Duke's Archives, Crystal Cave | Seath c5290 (1700800; immortal Archives version 1700700) |
| m18_00_00_00 | Firelink Altar, Kiln of the First Flame | Gwyn c5370 (1800800) |
| m12_01_00_00 | DLC: Sanctuary Garden, Oolacile Sanctuary, Royal Wood, Oolacile Township (+Dungeon), Chasm of the Abyss, Battle of Stoicism | Sanctuary Guardian c3471 (1210800), Artorias c4100 (1210820), Kalameet c4510 (1210401), Manus c4500 (1210840) |

## 1. Critical path and dependency graph

### 1.1 Global boss / progression flags (set by map events, read everywhere)

| Flag | Meaning | Set by |
|---|---|---|
| 16 | Asylum Demon killed | m18_01 E11810001 |
| 11810900 | Stray Demon killed | m18_01 E11810900 |
| 11810000 | Left the Asylum (crow ride done) | m18_01 E11810000 |
| 200 | Firelink reached (first arrival; also warp-unlock flag of Firelink) | m10_02 E11020001 |
| 11010901 | Taurus Demon killed | m10_01 |
| 3 | Bell Gargoyles killed | m10_01 E11010001 |
| 11010700 | **1st Bell of Awakening rung** (Undead Parish tower) | m10_01 E11010700 (ObjAct 11010700, obj 1011110) |
| 11010902 | Capra Demon killed | m10_01 |
| 2 | Gaping Dragon killed | m10_00 E11000001 |
| 9 | Quelaag killed | m14_00 E11400001 |
| 11400200 | **2nd Bell of Awakening rung** (Quelaag's Domain) | m14_00 E11400200 (ObjAct 11400201, obj 1401110) |
| 11500200 | **Sen's Fortress gate open** (= both bells rung; set by whichever bell is rung second) | m10_01 E11010700 / m14_00 E11400200 |
| 11 | Iron Golem killed (unlocks batwing ride to Anor Londo) | m15_00 E11500001 |
| 12 | Ornstein & Smough both dead; 11510902 = Ornstein died last (Soul of Ornstein), 11510903 = Smough died last | m15_01 E11510001 |
| 120 | O&S-killed helper (cleared again if Gwynevere is killed) | m15_01 |
| 710 | **Player owns the Lordvessel → bonfire warping enabled** (msg 10010620) | common E710 (owns goods 2510) |
| 11800100 | **Lordvessel placed** at Firelink Altar (altar becomes bonfire 11800992, kindle 10) | m18_00 E11800100 |
| 11510400 | **Anor Londo darkened** (Gwynevere killed) | m15_01 E11510400 |
| 11510900 | Gwyndolin killed | m15_01 |
| 4 | Priscilla killed | m11_00 E11100000 |
| 5 / 11200900 | Sif killed / Moonlight Butterfly killed | m12_00 |
| 6 | Pinwheel killed | m13_00 E11300001 |
| 7 | Nito killed | m13_01 E11310001 |
| 11410900 / 11410410 / 11410901 | Ceaseless Discharge / Demon Firesage / Centipede Demon killed | m14_01 |
| 10 | Bed of Chaos killed | m14_01 E11410001 |
| 11600100 | **New Londo drained** | m16_00 E11600100 |
| 13 | Four Kings killed | m16_00 E11600001 |
| 11700000 | Seath real fight started (Crystal Cave intro cutscene seen) | m17_00 E11700000 |
| 14 | Seath killed | m17_00 E11700001 |
| 711/712/713 | Owns Lord Soul (Nito) 2500 / Lord Soul (Bed) 2501 / Bequeathed Shard (4K) 2502 (714 tracks 2504, unused item) | common E711 |
| 11800201-11800204 | Lord Soul 2500 / 2501 / Shard 2502 / Shard 2503 placed in the Lordvessel | m18_00 E11800200 |
| 11800211 (11800210) | **All four placed → Kiln door opens** (cutscene 180010) | m18_00 E11800210 |
| 15 | Gwyn killed | m18_00 E11800001 |
| 20 / 21 | Ending: Link the Fire / Dark Lord (both increment game cycle) | m18_00 E20 / E21 |
| 11210000 / 11210001 / 11210004 / 11210002+17 | DLC: Sanctuary Guardian / Artorias / Kalameet / Manus (17 = Manus global) | m12_01 |

### 1.2 Mandatory sequence (start → Gwyn)

```
[Asylum m18_01]
 1. Cell (Oscar drops Dungeon Cell Key 2010 from the ceiling hole: lot 1810000, flag 51810000) → escape cell.
 2. First Asylum Demon encounter (run away) → Asylum courtyard bonfire.
 3. Up to Oscar: Estus Flask (lot 1082 = 5x Estus drained, flag F11810590) + Undead Asylum F2 East Key (lot 1080, F11810591).
 4. Kill Asylum Demon (F16) → Big Pilgrim's Key (lot 2660) → big door.
 5. Walk onto the cliff region 1812000 → cutscenes 180125 + 100225, crow carries you → Firelink (F11810000,
    respawn = Firelink bonfire 1022960).            [E11810000]
[Firelink m10_02]
 6. Firelink Shrine (F200). Crestfallen Warrior hints at two Bells of Awakening.
[Undead Burg/Parish m10_01]  (via the aqueduct up from Firelink)
 7. Undead Burg → Taurus Demon on the bridge (F11010901) → Hellkite drake bridge → Undead Parish.
 8. Bell Gargoyles on the church roof (F3) → bell tower → ring Bell #1 (F11010700, cutscene 100100; Oswald appears).
[Lower Burg → Depths m10_00 → Blighttown m14_00]
 9. Lower Undead Burg (Watchtower Basement Key 2019 or Residence Key 2021 shortcuts optional) → Capra Demon (F11010902)
    → Key to Depths (lot 2510).
10. The Depths → Gaping Dragon (F2) → Blighttown Key (lot 2500).
11. Blighttown (from the Depths door with Blighttown Key, or via Valley of Drakes / New Londo with Master Key) →
    Quelaag (F9) → ring Bell #2 (F11400200, cutscenes 140010 [+140015]); second bell sets F11500200.
    Frampt wakes at Firelink after the first bell (state 1641) and is fully awake after both (1642).
[Sen's Fortress m15_00]
12. Sen's gate (Undead Parish, by the Hellkite bridge) is open only with F11500200.
13. Sen's Fortress → Iron Golem (F11) → examine the ring of light (region 1502505, msg 10010120) → batwing demons
    carry you to Anor Londo (cutscene 150000, warp point 1502501).  [m15_00 E11500210]
[Anor Londo m15_01]
14. Anor Londo → Silver Knights / painting room → Ornstein & Smough (F12).
15. Gwynevere's chamber → talk → **Lordvessel** (lot 1090, F11510592 → common E911) → F710 bonfire warping.
    (If Gwynevere is killed instead: AL darkens and the Lordvessel is awarded anyway — E11510400 awards lot 1090 when
    the player lacks goods 2510.)
16. Place the Lordvessel at Firelink Altar (m18_00 obj 1801960, msg 10010105 "Place Lordvessel"; cutscene 180015;
    F11800100). Reach the altar via Frampt (Firelink, talk → F820 → cutscenes 100240+180040, E11026200) or by dropping
    into the Firelink hole region 1022111 with the Lordvessel (cutscene 180060, E11020351; without it you die).
    Alternative (Darkwraith route): Kaathe (Abyss, after Four Kings, Lordvessel NOT yet placed) — see §3 Kaathe.
[Four Lord Souls — any order; each is a separate branch]
17a. Nito (Tomb of the Giants)          → Lord Soul (goods 2500, lot 2560)          ← needs Pinwheel/Catacombs path + light
17b. Bed of Chaos (Lost Izalith)        → Lord Soul (goods 2501, lot 2580)          ← via Blighttown → Demon Ruins
17c. Four Kings (The Abyss)             → Bequeathed Lord Soul Shard (2502, lot 2630) ← needs New Londo drained
                                          (Key to the Seal from Ingward, F11600100) + Covenant of Artorias (Sif)
17d. Seath (Crystal Cave)               → Bequeathed Lord Soul Shard (2503, lot 2640) ← Duke's Archives (needs Lordvessel:
                                          Archives elevator in AL is Lordvessel-gated, unverified) ; scripted capture first
18. Offer souls at the Firelink Altar bonfire (talk 180100 sets F756 → E11800200 removes the souls, sets 11800201-204,
    SFX 180005-180008 on the vessel). Only the altar bonfire ESD (180100) sets F756.
19. All four → F11800211, cutscene 180010, Kiln door obj 1801101 opens.            [E11800210]
[Kiln m18_00]
20. Kiln of the First Flame (Black Knights 1800200-204 etc. are force-killed after F15) → Gwyn (F15) → bonfire 1801950.
21. Ending: action "Link the Fire" (msg 10010108) at 1801950 → cutscene 180000, F20, NG+ (E20);
    or walk out of region 1802990 → cutscene 180001, F21, NG+ (E21).
```

### 1.3 Dependency graph (text)

```
Asylum ─16→ Firelink ─┬─ Burg: Taurus(11010901) → Gargoyles(3) → BELL1(11010700) ─┐
                      │                                                             ├→ 11500200 (Sen's gate) → Sen's → Iron Golem(11)
                      └─ Lower Burg: Capra(11010902) → [Key to Depths] → Depths:     │      → Anor Londo → O&S(12) → Gwynevere/Lordvessel(710)
                             Gaping Dragon(2) → [Blighttown Key] → Blighttown →     │             │
                             Quelaag(9) → BELL2(11400200) ─────────────────────────┘             │ (or kill Gwynevere → 11510400 + Lordvessel)
                                                                                                   ▼
                     Firelink Altar: place Lordvessel (11800100) ◄── Frampt (1643, F820) / Firelink hole + 710
                                                                                                   │
            ┌──────────────────────────────┬────────────────────────────┬─────────────────────────┴─────────┐
   Catacombs: Pinwheel(6)       Blighttown/Demon Ruins:              Darkroot: Sif(5) →            Anor Londo → Archives elevator
   [Rite of Kindling]            Ceaseless(11410900) →               [Covenant of Artorias]        (Lordvessel) → Archives:
   → Tomb of Giants (dark;       Firesage(11410410) →                + Ingward [Key to the Seal    captured by Seath (1700700) →
     Skull/Sunlight light) →     Centipede(11410901)                 F11600592, needs 710]         Archive prison → Crystal Cave
     Nito(7) [Lord Soul]         [Orange Charred Ring] →             → drain New Londo (11600100)  → Seath(14) [Shard]
                                 Lost Izalith → Bed of Chaos(10)     → Abyss: Four Kings(13)
                                 [Lord Soul]                          [Shard] (Kaathe appears if 11800100 OFF)
            └──────────────────────────────┴────────────────────────────┴───────────────────────────────────┘
                                                   ▼ 11800201..204 → 11800211 (Kiln opens) → Gwyn(15) → ending 20 / 21
```

### 1.4 Required keys and items on the critical path

| Item (goods id) | Source (lot / flag) | Opens / used for |
|---|---|---|
| Dungeon Cell Key 2010 | Asylum, dropped by Oscar into the cell (lot 1810000, F51810000) | Asylum start cell |
| Undead Asylum F2 East Key 2012 | Oscar talk (lot 1080 via F11810591; Oscar's NPC drop lot 6020 is the same key) | Asylum F2 east door |
| Big Pilgrim's Key 2011 | Asylum Demon (lot 2660; Oscar lot 6021 alt) | Asylum exit door |
| Estus Flask 200/201 | Oscar talk (lot 1082 = 5x drained, F11810590) | healing (mandatory in practice) |
| Key to Depths 2014 | Capra Demon (lot 2510) | Lower Burg → Depths door |
| Blighttown Key 2007 | Gaping Dragon (lot 2500) | Depths → Blighttown door |
| (both bells) | — | F11500200 Sen's gate |
| Lordvessel 2510 | Gwynevere talk (lot 1090) or her death (E11510400) | warping (F710), altar (F11800100), Archives access, Ingward's key |
| Key to the Seal 2013 | Ingward talk with Lordvessel (lot 1100 via F11600592), or Ingward's corpse (NpcParam drop / lot 6180), or the cut New Londo boss event E11600900 | New Londo seal lever (drain) |
| Covenant of Artorias 2002 (ring) | Sif (lot 2540) | survive the Abyss (else msg 10010650 "taken by the abyss", F8120 death) |
| Lord Souls 2500/2501, Shards 2502/2503 | Nito 2560 / Bed of Chaos 2580 / Four Kings 2630 / Seath 2640 | Lordvessel offering |
| Peculiar Doll 384 (optional) | Asylum revisit (lot 1810080) | Painted World (AL painting, E11510230 msg 10010170 without it) |
| Broken Pendant 2520 (DLC) | Crystal Golem e1700510 in the Duke's Archives, only if DLC owned + Dusk freed (F11200530) and Dusk's freed state cleared (F1121 OFF): lot 27100200 (F51700930) via m17 E11700700. ItemLot label says "Darkroot" but the flag/event are Archives. | Darkroot Basin portal (DLC entry) |

Other keys (optional routes): Basement Key 2001 (Undead Parish, lot 1010140, Burg basement / Capra shortcut),
Residence Key 2021 (male Undead Merchant shop 1105, 1000s), Watchtower Basement Key 2019 (Darkroot Garden, lot 1200140;
Burg watchtower ↔ Darkroot Basin), Mystery Key 2017 (Undead Parish lot 1010000; Firelink/cell door, unverified which),
Sewer Chamber Key 2018 (Depths lot 1000240), Key to New Londo Ruins 2008 (Blighttown chest lot 1400500 — the
elevator/door down from Firelink), Cage Key 2003 (Sen's, lot 1500150; Logan's cage), Annex Key 2009 (Painted World
lot 1100140), Archive keys 2004/2005/2006/2020 (Archives lots 26900100 Serpent Soldier, 1700630, 1700590, 1700210),
Crest Key 2022 (DLC, Royal Garden chest 1210560 / mimic 27803001; Gough's tower), Lift Chamber Key 2015 (Firelink →
Undead Parish elevator, door obj 1021465 E11020120; DSR has no shop/treasure lot for it except the relief chest —
source unverified; note m10_02 event 50 sets F11020120 + F61020120 on every load, i.e. DSR treats this door as already
unlocked — verify in game), **Master Key 2100** (starting gift; also Domhnall shop row 1580 5000s, F11007020, in his extended list).

### 1.5 Optional routes and sequence breaks

| Route | What it skips / unlocks | Data |
|---|---|---|
| **Master Key** (gift) | Opens Firelink-area/Valley of Drakes doors: Firelink → New Londo → Valley of Drakes → Blighttown (bottom) without Capra/Gaping; also Undead Burg/Parish doors. Firelink lift door 1021465 also accepts it (msg 10010883). | E11020120 params (10010874 Lift Chamber Key / 10010883 Master Key) |
| **Valley of Drakes backdoor** | Key to New Londo Ruins (Blighttown chest) or Master Key → New Londo elevator → Valley of Drakes → Blighttown swamp / Darkroot Basin back door. | per-map |
| **Darkroot backdoor** | Watchtower Basement Key (Darkroot Garden lot 1200140) or the Crest of Artorias door (Andre shop 1401, 20000s, F11017140) to Sif. | |
| **Lower Burg first** | Residence Key (merchant) / Basement Key shortcut skips the Taurus/Gargoyle order; Capra before Gargoyles is legal. | |
| **Gargoyles/bells either order** | Whichever bell is rung second sets F11500200 and plays the gate cutscene 100105 / 140015. | E11010700, E11400200 |
| **Kill Gwynevere** | Lordvessel still awarded; AL darkens (§4); Gwyndolin boss accessible (state 1241); sin/covenant penalties. | E11510400 |
| **Darkwraith path** | Kaathe (Abyss) only if Four Kings dead and Lordvessel NOT placed (E11600537: 1670 & !11800100 & 13). He then transports you to the Firelink Altar instead of Frampt. | §3 Kaathe |
| **Seath capture** | First entry into the Archives tower region 1702890 spawns immortal Seath e1700700; dying there respawns you in the Archive prison (respawn point 1702900, E11705100). | §4 |
| **Painted World** | Peculiar Doll + Anor Londo painting (E11510230, cutscene 150135, respawn 1102511); exit via Priscilla's death or the fall (E11100710 cutscene 110035 back to AL). | |
| **DLC** | Broken Pendant + Dusk freed + Manus alive → Darkroot Basin portal region 1202008 (only in m12_00_00_01), action "Examine" (10010100), singleplayer → cutscene 120002, warp to m12_01 point 1212510 (E11200006). | §3 Dusk |

## 2. Bosses in order (with souls, drops, flags, world changes)

Boss soul reward = GameAreaParam row keyed by the boss entity (`bonusSoul_single`; multi is identical in DSR). "Lots" =
common E970 (`flag → ItemLot X4, X8, X12` awarded to everyone in the session) or map events; NpcParam drop lots listed
where present. Lot 9000 = 1 Humanity, 9010 = 4 Humanity, 9020 = Twin Humanities, 9030 = Homeward Bone, 9040 = 10 Humanity.

| # | Boss | Map / model / entity | Defeat flag (event) | Souls | Drops / rewards | Cut-tail lot | World change after kill |
|---|---|---|---|---|---|---|---|
| 1 | Asylum Demon | m18_01 c2232 1810800 | 16 (E11810001) | 2000 | Big Pilgrim's Key (lot 2660) + Humanity (E970 slot `16 → [2660, 9000]`) | — | Asylum doors 1811111/1811115 open anim; exit door ObjAct off |
| 1b | (Demon's Great Hammer from the first encounter if you damage it) | | | | lot 2661 | | |
| 2 | Taurus Demon | m10_01 c2250 1010700 | 11010901 | 3000 | Humanity + Homeward Bone (E970 11010901: 9000, 9030); NpcParam 22500000 = Demon's Greataxe 5% | — | fogs 1011890/1011892 off |
| 3 | Bell Gargoyles (2; 3rd perch 1010802 anim 7000) | m10_01 c5350 1010800/801 | 3 (E11010001) | 10000 | Twin Humanities (9020) | Gargoyle Tail Axe 53520000 | fogs 1011990/992 off; bell reachable; Lautrec escapes cell if not freed (§3) |
| 4 | Capra Demon (+2 dogs 1010500/501, 1010510/511) | m10_01 c2240 1010750 | 11010902 | 6000 | Key to Depths 2510 + Humanity + Homeward Bone; NpcParam 22400000 Demon Great Machete 5% | — | fog 1011790 off |
| 5 | Gaping Dragon | m10_00 c5260 1000800 (+tail part 1000801) | 2 (E11000001; intro cutscene 100000, E11005392) | 25000 | Blighttown Key 2500 + Twin Humanities + Homeward Bone | Dragon King Greataxe 52610000 | fog 1001990 off |
| 6 | Chaos Witch Quelaag | m14_00 c5280 1400800 | 9 (E11400001; intro 140000) | 20000 | Soul of Quelaag (lot 2570) + Twin Humanities | — | fogs off; bell 2 tower reachable |
| 7 | Moonlight Butterfly (opt.) | m12_00 c3230 1200801 | 11200900 | 10000 | Soul of the Moonlight Butterfly (2530) + Humanity | — | fogs 1201890/892 off |
| 8 | Iron Golem | m15_00 c2320 1500800 | 11 (E11500001) | 40000 | Core of an Iron Golem (2590) + Humanity | — | Golem armor in Domhnall's shop (F11807020-050 OFF); ring of light to AL (E11500210) |
| 9 | Ornstein & Smough | m15_01 c5270 1510800 / c5271 super 1510801; c2360 1510810 / super 1510811 (intro 150140; phase-2 cutscenes 150120/150121) | 12 (+11510902 Ornstein last / 11510903 Smough last) | 50000 (each GameArea row) | Ornstein last: Soul of Ornstein (2610), Leo Ring (NpcParam 52710000) ; Smough last: Soul of Smough (2620) ; Humanity | — | bonfire 1511950 "Chamber of the Princess"; Ornstein or Smough armor in Domhnall shop (F11807100-130 / 11807060-090 OFF) |
| 10 | Crossbreed Priscilla (opt.) | m11_00 c2730 1100160 (non-hostile until hit: state 1690 → 1691) | 4 (E11100000) | 30000 | Soul of Priscilla (2520) + Twin Humanities | Priscilla's Dagger 27310000 | exit; achievement 40 |
| 11 | Sif | m12_00 c5210 1200800 (intro 120000; post-DLC intro 120003 if F11210021) | 5 (E11200001) | 40000 | Covenant of Artorias (2540) + Soul of Sif (2541) + Humanity + Homeward Bone | — | fog 1201990 off |
| 12 | Pinwheel | m13_00 c3320 1300800 (+clones 1300801-814; intro 130000 sets 11300005) | 6 (E11300001) | 15000 | Rite of Kindling (2550) + Humanity + Homeward Bone; NpcParam 33200000 = one of the 3 masks | — | ladder 1301143; Rhea's party leaves Firelink (11300005), Patches moves to Tomb (§3) |
| 13 | Gravelord Nito | m13_01 c5220 1310800 (+6 skeletons 1310120-125 waking 9061; intro 130100) | 7 (E11310001) | 60000 | Lord Soul (2560) + Humanity | — | bonfire 1311950 "Altar of the Gravelord"; achievement 35 |
| 14 | Ceaseless Discharge | m14_01 c5250 1410600 | 11410900 (sets 11410800, 402) | 20000 | Humanity + Homeward Bone (E970) | — | bonfire 1411962 registered (11410976); fog 1411790 off |
| 15 | Demon Firesage | m14_01 c2231 1410400 | 11410410 | 20000 | Humanity; NpcParam 22310000 Demon's Catalyst | — | fogs 1411410/1411412 off |
| 16 | Centipede Demon | m14_01 c5200 1410700 (intro 140120) | 11410901 | 40000 | Orange Charred Ring (lot 2670 via map event) + Humanity + Homeward Bone | (arm/tail parts drop) | Solaire goes to Demon Ruins (1009) |
| 17 | Bed of Chaos | m14_01 c5400 1410800 (core c5401 1410802, bug c5230 1410801; intro 140115-140118) | 10 (E11410001) | 60000 | Lord Soul (2580) + Humanity | — | bonfire 1411950; achievement 36; Quelana's Fire Tempest |
| 18 | Four Kings | m16_00 c5390 1600800-804 (generator 1603000) | 13 (E11600001, cutscene 160010) | 60000 | Bequeathed Lord Soul Shard (2630) + 4 Humanity | — | bonfire 1601950 "The Abyss"; Kaathe (1671) if Lordvessel not placed; achievement 37 |
| 19 | Seath the Scaleless | m17_00 c5290 1700800 (intro 170000; Archives immortal copy 1700700) | 14 (E11700001) | 60000 | Bequeathed Lord Soul Shard (2640) + Humanity | Moonlight Greatsword 52910000 | bonfire 1701950 "Crystal Cave"; achievement 38; Logan's endgame (§3) |
| 20 | Dark Sun Gwyndolin (opt.) | m15_01 c5320 1510650 (Darkmoon Tomb; dialog phases 150152/150157) | 11510900 | 40000 | Soul of Gwyndolin (2600) | — | Moonlight set + Crown of the Dark Sun in Domhnall's shop |
| 21 | Gwyn, Lord of Cinder | m18_00 c5370 1800800 | 15 (E11800001) | 70000 | Soul of Gwyn (2650) | — | Great Lord set in Domhnall's shop; ending bonfire 1801950 |
| 22 | Stray Demon (opt.) | m18_01 c2230 1810810 | 11810900 | 20000 | Humanity + Homeward Bone; NpcParam 22300000 Titanite Slab | — | ladder 1811141 |
| DLC1 | Sanctuary Guardian | m12_01 c3471 1210800 | 11210000 | 30000 | Guardian Soul (2680) + Humanity | Guardian Tail (unverified) | |
| DLC2 | Knight Artorias | m12_01 c4100 1210820 (intro 120110) | 11210001 (+F121) | 50000 | Soul of Artorias (2690) | — | Artorias set in Domhnall shop (F11217060-090 OFF); Ciaran appears if you hold the soul |
| DLC3 | Black Dragon Kalameet | m12_01 c4510 1210401 (grounded by Gough, cutscene 120100) | 11210004 (+11210005, F121) | 60000 | Calamity Ring (2710) | Obsidian Greatsword 45110000 | Gough's Greatbow |
| DLC4 | Manus | m12_01 c4500 1210840 | 11210002 + 17 | 60000 | Soul of Manus (2700) + 10 Humanity | — | bonfire 1211950 "Chasm of the Abyss"; Dusk/Elizabeth endings |
| cut | unnamed New Londo boss | m16_00 e1600810 (no MSB part; fog 1602898; NameID 2390 empty) | 11600900 | — | awards lot 1100 Key to the Seal if not owned | — | leftover, do not port |

(Row 1 drop column, precise: Asylum Demon kill gives lot 2660 "Big Pilgrim's Key" + 9000 via E970 slot `16 → [2660, 9000, 0]`;
Stray Demon slot `11810900 → [0, 9000, 9030]`.)

Boss-room conventions used everywhere (port once, reuse): fog gate pair `obj 1xx1990 + sfx 1xx1991` (second gate
`1xx1992/1993`), entry event `IF Action Button (Line Segment) - Boss Room` with help msg 10010403 "Traverse the white light",
player anim 7410 (walk through fog), "Issue Boss Room Entry Notification", BGM region `1xx2990`, boss health bar NameID =
NPC_name FMG id (= model number), death SE 777777777, `Handle Boss Defeat` → GameAreaParam souls.

## 3. NPC questlines (end to end, across maps)

### 3.0 How NPC logic is built in DS1 (the port must reproduce these templates once)

- **One flag range per NPC** (table 3.1). `common.emevd` E50 sets each range's default flag on a new game if the whole
  range is OFF. Every map that hosts the NPC re-reads the range in its own **event 50**: `ChrEnable(OFF)` unless the
  state flag(s) for *this* map are ON, `Register NPC Humanity Event Value(entity, 8xxx)` (per-NPC humanity counter),
  then `Initialize` the template events below.
- Template events (same body in every map, ids `11AB05xx`):
  - `11AB0510(entity, hostileFlag)`: HP ≤ 90 % and damaged by the player → set hostile flag, team 9 "Hostile Ally",
    save. If the flag was already ON at load the NPC is disabled ("gone after hostility").
  - `11AB0520(entity, rangeStart, rangeEnd, deadFlag)`: HP = 0 → state = dead flag; on reload `Force Character Treasure`
    (corpse loot = MSB treasure attached to the part + NpcParam itemLotId_1).
  - Other `11AB05xx`: "IF state X & trigger (talk flag / boss flag / area / player in-out of map) → state Y,
    enable/disable, warp, set waiting anim". Dialogue triggers are `1:11(flag,1)` in the talk ESD.
- **Global hostility aggregate**: common E772 sets **F744** while any NPC hostile flag is ON (1004, 1033, 1096, 1114,
  1176, 1179, 1195, 1197, 1213, 1223, 1241, 1253, 1282, 1283, 1287, 1294, 1314, 1321, 1341, 1361, 1381, 1401, 1411, 1421,
  1434, 1461, 1512, 1547, 1574, 1603, 1627, 1646, 1675, 1691, 1711, 1712, 71200035, 71200042, 1763, 1822, 1841, 1863, 1871).
- **Absolution** (Oswald): his ESD sets **F755**; common E770 then clears 742-746, every per-map "hostile on load" block
  (flags 110x0501-0519 etc.), every hostile flag above (and sets 1710 back ON for Alvina), F11200596, 71200035, 71200042,
  and sets team Ally on e6001/6040/6072/6190/6230/6300. m10_01 E11010583 warps the Burg NPCs back to their homes with
  idle anims (Solaire 7845, Rhea 7880, Andre 9000, merchant 9000, Lautrec 7825) when F744 goes ON→OFF.
- **Covenant-join kneel**: the talk ESD sets 840-849; common E840 rotates the player to the NPC and plays the join anim
  (generic 7905 kneel; altar-type 7898 → loop 7896; coffin/dragon 7913 → loop 7911). Flag → NPC: 840 Oswald (absolution
  kneel), 841 Rhea, 842 Petrus, 843 Solaire (Burg), 844 Sunlight Altar / Gwynevere / Darkmoon altar, 845/846 Kaathe
  (Abyss / Firelink Altar), 847 Nito's coffin / Everlasting Dragon, 848 Alvina, 849 Fair Lady. Current covenant N is
  mirrored into F850+N by common E870.
- **Item gifts from dialogue**: talk ESD sets a flag; common E911/E890 awards the ItemLot and resets the flag (table 3.2).
- **Idle poses**: MSB InitAnimID per part (listed per NPC); events override with `Set Special Animation(WaitingAnimation)`.
  c0000 idle ids seen: 7800 (Siegmeyer at Sen's gate), 7805 (Oscar dying), 7810 (Crestfallen Warrior sitting),
  7820 (Laurentius in the barrel; 7821 climb-out), 7825 (Lautrec sitting; Solaire at the Lost Izalith bonfire),
  7830 (Logan / Rickert seated), 7835 (Domhnall, Laurentius at Firelink, Solaire in AL), 7840 (Patches),
  7845 (Solaire sun-gazing), 7850 (Crestfallen Merchant; Siegmeyer in AL), 7855 (Siegmeyer asleep; 7856 waking),
  7860 (Sieglinde mourning), 7865 (Oswald), 7870 (Darkmoon Knightess; Shiva in Blighttown), 7875/7876/7877 (Dusk seated /
  standing up / vanishing), 7880 (Rhea praying; Ciaran kneeling), 7885 (Rhea cowering in the Tomb; Quelana),
  7900 (hollowed Siegmeyer, Ash Lake), 7915/7916/7917 (Dusk in the Chasm: seated / flinch / vanish).
  Non-human NPC idles: 9000 (Andre, merchants, Gough, Alvina, Nito's coffin; Frampt dormant), 9001 (Chester, Eingyi;
  Frampt napping), 9002 (Giant Blacksmith), 9003 (Vamos), 7003 (Frampt/Kaathe raised), 7007 (Frampt at the altar),
  9060 (Kaathe appears), 7005 / 7009 (Frampt/Kaathe sink away), 9061 (generic wake-up / stand). Meanings are inferred
  from context — verify in TAE. No NPC part has MSB patrol points (MovePointNames all empty).

### 3.1 NPC flag-range table

| NPC | Flag range (default) | Hostile / dead | Entities (map: entity [NpcParam, talk, InitAnim]) |
|---|---|---|---|
| Knight Solaire | 1000-1029 (1000) | 1004 / 1005; 1011 insane killed | m10_01 e6001 [6000, 101600, 7845]; m15_01 e6003 [6002, 151600, 7835]; m14_01 e6002 [6003, 141600, 7825], insane e6004 [6004, 141658]; summons e6540 (m10_01), e6541 (m10_00), e6542 (m14_01), e6543 (m15_01), e6544 (m18_00) |
| Darkmoon Knightess ("Lady of the Darkling") | 1030-1036 (1030) | 1033 / 1034 (+F151) | m15_01 e6010 [6010, 151601, 7870] |
| Oscar of Astora | 1060-1074 (1060) | — / 1073 (dies after talk); 1062 hollow killed | m18_01 e6023 [6020, 181602, 7805]; hollow e6024 [6021] |
| Big Hat Logan | 1090-1109 (1090) | 1096 / 1097 | m15_00 e6030 [6030, 150603]; m10_02 e6031 [6030, 102603, 7830]; m17 e6032 [6031, 170603, 7830], hollow e6033 [6032] |
| Griggs of Vinheim | 1110-1119 (1110) | 1114 / 1115 | m10_01 e6040 [6040, 101604]; m10_02 e6041 [6040, 102604]; m15_00 hollow e6043 [6041] |
| Dusk of Oolacile | 1120-1139 (1120) | — / 1125 (killed in the Chasm) | m12_00 e6050 [6050, 120605], e6051 [6050, 120663]; m12_01 e6700 [6700, 121670, 7915] |
| Anastacia (Firekeeper) | 1140-1169 (1140) | — | m10_02 e6060 c2750 [275000; talk via c1000 dummy 102606] |
| Rhea of Thorolund | 1170-1189 (1170) | 1176, 1179 / 1177 (Petrus kills), 1180 | m10_02 e6070 [6070, 102607, 7880]; m13_01 e6071 [6070, 131607, 7885]; m10_01 e6072 [6070, 101607, 7880]; m17 hollow e6073 [6070] |
| Petrus of Thorolund | 1190-1209 (**1202**) | 1195, 1197 / 1196, 1198 | m10_02 e6080 [6080, 102608] |
| Nico / Vince of Thorolund | 1210-1219 / 1220-1229 (1210/1220) | 1213 / 1223; dead 1214 / 1224 | m10_02 e6090 / e6100 [6090/6100, 102609/102610]; m13_01 hollow e6091 / e6101 [6091/6101] |
| Gwynevere | 1230-1239 (1230) | — / 1232 | m15_01 e1510600 c5310 [531000, 151611] |
| Dark Sun Gwyndolin | 1240-1249 (1240) | 1241 (boss) / 1242 | m15_01 e1510650 c5320 [532000] |
| Laurentius | 1250-1259 (1250) | 1253 / 1254 (1255 in Depths) | m10_00 e6130 [6130, 100613, 7820]; m10_02 e6131 [6130, 102613, 7835]; m14_00 hollow e6132 [6131] |
| The Fair Lady | 1270-1279 (1270) | 1272 | m14_00 e1400700 c5340 [534000, 140615] |
| Eingyi | 1280-1289 (1280) | 1282, 1283, 1287 / 1284 | m14_00 e6160 c3210 [321001, 140616, 9001] |
| Quelana of Izalith | 1290-1309 (1290) | 1294 / 1295 | m14_00 e6170 [6170, 140617, 7885] |
| Ingward | 1310-1319 (1310) | 1314 / 1315 | m16 e6180 [6180, 160618]; m10_02 e6181 [6180, 102618] |
| Andre of Astora | 1320-1339 (1320) | 1321 / 1322 | m10_01 e6190 c2640 [264000, 101619, 9000] |
| Vamos | 1340-1343 (1340) | 1341 / 1342 | m13_00 e6200 c2920 [292000, 130620, 9003] |
| Giant Blacksmith | 1360-1363 (1360) | 1361 / 1362 | m15_01 e6210 c2860 [286001, 151621, 9002] |
| Blacksmith Rickert of Vinheim | 1380-1399 (1380) | 1381 / 1382 | m16 e6220 [6220, 160622, 7830] |
| Undead Merchant (male) | 1400-1409 (1400) | 1401 / 1402 | m10_01 e6230 c2510 [251001, 101623, 9000] |
| Undead Merchant (female) | 1410-1413 (1410) | 1411 / 1412 | m10_02 e6240 c2510 [251000, 102624] |
| Crestfallen Merchant | 1420-1429 (1420) | 1421 / 1422 | m15_00 e6250 [6250, 150625, 7850] |
| Domhnall of Zena | 1430-1459 (1430) | 1434 / 1435 | m10_00 e6260 [6260, 100626, 7835]; m10_02 e6261 [6260, 102626, 7835] |
| Crestfallen Warrior | 1460-1489 (1460) | 1461 / 1462 | m10_02 e6270 [6270, 102627, 7810]; m16 hollow e6271 [6271] |
| Siegmeyer of Catarina | 1490-1539 (1490) | 1512 / 1513 | m15_00 e6280 [6280, 150628, 7800]; m15_01 e6283 [6281, 151628, 7850]; m10_02 e6287 [6282, 102628]; m14_00 e6282 [6283, 140628, 7855]; m14_01 e6286 [6284, 141628, 7855]; m13_02 e6288 [6280, —, 7900] |
| Sieglinde of Catarina | 1540-1569 (1540) | 1547 / 1548 | m17 e6291 [6290, 170629]; m10_02 e6292 [6290, 102629]; m13_02 e6290 [6291, 132629, 7860] |
| Knight Lautrec of Carim | 1570-1599 (1570) | 1574 / 1575; 1578 killed in AL | m10_01 e6300 [6300, 101630, 7825]; m10_02 e6301 [6300, 102630, 7825]; m15_01 e6302 [6301, 151630] + bodyguards e6490/e6500; summons e6590 (m10_01), e6591 (m10_00) |
| Shiva of the East (+ bodyguard) | 1600-1619 (1600); bodyguard 1760-1769 | 1603 / 1604; bodyguard 1763 / 1764 | m12_00 e6310 [6310, 120631], e6420 [6420, 120642]; m14_00 e6311 [6310, 140631, 7870], e6421; m11 e6312 / e6422 |
| Patches | 1620-1639 (1620) | 1627 / 1628 | m13_00 e6320 [6320, 130632]; m13_01 e6321 [6321, 131632]; m10_02 e6322 [6321, 102632, 7840] |
| Kingseeker Frampt | 1640-1669 (1640) | 1646 / 1647 (gone) | m10_02 e6330 c5330 [533000, 102633, 7003]; m18_00 e6331 [533000, 180633, 7007] |
| Darkstalker Kaathe | 1670-1689 (1670) | 1675 / 1676, 1677 (gone) | m16 e6340 c5330 [533001, 160634, 7003]; m18_00 e6341 [533001, 180634, 7003] |
| Priscilla | 1690-1699 (1690) | 1691 (fight) / 1692 | m11 e1100160 c2730 [273000, 110636] |
| Oswald of Carim | 1700-1709 (1700) | 1701 / 1702 | m10_01 e6370 [6370, 101637, 7865] |
| Alvina | 1710-1712 (1710) | 1711, 1712 | m12_00 e6380 c5361 [536100, 120638, 9000] |
| Hawkeye Gough | 1820-1839 (1820) | 1822 / 1823 | m12_01 e6720 c4110 [411000, 121672, 9000] |
| Marvellous Chester | 1840-1859 (1840) | 1841 / 1842 | m12_01 e6730 c4090 [409000, 121673, 9001]; invader copies e6731/e6732 [409010] |
| Lord's Blade Ciaran | 1860-1869 (1860) | 1863 / 1864 | m12_01 e6740 [6740, 121674, 7880] |
| Elizabeth | 1870-1889 (1870) | 1871 / 1872 (gone) | m12_01 e6750 c4140 [414000, 121675] |

Unassigned default ranges in common E50: 1770-1779 and 1780-1789 (no map uses them).

### 3.2 Dialogue → item gifts (common E911 / E890 / E910; flag set by talk ESD → ItemLot)

| Trigger flag (talk) | Lot → item |
|---|---|
| 11810590 / 11810591 (Oscar 181602) | 1082 Estus Flask ×5 (drained) / 1080 Undead Asylum F2 East Key |
| 11010591 (Solaire 101600) | 1000 White Sign Soapstone |
| 11010594 (bonfire ESDs, Sunlight rank 0) / 11010595 (Sunlight Altar 101648, rank 1) / 11010598 (altar takes Soul of Gwyn 702) | 1120 Lightning Spear / 1130 Great Lightning Spear / 1410 Sunlight Spear |
| 11020599 (Petrus 102608) | 1140 Copper Coin |
| 11020602 (Laurentius 102613) | 1110 Pyromancy Flame |
| 11020607 (Lautrec 102630) | 1150 Sunlight Medal |
| 11020594 / 11020595 (Siegmeyer 102628) | 1060 Emit Force / 1070 Speckled Stoneplate Ring |
| 11510590 (Siegmeyer AL 151628) | 1010 Tiny Being's Ring |
| 11400590 (Siegmeyer Blighttown 140628) | 1040 Pierce Shield |
| 11410594 (Siegmeyer Izalith 141628) | 1050 Speckled Stoneplate Ring |
| 11320590 (Sieglinde Ash Lake 132629) | 1250 Titanite Slab |
| 11510592 (Gwynevere 151611) | 1090 Lordvessel |
| 11510595 (bonfire ESDs, Princess's Guard) | 1350 Ring of the Sun Princess |
| 11510596 / 11510597 (Darkmoon altar 151612) | 1360 Blue Eye Orb + 1361 Darkmoon Blade Covenant Ring / 1370 Darkmoon Blade + 1371 Darkmoon Talisman |
| 11600592 (Ingward 160618) | 1100 Key to the Seal |
| 11600594 / 11600595 / 11600596 / 11600580-583 (Kaathe 160634/180634) | 1380 Dark Hand / 1390 Red Eye Orb / 1400 Darksword / 1401-1404 Dark Mask, Armor, Gauntlets, Leggings |
| 11300590 / 11300591 (Patches Catacombs 130632) | 1190 Soul of a Lost Undead / 1200 Humanity |
| 11310594 (Patches Tomb 131632) | 1240 Twin Humanities |
| 11310590 (Rhea Tomb 131607) | 1210 Replenishment |
| 11310592 / 11310593 (Nito coffin 131646) | 1220 Gravelord Sword + 1221 Gravelord Sword Dance / 1230 Gravelord Greatsword Dance |
| 11320581 / 11320593 (Everlasting Dragon 132647) | 1260 Dragon Eye + 1261 Dragon Head Stone / 1270 Dragon Torso Stone |
| 11400591 (Eingyi 140616, repeatable E910) / 11400592 | 1280 Egg Vermifuge / 1290 Pyromancy Flame |
| 11400594 (Quelana 140617) | 1300 Fire Tempest |
| 11400596 / 11400597 / 11400598 (Fair Lady 140615) | 1310 Great Chaos Fireball / 1320 Chaos Fire Whip / 1330 Chaos Storm |
| 11400599 | 1340 Soul of a Hero (no setter found) |
| 11200592 / 11200593 / 11200594 (Alvina 120638 / bonfires) | 1160 Cat Covenant Ring / 1170 Divine Blessing / 1180 Ring of Fog |
| 11210590 (Ciaran) / 11210593 (Gough) / 11210594 (Elizabeth) | 1500 Dark Silver Tracer + 1501 Gold Tracer / 1510 Gough's Greatbow / 1520 Elizabeth's Mushroom ×3 |
| 11000591 (unplaced t100628) / 11700591 (unplaced t170628) | 1030 Purging Stone / 1020 Pierce Shield (cut) |
| 11020905, 11510905, 11010905, 11600905, 11320905, 11300906, 11200905, 11510906, 11400905 | 1800-1880 = hidden DSR goods 131-139 (unnamed; no setter found; DSR feature, unverified) |

NG+ duplicate suppression: common E8200/E8300/E8090 (cycle ≥ 1 and the player already owns the reward → mark the reward's
lot flag as obtained). Rank-0 covenant rewards are granted by the shared bonfire ESDs (t1xx000-t1xx010 all set
11010594/11200592/11310592/11320581/11400596/11510595/11510596/11600594).

### 3.3 Per-NPC questlines

#### Oscar, Knight of Astora (m18_01)
1. Asylum F2 balcony, dying (InitAnim 7805). Talk (ESD 181602) → Estus (F11810590 → lot 1082) and the F2 East Key
   (F11810591 → lot 1080); F11810592 marks the last line.
2. Once any of those flags is ON and the player is in region 1812121 or 1812990 → E11810530 kills him (souls to the
   player) → dead state 1073 (E11810520).
3. Revisit (F11810000 & 1073): hollow Oscar e6024 enabled, team Hostile Ally (E11810531 → 1061); killed (E11810532) →
   1062, lot 7020 Crest Shield (+ MSB treasure). Killed during step 1 instead: lot 6020 F2 East Key.

#### Knight Solaire of Astora
1. Undead Burg (m10_01 e6001, sun-gazing 7845) state 1000. Talk → White Sign Soapstone (F11010591) → 1001.
   His Anor Londo line sets F11010590.
2. Summon for the Bell Gargoyles (e6540 at region 1012000) while none of 1004/1005/1006/1010/1011 is ON
   (E11015100; DSR variant E11015900 additionally needs player SpEffect 28). DSR data also places a Solaire sign in the
   Depths at the Gaping Dragon fog (e6541, region 1002000, E11005030/029).
3. Anor Londo (m15_01 e6003, 7835, at the bonfire obj 1511961 before O&S): 1001 & F11010590 → 1008 (E11510530).
   Summon for O&S (e6543 at 1512001, home 1512002; his body e6003 is disabled while summoned) (E11515030/029).
4. Sunlight Altar: 1008 & F11510594 → 1007, warped to the altar point 1012530 (E11010531; at load the same warp in
   m10_01 event 50). Warrior of Sunlight join at the altar (F844, covenant 3); his own ESD also has a join branch (F843).
5. Demon Ruins (m14_01 e6002): 1007 & Centipede dead (11410901) → 1009 (E11410530). Centipede summon e6542 needs 1007.
6. Lost Izalith fork, when the player enters region 1412510:
   - Chaos Bug with the Sunlight Maggot still alive (F800 OFF) → 1002: e6002 removed, **insane Solaire e6004** enabled
     hostile (E11410531). Killed → 1011 (E11410534).
   - F800 ON (bug killed; it drops Sunlight Maggot lot 34800100 / F51410990; F800's setter is not in EMEVD/ESD) → 1003:
     warped to the Lost Izalith bonfire area 1412500, sitting 7825, F11410580 (E11410532). Talk F11410595 → 1012
     (E11410533 / m18_00 E11800550) → his Gwyn summon sign (e6544 at 1802050).
- Hostile 1004 / dead 1005; NpcParam lot 6000 Iron Helm + MSB treasure (Sun set, Sunlight Shield/Talisman: per-map file).

#### Crestfallen Warrior (m10_02 → m16)
1460 at Firelink (sitting 7810, hints about the bells). After his New Londo line (F11020597) and the player is outside
Firelink → 1464, disabled (E11020577). New Londo: hollow e6271 enabled, team Hostile Ally, while 1464 (E11600545).
Hostile 1461, dead 1462.

#### Petrus, Rhea, Vince and Nico of Thorolund
1. Firelink: Petrus e6080 is there from the start (default state **1202**). Rhea e6070 (praying 7880), Nico e6090 and
   Vince e6100 start hidden (1170/1210/1220) and **arrive when the Capra Demon dies**: E11020559 (1170 & F11010902) →
   Rhea 1171, Nico 1211, Vince 1221, all enabled.
   Petrus: shop 4000-4006 (Heal, Great Heal Excerpt, Homeward, Force, Seek Guidance, Talisman, Thorolund Talisman), Way
   of White join (F842, covenant 1), gesture Shrug (9005), Copper Coin (F11020599). Rhea's Firelink ESD (102607): shop
   4200-4209, Way of White join (F841), gesture Prayer (9014).
2. The party leaves (E11020560 / m13_01 E11310530): nobody hostile & (Petrus' line F11020600 or Pinwheel's arena entered
   F11300005) → Rhea 1173 (Tomb), Petrus 1192 (absent for one load), Nico 1216 / Vince 1226 (hollow in the Tomb),
   F11020693, event value 600 cleared. Next Firelink load: Petrus 1193, present again (E11020564, F11020692).
3. Tomb of the Giants (m13_01): Rhea e6071 cowering (7885) behind hollow Nico e6091 / Vince e6101 (team Hostile Ally).
   Kill both (1214 & 1224) → Rhea 1174 (E11310531); talk → Replenishment (F11310590), 1175, she leaves (E11310532).
4. Undead Parish (m10_01 e6072, praying 7880, next to Andre): 1174 & F11310590 → 1175 (E11010533). Talking to her sets
   F11010596 → resets the boss counter (E11010538). **Counter**: common E690 increments event value 600 each time a boss
   flag 2-15 turns ON after 1175. Counter ≥ 2 with Petrus alive and not hostile → **Petrus kills Rhea** (E11010537):
   1177, F11010599, F50006070 ON, F50006071/50006080 OFF, her corpse loot (Pendant lot 2070).
5. Whole Parish shop bought (F724 = 11017050-11017130 all ON) → the next load she is gone (1181, E11010534) and stands
   hollow and hostile in the Duke's Archives (m17 e6073, E11700545).
6. Petrus later: 1193 & Lautrec's line (F11020608) → 1194 (E11020565). Hostile 1195 / 1197; dead 1196 / 1198
   (E11020567/569; lot 6080 Ivory Talisman). Rhea killed by the player in the Parish → 1180 (E11010535; lot 6070).
7. Attacking any of the four at Firelink (E11020501) turns all four hostile (1176 / 1197 / 1213 / 1223).

#### Big Hat Logan
1. Sen's Fortress cage (m15_00 e6030) 1090. Cage opened with the Cage Key (ObjAct flag 11500112; E11500530 re-arms the
   cage ObjAct 1501032 after F11500593) → 1091 (E11500531). Talk (F11500594) → 1092: removed from Sen's (E11500532),
   seated at Firelink (E11020550, e6031, 7830). Shop 5000-5019 (Logan's list: Soul Arrow, Great Soul Arrow, Heavy and
   Great Heavy Soul Arrow, Magic Weapon/Shield; label "Griggs - Logan in DA").
2. After his "leaving" line (F11020603) with the player outside Firelink → 1093, F11020694 (E11020551).
3. Duke's Archives (m17): entering region 1702410 → e6032 enabled in a cell (ObjAct 1701506 param 27401) (E11700530);
   cell opened (F61700320) → 1094, F11700594 (E11700531). Player away from 1702410, or Seath dead → 1095, moved to the
   library point 1702501 (E11700532). Shop 5000-5099 (adds Homing Soulmass, Soul Spear, Homing Crystal Soulmass,
   Crystal Soul Spear, Crystal Magic Weapon 5020-5022).
4. Seath dead (14) & every Archives spell bought (F728 = 11707090-11707110) & his line F11700592 → 1099: e6032 removed,
   **hollow Logan e6033** hostile at the Archives top, object 1701664 removed, F11700595 (E11700533).
- Hostile 1096, dead 1097 (lot 7030 Big Hat + MSB treasure: Big Hat's set, Logan's Catalyst).

#### Griggs of Vinheim
1. Lower Undead Burg locked house (m10_01 e6040) 1110; door opened (F11010181) → 1111, special anims cleared (E11010532).
2. Firelink (player in Firelink) → 1112 (e6041) (E11020552). Shop 2000-2019 (Soul Arrow, Great Soul Arrow, Heavy and
   Great Heavy Soul Arrow, Magic Weapon/Shield, Aural Decoy, Fall Control, Bellowing/Lingering Dragoncrest Ring,
   Sorcerer's Catalyst); 2000-2099 later adds Homing Soulmass and Soul Spear.
3. Logan left (F11020694) → 1113 (E11020553).
4. Every Griggs spell bought (F723 = 11027130-11027240) & the player in Sen's Fortress → 1117: removed from Firelink
   (E11020554), **hollow Griggs e6043** hostile in Sen's (E11500533). Hostile 1114, dead 1115 (lot 6040 Hush).

#### Laurentius of the Great Swamp (NpcParam labelled "Griggs")
1. Depths (m10_00 e6130), stuck in barrel obj 1001250, gravity and map hits off, idle 7820, invincible until the barrel
   breaks (E11000534). Barrel broken or Laurentius hit → 1251, cancel anim 7821, F11000580 (E11000530).
2. Firelink (E11020574) → 1252 (e6131, seated 7835). Pyromancy Flame (F11020602). Shop 3000-3004 (Fireball, Fire Orb,
   Combustion, Iron Flesh, Flash Sweat).
3. The player owns an advanced pyromancy (F11020102 via common E720: Great Fireball, Firestorm, Fire Tempest, Fire Whip,
   Great Combustion, Great Chaos Fireball, Chaos Storm, Chaos Fire Whip) and his line F11020103 → 1256, leaves (E11020575).
4. Blighttown: hollow e6132, team Hostile Ally, 1256 → 1257 (E11400530). Hostile 1253, dead 1254 (Depths 1255).

#### Domhnall of Zena
1430 in the Depths (e6260, 7835). Both bells rung → 1431: leaves the Depths (E11000532) and sits at Firelink
(E11020412, e6261). Shop 1500-1569: Gold Pine Resin ×3, Bottomless Box, Crystal Straight Sword / Greatsword / Shield,
Helm of the Wise, Armor of the Glorious, Gauntlets of the Vanquisher, Boots of the Explorer, arrows/bolts, plus boss sets
that unlock as bosses die (common E50 pre-sets their sold-out flags ON, boss events clear them):
Golem set (Iron Golem: F11807020-050); **set of whichever of O&S dies last** (Ornstein last → Ornstein set F11807100-130;
Smough last → Smough set F11807060-090); Moonlight Robe/Gloves/Waistcloth + Crown of the Dark Sun (Gwyndolin:
F11807170/180/190/230); Great Lord set (Gwyn: F11807200/210/220/240). His extended list 1500-1599 adds Master Key
(1580, 5000s, F11007020) and the Artorias set (Artorias: F11217060-090). Gesture Joy (9004). Hostile 1434, dead 1435.

#### Undead Merchants
- Male (m10_01 e6230, sitting 9000): shop 1100-1134 (Orange Guidance Soapstone, Repair Powder, Throwing Knife, Firebomb,
  Lloyd's Talisman, Residence Key 1000s, Repairbox, Bottomless Box, basic weapons/shields/Chain set, arrows/bolts,
  Dried Finger 2000s F11017210). Hostile 1401, dead 1402 → lot 25100100 Uchigatana.
- Female (m10_02 e6240): shop 1200-1220 (Bloodred/Purple/Blooming Purple Moss, Poison Throwing Knife, Dung Pie,
  Alluring Skull, Charcoal/Rotten Pine Resin, Transient Curse ×4, Homeward Bone, Prism Stone, Humanity ×1 5000s
  F11027020, Fire/Poison Arrow, Purging Stone, arrows/bolts). When attacked she runs to home region 1022700 and turns
  Enemy (E11020504) → 1411; dead 1412.

#### Andre of Astora (m10_01 e6190 c2640, hammering 9000, Undead Parish church)
Shop 1400-1420 (Titanite Shard, Crest of Artorias 20000s F11017140, Weapon/Armor Smithbox, Repairbox, Longsword,
Broadsword, Bastard Sword, Battle Axe, Warpick, Caestus, Pike, Large Leather / Tower Kite / Caduceus Kite Shield, ammo).
Reinforce / repair menus (`1:24(0)` / `1:24(10)`). Ember unlocks (ESD `1:49` + F350 Large, F351 Very Large, F358
Divine, F359 Large Divine, F360 Dark; common E350 removes the ember item once its flag is set). Gesture Hurrah! (9002).
Hostile 1321 / dead 1322 → object 1011010 destroyed (E11015090; unverified: his anvil fire) and lot 26400000
Blacksmith Hammer.

#### Oswald of Carim (m10_01 e6370, 7865)
Appears at the Parish bell tower once Bell #1 is rung (E11010581). Absolution (F755 → common E770; kneel F840).
Shop 4400-4408 (Book of the Guilty, Purging Stone ×5, Indictment, Karmic Justice 40000s, Velka's Talisman,
Bloodbite Ring, Poisonbite Ring, Ring of Sacrifice ×10, Homeward Bone). Gesture Well! What is it! (9011).
Hostile 1701, dead 1702 (lot 6370 Book of the Guilty).

#### Knight Lautrec of Carim
1. Undead Parish cell (m10_01 e6300, 7825) 1570. Cell door opened (F11010190) while he is alive → 1571, F11010584
   (E11010550). Still caged when the Bell Gargoyles die (F3) or when both Fire Keeper Souls F812 (Blighttown lot 1400350)
   and F813 (Parish lot 1010050) are picked up → 1577, removed from the cell (E11010552). (Data-literal; verify in game.)
2. Firelink (m10_02 e6301, 7825): 1571 → 1572, F11020690/691 (E11020587); 1570/1577 & F3 → 1577 (E11020589). Each
   Firelink load rolls F11025120-122; on 11025120 he is absent (1573) (E11020588). Gives the Sunlight Medal (F11020607);
   summonable for the Gargoyles / Depths boss while 1572/1573 (E11015103, E11005033). His line F11020608 advances Petrus.
3. **Kills the Firekeeper**: (1572/1573/1577) & (both bells F11500200, or F812 & F813) → 1578, removed (E11020410);
   the same condition kills Anastacia. Killing Lautrec (1574 → 1575) before the second bell saves her.
4. Anor Londo invasion: the Black Eye Orb (goods 115, Anastacia's remains lot 2034) "quivers" in Anor Londo
   (common E818, msg 10010690, F11510150). Invading (F11510700) spawns e6302 + bodyguards e6490/e6500 (team White
   Phantom) (E11510541). Killed (E11510542) → 1578, F8102 → back in your world: Fire Keeper Soul (lot 2060) + Ring of Favor
   and Protection (lot 6300), orb removed (E11510544).

#### Anastacia of Astora, the Firekeeper (m10_02 e6060 c2750; talk dummy 102606)
1140 alive (E11020110 keeps her immortal): kindle, Estus reinforcement with Fire Keeper Souls (ESD removes goods 390-396),
level up. Lautrec's condition above with Lautrec not hostile/dead (1574/1575 OFF) → **1141 killed** (E11020555):
EzState command 1702 (death), F11020100, F102 ON, bonfire unusable (E11020106: msg 10010184 "Fire Keeper is absent.
Cannot light bonfire."), treasure obj 1021300 enabled = her remains (lots 2030-2034: Dingy Hood/Robe/Gloves,
Blood-Stained Skirt, Black Eye Orb 115). Orb taken (F50001031 → common F810) → 1146 (E11020556). Her soul returned at the
remains (ESD sets F11020609) → 1142, EzState 1701 revive, F11020101 (E11020557 / E11020110). F11800200 → 1145 (E11020558;
late dialogue state, unverified).

#### Kingseeker Frampt (m10_02 e6330 / m18_00 e6331, c5330)
- 1640: absent (disabled; gravity off, immortal). Either bell rung → 1641, enabled with idle 9000 (dormant) (E11020420).
  Both bells → 1642, idle 7003 (E11020421). Lordvessel owned (F710) → 1643 (E11020422).
- Transport: his line sets F820 → E11026200: cutscenes 100240 + 180040, player warped to the Firelink Altar (point
  1802110), F830 ON enables e6331 there (E11800541). From the altar F824 → E11806200: cutscenes 180050 + 100250, back to
  Firelink (point 1022110), F830 OFF. Lordvessel placed → 1644 (E11800530); Kiln opened → 1645 (E11800531).
- Soul Siphon: eats items for souls (ESD `1:52(3, titanite ids, n)`).
- 1/3 chance per load at Firelink of napping (F1650, idle 9001); hit → wakes (9061), SpEffect 5450 (E11026210).
- Attacked or Darkwraith betrayal (F11020598 from his ESD) → 1647, sinks (7005 / 7009), gone (E11020424/425, E11800539).

#### Darkstalker Kaathe (m16 Abyss e6340 / m18_00 e6341, c5330)
- 1670. Four Kings dead & Lordvessel **not** placed → 1671, appears (anim 9060) (E11600537). Lordvessel owned → 1672
  (E11600538); placed → 1673 (E11600539 / E11800533-534).
- Darkwraith join (F845/846, covenant 4; rewards in 3.2; shop 6100 Cracked Red Eye Orb 3000s). Abyss → altar via F821
  (E11606200, cutscenes 160040 + 180041; F831 enables e6341); altar → Abyss via F825 (E11806201, cutscenes 180051 +
  160050). Attacked or refused (F11600590) → 1677, sinks (7005) (E11600540/541, E11800540).

#### Ingward (m16 e6180 → m10_02 e6181)
1310 in the New Londo seal tower. Lordvessel owned → 1311 (E11600530); gives the Key to the Seal (F11600592). Drained
(F11600100) → 1312 (E11600531). Four Kings dead & his line F11600593 & the player outside m16 → 1313 (E11600532) →
Firelink (E11020576). Shop 2400-2401 (Resist Curse 5000s, Transient Curse 1000s); lifts Curse (ESD). Hostile 1314,
dead 1315 (lot 6180 Key to the Seal).

#### Blacksmith Rickert of Vinheim (m16 e6220, seated 7830, in a cell)
Shop 2100-2102 (Soul Arrow 2000s, Heavy Soul Arrow 4000s, Sorcerer's Catalyst; labelled "Griggs - Initial"),
reinforce/repair, Large Magic Ember 806 / Enchanted Ember 807 (F356/357). Hostile 1381, dead 1382.

#### Giant Blacksmith (m15_01 e6210 c2860, hammering 9002)
Shop 6200-6221 (Giant's Halberd, Giant Shield, Moonlight Arrow, Dragonslayer Arrow, arrows/bolts, Giant set,
smithboxes, Titanite Shard / Large / Green, Twinkling Titanite 8000s), reinforce incl. boss-soul weapons, Crystal Ember
802 (F352). Hammer loop via EzState commands 1500/1501 (E11515090/091). Hostile 1361 / dead 1362 → object 1511110
destroyed (E11515092); lot 28600100 Blacksmith Giant Hammer.

#### Vamos (m13_00 e6200 c2920, hammering 9003, Catacombs)
Shop 6300-6310 (Homeward Bone, Titanite Shard, smithboxes, ammo), reinforce, Large Flame Ember 812 / Chaos Flame Ember
813 (F362/363). Gravity/map-hit settle on load (E11305061). Hostile 1341, dead 1342 (lot 29200000 Hammer of Vamos).

#### Crestfallen Merchant (m15_00 e6250, 7850, Sen's)
Shop 5300-5329 (Green Blossom, Black Firebomb, Titanite Shard / Large / Green, Thunder / Spell Stoneplate Ring,
Greatsword, Greataxe, Balder Shield, Tower Shield, ammo incl. Feather Arrow / Sniper Bolt, Catarina / Steel / Balder
sets). Hostile 1421, dead 1422.

#### Siegmeyer of Catarina
1. Sen's gate (m15_00 e6280, idle 7800) 1490. Gate open (F11500200), with or without his line F11500591 → 1491/1492,
   warped onto the Sen's roof (point 1502510), enemies 1500120/121 force-killed (E11500534/535). Later (F11500850,
   unverified meaning) → 1493, gone (E11500536).
2. Anor Londo (m15_01 e6283, 7850): Silver Knights 1510300-302 dead → 1494, appears (E11510535); talk → Tiny Being's
   Ring (F11510590), leaves (E11510536).
3. Firelink (m10_02 e6287): 1494 & F11510590 → 1497 (E11020579). Gifts Emit Force / Speckled Stoneplate Ring
   (F11020594/595). His line F11020592 sends him on.
4. Blighttown (m14_00 e6282, asleep 7855 / wake 7856): 1497 & F11020592 & player in Blighttown → 1501, F814 (E11400551).
   Talk (F11400590; Pierce Shield) → 1502 (E11400552).
5. Demon Ruins / Lost Izalith (m14_01 e6286): 1502 & F11400590 & player in m14_01 → 1503, F814 (E11410540).
   **Chaos Eater event**: four Chaos Eaters c3240 e1410410-413 below him. Ask to help (F11410590) while they live → 1504
   (he jumps in, team Friendly Enemy, SpEffect 90111) (E11410541); the player kills all four first → 1505 (E11410542).
   After the fight his HP < 50 % → 1506, ≥ 50 % → 1507 (E11410543/544, home 1412360). 1506 & F11410591 → he dies (1513,
   treasure) (E11410545). 1507 & F11410593 → napping (7855) (E11410548); F11410594 → gone, Speckled Stoneplate Ring
   (E11410549/550).
6. Ash Lake (m13_02 e6288, hollowed, idle 7900): 1507 & F11410593 & Sieglinde's line F11020606 → 1511 (E11320534).
   Sieglinde's last talk (F11320591) & 1548 → 1514, removed (E11320535).
- Hostile 1512 anywhere, dead 1513 (lot 6280 Speckled Stoneplate Ring + MSB treasure: Catarina set, Zweihander).

#### Sieglinde of Catarina
1. Duke's Archives: sealed in Crystal Golem e1700500 (DispMask bit 1 ON) while Siegmeyer's quest is active (any of
   1501-1511) and she is 1540/1541 (E11700538). Golem killed → 1542, she appears at its spot (E11700539). Talk (F11700593)
   → 1543, removed (E11700540).
2. Firelink (m10_02 e6292): 1542 & F11700593 & Siegmeyer alive → 1543 (E11020583). Line F11020605 → 1544, leaves to
   search (E11020584). Siegmeyer napping in Izalith (F11410593) → 1545, back (E11020585). Line F11020606 → leaves for Ash
   Lake (E11020586).
3. Ash Lake (m13_02 e6290, mourning 7860): 1545 & F11020606 & Siegmeyer 1511 → 1546 (E11320540). Talk F11320591 →
   Titanite Slab (F11320590), 1549, gone (E11320541). Hostile 1547, dead 1548.

#### Patches the Hyena
1. Catacombs (m13_00 e6320) 1620. Bridge trick: player in region 1302000 → ObjAct 1301003 param 3011; then region
   1302001 after F11300403 → param 3012 (E11300592/593). Survive → 1621 (E11300530). Apology gifts F11300590/591.
2. Pinwheel dead (6) & Rhea in the Tomb (1173) → 1623, removed from the Catacombs (E11300533), placed in the Tomb (E11310540).
3. Tomb of the Giants (m13_01 e6321): stepping into region 1313400 at the cliff → cutscene 130111 (130110 when F1638)
   kicks the player into the pit (warp 1312120), Patches warps to 1312121 (E11310002) → 1624 (E11310541). Apology →
   Twin Humanities (F11310594). Confronting line F11310595 → 1625, he turns hostile (E11310543).
4. Firelink (m10_02 e6322, 7840): 1624 & Nito dead (7) & player in Firelink → 1626 (E11020411). Shop 1600-1617
   (Eye of Death ×3, Divine Blessing, Prism Stone, Humanity ×3, Twin Humanities, Heal, Great Heal Excerpt, Crescent Axe,
   Mace, Canvas / Thorolund Talisman, the three Pinwheel masks, Cleric set). Gesture Prostration (9012).
   Hostile 1627, dead 1628 (lot 6320 Crescent Axe).

#### Shiva of the East and his bodyguard (Forest Hunter)
1. Darkroot Garden (m12_00 e6310 + bodyguard e6420): enabled only while the player is in the Forest Hunter covenant (7)
   and they are not hostile (E11200535, F1766). His line F11400582 moves him to Blighttown.
2. Blighttown (m14_00 e6311 7870 + e6421): covenant 7 & F11400582 → 1601 (E11400560). Shop 1700-1709 (East Wood Grain
   Ring, Flamberge, Stone Greatsword, Demon Great Machete, Shotel, Uchigatana, Washing Pole, Demon's Greataxe, Claw,
   Demon's Spear); gesture Look skyward (9010). Hostility → both team Enemy, covenant betrayed, F742 + F746 (E11400504).
   Leaving the covenant hides them (E11400566/567).
3. Painted World (m11): with F11100700 ON (unverified: Painted World annex/door flag) Shiva e6312 and bodyguard e6422 (team
   White Phantom) appear and hollows 1100200-213 / 1100400-404 are removed (E11100532 → 1606/1607 by F8111). Shiva killed →
   1608/1609, F8110 (E11100533/534) → a Chaos Blade lot 2800/2810/2820 picked by F11400400-402 (E11100300); otherwise
   Shiva's Black Eye Orb (goods 116, lot 1420) is removed (E11100535). Exact trigger chain unverified.
- Hostile 1603 / bodyguard 1763; dead 1604 / 1764 (lots 6310 Murakumo, 6420 Dark Wood Grain Ring).

#### Alvina of the Darkroot Wood (m12_00 e6380 c5361, 9000; gravity off, immortal, no map hits)
1710 present → Forest Hunter join (F848, covenant 7), kill-count rewards 1160/1170/1180. Hit → 1711, vanishes (anim 9030)
(E11200538). Covenant betrayed (F746) → 1712 (E11200539); 1712 & F11200596 → leaves (7003) (E11200540). Forest Hunter
NPCs e1200300-306 (NpcParam 6801-6806) switch team with the covenant (E11205060).

#### Quelana of Izalith (m14_00 e6170, 7885, Blighttown swamp)
1290 hidden. Player owns a Pyromancy Flame (weapons 1331000-1331500) → 1291, appears (E11400536). Shop 3400-3407
(Fireball, Fire Orb, Great Fireball, Firestorm, Fire Whip, Combustion, Great Combustion, Undead Rapport). Bed of Chaos
dead (10) → 1292 (E11400538); talk → Fire Tempest (F11400594); 1293 & F11400595 → next state (E11400537); then gone
(1296, E11400539). Hostile 1294, dead 1295 (lot 6170 Fire Tempest).

#### Eingyi and the Fair Lady (m14_00, Quelaag's Domain)
- Eingyi e6160 (c3210, 9001) blocks the Fair Lady (1280). His line F11400593, then hit or entering region 1402300 → 1281,
  home 1402301, team Ally (E11400531). Player infected with the Egghead parasite (SpEffect 5213/5214 → common F753) → 1286
  (E11400532): Chaos Servant access, shop 3200-3212 (Servant Roster, Egg Vermifuge, Poison Mist, Toxic Mist), Egg
  Vermifuge (F11400591, repeatable), Pyromancy Flame (F11400592). Eingyi dead → egg carriers 1400411-415 removed
  (E11400533). Hostile 1282/1283/1287, dead 1284.
- The Fair Lady (c5340 e1400700, firekeeper of the Daughter of Chaos bonfire 1401960): Chaos Servant join (F849,
  covenant 9) by offering Humanity; takes Fire Keeper Souls; rewards 1310/1320/1330. Hostile 1272 (E11400501);
  lot 53400000 Fire Keeper Soul.

#### Gwynevere, Gwyndolin, Darkmoon Knightess (m15_01)
- Gwynevere (c5310 e1510600, enabled by E11510400 while Anor Londo is bright): talk → Lordvessel (F11510592), Princess's
  Guard join (covenant 2, F844, Ring of the Sun Princess F11510595). Killing her → 1232 → darkening (§4).
- Gwyndolin (c5320 e1510650): 1240; Anor Londo dark (1232) & his tomb fog crossed (F11515382) → 1241, team Strong Enemy
  (boss) (E11510502). Darkmoon covenant at the Darkmoon Tomb altar (talk dummy 151612 at (433.7, 60.2, 255.0); joins
  covenant 8, takes Souvenir of Reprisal `1:52(3,374,-1)`).
- Darkmoon Knightess (e6010, firekeeper of the Anor Londo bonfire 1511960, 7870): takes Fire Keeper Souls. Lordvessel
  owned → dialogue state change (E11510531); all four Lord Souls held (711-714) → another (E11510532). Hostile 1033,
  dead → F151 (E11510533), lot 6010 Fire Keeper Soul.
- Dark Anor Londo extras: Darkmoon Knight phantoms e6640/e6650 (NpcParam 6640/6650) enabled; Silver Knights 1510110-114
  and sentinels 1510450/452/1510500 removed (E11510130).

#### Priscilla (m11 c2730 e1100160)
Team Ally at start (event 50); her talk lets you leave by jumping. Damaged → 1691, fight (E11100530); dead → 1692
(E11100531). Tail: Priscilla's Dagger (27310000).

#### Dusk of Oolacile (DLC hook)
1. Darkroot Basin: kill the Golden Crystal Golem (E11200530 after F11200800) → Dusk e6050 warped to it seated (7875 →
   7876). Accept (F11200590) → 1122 / decline (F11200591) → 1126; she vanishes (7877 + 8289) (E11200531/532).
   1122 (or 1129) → her summon sign appears at one of four random spots (F11200210-213, sign objects 1202000-1202003 +
   SFX 1203100-1203103; E11200533, E11205040/070). Summoned (F11205310) → 1123 (E11200534); 1129 & F11205311 → 1130.
   1123/1130: e6051 sits at the golem spot (7875). Shop (Darkroot) 2200-2205: Hidden Weapon, Hidden Body, Cast Light,
   Repair, Chameleon, Oolacile Ivory Catalyst; gesture Proper bow (9013).
2. Broken Pendant owned & Manus alive & state 1122/1125/1126 → 1127 (E11200529) → portal usable (§1.5).
3. Chasm of the Abyss (m12_01 e6700): 1127 & Manus dead (11210002) → 1128, she sits in Manus' arena (7915) (E11210540).
   Hit → flinch 7916 (E11210542); killed → treasure, vanish 7917, Elizabeth e6750 force-killed, 1125 (E11210541).
   1125 → Elizabeth's treasure dropped (E11210538, 1872).

#### DLC NPCs (m12_01)
- **Elizabeth** (c4140 e6750, Oolacile Sanctuary): shop 6500-6507 (Repair Powder, Gold Pine Resin ×6, Dusk's sorceries,
  Oolacile Ivory Catalyst). Manus dead → Elizabeth's Mushroom ×3 (F11210594). Hostile 1871.
- **Hawkeye Gough** (c4110 e6720, 9000, Oolacile Township tower): tower door opened (F11210300) → 1821 (E11210530).
  Talk F11210592 → cutscene 120100, **Kalameet shot down**: e1210401 enabled grounded, e1210402 removed, fog 1211690,
  F11210539, F121 cleared (E11210535). Kalameet dead → Gough's Greatbow (F11210593). Shop 6600-6611 (Dung Pie, Homeward
  Bone, Prism Stone, Hello / Thank you Carving, Titanite Shard / Large / Green, Gough's Great Arrow). Hostile 1822.
- **Marvellous Chester** (c4090 e6730, 9001): shop 6410-6426 (consumables, Humanity ×13 10000s F11217100, ammo); invades
  as a black phantom e6731/e6732 in Oolacile Township (E11215030 / E11210905, msg 20001100; his merchant body is
  disabled while invading). Merchant hostile 1841, dead 1842 (object 1211130 removed; lot 40900000 Snickering Top Hat).
- **Lord's Blade Ciaran** (e6740, kneeling 7880 at Artorias' grave): Soul of Artorias held (goods 710) & Artorias dead
  (11210001) → 1861, grave object 1211220 enabled (E11210531). Trade the soul (F11210590) → Dark Silver Tracer + Gold
  Tracer, 1862 (E11210532) then gone (E11210533). Decline (F11210591) → gone (E11210534). Hostile 1863, dead 1864
  (lot 6740 Porcelain Mask).
- **Battle of Stoicism** (DSR arena): c1000 talk dummies e1210661-666 (talk 121661-666) and the arena dummy (talk
  121200) near (1060, -327.6, 845); Purple Coward's Crystal lot 2200. Online-only; skip for the DS2 port.

### 3.4 NPC phantoms (white summon signs) and NPC invaders (black signs)

| NPC | Map / entity | Sign point | Kind | Condition (event) | Kill flag / drop |
|---|---|---|---|---|---|
| Solaire | m10_01 e6540 | 1012000 (Gargoyle roof) | white | Solaire not hostile/dead (E11015100/900) | — |
| Lautrec | m10_01 e6590 | 1012001 (Gargoyles) | white | freed: F11020607 & 1572/1573, not 1574 (E11015103/901) | — |
| Solaire | m10_00 e6541 | 1002000 (Gaping Dragon fog) | white | as above (E11005030/029) | — |
| Lautrec | m10_00 e6591 | 1002002 | white | as above (E11005033/036) | — |
| Knight Kirk | m10_00 e6562 | 1002001 | invader | player in region 1002005, F11000810 OFF (E11005039) | F11000810; lot 6560 Barbed Straight Sword 33 % |
| Witch Beatrice | m12_00 e6521 | 1202010 | white | Moonlight Butterfly (E11205030/035); summoning sets F11200300 | — |
| Witch Beatrice | m16 e6520 | 1602000 | white | Four Kings, needs F11200300 (E11605030/029) | — |
| Paladin Leeroy | m13_00 e6550 | 1302050 | white | Pinwheel (E11305025/029) | — |
| Paladin Leeroy | m13_01 e6551 | 1312000 | invader | region 1312001, F11310810 OFF (E11315030) | F11310810; lot 6550 Grant |
| Maneater Mildred | m14_00 e6530 | 1402060 | invader | region 1402061, F11400901 OFF (E11405035) | F11400901 (E11400901) |
| Maneater Mildred | m14_00 e6531 | 1402050 | white | Quelaag; needs F11400901 (invasion beaten) (E11405030/029) | lot 6530 Butcher Knife |
| Xanthous King Jeremiah | m11 e6570 | 1102010 | invader | region 1102011, F11100810 OFF (E11105030) | F11100810; lot 6570 Notched Whip |
| Iron Tarkus | m15_00 e6510 | 1502000 | white | Iron Golem (E11505030/029) | — |
| Solaire | m14_01 e6542 | 1412000 | white | Centipede; Solaire state 1007 (E11415030/029) | — |
| Knight Kirk | m14_01 e6560 / e6561 | 1412001 (Demon Ruins) / 1412002 (Lost Izalith) | invader | regions 1412010 / 1412520 (E11415035/038; kill flags via E11410810 slots 11410810/811) | lot 6560 |
| Solaire | m15_01 e6543 | 1512001 | white | O&S (E11515030/029) | — |
| Solaire | m18_00 e6544 | 1802050 | white | Gwyn; needs 1012 (E11805030/029) | — |
| Marvellous Chester | m12_01 e6731/6732 | scripted spawn | invader | E11215030 / E11210905 | — |
| Lautrec + 2 | m15_01 e6302, e6490, e6500 | — | you invade him | Black Eye Orb 115 | F8102; lots 2060 + 6300 |
| Fixed hostile NPC phantoms | Havel e6580 (Burg watchtower basement), Undead Prince Ricard e6600 (Sen's; E11500860 keeps him dead), Crystal Knight e6610 (Archives), Daughter of Chaos e6620 (Lost Izalith; lot 6620 Izalith Catalyst), Darkmoon Knights e6640/e6650 (dark AL) | — | NPC enemies | — | per-map files |

Every white-sign event exists twice in DSR (the second variant also requires player SpEffect 28 and a different
`Multiplayer Count` threshold); a sign is erased once its summon flag is set (`11xx5990`, "Erase NPC Summon Sign").
NPC black-phantom invaders are one-shot: killing them sets the kill flag and they never return.

## 4. Covenants

Covenant index (used by `IF Player's Covenant`, ESD `1:47(31,5,N)`, common E870 → F850+N):

| # | Covenant | Where / how to join | Requirements (data + community) | Offering / rank-up | Rewards (lots) | Betrayal / notes |
|---|---|---|---|---|---|---|
| 1 | Way of White | Petrus at Firelink (talk 102608, kneel F842); Rhea at Firelink / Parish (102607 / 101607, F841) | none | — | none (community: Ring/talisman effects only) | — |
| 2 | Princess's Guard | Gwynevere (151611, F844) | Lordvessel stage reached; community: must be in Way of White (unverified in ESD) | — | Ring of the Sun Princess (1350 via F11510595) | Killing Gwynevere betrays it and adds sin (E11510400: Betray Covenant, Increment PvP Sin, F742) |
| 3 | Warrior of Sunlight | Sunlight Altar, Undead Parish (talk dummy c1000 e6480 at (38.55, 12.24, -33.34), talk 101648, F844); Solaire's ESD has a join branch (F843) | community: Faith 25 | Sunlight Medal 375 (`1:52(3,375,-1)`); rank thresholds community 10 / 30 (unverified) | Lightning Spear 1120 (rank 0), Great Lightning Spear 1130 (rank 1), Sunlight Spear 1410 for Soul of Gwyn 702 | Altar prayer anims: player 7895 → loop 7896 → 7897, SpEffect 4170, F11015031 (E11010580) |
| 4 | Darkwraith | Darkstalker Kaathe (Abyss 160634 / Firelink Altar 180634, F845/846) | Four Kings dead before placing the Lordvessel | Humanity (community thresholds 10 / 20 / 30, unverified) | Dark Hand 1380, Red Eye Orb 1390, Darksword 1400, Dark set 1401-1404 | Frampt leaves (F11020598 → 1647) |
| 5 | Path of the Dragon | Everlasting Dragon, Ash Lake (c3450 e1320800, talk 132647, F847); player prostration anims 7910 → 7911 loop → 7912 (E11320580 on F11325030) | reach Ash Lake | Dragon Scale 1110 (`1:52(3,1110,-1)`) | Dragon Eye 1260 + Dragon Head Stone 1261 (rank 0), Dragon Torso Stone 1270 | Dragon covenant reward lot 5040 = Dragon Scale (per-kill) |
| 6 | Gravelord Servant | Nito's coffin (c5220_0001 e1310810, talk 131646, F847). Entry: lie in the coffin in region 1302700 (m13_00) holding an Eye of Death, Nito's fight not started (F11310000 OFF), wait 30 s → cutscenes 130020 + 130120, warp to Tomb point 1312110 (E11305000). Return: coffin ObjAct 11315340 → player anim 7151, cutscenes 130121 + 130021 → Catacombs point 1302010 (E11310052) | Eye of Death 109 | Eye of Death (`1:52(3,109,-1)`) | Gravelord Sword 1220 + Gravelord Sword Dance 1221 (rank 0), Gravelord Greatsword Dance 1230 | Reward lot 5010 = Eye of Death |
| 7 | Forest Hunter | Alvina, Darkroot Garden (c5361 e6380, talk 120638, F848) | reach Alvina | kills of invaded players | Cat Covenant Ring 1160, Divine Blessing 1170, Ring of Fog 1180 | Attacking Shiva / betrayal → F746, Alvina leaves (1712), Shiva hostile; reward lot 5000 = random titanite |
| 8 | Darkmoon Blade | Darkmoon Tomb altar, Anor Londo (talk 151612 dummy, F844) | Lordvessel; community: Gwyndolin alive, dark AL not required (unverified) | Souvenir of Reprisal 374 | Blue Eye Orb 1360 + Darkmoon Blade Covenant Ring 1361 (rank 0), Darkmoon Blade 1370 + Darkmoon Talisman 1371 | Reward lot 5020 = Souvenir of Reprisal; covenant check in Gwyndolin cutscene choice E11515386 (150155 / 150161 / 150160) |
| 9 | Chaos Servant | The Fair Lady (c5340 e1400700, talk 140615, F849) | Eingyi's trust (Egghead) | Humanity; also takes Fire Keeper Souls 390-396 | Great Chaos Fireball 1310 (rank 0), Chaos Fire Whip 1320, Chaos Storm 1330 | Opens the Lost Izalith shortcut (per-map) |

Sin & betrayal flags: F742 (sin/betrayal happened), F743 (Gwynevere killed), F744 (some NPC hostile), F745 (event 745,
Shiva/bodyguard both dead check), F746 (Forest Hunter betrayed). Oswald's absolution clears 742-746 (common E770).

## 5. World-state changes

| Change | Trigger | Flags / events | What changes (port checklist) |
|---|---|---|---|
| Arrive at Firelink | crow ride from the Asylum | E11810000 → F11810000; m10_02 E11020001 → F200, F11020001 | Firelink NPCs briefly back-read-removed then restored (3 s), map sound 1023800, msg 10010600 (common E260) |
| Asylum ↔ Firelink crow | curl up (msg 10010506 "Curl up like a ball", anim 7815/loop 7816/stand 7817) at Firelink region 1022120 / Asylum region 1812001 | E11020020/E11020021 (first time waits 20 s; cutscenes 100230 + 180130; F11020000); E11810150 (180126 + 100226 → Firelink point 1022121) | Asylum revisit: hollow Oscar, Stray Demon (11810900), Snuggly trades |
| Bell #1 | ObjAct 11010700 on obj 1011110 (bell tower) | F11010700; cutscene 100100 (or 100105 + F11500200 if Bell #2 already rung); achievement 29 | Oswald appears; Frampt 1641; Domhnall/Lautrec/Firekeeper conditions |
| Bell #2 | ObjAct 11400201 on obj 1401110 | F11400200; cutscenes 140010 (+140015 + F11500200 if Bell #1 rung); Homeward Bone lot 9030; achievement 30 | as above |
| Sen's gate opens | both bells | F11500200 | gate object (per-map m10_01/m15_00); Firelink: obj 1021481 / hit 1023510 swap (E11020350) (unverified: gate visual from Firelink) |
| Iron Golem → Anor Londo | Iron Golem dead (F11) + action at region 1502505 | E11500210 → cutscene 150000 (150002 when AL is dark), warp 1502501 (m15_01), achievement 33 | Return trip: talk to batwing 1510100 (msg 10010200) → cutscene 150130/150132 → Sen's point 1502500 (E11510240) |
| Lordvessel obtained | Gwynevere talk / her death | F710 (common E710) → msg 10010620; F706 = "warp allowed now" (710 ON, not in Painted World m11_00, not F11705170 Archives capture) (common E706) | Bonfire warp menu enabled for lit warp bonfires (§6.8) |
| Lordvessel placed | Firelink Altar obj 1801960, msg 10010105 (no vessel: msg 10010174) | E11800100 → cutscene 180015, F11800100, bonfire 11800992 kindle 10, goods 2510 removed | Frampt 1644 / Kaathe 1673; Kaathe never appears afterwards if Four Kings still alive |
| Lord souls offered | Altar bonfire menu (talk 180100 sets F756) | E11800200: player anim 7697, souls removed, F11800201-204, SFX 180005-180008 (E11800230), msg 10010171 if none | Darkmoon Knightess / Frampt dialogue changes |
| Kiln opens | all four offered | E11800210 → F11800211, cutscene 180010, obj 1801101 anim 1 (door), altar bonfire kindle 30; E11800220 locked prompt 10010160 before | Kiln of the First Flame reachable; Black Knights 1800200-204, 1800900-904, 1803900-902 removed after Gwyn (E11800002) |
| Endings | Gwyn dead (F15) | E20 (Link the Fire, msg 10010108, cutscene 180000, F20, achievement 1) / E21 (leave region 1802990, cutscene 180001, F21, achievement 2); both "Increment Game Cycle" | NG+: Asylum event 50 sets F705 + F50000082 (Estus) and respawn 1812900; E8200/8300/8090 duplicate-reward suppression |
| **Anor Londo darkening** | Gwynevere (e1510600) killed within 15 m | E11510400 → F743, (Princess's Guard betrayed + sin + F742), F120 OFF, cutscene 150110 (150111 if Gwyndolin dead), DrawParam bank 1 for area 15, LockCam slot 1, map part 1513400 → 1513401, obj 1511400 off, map sound 1513801 off; Lordvessel lot 1090 if not owned | Darkmoon Knights e6640/6650 on, Silver Knights 1510110-114 + 1510450/452/1510500 off (E11510130); Gwyndolin fight unlocked (1241); Sen's batwing cutscene variants; F11510400 checked by AL/PW events |
| **Firekeeper (Anastacia) killed** | Lautrec condition (both bells or both FK souls 812&813, Lautrec not hostile/dead) | E11020555 → 1141; E11020110 EzState 1702; F11020100; F102 ON | Firelink bonfire unusable (msg 10010184), remains treasure obj 1021300 (lots 2030-2034); Black Eye Orb → Lautrec invasion in AL (§3 Lautrec); revive with her soul (F11020609 → EzState 1701, F11020101) |
| Firelink bonfire kindle 30 | F71020022 / 71020023 (unverified: revived-Firekeeper / NG states) | E11020108 → bonfire 11020992 re-registered with kindle 30 | |
| **New Londo drained** | Key to the Seal → seal levers (ObjAct, E11600101: push lever msg 10010502, player anim 8020, obj 1601100 anim per lever) | E11600100 → cutscene 160000, F11600100; obj 1601100 anim 10, hits 1603100/102/103/104 off, obj 1601120 off, F404 OFF | Water gone: lower New Londo + Four Kings' Abyss entrance; Ingward 1312; ghosts' area changes (per-map) |
| Abyss death | Entering the Abyss without Covenant of Artorias | m16 sets F8120 → common E718 msg 10010650 "You were taken by the abyss" | death |
| Kaathe / Frampt transport | F821 / F820 / F824 / F825 | E11606200, E11026200, E11806200, E11806201 | §3 |
| Firelink hole → Altar | entering region 1022111 | E11020351: without F710 the player dies; with it cutscene 180060 to Firelink Altar point 1802110; F710 also disables hits 1023600/601 (E11020352) | |
| **Seath capture** (Duke's Archives) | first time in the Archives tower region 1702896/1702890 before the Crystal Cave fight (F11700000 OFF) | E11705386/383: immortal Seath e1700700 (SpEffects 5441-5443, boss bar NameID 5290); E11705100: player dies inside 1702890 → respawn point 1702900 (Archive prison); E11705385 removes 1700700 once F11700000 | Archive prison sequence (per-map); F11705170 blocks warping (common E706) |
| Seath true fight | region 1702999 in the Crystal Cave | E11700000 cutscene 170000, warp 1702801, fog 1701990 | Crystal Cave bonfire after kill |
| Archives elevator / bookshelf states | F51700990 (Archive Tower Cell Key picked) etc. | E11705101 cutscene 170010 (obj 1701400/1701410 anims) (unverified: the rotating staircase / bell lever) | per-map |
| Ceaseless Discharge dead | Ceaseless killed (F11410800 set by E11410900) | E11410800: 5 s later, player in region 1412530 → cutscene 140100, F11410801; obj 1411100 off, hit 1413100 off / 1413101 on, treasure 1411610 on, **seven Taurus Demons c2250 1410450-456 enabled** | lava lowered; new path (per-map) |
| Painted World | Peculiar Doll + AL painting (region 1512510, msg 10010100) | E11510230 cutscene 150135 → m11 point 1102510, respawn 1102511 | exit E11100710 (cutscene 110035 → AL point 1512500, respawn 1512960, Homeward Bone lot 9030 first time) |
| DLC entry | §1.5 | E11200006 (cutscene 120002 → m12_01 point 1212510) | inside DLC: E11215027 (region 1212996 → cutscene 120140, warp 1212022, F11210031); Artorias intro 120110 (E11215013) |
| Sif post-DLC | F11210021 (Sif rescued in the DLC) | E11200002: alternate intro cutscene 120003, warp point 1202802 (only in m12_00_00_01) | |

## 6. Global mechanics to translate

### 6.1 Humanity, hollowing, human-only actions
- Player forms are tested with `Character Type`: 0 Alive (human), 8 Hollow, 1 White Phantom, 2 Black Phantom, 12 Invader.
  Human (0) is required for NPC summon signs, kindling and most covenant interactions; many cutscene/boss events accept
  0 or 8 ("Hollow & Human" = ReactionType 48 on action prompts).
- Humanity items: goods 500 Humanity, 501 Twin Humanities (350 is a legacy "Humanity" goods id used by the Snuggly
  table). Boss lots 9000/9010/9020/9040 give 1/4/2/10.
- Per-NPC humanity counters: `Register NPC Humanity Event Value(entity, base)`, bases 8250-8988 (NPC humanity / souls
  after death; one block per NPC, shared across that NPC's map copies).
- Per-area humanity drop curve: GameAreaParam `humanityPointCountFlagIdTop` (e.g. 8500 for Gaping Dragon) +
  `humanityDropPoint1..10` (100 → 300) (unverified semantics: kill-count–based humanity drop chance per boss area).
- Vagrants (online item ghosts, NpcParam 7100-7240) — common E701 disables vagrant spawning in the Asylum until
  F11810000. Online-only; do not port.

### 6.2 Bonfires, kindling, Firekeepers, Estus
- `Register Bonfire(flag, object, distance, angle, kindle)`; kindle 10 = Firekeeper bonfire (Firelink 1021960, Daughter of
  Chaos 1401960, Anor Londo 1511960; also Stone Dragon/Ash Lake 1321960 and the Firelink Altar 1801960), 30 = special
  (Firelink after F11020108, Altar after the Kiln opens). The bonfire object chr id = object id − 1000 (c1000 talk
  dummy runs the bonfire ESD: talk 1AB000-1AB010 per map, 180100 for the altar).
- Kindling needs the Rite of Kindling for > 10 (goods 2607 → F257 via common E250 slot 7, msg 10010610). Common E250
  maps goods 2600-2609 to F250-259: Weapon Smithbox 250, Armor Smithbox 251, Repairbox 252, (unused menu unlocks 253-256),
  Rite of Kindling 257, Bottomless Box 258, 259. Bonfire menu entries test these (`1:19(n, text, flag)`: 250/251/252,
  710 warp, 719 attune).
- Attunement unlock: owning any spell goods (3000-5910 list in common E719) → F719.
- **Fire Keeper Souls** (goods 390-396): Undead Parish (lot 1010050), Blighttown (1400350), New Londo (1600030), Duke's
  Archives (1700200), Fair Lady corpse (53400000), Darkmoon Knightess corpse (6010), Lautrec in AL (2060). Given to a
  Firekeeper (ESD `1:52(3,390..396,-1)`) → Estus reinforce +1.
- **Estus**: goods 200/201 (+1..+7 = 202-215). Oscar gives 5 drained flasks (lot 1082). Common E8131 sets F8131-8137 from
  the highest flask level owned. Estus charges by kindle level (5/10/15/20) are engine logic.
- Embers (goods 800-813) unlock infusion paths per blacksmith; common E350 removes the ember when its flag 350-363 is set
  (Andre: 350, 351, 358, 359, 360; Giant: 352; Rickert: 356, 357; Vamos: 362, 363).

### 6.3 Curse, rings, parasites, misc player-state flags
- Curse: player SpEffect 33/34 → first-time message 10010660 ("While cursed, your HP is halved…") when the player is host,
  not in SpEffect 71-74 (common E757). Cure: Purging Stone (goods 274), Ingward. Common E750 mirrors SpEffect 71-74 into F751
  (unverified meaning of 71-74).
- Ring of Sacrifice / Rare Ring of Sacrifice break: SpEffect 2130 / 2131 at death → msg 10010670 / 10010680 (E758/E759).
- Egghead parasite: SpEffect 5213/5214 → F753 (common E752); cured → F753 OFF and F11400591 reset.
- F754 → SpEffect 4600 + 4601 and SFX 22715 on the player (common E754; unverified purpose, likely a covenant/soul effect).
- F762 → player anim 7697 then loop 7701 (common E763; offering kneel, also used by the Lord-soul offering E11800200).
- Class flags: E290 sets 280-290 then clears 287 (Knight/Cleric) or 288 (others); E740 Pyromancer → F740 (dialogue
  variants, e.g. Laurentius).
- Shop-completion aggregates: 721 (Logan Archives basic spells 11707000-070), 728 (Logan crystal 11707090-110), 725 (≥ 2
  of 11707100-190), 722 (Quelana's list + Bed of Chaos + owns weapon 1332500), 723 (Griggs), 724 (Rhea), 726 (≥ 2 of
  11607000-090: Ingward / Oswald stock), 727 (11327000-090, unverified).

### 6.4 Invasions, summons, NPC phantoms
- See §3.4. Sign types: `Place NPC Summon Sign(SignType 0 white / 1 black, NPC entity, spawn point region, summon flag,
  dismissal flag)`; `Erase NPC Summon Sign` on the summon flag (11xx5990). Summoned phantoms walk to the boss fog
  (region 1xx2998) and play 7410 (AI command 10, E11015101/104).
- Boss fogs for phantoms: second fog event per boss accepts White Phantom (TargetType 1) and plays 7410 without the
  boss-entry notification.
- Player PvP items: White Sign Soapstone (Solaire), Orange Soapstone, Cracked Red Eye Orb (Kaathe shop / New Londo
  lot 1600310 / Firelink lot 1020200), Red Eye Orb (Darkwraith rank 1), Blue Eye Orb (Darkmoon), Black Eye Orbs 115/116,
  Dragon Eye, Eye of Death, Black Separation Crystal (goods 103; Firelink relief chest restores it when F200 ON),
  Purple Coward's Crystal. For an offline DS2 port only the NPC phantoms matter.

### 6.5 NG+ and cycles
- `Increment Game Cycle` in E20/E21. On load in NG+ the Asylum event 50 sets F705 and F50000082 (Estus already "given"),
  respawn point 1812900, and sets the Great Lord shop flags 11807200/210/220/240 OFF (set purchasable in NG+).
- Common E8200 (34 slots) / E8300 (8) / E8090 (5): in NG+ if the player already owns a covenant reward or shop key item,
  mark its lot/shop flag obtained so it is not duplicated.
- Common E50 one-time init sets every boss-armor shop flag (11807020-11807240, 11217060-090) ON = sold out until the boss dies.

### 6.6 Snuggly the Crow (Asylum nest, talk 181645; m18_01 E11810641 / E11815110)
Drop an item in the nest → the matching flag 11810601-621 → lot 3000-3200 (first time only, flags 51810800-51811000):
Cracked Red Eye Orb → Purging Stone ×2; Bloodred Moss → Twinkling Titanite; Purple Moss → Twinkling Titanite;
Blooming Purple Moss → Twinkling Titanite ×2; Egg Vermifuge → Dragon Scale; Dung Pie → Demon Titanite; Humanity (350/500)
→ Ring of Sacrifice; Prism Stone → Demon Titanite; Sunlight Medal → White Titanite Chunk; Pendant → Souvenir of Reprisal;
Rubbish → Titanite Chunk; Dried Finger → Blue Titanite Chunk; Twin Humanities → Rare Ring of Sacrifice; Pyromancy Flame
(weapon 1330000) → Red Titanite Chunk; Pyromancy Flame ascended (1332000) → Red Titanite Slab; Skull Lantern (1396000) →
Ring of Fog; Sunlight Maggot (armor 190000) → Old Witch's Ring; Xanthous Crown (290000) → Ring of Favor and Protection;
Sack (560000) → Demon's Great Hammer; Ring of the Sun Princess (ring 130) → Divine Blessing ×2; Soul of Manus (711) →
Pursuers. Other items (any) get the generic reply.

### 6.7 Firelink "relief chest" (DSR key-item recovery)
m10_02 event 50 initialises 71 slots of E11020800: if a key item's pickup flag is ON, the player no longer owns it and
the relief flag is OFF → F11025300; opening ObjAct 1021690 (flag 11020704) re-enables treasure objects 1021700-1021707
and re-rolls them (lots 4000-4077: every key, Lord Souls, Lordvessel, Estus levels, embers, smithboxes, covenant items)
(E11020899). Port as a safety net or skip.

### 6.8 Bonfires and the warp list

**Warp-capable bonfires** (DSR exe table @0x1a309c0: unlock flag, bonfire object, Place_name id). Warping needs F710
(Lordvessel) and F706 (not in the Painted World, not during the Archives capture); each entry also needs its own unlock
flag (200-220; Firelink's 200 is set on arrival by E11020001, the others when the bonfire is lit — engine-set, unverified).

| Flag | Object | Name (FMG 2000+) | Map / chr pos |
|---|---|---|---|
| 200 | 1021960 | Firelink Shrine | m10_02 (-51.8, -60.1, 55.7) |
| 205 | 1011961 | Sunlight Altar | m10_01 (24.35, 10.0, -22.2) |
| 215 | 1011964 | Undead Parish | m10_01 (87.9, 15.5, 107.5) by Andre |
| 214 | 1001960 | Depths | m10_00 (-120.5, -73.6, 12.2) |
| 202 | 1401960 | Daughter of Chaos | m14_00 (140.4, -252.1, 95.1) by the Fair Lady |
| 216 | 1101960 | Painted World of Ariamis | m11 (-23.0, 52.8, 944.0) |
| 203 | 1511960 | Anor Londo | m15_01 (169.8, 173.9, 255.0) by the Darkmoon Knightess |
| 207 | 1511950 | Chamber of the Princess | m15_01 (593.0, 161.5, 255.0) (after O&S) |
| 208 | 1511962 | Darkmoon Tomb | m15_01 (392.0, 70.5, 255.0) |
| 204 | 1601950 | The Abyss | m16 (85.3, -311.0, 3.2) (after Four Kings) |
| 206 | 1311950 | Altar of the Gravelord | m13_01 (-160.0, -264.4, -34.0) (after Nito) |
| 217 | 1311961 | Tomb of the Giants | m13_01 (96.0, -199.3, 104.0) by Patches |
| 220 | 1301962 | Catacombs | m13_00 (21.4, -157.0, 250.8) by Vamos |
| 201 | 1321960 | Stone Dragon | m13_02 (-700.0, -413.2, -323.5) Ash Lake, Everlasting Dragon |
| 218 | 1701960 | The Duke's Archives | m17 (350.1, 278.1, 594.4) |
| 219 | 1701950 | Crystal Cave | m17 (95.7, 134.6, 864.2) (after Seath) |
| 209 | 1211963 | Sanctuary Garden | m12_01 (897.5, -328.8, 451.5) |
| 210 | 1211961 | Oolacile Sanctuary | m12_01 (971.7, -313.4, 583.3) by Elizabeth |
| 211 | 1211962 | Oolacile Township | m12_01 (1040.6, -331.5, 875.9) |
| 213 | 1211964 | Oolacile Township Dungeon | m12_01 (862.8, -447.0, 911.2) |
| 212 | 1211950 | Chasm of the Abyss | m12_01 (854.0, -576.3, 848.0) (after Manus) |

Not warpable: Firelink Altar (reached via Frampt/Kaathe or the hole), both Asylum bonfires, Undead Burg, Darkroot
Garden, Sen's Fortress, the other Catacombs/Tomb/Blighttown/Demon Ruins/Lost Izalith/Great Hollow/Ash Lake/New Londo/
Archives bonfires, Kiln.

**All registered bonfires** (Register Bonfire flag / object / default kindle):
m18_01 11810992/1811960, 11810984/1811961 (Asylum; chr (3.1, 196.4, 7.4) and (33.3, 193.7, -26.5));
m10_02 11020992/1021960 (10; 30 after F11020108); m10_01 11010984/1011961 (Sunlight Altar), 11010976/1011962 (Undead Burg,
chr (3.2, -9.5, -61.2)), 11010960/1011964 (Undead Parish); m10_00 11000992/1001960; m14_00 11400992/1401960 (10),
11400984/1401961 (Blighttown swamp), 11400976/1401962 (Blighttown upper); m12_00 11200984/1201961 (Darkroot Garden);
m15_00 11500984/1501961 (Sen's); m15_01 11510992/1511960 (10), 11510984/1511961 (before O&S, Solaire), 11510976/1511962
(Darkmoon Tomb), 11510920/1511950 (after O&S); m11 11100992/1101960; m13_00 11300992/1301960, 11300984/1301961,
11300976/1301962; m13_01 11310992/1311960, 11310984/1311961, 11310920/1311950 (after Nito); m13_02 11320992/1321960 (10),
11320984/1321961 (Ash Lake, Sieglinde), 11320976/1321962 (Great Hollow); m14_01 11410992/1411960, 11410984/1411961,
11410976/1411962 (after Ceaseless), 11410968/1411963, 11410960/1411964, 11410920/1411950 (after Bed of Chaos);
m16 11600984/1601961 (New Londo side), 11600920/1601950 (after Four Kings); m17 11700992/1701960, 11700984/1701961,
11700976/1701962, 11700920/1701950 (after Seath); m18_00 11800992/1801960 (Lordvessel altar: 10, 30 after the Kiln opens);
Kiln bonfire obj 1801950 (enabled after Gwyn; "Link the Fire"); m12_01 11210984/1211963, 11210976/1211961,
11210968/1211962, 11210960/1211964, 11210992/1211950 (after Manus). Boss-kill bonfires appear with SFX 90014 then
`Register Bonfire(..., kindle 0)`.

### 6.9 Gestures (taught in dialogue: `1:65(3, 90xx, 1)` + `1:66(n)`)
Domhnall → Joy (9004); Rhea → Prayer (9014); Andre → Hurrah! (9002); Oswald → Well! What is it! (9011); Sunlight Altar →
Praise the Sun (9007); Petrus → Shrug (9005); Patches → Prostration (9012); Dusk → Proper bow (9013); Shiva → Look
skyward (9010). The rest (Beckon 9000, Point forward 9001, Bow 9003, Wave 9006, Point up 9008, Point down 9009) are
starting gestures (unverified).

### 6.10 Text ids worth carrying over
Event_text: 10010100 Examine, 10010105 Place Lordvessel, 10010108 Link the Fire, 10010120 Examine ring of light,
10010160 Locked by some contraption, 10010170 Contraption does not move, 10010171 No Lord Souls to offer, 10010174 No
Lordvessel, 10010182 Light bonfire, 10010184 Fire Keeper is absent, 10010200 Talk, 10010400 Open, 10010403/404 Traverse /
Leave through the white light, 10010501/502/503 Pull lever / Push lever / Turn handle, 10010506/507 Curl up / Stand up,
10010600 / 10010610 / 10010620 tutorial-style status messages (Firelink arrival / Rite / Lordvessel), 10010650 Abyss,
10010660 Curse, 10010670/680 Ring of Sacrifice, 10010690 Black Eye Orb quivering, 1001086x-883 "Used <key>".

## 7. Cross-map completion checklist (must-port)

- [ ] NPC framework: per-NPC flag ranges + map-local event-50 enable logic + the 0510/0520 hostility/death templates +
      F744 aggregate + Oswald absolution reset (E770) + covenant kneel E840 + E911 dialogue gift table.
- [ ] Talk ESD translation for ~120 talk files (shops `1:22`, flags `1:11`, covenant `1:47(31,5,N)`, gestures, item
      removal `1:52`, ember unlocks `1:49`, reinforce `1:24`); ShopLineup rows 1100-6611 with sold-out flags.
- [ ] Global boss flags 2-17 + map boss flags; E970 boss reward lots; GameAreaParam soul rewards.
- [ ] Bells (11010700, 11400200) → 11500200 Sen's gate; Frampt wake states.
- [ ] Lordvessel (710/706 warping) + warp table (21 entries) + Firelink Altar placing/offering/Kiln door (11800100,
      11800201-204, 11800211) + both endings (E20/E21) + NG+ cycle.
- [ ] Anor Londo darkening (draw param + map part swap + NPC/enemy swaps) as a world-state toggle.
- [ ] New Londo drain (water map parts/hits) as a world-state toggle; Abyss death without the ring.
- [ ] Firekeeper death/revival + Lautrec AL invasion; Rhea counter (event value 600 via E690).
- [ ] Seath capture respawn redirect; Painted World entry/exit; DLC portal (only in m12_00_00_01) + Broken Pendant event.
- [ ] Inter-map NPC moves (Logan, Griggs, Laurentius, Siegmeyer, Sieglinde, Rhea, Patches, Domhnall, Ingward,
      Crestfallen Warrior, Shiva) — states are read in several maps, so the DS2 flag space must be global.
- [ ] NPC summons (white signs) and NPC invaders (black signs) with one-shot kill flags.
- [ ] Covenants 1-9 (join, offerings, rank rewards, betrayal) — DS2 has its own covenant system; map each DS1 covenant to a
      DS2 covenant slot or emulate with flags.
- [ ] Humanity/hollow state, kindling (Rite), Firekeeper souls → Estus, embers per blacksmith, curse, Rings of Sacrifice,
      Egghead, Snuggly trades, relief chest (optional), NPC humanity counters.
