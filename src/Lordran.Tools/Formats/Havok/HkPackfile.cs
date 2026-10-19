using System.Text;

namespace Lordran.Tools.Formats.Havok;

/// <summary>
/// Reader for Havok binary packfiles (DS2 SOTFS: file version 11, "hk_2014.1.0-r1", 64-bit pointers, little-endian).
/// DS2's files carry a __types__ section with full hkClass reflection, so objects are decoded generically from it.
/// Layout: 0x40-byte header + predicate array, then 0x40-byte section headers; each section's data is followed by
/// local fixups (src, dst), global fixups (src, section, dst) and virtual fixups (src, classname section, name offset).
/// </summary>
public sealed class HkPackfile
{
    public byte[] Bytes { get; }
    public int FileVersion { get; }
    public string ContentsVersion { get; }
    public byte[] LayoutRules { get; }
    public int ContentsSectionIndex { get; }
    public int ContentsSectionOffset { get; }
    public int ContentsClassNameSectionIndex { get; }
    public int ContentsClassNameSectionOffset { get; }
    public int Flags { get; }
    public short MaxPredicate { get; }
    public byte[] Predicates { get; }
    public List<Section> Sections { get; } = new();

    public sealed class Section
    {
        public string Name = "";
        public int Start, LocalFixups, GlobalFixups, VirtualFixups, Exports, Imports, End;
        public Dictionary<int, int> Local = new();                    // src → dst (same section)
        public Dictionary<int, (int Section, int Offset)> Global = new();
        public List<(int Src, int NameSection, int NameOffset)> Virtual = new();
        public int DataSize => LocalFixups;
    }

    /// <summary>Objects in __data__ in file order: offset (section-relative) → class name.</summary>
    public List<(int Offset, string Class)> Objects { get; } = new();
    /// <summary>Class name → signature, from __classnames__.</summary>
    public Dictionary<string, uint> ClassSignatures { get; } = new();
    public Dictionary<string, HkClass> Classes { get; } = new();

    public HkPackfile(byte[] b)
    {
        Bytes = b;
        if (BitConverter.ToUInt32(b, 0) != 0x57E0E057 || BitConverter.ToUInt32(b, 4) != 0x10C0C010)
            throw new InvalidDataException("not a Havok packfile");
        FileVersion = BitConverter.ToInt32(b, 0x0C);
        LayoutRules = b[0x10..0x14];
        int sectionCount = BitConverter.ToInt32(b, 0x14);
        ContentsSectionIndex = BitConverter.ToInt32(b, 0x18);
        ContentsSectionOffset = BitConverter.ToInt32(b, 0x1C);
        ContentsClassNameSectionIndex = BitConverter.ToInt32(b, 0x20);
        ContentsClassNameSectionOffset = BitConverter.ToInt32(b, 0x24);
        ContentsVersion = CString(b, 0x28);
        Flags = BitConverter.ToInt32(b, 0x38);
        int headerEnd = 0x40;
        Predicates = Array.Empty<byte>();
        if (FileVersion >= 11)
        {
            MaxPredicate = BitConverter.ToInt16(b, 0x3C);
            short predicateSize = BitConverter.ToInt16(b, 0x3E);
            Predicates = b[0x40..(0x40 + predicateSize)];
            headerEnd += predicateSize;
        }
        int sectionHeaderSize = FileVersion >= 11 ? 0x40 : 0x30;
        for (int i = 0; i < sectionCount; i++)
        {
            int h = headerEnd + i * sectionHeaderSize;
            var s = new Section
            {
                Name = CString(b, h, 19),
                Start = BitConverter.ToInt32(b, h + 0x14),
                LocalFixups = BitConverter.ToInt32(b, h + 0x18),
                GlobalFixups = BitConverter.ToInt32(b, h + 0x1C),
                VirtualFixups = BitConverter.ToInt32(b, h + 0x20),
                Exports = BitConverter.ToInt32(b, h + 0x24),
                Imports = BitConverter.ToInt32(b, h + 0x28),
                End = BitConverter.ToInt32(b, h + 0x2C),
            };
            for (int p = s.Start + s.LocalFixups; p + 8 <= s.Start + s.GlobalFixups; p += 8)
            {
                int src = BitConverter.ToInt32(b, p);
                if (src == -1) continue;
                s.Local[src] = BitConverter.ToInt32(b, p + 4);
            }
            for (int p = s.Start + s.GlobalFixups; p + 12 <= s.Start + s.VirtualFixups; p += 12)
            {
                int src = BitConverter.ToInt32(b, p);
                if (src == -1) continue;
                s.Global[src] = (BitConverter.ToInt32(b, p + 4), BitConverter.ToInt32(b, p + 8));
            }
            for (int p = s.Start + s.VirtualFixups; p + 12 <= s.Start + s.Exports; p += 12)
            {
                int src = BitConverter.ToInt32(b, p);
                if (src == -1) continue;
                s.Virtual.Add((src, BitConverter.ToInt32(b, p + 4), BitConverter.ToInt32(b, p + 8)));
            }
            Sections.Add(s);
        }

        var names = Sections.FirstOrDefault(s => s.Name == "__classnames__");
        if (names != null)
        {
            int pos = names.Start;
            while (pos + 5 < names.Start + names.DataSize && b[pos + 4] == 0x09)
            {
                uint sig = BitConverter.ToUInt32(b, pos);
                string name = CString(b, pos + 5);
                ClassSignatures[name] = sig;
                pos += 5 + name.Length + 1;
            }
        }
        foreach (var s in Sections)
            foreach (var (src, ns, no) in s.Virtual)
                if (s.Name == "__data__") Objects.Add((src, CString(b, Sections[ns].Start + no)));
        Objects.Sort((x, y) => x.Offset.CompareTo(y.Offset));

        var types = Sections.FirstOrDefault(s => s.Name == "__types__");
        if (types != null) ReadTypes(types);
    }

    public string ClassNameAt(int section, int offset) => CString(Bytes, Sections[section].Start + offset);

    /// <summary>
    /// Session 8 (cutscene track): DS2's demo animations (<c>scn*.scnbnd</c> <c>cutNNNN_a0000.hkx</c>) ship with an
    /// empty __types__ section (no reflection), so their objects can't be decoded or written from their own file.
    /// Classes this file lacks are taken from donor packfiles of the same SDK (hk_2014.1.0-r1) that carry reflection
    /// (an object's <c>a00_*.hkx</c> for the animation classes, its <c>skeleton.hkx</c> for hkaSkeleton). The class
    /// signatures in __classnames__ stay this file's own; written files keep this file's (empty) __types__.
    /// </summary>
    public HkPackfile AdoptTypes(params HkPackfile[] donors)
    {
        foreach (var d in donors)
            foreach (var (name, cls) in d.Classes)
                if (!Classes.ContainsKey(name)) Classes[name] = cls;
        return this;
    }

    static string CString(byte[] b, int pos, int max = int.MaxValue)
    {
        int e = pos;
        while (e < b.Length && e - pos < max && b[e] != 0 && b[e] != 0xFF) e++;
        return Encoding.ASCII.GetString(b, pos, e - pos);
    }

    /// <summary>Resolves a pointer field at <paramref name="src"/> (section-relative) to (section, offset), or null.</summary>
    public (int Section, int Offset)? Pointer(int section, int src)
    {
        var s = Sections[section];
        if (s.Local.TryGetValue(src, out int dst)) return (section, dst);
        if (s.Global.TryGetValue(src, out var g)) return g;
        return null;
    }

    // ------------------------------------------------------------------ reflection (__types__)

    void ReadTypes(Section types)
    {
        int ti = Sections.IndexOf(types);
        foreach (var (src, ns, no) in types.Virtual)
        {
            if (ClassNameAt(ns, no) != "hkClass") continue;
            var c = ReadClass(ti, src);
            Classes[c.Name] = c;
        }
        foreach (var c in Classes.Values)
            if (c.ParentName != null && Classes.TryGetValue(c.ParentName, out var parent)) c.Parent = parent;
    }

    HkClass ReadClass(int section, int off)
    {
        var b = Bytes;
        int a = Sections[section].Start + off;
        string StrAt(int fieldOff)
        {
            var p = Pointer(section, off + fieldOff);
            return p == null ? "" : CString(b, Sections[p.Value.Section].Start + p.Value.Offset);
        }
        var c = new HkClass
        {
            Name = StrAt(0x00),
            ObjectSize = BitConverter.ToInt32(b, a + 0x10),
            NumInterfaces = BitConverter.ToInt32(b, a + 0x14),
            Flags = BitConverter.ToUInt32(b, a + 0x40),
            Version = BitConverter.ToInt32(b, a + 0x44),
        };
        var parentPtr = Pointer(section, off + 0x08);
        if (parentPtr != null) c.ParentName = StrAt2(parentPtr.Value);
        int numEnums = BitConverter.ToInt32(b, a + 0x20);
        var enumsPtr = Pointer(section, off + 0x18);
        if (enumsPtr != null)
            for (int i = 0; i < numEnums; i++) c.Enums.Add(ReadEnum(enumsPtr.Value.Section, enumsPtr.Value.Offset + i * 0x28));
        int numMembers = BitConverter.ToInt32(b, a + 0x30);
        var membersPtr = Pointer(section, off + 0x28);
        if (membersPtr != null)
            for (int i = 0; i < numMembers; i++)
            {
                int mo = membersPtr.Value.Offset + i * 0x28, ma = Sections[membersPtr.Value.Section].Start + mo;
                var m = new HkMember
                {
                    Name = PtrString(membersPtr.Value.Section, mo + 0x00),
                    Type = (HkType)b[ma + 0x18],
                    SubType = (HkType)b[ma + 0x19],
                    CArraySize = BitConverter.ToInt16(b, ma + 0x1A),
                    Flags = BitConverter.ToUInt16(b, ma + 0x1C),
                    Offset = BitConverter.ToUInt16(b, ma + 0x1E),
                };
                var cls = Pointer(membersPtr.Value.Section, mo + 0x08);
                if (cls != null) m.ClassName = StrAt2(cls.Value);
                var en = Pointer(membersPtr.Value.Section, mo + 0x10);
                if (en != null) m.EnumName = PtrString(en.Value.Section, en.Value.Offset);
                c.Members.Add(m);
            }
        return c;
    }

    string StrAt2((int Section, int Offset) cls) => PtrString(cls.Section, cls.Offset);

    string PtrString(int section, int fieldOffset)
    {
        var p = Pointer(section, fieldOffset);
        return p == null ? "" : CString(Bytes, Sections[p.Value.Section].Start + p.Value.Offset);
    }

    HkEnum ReadEnum(int section, int off)
    {
        var e = new HkEnum { Name = PtrString(section, off) };
        int a = Sections[section].Start + off;
        int n = BitConverter.ToInt32(Bytes, a + 0x10);
        var items = Pointer(section, off + 0x08);
        if (items != null)
            for (int i = 0; i < n; i++)
            {
                int io = items.Value.Offset + i * 0x10;
                e.Items.Add((BitConverter.ToInt32(Bytes, Sections[items.Value.Section].Start + io), PtrString(items.Value.Section, io + 8)));
            }
        return e;
    }

    // ------------------------------------------------------------------ generic object model

    readonly Dictionary<int, HkPObj> readObjects = new();

    /// <summary>The object graph rooted at the contents object (usually hkRootLevelContainer).</summary>
    public HkPObj ReadRoot() => ReadObject(ContentsSectionOffset);

    /// <summary>A top-level object in __data__ (offset = section-relative), decoded with the file's own reflection.</summary>
    public HkPObj ReadObject(int offset)
    {
        if (readObjects.TryGetValue(offset, out var o)) return o;
        string cls = Objects.First(x => x.Offset == offset).Class;
        o = new HkPObj(cls, topLevel: true);
        readObjects[offset] = o;
        int data = Sections.FindIndex(s => s.Name == "__data__");
        ReadStruct(o, Classes[cls], data, offset);
        return o;
    }

    void ReadStruct(HkPObj o, HkClass c, int section, int off)
    {
        foreach (var (m, _) in c.AllMembers())
            o.Fields[m.Name] = ReadMember(m, section, off + m.Offset);
    }

    object? ReadMember(HkMember m, int section, int at)
    {
        if (m.CArraySize > 0 && m.Type != HkType.Struct)
        {
            int size = MemberScalarSize(m.Type, m.SubType);
            var list = new List<object?>();
            for (int i = 0; i < m.CArraySize; i++) list.Add(ReadScalar(m.Type, m.SubType, m.ClassName, section, at + i * size));
            return list;
        }
        if (m.Type == HkType.Struct)
        {
            var sc = Classes[m.ClassName!];
            if (m.CArraySize > 0)
            {
                var list = new List<object?>();
                for (int i = 0; i < m.CArraySize; i++) { var so = new HkPObj(sc.Name); ReadStruct(so, sc, section, at + i * sc.ObjectSize); list.Add(so); }
                return list;
            }
            var s = new HkPObj(sc.Name);
            ReadStruct(s, sc, section, at);
            return s;
        }
        if (m.Type is HkType.Array or HkType.SimpleArray)
        {
            int n = BitConverter.ToInt32(Bytes, Sections[section].Start + at + 8);
            var list = new List<object?>(n);
            var p = Pointer(section, at);
            if (p == null || n == 0) return list;
            if (m.SubType == HkType.Struct)
            {
                var ec = Classes[m.ClassName!];
                for (int i = 0; i < n; i++) { var so = new HkPObj(ec.Name); ReadStruct(so, ec, p.Value.Section, p.Value.Offset + i * ec.ObjectSize); list.Add(so); }
                return list;
            }
            int es = MemberScalarSize(m.SubType, HkType.Void);
            for (int i = 0; i < n; i++) list.Add(ReadScalar(m.SubType, HkType.Void, m.ClassName, p.Value.Section, p.Value.Offset + i * es));
            return list;
        }
        return ReadScalar(m.Type, m.SubType, m.ClassName, section, at);
    }

    object? ReadScalar(HkType t, HkType sub, string? cls, int section, int at)
    {
        int a = Sections[section].Start + at;
        var b = Bytes;
        switch (t)
        {
            case HkType.Bool: return b[a] != 0;
            case HkType.Char: case HkType.Int8: return (sbyte)b[a];
            case HkType.UInt8: return b[a];
            case HkType.Int16: return BitConverter.ToInt16(b, a);
            case HkType.UInt16: case HkType.Half: return BitConverter.ToUInt16(b, a);
            case HkType.Int32: return BitConverter.ToInt32(b, a);
            case HkType.UInt32: return BitConverter.ToUInt32(b, a);
            case HkType.Int64: return BitConverter.ToInt64(b, a);
            case HkType.UInt64: case HkType.ULong: return BitConverter.ToUInt64(b, a);
            case HkType.Real: return BitConverter.ToSingle(b, a);
            case HkType.Enum: case HkType.Flags:
                return MemberScalarSize(sub, HkType.Void) switch { 1 => (long)b[a], 2 => BitConverter.ToUInt16(b, a), 4 => BitConverter.ToUInt32(b, a), _ => (long)BitConverter.ToUInt64(b, a) };
            case HkType.Vector4: case HkType.Quaternion: case HkType.Matrix3: case HkType.Rotation: case HkType.QsTransform: case HkType.Matrix4: case HkType.Transform:
            {
                int n = ScalarSize(t) / 4;
                var f = new float[n];
                for (int i = 0; i < n; i++) f[i] = BitConverter.ToSingle(b, a + i * 4);
                return f;
            }
            case HkType.Pointer:
            {
                var p = Pointer(section, at);
                if (p == null) return null;
                var target = Objects.FirstOrDefault(x => x.Offset == p.Value.Offset && Sections[p.Value.Section].Name == "__data__");
                return target.Class != null ? ReadObject(p.Value.Offset) : new HkPRaw(p.Value.Section, p.Value.Offset);
            }
            case HkType.CString: case HkType.StringPtr:
            {
                var p = Pointer(section, at);
                return p == null ? null : CString(Bytes, Sections[p.Value.Section].Start + p.Value.Offset);
            }
            default: return null; // Zero, FunctionPointer, Void, Variant: not serialised
        }
    }

    /// <summary>Size of a member's scalar storage (enums/flags take the size of their storage subtype).</summary>
    public static int MemberScalarSize(HkType t, HkType sub) =>
        t is HkType.Enum or HkType.Flags ? ScalarSize(sub) : ScalarSize(t);

    // ------------------------------------------------------------------ generic object dump

    public string Dump(int maxArray = 8)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"packfile v{FileVersion} '{ContentsVersion}' rules={BitConverter.ToString(LayoutRules)} contents=sec{ContentsSectionIndex}+0x{ContentsSectionOffset:X} flags={Flags} maxPred={MaxPredicate} preds={BitConverter.ToString(Predicates)}");
        foreach (var s in Sections)
            sb.AppendLine($"  section {s.Name}: start 0x{s.Start:X} data 0x{s.DataSize:X} local={s.Local.Count} global={s.Global.Count} virtual={s.Virtual.Count} end 0x{s.End:X}");
        sb.AppendLine("  classes: " + string.Join(", ", ClassSignatures.Select(kv => $"{kv.Key}#{kv.Value:X8}")));
        int data = Sections.FindIndex(s => s.Name == "__data__");
        foreach (var (off, cls) in Objects)
        {
            sb.AppendLine($"  object @0x{off:X} {cls}");
            if (Classes.TryGetValue(cls, out var c)) DumpStruct(sb, c, data, off, "    ", maxArray, 0);
        }
        return sb.ToString();
    }

    void DumpStruct(StringBuilder sb, HkClass c, int section, int off, string indent, int maxArray, int depth)
    {
        if (depth > 6) return;
        foreach (var (m, owner) in c.AllMembers())
        {
            if (m.Flags.HasFlag(1)) continue; // SERIALIZE_IGNORED
            sb.Append($"{indent}{m.Name} ({m.Type}{(m.SubType != HkType.Void ? "/" + m.SubType : "")}{(m.ClassName != null ? ":" + m.ClassName : "")} @{m.Offset:X}): ");
            sb.AppendLine(Value(m, section, off + m.Offset, indent + "  ", maxArray, depth));
        }
    }

    string Value(HkMember m, int section, int at, string indent, int maxArray, int depth)
    {
        var b = Bytes;
        int a = Sections[section].Start + at;
        int count = m.CArraySize > 0 ? m.CArraySize : 1;
        if (m.CArraySize > 0 && m.Type != HkType.Struct)
        {
            var parts = new List<string>();
            int size = ScalarSize(m.Type);
            for (int i = 0; i < count; i++) parts.Add(Scalar(m.Type, a + i * size, section, at + i * size));
            return "[" + string.Join(", ", parts) + "]";
        }
        switch (m.Type)
        {
            case HkType.Array:
            case HkType.SimpleArray:
            {
                int n = BitConverter.ToInt32(b, a + 8);
                var p = Pointer(section, at);
                if (p == null || n == 0) return $"[] (n={n})";
                var sb = new StringBuilder($"n={n}");
                if (m.SubType == HkType.Struct && m.ClassName != null && Classes.TryGetValue(m.ClassName, out var ec))
                {
                    for (int i = 0; i < Math.Min(n, maxArray); i++)
                    {
                        sb.Append($"\n{indent}[{i}]\n");
                        var inner = new StringBuilder();
                        DumpStruct(inner, ec, p.Value.Section, p.Value.Offset + i * ec.ObjectSize, indent + "  ", maxArray, depth + 1);
                        sb.Append(inner.ToString().TrimEnd('\n'));
                    }
                    return sb.ToString();
                }
                int es = ScalarSize(m.SubType);
                var items = new List<string>();
                for (int i = 0; i < Math.Min(n, maxArray * 4); i++)
                    items.Add(Scalar(m.SubType, Sections[p.Value.Section].Start + p.Value.Offset + i * es, p.Value.Section, p.Value.Offset + i * es));
                return $"n={n} [{string.Join(", ", items)}{(n > maxArray * 4 ? ", ..." : "")}]";
            }
            case HkType.Struct:
            {
                if (m.ClassName == null || !Classes.TryGetValue(m.ClassName, out var sc)) return "?struct";
                var sb = new StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    sb.Append($"\n{indent}{(count > 1 ? $"[{i}]" : "{")}\n");
                    var inner = new StringBuilder();
                    DumpStruct(inner, sc, section, at + i * sc.ObjectSize, indent + "  ", maxArray, depth + 1);
                    sb.Append(inner.ToString().TrimEnd('\n'));
                }
                return sb.ToString();
            }
            default:
                return Scalar(m.Type, a, section, at);
        }
    }

    string Scalar(HkType t, int a, int section, int at)
    {
        var b = Bytes;
        switch (t)
        {
            case HkType.Bool: return b[a] != 0 ? "true" : "false";
            case HkType.Char: case HkType.Int8: return ((sbyte)b[a]).ToString();
            case HkType.UInt8: return b[a].ToString();
            case HkType.Int16: return BitConverter.ToInt16(b, a).ToString();
            case HkType.UInt16: case HkType.Half: return BitConverter.ToUInt16(b, a).ToString();
            case HkType.Int32: return BitConverter.ToInt32(b, a).ToString();
            case HkType.UInt32: case HkType.Flags: case HkType.Enum: return $"0x{BitConverter.ToUInt32(b, a):X}";
            case HkType.Int64: case HkType.UInt64: case HkType.ULong: return BitConverter.ToInt64(b, a).ToString();
            case HkType.Real: return BitConverter.ToSingle(b, a).ToString("0.####");
            case HkType.Vector4: case HkType.Quaternion:
                return $"({F(a)}, {F(a + 4)}, {F(a + 8)}, {F(a + 12)})";
            case HkType.Matrix3: case HkType.Rotation: case HkType.QsTransform: case HkType.Matrix4: case HkType.Transform:
            {
                int n = ScalarSize(t) / 4;
                return "(" + string.Join(", ", Enumerable.Range(0, n).Select(i => F(a + i * 4))) + ")";
            }
            case HkType.Pointer:
            {
                var p = Pointer(section, at);
                if (p == null) return "null";
                var obj = Objects.FirstOrDefault(o => o.Offset == p.Value.Offset);
                return $"→0x{p.Value.Offset:X}{(obj.Class != null ? " " + obj.Class : "")}";
            }
            case HkType.CString: case HkType.StringPtr:
            {
                var p = Pointer(section, at);
                return p == null ? "null" : $"\"{CString(Bytes, Sections[p.Value.Section].Start + p.Value.Offset)}\"";
            }
            default: return $"?{t}";
        }
    }

    string F(int a) => BitConverter.ToSingle(Bytes, a).ToString("0.###");

    public static int ScalarSize(HkType t) => t switch
    {
        HkType.Bool or HkType.Char or HkType.Int8 or HkType.UInt8 => 1,
        HkType.Int16 or HkType.UInt16 or HkType.Half => 2,
        HkType.Int32 or HkType.UInt32 or HkType.Real or HkType.Enum or HkType.Flags => 4,
        HkType.Int64 or HkType.UInt64 or HkType.ULong or HkType.Pointer or HkType.CString or HkType.StringPtr or HkType.FunctionPointer => 8,
        HkType.Vector4 or HkType.Quaternion => 16,
        HkType.Matrix3 or HkType.Rotation => 48,
        HkType.QsTransform => 48,
        HkType.Matrix4 or HkType.Transform => 64,
        HkType.Array or HkType.SimpleArray => 16,
        _ => 0,
    };
}

public enum HkType : byte
{
    Void, Bool, Char, Int8, UInt8, Int16, UInt16, Int32, UInt32, Int64, UInt64, Real, Vector4, Quaternion, Matrix3, Rotation,
    QsTransform, Matrix4, Transform, Zero, Pointer, FunctionPointer, Array, InplaceArray, Enum, Struct, SimpleArray,
    HomogeneousArray, Variant, CString, ULong, Flags, Half, StringPtr, RelArray,
}

public sealed class HkClass
{
    public string Name = "";
    public string? ParentName;
    public HkClass? Parent;
    public int ObjectSize, NumInterfaces, Version;
    public uint Flags;
    public List<HkEnum> Enums = new();
    public List<HkMember> Members = new();

    public IEnumerable<(HkMember Member, HkClass Owner)> AllMembers()
    {
        if (Parent != null) foreach (var x in Parent.AllMembers()) yield return x;
        foreach (var m in Members) yield return (m, this);
    }

    public override string ToString() => $"{Name} : {ParentName} size=0x{ObjectSize:X} v{Version}";
}

public sealed class HkMember
{
    public string Name = "";
    public HkType Type, SubType;
    public short CArraySize;
    public ushort Flags;
    public ushort Offset;
    public string? ClassName, EnumName;
    public override string ToString() => $"{Name} {Type}/{SubType} @0x{Offset:X} [{CArraySize}] {ClassName}";
}

public sealed class HkEnum
{
    public string Name = "";
    public List<(int Value, string Name)> Items = new();
}

static class HkFlagExt
{
    public static bool HasFlag(this ushort flags, int bit) => (flags & bit) != 0;
}

/// <summary>
/// A Havok object or embedded struct: class name + member values (see HkPackfile.ReadObject for value kinds).
/// TopLevel = a pointed-to object (serialised on its own, with a virtual fixup); otherwise an inline struct.
/// </summary>
public sealed class HkPObj
{
    public string Class;
    public bool TopLevel;
    public Dictionary<string, object?> Fields = new();
    public HkPObj(string cls, bool topLevel = false) { Class = cls; TopLevel = topLevel; }
    public object? this[string key]
    {
        get => Fields.TryGetValue(key, out var v) ? v : null;
        set => Fields[key] = value;
    }
    public HkPObj Get(string key) => (HkPObj)Fields[key]!;
    public List<object?> List(string key) => (List<object?>)Fields[key]!;

    /// <summary>Copies this object: inline structs, lists and vectors are copied; pointed-to objects are shared.</summary>
    public HkPObj Clone()
    {
        var c = new HkPObj(Class, TopLevel);
        foreach (var (k, v) in Fields) c.Fields[k] = CloneValue(v);
        return c;
    }

    static object? CloneValue(object? v) => v switch
    {
        HkPObj { TopLevel: false } o => o.Clone(),
        List<object?> l => l.Select(CloneValue).ToList(),
        float[] f => f.ToArray(),
        _ => v,
    };
}

/// <summary>A pointer into a non-object location (e.g. the __types__ section).</summary>
public sealed record HkPRaw(int Section, int Offset);
