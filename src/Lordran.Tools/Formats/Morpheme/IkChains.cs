using System.Buffers.Binary;

namespace Lordran.Tools.Formats.Morpheme;

/// <summary>
/// Session 11 (crash11), rule <b>IK1</b>: the bone chains DS2's Morpheme uneven-terrain IK walks must be real parent chains
/// of the network's rig.
/// <para>Crash (Firelink travel, APPCRASH c0000005 at exe+0x679119, twice): FUN_140678e30 (foot-joint pose setup, called per
/// limb by FUN_14066f4a0 = the uneven-terrain leg solve of TaskBasic/PredictiveUnevenTerrainIKSetup) walks every foot joint
/// i ≥ 1 up the rig's parent array until it meets foot joint i-1, with no bound: if joint i is not a strict descendant of
/// joint i-1 the walk passes the root, reads parent -1 and tests the pose's channel bit set at index 0xFFFFFFFF (address
/// +512 MB → access violation). The dump's chain was [8, 9, 9], count 3 = DS1 c2900's L_Foot, L_Toe0, L_Toe0 in c2901/c2902:
/// NmbNetResize (session 8) maps the template's L_Toe0Nub (absent from DS1's skeleton) to its nearest matched ancestor
/// L_Toe0 - right for the lock-foot chain it was made for, but it leaves BASIC_UNEVEN_TERRAIN_CHAIN with ball == toe and
/// 3 foot joints. The chain was there since s5; it became fatal when Ds1NetRules G1 (enemy11) rewired the node's blend
/// weight from the engine-driven UnevenTerrain_BlendWeight to the constant PlaySpeed_0 and the solve started running on
/// the skeletons.</para>
/// <para>Rule: after any rig change of a network, each BASIC_UNEVEN_TERRAIN_CHAIN limb keeps the longest prefix of its foot
/// joints [ankle, ball, toe] in which each joint lies strictly below the previous (count ≥ 1). DS1's skeleton foot = ankle +
/// toe → 2 joints (ankle, ball), the toe nub dropped: the solve then aligns the foot to the ground on the ball joint like a
/// vanilla two-joint foot. A leg (hip ← knee ← ankle) that is not a parent chain cannot be repaired here (logged; the
/// pack check ik-chain-check fails on it).</para>
/// Layout (checked on all 173 vanilla networks, `lr ik-chain-check data\ds2\morpheme4\chr`): attrib 73 limb l at
/// +0x70 + 0x30·l: hip, knee, ankle, ball, toe (u32 rig indices), +0x14 foot-joint count (1..3; vanilla always 3).
/// </summary>
public static class IkChains
{
    public const int TerrainLimb0 = 0x70, TerrainLimbStride = 0x30, TerrainCount = 0x14;

    /// <summary>True when <paramref name="c"/> is a strict descendant of <paramref name="a"/> in <paramref name="parents"/>.</summary>
    public static bool Below(IReadOnlyList<int> parents, int a, int c)
    {
        int n = parents.Count;
        if (a < 0 || a >= n || c < 0 || c >= n || a == c) return false;
        for (int k = 0, p = parents[c]; k <= n; k++)
        {
            if (p == a) return true;
            if (p < 0 || p >= n) return false;
            p = parents[p];
        }
        return false;
    }

    /// <summary>Length of the longest prefix of <paramref name="joints"/> (at most <paramref name="count"/>) that is a strict
    /// descending parent chain (≥ 1 when joints[0] is a rig bone, else 0).</summary>
    public static int ValidPrefix(IReadOnlyList<int> parents, IReadOnlyList<int> joints, int count)
    {
        count = Math.Min(count, joints.Count);
        if (count <= 0 || joints[0] < 0 || joints[0] >= parents.Count) return 0;
        int k = 1;
        while (k < count && Below(parents, joints[k - 1], joints[k])) k++;
        return k;
    }

    /// <summary>Rule IK1 on a network bundle in place: every uneven-terrain limb's foot-joint count is cut to its valid
    /// prefix. Returns report lines (empty when nothing changed and nothing is wrong).</summary>
    public static List<string> RepairUnevenTerrain(byte[] net, IReadOnlyList<int> parents, IReadOnlyList<string>? names = null)
    {
        var rep = new List<string>();
        string B(int b) => names != null && b >= 0 && b < names.Count ? names[b] : b.ToString();
        foreach (var (o, t) in NmbRigReplace.ScanAttribs(net, 0x10000))
        {
            if (t != 73) continue;
            for (int limb = 0; limb < 2; limb++)
            {
                int b = o + TerrainLimb0 + TerrainLimbStride * limb;
                if (b + TerrainCount + 4 > net.Length) break;
                int[] j = Enumerable.Range(0, 5).Select(k => NmbFile.I32(net, b + 4 * k)).ToArray();
                int count = NmbFile.I32(net, b + TerrainCount);
                if (ValidPrefix(parents, j[..3], 3) < 3)
                    rep.Add($"IK1 WARNING uneven-terrain @0x{b:x} limb {limb}: leg {B(j[0])} > {B(j[1])} > {B(j[2])} is not a parent chain (not repairable here)");
                int keep = ValidPrefix(parents, j[2..], Math.Clamp(count, 1, 3));
                if (keep >= 1 && keep != count)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(net.AsSpan(b + TerrainCount), keep);
                    rep.Add($"IK1 uneven-terrain @0x{b:x} limb {limb}: foot joints {string.Join(" > ", j[2..5].Take(Math.Max(count, 0)).Select(B))} → {keep} ({string.Join(" > ", j[2..5].Take(keep).Select(B))})");
                }
            }
        }
        return rep;
    }

    /// <summary>Rule IK1 on an NMB file's bytes (its first rig); returns the new bytes (unchanged when nothing to do).</summary>
    public static byte[] RepairNmb(byte[] nmbBytes, List<string> log)
    {
        var nmb = NmbFile.Read(nmbBytes);
        int ri = nmb.IndexOf(1), ni = nmb.IndexOf(10);
        if (ri < 0 || ni < 0) return nmbBytes;
        var rig = NmbRig.Read(nmb.Bundles[ri].Data);
        var net = (byte[])nmb.Bundles[ni].Data.Clone();
        var rep = RepairUnevenTerrain(net, rig.Parents, rig.Names);
        log.AddRange(rep);
        if (!rep.Any(l => !l.Contains("WARNING"))) return nmbBytes;
        return nmb.Rebuild(new Dictionary<int, byte[]> { [ni] = net });
    }
}
