using System.Buffers.Binary;

namespace Lordran.Tools.Formats.Morpheme;

/// <summary>Skeleton for a rig replacement (the format of ds1_bridge.py's / ds1-anim-to-nsa's skeleton.json):
/// Morpheme rig bones in order, bind pose in Morpheme character space (Z up), rotations (x, y, z, w).</summary>
public sealed class MorphemeSkeleton
{
    public List<string> Names { get; set; } = [];
    public List<int> Parents { get; set; } = [];
    public List<double[]> BindPos { get; set; } = [];
    public List<double[]> BindRot { get; set; } = [];
    public int Traj { get; set; } = 2;
    public int Root { get; set; } = 3;
    public List<int>? AnimToRig { get; set; }

    static readonly System.Text.Json.JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower, WriteIndented = true,
    };
    public static MorphemeSkeleton Load(string path) => System.Text.Json.JsonSerializer.Deserialize<MorphemeSkeleton>(File.ReadAllText(path), Json)!;
    public void Save(string path) => File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(this, Json));
}

/// <summary>
/// Puts a new rig into a DS2 NMB (session 8 port of nmb_rig.py <c>replace_rig</c> / <c>network_rig_refs</c> and
/// ds1_rig_into_nmb.py; NSA-FORMAT.md §2.6 and §3):
/// <list type="number">
/// <item>the rig bundle (type 1) is rewritten (names, parents, bind pose, trajectory/root indices), size updated,
/// assetId and GUID kept;</item>
/// <item>the channel map (type 2) is rewritten (anim_to_rig, used bits); its GUID tail (= rig id) stays valid;</item>
/// <item>in the network (type 10) the RIG attrib's rig-data-size field is patched; with a bone remap (old → new rig
/// index) the bone-index fields (HEAD_LOOK_CHAIN 25, LOCK_FOOT_CHAIN 31, HIPS_IK_ANIM_SET_DEF 34,
/// BASIC_UNEVEN_TERRAIN_CHAIN 73) and sorted bone lists (INT_ARRAY 8) are remapped, and with an unchanged bone count
/// the per-bone arrays (BOOL_ARRAY 6, FLOAT_ARRAY 9, FEATHER_BLEND2_CHANNEL_ALPHAS 87) are permuted in place.</item>
/// </list>
/// Not handled (logged): the mirror mapping (66) and resizing per-bone arrays when the bone count changes (needs a
/// network re-serialiser: pointers inside it). Everything else is copied; bundle positions may shift (all intra-bundle
/// pointers are bundle-relative, references are assetIds). Python checks: identity replace of c1030 = identical file;
/// DS1 c2500 (52 bones + 3 = 55 = c1030's count) into c1030: 91 index fields, 15 bone lists, 18 per-bone arrays.
/// AttribData blocks in the network start with 8 zero bytes, u16 0xFFFF (refcount), u16 type, 8-aligned.
/// </summary>
public static class NmbRigReplace
{
    /// <summary>attrib type → offsets of u32 rig-bone-index fields inside the AttribData block.</summary>
    public static readonly Dictionary<int, int[]> NetIndexFields = new()
    {
        [25] = [0x20, 0x24],                                         // HEAD_LOOK_CHAIN: end joint, root joint
        [31] = [0x48, 0x4C, 0x50, 0x54, 0x58],                       // LOCK_FOOT_CHAIN: hip, knee, ankle, ball, toe
        [34] = [0x30, 0x34, 0x38, 0x3C, 0x40, 0x44, 0x48, 0x4C, 0x50], // HIPS_IK_ANIM_SET_DEF: L leg x4, R leg x4, pelvis
        [73] = [0x70, 0x74, 0x78, 0x7C, 0x80, 0xA0, 0xA4, 0xA8, 0xAC, 0xB0], // BASIC_UNEVEN_TERRAIN_CHAIN, 2 limbs: hip, knee, ankle, ball, toe
        // (session 11, crash11: +0x84 / +0xB4 are the limbs' foot-joint COUNTS (vanilla 3), not bones - were remapped as bone 3;
        // harmless while template bone 3 = Root maps to new bone 3, wrong otherwise. See IkChains, rule IK1.)
    };
    // not bone indices (checked in session 8): LOCK_FOOT_CHAIN +0x5c and PREDICTIVE_UNEVEN_TERRAIN (76) +0x30/+0x34
    // = footfall event ids 10/11; BASIC_UNEVEN_TERRAIN_SETUP (69) +0x20 = 2 (up axis Z).

    public sealed class Refs
    {
        public List<int> RigAttr = [], IndexFields = [];
        public List<(int Offset, int Count)> BoneLists = [];
        public List<(int Offset, int Type, int Count)> PerBone = [];
        public List<(int Offset, int Type)> Unhandled = [];
    }

    /// <summary>All AttribData blocks (offset, type &lt; 200) in network data.</summary>
    public static List<(int Offset, int Type)> ScanAttribs(byte[] x, int maxType = 200)
    {
        var hits = new List<(int, int)>();
        for (int i = x.AsSpan().IndexOf((ReadOnlySpan<byte>)[0xFF, 0xFF]); i >= 0;)
        {
            if (i % 8 == 0 && i >= 8 && x.AsSpan(i - 8, 8).IndexOfAnyExcept((byte)0) < 0)
            {
                int t = BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(i + 2));
                if (t < maxType) hits.Add((i - 8, t));
            }
            int next = x.AsSpan(i + 2).IndexOf((ReadOnlySpan<byte>)[0xFF, 0xFF]);
            i = next < 0 ? -1 : i + 2 + next;
        }
        return hits;
    }

    /// <summary>Locate the rig-dependent values in the network bundle data (nmb_rig.py network_rig_refs).</summary>
    public static Refs NetworkRigRefs(byte[] x, int nrig)
    {
        var refs = new Refs();
        foreach (var (o, t) in ScanAttribs(x, 0x10000))
        {
            if (t == 20) refs.RigAttr.Add(o);
            else if (NetIndexFields.TryGetValue(t, out var fields)) refs.IndexFields.AddRange(fields.Select(f => o + f));
            else if (t == 8)
            {
                int n = (int)NmbFile.U32(x, o + 16), p = (int)NmbFile.U64(x, o + 24);
                if (nrig - 2 <= n && n <= nrig)
                {
                    var vals = Enumerable.Range(0, n).Select(i => NmbFile.I32(x, o + p + 4 * i)).ToArray();
                    bool ok = vals.All(v => v >= 0 && v < nrig);
                    for (int i = 1; ok && i < n; i++) if (vals[i] < vals[i - 1]) ok = false;
                    if (ok) refs.BoneLists.Add((o + p, n));
                }
            }
            else if (t is 6 or 9)
            {
                int n = (int)NmbFile.U32(x, o + 16);
                if (n == nrig) refs.PerBone.Add((o, t, n));
            }
            else if (t == 87)
            {
                int n = BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(o + 18));
                if (n == nrig) refs.PerBone.Add((o, t, n));
            }
            else if (t == 66) refs.Unhandled.Add((o, t));
        }
        return refs;
    }

    /// <summary>
    /// Replace the first rig and channel map of <paramref name="nmb"/> with <paramref name="skel"/>; returns the new
    /// file bytes. <paramref name="remap"/>: ordered (old rig index, new rig index) pairs applied to the network.
    /// </summary>
    public static byte[] Replace(NmbFile nmb, MorphemeSkeleton skel, IReadOnlyList<(int Old, int New)>? remap, List<string> log, out Refs refs)
    {
        int ri = nmb.IndexOf(1), mi = nmb.IndexOf(2), ni = nmb.IndexOf(10);
        var r = NmbRig.Read(nmb.Bundles[ri].Data);
        var m = NmbChannelMap.Read(nmb.Bundles[mi].Data);
        int oldN = r.Names.Count, n = skel.Names.Count;
        r.Names = [.. skel.Names]; r.Parents = [.. skel.Parents];
        r.BindPos = skel.BindPos.Select(p => new[] { (float)p[0], (float)p[1], (float)p[2], 0f }).ToList();
        r.BindRot = skel.BindRot.Select(q => new[] { (float)q[0], (float)q[1], (float)q[2], (float)q[3] }).ToList();
        r.BindUsed = Enumerable.Repeat(true, n).ToArray();
        r.Traj = skel.Traj; r.Root = skel.Root;
        m.AnimToRig = skel.AnimToRig != null ? [.. skel.AnimToRig] : [.. Enumerable.Range(1, n - 1)];
        var usedSet = m.AnimToRig.ToHashSet();
        m.Used = Enumerable.Range(0, n).Select(usedSet.Contains).ToArray();
        byte[] rigData = r.Write(), mapData = m.Write();
        var net = (byte[])nmb.Bundles[ni].Data.Clone();
        refs = NetworkRigRefs(net, oldN);
        foreach (int o in refs.RigAttr)
        {
            ulong old = NmbFile.U64(net, o + 0x18);
            BinaryPrimitives.WriteUInt64LittleEndian(net.AsSpan(o + 0x18), (ulong)rigData.Length);
            log.Add($"network RIG attrib @0x{o:x}: rig size 0x{old:x} -> 0x{rigData.Length:x}");
        }
        if (remap != null)
        {
            var map = new Dictionary<int, int>();
            foreach (var (a, b) in remap) map[a] = b;
            foreach (int o in refs.IndexFields)
            {
                int v = (int)NmbFile.U32(net, o);
                if (map.TryGetValue(v, out int nv)) BinaryPrimitives.WriteUInt32LittleEndian(net.AsSpan(o), (uint)nv);
            }
            foreach (var (o, cnt) in refs.BoneLists)
                for (int i = 0; i < cnt; i++)
                {
                    int v = NmbFile.I32(net, o + 4 * i);
                    BinaryPrimitives.WriteInt32LittleEndian(net.AsSpan(o + 4 * i), map.TryGetValue(v, out int nv) ? nv : v);
                }
            log.Add($"remapped {refs.IndexFields.Count} index fields, {refs.BoneLists.Count} bone lists");
            if (n == oldN) // same bone count: per-bone arrays can be permuted in place
            {
                foreach (var (o, t, cnt) in refs.PerBone)
                {
                    int p = (int)NmbFile.U64(net, o + 0x18);
                    int sz = t == 6 ? 1 : 4;
                    var old = Enumerable.Range(0, cnt).Select(i => net.AsSpan(o + p + sz * i, sz).ToArray()).ToArray();
                    // 87: default 0.0 (alpha off for unmapped bones); 9: keep the old value; 6: default 1
                    var def = t == 87 ? BitConverter.GetBytes(0f) : t == 6 ? new byte[] { 1 } : null;
                    var nw = Enumerable.Range(0, cnt).Select(i => def ?? old[i]).ToArray();
                    foreach (var (a, b) in remap) nw[b] = old[a];
                    for (int i = 0; i < cnt; i++) nw[i].CopyTo(net, o + p + sz * i);
                }
                log.Add($"permuted {refs.PerBone.Count} per-bone arrays (types 6/9/87); NOT handled: {refs.Unhandled.Count} attribs (66 = mirror mapping) at "
                        + string.Join(", ", refs.Unhandled.Select(u => $"0x{u.Offset:x}(t{u.Type})")));
            }
        }
        if (n != oldN)
            log.Add($"WARNING bone count {oldN} -> {n}: {refs.PerBone.Count} per-bone arrays, {refs.BoneLists.Count} bone lists, "
                    + $"{refs.IndexFields.Count} index fields, {refs.Unhandled.Count} mirror/terrain attribs still sized/indexed for the old rig "
                    + "(network re-serialisation needed, not implemented)");
        return nmb.Rebuild(new Dictionary<int, byte[]> { [ri] = rigData, [mi] = mapData, [ni] = net });
    }

    static string Norm(string s) => s.Replace(' ', '_').ToLowerInvariant();

    // ================================================================== mapped replacement (session 8, track chr step 2)

    /// <summary>
    /// Bone correspondence between a template network's rig (old) and a new rig with the same bone count:
    /// <c>NewToOld[b]</c> = the old bone whose per-bone network data new bone b takes (-1: none, b inherits from its
    /// nearest ancestor that has one); <c>OldToNew[a]</c> = the new bone that old bone a's index fields and bone lists
    /// point to (-1: unmapped).
    /// </summary>
    public sealed class BoneMatch
    {
        public int[] NewToOld = [], OldToNew = [];
        public List<string> Notes = [];
    }

    /// <summary>
    /// DS2 humanoid rig (c1030 family) → DS1 rig bridged by Ds1AnimBridge: names equal after "L_Thigh" == "L Thigh";
    /// DS2 Master (trajectory) = Trajectory, DS2 Root (character root) = DS1 Master (as ds1_rig_into_nmb.py). Old bones
    /// with no namesake go to <paramref name="fallback"/> (DS2 name → new name) for index fields and bone lists only.
    /// </summary>
    public static BoneMatch MatchByName(IReadOnlyList<string> oldNames, IReadOnlyList<string> newNames, IReadOnlyDictionary<string, string>? fallback = null)
    {
        // a rig with the bridge's own names (Trajectory, DS1 Master) gets ds1_rig_into_nmb.py's specials; a rig already
        // renamed to DS2's names (Master, Root) matches directly
        var special = newNames.Contains("Trajectory") ? new Dictionary<string, string> { ["root"] = "master", ["master"] = "trajectory" } : [];
        var newIdx = new Dictionary<string, int>();
        for (int i = 0; i < newNames.Count; i++) newIdx.TryAdd(Norm(newNames[i]), i);
        var m = new BoneMatch { NewToOld = Enumerable.Repeat(-1, newNames.Count).ToArray(), OldToNew = Enumerable.Repeat(-1, oldNames.Count).ToArray() };
        for (int a = 0; a < oldNames.Count; a++)
        {
            string k = Norm(oldNames[a]);
            if (special.TryGetValue(k, out var s)) k = s;
            if (newIdx.TryGetValue(k, out int b)) { m.OldToNew[a] = b; m.NewToOld[b] = a; }
        }
        for (int a = 0; a < oldNames.Count; a++)
        {
            if (m.OldToNew[a] >= 0) continue;
            if (fallback != null && fallback.TryGetValue(oldNames[a], out var f) && newIdx.TryGetValue(Norm(f), out int b))
            {
                m.OldToNew[a] = b;
                m.Notes.Add($"{oldNames[a]} -> {newNames[b]} (fallback)");
            }
            else m.Notes.Add($"{oldNames[a]} -> (none)");
        }
        for (int b = 0; b < newNames.Count; b++)
            if (m.NewToOld[b] < 0) m.Notes.Add($"new {newNames[b]}: no template bone (per-bone data from its parent)");
        return m;
    }

    /// <summary>
    /// The mirror quaternion of rig bone j for the left/right mirror mapping (attrib 66): C_j = W_j⁻¹ · S(W_twin(j)),
    /// W = bind-pose world rotation in Morpheme space, S(q) = (w, x, -y, -z) the reflection x → -x (character left is +X).
    /// Evidence (session 8, scratchpad mirrorfit.py): reproduces every stored quaternion (up to sign) of c1030 (55),
    /// c3050 (40), c0001 (59), c6030 (248), c2250 (12), c5061 (173), c1010 (68), c3000 (79) and c1320 (64, DS2's copy of
    /// DS1's skeleton rig); the inverse form fails on c6030 and c1010.
    /// </summary>
    public static float[] MirrorQuat(Q4[] world, int j, int twin)
    {
        var w = world[twin];
        var s = new Q4(w.W, w.X, -w.Y, -w.Z);
        var c = MorphemeMath.Mul(MorphemeMath.Conj(world[j]), s);
        if (c.W < 0) c = c.Neg();
        return [(float)c.X, (float)c.Y, (float)c.Z, (float)c.W];
    }

    static bool IsTwin(string a, string b)
    {
        foreach (var (l, r) in new[] { ("L_", "R_"), ("L ", "R "), ("L", "R") })
            if (a.StartsWith(l) && b.StartsWith(r) && a[l.Length..] == b[r.Length..]) return true;
        return false;
    }

    /// <summary>
    /// Like <see cref="Replace"/> for a new rig with the template's bone count, but with every rig-dependent part of the
    /// network rebuilt for the new skeleton instead of index-remapped (session 8, the DS1 rig in c1030's network):
    /// <list type="bullet">
    /// <item>index fields (25/31/34/73) and short bone lists (INT_ARRAY on FilterTransforms/MirrorTransforms nodes, e.g.
    /// the 10 weapon bones excluded from mirroring) through <c>OldToNew</c>;</item>
    /// <item>rig-sized bone lists ("all bones but Master" × 15 on c1030) as complements: the excluded old bones are mapped,
    /// the list becomes every other new bone (sorted);</item>
    /// <item>per-bone arrays (6/9/87: blend masks, per-bone floats) through <c>NewToOld</c>, new-only bones inheriting from
    /// their nearest mapped ancestor (DS1's twist bones get their parent limb's mask value);</item>
    /// <item>the mirror mapping (66) recomputed from the new bind pose (<see cref="MirrorQuat"/>), L/R twins by name; to
    /// keep the attrib's size, surplus pairs are listed as two self-mirrored bones (end "Nub" bones first: leaves without
    /// skin, mirroring one onto itself or onto its twin gives the same local transform).</item>
    /// </list>
    /// </summary>
    public static byte[] ReplaceMapped(NmbFile nmb, MorphemeSkeleton skel, BoneMatch match, List<string> log)
    {
        int ri = nmb.IndexOf(1), mi = nmb.IndexOf(2), ni = nmb.IndexOf(10);
        var r = NmbRig.Read(nmb.Bundles[ri].Data);
        var m = NmbChannelMap.Read(nmb.Bundles[mi].Data);
        int oldN = r.Names.Count, n = skel.Names.Count;
        if (n != oldN) throw new NotSupportedException($"ReplaceMapped needs the template's bone count ({oldN}), got {n}: network re-serialiser not implemented");
        var oldParents = r.Parents.ToArray();
        r.Names = [.. skel.Names]; r.Parents = [.. skel.Parents];
        r.BindPos = skel.BindPos.Select(p => new[] { (float)p[0], (float)p[1], (float)p[2], 0f }).ToList();
        r.BindRot = skel.BindRot.Select(q => new[] { (float)q[0], (float)q[1], (float)q[2], (float)q[3] }).ToList();
        r.BindUsed = Enumerable.Repeat(true, n).ToArray();
        r.Traj = skel.Traj; r.Root = skel.Root;
        m.AnimToRig = skel.AnimToRig != null ? [.. skel.AnimToRig] : [.. Enumerable.Range(1, n - 1)];
        var usedSet = m.AnimToRig.ToHashSet();
        m.Used = Enumerable.Range(0, n).Select(usedSet.Contains).ToArray();
        byte[] rigData = r.Write(), mapData = m.Write();

        var netBytes = (byte[])nmb.Bundles[ni].Data.Clone();
        var refs = NetworkRigRefs(netBytes, oldN);
        foreach (int o in refs.RigAttr)
            BinaryPrimitives.WriteUInt64LittleEndian(netBytes.AsSpan(o + 0x18), (ulong)rigData.Length);
        // index fields
        int idxDone = 0;
        foreach (int o in refs.IndexFields)
        {
            int v = (int)NmbFile.U32(netBytes, o);
            if (v < 0 || v >= oldN) continue;
            int nv = match.OldToNew[v];
            if (nv < 0) { log.Add($"WARNING index field @0x{o:x}: template bone {v} has no counterpart, left as is"); continue; }
            BinaryPrimitives.WriteUInt32LittleEndian(netBytes.AsSpan(o), (uint)nv);
            idxDone++;
        }
        // per-bone arrays: new[b] = old[src(b)], src inherited down the new hierarchy
        var src = new int[n];
        for (int b = 0; b < n; b++)
        {
            int s = -1;
            for (int x = b; x >= 0 && s < 0; x = skel.Parents[x]) s = match.NewToOld[x];
            src[b] = s < 0 ? 0 : s;
        }
        foreach (var (o, t, cnt) in refs.PerBone)
        {
            int p = (int)NmbFile.U64(netBytes, o + 0x18);
            int sz = t == 6 ? 1 : 4;
            var old = Enumerable.Range(0, cnt).Select(i => netBytes.AsSpan(o + p + sz * i, sz).ToArray()).ToArray();
            for (int b = 0; b < cnt; b++) old[src[b]].CopyTo(netBytes, o + p + sz * b);
        }
        // bone lists (INT_ARRAY on FilterTransforms 105 / MirrorTransforms 135 nodes)
        var net = new NmbNetwork(netBytes);
        int rigLists = 0, shortLists = 0;
        foreach (var node in net.Nodes().Where(x => x.Type is 105 or 135))
            foreach (var a in node.Attribs.Where(x => x.Type == 8))
            {
                var v = net.IntArray(a);
                if (v.Length == 0 || v.Any(x => x < 0 || x >= oldN)) continue;
                int[] nv;
                if (v.Length >= oldN - 2)
                {
                    var excl = Enumerable.Range(0, oldN).Except(v).Select(x => match.OldToNew[x]).ToHashSet();
                    if (excl.Contains(-1)) throw new InvalidDataException($"bone list on node {node.Index}: an excluded template bone has no counterpart");
                    nv = Enumerable.Range(0, n).Where(b => !excl.Contains(b)).ToArray();
                    if (nv.Length != v.Length) throw new InvalidDataException($"bone list on node {node.Index}: complement size {nv.Length} != {v.Length}");
                    rigLists++;
                }
                else
                {
                    nv = v.Select(x => match.OldToNew[x] >= 0 ? match.OldToNew[x] : throw new InvalidDataException($"bone list on node {node.Index}: template bone {r.Names.ElementAtOrDefault(x)} ({x}) has no counterpart")).OrderBy(x => x).ToArray();
                    shortLists++;
                }
                net.PutIntArray(a, nv);
            }
        // mirror mapping
        var mirror = net.ReadMirror();
        if (mirror != null)
        {
            var pose = MorphemePose.From(skel);
            var world = new Q4[n];
            for (int i = 0; i < n; i++)
            {
                var q = new Q4(skel.BindRot[i][3], skel.BindRot[i][0], skel.BindRot[i][1], skel.BindRot[i][2]);
                world[i] = skel.Parents[i] < 0 ? q : MorphemeMath.Mul(world[skel.Parents[i]], q);
            }
            var twin = Enumerable.Repeat(-1, n).ToArray();
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++)
                    if (a != b && IsTwin(skel.Names[a], skel.Names[b])) { twin[a] = b; twin[b] = a; }
            var pairs = Enumerable.Range(0, n).Where(a => twin[a] > a && (skel.Names[a].StartsWith('L'))).Select(a => (A: a, B: twin[a])).ToList();
            int wantP = mirror.Pairs.Count;
            // demote surplus pairs to self-mirrored bones: Nub (end) bones first, then the last pairs
            var demoted = new List<(int, int)>();
            while (pairs.Count > wantP)
            {
                var pick = pairs.LastOrDefault(p => skel.Names[p.A].EndsWith("Nub", StringComparison.OrdinalIgnoreCase));
                if (pick == default) pick = pairs[^1];
                pairs.Remove(pick); demoted.Add(pick);
            }
            if (pairs.Count < wantP) throw new InvalidDataException($"mirror mapping: new rig has {pairs.Count} L/R pairs, the template's attrib holds {wantP} (needs a re-serialiser)");
            var inPair = pairs.SelectMany(p => new[] { p.A, p.B }).ToHashSet();
            var nm = new NmbNetwork.Mirror { Offset = mirror.Offset };
            foreach (var (a, b) in pairs) nm.Pairs.Add((a, b, MirrorQuat(world, a, b), MirrorQuat(world, b, a)));
            foreach (int i in Enumerable.Range(0, n).Where(i => !inPair.Contains(i))) nm.Self.Add((i, MirrorQuat(world, i, i)));
            net.WriteMirror(nm);
            log.Add($"mirror mapping rebuilt: {nm.Pairs.Count} L/R pairs, {nm.Self.Count} self-mirrored" + (demoted.Count > 0 ? $" (listed as self-mirrored to keep the attrib size: {string.Join(", ", demoted.Select(d => skel.Names[d.Item1] + "/" + skel.Names[d.Item2]))})" : ""));
        }
        log.Add($"network: RIG attribs {refs.RigAttr.Count} (size 0x{rigData.Length:x}), index fields {idxDone}/{refs.IndexFields.Count}, per-bone arrays {refs.PerBone.Count}, rig-sized bone lists {rigLists} (complement), short bone lists {shortLists}");
        return nmb.Rebuild(new Dictionary<int, byte[]> { [ri] = rigData, [mi] = mapData, [ni] = netBytes });
    }

    /// <summary>Name-based remap for a bridged DS1 skeleton (ds1_rig_into_nmb.py): DS2 "L_Thigh" == DS1 "L Thigh";
    /// DS2 Root (character root) → DS1 Master; DS2 Master (trajectory) → Trajectory. Returns the remap and the
    /// template bones without a match.</summary>
    public static (List<(int Old, int New)> Remap, List<string> Unmatched) RemapByName(NmbRig template, MorphemeSkeleton skel)
    {
        var nw = new Dictionary<string, int>();
        for (int i = 0; i < skel.Names.Count; i++) nw[Norm(skel.Names[i])] = i; // later duplicates win, like a dict literal
        var special = new Dictionary<string, string> { ["root"] = "master", ["master"] = "trajectory" };
        var remap = new List<(int, int)>(); var unmatched = new List<string>();
        for (int i = 0; i < template.Names.Count; i++)
        {
            string k = Norm(template.Names[i]);
            if (special.TryGetValue(k, out var sk)) k = sk;
            if (nw.TryGetValue(k, out int j)) remap.Add((i, j)); else unmatched.Add(template.Names[i]);
        }
        return (remap, unmatched);
    }
}
