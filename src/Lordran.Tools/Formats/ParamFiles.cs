namespace Lordran.Tools.Formats;

/// <summary>Session 11 (burg11): a per-map param our builds write without rows (the Burg's generators, talk rows: no
/// characters in M1; Firelink's hit groups) is valid for DS2 (Firelink ships several, verified in game) but SoulsFormats
/// cannot read it back (it reads the first row's offset unconditionally). Checks test this first.</summary>
public static class ParamFiles
{
    /// <summary>True when the PARAM file's row count (u16 at 0x0A) is 0.</summary>
    public static bool Rowless(string path)
    {
        if (!File.Exists(path)) return false;
        using var f = File.OpenRead(path);
        if (f.Length < 0x0C) return true;
        var b = new byte[2];
        f.Position = 0x0A;
        f.ReadExactly(b);
        return BitConverter.ToUInt16(b) == 0;
    }
}
