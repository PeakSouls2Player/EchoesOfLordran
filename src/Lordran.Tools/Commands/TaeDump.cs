using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>tae-dump FILE.tae — animations (id, header) and their events (type, start-end, raw parameter bytes).
/// Session 7: DS2 object TAEs (timeact/obj) — which animation ids an object has and what its events do.</summary>
public static class TaeDump
{
    public static int Run(string[] args)
    {
        // session 11 (snd11): a binder (DS1 objbnd / anibnd, .dcx or not) → every TAE inside it, nested binders included
        if (!args[1].EndsWith(".tae", StringComparison.OrdinalIgnoreCase))
        {
            void Walk(byte[] b, string where)
            {
                if (DCX.Is(b)) b = DCX.Decompress(b);
                IEnumerable<BinderFile> files = BND3.Is(b) ? BND3.Read(b).Files : BND4.Is(b) ? BND4.Read(b).Files : [];
                foreach (var f in files)
                {
                    if (f.Name.EndsWith(".tae", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine($"# {where} / {f.Name}"); Dump(TAE.Read(f.Bytes)); }
                    else if (BND3.Is(f.Bytes) || BND4.Is(f.Bytes) || DCX.Is(f.Bytes)) Walk(f.Bytes, where + " / " + Path.GetFileName(f.Name));
                }
            }
            Walk(File.ReadAllBytes(args[1]), Path.GetFileName(args[1]));
            return 0;
        }
        Dump(TAE.Read(args[1]));
        return 0;
    }

    static void Dump(TAE tae)
    {
        Console.WriteLine($"TAE id={tae.ID} format={tae.Format} skeleton={tae.SkeletonName} sib={tae.SibName} animations={tae.Animations.Count}");
        foreach (var a in tae.Animations)
        {
            Console.WriteLine($"  anim {a.ID}  {a.MiniHeader?.GetType().Name} {DescribeHeader(a.MiniHeader)}  events={a.Events.Count}");
            foreach (var e in a.Events)
                Console.WriteLine($"    type {e.Type,4} {e.StartTime,7:0.###}-{e.EndTime,-7:0.###} {Convert.ToHexString(e.GetParameterBytes(false))}");
        }
    }

    static string DescribeHeader(TAE.Animation.AnimMiniHeader? h) => h switch
    {
        TAE.Animation.AnimMiniHeader.Standard s => $"loop={s.IsLoopByDefault} importHkx={s.ImportsHKX} importId={s.ImportHKXSourceAnimID}",
        TAE.Animation.AnimMiniHeader.ImportOtherAnim o => $"import anim {o.ImportFromAnimID}",
        _ => "",
    };
}
