using System.Globalization;
using Lordran.Tools.Build.Chr;
using Lordran.Tools.Formats.Morpheme;
using Lordran.Tools.Textures;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// npc-idle OUTDIR [--png]: builds the DS1-NPC network (NpcIdle: c8001.anibnd.dcx + c800100 TAEs) into OUTDIR (pack
/// layout under gamedata\), prints the report, and with --png writes per slot a pose sheet (front and side stick
/// figures, DS1's skeleton × leg scale in grey under ours in colour, frames 0, ¼, ½, ¾, last) and re-checks the written
/// files: the anibnd re-read, NMB rig/maps/tracks byte-identical under c0001's ids, the network def under its own asset
/// id (I1b: not c0001's, unique in the NMB, absent from every vanilla anibnd) with node count intact and every SourceAnim
/// of the slot = the NSA's duration and trajectory, every other NSA byte-identical to c0001's, and the TAE lengths.
/// </summary>
public static class NpcIdleCmd
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: npc-idle OUTDIR [--template (default) | --human | --own-network | --probe CLIP] [--png] [--ds2-layers] [--foot-ik-off] | npc-idle probe CHR ANIMID OUTDIR"); return 2; }
        if (args[1] == "probe") return Probe(args[2], int.Parse(args[3]), args[4]);
        string outDir = args[1];
        bool png = args.Contains("--png");
        if (args.Contains("--ds2-layers")) NpcIdle.Ds1Layers = false;
        if (args.Contains("--template") || (!args.Contains("--human") && !args.Contains("--own-network") && !args.Contains("--probe"))) return RunTemplate(outDir, png);
        NpcIdle.Route = NpcIdle.IdleRoute.Human;   // --human: rule I1c (A/B)
        // rule I1c (track npcpose pass 2): the human route (c0001.anibnd + c000100 TAEs) is the default; --own-network /
        // --probe CLIP build the old c8001 route (A/B)
        if (args.Contains("--own-network")) NpcIdle.Route = NpcIdle.IdleRoute.Own;
        if (CliArgs.Option(args, "--probe", out _) is { } probeClip) { NpcIdle.Route = NpcIdle.IdleRoute.Own; NpcIdle.ProbeIdle = probeClip; }
        if (args.Contains("--foot-ik-off")) NpcIdle.FootIkOffShared = true;   // pass 3: I9 on the human route too (A/B)
        string ds2 = ExtractDs2.DefaultOut;
        var r = NpcIdle.Build(Program.DefaultDsrRoot, ds2);
        foreach (var l in r.Report) Console.WriteLine(l);
        foreach (var (path, bytes) in r.Files)
        {
            string p = Path.Combine(outDir, "gamedata", path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllBytes(p, bytes);
            Console.WriteLine($"wrote {p} ({bytes.Length:N0} bytes)");
        }
        int problems = Verify(r, ds2);
        if (png)
        {
            byte[] nmbBytes = r.Nmb;
            var nmb = NmbFile.Read(nmbBytes);
            var rig = NmbRig.Read(nmb.Bundles[nmb.IndexOf(1)].Data);
            foreach (var s in r.Slots)
            {
                var map = nmb.Bundles.Where(b => b.Type == 2).Select(b => NmbChannelMap.Read(b.Data)).First(m => m.AnimToRig.Count == s.Ds2.ChannelCount);
                string file = Path.Combine(outDir, $"pose-{s.Slot.Ds1Anim}.png");
                Sheet(s, rig, map, file);
                Console.WriteLine($"wrote {file}");
            }
        }
        Console.WriteLine(problems == 0 ? "verify: 0 problems" : $"verify: {problems} problems");
        return problems == 0 ? 0 : 1;
    }

    /// <summary>Plan evidence for DS1 characters with their own human rig (Anastacia c2750, the merchant c2510): their own
    /// DS1 clip retargeted onto c0001's network rig by the same rule (offline only; no pack change): bone coverage, joint
    /// distances, pose sheet.</summary>
    static int Probe(string chr, int anim, string outDir)
    {
        var ds1 = Ds1ChrSource.Load(Program.DefaultDsrRoot, chr);
        var bnd = BND4.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(ExtractDs2.DefaultOut, "morpheme4", "chr", "c0001.anibnd.dcx"))));
        var nmb = NmbFile.Read(bnd.Files.First(f => f.Name.EndsWith(".nmb")).Bytes);
        var rig = NmbRig.Read(nmb.Bundles[nmb.IndexOf(1)].Data);
        var map = nmb.Bundles.Where(b => b.Type == 2).Select(b => NmbChannelMap.Read(b.Data)).First(m => m.AnimToRig.Count == rig.Names.Count - 1);
        var src = Ds1AnimBridge.Bridge(ds1.Samples(anim), 30);
        Console.WriteLine($"{chr} skeleton: {string.Join(" ", src.Skeleton.Names)}");
        var a = NpcIdle.Retarget(src, rig, map, out var ck);
        var back = NsaCodec.Decode(NsaFile.Parse(NsaEncoder.EncodeFile(a).Build()));
        NpcIdle.CompareFor(src, back, rig, map, ck);
        Console.WriteLine($"{chr} a00_{anim}: {src.Frames} frames; {ck.Mapped} of {ck.Ds2Bones} DS2 bones driven, leg scale {ck.Scale:0.###}, facing turn {ck.FaceYawDeg:0.#}°; DS2 bones on bind: {string.Join(" ", ck.Ds2Unmapped)}; DS1 bones unused: {string.Join(" ", ck.Ds1Unmapped)}");
        Console.WriteLine($"  joints (cm): {string.Join(", ", ck.JointCm.Select(kv => $"{kv.Key} {kv.Value:0.#}"))}; seam DS1 {ck.SeamDs1Deg:0.##}° ours {ck.SeamOursDeg:0.##}°");
        Console.WriteLine($"  frame 0: pelvis {ck.Frame0.Pelvis}, head {ck.Frame0.Head}, feet {ck.Frame0.LFoot} {ck.Frame0.RFoot}");
        Console.WriteLine($"  DS1 TAE: {NpcIdle.Ds1Tae(ds1, anim)}");
        Directory.CreateDirectory(outDir);
        var sr = new NpcIdle.SlotResult { Slot = new NpcIdle.Slot(anim, 0, "", chr), Ds1 = src, Ds2 = back, Ds2Original = back, Check = ck };
        string file = Path.Combine(outDir, $"probe-{chr}-{anim}.png");
        Sheet(sr, rig, map, file);
        Console.WriteLine($"wrote {file}");
        return 0;
    }

    /// <summary>Re-reads the built anibnd and TAEs and checks them against c0001's.</summary>
    static int Verify(NpcIdle.Result r, string ds2)
    {
        int problems = 0;
        void Bad(string m) { problems++; Console.WriteLine("PROBLEM " + m); }
        var orig = BND4.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(ds2, "morpheme4", "chr", "c0001.anibnd.dcx"))));
        var ours = BND4.Read(DCX.Decompress(r.Files[$"morpheme4/chr/{NpcIdle.AnibndName}.anibnd.dcx"]));
        if (orig.Files.Count != ours.Files.Count) Bad($"file count {ours.Files.Count} vs c0001's {orig.Files.Count}");
        var replaced = r.Slots.Select(s => s.NsaName).Concat(r.ProbeNsas).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool own = NpcIdle.Route == NpcIdle.IdleRoute.Own;
        int same = 0, diff = 0;
        foreach (var (a, b) in orig.Files.Zip(ours.Files))
        {
            string an = Path.GetFileName(a.Name.Replace('\\', '/')), bn = Path.GetFileName(b.Name.Replace('\\', '/'));
            if (a.ID != b.ID) Bad($"entry id {b.ID} vs {a.ID}");
            if (an.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase))
            {
                if (bn != $"{NpcIdle.AnibndName}.nmb") Bad($"NMB entry named {bn}");
                continue;
            }
            if (an != bn) Bad($"entry {bn} vs {an}");
            bool eq = a.Bytes.AsSpan().SequenceEqual(b.Bytes);
            if (replaced.Contains(an)) { if (eq) Bad($"{an} not replaced"); diff++; }
            else if (!eq) Bad($"{an} changed"); else same++;
        }
        Console.WriteLine($"verify anibnd: {same} NSA/other entries byte-identical to c0001's, {diff} replaced, NMB named {NpcIdle.AnibndName}.nmb");
        // NMB: same bundles (ids, GUIDs, data) except the network def: own asset id + GUID (I1b) and the patched SourceAnims
        var nmb0 = NmbFile.Read(orig.Files.First(f => f.Name.EndsWith(".nmb")).Bytes);
        var nmb1 = NmbFile.Read(ours.Files.First(f => f.Name.EndsWith(".nmb")).Bytes);
        if (nmb0.Bundles.Count != nmb1.Bundles.Count) Bad("NMB bundle count");
        int changed = 0;
        for (int i = 0; i < Math.Min(nmb0.Bundles.Count, nmb1.Bundles.Count); i++)
        {
            var (x, y) = (nmb0.Bundles[i], nmb1.Bundles[i]);
            if (x.Type != y.Type) Bad($"bundle {i} type");
            if (y.Type == 10 && !own)
            {
                // I1c human route: the file replaces c0001.anibnd itself, so the network def keeps c0001's id and GUID
                if (y.AssetId != x.AssetId || !y.Guid.AsSpan().SequenceEqual(x.Guid)) Bad($"human route: network def id/GUID changed (0x{y.AssetId:X8} vs c0001's 0x{x.AssetId:X8})");
            }
            else if (y.Type == 10)
            {
                if (y.AssetId == x.AssetId || y.AssetId != r.NetworkAssetId) Bad($"network def id 0x{y.AssetId:X8} (c0001's 0x{x.AssetId:X8}, built 0x{r.NetworkAssetId:X8})");
                if (y.Guid.AsSpan().SequenceEqual(x.Guid)) Bad("network def GUID = c0001's");
                if (nmb1.Bundles.Count(b => b.AssetId == y.AssetId) != 1) Bad("network def id not unique in the NMB");
            }
            else if (x.AssetId != y.AssetId || !x.Guid.AsSpan().SequenceEqual(y.Guid)) Bad($"bundle {i} type {y.Type} id/GUID changed");
            if (!x.Data.AsSpan().SequenceEqual(y.Data))
            {
                changed++;
                Console.WriteLine($"  NMB bundle {i} type {y.Type} id 0x{y.AssetId:X8} changed ({x.Data.Length} → {y.Data.Length} bytes)");
                if (y.Type != 10) Bad($"bundle {i} type {y.Type} 0x{y.AssetId:X8} changed under c0001's id: the player's copy would be used");
            }
        }
        // I1b: the network def id must be free in every vanilla anibnd (any bundle type: the asset manager keys by id alone)
        var vanilla = Directory.GetFiles(Path.Combine(ds2, "morpheme4", "chr"), "*.anibnd.dcx");
        int idHits = 0, guidHits = 0;
        string guid1 = Convert.ToHexString(nmb1.Bundles[nmb1.IndexOf(10)].Guid);
        foreach (var p in own ? vanilla : vanilla.Where(p => !Path.GetFileName(p).Equals("c0001.anibnd.dcx", StringComparison.OrdinalIgnoreCase)))
            foreach (var e in NmbIdsCmd.Read(p, false))
            {
                if (e.Id == r.NetworkAssetId && e.Type == 10) { idHits++; Bad($"network def id 0x{e.Id:X8} used by {e.File} (type {e.Type})"); }
                else if (e.Id == r.NetworkAssetId && own) { idHits++; Bad($"network def id 0x{e.Id:X8} used by {e.File} (type {e.Type})"); }
                if (e.Guid == guid1 && (own || e.Type == 10)) { guidHits++; Bad($"network def GUID used by {e.File} (type {e.Type})"); }
            }
        Console.WriteLine($"verify ids: network def 0x{r.NetworkAssetId:X8} / GUID {guid1.ToLowerInvariant()} {(own ? "in none of" : "(c0001's own) in no other network def of")} {vanilla.Length} vanilla anibnds ({idHits} id, {guidHits} GUID hits)");
        var net0 = new NmbNetwork(nmb0.Bundles[nmb0.IndexOf(10)].Data);
        var net1 = new NmbNetwork(nmb1.Bundles[nmb1.IndexOf(10)].Data);
        if (net0.NodeCount != net1.NodeCount) Bad("node count");
        if (net0.X.Length != net1.X.Length) Bad("network size");
        int bytesDiff = 0;
        for (int i = 0; i < Math.Min(net0.X.Length, net1.X.Length); i++) if (net0.X[i] != net1.X[i]) bytesDiff++;
        Console.WriteLine($"verify NMB: {changed} bundles changed; network {net1.NodeCount} nodes, {bytesDiff} bytes differ from c0001's");
        foreach (var s in r.Slots)
        {
            var nsa = NsaCodec.Decode(NsaFile.Parse(ours.Files.First(f => f.Name.EndsWith(s.NsaName, StringComparison.OrdinalIgnoreCase)).Bytes));
            double dur = (nsa.Frames - 1) / nsa.Fps;
            foreach (int ni in s.Nodes)
                foreach (var sa in net1.GetNode(ni).Attribs.Where(a => a.Type == 21))
                {
                    if (net1.ClipIndex(sa) != s.ClipIndex) Bad($"node {ni} clip index");
                    if (Math.Abs(net1.ClipDuration(sa) - dur) > 1e-4) Bad($"node {ni} duration {net1.ClipDuration(sa)} vs NSA {dur}");
                    if (net1.ClipFractions(sa) != (0f, 1f)) Bad($"node {ni} fractions");
                }
            Console.WriteLine($"verify slot {s.Slot.Category}: {s.NsaName} {nsa.ChannelCount} channels, {nsa.Frames} frames @ {nsa.Fps} fps, nodes {string.Join(",", s.Nodes)} SourceAnim = NSA");
        }
        foreach (var kind in new[] { "pl", "sfx", "snd" })
        {
            var t0 = new Formats.Ds2Tae(File.ReadAllBytes(Path.Combine(ds2, "timeact", "chr", $"c000100_{kind}.tae")));
            var t1 = new Formats.Ds2Tae(r.Files[$"timeact/chr/c{NpcIdle.AnimId:D6}_{kind}.tae"]);
            if (t0.Bytes.Length != t1.Bytes.Length || t0.Records.Count != t1.Records.Count) Bad($"{kind} TAE layout");
            foreach (var s in r.Slots) foreach (int id in s.TimeAct)
                    if (t1.Length(id) != (int)Math.Round(s.Ds1Duration * 30)) Bad($"{kind} TAE {id} length {t1.Length(id)}");
        }
        return problems;
    }

    // ------------------------------------------------------------------ rule I1d (track npcpose2): own template networks

    /// <summary>npc-idle OUTDIR --template: every <see cref="NpcIdle.Nets"/> network + TAEs + AI copy into OUTDIR (pack layout)
    /// and the verify report: every template entry byte-identical except the loop clip's NSA and the network-def bundle (new
    /// id/GUID, I5's SourceAnim bytes, the listed I6c/I9d bytes); TAEs = the template's but the loop entry's length.</summary>
    static int RunTemplate(string outDir, bool png)
    {
        string ds2 = ExtractDs2.DefaultOut;
        int problems = 0;
        var built = new List<NpcIdle.TemplateResult>();
        foreach (var n in NpcIdle.Nets)
        {
            var r = NpcIdle.BuildTemplate(Program.DefaultDsrRoot, ds2, n);
            built.Add(r);
            foreach (var l in r.Report) Console.WriteLine(l);
            foreach (var p in r.Problems) { Console.WriteLine("PROBLEM " + p); problems++; }
            var ai = NpcAi.Build(Path.Combine(ds2, "ezstate"), n);
            r.Files[$"ezstate/ai{ai.Low:D6}.esd"] = ai.LowerEsd;
            r.Files[$"ezstate/ai{ai.High:D6}.esd"] = ai.UpperEsd;
            Console.WriteLine($"AI-N1: {ai.Report[0]}");
            foreach (var (path, bytes) in r.Files)
            {
                string p = Path.Combine(outDir, "gamedata", path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllBytes(p, bytes);
                Console.WriteLine($"wrote {p} ({bytes.Length:N0} bytes)");
            }
            problems += VerifyTemplate(r, ds2);
            problems += CheckAi(ESD.Read(ai.LowerEsd), ESD.Read(ai.UpperEsd), n, ds2, Console.WriteLine);
            if (png)
            {
                var nmb = NmbFile.Read(r.Nmb);
                var rig = NmbRig.Read(nmb.Bundles[nmb.IndexOf(1)].Data);
                var map = nmb.Bundles.Where(b => b.Type == 2).Select(b => NmbChannelMap.Read(b.Data)).First(m => m.AnimToRig.Count == r.Ds2.ChannelCount);
                var sr = new NpcIdle.SlotResult { Slot = new NpcIdle.Slot(n.Ds1Anim, n.StartupType, r.NsaName, n.Who), Ds1 = r.Ds1, Ds2 = r.Ds2, Ds2Original = r.Ds2Original, Check = r.Check };
                string file = Path.Combine(outDir, $"pose-{n.Name}-{n.Ds1Anim}.png");
                Sheet(sr, rig, map, file);
                Console.WriteLine($"wrote {file}");
            }
        }
        // distinct identities across our networks
        if (built.Select(b => b.NetworkAssetId).Distinct().Count() != built.Count) { Console.WriteLine("PROBLEM two NPC networks share a network-def id"); problems++; }
        Console.WriteLine(problems == 0 ? "verify: 0 problems" : $"verify: {problems} problems");
        return problems == 0 ? 0 : 1;
    }

    static int VerifyTemplate(NpcIdle.TemplateResult r, string ds2)
    {
        var n = r.Net;
        int problems = 0;
        void Bad(string m) { problems++; Console.WriteLine("PROBLEM " + m); }
        var orig = BND4.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(ds2, "morpheme4", "chr", $"{n.Template}.anibnd.dcx"))));
        var ours = BND4.Read(DCX.Decompress(r.Files[$"morpheme4/chr/{n.Name}.anibnd.dcx"]));
        if (orig.Files.Count != ours.Files.Count) Bad($"file count {ours.Files.Count} vs {n.Template}'s {orig.Files.Count}");
        int same = 0, diff = 0;
        foreach (var (a, b) in orig.Files.Zip(ours.Files))
        {
            string an = Path.GetFileName(a.Name.Replace('\\', '/')), bn = Path.GetFileName(b.Name.Replace('\\', '/'));
            if (a.ID != b.ID) Bad($"entry id {b.ID} vs {a.ID}");
            if (a.Name.Replace(n.Template, n.Name) != b.Name) Bad($"entry name {b.Name} vs {a.Name}");
            if (an.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)) { if (bn != $"{n.Name}.nmb") Bad($"NMB entry named {bn}"); continue; }
            bool eq = a.Bytes.AsSpan().SequenceEqual(b.Bytes);
            if (an.Equals(r.NsaName, StringComparison.OrdinalIgnoreCase)) { if (eq) Bad($"{an} not replaced"); diff++; }
            else if (!eq) Bad($"{an} changed"); else same++;
        }
        Console.WriteLine($"verify {n.Name}.anibnd: {same} entries byte-identical to {n.Template}'s, {diff} replaced ({r.NsaName}), NMB named {n.Name}.nmb");
        var nmb0 = NmbFile.Read(orig.Files.First(f => f.Name.EndsWith(".nmb")).Bytes);
        var nmb1 = NmbFile.Read(ours.Files.First(f => f.Name.EndsWith(".nmb")).Bytes);
        if (nmb0.Bundles.Count != nmb1.Bundles.Count) Bad("NMB bundle count");
        int changed = 0;
        for (int i = 0; i < Math.Min(nmb0.Bundles.Count, nmb1.Bundles.Count); i++)
        {
            var (x, y) = (nmb0.Bundles[i], nmb1.Bundles[i]);
            if (x.Type != y.Type) Bad($"bundle {i} type");
            if (y.Type == 10)
            {
                if (y.AssetId == x.AssetId || y.AssetId != r.NetworkAssetId) Bad($"network def id 0x{y.AssetId:X8} ({n.Template}'s 0x{x.AssetId:X8}, built 0x{r.NetworkAssetId:X8})");
                if (y.Guid.AsSpan().SequenceEqual(x.Guid)) Bad($"network def GUID = {n.Template}'s");
                if (nmb1.Bundles.Count(b => b.AssetId == y.AssetId) != 1) Bad("network def id not unique in the NMB");
            }
            else if (x.AssetId != y.AssetId || !x.Guid.AsSpan().SequenceEqual(y.Guid)) Bad($"bundle {i} type {y.Type} id/GUID changed");
            if (!x.Data.AsSpan().SequenceEqual(y.Data))
            {
                changed++;
                if (y.Type != 10) Bad($"bundle {i} type {y.Type} 0x{y.AssetId:X8} changed under the template's id");
            }
        }
        // the network def's id / GUID: in no vanilla anibnd (any bundle type: the asset manager keys by id alone)
        var vanilla = Directory.GetFiles(Path.Combine(ds2, "morpheme4", "chr"), "*.anibnd.dcx");
        string guid1 = Convert.ToHexString(r.NetworkGuid);
        int hits = 0;
        foreach (var p in vanilla)
            foreach (var e in NmbIdsCmd.Read(p, false))
                if (e.Id == r.NetworkAssetId || e.Guid == guid1) { hits++; Bad($"network def id/GUID used by {e.File} (type {e.Type})"); }
        Console.WriteLine($"verify ids: network def 0x{r.NetworkAssetId:X8} / GUID {guid1.ToLowerInvariant()} in none of {vanilla.Length} vanilla anibnds ({hits} hits)");
        // network-def bytes: only I5's SourceAnim attribs of the loop leaf and the listed I6c/I9d inputs differ
        var net0 = new NmbNetwork(nmb0.Bundles[nmb0.IndexOf(10)].Data);
        var net1 = new NmbNetwork(nmb1.Bundles[nmb1.IndexOf(10)].Data);
        if (net0.NodeCount != net1.NodeCount || net0.X.Length != net1.X.Length) Bad("network node count / size");
        var allowed = new List<(int Lo, int Hi, string What)>();
        foreach (int ni in r.Nodes) foreach (var sa in net1.GetNode(ni).Attribs.Where(a => a.Type == 21)) allowed.Add((sa.Offset, sa.Offset + (int)sa.Size, $"I5 SourceAnim node {ni}"));
        foreach (var (o, len, what) in r.Edits) allowed.Add((o, o + len, what));
        int bytesDiff = 0, outside = 0;
        var hitWhat = new SortedDictionary<string, int>();
        for (int i = 0; i < Math.Min(net0.X.Length, net1.X.Length); i++)
        {
            if (net0.X[i] == net1.X[i]) continue;
            bytesDiff++;
            var w = allowed.FirstOrDefault(a => i >= a.Lo && i < a.Hi);
            if (w.What == null) { outside++; if (outside <= 5) Bad($"network byte 0x{i:X} changed outside I5/I6c/I9d"); }
            else hitWhat[w.What] = hitWhat.GetValueOrDefault(w.What) + 1;
        }
        Console.WriteLine($"verify NMB: {changed} bundle(s) changed (network def only); network {net1.NodeCount} nodes, {bytesDiff} bytes differ from {n.Template}'s: {string.Join(", ", hitWhat.Select(kv => $"{kv.Key} {kv.Value}"))}");
        foreach (var (o, _, what) in r.Edits) Console.WriteLine($"  edit @0x{o:X}: {what}: ({net0.U16(o)}, pin {net0.U16(o + 2)}) → ({net1.U16(o)}, pin {net1.U16(o + 2)})");
        var nsa = NsaCodec.Decode(NsaFile.Parse(ours.Files.First(f => f.Name.EndsWith(r.NsaName, StringComparison.OrdinalIgnoreCase)).Bytes));
        double dur = (nsa.Frames - 1) / nsa.Fps;
        foreach (int ni in r.Nodes)
            foreach (var sa in net1.GetNode(ni).Attribs.Where(a => a.Type == 21))
            {
                if (net1.ClipIndex(sa) != r.ClipIndex) Bad($"node {ni} clip index");
                if (Math.Abs(net1.ClipDuration(sa) - dur) > 1e-4) Bad($"node {ni} duration {net1.ClipDuration(sa)} vs NSA {dur}");
                if (net1.ClipFractions(sa) != (0f, 1f)) Bad($"node {ni} fractions");
            }
        Console.WriteLine($"verify slot StartupType {n.StartupType}: {r.NsaName} {nsa.ChannelCount} channels, {nsa.Frames} frames @ {nsa.Fps} fps, nodes {string.Join(",", r.Nodes)} SourceAnim = NSA");
        foreach (var j in new[] { "Pelvis", "L_Foot", "R_Foot" }) if (r.Check.JointCm.GetValueOrDefault(j) > 1.0) Bad($"{j} {r.Check.JointCm[j]:0.##} cm");
        // hands: I4 keeps DS2's arm lengths (same numbers as I1c's c0001 retarget, npcpose work\p3): reported, not a problem
        foreach (var j in new[] { "L_Hand", "R_Hand" }) if (r.Check.JointCm.GetValueOrDefault(j) > 4.0) Console.WriteLine($"  note: {j} up to {r.Check.JointCm[j]:0.##} cm from DS1 × scale over the clip (I4: DS2 arm lengths kept; = the I1c retarget's)");
        // TAEs: the template's bytes except the loop entries' length fields
        foreach (var kind in new[] { "pl", "sfx", "snd" })
        {
            var t0 = new Formats.Ds2Tae(File.ReadAllBytes(Path.Combine(ds2, "timeact", "chr", $"c{n.TemplateAnimId:D6}_{kind}.tae")));
            var t1 = new Formats.Ds2Tae(r.Files[$"timeact/chr/c{n.AnimId:D6}_{kind}.tae"]);
            if (t0.Bytes.Length != t1.Bytes.Length || t0.Records.Count != t1.Records.Count) { Bad($"{kind} TAE layout"); continue; }
            int d = 0; for (int i = 0; i < t0.Bytes.Length; i++) if (t0.Bytes[i] != t1.Bytes[i]) d++;
            foreach (int id in r.TimeAct) if (t1.Length(id) != (int)Math.Round(r.Ds1Duration * 30)) Bad($"{kind} TAE {id} length {t1.Length(id)}");
            foreach (var id in t0.Records.Keys.Where(k => !r.TimeAct.Contains((int)k))) if (t0.Length(id) != t1.Length(id)) Bad($"{kind} TAE {id} length changed");
            if (d > 4 * r.TimeAct.Count) Bad($"{kind} TAE: {d} bytes differ (only the length fields may)");
            Console.WriteLine($"verify c{n.AnimId:D6}_{kind}.tae: {d} bytes differ from c{n.TemplateAnimId:D6}_{kind} (length of {string.Join(",", r.TimeAct)} → {t1.Length(r.TimeAct.FirstOrDefault())})");
        }
        return problems;
    }

    /// <summary>AI-N1 check: the lower's startup groups equal the template's, no active action request outside them, the
    /// upper = the template's upper; internal names carry the file ids.</summary>
    public static int CheckAi(ESD lower, ESD upper, NpcIdle.NpcNet n, string ds2, Action<string> log)
    {
        int problems = 0;
        void Bad(string m) { problems++; log("PROBLEM " + m); }
        var tLow = ESD.Read(Path.Combine(ds2, "ezstate", $"ai{n.TemplateAnimId:D6}.esd"));
        var tHigh = ESD.Read(Path.Combine(ds2, "ezstate", $"ai{n.TemplateAnimId + 10:D6}.esd"));
        string Dump(ESD e, long g) => string.Join("\n", e.StateGroups[g].OrderBy(s => s.Key).Select(s => $"{s.Key}:" + string.Join("|", s.Value.EntryCommands.Concat(s.Value.WhileCommands).Concat(s.Value.ExitCommands).Select(c => $"{c.CommandBank}:{c.CommandID}({string.Join(",", c.Arguments.Select(Esd.Expr))})")) + "#" + string.Join("|", s.Value.Conditions.Select(c => Esd.Expr(c.Evaluator) + "->" + c.TargetState))));
        foreach (long g in NpcAi.StartupGroups) if (Dump(lower, g) != Dump(tLow, g)) Bad($"startup group {g} differs from ai{n.TemplateAnimId:D6}'s");
        if (lower.StateGroups.Count != tLow.StateGroups.Count) Bad("group count");
        int diffGroups = lower.StateGroups.Keys.Count(g => Dump(lower, g) != Dump(tLow, g));
        foreach (long g in upper.StateGroups.Keys.Union(tHigh.StateGroups.Keys))
            if (!upper.StateGroups.ContainsKey(g) || !tHigh.StateGroups.ContainsKey(g) || Dump(upper, g) != Dump(tHigh, g)) Bad($"upper group {g} differs from the template's");
        if (!(lower.Name ?? "").StartsWith($"ai{n.AiLow:D6}_") || !(upper.Name ?? "").StartsWith($"ai{n.AiHigh:D6}_")) Bad($"internal names '{lower.Name}' / '{upper.Name}'");
        var reqs = NpcAi.ActiveRequests(lower);
        var active = reqs.Where(x => x.StartsWith("ACTIVE")).ToList();
        foreach (var a in active) Bad($"ai{n.AiLow:D6}: {a}");
        // Non-vacuity (review finding AI-N1): the same reach rule must see the template's requests; ours must have none
        // that it cannot resolve to a constant or an exact pN.
        var tReqs = NpcAi.ActiveRequests(tLow);
        int tActive = tReqs.Count(x => x.StartsWith("ACTIVE"));
        if (tActive == 0) Bad($"the reach rule sees no action request in the template ai{n.TemplateAnimId:D6} (check is vacuous)");
        foreach (var u in reqs.Where(x => x.StartsWith("UNRESOLVED"))) Bad($"ai{n.AiLow:D6}: {u}");
        log($"npc-ai-check ai{n.TemplateAnimId:D6} (template, same rule): {tActive} active action requests outside the startup groups, {tReqs.Count(x => x.StartsWith("UNRESOLVED"))} unresolved");
        log($"npc-ai-check ai{n.AiLow:D6} / ai{n.AiHigh:D6} ({n.Who}, template ai{n.TemplateAnimId:D6} / ai{n.TemplateAnimId + 10:D6}): {lower.StateGroups.Count} groups, {diffGroups} differ from the template (action requests → -1), startup groups verbatim; requests left: {string.Join("; ", reqs.Where(x => !x.StartsWith("ACTIVE")))}; {active.Count} active outside the startup groups; upper = template: {(problems == 0 ? "yes" : "see problems")}");
        return problems;
    }

    /// <summary>npc-ai-check PACKDIR: <see cref="CheckAi"/> on every NPC AI copy the pack holds (rule AI-N1).</summary>
    public static int AiCheck(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: npc-ai-check PACKDIR"); return 2; }
        string ez = Path.Combine(args[1], "gamedata", "ezstate");
        int problems = 0, files = 0;
        foreach (var n in NpcIdle.Nets)
        {
            string lo = Path.Combine(ez, $"ai{n.AiLow:D6}.esd"), hi = Path.Combine(ez, $"ai{n.AiHigh:D6}.esd");
            if (!File.Exists(lo) || !File.Exists(hi)) { Console.WriteLine($"{n.Who}: ai{n.AiLow:D6} / ai{n.AiHigh:D6} not in the pack"); continue; }
            files++;
            problems += CheckAi(ESD.Read(lo), ESD.Read(hi), n, ExtractDs2.DefaultOut, Console.WriteLine);
        }
        Console.WriteLine($"npc-ai-check: {files} AI pair(s), {problems} problem(s)");
        return problems == 0 && files > 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ pose sheet

    static readonly (string A, string B)[] Limbs =
    [
        ("Root", "Pelvis"), ("Pelvis", "Spine"), ("Spine", "Spine1"), ("Spine1", "Neck"), ("Neck", "Head"),
        ("Spine1", "L_Clavicle"), ("L_Clavicle", "L_UpperArm"), ("L_UpperArm", "L_Forearm"), ("L_Forearm", "L_Hand"),
        ("Spine1", "R_Clavicle"), ("R_Clavicle", "R_UpperArm"), ("R_UpperArm", "R_Forearm"), ("R_Forearm", "R_Hand"),
        ("Pelvis", "L_Thigh"), ("L_Thigh", "L_Calf"), ("L_Calf", "L_Foot"), ("L_Foot", "L_Toe0"),
        ("Pelvis", "R_Thigh"), ("R_Thigh", "R_Calf"), ("R_Calf", "R_Foot"), ("R_Foot", "R_Toe0"),
    ];

    static void Sheet(NpcIdle.SlotResult s, NmbRig rig, NmbChannelMap map, string file)
    {
        const int size = 360;
        var frames = new[] { 0, s.Ds2.Frames / 4, s.Ds2.Frames / 2, 3 * s.Ds2.Frames / 4, s.Ds2.Frames - 1 };
        var img = new Image(size * frames.Length, size * 3);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = new Rgba(28, 28, 36, 255);
        var p1 = MorphemePose.From(Ds1RigAnims.WithDs2Names(s.Ds1.Skeleton));
        var p2 = MorphemePose.From(rig, map);
        double scale = s.Check.Scale, px = size / 2.0;
        for (int k = 0; k < frames.Length; k++)
        {
            int f = Math.Clamp(frames[k], 0, s.Ds2.Frames - 1);
            var (a, _) = p1.World(s.Ds1.Anim, Math.Min(f, s.Ds1.Anim.Frames - 1), false);
            var (b, _) = p2.World(s.Ds2, f, false);
            var (o, _) = p2.World(s.Ds2Original, Math.Min(f * (s.Ds2Original.Frames - 1) / Math.Max(1, s.Ds2.Frames - 1), s.Ds2Original.Frames - 1), false);
            for (int view = 0; view < 3; view++)
            {
                int ox = k * size, oy = view * size;
                // front view: x right = -X (character's right on screen left as seen from the front: forward -Y towards the
                // viewer), up = Z; side view: x = -Y (forward to the right), up = Z
                (int X, int Y) P(V3 v) => view == 0
                    ? (ox + size / 2 + (int)Math.Round(-v.X * px), oy + size * 2 / 3 - (int)Math.Round(v.Z * px))
                    : (ox + size / 2 + (int)Math.Round(-v.Y * px), oy + size * 2 / 3 - (int)Math.Round(v.Z * px));
                Line(img, P(new V3(-1.2, -1.2, 0)), P(new V3(1.2, 1.2, 0)), new Rgba(70, 70, 80, 255)); // ground
                if (view == 2)
                {
                    // third row: the DS2 stand-in clip the slot played before (side view, white)
                    foreach (var (ja, jb) in Limbs)
                    {
                        int ia = p2.Index(ja), ib = p2.Index(jb);
                        if (ia >= 0 && ib >= 0) Line(img, P(o[ia]), P(o[ib]), new Rgba(235, 235, 235, 255));
                    }
                    var r0 = P(new V3(0, 0, 0));
                    for (int d = -3; d <= 3; d++) { Put(img, r0.X + d, r0.Y, new Rgba(255, 60, 60, 255)); Put(img, r0.X, r0.Y + d, new Rgba(255, 60, 60, 255)); }
                    Line(img, P(new V3(0, 0, 0.02)), P(new V3(0, -0.4, 0.02)), new Rgba(255, 60, 60, 255));
                    continue;
                }
                foreach (var (ja, jb) in Limbs)
                {
                    int ia = p1.Index(ja), ib = p1.Index(jb);
                    if (ia >= 0 && ib >= 0) Line(img, P(Scale(a[ia], scale)), P(Scale(a[ib], scale)), new Rgba(150, 150, 150, 255));
                }
                foreach (var (ja, jb) in Limbs)
                {
                    int ia = p2.Index(ja), ib = p2.Index(jb);
                    var col = ja.StartsWith("L_") || jb.StartsWith("L_") ? new Rgba(90, 200, 255, 255) : jb.StartsWith("R_") ? new Rgba(255, 140, 80, 255) : new Rgba(240, 230, 120, 255);
                    if (ia >= 0 && ib >= 0) Line(img, P(b[ia]), P(b[ib]), col);
                }
                var root = P(new V3(0, 0, 0));
                for (int d = -3; d <= 3; d++) { Put(img, root.X + d, root.Y, new Rgba(255, 60, 60, 255)); Put(img, root.X, root.Y + d, new Rgba(255, 60, 60, 255)); }
                // forward arrow (-Y) in the side view
                if (view == 1) Line(img, P(new V3(0, 0, 0.02)), P(new V3(0, -0.4, 0.02)), new Rgba(255, 60, 60, 255));
            }
        }
        Png.Save(img, file);
    }

    static V3 Scale(V3 v, double s) => new(v.X * s, v.Y * s, v.Z * s);

    static void Put(Image img, int x, int y, Rgba c) { if (x >= 0 && y >= 0 && x < img.Width && y < img.Height) img[x, y] = c; }

    static void Line(Image img, (int X, int Y) a, (int X, int Y) b, Rgba c)
    {
        int n = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)) + 1;
        for (int i = 0; i <= n; i++)
        {
            int x = a.X + (b.X - a.X) * i / n, y = a.Y + (b.Y - a.Y) * i / n;
            Put(img, x, y, c); Put(img, x + 1, y, c);
        }
    }
}
