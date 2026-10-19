# DS2 SOTFS reverse-engineering notes

Build: `DarkSoulsII.exe` v1.0.3.0, 28,200,992 bytes, SHA-256 `0045931B89145045…` (pristine copy in `bin\ds2s\`).
Addresses are for that build at its preferred base 0x140000000 (from the Steamless-unpacked copy, so they match the running game; ASLR is on, so rebase at runtime).
Decompile: `decomp\ds2s\DarkSoulsII.c` (Ghidra 12.1.4); Ghidra project `ghidra\projects\ds2s`.

## Virtual file system

| Address | Role (inferred) |
|---|---|
| `FUN_1402ef410` | Mount setup at boot. Retail branch mounts the archives; debug branch maps `title` → `N:/FRPG2_64/data/target/win64` (loose files) |
| `FUN_140aeed50(ctx)` | Returns nonzero in retail/archive mode (selects the branch above) |
| `FUN_140b0bd50(dev, name, bhd, pem)` | Mounts an encrypted BHD/BDT archive as device `name` (`gamedata`, `chrlq`, `partslq`, `objlq`, `maplq`) |
| `FUN_140859590(alias, root)` | Sets a path alias (`ezstate` → `gamedata:/`, `menu` → `gamedata:/menu`, `param` → `title:/param`, …) |
| `FUN_140859600(alias, root)` | Appends a fallback root to an alias (e.g. `icon` → `dlc_data:/menu`, then `gamedata:/menu`), so aliases are search lists |
| `FUN_140859670(1)` | Called after the patch aliases are set up in retail mode (purpose TBD) |

### File devices (from RTTI, read with `tools\rtti.py`)

| Class | vftable | Slot 1 = Open |
|---|---|---|
| `DLIO::DLFileDevice` (base) | `0x14114fcb8` | pure virtual |
| `DLEBL::DLEncryptedBinderLightFileDevice` (encrypted BHD/BDT archives = `gamedata`, `*lq`) | `0x1412928e8` | `0x140fef510` |
| `DLIOD::msvc90_windows::MicrosoftDiskFileDevice` (loose files) | `0x14114fdd8` | `0x1408e80f0` |
| `DLIO::DLBinder4FileDevice` / `KatanaBinder4FileDevice` / `BndMultiMountDevice` | `0x141156cf0` / `0x1411dcbe0` / `0x1411dcb98` | — |

- `Open(device, DLString* path, a3, a4, DLAllocator* alloc, bool flag)` returns a new file operator (archive: 0x70-byte object, vftable `0x141293168`; disk: 0xd8-byte object via `FUN_140907a20`). Disk Open first calls `FUN_140906f00(device, path)` and returns NULL if the file doesn't exist.
- `FUN_140fef650(archive, path)` is the BHD5 lookup. It skips past `:`, lowercases, maps `\`→`/`, hashes `h = h*37 + c`, and walks the bucket (32-byte entries: hash, size @+4, offset @+8, SHA @+0x10, AES @+0x18). It returns NULL when the file isn't in the archive.
- `DLString` = MSVC wstring: 8-wchar small buffer or heap ptr @0, size @0x10, capacity @0x18 (≥8 → heap), allocator @0x20.
- `.rdata` (vtables) isn't SteamStub-encrypted, so vtable slots can be swapped in DllMain before any game code runs.

Many loads use device paths directly (e.g. `L"gamedata:/model/chr/c%04d.texbnd"`, `L"gamedata:/morpheme4/chr/c%04d.%s"`), so aliases alone can't redirect everything. The loader must hook the virtual-path → device resolution, the way Mod Engine does for later games. Next RE target: the function that expands `alias:/path` / `device:/path` and opens the file.

## Data access without UXM
- `extract-ds2` (Lordran.Tools) RSA-decrypts each `.bhd` with its `*KeyCode.pem`, reads it with SoulsFormats BHD5 (DarkSouls2), and names files through UXM's `ScholarDictionary.txt`. All 16,289 files are named; GameDataEbl extracts to 11,699 files / 12.5 GB in `data\ds2`.
- `enc_regulation.bnd.dcx` is decrypted by Smithbox's `Andre.SoulsFormats.DecryptDS2Regulation`, loaded by reflection (temporary). It holds 228 params; 199 parse with Smithbox's DS2S paramdefs.

## Regulation (`title:/enc_regulation.bnd.dcx`), solved 2026-10-02 session 2
Load path:
- `FUN_1402de2c0` (resource setup, retail mode) creates the regulation resource via `FUN_1402dce30(mgr, FUN_14048cb30() = L"title:/enc_regulation.bnd.dcx", ...)`. The object is built by `FUN_1402df4c0`, vftable `0x1410d9a08`.
- Vtable slot 16 = `FUN_1402df8f0(self, data, size)` calls `FUN_1402e0470(self, data, size, param_4=1, encrypted=1, param_6=1)`. Afterwards the decoded BND4 is at `self+0xd8` and its size at `self+0xd4`. `DAT_141614038` is the parsed binder.
- `FUN_1402e0470` with `encrypted=1` calls `FUN_140261150(key, data, size, &out)` (key passed as two qwords `0x54940adf30811740`, `0x4c25bfec71e10933` = `40178130DF0A94543309E171ECBF254C`). **Only if it returns true** does it continue with the plaintext. Otherwise it parses the ciphertext and fails silently.
- With `param_4=1` it decodes through `DLCM::DLSimpleDecompressionUtility::AutoProfileDecStream` (vftable `0x14113e228`): `FUN_140877240` ctor, `FUN_140877840` init (vtable +0x68 → `FUN_140877f90`: "DCP\0" first → `DCP`/`DCS` layout, else `FUN_140877c00`: `DCX\0` header, version < 0x11001, `DCS`, `DCP` (algorithm), `DCA`). `FUN_140877950` decompresses (its return value is ignored). The size comes from the DCS uncompressed-size field (`FUN_140877b70`, big-endian). There are no version checks on this path.
- The `param_4=0` branch (`"algorithm_deflate_level_6"` decoder, BND4 version > `DAT_141614278` check, `+0x34 ≤ 0x67`) is the network-delivered regulation (`FUN_1400f00a0`, `FUN_1402dff30`), not the file.

Cipher = **AES-128-CWC** (Brian Gladman's `cwc.c`; context 0x168 bytes after a vtable `0x1410d1730`):
| Function | CWC role |
|---|---|
| `FUN_1402604b0` → `FUN_140260d90` | `cwc_init_and_key`: AES key schedule (`FUN_1406b9d10`), hash key Z = AES(C0 00..00) with bit 127 cleared, stored as big-endian words at ctx+0x124 |
| `FUN_140260e70` | `cwc_init_message`: counter block = 0x80 ‖ IV[0..10] ‖ 00000000 |
| `FUN_1402606f0` | `cwc_auth_header`: AAD = the full 16-byte IV (header[0..15]) |
| `FUN_140260d10` | `cwc_decrypt`: `FUN_140260510` (hash ciphertext) then `FUN_140260ad0` (CTR, counter starts at 1) |
| `FUN_140260ed0` / `FUN_140261080` | hash step: acc = (acc + Y) · Z mod 2^127−1 over 96-bit blocks; AAD and data each zero-padded to 12 bytes |
| `FUN_140260880` | `cwc_compute_tag`: acc += aad_len·2^64 + data_len (**bytes**, not bits), reduce, tag = AES(acc as BE 16 bytes) ⊕ AES(counter block with counter 0) |
| `FUN_1406babd0` | AES block encrypt |

DS2 quirk: each 32-bit word of a hash block is loaded natively (**little-endian**) and not byte-swapped as the CWC spec requires. `FUN_140261150` compares the computed tag with **header[16..31]** (`cmp` at `0x14026122c`/`0x140261233`). Our implementation: `Ds2RegulationCipher.ComputeTag`. It reproduces the vanilla tag `F2F83AE875F25103D85BB26BA7BB3769`.

## Archive file operator (`DLEBL::DLEncryptedBinderLightFileOperator`, vftable `0x141293168`, 40 slots, RTTI locator at `-8`)
- Built by the archive device's Open (`FUN_140fef510` → `FUN_140fef560`): always allocates 0x70 bytes, even for paths missing from the BHD (`FUN_140ff0e50` sets `+0x60 = -1`). Found files use `FUN_140ff0eb0`.
- Fields: `+0x08` allocator, `+0x10` last error (`FUN_1408969a0`), `+0x28` device (`FUN_140896d80`), `+0x60` data offset in the .bdt or -1, `+0x68` size (u32), `+0x6c` position (u32).
- Slots: 0 dtor `0x140ff0f30` (this, flags) · 7 Clone `0x140ff1480` (copy ctor `FUN_140ff0da0`, resets the vtable to the original) · 9/12/18 IsOpen (`+0x60 != -1`) · 10 returns false · 13 Size · 14 Remaining · 17 EOF · 23 Seek `0x140ff14d0` (whence 0/1/2) · 24 Tell · **25 Read `0x140ff1320`** (this, buf, len): -1 at EOF, 0 for len 0, otherwise reads via `FUN_140fef980(device, offset+pos, buf, n, &err)` and advances. Most other slots (writes, etc.) assert "encrypted binder light error".
- The archive operator is synchronous and simple. Substituting a `MicrosoftDiskFileOperator` (vftable `0x1411555e8`, which supports async I/O) for it makes the boot sequence wait forever (black screen). Loader v2.4 keeps the archive operator and swaps in a vtable copy with Read/Clone/dtor that serve the pack file.

## Item special effects = EMEVD scripts in the regulation (decoded session 2)
`SpEffectWeapon.emevd`, `SpEffectArmor`, `SpEffectRing`, `SpEffectSpell`, `SpEffectActiveItem`, `SpEffectPassiveItem`, `SpEffectAbnormalState`, `SpEffectEnemy`, `SpEffectCondition`, `SpEffectMustSub` and `SpEffectWideUse` sit in `enc_regulation.bnd.dcx` next to the params. They are EMEVD (`EVD\0`, version 0xCC, 64-bit; SoulsFormats reads them as `Format.Bloodborne` and rewrites them byte-identically). An item's effect is the event whose id = `ItemParam.specialEffectId` (= the item id). Tools: `lr.cmd probe-speffect NAME` (events, instructions, histogram) and `lr.cmd regulation-file NAME OUT`.

Opcodes decoded from items that exist in both games (bank:id, little-endian args):
| Opcode | Args | Meaning | Evidence |
|---|---|---|---|
| 100090:4 | s32 0, s16 kind, s16 value | flat bonus. kind 0 phys / 1 magic / 2 lightning / 3 fire / 4 dark defense; 5 poison / 6 bleed / 8 curse / 9 petrify resistance. Same units as DS1 `*Diffence` | Ring of Steel Protection +50/75/100 (DS1 +50); Spell/Thunder/Flame/Dark Quartz kinds 1–4; Poisonbite/Bloodbite +15, Cursebite/Ash Knuckle +40; Dragon Tooth kinds 1+3 |
| 100090:1 | s32 0, f32 rate | stamina regen multiplier | Chloranthy ×1.15/1.20/1.25 |
| 100050:0 | s32 1, s32 sfx, amount, f32, s32 mode | HP restoration. mode 0: s32 HP every f32 s (Ring of Restoration 3 / 1.5 s, sfx 850). mode 1: on hit (Butcher's Knife: f32 8.0, 0.25, sfx 852) | — |
| 100130:2 | s32 icon, 0, 0 | buff icon while equipped (20 defense, 30 resistance, 10 stamina, 120 regen, 80 life protection) | rings |
| 100040:1 / :2 | s32 sfx, … | equip SFX | Chloranthy, Ring of Restoration |
| 1000:6 | 7 floats | bow range/projectile factors (Long Bow 1.0, Sea Bow 1.25) | bows |
| 1000:1 | s32 kind, 0, f32 | stat multiplier (e.g. kind 0x3B ×0.9 on Steel Protection = its drawback) | rings |
| 100080:1, 100090:2, 100130:4 | | timed buffs from weapon specials (Defender/Watcher Greatsword: 45 s) | — |
Still unknown: 1000:0/3/9/13/15/17/19, 100110, 100120, 100170, 100700 (see `probe-speffect` output).

## Movesets and special attacks
`WeaponParam.weaponActionCategoryId` → `WeaponActionCategoryParam` (one row per weapon in DS2) lists an attack id per action (`atkIdSingle1HandNormal1st`, …, `atkIdDual*`, `atkIdLeft*`; guard slots hold 1, ripostes and backstabs 21–23M). Ordinary attacks are 10xxxxxx and shared by every weapon of a class. Specials show up as 20xxxxxx ids (Moonlight Greatsword's strong attacks 2003xxxx), placeholders (Channeler's Trident 2H strong = 1), or a 10xxxxxx id no other moveset uses. This is the route for DS1 projectile R2s (Drake Sword, Dragon/Obsidian Greatsword) before DS1 animations exist.

## Shops, ammo, title screen
- Shop price = `ItemParam.price × ShopLineupParam.priceRate`. `ShopLineupParam.price` is not the price. An NPC's lineup is a contiguous id block (Maughlin 76100000+, Shalquoir 77700200+).
- Ammo: `ItemParam.arrowParamId` → `ArrowParam` (arrowType 1/2/4; menu attack per element) → `BulletParam` and `PlayerDamageParam` rows at id + 0…70. `damageType` 0 phys, 1 magic, 2 lightning, 3 fire, 4 dark, 6 poison.
- Title screen: `menu/17.febnd.dcx` (`l17_01_Title.flo` + TPFs). FLO: shape table of pixel rects (x0, y0, x1, y1 floats) around 0x2f98–0x3800; texture table at 0x21a68 (24-byte records: name ptr, 0, id, width<<16, height).
- Item id ranges by kind (id / 10M): weapons 1M–9M, shields 11M+ (we use 18M), armor 21M–27M, spells 31M–35M, rings 40M–42M, goods 50M–69M (ammo 60.76M–60.96M; we use 66M).

## Session 3 findings (maps, collision, input, bonfires)

### Map ids and map files
- A map id is an int `(AA<<24)|(BB<<16)|(CC<<8)|DD` ↔ `mAA_BB_CC_DD` (`FUN_1403bf390` formats it, `FUN_1403bf510` parses it). Path builders (`FUN_14048cef0` etc.) prefix `map:/` (= gamedata) or `dlc_data:/`; the DLC checks `FUN_1403bf6a0/6b0` return 0. Nothing hard-codes the set of maps on the file side. `gamedata:/Map/WorldMapList/WorldMapList.list` (loaded at boot, `FUN_1403bd190`) is the debug map list with Japanese names.
- **Files DS2 opens to load a map** (from `log_opens=1` while loading Majula; replace 10_04 with the map):
  - `map/mAA…/mAA….msb` (read twice) + `.ngp`
  - `model/map/e….envbnd.dcx`
  - `model/map/{g….gibhd/gibdt, h….hkxbhd/bdt, l….hkxbhd/bdt, m….mapbhd/bdt, t….tpfbhd/bdt}` (each opened 4×)
  - `model/map/t…_low.tpfbdt` (only the bdt)
  - `param/{EventLocation, EventParam, GeneratorLocation, GeneratorParam, GeneratorRegistParam, HitGroupParam, IntrudePointParam, MapObjectInstanceParam, MapTargetDirParam, NpcTalkParam, TreasureBoxParam}_mAA….param`. IntrudePointParam isn't in the archives, so missing files are tolerated.
  - `EzState/event_m….esd`, `EzState/talk_m….esd`, `Filter/filter_m….fltparam`, `Breakobj/m….breakobj`
  - `Menu/Text/<lang>/{talk,bloodmes}/m….fmg`
  - `sound/frpg2_mAABB.bnd`
  - Per character type in the map: `ezstate/aiNNNNNN.esd` (+`…10`), `TimeAct/chr/cNNNNNN_sfx.tae`, `sound/frpg2_cNNNN.bnd`.
- **Archives:**
  - `mapbhd` (BXF4) = `mAA….acb` + `mNNNN.flv.dcx` + `mNNNN.mte.dcx`. MTE is 8 bytes: `MTE\0` v2.
  - `h`/`l` hkxbhd also carry the same `.acb` as their first file.
  - `t…_low.tpfbdt` is **one BND4** (not BXF4) of `low_NAME.tpf.dcx` 128×128 copies; the `.tpfbhd` twin isn't opened.
  - LqMapEbl has half-resolution `model_lq/map/t…`/`g…` archives.
- **ACB** (SoulsFormats `ACB`): per-asset render config.
  - Model: draw distance, LOD rate, shadow flags, low-texture distance; "Members" are just the source model's material names.
  - Texture: `tex/NAME.tga`.
  - GITexture: `tex/hq_NAME.tga`.
  - General: `model_hkt/hNN_NNNN.hkt` per collision, `light/{atlasinfo.btao, hq_atlasinfo.btao, light.btof, pointcloud.btpo}`.
- **GI:** per piece, `mNNNN_gi_00.tpf` in the gibhd. The inner texture is `hq_mNNNN_gi_00` (HQ, 2048² DXT5) or `mNNNN_gi_00` (LQ, 1024²). RGB = indirect light (dark), **alpha = baked sun-visibility mask**. Some pieces use atlases (`…_atlasNNN`, `atlasinfo.btab`). The gibhd also holds `pointcloud.btpb` (light probes) and `light.btl`. Materials reference GI as `n:\temp\windows64\map\mAA…\tex\NAME.tga` in slot `g_GITexture` (MTD uv=3, shader index 6, the same slot as DS1's `g_Lightmap`).
- **Textures:** DXT1 diffuse/spec (format 0), DXT5 (5), normal maps **DXT5nm** (format 23: X in alpha, Y in green, R = B = 255). DS1 normal maps are BC5 (X = R, Y = G).
- **MSB2:**
  - Map pieces' and collisions' `DrawGroups`/`DispGroups` use all 4 words (vanilla pieces always set bit 3 of word 3); `FFFFFFFF,0,0,0` draws nothing.
  - Per-map events: Light ×2 (ids referenced from collisions), Fog ×3, BGColor, MapOffset (the map's world offset; Things Betwixt −498/30/−260 = its pieces' positions), Warp (type exists, unused in TB).
  - Regions: StartPoint (new-game start), Sound (BGM/ambience), SFX, EnvLight (one per `CubeEnv_000N` in the envbnd).
  - Object parts carry `MapObjectInstanceParamID`.

### Bonfires
- `MapObjectBonfireParam` (regulation; row = bonfire id, e.g. 2650 Things Betwixt, 4650 The Far Fire, 10655…): `mapId` (decimal AABBCCDD, 10040000), `menuAreaID` (→ `ic_area_<%03d%02d%04d>`), `eventCameraIdBegin`, `menuSortID`, `isUseReGenerateItem` (Ascetic), `isUseLevelUpEstusFlaskItem`, boss-kill counters.
- In a map: MSB object `o00_0100_NNNN` → `MapObjectInstanceParam_mAA…` row (e.g. `10040650`) with `objCtrlType` 7 and `objCtrlId` = bonfire id. Other objCtrlType values in m10_10: 2, 3, 4, 6, 10, 11, 15, 24, 25, 28, 30, **34** (×30), **43** (`10100640`, objCtrlId 101000; a warp candidate).
- Icons: `menu/tex/icon/bonfire_list/ic_list_<bonfireId:D9>.tpf` and `bonfire_area/ic_area_<…>.tpf`, both 256×128 DXT5 holding a ~180×100 picture with a torn alpha edge.

### Event ESDs
- DS2 ESDs: `LongFormat`, `DarkSoulsCount = 2`, `Name` = file stem, 4 hash-like ints at 0x70. State machines are attached to owners through `EventParam_mAA…` (`stateMapId` = group id, `ownerId`, `bonfireId`, `bootType`, `ownerType`).
- The `.edd` next to every ESD names the whole command/function API in Japanese (`lr.cmd edd-dump FILE > out.txt`, UTF-8). All event EDDs share one API (325 commands, 131 functions).
- **Warps:** only within an area: `132121` 【マップ】プレイヤーをエリア内でワープ, plus object warps `1316xx`. **No map-to-map warp command**, so cross-map travel (bonfires, memories) is data/object driven.

### Collision (DS2 = DSR classes)
- Packfile v11 `hk_2014.1.0-r1`, 64-bit, with a `__types__` section (full hkClass reflection). Object chain: `hkRootLevelContainer` → `hkpPhysicsData` → `hkpPhysicsSystem` → `hkpRigidBody` (name = model) → `hkpMoppBvTreeShape` (+`hkpMoppCode`) → `CustomParamStorageExtendedMeshShape`.
  - The mesh has one `hkpStorageExtendedMeshShapeMeshSubpartStorage` per hit material (float4 vertices, u16 indices ×4 per triangle).
  - Each subpart has a `CustomMeshParameter` (version 0x9100, `primitiveDataBuffer = [0, MaterialParam id, 0, 11]`).
  - MOPP code info: offset = AABB min, w = 0xFE0000 / max extent (24-bit quantisation).
- DSR (2015 tagfile) uses the same classes. Its material is in `CustomMeshParameter.materialNameData` (DS1 `HitMtrlParam` id + 100 × variant) with a per-vertex byte buffer.
- DS2 `MaterialParam` ids: 1 none, 2 iron, 3 heavy iron, 4 tree, 5 water, 6 swamp, 7 wood, 8 raw meat, 12 bone, 19 stone, 20 soil, 21 carpet, 22 grass, 23 gravel, 26 ash, 27 poison swamp, 30 lava.

### Input
- XInput: `FUN_140ef6300` enumerates pads 0–3. Pad objects poll `XInputGetState(+0x19c)` only once paired with a DirectInput device, otherwise they use DirectInput (`GetDeviceState(0x50)`).
- Keyboard: `IDirectInputDevice8::GetDeviceState(0x100, obj+0xf0)` in `FUN_140f06dd0`. On `DIERR_INPUTLOST` it unacquires and the device is recreated by `FUN_140f06ca0`.
- Cooperative level: 6 or 10, plus 0x10 (NOWINKEY), chosen by config strings `Ext.UserInput.CooperativeLevel.SetForeGround.{Pad,Keyboard,Mouse}`. The input manager is at `DAT_141875278`: +0x16d = window active (set in `FUN_140ef6580`), +0x16e = the pad foreground setting.

### Player
- PlayerCtrl = `[[exe+0x16148F0]+0xD0]`: 3×3 rotation rows at +0x60/+0x70/+0x80, position at **+0x90** (float3, w = 1), a copy at +0xA0. `DAT_1416148f0` is the game manager (+0x30 resource manager, +0x38 a map-id lookup).
- The facing (forward) vector is the third rotation row (+0x80). Movement keys are camera-relative.

## Session 4 findings (2026-10-02/03)

### Keyboard config
- `title:/param/KeyConfigParam.param` (Game\Param): 2 rows (keyboard, mouse) of byte pairs (key, modifier). The reader is around decomp line ~579700.
- **Key code = DIK + 0x45.** Arrows use a compacted code (UP 0xBE, LEFT 0xC0, RIGHT 0xC1, DOWN 0xC3). Modifiers: 0x6F LSHIFT, 0x62 LCONTROL, 0x7D LALT.
- Slot → pad action id (0x0C A, 0x0D B, 0x0E X, 0x0F Y, 0x04 Start, 0x05 Back, 0x00-0x03 d-pad):
  - W/S/D/A move; I/K/L/J camera; LALT walk; SPACE B; **E X (use item)**; **RETURN A (interact/confirm)**; N Y; ESCAPE Start; T Back;
  - menus: arrows, RETURN confirm, BACKSPACE back, **Shift+Left / Shift+Right = menu LB/RB** (actions 0x25/0x26).

### Bonfires in the save, and the map registry
- **Save:** the world data of slot n is `USER_DATA0(10+n)` (`lr.cmd save-unpack`; AES-128-CBC with SoulsFormats' Scholar key; entry = MD5(IV+ciphertext) + IV + ciphertext). At **+0x282CC** it holds:
  - `u16 bonfireIds[256]` (sorted, zero-terminated),
  - then `u8 level[256]` at **+0x284CC** (0 unlit, n = intensity),
  - and at **+0x28ECE** the last rested bonfire (`u8 BB, u8 AA, u16 id`).
- **On load** DS2 rebuilds the id list from `MapObjectBonfireParam` and **keeps a bonfire only if its map is registered**; levels follow by id. Unregistered: vanilla rows 650/655 (m90) and 62650-62653 (m20_62).
- **The registry is `gamedata:/Map/WorldMapList/WorldMapList.list`** (Shift-JIS, `mAA_BB_CC_DD<TAB>#comment` per line, CRLF). It lists every real map including the DLC maps m50_35-38. `AreaParam` is not the filter (it has m20_62 and m90 rows).
- **A new map also wants** `AreaParam` (camera, lock-on, water material, chameleon), `NetworkAreaParam` and `MapObjectTorchParam` rows keyed by map id.
- **Travel menu text and pictures:**
  - `mapname.fmg[menuAreaID]` = area title (also shown in the character list);
  - `bonfirename.fmg[bonfireId]`;
  - `menu/tex/icon/bonfire_area/ic_area_<menuAreaID:D9>.tpf` and `bonfire_list/ic_list_<bonfireId:D9>.tpf` (inner texture named like the file, upper case).
  - Arrival also asks for `menu/tex/Icon/MapName/English/map_name_<AABBCC>.tpf`.
- **DS2 bonfire ids** are `BB*1000 + 650 + 5k`.
- A bonfire whose `mapId` is not the current map cannot be rested at ("Cannot use bonfire"). Travel to the bonfire you rest at reloads its map.
- **Per-bonfire event:** `EventParam` row `80000 + 1000k` (`bonfireId`, bootType 4) runs a group that calls `130131(AABB*1000 + 100k, +99, 0)`: set every flag in the range, the reset when resting.

### Map objects
- **Object loading:**
  - `FUN_1403ce060` walks the MSB model list. For object models it creates resource entries (`FUN_1403d76d0`; index = model index) and preloads one if `FUN_1403c1c60(objId)` has flag 2.
  - `FUN_1403ce470` walks object parts and creates instances (`FUN_1403e6390` / `FUN_1403e6e50`).
  - A model loads (`FUN_1404904d0` → `obj:/model/obj/%s.bnd`) only while its entry mask `+0x10 & 0x1FFFF` is set, i.e. while an instance is active.
- **Activation = draw groups:** `FUN_1403cdc90` compares each object part's group words (`part+0x54..0x60`) with the map's active mask (`[this+8]+0x30..0x3C`). That mask comes from the collision under the player: an MSB2 part's **`Unk44..Unk50` words are a third group set (area bits)**. TB's collisions use bit 0 + one unique bit each (h02_0700 = 0x81), and TB's bonfire has DrawGroups 0x980. Objects whose DrawGroups miss every active bit never show. Many vanilla objects use `FFFFFFFF`×4.
- **Object archives** `model/obj/oAA_BBBB.bnd` (BND4):
  - normally self-contained: FLVER, MTE, `_h.hkx`, raw `NAME.tpf` textures, and an ACB with Texture assets;
  - exception: the memory tree `o00_9070` has none (it uses FoFG's map textures).
  - `MapObjectParam` and `MapObjectGDParam` are keyed by the **model number** (9070 = "Giant Tree (for warp)").
  - `MapObjectInstanceParam.objType` → `MapObjectInstanceTypeParam` (1607 general/unbreakable, 100 bonfire).
  - Prefabs `PrefabEditor/oAA_BBBB.pfbbin` are optional.

### Map-to-map warps (Memories of the Giants)
- **`132125` "warp the player via polydrama"(demo, 0, mapId decimal AABBCCDD, EventLocation id in the target map, 0|1)** loads another map and places the player at that location. Demo 0 = plain warp. No other event command takes a map id.
- **FoFG's memory entrances:**
  - objects `o00_9070` (instances 10101510/20/30, objType 1607); `EventParam` 37000/37010/37020 (bootType 2, ownerType 1, owner = the instance) → routine `2147483551(object, demo, 0, map, location, flag)`;
  - inside it: `131652` (key guide on), `140205` examined + `140601` multiplayer + `140801` item check (Ashen Mist Heart 50910000);
  - then `131638` (key guide off), flags 105300-105302, `130613(1)`, `132125(demo, 0, 20100000, 3700000/3701000/3702000, 0)`.
  - Demos: `DemoParam` 101050 Vammar, 101060 Orro, 101070 Jeigh, 101420 Dragon Memories, 503610 Old Iron King.
- **Seamless neighbour links** (not warps):
  - `EventLocation` rows with `categoryType 23` and `categoryId` = the neighbouring map id;
  - MSB2 `Warp` events named 「シームレスワープ【area】」 are the arrival points.
  - Use these to join DS1 maps to each other later.
- **Coordinates:** per-map params (`GeneratorLocation`, `EventLocation`) are **map-local** = MSB/world position minus the MSB `MapOffset` event (TB: (-498, 30, -260)). In-game positions equal MSB positions.

### Event and talk script commands (names from the EDDs)
- **Interaction:**
  - `140141(group, chr)` character examined (talk);
  - `140132(group, locA, locB, 1|0)` player inside or outside an `EventLocation` region (TB: `(0, 500000, 500000, 1)`);
  - `140205(group, obj)` OBJ examined; `140151` player event action.
  - Group conditions are read with `f140001(n)` = bytecode `82 E1220200 4n 85 A6 A1`.
- **Windows:**
  - `130441(mapevent msg, 4, 220, 2, itemId, 0)` YES/NO (`mapevent.fmg`, e.g. 1002 "Use %s?"); `130443` YES-only.
  - Result bytecode: window gone `8276FD010084A64096A1`, cancelled `8275FD010084A743954096A1`, NO `AF42954096A1`, YES `AF41954096A1`.
- **Key guide:** `130408(34, n)` creates a key-guide region on the event's **owner** (handler near decomp line 865469; both args are numbers; `FUN_14044eb70` looks up a row by id). `130407` deletes it. Texts are in `keyguide.fmg` (100 Examine, 111 Touch, 500 Talk, 704 Touch Summon Sign, 706 Read message…).
- **Misc:** `130101(flag, on)`, `140301` flag condition, `130131(first, last, value)`, `130001`/`130002` end notifications.
- **`EventParam`:** bootType 1 = starts by itself (TB's ownerless 5010, 6000, 8000…), 2 = with an owner (ownerType 1 object, 2 character = `GeneratorLocation` id) or started by another script, 4 = per bonfire.
- Talk and event EDDs share one command set; NPC menus use `130461` "open NPC window".
- Characters are addressed by `GeneratorLocation` ids. TB's crones: Strowen 210-212 (talk 7050xxxx, the main one), Griant 215-217, Morrel 220-222; Milibeth 15-17. Their presence depends on story state.

### SoulsFormats round trips on vanilla DS2 files (`lr.cmd roundtrip`)
- MSB2: byte-identical.
- ESD: content-identical, but smaller (different layout).
- Per-map PARAM: identical except a **4-byte trailer** (12 bytes for MapObjectInstanceParam) that SoulsFormats drops. Not CRC32/Adler; suspected checksum. **HANDOFF FIRST #1: editing TB's per-map files broke TB's events.**

### Input manager
- `DAT_141875278`: `+0x16d` window active (set only by WM_ACTIVATE in `FUN_140f05f30`), `+0x16e` pad foreground setting.
- `FUN_140f06dd0` drops the keyboard device on any `GetDeviceState` failure; it is re-created once a second.
- DS2 ignores input while unfocused even with the device kept and `+0x16d` forced, so tests bring DS2 to the front (`src\ds2-focus.ps1`).
