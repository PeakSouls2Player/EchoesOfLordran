using System.Buffers.Binary;

namespace Lordran.Tools.Formats.Morpheme;

/// <summary>
/// Read/patch access to the parts of a DS2 Morpheme network bundle (NMB type 10) that the 1:1 DS1 character pipeline
/// changes in place (session 8, track chr step 2). Layout from our own research (data\research\morpheme\nmb_net.py,
/// net_mirror.py; no NaturalMotion code). All pointers are relative to the bundle data unless noted.
/// <list type="bullet">
/// <item>Header: u32 node count at 0x90, u64 pointer to the node pointer table at 0x98; u32 rig bone count at 0x118.</item>
/// <item>Node at p: u32 type, u16 flags, u16 id, u16 parent, u16 child count, u16 control-parameter count; u16 attrib count
/// at +0x10; at +0x20/+0x28/+0x30 pointers (relative to p) to the children (u16), control parameters and attrib headers
/// (0x18 each: u64 pointer relative to p, u64 size, u32 alignment). An AttribData block starts with 8 zero bytes,
/// u16 0xFFFF, u16 type.</item>
/// <item>Anim node (type 104), SourceAnim attrib (type 21, 160 bytes; evidence: every c1030 clip, session 8):
/// +0x20 trajectory start position (x,y,z,0), +0x30 start rotation (x,y,z,w), +0x40 end position, +0x50 end rotation
/// (Morpheme character space; c1030 walk: end (0,-1.6036,0) = its NSA trajectory end; WalkStayTurnRight: end rotation
/// -90° about Z), +0x74 clip index in the file-name table, +0x78 channel-map assetId, +0x80 f32 0, +0x84 f32 1,
/// +0x88 f32 clip duration in seconds (equals the NSA header duration on all 159 c1030 clips).</item>
/// <item>SourceEventTracks attrib (type 23): three groups (discrete, duration, curve) at +0x10 + 0x18·k: u32 count, then
/// u64 pointer to count × (u32 track assetId, u32), u64 pointer to count × u32 track bundle data size (2556/2556 sizes
/// equal the bundle sizes on c1030), pointers relative to the attrib.</item>
/// <item>Mirror mapping (type 66, on node 0; net_mirror.py): +0x14 u32 pair count P, +0x18 ptr P × {f32[4] qA, f32[4] qB,
/// u32 boneA, u32 boneB, u64 0}, +0x20 u32 T, +0x28 ptr T × {u32,u32} event-track userData pairs, +0x30 u32 E,
/// +0x38 ptr E × {u32,u32} event value pairs, +0x40 u32 U, +0x48 ptr U × f32[4], +0x50 ptr U × u32 self-mirrored bones.</item>
/// <item>MirrorTransforms node (type 135): attrib type 8 (INT_ARRAY: u32 count at +0x10, u64 pointer at +0x18) = rig bones
/// excluded from mirroring.</item>
/// </list>
/// </summary>
public sealed class NmbNetwork
{
    public readonly byte[] X;
    public readonly int NodeCount;
    readonly long[] nodePtr;

    public NmbNetwork(byte[] bundleData)
    {
        X = bundleData;
        NodeCount = (int)U32(0x90);
        long pTab = (long)U64(0x98);
        nodePtr = new long[NodeCount];
        for (int i = 0; i < NodeCount; i++) nodePtr[i] = (long)U64((int)pTab + 8 * i);
    }

    public uint U32(int o) => BinaryPrimitives.ReadUInt32LittleEndian(X.AsSpan(o));
    public ulong U64(int o) => BinaryPrimitives.ReadUInt64LittleEndian(X.AsSpan(o));
    public ushort U16(int o) => BinaryPrimitives.ReadUInt16LittleEndian(X.AsSpan(o));
    public float F32(int o) => BinaryPrimitives.ReadSingleLittleEndian(X.AsSpan(o));
    public void PutU32(int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(X.AsSpan(o), v);
    public void PutF32(int o, float v) => BinaryPrimitives.WriteSingleLittleEndian(X.AsSpan(o), v);

    public sealed record Attrib(int Offset, long Size, int Type);
    public sealed record Node(int Index, int Offset, int Type, int Parent, ushort[] Children, List<Attrib> Attribs);

    public Node GetNode(int i)
    {
        int p = (int)nodePtr[i];
        int type = (int)U32(p), parent = U16(p + 8), nch = U16(p + 10), nat = U16(p + 0x10);
        long pCh = (long)U64(p + 0x20), pAh = (long)U64(p + 0x30);
        var ch = new ushort[nch];
        for (int k = 0; k < nch && pCh != 0; k++) ch[k] = U16(p + (int)pCh + 2 * k);
        var at = new List<Attrib>();
        for (int k = 0; k < nat; k++)
        {
            int h = p + (int)pAh + 0x18 * k;
            long pa = (long)U64(h), size = (long)U64(h + 8);
            if (pa == 0) continue;
            int a = p + (int)pa;
            at.Add(new Attrib(a, size, U16(a + 10)));
        }
        return new Node(i, p, type, parent, ch, at);
    }

    public IEnumerable<Node> Nodes() { for (int i = 0; i < NodeCount; i++) yield return GetNode(i); }

    public static Attrib? Find(Node n, int type) => n.Attribs.FirstOrDefault(a => a.Type == type);

    // ------------------------------------------------------------------ SourceAnim (type 21)
    public int ClipIndex(Attrib sa) => (int)U32(sa.Offset + 0x74);
    public float ClipDuration(Attrib sa) => F32(sa.Offset + 0x88);
    /// <summary>SourceAnim +0x8C: index of the node's sync track among its discrete (group 0) event tracks, -1 = none
    /// (always the Footsteps track: net_srcanim.py --survey 48,832/48,832 nodes; fix pass, session 8).</summary>
    public int SyncTrackIndex(Attrib sa) => (int)U32(sa.Offset + 0x8C);
    /// <summary>SourceAnim +0x80 / +0x84: clip start and end fractions (0 / 1 = whole clip).</summary>
    public (float Start, float End) ClipFractions(Attrib sa) => (F32(sa.Offset + 0x80), F32(sa.Offset + 0x84));

    /// <summary>Write the clip's trajectory start/end transforms (Morpheme space; rotations as (w,x,y,z) in, stored x,y,z,w)
    /// and its duration.</summary>
    public void PatchSourceAnim(Attrib sa, V3 p0, Q4 q0, V3 p1, Q4 q1, double duration)
    {
        int a = sa.Offset;
        void Pos(int o, V3 v) { PutF32(a + o, (float)v.X); PutF32(a + o + 4, (float)v.Y); PutF32(a + o + 8, (float)v.Z); PutF32(a + o + 12, 0f); }
        void Rot(int o, Q4 q) { PutF32(a + o, (float)q.X); PutF32(a + o + 4, (float)q.Y); PutF32(a + o + 8, (float)q.Z); PutF32(a + o + 12, (float)q.W); }
        Pos(0x20, p0); Rot(0x30, q0); Pos(0x40, p1); Rot(0x50, q1);
        PutF32(a + 0x88, (float)duration);
    }

    // ------------------------------------------------------------------ SourceEventTracks (type 23)
    /// <summary>(group, track assetId, offset of its size field) for every track an attrib references.</summary>
    public IEnumerable<(int Group, uint TrackId, int SizeField)> Tracks(Attrib et)
    {
        int e = et.Offset;
        for (int k = 0; k < 3; k++)
        {
            int cnt = (int)U32(e + 0x10 + 0x18 * k);
            long pid = (long)U64(e + 0x18 + 0x18 * k), psz = (long)U64(e + 0x20 + 0x18 * k);
            for (int j = 0; j < cnt; j++)
                yield return (k, U32(e + (int)pid + 8 * j), psz != 0 ? e + (int)psz + 4 * j : -1);
        }
    }

    // ------------------------------------------------------------------ mirror mapping (type 66)
    public sealed class Mirror
    {
        public int Offset;
        public List<(int A, int B, float[] QA, float[] QB)> Pairs = [];
        public List<(int Bone, float[] Q)> Self = [];
    }

    public Mirror? ReadMirror()
    {
        var n0 = GetNode(0);
        var at = Find(n0, 66);
        if (at == null) return null;
        int A = at.Offset;
        var m = new Mirror { Offset = A };
        int P = (int)U32(A + 0x14); int pP = (int)U64(A + 0x18);
        for (int k = 0; k < P; k++)
        {
            int o = A + pP + 48 * k;
            m.Pairs.Add(((int)U32(o + 32), (int)U32(o + 36), [F32(o), F32(o + 4), F32(o + 8), F32(o + 12)], [F32(o + 16), F32(o + 20), F32(o + 24), F32(o + 28)]));
        }
        int U = (int)U32(A + 0x40); int pUq = (int)U64(A + 0x48), pUi = (int)U64(A + 0x50);
        for (int k = 0; k < U; k++)
            m.Self.Add(((int)U32(A + pUi + 4 * k), [F32(A + pUq + 16 * k), F32(A + pUq + 16 * k + 4), F32(A + pUq + 16 * k + 8), F32(A + pUq + 16 * k + 12)]));
        return m;
    }

    /// <summary>Overwrite the mirror mapping in place; the pair and self counts must equal the existing ones (the attrib
    /// cannot grow without re-serialising the network).</summary>
    public void WriteMirror(Mirror m)
    {
        int A = m.Offset;
        int P = (int)U32(A + 0x14), U = (int)U32(A + 0x40);
        if (m.Pairs.Count != P || m.Self.Count != U) throw new InvalidOperationException($"mirror mapping size change {P}/{U} -> {m.Pairs.Count}/{m.Self.Count} needs a network re-serialiser");
        int pP = (int)U64(A + 0x18);
        for (int k = 0; k < P; k++)
        {
            int o = A + pP + 48 * k;
            var (a, b, qa, qb) = m.Pairs[k];
            for (int c = 0; c < 4; c++) { PutF32(o + 4 * c, qa[c]); PutF32(o + 16 + 4 * c, qb[c]); }
            PutU32(o + 32, (uint)a); PutU32(o + 36, (uint)b);
        }
        int pUq = (int)U64(A + 0x48), pUi = (int)U64(A + 0x50);
        for (int k = 0; k < U; k++)
        {
            var (bone, q) = m.Self[k];
            for (int c = 0; c < 4; c++) PutF32(A + pUq + 16 * k + 4 * c, q[c]);
            PutU32(A + pUi + 4 * k, (uint)bone);
        }
    }

    // ------------------------------------------------------------------ INT_ARRAY (type 8)
    public int[] IntArray(Attrib a)
    {
        int n = (int)U32(a.Offset + 0x10); int p = (int)U64(a.Offset + 0x18);
        var v = new int[n];
        for (int i = 0; i < n; i++) v[i] = (int)U32(a.Offset + p + 4 * i);
        return v;
    }

    public void PutIntArray(Attrib a, int[] v)
    {
        int n = (int)U32(a.Offset + 0x10); int p = (int)U64(a.Offset + 0x18);
        if (v.Length != n) throw new InvalidOperationException("INT_ARRAY length change");
        for (int i = 0; i < n; i++) PutU32(a.Offset + p + 4 * i, (uint)v[i]);
    }
}
