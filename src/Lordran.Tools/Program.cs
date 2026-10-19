using Lordran.Tools.Commands;
using Lordran.Tools.Survey;

namespace Lordran.Tools;

public static class Program
{
    public const string DefaultDsrRoot = @"C:\Program Files (x86)\Steam\steamapps\common\DARK SOULS REMASTERED";
    public const string DefaultDs2Root = @"C:\Program Files (x86)\Steam\steamapps\common\Dark Souls II Scholar of the First Sin\Game";

    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8; // Japanese MSB/EDD names (UTF-8 console)
        // session 11 (burg11, round 8), rule H2: a command given a pack built with merged DS1 maps reads the same merge
        if (args.Length > 0 && args[0] != "build-pack") // (build-pack decides itself; its --out may hold an older pack)
        foreach (string a in args)
            if (a.Length > 0 && !a.StartsWith('-') && Directory.Exists(a) && Build.Maps.H2Merge.LoadFrom(DefaultDsrRoot, a)) break;
        if (args.Length == 0)
        {
            Console.WriteLine("usage: Lordran.Tools <command> [options]");
            Console.WriteLine("  survey-dsr [dsrRoot]   report format versions of DSR's game files");
            Console.WriteLine("  extract-ds2 [--list] [--lq] [--only /prefix ...] [--out DIR]   read-only extract of DS2's archives");
            Console.WriteLine("  nsa-dump SRC [CLIP] | nsa-roundtrip SRC [--ref DIR] | nsa-compare A B | nmb-rig-dump SRC | nmb-roundtrip PATH... | nmb-rig-replace T SKEL OUT | ds1-anim-to-nsa SKEL ANIM... OUT   Morpheme (DS2 animation) pipeline");
            Console.WriteLine("  chr-inspect FILE [--rig NMB] | clip-survey SRC | ds1rig-build OUT | ds1rig-check OUT [--pack DIR] | pose-view MODEL ANIBND CLIP PNG   1:1 DS1 characters (session 8)");
            return 1;
        }

        switch (args[0])
        {
            case "survey-dsr":
                DsrSurvey.Run(args.Length > 1 ? args[1] : DefaultDsrRoot);
                return 0;
            case "extract-ds2":
                return ExtractDs2.Run(args);
            case "dump-param":
                return DumpParam.Run(args);
            case "atmos-survey":
                return AtmosSurvey.Run(args);
            case "sky-survey":
                return SkySurvey.Run(args);
            case "mist-survey": // session 9 (mist track): DS2/DS1 effect banks, SFX regions, FFX conversion checks
                return MistSurvey.Run(args);
            case "shader-disasm":
                return ShaderDisasm.Run(args);
            case "sky-check":
                return SkyCheck.Run(args);
            case "atmos-check": // session 11 (atmos11): every DS1 atmosphere element of a map → DS2 counterpart or logged deviation
                return AtmosCheck.Run(args);
            case "item-map":
                return ItemMap.Run(args);
            case "inspect":
                return Inspect.Run(args);
            case "probe-smithbox":
                return ProbeSmithbox.Run();
            case "verify-regulation":
                return VerifyRegulation.Run();
            case "table":
                return Table.Run(args);
            case "probe-drb":
                return ProbeDrb.Run(args);
            case "tex":
                return Tex.Run(args);
            case "build-pack":
                return BuildPack.Run(args);
            case "probe-regulation-header":
                return ProbeRegulationHeader.Run(args);
            case "param-roundtrip":
                return ParamRoundTrip.Run(args);
            case "weapon-survey":
                return WeaponSurvey.Run(args);
            case "regulation-file":
                return RegulationFile.Run(args);
            case "probe-speffect":
                return ProbeSpEffect.Run(args);
            case "find-row":
                return FindRow.Run(args);
            case "regulation-dump":
                return RegulationDump.Run(args);
            case "make-regulation":
                return MakeRegulation.Run(args);
            case "unbind":
                return Unbind.Run(args);
            case "hkx-dump":
                return HkxDump.Run(args);
            case "obj-col-survey": // obj-col-survey [--breakable] [--model oAA_BBBB] [BND...]
                return ObjColSurvey.Run(args);
            case "param-diff": // session 11 (burg11): regulation rows that differ between two packs (golden gate)
                return ParamDiff.Run(args);
            case "balance-check": // session 11 (bal11): balance-check PACK [--chars]: rules BAL1-BAL3 / checks BAL-C0..C4 (Mappingsalance.tsv)
                return BalanceCheck.Run(args);
            case "map-hosts": // session 11 (cap11, rules H1/H2/CAP1): map-hosts [--cap N]: the DS1 -> DS2 host table, built and planned, and the WorldMapList budget
                return MapHostsCommand.Run(args);
            case "walk-check": // session 11 (walk11): walk-check PACK MAP [--seams] [--max N] [--steps FILE] — void / invisible walls / draw groups / assets vs DS1
                return WalkCheck.Run(args);
            case "walk-probe": // session 11 (walk11): walk-probe PACK|data\ds2 MAP... — ACB draw/LOD settings, model extents, MSB part groups
                return WalkProbe.Run(args);
            case "seam-check": // session 11 (burg11, BURG-PLAN M1 link-check): seam-check PACK MAP_A MAP_B [--from X,Y,Z --to X,Y,Z] [--via X,Y,Z] [--steps FILE]
                return SeamCheck.Run(args);
            case "lane-check": // lane-check DS1MAP PACK_DIR [--y Y] [--radius R] X1,Z1,X2,Z2 ... | --scan XMIN,ZMIN,XMAX,ZMAX --len L --dir +z
                return LaneCheck.Run(args);
            case "mtd-dump":
                return MtdDump.Run(args);
            case "acb-dump":
                return AcbDump.Run(args);
            case "convert-collision":
                return ConvertCollision.Run(args);
            case "msb2-dump":
                return Msb2Dump.Run(args);
            case "esd-dump":
                return EsdDump.Run(args);
            case "esd-check": // session 8: offline checks of a built pack's event script (esd-check PACKDIR [MAP])
                return EsdCheck.Run(args);
            case "save-flags": // session 11 (fix11): a save slot's global event flags (read-only)
                return SaveFlags.Run(args);
            case "warp-check": // session 11 (fix11): WT1 warp targets exist, WT2 no event box over a spawn point
                return WarpCheck.Run(args);
            case "esd-info":
                return EsdInfo.Run(args);
            case "event-sim": // session 9 (track nestfix): offline event ESD simulator + group-condition gate lint (rule G1)
                return EventSimCommand.Run(args);
            case "objact-rules": // session 10 (asy-events e2): DS1 ObjAct / ladder rules K1/K3/K4/K5/K12/G4 over DSR (--check: K3 unit check)
                return ObjActReport.Run(args);
            case "world-events": // session 10 (asy-events): rules WE1-WE5/WE8/C11 report for the Asylum (+ pack checks)
                return WorldEventsReport.Run(args);
            case "npc-events": // session 10 (asy-events e3): character / game-state event rules census (SP1, HB, N1-N4, G2, G5)
                return NpcEventsReport.Run(args);
            case "edd-dump":
                return EddDump.Run(args);
            case "survey-map":
                return MapSurvey.Run(args);
            case "bnd-ls":
                return BndLs.Run(args);
            case "roundtrip":
                return RoundTrip.Run(args);
            case "rewrite":
                return Rewrite.Run(args);
            case "regulation-ls":
                return RegulationLs.Run(args);
            case "map-param":
                return MapParamDump.Run(args);
            case "navpath":
                return NavPath.Run(args);
            case "dg-census": // session 11 (burg11, round 9): DS1 draw-group bits per map (rule DG1)
                return DgCensusCommand.Run(args);
            case "uv-survey": // session 11 (burg11): UV ranges vs DS2 short UVs
                return UvSurveyCommand.Run(args);
            case "tex-pairs": // session 11 (burg11): texture names two maps share
                return TexPairsCommand.Run(args);
            case "tex-census": // session 11 (burg11): map texture archive census
                return TexCensusCommand.Run(args);
            case "flver-version": // session 11 (burg11): FLVER header versions
                return FlverVersionCommand.Run(args);
            case "uv-check": // session 11 (burg11): rule UV1 check (converted UVs vs DS1)
                return UvCheckCommand.Run(args);
            case "zone-pieces": // session 11 (burg11): DS1 pieces inside collisions' box
                return ZonePiecesCommand.Run(args);
            case "vcolor": // session 11 (burg11): DS1 vertex colour statistics
                return VColorCommand.Run(args);
            case "overlap-check": // session 11 (burg11): coplanar seam geometry of two DS1 maps
                return OverlapCheckCommand.Run(args);
            case "seam-zfight": // session 11 (burg11): seam pieces of two maps drawn together
                return SeamZfightCommand.Run(args);
            case "floor-owner": // session 11 (burg11): floors of another map over a map's floors
                return FloorOwnerCommand.Run(args);
            case "route-draw": // session 11 (burg11): draw/object budget along a route
                return RouteDrawCommand.Run(args);
            case "h2-merge": // session 11 (burg11, round 8): rule H2 merged DS1 map report
                return H2MergeCommand.Run(args);
            case "msb1-dump":
                return Msb1Dump.Run(args);
            case "piece-near":
                return PieceNear.Run(args);
            case "floor-map": // session 8 (track nests): DS1 floor collision vs a model's surface around a point
                return FloorMap.Run(args);
            case "obj-nocol-survey": // session 8 (track nests): vanilla DS2 objects without collision
                return ObjNoColSurvey.Run(args);
            case "pack-model": // session 8 (track nests): a converted map-piece model in a built pack (meshes, textures, parts)
                return PackModel.Run(args);
            case "mesh-compare":
                return MeshCompare.Run(args);
            case "cull-survey":
                return CullSurvey.Run(args);
            case "winding-check":
                return WindingCheck.Run(args);
            case "plane-faces":
                return PlaneFaces.Run(args);
            case "travel-check": // session 11 (travel12): TC1 DS1 bonfire rows, TC2 vanilla rows, TC3 Travel pictures
                return TravelCheck.Run(args);
            case "travel-render": // session 11 (travel12), rule TP1: Travel menu pictures of the DS1 bonfires
                return TravelRender.Run(args);
            case "render-view":
                return RenderView.Run(args);
            case "bad-verts":
                return BadVerts.Run(args);
            case "ds1-tex":
                return Ds1TexDump.Run(args);
            case "hkx-convert": // hkx-convert DS1_TAGFILE DS2_TEMPLATE OUT
                File.WriteAllBytes(args[3], Formats.Havok.HkTagToPack.Convert(File.ReadAllBytes(args[1]), new Formats.Havok.HkPackfile(File.ReadAllBytes(args[2]))));
                return 0;
            case "param-grep":
                return ParamGrep.Run(args);
            case "node-survey":
                return NodeSurvey.Run(args);
            case "emevd-grep":
                return EmevdGrep.Run(args);
            case "sound-check": // session 10 (asy-sound): plan + translator + pack checks of the map sound rules SND1-SND5
                return SoundCheck.Run(args);
            case "bgm-check": // session 11 (snd11): every DS1 BGM / boss BGM / kill sting reachable from a region or event of a pack
                return BgmCheck.Run(args);
            case "sound-survey": // session 10 (asy-sound): DS1 MSB sound events / 2010:2 SE / DS2 sound regions, rules SND1-SND4
                return SoundSurvey.Run(args);
            case "esd-usage":
                return EsdUsage.Run(args);
            case "boss-ai-survey": // session 8, track boss: DS2 AI ESD command/function usage (evidence for Build/Ai/Boss); renamed in merge stage 5 (the enemies track's ai-survey has the name)
                return BossAiSurvey.Run(args);
            case "obj-states":
                return ObjStates.Run(args);
            case "tae-dump":
                return TaeDump.Run(args);
            case "ds1-tae-msgs": // session 11 (chrai11): DS1 TAE 231 event messages of characters (rule EM1 survey)
                return Ds1TaeMessages.Run(args);
            case "edd-machines":
                return EddMachines.Run(args);
            case "ds1-text":
                return Ds1Text.Run(args);
            case "ds1-talk": // session 8: DS1 talk ESD extractor (script\talk\*.talkesdbnd.dcx)
                return Ds1Talk.Run(args);
            case "fmg-get":
                return FmgGet.Run(args);
            case "talk-translate": // session 8 (track npc, step 2): DS1 talk ESD → DS2 talk group report (talk-translate TALKID|MAP|--all)
                return TalkTranslate.Run(args);
            case "talk-sim": // session 9 (track npctalk): frame simulator for a built pack's talk groups (DS2 ESD semantics)
                return TalkSimCommand.Run(args);
            case "flag-survey": // session 9 (track npcflags): DS1 / vanilla DS2 flag censuses (flag-survey ds1|vanilla [--list FILE])
                return FlagSurvey.Run(args);
            case "flag-check": // session 9 (track npcflags): every flag a pack references has DS2 storage (flag-check PACKDIR)
                return FlagSurvey.Check(args);
            case "npc-survey": // session 8 (track npc): how DS2 builds human NPCs (generators, talk groups, NPCGenerateParam)
                return NpcSurvey.Run(args);
            case "parts-check": // session 8 (track npc): converted human parts: DS2 skeleton bone names, weights, rig frames
                return PartsCheck.Run(args);
            case "armor-test": // session 8 (track npc): port DS1 armour pieces into a throwaway pack, write + check their parts
                return PartsCheck.ArmorTest(args);
            case "parts-view": // session 8 (track npc): several part binders merged and rendered (front/side PNGs)
                return PartsCheck.View(args);
            case "flver-bones":
                return FlverBones.Run(args);
            case "hkx-anim":
                return HkxAnim.Run(args);
            case "demo-obj-check": // session 9 (crow track): a demo object vs DS2's demo bird, skeleton/track pairing
                return DemoObjCheck.Run(args);
            case "scn-dump": // session 8 (cutscene track): DS2 demo scnbnd / MQB dump + round trip
                return ScnDump.Run(args);
            case "remo-dump": // session 8 (cutscene track): DS1 remo cutscene dump (parts, camera, TAE)
                return RemoDump.Run(args);
            case "ds2-has": // session 8 (cutscene track): does a DS2 archive hold PATH (by name hash)
                return Ds2Has.Run(args);
            case "remo-convert": // session 8 (cutscene track): DS1 remo → DS2 demo scnbnd + offline checks
                return RemoConvert.Run(args);
            case "emevd-stats":
                return EmevdDump.Stats(args);
            case "emevd-translate":
                return EmevdDump.Translate(args);
            case "msg-check": // session 10 (asy-items): DS1 developer messages in a built pack (rules M1-M4)
                return MsgCheck.Run(args);
            case "ring-check": // session 10 (asy-items i2): ported DS1 rings in a built pack (rule G9)
                return ItemsCheck.Rings(args);
            case "armor-check": // session 11 (equip11): ported DS1 armour in a built pack (rules AR1-AR7)
                return EquipCheck.Armor(args);
            case "outfit-survey": // session 11 (outfit11): part binders' GX lists / materials / meshes (culling-group evidence)
                return OutfitCheck.Survey(args);
            case "outfit-sheet": // session 11 (outfit11): every ported DS1 armour set rendered DS1 vs ours, both sexes, silhouette IoU
                return OutfitCheck.Sheet(args);
            case "outfit-npcs": // session 11 (outfit11): DS1 human NPCs of the built maps, DS1 outfit vs the pack's, renders + table
                return OutfitCheck.Npcs(args);
            case "spell-check": // session 11 (equip11): ported DS1 spells in a built pack (rules SP1-SP8)
                return EquipCheck.Spells(args);
            case "spell-ffx": // session 11 (spell11): can rule B2b convert the DS1 spells' own effects? (report)
                return EquipCheck.SpellFfx(args);
            case "goods-check": // session 11 (equip11): ported DS1 goods in a built pack (rules GD1-GD6)
                return EquipCheck.Goods(args);
            case "regulation-check": // session 11 (equip11): a pack's enc_regulation decrypts (ours = Smithbox), parses, ids sorted
                return EquipCheck.Regulation(args);
            case "k1b-check": // session 10 (asy-items i2 fix pass): rule K1b' forms through the event translator
                return ItemsCheck.K1bCheck(args);
            case "ring-census": // session 10 (asy-items i2 fix pass): DS1 events testing a ring's SpEffect (rule R5) + translator self-test
                return ItemsCheck.RingCensus(args);
            case "pickup-ids": // session 10 (asy-items i2): pickup ids not in Mappings/pickup-ids.tsv yet (rule PK1)
                return ItemsCheck.PickupIds(args);
            case "origin-check": // session 10 (asy-items): DS1's opening (rule C2) over every DS1 map, the pack's origin prompt
                return MsgCheck.Origin(args);
            case "emevd-dump":
                return EmevdDump.Run(args);
            case "col-survey-ds2":
                return ColSurveyDs2.Run(args);
            case "col-survey":
                return ColSurvey.Run(args);
            case "ramp-survey":
                return RampSurvey.Run(args);
            case "place-survey": // session 9 (track npcplace): facing/height evidence (skel FILE… | ds1 MAP ENTITY… | ds2 MAP GEN…)
                return PlaceSurvey.Run(args);
            case "spot-check": // session 9 (track npcplace): test warps + generators vs the pack's collision (spot-check MAP|all [--pack DIR])
                return SpotCheck.Run(args);
            case "tex-collide":
                return TexCollide.Run(args);
            case "tex-stats":
                return TexStats.Run(args);
            case "save-unpack":
                return SaveUnpack.Run(args);
            case "save-dev": // session 11 (devkit11): show / edit one character (name, max stats, souls, bonfires, flags)
                return SaveDev.Run(args);
            case "devkit-check": // session 11 (devkit11): a --dev-kit pack gives every ported item (devkit-check PACK | --census)
                return DevKitCheck.Run(args);
            case "loading-check": // session 12 (load12): loading screens: DS1 item rows (LD1/LD2), the logo line (LD3)
                return LoadingCheck.Run(args);
            case "save-bonfire":
                return SaveBonfire.Run(args);
            case "flver-view":
                return FlverView.Run(args);
            case "chr-compare":
                return ChrCompare.Run(args);
            case "ngp-dump":
                return NgpDump.Run(args);
            // Morpheme (DS2 animation) pipeline, session 8 (track chr): see Commands/MorphemeTools.cs
            case "nsa-dump":
                return MorphemeTools.NsaDump(args);
            case "nsa-roundtrip":
                return MorphemeTools.NsaRoundtrip(args);
            case "nsa-compare":
                return MorphemeTools.NsaCompare(args);
            case "nmb-rig-dump":
                return MorphemeTools.NmbRigDump(args);
            case "nmb-roundtrip":
                return MorphemeTools.NmbRoundtrip(args);
            case "nmb-rig-replace":
                return MorphemeTools.NmbRigReplaceCmd(args);
            case "ds1-anim-to-nsa":
                return MorphemeTools.Ds1AnimToNsa(args);
            // network re-serialiser (session 8, track boss): see Commands/NmbNetTools.cs; boss-nmb-net-roundtrip renamed in merge
            // stage 5 (the enemies track's nmb-net-roundtrip has the name)
            case "boss-nmb-net-roundtrip":
                return NmbNetTools.Roundtrip(args);
            case "nmb-resize":
                return NmbNetTools.Resize(args);
            case "nmb-resize-selftest":
                return NmbNetTools.ResizeSelfTest(args);
            // 1:1 boss pipeline (session 8, track boss): see Commands/BossTools.cs
            case "tae-ds2-roundtrip":
                return BossTools.TaeRoundtrip(args);
            case "ds1boss-build":
                return BossTools.Ds1BossBuild(args);
            case "ds1boss-check":
                return BossTools.Ds1BossCheck(args);
            case "boss-bind-compare":
                return BossTools.BindCompare(args);
            case "boss-drop-path": // session 8, track boss step 2: DS1's intro drop vs DS1 walkable collision
                return BossTools.DropPath(args);
            case "ds2-root-survey": // DS2 clips with large vertical root motion (drop / rise entrances)
                return BossTools.RootSurvey(args);
            case "tae-event-survey": // where DS2's chr TAEs use given event types (animation families, cover)
                return BossTools.TaeEventSurvey(args);
            case "nmb-clip-tracks": // event tracks of the anim leaves playing a clip
                return BossTools.ClipTracks(args);
            case "ai-lua-parse": // session 8, track boss: parse every decompiled DSR Lua script (Build/Ai)
                return BossTools.AiLuaParse(args);
            case "ds1-ai-paths": // decision paths of a DS1 AI function (symbolic run)
                return BossTools.Ds1AiPaths(args);
            case "ds1boss-ai": // the Asylum Demon's translated AI alone (AI ESDs + dumps + report)
                return BossTools.Ds1BossAi(args);
            case "nmb-track-attrib-selftest": // SourceEventTracks attrib + track-name table writers vs every DS2 network
                return BossTools.TrackAttribSelfTest(args);
            case "anibnd-reencode":
                return MorphemeTools.AnibndReencode(args);
            // network re-serialiser and 1:1 attack pipeline, session 8 (track enemies): see Commands/NetTools.cs
            case "nmb-net-roundtrip":
                return NetTools.NetRoundtrip(args);
            case "nmb-net-resize":
                return NetTools.NetResize(args);
            case "loop-feet":
                return NetTools.LoopFeet(args);
            case "ds1-skeleton":
                return NetTools.Ds1Skeleton(args);
            case "nmb-net-clone":
                return NetTools.NetClone(args);
            case "tae-write-roundtrip":
                return NetTools.TaeWriteRoundtrip(args);
            case "tae-groups": // session 11: event type → event-group type in a DS2 TAE
                return Ds1NetCheck.Groups(args);
            case "world-fold": // session 11 (track ret11): rule W1, DS1 world-state fold of a DS1 map
                return WorldCheck.Fold(args);
            case "world-check": // session 11 (track ret11): rule W1, generator start/stop conditions of a pack vs the fold
                return WorldCheck.Run(args);
            case "attack-travel-check": // session 11 (track leap12): DS1 attack travel = DS1, rule LP1 (JT 51 -> 280000) coverage
                return AttackTravelCheck.Run(args);
            case "ds1net-check": // session 11 (track enemy11): G1/G2 asserts on a pack's 1:1 networks and behaviour rows
                return Ds1NetCheck.Run(args);
            case "ik-chain-repair": // session 11 (crash11): rule IK1 / G1-off A/B variants of an existing anibnd
                return IkChainCheck.Repair(args);
            case "ik-chain-check": // session 11 (crash11): IK bone chains vs rig ancestry (uneven terrain / lock foot / hips IK / head look)
                return IkChainCheck.Run(args);
            case "audit-net": // enemy-audit / session 11 (track enemy11): full network dump (nodes, CPs, alphas, transitions, clips)
                return AuditNet.Run(args);
            case "nmb-runtime-check": // session 9 (track enemyfix): the bounds / invariants the DS2 Morpheme runtime relies on (Commands/NmbRuntimeCheck.cs)
                return NmbRuntimeCheck.Run(args);
            // DS1 Lua AI → DS2 AI ESD, session 8 (track enemies step 2): see Commands/AiTools.cs, Build/Ai
            case "ai-survey":
                return AiSurvey.Run(args);
            case "ai-goal":
                return AiTools.Goal(args);
            case "ai-translate":
                return AiTools.Translate(args);
            case "burg-chr-check": // session 11 (burgchr11): every Burg character of a --burg-chrs pack is complete
                return BurgChrCheck.Run(args);
            case "obj-check": // session 11 (fx11): every DS1 object behaviour of a map has a DS2 counterpart or a logged deviation
                return ObjCheck.Run(args);
            case "bullet-check": // session 12 (bomb12): player + enemy projectile chains vs vanilla (effects < 30000, children, categories, rule E10f)
                return BulletCheck.Run(args);
            case "hit-chain-check": // session 12 (ehit12): every enemy attack resolves to a DS2 hit chain (rule E-ID1 id ranges, rows, shapes, dummies, TAE slots)
                return HitChainCheck.Run(args);
            case "special-check": // session 11 (fx11): every DS1 special attack / bullet / buff of the ported characters is in the pack
                return SpecialCheck.Run(args);
            case "ai-check":
                return AiTools.Check(args);
            case "ai-roundtrip":
                return AiTools.Roundtrip(args);
            case "ai-verify":
                return AiVerify.Run(args);
            // 1:1 DS1 characters (session 8, track chr step 2): see Commands/ChrInspect.cs, Commands/Ds1ChrTools.cs
            case "chr-inspect":
                return ChrInspect.Run(args);
            case "param-row":
                return Ds1ChrTools.ParamRow(args);
            case "clip-vs-bind":
                return Ds1ChrTools.ClipVsBind(args);
            case "tae-roundtrip":
                return Ds1ChrTools.TaeRoundtrip(args);
            case "ds1rig-build":
                return Ds1ChrTools.Ds1RigBuild(args);
            case "ds1rig-check":
                return Ds1ChrTools.Ds1RigCheck(args);
            case "steps-calib":
                return Ds1ChrTools.StepsCalib(args);
            case "pose-scene": // session 9 (track npcpose): seated DS1 NPC in its map, DS1 vs ours (pose-scene oscar|cw OUT.png)
                return PoseScene.Run(args);
            case "pose-view":
                return Ds1ChrTools.PoseView(args);
            case "clip-survey":
                return Ds1ChrTools.ClipSurvey(args);
            case "gesture-check": // session 11 (track gest11): a pack's DS1 gestures: c0001/c000100 only additive vs vanilla, table, texts, icons
                return GestureTools.Check(args);
            case "gesture-build": // session 11 (track gest11): the DS1 gesture animation files alone + the vanilla-identity check
                return GestureTools.Build(args);
            case "npc-idle": // session 9 (track npcidle): DS1 idle clips on the c0001-based NPCs (NPC network c8001; npc-idle OUTDIR [--png])
                return NpcIdleCmd.Run(args);
            case "npc-ai-check": // session 9 (track npcpose2): rule AI-N1 checks of the NPC AI copies in a pack (npc-ai-check PACKDIR)
                return NpcIdleCmd.AiCheck(args);
            case "nmb-ids": // session 9 (track npcidle): Morpheme asset-id sharing check over vanilla + pack anibnds (nmb-ids PACKDIR)
                return NmbIdsCmd.Run(args);
            case "chr-sfx": // session 8, track chrsound: DS1/DS2 character effect evidence (rule S7)
                return ChrSfxTools.Run(args);
            case "ds1-chr-bank": // session 8, track chrsound: a DS1 character's DS2 sound bank alone (rules S1-S10)
                return Ds1ChrTools.Ds1ChrBank(args);
            case "survey-ds2":
                Ds2Survey.Run(args.Length > 1 ? args[1] : ExtractDs2.DefaultOut, DefaultDs2Root);
                return 0;
            default:
                Console.Error.WriteLine($"unknown command: {args[0]}");
                return 1;
        }
    }
}

