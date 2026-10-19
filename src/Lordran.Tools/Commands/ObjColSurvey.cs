using System.Globalization;
using Lordran.Tools.Formats;
using Lordran.Tools.Formats.Havok;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// obj-col-survey [--breakable] [--model oAA_BBBB] [BND...] — one line per rigid body of every object collision
/// (model/obj/*.bnd → *_h.hkx): motion type, broad-phase quality, collision filter info, npData, the shape chain with
/// convex radii, plus the model's MapObjectGDParam.isBreakable and MapObjectDefaultInstanceParam objType →
/// MapObjectInstanceTypeParam (size/weight/breakAction/hp/impact). Default input: every vanilla DS2 object; BND paths
/// (e.g. a built pack's o18_8410.bnd) are surveyed with the vanilla params of their model id when present.
/// Session 8 (track potroll): written to find DS2's conventions for breakable intact bodies (jar o00_2100: a box in a
/// hkpConvexTranslateShape, radius 0.005, FIXED, npData 0xFFFFFFFF) against our DS1 urn o18_8410 (a bare
/// hkpCylinderShape with radius 0, npData 0).
/// </summary>
public static class ObjColSurvey
{
    public static int Run(string[] args)
    {
        bool onlyBreakable = args.Contains("--breakable");
        string? model = args.SkipWhile(a => a != "--model").Skip(1).FirstOrDefault();
        var files = args.Skip(1).Where(a => a.EndsWith(".bnd", StringComparison.OrdinalIgnoreCase)).ToList();
        if (files.Count == 0) files = Directory.GetFiles(Path.Combine(ExtractDs2.DefaultOut, "model", "obj"), "o*.bnd").Order().ToList();
        var store = ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        PARAM.Row? Row(string p, int id) => store.Params.TryGetValue(p, out var param) ? param[id] : null;
        string V(PARAM.Row? r, string f) => r == null ? "-" : Convert.ToString(r[f].Value, CultureInfo.InvariantCulture) ?? "";
        var shapeCount = new Dictionary<string, int>();
        foreach (var path in files)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (model != null && !name.Equals(model, StringComparison.OrdinalIgnoreCase)) continue;
            int id;
            try { id = Build.Maps.ObjPorter.ModelId(name); } catch { continue; }
            var gd = Row("MapObjectGDParam", id);
            bool breakable = gd != null && Convert.ToInt32(gd["isBreakable"].Value) != 0;
            if (onlyBreakable && !breakable) continue;
            var di = Row("MapObjectDefaultInstanceParam", id);
            var it = di != null ? Row("MapObjectInstanceTypeParam", Convert.ToInt32(di["objType"].Value)) : null;
            string ps = $"brk={(gd == null ? "-" : breakable ? "1" : "0")} objType={V(di, "objType")} size={V(it, "sizeType")} weight={V(it, "weightType")} breakAction={V(it, "breakAction")} hp={V(it, "hp")} impact={V(it, "impact")}";
            BND4 bnd;
            try { bnd = BND4.Read(path); } catch { continue; }
            foreach (var f in bnd.Files.Where(f => f.Name.EndsWith("_h.hkx", StringComparison.OrdinalIgnoreCase)))
            {
                HkPObj root;
                try { root = new HkPackfile(f.Bytes).ReadRoot(); } catch (Exception e) { Console.WriteLine($"{name}\t{Path.GetFileName(f.Name)}\t(unreadable: {e.Message})"); continue; }
                foreach (var body in Bodies(root))
                {
                    var col = body.Get("collidable");
                    var bp = col.Get("broadPhaseHandle");
                    var motion = body.Get("motion");
                    string shape = Shape(col["shape"] as HkPObj);
                    if (!Path.GetFileName(f.Name).Contains("_1_h", StringComparison.OrdinalIgnoreCase) && breakable)
                        shapeCount[shape.Split('(')[0]] = shapeCount.GetValueOrDefault(shape.Split('(')[0]) + 1;
                    Console.WriteLine($"{name}\t{Path.GetFileName(f.Name)}\t{body["name"]}\tmotion={Convert.ToInt64(motion["type"]) & 0xFF}\tq={bp["objectQualityType"]}\tfilter=0x{Convert.ToUInt32(bp["collisionFilterInfo"]):X}\tnp=0x{Convert.ToUInt32(body["npData"] ?? 0u):X}\t{shape}\t{ps}");
                }
            }
        }
        if (shapeCount.Count > 0)
            Console.WriteLine("breakable intact bodies by top shape: " + string.Join(", ", shapeCount.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}")));
        return 0;
    }

    static IEnumerable<HkPObj> Bodies(HkPObj root)
    {
        foreach (var nv in root.List("namedVariants").OfType<HkPObj>())
            if (nv["variant"] is HkPObj { Class: "hkpPhysicsData" } pd)
                foreach (var sys in pd.List("systems").OfType<HkPObj>())
                    foreach (var b in sys.List("rigidBodies").OfType<HkPObj>()) yield return b;
    }

    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static string Vec(object? o) => o is float[] f ? $"({F(f[0])},{F(f[1])},{F(f[2])})" : "?";

    /// <summary>Shape chain: Class(radius, key fields) > child …</summary>
    public static string Shape(HkPObj? s)
    {
        if (s == null) return "null";
        string c = s.Class.StartsWith("hkp") ? s.Class[3..].Replace("Shape", "") : s.Class;
        var parts = new List<string>();
        if (s["radius"] is float r) parts.Add("r" + F(r));
        if (s["halfExtents"] != null) parts.Add("he" + Vec(s["halfExtents"]));
        if (s["translation"] != null) parts.Add("t" + Vec(s["translation"]));
        if (s["cylRadius"] is float cr) parts.Add($"cyl{F(cr)} a{Vec(s["vertexA"])} b{Vec(s["vertexB"])}");
        if (s["numVertices"] is int nv) parts.Add("verts " + nv);
        string self = $"{c}({string.Join(" ", parts)})";
        HkPObj? child = (s["childShape"] as HkPObj) is { } cs ? (cs.Class == "hkpSingleShapeContainer" ? cs["childShape"] as HkPObj : cs) : null;
        if (s["child"] is HkPObj ch) child = ch["childShape"] as HkPObj ?? ch;
        if (s["childInfo"] is List<object?> list) return self + " > [" + string.Join("; ", list.OfType<HkPObj>().Select(i => Shape(i["shape"] as HkPObj))) + "]";
        return child != null ? self + " > " + Shape(child) : self;
    }
}
