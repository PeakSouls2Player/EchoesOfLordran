using Lordran.Tools.Formats;
using Lordran.Tools.Textures;
using SoulsFormats;

namespace Lordran.Tools.Build;

/// <summary>Everything read from the player's DSR install: params, English text, item icons, parts models.</summary>
public sealed class Ds1Source
{
    public string Root { get; }
    public ParamStore Params { get; }
    readonly Dictionary<string, FMG> text = new(StringComparer.OrdinalIgnoreCase);
    readonly Lazy<(DRB Layout, Dictionary<string, TPF.Texture> Atlases)> icons;

    // DSR doubled the menu atlases (1024 → 2048) but kept PTDE's layout coordinates.
    const int IconAtlasScale = 2;

    static readonly Dictionary<string, ParamStore> paramCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>One DS1 param table without loading the texts (cached per install).</summary>
    public static PARAM? LoadParam(string root, string name)
    {
        if (!paramCache.TryGetValue(root, out var store)) paramCache[root] = store = ParamStore.LoadDsr(root);
        return store.Params.GetValueOrDefault(name);
    }

    public Ds1Source(string root)
    {
        Root = root;
        Params = ParamStore.LoadDsr(root);
        foreach (var f in BND3.Read(Path.Combine(root, @"msg\ENGLISH\item.msgbnd.dcx")).Files)
            text.TryAdd(Path.GetFileNameWithoutExtension(f.Name), FMG.Read(f.Bytes));
        // menu texts too (event messages "イベントテキスト", key guides, area names), under their own file names
        foreach (var f in BND3.Read(Path.Combine(root, @"msg\ENGLISH\menu.msgbnd.dcx")).Files)
            text.TryAdd(Path.GetFileNameWithoutExtension(f.Name), FMG.Read(f.Bytes));
        icons = new Lazy<(DRB, Dictionary<string, TPF.Texture>)>(LoadIcons);
        dlcLayout = new Lazy<DRB>(() => DRB.Read(DCX.Decompress(Path.Combine(Root, @"menu\menu.drb.dcx")), DRB.DRBVersion.DarkSoulsRemastered));
    }

    /// <summary>The first FMG (item or menu) that has the id, e.g. DS1 event message 10010163 "Locked".</summary>
    public string AnyText(int id) => text.Values.Select(f => f.Entries.FirstOrDefault(e => e.ID == id)?.Text).FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? "";
    public IEnumerable<string> TextNames => text.Keys;

    /// <summary>
    /// A DS1 event message (EMEVD dialogs, banners, action-button help): Event_text_ first, AnyText only as a fallback.
    /// Session 8: AnyText searches item names and Conversation_ before Event_text_, and ids collide, e.g. 10010400 is
    /// "Open" in Event_text_ but an NPC line in Conversation_, 10010600 "In Lordran, level up and kindle at bonfires" vs
    /// "Hrg!" (ds1-text --all 10010400 10010600).
    /// </summary>
    public string EventText(int id) => text.TryGetValue("Event_text_", out var ev) && ev.Entries.FirstOrDefault(e => e.ID == id)?.Text is { Length: > 0 } t ? t : AnyText(id);

    public string Text(string fmg, int id) => text[fmg].Entries.FirstOrDefault(e => e.ID == id)?.Text ?? "";

    /// <summary>DS1 long descriptions start with "Weapon type: …\nAttack type: …" lines that DS2's menus show elsewhere.</summary>
    public string LoreText(string fmg, int id)
    {
        var lines = Text(fmg, id).Replace("\r", "").Split('\n').ToList();
        while (lines.Count > 0 && (lines[0].Contains(':') && lines[0].Length < 48 || lines[0].Length == 0))
            lines.RemoveAt(0);
        return string.Join("\n", lines).Trim();
    }

    (DRB, Dictionary<string, TPF.Texture>) LoadIcons()
    {
        var layout = DRB.Read(Path.Combine(Root, @"menu\menu.drb"), DRB.DRBVersion.DarkSouls);
        var atlases = new Dictionary<string, TPF.Texture>(StringComparer.OrdinalIgnoreCase);
        foreach (string tpf in Directory.GetFiles(Path.Combine(Root, "menu"), "menu_*.tpf.dcx"))
            foreach (var t in TPF.Read(tpf).Textures)
                atlases.TryAdd(t.Name, t);
        return (layout, atlases);
    }

    /// <summary>
    /// The DS1 item icon (EquIcon_NNNN) cut out of its menu atlas. The loose menu.drb (PTDE layout, coordinates ×2)
    /// lacks the DLC icons (198–204); those come from menu.drb.dcx, whose scale is worked out from an icon both share.
    /// </summary>
    public Image Icon(int iconId)
    {
        var (layout, atlases) = icons.Value;
        string name = $"EquIcon_{iconId:D4}";
        var sprite = Sprite(layout, name);
        double scale = IconAtlasScale;
        if (sprite == null)
        {
            var dlc = dlcLayout.Value;
            sprite = Sprite(dlc, name) ?? throw new KeyNotFoundException($"{name} not in menu.drb or menu.drb.dcx");
            var a = Sprite(layout, "EquIcon_0010")!;
            var b = Sprite(dlc, "EquIcon_0010")!;
            scale = IconAtlasScale * (double)(a.TexRightEdge - a.TexLeftEdge) / (b.TexRightEdge - b.TexLeftEdge);
            layout = dlc;
        }
        var atlas = atlases[layout.Textures[sprite.TextureIndex].Name];
        var image = Dds.Read(atlas.Bytes).DecodeTop();
        int S(int v) => (int)Math.Round(v * scale);
        return image.Crop(S(sprite.TexLeftEdge), S(sprite.TexTopEdge), S(sprite.TexRightEdge - sprite.TexLeftEdge), S(sprite.TexBottomEdge - sprite.TexTopEdge));
    }

    static DRB.Shape.SpriteBase? Sprite(DRB layout, string name) =>
        layout.Dlgs.SelectMany(d => d.Dlgos).FirstOrDefault(o => o.Name == name)?.Shape as DRB.Shape.SpriteBase;

    readonly Lazy<DRB> dlcLayout;

    public BND3 Parts(string name) => BND3.Read(Path.Combine(Root, "parts", name + ".partsbnd.dcx"));
}
