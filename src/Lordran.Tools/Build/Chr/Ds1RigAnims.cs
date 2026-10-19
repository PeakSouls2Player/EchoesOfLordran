using System.Globalization;
using Lordran.Tools.Formats;
using Lordran.Tools.Formats.Morpheme;
using SoulsFormats;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// The animation half of a 1:1 DS1 character (session 8, track chr step 2): a DS2 Morpheme network (template c1030,
/// Hollow Infantry) driving DS1's own rig, every clip slot filled with a DS1 clip by role, plus the TimeAct files.
/// Nothing of DS2's motion stays on DS1 bones: a slot without a DS1 counterpart gets a DS1 idle, the nearest DS1 role, or
/// (additive slots) DS1's bind pose.
/// <list type="number">
/// <item><b>Network</b>: c1030's NMB with the DS1 rig (Ds1AnimBridge layout, 52 + 3 = 55 bones = c1030's count) put in by
/// <see cref="NmbRigReplace.ReplaceMapped"/>.</item>
/// <item><b>Clips</b>: each NSA keeps its file name (the network's file-name table and SourceAnim attribs refer to it);
/// its content becomes the role's DS1 clip bridged and encoded at DS1's 30 fps (<see cref="Roles"/>).</item>
/// <item><b>SourceAnim attribs</b> (<see cref="NmbNetwork"/>): duration and trajectory start/end of the new clip, read back
/// from our encoded NSA (what the runtime decodes).</item>
/// <item><b>Event tracks</b>: fractions of the clip, so they scale with the DS1 duration. Fix pass (session 8), tracks are
/// identified by what the state machines test, not by name (<see cref="KindOf"/>). From the DS1 motion (rule C11,
/// <see cref="FootEvents"/>): LeftSteps/RightSteps (userData 10/11, LOCK_FOOT_CHAIN's footfall ids) = windows in which
/// DS1's ball joint stays within 15 mm of its touchdown position; the sync track (SourceAnim +0x8C) partitions the clip
/// with no gap, one event per touchdown L R L R on locomotion loops (which start at their left touchdown,
/// <see cref="PhaseShift"/>), one event elsewhere. From the DS1 TAE (rule C12, <see cref="Gameplay"/>): Death State /
/// Throw Death Transition → DieControl, AI Movement Cancel → DamageDuration end and earliest return to base, Ignore
/// Gravity → Gravity; no-attack slots lose every attack-family window and return to base at once. Shared tracks are
/// merged (<see cref="Merge"/>). Other tracks keep DS2's fractions (rule P).</item>
/// <item><b>TimeAct</b> (<see cref="EditTae"/>): per TAE animation (= the TimeAct track's value) the length field becomes
/// DS1's frames at 30 fps; _pl events are time-scaled by the duration ratio, or dropped on no-attack slots; _snd/_sfx
/// events are the DS1 clip's own sound / effect events (track chrsound, <see cref="Ds1ChrSound"/>: DS1 TAE 128/129/130 →
/// DS2 201 naming DS1's FMOD events, DS1 FFX → DS2 2100 (one-shot) / 2104 (for the event's span) where a DS2 effect is
/// mapped, rule S7).</item>
/// </list>
/// </summary>
public static class Ds1RigAnims
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>One clip-slot rule: DS2 slot (NSA name prefix "aXX_YY_ZZZZ"; "a65_" = every slot starting so) → DS1
    /// animation ids in preference order (the first the DS1 anibnd has wins; -1 = DS1 bind pose, a neutral additive),
    /// the role, V/P/U status and the evidence. NoAttack: the slot's attack windows are cleared and its state returns to
    /// base at once (rule C12c). Loop (fix pass): a locomotion loop the network sync-blends with the other loops (c1030
    /// BlendN 158-187, 336-365, 717-746): the DS1 clip is started at its left-foot touchdown and its sync track holds one
    /// event per touchdown, L R L R (rule C11b). SyncEvents (fix pass): n equal sync events, for the non-stepping slots
    /// that DS2 blends with the loops (WalkStay and its turns: c1030 BlendN 746/785, where DS2 gives them the loops'
    /// event count too).</summary>
    public sealed record Role(string Slot, int[] Ds1, string Name, string Status, string Evidence, bool NoAttack = false, bool Loop = false, int SyncEvents = 0);

    /// <summary>Sync events of the walk-state idle / turn slots = the DS1 loops' count: every c2500 locomotion loop
    /// (a00_0200-0203, a00_0500) holds two gait cycles = 4 touchdowns (steps.txt). Session 8 (track enemies): a role's
    /// <c>SyncEvents</c> equal to this marker means "as many as the character's own loops" (DS1 c2540's loops hold one gait
    /// cycle = 2 touchdowns), resolved per character from its walk-forward loop (<see cref="LoopTouchdowns"/>); for c2500 it is
    /// 4, so the unarmed Hollow's output is unchanged.</summary>
    const int LoopSync = 4;

    const string MoveParam = "DS1 MoveParam 100 (NpcParam 250020 moveAnimId 100): walkF 200, walkB 201, walkL 202, walkR 203, dashF 500, dashB 501, dashL 502, dashR 503, turnL 602, turnR 603, largeTurnL 604, largeTurnR 605";

    /// <summary>
    /// c1030 network (DS2 Hollow Infantry) slots → DS1 Asylum Hollow roles for the unarmed variant (DS1 NpcParam 250020,
    /// think 250006 → battle goal 250004: never attacks, DS1-AI-NOTES.md). Durations/root motion: clip-survey output in
    /// NOTES.md. Directions in Morpheme space (+X = the character's left, forward −Y).
    /// </summary>
    public static readonly Role[] HollowUnarmed =
    [
        new("a00_00_0000", [0], "idle", "V", "DS1 idle a00_0000 (MoveParam stayId 0 = default idle; 2.0 s loop, in place)"),
        new("a00_00_7001", [0], "idle (weapon style 1)", "V", "same role; DS1 has one idle"),
        new("a00_00_7002", [0], "idle (weapon style 2)", "V", "same role"),
        new("a00_00_4000", [2250, 0], "ragdoll loop pose", "P", "DS1 a00_2250: 2-frame lying pose with TAE 228 Ragdoll Revive / 233 draw mask (the dead state); DS2 RagdollLoop is 2 frames"),
        new("a00_01_0010", [0], "walk-state idle", "P", "DS2 WalkStay = standing in the walk state, in place; DS1 has no such clip → idle", SyncEvents: LoopSync),
        new("a00_01_7011", [0], "walk-state idle", "P", "as a00_01_0010", SyncEvents: LoopSync),
        new("a00_01_7012", [0], "walk-state idle", "P", "as a00_01_0010", SyncEvents: LoopSync),
        new("a00_01_0011", [603], "turn right 90", "V", MoveParam + "; DS2 clip yaw −90°, DS1 a00_0603 yaw −90° (bridged)", SyncEvents: LoopSync),
        new("a00_01_0012", [605], "turn right 180", "V", MoveParam + "; DS2 yaw −180°, DS1 a00_0605 −180°", SyncEvents: LoopSync),
        new("a00_03_0010", [200], "walk forward", "V", MoveParam + "; DS2 −1.60 m/2.0 s, DS1 −2.36 m/2.67 s", Loop: true),
        new("a00_03_7001", [200], "walk forward", "V", "as a00_03_0010", Loop: true),
        new("a00_03_7002", [200], "walk forward", "V", "as a00_03_0010", Loop: true),
        new("a00_03_0040", [0], "walk to idle", "P", "no DS1 stop clip → idle"),
        new("a00_03_7041", [0], "walk to idle", "P", "as a00_03_0040"),
        new("a00_03_7042", [0], "walk to idle", "P", "as a00_03_0040"),
        new("a00_03_0071", [605], "walk turn 180 (right)", "V", MoveParam + "; DS2 yaw −180°"),
        new("a00_03_7071", [604], "walk turn 180 (left)", "V", MoveParam + "; DS2 yaw +173°"),
        new("a00_03_7072", [604], "walk turn 180 (left)", "V", "as a00_03_7071"),
        new("a00_04_0010", [500], "run forward", "V", MoveParam + "; DS2 jog −3.52 m/1.33 s, DS1 run −4.27 m/1.47 s", Loop: true),
        new("a00_04_7001", [500], "run forward", "V", "as a00_04_0010", Loop: true),
        new("a00_04_7002", [500], "run forward", "V", "as a00_04_0010", Loop: true),
        new("a00_04_0071", [605], "run turn (right)", "P", "DS2 yaw −180°; DS1 has no run turn → largeTurnR"),
        new("a00_04_7071", [602], "run turn (left)", "P", "DS2 yaw +86° → turnL 90"),
        new("a00_04_7072", [602], "run turn (left)", "P", "as a00_04_7071"),
        new("a00_06_0011", [200], "strafe-set walk forward", "V", MoveParam, Loop: true),
        new("a00_06_0012", [201], "walk back", "V", MoveParam + "; DS2 +1.60 m, DS1 +1.97 m", Loop: true),
        new("a00_06_0013", [202], "walk left", "V", MoveParam + "; DS2 +0.94 m X, DS1 +1.82 m X", Loop: true),
        new("a00_06_0014", [203], "walk right", "V", MoveParam + "; DS2 −0.91 m X, DS1 −2.34 m X", Loop: true),
        new("a00_06_7011", [200], "walk forward", "V", MoveParam, Loop: true), new("a00_06_7012", [201], "walk back", "V", MoveParam, Loop: true),
        new("a00_06_7013", [202], "walk left", "V", MoveParam, Loop: true), new("a00_06_7014", [203], "walk right", "V", MoveParam, Loop: true),
        new("a00_06_7021", [200], "walk forward", "V", MoveParam, Loop: true), new("a00_06_7022", [201], "walk back", "V", MoveParam, Loop: true),
        new("a00_06_7023", [202], "walk left", "V", MoveParam, Loop: true), new("a00_06_7024", [203], "walk right", "V", MoveParam, Loop: true),
        new("a00_07_0011", [500], "run forward (strafe set)", "V", MoveParam, Loop: true),
        new("a00_07_0012", [501, 201], "run back", "P", MoveParam + "; c2500 has no 501 → walkB", Loop: true),
        new("a00_07_0013", [502, 202], "run left", "P", MoveParam + "; c2500 has no 502 → walkL", Loop: true),
        new("a00_07_0014", [503, 203], "run right", "P", MoveParam + "; c2500 has no 503 → walkR", Loop: true),
        new("a00_07_7011", [500], "run forward", "V", MoveParam, Loop: true), new("a00_07_7012", [501, 201], "run back", "P", MoveParam, Loop: true),
        new("a00_07_7013", [502, 202], "run left", "P", MoveParam, Loop: true), new("a00_07_7014", [503, 203], "run right", "P", MoveParam, Loop: true),
        new("a00_07_7021", [500], "run forward", "V", MoveParam, Loop: true), new("a00_07_7022", [501, 201], "run back", "P", MoveParam, Loop: true),
        new("a00_07_7023", [502, 202], "run left", "P", MoveParam, Loop: true), new("a00_07_7024", [503, 203], "run right", "P", MoveParam, Loop: true),
        new("a00_08_", [0], "weapon change", "P", "unarmed: no DS1 weapon change → idle"),
        new("a00_09_7003", [9900, 0], "guard idle", "P", "DS1 MoveParam 102 Guard stayId 9900 (c2500 has none) → idle"),
        new("a00_09_7013", [2000], "guard hit light", "P", "no DS1 guard → small damage a00_2000"),
        new("a00_09_7023", [2004, 2000], "guard hit heavy", "P", "no DS1 guard → medium damage"),
        new("a00_13_0020", [1500], "jump takeoff", "P", "DS1 a00_1500: airborne loop (feet ≥ 0.24 m up, root 1.04 m)"),
        new("a00_13_0021", [1500], "jump takeoff", "P", "as a00_13_0020"),
        new("a00_13_0040", [1750], "landing", "P", "DS1 a00_1750: from the a00_1500 pose to standing, 1.17 s; DS2 JumpAttack (AttackDuration 0-1): no attack", NoAttack: true),
        new("a00_14_0020", [1751, 1750], "heavy landing", "P", "DS1 a00_1751: same start/end, 1.83 s"),
        new("a00_16_0015", [2005, 2000], "light hit (leans right-forward)", "P", "DS2 head lean (−0.30, −0.28); DS1 a00_2005 leans −X (right)"),
        new("a00_16_0016", [2004, 2000], "light hit from behind", "P", "DS2 lean (−0.22, −0.61) forward; DS1 a00_2004 leans forward"),
        new("a00_16_0025", [2000], "light hit from the front", "P", "DS2 lean (0.24, 0.20) back; DS1 a00_2000 leans back"),
        new("a00_16_0055", [2020, 2014, 2010], "heavy hit, knocked back", "P", "DS2 pushed back 4.1 m; DS1 a00_2020 knocked back 3 m, down and up"),
        new("a00_16_0065", [2020, 2014, 2010], "heavy hit, knocked back", "P", "as a00_16_0055"),
        new("a00_16_0056", [2040, 2004], "heavy hit from behind", "P", "DS2 pushed forward 3.5 m; DS1 a00_2040 falls forward and gets up"),
        new("a00_16_0115", [2010], "extra-heavy hit (front)", "P", "DS2 leans back, 3 s in place; DS1 a00_2010 large reaction, leans back"),
        new("a00_16_0125", [2010], "extra-heavy hit (front)", "P", "as a00_16_0115"),
        new("a00_16_0116", [2007, 2004], "extra-heavy hit (behind)", "P", "DS2 leans forward; DS1 a00_2007 forward-down"),
        new("a00_16_0155", [2014, 2020], "extra-heavy knock-back", "P", "DS2 +4.1 m back; DS1 a00_2014 +6 m back, down and up"),
        new("a00_16_0165", [2014, 2020], "extra-heavy knock-back", "P", "as a00_16_0155"),
        new("a00_16_0156", [2040], "extra-heavy from behind", "P", "DS2 −3.9 m forward; DS1 a00_2040"),
        new("a00_16_0210", [2200], "blown down face down", "P", "DS1 a00_2200: falls forward (head −1.34 m), face down (TAE: Death State flag, motion only used)"),
        new("a00_16_0220", [2202], "blown down face up", "P", "DS1 a00_2202: falls back (head +1.56 m), face up"),
        new("a00_16_0610", [2204], "fall face down (start)", "P", "DS1 a00_2204: falls forward, face down"),
        new("a00_16_0630", [2205], "fall face up (start)", "P", "DS1 a00_2205: falls back, face up"),
        new("a00_16_0710", [1510], "down (start)", "P", "DS1 a00_1510: lying loop pose (root 0.10 m, loop 0°)"),
        new("a00_16_0740", [1510], "down (start)", "P", "as a00_16_0710"),
        new("a00_16_0720", [1510], "down loop", "P", "DS1 a00_1510 lying loop"),
        new("a00_16_0750", [1510], "down loop", "P", "as a00_16_0720"),
        new("a00_16_0730", [1760], "get up", "P", "DS1 a00_1760: lying → standing, 4.33 s"),
        new("a00_16_0760", [1760], "get up", "P", "as a00_16_0730"),
        new("a00_16_0810", [7231, 2000], "ladder damage", "P", "DS1 a00_7231: ladder-set clip, 0.67 s, TAE 225/226 like the ladder idles 7230/7240"),
        new("a00_16_1010", [2100], "parried", "V", "DS1 a00_2100 TAE 67 Add SpEffect 30 = SpEffectParam 30 'Anim - Parry'"),
        new("a00_16_1020", [0], "parried loop", "P", "DS1's parried clip ends standing (2.6 s) → idle"),
        new("a00_16_1030", [0], "parried end", "P", "as a00_16_1020"),
        new("a00_16_1110", [2010], "bleed start", "P", "no DS1 bleed reaction → large hit"),
        new("a00_16_1120", [0], "bleed loop", "P", "→ idle"),
        new("a00_16_1130", [2204], "bleed end (falls)", "P", "DS2 ends lying; DS1 a00_2204 falls forward"),
        new("a00_16_1230", [2004], "stagger from behind", "P", "DS2 leans forward; DS1 a00_2004"),
        new("a00_16_70", [3500, 0], "attack rebound", "P", "DS1 a00_3500: 1.23 s from a non-idle pose, TAE JumpTable 41 'Vulnerable'; unarmed never attacks"),
        new("a00_17_0010", [2200], "death (falls forward)", "P", "DS1 a00_2200 TAE JumpTable 12 'Death State'; DS2 DieNormalSlash_F falls forward (head −1.89 m, face down) like 2200 (−1.34 m)"),
        new("a00_17_0020", [2204], "death (falls forward 2)", "P", "DS1 a00_2204 Death State; forward, face down"),
        new("a00_17_0610", [2202], "ladder death (falls back)", "P", "DS1 a00_2202 Death State; falls back, face up (DS2 DieLadder +1.28 m back)"),
        new("a00_50_", [-1], "additive (lean / tire)", "P", "DS2 additive clips equal the rig bind pose on every untouched channel (c1030 AdditiveTurn: 0.0° on 52 channels) → DS1 bind pose = no additive change"),
        new("a65_", [0], "attack / AI action", "V", "unarmed Asylum Hollow never attacks (DS1 battle goal 250004: Wait / SidewayMove / LeaveTarget) → idle, attack windows cleared", NoAttack: true),
        new("a71_01_0010", [7210], "ladder start up", "P", "DS1 a00_7210: rises 0.58 m (DS2 0.42 m)"),
        new("a71_01_0020", [7220], "ladder start down", "P", "DS1 a00_7220: −0.95 m forward, −1.43 m, yaw −174° (DS2 −1.50, −1.58, −180°)"),
        new("a71_01_0030", [7213], "ladder end up", "P", "DS1 a00_7213: −0.93 m forward, +0.80 m (DS2 −1.14, +1.58)"),
        new("a71_01_0050", [7223], "ladder end down", "P", "DS1 a00_7223: −0.55 m"),
        new("a71_02_0010", [7211], "ladder move up", "P", "DS1 a00_7211: +0.50 m in 0.4 s (DS2 +0.40 m in 0.33 s)"),
        new("a71_02_0020", [7230], "ladder idle", "P", "DS1 a00_7230: 3 s in place, ladder TAE flags"),
        new("a71_02_0030", [7221], "ladder move down", "P", "DS1 a00_7221: −0.50 m in 0.4 s"),
        new("a71_02_0050", [7233], "ladder fall", "P", "DS1 a00_7233: feet drop 0.9 m"),
        new("a71_02_0070", [7201], "ladder fall loop", "P", "DS1 a00_7201: −0.5 m in 0.1 s"),
        new("a71_02_0080", [7202], "ladder fall stop", "P", "DS1 a00_7202"),
        new("a71_02_0090", [1751], "ladder fall landing", "P", "DS1 heavy landing"),
        new("a71_02_0100", [7250], "ladder release", "P", "DS1 a00_7250: +0.37 m back, −0.54 m"),
        new("a71_03_", [7230], "ladder attack", "P", "unarmed → ladder idle, attack windows cleared", NoAttack: true),
        new("a75_01_7100", [9000, 0], "dormant sitting loop", "P", "DS1 a00_9000: sitting event loop (the census' passive Hollows use 9000/9005/9006/9008/9010)"),
        new("a75_01_7101", [1771, 1770], "wake from sitting", "P", "DS1 a00_1771: crouch → standing, 3.67 s"),
        // session 11 (burgchr11), rule P3b (asy10 P3 step 1, "the template's free categories first"): start-up types 3 / 4 held DS1's
        // lying loop a00_1510, which no DS1 part starts in; they now hold the pose clips a00_9005 / a00_9006 (no JumpTable window,
        // whole-length TAE 66; Asylum corridor/cell Hollows, Burg 1010113-class Hollows) with the sitting exit a00_1771 (DS1 has
        // three get-ups 1760/1770/1771; 1771 = the 9000 pose's exit, P3a). 9008 / 9010 / the action clip 9013 need added
        // categories (open: no free slot left).
        new("a75_01_7110", [9005, 1510], "dormant pose loop (start-up type 3)", "P", "DS1 a00_9005 (pose, 12.9 s)"),
        new("a75_01_7111", [1771, 1760], "wake from pose", "P", "DS1 a00_1771: crouch → standing"),
        new("a75_01_7120", [9006, 1510], "dormant pose loop (start-up type 4)", "P", "DS1 a00_9006 (pose, 12.9 s)"),
        new("a75_01_7121", [1771, 1760], "wake from pose", "P", "as a75_01_7111"),
        // session 11 (chrai11), rule P3 (E2b + I1b): start-up types 5 / 6 / 7 added by NmbNetClone (clones of type 4's state in every
        // weapon style's start-up SM, entered on StartupType 5 / 6 / 7) for the DS1 poses no template slot holds: the crouching /
        // sitting Hollows (9008 / 9010) and the dropping ones (9013: waiting above; their DS1 cancel animation 9060 = the drop, 11015250)
        new("a75_01_7130", [9008, 1510], "dormant pose loop (start-up type 5, rule P3)", "P", "DS1 a00_9008 (crouched)"),
        new("a75_01_7131", [1771, 1760], "wake from pose", "P", "as a75_01_7111"),
        new("a75_01_7140", [9010, 1510], "dormant pose loop (start-up type 6, rule P3)", "P", "DS1 a00_9010 (sitting)"),
        new("a75_01_7141", [1771, 1760], "wake from pose", "P", "as a75_01_7111"),
        new("a75_01_7150", [9013, 1510], "dormant drop pose (start-up type 7, rule P3)", "P", "DS1 a00_9013: waiting above"),
        new("a75_01_7151", [9060, 1771], "drop and land (wake, start-up type 7)", "P", "DS1 a00_9060: the drop 11015250 sets as the cancel animation"),
        new("a79_02_", [2500], "backstab victim", "V", "DS1 ThrowParam 12500 'Backstab - PC -> Hollow' defAnimId 2500"),
        new("a79_03_0010", [2510], "riposte victim (after parry)", "V", "DS1 ThrowParam 22500 'Parry - PC -> Hollow' defAnimId 2510"),
        new("a79_04_0010", [2510], "riposte victim (guard break)", "P", "DS1's riposte victim clip"),
    ];

    /// <summary>DS2 clip name "aXX_YY_ZZZZ_..." → its animation id XX·1000000 + YY·10000 + ZZZZ (the TAE id its TimeAct
    /// track carries: a00_03_0010 → 30010, a65_10_1010 → 65101010; c1030 timeact ids), -1 if not of that form.</summary>
    public static int NameId(string name) =>
        name.Length >= 11 && name[0] == 'a' && int.TryParse(name[1..3], out int xx) && int.TryParse(name[4..6], out int yy) && int.TryParse(name[7..11], out int zz)
            ? xx * 1000000 + yy * 10000 + zz : -1;

    public static Role? RoleFor(Role[] roles, string clipName) =>
        roles.Where(r => clipName.StartsWith(r.Slot, StringComparison.OrdinalIgnoreCase)).OrderByDescending(r => r.Slot.Length).FirstOrDefault();

    /// <summary>DS2 template bones without a namesake in the (DS2-named) DS1 rig → rig bone for index fields and bone lists
    /// (humanoid c1030 rig → DS1 c2500-style rig: same chain position).</summary>
    public static readonly Dictionary<string, string> Ds2ToDs1Fallback = new()
    {
        ["L_Finger02"] = "L_Finger0Nub", ["L_Finger12"] = "L_Finger1Nub", ["L_Finger22"] = "L_Finger2Nub",
        ["R_Finger02"] = "R_Finger0Nub", ["R_Finger12"] = "R_Finger1Nub", ["R_Finger22"] = "R_Finger2Nub",
        ["Bow_02"] = "bow_string", ["Bow_03"] = "bow_string", ["Bow_04"] = "bow_string",
        ["Bow_05"] = "bow_string", ["Bow_06"] = "bow_string", ["Bow_07"] = "bow_string",
    };

    /// <summary>
    /// DS1 bone name → the name DS2's engine looks up (rule, session 8). DS2's executable searches bones by UTF-16 name:
    /// "R_Hand", "L_Weapon", "L_Hand", "R_Weapon" (0x10bea80..), "Head", "L_UpperArm", "R_UpperArm", "Spine", "L_Calf",
    /// "L_Foot", "R_Calf", "R_Foot" (0x10bec40..), "Trajectory" then "Master" (0x10e0228), "Pelvis", "Spine1"; DS1 writes
    /// "L UpperArm". So: "L " / "R " prefixes become "L_" / "R_" (DS2's own names for the same Biped bones; LCalfTwist
    /// and the toe nubs already match DS2's), DS1's character root Master (pelvis height, like DS2's Root at 0.887 m) is
    /// "Root", the bridge's trajectory bone is DS2's "Master", DS1's weapon bones sword / bpw_00 / seeld are DS2's
    /// R_Weapon / L_Weapon / L_shield. Hierarchy, bind pose and motion stay DS1's.
    /// </summary>
    /// <para>Session 8 (track enemies): DS1 c2900/c2910's character root is "master" (lower case) and c2550's right weapon bone
    /// "R_weapon"; DS2 looks bones up by exact name, so they become "Root" / "R_Weapon" like the others (R_weapon2/3 stay).</para>
    public static string Ds2BoneName(string ds1) => ds1 switch
    {
        "Master" or "master" => "Root",
        "R_weapon" => "R_Weapon",
        "Trajectory" => "Master",
        "sword" => "R_Weapon",
        "bpw_00" => "L_Weapon",
        "seeld" => "L_shield",
        // DS1 c2232 (session 8, track boss): DS2 looks up "R_Weapon" (case as in the exe), so c2232's "R_weapon" (arm above, shared
        // with the enemies track's c2550) and "L_weapon" get DS2's case
        "L_weapon" => "L_Weapon",
        // session 11 (burgchr11): DS1 c2570 (Heavy Knight) names its weapon bone "R weapon" (space, lower case): the generic L/R rule
        // below would give "R_weapon", which DS2's exact-name lookup of "R_Weapon" misses
        "R weapon" => "R_Weapon", "L weapon" => "L_Weapon",
        _ when ds1.Length > 2 && (ds1[0] == 'L' || ds1[0] == 'R') && ds1[1] == ' ' => $"{ds1[0]}_{ds1[2..]}",
        _ => ds1,
    };

    /// <summary>The bridged DS1 rig with DS2 bone names (<see cref="Ds2BoneName"/>).</summary>
    public static MorphemeSkeleton WithDs2Names(MorphemeSkeleton s) => new()
    {
        Names = s.Names.Select(Ds2BoneName).ToList(), Parents = [.. s.Parents], BindPos = s.BindPos, BindRot = s.BindRot,
        Traj = s.Traj, Root = s.Root, AnimToRig = s.AnimToRig,
    };

    public sealed class ClipInfo
    {
        public string Name = "", Role = "", Status = "";
        public int Ds1Id;
        public double Ds2Duration, Ds1Duration;
        public V3 Ds2RootEnd, Ds1RootEnd;
        public int Frames; public double Fps;
        public List<int> TimeAct = [];
        public bool NoAttack;
        public string Steps = "";
        /// <summary>DS1 TAE events of the source clip (types, JumpTable ids), for the role table.</summary>
        public string Ds1Tae = "";
        /// <summary>DS1 TAE JumpTable 12 "Death State" (death clips) or 69 "Throw Death Animation Transition" (throw-victim
        /// clips) start as a fraction of the DS1 clip (-1: none) → DS2 DieControl (rule C12a).</summary>
        public double DeathState = -1;
        /// <summary>DS1 TAE JumpTable 78 "AI Movement Anim Cancel" start (else 79 "AI Dodge Anim Cancel", else 23 "AI
        /// Continuation Attack Flag") as a fraction of the DS1 clip (-1: none) → end of DS2 DamageDuration, earliest DS2
        /// return-to-base (rule C12b).</summary>
        public double Cancel = -1;
        /// <summary>DS1 TAE JumpTable 27 "Ignore Gravity" windows (fractions) → DS2 Gravity 9610 windows (rule C12d).</summary>
        public List<(double S, double E)> NoGravity = [];
        /// <summary>Locomotion loop started at its left-foot touchdown (rule C11b): the frame of the DS1 clip it now starts at.</summary>
        public bool Loop; public int Shift;
        /// <summary>Role's equal-sync-event count (0 = automatic).</summary>
        public int SyncEvents;
        /// <summary>Every event track this slot rewrote: "name: DS2 events → ours (rule)".</summary>
        public List<string> TrackLog = [];
    }

    public sealed class Result
    {
        public byte[] Anibnd = [];
        /// <summary>The rig in the network (DS2 bone names); <c>Ds1Names</c>: DS1's name of each rig bone.</summary>
        public MorphemeSkeleton Rig = null!;
        public List<string> Ds1Names = [];
        public List<ClipInfo> Clips = [];
        public List<string> Report = [];
        /// <summary>TAE animation id → the clip slot that owns it (<see cref="TaeSlot"/>)</summary>
        public Dictionary<int, TaeSlot> TaeIds = [];
        /// <summary>The DS1 attack layer (track enemies; null for a character without attacks).</summary>
        public Ds1Attacks.Result? Attacks;
        /// <summary>Sync events of the DS1 locomotion loops (touchdowns of the walk-forward loop).</summary>
        public int LoopSync;
        /// <summary>Rule I1b: the network def's own asset id (0 = I1b not applied).</summary>
        public uint NetworkAssetId;
    }

    /// <summary>The clip slot owning a TAE animation id: DS1/DS2 durations, no-attack, and (track chrsound, rule S6) the
    /// DS1 clip whose TAE events the slot's _snd/_sfx events come from, the frame its loop was re-cut to start at (0 = not
    /// re-cut), its frame count and rate (-1 Ds1Id: bind-pose slot, no DS1 events).</summary>
    public sealed record TaeSlot(double Ds1, double Ds2, bool NoAttack, int Ds1Id, int Shift, int Frames, double Fps);

    /// <summary>Touchdowns (sync events) of a locomotion loop: the left-touchdown-started DS1 clip's plant-run starts on both
    /// feet (rule C11b).</summary>
    public static int LoopTouchdowns(NsaAnim loopStarted, MorphemePose pose) => FootEvents(loopStarted, pose, true, 0).Sync.Count;

    /// <summary>Static clip holding the rig's bind pose (neutral additive), round(duration·30)+1 frames at 30 fps.</summary>
    internal static NsaAnim BindClip(MorphemeSkeleton rig, double duration)
    {
        int nch = rig.Names.Count - 1, nf = Math.Max(2, (int)Math.Round(duration * 30) + 1);
        var a = new NsaAnim { Fps = 30, ChannelCount = nch, Pos = new V3[nch][], Rot = new Q4[nch][], RootPos = new V3[nf], RootRot = new Q4[nf], Duration = (nf - 1) / 30.0 };
        for (int c = 0; c < nch; c++)
        {
            int b = c + 1;
            var t = new V3(rig.BindPos[b][0], rig.BindPos[b][1], rig.BindPos[b][2]);
            var q = new Q4(rig.BindRot[b][3], rig.BindRot[b][0], rig.BindRot[b][1], rig.BindRot[b][2]);
            a.Pos[c] = Enumerable.Repeat(new V3(MorphemeMath.F32(t.X), MorphemeMath.F32(t.Y), MorphemeMath.F32(t.Z)), nf).ToArray();
            a.Rot[c] = Enumerable.Repeat(q, nf).ToArray();
        }
        for (int f = 0; f < nf; f++) { a.RootPos[f] = new V3(0.0, 0.0, 0.0); a.RootRot[f] = Q4.Identity; }
        return a;
    }

    /// <summary>Foot-plant thresholds (see steps-calib: best match with DS2's authored LeftSteps/RightSteps windows).</summary>
    public const double PlantSpeed = 0.35, PlantHeight = 0.05;

    // ------------------------------------------------------------------ foot-lock windows and cycles (fix pass, session 8)

    /// <summary>
    /// Largest drift (m) the locked joint may have inside a foot-lock window, measured from its position at the window start
    /// (DS2's LOCK_FOOT_CHAIN pins the joint where it was when the footfall event began). Calibrated with
    /// <c>steps-calib</c> on DS2's own authored LeftSteps/RightSteps windows (see NOTES.md rule C11).
    /// </summary>
    public const double LockTolerance = 0.015;
    /// <summary>Shortest foot-lock window in frames (0.2 s at 30 fps, inclusive); shorter plants (a foot shuffling in a
    /// fall or a throw) are not locked.</summary>
    public const int MinLockFrames = 6;
    /// <summary>The joint a foot-lock window keeps still: the chain's ball joint (LOCK_FOOT_CHAIN attrib 31 lists hip, knee,
    /// ankle, ball, toe = L_Thigh, L_Calf, L_Foot, L_Toe0, L_Toe0Nub in c1030 and in our c2501 network).</summary>
    public static string LockJointL => lockL ?? "L_Toe0";
    public static string LockJointR => lockR ?? "R_Toe0";
    [ThreadStatic] static string? lockL, lockR;

    /// <summary>Session 11 (burgchr11, rule C11q): a rig without the biped ball joints (a quadruped: the Undead Attack Dog, the rat,
    /// the boar) measures foot plants on its hind feet's end joints, the first pair it has of LLeg_B_Palm / RLeg_B_Palm (dog), LLeg_b3 /
    /// RLeg_b3, LLegAnkle / RLegAnkle (DS1's other quadruped / dragon rigs); a biped is unchanged. Dispose to restore.</summary>
    public static IDisposable LockJointsFor(MorphemeSkeleton rig)
    {
        string? pl = lockL, pr = lockR;
        if (!rig.Names.Contains("L_Toe0"))
            foreach (var (l, r) in new[] { ("LLeg_B_Palm", "RLeg_B_Palm"), ("LLeg_bAnkle", "RLeg_bAnkle"), ("LLegAnkle", "RLegAnkle"), ("LLegCAnkle", "LLegDAnkle"),
                ("LLeg_b_Ankle", "RLeg_b_Ankle"),   // session 11 (dchr11, rule C11q2): the Gaping Dragon c5260 (hind legs b, on the pelvis)
                ("f006", "f002") })   // session 11 (dchr11, rule SL-F): the legless Slime c3200: its bottom ring's left / right nodes
                if (rig.Names.Contains(l) && rig.Names.Contains(r)) { lockL = l; lockR = r; break; }
        return new Restore(() => { lockL = pl; lockR = pr; });
    }
    sealed class Restore(Action a) : IDisposable { public void Dispose() => a(); }

    /// <summary>World path of one joint with the trajectory applied. For a cyclic clip (last frame = first frame of the next
    /// cycle) the path continues one more cycle: frame P + g = the cycle's end transform composed with frame g, so a window
    /// may run across the loop seam (DS2 stores such windows as start + duration &gt; 1, e.g. c1000 AdditiveTireFront).</summary>
    public static V3[] JointPath(NsaAnim a, MorphemePose pose, int joint, bool cyclic)
    {
        int nf = a.Frames, P = Math.Max(1, nf - 1);
        var p = new V3[cyclic ? 2 * P + 1 : nf];
        for (int f = 0; f < nf; f++) p[f] = pose.World(a, f, true).Pos[joint];
        if (cyclic)
        {
            var tp = a.RootPos[P]; var tq = a.RootRot[P];
            for (int g = nf; g < p.Length; g++) { var r = MorphemeMath.Rotate(tq, p[g - P]); p[g] = new V3(tp.X + r.X, tp.Y + r.Y, tp.Z + r.Z); }
        }
        return p;
    }

    /// <summary>
    /// Plant runs of one foot from its ball-joint path: maximal runs of frames where the joint is slower than
    /// <paramref name="vmax"/> and within <paramref name="band"/> of its lowest point in the clip (the step-2 detector's
    /// test, steps-calib IoU 0.64 against DS2's windows), at least <paramref name="minRun"/> frames. A run's first frame is a
    /// touchdown (frame 0 of a one-shot counts). Cyclic clips are scanned on the circle: a run may end past P. A foot planted
    /// on every frame gives one run [0, P].
    /// </summary>
    public static List<(int A, int B)> PlantRuns(V3[] p, int P, bool cyclic, double fps, double vmax = PlantSpeed, double band = PlantHeight, int minRun = 2)
    {
        int nf = P + 1;
        double zmin = double.MaxValue;
        for (int f = 0; f < nf; f++) zmin = Math.Min(zmin, p[f].Z);
        double Speed(int f)
        {
            if (cyclic) return MorphemePose.Dist(p[P + f - 1], p[P + f + 1]) * fps / 2;
            int f0 = Math.Max(0, f - 1), f1 = Math.Min(P, f + 1);
            return f1 > f0 ? MorphemePose.Dist(p[f0], p[f1]) * fps / (f1 - f0) : 0;
        }
        int scan = cyclic ? P : nf;
        var cond = new bool[scan];
        for (int f = 0; f < scan; f++) cond[f] = Speed(f) < vmax && p[f].Z < zmin + band;
        var res = new List<(int, int)>();
        if (cond.All(c => c)) { res.Add((0, P)); return res; }
        for (int s = 0; s < scan; s++)
        {
            bool prev = cyclic ? cond[(s - 1 + P) % P] : s > 0 && cond[s - 1];
            if (!cond[s] || prev) continue;
            int e = s;
            while (cyclic ? e + 1 < s + P && cond[(e + 1) % P] : e + 1 < nf && cond[e + 1]) e++;
            if (e - s + 1 >= minRun || nf <= 2) res.Add((s, e));
        }
        return res;
    }

    /// <summary>
    /// Touchdowns of a foot on a loop: its plant runs (<see cref="PlantRuns"/>); when the foot never gets slower than
    /// <see cref="PlantSpeed"/> (session 8, track enemies: DS1 c2900's sprint a00_0500, right ball ≥ 0.49 m/s), the runs at a speed
    /// limit 1.25 × the foot's slowest low frame, so its stride still counts as a touchdown for the sync track (P). Feet that pass
    /// the strict test (every c2500 / c2540 loop) are unchanged.
    /// </summary>
    /// <param name="scale">merge stage 5 (track boss): character size against DS2's humanoid; scales the strict test's speed and
    /// height band and the fallback's height band, as in <see cref="FootEvents"/>; 1 = the rule above unchanged</param>
    public static List<(int A, int B)> Touchdowns(V3[] p, int P, bool cyclic, double fps, double scale = 1)
    {
        var r = PlantRuns(p, P, cyclic, fps, PlantSpeed * scale, PlantHeight * scale);
        if (r.Count > 0) return r;
        double zmin = double.MaxValue;
        for (int f = 0; f <= P; f++) zmin = Math.Min(zmin, p[f].Z);
        double vmin = double.MaxValue;
        for (int f = 0; f < P; f++)
        {
            if (p[f].Z >= zmin + PlantHeight * scale) continue;
            double v = cyclic ? MorphemePose.Dist(p[P + f - 1 < p.Length ? P + f - 1 : f], p[P + f + 1 < p.Length ? P + f + 1 : f]) * fps / 2
                : MorphemePose.Dist(p[Math.Max(0, f - 1)], p[Math.Min(P, f + 1)]) * fps / 2;
            vmin = Math.Min(vmin, v);
        }
        return vmin < double.MaxValue ? PlantRuns(p, P, cyclic, fps, vmax: vmin * 1.25 + 1e-6, band: PlantHeight * scale, minRun: 1) : r;
    }

    /// <summary>
    /// Foot-lock windows of one foot: inside each plant run (<see cref="PlantRuns"/>) the anchor frame whose window is
    /// longest, the window lasting while the ball joint stays within <paramref name="tol"/> of where it was at the anchor
    /// (DS2's lock pins the joint where the footfall event begins) and within the height band. Windows shorter than
    /// <paramref name="minLen"/> frames are dropped (that plant is not locked), windows never overlap, and on a cyclic clip a
    /// window may run across the seam but never into the next cycle's first window.
    /// </summary>
    public static List<(int A, int B)> LockWindows(V3[] p, int P, bool cyclic, double fps, double vmax = PlantSpeed, double band = PlantHeight, double tol = LockTolerance, int minLen = MinLockFrames)
    {
        double zmin = double.MaxValue;
        for (int f = 0; f <= P; f++) zmin = Math.Min(zmin, p[f].Z);
        var res = new List<(int, int)>();
        int lastEnd = -1;
        foreach (var (ra, rb) in PlantRuns(p, P, cyclic, fps, vmax, band))
        {
            int bestS = -1, bestE = -1;
            for (int s = Math.Max(ra, lastEnd + 1); s <= rb && (!cyclic || s < P); s++)
            {
                int limit = cyclic ? Math.Min(s + P - 1, 2 * P) : P;
                if (cyclic && res.Count > 0) limit = Math.Min(limit, res[0].Item1 + P - 1);
                int e = s;
                while (e + 1 <= limit && p[e + 1].Z < zmin + band && MorphemePose.Dist(p[e + 1], p[s]) <= tol) e++;
                if (e - s > bestE - bestS) { bestS = s; bestE = e; }
            }
            if (bestS < 0 || bestE - bestS + 1 < minLen) continue;
            res.Add((bestS, bestE)); lastEnd = bestE;
        }
        return res;
    }

    /// <summary>Largest distance of the joint from its window-start position over frames A..B of a path.</summary>
    public static double WindowDrift(V3[] p, int a, int b)
    {
        double m = 0;
        for (int f = a; f <= b && f < p.Length; f++) m = Math.Max(m, MorphemePose.Dist(p[a], p[f]));
        return m;
    }

    /// <summary>
    /// A cyclic clip started k frames later (fix pass, session 8): frame j = old frame k + j (minus P past the end), trajectory
    /// R(k)^-1 ∘ R(k + j) where R(P + g) = R(P) ∘ R(g). The loop is the same motion; only where it begins changes. Used so
    /// every DS1 locomotion loop starts at a left-foot touchdown like DS2's (c1030: all 22 walk/strafe/jog clips have
    /// LeftSteps from 0 and Footsteps event 0 at 0), so the sync events of blended clips line up the same foot.
    /// </summary>
    public static NsaAnim PhaseShift(NsaAnim a, int k)
    {
        int nf = a.Frames, P = nf - 1;
        k = ((k % P) + P) % P;
        if (k == 0) return a;
        var o = new NsaAnim { Fps = a.Fps, Duration = a.Duration, ChannelCount = a.ChannelCount, Pos = new V3[a.ChannelCount][], Rot = new Q4[a.ChannelCount][], RootPos = new V3[nf], RootRot = new Q4[nf] };
        for (int c = 0; c < a.ChannelCount; c++)
        {
            o.Pos[c] = new V3[nf]; o.Rot[c] = new Q4[nf];
            // old frame k + j, the part past P taken from the next cycle (frame P itself stays frame P, so the shifted clip
            // reproduces the original frame for frame; its own last frame is old frame k again: exact loop closure)
            for (int j = 0; j < nf; j++) { int g = k + j > P ? k + j - P : k + j; o.Pos[c][j] = a.Pos[c][g]; o.Rot[c][j] = a.Rot[c][g]; }
        }
        (V3 P, Q4 Q) R(int g)
        {
            if (g <= P) return (a.RootPos[g], a.RootRot[g]);
            var (p2, q2) = (a.RootPos[g - P], a.RootRot[g - P]);
            var r = MorphemeMath.Rotate(a.RootRot[P], p2);
            return (new V3(a.RootPos[P].X + r.X, a.RootPos[P].Y + r.Y, a.RootPos[P].Z + r.Z), MorphemeMath.Mul(a.RootRot[P], q2));
        }
        var (pk, qk) = R(k);
        var qi = MorphemeMath.Conj(qk);
        for (int j = 0; j < nf; j++)
        {
            var (pj, qj) = R(k + j);
            o.RootPos[j] = MorphemeMath.Rotate(qi, new V3(pj.X - pk.X, pj.Y - pk.Y, pj.Z - pk.Z));
            o.RootRot[j] = MorphemeMath.Mul(qi, qj);
        }
        return o;
    }

    /// <summary>Frame of the first left-foot touchdown of a cyclic clip (start of its first left plant run on the circle),
    /// or 0 when the left foot has none. Track boss (session 8): <paramref name="scale"/> = the character's size against
    /// DS2's humanoid (root height / 0.887 m) scales the plant thresholds; when the motion shows no left plant,
    /// <paramref name="hintLeft"/> (DS1's own left footfall from its TAE) is the start.</summary>
    /// <summary>Session 11 (ret11), rule C11c: the frame a locomotion loop starts at. C11b's left-foot touchdown of the motion
    /// (<see cref="CycleStart"/>); when the motion's plant runs from that start do not alternate L R (c2500 a01_0202, the torch
    /// side walk: L L R), DS1's own TAE left footfall (<see cref="TaeFootfalls"/>), and FootEvents then takes the TAE footfalls
    /// as the touchdowns. Loops whose motion alternates keep the motion start (existing rigs byte-identical). Shared by the
    /// build and ds1rig-check.</summary>
    public static int LoopStart(NsaAnim b, MorphemePose pose, Ds1ChrSource ds1, int id, IReadOnlyDictionary<int, char>? footDummy)
    {
        int k = CycleStart(b, pose);
        if (footDummy != null && !Alternating(PhaseShift(b, k), pose)
            && TaeFootfalls(ds1.TaeAnim(id), footDummy).Where(h => h.Foot == 'L').Select(h => (int?)h.Frame).FirstOrDefault() is int tl)
            k = tl;
        return k;
    }

    /// <summary>Session 11 (ret11): whether a loop (already phase-shifted) has motion touchdowns alternating L R from frame 0, judged
    /// on the clip as encoded (the form FootEvents later sees).</summary>
    static bool Alternating(NsaAnim shifted, MorphemePose pose)
    {
        try { FootEvents(NsaCodec.Decode(NsaFile.Parse(NsaEncoder.EncodeFile(shifted).Build())), pose, true, 0); return true; }
        catch (InvalidDataException) { return false; }
    }

    public static int CycleStart(NsaAnim a, MorphemePose pose, double scale = 1, int? hintLeft = null)
    {
        int lj = pose.Index(LockJointL);
        var w = Touchdowns(JointPath(a, pose, lj, true), a.Frames - 1, true, a.Fps, scale);
        return w.Count > 0 ? w[0].A : hintLeft ?? 0;
    }

    /// <summary>The session-8 step-2 detector, superseded in the fix pass by <see cref="LockWindows"/> (kept for
    /// steps-calib's comparison): planted intervals (start frame, end frame inclusive) of a foot: world speed (trajectory
    /// applied) below 0.35 m/s and within 5 cm of the foot's lowest point in the clip; runs shorter than 2 frames are
    /// dropped. It bounds the speed frame by frame but not the drift over a run, so its windows let DS1's ankle move up
    /// to 125 mm (a00_2007) - the reviewer's finding.</summary>
    public static List<(int A, int B)> Planted(NsaAnim a, MorphemePose pose, int foot, double maxSpeed = PlantSpeed, double maxHeight = PlantHeight)
    {
        int nf = a.Frames;
        var p = new V3[nf];
        for (int f = 0; f < nf; f++) p[f] = pose.World(a, f, true).Pos[foot];
        double zmin = p.Min(x => x.Z);
        var planted = new bool[nf];
        for (int f = 0; f < nf; f++)
        {
            int f0 = Math.Max(0, f - 1), f1 = Math.Min(nf - 1, f + 1);
            double v = f1 > f0 ? MorphemePose.Dist(p[f0], p[f1]) * a.Fps / (f1 - f0) : 0;
            planted[f] = v < maxSpeed && p[f].Z < zmin + maxHeight;
        }
        var runs = new List<(int, int)>();
        for (int f = 0; f < nf;)
        {
            if (!planted[f]) { f++; continue; }
            int s = f; while (f < nf && planted[f]) f++;
            if (f - s >= 2 || nf <= 2) runs.Add((s, f - 1));
        }
        return runs;
    }

    /// <summary>
    /// LeftSteps / RightSteps / Footsteps events of one clip slot (start, duration as clip fractions, value), rule C11
    /// (fix pass, session 8):
    /// <list type="bullet">
    /// <item>LeftSteps/RightSteps (userData 10/11, LOCK_FOOT_CHAIN's footfall ids): <see cref="LockWindows"/> on the ball
    /// joint (L_Toe0/R_Toe0) with root motion: inside each plant a window lasts only while the ball stays within
    /// <see cref="LockTolerance"/> (15 mm) of its position at the window start, so DS2's IK never pins a foot that DS1
    /// still moves. A foot with no window gets one zero-length event (no lock), as before.</item>
    /// <item>Footsteps (the SourceAnim sync track): events partition [0,1] with no gap, like all 48,834 DS2 Footsteps
    /// tracks (syncsurvey.py). A locomotion loop (<c>Loop</c>, already started at its left touchdown) gets one event per
    /// touchdown (plant-run start, <see cref="PlantRuns"/>), which must alternate L R L R starting with L at 0 (DS2 c1030
    /// walk/strafe: L at 0, R at the right touchdown), so the same event index is the same foot in every blended loop.
    /// <c>SyncEvents</c> n: n equal events.
    /// Anything else (idle, turns, hits, deaths, throws, ladder): one event [0,1], DS2's own form for those slots (272 of
    /// c1030's 508 anim nodes, every damage / death / throw / ladder / start-up slot).</item>
    /// </list>
    /// </summary>
    /// <param name="scale">track boss: character size against DS2's humanoid (root height / 0.887 m); the plant speed,
    /// height band and lock tolerance scale with it (1 = the Hollow's calibration, unchanged)</param>
    /// <param name="downsHint">track boss: DS1's own footfalls of a loop from its TAE (frame in this clip, 'L'/'R'), used when
    /// the motion's plant runs do not alternate L R L R (a big character's sliding gallop)</param>
    /// <summary>Session 11 (burgchr11): rule C11d on (the boss builds set it for the Burg's demons; regular rigs keep the strict rule).</summary>
    [ThreadStatic] public static bool AllowEvenSync;

    public static (List<(float, float, int)> L, List<(float, float, int)> R, List<(float, float, int)> Sync, string Note) FootEvents(NsaAnim a, MorphemePose pose, bool loop, int syncEvents,
        double scale = 1, IReadOnlyList<(int Frame, char Foot)>? downsHint = null)
    {
        int P = Math.Max(1, a.Frames - 1);
        var pl = JointPath(a, pose, pose.Index(LockJointL), loop); var pr = JointPath(a, pose, pose.Index(LockJointR), loop);
        var wl = LockWindows(pl, P, loop, a.Fps, PlantSpeed * scale, PlantHeight * scale, LockTolerance * scale);
        var wr = LockWindows(pr, P, loop, a.Fps, PlantSpeed * scale, PlantHeight * scale, LockTolerance * scale);
        List<(float, float, int)> Win(List<(int A, int B)> w, int value) =>
            w.Count == 0 ? [(0f, 0f, value)] : w.Select(r => ((float)((double)r.A / P), (float)((double)(r.B - r.A) / P), value)).ToList();
        var sync = new List<(float, float, int)>();
        string note = "";
        if (syncEvents > 0)
        {
            for (int i = 0; i < syncEvents; i++) sync.Add(((float)((double)i / syncEvents), (float)(1.0 / syncEvents), 0));
        }
        else if (loop)
        {
            // touchdowns = plant-run starts (a plant can be too unsteady to lock and still be a step). The clip was started at
            // the left touchdown found on the full-precision motion; on the encoded clip that touchdown may land a frame or two
            // either side of the seam, so a left touchdown within 2 frames of frame 0 / P is frame 0.
            var dl = Touchdowns(pl, P, true, a.Fps, scale).Select(w => w.A <= 2 || w.A >= P - 2 ? 0 : w.A).Distinct().ToList();
            var downs = dl.Select(f => (F: f, Foot: 'L')).Concat(Touchdowns(pr, P, true, a.Fps, scale).Select(w => (F: w.A, Foot: 'R'))).OrderBy(d => d.F).ToList();
            bool Alternates(List<(int F, char Foot)> dd) => dd.Count >= 2 && dd.Count % 2 == 0 && dd[0].F == 0 && dd[0].Foot == 'L'
                && dd.Select((d, i) => d.Foot == (i % 2 == 0 ? 'L' : 'R')).All(x => x);
            bool alternates = Alternates(downs);
            if (!alternates && downsHint != null)
            {
                // DS1's own footfalls (TAE); a left one within 2 frames of the seam is frame 0, as above
                var hinted = downsHint.Select(h => (F: h.Foot == 'L' && (h.Frame <= 2 || h.Frame >= P - 2) ? 0 : h.Frame, h.Foot)).Distinct().OrderBy(h => h.F).ToList();
                if (Alternates(hinted)) { downs = hinted; alternates = true; note = "TAE "; }
            }
            if (!alternates && AllowEvenSync)
            {
                // session 11 (burgchr11, rule C11d, P): neither the motion nor DS1's TAE gives L R L R (the Taurus Demon's and the Titanite
                // Demon's heavy walks: one foot drags; the Titanite Demon's left leg is not even skinned) - the loop gets two equal sync
                // events [0, ½) L, [½, 1) R, so DS2's blends still pair it with the other loops; reported
                note = "even (C11d) after " + string.Concat(downs.Select(d => d.Foot));
                downs = [(0, 'L'), (P / 2, 'R')];
                alternates = true;
            }
            else note += string.Concat(downs.Select(d => d.Foot));
            if (!alternates) throw new InvalidDataException($"locomotion loop touchdowns {note} at frames {string.Join(",", downs.Select(d => d.F))}: not L R L R from a left touchdown at frame 0");
            for (int i = 0; i < downs.Count; i++)
            {
                double s = (double)downs[i].F / P, e = i + 1 < downs.Count ? (double)downs[i + 1].F / P : 1.0;
                sync.Add(((float)s, (float)(e - s), 0));
            }
        }
        else sync.Add((0f, 1f, 0));
        return (Win(wl, 10), Win(wr, 11), sync, note);
    }

    // ------------------------------------------------------------------ DS1 TAE gameplay windows → DS2 tracks (rule C12)

    /// <summary>DS1 TAE JumpTable windows (start, end in seconds) with the given JumpTable id (first int parameter).</summary>
    public static List<(double S, double E)> JumpTable(TAE.Animation? a, int id) =>
        a?.Events.Where(x => x.Type == 0 && BitConverter.ToInt32(x.GetParameterBytes(false), 0) == id).Select(x => ((double)x.StartTime, (double)x.EndTime)).OrderBy(x => x.Item1).ToList() ?? [];

    /// <summary>
    /// The DS2 role of an event track, from what SM_Main's conditions test (c1030.network.txt; track names vary:
    /// StateControl / StateControll / StateControl119728, LeftSteps / Leftsteps): the SourceAnim's sync track; userData 10
    /// / 11 foot-lock windows; value 80 = return to base (T[7], T[87]: "event value in [80,80]" → base); value 250 =
    /// DamageDuration (T[25], T[27], T[98]: the damage state can be left by moving, T[95-97]: by attacking, only when no
    /// event in [200,250] is active); value 300 = DieControl (T[82]: damage → die when the HP is gone and the value is
    /// 300); value 9610 = Gravity (ladder slots: on the ladder for the whole clip, LadderRelease 0-0.5); values 100-199 =
    /// the attack family (AttackStateControl 100-106: combo windows T[3-6] "== 101/102", Attack_Cancel T[91]/T[102]
    /// "[100,121]", attack → base T[99] "NOT [100,120]"; AttackDuration 120, HitDuration 123, AttackCancelControl 110-112,
    /// AttackSpeedControl 150-155, BladeDashDuration 121, AttackMasterBind 170).
    /// </summary>
    public enum TrackKind { Other, Sync, LeftFoot, RightFoot, ReturnToBase, Damage, Die, Gravity, Attack }

    public static TrackKind KindOf(NmbEventTrack t, bool isSync)
    {
        if (isSync) return TrackKind.Sync;
        if (t.UserData == 10) return TrackKind.LeftFoot;
        if (t.UserData == 11) return TrackKind.RightFoot;
        if (t.Events.Count == 0) return TrackKind.Other;
        var v = t.Events.Select(e => e.Value).Distinct().ToList();
        if (v.All(x => x == 80)) return TrackKind.ReturnToBase;
        if (v.All(x => x == 250)) return TrackKind.Damage;
        if (v.All(x => x == 300)) return TrackKind.Die;
        if (v.All(x => x == 9610)) return TrackKind.Gravity;
        if (v.All(x => x >= 100 && x < 200)) return TrackKind.Attack;
        return TrackKind.Other;
    }

    /// <summary>
    /// The events a slot wants on one of its tracks (null = keep DS2's). Rule C12 (fix pass, session 8):
    /// <list type="bullet">
    /// <item>C12a Die: DS1 "Death State" (JT 12) / "Throw Death Animation Transition" (JT 69) start → DieControl from there
    /// to the clip end (DS2's own death and throw-victim tracks run to the end: DieNormalSlash_F 0.438+0.562,
    /// CatchBackStabSlash 0.583+0.417).</item>
    /// <item>C12b Damage / return to base: DS1 "AI Movement Anim Cancel" (JT 78; else 79 dodge, 23 continuation) start →
    /// DamageDuration [0, cancel) (from the cancel on, SM_Main lets the AI move or act out of the reaction, which is what
    /// DS1's flag allows); the value-80 return-to-base window starts at max(DS2's fraction, cancel) - DS2's blend-out
    /// rule, never before DS1 lets the character leave.</item>
    /// <item>C12c No-attack slots (the unarmed Hollow never attacks): every attack-family track empty (no combo window, no
    /// hit / cancel / speed window), return to base over the whole clip, so an attack request falls straight back to the
    /// base state (T[87]) and plays nothing but DS1's idle. Its _pl TAE events (DS2 hit 2200, 120200, turn rate 280100,
    /// 110000) are dropped by <see cref="EditTae"/>.</item>
    /// <item>C12d Gravity: DS1 "Ignore Gravity" (JT 27) windows → DS2 Gravity 9610 windows (only slots whose DS2 node
    /// carries a Gravity track: the ladder set); no JT 27 → none.</item>
    /// </list>
    /// </summary>
    internal static List<(float, float, int)>? Gameplay(TrackKind kind, NmbEventTrack tr, ClipInfo info)
    {
        switch (kind)
        {
            case TrackKind.Attack when info.NoAttack: return [];
            case TrackKind.ReturnToBase when info.NoAttack: return [(0f, 1f, 80)];
            case TrackKind.Die when info.DeathState >= 0: return [((float)info.DeathState, (float)(1 - info.DeathState), 300)];
            case TrackKind.Damage when info.Cancel >= 0: return [(0f, (float)info.Cancel, 250)];
            case TrackKind.ReturnToBase when info.Cancel >= 0:
                {
                    double s = Math.Max(tr.Events.Min(e => e.Start), info.Cancel);
                    return [((float)s, (float)(1 - s), 80)];
                }
            case TrackKind.Gravity when info.Ds1Id >= 0: return info.NoGravity.Select(w => ((float)w.S, (float)(w.E - w.S), 9610)).ToList();
            default: return null;
        }
    }

    /// <summary>Two slots that share one track want different events: the merged events (rule C11/C12 shared tracks).
    /// Foot windows → no lock; sync → one event; return to base / die → the later start; damage → the longer window;
    /// anything else → DS2's original.</summary>
    internal static List<(float, float, int)> Merge(TrackKind kind, List<(float S, float D, int V)> a, List<(float S, float D, int V)> b, List<(float, float, int)> ds2) => kind switch
    {
        TrackKind.LeftFoot => [(0f, 0f, 10)],
        TrackKind.RightFoot => [(0f, 0f, 11)],
        TrackKind.Sync => [(0f, 1f, 0)],
        TrackKind.ReturnToBase or TrackKind.Die when a.Count == 1 && b.Count == 1 => a[0].S >= b[0].S ? [a[0]] : [b[0]],
        TrackKind.Damage when a.Count == 1 && b.Count == 1 => a[0].D >= b[0].D ? [a[0]] : [b[0]],
        _ => ds2,
    };

    internal static string Ev(IEnumerable<(float S, float D, int V)> e) => "[" + string.Join(" ", e.Select(x => $"{x.S.ToString("0.###", Inv)}+{x.D.ToString("0.###", Inv)}:{x.V}")) + "]";

    /// <summary>Session 11 (track ret11): DS1's own footfalls of a clip from its TAE, as (frame at 30 fps, foot): the boss
    /// pipeline's event 144 (floor effect, u16 dummy at +2; <see cref="Ds1BossAnims.TaeFootfalls"/>) and the regulars' event 112
    /// (sound at a dummy poly, int dummy at +4: c2500 a01_0200 footstep 3100 at dummies 4 / 3 = L / R Foot) on a foot dummy.</summary>
    public static List<(int Frame, char Foot)> TaeFootfalls(TAE.Animation? a, IReadOnlyDictionary<int, char> footDummy) =>
        a?.Events.Where(e => e.Type is 144 or 112).Select(e =>
        {
            var p = e.GetParameterBytes(false);
            int d = e.Type == 144 ? (p.Length >= 4 ? BitConverter.ToUInt16(p, 2) : -1) : (p.Length >= 8 ? BitConverter.ToInt32(p, 4) : -1);
            return (F: (int)Math.Round(e.StartTime * 30), D: d);
        }).Where(t => footDummy.ContainsKey(t.D)).Select(t => (t.F, footDummy[t.D])).Distinct().OrderBy(t => t.F).ToList() ?? [];

    /// <summary>Session 11 (track ret11), rule R-off: a role's DS1 clip ids with NpcParam.animIdOffset applied (x + offset when
    /// the DS1 chr has that clip, else x: DS1's fallback to a00). Shared by the build and ds1rig-check.</summary>
    public static int[] Offset(int[] ids, Ds1ChrSource ds1, int offset) => [.. ids.Select(x => x >= 0 && offset != 0 && ds1.Has(x + offset) ? x + offset : x)];

    /// <summary>Session 11 (chrai11), rule P3: the added start-up types (type, DS1 dormant-loop slot, wake slot), each cloned from type 4.</summary>
    public static readonly (int Type, string Loop, string Wake)[] AddedStartUps = [(5, "a75_01_7130", "a75_01_7131"), (6, "a75_01_7140", "a75_01_7141"), (7, "a75_01_7150", "a75_01_7151")];

    /// <summary>
    /// Session 11 (track chrai11), rule P3 (asy10 P3 step 2, burgchr11 "added categories", the additive NmbNetClone route E2b): DS1's
    /// start-up poses the template network has no slot for (9008 crouched, 9010 sitting, 9013 waiting above to drop) get start-up types
    /// of their own. In every start-up SM state that StartupType 4 enters (one per weapon style: c1030 SM 1717's children 1718, 1745, …),
    /// type 4's state (dormant loop a75_01_7120 + wake a75_01_7121, its subtree, rule D1's copies) is cloned as a new state entered on
    /// StartupType t (NmbNetClone.AddCategoryOn, an int CP), its two leaves pointed at new file-name entries named after the added
    /// slots (a75_01_7130 / 7131 …: the role table gives them their DS1 clips like the template's, the clip loop encodes them); the new
    /// NSAs are added to the anibnd before the clip loop. Only for a character whose DS1 set has the slot's own pose (role Ds1[0]); the
    /// cloned leaves keep the template's event tracks (TimeAct 75017120 / 75017121: shared, P). GeneratorRegistParam.startUpStateType t
    /// (AsylumEnemies.PoseStartUp) starts the part in it; when DS2's generic upper logic wakes it is U (as P3b).
    /// </summary>
    static byte[] AddStartUps(byte[] nmbBytes, BND4 bnd, Role[] roles, Ds1ChrSource ds1, string chrNote, List<string> report)
    {
        var adds = AddedStartUps.Where(a => roles.FirstOrDefault(r => r.Slot == a.Loop) is { Ds1.Length: > 0 } lr && ds1.Has(lr.Ds1[0])
            && roles.Any(r => r.Slot == a.Wake)).ToList();
        if (adds.Count == 0) return nmbBytes;
        var ed = new NmbNetClone(nmbBytes);
        var names = Formats.Morpheme.NmbNetEdit.NodeNames(ed.X);
        var files = NmbFileNamesFull.Read(ed.Nmb.Bundles[ed.Nmb.IndexOf(13)].Data).Files;
        var net = ed.View();
        int cp = names.FirstOrDefault(kv => kv.Value == "ControlParameters|StartupType").Key;
        if (cp == 0) { report.Add($"rule P3: {chrNote}: no StartupType control parameter: no added start-ups"); return nmbBytes; }
        // the type-4 states: (their SM, the state, its loop / wake leaves)
        var t4 = new List<(int Sm, int State, int Loop, int Wake)>();
        string Clip(int leaf) { var sa = NmbNetwork.Find(net.GetNode(leaf), 21); int ci = sa != null ? net.ClipIndex(sa) : -1; return ci >= 0 && ci < files.Count ? files[ci] : ""; }
        foreach (var nd in net.Nodes().Where(n => n.Type == 10))
        {
            var def = ed.Sm(nd.Index);
            var states = def.States.Select(s => (int)s.Node).ToList();
            foreach (var tr in def.States.Where(s => net.GetNode(s.Node).Type is 400 or 402 && s.Dst != 0xFFFF))
            {
                bool four = tr.Entry.Any(ci => BitConverter.ToUInt16(def.Conds[ci], 0) is 625 or 627 && BitConverter.ToUInt16(def.Conds[ci], 0x48) == cp
                    && (BitConverter.ToUInt16(def.Conds[ci], 0) == 625 ? BitConverter.ToInt32(def.Conds[ci], 0x4C) == 4 && BitConverter.ToInt32(def.Conds[ci], 0x50) == 4
                        : BitConverter.ToSingle(def.Conds[ci], 0x4C) == 4 && BitConverter.ToSingle(def.Conds[ci], 0x50) == 4));
                if (!four) continue;
                int st = states[tr.Dst];
                var leaves = ed.Subtree(st).Where(i => net.GetNode(i).Type == 104).ToList();
                int loop = leaves.FirstOrDefault(l => Clip(l).StartsWith("a75_01_7120", StringComparison.OrdinalIgnoreCase));
                int wake = leaves.FirstOrDefault(l => Clip(l).StartsWith("a75_01_7121", StringComparison.OrdinalIgnoreCase));
                if (loop != 0 && wake != 0 && !t4.Any(t => t.State == st)) t4.Add((nd.Index, st, loop, wake));
            }
        }
        if (t4.Count == 0) { report.Add($"rule P3: {chrNote}: no StartupType-4 state with a75_01_7120/7121 leaves: no added start-ups"); return nmbBytes; }
        // file-name entries + NSAs (placeholders = the template's 7120 / 7121 NSAs; the clip loop puts DS1's clips in by role)
        var nsas = bnd.Files.Where(f => f.Name.EndsWith(".nsa", StringComparison.OrdinalIgnoreCase)).ToList();
        BinderFile Tpl(string slot) => nsas.First(f => Path.GetFileName(f.Name.Replace('\\', '/')).StartsWith(slot, StringComparison.OrdinalIgnoreCase));
        var entries = new List<(string File, string Source, string Take, uint Crc)>();
        var bytesOf = new List<byte[]>();
        foreach (var (type, lp, wk) in adds)
            foreach (var (slot, from) in new[] { (lp, "a75_01_7120"), (wk, "a75_01_7121") })
            {
                var b = Tpl(from).Bytes;
                uint crc = NmbNetClone.Crc32Bzip2(b);
                entries.Add((NmbNetClone.NsaName($"{slot}_StartUp_P3", "untitled", crc), $"$(RootDir)\\motion_xmd\\{slot}_StartUp_P3.xmd", "untitled", crc));
                bytesOf.Add(b);
            }
        int first = ed.AddClips(entries);
        var dir = nsas[0].Name[..(Math.Max(nsas[0].Name.LastIndexOf('\\'), nsas[0].Name.LastIndexOf('/')) + 1)];
        int nextId = bnd.Files.Max(f => f.ID) + 1;
        for (int k = 0; k < entries.Count; k++) bnd.Files.Add(new BinderFile(nsas[0].Flags, nextId++, dir + entries[k].File, bytesOf[k]));
        var notes = new List<string>();
        for (int a = 0; a < adds.Count; a++)
            foreach (var (sm, st, loop, wake) in t4)
            {
                var cr = ed.AddCategoryOn("StartupType", st, adds[a].Type, sm);
                Formats.Morpheme.Boss.NmbNetEdit.SetClipIndex(ed.X, cr.IdMap[loop], first + 2 * a);
                Formats.Morpheme.Boss.NmbNetEdit.SetClipIndex(ed.X, cr.IdMap[wake], first + 2 * a + 1);
                notes.Add($"{adds[a].Type}: SM {sm} state {st} → {cr.NewSm} (transition {cr.NewTransition}, {cr.IdMap.Count} nodes)");
            }
        report.Add($"rule P3: {chrNote}: start-up types {string.Join(", ", adds.Select(a => $"{a.Type} ({a.Loop} = DS1 a00_{roles.First(r => r.Slot == a.Loop).Ds1[0]:D4})"))} added in {t4.Count} start-up SMs: {string.Join("; ", notes)}; clips {first}..{first + entries.Count - 1}");
        return ed.Build();
    }

    /// <param name="newChr">the anibnd's character name ("c2501"): its NMB entry is renamed cNNNN.nmb like every vanilla
    /// anibnd's (c3050.anibnd holds c3050.nmb)</param>
    /// <param name="i1bReserved">session 10 (rule I1b, <see cref="NmbIdentity"/>): the build's id registry (vanilla ids + ids handed out so far, rule I1c); when given, every
    /// registered bundle whose content is not the template's gets an id of its own (the network def always)</param>
    public static Result BuildAnibnd(byte[] templateAnibnd, Ds1ChrSource ds1, Role[] roles, string chrNote, string newChr, Ds1Attacks.Plan? attacks = null, Ds2TaeFile? templateTae = null, NmbIdentity.Registry? i1bReserved = null,
        IReadOnlyDictionary<int, char>? footDummy = null, bool idleFallback = false)
    {
        var res = new Result();
        byte[] raw = templateAnibnd;
        DCX.CompressionInfo? comp = null;
        if (DCX.Is(raw)) raw = DCX.Decompress(raw, out comp);
        var bnd = BND4.Read(raw);
        var nmbFile = bnd.Files.First(f => f.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase));
        byte[] templateNmb = nmbFile.Bytes;
        var nmb = NmbFile.Read(nmbFile.Bytes);
        var oldRig = NmbRig.Read(nmb.Bundles[nmb.IndexOf(1)].Data);

        // ---- the DS1 rig into the network
        var ds1Rig = ds1.Bridge(ds1.ClipIds.First()).Skeleton;
        var rig = WithDs2Names(ds1Rig);
        res.Rig = rig; res.Ds1Names = ds1Rig.Names;
        var log = new List<string>();
        byte[] nmbBytes;
        if (rig.Names.Count == oldRig.Names.Count)
        {
            var match = NmbRigReplace.MatchByName(oldRig.Names, rig.Names, Ds2ToDs1Fallback);
            nmbBytes = NmbRigReplace.ReplaceMapped(nmb, rig, match, log);
            res.Report.AddRange(log);
            res.Report.Add("bone match: " + string.Join("; ", match.Notes));
        }
        else
        {
            // session 8 (track enemies): another bone count than the template's rig → the network re-serialiser (NmbNetResize,
            // the C# port of net_resize.py; byte-identical to the Python on c3050 → c2232 and up to signed zeros on c1030 → c2540)
            var rr = new NmbNetResize.Report();
            nmbBytes = NmbNetResize.Resize(nmbFile.Bytes, rig, rr);
            var bad = NmbNetResize.CheckRigConsistency(nmbBytes);
            if (bad.Count > 0) throw new InvalidDataException($"{chrNote}: resized network fails the rig-consistency check: {string.Join("; ", bad)}");
            res.Report.Add($"network resized {oldRig.Names.Count} → {rig.Names.Count} bones (NmbNetResize): {rr}; template joints without a DS1 bone (→ root): [{string.Join(", ", rr.UnmatchedDonorJoints)}]; rig consistency clean");
        }
        {
            // session 11 (crash11), rule IK1: a template joint mapped onto its ancestor (L_Toe0Nub → L_Toe0 on DS1 c2900) must
            // not leave a repeated joint in an uneven-terrain foot chain: the runtime walk (FUN_140678e30) passes the root
            var ikLog = new List<string>();
            nmbBytes = IkChains.RepairNmb(nmbBytes, ikLog);
            res.Report.AddRange(ikLog);
        }
        if (attacks != null)
        {
            var ar = Ds1Attacks.Apply(nmbBytes, ds1, attacks, rig, chrNote, templateTae ?? throw new ArgumentNullException(nameof(templateTae)));
            nmbBytes = ar.Nmb;
            res.Attacks = ar;
            res.Report.AddRange(ar.Report);
        }
        nmbBytes = AddStartUps(nmbBytes, bnd, roles, ds1, chrNote, res.Report);   // session 11 (chrai11): rule P3
        nmb = NmbFile.Read(nmbBytes);
        var netIndex = nmb.IndexOf(10);
        var net = new NmbNetwork((byte[])nmb.Bundles[netIndex].Data.Clone());
        var fileNames = NmbFileNames.Read(nmb.Bundles[nmb.IndexOf(13)].Data).Anims;
        var pose = MorphemePose.From(rig);
        // session 11 (track ret11), rule R-off (asy10 enemies.md §3): DS1's NpcParam.animIdOffset shifts EVERY clip request,
        // falling back to a00 when the offset set lacks the clip (as Ds1Attacks.Plan.Clip does for attacks): a role's DS1
        // id x resolves to x + offset when the DS1 chr has that clip. Only the torch set (c2500 a01: idle 10000, walk
        // 10200-10203, run 10500, turns, steps, guard 12000) owns locomotion; the other offset sets own attacks only, so
        // their roles resolve to a00 as before (checked: the existing anibnds are byte-identical).
        int roff = attacks?.AnimOffset ?? 0;
        int[] Res(int[] ids) => Offset(ids, ds1, roff);
        // the loops' sync-event count (walk forward), for the roles marked SyncEvents = LoopSync
        {
            var walk = roles.FirstOrDefault(r => r.Loop && Res(r.Ds1).Any(ds1.Has)) ?? throw new InvalidDataException($"{chrNote}: no locomotion loop role");
            int wid = Res(walk.Ds1).First(ds1.Has);
            var wb = ds1.Bridge(wid).Anim;
            var ws = PhaseShift(wb, CycleStart(wb, pose));
            try { res.LoopSync = LoopTouchdowns(NsaCodec.Decode(NsaFile.Parse(NsaEncoder.EncodeFile(ws).Build())), pose); }
            catch (InvalidDataException) when (footDummy != null)
            {
                // session 11 (burgchr11): a walk loop whose motion touchdowns do not alternate (the Balder Knight's a00_0200: L R R, its
                // cape-heavy gait) counts DS1's own TAE footfalls from the rule C11c loop start, as the slot path (FootEvents' hint) does
                int k = LoopStart(wb, pose, ds1, wid, footDummy);
                int P = wb.Frames - 1;
                var hint = TaeFootfalls(ds1.TaeAnim(wid), footDummy).Select(h => (((h.Frame - k) % P + P) % P, h.Foot)).ToList();
                res.LoopSync = FootEvents(NsaCodec.Decode(NsaFile.Parse(NsaEncoder.EncodeFile(PhaseShift(wb, k)).Build())), pose, true, 0, 1, hint).Sync.Count;
                res.Report.Add($"loop sync count from DS1's TAE footfalls (walk a00_{wid:D4} from frame {k}): {res.LoopSync}");
            }
        }

        var idleFallbacks = new List<string>();   // session 11 (ret11), rule RF1
        // ---- clips
        var encoded = new Dictionary<(int Id, bool Loop), (byte[] Bytes, NsaAnim Back, int Shift)>();
        var clipByIndex = new Dictionary<int, ClipInfo>();
        var backByIndex = new Dictionary<int, NsaAnim>();
        foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".nsa", StringComparison.OrdinalIgnoreCase)))
        {
            string name = Path.GetFileName(f.Name.Replace('\\', '/'));
            int idx = fileNames.FindIndex(x => x.Equals(name, StringComparison.OrdinalIgnoreCase) || x.Equals(Path.GetFileNameWithoutExtension(name), StringComparison.OrdinalIgnoreCase));
            var ds2 = NsaFile.Parse(f.Bytes);
            var role = RoleFor(roles, name) ?? throw new InvalidDataException($"no DS1 role for clip slot {name}");
            int id = Res(role.Ds1).FirstOrDefault(x => x == -1 || ds1.Has(x), int.MinValue);
            // session 11 (ret11), rule RF1: a character whose DS1 set lacks a role's clips (the Black Knight: no ladder, lying or parry
            // clips) plays DS1's idle there when its rig allows it (OneToOne.IdleFallback), reported; otherwise the build stops
            if (id == int.MinValue && idleFallback && ds1.Has(0)) { id = 0; idleFallbacks.Add($"{name[..Math.Min(11, name.Length)]} ({role.Name}: DS1 {string.Join("/", role.Ds1)})"); }
            if (id == int.MinValue) throw new InvalidDataException($"{name}: none of DS1 {string.Join("/", role.Ds1)} in {chrNote}");
            byte[] bytes; NsaAnim back; int shift = 0;
            if (id == -1)
            {
                var a = BindClip(rig, ds2.Duration);
                bytes = NsaEncoder.EncodeFile(a).Build();
                back = NsaCodec.Decode(NsaFile.Parse(bytes));
            }
            else
            {
                if (!encoded.TryGetValue((id, role.Loop), out var e))
                {
                    var b = ds1.Bridge(id).Anim;
                    int k = 0;
                    // rule C11b: a locomotion loop starts at its left-foot touchdown (full-precision DS1 motion, before encoding)
                    if (role.Loop) { k = LoopStart(b, pose, ds1, id, footDummy); b = PhaseShift(b, k); }
                    var by = NsaEncoder.EncodeFile(b).Build();
                    encoded[(id, role.Loop)] = e = (by, NsaCodec.Decode(NsaFile.Parse(by)), k);
                }
                (bytes, back, shift) = e;
            }
            var orig = NsaCodec.Decode(ds2);
            f.Bytes = bytes;
            double d1 = (back.Frames - 1) / back.Fps;
            var tae = id >= 0 ? ds1.TaeAnim(id) : null;
            double Frac(double t) => d1 > 0 ? Math.Clamp(t / d1, 0, 1) : 0;
            var j78 = JumpTable(tae, 78); var j79 = JumpTable(tae, 79); var j23 = JumpTable(tae, 23);
            var cancelList = j78.Count > 0 ? j78 : j79.Count > 0 ? j79 : j23;
            var cancel = cancelList.Count > 0 ? cancelList[0] : (-1.0, -1.0);
            var death = JumpTable(tae, 12).Concat(JumpTable(tae, 69)).OrderBy(x => x.S).FirstOrDefault((-1.0, -1.0));
            var info = new ClipInfo
            {
                Name = name, Role = role.Name, Status = role.Status, Ds1Id = id, NoAttack = role.NoAttack, Loop = role.Loop && id >= 0, Shift = shift, SyncEvents = role.SyncEvents == LoopSync ? res.LoopSync : role.SyncEvents,
                Ds2Duration = ds2.Duration, Ds1Duration = d1, Frames = back.Frames, Fps = back.Fps,
                Ds2RootEnd = orig.RootPos[^1], Ds1RootEnd = back.RootPos[^1], Ds1Tae = id >= 0 ? DescribeDs1Tae(tae) : "",
                DeathState = death.Item1 >= 0 ? Frac(death.Item1) : -1,
                Cancel = cancel.Item1 >= 0 ? Frac(cancel.Item1) : -1,
                NoGravity = JumpTable(tae, 27).Select(w => (Frac(w.S), Frac(w.E))).Where(w => w.Item2 > w.Item1).ToList(),
            };
            res.Clips.Add(info);
            if (idx >= 0) { clipByIndex[idx] = info; backByIndex[idx] = back; }
            else res.Report.Add($"WARNING {name}: not in the file-name table");
        }

        // ---- network: SourceAnim attribs and event tracks
        var trackBundle = new Dictionary<uint, int>();
        for (int i = 0; i < nmb.Bundles.Count; i++) if (nmb.Bundles[i].Type is 3 or 4 or 5) trackBundle[nmb.Bundles[i].AssetId] = i;
        // per track: the track, its kind, and every slot's wish (null = keep DS2's events); resolved after all nodes are seen,
        // so a slot that keeps DS2's events still counts when another slot sharing the track wants a change
        var wishes = new Dictionary<uint, (NmbEventTrack Track, TrackKind Kind, List<(int Clip, List<(float, float, int)>? Events)> Wish)>();
        var newTracks = new Dictionary<uint, (NmbEventTrack Track, List<(float, float, int)> Ds2, TrackKind Kind, List<int> Clips)>();
        var sizeFields = new Dictionary<uint, List<int>>();
        var feetOf = new Dictionary<int, (List<(float, float, int)> L, List<(float, float, int)> R, List<(float, float, int)> Sync, string Note)>();
        int patched = 0, fractionsReset = 0;
        var conflictNotes = new List<string>();
        foreach (var node in net.Nodes().Where(x => x.Type == 104))
        {
            var sa = NmbNetwork.Find(node, 21);
            if (sa == null) continue;
            int ci = net.ClipIndex(sa);
            if (!backByIndex.TryGetValue(ci, out var back)) continue;
            net.PatchSourceAnim(sa, back.RootPos[0], back.RootRot[0], back.RootPos[^1], back.RootRot[^1], (back.Frames - 1) / back.Fps);
            // the DS1 clip plays whole: clip start/end fractions 0/1 (DS2 trims a few of its own clips a few thousandths in)
            if (net.F32(sa.Offset + 0x80) != 0f || net.F32(sa.Offset + 0x84) != 1f) { net.PutF32(sa.Offset + 0x80, 0f); net.PutF32(sa.Offset + 0x84, 1f); fractionsReset++; }
            patched++;
            var info = clipByIndex[ci];
            var et = NmbNetwork.Find(node, 23);
            if (et == null) continue;
            if (!feetOf.TryGetValue(ci, out var feet))
            {
                // session 11 (ret11): a loop whose plant runs do not alternate L R falls back to DS1's own TAE footfalls (event 144 on a
                // foot dummy), the boss pipeline's rule (Ds1BossAnims.TaeFootfalls); unused when the motion alternates (existing rigs unchanged)
                List<(int, char)>? hint = null;
                if (info.Loop && info.Ds1Id >= 0 && footDummy != null)
                {
                    int P = back.Frames - 1;
                    hint = TaeFootfalls(ds1.TaeAnim(info.Ds1Id), footDummy).Select(h => (((h.Frame - info.Shift) % P + P) % P, h.Foot)).ToList();
                }
                try { feet = info.Ds1Id == -1 ? ([(0f, 1f, 10)], [(0f, 1f, 11)], [(0f, 1f, 0)], "") : FootEvents(back, pose, info.Loop, info.SyncEvents, 1, hint); }
                catch (InvalidDataException ex) { throw new InvalidDataException($"{chrNote} slot {info.Name} (DS1 {Ds1ChrSource.ClipName(info.Ds1Id)}, shift {info.Shift}, {back.Frames} frames): {ex.Message}; TAE footfalls {(hint == null ? "none (no foot dummies)" : string.Join(",", hint.Select(h => $"{h.Item2}{h.Item1}")))}; foot dummies {(footDummy == null ? "-" : string.Join(",", footDummy.Select(kv => $"{kv.Key}={kv.Value}")))}", ex); }
                feetOf[ci] = feet;
                info.Steps = $"L{Ev(feet.L)} R{Ev(feet.R)} sync {feet.Sync.Count} {Ev(feet.Sync)}{(info.Loop ? $" (loop started at DS1 frame {info.Shift}, touchdowns {feet.Note})" : "")}";
            }
            int syncIndex = net.SyncTrackIndex(sa), discrete = 0;
            foreach (var (group, tid, sizeField) in net.Tracks(et))
            {
                bool isSync = group == 0 && discrete++ == syncIndex;
                if (!trackBundle.TryGetValue(tid, out int bi)) continue;
                var tb = nmb.Bundles[bi];
                var tr = NmbEventTrack.Read(tb.Data, tb.Type);
                if (sizeField >= 0) { if (!sizeFields.TryGetValue(tid, out var l)) sizeFields[tid] = l = []; if (!l.Contains(sizeField)) l.Add(sizeField); }
                if (tr.Name == "TimeAct")
                    foreach (var ev in tr.Events) if (!info.TimeAct.Contains(ev.Value)) info.TimeAct.Add(ev.Value);
                var kind = KindOf(tr, isSync);
                List<(float, float, int)>? events = kind switch
                {
                    TrackKind.Sync => feet.Sync,
                    TrackKind.LeftFoot => feet.L,
                    TrackKind.RightFoot => feet.R,
                    _ => Gameplay(kind, tr, info),
                };
                if (!wishes.TryGetValue(tid, out var w)) wishes[tid] = w = (tr, kind, []);
                if (!w.Wish.Any(x => x.Clip == ci)) w.Wish.Add((ci, events));
            }
        }
        foreach (var (tid, (tr, kind, wish)) in wishes)
        {
            if (wish.All(x => x.Events == null)) continue;
            var ds2Events = tr.Events.ToList();
            List<(float, float, int)>? cur = null;
            foreach (var (ci, ev) in wish)
            {
                var e = ev ?? ds2Events;   // a slot that keeps DS2's events takes part in the merge with them
                if (cur == null) { cur = e; continue; }
                if (cur.SequenceEqual(e)) continue;
                // one track serves slots that now want different events (rule C11/C12 shared tracks, Merge)
                var merged = Merge(kind, cur, e, ds2Events);
                if (conflictNotes.Count < 80) conflictNotes.Add($"{tr.Name} 0x{tid:X8} ({kind}, {wish.Count} slots): {Ev(cur)} + {clipByIndex[ci].Name[..11]}{(ev == null ? " (keeps DS2's)" : "")} {Ev(e)} → {Ev(merged)}");
                cur = merged;
            }
            if (cur!.SequenceEqual(ds2Events)) continue;
            tr.Events = cur!;
            newTracks[tid] = (tr, ds2Events, kind, wish.Select(x => x.Clip).ToList());
        }
        var repl = new Dictionary<int, byte[]>();
        foreach (var (tid, (tr, ds2Events, kind, cis)) in newTracks)
        {
            byte[] data = tr.Write();
            repl[trackBundle[tid]] = data;
            if (sizeFields.TryGetValue(tid, out var fields))
                foreach (int o in fields) net.PutU32(o, (uint)data.Length);
            foreach (int c in cis) clipByIndex[c].TrackLog.Add($"{tr.Name} ({kind}, ud {tr.UserData}{(cis.Count > 1 ? $", shared by {cis.Count} slots" : "")}): {Ev(ds2Events)} → {Ev(tr.Events)}");
        }
        // session 11 (track enemy11, G1): DS2's procedural / layered motion off the DS1 network (Ds1NetRules: NECK, HIPS, TERRAIN,
        // LM1, PB1, HB1, BL1)
        {
            var attackByIndex = res.Attacks?.Clips.Values.ToDictionary(c => c.Index) ?? [];
            Ds1NetRules.ClipFacts? Facts(int ci)
            {
                if (attackByIndex.TryGetValue(ci, out var co))
                {
                    var t = co.Timing; double d = t.Duration, h = t.FirstHit >= 0 && d > 0 ? t.FirstHit / d : 0;
                    double[] ev = h > 1e-4 && h < 1 - 1e-4 ? [h * d, (1 - h) * d] : [d];
                    return new(co.Clip, d, ev, Ds1NetRules.BlendSpan(ds1.TaeAnim(co.Clip)), true);
                }
                if (!clipByIndex.TryGetValue(ci, out var inf)) return null;
                if (inf.Ds1Id < 0) return new(-1, inf.Ds1Duration, [], -1);
                double[] evs = feetOf.TryGetValue(ci, out var ft) && ft.Sync.Count > 0 ? ft.Sync.Select(e => e.Item2 * inf.Ds1Duration).ToArray() : [inf.Ds1Duration];
                return new(inf.Ds1Id, inf.Ds1Duration, evs, Ds1NetRules.BlendSpan(ds1.TaeAnim(inf.Ds1Id)));
            }
            // the battle-stand loop (c1030 WalkStay a00_01_0010): its sync event is what entries from the base state are measured on
            double stand = -1;
            foreach (var (ci, inf) in clipByIndex)
                if (inf.Name.StartsWith("a00_01_0010", StringComparison.OrdinalIgnoreCase) && Facts(ci) is { } sf && sf.EventLengths.Length > 0) { stand = sf.EventLengths.Average(); break; }
            var nr = Ds1NetRules.Apply(net, Facts, new Ds1NetRules.Options { Template = "c1030", StandEvent = stand, IdleBlend = Ds1NetRules.BlendSpan(ds1.TaeAnim(0)) });
            res.Report.AddRange(nr.Select(l => "G1 " + l));
        }
        repl[netIndex] = net.X;
        nmbFile.Bytes = nmb.Rebuild(repl);
        string oldStem = Path.GetFileNameWithoutExtension(nmbFile.Name.Replace('\\', '/').Split('/')[^1]);
        nmbFile.Name = nmbFile.Name.Replace(oldStem, newChr);
        var byKind = newTracks.Values.GroupBy(v => v.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}");
        res.Report.Add($"clips: {res.Clips.Count} slots, {encoded.Count} distinct DS1 clips ({encoded.Count(e => e.Key.Loop)} locomotion loops started at the left touchdown) + {res.Clips.Count(c => c.Ds1Id == -1)} bind-pose (additive) slots; SourceAnim attribs patched {patched} (clip fractions reset to 0/1 on {fractionsReset}); event tracks rewritten {newTracks.Count}: {string.Join(", ", byKind)}; shared tracks merged {conflictNotes.Count}");
        if (conflictNotes.Count > 0) res.Report.Add("shared tracks whose slots want different events (merged): " + string.Join("; ", conflictNotes));

        // TAE ids → durations. Several clips can name one TAE id (c1030: id 0 = the idle and the additive/head-look slots);
        // the clip whose own name encodes the id (aXX_YY_ZZZZ → XX·1000000 + YY·10000 + ZZZZ: a00_03_0010 → 30010,
        // a65_10_1010 → 65101010) owns it, else the first.
        foreach (var c in res.Clips.OrderBy(c => c.TimeAct.Contains(NameId(c.Name)) ? 0 : 1))
            foreach (int ta in c.TimeAct)
                res.TaeIds.TryAdd(ta, new TaeSlot(c.Ds1Duration, c.Ds2Duration, c.NoAttack, c.Ds1Id, c.Loop ? c.Shift : 0, c.Frames, c.Fps));

        // the DS1 attack clips (track enemies): new NSA entries next to the template's, ids after the last
        if (res.Attacks != null)
        {
            var nsaFiles = bnd.Files.Where(f => f.Name.EndsWith(".nsa", StringComparison.OrdinalIgnoreCase)).ToList();
            string dir = nsaFiles[0].Name[..(Math.Max(nsaFiles[0].Name.LastIndexOf('\\'), nsaFiles[0].Name.LastIndexOf('/')) + 1)];
            int nextId = bnd.Files.Max(f => f.ID) + 1;
            foreach (var co in res.Attacks.Clips.Values.OrderBy(c => c.Index))
                bnd.Files.Add(new BinderFile(nsaFiles[0].Flags, nextId++, dir + co.Name, co.Nsa));
        }
        if (i1bReserved != null)
        {
            var id = NmbIdentity.Apply(nmbFile.Bytes, templateNmb, newChr, i1bReserved);
            if (id.Problems.Count > 0) throw new InvalidDataException($"{newChr}: {id.Summary}");
            nmbFile.Bytes = id.Nmb;
            res.NetworkAssetId = id.NetworkId;
            res.Report.Add(id.Summary);
        }
        byte[] outBnd = bnd.Write();
        res.Anibnd = comp != null ? DCX.Compress(outBnd, comp) : outBnd;
        if (idleFallbacks.Count > 0) res.Report.Add($"rule RF1: {idleFallbacks.Count} slots without a DS1 clip of their role play the DS1 idle a00_0000: {string.Join("; ", idleFallbacks)}");
        return res;
    }

    /// <summary>
    /// The TimeAct files for the 1:1 character, edited in place from the template's (<see cref="Ds2Tae"/>): every TAE
    /// animation a clip's TimeAct track names gets the DS1 clip's length (frames at 30 fps); _pl (<paramref name="gameplay"/>)
    /// keeps its events time-scaled by DS1/DS2 duration, except on no-attack slots (events dropped); _sfx/_snd events are
    /// dropped (DS1 sound/effect events are not translated yet).
    /// <para>Fix pass (session 8): the DS1 gameplay flags with no NMB track in the c1030 network (JT 24 Poise, 41
    /// Vulnerable, 67 Invincible excluding Throw Attacks, 39 hurtboxes off, 51, 7) have no DS2 equivalent we can name: the
    /// c103000_pl TAE carries events only on 87002/87005 (2501), the get-ups 160730/160760 (110100, 0-1.267 s) and the
    /// attacks (2200 hit, 110000, 120200, 280100 turn rate, 290002); the SOTFS TAE template names none of 110100 / 110000 /
    /// 120200 / 290002. So DS2's own events stay (time-scaled), rule C12e (U).</para>
    /// <para>Track chrsound (session 8): _snd and _sfx events are no longer just dropped: each TAE animation a clip names
    /// gets its DS1 clip's sound / effect events (<see cref="Ds1ChrSound.Translate"/>, rules S3-S7; loop events moved with
    /// the re-cut, S6), DS2's own events of the template removed. <paramref name="log"/> collects the per-animation notes
    /// (DS1 common sounds and effects not played). <paramref name="multiDummies"/> (fix pass): the dummy reference ids the
    /// model has more than once, where a DS2 effect event spawns at every dummy as DS1's does (rule S7). <paramref name="ownEvents"/>
    /// (merge9 stage 4): the character-range event names the bank's project defines (DS1's own + those copied from frpg_main, rule S5b); other character-range ids are not emitted (<see cref="Ds1ChrSound.Translate"/>).</para>
    /// <para>merge9 stage 4 (enemies x chrsound): the DS1 attack clips' own TAE animations (<see cref="Result.Attacks"/>) get
    /// their DS1 clip's _snd/_sfx events the same way (DS1's times: an attack clip plays whole from frame 0). The caller adds
    /// those animations to the _snd/_sfx template first (Ds2TaeFile needs every animation's data contiguous, and
    /// <see cref="Ds2Tae.SetEvents"/> appends at the end of the file, so it must come last); _pl keeps the enemies track's
    /// order (this edit, then Ds2TaeFile adds the attack animations with their gameplay events).</para>
    /// </summary>
    /// <param name="kind">"pl", "snd" or "sfx"</param>
    public static (byte[] Bytes, string Report) EditTae(byte[] template, Result r, string kind, Ds1ChrSource ds1, int ds1Chr, List<string>? log = null, IReadOnlyDictionary<(int Type, int Id), int>? commons = null, IReadOnlySet<int>? multiDummies = null, IReadOnlySet<string>? ownEvents = null)
    {
        var tae = new Ds2Tae(template);
        bool gameplay = kind == "pl";
        int lengths = 0, scaled = 0, cleared = 0, withEvents = 0, events = 0, own = 0, common = 0, unknown = 0, fxDropped = 0, attackAnims = 0, attackWith = 0;
        // merge9 stage 4 (enemies x chrsound): the DS1 attack clips' own TimeAct animations (Ds1Attacks, ids 65xxxxxx; the
        // caller adds them to the _snd/_sfx template with Ds2TaeFile before this edit) own their ids: a clip slot naming the
        // same id leaves it alone here, as on the enemies track, where AddAnimation replaced such a record after this edit
        HashSet<int> attackIds = gameplay || r.Attacks == null ? [] : r.Attacks.TaeAnims.Select(a => (int)a.Id).ToHashSet();
        // one TAE animation's _snd/_sfx events from its DS1 clip (rules S3-S7; loop events moved with the re-cut, S6)
        void Translate(int id, int ds1Id, int shift, int frames, double fps)
        {
            var t = ds1Id >= 0 ? Ds1ChrSound.Translate(ds1.TaeAnim(ds1Id), ds1Chr, shift, frames, fps, commons, multiDummies: multiDummies, ownEvents: ownEvents) : new Ds1ChrSound.Translation();
            var evs = kind == "snd" ? t.Snd : t.Sfx;
            if (tae.EventCount(id) > 0) cleared++;
            tae.SetEvents(id, evs, kind == "snd" ? Ds1ChrSound.SndGroup : Ds1ChrSound.SfxGroup);
            if (evs.Count > 0) { withEvents++; events += evs.Count; }
            own += t.Own; common += t.Common; unknown += t.Unknown; fxDropped += t.EffectsDropped;
            if (log != null && (kind == "snd" ? t.Snd.Count + t.Common + t.Unknown : t.Sfx.Count + t.EffectsDropped) > 0)
            {
                string src = ds1Id >= 0 ? Ds1ChrSource.ClipName(ds1Id) : "bind";
                var shown = kind == "snd"
                    ? t.Snd.Select(e => $"{Ds1ChrSound.Ds2EventName(e.Params)}@{e.Start.ToString("0.###", Inv)}").Concat(t.Notes.Where(n => !n.StartsWith("FFX")))
                    : t.Sfx.Select(e => $"{e.Type} sfx {BitConverter.ToInt32(e.Params, 4)} dummy {BitConverter.ToInt32(e.Params, 8)}@{e.Start.ToString("0.###", Inv)}-{e.End.ToString("0.###", Inv)}").Concat(t.Notes.Where(n => n.StartsWith("FFX")));
                log.Add($"{kind} {id} <- {src}{(shift != 0 ? $" (loop re-cut at DS1 frame {shift})" : "")}: {string.Join("; ", shown)}");
            }
        }
        foreach (var (id, s) in r.TaeIds.OrderBy(x => x.Key))
        {
            if (!tae.Records.ContainsKey(id) || attackIds.Contains(id)) continue;
            tae.SetLength(id, (int)Math.Round(s.Ds1 * 30)); lengths++;
            if (gameplay)
            {
                if (tae.EventCount(id) == 0) continue;
                if (s.NoAttack) { tae.ClearEvents(id); cleared++; }
                else if (s.Ds2 > 0) { tae.ScaleTimes(id, s.Ds1 / s.Ds2); scaled++; }
                continue;
            }
            Translate(id, s.Ds1Id, s.Shift, s.Frames, s.Fps);
        }
        // the DS1 attack clips (track enemies): each plays whole from frame 0, so its events keep DS1's times (shift 0); the
        // length is the one Ds1Attacks gave the animation
        if (!gameplay && r.Attacks != null)
            foreach (var co in r.Attacks.AllClips.OrderBy(c => c.TaeId))   // session 11 (burgchr11): + the rule CH5 slot variants
            {
                if (!tae.Records.ContainsKey(co.TaeId)) continue;
                int before = withEvents;
                Translate(co.TaeId, co.Clip, 0, co.Back.Frames, co.Back.Fps);
                attackAnims++; if (withEvents > before) attackWith++;
            }
        string atk = attackAnims > 0 ? $" (incl. {attackWith} of the {attackAnims} DS1 attack animations)" : "";
        string rep = gameplay
            ? $"{lengths} lengths set, {scaled} animations' events time-scaled, {cleared} animations' events dropped"
            : kind == "snd"
                ? $"{lengths} lengths set; DS2 template events removed on {cleared} animations; DS1 sound events → 201 on {withEvents} animations{atk} ({events} events: {own} DS1 character sounds, {common} DS1 common sounds copied into the character's project (rule S5); {unknown} not played)"
                : $"{lengths} lengths set; DS2 template events removed on {cleared} animations; DS1 effects → 2100/2104 on {withEvents} animations{atk} ({events} events; {fxDropped} DS1 FFX events not translated, rule S7)";
        return (tae.Bytes, rep);
    }

    /// <summary>DS1 TAE event names (Smithbox TAE.Template.DS1.xml, a data file of the installed modding tool) for reports.</summary>
    static readonly Dictionary<int, string> Ds1EventNames = new()
    {
        [0] = "JumpTable", [1] = "Attack", [2] = "Bullet", [5] = "CommonBehavior", [16] = "Blend", [66] = "SpEffect(MP)", [67] = "SpEffect",
        [101] = "FFX", [109] = "FFX109", [110] = "FFX110", [112] = "FFX(floor)", [128] = "Sound", [129] = "Sound(extra)",
        [225] = "StaminaRegen%", [226] = "Knockback%", [228] = "RagdollRevive", [233] = "DrawMask",
    };
    /// <summary>DS1 JumpTable ids (TAE.Template.DS1.xml: 23 "AI Continuation Attack Flag", 24 "Poise", 27 "Ignore Gravity",
    /// 39 "Disable Character Ragdoll Collision (includes Hurtboxes)", 41 "Vulnerable", 51 "Disable Fall Animation", 67
    /// "Invincible excluding Throw Attacks", 69 "Throw Death Animation Transition", 78 "AI Movement Anim Cancel", 79 "AI
    /// Dodge Anim Cancel").</summary>
    static readonly Dictionary<int, string> JumpTableNames = new()
    {
        [7] = "NoTurn", [8] = "Invincible", [12] = "Death", [19] = "NoMapCollision", [23] = "AIContinuationAttack", [24] = "Poise", [27] = "IgnoreGravity",
        [39] = "NoRagdollCollision", [41] = "Vulnerable", [51] = "NoFallAnim", [67] = "InvincibleExThrow", [69] = "ThrowDeathTransition", [74] = "LadderCollision",
        [78] = "AIMoveCancel", [79] = "AIDodgeCancel", [86] = "AIAttackCancel",
    };

    internal static string DescribeDs1Tae(TAE.Animation? a)
    {
        if (a == null) return "(no TAE entry)";
        var types = a.Events.GroupBy(e => e.Type).OrderBy(g => g.Key).Select(g => $"{(Ds1EventNames.TryGetValue(g.Key, out var nm) ? nm : g.Key.ToString(Inv))}×{g.Count()}");
        var jt = a.Events.Where(e => e.Type == 0).Select(e => BitConverter.ToInt32(e.GetParameterBytes(false), 0)).Distinct().Order()
            .Select(j => JumpTableNames.TryGetValue(j, out var nm) ? $"{j} {nm}" : j.ToString(Inv)).ToList();
        return string.Join(" ", types) + (jt.Count > 0 ? $"; JT {string.Join(", ", jt)}" : "");
    }

    /// <summary>The role table as Markdown rows (NOTES.md).</summary>
    public static IEnumerable<string> TableRows(Result r) =>
        r.Clips.OrderBy(c => c.Name, StringComparer.Ordinal).Select(c =>
            $"| {c.Name[..Math.Min(34, c.Name.Length)]} | {string.Join(",", c.TimeAct)} | {c.Ds2Duration.ToString("0.###", Inv)} | ({c.Ds2RootEnd.X.ToString("0.00", Inv)}, {c.Ds2RootEnd.Y.ToString("0.00", Inv)}) | {(c.Ds1Id == -1 ? "bind pose" : Ds1ChrSource.ClipName(c.Ds1Id))} | {c.Ds1Duration.ToString("0.###", Inv)} | ({c.Ds1RootEnd.X.ToString("0.00", Inv)}, {c.Ds1RootEnd.Y.ToString("0.00", Inv)}) | {c.Role} | {c.Status} | {c.Ds1Tae} |");
}
