using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Links between converted DS1 maps, the DS1 way. First: the Asylum crow. DS1's MSB marks the cliff edge past the
/// Asylum Demon's hall ("崖の先", entity 1812000, at (3.5, 210, -96)); stepping there, the crow carries the player to
/// Firelink Shrine, landing at Firelink's start point (DS1 player part c0000_0000, (-47.41, -59.82, 53.30), facing 145°).
/// Here: an EventLocation box at the cliff edge, and an Asylum event (state 0 → 1, since DS2 never runs state 0's entry
/// commands) that waits for the player inside it (140132 / f140001) and warps (132125, the Memories' map-to-map warp)
/// to an EventLocation point in Firelink. No crow animation yet.
/// </summary>
public static class MapLinks
{
    public const int CrowGroup = 37200;           // Asylum event ESD group + EventParam row
    public const int CrowRegion = 3700100;        // Asylum EventLocation box at the cliff edge
    public const int FirelinkArrival = 3700000;   // Firelink EventLocation point where the crow drops the player
    public const int FirelinkBonfireSpot = 3700010; // in front of Firelink's bonfire (test warps)
    public const int AsylumBoulderSpot = 3700120;  // before the boulder-corridor doorway (test warps)
    public const int AsylumCellDoorSpot = 3700130; // inside the start cell, facing its door (test warps)
    public const int AsylumLadderSpot = 3700140;   // in front of the pool ladder (test warps)
    public const int FirelinkStairsSpot = 3700030; // between the Undead Soldier and the stair Hollow Warriors (test warps)
    public const int FirelinkGraveyardSpot = 3700020; // test warps: before the graveyard skeletons
    public const int AsylumHallDoorSpot = 3700150;  // test warps (session 7): before the hall door, courtyard side
    public const int AsylumGateSpot = 3700160;      // in the hall before the escape gate
    public const int AsylumBossFogSpot = 3700170;   // behind the boss fog, corridor side (session 8 step 2: was past the balcony ledge)
    public const int AsylumCourtyardFogSpot = 3700180; // beside the courtyard fog
    public const int AsylumBoulderTriggerSpot = 3700190; // on the stairs below the boulder, before its trigger
    public const int AsylumCliffLanding = 3700110;  // DS1's landing point when the crow brings you back (1812110), 12 m before the edge
    // session 8 (track npc) test warps, each 2 m in front of a DS1 NPC facing it. Session 9 (track npcplace): the session-8
    // spots assumed yaw θ faces (sin θ, 0, −cos θ); DS2's EventLocation/GeneratorLocation yaw θ faces (sin θ, 0, cos θ)
    // and a DS1 character faces (−sin θ, 0, −cos θ) (rule P1, Ds1Placement), so they stood behind the NPC looking away
    // (Firelink's merchant spot was in the pipe wall: MP fell out of the world). Now computed by rule P4 (FrontSpot).
    public const int AsylumOscarSpot = 3700200;     // Oscar's room (session 10: sealed by the wall o8520 until the boulder breaks it, rule WE1/WE4b; the spot is inside)
    public const int FirelinkCrestfallenSpot = 3700200, FirelinkPetrusSpot = 3700210, FirelinkAnastaciaSpot = 3700220, FirelinkMerchantSpot = 3700230;
    // session 10 (asy-items; the Items track's ids are 3700350-3700399, numbered as spec messages-items rev 2 §6): test
    // warps 1.5 m from DS1 class corpses, Snuggly's nest and a message (rule P4)
    public const int AsylumShieldCellSpot = 3700350;      // the shield cell's ten class corpses (37.61, 192.66, -11.85)
    public const int AsylumSnugglyNestSpot = 3700360;     // Snuggly's trade region 1812200 (-11.33, 200.50, -69.18)
    public const int AsylumBasementMessageSpot = 3700370; // the basement corridor's message 9002 "Attack" (-12.83, 185.25, -27.54)
    public const int AsylumHallwayCorpseSpot = 3700380;   // the hallway end's ten class corpses (weapons, 38.60, 195.42, 17.00)
    public const int Asylum3FCorpseSpot = 3700390;        // the 3F corridor's four third-item corpses (-10.27, 208.62, -32.52)
    public const int AsylumRustedRingSpot = 3700355;      // session 10 (asy-items i2): the Rusted Iron Ring corpse o0500_0006 (14.45, 205.96, 4.60), behind the F2 West door

    /// <summary>Session 10 (asy-items): the item/message test spots and the DS1 point each faces (rule P4).</summary>
    public static (int Id, Vector3 Target, string What)[] ItemSpots =>
    [
        (AsylumShieldCellSpot, new(37.61f, 192.66f, -11.85f), "shield cell class corpses"),
        (AsylumSnugglyNestSpot, new(-11.33f, 200.50f, -69.18f), "Snuggly's nest (trade region 1812200)"),
        (AsylumBasementMessageSpot, new(-12.83f, 185.25f, -27.54f), "basement message 9002"),
        (AsylumHallwayCorpseSpot, new(38.60f, 195.42f, 17.00f), "hallway-end class corpses"),
        (Asylum3FCorpseSpot, new(-10.27f, 208.62f, -32.52f), "3F third-item class corpses"),
        (AsylumRustedRingSpot, new(14.45f, 205.96f, 4.60f), "Rusted Iron Ring corpse o0500_0006 (RingPorter G9)"),
    ];
    // session 10 (asy-events e2, test ids 3700300-3700349 = the events track): door / ladder test warps, each 0.4 m over
    // the DS2 floor (rule P4), facing the door
    public const int AsylumInnerDoorInsideSpot = 3700300;  // inner-key door o8540_0005, inside (in DS1's 1812020), facing it (+X)
    public const int AsylumInnerDoorOutsideSpot = 3700301; // same door, courtyard side (in 1812021), facing it (-X)
    public const int AsylumFarDoorSpot = 3700302;          // hall side of the far door o8544 (BPK), at its K12 helper, facing it (-Z)
    public const int AsylumF2EastDoorSpot = 3700303;       // before the F2 East key door o8540_06 (2F), facing it (-Z)
    public const int AsylumF2WestDoorSpot = 3700304;       // before the F2 West key door o8540_0004 (2F), facing it (+Z)
    public const int AsylumPitLadderSpot = 3700305;        // return trip: the Stray Demon pit, 1 m before the pit ladder o8501_0000 (rule G4), facing it (+Z)

    static readonly Vector3 CliffEdge = new(3.5f, 210.0f, -96.0f), CliffSize = new(5f, 4f, 5f);
    static readonly Vector3 FirelinkLanding = new(-47.41f, -59.82f, 53.30f);
    const float FirelinkLandingYaw = 145f;

    /// <summary>The map's EventLocation param as built so far (MapBuilder writes it empty; EntryItem/GiantTree add rows).</summary>
    internal static PARAM Locations(Pack pack, MapBuilder.Options map, List<PARAMDEF> defs)
    {
        PARAM p;
        try { p = PARAM.Read(pack.GetFile("gamedata", $"param/eventlocation_{map.Ds2Map}.param")); if (!p.ApplyParamdefCarefully(defs)) throw new InvalidDataException(); }
        catch (Exception) // not there yet, or row-less (SoulsFormats can't read a row-less param back): the template's, cleared
        {
            p = PARAM.Read(Path.Combine(pack.Ds2Data, "param", $"eventlocation_{map.TemplateMap}.param"));
            if (!p.ApplyParamdefCarefully(defs)) throw new InvalidDataException($"no paramdef fits EventLocation_{map.TemplateMap}");
            p.Rows.Clear();
        }
        return p;
    }

    internal static PARAM.Row Template(Pack pack, List<PARAMDEF> defs, string map, int id)
    {
        var p = PARAM.Read(Path.Combine(pack.Ds2Data, "param", $"eventlocation_{map}.param"));
        if (!p.ApplyParamdefCarefully(defs)) throw new InvalidDataException($"no paramdef fits EventLocation_{map}");
        return p[id] ?? throw new InvalidDataException($"EventLocation_{map} has no row {id}");
    }

    internal static void Put(PARAM p, PARAM.Row template, int id, Vector3 pos, Vector3 rot, Vector3? scale = null)
    {
        p.Rows.RemoveAll(r => r.ID == id);
        var r = new PARAM.Row(template) { ID = id };
        r["PositionX"].Value = pos.X; r["PositionY"].Value = pos.Y; r["PositionZ"].Value = pos.Z;
        r["RotationX"].Value = rot.X; r["RotationY"].Value = rot.Y; r["RotationZ"].Value = rot.Z;
        if (scale is { } s) { r["ScaleX"].Value = s.X; r["ScaleY"].Value = s.Y; r["ScaleZ"].Value = s.Z; }
        p.Rows.Add(r);
        p.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
    }

    /// <summary>Firelink's arrival point and bonfire spot; the Asylum's cliff-edge box. Run after EntryItem (it writes the Asylum's EventLocation).</summary>
    public static void Build(Pack pack, MapBuilder.Options asylum, MapBuilder.Options firelink)
    {
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        var point = Template(pack, defs, "m20_10_00_00", 3700000);  // a Memory's arrival point (formType 1)
        var box = Template(pack, defs, "m10_02_00_00", 700000);      // a Things Betwixt box region (formType 3)

        // session 9 (track npcplace): the maps' DS2 collision as written (rule P4: test spots 0.4 m over the floor, on a
        // walkable patch; rule P1: DS1 player/character points take DS1 yaw + 180), Ds1Placement
        var flProbe = FloorProbe.FromPack(pack, firelink.Ds2Map);
        var alProbe = FloorProbe.FromPack(pack, asylum.Ds2Map);
        Vector3 Spot(FloorProbe probe, int id, Vector3 p)
        {
            if (Ds1Placement.Spot(probe, p) is { } s) return s;
            pack.Note($"test warp {id}: no DS2 floor within -2..+1 m of {p}: kept as authored (spot-check reports it)");
            return p;
        }
        var flMsb = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", firelink.Ds1Map + ".msb"));
        var fl = Locations(pack, firelink, defs);
        // DS1's start point c0000_0000 (rot 145: DS1 faces compass -35, towards the shrine), rule P1
        Put(fl, point, FirelinkArrival, FirelinkLanding, new Vector3(0, Ds1Placement.Yaw(FirelinkLandingYaw), 0));
        Put(fl, point, FirelinkBonfireSpot, firelink.BonfirePos + new Vector3(0, 0, 1.0f), new Vector3(0, 180, 0));
        // test warp: on the graveyard path, 2.5 m before the first skeleton (-53.07, -61.59, 111.87; it stands there,
        // run s5-40), facing it (+Z). (6 m before it, MP fell through the ground in run s5-39.) Session 9: the authored
        // spot (-53.07, -61.3, 109.4) is 0.36 m over the floor but 0.5-1 m from the path's west edge (void below, run s9-1:
        // MP fell out of the world): the nearest walkable patch (rule P4)
        var graveyard = new Vector3(-53.07f, -61.3f, 109.4f);
        Put(fl, point, FirelinkGraveyardSpot, Ds1Placement.SafeSpot(flProbe, graveyard) ?? Spot(flProbe, FirelinkGraveyardSpot, graveyard), new Vector3(0, 0, 0));
        // test warp: the terrace between Firelink's Undead Soldier (-23.40, -49.67, 16.15) and a Hollow Warrior
        // (-29.79, -49.61, 24.88), both at the same height (session 6)
        Put(fl, point, FirelinkStairsSpot, Spot(flProbe, FirelinkStairsSpot, new Vector3(-26.6f, -49.3f, 20.5f)), new Vector3(0, 180, 0));
        // session 8 fix pass: the DS1 Firelink regions the Asylum's translated events warp to (the nest's landing 1022121
        // on the ledge, facing as in DS1: rule P1), as points: id 3701000 + entity % 1000, the same region rule as the Asylum's
        foreach (var (entity, pos, yaw) in WarpTargets())
            Put(fl, point, AsylumScript.RegionId(entity), pos, new Vector3(0, Ds1Placement.Yaw(yaw), 0));
        // NPC test warps (rule P4): 2 m in front of the Crestfallen Warrior, Petrus and the Undead Merchant, facing them;
        // Anastacia is behind her cell's bars: at DS1's talk dummy for her (c1000_0001 on the landing outside), facing her
        NpcSpot(pack, fl, point, flProbe, flMsb, FirelinkCrestfallenSpot, 6270);
        NpcSpot(pack, fl, point, flProbe, flMsb, FirelinkPetrusSpot, 6080);
        NpcSpot(pack, fl, point, flProbe, flMsb, FirelinkMerchantSpot, 6240);
        {
            var anastacia = flMsb.Parts.Enemies.First(e => e.EntityID == 6060).Position;
            var dummy = flMsb.Parts.Enemies.First(e => e.Name == "c1000_0001").Position;
            Put(fl, point, FirelinkAnastaciaSpot, Spot(flProbe, FirelinkAnastaciaSpot, dummy), new Vector3(0, Ds1Placement.YawToward(anastacia - dummy), 0));
        }
        pack.AddFile("gamedata", $"param/eventlocation_{firelink.Ds2Map}.param", fl.Write());

        var al = Locations(pack, asylum, defs);
        Put(al, box, CrowRegion, CliffEdge, Vector3.Zero, CliffSize);
        // DS1's landing region 1812110 (rot 180: DS1 faces +Z, away from the edge; rule P1)
        Put(al, point, AsylumCliffLanding, new Vector3(3.27f, 209.84f, -83.48f), new Vector3(0, Ds1Placement.Yaw(180), 0));
        // test warp: 3 m before the doorway at the top of the boulder-corridor stairs (DS1's start-disabled collision
        // h0550B1_0000 at x 15, z -26 was an invisible wall there; session 6), facing +X along the corridor (session 9:
        // DS2 yaw θ faces compass θ, so +X is 90; it was -90)
        // test warp: inside the start cell, 0.8 m before its barred door (DS1 o8540_0003 at (-14.50, 185.24, -42.47)), facing
        // it (+Z: yaw 0; it was 180, "warp faces the wall" in session 8). Session 9: the doorway floor there is 185.25, the
        // authored 184.8 was 0.45 m inside it (spot-check FAIL "under"); rule P4 puts it 0.4 m over 185.25
        Put(al, point, AsylumCellDoorSpot, Spot(alProbe, AsylumCellDoorSpot, new Vector3(-13.8f, 184.8f, -43.3f)), new Vector3(0, 0, 0));
        // test warp: 1 m in front of DS1's pool ladder o8500_0000 ((-0.31, 189.49, 21.99), rot 135; its bottom dummy side
        // is model -Z = world (-0.71, +0.71)), facing it (compass 135; it was -45 = away from the ladder)
        Put(al, point, AsylumLadderSpot, Spot(alProbe, AsylumLadderSpot, new Vector3(-1.02f, 189.6f, 22.70f)), new Vector3(0, 135, 0));
        Put(al, point, AsylumBoulderSpot, Spot(alProbe, AsylumBoulderSpot, new Vector3(12f, 198.6f, -25.5f)), new Vector3(0, 90, 0));
        // session 7: the Asylum script's trigger boxes (DS1 regions) and test warps for it
        var msb1 = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", asylum.Ds1Map + ".msb"));
        // session 8: every DS1 region shape with DS1's rotation, points as points, plus the translator's rule-made boxes
        // (collision bounds, white-door crossing boxes). Session 9 (fix pass): rule P1 for regions: a DS1 point marker
        // (the Asylum has no entity Point regions; its places for characters are Box(0.1, 1, 0.1): 1812100 start, 1812110
        // landing, 1812011, 1812300-02, 1812305 the demon's drop …) takes yaw + 180; areas keep DS1's rotation
        // (Ds1Placement.TakesPlacementYaw)
        var placements = msb1.Regions.GetEntries().Where(r => Ds1Placement.TakesPlacementYaw(r, Program.DefaultDsrRoot)).Select(r => AsylumScript.RegionId(r.EntityID)).ToHashSet();
        foreach (var (id, pos, rot, size, isPoint) in AsylumScript.Regions(msb1))
        {
            var facing = placements.Contains(id) ? rot with { Y = Ds1Placement.Yaw(rot.Y) } : rot;
            if (isPoint) Put(al, point, id, pos, facing); else Put(al, box, id, pos, facing, size);
        }
        // session 9 (track firelink): the DS1 Asylum regions Firelink's translated events warp to (its nest's landing
        // 1812110), as points: the WarpTargets rule the other way round; facing by rule P1 (DS1 point markers: DS1 yaw + 180)
        foreach (var (entity, pos, yaw) in FirelinkScript.AsylumWarpTargets())
            Put(al, point, AsylumScript.RegionId(entity), pos, new Vector3(0, Ds1Placement.Yaw(yaw), 0));
        // test warp: in the courtyard before the hall door (DS1 o8541 at (3.19, 198.08, -4.03)), facing it (-Z). Session 9:
        // 3 m before it was ~0.6 m outside the door's "Open" range (it ends ~2.4 m in front of the door, run s8-5): now
        // 1.7 m before it (z -2.3), 0.4 m over the floor (rule P4)
        Put(al, point, AsylumHallDoorSpot, Spot(alProbe, AsylumHallDoorSpot, new Vector3(3.19f, 198.2f, -2.3f)), new Vector3(0, 180, 0));
        // test warp: in the hall 3 m before the escape gate (o8542 at (15.37, 198.38, -25.53)), facing it (+X)
        Put(al, point, AsylumGateSpot, new Vector3(12.0f, 198.3f, -25.5f), new Vector3(0, 90, 0));
        // test warp: 3.2 m behind the boss fog (o8950 at (3.16, 209.74, -34.26)) in the corridor, facing it (+Z); session 8,
        // track boss step 2: (3.16, -31.8) was over the hall (no floor above y 198: MP fell 12 m, s8-5); now in the
        // corridor behind the fog (o8950 at z -34.26): DS1 floor h1400B1 y 208.94 at z -37.5 (boss-drop-path --floor), facing
        // the fog (+Z), so walking forward crosses it from DS1's button side (1812998) onto the balcony
        Put(al, point, AsylumBossFogSpot, new Vector3(3.16f, 209.1f, -37.5f), new Vector3(0, 0, 0));
        // test warp: 2 m from the courtyard fog (o8952 at (5.87, 201.78, 19.63)) on its far side (+X), facing it (-X)
        Put(al, point, AsylumCourtyardFogSpot, Spot(alProbe, AsylumCourtyardFogSpot, new Vector3(7.9f, 201.9f, 19.63f)), new Vector3(0, -90, 0));
        // test warp: 2.5 m below the boulder's trigger box (DS1 1812120 at (-8, 203.22, 7.17)), facing up the stairs (-Z)
        // (session 9: it hung 1.41 m over the stairs; rule P4)
        Put(al, point, AsylumBoulderTriggerSpot, Spot(alProbe, AsylumBoulderTriggerSpot, new Vector3(-8f, 203.4f, 10.2f)), new Vector3(0, 180, 0));
        // 2 m in front of Oscar ((-4.91, 201.96, 21.49), DS1 rot 20: he faces compass -160, the rubble behind him), facing
        // him (rule P4)
        NpcSpot(pack, al, point, alProbe, msb1, AsylumOscarSpot, 6023);
        // session 10 (asy-items, rule P4): 1.5 m from each class-corpse spot / message, on a walkable patch with nothing
        // between, 0.4 m over the DS2 floor, facing it
        foreach (var (id, target, what) in ItemSpots)
        {
            if (Ds1Placement.FrontSpot(alProbe, target, 0, 1.5f) is { } s)
            {
                Put(al, point, id, s.Pos, new Vector3(0, s.Yaw, 0));
                pack.Note($"test warp {id}: {Vector3.Distance(s.Pos with { Y = target.Y }, target):0.0} m from the {what} at {s.Pos:0.00}, yaw {s.Yaw:0.#}");
            }
            // a ledge (Snuggly's nest on the roof edge): no 1.5 m patch around it → rule P4's TestSpot on the target itself
            else if (Ds1Placement.TestSpot(alProbe, target) is { } t)
            {
                Put(al, point, id, t, Vector3.Zero);
                pack.Note($"test warp {id}: no 1.5 m walkable spot around the {what}: on it (rule P4 TestSpot) at {t:0.00}");
            }
            else pack.Note($"test warp {id}: no walkable spot near the {what} ({target}); left out");
        }
        // session 9 (rule P4): the two spots written above as authored (3700160; 3700170 = the boss track's corridor spot,
        // its lines kept verbatim so both tracks merge) 0.4 m over the DS2 floor on a walkable patch
        foreach (int id in new[] { AsylumGateSpot, AsylumBossFogSpot }) Respot(pack, al, alProbe, id);
        // session 10 (asy-events e2): door / ladder test warps. Inner door o8540_0005 at (-9.34, 196.06, -1.41) yaw -90, its
        // doorway centre z -0.72: inside 1.3 m west (DS1 1812020 spans x -11.95..-9.45), outside 1.3 m east (1812021);
        // far door o8544 (3.19, 198.08, -33.42) yaw 180: 1.1 m on the hall side (the K12 helper is at z -32.79); F2 East
        // o8540_06 (-8.60, 208.43, -8.65) yaw 0, approached from +Z; F2 West o8540_0004 (14.99, 208.41, -21.60) yaw 180,
        // approached from -Z (doors.md T5/T6 paths); the pit ladder o8501_0000 (-7.43, 182.25, -6.62) yaw 0: its
        // bottom-dummy side is model -Z (as the pool ladder's)
        foreach (var (id, p, yaw) in new (int, Vector3, float)[]
        {
            (AsylumInnerDoorInsideSpot, new(-10.64f, 196.5f, -0.72f), 90), (AsylumInnerDoorOutsideSpot, new(-8.04f, 196.4f, -0.72f), -90),
            (AsylumFarDoorSpot, new(3.19f, 198.5f, -32.3f), 180), (AsylumF2EastDoorSpot, new(-8.60f, 208.8f, -7.4f), 180),
            (AsylumF2WestDoorSpot, new(14.99f, 208.8f, -22.8f), 0), (AsylumPitLadderSpot, new(-7.43f, 182.6f, -7.62f), 0),
        })
            Put(al, point, id, Spot(alProbe, id, p), new Vector3(0, yaw, 0));
        pack.AddFile("gamedata", $"param/eventlocation_{asylum.Ds2Map}.param", al.Write());
        pack.Note($"link: {asylum.Ds2Map} cliff edge (EventLocation {CrowRegion} at {CliffEdge}) → {firelink.Ds2Map} location {FirelinkArrival} at {FirelinkLanding} (event group {CrowGroup})");
        Console.WriteLine($"  link: {asylum.AreaName} cliff edge → {firelink.AreaName} (crow, no animation)");
    }

    /// <summary>Session 9 (track npcplace), rule P4: a test warp 2 m in front of the DS1 character <paramref name="entity"/>
    /// (its DS1 facing, rule P1), on a walkable patch with nothing between, 0.4 m over the DS2 floor, facing it.</summary>
    static void NpcSpot(Pack pack, PARAM p, PARAM.Row point, FloorProbe probe, MSB1 msb, int id, int entity)
    {
        var e = msb.Parts.Enemies.First(x => x.EntityID == entity);
        if (Ds1Placement.FrontSpot(probe, e.Position, e.Rotation.Y) is { } s)
        {
            Put(p, point, id, s.Pos, new Vector3(0, s.Yaw, 0));
            pack.Note($"test warp {id}: {Vector3.Distance(s.Pos with { Y = e.Position.Y }, e.Position):0.0} m from DS1 character {entity} ({e.Name}, faces compass {Ds1Placement.YawToward(Ds1Placement.Facing(e.Rotation.Y)):0}) at {s.Pos:0.00}, yaw {s.Yaw:0.#}");
        }
        else pack.Note($"test warp {id}: no walkable spot in front of DS1 character {entity} ({e.Name}); left out");
    }

    /// <summary>Session 9 (rule P4): an EventLocation row already written, moved to <see cref="Ds1Placement.TestSpot"/> (0.4 m
    /// over the DS2 floor, on a walkable patch; x/z kept when the patch is there); facing unchanged.</summary>
    static void Respot(Pack pack, PARAM p, FloorProbe probe, int id)
    {
        if (p[id] is not { } r) return;
        var pos = new Vector3(Convert.ToSingle(r["PositionX"].Value), Convert.ToSingle(r["PositionY"].Value), Convert.ToSingle(r["PositionZ"].Value));
        if (Ds1Placement.TestSpot(probe, pos) is not { } s) { pack.Note($"test warp {id}: no DS2 floor within -2..+1 m of {pos}: kept as authored (spot-check reports it)"); return; }
        r["PositionX"].Value = s.X; r["PositionY"].Value = s.Y; r["PositionZ"].Value = s.Z;
        pack.Note($"test warp {id}: {pos:0.00} → {s:0.00} (rule P4)");
    }

    static List<(int Entity, Vector3 Pos, float Yaw)>? warpTargets;

    /// <summary>
    /// DS1 regions in Firelink (m10_02_00_00) that the Asylum's script warps to with 2002:4 Play Cutscene and Warp Player
    /// (11810000 → 1022100 the start point, which is <see cref="FirelinkArrival"/>; 11810150, the nest → 1022121 "where
    /// you land from the tutorial the second time on", (-43.56, -30.85, 100.85), facing -150°), except the start point.
    /// Pure (read from DS1's files), so the translator's context and this builder agree.
    /// </summary>
    public static List<(int Entity, Vector3 Pos, float Yaw)> WarpTargets()
    {
        if (warpTargets != null) return warpTargets;
        var defs = Commands.EmevdDump.Defs();
        var emevd = EMEVD.Read(Path.Combine(Program.DefaultDsrRoot, "event", "m18_01_00_00.emevd.dcx"));
        var ids = emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2002 && i.ID == 4).Select(i => Events.EmevdTranslator.Decode(defs, i))
            .Where(v => v.Length >= 5 && (int)v[3] == 10 && (int)v[4] == 2 && (int)v[2] != 1022100).Select(v => (int)v[2]).ToHashSet();
        var fl = MSB1.Read(Path.Combine(Program.DefaultDsrRoot, "map", "MapStudio", "m10_02_00_00.msb"));
        warpTargets = fl.Regions.GetEntries().Where(r => ids.Contains(r.EntityID)).Select(r => (r.EntityID, r.Position, r.Rotation.Y)).ToList();
        return warpTargets;
    }

    /// <summary>The Asylum's crow event: 0 → 1: wait for the player in the cliff-edge box → 2: no multiplayer, warp → 3.</summary>
    public static Dictionary<long, ESD.State> CrowGroupStates(int firelinkMapId)
    {
        byte[] True = Convert.FromHexString("41A1");
        byte[] Group0 = Convert.FromHexString("82E12202004085A6A1");        // f140001(0): condition group 0 met
        byte[] WarpDone = Convert.FromHexString("820504020084A64096A1");
        ESD.CommandCall C(int id, params byte[][] args) => new(1, id, args);
        ESD.State S(IEnumerable<ESD.CommandCall> entry, params (byte[] If, long Then)[] conds)
        {
            var s = new ESD.State();
            s.EntryCommands.AddRange(entry);
            foreach (var (cond, then) in conds) s.Conditions.Add(new ESD.Condition(then, cond));
            return s;
        }
        return new Dictionary<long, ESD.State>
        {
            [0] = S([], (True, 1)),
            [1] = S([C(140132, Int(0), Int(CrowRegion), Int(CrowRegion), Int(1))], (Group0, 2)),
            // session 8 (cutscene track): DS1's crow scenes as the warp's pre/post demos when the build converts them
            [2] = S([C(130613, Int(1)), C(132125, Int(Cutscenes.Cutscenes.Demo(180125) ?? 0), Int(Cutscenes.Cutscenes.Demo(100225) ?? 0), Int(firelinkMapId), Int(FirelinkArrival), Int(0))], (WarpDone, 3)),
            [3] = S([]),
        };
    }

    static byte[] Int(int v) => v is >= 0 and < 64 ? [(byte)(0x40 + v), 0xA1] : [0x82, .. BitConverter.GetBytes(v), 0xA1];
}
