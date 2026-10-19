using System.Numerics;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Session 11 (burg11, BURG-PLAN M1 track seam): walking links between ported DS1 maps, the DS2 way (no load screen, never
/// a warp). DS1 joins its maps of one area in one world frame with ConnectCollision parts (Firelink m10_02 ↔ Burg m10_01:
/// aqueduct/tunnel, the Lower-Burg rooms, the Parish lift shaft; V-data), and so does DS2 (research\seam.md, V-code):
/// while the player stands on collision X, every enabled ConnectCollision of X loads its target map (FUN_1403dc930: a map is
/// wanted when its slot mask ≠ 0) and switches on the target's parts whose groups intersect the connect's Unk44..50 mask
/// (FUN_1401c8cd0); the current map is the map owning the collision under the player (FUN_1403dc3e0).
/// SS1 (session 11, seam12; proven missing by the m19_42 → m19_41 play test: m19_41's MSB never opened): FUN_1401c8cd0 only
/// ORs masks into slots whose avail byte is set, and avail = FUN_1402db4f0(map) = "map in DS2's hard-coded 28-map PlayGo chunk
/// table" (FUN_141073830) — never true for our m18/m19 hosts. The loader's seam stream gate (src\loader\stream.c) answers 1
/// for areas 18/19; `lr seam-stream-check` checks connects, boxes and the loader against that model. Rules:
///  - SM1: area-10 DS1 maps share one frame: MapOffset 0 on every host, no transform (MapBuilder already does it).
///  - CC1: each DS1 ConnectCollision whose collision this build placed (filter 8, MapBuilder.PlacedCollisions) and whose
///    target DS1 map is built into a DIFFERENT host → an MSB2 ConnectCollision on that collision, MapID = the target host
///    [AA, BB, CC, DD], T08/T0C 0/0 (no selector flag), DrawGroups/DispGroups 0 (as vanilla), mask Unk44..50 = all bits (the
///    neighbour's parts carry the template's groups and our objects all bits: the whole neighbour shows, until DG1 ports
///    DS1's draw groups). Same-host targets are dropped; unbuilt targets and connects on unplaced collisions are logged
///    (they feed BD1 / C2b).
///  - SB1: one category-23 EventLocation box ("backread safety", FUN_1401ea510: holds the player while the neighbour's
///    parts in the connect's mask are not loaded yet; it never loads anything itself) per connect-bearing collision that
///    touches the other side, placed on that collision's floor at the point nearest the neighbour's back-connects, 8 × 4 ×
///    8 m (vanilla 5×3×7 / 10×5×9), categoryId = the neighbour host's decimal id, memoryBlockId = the collision's UnkT12
///    (BloodMessages rule M0), ids AABB00 + n (Majula 100401, FoFG 101000; V-data); boxes within 8 m of one another merged.
/// </summary>
public static class MapSeams
{
    const int BoxIdBase = 0; // AABB00 + n
    /// <summary>Test warp on seam box n of a map (session 11 burg11 range 3700450-3700499).</summary>
    public const int SeamSpot = 3700450;
    static readonly Vector3 BoxSize = new(8f, 4f, 8f);
    const float NearBack = 25f; // a connect collision this close to a back-connect of the neighbour is a crossing (m)

    public sealed record Link(string FromHost, string Ds1Collision, string Ds2Collision, string ToHost, string ToDs1);

    /// <summary>All seams between the given built maps; returns the build-log lines.</summary>
    public static List<string> Build(Pack pack, string dsrRoot, IReadOnlyList<MapBuilder.Options> maps)
    {
        var log = new List<string>();
        var links = new List<Link>();
        var majula = MSB2.Read(Path.Combine(pack.Ds2Data, "map", "m10_04_00_00", "m10_04_00_00.msb"));
        var tConnect = majula.Parts.ConnectCollisions.First();
        foreach (var x in maps)
        {
            var msb1 = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", x.Ds1Map + ".msb"));
            var msb2 = MSB2.Read(pack.GetFile("gamedata", $"map/{x.Ds2Map}/{x.Ds2Map}.msb"));
            int added = 0;
            foreach (var c in msb1.Parts.ConnectCollisions)
            {
                // DS1 writes the target as [AA, BB, 255, 255] (any block): the DS1 map mAA_BB_00_00
                string target = $"m{c.MapID[0]:D2}_{c.MapID[1]:D2}_{(c.MapID[2] == 255 ? 0 : c.MapID[2]):D2}_{(c.MapID[3] == 255 ? 0 : c.MapID[3]):D2}";
                var to = maps.FirstOrDefault(m => m.Ds1Map.Equals(target, StringComparison.OrdinalIgnoreCase));
                string? toHost = to?.Ds2Map ?? MapHosts.Of(target)?.Host;
                if (to == null) { log.Add($"seam CC1: {x.Ds2Map} DS1 connect {c.Name} ({c.CollisionName}) → {target}: not built in this pack, dropped (border, BD1)"); continue; }
                if (toHost == x.Ds2Map) { log.Add($"seam CC1: {x.Ds2Map} DS1 connect {c.Name} → {target}: same host, dropped"); continue; }
                if (!MapBuilder.PlacedCollisions.TryGetValue((x.Ds2Map, c.CollisionName), out var ds2Col))
                {
                    var dc = msb1.Parts.Collisions.FirstOrDefault(p => p.Name == c.CollisionName);
                    log.Add($"seam CC1: {x.Ds2Map} DS1 connect {c.Name} on {c.CollisionName} (hit filter {dc?.HitFilterID}) → {target}: collision not placed, dropped (rule C2b decides the filter-17 shaft planes)");
                    continue;
                }
                if (msb2.Parts.ConnectCollisions.Any(k => k.CollisionName == ds2Col && k.MapID.SequenceEqual(HostBytes(toHost!)))) continue;
                var col = msb2.Parts.Collisions.First(p => p.Name == ds2Col);
                var k = (MSB2.Part.ConnectCollision)tConnect.DeepCopy();
                k.Name = $"{col.ModelName}_{9000 + added:D4}";
                k.ModelName = col.ModelName;
                k.CollisionName = ds2Col;
                HostBytes(toHost!).CopyTo(k.MapID, 0);
                k.UnkT08 = 0; k.UnkT0C = 0;
                k.Position = Vector3.Zero; k.Rotation = Vector3.Zero; k.Scale = Vector3.One;
                Array.Clear(k.DrawGroups); Array.Clear(k.DispGroups);
                k.Unk44 = -1; k.Unk48 = -1; k.Unk4C = -1; k.Unk50 = -1; // the whole neighbour (all group bits)
                if (x.Dg1 && to.Dg1)
                {
                    // session 11 (burg11, round 9), rule DG1: the neighbour's groups DS1's connect shows (its DispGroups, in the
                    // target's merged bit space: rule H2-G)
                    var mask = H2Merge.MapGroups(target, c.DispGroups);
                    k.Unk44 = unchecked((int)mask[0]); k.Unk48 = unchecked((int)mask[1]); k.Unk4C = unchecked((int)mask[2]); k.Unk50 = unchecked((int)mask[3]);
                }
                if (!msb2.Models.Collisions.Any(m => m.Name == col.ModelName)) throw new InvalidDataException($"{x.Ds2Map}: no collision model {col.ModelName}");
                msb2.Parts.ConnectCollisions.Add(k);
                links.Add(new Link(x.Ds2Map, c.CollisionName, ds2Col, toHost!, target));
                // session 11 (burg11, play-test fix), rule SR3c: the step ramps lying on this collision carry the same connect
                // (on a ramp the player stands on the ramp's collision: without it the neighbour unloaded mid-seam, the
                // play test's "half the environment turned invisible" on the Firelink stairs)
                foreach (var (key, baseCol) in MapBuilder.RampBase.Where(r => r.Key.Map == x.Ds2Map && r.Value == c.CollisionName).ToList())
                {
                    var rc = msb2.Parts.Collisions.FirstOrDefault(p => p.Name == key.Ramp);
                    if (rc == null || msb2.Parts.ConnectCollisions.Any(q => q.CollisionName == rc.Name && q.MapID.SequenceEqual(k.MapID))) continue;
                    var rk = (MSB2.Part.ConnectCollision)k.DeepCopy();
                    rk.Name = $"{rc.ModelName}_{9000 + added + 500:D4}";
                    rk.ModelName = rc.ModelName; rk.CollisionName = rc.Name;
                    msb2.Parts.ConnectCollisions.Add(rk);
                    log.Add($"seam SR3c: {x.Ds2Map} {rk.Name} on ramp {rc.Name} (lies on DS1 {c.CollisionName}) → {toHost}");
                }
                log.Add($"seam CC1: {x.Ds2Map} {k.Name} on {ds2Col} (DS1 {c.CollisionName}, connect {c.Name}) → {toHost} (DS1 {target})");
                added++;
            }
            pack.AddFile("gamedata", $"map/{x.Ds2Map}/{x.Ds2Map}.msb", msb2.Write());
        }

        // ---- SB1: category-23 boxes
        var defs = Directory.GetFiles(Path.Combine(Formats.ParamStore.SmithboxParams, "DS2S", "Defs"), "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();
        var template = MapLinks.Template(pack, defs, "m10_04_00_00", 100401);
        var probes = maps.ToDictionary(m => m.Ds2Map, m => FloorProbe.FromPack(pack, m.Ds2Map));
        foreach (var x in maps)
        {
            var mine = links.Where(l => l.FromHost == x.Ds2Map).ToList();
            if (mine.Count == 0) continue;
            var p = MapLinks.Locations(pack, x, defs);
            var msb2 = MSB2.Read(pack.GetFile("gamedata", $"map/{x.Ds2Map}/{x.Ds2Map}.msb"));
            var blockOf = msb2.Parts.Collisions.ToDictionary(c => c.Name, c => (int)c.UnkT12);
            var boxes = new List<(Vector3 Pos, string To, string Col)>();
            foreach (var l in mine)
            {
                // the neighbour's collisions that connect back to this host (the other side of the crossing)
                var back = links.Where(b => b.FromHost == l.ToHost && b.ToHost == x.Ds2Map).Select(b => b.Ds2Collision).ToHashSet();
                var backTris = probes[l.ToHost].Triangles.Where(t => back.Contains(t.Part)).ToList();
                var myTris = probes[x.Ds2Map].Triangles.Where(t => t.Part == l.Ds2Collision && Normal(t).Y >= 0.7f).ToList();
                if (myTris.Count == 0 || backTris.Count == 0) { log.Add($"seam SB1: {x.Ds2Map} {l.Ds2Collision} → {l.ToHost}: {(myTris.Count == 0 ? "no walkable floor" : "no back-connect on the other side")}, no box"); continue; }
                var backPts = backTris.Select(Centre).ToList();
                (Vector3 Pt, float D) best = (default, float.MaxValue);
                foreach (var t in myTris)
                {
                    var c = Centre(t);
                    // the box must stand where this collision is the floor (the runtime reads the collision under the
                    // player: a step ramp or another part over it would hide the connect)
                    if (probes[x.Ds2Map].Floor(c + new Vector3(0, 0.3f, 0), above: 0.5f, below: 1f) is not { } top || top.Part != l.Ds2Collision) continue;
                    float d = backPts.Min(b => Vector3.Distance(b, c));
                    if (d < best.D) best = (c, d);
                }
                if (best.D > NearBack) { log.Add($"seam SB1: {x.Ds2Map} {l.Ds2Collision} → {l.ToHost}: nearest back-connect {best.D:0.0} m away, not a crossing, no box"); continue; }
                if (boxes.Any(b => b.To == l.ToHost && Vector3.Distance(b.Pos, best.Pt) < 8f)) continue;
                boxes.Add((best.Pt, l.ToHost, l.Ds2Collision));
            }
            int aabb = MapHosts.Aabb(x.Ds2Map);
            for (int n = 0; n < boxes.Count; n++)
            {
                var (pos, to, col) = boxes[n];
                int id = aabb * 100 + BoxIdBase + n;
                if (n > 99) throw new InvalidDataException($"seam SB1: {x.Ds2Map} has more than 100 seam boxes");
                MapLinks.Put(p, template, id, pos + new Vector3(0, BoxSize.Y / 2 - 0.5f, 0), Vector3.Zero, BoxSize);
                var row = p[id]!;
                Set(row, "categoryType", 23);
                Set(row, "categoryId", MapId(to));
                Set(row, "memoryBlockId", blockOf.GetValueOrDefault(col, 0));
                // test warp on the seam box (rule P4; 3700450 + n, this track's range): the in-game walks across start here
                if (n < 50 && Ds1Placement.TestSpot(probes[x.Ds2Map], pos + new Vector3(0, 0.4f, 0)) is { } spot)
                {
                    MapLinks.Put(p, MapLinks.Template(pack, defs, "m20_10_00_00", 3700000), SeamSpot + n, spot, Vector3.Zero);
                    log.Add($"seam test warp {SeamSpot + n} in {x.Ds2Map} at {spot:0.00} (seam box {id} → {to})");
                }
                log.Add($"seam SB1: {x.Ds2Map} EventLocation {id} (category 23 → {MapId(to)}, memory block {blockOf.GetValueOrDefault(col, 0)}) at {pos:0.00} on {col}, {BoxSize.X}×{BoxSize.Y}×{BoxSize.Z} m");
            }
            pack.AddFile("gamedata", $"param/eventlocation_{x.Ds2Map}.param", p.Write());
        }
        foreach (var l in log) pack.Note(l);
        int cc = links.Count;
        return [$"seams: {cc} ConnectCollisions ({string.Join(", ", links.GroupBy(l => (l.FromHost, l.ToHost)).Select(g => $"{g.Key.FromHost}→{g.Key.ToHost} {g.Count()}"))}), {log.Count(l => l.StartsWith("seam SB1") && l.Contains("category 23"))} category-23 boxes"];
    }

    /// <summary>
    /// Rule SD0 (session 11, burg11; interim until M2's ObjAct/event rules port the map's doors): a DS1 door that stands
    /// at a map seam — an Object part (not Dummy) with an entity, an animated model with DS1 collision, within 2 m of a
    /// collision carrying a DS1 ConnectCollision to another DS1 map — is placed CLOSED as a static object with DS1's
    /// collision (ObjPorter Kind.Static on o16_1230, instance AABB0000 + entity % 10000). Without it an empty map's
    /// doorway would be open: the Lower-Burg one-way door 1011320 (closed from Firelink's tunnel in DS1) and the doors
    /// into unported maps (rule BD1: Depths 1011306, Darkroot Basin 1011316 stay shut). A stand-in: the ledger line
    /// "SD0" names each one; M2 replaces it by the door's DS1 behaviour.
    /// </summary>
    /// <param name="target">M2 (rule BD1 via MapObjects): only seams whose DS1 target map ("mAA_BB_00_00") passes, e.g. the
    /// maps this build does not port; null = every seam (the M1 interim).</param>
    public static List<(MSB1.Part.Object Part, string Ds2Model)> SeamDoors(string dsrRoot, string ds1Map, string ds2Map, MSB1 msb1, Func<string, bool>? target = null)
    {
        var connected = msb1.Parts.ConnectCollisions.Where(c => !(c.MapID[0] == byte.Parse(ds1Map[1..3]) && c.MapID[1] == byte.Parse(ds1Map[4..6])))
            .Where(c => target == null || target($"m{c.MapID[0]:D2}_{c.MapID[1]:D2}_00_00"))
            .Select(c => c.CollisionName).ToHashSet();
        if (connected.Count == 0) return [];
        var probe = FloorProbe.FromDs1(dsrRoot, ds1Map, c => connected.Contains(c.Name));
        string aa = ds2Map.Substring(1, 2);
        var list = new List<(MSB1.Part.Object, string)>();
        foreach (var p in msb1.Parts.Objects.Where(p => p.EntityID > 0))
        {
            string bnd = Path.Combine(dsrRoot, "obj", p.ModelName + ".objbnd.dcx");
            if (!File.Exists(bnd)) continue;
            var files = BND3.Read(DCX.Decompress(File.ReadAllBytes(bnd))).Files.Select(f => Path.GetFileName(f.Name).ToLowerInvariant()).ToList();
            bool collision = files.Contains(p.ModelName.ToLowerInvariant() + ".hkx"), animated = files.Any(f => f.EndsWith(".anibnd"));
            if (!collision || !animated) continue;
            // within 3 m of a vertex of a connect-bearing collision (the door may stand on the NEIGHBOUR's floor: 1011320
            // stands on Firelink's tunnel floor, its own map's floor ends 1 m away)
            bool near = probe.Triangles.Any(t => new[] { t.A, t.B, t.C, (t.A + t.B + t.C) / 3 }.Any(v => Vector3.Distance(v, p.Position) <= 3f));
            if (near) list.Add((p, MapHosts.ObjectName(aa, p.ModelName)));
        }
        return list;
    }

    static byte[] HostBytes(string host) => [byte.Parse(host[1..3]), byte.Parse(host[4..6]), byte.Parse(host[7..9]), byte.Parse(host[10..12])];
    static int MapId(string host) => int.Parse(host[1..3] + host[4..6] + host[7..9] + host[10..12]);
    static Vector3 Centre(FloorProbe.Tri t) => (t.A + t.B + t.C) / 3;
    static Vector3 Normal(FloorProbe.Tri t) { var n = Vector3.Cross(t.B - t.A, t.C - t.A); return n.Length() > 0 ? Vector3.Normalize(n) * MathF.Sign(n.Y == 0 ? 1 : n.Y) : Vector3.Zero; }
    static void Set(PARAM.Row row, string field, int v) => row[field].Value = Convert.ChangeType(v, row[field].Value.GetType());
}
