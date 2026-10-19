using System.Runtime.InteropServices;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// shader-disasm FILE|BINDER [name-substring…] (session 8, track skylight): disassembles compiled DX11 shaders (DXBC:
/// DSR's FRPG_*_DX11.shaderbnd.dcx *.fpo/*.vpo, or loose files) with Windows' own D3DDisassemble (System32
/// d3dcompiler_47.dll, a system component; nothing downloaded). The output includes the RDEF block (constant buffer
/// variable names and offsets), which is what ties DS1's draw-param fields to the tone-map/colour math.
/// </summary>
public static class ShaderDisasm
{
    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall)]
    static extern int D3DDisassemble(IntPtr src, UIntPtr size, uint flags, IntPtr comments, out IntPtr blob);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr GetPtr(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate UIntPtr GetSize(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint Release(IntPtr self);

    public static string? Disassemble(byte[] dxbc)
    {
        var h = GCHandle.Alloc(dxbc, GCHandleType.Pinned);
        try
        {
            int hr = D3DDisassemble(h.AddrOfPinnedObject(), (UIntPtr)dxbc.Length, 0, IntPtr.Zero, out IntPtr blob);
            if (hr < 0 || blob == IntPtr.Zero) return null;
            IntPtr vtbl = Marshal.ReadIntPtr(blob);
            var ptr = Marshal.GetDelegateForFunctionPointer<GetPtr>(Marshal.ReadIntPtr(vtbl, 3 * IntPtr.Size))(blob);
            var size = Marshal.GetDelegateForFunctionPointer<GetSize>(Marshal.ReadIntPtr(vtbl, 4 * IntPtr.Size))(blob);
            string text = Marshal.PtrToStringAnsi(ptr, (int)size).TrimEnd('\0');
            Marshal.GetDelegateForFunctionPointer<Release>(Marshal.ReadIntPtr(vtbl, 2 * IntPtr.Size))(blob);
            return text;
        }
        finally { h.Free(); }
    }

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: shader-disasm FILE|BINDER [name-substring...]"); return 1; }
        var filters = args.Skip(2).ToList();
        byte[] head = File.ReadAllBytes(args[1]).Take(4).ToArray();
        bool single = head.Length == 4 && head[0] == 'D' && head[1] == 'X' && head[2] == 'B' && head[3] == 'C';
        IEnumerable<(string Name, byte[] Bytes)> files = single
            ? [(Path.GetFileName(args[1]), File.ReadAllBytes(args[1]))]
            : Unbind.Open(args[1]).Files.Select(f => (f.Name.Replace('\\', '/'), DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes));
        foreach (var (name, bytes) in files)
        {
            if (filters.Count > 0 && !filters.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase))) continue;
            Console.WriteLine($"===== {name} ({bytes.Length} bytes)");
            Console.WriteLine(bytes.Length >= 4 && bytes[0] == 'D' && bytes[1] == 'X' && bytes[2] == 'B' && bytes[3] == 'C' ? Disassemble(bytes) ?? "(D3DDisassemble failed)" : "(not DXBC)");
        }
        return 0;
    }
}
