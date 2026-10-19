using System.Text;
using Lordran.Tools.Formats.Havok;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// A DS1 object (DSR obj\oNNNN.objbnd.dcx) as a real DS2 object (model/obj/oAA_BBBB.bnd) — session 6, first for the
/// Asylum's doors. DS1 objbnd: 200 FLVER (skinned over the object's bones), 300 collision (Havok 2015 tagfile: one rigid
/// body per moving bone), 400 anibnd (a00_NNNN.hkx animations, skeleton.hkx, a TAE). DS2 object: FLVER(s) + .mte, Havok
/// 2014 packfiles (skeleton.hkx, a00_01_NNNN.hkx animations listed by AnimList.list, NAME_h.hkx collision), raw .tpf
/// textures and an ACB listing them; MapObjectParam / MapObjectGDParam rows keyed AA×10000 + BBBB.
///  - model: MapPieceConverter in object mode (DS2 map materials without GI, DS1 skinning, nodes and dummy polys kept);
///    textures named "NAME_…" (the object's own; no texture shared by name with another owner, see ChrPorter);
///  - Havok files: <see cref="HkTagToPack"/> with the template's own files as templates (same Havok classes);
///  - animations: DS1 a00_NNNN → DS2 file names given by the caller (AnimList.list ids from the template);
///  - params and the rest of the archive from a DS2 template object of the same kind.
/// </summary>
public static class ObjPorter
{
    public enum Kind { Door, Ladder, Prefab, Static, Breakable, Decor, DemoActor, Scenery }

    /// <param name="Kind">Door: a skinned object with animations (placed with a door instance row); Ladder: a static
    /// object whose behaviour comes from MapObjectDefaultInstanceParam (objCtrlType 4), placed with instance id 0;
    /// Prefab: an event-driven object (session 7): the template's state machine (prefabeditor/TEMPLATE.pfbbin, e.g.
    /// o10_1001 bars: 10 initial → 70 plays anim 10001 → 30 → 80 plays anim 10002 → 10), driven by event command
    /// 131636(instance, state); the caller names our DS1 animations after the ids the template's states play, and
    /// places the parts itself (MapBuilder.Options.Placements); Static: a model with collision and no behaviour (DS1's
    /// intact hall floor o8510 on o16_1230), also placed by the caller; Breakable (session 8, user: "pots should be
    /// actual items you can roll into and break"): DS1 breakables (ObjectParam.hp ≠ -1, defense 0) are built like DS2's:
    /// intact model + body (DS1 NAME.flver/.hkx → n.flv/n_h.hkx) and fragments + fragment bodies (NAME_1.flver/.hkx →
    /// n_1.flv/n_1_h.hkx), template DS2's jar o00_2100 (GDParam.isBreakable 1), placed with instance 0 like DS2's jars;
    /// Decor: an unbreakable DS1 object (hp -1 or defense > 0: pillars, gargoyles, bags) as a static object with DS1's
    /// collision, placed at every DS1 part with instance 0; DemoActor (session 8, cutscene track): a DS1 CHARACTER
    /// (chr\cNNNN.chrbnd.dcx: FLVER 200, textures in cNNNN.chrtpfbhd/.chrtpfbdt, chr\cNNNN.anibnd.dcx: Skeleton.hkx and
    /// a00_NNNN clips) as a skinned DS2 object without collision that only demos show and animate — DS2's own pattern:
    /// the bird that carries the player in scn101010 is the object o00_6090 (a creature model, template here), placed in
    /// the map as a demo-only part (draw group 110); the demo's tracks drive its bones by name; Scenery (session 8, track
    /// nests): a DS1 object that is only a model (objbnd: FLVER 200, no 300 collision, no 400 anibnd; e.g. the crow's
    /// nest o8550, ObjectParam hp -1) whose part DS1's events show/hide, as a DS2 object WITHOUT collision (DS1 has none:
    /// the player stands on the map collision) so 130702(instance, 0|1) can switch it; template a collision-less DS2
    /// static object (Things Betwixt's o02_8500, placed there with a generic event instance row); placed by the caller.</param>
    /// <param name="InstanceType">MapObjectInstanceTypeParam row for the model's MapObjectDefaultInstanceParam.objType
    /// instead of the template's (0 = keep). Session 8: DS1 breakables that break when the player walks into them
    /// (ObjectParam.isBreakByPlayerCollide 1: tubs o8400, urn o8403) get DS2's own contact-break type 1150 (breakAction 0);
    /// the others keep the jar's 1906 (breakAction 4 "Avoidance": rolling breaks them, walking doesn't).</param>
    public sealed record Options(string Ds1Obj, string Ds2Name, string TemplateObj, (string Ds1Anim, string Ds2Anim)[] Anims, Kind Kind = Kind.Door, int InstanceType = 0)
    {
        /// <summary>Test-only conversion (in-game A/B, never in a normal build); a variant is placed by the map's script.</summary>
        public TestVariant Variant { get; init; } = TestVariant.None;
        /// <summary>Session 11 (burg11): DS1 has no collision for the model (the Burg's rolling barrel o1330: DS1 hurts with a
        /// damaging sphere, 2005:9) - no _h.hkx is written instead of the template's (a door body bound to no node).</summary>
        public bool NoCollision { get; init; }
    }

    /// <summary>
    /// Test-only breakable conversions for the roll-break A/B (session 8, track potroll; AsylumScript LR_JAR_CONTROL=1).
    /// Statically the roll-break chain is the same for our urn and DS2's jars (NOTES.md, track potroll), so each variant
    /// changes one suspect of our DS1 urn and the in-game run shows which one decides:
    /// LegacyHavok: the collision exactly as before <see cref="Ds2PhysicsConventions"/> and the fragment quality rule
    /// (npData 0, dispatchType 0, convex radius 0, fragments DEBRIS quality) — tells whether those conventions matter;
    /// Ds2IntactBody: DS1's models and fragments, but DS2's jar body (o00_2100_h: a box in a translate shape) as the
    /// intact collision — tells whether our intact cylinder body is the cause;
    /// NoFragmentBase: DS1's fragment file without its FIXED base plate in game (the plate body, named like the model and
    /// so bound to the fragment model's root node, is renamed NAME_base, which no FLVER node matches; DS2 instantiates
    /// only bodies whose name matches a node, FUN_140bcc840 → FUN_140bcc110) — tells whether the plate interferes.
    /// Session 9 (crow track, demo actors; Cutscenes LR_CROW_AB): DemoPrefab ships the template's prefab state machine and
    /// keeps GDParam.isPrefab (the vanilla bird's object kind); DemoNoAlign keeps DS1's FLVER node tree (no rule R3c),
    /// i.e. the s8-8 model — the control for R3c.
    /// </summary>
    public enum TestVariant { None, LegacyHavok, Ds2IntactBody, NoFragmentBase, DemoPrefab, DemoNoAlign }

    public static int ModelId(string ds2Name) => int.Parse(ds2Name.Substring(1, 2)) * 10000 + int.Parse(ds2Name.Substring(4, 4));

    public static void Port(Pack pack, string dsrRoot, Options o, Ds2MapMaterials materials)
    {
        string data = pack.Ds2Data, t = o.TemplateObj, n = o.Ds2Name;
        if (t.Length != n.Length) throw new InvalidOperationException("template and new object names must have the same length (ACB strings are replaced in place)");
        var tBnd = BND4.Read(Path.Combine(data, "model", "obj", t + ".bnd"));
        BinderFile T(string name) => tBnd.Files.First(f => Path.GetFileName(f.Name).Equals(name, StringComparison.OrdinalIgnoreCase));
        bool chr = o.Kind == Kind.DemoActor;
        var ds1 = BND3.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(dsrRoot, chr ? "chr" : "obj", o.Ds1Obj + (chr ? ".chrbnd.dcx" : ".objbnd.dcx")))));
        // session 11 (burg11, rules SD0 / OK1): a Static or Decor object never moves: an animated DS1 model (a door kept shut) is baked in its
        // bind pose like a static one (no skeleton; the Static template o16_1230 has none)
        var anibndFile = o.Kind is Kind.Static or Kind.Decor or Kind.Ladder ? null : ds1.Files.FirstOrDefault(f => f.Name.EndsWith(".anibnd", StringComparison.OrdinalIgnoreCase));
        var anibnd = chr ? BND3.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(dsrRoot, "chr", o.Ds1Obj + ".anibnd.dcx"))))
            : anibndFile != null ? BND3.Read(anibndFile.Bytes) : null;
        BinderFile A(string name) => anibnd!.Files.First(f => Path.GetFileName(f.Name).Equals(name, StringComparison.OrdinalIgnoreCase));

        // ---- model + textures
        // skinned objects (doors) keep vertices in their bones' space; static ones (ladders) are baked like map pieces
        var conv = new MapPieceConverter(materials, n) { ObjectMode = anibnd != null, Gi = false, TexPrefix = n + "_" };
        var tFlv = FLVER2.Read(T(t + ".flv").Bytes);
        var flver = conv.Convert(FLVER2.Read(ds1.Files.First(f => f.ID == 200).Bytes), n, tFlv);
        // session 9 (crowfix): skinned meshes (rule §3): DS2 uploads face-set Unk06 palette matrices per draw
        if (flver.Meshes.Where(m => m.Dynamic != 0).Select(m => (int)m.FaceSets[0].Unk06).ToList() is { Count: > 0 } skin)
            pack.Note($"object {o.Ds1Obj} → {n}: {skin.Count} skinned meshes (rule §3): face-set Unk06 = palette slots the vertices use ({skin.Min()}–{skin.Max()}; "
                + $"map pieces' 1 left the other slots stale); {string.Join("; ", conv.SkinnedNotes.Where(s => s.Contains("WARNING")).DefaultIfEmpty("no vertex past its palette"))}");
        // a character's skeleton root "Master" takes the model's name, as DS2 objects name theirs (o00_6090's bone 0 =
        // "o00_6090"; demo tracks "<actor>_<bone>" then pair with the object's bones, the actor root with the model root)
        byte[]? skeleton = anibnd == null ? null
            : HkTagToPack.Convert(A(anibnd.Files.Any(f => Path.GetFileName(f.Name).Equals("skeleton.hkx", StringComparison.OrdinalIgnoreCase)) ? "skeleton.hkx" : "skelton.hkx").Bytes, // session 11: some DS1 anibnds spell it "skelton.hkx" (o1200, o1201)
                new HkPackfile(T("skeleton.hkx").Bytes), chr ? new Dictionary<string, string> { ["Master"] = n } : null);
        // session 9 (crow track, rule R3c): the FLVER node tree = the skeleton tree (the demo binder copies each track's
        // local transform onto the node of the same name, so a node's parent must be the bone its track is local to)
        if (chr && skeleton != null && o.Variant != TestVariant.DemoNoAlign)
            pack.Note($"object {o.Ds1Obj} → {n}: {AlignNodesToSkeleton(flver, n, skeleton)}");
        // DS2 doors need dummy polys DS1's don't have: 190 (MapObjectParam.moveDumyPolyId: where the player stands to
        // open, on the "dummy" node at the doorway's centre) and 191 (on the moving leaf, at the handle). Taken from the
        // template, re-parented to our nodes: the "dummy" node and the first "_00" node (DS1's leaf), positioned for the
        // DS1 model's width (its bounding box).
        if (anibnd == null)
        {
            // static object: the converter baked the node transforms and wrote one node; keep DS1's dummy polys (ladders:
            // 191 bottom / 192 top, the same ids DS2's ladders use) under it, on the ladder's rail plane like DS2's
            // (DS1 puts them 0.5 m in front of / behind it)
            // DS2's ladders (o00_0903/0904) keep 191/192 on the front rails: mesh z -0.08..0.19 (brackets back to the
            // wall), dummies at z 0, i.e. 0.08 behind the front. Same here (the depth centre put the climber into the wall:
            // "Climb up" showed but didn't mount, run s6-13).
            float rail = flver.Header.BoundingBoxMin.Z + 0.08f;
            foreach (var d in FLVER2.Read(ds1.Files.First(f => f.ID == 200).Bytes).Dummies)
                flver.Dummies.Add(new FLVER.Dummy
                {
                    ReferenceID = d.ReferenceID, ParentBoneIndex = 0, AttachBoneIndex = -1, Forward = d.Forward, Upward = d.Upward,
                    UseUpwardVector = d.UseUpwardVector, Color = d.Color, Flag1 = d.Flag1,
                    Position = o.Kind == Kind.Ladder && d.ReferenceID is 191 or 192 ? new System.Numerics.Vector3(0, d.Position.Y, rail) : d.Position,
                });
        }
        foreach (var td in tFlv.Dummies.Where(d => anibnd != null && !chr && d.ReferenceID is 190 or 191))
        {
            if (flver.Dummies.Any(d => d.ReferenceID == td.ReferenceID)) continue;
            int parent = td.ReferenceID == 190 ? flver.Nodes.FindIndex(x => x.Name == "dummy") : flver.Nodes.FindIndex(x => x.Name.EndsWith("_00"));
            if (parent < 0) continue;
            float width = flver.Header.BoundingBoxMax.X - flver.Header.BoundingBoxMin.X;
            var d = new FLVER.Dummy
            {
                ReferenceID = td.ReferenceID, ParentBoneIndex = (short)parent, AttachBoneIndex = -1, Forward = td.Forward, Upward = td.Upward,
                UseUpwardVector = td.UseUpwardVector, Color = td.Color, Flag1 = td.Flag1,
                Position = td.ReferenceID == 190 ? new System.Numerics.Vector3(flver.Header.BoundingBoxMin.X + width / 2, 0, 0)
                                                 : new System.Numerics.Vector3(flver.Header.BoundingBoxMin.X + width * 0.9f, 1, 0),
            };
            flver.Dummies.Add(d);
        }
        var ds1Tex = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in ds1.Files.Where(f => f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase)))
            foreach (var tex in TPF.Read(f.Bytes).Textures) ds1Tex.TryAdd(tex.Name, tex.Bytes);
        // a character's textures: the BXF3 split between the chrbnd's .chrtpfbhd and chr\cNNNN.chrtpfbdt, one TPF each
        if (chr && ds1.Files.FirstOrDefault(f => f.Name.EndsWith(".chrtpfbhd", StringComparison.OrdinalIgnoreCase)) is { } bhd)
            foreach (var f in BXF3.Read(bhd.Bytes, File.ReadAllBytes(Path.Combine(dsrRoot, "chr", o.Ds1Obj + ".chrtpfbdt"))).Files)
                foreach (var tex in TPF.Read(f.Bytes).Textures) ds1Tex.TryAdd(tex.Name, tex.Bytes);
        // the DS1 area of a DS2 object name: same number, except our area 19 = DS1 area 10 (DS1 m10_BB maps live in m19_BB)
        string ds1Area = n.Substring(1, 2) == "19" ? "m10" : "m" + n.Substring(1, 2);
        var areaTex = new Ds1MapTextures(dsrRoot, ds1Area);
        byte[]? TexBytes(string name) => ds1Tex.GetValueOrDefault(name) ?? areaTex.Dds(name);

        var files = new List<BinderFile>();
        void Add(string name, byte[] bytes, Binder.FileFlags flags = Binder.FileFlags.Flag1) => files.Add(new BinderFile(flags, files.Count, name, bytes));
        if (anibnd != null)
        {
            if (tBnd.Files.Any(f => Path.GetFileName(f.Name).Equals("AnimList.list", StringComparison.OrdinalIgnoreCase))) Add("AnimList.list", T("AnimList.list").Bytes);
            var tAnim = new HkPackfile(tBnd.Files.First(f => Path.GetFileName(f.Name).StartsWith("a00_", StringComparison.OrdinalIgnoreCase)).Bytes);
            foreach (var (ds1Anim, ds2Anim) in o.Anims)
                // a character's clips carry root motion (hkaDefaultAnimatedReferenceFrame), which no DS2 object clip
                // has: decoded and re-encoded without it (Formats.Ds2Scn.WriteClip), root bone "Master" named like the model
                Add(ds2Anim + ".hkx", chr ? Formats.Ds2Scn.WriteClip(new HkSplineAnim(new HkTagfile(A(ds1Anim + ".hkx").Bytes)), "Master", n, tAnim)
                    : HkTagToPack.Convert(A(ds1Anim + ".hkx").Bytes, tAnim));
        }
        // session 13 (seam13), rule DM1 (Build/DummyRules.cs): a rigid model has no inverse-bind array in DS2, so DS1's "attached"
        // dummies (Flag1 + AttachBoneIndex) become bone-relative (the barrel o1330 crashed DS2 at exe+0xb35dae)
        if (DummyRules.Apply(flver) is > 0 and var dm1) pack.Note($"object {o.Ds1Obj} → {n}: rule DM1: {dm1} attached dummies on a rigid model made bone-relative (DS2 crash exe+0xb35dae otherwise)");
        Add(n + ".flv", flver.Write());
        var mte = T(t + ".mte.dcx");
        Add(n + ".mte.dcx", mte.Bytes, mte.Flags);
        // breakables: the fragments model (DS1 NAME_1.flver: one node per fragment) and their bodies; fragment nodes and
        // bodies keep DS1's names with the model prefix swapped (o8410_22 → o18_8410_22), so they still pair up
        MapPieceConverter? fragConv = null;
        var ds1Frag = ds1.Files.FirstOrDefault(f => Path.GetFileName(f.Name).Equals(o.Ds1Obj + "_1.flver", StringComparison.OrdinalIgnoreCase));
        if (o.Kind == Kind.Breakable && ds1Frag != null)
        {
            fragConv = new MapPieceConverter(materials, n + "_1") { ObjectMode = true, Gi = false, TexPrefix = n + "_" };
            var frag = fragConv.Convert(FLVER2.Read(ds1Frag.Bytes), n + "_1", FLVER2.Read(T(t + "_1.flv").Bytes));
            foreach (var node in frag.Nodes)
                if (node.Name.StartsWith(o.Ds1Obj, StringComparison.OrdinalIgnoreCase)) node.Name = n + node.Name[o.Ds1Obj.Length..];
            if (DummyRules.Apply(frag) is > 0 and var dm1f) pack.Note($"object {o.Ds1Obj} → {n}_1: rule DM1: {dm1f} attached dummies made bone-relative");
            Add(n + "_1.flv", frag.Write());
            var mte1 = T(t + "_1.mte.dcx");
            Add(n + "_1.mte.dcx", mte1.Bytes, mte1.Flags);
            var fragCol = ds1.Files.FirstOrDefault(f => Path.GetFileName(f.Name).Equals(o.Ds1Obj + "_1.hkx", StringComparison.OrdinalIgnoreCase));
            if (fragCol != null)
            {
                var rename = PrefixRename(fragCol.Bytes, o.Ds1Obj, n);
                // test variant: the base plate (DS1 body named exactly like the model) under a name no fragment node has
                if (o.Variant == TestVariant.NoFragmentBase && rename.ContainsKey(o.Ds1Obj)) rename[o.Ds1Obj] = n + "_base";
                Add(n + "_1_h.hkx", o.Variant == TestVariant.LegacyHavok
                    ? ConvertCollision(data, fragCol.Bytes, T(t + "_1_h.hkx").Bytes, rename, LegacyFragmentFixup, ds2Conventions: false)
                    : ConvertCollision(data, fragCol.Bytes, T(t + "_1_h.hkx").Bytes, rename, FragmentFixup));
            }
        }
        // collision: DS1's when it has one; DS1 ladders have none (map collision holds the player), DS2's want the file;
        // demo actors none (their template, DS2's demo bird o00_6090, has no collision file; a chrbnd's 300 is a ragdoll)
        var ds1Col = chr ? null : ds1.Files.FirstOrDefault(f => Path.GetFileName(f.Name).Equals(o.Ds1Obj + ".hkx", StringComparison.OrdinalIgnoreCase)) ?? ds1.Files.FirstOrDefault(f => f.ID == 300);
        byte[]? col = null;
        Action<HkPObj>? ds1Fixup = null;
        var staticBinds = new Dictionary<string, (string Name, float[]? Node)>(); // rule OB1: original body name → bound name (+ the node it needs)
        if (ds1Col != null)
        {
            Action<HkPObj> fixup = anibnd == null ? StaticFixup : BoneBodyFixup;
            // session 12 (brk12), rule OB1: a baked (non-animated) object's model has ONE node, named like the DS2 model; DS2 creates
            // only bodies whose name matches a FLVER node (FUN_140bcc840 → FUN_140bcc110). DS1 names its bodies freely (o1495's
            // "o5550_h", o1496's "o1496_h", the Burg crates' "o1132_01", o8400's 15 "o8400_NN"), so those bodies were never
            // created: no collision, nothing to hit or roll into (user: "no-clipping through pots in Firelink that weren't
            // breaking"). Each body is bound: the first identity-placed one takes the model name, every other one gets its own
            // mesh-less node (n_bNN) at the body's own transform (DS2's convention: o00_2004 / o15_9009 nodes sit at their bodies).
            if (anibnd == null && o.Variant != TestVariant.Ds2IntactBody)
            {
                var inner = fixup;
                fixup = p => { inner(p); if (p.Class == "hkpRigidBody") BindStaticBody(p, n, staticBinds); };
            }
            ds1Fixup = fixup;
            // objects that rise to open (the Asylum's escape gate: 1.83 m): DS1's collision then leaves 1.61 m, enough for
            // DS1's player, not DS2's (user, session 8: "invisible wall at this gate"; MP blocked under the raised bars,
            // run s8-1). DS2's own bars (o10_1001) clear ~2.4 m. Rule: shapes touching the floor are trimmed from below
            // so the opening clears ClearOpen (the closed gap at the floor stays far too low to pass).
            float rise = anibnd == null ? 0 : o.Anims.Select(a => MaxRise(A(a.Ds1Anim + ".hkx").Bytes)).DefaultIfEmpty(0).Max();
            if (rise > 0.5f)
            {
                var bone = fixup;
                fixup = p => { bone(p); ClearanceFixup(p, rise); };
                pack.Note($"object {o.Ds1Obj}: opens by rising {rise:0.00} m; collision trimmed to clear {ClearOpen} m");
            }
            if (o.Variant == TestVariant.Ds2IntactBody)
            {
                // test variant: the template's own intact body (DS2 jar: box in a translate shape) under our name
                col = Replace(T(t + "_h.hkx").Bytes, t, n);
                pack.Note($"object {o.Ds1Obj} → {n}: TEST variant {o.Variant}: intact collision = {t}_h.hkx");
            }
            else try { col = ConvertCollision(data, ds1Col.Bytes, T(t + "_h.hkx").Bytes, BodyRename(o.Ds1Obj, n, anibnd != null), fixup, ds2Conventions: o.Variant != TestVariant.LegacyHavok); }
            catch (InvalidDataException) when (TryUnwrapped(out var unwrapped))
            {
                // session 11 (merge11, boulder defect 3, rule WE4c): last resort for DS1's offset sphere (o8530:
                // hkpConvexTranslateShape r 0.6024 over hkpSphereShape, which no DS2 file has together): the sphere alone,
                // DS1's own body (named o1330_00 = the FLVER node its clip drives, so DS2 binds it by node name), offset dropped
                col = unwrapped;
                pack.Note($"object {o.Ds1Obj}: DS1 collision converted with its offset sphere unwrapped (hkpConvexTranslateShape: no DS2 file has it with hkpSphereShape; offset dropped)");
            }
            catch (InvalidDataException e)
            {
                // e.g. the boulder o8530: hkpConvexTranslateShape + hkpSphereShape, which no single DS2 file's type table
                // has together (o37_9240's two collisions have one each). The template's collision stands in.
                pack.Note($"object {o.Ds1Obj}: DS1 collision not converted ({e.Message}); {t}'s collision used");
            }
        }
        bool TryUnwrapped(out byte[] r)
        {
            r = [];
            try { r = ConvertCollision(data, ds1Col!.Bytes, T(t + "_h.hkx").Bytes, new Dictionary<string, string> { [o.Ds1Obj] = n }, ds1Fixup!, ds2Conventions: o.Variant != TestVariant.LegacyHavok, unwrapOffsetSpheres: true); return true; }
            catch (InvalidDataException) { return false; }
        }
        // rule OB1: the nodes the bound bodies need (mesh-less, at the body's transform)
        if (staticBinds.Values.Where(v => v.Node != null).ToList() is { Count: > 0 } extra)
        {
            foreach (var (bn, tr) in extra.DistinctBy(v => v.Name)) flver.Nodes.Add(BodyNode(bn, tr!, flver));
            files.First(f => f.Name == n + ".flv").Bytes = flver.Write();
        }
        if (staticBinds.Count > 0 && staticBinds.Any(kv => kv.Key.Split('|')[0] != kv.Value.Name))
            pack.Note($"object {o.Ds1Obj} → {n}: rule OB1: {staticBinds.Count} intact bod{(staticBinds.Count == 1 ? "y" : "ies")} bound to model nodes: "
                + string.Join(", ", staticBinds.Select(kv => $"{kv.Key.Split('|')[0]}→{kv.Value.Name}{(kv.Value.Node != null ? " (+node)" : "")}")));
        // (Scenery: no collision file when DS1 has none; its template has none either)
        if (!chr && !o.NoCollision && (col != null || o.Kind != Kind.Scenery)) Add(n + "_h.hkx", col ?? Replace(T(t + "_h.hkx").Bytes, t, n));
        if (skeleton != null) Add("skeleton.hkx", skeleton);
        var texReqs = conv.Textures.Values.Concat((fragConv?.Textures.Values.AsEnumerable() ?? [])).GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()).OrderBy(r => r.Name).ToList();
        foreach (var req in texReqs)
            Add(req.Name + ".tpf", MapPieceConverter.MakeTpf(req, TexBytes, hqGi: false, 1.0).Write());

        // ---- ACB: the template's (names swapped), its textures replaced by ours, models/collisions it lists for files we lack dropped
        var acb = ACB.Read(Replace(T(t + ".acb").Bytes, t, n));
        // texture assets sit in the object's tex\ folder (some templates, e.g. o10_1001, list none: they use map textures)
        var tTexAsset = acb.Assets.OfType<ACB.Asset.Texture>().FirstOrDefault() ?? new ACB.Asset.Texture
        {
            AbsolutePath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(acb.Assets.OfType<ACB.Asset.Model>().First().AbsolutePath)!)!, "tex", "x.tga"),
        };
        acb.Assets.RemoveAll(a => a is ACB.Asset.Texture);
        acb.Assets.RemoveAll(a => a is ACB.Asset.Model m && !Path.GetFileNameWithoutExtension(m.AbsolutePath).Equals(n, StringComparison.OrdinalIgnoreCase)
            && !(fragConv != null && Path.GetFileNameWithoutExtension(m.AbsolutePath).Equals(n + "_1", StringComparison.OrdinalIgnoreCase)));
        acb.Assets.RemoveAll(a => a is ACB.Asset.General g && g.AbsolutePath.Contains("motion_hkt", StringComparison.OrdinalIgnoreCase) && !o.Anims.Any(x => g.AbsolutePath.Contains(x.Ds2Anim, StringComparison.OrdinalIgnoreCase)));
        // collision/skeleton entries for files this archive doesn't have (o16_1230's second collision o16_1230_1_h,
        // a skeleton for a static object): a listed file that isn't there kept the hall floor from showing (session 7)
        acb.Assets.RemoveAll(a => a is ACB.Asset.General g && g.AbsolutePath.Contains("model_hkt", StringComparison.OrdinalIgnoreCase)
            && !files.Any(f => Path.GetFileNameWithoutExtension(f.Name).Equals(Path.GetFileNameWithoutExtension(g.AbsolutePath), StringComparison.OrdinalIgnoreCase)));
        string texDir = Path.GetDirectoryName(tTexAsset.AbsolutePath)!;
        foreach (var req in texReqs)
            acb.Assets.Add(new ACB.Asset.Texture { AbsolutePath = $@"{texDir}\{req.Name}.tga", RelativePath = $"tex/{req.Name}.tga" });
        Add(n + ".acb", acb.Write());

        var bnd = tBnd;
        bnd.Files = files;
        pack.AddFile("gamedata", $"model/obj/{n}.bnd", bnd.Write());
        if (o.Kind == Kind.Prefab)
        {
            // the object's state machine; its strings name the model (sib paths), same length, replaced in place
            pack.AddFile("gamedata", $"prefabeditor/{n}.pfbbin", Replace(File.ReadAllBytes(Path.Combine(data, "prefabeditor", t + ".pfbbin")), t, n));
            // an object TAE (sounds, damage while a boulder rolls) under the new name, when the template has one
            string tae = Path.Combine(data, "timeact", "obj", t + ".tae");
            if (File.Exists(tae)) pack.AddFile("gamedata", $"timeact/obj/{n}.tae", Replace(File.ReadAllBytes(tae), t, n));
        }

        // ---- params: the template's MapObjectParam / MapObjectGDParam rows under the new model id
        int tId = ModelId(t), id = ModelId(n);
        foreach (string p in new[] { "MapObjectParam", "MapObjectGDParam", "MapObjectDefaultInstanceParam" })
        {
            var param = pack.Param(p);
            if (param[tId] is { } row && param[id] == null) pack.AddRow(p, row, id, $"[Lordran] DS1 {o.Ds1Obj} as {n}");
        }
        // unbreakable decoration on a breakable template (o16_1230's GD row says isBreakable 1)
        if (o.Kind == Kind.Decor && pack.Param("MapObjectGDParam")[id] is { } gd) gd["isBreakable"].Value = (byte)0;
        // session 9 (crow track): GDParam.isPrefab says "load prefabeditor/MODEL.pfbbin" (exe FUN_1403cf640, list built
        // from GD rows with the prefab bit, FUN_1403cf370 path); a demo actor ships no state machine (o00_6090's states
        // show/hide/play its own clips), so the copied bit only made DS2 look for a missing file on every map load
        // (s8-8 log: obj PrefabEditor/o18_3510.pfbbin not found). Vanilla demo-only skinned objects without a prefab:
        // o16_8501 (no GD row = row 0), o21_9600 (isPrefab 0). Test variant DemoPrefab ships the template's state machine.
        if (o.Kind == Kind.DemoActor && pack.Param("MapObjectGDParam")[id] is { } gdDemo)
        {
            if (o.Variant == TestVariant.DemoPrefab)
            {
                pack.AddFile("gamedata", $"prefabeditor/{n}.pfbbin", Replace(File.ReadAllBytes(Path.Combine(data, "prefabeditor", t + ".pfbbin")), t, n));
                pack.Note($"object {o.Ds1Obj} → {n}: TEST variant {o.Variant}: {t}'s prefab state machine shipped, isPrefab kept");
            }
            else gdDemo["isPrefab"].Value = (byte)0;
        }
        // DS1's break behaviour as a DS2 instance type (see Options.InstanceType)
        if (o.InstanceType != 0 && pack.Param("MapObjectDefaultInstanceParam")[id] is { } di)
        {
            if (pack.Param("MapObjectInstanceTypeParam")[o.InstanceType] == null) throw new InvalidDataException($"MapObjectInstanceTypeParam has no row {o.InstanceType}");
            di["objType"].Value = System.Convert.ChangeType(o.InstanceType, di["objType"].Value.GetType());
            pack.Note($"object {o.Ds1Obj} → {n}: MapObjectDefaultInstanceParam {id} objType {o.InstanceType} (DS1 break behaviour)");
        }
        // session 11 (snd11), rule SND7: the DS1 object's own animation sounds on its DS2 TAE
        if (anibnd != null && !chr && o.Anims.Length > 0 && o.Variant == TestVariant.None) ObjectSounds.Apply(pack, anibnd, o.Ds1Obj, n, o.Anims);
        // session 11 (fx11), rules OBJ-SND1 / OBJ-SFX1: a DS1 breakable's own break sound and break effect
        if (o.Kind == Kind.Breakable && o.Variant == TestVariant.None) Breakables.Apply(pack, dsrRoot, o.Ds1Obj, n);
        pack.Note($"object {o.Ds1Obj} → {n} (template {t}): {flver.Meshes.Count} meshes, {flver.Nodes.Count} nodes, {conv.Textures.Count} textures, animations " + string.Join(", ", o.Anims.Select(a => $"{a.Ds1Anim}→{a.Ds2Anim}")));
        Console.WriteLine($"  object {o.Ds1Obj} → {n}");
    }

    /// <summary>
    /// A vanilla DS2 object under a new name (archive entries, ACB and prefab strings replaced; params copied), with the
    /// prefab's key guide (KeyGuideParam id, a u32 in the pfbbin) swapped: session 7's invisible "Open" helper for the
    /// Asylum's hall door is Majula's obelisk key-guide object o04_9300 with KeyGuideParam 310 "Both doors open".
    /// </summary>
    public static void CloneDs2(Pack pack, string from, string to, uint? keyGuideFrom = null, uint? keyGuideTo = null)
    {
        string data = pack.Ds2Data;
        if (from.Length != to.Length) throw new InvalidOperationException("names must have the same length");
        var bnd = BND4.Read(Path.Combine(data, "model", "obj", from + ".bnd"));
        foreach (var f in bnd.Files)
        {
            f.Name = f.Name.Replace(from, to, StringComparison.OrdinalIgnoreCase);
            if (f.Name.EndsWith(".acb", StringComparison.OrdinalIgnoreCase)) f.Bytes = Replace(f.Bytes, from, to);
        }
        pack.AddFile("gamedata", $"model/obj/{to}.bnd", bnd.Write());
        string pfb = Path.Combine(data, "prefabeditor", from + ".pfbbin");
        if (File.Exists(pfb))
        {
            var b = Replace(File.ReadAllBytes(pfb), from, to);
            if (keyGuideFrom is uint kf && keyGuideTo is uint kt)
            {
                int hits = 0;
                for (int i = 0; i + 4 <= b.Length; i += 4)
                    if (BitConverter.ToUInt32(b, i) == kf) { BitConverter.GetBytes(kt).CopyTo(b, i); hits++; }
                if (hits != 1) throw new InvalidDataException($"{from}.pfbbin: key guide {kf} found {hits} times");
            }
            pack.AddFile("gamedata", $"prefabeditor/{to}.pfbbin", b);
        }
        int tId = ModelId(from), id = ModelId(to);
        foreach (string p in new[] { "MapObjectParam", "MapObjectGDParam", "MapObjectDefaultInstanceParam" })
        {
            var param = pack.Param(p);
            if (param[tId] is { } row && param[id] == null) pack.AddRow(p, row, id, $"[Lordran] {from} as {to}");
        }
        pack.Note($"object {from} → {to} (DS2 clone{(keyGuideTo != null ? $", key guide {keyGuideFrom} → {keyGuideTo}" : "")})");
    }

    /// <summary>DS1 object collision → DS2 packfile with the template's type table; when the DS1 file uses a class the
    /// template lacks (the boulder's hkpConvexTranslateShape), another DS2 object collision that has it is the template.</summary>
    static byte[] ConvertCollision(string data, byte[] ds1, byte[] template, IReadOnlyDictionary<string, string>? rename, Action<HkPObj> kindFixup, bool ds2Conventions = true, bool unwrapOffsetSpheres = false)
    {
        // every object collision also gets DS2's serialisation conventions (children are fixed up before their parents);
        // ds2Conventions false only for the LegacyHavok test variant
        Action<HkPObj> fixup = ds2Conventions ? p => { kindFixup(p); Ds2PhysicsConventions(p); } : kindFixup;
        // a template must have every class the DS1 file uses: in its __types__ (InvalidDataException) and among the
        // class names it writes (InvalidOperationException "class X not in template", e.g. hkpConvexVerticesConnectivity
        // for DS1's convex-hull pots); otherwise the first DS2 object collision that has all of them
        static string? Missing(Exception e) => e switch
        {
            InvalidDataException d when d.Message.Contains("__types__") => d.Message.Split(' ')[1],
            InvalidOperationException o when o.Message.Contains("not in template") => o.Message.Split(' ')[1],
            _ => null,
        };
        try { return HkTagToPack.Convert(ds1, new HkPackfile(template), rename, fixup, unwrapOffsetSpheres: unwrapOffsetSpheres); }
        catch (Exception e) when (Missing(e) is string first)
        {
            // every class found missing so far must be in the candidate collision file itself
            var need = new HashSet<string> { first };
            for (bool grew = true; grew;)
            {
                grew = false;
                foreach (var f in Directory.GetFiles(Path.Combine(data, "model", "obj"), "o*.bnd"))
                {
                    var bytes = File.ReadAllBytes(f);
                    if (need.Any(c => bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(c + "\0")) < 0)) continue;
                    foreach (var h in BND4.Read(bytes).Files.Where(x => x.Name.EndsWith("_h.hkx", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (need.Any(c => h.Bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(c + "\0")) < 0)) continue;
                        try { return HkTagToPack.Convert(ds1, new HkPackfile(h.Bytes), rename, fixup, unwrapOffsetSpheres: unwrapOffsetSpheres); }
                        catch (Exception e2) when (Missing(e2) is string more) { if (need.Add(more)) { grew = true; break; } }
                    }
                    if (grew) break;
                }
            }
            throw new InvalidDataException($"no DS2 collision template has the classes of this DS1 file ({string.Join(", ", need)})", e);
        }
    }

    /// <summary>
    /// DS1 animates an object's collision with KEYFRAMED rigid bodies (hkpMotion type 4, collidable quality 1); DS2's
    /// bone-bound bodies are FIXED (type 5, quality 0) named after the bone (o00_0308 "door", o10_1001 "model", o00_0300
    /// "door"; type 3 = dynamic break fragments): converted to DS2's convention (session 8). Not what kept the Asylum
    /// gate shut, though (run s8-1: still blocked with type 5; DS2 moves either kind, the hall door opened as type 4).
    /// </summary>
    /// <summary>
    /// The collision strings renamed while converting. A static object's body takes the DS2 model name (as DS2's own:
    /// o16_1230's body "o16_1230"). Session 11 (burg11, rule WE4d): an ANIMATED object's body binds by node name (DS2
    /// FUN_140bcc840; world-events' body check) and the FLVER keeps DS1's node names, so a body named like the DS1 model
    /// keeps that name (the root node "o1200") and DS1's "&lt;model&gt;_h" body (o1202) takes the root node's name; the Asylum's
    /// animated bodies carry other names (o1330_00, door …) and are unchanged.
    /// </summary>
    static Dictionary<string, string> BodyRename(string ds1, string ds2, bool animated) =>
        animated ? new() { [ds1 + "_h"] = ds1 } : new() { [ds1] = ds2 };

    static bool Identity(float[] t)
    {
        float[] id = [1, 0, 0, 0, 1, 0, 0, 0, 1];
        for (int c = 0; c < 3; c++) for (int r = 0; r < 3; r++) if (Math.Abs(t[c * 4 + r] - id[c * 3 + r]) > 1e-4f) return false;
        return Math.Abs(t[12]) < 1e-4f && Math.Abs(t[13]) < 1e-4f && Math.Abs(t[14]) < 1e-4f;
    }

    /// <summary>Rule OB1: binds one intact body of a baked object to a node (see Port). State keyed by the DS1 name + transform, so a
    /// conversion retried on another template names the bodies the same way.</summary>
    static void BindStaticBody(HkPObj body, string n, Dictionary<string, (string Name, float[]? Node)> binds)
    {
        string orig = body["name"] as string ?? "";
        var tr = (float[])body.Get("motion").Get("motionState")["transform"]!;
        string key = orig + "|" + string.Join(",", tr.Take(15).Select(v => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)));
        if (!binds.TryGetValue(key, out var b))
        {
            bool id = Identity(tr);
            b = id && !binds.Values.Any(v => v.Name == n) ? (n, null) : ($"{n}_b{binds.Count(v => v.Value.Node != null):D2}", (float[])tr.Clone());
            binds[key] = b;
        }
        body["name"] = b.Name;
    }

    /// <summary>A mesh-less FLVER root node at a Havok body transform (column-major rotation: floats 0-2, 4-6, 8-10; translation
    /// 12-14). SoulsFormats' local transform is Scale·RotX·RotZ·RotY·T (row vectors), so M = Rᵀ: M12 = sin z, M11 = cos z cos y,
    /// M13 = −cos z sin y, M22 = cos x cos z, M32 = −sin x cos z.</summary>
    static FLVER.Node BodyNode(string name, float[] t, FLVER2 flver)
    {
        float m11 = t[0], m12 = t[1], m13 = t[2], m22 = t[5], m32 = t[9];
        float z = MathF.Asin(Math.Clamp(m12, -1f, 1f)), y = MathF.Atan2(-m13, m11), x = MathF.Atan2(-m32, m22);
        var node = new FLVER.Node { Name = name, Translation = new System.Numerics.Vector3(t[12], t[13], t[14]), Rotation = new System.Numerics.Vector3(x, y, z) };
        // root siblings: link after the last root node
        int last = flver.Nodes.FindLastIndex(nd => nd.ParentIndex == -1);
        if (last >= 0) { flver.Nodes[last].NextSiblingIndex = (short)flver.Nodes.Count; node.PreviousSiblingIndex = (short)last; }
        return node;
    }

    static void BoneBodyFixup(HkPObj o)
    {
        if (o.Class == "hkpMaxSizeMotion") Swap(o, "type", 4, 5);
        if (o.Class == "hkpTypedBroadPhaseHandle") Swap(o, "objectQualityType", 1, 0);
    }

    /// <summary>A static/breakable object's intact body: always FIXED (DS2 o00_2100's intact jar, o16_1230).</summary>
    static void StaticFixup(HkPObj o)
    {
        if (o.Class == "hkpMaxSizeMotion") foreach (long t in new long[] { 1, 2, 3, 4 }) Swap(o, "type", t, 5);
        if (o.Class == "hkpTypedBroadPhaseHandle") Swap(o, "objectQualityType", 1, 0);
    }

    /// <summary>Break fragments: DS1 dynamic (1) → DS2's box-inertia dynamic (3), as o00_2100_1_h's 27 bodies. Session 8
    /// (track potroll): DS1's fragments are DEBRIS quality (2, collidable broad-phase handle) with savedQualityTypeIndex 0;
    /// every dynamic body in DS2's fragment files is MOVING (4) with savedQualityTypeIndex 0xFFFF (obj-col-survey: 4216
    /// motion-3 and 193 motion-2 fragment bodies, all q=4; o00_2100_1_h). A fixed body in a fragment file (DS1's base
    /// plate o8410 → o18_8410, quality 0) is left alone (DS2 has those too: o10_2260_1_h "o10_2260", o11_2000_1_h).</summary>
    static void FragmentFixup(HkPObj o)
    {
        if (o.Class == "hkpMaxSizeMotion")
        {
            Swap(o, "type", 1, 3);
            if (o["type"] is { } t and not string && System.Convert.ToInt64(t) is 1 or 2 or 3) Swap(o, "savedQualityTypeIndex", 0, 0xFFFF);
        }
        if (o.Class == "hkpTypedBroadPhaseHandle") Swap(o, "objectQualityType", 2, 4);
    }

    /// <summary>The fragment fixup before session 8's quality rule (only DS1 dynamic 1 → DS2 3): the LegacyHavok test variant.</summary>
    static void LegacyFragmentFixup(HkPObj o)
    {
        if (o.Class == "hkpMaxSizeMotion") Swap(o, "type", 1, 3);
    }

    /// <summary>Convex radius DS2's tools give primitive shapes (hkConvexShapeDefaultRadius): 0.005 on every box and
    /// cylinder of a breakable's intact body in vanilla DS2 (obj-col-survey, session 8).</summary>
    const float Ds2ConvexRadius = 0.005f;

    /// <summary>
    /// Rule (session 8, track potroll): a DS1 object collision converted to DS2 follows DS2's Havok serialisation
    /// conventions where DSR's 2015 tagfiles differ. Found comparing our church urn o18_8410 (DS1 o8410) with DS2's
    /// own urn o18_2040 — same MapObjectInstanceTypeParam (1906, breakAction 4 "Avoidance" = rolling breaks it) and
    /// the same shape class (an intact FIXED hkpCylinderShape) — and with the jar o00_2100; every other field matched:
    ///  - hkpEntity.npData: the tagfile has no such member, so the converter left 0; Havok's hkpEntity constructor
    ///    writes 0xFFFFFFFF (exe FUN_1406f94c0, +0x2C0) and all 5000 bodies of DS2's breakable objects carry 0xFFFFFFFF;
    ///  - hkcdShape.dispatchType: DSR writes 0, DS2 4 on every shape (USER: DS2's own shape constructors store it,
    ///    e.g. hkpCylinderShape FUN_140da3060 writes 0x401 at +0x10 = type 1, dispatch 4; the finish-loading
    ///    constructor FUN_140da3130 sets only the type byte, so the file's value is what the game keeps);
    ///  - convex radius: DS1 boxes and cylinders have radius 0, DS2's 0.005 (o18_2040 cylinder r 0.005 with
    ///    vertexA.w = cylRadius + radius; jar box r 0.005 with halfExtents = outer size − radius). Converted by
    ///    shrinking the core by the radius, so the outer surface stays at DS1's size; convex translate/transform
    ///    wrappers take their child's radius like DS2's (jar: translate r 0.005 over box r 0.005). Hulls
    ///    (hkpConvexVerticesShape) keep DS1's radius: DS2 has radius-0 hulls on breakables too (o00_2060, o00_2240),
    ///    and shrinking one needs its planes rebuilt. Shapes thinner than twice the radius keep radius 0 (DS2:
    ///    o10_1005, o27_1040 boxes with a zero extent).
    /// A file convention, not a proven fix: no DS2 code was found that reads npData or dispatchType (byte scans of the
    /// Havok range, session 8), and the roll stopped against the urn, so a contact did happen with radius 0 too. The roll-break chain itself takes the same path for our urn and DS2's jars (NOTES.md, track
    /// potroll). The in-game A/B decides: test variant <see cref="TestVariant.LegacyHavok"/> (o18_8491) is the urn with
    /// the pre-session-8 files, byte-identical to the installed pack's o18_8410 except for the name.
    /// </summary>
    static void Ds2PhysicsConventions(HkPObj o)
    {
        // (absent from DSR's tagfile: the member is null here and would be written as 0)
        if (o.Class == "hkpRigidBody" && (o["npData"] is null || o["npData"] is { } np and not string && System.Convert.ToInt64(np) == 0))
            o["npData"] = 0xFFFFFFFFL;
        if (o["dispatchType"] is { } dt and not string && System.Convert.ToInt64(dt) == 0)
            o["dispatchType"] = System.Convert.ChangeType(4, dt.GetType());
        if (o["radius"] is not float radius || radius != 0) return;
        const float r = Ds2ConvexRadius;
        switch (o.Class)
        {
            case "hkpBoxShape" when o["halfExtents"] is float[] he && MathF.Min(he[0], MathF.Min(he[1], he[2])) > 2 * r:
                for (int i = 0; i < 3; i++) he[i] -= r;
                he[3] = MathF.Min(he[0], MathF.Min(he[1], he[2]));   // Havok keeps the smallest half extent in w (DS1 and DS2 alike)
                o["radius"] = r;
                break;
            case "hkpCylinderShape" when o["cylRadius"] is float cr && cr > 2 * r && o["vertexA"] is float[] a && o["vertexB"] is float[] b:
            {
                var axis = new System.Numerics.Vector3(a[0] - b[0], a[1] - b[1], a[2] - b[2]);
                if (axis.Length() <= 2 * r) break;
                axis = System.Numerics.Vector3.Normalize(axis) * r;
                a[0] -= axis.X; a[1] -= axis.Y; a[2] -= axis.Z;
                b[0] += axis.X; b[1] += axis.Y; b[2] += axis.Z;
                o["cylRadius"] = cr - r;
                a[3] = b[3] = cr;   // w = cylRadius + radius (o18_2040: 0.5465 + 0.005), i.e. DS1's outer radius
                o["radius"] = r;
                break;
            }
            case "hkpConvexTranslateShape" or "hkpConvexTransformShape":
                if (o["childShape"] is HkPObj { } c && c["childShape"] is HkPObj child && child["radius"] is float cr2)
                {
                    o["radius"] = cr2;
                    // a parent fixup may have resized the box (ClearanceFixup): keep w = the smallest half extent
                    if (child.Class == "hkpBoxShape" && child["halfExtents"] is float[] he2) he2[3] = MathF.Min(he2[0], MathF.Min(he2[1], he2[2]));
                }
                break;
        }
    }

    /// <summary>Opening height a rising object must leave for DS2's player (DS1's 1.61 m gate blocked it; DS2's bars ~2.4 m).</summary>
    const float ClearOpen = 2.0f;

    /// <summary>Highest upward translation any bone reaches in a DS1 animation (frames sampled).</summary>
    static float MaxRise(byte[] tagfile)
    {
        var a = new HkSplineAnim(new HkTagfile(tagfile));
        float best = 0;
        for (int t = 0; t < a.TrackCount; t++)
            for (int f = 0; f < a.NumFrames; f += Math.Max(1, a.NumFrames / 16))
                best = MathF.Max(best, a.Sample(t, f).T.Y);
        return MathF.Max(best, Enumerable.Range(0, a.TrackCount).Max(t => a.Sample(t, a.NumFrames - 1).T.Y));
    }

    /// <summary>A box (in a translate shape) that reaches the floor: its bottom raised until the opened object clears
    /// <see cref="ClearOpen"/> (bottom + rise).</summary>
    static void ClearanceFixup(HkPObj o, float rise)
    {
        if (o.Class != "hkpConvexTranslateShape" || o["translation"] is not float[] tr) return;
        if ((o["childShape"] as HkPObj)?["childShape"] is not HkPObj { Class: "hkpBoxShape" } box || box["halfExtents"] is not float[] he) return;
        // outer surface = core half extents + convex radius (session 8: Ds2PhysicsConventions gives DS1's boxes DS2's 0.005)
        float r = box["radius"] is float br ? br : 0;
        float bottom = tr[1] - he[1] - r, top = tr[1] + he[1] + r;
        if (bottom > 0.3f || bottom + rise >= ClearOpen) return;
        float newBottom = ClearOpen - rise;
        if (newBottom >= top - 0.1f) return;
        tr[1] = (newBottom + top) / 2; he[1] = (top - newBottom) / 2 - r;
    }

    /// <summary>
    /// Rule R3c (session 9, crow track; generalized in the fix pass): a DS1 character used as a DS2 demo object gets a FLVER
    /// node tree equal to its skeleton.hkx tree. Why: DS2's demo binder maps every FLVER node to a demo track by name
    /// (FUN_140bc7be0 / FUN_140bc70d0: a node named "o??_????" gets the actor root track, any other node
    /// "&lt;actor&gt;_&lt;node&gt;", an unmatched node its nearest matched ancestor's) and the pose copy (FUN_140308220) writes that
    /// track's LOCAL transform (hkaPose+8 = the synced local pose, FUN_1407736c0) onto the node, so a node's FLVER parent
    /// must be the bone its track is local to. DS1 never needed that (it skins by name in model space), and c3510's FLVER
    /// differs from its skeleton twice: (1) the skeleton root "Master" (renamed to the model, as the skeleton's root) is a
    /// Disabled root-level node and Pelvis a second root, so no node took the actor root track = the crow's whole flight
    /// (s8-8: crow drawn around the cut offset); (2) Disabled root-level helpers the skeleton hangs under real bones
    /// (LArmCollarbone/RArmCollarbone under Spine2, 29 wing_* under Head/Neck/Spine1/Spine2), and the skinned wing roots
    /// LArm11/RArm11 under Spine2 in the FLVER but under the collarbones in the skeleton and the demo, so each wing lost the
    /// collarbone transform (review: 90 of 194 skinned nodes 0.7-8.6 m / up to 90 deg off with (1) alone).
    /// The rule (the chr track's Ds1RigModel node rule, with the skeleton.hkx as the rig): DS1's mesh node(s) ahead of the
    /// first bone stay first, then the skeleton bones in skeleton order (the root = DS1's Master node, named like the
    /// model), then DS1's other nodes; a bone node's parent is its skeleton parent's node; a connected DS1 node keeps its
    /// FLVER bind world (DS1 skins with it: skin 1:1), a Disabled one keeps its stored transform as its local under its
    /// skeleton parent (DS1 stores the hkx reference local there: c3510 LArmCollarbone (0.588, 0.046, -0.597) = hkx bone
    /// 34) and loses the Disabled flag (vanilla demo objects: flags 0); meshes and dummies are re-indexed.
    /// </summary>
    static string AlignNodesToSkeleton(FLVER2 flver, string model, byte[] skeleton)
    {
        var pf = new HkPackfile(skeleton);
        var sk = pf.ReadObject(pf.Objects.First(x => x.Class == "hkaSkeleton").Offset);
        var bones = ((List<object?>)sk["bones"]!).Select(b => (string)((HkPObj)b!)["name"]!).ToList();
        var parents = ((List<object?>)sk["parentIndices"]!).Select(x => System.Convert.ToInt32(x)).ToList();
        var refPose = ((List<object?>)sk["referencePose"]!).Select(x => (float[])x!).ToList();
        if (bones.Count == 0 || bones[0] != model) return $"skeleton root is '{bones.FirstOrDefault()}', not {model}: FLVER nodes left as DS1's";
        var old = flver.Nodes;
        int count = old.Count;
        var oldWorld = Commands.ChrCompare.World(flver);
        // the FLVER node of each skeleton bone, by name; the root is DS1's "Master" node (the skeleton's Master took the model's name)
        var nodeOf = bones.Select(b => old.FindIndex(x => x.Name == b)).ToArray();
        if (nodeOf[0] < 0) nodeOf[0] = old.FindIndex(x => x.Name == "Master");
        if (nodeOf[0] < 0) return "the FLVER has no node for the skeleton root (Master): FLVER nodes left as DS1's";
        var boneOf = Enumerable.Repeat(-1, count).ToArray();
        for (int b = 0; b < bones.Count; b++) if (nodeOf[b] >= 0) boneOf[nodeOf[b]] = b;
        int firstBone = nodeOf.Where(i => i >= 0).Min();
        var order = Enumerable.Range(0, firstBone).Where(i => boneOf[i] < 0)
            .Concat(nodeOf.Where(i => i >= 0))
            .Concat(Enumerable.Range(firstBone, count - firstBone).Where(i => boneOf[i] < 0)).ToList();
        var newIndex = new short[count];
        for (int k = 0; k < count; k++) newIndex[order[k]] = (short)k;
        static bool Close(System.Numerics.Matrix4x4 a, System.Numerics.Matrix4x4 b) =>
            System.Numerics.Vector3.Distance(a.Translation, b.Translation) < 1e-4f
            && MathF.Abs(a.M11 - b.M11) + MathF.Abs(a.M12 - b.M12) + MathF.Abs(a.M13 - b.M13) + MathF.Abs(a.M21 - b.M21) + MathF.Abs(a.M22 - b.M22)
             + MathF.Abs(a.M23 - b.M23) + MathF.Abs(a.M31 - b.M31) + MathF.Abs(a.M32 - b.M32) + MathF.Abs(a.M33 - b.M33) < 1e-4f;
        var nodes = new List<FLVER.Node>(count);
        var world = new System.Numerics.Matrix4x4[count]; // bind world per new index
        var moved = new List<string>();
        int enabled = 0, recomputed = 0;
        double drift = 0;
        for (int k = 0; k < count; k++)
        {
            int i = order[k];
            var src = old[i];
            var node = new FLVER.Node(src);
            int b = boneOf[i], p;
            if (b >= 0)
            {
                int pb = parents[b];
                while (pb >= 0 && nodeOf[pb] < 0) pb = parents[pb]; // a skeleton bone without a node (vanilla o21_9600: nubs): the nearest one with a node
                p = pb >= 0 ? newIndex[nodeOf[pb]] : -1;
            }
            else p = src.ParentIndex >= 0 ? newIndex[src.ParentIndex] : -1;
            if (p >= k) throw new InvalidDataException($"{model}: node '{src.Name}' would come before its parent");
            var parentWorld = p >= 0 ? world[p] : System.Numerics.Matrix4x4.Identity;
            bool disabled = src.Flags.HasFlag(FLVER.Node.NodeFlags.Disabled);
            string oldParent = src.ParentIndex >= 0 ? old[src.ParentIndex].Name : "-";
            string newParent = p >= 0 ? old[order[p]].Name : "-";
            if (oldParent != newParent) moved.Add($"{src.Name} ({oldParent} -> {newParent})");
            // a connected node keeps DS1's bind world: its stored TRS stays when that already gives it, else it is
            // re-expressed under the new parent; a Disabled bone keeps its stored TRS as the local under its skeleton parent
            if (!disabled && !Close(node.ComputeLocalTransform() * parentWorld, oldWorld[i]))
            {
                System.Numerics.Matrix4x4.Invert(parentWorld, out var inv);
                var local = oldWorld[i] * inv;
                System.Numerics.Matrix4x4.Decompose(local, out var scale, out _, out var tr);
                node.Translation = tr;
                node.Rotation = Build.Chr.Ds1RigModel.EulerXZY(local);
                node.Scale = System.Numerics.Vector3.Distance(scale, System.Numerics.Vector3.One) < 1e-3f ? System.Numerics.Vector3.One : scale;
                recomputed++;
            }
            if (b >= 0 && disabled) { node.Flags &= ~FLVER.Node.NodeFlags.Disabled; enabled++; }
            if (b == 0) node.Name = model;
            node.ParentIndex = (short)p;
            world[k] = node.ComputeLocalTransform() * parentWorld;
            if (!disabled) drift = Math.Max(drift, System.Numerics.Vector3.Distance(world[k].Translation, oldWorld[i].Translation));
            nodes.Add(node);
        }
        // first-child / sibling links from the parents (siblings in index order)
        var last = new Dictionary<int, int>();
        foreach (var x in nodes) { x.FirstChildIndex = -1; x.NextSiblingIndex = -1; x.PreviousSiblingIndex = -1; }
        for (int k = 0; k < count; k++)
        {
            int p = nodes[k].ParentIndex;
            if (last.TryGetValue(p, out int prev)) { nodes[prev].NextSiblingIndex = (short)k; nodes[k].PreviousSiblingIndex = (short)prev; }
            else if (p >= 0) nodes[p].FirstChildIndex = (short)k;
            last[p] = k;
        }
        short Re(int i) => i >= 0 && i < count ? newIndex[i] : (short)i;
        foreach (var m in flver.Meshes)
        {
            m.NodeIndex = Re(m.NodeIndex);
            for (int j = 0; j < m.BoneIndices.Count; j++) m.BoneIndices[j] = Re(m.BoneIndices[j]);
        }
        foreach (var d in flver.Dummies) { d.ParentBoneIndex = Re(d.ParentBoneIndex); d.AttachBoneIndex = Re(d.AttachBoneIndex); }
        flver.Nodes = nodes;
        // check: connected skinned bones, FLVER bind (DS1's, kept) vs the hkx reference pose (what DS1's clips are relative to)
        var refWorld = new (System.Numerics.Vector3 T, System.Numerics.Quaternion R)[bones.Count];
        for (int b = 0; b < bones.Count; b++)
        {
            var f = refPose[b];
            var (t, r) = (new System.Numerics.Vector3(f[0], f[1], f[2]), System.Numerics.Quaternion.Normalize(new System.Numerics.Quaternion(f[4], f[5], f[6], f[7])));
            refWorld[b] = parents[b] >= 0 ? (refWorld[parents[b]].T + System.Numerics.Vector3.Transform(t, refWorld[parents[b]].R), refWorld[parents[b]].R * r) : (t, r);
        }
        var skinned = flver.Meshes.SelectMany(m => m.BoneIndices).ToHashSet();
        double bindVsRef = 0; string worst = "-";
        for (int b = 0; b < bones.Count; b++)
        {
            if (nodeOf[b] < 0 || !skinned.Contains(newIndex[nodeOf[b]])) continue;
            double dd = System.Numerics.Vector3.Distance(world[newIndex[nodeOf[b]]].Translation, refWorld[b].T);
            if (dd > bindVsRef) { bindVsRef = dd; worst = bones[b]; }
        }
        return $"FLVER node tree = skeleton tree (rule R3c): {nodeOf.Count(i => i >= 0)} of {bones.Count} skeleton bones in skeleton order from node {newIndex[nodeOf[0]]} "
            + $"(root DS1 'Master' -> '{model}'), {count - nodeOf.Count(i => i >= 0)} other DS1 nodes kept; {moved.Count} nodes under a new parent "
            + $"[{string.Join(", ", moved.Take(6))}{(moved.Count > 6 ? ", ..." : "")}]; {enabled} Disabled DS1 bones enabled with their stored local under the "
            + $"skeleton parent; {recomputed} connected nodes re-expressed, DS1 bind world kept (max drift {drift * 1000:0.000} mm); skinned bones' FLVER bind vs hkx "
            + $"reference pose max {bindVsRef * 1000:0.0} mm ({worst})";
    }

    static void Swap(HkPObj o, string field, long from, long to)
    {
        if (o[field] is { } v and not string && System.Convert.ToInt64(v) == from) o[field] = System.Convert.ChangeType(to, v.GetType());
    }

    /// <summary>Every name in a DS1 tagfile that starts with the DS1 model name (fragment bodies o8410_00, ...) mapped
    /// to the DS2 model prefix (o18_8410_00).</summary>
    static Dictionary<string, string> PrefixRename(byte[] tagfile, string ds1, string ds2)
    {
        var map = new Dictionary<string, string>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(Encoding.ASCII.GetString(tagfile), ds1 + "[_0-9A-Za-z]*"))
            map.TryAdd(m.Value, ds2 + m.Value[ds1.Length..]);
        return map;
    }

    static byte[] Replace(byte[] bytes, string from, string to)
    {
        var result = (byte[])bytes.Clone();
        foreach (var enc in new Encoding[] { Encoding.ASCII, Encoding.Unicode })
        {
            byte[] a = enc.GetBytes(from), b = enc.GetBytes(to);
            for (int i = 0; i + a.Length <= result.Length; i++)
                if (result.AsSpan(i, a.Length).SequenceEqual(a)) { b.CopyTo(result, i); i += a.Length - 1; }
        }
        return result;
    }
}
