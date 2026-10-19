using System.Globalization;
using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// obj-nocol-survey [--placed] — session 8 (track nests): vanilla DS2 objects whose archive has NO collision (_h.hkx),
/// with their skeleton/animation files, MapObjectParam / GDParam (isBreakable, isPrefab) / DefaultInstanceParam
/// (objCtrlType, objType) rows, and with --placed how often vanilla MSBs place them and with which instance rows
/// (objCtrlType/objType of the MapObjectInstanceParam). Purpose: a template for DS1 objects that have only a model
/// (DS1 objbnd without 300 collision / 400 anibnd, e.g. the crow's nest o8550): what DS2's own collision-less static
/// objects look like, so such an object is ported without inventing a collision.
/// </summary>
public static class ObjNoColSurvey
{
    public static int Run(string[] args)
    {
        bool placed = args.Contains("--placed");
        var store = ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        var defs = Directory.GetFiles(Path.Combine(ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        PARAM.Row? Row(string p, int id) => store.Params.TryGetValue(p, out var param) ? param[id] : null;
        string V(PARAM.Row? r, string f) => r == null ? "-" : Convert.ToString(r[f].Value, CultureInfo.InvariantCulture) ?? "";
        // vanilla placements: model → (count, instance rows' objCtrlType/objType)
        var uses = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (placed)
            foreach (var msbPath in Directory.GetFiles(Path.Combine(ExtractDs2.DefaultOut, "map"), "*.msb", SearchOption.AllDirectories))
            {
                MSB2 msb;
                try { msb = MSB2.Read(msbPath); } catch { continue; }
                string map = Path.GetFileNameWithoutExtension(msbPath);
                PARAM? inst = null;
                string ip = Path.Combine(ExtractDs2.DefaultOut, "param", $"mapobjectinstanceparam_{map}.param");
                if (File.Exists(ip)) { try { inst = PARAM.Read(ip); inst.ApplyParamdefCarefully(defs); } catch { inst = null; } }
                foreach (var o in msb.Parts.Objects)
                {
                    var r = inst?.Rows.FirstOrDefault(x => x.ID == o.MapObjectInstanceParamID);
                    string desc = o.MapObjectInstanceParamID == 0 ? "inst0" : r == null ? $"inst{o.MapObjectInstanceParamID}(no row)"
                        : $"ctrl{V(r, "objCtrlType")}/type{V(r, "objType")}";
                    if (!uses.TryGetValue(o.ModelName, out var l)) uses[o.ModelName] = l = [];
                    l.Add($"{map}:{desc}{(o.Scale.X != 1 || o.Scale.Y != 1 || o.Scale.Z != 1 ? $" scale {o.Scale.X:0.##}" : "")}");
                }
            }
        int total = 0, nocol = 0;
        foreach (var path in Directory.GetFiles(Path.Combine(ExtractDs2.DefaultOut, "model", "obj"), "o*.bnd").Order())
        {
            string name = Path.GetFileNameWithoutExtension(path);
            int id;
            try { id = Build.Maps.ObjPorter.ModelId(name); } catch { continue; }
            BND4 bnd;
            try { bnd = BND4.Read(path); } catch { continue; }
            total++;
            var names = bnd.Files.Select(f => Path.GetFileName(f.Name)).ToList();
            if (names.Any(n => n.EndsWith("_h.hkx", StringComparison.OrdinalIgnoreCase))) continue;
            nocol++;
            var mo = Row("MapObjectParam", id); var gd = Row("MapObjectGDParam", id); var di = Row("MapObjectDefaultInstanceParam", id);
            string files = string.Join(",", names.Where(n => !n.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase)));
            int tex = names.Count(n => n.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase));
            string u = uses.TryGetValue(name, out var l) ? $" placed {l.Count}: {string.Join(" ", l.GroupBy(x => x.Split(':')[1]).Select(g => $"{g.Key}x{g.Count()}"))} ({string.Join(",", l.Select(x => x.Split(':')[0]).Distinct().Take(4))})" : placed ? " placed 0" : "";
            Console.WriteLine($"{name}\tMOP={(mo != null ? "y" : "-")} brk={V(gd, "isBreakable")} prefab={V(gd, "isPrefab")} defInst ctrl={V(di, "objCtrlType")} type={V(di, "objType")}\t{files} +{tex} tpf{u}");
        }
        Console.WriteLine($"# {nocol} of {total} DS2 objects have no collision file");
        return 0;
    }
}
