namespace Lordran.Tools.Formats.Morpheme;

/// <summary>Double-precision 3-vector (bone-local translation, tan-quarter-angle vector).</summary>
public readonly record struct V3(double X, double Y, double Z)
{
    public double this[int a] => a == 0 ? X : a == 1 ? Y : Z;
    public static V3 From(Func<int, double> f) => new(f(0), f(1), f(2));
}

/// <summary>Double-precision quaternion, (w, x, y, z) order like the research notes (NSA-FORMAT.md).</summary>
public readonly record struct Q4(double W, double X, double Y, double Z)
{
    public static readonly Q4 Identity = new(1.0, 0.0, 0.0, 0.0);
    public double this[int i] => i switch { 0 => W, 1 => X, 2 => Y, _ => Z };
    public Q4 Neg() => new(-W, -X, -Y, -Z);
}

/// <summary>
/// The float math of the Morpheme NSA pipeline, written so the C# port gives the same bits as the session-8 Python
/// research code (data\research\morpheme\nsa_codec.py / nsa_encode.py), which was validated byte-for-byte on 1552 DS2
/// clips. Rules that matter for bit-exactness (session 8):
/// <list type="bullet">
/// <item>All intermediate math is IEEE double; <see cref="F32"/> rounds to float32 where the runtime (and the Python
/// emulation <c>struct.pack('&lt;f')</c>) does: dequantisation <c>f32(f32(ext·r) + min)</c>, factor extents.</item>
/// <item>Python 3.12+ <c>sum()</c> of floats is Neumaier-compensated (checked on 3.14.2: <c>sum([0.1]*10) == 1.0</c>);
/// <see cref="Sum"/> reproduces it (means, quaternion norms, dot products).</item>
/// <item>Python <c>min</c>/<c>max</c> keep the first of equal values (so -0.0 vs 0.0 keeps the first seen, which is
/// written to the file's sign bit); <see cref="Min"/>/<see cref="Max"/> do the same (Math.Min would prefer -0.0).</item>
/// <item>Products are evaluated left to right exactly as written in the Python (no FMA contraction in RyuJIT).</item>
/// </list>
/// </summary>
public static class MorphemeMath
{
    public static double F32(double x) => (float)x;

    /// <summary>Python 3.12+ <c>sum(iterable)</c> over floats: Neumaier's improved Kahan-Babuska summation (CPython
    /// <c>cs_add</c>/<c>cs_to_double</c>): the compensation is added at the end only if it is non-zero and finite.</summary>
    public static double Sum(IEnumerable<double> xs)
    {
        double hi = 0.0, lo = 0.0;
        bool first = true;
        foreach (double x in xs)
        {
            if (first) { hi = 0.0 + x; first = false; continue; } // int 0 + float: exact, as CPython's int fast path
            double t = hi + x;
            if (Math.Abs(hi) >= Math.Abs(x)) lo += (hi - t) + x;
            else lo += (x - t) + hi;
            hi = t;
        }
        if (first) return 0.0;
        if (lo != 0.0 && double.IsFinite(lo)) return hi + lo;
        return hi;
    }

    /// <summary>Python min(): the first element unless a later one is strictly smaller.</summary>
    public static double Min(IEnumerable<double> xs)
    {
        bool any = false; double m = 0;
        foreach (double x in xs) { if (!any || x < m) m = x; any = true; }
        if (!any) throw new InvalidOperationException("min() of an empty sequence");
        return m;
    }

    /// <summary>Python max(): the first element unless a later one is strictly larger.</summary>
    public static double Max(IEnumerable<double> xs)
    {
        bool any = false; double m = 0;
        foreach (double x in xs) { if (!any || x > m) m = x; any = true; }
        if (!any) throw new InvalidOperationException("max() of an empty sequence");
        return m;
    }

    /// <summary>tan-quarter-angle rotation vector to unit quaternion: m = |v|², q = ((1-m)/(1+m), 2v/(1+m)).</summary>
    public static Q4 TqaToQuat(V3 v)
    {
        double m = v.X * v.X + v.Y * v.Y + v.Z * v.Z;
        double s = 2.0 / (1.0 + m);
        return new Q4((1.0 - m) / (1.0 + m), s * v.X, s * v.Y, s * v.Z);
    }

    /// <summary>Unit quaternion to tan-quarter-angle vector q.xyz / (1 + q.w) (the caller picks the hemisphere).</summary>
    public static V3 QuatToTqa(Q4 q)
    {
        double d = 1.0 + q.W;
        return new V3(q.X / d, q.Y / d, q.Z / d);
    }

    public static Q4 Mul(Q4 a, Q4 b) => new(
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z,
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y + a.Y * b.W + a.Z * b.X - a.X * b.Z,
        a.W * b.Z + a.Z * b.W + a.X * b.Y - a.Y * b.X);

    public static Q4 Conj(Q4 q) => new(q.W, -q.X, -q.Y, -q.Z);

    public static double Dot(Q4 a, Q4 b) => Sum([a.W * b.W, a.X * b.X, a.Y * b.Y, a.Z * b.Z]);

    public static Q4 Normalize(Q4 q)
    {
        double l = Math.Sqrt(Sum([q.W * q.W, q.X * q.X, q.Y * q.Y, q.Z * q.Z]));
        return new Q4(q.W / l, q.X / l, q.Y / l, q.Z / l);
    }

    /// <summary>Angle between two rotations in degrees (sign-insensitive), for reports.</summary>
    public static double AngleDeg(Q4 a, Q4 b)
    {
        double d = Math.Abs(Dot(a, b));
        return 2 * (180.0 / Math.PI) * Math.Acos(Math.Min(1.0, d));
    }

    /// <summary>Rotate v by unit quaternion q (q v q*).</summary>
    public static V3 Rotate(Q4 q, V3 v)
    {
        var p = Mul(Mul(q, new Q4(0.0, v.X, v.Y, v.Z)), Conj(q));
        return new V3(p.X, p.Y, p.Z);
    }

    /// <summary>q if w ≥ 0 else -q (Python <c>hemi</c>; -0.0 counts as ≥ 0).</summary>
    public static Q4 Hemi(Q4 q) => q.W >= 0 ? q : q.Neg();

    /// <summary>Dynamic/trajectory translation: 11/11/10 bits packed into a u32 (x &lt;&lt; 21 | y &lt;&lt; 10 | z).</summary>
    public static (int X, int Y, int Z) UnpackPos(uint v) => ((int)((v >> 21) & 0x7FF), (int)((v >> 10) & 0x7FF), (int)(v & 0x3FF));
    public static uint PackPos(int x, int y, int z) => ((uint)x << 21) | ((uint)y << 10) | (uint)z;
}
