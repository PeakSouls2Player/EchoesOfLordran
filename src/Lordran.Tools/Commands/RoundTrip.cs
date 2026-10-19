using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// roundtrip FILE... — reads and rewrites each file with SoulsFormats (MSB2 .msb, ESD .esd, per-map .param with the DS2S
/// paramdefs) and reports whether the bytes come back identical, so vanilla files can be edited without side effects.
/// </summary>
public static class RoundTrip
{
    public static int Run(string[] args)
    {
        var defs = Directory.GetFiles(Path.Combine(ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        int bad = 0;
        foreach (string path in args.Skip(1))
        {
            byte[] a = File.ReadAllBytes(path);
            byte[] b = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".msb" => MSB2.Read(a).Write(),
                ".esd" => ESD.Read(a).Write(),
                ".param" => Param(a, defs),
                _ => throw new ArgumentException($"unsupported: {path}"),
            };
            bool same = a.AsSpan().SequenceEqual(b);
            int first = same ? -1 : Enumerable.Range(0, Math.Min(a.Length, b.Length)).FirstOrDefault(i => a[i] != b[i], Math.Min(a.Length, b.Length));
            string semantic = "";
            if (!same && path.EndsWith(".esd", StringComparison.OrdinalIgnoreCase))
                semantic = EsdFingerprint(ESD.Read(a)) == EsdFingerprint(ESD.Read(b)) ? "; same content (layout only)" : "; CONTENT DIFFERS";
            else if (!same && path.EndsWith(".param", StringComparison.OrdinalIgnoreCase) && b.Length <= a.Length && a.AsSpan(0, b.Length).SequenceEqual(b))
                semantic = $"; identical except {a.Length - b.Length} trailing byte(s) dropped";
            Console.WriteLine($"{(same ? "IDENTICAL" : "DIFFERS  ")} {Path.GetFileName(path)} ({a.Length:N0} → {b.Length:N0} bytes{(same ? "" : $", first difference at 0x{first:X}")}{semantic})");
            if (!same) bad++;
        }
        return bad == 0 ? 0 : 1;
    }

    /// <summary>Every header field, state, command, condition and evaluator of an ESD as one string.</summary>
    public static string EsdFingerprint(ESD esd)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"{esd.LongFormat} {esd.DarkSoulsCount} {esd.Name} {esd.Unk70} {esd.Unk74} {esd.Unk78} {esd.Unk7C}\n");
        string Cmd(ESD.CommandCall c) => $"{c.CommandBank}:{c.CommandID}({string.Join(",", c.Arguments.Select(Convert.ToHexString))})";
        void Cond(ESD.Condition c, int depth)
        {
            sb.Append(new string(' ', depth)).Append($"if {Convert.ToHexString(c.Evaluator)} -> {c.TargetState} pass[{string.Join(";", c.PassCommands.Select(Cmd))}]\n");
            foreach (var s in c.Subconditions) Cond(s, depth + 1);
        }
        foreach (var (gid, states) in esd.StateGroups.OrderBy(g => g.Key))
            foreach (var (sid, s) in states.OrderBy(s => s.Key))
            {
                sb.Append($"{gid}/{sid} entry[{string.Join(";", s.EntryCommands.Select(Cmd))}] exit[{string.Join(";", s.ExitCommands.Select(Cmd))}] while[{string.Join(";", s.WhileCommands.Select(Cmd))}]\n");
                foreach (var c in s.Conditions) Cond(c, 1);
            }
        return sb.ToString();
    }

    static byte[] Param(byte[] bytes, List<PARAMDEF> defs)
    {
        var p = PARAM.Read(bytes);
        if (!p.ApplyParamdefCarefully(defs)) throw new InvalidDataException("no fitting paramdef");
        return p.Write();
    }
}
