# 06 — Demon Ruins, Lost Izalith, The Great Hollow, Ash Lake

DS1 content census for the "Lordran in DS2" port. Ground truth: DSR MSBs (`msb1-dump` + the `msbev` reader for InitAnimID / MovePointNames / PlatoonID / MSB events), EMEVDs (`emevd-dump`), DSR params (`dump-param dsr`), talk ESDs (`unbind` + `esd-dump`). Wiki knowledge (Fextralife) is used only to name things and to cross-check; anything not confirmed by data is marked **(unverified)**.

Conventions
- Positions are DS1 world coordinates `(x, y, z)` from the MSB (m14_00 / m14_01 share one frame, as do m13_xx), so cross-map adjacency can be read off coordinates.
- `ent` = MSB entity ID, `npc` = NpcParam row, `think` = NpcThinkParam, `talk` = ESD id, `flag` = event flag, `lot` = ItemLotParam row. DS1 item lots chain: lot N also rolls N+1, N+2… while rows exist.
- "BWT" = only enabled by the Black World Tendency event (see porting notes). "Non-respawn" = killed state persisted by an event (usually 11410100 / 11320100-style "if dead → disable" events).
- Empty corpse = MSB treasure that points at an ItemLotParam row that does not exist in DSR → gives nothing.
- No enemy in either map has `MovePointNames` (patrol routes) or a `PlatoonID`; every non-default `InitAnimID` is listed. Neither MSB has enemy **generators** — every enemy is an MSB part listed below.
- Text strings come from a direct FMG dump (item/menu msgbnd), not `ds1-text` (which collides ids, e.g. 5200 → "Miracle: Escape Death" instead of NPC name "Centipede Demon").

## 0. Map-file facts

| Area | MSB / EMEVD | Place-name id (collision mapName) | Notes |
|---|---|---|---|
| Demon Ruins | `m14_01_00_00` | 1410 "Demon Ruins" | Same MSB as Lost Izalith; boundary is roughly the Centipede Demon arena exit. |
| Lost Izalith | `m14_01_00_00` | 1411 / −1411 "Lost Izalith" | 61 collisions carry −1411, 1 carries 1411 (`h0042B1` ent 1413000 — the bug-room floor used by Solaire's warp). |
| The Great Hollow | `m13_02_00_00` | 1320 "The Great Hollow" (3 collisions) | Tree interior, top y≈−207 → bottom y≈−393. |
| Ash Lake | `m13_02_00_00` | −1321 "Ash Lake" (22 collisions) | Same MSB as Great Hollow. |
| Quelaag's Domain / Daughter of Chaos / Fair Lady / Eingyi | `m14_00_00_00` | 1401 | **Not in my maps** — covered by 03-darkroot-blighttown.md. Only their interplay with m14_01 is listed here (Chaos Servant door, elevator, backread removal). |

Brief corrections / surprises
- **No Mimic in the Great Hollow or Ash Lake** — m13_02 has no c2780 part and its only chest (`o0110_0000`, Twin Humanities) is a real chest (ObjAct 11320600).
- **The elevator is not "to Firelink-side"**: the only lift in m14_01 ("神殿エレベータ" temple elevator, `o4600` ent 1411400) runs from the Demon Firesage exit stairs (y −318.3) up to the Quelaag's Domain / Daughter of Chaos level (y −251.7). It only works after the Firesage dies.
- **"Daughter of Chaos" also exists as a hostile NPC in Lost Izalith** (`c0000_0010` ent 6620, npc 6620, wearing the Gold-Hemmed Black set + Pyromancy Flame+5 with chaos pyromancies, drops the **Izalith Catalyst**, lot 6620). Quelaag's sister (the Fair Lady) is the m14_00 c5340.
- The Titanite Demon (`c2300_0000`) has entity **1310150** (a Catacombs-style id), while the "kill once" event 11410100 slot 8 watches **1410150** — a data typo, so this Titanite Demon respawns (matches wiki "respawning Titanite Demon").
- 8 Black-World-Tendency-only phantoms (5 Capra Demons, 3 Chaos Eaters) exist in m14_01 (event 11415090–92).
- Unused EMEVD leftovers: m14_01 events 11410090 (white-light warp template), 11410402 ("Contraption does not move" lift message — never initialised, so the locked lift is silent), 11415200 (NPC-part setup for a non-existent ent 1410500); m13_02 event 11325090 (BWT spawn of non-existent ent 1320900).

---

# PART A — DEMON RUINS (m14_01_00_00)

## A.1 Header

- Reached from **Quelaag's Domain** (m14_00): the stairs below the Second Bell tower lead down to the Demon Ruins entrance bonfire (wiki: passage beneath the bell tower). Quelaag must be dead to pass her arena (m14_00).
- Entry warp point: Player part `c0000_0001` ent 1410998 (190.0, −267.2, 128.0) next to the entrance bonfire.
- Co-op boundary fogs (client-only, disabled for host/single-player by event 0): `o4966` ent 1411994 (180.0, −266.75, 128.0) — the m14_00/m14_01 seam; `o4973` 1411982 (237.55, −280.45, 139.72).

| Connection | Where | Requirement |
|---|---|---|
| Quelaag's Domain (m14_00) → Demon Ruins entrance | boundary fog 1411994 (180, −266.8, 128) | Quelaag dead (m14_00) |
| Demon Ruins Central ↔ lower ruins | after Ceaseless Discharge drains the lava | **Ceaseless Discharge killed** (lava drain, flag 11410800/11410801) |
| Lower ruins → Demon Firesage | golden seal `o4960` ent 1411710 (159.53, −340.96, 82.0) | **Lordvessel placed** (global flag 11800100) |
| Firesage exit stairs ↔ Quelaag's Domain (Daughter of Chaos level) | lift `o4600` ent 1411400, bottom (122.66, −318.3, 125.82) ↔ top (122.66, −251.7, 125.82) | **Demon Firesage killed** (flag 11410410). Top landing sits behind m14_00 breakable wall `o4570` ent 1401200 (126.5, −251.63, 119.19) (m14_00 event 11400210) |
| Demon Ruins → Lost Izalith | through the Centipede Demon arena (fogs 1411890 / 1411892) | Centipede Demon fight (fogs) |
| Demon Ruins (locked side) ↔ Lost Izalith bug room | Chaos Servant door `o4700` ent 1411340 (239.64, −367.78, 158.02) | From the Demon-Ruins side: host + **Chaos Servant covenant** (EMEVD covenant index 9) + flag **11400598** (covenant rank 2 reward = Chaos Storm). From the bug-room side anyone can open it. |

## A.2 Sub-zones in route order

| # | Sub-zone | Rough bounds | Content |
|---|---|---|---|
| D1 | Entrance landing | x 180–260, y −264…−292, z 120–200 | Entrance bonfire, 14 Egg Carriers (praying), co-op fogs |
| D2 | Ceaseless Discharge hall — upper walkway | x 250–405, y −277…−284, z 13–76 | CD fog gate, 2 Large Souls, Gold-Hemmed Black set "altar" corpse |
| D3 | Ceaseless Discharge hall — lava bed | x 280–430, y −303…−306, z 38–160 | CD (boss), Chaos Flame Ember, Proud Knight souls; after the drain: 7 Taurus Demons, Capra Demon, Vagrants, Kirk invasion trigger |
| D4 | Demon Ruins Central | x 185–255, y −324…−351, z −6…108 | Central bonfire (after CD), 5 Capra Demons, wall Burrower, Stone Demons, Capra + Burrower further down |
| D5 | Lower ruins / Rockworm room | x 105–170, y −344…−356, z 0–80 | 2 Taurus Demons, 5 Burrowers (4 drop from ceiling), Large Flame Ember chest |
| D6 | Golden seal + Firesage room | x 105–170, y −331…−353, z 70–145 | Lordvessel seal, Taurus, 2 Stone Demons, Demon Firesage (boss) |
| D7 | Firesage exit stairs / lift / lower bonfire | x 90–130, y −318…−367, z 110–165 | 7 Stone Demons, lift to Quelaag's Domain, bonfire before Centipede |
| D8 | Centipede Demon lava arena | x 125–225, y −383…−385, z −20…100 | Solaire's summon sign, Centipede Demon (boss), Green Titanite Shards alcove |

## A.3 Progression walkthrough

1. **D1** Arrive at the bottom of the stairs from Quelaag's Domain: **Demon Ruins bonfire** (195.0, −267.0, 130.0). 14 **Egg Carriers** kneel/pray around it (`InitAnimID 9000` on 12 of them, ambient prayer sound region "卵背負い祈り" (205.3, −261.5, 124.3); map sound 1413806 is switched off once ≥11 carriers are dead — event 11415210). They are passive; killing one releases 5 Chaos Bugs (A.5).
2. Down the passage to the **Ceaseless Discharge fog** `o4961` ent 1411790 (249.0, −283.15, 70.4) (entry region 1412698, target 1412697; "safe zone near the entrance" region 1412780 (260, −284, 76)). The fog exists only until CD dies.
3. **D2** Walk the upper walkway along the lava hall (CD sits inert in the lava). Large Souls of a Brave Warrior at (382.2, −278.15, 59.57) and (374.55, −278.14, 73.35). At the far end the special corpse `o0504_0000` (401.48, −277.18, 13.51) holds the **Gold-Hemmed Black set** (lots 1410180–1410183). Picking it up (flag 51410180) **wakes Ceaseless Discharge** (or hitting him first does).
4. Fight or cheese CD (A.6.1). His death (flag 11410900) removes the fog, enables the **Demon Ruins Central bonfire**, and (5 s later, when the host enters the lava-hall box 1412530) plays cutscene **140100**: the lava drains (object `o4500` ent 1411100 = whole lava layer disabled; collision `h0008B1` 1413100 off, `h1008B1` 1413101 on), the corpse at (301.12, −304.2, 111.67) becomes lootable, and **7 Taurus Demons** appear on the cooled lava bed.
5. **D3** Cross the drained bed: Chaos Flame Ember (425.28, −303.99, 67.19), Large Soul of a Proud Knight (386.0, −303.21, 100.0), Soul of a Proud Knight (301.12, −304.2, 111.67). One Capra Demon (291.3, −303.6, 86.7). Vagrants at (312.9, −303.0, 154.6). **Kirk invasion #1** trigger box 1412010 (248.1, −314.0, 70.7) on the descent; he spawns at 1412001 (255.93, −308.66, 81.01) (A.7).
6. **D4** Descend into Demon Ruins Central: bonfire (251.9, −334.0, 22.0) (only after CD), a wall Burrower right by it (249.67, −332.73, 21.27), **5 Capra Demons** on the floor below (223–249, −324.3, −0.6…15.8) (Capra patrol points "移動ポイント 鉈00_00/01" (245.77, −334.33, 16.2)/(211.97, −334.47, 20.98) exist but are not wired to MovePointNames), 5 Stone Demons (187–208, −332…−349, 19–46). Souls of a Proud Knight (243.99, −334.03, 25.59) and (218.22, −333.89, 22.97), Green Titanite Shard ×2 (210.87, −333.86, 3.34). Further on: Capra Demon (194.0, −342.7, 84.7), Burrower (187.8, −351.0, 64.2) (emerges within 15 m), Souls of a Brave Warrior (193.16, −343.31, 79.19) and (187.54, −346.52, 107.8); empty corpse (158.0, −344.3, 63.1).
7. **D5** Lower ruins: Taurus Demons at (155.7, −354.3, 45.1) and (110.9, −354.5, 49.4). Rockworm room (113–127, −348, 3–37): one Burrower emerges at 10 m (113.0, −348.2, 37.1), four more drop from the ceiling when the player enters region 1412302 "天井ウジ虫出現領域" (122.96, −355.0, 12.05) with 0.0–0.3 s staggered delays (event 11415120). Chest (129.02, −354.51, −4.42) = **Large Flame Ember**; 3 empty corpses beside it.
8. **D6** **Golden Lordvessel seal** `o4960` 1411710 + "examine brilliant light" region 1412660 (160.01, −341.23, 81.43): without the Lordvessel the prompt shows "Sealed by the Great Lord's power" (10010630). Guarded by a Taurus Demon (166.96, −344.87, 72.52) and Stone Demons (160–167, −341, 76–77).
9. **Demon Firesage fog** `o4962` ent 1411410 (148.9, −341.04, 94.6) (entry 1412411 / target 1412412; fight starts at 1412416). Firesage room box (130.2, −342, 117.4) 59×21×30. Large Souls (126.3, −341.02, 117.5) and (122.0, −341.24, 135.0) inside. Exit fog `o4963` ent 1411412 (110.38, −341.0, 140.5) (+ client-only twin `o4969` 1411988 at the same spot).
10. **D7** Exit stairs up (Stone Demons ×7 at (91–105, −328…−338, 140–162)); Soul of a Brave Warrior (103.77, −328.16, 163.05). At the top (y −318): **lift** to Quelaag's Domain (works only after the Firesage, A.9), Large Soul (117.0, −319.19, 121.5), Vagrants (128.3, −319.2, 130.6) / (135.7, −330.7, 110.1). Client-only fog `o4967` 1411996 (111.52, −321.44, 139.01). Downward: **bonfire before the Centipede** (118.8, −356.6, 138.2), Soul of a Brave Warrior (122.1, −366.24, 120.1).
11. **D8** Down to the lava lake: Large Soul (164.0, −383.46, 93.0), **Solaire's summon sign** 1412000 (156.86, −383.2, 96.92) (conditions A.7), **Centipede fog** `o4964` 1411890 (167.3, −383.48, 80.4) (entry 1412898, target 1412897, fight starts at 1412896). Centipede (204.0, −383.7, 51.5); arena BGM box 1412890 (185, −385, 30) 120×30×100; Green Titanite Shard ×2 in an alcove (133.89, −383.46, 40.64). Hidden developer message "Safe zone ahead" (blood message 4100, Hidden=True) at (159.3, −383.44, 47.1). Exit fog `o4965` 1411892 (216.2, −383.49, 58.6) (+ client twin `o4970` 1411986) → Lost Izalith (Part B).

## A.4 Bonfires (all of m14_01)

Bonfires are a `c1000` "character" (npc 100000, talk 1410xx) + an `o0200` object; registered in event 0 with `Register Bonfire`. All standard kindling 0.

| Name (community) | Char part / ent | Object ent | Pos | Flag | Talk | Notes |
|---|---|---|---|---|---|---|
| Demon Ruins (entrance) | `c1000_0001` 1410961 | 1411961 | (195.0, −267.0, 130.0) | 11410984 | 141001 | Collision `h0001B1` has `disableBonfire=1411961`. Spawn point event 17 → 1412961 |
| Demon Ruins Central | `c1000_0002` 1410962 | 1411962 | (251.9, −334.0, 22.0) | 11410976 | 141002 | **Object disabled and not registered until Ceaseless Discharge dies** (event 0 line 4, event 11410900) |
| Demon Ruins (before Centipede / below Firesage) | `c1000_0005` 1410963 | 1411963 | (118.8, −356.6, 138.2) | 11410968 | 141003 | — |
| Lost Izalith (after Centipede) | `c1000_0006` 1410964 | 1411964 | (229.5, −383.5, 91.5) | 11410960 | 141004 | Solaire sits here (B.7) |
| Lost Izalith (hidden, 2nd tower) | `c1000_0004` 1410960 | 1411960 | (455.6, −380.0, 169.3) | 11410992 | 141000 | Behind breakable illusory wall `o4510` ent 1411360 (456.18, −380.03, 175.22) |
| Bed of Chaos (post-boss) | `c1000_0003` 1410950 | 1411950 | (581.08, −443.94, 444.83) | 11410920 | 141010 | Object disabled until BoC dies (flag 10), then SFX 90014 + register (event 11410001) |

No Firekeeper in m14_01. Kindling is the generic bonfire ESD (t141000–141004, t141010).

## A.5 Enemies — Demon Ruins

Souls/HP from NpcParam; drops = NpcParam item lot (chained). "resp" = respawns at rest.

| Sub-zone | Enemy (community / NpcParam name) | Model | n | MSB parts (ent) | npc / think | Behaviour & notes | Drops |
|---|---|---|---|---|---|---|---|
| D1 | Egg Carrier ("Egg Carrier") | c3210 | 14 | `c3210_0000…0013` (1410200–1410213) at (199–259, −265…−290, 122–197) | 321000 / 321000 | Passive. `0000–0011` InitAnim **9000** (kneel/pray), `0012–0013` default. Pairs with the hatchlings below; resp | 0 souls, lot 32100000 |
| D1 | Chaos Bug / "Vile Maggot" (egg hatchlings) | c3220 | 70 | `c3220_0000…0069` (1410220–1410289), 5 per carrier, parked at the carrier's position | 322000 / 322000 | Disabled until their carrier dies (event 11415250 slot n): carrier's egg DispMask bit 2 off, the 5 bugs warp to carrier dummypolys 100–104 and play anim 7000 (burst out) | 15 souls, lot 32200000 (none) |
| D3 | Ceaseless Discharge | c5250 | 1 | `c5250_0000` 1410600 (420.05, −304.13, 52.03) | 525000 | Boss, A.6.1 | — |
| D3 (post-drain) | Taurus Demon | c2250 | 7 | `c2250_0003…0009` (1410450–1410456) at (383–419, −304.16, 117–148) | 225002 / 225002 | **Disabled until the lava drains** (event 11410800); **non-respawn** (11410100 slots 38–44) | 1200 souls, lot 22500200 (Demon's Greataxe, rare) |
| D3 | Capra Demon | c2240 | 1 | `c2240_0000` (291.29, −303.6, 86.72) | 224001 / 224000 | resp; "one initial Capra" on the cooled bed | 800 souls, lot 22400100 (Demon Great Machete, rare) |
| D3 | Vagrant set | c3490 + c3491 ×2 | 3 | `c3490_0000` 1413900 (312.88, −303.03, 154.59); `c3491_0000` 1413901 + `c3491_0003` 1413902 (314.79, −303.11, 152.59) | 349012 passive / 349112, 349212 aggressive | Online/vagrant system; no map EMEVD references (unverified spawn rules — engine-driven) | lots 34911200 / 34921200 |
| D4 | Burrower (Burrowing Rockworm) | c3390 | 1 | `c3390_0007` 1410307 (249.67, −332.73, 21.27) | 339002 | In the wall next to Central bonfire (wall regions 1412315–1412318). InitAnim 9000 (buried); cancel anim **9060** when player within 15 m (event 11415100 slot 2). **Non-respawn** (11410100 slot 4) | 900 souls; one-time award lot **33900000** on death (Green Titanite Shard ×1–2 75%/25%/5%) via 11410150 |
| D4 | Capra Demon | c2240 | 5 | `c2240_0002…0006` (223–249, −324.3, −0.6…15.8) | 224001 / 224003 | resp | as above |
| D4 BWT | Capra Demon (black phantom) | c2240 | 5 | `c2240_0007` 1410900 (194.07, −345.06, 38.95), `0008` 1410901 (243, −324.3, 14), `0009` 1410902 (227.5, −324.25, −5.2), `0010` 1410903 (201.5, −339.72, 30.5), `0011` 1410904 (197, −344.87, 41) | 224002 / 224003 | **Disabled unless Black World Tendency > 50** (event 11415090–92) | as above |
| D4 | Stone Demon ("Demonic Statue") | c2430 | 5 | `c2430_0007…0011` (187–208, −332.75…−349.45, 19–46) | 243000 / 243000 | Statue-like enemies; resp | 300 souls, lot 24300000 (none) |
| D4 | Capra Demon | c2240 | 1 | `c2240_0001` (194.03, −342.69, 84.68) | 224001 / 224004 | resp | as above |
| D4 | Burrower | c3390 | 1 | `c3390_0001` 1410301 (187.83, −350.98, 64.22) | 339001 | InitAnim 9000; emerges (9060) within 15 m | one-time lot 33900000 |
| D5 | Taurus Demon | c2250 | 2 | `c2250_0000` (155.67, −354.28, 45.14), `c2250_0002` (110.94, −354.51, 49.41) | 225002 / **225003** | resp | as above |
| D5 | Burrower | c3390 | 5 | `c3390_0002` 1410302 (113.04, −348.21, 37.14) [emerges at 10 m]; `0003–0006` 1410303–306 (119–127, −348.1…−348.2, 3–21) [ceiling ambush, region 1412302] | 339003 | InitAnim 9000 | one-time lot 33900000 each |
| D5 | Vagrant set | c3490 / c3491 | 3 | `c3490_0001` 1413910 (131.09, −354.41, 0.06); `c3491_0001` 1413911 + `c3491_0005` 1413912 (215.6, −324.3, 16.5) | as above | Note the aggressive pair sits in D4 near black-invasion point 100 | — |
| D6 | Taurus Demon | c2250 | 1 | `c2250_0001` (166.96, −344.87, 72.52) | 225002 / 225002 | resp, guards the golden seal | as above |
| D6 | Stone Demon | c2430 | 2 | `c2430_0012` (167.3, −341.04, 77.45), `c2430_0013` (160.19, −341.04, 76.18) | 243000 | resp | — |
| D6 | Demon Firesage | c2231 | 1 | `c2231_0000` 1410400 (119.22, −341.04, 129.8) | 223100 / 223100 | Boss, A.6.2 | — |
| D7 | Stone Demon | c2430 | 7 | `c2430_0000, 0003–0006, 0031, 0032` (91–105, −328.5…−338.5, 140–162) | 243000 / 243000 (`0000, 0031, 0032` think 243001) | resp | — |
| D7 | Vagrant set | c3490 / c3491 | 3 | `c3490_0002` 1413920 (128.27, −319.19, 130.6); `c3491_0002` 1413921 + `c3491_0006` 1413922 (135.71, −330.67, 110.11) | | | — |
| D8 | Centipede Demon (+ severable Arm ×5, Tail ×5) | c5200 / c5201 / c5202 | 11 | `c5200_0000` 1410700 (203.99, −383.68, 51.5); arms `c5201_0000…0004` 1410710–714 (163.18, −383.81, 5…−18); tails `c5202_0000…0004` 1410720–724 (157.35, −383.81, 5…−19) | 520000 / 520100 / 520200 | Boss, A.6.3; limb copies parked off to the side, disabled until severed | Arm copy 1000 souls |

## A.6 Bosses — Demon Ruins

### A.6.1 Ceaseless Discharge (`c5250_0000`, ent 1410600, NameID 5250)

- Arena: the lava hall; fog `o4961` 1411790 (249.0, −283.15, 70.4) at its entrance; BGM box 1412690 (334.8, −302, 21.1) 160×40×210 (sound 1413801 = BGM 140100000).
- Dormant state (event **11415372**): AI off, HP bar off, invincible. Wakes (AI on, HP bar NameID 5250) when the host is past the fog (11415373) **and** either picks up the Gold-Hemmed set (flag **51410180**) or damages him.
- Movement: AI path points "爛れ移動ポイント" A00–A12, C01 (1412700–1412730, along y −304.13 from (422, 38) to (291, 96)); "upper level" box 1412790 / "lower level" box 1412791; "flame-breath restraint" box 1412796 → AI command 1 when the player is inside after the pickup (11415378); "attack-restraint" 1412795 and "special-attack-only" 1412794 boxes.
- Phase: event **11415377** — on event message 300 from his AI, SpEffect 5132 "thermal power stage 1" → 5133 "stage 2" (powered-up flames) and AI slot-1 command −1.
- **Ledge-grab cheese (scripted)**: event **11415376** — if he is alive and inside box 1412799 (296, −305.13, 97.5) 12.7×5×20 (the cliff under the fog), he is short-warped to 1412797, plays anim **17006** (cling to the ledge), then warped to 1412798. Events **11415310** slots 0–4 chain: each needs the cling to have happened plus one player hit; when all five flags 11415310–11415314 are set, **11415379** force-kills him with souls (he falls). Wiki: run back to the fog after the pickup, he grabs the ledge, hit his arm repeatedly.
- Death (event **11410900**): flag 11410900, Handle Boss Defeat, backread removed, flag **11410800** (lava drain), fog removed, Central bonfire enabled + registered; if the Lordvessel is not yet placed (11800100 OFF) sets global flag **402** (cleared by 11410095 once the seal is removed; purpose unverified — Lordvessel-hint flag).
- Rewards: 20,000 souls (GameAreaParam 1410600), common event 970 → **Humanity** (lot 9000) + **Homeward Bone** (lot 9030). The Gold-Hemmed Black set is the pickup that triggers him, not a drop. NpcParam lot 52500000 is empty.
- World change: lava drain cutscene **140100** (event 11410800, played once to the host 5 s after the kill when inside box 1412530 (344.8, −315, 111) 250×50×250; flag 11410801); lava object 1411100 off; lava collision 1413100 → cooled 1413101; map sound 1413805 off; corpse 1411610 lootable; Taurus Demons 1410450–456 enabled.

### A.6.2 Demon Firesage (`c2231_0000`, ent 1410400, NameID 2230 "Demon Firesage")

- Behind the golden Lordvessel seal; fog `o4962` 1411410 (148.9, −341.04, 94.6) + exit fog `o4963` 1411412 (110.38, −341.0, 140.5). Fight trigger box 1412416; BGM box 1412410 (130.2, −342, 117.38) 59×21×30 → sound 1413803 (BGM 140100003).
- Event **11415342**: AI disabled until the host enters 1412416, then AI on + HP bar 2230. No intro cutscene.
- Event **11415345**: while the player is in the Firesage room, m14_00 characters directly above (Fair Lady 1400700, Eingyi 6160, Eingyi's bugs 1400411–415) are backread-removed; restored 5 s after the Firesage dies.
- Death (**11410410**): fogs 1411410/1411412 removed (+SFX 1411411/1411413), lift starts working (11410400), Kirk #1 stops invading (11415035 ends).
- Rewards: 20,000 souls; NpcParam lot 22310000 **Demon's Catalyst**; common 970 → **Humanity**. (ItemLot 2670 is *named* "[Firesage Demon] Orange Charred Ring" in the community names but is awarded by the Centipede event — see A.6.3.)
- Wiki: optional only via a speedrun skip; required otherwise.

### A.6.3 Centipede Demon (`c5200_0000`, ent 1410700, NameID 5200 "Centipede Demon")

- Arena: lava lake; fogs `o4964` 1411890 (167.3, −383.48, 80.4) (entry) and `o4965` 1411892 (216.2, −383.49, 58.6) (exit). BGM box 1412890 → sound 1413802 (BGM 140100002).
- Intro (event **11415382**): first time only (flag 11410002), when the host enters 1412896 and is not a black phantom/invader: cutscene **140120** (skippable single-player, unskippable in co-op), then the boss is enabled, object `o4450` ent 1411110 (222.6, −341.32, 23.19) is disabled (pre-fight prop, presumably the centipede seen from Demon Ruins Central above; a twin `o4451` is parked at y +27.7 — unverified), AI on, HP bar 5200.
- **Severable limbs** (event 11415385 → 11415350 / 11415360): two NPC-part groups, each can be severed up to **5 times** (they regrow):
  - Group 1 "Arm" (NpcParam 520100): NPC parts 5200–5204, part HP 125 each; on sever: EzState 1001, wait event msg 400 → enable the next `c5201` copy (1410710→714), warp it to boss dummypoly **140**, anim **8100** (falling limb); boss DispMask bit 0 off, HitMask bit 1 on, AI cmd 10 (slot 0). Regrows on event msg 300 (DispMask on, HitMask off, cmd −1).
  - Group 2 "Tail" (NpcParam 520200): parts 5205–5209, part HP 95; copies `c5202` 1410720→724 at dummypoly **141**, anim 8100; DispMask bit 1 / HitMask bit 2; regrow on event msg 600.
  - When the boss dies, any severed copy still alive is force-killed with souls (11415320).
- **Orange Charred Ring** (event **11415386**): awarded (lot **2670**, ring 139, pickup flag 50001670) the first time the boss **or any severed limb copy** dies, to an alive/hollow (non-phantom) player. Wiki: obtained by cutting off a limb or by killing the boss.
- Solaire can be summoned (A.7); his phantom walks through the fog with the host (11415032).
- Death (**11410901**): fogs removed (+SFX 1411891/1411893), boss disabled after 10 s; enables Solaire's after-Centipede spot (11410530).
- Rewards: 40,000 souls; common 970 → **Humanity** + **Homeward Bone**; Orange Charred Ring as above. NpcParam lot 52000000 empty.

## A.7 NPCs — Demon Ruins

| NPC | Part / ent | npc / think / talk / chrInit | Where | What happens | Pose / anims |
|---|---|---|---|---|---|
| **Knight Kirk** (invasion #1) | `c0000_0008` ent **6560** | 6561 / 6560 / – / 6561 | spawn point 1412001 (255.93, −308.66, 81.01); trigger box 1412010 (248.1, −314.0, 70.7) | Event **11415035**: host, Human (Alive), Kirk not killed (11410810 OFF), first time inside 1412010 → `Place NPC Summon Sign` **black sign** (invasion) for 6560, flags 11415036/11415037; re-tries every 20 s. Stops permanently when the **Firesage dies** (11410410). Killed → flag 11410810 (event 11410810 slot 0) → no more invasions | Equipment (CharaInit 6561): Barbed Straight Sword, Spiked Shield, Thorns set (see 6562). Drop lot 6560 (33 % Barbed Straight Sword, chained 6561 Spiked Shield). Humanity value base 8956 |
| **Knight Solaire** (summon phantom for Centipede) | `c0000_0007` ent **6542** | 6543 / 6542 / – / 6543, team 7 (white phantom) | sign spawn 1412000 (156.86, −383.2, 96.92) | Events **11415030** (+ DSR-only variant 11415029 with a multiplayer-count check and SpEffect 28): host, Human, Solaire not hostile (1004 OFF), Solaire stage **1007** ON (talked to at the Sunlight Altar per wiki), Bed of Chaos not dead (flag 10 OFF), player within 20 m of 6542 → white NPC sign, flags 11415031 (summoned) / 11415033 (dismissed). 14015990 erases the sign. Summoned + Centipede fight started → he walks to 1412898 and does the fog-walk anim 7410 (11415032) | default |
| The Fair Lady / Eingyi | m14_00 (`c5340_0000` 1400700, `c3210_0000` 6160) | 534000 / 321001, talk 140615 / 140616 | Daughter of Chaos chamber (134–136, −252.5, 92–103) | Chaos Servant covenant (03 doc). Relevant here: joining = covenant 9; offering Humanity (ESD covenant points 10 → rank 1 **Chaos Fire Whip** flag 11400597, 30 → rank 2 **Chaos Storm** flag **11400598** → unlocks the Demon-Ruins side of the Izalith door 1411340); ESD also checks 80/100 points (unverified meaning). Join gift **Great Chaos Fireball** (11400596). Fair Lady also reinforces Estus with Fire Keeper Souls (ESD). Backread-removed during the Firesage fight | — |

## A.8 Items / treasures — Demon Ruins (route order)

| Where | Item | Lot / flag | Part |
|---|---|---|---|
| D2 upper walkway | Large Soul of a Brave Warrior | 1410190 / 51410190 | o0500_0019 (382.2, −278.15, 59.57) |
| D2 upper walkway | Large Soul of a Brave Warrior | 1410200 | o0500_0020 (374.55, −278.14, 73.35) |
| D2 far end (altar corpse) | **Gold-Hemmed Black Hood + Cloak + Gloves + Skirt** (triggers CD) | 1410180–183 / 51410180 | o0504_0000 (401.48, −277.18, 13.51) |
| D3 lava (pre-drain needs fire res.) | **Chaos Flame Ember** | 1410530 | o0500_0043 (425.28, −303.99, 67.19) |
| D3 lava | Large Soul of a Proud Knight | 1410060 | o0500_0006 (386.0, −303.21, 100.0) |
| D3 lava bed (lootable only after drain) | Soul of a Proud Knight | 1410050 | o0500_0005 ent 1411610 (301.12, −304.2, 111.67) |
| D3 | empty corpses | 1410040, 1410070 | (315.56, −303.02, 153.49), (291.0, −303.73, 88.77) |
| D4 | Soul of a Proud Knight | 1410080 | (243.99, −334.03, 25.59) |
| D4 | Soul of a Proud Knight | 1410510 | (218.22, −333.89, 22.97) |
| D4 | Green Titanite Shard ×2 | 1410090 | (210.87, −333.86, 3.34) |
| D4 | Soul of a Brave Warrior | 1410140 | (193.16, −343.31, 79.19) |
| D4 | Soul of a Brave Warrior | 1410160 | (187.54, −346.52, 107.8) |
| D4/D5 | empty corpse | 1410150 | (158.0, −344.29, 63.09) |
| D5 Rockworm room | **Large Flame Ember** (chest, ObjAct 11410601) | 1410100 | o0110_0001 ent 1411651 (129.02, −354.51, −4.42) |
| D5 | empty corpses ×3 | 1410110/120/130 | (120–130, −354.5, −0.5…10.5) |
| D6 Firesage room | Large Soul of a Brave Warrior ×2 | 1410210, 1410220 | (126.3, −341.02, 117.5), (122.0, −341.24, 135.0) |
| D7 stairs | Soul of a Brave Warrior | 1410230 | (103.77, −328.16, 163.05) |
| D7 lift bottom | Large Soul of a Brave Warrior | 1410240 | (117.0, −319.19, 121.5) |
| D7 below bonfire | Soul of a Brave Warrior | 1410250 | (122.1, −366.24, 120.1) |
| D8 before fog | Large Soul of a Brave Warrior | 1410260 | (164.0, −383.46, 93.0) |
| D8 arena alcove | Green Titanite Shard ×2 | 1410270 | (133.89, −383.46, 40.64) |
| Boss / enemy drops | Demon's Catalyst (Firesage), Orange Charred Ring (Centipede limb/boss), Humanity ×3 + Homeward Bone ×2 (boss common 970), Burrower Green Titanite Shards | see A.6 | |

## A.9 Interactive objects & scripted events — Demon Ruins

| What | Object / region ent | Event | Behaviour |
|---|---|---|---|
| Golden Lordvessel seal | `o4960` 1411710 (+SFX 1411711), action region 1412660 | 11410095 | If 11800100 (Lordvessel placed) → seal removed permanently (flag 11410096), global 402 OFF. Else action "Examine brilliant light" (10010410) → message "Sealed by the Great Lord's power" (10010630) |
| CD fog | `o4961` 1411790 | 11415370/71/73, removed by 11410900 | Standard boss fog: action "Traverse the white light" (10010403), rotate + anim 7410 fog-walk; white phantoms via 11415371; DSR co-op re-entry 11415843/11415846 |
| Lava layer + drain | `o4500` 1411100; collisions 1413100 → 1413101 | 11410800 | Cutscene 140100; permanent (11410801) |
| Firesage fogs | `o4962` 1411410, `o4963` 1411412 | 11415340/41/43, removed by 11410410 | — |
| **Lift** Firesage stairs ↔ Quelaag's Domain | `o4600` 1411400; plates 1412403 (bottom, y −318.27) / 1412402 (top, y −251.63); call spheres 1412401 / 1412400 (r 50) | 11410400 | Waits for **Firesage dead**. Flag 11410401 = car at lower landing. Standing on a plate rides it (obj anim 1 = up, 3 = down, SFX 140002), standing within 50 m of the far landing calls it; settle anims 4/5 once players leave the plate. Event 11410402 (locked message) is not initialised |
| Centipede fogs | `o4964` 1411890, `o4965` 1411892 | 11415380/81/83, removed by 11410901 | — |
| Centipede prop | `o4450` 1411110 | 11415382 | Enabled before the intro, disabled after cutscene 140120 |
| Co-op-only fogs | 1411994, 1411996, 1411998, 1411988, 1411986, 1411984, 1411982 | event 0 | Disabled for host/single-player; phantoms are blocked |
| Burrower ambush | region 1412302 | 11415100, 11415120 | Burrowers wait in anim 9000; distance / area triggers set cancel anim 9060 |
| Egg-carrier hatch | carriers 1410200–213 | 11415250 | 5 bugs per carrier on death |
| Black-phantom invasion points (online) | 1414000 (223.7, −277.5, 125.1), 1414100 (215.6, −324.3, 16.5), 1414200 (130.0, −383.2, 124.3) | engine | Player invader spawn spots |
| Cutscenes | 140100 (lava drain), 140120 (Centipede intro) | | |

## A.10 Porting notes — Demon Ruins

- **Lava**: collision material HitMtrl 27 "Lava" applies SpEffects **4030** "Lava (No Orange Charred Ring)" + **4031** "Lava (Base)" every 0.5 s (fire attack). The Orange Charred Ring (ring 139) removes the 4030 part. DS2 needs a lava floor damage equivalent and a ring-based reduction.
- **Lava drain world-state**: one MSB holds both the lava layer object (`o4500` 1411100, positioned at the map origin) and two alternative floor collisions (1413100 lava / 1413101 cooled). Port as two map-part states toggled by a flag; the 7 lava-bed Taurus Demons and corpse 1411610 hang off the same flag.
- **Ceaseless Discharge ledge cheese** is purely EMEVD (area trigger + forced anim 17006 + 5-hit counter + force death). Needs a DS2 event equivalent or it will be lost.
- **Centipede limb system**: NPC-part HP + separate "severed limb" characters warped to dummypolys 140/141, 5 regrows each; the ring is awarded by an event, not a drop. DS2 has a tail-cut mechanism but not this multi-copy limb pattern.
- **Lordvessel seal** (global flag 11800100 from m18_01 Firelink Altar).
- **Lift** requires a DS2 elevator object; the top landing lives in m14_00 (behind breakable wall 1401200).
- **Kirk invasion** = scripted black NPC sign; DS2 has scripted NPC invaders (good match). Invasion ends when the Firesage dies.
- **Firesage backread trick** (removing m14_00 NPCs above the arena) — likely unnecessary in DS2 but keep the NPC positions separate.
- Black World Tendency phantoms (5 Capras): DS2 has no world tendency; either drop or drive from a custom flag.
- Vagrants (c3490/c3491): online-item system; no DS2 equivalent — candidate to omit.

---

# PART B — LOST IZALITH (m14_01_00_00)

## B.1 Header

- From the Centipede Demon exit fog (216.2, −383.5, 58.6) → first Lost Izalith bonfire (229.5, −383.5, 91.5).
- Prerequisites: Lordvessel (for the Demon Ruins seal) unless entering via the Chaos Servant door; Bed of Chaos is a Lord-Soul boss.
- Alternative entry: **Chaos Servant door** `o4700` 1411340 (239.64, −367.78, 158.02) from the Demon Ruins side (covenant + rank 2); bypasses Firesage, Centipede and the lava field into the bug room.
- "Lost Izalith reached" sphere **1412510** (547.32, −440.11, 416.09) r = **360** (centred on the BoC arena) drives Solaire's fate and the Chaos-Eater killplane. The bug room (≈ 408 m from the centre) and the Centipede bonfire (≈ 458 m) are outside it; the lava field, towers, ruins and Titanite Demon bridge are inside (computed from MSB coords).
- Second player warp point: `c0000_0006` ent 1410980 (515.4, −398.23, 389.25) on the slide down to the BoC arena (use unverified).

## B.2 Sub-zones (route order)

| # | Sub-zone | Rough bounds | Content |
|---|---|---|---|
| I1 | Bonfire after Centipede | (215–230, −383.5, 58–95) | bonfire, Solaire (post-Centipede), 2 Large Souls |
| I2 | Lava field ("dragon-butt" field) | x 295–510, y −383…−384, z 100–270 | 29 Bounding Demons, Divine Blessings ×2, 2 Souls of a Brave Warrior |
| I3 | First tower | (370–392, −380, 158–192) | chest Soul of a Great Hero, 2 Large Souls, 4 Stone Demons, Vagrant |
| I4 | Second tower + hidden bonfire | (450–462, −371…−380, 160–190) | illusory wall + bonfire, Twin Humanities, Large Soul, black invasion point 300 |
| I5 | Izalith ruins | x 365–460, y −359…−380, z 270–365 | 23 Stone Demons, Chaos Eaters, Chaos Fire Whip chest, Rare Ring of Sacrifice, Soul of a Hero |
| I6 | Titanite Demon bridge | corridor y −360 from (296, 205) to (390, 284) | Titanite Demon (patrols the corridor), Crystal Lizard |
| I7 | Bug room / Solaire | (215–270, −367.7, 130–180) | 9 Chaos Bugs (one with the Sunlight Maggot), Chaos Servant door, Solaire (sane or hollow) |
| I8 | Collapsing floor + Chaos Eater pit | floor (431, −365.5, 318.7); pit y −384 (405–460, 285–340) | breakable floor, 4(+1, +3 BWT) Chaos Eaters, Siegmeyer above, Red Titanite Slab chest |
| I9 | Bed of Chaos approach | stairs (425–455, −354…−364, 315–345) | Daughter of Chaos NPC, Kirk invasion #2, BoC fog, slide |
| I10 | Bed of Chaos arena | (510–590, −436…−444, 385–450) | Bed of Chaos |

## B.3 Progression walkthrough

1. **I1** Bonfire (229.5, −383.5, 91.5). After the Centipede dies Solaire sits here (B.7). Large Souls (216.11, −383.49, 67.62) and (224.79, −383.49, 78.09).
2. **I2** Cross the lava field (Orange Charred Ring recommended). 29 **Bounding Demons** ("Undead Dragon – Legs") stand in the lava (non-respawn). Souls of a Brave Warrior at (299.26, −383.32, 214.12) and (361.04, −383.05, 217.24). Ambient lava-sound region "溶岩サウンド3" (362.8, −386.5, 163.6), no entity. Two **Divine Blessings** lie in the lava at (460.09, −383.71, 108.0) and (457.39, −383.70, 106.5).
3. **I3** First tower: chest **Soul of a Great Hero** (384.4, −380.03, 190.3), Large Souls (376.77, −380.39, 186.63) and (388.36, −380.03, 188.3), Stone Demons `c2430_0027–0030` (372–461, −380.87, 158–190), Vagrant (382.75, −380.03, 187.2).
4. **I4** Second tower: hit the illusory wall `o4510` 1411360 (456.18, −380.03, 175.22) (event 11410360 remembers it) → **hidden Lost Izalith bonfire** (455.6, −380.0, 169.3). Up the tower: **Twin Humanities** (458.11, −371.25, 186.68), Large Soul (459.65, −371.2, 165.3).
5. **I5** Into the Izalith ruins (Stone Demons everywhere at y −363…−378). Red Titanite Chunk (450.59, −373.94, 303.49); **Rare Ring of Sacrifice** (381.54, −371.82, 310.88); **Soul of a Hero** (379.98, −369.06, 336.75); Large Soul (385.82, −378.13, 362.61). Upper ledge: chest **Chaos Fire Whip** (452.49, −359.75, 281.82) guarded by a Chaos Eater (454.58, −359.75, 288.96). Chaos Eaters also at (411.99, −360.26, 357.21) and (458.05, −373.93, 306.46).
6. **I6** Titanite Demon bridge (y −360): Titanite Demon (341.91, −360.18, 243.65) uses AI regions "徘徊デーモン" — aggressive strip 1412740 (342.6, −360.3, 244.3) 8×2×106 and passive ends 1412741 (296, −360.3, 205) / 1412742 (390.3, −362.9, 283.9). **Crystal Lizard** (285.15, −360.18, 196.15) at the west end. Leads to the bug room (wiki).
7. **I7** Bug room: 9 Chaos Bugs (222–267, −367.73, 146–177), the red-eyed one `c3480_0000` ent 1410100 (243.0, −367.73, 161.0) carries the **Sunlight Maggot**. Bug AI uses "burrowing maggot" regions 1412310–1412314 (bugs dig into holes) and "surprise maggot" boxes 1412300/1412301. **Chaos Servant door** 1411340 on the far side (opens freely from this side → one-way shortcut back to the Demon Ruins). Solaire ends up here (B.7).
8. **I8** Back in the ruins, the **breakable floor** `o4810` 1411350/1411351 (435.51, −367.88, 322.33) collapses when the player steps into box 1412350 (431.0, −365.5, 318.7) 11×1×5 or it is broken (event 11410350, SFX 140001, sound 481000001). The corpse `o0500_0000` ent 1411600 lying on it falls with it (obj anims 115 loop → 116) and becomes lootable in the pit: **Red Titanite Chunk** (440.44, −384.17, 327.59). Pit (y −384): 4 scripted Chaos Eaters + 1 respawning one; Soul of a Brave Warrior (444.5, −383.81, 332.2), Green Titanite Shard ×2 (450.7, −384.07, 328.6); dead-end chest **Red Titanite Slab** (409.25, −384.2, 288.75). **Siegmeyer** waits in the room above (446.21, −375.97, 330.81) (B.7).
9. **I9** Stairs to the boss: **Daughter of Chaos** hostile NPC (434.54, −354.12, 322.68). **Kirk invasion #2** trigger box 1412520 (425.31, −360.32, 338.03), spawn 1412002 (437.64, −354.12, 316.04). Black invasion point 301 (441.95, −355.0, 327.75).
10. **BoC fog** `o4972` 1411990 (453.7, −363.73, 337.62) (action region 1412998, target 1412997). Beyond it the player slides down a long root slope (slide-direction box 1413300 (454.8, −362.5, 338.3); the slide box **1412110** (495, −433, 372) 6×70×100 makes the player **invincible while sliding + 3 s**, event 11415399) into the arena.
11. **I10** Bed of Chaos (B.6). After the kill, bonfire 1411950 appears in the arena.

## B.4 Bonfires
See A.4: Lost Izalith (after Centipede) 1411964, hidden Lost Izalith 1411960 (illusory wall 1411360), Bed of Chaos 1411950 (post-boss).

## B.5 Enemies — Lost Izalith

| Sub-zone | Enemy | Model | n | MSB parts (ent) | npc / think | Behaviour & notes | Drops |
|---|---|---|---|---|---|---|---|
| I2 | Bounding Demon of Izalith ("Undead Dragon – Legs") | c3421 | 29 | `c3421_0000…0028` (1410350–1410378) at (324–506, −383.0…−383.76, 133–268) | 342101 / 342100 | **Non-respawn** (11410100 slots 9–37) | 2000 souls, lot 34210100 (none) |
| I3 / I2 edge | Stone Demon | c2430 | 4 | `c2430_0027…0030` (372.4–460.7, −380.87, 158–190) | 243001 / 243000 | resp | 300 souls |
| I3 | Vagrant | c3490 | 1 | `c3490_0003` 1413930 (382.75, −380.03, 187.2) | 349012 | engine/online | — |
| I5 | Stone Demon | c2430 | 23 | y −362.9…−365.4: `0014, 0015, 0016, 0020, 0021, 0024, 0043, 0044`; y −368.5: `0025, 0026`; y −377.8…−378.1: `0018, 0022, 0023, 0033–0042` — all within (370–439, 273–362) | 243001 / 243000 | resp | 300 souls |
| I5 | Chaos Eater | c3240 | 3 | `c3240_0001` (411.99, −360.26, 357.21) think 324002; `0007` (458.05, −373.93, 306.46); `0008` (454.58, −359.75, 288.96) think 324000 (Chaos Fire Whip chest) | 324000 | resp. Killplane for model 3240 at y < −400 while the player is in sphere 1412510 (event 11410260) | 1000 souls, lot 32400000 (10 % Red Titanite Chunk, 0.3 % White Titanite Chunk ×2, 0.2 % Red Titanite Slab) |
| I5 | Vagrant pair | c3491 | 2 | `c3491_0004` 1413931 + `c3491_0007` 1413932 (378.85, −369.03, 313.91) | 349112 / 349212 | engine/online | — |
| I6 | Titanite Demon ("Prowling Demon") | c2300 | 1 | `c2300_0000` ent **1310150** (341.91, −360.18, 243.65) | 230005 / 230005 | Corridor patrol via AI regions 1412740–742. **Respawns** (11410100 watches 1410150 instead) | 5000 souls; lot 23000500 (2 % Titanite Catch Pole) + 23000501 (**Demon Titanite ×2**) |
| I6 | Crystal Lizard | c3300 | 1 | `c3300_0000` 1410110 (285.15, −360.18, 196.15) | 330020 / 330000 | **Non-respawn**; on death awards lot **33002000** (Twinkling Titanite ×2) + 33002001 (random Green Shard / Red Chunk) (11410100 slot 7) | — |
| I7 | Chaos Bug (Sunlight Maggot carrier) | c3480 | 1 | `c3480_0000` **1410100** (243.0, −367.73, 161.0) | **348001** / 348000 | One-time: event 800 → if dead, its treasure is forced + disabled (flag **800** = maggot bug killed) | 100 souls, lot **34800100 Sunlight Maggot** (flag 51410990) |
| I7 | Chaos Bug | c3480 | 8 | `c3480_0001…0008` (222–266.5, −367.73, 146–177) | 348000 / 348000 | resp; burrow AI regions 1412310–314 | 30 souls, lot 34800000 (3 % Sunlight Medal, 0.5 % Red Titanite Chunk) |
| I7 | Knight Solaire (hollow) | c0000 | 1 | `c0000_0003` ent **6004** (250.15, −367.73, 166.81) | 6004 / 6000 / talk 141658 / chrInit 6004 | See B.7 | |
| I8 pit | Chaos Eater (Siegmeyer's) | c3240 | 4 | `c3240_0002…0005` (1410410–1410413) (435.9–440.5, −384.2, 321–330) | 324000 / 324001 | **Non-respawn** (event 50 / 11410100 slots 0–3); drive Siegmeyer's quest | as above |
| I8 pit | Chaos Eater | c3240 | 1 | `c3240_0006` (421.63, −384.2, 337.2) | 324000 / 324001 | resp | |
| I8 pit BWT | Chaos Eater (black phantom) | c3240 | 3 | `c3240_0009` 1410905 (444.05, −384.12, 336.61), `0010` 1410906 (436.5, −384.1, 317.5), `0011` 1410907 (430.5, −384.2, 333.5) | **324001** / 324000 | BWT only (11415090–92) | |
| I8 | Siegmeyer of Catarina | c0000 | 1 | `c0000_0005` ent **6286** (446.21, −375.97, 330.81) | 6284 / 6280 / talk 141628 / chrInit 6284 | B.7. **InitAnim 7855** | |
| I9 | Daughter of Chaos (hostile NPC) | c0000 | 1 | `c0000_0010` ent **6620** (434.54, −354.12, 322.68) | 6620 / 6620 / – / 6620 | Hostile; Gold-Hemmed Black set, Pyromancy Flame+5, NPC spells 14500/14510/14520 (Great Chaos Fireball / Chaos Storm / Chaos Fire Whip variants). **Non-respawn** (11410100 slot 6). Humanity value 8250 | 5000 souls, lot **6620 Izalith Catalyst** (flag 50006620) |
| I9 | Knight Kirk (invasion #2) | c0000 | 1 | `c0000_0009` ent **6561** spawn 1412002 (437.64, −354.12, 316.04) | 6562 / 6561 / – / 6562 | B.7 | lot 6560 |
| I10 | Bed of Chaos (tree body) | c5230 | 1 | `c5230_0000` 1410801 (564.88, −440.77, 429.78) | 523000 | **InitAnim 9000** (dormant) → anim 9060 when the host enters the arena | — |
| I10 | Bed of Chaos Arm | c5400 | 1 | `c5400_0000` 1410800 (569.88, −440.11, 435.68) | 540000 | Enabled after the first seal breaks; Handle-Boss-Defeat entity | — |
| I10 | Bed of Chaos Worm (core / HP-bar target) | c5401 | 1 | `c5401_0000` 1410802 (582.0, −443.9, 445.97) | 540100 (HP 1) | Invincible until both seals break | — |
| I1 | Knight Solaire (post-Centipede) | c0000 | 1 | `c0000_0002` ent **6002** (221.45, −383.25, 93.51) | 6003 / 6000 / talk 141600 / chrInit 6003 | **InitAnim 7825** (seated at the bonfire) | lot 6000 chain |

## B.6 Boss — Bed of Chaos (NameID 5400 "Bed of Chaos")

- Parts: body `c5230_0000` 1410801, arm `c5400_0000` 1410800, core worm `c5401_0000` 1410802 (HP bar target). Arena centre (547.32, −440.11, 416.09); "boss room logic" box 1412996 (550.3, −442.1, 418.6) 100×2.5×100; BGM sphere 1412990 r 42 → sound 1413800 (BGM 140100001). Lock-cam slot 1 during the fight.
- Fog `o4972` 1411990 (453.7, −363.73, 337.62); entry events 11415390/91/93; slide-invincibility 11415399.
- AI geometry: 29 floor-grid boxes "イザリス部屋 エリア0–28" (1412600–1412628), "lower-body a3000–a3006" boxes (1412550–1412564) and "upper-body a3006–a3011" boxes (1412570–1412588) for attack selection.
- **Two seals** ("イザリス_封印" left/right): `o4610_0000` **1411121** (572.64, −439.71, 423.55) → flag **11410291**; `o4610_0001` **1411122** (558.36, −439.70, 440.42) → flag **11410292** (event 11415300; SFX 1413400/1413410 resp. 1413401/1413411 removed on break). Once broken they stay broken across deaths.
- Sequence (event **11415396**): until a seal breaks the arm 1410800 is disabled; first seal → cutscene **140116** (if 1411121 first, sets 11410005) or **140115** (if 1411122 first) and the arm is enabled; both seals → SpEffect **5130** "arm release motion change" on the arm, and (first time, flag 11410000) cutscene **140117** (if 1411121 was first) or **140118**; AI commands 2 (one seal) / 3 (both) to body and arm (11415398).
- **Collapsing floors**:
  - 13 outer floor tiles `o4620…o4632` (ents 1411210–1411222, sounds 462000000–463200000): after either seal breaks, each tile breaks (SFX 140008) when the player comes within **8 m**, and is gone 10 s later (event 11410201 slots 0–12; persisted per slot).
  - 5 central floor pieces `o4633…o4637` (1411200–1411204): invulnerable until both seals break; then when the player enters sphere 1412100 (r 14) at the centre they shatter in sequence (SFX 140009, sounds 463300000–463700000) — the central pit (event 11410200, flag 11410200).
- **Root cage**: 48 roots `o4840_0000…0047` (1411250–1411297) around the core (557–582, −436…−444, 423–445) are invulnerable until both seals break (event 11410250), then breakable to reach the worm.
- Core (event **11415397**): 1410802 invincible until both seals; when its HP hits 0 → arm (with souls) and body force-killed.
- Death (event **11410001**): global flag **10**, Handle Boss Defeat (1410800), achievement 36, fog removed, bonfire 1411950 appears. Common 970 → lot **2580 Lord Soul** + **Humanity**. 60,000 souls (GameAreaParam 1410800).
- World changes after the kill (flag 10): Kirk #2 and Solaire's summon sign stop (11415038/11415030 end), BoC area re-entry handled by 11415392 (all parts dead), Quelana's "Fire Tempest" gift (lot 1300, Blighttown doc), Lord Soul for the Firelink Altar.

## B.7 NPCs — Lost Izalith

### Knight Solaire — the Sunlight Maggot ending (Solaire flags 1000–1029)

Entities: post-Centipede 6002 (`c0000_0002`, talk 141600, InitAnim 7825, team 2), hollow 6004 (`c0000_0003`, talk 141658, team forced 9 "Hostile Ally" at load), summon 6542 (Part A). Humanity value base 8310.

| Step | Event | Conditions | Result |
|---|---|---|---|
| Load | event 50 | 6002 disabled unless flag 1009, 1004 or 1003; if 11410580 → 6002 warped to bug-room spot 1412500 (247.63, −367.73, 166.85) on floor collision 1413000; 6004 disabled unless **1002** | |
| Appears at the Izalith bonfire | 11410530 | 1004 OFF, **1007** ON (met at Sunlight Altar per wiki), **Centipede dead** (11410901) | state **1009**, 6002 enabled (seated, anim 7825) |
| Goes hollow | 11410531 | 1004 OFF, 1009 ON, **flag 800 OFF** (maggot bug alive), host, host inside sphere **1412510** | state **1002**, 6002 disabled, **6004 enabled** (hostile, wearing **Sunlight Maggot** + Orange Charred Ring, Sunlight Straight Sword/Shield/Talisman, Lightning Spear + Great Heal), save |
| Saved | 11410532 | 1004 OFF, 1009 ON, **flag 800 ON** (maggot bug already killed), host in sphere 1412510 | state **1003**, 6002 warped to 1412500 in the bug room, home set there, flag 11410580 |
| After talking in the bug room | 11410533 (param 6006) | 1004 OFF, 1003 ON, 11410595 (set by his ESD) | state **1012**, disables ent 6006 (another Solaire instance outside this map — next step of his quest, unverified) |
| Made hostile | 11410510 | 6002 hit to ≤ 90 % HP by the player | state **1004**, team 9, save |
| Killed (sane) | 11410520 | 6002 HP 0 | state **1005**; drops lot 6000 chain (Iron Helm, Armor of the Sun, Iron Bracelet, Iron Leggings, Sunlight Straight Sword, (6005) shield, Sunlight Talisman, White Sign Soapstone, Humanity) |
| Hollow killed | 11410534 | 1002 ON, 6004 HP 0 | state **1011**; same lot chain (wiki: 2 Humanity + Iron and Sun set + Sunlight weapons) |

Practical consequence (from the region math above + wiki): the maggot bug can only be killed before Solaire hollows by reaching the bug room without entering sphere 1412510 — i.e. via the **Chaos Servant door** from the Demon Ruins side (or the wiki's Poison Mist / White Dragon Breath through the closed door).

### Siegmeyer of Catarina (Siegmeyer flags 1490–1514; Izalith states 1503–1514)

Ent 6286, npc 6284, chrInit 6284 (Zweihander, Pierce Shield, Catarina set, Blue Tearstone Ring), talk 141628, team 2. Humanity value base 8446. **InitAnim 7855** (seated nap); event 50 re-applies waiting anim 7855 if 11410593.

| Step | Event | Conditions → result |
|---|---|---|
| Arrives | 11410540 | 1512 (hostile) OFF, **1502** ON (Blighttown stage), **11400590** (Blighttown event flag) ON, player in m14_01 → state **1503**, enabled, flag 814 ON |
| Wake-up cancel anim | 11410547 | on 1503 / flag 814 OFF → cancel anim **7856** |
| Charges the pit | 11410541 | 1503 + any of Chaos Eaters 1410410–413 alive + **11410590** (talked to him here, ESD) → state **1504**, team **12 Friendly Enemy**, SpEffect **90111** "poison disabled" (fights the Chaos Eaters) |
| Player cleared the pit first | 11410542 | 1503 + all 4 Chaos Eaters dead + Siegmeyer alive → state **1505** |
| Survived, HP < 50 % | 11410543 | 1504 + HP < 50 % + Chaos Eaters dead → state **1506**, home 1412360 (433.57, −384.17, 321.19, "charging onion's nest") |
| Survived, HP ≥ 50 % | 11410544 | 1504 + HP ≥ 50 % + Chaos Eaters dead → state **1507**, home 1412360 |
| Dies of wounds | 11410545 | 1506 + **11410591** (talked after the fight) → force death (with souls), state **1513** (on reload: corpse treasure) |
| Naps | 11410548 | 1507 + **11410593** (ESD) → waiting anim **7855** |
| Leaves (healthy) | 11410549 | 1507 + **11410594** + not loaded → disabled (continues to Ash Lake, Part D) |
| Leaves (player-cleared) | 11410550 | 1505 + 11410594 + not loaded → state **1514**, disabled |
| Hostile / killed | 11410501 / 11410546 | hit ≤ 90 % while 1503/1505/1506/1507 → **1512**; killed (not 1506/1508) → **1513** |

Gifts: flag **11410594** → lot **1050 Speckled Stoneplate Ring** (common event 911); death drop lot 6280 (Speckled Stoneplate Ring). Wiki outcome summary: >50 % HP → later reunion with Sieglinde at Ash Lake (Titanite Slab); <50 % → ring then dies; pit cleared by the player → ring, quest ends.

### Knight Kirk — invasion #2 (ent 6561)
Event **11415038**: host, Human, Kirk #2 not killed (11410811 OFF), first time inside box **1412520** → black NPC sign at 1412002; flags 11415039/11415040; retries every 20 s; **ends when Bed of Chaos is dead** (flag 10). CharaInit 6562 "Knight Kirk (Invader) – Lost Izalith": Barbed Straight Sword+15, Spiked Shield+15, Thorns set+5. Killed → 11410811. Drop lot 6560.

### Daughter of Chaos (hostile, ent 6620)
No talk, no enable/disable scripting beyond "stay dead" (11410100 slot 6). Stands on the stairs before the BoC fog. Drops **Izalith Catalyst** (lot 6620) + 5000 souls.

## B.8 Items / treasures — Lost Izalith (route order)

| Where | Item | Lot / flag | Part |
|---|---|---|---|
| I1 | Large Soul of a Brave Warrior ×2 | 1410280, 1410290 | (216.11, −383.49, 67.62), (224.79, −383.49, 78.09) |
| I2 lava | Soul of a Brave Warrior | 1410320 | (299.26, −383.32, 214.12) |
| I2 lava | Soul of a Brave Warrior | 1410310 | (361.04, −383.05, 217.24) |
| I3 tower | Large Soul of a Brave Warrior | 1410300 | (376.77, −380.39, 186.63) |
| I3 tower | Large Soul of a Brave Warrior | 1410350 | (388.36, −380.03, 188.3) |
| I3 tower chest | **Soul of a Great Hero** (ObjAct 11410600) | 1410500 | o0110_0000 ent 1411650 (384.4, −380.03, 190.3) |
| I2 lava | **Divine Blessing** ×2 (two corpses) | 1410330, 1410340 | (460.09, −383.71, 108.0), (457.39, −383.70, 106.5) |
| I4 tower | **Twin Humanities** | 1410360 | (458.11, −371.25, 186.68) |
| I4 tower | Large Soul of a Brave Warrior | 1410370 | (459.65, −371.2, 165.3) |
| I5 | Red Titanite Chunk | 1410010 | (450.59, −373.94, 303.49) |
| I5 upper ledge chest | **Chaos Fire Whip** (ObjAct 11410603) | 1410520 | o0110_0003 ent 1411653 (452.49, −359.75, 281.82) |
| I5 | **Rare Ring of Sacrifice** | 1410380 | (381.54, −371.82, 310.88) |
| I5 | **Soul of a Hero** | 1410400 | (379.98, −369.06, 336.75) |
| I5 | Large Soul of a Brave Warrior | 1410390 | (385.82, −378.13, 362.61) |
| I8 (falls with the floor) | Red Titanite Chunk | 1410000 | o0500_0000 ent 1411600 (440.44, −384.17, 327.59) |
| I8 pit | Soul of a Brave Warrior | 1410020 | (444.5, −383.81, 332.2) |
| I8 pit | Green Titanite Shard ×2 | 1410030 | (450.7, −384.07, 328.6) |
| I8 pit dead end chest | **Red Titanite Slab** (ObjAct 11410602) | 1410410 | o0110_0002 ent 1411652 (409.25, −384.2, 288.75) |
| Drops | Sunlight Maggot (bug 1410100), Izalith Catalyst (Daughter of Chaos), Demon Titanite ×2 (Titanite Demon), Twinkling Titanite (lizard), Lord Soul + Humanity (BoC), Speckled Stoneplate Ring (Siegmeyer), Solaire's set (if killed), Kirk's lot 6560 | | |

## B.9 Interactive objects & scripted events — Lost Izalith

| What | Ents | Event | Behaviour |
|---|---|---|---|
| Illusory wall → hidden bonfire | `o4510` 1411360 | 11410360 | Breaks on hit; flag 11410360 keeps it gone |
| Chaos Servant door | `o4700` 1411340; regions 1412201 (Demon-Ruins side, 238.2, −368.13, 156.38) / 1412200 (bug-room side, 241.2, −368.13, 159.38) | 11410340 / 11410341 | Action "Touch door" (10010510). Side 1412201 requires host + covenant 9 + **11400598**; side 1412200 has no requirement; clients may open from either. Opening: flag 11410340, player rotates + anim **7114**, door obj anim 1, SFX 140000. Locked attempt → "Locked by some contraption" (10010160). Client-only fog `o4971` 1411984 at the same spot |
| Collapsing floor | `o4810_0000` 1411350 (intact) / `o4810_0001` 1411351 (breaking) ; trigger 1412350 | 11410350 | See B.3 step 8 |
| Chests (4) | 1411650–1411653, ObjAct 11410600–603 | 11410600 slots | Standard chest |
| BoC fog | `o4972` 1411990 | 11415390/91/93 | Standard boss fog |
| BoC seals, floors, roots, core | 1411121/122, 1411210–222, 1411200–204, 1411250–297 | 11415300, 11410201, 11410200, 11410250, 11415397 | B.6 |
| Slide invincibility | region 1412110 | 11415399 | Player invincible inside + 3 s |
| Chaos-Eater killplane | — | 11410260 | Killplane y −400 for model 3240 while the player is in sphere 1412510 |
| Black invasion points | 1414300 (455.25, −371.25, 182.6), 1414301 (441.95, −355.0, 327.75) | engine | |
| Cutscenes | 140115, 140116, 140117, 140118 (BoC seals) | | |
| Unscripted objects | `o4350–o4353` ×82 at lava level (root walkways/rocks across the lava field), `o4360–o4362` ×63 (Demon Ruins), `o4830` ×52, `o4840` ×118 (70 without entity), `o4611` ×18 on the BoC slide (types unverified) | | |

## B.10 Porting notes — Lost Izalith

- **Bed of Chaos is an EMEVD-heavy set piece**: two seal objects, 13 proximity-breaking floor tiles, a 5-piece central floor collapse, 48 invulnerable-then-breakable roots, a 1-HP core with boss bar, a slide with invincibility, 4 one-shot cutscenes that depend on which seal broke first, and an "arm" character that only appears after the first seal. All persistence is per-flag (seals/tiles stay broken after death). DS2 needs breakable map objects with flags + scripted NPC parts.
- **Solaire's fate** hinges on a 360 m sphere centred on the BoC arena plus the one-time maggot-bug flag 800; preserve the geometry relationship (bug room outside the sphere, reachable only via the Chaos Servant door) or re-express the rule.
- **Chaos Servant door**: one-way-from-inside door with a covenant + rank check from outside. DS2 covenants differ — needs a custom covenant/flag stand-in.
- **Siegmeyer** is a friendly NPC that becomes a "Friendly Enemy" combatant (team 12) and jumps into the pit (AI home = 1412360); HP-ratio branches decide his fate. DS2 has ally NPC combat (e.g. Lucatiel), but HP-ratio branching needs events.
- **Collapsing floor** with a corpse riding it down (object anims 115/116).
- **Bounding Demons** (legs-only dragons) need a DS2 skeleton/model port; 29 non-respawning instances.
- **Lava**: same HitMtrl 27 lava as Demon Ruins (SpEffects 4030/4031; Orange Charred Ring).
- Daughter of Chaos NPC uses Gold-Hemmed Black + chaos pyromancies — DS2 pyromancy equivalents needed.
- Kirk #2 invasion (scripted NPC invader, ends after BoC).
- Black World Tendency phantoms (3 Chaos Eaters) — omit or remap.

---

# PART C — THE GREAT HOLLOW (m13_02_00_00)

## C.1 Header

- Reached from **Blighttown's swamp** (m14_00): Blighttown co-op fog `o4954` 1401998 (−322.07, −206.44, 83.0) and its m13_02 twin `o3970` ent 1321994 (−323.95, −207.58, 83.39) mark the seam. Wiki: entered through an illusory wall at the bottom of Blighttown. In m13_02 two hit-to-break walls sit right at the mouth: `o3450` ent **1321200** (−329.45, −207.47, 84.3) and `o3451` ent **1321201** (−338.72, −207.85, 89.41) (event 11320200 slots 0/1, flags 11320200 / 11320201 keep them broken). Which one is the Blighttown entrance vs. the Twin-Humanities alcove wall is unverified (the chest (−337.81, −207.68, 87.55) sits beside `o3451`).
- No key/flag requirement. Exit at the bottom: white-light checkpoint into Ash Lake.
- Spawn/warp point: `c0000_0001` ent 1320997 (−316.84, −236.08, 122.65) by the bonfire. Spawn point events 1322960/1322961/1322962 map to the three bonfires.

## C.2 Progression walkthrough (top → bottom)

1. Mouth (y −207): **Plank Shield** corpse (−328.8, −207.35, 86.6); chest **Twin Humanities** (−337.81, −207.68, 87.55) (ObjAct 11320600); two breakable walls (above). Basilisks just below (−336.05, −217.51, 90.71) and (−347.68, −220.21, 101.79).
2. **Great Hollow bonfire** (−319.0, −236.06, 122.5); ladder `o3240` 1321140 (−324.88, −235.49, 131.77) next to it; 8 "Register Statue" slots `o0101_0000…0007` (1321900–1321907) in a row at (−311…−297, −234, 119) (online petrified-player statue slots — unverified).
3. Descend the tree interior on branches (falling damage is the hazard): empty corpse (−337.5, −253.12, 120.0); **Cloranthy Ring** (−327.88, −259.0, 111.6); empty corpse (−332.0, −262.4, 138.0); Titanite Chunk (−331.9, −267.07, 117.1); Red Titanite Chunk (−319.91, −267.77, 104.69); Large Soul of a Nameless Soldier (−323.2, −272.74, 127.3). **Crystal Lizards** (random, C.4) between y −244 and −292. Basilisk trio at y −272.6.
4. Mid-tree: White Titanite Chunk (−346.15, −301.71, 116.1); Blue Titanite Chunk (−320.1, −310.77, 106.15); ladder `o3241` 1321141 (−313.75, −316.12, 118.41); Red Titanite Chunk (−333.6, −325.52, 113.7); ladder `o3242` 1321142 (−317.71, −333.24, 122.4); Blue Titanite Chunk (−324.4, −336.37, 112.2); ladder `o3243` 1321143 (−330.77, −343.4, 103.89); Large Soul of a Nameless Soldier (−338.2, −344.51, 117.4); White Titanite Chunk (−328.5, −352.54, 98.15). Basilisks ×6 at y −333…−344.
5. Lower tree: Titanite Chunk (−324.0, −376.95, 111.3); Mushroom Children/Parents (y −373…−393); Vagrant pair (−322.77, −376.81, 101.58); empty corpse (−328.4, −392.07, 106.9).
6. **White-light checkpoint** `o3971` ent 1321700 (−322.0, −393.25, 131.15), regions 1322600 (front) / 1322601 (back): one-time fog — "Traverse the white light" / "Enter through the white light", warp + anim 7410, then removed for good (event 11320090 slot 0). Beyond it: Ash Lake.

## C.3 Bonfires (m13_02)

| Name | Char part / ent | Object ent | Pos | Flag | Kindling | Talk |
|---|---|---|---|---|---|---|
| The Great Hollow | `c1000_0002` 1320962 | 1321962 | (−319.0, −236.06, 122.5) | 11320976 | 0 | 132002 |
| Ash Lake | `c1000_0001` 1320961 | 1321961 | (−387.95, −408.26, 154.85) | 11320984 | 0 | 132001 |
| Stone Dragon | `c1000_0000` 1320960 | 1321960 | (−700.0, −413.94, −323.5) | 11320992 | **10** (same marking as the Daughter of Chaos Firekeeper bonfire; "Stone Dragon" is a PTDE Lordvessel warp destination, place name 2008) | 132000 |

## C.4 Enemies — Great Hollow

| Zone | Enemy | Model | n | MSB parts (ent) | npc / think | Notes | Drops |
|---|---|---|---|---|---|---|---|
| Top | Basilisk | c3270 | 2 | `c3270_0000` (−336.05, −217.51, 90.71), `0001` (−347.68, −220.21, 101.79) | 327001 / 327000 | Curse-gas; resp | 400 souls, lot 32700100 (Eye of Death) |
| Upper | **Crystal Lizard** | c3300 | 10 | `c3300_0001…0010` (1320201–1320210) at (−317…−351, −244.8…−292.3, 109–143) | 330000 / 330000 | **Random spawn**: each load (host) event 11320300 picks one of 3 flags (e.g. 11325203–11325205) and only enables the lizard if the first is chosen → ≈ 1/3 chance each, re-rolled until killed. Killed → award lot **33000000** (Twinkling Titanite, + chained random material 33000001); non-respawn | — |
| Upper | Vagrant | c3490 | 1 | `c3490_0000` 1323900 (−327.6, −263.3, 109.9) | 349011 | engine/online | — |
| Mid | Basilisk | c3270 | 3 | `c3270_0002…0004` (−326.7…−336.7, −272.6, 130–137) | 327001 | resp | as above |
| Lower | Basilisk | c3270 | 6 | `c3270_0005…0010` (−317…−340, −333.1…−344.5, 100–118) | 327001 | resp | as above |
| Bottom | Mushroom Child | c2280 | 6 | `c2280_0000…0005` (−321…−335, −373.4…−392.8, 99–122) | 228001 / 228000 | resp | 50 souls |
| Bottom | Mushroom Parent | c2270 | 3 | `c2270_0000…0002` (−318…−326, −392.9, 102–122) | 227001 / 227000 | resp | 1000 souls |
| Bottom | Vagrant pair | c3491 | 2 | `c3491_0000` 1323901 + `c3491_0002` 1323902 (−322.77, −376.81, 101.58) | 349111 / 349211 | engine/online | — |

No bosses, no NPCs, no mimic in the Great Hollow.

## C.5 Items — Great Hollow (top → bottom)

| Item | Lot | Pos |
|---|---|---|
| **Plank Shield** | 1320000 | (−328.8, −207.35, 86.6) |
| **Twin Humanities** (chest) | 1320180 | (−337.81, −207.68, 87.55) |
| (empty) | 1320010 | (−337.5, −253.12, 120.0) |
| **Cloranthy Ring** | 1320020 | (−327.88, −259.0, 111.6) |
| (empty) | 1320030 | (−332.0, −262.4, 138.0) |
| Titanite Chunk | 1320040 | (−331.9, −267.07, 117.1) |
| Red Titanite Chunk | 1320190 | (−319.91, −267.77, 104.69) |
| Large Soul of a Nameless Soldier | 1320050 | (−323.2, −272.74, 127.3) |
| White Titanite Chunk | 1320070 | (−346.15, −301.71, 116.1) |
| Blue Titanite Chunk | 1320060 | (−320.1, −310.77, 106.15) |
| Red Titanite Chunk | 1320080 | (−333.6, −325.52, 113.7) |
| Blue Titanite Chunk | 1320090 | (−324.4, −336.37, 112.2) |
| Large Soul of a Nameless Soldier | 1320110 | (−338.2, −344.51, 117.4) |
| White Titanite Chunk | 1320100 | (−328.5, −352.54, 98.15) |
| Titanite Chunk | 1320120 | (−324.0, −376.95, 111.3) |
| (empty) | 1320130 | (−328.4, −392.07, 106.9) |

## C.6 Porting notes — Great Hollow
- Vertical tree descent with 4 ladders (`Register Ladder` 1321140–143, top/bottom disable flags 11320010–017) and branch drops; fall damage tuning matters.
- **Curse** (Basilisks) — DS2 has petrification/curse (basilisks exist in DS2), good mapping.
- Random-spawn crystal lizards (1-in-3 per load) need a DS2 random-flag event.
- 403 unscripted `o3500/o3501` objects (tree-interior branches/fungus, type unverified) are part of the walkable geometry dressing.
- Statue slots (Register Statue ×8) are an online feature — omit.

---

# PART D — ASH LAKE (m13_02_00_00)

## D.1 Header
- Reached through the white-light checkpoint at the bottom of the Great Hollow. Dead end (no other exits); warp via bonfires.
- Path of the Dragon covenant entrance = the Everlasting (Stone) Dragon at the far end.
- BGM region (−524.5, −444.1, −95.6) 240×49×790 (sound 130200000).

## D.2 Progression walkthrough
1. **Ash Lake bonfire** (−387.95, −408.26, 154.85) at the foot of the tree; Siegmeyer/Sieglinde spot right beside it (D.5). **Dragon Scale** (−320.2, −414.68, 77.4).
2. Sand shore going south-west: Vagrant (−433.89, −414.42, 51.61); **Dragon Scale** (−434.04, −410.32, 40.61); Maneater Clams (−470.0, −414.4, −23.9) and (−499.7, −413.8, −27.25). The **Hydra** (lake beast) guards the lake (−348.1, −415.4, −45.0) and relocates between two zones (D.4).
3. **Dragon Scale** (−521.05, −414.52, −97.01).
4. Hollow tree on the shore (env point "樹内-特殊" (−561.4, −411.3, −126.9)): Basilisks ×3 (−561…−565, −407.7…−413.4, −129…−152), Mushroom Parent (−569.45, −413.46, −131.86), **Great Magic Barrier** (−568.3, −407.81, −126.9).
5. Maneater Clam (−618.15, −415.14, −161.83); Vagrant pair (−710.18, −414.49, −266.41).
6. **Stone Dragon bonfire** (−700.0, −413.94, −323.5) and the **Everlasting Dragon** (−703.22, −409.68, −340.35) (D.5).

## D.3 Enemies — Ash Lake

| Enemy | Model | n | MSB parts (ent) | npc / think | Notes | Drops |
|---|---|---|---|---|---|---|
| Hydra | c3530 | 1 | `c3530_0000` 1320700 (−348.11, −415.42, −44.98) | 353000 / 353000 | D.4 | 10,000 souls; event award lot **35300000 Dragon Scale** |
| Hydra's Head (severed-head props) | c3531 | 7 | `c3531_0000…0006` 1320701–1320707 parked at (42.25…−17.75, −436.49, −145.18) | 353100 | Disabled; each is warped to its neck dummypoly when cut | — |
| Maneater Clam | c3250 | 3 | `c3250_0000` 1320100 (−470.01, −414.39, −23.88), `0001` 1320101 (−499.7, −413.8, −27.25), `0002` 1320102 (−618.15, −415.14, −161.83) | 325001 / 325000 | Waiting anim **9000** (closed) until player within 15/15/10 m or hits it → cancel anim 9060 (event 11325150) | 200 souls, lot 32500100 (Twinkling Titanite) + 32500101 (Purging Stone) |
| Basilisk | c3270 | 3 | `c3270_0011` (−561.37, −413.39, −130.91), `0012` (−561.13, −413.31, −129.22), `0013` (−565.36, −407.67, −152.23) | 327001 | in the shore tree | Eye of Death (lot 32700100) |
| Mushroom Parent | c2270 | 1 | `c2270_0003` (−569.45, −413.46, −131.86) | 227001 | shore tree | — |
| Vagrant | c3490 / c3491 | 3 | `c3490_0001` 1323910 (−433.89, −414.42, 51.61); `c3491_0001` 1323911 + `c3491_0003` 1323912 (−710.18, −414.49, −266.41) | 349011 / 349111 / 349211 | engine/online | — |
| Everlasting Dragon ("Stone Dragon") | c3450 | 1 | `c3450_0000` **1320800** (−703.22, −409.68, −340.35), talk **132647** | 345000 / 345000, team 2 | **Immortal** (event 50 `Set Character Immortality`); D.5 | — |
| Stone Dragon's Tail | c3451 | 1 | `c3451_0000` 1320801 (−696.45, −413.70, −342.38) | 345100 | Disabled until cut (D.5) | — |
| Siegmeyer (Ash Lake) | c0000 | 1 | `c0000_0004` ent **6288** (−381.68, −408.31, 152.39) | 6280 / 6280, chrInit 6280, talk 0 | **InitAnim 7900** | lot 6280 |
| Sieglinde of Catarina | c0000 | 1 | `c0000_0005` ent **6290** (−382.94, −408.14, 152.13) | 6291 / 6290, chrInit 6291, talk **132629** | **InitAnim 7860** | — |

## D.4 Mini-boss — Hydra (lake beast)

- Head system (event 11320110 → **11325110** slots 0–6): 7 NPC parts (part ids 3530–3536, groups 1–7) with **270 HP** each. When a part's HP reaches 0 by a player hit while the hydra is not in its "submerged" state (flag 11325120, set/cleared by event messages 500/600 via 11325121): the matching severed head `c3531` is enabled at neck dummypoly **91–97**, plays anim **8100**; hydra plays flinch **8000**; DispMask bit n off / HitMask bit n+1 on; SpEffect **5430–5436** set.
- **All 7 heads severed → hydra force-killed** with souls (event 11320101).
- **Relocation** (event 11325100): on AI event message 300 it dives and resurfaces at the other end of the lake: A→B = warp to 1322700 (−322.04, −415.42, −11.47), anim **3011**, warp to 1322710 (−605.58, −415.42, −45.32), home there, anim 9060; B→A = 1322701 (−635.22, −415.42, −36.69), anim **3014**, 1322711 (−339.09, −415.42, −2.82). Trigger zones for the AI: "A→B when PC in" 1322703 (−541.9, −422, −65.2) 170×30×150, "B→A when PC in" 1322704 (−427.5, −422, 48.5); territories A 1322705 / B 1322706; patrol points A-1…A-6 (1322751–756) and B-1…B-7 (1322761–767); long-range breath boxes 1322770/1322771.
- Death (event **11320100**): one-time award **lot 35300000 (Dragon Scale)**; on reload the hydra and heads stay disabled. No fog, no HP bar, no boss flag.

## D.5 NPCs & covenant — Ash Lake

### Everlasting Dragon / Path of the Dragon (talk t132647)
- Dormant: waiting anim **9000** until the host comes within **30 m**, then cancel anim **9060** (event 11325000).
- Action prompt "Pray to the everlasting dragons" (10010240). Joining (ESD JoinCovenant 13, "Join Covenant? (abandons former Covenant)") sets **11320581** → lot **1260 Dragon Eye** and **11320592** → lot **1261 Dragon Head Stone** (common events 911/890). Offering **Dragon Scales** (goods 1110) raises covenant points; at **≥ 30** → **11320593** → lot **1270 Dragon Torso Stone**; capped at 100 ("Cannot offer more. Well done."). ESD also branches at 10 and 80 points (messages "Covenant allegiance deepened" / "…Rank gained").
- **Prayer pose**: while the dialogue sets flag **11325030**, event 11320580 rotates the player to face the dragon and plays **7910** then loops **7911**; when the flag clears, **7912** (stand up).
- **Tail cut** (event 11325001): NPC part 3451 with 200 HP; when it breaks → dragon anim 8000, on event msg 400 the tail `c3451` (1320801) is enabled at dummypoly **19** with anim 8100, DispMask bit 0 off, AI command 20; awards lot **34510000 Dragon Greatsword** (alive/hollow player only). The tail is cut only once (flag of 11325001).
- The dragon cannot die (immortality); flag 11320800 would record its death.
- NG+ (common 8200): owning Dragon Eye / Dragon Head Stone / Dragon Torso Stone pre-sets 11320581 / 11320592 / 11320593.

### Siegmeyer & Sieglinde — Catarina questline ending (flags 1490–1539 Siegmeyer, 1540–1569 Sieglinde)
| Step | Event | Conditions → result |
|---|---|---|
| Siegmeyer present | event 50 / 11320534 | Siegmeyer not hostile (1512 OFF), Sieglinde not hostile/dead (1547/1548 OFF), Izalith state **1507** + nap flag **11410593** + Firelink flag **11020606** → state **1511**, ent 6288 enabled (InitAnim 7900) |
| Sieglinde arrives | 11320540 | Sieglinde **1545** (freed from the Duke's Archives crystal, unverified) + 11020606 + Siegmeyer 1511 → state **1546**, ent 6290 enabled (InitAnim 7860) |
| Talk to Sieglinde | ESD t132629 | sets 11320590 → lot **1250 Titanite Slab** (common 911) and 11320591. Her lines include "My father...all Hollow now...has been subdued." and "Oh, father...dear father..." (wiki: Siegmeyer is found hollowed here) |
| Both leave | 11320535 / 11320541 | 11320591 (or Sieglinde dead 1548) + not loaded → Siegmeyer disabled (state **1514**), Sieglinde disabled (state **1549**) |
| Sieglinde hostile / killed | 11320510 / 11320520 | hit ≤ 90 % → **1547** (team 9); killed → **1548** |
- Humanity value bases: Siegmeyer 8446, Sieglinde 8454.

## D.6 Items — Ash Lake

| Item | Lot | Pos |
|---|---|---|
| **Dragon Scale** | 1320140 | (−320.2, −414.68, 77.4) |
| **Dragon Scale** | 1320150 | (−434.04, −410.32, 40.61) |
| **Dragon Scale** | 1320160 | (−521.05, −414.52, −97.01) |
| **Great Magic Barrier** | 1320170 | (−568.3, −407.81, −126.9) |
| Drops / gifts | Dragon Scale (Hydra), Dragon Greatsword (tail), Dragon Eye + Dragon Head Stone + Dragon Torso Stone (covenant), Titanite Slab (Sieglinde), clam Twinkling Titanite / Purging Stone | |

## D.7 Porting notes — Ash Lake
- **Hydra**: 7-head NPC-part system + severed-head characters + teleport-dive between two lake zones driven by AI event messages and PC trigger boxes. DS2 has no hydra model; needs model/anim port and the EMEVD logic. Water is cosmetic for the player (shallow shore) — the hydra stands in deep water.
- **Everlasting Dragon**: immortal talking NPC with a cuttable tail (weapon reward) and a covenant ESD; plus the scripted player prayer animations 7910–7912. DS2 covenant system differs; Dragon Head/Torso Stones are transformation items with no DS2 equivalent.
- Stone Dragon bonfire carries kindling level 10 like Firekeeper bonfires (PTDE warp-list bonfire).
- Siegmeyer/Sieglinde reunion depends on flags from m14_01 (1507, 11410593), m10_02 Firelink (11020606) and the Duke's Archives (1545).

---

# Area completion checklists

### Demon Ruins (m14_01)
- [ ] Geometry: entrance landing, CD lava hall with **two floor states** (lava 1413100 / cooled 1413101) + lava layer object 1411100, Central, lower ruins, Firesage room, exit stairs, Centipede lava lake
- [ ] Lava damage material (SpEffects 4030/4031) + Orange Charred Ring negation
- [ ] Bonfires: entrance 1411961, Central 1411962 (**only after CD**), before-Centipede 1411963
- [ ] 14 praying Egg Carriers (InitAnim 9000) + 70 hatchling Chaos Bugs (event 11415250)
- [ ] Capra Demons 7 (+5 BWT), Taurus Demons 3 + 7 post-drain non-respawn, Burrowers 7 (buried 9000/emerge 9060, ceiling ambush 1412302, one-time Green Shard awards), Stone Demons 14, Vagrants
- [ ] **Ceaseless Discharge**: dormant until Gold-Hemmed pickup (51410180) or hit; stage-2 SpEffect; ledge-grab cheese (17006 + 5 hits); death → bonfire, flag 11410800, drain cutscene 140100, Taurus spawn; rewards 20k + Humanity + Homeward Bone
- [ ] Gold-Hemmed Black set altar corpse (o0504) + Chaos Flame Ember + Large Flame Ember chest + all souls/shards (A.8)
- [ ] Golden Lordvessel seal 1411710 (flag 11800100)
- [ ] **Demon Firesage**: fogs 1411410/1411412, AI wake at 1412416; Demon's Catalyst + Humanity; 20k
- [ ] Lift 1411400 to Quelaag's Domain (only after Firesage; top behind m14_00 wall 1401200)
- [ ] **Centipede Demon**: intro 140120, prop 1411110, arm/tail sever ×5 each with limb copies, Orange Charred Ring on boss or any limb death (lot 2670); 40k + Humanity + Homeward Bone
- [ ] Kirk invasion #1 (box 1412010, ends at Firesage death); Solaire summon sign 1412000 (1007, ends at BoC death)
- [ ] Co-op-only fogs (optional)

### Lost Izalith (m14_01)
- [ ] Bonfires: after-Centipede 1411964 (Solaire seated, anim 7825), hidden 1411960 behind illusory wall 1411360, BoC 1411950 (post-boss)
- [ ] Lava field + 29 non-respawn Bounding Demons; towers (Soul of a Great Hero chest, Twin Humanities, Divine Blessings in lava)
- [ ] Ruins: 27 Stone Demons (4 at the towers + 23 in the ruins), Chaos Eaters (respawning + Chaos Fire Whip chest guard), Rare Ring of Sacrifice, Soul of a Hero
- [ ] Titanite Demon bridge (respawning, Demon Titanite ×2) + Crystal Lizard (one-time Twinkling Titanite)
- [ ] Bug room: 9 Chaos Bugs incl. Sunlight Maggot carrier 1410100 (flag 800); Chaos Servant door 1411340 (one-way, covenant 9 + 11400598 from outside)
- [ ] Solaire ending logic (1009 → 1002 hollow / 1003 saved via sphere 1412510 + flag 800; hollow 6004 wears Sunlight Maggot)
- [ ] Collapsing floor 1411350/351 with falling corpse 1411600; Chaos Eater pit (4 scripted + 1 respawning); Red Titanite Slab chest
- [ ] Siegmeyer pit sequence (1503 → 1504 team 12 + SpEffect 90111 → 1506/1507 by HP; nap 7855; ring 11410594 → lot 1050)
- [ ] Daughter of Chaos hostile NPC (Izalith Catalyst); Kirk invasion #2 (box 1412520, ends at BoC death)
- [ ] BoC fog 1411990 + invincible slide 1412110
- [ ] **Bed of Chaos**: 2 seals (11410291/292), cutscenes 140115–140118, arm enable, 13 proximity floor tiles, central floor collapse, 48 roots, 1-HP core; flag 10; Lord Soul + Humanity; 60k; achievement; bonfire appears

### The Great Hollow (m13_02)
- [ ] Entrance seam from Blighttown + 2 breakable walls 1321200/1321201
- [ ] Bonfire 1321962; 4 ladders 1321140–143; tree-interior descent geometry
- [ ] 11 Basilisks (curse), 10 random-spawn Crystal Lizards (1/3 chance, one-time Twinkling Titanite), Mushroom Parents ×3 / Children ×6, Vagrants
- [ ] Plank Shield, Twin Humanities chest, Cloranthy Ring, titanite chunks (C.5)
- [ ] White-light checkpoint 1321700 (one-time) into Ash Lake

### Ash Lake (m13_02)
- [ ] Bonfires: Ash Lake 1321961, Stone Dragon 1321960 (kindling 10)
- [ ] Hydra with 7 severable heads (270 HP parts), all-heads-cut death, A/B dive relocation, Dragon Scale award
- [ ] 3 Maneater Clams (closed until 15/10 m), 3 Basilisks + Mushroom Parent in the shore tree, Vagrants
- [ ] 3 Dragon Scales, Great Magic Barrier
- [ ] Everlasting Dragon: immortal, wakes at 30 m, Path of the Dragon ESD (Dragon Eye + Dragon Head Stone on join, Dragon Torso Stone at 30 scales), prayer anims 7910/7911/7912, tail cut → Dragon Greatsword
- [ ] Siegmeyer (7900) + Sieglinde (7860) reunion → Titanite Slab
