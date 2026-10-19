using System.Text.RegularExpressions;
using Lordran.Tools.Build.Chr;
using Lordran.Tools.Build.Events;
using Lordran.Tools.Build.Maps;
using Lordran.Tools.Formats;
using SoulsFormats;
using static Lordran.Tools.Build.ParamCells;
using static Lordran.Tools.Build.Talk.TalkExpr;

namespace Lordran.Tools.Build.Talk;

/// <summary>
/// DS1 NPC talk in our maps (session 8, track npc, step 2): for every placed DS1 NPC with a talk script, its DS1 talk ESD
/// translated by <see cref="TalkTranslator"/> plus everything the DS2 group names, all in our own per-map files (vanilla
/// maps untouched):
///  - talk_MAP.esd: one group per talk, id = the DS1 talk id (t181602 → group 181602);
///  - eventparam_MAP: the row that starts it, copied from Crestfallen Saulden's talk row (eventparam_m10_04 111220:
///    resourceType 1 = talk ESD, bootType 2, ownerType 2 = character, ownerId = the NPC's generator), id = the group;
///  - npctalkparam_MAP: DS1's TalkParam rows the talk plays, ids kept (DS1 chains are consecutive rows; DS2 continues
///    with the next row while bNextPlay = 1: <see cref="TalkTranslator"/>), msgId = DS1's msgId, voiceId = DS1's voiceId;
///  - menu/text/english/talk/MAP.fmg: each row's lines (DS1 Conversation_: a row shows its msgId and the entries after it
///    up to the next msgId any TalkParam row uses, as Ds1Talk.TalkLines reads them; DS2 shows one entry per row, so the
///    DS1 subtitles of a row are joined by line breaks);
///  - mapevent.fmg: DS1 dialog texts (AsylumContext's rule: DS2's own entry when the English text is the same, else
///    17000000 + id % 1000000 for talk dialogs, item placeholders &lt;?gdsparam@N?&gt; filled with the item's name since a
///    talk dialog passes no item);
///  - NpcEventParam (NPC windows): id = our NPC id (NpcPorter.Id) × 100 + k, Maughlin's row 76100000 with DS1's entries;
///    npcId = <see cref="MenuName"/> with the DS1 NPC's name in npcmenu.fmg (DS2's NPC names live there: 7410 Crestfallen
///    Saulden, 7610 Maughlin);
///  - ShopLineupParam: DS1's rows of the shop range, id <see cref="ShopRow"/>; item through the item map (duplicates →
///    DS2's item, DS1 spells via Magic), DS1's price (priceRate = DS1 value / DS2 ItemParam.price), stock (sellQuantity →
///    unitSales) and its sold flag (eventFlag → saleEndFlag);
///  - ItemLotParam2_Other: one row per DS1 gift lot row (<see cref="EntityRules.ItemLotRow"/>).
/// Gifts (DS1 common.emevd event 911: Initialize Event(slot, 911, flag, lot, 1) = "when flag turns on, award lot once"):
/// read from DS1's own common.emevd, so every NPC gift flag is known without a per-NPC table.
/// Estus gifts (rule G1, user decision session 9, <see cref="Ds1Drops.EstusShard"/>): DS1's Estus Flask (goods 200-215)
/// → DS2's Estus Flask Shard (60525000) ×1 per DS1 lot row, always given, once by the row's own DS1 item flag (the
/// earlier rule gave DS2's Estus Flask only when the player had none).
/// Deaths (fix pass, review): the NPC's DS1 death event (NpcPorter.FindDeath) and the drop rows NpcPorter routed to the
/// talk (their item flag is also a gift's, so DS2's ungated lot would hand them out a second time: Oscar's F2 East Key)
/// become the translator's death branch (<see cref="TalkTranslator.DeathSpec"/>), the rows by the same gift rule.
/// </summary>
public static class TalkBuilder
{
    public static int ShopRow(int ds1Shop) => 90_000_000 + ds1Shop;
    /// <summary>NpcEventParam.npcId (u16) = npcmenu.fmg name id for a DS1 NPC entity (6000-6999 → 56000-56999; DS2's
    /// npcmenu uses ≤ 8300 for names and 10101-49403 / 160107 / 200100 for menu texts).</summary>
    public static int MenuName(int ds1Entity) => 50_000 + ds1Entity % 10_000;
    const int MaughlinWindow = 76100000, LenigrastWindow = 76400000, SauldenTalkRow = 111220, ShopTemplate = 76100000;
    /// <summary>What a DS1 Estus Flask lot gives (rule G1): DS2's Estus Flask Shard.</summary>
    public const int Ds2Estus = Ds1Drops.EstusShard;

    /// <summary>A placed DS1 NPC with a talk: where its talk comes from (its own MSB TalkID or a talk dummy beside it) and
    /// its DS1 death (NpcPorter.FindDeath; null = none).</summary>
    public sealed record Npc(int Entity, int NpcParam, int Generator, string Name, int TalkId, string Source, float DummyOffset = 0, NpcPorter.Ds1Death? Death = null);

    /// <summary>The NPCs of a map that have a talk. DS1 rule: an NPC behind an obstacle (Anastacia behind her cell bars:
    /// own TalkID 0) talks through an invisible c1000 talk dummy (no entity) placed where the player stands (1.85 m in
    /// front of her); its TalkID is the NPC's when it stands within 3 m.</summary>
    public static List<Npc> Talkers(MSB1 msb, IEnumerable<(int Entity, int Generator, string Name, NpcPorter.Ds1Death? Death)> placed)
    {
        var list = new List<Npc>();
        foreach (var (entity, generator, name, death) in placed)
        {
            var part = msb.Parts.Enemies.FirstOrDefault(e => e.EntityID == entity);
            if (part == null) continue;
            if (part.TalkID > 0) { list.Add(new Npc(entity, part.NPCParamID, generator, name, part.TalkID, "own TalkID", 0, death)); continue; }
            var dummy = msb.Parts.Enemies.Where(e => e.ModelName == "c1000" && e.TalkID > 0 && e.EntityID <= 0)
                .OrderBy(e => System.Numerics.Vector3.Distance(e.Position, part.Position)).FirstOrDefault();
            if (dummy != null && System.Numerics.Vector3.Distance(dummy.Position, part.Position) <= 3f)
            {
                float d = System.Numerics.Vector3.Distance(dummy.Position, part.Position);
                list.Add(new Npc(entity, part.NPCParamID, generator, name, dummy.TalkID, $"talk dummy {dummy.Name} {d:0.00} m away", d, death));
            }
        }
        return list;
    }

    /// <summary>DS1 common.emevd event 0: Initialize Event(slot, 911, flag, lot, 1) = "when flag turns on, award lot once":
    /// gift flag → item lot.</summary>
    public static Dictionary<int, int> GiftLots(string dsrRoot)
    {
        var gifts = new Dictionary<int, int>();
        var common = EMEVD.Read(Path.Combine(dsrRoot, "event", "common.emevd.dcx"));
        foreach (var ins in common.Events.Where(e => e.ID == 0).SelectMany(e => e.Instructions).Where(i => i.Bank == 2000 && i.ID == 0))
        {
            var a = ins.ArgData;
            if (a.Length >= 16 && BitConverter.ToUInt32(a, 4) == 911) gifts[BitConverter.ToInt32(a, 8)] = BitConverter.ToInt32(a, 12);
        }
        return gifts;
    }

    /// <summary>A DS1 lot chain: rows lot, lot+1 … while they exist (at most 8), each given once by its getItemFlagId
    /// (Oscar's gift 1080 → 1081 → 1082, his drop 6020 → 6021 → 6022).</summary>
    public static List<int> Chain(Ds1Source ds1, int lot)
    {
        var lots = ds1.Params.Params["ItemLotParam"];
        var rows = new List<int>();
        for (int k = lot; rows.Count < 8 && lots[k] != null; k++) rows.Add(k);
        return rows;
    }

    /// <summary>The DS1 talk ESD t{id}.esd of a DS1 map's talk bnd.</summary>
    public static ESD? Ds1Talk(string dsrRoot, string ds1Map, int talkId)
    {
        string bnd = Path.Combine(dsrRoot, "script", "talk", ds1Map + ".talkesdbnd.dcx");
        if (!File.Exists(bnd)) return null;
        var f = BND3.Read(bnd).Files.FirstOrDefault(x => Path.GetFileName(x.Name).Equals($"t{talkId}.esd", StringComparison.OrdinalIgnoreCase));
        return f == null ? null : ESD.Read(f.Bytes);
    }

    /// <summary>
    /// Builds the talks of one map into the pack. <paramref name="translated"/> = the DS1 map events the build translated
    /// (AsylumEvents; null or empty: none, e.g. Firelink today), for the gift-chain rule. Returns report lines.
    /// </summary>
    public static List<string> Build(Pack pack, Ds1Source ds1, MapBuilder.Options map, IEnumerable<NpcPorter.Placed> placed,
        IReadOnlySet<long>? translated, bool voices)
    {
        var msb = MSB1.Read(Path.Combine(ds1.Root, "map", "MapStudio", map.Ds1Map + ".msb"));
        var talkers = Talkers(msb, placed.Select(p => (p.Entity, p.Custom.NewGenerator, p.Name, p.Death)));
        var mt = new MapTalk(pack, ds1, map.Ds1Map, map.Ds2Map, translated ?? new HashSet<long>());
        var report = new List<string>();
        foreach (var npc in talkers)
        {
            var esd = Ds1Talk(ds1.Root, map.Ds1Map, npc.TalkId);
            if (esd == null) { report.Add($"{map.Ds2Map} {npc.Name}: no t{npc.TalkId}.esd in {map.Ds1Map}'s talk bnd"); continue; }
            var tr = new TalkTranslator(esd, npc.TalkId, mt.For(npc));
            var states = tr.Translate();
            mt.Groups[npc.TalkId] = states;
            mt.Owners[npc.TalkId] = npc.Generator;
            string line = $"{map.Ds2Map} talk {npc.Name} (entity {npc.Entity}, generator {npc.Generator}, DS1 t{npc.TalkId} via {npc.Source}): group {npc.TalkId}, {states.Count} states ({tr.PromptCopies} extra copies of DS1 states: prompt shown / hit pending)";
            report.Add(line);
            foreach (var n in tr.Notes) report.Add("    " + n);
            foreach (var (what, count) in tr.Missing) report.Add($"    left out: {what}{(count > 1 ? $" ×{count}" : "")}");
        }
        if (mt.Groups.Count == 0) return report;
        mt.Write(map, voices, report);
        foreach (var l in report) pack.Note(l);
        return report;
    }

    /// <summary>Per-map state: rows and texts the talks of one map register, written once by <see cref="Write"/>.</summary>
    public sealed class MapTalk
    {
        readonly Pack? pack;
        readonly Ds1Source ds1;
        public readonly string Ds1Map, Ds2Map;
        readonly IReadOnlySet<long> translated;
        public readonly Dictionary<long, Dictionary<long, ESD.State>> Groups = [];
        public readonly Dictionary<long, int> Owners = [];
        /// <summary>NpcTalkParam rows: id → (msgId, voiceId, next row continues).</summary>
        public readonly SortedDictionary<int, (int Msg, int Voice, bool Next)> TalkRows = [];
        public readonly SortedDictionary<int, string> Lines = [];
        /// <summary>Session 11 (cov11): lot rows a report-only translation (no pack) would write: id → (item, number); the
        /// items are DS1 ids there (no item map without a pack). For talk-sim --ds1.</summary>
        public readonly Dictionary<int, List<(int Item, int Num)>> ReportLots = [];
        readonly Dictionary<string, int> windows = [];
        readonly Dictionary<int, int> windowCount = [];
        readonly Dictionary<int, int?> lotRows = [];
        readonly Dictionary<(int, int), int?> itemLots = [];
        readonly PARAM talkParam;
        readonly int[] msgIds;
        readonly string conversation;
        Dictionary<int, int>? gifts;
        HashSet<int>? presetByUntranslated, presetByTranslated;

        public MapTalk(Pack? pack, Ds1Source ds1, string ds1Map, string ds2Map, IReadOnlySet<long> translated)
        {
            this.pack = pack; this.ds1 = ds1; Ds1Map = ds1Map; Ds2Map = ds2Map; this.translated = translated;
            talkParam = ds1.Params.Params["TalkParam"];
            msgIds = talkParam.Rows.Select(r => I(r, "msgId")).Where(m => m > 0).Distinct().Order().ToArray();
            conversation = ds1.TextNames.FirstOrDefault(n => n.StartsWith("Conversation", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("DS1 Conversation_ FMG not found");
        }

        public TalkTranslator.IContext For(Npc npc) => new Ctx(this, npc);

        /// <summary>DS1 common event 911 instances: gift flag → item lot.</summary>
        Dictionary<int, int> Gifts() => gifts ??= GiftLots(ds1.Root);

        List<(int First, int Last, int Default)>? stateBlocks;

        /// <summary>
        /// DS1's NPC initial states, read from common.emevd event 50: pairs "SKIP IF Batch Event Flags(1, Not All OFF, first,
        /// last)" + "Set Event Flag(default, ON)" = each NPC's state block (Oscar 1060-1089 → 1060, Anastacia 1140-1169 → 1140,
        /// Petrus 1190-1209 → 1202, the Crestfallen Warrior 1460-1489 → 1460); DS1 runs it on every load.
        /// </summary>
        public IReadOnlyList<(int First, int Last, int Default)> StateBlocks(IEnumerable<int> flags)
        {
            if (stateBlocks == null)
            {
                stateBlocks = [];
                var defs = Commands.EmevdDump.Defs();
                var common = EMEVD.Read(Path.Combine(ds1.Root, "event", "common.emevd.dcx"));
                var ins = common.Events.FirstOrDefault(e => e.ID == 50)?.Instructions ?? [];
                for (int i = 0; i + 1 < ins.Count; i++)
                {
                    if (ins[i] is not { Bank: 1003, ID: 3 } || ins[i + 1] is not { Bank: 2003, ID: 2 }) continue;
                    var skip = EmevdTranslator.Decode(defs, ins[i]);
                    var set = EmevdTranslator.Decode(defs, ins[i + 1]);
                    // (skipped lines 1, state 2 = Not All OFF, flag type 0, first, last) + (flag, ON)
                    if (skip.Length >= 5 && skip[0] == 1 && skip[1] == 2 && set.Length >= 2 && set[1] == 1)
                        stateBlocks.Add(((int)skip[3], (int)skip[4], (int)set[0]));
                }
            }
            var want = flags.ToHashSet();
            return stateBlocks.Where(b => want.Any(f => f >= b.First && f <= b.Last)).ToList();
        }

        /// <summary>DS1 flags the map's DS1 events set ON (Set Event Flag 2003:2 / batch 2003:22 with literal ids) in events
        /// the build didn't translate.</summary>
        HashSet<int> PresetByUntranslated()
        {
            if (presetByUntranslated != null) return presetByUntranslated;
            presetByUntranslated = []; presetByTranslated = [];
            string path = Path.Combine(ds1.Root, "event", Ds1Map + ".emevd.dcx");
            if (!File.Exists(path)) return presetByUntranslated;
            var defs = Commands.EmevdDump.Defs();
            foreach (var ev in EMEVD.Read(path).Events)
            {
                var into = translated.Contains(ev.ID) ? presetByTranslated : presetByUntranslated;
                foreach (var ins in ev.Instructions)
                {
                    var v = EmevdTranslator.Decode(defs, ins);
                    if (ins.Bank == 2003 && ins.ID == 2 && v.Length >= 2 && v[1] == 1) into.Add((int)v[0]);
                    if (ins.Bank == 2003 && ins.ID == 22 && v.Length >= 3 && v[2] == 1)
                        for (long f = (long)v[0]; f <= (long)v[1] && f - (long)v[0] < 1000; f++) into.Add((int)f);
                }
            }
            return presetByUntranslated;
        }

        /// <summary>The same for the events the build translated (rule NT1, <see cref="Row"/>).</summary>
        HashSet<int> PresetByTranslated() { PresetByUntranslated(); return presetByTranslated!; }

        /// <summary>Rule NT1: DS2 keeps writes to this DS2 flag in the talk's map (<see cref="Ds2FlagStore"/>, V-code by track
        /// npcflags: a flag without a block reads 0 and drops writes silently, s9-12).</summary>
        bool Stored(int? ds2Flag) => ds2Flag is int g && Ds2FlagStore.Lookup(g, [Ds2Map]) != Ds2FlagStore.Kind.None;

        public int? Flag(int f) => FlagRules.Any(f);

        readonly Dictionary<int, TalkTranslator.DeathSpec?> deaths = [];
        /// <summary>The NPC's DS1 death for its talk machine (fix pass): DS1's dead flag and state block through FlagRules,
        /// the drop rows NpcPorter routed to the talk (their item flag is also a talk gift's) as gift rows.</summary>
        public TalkTranslator.DeathSpec? Death(Npc npc)
        {
            if (deaths.TryGetValue(npc.Entity, out var done)) return done;
            TalkTranslator.DeathSpec? spec = null;
            if (npc.Death is { } d && Flag(d.DeadFlag) is int dead)
            {
                var clear = Enumerable.Range(d.First, Math.Max(0, d.Last - d.First + 1)).Where(f => f != d.DeadFlag).Select(Flag).OfType<int>().ToList();
                var rows = d.TalkDropLots.Select(r => Row(r.Row, r.Chained)).ToList();
                spec = new TalkTranslator.DeathSpec(dead, clear, rows, $"DS1 event {d.Event}: flags {d.First}-{d.Last} off, {d.DeadFlag} on");
            }
            deaths[npc.Entity] = spec;
            return spec;
        }

        // ---------------------------------------------------------------- talk rows and lines
        /// <summary>DS1 TalkToPlayer(t): the TalkParam chain t, t+1 … (consecutive rows); a talk id without a row is the
        /// row whose msgId is t (Oscar's 12000009 = row 12000005's line: DS1 plays that line there).</summary>
        public int? Talk(int t)
        {
            if (TalkRows.ContainsKey(t)) return t;
            var chain = new List<PARAM.Row>();
            for (int id = t; chain.Count < 64 && talkParam[id] is { } row; id++) chain.Add(row);
            if (chain.Count == 0)
            {
                var byMsg = talkParam.Rows.FirstOrDefault(r => I(r, "msgId") == t);
                if (byMsg == null) return null;
                Register(t, byMsg, false);
                return t;
            }
            for (int k = 0; k < chain.Count; k++)
                if (!TalkRows.ContainsKey(chain[k].ID)) Register(chain[k].ID, chain[k], k + 1 < chain.Count);
            return t;
        }

        void Register(int id, PARAM.Row row, bool next)
        {
            int msg = I(row, "msgId");
            TalkRows[id] = (msg, I(row, "voiceId"), next);
            if (!Lines.ContainsKey(msg)) Lines[msg] = Text(msg);
        }

        /// <summary>A row's English subtitles: msgId and the Conversation_ entries after it up to the next msgId any
        /// TalkParam row uses (at most 16), joined by line breaks.</summary>
        string Text(int msg)
        {
            int next = Array.BinarySearch(msgIds, msg + 1);
            if (next < 0) next = ~next;
            int end = next < msgIds.Length ? Math.Min(msgIds[next], msg + 16) : msg + 1;
            var parts = new List<string>();
            for (int m = msg; m < end && ds1.Text(conversation, m) is { Length: > 0 } t; m++) parts.Add(t.Replace("\r", "").Trim('\n'));
            return parts.Count > 0 ? string.Join("\n", parts) : $"[DS1 line {msg}]";
        }

        // ---------------------------------------------------------------- dialogs
        static FMG? ds2MapEvent;

        public int? Dialog(int m)
        {
            string text = ds1.EventText(m);
            if (string.IsNullOrEmpty(text)) return null;
            string filled = Regex.Replace(text, @"<\?gdsparam@(\d+)\?>", x => Ds1GoodsName(int.Parse(x.Groups[1].Value)));
            ds2MapEvent ??= FMG.Read(Path.Combine(Commands.ExtractDs2.DefaultOut, "menu", "text", "english", "mapevent.fmg"));
            var same = ds2MapEvent.Entries.Where(e => e.Text?.Trim() == filled.Trim()).OrderBy(e => e.ID).FirstOrDefault();
            if (same != null) return same.ID;
            int id = 17_000_000 + m % 1_000_000;
            pack?.SetText("mapevent", id, filled);
            return id;
        }

        string Ds1GoodsName(int id)
        {
            foreach (string fmg in new[] { "Item_name_", "Goods_name_" })
                if (ds1.TextNames.Contains(fmg) && ds1.Text(fmg, id) is { Length: > 0 } t) return t;
            return ds1.Params.RowName("EquipParamGoods", id) is { Length: > 0 } n ? n : $"item {id}";
        }

        // ---------------------------------------------------------------- items, gifts
        static uint LotCategory(int talkCategory) => talkCategory switch { 0 => 0u, 1 => 0x10000000u, 2 => 0x20000000u, _ => 0x40000000u };

        public static bool IsEstus(uint category, int id) => Ds1Drops.IsEstus(category, id);

        public int? Item(int category, int id)
        {
            if (IsEstus(LotCategory(category), id)) return Ds2Estus;
            if (pack == null) return id; // report: no item map writes
            int ds2 = Ds1Drops.MapItem(pack, ds1, LotCategory(category), id, out _, out _, out _);
            return ds2 > 0 ? ds2 : null;
        }

        public IReadOnlyList<TalkTranslator.GiftRow>? Gift(int flag)
        {
            if (!Gifts().TryGetValue(flag, out int lot)) return null;
            return Chain(ds1, lot).Select(k => Row(k, k > lot)).ToList();
        }

        /// <summary>One DS1 lot row of a chain as a gift row: its flag, the DS1 "not this time" skip (a chained row whose
        /// flag a DS1 map event pre-sets, when that pre-set doesn't reach DS2: the event is untranslated, or (rule NT1,
        /// chrint) it is translated but DS2 has no storage for the flag, so its write is dropped and the row would be given
        /// on DS1's "not this time" route: Oscar's Big Pilgrim's Key 1081 / death drop 6021, flag 50000081 → 109500081 set by
        /// the boss's 11810300), DS2's Estus rule.</summary>
        TalkTranslator.GiftRow Row(int k, bool chained)
        {
            var r = ds1.Params.Params["ItemLotParam"][k]!;
            int itemFlag = I(r, "getItemFlagId");
            int? f = itemFlag > 0 ? FlagRules.Any(itemFlag) : null;
            bool skip = chained && itemFlag > 0 && PresetByUntranslated().Contains(itemFlag);
            bool unstored = chained && itemFlag > 0 && !skip && PresetByTranslated().Contains(itemFlag) && !Stored(f);
            skip |= unstored;
            int item = I(r, "lotItemId01");
            uint cat = unchecked((uint)Convert.ToInt64(r["lotItemCategory01"].Value));
            bool estus = IsEstus(cat, item);
            int? ds2Lot = LotRow(r);
            string what = $"DS1 lot row {k} ({ds1.Params.RowName("ItemLotParam", k)})";
            string note = unstored ? $"{what}: left out, DS1 pre-sets its flag {itemFlag} in a translated map event but DS2 has no storage for {(f is int g ? g.ToString() : "it")} (the write is dropped; rule NT1, DS1's 'not this time')"
                : skip ? $"{what}: left out, DS1 pre-sets its flag {itemFlag} in a map event this build doesn't translate (DS1's 'not this time')"
                : estus ? $"{what} → DS2's Estus Flask Shard x1 (rule G1, user decision)"
                : ds2Lot == null ? $"{what}: item not in DS2 yet" : what;
            return new TalkTranslator.GiftRow(k, ds2Lot, f, skip, null, note);
        }

        /// <summary>One DS1 ItemLotParam row (no chain) → ItemLotParam2_Other (EntityRules.ItemLotRow), null = no item in DS2.</summary>
        int? LotRow(PARAM.Row r)
        {
            if (lotRows.TryGetValue(r.ID, out var done)) return done;
            var items = new List<(int Item, int Num, int Reinforce, float Percent)>();
            var entries = Enumerable.Range(1, 8).Select(i => (Id: I(r, $"lotItemId0{i}"), Cat: unchecked((uint)Convert.ToInt64(r[$"lotItemCategory0{i}"].Value)),
                Points: Get(r, $"lotItemBasePoint0{i}"), Num: I(r, $"lotItemNum0{i}"))).Where(x => x.Points > 0 && x.Id > 0).ToList();
            double total = entries.Sum(x => x.Points);
            foreach (var x in entries)
            {
                int reinforce = 0, ds2;
                int num = Math.Max(1, x.Num);
                if (IsEstus(x.Cat, x.Id)) { ds2 = Ds2Estus; num = 1; }
                else ds2 = pack != null ? Ds1Drops.MapItem(pack, ds1, x.Cat, x.Id, out _, out reinforce, out _) : x.Id;
                if (ds2 == 0) { pack?.Note($"talk gift lot {r.ID}: DS1 item {x.Id} not in DS2 yet, left out"); continue; }
                items.Add((ds2, num, reinforce, entries.Count == 1 ? 100f : (float)(100 * x.Points / total)));
            }
            int? id = items.Count > 0 ? EntityRules.ItemLotRow(r.ID) : null;
            if (id != null) WriteLot(id.Value, $"[Lordran] DS1 talk gift lot row {r.ID}", items);
            lotRows[r.ID] = id;
            return id;
        }

        public int? LotFor(int ds2Item, int num)
        {
            if (itemLots.TryGetValue((ds2Item, num), out var done)) return done;
            int id = EntityRules.ItemLotRow(9_000_000 + itemLots.Count + 1); // GiveItem without a DS1 lot: 1,109,000,001 …
            WriteLot(id, $"[Lordran] DS1 talk GiveItem {ds2Item} x{num}", [(ds2Item, num, 0, 100f)]);
            itemLots[(ds2Item, num)] = id;
            return id;
        }

        void WriteLot(int id, string name, List<(int Item, int Num, int Reinforce, float Percent)> items)
        {
            if (pack == null) { ReportLots[id] = items.Select(x => (x.Item, x.Num)).ToList(); return; } // session 11 (cov11): talk-sim --ds1 gives them
            var other = pack.Param("ItemLotParam2_Other");
            var row = pack.AddRow("ItemLotParam2_Other", other[60042000]!, id, name);
            bool sure = items.All(x => x.Percent >= 100);
            items = items.Take(10).ToList();
            for (int i = 1; i <= 10; i++)
            {
                bool used = i <= items.Count;
                var it = used ? items[i - 1] : default;
                Set(row, $"itemId{i}", used ? it.Item : 10);
                Set(row, $"itemNum{i}", used ? it.Num : 0);
                Set(row, $"reinforceLevel{i}", used ? it.Reinforce : 0);
                Set(row, $"attribute{i}", 0);
                Set(row, $"bExFlag{i}", used ? 1 : 0);
                Set(row, $"itemWeight{i}", used ? (sure ? 1 : it.Percent) : 0);
            }
            Set(row, "lotCount", sure ? items.Count : 1);
            pack.Note($"{name} → ItemLotParam2_Other {id}: " + string.Join(", ", items.Select(x => $"{x.Item} x{x.Num}")));
        }

        // ---------------------------------------------------------------- prompts
        static FMG? ds2KeyGuide;
        readonly Dictionary<(int, int), int> keyGuides = [];

        /// <summary>
        /// DS1 action-button text → KeyGuideParam: DS2's talk prompt 600 ("Talk": textId 501, 1.8 m cylinder, 80° facing)
        /// for DS1's 10010200 "Talk"; another DS1 text (Anastacia's corpse "Examine" 10010100) → a copy of 600 with the
        /// keyguide.fmg entry of the same English text ("⑲:Examine" 100). A talk through a DS1 talk dummy (the NPC behind
        /// bars) widens the region by the dummy's distance, so the player can stand where DS1's dummy stood. Copies:
        /// 90,000,000 + talk id % 100,000 × 10 + k.
        /// </summary>
        public int? KeyGuide(Npc npc, int ds1Text)
        {
            string want = ds1.EventText(ds1Text).Trim();
            if (want.Length == 0) return null;
            ds2KeyGuide ??= FMG.Read(Path.Combine(Commands.ExtractDs2.DefaultOut, "menu", "text", "english", "keyguide.fmg"));
            int? textId = ds1Text == 10010200 ? 501 : ds2KeyGuide.Entries.Where(e => e.Text != null && Regex.Replace(e.Text, @"^[^:]*:", "").Trim() == want).OrderBy(e => e.ID).Select(e => (int?)e.ID).FirstOrDefault();
            if (textId == null) return null;
            if (textId == 501 && npc.DummyOffset <= 0) return 600;
            if (keyGuides.TryGetValue((npc.TalkId, textId.Value), out int done)) return done;
            int id = 90_000_000 + npc.TalkId % 100_000 * 10 + keyGuides.Keys.Count(k => k.Item1 == npc.TalkId);
            keyGuides[(npc.TalkId, textId.Value)] = id;
            if (pack != null)
            {
                var p = pack.Param("KeyGuideParam");
                var row = pack.AddRow("KeyGuideParam", p[600]!, id, $"[Lordran] {npc.Name} prompt {want}");
                Set(row, "textId", textId.Value);
                Set(row, "width_radius", Get(p[600]!, "width_radius") + npc.DummyOffset);
            }
            return id;
        }

        // ---------------------------------------------------------------- NPC windows and shops
        public int? Window(Npc npc, TalkTranslator.WindowSpec spec)
        {
            // a shop DS1 opens outside its list (an unreachable leftover once the list runs the shop inside the window):
            // the list's window that carries the same shop
            if (spec.ListState < 0 && windows.Keys.FirstOrDefault(k => k.StartsWith($"{npc.Entity}:") && k.Contains($":{spec.ShopFirst}:{spec.ShopLast}:")) is { } same)
                return windows[same];
            string key = $"{npc.Entity}:{spec.Talk}:{spec.ShopFirst}:{spec.ShopLast}:{spec.Repair}:{spec.Fortify}:{spec.Pledge}:{spec.Votive}"; // session 11 (cov11): + covenant buttons
            if (windows.TryGetValue(key, out int done)) return done;
            int k = windowCount.GetValueOrDefault(npc.Entity);
            windowCount[npc.Entity] = k + 1;
            int id = NpcPorter.Id(npc.NpcParam) * 100 + k;
            windows[key] = id;
            if (pack == null) return id;
            var param = pack.Param("NpcEventParam");
            var row = pack.AddRow("NpcEventParam", param[MaughlinWindow]!, id, $"[Lordran] {npc.Name} window");
            foreach (var cell in row.Cells.Where(c => c.Def.InternalName.StartsWith('b') && c.Value is byte))
                cell.Value = (byte)0;
            Set(row, "pledgeType", 0);
            Set(row, "bTalk", spec.Talk ? 1 : 0);
            Set(row, "bShop", spec.Shop ? 1 : 0);
            Set(row, "bRepair", spec.Repair ? 1 : 0);
            Set(row, "bFortify", spec.Fortify ? 1 : 0);
            Set(row, "fortifyTyoe", spec.Fortify ? I(param[LenigrastWindow]!, "fortifyTyoe") : 0);
            Set(row, "shopIdBegin", spec.Shop ? ShopRow(spec.ShopFirst) : 0);
            Set(row, "shopIdEnd", spec.Shop ? ShopRow(spec.ShopLast) : 0);
            foreach (string f in new[] { "votiveItemId", "barterParamBeginId", "barterParamEndId", "gestureId", "eventValueChangeType", "eventValueId", "pluralSelectIdBegin", "pluralSelectIdEnd" })
                Set(row, f, 0);
            // session 11 (cov11), rule K1b: DS1's "Enter Covenant" / "Offer …" entries as DS2's pledge / votive buttons (the talk
            // runs DS1's join / offer states on window result 5 / 6, as vanilla's Altar of Sunlight 78602000)
            Set(row, "bPledge", spec.Pledge > 0 ? 1 : 0);
            Set(row, "pledgeType", spec.Pledge);
            Set(row, "bVotive", spec.Votive > 0 ? 1 : 0);
            Set(row, "votiveItemId", spec.Votive);
            int menuName = MenuName(npc.Entity);
            Set(row, "npcId", menuName);
            pack.SetText("npcmenu", menuName, npc.Name);
            if (spec.Shop) Shop(npc, spec.ShopFirst, spec.ShopLast);
            return id;
        }

        void Shop(Npc npc, int first, int last)
        {
            var dsShop = ds1.Params.Params["ShopLineupParam"];
            var shop = pack!.Param("ShopLineupParam");
            var items = pack.Param("ItemParam");
            int added = 0;
            foreach (var r in dsShop.Rows.Where(r => r.ID >= first && r.ID <= last).OrderBy(r => r.ID))
            {
                int equipType = I(r, "equipType"), equipId = I(r, "equipId");
                int ds2 = Ds1Drops.MapItem(pack, ds1, LotCategory(equipType), equipId, out _, out _, out string how);
                string name = ds1.Params.RowName("ShopLineupParam", r.ID);
                if (ds2 == 0 || items[ds2] == null) { pack.Note($"{Ds2Map} shop {npc.Name}: DS1 row {r.ID} {name} (type {equipType} id {equipId}) not in DS2 yet, left out"); continue; }
                var row = pack.AddRow("ShopLineupParam", shop[ShopTemplate]!, ShopRow(r.ID), $"[Lordran] {name}");
                Set(row, "saleItem", ds2);
                int price = I(items[ds2]!, "price"), value = I(r, "value");
                Set(row, "priceRate", price > 0 ? (double)value / price : 1);
                int quantity = I(r, "sellQuantity"), soldFlag = I(r, "eventFlag");
                Set(row, "unitSales", Math.Clamp(quantity, -1, 127));
                Set(row, "saleFlag", -1);
                Set(row, "saleEndFlag", quantity > 0 && soldFlag > 0 && Flag(soldFlag) is int f ? f : -1);
                Set(row, "displayOrder", 0);
                if (I(r, "mtrlId") > 0) pack.Note($"{Ds2Map} shop {npc.Name}: DS1 row {r.ID} also costs material {I(r, "mtrlId")} (not carried)");
                added++;
                pack.Note($"{Ds2Map} shop {npc.Name}: DS1 {r.ID} {name} → ShopLineupParam {ShopRow(r.ID)} item {ds2} ({how}) DS1 price {value} (DS2 base {price}), stock {quantity}");
            }
            pack.Note($"{Ds2Map} shop {npc.Name}: {added} rows {ShopRow(first)}..{ShopRow(last)}");
        }

        // ---------------------------------------------------------------- write
        public void Write(MapBuilder.Options map, bool voices, List<string> report)
        {
            if (pack == null) return;
            string data = pack.Ds2Data, tId = map.TemplateMap, nId = map.Ds2Map;
            var defs = Directory.GetFiles(Path.Combine(ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();

            // talk ESD: our groups replace MapBuilder's placeholder machine
            var esd = ESD.Read(pack.GetFile("gamedata", $"ezstate/talk_{nId}.esd"));
            esd.StateGroups.Remove(0x7FFFFFFF);
            foreach (var (gid, states) in Groups)
            {
                if (states.Count < 2) throw new InvalidDataException($"talk group {gid}: {states.Count} state(s)");
                if (states.TryGetValue(0, out var s0) && s0.EntryCommands.Count > 0) throw new InvalidDataException($"talk group {gid}: state 0 has entry commands");
                esd.StateGroups[gid] = states;
            }
            pack.AddFile("gamedata", $"ezstate/talk_{nId}.esd", esd.Write());

            // EventParam: Saulden's talk row per group (the map's event rows are already there)
            var events = PARAM.Read(pack.GetFile("gamedata", $"param/eventparam_{nId}.param"));
            if (!events.ApplyParamdefCarefully(defs)) throw new InvalidDataException("no paramdef fits EventParam");
            var sauldenRows = PARAM.Read(Path.Combine(data, "param", "eventparam_m10_04_00_00.param"));
            sauldenRows.ApplyParamdefCarefully(defs);
            var template = sauldenRows.Rows.Single(r => r.ID == SauldenTalkRow);
            foreach (var (gid, owner) in Owners)
            {
                if (events.Rows.Any(r => r.ID == gid)) throw new InvalidDataException($"EventParam {gid} already used in {nId}");
                var row = new PARAM.Row(template) { ID = (int)gid };
                Set(row, "stateMapId", gid);
                Set(row, "ownerId", owner);
                events.Rows.Add(row);
            }
            events.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
            pack.AddFile("gamedata", $"param/eventparam_{nId}.param", events.Write());

            // NpcTalkParam: the template map's file with our rows (MapBuilder wrote it empty; SoulsFormats can't read a
            // row-less param back, so it is rebuilt from the template)
            var npcTalk = PARAM.Read(Path.Combine(data, "param", $"npctalkparam_{tId}.param"));
            if (!npcTalk.ApplyParamdefCarefully(defs)) throw new InvalidDataException("no paramdef fits NpcTalkParam");
            var talkTemplate = npcTalk.Rows[0];
            npcTalk.Rows.Clear();
            foreach (var (id, (msg, voice, next)) in TalkRows)
            {
                var row = new PARAM.Row(talkTemplate) { ID = id };
                Set(row, "msgId", msg);
                Set(row, "voiceId", voice);
                Set(row, "actionId", -1);
                Set(row, "windowType", 0);
                Set(row, "bNextPlay", next ? 1 : 0);
                Set(row, "changeNameNpcId", 0);
                Set(row, "endWaitTime", 0);
                npcTalk.Rows.Add(row);
            }
            pack.AddFile("gamedata", $"param/npctalkparam_{nId}.param", npcTalk.Write());

            // talk FMG
            var fmg = FMG.Read(Path.Combine(data, "menu", "text", "english", "talk", $"{tId}.fmg"));
            fmg.Entries.Clear();
            foreach (var (id, text) in Lines) fmg.Entries.Add(new FMG.Entry(id, text));
            pack.AddFile("gamedata", $"menu/text/english/talk/{nId}.fmg", fmg.Write());

            report.Add($"{nId} talk files: {Groups.Count} groups, {Owners.Count} EventParam rows, {TalkRows.Count} NpcTalkParam rows, {Lines.Count} talk FMG lines, {windows.Count} NPC windows");
            if (voices) report.AddRange(TalkVoices.Build(pack, ds1.Root, Ds1Map, nId, TalkRows.Values.Select(v => v.Voice).Distinct().ToList(), map.Ds1Sound));
        }

        sealed class Ctx(MapTalk mt, Npc npc) : TalkTranslator.IContext
        {
            public string Ds1Map => mt.Ds1Map;
            public int? Flag(int f) => mt.Flag(f);
            public TalkTranslator.DeathSpec? Death => mt.Death(npc);
            public int? Talk(int t) => mt.Talk(t);
            public int? Dialog(int m) => mt.Dialog(m);
            public int? Item(int category, int id) => mt.Item(category, id);
            public IReadOnlyList<TalkTranslator.GiftRow>? Gift(int f) => mt.Gift(f);
            public int? Window(TalkTranslator.WindowSpec spec) => mt.Window(npc, spec);
            public int? LotFor(int item, int num) => mt.LotFor(item, num);
            public string EventText(int id) => mt.ds1.EventText(id);
            public IReadOnlyList<(int First, int Last, int Default)> StateBlocks(IEnumerable<int> flags) => mt.StateBlocks(flags);
            public int? KeyGuide(int text) => mt.KeyGuide(npc, text);
            public float DistanceOffset => npc.DummyOffset;
        }
    }
}
