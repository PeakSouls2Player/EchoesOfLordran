using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// probe-speffect [Name] — DS2 keeps item special effects as EMEVD scripts in the regulation (SpEffectWeapon,
/// SpEffectArmor, SpEffectRing, …; event id = item id). Lists each event with the item it belongs to and its
/// instructions (bank:id and argument bytes), plus a histogram of instruction kinds.
/// </summary>
public static class ProbeSpEffect
{
    public static int Run(string[] args)
    {
        string name = args.Length > 1 ? args[1] : "SpEffectWeapon";
        var bnd = BND4.Read(Ds2RegulationCipher.Decrypt(File.ReadAllBytes(Path.Combine(Program.DefaultDs2Root, "enc_regulation.bnd.dcx"))));
        var file = bnd.Files.First(f => Path.GetFileNameWithoutExtension(f.Name).Equals(name, StringComparison.OrdinalIgnoreCase));
        var ds2 = ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut);
        var evd = EMEVD.Read(file.Bytes);
        byte[] rewritten = evd.Write();
        Console.WriteLine($"{file.Name}: format {evd.Format}, {evd.Events.Count} events, rewrite identical: {rewritten.AsSpan().SequenceEqual(file.Bytes)} ({file.Bytes.Length} → {rewritten.Length} bytes)");
        var kinds = new Dictionary<string, int>();
        foreach (var e in evd.Events)
        {
            string item = ds2.RowName("ItemParam", (int)e.ID);
            Console.WriteLine($"event {e.ID} {(item.Length > 0 ? item : "?")}  rest={e.RestBehavior} params={e.Parameters.Count}");
            foreach (var ins in e.Instructions)
            {
                string key = $"{ins.Bank}:{ins.ID}";
                kinds[key] = kinds.GetValueOrDefault(key) + 1;
                Console.WriteLine($"    {key,-8} [{ins.ArgData.Length,2}] {Convert.ToHexString(ins.ArgData)}");
            }
        }
        Console.WriteLine("instruction kinds: " + string.Join(", ", kinds.OrderBy(k => k.Key).Select(k => $"{k.Key}×{k.Value}")));
        return 0;
    }
}
