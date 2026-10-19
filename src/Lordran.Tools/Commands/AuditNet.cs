using System.Globalization;
using Lordran.Tools.Formats.Morpheme;

namespace Lordran.Tools.Commands;

/// <summary>enemy-audit (animations): audit-net ANIBND|NMB [--tree N [depth]] - every network node with type, parent,
/// children, CP inputs, attribs; per-bone alphas (87) and bone lists (8) and head-look joints (25) by bone name; SourceAnim
/// clip, duration, fractions, flags; state machines with their states and decoded transition conditions (session 11, track
/// enemy11). --tree: the subtree under node N (types, names, clips) only. Read-only.</summary>
public static class AuditNet
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static int U16(byte[] d, int o) => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o));
    public static int Run(string[] args)
    {
        var f = MorphemeTools.LoadFiles(args[1]).First(x => x.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase));
        var nmb = NmbFile.Read(f.Bytes);
        var rig = NmbRig.Read(nmb.Bundles[nmb.IndexOf(1)].Data);
        var x = nmb.Bundles[nmb.IndexOf(10)].Data;
        var net = new NmbNetwork(x);
        var names = NmbNetEdit.NodeNames(x);
        var files = NmbFileNamesFull.Read(nmb.Bundles[nmb.IndexOf(13)].Data).Files;
        string B(int b) => b >= 0 && b < rig.Names.Count ? rig.Names[b] : $"#{b}";
        int ti = Array.IndexOf(args, "--tree");
        if (ti > 0)
        {
            int root = int.Parse(args[ti + 1]);
            int maxDepth = args.Length > ti + 2 && int.TryParse(args[ti + 2], out int md) ? md : 12;
            var seen = new HashSet<int>();
            void Rec(int i, int depth)
            {
                if (i == 0xFFFF || depth > maxDepth) return;
                var n = net.GetNode(i);
                string extra = "";
                var sa = NmbNetwork.Find(n, 21);
                if (sa != null) { int ci = net.ClipIndex(sa); extra = $" clip {(ci < files.Count ? files[ci] : ci.ToString())} {net.ClipDuration(sa).ToString("0.###", Inv)}s"; }
                var a59 = NmbNetwork.Find(n, 59);
                if (a59 != null) extra = $" dur {NmbFile.F32(x, a59.Offset + 0x10).ToString("0.###", Inv)}";
                int p0 = n.Offset; int ncp = x[p0 + 0x0E]; long pCp = (long)NmbFile.U64(x, p0 + 0x28);
                var cps = new List<string>();
                for (int k = 0; k < ncp && pCp != 0; k++) { int src = U16(x, p0 + (int)pCp + 4 * k); cps.Add(names.TryGetValue(src, out var sn) ? sn.Replace("ControlParameters|", "") : $"{src}"); }
                Console.WriteLine($"{new string(' ', 2 * depth)}{i} t{n.Type}{(names.TryGetValue(i, out var nm) ? $" '{nm}'" : "")}{(cps.Count > 0 ? $" cp[{string.Join(",", cps)}]" : "")}{extra}");
                if (!seen.Add(i) || n.Type is 400 or 402) return;
                foreach (int c in n.Children) Rec(c, depth + 1);
            }
            Rec(root, 0);
            return 0;
        }
        for (int i = 0; i < net.NodeCount; i++)
        {
            var n = net.GetNode(i);
            int p = n.Offset;
            int ncp = x[p + 0x0E];
            long pCp = (long)NmbFile.U64(x, p + 0x28);
            var cps = new List<string>();
            for (int k = 0; k < ncp && pCp != 0; k++)
            {
                int src = U16(x, p + (int)pCp + 4 * k), pin = U16(x, p + (int)pCp + 4 * k + 2);
                cps.Add($"{src}{(names.TryGetValue(src, out var sn) ? "(" + sn.Replace("ControlParameters|", "") + ")" : "")}:{pin}");
            }
            string nm = names.TryGetValue(i, out var s) ? $" '{s}'" : "";
            Console.WriteLine($"N {i} type {n.Type} flags 0x{U16(x, p + 4):X} parent {n.Parent}{nm} children [{string.Join(",", n.Children)}] cp [{string.Join(" ", cps)}] attribs [{string.Join(",", n.Attribs.Select(a => a.Type))}]");
            foreach (var a in n.Attribs)
            {
                int o = a.Offset;
                switch (a.Type)
                {
                    case 87:
                    {
                        int cnt = U16(x, o + 0x12); int pp = (int)NmbFile.U64(x, o + 0x18);
                        var v = Enumerable.Range(0, cnt).Select(k => (B(k), NmbFile.F32(x, o + pp + 4 * k))).ToList();
                        Console.WriteLine("   alphas: " + string.Join(" ", v.Select(t => $"{t.Item1}={t.Item2.ToString("0.##", Inv)}")));
                        break;
                    }
                    case 8:
                        Console.WriteLine("   bones: " + string.Join(" ", net.IntArray(a).Select(B)));
                        break;
                    case 9:
                    {
                        int cnt = (int)NmbFile.U32(x, o + 0x10); int pp = (int)NmbFile.U64(x, o + 0x18);
                        Console.WriteLine($"   floats[{cnt}]: " + string.Join(" ", Enumerable.Range(0, Math.Min(cnt, 120)).Select(k => (cnt == rig.Names.Count ? B(k) + "=" : "") + NmbFile.F32(x, o + pp + 4 * k).ToString("0.###", Inv))));
                        break;
                    }
                    case 25:
                        Console.WriteLine($"   headlook end {B((int)NmbFile.U32(x, o + 0x20))} root {B((int)NmbFile.U32(x, o + 0x24))} joints {NmbFile.U32(x, o + 0x28)} raw {Convert.ToHexString(x, o + 0x10, (int)Math.Min(a.Size - 0x10, 0x60))}");
                        break;
                    case 24:
                        Console.WriteLine($"   headlook setup raw {Convert.ToHexString(x, o + 0x10, (int)Math.Min(a.Size - 0x10, 0x40))}");
                        break;
                    case 31:
                        Console.WriteLine($"   lockfoot {string.Join(",", new[] { 0x48, 0x4C, 0x50, 0x54, 0x58 }.Select(q => B((int)NmbFile.U32(x, o + q))))}");
                        break;
                    case 21:
                    {
                        int ci = net.ClipIndex(a);
                        var (s0, s1) = net.ClipFractions(a);
                        Console.WriteLine($"   clip {(ci < files.Count ? files[ci] : ci.ToString())} dur {net.ClipDuration(a).ToString("0.###", Inv)} frac {s0.ToString("0.###", Inv)}-{s1.ToString("0.###", Inv)} flags6C 0x{NmbFile.U32(x, o + 0x6C):X} 7C {x[o + 0x7C]},{x[o + 0x7D]} add90 {x[o + 0x90]} sync {net.SyncTrackIndex(a)}");
                        break;
                    }
                    case 59:
                        Console.WriteLine($"   transit dur {NmbFile.F32(x, o + 0x10).ToString("0.###", Inv)} mode {NmbFile.U32(x, o + 0x14)} raw {Convert.ToHexString(x, o + 0x10, (int)Math.Min(a.Size - 0x10, 0x30))}");
                        break;
                    case 60:
                        Console.WriteLine($"   transit60 raw {Convert.ToHexString(x, o + 0x10, (int)Math.Min(a.Size - 0x10, 0x30))}");
                        break;
                    case 38:
                    {
                        var sm = NmbNetEdit.ReadSmDef(x, o);
                        var reqs = NmbNetEdit.StringTable(x, (int)NmbFile.U64(x, 0xD0)).ToDictionary(t => (int)t.Id, t => t.Name);
                        string Cp(int id) => names.TryGetValue(id, out var cn) ? cn.Replace("ControlParameters|", "") : $"node{id}";
                        string Cond(byte[] c)
                        {
                            int t = U16(c, 0); string inv = c[2] != 0 ? "NOT " : "";
                            string body = t switch
                            {
                                601 => $"request {(int)NmbFile.U32(c, 0x48)}({reqs.GetValueOrDefault((int)NmbFile.U32(c, 0x48), "?")})",
                                602 => $"event@{U16(c, 0x48)} == {NmbFile.I32(c, 0x54)}",
                                603 => $"node {U16(c, 0x48)} crossed {NmbFile.F32(c, 0x54).ToString("0.###", Inv)}",
                                606 or 608 => $"{Cp(U16(c, 0x48))} cmp{t} {NmbFile.F32(c, 0x4C).ToString("0.###", Inv)} f{c[0x50]}",
                                610 or 618 => $"event@{U16(c, 0x48)} in [{NmbFile.I32(c, 0x54)},{NmbFile.I32(c, 0x58)}]",
                                611 => $"sync@{U16(c, 0x48)} in [{NmbFile.F32(c, 0x60).ToString("0.###", Inv)},{NmbFile.F32(c, 0x64).ToString("0.###", Inv)}]",
                                617 => $"node {U16(c, 0x48)} active",
                                623 or 624 => $"{Cp(U16(c, 0x48))} icmp{t} {NmbFile.I32(c, 0x4C)} f{c[0x50]}",
                                625 => $"{Cp(U16(c, 0x48))} in [{NmbFile.I32(c, 0x4C)},{NmbFile.I32(c, 0x50)}]",
                                627 => $"{Cp(U16(c, 0x48))} in [{NmbFile.F32(c, 0x4C).ToString("0.###", Inv)},{NmbFile.F32(c, 0x50).ToString("0.###", Inv)}]",
                                _ => $"cond{t}",
                            };
                            return inv + body;
                        }
                        Console.WriteLine($"   SM default {sm.Default} ({(sm.Default < sm.States.Count ? sm.States[sm.Default].Node : -1)})");
                        for (int si = 0; si < sm.States.Count; si++)
                        {
                            var st = sm.States[si];
                            int sn = st.Node;
                            var nt = net.GetNode(sn).Type;
                            if (nt is 400 or 402)
                            {
                                string S(int k) => k == 0xFFFF ? "ANY" : k < sm.States.Count ? $"{sm.States[k].Node}" : $"?{k}";
                                Console.WriteLine($"   T {sn} ({nt}) {S(st.Src)} -> {S(st.Dst)} when {string.Join(" AND ", st.Entry.Select(ci => ci < sm.Conds.Count ? Cond(sm.Conds[ci]) : "?"))}");
                            }
                            else Console.WriteLine($"   S [{si}] {sn} type {nt}{(names.TryGetValue(sn, out var stn) ? $" '{stn}'" : "")}");
                        }
                        if (sm.Glob.ExitTrans.Count > 0) Console.WriteLine($"   global exit transitions [{string.Join(",", sm.Glob.ExitTrans.Select(k => k < sm.States.Count ? sm.States[k].Node : -1))}]");
                        break;
                    }
                    case 2: case 3:
                        Console.WriteLine($"   default {(a.Type == 2 ? NmbFile.I32(x, o + 0x10).ToString() : NmbFile.F32(x, o + 0x10).ToString("0.###", Inv))}");
                        break;
                }
            }
        }
        return 0;
    }
}
