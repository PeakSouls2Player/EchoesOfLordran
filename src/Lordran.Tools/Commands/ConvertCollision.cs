using Lordran.Tools.Build.Maps;
using Lordran.Tools.Formats.Havok;

namespace Lordran.Tools.Commands;

/// <summary>convert-collision DSR.hkx DS2-TEMPLATE.hkx OUT.hkx [NAME] — one DS1 collision file → DS2 (CollisionConverter).</summary>
public static class ConvertCollision
{
    public static int Run(string[] args)
    {
        if (args.Length < 4) { Console.Error.WriteLine("usage: convert-collision DSR.hkx DS2-TEMPLATE.hkx OUT.hkx [NAME]"); return 1; }
        byte[] src = File.ReadAllBytes(args[1]);
        if (SoulsFormats.DCX.Is(src)) src = SoulsFormats.DCX.Decompress(src);
        byte[] tpl = File.ReadAllBytes(args[2]);
        if (SoulsFormats.DCX.Is(tpl)) tpl = SoulsFormats.DCX.Decompress(tpl);
        var r = CollisionConverter.Convert(src, new HkPackfile(tpl), args.Length > 4 ? args[4] : Path.GetFileNameWithoutExtension(args[3]));
        File.WriteAllBytes(args[3], r.Bytes);
        Console.WriteLine($"{args[3]}: {r.Bytes.Length:N0} bytes, {r.Triangles} triangles in {r.Subparts} subparts, materials {r.Materials}");
        return 0;
    }
}
