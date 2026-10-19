using System.Text.Json;
using SoulsFormats;

namespace Lordran.Tools.Formats;

/// <summary>
/// Loads a game's gameplay params with paramdefs applied and Smithbox's community row names attached.
/// DSR: param\GameParam\GameParam.parambnd.dcx + shipped paramdefs (Smithbox DS1R defs fill gaps).
/// DS2: enc_regulation.bnd.dcx (decrypted) + loose per-map params from the extracted data, Smithbox DS2S defs.
/// </summary>
public sealed class ParamStore
{
    public const string SmithboxParams = @"C:\EchoesOfLordran\tools\smithbox\Assets\PARAM";

    public string Game { get; }
    public Dictionary<string, PARAM> Params { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Params present in the files but left out because no paramdef matched (name → why).</summary>
    public Dictionary<string, string> Skipped { get; } = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Dictionary<int, string>> rowNames = new(StringComparer.OrdinalIgnoreCase);

    ParamStore(string game) => Game = game;

    public static ParamStore LoadDsr(string dsrRoot)
    {
        var store = new ParamStore("DS1R");
        var defs = BND3.Read(Path.Combine(dsrRoot, @"paramdef\paramdef.paramdefbnd.dcx")).Files.Select(f => PARAMDEF.Read(f.Bytes)).ToList();
        // Shipped defs first, then Smithbox's; ApplyParamdefCarefully takes the first whose type, version and row size match
        // (DSR ships a SpEffectParam def that no longer matches its own rows).
        defs.AddRange(SmithboxDefs("DS1R"));
        foreach (var file in BND3.Read(Path.Combine(dsrRoot, @"param\GameParam\GameParam.parambnd.dcx")).Files)
            store.Add(Path.GetFileNameWithoutExtension(file.Name), file.Bytes, defs);
        store.LoadRowNames();
        return store;
    }

    public static ParamStore LoadDs2(string ds2Game, string extractedData)
    {
        var store = new ParamStore("DS2S");
        var defs = SmithboxDefs("DS2S");
        foreach (var file in BND4.Read(Ds2Regulation.Decrypt(Path.Combine(ds2Game, "enc_regulation.bnd.dcx"))).Files)
            store.Add(Path.GetFileNameWithoutExtension(file.Name), file.Bytes, defs);
        foreach (string path in Directory.GetFiles(Path.Combine(extractedData, "param"), "*.param"))
            store.Add(Path.GetFileNameWithoutExtension(path), File.ReadAllBytes(path), defs);
        store.LoadRowNames();
        return store;
    }

    static List<PARAMDEF> SmithboxDefs(string game) =>
        Directory.GetFiles(Path.Combine(SmithboxParams, game, "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();

    void Add(string name, byte[] bytes, List<PARAMDEF> defs)
    {
        try
        {
            var param = PARAM.Read(bytes);
            // DSR bumped some params' data version (SpEffectParam, CharaInitParam: v2) without changing the layout, so
            // fall back to a def with the same type and row size.
            var sameLayout = defs.FirstOrDefault(d => d.ParamType == param.ParamType && d.GetRowSize() == param.DetectedSize);
            if (param.ApplyParamdefCarefully(defs))
                Params[name] = param;
            else if (sameLayout != null)
            {
                param.ApplyParamdef(sameLayout);
                Params[name] = param;
            }
            else
                Skipped[name] = $"type {param.ParamType} v{param.ParamdefDataVersion} rowsize {param.DetectedSize}; defs: " +
                    string.Join(", ", defs.Where(d => d.ParamType == param.ParamType).Select(d => $"v{d.DataVersion} size {d.GetRowSize()}"));
        }
        catch (InvalidDataException) { } // non-param files bundled in the regulation (language markers)
    }

    void LoadRowNames()
    {
        string dir = Path.Combine(SmithboxParams, Game, "Param Row Names", "English");
        foreach (string file in Directory.GetFiles(dir, "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var names = new Dictionary<int, string>();
            foreach (var entry in doc.RootElement.GetProperty("Entries").EnumerateArray())
            {
                var texts = entry.GetProperty("Entries");
                if (texts.GetArrayLength() > 0)
                    names[entry.GetProperty("ID").GetInt32()] = texts[0].GetString()!.Trim();
            }
            rowNames[Path.GetFileNameWithoutExtension(file)] = names;
        }
    }

    public string RowName(string param, int id) =>
        rowNames.TryGetValue(param, out var names) && names.TryGetValue(id, out var name) ? name : "";

    public IEnumerable<(int Id, string Name)> FindRows(string param, string text) =>
        Params[param].Rows.Select(r => (r.ID, RowName(param, r.ID)))
            .Where(r => r.Item2.Contains(text, StringComparison.OrdinalIgnoreCase));
}
