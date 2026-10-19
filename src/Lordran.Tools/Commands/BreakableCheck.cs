using System.Text.RegularExpressions;
using Lordran.Tools.Formats.Havok;
using Lordran.Tools.Build.Maps;
using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// Session 12 (brk12): <c>breakable-check PACK [MAP...]</c> (default: every map of the pack) — every placed DS1 object has its
/// collision in game and every breakable a working break chain:
/// <list type="bullet">
/// <item><b>B1</b> (all our objects) every body of the intact collision binds to a FLVER node of its name, at the body's transform
/// (DS2 creates only those, FUN_140bcc840 → FUN_140bcc110; rule OB1). The Firelink pots' "o5550_h" / "o1496_h" bodies never
/// existed: no collision, nothing to hit or roll into (user: "no-clipping through pots in Firelink that weren't breaking").</item>
/// <item><b>B2</b> fragment bodies bind to fragment nodes, at least one is dynamic; FIXED fragments (DS1 base pieces) listed.</item>
/// <item><b>B3</b> MapObjectParam / GD / DefaultInstance rows; objType from DS1 (rule potroll-3) with hp &gt; 0.</item>
/// <item><b>B4</b> hit material = rule OBJ-MAT1 (DS1 materialId: wood → 7, else the jar's 19).</item>
/// <item><b>B5</b> sfxIdBreak0 −1 or a title-bank effect &lt; 30000 (rule B2b-id).</item>
/// <item><b>B6</b> break sound o{model×1000} defined once, by a DS1 project the map loads (rules OBJ-SND1, SND2 / H2-S).</item>
/// <item><b>B7</b> breakobj: one file per map, its entries name instance rows of the map. Vanilla Majula's jars (instance 0) have
/// none: the file stores pre-broken poses of instanced objects only, so instance-0 breakables need no entry.</item>
/// <item><b>B8</b> instance-0 parts carry the map's bonfire (UnkT04: DS2 restores them on resting there); instanced parts have rows.</item>
/// </list>
/// Exit 1 on any FAIL. Dump modes for research: --bind PACK, --ds1 oXXXX..., --bnd FILE.bnd..., --xf FILE.bnd..., --mat, --vanilla-static.
/// </summary>
public static class BreakableCheck
{
    public static int Run(string[] args)
    {
        if (args.Length > 2 && args[1] == "--ds1") return DumpDs1(args.Skip(2));
        if (args.Length > 2 && args[1] == "--bnd") return DumpBnd(args.Skip(2));
        if (args.Length > 1 && args[1] == "--bind") return Bind(args[2]);
        if (args.Length > 1 && args[1] == "--mat")
        {
            var op = Lordran.Tools.Build.Ds1Source.LoadParam(Program.DefaultDsrRoot, "ObjectParam")!;
            foreach (var g in op.Rows.Where(r => Convert.ToInt32(r["hp"].Value) > 0 && Convert.ToInt32(r["defense"].Value) == 0).GroupBy(r => Convert.ToInt32(r["materialId"].Value)).OrderBy(g => g.Key))
                Console.WriteLine($"DS1 mat {g.Key}: " + string.Join("; ", g.Select(r => $"{r.ID} {r.Name}")));
            var st = Lordran.Tools.Formats.ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
            var gd = st.Params["MapObjectGDParam"]; var mo = st.Params["MapObjectParam"];
            foreach (var g in mo.Rows.Where(r => gd[r.ID] is { } x && Convert.ToInt32(x["isBreakable"].Value) == 1).GroupBy(r => Convert.ToInt32(r["material"].Value)).OrderBy(g => g.Key))
                Console.WriteLine($"DS2 mat {g.Key}: " + string.Join("; ", g.Take(14).Select(r => $"{r.ID} {r.Name}")));
            return 0;
        }
        if (args.Length > 1 && args[1] == "--xf") { foreach (var p in args.Skip(2)) { var bnd = BND4.Read(p); foreach (var f in bnd.Files.Where(f => f.Name.EndsWith("_h.hkx"))) foreach (var b in ObjColSurvey.Bodies(new HkPackfile(f.Bytes).ReadRoot())) { var tr = (float[])b.Get("motion").Get("motionState")["transform"]!; Console.WriteLine($"{Path.GetFileName(f.Name)} {b["name"]} {string.Join(",", tr.Select(v => v.ToString("0.###")))}"); } } return 0; }
        if (args.Length > 1 && args[1] == "--vanilla-static") return VanillaStatic();
        if (args.Length < 2) { Console.Error.WriteLine("usage: breakable-check PACK [MAP...]"); return 1; }
        return Check(args[1], args.Skip(2).ToArray());
    }

    public static List<(string Name, int Motion, int Quality, string Shape)> BodiesOf(byte[] packfile)
    {
        var root = new HkPackfile(packfile).ReadRoot();
        return ObjColSurvey.Bodies(root).Select(b =>
        {
            var col = b.Get("collidable");
            return ((string)b["name"]!, (int)(Convert.ToInt64(b.Get("motion")["type"]) & 0xFF), Convert.ToInt32(col.Get("broadPhaseHandle")["objectQualityType"]), ObjColSurvey.Shape(col["shape"] as HkPObj));
        }).ToList();
    }

    static int DumpBnd(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            var bnd = BND4.Read(p);
            foreach (var f in bnd.Files)
            {
                string fn = Path.GetFileName(f.Name);
                Console.WriteLine($"{Path.GetFileNameWithoutExtension(p)} {fn} {f.Bytes.Length}");
                if (fn.EndsWith(".flv", StringComparison.OrdinalIgnoreCase))
                {
                    var fl = FLVER2.Read(f.Bytes);
                    Console.WriteLine("   nodes: " + string.Join(", ", fl.Nodes.Select((n, i) => $"{i}:{n.Name}(p{n.ParentIndex})")));
                }
                if (fn.EndsWith("_h.hkx", StringComparison.OrdinalIgnoreCase))
                    foreach (var b in BodiesOf(f.Bytes)) Console.WriteLine($"   body {b.Name} motion={b.Motion} q={b.Quality} {b.Shape}");
            }
        }
        return 0;
    }

    static int DumpDs1(IEnumerable<string> models)
    {
        foreach (var m in models)
        {
            var bnd = BND3.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(Program.DefaultDsrRoot, "obj", m + ".objbnd.dcx"))));
            foreach (var f in bnd.Files)
            {
                string fn = Path.GetFileName(f.Name);
                Console.WriteLine($"{m} id={f.ID} {fn} {f.Bytes.Length}");
                if (fn.EndsWith(".flver", StringComparison.OrdinalIgnoreCase))
                {
                    var fl = FLVER2.Read(f.Bytes);
                    Console.WriteLine("   nodes: " + string.Join(", ", fl.Nodes.Select((n, i) => $"{i}:{n.Name}(p{n.ParentIndex})")));
                    Console.WriteLine("   meshes: " + string.Join(", ", fl.Meshes.Select(x => $"node{x.NodeIndex}/dyn{x.Dynamic}/v{x.Vertices.Count}")));
                }
                if (fn.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine("   strings: " + string.Join(", ", Regex.Matches(System.Text.Encoding.ASCII.GetString(f.Bytes), @"[A-Za-z_][\w]{1,40}").Select(x => x.Value).Where(s => !s.StartsWith("hk") && !s.StartsWith("Hk")).Distinct().Take(60)));
            }
        }
        return 0;
    }

    /// <summary>Every body of every object archive in PACK binds to a FLVER node of the same name (intact _h → .flv, fragments _1_h → _1.flv).</summary>
    static int Bind(string pack)
    {
        int bad = 0;
        foreach (var p in Directory.GetFiles(Path.Combine(pack, "gamedata", "model", "obj"), "*.bnd").Order())
        {
            var bnd = BND4.Read(p);
            string n = Path.GetFileNameWithoutExtension(p);
            foreach (var (col, flv) in new[] { (n + "_h.hkx", n + ".flv"), (n + "_1_h.hkx", n + "_1.flv") })
            {
                var c = bnd.Files.FirstOrDefault(f => Path.GetFileName(f.Name).Equals(col, StringComparison.OrdinalIgnoreCase));
                var fl = bnd.Files.FirstOrDefault(f => Path.GetFileName(f.Name).Equals(flv, StringComparison.OrdinalIgnoreCase));
                if (c == null || fl == null) continue;
                var nodes = FLVER2.Read(fl.Bytes).Nodes.Select(x => x.Name).ToHashSet();
                List<(string Name, int Motion, int Quality, string Shape)> bodies;
                try { bodies = BodiesOf(c.Bytes); } catch (Exception e) { Console.WriteLine($"{n} {col}: unreadable {e.Message}"); continue; }
                var miss = bodies.Where(b => !nodes.Contains(b.Name)).ToList();
                var dup = bodies.GroupBy(b => b.Name).Where(g => g.Count() > 1).Select(g => $"{g.Key}x{g.Count()}").ToList();
                if (miss.Count > 0 || dup.Count > 0) { bad++; Console.WriteLine($"{n} {col}: {bodies.Count} bodies, unbound {string.Join(",", miss.Select(b => b.Name + "/m" + b.Motion))}; dup {string.Join(",", dup)}; nodes {string.Join(",", nodes.Take(6))}"); }
            }
        }
        Console.WriteLine($"{bad} files with unbound/duplicate bodies");
        return 0;
    }

    static string T(HkPObj b)
    {
        var ms = b.Get("motion").Get("motionState");
        var tr = ms["transform"];
        return tr is float[] f ? string.Join(",", f.Skip(12).Take(3).Select(v => v.ToString("0.##"))) + " rot " + string.Join(",", f.Take(3).Select(v => v.ToString("0.##"))) : (tr?.GetType().Name ?? "null") + " keys " + string.Join("/", ms.Fields.Keys);
    }

    static int VanillaStatic()
    {
        int shown = 0;
        foreach (var p in Directory.GetFiles(Path.Combine(ExtractDs2.DefaultOut, "model", "obj"), "o*.bnd").Order())
        {
            BND4 bnd; try { bnd = BND4.Read(p); } catch { continue; }
            string n = Path.GetFileNameWithoutExtension(p);
            if (bnd.Files.Any(f => Path.GetFileName(f.Name).Equals("skeleton.hkx", StringComparison.OrdinalIgnoreCase))) continue;
            var c = bnd.Files.FirstOrDefault(f => Path.GetFileName(f.Name).Equals(n + "_h.hkx", StringComparison.OrdinalIgnoreCase));
            var fl = bnd.Files.FirstOrDefault(f => Path.GetFileName(f.Name).Equals(n + ".flv", StringComparison.OrdinalIgnoreCase));
            if (c == null || fl == null) continue;
            List<HkPObj> bodies; try { bodies = ObjColSurvey.Bodies(new HkPackfile(c.Bytes).ReadRoot()).ToList(); } catch { continue; }
            var flv = FLVER2.Read(fl.Bytes);
            if (bodies.Count < 2) continue;
            if (shown++ > 25) break;
            Console.WriteLine($"{n}: nodes {string.Join(", ", flv.Nodes.Select(x => $"{x.Name}@{x.Translation.X:0.##},{x.Translation.Y:0.##},{x.Translation.Z:0.##}"))}");
            foreach (var b in bodies) Console.WriteLine($"    body {b["name"]} t {T(b)}");
        }
        return 0;
    }

    static readonly Regex PortLine = new(@"^object (o\d{4}) → (o\d\d_\d{4}) \(template (o\d\d_\d{4})\)");

    static int Check(string pack, string[] maps)
    {
        string dsr = Program.DefaultDsrRoot;
        if (maps.Length == 0)
            maps = Directory.GetDirectories(Path.Combine(pack, "gamedata", "map")).Select(d => Path.GetFileName(d)!)
                .Where(m => File.Exists(Path.Combine(pack, "gamedata", "map", m, m + ".msb"))).Order().ToArray();
        var ps = ParamStore.LoadDs2(Path.Combine(pack, "title"), ExtractDs2.DefaultOut);
        var van = ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        var objParam = Lordran.Tools.Build.Ds1Source.LoadParam(dsr, "ObjectParam")!;
        string[] build = File.Exists(Path.Combine(pack, "build.log")) ? File.ReadAllLines(Path.Combine(pack, "build.log")) : [];
        var ds1Of = build.Select(l => PortLine.Match(l)).Where(m => m.Success && m.Groups[3].Value == "o00_2100").GroupBy(m => m.Groups[2].Value).ToDictionary(g => g.Key, g => g.Last().Groups[1].Value); // breakables: ported on the jar
        // effects the title banks hold (vanilla + the pack's own append bank); rule B2b-id: DS2 aborts on an id >= 30000
        var fx = new HashSet<int>(FfxPorter.Ds2UsedIds());
        string ownAppend = Path.Combine(pack, "title", "sfx", "sfx9999_Append.ffxbnd.dcx");
        if (File.Exists(ownAppend))
            foreach (var f in BND4.Read(DCX.Decompress(File.ReadAllBytes(ownAppend))).Files)
            {
                string nm = Path.GetFileName(f.Name);
                if (nm.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase) && int.TryParse(nm[1..^4], out int fid)) fx.Add(fid);
            }
        // sounds: the pack's own FMOD projects (the DS1 projects it ships): event -> projects
        string fevPath = Path.Combine(pack, "gamedata", "sound", "frpg2.fevbnd.dcx");
        var vanillaFev = BND4.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine("data", "ds2", "sound", "frpg2.fevbnd.dcx")))).Files.Select(f => f.Name).ToHashSet();
        var ours = File.Exists(fevPath)
            ? SoundSurvey.FevIndex(BND4.Read(DCX.Decompress(File.ReadAllBytes(fevPath))).Files.Where(f => f.Name.EndsWith(".fev") && !vanillaFev.Contains(f.Name)).Select(f => (Path.GetFileNameWithoutExtension(f.Name), f.Bytes)))
            : [];
        var ds1Fev = SoundSurvey.FevIndex(SoundSurvey.Ds1Projects(dsr));

        int fails = 0, devs = 0, oks = 0;
        void Fail(string m) { fails++; Console.WriteLine("  FAIL " + m); }
        void Ok(string m) { oks++; Console.WriteLine("  ok   " + m); }
        void Dev(string m) { devs++; Console.WriteLine("  DEV  " + m); }
        int I(PARAM.Row r, string f) => Convert.ToInt32(r[f].Value);

        // ---- B7 evidence, once: vanilla Majula's breakables vs its breakobj entries
        {
            string vm = "m10_04_00_00";
            var vmsb = MSB2.Read(Path.Combine(ExtractDs2.DefaultOut, "map", vm, vm + ".msb"));
            var vgd = van.Params["MapObjectGDParam"];
            var vbr = vmsb.Parts.Objects.Where(p => vgd[ObjPorter.ModelId(p.ModelName)] is { } x && I(x, "isBreakable") == 1).ToList();
            var ids = BreakobjIds(File.ReadAllBytes(Path.Combine(ExtractDs2.DefaultOut, "breakobj", vm + ".breakobj")));
            int inst0 = vbr.Count(p => p.MapObjectInstanceParamID == 0);
            bool named = ids.All(i => vmsb.Parts.Objects.Any(p => p.MapObjectInstanceParamID == i));
            Console.WriteLine($"vanilla {vm}: {vbr.Count} breakable parts ({inst0} with instance 0), breakobj entries {ids.Count} [{string.Join(",", ids)}], "
                + $"all naming an instance row: {named} -> instance-0 breakables need no breakobj entry (B7)");
        }

        foreach (var map in maps)
        {
            var msb = MSB2.Read(Path.Combine(pack, "gamedata", "map", map, map + ".msb"));
            string? ds1Map = MapHosts.Ds1Of(map);
            var areas = ds1Map == null ? new HashSet<string>() : Lordran.Tools.Build.Events.MapSounds.Ds1ProjectsFor(ds1Map).Select(p => p.Memory).ToHashSet();
            string bo = Path.Combine(pack, "gamedata", "breakobj", map + ".breakobj");
            var boIds = File.Exists(bo) ? BreakobjIds(File.ReadAllBytes(bo)) : null;
            var inst = LoadPackParam(pack, $"param/mapobjectinstanceparam_{map}.param");
            Console.WriteLine($"breakable-check {map} (DS1 {ds1Map ?? "-"}): {msb.Parts.Objects.Count} object parts; breakobj {(boIds == null ? "missing" : boIds.Count + " entries")}");
            if (boIds == null) Fail($"{map}: breakobj/{map}.breakobj missing (DS2 loads one per map)");
            else foreach (var bid in boIds.Where(b => !msb.Parts.Objects.Any(p => p.MapObjectInstanceParamID == b))) Fail($"{map}: breakobj entry {bid} names no object instance of the map");
            int bound = 0;
            foreach (var g in msb.Parts.Objects.GroupBy(p => p.ModelName).OrderBy(g => g.Key))
            {
                string n = g.Key, bndPath = Path.Combine(pack, "gamedata", "model", "obj", n + ".bnd");
                if (!File.Exists(bndPath)) continue; // a vanilla DS2 object
                var bnd = BND4.Read(bndPath);
                byte[]? F(string name) => bnd.Files.FirstOrDefault(f => Path.GetFileName(f.Name).Equals(name, StringComparison.OrdinalIgnoreCase))?.Bytes;
                // ---- B1 (every object of ours)
                string bindIntact = BindProblems(F(n + "_h.hkx"), F(n + ".flv"), out var dupI, out int nI);
                if (bindIntact.Length > 0) Fail($"{n}: intact bodies never created in game (no FLVER node of their name / at their place; DS2 FUN_140bcc840, rule OB1): {bindIntact}");
                else if (nI > 0) bound++;
                if (dupI.Length > 0) Dev($"{n}: intact body names shared {dupI} (DS2's own data has such pairs: o10_2340, o16_1920)");
                int id = ObjPorter.ModelId(n);
                var gd = ps.Params["MapObjectGDParam"][id];
                bool breakable = gd != null && I(gd, "isBreakable") == 1 && F(n + "_1.flv") != null && (ds1Of.ContainsKey(n) || build.Length == 0);
                if (!breakable) continue;

                string? m1 = ds1Of.GetValueOrDefault(n);
                var row = m1 != null ? objParam[int.Parse(m1[1..])] : null;
                Console.WriteLine($"- {n} (DS1 {m1 ?? "?"}{(row != null ? $" {row.Name}: hp {row["hp"].Value}, material {row["materialId"].Value}, collide-break {row["isBreakByPlayerCollide"].Value}, breakSfx {row["breakSfxId"].Value}" : "")}): {g.Count()} parts");
                if (nI == 0) Fail($"{n}: no intact collision ({n}_h.hkx missing or empty): nothing to hit or roll into");
                else if (bindIntact.Length == 0) Ok($"{n}: {nI} intact bod{(nI == 1 ? "y" : "ies")} bound to model nodes (collision, hit and roll contact exist)");
                // ---- B2 fragments
                string bindFrag = BindProblems(F(n + "_1_h.hkx"), F(n + "_1.flv"), out var dupF, out int nF, place: false);
                var frags = F(n + "_1_h.hkx") is { } fh ? BodiesOf(fh) : [];
                int dyn = frags.Count(b => b.Motion != 5);
                if (bindFrag.Length > 0) Fail($"{n}: fragment bodies with no fragment node (never created): {bindFrag}");
                else if (dyn == 0) Fail($"{n}: no dynamic fragment body");
                else Ok($"{n}: {dyn} dynamic fragments (DS2 debris motion 3 / quality 4: pushed aside, they don't hold the player) bound to fragment nodes");
                if (dupF.Length > 0) Dev($"{n}: DS1 fragment names shared {dupF}: one of each pair is not created (DS1 data)");
                foreach (var b in frags.Where(b => b.Motion == 5)) Dev($"{n}: fragment {b.Name} stays FIXED as in DS1 (DS1's base piece left standing: {b.Shape})");
                // ---- B3 params / break trigger
                var mop = ps.Params["MapObjectParam"][id];
                var di = ps.Params["MapObjectDefaultInstanceParam"][id];
                if (mop == null || di == null) { Fail($"{n}: MapObjectParam / DefaultInstance row {id} missing"); continue; }
                int type = I(di, "objType");
                var it = ps.Params["MapObjectInstanceTypeParam"][type];
                bool collide = row != null && I(row, "isBreakByPlayerCollide") != 0;
                int want = collide ? MapBuilder.ContactBreakInstanceType : 1906;
                if (it == null) Fail($"{n}: objType {type} has no MapObjectInstanceTypeParam row");
                else if (row != null && type != want) Fail($"{n}: objType {type}, DS1 collide-break {(collide ? 1 : 0)} wants {want} (rule potroll-3)");
                else if (I(it, "hp") <= 0) Fail($"{n}: objType {type} hp {it["hp"].Value}: no hit breaks it");
                else Ok($"{n}: objType {type} (hp {it["hp"].Value}, breakAction {it["breakAction"].Value}: {(collide ? "any contact" : "roll / jump / fall into it")} or any hit breaks it; DS1 hp {row?["hp"].Value})");
                // ---- B4 hit material
                if (row != null)
                {
                    int wm = Breakables.Ds2Material(row), hm = I(mop, "material");
                    if (hm != wm) Fail($"{n}: MapObjectParam.material {hm}, rule OBJ-MAT1 wants {wm} (DS1 materialId {row["materialId"].Value} {row.Name})");
                    else Ok($"{n}: hit material {hm} ({(hm == 7 ? "DS2 wood, as its crates / barrels" : "DS2 pottery, as its jars")}) from DS1 materialId {row["materialId"].Value}");
                }
                // ---- B5 break effect
                int sfx = I(gd!, "sfxIdBreak0");
                if (sfx <= 0) Ok($"{n}: no break effect (sfxIdBreak0 {sfx}{(row != null && I(row, "breakSfxId") < 0 ? " = DS1 breakSfxId -1" : "")})");
                else if (sfx >= FfxPorter.LazyIdLimit) Fail($"{n}: sfxIdBreak0 {sfx} >= {FfxPorter.LazyIdLimit} (DS2 aborts, rule B2b-id)");
                else if (!fx.Contains(sfx)) Fail($"{n}: sfxIdBreak0 {sfx} in no title effect bank");
                else Ok($"{n}: break effect {sfx} (title bank, < {FfxPorter.LazyIdLimit})");
                // ---- B6 break sound
                string ev = $"o{id * 1000:D9}";
                if (ps.Params["SoundObjectBreakParam"][id] != null) Fail($"{n}: SoundObjectBreakParam row {id}: DS2 plays the generic break instead of {ev}");
                else if (ours.Count == 0) Dev($"{n}: pack without DS1 sound projects: {ev} silent");
                else if (!ours.TryGetValue(ev, out var own))
                {
                    if (m1 != null && !ds1Fev.ContainsKey(ObjectSounds.BreakEvent(m1))) Dev($"{n}: DS1 defines no break sound {ObjectSounds.BreakEvent(m1)}: silent in DS1 too");
                    else Fail($"{n}: break sound {ev} defined by no pack project");
                }
                else if (own.Count != 1) Fail($"{n}: break sound {ev} defined by {string.Join("+", own)}");
                else if (areas.Count > 0 && !areas.Contains(own[0])) Fail($"{n}: break sound {ev} in {own[0]}, a project {map} does not load ({string.Join("/", areas)})");
                else Ok($"{n}: break sound {ev} ({own[0]}, loaded on {map})");
                // ---- B8 placement / respawn
                foreach (var p in g)
                {
                    if (p.MapObjectInstanceParamID == 0) { if ((ushort)p.UnkT04 == 0) Fail($"{p.Name}: instance 0 without a bonfire in UnkT04 (rule potroll-5)"); }
                    else if (inst?[p.MapObjectInstanceParamID] is not { } ir) Fail($"{p.Name}: instance {p.MapObjectInstanceParamID} without a MapObjectInstanceParam row");
                    else if (Convert.ToByte(ir["onlyEventCanBreak"].Value) != 0) Dev($"{p.Name}: instance {p.MapObjectInstanceParamID} onlyEventCanBreak (a world-event part)");
                }
                var bonfires = g.Where(p => p.MapObjectInstanceParamID == 0).Select(p => (ushort)p.UnkT04).Distinct().ToList();
                if (bonfires.Count > 0) Ok($"{n}: {g.Count(p => p.MapObjectInstanceParamID == 0)} instance-0 parts, restored on resting at bonfire {string.Join("/", bonfires)} (DS2's jar rule; no breakobj entry needed, B7)");
            }
            Ok($"{map}: {bound} object models of ours with every intact body bound (B1)");
        }
        Console.WriteLine($"breakable-check: {oks} ok, {devs} deviations, {fails} FAIL");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>Bodies of a collision file without a FLVER node of their name ("" = all bound); for a baked (non-skinned) model a
    /// node away from its body counts too.</summary>
    static string BindProblems(byte[]? hkx, byte[]? flv, out string dups, out int count, bool place = true)
    {
        dups = ""; count = 0;
        if (hkx == null || flv == null) return "";
        var fl = FLVER2.Read(flv);
        var bodies = ObjColSurvey.Bodies(new HkPackfile(hkx).ReadRoot()).ToList();
        count = bodies.Count;
        bool skinned = fl.Meshes.Any(m => m.Dynamic != 0) || fl.Nodes.Any(x => x.ParentIndex >= 0);
        var bad = new List<string>();
        foreach (var b in bodies)
        {
            string name = b["name"] as string ?? "";
            int ni = fl.Nodes.FindIndex(x => x.Name == name);
            if (ni < 0) { bad.Add(name); continue; }
            if (skinned || !place) continue;
            var tr = (float[])b.Get("motion").Get("motionState")["transform"]!;
            var w = fl.Nodes[ni].ComputeLocalTransform();
            float[] got = [w.M11, w.M12, w.M13, w.M21, w.M22, w.M23, w.M31, w.M32, w.M33, w.M41, w.M42, w.M43];
            float[] exp = [tr[0], tr[1], tr[2], tr[4], tr[5], tr[6], tr[8], tr[9], tr[10], tr[12], tr[13], tr[14]];
            if (got.Zip(exp).Any(x => Math.Abs(x.First - x.Second) > 2e-3f))
                bad.Add($"{name} (node at {w.M41:0.##},{w.M42:0.##},{w.M43:0.##}, body at {tr[12]:0.##},{tr[13]:0.##},{tr[14]:0.##})");
        }
        dups = string.Join(",", bodies.GroupBy(b => b["name"] as string).Where(x => x.Count() > 1).Select(x => $"{x.Key}x{x.Count()}"));
        return string.Join(", ", bad);
    }

    /// <summary>breakobj: "OBJB", version, count; per entry: instance id, 2 x u32, fragment count, 8 bytes per fragment.</summary>
    public static List<int> BreakobjIds(byte[] b)
    {
        var ids = new List<int>();
        if (b.Length < 12 || b[0] != (byte)'O' || b[1] != (byte)'B') return ids;
        int n = BitConverter.ToInt32(b, 8), off = 12;
        for (int i = 0; i < n && off + 16 <= b.Length; i++)
        {
            ids.Add(BitConverter.ToInt32(b, off));
            off += 16 + 8 * BitConverter.ToInt32(b, off + 12);
        }
        return ids;
    }

    static PARAM? LoadPackParam(string pack, string rel)
    {
        string p = Path.Combine(pack, "gamedata", rel.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(p)) return null;
        var defs = Directory.GetFiles(Path.Combine(ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(x => PARAMDEF.XmlDeserialize(x)).ToList();
        var param = PARAM.Read(p);
        return param.ApplyParamdefCarefully(defs) ? param : null;
    }
}
