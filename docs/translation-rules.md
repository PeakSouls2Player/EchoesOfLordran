# DS1 → DS2 universal translation rules

The rulebook every converter follows. Goal (user, session 7): fixed rules for **every** kind of DS1 content, so the rest
of DS1 ports quickly and repeatably; nitpicking comes after. Principles, in priority order:

1. **DS1 content, DS2 rules.** DS2 owns menus, stats, upgrades, healing, online. Anything DS1-specific is reproduced 1:1.
2. **Translate DS1's own code and data; don't re-author.** DS1 scripts, AI, animations, timings and params are converted
   mechanically into DS2-compatible forms. Hand-written behaviour only where no rule exists yet, and then only as the
   reference for a new rule.
3. **DS1 characters are 1:1**: DS1 skeleton, animations, movement, attack patterns (AI) and timings. No DS2 stand-in
   bodies, movesets or AI (the current Hollow/Skeleton/Soldier/Warrior ports are interim and get replaced).
4. **Duplicates use DS2's version** (items that exist in both games). No UXM, never touch ds2sc.dll, never change a
   vanilla DS2 bonfire row.
5. Each rule has a status: **V** verified in game, **P** provisional (built, not verified), **U** unknown (open
   question). A rule changes only with evidence; record it here and in HANDOFF.

Tools that read the DS1 side: `emevd-dump/-stats`, `msb1-dump MAP players|pieces|objects|collisions|regions|treasures|objacts`,
`hkx-anim`, `ds1-text`, `dump-param dsr`. DS2 side: `esd-dump/-usage`, `edd-machines` (developer presets with parameter
names), `obj-states`, `map-param`, `msb2-dump`, `dump-param ds2`, `tae-dump`, `flver-bones`.

---

## 1. Identifiers and coordinates

| DS1 | DS2 | Status |
|---|---|---|
| map mAA_BB (DS1 ids 10..18) | same id if free in DS2 (Asylum m18_01); if taken, area 19 (Firelink m10_02 → m19_02) | V |
| world position / rotation | unchanged (our MSB MapOffset = 0; per-map params are map-local = world) | V |
| bonfire | id `40000 + (AA-10)*1000 + BB*100 + 50 + 5i`; object placed at DS1's o0200 part (position + rotation) | V |
| event flag 1AABnnnn (map flag, e.g. 11810312) | ~~`A BB nnnnnn` per-map flag~~ **wrong (session 9): DS2 stores flags only in blocks id/10000 that exist: global blocks 10 and 20 (10000 flags each, saved) and 3 × 200-flag blocks per loaded map (P·100+k, P = (AA/10)·100+BB); any other id reads 0 and drops writes (FUN_140474230/FUN_1404750b0). 2656 of our 2704 flag refs were unbacked (Oscar's talk flags, gate 101000312, Firelink 142xxxxxx).** Rule **F1**: persistent DS1 flags → bits of global blocks 10/20 from an append-only table `Mappings/ds1-flag-slots.tsv` (census of 9174 DSR flags, runs no vanilla data uses); **F2**: temporary 1AAB5nnn → the hosting map's k1 block (overflow k2); **F3** restart requests in the temporary list; **T1** event 0 clears the temporary list per flag (130131 is NOT a range set: it sets EventFlagListParam rows) | **V** (s9-19: Oscar's 2nd talk = after-gift lines; gate shuts behind) / open: co-op sync of global blocks, boulder re-roll |
| global DS1 flags (0..9999, 50000000+, 7xxxxxxx) | F1 (above) | P |
| character / point facing | **P1: DS2 yaw = DS1 yaw + 180** (DS1 models face −Z, DS2 +Z; Saulden/Maughlin/Oscar, 332 DS1 characters vs walls) for generators, DS1 placement point regions and warp targets; area regions keep DS1's rotation | **V** (s9-12: Oscar faces the player) |
| character height | **P2**: root from the clip that actually plays (pelvis on DS1's seat), on our collision via FloorProbe; `spot-check` fails generators/test spots off the floor | P |
| object entity 1AABxxxx | MapObjectInstanceParam id `AABB0000 + entity % 10000` (1811111 → 18011111) | P |
| region entity | EventLocation id `37 00000 + …` today ad hoc (3701996 = DS1 1812996); rule: `3700000 + 1000 + entity % 1000`? | P |
| character entity | GeneratorLocation/GeneratorParam id (Asylum generators 2500..); rule to fix with the enemy pipeline | U |
| DS1 item ids | `item-map.tsv`: shared → DS2 item; new → weapons 7M/17M+id, goods 52M + id×1000 | V |
| DS1 text | DS1 FMG text copied into the DS2 FMG the menu reads (mapevent, item names…) | V |

## 2. Maps

| DS1 | DS2 | Status |
|---|---|---|
| map pieces (FLVER, fragments with node transforms) | DS2 map pieces, DS2 map MTDs, DS1 lightmap → GI texture | V |
| water MTD | DS2 water material | V |
| collision HitFilterID 8 | DS2 collision (Havok packfile); other filters left out | V |
| collisions disabled by DS1's event 0 | left out (or toggled by the translated event) | V (one case) |
| navmesh (NVM) | one NGP mesh | P |
| map registration | WorldMapList line, AreaParam/NetworkAreaParam/TorchParam rows, bonfire, banner, Travel pictures | V |
| light probes for objects | own `pointcloud.btpb` per map (`LightProbes`: grid over the playable area, one neutral payload ×1.6). Probe positions are MapOffset-relative; TB's sat ~300 m away and every object rendered black | V |
| DS1 draw groups / backread | not ported: all parts draw (`FFFFFFFF`) | P |
| depth of field (DS1 DofBank) | far blur from DS1 (FarStart/FarEnd, DOF Alpha = farDofMul/100); **near blur always off (-1, -2)**: DS2 measures from its camera focus point (just past the player), DS1 from the camera, so DS1's near band blurred the player (user report, Firelink, session 8) | P (fix built, user to confirm) |
| lighting (DS1 drawparam: light, fog, ambient, shadow, tone map; env cubemaps), BGM + ambient sound, map ambience SFX (dust, embers, fog particles) | **user, session 8: replicate DS1's**; fallback only where impossible: the DS2 area that fits best (atmos track: DS1 drawparam → fltparam per zone; skylight track rules below) | P (sound U) |
| **S1 sky dome** (DS1 sky material: MTD "Sky" / 【天球】) | DS2 `SkyDome2[NoFog].mtd` with an RGBM texture baking DS1's own result (tex × MTD multiplier, DS1 part fog + scattering), the model takes DS2's sky ACB (RenderType 2, no shadows) when it is all-unlit; **S1b**: a DS1 model with a sky material AND lit meshes (Firelink m9000B2) is split: sky → own model m9999↓ (dome = material 0), lit meshes stay under the DS1 name | **V** (s9-1: Firelink blue-grey cloud dome, no white void, no black blotches; Asylum pale overcast dome) |
| S3 / S3b unlit colour, distant unlit pieces | DS1 g_DiffuseMapColor × Power baked into vertex colour; pieces > 300 m also get their part's fog + scatter baked | **V** (s9-1: Firelink cliffs fade into teal-grey haze, no white wash) |
| S4 draw-group variants | MSB1 parts with the same model + transform → one part | P |
| H1 scattering (LightScatteringBank) | DS1's formula on the zone fog colour (Firelink #72726E → #545B5D) | P (look V s9-1) |
| A3 fallback area | best-fit DS2 area ranked by DS1's authored fog colour; close calls logged; `--atmos-template MAP=AREA[:F]` pins | P |
| A7b grading (ToneCorrectBank) | own 16³ LUT palette per DS1 row in filterresource.fltbnd (vanilla entries untouched), mode 3 | **V** (s9-1: Firelink no longer yellow-green) |
| A8c exposure (ToneMapBank) | every zone adapts with DS1's band/speed; AvgLuminance = bias/(τ·key·LB), τ(0.18) = 0.324; `--exposure-bias X` (> 1 darker) | **V** (s9-1: Firelink and the Asylum clearly darker than s8-4, readable; user to judge against DSR) |
| B1b map effects (area emitters) | ⌈W1/W2⌉×⌈D1/D2⌉ copies of the DS2 effect over DS1's footprint at DS1's height (Firelink mist 100201 → 8× DS2 3011) | U (s9-1: no ground mist visible at the bonfire/steps) |
| DS1 step risers (0.08–0.45 m, e.g. the hall platforms) | the DS2 player can't climb them. **Decided (user, session 8): invisible ramps; DS2's player step height stays vanilla.** `StepRamps`: from DS1's walkable collision, a riser = vertical face with floor at its foot on one side and at its top on the other (≥ 0.6 m wide); a wedge from its top edge down to the floor `run` in front (run = distance to the nearest parallel riser in front that overlaps laterally (≥ 5 cm, measured in the riser's own frame), so staircases chain into one surface through the nosings and no wedge overhangs the step below; lone steps 0.55 m, ≤ 40°; duplicate risers from overlapping DS1 collision parts merged. Session 8 fixes: the first version's 'in front' had the wrong sign and compared plane offsets from the world origin, so stairs never chained and Firelink's broken stairs got overhanging 0.55 m wedges = lips the DS2 player can't climb (user: 'broken stairs, invisible wall')); top faces become extra map collision parts `h0A_99NN` (≤ 48 triangles, our own MOPP: split-plane nodes `10+axis leftMax rightMin len` + terminals, format read from DS2's ladder/wall files; `ramp-survey MAP` self-tests it). DS1's long staircases already have sloped collision (no ramps needed). Asylum 211 ramps, Firelink 606 | **V** (run s8-1: MP walked hall floor 197.71 → 198.10 over two risers and back) |

## 3. Objects

| DS1 | DS2 | Status |
|---|---|---|
| model FLVER (rigid, multi-node) | object FLVER: rigid meshes split one per bone (one-entry bone list), plain vertex layout, NormalW = bone-list index, colour-0 alpha 1, textures under `Model\obj\NAME\tex` | V (hall door: both leaves, textured, lit) |
| skinned (dynamic) meshes | skinned layout + bone indices/weights | P |
| animations (Havok 2015 spline) | Havok 2014 packfile `a00_AA_BBBB.hkx`, id AA·10000+BBBB; which DS1 clip does what: decode with `hkx-anim` | V (converter), V (decoder) |
| collision | DS1 hkx → packfile with a DS2 type table; rigid bodies named after what they bind to (static: the model name, o8510 → o18_8510; moving: the bone); a template must hold every class the file uses (types + class names): the first DS2 object collision with all of them is searched; DS1's hkpConvexVerticesConnectivity is dropped (DS2 never stores it); still not convertible: hkpConvexTranslateShape + hkpSphereShape together (boulder) | V (hall floor, door, columns) / U (boulder) |
| Havok C-arrays of pointers (constraint entities[2], ragdoll motors[3]) | converted like single pointers (shared objects keep identity); session 8: they were written null and crashed DS2 on the first ragdoll (c2501 spawn) | **V** (s8-7) |
| ragdoll cone limits (hkpConeLimitConstraintAtom.memOffsetToAngleOffset) | DSR's 2015 runtime layout 160 → DS2's 56 (every DS2 cone limit); npData 0xFFFFFFFF, dispatchType 4 | V (s8-7 ragdoll stable) |
| hkHalf fields (timeFactor, gravityFactor, damping) | the top 16 bits of a float32 (0x3F80 = 1.0), not IEEE half; converter bug fixed session 8 (it wrote 1.0 as 2: timeFactor ≈ 0 froze break fragments) | V (s8-3: fragments fall) |
| moving-object bodies | DS1 KEYFRAMED (motion 4, quality 1) → DS2's FIXED (5, 0), as every DS2 bone-bound body; static/intact bodies always FIXED; break fragments DS1 dynamic (1) → DS2 box-inertia (3) | P (convention; DS2 moved the type-4 hall door too) |
| objects that rise to open (Asylum gate o8542: 1.83 m) | DS1's collision then clears 1.61 m: enough for DS1's player, not DS2's. Shapes touching the floor are trimmed from below so the opening clears 2.0 m (DS2's bars ~2.4 m); the closed gap at the floor stays 0.17 m | **V** (s8-2: MP passes the open gate) |
| decoration objects with DS1 collision | DS1 ObjectParam hp −1 or defense > 0 → static object with DS1's collision (`Kind.Decor`, template o16_1230 with isBreakable 0: church pillars o8600, sewer columns o8610, gargoyles, bags); else → DS2 breakable (`Kind.Breakable`, template DS2's jar o00_2100: DS1 NAME.flver/.hkx = intact, NAME_1.flver/.hkx = fragments, names prefix-swapped); placed at every DS1 part with instance 0 (behaviour from the model's MapObjectDefaultInstanceParam → objType 1906 breakAction 4 = breaks on roll); MSB UnkT04 = the map's bonfire (u16; DS2's jars: their area's bonfire) | **V** solid columns/pots, pots shatter on hit (s8-2/3) / U break on roll / U pillars breakable only by the demon |
| ObjAct door (player opens) | DS2 door (objCtrlType 2, MapObjectDoorParam) | V (opens) |
| ObjAct key requirement | event owned by the door: `131602(region, item, 1005 "Used %s", 1105 "It's locked" / 1101 "Does not open from this side", flag, 0)` | P |
| object animated by events | prefab object (`prefabeditor/MODEL.pfbbin` = state machine; GDParam.isPrefab = 1), states via `131636(instance, state)` / `f131653`; clone a vanilla prefab whose states match (o10_1001: 10 → 70 anim 10001 → 30 → 80 anim 10002; o10_9240: 10 → 70 anim 1 → 20) | V (hall door opens and closes) |
| action button on an object/area | invisible key-guide prefab object (o04_9300 clone, KeyGuideParam with DS1's text, facing like the object) + `140205(group, instance)`; prompt switched on (`131638`) only while DS1 waits for it | V |
| ObjAct (player acts on an object) | its own helper + rule group: examined → the object state for the ObjAct's anim, DS1's ObjAct flag set | V (hall door) |
| "Light Wall" fog (o8950…) | white door o00_0501 + MapObjectWhiteDoorParam (cat 1 boss fog, passFlag = boss-killed flag; cat 5 two-way); show/hide `130702(instance, 1|0)` | V (traverse) |
| treasure | item corpse o00_0240 + ItemLotParam2_Other | V |
| ladder | o00_0903/0904-based object, MapObjectDefaultInstanceParam, dummies 191/192 | V |
| breakable floor / destructible | intact model as a Static object (model + collision) until the break event is ported | V (intact) / U (break) |
| elevator | U (event-driven DS2 lifts) |

## 4. Events (EMEVD → DS2 event ESD)

Implementation: `Build/Events/EmevdTranslator.cs` (+ `FlagRules`; per-map knowledge in a context, `AsylumContext`);
report with `lr.cmd emevd-translate MAP`. Arguments are decoded from DarkScript3's EMEDF types (DSR stores byte-sized
arguments in 4-byte slots). Asylum today: 9 of 99 event instances translate (event 0, the hall door/gate events
11810311/312/313/111, BGM flags); the rest wait for character, item and cutscene rules. **Verified in game (run
s7-12): entering the hall, DS1's own 11810311 shut the hall door behind the player.**

Model: each DS1 event (per Initialize Event slot, arguments substituted) becomes one ESD state group + EventParam row
(bootType 1 ownerless; bootType 2 + ownerType 1 when it must belong to an object, e.g. key doors). DS2 never runs
state 0's entry commands: state 0 → 1 unconditionally. DS1 sequential execution → a chain of states; DS1 condition
groups → DS2 group-condition commands (`140xxx(slot, …)`, read with `f140001(slot)`) or direct expressions (`f6(flag)`,
`f3()` state time, `f131653` object state) combined with `&&`/`||`; `IF … MAIN` = a waiting state; SKIP/END = branches;
"Restart" = back to state 1. Map-load "reproduce" checks (SKIP IF event flag (Event ID)) translate as-is.

Instruction rules (DS1 usage count over all maps; status):

| DS1 instruction | DS2 | Status |
|---|---|---|
| 2000:0 Initialize Event | one group per (event, slot), args substituted | P |
| 3:0 IF Event Flag / 1003:1 SKIP IF / 1003:2 END IF | `f6(flag) != 0` / `== 0` | P |
| 2003:2 Set Event Flag | `130101(flag, 0|1)` | P |
| 2003:22 Batch Set Event Flags | one 130101 per flag | P |
| 0:0 IF Condition Group, 1000:1/2/7/8 group state | expression composition | P |
| 1000:3/4 SKIP / END (End, Restart) | state branches | P |
| 1001:0/1 WAIT seconds/frames | `f3() > s` (frames/30) | P |
| 3:2 IF In/Outside Area (player) | EventLocation box from the DS1 region + `140132(slot, loc, loc, 1)`; outside: 0? | P (inside) / U (outside) |
| 3:5 / 3:18 IF Action Button | key-guide helper + `140205(slot, helper)` | P |
| 2003:18 Force Animation Playback (object) | `131636(instance, state)` with the object's anim→state table | P |
| 2005:7 Reproduce Object Animation | same, on load (state re-request) | P |
| 2005:3 (De)activate Object | `130702(instance, 0|1)` | P |
| 2006:1/2 Delete/Spawn Map SFX (fog) | part of the fog's white door show/hide | P |
| 2007:1 Display Generic Dialog | `130443(mapevent id, 3, 15, 220, 2, 0, 0)` with DS1's text | P |
| 2005:6 Set ObjAct State | key guide off/on `131638(helper, 1|0)`; doors: lock event | P |
| 2011:1 Activate Hit | `132153(hit group, 0|1)` (hit groups U) | U |
| 11:2 IF Player Standing On Hit | box over the collision's bounds + 140132; or `140401` hit group | P |
| 4:0 IF Character Dead/Alive, 4:2 HP ratio | generator-based checks (`140522` for bosses; others U) | U |
| 2004:5 Change Character Enable State, 2004:4 Force Death | generator commands (131721/131731…) U | U |
| 2003:11 boss HP bar, 2003:12 Handle Boss Defeat | BossBattleParam row + `132201/132202`, killed flag | U |
| 2004:41 Short Warp / 2002:x cutscenes | `132121` warp; cutscenes U | U |
| 2003:36 Award Items | `131402(lot, 1)` | P |
| 3:6 / 1003:5 multiplayer state | `140601` | U |
| 2000:2 network sync, 2000:5 save, 2003:28 achievement, 2009:4/6 MP buffs | dropped | P |
| 2009:3 Register Bonfire, 2009:0 Register Ladder | handled by the map builder | V |
| everything else (216 instructions in use, `data\scratch\emevd-stats.txt`) | reported by the translator as unsupported | U |

## 5. Characters (enemies, bosses, NPCs) — 1:1 pipeline (to build)

| DS1 | DS2 | Status |
|---|---|---|
| FLVER + DS1 skeleton | DS2 chr model on DS1's own skeleton (no DS2 template body): `Ds1RigModel` (3 DS2 top bones + DS1's bones; DS1 dummies; ragdoll from DS1's) | **V** (unarmed Asylum Hollow c2501, run s8-7) |
| Havok animations | Morpheme NSA clips (our encoder `Formats/Morpheme`, byte-identical on 1552 DS2 clips) in a DS2 network resized to the DS1 rig (`NmbRigReplace`; same bone count today, re-serialiser for others in `data/research/morpheme/net_resize.py`); clips by role (`Ds1RigAnims`: idle/locomotion re-cut at the left touchdown, Footsteps sync, foot-lock windows on the ball joint) | **V** idle/hit/death (s8-7) / P locomotion |
| TAE (events: attacks, hit windows, sfx, sounds) | DS2 TAE + NMB event tracks (TimeAct id, AttackDuration/HitDuration fractions) | U |
| animation network | NMB (rig, channel map, event tracks, state machine): start from a copied DS2 network, our own writer | U |
| Lua AI (battle goals, `script\mAA_BB.luabnd`, Lua 5.0 bytecode) | DS2 AI ESD (`ezstate/aiNNNNNN.esd`): decompile, translate goal logic | U |
| NpcParam / NpcThinkParam | EnemyParam / logic params; HP, souls, drops DS1-calibrated | V (HP/souls/drops) |
| hit boxes / damage (AtkParam, BehaviorParam) | DS2 attack params | U |

## 6. Items, text, sound (existing rules)

Weapons/shields/ammo/goods/keys: `WeaponPorter`, `AmmoPorter`, `GoodsPorter`, `item-map.tsv` (V). Text: DS1 FMG → DS2
FMG (V). Sound: U (FMOD banks). Loading screens: V. Travel menu: separate Lordran page wanted (user, session 7): U.

## 7. Session 9 additions (2026-10-05)

| Area | Rule | Status |
|---|---|---|
| ESD bytecode (all tools) | **Comparison opcodes: 0x91 <, 0x92 >, 0x93 <=, 0x94 >=** (DS2 PTR_FUN_1415b2920, DSR the same; right operand popped first). Our tables had <=, >=, <, > → the talk translator's hit test "> 0" ran as ">= 0" (always true: every NPC looped its hit reaction, Oscar rebuilt his prompt 14×/frame). Fixed in TalkExpr/EsdUsage/TalkSim; **AI emitters (enemies EsdExpr, boss Ds2AiEsd) fixed in chrint** | V (s9-12) |
| ESD runtime | DS2 runs Update(100) per frame and stops at the first iteration without a transition (FUN_1409859c0); a cycle of N instantaneous states runs ⌊100/N⌋ times per frame | V-code |
| Event groups | **G2**: DS2 records condition results per registering state per run and replays them on a revisit; DS1 "Restart" (END type 1, restart branches) → state `130002()` (re-execute), never a back edge into a registering state. `event-sim` models it | V (s9-13: nest prompt re-arms) |
| Talk | DS1 f5 attacked / f9 hit → `f130311(1) > 0` (0x92) + 130351 reset in an ack state's entry; f3 dead → `f130301() < 1`; death guards run DS1's death event once | V (s9-12) |
| Talk | DS1 voices (`frpg_smAA` projects, events renamed v%09d → v{AABB}%08d) are **on by default** (`--no-ds1-voices`): without a speech bank DS2 skips/stalls voiced rows | V (s9-12) |
| Talk | DS1's OpenGenericDialog default button (5th arg) has no DS2 counterpart (130441): DS2 always pre-selects NO | V (s9-12) |
| Items | **G1 (user decision)**: DS1 Estus Flask gifts/drops (goods 200-215) → **DS2 Estus Flask Shard 60525000**, once per DS1 gift flag | V (s9-17) |
| Human NPCs | DS2 makes a model-1 NPC human (StartUp component, startUpStateType idles) **only if EnemyParam.animId == 100** (FUN_1403560a0); any other animId = generic NPC stuck in BasicMove/StaggerIdle. DS1 idles for human NPCs need a route that keeps animId 100 (I1c edits the global c0001 network: user decision pending) | V-code + s9-12/17 |
| Morpheme | **I1b**: DS2 shares NMB objects by asset id (FUN_1404e0f40, first loaded wins): every converted bundle with new content needs its own deterministic id/GUID; `nmb-ids` checks | V-code (in chrint) |
| Morpheme | **E2b**: category clones must be self-contained: copy stage-shared additive layers (FilterTransforms banking) whose def parent would lie in another band's SM; invariant N1 (def parent is an ancestor-or-self of every user) holds on 173/173 vanilla networks; `nmb-runtime-check` (30 checks). Violation = crash in FUN_1405f8ed0 on attack (s9-6/7) | V-code (fix in game: pending) |
| Objects | Skinned (dynamic) object meshes: face-set **Unk06 = highest palette slot + 1** (DS2 uploads only that many bone matrices; Unk06 1 skinned 12148/12205 crow vertices with stale matrices = black stick) | V (s9-13: crow visible) |
| Cutscene objects | **R3c**: the object FLVER node tree = its skeleton tree, root node named like the model (DS2 copies demo tracks as local transforms by node name); GDParam isPrefab only with a pfbbin | V (s9-13) |
| Map effects | **B1d** (default): DS2 area effect at its own height, ⌈3·W1/W2⌉ per axis on DS1's footprint, copies < 50 % inside dropped (Firelink 42 × 3011) | V (s9-14: mist visible) |
| Map effects | **B2b-id**: converted DS1 effects get DS2 ids 20000 + free family (DS2 aborts on any title-bank effect id ≥ 30000: std::bitset<30000> in FUN_140bf7960). B2b (DS1 100201 → 22955) loads, draws nothing yet | V (no crash, s9-15) / U (look) |
| Sky | DXBC `exp` = 2^x: DS1 transmittance T = 2^(−τ); **S3d** no second DS2 zone fog on pieces that bake DS1's part fog; **A7c** tone match (user to judge) | P (look s9-14) |
| Atmosphere | mist B1b/B1c (DS1 height, 1:1 counts) too faint; superseded by B1d | rejected |
