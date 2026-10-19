using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// regulation-dump [dump.bnd] — checks the BND4 the running game decoded (written by the loader when loader.ini has
/// dump_regulation=1) against the pack's enc_regulation.bnd.dcx, then lists every param row that differs from vanilla.
/// </summary>
public static class RegulationDump
{
    public static int Run(string[] args)
    {
        string dumpPath = args.Length > 1 ? args[1] : Path.Combine(Program.DefaultDs2Root, "lordran", "regulation-loaded.bnd");
        byte[] dump = File.ReadAllBytes(dumpPath);
        byte[] pack = DCX.Decompress(Ds2RegulationCipher.Decrypt(File.ReadAllBytes(Path.Combine(Build.Pack.DefaultOut, "title", "enc_regulation.bnd.dcx"))));
        byte[] vanilla = DCX.Decompress(Ds2RegulationCipher.Decrypt(File.ReadAllBytes(Path.Combine(Program.DefaultDs2Root, "enc_regulation.bnd.dcx"))));
        bool same = dump.AsSpan().SequenceEqual(pack);
        Console.WriteLine($"game's decoded regulation == pack regulation: {same} (game {dump.Length:N0} bytes, pack {pack.Length:N0}, vanilla {vanilla.Length:N0})");

        var before = BND4.Read(vanilla).Files.ToDictionary(f => f.Name);
        foreach (var file in BND4.Read(dump).Files)
        {
            if (!before.TryGetValue(file.Name, out var old) || old.Bytes.AsSpan().SequenceEqual(file.Bytes))
                continue;
            string name = Path.GetFileNameWithoutExtension(file.Name);
            if (file.Name.EndsWith(".emevd", StringComparison.OrdinalIgnoreCase))
            {
                // Item special-effect scripts (SpEffectWeapon, …): one event per item.
                var was = EMEVD.Read(old.Bytes).Events.ToDictionary(e => e.ID);
                var now = EMEVD.Read(file.Bytes).Events;
                Console.WriteLine($"  {name}: {now.Count} events; added [{string.Join(", ", now.Where(e => !was.ContainsKey(e.ID)).Select(e => e.ID))}]");
                continue;
            }
            try
            {
                var a = RowData(old.Bytes);
                var b = RowData(file.Bytes);
                var added = b.Keys.Where(id => !a.ContainsKey(id)).ToList();
                var changed = b.Where(r => a.TryGetValue(r.Key, out var bytes) && !bytes.SequenceEqual(r.Value)).Select(r => r.Key).ToList();
                Console.WriteLine($"  {name}: {b.Count} rows; added [{string.Join(", ", added)}]; changed [{string.Join(", ", changed)}]");
            }
            catch (InvalidDataException)
            {
                Console.WriteLine($"  {name}: differs (not a param)");
            }
        }
        return same ? 0 : 1;
    }

    static readonly System.Reflection.FieldInfo DataOffset =
        typeof(PARAM.Row).GetField("DataOffset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    // Raw row bytes by ID, without needing a paramdef (SoulsFormats keeps the row offset internal).
    static Dictionary<int, byte[]> RowData(byte[] paramFile)
    {
        var param = PARAM.Read(paramFile);
        var result = new Dictionary<int, byte[]>();
        foreach (var row in param.Rows)
        {
            long offset = (long)DataOffset.GetValue(row)!;
            result[row.ID] = paramFile.AsSpan((int)offset, (int)Math.Max(0, param.DetectedSize)).ToArray();
        }
        return result;
    }
}
