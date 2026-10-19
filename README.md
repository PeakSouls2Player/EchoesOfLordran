# Echoes of Lordran

Dark Souls (Lordran) rebuilt inside **DARK SOULS II: Scholar of the First Sin**, as if it were a DS2 DLC: DS1's maps,
enemies, bosses, NPCs, items, events, music and atmosphere, translated to run on DS2's engine and rules in one game and
one save. A separate Lordran page in the bonfire Travel menu (LB/RB) takes you between Drangleic and Lordran.

**Status: work in progress.** Playable so far: Northern Undead Asylum, Firelink Shrine, Undead Burg / Parish, the Depths
and Blighttown, with DS1 enemies and bosses (1:1 DS1 skeletons, animations and AI), items, armor, spells, covenants,
gestures, music and sounds. Many areas are still to come; see `docs/PLAN.md`.

## This repository contains no game files

Everything here is our own code and documentation. No FromSoftware assets (models, textures, maps, sounds, params,
text) are included or may be added. The tools build the mod's content pack from **your own legally owned copies** of:

- DARK SOULS: REMASTERED (the DS1 source data)
- DARK SOULS II: Scholar of the First Sin (Steam)

Play **offline only** with the mod installed. Never take a modded game online.

## Layout

| Path | What it is |
|---|---|
| `src/Lordran.Tools` | C# (.NET 10) command-line tools: the DS1â†’DS2 translators and `build-pack`, plus offline checks |
| `src/loader` | `dinput8.dll` loader (C, MinHook): serves the content pack to the game, Travel/Gesture menu pages, entry item |
| `src/*.ps1`, `src/lr.cmd` | test and helper scripts (pack verification gate, hot-swap, test launches) |
| `docs/translation-rules.md` | the rulebook: every DS1â†’DS2 translation rule and its status |
| `docs/ds2-re-notes.md`, `docs/travel-page-re.md` | DS2 engine reverse-engineering notes |
| `docs/ds1-content/` | DS1 content census per area (enemies, NPCs, items, events) |

## Building

Quick start: run `powershell -ExecutionPolicy Bypass -File setup.ps1` from the repo root. It fetches the open-source
dependencies, lists what you still need, and builds everything. See `CONTRIBUTING.md` before sending changes.

Paths in the scripts and tools assume the workspace lives at `C:\EchoesOfLordran` (clone it there, or search-replace
that root). Expected workspace layout:

```
C:\EchoesOfLordran\
  src\                      this repo's src
  tools\dotnet\             .NET 10 SDK (or use one on PATH)
  tools\SoulsFormatsNEXT\   git clone https://github.com/soulsmods/SoulsFormatsNEXT
  tools\minhook\            git clone https://github.com/TsudaKageyu/minhook
  tools\llvm-mingw-*\       llvm-mingw (x86_64, ucrt) for the loader
  tools\smithbox\           Smithbox (its Assets\PARAM definitions and Pfim.dll are used)
  tools\uxm\                UXM Selective Unpack (to unpack DS2's archives)
  data\                     generated: unpacked game data, the built pack (never commit)
```

1. Unpack DS2 with UXM and point the tools at your DSR install (see `src/Lordran.Tools/Commands/ExtractDs2.cs`).
2. Build the tools: `tools\dotnet\dotnet build src\Lordran.Tools -c Release`
3. Build the loader: `src\loader\build.bat`
4. Build the pack:

```
src\lr.cmd build-pack --weapons all --ammo all --armor all --spells all --goods all --rings all ^
  --map asylum --map burg --map depths --map blighttown --burg-chrs --entry firelink
```

Developer options: `--test-shop`, `--dev-kit` (an item that grants every ported item), `--debug-markers`.

5. Verify offline: `powershell -File src\verify-pack.ps1` (the gate every change must pass).
6. Install: copy `dinput8.dll` next to `DarkSoulsII.exe` and the pack to `<game>\lordran\pack` (or set
   `pack=` in `<game>\lordran\lordran.ini`).

## Contributing

Work in a sandbox copy, change the rule (not one-off data), add or extend an offline check that proves it, and run the
verify-pack gate before sending a pull request. Read `docs/translation-rules.md` first: DS1 content must match DS1
exactly while following DS2's systems. Pull requests must not contain any game files.

## Credits

- [SoulsFormatsNEXT](https://github.com/soulsmods/SoulsFormatsNEXT) â€” FromSoftware file formats
- [MinHook](https://github.com/TsudaKageyu/minhook) â€” function hooking in the loader
- Smithbox (param definitions) and UXM (archive unpacking)
- FromSoftware for Dark Souls and Dark Souls II. This is an unofficial fan project, not affiliated with or endorsed by
  FromSoftware or Bandai Namco.

## License

MIT for the code and documentation in this repository (see `LICENSE`). It grants nothing for FromSoftware's games or
assets.
