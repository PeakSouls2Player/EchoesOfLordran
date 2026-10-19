using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Lordran.Tools.Formats;
using Lordran.Tools.Formats.Morpheme;
using SoulsFormats;
using static Lordran.Tools.Formats.Morpheme.MorphemeMath;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// <b>Rule I1d</b> (session 9, track npcpose2; replaces I1c for DS1 human NPCs; P until in-game T1): a DS1 human (model-1,
/// c0000) NPC gets <b>its own network</b>, cloned from a DS2 template network with the human rig, runs on DS2's generic
/// NPC class (EnemyParam.animId = AnibndId·100 ≠ 100) and sits in a StartupType slot of that network.
/// <para>Evidence (decompile <c>decomp\ds2s\DarkSoulsII.c</c>, V code unless noted): the generator copies
/// GeneratorRegistParam.startUpStateType (+0x0C, Smithbox GENERATOR_REGIST_PARAM) into its runtime record +0x77
/// (FUN_14040f300 via FUN_140412530, line 811218; FUN_14040f1f0 811161 after a spawn); FUN_140418690 passes it as the spawn
/// request +0x28 (819183) → FUN_1403560a0 desc+0xa8 (local_d0, 662835) → the generic class init FUN_140312dd0
/// (<c>(longlong*, short* desc)</c>, decomp 607861) writes StartupType chr+0xb8+0x114 = desc+0xa8 when the mode byte
/// desc+0xb4 is 0 (608154-608161; 1 → 15, 2 → 16). desc+0xb4 = the generator record's +0x76 bits 0-1 (the runtime
/// alive/dead state: set by FUN_14040fdb0 811750 on a death reset, restored from the save by FUN_14040deb0 810240; no
/// param field writes it) → 0 for a living NPC (a dead one is never spawned: start condition 8 on its dead flag).
/// WeaponCategory_Ene = desc+0xb1 if ≤ 5, else EnemyParam+0x48 = defaultEquipWeapon (608221-608225; layout
/// all_EnemyParam.txt: modelId 0 … deadType02 0x44, defaultEquipWeapon 0x48, V data); desc+0xb1 = the generator record's
/// +0x34 (spawn request +0x3e, 819193). Template data (V, <c>research-0\c7240.sms.txt</c>): c7240 SM 132 default state 5
/// = SM 997 SM_StartUp; its hub 1023 picks the branch by WeaponCategory_Ene ([0,0] → SM 998); SM 998: StartupType 3 →
/// HeadLook 1006 → SM 1000 (1001 → leaf 1004 a75_01_7110 loop; 1002 → leaf 1005 a75_01_7111 exit on request 0 Idle),
/// 6 → 1008, 11 → 1014 (slots 3/6/11). SM 132 leaves 997 only on event 80 inside 997, on 1034/1038 active (StartupType
/// 0/1) or 15 → Die. Rig (V, rigcmp.py): c7240 = c0001, 59 bones, names/parents equal, bind difference 0.</para>
/// <para>Never ships c0001.anibnd or c000100 TAEs (guard I1d-G in BuildPack): the player and every vanilla human (animId
/// 100) and Drummond (c7240) are untouched. Model 1 on a non-c0001 protocol network: U (risk R1); s9-12/s9-17 ran model 1 on
/// the generic class with a c0001-rig network (c8001), so the skinning side is the same rig (P).</para>
/// </summary>
public static partial class NpcIdle
{
    /// <summary>
    /// One DS1 human NPC's own network (rule I1d). <paramref name="AnibndId"/>: the network/anim chr id (animId =
    /// AnibndId·100, TAE c{animId}); <paramref name="Template"/>: the DS2 network cloned; <paramref name="StartupType"/>:
    /// the template slot (GeneratorRegistParam.startUpStateType); <paramref name="LoopLeaf"/>: the slot's loop clip leaf
    /// (gets DS1's clip); <paramref name="ExitLeaf"/>: the slot's exit clip (stand-in, I8); <paramref name="HeadLookNode"/>:
    /// the slot's head look (I6c).
    /// </summary>
    public sealed record NpcNet(int Ds1Anim, string Who, int AnibndId, string Template, int StartupType, int LoopLeaf, int ExitLeaf, int HeadLookNode)
    {
        public int AnimId => AnibndId * 100;
        public string Name => $"c{AnibndId:D4}";
        public int TemplateAnimId => int.Parse(Template[1..]) * 100;
        /// <summary>The NPC's AI pair (rule AI-N1, NpcAi): lower = animId, upper = animId + 10 (DS2's own convention:
        /// Drummond animId 724000 → ai724000 / ai724010).</summary>
        public int AiLow => AnimId;
        public int AiHigh => AnimId + 10;
    }

    public static readonly NpcNet[] Nets =
    [
        new(7805, "Oscar", 8002, "c7240", 3, 1004, 1002, 1006),         // loop a75_01_7110, exit a75_01_7111
        new(7810, "Crestfallen", 8003, "c7240", 3, 1004, 1002, 1006),
    ];

    /// <summary>The NPC's own network (Template route, default).</summary>
    public static NpcNet? NetFor(int ds1InitAnim) => Enabled && Route == IdleRoute.Template ? Nets.FirstOrDefault(n => n.Ds1Anim == ds1InitAnim) : null;

    /// <summary>Anibnd ids other tracks already use (chrint's I1b list: the DS1 rigs + the demon; npcidle's c8001).</summary>
    static readonly int[] OtherTrackAnibnds = [2501, 2502, 2503, 2541, 2542, 2543, 2551, 2552, 2901, 2902, 2210, 8001];

    /// <summary>Template-network constants (c7240, V data: <c>work\c7240\cps.txt</c>, <c>nodes.py</c>): CP node 7 =
    /// PlaySpeed_0 (CPFloat, default 0; not among the 217 "ControlParameters|…" names the exe binds, <c>work\exe-cp-names.txt</c>:
    /// never written by game code) = the constant 0 for I6c / I9d.</summary>
    const int TemplateZeroCp = 7;
    /// <summary>I9d nodes (c7240): the IK chain above SM 132 (root 0 → SM 1246 → T500 1249 → T138 1250 (uneven terrain:
    /// Hips, BlendWeight, Prediction) → T121 1248 → T121 1247 (foot lock, weight = OpType142 1251 ← FootLock_IK) → 1256 →
    /// HipsIK 1257 (slope, weight BlendWeight_1) → … → HipsIK 1208 (move-hit, weight MoveHit_Weight) → 1207 → 1195 → SM 132)
    /// and each node's blend-weight input (pin index). T500 1249 is left as c0001's I9 left 8287 (custom type, U).</summary>
    static readonly (int Node, int Type, int Input)[] TemplateIkWeights = [(1250, 138, 1), (1248, 121, 0), (1247, 121, 0), (1257, 129, 4), (1208, 129, 4)];

    public sealed class TemplateResult
    {
        public NpcNet Net = null!;
        public Dictionary<string, byte[]> Files = [];
        public List<string> Report = [];
        public byte[] Nmb = [];
        public uint NetworkAssetId, TemplateAssetId;
        public byte[] NetworkGuid = [];
        public string NsaName = "";
        public int ClipIndex;
        public List<int> Nodes = [];
        public List<int> TimeAct = [];
        public double Ds1Duration, Ds2Duration;
        public NsaAnim Ds2 = null!, Ds2Original = null!;
        public Ds1AnimBridge.Result Ds1 = null!;
        public Check Check = null!;
        /// <summary>Network-def byte offsets the I6c / I9d edits wrote (verify: every other changed byte is I5's SourceAnim).</summary>
        public List<(int Offset, int Length, string What)> Edits = [];
        public List<string> Problems = [];
    }

    static readonly ConditionalWeakTable<Pack, Dictionary<int, TemplateResult>> builtNets = new();

    /// <summary>Writes the NPC's network (c{AnibndId}.anibnd.dcx) and TAEs (c{animId}_pl/sfx/snd.tae) into the pack once.</summary>
    public static TemplateResult EnsureNet(Pack pack, string dsrRoot, NpcNet n)
    {
        var done = builtNets.GetOrCreateValue(pack);
        if (done.TryGetValue(n.AnibndId, out var r)) return r;
        r = BuildTemplate(dsrRoot, pack.Ds2Data, n);
        if (r.Problems.Count > 0) throw new InvalidDataException($"I1d {n.Who}: {string.Join("; ", r.Problems)}");
        foreach (var (path, bytes) in r.Files) pack.AddFile("gamedata", path, bytes);
        foreach (var line in r.Report) pack.Note($"npc idle: {line}");
        done[n.AnibndId] = r;
        Console.WriteLine($"  npc idle network {n.Name} ({n.Who}, template {n.Template}): StartupType {n.StartupType} = DS1 a00_{n.Ds1Anim}");
        return r;
    }

    /// <summary>The CP inputs of a node: (source node, source pin) at node+0x28 (count byte node+0x0E; as I9).</summary>
    static List<(int Offset, ushort Node, ushort Pin)> CpInputs(NmbNetwork net, NmbNetwork.Node n)
    {
        var list = new List<(int, ushort, ushort)>();
        int ncp = net.X[n.Offset + 0x0E];
        int p = (int)net.U64(n.Offset + 0x28);
        for (int k = 0; k < ncp; k++) { int o = n.Offset + p + 4 * k; list.Add((o, net.U16(o), net.U16(o + 2))); }
        return list;
    }

    public static TemplateResult BuildTemplate(string dsrRoot, string ds2Data, NpcNet n)
    {
        var res = new TemplateResult { Net = n };
        void Bad(string m) => res.Problems.Add(m);

        // ---- id checks (I1d): the anibnd id is free in vanilla, in the other tracks' networks and for the TAEs
        string chrDir = Path.Combine(ds2Data, "morpheme4", "chr");
        if (File.Exists(Path.Combine(chrDir, $"{n.Name}.anibnd.dcx"))) Bad($"{n.Name}.anibnd.dcx exists in vanilla");
        if (OtherTrackAnibnds.Contains(n.AnibndId)) Bad($"anibnd {n.AnibndId} is used by another track (chrint I1b list / c8001)");
        if (Nets.Count(x => x.AnibndId == n.AnibndId) != 1) Bad($"anibnd {n.AnibndId} listed twice");
        foreach (var kind in new[] { "pl", "sfx", "snd" })
            if (File.Exists(Path.Combine(ds2Data, "timeact", "chr", $"c{n.AnimId:D6}_{kind}.tae"))) Bad($"c{n.AnimId:D6}_{kind}.tae exists in vanilla");

        // ---- template anibnd
        byte[] raw = File.ReadAllBytes(Path.Combine(chrDir, $"{n.Template}.anibnd.dcx"));
        DCX.CompressionInfo? comp = null;
        if (DCX.Is(raw)) raw = DCX.Decompress(raw, out comp);
        var bnd = BND4.Read(raw);
        var nmbFile = bnd.Files.First(f => f.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase));
        var nmb = NmbFile.Read(nmbFile.Bytes);
        var rig2 = NmbRig.Read(nmb.Bundles[nmb.IndexOf(1)].Data);
        var maps = nmb.Bundles.Where(b => b.Type == 2).ToDictionary(b => b.AssetId, b => NmbChannelMap.Read(b.Data));
        int netIndex = nmb.IndexOf(10);
        var net = new NmbNetwork((byte[])nmb.Bundles[netIndex].Data.Clone());
        var fileNames = NmbFileNames.Read(nmb.Bundles[nmb.IndexOf(13)].Data).Anims;
        var trackBundle = new Dictionary<uint, int>();
        for (int i = 0; i < nmb.Bundles.Count; i++) if (nmb.Bundles[i].Type is 3 or 4 or 5) trackBundle[nmb.Bundles[i].AssetId] = i;
        NmbEventTrack Track(uint id) => NmbEventTrack.Read(nmb.Bundles[trackBundle[id]].Data, nmb.Bundles[trackBundle[id]].Type);

        // the template's rig must be the human rig (c0001's: names + parents)
        {
            var c1 = BND4.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(chrDir, "c0001.anibnd.dcx"))));
            var n1 = NmbFile.Read(c1.Files.First(f => f.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)).Bytes);
            var r1 = NmbRig.Read(n1.Bundles[n1.IndexOf(1)].Data);
            if (!r1.Names.SequenceEqual(rig2.Names) || !r1.Parents.SequenceEqual(rig2.Parents)) Bad($"{n.Template}'s rig ({rig2.Names.Count} bones) differs from c0001's ({r1.Names.Count}): not the human rig");
        }

        // ---- the slot's loop leaf and its clip
        var leaf = net.GetNode(n.LoopLeaf);
        if (leaf.Type != 104) Bad($"node {n.LoopLeaf} type {leaf.Type}, not an anim leaf");
        var sas = leaf.Attribs.Where(a => a.Type == 21).ToList();
        int ci = net.ClipIndex(sas[0]);
        if (sas.Any(a => net.ClipIndex(a) != ci)) Bad($"leaf {n.LoopLeaf}: anim sets play different clips");
        string nsaName = fileNames[ci].EndsWith(".nsa", StringComparison.OrdinalIgnoreCase) ? fileNames[ci] : fileNames[ci] + ".nsa";
        var file = bnd.Files.FirstOrDefault(f => Path.GetFileName(f.Name.Replace('\\', '/')).Equals(nsaName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"{n.Template}.anibnd has no {nsaName}");
        var animNodes = net.Nodes().Where(x => x.Type == 104).ToList();
        var users = animNodes.Where(x => x.Attribs.Any(a => a.Type == 21 && net.ClipIndex(a) == ci)).ToList();
        var mapIds = users.SelectMany(x => x.Attribs.Where(a => a.Type == 21).Select(a => net.U32(a.Offset + 0x78))).Distinct().ToList();
        if (mapIds.Count != 1) Bad($"{nsaName}: {mapIds.Count} channel maps");
        var map2 = maps[mapIds[0]];
        var ds2Orig = NsaCodec.Decode(NsaFile.Parse(file.Bytes));
        if (ds2Orig.ChannelCount != map2.AnimToRig.Count) Bad($"{nsaName}: NSA {ds2Orig.ChannelCount} channels, map {map2.AnimToRig.Count}");

        // the slot must stay in SM 997: no StateControl 80-85 on the loop leaf (SM 132 leaves 997 on event 80 inside it, the
        // attack/damage states return to 997 on 80-85); record its TimeAct ids (I7)
        foreach (var u in users)
            foreach (var at in u.Attribs.Where(a => a.Type == 23))
                foreach (var (_, tid, _) in net.Tracks(at))
                {
                    if (!trackBundle.ContainsKey(tid)) { Bad($"leaf {u.Index}: track 0x{tid:X8} not in the NMB"); continue; }
                    var tr = Track(tid);
                    if (tr.Name == "TimeAct") foreach (var ev in tr.Events) if (!res.TimeAct.Contains(ev.Value)) res.TimeAct.Add(ev.Value);
                    if (tr.Events.Any(ev => ev.Value is >= 80 and <= 85)) Bad($"leaf {u.Index}: track {tr.Name} holds event {string.Join(",", tr.Events.Select(e => e.Value))} (80-85: SM 132 would leave the startup slot)");
                }
        string leafTracks = string.Join("; ", leaf.Attribs.Where(a => a.Type == 23).SelectMany(a => net.Tracks(a)).Select(t => { var tr = Track(t.TrackId); return $"{tr.Name.Trim()} {string.Join(",", tr.Events.Select(e => e.Value))}"; }));

        // ---- I4: DS1 c0000 a00_{Ds1Anim} on the template's own rig and channel map
        var ds1 = LoadC0000(dsrRoot, [n.Ds1Anim]);
        var src = Ds1AnimBridge.Bridge(ds1.Samples(n.Ds1Anim), 30);
        var anim = Retarget(src, rig2, map2, out var check);
        byte[] nsa = NsaEncoder.EncodeFile(anim).Build();
        var back = NsaCodec.Decode(NsaFile.Parse(nsa));
        Compare(src, back, rig2, map2, check);
        double enc = 0;
        for (int c = 0; c < anim.ChannelCount; c++) for (int f = 0; f < anim.Frames; f++) enc = Math.Max(enc, AngleDeg(anim.Rot[c][f], back.Rot[c][f]));
        check.EncodeMaxDeg = enc;
        foreach (var j in new[] { "Pelvis", "L_Foot", "R_Foot" })
            if (check.JointCm.TryGetValue(j, out double cm) && cm > 1.0) Bad($"I4 check: {j} {cm:0.##} cm from DS1 × scale (limit 1 cm)");
        file.Bytes = nsa;
        double d1 = (back.Frames - 1) / back.Fps;
        res.NsaName = nsaName; res.ClipIndex = ci; res.Nodes = users.Select(x => x.Index).ToList();
        res.Ds1Duration = d1; res.Ds2Duration = ds2Orig.Duration ?? (ds2Orig.Frames - 1) / ds2Orig.Fps;
        res.Ds2 = back; res.Ds2Original = ds2Orig; res.Ds1 = src; res.Check = check;

        // ---- I5: the leaf's SourceAnim = DS1's duration and trajectory (event tracks are in clip fractions 0..1: unchanged)
        foreach (var u in users)
            foreach (var sa in u.Attribs.Where(a => a.Type == 21))
            {
                net.PatchSourceAnim(sa, back.RootPos[0], back.RootRot[0], back.RootPos[^1], back.RootRot[^1], d1);
                net.PutF32(sa.Offset + 0x80, 0f); net.PutF32(sa.Offset + 0x84, 1f);
            }

        // ---- I6c / I9d (DS1 layers; --npc-ds2-layers keeps DS2's)
        string i6c, i9d;
        if (Ds1Layers)
        {
            var z = net.GetNode(TemplateZeroCp);
            var za = NmbNetwork.Find(z, 3);
            if (z.Type != 20 || za == null || net.F32(za.Offset + 0x10) != 0f) Bad($"constant-0 CP {TemplateZeroCp}: type {z.Type}, not a CPFloat with default 0");
            i6c = HeadLookOff(net, n, res);
            i9d = FootIkOffTemplate(net, n, ds1, d1, res);
        }
        else { i6c = "I6c: not applied (--npc-ds2-layers)"; i9d = "I9d: not applied (--npc-ds2-layers)"; }

        // ---- I1b: own network-def id + GUID per c{AnibndId}; no track bundle edited (I6c / I9d rewire CP inputs only)
        var netBundle = nmb.Bundles[netIndex];
        res.TemplateAssetId = netBundle.AssetId;
        (netBundle.AssetId, netBundle.Guid) = NetworkIdentity(nmb, n.Name);
        res.NetworkAssetId = netBundle.AssetId; res.NetworkGuid = netBundle.Guid;
        nmbFile.Bytes = nmb.Rebuild(new Dictionary<int, byte[]> { [netIndex] = net.X });
        res.Nmb = nmbFile.Bytes;
        string oldNmbName = Path.GetFileName(nmbFile.Name.Replace('\\', '/'));
        if (!oldNmbName.Equals($"{n.Template}.nmb", StringComparison.OrdinalIgnoreCase)) Bad($"template NMB entry named {oldNmbName}");
        nmbFile.Name = nmbFile.Name.Replace(n.Template, n.Name);
        byte[] outBnd = bnd.Write();
        res.Files[$"morpheme4/chr/{n.Name}.anibnd.dcx"] = comp != null ? DCX.Compress(outBnd, comp) : outBnd;

        // ---- TimeAct (I7): the template's three files as c{animId}'s; the loop entry gets DS1's length, every other entry verbatim
        var taeNotes = new List<string>();
        foreach (var kind in new[] { "pl", "sfx", "snd" })
        {
            var tae = new Ds2Tae(File.ReadAllBytes(Path.Combine(ds2Data, "timeact", "chr", $"c{n.TemplateAnimId:D6}_{kind}.tae")));
            foreach (int id in res.TimeAct)
            {
                int? len0 = tae.Length(id);
                if (len0 == null) { Bad($"c{n.TemplateAnimId:D6}_{kind}.tae has no standard entry {id}"); continue; }
                tae.SetLength(id, (int)Math.Round(d1 * 30));
                taeNotes.Add($"{kind} {id}: length {len0} → {tae.Length(id)}, {tae.EventCount(id)} events verbatim ({string.Join(" ", tae.Events(id).Select(e => $"{e.Type} {e.Start:0.##}-{e.End:0.##}"))})");
            }
            res.Files[$"timeact/chr/c{n.AnimId:D6}_{kind}.tae"] = tae.Bytes;
        }

        // ---- report
        var ck = check;
        res.Report.Add($"I1d: {n.Who} → {n.Name} (template {n.Template}), StartupType {n.StartupType} = DS1 a00_{n.Ds1Anim}; EnemyParam animId {n.AnimId}, TAE c{n.AnimId:D6}_pl/sfx/snd; no c0001 shipped (c0001.anibnd / c000100 TAEs stay vanilla: the player and every animId-100 human are untouched)");
        res.Report.Add($"  {n.Template}: rig {rig2.Names.Count} bones (= c0001's names/parents), {fileNames.Count} clip names, {animNodes.Count} anim nodes; loop leaf {n.LoopLeaf} plays {nsaName} (index {ci}, nodes {string.Join(",", res.Nodes)}), tracks [{leafTracks}]: no StateControl 80-85");
        res.Report.Add($"  clip {nsaName} ← DS1 a00_{n.Ds1Anim}: {src.Frames} frames {d1:0.###} s (template {res.Ds2Duration:0.###} s), {nsa.Length} bytes; I5 SourceAnim duration/trajectory on nodes {string.Join(",", res.Nodes)}");
        res.Report.Add($"  retarget (I4, {n.Template}'s rig + channel map): {ck.Mapped} of {ck.Ds2Bones} DS2 bones driven, leg scale {ck.Scale:0.###}, facing turn {ck.FaceYawDeg:0.#}°; check (cm, max over frames): {string.Join(", ", ck.JointCm.Select(kv => $"{kv.Key} {kv.Value:0.##}"))}; encode max {ck.EncodeMaxDeg:0.###}°; loop seam DS1 {ck.SeamDs1Deg:0.##}° ours {ck.SeamOursDeg:0.##}°");
        res.Report.Add($"  pose frame 0 (Morpheme space): pelvis {Fmt(ck.Frame0.Pelvis)} (DS1 {Fmt(ck.Ds1PelvisFrame0)}), head {Fmt(ck.Frame0.Head)}, feet {Fmt(ck.Frame0.LFoot)} {Fmt(ck.Frame0.RFoot)}; DS1 TAE {Ds1Tae(ds1, n.Ds1Anim)}");
        res.Report.Add($"  {i6c}");
        res.Report.Add($"  {i9d}");
        res.Report.Add($"  I1b: network def 0x{res.TemplateAssetId:X8} ({n.Template}) → 0x{res.NetworkAssetId:X8}, GUID {Convert.ToHexString(res.NetworkGuid).ToLowerInvariant()}: own NetworkDef + MorphemeAnimContainer_{res.NetworkAssetId:x8}; NMB {oldNmbName} → {n.Name}.nmb; rig, channel maps, event tracks keep {n.Template}'s ids (unedited, identical content)");
        res.Report.Add($"  I7 TAE c{n.AnimId:D6}_* (from c{n.TemplateAnimId:D6}_*): {string.Join("; ", taeNotes)}; DS1 JT 7 / JT 28 of a00_{n.Ds1Anim} noted (JT 28 = I9d)");
        res.Report.Add($"  I8 listed DS2 stand-ins in {n.Name}: the slot's exit {Clip(net, fileNames, n.ExitLeaf)} (request 0 Idle), {n.Template}'s damage / die / attack clips and its main idle {Clip(net, fileNames, 1034)} (StartupType 0)");
        return res;
    }

    /// <summary>The clip a leaf (or the first anim leaf under a pass-through) plays.</summary>
    static string Clip(NmbNetwork net, List<string> fileNames, int node)
    {
        var x = net.GetNode(node);
        while (x.Type != 104 && x.Children.Length == 1) x = net.GetNode(x.Children[0]);
        var sa = x.Attribs.FirstOrDefault(a => a.Type == 21);
        return sa == null ? $"node {node}" : fileNames[net.ClipIndex(sa)].Split('_').Take(3).Aggregate((a, b) => a + "_" + b);
    }

    /// <summary>
    /// I6c (P): DS1's idles carry no look-at (7805: 226, 16, JT 7, JT 28, 308, JT 24), so the slot's head look is switched
    /// off in this NPC's network only: the loop leaf has no LookAt* event track (c7240 1004: Footsteps, TimeAct, IK 9550,
    /// UpperBody 9820, TalkDuration 118, IK_1 9510), so the HeadLook's blend-weight input (pin 1; c7240 1006: OpType142
    /// 1007 ← PlaySpeed_05, the same shape as the global HeadLooks 1211-1221 ← LookAtWeight) is pointed at the constant-0 CP.
    /// The global head-look family (SM 1209) is already left by the leaf's IK 9550 event (1210 → 1229 on 9500-9550, V data).
    /// </summary>
    static string HeadLookOff(NmbNetwork net, NpcNet n, TemplateResult res)
    {
        var h = net.GetNode(n.HeadLookNode);
        var ins = CpInputs(net, h);
        if (ins.Count != 2) { res.Problems.Add($"I6c: node {n.HeadLookNode} has {ins.Count} CP inputs (HeadLook: target, weight)"); return "I6c: failed"; }
        var (o, src, pin) = ins[1];
        if (src == TemplateZeroCp) return $"I6c: node {n.HeadLookNode} weight already the constant 0";
        var s = net.GetNode(src);
        BinaryPrimitives.WriteUInt16LittleEndian(net.X.AsSpan(o), TemplateZeroCp);
        BinaryPrimitives.WriteUInt16LittleEndian(net.X.AsSpan(o + 2), 0);
        res.Edits.Add((o, 4, $"I6c HeadLook {n.HeadLookNode} weight"));
        return $"I6c: HeadLook {n.HeadLookNode} (type {h.Type}) blend-weight input 1 node {src} (type {s.Type}) pin {pin} → CP {TemplateZeroCp} PlaySpeed_0 (constant 0) @0x{o:X}: no DS2 head turn on the DS1 slot (this network only)";
    }

    /// <summary>
    /// I9d (P): DS1 JT 28 (Disable Foot IK) over the whole DS1 idle → DS2's foot / hip IK off in this NPC's network: the
    /// blend-weight inputs of <see cref="TemplateIkWeights"/> → the constant-0 CP. Applied only when every DS1 slot of the
    /// network (one per NPC network) has JT 28 over its whole clip. Effect beyond the slump: no DS2 foot/hip IK when he later
    /// stands or is hit (his network only). The slot's own IK_1 9510 event already moves SM 1246 to its IK-free state 1253
    /// while the slump plays (vanilla Drummond, V data); I9d makes it hold for every state.
    /// </summary>
    static string FootIkOffTemplate(NmbNetwork net, NpcNet n, Ds1ChrSource ds1, double d1, TemplateResult res)
    {
        var a = ds1.TaeAnim(n.Ds1Anim);
        bool off = a != null && a.Events.Any(e => e.Type == 0 && BitConverter.ToInt32(e.GetParameterBytes(false), 0) == 28
            && e.StartTime <= 0.001f && e.EndTime >= d1 - 0.001);
        if (!off) return $"I9d: not applied: DS1 a00_{n.Ds1Anim} has no JT 28 over its whole clip (DS2's IK stays)";
        var done = new List<string>();
        foreach (var (node, type, input) in TemplateIkWeights)
        {
            var x = net.GetNode(node);
            if (x.Type != type) { res.Problems.Add($"I9d: node {node} type {x.Type}, expected {type}"); continue; }
            var ins = CpInputs(net, x);
            if (input >= ins.Count) { res.Problems.Add($"I9d: node {node} has {ins.Count} CP inputs"); continue; }
            var (o, src, pin) = ins[input];
            BinaryPrimitives.WriteUInt16LittleEndian(net.X.AsSpan(o), TemplateZeroCp);
            BinaryPrimitives.WriteUInt16LittleEndian(net.X.AsSpan(o + 2), 0);
            res.Edits.Add((o, 4, $"I9d node {node} weight"));
            done.Add($"{node} (T{type}) input {input} {src}:{pin}");
        }
        return $"I9d: DS1 JT 28 over a00_{n.Ds1Anim} → blend-weight inputs → CP {TemplateZeroCp} (constant 0): {string.Join(", ", done)}; T500 1249 unchanged";
    }
}
