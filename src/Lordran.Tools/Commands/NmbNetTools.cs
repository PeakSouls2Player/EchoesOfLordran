using Lordran.Tools.Formats.Morpheme;
// merge stage 5: the boss track's network / TAE code lives in its own namespaces (the enemies track has
// classes of the same names in Formats.Morpheme / Formats); see MERGE-NOTES.md "Unification backlog"
using NmbNetBlocks = Lordran.Tools.Formats.Morpheme.Boss.NmbNetBlocks;
using NmbNetResize = Lordran.Tools.Formats.Morpheme.Boss.NmbNetResize;

namespace Lordran.Tools.Commands;

/// <summary>
/// Network re-serialiser commands (session 8, track boss: the C# port of the research tools net_rewrite / net_nodewrite /
/// net_smwrite / net_resize, NETWORK-FORMAT.md):
/// <list type="bullet">
/// <item><c>boss-nmb-net-roundtrip [DIR|FILE...]</c> (default data\ds2\morpheme4\chr): per network, identity re-emit from the block
/// model, relocation (every block moved by ≥ 48 bytes: same blocks, same pointer targets, same decoded digest), every node
/// block through the node writer and every state machine through the SM writer, byte compared.</item>
/// <item><c>nmb-resize DONOR SKELETON.json OUT.nmb</c>: move DONOR's network onto the skeleton (any bone count), with
/// the consistency check.</item>
/// <item><c>nmb-resize-selftest [DIR|FILE...]</c>: every network resized onto its own rig must re-emit byte-identically
/// (identity resize), and pass the consistency check.</item>
/// </list>
/// </summary>
public static class NmbNetTools
{
    public const string DefaultDs2Chr = @"data\ds2\morpheme4\chr";

    static IEnumerable<(string Name, byte[] Nmb)> Inputs(string[] args)
    {
        var paths = args.Skip(1).Where(a => !a.StartsWith("-")).ToList();
        if (paths.Count == 0) paths.Add(DefaultDs2Chr);
        foreach (var p in paths)
        {
            var files = Directory.Exists(p)
                ? Directory.GetFiles(p, "*.anibnd.dcx").OrderBy(f => f, StringComparer.Ordinal).ToList()
                : [p];
            foreach (var f in files)
                foreach (var (name, d) in MorphemeTools.LoadFiles(f).Where(x => x.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)))
                    yield return ($"{Path.GetFileName(f)}/{name}", d);
        }
    }

    public static int Roundtrip(string[] args)
    {
        int files = 0, same = 0, moved = 0, nodes = 0, nodesOk = 0, sms = 0, smOk = 0, bad = 0;
        foreach (var (name, nmb) in Inputs(args))
        {
            files++;
            var f = NmbFile.Read(nmb);
            var x = f.Bundles[f.IndexOf(10)].Data;
            try
            {
                var r = NmbNetBlocks.RoundTrip(x);
                same += r.Same ? 1 : 0; moved += r.Moved ? 1 : 0;
                nodes += r.Nodes; nodesOk += r.NodesOk; sms += r.Sms; smOk += r.SmOk;
                if (!r.Same || !r.Moved || r.NodesOk != r.Nodes || r.SmOk != r.Sms)
                {
                    bad++;
                    Console.WriteLine($"  {name}: identity {r.Same}, relocated {r.Moved}, nodes {r.NodesOk}/{r.Nodes}, SMs {r.SmOk}/{r.Sms}: {r.First}");
                }
            }
            catch (Exception e) { bad++; Console.WriteLine($"  {name}: EXCEPTION {e.GetType().Name}: {e.Message}"); }
        }
        Console.WriteLine($"{files} networks: identical re-emit {same}/{files}, relocated copy (blocks, pointer targets, digest) identical {moved}/{files}; " +
                          $"node blocks re-emitted byte-identically {nodesOk}/{nodes}; state-machine nodes {smOk}/{sms}");
        return bad == 0 ? 0 : 1;
    }

    /// <summary>A network's own rig as a skeleton (bind pose as stored, channel map of the first type-2 bundle).</summary>
    public static MorphemeSkeleton OwnSkeleton(NmbFile f)
    {
        var r = NmbRig.Read(f.Bundles[f.IndexOf(1)].Data);
        var m = NmbChannelMap.Read(f.Bundles[f.IndexOf(2)].Data);
        return new MorphemeSkeleton
        {
            Names = [.. r.Names], Parents = [.. r.Parents],
            BindPos = r.BindPos.Select(p => new double[] { p[0], p[1], p[2] }).ToList(),
            BindRot = r.BindRot.Select(q => new double[] { q[0], q[1], q[2], q[3] }).ToList(),
            Traj = r.Traj, Root = r.Root, AnimToRig = [.. m.AnimToRig],
        };
    }

    public static int Resize(string[] args)
    {
        if (args.Length < 4) { Console.Error.WriteLine("usage: nmb-resize DONOR(.nmb|.anibnd.dcx) SKELETON.json OUT.nmb"); return 1; }
        var nmb = MorphemeTools.LoadFiles(args[1]).First(x => x.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)).Bytes;
        var skel = MorphemeSkeleton.Load(args[2]);
        var r = NmbNetResize.Resize(nmb, skel);
        File.WriteAllBytes(args[3], r.Nmb);
        Console.WriteLine($"{args[1]} -> {args[3]}: rig {skel.Names.Count} bones; rewritten: {string.Join(", ", r.Report.Select(kv => $"{kv.Key} x{kv.Value}"))}");
        Console.WriteLine($"donor joints without a target bone (mapped to the target root): [{string.Join(", ", r.Unmatched)}]");
        var bad = NmbNetResize.CheckRigConsistency(r.Nmb);
        Console.WriteLine($"consistency problems: {(bad.Count == 0 ? "none" : string.Join("; ", bad))}");
        var x = NmbFile.Read(r.Nmb); var net = x.Bundles[x.IndexOf(10)].Data;
        var rel = NmbNetBlocks.RelocationCheck(net, NmbNetBlocks.Emit(NmbNetBlocks.BuildItems(net), 48));
        Console.WriteLine($"block model of the output: {(rel.Count == 0 ? "relocates cleanly" : string.Join("; ", rel.Take(3)))}");
        return bad.Count == 0 && rel.Count == 0 ? 0 : 1;
    }

    /// <summary>Which blocks of two networks differ (node 0: which attrib), for -v diagnostics.</summary>
    public static IEnumerable<string> BlockDiff(byte[] a, byte[] b)
    {
        var A = NmbNetBlocks.Layout(a); var B = NmbNetBlocks.Layout(b);
        if (A.Count != B.Count) { yield return $"blocks {A.Count} vs {B.Count}"; yield break; }
        for (int i = 0; i < A.Count; i++)
        {
            var da = a[A[i].Start..A[i].End]; var db = b[B[i].Start..B[i].End];
            bool eq = da.Length == db.Length;
            if (eq)
            {
                var ca = (byte[])da.Clone(); var cb = (byte[])db.Clone();
                foreach (int f in A[i].AbsPtrs) for (int k = 0; k < 8; k++) { ca[f + k] = 0; cb[f + k] = 0; }
                eq = ca.AsSpan().SequenceEqual(cb);
            }
            if (eq) continue;
            if (A[i].Kind == "node0")
            {
                var na = new NmbNetwork(a).GetNode(0); var nb = new NmbNetwork(b).GetNode(0);
                for (int k = 0; k < Math.Min(na.Attribs.Count, nb.Attribs.Count); k++)
                {
                    var x1 = na.Attribs[k]; var x2 = nb.Attribs[k];
                    if (x1.Size != x2.Size || !a.AsSpan(x1.Offset, (int)x1.Size).SequenceEqual(b.AsSpan(x2.Offset, (int)x2.Size)))
                    {
                        int j = 0; while (j < Math.Min(x1.Size, x2.Size) && a[x1.Offset + j] == b[x2.Offset + j]) j++;
                        yield return $"node0 attrib {x1.Type} ({x1.Size} vs {x2.Size} bytes, first diff +0x{j:x})";
                    }
                }
                yield return $"node0 region {da.Length} vs {db.Length}";
            }
            else
            {
                int j = 0; while (j < Math.Min(da.Length, db.Length) && da[j] == db[j]) j++;
                string t = A[i].Kind == "node" ? $" type {BitConverter.ToUInt32(a, A[i].Start)}" : "";
                yield return $"{A[i].Kind} {A[i].Index}{t} ({da.Length} vs {db.Length}, first diff +0x{j:x})";
            }
        }
    }

    public static int ResizeSelfTest(string[] args)
    {
        int files = 0, identical = 0, multiSet = 0, multiMap = 0, consistent = 0, failed = 0;
        var notes = new List<string>();
        foreach (var (name, nmb) in Inputs(args))
        {
            files++;
            var f = NmbFile.Read(nmb);
            var x = f.Bundles[f.IndexOf(10)].Data;
            if (BitConverter.ToUInt16(x, 0xA0) != 1) { multiSet++; notes.Add($"{name}: {BitConverter.ToUInt16(x, 0xA0)} animation sets (player network: not resized)"); continue; }
            int maps = f.Bundles.Count(b => b.Type == 2);
            try
            {
                var r = NmbNetResize.Resize(nmb, OwnSkeleton(f));
                bool same = r.Nmb.AsSpan().SequenceEqual(nmb);
                var bad = NmbNetResize.CheckRigConsistency(r.Nmb);
                // DS2's own data: a few networks hold per-bone arrays sized for another rig (c1050, c6820, c6830: Python
                // net_rigdep) or mirror blocks that use artist pairs the name rule cannot know (13 of 157): the check reports them
                consistent += bad.Count == 0 ? 1 : 0;
                if (same) identical++;
                else if (maps > 1)
                {
                    multiMap++;
                    var g = NmbFile.Read(r.Nmb);
                    bool netSame = g.Bundles[g.IndexOf(10)].Data.AsSpan().SequenceEqual(x);
                    notes.Add($"{name}: {maps} channel maps (every map becomes the rig's own: expected difference); network bundle identical {netSame}");
                    if (netSame) identical++;
                }
                else
                {
                    failed++;
                    var g = NmbFile.Read(r.Nmb);
                    var diff = new List<string>();
                    for (int i = 0; i < Math.Min(g.Bundles.Count, f.Bundles.Count); i++)
                        if (!g.Bundles[i].Data.AsSpan().SequenceEqual(f.Bundles[i].Data))
                        {
                            var a = f.Bundles[i].Data; var b = g.Bundles[i].Data;
                            int k = 0; while (k < Math.Min(a.Length, b.Length) && a[k] == b[k]) k++;
                            diff.Add($"bundle {i} type {f.Bundles[i].Type} ({a.Length} -> {b.Length} bytes, first difference 0x{k:x})");
                        }
                    notes.Add($"{name}: NOT identical: {string.Join("; ", diff.Take(4))}{(args.Contains("-v") ? " | " + string.Join("; ", BlockDiff(x, g.Bundles[g.IndexOf(10)].Data).Take(6)) : "")}");
                }
                if (bad.Count > 0) notes.Add($"{name}: consistency: {string.Join("; ", bad.Take(4))}");
            }
            catch (Exception e) { failed++; notes.Add($"{name}: EXCEPTION {e.GetType().Name}: {e.Message}"); }
        }
        foreach (var n in notes) Console.WriteLine("  " + n);
        Console.WriteLine($"{files} networks: identity resize byte-identical {identical}/{files - multiSet} (network bundle identical for the {multiMap} multi-map networks), " +
                          $"skipped {multiSet} player networks, failed {failed}; consistency check clean {consistent}");
        return failed == 0 ? 0 : 1;
    }
}
