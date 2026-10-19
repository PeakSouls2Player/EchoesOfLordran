# DS1 content census 02: Undead Burg, Lower Undead Burg, Undead Parish, The Depths

Sources (all read-only, DSR install): `map/MapStudio/m10_01_00_00.msb` and `m10_00_00_00.msb` (via `lr msb1-dump`),
`event/m10_01_00_00.emevd.dcx`, `event/m10_00_00_00.emevd.dcx` and `event/common.emevd.dcx` (via `lr emevd-dump`), DSR
params (`NpcParam`, `ItemLotParam`, `ObjActParam`, `ShopLineupParam`). MSB/EMEVD values are ground truth. Everything
labelled *(interp.)* is my reading of the data. Everything labelled *(unverified)* is from general DS1 knowledge and was
not confirmed in the files.

Conventions:
- Positions are MSB world coordinates `(x, y, z)`. They're given so the port can find things on the converted map.
- "Entity" is the MSB EntityID. `-1` means no entity id (EMEVD cannot target it).
- "BP set" means black-phantom variants. They're enabled only while the world tendency is pure black (>50); see Porting notes.
- "Vagrant" parts (c3490/c3491) are online-only vagrant spawns, so offline they never appear *(interp.)*.
- Item lots chain: lot N also awards N+1, N+2… while those rows exist. A treasure listed as "lot X [missing]" points at an
  ItemLotParam row that does **not exist in DSR**, so the pickup gives nothing. These are probably cut items *(interp.)*.

One MSB (m10_01_00_00) covers four player-facing areas: **Undead Burg**, **Lower Undead Burg**, **Undead Parish**, and
**Havel's watchtower**. The Depths is its own MSB (m10_00_00_00). The EMEVD for m10_01 contains both Burg and Parish logic.

---------------------------------------------------------------------------------------------------------------------

## 1. Undead Burg (upper Burg), m10_01_00_00

### 1.1 Header
- **Map**: m10_01_00_00 (area 10, block 1). The Burg part spans roughly x -70…+70, y -25…+30, z -130…-20.
- **Entrances**
  - From **Firelink Shrine (m10_02)**: climb the aqueduct stairs. The player arrives at MSB player `c0000_0001` (entity
    1010998) at (-47.77,-21.79,-35.62), next to "white light" fog #1 (object 1011700, see 1.7).
  - Bonfire warps: Undead Burg bonfire, Sunlight Altar bonfire (warp player point `c0000_0009` entity 1010981 at 23.1,9.5,-24.4).
- **Exits**
  - Lower Undead Burg, three ways: (a) the Basement-Key door at the foot of the Taurus tower near the Hellkite bridge
    (1011311); (b) the one-way "mid→lower" door near the bonfire (1011314); (c) the one-way door near the aqueduct entry
    (1011320), which is opened from the lower side.
  - Undead Parish: across the Hellkite bridge, through the Sunlight Altar tower.
  - Havel's watchtower: Watchtower Basement Key door 1011315 (top) at (61.94,0.81,-120.42).
- **Prerequisites**: none to enter. Optional keys used here: Residence Key 2021 (sold by the Undead Merchant), Watchtower
  Basement Key 2019 (from the Firelink start gift), Basement Key 2001 (found in the Parish), and Master Key 2100 (the
  alternative accepted by the keyed doors, per msg 10010883 "Used Master Key").

### 1.2 Progression walkthrough (route order)
1. **Aqueduct top / Burg entry** (x≈-40…-65, y≈-22). Pass the first-time white-light fog 1011700 (event 11010090 slot 0).
   The fog is deleted for good once crossed. On first entry the **Hellkite fly-over** plays when the player enters region
   1012301 (-39.3,-18.35,-40.1); see Boss 1.5.3. Expect hollows on the stairs, Hollow Warriors (sword/axe/firebomb), and
   ladder 1011140 (o1215, -44.95,-24.58,-40.11).
2. A corridor of houses climbs toward the **Undead Merchant (Male)** on his balcony at (-38.02,-19.80,-66.50). A Hollow
   Warrior hides in crates there: object 1011103 (o1132), event 11015110. He bursts out with anim 3000 when the player is
   within 5 m or the crate breaks.
3. **Undead Burg bonfire** at (3.20,-10.02,-61.20). It is object 1011962 with bonfire flag 11010976.
   - Ladder 1011149 (o1208, -3.21,-10.02,-63.03) is the bridge→bonfire shortcut. It starts raised. Event 11010100 lets
     the player kick/lower it from above (player anim 8005, flag 11010100); after that it is registered as a normal ladder.
4. **Burg mid-level** (y -16…+1, z -84…-113). Points of interest:
   - A Hollow Soldier kicks open door 1011318 (o1318_01) from the next room: entity 1010130, event 11010111, kick anim 7000.
     It triggers when the player enters region 1012170 or hits him.
   - Black Knight (sword) 1010350 in the basement at (7.7,-15.7,-100). The Blue Tearstone Ring corpse is nearby.
   - The one-way "mid→lower" door 1011314 leads to Lower Burg (1.6.2).
   - Residence-Key house door 1011310 (20.22,-5.72,-104.20). It has two chests: Black Firebomb ×5 and Gold Pine Resin ×3.
   - Spiral-stair tower with a crossbowman: 1010200, event 11015113. He wakes on region 1012050, on damage, or within 5 m.
   - **Burning-barrel trap**: Hollow Soldier 1010103 at (51.26,0.86,-113.0), event 11010120. When the player enters region
     1012101 or hits him, he plays anim 3006 and pushes barrel 1011102 (o1330). The barrel rolls with fire SFX 100100 and
     a damaging hit (behavior 5020, 3 s). Flag 11010120 keeps the barrel spent.
   - Watchtower top door 1011315 (Watchtower Basement Key): see 1.8.
5. **Ladder 1011141** (o1209, 49.57,15.87,-117.0) climbs onto the wall-walk at the Taurus tower's east end. Two crossbow
   soldiers stand on the tower top (c2550_0008/0009). A **Crystal Lizard** pops out of barrel 1011000 (o1154_31): see 1.4.
6. **Taurus Demon** fog 1011890 (east, 51.41,16.86,-118.69). The fight is on the narrow wall-walk, and the exit fog 1011892
   is at the west tower (-3.94,17.26,-111.91). See 1.5.1.
7. West tower: **Knight Solaire** stands outside on the ledge gazing at the sun (-34.51,4.09,-124.17). Below him, door
   1011311 (Basement Key; one-way/locked from this side until opened) leads down to Lower Burg. The doors around the
   tower foot: 1011312 (o1312_0000, -13.32,-13.47,-69.61) and 1011313 (o1313_0000, 23.69,-14.99,-101.68) are plain
   doors (ObjAct 1312/1313, no key).
8. **Hellkite bridge** (y≈9.5) runs from about (-12,9.5,-85) at the Taurus/west side to (20,9.5,-30) at the Sunlight Altar
   tower. The Hellkite Drake perches on the altar tower and breathes fire along the bridge; see 1.5.3.
   - Claymore corpse at (11.80,9.41,-47.0). Soldiers on the bridge: c2550_0011/0012/0013/0014/0015/0043.
   - The **under-bridge ladder** (step 3) connects back to the bonfire.
9. **Sunlight Altar tower** (≈ 20…40, 9.5, -20…-35):
   - Sunlight Altar bonfire 1011961 (24.35,9.5,-22.2), bonfire flag 11010984.
   - The Warrior of Sunlight covenant statue (dummy chr c1000_0008, entity 6480, talk 101648).
   - Gate lever 1011120 (o1200_1001, 18.03,9.4,-25.49). It opens gate 1011121 (o1201_1001) to the Parish side; this is
     a shortcut, see 1.7.
   - Ladder 1011147 (o1219, 13.35,4.73,-11.46) goes down to a small room with 3 small rats.
   - Tower top (y≈32.5): Black Knight (UGS) 1010351 and two Soul of a Lost Undead corpses. The Hellkite's tail-drop spot
     c3431 is also here (23.5,43.6,-23.4).
   - From here the route continues into the **Undead Parish** (section 3).

### 1.3 Bonfires (m10_01)
| Bonfire | Object part / entity | Bonfire flag | Bonfire dummy chr | Notes |
|---|---|---|---|---|
| Undead Burg | o0200_0002 / 1011962 @ (3.20,-10.02,-61.20) | 11010976 | c1000_0002 ent 1010962 talk 100102 | Under the Hellkite bridge; under-bridge ladder 1011149 next to it |
| Sunlight Altar | o0200_0001 / 1011961 @ (24.35,9.50,-22.20) | 11010984 | c1000_0001 ent 1010961 talk 100101 | Altar statue 6480 nearby |
| Undead Parish | o0200_0005 / 1011964 @ (87.90,15.00,107.50) | 11010960 | c1000_0005 ent 1010964 talk 100104 | Near Andre; see section 3 |

All three are registered in event 11010008 (`Register Bonfire`, reaction 2 m / 180°, standard kindling 0). None of them
has a Firekeeper. The Fire Keeper Soul in the Parish is an item (section 3.7).

### 1.4 Enemies, Undead Burg
NpcParam names come from DSR NpcParam. "Think" is the AI id; parts with the same NpcParam but a different think id behave
differently (patrol/idle variants).

**A. Burg entry (aqueduct top, y -18…-28)**
| Enemy | Model | Count | Parts (entity) | Behaviour / notes |
|---|---|---|---|---|
| Hollow (Broken Straight Sword) | c2500 | 5 | c2500_0000 (think 250001), _0020, _0022 (250002), _0023 (250001), _0046 (250000) | Stair/house hollows; 20 souls |
| Hollow Warrior (firebomb) | c2540 | 1 | c2540_0000 (npc 254012) | Firebomb thrower |
| Hollow Warrior (sword) | c2540 | 2 | c2540_0012, _0034 (254010, think 254006) | |
| Hollow Warrior (axe) | c2540 | 1 | c2540_0015 (254011, think 254007) | |
| Vagrant (passive) | c3490 | 1 | c3490_0000 (1013900) | Online-only |
| Vagrant (aggressive L1/L2) | c3491 | 2 | c3491_0000 (1013901), c3491_0005 (1013902) | Same spot (-52.5,-18.8,-19.3); online-only |

**C. Burg mid / merchant / bonfire / residence (y -20…+5)**
| Enemy | Model | Count | Parts (entity) | Behaviour / notes |
|---|---|---|---|---|
| Hollow Warrior (sword, 254010) | c2540 | 6 | c2540_0001, _0002 (think 254030), _0003 (254020), _0004 (254006), _0010, _0025 (254006) | |
| Hollow Warrior (axe, 254011) | c2540 | 4 | c2540_0005, _0006, _0007 (**1010102**, crate ambush, event 11015110), _0008 | |
| Hollow Warrior (firebomb, 254012) | c2540 | 4 | c2540_0011, _0013, _0023, _0024 | _0023/_0024 sit high at y≈-1.8, throwing down |
| Hollow Warrior BP (254013/254014) | c2540 | 3 | c2540_0014 (1010906, axe), _0016 (1010907, sword), _0017 (1010908, axe) | **BP set** |
| Hollow Soldier (spear, 255000) | c2550 | 3 | c2550_0001, _0003 (think 255011, near merchant), _0041 | Spear + shield |
| Hollow Soldier (sword, 255001) | c2550 | 4 | c2550_0002, _0006 (think 255005), **_0010 (1010130, door-kicker, event 11010111)**, **_0020 (1010103, barrel roller, event 11010120)** | |
| Hollow Soldier (crossbow, 255002) | c2550 | 2 | c2550_0040 (think 255007), **_0042 (1010200, spiral-tower sniper, event 11015113)** | Light Crossbow |
| Hollow Soldier BP | c2550 | 2 | c2550_0024 (1010909, npc 255044 sword), _0025 (1010910, npc 255043 spear) | **BP set** |
| Hollow Soldier (sword, 255041) | c2550 | 1 | c2550_0044 (13.85,4.56,-42.17) | Under the bridge near the altar tower |
| **Black Knight** (sword) | c2792 | 1 | c2792_0000 (**1010350**) @ (7.70,-15.73,-100.0) | 355 HP, 800 souls. **Non-respawning** (event 11010860 slot 1). Drops lot 27900000: Black Knight Sword 20% / Black Knight Shield 5% |
| Vagrant | c3490 | 1 | c3490_0001 (1013910) | Online-only |

**D. Taurus wall-walk / tower top (y 15…27)**
| Enemy | Model | Count | Parts (entity) | Notes |
|---|---|---|---|---|
| Taurus Demon | c2250 | 1 | c2250_0000 (1010700) | Boss, see 1.5.1 |
| Hollow Soldier (crossbow) | c2550 | 2 | c2550_0008, _0009 (npc 255002, think 255002) @ tower top (52…53, 24, -120) | Shoot down onto the wall-walk. Region 1012700 is "nest the Taurus's minions flee to" *(interp.)* |
| Crystal Lizard | c3300 | 1 | c3300_0000 (**1010400**) @ (59.19,17.21,-123.30) | Disabled until barrel **1011000** breaks (event 11015130). Then it is enabled, plays anim 500 ×4 (emerging), runs home to region 1012060. Kill event 11010710 awards lot **33006000 = Twinkling Titanite** and sets flag 11010710 (never returns) |
| Vagrant (aggr.) | c3491 | 2 | c3491_0001 (1013911), _0006 (1013912) | Online-only |

**E. Hellkite bridge / Sunlight Altar tower (y 4…43)**
| Enemy | Model | Count | Parts (entity) | Notes |
|---|---|---|---|---|
| Hollow Soldier (spear, 255040) | c2550 | 4 | c2550_0011, _0012 (bridge west), _0013, _0043 (bridge east / under-tower) | |
| Hollow Soldier (crossbow, 255042) | c2550 | 2 | c2550_0014, _0015 (bridge east end) | |
| Hollow Soldier (sword, 255041) | c2550 | 1 | c2550_0033 (4.91,16.00,-6.97) | Stair to the Parish |
| Hollow (sword) | c2500 | 3 | c2500_0034, _0038, _0039 (think 250006) | Altar-tower rooms |
| Small Rat | c1201 | 3 | c1201_0000, _0001, _0002 (npc 120101) | Room under the altar (ladder 1011147); drop lot 12010100 (Humanity, rare) |
| **Black Knight** (UGS) | c2793 | 1 | c2793_0000 (**1010351**) @ (13.58,32.47,-8.65) | Altar tower roof. **Non-respawning** (11010860 slot 2). Drops lot 27900100: Black Knight Greatsword 20% / Shield 5% |
| Hellkite Drake (main) | c3430 | 1 | c3430_0000 (**1010300**) MSB pos (67.75,15.05,-343.31) = region "A′" 1012302 | Boss-like; see 1.5.3 |
| Hellkite Drake (fly-over double) | c3430 | 1 | c3430_0001 (**1010302**) @ (-128.6,-46.35,39.2) | Cosmetic fly-over only (event 11010790) |
| Hellkite Drake's Tail | c3431 | 1 | c3431_0000 (**1010301**) @ (23.51,43.60,-23.36) | Severed-tail object, enabled on tail cut |
| Vagrant | c3491 | 2 | c3491_0003 (1013931), _0008 (1013932) | Online-only |

### 1.5 Bosses and boss-like encounters (Burg)

#### 1.5.1 Taurus Demon
- **Part**: c2250_0000, entity **1010700**, NpcParam 225000 (1215 HP), think 225000. Boss name text id 2250.
- **Arena**: the narrow wall-walk between the east tower (~52,17,-119) and the west tower (~-4,17,-112). The MSB spawn is
  on top of the west tower at (-6.70,26.98,-110.40).
- **Fog walls**: entry 1011890 (o1405, east) with line-segment action region 1012898, rotate target 1012897; second fog
  1011892 (o1406, west). Fog SFX 1011891/1011893. Fog interaction events: 11015380 (host, anim 7410 walk-through),
  11015381 (white phantoms after host entry 11015383), 11015843/11015846 (DSR co-op fog re-enable). Area-entry event
  11015383 fires on region 1012896: it issues the boss-room notification, forced network authority, and multiplayer
  buffs.
- **Intro (event 11015382)**: AI off, waiting anim **9001** (perched on the tower), HP bar hidden. It triggers when the
  player damages it OR the host enters region 1012701 (22.8,15.16,-120.7). Then it sets flag 11010005, plays anim
  **9061** (leap down onto the wall-walk) and turns AI on.
- **Health bar / BGM**: event 11015384 shows the boss bar (slot 0, name 2250) and BGM 1013802 when the player is in
  region 1012890. Event 11015385 stops both on death.
- **Mechanic (event 11015386)**: when the AI posts event message 700 it warps to 1012741 ("jump-up start"); on
  message 710 it warps to 1012740 ("jump-down start"). This lets the Taurus leap onto or off the east tower. Helper
  logic regions: 1012742/1012743 (player below/above), 1012744/1012745 (turn targets). Plunging attack from the tower
  top is the classic cheese *(unverified)*.
- **Death (event 11010901)**: when HP ≤ 0: wait 1 s, play SE, wait for the death. Then flag **11010901** is set,
  `Handle Boss Defeat(1010700)`, and both fogs are disabled. If already dead on load, the boss is disabled and killed
  silently.
- **Rewards**: common.emevd event 970 slot 1 on 11010901 awards lots 9000 (Humanity) and 9030 (Homeward Bone).
  NpcParam itemLot 22500000 gives Demon's Greataxe at 5%. Boss souls: 3000 *(unverified, not in NpcParam)*.
- **World change**: the fogs are gone, and the route to Solaire / Hellkite bridge opens. Solaire's and Lautrec's Taurus
  summons do not exist in DS1, so there are none here.

#### 1.5.2 Crystal Lizard from the barrel
See enemy table D. Barrel 1011000 (o1154_31, 59.19,17.09,-123.30) must break first.

#### 1.5.3 Hellkite Drake (bridge dragon; not a fog boss)
- **Parts**: main **1010300** (c3430_0000, NpcParam 343000, 1840 HP, **10000 souls**), fly-over double **1010302**
  (c3430_0001), tail **1010301** (c3431_0000, NpcParam 343100).
- **Init (event 11010899)**: 1010300 is immortal and disabled, with home region 1012320. Then:
  - If the landing intro is done (11010791 ON) and it is not on the ground (11015311 OFF): enable it, warp it to perch
    **B** 1012310 (20.6,18.87,-29.0, the Sunlight Altar tower top, floor hit 1013211), waiting anim **7006** (perched),
    map hits off, gravity off.
  - If it was on the ground: warp it to **C** 1012320 (9.14,9.0,-48.0, on the bridge).
  - Either way it starts 13 attack-pattern events (11010805 slots 0–12) and two landing events (11010800 slots 0–1).
- **Fly-over (event 11010790)**: on first entry to region 1012301 (Burg entry), enable 1010302, warp it to **A** 1012300
  (-81.69,-46.35,46.68) and play anim **7012** (fly-over). Then disable it again. Collision 1013200 is disabled during the
  pass. Flag 11010790.
- **First landing (event 11010791)**: needs 11010790 done, HP ≥ 10%, player in region 1012305 (bridge middle) and nobody
  in region 1012337 (altar tower). The player must not be a black phantom. Then it sets flags 11010791/11010780, warps
  the drake to **A′** 1012302, plays anim **7005** (fly in and land on the tower), waits 395 frames, warps to B.
- **Tower-roof object 1011290** (o1290, 22.51,9.4,-25.38), event 11010780: when the drake posts message 1000 the object
  plays anim 1 then loops anim 2. This is the perch/roof reacting to the landing *(interp.)*.
- **Attack selection (event 11015302 + 11010890 + 11010805/11010800)**: while perched (11010791 ON, 11015311 OFF, HP ≥
  10%) the event checks the player's position against regions A 1012330 (only after ladder flag 11010100), B 1012331,
  C 1012332 (needs 11010100), D 1012333, plus timers 11015305/11015317. It then randomly sets one flag in 11015320–339.
  The mapped event then plays a breath anim:
  - 7004 (190 f), 7006 (90 f), 7008 (200 f), 7009 (180 f) or 7011 (192 f; 7011 also sets 11015312), with SpEffect 4160
    applied during the anim.
  - Or event 11010800 plays anim **7010** = **land on the bridge**: 11015311 ON, gravity and map hits on, default backread.
- **Fire breath objects (event 11010200 ×10)**: on AI messages 10…100, `Hellkite Breath Control(1010300, object
  1011060, anim)` runs with anims 3000/3001/3002/3009/3010/7004/7005/7008/7009/7011. Object 1011060 (o1060,
  -24.9,9.49,-107.33) is the bridge-fire controller.
- **Return to perch (event 11015307)**: while landed it fights normally. On message 700 it warps to home 1012340, plays
  anim **7016** (take off), then warps to B and resets.
- **Leaves when you reach the tower (event 11010782)**: if a non-black-phantom player enters 1012337 while it is perched,
  it plays anim **7013** (fly away), is disabled and warped to B, and 11010791 is cleared. The fly-in (7005) then replays
  next time.
- **Retreat at <70% HP (events 11010851/11010852/11010783)**: after HP < 70%, with players outside 1012338 for 60 s
  (flag 11015315), it plays anim **7017** and waits with anim 7018 until HP ≥ 70%. Then it plays **7019** to return.
- **"Death" (event 11010900)**: at HP < 10%:
  - If it is airborne or perched, it plays anim **7001** or **7007** and is disabled (flies off permanently; event flag
    11010900 set).
  - If it is landed, immortality is removed and `Force Character Death(souls=TRUE)` fires, so the player gets 10000
    souls.
  - Once 11010900 is set, all three parts are disabled on load.
- **Tail cut (event 11015301)**: NPC part 3430 has 200 HP. When the tail part HP reaches 0 (HP ≥ 10%, not during
  message window 500–600, damaged by the player), it plays anim **8000** (perched) or **8010** (landed). On message 400
  it hides the tail mesh on the drake (DispMask bit 0 OFF, HitMask bit 1 ON), spawns 1010301 at dummy 66 with anim
  **8100**, and awards lot **34310000 = Drake Sword** (flag 51010980).
- **Safe-zone helpers**: logic regions 1012710–1012763 ("no safe zone 1–4", "pursue on bridge/tower side", backstep and
  fly-over control) and path test points 1012720–1012736 along the bridge.

### 1.6 Lower Undead Burg (same MSB): see section 2.

### 1.7 Interactive objects and scripted events (Burg)
| What | Object / entity | Event / flag | Behaviour |
|---|---|---|---|
| White-light fog #1 (first entry from aqueduct) | o1419 **1011700**, SFX 1011701 | 11010090 slot 0, regions 1012600/1012601 | Action-button "Traverse/Enter the white light" (msg 10010403/10010407). Short-warps through with anim 7410, then the fog is deleted permanently |
| Hellkite bridge gate | lever o1200_1001 **1011120** (ObjAct 11010620), gate o1201_1001 **1011121** | 11010621 | Lever opens the gate (anim 4; closed anim 3); ObjAct disabled afterwards. Shortcut between the Parish side and the bridge/altar tower |
| Under-bridge ladder | o1208 **1011149** | 11010100 | Starts up; action (msg 10010500) lowers it (player anim 8005, obj anim 0); then Register Ladder 11010028/29 |
| Ladders | 1011140 (o1215), 1011141 (o1209), 1011147 (o1219), 1011148 (o1207, -10.87,-19.53,-109.2), 1011151 (o1206, -71.99,-28.8,-24.64), 1011152 (o1218, 6.49,-4.52,-99.96) | Register Ladder 11010010–35 | Normal ladders. Parish ladders 1011142–1011146 and 1011150 are listed in section 3 |
| Residence-Key house door | o1310_0000 **1011310** (ObjAct 1310, key 2021) | 11010140, ObjAct flag 61010510 | Locked ("It's locked"); "Used Residence Key" (10010881) or Master Key (10010883) |
| Residence-house chests | o0110_0000 **1011670** (Black Firebomb ×5), o0110_0001 **1011671** (Gold Pine Resin ×3) | 11010650 slots 0/1 (ObjAct 11010650/11010651) | Treasure enabled 10 frames after the chest opens |
| Watchtower top door | o1305_01 **1011315** (ObjAct 1305, key 2019) | 11010190 slot 1, flag 61010504 | "Used Watchtower Basement Key" (10010878) or Master Key |
| Basement-Key door (Taurus tower foot / bridge level) | o1312_01 **1011311** (ObjAct 11312, key 2001) | 11010170 slot 2, flag 11010172 / 61010511 | Locked; opening shows "Used Basement Key" (10010860). Shortcut to Lower Burg |
| "Mid→lower" one-way door | o1314_0000 **1011314** (ObjAct 1314: spQualifiedType 2 = needs SpEffect 4150) | 11010150 / 11010160 (flag 11010160) | Event 11010150 gives the player SpEffect 4150 only inside region 1012401 (-5.5,-10.1,-104.5) until the door is opened. After that, either region 1012400/1012401 grants it. So the door opens from one side only; "Does not open from this side" (10010161) otherwise |
| Door kicked open by enemy | o1318_01 **1011318** | 11010111, flag 61010518 | Hollow Soldier 1010130 kicks it (anim 7000, door anim 2). ObjAct re-enabled after 4 s |
| Burning-barrel trap | o1330_0000 **1011102** | 11010120 (flag 11010120) | See walkthrough step 4; damaging object behavior 5020 r0.6 for 3 s |
| Crystal-lizard barrel | o1154_31 **1011000** | 11015130 / 11010710 | See enemy table D |
| Merchant crate (ambush) | o1132_16 **1011103** | 11015110 | Hollow Warrior 1010102 bursts out (anim 3000) |
| Fire hazard | o1110 **1011450** (-12.80,-28.43,-88.16) | 11010008 (flag 11010300) | Persistent damaging object, behavior 5000, r1.2, every 1 s (likely fire) *(interp.)* |
| Hellkite breath object | o1060 **1011060** | 11010200 | See 1.5.3 |
| Hellkite perch roof | o1290 **1011290** | 11010780 | See 1.5.3 |
| Taurus fogs | o1405 **1011890**, o1406 **1011892** | 11010901, 11015380/81/83/843/846 | See 1.5.1 |
| Multiplayer-only area fogs | o1407 1011994, o1408 1011996, o1409 1011998, o1410 1011988, o1411 1011986, o1412 1011984, o1414 1011976, o1415 1011982, o1417 1011980, o1418 1011978, o1421 1011974/1011972 (SFX = id+1) | event 0 | Disabled and SFX deleted for host/offline (`SKIP IF client`). They stay only for summoned clients (co-op area limits) |
| Message / map name | none found in EMEVD | — | — |

### 1.8 Havel's watchtower (Burg, Watchtower Basement Key)
- Top door 1011315 (61.94,0.81,-120.42) is in the barrel-trap section of the Burg. Bottom door 1011316 (o1305_02,
  63.54,-38.25,-113.59; ObjAct 11305, key 2019) is in event 11010190 slot 2 with flag 61010505. The bottom door is the
  Lower Burg side, so the watchtower is a vertical shortcut between the Burg and Lower Burg *(interp.)*.
- **Havel the Rock**: c0000_0010, entity **6580**, NpcParam 6580 (3000 souls), think 6580, talk 0 (no dialogue). He is
  in the tower base at (59.41,-38.24,-111.39). His MSB InitAnimID is -1 (default stand; no special pose). Logic region 1012799 ("Havel combat area", cylinder r 7.5, h 62) keeps
  him inside the tower. Humanity event value base 8972. **Non-respawning** (11010860 slot 0). His death drop is lot
  **6580 = Havel's Ring** (flag 50006580). Dragon Tooth also drops *(unverified)*: the NpcParam has only lot 6580.

### 1.9 NPCs in the Undead Burg
| NPC | Part / entity / NpcParam / talk | Where | Idle / anims | Quest steps here | Death / hostility |
|---|---|---|---|---|---|
| **Undead Merchant (Male)** | c2510_0000 / **6230** / 251001 / talk 101623 | Balcony (-38.02,-19.80,-66.50), region 1012014 | Waiting anim **9000** (set on absolution reset, 11010583) | Flags 1400–1409. Gifts/lots: 6230 Orange Guidance Soapstone, 6231 Residence Key (flag 11017030), 6232 Humanity | Hostile → flag 1401, team "Hostile Ally" (11010510 slot 4). Death → flag 1402, force treasure (11010520 slot 4); lot 25100100 Uchigatana |
| Shop: Undead Merchant (M) | ShopLineup 1100–1133 | | | Sells: Orange Guidance Soapstone, Repair Powder, Throwing Knife, Firebomb, Lloyd's Talisman, Residence Key, Repairbox, Dagger, Shortsword, Scimitar, Rapier, Hand Axe, Club, Reinforced Club, Spear, Short Bow, East-West/Small Leather/Buckler/Leather/Heater/Warrior's Round shields, arrows and bolts, Chain set, Leather Gauntlets, Bottomless Box | |
| **Knight Solaire** | c0000_0002 / **6001** / 6000 / talk 101600 | West-tower ledge after Taurus (-34.51,4.09,-124.17), region 1012010 | Waiting anim **7845** (sun-gazing pose) | Enabled only when flag 1000/1001/1004/1007 (states). 11010530: 1000 → 1001 once talked (11010591). 11010531: state 1008 + Anor Londo flag 11510594 → state 1007 and **warps to the Sunlight Altar** 1012530 (35.92,11.53,-34.36, floor 1013050). If 11510594 is set on load he is already placed at the altar. Gift lot **1000 = White Sign Soapstone** (flag 50000000) | Hostile 1004 (11010510 slot 0); dead 1005 (11010520). Drops lots 6000–6008 (Iron Helm, Armor of the Sun, Iron Bracelet, Iron Leggings, Sunlight Straight Sword, Sunlight Shield, Sunlight Talisman, White Sign Soapstone, Humanity) |
| Warrior of Sunlight altar | dummy c1000_0008 / **6480** / talk 101648 | Altar (38.55,12.24,-33.34) | — | Event 11010580: when ESD flag 11015030 is set and the player is human/hollow, the player gets SpEffect 4170, rotates to 6480, and plays **7895** then loops **7896** (kneel/pray). On 11015031 it plays **7897** (stand). Covenant join statue | — |

**Absolution reset (event 11010583)**: on each toggle of flag 744 (absolution) all Burg/Parish NPCs are warped back to
their "免罪時初期配置" regions with their idle anims: Solaire 1012010 (7845), Griggs 1012011, Rhea 1012012 (7880), Andre
1012013 (9000), Merchant 1012014 (9000), Lautrec 1012015 (7825). Andre's prop 1011010 is restored.

**Generic NPC hostility (event 11010510)**: an NPC turns hostile at HP ≤ 90% after player damage, if flag 703 is set,
or from a saved hostile state. The result is team type 9 (Hostile Ally) and a save request.

### 1.10 Items, Undead Burg (route order)
| # | Location (pos) | Lot → contents | Pickup flag | Notes |
|---|---|---|---|---|
| 1 | Burg entry (-43.90,-24.57,-41.00) | 1010110 [missing] | — | Empty |
| 2 | Burg entry (-43.71,-21.65,-37.57) | 1010440 Soul of a Lost Undead | 51010440 | |
| 3 | Burg entry house (-48.50,-14.02,-33.50) | 1010120 Throwing Knife ×10 | 51010120 | |
| 4 | Burg entry (-48.80,-22.79,-8.45) | 1010100 **Humanity** | 51010100 | |
| 5 | Burg entry upper (-56.10,-15.35,-15.95) | 1010090 **Light Crossbow** (+1010091 Standard Bolts) | 51010090 | |
| 6 | (-65.73,-26.10,-23.35) | 1010400 Soul of a Lost Undead | 51010400 | |
| 7 | (-57.19,-25.75,-34.17) | 1010070 Large Soul of a Lost Undead | 51010070 | |
| 8 | (-69.00,-31.83,-33.40) | 1010080 Rubbish | 51010080 | Lower ledge |
| 9 | Near merchant (-10.90,-13.30,-73.55) | 1010130 **Wooden Shield** | 51010130 | |
| 10 | Basement (-19.30,-22.82,-96.15) | 1010360 [missing] | — | |
| 11 | Basement, Black Knight (-4.60,-16.38,-92.00) | 1010160 **Blue Tearstone Ring** | 51010160 | |
| 12 | Mid (5.82,-1.84,-84.04) | 1010470 Soul of a Lost Undead | 51010470 | |
| 13 | Residence house chest (13.99,-8.80,-104.16) | 1010450 Black Firebomb ×5 | 51010450 | Chest, Residence Key |
| 14 | Residence house chest (31.34,-7.45,-111.07) | 1010460 Gold Pine Resin ×3 | 51010460 | Chest |
| 15 | (27.80,-12.88,-102.20) | 1010010 [missing] | — | |
| 16 | (41.00,-8.74,-100.50) | 1010150 [missing] | — | |
| 17 | (-17.60,4.71,-94.85) | 1010240 [missing] | — | |
| 18 | West tower top area (-29.06,17.06,-97.54) | 1010480 Large Soul of a Lost Undead | 51010480 | |
| 19 | Bridge (-8.00,9.45,-82.20) | 1010190 [missing] | — | |
| 20 | Bridge (-3.85,9.43,-67.45) | 1010200 [missing] | — | |
| 21 | Bridge (11.80,9.41,-47.00) | 1010210 **Claymore** (weapon 301000) | 51010210 | Community lot label "Black Leather Armor" is wrong |
| 22 | Bridge end (12.50,9.45,-40.00) | 1010220 Soul of a Nameless Soldier | 51010220 | |
| 23 | Altar room (40.85,9.90,-22.75) | 1010230 [missing] | — | |
| 24 | Altar tower roof (15.42,32.47,-14.42) | 1010410 Soul of a Lost Undead | 51010410 | Black Knight (UGS) here |
| 25 | Altar tower roof (10.80,32.68,-8.80) | 1010420 Soul of a Lost Undead | 51010420 | |
| — | Crystal Lizard kill | 33006000 Twinkling Titanite | 11010710 | One-time |
| — | Hellkite tail | 34310000 **Drake Sword** | 51010980 | |
| — | Merchant gift | 6231 **Residence Key** (also sold) | 11017030 | |
| — | Solaire gift | 1000 **White Sign Soapstone** | 50000000 | |

### 1.11 Porting notes (Burg)
- **Hellkite Drake** is the biggest single porting job. It is a scripted, perch-based dragon (perch at B, land at C,
  fly-in from A′, fly-over double at A). It needs: the `Hellkite Breath Control` instruction (2004:36) driving object
  1011060 along the bridge; region-driven random attack selection; tail-cut NPC part 3430; a 70% HP retreat timer; and
  "fly away at <10% unless landed". DS2 has no equivalent instruction. The breath sweep must be rebuilt as scripted
  bullets/hit areas along the bridge.
- **Taurus tower leaps** depend on AI messages 700/710 that warp him to jump points. In DS2 these need EzState/AI
  equivalents or scripted warps.
- **Black-tendency phantoms** (entities 1010900–1010912 across the Burg and Parish): enabled by flag 11010050, which is
  set when world tendency is black >50 (event 11015072, 600 s check loop). Disabled and killed on reset (11015071). DS2
  has no world tendency, so decide: drop them, or tie them to an equivalent toggle.
- **Online-only Vagrants** (c3490/c3491, entities 10139xx): can be skipped for an offline port.
- **Multiplayer-only fogs** (1011972–1011999 pairs) exist only for clients. With Seamless Co-op they act as area
  limits; decide whether to keep them.
- **Residence Key** is used by two doors (Burg house + Griggs's room). Master Key should open the Burg house, Lautrec's
  cell and the watchtower doors (msg 10010883).
- **One-way doors** use ObjAct spQualifiedType 2 (SpEffect 4150 granted only on the correct side), not a simple
  facing check. Reproduce them with DS2 one-sided door logic.

### 1.12 Area completion checklist: Undead Burg
- [ ] Aqueduct entry + first-time white fog 1011700 (11010090 slot 0)
- [ ] Hellkite fly-over (1010302, anim 7012) at region 1012301
- [ ] All enemy parts in tables A, C, D, E (incl. the BP set flagged 11010050)
- [ ] Merchant balcony ambush crate 1011103; merchant 6230 + shop + Residence Key
- [ ] Undead Burg bonfire 1011962; under-bridge kick ladder 1011149
- [ ] Door-kick soldier 1010130 / door 1011318; one-way door 1011314 (SpEffect 4150)
- [ ] Residence-Key house 1011310 + 2 chests; burning barrel trap 1011102 + roller 1010103; spiral-tower sniper 1010200
- [ ] Black Knight (sword) 1010350 non-respawn + Blue Tearstone Ring
- [ ] Watchtower door 1011315 / 1011316 + Havel 6580 (non-respawn, Havel's Ring)
- [ ] Crystal Lizard in barrel 1011000 (one-time Twinkling Titanite)
- [ ] Taurus Demon full fight (9001 perch → 9061 leap, tower jumps, fogs 1011890/892, rewards, flag 11010901)
- [ ] Solaire at the west ledge (7845) + White Sign Soapstone + later warp to the Sunlight Altar
- [ ] Basement-Key door 1011311
- [ ] Hellkite Drake: perch / fly-in / land / breath object 1011060 / tail cut → Drake Sword / retreat / death rules
- [ ] Sunlight Altar bonfire 1011961, covenant statue 6480 (kneel anims 7895/7896/7897), gate lever 1011120/1011121
- [ ] Black Knight (UGS) 1010351 on the altar tower roof
- [ ] All pickups in 1.10

---------------------------------------------------------------------------------------------------------------------

## 2. Lower Undead Burg, m10_01_00_00

### 2.1 Header
- **Map**: same MSB, y ≈ -28…-55. It covers the dog/thief alleys (x -20…-90, z -80…0), Griggs's house (≈0,-31,-75),
  the Twin Humanities room (≈10,-35,-90), the Capra arena (≈-72…-85, -43.6, -1…-17), the Depths door, and the
  watchtower base.
- **Entrances**:
  - Basement-Key door 1011311 (from the Hellkite bridge level, via stairs down).
  - One-way door 1011314 (from the Burg bonfire/basement level).
  - Watchtower bottom door 1011316.
  - Burg-entry one-way door 1011320 (o1308, -50.40,-33.70,-8.49). It opens only from the lower side (event 11010101 with
    player anim 7110, dummies 101/121; the other side shows "Does not open from this side", event 11010102). So it is a
    shortcut Lower Burg ↔ aqueduct/Firelink *(interp.)*.
- **Exit**: **door to The Depths** o1306 **1011306** (-107.01,-54.24,-13.47), ObjAct 1306, needs **Key to Depths
  2014** (ObjAct flag 11010171, msg 10010873 "Used Key to Depths"). Behind it the player lands at the Depths entry
  (m10_00 player 1000999).
- **Prerequisites**: Key to Depths comes from **Capra Demon** (lot 2510 via common event 970 slot 2). Lot 4014 "[Firelink
  Relief Chest] Key to Depths" exists as a fallback *(unverified how it's awarded)*. Residence Key opens Griggs's door.

### 2.2 Progression walkthrough
1. Come down the stairs from door 1011311 or 1011314 into the narrow alleys. **Undead Assassins (thieves) burst out of
   doors**: event 11010130 ×6. Door objects 1011250–1011255 (o1316/o1317/o1318) are paired with thieves
   1010150–1010155. When the player enters region 1012150 (first trio) or 1012151 (second trio), after a random 0–1 s
   delay the thief plays **anim 7001** and the door plays anim 2. The doors stay open after (flag per slot).
2. **Undead Attack Dogs** roam the alley: c3340_0000/0001/0002 near Griggs, _0005/_0006 near the Capra fog.
3. **Griggs's house**: locked door o1317_04 **1011317** (ObjAct 1317, **Residence Key 2021**, flag 11010181 /
   61010517). Inside: Griggs (2.4) and the Sorcerer set in a barrel.
4. **Twin Humanities room** (≈ 9…15, -34.8, -83…-100): 7 huddled Hollows (c2500_0052–0058, npc 250001, think
   250012/250013) around the Twin Humanities corpse. Corpse 1010030 here points to a missing lot. Event 11010125/126
   ("trap thief" warps in on pickup 51010030) exists but is **never initialized**, so it is dead content.
5. **Hollows that drop in** (event 11015250 ×6): hollows 1010250/251 and 1010260–263 start with map collision off.
   They activate when the player comes within 5.0/5.5/4.0/3.0 m, then get collision back and cancel anim 9060. These
   are ambushers lying in wait *(interp.)*.
6. **Capra Demon** arena behind fog 1011790 (o1413, -72.75,-43.57,-16.50). See 2.3.
7. After Capra, take the Key to Depths. The door 1011306 is down the street west of the arena (lone archer hollow
   c2500_0051 at (-111.2,-40.3,-35.8) with a Short Bow, think 250020). Large Soul corpse 1010430 is by the door.

### 2.3 Boss: Capra Demon
- **Part**: c2240_0000, entity **1010750**, NpcParam 224000 (980 HP), think 224001. Boss name 2240.
- **Adds**: two Undead Attack Dogs **1010500/1010501** (c3340_0003/_0004, npc 334003, 150 souls) start in the arena
  with Capra.
- **Fog**: 1011790 (SFX 1011791), action region 1012888, rotate target 1012887.
  - Events 11015370 (host walk-through, anim 7410 *with wait*), 11015371 (white phantoms), and 11015373 (entry region
    1012886: boss-room notification, multiplayer buffs, sets flag 11010597).
- **Start (event 11015372)**: AI off until entry (11015373) and the player is inside 1012880. Then AI on and boss bar
  on. BGM 1013803 (events 11015374/11015375).
- **Death (event 11010902)**: flag **11010902**, Handle Boss Defeat, fog removed.
  - On later loads, Capra **and dogs 1010500/1010501** are disabled and killed.
  - **Dogs 1010510/1010511** (c3340_0007/_0008, same positions, npc 334000) are disabled *only while Capra is alive*.
    So after Capra's death two ordinary respawning dogs occupy the arena.
- **Rewards**: common 970 slot 2: lots **2510 Key to Depths**, 9000 Humanity, 9030 Homeward Bone. NpcParam lot 22400000:
  Demon Great Machete 5%. Souls 6000 *(unverified)*.
- **Mechanics**: tiny arena with stairs to a ledge; the dogs must be killed first *(general knowledge)*.

### 2.4 NPC: Griggs of Vinheim
- **Part**: c0000_0003, entity **6040**, NpcParam 6040, think 6040, talk **101604**. Position (-0.41,-31.35,-75.34),
  absolution region 1012011. Humanity value base 8342.
- **Presence**: disabled once flag 1112 or 1113 is set (he has moved to Firelink).
- **Rescue (event 11010532)**: when flag 1110 is set and door flag **11010181** (Residence-Key door opened) is ON, it
  sets 1111 and clears any special waiting anim. His MSB **InitAnimID is -1** (default stand), so no trapped pose comes
  from the MSB. If he has one, it is set by his ESD *(unverified)*.
- **Hostile/dead**: 1114 (11010510 slot 1) / 1115 (11010520 slot 1).
- **Drops**: lot 6040 = goods 3510 (Hush), 6041 Slumbering Dragoncrest Ring, 6042 Humanity.
- **Shop** (once at Firelink, *unverified* if he sells in the Burg): initial lineup 2100–2101 (Soul Arrow, Heavy Soul
  Arrow). Full list 2000–2021.

### 2.5 Enemies, Lower Undead Burg
| Enemy | Model | Count | Parts (entity) | Notes |
|---|---|---|---|---|
| Undead Assassin (door ambush) | c2520 | 6 | c2520_0004–_0009 (**1010150–1010155**) | Event 11010130; burst-out anim 7001 from doors 1011250–1011255. Bandit's Knife / Target Shield / Hollow Thief set drops (lot 25200000–003) |
| Undead Assassin (free) | c2520 | 2 | c2520_0010, _0011 (think 252010) @ (-88…-78, -54, -28) | Near the Depths stairs |
| Undead Assassin BP | c2520 | 2 | c2520_0012 (1010911), _0013 (1010912) | **BP set** |
| Hollow (dropping ambush) | c2500 | 6 | c2500_0001 (1010250), _0008 (1010251), _0021 (1010263), _0025 (1010260), _0026 (1010261), _0027 (1010262) | Event 11015250 (activation radii 5.0/5.5/5.0/4.0/3.0/3.0 m) |
| Hollow (huddled, Twin Humanities room) | c2500 | 7 | c2500_0052–_0058 (npc 250001, think 250012/250013) | No weapon drop (lot 25000100 "None") |
| Hollow archer | c2500 | 1 | c2500_0051 (npc 250052, think 250020) | Short Bow |
| Undead Attack Dog | c3340 | 5 | c3340_0000, _0001, _0002, _0005, _0006 | 80 souls |
| Undead Attack Dog (Capra adds) | c3340 | 2 | c3340_0003 (**1010500**), _0004 (**1010501**) (npc 334003) | Present only until Capra dies |
| Undead Attack Dog (post-Capra) | c3340 | 2 | c3340_0007 (**1010510**), _0008 (**1010511**) | Present only **after** Capra dies |
| Capra Demon | c2240 | 1 | c2240_0000 (1010750) | Boss |
| Hellkite fly-over double | c3430 | 1 | c3430_0001 (1010302) | Listed geographically here; see 1.5.3 |
| Griggs | c0000 | 1 | c0000_0003 (6040) | NPC |
| Havel | c0000 | 1 | c0000_0010 (6580) | NPC enemy, watchtower base (1.8) |
| Vagrants | c3490/c3491 | 3 | c3490_0002 (1013920), c3491_0002 (1013921), c3491_0007 (1013922) | Online-only |

### 2.6 Items, Lower Undead Burg
| Location | Lot → contents | Flag | Notes |
|---|---|---|---|
| Alley (-23.70,-31.82,-57.90) | 1010020 **Mail Breaker** | 51010020 | |
| Griggs's room, barrel o1154_47 **1011504** (0.77,-31.40,-76.51) | 1010510 **Sorcerer Hat** (+1010511–514 Sorcerer Cloak, Gauntlets, Boots, **Sorcerer's Catalyst**) | 51010510 | Treasure starts disabled; enabled when the barrel breaks (event 11010400 slot 4; corpse obj 1011654 loops anim 100 → 101) |
| Twin Humanities room (12.13,-34.83,-89.73) | 1010030 [missing] | — | Dead trap event 11010125/126 references its pickup flag |
| Twin Humanities room (13.30,-34.51,-99.80) | 1010490 **Twin Humanities** | 51010490 | o0504 corpse |
| Thief alley (-60.05,-43.48,-28.85) | 1010380 **Thief Mask** (+1010381–384 Black Leather Armor, Gloves, Boots, **Target Shield**) | 51010380 | |
| Barrel o1155_0002 **1011503** (-61.68,-43.57,-39.09) | 1010370 Large Soul of a Lost Undead | 51010370 | Event 11010400 slot 3 |
| Near the Depths door (-113.59,-54.08,-1.09) | 1010430 Large Soul of a Lost Undead | 51010430 | |
| Capra kill | 2510 **Key to Depths** (+9000 Humanity, 9030 Homeward Bone) | 50001510 | common 970 |

### 2.7 Interactive objects (Lower Burg)
| What | Object / entity | Event / flag | Behaviour |
|---|---|---|---|
| Door to the Depths | o1306 **1011306** | 11010170 slot 1; ObjAct flag 11010171 / 61010506 | Key to Depths 2014; msg 10010873 |
| Griggs's door | o1317_04 **1011317** | 11010180 slot 1, flag 11010181 | Residence Key 2021 |
| Thief doors | o1316_0000 1011250, o1318_0000 1011251, o1317_0000 1011252, o1317_0001 1011253, o1317_0003 1011254, o1317_0002 1011255 | 11010130 slots 0–5 | Open when the thief bursts out |
| Hidden-item barrels | 1011503, 1011504 (Parish one: 1011502) | 11010400 | Treasure enabled on destruction |
| Aqueduct one-way door | o1308 **1011320** | 11010101 / 11010102 | See 2.1 |
| Watchtower bottom door | o1305_02 **1011316** | 11010190 slot 2 | Watchtower Basement Key |
| Capra fog | o1413 **1011790** | 11010902 etc. | |

### 2.8 Porting notes (Lower Burg)
- **Door-burst ambush** needs paired door objects + character anims (7001) and area triggers. It's reusable as one
  template (6 instances).
- **Capra dog swap** (two dog sets keyed on the boss flag) must be preserved, or the post-boss arena will be wrong.
- **Key to Depths** gating door 1011306 is a cross-map gate. The Depths entry (m10_00) must be connected to it.

### 2.9 Area completion checklist: Lower Undead Burg
- [ ] Entrances 1011311 / 1011314 / 1011316 / 1011320 (one-way) wired
- [ ] 6 door-burst assassins (11010130) + 2 free assassins + BP pair
- [ ] 6 dropping hollows (11015250), 7 huddled hollows, archer hollow, 5 dogs
- [ ] Griggs (6040) behind Residence-Key door 1011317; rescue flag chain 1110 → 1111; Sorcerer-set barrel
- [ ] Twin Humanities, Thief set, Mail Breaker, Large Soul (barrel), Large Soul by the Depths door
- [ ] Capra Demon: fog 1011790, dogs 1010500/501, post-kill dogs 1010510/511, rewards (Key to Depths)
- [ ] Depths door 1011306 (Key to Depths) → m10_00 entry

---------------------------------------------------------------------------------------------------------------------

## 3. Undead Parish, m10_01_00_00

### 3.1 Header
- **Map**: same MSB. The Parish proper is x -12…40, y 13…76, z -15…145. The lower Parish (Andre, bonfire, Titanite
  Demon) is at x≈85–90, y -3…16.
- **Entrances**: from the Sunlight Altar tower (end of the Hellkite bridge); from Firelink via the elevator (lower
  Parish; the lift belongs to m10_02, ObjAct 1465 "lift room door") *(unverified exact link point)*; from Darkroot
  Garden (m12) via the path below Andre.
- **Exits**: Darkroot Garden (crest door, Crest of Artorias sold by Andre); Firelink elevator; Sen's Fortress
  (m15_00), whose gate opens after both Bells of Awakening (flag **11500200** set by the bell event when the second bell
  is rung).
- **Prerequisites**: none to enter. Mystery Key 2017 (found here) opens Lautrec's cell.

### 3.2 Progression walkthrough
1. From the altar tower stairs (soldier c2550_0033), enter the **Parish courtyard** (z 0…45, y 13…23).
   - The **Armored Tusk** 1010310 stands in front of the church gate (region 1012772).
   - Hollows and Hollow Soldiers line the stairs and walls.
2. **Church gate and lever**: lever o1200_1000 **1011100** (2.86,19.81,42.31) works gate o1201_1000 **1011101**
   (7.90,19.76,40.38). It's an ObjAct "Loop" (toggle), with state flag **61010610**.
   - Opening/closing toggles navmesh 1013100 and anims 2 (open) / 4 (close).
   - Closing deals crush damage (event 11010601, damage points dummy 42/43/44, behavior 5010).
   - **Lever-pulling soldier** (Hollow Soldier c2550_0016, entity **6010**): when the gate is open and the player
     enters region 1012200 (the courtyard), events 11015116/11010609 make the enemy use the lever. Event 11010607 then
     sends 6010 back home to 1012201 by the lever. So an enemy shuts the gate on you *(interp.)*.
   - **Armored Tusk gate logic** (event 11010608): while the gate is open, the Tusk's home moves through the gate
     (1012773) and AI command 1 is issued. When the gate closes it returns home to 1012772 in front of the gate.
   - The **Basement Key** lies right by the lever at (7.55,20.50,44.11).
3. **Fleeing hollow** (c2500_0006, entity **1010111**, event 11015112): AI off. When the player enters 1012121 it runs to
   its hideout 1012120 (-7.73,16.5,37.9). It fights if attacked or once the player enters 1012122. The **Mystery Key**
   corpse is in that side area (-9.46,15.32,26.70) *(interp.)*.
4. **Church nave** (y≈28, z 60…125):
   - Balder Knights on the ground floor, Heavy Knight 1010320 by the altar (at Rhea's spot).
   - Channeler 1010370 and 11 hollows on the upper gallery (y≈35).
   - **Fire Keeper Soul** on the altar (10.90,30.06,124.15).
   - Logic regions "four knights D" 1012775–1012777 (buff area, "wait on 2nd floor", "shoot from 2nd floor").
     Regions 1012106 ("church ambush") and 1012107 ("pincer heavy knight jump-down") are AI-side only (no EMEVD use
     found).
5. Ladders up the church interior: 1011144 (o1212, 12.67,37.05,69.07) then 1011145 (o1213, 15.90,43.27,69.12) to the
   roof door. Solaire's and Lautrec's summon signs for the Gargoyles sit at the top (see 3.5).
6. **Bell Gargoyles** on the church roof (fog 1011990 at the near end, fog 1011992 at the belfry end). See 3.3.
7. **Belfry**:
   - Lautrec's cell door o1308_02 **1011308** (Mystery Key) at (15.19,44.41,137.00).
   - Oswald of Carim at (7.00,50.30,135.40), appearing after the bell is rung.
   - Ladder 1011146 (o1214, 12.34,50.26,132.79) then ladder 1011150 (o1216, 10.75,66.46,142.37) up to the **Bell
     lever** o1202 **1011110** (10.75,75.41,134.49). The bell is o1303 **1011111**.
8. **Lower Parish** (via the stairs from the church side down to the Parish bonfire):
   - Parish bonfire 1011964 and **Andre** at (86.98,5.36,107.80).
   - Below Andre, the **Titanite (Prowling) Demon** 1010340 at (87.49,-2.60,61.51) guards the way toward Darkroot
     Garden and the Firelink lift *(unverified routing)*.

### 3.3 Boss: Bell Gargoyles
- **Parts**:
  - Gargoyle #1 **1010800** (c5350_0000, NpcParam 535000, 833 HP, think 535000) at (11.00,48.88,105.00).
  - Gargoyle #2 **1010801** (c5350_0001, NpcParam 535001, 833 HP) waits on the belfry roof at region 1012650
    (15.04,69.76,132.10).
  - Cutscene stand-in **1010802** (c5350_0002, NpcParam 535000) at (6.14,69.76,132.40).
  - Tail parts 1010810 / 1010811 (c5352, NpcParam 535200).
- **Fogs**: entry 1011990 (o1403, 17.36,47.83,74.37), action region 1012998, rotate target 1012997. Second fog 1011992
  (o1404, 7.09,49.15,131.00). Boss flag **3**.
- **Intro (event 11010000)**: 1010802 sits perched with AI off, invincible, no HP bar, waiting anim **7000**. 1010800 is
  disabled. When the host enters region 1012999 (cylinder r20 on the roof) after fog entry 11015390, cutscene
  **100110** plays (skippable offline, unskippable in multiplayer). Then 1010802 is disabled and 1010800 enabled. Black
  phantoms/invaders don't trigger it.
- **Start (event 11015392)**: 1010800 AI on and boss bar (slot 0, name 5350) once 11010000 + 11015393 are done and the
  player is in 1012990. BGM 1013800/1013801 swap (events 11015394/11015395).
- **Phase 2 (event 11015396)**: 1010801 waits invincible with anim 7000 and no HP bar. When **1010800 HP ≤ 60%** it is
  warped to 1012650, plays anim **7001** (fire-breath dive into the fight), becomes vulnerable, AI on, HP bar slot 1.
- **Tail cut (event 11015397, slot for 1010800)**: NPC part 5350 has 65 HP. On break it plays anim 8000 on the gargoyle,
  hides the tail (DispMask 0 OFF / HitMask 1 ON), spawns tail 1010810 at dummy 130 with anim 8100, SpEffect 5434, AI
  command 20. Award lot **53520000 = Gargoyle Tail Axe** (flag 51010970).
  - Gargoyle #2's tail (event 11015398 with 1010811) only sets hit masks: **no tail drop from #2**.
- **Death (event 11010001)**: when both are dead, flag **3** is set, death SE plays on whichever died last, Handle Boss
  Defeat(1010800), and both fogs are removed.
- **Rewards**:
  - common 970 slot 3 on flag 3: lot 9020 (**Twin Humanities**).
  - Per-gargoyle drops (event 11010750): 1010800 → lot 53500000, 1010801 → lot 53500100 (both: 97% nothing / 3%
    **Gargoyle's Halberd**).
  - Gargoyle Helm/Shield also drop from the Bell Gargoyles *(unverified)*; they are not visible in these lots.
  - Boss souls 10000 *(unverified)*.
- **Area-entry notification**: 11015393 (region 1012996) gives boss-room notify and multiplayer buffs.
- **Logic regions**: 1012790–1012793 (gargoyle step-allowed areas, narrow corridor attack area).
- **World change**: flag 3 drives several things.
  - It removes the Solaire/Lautrec Gargoyle summon logic.
  - It moves Lautrec out (11010552).
  - It enables the bell lever route. The belfry ladders are physically reachable afterwards *(interp.: fog 1011992
    blocks the belfry until the kill)*.
  - Boss-fight "Hollow" spawns don't change.

### 3.4 First Bell of Awakening (event 11010700)
- Lever ObjAct **11010700** (o1202 1011110, ObjAct 1202 msg 10010501). Once used, the ObjAct is disabled permanently.
- On use: Trigger Multiplayer Event 10010, which plays the bell SE 130300002 on object 1011111 three times at random
  0.5–2 s intervals (event 11015170, for all players). Then it waits until the player is alive.
- If the **second bell (Quelaag's, flag 11400200)** is not yet rung, it plays cutscene **100100**.
- If it is already rung, it plays cutscene **100105** and sets **11500200** (Sen's Fortress gate open).
- Then it awards **Achievement 29**.
- The event flag **11010700** is "first bell rung". It also enables Oswald (11010581), and with 11400200 it moves
  Domhnall out of the Depths (Depths 11000532).

### 3.5 NPCs in the Undead Parish
| NPC | Part / entity / NpcParam / talk | Where | Idle / anims | Quest steps here | Death / hostility |
|---|---|---|---|---|---|
| **Andre of Astora** | c2640_0000 / **6190** / 264000 (888 HP) / talk **101619** | (86.98,5.36,107.80), region 1012013 | Waiting **9000** (smithing loop) | Flags 1320–1339. Prop object o1252 **1011010** next to him (anvil *(interp.)*) is invulnerable until he turns hostile (1321) or dies (1322). Then it is destroyed with SE 125200000 (event 11015090) and restored on absolution | Hostile 1321 (11010510 slot 3), dead 1322 (11010520 slot 3); lots 6190 (goods 2002 = **Crest of Artorias**, flag 11017140), 6191 Humanity; NpcParam lot 26400000 Blacksmith Hammer |
| Shop: Andre | ShopLineup 1400–1420 | | | Titanite Shard, Crest of Artorias, Weapon/Armor Smithbox, Repairbox, Longsword, Broadsword, Bastard Sword, Battle Axe, Warpick, Caestus, Pike, Large Leather Shield, Tower Kite Shield, Caduceus Kite Shield, arrows/bolts. Also upgrades | |
| **Knight Lautrec of Carim** | c0000_0006 / **6300** / 6300 / talk **101630** | Cell in the belfry (17.59,44.40,136.27), region 1012015 | Waiting **7825** (sitting in the cell) | Flags 1570–1599. Visible if 1570 or 1574, or after release (11010584). The cell door ObjAct 1011308 stays **disabled until flag 11010593** (agreed to free him in dialogue, event 11010551). Opening it (Mystery Key, 11010190) → event 11010550 sets 1571 + 11010584 (released). He leaves (disabled, 11010552) once **Gargoyles are dead (3)** or flags 812+813 | Hostile 1574, dead 1575; lot 6300 = ring 143 (**Ring of Favor and Protection**), 6301 Humanity. Release gift lot 1150 = goods 375 *(attribution unverified)* |
| **Oswald of Carim** | c0000_0012 / **6370** / 6370 (2000 souls) / talk **101637** | Belfry stairs (7.00,50.30,135.40) | MSB InitAnimID **7865** (standing idle pose, hands clasped *(unverified look)*) | Enabled only after the **first bell** (11010700, event 11010581). Flags 1700–1709 | Hostile 1701, dead 1702; lots 6370 (goods 108 **Book of the Guilty**, flag 11607020), 6371 Twin Humanities |
| Shop: Oswald | 4400–4408 | | | Book of the Guilty, Purging Stone, Indictment, Karmic Justice, Velka's Talisman, Bloodbite Ring, Poisonbite Ring, Ring of Sacrifice, Homeward Bone. Absolution service (flag 744 → absolution reset 11010583) | |
| **Rhea of Thorolund** | c0000_0005 / **6072** / 6070 / talk **101607** | Church altar (10.79,29.32,121.67), region 1012012 | Waiting **7880** (praying) | Flags 1170–1189. Disabled unless 1175. **Arrives** after she leaves Firelink for the Tomb (1174) + flag 11310590 (event 11010533: sets 1175, enables her, clears event value 600). **Hollowed/left**: 1176. Leaves the Parish when 724 is set (event 11010534 sets 1181). Event 11010537 handles talk-count value 600 ≥ 2 → 11010599 and her item lots 50006070/71 (Ivory Talisman/Pendant). Event 11010539 sets 815 when 1177 + 50006071. **Heavy Knight 1010320 is removed** once Rhea's state 1175/1179/1181 is set (event 11010582) | Hostile 1179 (event 11010501 also enables her and sets AI id 1), dead 1180 (11010535 force treasure); lots 6070 Ivory Talisman, 6071 Pendant, 6072 Humanity |
| Shop: Rhea | 4200–4209 | | | Divine Blessing, Heal, Great Heal, Great Heal Excerpt, Homeward, Force, Wrath of the Gods, Seek Guidance, Magic Barrier, Talisman | |
| **Solaire summon (Gargoyles)** | c0000_0004 / **6540** / NpcParam 6540 / talk 0 | Sign at region 1012000 (17.98,47.83,71.32) by the fog | Walks into the fog (anim 7410) after summon (11015101) | Events 11015100 / 11015900 (two DSR variants: multiplayer-count condition 5/2 or 4/3 + SpEffect 28). Needs host, alive (human), Gargoyles alive, Solaire not hostile/dead/away (1004/1005/1006/1010/1011 all off). Summon flag 11015102, dismissal 11015106. When placed, the Burg Solaire 6001 is disabled | Phantom; no drops |
| **Lautrec summon (Gargoyles)** | c0000_0011 / **6590** / NpcParam 6590 (chrInit 6300) | Sign at 1012001 (6.20,43.24,68.65) | Same walk-in (11015104); SpEffect 5450 on summon (11015203) | Events 11015103 / 11015901. Needs **11020607** (Lautrec at Firelink after release) and state 1572 or 1573, not 1574, within 20 m. Summon flag 11015105, dismissal 11015107 | Phantom |
| Lever-pulling soldier | c2550_0016 / **6010** / 255041 | Courtyard/lever (4.74,17.75,24.85), home 1012201 | — | Event 11010607/11010609 (see 3.2). **Non-respawning** (11010860 slot 6) | Normal enemy |

Absolution reset (11010583) and generic hostility (11010510) are as in 1.9.

### 3.6 Enemies, Undead Parish
**F. Parish courtyard / outside (y 13…33)**
| Enemy | Model | Count | Parts (entity) | Notes |
|---|---|---|---|---|
| Hollow (sword, 250050) | c2500 | 7 | c2500_0002, _0003, _0004, _0005, _0007, _0028, _0029 (think 250007) | Stairs and walls |
| Hollow (fleeing) | c2500 | 1 | c2500_0006 (**1010111**, think 250008) | Event 11015112 |
| Hollow BP | c2500 | 3 | c2500_0035 (1010901), _0036 (1010902), _0037 (1010903) (npc 250053) | **BP set** |
| Hollow Soldier (spear, 255040) | c2550 | 3 | c2550_0031, _0034, _0037 | |
| Hollow Soldier (sword, 255041) | c2550 | 3 | c2550_0016 (**6010**, lever puller), _0035 (think 255040), _0036 | |
| Hollow Soldier (crossbow, 255042) | c2550 | 2 | c2550_0030, _0032 | |
| Balder Knight (rapier, 256002) | c2560 | 1 | c2560_0001 (31.08,27.44,32.39) | Knight Shield corpse near |
| Balder Knight (side sword/crossbow, 256000) | c2560 | 1 | c2560_0003 (37.72,29.81,60.83, think 256008) | |
| **Armored Tusk** | c3460 | 1 | c3460_0000 (**1010310**) | 256 HP, 750 souls; lot 34600000 (Fang Boar Helm). **Non-respawning** (event 11010903 sets 11010903 on death, then disabled on load). Gate-aware AI (11010608) |
| Vagrant | c3490 | 1 | c3490_0003 (1013930) | Online-only |

**F′. Church interior / roof / belfry (y 27…76)**
| Enemy | Model | Count | Parts (entity) | Notes |
|---|---|---|---|---|
| Hollow (upper gallery, 250050) | c2500 | 10 | c2500_0040 (ladder top, 8.72,37.05,71.09), _0041, _0042, _0043, _0044, _0045, _0047, _0048, _0049, _0050 (all y≈35, think 250007) | There is no _0046 here; that one is in the Burg entry |
| Balder Knight (side sword, 256000) | c2560 | 2 | c2560_0006 (think 256003), _0007 (think 256000) | Nave floor |
| Balder Knight (rapier, 256002) | c2560 | 3 | c2560_0004 (15.60,38.17,121.0), _0005 (-4.38,35.06,104.3, think 256007), _0008 | |
| Balder Knight BP (256004) | c2560 | 1 | c2560_0000 (**1010900**) | **BP set** |
| Heavy Knight (Greatsword/Greataxe/Tower Shield, 257001) | c2570 | 1 | c2570_0000 (**1010320**) at the altar (10.69,29.32,119.0) | **Non-respawning** (11010860 slot 3); removed when Rhea arrives (11010582) |
| Heavy Knight BP (257002) | c2570 | 1 | c2570_0003 (**1010905**) | **BP set** |
| Hollow Soldier (crossbow, 255042) | c2550 | 1 | c2550_0045 | Side aisle (30,26.6,113) |
| Hollow Soldier (sword, 255041) | c2550 | 2 | c2550_0046, _0047 | Side aisle |
| Hollow Soldier (spear, 255040) | c2550 | 1 | c2550_0048 (think 255011) | (20,32.5,59) |
| **Channeler** | c2370 | 1 | c2370_0001 (**1010370**) @ (10.73,35.07,88.72) | 155 HP, 1000 souls; buffs hollows (Power Within-type) *(unverified)*; lot 23700000 (Channeler's Trident 1%). **Non-respawning** (11010860 slot 5) |
| Bell Gargoyles | c5350 ×3, c5352 ×2 | 5 | see 3.3 | Boss |
| Vagrants | c3490/c3491 | 3 | c3490_0004 (1013940), c3491_0004 (1013941), c3491_0009 (1013942) | Online-only |

**G. Lower Parish (Andre / bonfire / Titanite Demon)**
| Enemy | Model | Count | Parts (entity) | Notes |
|---|---|---|---|---|
| Prowling Demon (= **Titanite Demon**) | c2300 | 1 | c2300_0000 (**1010340**, npc 230001, think 230003) @ (87.49,-2.60,61.51) | 1255 HP, 2000 souls. Lots 23000100 (**Titanite Catch Pole**) + 23000101 (Demon Titanite). Logic regions 1012795 (aggressive), 1012796/1012797 (passive), 1012798 (nest). **Non-respawning** (11010860 slot 4) |
| Andre | c2640 | 1 | 6190 | NPC |
| Bonfire dummy | c1000 | 1 | c1000_0005 (1010964) | |

### 3.7 Items, Undead Parish (route order)
| Location | Lot → contents | Flag | Notes |
|---|---|---|---|
| Courtyard (3.05,23.06,30.80) | 1010260 Alluring Skull ×4 | 51010260 | |
| Courtyard side (-8.20,22.83,36.25) | 1010280 Large Soul of a Lost Undead | 51010280 | |
| Hideout room (-9.46,15.32,26.70) | 1010000 **Mystery Key** (goods 2017) | 51010000 | Opens Lautrec's cell |
| By the gate lever (7.55,20.50,44.11) | 1010140 **Basement Key** (goods 2001) | 51010140 | Opens 1011311 (Burg) |
| Ledge (36.40,27.50,32.35) | 1010300 **Knight Shield** | 51010300 | Balder Knight c2560_0001 nearby |
| (-1.22,26.87,59.86) | 1010520 **Halberd** | 51010520 | |
| Side aisle (37.70,29.81,81.40) | 1010320 [missing] | — | |
| Church upper (20.45,35.86,68.79) | 1010500 Large Soul of a Nameless Soldier | 51010500 | |
| Church upper, barrel o1155_0001 **1011502** (5.50,38.12,114.65) | 1010040 **Humanity** | 51010040 | Event 11010400 slot 2 |
| Church altar (10.90,30.06,124.15) | 1010050 **Fire Keeper Soul** | 51010050 | o0504 altar treasure |
| Gargoyle tail | 53520000 **Gargoyle Tail Axe** | 51010970 | |
| Gargoyles kill | 9020 Twin Humanities | — | common 970 |
| Titanite Demon kill | 23000100/23000101 Titanite Catch Pole + Demon Titanite | — | NpcParam lot |
| Andre gift/kill | 6190 Crest of Artorias (only on his death; he also sells it) | 11017140 | |

Also present but unused for treasure: barrel props o1155 1011500/1011501 (ladder top) and o1155 1011130 (Lower Burg)
have no item event *(interp.)*.

### 3.8 Interactive objects and scripted events (Parish)
| What | Object / entity | Event / flag | Behaviour |
|---|---|---|---|
| White-light fog #2 | o1416 **1011702**, SFX 1011703 (1.50,27.58,37.08) | 11010090 slot 1, regions 1012602/1012603 | First-traverse fog on the courtyard upper level; deleted after crossing |
| Church gate + lever | lever **1011100** (ObjAct 11010600, Loop, flag 61010610), gate **1011101** | 11015185, 11010600, 11010601, 11010611, 11015116, 11010609, 11010607, 11010608 | Toggle gate (anim 2 open / 4 close), navmesh 1013100, crush damage, enemy lever use, Tusk home switching |
| Ladders | 1011142 (o1210, -10.58,16.64,30.72), 1011143 (o1211, -2.97,22.79,28.06), 1011144, 1011145, 1011146, 1011150 | Register Ladder | |
| Fire hazards | o1111_07 **1011407** (3.92,18.73,28.64), o1111_08 **1011408** (10.43,15.99,12.84) | 11010008 (flags 11010308/309) | Persistent damaging objects, behavior 5000, r0.7 (braziers/fires *(interp.)*) |
| Lautrec cell door | o1308_02 **1011308** (ObjAct 1308, Mystery Key 2017) | 11010190 slot 0 (flag 11010190 / 61010508), 11010551 (enabled only after 11010593) | "Used Mystery Key" (10010876) / Master Key |
| Gargoyle fogs | o1403 **1011990**, o1404 **1011992** | 11015390/391/393, 11010001, 11015843/846 | |
| Gargoyle cutscene | — | 11010000 | Cutscene 100110 |
| Bell lever | o1202 **1011110** (ObjAct 11010700) | 11010700 | Bell cutscene 100100/100105; sets 11500200 on second bell; achievement 29 |
| Bell object | o1303 **1011111** | 11015170 | Bell SE ×3 via multiplayer event 10010 |
| Andre's prop | o1252 **1011010** | 11015090 | Breaks when Andre is hostile/dead |
| Multiplayer area fogs | listed in 1.7 (o1408 1011996 Parish nave side; o1410 1011988 church) | event 0 | Client-only |
| Boss-warning SFX regions | 1012500–1012506, 1012510, 1012520 ("boss omen: dust 1–6, camera shake, SE") | none found in EMEVD | Probably dust/shake omens on the roof driven elsewhere *(unverified)* |

### 3.9 Porting notes (Parish)
- **Two-phase gargoyle fight**: the second gargoyle is a pre-placed hidden character that warps in at 60% HP. The
  cutscene stand-in 1010802 must be disabled after the cutscene. DS2 has no cutscene 100110, so replace it with a short
  scripted intro or skip it.
- **Gargoyle tail cut** (NPC part 5350) and **Hellkite tail cut** use DS1's NPC-part system plus DispMask/HitMask toggles
  to hide the tail. DS2 has its own severable parts. Map the "hide tail + spawn tail object + award lot" pattern.
- **Bell of Awakening**: a two-bell global state (11010700 + 11400200 → 11500200) and a cutscene. In DS2 use a flag and a
  Sen's-gate opener in the m15 port, plus a "bell rung" toggle for Oswald and Domhnall.
- **Church gate with enemy lever use** requires an enemy to drive an ObjAct (`Initialize ObjAct 1011100`). DS2 may need
  a scripted gate close in place of AI interaction.
- **NPC summons** for the Gargoyles (Solaire 6540, Lautrec 6590) and their multiplayer-count gating are DSR-only
  instructions (3:24, 2003:49). With Seamless Co-op, decide whether NPC summons remain.
- **Absolution reset** (flag 744 toggle) re-places all NPCs. Oswald's absolution service needs an equivalent.

### 3.10 Area completion checklist: Undead Parish
- [ ] Altar-tower → courtyard path; fog #2 1011702
- [ ] Armored Tusk 1010310 + gate-aware home switch; lever-pull soldier 6010; gate 1011101 / lever 1011100 with crush damage
- [ ] Fleeing hollow 1010111; Mystery Key; Basement Key; Alluring Skulls; Large Souls; Knight Shield; Halberd
- [ ] Church nave: Balder Knights (5 + BP), Heavy Knight 1010320 (removed when Rhea arrives), Channeler 1010370, 11 gallery hollows, side-aisle soldiers
- [ ] Fire Keeper Soul on the altar; Humanity barrel 1011502
- [ ] Ladders to the roof; Solaire/Lautrec summon signs
- [ ] Bell Gargoyles: intro stand-in 1010802 + cutscene, #2 at 60%, tail axe, flag 3, rewards
- [ ] Lautrec cell (dialogue flag 11010593 → Mystery Key door) + departure after flag 3
- [ ] Belfry ladders, bell lever 1011110 → bell flag 11010700 (+ Sen's gate 11500200 if second bell)
- [ ] Oswald appears after the bell; Rhea at the altar (later state)
- [ ] Lower Parish: Parish bonfire 1011964, Andre 6190 (+ prop 1011010, shop, upgrades), Titanite Demon 1010340, exits to Darkroot / Firelink lift / Sen's
- [ ] (All m10_01 areas) MSB idle poses from 3A.1, dog patrols from 3A.2, and the 5 dev messages, 8 spawn points, BGM/ambient sounds and torch/fog SFX from 3A.3

---------------------------------------------------------------------------------------------------------------------

## 3A. MSB idle poses, patrol routes and MSB events: m10_01_00_00 (Burg, Lower Burg, Parish)
Source: `msbev.exe m10_01_00_00.msb enemies` and the MSB events listing. Across all 174 enemy parts here,
PointMoveType = 0, PlatoonID = 0 and DamageAnimID = -1. **Every part not listed below has InitAnimID -1**: it stands in
its default idle and its AI (think param) drives everything else.

### 3A.1 InitAnimID (MSB idle pose)
| Part | Entity | Who | InitAnimID | Pose *(look is interp. unless EMEVD confirms)* |
|---|---|---|---|---|
| c0000_0002 | 6001 | Knight Solaire | **7845** | "Praise/gaze at the sun" ledge pose. The EMEVD absolution reset also uses 7845 |
| c0000_0005 | 6072 | Rhea of Thorolund | **7880** | Kneeling in prayer at the altar (EMEVD reset uses 7880) |
| c0000_0006 | 6300 | Knight Lautrec | **7825** | Sitting on the cell floor (EMEVD reset uses 7825) |
| c0000_0012 | 6370 | Oswald of Carim | **7865** | Standing idle pose in the belfry |
| c2510_0000 | 6230 | Undead Merchant (M) | **9000** | Sitting/crouched merchant idle (EMEVD reset uses 9000) |
| c2640_0000 | 6190 | Andre | **9000** | Hammering at the anvil (EMEVD reset uses 9000) |
| c3430_0000 | 1010300 | Hellkite Drake | **7006** | Perched on the altar tower (matches EMEVD waiting anim 7006) |
| c2500_0001, _0008, _0021, _0025, _0026, _0027 | 1010250, 1010251, 1010263, 1010260, 1010261, 1010262 | Lower-Burg ambush Hollows | **9013** | Dormant pose. With map collision off (event 11015250) they're "lying in wait" in an alcove or on the ground; the wake-up is cancel anim 9060 |
| c2500_0034 | — | Altar-tower Hollow | **9005** | Idle variant (likely sitting/slumped) |
| c2500_0038 | — | Altar-tower Hollow | **9006** | Idle variant |
| c2500_0039 | — | Altar-tower Hollow | **9008** | Idle variant (likely crouched/cowering) |
| c0000_0003 / c0000_0010 / c0000_0004 / c0000_0011 | 6040 / 6580 / 6540 / 6590 | Griggs, Havel, Solaire and Lautrec summons | -1 | Default stand |
| Taurus, Capra, Gargoyles, Black Knights, Tusk, Titanite Demon, Channeler, all Hollow Soldiers/Warriors/Balder/Heavy Knights/Assassins/dogs | | | -1 | Default. Taurus's crouch 9001, the gargoyles' perch 7000 and the doors' burst-out 7001 all come from **EMEVD** (`Set Special Animation` / `Force Animation`), not the MSB |

### 3A.2 Patrol routes (MovePointNames)
Only **two** parts in m10_01 have move points:
| Part | Who | Route (regions → positions) |
|---|---|---|
| c3340_0001 | Undead Attack Dog, Lower Burg (by Griggs's door) | `移動ポイント 黒犬01_00` (-6.13,-31.35,-80.57) ↔ `黒犬01_01` (-16.42,-31.25,-75.33) |
| c3340_0002 | Undead Attack Dog, Lower Burg alley | `黒犬02_00` (-26.30,-33.03,-69.11) ↔ `黒犬02_01` (-35.87,-35.00,-58.31) |

Unreferenced move-point regions also exist in the MSB. No part's MovePointNames points to them, so they're probably
leftovers, or read by think params *(unverified)*. Port them only if patrols look wrong:
- 軽騎士04_00/01 (37.68,29.81, z 51.43→69.45). Right on Balder Knight c2560_0003 (37.72,29.81,60.83, think 256008).
- 重戦士03_00/01 (9.19→11.80, 13.54, -15.95→-13.26).
- 重戦士12_00/01 (-48.24→-57.32, -5.25, -153.2→-148.8).
- 重戦士16_00/01 (13.74→8.36, 16.00, 4.67). Near Hollow Soldier c2550_0034.
- 重戦士17_00/01 (-10.32, 16.64, 43.50→32.34). Near Hollow c2500_0007 / BP _0035.
- 重戦士19_00/01 (-10.32,26.29,37.97 → 1.18,27.58,37.11).
- 不死人05_00/01 (-2.22,-16.19,-93.67 → 7.27,-15.69,-99.71). Next to the Black Knight 1010350 in the Burg basement.
- 不死人07_00/01 (-27.17,4.21,-123.97 → -35.20,4.29,-119.10). Solaire's ledge.
- 鉈01_00/01 (6.09→15.45, 35.07, 81.68) and 鉈02_00/01 (15.59,35.07,85.92 → 15.59,38.17,123.26). The church upper gallery.

### 3A.3 Notable MSB events (m10_01)
| Type | Name | Data | Location / meaning |
|---|---|---|---|
| Message (dev blood message, Hidden=True) | 魔法血文字1 | MessageID **1100** "Imminent drake..." | (-18.70,9.54,-98.80): start of the Hellkite bridge |
| Message | 魔法血文字2 | **1101** "Prisoner ahead" | (-6.60,-31.34,-80.40): outside Griggs's door |
| Message | 魔法血文字3 | **1102** "Prisoner ahead" | (15.60,35.20,90.80): church upper gallery, toward Lautrec |
| Message | 魔法血文字4 | **1103** "Blacksmith ahead" | (35.30,26.60,107.50): side door toward Andre |
| Message | 魔法血文字5 | **1104** "Jumping off ahead" | (-61.00,-23.28,-30.70): Burg entry ledge drop to Lower Burg |
| SpawnPoint | 復活イベント00 → 復活ポイント00 | entity 1012960 | (-47.77,-21.79,-35.62): aqueduct arrival (= player 1010998) |
| SpawnPoint | 復活イベント01 → 01 | 1012961 | (23.10,9.50,-24.40): Sunlight Altar |
| SpawnPoint | 復活イベント02 → 02 | 1012962 | (0.03,-10.02,-59.30): Undead Burg bonfire |
| SpawnPoint | 復活イベント03 → 03 | 1012963 | (-111.34,-54.28,-5.66): Lower Burg side of the Depths door (same coords as the Depths' 復活ポイント01) |
| SpawnPoint | 復活イベント04 → 04 | 1012964 | (88.00,15.00,106.00): Undead Parish bonfire |
| SpawnPoint | 復活イベント05 → 05 | 1012965 | (-6.29,26.29,36.75): Parish courtyard upper level |
| SpawnPoint | 復活イベント06 → 06 | 1012966 | (13.45,38.20,136.72): belfry |
| SpawnPoint | 復活イベントboss → boss | 1012950 | (9.48,50.26,136.56): belfry above the Gargoyle roof |
| Sound (BGM) | ボスBGM | SoundID 100100000, entity 1013800 | Bell Gargoyles BGM (region 1012990) |
| Sound | 屋上の風 (rooftop wind) | 100100001, entity 1013801 | Ambient wind on the Gargoyle roof (swapped with the BGM by 11015394/395) |
| Sound (BGM) | 中ボス1BGM | 100100001, entity 1013802 | Taurus BGM (region 1012890) |
| Sound (BGM) | 中ボス2BGM | 100100002, entity 1013803 | Capra BGM (region 1012880) |
| Sound | 鳥1/鳥2 (birds), 風1–5 (wind), 風橋1/2 (bridge wind) | 100000004 / 100100001 / 100000003 | Ambience |
| SFX | 光の壁 CP1/CP2, ボス前/ボス後, 中ボス1前/後, 中ボス2前, マルチ1–12 | EffectIDs 81003–81021; entities 1011701/1011703/1011991/1011993/1011891/1011893/1011791/1011973–1011999 | Fog-wall visuals, paired with the fog objects in 1.7/2.7/3.8 |
| SFX | 燃える死体（多）/（少）06/07 (burning corpses, large/small) | 100101 / 100102 | Burning-corpse fires (on parts m2250B1 / h1004B1) |
| SFX | マップに振る火の粉 (embers falling on the map) | 100040 | Ambient ember fall |
| SFX | 松明 ×~40 (torches), church candle stands 教会石像蜀台0–7 / 祭壇蜀台 / 階段蜀台 / 隠し部屋蜀台 | 530 / 511 | Torch and candle flames. "隠し部屋" = a church **hidden room** candle (part m3510B1) |
| Light | ~52 point lights | PointLightID 15/16/17 | Paired with the torch/candle SFX |
| Navmesh | 教会レバー, 汎用扉05_1/05_2/06/08/10/11/12/13/14/17_4/18 | entities 1013100, 1013006, 1013008, 1013016, 1013003, 1013000, 1013007, 1013002, 1013010, 1013009, 1013004, 1013005 | Navmesh gates toggled by the church gate and the doors (AI pathing) |
| Wind | SFX 風 | WindVecMin <1,1.5,1>, Max <3,2,3> | Map wind for cloth/SFX |
| Environment | GIEL h1000…h1124 (55) | | Per-collision GI/environment-light probes |

---------------------------------------------------------------------------------------------------------------------

## 4. The Depths, m10_00_00_00

### 4.1 Header
- **Map**: m10_00_00_00 (area 10, block 0). It spans x -90…-216, y -57…-101, z -50…+60.
- **Entrance**: from Lower Undead Burg through door 1011306 (Key to Depths). The player arrives at MSB player
  `c0000_0001` (entity **1000999**) at (-99.13,-57.65,-10.70), the top of the Depths stairs. Map-name display region
  1022703 is at (-97.42,-64.39,-11.59).
- **Exit**: **door to Blighttown** o1309 **1001200** (-215.45,-95.46,-26.00). It needs the **Blighttown Key 2007**
  (Gaping Dragon drop lot 2500, or the Master Key; event 11000110 sets **flag 590**, which Blighttown also reads).
- **Prerequisites**: Key to Depths. The Blighttown Key is earned here.

### 4.2 Progression walkthrough
1. **Entry stairs**: client-only fog 1001994 at the door. Hollows at the stair foot (y -64): c2500_0000/0001/0002 + BP
   0013. A **Butcher drops from the ledge**: 1000090, event 11005070. When the player enters region 1002200 it plays
   anim 500 and AI turns on.
2. **Butcher kitchen**: three tiers, regions "階層A/B/C" 1002020/1002021/1002022 (y -66.9 / -70.2 / -73.2). Event
   11005200 ×11 keeps hollows 1000120–1000126 and dogs 1000110–1000113 idle (AI off) until the player enters their tier
   region or hits them.
   - **Butcher 1000099** works at (-111.45,-70.21,-31.28) next to cauldron/prop o1002 **1001400** (SFX 100013). Event
     11005050: he keeps his work idle until the player is within 7 m, he's hit, the prop breaks, or dog 1000110 enters
     combat. Then his cancel anim is 9060.
   - The **chest** o0110 **1001650** (-106.30,-70.25,-32.70) holds a **Large Ember** (event 11000600).
   - **Ceiling slimes**: c3200 1000100–1000106 hang with gravity and collision off. Event 11005100 drops each when the
     player enters its region ("腐肉 00–06" 1002100–1002103) or hits it, with delays 0 / 0.2 / 0.6 / 0.9 s.
3. **Laurentius's barrel**: o1155 **1001250** (-124.51,-68.46,-11.07), in a back room off the top tier. See 4.5.
4. **Depths bonfire** at (-120.50,-74.13,12.20), object 1001960, flag 11000992. Next to it is the **Sewer Chamber door**
   o1315_0000 **1001315** (-121.15,-74.14,-12.98; ObjAct 11315, key **2018**). Initially it's a locked shortcut.
5. **Upper sewers** (y -76…-84):
   - The **Giant Rat** 1000500 patrols its room (-129.69,-81.36,-24.08). Logic regions 1002700–1002704 make it turn
     back and return home; it does not chase far. The **Sewer Chamber Key** is on a corpse in its room
     (-129.45,-81.15,-32.63). The Spider Shield is nearby.
   - One-way door o1319 **1001319** (-139.47,-81.13,-17.31). It opens only from dummy-101's side (events
     11000100/11000101, anim 7110). The other side shows "Does not open from this side". It's a shortcut back toward
     the bonfire *(interp.)*.
   - Ladder 1001140 (o1205, -128.15,-81.12,-18.14).
   - **Rats in crates**: o1322 1001100/1001101/1001102. Event 11005150 keeps small rats 1000150/151/152 hidden until the
     player is within 3 m or the crate breaks, then the rat jumps out (anim 3002). Flags 11009000–02 = spawned,
     11009010–12 = killed (stays dead).
   - Proximity props o1340 1001000/1001001/1001002 (event 11005000) play a one-shot anim (1, 1, 3) when the player is
     within 3 m, then disable. These are hanging debris/carcasses *(interp.)*.
   - Hit region 1003100 "床滑り" (floor slide) at (-140.79,-81.13,-31.05) is the slippery slope down to the lower level
     *(interp.; no EMEVD use)*.
   - Rat pack near the **Greataxe** corpse (-159.36,-78.36,-14.47).
   - First-traverse fog o1420 **1001700** (-158.10,-81.35,-29.65), event 11000090, regions 1002600/1002601.
   - **Channeler** 1000300 at (-177.68,-84.18,-12.88).
   - Large Titanite Shard at (-191.80,-83.98,-16.60).
6. **Lower sewer / Basilisk sewer** (y -94…-98, x -105…-140):
   - 10 **Basilisks**. Basilisk 1000200 performs anim 500 seven times when the player comes within 9 m (event 11000200):
     a curse-mist display/puff *(interp.)*.
   - **Ring of the Evil Eye**, Heavy Crossbow, Humanity.
   - Regions "passing basilisk spawn" 1002010/1002011 exist but are unused in EMEVD.
7. **Gaping Dragon antechamber** (≈ -155…-175, -97.8, -40…-50): big Rats c1200 ×3 and a small rat.
   - **Kirk invasion trigger** region 1002005.
   - Solaire's and Lautrec's summon signs at (-166.5,-94.8,-15.7) and (-161.2,-94.8,-15.6).
   - Boss fog o1400 **1001990** at (-166.59,-100.15,-12.26).
8. **Gaping Dragon** arena (≈ -150…-200, -100, -10…+60). See 4.3. After the kill, continue to the upper corridor (y
   -95) with rats, slimes and BP rats, past **Domhnall of Zena** (-205.65,-95.18,-21.16), to the **Blighttown door**
   1001200 (client-only fog 1001996 there).

### 4.3 Boss: Gaping Dragon
- **Parts**: **1000800** (c5260_0000, NpcParam 526000, 2589 HP, think 526000) at (-170.0,-100.0,30.0); tail **1000801**
  (c5261_0000, NpcParam 526100) at (-185,-100,20). Boss name 5260, flag **2**.
- **Fog**: 1001990 (SFX 1001991), action region 1002998, rotate target 1002997. Events 11005390 (host), 11005391
  (phantoms), 11005393 (entry region 1002999: boss notify + buffs), 11005843/11005844 (co-op fog). BGM 1003800
  (11005394/11005395).
- **Intro (event 11005392)**: the dragon starts disabled with AI off. When the host (not an invader or BP) enters
  1002999 after the fog, cutscene **100000** plays (skippable offline, unskippable in multiplayer). Then the dragon is
  enabled, flag **11000000** is set, AI on, HP bar (slot 0, name 5260).
  - Event 0 clears 11000000 on every load for the host, so **the cutscene replays on every attempt** (it is
    skippable).
- **NPC parts**:
  - **Tail** part 5260, HP 300 (event 11005396). When broken: EzState command 1001; on message 400 it enables tail chr
    1000801 at dummy 100 with anim **8100**, hides the tail on the dragon (DispMask 0 OFF / HitMask 1 ON), AI command
    20, and awards lot **52610000 = Dragon King Greataxe** (flag 51000990).
  - Part 5261, HP 100, damage correction 0, defense material SE/SFX 60 (event 11005397). This is a hard/armoured body
    part *(interp.)*.
  - Part 5262, HP 1 (event 11005398). When hit, AI command 1 → a stagger/flinch reaction, then back to command 0 (or 20
    if the tail is already cut). A weak-point flinch *(interp.)*.
- **Death (event 11000001)**: flag **2**, the tail chr is killed silently, Handle Boss Defeat, fog removed.
- **Rewards**: common 970 slot 0 on flag 2: lots **2500 Blighttown Key** (flag 50001500), **9020 Twin Humanities**, 9030
  Homeward Bone. NpcParam lot 52600000 (none). Souls 25000 *(unverified)*.
- **Summons**:
  - **Solaire 6541** at sign 1002000 (events 11005030/11005029, flags 11005032/11005037; walk-in 11005031).
  - **Lautrec 6591** at sign 1002002 (events 11005033/11005036, needs 11020607 + 1572/1573, flags 11005035/11005038,
    SpEffect 5450 on summon 11005333; walk-in 11005034).
  - Signs are erased via 11005990 when used.
  - Both summons disappear once flag 2 is set.

### 4.4 Invasion: Kirk, Knight of Thorns (NPC black phantom)
- **Part**: c0000_0006, entity **6562**, NpcParam **6560** ("Knight Kirk"), think 6560, chrInit 6560. Placed at
  (-165.96,-97.79,-50.06), the black-sign point 1002001.
- **Trigger (event 11005039)**: host, alive (human), Gaping Dragon alive (flag 2 OFF), Kirk not yet killed here
  (11000810 OFF), and player in region **1002005** (-155.60,-98.55,-41.07). Then it places a **black summon sign** for
  6562 (summon flag 11005040, dismissal 11005041). It re-tries every 20 s.
- **Death (event 11000810)**: Kirk disabled unless the invasion is active; on death flag 11000810 is set (no repeat).
  Drops: NpcParam lot 6560 (Barbed Straight Sword; lot 6561 Spiked Shield). Humanity event value base 8956.
- Note: **Maneater Mildred is NOT in these maps.** Her invader part (entity 6531, NpcParam 6530) is in **m14_00
  (Blighttown)**, so her invasion belongs to the Blighttown census.

### 4.5 NPCs in the Depths
| NPC | Part / entity / NpcParam / talk | Where | Idle / anims | Quest steps | Death / hostility |
|---|---|---|---|---|---|
| **Laurentius of the Great Swamp** | c0000_0002 / **6130** / NpcParam 6130 (community label wrongly says "Griggs of Vinheim – Undead Burg") / talk **100613** | Inside barrel **1001250** (-124.50,-68.46,-11.05) | MSB InitAnimID **7820** (crammed inside the barrel). While captive: gravity off, map hits off, **invincible** (11000534) until the barrel breaks + 2 s. On release, cancel anim **7821** (climb out of the barrel *(interp.)*) | Flags 1250–1255. Present if 1250 (captive) or 1253 (hostile). **Rescue (event 11000530)**: when the barrel breaks or he is hit → flag 1251, barrel destroyed, gravity/collision on, flag 11000580. Leaves for Firelink at 1252 (11000531). Gift lot **1110 = Pyromancy Flame** (weapon 1330000, flag 50000110) | Hostile 1253 (11000510 slot 0), dead 1254 (11000520 slot 0) |
| **Domhnall of Zena** | c0000_0004 / **6260** / 6260 / talk **100626** | By the Blighttown door (-205.65,-95.18,-21.16) | MSB InitAnimID **7835** (seated idle *(unverified look)*) | Flags 1430–1459. Present if 1430 or 1434. **Leaves** (disabled, sets 1435) once **both bells** are rung (11010700 + 11400200, event 11000532) → appears in Firelink (m10_02 entity 6261) | Hostile 1434, dead 1435 (11000533 force treasure) |
| Shop: Domhnall | 1500–1514 (+1550–1584 as bosses die) | | | Gold Pine Resin, Bottomless Box, Crystal Straight Sword/Greatsword/Shield, Helm of the Wise, Armor of the Glorious, Gauntlets of the Vanquisher, Boots of the Explorer, arrows/bolts; later Golem, Smough, Ornstein, Dark Sun, Moonlight, Great Lord, Artorias sets, Master Key | |
| Solaire summon | c0000_0005 / **6541** | Sign 1002000 | Walk-in anim 7410 | See 4.3 | — |
| Lautrec summon | c0000_0007 / **6591** (NpcParam 6591, chrInit 6591) | Sign 1002002 | Walk-in 7410 | See 4.3 | — |
| Kirk | c0000_0006 / **6562** | 1002001 | — | See 4.4 | — |
| Bonfire dummy | c1000_0000 / 1000960 / talk 100000 | Bonfire | — | — | — |

### 4.6 Enemies, The Depths (all 93 MSB enemy parts)
| Sub-zone | Enemy | Model | Count | Parts (entity) | Notes |
|---|---|---|---|---|---|
| Entry stairs (y -64) | Hollow (Depths, 250030) | c2500 | 3 | c2500_0000 (think 250030), _0001, _0002 (think 250001) | 50 souls |
| Entry stairs | Hollow BP | c2500 | 1 | c2500_0013 (1000901) | **BP set** |
| Entry ledge | **Butcher** (jumper) | c2660 | 1 | c2660_0001 (**1000090**) @ (-114.2,-64.0,2.0) | Drop-down chase (11005070, anim 500). **Non-respawning** (11000850 slot 2) |
| Kitchen tier A (y -66.9) | Hollow (250031 unarmed / 250030) | c2500 | 6 | c2500_0003 (1000120), _0004 (1000121), _0005 (1000122), _0006 (1000123), _0007 (1000124), _0008 (1000125) | AI off until tier region 1002020 / damage (11005200) |
| Kitchen tier A | Hollow BP | c2500 | 1 | c2500_0012 (1000900) | **BP set** |
| Kitchen tier B (y -70.2) | **Butcher** (cook) | c2660 | 1 | c2660_0000 (**1000099**) | 480 HP, 1000 souls; lot 26600000 (Sack 10%). Idle-work until disturbed (11005050). **Non-respawning** (11000850 slot 1) |
| Kitchen tier B | Butcher BP | c2660 | 1 | c2660_0002 (**1000905**, npc 266001) | **BP set** |
| Kitchen tier B | Undead Attack Dog (Depths) | c3340 | 2 | c3340_0000 (1000111, npc 334001), _0002 (**1000110**, npc 334002) | AI off until tier 1002021/1002022. 1000110 is **non-respawning** (11000850 slot 0) |
| Kitchen ceiling (y -70.5) | **Slime** (ceiling drop) | c3200 | 7 | c3200_0000 (1000100) @ (-92.5,-70.8,-37.9); _0001–_0006 (1000101–1000106) along the corridor (-119.8…-122.8, -70.3…-70.7, -21…-32) | 11005100; lot 32000000 (Large Titanite Shard / Green Titanite Shard) |
| Kitchen tier C (y -73.2) | Hollow | c2500 | 1 | c2500_0009 (1000126) | |
| Kitchen tier C | Hollow BP | c2500 | 2 | c2500_0014 (1000902), _0015 (1000903) | **BP set** |
| Kitchen tier C | Undead Attack Dog | c3340 | 2 | c3340_0001 (1000112), _0003 (1000113) | |
| Bonfire level (y -74) | Hollow | c2500 | 2 | c2500_0010 (-107.2,-74.1,-42.8, think 250011), _0011 (-120.0,-74.2,-16.8, think 250013) | |
| Bonfire level | Hollow BP | c2500 | 1 | c2500_0016 (1000904) | **BP set** |
| Barrel room (y -68.5) | Vagrant (aggr.) | c3491 | 2 | c3491_0000 (1003901), _0002 (1003902) | Online-only |
| Upper sewer | **Giant Rat** | c1202 | 1 | c1202_0000 (**1000500**) | 495 HP, 1000 souls, lot 12020000 (Humanity). Death sets flag **11000800** → never respawns |
| Upper sewer | Small Rat (Depths, 120102) | c1201 | 12 | c1201_0003, _0004, _0005, _0009, _0010–_0014 (pack of 5 by the Greataxe), _0015–_0017 (trio by the key room) | 60 souls; Humanity drop chance |
| Upper sewer | Small Rat (in crates, 120102/120100) | c1201 | 3 | c1201_0000 (**1000150**), _0001 (**1000151**), _0002 (**1000152**) | Crate ambush 11005150 |
| Upper sewer | Small Rat BP (120104) | c1201 | 5 | c1201_0018 (1000906), _0019 (1000907), _0020 (1000908), _0021 (1000909), _0022 (1000910) | **BP set** |
| Upper sewer (y -84) | Rat (dog-sized, 120000, think 120010) | c1200 | 3 | c1200_0003, _0004, _0005 @ (-151…-160, -84.2, -6…-8) | 100 souls |
| Upper sewer | Slime | c3200 | 1 | c3200_0010 (-115.37,-84.34,-7.69) | Free (no drop event) |
| Upper sewer | **Channeler** | c2370 | 1 | c2370_0000 (**1000300**, think 237002) @ (-177.68,-84.18,-12.88) | **Non-respawning** (11000850 slot 3); NpcParam lot 23700000 (Channeler's Trident 1%). Not widely documented; ground truth from MSB |
| Upper sewer | Slime (ceiling) | c3200 | 1 | c3200_0007 (**1000107**) @ (-179.67,-89.42,-27.10) | 11005100 slot 7, region 1002107 |
| Upper sewer | Vagrant (passive) | c3490 | 2 | c3490_0000 (1003900), _0001 (1003910) | Online-only |
| Lower corridor to Blighttown (y -95) | Small Rat | c1201 | 2 | c1201_0006, _0007 | |
| Lower corridor | Small Rat BP | c1201 | 3 | c1201_0023 (1000911), _0024 (1000912), _0025 (1000913) | **BP set** |
| Lower corridor | Slime | c3200 | 2 | c3200_0008 (-171.5,-95.4,-24.5), _0009 (-187.9,-95.5,-27.3) | |
| Basilisk sewer (y -94…-98) | **Basilisk** | c3270 | 10 | c3270_0000 (**1000200**, puff display 11000200), _0001, _0002, _0003, _0004 (think 327001), _0005, _0006 (1000210, think 327001), _0007 (327001), _0009, _0010 | 84 HP, 200 souls; **Curse** gas; lot 32700000 (Eye of Death 6%) |
| Basilisk sewer | Vagrant (aggr.) | c3491 | 2 | c3491_0001 (1003911), _0003 (1003912) | Online-only |
| Gaping antechamber (y -97.8) | Rat (dog-sized) | c1200 | 3 | c1200_0000, _0001, _0002 (think 120000) | |
| Gaping antechamber | Small Rat | c1201 | 1 | c1201_0008 | |
| Gaping arena | Gaping Dragon + tail | c5260 / c5261 | 2 | 1000800 / 1000801 | Boss |
| NPCs | c0000 ×5, c1000 ×1 | | 6 | 6130, 6260, 6541, 6562, 6591, 1000960 | See 4.5 |

Totals: c1201 26 (8 BP) · c1200 6 · c2500 17 (5 BP) · c2660 3 (1 BP) · c3200 11 · c3270 10 · c3340 4 · c1202 1 · c2370 1 ·
c3490 2 · c3491 4 · c5260/c5261 2 · c0000 5 · c1000 1 = **93**.

### 4.7 Items, The Depths (route order)
| Location | Lot → contents | Flag | Notes |
|---|---|---|---|
| Entry stairs (-97.85,-63.72,4.85) | 1000040 Large Soul of a Nameless Soldier | 51000040 | |
| Kitchen (-114.20,-72.80,-8.10) | 1000060 [missing] | — | |
| Kitchen chest **1001650** (-106.30,-70.25,-32.70) | 1000500 **Large Ember** | 51000500 | Chest ObjAct 11000600 |
| Kitchen ledge (-91.66,-73.57,-37.85) | 1000030 Soul of a Nameless Soldier | 51000030 | Under slime 1000100 |
| (-109.50,-80.86,-30.70) | 1000140 Soul of a Nameless Soldier | 51000140 | |
| (-109.25,-83.28,-14.30) | 1000120 Soul of a Nameless Soldier | 51000120 | |
| Giant Rat room (-133.00,-81.11,-21.60) | 1000130 [missing] | — | |
| Giant Rat room (-129.45,-81.15,-32.63) | 1000240 **Sewer Chamber Key** (goods 2018) | 51000240 | Opens door 1001315 |
| (-138.90,-76.36,-23.11) | 1000090 **Spider Shield** | 51000090 | |
| (-155.25,-81.33,-39.93) | 1000010 Soul of a Nameless Soldier | 51000010 | |
| Rat pack (-159.36,-78.36,-14.47) | 1000020 **Greataxe** | 51000020 | |
| (-191.80,-83.98,-16.60) | 1000100 **Large Titanite Shard** | 51000100 | |
| Basilisk sewer (-133.30,-92.50,-36.08) | 1000000 **Humanity** | 51000000 | |
| Basilisk sewer (-105.20,-97.75,-18.60) | 1000050 Large Soul of a Nameless Soldier | 51000050 | |
| (-105.05,-97.60,-6.70) | 1000110 [missing] | — | |
| (-127.65,-97.80,-34.10) | 1000160 [missing] | — | |
| (-129.70,-97.62,-38.80) | 1000170 **Ring of the Evil Eye** | 51000170 | |
| (-129.30,-94.87,-9.00) | 1000180 Soul of a Nameless Soldier | 51000180 | |
| (-157.54,-94.83,-6.09) | 1000190 **Heavy Crossbow** (+1000191 Heavy Bolts) | 51000190 | |
| (-161.40,-95.02,-21.19) | 1000150 [missing] | — | |
| (-157.96,-99.98,-8.74) | 1000200 [missing] | — | |
| Gaping arena (-192.95,-100.11,22.99) | 1000210 **Standard Helm** (+1000211–213 Hard Leather Armor, Gauntlets, Boots) | 51000210 | |
| Gaping arena (-165.00,-100.85,58.50) | 1000220 [missing] | — | |
| Gaping arena (-196.00,-100.05,22.90) | 1000230 [missing] | — | |
| Gaping tail | 52610000 **Dragon King Greataxe** | 51000990 | |
| Gaping kill | 2500 **Blighttown Key** + 9020 Twin Humanities + 9030 Homeward Bone | 50001500 | |
| Laurentius rescue | 1110 **Pyromancy Flame** | 50000110 | |

### 4.8 Interactive objects and scripted events (Depths)
| What | Object / entity | Event / flag | Behaviour |
|---|---|---|---|
| Depths bonfire | o0200_0000 **1001960** | event 0, flag 11000992 | |
| Sewer Chamber door | o1315_0000 **1001315** (ObjAct 11315, key 2018) | 11000120 (flag 11000120 / 61000500) | "Used Sewer Chamber Key" (10010877) or Master Key |
| Plain door | o1315_0001 (no entity, ObjAct flag 61000501) @ (-96.13,-69.94,-25.07) | — | Generic openable door in the kitchen |
| One-way door | o1319_0000 **1001319** | 11000100 / 11000101 (flag 11000100) | Opens from one side (anim 7110) |
| Blighttown door | o1309 **1001200** | 11000110 / 11000111 (flags 11000111, **590**) | Needs Blighttown Key 2007; anim 7120; dialog "Used Blighttown Key" (10010866); without the key "It's locked" (10010163) |
| Ladder | o1205 **1001140** | Register Ladder 11000010/11 | |
| First-traverse fog | o1420 **1001700** | 11000090 | |
| Gaping fog | o1400 **1001990** | 11005390/391/393, 11000001, 11005843/844 | |
| Client-only fogs | o1401 **1001994** (entry), o1402 **1001996** (Blighttown door) | event 0 | Removed for host/offline |
| Laurentius barrel | o1155 **1001250** | 11000530/11000534 | |
| Butcher prop | o1002_01 **1001400** | 11005050 / 11005060 | SFX 100013 until destroyed; destroying it wakes the Butcher |
| Rat crates | o1322_0002 **1001100**, o1322_0005 **1001101**, o1322_0006 **1001102** | 11005150 | |
| Proximity props | o1340 **1001000/1001001/1001002** | 11005000 | One-shot anim + disable |
| Stone statues | o0100 **1001900–1001907** (8 at -107.75…-108.75, -62.38, -6.5…-9.5) | event 0 `Register Statue (Stone)` | Placeholder statue slots for petrified/cursed players *(interp.)* |
| Kitchen chest | o0110 **1001650** | 11000600 | Large Ember |

### 4.9 Porting notes (Depths)
- **Curse** (Basilisk mist) is the defining hazard: curse build-up → instant death and halved max HP until cured (Purging
  Stone / Velka statue / Oswald). DS2 has **Petrify** (instant, from Basilisks too) but no lingering half-HP curse. Map
  it to DS2 petrification, or recreate the DS1 curse SpEffect chain.
- **Ceiling slimes** need gravity/collision toggling and per-region delayed drops; DS2 supports `SetCharacterGravity` /
  map-hit equivalents.
- **Laurentius barrel** pattern: NPC invincible inside a breakable object until it breaks, then gravity on + climb-out
  anim 7821.
- **Rat-in-crate** pattern, **tiered kitchen wake-up** (AI off until a layer region), and the **jump-down Butcher**.
- **Gaping Dragon**: three NPC parts (tail 300 HP severable, armoured part 5261, 1-HP flinch part 5262). The
  belly-mouth grab and acid are AI/TAE driven. It's a large model (c5260), so DS2 needs a capable chr slot and a big
  arena collision.
- **Kirk** uses an NPC black-sign invasion (2003:25 with Black Sign). DS2 NPC invasions work differently (scripted
  `SummonBlackPhantom` style); Seamless Co-op interaction is TBD.
- **Black-tendency phantoms** (1000900–1000913), as in the Burg.
- **Global flags** to export: 2 (Gaping dead), 590 (Blighttown door opened, read by m14), 11000810 (Kirk Depths killed),
  Laurentius 1250–1255, Domhnall 1430–1459.

### 4.10 Area completion checklist: The Depths
- [ ] Entry door link from Lower Burg 1011306 → player point 1000999
- [ ] Entry hollows + jump-down Butcher 1000090
- [ ] Kitchen tiers A/B/C wake-up (11005200), Butcher 1000099 + prop 1001400, dogs, BP set
- [ ] Ceiling slimes 1000100–1000107 with drop delays
- [ ] Large Ember chest 1001650; all pickups in 4.7
- [ ] Laurentius barrel rescue (1001250, flags 1250–1255, Pyromancy Flame)
- [ ] Depths bonfire 1001960; Sewer Chamber door 1001315 (+ key from the Giant Rat room)
- [ ] Giant Rat 1000500 (one-time) with leash regions; rats in crates; rat packs
- [ ] One-way door 1001319; ladder 1001140; fog 1001700
- [ ] Channeler 1000300 (one-time); Basilisks ×10 with Curse; Ring of the Evil Eye
- [ ] Kirk black-sign invasion at 1002005
- [ ] Solaire / Lautrec summon signs for Gaping
- [ ] Gaping Dragon: cutscene 100000, fog 1001990, tail cut → Dragon King Greataxe, parts 5261/5262, flag 2, Blighttown Key
- [ ] Domhnall 6260 (shop; leaves after both bells)
- [ ] Blighttown door 1001200 (key 2007, sets flag 590) → m14_00
- [ ] Idle poses (Laurentius 7820, Domhnall 7835, Butcher 9000, slimes 9000/9001), torch-hollow patrol, 4 dev messages, 3 spawn points, sewer/waterfall ambience (4A)

---------------------------------------------------------------------------------------------------------------------

## 4A. MSB idle poses, patrol routes and MSB events: m10_00_00_00 (Depths)
All 93 parts have PointMoveType 0, PlatoonID 0 and DamageAnimID -1. Parts not listed have InitAnimID -1.

### 4A.1 InitAnimID
| Part | Entity | Who | InitAnimID | Pose |
|---|---|---|---|---|
| c0000_0002 | 6130 | Laurentius | **7820** | Inside the barrel (released via cancel anim 7821, event 11000530) |
| c0000_0004 | 6260 | Domhnall of Zena | **7835** | Seated/idle merchant pose *(look unverified)* |
| c2660_0000 | 1000099 | Butcher (cook) | **9000** | Working idle: chopping/stirring at the pot. Cancelled with 9060 by event 11005050 |
| c3200_0000, _0002, _0003, _0004, _0005, _0006 | 1000100, 1000102–1000106 | Ceiling slimes | **9000** | Clinging to the ceiling (gravity off until the drop event) |
| c3200_0001, _0007 | 1000101, 1000107 | Ceiling slimes | **9001** | Ceiling-cling variant |
| c2660_0001 | 1000090 | Jump-down Butcher | -1 | The jump is EMEVD anim 500 |
| c3270_0000 | 1000200 | Basilisk | -1 | The 7× anim 500 display is EMEVD (11000200) |
| everything else | | | -1 | Default |

### 4A.2 Patrol routes
| Part | Who | Route |
|---|---|---|
| c2500_0010 | Hollow (Depths, unarmed npc 250031, think 250011) on the bonfire level | `移動ポイント 松明00_00` (-106.98,-74.14,-42.85) → `松明00_01` (-121.22,-74.13,-42.68) → `松明00_02` (-121.30,-74.14,-47.81). "松明" = torch: a torch-carrying patrol *(interp.)* |

Unreferenced move points (not linked to any part): `松明01_00/01` (-122.25,-74.14, -20.49↔-30.22) is next to Hollow
c2500_0011 (-120.03,-74.15,-16.8), which is likely the second torch-bearer. `バジリスク00–04` (basilisk) pairs sit in the
lower sewer: 00 (-120.6,-94.8,-23.7↔-8.8), 01 (-109.4,-97.8,-18.5↔-10.3), 02 (-105.8,-97.8,-15.3↔-7.8), 03
(-107.5,-97.8,-18.9↔-6.1), 04 (-123.5,-98.1,-26.6↔-16.7). These look like intended basilisk patrols.

### 4A.3 Notable MSB events (m10_00)
| Type | Name | Data | Location / meaning |
|---|---|---|---|
| Message (dev blood message, Hidden=True) | 魔法血文字1 | MessageID **1000** "Try sliding down" | (-141.00,-81.07,-31.00): at the slide region 1003100. Confirms the slope down to the lower sewer |
| Message | 魔法血文字2 | **1001** "Weakness: Head" | (-182.40,-100.12,-5.40): Gaping Dragon arena entrance |
| Message | 魔法血文字3 | **1002** "Imminent shortcut..." | (-129.90,-78.32,-17.30): by the one-way door 1001319 / ladder 1001140 |
| Message | 魔法血文字4 | **1003** "I can't take this..." | (-121.70,-74.14,17.00): at the Depths bonfire |
| SpawnPoint | 復活イベント00 → 復活ポイント00 | entity 1002960 | (-120.50,-74.14,14.20): Depths bonfire |
| SpawnPoint | 復活イベント01 → 01 | 1002961 | (-111.34,-54.28,-5.66): Depths door (Lower Burg side) |
| SpawnPoint | 復活イベントboss → boss | 1002950 | (-170.99,-99.65,0.00): Gaping Dragon arena |
| Sound (BGM) | ボスBGM | 100000000, entity 1003800 | Gaping Dragon BGM (region 1002990) |
| Sound | ボス滝 / 滝 (boss waterfall / waterfall) | 107700000 | Waterfall in/near the Gaping arena |
| Sound | 下水の滝2 (sewer waterfall 2) | 106600000 | |
| Sound | へどろ1–3 (sludge), 下水の水飛沫 ×3 (sewer splashes), 下水の滝 (sewer waterfall), 落とし穴の水飛沫 (pitfall splash), 地下サウンド1/2 (underground) | 100000001 / 100000002 | Ambience |
| SFX | 光の壁 CP1 / ボス前 / マルチ1 / マルチ2 | 81020 / 81000 / 81001 / 81002; entities 1001701 / 1001991 / 1001995 / 1001997 | Fog visuals for 1001700 / 1001990 / 1001994 / 1001996 |
| SFX | m2130 厨房の火 (kitchen fire) | 100012 | Butcher kitchen fire |
| SFX | 下水の水飛沫 (splashes) / 下水の滝 (sewer waterfall) / 落とし穴の滝 (pitfall waterfall) / 落とし穴の水飛沫 (pitfall splash) | 100098 / 100099 / 100096 / 100097 | Sewer water effects. "落とし穴" = pitfall, the drop into the lower sewer *(interp.)* |
| SFX | m2200 霧01/02 (mist) | 100001 | Sewer mist |
| SFX | ~40 torches 松明 | 530 | Torch flames (+54 point lights) |
| Navmesh | 汎用扉02 / 1315_0000 / 1315_0001 | entities 1004002 / 1004000 / 1004001 | Door navmesh node banks |
| Wind | SFX 風 | Min <-1,1,-1>, Max <-2,2,-2> | Map wind |
| Environment | 14 GIEL probes | | |

---------------------------------------------------------------------------------------------------------------------

## 5. Cross-area flag summary (for the port's flag map)
| Flag | Meaning |
|---|---|
| 2 / 3 | Gaping Dragon dead / Bell Gargoyles dead |
| 11010901 / 11010902 | Taurus dead / Capra dead |
| 11010700 | First Bell of Awakening rung (Parish) |
| 11400200 | Second bell (Blighttown/Quelaag) rung, read here |
| 11500200 | Sen's Fortress gate open (set here when ringing the 2nd of the two bells) |
| 590 | Blighttown door (Depths side) opened |
| 11010900 | Hellkite gone (fled at <10% or killed) |
| 11010791 / 11010790 | Hellkite landing intro done / fly-over done |
| 11010100 | Under-bridge ladder lowered |
| 61010610 | Parish church gate lever state |
| 11010171 / 11010172 / 11010140 / 11010181 / 11010190 / 11010191 / 11010192 / 11000120 | Doors opened: Depths, Basement-Key, Residence house, Griggs, Lautrec cell, watchtower top/bottom, Sewer Chamber |
| 11010050 / 11000050 | Pure-black world tendency active in Burg-Parish / Depths (BP sets) |
| 744 | Absolution toggle (NPC re-placement) |
| 1000–1011 Solaire, 1110–1119 Griggs, 1170–1198 Rhea, 1250–1255 Laurentius, 1320–1339 Andre, 1400–1409 Merchant, 1430–1459 Domhnall, 1570–1599 Lautrec, 1700–1709 Oswald | NPC state ranges |
| 11015102/106, 11015105/107, 11005032/037, 11005035/038, 11005040/041 | NPC summon/invasion sign flags |
