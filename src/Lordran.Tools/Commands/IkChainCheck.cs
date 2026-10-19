using Lordran.Tools.Formats.Morpheme;

namespace Lordran.Tools.Commands;

/// <summary>
/// ik-chain-check PACK|ANIBND|NMB [--raw] [--only cNNNN,...] (session 11, crash11): the bone chains the DS2 Morpheme IK
/// nodes walk at run time, checked against the network's rig. The c11 Firelink crash (exe+0x679119, FUN_140678e30, under
/// FUN_14066f4a0 = BASIC_UNEVEN_TERRAIN per-limb foot solve) walks each foot joint's parent chain up to the previous foot
/// joint (rig parent array, no bound): when joint i is not a strict descendant of joint i-1 the walk passes the root, reads
/// parent -1 and indexes the pose's bit set with 0xFFFFFFFF (access violation). Checked per chain:
/// <list type="bullet">
/// <item>FAIL: BASIC_UNEVEN_TERRAIN_CHAIN (73), per limb at +0x70 + 0x30·limb: hip ← knee ← ankle a parent chain, foot-joint
/// count (+0x14) in 1..3, foot joints [ankle, ball, toe][..count] each strictly below the previous (rule IK1, IkChains);</item>
/// <item>WARN: LOCK_FOOT_CHAIN (31) and HIPS_IK_ANIM_SET_DEF (34) chains (trailing -1 allowed: vanilla c6600), HEAD_LOOK_CHAIN
/// (25) root above end - not the walk that crashed; lock-foot toe == ball is the s8 rule (L_Toe0Nub → L_Toe0) in packs that ran.</item>
/// </list>
/// Vanilla baseline: all 173 DS2 networks (data\ds2\morpheme4\chr) pass with 0 problems.
/// Read-only; exit 1 on any problem.
/// </summary>
public static class IkChainCheck
{
    public static int Run(string[] args)
    {
        string src = args[1];
        bool raw = args.Contains("--raw");
        int oi = Array.IndexOf(args, "--only");
        var only = oi > 0 ? args[oi + 1].Split(',').ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        var inputs = new List<string>();
        if (Directory.Exists(src))
        {
            string dir = Directory.Exists(Path.Combine(src, "gamedata", "morpheme4", "chr")) ? Path.Combine(src, "gamedata", "morpheme4", "chr") : src;
            inputs.AddRange(Directory.GetFiles(dir, "*.anibnd.dcx", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.Ordinal));
            inputs.AddRange(Directory.GetFiles(dir, "*.nmb", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.Ordinal));
        }
        else inputs.Add(src);
        int problems = 0, nets = 0, chains = 0, warns = 0;
        foreach (var path in inputs)
        {
            string chr = Path.GetFileName(path).Split('.')[0];
            if (only != null && !only.Contains(chr)) continue;
            List<(string Name, byte[] Bytes)> files;
            try { files = MorphemeTools.LoadFiles(path); }
            catch (Exception e) { Console.WriteLine($"{chr}: unreadable ({e.Message})"); problems++; continue; }
            foreach (var f in files.Where(x => x.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)))
            {
                nets++;
                var probs = Check(f.Bytes, raw, out int n, out var lines);
                chains += n;
                string tag = files.Count(x => x.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)) > 1 ? $"{chr}/{f.Name}" : chr;
                foreach (var l in lines) if (raw || l.StartsWith("WARN")) Console.WriteLine($"{tag}: {l}");
                warns += lines.Count(l => l.StartsWith("WARN"));
                foreach (var p in probs) Console.WriteLine($"{tag}: PROBLEM {p}");
                problems += probs.Count;
            }
        }
        Console.WriteLine($"ik-chain-check: {nets} networks, {chains} chains, {problems} problem(s), {warns} warning(s)");
        return problems == 0 ? 0 : 1;
    }

    /// <summary>Problems of one NMB's IK chains (empty = fine). <paramref name="lines"/>: per-chain description when raw.</summary>
    public static List<string> Check(byte[] nmbBytes, bool raw, out int chains, out List<string> lines)
    {
        var probs = new List<string>(); var outLines = new List<string>(); int nChains = 0; lines = outLines; chains = 0;
        var nmb = NmbFile.Read(nmbBytes);
        int ri = nmb.IndexOf(1), ni = nmb.IndexOf(10);
        if (ri < 0 || ni < 0) return probs;
        var rig = NmbRig.Read(nmb.Bundles[ri].Data);
        var x = nmb.Bundles[ni].Data;
        int nb = rig.Names.Count;
        string B(int b) => b >= 0 && b < nb ? $"{b}:{rig.Names[b]}" : $"#{b}";
        bool Valid(int b) => b >= 0 && b < nb;
        // fatal = walked by the uneven-terrain foot solve (FUN_140678e30) or a leg the solve uses; warn = template quirks seen in
        // packs that ran (lock-foot toe == ball since s8: L_Toe0Nub → L_Toe0; not the walk that crashed)
        void Chain(string what, int at, IReadOnlyList<int> joints, bool fatal = true, bool optionalTail = false)
        {
            nChains++;
            var js = joints.ToList();
            if (optionalTail) while (js.Count > 1 && js[^1] == -1) js.RemoveAt(js.Count - 1);
            if (raw) outLines.Add($"{what} @0x{at:x}: {string.Join(" > ", js.Select(B))}");
            string all = string.Join(" > ", js.Select(B));
            void Add(string m) { if (fatal) probs.Add(m); else outLines.Add("WARN " + m); }
            foreach (int j in js) if (!Valid(j)) { Add($"{what} @0x{at:x}: bone {j} out of rig [0,{nb})  ({all})"); return; }
            for (int i = 1; i < js.Count; i++)
                if (!IkChains.Below(rig.Parents, js[i - 1], js[i]))
                    Add($"{what} @0x{at:x}: {B(js[i])} is not below {B(js[i - 1])} (a parent walk from it passes the root: index -1)  ({all})");
        }
        foreach (var (o, t) in NmbRigReplace.ScanAttribs(x, 0x10000))
        {
            switch (t)
            {
                case 73:
                    for (int limb = 0; limb < 2; limb++)
                    {
                        int b = o + IkChains.TerrainLimb0 + IkChains.TerrainLimbStride * limb;
                        if (b + IkChains.TerrainCount + 4 > x.Length) break;
                        int hip = NmbFile.I32(x, b), knee = NmbFile.I32(x, b + 4), ankle = NmbFile.I32(x, b + 8);
                        int ball = NmbFile.I32(x, b + 12), toe = NmbFile.I32(x, b + 16), nfoot = NmbFile.I32(x, b + IkChains.TerrainCount);
                        if (raw) outLines.Add($"uneven-terrain limb {limb} @0x{b:x}: hip {B(hip)} knee {B(knee)} ankle {B(ankle)} ball {B(ball)} toe {B(toe)} footJoints {nfoot}");
                        Chain($"uneven-terrain limb {limb} leg", b, [hip, knee, ankle]);
                        if (nfoot < 1 || nfoot > 3) { probs.Add($"uneven-terrain limb {limb} @0x{b:x}: foot-joint count {nfoot} not in 1..3"); continue; }
                        Chain($"uneven-terrain limb {limb} foot (count {nfoot})", b + 8, new[] { ankle, ball, toe }.Take(nfoot).ToArray());
                    }
                    break;
                case 31:
                    Chain("lock-foot", o + 0x48, new[] { 0x48, 0x4C, 0x50, 0x54, 0x58 }.Select(q => NmbFile.I32(x, o + q)).ToArray(), fatal: false, optionalTail: true);
                    break;
                case 34:
                    for (int leg = 0; leg < 2; leg++)
                        Chain($"hips-ik leg {leg}", o + 0x30 + 0x10 * leg, Enumerable.Range(0, 4).Select(k => NmbFile.I32(x, o + 0x30 + 0x10 * leg + 4 * k)).ToArray(), fatal: false, optionalTail: true);
                    break;
                case 25:
                {
                    int end = NmbFile.I32(x, o + 0x20), root = NmbFile.I32(x, o + 0x24);
                    nChains++;
                    if (raw) outLines.Add($"head-look @0x{o:x}: root {B(root)} end {B(end)}");
                    if (!Valid(end) || !Valid(root)) outLines.Add($"WARN head-look @0x{o:x}: bone out of rig (root {root}, end {end})");
                    else if (root != end && !IkChains.Below(rig.Parents, root, end)) outLines.Add($"WARN head-look @0x{o:x}: end {B(end)} is not below root {B(root)}");
                    break;
                }
            }
        }
        chains = nChains;
        return probs;
    }

    /// <summary>ik-chain-repair IN.anibnd.dcx OUT.anibnd.dcx [--g1off] (crash11 A/B): rule IK1 on an existing anibnd's network
    /// (foot-joint counts cut to the valid prefix); --g1off instead restores the engine-driven UnevenTerrain_BlendWeight on the
    /// uneven-terrain nodes (138 and the 500 above it) where Ds1NetRules G1 wired PlaySpeed_0 (chain left as is: the trigger
    /// A/B). Writes OUT only; prints what changed.</summary>
    public static int Repair(string[] args)
    {
        bool g1off = args.Contains("--g1off");
        byte[] raw = File.ReadAllBytes(args[1]);
        SoulsFormats.DCX.CompressionInfo? comp = null;
        if (SoulsFormats.DCX.Is(raw)) raw = SoulsFormats.DCX.Decompress(raw, out comp);
        var bnd = SoulsFormats.BND4.Read(raw);
        var f = bnd.Files.First(x => x.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase));
        var log = new List<string>();
        if (!g1off) f.Bytes = IkChains.RepairNmb(f.Bytes, log);
        else
        {
            var nmb = NmbFile.Read(f.Bytes);
            int ni = nmb.IndexOf(10);
            var x = (byte[])nmb.Bundles[ni].Data.Clone();
            var net = new NmbNetwork(x);
            var names = NmbNetEdit.NodeNames(x);
            int Cp(string n) => names.FirstOrDefault(kv => kv.Value == "ControlParameters|" + n, new(-1, "")).Key;
            int zero = Cp("PlaySpeed_0"), w = Cp("UnevenTerrain_BlendWeight");
            if (zero < 0 || w < 0) throw new InvalidDataException("no PlaySpeed_0 / UnevenTerrain_BlendWeight CP");
            foreach (var n in net.Nodes().Where(n => n.Type == 138))
            {
                var targets = new List<(NmbNetwork.Node Node, int Pin)> { (n, 1) };
                if (n.Parent != 0xFFFF && net.GetNode(n.Parent) is { Type: 500 } up) targets.Add((up, 0));
                foreach (var (node, pin) in targets)
                {
                    int p = node.Offset, ncp = x[p + 0x0E]; long pCp = (long)NmbFile.U64(x, p + 0x28);
                    if (pin >= ncp || pCp == 0) continue;
                    int e = p + (int)pCp + 4 * pin;
                    if (System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(e)) != zero) continue;
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(x.AsSpan(e), (ushort)w);
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(x.AsSpan(e + 2), 0);
                    log.Add($"G1-off: node {node.Index} (type {node.Type}) CP input {pin} PlaySpeed_0 → UnevenTerrain_BlendWeight");
                }
            }
            f.Bytes = nmb.Rebuild(new Dictionary<int, byte[]> { [ni] = x });
        }
        foreach (var l in log) Console.WriteLine(l);
        byte[] outBnd = bnd.Write();
        File.WriteAllBytes(args[2], comp != null ? SoulsFormats.DCX.Compress(outBnd, comp) : outBnd);
        Console.WriteLine($"wrote {args[2]} ({log.Count} change(s))");
        return log.Count > 0 ? 0 : 1;
    }
}
