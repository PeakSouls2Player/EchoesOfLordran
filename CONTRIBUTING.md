# Contributing to Echoes of Lordran

Thanks for helping bring Lordran into Drangleic. Please read this before opening a pull request.

## Ground rules

- **No game files, ever.** Don't commit anything extracted from DARK SOULS: REMASTERED or DARK SOULS II (models,
  textures, maps, sounds, params, text dumps, decompiled code), and no saves. PRs containing them will be closed.
- **DS1 content matches DS1; systems follow DS2.** Enemies, bosses, items and events should behave exactly as in DS1,
  running on DS2's own mechanics (upgrades, scaling, bonfires, covenants, menus). See `docs/translation-rules.md`.
- **Fix the rule, not the instance.** A translation bug usually affects every map or enemy. Change the translator so all
  of them get the fix, and record the rule in `docs/translation-rules.md`.
- **Prove it offline.** Add or extend a check in `Lordran.Tools` (the `*-check` commands) that fails before your change
  and passes after it, and add it to `src/verify-pack.ps1` if it has a pass/fail result.
- **Play offline only** when testing. Never take a modded game online.

## Getting set up

1. Clone the repo to `C:\EchoesOfLordran`.
2. Run `powershell -ExecutionPolicy Bypass -File setup.ps1`. It clones SoulsFormatsNEXT and MinHook, tells you what else
   to install (.NET 10 SDK, llvm-mingw, Smithbox, UXM, your DS2 and DSR installs), and builds the tools and the loader.
3. Unpack DS2 and build the pack (see `README.md`).

## Workflow

1. Open an Issue first for anything big, so work isn't duplicated.
2. Make your change on a branch in your fork.
3. Build the pack and run the gate: `powershell -File src\verify-pack.ps1` must report 0 failed.
4. If you tested in game, say what you saw (screenshots help).
5. Open a pull request describing the problem, the rule you changed, and the check that proves it.

## Where things live

- `src/Lordran.Tools/Build/` – the translators (maps, characters, AI, events, talk, items)
- `src/Lordran.Tools/Commands/` – CLI commands and offline checks
- `src/Lordran.Tools/Mappings/` – DS1↔DS2 mapping tables
- `src/loader/` – the dinput8.dll loader (archive redirect, Travel and Gesture pages)
- `docs/ds2-re-notes.md` – what we know about DS2's engine
- `docs/ds1-content/` – what each DS1 area contains, for porting checklists
