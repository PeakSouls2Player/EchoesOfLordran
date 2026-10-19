using System.Reflection;
using System.Runtime.Loader;

namespace Lordran.Tools.Formats;

/// <summary>
/// DS2's gameplay params live in enc_regulation.bnd.dcx, which is encrypted. SoulsFormatsNEXT can't
/// decrypt the DS2 variant, but Smithbox's bundled fork (Andre.SoulsFormats) can, so we load that
/// assembly in its own load context (both use the SoulsFormats namespace) and call it by reflection.
/// TODO: replace with our own implementation once the scheme is documented, so the converter
/// doesn't depend on a Smithbox install.
/// </summary>
public static class Ds2Regulation
{
    public const string SmithboxDir = @"C:\EchoesOfLordran\tools\smithbox";

    /// <summary>Returns the decrypted regulation as plain BND4 bytes (readable by our SoulsFormats).</summary>
    public static byte[] Decrypt(string encRegulationPath)
    {
        var context = new AssemblyLoadContext("smithbox-souls", isCollectible: true);
        context.Resolving += (ctx, name) =>
        {
            string candidate = Path.Combine(SmithboxDir, name.Name + ".dll");
            return File.Exists(candidate) ? ctx.LoadFromAssemblyPath(candidate) : null;
        };

        try
        {
            var asm = context.LoadFromAssemblyPath(Path.Combine(SmithboxDir, "Andre.SoulsFormats.dll"));
            var method = asm.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .First(m => m.Name == "DecryptDS2Regulation" && m.GetParameters() is [{ ParameterType: var p }] && p == typeof(string));

            object bnd = method.Invoke(null, new object[] { encRegulationPath })!;
            var write = bnd.GetType().GetMethods().First(m => m.Name == "Write" && m.GetParameters().Length == 0);
            object result = write.Invoke(bnd, null)!;
            return result switch
            {
                byte[] bytes => bytes,
                Memory<byte> memory => memory.ToArray(),
                _ => throw new InvalidOperationException($"unexpected Write() result {result.GetType()}"),
            };
        }
        finally
        {
            context.Unload();
        }
    }
}
