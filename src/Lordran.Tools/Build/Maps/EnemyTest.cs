using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// build-pack --enemy-test [GEN] (test only, never in the default pack): one of Things Betwixt's own enemies (default
/// generator 3000: GeneratorRegistParam 11500000 → EnemyParam 115006, c1150 Undead Traveler, hostile; generator 1000 =
/// c1010 Kobold, passive: it cowers) placed in a converted DS1 map, to prove that
/// generator params work in our maps and that DS2's AI walks our converted navmesh (NavmeshConverter).
/// Per-map generator params have no fitting community paramdef (GeneratorLocation has none; GeneratorParam byte 0x48
/// uses bit 7), so rows are copied raw. GeneratorParam (160 bytes): +0x08 GeneratorRegistParam, +0x2C..+0x40 logicMoveZoneId1..6, +0x4A u16 bonfireId,
/// +0x50/+0x54 logicNaviPoint1/2, +0x70/+0x74/+0x78 returnPointId1..3 (TB's ids, cleared here). GeneratorLocation
/// (32 bytes): position, rotation (degrees), Unk18, InheritInstanceParamId.
/// </summary>
public static class EnemyTest
{
    /// <summary>
    /// A generator for any EnemyParam row: TB generator <c>TemplateGenerator</c>'s rows (GeneratorParam/Location) cloned
    /// as <c>NewGenerator</c>, with a new GeneratorRegistParam row EnemyId×100 = (EnemyId, logic ids,
    /// startUpStateType 0, memoryBlock8 only: the only chr memory block that loads in our TB-based maps, see HANDOFF).
    /// </summary>
    public sealed record Custom(int TemplateGenerator, int NewGenerator, int EnemyId, int HighLogic, int LowLogic, Vector3 Position, float Yaw)
    {
        /// <summary>GeneratorParam.eventGroupId (byte 2, Smithbox's GENERATOR_PARAM def) when nonzero: a boss's group, which
        /// BossBattleParam.genEventGroupId1 names (vanilla 10/20/30; session 8). 0 keeps the template's.</summary>
        public int EventGroup { get; init; }

        /// <summary>GeneratorRegistParam.startUpStateType: the start/idle state the character's AI enters (session 8,
        /// track npc: DS2's NPC AI ESDs (ai741000 group 2147483645) branch on it, f5902() == n → 2147483644(n) → 5204(n);
        /// Crestfallen Saulden 13 sits, Maughlin/Carhillion 14 stand at their posts, the "(Dead)" corpse variants 11).</summary>
        public byte StartUpStateType { get; init; }

        /// <summary>GeneratorParam.logicGroupId (byte 3) when ≥ 0, else the template's (TB 1600: 5, shared by every
        /// enemy cloned from it). Vanilla NPCs each have their own (Majula: Saulden 20, Maughlin 25, Rosabeth 35).</summary>
        public int LogicGroup { get; init; } = -1;

        /// <summary>Session 8 (track npc, fix pass): a human NPC's generator, spawned the way vanilla NPCs are (`npc-survey`:
        /// Saulden's generator 100 = respawnDisableFlag 104140, start condition judge 0 / condition 8 (flag off) / param
        /// 104140; always-present characters 0/0/0) instead of TB 1600's approach condition (1 = distance, 50 m): the
        /// character exists from the map load (its talk EventParam row has its owner at once) and, with
        /// <see cref="DeadFlag"/>, never comes back once dead.</summary>
        public bool Npc { get; init; }

        /// <summary>Session 9 (track npcplace): with <see cref="Npc"/>, the NPC AI's generator fields as every vanilla
        /// Majula NPC generator has them (100, 105, 107, 108, 110, 115, 135; the AI ESDs we use are Saulden's):
        /// logicEventFlag3 (+0x64) = a flag of the NPC's 1037xx family (here DS1's hostility flag, 0 = none),
        /// logicEventFlag4 (+0x68) = a flag of its 1042xx dead/respawnDisable family (here the dead flag; vanilla equals
        /// respawnDisableFlag for 100/105/107/108, +2 for 110/115/135: meaning U), returnDistance (+0x4C) 200,
        /// aiReturnStandbyRange (+0x94) 5. TB 1600's were 0/0/50/2.</summary>
        public int HostileFlag { get; init; }

        /// <summary>With <see cref="Npc"/>: the dead flag (DS2 id), written as respawnDisableFlag (+0x04, set by the engine
        /// when the character dies) and as start condition 8 on it (+0x0D, +0x10); 0 = spawned at load without condition.</summary>
        public int DeadFlag { get; init; }
        /// <summary>GeneratorRegistParam row id (0 = EnemyId·100). Track enemies step 2: placements of one EnemyParam with different
        /// translated DS1 logic need rows of their own (EnemyId·100 + k).</summary>
        public int RegistId { get; init; }
        public int Regist => RegistId != 0 ? RegistId : EnemyId * 100;

        /// <summary>
        /// A DS2 boss generator (session 8, track boss step 2), as vanilla's (Smelter Demon m10_19 806, the Pursuer m10_10 853,
        /// checked offline): byte 0x48 = 0x6C (bossBattleStopDisable, hpGaugeDisable, generateOverDeleteDisable,
        /// invincibleWithoutBossBattle), +0x58 bootLowStateId 10, +0x5C logicEventFlag1 = the boss's logic flag (its upper
        /// logic starts / wakes it when the flag turns on). 0 = an ordinary generator.
        /// </summary>
        public uint BossLogicFlag { get; init; }

        /// <summary>Session 11 (fx11), rule E-REQ-P: the AI request channel flags k = 1..3 → GeneratorParam.logicEventFlag2-4
        /// (+0x60 / +0x64 / +0x68), read by the AI as f5903(k) (<see cref="Ai.EventRequests"/>); 0 = keep. An NPC's lef3/4 (its
        /// hostility / dead flags) may not be taken.</summary>
        public int[] ChannelFlags { get; init; } = [];

        /// <summary>Session 11 (track ret11), rule W1 (<see cref="Events.WorldState"/>): the generator's start condition
        /// (GeneratorParam +0x0C judge, +0x0D..0x0F conditions, +0x10/0x14/0x18 params; decompile FUN_140413280 / evaluator
        /// FUN_1404125f0: condition 7 = flag ON, 8 = flag OFF) replacing TB 1600's unconditional one (condition 1; its param 50
        /// is unused) and, for an NPC, the dead-flag condition (the dead flag stays respawnDisableFlag, checked first: D3).
        /// Null = the template's / the NPC rule's.</summary>
        public GenCondition? StartCondition { get; init; }
        /// <summary>Rule W1: the stop condition (+0x1C..0x2B, FUN_1404132e0; an active generator whose stop condition holds is
        /// despawned, FUN_14041ae10): DS1's live switch that disables the character (11810350 → first-visit set off when
        /// 11810000 turns on). Null = none.</summary>
        public GenCondition? StopCondition { get; init; }
        /// <summary>Rule W1: what the conditions say in DS1 terms (build log).</summary>
        public string WorldNote { get; init; } = "";
    }

    /// <summary>A GeneratorParam condition record (GENERATOR_JUDGE_ENUM judge, GENERATOR_EVAL_ENUM conditions, params).</summary>
    public sealed record GenCondition(byte Judge, byte C1, byte C2, byte C3, int P1, int P2, int P3)
    {
        public void Write(byte[] g, int at)
        {
            g[at] = Judge; g[at + 1] = C1; g[at + 2] = C2; g[at + 3] = C3;
            BitConverter.GetBytes(P1).CopyTo(g, at + 4); BitConverter.GetBytes(P2).CopyTo(g, at + 8); BitConverter.GetBytes(P3).CopyTo(g, at + 12);
        }
        public override string ToString() => $"judge {Judge}: {string.Join(" ", new[] { (C1, P1), (C2, P2), (C3, P3) }.Where(x => x.Item1 != 0).Select(x => $"{x.Item1}/{x.Item2}"))}";
    }

    public static void Build(Pack pack, MapBuilder.Options map, IReadOnlyList<(int Generator, Vector3 Position, float Yaw)> placements, IReadOnlyList<Custom>? customs = null)
    {
        string tb = "m10_02_00_00";
        PARAM Raw(string name)
        {
            var p = PARAM.Read(Path.Combine(pack.Ds2Data, "param", $"{name}_{tb}.param"));
            var blob = new PARAMDEF { ParamType = p.ParamType, DataVersion = p.ParamdefDataVersion, Unicode = true, FormatVersion = 104, BigEndian = false };
            blob.Fields.Add(new PARAMDEF.Field(blob, PARAMDEF.DefType.dummy8, "data") { ArrayLength = (int)p.DetectedSize });
            p.ApplyParamdef(blob);
            return p;
        }
        static byte[] Data(PARAM.Row r) => (byte[])r["data"].Value;

        var gen = Raw("generatorparam");
        var loc = Raw("generatorlocation");
        var reg = Raw("generatorregistparam");
        var keepGen = new HashSet<PARAM.Row>(); var keepLoc = new HashSet<PARAM.Row>(); var keepReg = new HashSet<int>();
        foreach (var (generatorId, position, yaw) in placements)
        {
            var genRow = gen.Rows.Single(r => r.ID == generatorId);
            keepGen.Add(genRow);
            var g = Data(genRow);
            int registId = BitConverter.ToInt32(g, 0x08);
            keepReg.Add(registId);
            BitConverter.GetBytes((ushort)map.BonfireId).CopyTo(g, 0x4A);
            foreach (int off in new[] { 0x2C, 0x30, 0x34, 0x38, 0x3C, 0x40, 0x50, 0x54, 0x70, 0x74, 0x78 }) BitConverter.GetBytes(0).CopyTo(g, off);

            var locRow = loc.Rows.Single(r => r.ID == generatorId);
            keepLoc.Add(locRow);
            var l = Data(locRow);
            BitConverter.GetBytes(position.X).CopyTo(l, 0); BitConverter.GetBytes(position.Y).CopyTo(l, 4); BitConverter.GetBytes(position.Z).CopyTo(l, 8);
            BitConverter.GetBytes(0f).CopyTo(l, 12); BitConverter.GetBytes(yaw).CopyTo(l, 16); BitConverter.GetBytes(0f).CopyTo(l, 20);
            BitConverter.GetBytes(0).CopyTo(l, 24); BitConverter.GetBytes(0).CopyTo(l, 28);
            pack.Note($"{map.Ds2Map} ENEMY TEST: TB generator {generatorId} (regist {registId}) at {position} yaw {yaw}");
            Console.WriteLine($"  enemy test: TB generator {generatorId} (regist {registId}) at {position}");
        }
        // custom generators: cloned rows (raw), regist row through the paramdef
        var regDefRows = new List<PARAM.Row>();
        if (customs is { Count: > 0 })
        {
            var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
            var regTyped = PARAM.Read(Path.Combine(pack.Ds2Data, "param", $"generatorregistparam_{tb}.param"));
            if (!regTyped.ApplyParamdefCarefully(defs)) throw new InvalidDataException("no paramdef fits GeneratorRegistParam");
            var regTemplate = regTyped.Rows[0];
            foreach (var c in customs)
            {
                var g = (byte[])Data(gen.Rows.Single(r => r.ID == c.TemplateGenerator)).Clone();
                BitConverter.GetBytes(c.Regist).CopyTo(g, 0x08);
                // maxRespawnNum / maxRespawnNum2ndOver (bytes 0, 1): TB's Ogre has 1/1 (never respawns once killed), which
                // left killed Asylum Hollows gone for good (session 6). Regular DS2 enemies use 0/0 (139 of FoFG's 149
                // generators; Things Betwixt 47 of 54): they respawn on resting under DS2's rules, as DS1's do on resting.
                g[0] = 0; g[1] = 0;
                if (c.EventGroup > 0) g[2] = (byte)c.EventGroup;
                if (c.LogicGroup >= 0) g[3] = (byte)c.LogicGroup;
                if (c.Npc)
                {
                    // GENERATOR_PARAM (Smithbox DS2S def): +0x04 respawnDisableFlag, +0x0C startConditionJudgeType,
                    // +0x0D..0x0F startConditionCondition1-3, +0x10..0x1B startConditionParam1-3
                    BitConverter.GetBytes(c.DeadFlag).CopyTo(g, 0x04);
                    g[0x0C] = 0; g[0x0D] = (byte)(c.DeadFlag > 0 ? 8 : 0); g[0x0E] = 0; g[0x0F] = 0;
                    BitConverter.GetBytes(c.DeadFlag).CopyTo(g, 0x10); BitConverter.GetBytes(0).CopyTo(g, 0x14); BitConverter.GetBytes(0).CopyTo(g, 0x18);
                    // session 9 (track npcplace): the NPC AI's generator fields as vanilla NPCs (see Custom.HostileFlag);
                    // LR_NPC_LOGIC=tb keeps TB 1600's (A/B lane)
                    if (Environment.GetEnvironmentVariable("LR_NPC_LOGIC") != "tb")
                    {
                        BitConverter.GetBytes(c.HostileFlag).CopyTo(g, 0x64); BitConverter.GetBytes(c.DeadFlag).CopyTo(g, 0x68);
                        BitConverter.GetBytes(200f).CopyTo(g, 0x4C); BitConverter.GetBytes(5f).CopyTo(g, 0x94);
                        pack.Note($"{map.Ds2Map} NPC generator {c.NewGenerator}: logicEventFlag3/4 {c.HostileFlag}/{c.DeadFlag}, returnDistance 200, aiReturnStandbyRange 5");
                    }
                }
                // session 11 (track ret11), rule W1: DS1's world-state fold as the generator's own start/stop conditions
                if (c.StartCondition is { } sc) sc.Write(g, 0x0C);
                if (c.StopCondition is { } st) st.Write(g, 0x1C);
                if (c.StartCondition != null || c.StopCondition != null)
                    pack.Note($"{map.Ds2Map} W1 generator {c.NewGenerator}: start {(c.StartCondition?.ToString() ?? "unchanged")}, stop {(c.StopCondition?.ToString() ?? "none")} ({c.WorldNote})");
                for (int k = 0; k < c.ChannelFlags.Length && k < 3; k++)
                {
                    if (c.ChannelFlags[k] == 0) continue;
                    if (c.Npc && k >= 1) throw new InvalidDataException($"generator {c.NewGenerator}: AI channel {k + 1} would take the NPC's logicEventFlag{k + 2} (rule E-REQ-P)");
                    BitConverter.GetBytes(c.ChannelFlags[k]).CopyTo(g, 0x60 + 4 * k);
                }
                if (c.ChannelFlags.Any(f => f != 0)) pack.Note($"{map.Ds2Map} E-REQ-P generator {c.NewGenerator}: logicEventFlag2-4 = {string.Join(" / ", c.ChannelFlags)} (DS1 AI request channels, f5903(1..3))");
                if (c.BossLogicFlag != 0)
                {
                    g[0x48] = 0x6C;
                    BitConverter.GetBytes(10).CopyTo(g, 0x58);
                    BitConverter.GetBytes(c.BossLogicFlag).CopyTo(g, 0x5C);
                }
                BitConverter.GetBytes((ushort)map.BonfireId).CopyTo(g, 0x4A);
                foreach (int off in new[] { 0x2C, 0x30, 0x34, 0x38, 0x3C, 0x40, 0x50, 0x54, 0x70, 0x74, 0x78 }) BitConverter.GetBytes(0).CopyTo(g, off);
                var gr = new PARAM.Row(c.NewGenerator, "", gen.AppliedParamdef); gr["data"].Value = g; gen.Rows.Add(gr); keepGen.Add(gr);

                var l = new byte[32];
                BitConverter.GetBytes(c.Position.X).CopyTo(l, 0); BitConverter.GetBytes(c.Position.Y).CopyTo(l, 4); BitConverter.GetBytes(c.Position.Z).CopyTo(l, 8);
                BitConverter.GetBytes(c.Yaw).CopyTo(l, 16);
                var lr = new PARAM.Row(c.NewGenerator, "", loc.AppliedParamdef); lr["data"].Value = l; loc.Rows.Add(lr); keepLoc.Add(lr);

                var rr = new PARAM.Row(regTemplate) { ID = c.Regist };
                rr["EnemyId"].Value = c.EnemyId; rr["HighLogicId"].Value = c.HighLogic; rr["LowLogicId"].Value = c.LowLogic;
                rr["startUpStateType"].Value = c.StartUpStateType;
                foreach (var cell in rr.Cells.Where(x => x.Def.InternalName.StartsWith("memoryBlock") || x.Def.InternalName == "bAnotherWorldGenerate"))
                    cell.Value = Convert.ChangeType(cell.Def.InternalName == "memoryBlock8" ? 1 : 0, cell.Value.GetType());
                // one row per regist id (placements of one EnemyParam and logic pair share it). Merge9 (npc + enemies): a shared id
                // must describe the same character, or one of them (e.g. an NPC's startUp/logic) would silently take the other's row
                var same = regDefRows.FirstOrDefault(x => x.ID == rr.ID);
                if (same == null) regDefRows.Add(rr);
                else if (!Equals(same["EnemyId"].Value, rr["EnemyId"].Value) || !Equals(same["HighLogicId"].Value, rr["HighLogicId"].Value)
                    || !Equals(same["LowLogicId"].Value, rr["LowLogicId"].Value) || !Equals(same["startUpStateType"].Value, rr["startUpStateType"].Value))
                    throw new InvalidDataException($"{map.Ds2Map}: generator {c.NewGenerator} needs GeneratorRegistParam {rr.ID} with EnemyParam {c.EnemyId} logic {c.HighLogic}/{c.LowLogic} startUp {c.StartUpStateType}, but another generator already uses that id for a different character");
                pack.Note($"{map.Ds2Map} enemy: generator {c.NewGenerator} (rows from TB {c.TemplateGenerator}) EnemyParam {c.EnemyId} regist {c.Regist} logic {c.HighLogic}/{c.LowLogic} startUp {c.StartUpStateType} logicGroup {g[3]} at {c.Position} yaw {c.Yaw}"
                    + (c.Npc ? $" (NPC: start condition {g[0x0D]} on {c.DeadFlag}, respawnDisableFlag {c.DeadFlag})" : ""));
                Console.WriteLine($"  enemy: generator {c.NewGenerator} EnemyParam {c.EnemyId} at {c.Position}");
            }
            // write the regist param through the typed copy so the custom rows serialize with the real layout
            regTyped.Rows.RemoveAll(r => !keepReg.Contains(r.ID) || regDefRows.Any(x => x.ID == r.ID));
            regTyped.Rows.AddRange(regDefRows);
            regTyped.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
            gen.Rows.RemoveAll(r => !keepGen.Contains(r));
            loc.Rows.RemoveAll(r => !keepLoc.Contains(r));
            gen.Rows.Sort((a, b) => a.ID.CompareTo(b.ID)); loc.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
            pack.AddFile("gamedata", $"param/generatorparam_{map.Ds2Map}.param", gen.Write());
            pack.AddFile("gamedata", $"param/generatorlocation_{map.Ds2Map}.param", loc.Write());
            pack.AddFile("gamedata", $"param/generatorregistparam_{map.Ds2Map}.param", regTyped.Write());
            return;
        }
        gen.Rows.RemoveAll(r => !keepGen.Contains(r));
        loc.Rows.RemoveAll(r => !keepLoc.Contains(r));
        reg.Rows.RemoveAll(r => !keepReg.Contains(r.ID));

        pack.AddFile("gamedata", $"param/generatorparam_{map.Ds2Map}.param", gen.Write());
        pack.AddFile("gamedata", $"param/generatorlocation_{map.Ds2Map}.param", loc.Write());
        pack.AddFile("gamedata", $"param/generatorregistparam_{map.Ds2Map}.param", reg.Write());
    }

    /// <summary>The navmesh height under (x, z) closest to <paramref name="nearY"/> (map-local), or null if off the mesh.</summary>
    public static float? NavHeight(NGP ngp, float x, float z, float nearY)
    {
        float? best = null;
        foreach (var m in ngp.Meshes)
            foreach (var f in m.Faces)
            {
                Vector3 a = m.Vertices[f.V1], b = m.Vertices[f.V2], c = m.Vertices[f.V3];
                float d = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
                if (MathF.Abs(d) < 1e-6f) continue;
                float l1 = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / d, l2 = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / d, l3 = 1 - l1 - l2;
                if (l1 < 0 || l2 < 0 || l3 < 0) continue;
                float y = l1 * a.Y + l2 * b.Y + l3 * c.Y;
                if (best == null || MathF.Abs(y - nearY) < MathF.Abs(best.Value - nearY)) best = y;
            }
        return best;
    }
}
