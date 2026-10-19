using System.Security.Cryptography;
using System.Text;
using SoulsFormats;
using SoulsFormats.Cryptography;

namespace Lordran.Tools.Commands;

/// <summary>
/// save-dev SL2 SLOT [--expect-name NAME] [edits --out OUT.sl2]   (session 11, track devkit11: the dev / test character)
/// Shows (no edits) or edits ONE character of a DS2 SOTFS save. SLOT = the Data List slot 1-10. Edits, all optional:
///   --name NAME         character name (character data + both Data List summaries, UTF-16, at most 13 characters)
///   --max-stats         all 9 attributes 99, soul level 838 (DS2: SL = sum of attributes − 53, for every class)
///   --souls N           souls carried; soul memory (both counters) raised to at least N + its old value (souls ≤ memory)
///   --light-all [ID+ID] every bonfire of the slot's table lit (level ≥ 1), plus these ids inserted (sorted) if missing
///   --flags A-B+C       event flags set (global blocks 10 / 20 only: 100000..109999, 200000..209999)
/// The input is never modified; OUT is the input with only the edited entries' encrypted bytes replaced in place (same
/// length; MD5(IV + ciphertext) + IV + AES-128-CBC, as save-bonfire), then read back and checked (round trip, MD5s, every
/// unedited entry byte-identical).
///
/// Layout (decoded by diffing saves; session 4 + 9 + this track):
///  - USER_DATA0(SLOT)       character data: +0x24 u16 attributes[9] (VGR END VIT ATN STR DEX ADP INT FTH), +0x3C u32 soul
///                           level, +0x40 u32 souls, +0x44 / +0x48 u32 soul memory (≥ souls; the larger copy = total),
///                           +0x3C4 UTF-16 name (0-terminated);
///  - USER_DATA000 / USER_DATA022  Data List summaries (two copies), record SLOT−1 at 0x4D4 / 0x188 + 0x1F0·(SLOT−1):
///                           +0x20 attributes[9], +0x36 UTF-16 name, +0x78 u32 play time, +0x7C u32 map, +0x80 u16 soul level;
///  - USER_DATA0(10+SLOT)    world data: flag section at +0x26D34 (global block 10 at +0, block 20 at +0x4E4; flag F →
///                           byte (F % 10000) >> 3, mask 0x80 >> (F & 7)), bonfire table at +0x282CC (see save-bonfire).
/// </summary>
public static class SaveDev
{
    const int StatsOff = 0x24, SlOff = 0x3C, SoulsOff = 0x40, Memory1Off = 0x44, Memory2Off = 0x48, NameOff = 0x3C4;
    const int SumStride = 0x1F0, SumStats = 0x20, SumName = 0x36, SumSl = 0x80, MaxName = 13;
    const int FlagSection = 0x26D34, Block20 = 0x4E4, BlockBytes = 1250;
    const int BonfireTable = 0x282CC, BonfireLevels = BonfireTable + 0x200, BonfireCap = 256;
    static readonly (string Entry, int Base)[] Summaries = [("USER_DATA000", 0x4D4), ("USER_DATA022", 0x188)];
    static readonly string[] StatNames = ["VGR", "END", "VIT", "ATN", "STR", "DEX", "ADP", "INT", "FTH"];
    /// <summary>The Lordran bonfires of the full build (MapObjectBonfireParam ids; BF1): Asylum ×2 (travel12 BF1-A: 48155), Firelink, Burg ×3, Depths, Blighttown ×3.</summary>
    public static readonly int[] LordranBonfires = [48150, 48155, 40250, 40150, 40155, 40160, 40050, 40055, 40060, 40065];

    sealed class Slot
    {
        public required byte[] Chr, World;
        public required Dictionary<string, byte[]> Sums;
    }

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: save-dev SL2 SLOT [--expect-name NAME] [--name NAME] [--max-stats] [--souls N] [--light-all [ID+ID]] [--flags A-B+C] [--show-flags A-B] [--out OUT.sl2]"); return 1; }
        string path = args[1];
        int slot = int.Parse(args[2]);
        if (slot is < 1 or > 10) { Console.Error.WriteLine("SLOT is the Data List slot, 1-10"); return 1; }
        string? expect = null, name = null, outPath = null;
        bool maxStats = false, light = false;
        long souls = -1;
        var addBonfires = new List<int>();
        var flags = new List<int>();
        var showFlags = new List<int>(); // --show-flags: displayed only
        for (int i = 3; i < args.Length; i++)
            switch (args[i])
            {
                case "--expect-name": expect = args[++i]; break;
                case "--name": name = args[++i]; break;
                case "--max-stats": maxStats = true; break;
                case "--souls": souls = long.Parse(args[++i]); break;
                case "--light-all":
                    light = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) addBonfires.AddRange(args[++i].Split('+').Select(int.Parse));
                    break;
                case "--flags" or "--show-flags":
                    var into = args[i] == "--flags" ? flags : showFlags;
                    foreach (var part in args[++i].Split('+', ','))
                    {
                        var ab = part.Split('-');
                        int a = int.Parse(ab[0]), b = ab.Length > 1 ? int.Parse(ab[1]) : a;
                        for (int f = a; f <= b; f++) into.Add(f);
                    }
                    break;
                case "--out": outPath = args[++i]; break;
                default: Console.Error.WriteLine($"unknown option {args[i]}"); return 1;
            }
        bool edits = name != null || maxStats || souls >= 0 || light || flags.Count > 0;
        if (edits && outPath == null) { Console.Error.WriteLine("edits need --out"); return 1; }
        if (name != null && (name.Length is 0 or > MaxName || name.Any(char.IsControl))) { Console.Error.WriteLine($"name must be 1-{MaxName} printable characters"); return 1; }
        if (souls > 999_999_999) { Console.Error.WriteLine("DS2 caps souls at 999,999,999"); return 1; }
        if (flags.Concat(showFlags).Any(f => f / 10000 is not (10 or 20))) { Console.Error.WriteLine("--flags: only global blocks 10 / 20 (100000..109999, 200000..209999) are in the save's flag section"); return 1; }
        if (outPath != null && Path.GetFullPath(outPath).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) { Console.Error.WriteLine("--out must differ from the input (the input is never modified)"); return 1; }

        byte[] file = File.ReadAllBytes(path);
        var (s, err) = Load(file, slot);
        if (s == null) { Console.Error.WriteLine(err); return 1; }
        string current = ReadName(s.Chr, NameOff);
        if (expect != null && current != expect) { Console.Error.WriteLine($"slot {slot} is \"{current}\", not \"{expect}\"; refusing to continue"); return 1; }
        Show($"{Path.GetFileName(path)} slot {slot}", s, slot, [.. flags, .. showFlags]);
        if (!edits) return 0;

        // ---- edits
        if (name != null)
        {
            WriteName(s.Chr, NameOff, current, name);
            foreach (var (entry, b) in Summaries) WriteName(s.Sums[entry], b + SumStride * (slot - 1) + SumName, current, name);
        }
        if (maxStats)
        {
            for (int k = 0; k < 9; k++)
            {
                BitConverter.TryWriteBytes(s.Chr.AsSpan(StatsOff + 2 * k), (ushort)99);
                foreach (var (entry, b) in Summaries) BitConverter.TryWriteBytes(s.Sums[entry].AsSpan(b + SumStride * (slot - 1) + SumStats + 2 * k), (ushort)99);
            }
            int sl = 99 * 9 - 53;
            BitConverter.TryWriteBytes(s.Chr.AsSpan(SlOff), sl);
            foreach (var (entry, b) in Summaries) BitConverter.TryWriteBytes(s.Sums[entry].AsSpan(b + SumStride * (slot - 1) + SumSl), (ushort)sl);
        }
        if (souls >= 0)
        {
            uint m1 = U32(s.Chr, Memory1Off), m2 = U32(s.Chr, Memory2Off);
            BitConverter.TryWriteBytes(s.Chr.AsSpan(SoulsOff), (uint)souls);
            BitConverter.TryWriteBytes(s.Chr.AsSpan(Memory1Off), (uint)Math.Min(999_999_999L, Math.Max(m1, souls + m1)));
            BitConverter.TryWriteBytes(s.Chr.AsSpan(Memory2Off), (uint)Math.Min(999_999_999L, Math.Max(m2, souls + m2)));
        }
        if (light)
        {
            var (ids, levels) = Bonfires(s.World) ?? throw new InvalidDataException("no bonfire table");
            foreach (int id in addBonfires.Distinct())
            {
                if (id is <= 0 or > ushort.MaxValue) { Console.Error.WriteLine($"bad bonfire id {id}"); return 1; }
                int at = ids.BinarySearch((ushort)id);
                if (at >= 0) continue;
                if (ids.Count >= BonfireCap) { Console.Error.WriteLine("bonfire table full"); return 1; }
                ids.Insert(~at, (ushort)id);
                levels.Insert(~at, 0);
            }
            for (int i = 0; i < ids.Count; i++) if (levels[i] == 0) levels[i] = 1;
            for (int i = 0; i < BonfireCap; i++)
            {
                BitConverter.TryWriteBytes(s.World.AsSpan(BonfireTable + 2 * i), i < ids.Count ? ids[i] : (ushort)0);
                s.World[BonfireLevels + i] = i < levels.Count ? levels[i] : (byte)0;
            }
        }
        foreach (int f in flags)
        {
            var (at, mask) = FlagBit(f);
            s.World[at] |= mask;
        }

        // ---- write back: only the edited entries' encrypted bytes change
        var bnd = BND4.Read(file);
        var key = SL2Decryptor.GetScholarSaveKey();
        var plainByName = new Dictionary<string, byte[]> { [Entry(slot)] = s.Chr, [Entry(10 + slot)] = s.World };
        foreach (var kv in s.Sums) plainByName[kv.Key] = kv.Value;
        byte[] output = (byte[])file.Clone();
        var changed = new List<string>();
        foreach (var (entryName, plain) in plainByName)
        {
            var e = bnd.Files.First(f => f.Name == entryName);
            byte[] enc = e.Bytes;
            if (SL2Decryptor.DecryptSL2File(enc, key).AsSpan().SequenceEqual(plain)) continue; // nothing changed in it
            byte[] newEnc = SL2Decryptor.EncryptSL2File(plain, key);
            if (newEnc.Length != enc.Length) { Console.Error.WriteLine($"{entryName}: re-encrypted size {newEnc.Length} != {enc.Length}"); return 1; }
            int pos = IndexOf(file, enc);
            if (pos < 0 || IndexOf(file, enc, pos + 1) >= 0) { Console.Error.WriteLine($"{entryName}: entry not found exactly once in the file"); return 1; }
            newEnc.CopyTo(output, pos);
            changed.Add($"{entryName}@0x{pos:X}");
        }
        File.WriteAllBytes(outPath!, output);
        Console.WriteLine($"wrote {outPath}: replaced {string.Join(", ", changed)}; everything else identical");

        // ---- read back
        int problems = Verify(file, output, slot, out var back);
        if (back == null) return 1;
        Show($"{Path.GetFileName(outPath)} slot {slot} (read back)", back, slot, [.. flags, .. showFlags]);
        if (name != null && ReadName(back.Chr, NameOff) != name) { Console.Error.WriteLine("PROBLEM name not written"); problems++; }
        foreach (var (entry, b) in Summaries)
            if (ReadName(back.Sums[entry], b + SumStride * (slot - 1) + SumName) != ReadName(back.Chr, NameOff)) { Console.Error.WriteLine($"PROBLEM {entry} summary name differs from the character's"); problems++; }
        foreach (int f in flags) if (!FlagOn(back.World, f)) { Console.Error.WriteLine($"PROBLEM flag {f} not set"); problems++; }
        Console.WriteLine($"save-dev check: {problems} problems");
        return problems == 0 ? 0 : 2;
    }

    /// <summary>Round trip: every entry's stored MD5 = MD5(IV + ciphertext); unedited entries byte-identical to the input; the slot re-reads.</summary>
    static int Verify(byte[] original, byte[] written, int slot, out Slot? back)
    {
        int problems = 0;
        var a = BND4.Read(original);
        var b = BND4.Read(written);
        var key = SL2Decryptor.GetScholarSaveKey();
        if (a.Files.Count != b.Files.Count) { Console.Error.WriteLine("PROBLEM entry count differs"); problems++; }
        int same = 0, edited = 0;
        foreach (var (x, y) in a.Files.Zip(b.Files))
        {
            if (x.Name != y.Name || x.Bytes.Length != y.Bytes.Length) { Console.Error.WriteLine($"PROBLEM entry {y.Name} renamed or resized"); problems++; continue; }
            if (!MD5.HashData(y.Bytes.AsSpan(16)).AsSpan().SequenceEqual(y.Bytes.AsSpan(0, 16))) { Console.Error.WriteLine($"PROBLEM {y.Name}: MD5 mismatch"); problems++; }
            if (x.Bytes.AsSpan().SequenceEqual(y.Bytes)) { same++; continue; }
            edited++;
            var p = SL2Decryptor.DecryptSL2File(y.Bytes, key);
            if (p.Length != SL2Decryptor.DecryptSL2File(x.Bytes, key).Length) { Console.Error.WriteLine($"PROBLEM {y.Name}: plain size changed"); problems++; }
        }
        // outside the entries, the file must be identical
        int outside = 0;
        for (int i = 0; i < original.Length; i++) if (original[i] != written[i]) outside++;
        Console.WriteLine($"read back: {b.Files.Count} entries, MD5 ok on all; {edited} re-encrypted, {same} byte-identical; {outside:N0} bytes differ in total");
        var (s, err) = Load(written, slot);
        if (s == null) { Console.Error.WriteLine("PROBLEM " + err); back = null; return problems + 1; }
        back = s;
        return problems;
    }

    static (Slot?, string) Load(byte[] file, int slot)
    {
        var bnd = BND4.Read(file);
        var key = SL2Decryptor.GetScholarSaveKey();
        byte[]? Get(string n)
        {
            var e = bnd.Files.FirstOrDefault(f => f.Name == n);
            if (e == null) return null;
            if (!MD5.HashData(e.Bytes.AsSpan(16)).AsSpan().SequenceEqual(e.Bytes.AsSpan(0, 16))) throw new InvalidDataException($"{n}: stored hash is not MD5(IV + ciphertext)");
            return SL2Decryptor.DecryptSL2File(e.Bytes, key);
        }
        var chr = Get(Entry(slot));
        var world = Get(Entry(10 + slot));
        if (chr == null || world == null) return (null, $"{Entry(slot)} / {Entry(10 + slot)} missing");
        if (world.Length < BonfireLevels + BonfireCap) return (null, $"{Entry(10 + slot)} is too small ({world.Length:N0} bytes): empty slot?");
        var sums = new Dictionary<string, byte[]>();
        string chrName = ReadName(chr, NameOff);
        if (chrName.Length == 0) return (null, $"slot {slot} has no character");
        foreach (var (n, b) in Summaries)
        {
            var d = Get(n);
            if (d == null) return (null, $"{n} missing");
            int r = b + SumStride * (slot - 1);
            if (ReadName(d, r + SumName) != chrName) return (null, $"{n}: summary record {slot - 1} is \"{ReadName(d, r + SumName)}\", the character \"{chrName}\": layout unknown, refusing to continue");
            for (int k = 0; k < 9; k++)
                if (BitConverter.ToUInt16(d, r + SumStats + 2 * k) != BitConverter.ToUInt16(chr, StatsOff + 2 * k))
                    return (null, $"{n}: summary attributes differ from the character's: layout unknown, refusing to continue");
            sums[n] = d;
        }
        int sum = Enumerable.Range(0, 9).Sum(k => BitConverter.ToUInt16(chr, StatsOff + 2 * k));
        if (BitConverter.ToInt32(chr, SlOff) != sum - 53) return (null, $"soul level {BitConverter.ToInt32(chr, SlOff)} != attributes − 53 ({sum - 53}): layout unknown, refusing to continue");
        if (U32(chr, SoulsOff) > U32(chr, Memory1Off) || U32(chr, SoulsOff) > U32(chr, Memory2Off)) return (null, "souls > soul memory: layout unknown, refusing to continue");
        if (Bonfires(world) == null) return (null, "no sorted bonfire table");
        return (new Slot { Chr = chr, World = world, Sums = sums }, "");
    }

    static void Show(string title, Slot s, int slot, List<int> flags)
    {
        var st = Enumerable.Range(0, 9).Select(k => BitConverter.ToUInt16(s.Chr, StatsOff + 2 * k)).ToArray();
        Console.WriteLine($"{title}: \"{ReadName(s.Chr, NameOff)}\" SL {BitConverter.ToInt32(s.Chr, SlOff)}  {string.Join(" ", StatNames.Zip(st, (n, v) => $"{n} {v}"))}");
        Console.WriteLine($"  souls {U32(s.Chr, SoulsOff):N0}, soul memory {U32(s.Chr, Memory1Off):N0} / {U32(s.Chr, Memory2Off):N0}");
        foreach (var (n, b) in Summaries)
        {
            int r = b + SumStride * (slot - 1);
            Console.WriteLine($"  Data List {n}: \"{ReadName(s.Sums[n], r + SumName)}\" SL {BitConverter.ToUInt16(s.Sums[n], r + SumSl)} attributes {string.Join(",", Enumerable.Range(0, 9).Select(k => BitConverter.ToUInt16(s.Sums[n], r + SumStats + 2 * k)))}");
        }
        var (ids, levels) = Bonfires(s.World)!.Value;
        int lit = levels.Count(l => l > 0);
        Console.WriteLine($"  bonfires: {ids.Count} in the table, {lit} lit; Lordran: {string.Join(" ", LordranBonfires.Select(id => { int i = ids.BinarySearch((ushort)id); return $"{id}:{(i < 0 ? "absent" : levels[i] > 0 ? "lit" : "unlit")}"; }))}");
        if (flags.Count > 0)
            Console.WriteLine($"  flags: {string.Join(" ", flags.Select(f => $"{f}={(FlagOn(s.World, f) ? 1 : 0)}"))}");
    }

    static (List<ushort> Ids, List<byte> Levels)? Bonfires(byte[] w)
    {
        var ids = new List<ushort>();
        for (int i = 0; i < BonfireCap; i++)
        {
            ushort id = BitConverter.ToUInt16(w, BonfireTable + 2 * i);
            if (id == 0) break;
            ids.Add(id);
        }
        if (ids.Count == 0 || ids.Zip(ids.Skip(1)).Any(p => p.First >= p.Second)) return null;
        if (w.AsSpan(BonfireLevels + ids.Count, BonfireCap - ids.Count).ContainsAnyExcept((byte)0)) return null;
        return (ids, w.AsSpan(BonfireLevels, ids.Count).ToArray().ToList());
    }

    static (int At, byte Mask) FlagBit(int f)
    {
        int bit = f % 10000;
        return (FlagSection + (f / 10000 == 20 ? Block20 : 0) + (bit >> 3), (byte)(0x80 >> (bit & 7)));
    }
    static bool FlagOn(byte[] w, int f) { var (at, m) = FlagBit(f); return (w[at] & m) != 0; }

    static string Entry(int n) => $"USER_DATA{n:D3}";
    static uint U32(byte[] b, int at) => BitConverter.ToUInt32(b, at);

    static string ReadName(byte[] b, int at)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 32; i++)
        {
            char c = (char)BitConverter.ToUInt16(b, at + 2 * i);
            if (c == 0) break;
            sb.Append(c);
        }
        return sb.ToString();
    }

    static void WriteName(byte[] b, int at, string old, string name)
    {
        // clear the old name (and its terminator) and the new one's span, then write it 0-terminated
        b.AsSpan(at, 2 * (Math.Max(old.Length, name.Length) + 1)).Clear();
        Encoding.Unicode.GetBytes(name).CopyTo(b, at);
    }

    static int IndexOf(byte[] hay, byte[] needle, int start = 0)
    {
        int i = hay.AsSpan(start).IndexOf(needle);
        return i < 0 ? -1 : start + i;
    }
}
