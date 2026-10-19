using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// param-roundtrip [Name+Name...] — reads vanilla DS2 regulation params and writes them back unchanged, reporting
/// whether SoulsFormats reproduces the original bytes (and where the first difference is if not).
/// </summary>
public static class ParamRoundTrip
{
    public static int Run(string[] args)
    {
        var names = args.Length > 1 ? args[1].Split('+').ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        var bnd = BND4.Read(Ds2RegulationCipher.Decrypt(File.ReadAllBytes(Path.Combine(Program.DefaultDs2Root, "enc_regulation.bnd.dcx"))));
        var defs = Directory.GetFiles(Path.Combine(ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        int same = 0, differ = 0;
        foreach (var file in bnd.Files)
        {
            string name = Path.GetFileNameWithoutExtension(file.Name);
            if (names != null && !names.Contains(name)) continue;
            PARAM param;
            try { param = PARAM.Read(file.Bytes); } catch { continue; }
            if (!param.ApplyParamdefCarefully(defs)) continue;
            byte[] written = param.Write();
            int first = Enumerable.Range(0, Math.Min(written.Length, file.Bytes.Length)).FirstOrDefault(i => written[i] != file.Bytes[i], -1);
            bool equal = written.Length == file.Bytes.Length && first < 0;
            if (equal) { same++; continue; }
            differ++;
            if (names != null || differ <= 15)
            {
                Console.WriteLine($"{name}: original {file.Bytes.Length} bytes, written {written.Length}; first diff at 0x{(first < 0 ? Math.Min(written.Length, file.Bytes.Length) : first):X}");
                int at = Math.Max(0, (first < 0 ? Math.Min(written.Length, file.Bytes.Length) : first) - 8);
                Console.WriteLine($"  orig: {Convert.ToHexString(file.Bytes.AsSpan(at, Math.Min(48, file.Bytes.Length - at)))}");
                Console.WriteLine($"  ours: {Convert.ToHexString(written.AsSpan(at, Math.Min(48, written.Length - at)))}");
                Console.WriteLine($"  header orig: {Convert.ToHexString(file.Bytes.AsSpan(0, 0x40))}");
                Console.WriteLine($"  header ours: {Convert.ToHexString(written.AsSpan(0, 0x40))}");
            }
        }
        Console.WriteLine($"round-trip identical: {same}, different: {differ}");
        return 0;
    }
}
