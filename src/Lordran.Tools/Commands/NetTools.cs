using System.Buffers.Binary;
using System.Security.Cryptography;
using Lordran.Tools.Formats.Morpheme;

namespace Lordran.Tools.Commands;

/// <summary>
/// Network re-serialiser checks (session 8, track enemies step 1; C# port of net_rewrite / net_nodewrite / net_smwrite /
/// net_resize, see Formats/Morpheme/NmbNetEdit.cs and NmbNetResize.cs).
/// <list type="bullet">
/// <item><c>nmb-net-roundtrip DIR|FILE... [--pad N]</c>: for every NMB network (1) the block model re-emits byte-identically,
/// (2) a copy with every block moved by ≥ N bytes decodes to the same content (semantic digest), (3) every node block and
/// (4) every state-machine node block re-emits byte-identically from its model.</item>
/// <item><c>nmb-net-resize DONOR SKELETON.json OUT.nmb</c>: move a network onto a skeleton with another bone count, then run the
/// rig-consistency check.</item>
/// </list>
/// </summary>
public static class NetTools
{
    static IEnumerable<string> Inputs(IEnumerable<string> args)
    {
        foreach (var p in args)
        {
            if (Directory.Exists(p))
                foreach (var f in Directory.GetFiles(p, "*", SearchOption.TopDirectoryOnly).Where(f => f.EndsWith(".anibnd.dcx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.Ordinal))
                    yield return f;
            else yield return p;
        }
    }

    public static byte[] NmbOf(string path) => MorphemeTools.LoadFiles(path).First(f => f.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)).Bytes;

    /// <summary>Everything a decoder sees in a network bundle, positions excluded (net_rewrite.semantic_digest).</summary>
    public static string SemanticDigest(byte[] x)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        void Put(string s) => h.AppendData(System.Text.Encoding.UTF8.GetBytes(s + "\n"));
        void PutB(ReadOnlySpan<byte> b) => h.AppendData(b);
        var net = new NmbNetwork(x);
        Put($"nn {net.NodeCount} sets {BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(0xA0))} bones {net.U32(0x118)} cps {net.U32(0x11C)}");
        foreach (int hp in new[] { 0xC8, 0xD0, 0xD8 })
            foreach (var (id, nm, _) in NmbNetEdit.StringTable(x, (int)net.U64(hp))) Put($"{hp:X} {id} {nm}");
        int nm2 = (int)net.U32(0xF8); int pM = (int)net.U64(0x100);
        for (int i = 0; i < nm2; i++)
        {
            int p = (int)net.U64(pM + 8 * i);
            int cnt = (int)net.U32(p + 8); int p1 = (int)net.U64(p + 0x10);
            Put($"dist {net.U32(p)} {net.U32(p + 4)} [{string.Join(",", Enumerable.Range(0, cnt).Select(k => net.U16(p + p1 + 2 * k)))}]");
        }
        foreach (var n in net.Nodes())
        {
            int cpn = x[n.Offset + 0x0E]; long pCP = (long)net.U64(n.Offset + 0x28);
            var cps = cpn > 0 && pCP != 0 ? string.Join(",", Enumerable.Range(0, cpn).Select(k => $"{net.U16(n.Offset + (int)pCP + 4 * k)}:{net.U16(n.Offset + (int)pCP + 4 * k + 2)}")) : "";
            Put($"node {n.Index} t{n.Type} f{net.U16(n.Offset + 4)} p{n.Parent} [{string.Join(",", n.Children)}] cp[{cps}] at[{string.Join(",", n.Attribs.Select(a => $"{a.Type}/{a.Size}"))}]");
            foreach (var a in n.Attribs) PutB(x.AsSpan(a.Offset, (int)Math.Min(a.Size, x.Length - a.Offset)));
            if (n.Type == 10)
            {
                var sa = n.Attribs.First(a => a.Type == 38);
                var sm = NmbNetEdit.ReadSmDef(x, sa.Offset);
                Put($"sm d{sm.Default} " + string.Join(";", sm.States.Append(sm.Glob).Select(s => $"{s.Node},{s.Src},{s.Dst},{s.U20},[{string.Join(",", s.Entry)}],[{string.Join(",", s.ExitConds)}],[{string.Join(",", s.ExitTrans)}]")));
                foreach (var c in sm.Conds) PutB(c);
            }
        }
        foreach (int hp in new[] { 0xB0, 0xB8, 0xC0 })
        {
            int o = (int)net.U64(hp); int cnt = (int)net.U32(o); int p = (int)net.U64(o + 8);
            PutB(x.AsSpan(o + p, 2 * cnt));
        }
        foreach (int hp in new[] { 0x130, 0x138 })
        {
            int o = (int)net.U64(hp); int p = (int)net.U64(o);
            PutB(x.AsSpan(p, 4 * (int)net.U32(0x118)));
        }
        return Convert.ToHexString(h.GetHashAndReset());
    }

    public static int NetRoundtrip(string[] args)
    {
        int pad = args.SkipWhile(a => a != "--pad").Skip(1).Select(int.Parse).FirstOrDefault(48);
        var pos = args.Skip(1).Where((a, i) => !a.StartsWith("--") && (i == 0 || args[i] != "--pad")).ToList();
        int files = 0, same = 0, moved = 0, nodes = 0, nodeOk = 0, sms = 0, smOk = 0;
        foreach (var path in Inputs(pos))
        {
            byte[] nmb = NmbOf(path);
            var f = NmbFile.Read(nmb);
            byte[] x = f.Bundles[f.IndexOf(10)].Data;
            files++;
            bool s = NmbNetEdit.Emit(NmbNetEdit.BuildItems(x)).AsSpan().SequenceEqual(x);
            same += s ? 1 : 0;
            var mv = NmbNetEdit.EmitPadded(x, pad);
            bool m = mv.Length > x.Length && SemanticDigest(mv) == SemanticDigest(x);
            moved += m ? 1 : 0;
            var ends = NmbNetEdit.NodeEnds(x);
            var net = new NmbNetwork(x);
            int bad = 0, badSm = 0; string first = "";
            for (int i = 1; i < net.NodeCount; i++)
            {
                var n = net.GetNode(i);
                int p = n.Offset, end = ends[i];
                byte[] w = NmbNetEdit.WriteNode(NmbNetEdit.ReadNode(x, p, end));
                bool ok = OkWithPad(w, x, p, end);
                nodes++; nodeOk += ok ? 1 : 0;
                if (!ok) { bad++; if (first == "") first = $"node {i} type {n.Type}"; }
                if (n.Type == 10)
                {
                    sms++;
                    byte[] ws = NmbNetEdit.WriteSmNode(NmbNetEdit.ReadSmNode(x, p, end));
                    bool oks = ws.AsSpan().SequenceEqual(x.AsSpan(p, end - p));
                    smOk += oks ? 1 : 0;
                    if (!oks) { badSm++; if (first == "") first = $"SM node {i}"; }
                }
            }
            if (!s || !m || bad > 0 || badSm > 0) Console.WriteLine($"  {Path.GetFileName(path)}: re-emit {(s ? "same" : "DIFF")}, relocated {(m ? "same content" : "DIFFERENT")}, node blocks bad {bad}, SM blocks bad {badSm} {first}");
        }
        Console.WriteLine($"{files} networks: block model re-emit byte-identical {same}/{files}; relocated copy (every block moved by >= {pad} bytes) decodes identically {moved}/{files}");
        Console.WriteLine($"  node blocks re-emitted byte-identically {nodeOk}/{nodes}; state-machine node blocks {smOk}/{sms}");
        return same == files && moved == files && nodeOk == nodes && smOk == sms ? 0 : 2;
    }

    /// <summary><c>nmb-net-resize DONOR SKELETON.json OUT.nmb</c> (net_resize.py main).</summary>
    public static int NetResize(string[] args)
    {
        if (args.Length < 4) { Console.Error.WriteLine("usage: nmb-net-resize DONOR(.nmb|.anibnd.dcx) SKELETON.json OUT.nmb"); return 1; }
        var skel = MorphemeSkeleton.Load(args[2]);
        var rep = new NmbNetResize.Report();
        var nmb = NmbNetResize.Resize(NmbOf(args[1]), skel, rep);
        File.WriteAllBytes(args[3], nmb);
        Console.WriteLine($"{args[1]} -> {args[3]}: rig {skel.Names.Count} bones; rewritten: {rep}");
        Console.WriteLine("donor joints without a target bone (mapped to the target root): [" + string.Join(", ", rep.UnmatchedDonorJoints) + "]");
        var bad = NmbNetResize.CheckRigConsistency(nmb);
        Console.WriteLine("consistency problems: " + (bad.Count == 0 ? "none" : string.Join("; ", bad)));
        var f = NmbFile.Read(nmb);
        Console.WriteLine($"semantic re-emit: block model {(NmbNetEdit.Emit(NmbNetEdit.BuildItems(f.Bundles[f.IndexOf(10)].Data)).AsSpan().SequenceEqual(f.Bundles[f.IndexOf(10)].Data) ? "identical" : "DIFFERS")}");
        return bad.Count == 0 ? 0 : 2;
    }

    /// <summary><c>nmb-net-clone NMB TEMPLATE_SM VALUE OUT.nmb [--parent SM]</c>: clone one SM_Attack category (net_clone.py
    /// add_category without a clip map), for the C#/Python comparison.</summary>
    public static int NetClone(string[] args)
    {
        var ed = new NmbNetClone(NmbOf(args[1]));
        int? parent = args.SkipWhile(a => a != "--parent").Skip(1).Select(int.Parse).Cast<int?>().FirstOrDefault();
        var r = ed.AddCategory(int.Parse(args[2]), float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture), parent);
        File.WriteAllBytes(args[4], ed.Build());
        Console.WriteLine($"cloned {r.IdMap.Count} nodes ({r.IdMap.Values.Min()}..{r.IdMap.Values.Max()}) + transition {r.NewTransition}; new category SM {r.NewSm}");
        return 0;
    }

    /// <summary><c>tae-write-roundtrip DIR|FILE...</c>: (1) <see cref="Formats.Ds2TaeFile"/> re-emits every TAE byte for byte;
    /// (2) with one extra animation inserted after the first (every later record and blob moves) SoulsFormats reads the same
    /// events (type, times, parameters) for every original animation and the new one with its events.</summary>
    public static int TaeWriteRoundtrip(string[] args)
    {
        var files = args.Skip(1).SelectMany(p => Directory.Exists(p) ? Directory.GetFiles(p, "*.tae").OrderBy(f => f, StringComparer.Ordinal).ToArray() : new[] { p }).ToList();
        int same = 0, moved = 0;
        foreach (var f in files)
        {
            var b = File.ReadAllBytes(f);
            var t = Formats.Ds2TaeFile.Read(b);
            bool s = t.Write().AsSpan().SequenceEqual(b);
            same += s ? 1 : 0;
            // shift test
            // the first free id after the smallest one: inserted near the start, so almost every record and blob moves
            var ids = t.Anims.Select(a => a.Id).ToHashSet();
            long id = t.Anims.Min(a => a.Id) + 1;
            while (ids.Contains(id)) id++;
            bool m = false;
            string why = "";
            if (id >= 0)
            {
                var p2 = t.ParamTemplate(280100) ?? new byte[32];
                t.AddAnimation(id, 45, [new(280100, 0f, 0.5f, p2), new(280100, 0.5f, 1.5f, p2)]);
                var w = t.Write();
                try
                {
                    var a0 = SoulsFormats.TAE.Read(b); var a1 = SoulsFormats.TAE.Read(w);
                    m = a1.Animations.Count == a0.Animations.Count + 1;
                    foreach (var an in a0.Animations)
                    {
                        var bn = a1.Animations.First(x => x.ID == an.ID);
                        if (bn.Events.Count != an.Events.Count) { m = false; why = $"anim {an.ID} events"; break; }
                        for (int k = 0; k < an.Events.Count; k++)
                        {
                            var e0 = an.Events[k]; var e1 = bn.Events[k];
                            if (e0.Type != e1.Type || e0.StartTime != e1.StartTime || e0.EndTime != e1.EndTime || !e0.GetParameterBytes(false).AsSpan().SequenceEqual(e1.GetParameterBytes(false))) { m = false; why = $"anim {an.ID} event {k}"; break; }
                        }
                        if (!m) break;
                    }
                    var na = a1.Animations.First(x => x.ID == id);
                    if (na.Events.Count != 2 || na.Events[1].EndTime != 1.5f) { m = false; why = "new anim"; }
                }
                catch (Exception e) { m = false; why = e.Message; }
            }
            moved += m ? 1 : 0;
            if (!s || !m) Console.WriteLine($"  {Path.GetFileName(f)}: re-emit {(s ? "same" : "DIFF")}, shifted copy {(m ? "same events" : "DIFFERENT " + why)}");
        }
        Console.WriteLine($"{files.Count} TAE files: re-emit byte-identical {same}/{files.Count}; with an animation inserted (all blocks moved) SoulsFormats reads identical events {moved}/{files.Count}");
        return same == files.Count && moved == files.Count ? 0 : 2;
    }

    /// <summary><c>loop-feet CHR CLIP...</c>: the plant runs and lock windows of both ball joints on DS1 locomotion clips
    /// (diagnostics for rule C11b's L R L R touchdown check).</summary>
    public static int LoopFeet(string[] args)
    {
        var src = Build.Chr.Ds1ChrSource.Load(Program.DefaultDsrRoot, args[1]);
        var rig = Build.Chr.Ds1RigAnims.WithDs2Names(src.Bridge(src.ClipIds.First()).Skeleton);
        var pose = MorphemePose.From(rig);
        foreach (var c in args.Skip(2).Select(int.Parse))
        {
            var a = src.Bridge(c).Anim;
            int P = a.Frames - 1;
            foreach (var j in new[] { Build.Chr.Ds1RigAnims.LockJointL, Build.Chr.Ds1RigAnims.LockJointR })
            {
                var p = Build.Chr.Ds1RigAnims.JointPath(a, pose, pose.Index(j), true);
                double zmin = Enumerable.Range(0, P + 1).Min(f => p[f].Z);
                var speeds = Enumerable.Range(0, P).Select(f => MorphemePose.Dist(p[P + f - 1 < p.Length ? P + f - 1 : f], p[P + f + 1 < p.Length ? P + f + 1 : f]) * a.Fps / 2).ToList();
                var runs = Build.Chr.Ds1RigAnims.PlantRuns(p, P, true, a.Fps);
                Console.WriteLine($"{Build.Chr.Ds1ChrSource.ClipName(c)} {j}: {P} frames, z {zmin:0.000}..{Enumerable.Range(0, P + 1).Max(f => p[f].Z):0.000}, min speed {speeds.Min():0.00} m/s; plant runs [{string.Join(" ", runs.Select(r => $"{r.A}-{r.B}"))}]");
            }
        }
        return 0;
    }

    /// <summary><c>ds1-skeleton CHR OUT.json</c>: the bridged DS1 rig of a DSR character with DS2 bone names (the rig the 1:1
    /// pipeline puts into its network), its bone list, and the DS2 networks with the same bone count.</summary>
    public static int Ds1Skeleton(string[] args)
    {
        var src = Build.Chr.Ds1ChrSource.Load(Program.DefaultDsrRoot, args[1]);
        var rig = Build.Chr.Ds1RigAnims.WithDs2Names(src.Bridge(src.ClipIds.First()).Skeleton);
        if (args.Length > 2) rig.Save(args[2]);
        Console.WriteLine($"{args[1]}: {src.Skeleton.Names.Count} DS1 bones -> rig {rig.Names.Count} bones: " + string.Join(", ", rig.Names));
        return 0;
    }

    /// <summary>A re-emitted node block equals the original, which may carry 0xCD padding up to the next block.</summary>
    static bool OkWithPad(byte[] w, byte[] x, int p, int end)
    {
        if (w.Length > end - p) return false;
        if (!w.AsSpan().SequenceEqual(x.AsSpan(p, w.Length))) return false;
        for (int i = p + w.Length; i < end; i++) if (x[i] != 0xCD) return false;
        return true;
    }
}
