using System.Reflection;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>msb2-dump MAPID|FILE.msb [events|regions|parts|models|poses] — every DS2 MSB entry of a kind with all its properties (reflection).</summary>
public static class Msb2Dump
{
    public static int Run(string[] args)
    {
        string map = args[1];
        string what = args.Length > 2 ? args[2] : "events";
        var msb = MSB2.Read(map.EndsWith(".msb", StringComparison.OrdinalIgnoreCase) ? map : Path.Combine(ExtractDs2.DefaultOut, "map", map, map + ".msb"));
        IEnumerable<object> entries = what switch
        {
            "regions" => msb.Regions.GetEntries(),
            "parts" => msb.Parts.GetEntries(),
            "models" => msb.Models.GetEntries(),
            "poses" => msb.PartPoses.Cast<object>(),
            _ => msb.Events.GetEntries(),
        };
        foreach (var e in entries)
        {
            var props = e.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0)
                .Select(p => $"{p.Name}={Fmt(p.GetValue(e))}");
            Console.WriteLine($"{e.GetType().Name}: {string.Join(" ", props)}");
        }
        return 0;
    }

    static string Fmt(object? v) => v switch
    {
        null => "null",
        System.Collections.IEnumerable list when v is not string => "[" + string.Join(",", list.Cast<object>().Select(x => x is uint u ? u.ToString("X8") : x?.ToString())) + "]",
        _ => v.ToString() ?? "",
    };
}
