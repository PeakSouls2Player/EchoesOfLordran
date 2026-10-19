using System.Buffers.Binary;
using System.Text;

namespace Lordran.Tools.Formats.Morpheme;

/// <summary>
/// DS2 SOTFS Morpheme 4 (x64) NMB network file: bundle walker plus readers/writers for the rig (bundle type 1), the
/// rig→anim channel map (2), the event tracks (3 discrete / 4 duration / 5 curve) and a reader for the file-name table
/// (13). Session 8 port of our own Python research code (data\research\morpheme\nmb_rig.py, NSA-FORMAT.md §2); no
/// NaturalMotion code. Python results (re-checked by the C# port with nmb-roundtrip): all DS2 NMBs re-emit
/// byte-identically, write(read()) is byte-identical for every rig, channel map and event track.
///
/// Bundle header (0x30): u32 0x18, u32 10 (version), u32 type, u32 assetId, u8 guid[16], u64 dataSize, u32 alignment,
/// u32 (0x7FE for types 1 and 12, else 0); data at the next alignment boundary; the next bundle at the next 4-byte
/// boundary. Types: 12 file header, 7 character controller, 1 rig, 2 channel map, 3/4/5 event tracks, 10 network,
/// 13 file-name table. The assetId is the reference key (the network refers to rigs, maps, tracks by id, never by
/// offset), so a rewritten bundle keeps its id and GUID and bundle positions may shift freely.
/// </summary>
public sealed class NmbFile
{
    public const int HeaderSize = 0x30;
    public byte[] Bytes = [];
    public List<Bundle> Bundles = [];

    public sealed class Bundle
    {
        public uint Magic, Version, Type, AssetId;
        public byte[] Guid = new byte[16];
        public uint Align, U2C;
        public int Offset, DataOffset;
        public byte[] Data = [];
        public byte[] Gap = [];         // bytes between the data end and the next bundle (zeros)

        public byte[] HeaderBytes(int size)
        {
            var h = new byte[HeaderSize];
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(0), Magic);
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(4), Version);
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(8), Type);
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(12), AssetId);
            Guid.CopyTo(h, 16);
            BinaryPrimitives.WriteUInt64LittleEndian(h.AsSpan(32), (ulong)size);
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(40), Align);
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(44), U2C);
            return h;
        }
    }

    static int Al(int x, int a) => (x + a - 1) / a * a;

    public static NmbFile Read(byte[] d)
    {
        var f = new NmbFile { Bytes = d };
        int p = 0;
        while (p < d.Length)
        {
            var b = new Bundle
            {
                Offset = p,
                Magic = U32(d, p), Version = U32(d, p + 4), Type = U32(d, p + 8), AssetId = U32(d, p + 12),
                Guid = d[(p + 16)..(p + 32)],
                Align = U32(d, p + 40), U2C = U32(d, p + 44),
            };
            long size = (long)BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(p + 32));
            b.DataOffset = Al(p + HeaderSize, (int)b.Align);
            b.Data = d[b.DataOffset..(int)(b.DataOffset + size)];
            f.Bundles.Add(b);
            p = b.DataOffset + (int)size;
            if (p < d.Length)
            {
                int q = Math.Min(Al(p, 4), d.Length);
                if (q < d.Length && U32(d, q) != 0x18) throw new InvalidDataException($"NMB: no bundle at 0x{q:X}");
                b.Gap = d[p..q]; p = q;
            }
        }
        return f;
    }

    /// <summary>Re-emit the bundles; <paramref name="replace"/> = {bundle index: new data} (size field updated).</summary>
    public byte[] Rebuild(IReadOnlyDictionary<int, byte[]>? replace = null)
    {
        var o = new List<byte>(Bytes.Length + 4096);
        for (int i = 0; i < Bundles.Count; i++)
        {
            var b = Bundles[i];
            var data = replace != null && replace.TryGetValue(i, out var r) ? r : b.Data;
            o.AddRange(b.HeaderBytes(data.Length));
            while (o.Count % b.Align != 0) o.Add(0);
            o.AddRange(data);
            while (o.Count % 4 != 0) o.Add(0);
        }
        return o.ToArray();
    }

    public int IndexOf(uint type) => Bundles.FindIndex(b => b.Type == type);

    // ------------------------------------------------------------------ helpers
    internal static uint U32(byte[] d, int o) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o));
    internal static int I32(byte[] d, int o) => BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(o));
    internal static ulong U64(byte[] d, int o) => BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(o));
    internal static float F32(byte[] d, int o) => BinaryPrimitives.ReadSingleLittleEndian(d.AsSpan(o));

    internal sealed class Buf
    {
        public readonly List<byte> B = [];
        public int Len => B.Count;
        public void U8(byte v) => B.Add(v);
        public void U16(ushort v) { B.Add((byte)v); B.Add((byte)(v >> 8)); }
        public void U32(uint v) { for (int i = 0; i < 4; i++) B.Add((byte)(v >> (8 * i))); }
        public void I32(int v) => U32((uint)v);
        public void U64(ulong v) { for (int i = 0; i < 8; i++) B.Add((byte)(v >> (8 * i))); }
        public void F32(float v) => U32(BitConverter.SingleToUInt32Bits(v));
        public void Raw(IEnumerable<byte> x) => B.AddRange(x);
        public void Fill(byte v, int n) { for (int i = 0; i < n; i++) B.Add(v); }
        public void PadTo(int len, byte v) { while (B.Count < len) B.Add(v); }
        public void Pad(int a, byte v) { while (B.Count % a != 0) B.Add(v); }
        public void PatchU64(int o, ulong v) { for (int i = 0; i < 8; i++) B[o + i] = (byte)(v >> (8 * i)); }
        public byte[] ToArray() => B.ToArray();
    }

    /// <summary>Bitset: u32 nbits, u32 nwords, words MSB-first (bit i = word[i/32] &gt;&gt; (31 - i%32)).</summary>
    public static bool[] ReadBitset(byte[] d, int o)
    {
        int nbits = (int)U32(d, o);
        var bits = new bool[nbits];
        for (int i = 0; i < nbits; i++) bits[i] = ((U32(d, o + 8 + 4 * (i / 32)) >> (31 - i % 32)) & 1) != 0;
        return bits;
    }

    internal static byte[] WriteBitset(bool[] bits)
    {
        int nw = (bits.Length + 31) / 32;
        var words = new uint[nw];
        for (int i = 0; i < bits.Length; i++) if (bits[i]) words[i / 32] |= 1u << (31 - i % 32);
        var w = new Buf(); w.U32((uint)bits.Length); w.U32((uint)nw);
        foreach (uint x in words) w.U32(x);
        return w.ToArray();
    }

    /// <summary>String table: u32 n, u32 dataLen, u64 pIds, u64 pOffsets, u64 pData (relative to the table), u32 id[n],
    /// u32 offset[n], NUL-terminated strings.</summary>
    public static (List<uint> Ids, List<string> Names) ReadStringTable(byte[] d, int o)
    {
        int n = (int)U32(d, o);
        int pIds = (int)U64(d, o + 8), pOffs = (int)U64(d, o + 16), pData = (int)U64(d, o + 24);
        var ids = new List<uint>(); var names = new List<string>();
        for (int i = 0; i < n; i++)
        {
            ids.Add(U32(d, o + pIds + 4 * i));
            int s = o + pData + (int)U32(d, o + pOffs + 4 * i);
            int e = Array.IndexOf(d, (byte)0, s);
            names.Add(Encoding.ASCII.GetString(d, s, e - s));
        }
        return (ids, names);
    }
}

/// <summary>
/// Morpheme rig (NMB bundle type 1; NSA-FORMAT.md §2.2). Rig local space is Z-up; DS2 maps Morpheme character space to
/// FLVER model space by the reflection (x, y, z) → (x, z, y) (c1030: exact for all 49 shared bones). DS2 rigs start with
/// CharacterWorldSpaceTM (unused), Character, Master (trajectory, index 2), Root (character root, index 3).
/// Layout: blend-frame orientation (x,y,z,w) and translation; ptr hierarchy; u32 trajectory bone, u32 character root;
/// ptr bone-name string table; ptr bind pose (AttribData type 13 transform buffer: used-bitset, element descriptors
/// {2,16,16} positions and {6,16,16} quaternions, 16-byte records allocated for roundup(n,4) bones, spares 0xCD).
/// </summary>
public sealed class NmbRig
{
    public float[] BlendQ = new float[4], BlendT = new float[4];
    public int Traj = 2, Root = 3;
    public List<string> Names = [];
    public List<int> Parents = [];
    public List<float[]> BindPos = [];  // x, y, z, w(0) per bone
    public List<float[]> BindRot = [];  // x, y, z, w per bone
    public bool[] BindUsed = [];

    public static NmbRig Read(byte[] d)
    {
        var r = new NmbRig();
        for (int i = 0; i < 4; i++) { r.BlendQ[i] = NmbFile.F32(d, 4 * i); r.BlendT[i] = NmbFile.F32(d, 16 + 4 * i); }
        int pHier = (int)NmbFile.U64(d, 0x20);
        r.Traj = (int)NmbFile.U32(d, 0x28); r.Root = (int)NmbFile.U32(d, 0x2C);
        int pNames = (int)NmbFile.U64(d, 0x30), pBind = (int)NmbFile.U64(d, 0x38);
        int n = (int)NmbFile.U32(d, pHier);
        for (int i = 0; i < n; i++) r.Parents.Add(NmbFile.I32(d, pHier + 16 + 4 * i));
        r.Names = NmbFile.ReadStringTable(d, pNames).Names;
        int bb = pBind + (int)NmbFile.U64(d, pBind + 0x10);
        int blen = (int)NmbFile.U32(d, bb + 16);
        int pEl = (int)NmbFile.U64(d, bb + 32), pPtrs = (int)NmbFile.U64(d, bb + 40), pBits = (int)NmbFile.U64(d, bb + 48);
        r.BindUsed = NmbFile.ReadBitset(d, bb + pBits);
        int pPos = (int)NmbFile.U64(d, bb + pPtrs), pRot = (int)NmbFile.U64(d, bb + pPtrs + 8);
        for (int i = 0; i < blen; i++)
        {
            r.BindPos.Add([.. Enumerable.Range(0, 4).Select(k => NmbFile.F32(d, bb + pPos + 16 * i + 4 * k))]);
            r.BindRot.Add([.. Enumerable.Range(0, 4).Select(k => NmbFile.F32(d, bb + pRot + 16 * i + 4 * k))]);
        }
        return r;
    }

    static int Al(int x, int a) => (x + a - 1) / a * a;

    /// <summary>Serialise into bundle-type-1 data with the DS2 files' layout and padding rules.</summary>
    public byte[] Write()
    {
        int n = Names.Count;
        var w = new NmbFile.Buf();
        foreach (float f in BlendQ) w.F32(f);
        foreach (float f in BlendT) w.F32(f);
        w.U64(0); w.U32((uint)Traj); w.U32((uint)Root); w.U64(0); w.U64(0); // pointers patched below
        int pHier = w.Len;
        w.U32((uint)n); w.U32(0xCDCDCDCD); w.U64(0);                       // parent-array pointer stored as 0
        foreach (int p in Parents) w.I32(p);
        int pNames = w.Len;
        var strs = new List<byte>(); var offs = new List<int>();
        foreach (var s in Names) { offs.Add(strs.Count); strs.AddRange(Encoding.ASCII.GetBytes(s)); strs.Add(0); }
        int pIds = 32, pOffs = pIds + 4 * n, pData = pOffs + 4 * n;
        w.U32((uint)n); w.U32((uint)strs.Count); w.U64((ulong)pIds); w.U64((ulong)pOffs); w.U64((ulong)pData);
        for (int i = 0; i < n; i++) w.U32((uint)i);
        foreach (int o in offs) w.U32((uint)o);
        w.Raw(strs);
        w.Pad(16, 0xCD);
        int pBind = w.Len;
        // AttribData header: 8 x 0xCD, u16 0xFFFF, u16 type 13, 4 x 0xCD; pointer to the buffer (0x20); 8 x 0xCD
        w.Fill(0xCD, 8); w.U16(0xFFFF); w.U16(13); w.Fill(0xCD, 4);
        w.U64(0x20); w.Fill(0xCD, 8);
        int bb = w.Len;
        int nw = (n + 31) / 32;
        int pBits = 0x38, pEl = Al(pBits + 8 + 4 * nw, 4), pPtrs = Al(pEl + 24, 4);
        int n4 = Al(n, 4);
        int pPos = Al(pPtrs + 16, 16), pRot = Al(pPos + 16 * n4, 16);
        int bsize = pRot + 16 * n4;
        w.U64((ulong)bsize); w.U64(16);
        w.U32((uint)n); w.Raw(new byte[] { 1, 0xCD, 0xCD, 0xCD }); w.U32(2); w.Fill(0xCD, 4);
        w.U64((ulong)pEl); w.U64((ulong)pPtrs); w.U64((ulong)pBits);
        w.Raw(NmbFile.WriteBitset(BindUsed));
        w.PadTo(bb + pEl, 0xCD);
        w.U32(2); w.U32(16); w.U32(16); w.U32(6); w.U32(16); w.U32(16);
        w.PadTo(bb + pPtrs, 0xCD);
        w.U64((ulong)pPos); w.U64((ulong)pRot);
        w.PadTo(bb + pPos, 0xCD);
        foreach (var v in BindPos) foreach (float f in v) w.F32(f);
        w.PadTo(bb + pRot, 0xCD);
        foreach (var v in BindRot) foreach (float f in v) w.F32(f);
        w.PadTo(bb + bsize, 0xCD);
        w.Pad(16, 0xCD);
        w.PatchU64(0x20, (ulong)pHier);
        w.PatchU64(0x30, (ulong)pNames); w.PatchU64(0x38, (ulong)pBind);
        return w.ToArray();
    }
}

/// <summary>
/// Rig→anim channel map ("AnimToRig", NMB bundle type 2; NSA-FORMAT.md §2.3): u32 map type (1), u32 0, u64 pUsedBits
/// (0x28), u64 subSize, u64 4, u64 pSub; used bitset over rig bones; sub-struct (4-aligned): u32 numChannels,
/// u16 numChannels ×2, u64 0x12, u16 0, u16 anim_to_rig[numChannels]. anim_to_rig[c] = rig bone of NSA channel c
/// (identity+1 on c1030/c3050; not identity on some boss rigs, e.g. c6030 248 bones / 231 channels).
/// </summary>
public sealed class NmbChannelMap
{
    public uint MapType = 1;
    public bool[] Used = [];
    public List<int> AnimToRig = [];

    public static NmbChannelMap Read(byte[] d)
    {
        var m = new NmbChannelMap { MapType = NmbFile.U32(d, 0) };
        int pBits = (int)NmbFile.U64(d, 8), pSub = (int)NmbFile.U64(d, 32);
        m.Used = NmbFile.ReadBitset(d, pBits);
        int nAnim = (int)NmbFile.U32(d, pSub);
        int pTab = (int)NmbFile.U64(d, pSub + 8);
        for (int i = 0; i < nAnim; i++) m.AnimToRig.Add(BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(pSub + pTab + 2 * i)));
        return m;
    }

    public byte[] Write()
    {
        int na = AnimToRig.Count;
        var bits = NmbFile.WriteBitset(Used);
        int pBits = 0x28, pSub = (pBits + bits.Length + 3) / 4 * 4;
        var sub = new NmbFile.Buf();
        sub.U32((uint)na); sub.U16((ushort)na); sub.U16((ushort)na); sub.U64(0x12);
        sub.U16(0);                                     // table starts at sub + 0x12
        foreach (int v in AnimToRig) sub.U16((ushort)v);
        sub.Pad(4, 0);
        var w = new NmbFile.Buf();
        w.U32(MapType); w.U32(0); w.U64((ulong)pBits); w.U64((ulong)sub.Len); w.U64(4); w.U64((ulong)pSub);
        w.Raw(bits);
        w.PadTo(pSub, 0);
        w.Raw(sub.B);
        w.Pad(16, 0);
        return w.ToArray();
    }
}

/// <summary>
/// Event track (NMB bundle types 3 discrete / 4 duration / 5 curve; NSA-FORMAT.md §2.4): i32 numEvents, i32 eventType
/// (0 discrete, 2 duration), u64 pName, i32 userData, i32 trackId, u64 pEvents (0x20, or 0 without events), events
/// (f32 start, f32 duration, i32 value; start/duration are fractions of the clip), the name (ASCII; a few Shift-JIS)
/// + NUL, 0xCD padding to 16. Known DS2 tracks: TimeAct (userData 1000, value = TAE animation id, e.g. 65101010 for
/// a65_10_1010), Footsteps, AttackStateControl/SpeedControl/CancelControl, AttackDuration and HitDuration (gameplay
/// windows), IK (9500), UpperBody (9800), Gravity (9600), HitControll (9700), InputControl.
/// </summary>
public sealed class NmbEventTrack
{
    public uint BundleType;
    public int ChannelId, UserData, TrackId;
    public byte[] NameBytes = [];
    public List<(float Start, float Duration, int Value)> Events = [];

    static Encoding? sjis;
    public string Name
    {
        get
        {
            if (NameBytes.All(b => b < 0x80)) return Encoding.ASCII.GetString(NameBytes);
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); sjis ??= Encoding.GetEncoding(932); return sjis.GetString(NameBytes); }
            catch { return Encoding.Latin1.GetString(NameBytes); }
        }
    }

    public static NmbEventTrack Read(byte[] d, uint bundleType)
    {
        var t = new NmbEventTrack
        {
            BundleType = bundleType,
            ChannelId = NmbFile.I32(d, 4), UserData = NmbFile.I32(d, 16), TrackId = NmbFile.I32(d, 20),
        };
        int n = NmbFile.I32(d, 0);
        int pName = (int)NmbFile.U64(d, 8), pEv = (int)NmbFile.U64(d, 24);
        t.NameBytes = d[pName..Array.IndexOf(d, (byte)0, pName)];
        for (int i = 0; i < n; i++) t.Events.Add((NmbFile.F32(d, pEv + 12 * i), NmbFile.F32(d, pEv + 12 * i + 4), NmbFile.I32(d, pEv + 12 * i + 8)));
        return t;
    }

    public byte[] Write()
    {
        int n = Events.Count;
        var w = new NmbFile.Buf();
        w.I32(n); w.I32(ChannelId); w.U64((ulong)(0x20 + 12 * n)); w.I32(UserData); w.I32(TrackId); w.U64(n > 0 ? 0x20UL : 0UL);
        foreach (var (s, du, v) in Events) { w.F32(s); w.F32(du); w.I32(v); }
        w.Raw(NameBytes); w.U8(0);
        w.Pad(16, 0xCD);
        return w.ToArray();
    }
}

/// <summary>File-name table (NMB bundle type 13; NSA-FORMAT.md §2.5): string tables for the NSA file names, formats
/// ("nsa"), source paths ($(RootDir)\motion_xmd\...xmd) and take names, then u32 hashes. The network's SourceAnim
/// attribs refer to clips by index in this table.</summary>
public sealed class NmbFileNames
{
    public List<string> Anims = [], Formats = [], Sources = [], Takes = [];

    public static NmbFileNames Read(byte[] d) => new()
    {
        Anims = NmbFile.ReadStringTable(d, (int)NmbFile.U64(d, 0)).Names,
        Formats = NmbFile.ReadStringTable(d, (int)NmbFile.U64(d, 8)).Names,
        Sources = NmbFile.ReadStringTable(d, (int)NmbFile.U64(d, 16)).Names,
        Takes = NmbFile.ReadStringTable(d, (int)NmbFile.U64(d, 24)).Names,
    };
}
