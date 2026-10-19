using Lordran.Tools.Formats.Havok;
using SoulsFormats;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// A DS1 (DSR) character's animation archive, <c>chr/cNNNN.anibnd.dcx</c> (BND3), read for the 1:1 pipeline (session 8,
/// track chr step 2). Entries (c2500, checked): <c>hkxx64\aXX_YYYY.hkx</c> with BND id XX·10000+YYYY (the animation id the
/// AI and TAE use: a00_3000 = 3000, a02_3000 = 23000), <c>hkxx64\Skeleton.hkx</c> (id 1000000) and
/// <c>taeNew\x64\cNNNN.tae</c> (id 3000000, DS1 TAE: the per-animation events, ids = animation ids).
/// </summary>
public sealed class Ds1ChrSource
{
    readonly Dictionary<int, byte[]> clips = [];
    public Ds1AnimBridge.Ds1Skeleton Skeleton = null!;
    public TAE? Tae;
    readonly Dictionary<int, Ds1AnimBridge.Result> cache = [];

    public IEnumerable<int> ClipIds => clips.Keys.OrderBy(k => k);
    public bool Has(int id) => clips.ContainsKey(id);

    /// <summary>Animation id → file stem (3000 → a00_3000, 23000 → a02_3000).</summary>
    public static string ClipName(int id) => $"a{id / 10000:D2}_{id % 10000:D4}";

    static byte[] Raw(byte[] b) => DCX.Is(b) ? DCX.Decompress(b) : b;

    public static Ds1ChrSource FromAnibnd(BND3 bnd)
    {
        var s = new Ds1ChrSource();
        foreach (var f in bnd.Files)
        {
            string name = Path.GetFileName(f.Name.Replace('\\', '/'));
            if (name.Equals("Skeleton.hkx", StringComparison.OrdinalIgnoreCase))
                s.Skeleton = Ds1AnimBridge.ReadSkeleton(new HkTagfile(Raw(f.Bytes)));
            else if (name.EndsWith(".tae", StringComparison.OrdinalIgnoreCase))
                s.Tae = TAE.Read(f.Bytes);
            else if (name.StartsWith('a') && name.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase) && name.Length == 12
                     && int.TryParse(name[1..3], out int xx) && int.TryParse(name[4..8], out int yyyy))
                s.clips[xx * 10000 + yyyy] = f.Bytes;
        }
        if (s.Skeleton == null) throw new InvalidDataException("DS1 anibnd without Skeleton.hkx");
        return s;
    }

    public static Ds1ChrSource Load(string dsrRoot, string chr) =>
        FromAnibnd(BND3.Read(Path.Combine(dsrRoot, "chr", chr + ".anibnd.dcx")));

    public Ds1AnimBridge.Ds1Samples Samples(int id) => Ds1AnimBridge.Sample(Skeleton, new HkTagfile(Raw(clips[id])));

    /// <summary>The DS1 clip bridged into Morpheme space (rig + channels + trajectory), cached.</summary>
    public Ds1AnimBridge.Result Bridge(int id)
    {
        if (!cache.TryGetValue(id, out var r)) cache[id] = r = Ds1AnimBridge.Bridge(Samples(id));
        return r;
    }

    public TAE.Animation? TaeAnim(int id) => Tae?.Animations.FirstOrDefault(a => a.ID == id);
}
