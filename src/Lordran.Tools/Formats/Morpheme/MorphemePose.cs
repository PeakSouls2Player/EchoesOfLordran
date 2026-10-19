using static Lordran.Tools.Formats.Morpheme.MorphemeMath;

namespace Lordran.Tools.Formats.Morpheme;

/// <summary>
/// Forward kinematics of a decoded Morpheme clip on its rig (session 8, track chr step 2: the per-clip foot-planting
/// check of DS1 clips bridged into DS2 NSA, and the role survey of DS1/DS2 clips). Morpheme character space: Z up,
/// forward -Y (NSA-FORMAT.md §1.4/§2.2). A rig bone without an animation channel keeps its bind-pose local transform;
/// channel c drives rig bone anim_to_rig[c]. <see cref="World"/> optionally applies the clip's trajectory (root motion)
/// to the whole pose, which is how the character moves through the world: a stance foot then stands still.
/// </summary>
public sealed class MorphemePose
{
    public readonly List<string> Names;
    public readonly List<int> Parents;
    readonly V3[] bindT; readonly Q4[] bindR;
    readonly int[] rigToChannel;

    public MorphemePose(IReadOnlyList<string> names, IReadOnlyList<int> parents, IReadOnlyList<double[]> bindPos, IReadOnlyList<double[]> bindRot, IReadOnlyList<int> animToRig)
    {
        Names = [.. names]; Parents = [.. parents];
        int n = names.Count;
        bindT = new V3[n]; bindR = new Q4[n];
        for (int i = 0; i < n; i++)
        {
            bindT[i] = new V3(bindPos[i][0], bindPos[i][1], bindPos[i][2]);
            bindR[i] = new Q4(bindRot[i][3], bindRot[i][0], bindRot[i][1], bindRot[i][2]);
        }
        rigToChannel = Enumerable.Repeat(-1, n).ToArray();
        for (int c = 0; c < animToRig.Count; c++) if (animToRig[c] >= 0 && animToRig[c] < n) rigToChannel[animToRig[c]] = c;
    }

    public static MorphemePose From(NmbRig r, NmbChannelMap m) =>
        new(r.Names, r.Parents, r.BindPos.Select(p => new double[] { p[0], p[1], p[2] }).ToList(), r.BindRot.Select(q => new double[] { q[0], q[1], q[2], q[3] }).ToList(), m.AnimToRig);

    public static MorphemePose From(MorphemeSkeleton s) =>
        new(s.Names, s.Parents, s.BindPos, s.BindRot, s.AnimToRig ?? [.. Enumerable.Range(1, s.Names.Count - 1)]);

    public int Index(string name) => Names.FindIndex(x => x == name);

    /// <summary>World (character-space) positions and rotations of every rig bone at frame f; with
    /// <paramref name="trajectory"/> the clip's root motion of that frame is applied on top.</summary>
    public (V3[] Pos, Q4[] Rot) World(NsaAnim a, int f, bool trajectory)
    {
        int n = Names.Count;
        var p = new V3[n]; var q = new Q4[n];
        for (int i = 0; i < n; i++)
        {
            int c = rigToChannel[i];
            V3 t = c >= 0 && c < a.ChannelCount ? a.Pos[c][f] : bindT[i];
            Q4 r = c >= 0 && c < a.ChannelCount ? a.Rot[c][f] : bindR[i];
            int par = Parents[i];
            if (par < 0) { p[i] = t; q[i] = r; }
            else
            {
                var rt = Rotate(q[par], t);
                p[i] = new V3(p[par].X + rt.X, p[par].Y + rt.Y, p[par].Z + rt.Z);
                q[i] = Mul(q[par], r);
            }
        }
        if (trajectory && f < a.RootPos.Length)
        {
            var tp = a.RootPos[f]; var tq = a.RootRot[f];
            for (int i = 0; i < n; i++)
            {
                var rp = Rotate(tq, p[i]);
                p[i] = new V3(tp.X + rp.X, tp.Y + rp.Y, tp.Z + rp.Z);
                q[i] = Mul(tq, q[i]);
            }
        }
        return (p, q);
    }

    /// <summary>Bind-pose world positions (no clip).</summary>
    public V3[] BindWorld()
    {
        int n = Names.Count;
        var p = new V3[n]; var q = new Q4[n];
        for (int i = 0; i < n; i++)
        {
            int par = Parents[i];
            if (par < 0) { p[i] = bindT[i]; q[i] = bindR[i]; continue; }
            var rt = Rotate(q[par], bindT[i]);
            p[i] = new V3(p[par].X + rt.X, p[par].Y + rt.Y, p[par].Z + rt.Z);
            q[i] = Mul(q[par], bindR[i]);
        }
        return p;
    }

    public static double Dist(V3 a, V3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
}
