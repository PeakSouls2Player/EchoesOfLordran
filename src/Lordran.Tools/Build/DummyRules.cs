using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build;

/// <summary>
/// Session 13 (seam13), rule <b>DM1</b>: no rigid model may carry a "follow the attach bone" dummy.
/// <para>DS2 (DarkSoulsII.exe 1.0.3.0) computes a dummy's matrix in FUN_140b35cd0 (decomp l.2134638): when the runtime dummy's
/// Flag1 (FLVER dummy byte 0x2E → runtime +0x31) is set and its AttachBoneIndex (0x2C → +0x36) is ≥ 0, it multiplies the attach
/// bone's animated matrix by the model's inverse-bind array model+0x198 (no null check). FUN_140b36040 (l.2134811) builds that
/// array only when model+0xB0 is set = some mesh is Dynamic 1/2 (FUN_140b630d0, l.2169148); a rigid-only model leaves it null
/// and the first dummy query on it reads address attach×0x30 = <b>crash exe+0xb35dae</b> (MOVAPS XMM3,[RCX], RCX = 0). Every
/// dummy query walks ALL dummies of the model (FUN_140b37300) before filtering by id, so one such dummy is enough. Vanilla: 0 of
/// 1504 object FLVERs have the pattern. Ours: the Burg's rolling barrel o19_1330 (DS1 o1330: rigid meshes, dummy 1 parent 14 /
/// attach 0 / Flag1) — its TAE SFX event (FUN_1403e59b0 → FUN_1403fc900 → dummy lookup) crashed DS2 ~3 s after m19_41 loaded
/// (minidumps 10/19 14:50-14:52, seam12 stream test).</para>
/// <para>Rule: such a dummy becomes DS2's bone-relative form (ParentBoneIndex = the attach bone, AttachBoneIndex −1, Flag1 off,
/// position/directions in that bone's bind frame), which DS2 evaluates as anim[bone]·local — the same matrix the Flag1 path
/// gives (anim[attach]·invBind[attach]·modelSpace) when the array exists. Skinned models keep DS1's form (the array exists).</para>
/// </summary>
public static class DummyRules
{
    /// <summary>True when DS2 builds the inverse-bind array for the model (a mesh with Dynamic 1 or 2).</summary>
    public static bool HasBindArray(FLVER2 f) => f.Meshes.Any(m => m.Dynamic is 1 or 2);

    /// <summary>The dummies that crash DS2 (rule DM1): Flag1 + AttachBoneIndex ≥ 0 on a model without the inverse-bind array.</summary>
    public static IEnumerable<FLVER.Dummy> Offenders(FLVER2 f) => HasBindArray(f) ? [] : f.Dummies.Where(d => d.Flag1 && d.AttachBoneIndex >= 0);

    /// <summary>Applies rule DM1; returns the number of dummies rewritten.</summary>
    public static int Apply(FLVER2 f)
    {
        var bad = Offenders(f).ToList();
        if (bad.Count == 0) return 0;
        var world = Commands.ChrCompare.World(f);
        foreach (var d in bad)
        {
            int bone = d.AttachBoneIndex < f.Nodes.Count ? d.AttachBoneIndex : 0;
            Matrix4x4.Invert(f.Nodes.Count > 0 ? world[bone] : Matrix4x4.Identity, out var inv);
            var fw = Vector3.TransformNormal(d.Forward, inv); var up = Vector3.TransformNormal(d.Upward, inv);
            d.Position = Vector3.Transform(d.Position, inv);
            d.Forward = fw.LengthSquared() > 1e-12f ? Vector3.Normalize(fw) : new Vector3(0, 0, -1);
            d.Upward = up.LengthSquared() > 1e-12f ? Vector3.Normalize(up) : new Vector3(0, 1, 0);
            d.ParentBoneIndex = (short)bone;
            d.AttachBoneIndex = -1;
            d.Flag1 = false;
        }
        return bad.Count;
    }
}
