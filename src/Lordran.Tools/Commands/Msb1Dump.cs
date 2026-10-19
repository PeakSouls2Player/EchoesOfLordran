using System.Globalization;
using System.Numerics;
using Lordran.Tools.Formats;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// msb1-dump MAP [SECTION [KIND]] [X Y Z R] [--brief] — DS1 (DSR) MSB contents with model, position and rotation
/// (optionally only within R of a point). Sections: players, pieces, objects, collisions, enemies, models, regions,
/// treasures, objacts, events [KIND], all (every part, the old default for an unknown section).
/// <list type="bullet">
/// <item>"models" lists map-piece models and whether their FLVER exists in DSR's map folder (MapBuilder skips missing ones).</item>
/// <item>"enemies" (Enemy + DummyEnemy parts): NpcParam (with its community name), ThinkParam, TalkID, CharaInitID,
/// PointMoveType, PlatoonID, InitAnimID (the idle/pose the character starts in, e.g. Asylum Hollow c2500_0000 = 9005),
/// DamageAnimID, the collision it stands on, its MovePointNames (patrol regions with positions) and its NpcParam drop lots
/// as full lot chains. --brief prints only the first line.</item>
/// <item>"treasures": every ItemLot of the treasure (up to 5) with every row of each lot chain (Ds1Lots), weights in %.</item>
/// <item>"events [KIND]": the MSB event section: lights, sounds, sfx, wind, treasures, generators, messages (with the
/// Blood_writing_ text), objacts, spawns, offsets, navmeshes, environments, pseudomp. Each line has the event id, entity,
/// part and region (with the region's position and shape) and the type's own fields.</item>
/// </list>
/// Session 8 (census tool gaps): enemy InitAnimID/DamageAnimID/MovePointNames/PlatoonID, the MSB events section and
/// multi-row item lots were added; the census agents had needed throwaway SoulsFormats readers for them.
/// </summary>
public static class Msb1Dump
{
    const string Usage =
        "usage: msb1-dump MAP [SECTION [KIND]] [X Y Z R] [--brief]\n" +
        "  SECTION: players (default) | pieces | objects | collisions | enemies | models | regions | treasures | objacts |\n" +
        "           events [lights|sounds|sfx|wind|treasures|generators|messages|objacts|spawns|offsets|navmeshes|environments|pseudomp] | all\n" +
        "  X Y Z R: only entries within R of the point (events: by their region, else their part)\n" +
        "  --brief: enemies without the move-point and drop lines\n" +
        "  e.g. msb1-dump m18_01_00_00 enemies   msb1-dump m18_01_00_00 events messages   msb1-dump m10_02_00_00 treasures";

    static readonly string[] Sections = ["players", "pieces", "objects", "collisions", "enemies", "models", "regions", "treasures", "objacts", "events", "all"];

    static readonly Dictionary<string, string> EventKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lights"] = "Light", ["light"] = "Light", ["sounds"] = "Sound", ["sound"] = "Sound", ["sfx"] = "SFX",
        ["wind"] = "Wind", ["treasures"] = "Treasure", ["treasure"] = "Treasure", ["generators"] = "Generator", ["generator"] = "Generator",
        ["messages"] = "Message", ["message"] = "Message", ["objacts"] = "ObjAct", ["objact"] = "ObjAct",
        ["spawns"] = "SpawnPoint", ["spawn"] = "SpawnPoint", ["spawnpoints"] = "SpawnPoint", ["offsets"] = "MapOffset", ["mapoffset"] = "MapOffset",
        ["navmeshes"] = "Navmesh", ["navmesh"] = "Navmesh", ["environments"] = "Environment", ["environment"] = "Environment",
        ["pseudomp"] = "PseudoMultiplayer", ["pseudomultiplayer"] = "PseudoMultiplayer",
    };

    public static int Run(string[] args)
    {
        if (args.Length < 2 || args[1].StartsWith("-")) return CliArgs.Fail("msb1-dump", Usage, "missing MAP");
        string map = args[1].Replace(".msb", "");
        string msbPath = Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", map + ".msb");
        if (!File.Exists(msbPath))
        {
            var maps = Directory.GetFiles(Path.GetDirectoryName(msbPath)!, "*.msb").Select(Path.GetFileNameWithoutExtension).ToList();
            return CliArgs.Fail("msb1-dump", Usage, $"no DSR map '{map}'; maps: {string.Join(" ", maps)}");
        }
        bool brief = args.Contains("--brief");
        foreach (string o in args.Skip(2).Where(a => a.StartsWith("--")))
            if (o != "--brief") return CliArgs.Fail("msb1-dump", Usage, $"unknown option {o}");
        var rest = args.Skip(2).Where(a => !a.StartsWith("--")).ToList();
        // trailing X Y Z R
        Vector3? at = null;
        float r = 0;
        if (rest.Count >= 4 && rest.TakeLast(4).All(a => float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
        {
            var n = rest.TakeLast(4).Select(a => float.Parse(a, CultureInfo.InvariantCulture)).ToArray();
            at = new Vector3(n[0], n[1], n[2]);
            r = n[3];
            rest = rest.Take(rest.Count - 4).ToList();
        }
        else if (rest.Any(a => float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            return CliArgs.Fail("msb1-dump", Usage, "a point filter needs exactly X Y Z R at the end");
        string what = rest.Count > 0 ? rest[0].ToLowerInvariant() : "players";
        if (!Sections.Contains(what)) return CliArgs.Fail("msb1-dump", Usage, $"unknown section '{rest[0]}'");
        string? kind = null;
        if (rest.Count > 1)
        {
            if (what != "events") return CliArgs.Fail("msb1-dump", Usage, $"unexpected argument '{rest[1]}' (only 'events' takes a KIND)");
            if (!EventKinds.TryGetValue(rest[1], out kind)) return CliArgs.Fail("msb1-dump", Usage, $"unknown event kind '{rest[1]}'");
            if (rest.Count > 2) return CliArgs.Fail("msb1-dump", Usage, $"unexpected argument '{rest[2]}'");
        }

        MSB1 msb;
        try { msb = MSB1.Read(msbPath); }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        {
            Console.Error.WriteLine($"msb1-dump: {map}.msb exists but is not a readable DS1 MSB ({e.Message.Split('\n')[0]})");
            return 1;
        }
        bool Near(Vector3 p) => !at.HasValue || Vector3.Distance(p, at.Value) <= r;

        switch (what)
        {
            case "models":
                {
                    string area = map.Substring(1, 2);
                    foreach (var m in msb.Models.MapPieces)
                    {
                        string f = Path.Combine(Program.DefaultDsrRoot, "map", map, $"{m.Name}A{area}.flver.dcx");
                        int uses = msb.Parts.MapPieces.Count(p => p.ModelName == m.Name);
                        Console.WriteLine($"{m.Name,-10} parts={uses,-3} flver={(File.Exists(f) ? "yes" : "MISSING")} {m.SibPath}");
                    }
                    return 0;
                }
            case "treasures":
                {
                    var store = ParamStore.LoadDsr(Program.DefaultDsrRoot);
                    foreach (var t in msb.Events.Treasures)
                    {
                        var part = msb.Parts.GetEntries().FirstOrDefault(p => p.Name == t.TreasurePartName);
                        if (part != null && !Near(part.Position) || part == null && at.HasValue) continue;
                        Console.WriteLine($"{t.Name,-24} part={t.TreasurePartName,-16} {(part != null ? $"pos={V(part.Position)} model={part.ModelName}{PartKind(part)}" : "(no part)")} chest={t.InChest} disabled={t.StartDisabled} entity={t.EntityID} lots={string.Join(",", t.ItemLots.Where(l => l > 0))}");
                        foreach (int lot in t.ItemLots.Where(l => l > 0))
                            foreach (string line in Ds1Lots.Describe(store, lot, "    ")) Console.WriteLine(line);
                    }
                    return 0;
                }
            case "objacts":
                foreach (var a in msb.Events.ObjActs)
                    Console.WriteLine($"{a.Name,-24} part={a.ObjActPartName,-14} param={a.ObjActParamID} state={a.ObjActState} flag={a.EventFlagID} entity={a.ObjActEntityID}");
                return 0;
            case "regions":
                foreach (var g in msb.Regions.GetEntries())
                {
                    if (!Near(g.Position)) continue;
                    Console.WriteLine($"{g.GetType().Name,-10} {g.Name,-24} pos={V(g.Position)} rot=({g.Rotation.Y:0}) entity={g.EntityID} shape={Shape(g.Shape)}");
                }
                return 0;
            case "events":
                return DumpEvents(msb, kind, Near, at.HasValue);
            case "enemies":
                return DumpEnemies(msb, Near, brief);
        }

        IEnumerable<MSB1.Part> parts = what switch
        {
            "players" => msb.Parts.Players,
            "pieces" => msb.Parts.MapPieces,
            "objects" => msb.Parts.Objects,
            "collisions" => msb.Parts.Collisions,
            _ => msb.Parts.GetEntries(),
        };
        foreach (var p in parts)
        {
            if (!Near(p.Position)) continue;
            string extra = p is MSB1.Part.EnemyBase e ? $" npc={e.NPCParamID} think={e.ThinkParamID} talk={e.TalkID} chrInit={e.CharaInitID} point={e.PointMoveType} init={e.InitAnimID}"
                : p is MSB1.Part.Collision c ? $" hitFilter={c.HitFilterID} sound={c.SoundSpaceType} mapName={c.MapNameID} playRegion={c.PlayRegionID} disableStart={c.DisableStart} disableBonfire={c.DisableBonfireEntityID} draw={string.Join(",", c.DrawGroups.Select(x => x.ToString("X")))} disp={string.Join(",", c.DispGroups.Select(x => x.ToString("X")))}" : "";
            Console.WriteLine($"{p.GetType().Name,-10} {p.Name,-16} model={p.ModelName,-10} pos={V(p.Position)} rot=({p.Rotation.X:0}, {p.Rotation.Y:0}, {p.Rotation.Z:0}) entity={p.EntityID}{extra}");
        }
        return 0;
    }

    /// <summary>" (DummyObject)" etc. for a part that is not a live Object/Enemy (DS1 dummies are not placed in game).</summary>
    static string PartKind(MSB1.Part p) => p is MSB1.Part.DummyObject or MSB1.Part.DummyEnemy ? $" ({p.GetType().Name})" : "";

    static string V(Vector3 v) => $"({v.X:0.00}, {v.Y:0.00}, {v.Z:0.00})";

    static string Shape(MSB.Shape s) => s switch
    {
        MSB.Shape.Box bx => $"Box({bx.Width:0.##}, {bx.Height:0.##}, {bx.Depth:0.##})",
        MSB.Shape.Sphere sp => $"Sphere(r {sp.Radius:0.##})",
        MSB.Shape.Cylinder cy => $"Cylinder(r {cy.Radius:0.##}, h {cy.Height:0.##})",
        MSB.Shape.Circle ci => $"Circle(r {ci.Radius:0.##})",
        MSB.Shape.Rectangle re => $"Rect({re.Width:0.##}, {re.Depth:0.##})",
        _ => s.GetType().Name,
    };

    /// <summary>Enemy and DummyEnemy parts with every EnemyBase field (session 8).</summary>
    static int DumpEnemies(MSB1 msb, Func<Vector3, bool> near, bool brief)
    {
        var store = ParamStore.LoadDsr(Program.DefaultDsrRoot);
        var regions = msb.Regions.GetEntries().ToDictionary(g => g.Name, g => g);
        var npcs = store.Params.GetValueOrDefault("NpcParam");
        foreach (var e in msb.Parts.Enemies.Cast<MSB1.Part.EnemyBase>().Concat(msb.Parts.DummyEnemies))
        {
            if (!near(e.Position)) continue;
            string npcName = store.RowName("NpcParam", e.NPCParamID);
            Console.WriteLine($"{e.GetType().Name,-10} {e.Name,-16} model={e.ModelName,-6} pos={V(e.Position)} rot=({e.Rotation.X:0}, {e.Rotation.Y:0}, {e.Rotation.Z:0}) entity={e.EntityID}" +
                $" npc={e.NPCParamID}{(npcName != "" ? $" '{npcName}'" : "")} think={e.ThinkParamID} talk={e.TalkID} chrInit={e.CharaInitID} point={e.PointMoveType} platoon={e.PlatoonID}" +
                $" init={e.InitAnimID} damage={e.DamageAnimID} collision={(string.IsNullOrEmpty(e.CollisionName) ? "-" : e.CollisionName)}");
            if (brief) continue;
            var points = e.MovePointNames.Where(n => !string.IsNullOrEmpty(n)).ToList();
            if (points.Count > 0)
                Console.WriteLine("    movePoints: " + string.Join("; ", points.Select(n => regions.TryGetValue(n, out var g) ? $"{n} {V(g.Position)} entity={g.EntityID}" : $"{n} (no region)")));
            if (npcs?[e.NPCParamID] is { } npc)
            {
                var lots = Enumerable.Range(1, 6).Select(i => $"itemLotId_{i}").Where(f => npc.Cells.Any(c => c.Def.InternalName == f))
                    .Select(f => System.Convert.ToInt32(npc[f].Value)).Where(l => l > 0).ToList();
                long souls = npc.Cells.Any(c => c.Def.InternalName == "getSoul") ? System.Convert.ToInt64(npc["getSoul"].Value) : 0;
                long humanity = npc.Cells.Any(c => c.Def.InternalName == "humanityLotId") ? System.Convert.ToInt64(npc["humanityLotId"].Value) : 0;
                if (lots.Count > 0 || souls > 0)
                    Console.WriteLine($"    drops: souls={souls} humanityLot={humanity}{(lots.Count == 0 ? " (no item lot)" : "")}");
                foreach (int lot in lots)
                    foreach (string line in Ds1Lots.Describe(store, lot, "      ")) Console.WriteLine(line);
            }
        }
        return 0;
    }

    /// <summary>The MSB event section, every type with its own fields (session 8).</summary>
    static int DumpEvents(MSB1 msb, string? kind, Func<Vector3, bool> near, bool filtered)
    {
        var regions = msb.Regions.GetEntries().ToDictionary(g => g.Name, g => g);
        var parts = msb.Parts.GetEntries().ToDictionary(p => p.Name, p => p);
        Lazy<ParamStore> store = new(() => ParamStore.LoadDsr(Program.DefaultDsrRoot));
        Lazy<Ds1Fmgs> fmgs = new(() => Ds1Fmgs.Load(Program.DefaultDsrRoot));
        string Region(string? n) => string.IsNullOrEmpty(n) ? "" : regions.TryGetValue(n, out var g) ? $" region={n} {V(g.Position)} {Shape(g.Shape)}" + (g.EntityID > 0 ? $" regionEntity={g.EntityID}" : "") : $" region={n} (missing)";
        string Part(string? n) => string.IsNullOrEmpty(n) ? "" : parts.TryGetValue(n, out var p) ? $" part={n}" : $" part={n} (missing)";
        Vector3? Where(MSB1.Event ev) =>
            !string.IsNullOrEmpty(ev.RegionName) && regions.TryGetValue(ev.RegionName, out var g) ? g.Position
            : !string.IsNullOrEmpty(ev.PartName) && parts.TryGetValue(ev.PartName, out var p) ? p.Position
            : ev is MSB1.Event.Treasure t && parts.TryGetValue(t.TreasurePartName ?? "", out var tp) ? tp.Position : null;

        foreach (var ev in msb.Events.GetEntries())
        {
            string type = ev.GetType().Name;
            if (kind != null && type != kind) continue;
            var pos = Where(ev);
            if (pos.HasValue ? !near(pos.Value) : filtered) continue; // a point filter drops events without a place
            string head = $"{type,-17} {ev.Name,-28} id={ev.EventID} entity={ev.EntityID}{Part(ev.PartName)}{Region(ev.RegionName)}";
            string body = ev switch
            {
                MSB1.Event.Light l => $"pointLight={l.PointLightID}",
                MSB1.Event.Sound s => $"soundType={s.SoundType}{SoundType(s.SoundType)} soundId={s.SoundID}",
                MSB1.Event.SFX x => $"effect={x.EffectID}",
                MSB1.Event.Wind w => $"vecMin={V(w.WindVecMin)} vecMax={V(w.WindVecMax)} unk0C={w.UnkT0C:0.##} unk1C={w.UnkT1C:0.##} swingCycle=({w.WindSwingCycle0:0.##}, {w.WindSwingCycle1:0.##}, {w.WindSwingCycle2:0.##}, {w.WindSwingCycle3:0.##}) swingPow=({w.WindSwingPow0:0.##}, {w.WindSwingPow1:0.##}, {w.WindSwingPow2:0.##}, {w.WindSwingPow3:0.##})",
                MSB1.Event.Treasure t => $"treasurePart={t.TreasurePartName}{(parts.TryGetValue(t.TreasurePartName ?? "", out var tp) ? $" {V(tp.Position)} model={tp.ModelName}{PartKind(tp)}" : " (missing)")} chest={t.InChest} disabled={t.StartDisabled} lots={string.Join(",", t.ItemLots.Where(i => i > 0))}",
                MSB1.Event.Generator g => $"maxNum={g.MaxNum} genType={g.GenType} limitNum={g.LimitNum} genNum={g.MinGenNum}-{g.MaxGenNum} interval={g.MinInterval:0.##}-{g.MaxInterval:0.##} initialSpawn={g.InitialSpawnCount}" +
                    $" spawnPoints=[{string.Join(", ", g.SpawnPointNames.Where(n => !string.IsNullOrEmpty(n)).Select(n => regions.TryGetValue(n, out var rg) ? $"{n} {V(rg.Position)}" : n))}]" +
                    $" spawnParts=[{string.Join(", ", g.SpawnPartNames.Where(n => !string.IsNullOrEmpty(n)))}]",
                MSB1.Event.Message m => $"message={m.MessageID} unk02={m.UnkT02} hidden={m.Hidden} text=\"{Ds1Fmgs.OneLine(fmgs.Value.Get("blood", m.MessageID) ?? "(not in Blood_writing_)")}\"",
                MSB1.Event.ObjAct a => $"objActPart={a.ObjActPartName} param={a.ObjActParamID} state={a.ObjActState} flag={a.EventFlagID} objActEntity={a.ObjActEntityID}",
                MSB1.Event.SpawnPoint sp => $"spawnRegion={sp.SpawnPointName}{(regions.TryGetValue(sp.SpawnPointName ?? "", out var sg) ? $" {V(sg.Position)} rot=({sg.Rotation.Y:0}) spawnEntity={sg.EntityID}" : " (missing)")}",
                MSB1.Event.MapOffset o => $"offset={V(o.Position)} degree={o.Degree:0.##}",
                MSB1.Event.Navmesh nv => $"navmeshRegion={nv.NavmeshRegionName}{(regions.TryGetValue(nv.NavmeshRegionName ?? "", out var ng) ? $" {V(ng.Position)} {Shape(ng.Shape)}" : " (missing)")}",
                MSB1.Event.Environment en => $"unk00={en.UnkT00} unk04={en.UnkT04:0.###} unk08={en.UnkT08:0.###} unk0C={en.UnkT0C:0.###} unk10={en.UnkT10:0.###} unk14={en.UnkT14:0.###}",
                MSB1.Event.PseudoMultiplayer pm => $"host={pm.HostEntityID} flag={pm.EventFlagID} goods={pm.ActivateGoodsID}{(pm.ActivateGoodsID > 0 ? $" '{store.Value.RowName("EquipParamGoods", pm.ActivateGoodsID)}'" : "")}",
                _ => "",
            };
            Console.WriteLine($"{head} {body}");
            if (ev is MSB1.Event.Treasure tr)
                foreach (int lot in tr.ItemLots.Where(i => i > 0))
                    foreach (string line in Ds1Lots.Describe(store.Value, lot, "    ")) Console.WriteLine(line);
        }
        return 0;
    }

    /// <summary>MSB sound event categories known from SoulsFormats' MSB3 enum (same numbering): 0 environment, 6 BGM, 7 voice.</summary>
    static string SoundType(int t) => t switch { 0 => "(env)", 6 => "(BGM)", 7 => "(voice)", _ => "" };
}
