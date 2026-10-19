using System.Buffers.Binary;
using System.Globalization;
using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// DS1 characters make DS1's sounds (session 8, track chrsound): the sound and effect events of a 1:1 character's DS1 TAE
/// become DS2 TimeAct events, and the character's DS2 sound bank carries DS1's own FMOD project and wave bank. Generic for
/// every 1:1 character (<see cref="Ds1RigChr"/>); rules S1-S10 (status V verified offline / P plausible / U unknown, evidence
/// in data\sandbox\chrsound\NOTES.md).
/// <list type="bullet">
/// <item><b>S1 (P; FMOD side V offline) Bank.</b> DS2 loads a character's sounds from <c>sound/frpg2_cBBBB.bnd</c>, BBBB =
/// animId / 100 rounded down to tens (decomp FUN_140166c20 "chr:/sound/frpg2_c%04d.bnd"; run s8-6 log: the c2501 (animId
/// 250100) spawn opened gamedata:/sound/frpg2_c2500.bnd; DS2 ships banks only for xxx0 ids although it has models
/// c1021/c1031/c2011/...). The bank resource loader reads entry id 0 (+ optional id 2) as an FMOD project and id 1 as the
/// wave bank (decomp FUN_140b13bf0: id 0 → FUN_140b05bd0 message with the project bytes → FUN_140b71550 → FUN_1409d7ad0 →
/// vtable+0xe0 → FUN_1409df930 → EventSystem::load from memory, then the project is registered as a sound bank under its
/// own FMOD name (getInfo → FUN_1409e5d30 → FUN_1409dc1c0) and its groups are listed (FUN_1409e5930, a name tree); id 1 →
/// createSound + EventSystem::preloadFSB under "&lt;resource name&gt;.fsb"). Vanilla banks only use id 1 (177/177: all
/// their projects are in frpg2.fevbnd), so a DS1 character brings its own DS1 project as id 0: DS1 keeps one FMOD project
/// per character (sound\frpg_cNNNN.fev + .fsb), FEV1 v0x3D0000 + FSB4. V offline (validate\fmod_play.py,
/// fmod_resolve.py on the game's own fmod_event64.dll 4.44.50): the project loads from memory after DS2's 22 fevbnd
/// projects, its events start and render DS1 audio, and DS2's event lookup routes 1 (EventSystem::getEvent) and 3 (the
/// bank's name → system id map, getEventBySystemID; decomp FUN_1409f5120 / FUN_1409f52f0) find them when emulated. P: that
/// DS2's own code path for an id-0 project runs as decoded (no vanilla data uses it; the in-game run decides, S8).</item>
/// <item><b>S1b (V offline; decomp) Project name = bank resource name.</b> DS2 de-duplicates a bank load by asking whether a
/// sound bank of the resource's name is registered (FUN_140b71550 → FUN_1409d7a60 → FUN_1409f1330 over the banks at
/// +0x238, compared by their FMOD project name) and releases by the resource name (FUN_140b04b20 / FUN_140b05e90), while the
/// loaded project is registered under the name EventProject::getInfo reports (the FEV header's project name). DS1's own name
/// "frpg_cNNNN" would never match "frpg2_cBBBB": every reload of the area would call EventSystem::load again (FMOD 92, the
/// loaded copy stays) and DS2 would never release it. So the project is renamed to the bank's resource name
/// (<see cref="Fev1Project.WithName"/>; nothing else in an FEV1 refers to it), and a DS2 fevbnd project of that name must
/// not exist (checked: <see cref="BankChecks"/>). The name also gives each decade its own FMOD project name, so the same DS1
/// character could even sit in two decades without FMOD refusing the second load.</item>
/// <item><b>S2 (V offline) Loose wave bank.</b> DS2 preloads id 1 under "frpg2_cBBBB.fsb", but DS1's project asks FMOD for
/// its own bank name "frpg_cNNNN.fsb" (FEV bank list; FSB4 hash at 0x18 = the FEV's bank hash). Harness: preloaded under
/// DS2's name → getEvent fails with FMOD_ERR_FILE_NOTFOUND (23); preloaded under DS1's name, or present in the media
/// path → plays. DS2's media path is Game\sound\ (FMOD opens Game\sound\frpg2_smain.fsb itself, s8-6 log), and the
/// loader serves title/sound/... for it, so the DS1 bank also ships loose as title/sound/frpg_cNNNN.fsb. Editing the bank
/// name inside the FEV would shift the whole FEV1 file (the name also follows every wave entry), so it stays DS1's.</item>
/// <item><b>S3 (V offline) Sound events.</b> DS1 TAE 128 "Play Sound - General" {type, id}, 129 "Play Sound - Extra" and
/// 130 {type, id, dummy, slot} (Smithbox TAE.Template.DS1.xml) → DS2 201 in the _snd TAE. DS1 names an event type letter
/// + 9 digits ("acfopsmvxbgy"[type], DSR FUN_140255520). DS2 201 (decomp FUN_1403278d0 case 0xc9 → FUN_1404a4a70) builds
/// "c%09d" of (chr·100000 + id + offset), chr = 0 when the params byte +13 is set, chr = animId/100 rounded down to tens
/// otherwise. So a DS1 character event (type 1 'c' or 8 'x', id = DS1 chr·100000 + n) becomes 201 with the whole DS1
/// id and byte +13 = 1: the name is exactly DS1's for any 1:1 model/animId numbering. Params (24 B, vanilla layout):
/// +0 id, +4 offset kind 0, +8 0 (no +1000), +9 0 ('c' route; 1 = "s%09d"), +10 s16 dummy (-1 = the character's
/// position), +12 1 (as DS2's own character sounds), +13 1 (no chr prefix), +14 0 (no condition), +15 0, +16 u16 slot
/// (0xFFFF none), +18 0, +20 0; data unk = 1, group type 200 (every vanilla 201).</item>
/// <item><b>S4 (P) Floor-material sounds.</b> DS1 type 8 'x' = floor material determined: DS1 plays the 'c' event whose last
/// digit is the floor's material digit (c2500 defines c250001001..005/009 for TAE id 250001001; frpg_main defines
/// c000006101..6105/6109 for TAE id 6100 and c000006100 is an empty event), so 'x' id → 'c' (id − id mod 10 + 1), the
/// material-1 (stone) variant (<see cref="Ds1Event"/>). In c2500 and frpg_main every material variant of these ids names
/// the same sound definition (validate\fev1_events.py: c25000100x → c2500_foot_walk, c00000610x → downs-knee), so the
/// choice is exact for the Hollow. Fix pass: DS1 type 9 'b' (armor material determined, TAE.Template.DS1.xml) works the
/// same way with the character's armor material: DS1 c2540's TAE plays b254001001 and frpg_c2540 defines only 'c' events,
/// c254001001..006 (six material variants, all c2540_foot_walk), so 'b' id → 'c' material 1 as well (no DS1 project
/// defines a 'b' event, and DS2's 201 can only name 'c'/'s'/'h' events).</item>
/// <item><b>S5 (V offline) DS1 common sounds.</b> Ids outside the character's range live in DS1's frpg_main project
/// (c999999999 enemy-damage at frame 0 of every DS1 damage clip, c999999997 kill, c000004700 swings, s000007701 ...).
/// frpg_main itself cannot be loaded into DS2: 75 of its 1282 event names are also DS2 event names (c000001002..c000008000,
/// s999999999, z00201000x), and DS2 resolves events by name. So the events the character's DS1 TAE names are copied into
/// the character's own project (<see cref="Fev1Project.AddEvents"/>: event bytes, sound definition, config and DS1's
/// frpg_main wave bank entry; DS1's own frpg_c2232 already lists frpg_main as a second bank) under the name
/// c{DS1 chr}9{last 4 digits} (c999999999 → c250099999, c000004700 → c250094700; DS2's own characters keep their blood /
/// damage sound at c{chr}99999 too, c103099999), and DS1's frpg_main.fsb ships loose next to the character's bank.
/// The 201 event names that id. <see cref="Ds1Common"/> records what each one is (DS1 sound definition by
/// validate\fev1_events.py). A common id frpg_main does not define (c000001000) stays silent, as in DS1.</item>
/// <item><b>S5b (merge9 stage 4, V offline) Character-range events in frpg_main.</b> DS1 also keeps 140 character-range events
/// in frpg_main instead of the character's project (c254000000 /frpg_main/body-hauberk2540 and c255000000 body-armor2550,
/// which DS1 c2540 / c2550 play on most clips; c290099999, a 2-layer event of c2900). A character-range id the character's
/// project lacks is copied from frpg_main under its own DS1 name when it is a simple event; one that is in neither (or not
/// simple: c290099999) is not emitted, so no 201 names an event no loaded project defines (silent; DS1 plays the 2-layer
/// one). <b>S5c (chrint stage 4, V offline)</b>: layered events are copied too, every layer's sound definition re-pointed
/// (<see cref="Fev1Project.LayerSoundDefOffsets"/>): c290099999 (bone_damage1 + iron-knock-stone fragmentB) now plays at
/// frame 0 of the skeleton's damage clips as in DS1.</item>
/// <item><b>S6 (V offline) Times.</b> A clip slot plays its DS1 clip whole from frame 0, so events keep DS1's times; a
/// locomotion loop re-cut to start at its left touchdown k frames later (rule C11b, <see cref="Ds1RigAnims.PhaseShift"/>)
/// has every event moved to frame (f - k) mod P (P = frames - 1, the cycle), durations kept (clamped to the clip).</item>
/// <item><b>S7 (P/U) Effects.</b> DS1 FFX events → DS2 _sfx events where <see cref="EffectMap"/> names a DS2 effect, by the
/// DS1 event's kind (<see cref="Ds2Effect"/>; decomp FUN_1403271b0; layouts and byte patterns as the vanilla events,
/// validate\sfx_survey.py over all 696 vanilla TAEs). DS2 2100 spawns its effect when the event starts and leaves it
/// alive after the event (case 0x834: no kill at the event's end), into the slot at +0 (-1 none; 2101 removes a slot's
/// effect); 2104 holds its effect while the event is active and ends it at the event's end (case 0x838: FUN_1402e8d30 /
/// FUN_1402e8c10). Byte +13 picks the dummies: 1 = every dummy of the reference id (FUN_1404c7450), 0 = the first
/// (FUN_1404c7ca0). So: DS1 96 "Spawn One Shot FFX", 112 floor-determined, 115 once → 2100 slot -1, one frame; DS1's slot
/// effects 99/100/104/108/109/114/116/118/120/121 (spawned into a slot, living on after the event: c2500's eye glow 109 is
/// a one-frame event at frame 0 of the idle clips) → 2100 with DS1's slot id, one frame; DS1 119 "Body - for Event
/// Duration" → 2104 over the DS1 span. Params {i32 slot, i32 sfx, i32 dummy, u8 1, u8 every-dummy, u8 0 body model, u8 0}
/// zero-padded to 48 B (2100) / 32 B (2104); +13 = 1 where the model has several dummies of the id (DS1 spawns at each);
/// data unk 0, group type 2100, as every vanilla _sfx event. 101/110 {one int} are not translated (U). P: whether
/// re-spawning into an occupied DS2 slot replaces the old effect as DS1's slots do (no vanilla data shows it). DS1 FFX
/// (FXR1) and DS2 effects (DLsE) are both FX action graphs (they share action ids 27/28/55/59/70/71/105: SoulsFormats
/// reads both, chr-sfx), but a converted DS1 effect would need a new DS2 effect bank entry (title:/sfx/sfx9999_Append or a
/// dlc_data:/sfx bank), so the map holds DS2 effects only. For c2500 none qualifies (evaluated: chr-sfx,
/// validate\sfx_dummy_bones.py, sfx_tex_similar.py, sfx_ds2_sprites.py): 112 floor dust 3000/3100/3200 → DS2's own foot
/// effects (FootEffectSfxParam by floor; DS2's own Hollow c103000_sfx has no events); 109 FFX 12501 on dummy 199 (the two
/// eye dummies on the head) = the Hollow's eye glow, a 3.5 cm faint orange (1, 0.33, 0, alpha 0.12-0.24) soft sprite
/// (texture s01036) in DS1's idle/dormant clips: DS2 attaches no effect to a head in any idle or wait animation (21 head
/// effects in vanilla TAEs: breath, magic, all in attacks), and the DS2 effects drawn with the same soft sprite (DS2
/// s000007/s000020/s000052, correlation 0.98-0.99) never draw it alone and small: of the 40 with such a sprite of at most
/// 12 cm all also draw other textures (39 also sprites of 1-50 m: magic, spells; validate\sfx_glow_candidates.py), and
/// the plain soft-glow effects are big glows or flashes (1700: 2 m glow + point light; 293: 2.5 m flash), so none is
/// emitted (U); 99/100 FFX 12500 on dummy 20 (weapon) belong to the a01 set (not the unarmed variant).</item>
/// <item><b>S8 (U) What the in-game test must show.</b> DS2 has never loaded a per-character project from a bank (no
/// vanilla data uses id 0), so the first in-game run decides S1; the first suspect if the Hollow stays silent. With S1b a
/// map reload finds the bank registered under its resource name and skips the load (as for any loaded bank), and the
/// release by name finds it (validate\fmod_reload.py emulates DS2's name check: no FMOD 92).</item>
/// <item><b>S9 (V offline) Wave banks.</b> Every wave bank the (extended) project names ships loose as
/// title/sound/&lt;bank&gt;.fsb from DSR's sound folder, its FSB4 hash checked against the project's bank entry; the
/// character's own bank (frpg_cNNNN, else the first listed) is also entry id 1.</item>
/// <item><b>S10 (V offline) One bank per DS1 character per decade.</b> The bank is a function of the DS1 character and the
/// decade only: DS1's project, plus the DS1 common sounds named anywhere in the DS1 character's TAE (every move set and
/// variant), under the decade's name. So every 1:1 build of one DS1 character in one decade (the sword / bow / unarmed
/// Hollows 250100/250200/250300, the Hollow Warriors 2541xx-2543xx, ...) yields the same bytes and shares the file and the
/// common-sound ids; two different DS1 characters in one decade cannot share it (one project per bank) and the build
/// refuses them, as it refuses a decade that is one of DS2's own banks (<see cref="BankChecks"/>, ChrPorter.AddDs1RigFiles).
/// DS1 neighbours (c2230/c2231/c2232, c2790-c2795, ...) and DS1 characters whose DS2 decade is taken (c223x, c224x,
/// c226x, c227x, ...) need animIds in a free decade (<see cref="FreeDecades"/>).</item>
/// </list>
/// </summary>
public static class Ds1ChrSound
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>DS1 sound type letters (DSR FUN_140255520: "acfopsmvxbgy"[type] + 9 digits = FMOD event name).</summary>
    public const string Ds1TypeLetters = "acfopsmvxbgy";

    /// <summary>DS2 _snd event group type (every vanilla group of 201/202 events) and _sfx group type (2100-2104 events).</summary>
    public const int SndGroup = 200, SfxGroup = 2100;

    /// <summary>A DS1 TAE sound event: TAE type (128/129/130), sound type, id, dummy (-1 for 128), slot.</summary>
    public sealed record Ds1Sound(float Start, float End, int Event, int Type, int Id, int Dummy, int Slot)
    {
        public string Name => $"{(Type is >= 0 and < 12 ? Ds1TypeLetters[Type] : '?')}{Id:D9}";
    }

    /// <summary>A DS1 TAE effect event: TAE type, FFX id, dummy (-1 if the type has none), slot (-1 if none).</summary>
    public sealed record Ds1Effect(float Start, float End, int Event, int Ffx, int Dummy, int Slot);

    static int I32(byte[] p, int o) => p.Length >= o + 4 ? BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(o)) : -1;
    static int I16(byte[] p, int o) => p.Length >= o + 2 ? BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(o)) : -1;

    /// <summary>The DS1 FMOD event a sound event plays (rule S4): type 8 'x' (floor material determined) and type 9 'b' (armor
    /// material determined) → the 'c' event with material digit 1; other types unchanged.</summary>
    public static (int Type, int Id) Ds1Event(int type, int id) => type is 8 or 9 ? (1, id - id % 10 + 1) : (type, id);

    public static List<Ds1Sound> Sounds(TAE.Animation? a) =>
        a == null ? [] : a.Events.Where(e => e.Type is 128 or 129 or 130).Select(e =>
        {
            var p = e.GetParameterBytes(false);
            return new Ds1Sound(e.StartTime, e.EndTime, e.Type, I32(p, 0), I32(p, 4), e.Type == 128 ? -1 : I32(p, 8), e.Type == 128 ? -1 : I32(p, 12));
        }).OrderBy(s => s.Start).ToList();

    /// <summary>DS1 FFX event layouts (TAE.Template.DS1.xml): {FFX, dummy, slot} for 96/99/100/104/108/109/116; {FFX,
    /// dummy} for 112 (floor determined: the floor material is added to the id at run time, f0003001..3029); {FFX, s16
    /// source, s16 dummy, slot} 114/115; {FFX, s16 source, s16 base, s16 tip, s16 slot} 118; {FFX, s16 dummy, ...} 119/121;
    /// {4 FFX ids, dummy, slot} 120; {one int} 101/110.</summary>
    public static List<Ds1Effect> Effects(TAE.Animation? a) =>
        a == null ? [] : a.Events.Where(e => e.Type is 96 or 99 or 100 or 101 or 104 or 108 or 109 or 110 or 112 or 114 or 115 or 116 or 118 or 119 or 120 or 121).Select(e =>
        {
            var p = e.GetParameterBytes(false);
            return e.Type switch
            {
                96 or 99 or 100 or 104 or 108 or 109 or 116 => new Ds1Effect(e.StartTime, e.EndTime, e.Type, I32(p, 0), I32(p, 4), I32(p, 8)),
                112 => new Ds1Effect(e.StartTime, e.EndTime, e.Type, I32(p, 0), I32(p, 4), -1),
                114 or 115 => new Ds1Effect(e.StartTime, e.EndTime, e.Type, I32(p, 0), I16(p, 6), I32(p, 8)),
                118 => new Ds1Effect(e.StartTime, e.EndTime, e.Type, I32(p, 0), I16(p, 6), I16(p, 10)),
                119 or 121 => new Ds1Effect(e.StartTime, e.EndTime, e.Type, I32(p, 0), I16(p, 4), -1),
                120 => new Ds1Effect(e.StartTime, e.EndTime, e.Type, I32(p, 0), I32(p, 16), I32(p, 20)),
                _ => new Ds1Effect(e.StartTime, e.EndTime, e.Type, I32(p, 0), -1, -1),
            };
        }).OrderBy(s => s.Start).ToList();

    /// <summary>
    /// DS1 frpg_main sounds the 1:1 characters' TAEs name (rule S5: copied into the character's project). Value: DS1 sound
    /// definition (validate\fev1_events.py on DSR sound\frpg_main.fev) and the DS2 system with the same role (which keeps
    /// playing too: DS1 layered its common sounds over the hit sound the same way). Key (DS1 sound type, id). c000001000 is
    /// in no DS1 project (silent in DS1 too). Reports only: which commons are copied is decided by frpg_main's event list.
    /// </summary>
    public static readonly Dictionary<(int Type, int Id), (string Ds1, string Ds2)> Ds1Common = new()
    {
        [(1, 1000)] = ("(no DS1 event: silent in DS1)", "none"),
        [(1, 4500)] = ("/frpg_main/swing-stab (attack swing)", "DS2 attack swing sounds c00000600x (weapon attacks: tracks enemies/boss)"),
        [(1, 4700)] = ("/frpg_main/swing-knock (attack swing)", "as 4500"),
        [(1, 4900)] = ("/frpg_main/swing-throw (attack swing)", "as 4500"),
        [(1, 10000)] = ("/frpg_main/bow-pre", "DS2 bow sounds (bow Hollow: tracks enemies)"),
        [(1, 10100)] = ("/frpg_main/bow-stance", "as 10000"),
        [(1, 10200)] = ("/frpg_main/bow-shot", "as 10000"),
        [(1, 999999988)] = ("/frpg_main/EST-drink", "DS2 item use sounds"),
        [(1, 999999989)] = ("/frpg_main/slipslide", "DS2 slide sounds"),
        [(1, 999999990)] = ("/frpg_main/headshot", "DS2 hit sound (z weapon x material)"),
        [(1, 999999991)] = ("/frpg_main/kill-success", "DS2 throw (riposte/backstab) sounds"),
        [(1, 999999992)] = ("/frpg_main/kill-chance", "DS2 throw sounds"),
        [(1, 999999995)] = ("/frpg_main/kill-prekill-kick", "DS2 throw sounds"),
        [(1, 999999996)] = ("/frpg_main/enemy-damage (damage1-3.wav)", "DS2 hit sound: DamageEffectSeParam material of the cloned row x weapon ('z' events)"),
        [(1, 999999997)] = ("/frpg_main/kill1 (kill1-2.wav)", "DS2 death/critical sounds of the damage system"),
        [(1, 999999998)] = ("/frpg_main/kill-pre", "DS2 throw sounds"),
        [(1, 999999999)] = ("/frpg_main/enemy-damage (damage1-3.wav)", "DS2 hit sound: DamageEffectSeParam material of the cloned row x weapon ('z' events)"),
        [(5, 7701)] = ("/frpg_main/Fragment (fragment.wav)", "none identified"),
        [(8, 6100)] = ("/frpg_main/downs-knee (floor-material event c000006101, rule S4)", "DS2 body-fall sounds of its own clips"),
    };

    /// <summary>
    /// DS1 FFX id → DS2 effect id (0 = not translated) and why (rule S7). FFX ids of 112 events are the floor-determined
    /// bases (3000/3100/3200: the floor material is added at run time). A DS2 id here emits DS2 2100 / 2104 events
    /// (<see cref="Ds2Effect"/>); the DS2 id must be one of the global title:/sfx/sfx9999[_Append] effects (loaded in
    /// every map). Candidates are evaluated with chr-sfx (DS1 FXR1 / DS2 DLsE contents) and validate\sfx_*.py (fix pass).
    /// </summary>
    public static readonly Dictionary<int, (int Ds2, string Why)> EffectMap = new()
    {
        [3000] = (0, "DS1 floor-determined dust (112, walk; f0003001-3029 per floor) → DS2's engine foot effects (FootEffectSfxParam by floor; DS2's own Hollow has no TAE effects)"),
        [3100] = (0, "DS1 floor-determined dust (112, run / body) → DS2's engine foot effects"),
        [3200] = (0, "DS1 floor-determined dust (112, fall / landing) → DS2's engine foot effects"),
        [12501] = (0, "DS1 c2500 eye glow (109 on dummy 199 = the two eye dummies on Head, idle/dormant clips; removed by 110 0 on death): DS1 f0012501 = cone emitter + cluster billboard of the soft sprite s01036, 3.5 cm, colour (1, 0.33, 0) alpha 0.12-0.24, 0.33 s particles. No DS2 counterpart: DS2 attaches no effect to a head in idle/wait animations, and none of its effects draws only small soft-glow sprites (s000007/s000020/s000052 look like s01036): the 40 with such a sprite of at most 12 cm all draw other textures too (39 also 1-50 m sprites: magic), the plain ones are big glows or flashes (1700: a 2 m glow + point light; 293: a 2.5 m flash). Converting f0012501 needs a new DS2 effect bank entry (U)"),
        [12500] = (0, "DS1 c2500 a01-set effect on dummy 20 (weapon): not this variant"),
    };

    /// <summary>Rule S6: DS1 event time → slot time. Non-loops keep DS1's times; a loop started <paramref name="shift"/>
    /// frames later moves each event to frame (f - shift) mod P, P = frames - 1; durations kept, clamped to the clip.</summary>
    public static (float S, float E) SlotTime(float s, float e, int shift, int frames, double fps)
    {
        double d = (frames - 1) / fps;
        if (shift == 0) return (s, Math.Min(e, (float)d));
        int P = frames - 1, f = (int)Math.Round(s * fps), len = Math.Max(0, (int)Math.Round((e - s) * fps));
        int g = ((f - shift) % P + P) % P;
        return ((float)(g / fps), (float)(Math.Min(g + len, P) / fps));
    }

    /// <summary>DS2 201 parameters (rule S3), 32 bytes as in the vanilla files (24 used + 8 zero).</summary>
    public static byte[] Ds2SoundParams(int soundId, int dummy, int slot)
    {
        var p = new byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(p, soundId);
        BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(10), (short)(dummy >= 0 ? dummy : -1));
        p[12] = 1; p[13] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(16), slot >= 0 ? (ushort)slot : (ushort)0xFFFF);
        return p;
    }

    /// <summary>The FMOD event name DS2 builds from a 201 written by <see cref="Ds2SoundParams"/> (byte +13 set: no chr
    /// prefix, no offset; prefix 'c' for a 'c'-type character).</summary>
    public static string Ds2EventName(byte[] p) => $"c{BinaryPrimitives.ReadInt32LittleEndian(p):D9}";

    /// <summary>DS2 effect event spawned once (2100) and effect event held for its span (2104) (rule S7).</summary>
    public const int Ds2Spawn = 2100, Ds2ForSpan = 2104;

    /// <summary>How a DS1 FFX event type becomes a DS2 event (rule S7): (DS2 type, keeps DS1's slot), or (0, false) = not
    /// translated (101/110: one int each, meaning not established). DS1 96 "Spawn One Shot FFX", 112 floor-determined and
    /// 115 "once" spawn and forget (2100, slot -1); DS1 99/100/104/108/109/114/116/118/120/121 spawn into a slot where the
    /// effect lives on after the event (DS1 c2500's 109 eye glow is a one-frame event at frame 0 of its idle clips; the
    /// effect lasts), so 2100 with DS1's slot id; DS1 119 "Body - for Event Duration" → 2104 over the event's span.</summary>
    public static (int Type, bool KeepSlot) Ds2Effect(int ds1Event) => ds1Event switch
    {
        96 or 112 or 115 => (Ds2Spawn, false),
        99 or 100 or 104 or 108 or 109 or 114 or 116 or 118 or 120 or 121 => (Ds2Spawn, true),
        119 => (Ds2ForSpan, false),
        _ => (0, false),
    };

    /// <summary>Vanilla parameter block sizes (SOTFS template: 2100 = 12 s32, 2104 = 8 s32; to-the-next-block distances in
    /// the vanilla TAEs 64 / 48 = params + the next event's 16-byte data header, validate\sfx_survey.py).</summary>
    public static int Ds2EffectParamSize(int type) => type == Ds2Spawn ? 48 : 32;

    /// <summary>
    /// DS2 2100 / 2104 parameters (rule S7; decomp FUN_1403271b0 cases 0x834 / 0x838): +0 i32 slot (-1 = none: 67/75
    /// vanilla 2100 events; 8 use slots 1), +4 i32 sfx id, +8 i32 dummy, +12 u8 1 (72/80 vanilla 2100, 586/711 2104), +13 u8
    /// = 1 to spawn at every dummy of that reference id (FUN_1404c7450 loops over the model's dummies of the id), 0 at the
    /// first one (FUN_1404c7ca0; both then call FUN_1404c8d20 with the slot), +14 u8 0 (body model; 1 = the second model),
    /// +15 u8 0; the rest zero to the vanilla size (48 / 32 bytes). DS1 spawns at every dummy of an id (c2500's eye glow on
    /// the two dummies 199), so +13 = 1 where the model has several dummies of the id (vanilla: 12 2104 events of c1380).
    /// </summary>
    public static byte[] Ds2EffectParams(int type, int sfx, int dummy, int slot = -1, bool everyDummy = false)
    {
        var p = new byte[Ds2EffectParamSize(type)];
        BinaryPrimitives.WriteInt32LittleEndian(p, slot >= 0 ? slot : -1);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(4), sfx);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(8), dummy);
        p[12] = 1;
        p[13] = (byte)(everyDummy ? 1 : 0);
        return p;
    }

    public sealed class Translation
    {
        public List<Ds2Tae.NewEvent> Snd = [], Sfx = [];
        public int Own, Common, Unknown, EffectsMapped, EffectsDropped;
        public List<string> Notes = [];
    }

    /// <summary>Rules S3-S7 for one clip slot: the DS1 animation's sound and effect events as DS2 _snd / _sfx events.
    /// <paramref name="commons"/>: DS1 common sounds (type, id) copied into the character's project → their new id
    /// (<see cref="Bank"/>, rule S5); null = none.
    /// <paramref name="multiDummies"/>: dummy reference ids the model has more than once (DS1 spawns an effect at each).
    /// <paramref name="effectMap"/>: null = <see cref="EffectMap"/> (ds1rig-check exercises the S7 writer with a test map).
    /// <paramref name="ownEvents"/> (merge9 stage 4): the character-range event names the bank's project defines
    /// (<see cref="BankResult.OwnEvents"/>: DS1's own project + the character-range events copied from frpg_main, rule S5b); a
    /// character-range DS1 event outside it is not emitted (no 201 naming an event no loaded project has; e.g. DS1 c2900's
    /// c290099999, a 2-layer frpg_main event AddEvents cannot copy); null = every own-range id is emitted.</summary>
    public static Translation Translate(TAE.Animation? ds1, int ds1Chr, int shift, int frames, double fps, IReadOnlyDictionary<(int Type, int Id), int>? commons = null,
        IReadOnlyDictionary<int, (int Ds2, string Why)>? effectMap = null, IReadOnlySet<int>? multiDummies = null, IReadOnlySet<string>? ownEvents = null)
    {
        effectMap ??= EffectMap;
        var t = new Translation();
        foreach (var s in Sounds(ds1))
        {
            var (st, en) = SlotTime(s.Start, s.End, shift, frames, fps);
            var (et, eid) = Ds1Event(s.Type, s.Id);
            bool own = et == 1 && eid / 100000 == ds1Chr;
            bool known = Ds1Common.TryGetValue((s.Type, s.Id), out var c);
            string what = known ? c.Ds1 : "DS1 frpg_main event";
            if (own && ownEvents != null && !ownEvents.Contains($"c{eid:D9}"))
            {
                t.Unknown++;
                t.Notes.Add($"{s.Name} @{st.ToString("0.###", Inv)} (TAE {s.Event}): the character's own id c{eid:D9}, which the bank's project lacks (DS1 has it neither in the character's project nor as a simple frpg_main event; see the bank report): not played");
            }
            else if (own)
            {
                t.Snd.Add(new Ds2Tae.NewEvent(201, 1, st, en, Ds2SoundParams(eid, s.Dummy, s.Slot)));
                t.Own++;
            }
            else if (commons != null && commons.TryGetValue((et, eid), out int nid))
            {
                t.Snd.Add(new Ds2Tae.NewEvent(201, 1, st, en, Ds2SoundParams(nid, s.Dummy, s.Slot)));
                t.Common++;
                t.Notes.Add($"{s.Name} @{st.ToString("0.###", Inv)} DS1 common {what} → c{nid:D9} (copied into the character's project, S5)");
            }
            else
            {
                t.Unknown++;
                t.Notes.Add($"{s.Name} @{st.ToString("0.###", Inv)} (TAE {s.Event}): {(known ? $"DS1 common {c.Ds1}" : "neither the character's sound nor a copied DS1 common sound")}: not played");
            }
        }
        foreach (var e in Effects(ds1))
        {
            var (st, en) = SlotTime(e.Start, e.End, shift, frames, fps);
            var (type, keepSlot) = Ds2Effect(e.Event);
            if (type != 0 && effectMap.TryGetValue(e.Ffx, out var m) && m.Ds2 > 0)
            {
                // spawn: one frame from the DS1 start, like the vanilla 2100 events; for span: the DS1 event's span
                float clipEnd = (float)((frames - 1) / fps);
                float end = type == Ds2Spawn ? Math.Min(st + (float)(1 / 30.0), Math.Max(st, clipEnd)) : en;
                bool every = multiDummies != null && multiDummies.Contains(e.Dummy);
                t.Sfx.Add(new Ds2Tae.NewEvent(type, 0, st, end, Ds2EffectParams(type, m.Ds2, e.Dummy, keepSlot ? e.Slot : -1, every)));
                t.EffectsMapped++;
            }
            else
            {
                t.EffectsDropped++;
                string why = type == 0 ? "single-value FFX event (DS1 meaning not established; U)" : effectMap.TryGetValue(e.Ffx, out var mm) ? mm.Why : "no DS2 counterpart chosen";
                t.Notes.Add($"FFX {e.Event} {e.Ffx} dummy {e.Dummy}{(multiDummies != null && multiDummies.Contains(e.Dummy) ? " (several)" : "")} slot {e.Slot} @{st.ToString("0.###", Inv)}-{en.ToString("0.###", Inv)}: not translated ({why})");
            }
        }
        t.Snd = [.. t.Snd.OrderBy(x => x.Start)];
        t.Sfx = [.. t.Sfx.OrderBy(x => x.Start)];
        return t;
    }

    /// <summary>The DS1 FEV1 project's name and wave banks (name, 8-byte hash): FEV1 header {"FEV1", u32 version, u32,
    /// u32, lstr project, u32 bank count, per bank {u32 load mode, u32 max streams, u64 hash, lstr name}} (lstr = u32 length
    /// incl. NUL + bytes), checked on DSR frpg_c2500/c2731.fev.</summary>
    public static (string Project, List<(string Name, ulong Hash)> Banks) Fev1Header(byte[] fev)
    {
        if (fev.Length < 0x20 || fev[0] != 'F' || fev[1] != 'E' || fev[2] != 'V' || fev[3] != '1') throw new InvalidDataException("not an FEV1 project");
        int o = 0x10;
        string Lstr() { int n = BinaryPrimitives.ReadInt32LittleEndian(fev.AsSpan(o)); string s = System.Text.Encoding.ASCII.GetString(fev, o + 4, Math.Max(0, n - 1)); o += 4 + n; return s; }
        string project = Lstr();
        int nb = BinaryPrimitives.ReadInt32LittleEndian(fev.AsSpan(o)); o += 4;
        var banks = new List<(string, ulong)>();
        for (int i = 0; i < nb; i++)
        {
            o += 8;
            ulong hash = BinaryPrimitives.ReadUInt64LittleEndian(fev.AsSpan(o)); o += 8;
            banks.Add((Lstr(), hash));
        }
        return (project, banks);
    }

    /// <summary>FSB4 header hash (8 bytes at 0x18, after "FSB4", sample count, header size, data size, version, mode).</summary>
    public static ulong Fsb4Hash(byte[] fsb) =>
        fsb.Length >= 0x20 && fsb[0] == 'F' && fsb[1] == 'S' && fsb[2] == 'B' && fsb[3] == '4' ? BinaryPrimitives.ReadUInt64LittleEndian(fsb.AsSpan(0x18)) : throw new InvalidDataException("not an FSB4 bank");

    /// <summary>DS2 sound bank id of a character: animId / 100 rounded down to tens (rule S1).</summary>
    public static int BankChr(int animId) => animId / 100 / 10 * 10;

    /// <summary>The bank's resource name = the FMOD project name the bank's project gets (rule S1b).</summary>
    public static string BankName(int bankChr) => $"frpg2_c{bankChr:D4}";

    /// <summary>
    /// Rules S1b/S10: why a decade cannot hold a 1:1 character's bank (empty = it can): DS2's own bank of that name (it would
    /// replace that DS2 character's sounds), DS2's special case "frpg2_c2250" (FUN_140b13bf0 skips its wave-bank fallback by
    /// name), or a DS2 fevbnd project of the bank's name (DS2 would think the bank already loaded, S1b).
    /// </summary>
    public static List<string> BankChecks(string ds2Data, int bankChr)
    {
        var why = new List<string>();
        string name = BankName(bankChr);
        if (File.Exists(Path.Combine(ds2Data, "sound", name + ".bnd"))) why.Add($"{name}.bnd is DS2's own bank");
        if (bankChr == 2250) why.Add("frpg2_c2250 is special-cased by DS2's bank loader (FUN_140b13bf0)");
        if (Ds2ProjectNames(ds2Data).Contains(name)) why.Add($"DS2's frpg2.fevbnd has a project named {name}");
        return why;
    }

    static readonly Dictionary<string, HashSet<string>> ds2Projects = [];

    /// <summary>The FMOD project names of DS2's frpg2.fevbnd: DS2's projects are RIFF "FEV " files (newer than DS1's FEV1),
    /// each named like its entry's file stem (frpg2, frpg2_c1 .. frpg2_c9, frpg2_m, ...: EventProject::getInfo gives exactly
    /// the stem for all 22, validate\fmod_resolve.py); an FEV1 entry would carry the name in its header.</summary>
    public static HashSet<string> Ds2ProjectNames(string ds2Data)
    {
        lock (ds2Projects)
        {
            if (ds2Projects.TryGetValue(ds2Data, out var set)) return set;
            set = [];
            string p = Path.Combine(ds2Data, "sound", "frpg2.fevbnd.dcx");
            if (File.Exists(p))
                foreach (var f in BND4.Read(p).Files.Where(f => f.Name.EndsWith(".fev", StringComparison.OrdinalIgnoreCase)))
                {
                    string stem = Path.GetFileNameWithoutExtension(f.Name.Replace('\\', '/').Split('/')[^1]);
                    try { set.Add(Fev1Header(f.Bytes).Project); } catch (InvalidDataException) { set.Add(stem); }
                }
            return ds2Projects[ds2Data] = set;
        }
    }

    /// <summary>
    /// Rule S10: decades free for a 1:1 character near <paramref name="near"/>, nearest first: no DS2 sound bank, model,
    /// anibnd or TimeAct in the decade (DS2 numbers a character's model, anim set and bank in one decade), not 2250, and no
    /// <see cref="BankChecks"/> objection. A 1:1 animId is then decade·100 + n·100 (anibnd c{decade+n}) for n = 0..9.
    /// </summary>
    public static List<int> FreeDecades(string ds2Data, int near, int count = 6)
    {
        var used = new HashSet<int>();
        void Mark(string dir, string pattern, Func<string, int?> id)
        {
            string d = Path.Combine(ds2Data, dir);
            if (!Directory.Exists(d)) return;
            foreach (var f in Directory.GetFiles(d, pattern))
                if (id(Path.GetFileName(f)) is int v) used.Add(v / 10 * 10);
        }
        static int? Num(string s, int at, int len) => s.Length >= at + len && int.TryParse(s.AsSpan(at, len), NumberStyles.None, CultureInfo.InvariantCulture, out int v) ? v : null;
        Mark("sound", "frpg2_c*.bnd", f => Num(f, 7, 4));
        Mark(Path.Combine("model", "chr"), "c*.bnd", f => Num(f, 1, 4));
        Mark(Path.Combine("morpheme4", "chr"), "c*.anibnd.dcx", f => Num(f, 1, 4));
        Mark(Path.Combine("timeact", "chr"), "c*_pl.tae", f => Num(f, 1, 4));
        return Enumerable.Range(100, 900).Select(x => x * 10).Where(d => !used.Contains(d) && d != 2250 && BankChecks(ds2Data, d).Count == 0)
            .OrderBy(d => Math.Abs(d - near)).ThenBy(d => d).Take(count).ToList();
    }

    /// <summary>Rule S5: the name a DS1 common sound gets in the character's project: c{DS1 chr}9{last 4 digits of the DS1
    /// id} (c999999999 → c250099999, c000004700 → c250094700, s000007701 → c250097701).</summary>
    public static string CommonName(int ds1Chr, int id) => $"c{ds1Chr:D4}9{id % 10000:D4}";

    /// <summary>The character's DS2 bank (rules S1, S2, S5, S9).</summary>
    public sealed class BankResult
    {
        public byte[] Bank = [];
        /// <summary>title/sound/&lt;bank&gt;.fsb for every wave bank the project names.</summary>
        public Dictionary<string, byte[]> Loose = [];
        /// <summary>DS1 common sound (type, id) → the id of its copy in the character's project (rule S5).</summary>
        public Dictionary<(int Type, int Id), int> Commons = [];
        /// <summary>Character-range event names the bank's project defines: DS1's own project + the character-range events copied
        /// from frpg_main (merge9 stage 4, rule S5b); other character-range TAE ids are not emitted (<see cref="Translate"/>).</summary>
        public HashSet<string> OwnEvents = [];
        public byte[] Fev = [];
        public List<string> Report = [];
    }

    /// <summary>
    /// Rules S1/S1b/S2/S5/S9/S10: the character's DS2 bank sound/frpg2_cBBBB.bnd (BND4 + DCX like the template's vanilla
    /// bank, entry flags as there; entries named ...\frpg2_cBBBB\frpg2_cBBBB.fev / .fsb after the project, as vanilla names
    /// its fsb entry ...\frpg2_c1\frpg2_c1030.fsb after its fevbnd project; the loader reads entries by id): id 0 =
    /// DS1's frpg_cNNNN.fev extended with the DS1 common sounds the character's DS1 TAE names anywhere (copied from DS1's
    /// frpg_main.fev, <see cref="Fev1Project.AddEvents"/>) and renamed to the resource name frpg2_cBBBB (S1b), id 1 = DS1's
    /// frpg_cNNNN.fsb; every wave bank the project names loose (title/sound/&lt;bank&gt;.fsb), each FSB4 hash equal to the
    /// project's bank entry (what FMOD verifies when it opens the bank). Depends only on the DS1 character, the decade and the
    /// template's bank format, so every 1:1 build of one DS1 character in one decade gets the same bytes (S10).
    /// </summary>
    public static BankResult Bank(string dsrRoot, string ds2Data, string ds1Chr, int bankChr, string templateChr, TAE? ds1Tae)
    {
        var res = new BankResult();
        string snd = Path.Combine(dsrRoot, "sound"), proj = $"frpg_{ds1Chr}";
        int chr = int.Parse(ds1Chr[1..], CultureInfo.InvariantCulture);
        byte[] fev = File.ReadAllBytes(Path.Combine(snd, proj + ".fev"));
        var own = Fev1Project.Read(fev);
        var ownNames = own.Events.Select(e => e.Name).ToHashSet();
        bool OwnRange(int type, int id) => type == 1 && id / 100000 == chr;
        // DS1 common sounds the character's TAE names (any animation): copy those frpg_main defines. merge9 stage 4: also the
        // character-range ids DS1 keeps in frpg_main rather than in the character's project (frpg_main defines 140 such
        // events, e.g. c254000000 /frpg_main/body-hauberk2540, c255000000 body-armor2550, which DS1 c2540 / c2550 play on
        // most clips): copied under their own DS1 name, so the TAE keeps naming them by DS1's id (rule S5b)
        var main = Fev1Project.Read(File.ReadAllBytes(Path.Combine(snd, "frpg_main.fev")));
        var smain = Fev1Project.Read(File.ReadAllBytes(Path.Combine(snd, "frpg_smain.fev")));
        var addSmain = new List<(string Source, string NewName)>();
        var used = (ds1Tae?.Animations ?? []).SelectMany(Sounds).Select(s => Ds1Event(s.Type, s.Id)).Where(e => !OwnRange(e.Type, e.Id) || !ownNames.Contains($"c{e.Id:D9}"))
            .Distinct().OrderBy(x => x.Type).ThenBy(x => x.Id).ToList();
        var add = new List<(string Source, string NewName)>();
        var taken = new HashSet<string>(own.Events.Select(e => e.Name));
        res.OwnEvents = [.. ownNames];
        foreach (var (type, id) in used)
        {
            bool ownRange = OwnRange(type, id);
            string src = $"{(type is >= 0 and < 12 ? Ds1TypeLetters[type] : '?')}{id:D9}", nn = ownRange ? src : CommonName(chr, id);
            string what = ownRange ? $"DS1 character-range {src} (not in {proj})" : $"DS1 common {src}";
            // session 11 (snd11), rule S5d: DS1 resolves a TAE sound in every loaded project, so a common frpg_main lacks comes
            // from DS1's global stream project frpg_smain (the boss kill sting s777777777 on the Asylum Demon's death)
            var from = main.EventByName(src) != null ? main : smain.EventByName(src) != null ? smain : null;
            var ev = from?.EventByName(src);
            if (from == null || ev == null) { res.Report.Add($"{what}: not in frpg_main or frpg_smain (silent in DS1 too)"); continue; }
            var layered = from.SoundDefOf(ev) < 0 ? from.LayerSoundDefOffsets(ev) : null;
            if (from.SoundDefOf(ev) < 0 && layered == null) { res.Report.Add($"{what}: neither a simple nor a layered event in {from.Name} (rule S5c), not copied (silent here; DS1 plays it)"); continue; }
            if (!taken.Add(nn)) { res.Report.Add($"{what}: name {nn} already used in {proj}, not copied"); continue; }
            if (layered != null) res.Report.Add($"{what}: {layered.Count}-layer event copied with every layer's sound definition (rule S5c: {string.Join(" + ", layered.Select(o => from.SoundDefs[System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(from.Bytes.AsSpan(o))].Name))})");
            if (from == smain) { addSmain.Add((src, nn)); res.Report.Add($"{what}: copied from frpg_smain (rule S5d; stream bank frpg_smain loose)"); }
            else add.Add((src, nn));
            if (ownRange) res.OwnEvents.Add(nn);
            else res.Commons[(type, id)] = int.Parse(nn[1..], CultureInfo.InvariantCulture);
        }
        if (add.Count > 0) fev = own.AddEvents(main, add);
        if (addSmain.Count > 0) fev = Fev1Project.Read(fev).AddEvents(smain, addSmain); // rule S5d
        var ext = Fev1Project.Read(fev);
        var (a, b) = Fev1Project.Counters(ext.SoundDefs);
        if (ext.Header != (a, b) || ext.Events.Count != own.Events.Count + add.Count + addSmain.Count) throw new InvalidDataException($"{proj}: extended project inconsistent");
        add.AddRange(addSmain);
        // rule S1b: the project carries the bank's resource name (DS2 de-duplicates / releases banks by that name)
        string bankName = BankName(bankChr);
        fev = Fev1Project.WithName(fev, bankName);
        res.Fev = fev;
        // wave banks: every one the project names, loose; the character's own (else the last listed) also as id 1
        var heads = Fev1Header(fev).Banks;
        foreach (var (bank, hash) in heads)
        {
            byte[] fsb = File.ReadAllBytes(Path.Combine(snd, bank + ".fsb"));
            if (Fsb4Hash(fsb) != hash) throw new InvalidDataException($"{bank}.fsb: FSB4 hash {Fsb4Hash(fsb):X16} != FEV bank hash {hash:X16}");
            res.Loose[$"sound/{bank}.fsb"] = fsb;
        }
        string idOne = heads.Any(x => x.Name == proj) ? proj : heads[^1].Name;
        byte[] ownFsb = res.Loose[$"sound/{idOne}.fsb"];
        byte[] raw = File.ReadAllBytes(Path.Combine(ds2Data, "sound", $"frpg2_{templateChr}.bnd"));
        DCX.CompressionInfo? comp = null;
        if (DCX.Is(raw)) { raw = DCX.Decompress(raw, out DCX.CompressionInfo ci); comp = ci; }
        var bnd = BND4.Read(raw);
        var flags = bnd.Files[0].Flags;
        bnd.Files.Clear();
        bnd.Files.Add(new BinderFile(flags, 0, $@"N:\FRPG2_64\SoundOutTemp\win64\{bankName}\{bankName}.fev", fev));
        bnd.Files.Add(new BinderFile(flags, 1, $@"N:\FRPG2_64\SoundOutTemp\win64\{bankName}\{bankName}.fsb", ownFsb));
        byte[] outBnd = bnd.Write();
        res.Bank = comp != null ? DCX.Compress(outBnd, comp) : outBnd;
        res.Report.Insert(0, $"sound bank {bankName}.bnd (animId/100 rounded to tens) = id 0 DS1 {proj}.fev as FMOD project {bankName} (rule S1b) ({own.Events.Count} events + {add.Count} DS1 common events copied from frpg_main: {string.Join(", ", add.Select(x => $"{x.Source}→{x.NewName}"))}; {ext.SoundDefs.Count} sound definitions, banks {string.Join(", ", heads.Select(h => h.Name))}, A/B {a}/{b}; {fev.Length:N0} B) + id 1 {idOne}.fsb ({ownFsb.Length:N0} B); loose {string.Join(", ", res.Loose.Keys.Select(k => "title/" + k))} (FSB4 hashes = the project's bank entries)");
        return res;
    }

    /// <summary>The FMOD event names defined in a DS1 FEV1 project (length-prefixed letter + 9 digits), for checks.</summary>
    public static HashSet<string> Fev1EventNames(byte[] fev)
    {
        var set = new HashSet<string>();
        for (int o = 0; o + 15 <= fev.Length; o++)
        {
            if (fev[o] != 11 || fev[o + 1] != 0 || fev[o + 2] != 0 || fev[o + 3] != 0 || fev[o + 14] != 0) continue;
            byte l = fev[o + 4];
            if (l < 'a' || l > 'z') continue;
            bool digits = true;
            for (int k = 5; k < 14 && digits; k++) digits = fev[o + k] >= '0' && fev[o + k] <= '9';
            if (digits) set.Add(System.Text.Encoding.ASCII.GetString(fev, o + 4, 10));
        }
        return set;
    }
}
