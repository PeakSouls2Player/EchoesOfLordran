# DS1 content census: everything in Lordran, in progression order

The porting checklist for the mass migration (session 8, 2026-10-03). Every enemy part, NPC, item, object and scripted
event in DSR's maps, area by area. It was written from the DSR files (`msb1-dump`, `emevd-dump`, `dump-param dsr`, a
SoulsFormats MSB reader for idle animations/patrols/MSB events, decoded talk ESDs and FMGs) and checked against wiki
knowledge. Where the data and the wiki disagree, the data wins. Anything not confirmed is marked "(unverified)".

## Files

| File | Areas | DS1 maps |
|---|---|---|
| [00-progression-npcs-global.md](00-progression-npcs-global.md) | **Start here.** Map table, critical path + dependency graph, all bosses in order (souls, drops, flags, world changes), every NPC questline end to end, covenants, world-state changes, global mechanics (humanity, kindling, Estus, curse, invasions, warp list) | all + common.emevd |
| [01-asylum-firelink.md](01-asylum-firelink.md) | Opening, Northern Undead Asylum (first visit + return trip), Firelink Shrine, Firelink Altar | m18_01, m10_02, m18_00 (altar) |
| [02-burg-parish-depths.md](02-burg-parish-depths.md) | Undead Burg, Lower Undead Burg, Undead Parish, The Depths | m10_01, m10_00 |
| [03-darkroot-blighttown.md](03-darkroot-blighttown.md) | Darkroot Garden, Darkroot Basin, Blighttown, Quelaag's Domain | **m12_00_00_01** (see below), m14_00 |
| [04-sens-anorlondo-painted.md](04-sens-anorlondo-painted.md) | Sen's Fortress, Anor Londo (incl. darkened state, Gwyndolin), Painted World of Ariamis | m15_00, m15_01, m11_00 |
| [05-newlondo-catacombs-tomb.md](05-newlondo-catacombs-tomb.md) | New Londo (flooded + drained), Valley of Drakes, Darkroot Basin bonfire/elevator, The Abyss, Catacombs, Tomb of the Giants | m16_00, m13_00, m13_01 |
| [06-izalith-ashlake.md](06-izalith-ashlake.md) | Demon Ruins, Lost Izalith, The Great Hollow, Ash Lake | m14_01, m13_02 |
| [07-archives-kiln.md](07-archives-kiln.md) | Duke's Archives, Archive Tower prison, Crystal Cave, Firelink Altar, Kiln of the First Flame, endings | m17_00, m18_00 |
| [08-oolacile-dlc.md](08-oolacile-dlc.md) | Artorias of the Abyss: Sanctuary Garden, Oolacile Sanctuary, Royal Wood, Township, Chasm of the Abyss, Battle of Stoicism arena | m12_01 |

Every area file follows the same structure: header (entrances, prerequisites) → route walkthrough → bonfires → enemies
(every MSB part, model id, part-name ranges, idle InitAnimID, patrols) → bosses (fogs, trigger events, anim ids,
rewards, world change) → NPCs (entity, talk id, idle pose, quest steps) → items → interactive objects & EMEVD events →
porting notes → **area completion checklist**.

## Progression order (short form; details in 00 §1)

Asylum (Asylum Demon) → Firelink → Undead Burg (Taurus Demon) → Undead Parish (Bell Gargoyles, **Bell 1**) →
Lower Burg (Capra Demon → Key to the Depths) → Depths (Gaping Dragon → Blighttown Key) → Blighttown → Quelaag's Domain
(Quelaag, **Bell 2**) → Frampt opens Sen's Fortress (Iron Golem) → Anor Londo (Ornstein & Smough → Gwynevere gives the
**Lordvessel**) → place the Lordvessel at the Firelink Altar → the four lord souls in any order:
New Londo/Abyss (Four Kings; drain needs Ingward's Key to the Seal), Duke's Archives/Crystal Cave (Seath),
Catacombs → Tomb of the Giants (Nito), Demon Ruins → Lost Izalith (Bed of Chaos) → fill the Lordvessel → Kiln of the
First Flame (Gwyn) → ending (Link the Fire / Dark Lord) → NG+.
Optional: Darkroot (Moonlight Butterfly, Sif), Painted World (Priscilla), Gwyndolin, Great Hollow/Ash Lake, Stray Demon,
DLC (via Darkroot Basin after Dusk + the Broken Pendant).

## Things the data showed that differ from common knowledge (port from the data)

- **Darkroot is `m12_00_00_01.msb`** in DSR (the DLC variant); only it has the Oolacile portal trigger and Sif's
  alternate post-DLC intro. Build Darkroot from `_01`.
- **`m16_00` holds more than New Londo**: Valley of Drakes, the Darkroot Basin bonfire and the Basin→Valley elevator.
- The **Firelink Altar is in `m18_00`** (the Kiln map). The Lordvessel is filled from a menu option at the altar bonfire.
- **Broken Pendant**: Crystal Golem 1700510 in the Duke's Archives garden (event 11700700, DLC owned + Dusk freed and
  talked to), not a Darkroot golem.
- **Maneater Mildred** invades in Blighttown (m14_00); the Depths invader is Kirk.
- Wrong community param labels: NpcParam 6130/6131 "Griggs" = Laurentius, 6010 = Darkmoon Knightess; ShopLineup
  5000-5022 = Logan, 2100-2102 = Rickert; ItemLot 2670 (Orange Charred Ring) is the Centipede Demon's.
- Mechanics that are built differently from how they look: Four Kings = an invisible 4320-HP pool + a spawner adding a
  King every 40 s; the Catacombs bridges start spikes-up (Patches scripts the lever); New Londo's drain swaps two complete
  collision sets + 11 map pieces; Gwyndolin's corridor is teleport-based; the Hellkite gives souls only if killed while landed.
- Dead or cut content to **not** port: 18 Burg/Depths corpses with missing ItemLot rows, a Blighttown cut boss + props,
  a New Londo cut boss (e1600810) and Darkwraith key mini-boss, Nito's unused skeleton release, Black World Tendency
  spawns (never active in DSR), the Titanite Demon kill-flag typo in Izalith (1410150 vs entity 1310150: the reason it respawns,
  so port the typo for 1:1 behaviour).
- `docs/asylum-acceptance.md` row 4 corrected: the courtyard ladder is o8500 (1811140); o8501 (1811141) is the Stray Demon pit ladder.

## Tool gaps found (fix in Lordran.Tools before the mass migration)

- `msb1-dump` lacks enemy **InitAnimID / DamageAnimID / MovePointNames / PlatoonID**, the MSB **events** section
  (spawn points, messages, SFX, sounds, PseudoMultiplayer, generators) and multi-row item lots (prints only the first lot).
  The agents used throwaway SoulsFormats readers in the session scratchpad.
- `ds1-text` searches every FMG and returns the wrong string when ids collide (tutorial messages, boss names, event text).
  It needs an FMG/category argument.
- `param-grep`, `find-row` and `emevd-grep` crash on missing or misordered arguments.
- No tool extracts DS1 talk ESDs (`script\talk\*.talkesdbnd.dcx`), which the NPC questlines need.
