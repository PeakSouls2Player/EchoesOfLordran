using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Events;

/// <summary>
/// Session 10 (asy-items i2), rules S1 / S1-R / S2: DS1's "drop an item in an area → trade" (Snuggly's nest) as DS2's
/// give-item prompt.
///
/// DS1 (V-data, m18_01 11810641 ×22 slots + 11815110 ×21): `3:14 IF Dropped Items In Area(area)` (MAIN) and `3:15 IF
/// Dropped Item(type, id)` (AND_02); no match → flag A (11815201, Snuggly's "No, no"); match and the slot's done flag
/// OFF → flag B (11815200) + `2003:33 Set Snuggly Next Trade(trigger)`; done → flag C (11815202). The partner event
/// (11815110: trigger ON, done OFF → `2003:34 Spawn Snuggly Item(lot, …)`) gives the reward lot, whose getItemFlagId is
/// the done flag. DS1's 11810600 turns A/B/C into Snuggly's talk flags (translates by the existing rules).
///
/// DS2 has no drop trade: group condition 140803 (handler 0x14046D8DC) only tests "an item with this id lies anywhere
/// on the map" (no area, no removal command; one vanilla use, m20_11) → a drop trade on it would let the player pick
/// the item up again (S1b, rejected; spec messages-items §3.2, V-code). DS2's own way to hand an item over (V-data):
/// count check `f131401(item, 1, 1, 0) >= n` (27 vanilla uses), the YES/NO window with the item's name
/// `130441(text, 0, 190, 2, item, 0)` (m10_23 / m10_31 use-item groups; mapevent 1200 "Give\n%s?"), consume
/// `131401(item, n)` (29 uses), reward `131402(lot, 1)` (rule 2003:36, V s9-12). The action button is DS2's key-guide
/// helper (rulebook §3 action-button rule, V) with keyguide 504 "⑲:Offer".
///
/// <b>S1</b>: one group per DS1 trade area (<see cref="Group"/> + n): helper on the area (EntityRules.AreaHelper(area,
/// 0)); on "Offer", for each emitted slot in DS1 order whose done flag is OFF and whose DS2 input the player holds:
/// "Give %s?" → YES: consume, flag B, the trigger, the reward lot and the done flag (DS2 gives the reward at once: DS2
/// has no "item appears in the nest at the next load"; the partner event still covers an interrupted trade at load,
/// rule S2) → NO: the next slot. Nothing offerable: flag C if the player holds a done slot's item, else flag A.
/// Slots with the same DS2 input, trigger and done flag are one offer (DS1's Humanity 350 / 500 → Human Effigy).
/// <b>S1-R</b>: a slot is emitted iff its input AND every item of its reward lot map to DS2 items in this pack (item map,
/// porters, RingPorter on demand); otherwise "pending: …" in the build log. Never a trade that takes an item and gives
/// nothing. An unported input item: the slot is left out (the player can't hold that item in DS2 anyway); it appears by
/// itself once its porter lands. <b>Host only</b> (DS1: event 0 doesn't start these for a client): the group runs while
/// `f130602() == 0` (vanilla's guest test; offline always true).
/// <b>S2</b>: `2003:33 Set Snuggly Next Trade(f)` = set f; `2003:34 Spawn Snuggly Item(lot, …)` = award the lot + set its
/// getItemFlagId (EmevdTranslator). Generalises: DS1 has this pattern only in the Asylum (Snuggly); any map whose DS1
/// script has it gets the group from DS1 data alone.
/// </summary>
public static class DropTrades
{
    /// <summary>ESD group / EventParam row of the n-th trade area of a map (rule-made groups 37xxx; 37420+ unused by every track).</summary>
    public const int Group = 37420;
    /// <summary>DS2 keyguide text "⑲:Offer" (keyguide.fmg 504) and the YES/NO question "Give\n%s?" (mapevent 1200).</summary>
    public const int OfferText = 504, GiveText = 1200;
    /// <summary>The question for a count above 1 ("Give\n%s x2?"): new mapevent row 18001200 + count.</summary>
    public static int GiveTextFor(int count) => count <= 1 ? GiveText : 18_001_200 + count;
    /// <summary>The helper's model and KeyGuideParam row (n-th trade area of the map).</summary>
    public static string Model(int n) => $"o18_93{90 + n:D2}";
    public static int KeyGuide(int ds2MapPrefix, int n) => ds2MapPrefix * 10000 + 9200 + n;

    public sealed record Slot(int K, int Ds1Type, int Ds1Item, int Trigger, int Done, int Lot);
    public sealed record Area(int Entity, int EventId, int NoMatchFlag, int AcceptFlag, int DoneFlagSet, List<Slot> Slots);

    /// <summary>DS1 drop-trade events of a map script, with their slots joined to the reward lots of the 2003:34 events.</summary>
    public static List<Area> Find(EMEVD emevd, Dictionary<(int, int), (string Name, int[] Types)> defs)
    {
        var areas = new List<Area>();
        var byId = emevd.Events.ToDictionary(e => e.ID);
        var inits = emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2000 && i.ID == 0)
            .Select(i => (Id: (long)BitConverter.ToUInt32(i.ArgData, 4), Slot: BitConverter.ToInt32(i.ArgData, 0), Args: i.ArgData[8..])).ToList();
        // the partner: trigger flag → lot (2003:34 in an event whose slots pass the trigger)
        var lotOf = new Dictionary<int, int>();
        foreach (var ev in emevd.Events.Where(e => e.Instructions.Any(i => i is { Bank: 2003, ID: 34 })))
            foreach (var c in inits.Where(c => c.Id == ev.ID))
            {
                int at = ev.Instructions.FindIndex(i => i is { Bank: 2003, ID: 34 });
                var v = EmevdTranslator.Decode(defs, EmevdTranslator.Substitute(ev, at, ev.Instructions[at], c.Args));
                lotOf[(int)v[2]] = (int)v[0]; // (lot, area, trigger flag, hitbox)
            }
        foreach (var ev in emevd.Events)
        {
            var ins = ev.Instructions;
            int dropArea = ins.FindIndex(i => i is { Bank: 3, ID: 14 }), dropped = ins.FindIndex(i => i is { Bank: 3, ID: 15 }), next = ins.FindIndex(i => i is { Bank: 2003, ID: 33 });
            if (dropArea < 0 || dropped < 0 || next < 0) continue;
            var sets = ins.Select((x, i) => (x, i)).Where(p => p.x is { Bank: 2003, ID: 2 }).Select(p => (int)EmevdTranslator.Decode(defs, p.x)[0]).ToList();
            int doneTest = ins.FindIndex(i => i is { Bank: 1003, ID: 1 });
            if (sets.Count != 3 || doneTest < 0) throw new InvalidDataException($"event {ev.ID}: drop trade pattern not recognised (3 flag sets and a done-flag SKIP expected)");
            int area = (int)EmevdTranslator.Decode(defs, ins[dropArea])[1];
            var slots = new List<Slot>();
            foreach (var c in inits.Where(c => c.Id == ev.ID).OrderBy(c => c.Slot))
            {
                var d = EmevdTranslator.Decode(defs, EmevdTranslator.Substitute(ev, dropped, ins[dropped], c.Args)); // (group, type, id)
                int trigger = (int)EmevdTranslator.Decode(defs, EmevdTranslator.Substitute(ev, next, ins[next], c.Args))[0];
                int done = (int)EmevdTranslator.Decode(defs, EmevdTranslator.Substitute(ev, doneTest, ins[doneTest], c.Args))[3];
                slots.Add(new Slot(c.Slot, (int)d[1], (int)d[2], trigger, done, lotOf.GetValueOrDefault(trigger)));
            }
            areas.Add(new Area(area, (int)ev.ID, sets[0], sets[1], sets[2], slots));
        }
        return areas;
    }

    static readonly Dictionary<string, List<Area>> cache = [];
    /// <summary>The drop-trade areas of a DS1 map (cached).</summary>
    public static List<Area> Of(string dsrRoot, string ds1Map)
    {
        if (cache.TryGetValue(ds1Map, out var a)) return a;
        var emevd = EMEVD.Read(Path.Combine(dsrRoot, "event", ds1Map + ".emevd.dcx"));
        return cache[ds1Map] = Find(emevd, Commands.EmevdDump.Defs());
    }

    /// <summary>DS1 3:15 ItemType → ItemLotParam category (0 weapon, 1 armor, 2 ring, 3 goods).</summary>
    public static uint Category(int ds1Type) => ds1Type switch { 0 => 0u, 1 => 0x10000000u, 2 => 0x20000000u, _ => 0x40000000u };

    /// <summary>The key-guide helpers (MapBuilder placements): one per trade area, at the DS1 area, facing like it.</summary>
    public static IEnumerable<Maps.MapBuilder.Placement> Placements(MSB1 msb1, string dsrRoot, string ds1Map)
    {
        int n = 0;
        foreach (var a in Of(dsrRoot, ds1Map))
        {
            var r = msb1.Regions.GetEntries().First(x => x.EntityID == a.Entity);
            yield return new Maps.MapBuilder.Placement(Model(n), r.Position, r.Rotation, EntityRules.AreaHelper(a.Entity, 0), Maps.MapBuilder.PlacementKind.Generic, 0,
                $"(DS1 region {r.Name}, entity {a.Entity}) key guide \"Offer\" for DS1's drop trade {a.EventId} (rule S1)");
            n++;
        }
    }

    /// <summary>Regulation rows: the helper model (o04_9300 clone) and its KeyGuideParam (sphere like DS2's torch guide
    /// 1110, radius = the area's half diagonal + 1 m, text 504 "⑲:Offer"); the "x2" question rows.</summary>
    public static void Params(Pack pack, Ds1Source ds1, MSB1 msb1, string ds1Map, int ds2MapPrefix)
    {
        int n = 0;
        foreach (var a in Of(ds1.Root, ds1Map))
        {
            var r = msb1.Regions.GetEntries().First(x => x.EntityID == a.Entity);
            float radius = r.Shape is MSB.Shape.Box box ? MathF.Sqrt(box.Width * box.Width + box.Depth * box.Depth) / 2 + 1f : 2f;
            int kg = KeyGuide(ds2MapPrefix, n);
            var row = pack.AddRow("KeyGuideParam", pack.Param("KeyGuideParam")[1110]!, kg, $"[Lordran] drop trade {a.EventId}: Offer");
            row["textId"].Value = Convert.ChangeType(OfferText, row["textId"].Value.GetType());
            row["width_radius"].Value = radius;
            Maps.ObjPorter.CloneDs2(pack, "o04_9300", Model(n), 10049040, (uint)kg);
            n++;
        }
        pack.SetText("mapevent", GiveTextFor(2), "Give\n%s x2?");
    }

    /// <summary>Builds the groups (S1/S1-R) after the map's events were translated; returns notes for the report.</summary>
    public static List<string> Add(Pack pack, Ds1Source ds1, ESD esd, PARAM events, PARAM.Row ownerless, string ds1Map, EmevdTranslator.IContext ctx)
    {
        var notes = new List<string>();
        int n = 0;
        foreach (var a in Of(ds1.Root, ds1Map))
        {
            int gid = Group + n;
            if (esd.StateGroups.ContainsKey(gid) || events.Rows.Any(r => r.ID == gid)) throw new InvalidDataException($"{ds1Map}: ESD group / EventParam row {gid} is already used");
            int helper = EntityRules.AreaHelper(a.Entity, 0);
            int? F(int f) => ctx.Flag(f);
            var offers = new List<(int Item, int Count, int Trigger, int Done, int Lot, string Label)>();
            var pending = new List<string>();
            foreach (var s in a.Slots)
            {
                string label = $"slot {s.K} (DS1 {(s.Ds1Type switch { 0 => "weapon", 1 => "armor", 2 => "ring", _ => "goods" })} {s.Ds1Item} '{ds1.Text(s.Ds1Type switch { 0 => "Weapon_name_", 1 => "Armor_name_", 2 => "Accessory_name_", _ => "Item_name_" }, s.Ds1Item)}' → lot {s.Lot})";
                int input = Chr.Ds1Drops.MapItem(pack, ds1, Category(s.Ds1Type), s.Ds1Item, out _, out _, out _);
                int count = s.Ds1Type == 3 ? Chr.Ds1Drops.SystemQuantity(ds1.Text("Item_name_", s.Ds1Item)) : 1;
                var missing = new List<string>();
                if (input == 0) missing.Add("input not in DS2 yet");
                foreach (var (cat, id) in LotItems(ds1, s.Lot))
                    if (Chr.Ds1Drops.MapItem(pack, ds1, cat, id, out string kind, out _, out _) == 0) missing.Add($"reward {kind} {id} '{ds1.AnyText(id)}' not in DS2 yet");
                int? lot = missing.Count == 0 ? ctx.ItemLot(s.Lot) : null;
                if (missing.Count == 0 && lot == null) missing.Add($"reward lot {s.Lot} empty in DS2");
                int? trig = F(s.Trigger), done = F(s.Done);
                if (trig == null || done == null) missing.Add($"flag {(trig == null ? s.Trigger : s.Done)} has no DS2 flag");
                if (missing.Count > 0) { pending.Add($"{label}: {string.Join(", ", missing)}"); continue; }
                if (offers.Any(o => o.Item == input && o.Count == count && o.Trigger == trig && o.Done == done)) { notes.Add($"    {label}: same offer as an earlier slot (merged)"); continue; }
                offers.Add((input, count, trig!.Value, done!.Value, lot!.Value, label));
            }
            if (F(a.NoMatchFlag) is not int fNo || F(a.AcceptFlag) is not int fYes || F(a.DoneFlagSet) is not int fDone)
            { notes.Add($"  drop trade {a.EventId}: talk flags without DS2 flags: left out"); continue; }

            var states = States(helper, offers.Select(o => (o.Item, o.Count, o.Trigger, o.Done, o.Lot)).ToList(), fNo, fYes, fDone);
            esd.StateGroups[gid] = states;
            var row = new PARAM.Row(ownerless) { ID = gid };
            row["stateMapId"].Value = Convert.ChangeType(gid, row["stateMapId"].Value.GetType());
            events.Rows.Add(row);
            notes.Add($"  OK   drop trade {a.EventId} (area {a.Entity}, {a.Slots.Count} slots) → group {gid}, helper {helper}: {offers.Count} offers emitted, {pending.Count} slots pending (rule S1-R)");
            foreach (var o in offers) notes.Add($"    offer {o.Label}: give DS2 {o.Item} x{o.Count} → ItemLotParam2_Other {o.Lot}; trigger {o.Trigger}, done {o.Done}");
            foreach (var p in pending) notes.Add($"    pending {p}");
            n++;
        }
        foreach (var l in notes) pack.Note(l);
        return notes;
    }

    /// <summary>The DS1 items of a lot row (category, id), at most 8.</summary>
    public static IEnumerable<(uint Cat, int Id)> LotItems(Ds1Source ds1, int lot)
    {
        if (ds1.Params.Params["ItemLotParam"][lot] is not { } r) yield break;
        for (int i = 1; i <= 8; i++)
        {
            int id = Convert.ToInt32(r[$"lotItemId0{i}"].Value);
            if (id > 0 && Convert.ToDouble(r[$"lotItemBasePoint0{i}"].Value) > 0)
                yield return (unchecked((uint)Convert.ToInt64(r[$"lotItemCategory0{i}"].Value)), id);
        }
    }

    /// <summary>
    /// The group: 1 host test → 2 wait for "Offer" → for each offer j: 10+3j check (done OFF and held: ask once no
    /// window is open; else next), 11+3j ask, 12+3j answer (YES → 100+j, NO/cancel → next) → 90 nothing taken: 91 flag C
    /// (holds a done slot's item) / 92 flag A (holds nothing tradable) / declined everything. Every way back to the
    /// "Offer" wait re-executes the group (8995: 130002, rule G2), 1 s after a trade or a flag so one press isn't read twice.
    /// </summary>
    public static Dictionary<long, ESD.State> States(int helper, List<(int Item, int Count, int Trigger, int Done, int Lot)> offers, int fNo, int fYes, int fDone)
    {
        const long Wait = 2, End = 90, Already = 91, NoNo = 92, Rerun = 8995, Park = 9001;
        var s = new Dictionary<long, ESD.State>
        {
            [0] = S([], (True, 1)),
            // host only (DS1: a client doesn't run the trade events)
            [1] = S([C(131638, helper, 1)], ([.. Call0(130602), 0x40, 0x95, 0xA1], Wait), (True, Park)),
            [Wait] = S([C(131638, helper, 0), C(140205, 0, helper)], ([.. Group0, 0xA1], 3)),
            [3] = S([C(131638, helper, 1)], (True, offers.Count > 0 ? 10 : End)),
            [Park] = S([]),
            // rule G2 (rulebook §7): DS2 replays a registering state's recorded results on a revisit, so the group never
            // goes back to the "Offer" wait; it re-executes (130002)
            [Rerun] = S([C(130002)]),
        };
        for (int j = 0; j < offers.Count; j++)
        {
            var o = offers[j];
            long check = 10 + 3 * j, ask = check + 1, answer = check + 2, yes = 100 + j, next = j + 1 < offers.Count ? check + 3 : End;
            byte[] can = [.. FlagExpr(o.Done, false), .. Held(o.Item, o.Count), 0x98];
            s[check] = S([], ([.. can, 0x40, 0x95, 0xA1], next), ([.. Call0(130422), 0x40, 0x95, 0xA1], ask));
            s[ask] = S([C(130441, GiveTextFor(o.Count), 0, 190, 2, o.Item, 0)], ([.. Call0(130422), 0x40, 0x96, 0xA1], answer));
            s[answer] = S([], ([.. Call0(130421), 0x41, 0x95, 0xA1], yes), ([.. Call0(130421), 0x42, 0x95, 0xA1], next), ([.. Call0(130421), 0x43, 0x95, 0xA1], next));
            s[yes] = S([C(131401, o.Item, o.Count), C(130101, fYes, 1), C(130101, o.Trigger, 1), C(131402, o.Lot, 1), C(130101, o.Done, 1)],
                ([.. StateTime(1f), 0xA1], Rerun));
        }
        byte[]? Any(Func<(int Item, int Count, int Trigger, int Done, int Lot), byte[]> f) =>
            offers.Count == 0 ? null : offers.Select(f).Aggregate((x, y) => [.. x, .. y, 0x99]);
        var undone = Any(o => [.. FlagExpr(o.Done, false), .. Held(o.Item, o.Count), 0x98]);
        var doneHeld = Any(o => [.. FlagExpr(o.Done, true), .. Held(o.Item, o.Count), 0x98]);
        var endConds = new List<(byte[] If, long Then)>();
        if (undone != null) endConds.Add(([.. undone, 0xA1], Rerun));      // declined every offer: nothing said
        if (doneHeld != null) endConds.Add(([.. doneHeld, 0xA1], Already)); // only finished trades' items: DS1's flag C
        endConds.Add((True, NoNo));                                           // nothing tradable: DS1's flag A
        s[End] = S([], [.. endConds]);
        s[Already] = S([C(130101, fDone, 1)], ([.. StateTime(1f), 0xA1], Rerun));
        s[NoNo] = S([C(130101, fNo, 1)], ([.. StateTime(1f), 0xA1], Rerun));
        return s;
    }

    // ---- ESD helpers (the translator's encodings; comparison opcodes 0x91 <, 0x92 >, 0x93 <=, 0x94 >=, 0x95 ==, 0x96 !=)
    static readonly byte[] True = [0x41, 0xA1];
    static readonly byte[] Group0 = [0x82, .. BitConverter.GetBytes(140001), 0x40, 0x85, 0xA6];
    static byte[] Push(int v) => v is >= -64 and < 64 ? [(byte)(0x40 + v)] : [0x82, .. BitConverter.GetBytes(v)];
    static byte[] Int(int v) => [.. Push(v), 0xA1];
    static byte[] Call0(int f) => [0x82, .. BitConverter.GetBytes(f), 0x84, 0xA6];
    static byte[] FlagExpr(int flag, bool on) => [0x46, .. Push(flag), 0x85, 0xA6, 0x40, on ? (byte)0x96 : (byte)0x95];
    /// <summary>f131401(item, 1, 1, 0) >= count (vanilla's form, e.g. m10_02 g2147483579).</summary>
    static byte[] Held(int item, int count) => [0x82, .. BitConverter.GetBytes(131401), .. Push(item), 0x41, 0x41, 0x40, 0x88, 0xA6, .. Push(count), 0x94];
    static byte[] StateTime(float sec) => [0x43, 0x84, 0xA6, 0x80, .. BitConverter.GetBytes(sec), 0x94, 0x40, 0x96];
    static ESD.CommandCall C(int id, params int[] args) => new(1, id, args.Select(Int).ToArray());
    static ESD.State S(IEnumerable<ESD.CommandCall> entry, params (byte[] If, long Then)[] conds)
    {
        var st = new ESD.State();
        st.EntryCommands.AddRange(entry);
        foreach (var (c, t) in conds) st.Conditions.Add(new ESD.Condition(t, c));
        return st;
    }
}
