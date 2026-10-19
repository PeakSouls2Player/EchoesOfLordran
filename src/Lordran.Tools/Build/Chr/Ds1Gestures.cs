using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Lordran.Tools.Build.Events;
using Lordran.Tools.Formats;
using Lordran.Tools.Formats.Morpheme;
using SoulsFormats;
using static Lordran.Tools.Formats.Morpheme.MorphemeMath;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// DS1's fifteen gestures on a "Lordran" page of DS2's gesture menu (session 11, track gest11; user: "Also include the poses.
/// Add them to the pose menu same style as the travel menu where they can switch around"). Rules GS1-GS6
/// (docs\translation-rules.md, data\sandbox\gest11\NOTES.md):
/// <list type="bullet">
/// <item><b>GS1 animation (additive c0001 content)</b>. How DS2 plays a gesture (decompile, V code): the menu calls
/// FUN_140098c20(slot) / FUN_140098c80(category) → FUN_140140910 / FUN_1401409b0 write the category to
/// [chr+0xb8]+0x730 and set bit 0x2000 of +0x4c0; the EventAction component FUN_14035ff00 then sets the network control
/// parameter <c>GestureCategory</c> (its CP list FUN_1403603c0, index 10) and fires request <c>Gesture</c> (FUN_140360540,
/// index 4). In c0001's network the request enters StateMachine_Main|BT_Gesture (node 8000) whose SM 7928 picks a state
/// from its hub 7929 by <c>GestureCategory in [v,v]</c> (CPIntInRange, 21 transitions, v = 10..310 = ItemParam.gestureId
/// of DS2's gesture items 63000000..63021000); each state is PlaySpeed(GestureSpeed) → one AnimSource leaf. A DS1 gesture
/// k becomes a NEW state of SM 7928: a clone of a vanilla gesture state (NmbNetClone.AddCategoryOn, rule E2b: the clone
/// owns its whole subtree), entered on <c>GestureCategory in [1000+10k, 1000+10k]</c>, playing a NEW clip (DS1 c0000
/// a00_68xx retargeted onto c0001's rig by NpcIdle.Retarget and encoded by our NSA encoder, appended to the file-name
/// table and the anibnd) with a NEW TimeAct track (value = a NEW TAE animation 74080000 + DS1 anim id, copied from the
/// template gesture's and time-scaled to DS1's length); every other event track of the template leaf is shared (their
/// times are clip fractions). Nothing vanilla changes: <see cref="VerifyAdditive"/> proves every vanilla node, bundle, clip,
/// file-name entry, category and TAE animation byte-identical (the only touched vanilla structure, SM 7928's own
/// state/condition table, keeps every vanilla record and gains appended ones).</item>
/// <item><b>GS2 storage</b>: DS2's gestures are inventory items (ItemParam 63xxx000, gestureId = the category; learned =
/// owned; equipped in PlayerStatusParam gestureSlot01-08 / equip slots 0x2c-0x33). An unknown item id in a save is a risk
/// for a vanilla game, so DS1 gestures are NOT items: a learned DS1 gesture is one synthetic flag of our flag store
/// (FlagSlots S rows "ds1-gesture:90xx", DS2 global block 20, bits no vanilla data references), which a vanilla game
/// ignores and the save keeps. DS1's six never-taught gestures (no DS1 talk teaches them: <see cref="Taught"/>) are
/// learned from the start, as in DS1.</item>
/// <item><b>GS3 learning</b>: DS1 talk <c>GiveItem(3, 90xx, 1)</c> + <c>AcquireGesture(n)</c> (cmds 65/66) → set the flag
/// (TalkTranslator); <c>--test-gestures</c> = the loader shows every DS1 gesture as learned (gestures.ini unlock_all=1).</item>
/// <item><b>GS4 menu</b> (loader gesture.c): LB/RB (Shift+Left/Right) switch the Top between Drangleic (vanilla, untouched)
/// and Lordran (8 cells per page: 1/2, 2/2) pages; texts: DS1 names (DS1 Item_name_ 9000-9014; DS1 has no gesture
/// descriptions), ingamemenu <see cref="TextBase"/>+1000k; icons: DS1 EquIcon_7000-7014 on DS2's gesture icon canvas
/// (ic_00631xx000.tpf); key guide 19019000 gets "⑨⑦：Drangleic/Lordran".</item>
/// </list>
/// </summary>
public static class Ds1Gestures
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>build-pack --no-gestures turns it off (no c0001.anibnd / c000100 TAEs, no texts, no gestures.ini).</summary>
    public static bool Enabled = true;
    /// <summary>build-pack --test-gestures: every DS1 gesture shows as learned (loader gestures.ini unlock_all=1; no save write).</summary>
    public static bool TestUnlockAll;

    public const string CategoryCp = "GestureCategory";
    public const int CategoryBase = 1000, CategoryStep = 10;      // vanilla 10..310
    public const int TextBase = 63_100_000, TextStep = 1000;      // ingamemenu name id = icon id (vanilla 63000000..63021000)
    public const int TaeBase = 74_080_000;                        // + DS1 anim id: a74_08_68xx (vanilla gestures 74010010..74010310)
    public const int TopGuideText = 19019000;                     // ingamemenu: the gesture Top's key guide
    public const int PageNameText = 19019100;                     // + 0 Drangleic, + 1 Lordran (loader gesture.c)
    public const int LordranGuideText = 19019102;                 // the Top's key guide on a Lordran page (no "Change")
    public const int TemplateIcon = 63_000_000;                   // Point's icon: the canvas
    public const string TableDevice = "lordran", TablePath = "gestures.ini";
    const int LbRbToggleText = 10011;                             // common.fmg "⑨⑦：Toggle"
    const int GestureSm = 7928, GestureHub = 7929;                // c0001 v1.0.3.0 layout (checked)

    /// <summary>DS1 gesture k = goods 9000+k = DS1 menu action 22+k (c0000.esd: f100(22+k) → state 257+k → its clip),
    /// its DS1 clip, and the DS2 gesture whose state is cloned (weapon hiding, cancel windows, TAE sounds).</summary>
    public sealed record Gesture(int Index, int Ds1Anim, int Template, string Why);

    public static readonly Gesture[] All =
    [
        new(0, 6800, 20, "Beckon ← ComeOn (I won't bite)"),
        new(1, 6805, 10, "Point forward ← Foreword (Point)"),
        new(2, 6820, 310, "Hurrah! ← HardDelight (Fist pump)"),
        new(3, 6810, 30, "Bow ← MakeaBow (Bow)"),
        new(4, 6825, 70, "Joy ← DelightMale (Joy)"),
        new(5, 6830, 220, "Shrug ← IDontKnowJP"),
        new(6, 6815, 60, "Wave ← Bye (Wave)"),
        new(7, 6801, 300, "Praise the Sun ← PrayingToTheSun (Praise the Sun)"),
        new(8, 6802, 10, "Point up ← Foreword (Point)"),
        new(9, 6803, 10, "Point down ← Foreword (Point)"),
        new(10, 6804, 120, "Look skyward ← Rise"),
        new(11, 6806, 90, "Well! What is it! ← No (No way)"),
        new(12, 6807, 250, "Prostration ← IAmSorryJP (Prostration)"),
        new(13, 6808, 50, "Proper bow ← Saluez (Duel bow)"),
        new(14, 6809, 130, "Prayer ← Pray (Have mercy!)"),
    ];

    /// <summary>The DS1 gestures a DS1 talk teaches (AcquireGesture, cmd 66; FlagSlots S rows exist for exactly these).</summary>
    public static readonly int[] TaughtIndices = [2, 4, 5, 7, 10, 11, 12, 13, 14];

    public static int Goods(Gesture g) => 9000 + g.Index;
    public static int Category(Gesture g) => CategoryBase + CategoryStep * g.Index;
    public static int TextId(Gesture g) => TextBase + TextStep * g.Index;
    public static int TaeId(Gesture g) => TaeBase + g.Ds1Anim;
    public static string FlagKey(int index) => $"ds1-gesture:{9000 + index}";

    /// <summary>Rule GS2: the learned flag of DS1 gesture <paramref name="index"/> (FlagSlots S row), null = learned from the
    /// start (never taught) or no row (table not regenerated).</summary>
    public static int? Flag(int index) => TaughtIndices.Contains(index) && FlagSlots.Current.Synthetic.TryGetValue(FlagKey(index), out int v) ? v : null;

    /// <summary>DS1 gesture index of a DS1 gesture goods (talk GiveItem category 3), null = not a gesture.</summary>
    public static int? IndexOfGoods(int category, int goods) => category == 3 && goods is >= 9000 and <= 9014 ? goods - 9000 : null;

    /// <summary>Every AcquireGesture(n) argument over all DS1 talk scripts (evidence for GS2's starting set).</summary>
    public static SortedSet<int> Taught()
    {
        var set = new SortedSet<int>();
        foreach (var (_, _, bytes) in Commands.Ds1Talk.AllTalks())
        {
            var esd = ESD.Read(bytes);
            foreach (var s in esd.StateGroups.Values.SelectMany(g => g.Values))
            {
                IEnumerable<ESD.Condition> Flat(IEnumerable<ESD.Condition> cs) => cs.SelectMany(c => new[] { c }.Concat(Flat(c.Subconditions)));
                foreach (var c in s.EntryCommands.Concat(s.WhileCommands).Concat(s.ExitCommands).Concat(Flat(s.Conditions).SelectMany(c => c.PassCommands)))
                    if (c.CommandBank == 1 && c.CommandID == 66 && c.Arguments.Count > 0 && int.TryParse(Commands.Esd.Expr(c.Arguments[0]), out int n)) set.Add(n);
            }
        }
        return set;
    }

    // ------------------------------------------------------------------ pack integration

    public sealed class Result
    {
        public Dictionary<string, byte[]> Files = [];
        public List<string> Report = [];
        public List<string> Problems = [];
        public byte[] Table = [];
    }

    static readonly ConditionalWeakTable<Pack, Result> built = new();

    /// <summary>The verified files of this pack's build (BuildPack's guard I1d-G compares the pack's c0001/c000100 with them).</summary>
    public static Result? Of(Pack pack) => built.TryGetValue(pack, out var r) ? r : null;

    public static Result Ensure(Pack pack, Ds1Source ds1)
    {
        if (built.TryGetValue(pack, out var r)) return r;
        r = Build(pack, ds1);
        if (r.Problems.Count > 0) throw new InvalidDataException("DS1 gestures: the vanilla-identity check failed: " + string.Join("; ", r.Problems.Take(12)));
        foreach (var (path, bytes) in r.Files) pack.AddFile("gamedata", path, bytes);
        pack.AddFile(TableDevice, TablePath, r.Table);
        foreach (var line in r.Report) pack.Note($"gestures: {line}");
        built.Add(pack, r);
        return r;
    }

    public static Result Build(Pack pack, Ds1Source ds1)
    {
        var res = new Result();
        string ds2Data = pack.Ds2Data;
        // ---- GS2 evidence: DS1's taught gestures = the S rows
        var taught = Taught();
        if (!taught.SetEquals(TaughtIndices)) throw new InvalidDataException($"DS1 talks teach gestures [{string.Join(",", taught)}], the table expects [{string.Join(",", TaughtIndices)}]");
        var missingFlags = TaughtIndices.Where(i => Flag(i) == null).ToList();
        if (missingFlags.Count > 0) throw new InvalidDataException($"no FlagSlots S row for {string.Join(", ", missingFlags.Select(FlagKey))} (Mappings\\ds1-flag-slots.tsv)");
        res.Report.Add($"GS2: DS1 talks teach gestures [{string.Join(", ", taught)}] (AcquireGesture over all {Commands.Ds1Talk.AllTalks().Count()} talk scripts); the other {All.Length - taught.Count} are learned from the start; flags {string.Join(", ", TaughtIndices.Select(i => $"{9000 + i}→{Flag(i)}"))}");

        // ---- GS1: network, clips, TAEs
        var (files, report) = BuildAnimations(ds2Data, Program.DefaultDsrRoot);
        foreach (var kv in files) res.Files[kv.Key] = kv.Value;
        res.Report.AddRange(report);
        var v = VerifyAdditive(ds2Data, res.Files);
        res.Report.AddRange(v.Report);
        res.Problems.AddRange(v.Problems);

        // ---- GS4: texts, icons, table
        res.Report.AddRange(Texts(pack, ds1));
        var goods = ds1.Params.Params["EquipParamGoods"];
        foreach (var g in All)
            WeaponPorter.PortIcon(ds1, pack, Convert.ToInt32(goods[Goods(g)]!["iconId"].Value), TextId(g), TemplateIcon);
        res.Report.Add($"icons: DS1 EquIcon {string.Join(",", All.Select(g => goods[Goods(g)]!["iconId"].Value))} → ic_{TextId(All[0]):D10}..ic_{TextId(All[^1]):D10}.tpf on ic_{TemplateIcon:D10}'s canvas");
        res.Table = Table(ds1);
        res.Report.Add($"table {TableDevice}/{TablePath}: {All.Length} gestures, unlock_all={(TestUnlockAll ? 1 : 0)}");
        return res;
    }

    static string Name(Ds1Source ds1, Gesture g) => ds1.Text("Item_name_", Goods(g));

    /// <summary>The loader's table (gesture.c reads it with GetPrivateProfileInt): per gesture its category, text/icon id
    /// and learned flag (0 = learned from the start).</summary>
    static byte[] Table(Ds1Source ds1)
    {
        var sb = new StringBuilder();
        sb.Append("; DS1 gestures for the loader's Lordran gesture page (gesture.c). Written by build-pack (Build/Chr/Ds1Gestures.cs).\r\n");
        sb.Append("[gestures]\r\nversion=1\r\n");
        sb.Append($"count={All.Length}\r\nunlock_all={(TestUnlockAll ? 1 : 0)}\r\npage_text={PageNameText}\r\nlordran_guide_text={LordranGuideText}\r\n");
        foreach (var g in All)
            sb.Append($"[g{g.Index}]\r\n; {Name(ds1, g)}\r\nds1={Goods(g)}\r\ncategory={Category(g)}\r\ntext={TextId(g)}\r\nicon={TextId(g)}\r\nflag={Flag(g.Index) ?? 0}\r\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>GS4 texts: DS1 names (ingamemenu, kind 7, as DS2's gesture names), the key-guide hint, the page names and
    /// the Lordran page's key guide (the vanilla one without the "Change" command: Lordran cells are not shortcut slots).</summary>
    static List<string> Texts(Pack pack, Ds1Source ds1)
    {
        var o = new List<string>();
        var menu = pack.Text("ingamemenu");
        foreach (int id in All.Select(TextId).Append(PageNameText).Append(PageNameText + 1).Append(LordranGuideText))
            if (menu.Entries.Any(e => e.ID == id && !string.IsNullOrEmpty(e.Text)))
                throw new InvalidDataException($"ingamemenu.fmg {id} is already used");
        foreach (var g in All) pack.SetText("ingamemenu", TextId(g), Name(ds1, g));
        string guide = menu.Entries.FirstOrDefault(e => e.ID == TopGuideText)?.Text ?? throw new InvalidDataException($"ingamemenu.fmg has no {TopGuideText}");
        var common = FMG.Read(Path.Combine(pack.Ds2Data, "menu", "text", "english", "common.fmg"));
        string toggle = common.Entries.FirstOrDefault(e => e.ID == LbRbToggleText)?.Text ?? throw new InvalidDataException($"common.fmg has no {LbRbToggleText}");
        int colon = toggle.IndexOf('：');
        if (colon <= 0) throw new InvalidDataException($"common.fmg {LbRbToggleText} \"{toggle}\" has no glyphs before '：'");
        string command = $"{toggle[..(colon + 1)]}{Maps.TravelPages.Drangleic}/{Maps.TravelPages.Lordran}";
        var cmds = SplitCommands(guide);
        if (cmds.Count < 2) throw new InvalidDataException($"ingamemenu {TopGuideText} \"{guide}\": unexpected key guide");
        var with = cmds.Contains(command) ? cmds : [cmds[0], command, .. cmds.Skip(1)];
        string updated = string.Join(" ", with);
        var lordran = with.Where(c => !c.StartsWith('①')).ToList();   // ①：Change = Top slot 20 (open the List), blocked on Lordran pages
        if (lordran.Count != with.Count - 1) throw new InvalidDataException($"ingamemenu {TopGuideText} \"{guide}\": no single ① (Change) command");
        pack.SetText("ingamemenu", TopGuideText, updated);
        pack.SetText("ingamemenu", PageNameText, Maps.TravelPages.Drangleic);
        pack.SetText("ingamemenu", PageNameText + 1, Maps.TravelPages.Lordran);
        pack.SetText("ingamemenu", LordranGuideText, string.Join(" ", lordran));
        o.Add($"texts: ingamemenu {TopGuideText} \"{guide}\" → \"{updated}\"; {LordranGuideText} \"{string.Join(" ", lordran)}\"; page names {PageNameText}/{PageNameText + 1}; names {string.Join(", ", All.Select(g => $"{TextId(g)} \"{Name(ds1, g)}\""))}; DS1 has no gesture descriptions (ds1-text 9000-9014 --all: Item_name_ only)");
        return o;
    }

    /// <summary>A DS2 key guide split at each " " followed by a button glyph (① U+2460 .. ㊨).</summary>
    static List<string> SplitCommands(string s)
    {
        var o = new List<string>();
        int start = 0;
        for (int i = 1; i < s.Length; i++)
            if (s[i - 1] == ' ' && (s[i] is >= '①' and <= '⑳' || s[i] is >= '㊤' and <= '㊨'))
            {
                o.Add(s[start..(i - 1)]);
                start = i;
            }
        o.Add(s[start..]);
        return o;
    }

    // ------------------------------------------------------------------ GS1 animations

    sealed class Clip
    {
        public Gesture G = null!;
        public byte[] Nsa = [];
        public NsaAnim Back = null!;
        public string Name = "";
        public uint Crc;
        public int Index;
        public double Ds1Duration, TemplateDuration;
        public int TemplateState, TemplateLeaf;
        public long TemplateTae;
        public NpcIdle.Check Check = null!;
    }

    /// <summary>The category states of the gesture SM: value → (state node, transition node, leaf, clip index).</summary>
    public static SortedDictionary<int, (int State, int Transition, int Leaf, List<int> Clips)> Categories(byte[] x)
    {
        var net = new NmbNetwork(x);
        var names = NmbNetEdit.NodeNames(x);
        var o = new SortedDictionary<int, (int, int, int, List<int>)>();
        int A = net.GetNode(GestureSm).Attribs.First(a => a.Type == 38).Offset;
        var sm = NmbNetEdit.ReadSmDef(x, A);
        long pC = (long)BinaryPrimitives.ReadUInt64LittleEndian(x.AsSpan(A + 0x30));
        foreach (var st in sm.States.Where(s => net.GetNode(s.Node).Type is 400 or 402 && s.Dst < sm.States.Count))
            foreach (int ci in st.Entry)
            {
                int c = A + (int)(long)BinaryPrimitives.ReadUInt64LittleEndian(x.AsSpan(A + (int)pC + 8 * ci));
                if (BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(c)) != 625) continue;
                if (!names.TryGetValue(BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(c + 0x48)), out var nm) || nm != "ControlParameters|" + CategoryCp) continue;
                int lo = BinaryPrimitives.ReadInt32LittleEndian(x.AsSpan(c + 0x4C)), hi = BinaryPrimitives.ReadInt32LittleEndian(x.AsSpan(c + 0x50));
                if (lo != hi) continue;
                int state = sm.States[st.Dst].Node;
                var kids = net.GetNode(state).Children.Where(k => k != 0xFFFF).ToList();
                int leaf = kids.Count == 1 && net.GetNode(kids[0]).Type == 104 ? kids[0] : -1;
                var clips = leaf >= 0 ? net.GetNode(leaf).Attribs.Where(a => a.Type == 21).Select(net.ClipIndex).ToList() : [];
                o[lo] = (state, st.Node, leaf, clips);
            }
        return o;
    }

    static (BND4 Bnd, DCX.CompressionInfo? Comp, BinderFile NmbFile) ReadAnibnd(byte[] raw)
    {
        DCX.CompressionInfo? comp = null;
        if (DCX.Is(raw)) raw = DCX.Decompress(raw, out comp);
        var bnd = BND4.Read(raw);
        return (bnd, comp, bnd.Files.First(f => f.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)));
    }

    public static (Dictionary<string, byte[]> Files, List<string> Report) BuildAnimations(string ds2Data, string dsrRoot)
    {
        var report = new List<string>();
        var files = new Dictionary<string, byte[]>();
        var (bnd, comp, nmbFile) = ReadAnibnd(File.ReadAllBytes(Path.Combine(ds2Data, "morpheme4", "chr", "c0001.anibnd.dcx")));
        var ed = new NmbNetClone(nmbFile.Bytes);
        var nmb = ed.Nmb;
        var rig = NmbRig.Read(nmb.Bundles[nmb.IndexOf(1)].Data);
        var maps = nmb.Bundles.Where(b => b.Type == 2).ToDictionary(b => b.AssetId, b => NmbChannelMap.Read(b.Data));
        var fileNames = NmbFileNamesFull.Read(nmb.Bundles[nmb.IndexOf(13)].Data);
        int vanillaClips = fileNames.Files.Count;

        // layout checks (c0001 of DS2 v1.0.3.0): BND id = clip index, NMB last; the gesture SM, its hub, 21 categories
        var nsas = bnd.Files.Where(f => f.Name.EndsWith(".nsa", StringComparison.OrdinalIgnoreCase)).ToList();
        int badIds = nsas.Count(f => f.ID < 0 || f.ID >= vanillaClips || !Path.GetFileName(f.Name.Replace('\\', '/')).Equals(fileNames.Files[f.ID] + (fileNames.Files[f.ID].EndsWith(".nsa", StringComparison.OrdinalIgnoreCase) ? "" : ".nsa"), StringComparison.OrdinalIgnoreCase));
        if (badIds > 0 || nmbFile.ID != vanillaClips) throw new InvalidDataException($"c0001.anibnd: {badIds} NSA ids are not their clip index, NMB id {nmbFile.ID} (clips {vanillaClips})");
        var x0 = ed.X;
        var names0 = NmbNetEdit.NodeNames(x0);
        var net0 = new NmbNetwork(x0);
        if (net0.GetNode(GestureSm).Type != 10 || net0.GetNode(GestureSm).Parent != 8002 || net0.GetNode(GestureHub).Parent != GestureSm || names0.GetValueOrDefault(8000) != "StateMachine_Main|BT_Gesture")
            throw new InvalidDataException("c0001: the gesture state machine is not where DS2 v1.0.3.0 has it (SM 7928 under BT_Gesture 8000)");
        var cats0 = Categories(x0);
        if (cats0.Count != 21 || cats0.Keys.Any(k => k >= CategoryBase)) throw new InvalidDataException($"c0001: {cats0.Count} gesture categories ({string.Join(",", cats0.Keys)})");
        report.Add($"GS1: c0001 BT_Gesture 8000 → SM {GestureSm} (hub {GestureHub}): {cats0.Count} vanilla categories {string.Join(",", cats0.Keys)}; {vanillaClips} clips (anibnd NSA id = clip index, NMB id {nmbFile.ID})");

        // DS1 clips → c0001's rig
        var ds1 = NpcIdle.LoadC0000(dsrRoot, All.Select(g => g.Ds1Anim));
        var clips = new List<Clip>();
        var net = ed.View();
        foreach (var g in All)
        {
            if (!cats0.TryGetValue(g.Template, out var t)) throw new InvalidDataException($"no vanilla gesture category {g.Template}");
            var tleaf = net.GetNode(t.Leaf);
            var sa = tleaf.Attribs.First(a => a.Type == 21);
            uint mapId = net.U32(sa.Offset + 0x78);
            if (tleaf.Attribs.Where(a => a.Type == 21).Any(a => net.U32(a.Offset + 0x78) != mapId)) throw new InvalidDataException($"category {g.Template}: anim sets use different channel maps");
            var map2 = maps[mapId];
            var src = Ds1AnimBridge.Bridge(ds1.Samples(g.Ds1Anim), 30);
            var anim = NpcIdle.Retarget(src, rig, map2, out var check);
            byte[] bytes = NsaEncoder.EncodeFile(anim).Build();
            var back = NsaCodec.Decode(NsaFile.Parse(bytes));
            NpcIdle.CompareFor(src, back, rig, map2, check);
            double enc = 0;
            for (int c = 0; c < anim.ChannelCount; c++) for (int f = 0; f < anim.Frames; f++) enc = Math.Max(enc, AngleDeg(anim.Rot[c][f], back.Rot[c][f]));
            check.EncodeMaxDeg = enc;
            uint crc = NmbNetClone.Crc32Bzip2(bytes);
            string stem = $"a74_08_{g.Ds1Anim:D4}_Ds1{new string(Letters(g).ToArray())}";
            long tplTae = TimeActOf(ed, t.Leaf);
            clips.Add(new Clip
            {
                G = g, Nsa = bytes, Back = back, Crc = crc, Name = NmbNetClone.NsaName(stem, "untitled", crc),
                Ds1Duration = (back.Frames - 1) / back.Fps, TemplateDuration = net.ClipDuration(sa), TemplateState = t.State, TemplateLeaf = t.Leaf,
                TemplateTae = tplTae, Check = check,
            });
        }
        int first = ed.AddClips(clips.Select(c => (c.Name, $"$(RootDir)\\motion_xmd\\c0001_{Ds1ChrSource.ClipName(c.G.Ds1Anim)}.xmd", "untitled", c.Crc)).ToList());
        for (int k = 0; k < clips.Count; k++) clips[k].Index = first + k;

        // new categories
        foreach (var c in clips)
        {
            var cr = ed.AddCategoryOn(CategoryCp, c.TemplateState, Category(c.G), GestureSm);
            int leaf = cr.IdMap[c.TemplateLeaf];
            var v = ed.View();
            var map = new Dictionary<uint, uint>();
            foreach (var et in v.GetNode(leaf).Attribs.Where(a => a.Type == 23))
                foreach (var (_, tid, _) in v.Tracks(et))
                {
                    if (map.ContainsKey(tid)) continue;
                    var tr = ed.Track(tid);
                    if (tr.Name != "TimeAct") continue;
                    var evs = tr.Events.Select(e => (e.Start, e.Duration, (int)TaeId(c.G))).ToList();
                    map[tid] = ed.AddTrack(tid, evs, $"lordran:c0001:gesture:{c.G.Ds1Anim}:TimeAct");
                }
            if (map.Count != 1) throw new InvalidDataException($"gesture {c.G.Index}: template leaf {c.TemplateLeaf} has {map.Count} TimeAct tracks");
            PointLeaf(ed, leaf, c.Index, c.Back, map);
            report.Add($"category {Category(c.G)} = DS1 {Goods(c.G)} {c.G.Why}: state {cr.NewSm} (clone of {c.TemplateState}, {cr.IdMap.Count} nodes), transition {cr.NewTransition}, leaf {leaf} → clip {c.Index} {c.Name} (DS1 a00_{c.G.Ds1Anim}: {c.Back.Frames} frames {c.Ds1Duration.ToString("0.###", Inv)} s; template {c.TemplateDuration.ToString("0.###", Inv)} s), TimeAct {c.TemplateTae} → {TaeId(c.G)}");
            var ck = c.Check;
            report.Add($"  retarget: {ck.Mapped}/{ck.Ds2Bones} bones, leg scale {ck.Scale.ToString("0.###", Inv)}, facing {ck.FaceYawDeg.ToString("0.#", Inv)}°, encode max {ck.EncodeMaxDeg.ToString("0.###", Inv)}°; joints (cm) {string.Join(", ", ck.JointCm.Select(kv => $"{kv.Key} {kv.Value.ToString("0.#", Inv)}"))}; DS1 TAE {NpcIdle.Ds1Tae(ds1, c.G.Ds1Anim)}");
        }
        // binder: the new NSAs take the NMB's id onwards (id = clip index), the NMB moves to the end (Ds1BossAnims' rule)
        var proto = nsas[0];
        string dir = proto.Name[..(Math.Max(proto.Name.LastIndexOf('\\'), proto.Name.LastIndexOf('/')) + 1)];
        foreach (var c in clips) bnd.Files.Insert(bnd.Files.IndexOf(nmbFile), new BinderFile(proto.Flags, c.Index, dir + c.Name, c.Nsa));
        nmbFile.ID = clips[^1].Index + 1;
        nmbFile.Bytes = ed.Build();
        byte[] outBnd = bnd.Write();
        files["morpheme4/chr/c0001.anibnd.dcx"] = comp != null ? DCX.Compress(outBnd, comp) : outBnd;
        report.Add($"anibnd: {clips.Count} NSAs as ids {first}..{clips[^1].Index}, NMB id {vanillaClips} → {nmbFile.ID}; {nmb.Bundles.Count} NMB bundles");

        // TAEs: each new animation = the template gesture's, times × DS1/template length, length = DS1's
        foreach (var kind in new[] { "pl", "sfx", "snd" })
        {
            var t = Ds2TaeFile.Read(File.ReadAllBytes(Path.Combine(ds2Data, "timeact", "chr", $"c000100_{kind}.tae")));
            var notes = new List<string>();
            foreach (var c in clips)
            {
                if (t.Find(TaeId(c.G)) != null) throw new InvalidDataException($"c000100_{kind}.tae already has {TaeId(c.G)}");
                var tpl = t.Find(c.TemplateTae);
                int frames = (int)Math.Round(c.Ds1Duration * 30);
                if (tpl == null)
                {
                    // DS2 has TimeAct values without a TAE animation (74010310 HardDelight: no _pl/_sfx/_snd entry): the game then
                    // runs no events; the new gesture gets an event-less animation of its own length
                    t.AddAnimation(TaeId(c.G), frames, []);
                    notes.Add($"{TaeId(c.G)} (template {c.TemplateTae} has none: no events, length {frames})");
                    continue;
                }
                var a = new Ds2TaeFile.Anim { Id = TaeId(c.G), Record = (byte[])tpl.Record.Clone(), Blob = (byte[])tpl.Blob.Clone(), BlobStart = tpl.BlobStart, BlobPtrs = [.. tpl.BlobPtrs] };
                double ratio = c.Ds1Duration / c.TemplateDuration;
                long pT = BinaryPrimitives.ReadInt64LittleEndian(a.Record.AsSpan(16));
                int nT = BinaryPrimitives.ReadInt32LittleEndian(a.Record.AsSpan(40));
                for (int k = 0; k < nT && pT != 0; k++)
                {
                    int o = (int)(pT - a.BlobStart) + 4 * k;
                    BinaryPrimitives.WriteSingleLittleEndian(a.Blob.AsSpan(o), (float)(BinaryPrimitives.ReadSingleLittleEndian(a.Blob.AsSpan(o)) * ratio));
                }
                long inner = BinaryPrimitives.ReadInt64LittleEndian(a.Blob.AsSpan(8));
                if (BinaryPrimitives.ReadInt32LittleEndian(a.Blob) != 0 || inner == 0) throw new InvalidDataException($"c000100_{kind} {c.TemplateTae}: not a standard anim header");
                int lenAt = (int)(inner - a.BlobStart) + 12;
                int len0 = BinaryPrimitives.ReadInt32LittleEndian(a.Blob.AsSpan(lenAt));
                BinaryPrimitives.WriteInt32LittleEndian(a.Blob.AsSpan(lenAt), frames);
                t.Anims.Add(a);
                notes.Add($"{TaeId(c.G)} ← {c.TemplateTae} ({Ds2TaeFile.Events(tpl).Count} events × {ratio.ToString("0.###", Inv)}, length {len0} → {frames})");
            }
            files[$"timeact/chr/c000100_{kind}.tae"] = t.Write();
            report.Add($"c000100_{kind}.tae: +{clips.Count} animations: {string.Join("; ", notes)}");
        }
        return (files, report);

        static IEnumerable<char> Letters(Gesture g) => g.Why.Split('←')[0].Where(char.IsLetter);
    }

    /// <summary>The TimeAct value (TAE animation id) of a leaf's TimeAct track.</summary>
    static long TimeActOf(NmbNetClone ed, int leaf)
    {
        var v = ed.View();
        foreach (var et in v.GetNode(leaf).Attribs.Where(a => a.Type == 23))
            foreach (var (_, tid, _) in v.Tracks(et))
            {
                var tr = ed.Track(tid);
                if (tr.Name == "TimeAct" && tr.Events.Count > 0) return tr.Events[0].Value;
            }
        throw new InvalidDataException($"leaf {leaf} has no TimeAct track");
    }

    /// <summary>Every anim set of leaf <paramref name="leaf"/> plays clip <paramref name="clip"/> (SourceAnim: index,
    /// trajectory, duration, whole clip; NmbNetClone.RetargetLeaf does the first set only) and its event-track lists are
    /// re-pointed through <paramref name="trackMap"/> (with the new bundle sizes).</summary>
    static void PointLeaf(NmbNetClone ed, int leaf, int clip, NsaAnim back, IReadOnlyDictionary<uint, uint> trackMap)
    {
        var x = ed.X; var net = new NmbNetwork(x);
        var n = net.GetNode(leaf);
        double d = (back.Frames - 1) / back.Fps;
        foreach (var sa in n.Attribs.Where(a => a.Type == 21))
        {
            net.PutU32(sa.Offset + 0x74, (uint)clip);
            net.PatchSourceAnim(sa, back.RootPos[0], back.RootRot[0], back.RootPos[^1], back.RootRot[^1], d);
            net.PutF32(sa.Offset + 0x80, 0f); net.PutF32(sa.Offset + 0x84, 1f);
        }
        foreach (var et in n.Attribs.Where(a => a.Type == 23))
        {
            int e = et.Offset;
            for (int k = 0; k < 3; k++)
            {
                int cnt = (int)net.U32(e + 0x10 + 0x18 * k);
                long pid = (long)net.U64(e + 0x18 + 0x18 * k), psz = (long)net.U64(e + 0x20 + 0x18 * k);
                for (int j = 0; j < cnt; j++)
                {
                    uint old = net.U32(e + (int)pid + 8 * j);
                    if (!trackMap.TryGetValue(old, out uint nw)) continue;
                    net.PutU32(e + (int)pid + 8 * j, nw);
                    if (psz != 0) net.PutU32(e + (int)psz + 4 * j, (uint)ed.Nmb.Bundles.First(b => b.AssetId == nw).Data.Length);
                }
            }
        }
        ed.X = x;
    }

    // ------------------------------------------------------------------ vanilla identity (the hard constraint)

    public sealed class Verify
    {
        public List<string> Report = [];
        public List<string> Problems = [];
    }

    /// <summary>
    /// Proves the gesture build only ADDS to the global human files (c0001.anibnd.dcx, c000100_pl/sfx/snd.tae), against
    /// vanilla in <paramref name="ds2Data"/>: (1) binder: every vanilla file (id, name, bytes) kept but the NMB, new files
    /// only NSAs at ids ≥ the vanilla clip count; (2) NMB: every vanilla bundle (type, id, GUID, data) kept but the network
    /// (10) and file-name table (13); new bundles only event tracks with ids no vanilla bundle has; (3) file names: the
    /// vanilla entries (4 tables + CRCs) are a prefix; (4) network: node count grows, every vanilla node block
    /// byte-identical except SM 7928, whose state-machine table keeps every vanilla state, condition, entry/exit list as a
    /// prefix; (5) every vanilla gesture category → same state, transition, leaf, clips; new categories only ≥ 1000;
    /// (6) nmb-runtime-check 0 violations; (7) TAE: every vanilla animation's record and blob identical up to relocation,
    /// new animations only the gesture ids.
    /// </summary>
    public static Verify VerifyAdditive(string ds2Data, IReadOnlyDictionary<string, byte[]> built)
    {
        var r = new Verify();
        void Bad(string s) => r.Problems.Add(s);
        // ---- anibnd
        var (vb, _, vn) = ReadAnibnd(File.ReadAllBytes(Path.Combine(ds2Data, "morpheme4", "chr", "c0001.anibnd.dcx")));
        var (nb, _, nn) = ReadAnibnd(built["morpheme4/chr/c0001.anibnd.dcx"]);
        var newById = nb.Files.Where(f => f != nn).ToDictionary(f => f.ID);
        int keptFiles = 0;
        foreach (var f in vb.Files.Where(f => f != vn))
            if (!newById.TryGetValue(f.ID, out var g) || g.Name != f.Name || !g.Bytes.AsSpan().SequenceEqual(f.Bytes)) Bad($"binder file {f.ID} {f.Name} changed or missing");
            else keptFiles++;
        var vNmb = NmbFile.Read(vn.Bytes); var nNmb = NmbFile.Read(nn.Bytes);
        var vNames = NmbFileNamesFull.Read(vNmb.Bundles[vNmb.IndexOf(13)].Data);
        var nNames = NmbFileNamesFull.Read(nNmb.Bundles[nNmb.IndexOf(13)].Data);
        int vClips = vNames.Files.Count;
        var added = nb.Files.Where(f => f != nn && !vb.Files.Any(o => o != vn && o.ID == f.ID)).ToList();
        foreach (var f in added) if (!f.Name.EndsWith(".nsa", StringComparison.OrdinalIgnoreCase) || f.ID < vClips) Bad($"added binder file {f.ID} {f.Name} is not a new clip");
        r.Report.Add($"verify: binder {keptFiles}/{vb.Files.Count - 1} vanilla files byte-identical, {added.Count} NSAs added (ids {(added.Count > 0 ? $"{added.Min(f => f.ID)}..{added.Max(f => f.ID)}" : "-")}), NMB id {vn.ID} → {nn.ID}");
        // ---- bundles
        var vIds = vNmb.Bundles.Select(b => b.AssetId).ToHashSet();
        int keptB = 0;
        foreach (var b in vNmb.Bundles)
        {
            var m = nNmb.Bundles.FirstOrDefault(x => x.AssetId == b.AssetId && x.Type == b.Type);
            if (m == null) { Bad($"bundle 0x{b.AssetId:X8} type {b.Type} missing"); continue; }
            if (b.Type is 10 or 13) continue;
            if (!m.Guid.AsSpan().SequenceEqual(b.Guid) || !m.Data.AsSpan().SequenceEqual(b.Data)) Bad($"vanilla bundle 0x{b.AssetId:X8} type {b.Type} changed");
            else keptB++;
        }
        var newB = nNmb.Bundles.Where(b => !vIds.Contains(b.AssetId)).ToList();
        foreach (var b in newB) if (b.Type is not (3 or 4 or 5)) Bad($"new bundle 0x{b.AssetId:X8} has type {b.Type} (only event tracks may be added)");
        r.Report.Add($"verify: NMB {keptB}/{vNmb.Bundles.Count - 2} vanilla bundles byte-identical (+ network, file names below), {newB.Count} new event-track bundles with new ids");
        // ---- file names
        bool prefix = nNames.Files.Count >= vClips;
        for (int i = 0; i < vClips && prefix; i++)
            prefix = nNames.Files[i] == vNames.Files[i] && nNames.Formats[i] == vNames.Formats[i] && nNames.Sources[i] == vNames.Sources[i] && nNames.Takes[i] == vNames.Takes[i] && nNames.Hashes[i] == vNames.Hashes[i];
        if (!prefix) Bad("file-name table: the vanilla entries are not a prefix");
        r.Report.Add($"verify: file names {vClips} vanilla entries kept as a prefix: {prefix}; {nNames.Files.Count - vClips} appended");
        // ---- network nodes
        var vx = vNmb.Bundles[vNmb.IndexOf(10)].Data; var nx = nNmb.Bundles[nNmb.IndexOf(10)].Data;
        var vItems = NmbNetEdit.BuildItems(vx).Where(i => i.Kind == "node").ToDictionary(i => i.Idx);
        var nItems = NmbNetEdit.BuildItems(nx).Where(i => i.Kind == "node").ToDictionary(i => i.Idx);
        int same = 0;
        foreach (var (idx, it) in vItems)
        {
            if (idx == GestureSm) continue;
            if (!nItems.TryGetValue(idx, out var m) || !SameBlock(it.Data, m.Data)) Bad($"vanilla node {idx} changed");
            else same++;
        }
        var vnet = new NmbNetwork(vx); var nnet = new NmbNetwork(nx);
        var vsm = NmbNetEdit.ReadSmDef(vx, vnet.GetNode(GestureSm).Attribs.First(a => a.Type == 38).Offset);
        var nsm = NmbNetEdit.ReadSmDef(nx, nnet.GetNode(GestureSm).Attribs.First(a => a.Type == 38).Offset);
        bool smOk = nsm.Default == vsm.Default && nsm.States.Count >= vsm.States.Count && nsm.Conds.Count >= vsm.Conds.Count && nnet.GetNode(GestureSm).Parent == vnet.GetNode(GestureSm).Parent;
        static bool Pre<T>(List<T> a, List<T> b) => b.Count >= a.Count && a.SequenceEqual(b.Take(a.Count));
        for (int k = 0; k < vsm.States.Count && smOk; k++)
        {
            var a = vsm.States[k]; var b = nsm.States[k];
            smOk = a.Node == b.Node && a.Src == b.Src && a.Dst == b.Dst && a.U20 == b.U20 && a.Entry.SequenceEqual(b.Entry) && Pre(a.ExitConds, b.ExitConds) && Pre(a.ExitTrans, b.ExitTrans);
        }
        for (int k = 0; k < vsm.Conds.Count && smOk; k++) smOk = vsm.Conds[k].AsSpan().SequenceEqual(nsm.Conds[k]);
        smOk &= Pre(vsm.Glob.ExitConds, nsm.Glob.ExitConds) && Pre(vsm.Glob.ExitTrans, nsm.Glob.ExitTrans);
        var vKids = vnet.GetNode(GestureSm).Children.ToList(); var nKids = nnet.GetNode(GestureSm).Children.ToList();
        smOk &= Pre(vKids, nKids);
        if (!smOk) Bad($"SM {GestureSm}: a vanilla state, condition or list entry changed");
        r.Report.Add($"verify: network nodes {vnet.NodeCount} → {nnet.NodeCount}; {same}/{vItems.Count - 1} vanilla node blocks byte-identical (all but SM {GestureSm}); SM {GestureSm}: vanilla {vsm.States.Count} records / {vsm.Conds.Count} conditions / {vKids.Count} children kept as prefixes (+{nsm.States.Count - vsm.States.Count} / +{nsm.Conds.Count - vsm.Conds.Count} / +{nKids.Count - vKids.Count}): {smOk}");
        // ---- categories
        var vc = Categories(vx); var nc = Categories(nx);
        foreach (var (val, t) in vc)
            if (!nc.TryGetValue(val, out var u) || u.State != t.State || u.Transition != t.Transition || u.Leaf != t.Leaf || !u.Clips.SequenceEqual(t.Clips)) Bad($"vanilla gesture category {val} changed");
        var newCats = nc.Keys.Except(vc.Keys).ToList();
        foreach (int val in newCats) if (val < CategoryBase) Bad($"new category {val} below {CategoryBase}");
        foreach (var g in All)
            if (!nc.TryGetValue(Category(g), out var u) || u.Clips.Any(ci => ci < vClips) || u.Leaf < vnet.NodeCount) Bad($"DS1 gesture {g.Index}: category {Category(g)} missing or not on a new leaf/clip");
        r.Report.Add($"verify: {vc.Count} vanilla categories identical (state, transition, leaf, clips); new {string.Join(",", newCats)}");
        // ---- runtime rules
        var rc = Commands.NmbRuntimeCheck.Check(nn.Bytes, "c0001 (gestures)");
        if (rc.Violations > 0) Bad($"nmb-runtime-check: {rc.Violations} violations ({string.Join("; ", rc.Samples.Where(kv => !kv.Key.StartsWith('W')).Select(kv => kv.Key + ": " + string.Join(" | ", kv.Value)))})");
        r.Report.Add($"verify: nmb-runtime-check {rc.Nodes} nodes, {rc.Violations} violations, {rc.Warnings} warnings");
        // ---- TAEs
        foreach (var kind in new[] { "pl", "sfx", "snd" })
        {
            var v = Ds2TaeFile.Read(File.ReadAllBytes(Path.Combine(ds2Data, "timeact", "chr", $"c000100_{kind}.tae")));
            var n = Ds2TaeFile.Read(built[$"timeact/chr/c000100_{kind}.tae"]);
            var nById = n.Anims.ToDictionary(a => a.Id);
            int ok = 0;
            foreach (var a in v.Anims)
                if (!nById.TryGetValue(a.Id, out var b) || !Normal(a).AsSpan().SequenceEqual(Normal(b)) || !Rel(a.Record, a.BlobStart).AsSpan().SequenceEqual(Rel(b.Record, b.BlobStart))) Bad($"c000100_{kind} animation {a.Id} changed");
                else ok++;
            var extra = n.Anims.Select(a => a.Id).Except(v.Anims.Select(a => a.Id)).Order().ToList();
            if (!extra.SequenceEqual(All.Select(g => (long)TaeId(g)).Order())) Bad($"c000100_{kind}: added animations {string.Join(",", extra)}");
            if (!n.Write().AsSpan().SequenceEqual(built[$"timeact/chr/c000100_{kind}.tae"])) Bad($"c000100_{kind}: not a stable re-emit");
            r.Report.Add($"verify: c000100_{kind}.tae {ok}/{v.Anims.Count} vanilla animations identical (record + blob up to relocation), +{extra.Count} ({string.Join(",", extra)})");
        }
        r.Report.Add(r.Problems.Count == 0 ? "verify: ADDITIVE (every vanilla clip, node, category and TAE animation byte-identical)" : $"verify: {r.Problems.Count} PROBLEMS");
        return r;

        // a node block as BuildItems cuts it runs to the next block: the emitter's 0xCD alignment padding at its end may differ
        static bool SameBlock(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            if (!a.AsSpan(0, n).SequenceEqual(b.AsSpan(0, n))) return false;
            var tail = a.Length > n ? a.AsSpan(n) : b.AsSpan(n);
            foreach (byte t in tail) if (t != 0xCD) return false;
            return tail.Length < 16;
        }
        // a TAE blob with its pointer fields made blob-relative
        static byte[] Normal(Ds2TaeFile.Anim a)
        {
            var b = (byte[])a.Blob.Clone();
            foreach (int f in a.BlobPtrs) BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(f), BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(f)) - a.BlobStart);
            return b;
        }
        static byte[] Rel(byte[] rec, long start)
        {
            var b = (byte[])rec.Clone();
            for (int f = 0; f < 4; f++) { long v = BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(8 * f)); if (v != 0) BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(8 * f), v - start); }
            return b;
        }
    }
}
