namespace Lordran.Tools.Formats.Havok;

/// <summary>
/// Generic DSR Havok 2015 tagfile → DS2 Havok 2014 packfile (session 6, first for DS1 object skeletons and animations:
/// hkaAnimationContainer / hkaSkeleton / hkaSplineCompressedAnimation / hkaAnimationBinding exist on both sides). The
/// tagfile's object graph is rebuilt as <see cref="HkPObj"/>s typed by a DS2 packfile template's class table (same class
/// names, members matched by name; members the template's class lacks are dropped, the template's members the tagfile
/// lacks stay zero), then written with <see cref="HkPackWriter"/>. Pointed-to objects become top-level objects, shared.
/// </summary>
public static class HkTagToPack
{
    /// <param name="rename">strings replaced while converting (e.g. a DS1 object's rigid-body name → the DS2 model name:
    /// DS2 binds an object's collision bodies by name to its model or bones)</param>
    /// <param name="fixup">called on every converted object (top-level and inline structs) before writing</param>
    /// <param name="substitute">session 8, track boss: a tagfile class the template lacks → a template class to convert it
    /// as (members matched by name; the fixup completes the rest), e.g. DS1's hkpHingeConstraintData → DS2's
    /// hkpLimitedHingeConstraintData (no DS2 character ragdoll has the plain hinge)</param>
    /// <param name="unwrapOffsetSpheres">session 11 (merge11): an hkpConvexTranslateShape over an hkpSphereShape becomes the
    /// sphere when the template lacks the wrapper (last resort of ObjPorter; the offset is dropped)</param>
    /// <summary>Session 11 (burgchr11): the tag object each converted object came from (a fixup may read members the template class
    /// does not have: a DS1 ball-and-socket's pivots).</summary>
    public static readonly System.Runtime.CompilerServices.ConditionalWeakTable<HkPObj, HkTagfile.HkObj> Sources = new();

    public static byte[] Convert(byte[] tagfile, HkPackfile template, IReadOnlyDictionary<string, string>? rename = null, Action<HkPObj>? fixup = null,
        IReadOnlyDictionary<string, string>? substitute = null, bool unwrapOffsetSpheres = false)
    {
        var tag = new HkTagfile(tagfile);
        var c = new Converter(template) { Rename = rename, Fixup = fixup, Substitute = substitute, UnwrapOffsetSpheres = unwrapOffsetSpheres };
        var root = c.Obj(tag.Root ?? throw new InvalidDataException("tagfile has no root"), topLevel: true);
        return new HkPackWriter(template).Write(root);
    }

    sealed class Converter(HkPackfile template)
    {
        public IReadOnlyDictionary<string, string>? Rename;
        public Action<HkPObj>? Fixup;
        public IReadOnlyDictionary<string, string>? Substitute;
        public bool UnwrapOffsetSpheres;

        /// <summary>Optional data DS2 never serializes: pointers to these classes are written null. DS1's convex hulls
        /// (church pillars o8600, sewer columns o8610) carry an hkpConvexVerticesConnectivity; no DS2 collision file has
        /// that class next to hkpConvexVerticesShape (session 8), and Havok rebuilds connectivity on demand.
        /// Session 11 (burg11): DS1 object animations with motion extraction (hkaAnimation.extractedMotion →
        /// hkaDefaultAnimatedReferenceFrame: the Burg's doors o1308, gates o1201/o1290, lever o1202) — no DS2 2014 class
        /// table to convert with has the class; the pointer is written null (no root motion: an object's own transform
        /// stays where the MSB puts it, its bones still animate; P, a door/gate test in B2). Only conversions that failed
        /// before take this path.</summary>
        static readonly HashSet<string> Dropped = ["hkpConvexVerticesConnectivity", "hkaDefaultAnimatedReferenceFrame"];

        /// <summary>Session 8 (track enemies): DS1 c2900's ragdoll joints are hkpBreakableConstraintData (DS1 skeletons fall
        /// apart); no DS2 chr or object file uses that class (none of the 2014 class tables to convert with has it), so a
        /// pointer to one becomes a pointer to the constraint it wraps (its constraintData: hkpRagdollConstraintData /
        /// hkpLimitedHingeConstraintData): the same joint, unbreakable.</summary>
        HkTagfile.HkObj Unwrap(HkTagfile.HkObj o)
        {
            while (o.Type.Name == "hkpBreakableConstraintData" && o.Fields.TryGetValue("constraintData", out var inner) && inner is HkTagfile.HkObj io) o = io;
            // session 11 (merge11, boulder WE4 defect 3): DS1's offset sphere (hkpConvexTranslateShape over hkpSphereShape,
            // the Asylum boulder o8530: r 0.6024, offset (−0.139, 0.049, 0.02)) — no DS2 collision file has both classes;
            // with a template that lacks the translate wrapper the body takes its child shape (the offset, 0.15 m here, is
            // dropped: ObjPorter notes it), so the ball keeps a collision instead of falling back to the template's
            while (UnwrapOffsetSpheres && o.Type.Name == "hkpConvexTranslateShape" && !template.Classes.ContainsKey("hkpConvexTranslateShape")
                && o.Fields.TryGetValue("childShape", out var cont) && cont is HkTagfile.HkObj co
                && co.Fields.TryGetValue("childShape", out var child) && child is HkTagfile.HkObj ch && ch.Type.Name == "hkpSphereShape") o = ch;
            return o;
        }
        readonly Dictionary<HkTagfile.HkObj, HkPObj> top = new(ReferenceEqualityComparer.Instance);

        /// <param name="asClass">the template member's class for inline structs (2015 names nested types "A::B", 2014 "AB")</param>
        public HkPObj Obj(HkTagfile.HkObj o, bool topLevel, string? asClass = null)
        {
            if (topLevel && top.TryGetValue(o, out var done)) return done;
            string name = asClass ?? o.Type.Name.Replace("::", "");
            if (!template.Classes.ContainsKey(name) && Substitute != null && Substitute.TryGetValue(name, out var sub)) name = sub;
            if (!template.Classes.TryGetValue(name, out var cls))
                throw new InvalidDataException($"class {name} is not in the template's __types__");
            var p = new HkPObj(name, topLevel);
            Sources.AddOrUpdate(p, o);
            if (topLevel) top[o] = p;
            foreach (var (m, _) in cls.AllMembers())
                if (o.Fields.TryGetValue(m.Name, out var v) && v != null)
                    p[m.Name] = Value(v, m.Type, m.SubType, m.ClassName, m.CArraySize);
            Fixup?.Invoke(p);
            return p;
        }

        object? Value(object v, HkType type, HkType sub, string? cls, int carray)
        {
            if (carray > 0)
            {
                // C-arrays of pointers (hkpConstraintInstance::m_entities[2], hkpRagdollMotorConstraintAtom::m_motors[3])
                // become pointers like single ones. Session 8: they went through Scalar(), the writer wrote every
                // entity/motor as null, and DS2 crashed at the 1:1 Hollow's spawn (hkaRagdollInstance::clone →
                // hkpConstraintInstance ctor dereferencing entityA = null, exe+0x6fa30e, run s8-6)
                object? Elem(object? x) => x == null ? null : type switch
                {
                    HkType.Struct => Obj((HkTagfile.HkObj)x, false, cls),
                    HkType.Pointer => x is HkTagfile.HkObj po && !Dropped.Contains(po.Type.Name) ? Obj(Unwrap(po), true) : null,
                    _ => Scalar(x, type),
                };
                return v is System.Collections.IEnumerable e and not string
                    ? e.Cast<object?>().Select(Elem).ToList()
                    : new List<object?> { Elem(v) };
            }
            switch (type)
            {
                case HkType.Struct: return Obj((HkTagfile.HkObj)v, false, cls);
                case HkType.Pointer: return v is HkTagfile.HkObj po && !Dropped.Contains(po.Type.Name) ? Obj(Unwrap(po), true) : null;
                case HkType.Array: case HkType.SimpleArray:
                {
                    var list = ((System.Collections.IEnumerable)v).Cast<object?>().ToList();
                    if (sub == HkType.UInt8 || sub == HkType.Int8) return list.Select(x => System.Convert.ToByte(System.Convert.ToInt64(x) & 0xFF)).ToArray();
                    return list.Select(x => x == null ? null : sub switch
                    {
                        HkType.Struct => (object?)Obj((HkTagfile.HkObj)x, false, cls),
                        HkType.Pointer => x is HkTagfile.HkObj po ? Obj(Unwrap(po), true) : null,
                        _ => Scalar(x, sub),
                    }).ToList();
                }
                default: return Scalar(v, type);
            }
        }

        /// <summary>A vector/matrix/transform's floats: 2015 tagfiles store some as structs (hkQsTransform = translation,
        /// rotation, scale), others as float lists; flattened in declaration order.</summary>
        static IEnumerable<float> Floats(object? v) => v switch
        {
            null => [],
            float f => [f],
            float[] fa => fa,
            HkTagfile.HkObj o => o.Fields.Values.SelectMany(Floats),
            System.Collections.IEnumerable e and not string => e.Cast<object?>().SelectMany(Floats),
            _ => [System.Convert.ToSingle(v)],
        };

        object? Scalar(object v, HkType type) => type switch
        {
            HkType.Vector4 or HkType.Quaternion or HkType.Matrix3 or HkType.Rotation or HkType.QsTransform or HkType.Matrix4 or HkType.Transform
                => Floats(v).ToArray(),
            HkType.CString or HkType.StringPtr => v is string str && Rename != null && Rename.TryGetValue(str, out var to) ? to : v as string,
            HkType.Bool => v is bool b ? b : System.Convert.ToInt64(v) != 0,
            HkType.Real => System.Convert.ToSingle(v),
            _ => v is bool bb ? (bb ? 1L : 0L) : v,
        };
    }
}
