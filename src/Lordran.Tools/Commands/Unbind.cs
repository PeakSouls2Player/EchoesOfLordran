using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// unbind FILE OUTDIR [filter] — extracts a binder (BND3/BND4, or a split BXF3/BXF4 given its *bhd) into OUTDIR,
/// DCX-decompressing each file. Optional filter = substring of the file name.
/// </summary>
public static class Unbind
{
    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: unbind FILE OUTDIR [filter]"); return 1; }
        string path = args[1], outDir = args[2];
        string? filter = args.Length > 3 ? args[3] : null;
        IBinder binder = Open(path);
        Directory.CreateDirectory(outDir);
        int n = 0;
        foreach (var f in binder.Files)
        {
            if (filter != null && !f.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            byte[] bytes = DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes;
            string name = Path.GetFileName(f.Name.Replace('\\', '/'));
            if (name.EndsWith(".dcx", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            File.WriteAllBytes(Path.Combine(outDir, name), bytes);
            n++;
        }
        Console.WriteLine($"{n} files from {Path.GetFileName(path)} → {outDir}");
        return 0;
    }

    public static IBinder Open(string path)
    {
        if (path.EndsWith("bhd", StringComparison.OrdinalIgnoreCase))
        {
            string bdt = path[..^3] + "bdt";
            byte[] head = File.ReadAllBytes(path);
            return head[3] == (byte)'4' ? BXF4.Read(path, bdt) : BXF3.Read(path, bdt);
        }
        byte[] bytes = File.ReadAllBytes(path);
        if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
        return BND4.Is(bytes) ? BND4.Read(bytes) : BND3.Read(bytes);
    }
}
