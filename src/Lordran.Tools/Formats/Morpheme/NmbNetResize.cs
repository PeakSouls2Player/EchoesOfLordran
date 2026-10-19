using System.Buffers.Binary;

namespace Lordran.Tools.Formats.Morpheme;

/// <summary>
/// Moves a DS2 network onto a skeleton with a different bone count (session 8, track enemies step 1): the C# port of our own
/// research code data\research\morpheme\net_resize.py (stage 3 of the re-serialiser), with the mirror rule of net_mirror.py and
/// the joint-limit records of net_rigdep.py. No NaturalMotion code. Needed for every DS1 model whose rig (DS1 bones + the
/// three DS2 top bones) has another bone count than its DS2 template network: DS1 c2540 Hollow Warrior (49 + 3 = 52) on
/// c1030's 55-bone network, c2550 / c2900 and the bosses (NETWORK-FORMAT.md §4).
/// <para>Rewritten (NETWORK-FORMAT.md §4 table): NMB bundles 1 (rig) and 2 (every channel map); network +0x118 (bone count);
/// node 0: attrib 20 (rig data size), 6 (u8 per bone), 66 (mirror mapping), 84 (joint limits), the two bone-order tables
/// after its attribs; the network's bone-order tables at +0x130/+0x138; per node: attrib 87 (f32 per bone), 8 (bone lists),
/// 9 (rig-sized float arrays), 25 (head-look chain end/root), and the joint-index fields of 29 (two-bone hand IK), 31 (lock
/// foot), 34 (hips IK), 73 (uneven terrain).</para>
/// <para>Value mapping donor → target: bones match by role (CharacterWorldSpaceTM, Character, trajectory, root) and by name
/// (DS2 'L_Thigh' == DS1 'L Thigh'); a target bone without a match takes the values / set membership of its nearest matched
/// ancestor. Mirror mapping: rebuilt from the TARGET skeleton by DS2's own rule (V on every DS2 mirror block): pairs =
/// left/right name twins, qA = W[B]⁻¹·M(W[A]), qB = W[A]⁻¹·M(W[B]), self q = W⁻¹·M(W) (W = bind rotation in character
/// space, M = reflection through X = 0); the donor's event-track and event-value pairs are kept. Bone order A (unknown key)
/// is written in name order (Python: the same; in-game check pending, P).</para>
/// </summary>
public static class NmbNetResize
{
    /// <summary>u32 rig-bone-index fields per attrib type (nmb_rig.NET_INDEX_FIELDS + attrib 29 of node type 120).</summary>
    public static readonly Dictionary<int, int[]> IndexFields = new(NmbRigReplace.NetIndexFields) { [29] = [0x30, 0x34, 0x38] };

    static uint U32(byte[] x, int o) => BinaryPrimitives.ReadUInt32LittleEndian(x.AsSpan(o));
    static int I32(byte[] x, int o) => BinaryPrimitives.ReadInt32LittleEndian(x.AsSpan(o));
    static ulong U64(byte[] x, int o) => BinaryPrimitives.ReadUInt64LittleEndian(x.AsSpan(o));
    static ushort U16(byte[] x, int o) => BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(o));
    static float F32(byte[] x, int o) => BinaryPrimitives.ReadSingleLittleEndian(x.AsSpan(o));

    static string Norm(string s) => s.Replace(' ', '_').ToLowerInvariant();

    /// <summary>t2d[t] = donor bone for target bone t (exact role/name match or -1); near[t] = donor bone of the nearest matched
    /// ancestor (incl. t; 1 when none).</summary>
    public static (int[] T2D, int[] Near) BoneMaps(IReadOnlyList<string> dn, int dTraj, int dRoot, MorphemeSkeleton tgt)
    {
        var dIdx = new Dictionary<string, int>();
        for (int i = 0; i < dn.Count; i++) dIdx[Norm(dn[i])] = i;           // later duplicates win (Python dict)
        int n = tgt.Names.Count;
        var t2d = Enumerable.Repeat(-1, n).ToArray();
        var fixedMap = new Dictionary<int, int> { [0] = 0, [1] = 1 };
        fixedMap[tgt.Traj] = dTraj; fixedMap[tgt.Root] = dRoot;
        for (int t = 0; t < n; t++)
        {
            if (fixedMap.TryGetValue(t, out int f)) t2d[t] = f;
            else if (dIdx.TryGetValue(Norm(tgt.Names[t]), out int d)) t2d[t] = d;
        }
        var near = new int[n];
        for (int t = 0; t < n; t++)
        {
            int a = t;
            while (a >= 0 && t2d[a] < 0) a = tgt.Parents[a];
            near[t] = a >= 0 ? t2d[a] : 1;
        }
        return (t2d, near);
    }

    static byte[] Pad16(List<byte> b) { while (b.Count % 16 != 0) b.Add(0xCD); return b.ToArray(); }

    static byte[] BlobU8(byte[] hdr16, IEnumerable<byte> vals)
    {
        var v = vals.ToList();
        var b = new List<byte>(hdr16[..16]); b.AddRange(BitConverter.GetBytes((uint)v.Count)); b.AddRange([0xCD, 0xCD, 0xCD, 0xCD]); b.AddRange(BitConverter.GetBytes(0x20UL)); b.AddRange(v);
        return Pad16(b);
    }

    static byte[] BlobU32(byte[] hdr16, IReadOnlyList<int> vals)
    {
        var b = new List<byte>(hdr16[..16]); b.AddRange(BitConverter.GetBytes((uint)vals.Count)); b.AddRange([0xCD, 0xCD, 0xCD, 0xCD]); b.AddRange(BitConverter.GetBytes(0x20UL));
        foreach (int v in vals) b.AddRange(BitConverter.GetBytes((uint)v));
        return Pad16(b);
    }

    static byte[] BlobF32(byte[] hdr16, IReadOnlyList<float> vals)
    {
        var b = new List<byte>(hdr16[..16]); b.AddRange(BitConverter.GetBytes((uint)vals.Count)); b.AddRange([0xCD, 0xCD, 0xCD, 0xCD]); b.AddRange(BitConverter.GetBytes(0x20UL));
        foreach (float v in vals) b.AddRange(BitConverter.GetBytes(v));
        return Pad16(b);
    }

    static byte[] BlobFeather(byte[] old, IReadOnlyList<float> vals)
    {
        ushort nsrc = U16(old, 0x10);
        var b = new List<byte>(old[..16]); b.AddRange(BitConverter.GetBytes(nsrc)); b.AddRange(BitConverter.GetBytes((ushort)vals.Count)); b.AddRange([0xCD, 0xCD, 0xCD, 0xCD]); b.AddRange(BitConverter.GetBytes(0x20UL));
        foreach (float v in vals) b.AddRange(BitConverter.GetBytes(v));
        return Pad16(b);
    }

    // ------------------------------------------------------------------ mirror rule (net_mirror.py), quaternions (x, y, z, w)
    static (double X, double Y, double Z, double W) QMul((double X, double Y, double Z, double W) a, (double X, double Y, double Z, double W) b) =>
        (a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y, a.W * b.Y + a.Y * b.W + a.Z * b.X - a.X * b.Z,
         a.W * b.Z + a.Z * b.W + a.X * b.Y - a.Y * b.X, a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);
    static (double X, double Y, double Z, double W) QInv((double X, double Y, double Z, double W) q) => (-q.X, -q.Y, -q.Z, q.W);
    static (double X, double Y, double Z, double W) QNorm((double X, double Y, double Z, double W) q)
    {
        double l = Math.Sqrt(MorphemeMath.Sum([q.X * q.X, q.Y * q.Y, q.Z * q.Z, q.W * q.W])); if (l == 0) l = 1;   // Python sum() (Neumaier), bit-exact with net_mirror.qnorm
        return (q.X / l, q.Y / l, q.Z / l, q.W / l);
    }
    static (double X, double Y, double Z, double W) ReflectX((double X, double Y, double Z, double W) q) => (q.X, -q.Y, -q.Z, q.W);

    public static (double X, double Y, double Z, double W)[] WorldRotations(IReadOnlyList<int> parents, IReadOnlyList<double[]> bindRot)
    {
        var w = new (double, double, double, double)[parents.Count];
        for (int i = 0; i < parents.Count; i++)
        {
            var q = QNorm((bindRot[i][0], bindRot[i][1], bindRot[i][2], bindRot[i][3]));
            w[i] = parents[i] < 0 ? q : QNorm(QMul(w[parents[i]], q));
        }
        return w;
    }

    public static ((double, double, double, double) A, (double, double, double, double) B) PairQuats((double X, double Y, double Z, double W)[] w, int a, int b) =>
        (QNorm(QMul(QInv(w[b]), ReflectX(w[a]))), QNorm(QMul(QInv(w[a]), ReflectX(w[b]))));

    public static (double, double, double, double) SelfQuat((double X, double Y, double Z, double W)[] w, int i) => QNorm(QMul(QInv(w[i]), ReflectX(w[i])));

    /// <summary>net_mirror.twin_name: the left/right twin of a bone name if it exists in <paramref name="names"/>.</summary>
    public static string? TwinName(string nm, HashSet<string> names)
    {
        var c = new List<string>();
        foreach (var (l, r) in new[] { ("L ", "R "), ("L_", "R_"), ("Left", "Right"), ("R ", "L "), ("R_", "L_"), ("Right", "Left") })
            if (nm.StartsWith(l, StringComparison.Ordinal)) c.Add(r + nm[l.Length..]);
        foreach (var (l, r) in new[] { ("_L_", "_R_"), ("_R_", "_L_") })
            if (nm.Contains(l, StringComparison.Ordinal)) { int k = nm.IndexOf(l, StringComparison.Ordinal); c.Add(nm[..k] + r + nm[(k + l.Length)..]); }
        foreach (var (l, r) in new[] { ("_L", "_R"), ("_R", "_L"), (" L", " R"), (" R", " L") })
            if (nm.EndsWith(l, StringComparison.Ordinal)) c.Add(nm[..^l.Length] + r);
        if (nm.Length > 1 && (nm[0] == 'L' || nm[0] == 'R') && (char.IsUpper(nm[1]) || char.IsDigit(nm[1])))
            c.Add((nm[0] == 'L' ? "R" : "L") + nm[1..]);
        return c.FirstOrDefault(x => names.Contains(x) && x != nm);
    }

    /// <summary>net_mirror.name_pairs: (left, right) twin pairs, each bone at most once.</summary>
    public static List<(int A, int B)> NamePairs(IReadOnlyList<string> names)
    {
        var s = names.ToHashSet(); var idx = new Dictionary<string, int>();
        for (int i = 0; i < names.Count; i++) idx[names[i]] = i;
        var used = new HashSet<int>(); var o = new List<(int, int)>();
        for (int i = 0; i < names.Count; i++)
        {
            if (used.Contains(i)) continue;
            string nm = names[i];
            var tw = TwinName(nm, s);
            if (tw != null && !used.Contains(idx[tw]))
            {
                int j = idx[tw]; used.Add(i); used.Add(j);
                o.Add(string.CompareOrdinal(nm, tw) < 0 || nm.StartsWith('L') || nm.StartsWith("Left", StringComparison.Ordinal) ? (i, j) : (j, i));
            }
        }
        return o;
    }

    public sealed class MirrorBlock
    {
        public List<(int A, int B, float[] QA, float[] QB)> Pairs = [];
        public List<(int Bone, float[] Q)> Self = [];
        public List<(uint, uint)> Tracks = [], Events = [];
    }

    public static MirrorBlock ReadMirror(byte[] x, int A)
    {
        var m = new MirrorBlock();
        int P = (int)U32(x, A + 0x14); int pP = (int)U64(x, A + 0x18);
        int T = (int)U32(x, A + 0x20); int pT = (int)U64(x, A + 0x28);
        int E = (int)U32(x, A + 0x30); int pE = (int)U64(x, A + 0x38);
        int U = (int)U32(x, A + 0x40); int pUq = (int)U64(x, A + 0x48), pUi = (int)U64(x, A + 0x50);
        for (int k = 0; k < P; k++)
        {
            int o = A + pP + 48 * k;
            m.Pairs.Add(((int)U32(x, o + 32), (int)U32(x, o + 36), [F32(x, o), F32(x, o + 4), F32(x, o + 8), F32(x, o + 12)], [F32(x, o + 16), F32(x, o + 20), F32(x, o + 24), F32(x, o + 28)]));
        }
        for (int k = 0; k < T; k++) m.Tracks.Add((U32(x, A + pT + 8 * k), U32(x, A + pT + 8 * k + 4)));
        for (int k = 0; k < E; k++) m.Events.Add((U32(x, A + pE + 8 * k), U32(x, A + pE + 8 * k + 4)));
        for (int k = 0; k < U; k++) m.Self.Add(((int)U32(x, A + pUi + 4 * k), [F32(x, A + pUq + 16 * k), F32(x, A + pUq + 16 * k + 4), F32(x, A + pUq + 16 * k + 8), F32(x, A + pUq + 16 * k + 12)]));
        return m;
    }

    static float[] F4((double X, double Y, double Z, double W) q) => [(float)q.X, (float)q.Y, (float)q.Z, (float)q.W];

    /// <summary>The mirror block of a skeleton by DS2's rule (net_resize.mirror_layout).</summary>
    public static MirrorBlock MirrorLayout(MorphemeSkeleton tgt)
    {
        var w = WorldRotations(tgt.Parents, tgt.BindRot);
        var m = new MirrorBlock();
        foreach (var (a, b) in NamePairs(tgt.Names)) { var (qa, qb) = PairQuats(w, a, b); m.Pairs.Add((a, b, F4(qa), F4(qb))); }
        var used = m.Pairs.SelectMany(p => new[] { p.A, p.B }).ToHashSet();
        for (int i = 0; i < tgt.Names.Count; i++) if (!used.Contains(i)) m.Self.Add((i, F4(SelfQuat(w, i))));
        return m;
    }

    /// <summary>Attrib 66 for the target skeleton (net_resize.write_mirror; layout net_mirror.py).</summary>
    public static byte[] WriteMirror(byte[] oldBlob, MorphemeSkeleton tgt, MirrorBlock donor)
    {
        var lay = MirrorLayout(tgt);
        var tracks = donor.Tracks; var events = donor.Events;
        int P = lay.Pairs.Count, U = lay.Self.Count, T = tracks.Count, E = events.Count;
        var o = new List<byte>(oldBlob[..16]);
        void U32w(uint v) => o.AddRange(BitConverter.GetBytes(v));
        void Z(int c) { for (int i = 0; i < c; i++) o.Add(0); }
        void Cd(int c) { for (int i = 0; i < c; i++) o.Add(0xCD); }
        U32w(0); U32w((uint)P); Z(8);
        U32w((uint)T); Cd(4); Z(8);
        U32w((uint)E); Cd(4); Z(8);
        U32w((uint)U); Cd(4); Z(16); Cd(8);
        if (o.Count != 0x60) throw new InvalidOperationException("mirror header size");
        int pP = o.Count;
        foreach (var (a, b, qa, qb) in lay.Pairs)
        {
            foreach (float f in qa) o.AddRange(BitConverter.GetBytes(f));
            foreach (float f in qb) o.AddRange(BitConverter.GetBytes(f));
            U32w((uint)a); U32w((uint)b); Z(8);
        }
        int pUq = o.Count;
        foreach (var (_, q) in lay.Self) foreach (float f in q) o.AddRange(BitConverter.GetBytes(f));
        int pUi = o.Count;
        foreach (var (i, _) in lay.Self) U32w((uint)i);
        while (o.Count % 8 != 0) o.Add(0xCD);
        int pE = o.Count;
        foreach (var (a, b) in events) { U32w(a); U32w(b); }
        int pT = o.Count;
        foreach (var (a, b) in tracks) { U32w(a); U32w(b); }
        while (o.Count % 16 != 0) o.Add(0xCD);
        var r = o.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(0x18), (ulong)pP);
        BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(0x28), (ulong)(T > 0 ? pT : 0));
        BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(0x38), (ulong)(E > 0 ? pE : 0));
        BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(0x48), (ulong)pUq);
        BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(0x50), (ulong)pUi);
        return r;
    }

    // ------------------------------------------------------------------ attrib 84 joint limits (net_rigdep.py)
    static (int N, int K, long P1, long P2, long[] Ptr, Dictionary<int, byte[]> Recs) ReadA84(byte[] x, int o)
    {
        int n = (int)U32(x, o + 0x10), k = (int)U32(x, o + 0x14);
        long p1 = (long)U64(x, o + 0x18), p2 = (long)U64(x, o + 0x20);
        var ptr = new long[n];
        for (int b = 0; b < n; b++) ptr[b] = (long)U64(x, o + (int)p1 + 8 * b);
        var recs = new Dictionary<int, byte[]>();
        for (int b = 0; b < n; b++) if (ptr[b] != 0) recs[b] = x[(o + (int)ptr[b])..(o + (int)ptr[b] + 80)];
        return (n, k, p1, p2, ptr, recs);
    }

    static byte[] WriteA84(byte[] hdr16, int n, Dictionary<int, byte[]> recs, List<int> order)
    {
        long p1 = 0x28, p2 = (p1 + 8 * n + 15) / 16 * 16;
        var o = new List<byte>(hdr16[..16]);
        o.AddRange(BitConverter.GetBytes((uint)n)); o.AddRange(BitConverter.GetBytes((uint)order.Count)); o.AddRange(BitConverter.GetBytes((ulong)p1)); o.AddRange(BitConverter.GetBytes((ulong)p2));
        var ptr = new long[n];
        for (int j = 0; j < order.Count; j++) ptr[order[j]] = p2 + 80 * j;
        foreach (long v in ptr) o.AddRange(BitConverter.GetBytes((ulong)v));
        while (o.Count < p2) o.Add(0xCD);
        foreach (int b in order) o.AddRange(recs[b]);
        while (o.Count % 16 != 0) o.Add(0xCD);
        return o.ToArray();
    }

    static List<int>? PathToRoot(IReadOnlyList<int> parents, int end, int root)
    {
        var chain = new List<int> { end };
        while (chain[^1] != root && parents[chain[^1]] >= 0) chain.Add(parents[chain[^1]]);
        return chain[^1] == root ? chain : null;
    }

    public sealed class Report
    {
        public Dictionary<string, int> Counts = [];
        public List<string> UnmatchedDonorJoints = [];
        public void Add(string k) => Counts[k] = Counts.GetValueOrDefault(k) + 1;
        public override string ToString() => string.Join(", ", Counts.Select(kv => $"{kv.Key}: {kv.Value}"));
    }

    /// <summary>net_resize.resize: the donor NMB moved onto skeleton <paramref name="skel"/>. Returns the new NMB bytes.</summary>
    public static byte[] Resize(byte[] nmbBytes, MorphemeSkeleton skel, Report report)
    {
        var nmb = NmbFile.Read(nmbBytes);
        int ri = nmb.IndexOf(1), ni = nmb.IndexOf(10);
        var x = nmb.Bundles[ni].Data;
        if (BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(0xA0)) != 1) throw new NotSupportedException("one animation set only");
        var rig = NmbRig.Read(nmb.Bundles[ri].Data);
        var dn = rig.Names; int nd = dn.Count;
        var tn = skel.Names; var tp = skel.Parents; int n = tn.Count;
        var (t2d, near) = BoneMaps(dn, rig.Traj, rig.Root, skel);
        var d2t = new Dictionary<int, int>();
        for (int t = 0; t < n; t++) if (t2d[t] >= 0 && !d2t.ContainsKey(t2d[t])) d2t[t2d[t]] = t;
        int MapIndex(int d)
        {
            if (d2t.TryGetValue(d, out int t)) return t;
            // session 8 (track enemies), deviation from net_resize.py (which uses the target root): the joint's nearest matched
            // ancestor in the donor rig, so a lock-foot chain's toe nub (L_Toe0Nub, absent from DS1 c2900) becomes the ball
            // joint L_Toe0 instead of the pelvis-height root
            for (int a = d < nd ? rig.Parents[d] : -1; a >= 0; a = rig.Parents[a])
                if (d2t.TryGetValue(a, out int ta))
                {
                    string nmA = d < nd ? dn[d] : d.ToString();
                    if (!report.UnmatchedDonorJoints.Contains(nmA)) report.UnmatchedDonorJoints.Add(nmA + " → " + tn[ta]);
                    return ta;
                }
            string nm = d < nd ? dn[d] : d.ToString();
            if (!report.UnmatchedDonorJoints.Contains(nm)) report.UnmatchedDonorJoints.Add(nm);
            return skel.Root;
        }
        // ---- rig + map bundles
        var r = NmbRig.Read(nmb.Bundles[ri].Data);
        r.Names = [.. tn]; r.Parents = [.. tp];
        r.BindPos = skel.BindPos.Select(p => new[] { (float)p[0], (float)p[1], (float)p[2], 0f }).ToList();
        r.BindRot = skel.BindRot.Select(q => new[] { (float)q[0], (float)q[1], (float)q[2], (float)q[3] }).ToList();
        r.BindUsed = Enumerable.Repeat(true, n).ToArray(); r.Traj = skel.Traj; r.Root = skel.Root;
        byte[] rigData = r.Write();
        var m = NmbChannelMap.Read(nmb.Bundles[nmb.IndexOf(2)].Data);
        m.AnimToRig = skel.AnimToRig != null ? [.. skel.AnimToRig] : [.. Enumerable.Range(1, n - 1)];
        var usedSet = m.AnimToRig.ToHashSet();
        m.Used = Enumerable.Range(0, n).Select(usedSet.Contains).ToArray();
        byte[] mapData = m.Write();

        // ---- network blocks
        var items = NmbNetEdit.BuildItems(x);
        var nodeItems = items.Where(i => i.Kind == "node").ToDictionary(i => i.Idx);
        var net = new NmbNetwork(x);
        var ends = NmbNetEdit.NodeEnds(x);
        var perNodeTypes = new HashSet<int>([87, 8, 9, 25, .. IndexFields.Keys]);
        for (int i = 1; i < net.NodeCount; i++)
        {
            var node = net.GetNode(i);
            if (!node.Attribs.Any(a => perNodeTypes.Contains(a.Type))) continue;
            var it = nodeItems[i]; int p = node.Offset;
            var model = NmbNetEdit.ReadNode(x, p, p + it.Data.Length);
            bool changed = false;
            for (int hk = 0; hk < model.Handles.Count; hk++)
            {
                var (pa, sz, al, pad) = model.Handles[hk];
                if (pa == 0) continue;
                var blob = model.Blobs[pa]; int t = U16(blob, 10);
                byte[]? nw = null;
                if (t == 87)
                {
                    int cnt = U16(blob, 0x12);
                    if (cnt == nd) { nw = BlobFeather(blob, Enumerable.Range(0, n).Select(k => F32(blob, 0x20 + 4 * near[k])).ToList()); report.Add("feather alphas"); }
                }
                else if (t == 8)
                {
                    int cnt = (int)U32(blob, 0x10);
                    var vals = Enumerable.Range(0, cnt).Select(k => (int)U32(blob, 0x20 + 4 * k)).ToHashSet();
                    if (vals.All(v => v < nd))
                    {
                        var newl = vals.Count > 0 ? Enumerable.Range(0, n).Where(k => vals.Contains(near[k])).ToList() : [];
                        nw = BlobU32(blob, newl); report.Add("bone lists");
                    }
                }
                else if (t == 9)
                {
                    int cnt = (int)U32(blob, 0x10);
                    if (cnt == nd) { nw = BlobF32(blob, Enumerable.Range(0, n).Select(k => F32(blob, 0x20 + 4 * near[k])).ToList()); report.Add("per-bone floats"); }
                }
                else if (t == 25)
                {
                    var b = (byte[])blob.Clone();
                    int end = (int)U32(b, 0x20), root = (int)U32(b, 0x24), nj = (int)U32(b, 0x28);
                    int te = MapIndex(end), tr = MapIndex(root);
                    var ch = PathToRoot(tp, te, tr);
                    BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x20), (uint)te); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x24), (uint)tr);
                    nw = b;
                    report.Add(ch != null && ch.Count == nj ? "head-look chains" : $"head-look chains (joint count differs: {(ch == null ? "None" : ch.Count.ToString())} vs {nj})");
                }
                else if (IndexFields.TryGetValue(t, out var fields))
                {
                    var b = (byte[])blob.Clone();
                    foreach (int f in fields)
                    {
                        int v = I32(b, f);
                        if (v >= 0 && v < nd) BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(f), (uint)MapIndex(v));
                    }
                    nw = b; report.Add($"IK joints ({t})");
                }
                if (nw != null && !nw.AsSpan().SequenceEqual(blob))
                {
                    model.Blobs[pa] = nw;
                    model.Handles[hk] = (pa, t is 87 or 8 or 9 ? nw.Length : sz, al, pad);
                    changed = true;
                }
            }
            if (changed) it.Data = NmbNetEdit.WriteNode(model);
        }
        // ---- node 0: header +0x118, attribs 20/6/66/84, then the two bone-order tables
        var hdr = x[..0x140];
        BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(0x118), (uint)n);
        long pCh0 = (long)U64(x, 0x20); int nch0 = U16(x, 0x0A);
        long pAH0 = (long)U64(x, 0x30); int nah0 = U16(x, 0x10);
        var handles = Enumerable.Range(0, nah0).Select(k => ((long)U64(x, (int)pAH0 + 0x18 * k), (long)U64(x, (int)pAH0 + 0x18 * k + 8), U32(x, (int)pAH0 + 0x18 * k + 16), U32(x, (int)pAH0 + 0x18 * k + 20))).ToList();
        var byOff = handles.Where(h => h.Item1 != 0).OrderBy(h => h.Item1).ToList();
        var n0node = net.GetNode(0);
        var dm = ReadMirror(x, n0node.Attribs.First(a => a.Type == 66).Offset);
        var blobs = new Dictionary<long, byte[]>();
        foreach (var (pa, sz, al, pad) in handles)
        {
            if (pa == 0) continue;
            int t = U16(x, (int)pa + 10);
            var old = x[(int)pa..(int)(pa + sz)];
            if (t == 20) { var b = (byte[])old.Clone(); BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(0x18), (ulong)rigData.Length); blobs[pa] = b; }
            else if (t == 6) blobs[pa] = BlobU8(old, Enumerable.Range(0, n).Select(k => x[(int)pa + 0x20 + near[k]]));
            else if (t == 66)
            {
                blobs[pa] = WriteMirror(old, skel, dm);
                var lay = MirrorLayout(skel);
                report.Add($"mirror pairs {lay.Pairs.Count} self {lay.Self.Count} (bind-pose formula)");
            }
            else if (t == 84)
            {
                var (n84, k84, p1, p2, ptr, recs) = ReadA84(x, (int)pa);
                var order = Enumerable.Range(0, n84).Where(b => ptr[b] != 0).OrderBy(b => ptr[b]).Where(d => d2t.ContainsKey(d)).ToList();
                var newrecs = order.ToDictionary(d => d2t[d], d => recs[d]);
                blobs[pa] = WriteA84(old, n, newrecs, order.Select(d => d2t[d]).ToList());
                report.Add($"joint-limit records (84): {newrecs.Count} of {k84} kept on matched bones");
            }
            else blobs[pa] = old;
        }
        var o = new List<byte>(hdr);
        for (int k = 0; k < nch0; k++) { ushort c = U16(x, (int)pCh0 + 2 * k); o.Add((byte)c); o.Add((byte)(c >> 8)); }
        while (o.Count % 4 != 0) o.Add(0xCD);
        int newAH = o.Count; o.AddRange(new byte[0x18 * nah0]);
        var newpos = new Dictionary<long, int>();
        foreach (var (pa, _, _, _) in byOff) { while (o.Count % 16 != 0) o.Add(0xCD); newpos[pa] = o.Count; o.AddRange(blobs[pa]); }
        var byName = Enumerable.Range(0, n).OrderBy(k => tn[k], StringComparer.Ordinal).ToList(); // Python: sorted by name bytes (ASCII)
        for (int tab = 0; tab < 2; tab++)
        {
            while (o.Count % 4 != 0) o.Add(0xCD);
            o.AddRange(BitConverter.GetBytes(8UL)); foreach (int v in byName) o.AddRange(BitConverter.GetBytes((uint)v));
        }
        var n0data = o.ToArray();
        for (int k = 0; k < nah0; k++)
        {
            var (pa, sz, al, pad) = handles[k];
            int h = newAH + 0x18 * k;
            BinaryPrimitives.WriteUInt64LittleEndian(n0data.AsSpan(h), (ulong)(pa != 0 ? newpos[pa] : 0));
            BinaryPrimitives.WriteUInt64LittleEndian(n0data.AsSpan(h + 8), (ulong)(pa != 0 ? blobs[pa].Length : sz));
            BinaryPrimitives.WriteUInt32LittleEndian(n0data.AsSpan(h + 16), al);
            BinaryPrimitives.WriteUInt32LittleEndian(n0data.AsSpan(h + 20), pad);
        }
        BinaryPrimitives.WriteUInt64LittleEndian(n0data.AsSpan(0x20), 0x140);
        BinaryPrimitives.WriteUInt64LittleEndian(n0data.AsSpan(0x30), (ulong)newAH);
        items.First(i => i.Kind == "node0").Data = n0data;
        foreach (var k in new[] { "boneorderA", "boneorderName" })
        {
            var it = items.First(i => i.Kind == k);
            var d = new List<byte>(BitConverter.GetBytes(0UL)); foreach (int v in byName) d.AddRange(BitConverter.GetBytes((uint)v));
            while (d.Count % 4 != 0) d.Add(0xCD);
            it.Data = d.ToArray();
            it.Ptrs = [(0, it, 8)];
        }
        byte[] newNet = NmbNetEdit.Emit(items);
        var repl = new Dictionary<int, byte[]> { [ri] = rigData, [ni] = newNet };
        int maps = 0;
        for (int k = 0; k < nmb.Bundles.Count; k++) if (nmb.Bundles[k].Type == 2) { repl[k] = mapData; maps++; }
        report.Add($"channel maps rewritten: {maps}");
        return nmb.Rebuild(repl);
    }

    /// <summary>net_resize.check_rig_consistency: every per-bone array has the rig's length, every bone index is in range,
    /// attrib 84's records resolve, the mirror block covers every bone once and follows DS2's rule for THIS rig.</summary>
    public static List<string> CheckRigConsistency(byte[] nmbBytes)
    {
        var bad = new List<string>();
        var nmb = NmbFile.Read(nmbBytes);
        var x = nmb.Bundles[nmb.IndexOf(10)].Data;
        var rig = NmbRig.Read(nmb.Bundles[nmb.IndexOf(1)].Data);
        int n = rig.Names.Count;
        var net = new NmbNetwork(x);
        if (net.U32(0x118) != n) bad.Add("+0x118");
        int rigLen = nmb.Bundles[nmb.IndexOf(1)].Data.Length;
        foreach (var node in net.Nodes())
            foreach (var a in node.Attribs)
            {
                int o = a.Offset;
                if (a.Type == 87 && U16(x, o + 0x12) != n) bad.Add($"node {node.Index} attrib 87");
                if (a.Type == 6 && U32(x, o + 0x10) != n) bad.Add($"node {node.Index} attrib 6");
                if (a.Type == 84)
                {
                    var (n84, k84, p1, p2, ptr, _) = ReadA84(x, o);
                    var refs = ptr.Where(q => q != 0).Select(q => (q - p2) / 80).Order().ToList();
                    if (n84 != n || a.Size != p2 + 80 * k84 || !refs.SequenceEqual(Enumerable.Range(0, k84).Select(v => (long)v))) bad.Add($"node {node.Index} attrib 84");
                }
                if (a.Type == 8)
                {
                    int cnt = (int)U32(x, o + 0x10); long p = (long)U64(x, o + 0x18);
                    if (Enumerable.Range(0, cnt).Any(k => U32(x, o + (int)p + 4 * k) >= n)) bad.Add($"node {node.Index} attrib 8 index");
                }
                if (IndexFields.TryGetValue(a.Type, out var f) && f.Any(ff => I32(x, o + ff) >= n)) bad.Add($"node {node.Index} attrib {a.Type} index");
                if (a.Type == 20 && U64(x, o + 0x18) != (ulong)rigLen) bad.Add("rig size");
            }
        var mi = net.GetNode(0).Attribs.Where(a => a.Type == 66).ToList();
        var skel = new MorphemeSkeleton
        {
            Names = rig.Names, Parents = rig.Parents, BindPos = rig.BindPos.Select(p => new double[] { p[0], p[1], p[2] }).ToList(),
            BindRot = rig.BindRot.Select(q => new double[] { q[0], q[1], q[2], q[3] }).ToList(),
        };
        var w = WorldRotations(skel.Parents, skel.BindRot);
        var want = NamePairs(rig.Names).Select(p => (Math.Min(p.A, p.B), Math.Max(p.A, p.B))).ToHashSet();
        static bool QSame(float[] a, (double X, double Y, double Z, double W) b) => Math.Abs(a[0] * b.X + a[1] * b.Y + a[2] * b.Z + a[3] * b.W) > 1 - 2e-3;
        foreach (var at in mi)
        {
            var m = ReadMirror(x, at.Offset);
            var bones = m.Pairs.SelectMany(p => new[] { p.A, p.B }).Concat(m.Self.Select(s => s.Bone)).Order().ToList();
            if (!bones.SequenceEqual(Enumerable.Range(0, n))) bad.Add("mirror coverage");
            if (!m.Pairs.Select(p => (Math.Min(p.A, p.B), Math.Max(p.A, p.B))).ToHashSet().SetEquals(want)) bad.Add("mirror pairs != name twins");
            if (!m.Pairs.All(p => { var (qa, qb) = PairQuats(w, p.A, p.B); return QSame(p.QA, qa) && QSame(p.QB, qb); })) bad.Add("mirror pair quaternions != bind-pose rule");
            if (!m.Self.All(s => QSame(s.Q, SelfQuat(w, s.Bone)))) bad.Add("mirror self quaternions != bind-pose rule");
        }
        return bad;
    }
}
