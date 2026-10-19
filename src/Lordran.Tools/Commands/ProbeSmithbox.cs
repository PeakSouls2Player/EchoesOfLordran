using System.Reflection;
using System.Runtime.Loader;
using Lordran.Tools.Formats;

namespace Lordran.Tools.Commands;

/// <summary>
/// probe-smithbox — lists static byte[] fields (keys/IVs) in Smithbox's Andre.SoulsFormats whose names
/// mention DS2, so our own regulation cipher can be implemented and verified against it.
/// </summary>
public static class ProbeSmithbox
{
    public static int Run()
    {
        var context = new AssemblyLoadContext("probe", isCollectible: true);
        context.Resolving += (ctx, name) =>
        {
            string candidate = Path.Combine(Ds2Regulation.SmithboxDir, name.Name + ".dll");
            return File.Exists(candidate) ? ctx.LoadFromAssemblyPath(candidate) : null;
        };
        var asm = context.LoadFromAssemblyPath(Path.Combine(Ds2Regulation.SmithboxDir, "Andre.SoulsFormats.dll"));
        foreach (var type in asm.GetTypes())
        {
            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.FieldType != typeof(byte[]) || !field.Name.Contains("ds2", StringComparison.OrdinalIgnoreCase))
                    continue;
                var value = (byte[]?)field.GetValue(null);
                Console.WriteLine($"{type.FullName}.{field.Name} = {(value == null ? "null" : Convert.ToHexString(value))}");
            }
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (method.Name.Contains("Aes", StringComparison.OrdinalIgnoreCase) || method.Name.Contains("DS2Regulation"))
                    Console.WriteLine($"  method {type.FullName}.{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})");
        }
        context.Unload();
        return 0;
    }
}
