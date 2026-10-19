using System.Buffers.Binary;

namespace Lordran.Tools.Formats.Morpheme.Boss;

/// <summary>
/// Network re-serialiser stage 3 (session 8, track boss): move a DS2 Morpheme network onto a skeleton with another bone
/// count. C# port of data\research\morpheme\net_resize.py (with net_mirror.py's bind-pose rule and net_rigdep.py's attrib 84
/// reader/writer); own code, NETWORK-FORMAT.md §4/§5.
/// <para>Everything that depends on the rig is rewritten: NMB bundles 1 (rig) and 2 (every channel map); network +0x118
/// (bone count); node 0: attrib 20 (rig data size), attrib 6 (u8 per bone), attrib 66 (mirror mapping), attrib 84 (per-joint
/// limit records, 6 DS2 networks) and the two bone-order tables after its attribs; the network's bone-order tables at
/// +0x130/+0x138; per node: attrib 87 (f32 per bone), 8 (bone lists), 9 (rig-sized float arrays), 25 (head-look chain: end
/// and root joint), 29 (two-bone hand IK end joint, node type 120), 31 (lock-foot joints), 34 (hips-IK joints), 73
/// (uneven-terrain limb joints). Completeness (Python net_rigdep --count-fields over all 173 DS2 networks): a field equal to
/// the rig bone count exists only in attribs 6, 84 and 87.</para>
/// <para><b>Value mapping donor → target</b>: bones match by role (CharacterWorldSpaceTM, Character, trajectory, root) and by
/// name (DS1 "L Thigh" == DS2 "L_Thigh"); a target bone without a match takes the values / set membership of its nearest
/// matched ancestor (wings, tails and extra fingers follow the body part they hang from). Attrib 84 joint limits are
/// joint-specific: only exactly matched bones keep the donor's record (P).</para>
/// <para><b>Mirror mapping</b>: rebuilt for the TARGET skeleton (<see cref="MirrorFor"/>): bones the donor also has mirror
/// as in the donor, bones it lacks pair by left/right name twins; quaternions by DS2's own rule (V on every DS2 mirror block,
/// Python net_mirror --formula: 4,471/4,471 pairs, 2,984/2,984 self bones): qA = W[B]⁻¹·M(W[A]), qB = W[A]⁻¹·M(W[B]), self
/// q = W⁻¹·M(W), W = bind rotation in character space, M(x,y,z,w) = (x,−y,−z,w); the event-track and event-value pairs (DS2
/// conventions) are kept from the donor. Byte-exactness (session 8, C# port): where the donor's stored quaternion equals
/// the rule's value within 2e-3 (qsame), its stored floats are kept, and pairs keep the donor's order; so resizing a network
/// to its own rig re-emits it byte-identically (<c>nmb-resize-selftest</c>).</para>
/// <para><b>Bone order A</b> (key unknown, NETWORK-FORMAT.md §1.1): when every donor bone has a target, the donor's order
/// mapped through the bone match (identity for a self-resize); otherwise name order (as net_resize.py; in-game check
/// pending).</para>
/// </summary>
public static class NmbNetResize
{
    /// <summary>u32 rig-bone-index fields per attrib type (net_resize INDEX_FIELDS = nmb_rig.NET_INDEX_FIELDS + attrib 29).</summary>
    public static readonly Dictionary<int, int[]> IndexFields = new(NmbRigReplace.NetIndexFields) { [29] = [0x30, 0x34, 0x38] };

    static uint U(byte[] x, int o) => BinaryPrimitives.ReadUInt32LittleEndian(x.AsSpan(o));
    static int I(byte[] x, int o) => BinaryPrimitives.ReadInt32LittleEndian(x.AsSpan(o));
    static ulong Q(byte[] x, int o) => BinaryPrimitives.ReadUInt64LittleEndian(x.AsSpan(o));
    static ushort H(byte[] x, int o) => BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(o));
    static float F(byte[] x, int o) => BinaryPrimitives.ReadSingleLittleEndian(x.AsSpan(o));
    static string Norm(string s) => s.Replace(' ', '_').ToLowerInvariant();

    // ------------------------------------------------------------------ quaternions (x, y, z, w), net_mirror.py
    public static double[] QMul(double[] a, double[] b) =>
    [
        a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1],
        a[3] * b[1] + a[1] * b[3] + a[2] * b[0] - a[0] * b[2],
        a[3] * b[2] + a[2] * b[3] + a[0] * b[1] - a[1] * b[0],
        a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2],
    ];
    static double[] QInv(double[] q) => [-q[0], -q[1], -q[2], q[3]];
    static double[] QNorm(double[] q) { double l = Math.Sqrt(q.Sum(c => c * c)); if (l == 0) l = 1; return [.. q.Select(c => c / l)]; }
    static double[] ReflectX(double[] q) => [q[0], -q[1], -q[2], q[3]];

    public static double[][] WorldRotations(IReadOnlyList<int> parents, IReadOnlyList<double[]> bindRot)
    {
        var W = new double[parents.Count][];
        for (int i = 0; i < parents.Count; i++)
        {
            var q = QNorm(bindRot[i][..4]);
            W[i] = parents[i] < 0 ? q : QNorm(QMul(W[parents[i]], q));
        }
        return W;
    }

    public static (double[] QA, double[] QB) PairQuats(double[][] W, int a, int b) =>
        (QNorm(QMul(QInv(W[b]), ReflectX(W[a]))), QNorm(QMul(QInv(W[a]), ReflectX(W[b]))));
    public static double[] SelfQuat(double[][] W, int i) => QNorm(QMul(QInv(W[i]), ReflectX(W[i])));
    public static bool QSame(IReadOnlyList<double> a, IReadOnlyList<double> b, double tol = 2e-3) =>
        Math.Abs(a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3]) > 1 - tol;

    /// <summary>The left/right twin of a bone name among <paramref name="names"/> (net_mirror.twin_name): "L "/"R ",
    /// "L_"/"R_", "Left"/"Right" prefixes, "_L_"/"_R_" infix, "_L"/"_R"/" L"/" R" suffix, bare L/R before an upper-case letter
    /// or digit (LThighTwist / RThighTwist).</summary>
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

    /// <summary>Name-twin pairs (left bone first), net_mirror.name_pairs.</summary>
    public static List<(int A, int B)> NamePairs(IReadOnlyList<string> names)
    {
        var s = names.ToHashSet(); var idx = new Dictionary<string, int>();
        for (int i = 0; i < names.Count; i++) idx[names[i]] = i;   // later duplicates win, like the Python dict
        var used = new HashSet<int>(); var o = new List<(int, int)>();
        for (int i = 0; i < names.Count; i++)
        {
            if (used.Contains(i)) continue;
            var tw = TwinName(names[i], s);
            if (tw == null || used.Contains(idx[tw])) continue;
            int j = idx[tw]; used.Add(i); used.Add(j);
            o.Add(string.CompareOrdinal(names[i], tw) < 0 || names[i].StartsWith('L') || names[i].StartsWith("Left") ? (i, j) : (j, i));
        }
        return o;
    }

    // ------------------------------------------------------------------ mirror block (attrib 66)
    public sealed class MirrorBlock
    {
        public List<(int A, int B, float[] QA, float[] QB)> Pairs = [];
        public List<(int Bone, float[] Q)> Self = [];
        public List<(uint A, uint B)> Tracks = [], Events = [];
    }

    public static MirrorBlock ReadMirror(byte[] x, int A)
    {
        var m = new MirrorBlock();
        int P = (int)U(x, A + 0x14); int pP = (int)Q(x, A + 0x18);
        int T = (int)U(x, A + 0x20); int pT = (int)Q(x, A + 0x28);
        int E = (int)U(x, A + 0x30); int pE = (int)Q(x, A + 0x38);
        int Un = (int)U(x, A + 0x40); int pUq = (int)Q(x, A + 0x48), pUi = (int)Q(x, A + 0x50);
        float[] F4(int o) => [F(x, o), F(x, o + 4), F(x, o + 8), F(x, o + 12)];
        for (int k = 0; k < P; k++) { int o = A + pP + 48 * k; m.Pairs.Add(((int)U(x, o + 32), (int)U(x, o + 36), F4(o), F4(o + 16))); }
        for (int k = 0; k < T; k++) m.Tracks.Add((U(x, A + pT + 8 * k), U(x, A + pT + 8 * k + 4)));
        for (int k = 0; k < E; k++) m.Events.Add((U(x, A + pE + 8 * k), U(x, A + pE + 8 * k + 4)));
        for (int k = 0; k < Un; k++) m.Self.Add(((int)U(x, A + pUi + 4 * k), F4(A + pUq + 16 * k)));
        return m;
    }

    /// <summary>Serialise attrib 66 with the DS2 layout: 0x60-byte head, pairs (48 B), self quaternions, self bones, event
    /// pairs, track pairs, 0xCD to 16. (net_resize.write_mirror padded the event pairs to 8; DS2 does not: c1030's event
    /// pairs start at +0x544. The C# self-test re-emits every DS2 mirror block byte-identically.)</summary>
    public static byte[] WriteMirror(byte[] header16, MirrorBlock m)
    {
        int P = m.Pairs.Count, Un = m.Self.Count, T = m.Tracks.Count, E = m.Events.Count;
        var o = new NmbFile.Buf();
        o.Raw(header16);
        o.U32(0); o.U32((uint)P); o.U64(0); o.U32((uint)T); o.Fill(0xCD, 4); o.U64(0);
        o.U32((uint)E); o.Fill(0xCD, 4); o.U64(0); o.U32((uint)Un); o.Fill(0xCD, 4); o.U64(0); o.U64(0); o.Fill(0xCD, 8);
        if (o.Len != 0x60) throw new InvalidOperationException("mirror head size");
        int pP = o.Len;
        foreach (var (a, b, qa, qb) in m.Pairs) { foreach (var f in qa) o.F32(f); foreach (var f in qb) o.F32(f); o.U32((uint)a); o.U32((uint)b); o.U64(0); }
        int pUq = o.Len;
        foreach (var (_, q) in m.Self) foreach (var f in q) o.F32(f);
        int pUi = o.Len;
        foreach (var (i, _) in m.Self) o.U32((uint)i);
        int pE = o.Len;                                   // directly after the self-bone indices (c1030: 0x544, not 8-aligned)
        foreach (var (a, b) in m.Events) { o.U32(a); o.U32(b); }
        int pT = o.Len;
        foreach (var (a, b) in m.Tracks) { o.U32(a); o.U32(b); }
        o.Pad(16, 0xCD);
        var arr = o.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(arr.AsSpan(0x18), (ulong)pP);
        BinaryPrimitives.WriteUInt64LittleEndian(arr.AsSpan(0x28), (ulong)pT);    // set even when T = 0 (c2020, c3040, c7510)
        BinaryPrimitives.WriteUInt64LittleEndian(arr.AsSpan(0x38), (ulong)pE);    // set even when E = 0 (c7510)
        BinaryPrimitives.WriteUInt64LittleEndian(arr.AsSpan(0x48), (ulong)pUq);
        BinaryPrimitives.WriteUInt64LittleEndian(arr.AsSpan(0x50), (ulong)pUi);
        return arr;
    }

    static float[] F4(double[] q) => [(float)q[0], (float)q[1], (float)q[2], (float)q[3]];

    /// <summary>
    /// Mirror block for the target skeleton. Pairing: a target bone the donor also has (same name) mirrors as it does in the
    /// donor (pair with the same twin, or self-mirrored), so DS2's own pair set is kept where it applies (13 of 157 DS2
    /// blocks use artist pairs the name rule cannot derive, or keep name twins self-mirrored, e.g. c1290 L_B_Root); bones the
    /// donor lacks (DS1-only wings, tails, helper bones) pair by the name rule (<see cref="NamePairs"/>), else are
    /// self-mirrored. Quaternions: DS2's bind-pose rule, with the donor's stored floats kept where they equal the rule's value
    /// (qsame 2e-3); donor pairs/self bones keep the donor's order, new ones follow.
    /// </summary>
    public static (MirrorBlock Block, List<string> Asym) MirrorFor(MorphemeSkeleton tgt, MirrorBlock donor, IReadOnlyList<string> donorNames)
    {
        var tn = tgt.Names; int n = tn.Count;
        var W = WorldRotations(tgt.Parents, tgt.BindRot);
        var ti = new Dictionary<string, int>();
        for (int i = 0; i < n; i++) ti.TryAdd(tn[i], i);
        var donorHas = donorNames.ToHashSet();
        var m = new MirrorBlock { Tracks = [.. donor.Tracks], Events = [.. donor.Events] };
        var used = new HashSet<int>();
        double[] D(float[] q) => [q[0], q[1], q[2], q[3]];
        // 1. the donor's pairs whose two bones the target has
        foreach (var (a, b, qa, qb) in donor.Pairs)
        {
            if (a >= donorNames.Count || b >= donorNames.Count || !ti.TryGetValue(donorNames[a], out int ta) || !ti.TryGetValue(donorNames[b], out int tb)) continue;
            if (used.Contains(ta) || used.Contains(tb)) continue;
            var (ra, rb) = PairQuats(W, ta, tb);
            m.Pairs.Add((ta, tb, QSame(ra, D(qa)) ? qa : F4(ra), QSame(rb, D(qb)) ? qb : F4(rb)));
            used.Add(ta); used.Add(tb);
        }
        // 2. name-rule pairs among the bones the donor does not have
        var fresh = Enumerable.Range(0, n).Where(i => !used.Contains(i) && !donorHas.Contains(tn[i])).ToList();
        var freshNames = fresh.Select(i => tn[i]).ToList();
        foreach (var (pa, pb) in NamePairs(freshNames))
        {
            int a = fresh[pa], b = fresh[pb];
            var (qa, qb) = PairQuats(W, a, b);
            m.Pairs.Add((a, b, F4(qa), F4(qb)));
            used.Add(a); used.Add(b);
        }
        // 3. self-mirrored: the donor's self bones first (its order), then the rest
        foreach (var (bone, q) in donor.Self)
        {
            if (bone >= donorNames.Count || !ti.TryGetValue(donorNames[bone], out int t) || used.Contains(t)) continue;
            var r = SelfQuat(W, t);
            m.Self.Add((t, QSame(r, D(q)) ? q : F4(r)));
            used.Add(t);
        }
        for (int i = 0; i < n; i++)
            if (used.Add(i)) m.Self.Add((i, F4(SelfQuat(W, i))));
        // left/right bind-pose symmetry report (DS2 pairs are often asymmetric: information only)
        var P = MorphemePose.From(tgt).BindWorld();
        var asym = m.Pairs.Select(p => (D: Math.Sqrt(Math.Pow(-P[p.A].X - P[p.B].X, 2) + Math.Pow(P[p.A].Y - P[p.B].Y, 2) + Math.Pow(P[p.A].Z - P[p.B].Z, 2)), p))
            .OrderByDescending(t => t.D).Take(3).Select(t => $"{tn[t.p.A]}/{tn[t.p.B]} {t.D * 1000:0.0} mm").ToList();
        return (m, asym);
    }

    // ------------------------------------------------------------------ attrib 84 (net_rigdep.read_a84 / write_a84)
    public static (int N, int K, long P1, long P2, long[] Ptr, Dictionary<int, byte[]> Recs) ReadA84(byte[] x, int o)
    {
        int n = (int)U(x, o + 0x10), k = (int)U(x, o + 0x14);
        long p1 = (long)Q(x, o + 0x18), p2 = (long)Q(x, o + 0x20);
        var ptr = Enumerable.Range(0, n).Select(b => (long)Q(x, o + (int)p1 + 8 * b)).ToArray();
        var recs = new Dictionary<int, byte[]>();
        for (int b = 0; b < n; b++) if (ptr[b] != 0) recs[b] = x[(o + (int)ptr[b])..(o + (int)ptr[b] + 80)];
        return (n, k, p1, p2, ptr, recs);
    }

    public static List<int> RecordOrder(long[] ptr) => [.. ptr.Select((q, b) => (q, b)).Where(t => t.q != 0).OrderBy(t => t.q).Select(t => t.b)];

    public static byte[] WriteA84(byte[] hdr16, int n, Dictionary<int, byte[]> recs, IReadOnlyList<int> order)
    {
        long p1 = 0x28, p2 = (p1 + 8 * n + 15) / 16 * 16;
        var o = new NmbFile.Buf();
        o.Raw(hdr16); o.U32((uint)n); o.U32((uint)order.Count); o.U64((ulong)p1); o.U64((ulong)p2);
        var ptr = new long[n];
        for (int j = 0; j < order.Count; j++) ptr[order[j]] = p2 + 80 * j;
        foreach (var v in ptr) o.U64((ulong)v);
        o.PadTo((int)p2, 0xCD);
        foreach (int b in order) o.Raw(recs[b]);
        o.Pad(16, 0xCD);
        return o.ToArray();
    }

    // ------------------------------------------------------------------ the resize
    /// <summary>t2d[t] = donor bone of target bone t (role / name match, -1 none); near[t] = donor bone of its nearest
    /// matched ancestor (incl. itself), 1 (Character) if none.</summary>
    public static (int[] T2D, int[] Near) BoneMaps(IReadOnlyList<string> donorNames, int dTraj, int dRoot, MorphemeSkeleton tgt)
    {
        var dn = new Dictionary<string, int>();
        for (int i = 0; i < donorNames.Count; i++) dn[Norm(donorNames[i])] = i;    // later duplicates win (Python dict)
        int n = tgt.Names.Count;
        var t2d = Enumerable.Repeat(-1, n).ToArray();
        var fix = new Dictionary<int, int> { [0] = 0, [1] = 1, [tgt.Traj] = dTraj, [tgt.Root] = dRoot };
        for (int t = 0; t < n; t++)
        {
            if (fix.TryGetValue(t, out int f)) t2d[t] = f;
            else if (dn.TryGetValue(Norm(tgt.Names[t]), out int d)) t2d[t] = d;
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

    static byte[] BlobArr(byte[] hdr16, int elem, IReadOnlyList<byte[]> vals)
    {
        var o = new NmbFile.Buf();
        o.Raw(hdr16); o.U32((uint)vals.Count); o.Fill(0xCD, 4); o.U64(0x20);
        foreach (var v in vals) o.Raw(v);
        o.Pad(16, 0xCD);
        return o.ToArray();
    }

    static byte[] BlobFeather(byte[] old, IReadOnlyList<byte[]> vals)
    {
        var o = new NmbFile.Buf();
        o.Raw(old[..16]); o.U16(H(old, 0x10)); o.U16((ushort)vals.Count); o.Fill(0xCD, 4); o.U64(0x20);
        foreach (var v in vals) o.Raw(v);
        while (o.Len % 16 != 0) o.F32(1f);                // spare alphas = 1.0 (all 2,182 DS2 feather arrays), not 0xCD
        return o.ToArray();
    }

    static List<int> PathToRoot(IReadOnlyList<int> parents, int end, int root)
    {
        var ch = new List<int> { end };
        while (ch[^1] != root && ch[^1] >= 0 && parents[ch[^1]] >= 0) ch.Add(parents[ch[^1]]);
        return ch[^1] == root ? ch : [];
    }

    public sealed class Result
    {
        public byte[] Nmb = [];
        public Dictionary<string, int> Report = [];
        public List<string> Unmatched = [];
        public void Add(string k) => Report[k] = Report.GetValueOrDefault(k) + 1;
    }

    /// <summary>Limb-name key (rule IK2): lower case without '_' / ' ', "finger" = "digit" (DS2 L_Leg_Finger11 = DS1 LLegDigit11).</summary>
    public static string LimbKey(string s) => s.Replace("_", "").Replace(" ", "").ToLowerInvariant().Replace("finger", "digit");

    /// <summary>Rule IK2: the target bone for a donor limb joint without a namesake - same limb key, else the shallowest target bone
    /// whose key is the donor's key plus digits (DS2 "L_Leg_1" = the first segment of DS1's split thigh "LLeg11" / "LLeg12");
    /// -1 when none.</summary>
    public static int LimbMatch(string donor, IReadOnlyList<string> tgt, IReadOnlyList<int> parents, bool lettered = false)
    {
        string k = LimbKey(donor);
        if (k.Length < 3) return -1;
        for (int t = 0; t < tgt.Count; t++) if (LimbKey(tgt[t]) == k) return t;
        int best = -1, bestDepth = int.MaxValue;
        for (int t = 0; t < tgt.Count; t++)
        {
            string kt = LimbKey(tgt[t]);
            if (!kt.StartsWith(k) || kt.Length == k.Length || !kt[k.Length..].All(char.IsDigit) || !char.IsDigit(k[^1])) continue;
            int depth = 0; for (int a = parents[t]; a >= 0; a = parents[a]) depth++;
            if (depth < bestDepth) { best = t; bestDepth = depth; }
        }
        return best >= 0 || lettered ? best : LetteredLimbMatch(donor, tgt, parents);
    }

    /// <summary>Session 11 (dchr11), rule IK2b: a DS1 rig with several limbs of one kind, lettered (the Gaping Dragon c5260: LLeg_a_* on the
    /// chest, LLeg_b_* on the pelvis; LArm_a/b/c_*), has no rule-IK2 target for DS2's single limb (L_Leg_1 / L_Leg_2 / L_Leg_Ankle /
    /// L_Leg_Finger11), so its IK joints fell to the root (a root-collapsed leg: rule IK2's crash). Then the limb whose first bone hangs
    /// shallowest in the hierarchy (the pelvis limb: DS2's L_Leg is its dragons' hind leg) is taken, its letter inserted into the donor name
    /// (L_Leg_1 → LLeg_b_1) and rule IK2 run on that; one letter per limb kind and side. Reached only when rule IK2 found nothing in a rig
    /// with lettered limbs ("LLeg_x_…"), so every rig rule IK2 served is unchanged.</summary>
    static int LetteredLimbMatch(string donor, IReadOnlyList<string> tgt, IReadOnlyList<int> parents)
    {
        var m = System.Text.RegularExpressions.Regex.Match(donor, "^([LR])_?(Leg|Arm)_?(.+)$");
        if (!m.Success) return -1;
        string side = m.Groups[1].Value, kind = m.Groups[2].Value, rest = m.Groups[3].Value;
        var limb = new System.Text.RegularExpressions.Regex($"^{side}{kind}_([a-z])_");
        int Depth(int t) { int d = 0; for (int a = parents[t]; a >= 0; a = parents[a]) d++; return d; }
        char letter = '\0'; int best = int.MaxValue;
        for (int t = 0; t < tgt.Count; t++)
            if (limb.Match(tgt[t]) is { Success: true } lm && Depth(t) < best) { best = Depth(t); letter = lm.Groups[1].Value[0]; }
        return letter == '\0' ? -1 : LimbMatch($"{side}{kind}_{letter}_{rest}", tgt, parents, lettered: true);
    }

    /// <summary>Move the network of <paramref name="nmb"/> (one animation set) onto <paramref name="skel"/>.</summary>
    public static Result Resize(byte[] nmb, MorphemeSkeleton skel)
    {
        var res = new Result();
        var f = NmbFile.Read(nmb);
        int ri = f.IndexOf(1), ni = f.IndexOf(10);
        byte[] x = f.Bundles[ni].Data;
        if (H(x, 0xA0) != 1) throw new NotSupportedException("network with more than one animation set (player network)");
        var donorRig = NmbRig.Read(f.Bundles[ri].Data);
        var dn = donorRig.Names; int nd = dn.Count;
        var tn = skel.Names; var tp = skel.Parents; int n = tn.Count;
        var (t2d, near) = BoneMaps(dn, donorRig.Traj, donorRig.Root, skel);
        var d2t = new Dictionary<int, int>();
        for (int t = 0; t < n; t++) if (t2d[t] >= 0 && !d2t.ContainsKey(t2d[t])) d2t[t2d[t]] = t;
        var unm = new SortedSet<string>(StringComparer.Ordinal);
        int MapIndex(int d)
        {
            if (d2t.TryGetValue(d, out int t)) return t;
            unm.Add(d < nd ? dn[d] : d.ToString()); return skel.Root;
        }
        // session 11 (crash11), rule IK2: IK joint fields (lock foot 31, hips IK 34, uneven terrain 73, 29) of a donor joint with no
        // namesake take the target's limb bone by limb name (LimbMatch: DS2 L_Leg_1 / L_Leg_2 / L_Leg_Ankle / L_Leg_Finger11 =
        // DS1 LLeg11 / LLeg2 / LLegAnkle / LLegDigit11) instead of the root: a root-collapsed leg (hip = knee = ankle = Root,
        // the Hellkite c3430 / Bell Gargoyles c5350) makes the uneven-terrain parent walk pass the root (exe+0x679119).
        // Only joints the exact-name map leaves unmatched change; head look (25) and per-bone data keep the old rule.
        var limbHits = new Dictionary<int, int>();
        int MapIkIndex(int d)
        {
            if (d2t.ContainsKey(d) || d >= nd) return MapIndex(d);
            if (!limbHits.TryGetValue(d, out int t)) limbHits[d] = t = LimbMatch(dn[d], tn, tp);
            if (t < 0) return MapIndex(d);
            res.Add($"IK2 limb joint {dn[d]} → {tn[t]}");
            return t;
        }
        // ---- rig + channel map
        var r = NmbRig.Read(f.Bundles[ri].Data);
        r.Names = [.. tn]; r.Parents = [.. tp];
        r.BindPos = skel.BindPos.Select(p => new[] { (float)p[0], (float)p[1], (float)p[2], 0f }).ToList();
        r.BindRot = skel.BindRot.Select(q => new[] { (float)q[0], (float)q[1], (float)q[2], (float)q[3] }).ToList();
        r.BindUsed = Enumerable.Repeat(true, n).ToArray(); r.Traj = skel.Traj; r.Root = skel.Root;
        // self-resize keeps the donor's bind floats and used bits (identical skeleton → identical rig bundle)
        if (n == nd && tn.SequenceEqual(dn) && tp.SequenceEqual(donorRig.Parents))
        {
            bool sameBind = true;
            for (int i = 0; i < n && sameBind; i++)
                for (int c = 0; c < 3; c++) if ((float)skel.BindPos[i][c] != donorRig.BindPos[i][c] || (float)skel.BindRot[i][c] != donorRig.BindRot[i][c]) sameBind = false;
            if (sameBind) { r.BindPos = donorRig.BindPos; r.BindRot = donorRig.BindRot; r.BindUsed = donorRig.BindUsed; }
        }
        byte[] rigData = r.Write();
        var mapBundles = Enumerable.Range(0, f.Bundles.Count).Where(i => f.Bundles[i].Type == 2).ToList();
        var donorMap = NmbChannelMap.Read(f.Bundles[mapBundles[0]].Data);
        var m = new NmbChannelMap { MapType = donorMap.MapType, AnimToRig = skel.AnimToRig != null ? [.. skel.AnimToRig] : [.. Enumerable.Range(1, n - 1)] };
        var usedSet = m.AnimToRig.ToHashSet();
        m.Used = Enumerable.Range(0, n).Select(usedSet.Contains).ToArray();
        byte[] mapData = m.Write();
        // ---- network blocks: per-node attribs
        var items = NmbNetBlocks.BuildItems(x);
        var nodeItems = items.Where(it => it.Kind == "node").ToDictionary(it => it.Index);
        var net = new NmbNetwork(x);
        foreach (var node in net.Nodes().Skip(1))
        {
            var types = node.Attribs.Select(a => a.Type).ToHashSet();
            if (!types.Overlaps(new[] { 87, 8, 9, 25 }.Concat(IndexFields.Keys))) continue;
            var it = nodeItems[node.Index];
            var model = NmbNetBlocks.ReadNode(it.Data, 0, it.Data.Length);
            bool changed = false;
            for (int k = 0; k < model.Handles.Count; k++)
            {
                var (pa, sz, al, pad) = model.Handles[k];
                if (pa == 0) continue;
                var blob = model.Blobs[pa];
                int t = H(blob, 10);
                byte[]? nw = null;
                if (t == 87)
                {
                    int cnt = H(blob, 0x12);
                    if (cnt == nd) { nw = BlobFeather(blob, [.. Enumerable.Range(0, n).Select(b => blob[(0x20 + 4 * near[b])..(0x24 + 4 * near[b])])]); res.Add("feather alphas"); }
                }
                else if (t == 8)
                {
                    int cnt = (int)U(blob, 0x10);
                    var vals = Enumerable.Range(0, cnt).Select(k2 => (int)U(blob, 0x20 + 4 * k2)).ToHashSet();
                    if (vals.All(v => v < nd))
                    {
                        var nl = vals.Count > 0 ? Enumerable.Range(0, n).Where(b => vals.Contains(near[b])).ToList() : [];
                        nw = BlobArr(blob[..16], 4, [.. nl.Select(v => BitConverter.GetBytes((uint)v))]); res.Add("bone lists");
                    }
                }
                else if (t == 9)
                {
                    int cnt = (int)U(blob, 0x10);
                    if (cnt == nd) { nw = BlobArr(blob[..16], 4, [.. Enumerable.Range(0, n).Select(b => blob[(0x20 + 4 * near[b])..(0x24 + 4 * near[b])])]); res.Add("per-bone floats"); }
                }
                else if (t == 25)
                {
                    var b = (byte[])blob.Clone();
                    int end = (int)U(b, 0x20), root = (int)U(b, 0x24), nj = (int)U(b, 0x28);
                    int te = MapIndex(end), tr = MapIndex(root);
                    var ch = PathToRoot(tp, te, tr);
                    BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x20), (uint)te);
                    BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x24), (uint)tr);
                    nw = b;
                    res.Add(ch.Count == nj ? "head-look chains" : $"head-look chains (joint count differs: {(ch.Count > 0 ? ch.Count.ToString() : "None")} vs {nj})");
                }
                else if (IndexFields.TryGetValue(t, out var fields))
                {
                    var b = (byte[])blob.Clone();
                    foreach (int fo in fields)
                    {
                        int v = I(b, fo);
                        if (v >= 0 && v < nd) BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(fo), (uint)MapIkIndex(v));
                    }
                    nw = b; res.Add($"IK joints ({t})");
                }
                if (nw != null && !nw.AsSpan().SequenceEqual(blob))
                {
                    model.Blobs[pa] = nw;
                    model.Handles[k] = (pa, t is 87 or 8 or 9 ? nw.Length : sz, al, pad);
                    changed = true;
                }
            }
            if (changed) it.Data = NmbNetBlocks.WriteNode(model);
        }
        // ---- node 0: header +0x118, attribs 20/6/66/84, then the two bone-order tables
        var n0 = items.First(it => it.Kind == "node0");
        var hdr = x[..0x140];
        BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(0x118), (uint)n);
        long pCh = (long)Q(x, 0x20); int nch = H(x, 0x0A);
        long pAH = (long)Q(x, 0x30); int nah = H(x, 0x10);
        var handles = Enumerable.Range(0, nah).Select(k => ((long)Q(x, (int)pAH + 0x18 * k), (long)Q(x, (int)pAH + 0x18 * k + 8), U(x, (int)pAH + 0x18 * k + 16), U(x, (int)pAH + 0x18 * k + 20))).ToList();
        var blobs = new Dictionary<long, byte[]>();
        long attrEnd = handles.Where(h => h.Item1 != 0).Max(h => h.Item1 + h.Item2);
        // donor bone-order tables after node 0's attribs: {u64 8, u32[nd]} twice
        int bo = (int)attrEnd; while (bo % 4 != 0) bo++;
        var donorOrderA = Enumerable.Range(0, nd).Select(k => (int)U(x, bo + 8 + 4 * k)).ToList();
        foreach (var (pa, sz, al, pad) in handles)
        {
            if (pa == 0) continue;
            int t = H(x, (int)pa + 10);
            var old = x[(int)pa..(int)(pa + sz)];
            if (t == 20) { var b = (byte[])old.Clone(); BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(0x18), (ulong)rigData.Length); blobs[pa] = b; }
            else if (t == 6) { blobs[pa] = BlobArr(old[..16], 1, [.. Enumerable.Range(0, n).Select(b => new[] { x[(int)pa + 0x20 + near[b]] })]); }
            else if (t == 66)
            {
                var dm = ReadMirror(x, (int)pa);
                var (mb, asym) = MirrorFor(skel, dm, dn);
                blobs[pa] = WriteMirror(old[..16], mb);
                res.Add($"mirror pairs {mb.Pairs.Count} self {mb.Self.Count} (bind-pose rule)");
                if (asym.Count > 0) res.Add("mirror bind-pose asymmetry, worst pairs: " + string.Join(", ", asym));
            }
            else if (t == 84)
            {
                var (n84, k84, _, _, ptr, recs) = ReadA84(x, (int)pa);
                var order = RecordOrder(ptr).Where(d2t.ContainsKey).ToList();
                var newRecs = order.ToDictionary(d => d2t[d], d => recs[d]);
                blobs[pa] = WriteA84(old[..16], n, newRecs, [.. order.Select(d => d2t[d])]);
                res.Add($"joint-limit records (84): {newRecs.Count} of {k84} kept on matched bones");
            }
            else blobs[pa] = old;
        }
        var o = new NmbFile.Buf();
        o.Raw(hdr);
        for (int k = 0; k < nch; k++) o.U16(H(x, (int)pCh + 2 * k));
        o.Pad(4, 0xCD);
        int newAH = o.Len; o.Fill(0, 0x18 * nah);
        var newPos = new Dictionary<long, int>();
        foreach (var h in handles.Where(h => h.Item1 != 0).OrderBy(h => h.Item1))
        {
            o.Pad(16, 0xCD);
            newPos[h.Item1] = o.Len; o.Raw(blobs[h.Item1]);
        }
        // bone order A: the donor's mapped through the match when every donor bone has a target, else name order
        var byName = Enumerable.Range(0, n).OrderBy(k => tn[k], StringComparer.Ordinal).ToList();
        var orderA = Enumerable.Range(0, nd).All(d2t.ContainsKey) && n == nd ? donorOrderA.Select(d => d2t[d]).ToList() : byName;
        var byNameBytes = Enumerable.Range(0, n).OrderBy(k => System.Text.Encoding.ASCII.GetBytes(tn[k]), new ByteCmp()).ToList();
        foreach (var tab in new[] { orderA, byNameBytes })
        {
            o.Pad(4, 0xCD);
            o.U64(8); foreach (int v in tab) o.U32((uint)v);
        }
        var n0d = o.ToArray();
        for (int k = 0; k < nah; k++)
        {
            var (pa, sz, al, pad) = handles[k];
            BinaryPrimitives.WriteUInt64LittleEndian(n0d.AsSpan(newAH + 0x18 * k), (ulong)(newPos.TryGetValue(pa, out int np) ? np : 0));
            BinaryPrimitives.WriteUInt64LittleEndian(n0d.AsSpan(newAH + 0x18 * k + 8), (ulong)(pa != 0 ? blobs[pa].Length : sz));
            BinaryPrimitives.WriteUInt32LittleEndian(n0d.AsSpan(newAH + 0x18 * k + 16), al);
            BinaryPrimitives.WriteUInt32LittleEndian(n0d.AsSpan(newAH + 0x18 * k + 20), pad);
        }
        BinaryPrimitives.WriteUInt64LittleEndian(n0d.AsSpan(0x20), 0x140);
        BinaryPrimitives.WriteUInt64LittleEndian(n0d.AsSpan(0x30), (ulong)newAH);
        // node 0's tail after the second table (DS2 pads with CD up to the next block)
        n0.Data = n0d;
        // network bone-order tables (+0x130 / +0x138): {abs ptr → data, u32[n]}
        int ki = 0;
        foreach (var kind in new[] { "boneorderA", "boneorderName" })
        {
            var it = items.First(i => i.Kind == kind);
            var tab = ki++ == 0 ? orderA : byNameBytes;
            var b = new NmbFile.Buf(); b.U64(0); foreach (int v in tab) b.U32((uint)v); b.Pad(4, 0xCD);
            // keep the donor's trailing bytes when the size is unchanged (padding before the next block)
            var nb = b.ToArray();
            if (n == nd && it.Data.Length > nb.Length) nb = [.. nb, .. it.Data[nb.Length..]];
            it.Data = nb;
            it.Ptrs = [(0, it, 8)];
        }
        byte[] newNet = NmbNetBlocks.Emit(items);
        var repl = new Dictionary<int, byte[]> { [ri] = rigData, [ni] = newNet };
        foreach (int k in mapBundles) repl[k] = mapData;
        // self-resize: an identical channel map keeps the donor bundle's own bytes (some DS2 maps are not identity+1)
        if (n == nd && tn.SequenceEqual(dn))
            foreach (int k in mapBundles) if (NmbChannelMap.Read(f.Bundles[k].Data).AnimToRig.SequenceEqual(m.AnimToRig)) repl[k] = f.Bundles[k].Data;
        res.Add($"channel maps rewritten: {mapBundles.Count}");
        res.Nmb = f.Rebuild(repl);
        res.Unmatched = [.. unm];
        return res;
    }

    sealed class ByteCmp : IComparer<byte[]>
    {
        public int Compare(byte[]? a, byte[]? b)
        {
            for (int i = 0; i < Math.Min(a!.Length, b!.Length); i++) if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return a.Length.CompareTo(b.Length);
        }
    }

    /// <summary>
    /// After a resize (net_resize.check_rig_consistency): every per-bone array has the rig's length, every bone index is in
    /// range, attrib 84's joint records resolve, the mirror covers every bone once with bind-pose-rule quaternions (and, with
    /// <paramref name="strictNamePairs"/>, exactly the name-twin pairs), attrib 20 names the rig bundle's size. Returns the
    /// problems (empty = clean).
    /// </summary>
    public static List<string> CheckRigConsistency(byte[] nmb, bool strictNamePairs = false)
    {
        var f = NmbFile.Read(nmb);
        var x = f.Bundles[f.IndexOf(10)].Data;
        var rig = NmbRig.Read(f.Bundles[f.IndexOf(1)].Data);
        int n = rig.Names.Count; int rigSize = f.Bundles[f.IndexOf(1)].Data.Length;
        var bad = new List<string>();
        if (U(x, 0x118) != n) bad.Add("+0x118");
        var net = new NmbNetwork(x);
        foreach (var node in net.Nodes())
            foreach (var a in node.Attribs)
            {
                int o = a.Offset, t = a.Type;
                if (t == 87 && H(x, o + 0x12) != n) bad.Add($"node {node.Index} attrib 87 ({H(x, o + 0x12)} for {n})");
                if (t == 6 && U(x, o + 0x10) != n) bad.Add($"node {node.Index} attrib 6");
                if (t == 84)
                {
                    var (n84, k84, _, p2, ptr, _) = ReadA84(x, o);
                    var refs = ptr.Where(q => q != 0).Select(q => (q - p2) / 80).Order().ToList();
                    if (n84 != n || a.Size != p2 + 80 * k84 || !refs.SequenceEqual(Enumerable.Range(0, k84).Select(v => (long)v))) bad.Add($"node {node.Index} attrib 84");
                }
                if (t == 8)
                {
                    int cnt = (int)U(x, o + 0x10);
                    if (Enumerable.Range(0, cnt).Any(k => U(x, o + 0x20 + 4 * k) >= n)) bad.Add($"node {node.Index} attrib 8 index");
                }
                if (IndexFields.TryGetValue(t, out var fields) && fields.Any(fo => I(x, o + fo) >= n)) bad.Add($"node {node.Index} attrib {t} index");
                if (t == 20 && Q(x, o + 0x18) != (ulong)rigSize) bad.Add("rig size");
            }
        var skel = new MorphemeSkeleton
        {
            Names = rig.Names, Parents = rig.Parents,
            BindPos = rig.BindPos.Select(p => new[] { (double)p[0], p[1], p[2] }).ToList(),
            BindRot = rig.BindRot.Select(q => new[] { (double)q[0], q[1], q[2], q[3] }).ToList(),
        };
        var W = WorldRotations(skel.Parents, skel.BindRot);
        // session 11 (burgchr11): the weapon twins may stay self-mapped (Capra's L_Weapon / R_Weapon, two machetes: the resize pairs
        // twins whose bind poses mirror; DS2 mirror nodes never swap weapons anyway)
        var want = NamePairs(rig.Names).Select(p => (Math.Min(p.A, p.B), Math.Max(p.A, p.B))).ToHashSet();
        var wantNoWeapons = want.Where(p => !rig.Names[p.Item1].Contains("Weapon", StringComparison.OrdinalIgnoreCase)).ToHashSet();
        foreach (var a in net.GetNode(0).Attribs.Where(a => a.Type == 66))
        {
            var mb = ReadMirror(x, a.Offset);
            var cover = mb.Pairs.SelectMany(p => new[] { p.A, p.B }).Concat(mb.Self.Select(s => s.Bone)).Order().ToList();
            if (!cover.SequenceEqual(Enumerable.Range(0, n))) bad.Add("mirror coverage");
            // strict: every pair a name twin and every name twin paired (a DS1 rig on a donor whose pairs are all twins);
            // DS2's own artist pairs (13 of 157 blocks) are not an error in general
            var gotP = mb.Pairs.Select(p => (Math.Min(p.A, p.B), Math.Max(p.A, p.B))).ToHashSet();
            if (strictNamePairs && !(gotP.SetEquals(want) || gotP.SetEquals(wantNoWeapons)))
            {
                var got = mb.Pairs.Select(p => (Math.Min(p.A, p.B), Math.Max(p.A, p.B))).ToHashSet();
                string Nm((int A, int B) q) => $"{rig.Names[q.A]}/{rig.Names[q.B]}";
                bad.Add($"mirror pairs != name twins (extra: {string.Join(", ", got.Except(want).Take(4).Select(Nm))}; missing: {string.Join(", ", want.Except(got).Take(4).Select(Nm))})");   // session 11 (burgchr11): detail
            }
            if (!mb.Pairs.All(p => { var (qa, qb) = PairQuats(W, p.A, p.B); return QSame(qa, p.QA.Select(v => (double)v).ToArray()) && QSame(qb, p.QB.Select(v => (double)v).ToArray()); }))
                bad.Add("mirror pair quaternions != bind-pose rule");
            if (!mb.Self.All(s => QSame(SelfQuat(W, s.Bone), s.Q.Select(v => (double)v).ToArray()))) bad.Add("mirror self quaternions != bind-pose rule");
        }
        return bad;
    }
}
