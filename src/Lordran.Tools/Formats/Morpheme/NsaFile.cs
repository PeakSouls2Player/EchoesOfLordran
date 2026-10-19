using System.Buffers.Binary;

namespace Lordran.Tools.Formats.Morpheme;

/// <summary>
/// DS2 SOTFS Morpheme 4 (x64) NSA animation clip: raw layout parse ↔ build, all quantised values as stored (no float
/// math). Ported in session 8 from our own Python research reader/writer (data\research\morpheme\nsa_format.py,
/// format notes in NSA-FORMAT.md §1), which rebuilds 1552/1552 DS2 clips byte-identically. No NaturalMotion code.
///
/// Layout, in order: header (0xC8); channel lists static-T, static-R; one pointer each to the dynamic-T/-R lists (when
/// there is a section); the dynamic lists; translation and rotation factor sets (6 floats: min xyz, extent xyz);
/// section start frames / sizes / absolute offsets; the static segment (padded to 16); the sections (DS2: exactly one,
/// holding every frame, or none for a fully static clip); the trajectory (root motion) segment, or 32 zero bytes for
/// the 2 of 1552 clips without one. A channel list is an i16 count, the u16 channels, 0xFFFF padding to
/// 1 + roundup(count, 4) entries and to 4 bytes. Channel c drives rig bone anim_to_rig[c] of the NMB channel map.
/// </summary>
public sealed class NsaFile
{
    public uint U14 = 2046, U18 = 2;            // 0x14 = 2046 when the clip has sections, else 0; 0x18 = 2
    public float Duration, Fps;                 // duration = (frames - 1) / fps
    public int ChannelCount;                    // = NMB channel-map length (rig bones - 1 on DS2 rigs)
    public List<int> StaticT = [], StaticR = [], DynT = [], DynR = [];
    public float[] TInit = new float[6];        // factor of the 8-bit dynamic-translation means
    public List<float[]> TFactors = [], RFactors = [];
    public List<uint> SectionFrames = [];       // [0, frames - 1]
    public StaticSegment Static = new();
    public List<Section> Sections = [];
    public Trajectory? Root;
    public byte[]? Tail;                        // bytes after the last section when there is no trajectory (32 x 0)

    public sealed class StaticSegment
    {
        public float[] TFac = new float[6], RFac = new float[6];
        public List<ushort[]> TRaw = [], RRaw = []; // 3 x u16 per channel
        public long ParsedPT = -1, ParsedPR = -1;   // the segment-relative pointers as read (diagnostics)
    }

    public sealed class Section
    {
        public int SampleCount, U0C;
        public int TCount, RCount;
        public uint[][] TRaw = [];      // [frame][channel] packed 11/11/10
        public List<byte[]> TInfo = []; // roundup(tc,4) x (mean xyz u8, factor index xyz u8)
        public ushort[][] RRaw = [];    // [frame][channel*3]
        public byte[][]? RPad;          // per-frame padding bytes as read (null = zeros)
        public List<byte[]> RInfo = [];
    }

    public sealed class Trajectory
    {
        public uint U00 = 2, U04;
        public ulong U08;
        public float Fps;
        public int N;
        public float[] PosFac = new float[6], RotFac = new float[6];
        public uint[]? Pos;             // null: constant position = PosFac[0..2]
        public ushort[][]? Rot;         // null: constant rotation = RotFac[0..3] as (x, y, z, w)
    }

    static int Al(int x, int a) => (x + a - 1) / a * a;
    static int IdxListSize(int n) => Al(2 * (1 + Al(n, 4)), 4);

    // ------------------------------------------------------------------ parse
    public static NsaFile Parse(byte[] d)
    {
        ulong Q(long o) => BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan((int)o));
        uint I(long o) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan((int)o));
        float Fl(long o) => BinaryPrimitives.ReadSingleLittleEndian(d.AsSpan((int)o));
        ushort H(long o) => BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan((int)o));
        float[] F6(long o) { var f = new float[6]; for (int i = 0; i < 6; i++) f[i] = Fl(o + 4 * i); return f; }
        List<int> IdxList(long o)
        {
            int n = (short)H(o);
            var l = new List<int>(n);
            for (int i = 0; i < n; i++) l.Add(H(o + 2 + 2 * i));
            return l;
        }
        var n = new NsaFile
        {
            U14 = I(0x14), U18 = I(0x18),
            Duration = Fl(0x28), Fps = Fl(0x2C),
            ChannelCount = (int)I(0x30),
        };
        int ns = (int)I(0x34);
        n.StaticT = IdxList((long)Q(0x40)); n.StaticR = IdxList((long)Q(0x48));
        if (Q(0x50) != 0) n.DynT = IdxList((long)Q((long)Q(0x50)));
        if (Q(0x58) != 0) n.DynR = IdxList((long)Q((long)Q(0x58)));
        n.TInit = F6(0x60);
        int nTF = (int)I(0x78), nRF = (int)I(0x7C);
        for (int i = 0; i < nTF; i++) n.TFactors.Add(F6((long)Q(0x80) + 24 * i));
        for (int i = 0; i < nRF; i++) n.RFactors.Add(F6((long)Q(0x88) + 24 * i));
        var secSizes = new List<uint>(); var secOffs = new List<ulong>();
        if (ns > 0)
        {
            for (int i = 0; i <= ns; i++) n.SectionFrames.Add(I((long)Q(0xA0) + 4 * i));
            for (int i = 0; i < ns; i++) secSizes.Add(I((long)Q(0xA8) + 4 * i));
            for (int i = 0; i < ns; i++) secOffs.Add(Q((long)Q(0xB0) + 8 * i));
        }
        long ps = (long)Q(0x90);
        var st = n.Static;
        int stc = (int)I(ps), src = (int)I(ps + 4);
        st.TFac = F6(ps + 8); st.RFac = F6(ps + 32);
        long pT = (long)Q(ps + 56), pR = (long)Q(ps + 64);
        st.ParsedPT = pT; st.ParsedPR = pR;
        for (int i = 0; i < stc; i++) st.TRaw.Add([H(ps + pT + 6 * i), H(ps + pT + 6 * i + 2), H(ps + pT + 6 * i + 4)]);
        for (int i = 0; i < src; i++) st.RRaw.Add([H(ps + pR + 6 * i), H(ps + pR + 6 * i + 2), H(ps + pR + 6 * i + 4)]);
        for (int si = 0; si < ns; si++)
        {
            long p = (long)secOffs[si];
            var s = new Section { SampleCount = (int)I(p), TCount = (int)I(p + 4), RCount = (int)I(p + 8), U0C = (int)I(p + 12) };
            long pTF = (long)Q(p + 16), pTI = (long)Q(p + 24), pRF = (long)Q(p + 32), pRI = (long)Q(p + 40);
            s.TRaw = new uint[s.SampleCount][];
            for (int f = 0; f < s.SampleCount; f++)
            {
                s.TRaw[f] = new uint[s.TCount];
                for (int c = 0; c < s.TCount; c++) s.TRaw[f][c] = I(p + pTF + 4 * (f * s.TCount + c));
            }
            int nti = Al(s.TCount, 4), nri = Al(s.RCount, 4);
            for (int i = 0; i < nti; i++) s.TInfo.Add(d[(int)(p + pTI + 6 * i)..(int)(p + pTI + 6 * i + 6)]);
            int stride = Al(6 * s.RCount, 4);
            s.RRaw = new ushort[s.SampleCount][];
            s.RPad = new byte[s.SampleCount][];
            for (int f = 0; f < s.SampleCount; f++)
            {
                s.RRaw[f] = new ushort[3 * s.RCount];
                for (int k = 0; k < 3 * s.RCount; k++) s.RRaw[f][k] = H(p + pRF + (long)f * stride + 2 * k);
                s.RPad[f] = d[(int)(p + pRF + (long)f * stride + 6 * s.RCount)..(int)(p + pRF + (long)(f + 1) * stride)];
            }
            for (int i = 0; i < nri; i++) s.RInfo.Add(d[(int)(p + pRI + 6 * i)..(int)(p + pRI + 6 * i + 6)]);
            n.Sections.Add(s);
        }
        long pr = (long)Q(0xB8);
        if (pr == 0)
        {
            long end = ns > 0 ? (long)secOffs[^1] + secSizes[^1] : Al((int)ps + 72, 16);
            n.Tail = d[(int)end..];
            return n;
        }
        var r = n.Root = new Trajectory
        {
            U00 = I(pr), U04 = I(pr + 4), U08 = Q(pr + 8),
            Fps = Fl(pr + 0x20), N = (int)I(pr + 0x24),
            PosFac = F6(pr + 0x28), RotFac = F6(pr + 0x40),
        };
        long pP = (long)Q(pr + 0x58), pQ = (long)Q(pr + 0x60);
        if (pP != 0) { r.Pos = new uint[r.N]; for (int i = 0; i < r.N; i++) r.Pos[i] = I(pr + pP + 4 * i); }
        if (pQ != 0)
        {
            r.Rot = new ushort[r.N][];
            for (int i = 0; i < r.N; i++) r.Rot[i] = [H(pr + pQ + 6 * i), H(pr + pQ + 6 * i + 2), H(pr + pQ + 6 * i + 4)];
        }
        return n;
    }

    // ------------------------------------------------------------------ build
    sealed class W
    {
        readonly List<byte> b = [];
        public int At => b.Count;
        public void Pad(int a, byte v = 0) { while (b.Count % a != 0) b.Add(v); }
        public void U8(byte v) => b.Add(v);
        public void U16(ushort v) { b.Add((byte)v); b.Add((byte)(v >> 8)); }
        public void I16(short v) => U16((ushort)v);
        public void U32(uint v) { for (int i = 0; i < 4; i++) b.Add((byte)(v >> (8 * i))); }
        public void I32(int v) => U32((uint)v);
        public void U64(ulong v) { for (int i = 0; i < 8; i++) b.Add((byte)(v >> (8 * i))); }
        public void F32(float v) => U32(BitConverter.SingleToUInt32Bits(v));
        public void F6(float[] f) { for (int i = 0; i < 6; i++) F32(f[i]); }
        public void Raw(IEnumerable<byte> x) => b.AddRange(x);
        public void PatchU32(int o, uint v) { for (int i = 0; i < 4; i++) b[o + i] = (byte)(v >> (8 * i)); }
        public void PatchU64(int o, ulong v) { for (int i = 0; i < 8; i++) b[o + i] = (byte)(v >> (8 * i)); }
        public void PatchF32(int o, float v) => PatchU32(o, BitConverter.SingleToUInt32Bits(v));
        public byte[] ToArray() => b.ToArray();
    }

    static int PutIdxList(W w, List<int> lst)
    {
        int o = w.At;
        w.I16((short)lst.Count);
        foreach (int c in lst) w.U16((ushort)c);
        int end = o + IdxListSize(lst.Count);
        while (w.At < end) w.U16(0xFFFF);
        return o;
    }

    static byte[] BuildSection(Section s)
    {
        var w = new W();
        w.I32(s.SampleCount); w.I32(s.TCount); w.I32(s.RCount); w.I32(s.U0C);
        w.U64(0); w.U64(0); w.U64(0); w.U64(0);
        int pTF = w.At;
        foreach (var fr in s.TRaw) foreach (uint v in fr) w.U32(v);
        int pTI = w.At;
        foreach (var inf in s.TInfo) w.Raw(inf);
        w.Pad(4);
        int pRF = w.At;
        int stride = Al(6 * s.RCount, 4);
        for (int f = 0; f < s.RRaw.Length; f++)
        {
            foreach (ushort v in s.RRaw[f]) w.U16(v);
            int padLen = stride - 6 * s.RCount;
            var pad = s.RPad?[f] ?? [];
            for (int i = 0; i < padLen; i++) w.U8(i < pad.Length ? pad[i] : (byte)0);
        }
        int pRI = w.At;
        foreach (var inf in s.RInfo) w.Raw(inf);
        w.Pad(16);
        if (s.TCount == 0) pTF = pTI = 0;
        if (s.RCount == 0) pRF = pRI = 0;
        w.PatchU64(16, (ulong)pTF); w.PatchU64(24, (ulong)pTI); w.PatchU64(32, (ulong)pRF); w.PatchU64(40, (ulong)pRI);
        return w.ToArray();
    }

    public static byte[] BuildTrajectory(Trajectory r)
    {
        var w = new W();
        w.U32(r.U00); w.U32(r.U04); w.U64(r.U08); w.U64(0); w.U64(16);
        w.F32(r.Fps); w.I32(r.N);
        w.F6(r.PosFac); w.F6(r.RotFac);
        w.U64(0); w.U64(0);
        int pP = 0, pQ = 0;
        if (r.Pos != null) { pP = w.At; foreach (uint v in r.Pos) w.U32(v); }
        if (r.Rot != null)
        {
            pQ = w.At;
            foreach (var v in r.Rot) { w.U16(v[0]); w.U16(v[1]); w.U16(v[2]); }
            for (int i = 0; i < 6; i++) w.U8(0); // one extra zero rotation sample is always allocated
        }
        w.Pad(16);
        w.PatchU64(0x58, (ulong)pP); w.PatchU64(0x60, (ulong)pQ);
        w.PatchU64(0x10, (ulong)w.At);
        return w.ToArray();
    }

    public byte[] Build()
    {
        var w = new W();
        for (int i = 0; i < 0xC8; i++) w.U8(0);
        int pST = PutIdxList(w, StaticT), pSR = PutIdxList(w, StaticR);
        int ns = Sections.Count;
        int ppDT = 0, ppDR = 0;
        if (ns > 0)
        {
            ppDT = w.At; w.U64(0);
            ppDR = w.At; w.U64(0);
            int pDT = PutIdxList(w, DynT), pDR = PutIdxList(w, DynR);
            w.PatchU64(ppDT, (ulong)pDT); w.PatchU64(ppDR, (ulong)pDR);
        }
        int pTF = TFactors.Count > 0 ? w.At : 0;
        foreach (var f in TFactors) w.F6(f);
        int pRF = RFactors.Count > 0 ? w.At : 0;
        foreach (var f in RFactors) w.F6(f);
        var secs = Sections.Select(BuildSection).ToList();
        int pA0 = 0, pA8 = 0, pB0 = 0;
        if (ns > 0)
        {
            pA0 = w.At; foreach (uint v in SectionFrames) w.U32(v);
            pA8 = w.At; foreach (var s in secs) w.U32((uint)s.Length);
            pB0 = w.At; for (int i = 0; i < ns; i++) w.U64(0);
        }
        int pStat = w.At;
        w.I32(Static.TRaw.Count); w.I32(Static.RRaw.Count); w.F6(Static.TFac); w.F6(Static.RFac); w.U64(0); w.U64(0);
        int sT = w.At - pStat;
        foreach (var v in Static.TRaw) { w.U16(v[0]); w.U16(v[1]); w.U16(v[2]); }
        w.Pad(4);
        int sR = w.At - pStat;
        foreach (var v in Static.RRaw) { w.U16(v[0]); w.U16(v[1]); w.U16(v[2]); }
        // an empty static list has a null pointer (session 8 survey of all 19221 DS2 clips: static rc = 0 occurs in 2
        // clips, c6500 a65_15_1010 and c6540 a00_07_7014, both with pR = 0; tc = 0 never occurs, same rule assumed,
        // like the section pointers). nsa_format.py wrote the offset there (its 1552-clip test set had no such clip).
        w.PatchU64(pStat + 56, Static.TRaw.Count > 0 ? (ulong)sT : 0); w.PatchU64(pStat + 64, Static.RRaw.Count > 0 ? (ulong)sR : 0);
        w.Pad(16);
        int q08 = w.At;
        var offs = new List<int>();
        foreach (var s in secs) { offs.Add(w.At); w.Raw(s); }
        int pRoot = Root != null ? w.At : 0;
        if (Root != null) w.Raw(BuildTrajectory(Root));
        else w.Raw(Tail ?? new byte[32]);
        for (int i = 0; i < ns; i++) w.PatchU64(pB0 + 8 * i, (ulong)offs[i]);
        w.PatchU64(0x08, (ulong)q08);
        w.PatchU32(0x10, 16); w.PatchU32(0x14, U14); w.PatchU32(0x18, U18);
        w.PatchF32(0x28, Duration); w.PatchF32(0x2C, Fps);
        w.PatchU32(0x30, (uint)ChannelCount); w.PatchU32(0x34, (uint)ns); w.PatchU32(0x38, (uint)ns);
        w.PatchU32(0x3C, (uint)new[] { StaticT.Count, StaticR.Count, DynT.Count, DynR.Count }.Max()); // longest channel list
        w.PatchU64(0x40, (ulong)pST); w.PatchU64(0x48, (ulong)pSR); w.PatchU64(0x50, (ulong)ppDT); w.PatchU64(0x58, (ulong)ppDR);
        for (int i = 0; i < 6; i++) w.PatchF32(0x60 + 4 * i, TInit[i]);
        w.PatchU32(0x78, (uint)TFactors.Count); w.PatchU32(0x7C, (uint)RFactors.Count);
        w.PatchU64(0x80, (ulong)pTF); w.PatchU64(0x88, (ulong)pRF);
        w.PatchU64(0x90, (ulong)pStat);
        w.PatchU64(0x98, (ulong)(secs.Count > 0 ? secs.Max(s => s.Length) : 0));
        w.PatchU64(0xA0, (ulong)pA0); w.PatchU64(0xA8, (ulong)pA8); w.PatchU64(0xB0, (ulong)pB0); w.PatchU64(0xB8, (ulong)pRoot);
        return w.ToArray();
    }

    /// <summary>Frame count: the section samples, else the trajectory samples (fully static clip).</summary>
    public int FrameCount => Sections.Count > 0 ? Sections.Sum(s => s.SampleCount) : Root?.N ?? 1;
}
