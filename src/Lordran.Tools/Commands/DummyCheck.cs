using Lordran.Tools.Build;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 13 (seam13): <c>dummy-check PACK</c> — rule DM1 (<see cref="DummyRules"/>): no FLVER in the pack (objects, characters,
/// parts, map pieces; loose, in BND4/BND3 binders, DCX-compressed or not, nested) is a rigid model (no Dynamic 1/2 mesh) with a
/// dummy that has Flag1 set and AttachBoneIndex ≥ 0: DS2 reads its null inverse-bind array on the first dummy query and crashes at
/// exe+0xb35dae. Also FAILs a skinned model whose attach bone index is past its node list (out-of-range read of the same array).
/// Exit 1 on any FAIL.
/// </summary>
public static class DummyCheck
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: dummy-check PACK"); return 2; }
        // every FLVER of a pack lives under gamedata\model (obj, chr, parts, map)
        string root = Path.Combine(args[1], "gamedata", "model");
        if (!Directory.Exists(root)) root = args[1];
        int flvers = 0, fails = 0, files = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string ext = path.ToLowerInvariant();
            if (!(ext.Contains("bnd") || ext.EndsWith(".flver") || ext.EndsWith(".flv") || ext.EndsWith(".dcx"))) continue;
            if (ext.EndsWith("bdt") || ext.EndsWith(".tpf") || ext.EndsWith(".hkx")) continue;
            files++;
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); } catch { continue; }
            Scan(Path.GetRelativePath(root, path), bytes, 0);
        }
        Console.WriteLine($"dummy-check: {files} files, {flvers} FLVERs, {fails} FAIL (rule DM1)");
        return fails > 0 ? 1 : 0;

        void Scan(string name, byte[] b, int depth)
        {
            if (depth > 4 || b.Length < 8) return;
            try
            {
                if (DCX.Is(b)) { Scan(name, DCX.Decompress(b), depth + 1); return; }
                if (BND4.Is(b)) { foreach (var f in BND4.Read(b).Files) Scan(name + "|" + Path.GetFileName(f.Name), f.Bytes, depth + 1); return; }
                if (BND3.Is(b)) { foreach (var f in BND3.Read(b).Files) Scan(name + "|" + Path.GetFileName(f.Name), f.Bytes, depth + 1); return; }
                if (!FLVER2.Is(b)) return;
            }
            catch { return; }
            FLVER2 flv;
            try { flv = FLVER2.Read(b); } catch (Exception e) { Console.WriteLine($"WARN {name}: unreadable FLVER ({e.Message})"); return; }
            flvers++;
            foreach (var d in DummyRules.Offenders(flv))
            {
                fails++;
                Console.WriteLine($"FAIL {name}: rigid model (no Dynamic mesh, no DS2 inverse-bind array) with dummy {d.ReferenceID} Flag1 + AttachBoneIndex {d.AttachBoneIndex} (parent {d.ParentBoneIndex}) -> DS2 crash exe+0xb35dae on its first dummy query");
            }
            if (DummyRules.HasBindArray(flv))
                foreach (var d in flv.Dummies.Where(d => d.Flag1 && d.AttachBoneIndex >= flv.Nodes.Count))
                {
                    fails++;
                    Console.WriteLine($"FAIL {name}: dummy {d.ReferenceID} AttachBoneIndex {d.AttachBoneIndex} past the {flv.Nodes.Count} nodes (DS2 reads past its inverse-bind array)");
                }
        }
    }
}
