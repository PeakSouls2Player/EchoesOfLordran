using static Lordran.Tools.Formats.Morpheme.MorphemeMath;

namespace Lordran.Tools.Formats.Morpheme;

/// <summary>
/// Our own DS2 Morpheme NSA encoder (session 8 port of data\research\morpheme\nsa_encode.py; rules in NSA-FORMAT.md
/// §1.5). Two modes:
/// <list type="bullet">
/// <item><b>fresh</b> (no template): computes its own quantisation. Rules that reproduce Morpheme on the DS2 data:
/// a channel is static when no frame differs from frame 0 by more than 1e-5 (identical split on 1552/1552 clips);
/// factor extent = f32(f32(f32(max) - f32(min)) / maxCode) with maxCode 65535 (16-bit), 2047/2047/1023 (packed
/// translations), 255 (tInit) (843/843 static translation factors reproduced); codes = round((v - min)/ext), with the
/// neighbouring codes checked against the float32 decode; min(16, max(1, (n + 2) / 4)) delta factor sets for n
/// dynamic channels (matches every clip); tInit = box of all dynamic translation samples; translation mean = average of
/// the channel; rotation mean = normalised hemisphere-aligned average. How Morpheme assigns channels to factor sets is
/// unknown: we sort channels by delta range and cut equal chunks. Re-decoded vs original (Python, 1552 clips): rotations
/// ≤ 0.0046°, translations ≤ 0.5 quantisation step.</item>
/// <item><b>template</b>: reuses a parsed clip's static/dynamic split, means, factor tables and factor assignment.
/// decode → encode(template) is byte-identical on 1548/1552 DS2 clips (the other 4, c6000, have an axis with
/// ext ≈ 1e-9 where neighbouring codes decode to the same float32: same floats, original code unrecoverable).</item>
/// </list>
/// The C# port reproduces the Python output byte for byte (validated on all c1030 and c3050 clips, both modes; the
/// float rules are in <see cref="MorphemeMath"/>). DS2 data always uses one section holding every frame.
/// </summary>
public static class NsaEncoder
{
    static readonly int[] MaxP = [2047, 2047, 1023]; // dynamic / trajectory translations: 11/11/10 bits in a u32
    const int MaxR = 65535;                           // rotations and static translations: 16 bits per component

    public static int QRound(double v, double lo, double ext, int mx)
    {
        if (ext == 0) return 0;
        double r = Math.Floor((v - lo) / ext + 0.5);
        return r < 0 ? 0 : r > mx ? mx : (int)r;
    }

    public static double ExtOf(double lo, double hi, int mx) => F32(F32(F32(hi) - F32(lo)) / mx);

    /// <summary>[min x, min y, min z, ext x, ext y, ext z] over the values.</summary>
    static float[] MakeFactor(IReadOnlyList<V3> vals, int[] mx)
    {
        var f = new float[6];
        for (int a = 0; a < 3; a++)
        {
            double lo = Min(vals.Select(v => v[a])), hi = Max(vals.Select(v => v[a]));
            f[a] = (float)lo;
            f[3 + a] = (float)ExtOf(lo, hi, mx[a]);
        }
        return f;
    }
    static float[] MakeFactor(IReadOnlyList<V3> vals, int mx) => MakeFactor(vals, [mx, mx, mx]);

    public static int NFactors(int n) => n == 0 ? 0 : Math.Min(16, Math.Max(1, (n + 2) / 4));

    /// <summary>Code whose float32 decode f32(f32(f32(ext·r) + lo) + add) is closest to v + add (r0, r0-1, r0+1).</summary>
    static int QRoundExact(double v, double lo, double ext, int mx, double add = 0.0)
    {
        int r0 = QRound(v, lo, ext, mx);
        if (ext == 0) return r0;
        double target = v + add;
        double bestE = 0; int bestR = 0; bool have = false;
        foreach (int r in new[] { r0, r0 - 1, r0 + 1 })
        {
            if (r < 0 || r > mx) continue;
            double dv = add != 0.0 ? F32(F32(F32(ext * r) + lo) + add) : F32(F32(ext * r) + lo);
            double e = Math.Abs(dv - target);
            if (!have || e < bestE) { bestE = e; bestR = r; have = true; }
        }
        return bestR;
    }

    static ushort[] Quantise3(V3 v, float[] fac, int[] mx) => [(ushort)QRoundExact(v.X, fac[0], fac[3], mx[0]), (ushort)QRoundExact(v.Y, fac[1], fac[4], mx[1]), (ushort)QRoundExact(v.Z, fac[2], fac[5], mx[2])];

    static V3 Tqa(Q4 q) => QuatToTqa(Hemi(q));

    /// <summary>Quantise a rotation with per-axis factors; q and -q are both tried (Morpheme does not force w ≥ 0).</summary>
    static ushort[] QuantRot(Q4 q, float[][] facs)
    {
        double bestE = 0; int[]? best = null;
        foreach (int sgn in new[] { 1, -1 })
        {
            var qq = sgn == 1 ? q : q.Neg();
            if (qq.W <= -0.999999) continue;
            var v = QuatToTqa(qq);
            var raw = new int[3];
            for (int a = 0; a < 3; a++) raw[a] = QRound(v[a], facs[a][a], facs[a][3 + a], MaxR);
            double err = Sum(Enumerable.Range(0, 3).Select(a => Math.Pow(facs[a][a] + raw[a] * (double)facs[a][3 + a] - v[a], 2.0)));
            if (best == null || err < bestE - 1e-18) { bestE = err; best = raw; }
        }
        return [(ushort)best![0], (ushort)best[1], (ushort)best[2]];
    }

    /// <summary>8-bit tan-quarter-angle code of the channel's mean rotation (normalised hemisphere-aligned average).</summary>
    static byte[] MeanRotInit(Q4[] qs)
    {
        var rf = qs[0];
        var al = qs.Select(q => Dot(q, rf) >= 0 ? q : q.Neg()).ToArray();
        var avg = Hemi(Normalize(new Q4(Sum(al.Select(q => q.W)) / al.Length, Sum(al.Select(q => q.X)) / al.Length,
                                        Sum(al.Select(q => q.Y)) / al.Length, Sum(al.Select(q => q.Z)) / al.Length)));
        var v = QuatToTqa(avg);
        return [Code8(v.X), Code8(v.Y), Code8(v.Z)];
        static byte Code8(double c) => (byte)Math.Max(0, Math.Min(255, (int)Math.Floor((c + 1.0) * 128.0 + 0.5)));
    }

    /// <summary>Factor-set index per channel: channels sorted by range (stable), cut into chunks of ceil(n / groups).</summary>
    static int[] GroupChannels(double[] ranges, int ngroups)
    {
        if (ranges.Length == 0) return [];
        var order = Enumerable.Range(0, ranges.Length).OrderBy(k => ranges[k]).ToArray(); // LINQ OrderBy is stable, like sorted()
        var idx = new int[ranges.Length];
        int per = Math.Max(1, (int)Math.Ceiling(ranges.Length / (double)ngroups));
        for (int rank = 0; rank < order.Length; rank++) idx[order[rank]] = Math.Min(ngroups - 1, rank / per);
        return idx;
    }

    static bool Classify(IReadOnlyList<V3> series, double eps)
    {
        var first = series[0];
        foreach (var v in series)
            if (Max([Math.Abs(v.X - first.X), Math.Abs(v.Y - first.Y), Math.Abs(v.Z - first.Z)]) > eps) return false;
        return true;
    }

    static byte[] Info(byte[] mean, int[] fidx) => [mean[0], mean[1], mean[2], (byte)fidx[0], (byte)fidx[1], (byte)fidx[2]];

    /// <summary>Encode; with <paramref name="template"/> its quantisation parameters are reused (template mode).</summary>
    public static NsaFile EncodeFile(NsaAnim anim, NsaFile? template = null, double posEps = 1e-5, double rotEps = 1e-5)
    {
        int nch = anim.ChannelCount; double fps = anim.Fps;
        var pos = anim.Pos; var rot = anim.Rot;
        int Fn = anim.RootPos.Length;
        var T = template;
        var n = new NsaFile
        {
            U14 = 2046, U18 = 2,
            Fps = (float)fps, Duration = (float)(anim.Duration ?? (Fn - 1) / fps), ChannelCount = nch,
        };
        // ---- static / dynamic split
        if (T != null) { n.StaticT = [.. T.StaticT]; n.StaticR = [.. T.StaticR]; n.DynT = [.. T.DynT]; n.DynR = [.. T.DynR]; }
        else
            for (int c = 0; c < nch; c++)
            {
                (Classify(pos[c], posEps) ? n.StaticT : n.DynT).Add(c);
                (Classify(rot[c].Select(Tqa).ToArray(), rotEps) ? n.StaticR : n.DynR).Add(c);
            }
        // ---- static segment
        var st = n.Static;
        var sp = n.StaticT.Select(c => pos[c][0]).ToList();
        var sr = n.StaticR.Select(c => Tqa(rot[c][0])).ToList();
        if (T != null) { st.TFac = (float[])T.Static.TFac.Clone(); st.RFac = (float[])T.Static.RFac.Clone(); }
        else
        {
            st.TFac = sp.Count > 0 ? MakeFactor(sp, MaxR) : new float[6];
            st.RFac = sr.Count > 0 ? MakeFactor(sr, MaxR) : new float[6];
        }
        st.TRaw = sp.Select(v => Quantise3(v, st.TFac, [MaxR, MaxR, MaxR])).ToList();
        st.RRaw = n.StaticR.Select(c => QuantRot(rot[c][0], [st.RFac, st.RFac, st.RFac])).ToList();
        // ---- dynamic section (DS2 data always uses a single section holding every frame)
        int nTF = NFactors(n.DynT.Count), nRF = NFactors(n.DynR.Count);
        if (n.DynT.Count > 0 || n.DynR.Count > 0)
        {
            var s = new NsaFile.Section { SampleCount = Fn, TCount = n.DynT.Count, RCount = n.DynR.Count, U0C = 0 };
            n.SectionFrames = [0, (uint)(Fn - 1)];
            // translations: 8-bit mean (tInit factor over all dynamic samples) + grouped delta factors
            List<byte[]> tinfo;
            if (T != null) { n.TInit = (float[])T.TInit.Clone(); tinfo = T.Sections[0].TInfo.Take(s.TCount).Select(x => (byte[])x.Clone()).ToList(); }
            else if (n.DynT.Count > 0)
            {
                var allp = n.DynT.SelectMany(c => pos[c].Take(Fn)).ToList();
                n.TInit = MakeFactor(allp, 255);
                tinfo = [];
                foreach (int c in n.DynT)
                {
                    var m = V3.From(a => Sum(pos[c].Select(p => p[a])) / Fn);
                    tinfo.Add([(byte)QRound(m.X, n.TInit[0], n.TInit[3], 255), (byte)QRound(m.Y, n.TInit[1], n.TInit[4], 255), (byte)QRound(m.Z, n.TInit[2], n.TInit[5], 255)]);
                }
            }
            else { n.TInit = new float[6]; tinfo = []; }
            var deltas = new List<V3[]>();
            for (int k = 0; k < n.DynT.Count; k++)
            {
                var mp = NsaCodec.MeanPos(tinfo[k], n.TInit);
                deltas.Add(pos[n.DynT[k]].Select(p => V3.From(a => F32(p[a] - mp[a]))).ToArray());
            }
            if (T != null) n.TFactors = T.TFactors.Select(x => (float[])x.Clone()).ToList();
            else
            {
                var gidx = n.DynT.Select(_ => new int[3]).ToArray();
                n.TFactors = Enumerable.Range(0, nTF).Select(_ => new float[6]).ToList();
                for (int a = 0; a < 3; a++)
                {
                    var rng = deltas.Select(dl => Max(dl.Select(d => d[a])) - Min(dl.Select(d => d[a]))).ToArray();
                    var g = GroupChannels(rng, nTF);
                    for (int k = 0; k < n.DynT.Count; k++) gidx[k][a] = g[k];
                    for (int gi = 0; gi < nTF; gi++)
                    {
                        var vals = deltas.Where((_, k) => g[k] == gi).SelectMany(dl => dl.Select(d => d[a])).ToList();
                        if (vals.Count == 0) continue;
                        double lo = Min(vals), hi = Max(vals);
                        n.TFactors[gi][a] = (float)lo; n.TFactors[gi][3 + a] = (float)ExtOf(lo, hi, MaxP[a]);
                    }
                }
                tinfo = tinfo.Select((t, k) => Info(t, gidx[k])).ToList();
            }
            s.TRaw = new uint[Fn][];
            var tMeans = tinfo.Select(t => NsaCodec.MeanPos(t, n.TInit)).ToArray();
            for (int f = 0; f < Fn; f++)
            {
                var row = new uint[n.DynT.Count];
                for (int k = 0; k < n.DynT.Count; k++)
                {
                    var fi = tinfo[k]; var mp = tMeans[k]; var pv = pos[n.DynT[k]][f];
                    var q = new int[3];
                    for (int a = 0; a < 3; a++)
                    {
                        var fac = n.TFactors[fi[3 + a]];
                        q[a] = QRoundExact(pv[a] - mp[a], fac[a], fac[3 + a], MaxP[a], mp[a]);
                    }
                    row[k] = PackPos(q[0], q[1], q[2]);
                }
                s.TRaw[f] = row;
            }
            s.TInfo = tinfo.Count > 0 ? [.. tinfo, .. Enumerable.Repeat(tinfo[^1], Al4(tinfo.Count) - tinfo.Count)] : [];
            // rotations: 8-bit tqa mean + grouped delta factors, delta = conj(mean) · q
            List<byte[]> rinfo = T != null
                ? T.Sections[0].RInfo.Take(s.RCount).Select(x => (byte[])x.Clone()).ToList()
                : n.DynR.Select(c => MeanRotInit(rot[c])).ToList();
            var rdel = new List<Q4[]>();
            for (int k = 0; k < n.DynR.Count; k++)
            {
                var mq = NsaCodec.MeanQuat(rinfo[k]);
                rdel.Add(rot[n.DynR[k]].Select(q => Mul(Conj(mq), q)).ToArray());
            }
            if (T != null) n.RFactors = T.RFactors.Select(x => (float[])x.Clone()).ToList();
            else
            {
                var gidx = n.DynR.Select(_ => new int[3]).ToArray();
                n.RFactors = Enumerable.Range(0, nRF).Select(_ => new float[6]).ToList();
                var rdt = rdel.Select(dl => dl.Select(Tqa).ToArray()).ToList();
                for (int a = 0; a < 3; a++)
                {
                    var rng = rdt.Select(dl => Max(dl.Select(d => d[a])) - Min(dl.Select(d => d[a]))).ToArray();
                    var g = GroupChannels(rng, nRF);
                    for (int k = 0; k < n.DynR.Count; k++) gidx[k][a] = g[k];
                    for (int gi = 0; gi < nRF; gi++)
                    {
                        var vals = rdt.Where((_, k) => g[k] == gi).SelectMany(dl => dl.Select(d => d[a])).ToList();
                        if (vals.Count == 0) continue;
                        double lo = Min(vals), hi = Max(vals);
                        n.RFactors[gi][a] = (float)lo; n.RFactors[gi][3 + a] = (float)ExtOf(lo, hi, MaxR);
                    }
                }
                rinfo = rinfo.Select((t, k) => Info(t, gidx[k])).ToList();
            }
            s.RRaw = new ushort[Fn][];
            var rfacs = rinfo.Select(ri => new[] { n.RFactors[ri[3]], n.RFactors[ri[4]], n.RFactors[ri[5]] }).ToArray();
            for (int f = 0; f < Fn; f++)
            {
                var row = new ushort[3 * n.DynR.Count];
                for (int k = 0; k < n.DynR.Count; k++)
                {
                    var q = QuantRot(rdel[k][f], rfacs[k]);
                    row[3 * k] = q[0]; row[3 * k + 1] = q[1]; row[3 * k + 2] = q[2];
                }
                s.RRaw[f] = row;
            }
            s.RPad = null;
            s.RInfo = rinfo.Count > 0 ? [.. rinfo, .. Enumerable.Repeat(rinfo[^1], Al4(rinfo.Count) - rinfo.Count)] : [];
            n.Sections.Add(s);
        }
        else { n.TInit = new float[6]; n.TFactors = []; n.RFactors = []; n.SectionFrames = []; n.U14 = 0; }
        if (n.DynT.Count == 0) { n.TFactors = []; n.TInit = T != null ? (float[])T.TInit.Clone() : new float[6]; }
        if (n.DynR.Count == 0) n.RFactors = [];
        // ---- trajectory / root motion
        if (T != null && T.Root == null) { n.Root = null; n.Tail = null; return n; } // Python writes bytes(32) here
        var r = n.Root = new NsaFile.Trajectory { U00 = 2, U04 = 0, U08 = 0, Fps = (float)fps, N = Fn };
        var rp = anim.RootPos; var rr = anim.RootRot;
        var tr = T?.Root;
        if ((tr != null && tr.Pos == null) || (tr == null && Classify(rp, posEps)))
        {
            r.PosFac = tr != null ? (float[])tr.PosFac.Clone() : [(float)rp[0].X, (float)rp[0].Y, (float)rp[0].Z, 0f, 0f, 0f];
            r.Pos = null;
        }
        else
        {
            r.PosFac = tr != null ? (float[])tr.PosFac.Clone() : MakeFactor(rp, MaxP);
            r.Pos = rp.Select(v => { var q = Quantise3(v, r.PosFac, MaxP); return PackPos(q[0], q[1], q[2]); }).ToArray();
        }
        var rt = rr.Select(Tqa).ToArray();
        if ((tr != null && tr.Rot == null) || (tr == null && Classify(rt, rotEps)))
        {
            if (tr != null) r.RotFac = (float[])tr.RotFac.Clone();
            else { var h = Hemi(rr[0]); r.RotFac = [(float)h.X, (float)h.Y, (float)h.Z, (float)h.W, 0f, 0f]; }
            r.Rot = null;
        }
        else
        {
            r.RotFac = tr != null ? (float[])tr.RotFac.Clone() : MakeFactor(rt, MaxR);
            r.Rot = rr.Select(q => QuantRot(q, [r.RotFac, r.RotFac, r.RotFac])).ToArray();
        }
        return n;
    }

    public static byte[] Encode(NsaAnim anim, NsaFile? template = null) => EncodeFile(anim, template).Build();

    static int Al4(int x) => (x + 3) / 4 * 4;
}
