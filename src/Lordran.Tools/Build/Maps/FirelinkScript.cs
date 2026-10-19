using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Firelink Shrine's DS1 event script (m10_02_00_00.emevd) translated by rule into DS2 m19_02_00_00's event ESD (session 9,
/// track firelink): <see cref="Events.EmevdTranslator"/> with <see cref="Events.FirelinkContext"/>, exactly as the Asylum's
/// (AsylumScript.Events). First target: the nest → Asylum route (DS1 11020020 "Curl up like a ball" in region 1022120 →
/// 11020021: 20 s, skipped once 11020000 is set → cutscenes 100230 + 180130 and the warp to the Asylum cliff 1812110).
/// The map's part of the rules:
///  - <see cref="AreaButtons"/>: the Asylum's action-button area rule (AsylumScript.AreaButtons) for any DS1 map, with the
///    DS2 map's AABB in the ids (Firelink 1902: helpers o19_93NN, KeyGuideParam 1902nnnn, instances 1902k120);
///  - <see cref="Regions"/>: every DS1 region with an entity → EventLocation 3701000 + entity % 1000 (the Asylum's rule),
///    except the warp targets MapLinks writes as points;
///  - <see cref="AsylumWarpTargets"/>: the DS1 Asylum regions Firelink's events warp to, as points (MapLinks.WarpTargets the
///    other way round).
/// </summary>
public static class FirelinkScript
{
    const string Ds1Map = Events.FirelinkContext.Map;
    static readonly string Ds2Map = Events.FirelinkContext.Ds2Map;
    static readonly int Aabb = MapHosts.Aabb(Events.FirelinkContext.Ds2Map); // session 11 (burg11): DS2 m19_42 → 1942 (rule H1; was 1902)

    /// <summary>Fix pass: build-pack --no-firelink-script turns the whole script off (no helpers, no translated events, no
    /// Asylum warp-target points), so a crash at Firelink's load can be A/B'd against the pack without it.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>EventLocation of a DS1 Firelink region: the Asylum's rule (3701000 + entity % 1000; per-map param).</summary>
    public static int RegionId(int ds1Entity) => AsylumScript.RegionId(ds1Entity);

    /// <summary>A DS1 action button on an area (see AsylumScript.AreaButton), with the DS2 map's ids.</summary>
    public sealed record AreaButton(int Area, int Message, int K, int Helper, string Model, int KeyGuide);

    /// <summary>
    /// The action-button area rule (AsylumScript.AreaButtons, session 8 fix pass) for a DS1 map: every 3:5 / 3:18 action
    /// button on a fixed area (TargetEntityType 1, no light-wall line segment) gets an invisible key-guide helper at the
    /// area; the k-th distinct prompt of an area → instance AABB7000 + k·1000 + area % 1000 (EntityRules.AreaHelper with the
    /// DS2 map's AABB), model oAA_93NN (NN = 1 + the prompt's index among the map's prompts), KeyGuideParam AABB0000 +
    /// message % 10000. For m18_01 this gives AsylumScript.AreaButtons' ids exactly (checked: <see cref="SameAsAsylum"/>).
    /// </summary>
    public static List<AreaButton> AreaButtons(string ds1Map, int aabb)
    {
        var defs = Commands.EmevdDump.Defs();
        var emevd = EMEVD.Read(Path.Combine(Program.DefaultDsrRoot, "event", ds1Map + ".emevd.dcx"));
        var found = new List<(int Area, int Message)>();
        foreach (var x in emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 3 && i.ID is 5 or 18))
        {
            var v = Events.EmevdTranslator.Decode(defs, x);
            if (v.Length < 7 || (int)v[1] != 1 || (int)v[2] <= 0) continue; // areas with a fixed entity
            if (x.ID == 18 && v.Length > 9 && (int)v[9] > 0) continue;        // a light wall: the white-door rule
            found.Add(((int)v[2], (int)v[6]));
        }
        var messages = found.Select(f => f.Message).Distinct().ToList();
        string aa = (aabb / 100).ToString("D2");
        return found.Distinct().GroupBy(f => f.Area).SelectMany(g => g.Select((f, k) => new AreaButton(f.Area, f.Message, k,
            aabb * 10000 + 7000 + k * 1000 + f.Area % 1000, $"o{aa}_93{1 + messages.IndexOf(f.Message):D2}", aabb * 10000 + f.Message % 10000))).ToList();
    }

    static List<AreaButton>? areaButtons;
    public static List<AreaButton> AreaButtons() => areaButtons ??= AreaButtons(Ds1Map, Aabb);

    /// <summary>The generic rule reproduces the Asylum's own helpers (ids, models, key guides).</summary>
    public static bool SameAsAsylum() => AreaButtons("m18_01_00_00", 1801).Select(b => (b.Area, b.Message, b.K, b.Helper, b.Model, b.KeyGuide))
        .SequenceEqual(AsylumScript.AreaButtons().Select(b => (b.Area, b.Message, b.K, b.Helper, b.Model, b.KeyGuide)));

    /// <summary>Firelink's placements (MapBuilder.Options.Placements): the key-guide helpers of its action-button areas.</summary>
    public static List<MapBuilder.Placement> Placements(MSB1 msb1) => !Enabled ? [] :
        AreaButtons().Select(b =>
        {
            var r = msb1.Regions.GetEntries().First(x => x.EntityID == b.Area);
            return new MapBuilder.Placement(b.Model, r.Position, r.Rotation, b.Helper, MapBuilder.PlacementKind.Generic, 0,
                $"(DS1 region {r.Name}, entity {b.Area}) key guide for DS1 action button {b.Message}");
        }).ToList();

    /// <summary>
    /// The DS1 Asylum (m18_01) regions Firelink's events warp to with 2002:4 (11020021: the nest's landing 1812110 "landing
    /// point of the warp cutscene from the base", (3.27, 209.84, -83.48), facing 180°). MapLinks writes them into the
    /// Asylum's EventLocation as points (id AsylumScript.RegionId, DS1 position and yaw), as it writes Firelink's own warp
    /// targets (MapLinks.WarpTargets). Pure (DS1's files).
    /// </summary>
    public static List<(int Entity, Vector3 Pos, float Yaw)> AsylumWarpTargets()
    {
        if (!Enabled) return [];
        if (asylumTargets != null) return asylumTargets;
        var defs = Commands.EmevdDump.Defs();
        var emevd = EMEVD.Read(Path.Combine(Program.DefaultDsrRoot, "event", Ds1Map + ".emevd.dcx"));
        var ids = emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2002 && i.ID == 4).Select(i => Events.EmevdTranslator.Decode(defs, i))
            .Where(v => v.Length >= 5 && (int)v[3] == 18 && (int)v[4] == 1).Select(v => (int)v[2]).ToHashSet();
        var asylum = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", "m18_01_00_00.msb"));
        return asylumTargets = asylum.Regions.GetEntries().Where(r => ids.Contains(r.EntityID)).Select(r => (r.EntityID, r.Position, r.Rotation.Y)).ToList();
    }
    static List<(int Entity, Vector3 Pos, float Yaw)>? asylumTargets;

    /// <summary>EventLocation rows of DS1's regions (AsylumScript.Regions' shapes: boxes as DS1 gives them, points as
    /// points, spheres/cylinders as boxes around them).</summary>
    public static IEnumerable<(int Id, Vector3 Pos, Vector3 Rot, Vector3 Size, bool Point)> Regions(MSB1 msb1)
    {
        foreach (var r in msb1.Regions.GetEntries().Where(r => r.EntityID > 0))
        {
            (Vector3 Pos, Vector3 Size, bool Point)? s = r.Shape switch
            {
                MSB.Shape.Box b => (r.Position, new Vector3(b.Width, b.Height, b.Depth), false),
                MSB.Shape.Point => (r.Position, Vector3.Zero, true),
                MSB.Shape.Sphere sp => (r.Position - new Vector3(0, sp.Radius, 0), new Vector3(2 * sp.Radius, 2 * sp.Radius + 2, 2 * sp.Radius), false),
                MSB.Shape.Cylinder c => (r.Position, new Vector3(2 * c.Radius, c.Height, 2 * c.Radius), false),
                _ => null,
            };
            if (s is { } v) yield return (RegionId(r.EntityID), v.Pos, r.Rotation, v.Size, v.Point);
        }
    }

    /// <summary>KeyGuideParam rows + helper models for the action-button areas (AsylumScript.Params' rule).</summary>
    static void Params(Pack pack, Ds1Source ds1, MSB1 msb1)
    {
        foreach (var g in AreaButtons().GroupBy(b => b.Message))
        {
            var b0 = g.First();
            float radius = g.Max(b => msb1.Regions.GetEntries().First(r => r.EntityID == b.Area).Shape is MSB.Shape.Box box
                ? MathF.Sqrt(box.Width * box.Width + box.Depth * box.Depth) / 2 + 1f : 2f);
            string text = ds1.Text("Event_text_", b0.Message);
            if (string.IsNullOrEmpty(text)) text = ds1.AnyText(b0.Message) ?? $"[DS1 {b0.Message}]";
            var kgFmg = pack.Text("keyguide");
            string glyph = kgFmg.Entries.First(e => e.ID == 100).Text.Split(':')[0] + ":";
            var same = kgFmg.Entries.Where(e => e.Text?.Trim() == glyph + text.Trim()).OrderBy(e => e.ID).FirstOrDefault();
            int textId = same?.ID ?? 18000000 + b0.Message % 1000000;
            if (same == null) pack.SetText("keyguide", textId, glyph + text.Trim());
            var row = pack.AddRow("KeyGuideParam", pack.Param("KeyGuideParam")[1110]!, b0.KeyGuide, $"[Lordran] Firelink action button {b0.Message}: {text}");
            row["textId"].Value = textId;
            row["width_radius"].Value = radius;
            ObjPorter.CloneDs2(pack, "o04_9300", b0.Model, 10049040, (uint)b0.KeyGuide);
            pack.Note($"{Ds2Map} action-button area rule: DS1 prompt {b0.Message} \"{text}\" → helper {b0.Model} (KeyGuideParam {b0.KeyGuide}, keyguide text {textId}, sphere r {radius:0.##} m) at {string.Join(", ", g.Select(b => $"{b.Area} → instance {b.Helper}"))}");
        }
    }

    /// <summary>
    /// Builds Firelink's script into the pack, after the map, its characters and MapLinks: the helpers' params, the
    /// EventLocation rows of DS1's regions, and DS1's events translated into the map's event ESD / EventParam (which
    /// AsylumEvents wrote with the bonfire's event; groups 38000+, rows from Things Betwixt's 5010 like the Asylum's).
    /// </summary>
    /// <returns>The DS1 events that translated.</returns>
    public static HashSet<long> Build(Pack pack, Ds1Source ds1, MapBuilder.Options map)
    {
        if (!Enabled)
        {
            pack.Note($"{Ds2Map} script: off (--no-firelink-script): no DS1 events, no key-guide helpers, no Asylum warp-target points");
            Console.WriteLine($"  {map.AreaName}: DS1 events off (--no-firelink-script)");
            return [];
        }
        var msb1 = MSB1.Read(Path.Combine(ds1.Root, "map", "MapStudio", Ds1Map + ".msb"));
        var emevd = EMEVD.Read(Path.Combine(ds1.Root, "event", Ds1Map + ".emevd.dcx"));
        Params(pack, ds1, msb1);
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();

        // ---- EventLocation: DS1's regions (MapLinks wrote the arrival / test / warp-target points first: those stay)
        var fl = MapLinks.Locations(pack, map, defs);
        var point = MapLinks.Template(pack, defs, "m20_10_00_00", 3700000);
        var box = MapLinks.Template(pack, defs, "m10_02_00_00", 700000);
        int added = 0;
        foreach (var (id, pos, rot, size, isPoint) in Regions(msb1))
        {
            if (fl.Rows.Any(r => r.ID == id)) continue;
            if (isPoint) MapLinks.Put(fl, point, id, pos, rot); else MapLinks.Put(fl, box, id, pos, rot, size);
            added++;
        }
        pack.AddFile("gamedata", $"param/eventlocation_{Ds2Map}.param", fl.Write());

        // ---- the translated DS1 events
        IReadOnlyDictionary<int, int>? sounds = map.Ds1Sound && map.Ds1Atmosphere ? Events.EntityRules.SoundId(msb1, emevd, Ds1Map) : null; // asy-sound: the MapSounds plan (regions are placed with the atmosphere)
        var ctx = new Events.FirelinkContext(pack, ds1, Events.FirelinkContext.PlacedGenerators(pack), sounds);
        var tr = new Events.EmevdTranslator(emevd, ctx, Commands.EmevdDump.Defs());
        int nextGroup = 38000;
        tr.TranslateMap((id, slot) => nextGroup++);

        var esd = ESD.Read(pack.GetFile("gamedata", $"ezstate/event_{Ds2Map}.esd"));
        var events = PARAM.Read(pack.GetFile("gamedata", $"param/eventparam_{Ds2Map}.param"));
        if (!events.ApplyParamdefCarefully(defs)) throw new InvalidDataException("no paramdef fits EventParam");
        var tbEvents = PARAM.Read(Path.Combine(pack.Ds2Data, "param", "eventparam_m10_02_00_00.param"));
        if (!tbEvents.ApplyParamdefCarefully(defs)) throw new InvalidDataException("no paramdef fits EventParam");
        var ownerless = tbEvents[5010]!;
        foreach (var r in tr.Results.Where(r => r.States != null))
        {
            if (esd.StateGroups.ContainsKey(r.GroupId) || events[r.GroupId] != null) throw new InvalidDataException($"{Ds2Map}: event group {r.GroupId} exists");
            esd.StateGroups[r.GroupId] = r.States!;
            var row = new PARAM.Row(ownerless) { ID = r.GroupId };
            row["stateMapId"].Value = Convert.ChangeType(r.GroupId, row["stateMapId"].Value.GetType());
            if (r.Owner != 0)
            {
                row["bootType"].Value = Convert.ChangeType(2, row["bootType"].Value.GetType());
                row["ownerType"].Value = Convert.ChangeType(1, row["ownerType"].Value.GetType());
                row["ownerId"].Value = Convert.ChangeType(r.Owner, row["ownerId"].Value.GetType());
            }
            events.Rows.Add(row);
        }
        // session 10 (asy-items), rules C2/C2b: the origin prompt where DS1's opening is or where DS1 events read the class
        // (data rule; Firelink's own script has neither today: no-op; once common 290/740 run here pass readsClass: true)
        Events.OriginPrompt.Add(pack, ds1, esd, events, ownerless, emevd, Ds1Map);
        Talk.Covenants.AddHousekeeping(pack, esd, events, ownerless, Ds2Map); // session 11 (cov11), rule K2 / D2-A: group 37430
        foreach (var (id, states) in esd.StateGroups)
        {
            if (states.TryGetValue(0, out var st0) && st0.EntryCommands.Count > 0)
                throw new InvalidDataException($"{Ds2Map} ESD group {id}: state 0 has entry commands; DS2 never runs them");
            if (states.Count < 2) throw new InvalidDataException($"{Ds2Map} ESD group {id} has {states.Count} state(s); DS2 needs at least 2");
        }
        // DS1 texts the translated groups show (130443's message): only those are added to mapevent
        foreach (var c in tr.Results.Where(r => r.States != null).SelectMany(r => r.States!.Values).SelectMany(s => s.EntryCommands).Where(c => c.CommandID == 130443))
            if (c.Arguments.Count > 0 && c.Arguments[0] is { Length: 6 } a && a[0] == 0x82 && a[5] == 0xA1 && ctx.PendingTexts.TryGetValue(BitConverter.ToInt32(a, 1), out var text))
                pack.SetText("mapevent", BitConverter.ToInt32(a, 1), text);
        events.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
        pack.AddFile("gamedata", $"ezstate/event_{Ds2Map}.esd", esd.Write());
        pack.AddFile("gamedata", $"param/eventparam_{Ds2Map}.param", events.Write());
        int ok = tr.Results.Count(r => r.States != null);
        pack.Note($"{Ds1Map} DS1 events translated ({Ds2Map}):" + Environment.NewLine + tr.Report());
        pack.Note($"{Ds2Map} script: {ok} of {tr.Results.Count} translated DS1 event instances (38000+), {added} EventLocation rows from DS1's regions, flags by Events.FlagRules (F1 global slots, F2 temporary in {Ds2Map}'s map-slot block)");
        pack.Note($"{Ds2Map} action-button area rule for any map (FirelinkScript.AreaButtons) reproduces AsylumScript.AreaButtons for m18_01: {SameAsAsylum()}");
        pack.Note($"{Ds2Map} rule T1 (DS1 temporary flags reset at load): {(tr.TemporaryFlags.Count == 0 ? "none used" : $"{tr.TemporaryFlags.Count} used ({string.Join(", ", tr.TemporaryFlags)}), event 0 clears each at load (130101 …, 0): {tr.TemporaryFlagsReset}")}");
        Console.WriteLine($"  {map.AreaName}: {ok} of {tr.Results.Count} DS1 event instances translated");
        return tr.Results.Where(r => r.States != null).Select(r => r.EventId).ToHashSet();
    }
}
