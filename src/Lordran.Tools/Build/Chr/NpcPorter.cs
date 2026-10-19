using System.Numerics;
using Lordran.Tools.Build.Events;
using Lordran.Tools.Build.Maps;
using Lordran.Tools.Formats;
using SoulsFormats;
using static Lordran.Tools.Build.ParamCells;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// DS1's NPCs as DS2 human NPCs (session 8, track npc, step 1: present, peaceful, dressed; talk is step 2).
/// <para>How DS2 builds a human NPC (`npc-survey`: every vanilla map's regist rows whose EnemyParam uses model 1; the
/// decompiled loader of NPCGenerateParam FUN_14035a650): GeneratorRegistParam (EnemyId, High/LowLogicId = the NPC's
/// own ai ESDs, startUpStateType = its idle state) → EnemyParam (modelId 1 = c0001, the player body; animId 100;
/// npcStatusId; categoryType 4, groupType 2) → NPCGenerateParam row with the EnemyParam's id (chrId, gender, equipType,
/// faceType, faceId → parts/face/FC_%4d_%s.bnd, skin colour, 6 weapons L1-3/R1-3, head/body/arm/leg ArmorParam ids;
/// equipType 0 = dressed from this row) and NpcPlayerStatusParam row npcStatusId (levels, belt items, equipment for the
/// player-like combat AI; armour 11001100/11002100… = the naked body pieces). Talk: EventParam rows with ownerType 2
/// (character = generator id), resourceType 1 (talk ESD), stateMapId = the talk_mAA ESD group (Saulden: 111220 →
/// group 7410 of talk_m10_04_00_00 on generator 100). Written by TalkBuilder (step 2) once the talk group exists: a row
/// pointing at a missing talk group would risk DS2's all-or-nothing event setup (session 5: one bad row aborts every
/// event of the map).</para>
/// <para>Death and spawning (fix pass, review: the first version cloned the enemies' generator, which spawns on approach
/// (start condition 1, 50 m) and never stays dead, and wrote DS1's lot 6020 as an unconditional 100% drop, so Oscar
/// could be farmed and hand out the F2 East Key twice). Vanilla NPC generators (`npc-survey`): Saulden's 100
/// respawnDisableFlag 104140 + start condition 8 (flag off) on 104140, a flag no vanilla script sets (the engine does
/// when the generator's character dies; his talk reads it as "dead": 2147483638); always-present characters 0/0
/// (no condition: spawned at load). Rule: the DS1 NPC's dead flag (<see cref="FindDeath"/>: DS1's own death event,
/// e.g. Oscar 11810520(6023, 1060, 1074, 1073)) → respawnDisableFlag and start condition 8 on it; no DS1 death event
/// (Anastacia: immortal at Firelink in DS1) → spawned at load without condition. Drops: DS1's NpcParam lot chains
/// (6020 → 6021 → 6022, each row once by its getItemFlagId); DS2 lots have no flags, so the rows whose flag a DS1 talk
/// gift shares go to the NPC's talk machine (TalkTranslator's death branch, by the gift rule) when the NPC has a
/// translated talk, the others into EnemyParam's drop lot (once: the NPC stays dead), without DS2's random Lifegem
/// (vanilla NPC lots have none: Saulden 74100000, Maughlin 76100000).</para>
/// <para>DS1 side: the MSB enemy part (c0000 = player body: NPCParamID, CharaInitID, TalkID, InitAnimID, position,
/// rotation, entity); CharaInitParam (equipment, npcPlayerSex 1 = male, npcPlayerFaceGenId, body scale); NpcParam
/// (souls, drops). Human-rig DS1 characters with their own model (Anastacia c2750 = DS1's Fire Keeper parts baked into
/// one chr; the Undead Merchant c2510) become one DS2 body part on c0001 with empty head/arm/leg parts
/// (<see cref="ChrNpc"/>).</para>
/// <para>Ids (rules): EnemyParam = NPCGenerateParam = NpcPlayerStatusParam = <see cref="Id"/> (900,000 + DS1 NpcParam %
/// 100,000; checked free at build); generator = DS1 entity (<see cref="EntityRules.Generator"/>: Oscar 6023); drops
/// ItemLotParam2_Chr EnemyParam·100 (as the enemies).</para>
/// <para>Idle pose: DS1 InitAnimID 78xx are c0000 sitting/kneeling clips (7805 Oscar slumped against the wall, 7810 the
/// Crestfallen Warrior on the steps, 7825/7830/7835 sitting, 7840 crouching, 7880 kneeling) → DS2 startUpStateType 13
/// (Saulden's sitting idle, the only sitting state DS2's NPC AI has); none → 0 (standing). 1:1 needs DS1's own clips
/// (c0000 TAE 78xx in c0000_a7x.anibnd) through the Morpheme encoder (rulebook §5, U); recorded per NPC in the build log.</para>
/// </summary>
public static class NpcPorter
{
    /// <summary>A DS1 NPC part to place: DS1 map, MSB entity, and why it is present at the start (census). Female: the sex
    /// of a human-rig chr NPC (DS1's chr params carry none; c0000 NPCs take CharaInitParam.npcPlayerSex), null = male.</summary>
    public sealed record Npc(string Ds1Map, int Entity, string Why, bool? Female = null);

    /// <summary>DS1 Asylum NPCs present on the first visit (census 01-asylum-firelink.md §1.6): Oscar (c0000_0002).
    /// Hollow Oscar (6024) only appears on the return trip after flag 1061; Snuggly (6450) is an invisible talk dummy.</summary>
    public static readonly Npc[] Asylum =
    [
        new("m18_01_00_00", 6023, "Oscar, Knight of Astora: behind the boulder wall, dying (first visit)"),
        // session 11 (track ret11), rule N-H: hollow Oscar (c0000_0003, NpcParam 6021): present iff (1073 & 11810000) | 1061 (rule W1:
        // generator start condition), hostile (rule N-H2), on Oscar's own network standing (rule I1d-P)
        new("m18_01_00_00", 6024, "Oscar, hollowed: his room on the return trip after his death (W1: (1073 & 11810000) | 1061), hostile"),
    ];

    /// <summary>DS1 Firelink NPCs present from the start (census §3.4: no enable condition): Crestfallen Warrior,
    /// Petrus, Anastacia (cell), the Undead Merchant (aqueduct end). Everyone else arrives on quest flags (step 2+).
    /// The merchant is the female one (census correction, NOTES §12: her talk t102624 sells shop 1200-1299 "[Undead
    /// Merchant - Female]" with "dearie" / "Vee hee hee"; the male Burg merchant shares her model c2510 with NpcParam
    /// 251001, which shows his katana).</summary>
    public static readonly Npc[] Firelink =
    [
        new("m10_02_00_00", 6270, "Crestfallen Warrior: steps beside the bonfire, present from the start"),
        new("m10_02_00_00", 6080, "Petrus of Thorolund: upper ruins, present from the start"),
        new("m10_02_00_00", 6060, "Anastacia of Astora (Fire Keeper): cell below the terrace, always present", Female: true),
        new("m10_02_00_00", 6240, "Undead Merchant (female): aqueduct end towards the Burg, always present", Female: true),
    ];

    /// <summary>Session 11 (burg11): the NPCs present at the start of a map ported by MapHosts' generic route
    /// (Mappings\ds1-npcs.tsv: DS1 map, entity, sex, why).</summary>
    public static Npc[] Table(string ds1Map)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Mappings", "ds1-npcs.tsv");
        if (!File.Exists(path)) return [];
        return File.ReadLines(path).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split('\t'))
            .Where(c => c.Length >= 4 && c[0].Equals(ds1Map, StringComparison.OrdinalIgnoreCase))
            .Select(c => new Npc(c[0], int.Parse(c[1]), c[3], c[2] switch { "f" => true, "m" => false, _ => null })).ToArray();
    }

    /// <summary>A DS1 NPC's death (DS1 map event: HP ratio ≤ 0 → Batch Set Event Flags(First..Last, OFF) → Set Event
    /// Flag(DeadFlag, ON)), DS1 ids; TalkDropLots = its DS1 drop rows (lot chains) that the talk machine gives (their flag
    /// is also a talk gift's), each with whether it follows the first row of its chain.</summary>
    public sealed record Ds1Death(int Event, int DeadFlag, int First, int Last, IReadOnlyList<(int Row, bool Chained)> TalkDropLots);

    /// <summary>One placed NPC: its generator (written by AsylumEnemies/EnemyTest with the map's enemies) and its DS1
    /// death (null: none in DS1).</summary>
    public sealed record Placed(int Entity, string Name, EnemyTest.Custom Custom, Ds1Death? Death = null);

    /// <summary>
    /// DS1 NpcParam id → our EnemyParam = NPCGenerateParam = NpcPlayerStatusParam id: 900,000 + id % 100,000 (Oscar 6020
    /// → 906020, Anastacia 275000 → 975000, the Undead Merchant 251000 → 951000). DS2's EnemyParam ends at 898000 and
    /// uses 6200-6330 itself (the first rule, the DS1 id as is, hit DS2's 6270 with the Crestfallen Warrior); DS1's NPC
    /// rows are 6000-6999 (c0000 NPCs) and x00000-x99000 chr rows, distinct modulo 100,000. ×100 (regist and drop-lot
    /// rows, as the enemies) stays inside int32.
    /// </summary>
    public static int Id(int ds1NpcParam) => 900_000 + ds1NpcParam % 100_000;

    // DS2 templates (vanilla Majula NPCs): EnemyParam/NPCGenerateParam 741000 + NpcPlayerStatusParam 7410 = Crestfallen
    // Saulden (sits: startUpStateType 13; logic 741010/741000). Both his ai ESDs only branch on startUpStateType for idling.
    const int TemplateEnemy = 741000, TemplateStatus = 7410, HighLogic = 741010, LowLogic = 741000;
    const byte Sitting = 13, Standing = 0;
    /// <summary>DS2's naked body pieces (ArmorParam "[Body]" rows 1001): male head/body/arm/leg, female = +1000.</summary>
    static int Naked(int slot, bool female) => 11001100 + (female ? 1000 : 0) + slot;
    /// <summary>DS2's "no weapon" WeaponParam (NPCGenerateParam's unused slots; NpcPlayerStatusParam uses 3405000).</summary>
    const int NoWeapon = 3400000, NoWeaponStatus = 3405000;
    /// <summary>
    /// DS2's two NPC face kinds (`npc-survey`, every NPCGenerateParam row): faceType 0 = a custom face model
    /// parts/face/FC_{faceId}_{M|F}.bnd (14 exist: Saulden 7410, Maughlin 7610 …), faceType 1 = a FaceGen preset
    /// (facePresetNo; faceId -1: the phantoms 813000/833000/836x00/837000 use presets 0 and 3). Until DS1's FaceGen faces
    /// are converted into FC models (NOTES: FaceGen), DS1 NPCs get preset 3, the phantoms' generic face, rather than a
    /// named DS2 NPC's face.
    /// </summary>
    public const int InterimFacePreset = 3;

    /// <param name="talk">The build translates the NPCs' talk (build-pack without --no-talk): drop rows whose flag a talk
    /// gift shares go to the talk machine; without talk they stay in EnemyParam's drop lot (no gift can duplicate them).</param>
    public static List<Placed> Build(Pack pack, Ds1Source ds1, ParamStore ds2, ArmorPorter armour, PlayerRig rig, MapBuilder.Options map, IEnumerable<Npc> npcs, bool talk = true)
    {
        var msb = MSB1.Read(Path.Combine(ds1.Root, "map", "MapStudio", map.Ds1Map + ".msb"));
        var result = new List<Placed>();
        // session 9 (track npcplace): the map's DS2 collision as written by MapBuilder (placement rules P1/P2, Ds1Placement)
        var probe = FloorProbe.FromPack(pack, map.Ds2Map);
        int logicGroup = 20; // vanilla NPCs: 20, 25, 30 … (enemies cloned from TB 1600 share 5)
        foreach (var npc in npcs.Where(n => n.Ds1Map == map.Ds1Map))
        {
            var part = msb.Parts.Enemies.FirstOrDefault(e => e.EntityID == npc.Entity) ?? throw new InvalidDataException($"{map.Ds1Map}: no MSB character with entity {npc.Entity}");
            // the talk machine exists when the build translates talk and the NPC has one (own TalkID or a talk dummy)
            bool hasTalk = talk && Talk.TalkBuilder.Talkers(msb, [(npc.Entity, 0, "", null)]).Count > 0;
            var p = Place(pack, ds1, ds2, armour, rig, map.Ds1Map, part, npc.Female, hasTalk, logicGroup, out string note, probe);
            logicGroup += 5;
            result.Add(p);
            pack.Note($"{map.Ds2Map} NPC {p.Name} (DS1 {part.Name}, entity {npc.Entity}, {npc.Why}): {note}");
            Console.WriteLine($"  {map.Ds2Map} NPC {p.Name}: generator {p.Custom.NewGenerator}, EnemyParam {p.Custom.EnemyId}, startUp {p.Custom.StartUpStateType}, dead flag {p.Custom.DeadFlag}");
        }
        return result;
    }

    /// <summary>
    /// The DS1 NPC's death event, read from DS1's own map EMEVD: an event whose body has "IF HP Ratio(X, ≤, 0)" (4:2,
    /// comparison 5, ratio 0) and after it "Set Event Flag(Y, ON)" (2003:2) with X and Y event parameters (optionally
    /// "Batch Set Event Flags(A, B, OFF)" (2003:22) = the NPC's state block), and an Initialize Event (2000:0) of it whose
    /// X is this entity. Firelink: 11020530(6240, 1410, 1413, 1412) Undead Merchant, (6270, 1460, 1464, 1462) Crestfallen
    /// Warrior, 11020567(6080, 1190, 1209, 1196) Petrus; Asylum: 11810520(6023, 1060, 1074, 1073) Oscar. The hostility
    /// events (11020510/11020504: HP ratio ≤ 0.9) don't match. Null: no such event (Anastacia, immortal in DS1).
    /// </summary>
    public static Ds1Death? FindDeath(string dsrRoot, string ds1Map, int entity)
    {
        string path = Path.Combine(dsrRoot, "event", ds1Map + ".emevd.dcx");
        if (!File.Exists(path)) return null;
        var emevd = EMEVD.Read(path);
        static int? Param(EMEVD.Event ev, int instruction, int targetByte) =>
            ev.Parameters.FirstOrDefault(p => p.InstructionIndex == instruction && p.TargetStartByte == targetByte) is { } p ? (int)p.SourceStartByte : null;
        var inits = emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2000 && i.ID == 0 && i.ArgData.Length >= 8).ToList();
        foreach (var ev in emevd.Events)
        {
            var ins = ev.Instructions;
            int hp = ins.FindIndex(i => i.Bank == 4 && i.ID == 2 && i.ArgData.Length >= 16 && i.ArgData[8] == 5 && BitConverter.ToSingle(i.ArgData, 12) == 0f && Param(ev, ins.IndexOf(i), 4) != null);
            if (hp < 0) continue;
            int set = ins.FindIndex(hp + 1, i => i.Bank == 2003 && i.ID == 2 && i.ArgData.Length >= 5 && i.ArgData[4] == 1 && Param(ev, ins.IndexOf(i), 0) != null);
            if (set < 0) continue;
            int batch = ins.FindIndex(hp + 1, i => i.Bank == 2003 && i.ID == 22 && i.ArgData.Length >= 9 && i.ArgData[8] == 0);
            int entityAt = Param(ev, hp, 4)!.Value, deadAt = Param(ev, set, 0)!.Value;
            int? firstAt = batch >= 0 ? Param(ev, batch, 0) : null, lastAt = batch >= 0 ? Param(ev, batch, 4) : null;
            foreach (var init in inits.Where(i => BitConverter.ToUInt32(i.ArgData, 4) == ev.ID))
            {
                int Arg(int at) => 8 + at + 4 <= init.ArgData.Length ? BitConverter.ToInt32(init.ArgData, 8 + at) : int.MinValue;
                if (Arg(entityAt) != entity) continue;
                int dead = Arg(deadAt);
                return new Ds1Death((int)ev.ID, dead, firstAt is int f ? Arg(f) : dead, lastAt is int l ? Arg(l) : dead, []);
            }
        }
        return null;
    }

    /// <summary>
    /// Session 9 (track npcplace): the DS1 NPC's hostility flag, read from DS1's own map EMEVD: an event with "IF HP
    /// Ratio(X, ≤, r)" for 0 &lt; r &lt; 1 (4:2, comparison 5), "Set Event Flag(Y, ON)" (2003:2) and "Set Character Team
    /// Type(X, …)" (2004:2), X and Y event parameters, initialised for this entity (Firelink 11020510(6240, 1411):
    /// HP ≤ 0.9 after the player's damage → flag on, team type Hostile Ally). The first such event in DS1's order wins:
    /// Petrus has two, 11020502(6080, 1197) for his Firelink states 1202/1193 and 11020503(6080, 1195) for state 1194 →
    /// 1197. DS2's NPC generators carry a flag pair for the NPC AI (vanilla Majula: logicEventFlag3 from the NPC's 1037xx
    /// family, logicEventFlag4 from its 1042xx dead family; their exact meaning is U, EnemyTest.Custom.HostileFlag).
    /// Null: none in DS1.
    /// </summary>
    public static int? FindHostile(string dsrRoot, string ds1Map, int entity)
    {
        string path = Path.Combine(dsrRoot, "event", ds1Map + ".emevd.dcx");
        if (!File.Exists(path)) return null;
        var emevd = EMEVD.Read(path);
        static int? Param(EMEVD.Event ev, int instruction, int targetByte) =>
            ev.Parameters.FirstOrDefault(p => p.InstructionIndex == instruction && p.TargetStartByte == targetByte) is { } p ? (int)p.SourceStartByte : null;
        var inits = emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2000 && i.ID == 0 && i.ArgData.Length >= 8).ToList();
        foreach (var ev in emevd.Events)
        {
            var ins = ev.Instructions;
            int hp = ins.FindIndex(i => i.Bank == 4 && i.ID == 2 && i.ArgData.Length >= 16 && i.ArgData[8] == 5 && BitConverter.ToSingle(i.ArgData, 12) is > 0f and < 1f && Param(ev, ins.IndexOf(i), 4) != null);
            if (hp < 0) continue;
            int team = ins.FindIndex(hp + 1, i => i.Bank == 2004 && i.ID == 2 && Param(ev, ins.IndexOf(i), 0) == Param(ev, hp, 4));
            int set = ins.FindIndex(hp + 1, i => i.Bank == 2003 && i.ID == 2 && i.ArgData.Length >= 5 && i.ArgData[4] == 1 && Param(ev, ins.IndexOf(i), 0) != null);
            if (team < 0 || set < 0) continue;
            int entityAt = Param(ev, hp, 4)!.Value, flagAt = Param(ev, set, 0)!.Value;
            foreach (var init in inits.Where(i => BitConverter.ToUInt32(i.ArgData, 4) == ev.ID))
            {
                int Arg(int at) => 8 + at + 4 <= init.ArgData.Length ? BitConverter.ToInt32(init.ArgData, 8 + at) : int.MinValue;
                if (Arg(entityAt) == entity) return Arg(flagAt);
            }
        }
        return null;
    }

    /// <summary>
    /// Session 11 (track ret11), rule N-H2: an NPC DS1 sets hostile with a literal "Set Character Team Type(entity, 9 Hostile
    /// Ally)" (2004:2; m18_01 event 50 [17] on hollow Oscar 6024, every load) gets as its hostility flag the persistent flag DS1
    /// sets ON (literal 2003:2) in the event that enables it (2004:5(X, 1) with X = this entity through the event's
    /// initialisation: 11810531(6024) sets 1061 and enables him), i.e. a flag that is ON whenever the NPC is present. Null when
    /// DS1 has no such team-type command or no such enabling event.
    /// </summary>
    public static int? HostileAtLoad(string dsrRoot, string ds1Map, int entity)
    {
        string path = Path.Combine(dsrRoot, "event", ds1Map + ".emevd.dcx");
        if (!File.Exists(path)) return null;
        var emevd = EMEVD.Read(path);
        bool hostile = emevd.Events.SelectMany(e => e.Instructions).Any(i => i.Bank == 2004 && i.ID == 2 && i.ArgData.Length >= 5
            && BitConverter.ToInt32(i.ArgData, 0) == entity && i.ArgData[4] == 9);
        if (!hostile) return null;
        static int? Param(EMEVD.Event ev, int instruction, int targetByte) =>
            ev.Parameters.FirstOrDefault(p => p.InstructionIndex == instruction && p.TargetStartByte == targetByte) is { } p ? (int)p.SourceStartByte : null;
        var inits = emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2000 && i.ID == 0 && i.ArgData.Length >= 8).ToList();
        foreach (var ev in emevd.Events)
        {
            var ins = ev.Instructions;
            int en = ins.FindIndex(i => i.Bank == 2004 && i.ID == 5 && i.ArgData.Length >= 5 && i.ArgData[4] == 1 && Param(ev, ins.IndexOf(i), 0) != null);
            if (en < 0) continue;
            int set = ins.FindIndex(i => i.Bank == 2003 && i.ID == 2 && i.ArgData.Length >= 5 && i.ArgData[4] == 1);
            if (set < 0) continue;
            int entityAt = Param(ev, en, 0)!.Value;
            foreach (var init in inits.Where(i => BitConverter.ToUInt32(i.ArgData, 4) == ev.ID))
                if (8 + entityAt + 4 <= init.ArgData.Length && BitConverter.ToInt32(init.ArgData, 8 + entityAt) == entity)
                    // the flag literal, or the initialisation's argument (11810531(6024, 1060, 1074, 1061): X12 = 1061)
                    return Param(ev, set, 0) is int fa ? (8 + fa + 4 <= init.ArgData.Length ? BitConverter.ToInt32(init.ArgData, 8 + fa) : null) : BitConverter.ToInt32(ins[set].ArgData, 0);
        }
        return null;
    }

    /// <summary>DS1 item flags of the talk gifts (common.emevd event 0: Initialize Event(slot, 911, flag, lot) → every row
    /// of the lot chain): a drop row with one of these flags can come from the talk as well.</summary>
    static HashSet<int> TalkGiftFlags(Ds1Source ds1)
    {
        var lots = ds1.Params.Params["ItemLotParam"];
        var flags = new HashSet<int>();
        foreach (int lot in Talk.TalkBuilder.GiftLots(ds1.Root).Values)
            foreach (int k in Talk.TalkBuilder.Chain(ds1, lot))
                if (I(lots[k]!, "getItemFlagId") is int f and > 0) flags.Add(f);
        return flags;
    }

    /// <summary>An NPC's DS1 drop rows (its NpcParam lot chains): those for EnemyParam's drop lot, and those the talk
    /// machine's death branch gives (<paramref name="talkGives"/>: the NPC has a translated talk and a DS1 death; rows whose
    /// item flag a talk gift shares, with whether they follow their chain's first row).
    /// A chained row for EnemyParam's lot whose flag a DS1 map event sets (DS1's "not this time": Oscar's Big Pilgrim's Key
    /// 6021, flag 50000081 pre-set by 11810300 unless the demon died early) is left out and listed in <paramref name="notes"/>:
    /// a DS2 lot can't test the flag (the talk machine's gift rule makes the same decision for its rows).</summary>
    public static (List<int> LotRows, List<(int Row, bool Chained)> TalkRows) SplitDrops(Ds1Source ds1, string ds1Map, PARAM.Row npcRow, bool talkGives, List<string>? notes = null)
    {
        var dsLots = ds1.Params.Params["ItemLotParam"];
        HashSet<int> giftFlags = talkGives ? TalkGiftFlags(ds1) : [];
        var preset = PresetFlags(ds1.Root, ds1Map);
        var lotRows = new List<int>();
        var talkRows = new List<(int Row, bool Chained)>();
        for (int k = 1; k <= 6; k++)
        {
            int lot = I(npcRow, $"itemLotId_{k}");
            if (lot <= 0 || dsLots[lot] == null) continue;
            var chain = Talk.TalkBuilder.Chain(ds1, lot);
            for (int c = 0; c < chain.Count; c++)
            {
                int flag = I(dsLots[chain[c]]!, "getItemFlagId");
                if (flag > 0 && giftFlags.Contains(flag)) talkRows.Add((chain[c], c > 0));
                else if (c > 0 && flag > 0 && preset.Contains(flag)) notes?.Add($"drop row {chain[c]} left out: DS1 sets its flag {flag} in a map event (DS1's 'not this time'; a DS2 lot can't test it)");
                else lotRows.Add(chain[c]);
            }
        }
        return (lotRows, talkRows);
    }

    /// <summary>DS1 flags any event of the DS1 map sets ON with a literal id (Set Event Flag 2003:2, Batch Set 2003:22).</summary>
    static HashSet<int> PresetFlags(string dsrRoot, string ds1Map)
    {
        var flags = new HashSet<int>();
        string path = Path.Combine(dsrRoot, "event", ds1Map + ".emevd.dcx");
        if (!File.Exists(path)) return flags;
        var defs = Commands.EmevdDump.Defs();
        foreach (var ins in EMEVD.Read(path).Events.SelectMany(e => e.Instructions))
        {
            if (ins.Bank != 2003 || ins.ID is not (2 or 22)) continue;
            var v = EmevdTranslator.Decode(defs, ins);
            if (ins.ID == 2 && v.Length >= 2 && v[1] == 1) flags.Add((int)v[0]);
            if (ins.ID == 22 && v.Length >= 3 && v[2] == 1)
                for (long f = (long)v[0]; f <= (long)v[1] && f - (long)v[0] < 1000; f++) flags.Add((int)f);
        }
        return flags;
    }

    static Placed Place(Pack pack, Ds1Source ds1, ParamStore ds2, ArmorPorter armour, PlayerRig rig, string ds1Map, MSB1.Part.Enemy part, bool? censusFemale, bool hasTalk, int logicGroup, out string note, FloorProbe? probe = null)
    {
        var notes = new List<string>();
        int id = Id(part.NPCParamID);
        foreach (string param in new[] { "EnemyParam", "NPCGenerateParam", "NpcPlayerStatusParam" })
            if (ds2.Params[param][id] != null) throw new InvalidOperationException($"{param} {id} exists in DS2: the NPC id rule needs another range");
        var npcRow = ds1.Params.Params["NpcParam"][part.NPCParamID] ?? throw new KeyNotFoundException($"DS1 NpcParam {part.NPCParamID}");
        string name = ds1.Params.RowName("NpcParam", part.NPCParamID) is { Length: > 0 } n ? n : part.Name;

        // ---- appearance: equipment (CharaInitParam) for c0000 NPCs, the converted model for human-rig chrs
        int[] armourIds = [-1, -1, -1, -1];
        int right = NoWeapon, left = NoWeapon, rightStatus = NoWeaponStatus, leftStatus = NoWeaponStatus;
        bool female;
        if (part.ModelName == "c0000")
        {
            var init = ds1.Params.Params["CharaInitParam"][part.CharaInitID] ?? throw new KeyNotFoundException($"DS1 CharaInitParam {part.CharaInitID}");
            female = I(init, "npcPlayerSex") == 0;
            string[] fields = ["equip_Helm", "equip_Armer", "equip_Gaunt", "equip_Leg"];
            for (int s = 0; s < 4; s++)
            {
                int ds1Armour = I(init, fields[s]);
                if (ds1Armour <= 0) { notes.Add($"{fields[s]} none"); continue; }
                // session 11 (outfit11), rule N-OUT1: DS1's own mesh, also for a piece DS2 shares (NPC-only ArmorParam)
                armourIds[s] = armour.NpcArmorParam(ds1Armour, out string how);
                notes.Add($"{fields[s]} {ds1Armour} {ds1.Text("Armor_name_", ds1Armour / 1000 * 1000)} → ArmorParam {armourIds[s]} ({how})");
            }
            (right, rightStatus) = Weapon(pack, ds1, I(init, "equip_Wep_Right"), notes, "right");
            (left, leftStatus) = Weapon(pack, ds1, I(init, "equip_Wep_Left"), notes, "left");
            notes.Add($"face: DS1 FaceGenParam {I(init, "npcPlayerFaceGenId")} (hair style {I(ds1.Params.Params["FaceGenParam"][I(init, "npcPlayerFaceGenId")]!, "hairStyle")}) not converted yet → DS2 FaceGen preset {InterimFacePreset} (interim)");
        }
        else
        {
            // the chr's own face is part of its body mesh; the empty head part (full-helm interfere) hides DS2's preset face
            female = censusFemale ?? false;
            ChrNpc(pack, ds1, armour, rig, part.ModelName, npcRow, female, armourIds, notes);
        }

        // ---- death: DS1's own death event gives the dead flag (generator) and the state block (talk death branch)
        var death = FindDeath(ds1.Root, ds1Map, part.EntityID);
        int deadFlag = death != null && FlagRules.Any(death.DeadFlag) is int df ? df : 0;
        notes.Add(death != null
            ? $"death: DS1 event {death.Event} (HP ratio ≤ 0 → flags {death.First}-{death.Last} off, dead flag {death.DeadFlag} on) → generator respawnDisableFlag + start condition 8 on {deadFlag}"
            : "death: no DS1 death event (immortal in DS1) → generator spawns at load without a condition, no dead flag");

        // ---- EnemyParam: Saulden's row (human NPC body, NPC category/group), DS1 souls and drops
        var enemy = pack.AddRow("EnemyParam", pack.Param("EnemyParam")[TemplateEnemy]!, id, $"[Lordran] {name}");
        Set(enemy, "npcStatusId", id);
        Set(enemy, "dropSoulValue", Ds1Calibration.Souls(npcRow));   // bal11 rule BAL2
        // DS1's NpcParam lot chains (each row once by its getItemFlagId): rows a talk gift shares → the talk machine
        var (lotRows, talkRows) = SplitDrops(ds1, ds1Map, npcRow, hasTalk && death != null, notes);
        if (talkRows.Count > 0)
            notes.Add($"drop rows {string.Join(", ", talkRows.Select(r => r.Row))}: their item flags are also talk gifts' (a DS2 lot can't gate them) → given by the talk machine's death branch by the gift rule");
        if (death != null) death = death with { TalkDropLots = talkRows };
        var drops = Ds1Drops.ResolveRows(ds1, lotRows, pack, notes);
        if (drops.Count > 0)
        {
            int lotId = id * 100;
            if (ds2.Params["ItemLotParam2_Chr"][lotId] != null) throw new InvalidOperationException($"ItemLotParam2_Chr {lotId} exists in DS2");
            var lots = pack.Param("ItemLotParam2_Chr");
            Ds1Drops.Write(pack, lots[I(enemy, "itemLotId")] ?? lots[1105000]!, lotId, $"[Lordran] {name} drops", drops, lifegem: false);
            Set(enemy, "itemLotId", lotId);
            notes.Add($"drops lot {lotId} (DS1 rows {string.Join(", ", lotRows)}; once: the NPC stays dead): " + string.Join("; ", drops.Select(d => $"{d.ItemId} {d.Percent:0.#}%")));
        }
        else
            Set(enemy, "itemLotId", 0); // no DS1 drops: not the template's (Saulden's own lot 74100000)
        // NPC HP: DS1 human NPCs have NpcParam hp 0 (their HP comes from their CharaInitParam level/vitality through DS1's
        // player HP curve); DS2's NPC baseline (Saulden 1320) stays until the curve is ported.

        // ---- NPCGenerateParam (row = EnemyParam id): what DS2 dresses the NPC in (FUN_14035a650 reads weapons at
        // +0x1C..+0x30 into slots L1 R1 L2 R2 L3 R3 and armour at +0x34..+0x40)
        var gen = pack.AddRow("NPCGenerateParam", pack.Param("NPCGenerateParam")[TemplateEnemy]!, id, $"[Lordran] {name}");
        Set(gen, "chrId", id);
        Set(gen, "gender", female ? 1 : 0);
        Set(gen, "equipType", 0);
        Set(gen, "faceType", 1);
        Set(gen, "faceId", -1);
        Set(gen, "facePresetNo", InterimFacePreset);
        // skin tint offsets: neutral (Carhillion/Magerold use 0,0,0; the template's Saulden -0.2/-0.14/-0.1 would darken
        // DS1's own skin textures, which the converted parts carry)
        foreach (string f in new[] { "skinR", "skinG", "skinB" }) Set(gen, f, 0);
        Set(gen, "equipWeaponIdR1", right); Set(gen, "equipWeaponIdL1", left);
        foreach (string f in new[] { "equipWeaponIdL2", "equipWeaponIdL3", "equipWeaponIdR2", "equipWeaponIdR3" }) Set(gen, f, NoWeapon);
        string[] genArmour = ["equipHeadId", "equipBodyId", "equipArmId", "equipLegId"];
        for (int s = 0; s < 4; s++) Set(gen, genArmour[s], armourIds[s]); // -1 = bare (DS2's own NPCs: Rosabeth's head, Carhillion's head)

        // ---- NpcPlayerStatusParam (row = npcStatusId): the same outfit for the player-like status (naked pieces when bare)
        var status = pack.AddRow("NpcPlayerStatusParam", pack.Param("NpcPlayerStatusParam")[TemplateStatus]!, id, $"[Lordran] {name}");
        Set(status, "genderType", female ? 1 : 0);
        Set(status, "bodyType", female ? 1 : 0);
        Set(status, "faceId", 0); // vanilla rows of preset-faced NPCs: 0 or an unrelated FC id (Felicia 8130); NPCGenerateParam decides
        Set(status, "rightWeapon01", rightStatus); Set(status, "leftWeapon01", leftStatus);
        foreach (string f in new[] { "rightWeapon02", "rightWeapon03", "leftWeapon02", "leftWeapon03" }) Set(status, f, NoWeaponStatus);
        string[] statusArmour = ["headArmor", "bodyArmor", "armArmor", "legArmor"];
        for (int s = 0; s < 4; s++) Set(status, statusArmour[s], armourIds[s] > 0 ? armourIds[s] : Naked(s, female));

        // ---- generator: DS1 position/facing, the NPC AI with DS1's idle mapped to DS2's
        byte startUp = part.InitAnimID is >= 7800 and < 7900 ? Sitting : part.ModelName == "c2750" ? Sitting : Standing;
        // session 9 (track npcidle): a c0000 NPC whose InitAnimID has a slot in the DS1-NPC network plays DS1's own clip
        // there (NpcIdle: EnemyParam animId → network c8001, startUpStateType → the slot); others keep the stand-in
        var idleSlot = part.ModelName == "c0000" ? NpcIdle.For(part.InitAnimID) : null;
        if (idleSlot != null)
        {
            NpcIdle.Ensure(pack, ds1.Root);
            Set(enemy, "animId", NpcIdle.AnimId);
            startUp = (byte)idleSlot.Category;
        }
        // session 9 (track npcpose2), rule I1d (default): the NPC's own network cloned from a DS2 template with the human rig
        // (Oscar → c8002 from Drummond's c7240), the generic NPC class (animId ≠ 100), its StartupType slot = DS1's clip;
        // logic = the template's AI copy (rule AI-N1). commonId / moveId stay the template row's 800000 (as s9-12/s9-17, V on
        // the generic class); npcStatusId, NPCGenerateParam dressing, talk, flags unchanged.
        var idleNet = part.ModelName == "c0000" ? NpcIdle.NetFor(part.InitAnimID) : null;
        // session 11 (ret11), rule I1d-P (asy10 enemies.md N-H): a c0000 NPC without a pose clip (InitAnimID -1) that is the same DS1
        // person as a posed NPC of the map (same NpcThinkParam: hollow Oscar 6024 / Oscar 6023, think 6020) reuses that NPC's own
        // network (c8002) with startUpStateType 0 = the network's standing idle (SM 132 leaves the start-up state on StartupType 0)
        bool sharedNet = false;
        if (idleNet == null && part.ModelName == "c0000" && part.InitAnimID <= 0 && part.ThinkParamID > 0)
        {
            var same = MSB1.Read(Path.Combine(ds1.Root, "map", "MapStudio", ds1Map + ".msb")).Parts.Enemies.FirstOrDefault(e => e != part && e.ModelName == "c0000" && e.ThinkParamID == part.ThinkParamID && NpcIdle.NetFor(e.InitAnimID) != null);
            if (same != null) { idleNet = NpcIdle.NetFor(same.InitAnimID); sharedNet = true; notes.Add($"rule I1d-P: same DS1 person as {same.Name} (entity {same.EntityID}, think {part.ThinkParamID}) → its network {idleNet!.Name}, startUpStateType 0 (standing)"); }
        }
        int highLogic = HighLogic, lowLogic = LowLogic;
        if (idleNet != null)
        {
            NpcIdle.EnsureNet(pack, ds1.Root, idleNet);
            Set(enemy, "animId", idleNet.AnimId);
            // WeaponCategory_Ene = generator desc+0xb1 if ≤ 5, else EnemyParam+0x48 = defaultEquipWeapon (decomp 608221-608225):
            // the c7240 hub 1023 needs branch 0 (→ SM 998, the StartupType slots)
            if (I(enemy, "defaultEquipWeapon") != 0) throw new InvalidDataException($"I1d {idleNet.Who}: EnemyParam {id} defaultEquipWeapon {I(enemy, "defaultEquipWeapon")} ≠ 0 (WeaponCategory_Ene branch)");
            startUp = (byte)(sharedNet ? 0 : idleNet.StartupType);   // session 11 (ret11), rule I1d-P: the same person's network, standing (0)
            (highLogic, lowLogic) = NpcAi.Ensure(pack, idleNet);
        }
        notes.Add(idleNet != null
            ? $"idle (I1d): DS1 InitAnimID {part.InitAnimID} → DS1's own clip a00_{part.InitAnimID} in {idleNet.Who}'s own network {idleNet.Name} (template {idleNet.Template}, generic NPC class): EnemyParam animId {idleNet.AnimId}, defaultEquipWeapon 0, startUpStateType {startUp} (its slot); logic {highLogic}/{lowLogic} (AI-N1)"
            : idleSlot != null
            ? $"idle: DS1 InitAnimID {part.InitAnimID} → DS1's own clip a00_{part.InitAnimID} (retargeted onto c0001's rig) in the DS1-NPC network {NpcIdle.AnibndName}: EnemyParam animId {NpcIdle.AnimId}, startUpStateType {startUp} (its slot)"
            : part.InitAnimID > 0
            ? $"idle: DS1 InitAnimID {part.InitAnimID} (c0000 sitting/kneeling clip) → DS2 startUpStateType {startUp} (Saulden's sitting); 1:1 needs DS1's clip via the Morpheme encoder"
            : $"idle: DS1 default idle of {part.ModelName} → DS2 startUpStateType {startUp}");
        // session 9 (track npcplace), rules P3/P2/P1 (Ds1Placement; evidence data\sandbox\npcplace\NOTES.md). P3: DS1's idle
        // clip (InitAnimID, else a chr NPC's own a00_0000) decides DS2's stand-in idle: frame-0 pelvis under 0.6 m over the
        // root with the head ≥ 0.3 m over the pelvis = a sit, kneel or crouch → Saulden's sit (13), else standing (0) (c0000
        // 7820/7845-7870 stand at 0.91-0.97 m); a slot another rule chose (track npcidle: 20) stays. `place-survey seats`.
        var pose = part.InitAnimID > 0 || part.ModelName != "c0000" ? Ds1Placement.ClipPose(ds1.Root, part.ModelName, part.InitAnimID > 0 ? part.InitAnimID : 0) : null;
        if (pose is { } pz && startUp is Sitting or Standing) startUp = Ds1Placement.Sits(pz) ? Sitting : Standing;
        notes.Add($"P3: DS1 idle frame 0 pelvis {(pose is { } p0 ? $"{p0.Pelvis:0.00}, head {p0.Head:0.00}" : "? (no clip)")} → startUpStateType {startUp}");
        // P2: a sitter's root puts the pelvis of the idle DS2 plays on DS1's pelvis (EnemyParam animId 100 = c0001's
        // network: the stand-in Saulden's a77_03_0010; any other = a network with DS1's own clip in the slot, track npcidle:
        // DS1's root), feet on the DS2 floor; a stander's root on the DS2 floor. P1: DS2 yaw = DS1 yaw + 180.
        // I1c (track npcpose pass 2): in the human route one network serves every human NPC, so an NPC on a DS1 slot's
        // StartUpCategory plays that DS1 clip (Anastacia on 13 → Oscar's a00_7805, a stand-in until she has a slot)
        var slotClip = idleSlot ?? NpcIdle.ForCategory(startUp);
        if (slotClip != null && idleSlot == null)
        {
            NpcIdle.Ensure(pack, ds1.Root);
            notes.Add($"I1c: startUpStateType {startUp} holds DS1 a00_{slotClip.Ds1Anim} ({slotClip.Who}) in the human network: this NPC plays it (stand-in)");
        }
        var pos = part.Position;
        if (probe != null)
        {
            bool sits = startUp != Standing && pose != null;
            // I1d: the clip that plays is DS1's own (the slot's loop leaf), so the root stays DS1's (moved 0 m)
            int? playedDs1 = idleNet?.Ds1Anim ?? slotClip?.Ds1Anim;
            var played = playedDs1 is int pa ? Ds1Placement.ClipPose(ds1.Root, "c0000", pa)?.Pelvis ?? Vector3.Zero
                : Convert.ToInt32(enemy["animId"].Value) == 100 ? Ds1Placement.SauldenSitPelvis : pose?.Pelvis ?? Vector3.Zero;
            string how;
            pos = sits ? Ds1Placement.Sit(probe, part.Position, part.Rotation.Y, pose!.Value.Pelvis, played, out how) : Ds1Placement.Stand(probe, part.Position, out how);
            notes.Add($"P1/P2: yaw DS1 {part.Rotation.Y} → DS2 {Ds1Placement.Yaw(part.Rotation.Y)}; height {(sits ? "sitting" : "standing")}: {how}");
        }
        // the NPC AI's hostility flag (DS1's, by FindHostile) next to the dead flag, as vanilla NPC generators carry them
        int hostileFlag = FindHostile(ds1.Root, ds1Map, part.EntityID) is int hf && FlagRules.Any(hf) is int hf2 ? hf2 : 0;
        // session 11 (ret11), rule N-H2: an NPC DS1 makes hostile at load (2004:2 team type 9 Hostile Ally, hollow Oscar in event 50)
        // without an HP-ratio hostility event: its hostility flag = the persistent flag DS1 sets in the event that enables it
        // (11810531: enable 6024 + flag 1061 = "hollow Oscar present"), ON whenever the NPC is there
        if (hostileFlag == 0 && HostileAtLoad(ds1.Root, ds1Map, part.EntityID) is int hl && FlagRules.Any(hl) is int hl2) { hostileFlag = hl2; notes.Add($"rule N-H2: DS1 sets team type 9 (Hostile Ally) at load; hostility flag = DS1 {hl} (set by the event that enables it) → {hl2}"); }
        notes.Add($"AI generator flags: hostile {(hostileFlag != 0 ? hostileFlag.ToString() : "none (no DS1 hostility event)")}, dead {deadFlag}");
        var custom = new EnemyTest.Custom(1600, EntityRules.Generator(part.EntityID), id, highLogic, lowLogic, pos, Ds1Placement.Yaw(part.Rotation.Y))
            { StartUpStateType = startUp, LogicGroup = logicGroup, Npc = true, DeadFlag = deadFlag, HostileFlag = hostileFlag };
        note = string.Join("; ", notes);
        return new Placed(part.EntityID, name, custom, death);
    }

    /// <summary>A DS1 weapon (CharaInitParam id with upgrade level) → DS2 WeaponParam id for NPCGenerateParam and
    /// NpcPlayerStatusParam (the item map: DS2's duplicate or our ported weapon; 7M/17M + DS1 id).</summary>
    static (int Gen, int Status) Weapon(Pack pack, Ds1Source ds1, int ds1Id, List<string> notes, string hand)
    {
        if (ds1Id <= 0) return (NoWeapon, NoWeaponStatus);
        int item = Ds1Drops.MapItem(pack, ds1, 0, ds1Id, out _, out int reinforce, out string how);
        if (item == 0) { notes.Add($"{hand} weapon {ds1Id} not ported → none"); return (NoWeapon, NoWeaponStatus); }
        int weapon = I(pack.Param("ItemParam")[item]!, "weaponParamId");
        notes.Add($"{hand} weapon {ds1Id} {ds1.Text("Weapon_name_", ds1Id / 1000 * 1000)} → WeaponParam {weapon} ({how}; DS1 +{reinforce} not shown on NPCs)");
        return (weapon, weapon);
    }

    /// <summary>
    /// A DS1 human-rig character (own chr model on the player skeleton) as DS2 parts: its whole mesh as one body part
    /// BD_{4500 + chr % 500}_{M|F} (c2750 → 4750, c2510 → 4510: armour sets use 4000–4264), re-bound with the chr's
    /// own skeleton (the Undead Merchant's is hunched: Neck y 1.515 vs DS1's player 1.464), plus empty head/arm/leg
    /// parts so DS2 draws no naked body or default face under it. ArmorParam rows for the four (NPC-only, no item);
    /// the head row borrows the Elite Knight Helm's interfere row (11060100: a full helm, hides face and hair).
    /// <para>Display masks (fix pass, review): a DS1 chr mesh whose material is named "#NN#…" belongs to display group NN,
    /// drawn only when the NPC's NpcParam.modelDispMaskNN is 1 (ChrPorter's rule for enemies: weapons per variant).
    /// c2510 is shared by the Burg's male merchant (NpcParam 251001, modelDispMask0 = 1: material[0] "#00# 日本刀", his
    /// katana) and Firelink's female merchant (251000, every mask 0): the hidden groups' meshes are left out.</para>
    /// <paramref name="female"/>: the census sex (DS1's chr params carry none for non-player models).
    /// </summary>
    static void ChrNpc(Pack pack, Ds1Source ds1, ArmorPorter armour, PlayerRig rig, string chr, PARAM.Row npcRow, bool female, int[] armourIds, List<string> notes)
    {
        int model = 4500 + int.Parse(chr[1..]) % 500;
        int npcId = npcRow.ID;
        string g = female ? "F" : "M";
        var bnd = BND3.Read(Path.Combine(ds1.Root, "chr", chr + ".chrbnd.dcx"));
        var flver = FLVER2.Read(bnd.Files.First(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
        var tex = ArmorPorter.Ds1Textures(ds1.Root, bnd);
        // bigger characters keep textures in chrtpfbdt (+ chrtpfbhd inside the chrbnd), as ChrPorter reads them
        var bhd = bnd.Files.FirstOrDefault(f => f.Name.EndsWith(".chrtpfbhd", StringComparison.OrdinalIgnoreCase));
        string bdt = Path.Combine(ds1.Root, "chr", chr + ".chrtpfbdt");
        if (bhd != null && File.Exists(bdt))
            foreach (var f in BXF3.Read(bhd.Bytes, File.ReadAllBytes(bdt)).Files)
                foreach (var x in TPF.Read(DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes).Textures) tex[x.Name] = x;
        // display groups: "#NN#" materials shown only when modelDispMaskNN = 1
        var hidden = new SortedSet<string>();
        bool Visible(FLVER2.Mesh mesh, FLVER2.Material mat)
        {
            var m = System.Text.RegularExpressions.Regex.Match(mat.Name ?? "", @"^#(\d+)#");
            if (!m.Success || npcRow.Cells.FirstOrDefault(c => c.Def.InternalName == $"modelDispMask{int.Parse(m.Groups[1].Value)}") is not { } cell) return true;
            if (Convert.ToInt32(cell.Value) != 0) return true;
            hidden.Add($"'{mat.Name!.Trim()}' (modelDispMask{int.Parse(m.Groups[1].Value)} = 0)");
            return false;
        }
        // a female NPC's pieces (ArmorParam modelGender 1) load the _f files; the _m copies cover a male lookup too
        foreach (string gg in female ? new[] { "F", "M" } : new[] { "M" })
        {
            armour.AddPart(flver, chr, $"BD_{model}_{gg}", ArmorPorter.Slot.Body, tex, Visible, rigOverride: rig.WithDs1Skeleton(flver));
            foreach (var slot in new[] { ArmorPorter.Slot.Head, ArmorPorter.Slot.Arm, ArmorPorter.Slot.Leg })
                armour.AddPart(new FLVER2(), chr, $"{new[] { "HD", "BD", "AM", "LG" }[(int)slot]}_{model}_{gg}", slot, tex);
        }
        // NPC-only ArmorParam rows: copies of DS2's naked pieces with our model
        var ap = pack.Param("ArmorParam");
        for (int s = 0; s < 4; s++)
        {
            // session 11 (burg11): one row set per sex (a chr shared by a female and a male NPC: c2510's Firelink merchant and
            // the Burg's male merchant; the second used to overwrite the first's rows): female 100+, male 120+
            int rowId = 10_000_000 + model * 1000 + (female ? 100 : 120) + s;
            var row = pack.AddRow("ArmorParam", ap[Naked(s, female)]!, rowId, $"[Lordran] {chr} part {s}");
            Set(row, "armorId", rowId);
            Set(row, "modelId", model);
            Set(row, "modelGender", female ? 1 : 0);
            if (s == 0) Set(row, "interfereId", 11060100);
            armourIds[s] = rowId;
        }
        notes.Add($"{chr} (DS1 NpcParam {npcId}) → body part BD_{model}_{g} (whole model re-bound with its own skeleton{(hidden.Count > 0 ? "; hidden display groups left out: " + string.Join(", ", hidden) : "")}) + empty HD/AM/LG; ArmorParam {10_000_000 + model * 1000 + (female ? 100 : 120)}..{10_000_000 + model * 1000 + (female ? 103 : 123)}");
    }
}
