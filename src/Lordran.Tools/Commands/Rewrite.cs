using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// rewrite IN OUT — reads IN with SoulsFormats and writes it back unchanged to OUT (MSB2 .msb, ESD .esd, per-map .param
/// with the DS2S paramdefs). For bisecting which rewritten file the game rejects (HANDOFF, session 5).
/// </summary>
public static class Rewrite
{
    public static int Run(string[] args)
    {
        string input = args[1], output = args[2];
        byte[] a = File.ReadAllBytes(input);
        byte[] b = Path.GetExtension(input).ToLowerInvariant() switch
        {
            ".msb" => MSB2.Read(a).Write(),
            ".esd" => ESD.Read(a).Write(),
            ".param" => Param(a),
            _ => throw new ArgumentException($"unsupported: {input}"),
        };
        File.WriteAllBytes(output, b);
        Console.WriteLine($"{Path.GetFileName(input)}: {a.Length:N0} → {b.Length:N0} bytes{(a.AsSpan().SequenceEqual(b) ? " (identical)" : "")} → {output}");
        return 0;
    }

    static byte[] Param(byte[] bytes)
    {
        var defs = Directory.GetFiles(Path.Combine(ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        var p = PARAM.Read(bytes);
        if (!p.ApplyParamdefCarefully(defs)) throw new InvalidDataException("no fitting paramdef");
        return p.Write();
    }
}
