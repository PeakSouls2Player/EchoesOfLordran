using static Lordran.Tools.Formats.Morpheme.MorphemeMath;

namespace Lordran.Tools.Formats.Morpheme;

/// <summary>
/// Decoded animation: per channel, per frame bone-local translation and rotation, plus the trajectory (root motion).
/// Also the encoder's input (<see cref="NsaEncoder.Encode"/>). Channel c drives rig bone anim_to_rig[c].
/// </summary>
public sealed class NsaAnim
{
    public double Fps;
    public double? Duration;             // default (frames - 1) / fps
    public int ChannelCount;
    public V3[][] Pos = [];              // [channel][frame]
    public Q4[][] Rot = [];              // [channel][frame], (w, x, y, z)
    public V3[] RootPos = [];            // [frame] absolute trajectory from the clip start, Morpheme space (Z up, forward -Y)
    public Q4[] RootRot = [];
    public bool[] StaticT = [], StaticR = []; // set by the decoder (which list a channel came from)
    public int Frames => RootPos.Length;
}

/// <summary>
/// NSA dequantisation (session 8 port of data\research\morpheme\nsa_codec.py; NSA-FORMAT.md §1.2-1.4), with the
/// runtime's float32 steps emulated exactly as the Python does:
/// <list type="bullet">
/// <item>static translation = dequant(raw, tFac); static rotation = tqa2q(dequant(raw, rFac));</item>
/// <item>dynamic translation = (tInit.min + tInit.ext·mean) + (tF[f].min + tF[f].ext·raw), packed 11/11/10 bits;</item>
/// <item>dynamic rotation = Qmean · tqa2q(rF[f].min + rF[f].ext·raw), Qmean = tqa2q(mean/128 - 1): the order mean·delta
/// was proven by foot planting (stance-foot speed 0.10 vs 0.80 m/s for the other order on c1030 walks);</item>
/// <item>trajectory: positions 11/11/10 bits with one factor, rotations tqa 3 × u16; a missing array means a constant
/// stored in the factor (rotation as x, y, z, w).</item>
/// </list>
/// </summary>
public static class NsaCodec
{
    static double Deq(int raw, float[] fac, int a) => F32(F32(fac[3 + a] * (double)raw) + fac[a]);

    public static Q4 MeanQuat(byte[] info) => TqaToQuat(new V3(info[0] / 128.0 - 1.0, info[1] / 128.0 - 1.0, info[2] / 128.0 - 1.0));

    public static V3 MeanPos(byte[] info, float[] tInit) => V3.From(a => F32(F32(tInit[3 + a] * (double)info[a]) + tInit[a]));

    public static NsaAnim Decode(NsaFile n)
    {
        int nch = n.ChannelCount;
        int F = n.Sections.Count > 0 ? n.Sections.Sum(s => s.SampleCount) : n.Root?.N ?? 1;
        var c = new NsaAnim
        {
            ChannelCount = nch, Fps = n.Fps, Duration = n.Duration,
            Pos = new V3[nch][], Rot = new Q4[nch][], StaticT = new bool[nch], StaticR = new bool[nch],
        };
        var st = n.Static;
        for (int i = 0; i < n.StaticT.Count; i++)
        {
            int ch = n.StaticT[i]; var raw = st.TRaw[i];
            var v = V3.From(a => Deq(raw[a], st.TFac, a));
            c.Pos[ch] = Enumerable.Repeat(v, F).ToArray(); c.StaticT[ch] = true;
        }
        for (int i = 0; i < n.StaticR.Count; i++)
        {
            int ch = n.StaticR[i]; var raw = st.RRaw[i];
            var q = TqaToQuat(V3.From(a => Deq(raw[a], st.RFac, a)));
            c.Rot[ch] = Enumerable.Repeat(q, F).ToArray(); c.StaticR[ch] = true;
        }
        var dynPos = n.DynT.ToDictionary(ch => ch, _ => new List<V3>());
        var dynRot = n.DynR.ToDictionary(ch => ch, _ => new List<Q4>());
        foreach (var s in n.Sections)
        {
            for (int k = 0; k < n.DynT.Count; k++)
            {
                var inf = s.TInfo[k]; var m = MeanPos(inf, n.TInit);
                var list = dynPos[n.DynT[k]];
                for (int f = 0; f < s.SampleCount; f++)
                {
                    var (rx, ry, rz) = UnpackPos(s.TRaw[f][k]);
                    int[] raw = [rx, ry, rz];
                    list.Add(V3.From(a => F32(Deq(raw[a], n.TFactors[inf[3 + a]], a) + m[a])));
                }
            }
            for (int k = 0; k < n.DynR.Count; k++)
            {
                var inf = s.RInfo[k]; var m = MeanQuat(inf);
                var list = dynRot[n.DynR[k]];
                for (int f = 0; f < s.SampleCount; f++)
                {
                    var raw = s.RRaw[f];
                    var dq = TqaToQuat(V3.From(a => Deq(raw[3 * k + a], n.RFactors[inf[3 + a]], a)));
                    list.Add(Mul(m, dq));
                }
            }
        }
        foreach (var (ch, l) in dynPos) c.Pos[ch] = l.ToArray();
        foreach (var (ch, l) in dynRot) c.Rot[ch] = l.ToArray();
        var r = n.Root;
        if (r == null)
        {
            c.RootPos = Enumerable.Repeat(new V3(0.0, 0.0, 0.0), F).ToArray();
            c.RootRot = Enumerable.Repeat(Q4.Identity, F).ToArray();
            return c;
        }
        if (r.Pos != null)
            c.RootPos = r.Pos.Select(v => { var (x, y, z) = UnpackPos(v); int[] raw = [x, y, z]; return V3.From(a => Deq(raw[a], r.PosFac, a)); }).ToArray();
        else
            c.RootPos = Enumerable.Repeat(new V3(r.PosFac[0], r.PosFac[1], r.PosFac[2]), r.N).ToArray();
        if (r.Rot != null)
            c.RootRot = r.Rot.Select(v => TqaToQuat(V3.From(a => Deq(v[a], r.RotFac, a)))).ToArray();
        else
            c.RootRot = Enumerable.Repeat(new Q4(r.RotFac[3], r.RotFac[0], r.RotFac[1], r.RotFac[2]), r.N).ToArray();
        return c;
    }

    /// <summary>True when both clips decode to exactly the same values (every channel, frame and trajectory sample).</summary>
    public static bool SameFloats(NsaAnim a, NsaAnim b)
    {
        if (a.ChannelCount != b.ChannelCount || a.Frames != b.Frames) return false;
        for (int ch = 0; ch < a.ChannelCount; ch++)
            if (!a.Pos[ch].SequenceEqual(b.Pos[ch]) || !a.Rot[ch].SequenceEqual(b.Rot[ch])) return false;
        return a.RootPos.SequenceEqual(b.RootPos) && a.RootRot.SequenceEqual(b.RootRot);
    }

    /// <summary>Max translation error (m), rotation error (deg), trajectory position / rotation error between two clips.</summary>
    public static (double Pos, double Rot, double RootPos, double RootRot) Compare(NsaAnim a, NsaAnim b)
    {
        double ep = 0, er = 0, rp = 0, rr = 0;
        for (int ch = 0; ch < a.ChannelCount; ch++)
            for (int f = 0; f < a.Frames; f++)
            {
                for (int k = 0; k < 3; k++) ep = Math.Max(ep, Math.Abs(a.Pos[ch][f][k] - b.Pos[ch][f][k]));
                er = Math.Max(er, AngleDeg(a.Rot[ch][f], b.Rot[ch][f]));
            }
        for (int f = 0; f < Math.Min(a.RootPos.Length, b.RootPos.Length); f++)
        {
            for (int k = 0; k < 3; k++) rp = Math.Max(rp, Math.Abs(a.RootPos[f][k] - b.RootPos[f][k]));
            rr = Math.Max(rr, AngleDeg(a.RootRot[f], b.RootRot[f]));
        }
        return (ep, er, rp, rr);
    }
}
