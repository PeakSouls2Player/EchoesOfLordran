using System.Text;

namespace Lordran.Tools.Formats.Havok;

/// <summary>
/// Reader for Havok binary tagfiles (DSR: "TAG0", SDK "20150100"): a tree of chunks (u32 big-endian size with flag
/// bits on top, 4-char tag). TAG0 holds SDKV (version), DATA (raw object data), TYPE (TSTR/TNAM/FSTR/TBOD/THSH: type
/// names, template arguments, field names, type bodies, hashes) and INDX (ITEM: objects/arrays as type+offset+count;
/// PTCH: where pointers live). Pointers in DATA hold item indices. Objects are decoded generically into
/// <see cref="HkObj"/> trees using the file's own type descriptions.
/// </summary>
public sealed class HkTagfile
{
    public string Sdk { get; private set; } = "";
    public byte[] Data { get; private set; } = Array.Empty<byte>();
    public List<HkTType?> Types { get; } = new() { null };
    public List<HkItem> Items { get; } = new();
    readonly List<string> tstr = new(), fstr = new();

    public sealed class HkTType
    {
        public int Index;
        public string Name = "";
        public List<(string Name, object Value)> Templates = new();
        public HkTType? Parent;
        public int Flags;           // body flags
        public int Format;          // "subtype" format bits (kind in low 5 bits)
        public HkTType? Pointer;    // element/pointee type
        public int Version;
        public int Size, Alignment;
        public int TypeFlags;       // flags & 0x10 value
        public List<HkTField> Fields = new();
        public List<(HkTType Type, int Value)> Interfaces = new();
        public uint Hash;

        public int Kind => Format & 0x1F;
        public override string ToString() => Name + (Templates.Count > 0 ? "<" + string.Join(",", Templates.Select(t => t.Value is HkTType tt ? tt.Name : t.Value.ToString())) + ">" : "");

        public IEnumerable<HkTField> AllFields()
        {
            if (Parent != null) foreach (var f in Parent.AllFields()) yield return f;
            foreach (var f in Fields) yield return f;
        }

        /// <summary>Follows typedef-like wrappers (no fields, no own format) to the type that has the layout.</summary>
        public HkTType Resolved()
        {
            var t = this;
            while (t.Format == 0 && t.Parent != null && t.Fields.Count == 0) t = t.Parent;
            return t;
        }
    }

    public sealed record HkTField(string Name, int Flags, int Offset, HkTType Type);
    public sealed record HkItem(HkTType? Type, int Flags, int Offset, int Count)
    {
        public bool IsPtr => (Flags & 0x10) != 0;
    }

    public HkTagfile(byte[] bytes)
    {
        var br = new Reader(bytes);
        ReadChunk(br, bytes.Length);
    }

    sealed class Reader
    {
        public byte[] B; public int Pos;
        public Reader(byte[] b) { B = b; }
        public uint U32BE() { uint v = (uint)(B[Pos] << 24 | B[Pos + 1] << 16 | B[Pos + 2] << 8 | B[Pos + 3]); Pos += 4; return v; }
        public uint U32() { uint v = BitConverter.ToUInt32(B, Pos); Pos += 4; return v; }
        public string Tag() { string s = Encoding.ASCII.GetString(B, Pos, 4); Pos += 4; return s; }
        public byte U8() => B[Pos++];
        public long Packed()
        {
            byte c = U8();
            if ((c & 0x80) == 0) return c;
            switch (c >> 3)
            {
                case >= 0x10 and <= 0x17: return ((c << 8) | U8()) & 0x3FFF;
                case >= 0x18 and <= 0x1B: return ((c << 16) | (U8() << 8) | U8()) & 0x1FFFFF;
                case 0x1C: return (((long)c << 24) | ((long)U8() << 16) | ((long)U8() << 8) | U8()) & 0x7FFFFFF;
                case 0x1D: { long v = c & 7; for (int i = 0; i < 4; i++) v = (v << 8) | U8(); return v; }
                case 0x1E: { long v = c & 7; for (int i = 0; i < 7; i++) v = (v << 8) | U8(); return v; }
                default: { long v = 0; for (int i = 0; i < 8; i++) v = (v << 8) | U8(); return v; }
            }
        }
    }

    void ReadChunk(Reader br, int limit)
    {
        while (br.Pos + 8 <= limit)
        {
            int start = br.Pos;
            uint head = br.U32BE();
            int size = (int)(head & 0x3FFFFFFF);
            string tag = br.Tag();
            int end = start + size;
            switch (tag)
            {
                case "TAG0": case "TYPE": case "INDX": case "TCRF":
                    ReadChunk(br, end); break;
                case "SDKV": Sdk = Encoding.ASCII.GetString(br.B, br.Pos, end - br.Pos); break;
                case "DATA": Data = br.B[br.Pos..end]; break;
                case "TSTR": ReadStrings(br, end, tstr); break;
                case "FSTR": ReadStrings(br, end, fstr); break;
                case "TNAM": ReadNames(br, end); break;
                case "TBOD": ReadBodies(br, end); break;
                case "THSH": ReadHashes(br); break;
                case "ITEM": ReadItems(br, end); break;
            }
            br.Pos = end;
        }
    }

    static void ReadStrings(Reader br, int end, List<string> into)
    {
        int s = br.Pos;
        for (int i = br.Pos; i < end; i++)
            if (br.B[i] == 0) { into.Add(Encoding.ASCII.GetString(br.B, s, i - s)); s = i + 1; }
    }

    void ReadNames(Reader br, int end)
    {
        int count = (int)br.Packed();
        for (int i = 1; i < count; i++) Types.Add(new HkTType { Index = i });
        for (int i = 1; i < count; i++)
        {
            var t = Types[i]!;
            t.Name = tstr[(int)br.Packed()];
            int templates = (int)br.Packed();
            for (int j = 0; j < templates; j++)
            {
                string name = tstr[(int)br.Packed()];
                long value = br.Packed();
                t.Templates.Add((name, name.StartsWith('t') ? (object)(Types[(int)value] ?? (object)"null") : value));
            }
        }
    }

    void ReadBodies(Reader br, int end)
    {
        while (br.Pos < end)
        {
            int index = (int)br.Packed();
            if (index == 0) continue;
            var t = Types[index]!;
            int parent = (int)br.Packed();
            t.Parent = parent > 0 ? Types[parent] : null;
            t.Flags = (int)br.Packed();
            if ((t.Flags & 0x01) != 0)
            {
                t.Format = (int)br.Packed();
                if ((t.Format & 0x0F) is 6 or 7 or 8 or 9 || t.Kind is 6 or 8 or 9) { }
            }
            if ((t.Flags & 0x02) != 0) { int p = (int)br.Packed(); t.Pointer = p > 0 ? Types[p] : null; }
            if ((t.Flags & 0x04) != 0) t.Version = (int)br.Packed();
            if ((t.Flags & 0x08) != 0) { t.Size = (int)br.Packed(); t.Alignment = (int)br.Packed(); }
            if ((t.Flags & 0x10) != 0) t.TypeFlags = (int)br.Packed();
            if ((t.Flags & 0x20) != 0)
            {
                int n = (int)(br.Packed() & 0xFFFF);
                for (int i = 0; i < n; i++)
                {
                    string name = fstr[(int)br.Packed()];
                    int flags = (int)br.Packed();
                    int offset = (int)br.Packed();
                    int type = (int)br.Packed();
                    t.Fields.Add(new HkTField(name, flags, offset, Types[type]!));
                }
            }
            if ((t.Flags & 0x40) != 0)
            {
                int n = (int)br.Packed();
                for (int i = 0; i < n; i++) t.Interfaces.Add((Types[(int)br.Packed()]!, (int)br.Packed()));
            }
            if ((t.Flags & 0x80) != 0) br.Packed(); // attribute (unused here)
        }
    }

    void ReadHashes(Reader br)
    {
        int n = (int)br.Packed();
        for (int i = 0; i < n; i++)
        {
            int t = (int)br.Packed();
            uint h = br.U32();
            if (Types[t] != null) Types[t]!.Hash = h;
        }
    }

    void ReadItems(Reader br, int end)
    {
        while (br.Pos + 12 <= end)
        {
            uint tf = br.U32();
            int offset = (int)br.U32();
            int count = (int)br.U32();
            int ti = (int)(tf & 0xFFFFFF);
            Items.Add(new HkItem(ti > 0 && ti < Types.Count ? Types[ti] : null, (int)(tf >> 24), offset, count));
        }
    }

    // ------------------------------------------------------------------ object decoding

    /// <summary>Decoded value: HkObj (record), List&lt;object?&gt; (array/tuple), primitives, string, or null.</summary>
    public sealed class HkObj
    {
        public HkTType Type = null!;
        public int ItemIndex;
        public Dictionary<string, object?> Fields = new();
        public object? this[string name] => Fields.TryGetValue(name, out var v) ? v : null;
        public override string ToString() => $"{Type.Name}#{ItemIndex}";
    }

    readonly Dictionary<int, HkObj> objects = new();

    public HkObj? Root => ItemObject(1);

    public HkObj? ItemObject(int item)
    {
        if (item <= 0 || item >= Items.Count) return null;
        if (objects.TryGetValue(item, out var o)) return o;
        var it = Items[item];
        if (it.Type == null) return null;
        o = new HkObj { Type = it.Type, ItemIndex = item };
        objects[item] = o;
        DecodeRecord(o, it.Type, it.Offset);
        return o;
    }

    void DecodeRecord(HkObj o, HkTType type, int at)
    {
        foreach (var f in type.AllFields())
            o.Fields[f.Name] = Value(f.Type, at + f.Offset);
    }

    public object? Value(HkTType type, int at)
    {
        var t = type.Resolved();
        int kind = t.Kind;
        switch (kind)
        {
            case 0: return null;           // void
            case 1: return null;           // opaque
            case 2: return SizeOf(t) switch { 1 => Data[at] != 0, 2 => BitConverter.ToUInt16(Data, at) != 0, _ => BitConverter.ToUInt32(Data, at) != 0 };
            case 3:                         // string: pointer to item with chars
            {
                long idx = BitConverter.ToInt64(Data, at);
                if (idx <= 0 || idx >= Items.Count) return null;
                var it = Items[(int)idx];
                return Encoding.UTF8.GetString(Data, it.Offset, Math.Max(0, it.Count - 1));
            }
            case 4:                         // int
            {
                bool signed = (t.Format & 0x200) != 0;
                int bits = (t.Format & 0x2000) != 0 ? 8 : (t.Format & 0x4000) != 0 ? 16 : (t.Format & 0x8000) != 0 ? 32 : 64;
                return bits switch
                {
                    8 => signed ? (object)(sbyte)Data[at] : Data[at],
                    16 => signed ? BitConverter.ToInt16(Data, at) : BitConverter.ToUInt16(Data, at),
                    32 => signed ? BitConverter.ToInt32(Data, at) : BitConverter.ToUInt32(Data, at),
                    _ => signed ? BitConverter.ToInt64(Data, at) : BitConverter.ToUInt64(Data, at),
                };
            }
            case 5:                         // float (32-bit; hkHalf = 16: the top half of a float32, not IEEE half: 0x3F80 = 1.0)
                return (t.Format & 0x1F00) == 0x0F00 || SizeOf(t) == 2 ? (object)BitConverter.Int32BitsToSingle(BitConverter.ToUInt16(Data, at) << 16) : BitConverter.ToSingle(Data, at);
            case 6:                         // pointer → item index
            {
                long idx = BitConverter.ToInt64(Data, at);
                return idx > 0 ? ItemObject((int)idx) : null;
            }
            case 7:                         // record
            {
                var o = new HkObj { Type = t, ItemIndex = -1 };
                DecodeRecord(o, t, at);
                return o;
            }
            case 8 when (t.Format & 0x20) != 0: // fixed tuple T[N]: count in format >> 8
            {
                int count = t.Format >> 8;
                var elem = t.Pointer ?? t;
                int es = SizeOf(elem);
                var list = new List<object?>(count);
                for (int i = 0; i < count; i++) list.Add(Value(elem, at + i * es));
                return list;
            }
            case 8:                         // array (hkArray-like): item index, size
            {
                long idx = BitConverter.ToInt64(Data, at);
                if (idx <= 0 || idx >= Items.Count) return new List<object?>();
                var it = Items[(int)idx];
                return ArrayItems(it);
            }
            case 9:                         // fixed tuple: count in format >> 8
            {
                int count = t.Format >> 8;
                var elem = t.Pointer ?? t;
                int es = SizeOf(elem);
                var list = new List<object?>(count);
                for (int i = 0; i < count; i++) list.Add(Value(elem, at + i * es));
                return list;
            }
            default: return $"?kind{kind}";
        }
    }

    public List<object?> ArrayItems(HkItem it)
    {
        var list = new List<object?>(it.Count);
        if (it.Type == null) return list;
        if (it.IsPtr)
        {
            for (int i = 0; i < it.Count; i++) list.Add(ItemObject(Items.IndexOf(it))); // single object item
            return list;
        }
        int es = SizeOf(it.Type);
        for (int i = 0; i < it.Count; i++) list.Add(Value(it.Type, it.Offset + i * es));
        return list;
    }

    public static int SizeOf(HkTType t)
    {
        for (var x = t; x != null; x = x.Parent)
            if (x.Size > 0) return x.Size;
        return 0;
    }

    /// <summary>Raw bytes of an array field's backing item (e.g. MOPP code, vertex buffers).</summary>
    public byte[] ArrayBytes(int at, out int count, out HkTType? elemType)
    {
        long idx = BitConverter.ToInt64(Data, at);
        count = 0; elemType = null;
        if (idx <= 0 || idx >= Items.Count) return Array.Empty<byte>();
        var it = Items[(int)idx];
        count = it.Count; elemType = it.Type;
        int es = it.Type != null ? SizeOf(it.Type) : 1;
        return Data[it.Offset..(it.Offset + es * it.Count)];
    }

    public string DumpTypes()
    {
        var sb = new StringBuilder();
        foreach (var t in Types.Skip(1))
        {
            if (t == null) continue;
            sb.AppendLine($"[{t.Index}] {t} : {t.Parent?.ToString() ?? "-"} flags=0x{t.Flags:X} fmt=0x{t.Format:X} ptr={t.Pointer} v{t.Version} size={t.Size}/{t.Alignment} hash={t.Hash:X8}");
            foreach (var f in t.Fields) sb.AppendLine($"      {f.Name} @{f.Offset} : {f.Type} (fl {f.Flags})");
        }
        return sb.ToString();
    }

    public string Dump(int maxArray = 6)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"tagfile SDK {Sdk}: {Types.Count - 1} types, {Items.Count} items, data {Data.Length} bytes");
        var seen = new HashSet<HkObj>();
        void Walk(object? v, string indent, string name, int depth)
        {
            switch (v)
            {
                case HkObj o:
                    sb.AppendLine($"{indent}{name}: {o.Type}{(o.ItemIndex > 0 ? $" (item {o.ItemIndex})" : "")}");
                    if (depth > 30 || !seen.Add(o) && o.ItemIndex > 0) return;
                    foreach (var (k, fv) in o.Fields) Walk(fv, indent + "  ", k, depth + 1);
                    break;
                case List<object?> l:
                    if (l.Count > 0 && l[0] is HkObj)
                    {
                        sb.AppendLine($"{indent}{name}: [{l.Count}]");
                        for (int i = 0; i < Math.Min(l.Count, maxArray); i++) Walk(l[i], indent + "  ", $"[{i}]", depth + 1);
                    }
                    else
                        sb.AppendLine($"{indent}{name}: [{l.Count}] {string.Join(", ", l.Take(maxArray * 3).Select(x => x is float f ? f.ToString("0.###") : x?.ToString()))}{(l.Count > maxArray * 3 ? ", …" : "")}");
                    break;
                default:
                    sb.AppendLine($"{indent}{name}: {(v is float f2 ? f2.ToString("0.####") : v)}");
                    break;
            }
        }
        Walk(Root, "", "root", 0);
        return sb.ToString();
    }
}
