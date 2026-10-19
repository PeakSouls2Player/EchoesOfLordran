namespace Lordran.Tools.Textures;

/// <summary>
/// BC6H (DXGI 95 BC6H_UF16) block decoder, session 9 track skyfix: DS1's environment-diffuse cubes (DSR
/// <c>other\envlight.tpf</c> EnvDif_*, <c>map\mAA\GI_EnvM_mAA</c>) are BC6H HDR; Pfim decodes them only to clamped 8 bit.
/// They light DS1's FRPG_NormalToAlpha materials (light shafts, mist sheets: <c>hemisphere + cube(n) × EnvDifMapMulCol</c>),
/// so the NormalToAlpha rule (<see cref="Build.Maps.Ds1Scatter"/>) needs their real values. Written from the D3D11 BC6H
/// specification: 14 modes (field layouts below, LSB-first bit stream), 32 two-region partitions (the BC7 table) with
/// their anchors, endpoint delta transform, unsigned unquantisation <c>((q &lt;&lt; 16) + 0x8000) &gt;&gt; bits</c>, 3/4-bit
/// weights, finish <c>(c × 31) &gt;&gt; 6</c> = half-float bits. Validated against Pfim's clamped decode (sky-check bc6h).
/// </summary>
public static class Bc6h
{
    /// <summary>Per mode: base bits, delta bits (r, g, b), transformed, regions, field layout.</summary>
    sealed record Mode(int Base, int Dr, int Dg, int Db, bool Transformed, int Regions, (int Ep, int Ch, int Lo, int Count, bool Rev)[] Fields);

    static (int, int, int, int, bool)[] F(string spec) => spec.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(f =>
    {
        int ch = "rgb".IndexOf(f[0]), ep = f[1] - '0';
        string bits = f[3..];
        bool rev = bits.EndsWith('r');
        if (rev) bits = bits[..^1];
        var range = bits.Split('-').Select(int.Parse).ToArray();
        int lo = range[0], hi = range.Length > 1 ? range[1] : range[0];
        return (ep, ch, lo, hi - lo + 1, rev);
    }).ToArray();

    static readonly Dictionary<int, Mode> Modes = new()
    {
        [1] = new(10, 5, 5, 5, true, 2, F("g2.4 b2.4 b3.4 r0.0-9 g0.0-9 b0.0-9 r1.0-4 g3.4 g2.0-3 g1.0-4 b3.0 g3.0-3 b1.0-4 b3.1 b2.0-3 r2.0-4 b3.2 r3.0-4 b3.3")),
        [2] = new(7, 6, 6, 6, true, 2, F("g2.5 g3.4 g3.5 r0.0-6 b3.0 b3.1 b2.4 g0.0-6 b2.5 b3.2 g2.4 b0.0-6 b3.3 b3.5 b3.4 r1.0-5 g2.0-3 g1.0-5 g3.0-3 b1.0-5 b2.0-3 r2.0-5 r3.0-5")),
        [3] = new(11, 5, 4, 4, true, 2, F("r0.0-9 g0.0-9 b0.0-9 r1.0-4 r0.10 g2.0-3 g1.0-3 g0.10 b3.0 g3.0-3 b1.0-3 b0.10 b3.1 b2.0-3 r2.0-4 b3.2 r3.0-4 b3.3")),
        [4] = new(11, 4, 5, 4, true, 2, F("r0.0-9 g0.0-9 b0.0-9 r1.0-3 r0.10 g3.4 g2.0-3 g1.0-4 g0.10 g3.0-3 b1.0-3 b0.10 b3.1 b2.0-3 r2.0-3 b3.0 b3.2 r3.0-3 g2.4 b3.3")),
        [5] = new(11, 4, 4, 5, true, 2, F("r0.0-9 g0.0-9 b0.0-9 r1.0-3 r0.10 b2.4 g2.0-3 g1.0-3 g0.10 b3.0 g3.0-3 b1.0-4 b0.10 b2.0-3 r2.0-3 b3.1 b3.2 r3.0-3 b3.4 b3.3")),
        [6] = new(9, 5, 5, 5, true, 2, F("r0.0-8 b2.4 g0.0-8 g2.4 b0.0-8 b3.4 r1.0-4 g3.4 g2.0-3 g1.0-4 b3.0 g3.0-3 b1.0-4 b3.1 b2.0-3 r2.0-4 b3.2 r3.0-4 b3.3")),
        [7] = new(8, 6, 5, 5, true, 2, F("r0.0-7 g3.4 b2.4 g0.0-7 b3.2 g2.4 b0.0-7 b3.3 b3.4 r1.0-5 g2.0-3 g1.0-4 b3.0 g3.0-3 b1.0-4 b3.1 b2.0-3 r2.0-5 r3.0-5")),
        [8] = new(8, 5, 6, 5, true, 2, F("r0.0-7 b3.0 b2.4 g0.0-7 g2.5 g2.4 b0.0-7 g3.5 b3.4 r1.0-4 g3.4 g2.0-3 g1.0-5 g3.0-3 b1.0-4 b3.1 b2.0-3 r2.0-4 b3.2 r3.0-4 b3.3")),
        [9] = new(8, 5, 5, 6, true, 2, F("r0.0-7 b3.1 b2.4 g0.0-7 b2.5 g2.4 b0.0-7 b3.5 b3.4 r1.0-4 g3.4 g2.0-3 g1.0-4 b3.0 g3.0-3 b1.0-5 b2.0-3 r2.0-4 b3.2 r3.0-4 b3.3")),
        [10] = new(6, 6, 6, 6, false, 2, F("r0.0-5 g3.4 b3.0 b3.1 b2.4 g0.0-5 g2.5 b2.5 b3.2 g2.4 b0.0-5 g3.5 b3.3 b3.5 b3.4 r1.0-5 g2.0-3 g1.0-5 g3.0-3 b1.0-5 b2.0-3 r2.0-5 r3.0-5")),
        [11] = new(10, 10, 10, 10, false, 1, F("r0.0-9 g0.0-9 b0.0-9 r1.0-9 g1.0-9 b1.0-9")),
        [12] = new(11, 9, 9, 9, true, 1, F("r0.0-9 g0.0-9 b0.0-9 r1.0-8 r0.10 g1.0-8 g0.10 b1.0-8 b0.10")),
        [13] = new(12, 8, 8, 8, true, 1, F("r0.0-9 g0.0-9 b0.0-9 r1.0-7 r0.10-11r g1.0-7 g0.10-11r b1.0-7 b0.10-11r")),
        [14] = new(16, 4, 4, 4, true, 1, F("r0.0-9 g0.0-9 b0.0-9 r1.0-3 r0.10-15r g1.0-3 g0.10-15r b1.0-3 b0.10-15r")),
    };

    static readonly Dictionary<int, int> FiveBitModes = new() { [2] = 3, [6] = 4, [10] = 5, [14] = 6, [18] = 7, [22] = 8, [26] = 9, [30] = 10, [3] = 11, [7] = 12, [11] = 13, [15] = 14 };

    static readonly ushort[] Partitions =
    [
        0xCCCC, 0x8888, 0xEEEE, 0xECC8, 0xC880, 0xFEEC, 0xFEC8, 0xEC80, 0xC800, 0xFFEC, 0xFE80, 0xE800, 0xFFE8, 0xFF00, 0xFFF0, 0xF000,
        0xF710, 0x008E, 0x7100, 0x08CE, 0x008C, 0x7310, 0x3100, 0x8CCE, 0x088C, 0x3110, 0x6666, 0x366C, 0x17E8, 0x0FF0, 0x718E, 0x399C,
    ];
    static readonly int[] Anchor2 = [15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 2, 8, 2, 2, 8, 8, 15, 2, 8, 2, 2, 8, 8, 2, 2];
    static readonly int[] W3 = [0, 9, 18, 27, 37, 46, 55, 64];
    static readonly int[] W4 = [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

    ref struct Bits(ReadOnlySpan<byte> b)
    {
        readonly ReadOnlySpan<byte> bytes = b;
        int pos;
        public int Read(int n)
        {
            int v = 0;
            for (int i = 0; i < n; i++, pos++) v |= ((bytes[pos >> 3] >> (pos & 7)) & 1) << i;
            return v;
        }
    }

    static int Unquantize(int q, int bits)
    {
        if (bits >= 15) return q;
        if (q == 0) return 0;
        if (q == (1 << bits) - 1) return 0xFFFF;
        return ((q << 16) + 0x8000) >> bits;
    }

    static int SignExtend(int v, int bits) => (v & (1 << (bits - 1))) != 0 ? v - (1 << bits) : v;

    /// <summary>One 16-byte block → 16 RGB texels (linear floats), row-major 4×4.</summary>
    public static void DecodeBlock(ReadOnlySpan<byte> block, Span<System.Numerics.Vector3> texels)
    {
        var bs = new Bits(block);
        int m2 = bs.Read(2);
        int modeNo = m2 switch { 0 => 1, 1 => 2, _ => FiveBitModes.GetValueOrDefault(m2 | (bs.Read(3) << 2), 0) };
        if (modeNo == 0) { texels.Clear(); return; }
        var mode = Modes[modeNo];
        var e = new int[4, 3];
        foreach (var (ep, ch, lo, count, rev) in mode.Fields)
        {
            int v = bs.Read(count);
            if (rev) { int r = 0; for (int i = 0; i < count; i++) r |= ((v >> i) & 1) << (count - 1 - i); v = r; }
            e[ep, ch] |= v << lo;
        }
        int nEp = mode.Regions * 2;
        int[] deltaBits = [mode.Dr, mode.Dg, mode.Db];
        int mask = (1 << mode.Base) - 1;
        for (int ch = 0; ch < 3; ch++)
        {
            if (mode.Transformed)
                for (int i = 1; i < nEp; i++) e[i, ch] = (e[0, ch] + SignExtend(e[i, ch], deltaBits[ch])) & mask;
            for (int i = 0; i < nEp; i++) e[i, ch] = Unquantize(e[i, ch], mode.Base);
        }
        int partition = mode.Regions == 2 ? bs.Read(5) : 0;
        int ib = mode.Regions == 2 ? 3 : 4;
        for (int t = 0; t < 16; t++)
        {
            int region = mode.Regions == 2 ? (Partitions[partition] >> t) & 1 : 0;
            bool anchor = t == 0 || (mode.Regions == 2 && t == Anchor2[partition]);
            int idx = bs.Read(anchor ? ib - 1 : ib);
            int w = ib == 3 ? W3[idx] : W4[idx];
            var c = new float[3];
            for (int ch = 0; ch < 3; ch++)
            {
                int a = e[region * 2, ch], b = e[region * 2 + 1, ch];
                int v = ((64 - w) * a + w * b + 32) >> 6;
                c[ch] = (float)BitConverter.UInt16BitsToHalf((ushort)((v * 31) >> 6));
            }
            texels[t] = new System.Numerics.Vector3(c[0], c[1], c[2]);
        }
    }

    /// <summary>A BC6H_UF16 surface (width × height, the top level at <paramref name="offset"/>) → linear RGB floats.</summary>
    public static System.Numerics.Vector3[] Decode(ReadOnlySpan<byte> data, int width, int height)
    {
        var px = new System.Numerics.Vector3[width * height];
        Span<System.Numerics.Vector3> blk = stackalloc System.Numerics.Vector3[16];
        int bw = Math.Max(1, (width + 3) / 4), bh = Math.Max(1, (height + 3) / 4);
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                DecodeBlock(data.Slice((by * bw + bx) * 16, 16), blk);
                for (int t = 0; t < 16; t++)
                {
                    int x = bx * 4 + (t & 3), y = by * 4 + (t >> 2);
                    if (x < width && y < height) px[y * width + x] = blk[t];
                }
            }
        return px;
    }

    /// <summary>Bytes of one mip chain (BC6H, 16 bytes per 4×4 block) of a <paramref name="size"/>² face with <paramref name="mips"/> levels.</summary>
    public static int FaceBytes(int size, int mips)
    {
        int n = 0;
        for (int m = 0, s = size; m < mips; m++, s = Math.Max(1, s / 2)) n += Math.Max(1, (s + 3) / 4) * Math.Max(1, (s + 3) / 4) * 16;
        return n;
    }
}
