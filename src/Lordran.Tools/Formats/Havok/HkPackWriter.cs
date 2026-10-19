using System.Text;

namespace Lordran.Tools.Formats.Havok;

/// <summary>
/// Writes a Havok 2014 packfile (DS2 SOTFS layout: v11, 64-bit pointers, little-endian) from an <see cref="HkPObj"/>
/// graph, using a template packfile from the game for everything that isn't object data: the header, predicates,
/// __classnames__ (class signatures) and __types__ (hkClass reflection, copied verbatim, so the game sees exactly the
/// class descriptions it ships). Object layout (member offsets, sizes) comes from that same reflection.
/// __data__ is laid out depth-first: each object (16-aligned), then its out-of-line data (arrays, strings; 16-aligned)
/// in member order, then the objects it points to. Pointers to data are local fixups, pointers to objects global
/// fixups, and every object gets a virtual fixup naming its class.
/// </summary>
public sealed class HkPackWriter
{
    readonly HkPackfile template;
    readonly Dictionary<string, int> classNameOffsets = new();
    readonly int classNamesIndex, typesIndex, dataIndex;

    MemoryStream data = new();
    readonly List<(int Src, int Dst)> local = new();
    readonly List<(int Src, HkPObj Target)> global = new();
    readonly List<(int Src, string Class)> virt = new();
    readonly Dictionary<HkPObj, int> placed = new(ReferenceEqualityComparer.Instance);

    public HkPackWriter(HkPackfile template)
    {
        this.template = template;
        classNamesIndex = template.Sections.FindIndex(s => s.Name == "__classnames__");
        typesIndex = template.Sections.FindIndex(s => s.Name == "__types__");
        dataIndex = template.Sections.FindIndex(s => s.Name == "__data__");
        var cn = template.Sections[classNamesIndex];
        var b = template.Bytes;
        int pos = cn.Start;
        while (pos + 5 < cn.Start + cn.DataSize && b[pos + 4] == 0x09)
        {
            int s = pos + 5, e = s;
            while (b[e] != 0) e++;
            classNameOffsets[Encoding.ASCII.GetString(b, s, e - s)] = s - cn.Start;
            pos = e + 1;
        }
    }

    public byte[] Write(HkPObj root)
    {
        data = new MemoryStream();
        local.Clear(); global.Clear(); virt.Clear(); placed.Clear();
        WriteObjectTree(root);

        var b = template.Bytes;
        var output = new MemoryStream();
        var w = new BinaryWriter(output);
        // Header + predicates + section headers are copied, then section offsets are patched.
        int sectionHeaders = template.FileVersion >= 11 ? 0x40 + template.Predicates.Length : 0x40;
        int sectionHeaderSize = template.FileVersion >= 11 ? 0x40 : 0x30;
        int headerEnd = sectionHeaders + template.Sections.Count * sectionHeaderSize;
        w.Write(b, 0, headerEnd);
        if (!classNameOffsets.TryGetValue(root.Class, out int rootName)) throw new InvalidOperationException($"class {root.Class} not in template");
        PatchInt(output, 0x18, dataIndex);       // contents section
        PatchInt(output, 0x1C, 0);               // contents offset (root first)
        PatchInt(output, 0x20, classNamesIndex);
        PatchInt(output, 0x24, rootName);

        for (int si = 0; si < template.Sections.Count; si++)
        {
            var s = template.Sections[si];
            Align(output, 16, 0xFF);
            int start = (int)output.Position;
            int headerAt = sectionHeaders + si * sectionHeaderSize;
            if (si != dataIndex)
            {
                // Copied verbatim (data + its fixup tables); offsets inside are section-relative.
                output.Write(b, s.Start, s.End);
                PatchInt(output, headerAt + 0x14, start);
                continue;
            }
            byte[] d = data.ToArray();
            output.Write(d);
            Align(output, 16, 0);
            int localOff = (int)output.Position - start;
            foreach (var (src, dst) in local.OrderBy(x => x.Src)) { w.Write(src); w.Write(dst); }
            Align(output, 16, 0xFF);
            int globalOff = (int)output.Position - start;
            foreach (var (src, target) in global.OrderBy(x => x.Src)) { w.Write(src); w.Write(dataIndex); w.Write(placed[target.Target2()]); }
            Align(output, 16, 0xFF);
            int virtOff = (int)output.Position - start;
            foreach (var (src, cls) in virt.OrderBy(x => x.Src))
            {
                if (!classNameOffsets.TryGetValue(cls, out int no)) throw new InvalidOperationException($"class {cls} not in template");
                w.Write(src); w.Write(classNamesIndex); w.Write(no);
            }
            Align(output, 16, 0xFF);
            int end = (int)output.Position - start;
            PatchInt(output, headerAt + 0x14, start);
            PatchInt(output, headerAt + 0x18, localOff);
            PatchInt(output, headerAt + 0x1C, globalOff);
            PatchInt(output, headerAt + 0x20, virtOff);
            PatchInt(output, headerAt + 0x24, end);
            PatchInt(output, headerAt + 0x28, end);
            PatchInt(output, headerAt + 0x2C, end);
        }
        return output.ToArray();
    }

    static void PatchInt(MemoryStream s, int at, int value)
    {
        long keep = s.Position;
        s.Position = at;
        s.Write(BitConverter.GetBytes(value));
        s.Position = keep;
    }

    static void Align(Stream s, int n, byte fill)
    {
        while (s.Position % n != 0) s.WriteByte(fill);
    }

    // ------------------------------------------------------------------ data section

    sealed class Deferred
    {
        public int FieldAt;               // where the pointer goes (section-relative)
        public Action<int> Write = _ => { }; // writes the payload; argument = payload start
    }

    readonly Queue<Deferred> deferred = new();
    readonly List<HkPObj> childOrder = new();

    void WriteObjectTree(HkPObj obj)
    {
        if (placed.ContainsKey(obj)) return;
        var cls = Class(obj.Class);
        Pad(16);
        int at = (int)data.Length;
        placed[obj] = at;
        virt.Add((at, obj.Class));
        data.SetLength(at + cls.ObjectSize);
        var children = new List<HkPObj>();
        WriteStruct(obj, cls, at, children);
        while (deferred.Count > 0)
        {
            var d = deferred.Dequeue();
            Pad(16);
            int start = (int)data.Length;
            local.Add((d.FieldAt, start));
            d.Write(start);
        }
        foreach (var c in children) WriteObjectTree(c);
    }

    void Pad(int n)
    {
        while (data.Length % n != 0) data.SetLength(data.Length + 1);
    }

    HkClass Class(string name) => template.Classes.TryGetValue(name, out var c) ? c : throw new InvalidOperationException($"class {name} not in the template's __types__");

    void WriteStruct(HkPObj o, HkClass cls, int at, List<HkPObj> children)
    {
        foreach (var (m, _) in cls.AllMembers())
        {
            if ((m.Flags & 1) != 0) continue; // SERIALIZE_IGNORED: left zero
            o.Fields.TryGetValue(m.Name, out var v);
            WriteMember(m, v, at + m.Offset, children);
        }
    }

    void WriteMember(HkMember m, object? v, int at, List<HkPObj> children)
    {
        if (m.Type == HkType.Struct)
        {
            var sc = Class(m.ClassName!);
            if (m.CArraySize > 0)
            {
                var list = v as List<object?> ?? new();
                for (int i = 0; i < m.CArraySize && i < list.Count; i++)
                    if (list[i] is HkPObj so) WriteStruct(so, sc, at + i * sc.ObjectSize, children);
            }
            else if (v is HkPObj s) WriteStruct(s, sc, at, children);
            return;
        }
        if (m.CArraySize > 0)
        {
            var list = v as List<object?> ?? new();
            int size = HkPackfile.MemberScalarSize(m.Type, m.SubType);
            for (int i = 0; i < m.CArraySize && i < list.Count; i++) WriteScalar(m.Type, m.SubType, list[i], at + i * size, children);
            return;
        }
        if (m.Type is HkType.Array or HkType.SimpleArray)
        {
            var list = v as System.Collections.IList ?? new List<object?>();
            int n = list.Count;
            Put(at + 8, BitConverter.GetBytes(n));
            if (m.Type == HkType.Array) Put(at + 12, BitConverter.GetBytes(n == 0 ? 0 : n | unchecked((int)0x80000000)));
            if (n == 0) return;
            if (m.SubType == HkType.Struct)
            {
                var ec = Class(m.ClassName!);
                deferred.Enqueue(new Deferred
                {
                    FieldAt = at,
                    Write = start =>
                    {
                        data.SetLength(start + ec.ObjectSize * n);
                        for (int i = 0; i < n; i++) WriteStruct((HkPObj)list[i]!, ec, start + i * ec.ObjectSize, children);
                    },
                });
            }
            else
            {
                int es = HkPackfile.MemberScalarSize(m.SubType, HkType.Void);
                deferred.Enqueue(new Deferred
                {
                    FieldAt = at,
                    Write = start =>
                    {
                        data.SetLength(start + es * n);
                        if (list is byte[] bytes) { Put(start, bytes); return; }
                        for (int i = 0; i < n; i++) WriteScalar(m.SubType, HkType.Void, list[i], start + i * es, children);
                    },
                });
            }
            return;
        }
        WriteScalar(m.Type, m.SubType, v, at, children);
    }

    void WriteScalar(HkType t, HkType sub, object? v, int at, List<HkPObj> children)
    {
        switch (t)
        {
            case HkType.Bool: Put(at, new[] { (byte)(v is true ? 1 : 0) }); return;
            case HkType.Char: case HkType.Int8: case HkType.UInt8: Put(at, new[] { (byte)Convert.ToInt64(v ?? 0) }); return;
            case HkType.Int16: case HkType.UInt16: Put(at, BitConverter.GetBytes((ushort)Convert.ToInt64(v ?? 0))); return;
            // hkHalf: the top 16 bits of a float32 (session 8: written as an integer, 1.0 became 2 → timeFactor/gravityFactor ~0, frozen break fragments)
            case HkType.Half: Put(at, BitConverter.GetBytes(v is float or double ? (ushort)(BitConverter.SingleToInt32Bits(Convert.ToSingle(v)) >> 16) : (ushort)Convert.ToInt64(v ?? 0))); return;
            case HkType.Int32: case HkType.UInt32: Put(at, BitConverter.GetBytes((uint)Convert.ToInt64(v ?? 0))); return;
            case HkType.Int64: case HkType.UInt64: case HkType.ULong: Put(at, BitConverter.GetBytes(Convert.ToUInt64(v ?? 0UL))); return;
            case HkType.Real: Put(at, BitConverter.GetBytes(Convert.ToSingle(v ?? 0f))); return;
            case HkType.Enum: case HkType.Flags:
            {
                long x = Convert.ToInt64(v ?? 0);
                int size = HkPackfile.MemberScalarSize(t, sub);
                Put(at, BitConverter.GetBytes(x).AsSpan(0, size).ToArray());
                return;
            }
            case HkType.Vector4: case HkType.Quaternion: case HkType.Matrix3: case HkType.Rotation: case HkType.QsTransform: case HkType.Matrix4: case HkType.Transform:
            {
                if (v is not float[] f) return;
                var bytes = new byte[f.Length * 4];
                Buffer.BlockCopy(f, 0, bytes, 0, bytes.Length);
                Put(at, bytes);
                return;
            }
            case HkType.Pointer:
                if (v is HkPObj target)
                {
                    global.Add((at, target));
                    if (!children.Contains(target)) children.Add(target);
                }
                return;
            case HkType.CString: case HkType.StringPtr:
                if (v is string s)
                    deferred.Enqueue(new Deferred { FieldAt = at, Write = start => Put(start, Encoding.ASCII.GetBytes(s + "\0")) });
                return;
            default: return;
        }
    }

    void Put(int at, byte[] bytes) => Put(at, (ReadOnlySpan<byte>)bytes);

    void Put(int at, ReadOnlySpan<byte> bytes)
    {
        if (data.Length < at + bytes.Length) data.SetLength(at + bytes.Length);
        data.Position = at;
        data.Write(bytes);
    }
}

static class HkPObjExt
{
    public static HkPObj Target2(this HkPObj o) => o;
}
