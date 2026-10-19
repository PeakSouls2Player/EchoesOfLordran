using System.Security.Cryptography;
using SoulsFormats;
using SoulsFormats.Cryptography;

namespace Lordran.Tools.Commands;

/// <summary>
/// save-bonfire SL2 SLOT [--set BONFIRE LEVEL --out OUT.sl2]
/// Lists (or sets) the bonfire table of one character (SLOT 0-9) in a DS2 SOTFS save.
///
/// Layout (decoded session 4 by diffing saves): the character's world data is USER_DATA0(10+SLOT); at +0x282CC it holds
/// u16 bonfireIds[256] (MapObjectBonfireParam row ids, sorted, zero-terminated) and at +0x284CC u8 level[256] in the same
/// order (0 = unlit, 1.. = bonfire intensity). DS2 rebuilds the id list from MapObjectBonfireParam on load and keeps a
/// bonfire only if its mapId is a registered map (map/WorldMapList/WorldMapList.list), carrying levels over by id: a
/// bonfire whose mapId is not registered is dropped together with its level.
///
/// --set inserts the bonfire (sorted) if missing and sets its level, then writes OUT.sl2: the original file with only
/// that entry's encrypted bytes replaced (same length; MD5(IV + ciphertext) + IV + AES-128-CBC, SoulsFormats' scheme,
/// checked against the original entry first). The input save is never modified.
/// </summary>
public static class SaveBonfire
{
    const int TableOffset = 0x282CC, LevelOffset = TableOffset + 0x200, Capacity = 256;

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: save-bonfire SL2 SLOT [--set BONFIRE LEVEL --out OUT.sl2]"); return 1; }
        string path = args[1];
        int slot = int.Parse(args[2]);
        int setId = -1, setLevel = -1;
        string? outPath = null;
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i] == "--set") { setId = int.Parse(args[++i]); setLevel = int.Parse(args[++i]); }
            else if (args[i] == "--out") outPath = args[++i];
        }
        if (slot is < 0 or > 9) { Console.Error.WriteLine("SLOT must be 0-9"); return 1; }

        byte[] file = File.ReadAllBytes(path);
        var bnd = BND4.Read(file);
        string name = $"USER_DATA{10 + slot:D3}";
        var entry = bnd.Files.FirstOrDefault(f => f.Name == name);
        if (entry == null) { Console.Error.WriteLine($"{name} not found"); return 1; }
        byte[] enc = entry.Bytes;
        if (!MD5.HashData(enc.AsSpan(16)).AsSpan().SequenceEqual(enc.AsSpan(0, 16)))
        {
            Console.Error.WriteLine($"{name}: stored hash is not MD5(IV + ciphertext); unknown format, refusing to continue");
            return 1;
        }
        byte[] key = SL2Decryptor.GetScholarSaveKey();
        byte[] plain = SL2Decryptor.DecryptSL2File(enc, key);
        if (plain.Length < LevelOffset + Capacity) { Console.Error.WriteLine($"{name} is too small ({plain.Length:N0} bytes): empty slot?"); return 1; }

        var ids = new List<ushort>();
        for (int i = 0; i < Capacity; i++)
        {
            ushort id = BitConverter.ToUInt16(plain, TableOffset + 2 * i);
            if (id == 0) break;
            ids.Add(id);
        }
        var levels = plain.AsSpan(LevelOffset, Capacity).ToArray();
        if (ids.Count == 0 || ids.Zip(ids.Skip(1)).Any(p => p.First >= p.Second))
        {
            Console.Error.WriteLine($"no sorted bonfire table at 0x{TableOffset:X} in {name}; refusing to continue");
            return 1;
        }
        if (levels.Skip(ids.Count).Any(b => b != 0))
        {
            Console.Error.WriteLine("levels set past the end of the id list; refusing to continue");
            return 1;
        }

        var names = BonfireNames();
        void Print(string title)
        {
            Console.WriteLine($"{title}: {ids.Count} bonfires, lit:");
            for (int i = 0; i < ids.Count; i++)
                if (levels[i] != 0)
                    Console.WriteLine($"  [{i,3}] {ids[i],6} level {levels[i]}  {names.GetValueOrDefault(ids[i], "")}");
        }
        Print($"{Path.GetFileName(path)} slot {slot}");
        if (setId < 0) return 0;
        if (outPath == null) { Console.Error.WriteLine("--set needs --out"); return 1; }
        if (setId is <= 0 or > ushort.MaxValue || setLevel is < 0 or > 255) { Console.Error.WriteLine("bad bonfire id or level"); return 1; }

        int at = ids.BinarySearch((ushort)setId);
        if (at < 0)
        {
            at = ~at;
            if (ids.Count >= Capacity) { Console.Error.WriteLine("table full"); return 1; }
            ids.Insert(at, (ushort)setId);
            var lv = levels.ToList();
            lv.Insert(at, 0);
            levels = lv.Take(Capacity).ToArray();
            Console.WriteLine($"inserted {setId} at index {at}");
        }
        levels[at] = (byte)setLevel;
        for (int i = 0; i < Capacity; i++)
            BitConverter.TryWriteBytes(plain.AsSpan(TableOffset + 2 * i), i < ids.Count ? ids[i] : (ushort)0);
        levels.CopyTo(plain.AsSpan(LevelOffset));
        Print("after");

        byte[] newEnc = SL2Decryptor.EncryptSL2File(plain, key);
        if (newEnc.Length != enc.Length) { Console.Error.WriteLine($"re-encrypted size {newEnc.Length} != {enc.Length}"); return 1; }
        if (!SL2Decryptor.DecryptSL2File(newEnc, key).AsSpan().SequenceEqual(plain)) { Console.Error.WriteLine("round trip failed"); return 1; }
        int pos = IndexOf(file, enc);
        if (pos < 0 || IndexOf(file, enc, pos + 1) >= 0) { Console.Error.WriteLine("entry not found exactly once in the file"); return 1; }
        byte[] output = (byte[])file.Clone();
        newEnc.CopyTo(output, pos);
        File.WriteAllBytes(outPath, output);
        Console.WriteLine($"wrote {outPath} ({name} at file offset 0x{pos:X} replaced, {enc.Length:N0} bytes; everything else identical)");
        return 0;
    }

    static int IndexOf(byte[] hay, byte[] needle, int start = 0)
    {
        int i = hay.AsSpan(start).IndexOf(needle);
        return i < 0 ? -1 : start + i;
    }

    static Dictionary<int, string> BonfireNames()
    {
        var d = new Dictionary<int, string>();
        string json = Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Param Row Names", "English", "MapObjectBonfireParam.json");
        if (!File.Exists(json)) return d;
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(json));
        foreach (var e in doc.RootElement.GetProperty("Entries").EnumerateArray())
            d[e.GetProperty("ID").GetInt32()] = string.Join(" ", e.GetProperty("Entries").EnumerateArray().Select(x => x.GetString()));
        return d;
    }
}
