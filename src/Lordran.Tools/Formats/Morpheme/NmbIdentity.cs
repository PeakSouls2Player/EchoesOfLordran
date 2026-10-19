using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SoulsFormats;

namespace Lordran.Tools.Formats.Morpheme;

/// <summary>
/// Rule I1b for every converted (1:1) anibnd (session 10, chrint stage 2; first applied to the npcidle network def by
/// <c>NpcIdle.NetworkIdentity</c>): <b>every NMB bundle DS2 registers by asset id gets an id of its own unless its content is
/// the template's, byte for byte</b>; the network def always gets one.
/// <para>Why (V-code): the NMB bundle loader FUN_1404e0f40 looks each bundle's id up in the global asset manager
/// (FUN_1405f5aa0, a hash map keyed by the id alone) and, when the id is registered already, only adds a reference
/// (FUN_1405f5c00) and drops this file's copy; types below 13 except 7 (character controller) and 12 (file header) are
/// registered. The file-name table (13) and the clip container are named after the network def's id
/// (<c>MorphemeFileNameLookUpTable_%08x</c>, <c>MorphemeAnimContainer_%08x</c>, FUN_1404e1d10), and an existing container
/// only receives clips into its empty slots (FUN_1404deb10). So an anibnd that keeps its template's ids (c1030's for the
/// regulars, c3050's for the Asylum Demon) for other content gets whatever was loaded first: the template's network and
/// clips, or the first-loaded converted character's rig / tracks for every other one.</para>
/// <para>References (V-code + survey): the network def names the other bundles only through four attrib fields, each
/// turned into a pointer by an attrib "locate" function through FUN_1405f5aa0: SourceEventTracks (23) id entries
/// (FUN_1405f2c80: three groups of u64 ids → pointers), SourceAnim (21) +0x78 channel map (FUN_1405f4ba0: +0x78 → +0x18),
/// the rig reference (20) +0x10 and the character controller (43) +0x10 (FUN_1405f8830 / FUN_1405f8cf0). <see cref="IdFields"/>
/// lists them; <c>nmb-ids --refs</c> checks on every vanilla NMB that these fields resolve to bundles of the right type and
/// that no bundle id occurs anywhere else in the network.</para>
/// <para>Ids are deterministic and drawn through one pack-wide <see cref="Registry"/> (chrint stage-2 fix pass): the network
/// def per anibnd (MD5 of "Lordran.I1b/{anibnd}/10/{old id}/{k}": it names the clip container and the file-name table, so
/// it is never shared), every other renamed bundle <b>by content</b> (MD5 of "Lordran.I1b/{type}/{MD5(data)}/{k}", rule
/// I1c): byte-identical converted content in two anibnds (the c2541/2/3 variants' rigs and tracks) shares one id, the
/// I1b-keep / vanilla sharing pattern, and so one entry of DS2's fixed asset-manager pool (rule I1p: 0x3000 entries,
/// FUN_1405f4e00; an empty pool is a NULL write in FUN_1405f65c0). k is re-drawn while the id is 0 / 0xFFFFFFFF, any id of
/// any vanilla DS2 NMB (all anibnds / extanibnds under morpheme4), or an id the registry already holds for other content
/// (other anibnds' network defs and content, c8001's network def: <see cref="Registry.Claim"/>). The GUID comes from the
/// same seed in DS2's random-GUID layout. build-pack also runs <c>nmb-ids</c>'s pack check before writing.</para>
/// </summary>
public static class NmbIdentity
{
    /// <summary>Bundle types DS2's NMB loader registers in the global asset manager by id (FUN_1404e0f40).</summary>
    public static bool Registered(uint type) => type < 13 && type != 7 && type != 12;

    static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    static ulong U64(byte[] b, int o) => BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(o));

    /// <summary>The asset-id reference fields of a network bundle: (byte offset of the u32 id, attrib type 20 / 21 / 23 / 43).</summary>
    public static List<(int Offset, int Attrib)> IdFields(byte[] x)
    {
        var net = new NmbNetwork(x);
        var o = new List<(int, int)>();
        foreach (var n in net.Nodes())
            foreach (var a in n.Attribs)
                switch (a.Type)
                {
                    case 20: case 43: o.Add((a.Offset + 0x10, a.Type)); break;
                    case 21: o.Add((a.Offset + 0x78, 21)); break;
                    case 23:
                        for (int k = 0; k < 3; k++)
                        {
                            int cnt = (int)U32(x, a.Offset + 0x10 + 0x18 * k);
                            long pid = (long)U64(x, a.Offset + 0x18 + 0x18 * k);
                            for (int j = 0; j < cnt; j++) o.Add((a.Offset + (int)pid + 8 * j, 23));
                        }
                        break;
                }
        return o;
    }

    /// <summary>Which bundle types an id field may name.</summary>
    public static bool FieldType(int attrib, uint bundleType) => attrib switch
    {
        20 => bundleType == 1,
        21 => bundleType == 2,
        23 => bundleType is 3 or 4 or 5,
        43 => bundleType == 7,
        _ => false,
    };

    static readonly Dictionary<string, HashSet<uint>> vanillaCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every bundle id of every NMB under <c>{ds2Data}\morpheme4</c> (anibnds and extanibnds, all types). Cached.</summary>
    public static HashSet<uint> VanillaIds(string ds2Data)
    {
        string dir = Path.GetFullPath(Path.Combine(ds2Data, "morpheme4"));
        lock (vanillaCache)
        {
            if (vanillaCache.TryGetValue(dir, out var have)) return have;
            var ids = new HashSet<uint>();
            foreach (var p in Directory.GetFiles(dir, "*anibnd.dcx", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
                foreach (var nmb in Nmbs(File.ReadAllBytes(p)))
                    foreach (var b in nmb.Bundles) ids.Add(b.AssetId);
            if (ids.Count < 10000) throw new InvalidDataException($"I1b: only {ids.Count} vanilla NMB ids under {dir}");
            vanillaCache[dir] = ids;
            return ids;
        }
    }

    /// <summary>The NMBs of an (optionally DCX-compressed) BND3/BND4 binder.</summary>
    public static IEnumerable<NmbFile> Nmbs(byte[] raw)
    {
        if (DCX.Is(raw)) raw = DCX.Decompress(raw);
        IEnumerable<BinderFile> files = BND4.Is(raw) ? BND4.Read(raw).Files : BND3.Is(raw) ? BND3.Read(raw).Files : [];
        foreach (var f in files.Where(f => f.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase))) yield return NmbFile.Read(f.Bytes);
    }

    /// <summary>
    /// The ids one build hands out (pack-wide; one per process and DS2 data folder: <see cref="For"/>). Vanilla ids are never
    /// handed out; every other id is held for one content key: a network def's key names its anibnd ("net/c2501"), any
    /// other bundle's key is its type, header and data hash. A key may get an id the registry already holds for the same key
    /// (shared content), never one held for another key.
    /// </summary>
    public sealed class Registry(HashSet<uint> vanilla)
    {
        public readonly HashSet<uint> Vanilla = vanilla;
        readonly Dictionary<uint, (string Key, string Who)> held = [];
        public int Count => held.Count;

        /// <summary>Whether <paramref name="id"/> may carry <paramref name="key"/>.</summary>
        public bool Free(uint id, string key) => id != 0 && id != 0xFFFFFFFF && !Vanilla.Contains(id) && (!held.TryGetValue(id, out var h) || h.Key == key);

        /// <summary>Record an id emitted outside <see cref="Apply"/> (c8001's network def, NpcIdle); throws when another
        /// key already holds it (a pack-wide collision is a build error, never a silent share).</summary>
        public void Claim(uint id, string key, string who)
        {
            if (Vanilla.Contains(id)) throw new InvalidDataException($"I1b: {who} uses vanilla NMB id 0x{id:X8} for {key}");
            if (held.TryGetValue(id, out var h) && h.Key != key)
                throw new InvalidDataException($"I1b: NMB id 0x{id:X8} of {who} ({key}) is already {h.Who}'s ({h.Key})");
            held.TryAdd(id, (key, who));
        }

        /// <summary>A new id + GUID for <paramref name="key"/> from <paramref name="seed"/> (k = 0, 1, … while not
        /// <see cref="Free"/> or in <paramref name="local"/>), claimed for it.</summary>
        public (uint Id, byte[] Guid, bool Existing) Draw(string seed, string key, string who, ISet<uint>? local = null)
        {
            for (int k = 0; ; k++)
            {
                byte[] h = MD5.HashData(Encoding.ASCII.GetBytes($"{seed}/{k}"));
                uint id = BitConverter.ToUInt32(h, 0);
                if (!Free(id, key) || local?.Contains(id) == true) continue;
                bool existing = held.ContainsKey(id);
                Claim(id, key, who);
                byte[] g = MD5.HashData(Encoding.ASCII.GetBytes($"{seed}/{k}/guid"));
                g[6] = (byte)((g[6] & 0x0F) | 0x40);   // random-GUID layout like DS2's network GUIDs
                g[8] = (byte)((g[8] & 0x3F) | 0x80);
                return (id, g, existing);
            }
        }
    }

    static readonly Dictionary<string, Registry> registries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The build's registry for <paramref name="ds2Data"/> (vanilla ids of its morpheme4 folder): every 1:1 anibnd
    /// and c8001 of one build-pack run draw / claim through it.</summary>
    public static Registry For(string ds2Data)
    {
        var v = VanillaIds(ds2Data);
        string dir = Path.GetFullPath(Path.Combine(ds2Data, "morpheme4"));
        lock (registries)
        {
            if (!registries.TryGetValue(dir, out var r)) registries[dir] = r = new Registry(v);
            return r;
        }
    }

    /// <summary>The content key of a non-network bundle (rule I1c): type, header fields and data hash.</summary>
    public static string ContentKey(NmbFile.Bundle b) => $"{b.Type}/{b.Magic:x}/{b.Version:x}/{b.Align:x}/{b.U2C:x}/{Convert.ToHexString(MD5.HashData(b.Data))}";

    public sealed class Result
    {
        public byte[] Nmb = [];
        public uint NetworkId, OldNetworkId;
        public int Kept;
        /// <summary>Renamed bundles whose content id another anibnd of this build already holds (rule I1c).</summary>
        public int Shared;
        /// <summary>old id → new id.</summary>
        public Dictionary<uint, uint> Map = [];
        public SortedDictionary<uint, int> RenamedByType = [];
        public SortedDictionary<int, int> FieldsPatched = [];
        public List<string> Problems = [];
        public string Summary => $"rule I1b: network def 0x{OldNetworkId:X8} → 0x{NetworkId:X8}; {Map.Count} registered bundles with new content got ids of their own "
            + $"(by type {string.Join(", ", RenamedByType.Select(kv => $"{kv.Key}: {kv.Value}"))}; {Shared} of them content another anibnd of this build already holds, rule I1c), {Kept} identical to the template's keep its ids; "
            + $"network references re-pointed by attrib {string.Join(", ", FieldsPatched.Select(kv => $"{kv.Key}: {kv.Value}"))}"
            + (Problems.Count == 0 ? "; references resolve" : "; PROBLEMS " + string.Join("; ", Problems.Take(8)));
    }

    /// <summary>
    /// Apply I1b to <paramref name="nmbBytes"/> (a converted NMB) given its template's original NMB: every registered bundle
    /// whose (type, id, data) is not the template's gets a new id from <paramref name="reg"/>: the network def always, by
    /// <paramref name="anibnd"/> (e.g. "c2501"); every other one by content (rule I1c: the n-th copy of one content inside
    /// this NMB gets content key + "#n", so ids stay unique within the NMB); the network's reference fields are re-pointed;
    /// nothing else changes. Throws on duplicate ids.
    /// </summary>
    public static Result Apply(byte[] nmbBytes, byte[] templateNmb, string anibnd, Registry reg)
    {
        var nmb = NmbFile.Read(nmbBytes);
        var tpl = NmbFile.Read(templateNmb);
        var res = new Result();
        var dup = nmb.Bundles.GroupBy(b => b.AssetId).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dup.Count > 0) throw new InvalidDataException($"I1b {anibnd}: duplicate bundle ids {string.Join(", ", dup.Select(d => $"0x{d:X8}"))}");
        var tplData = tpl.Bundles.GroupBy(b => (b.Type, b.AssetId)).ToDictionary(g => g.Key, g => g.First().Data);
        int ni = nmb.IndexOf(10);
        res.OldNetworkId = nmb.Bundles[ni].AssetId;
        bool Keep(NmbFile.Bundle b) => b.Type != 10 && tplData.TryGetValue((b.Type, b.AssetId), out var d) && d.AsSpan().SequenceEqual(b.Data);
        // ids that stay in this NMB (unregistered bundles, template-identical ones): never drawn for a renamed bundle
        var local = nmb.Bundles.Where(b => !Registered(b.Type) || Keep(b)).Select(b => b.AssetId).ToHashSet();
        var occurrences = new Dictionary<string, int>();
        foreach (var b in nmb.Bundles)
        {
            if (!Registered(b.Type)) continue;
            if (Keep(b)) { res.Kept++; continue; }
            (uint Id, byte[] Guid, bool Existing) drawn;
            if (b.Type == 10) drawn = reg.Draw($"Lordran.I1b/{anibnd}/{b.Type}/{b.AssetId:x8}", $"net/{anibnd}", anibnd, local);
            else
            {
                string key = ContentKey(b);
                int n = occurrences[key] = occurrences.GetValueOrDefault(key) + 1;
                string hash = key[(key.LastIndexOf('/') + 1)..];
                drawn = reg.Draw($"Lordran.I1b/{b.Type}/{hash}" + (n > 1 ? $"#{n}" : ""), n > 1 ? $"{key}#{n}" : key, anibnd, local);
            }
            var (id, guid, existing) = drawn;
            if (existing) res.Shared++;
            res.Map[b.AssetId] = id;
            b.AssetId = id; b.Guid = guid;
            res.RenamedByType[b.Type] = res.RenamedByType.GetValueOrDefault(b.Type) + 1;
        }
        var dup2 = nmb.Bundles.GroupBy(b => b.AssetId).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dup2.Count > 0) throw new InvalidDataException($"I1b {anibnd}: renaming left duplicate bundle ids {string.Join(", ", dup2.Select(d => $"0x{d:X8}"))}");
        res.NetworkId = nmb.Bundles[ni].AssetId;
        byte[] x = (byte[])nmb.Bundles[ni].Data.Clone();
        var fields = IdFields(x);
        foreach (var (o, at) in fields)
        {
            uint v = U32(x, o);
            if (!res.Map.TryGetValue(v, out uint w)) continue;
            BinaryPrimitives.WriteUInt32LittleEndian(x.AsSpan(o), w);
            res.FieldsPatched[at] = res.FieldsPatched.GetValueOrDefault(at) + 1;
        }
        // checks: every field names a bundle of the right type (controller 0 = none); no old id left in any field
        var byId = nmb.Bundles.ToDictionary(b => b.AssetId);
        foreach (var (o, at) in fields)
        {
            uint v = U32(x, o);
            if (at == 43 && v == 0) continue;
            if (!byId.TryGetValue(v, out var b)) res.Problems.Add($"attrib {at} @0x{o:X} names 0x{v:X8}: no such bundle");
            else if (!FieldType(at, b.Type)) res.Problems.Add($"attrib {at} @0x{o:X} names a type-{b.Type} bundle");
        }
        res.Nmb = nmb.Rebuild(new Dictionary<int, byte[]> { [ni] = x });
        return res;
    }
}
