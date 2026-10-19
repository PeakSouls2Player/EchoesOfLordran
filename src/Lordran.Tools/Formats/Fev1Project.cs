using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Lordran.Tools.Formats;

/// <summary>
/// DS1's FMOD Designer projects (DSR sound\frpg_*.fev: "FEV1", version 0x003D0000), read and extended for the 1:1
/// characters (session 8, track chrsound). The file is one sequential stream of little-endian fields and length-prefixed
/// strings (lstr = u32 length incl. NUL + bytes); no absolute offsets. Layout as far as it is used here (checked on DSR
/// frpg_c2731/c2750/c2210/c2500/c2540/c2232/frpg_main/frpg_m18):
/// <list type="bullet">
/// <item>header: "FEV1", u32 version, u32 A, u32 B, lstr project name. A = Σ over sound definitions of
/// align4(name length + 1) + 24 per waveform, B = A + 8 per waveform (memory sizes: the formula reproduces A and B of all
/// eight projects, frpg_main's 786 definitions / 1,325 waveforms included);</item>
/// <item>wave banks: u32 count, per bank {u32 load mode (0x200), u32 max streams (10), u64 hash (= the FSB4's hash at
/// 0x18), lstr name}; DS1's own frpg_c2232 (Asylum Demon) lists two banks, frpg_main and frpg_c2232;</item>
/// <item>categories: recursive {lstr name, f32 volume, f32 pitch, u32, u32, u32 child count, children} (master: music,
/// Ghost, SE, Voice, Default in every DS1 project);</item>
/// <item>event groups: u32 count; DS1 character and main projects hold one group {lstr name, u32 user properties 0, u32
/// subgroups 0, u32 event count, events}; an event = u32 8, lstr name (a letter + 9 digits), 16-byte GUID, ...,
/// ending with its category lstr; the events of the simple kind DS1 uses (one layer, one sound instance: u32 layer count
/// 1 at name+0xa3, u16 instance count 1 at +0xad) name their sound definition by the u16 at name+0xb1 (c2500: every
/// footstep event → c2500_foot_walk, c250005001 → c2500_v_dead1; frpg_main c999999999 → enemy-damage);</item>
/// <item>sound definition configs: u32 count + 64-byte records, right after the last event;</item>
/// <item>sound definitions: u32 count, per definition {lstr name, u32 config index, u32 waveform count, per waveform
/// {u32 type 0, u32 weight, lstr file, lstr bank name, u32 index in the bank, u32 length ms}};</item>
/// <item>then u32 0 and the music data ("comp"/"sett" chunks), kept as they are.</item>
/// </list>
/// <see cref="AddEvents"/> copies events from another DS1 project (DS1's frpg_main) into this one with their sound
/// definitions, configs and wave bank entry, under new names: the way DS1's own frpg_c2232 already plays frpg_main waves.
/// </summary>
public sealed class Fev1Project
{
    public readonly byte[] Bytes;
    public string Name = "";
    public sealed record Bank(string Name, int Start, int End);
    public sealed record Event(string Name, int Start, int End)   // Start = the u32 8 before the name; End = after its category
    {
        public int NameAt => Start + 4;
    }
    public sealed record Wave(uint Type, uint Weight, string File, string Bank, uint Index, uint LengthMs);
    public sealed record SoundDef(string Name, uint Config, List<Wave> Waves, int Start, int End);
    public readonly List<Bank> Banks = [];
    public readonly List<Event> Events = [];
    public readonly List<SoundDef> SoundDefs = [];
    int bankCountAt, banksEnd, eventCountAt, cfgAt, sdAt, sdEnd;
    public int ConfigCount { get; private set; }

    Fev1Project(byte[] b) { Bytes = b; }

    static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    static string Lstr(byte[] b, ref int o)
    {
        int n = (int)U32(b, o);
        string s = Encoding.Latin1.GetString(b, o + 4, Math.Max(0, n - 1));
        o += 4 + n;
        return s;
    }

    static bool IsEventStart(byte[] b, int o)
    {
        if (o + 19 > b.Length || U32(b, o) != 8 || U32(b, o + 4) != 11 || b[o + 18] != 0) return false;
        if (b[o + 8] < 'a' || b[o + 8] > 'z') return false;
        for (int k = 9; k < 18; k++) if (b[o + k] < '0' || b[o + k] > '9') return false;
        return true;
    }

    public static Fev1Project Read(byte[] b)
    {
        if (b.Length < 0x20 || Encoding.ASCII.GetString(b, 0, 4) != "FEV1") throw new InvalidDataException("not an FEV1 project");
        var p = new Fev1Project(b);
        int o = 0x10;
        p.Name = Lstr(b, ref o);
        p.bankCountAt = o;
        int nb = (int)U32(b, o); o += 4;
        for (int i = 0; i < nb; i++) { int s = o; o += 16; string n = Lstr(b, ref o); p.Banks.Add(new Bank(n, s, o)); }
        p.banksEnd = o;
        void Category() { Lstr(b, ref o); int nsub = (int)U32(b, o + 16); o += 20; for (int i = 0; i < nsub; i++) Category(); }
        Category();
        if (U32(b, o) != 1) throw new InvalidDataException($"{p.Name}: {U32(b, o)} event groups (one expected)");
        o += 4;
        Lstr(b, ref o);
        if (U32(b, o) != 0 || U32(b, o + 4) != 0) throw new InvalidDataException($"{p.Name}: group with user properties / subgroups");
        p.eventCountAt = o + 8;
        int ne = (int)U32(b, p.eventCountAt);
        int first = p.eventCountAt + 4;
        // the sound definition table: the count before the first printable lstr starting with '/' that has the config table
        // (u32 n + n 64-byte records) right before it
        p.sdAt = p.cfgAt = -1;
        for (int x = first; x + 9 < b.Length && p.sdAt < 0; x++)
        {
            if (b[x + 8] != '/' || U32(b, x) == 0) continue;
            int n = (int)U32(b, x + 4);
            if (n < 3 || x + 8 + n > b.Length || b[x + 7 + n] != 0) continue;
            bool printable = true;
            for (int k = x + 8; k < x + 7 + n && printable; k++) printable = b[k] >= 0x20 && b[k] < 0x7f;
            if (!printable) continue;
            for (int c = 1; c < 8192 && x - 4 - 64 * c >= first; c++)
                if (U32(b, x - 4 - 64 * c) == c) { p.sdAt = x; p.cfgAt = x - 4 - 64 * c; p.ConfigCount = c; break; }
        }
        if (p.sdAt < 0) throw new InvalidDataException($"{p.Name}: sound definition table / config table not found");
        // events: sequential, each starting with u32 8 + an 11-byte name lstr; the last one ends at the config table
        var starts = new List<int>();
        for (int x = first; x < p.cfgAt; x++) if (IsEventStart(b, x)) starts.Add(x);
        if (starts.Count != ne || starts[0] != first) throw new InvalidDataException($"{p.Name}: {starts.Count} event starts for {ne} events");
        for (int i = 0; i < ne; i++)
        {
            int s = starts[i], e = i + 1 < ne ? starts[i + 1] : p.cfgAt;
            p.Events.Add(new Event(Encoding.ASCII.GetString(b, s + 8, 10), s, e));
        }
        int nsd = (int)U32(b, p.sdAt);
        o = p.sdAt + 4;
        for (int i = 0; i < nsd; i++)
        {
            int s = o;
            string n = Lstr(b, ref o);
            uint cfg = U32(b, o), nw = U32(b, o + 4); o += 8;
            var waves = new List<Wave>();
            for (int w = 0; w < nw; w++)
            {
                uint type = U32(b, o), weight = U32(b, o + 4); o += 8;
                if (type != 0) throw new InvalidDataException($"{p.Name}: waveform type {type} in {n}");
                string file = Lstr(b, ref o), bank = Lstr(b, ref o);
                waves.Add(new Wave(type, weight, file, bank, U32(b, o), U32(b, o + 4))); o += 8;
            }
            p.SoundDefs.Add(new SoundDef(n, cfg, waves, s, o));
        }
        p.sdEnd = o;
        // what follows the definitions in every DS1 project: u32 0, then the music data chunk "comp"
        if (o + 12 > b.Length || U32(b, o) != 0 || Encoding.ASCII.GetString(b, o + 8, 4) != "comp") throw new InvalidDataException($"{p.Name}: unexpected data after the sound definitions at 0x{o:X}");
        return p;
    }

    public (uint A, uint B) Header => (U32(Bytes, 8), U32(Bytes, 12));

    /// <summary>
    /// A copy of a project with another project name (the header lstr at 0x10; fix pass, rule S1b). Nothing else in an
    /// FEV1 file refers to the project name (the file is one sequential stream without absolute offsets, and A/B count only
    /// sound definitions), so the rest is kept byte for byte. FMOD reports the new name through
    /// EventProject::getInfo, and DS2 registers a loaded project under that name (decomp FUN_1409df930 → getInfo →
    /// FUN_1409e5d30 → FUN_1409dc1c0), so the name must be the bank's resource name for DS2's load de-duplication and
    /// release by name to find it (validate\fmod_play.py checks getInfo; validate\fmod_reload.py the de-duplication).
    /// </summary>
    public static byte[] WithName(byte[] fev, string name)
    {
        var p = Read(fev);
        if (name.Length == 0 || name.Any(c => c < 0x20 || c > 0x7e)) throw new InvalidDataException($"FEV1 project name '{name}'");
        int oldLen = (int)U32(fev, 0x10);
        var o = new byte[fev.Length - oldLen + name.Length + 1];
        fev.AsSpan(0, 0x10).CopyTo(o);
        BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(0x10), (uint)(name.Length + 1));
        Encoding.ASCII.GetBytes(name).CopyTo(o, 0x14);
        fev.AsSpan(0x14 + oldLen).CopyTo(o.AsSpan(0x14 + name.Length + 1));
        var q = Read(o);
        if (q.Name != name || q.Events.Count != p.Events.Count || q.SoundDefs.Count != p.SoundDefs.Count || q.Header != p.Header)
            throw new InvalidDataException($"{p.Name} → {name}: renamed project inconsistent");
        return o;
    }

    /// <summary>A and B as the formula gives them for the sound definitions (class remarks).</summary>
    public static (uint A, uint B) Counters(IEnumerable<SoundDef> sds)
    {
        uint a = 0, w = 0;
        foreach (var s in sds) { a += (uint)((s.Name.Length + 1 + 3) / 4 * 4); w += (uint)s.Waves.Count; }
        return (a + 24 * w, a + 32 * w);
    }

    /// <summary>The event's sound definition (simple events: one layer, one sound instance), or -1.</summary>
    public int SoundDefOf(Event e)
    {
        int n = e.NameAt;
        if (n + 0xb3 > e.End || U32(Bytes, n + 0xa3) != 1 || BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(n + 0xad)) != 1) return -1;
        return BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(n + 0xb1));
    }

    /// <summary>
    /// Rule S5c (chrint stage 4): the sound definition fields of every layer of an event, as absolute offsets of the u16
    /// index in <see cref="Bytes"/>; null when the event is not of the layered kind this rule reads. An event's layers
    /// follow each other (u32 layer count at name+0xa3, layer 1 at name+0xa7); a layer = u16 flags 2, i16 priority -1,
    /// u16 parameter, u16 sound instances (1), u16 envelopes, then the instance's u16 sound definition index, then the
    /// instance and envelope data (variable: DSP names). The header is found by scanning the event's bytes; the rule
    /// accepts the event only when the scan finds exactly as many headers as the layer count and the first one at
    /// name+0xa7 (so a simple event's index is at name+0xb1, as <see cref="SoundDefOf"/>). Evidence (work\c2900hit\
    /// layer_rule.py over all 253 DSR frpg_*.fev projects): the scan finds exactly one header in each of the 8,585 simple events
    /// (no false positive), and accepts 915 of 916 multi-layer events (frpg_c2410 z003011090 rejected); DS1 c2900's
    /// skeleton hit c290099999 = 2 layers, /frpg_main/bone_damage1 + /frpg_main/iron-knock-stone fragmentB.
    /// </summary>
    public List<int>? LayerSoundDefOffsets(Event e)
    {
        int n = e.NameAt;
        if (n + 0xb3 > e.End) return null;
        uint layers = U32(Bytes, n + 0xa3);
        if (layers is < 1 or > 16) return null;
        static ushort U16(byte[] b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o));
        var hits = new List<int>();
        for (int p = n + 0xa7; p + 12 <= e.End; p++)
            if (U16(Bytes, p) == 2 && U16(Bytes, p + 2) == 0xFFFF && U16(Bytes, p + 6) == 1 && U16(Bytes, p + 8) <= 8 && U16(Bytes, p + 10) < SoundDefs.Count)
                hits.Add(p + 10);
        return hits.Count == layers && hits[0] == n + 0xb1 ? hits : null;
    }

    public Event? EventByName(string name) => Events.FirstOrDefault(e => e.Name == name);

    /// <summary>
    /// Rule FH2 (chrint stage-4 fix pass, least loss for the FMOD heap): a copy of this project holding only the events
    /// named in <paramref name="keep"/> (original order), the sound definitions they play (original order, indices
    /// re-pointed in every kept event: simple events' one index or every layer's, rule S5c) and the definition configs
    /// those use (original order, re-pointed); banks, categories, the group header (event count rewritten) and the data
    /// after the definitions stay byte for byte; A/B recomputed. Null when a kept event is neither simple nor S5c-layered
    /// (its indices cannot be re-pointed; the caller keeps the whole project). DS2 asks FMOD for an event only by name
    /// (validate\fmod_heap_worst.py, fmod_strip_check.py), so dropping events no DS2 name can reach loses nothing.
    /// </summary>
    public byte[]? KeepEvents(IReadOnlySet<string> keep)
    {
        var kept = Events.Where(e => keep.Contains(e.Name)).ToList();
        if (kept.Count == 0) return null;
        var offsets = new List<List<int>>();
        foreach (var e in kept)
        {
            List<int>? offs = SoundDefOf(e) >= 0 ? [e.NameAt + 0xb1] : LayerSoundDefOffsets(e);
            if (offs == null) return null;
            offsets.Add(offs);
        }
        var usedSd = new SortedSet<int>(offsets.SelectMany(l => l).Select(off => (int)BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(off))));
        if (usedSd.Count > 0 && usedSd.Max >= SoundDefs.Count) throw new InvalidDataException($"{Name}: sound definition index out of range");
        var sdMap = new Dictionary<int, int>();
        foreach (int s in usedSd) sdMap[s] = sdMap.Count;
        var usedCfg = new SortedSet<uint>(usedSd.Select(s => SoundDefs[s].Config));
        if (usedCfg.Count > 0 && usedCfg.Max >= ConfigCount) throw new InvalidDataException($"{Name}: config index out of range");
        var cfgMap = new Dictionary<uint, int>();
        foreach (uint c in usedCfg) cfgMap[c] = cfgMap.Count;

        var o = new List<byte>(Bytes.Length);
        void Put(ReadOnlySpan<byte> s) { foreach (byte x in s) o.Add(x); }
        void PutU32(uint v) { Span<byte> s = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(s, v); Put(s); }
        Put(Bytes.AsSpan(0, eventCountAt));
        PutU32((uint)kept.Count);
        Put(Bytes.AsSpan(eventCountAt + 4, Events[0].Start - eventCountAt - 4));
        for (int i = 0; i < kept.Count; i++)
        {
            var e = kept[i];
            var blob = Bytes.AsSpan(e.Start, e.End - e.Start).ToArray();
            foreach (int off in offsets[i])
                BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(off - e.Start), (ushort)sdMap[BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(off))]);
            Put(blob);
        }
        PutU32((uint)usedCfg.Count);
        foreach (uint c in usedCfg) Put(Bytes.AsSpan(cfgAt + 4 + 64 * (int)c, 64));
        Put(Bytes.AsSpan(cfgAt + 4 + 64 * ConfigCount, sdAt - (cfgAt + 4 + 64 * ConfigCount)));
        PutU32((uint)usedSd.Count);
        foreach (int s in usedSd)
        {
            var sd = SoundDefs[s];
            var bl = Bytes.AsSpan(sd.Start, sd.End - sd.Start).ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(bl.AsSpan(4 + sd.Name.Length + 1), (uint)cfgMap[sd.Config]);
            Put(bl);
        }
        Put(Bytes.AsSpan(sdEnd));
        byte[] r = [.. o];
        var (a, b2) = Counters(usedSd.Select(s => SoundDefs[s]));
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(8), a);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(12), b2);
        // read back: same kept events in order, each playing the same definitions (by name) as before
        var q = Read(r);
        if (q.Events.Count != kept.Count || q.SoundDefs.Count != usedSd.Count || q.ConfigCount != usedCfg.Count)
            throw new InvalidDataException($"{Name}: stripped project inconsistent");
        for (int i = 0; i < kept.Count; i++)
        {
            var qe = q.Events[i];
            List<int>? qo = q.SoundDefOf(qe) >= 0 ? [qe.NameAt + 0xb1] : q.LayerSoundDefOffsets(qe);
            if (qe.Name != kept[i].Name || qo == null || qo.Count != offsets[i].Count) throw new InvalidDataException($"{Name}: stripped event {kept[i].Name} inconsistent");
            for (int k = 0; k < qo.Count; k++)
            {
                var before = SoundDefs[BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(offsets[i][k]))];
                var after = q.SoundDefs[BinaryPrimitives.ReadUInt16LittleEndian(q.Bytes.AsSpan(qo[k]))];
                if (before.Name != after.Name || !before.Waves.SequenceEqual(after.Waves)) throw new InvalidDataException($"{Name}: stripped event {kept[i].Name} plays {after.Name}, was {before.Name}");
            }
        }
        return r;
    }

    /// <summary>
    /// A copy of this project with events of <paramref name="src"/> added under new names (each pair: source event name,
    /// new 10-character name). Each copied event keeps its bytes (properties, envelopes, category) with the new name, a GUID
    /// derived from the new name (MD5) and its sound definition index pointing at the copied definition; the definitions
    /// keep their waveform lists (bank by name) with their config copied; banks they name that this project lacks are added
    /// from <paramref name="src"/>'s list (same hash); A/B are recomputed. Source events must be simple (SoundDefOf ≥ 0)
    /// or layered (rule S5c, <see cref="LayerSoundDefOffsets"/>: every layer's index re-pointed; chrint stage 4).
    /// </summary>
    public byte[] AddEvents(Fev1Project src, IReadOnlyList<(string Source, string NewName)> add)
    {
        if (add.Count == 0) return (byte[])Bytes.Clone();
        var have = new HashSet<string>(Events.Select(e => e.Name));
        var sdMap = new Dictionary<int, int>(); var cfgMap = new Dictionary<uint, int>();
        var newSds = new List<(SoundDef Def, int Config)>(); var newCfgs = new List<uint>(); var newBanks = new List<Bank>();
        var blobs = new List<byte[]>();
        foreach (var (srcName, newName) in add)
        {
            if (newName.Length != 10 || have.Contains(newName)) throw new InvalidDataException($"{Name}: new event name '{newName}' (10 characters, unused)");
            have.Add(newName);
            var e = src.EventByName(srcName) ?? throw new InvalidDataException($"{src.Name}: no event {srcName}");
            // simple events: the one index at name+0xb1; layered events (rule S5c): one index per layer
            int simple = src.SoundDefOf(e);
            List<int> offs = simple >= 0 ? [e.NameAt + 0xb1] : src.LayerSoundDefOffsets(e) ?? throw new InvalidDataException($"{src.Name} {srcName}: neither a simple nor a layered event (rule S5c)");
            var blob = src.Bytes.AsSpan(e.Start, e.End - e.Start).ToArray();
            foreach (int off in offs)
            {
                int sdi = BinaryPrimitives.ReadUInt16LittleEndian(src.Bytes.AsSpan(off));
                if (sdi >= src.SoundDefs.Count) throw new InvalidDataException($"{src.Name} {srcName}: sound definition {sdi} out of range");
                if (!sdMap.TryGetValue(sdi, out int ni))
                {
                    var sd = src.SoundDefs[sdi];
                    if (!cfgMap.TryGetValue(sd.Config, out int nc)) { nc = ConfigCount + newCfgs.Count; cfgMap[sd.Config] = nc; newCfgs.Add(sd.Config); }
                    ni = SoundDefs.Count + newSds.Count; sdMap[sdi] = ni; newSds.Add((sd, nc));
                    foreach (var w in sd.Waves)
                        if (!Banks.Any(x => x.Name == w.Bank) && !newBanks.Any(x => x.Name == w.Bank))
                            newBanks.Add(src.Banks.FirstOrDefault(x => x.Name == w.Bank) ?? throw new InvalidDataException($"{src.Name}: bank {w.Bank} not listed"));
                }
                BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(off - e.Start), (ushort)ni);
            }
            Encoding.ASCII.GetBytes(newName).CopyTo(blob, 8);
            MD5.HashData(Encoding.ASCII.GetBytes("lordran fev1 event " + newName)).CopyTo(blob, 19);
            blobs.Add(blob);
        }
        var o = new List<byte>(Bytes.Length + 4096);
        void Put(ReadOnlySpan<byte> s) { foreach (byte x in s) o.Add(x); }
        void PutU32(uint v) { Span<byte> s = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(s, v); Put(s); }
        Put(Bytes.AsSpan(0, bankCountAt));
        PutU32((uint)(Banks.Count + newBanks.Count));
        Put(Bytes.AsSpan(bankCountAt + 4, banksEnd - bankCountAt - 4));
        foreach (var bk in newBanks) Put(src.Bytes.AsSpan(bk.Start, bk.End - bk.Start));
        Put(Bytes.AsSpan(banksEnd, eventCountAt - banksEnd));
        PutU32((uint)(Events.Count + blobs.Count));
        Put(Bytes.AsSpan(eventCountAt + 4, cfgAt - eventCountAt - 4));
        foreach (var bl in blobs) Put(bl);
        PutU32((uint)(ConfigCount + newCfgs.Count));
        Put(Bytes.AsSpan(cfgAt + 4, sdAt - cfgAt - 4));
        foreach (uint c in newCfgs) Put(src.Bytes.AsSpan(src.cfgAt + 4 + 64 * (int)c, 64));
        PutU32((uint)(SoundDefs.Count + newSds.Count));
        Put(Bytes.AsSpan(sdAt + 4, sdEnd - sdAt - 4));
        foreach (var (sd, nc) in newSds)
        {
            var bl = src.Bytes.AsSpan(sd.Start, sd.End - sd.Start).ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(bl.AsSpan(4 + sd.Name.Length + 1), (uint)nc);
            Put(bl);
        }
        Put(Bytes.AsSpan(sdEnd));
        byte[] r = [.. o];
        var (a, b2) = Counters(SoundDefs.Concat(newSds.Select(x => x.Def)));
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(8), a);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(12), b2);
        return r;
    }
}
