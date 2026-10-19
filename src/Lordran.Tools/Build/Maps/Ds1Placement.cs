using System.Numerics;
using Lordran.Tools.Formats.Havok;
using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Session 9 (track npcplace): DS1 MSB placements → DS2 generator / EventLocation placements, by rule (evidence in
/// data\sandbox\npcplace\NOTES.md; tools `place-survey`, `spot-check`).
/// <para><b>P1 facing.</b> DS1: a character's MSB rotation Y = θ is a DirectX yaw (as for every DS1 part: the objects'
/// rotations carry over unchanged, V) applied to its model, and DS1 character models face model −Z (FLVER bind pose
/// toes−feet: c0000, c2500, c2510, c2790, c2230 all compass 180; DS1 clips walk along −Z, the merchant's walk −2.1 m),
/// so it faces compass θ+180 (compass a = atan2(x, z): the Crestfallen Warrior (θ 10) faces the bonfire at −174, Oscar
/// (θ 20) has the rubble behind him, Anastacia (θ 10) faces her bars and DS1's talk dummy; over 332 DS1 characters of
/// four maps 13 face a wall within 1 m that way, 32 the other way; the Undead Merchant (θ −125) then faces her alcove's
/// back wall: U, see NOTES §1). DS2: GeneratorLocation/EventLocation rotation (degrees) → FUN_1401c2e00 = DirectXMath
/// RollPitchYaw quaternion (generator object FUN_140411a40 → FUN_140413370; locations FUN_1401c1470) → the character's
/// matrix, whose third row (sin θ, 0, cos θ) is its forward (PlayerCtrl +0x80): a DS2 character faces compass θ (vanilla
/// Saulden faces the cliff 3 m ahead with a rock 0.8 m behind him, Maughlin his plaza with his house 2.4 m behind; the
/// player warp to 3700200 at yaw 200 faced away from Oscar, run s9-2; the user saw Oscar "reverse"). Rule: <b>DS2 yaw =
/// DS1 yaw + 180</b> for every character and every DS1 region DS1 puts a character at (<see cref="IsPlacementRegion"/>).</para>
/// <para><b>P2 height.</b> DS1 stands characters on their floor (DS1 roots vs DSR collision: 0.00-0.02 m) and seats
/// sitters where their idle clip needs them (Oscar's root 0.15 m over the rubble: a00_7805's pelvis is 0.04 m under the
/// root and 0.13 m ahead, his feet 0.33-0.41 m under it, down the rubble's slope; the Crestfallen Warrior's a00_7810
/// holds the pelvis 0.42 m up and 0.43 m behind his feet, on a step). DS2's own generators stand 0.05-0.06 m over the
/// floor (median, m10_04/m10_02; Saulden 0.062). A sitter's root goes where the idle DS2 plays puts the pelvis on DS1's
/// pelvis (<see cref="Sit"/>: the interim stand-in, Saulden's a77_03_0010, holds it 0.58 m up and 0.44 m behind the root,
/// a ledge sit; DS1's own clip (track npcidle) keeps DS1's root), the feet on the DS2 floor; everyone else
/// <see cref="Stand"/>: a root at or under the DS2 floor rises to the floor + 0.05, a root over it keeps DS1's height.
/// Both ray-cast the collision the pack ships (<see cref="FloorProbe.FromPack"/>) inside a window around DS1's height,
/// else DS1's root stays (a character DS1 keeps off its floor: Anastacia's cell has no DS1 collision, the demon starts
/// in the air).</para>
/// <para><b>P4 test spots.</b> Player test warps go 0.4 m over the DS2 floor (<see cref="SpotOverFloor"/>; 0.3-0.5 m:
/// the player drops onto it), on a walkable patch (every direction 0.5 m out stays within 0.35 m of the spot's floor), and
/// NPC spots stand 2 m in front of the NPC by P1, facing it (<see cref="FrontSpot"/>).</para>
/// </summary>
public static class Ds1Placement
{
    public const float StandOverFloor = 0.05f, SpotOverFloor = 0.4f;
    /// <summary>P3: a DS1 idle clip whose frame-0 pelvis is lower than this over the root sits, kneels or crouches (every
    /// DS1 c0000 78xx clip in an MSB: sits −0.04..0.21, crouch 0.29, kneels 0.43; the standing 78xx poses 0.91-0.97; chr
    /// NPCs' idles: Anastacia 0.18, the merchant 0.88) …</summary>
    public const float SitPelvisMax = 0.6f;
    /// <summary>… with the head at least this much over the pelvis (an upright, slumped or bowed torso: 7805 +0.52, 7810
    /// +0.48, the ground sits 7825-7835 +0.49-0.55, the bow 7816/7817 +0.23, Saulden's a77_03_0010 +0.39; DS1's lying
    /// poses 7890/7891 −0.18 and 7896-7898 +0.11 are not sits; `place-survey clips`).</summary>
    public const float SitHeadOverPelvisMin = 0.2f;

    /// <summary>The interim DS2 sit (GeneratorRegistParam.startUpStateType 13 → c0001 slot 13, a77_03_0010
    /// NPCIdleSaulden): frame-0 pelvis in DS1 model axes (x, up, +z = behind the character): 0.58 m up (`clip-survey
    /// c0001.anibnd.dcx --filter a77_0`: pelvisZ 0.58, head 0.97, feet 0.10) and 0.44 m behind the root (track npcidle's
    /// FK, work\idle2.log: Morpheme (−0.00, 0.44, 0.58), forward −Y): a ledge sit, feet on the ground at the root.</summary>
    public static readonly Vector3 SauldenSitPelvis = new(0, 0.58f, 0.44f);

    static float Norm(float a) { a %= 360; if (a > 180) a -= 360; if (a <= -180) a += 360; return a; }

    /// <summary>P1: DS2 GeneratorLocation/EventLocation yaw for a DS1 character/player rotation Y.</summary>
    public static float Yaw(float ds1Yaw) => Norm(ds1Yaw + 180);

    /// <summary>P1 + P2 (standing) for a generator built from a DS1 MSB character part: the custom still carries DS1's
    /// position and rotation Y; returns it with the DS2 yaw (DS1 + 180) and the root on the DS2 floor (<see cref="Stand"/>;
    /// <paramref name="probe"/> null: P1 only). The one call every DS1-sourced enemy/boss generator goes through
    /// (AsylumEnemies; the boss track's BossCustom: NOTES §5).</summary>
    public static EnemyTest.Custom Generator(FloorProbe? probe, EnemyTest.Custom ds1) =>
        ds1 with { Position = probe != null ? Stand(probe, ds1.Position, out _) : ds1.Position, Yaw = Yaw(ds1.Yaw) };

    // ---------------------------------------------------------------- P1 for DS1 regions
    /// <summary>DS1 instructions that put a character or the player at a region, with the region's rotation as its facing
    /// (ds1-common.emedf): bank, id, the argument indices that name the region, and the warp-type argument that must be 1
    /// (Area) or −1.</summary>
    static readonly (int Bank, int Id, int[] Region, int AreaType, string What)[] PlacementInstructions =
    [
        (2002, 2, [2], -1, "Play Cutscene and Warp All Players"), (2002, 4, [2], -1, "Play Cutscene and Warp Player"),
        (2002, 6, [2, 3], -1, "Play Cutscene and Randomly Warp Player"), (2002, 7, [2, 3], -1, "Play Cutscene and Randomly Warp Player"),
        (2003, 14, [2], -1, "Warp Player"), (2003, 20, [0], -1, "Set Temporary Player Respawn Point"), (2003, 23, [0], -1, "Set Player Respawn Point"),
        (2003, 25, [2], -1, "Place NPC Summon Sign (spawn point)"),
        (2004, 3, [2], 1, "Character Warp Request"), (2004, 40, [2], 1, "Warp Character and Set Floor"),
        (2004, 41, [2], 1, "Issue Short Warp Request"), (2004, 42, [2], 1, "Warp Character and Copy Floor"),
        (2004, 13, [1], -1, "Set Character Home"),
    ];

    static Dictionary<int, List<string>>? placementUses;

    /// <summary>Every DS1 region entity that a DS1 event script (every `event\*.emevd.dcx` of DSR, parameters substituted
    /// from each Initialize Event call) names as a <see cref="PlacementInstructions"/> destination, with where; cached.</summary>
    public static IReadOnlyDictionary<int, List<string>> PlacementUses(string dsrRoot)
    {
        if (placementUses != null) return placementUses;
        var defs = Commands.EmevdDump.Defs();
        var uses = new Dictionary<int, List<string>>();
        foreach (string file in Directory.GetFiles(Path.Combine(dsrRoot, "event"), "*.emevd.dcx").OrderBy(f => f))
        {
            string name = Path.GetFileName(file).Split('.')[0];
            var emevd = EMEVD.Read(file);
            var calls = emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2000 && i.ID == 0 && i.ArgData.Length >= 8)
                .GroupBy(i => (long)BitConverter.ToUInt32(i.ArgData, 4)).ToDictionary(g => g.Key, g => g.Select(i => i.ArgData[8..]).ToList());
            foreach (var ev in emevd.Events)
            {
                var argSets = calls.TryGetValue(ev.ID, out var l) ? l : [[]];
                for (int k = 0; k < ev.Instructions.Count; k++)
                {
                    var x = ev.Instructions[k];
                    foreach (var pi in PlacementInstructions.Where(p => p.Bank == x.Bank && p.Id == x.ID))
                        foreach (var args in argSets)
                        {
                            var y = new EMEVD.Instruction(x.Bank, x.ID, (byte[])x.ArgData.Clone());
                            foreach (var p in ev.Parameters.Where(p => p.InstructionIndex == k))
                                if (p.SourceStartByte + p.ByteCount <= args.Length) Array.Copy(args, p.SourceStartByte, y.ArgData, p.TargetStartByte, p.ByteCount);
                            var v = Events.EmevdTranslator.Decode(defs, y);
                            if (pi.AreaType >= 0 && (v.Length <= pi.AreaType || (int)v[pi.AreaType] != 1)) continue;
                            foreach (int a in pi.Region)
                                if (a < v.Length && (int)v[a] is int r && r > 0)
                                {
                                    if (!uses.TryGetValue(r, out var u)) uses[r] = u = [];
                                    string where = $"{name} {ev.ID} {x.Bank}:{x.ID} {pi.What}";
                                    if (!u.Contains(where)) u.Add(where);
                                }
                        }
                }
            }
        }
        return placementUses = uses;
    }

    /// <summary>DS1's point marker: a Point shape, or a box with a footprint of at most 0.1 × 0.1 m (DS1 authors its start
    /// points and warp targets as Box(0.1, 1, 0.1): every such Asylum region with an entity id is a place for a character
    /// or the player, e.g. the game start 1812100, which no event names).</summary>
    public static bool IsPointMarker(MSB1.Region r) => r.Shape is MSB.Shape.Point || r.Shape is MSB.Shape.Box b && b.Width <= 0.11f && b.Depth <= 0.11f;

    /// <summary>A DS1 region is a character placement when DS1 puts a character or the player there
    /// (<see cref="PlacementUses"/>) or it has DS1's point-marker shape (<see cref="IsPointMarker"/>).</summary>
    public static bool IsPlacementRegion(MSB1.Region r, string dsrRoot) =>
        r.EntityID > 0 && (IsPointMarker(r) || PlacementUses(dsrRoot).ContainsKey(r.EntityID));

    /// <summary>P1 for regions: the EventLocation row of a DS1 placement region takes DS1 yaw + 180 when the region is a
    /// point marker. An area-shaped placement region (a box with a larger footprint, a sphere or cylinder that DS1 also
    /// warps to: the fog-traverse boxes 1812600/01 "光の壁 CP1前/後", heroine warp spheres …) keeps DS1's rotation: the row's
    /// rotation also orients its area, and whether DS2 turns a box about its position (as DS1's bottom-centred boxes, so a
    /// half turn would be harmless) is U; a translated warp to one needs a point row with θ + 180 (NOTES §1, open).</summary>
    public static bool TakesPlacementYaw(MSB1.Region r, string dsrRoot) => IsPointMarker(r) && IsPlacementRegion(r, dsrRoot);

    /// <summary>P1: the direction a DS1 character with rotation Y faces (model −Z under DS1's yaw).</summary>
    public static Vector3 Facing(float ds1Yaw) => Rotate(new Vector3(0, 0, -1), ds1Yaw);

    /// <summary>The DS2 yaw that makes a character face along <paramref name="dir"/> (compass atan2(x, z)).</summary>
    public static float YawToward(Vector3 dir) => Norm(MathF.Atan2(dir.X, dir.Z) * 180 / MathF.PI);

    /// <summary>A model-space vector under an MSB yaw (DirectX RotationY, row vectors: model +Z → (sin θ, 0, cos θ)).</summary>
    public static Vector3 Rotate(Vector3 v, float yawDeg)
    {
        float r = yawDeg * MathF.PI / 180, s = MathF.Sin(r), c = MathF.Cos(r);
        return new Vector3(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
    }

    /// <summary>P2, standing: a DS1 root at or under the DS2 floor (walkable, at most 0.3 m over the root) is raised to
    /// the floor + <see cref="StandOverFloor"/> (DS1's own physics lifts it; DS2 may drop it through: Firelink skeleton 200
    /// was 0.07 m under the slope); a root already over its floor keeps DS1's height (it may stand on an object the probe
    /// doesn't see; gravity settles it, as DS2's own generators 0.05-9 m over the floor); no floor near: DS1's height.</summary>
    public static Vector3 Stand(FloorProbe probe, Vector3 ds1Pos, out string note)
    {
        if (probe.Floor(ds1Pos, above: 0.3f, below: 0.5f) is not { } f) { note = $"no DS2 floor within -0.5..+0.3 m of DS1's root: DS1 height {ds1Pos.Y:0.###} kept"; return ds1Pos; }
        if (ds1Pos.Y - f.Y >= StandOverFloor) { note = $"DS1 root {ds1Pos.Y - f.Y:0.###} m over the DS2 floor {f.Y:0.###}: kept"; return ds1Pos; }
        var p = ds1Pos with { Y = f.Y + StandOverFloor };
        note = $"DS1 root {ds1Pos.Y - f.Y:+0.###;-0.###} m vs the DS2 floor {f.Y:0.###} ({f.Part}) → raised to {p.Y:0.###}";
        return p;
    }

    /// <summary>P2, sitting: the DS2 root that makes the idle DS2 plays (<paramref name="playedPelvis"/>: its frame-0 pelvis
    /// in DS1 model axes, x / up / +z behind; <see cref="SauldenSitPelvis"/> for the interim sit, the DS1 clip's own
    /// <paramref name="ds1Pelvis"/> when DS1's clip plays) hold the pelvis where DS1's clip holds it: root = DS1 root +
    /// R(θ)·(DS1 pelvis − played pelvis) (P1 keeps the facing, so "behind" is the same direction in both games). The
    /// played clip's feet are at its root: a root at or under the DS2 floor under it (≤ 0.3 m) rises to the floor + 0.05
    /// (the pelvis then sits that much higher than DS1's); no DS2 floor within −0.6..+0.3 m of it (Anastacia's cell has no
    /// collision) → DS1's root is kept.</summary>
    public static Vector3 Sit(FloorProbe probe, Vector3 ds1Pos, float ds1Yaw, Vector3 ds1Pelvis, Vector3 playedPelvis, out string note)
    {
        var p = ds1Pos + Rotate(ds1Pelvis - playedPelvis, ds1Yaw);
        string clip = $"DS1 pelvis {ds1Pelvis:0.00}, played {playedPelvis:0.00}";
        if (probe.Floor(p, above: 0.3f, below: 0.6f) is not { } f) { note = $"{clip}: root {p:0.##} has no DS2 floor within -0.6..+0.3 m: DS1's root {ds1Pos:0.##} kept"; return ds1Pos; }
        string lift = "";
        if (p.Y - f.Y < StandOverFloor) { lift = $", feet {p.Y - f.Y:+0.00;-0.00} m vs the DS2 floor {f.Y:0.###} ({f.Part}) → raised {f.Y + StandOverFloor - p.Y:0.00} m"; p = p with { Y = f.Y + StandOverFloor }; }
        note = $"{clip}: root {p:0.###} (DS1 {ds1Pos:0.###}, moved {Vector3.Distance(p, ds1Pos):0.##} m){lift}";
        return p;
    }

    static readonly Dictionary<(string, int), (Vector3 Pelvis, Vector3 Head)?> poses = [];
    static readonly Dictionary<string, BND3> archives = [];

    /// <summary>Model-space pelvis of a DS1 clip's first frame (see <see cref="ClipPose"/>); null if the clip is missing.</summary>
    public static Vector3? ClipSeat(string dsrRoot, string chr, int animId) => ClipPose(dsrRoot, chr, animId)?.Pelvis;

    /// <summary>P3: a DS1 idle pose sits (kneels, crouches): pelvis under <see cref="SitPelvisMax"/> over the root and the
    /// head at least <see cref="SitHeadOverPelvisMin"/> over the pelvis.</summary>
    public static bool Sits((Vector3 Pelvis, Vector3 Head) pose) => pose.Pelvis.Y < SitPelvisMax && pose.Head.Y - pose.Pelvis.Y >= SitHeadOverPelvisMin;

    /// <summary>Model-space pelvis and head of a DS1 clip's first frame (DS1's Havok skeleton `chr\CHR.anibnd.dcx`
    /// Skeleton.hkx; the clip from that archive or, for c0000, the split archives c0000_a00_hi/md/lo), cached; null if the
    /// clip is missing.</summary>
    public static (Vector3 Pelvis, Vector3 Head)? ClipPose(string dsrRoot, string chr, int animId)
    {
        if (poses.TryGetValue((chr, animId), out var cached)) return cached;
        (Vector3, Vector3)? result = null;
        string clip = Chr.Ds1ChrSource.ClipName(animId) + ".hkx";
        byte[]? bytes = null;
        foreach (string bnd in new[] { chr, $"{chr}_a00_md", $"{chr}_a00_hi", $"{chr}_a00_lo" })
        {
            string path = Path.Combine(dsrRoot, "chr", bnd + ".anibnd.dcx");
            if (!File.Exists(path)) continue;
            if (!archives.TryGetValue(path, out var bnd3)) archives[path] = bnd3 = BND3.Read(path);
            var f = bnd3.Files.FirstOrDefault(x => Path.GetFileName(x.Name.Replace('\\', '/')).Equals(clip, StringComparison.OrdinalIgnoreCase));
            if (f != null) { bytes = DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes; break; }
        }
        if (bytes != null)
        {
            var sk = Chr.Ds1ChrSource.Load(dsrRoot, chr).Skeleton;
            var s = Chr.Ds1AnimBridge.Sample(sk, new HkTagfile(bytes));
            var world = new Matrix4x4[sk.Names.Count];
            for (int b = 0; b < sk.Names.Count; b++)
            {
                float[] t = sk.RefPos[b], r = sk.RefRot[b];
                if (s.Tracks.TryGetValue(b, out var tr) && tr.Count > 0) { t = tr[0].T; r = tr[0].R; }
                var local = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(new Quaternion(r[0], r[1], r[2], r[3]))) * Matrix4x4.CreateTranslation(t[0], t[1], t[2]);
                int p = sk.Parents[b];
                world[b] = p >= 0 && p < b ? local * world[p] : local;
            }
            int pelvis = sk.Names.FindIndex(n => n.Equals("Pelvis", StringComparison.OrdinalIgnoreCase));
            int head = sk.Names.FindIndex(n => n.Equals("Head", StringComparison.OrdinalIgnoreCase));
            if (pelvis >= 0) result = (world[pelvis].Translation, head >= 0 ? world[head].Translation : world[pelvis].Translation + new Vector3(0, 0.6f, 0));
        }
        poses[(chr, animId)] = result;
        return result;
    }

    /// <summary>P4: a test spot over the floor near <paramref name="p"/>: <see cref="Spot"/> when its floor is a walkable
    /// patch, else the nearest <see cref="SafeSpot"/> within 1.5 m, else <see cref="Spot"/>; null if no floor at all.</summary>
    public static Vector3? TestSpot(FloorProbe probe, Vector3 p)
    {
        var s = Spot(probe, p);
        if (s is { } q && Patch(probe, q with { Y = q.Y - SpotOverFloor })) return q;
        return SafeSpot(probe, p, 1.5f) ?? s;
    }

    /// <summary>P4: a test-warp point 0.4 m over the DS2 floor nearest to <paramref name="p"/> (within 2 m under to 1 m
    /// over it); null if there is none.</summary>
    public static Vector3? Spot(FloorProbe probe, Vector3 p) =>
        probe.Floor(p, above: 1.0f, below: 2.0f) is { } f ? p with { Y = f.Y + SpotOverFloor } : null;

    /// <summary>Walkable patch: the floor 0.5 m out in 8 directions stays within 0.35 m of the floor at p (no edge, step
    /// or wall right next to the spot).</summary>
    public static bool Patch(FloorProbe probe, Vector3 floorPoint, float radius = 0.5f)
    {
        for (float r = 0.5f; r <= radius + 1e-3f; r += 0.5f)
            for (int k = 0; k < 8; k++)
            {
                float a = k * MathF.PI / 4;
                var q = floorPoint + new Vector3(MathF.Sin(a), 0, MathF.Cos(a)) * r;
                if (probe.Floor(q with { Y = floorPoint.Y }, above: 0.35f, below: 0.35f) == null) return false;
            }
        return true;
    }

    /// <summary>P4: a test spot <paramref name="distance"/> m in front of a DS1 character (P1 facing), on a walkable patch
    /// with no wall between it and the character (at +1 m), 0.4 m over the floor, facing the character. Tries the
    /// facing first, then ±20°, ±40° … ±100°, and 1.6/2.4 m; then the rest of the circle (a character that faces a wall
    /// under P1, the Firelink merchant in her alcove: her spot ends up behind her; whether DS2's Talk prompt works from
    /// there is U, NOTES §1).</summary>
    public static (Vector3 Pos, float Yaw)? FrontSpot(FloorProbe probe, Vector3 target, float ds1Yaw, float distance = 2f)
    {
        float face = YawToward(Facing(ds1Yaw));
        foreach (float d in new[] { distance, distance * 0.8f, distance * 1.2f, distance * 0.6f })
            foreach (float off in new[] { 0f, 20, -20, 40, -40, 60, -60, 80, -80, 100, -100, 120, -120, 140, -140, 160, -160, 180 })
            {
                float a = (face + off) * MathF.PI / 180;
                var dir = new Vector3(MathF.Sin(a), 0, MathF.Cos(a));
                var q = target + dir * d;
                if (probe.Floor(q, above: 0.8f, below: 1.5f) is not { } f) continue;
                var floorPoint = q with { Y = f.Y };
                if (!Patch(probe, floorPoint)) continue;
                var eye = target + new Vector3(0, 1, 0);
                if (probe.Ray(eye, dir, d) is float w && w < d - 0.3f) continue;
                return (floorPoint with { Y = f.Y + SpotOverFloor }, YawToward(-dir));
            }
        return null;
    }

    /// <summary>P4: the walkable patch nearest to <paramref name="near"/> (rings of 0.25 m up to <paramref name="search"/>
    /// m) with floor 1 m out all round (an open spot near an edge: Firelink's graveyard path), 0.4 m over its floor;
    /// null if none.</summary>
    public static Vector3? SafeSpot(FloorProbe probe, Vector3 near, float search = 2f)
    {
        for (float r = 0; r <= search + 1e-3f; r += 0.25f)
            for (int k = 0; k < (r == 0 ? 1 : 16); k++)
            {
                float a = k * MathF.PI / 8;
                var q = near + new Vector3(MathF.Sin(a), 0, MathF.Cos(a)) * r;
                if (probe.Floor(q, above: 1.0f, below: 2.0f) is not { } f) continue;
                if (Patch(probe, q with { Y = f.Y }, 1.0f)) return q with { Y = f.Y + SpotOverFloor };
            }
        return null;
    }
}
