using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>acb-dump FILE [max] — assets in a DS2 .acb (rendering config): type, paths and model settings.</summary>
public static class AcbDump
{
    public static int Run(string[] args)
    {
        var acb = ACB.Read(args[1]);
        int max = args.Length > 2 ? int.Parse(args[2]) : 20;
        Console.WriteLine($"{acb.Assets.Count} assets: " + string.Join(", ", acb.Assets.GroupBy(a => a.GetType().Name).Select(g => $"{g.Key}×{g.Count()}")));
        foreach (var a in acb.Assets.Take(max))
        {
            string extra = a switch
            {
                ACB.Asset.Model m => $"draw={m.DrawDistance} lodRate={m.MeshLodRate} refl={m.Reflectible} normInter={m.NormalInteraction} u20={m.Unk20} render={m.RenderType} noShadowSrc={m.DisableShadowSource} noShadowDst={m.DisableShadowTarget} u27={m.Unk27} u28={m.Unk28} u2c={m.Unk2C} fixCam={m.FixToCamera} u2e={m.Unk2E} lowTex={m.LowTextureDistance} cheap={m.CheapRenderDistance} u34={m.Unk34} members={m.Members?.Count}",
                ACB.Asset.GITexture g => $"u10={g.Unk10} u14={g.Unk14}",
                _ => ""
            };
            Console.WriteLine($"  {a.GetType().Name,-10} {a.AbsolutePath} | {a.RelativePath} {extra}");
            if (a is ACB.Asset.Model mm && mm.Members != null)
                Console.WriteLine($"      unk0A={mm.Unk0A} members(unk00={mm.Members.Unk00}): " + string.Join(" | ", mm.Members.Select(x => $"{x.Text}/{x.Unk04}")));
        }
        return 0;
    }
}
