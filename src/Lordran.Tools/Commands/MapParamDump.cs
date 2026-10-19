using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// map-param FILE [ROW...] — prints a per-map param file (e.g. data\ds2\param\eventparam_m10_02_00_00.param) with the
/// DS2S paramdefs: one line per row (id + every field), or only the given rows.
/// </summary>
public static class MapParamDump
{
    public static int Run(string[] args)
    {
        var defs = Directory.GetFiles(Path.Combine(ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        var p = PARAM.Read(args[1]);
        var only = args.Skip(2).Select(int.Parse).ToHashSet();
        bool fits;
        try { fits = p.ApplyParamdefCarefully(defs); } catch (InvalidDataException) { fits = false; p = PARAM.Read(args[1]); }
        if (!fits)
        {
            // No def fits (GeneratorLocation, some GeneratorParam rows): raw 4-byte words, as float when that looks sane.
            var blob = new PARAMDEF { ParamType = p.ParamType, DataVersion = p.ParamdefDataVersion, Unicode = true, FormatVersion = 104, BigEndian = false };
            blob.Fields.Add(new PARAMDEF.Field(blob, PARAMDEF.DefType.dummy8, "data") { ArrayLength = (int)p.DetectedSize });
            p.ApplyParamdef(blob);
            Console.WriteLine($"{p.ParamType}: {p.Rows.Count} rows, {p.DetectedSize} bytes each (no paramdef fits; raw words)");
            foreach (var row in p.Rows.Where(r => only.Count == 0 || only.Contains(r.ID)))
            {
                var b = (byte[])row["data"].Value;
                var words = Enumerable.Range(0, b.Length / 4).Select(i =>
                {
                    int x = BitConverter.ToInt32(b, i * 4); float f = BitConverter.ToSingle(b, i * 4);
                    return x != 0 && float.IsFinite(f) && Math.Abs(f) is > 1e-4f and < 1e6f && (x & 0xFF000000) != 0 ? f.ToString("0.###") + "f" : x.ToString();
                });
                Console.WriteLine($"{row.ID,10}: " + string.Join(" ", words));
            }
            return 0;
        }
        Console.WriteLine($"{p.ParamType}: {p.Rows.Count} rows");
        foreach (var row in p.Rows.Where(r => only.Count == 0 || only.Contains(r.ID)))
            Console.WriteLine($"{row.ID,10}: " + string.Join(" ", row.Cells.Where(c => !c.Def.InternalName.StartsWith("pad", StringComparison.OrdinalIgnoreCase) && !c.Def.InternalName.StartsWith("dummy", StringComparison.OrdinalIgnoreCase))
                .Select(c => $"{c.Def.InternalName}={Fmt(c.Value)}")));
        return 0;
    }

    static string Fmt(object v) => v switch
    {
        float f => f.ToString("0.###"),
        byte[] b => Convert.ToHexString(b),
        _ => v?.ToString() ?? "null",
    };
}
