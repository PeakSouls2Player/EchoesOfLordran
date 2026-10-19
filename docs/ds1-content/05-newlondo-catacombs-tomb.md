# 05 — New Londo Ruins, Valley of Drakes, The Abyss, The Catacombs, Tomb of the Giants

DS1 content census for the "Lordran in DS2" port. Ground truth is DSR data:

- MSB parts: `msb1-dump`, `survey-map` (InitAnim = idle pose), plus a SoulsFormats reader for MSB *events* (generators, messages, spawn points, dummy enemies) and enemy `MovePointNames`/`PlatoonID`.
- Events: `emevd-dump` of `event\m16_00_00_00`, `m13_00_00_00`, `m13_01_00_00` and `common.emevd.dcx`.
- Params: NpcParam (HP, souls, drop lots), ItemLotParam, ObjActParam, GameAreaParam, ShopLineupParam. The names are the community row names.
- Wiki checks (paraphrased): darksouls.wiki.fextralife.com pages *Tomb of Giants*, *The Catacombs* and *New Londo Ruins*, and searches on the Key to New Londo Ruins, Rickert and the Catacombs pit/Vamos cutscene. Anything taken only from a wiki or from my own inference is marked **(unverified)**.

Conventions:
- Positions are DS1 map-local `(x, y, z)` in metres.
- `E=` is an MSB entity ID and `F` is an event flag.
- "Respawning" means the part has no EMEVD one-time-death event, so it returns on bonfire rest.
- "One-time" means an event force-kills or disables the part on reload after its first death.
- Map-name IDs (collision `mapName`): 1600 = New Londo Ruins, 1601 = The Abyss, 1602 = Valley of Drakes, 1201 = Darkroot Basin, 1300 = The Catacombs, 1310 = Tomb of the Giants.
- `m13_00` and `m13_01` both carry an MSB MapOffset of (46, −105, 182). Coordinates below are the raw MSB values.

## Shared flags and IDs used by these areas

| Flag / ID | Meaning (verified from the scripts unless marked) |
|---|---|
| F 6 | Pinwheel defeated (set in m13_00 event 11300001) |
| F 7 | Gravelord Nito defeated (m13_01 event 11310001) |
| F 13 | Four Kings defeated (m16 event 11600001) |
| F 14 / F 15 | Seath (m17) / Gwyn (m18_00) defeated. F15 removes Kaathe from the Abyss. |
| F 710 | Player has received the Lordvessel. It gates Ingward's Key to the Seal and Kaathe's quest step (who sets it is unverified, probably a talk script). |
| F 11800100 | Lordvessel placed at the Firelink Altar (m18_00). Opens the golden fog in the Tomb and changes Kaathe's appearance. |
| F 11600100 | New Londo drained (seal lever pulled, cutscene 160000 played) |
| F 735 / 5000 / 5001 | Black World Tendency extra-enemy system: 735 forces the extras off; 5000 = extras on, 5001 = extras off |
| F 8120 | "Taken by the abyss" death. Common event 718 shows status message 10010650. |
| F 703 | Gate used by every NPC-hostility event before it saves the hostile flag (generic) |
| SpEffect 2200 | Covenant of Artorias effect: walk on the Abyss floor instead of falling forever (inferred from use) |
| SpEffect 5451 | Revive effect on Catacombs and Nito-arena skeletons, cleared when their master dies |
| Help text 10010403 / 10010407 | "Traverse the white light" / "Enter through the white light" (fog prompts) |
| Text 10010163 | "It's locked" (ObjAct failure text on key doors) |

Boss rewards:
- Souls come from GameAreaParam `bonusSoul`: Pinwheel 15,000; Nito 60,000; Four Kings 60,000. Boss NpcParam `getSoul` is 0.
- Items come from common event 970:
  - F6 → lot 2550 Rite of Kindling + lot 9000 Humanity + lot 9030 Homeward Bone.
  - F7 → lot 2560 Lord Soul (Nito) + lot 9000 Humanity.
  - F13 → lot 2630 Bequeathed Lord Soul Shard + lot 9010 Humanity.

---

# 1. New Londo Ruins — upper, flooded state (m16_00_00_00)

## 1.1 Header

- **Map:** m16_00_00_00. The same MSB also holds the Valley of Drakes (§2), the lower drained ruins and the Abyss (§5).
- **Entrances:**
  - **Firelink Shrine elevator** ("拠点エレベータ", object E1601200). It runs between Firelink (top, y ≈ −74, region 1602200) and the New Londo landing at (−55, −136.5, 50) (region 1602201). The top lever E1021201 is in m10_02; the bottom call lever is E1601202.
  - **Valley of Drakes door** E1601110 at (−41.7, −140, 33.4). It needs the **Key to New Londo Ruins** (goods 2008) or the **Master Key** (goods 2100), from ObjActParam 6010. On the wiki, the Key to New Londo Ruins comes from the Blighttown chest (lot 1400500).
  - **Valley ladder/floodgate side** (after draining; see §2 and §5).
- **Prerequisites:**
  - None to enter.
  - Ghosts can only be damaged while Cursed or under Transient Curse (or with ghost weapons). This is the engine's ghost rule.
  - Lower New Londo needs the **Key to the Seal** (goods 2013, from Ingward after the Lordvessel, F710) to drain the water.
- **No bonfire in vanilla New Londo.** A New Londo bonfire was cut: dummy talker c1000_0000 E1600960 at (50.07, −141.9, 34.42), and collisions still reference `disableBonfire = 1601960`.

## 1.2 Progression walkthrough (pre-drain)

1. **Elevator landing.** Take the Firelink elevator down to (−55, −136.5, 50). The landing has an online-boundary fog E1601994 (o6601). Event 0 removes it for the host; clients see it.
2. **Entrance stairs.** Descend past 15 cowering **Hollows** (c2500, idle poses 9002–9012). The wiki calls them non-hostile.
   - **Rickert of Vinheim** sits caged at (−32.4, −150.4, 53.3) in InitAnim 7830.
   - Corpses on the way: Soul of a Nameless Soldier at (−59.9, −140.1, 59.5) and the **Estoc** at (−38.7, −148.3, 72).
3. **Valley door (optional, keyed).** On the left, door E1601110 (needs the key) leads to the Valley of Drakes stairs (§2). A Large Soul of a Nameless Soldier lies at (−39.8, −140.2, 32.4) beside it.
4. **Bottom of the stairs.** There is a **corpse inside an urn** at (−28.9, −154.1, 91.4): break the urn E1601310 to drop it and loot **Transient Curse ×2** (event 11600650). The **hollowed Crestfallen Warrior** appears here once F1464 is set (−25.7, −154, 92.9).
5. **Lower ghost street (water level y ≈ −157).**
   - **Fire Keeper Soul** on corpse o0504 at (19.1, −157.4, 74.3).
   - Three ghosts rise from the water when you enter region 1602270 (29.3, −157.6, 71.7).
   - Another urn corpse with **Transient Curse ×2** at (56.9, −157.4, 54.6) (urn E1601311).
6. **Upper streets and squares** (x 50–110, y −138…−150). There are wall-hiding ghosts (§1.4).
   - **Kick-down ladder** E1601142 at (74.6, −150, 50): lower it from above (event 11600160). It is a one-way shortcut.
   - Corpses: **Parrying Dagger** at (98.3, −147.9, 31.7) and **Transient Curse ×2** at (74.8, −147.9, 32.2).
   - Large Soul of a Nameless Soldier at (65.5, −142.4, 55.5).
   - **Chest: Very Large Ember** at (81, −148.2, 24).
7. **White light gate CP1** (o6604 E1601700 at (87.8, −148.2, 42.1); event 11600090 slot 0). Pass through either way (regions 1602600/1602601). It is deleted permanently afterwards.
8. **Ingward's tower** (61.4, −135.9, 10.9).
   - Climb the fireplace/chimney ladder E1601140 (o6400 at (60.2, −147, −11.6)) to **Ingward** on the roof.
   - Nearby: **Rare Ring of Sacrifice** on the roof ledge (52.1, −140.3, 14.4; jump), **Cursebite Ring** (50.7, −152, 7.4), **Humanity** (33.7, −138.8, 6.4), **Soul of a Proud Knight** (65.6, −146.8, −6.9) and a **Large Soul of a Nameless Soldier** (56.3, −148.2, −18).
   - Female ghosts guard the tower base.
9. **Floodgate / seal tower** (x ≈ 98–122, z ≈ −20…−27).
   - **Green Titanite Shard** at (80, −154.3, −12.2).
   - **Seal door** E1601111 (o6010 "封印の扉", ObjActParam 16010): needs the Key to the Seal; on use it shows "Used Key to the Seal" (10010872).
   - Behind it is the **sluice lever** E1601101 (o6110) at (100.7, −152, −23.7), which drains New Londo (§5).
   - The spiral tower holds the **Composite Bow + Large Arrows** at (117.2, −143.4, −20.2) and the **Red Tearstone Ring** at (119.9, −152, −22.8) (Valley map-name zone).
   - Ladder E1601141 (o6401 at (122.5, −171.6, −24.5)) goes down to the Valley floor.
10. **Four Kings / Abyss entrance** at the bottom (81.5, −163, −0.6) is underwater until drained (§5).

## 1.3 Bonfires

None in New Londo proper. The cut bonfire E1601960 is listed above. For the Darkroot Basin bonfire in this MSB, see §2; for the Abyss bonfire, see §5.

## 1.4 Enemies (upper / flooded)

| Sub-zone | Enemy | Model | Count | MSB parts (entity) | Behaviour / notes | Drops |
|---|---|---|---|---|---|---|
| Entrance stairs | Hollow (cowering) | c2500 | 15 | c2500_0000–0014 (no entity) | npc 250040 / think 250000, HP 53, 20 souls. InitAnim per part: 0000 9006, 0001 9010, 0002 9002, 0003 9011, 0004 9002, 0005 9008, 0006 9005, 0007 9006, 0008 9012, 0009 9010, 0010 9011, 0011 9005, 0012 9002, 0013 9005, 0014 9008. Respawning; huddled, non-hostile per wiki (unverified). | lot 25004000 (none) |
| Upper ruins | Ghost (Male) — wall-hiders | c2670 | 8 | E1600200 (_0008), 1600201 (_0007), 1600202 (_0004), 1600203 (_0001), 1600204 (_0016), 1600205 (_0003), 1600206 (_0015), 1600210 (_0030) | InitAnim 9000 (inside wall). Event 11605001: AI off until the player is within R m (5/5/7/7/6/6/3/5) or hits it. Emerges with cancel anim 9060. Respawning. | lot 26700000: Jagged Ghost Blade / Transient Curse |
| Upper ruins | Ghost (Male) — trigger ambush | c2670 | 10 | E1600250 (_0022) and 1600251 (_0026): anim 3007, areas 1602250/1602251. E1600252 (_0012) and 1600253 (_0013): anim 3006, area or ≤5 m. E1600254 (_0027) and 1600255 (_0014): anim 3005. E1600207/208/209 (_0018/_0019/_0029): emerge 9060 at area 1602270. E1600211 (_0031): 9060 at area 1602271. | Event 11605050: AI off until the area is entered or the ghost is damaged. All InitAnim 9000. | same |
| Upper ruins | Ghost (Male) — roaming | c2670 | 14 | E1600350–1600361 (_0000, _0002, _0005, _0006, _0009, _0017, _0020, _0021, _0023, _0024, _0025, _0028), E1600301/1600302 (_0010/_0011) | Think variants 267010/011/020/030/031/040/050. Respawning. | same |
| Ingward tower / plaza | Ghost (Female) ("Banshee" on wiki) | c2680 | 2 | E1600310 (_0000 at (75.8, −148.1, 33.3)), E1600300 (_0001 at (59.2, −146.9, −6.9)) | npc 268000, HP 171, 400 souls, think 268001 | lot 26800000: Ghost Blade / Transient Curse |
| (all ghosts) | leash boxes | — | — | Event 11605150 re-plans a ghost that leaves its box. 1602850: 202, 204, 205, 211, 351, 356, 357, 250. 1602851: 252, 253, 255. 1602852: 200, 201, 206, 251, 254, 301, 302, 352–354, 360. 1602853: 350, 355, 203, 207–210. 1602854: 358, 359, 361. 1602860: female ghosts 300, 310. | Port the leash as home areas. | |
| Online | Vagrants (DSR online drops) | c3490 / c3491 | 3 / 6 | Passive c3490_0000 E1603900 (18, −157.4, 74.7), _0001 E1603910 (79.3, −154, −16), _0003 E1603920 (91.5, −163.2, 42.8, lower). Aggressive pairs: c3491_0000/0002 E1603901/1603902 (59.5, −157.4, 54.6); _0001/_0004 E1603911/1603912 (60.4, −138.7, −14.6); _0003/_0005 E1603921/1603922 (86, −161.5, −19.5) | Online-only vagrant system; optional to port. | Aggressive: lots 34910600/34920600, 34911100/34921100 |

Black-World-Tendency extras, drained-only Darkwraiths and Mass of Souls are listed in §5. They are present in the MSB but script-removed until the water is drained.

## 1.5 Bosses

None in the upper ruins.

There is also a **cut Darkwraith mini-boss**. Events 11605380–11605385 and 11600900 are never initialized, and the boss entity E1600810 does not exist in the MSB. As scripted:
- Fog area 1602898 and fog object 1601890 (both absent from the MSB).
- Boss bar name 2390.
- BGM region "中ボス BGM" 1602890 at (59.4, −140, 0), next to Ingward's tower.
- On death it would award lot 1100 (**Key to the Seal**) if the player lacks goods 2013.

Optional content for the port.

## 1.6 NPCs

| NPC | Part / entity | Talk / think | Where | Notes |
|---|---|---|---|---|
| **Rickert of Vinheim** (blacksmith) | c0000_0004, E6220, NpcParam 6220 | talk 160622 / think 6220 | Cage at (−32.4, −150.4, 53.3) | **InitAnim 7830** (caged idle). Hostile when hit (F1381, event 11600510 slot 1). Death sets F1382 (range 1380–1399, event 11600520). Upgrades: Magic / Enchanted / Crystal paths (wiki). Shop: Soul Arrow, Heavy Soul Arrow, Sorcerer's Catalyst (ShopLineup 2100–2102). |
| **Ingward** (healer) | c0000_0003, E6180 | talk 160618 / think "Healer" 6180 | Roof of the tower (61.4, −135.9, 10.9) | No idle anim (−1). Disabled if F1315 (dead) or F1313 (left). Shop: "Resist Curse" (2400) and Transient Curse (2401); offers curse removal (talk). Quest steps below the table. |
| **Crestfallen Warrior (Hollow)** | c0000_0007, E6271, NpcParam 6271 | talk 0 / think 6271 | (−25.7, −154, 92.9) at the stair bottom | Disabled until **F1464** (he went hollow at Firelink; m10_02 entity 6270). Event 11600545 enables him as TeamType Hostile Ally when F1464 is set. Death sets F1462 (range 1460–1489). Drops via Force Character Treasure (CharaInit 6271; contents unverified). |
| **Witch Beatrice** (summon) | c0000_0006, E6520, NpcParam 6521 / think 6520 "Witch @ Abyss" | — | Sign at spawn point 1602000 (83.2, −161.5, −18.4) by the Four Kings drop | White summon sign for Four Kings; see §5. |

Ingward quest steps:
- F1310 + F710 (has Lordvessel) + alive → **F1311** (event 11600530).
- Talking with F1311 sets talk flag 11600592. Common event 911 then awards **lot 1100 Key to the Seal**.
- F1311 + drained (F11600100) → **F1312** (event 11600531).
- F1312 + Kings dead (F13) + talk flag 11600593 + player outside m16 → **F1313**, and Ingward is disabled (event 11600532). The m10_02 EMEVD has a second Ingward, E6181, keyed on F1313, so he moves to Firelink Shrine.
- Attacking him sets F1314 (hostile).
- Death sets F1315. Common event 960 slot 1 then awards **lot 1100 Key to the Seal**, so killing him also yields the key. ItemLot rows 6180/6181 (Key to the Seal / Humanity) exist (their use is unverified).

## 1.7 Items (upper ruins, route order)

| # | Item | Lot (pickup flag) | Location | Notes |
|---|---|---|---|---|
| 1 | Soul of a Nameless Soldier | 1600000 (51600000) | (−59.9, −140.1, 59.5) landing | |
| 2 | Estoc | 1600020 | (−38.7, −148.3, 72) stairs | |
| 3 | Transient Curse ×2 | 1600520 (51600520) | (−28.9, −154.1, 91.4) | corpse o0500_0003 E1601610 inside urn E1601310 (event 11600650) |
| 4 | Fire Keeper Soul | 1600030 | (19.1, −157.4, 74.3) on corpse o0504 | **Fire Keeper Soul** |
| 5 | Transient Curse ×2 | 1600040 | (56.9, −157.4, 54.6) | corpse E1601611 inside urn E1601311 |
| 6 | Large Soul of a Nameless Soldier | 1600090 | (65.5, −142.4, 55.5) | |
| 7 | Transient Curse ×2 | 1600070 | (74.8, −147.9, 32.2) | |
| 8 | Parrying Dagger | 1600060 | (98.3, −147.9, 31.7) | |
| 9 | **Chest: Very Large Ember** | 1600500 (51600500) | (81, −148.2, 24) | chest o0110_0000 E1601650; ObjAct 11600600; event 11600600 |
| 10 | Soul of a Proud Knight | 1600110 | (65.6, −146.8, −6.9) | |
| 11 | Large Soul of a Nameless Soldier | 1600120 | (56.3, −148.2, −18) | |
| 12 | **Rare Ring of Sacrifice** | 1600130 | (52.1, −140.3, 14.4) tower roof ledge | |
| 13 | **Cursebite Ring** | 1600140 | (50.7, −152, 7.4) | |
| 14 | **Humanity** | 1600150 | (33.7, −138.8, 6.4) | |
| 15 | Green Titanite Shard | 1600100 | (80, −154.3, −12.2) | |
| 16 | Composite Bow + Large Arrows | 1600160 / 1600161 | (117.2, −143.4, −20.2) spiral tower | |
| 17 | **Red Tearstone Ring** | 1600380 | (119.9, −152, −22.8) | Valley map-name zone (collision h0056B0) |

Unused treasure entries: 宝死体01/05/08/23/30/32/34/35 point at parts or lots that do not exist (lots 1600010, 1600050, 1600080, 1600230, 1600300, 1600320, 1600340, 1600350 are "missing"). Skip them.

## 1.8 Interactive objects and scripted events (upper)

| Object | Entity / model | Event | What it does |
|---|---|---|---|
| Firelink elevator | E1601200 (o6200, InitAnim 7) | 11600200 + 11600250 | Two-state lift. Riding areas 1602200 (top) and 1602201 (bottom); call levers E1021201 (m10_02) and E1601202 (bottom, ObjAct 11600203, param 6101). Busy flag 11605120. Lift object anims 11/1/21 (going up?) and 10/0/20. Pressing a lever while it is busy shows 10010170 "Contraption does not move". |
| Online fogs | o6601 E1601994 (landing), o6602 E1601996 (Valley door), o6603 E1601998 (floodgate) + SFX 1601995/7/9 | event 0 | Disabled for host/single player and kept for clients (phantom boundary). |
| Valley door | E1601110 (o6010_0000), ObjAct 11600120 / flag 61600120, ObjActParam 6010 | 11600120 | Opens with Key to NL Ruins (2008) or Master Key (2100). Message 10010867 or 10010883 "Used <key>". Player anim 7110. |
| Urn corpses | corpses E1601610/1601611; urns E1601310/1601311 (o6450 "Urn AQ") | 11600650 | The corpse loops anim 100 inside the urn. Destroying the urn plays anim 101 and enables the treasure. |
| Kick-down ladder | E1601142 (o6410) | 11600160 | Help 10010500 (text lookup ambiguous). Player warps to dummy 192 and plays 8005; ladder plays anim 0 and is registered with flags 11600014/15. |
| Ladders | E1601140 (tower chimney), E1601141 (floodgate → Valley) | event 0 | Plain ladders (flags 11600010–13). |
| White-light gates | CP1 E1601700 (o6604) and SFX 1601701; CP2 E1601702 (o6605) and SFX 1601703 (lower, §5) | 11600090 slots 0/1 | Action-button line-segment fog. Warps the player to the far side (regions 1602600/601, 1602602/603) with anim 7410, then is permanently removed. |
| Chests | E1601650, E1601651, E1601652 (o0110) | 11600600 slots 0–2 | Standard chest: open anim, then treasure enabled. |
| Seal door | E1601111 (o6010_0001), ObjAct 11600110 / flag 61600110, ObjActParam 16010 | 11600110 | Key to the Seal (2013). Message 10010872. |
| Breakable urns | o6450/6451/6452/6455/6456/6457 (many, no entity) | — | Decoration / breakables. |
| Dev messages (MSB) | 6000 "Need curses", 6001 "Need covenant", 6002 "Imminent jumping off...", 6003 "Ring ahead" | — | All flagged Hidden in the MSB; on collisions h0005B0, h0037B0, h0021B0, h0052B0. |

## 1.9 Porting notes (upper)

- **Ghost/curse mechanic.** Male and female ghosts must be immune unless the player is Cursed, has Transient Curse active, or wields ghost weapons. DS2 has no such rule, so it needs SpEffect/hit-filter emulation.
- **Curse status.** It halves max HP, and Ingward cures it. DS2 has Petrify/Curse buildup but different semantics.
- **Ambush ghosts.** Hidden-in-wall InitAnim 9000 with emerge 9060/300x needs AI-off plus a special-anim swap; every trigger radius and region is listed above.
- **Water plane.** The water object E1601120 (o6500 at y −157.1) with flooded collisions 1603200–1603217 is the *default* state; draining swaps sets (§5).
- **Urn-trapped corpses** need a destructible-object to treasure link.
- The cut bonfire E1601960 and the cut Darkwraith mini-boss could be restored optionally.

---

# 2. Valley of Drakes, plus the Darkroot Basin bonfire and Basin elevator (m16_00_00_00)

## 2.1 Header

- **Map:** m16_00_00_00 (map-name 1602 "Valley of Drakes"; the top of the Basin elevator is map-name 1201 "Darkroot Basin").
- **Connections:**
  1. **Darkroot Basin elevator** ("森庭エレベータ", E1601230 at (176.5, −166.5, −84.6)). It runs from the Basin top (y −78.6, region 1602230, lever E1601231) to the Valley floor (y −166.5, region 1602231, lever E1601232). It is always usable (event 11600230, busy flag 11605123, lift anims 26/6/10 up and 21/7/17 down).
  2. **New Londo door** E1601110 (keyed; §1). The stairs from it run past the Witch-set corpse spot (−25.4, −144.8, −8).
  3. **New Londo floodgate ladder** E1601141 / drained floodgate E1601100 (§5).
  4. **Blighttown** cave (m14_00, owned by doc 03/06). The Key to New Londo Ruins chest is at that cave's end (lot 1400500).
- **Prerequisites:** none (via Darkroot Basin). Drakes and the Undead Dragon are dangerous.

## 2.2 Walkthrough

1. **Darkroot Basin bonfire.** Rest at the Darkroot Basin bonfire (E1601961 at (165, −76.7, −56); talker c1000_0002 E1600961, talk 160001). Ride the elevator down using lever E1601231.
2. **Valley floor** (x 110–145, y −172…−174).
   - Drakes patrol.
   - **Brigand set + Spider Shield** corpse at (127.6, −173.5, −48.7).
   - **Humanity** at (119.6, −172.1, −111).
   - A drake bridge leads toward the Blighttown cave (Blighttown side not in this MSB).
3. **Ledge to New Londo.** Ladder E1601141 at (122.5, −171.6, −24.5) climbs into the New Londo floodgate tower. The Red Tearstone Ring corpse is on its ledge (§1).
4. **Undead Dragon ledge** (18–28, −153…−155, −60…−70).
   - The **Undead Dragon** lies "dead" at (27.7, −153.3, −59.9).
   - **Astora's Straight Sword** (18.8, −154.7, −62.5), **Dragon Crest Shield** (20.4, −154.9, −63.3) and **Soul of a Proud Knight** (22.1, −155.2, −69.7).
5. **Stairs up to the New Londo door** (keyed). Large Soul of a Nameless Soldier at (−39.8, −140.2, 32.4). The **Witch set** appears at (−25.4, −144.8, −8) only after the Four Kings die (§5).

## 2.3 Bonfires

| Bonfire | Object / talker | Flag | Location | Notes |
|---|---|---|---|---|
| Darkroot Basin | o0200_0002 E1601961; c1000_0002 E1600961 (talk 160001) | 11600984 | (165, −76.7, −56) | Registered in m16 event 0. Kindling level 0. Collision h0062B0 has `disableBonfire = 1601961` and mapName 1201. **This is the Darkroot Basin bonfire, owned by this MSB.** |

## 2.4 Enemies

| Enemy | Model | Count | Parts | Behaviour | Drops |
|---|---|---|---|---|---|
| **Undead Dragon** | c3420 | 1 | c3420_0000 E1600500, npc/think 342002 | HP 1582, 3000 souls. **InitAnim 29000** (lying dead). Event 11600400: map hits and gravity disabled (it stays on its perch); waits with cancel anim 29060 until the player is within 10 m or hits it, then plays 29060 (rise). **One-time** (event 11600810 disables it after death). | Event 11600810 awards **lot 34200200 Dragon Scale** on first kill (only if the player is human/hollow, not a phantom). |
| Drake | c3520 | 6 | c3520_0007, _0009, _0010, _0011, _0012, _0013 (no entity) | npc 352002, think 352002 (_0007) or 352003. HP 524, 1000 souls. Respawning. Valley floor and bridge (112–143, −171.5…−173.5, −28…−106). | lot 35200200 Dragon Scale (chance) |
| (dummy) Drake | c3520 | 8 | dummy enemies c3520_0000–0006, _0008 | Not spawned. A "Drake (Flying)" lot (35200500) exists. Cut flying drakes (optional). | — |

## 2.5 Bosses

None. The Undead Dragon is a one-time "elite".

## 2.6 NPCs

None. A dummy enemy c0000_0005 (E6262) at (119.4, −151.9, −22.5) is not spawned (identity unverified).

## 2.7 Items

| Item | Lot | Location |
|---|---|---|
| Brigand Hood/Armor/Gauntlets/Trousers + Spider Shield | 1600220–1600224 (flags 51600220/51600221) | (127.6, −173.5, −48.7) |
| **Humanity** | 1600210 | (119.6, −172.1, −111) |
| **Astora's Straight Sword** | 1600180 | (18.8, −154.7, −62.5) |
| **Dragon Crest Shield** | 1600200 | (20.4, −154.9, −63.3) |
| Soul of a Proud Knight | 1600190 | (22.1, −155.2, −69.7) |
| Large Soul of a Nameless Soldier | 1600170 | (−39.8, −140.2, 32.4) by the NL door |
| **Witch Hat / Cloak / Gloves / Skirt + Beatrice's Catalyst** | 1600360–1600364 | (−25.4, −144.8, −8). Corpse o0504_0001 **E1601600**, disabled in the MSB; enabled only after Four Kings die (m16 events 11600001 / 11605392). |
| Dragon Scale | 34200200 | Undead Dragon kill (event) |

## 2.8 Objects and events

- Basin elevator E1601230 (o6200_0003) with levers E1601231/1601232 (o6100_0007/0008). ObjAct flags 11600232/11600233; area triggers 1602230/1602231.
- Undead Dragon wake script 11600400 and loot script 11600810.
- Witch-set corpse E1601600 toggled by the Four Kings scripts.

## 2.9 Porting notes

- The Undead Dragon needs its "dead" idle (29000) and wake (29060) with gravity and map-collision off; otherwise it falls off its perch.
- Drakes breathe fire across the bridge; the respawn rule applies.
- The Basin bonfire must be ported inside the m16 bridge map even though players think of it as Darkroot.

---

# 3. The Catacombs (m13_00_00_00)

## 3.1 Header

- **Map:** m13_00_00_00 (mapName 1300). MapOffset is (46, −105, 182).
- **Entrance:** from the Firelink Shrine graveyard, down the long staircase. Player start is (45.3, −112, 180.1). The online fog E1301994 (o3951) at (28.1, −103.7, 168.2) is removed for host/single player.
- **Exit:** Pinwheel's arena. The wall E1301040 opens when Pinwheel dies, and ladder E1301143 leads down to the **Tomb of the Giants** (m13_01).
- **Prerequisites:** none. Skeletons keep reviving until their **Necromancer** dies. Divine weapons stop revives in DS1, but no script for that exists here (engine/param rule; unverified).

## 3.2 Walkthrough

1. **Entrance stairs.** Skeletons rise from bone piles and three **Wisps** (exploding skulls) patrol.
2. **First bonfire chamber.**
   - Bonfire E1301960 at (43.9, −119.1, 202.9).
   - The **lever** E1301000 ("レバー入口門", ObjAct 11300900) opens stone wall E1301100, which is a shortcut panel (event 11300900 slot 0, navmesh 1304000).
   - Necromancer E1300180 is here.
3. **Upper hall** (z 225–275, y −108…−115).
   - Necromancer E1300100 with 14 skeletons. This necromancer **flees** between nests 1302202→1302205 when the player is within 10 m (event 11305050).
   - **Lucerne** (77.6, −111.2, 241.9), **Tranquil Walk of Peace** (66.9, −107.9, 233.6), Large Soul of a Nameless Soldier (65.2, −110.4, 255.9).
   - **Collapsing floor tiles** A–D drop you to lower levels (event 11300100).
4. **White light gate** E1301700 (o3952 at (53.2, −118.6, 243.4); event 11300090; regions 1302650/1302651). It is a one-time pass.
5. **Spiked rotating bridges.**
   - **Bridge A** E1301102 (o3000 at (46.1, −113.3, 260.2)) is turned by lever E1301002 (60.5, −111.3, 292.6).
   - **Bridge B** E1301103 (o3001 at (44.8, −119.5, 255.4)) is turned by lever E1301003 (39.2, −111.6, 289.4).
   - Both start **spike-side-up**: damaging hitboxes on the bridge dummies (behaviour 5100), events 11300300/11300350, while state flags 11300402/11300403 are OFF.
   - Pulling the lever (ObjActParam 3011 "push") flips the bridge to the safe side. ObjAct 3012 ("pull") flips it back.
   - **Patches' trap:**
     - When the player enters region 1302000 (34, −119, 266.2), Patches *lowers* bridge B for you (event 11300592; scripted ObjAct 3011 on lever E1301003).
     - When the player is on bridge B (region 1302001 "drop the PC crossing the bridge"), he **flips it back** (event 11300593, ObjAct 3012), dropping the player.
     - That sets 11300593 and moves Patches to state F1621.
   - The second lever "レバー途中門" E1301001 (32.9, −125.3, 249.6) opens the stone plaque E1301101 (event 11300900 slot 1).
6. **West wing.**
   - Necromancer E1300120 with 8 skeletons.
   - **Great Scythe** (35.8, −123, 252.2), Souls of a Proud Knight at (24.8, −118.9, 235) and (15.5, −114.6, 253.4), **Green Titanite Shard** (22.5, −140.9, 252.1).
   - Necromancer E1300140 (east) with 7 skeletons.
   - Necromancer E1300160 with 6 skeletons near (30.4, −111.4, 260.5).
7. **Illusory wall K** E1301050 (o3661 at (31.2, −116.5, 293.5); event 11300160): hit it to dissolve it. Region names and positions place the **second bonfire** E1301961 at (49, −111.9, 301.5) nearby; the wiki says it is behind an illusory wall.
8. **Breaking ceiling.** In region 1302020 (64.4, −132.2, 271.6) the **Giant Skeleton** E1300050 bursts through the stone arch E1301020 (event 11300150). **Darkmoon Séance Ring** at (64.6, −130.7, 284.3).
9. **Trap floors E/F** (56.6/50.2, −140.1, 294) drop onto the **Black Knight** (axe) E1300400 at (52.9, −147.4, 292.4). Soul of a Proud Knight at (51.5, −147.2, 289.8).
10. **Coffin room** (x 30–36, y −131.5, z 313–345).
    - The **Titanite Demon** E1300300 sleeps (waiting anim 9000) until the player is within 5 m or hits it (event 11305045). It then patrols between "coffin guard" points.
    - **Eye of Death ×3** (35.2, −131.4, 344.1); Large Soul of a Nameless Soldier (46.4, −131.3, 290.7); Crystal Lizards ×2 nearby.
    - The **Gravelord coffin** E1301104 (o3060 "Sarcophagus A") is at (32.3, −131.5, 317.6); see §3.8.
11. **Spiral staircase pit.**
    - Dropping into region 1302030 the **first time** plays cutscene **130010**: the player survives the fall and lands by Vamos (event 11300210).
    - The brick walls E1301030/1301031 (o3871/o3872) are then removed (Vamos breaks the wall out).
    - Event 11305060 tracks whether the player is in the region. Later drops are lethal (wiki).
    - Third bonfire E1301962 at (21.4, −157.5, 250.7), opposite **Vamos** (21.3, −157.5, 274).
12. **Bonewheel ravine** (x 41–53, y −145…−158, z 193–275).
    - Bonewheel Skeletons.
    - **Priest's Hat / Holy Robe / Traveling Gloves / Holy Trousers + Mace** corpse at (45.9, −151.5, 241.9); Large Soul of a Nameless Soldier at (41.7, −145.1, 239.8).
    - **Paladin Leeroy's white summon sign** at (42.9, −144.6, 236.9).
    - Necromancer E1300200 with 7 skeletons in the lower tunnels (37.2, −151.5, 303.7).
13. **Pinwheel fog** E1301990 (o3954 at (50.4, −158.4, 189.5)) leads into the arena (46, −166, 152). After the kill, the wall E1301040 (o3350) opens and ladder E1301143 goes down toward the Tomb.

## 3.3 Bonfires

| Bonfire | Object / talker | Flag | Location |
|---|---|---|---|
| Catacombs (first, lever room) | o0200_0000 E1301960; c1000_0000 E1300960 (talk 130000) | 11300992 | (43.9, −119.1, 202.9) |
| Catacombs (second, illusory wall) | o0200_0001 E1301961; c1000_0001 E1300961 (talk 130001) | 11300984 | (49, −111.9, 301.5) |
| Catacombs (Vamos) | o0200_0003 E1301962; c1000_0003 E1300962 (talk 130002) | 11300976 | (21.4, −157.5, 250.7) |

All are kindling level 0. A **cut post-Pinwheel bonfire** exists: dummy talker c1000_0002 E1300950 at (46, −157, 134.3), and collisions reference bonfire 1301950.

## 3.4 Enemies

| Sub-zone | Enemy | Model | Count | Parts (entity) | Behaviour | Drops |
|---|---|---|---|---|---|---|
| Entrance stairs / first bonfire | Skeleton (group of Necromancer E1300180) | c2900 | 7 | _0048 E1300181, _0049 E1300182, _0047 E1300183, _0045 E1300184, _0046 E1300185, _0000 E1300000, _0001 E1300001 | Revive (SpEffect 5451 + immortality) while E1300180 lives (event 11305100). _0000, _0001, _0048, _0049 have **InitAnim 9001** (lying in bones) and rise with 9061 within 5 m (event 11305070). | npc 290020/290022 → lot 29002000 Scimitar + R&W Round Shield, or 29002200 Falchion |
| First bonfire | Necromancer | c2650 | 1 | _0004 E1300180 | HP 242, 1000 souls. **One-time** (event 11300801). | lot 26500000 **Skull Lantern** (chance); humanity lot 5250 |
| Upper hall | Necromancer | c2650 | 1 | _0000 E1300100 | Flees between nests 1302202–1302205 (event 11305050, 10 m, not vs black phantoms). One-time. | same |
| Upper hall | Skeleton | c2900 | 14 | _0002–_0015 → E1300101–1300114 | Revive until E1300100 dies. 9001 ambushers: _0008 (107), _0010 (109), _0011 (110), _0014 (113). Bow: _0015 (npc 290021). Falchion: _0003, _0006, _0011, _0012, _0014. | lots 29002000/29002100 Short Bow/29002200 |
| East wing | Necromancer | c2650 | 1 | _0002 E1300140 at (75.4, −119.2, 240.1) | One-time | |
| East wing | Skeleton | c2900 | 7 | _0027–_0033 → E1300141–1300147 | Revive until E1300140 dies. _0033 (147) InitAnim 9001. Bows _0029, _0031. | |
| West wing | Necromancer | c2650 | 1 | _0001 E1300120 at (32.1, −125.4, 252.2) | One-time | |
| West wing | Skeleton | c2900 | 8 | _0022–_0026 → E1300121–125, _0021 → E1300126, _0044 → E1300127, _0020 → E1300020 | Revive until E1300120 dies. 9001 ambushers: _0020, _0022–_0026. | |
| Bridge / Patches area | Necromancer | c2650 | 1 | _0003 E1300160 at (30.4, −111.4, 260.5) | One-time | |
| Bridge / Patches area | Skeleton | c2900 | 6 | _0016–_0019 → E1300161–164, _0052 → E1300165, _0053 → E1300166 | Revive until E1300160 dies. _0018/_0019 InitAnim 9001. | |
| Lower tunnels | Necromancer | c2650 | 1 | _0005 E1300200 at (37.2, −151.5, 303.7) | One-time | |
| Lower tunnels | Skeleton | c2900 | 7 | _0035–_0039 → E1300201–205, _0042/_0043 → E1300206/207 (bows) | Revive until E1300200 dies. | |
| Black WT only | Skeleton | c2900 | 8 | _0034 E1300900, _0040 E1300901, _0041 E1300902, _0054–_0058 E1300903–907 (npc 290013/290014) | Enabled only while Black World Tendency > 50 (events 11305065/66/67; 10-min re-check). Normally disabled. | lots 29001000/29001200 |
| Entrance / halls | Wisp (exploding skull) | c3501 | 12 | _0006–_0011 → E1300350–355, _0012–_0016 → E1300356–360, _0019 → E1300363 (npc/think 350101) | HP 9. Patrol routes (PointMoveType 2) over "logic ウィプス移動ポイント 00–14"; _0016 uses boss warp points 05–09. Disabled permanently after exploding (event message 400; event 11305210). | none |
| Ceiling (Darkmoon ring) | Giant Skeleton | c2910 | 1 | _0001 E1300050, npc 291002 | AI off until region 1302020, then bursts through arch E1301020 (event 11300150). Respawning. | lot 29100200 Murakumo |
| Trap-floor pit | Black Knight (Greataxe) | c2794 | 1 | _0000 E1300400, npc 279020 | HP 355, 1200 souls. **One-time** (event 11300850 slot 9). | lot 27902000 **Black Knight Greataxe + Shield**, chain 27902001 White Titanite Chunk |
| Coffin room | Titanite Demon ("Prowling Demon") | c2300 | 1 | _0000 E1300300, npc 230000 | HP 1255, 2000 souls. Sleeps (9000) until 5 m or hit. Patrols "棺を守る敵 00/01". Clears its target if the player has SpEffect 4130 (lying in the coffin; event 11305004). **One-time**. | lot 23000000 **Titanite Catch Pole**, chain 23000001 Demon Titanite |
| Coffin room | Crystal Lizard | c3300 | 2 | _0000 E1300500, _0001 E1300501, npc 330030 | One-time | lot 33003000 Twinkling Titanite + 33003001 random material |
| Bonewheel ravine | Bonewheel Skeleton | c2930 | 7 | _0000–_0006 (no entity), npc 293000 | Respawning; rolls. | lot 29300000 **Bonewheel Shield** |
| Black WT only | Bonewheel Skeleton | c2930 | 3 | _0007 E1300908, _0008 E1300909, _0009 E1300910 (npc 293003) | Black-WT extras | same |
| Online | Vagrants | c3490 / c3491 | 2 / 4 | c3490_0001 E1303900, _0002 E1303910; c3491_0000/0001 E1303902/901, _0002/0003 E1303911/912 | Online-only | |
| (dummies) | — | — | 6 | c2900_0050/0051 (E1300018/19), c2920_0001 (Vamos), c3501_0017/0018 (E1300361/362), c1000_0002 (cut bonfire) | Not spawned | |

## 3.5 Boss — Pinwheel

| Field | Value |
|---|---|
| Model / parts | c3320. Real body c3320_0000 **E1300800** (npc 332000, HP 780). 14 fake clones c3320_0001–0014 **E1300801–1300814** (npc 332001, HP 1, think 332001), parked at (46, −195.8, 146). |
| Arena | (46, −166, 152); regions 1302500 (logic) and 1302999 (cutscene trigger); BGM region 1302990, sound E1303800. |
| Fog | E1301990 (o3954) with SFX 1301991. Entry line segment 1302998 to target 1302997 (anim 7410). Phantom entry: 11305391. DSR multiplayer fog: 11305843/11305846. |
| Intro | Event 11305392. When the host enters 1302999, cutscene **130000** plays every first entry per load: event 0 resets F11300000 on load. The event then sets F11300000 and F11300005 (F11300005 is used by Rhea's quest), shows the boss bar (name 3320) and enables the AI. |
| Mechanics | Teleport (event 11305396): on AI event message 600 the body hides, a random slot is picked from flags 11305320–326, it warps to one of 7 points (1302600/602/604/605/606/608/612), waits 3 s and reappears with anim 7000. Clone spawning (event 11305350, 7 pairs): on AI event message 700 (via 11305397), a pair of clones gets SpEffect 5450, warps to warp points, is enabled and plays 7000. At HP ≤ 30% (event 11305398) F11305399 is set, a second command set runs, and four extra pair slots re-arm with new warp points. Clones despawn on event message 710 or when the body dies (event 11305370). HP bars hidden on clones (11300880). |
| Death | Event 11300001: plays SE 777777777, sets **F6**, Handle Boss Defeat, removes the fog, registers ladder E1301143 and kills the clones. Event 11300200 then opens exit wall E1301040. |
| Rewards | 15,000 souls. NpcParam lot 33200000 = **Mask of the Father / Mother / Child** (one). Common event 970: **Rite of Kindling** (2550) + Humanity + Homeward Bone. |
| Summon | Paladin Leeroy (white sign; §3.6). |
| World change | Patches leaves for the Tomb (F1623, if not hostile/dead). Path to the Tomb opens. Leeroy's sign gone. |

## 3.6 NPCs

| NPC | Part / entity | Talk / think | Where | Notes |
|---|---|---|---|---|
| **Patches (Trusty)** | c0000_0002 E6320 | talk 130632 / think 6320 "Cemetery Coward Patch" | (41.2, −111.7, 292) by bridge-B lever | No idle anim. Enabled if F1627 (hostile), or F1620 or F1621. The bridge trap moves him F1620 → **F1621** (event 11300530 needs 11300593 and alive). Hostility: F1627 (event 11300510 slot 1). Death sets F1628 (event 11300531; Force Character Treasure). NpcParam lot 6320 Crescent Axe, then 6321 Humanity. He leaves for the Tomb as **F1623** when Pinwheel is dead (F6) or Rhea is in the Tomb (F1173) (event 11300533). Talk flags 11300590/11300591 award lots 1190 Soul of a Lost Undead / 1200 Humanity via common 911 (community-unnamed; presumably Patches' gifts, unverified). |
| **Vamos** (blacksmith) | c2920_0000 E6200, npc 292000 | talk 130620 | (21.3, −157.5, 274) | **InitAnim 9003** (hammering at his anvil). Hostile when hit (F1341); death F1342 (range 1340–1343). Event 11305061 turns his map-hit and gravity off for 10 frames on load (settling). Drops lot 29200000 **Hammer of Vamos** (flag 51300990), chain 29200001 (flag 51300991). Shop rows 6300–6310: Homeward Bone, Titanite Shard, Weapon/Armor Smithbox, Repairbox, arrows and bolts. Ascension: Fire/Chaos paths (wiki, unverified). |
| **Paladin Leeroy** (summon) | c0000_0004 E6550, npc 6550 | think "Temple Knight Cemetery 1" | Sign at 1302050 (42.9, −144.6, 236.9) | White sign (events 11305025 and DSR variant 11305029): host, human, Leeroy loaded, Pinwheel alive. After summon his home becomes 1302051; event 11305027 walks him through the boss fog with anim 7410. |

## 3.7 Items (route order)

| Item | Lot | Location |
|---|---|---|
| Lucerne | 1300070 | (77.6, −111.2, 241.9) |
| **Tranquil Walk of Peace** (miracle) | 1300230 | (66.9, −107.9, 233.6) |
| Large Soul of a Nameless Soldier | 1300000 | (65.2, −110.4, 255.9) |
| Soul of a Proud Knight | 1300010 | (24.8, −118.9, 235) |
| **Great Scythe** | 1300110 | (35.8, −123, 252.2) |
| Soul of a Proud Knight | 1300250 | (15.5, −114.6, 253.4) |
| Green Titanite Shard | 1300100 | (22.5, −140.9, 252.1) |
| Soul of a Proud Knight | 1300240 | (44.8, −111.3, 295.8) |
| **Darkmoon Séance Ring** | 1300020 | (64.6, −130.7, 284.3) |
| Large Soul of a Nameless Soldier | 1300140 | (46.4, −131.3, 290.7) |
| Soul of a Proud Knight | 1300150 | (51.5, −147.2, 289.8) |
| **Eye of Death ×3** | 1300030 | (35.2, −131.4, 344.1) coffin room |
| Large Soul of a Nameless Soldier | 1300220 | (41.7, −145.1, 239.8) |
| **Priest's Hat / Holy Robe / Traveling Gloves / Holy Trousers + Mace** | 1300190–1300194 | (45.9, −151.5, 241.9) |
| Soul of a Proud Knight | 1300210 | (50.6, −154.7, 196.4) before the Pinwheel fog |

Unused corpses (missing lots): 0004, 0005, 0006, 0008, 0009, 0012, 0013, 0016, 0017, 0018, 0020.

## 3.8 Interactive objects and events

| Object | Entity / model | Event | Function |
|---|---|---|---|
| Entrance-gate lever, stone wall | E1301000 (o3011 Lever Handle), E1301100 (o3020) | 11300900 slot 0 (ObjAct 11300900) | Lever opens the wall (anim 1). Navmesh 1304000 flag "solid" removed. Reload state uses anim 2. |
| Midway-gate lever, stone plaque | E1301001, E1301101 (o3021) | 11300900 slot 1 (ObjAct 11300901) | Same, with navmesh 1304001. |
| Rotating bridge A / B | E1301102 (o3000) / E1301103 (o3001), levers E1301002 / E1301003 | 11305030/11305032 (ObjAct 11300902/903 and 11300904/905), spikes 11300300/11300350 | Flip lever (3011 → bridge anim 3) or flip back (3012 → anim 1), 140 frames each. State flags 11300402/11300403. Navmesh 1304002/1304003 toggled. While the state flag is OFF, 8 or 11 damaging hit-points (behaviour 5100) are live. |
| Patches bridge trap | regions 1302000 / 1302001 | 11300592 / 11300593 | Scripted lever pulls on E1301003 (see walkthrough). |
| Collapsing floor tiles A–F | E1301010–1301015 (o3030–o3035; Smithbox aliases "Illusory Wall C–F" / "Stone Tiles A–B") | 11300100 slots 0–5 | Destroyed (SFX 130000, SE 303x00000) when the player steps into cylinders 1302100–105. One-time. |
| Breaking ceiling arch | E1301020 (o3036) | 11300150 | Giant Skeleton drop-in. |
| Illusory wall K | E1301050 (o3661) | 11300160 | Hit to dissolve; sets F11300160. |
| Pinwheel exit wall | E1301040 (o3350) | 11300200 | Opens (anim 0) when F6 is set. |
| Vamos bricks | E1301030 / E1301031 (o3871/o3872) | 11300210 | Removed after pit cutscene 130010. |
| Statue traps (o3321 "Statue J") | E1301200–1301210, 1301212–1301219 (19 objects) | 11300700 slots | When any character comes within 1.5 m they create 3 damaging hits (behaviour 5120, dummies 100–102) and play anim 0, then re-arm. Exact visual is unverified (likely a spike/blade statue trap). |
| Ladders | E1301140–1301149 (o3040–o3048) | event 0 | Flags 11300010–11300029. E1301143 is registered only after Pinwheel dies. |
| **Gravelord coffin** | E1301104 (o3060), ObjAct 11300910 (enter, ObjActParam 3060 "Nestle in coffin") / 11300911 (exit, 3061) | 11305000–11305009, 11300420 | Conditions: single player, in region 1302700, F11310000 OFF (the Nito fight has not started this load), **owns Eye of Death** (goods 109). Lying 30 s (alive, still inside) sets F11310050 and plays cutscenes **130020** + **130120**, warping the player into the Tomb at point 1312110 beside Nito. Players near the coffin get waiting anim 7151 / death anim 6082. The ObjAct swaps enter/exit; it resets after 5 s or when leaving. |
| White-light gate | E1301700 (o3952) | 11300090 | One-time pass (regions 1302650/651). |
| Dev messages | 3000–3004 (3001–3003 hidden). The text tool returns other FMGs' strings for these IDs (unverified). | — | |

## 3.9 Porting notes

- **Necromancer revive system.** Each skeleton is immortal with SpEffect 5451 until its master necromancer dies (49 skeletons, 6 masters). Necromancer 1300100 also has a 4-point flee route.
- **Spiked rotating bridges** need a 2-state rotating object, toggling damage hitboxes, toggling navmesh, and an NPC-scripted lever (Patches).
- **Statue blade traps** (19) and **collapsing floor tiles** (6) need proximity-triggered destructible/damage objects.
- **Pit cutscene 130010.** The first fall is survivable; later falls are lethal (a fall-damage exception).
- **Pinwheel.** Clone system (14 HP-1 fakes in 7 pairs, warp points, random teleport through flags) plus an intro cutscene that replays each load.
- **Gravelord covenant coffin** is a cross-map warp (m13_00 → m13_01) with lie-down animations.
- Skeleton ambush poses (9001 → 9061).

---

# 4. Tomb of the Giants (m13_01_00_00)

## 4.1 Header

- **Map:** m13_01_00_00 (mapName 1310). MapOffset is (46, −105, 182).
- **Entrance:** from Pinwheel's room (Catacombs). Player start is (44.2, −157.6, 132), with online fog E1311994 (o3961) at (46.2, −157.7, 133).
- **Exits:** back to the Catacombs. The Gravelord coffin E1311300 in Nito's chamber warps to the Catacombs coffin.
- **Prerequisites:**
  - **Darkness:** carry the Skull Lantern (found here), the Sunlight Maggot helm, or Cast Light.
  - **Golden fog** before Nito's area requires the **Lordvessel placed** (F11800100).
  - Skeleton Babies cause toxic buildup (wiki).

## 4.2 Walkthrough

1. **Descent** (y −153…−200).
   - Slanting coffins and slopes (slide regions 1313100–1313104).
   - Giant Skeletons (greatsword and bow) on ledges.
   - Ladders E1311142/1311143 (50–54, −167…−170, 102–105) and E1311145 (65.9, −185.2, 106.9).
   - Corpses: **Eye of Death ×3** (51.8, −166.8, 111.1), Large Souls of a Proud Knight at (68, −170.3, 109.7), (65.4, −173.6, 105.3) and (80.1, −175.7, 104.2). The first Large Soul of a Proud Knight is by the entrance (42.4, −153.6, 126.1).
   - **Illusory wall** E1311400 (o3452 at (87, −193, 120.9); event 11310100): **Humanity** (84, −191.7, 98.3) is nearby.
2. **Giant coffin room** (ladders E1311140/1311141 down at (80, −196…−201, 127–131)). Six Giant Skeletons stand around a giant sarcophagus holding the **Large Divine Ember** corpse (65.7, −201.2, 155.2; lot 1310500). There is also a **Soul of a Brave Warrior** (69, −201.2, 133.3) and three Bone Towers (no script).
3. **First bonfire** E1311961 at (96, −199.8, 104), with ladders E1311146/1311147.
   - The "totem" corpse with a **White Titanite Chunk** (80, −202.8, 103.1; pickup flag 51310100) wakes four **Bone Towers** E1310203–206 (event 11315060, staggered 0 / 0.2 / 0.9 / 1.0 s).
4. **Patches' kick.**
   - Patches waits at the pit edge (87.1, −195.1, 88.1).
   - Entering region 1313400 (89.4, −195.5, 86.1) plays cutscene **130110**, or **130111** if F1638 is set (variant meaning unverified). The player is warped into the pit at 1312120 (91, −208.1, 83.5) and Patches to 1312121 (event 11310002).
5. **The pit (darkness).**
   - **Skull Lantern** (93, −208.3, 77.5); Large Souls of a Proud Knight at (91, −208, 79.5) and (93.5, −208.5, 80).
   - **Rhea** (74.5, −207.6, 82) with hollowed **Nico** (57.6, −207.6, 88.5) and **Vince** (61.3, −207.7, 89.7); kill both for her reward.
   - **Effigy Shield** (87.3, −205.8, 69.4).
   - The Skull Lantern makes the "lantern resonance lights" glow along the path (§4.8).
6. **Black Knight (halberd)** E1310300 at (74.6, −206.1, 65.8). Skeleton Beasts follow.
7. **White light gate** E1311700 (o3962 at (68.5, −198.3, 52.8); regions 1312600/1312601) is a one-time pass. Beyond it, Skeleton Beasts and Giant Skeletons; **Soul of a Brave Warrior** (86.5, −202.1, 30.8).
8. **Second bonfire** E1311960 at (76.3, −214.3, 43.9).
9. **Split path.**
   - Right: **Covetous Silver Serpent Ring** (84, −209.7, 12). Entering region 1312251 wakes **five Bone Towers** E1310207–211 around it (event 11315070, staggered 0–0.8 s).
   - Also on this path: White Titanite Chunk (70.7, −215.2, 21.1) and Soul of a Brave Warrior (98.1, −218.6, 15.5).
   - Left: ladder E1311148 (92.3, −225.4, 8.3) leads down.
10. **Crystal Lizard** E1310400 at (34, −234.4, −23). **Paladin Leeroy invades** (black NPC sign) as a human host enters the 50 m sphere 1312001 around (41.3, −239.9, −49.3).
11. **Golden fog** E1311710 (o3963 at (45.3, −232.2, −21.6), region 1312610 "王の封印前").
    - Before the Lordvessel is placed, examining it ("Examine brilliant light", 10010410) prints "What the...!" (10010630).
    - Once F11800100 is set it is removed for good (event 11310095; sets F11310096 and clears F401).
12. **Bone Tower ledge** (x −1…−41, y −240…−248).
    - Bone Towers E1310200–202 emerge when the player is within 5 m (event 11315050).
    - Bone Tower E1310250 burrows away (9070, warp to 1312250) and waits until the Giant Skeleton E1310251 is engaged (event 11315080).
    - A Giant Skeleton archer is here. **Soul of a Hero** (−74.6, −253.9, −30.5).
13. **Pinwheel servants and Skeleton Babies** (x −90…−105, y −248…−259).
    - Five Pinwheels.
    - An **infinite Skeleton Baby generator** (9 parts, 1 s interval, max 9 alive, 3 initial, 4 spawn points).
    - **White Titanite Slab** (−96.5, −255.5, −60.6), **Soul of a Hero** (−102.4, −248.1, −43.5), White Titanite Chunk (−104.6, −258.8, −47.6).
14. **Nito's chamber.**
    - Break the wooden barricade o3600 (−110.5, −249.6, −34.3), pass the **Nito fog** E1311990 (o3964 at (−112.3, −249, −33.4)) and drop into the arena (−130…−168, y −265).
    - After the kill, the bonfire E1311950 appears at (−160, −264.9, −34).
    - The **Paladin set** corpse (−145.1, −265, −17.8) appears if Leeroy was killed.

## 4.3 Bonfires

| Bonfire | Object / talker | Flag | Location | Notes |
|---|---|---|---|---|
| Tomb of the Giants (first) | o0200_0002 E1311961; c1000_0002 E1310961 (talk 131001) | 11310984 | (96, −199.8, 104) | Patches' bonfire |
| Tomb of the Giants (second) | o0200_0000 E1311960; c1000_0000 E1310960 (talk 131000) | 11310992 | (76.3, −214.3, 43.9) | |
| Nito's arena (post-boss; "Altar of the Gravelord" name unverified) | o0200_0001 E1311950; c1000_0001 E1310950 (talk 131010) | 11310920 | (−160, −264.9, −34) | Hidden until F7. Event 11310001 shows SFX 90014 and the bonfire 2 s later. |

## 4.4 Enemies

| Sub-zone | Enemy | Model | Count | Parts (entity) | Behaviour | Drops |
|---|---|---|---|---|---|---|
| Descent | Giant Skeleton (greatsword) | c2910 | 8 | _0006–_0013 (no entity), npc 291011, think 291003 | Respawning. _0008 and _0012 share platoon 65464. | lot 29101100 Murakumo |
| Descent / coffin room | Giant Skeleton (bow) | c2910 | 3 | _0014 (65.5, −185.2, 109.9), _0015 (72.9, −188.2, 136), _0016 (85, −202.3, 34.5); npc 291010, think 291001 | Archers | lot 29101000 Longbow |
| Giant coffin room | Giant Skeleton (greatsword) | c2910 | 6 | _0000–_0005 (no entity), npc 291011, think 291002 | Around the Large Divine Ember | Murakumo |
| Coffin room | Bone Tower | c2960 | 3 | _0000–_0002 (no entity) at (76–81.5, −205, 110–113.5) | No script | lot 29600000 White Titanite Chunk |
| Pit (totem) | Bone Tower | c2960 | 4 | _0003–_0006 → E1310203–1310206 | Hidden (waiting 9000) until pickup flag 51310100 (White Titanite Chunk totem corpse), then rise 9060 | same |
| Ring trap | Bone Tower | c2960 | 5 | _0007–_0011 → E1310207–1310211 | Hidden until region 1312251 | same |
| Past golden fog | Bone Tower | c2960 | 4 | _0012–_0014 → E1310200–202; _0015 → E1310250 | Emerge at 5 m. E1310250 retreats to 1312250. | same; npc 296000, HP 127, 250 souls |
| Throughout | Skeleton Beast | c2950 | 7 | _0000–_0006 (no entity), npc 295000, think 295001 | Some placed on slopes (rot x 1–8°) | lot 29500000 (none); HP 194, 1500 souls |
| Black WT only | Skeleton Beast | c2950 | 5 | _0007–_0011 → E1310905–909 (npc 295001) | Events 11315040–42 | |
| Black WT only | Giant Skeleton | c2910 | 5 | _0022–_0026 → E1310900–904 (npc 291015) | | Murakumo |
| Past golden fog | Giant Skeleton | c2910 | 2 | _0017 (bow, npc 291013, (−44.4, −248.6, −22.5)), _0018 E1310251 (greatsword, npc 291014) | | lots 29101300 / 29101400 |
| Pit / gate | Black Knight (Halberd) | c2795 | 1 | _0000 E1310300, npc 279030, think 279003 | **One-time** (event 11310820 slot 0) | lot 27903000 **Black Knight Halberd + Shield**, chain 27903001 White Titanite Chunk |
| Below the ladder | Crystal Lizard | c3300 | 1 | _0000 E1310400, npc 330050 | One-time | lot 33005000 Twinkling Titanite + random |
| Pinwheel area | Pinwheel (servant) | c3320 | 5 | _0000–_0004 (no entity), npc 332002. Think 332003 for _0000/_0001; 332002 for _0002–_0004. | HP 220, 1000 souls. Respawning. | lot 33200200 masks; chain 33200201 White Titanite Chunk / Slab |
| Pinwheel area | Skeleton Baby | c2940 | 9 | _0000–_0008 at (−90.8, −258.8, −27.4) | **MSB Generator**: 9 parts, 4 spawn points, interval 1 s, MaxNum 9, initial 3. Infinite. HP 15, 100 souls. | lot 29400000 Humanity (chance) |
| Nito arena | Skeleton | c2900 | 3 | _0000 E1310120, _0001 E1310121, _0002 E1310122 (npc 290030/290032) | Lie dormant (9001) before the fight. Immortal/revive until Nito dies, then killed with souls (event 11315350). | lots 29003000 / 29003200 |
| Nito arena | Giant Skeleton | c2910 | 3 | _0019 E1310125, _0020 E1310124, _0021 E1310123 (npc 291012) | Same | lot 29101200 (none) |
| Online | Vagrants | c3490 / c3491 | 2 / 4 | c3490_0000 E1313900, _0001 E1313910; c3491_0000/0002 E1313901/902, _0001/0003 E1313911/912 | Online only | |
| (dummies) | Skeleton | c2900 | 6 | dummy c2900_0008–0013 → E1310110–115 in Nito's arena | Cut Nito body-part summons (§4.5) | |

## 4.5 Boss — Gravelord Nito

| Field | Value |
|---|---|
| Model / part | c5220_0000 **E1310800** (npc / think 522000), HP 1877. The covenant Nito c5220_0001 E1310810 (think 1, talk 131646, **InitAnim 9000**) lies in the giant sarcophagus o3880 at (−166.8, −260.6, −33.5). |
| Arena | Box 1312999 (30 × 1.2 × 60 at (−136, −266, −43)); BGM region 1312990, sound E1313800. |
| Fog | E1311990 (o3964) with SFX 1311991. Entry segment 1312998 to target 1312997. Phantom version 11315391; DSR multiplayer 11315843/846. |
| Intro | Event 11315392 (host, F11310050 OFF, in 1312999). Cutscene **130100** plays once per load (event 0 resets F11310000 on load). Boss bar name 5220. The six arena skeletons rise with anim 9061 at 0.3 / 0.5 / 0.2 / 0.3 / 0.7 s offsets. |
| Mechanics | **Gravelord Sword Dance** is done by script (events 11315398 and 11314398). When Nito targets the player and raises event message 700, the anchor object E1311250 (o0020) warps to the player's position. On message 710, a damaging object (behaviour 11300, radius 0.3) plus SFX 15224 spawns there, so blades erupt under the player. Skeletons in the arena revive until he dies. Poison/toxic miasma is AI-side. Wiki: weak to fire and divine (unverified). |
| Cut | Body-part breaking (events 11315397 and 11315370 are never initialized). NPC parts 5220/5221/5222 (200 HP each), anims 8000/8010/8020, part objects E1311200–1311202 (o3900/o3901). Each break would release two skeletons from the dummies E1310110–115. Event 11315396 still force-kills those dummies on death. |
| Death | Event 11310001: sets **F7**, Handle Boss Defeat, achievement 35, removes the fog, kills covenant Nito, shows bonfire E1311950. |
| Rewards | 60,000 souls; **Lord Soul (Nito)** (lot 2560) + Humanity (common event 970). |
| World change | Covenant Nito gone (but the coffin still works). Lord Soul can be placed in the Lordvessel. |

## 4.6 NPCs

| NPC | Part / entity | Talk / think | Where | Notes |
|---|---|---|---|---|
| **Rhea of Thorolund** | c0000_0002 E6071, NpcParam 6070 | talk 131607 / think "White Saint" 6070 | (74.5, −207.6, 82) in the pit | **InitAnim 7885** (kneeling/praying pose; exact name unverified). Enabled only if F1173 or F1174. Quest steps below the table. |
| **Nico / Vince of Thorolund (hollow)** | c0000_0003 E6091 / c0000_0004 E6101 | think 6091 / 6101 | (57.6, −207.6, 88.5) / (61.3, −207.7, 89.7) | Platoon 65465 (shared). Hostile Ally team. Enabled only if F1216 / F1226. Deaths set F1214 / F1224. No drops. |
| **Patches (Trusty)** | c0000_0005 E6321 (npc 6321 / think 6320) | talk 131632 | (87.1, −195.1, 88.1) | Present if F1623–F1625. Quest steps below the table. |
| **Paladin Leeroy** (invader) | c0000_0007 E6551, npc 6551 | think "Temple Knight II Cemetery 2 (Invasion)" | Spawn 1312000 (41.3, −239.9, −49.3) | **Black (red) NPC invasion sign** (event 11315030): host human, Nito alive, Leeroy not yet killed, in sphere 1312001 (50 m). Re-attempts every 20 s until he is killed. Death sets F11310810 (event 11310810). Drops **Grant** (lot 6550) + **Sanctus** (6551) + Humanity (6552); wiki says 20,000 souls. His death also **enables the Paladin set corpse** E1311600 in Nito's arena (event 0). |
| **Gravelord Nito (covenant)** | c5220_0001 E1310810 | talk 131646 | Giant sarcophagus | Only when F11310050 is set (arrived via the coffin) and F7 is OFF (event 11310051). Covenant ranks and rewards are listed below the table. |

Rhea quest steps:
- **Arrival in the Tomb** (event 11310530) needs all of these:
  - Rhea, Petrus, Nico and Vince all neutral and alive (F1176–1181, 1195–1198, 1213–1215, 1223–1225 all OFF).
  - F1171 (Rhea's pre-Tomb state).
  - F1202 (Petrus state), F1211 (Nico state) and F1221 (Vince state).
  - Plus one of: F11020600 (a Firelink flag; meaning unverified), F11300005 (entered Pinwheel's arena), or the event already done.
- That event sets **F1173** (Rhea in the Tomb), Petrus F1192, Nico F1216 (hollow), Vince F1226 (hollow), and sets 11020693.
- Killing Nico and Vince (F1214 + F1224) with Rhea alive gives **F1174** (rescued; event 11310531).
- Talk flag 11310590 awards lot 1210 **Replenishment** (common 911).
- F1174 + 11310590 + unloaded gives **F1175**: Rhea leaves and appears in the Undead Parish (m10_01 uses F1175; event 11310532).
- Hostile: F1176 (event 11310502) or F1179 (event 11310503, AI ID 1).
- Dead: F1177 / F1180 (events 11310533 / 11310534) → lots 6070 Ivory Talisman, 6071 Pendant, 6072 Humanity.
- Shop rows 4200–4209 (Divine Blessing, Heal, Great Heal Excerpt, Great Heal, Homeward, Force, Wrath of the Gods, Seek Guidance, Magic Barrier, Talisman) apply wherever she vends; outside this map (unverified where).

Patches quest steps:
- Enabled as **F1623** by event 11310540: (F1620 or F1621) with Pinwheel dead, or F1173.
- The **kick** (event 11310002) leads to **F1624** (event 11310541).
- Talk flag 11310595 sets **F1625** with Hostile Ally team (event 11310543). Exact dialogue branch unverified.
- Attacking him: F1627 (event 11310501). Death: F1628 (event 11310542) → Crescent Axe / Humanity.
- Talk flag 11310594 awards **Twin Humanities** (lot 1240).
- Shop rows 1600–1617 (Eye of Death, Divine Blessing, Prism Stone, Humanity, Twin Humanities, Heal, Great Heal Excerpt, Crescent Axe, Mace, Canvas/Thorolund Talisman, the three masks, Cleric set). The wiki says he vends later at Firelink.

Gravelord covenant (Nito):
- Talking sets F11315020. Event 11310053 then plays the player kneel anims 7910, a 7911 loop and 7912 (stand).
- Rank rewards via common 911/890:
  - Rank 0: 11310592 → lot 1220 **Gravelord Sword**; 11310580 → lot 1221 **Gravelord Sword Dance**.
  - Rank 1: 11310593 → lot 1230 **Gravelord Greatsword Dance**.
- Lot 5010 [Gravelord Servant] Eye of Death also exists.
- **Return coffin** E1311300 (ObjAct 11315340, enter only): lying down for 3 s plays cutscenes **130121** + **130021** and warps to the Catacombs point 1302010 (event 11310052). Event 11310054 re-arms it within 2 m.

## 4.7 Items (route order)

| Item | Lot | Location |
|---|---|---|
| Large Soul of a Proud Knight | 1310010 | (42.4, −153.6, 126.1) |
| **Eye of Death ×3** | 1310080 | (51.8, −166.8, 111.1) |
| Large Soul of a Proud Knight ×3 | 1310070 / 1310300 / 1310000 | (68, −170.3, 109.7), (65.4, −173.6, 105.3), (80.1, −175.7, 104.2) |
| **Humanity** | 1310020 | (84, −191.7, 98.3) |
| Soul of a Brave Warrior | 1310040 | (69, −201.2, 133.3) |
| **Large Divine Ember** | 1310500 | (65.7, −201.2, 155.2), corpse o0503 in the giant coffin |
| White Titanite Chunk (totem; wakes Bone Towers) | 1310100 | (80, −202.8, 103.1) |
| **Skull Lantern** | 1310090 | (93, −208.3, 77.5) pit |
| Large Soul of a Proud Knight ×2 | 1310030 / 1310050 | (91, −208, 79.5), (93.5, −208.5, 80) |
| **Effigy Shield** | 1310120 | (87.3, −205.8, 69.4) |
| Soul of a Brave Warrior | 1310110 | (86.5, −202.1, 30.8) |
| White Titanite Chunk | 1310160 | (70.7, −215.2, 21.1) |
| **Covetous Silver Serpent Ring** | 1310140 | (84, −209.7, 12) (Bone Tower trap) |
| Soul of a Brave Warrior | 1310180 | (98.1, −218.6, 15.5) |
| Soul of a Hero | 1310200 | (−74.6, −253.9, −30.5) |
| **White Titanite Slab** | 1310220 | (−96.5, −255.5, −60.6) |
| Soul of a Hero | 1310230 | (−102.4, −248.1, −43.5) |
| White Titanite Chunk | 1310290 | (−104.6, −258.8, −47.6) |
| **Paladin Helm / Armor / Gauntlets / Leggings** | 1310240–1310243 | (−145.1, −265, −17.8) Nito arena; corpse E1311600 only after Leeroy dies |

Unused corpses (missing lots): 13, 15, 17, 19, 21, 25, 26, 27, 28.

## 4.8 Interactive objects and events

| Object | Entity | Event | Function |
|---|---|---|---|
| Skull Lantern resonance lights | Map SFX E1313200–1313248 (regions "ランタン共鳴明かり 00–48", from the pit through the second-bonfire area) | 11315091 / 11315092 / 11315150 ×49 | While the player uses the **Skull Lantern** for 2 s, F11315090 is set and two light chains propagate (slots 0–23 and 24–48). They switch off 3 s after the lantern stops. |
| Patches kick | regions 1313400 / 1312120 / 1312121 | 11310002 | Cutscene 130110/130111 plus warps. |
| Golden (Lordvessel) fog | E1311710 (o3963) + SFX 1311711 | 11310095 | Blocks until F11800100. |
| White light gate | E1311700 (o3962) | 11310090 | One-time. |
| Nito fog | E1311990 | 11315390–11315393 | Boss entry. |
| Wooden barricade | o3600_0000 (no entity) | — | Breakable before the Nito fog (wiki). |
| Illusory wall I | E1311400 (o3452) | 11310100 | Hit to dissolve. |
| Gravelord coffin (return) | E1311300 (o3060) | 11310051 / 11310052 / 11310054 | See §4.6. |
| Sword Dance anchor | E1311250 (o0020) | 11314398 | See §4.5. |
| Ladders | E1311140–1311148 | event 0 | Flags 11310010–27. |
| Skeleton Baby generator | MSB generator (event ID 90) | — | See §4.4. |
| Dev message | 3100 (visible; text lookup unreliable) | — | |
| F401 | set on load (host) | cleared when the golden fog opens | Meaning unverified (seal-state flag; m16 uses F404 the same way). |

## 4.9 Porting notes

- **Darkness.** The map lighting must be near-black. The light sources are the Skull Lantern (a left-hand weapon light), the Sunlight Maggot and Cast Light. The resonance-light SFX chain is keyed to "player using Skull Lantern" (EMEVD condition 4:10), which DS2 lacks, so emulate it with an equip/SpEffect check.
- **Golden fog** gated on Lordvessel placement (cross-map flag F11800100 from m18_00).
- **Infinite Skeleton Baby generator** (1 s, cap 9), and the toxic buildup.
- Bone Tower burrow / emerge (9000 → 9060, 9070 retreat warp).
- **NPC invasion (black sign) of Leeroy** with a re-roll every 20 s, and a reward corpse appearing in another room.
- Patches' kick cutscene with a forced warp.
- The **coffin warp pair** between m13_00 and m13_01.
- Nito's script-driven Sword Dance.
- Nito arena skeleton revive.

---

# 5. New Londo Ruins — lower (drained) and The Abyss (m16_00_00_00)

## 5.1 Header

- **Prerequisites:**
  1. **Lordvessel** (F710) → Ingward gives the **Key to the Seal** (or kill him).
  2. Open the seal door E1601111 and pull the **sluice lever** E1601101, which drains the water (cutscene 160000, F11600100).
  3. For the Abyss: the **Covenant of Artorias** ring (SpEffect 2200; from Sif in m12_00). Without it, dropping into the Abyss is death.
- **Entrances after draining:**
  - The Cathedral elevator ("聖堂", E1601210) from the upper plaza.
  - The floodgate elevator ("水門", E1601220) from the seal room.
  - The open floodgate E1601100 to the Valley of Drakes.
  - The Four Kings fog E1601990 (o6606 at (81.5, −163, −0.6)), which opens onto the abyss shaft.

## 5.2 Walkthrough

1. **Drain.** Use the Key to the Seal on door E1601111, then pull lever E1601101 (event 11600101: player anim 8020 at lever dummy 101; the lever plays anim 0).
   - Event 11600100 waits on 11600101 and plays **cutscene 160000** (water drains), then sets **F11600100**.
   - On every load after that: the floodgate E1601100 is set to anim 10 (open); hits 1603100/1603102/1603103/1603104 are disabled; the **water object E1601120** is disabled; F404 is cleared.
   - The collision swap (event 11600150): flooded collisions 1603200–1603217 are disabled; drained collisions **1603250–1603267** and hits 1603121/1603122 are enabled; drained map pieces **m4000B0, m4001B0 and m4010B0–m4018B0 (E1603110–1603120)** are enabled; water ambience E1603850 is disabled.
   - Cathedral/floodgate lever ObjActs E1601211/E1601221 become usable (event 11600199).
2. **Floodgate elevator** (E1601220 at (92.7, −170.5, −27.6); y −151 ↔ −170.4; levers E1601221 upper / E1601222 lower) down to the drained streets. The open **floodgate** connects to the Valley floor.
3. **Drained streets** (y −158…−168).
   - **Darkwraiths** and two **Mass of Souls** (with Wisps).
   - Loot: Large Soul of a Proud Knight (112.3, −165, 15.8), **Humanity** (100.8, −163.2, 47.5), Soul of a Brave Warrior (97.1, −158.1, 31.3), Large Soul of a Proud Knight (50.2, −165, 37.1), **Cracked Red Eye Orb ×6** (66.9, −164, 26.2), Large Souls of a Proud Knight at (46.4, −162, −2.8) and (56.5, −162.4, 19.7).
   - Chests: **Titanite Chunk** at (53.6, −160, −11) and (30.9, −165, 30.2). A Stone Wall P (o6700, no entity) sits at (44.6, −165, 30.2) next to the second chest (probably a breakable, unverified).
4. **Cathedral elevator** (E1601210 at (100.4, −163.3, 59.1); y −143.2 ↔ −163.2; levers E1601211 upper (99.5, −143.2, 55.3) and E1601212 lower) is the shortcut back to the upper plaza.
5. **White light gate CP2** E1601702 (o6605 at (101.6, −163.2, 21.8)) is a one-time pass.
6. **Four Kings.**
   - Beatrice's sign is at (83.2, −161.5, −18.4).
   - The fog E1601990 (entry segment 1602998, target 1602997, entry-complete box 1602996) opens onto the hole. Drop into the Abyss: shaft region 1602900 (cylinder r 4.5, h 90), arena floor hit E1603300 at y −311.
7. **After the kill:**
   - The Abyss bonfire E1601950 appears at (85.3, −311, 3.2).
   - **Darkstalker Kaathe** appears (§5.6) if the Lordvessel has not been placed yet.
   - The Witch set appears in the Valley stairs (§2).

## 5.3 Bonfires

| Bonfire | Object / talker | Flag | Location | Notes |
|---|---|---|---|---|
| The Abyss | o0200_0001 E1601950; c1000_0001 E1600950 (talk 160010) | 11600920 | (85.3, −311, 3.2) | Registered in event 0 only if F13. Disabled until the Kings die; then SFX 90014, enabled after 2 s (event 11600001). Spawn points 1602960, 1602961 and boss 1602950. |

## 5.4 Enemies (drained + Abyss)

| Sub-zone | Enemy | Model | Count | Parts | Behaviour | Drops |
|---|---|---|---|---|---|---|
| Drained streets | **Darkwraith** | c2390 | 11 | c2390_0011 E1600100, c2390_0001–0010 → E1600101–1600110 (npc 239000; _0006 think 239001) | **Backread-removed until drained** (event 11605100). HP 239, 1200 souls. Respawning. Humanity drain grab (AI). | lot 23900000 Titanite Chunk / Slab, chain 23900001 **Dark Hand** (chance); humanity lot 5230 |
| Black WT only | Darkwraith | c2390 | 6 | c2390_0012–0017 → E1600900–1600905 (npc 239003) | Black World Tendency > 50 *and* drained (events 11605090/91/92) | same; humanity 5240 |
| Lower east | **Mass of Souls** | c3500 | 1 | _0000 E1600400 (96.7, −163.2, 41.2) | HP 936, 5000 souls. **One-time** (event 11600850). Absent until drained. | humanity lot 5280 |
| Lower west | Mass of Souls | c3500 | 1 | _0001 E1600401 (59, −160, −8.4) | same | |
| (spawned) | Wisp | c3501 | 12 | _0000–_0005 → E1600410–415 (from 1600400); _0006–_0011 → E1600416–421 (from 1600401) | Event 11605200: start disabled. Each is enabled and warped to the Mass's dummy 100 with anim 8300 when the Mass raises event message 300; the first needs F11600100, later ones chain on the previous slot. Removed on explode (message 400; event 11605250). HP 9. | none |
| Abyss | **Four Kings** | c5390 | 5 | _0000 E1600800 (shared HP pool) + _0001–_0004 E1600801–804 | See §5.5 | |

## 5.5 Boss — Four Kings

| Field | Value |
|---|---|
| Parts | **E1600800** (c5390_0000, npc 539000, **HP 4320**) is an invisible HP-pool dummy at (0, −350, 0) with gravity off. Kings **E1600801–1600804** (npc 539001, **HP 1080** each = 4 × 1080) at (12–56, −311, −13…15). |
| Arena | The Abyss floor: hit E1603300 (h0040B0, mapName −1601), y −311. BGM region 1602990 (200 × 18 × 200), sound E1603800. |
| Entry | The Four Kings fog E1601990 at the bottom of the drained ruins. Host: 11605390; white phantom: 11605391 (after the host); 11605393 sends the boss-entry notification. Then the **fall**, event 11605398: with **SpEffect 2200** (Artorias ring), landing in 1602999 gives 2 s of invincibility. **Without it**, entering the shaft cylinder 1602900 disables map hits, so the player falls forever and dies (F8120 → "You were taken by the abyss"). After the Kings die, no ring is needed (invincible landing only). Hits E1603310/1603311 (h9000B0) are enabled only while the player lacks SpEffect 2200 and the Kings are alive (event 11605397; probably kill/catch volumes, unverified). Common event 717 sets F717 while standing on E1603300 without F710. |
| Start | Event 11605399: when the host stands on E1603300, **Generator E1603000** is enabled. Event 11605392: kings 802–804 are force-killed at setup (the generator revives them later) and 801 is disabled. Seven seconds after F11605399 + area 1602999, E1600801 is enabled with **anim 6200** (rising from the dark); the AI turns on; the boss bar (name 5390) is bound to E1600800; **referred-damage pairs** 801–804 → 800 make every hit on any King drain the shared pool. |
| Spawns | MSB Generator 1603000: parts c5390_0001–0004, 3 spawn points, **interval 40 s**, MaxNum 4, initial count 4. A new King roughly every 40 s while any slot is dead. |
| King "death" | Event 11605350: each King is immortal. At ≤1% of its own HP while the pool is above 0, it plays **anim 7000** (vanish) and is disabled/killed without souls; the generator later brings a new one. When the pool reaches 0, all die. |
| AI sync | Event 11605396 sends AI command 1 to each live King until it dies or raises message 500. |
| Death | Event 11600001: the pool reaches 0 → kings fade out (DSR 2004:48, 3 s); **cutscene 160010** with a warp to 1602120 (59.7, −311, 2.8); F4047; **F13**; generator off; Handle Boss Defeat; boss fog removed; Witch-set corpse enabled; achievement 37; bonfire E1601950. |
| Rewards | 60,000 souls; **Bequeathed Lord Soul Shard** (lot 2630) + Humanity (lot 9010). |
| Summon | **Witch Beatrice** (white sign) — see §5.6. |
| World change | Kaathe can appear, the Abyss bonfire exists, Ingward can leave for Firelink (F1313), and the Witch set appears. |

## 5.6 NPCs

| NPC | Part / entity | Talk | Where | Notes |
|---|---|---|---|---|
| **Witch Beatrice** (summon) | c0000_0006 E6520 (npc 6521, think 6520) | — | Sign at spawn point 1602000 (83.2, −161.5, −18.4) | **White sign** (events 11605030 and DSR variant 11605029): host, human, Kings alive, Beatrice loaded, player within 5 m, and **F11200300** (she was summoned for the Moonlight Butterfly in m12_00). Summoned flag 11605031, dismissed 11605034; event 11605990 erases the sign. Event 11605032 walks her through the fog with anim 7410. Event 11605033 gives her a 2 s invincible landing if she has SpEffect 2200. If she dies, her set shows up (§2). |
| **Darkstalker Kaathe** | c5330_0000 **E6340** (npc 533001, think "Kingseeker Frampt" 533000) | 160634 | Abyss floor (95.7, −311, 3.5) | **InitAnim 7003**; immortal. Quest steps below the table. |

Kaathe quest steps:
- **Appears**: disabled while F15 (Gwyn dead), F1676 or F1677. Event 11600537 enables him with **anim 9060** (appear) when F1670 (initial state) is set, the Lordvessel has *not* been placed (F11800100 OFF), F13 is set and the player is host. That sets **F1671**.
- F1671 + F710 (has Lordvessel) → **F1672** (event 11600538).
- F1672 + Lordvessel placed → **F1673** (event 11600539).
- **Lordvessel via Kaathe** (event 11606200): F1672/1673/1674 + in m16 + **F821** (talk choice). This clears F821, sets F831, enables Kaathe-at-Altar E6341 in m18_00, plays **cutscene 160040** warping to m18_00 point 1802110, then cutscene **180041**, and sets F823.
- Vanish (anim **7005**; sets **F1677**):
  - event 11600540: F1672/F1678 + talk flag 11600590;
  - event 11600541: F11600590, or hit to ≤90% HP while further than 15 m away.
- **Darkwraith covenant**: kneel via common event 840 slot 5 (flag 845, player anim 7905). Rewards via common 911:
  - Rank 0: 11600594 → lot 1380 **Dark Hand**.
  - Rank 1: 11600595 → 1390 **Red Eye Orb**.
  - Rank 2: 11600596 → 1400 **Darksword**; 11600580–583 → 1401–1404 **Dark Mask/Armor/Gauntlets/Leggings**.
- Shop row 6100: Cracked Red Eye Orb.
- Event 8200 marks rewards already owned in NG+.

## 5.7 Items

Drained-street loot is listed in §5.2 (lots 1600250, 260, 270, 280, 290, 310, 330, 370, 510).

The Abyss has no pickups; boss rewards are listed in §5.5.

## 5.8 Interactive objects and events

| Object | Entity | Event | Function |
|---|---|---|---|
| Seal door | E1601111 | 11600110 | Key to the Seal |
| Sluice lever | E1601101 (o6110_0000); a second lever o6110_0001 (no entity) at (121.3, −152, −21.1) has InitAnim 10 | 11600101 / 11600100 | Drain plus cutscene 160000 |
| Floodgate | E1601100 (o6000 "Door BF") | 11600100 | Opens to the Valley (anim 10) |
| Water plane | E1601120 (o6500) | 11600100 | Disabled when drained |
| Drained geometry / collisions | E1603110–1603120, 1603121/122, 1603200–217 ↔ 1603250–267 | 11600150 | Flooded ↔ drained swap |
| Cathedral elevator | E1601210 + levers E1601211/1601212 (ObjAct 11600212/11600213) | 11600210 / 11600251 / 11600199 | Lift anims 2/10/22 and 3/13/21; dead until drained ("Contraption does not move") |
| Floodgate elevator | E1601220 + levers E1601221/1601222 (ObjAct 11600222/11600223) | 11600220 / 11600252 | Lift anims 4/10/24 and 5/15/21; dead until drained |
| CP2 light gate | E1601702 | 11600090 slot 1 | One-time |
| Four Kings fog | E1601990 (o6606) + SFX 1601991 | 11605390 / 11605391 / 11605393 / 11605843 / 11605846 | Boss fog |
| Abyss floor | hit E1603300 | 11605397 / 11605398 / 11605399 | Ring check, generator start |

## 5.9 Porting notes

- **World-state toggle.** The drained/flooded geometry, collision, water and enemy swap is the biggest one-off. The DS2 port needs two collision sets, plus map-piece enable/disable and backread-off enemies until F11600100.
- **The Abyss.** A bottomless arena whose floor exists only with SpEffect 2200 (Covenant of Artorias); otherwise fall-to-death with a custom message. NPC summons need the same protection.
- **Four Kings.** Shared HP pool (referred damage), a generator spawning a King every 40 s, per-King vanish at 1% HP, a post-fight cutscene with warp, and the Witch-set reward.
- **Kaathe.** Covenant join, the Lordvessel-placement cross-map cutscene (m16 → m18_00), and the vanish-on-attack logic.
- **Mass of Souls → Wisp spawning** (warp to dummy, chained slots).
- **Darkwraith humanity-drain grab** and black-tendency spawns.

---

# Area completion checklists

## New Londo Ruins (upper, flooded)
- [ ] Firelink ↔ New Londo elevator E1601200 (two levers, cross-map lever E1021201)
- [ ] Online fogs E1601994 / E1601996 / E1601998
- [ ] 15 cowering Hollows with idles 9002–9012
- [ ] 32 male + 2 female ghosts: hide-in-wall 9000 / emerge 9060 / lunge 3005–3007 triggers and radii, plus leash boxes
- [ ] Ghost hit rule (curse / Transient Curse / ghost weapons) and the Curse status
- [ ] Urn-trapped corpses E1601610/1601611 ↔ urns E1601310/1601311
- [ ] Kick-down ladder E1601142; ladders E1601140/1601141
- [ ] White light gate CP1 E1601700
- [ ] 3 chests (Very Large Ember chest upper; 2 Titanite Chunk chests lower)
- [ ] Rickert (cage, anim 7830, shop, Magic/Enchanted/Crystal smithing, hostility/death flags)
- [ ] Ingward (Key to the Seal via F710 + talk / on death; moves to Firelink at F1313; curse removal; shop)
- [ ] Crestfallen hollow (F1464)
- [ ] Valley door E1601110 (Key to NL Ruins / Master Key)
- [ ] Seal door E1601111 (Key to the Seal)
- [ ] All pickups in §1.7 (Rare Ring of Sacrifice, Cursebite, Fire Keeper Soul, Red Tearstone, Humanity…)
- [ ] Water plane E1601120 and flooded collision set
- [ ] Dev messages 6000–6003 (optional)
- [ ] (optional) cut bonfire E1601960 and the Darkwraith mini-boss

## Valley of Drakes + Darkroot Basin bonfire
- [ ] Darkroot Basin bonfire E1601961 (flag 11600984, talker E1600961)
- [ ] Basin ↔ Valley elevator E1601230 with levers E1601231/1601232
- [ ] Undead Dragon E1600500 (lie 29000 / wake 29060, gravity and map-hit off, one-time, Dragon Scale)
- [ ] 6 Drakes (respawning, Dragon Scale drops)
- [ ] Items: Brigand set + Spider Shield, Humanity, Astora's Straight Sword, Dragon Crest Shield, Soul of a Proud Knight, Large Soul of a Nameless Soldier
- [ ] Witch set corpse E1601600 (appears after F13)
- [ ] Ladder E1601141 / floodgate link to New Londo; Blighttown cave link (Key to NL Ruins chest)

## The Catacombs
- [ ] 3 bonfires (1301960 / 1301961 / 1301962) and online fog E1301994
- [ ] 6 Necromancers (one-time) + 49 bound skeletons (revive until master dies) + 8 black-WT skeletons
- [ ] Skeleton bone-pile ambush (9001 → 9061, 5 m)
- [ ] Necromancer 1300100 flee nests
- [ ] 12 Wisps on patrol routes (explode once)
- [ ] 10 Bonewheels (3 black-WT)
- [ ] Giant Skeleton ceiling drop (E1300050 + arch E1301020)
- [ ] Black Knight (Greataxe) E1300400 (one-time)
- [ ] Titanite Demon E1300300 (sleep, patrol, ignores coffin occupant, one-time)
- [ ] 2 Crystal Lizards
- [ ] Levers E1301000 / E1301001 and walls E1301100 / E1301101
- [ ] Spiked rotating bridges E1301102 / E1301103 (levers, spikes, navmesh)
- [ ] Patches' bridge trap
- [ ] Collapsing floor tiles E1301010–1301015; 19 statue blade traps E1301200–1301219
- [ ] Illusory wall K E1301050; white light gate E1301700
- [ ] Spiral-pit cutscene 130010 and Vamos bricks; Vamos (anim 9003, smith, shop)
- [ ] Gravelord coffin E1301104 (Eye of Death, 30 s, warp to Tomb)
- [ ] Leeroy white sign
- [ ] Pinwheel (clones, teleports, intro cutscene 130000, F6, exit wall E1301040, ladder E1301143, rewards)
- [ ] All pickups in §3.7

## Tomb of the Giants
- [ ] Darkness lighting; Skull Lantern pickup; lantern resonance lights E1313200–1313248
- [ ] 3 bonfires (1311961 / 1311960 / post-Nito 1311950)
- [ ] 27 Giant Skeletons (incl. archers, coffin-room six, Nito-arena three, black-WT five)
- [ ] 12 Skeleton Beasts (5 black-WT)
- [ ] 16 Bone Towers (totem trigger F51310100, ring trap 1312251, ledge emerge, E1310250 retreat)
- [ ] Skeleton Baby generator (9, 1 s, cap 9); 5 Pinwheel servants
- [ ] Black Knight (Halberd) E1310300; Crystal Lizard E1310400
- [ ] Patches kick (cutscene 130110/111 + warps), quest flags, Twin Humanities
- [ ] Rhea / Nico / Vince (F1173 → F1174 → F1175, Replenishment)
- [ ] Leeroy black-sign invasion (Grant / Sanctus, Paladin set corpse in the arena)
- [ ] White light gate E1311700; golden fog E1311710 (needs F11800100)
- [ ] Illusory wall E1311400; ladders E1311140–1311148; wooden barricade
- [ ] Nito (cutscene 130100, Sword Dance anchor E1311250, revive skeletons, F7, Lord Soul, bonfire)
- [ ] Gravelord covenant Nito E1310810, kneel anims, rank rewards, return coffin E1311300
- [ ] All pickups in §4.7 (Large Divine Ember, Covetous Silver Serpent Ring, Effigy Shield, White Titanite Slab…)

## New Londo (drained) + The Abyss
- [ ] Sluice lever E1601101 → cutscene 160000 → F11600100
- [ ] Floodgate E1601100 open; water plane off
- [ ] Collision/map-piece swap (1603200–217 ↔ 1603250–267, E1603110–1603120)
- [ ] Cathedral and floodgate elevators (dead until drained)
- [ ] 11 Darkwraiths (+6 black-WT), absent until drained
- [ ] 2 Mass of Souls (one-time) + 12 Wisp spawns
- [ ] White light gate CP2 E1601702; drained-street loot and 2 chests
- [ ] Beatrice summon (needs F11200300)
- [ ] Abyss fall rule (SpEffect 2200 / death F8120)
- [ ] Four Kings (HP pool, referred damage, 40 s generator, vanish at 1%, anim 6200 entry, cutscene 160010, F13, rewards)
- [ ] Abyss bonfire E1601950
- [ ] Kaathe (appear 9060 / vanish 7005, Lordvessel cutscene 160040 + 180041 to m18_00, Darkwraith covenant ranks, shop)
- [ ] Witch set corpse enable (cross-ref §2)

---

## Appendix — cut / unused content in these MSBs (optional for the port)

- **m16:**
  - Darkwraith mini-boss E1600810 (events 11605380–85, 11600900; drops the Key to the Seal; BGM box at Ingward's tower).
  - New Londo bonfire E1601960 (dummy talker E1600960).
  - Female-ghost re-home event 11605101 (never initialized; home 1602300).
  - Dummy flying drakes c3520_0000–0006 and _0008.
  - Dummy c0000_0005 E6262; dummy Darkwraith c2390_0000; dummy female ghost c2680_0003; dummy object o0500_0029.
- **m13_00:**
  - Post-Pinwheel bonfire E1301950 (dummy talker E1300950).
  - Dummy Vamos c2920_0001; dummy skeletons E1300018/19; dummy wisps E1300361/362.
  - Corpses with missing lots.
- **m13_01:**
  - Nito body-part skeleton summons (events 11315397/11315370; dummies E1310110–115; objects E1311200–202).
  - Black Knight lot rows 27905xxx are unused.
