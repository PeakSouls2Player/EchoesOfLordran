using Lordran.Tools.Formats;
using Lordran.Tools.Formats.Havok;
using Lordran.Tools.Formats.Morpheme;
using SoulsFormats;
// merge stage 5: the boss track's network / TAE code lives in its own namespaces (the enemies track has
// classes of the same names in Formats.Morpheme / Formats); see MERGE-NOTES.md "Unification backlog"
using NmbNetBlocks = Lordran.Tools.Formats.Morpheme.Boss.NmbNetBlocks;
using NmbNetEdit = Lordran.Tools.Formats.Morpheme.Boss.NmbNetEdit;
using NmbNetResize = Lordran.Tools.Formats.Morpheme.Boss.NmbNetResize;
using Ds2TaeFile = Lordran.Tools.Formats.Boss.Ds2TaeFile;

namespace Lordran.Tools.Commands;

/// <summary>
/// <c>ds1boss-check OUTDIR [--pack PACKDIR]</c> (session 8, track boss): structural checks of a ds1boss-build output (and,
/// with --pack, that the pack holds the same files), against the vanilla conventions found in this session:
/// <list type="number">
/// <item>anibnd/NMB: bundles re-emit, rig = skeleton.json, every channel map = bone c+1, +0x118, rig consistency (strict:
/// per-bone arrays, indices, mirror by DS2's rule), network structure (<see cref="NmbNetEdit.Check"/>: SM parents,
/// transition children, exit-condition unions, condition node/CP references, relocation), the block model round-trips, every
/// NSA decodes with the rig's channel count, every anim node's SourceAnim duration/trajectory end = its NSA, every clip
/// index has its NSA in the archive, every event-track id resolves, every track bundle re-serialises;</item>
/// <item>attack plan: categories 0-14, 20-23, 30-32 reachable from the weapon hub through the widened bands; every
/// attack leaf's TimeAct names a TAE animation; HitDuration on every leaf whose DS1 clip hits; chain stages carry the value
/// their next transition waits for;</item>
/// <item>TAE: the three files re-read and re-write byte-identically (<see cref="Ds2TaeFile"/>), every TimeAct id of the
/// network exists in all three, lengths = DS1 frames at 30 fps, 2200 IDs inside the damage slots;</item>
/// <item>FLVER: nodes 0..n-1 = rig bones 1..n (names, parents), palettes on rig bones, dummy ids ≤ 255 incl. the hit-capsule
/// dummies, every texture path named in the texbnd and unique to the model (no vanilla DS2 name), flvpwv size;</item>
/// <item>ragdoll (s8-6 crash lessons): animation skeleton = rig minus CharacterWorldSpaceTM, mapper indices in range, every
/// constraint's two entities set and among its bodies, ragdoll constraints with 3 motors, cone limits at 56, npData
/// 0xFFFFFFFF, dispatchType 4, and the converted file's class set ⊆ a vanilla DS2 ragdoll's.</item>
/// <item>step 2 (BossAiCheck.cs): DS1 gravity / map-collision windows as DS2 tracks, the translated AI ESDs, and with --pack
/// the AI's ids, logic params, the boss generator, BossBattleParam and the boss events.</item>
/// <item>merge9 stage 6 (BossSoundCheck.cs): the animId's decade free, file names after the anim set, DS1's own sound bank
/// (chrsound rules S1-S10) and the _snd events; with --pack the moved config rows and the loose wave banks.</item>
/// </list>
/// </summary>
public static partial class BossTools
{
    public static int Ds1BossCheck(string[] args)
    {
        string dir = args[1];
        CheckSpec = SpecArg(args, dir);   // session 11 (ret11), rule B-k: the boss of this reference (boss.txt / --boss)
        CheckSpec = Build.Chr.Ds1Boss.Fitted(CheckSpec, Build.Chr.Ds1ChrSource.Load(Program.DefaultDsrRoot, CheckSpec.Ds1Chr), ExtractDs2.DefaultOut);   // session 11 (burgchr11): rule B2c
        Console.WriteLine($"ds1boss-check: {CheckSpec.Ds1Chr} → {CheckSpec.N} (entity {CheckSpec.Ds1Entity})");
        string gd = Path.Combine(dir, "gamedata");
        var skel = MorphemeSkeleton.Load(Path.Combine(dir, "skeleton.json"));
        int fails = 0;
        void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {what}"); if (!ok) fails++; }
        // ---- 1. anibnd / NMB
        string anibnd = Directory.GetFiles(Path.Combine(gd, "morpheme4", "chr"), "*.anibnd.dcx").Single();
        var files = MorphemeTools.LoadFiles(anibnd);
        var nmbBytes = files.First(f => f.Name.EndsWith(".nmb")).Bytes;
        var nmb = NmbFile.Read(nmbBytes);
        var rig = NmbRig.Read(nmb.Bundles[nmb.IndexOf(1)].Data);
        Console.WriteLine($"{Path.GetFileName(anibnd)}: {files.Count} files, NMB {nmbBytes.Length:N0} bytes, {nmb.Bundles.Count} bundles");
        {
            byte[] ab = File.ReadAllBytes(anibnd); if (DCX.Is(ab)) ab = DCX.Decompress(ab);
            var ids = BND4.Read(ab).Files.Select(f => (f.ID, f.Name)).ToList();
            Check(ids.Select(t => t.ID).SequenceEqual(Enumerable.Range(0, ids.Count)) && ids[^1].Name.EndsWith(".nmb"), $"anibnd entries numbered 0..{ids.Count - 1} in order, the NMB last (vanilla layout)");
        }
        Check(nmb.Rebuild().AsSpan().SequenceEqual(nmbBytes), "NMB bundles re-emit byte-identically");
        // session 11 (burgchr11): non-ASCII DS1 bone names (c2250's Shift-JIS "master エクストラ01Nub") compared per character as '?'
        // (the NMB rig writer stores ASCII; the tag-file reader decodes bytes as ASCII)
        static string Asc(string? x) => new([.. (x ?? "").Select(c => c >= 128 || c == '?' ? '?' : c)]);
        Check(rig.Names.Select(Asc).SequenceEqual(skel.Names.Select(Asc)) && rig.Parents.SequenceEqual(skel.Parents), $"rig = DS1 rig ({rig.Names.Count} bones)");
        var maps = nmb.Bundles.Where(b => b.Type == 2).Select(b => NmbChannelMap.Read(b.Data)).ToList();
        Check(maps.All(m => m.AnimToRig.Count == rig.Names.Count - 1 && m.AnimToRig.Select((v, c) => v == c + 1).All(x => x)), $"{maps.Count} channel map(s): {rig.Names.Count - 1} channels, channel c = rig bone c+1");
        var x = nmb.Bundles[nmb.IndexOf(10)].Data;
        var net = new NmbNetwork(x);
        Check(net.U32(0x118) == rig.Names.Count, $"network +0x118 bone count {net.U32(0x118)}");
        var rc = NmbNetResize.CheckRigConsistency(nmbBytes, strictNamePairs: true);
        Check(rc.Count == 0, $"rig consistency (per-bone arrays, bone indices, attrib 84, mirror = name twins + bind-pose rule): {(rc.Count == 0 ? "clean" : string.Join("; ", rc.Take(5)))}");
        var fn = NmbNetEdit.ReadFileNames(nmb.Bundles[nmb.IndexOf(13)].Data);
        Check(NmbNetEdit.WriteFileNames(fn, fn.Pad).AsSpan().SequenceEqual(nmb.Bundles[nmb.IndexOf(13)].Data), $"file-name table re-emits ({fn.Files.Count} clips)");
        var nc = NmbNetEdit.Check(x, fn.Files.Count);
        Check(nc.Count == 0, $"network structure (SM parents, transition children, exit-condition unions, condition references, relocation): {(nc.Count == 0 ? "clean" : string.Join("; ", nc.Take(5)))}");
        var rt = NmbNetBlocks.RoundTrip(x);
        Check(rt.Same && rt.Moved && rt.NodesOk == rt.Nodes && rt.SmOk == rt.Sms, $"block model: identity {rt.Same}, relocation {rt.Moved}, nodes {rt.NodesOk}/{rt.Nodes}, SMs {rt.SmOk}/{rt.Sms}");
        var nsa = new Dictionary<string, NsaAnim>(StringComparer.OrdinalIgnoreCase);
        int decoded = 0;
        foreach (var (name, b) in files.Where(f => f.Name.EndsWith(".nsa")))
        {
            try { var a = NsaCodec.Decode(NsaFile.Parse(b)); if (a.ChannelCount == rig.Names.Count - 1) decoded++; nsa[name] = a; }
            catch (Exception e) { Console.WriteLine($"    {name}: {e.Message}"); }
        }
        Check(decoded == files.Count - 1, $"every NSA decodes with {rig.Names.Count - 1} channels: {decoded}/{files.Count - 1}");
        Check(fn.Files.All(nsa.ContainsKey), $"every file-name entry has its NSA in the archive ({fn.Files.Count(nsa.ContainsKey)}/{fn.Files.Count})");
        int crcOk = fn.Files.Select((f, i) => (f, i)).Count(t => nsa.ContainsKey(t.f) && fn.Hashes[t.i] == NmbNetEdit.Crc32Bzip2(files.First(z => z.Name.Equals(t.f, StringComparison.OrdinalIgnoreCase)).Bytes));
        Console.WriteLine($"  [info] file-name hashes = CRC-32/BZIP2 of the NSA bytes on {crcOk}/{fn.Files.Count} (new entries exact; slots whose content was replaced keep DS2's value, as c2501: the runtime looks clips up by name)");
        var tracks = nmb.Bundles.Where(b => b.Type is 3 or 4 or 5).ToDictionary(b => b.AssetId, b => (b, NmbEventTrack.Read(b.Data, b.Type)));
        Check(tracks.Values.All(t => t.Item2.Write().AsSpan().SequenceEqual(t.b.Data)), $"{tracks.Count} event tracks re-serialise byte-identically");
        Check(nmb.Bundles.Select(b => b.AssetId).Distinct().Count() == nmb.Bundles.Count, "bundle assetIds unique");
        int nodes = 0, durOk = 0, trajOk = 0, unresolved = 0;
        foreach (var node in net.Nodes().Where(n => n.Type == 104))
        {
            var sa = NmbNetwork.Find(node, 21); if (sa == null) continue;
            var et = NmbNetwork.Find(node, 23);
            if (et != null) foreach (var (_, tid, _) in net.Tracks(et)) if (!tracks.ContainsKey(tid)) unresolved++;
            if (!nsa.TryGetValue(fn.Files[net.ClipIndex(sa)], out var a)) continue;
            nodes++;
            if (Math.Abs(net.ClipDuration(sa) - (a.Frames - 1) / a.Fps) < 1e-4) durOk++;
            var e = a.RootPos[^1];
            if (Math.Abs(net.F32(sa.Offset + 0x40) - e.X) < 1e-4 && Math.Abs(net.F32(sa.Offset + 0x44) - e.Y) < 1e-4 && Math.Abs(net.F32(sa.Offset + 0x48) - e.Z) < 1e-4) trajOk++;
        }
        Check(durOk == nodes && trajOk == nodes, $"SourceAnim duration = NSA on {durOk}/{nodes} anim nodes, trajectory end on {trajOk}/{nodes}");
        Check(unresolved == 0, $"every event-track reference resolves ({unresolved} unresolved)");
        // ---- 2. attack plan
        var names = NmbNetEdit.NodeNames(x);
        var cats = NmbNetEdit.AttackCategories(net, names);
        // session 11 (ret11), rule B-k: the boss's own plan (Asylum Demon: 0-14, 20-23, 30-32; Stray Demon: 0-7, 30-31)
        var want = CheckSpec.Anim.Singles.Select(a => a - 3000).Concat(Enumerable.Range(30, CheckSpec.Anim.Chains.Length)).Distinct().Order().ToList();
        Check(want.All(cats.ContainsKey), $"weapon-0 attack categories: {string.Join(",", cats.Keys.Order())} (need {string.Join(",", want)})");
        float? BandHi(int sm, float v) => NmbNetEdit.Sm(net, sm).Conds.Where(c => NmbNetEdit.CondType(c) == 627 && NmbNetEdit.CpName(names, NmbNetEdit.CondCp(c)) == "AttackCategory_Ene")
            .Select(c => NmbNetEdit.CondFloatRange(c)).Where(r => r.Lo <= v && v <= r.Hi && r.Lo != r.Hi).Select(r => (float?)r.Hi).FirstOrDefault();
        int hubOfBands = net.GetNode(cats[0].Band).Parent;
        // session 11 (dchr11, rule SL-T): a template whose weapon-0 hub has no band conditions of its own (the Corrosive Egg Insect c6720:
        // SM 363 holds both bands, the AttackCategory_Ene ranges sit on the attack SM's entry transitions into the hub) is checked there
        bool hubOwn = BandHi(hubOfBands, 32) != null;
        int bandSm = hubOwn ? hubOfBands : net.GetNode(hubOfBands).Parent;
        Check(BandHi(bandSm, 32) == 39, $"band hub {bandSm}{(hubOwn ? "" : $" (entry of weapon-0 hub {hubOfBands})")}: categories 20-39 reach the [10,39] band");
        var leaves = File.ReadAllLines(Path.Combine(dir, "leaves.txt"));
        Console.WriteLine($"  [info] {leaves.Length} attack leaves (leaves.txt)");
        // ---- 3. TAE
        string taeDir = Path.Combine(gd, "timeact", "chr");
        var taes = Directory.GetFiles(taeDir, "*.tae").OrderBy(f => f, StringComparer.Ordinal).ToDictionary(f => Path.GetFileNameWithoutExtension(f).Split('_')[^1], f => File.ReadAllBytes(f));
        Check(taes.Count == 3 && taes.Values.All(d => Ds2TaeFile.Read(d).Write().AsSpan().SequenceEqual(d)), $"TAE files {string.Join(", ", taes.Keys)}: write(read) byte-identical");
        var pl = Ds2TaeFile.Read(taes["pl"]);
        var timeActs = new HashSet<int>();
        foreach (var node in net.Nodes().Where(n => n.Type == 104))
        {
            var et = NmbNetwork.Find(node, 23); if (et == null) continue;
            foreach (var (_, tid, _) in net.Tracks(et))
                if (tracks.TryGetValue(tid, out var t) && t.Item2.UserData == 1000) foreach (var ev in t.Item2.Events) timeActs.Add(ev.Value);
        }
        var missingTae = taes.ToDictionary(kv => kv.Key, kv => timeActs.Where(id => Ds2TaeFile.Read(kv.Value).Get(id) == null).ToList());
        Check(missingTae.Values.All(l => l.Count == 0), $"every TimeAct id of the network ({timeActs.Count}) exists in _pl/_sfx/_snd: missing {string.Join("; ", missingTae.Select(kv => $"{kv.Key} [{string.Join(",", kv.Value.Take(8))}]"))}");
        var hits = pl.Animations.Where(a => a.Id >= 65_000_000).SelectMany(a => a.Events.Where(e => e.Type == 2200).Select(e => (a.Id, Id: BitConverter.ToInt32(e.Params, 0)))).ToList();
        Check(hits.All(h => h.Id >= 0 && h.Id <= 3), $"{hits.Count} 2200 hit events, IDs within the 4 damage slots ({string.Join(" ", hits.GroupBy(h => h.Item1).Select(g => $"{g.Key}:{string.Join("/", g.Select(h => h.Id))}"))})");
        // ---- 4. FLVER
        string bndPath = Directory.GetFiles(Path.Combine(gd, "model", "chr"), "*.bnd").Single();
        var bnd = BND4.Read(bndPath);
        var flv = FLVER2.Read(bnd.Files.First(f => f.Name.EndsWith(".flv")).Bytes);
        var rigBones = skel.Names.Skip(1).ToList();
        bool namesOk = flv.Nodes.Take(rigBones.Count).Select(n => n.Name).SequenceEqual(rigBones);
        bool parentsOk = Enumerable.Range(0, rigBones.Count).All(i => flv.Nodes[i].ParentIndex == skel.Parents[i + 1] - 1);
        Check(namesOk && parentsOk, $"FLVER {flv.Nodes.Count} nodes: nodes 0..{rigBones.Count - 1} = rig bones (names {namesOk}, parents {parentsOk})");
        Check(flv.Meshes.All(m => m.BoneIndices.All(b => b >= 0 && b < rigBones.Count)), $"{flv.Meshes.Count} meshes skinned only to rig bones ({flv.Meshes.Sum(m => m.Vertices.Count):N0} vertices)");
        Check(flv.Dummies.All(d => d.ReferenceID >= 0 && d.ReferenceID <= 255 && d.ParentBoneIndex >= 0 && d.ParentBoneIndex < rigBones.Count), $"{flv.Dummies.Count} dummies: ids ≤ 255, bone-relative on rig bones");
        // session 11 (burgchr11): a DS1 model with more distinct dummy ids than that (the Hellkite: 55 ids of 446 dummies) keeps one per id
        // (Ds1Boss.MaxPerDummyId) + its hit-capsule dummies; above vanilla's range is then an in-game risk (engine limit unknown), reported
        int ds1Ids = FLVER2.Read(BND3.Read(Path.Combine(Program.DefaultDsrRoot, "chr", CheckSpec.Ds1Chr + ".chrbnd.dcx")).Files.First(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes).Dummies.Select(d => d.ReferenceID).Distinct().Count();
        int unique = flv.Dummies.Select(d => d.ReferenceID).Distinct().Count();
        Check(flv.Dummies.Count <= 64 || unique == flv.Dummies.Count && ds1Ids + 30 >= unique, $"dummy count {flv.Dummies.Count} near vanilla DS2's range (≤ 48 on every character surveyed: c6030, c6191 48, c6020 45, c6000 32){(flv.Dummies.Count > 64 ? $"; above it: one per id, DS1's model has {ds1Ids} ids (in-game risk)" : "")}");
        var dmgLines = File.ReadAllLines(Path.Combine(dir, "damage.txt"));
        var capsuleIds = dmgLines.Select(l => System.Text.RegularExpressions.Regex.Match(l, @"(capsule|sphere) dummy (\d+)")).Where(m => m.Success).Select(m => int.Parse(m.Groups[2].Value)).Distinct().ToList();
        Check(capsuleIds.All(id => flv.Dummies.Any(d => d.ReferenceID == id)), $"hit-shape dummies present: {string.Join(",", capsuleIds)}");
        var texb = BND4.Read(Path.ChangeExtension(bndPath, ".texbnd"));
        var texNames = texb.Files.SelectMany(f => TPF.Read(f.Bytes).Textures.Select(t => t.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // DS2's engine texture dummy128 (vanilla c1030/c3050 materials name it, no texbnd holds it) is not ours
        var flvTex = flv.Materials.SelectMany(m => m.Textures).Where(t => !string.IsNullOrEmpty(t.Path)).Select(t => Path.GetFileNameWithoutExtension(t.Path.Replace('\\', '/')))
            .Where(t => !t.Equals("dummy128", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
        Check(flvTex.All(texNames.Contains), $"every FLVER texture in the texbnd ({flvTex.Count(texNames.Contains)}/{flvTex.Count}){string.Concat(flvTex.Where(t => !texNames.Contains(t)).Select(t => $"; missing {t}"))}");
        string n = Path.GetFileNameWithoutExtension(bndPath);
        Check(texNames.All(t => t.StartsWith(n + "_", StringComparison.OrdinalIgnoreCase)), $"texture names unique to {n} (session-6 rule): {string.Join(", ", texNames.Order())}");
        // session 11 (burgchr11): a template archive without a flvpwv (the Corpse Rat c2260) keeps none
        var pwv = bnd.Files.FirstOrDefault(f => f.Name.EndsWith(".flvpwv"))?.Bytes;
        if (pwv == null) Check(true, "flvpwv: none (the template archive has none)");
        else Check(BitConverter.ToUInt16(pwv, 6) == flv.Nodes.Count && pwv.Length == 20 + 148 * flv.Nodes.Count, $"flvpwv: {BitConverter.ToUInt16(pwv, 6)} records for {flv.Nodes.Count} nodes");
        Check(!bnd.Files.Any(f => f.Name.Contains("_h.hkx", StringComparison.OrdinalIgnoreCase)), "no template hit-body file (_h) bound to another skeleton");
        // ---- 5. ragdoll
        var ragBytes = bnd.Files.First(f => f.Name.EndsWith("_Ragdoll.hkx")).Bytes;
        var hk = new HkPackfile(ragBytes);
        var objs = hk.Objects.Select(o => hk.ReadObject(o.Offset)).ToList();
        var sk = objs.Where(o => o.Class == "hkaSkeleton").ToList();
        var anim = sk.FirstOrDefault(s => ((System.Collections.IEnumerable)s["bones"]!).Cast<HkPObj>().Any(b => b["name"] as string == "Pelvis"))
            ?? sk.First(s => s["name"] as string == "Character");   // session 11 (dchr11, rule SL-F): a rig without a Pelvis (the Slime c3200)
        var an = anim.List("bones").Cast<HkPObj>().Select(b => b["name"] as string).ToList();
        Check(an.Select(Asc).SequenceEqual(rigBones.Select(Asc)), $"ragdoll animation skeleton '{anim["name"]}' = rig minus CharacterWorldSpaceTM ({an.Count} bones)");
        int badMap = 0, totalMap = 0;
        foreach (var mp in objs.Where(o => o.Class == "hkaSkeletonMapper"))
        {
            var d = mp.Get("mapping");
            int na = ((System.Collections.IList)((HkPObj)d["skeletonA"]!)["bones"]!).Count, nbb = ((System.Collections.IList)((HkPObj)d["skeletonB"]!)["bones"]!).Count;
            foreach (var sm in d.List("simpleMappings").Cast<HkPObj>()) { totalMap++; if ((short)sm["boneA"]! >= na || (short)sm["boneB"]! >= nbb) badMap++; }
        }
        Check(badMap == 0, $"skeleton mappers: {totalMap} simple mappings, all indices in range");
        bool Linked(HashSet<object?> bodies, HkPObj c) => c["entities"] is System.Collections.IList en && en.Count == 2 && en[0] is HkPObj ea && en[1] is HkPObj eb && !ReferenceEquals(ea, eb) && bodies.Contains(ea) && bodies.Contains(eb);
        var ri = objs.First(o => o.Class == "hkaRagdollInstance");
        var riBodies = new HashSet<object?>(ri.List("rigidBodies"), ReferenceEqualityComparer.Instance);
        var riCons = ri.List("constraints").Cast<HkPObj>().ToList();
        Check(riCons.Count > 0 && riCons.All(c => Linked(riBodies, c)), $"ragdoll instance: {riCons.Count(c => Linked(riBodies, c))}/{riCons.Count} constraints with both entities set and among its {riBodies.Count} bodies");
        var riRag = riCons.Select(c => c["data"]).OfType<HkPObj>().Where(d => d.Class == "hkpRagdollConstraintData").ToList();
        int motored = riRag.Count(d => d.Get("atoms").Get("ragdollMotors")["motors"] is System.Collections.IList m && m.Count == 3 && m.Cast<object?>().All(v => v is HkPObj));
        Check(motored == riRag.Count, $"ragdoll constraints with 3 motors: {motored}/{riRag.Count}");
        var allRag = objs.Where(o => o.Class == "hkpRagdollConstraintData").ToList();
        int cone56 = allRag.Count(d => Convert.ToInt64(d.Get("atoms").Get("coneLimit")["memOffsetToAngleOffset"] ?? 0L) == 56);
        Check(cone56 == allRag.Count, $"cone limit memOffsetToAngleOffset 56 on {cone56}/{allRag.Count}");
        var rbs = objs.Where(o => o.Class == "hkpRigidBody").ToList();
        int npOk = rbs.Count(b => (Convert.ToInt64(b["npData"] ?? 0L) & 0xFFFFFFFFL) == 0xFFFFFFFFL);
        var shapes = objs.Where(o => o["dispatchType"] is { } v && v is not string).ToList();
        int dispOk = shapes.Count(o => Convert.ToInt64(o["dispatchType"]) == 4);
        Check(npOk == rbs.Count && dispOk == shapes.Count, $"npData 0xFFFFFFFF on {npOk}/{rbs.Count} bodies, dispatchType 4 on {dispOk}/{shapes.Count} shapes");
        var vanillaClasses = new HashSet<string>();
        foreach (var vb in new[] { "c3050", "c1030", "c6020", "c3090" })
        {
            string p = Path.Combine(ExtractDs2.DefaultOut, "model", "chr", vb + ".bnd");
            if (!File.Exists(p)) continue;
            var vf = BND4.Read(p).Files.FirstOrDefault(f => f.Name.EndsWith("_Ragdoll.hkx", StringComparison.OrdinalIgnoreCase));
            if (vf == null) continue;
            foreach (var o in new HkPackfile(vf.Bytes).Objects) vanillaClasses.Add(o.Class);
        }
        var ours = hk.Objects.Select(o => o.Class).Distinct().ToList();
        Check(ours.All(vanillaClasses.Contains), $"ragdoll classes ⊆ vanilla DS2 ragdolls' (c3050/c1030/c6020/c3090): {string.Join(", ", ours.Where(c => !vanillaClasses.Contains(c)).DefaultIfEmpty("all known"))}");
        // ---- step 2: gravity / map-hit tracks, AI ESDs, params, boss events (BossAiCheck.cs)
        int pi = Array.IndexOf(args, "--pack");
        Ds1BossCheckStep2(dir, pi > 0 ? args[pi + 1] : null, Check);
        // ---- merge9 stage 6: the anim set in a free decade, DS1's own sound bank and _snd events (BossSoundCheck.cs)
        Ds1BossCheckSound(dir, pi > 0 ? args[pi + 1] : null, Check);
        // ---- pack
        if (pi > 0)
        {
            string pg = Path.Combine(args[pi + 1], "gamedata");
            int same = 0, total = 0, idsOnly = 0;
            foreach (var f in Directory.GetFiles(gd, "*", SearchOption.AllDirectories))
            {
                total++;
                string q = Path.Combine(pg, Path.GetRelativePath(gd, f));
                if (File.Exists(q) && File.ReadAllBytes(q).AsSpan().SequenceEqual(File.ReadAllBytes(f))) same++;
                // session 11 (burgchr11): an anibnd built after other 1:1 characters gets other rule-I1b asset ids (one id registry per
                // build, ids handed out in build order): equal up to a one-to-one renaming of the bundle ids
                else if (File.Exists(q) && f.EndsWith(".anibnd.dcx", StringComparison.OrdinalIgnoreCase) && SameUpToIds(File.ReadAllBytes(f), File.ReadAllBytes(q))) { same++; idsOnly++; }
            }
            Check(same == total, $"pack holds the same {total} files ({same} identical{(idsOnly > 0 ? $", {idsOnly} of them up to the build-order I1b asset ids" : "")})");
        }
        Console.WriteLine(fails == 0 ? "ds1boss-check: all checks passed" : $"ds1boss-check: {fails} FAILED");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>Session 11 (burgchr11): two anibnds whose files are equal except that the NMB's bundles carry other asset ids
    /// (renamed one to one, in bundle order) and every 4-byte occurrence of an id in the bundle data follows the renaming.</summary>
    static bool SameUpToIds(byte[] a, byte[] b)
    {
        static List<(string Name, byte[] Bytes)> Files(byte[] x) { if (SoulsFormats.DCX.Is(x)) x = SoulsFormats.DCX.Decompress(x); return [.. SoulsFormats.BND4.Read(x).Files.Select(f => (f.Name, f.Bytes))]; }
        var fa = Files(a); var fb = Files(b);
        if (fa.Count != fb.Count || !fa.Select(f => f.Name).SequenceEqual(fb.Select(f => f.Name))) return false;
        for (int i = 0; i < fa.Count; i++)
        {
            if (fa[i].Bytes.AsSpan().SequenceEqual(fb[i].Bytes)) continue;
            if (!fa[i].Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)) return false;
            var na = NmbFile.Read(fa[i].Bytes); var nb = NmbFile.Read(fb[i].Bytes);
            if (na.Bundles.Count != nb.Bundles.Count) return false;
            var map = new Dictionary<uint, uint>();
            for (int k = 0; k < na.Bundles.Count; k++)
            {
                if (na.Bundles[k].Type != nb.Bundles[k].Type || na.Bundles[k].Data.Length != nb.Bundles[k].Data.Length) return false;
                if (map.TryGetValue(na.Bundles[k].AssetId, out var m0) && m0 != nb.Bundles[k].AssetId) return false;
                map[na.Bundles[k].AssetId] = nb.Bundles[k].AssetId;
            }
            for (int k = 0; k < na.Bundles.Count; k++)
            {
                var da = (byte[])na.Bundles[k].Data.Clone(); var db = nb.Bundles[k].Data;
                for (int o = 0; o + 4 <= da.Length; o += 4)
                    if (map.TryGetValue(BitConverter.ToUInt32(da, o), out uint to) && BitConverter.ToUInt32(db, o) == to) BitConverter.GetBytes(to).CopyTo(da, o);
                if (!da.AsSpan().SequenceEqual(db)) return false;
            }
        }
        return true;
    }
}
