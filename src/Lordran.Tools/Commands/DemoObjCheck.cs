using System.Numerics;
using Lordran.Tools.Formats;
using Lordran.Tools.Formats.Havok;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// demo-obj-check OBJ.bnd [SCN.scnbnd ACTOR [--png OUT]] — session 9 (crow track): everything a DS2 demo object needs, side by side
/// with DS2's own demo bird (o00_6090): the archive's files, FLVER header/bounds, nodes, materials/textures, vertex layouts,
/// meshes (skinning, bone lists), the skeleton.hkx bones, the TPF textures, and (with a scnbnd) how the demo actor's track
/// names pair with the object's skeleton bones and FLVER nodes per cut, and where the actor root goes. Fix pass: the
/// FLVER tree vs the skeleton tree, the FLVER bind vs the hkx reference pose, every skinned node replayed by the exe's
/// rules vs the demo's own pose (pass = 0 off), and with --png OUT a render per cut (OUT_cutNNNN.png): the model skinned
/// by the exe replay (top row) and by the demo's own pose = DS1's (bottom row) at the cut's first, middle and last frame.
/// </summary>
public static class DemoObjCheck
{
    public static int Run(string[] args)
    {
        // a folder holding an archive's unpacked files (e.g. a review's extracted o18_3510.bnd) works too
        var bnd = Directory.Exists(args[1])
            ? new BND4 { Files = Directory.GetFiles(args[1]).Select((p, i) => new BinderFile(Binder.FileFlags.Flag1, i, Path.GetFileName(p), File.ReadAllBytes(p))).ToList() }
            : BND4.Read(args[1]);
        string model = Path.GetFileNameWithoutExtension(args[1]);
        Console.WriteLine($"== {args[1]}: {bnd.Files.Count} files");
        foreach (var f in bnd.Files) Console.WriteLine($"   {f.ID,3} {f.Flags,-12} {f.Bytes.Length,10:N0} {f.Name}");
        var flvFile = bnd.Files.First(f => f.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase));
        var flv = FLVER2.Read(flvFile.Bytes);
        var h = flv.Header;
        Console.WriteLine($"FLVER version {h.Version:X} bigEndian={h.BigEndian} unicode={h.Unicode} bbox {V(h.BoundingBoxMin)}..{V(h.BoundingBoxMax)} unk5c={h.Unk5C} unk5d={h.Unk5D} unk68={h.Unk68}");
        Console.WriteLine($"  nodes {flv.Nodes.Count}, dummies {flv.Dummies.Count}, materials {flv.Materials.Count}, gx {flv.GXLists.Count}, layouts {flv.BufferLayouts.Count}, meshes {flv.Meshes.Count}");
        for (int i = 0; i < flv.Nodes.Count; i++)
        {
            var n = flv.Nodes[i];
            if (i < 12 || i >= flv.Nodes.Count - 4)
                Console.WriteLine($"  node {i,3} '{n.Name}' parent {n.ParentIndex} child {n.FirstChildIndex} next {n.NextSiblingIndex} flags {n.Flags} bbox {V(n.BoundingBoxMin)}..{V(n.BoundingBoxMax)} T {V(n.Translation)} R {V(n.Rotation)}");
        }
        for (int i = 0; i < flv.Materials.Count; i++)
        {
            var m = flv.Materials[i];
            Console.WriteLine($"  material {i} '{m.Name}' mtd '{m.MTD}' gx {m.GXIndex} index {m.Index}");
            foreach (var t in m.Textures) Console.WriteLine($"      {t.ParamName,-22} '{t.Path}' scale {t.TilingScale} tile {t.TilingTypeU}/{t.TilingTypeV} unk14 {t.Unk14} unk18 {t.Unk18} unk1C {t.Unk1C}");
        }
        for (int i = 0; i < flv.GXLists.Count; i++)
            Console.WriteLine($"  gx {i}: " + string.Join(" ", flv.GXLists[i].Select(g => $"{g.ID}/{g.Unk04}/{g.Data.Length}")));
        for (int i = 0; i < flv.BufferLayouts.Count; i++)
            Console.WriteLine($"  layout {i}: " + string.Join(" ", flv.BufferLayouts[i].Select(m => $"{m.Semantic}{m.Index}:{m.Type}@s{m.Stream}")));
        int maxBones = 0;
        for (int i = 0; i < flv.Meshes.Count; i++)
        {
            var m = flv.Meshes[i];
            maxBones = Math.Max(maxBones, m.BoneIndices.Count);
            var lay = string.Join(",", m.VertexBuffers.Select(b => b.LayoutIndex));
            var fs = string.Join(" ", m.FaceSets.Select(f => $"{f.Flags}:{f.Indices.Count}{(f.TriangleStrip ? "s" : "")}{(f.CullBackfaces ? "c" : "")}"));
            float wmax = m.Vertices.Count == 0 ? 0 : m.Vertices.Max(v => v.BoneWeights[0] + v.BoneWeights[1] + v.BoneWeights[2] + v.BoneWeights[3]);
            float wmin = m.Vertices.Count == 0 ? 0 : m.Vertices.Min(v => v.BoneWeights[0] + v.BoneWeights[1] + v.BoneWeights[2] + v.BoneWeights[3]);
            int maxIdx = m.Vertices.Count == 0 ? 0 : m.Vertices.Max(v => Math.Max(Math.Max(v.BoneIndices[0], v.BoneIndices[1]), Math.Max(v.BoneIndices[2], v.BoneIndices[3])));
            var nw = string.Join(" ", m.Vertices.GroupBy(v => v.NormalW).OrderBy(g => g.Key).Take(4).Select(g => $"{g.Key}x{g.Count()}"));
            if (i < 6 || i == flv.Meshes.Count - 1)
                Console.WriteLine($"  mesh {i}: material {m.MaterialIndex} dynamic {m.Dynamic} node {m.NodeIndex} bones {m.BoneIndices.Count} (max local idx {maxIdx}) verts {m.Vertices.Count} layouts [{lay}] facesets {fs} weightsum {wmin:0.###}..{wmax:0.###} normalW {nw}");
        }
        // skinning identity across rebuilds: per mesh the NAMES its bone list points at, and the dummies' parent/attach names
        string skinSig = string.Join("|", flv.Meshes.Select(m => flv.Nodes[m.NodeIndex].Name + ":" + string.Join(",", m.BoneIndices.Select(b => flv.Nodes[b].Name))));
        string dmySig = string.Join("|", flv.Dummies.Select(d => $"{d.ReferenceID}:{(d.ParentBoneIndex >= 0 ? flv.Nodes[d.ParentBoneIndex].Name : "-")}:{(d.AttachBoneIndex >= 0 ? flv.Nodes[d.AttachBoneIndex].Name : "-")}"));
        Console.WriteLine($"  skin signature {System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(skinSig)).Take(6).Aggregate("", (s, x) => s + x.ToString("x2"))}, dummy signature {System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(dmySig)).Take(6).Aggregate("", (s, x) => s + x.ToString("x2"))}");
        Console.WriteLine($"  largest mesh bone list: {maxBones}; total vertices {flv.Meshes.Sum(m => m.Vertices.Count):N0}, faceset indices {flv.Meshes.Sum(m => m.FaceSets.Sum(f => f.Indices.Count)):N0}");
        // session 9 (crowfix, rule §3 skinned meshes): DS2 uploads per draw only face-set Unk06 palette matrices (0 = the
        // whole bone list; FUN_140b630d0 → FUN_140b335f0); a skinned vertex reading a slot past it gets a stale matrix
        int pastPalette = flv.Meshes.Where(m => m.Dynamic != 0).Sum(m =>
        {
            int cnt = m.FaceSets.Where(f => ((uint)f.Flags >> 24 & 0xF) == 0).Select(f => f.Unk06 == 0 ? m.BoneIndices.Count : f.Unk06).DefaultIfEmpty(m.BoneIndices.Count).Max();
            return m.Vertices.Count(v => Enumerable.Range(0, 4).Any(k => v.BoneIndices[k] >= cnt));
        });
        Console.WriteLine($"  DS2 palette upload (face-set Unk06): {pastPalette} skinned vertices read a slot DS2 never fills{(pastPalette == 0 ? " (PASS)" : " (FAIL: stale matrices, the s9-4/s9-5 black stick)")}");
        foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase)))
        {
            var tpf = TPF.Read(f.Bytes);
            Console.WriteLine($"  tpf {f.Name}: platform {tpf.Platform} enc {tpf.Encoding} flag2 {tpf.Flag2} " + string.Join(" ", tpf.Textures.Select(t => $"'{t.Name}' fmt {t.Format} type {t.Type} mips {t.Mipmaps} flags1 {t.Flags1} {t.Bytes.Length:N0}B")));
        }
        var skelFile = bnd.Files.FirstOrDefault(f => Path.GetFileName(f.Name).Equals("skeleton.hkx", StringComparison.OrdinalIgnoreCase));
        string[] skelBones = [];
        if (skelFile != null)
        {
            var pf = new HkPackfile(skelFile.Bytes);
            var sk = pf.ReadObject(pf.Objects.First(o => o.Class == "hkaSkeleton").Offset);
            skelBones = ((List<object?>)sk["bones"]!).Select(b => (string)((HkPObj)b!)["name"]!).ToArray();
            var parents = ((List<object?>)sk["parentIndices"]!).Select(x => Convert.ToInt32(x)).ToArray();
            Console.WriteLine($"  skeleton '{sk["name"]}': {skelBones.Length} bones; first: " + string.Join(", ", skelBones.Take(6).Select((b, i) => $"{b}(p{parents[i]})")));
            var nodeNames = flv.Nodes.Select(n => n.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var notInFlv = skelBones.Where(b => !nodeNames.Contains(b)).ToList();
            var notInSkel = flv.Nodes.Select(n => n.Name).Where(n => !skelBones.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
            Console.WriteLine($"  skeleton bones without a FLVER node: {notInFlv.Count} [{string.Join(", ", notInFlv.Take(8))}]; FLVER nodes not in the skeleton: {notInSkel.Count} [{string.Join(", ", notInSkel.Take(8))}]");
            // the bones the meshes skin to (bone lists → node names) must be skeleton bones
            var skinned = flv.Meshes.SelectMany(m => m.BoneIndices).Distinct().Where(i => i >= 0 && i < flv.Nodes.Count).Select(i => flv.Nodes[i].Name).ToList();
            Console.WriteLine($"  skinned nodes {skinned.Count}, of them not skeleton bones: {skinned.Count(n => !skelBones.Contains(n, StringComparer.OrdinalIgnoreCase))}; node order == skeleton order: {flv.Nodes.Select(n => n.Name).SequenceEqual(skelBones)}");
            // fix pass (rule R3c): the demo pose copy writes each track's LOCAL transform onto the node of the same name, so
            // every FLVER node that is a skeleton bone must hang under its skeleton parent's node (nearest one that has a
            // node); also parents ahead of children, and no Disabled bone node
            var usedIdx = flv.Meshes.SelectMany(m => m.BoneIndices).ToHashSet();
            var mism = new List<string>();
            int bonesWithNode = 0, disabledBones = 0, skinnedMism = 0;
            bool ordered = flv.Nodes.Select((x, i) => x.ParentIndex < i).All(v => v);
            for (int i = 0; i < flv.Nodes.Count; i++)
            {
                int b = Array.IndexOf(skelBones, flv.Nodes[i].Name);
                if (b < 0) continue;
                bonesWithNode++;
                if (flv.Nodes[i].Flags.HasFlag(FLVER.Node.NodeFlags.Disabled)) disabledBones++;
                int pb = parents[b];
                while (pb >= 0 && !flv.Nodes.Any(x => x.Name == skelBones[pb])) pb = parents[pb];
                string want = pb >= 0 ? skelBones[pb] : "-";
                string have = flv.Nodes[i].ParentIndex >= 0 ? flv.Nodes[flv.Nodes[i].ParentIndex].Name : "-";
                if (want != have) { mism.Add($"{flv.Nodes[i].Name}{(usedIdx.Contains(i) ? "*" : "")} ({have}, skeleton {want})"); if (usedIdx.Contains(i)) skinnedMism++; }
            }
            Console.WriteLine($"  FLVER tree vs skeleton tree: {mism.Count} of {bonesWithNode} bone nodes under another parent than the skeleton's ({skinnedMism} skinned, marked *){(mism.Count > 0 ? ": " + string.Join(", ", mism.Take(8)) + (mism.Count > 8 ? ", ..." : "") : "")}; "
                + $"Disabled bone nodes {disabledBones}; parents ahead of children: {ordered}; FLVER 'Master' node: {flv.Nodes.FindIndex(x => x.Name == "Master")}");
            // skinned bones: FLVER bind world (what the skin is relative to) vs the hkx reference pose in model space
            var refPose = ((List<object?>)sk["referencePose"]!).Select(x => (float[])x!).ToArray();
            var refW = new (Vector3 T, Quaternion R)[skelBones.Length];
            for (int b = 0; b < skelBones.Length; b++)
            {
                var f = refPose[b];
                var (t, r) = (new Vector3(f[0], f[1], f[2]), Quaternion.Normalize(new Quaternion(f[4], f[5], f[6], f[7])));
                refW[b] = parents[b] >= 0 ? (refW[parents[b]].T + Vector3.Transform(t, refW[parents[b]].R), refW[parents[b]].R * r) : (t, r);
            }
            var bindW = ChrCompare.World(flv);
            var dev = usedIdx.Where(i => i >= 0 && i < flv.Nodes.Count && Array.IndexOf(skelBones, flv.Nodes[i].Name) >= 0)
                .Select(i => (flv.Nodes[i].Name, D: Vector3.Distance(bindW[i].Translation, refW[Array.IndexOf(skelBones, flv.Nodes[i].Name)].T))).OrderByDescending(x => x.D).ToList();
            if (dev.Count > 0) Console.WriteLine($"  skinned bones: FLVER bind vs hkx reference pose max {dev[0].D * 1000:0.0} mm ({dev[0].Name}), {dev.Count(x => x.D > 0.01)} of {dev.Count} over 1 cm");
            // every bone node (also the unskinned helpers: collarbones, wing_*, the root), position and rotation
            var devAll = Enumerable.Range(0, flv.Nodes.Count).Select(i => (i, b: Array.IndexOf(skelBones, flv.Nodes[i].Name))).Where(x => x.b >= 0)
                .Select(x =>
                {
                    Matrix4x4.Decompose(bindW[x.i], out _, out var q, out var t);
                    float ang = 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(q), refW[x.b].R)), 0f, 1f)) * 180f / MathF.PI;
                    return (flv.Nodes[x.i].Name, D: Vector3.Distance(t, refW[x.b].T), A: ang);
                }).ToList();
            if (devAll.Count > 0) Console.WriteLine($"  all bone nodes: FLVER bind vs hkx reference pose max {devAll.Max(x => x.D) * 1000:0.0} mm ({devAll.MaxBy(x => x.D).Name}) / {devAll.Max(x => x.A):0.00} deg ({devAll.MaxBy(x => x.A).Name}); {devAll.Count(x => x.D > 0.01 || x.A > 1)} of {devAll.Count} over 1 cm or 1 deg");
        }
        string? png = args.SkipWhile(x => x != "--png").Skip(1).FirstOrDefault();
        // session 9 (crowfix): --pose-csv OUT writes per cut (OUT_cutNNNN.csv) every node's model-space pose at the cut's
        // first, middle and last frame, by the exe replay and by the demo's own pose (DS1), for an outside skinning check
        string? poseCsv = args.SkipWhile(x => x != "--pose-csv").Skip(1).FirstOrDefault();
        if (args.Length > 3)
        {
            string actor = args[3];
            var scn = BND4.Read(args[2]);
            var mqbFile = scn.Files.FirstOrDefault(x => x.Name.EndsWith(".mqb", StringComparison.OrdinalIgnoreCase));
            var mqb = mqbFile != null ? Ds2Mqb.Read(mqbFile.Bytes) : null;
            Vector3 CutOffset(string cut)
            {
                var c = mqb?.Cuts.FirstOrDefault(x => x.Name.Equals(cut, StringComparison.OrdinalIgnoreCase));
                var ev = c?.Events.FirstOrDefault(e => mqb!.Resources[e.ResourceIndex].Name.EndsWith("_offset.sib", StringComparison.OrdinalIgnoreCase));
                return ev?.Transforms.FirstOrDefault()?.Translation ?? Vector3.Zero;
            }
            foreach (var f in scn.Files.Where(f => f.Name.EndsWith("_a0000.hkx", StringComparison.OrdinalIgnoreCase)))
            {
                var acts = Ds2Scn.ReadActors(f.Bytes);
                var a = acts.FirstOrDefault(x => x.Name.Equals(actor, StringComparison.OrdinalIgnoreCase));
                if (a == null) { Console.WriteLine($"  {f.Name}: no actor {actor} (actors: {string.Join(", ", acts.Select(x => x.Name))})"); continue; }
                string pre = actor + "_";
                var trackNames = a.Bones.Select(b => b.StartsWith(pre, StringComparison.OrdinalIgnoreCase) ? b[pre.Length..] : b).ToArray();
                int matched = trackNames.Skip(1).Count(t => skelBones.Contains(t, StringComparer.OrdinalIgnoreCase));
                var missing = trackNames.Skip(1).Where(t => !skelBones.Contains(t, StringComparer.OrdinalIgnoreCase)).Take(6).ToList();
                var r0 = a.Bone(0, 0); var rEnd = a.Bone(0, a.Anim.NumFrames - 1);
                var c0 = a.Anim.NumFrames > 0 ? a.Bone(1, 0) : default;
                Console.WriteLine($"  {f.Name}: actor {actor} {a.Bones.Length} tracks, {a.Anim.NumFrames} frames; root track 0 '{a.Bones[0]}' ({a.Parents[0]}), track 1 '{a.Bones[1]}' (parent {a.Parents[1]}); "
                    + $"{matched}/{trackNames.Length - 1} child tracks name a skeleton bone{(missing.Count > 0 ? " (unmatched: " + string.Join(", ", missing) + ")" : "")}; root T {V(r0.T)} → {V(rEnd.T)}, track1 local T {V(c0.T)}");
                Console.WriteLine($"     other actors: {string.Join(", ", acts.Where(x => x != a).Select(x => $"{x.Name}({x.Bones.Length})"))}");
                var camActor = acts.FirstOrDefault(x => x.Name.StartsWith("cam", StringComparison.OrdinalIgnoreCase));
                Simulate(flv, a, camActor, png == null ? null : $"{Path.ChangeExtension(png, null)}_{f.Name[..7]}.png",
                    poseCsv == null ? null : $"{Path.ChangeExtension(poseCsv, null)}_{f.Name[..7]}.csv");
                // world space = the cut offset (MQB offset.sib event) + the offset-relative pose: equal across packs that
                // only move the offset (A/B "rebase")
                var off = CutOffset(f.Name[..7]);
                int last = a.Anim.NumFrames - 1;
                Console.WriteLine($"     world: cut offset {V(off)}; actor root f0 {V(off + a.Bone(0, 0).T)} f{last / 2} {V(off + a.Bone(0, last / 2).T)} f{last} {V(off + a.Bone(0, last).T)}"
                    + (camActor != null ? $"; camera f0 {V(off + camActor.Bone(0, 0).T)} f{last} {V(off + camActor.Bone(0, Math.Min(last, camActor.Anim.NumFrames - 1)).T)}" : "")
                    + $"; other actor roots f0 {string.Join(" ", acts.Where(x => x != a && x != camActor).Select(x => $"{x.Name} {V(off + x.Bone(0, 0).T)}"))}");
            }
        }
        return 0;
    }

    static string V(Vector3 v) => $"({v.X:0.###}, {v.Y:0.###}, {v.Z:0.###})";

    /// <summary>
    /// DS2's demo binder, offline: the object's FLVER-node skeleton posed from the actor's tracks by the exe's rules
    /// (FUN_140bc7be0 / FUN_140bc70d0): a node named "o??_????", "o????", "Character" or "cam????" takes the actor root
    /// track, any other node the track "&lt;actor&gt;_&lt;node&gt;", an unmatched node its nearest matched ancestor's track (none:
    /// its FLVER bind). The object itself stands at the cut offset (its MQB disposition: identity under the offset event,
    /// FUN_140488460 → FUN_140b191b0), so node poses are cut-offset-relative. Prints where the most-skinned node is drawn
    /// against where the demo means it to be, and whether the cut's camera looks at it (cosine of its −Y axis).
    /// </summary>
    static void Simulate(FLVER2 flv, Ds2Scn.Actor a, Ds2Scn.Actor? cam, string? png = null, string? poseCsv = null)
    {
        static bool Pattern(string s, string p) => s.Length == p.Length && s.Zip(p).All(x => x.Second == '?' ? char.IsDigit(x.First) : char.ToLowerInvariant(x.First) == char.ToLowerInvariant(x.Second));
        int TrackOf(string node)
        {
            string want = Pattern(node, "o??_????") || Pattern(node, "o????") || node.Equals("Character", StringComparison.OrdinalIgnoreCase) || Pattern(node, "cam????") ? a.Name : a.Name + "_" + node;
            return Array.IndexOf(a.Bones, want); // the exe compares the names byte by byte (FUN_140bc7be0)
        }
        int n = flv.Nodes.Count;
        var map = new int[n];
        for (int i = 0; i < n; i++)
        {
            int j = i, t = -1;
            while (j >= 0 && (t = TrackOf(flv.Nodes[j].Name)) < 0) j = flv.Nodes[j].ParentIndex;
            map[i] = t;
        }
        int rootNode = Array.FindIndex(map, t => t == 0);
        bool rootDirect = rootNode >= 0 && TrackOf(flv.Nodes[rootNode].Name) == 0;
        var weight = new Dictionary<int, int>();
        foreach (var m in flv.Meshes) foreach (var v in m.Vertices) for (int k = 0; k < 4; k++) if (v.BoneWeights[k] > 0.25f && v.BoneIndices[k] < m.BoneIndices.Count) { int node = m.BoneIndices[v.BoneIndices[k]]; weight[node] = weight.GetValueOrDefault(node) + 1; }
        int probe = weight.Count > 0 ? weight.OrderByDescending(x => x.Value).First().Key : 0;
        Console.WriteLine($"     DS2 binder: actor root track → {(rootDirect ? $"node {rootNode} '{flv.Nodes[rootNode].Name}'" : "NO node (the actor root's motion is dropped)")}; nodes without any track: {map.Count(t => t < 0)}; probe node '{flv.Nodes[probe].Name}' (most skinned vertices) ← track {map[probe]} '{(map[probe] >= 0 ? a.Bones[map[probe]] : "-")}'");
        // the exe's pose copy (FUN_140308220): node i gets the demo's LOCAL transform of track map[i] (hkaPose+8,
        // FUN_1407736c0), also when map[i] is an ancestor's track; no track: the node's bind (the runtime skeleton's
        // reference pose = the FLVER node TRS)
        (Vector3 T, Quaternion R) Local(int node, float f)
        {
            int t = map[node];
            if (t >= 0) { var s = a.Anim.Sample(TrackIndex(t), f); return (s.T, s.R); }
            var nd = flv.Nodes[node];
            var r = Quaternion.CreateFromYawPitchRoll(nd.Rotation.Y, 0, 0) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, nd.Rotation.Z) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, nd.Rotation.X);
            return (nd.Translation, r);
        }
        int TrackIndex(int bone) { int t = Array.IndexOf(a.Anim.TrackToBone, bone); return t < 0 ? bone : t; }
        (Vector3 T, Quaternion R) Model(int node, float f)
        {
            var (t, r) = Local(node, f);
            for (int p = flv.Nodes[node].ParentIndex; p >= 0; p = flv.Nodes[p].ParentIndex)
            {
                var (pt, pr) = Local(p, f);
                t = pt + Vector3.Transform(t, pr); r = pr * r;
            }
            return (t, r);
        }
        Vector3 Drawn(int node, float f) => Model(node, f).T;
        int probeTrack = Array.IndexOf(a.Bones, a.Name + "_" + flv.Nodes[probe].Name);
        int frames = a.Anim.NumFrames;
        foreach (int f in new[] { 0, frames / 4, frames / 2, 3 * frames / 4, frames - 1 })
        {
            var drawn = Drawn(probe, f);
            var meant = probeTrack >= 0 ? a.Bone(probeTrack, f).T : Vector3.Zero;
            string Cos(Vector3 p)
            {
                if (cam == null) return "-";
                var (ct, cr) = cam.Bone(0, f);
                var look = Vector3.Transform(-Vector3.UnitY, cr);
                var d = p - ct;
                return d.Length() < 1e-3f ? "-" : $"{Vector3.Dot(Vector3.Normalize(d), look):0.00}@{d.Length():0}m";
            }
            Console.WriteLine($"       frame {f,3}: drawn {V(drawn)} cam {Cos(drawn)} | demo means {V(meant)} cam {Cos(meant)} | off by {(drawn - meant).Length():0.0} m");
        }
        // fix pass: EVERY skinned node (the skin follows each node's posed world: posed * inverse(bind)), drawn by the exe's
        // rules vs the demo skeleton's own model-space pose of its track; pass = 0 nodes off by > 1 cm or > 1 deg
        var skinnedNodes = flv.Meshes.SelectMany(m => m.BoneIndices).Where(i => i >= 0 && i < n).Distinct().ToList();
        var worst = new Dictionary<int, (float Pos, float Ang)>();
        int noTrack = 0;
        foreach (int f in new[] { 0, frames / 4, frames / 2, 3 * frames / 4, frames - 1 })
            foreach (int node in skinnedNodes)
            {
                int own = TrackOf(flv.Nodes[node].Name);
                if (own < 0) { if (f == 0) noTrack++; continue; }
                var drawn = Model(node, f);
                var meant = a.Bone(own, f);
                float d = (drawn.T - meant.T).Length();
                float ang = 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(drawn.R), Quaternion.Normalize(meant.R))), 0f, 1f)) * 180f / MathF.PI;
                var w = worst.GetValueOrDefault(node);
                worst[node] = (Math.Max(w.Pos, d), Math.Max(w.Ang, ang));
            }
        var bad = worst.Where(x => x.Value.Pos > 0.01f || x.Value.Ang > 1f).OrderByDescending(x => x.Value.Pos).ToList();
        string Parent(int node) => flv.Nodes[node].ParentIndex >= 0 ? flv.Nodes[flv.Nodes[node].ParentIndex].Name : "-";
        Console.WriteLine($"     all skinned nodes: {worst.Count} compared at 5 frames ({noTrack} without an own track); {bad.Count} off by > 1 cm or > 1 deg{(bad.Count == 0 ? " (PASS)" : "")}"
            + (bad.Count > 0 ? "; worst " + string.Join(", ", bad.Take(4).Select(x => $"{flv.Nodes[x.Key].Name} {x.Value.Pos:0.00} m/{x.Value.Ang:0.0} deg (FLVER parent {Parent(x.Key)})")) : $"; max {worst.Values.DefaultIfEmpty().Max(x => x.Pos) * 1000:0.0} mm / {worst.Values.DefaultIfEmpty().Max(x => x.Ang):0.00} deg"));
        if (png != null) Render(png, flv, frames, f => Enumerable.Range(0, n).Select(i => Model(i, f)).ToArray(),
            f => Enumerable.Range(0, n).Select(i => TrackOf(flv.Nodes[i].Name) is int t && t >= 0 ? a.Bone(t, f) : Model(i, f)).ToArray(), cam);
        if (poseCsv != null)
        {
            var sb = new System.Text.StringBuilder("frame,node,name,rx_t,ry_t,rz_t,rx_q,ry_q,rz_q,rw_q,dx_t,dy_t,dz_t,dx_q,dy_q,dz_q,dw_q,cam_fx,cam_fy,cam_fz,cam_ux,cam_uy,cam_uz\n");
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (int f in new[] { 0, frames / 2, frames - 1 })
            {
                var camR = cam != null ? cam.Bone(0, Math.Min(f, cam.Anim.NumFrames - 1)).R : Quaternion.Identity;
                Vector3 fwd = Vector3.Transform(-Vector3.UnitY, camR), up = Vector3.Transform(Vector3.UnitZ, camR);
                for (int i = 0; i < n; i++)
                {
                    var r = Model(i, f);
                    var d = TrackOf(flv.Nodes[i].Name) is int t && t >= 0 ? a.Bone(t, f) : r;
                    sb.AppendLine(string.Join(",", new object[] { f, i, flv.Nodes[i].Name, r.T.X, r.T.Y, r.T.Z, r.R.X, r.R.Y, r.R.Z, r.R.W, d.T.X, d.T.Y, d.T.Z, d.R.X, d.R.Y, d.R.Z, d.R.W, fwd.X, fwd.Y, fwd.Z, up.X, up.Y, up.Z }
                        .Select(x => x is float fl ? fl.ToString("R", inv) : Convert.ToString(x, inv))));
                }
            }
            File.WriteAllText(poseCsv, sb.ToString());
            Console.WriteLine($"     pose csv {poseCsv}: {n} nodes at frames 0, {frames / 2}, {frames - 1} (exe replay r*, demo pose d*, camera axes)");
        }
    }

    /// <summary>
    /// The model skinned (linear blend, FLVER bind) by two node poses per frame — top row the exe replay, bottom row the
    /// demo's own pose — at the cut's first, middle and last frame; orthographic along the cut camera's view axis (screen
    /// up = camera +Z; screen right = camera +X, so the game may show it mirrored), centred on the bottom row's model.
    /// </summary>
    static void Render(string png, FLVER2 flv, int frames, Func<int, (Vector3 T, Quaternion R)[]> replay, Func<int, (Vector3 T, Quaternion R)[]> demo, Ds2Scn.Actor? cam)
    {
        const int size = 360;
        int[] at = [0, frames / 2, frames - 1];
        var img = new Textures.Image(size * at.Length, size * 2);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = new Textures.Rgba(30, 30, 40, 255);
        var depth = new float[img.Pixels.Length];
        Array.Fill(depth, float.MinValue);
        var bind = ChrCompare.World(flv);
        var inv = bind.Select(m => Matrix4x4.Invert(m, out var x) ? x : Matrix4x4.Identity).ToArray();
        for (int k = 0; k < at.Length; k++)
        {
            int f = at[k];
            var camR = cam != null ? cam.Bone(0, f).R : Quaternion.Identity;
            Vector3 right = Vector3.Transform(Vector3.UnitX, camR), up = Vector3.Transform(Vector3.UnitZ, camR), fwd = Vector3.Transform(-Vector3.UnitY, camR);
            var rows = new[] { replay(f), demo(f) };
            var skinned = rows.Select(pose =>
            {
                var skin = pose.Select((x, i) => inv[i] * Matrix4x4.CreateFromQuaternion(x.R) * Matrix4x4.CreateTranslation(x.T)).ToArray();
                return flv.Meshes.Select(m => m.Vertices.Select(v =>
                {
                    var acc = Vector3.Zero; float ws = 0;
                    for (int j = 0; j < 4; j++)
                    {
                        float w = v.BoneWeights[j];
                        if (w <= 0) continue;
                        int node = m.BoneIndices.Count > 0 ? m.BoneIndices[v.BoneIndices[j]] : v.BoneIndices[j];
                        acc += w * Vector3.Transform(v.Position, skin[node]); ws += w;
                    }
                    return ws > 0 ? acc / ws : Vector3.Transform(v.Position, skin[Math.Max(0, m.NodeIndex)]);
                }).ToArray()).ToArray();
            }).ToArray();
            var all = skinned[1].SelectMany(x => x).ToList();
            var c = all.Aggregate(Vector3.Zero, (s, v) => s + v) / Math.Max(1, all.Count);
            float ext = all.Select(v => Math.Max(Math.Abs(Vector3.Dot(v - c, right)), Math.Abs(Vector3.Dot(v - c, up)))).DefaultIfEmpty(1).Max();
            float scale = size * 0.45f / Math.Max(ext, 0.5f);
            for (int row = 0; row < 2; row++)
            {
                float ox = k * size, oy = row * size;
                (float X, float Y, float Z) Proj(Vector3 v) => (ox + size / 2f + Vector3.Dot(v - c, right) * scale, oy + size / 2f - Vector3.Dot(v - c, up) * scale, -Vector3.Dot(v - c, fwd));
                for (int mi = 0; mi < flv.Meshes.Count; mi++)
                {
                    var m = flv.Meshes[mi];
                    var sv = skinned[row][mi];
                    foreach (var fs in m.FaceSets.Where(x => x.Flags == FLVER2.FaceSet.FSFlags.None).Take(1))
                    {
                        var idx = fs.Triangulate(m.Vertices.Count < ushort.MaxValue);
                        for (int t = 0; t + 2 < idx.Count; t += 3)
                        {
                            var (p0, p1, p2) = (sv[idx[t]], sv[idx[t + 1]], sv[idx[t + 2]]);
                            var nn = Vector3.Cross(p1 - p0, p2 - p0);
                            float lt = nn.LengthSquared() > 1e-12f ? Math.Clamp(0.3f + 0.7f * MathF.Abs(Vector3.Dot(Vector3.Normalize(nn), Vector3.Normalize(-fwd + up * 0.5f))), 0, 1) : 0.5f;
                            var col = row == 0 ? new Textures.Rgba((byte)(220 * lt), (byte)(180 * lt), (byte)(120 * lt), 255) : new Textures.Rgba((byte)(140 * lt), (byte)(190 * lt), (byte)(230 * lt), 255);
                            Tri(img, depth, Proj(p0), Proj(p1), Proj(p2), col, ox, ox + size, oy, oy + size);
                        }
                    }
                }
            }
        }
        Textures.Png.Save(img, png);
        Console.WriteLine($"     render {png}: frames {string.Join(", ", at)}; top = exe replay (this model), bottom = demo pose (DS1)");
    }

    static void Tri(Textures.Image img, float[] depth, (float X, float Y, float Z) a, (float X, float Y, float Z) b, (float X, float Y, float Z) c, Textures.Rgba col, float x0, float x1, float y0, float y1)
    {
        int minX = (int)Math.Max(x0, Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)))), maxX = (int)Math.Min(x1 - 1, Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
        int minY = (int)Math.Max(y0, Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)))), maxY = (int)Math.Min(y1 - 1, Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
        float d = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (Math.Abs(d) < 1e-6f) return;
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float l1 = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / d, l2 = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / d, l3 = 1 - l1 - l2;
                if (l1 < 0 || l2 < 0 || l3 < 0) continue;
                float z = l1 * a.Z + l2 * b.Z + l3 * c.Z;
                int k = y * img.Width + x;
                if (z <= depth[k]) continue;
                depth[k] = z;
                img[x, y] = col;
            }
    }
}
