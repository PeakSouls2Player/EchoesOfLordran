using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Rule S3d (session 9, track skyfix fix pass): DS2's zone fog on distant unlit pieces.
///
/// <para><b>DS1</b> fogs every map piece with the piece's OWN FogBank/ScatterBank rows (FUN_1401c8650 reads the
/// drawable's ids); the collision's rows only light and fog characters. <b>DS2</b> has one fog per filter zone (the
/// camera's) and applies it to every map piece except SkyDome2[NoFog]: <c>Flver_Col[A].fpo</c> (validation\ds2-flver-cola.txt)
/// draws <c>lerp((tex × vc)², FogCol, f)</c> with <c>f = (α (1 − 2^(−0.693149·d·density)))^p</c> (no height falloff) —
/// saturating at α far beyond the zone's fog distance. Rule S3b bakes DS1's own part fog + scattering into distant unlit
/// pieces, so DS2's zone fog then applies a second time: the Asylum cliff zone's black α 0.2 fog took another 20 % off
/// the mountains (review finding).</para>
///
/// <para><b>Rule S3d</b>: for an S3b piece at ≥ <see cref="Sky.FogBakeDistance"/> m from the viewpoint (S3b's own
/// "DS2's zone fog has saturated" assumption) the bake divides out the viewpoint zone's saturated fog. It is applied only
/// where that inversion is exact and independent of the untraced parts of DS2's fog: a BLACK zone fog colour, where the
/// fog only scales (<c>(1 − α) L</c>) and the bake gets × 1/√(1 − α) on DS2's pre-square value. A non-black zone fog adds
/// <c>α × FogCol</c>, whose units (byte/255 used linearly, or squared by CPU code not traced) and exponent p (FogCol.w)
/// are open; its inversion would amplify those by 1/(1 − α) (Firelink α 0.65: × 2.9), so it is left out and logged.
/// The viewpoint zone is the zone of the ordinary DS1 collision under the viewpoint (the bonfire, as rule S3b's), its
/// DS2 fog colour/α as rule A6/H1 writes them (<see cref="Atmosphere.Fog"/>).</para>
/// </summary>
public static class ViewZone
{
    /// <summary>The DS2 fog of the zone under a point: colour (fltparam bytes / 255), α, and a note; null if no ordinary
    /// DS1 collision lies under the point.</summary>
    public sealed record ZoneFog(Vector3 Colour, float Alpha, float Distance, string Note)
    {
        public bool Black => Colour == Vector3.Zero;
        /// <summary>Rule S3d's gain on DS2's pre-square value (1 if the inversion is not exact).</summary>
        public float Gain => Black && Alpha < 0.99f ? 1 / MathF.Sqrt(1 - Alpha) : 1;
    }

    /// <summary>The ordinary (HitFilterID 8) DS1 collision part under a point: the highest triangle from 6 m below to
    /// 0.5 m above it (as <c>atmos-survey spot</c>).</summary>
    public static MSB1.Part.Collision? CollisionUnder(string dsrRoot, string ds1Map, MSB1 msb, Vector3 p)
    {
        string dir = Path.Combine(dsrRoot, "map", ds1Map);
        string bhd = Path.Combine(dir, $"h{ds1Map[1..]}.hkxbhd");
        if (!File.Exists(bhd)) return null;
        var bxf = BXF3.Read(bhd, Path.ChangeExtension(bhd, ".hkxbdt"));
        (float Y, MSB1.Part.Collision Part)? best = null;
        foreach (var part in msb.Parts.Collisions.Where(c => c.HitFilterID == 8))
        {
            var f = bxf.Files.FirstOrDefault(x => Path.GetFileName(x.Name).StartsWith($"h{part.ModelName[1..]}A", StringComparison.OrdinalIgnoreCase));
            if (f == null) continue;
            var m = Commands.ColSurvey.Place(part.Position, part.Rotation, part.Scale);
            foreach (var (a0, b0, c0) in Commands.ColSurvey.Triangles(DCX.Decompress(f.Bytes)))
            {
                var a = Vector3.Transform(a0, m); var b = Vector3.Transform(b0, m); var c = Vector3.Transform(c0, m);
                float d = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
                if (Math.Abs(d) < 1e-6f) continue;
                float u = ((b.Z - c.Z) * (p.X - c.X) + (c.X - b.X) * (p.Z - c.Z)) / d;
                float v = ((c.Z - a.Z) * (p.X - c.X) + (a.X - c.X) * (p.Z - c.Z)) / d;
                if (u < 0 || v < 0 || u + v > 1) continue;
                float y = u * a.Y + v * b.Y + (1 - u - v) * c.Y;
                if (y > p.Y + 0.5f || y < p.Y - 6) continue;
                if (best == null || y > best.Value.Y) best = (y, part);
            }
        }
        return best?.Part;
    }

    /// <summary>The DS2 zone fog at a point (the collision under it → its FogBank/ScatterBank rows → rule A6/H1).</summary>
    public static ZoneFog? FogAt(string dsrRoot, string ds1Map, MSB1 msb, Ds1DrawParam dp, Vector3 p)
    {
        var col = CollisionUnder(dsrRoot, ds1Map, msb, p);
        if (col == null) return null;
        var row = dp.Row("FogBank", col.FogID);
        var (bytes, dist, alpha) = Atmosphere.Fog(row, 1, dp.Row("LightScatteringBank", col.ScatterID));
        var colour = new Vector3(bytes[0], bytes[1], bytes[2]) / 255f;
        return new ZoneFog(colour, alpha, dist, $"viewpoint on {col.Name}: FogBank {col.FogID} '{row?.Name}' → DS2 fog #{bytes[0]:X2}{bytes[1]:X2}{bytes[2]:X2} α {alpha:0.##} {dist:0} m");
    }
}
