using SoulsFormats;

namespace Lordran.Tools.Build;

/// <summary>Typed access to param cells for the porters.</summary>
public static class ParamCells
{
    public static int I(PARAM.Row row, string field) => (int)Convert.ToInt64(row[field]?.Value ?? throw new KeyNotFoundException(field));

    public static double Get(PARAM.Row row, string field) => Convert.ToDouble(row[field]?.Value ?? throw new KeyNotFoundException(field));

    /// <summary>
    /// Stores <paramref name="value"/> in the cell's own type. Each arm is boxed separately: a switch expression over
    /// float/int/uint/… otherwise takes float as its common type and rounds large ids (66,050,010 → 66,050,008).
    /// </summary>
    public static void Set(PARAM.Row row, string field, double value)
    {
        var cell = row[field] ?? throw new KeyNotFoundException(field);
        cell.Value = cell.Value switch
        {
            float => (object)(float)value,
            int => (object)(int)Math.Round(value),
            uint => (object)(uint)Math.Round(value),
            short => (object)(short)Math.Round(value),
            ushort => (object)(ushort)Math.Round(value),
            byte => (object)(byte)Math.Round(value),
            sbyte => (object)(sbyte)Math.Round(value),
            _ => throw new NotSupportedException($"{field}: {cell.Value?.GetType()}"),
        };
    }
}
