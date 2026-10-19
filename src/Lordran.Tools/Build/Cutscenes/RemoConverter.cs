using System.Numerics;
using Lordran.Tools.Formats;
using Lordran.Tools.Formats.Havok;
using SoulsFormats;

namespace Lordran.Tools.Build.Cutscenes;

/// <summary>
/// DS1 cutscene (remo) → DS2 realtime demo (session 8, cutscene track; formats in REMO-FORMAT.md / SCN-FORMAT.md,
/// rules in NOTES.md). The two engines describe a cut the same way: a camera bone and actors animated by Havok
/// spline-compressed tracks at 30 fps. Conversion rules (all from DS1's own data):
///  - R1 scene space: DS1 animates every part in the scene's space = map space minus the MSB position of the map parts
///    the cut lists (180125: h0054B1 at (0, 200, 0); 100225: Firelink's pieces at the origin). DS2 places a cut's
///    tracks relative to its <c>cutNNNN_offset.sib</c> event: offset = that origin (our maps keep DS1 coordinates and
///    MapOffset 0), tracks copied unchanged.
///  - R2 camera: <c>camera_win32.sibcam</c> per frame (start frame + animation frame) → actor "camNNNN" (one bone,
///    rotation q = Ry·Rz·Rx of DS1's Euler angles: both engines' cameras look down local -Y with +Z up) + float slot
///    "camNNNN_FOV" (radians, copied).
///  - R3 actors: a DS1 part's bones are "&lt;part&gt;_&lt;bone&gt;" with the part root = the skeleton's Master; a DS2 actor's are
///    "&lt;actor&gt;_&lt;bone&gt;" with the actor root = the placement. DS1 characters ported as DS2 demo objects (the crow c3510 →
///    o18_3510, as DS2's own bird o00_6090) keep DS1's skeleton and tracks 1:1 under the DS2 actor name = the MSB part
///    the map places for the demo; the player (DS1 part c0000_0000) is DS2's demo character c000100_0000: DS1's motion
///    retargeted onto DS2's player rig (<see cref="Retarget"/>). R3b: DS1 NPC humans c0000_00xx (Firelink's Crestfallen
///    Warrior c0000_0018 in 100225) are DS2 demo characters c&lt;EnemyParam&gt;_0000 on the same rig, retargeted the same
///    way, when the scene names them (<see cref="Scene.HumanActors"/>: only once the map registers the NPC). Other parts
///    (map pieces, dummies d0000_*, unregistered NPCs, unported characters/objects) are left out and listed in the notes.
///  - R4 fades: DS1 scene TAE (id per cut = the cut number) events type 0 (fade in) / type 1 (fade out) → MQB fade
///    events (MQB_CUSTOM_FADE_ST, third word 0 in / 1 out, as scn101010's first and last cut).
///  - R5 ids: DS2 demo id = DS2 map AABB × 100 + the DS1 scene's last two digits (180125 → 180125; Firelink's 100225 →
///    190225: DS1's Firelink is our m19_02); DemoParam row = Things Betwixt's 100200 (addGroupMask 110 shows demo-only
///    objects) with memoryBlock/groupMask = the map area's (<see cref="Cutscenes.DemoArea"/>: our maps block 1 / group 2).
/// Not converted yet (U): DS1 cutscene sounds (TAE 128 type 4 "p" sounds need DS1's FMOD banks), SFX (TAE 96), the
/// other TAE state events (lighting/fog banks 176-200), subtitles.
/// </summary>
public static class RemoConverter
{
    public const float FrameDuration = 1 / 30f;

    /// <summary>TEST only (session 9 crow track, A/B "rebase" via Cutscenes.CrowTest / LR_CROW_AB; never set in a normal
    /// build): each cut's offset moves to the demo object actor (its root at the frame it is nearest the cut camera) and
    /// every actor's root track and reference pose shift back by the same amount, so world poses are unchanged but the
    /// object's origin (= the cut offset) sits in the shot, as in vanilla demos.</summary>
    public static bool TestRebaseOnObject;

    public static int DemoId(int ds1Scene, string ds2Map) => int.Parse(ds2Map[1..3] + ds2Map[4..6]) * 100 + ds1Scene % 100;

    /// <param name="ObjectActors">DS1 character model number → the DS2 demo object model that stands for it (its MSB
    /// part "&lt;model&gt;_&lt;instance&gt;" must exist in the DS2 map with draw group 110)</param>
    /// <param name="HumanActors">DS1 human NPC part (c0000_00xx) → DS2 demo character actor "c&lt;EnemyParam&gt;_0000"
    /// (R3b, <see cref="Cutscenes.HumanActors"/>; null/missing = left out)</param>
    public sealed record Scene(int Ds1Scene, string Ds1Map, string Ds2Map, IReadOnlyDictionary<int, string> ObjectActors, IReadOnlyDictionary<string, string>? HumanActors = null)
    {
        public int Demo => DemoId(Ds1Scene, Ds2Map);
    }

    public sealed class Result
    {
        public int Demo;
        public byte[] ScnBnd = [];
        public Ds2Mqb Mqb = null!;
        public List<(string Cut, byte[] Hkx, List<Ds2Scn.NewActor> Actors)> Cuts = [];
        public List<string> Notes = [];
        public Vector3 Origin;
        public Ds1Remo Remo = null!;
    }

    /// <summary>A vanilla demo animation with reflection (template for our files) and its player rig.</summary>
    public sealed class Templates
    {
        public HkPackfile Hkx = null!;
        public BND4 Bnd = null!;
        public ACB Acb = null!;
        public Ds2Scn.Actor Player = null!;
        public (Vector3 T, Quaternion R, Vector3 S)[] PlayerRef = [];
        /// <summary>Bind poses (FLVER node local transforms by name): DS2's player model c0001 ("Character" = the demo
        /// actor root), DS1's player c0000 ("Master" = the remo part root).</summary>
        public Dictionary<string, (Vector3 T, Quaternion R, string? Parent)> Ds2PlayerBind = [], Ds1PlayerBind = [];

        static Dictionary<string, (Vector3 T, Quaternion R, string? Parent)> Bind(FLVER2 f) =>
            f.Nodes.GroupBy(n => n.Name).Select(g => g.First()).ToDictionary(n => n.Name, n => (n.Translation, NodeRotation(n.Rotation), n.ParentIndex >= 0 ? f.Nodes[n.ParentIndex].Name : (string?)null));

        public static Templates Load(string ds2Data, string? dsrRoot = null)
        {
            dsrRoot ??= Program.DefaultDsrRoot;
            var c0001 = BND4.Read(Path.Combine(ds2Data, "model", "chr", "c0001.bnd"));
            var c0000 = BND3.Read(Path.Combine(dsrRoot, "chr", "c0000.chrbnd.dcx"));
            var bnd = BND4.Read(Path.Combine(ds2Data, "model", "scn", "scn101010.scnbnd"));
            var hkx = bnd.Files.First(f => f.Name.EndsWith("cut0020_a0000.hkx", StringComparison.OrdinalIgnoreCase)).Bytes;
            var pf = new HkPackfile(hkx);
            var skel = pf.Objects.Where(o => o.Class == "hkaSkeleton").Select(o => pf.ReadObject(o.Offset)).First(s => (string?)s["name"] == "c000100_0000");
            var refPose = ((List<object?>)skel["referencePose"]!).Select(x => (float[])x!).Select(f => (new Vector3(f[0], f[1], f[2]), Quaternion.Normalize(new Quaternion(f[4], f[5], f[6], f[7])), new Vector3(f[8], f[9], f[10]))).ToArray();
            return new Templates
            {
                Hkx = pf, Bnd = bnd,
                Acb = ACB.Read(bnd.Files.First(f => f.Name.EndsWith(".acb", StringComparison.OrdinalIgnoreCase)).Bytes),
                Player = Ds2Scn.ReadActors(hkx).First(a => a.Name == "c000100_0000"),
                PlayerRef = refPose,
                Ds2PlayerBind = Bind(FLVER2.Read(c0001.Files.First(f => f.Name.EndsWith("c0001.flv", StringComparison.OrdinalIgnoreCase)).Bytes)),
                Ds1PlayerBind = Bind(FLVER2.Read(c0000.Files.First(f => f.ID == 200).Bytes)),
            };
        }
    }

    public static Result Convert(string dsrRoot, Scene s, Templates tpl)
    {
        var remo = Ds1Remo.Read(Path.Combine(dsrRoot, "remo", $"scn{s.Ds1Scene}.remobnd.dcx"));
        var msb = MSB1.Read(Path.Combine(dsrRoot, "map", "MapStudio", s.Ds1Map + ".msb"));
        var mapParts = msb.Parts.MapPieces.Cast<MSB1.Part>().Concat(msb.Parts.Collisions).ToDictionary(p => p.Name, p => p);
        var r = new Result { Demo = s.Demo, Remo = remo };
        string scn = $"scn{s.Demo:D6}";
        var mqb = new Ds2Mqb { Name = scn, Framerate = 30, ResourceDirectory = @"N:\FRPG2\data\Model" };
        int eventId = 0;

        // R1: the scene's origin, one for all cuts: per map part the cut lists, its MSB position minus its bone's position
        // at frame 0; the most common value (1 cm) wins (180125: h0054B1 (0, 200, 0) − (0, 0, 0); Firelink's pieces sit in
        // the scene at their own MSB positions, so all give (0, 0, 0); a few pieces with rotated pivots disagree by cm)
        r.Origin = SceneOrigin(remo, mapParts, out int agree, out int total);
        if (agree < total) r.Notes.Add($"{scn}: scene origin {r.Origin} from {agree} of {total} map parts (the others are off by their pivots)");
        foreach (var cut in remo.Cuts)
        {
            string num = cut.Name[3..]; // "0005"
            // cut length: the animation's, or the camera's range when the parts hold still (DS1 stores a static cut's
            // parts as a 2-frame animation: 140100, 150104 … in remo-dump --survey; sampling clamps to its last frame)
            int frames = Math.Max(cut.Animation.NumFrames, cut.Camera is { } cc ? cc.EndFrame - cc.StartFrame + 1 : 0);
            var actors = new List<Ds2Scn.NewActor>();
            if (cut.Camera != null) actors.Add(Camera($"cam{num}", cut, frames));
            foreach (var part in cut.Parts)
            {
                if (mapParts.ContainsKey(part.Name)) continue;
                string pn = part.Name;
                if (pn == "c0000_0000") { actors.Add(Retarget(cut, part, tpl, frames, r.Notes)); continue; }
                if (s.HumanActors?.TryGetValue(pn, out var human) == true) { actors.Add(Retarget(cut, part, tpl, frames, r.Notes, human)); continue; } // R3b
                if (pn.Length == 10 && pn[0] == 'c' && int.TryParse(pn[1..5], out int model) && s.ObjectActors.TryGetValue(model, out var obj))
                {
                    actors.Add(Rename(cut, part, $"{obj}_{pn[6..]}", frames));
                    continue;
                }
                r.Notes.Add($"{scn} {cut.Name}: DS1 part {pn} ({part.Bones.Count} bones) left out (no DS2 actor for it yet)");
            }
            var cutOrigin = r.Origin;
            if (TestRebaseOnObject && actors.FirstOrDefault(a => a.Name.StartsWith('o')) is { } objActor)
            {
                var camActor = actors.FirstOrDefault(a => a.Name.StartsWith("cam"));
                var objRoot = objActor.Tracks[0];
                int near = camActor == null ? objRoot.Length / 2
                    : Enumerable.Range(0, Math.Min(objRoot.Length, camActor.Tracks[0].Length)).MinBy(k => Vector3.Distance(objRoot[k].T, camActor.Tracks[0][k].T));
                var at = objRoot[near].T;
                var delta = new Vector3(MathF.Round(at.X, 2), MathF.Round(at.Y, 2), MathF.Round(at.Z, 2));
                foreach (var a in actors)
                    for (int b = 0; b < a.Bones.Length; b++)
                        if (a.Parents[b] < 0)
                        {
                            for (int k = 0; k < a.Tracks[b].Length; k++) a.Tracks[b][k].T -= delta;
                            a.ReferencePose[b].T -= delta;
                        }
                cutOrigin = r.Origin + delta;
                r.Notes.Add($"{scn} {cut.Name}: TEST rebase: cut offset {cutOrigin} (scene origin + {objActor.Name}'s root at frame {near}, nearest the camera); every actor root shifted by {-delta}");
            }
            byte[] hkx = Ds2Scn.WriteActors(actors, FrameDuration, tpl.Hkx);
            r.Cuts.Add((cut.Name, hkx, actors));

            // ---- MQB: resources, then the cut's timelines (as vanilla: offset, HKT, actors, fades)
            var c = new Ds2Mqb.Cut { Name = cut.Name, Duration = frames - 1 };
            Ds2Mqb.Transform Identity(float f = 0) => new() { Frame = f };
            int Res(Ds2Mqb.Resource res) { mqb.Resources.Add(res); return mqb.Resources.Count - 1; }
            void Timeline(int parentTimeline, int res, int start, int dur, bool full, Ds2Mqb.Parameter[] pars, params Ds2Mqb.Transform[] xf)
            {
                var ev = new Ds2Mqb.Event
                {
                    Id = eventId++, ResourceIndex = res, Unk08 = c.Timelines.Count, StartFrame = start, Duration = dur,
                    Unk18 = full ? frames : 100, Unk1C = full ? frames : 100, Unk28 = parentTimeline,
                };
                ev.Parameters.AddRange(pars);
                ev.Transforms.AddRange(xf.Length > 0 ? xf : [Identity()]);
                c.Events.Add(ev);
                c.Timelines.Add(new Ds2Mqb.Timeline { EventIndices = [c.Events.Count - 1], Unk10 = parentTimeline });
            }
            static Ds2Mqb.Parameter Disposition() => Ds2Mqb.Parameter.Custom("MQB_CUSTOM_DISPOSITION_HAVOK_AN", new byte[4]);
            int hkt = Res(new Ds2Mqb.Resource { Name = $"cut{num}_a0000.HKT", Unk48 = 266, Path = $@".\scn\{scn}\cut{num}\motion_hkt\cut{num}_a0000.HKT" });
            var actorRes = actors.Select(a => (a, Res(new Ds2Mqb.Resource { Name = a.Name, ParentIndex = hkt, Unk48 = a.Name.StartsWith("cam") ? 3 : 1 }))).ToList();
            int sib = Res(new Ds2Mqb.Resource { Name = $"cut{num}_offset.sib", Unk48 = 265 });
            Timeline(-1, sib, 0, frames, true, [], new Ds2Mqb.Transform { Frame = 0, Translation = cutOrigin }, new Ds2Mqb.Transform { Frame = frames - 1, Translation = cutOrigin });
            Timeline(0, hkt, 0, frames, true, [Disposition()]);
            foreach (var (a, res) in actorRes)
                Timeline(1, res, 0, frames, true, a.Name.StartsWith("cam") ? [] : [Disposition()]);
            // R4: fades from the scene TAE
            if (remo.Tae?.Animations.FirstOrDefault(x => x.ID == int.Parse(num)) is { } tae)
                foreach (var e in tae.Events.Where(e => e.Type is 0 or 1).OrderBy(e => e.StartTime))
                {
                    int start = (int)MathF.Round(e.StartTime * 30), dur = Math.Max(1, (int)MathF.Round((e.EndTime - e.StartTime) * 30));
                    if (start >= frames) continue;
                    dur = Math.Min(dur, frames - start);
                    var payload = new byte[12];
                    payload[8] = (byte)(e.Type == 1 ? 1 : 0);
                    int fade = Res(new Ds2Mqb.Resource { Name = $"Custom5_{mqb.Resources.Count}", Unk48 = 12, Parameters = [Ds2Mqb.Parameter.Custom("MQB_CUSTOM_FADE_ST", payload)] });
                    Timeline(-1, fade, start, dur, false, []);
                    r.Notes.Add($"{scn} {cut.Name}: fade {(e.Type == 1 ? "out" : "in")} frames {start}-{start + dur} (DS1 TAE {e.StartTime:0.###}-{e.EndTime:0.###} s)");
                }
            mqb.Cuts.Add(c);
        }
        r.Mqb = mqb;

        // ---- the scnbnd: ACB (asset paths of the HKT files, offsets, mqx), the cut animations, the MQB (vanilla layout)
        var acb = tpl.Acb;
        var assets = new List<ACB.Asset>();
        string root = $@"N:\FRPG2_64\data\model\scn\{scn}";
        foreach (var cut in r.Cuts)
        {
            string num = cut.Cut[3..];
            assets.Add(new ACB.Asset.Motion { AbsolutePath = $@"{root}\cut{num}\motion_hkt\cut{num}_a0000.HKT", RelativePath = $"cut{num}/motion_hkt/cut{num}_a0000.HKT" });
            assets.Add(new ACB.Asset.General { AbsolutePath = $@"{root}\cut{num}\cut{num}_offset.sib", RelativePath = $"cut{num}/cut{num}_offset.sib" });
        }
        assets.Add(new ACB.Asset.General { AbsolutePath = $@"{root}\mqx\{scn}.mqx", RelativePath = $"mqx/{scn}.mqx" });
        var newAcb = new ACB { BigEndian = acb.BigEndian, Assets = assets };
        var bnd = new BND4
        {
            Version = tpl.Bnd.Version, Format = tpl.Bnd.Format, Unicode = tpl.Bnd.Unicode, Extended = tpl.Bnd.Extended,
            BigEndian = tpl.Bnd.BigEndian, BitBigEndian = tpl.Bnd.BitBigEndian, Unk04 = tpl.Bnd.Unk04, Unk05 = tpl.Bnd.Unk05,
        };
        var flags = tpl.Bnd.Files[0].Flags;
        bnd.Files.Add(new BinderFile(flags, 0, $"{scn}.acb", newAcb.Write()));
        foreach (var cut in r.Cuts) bnd.Files.Add(new BinderFile(flags, bnd.Files.Count, $"{cut.Cut}_a0000.hkx", cut.Hkx));
        bnd.Files.Add(new BinderFile(flags, bnd.Files.Count, $"{scn}.mqb", mqb.Write()));
        r.ScnBnd = bnd.Write();
        return r;
    }

    /// <summary>R1 (see <see cref="Convert"/>): the most common "MSB position − bone position at frame 0" of the scene's map
    /// parts, rounded to 1 cm; (0, 0, 0) when the scene lists none.</summary>
    public static Vector3 SceneOrigin(Ds1Remo remo, IReadOnlyDictionary<string, MSB1.Part> mapParts, out int agree, out int total)
    {
        var votes = remo.Cuts.SelectMany(c => c.Parts.Where(p => mapParts.ContainsKey(p.Name)).Select(p => mapParts[p.Name].Position - c.BoneInRoot(p.Bones[0], 0).T))
            .Select(v => new Vector3(MathF.Round(v.X, 2), MathF.Round(v.Y, 2), MathF.Round(v.Z, 2))).ToList();
        total = votes.Count;
        if (total == 0) { agree = 0; return Vector3.Zero; }
        var best = votes.GroupBy(v => v).OrderByDescending(g => g.Count()).First();
        agree = best.Count();
        return best.Key;
    }

    // ------------------------------------------------------------------ actors

    /// <summary>R2: the sibcam as a DS2 camera actor.</summary>
    static Ds2Scn.NewActor Camera(string name, Ds1Remo.Cut cut, int frames)
    {
        var cam = cut.Camera!;
        var track = new (Vector3 T, Quaternion R, Vector3 S)[frames];
        var fov = new float[frames];
        for (int k = 0; k < frames; k++)
        {
            var (pos, rot) = cam.At(cam.StartFrame + k);
            track[k] = (pos, CameraRotation(rot), Vector3.One);
            fov[k] = cam.FovAt(cam.StartFrame + k);
        }
        return new Ds2Scn.NewActor
        {
            Name = name, Bones = [name], Parents = [-1], ReferencePose = [track[0]], Tracks = [track],
            FloatSlots = [name + "_FOV"], Floats = [fov],
        };
    }

    /// <summary>DS1 sibcam Euler angles (radians) → the camera's orientation: M = Ry(y)·Rz(z)·Rx(x) (REMO-FORMAT.md: the
    /// order that makes the DS1 camera's -Y axis look at the subject; DS2 demo cameras use the same local axes).</summary>
    public static Quaternion CameraRotation(Vector3 e) =>
        Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY, e.Y) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, e.Z) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, e.X));

    /// <summary>R3: a DS1 part as a DS2 actor with DS1's skeleton and tracks 1:1 (bones renamed part → actor).</summary>
    static Ds2Scn.NewActor Rename(Ds1Remo.Cut cut, Ds1Remo.Part part, string actor, int frames)
    {
        var idx = part.Bones;
        var map = idx.Select((b, i) => (b, i)).ToDictionary(x => x.b, x => x.i);
        return new Ds2Scn.NewActor
        {
            Name = actor,
            Bones = idx.Select(b => actor + cut.BoneNames[b][part.Name.Length..]).ToArray(),
            Parents = idx.Select(b => cut.Parents[b] < 0 ? -1 : map[cut.Parents[b]]).ToArray(),
            ReferencePose = idx.Select(b => cut.ReferencePose[b]).ToArray(),
            Tracks = idx.Select(b => Enumerable.Range(0, frames).Select(f => cut.Animation.Sample(cut.TrackOf(b), f)).ToArray()).ToArray(),
        };
    }

    /// <summary>DS1 player bone → DS2 player bone (same names, except DS1's Lower_Root = DS2's Root: both carry the pelvis).</summary>
    static string Ds2Bone(string ds1) => ds1 == "Lower_Root" ? "Root" : ds1;

    /// <summary>
    /// R3 player: DS1's c0000 motion on DS2's player rig (c000100_0000: actor root → Master (a leaf: root-motion
    /// locator) and actor root → Root → Pelvis → body). Per frame, for every DS2 bone with a DS1 namesake, the DS1 bone's
    /// rotation relative to its bind pose (both in the part/actor root's frame) is applied to the DS2 bone's bind
    /// orientation; the pelvis keeps DS1's position (scaled by the pelvis heights), the other bones DS2's bone lengths;
    /// bones DS1 lacks (knees, elbows, fingers 3/4, nubs, Footsteps) keep DS2's bind pose. The actor root = DS1's part
    /// root (Master) in scene space; if the rigs face different ways, DS1's frame is turned onto DS2's first.
    /// R3b: a DS1 NPC human (same DS1 c0000 rig) goes onto the same DS2 c0001 rig under its demo character name
    /// <paramref name="actor"/> (bones "&lt;actor&gt;_&lt;bone&gt;", as c000100_0000's).
    /// </summary>
    static Ds2Scn.NewActor Retarget(Ds1Remo.Cut cut, Ds1Remo.Part part, Templates tpl, int frames, List<string> notes, string actor = PlayerActor)
    {
        var p2 = tpl.Player;
        int n2 = p2.Bones.Length;
        string who = actor == PlayerActor ? "player" : $"{actor} (DS1 {part.Name})";
        string Strip2(string b) => b.Length > 13 ? b[13..] : "";         // "c000100_0000_Pelvis" → "Pelvis"; root → ""
        string Strip1(string b) => b.Length > part.Name.Length ? b[(part.Name.Length + 1)..] : "";
        var ds1Index = part.Bones.Skip(1).ToDictionary(b => Strip1(cut.BoneNames[b]), b => b);
        int root1 = part.Bones[0];
        // bind poses relative to each rig's root, from the models (FLVER nodes): DS1 c0000 (root Master = the part root),
        // DS2 c0001 (root Character = the actor root); DS2 skeleton bones the model lacks (Footsteps, the *Nub ends) take
        // the vanilla demo skeleton's reference pose
        var bind1 = new Dictionary<int, (Vector3 T, Quaternion R)> { [root1] = (Vector3.Zero, Quaternion.Identity) };
        foreach (int b in part.Bones.Skip(1))
        {
            var l = tpl.Ds1PlayerBind.TryGetValue(Strip1(cut.BoneNames[b]), out var nb) ? (nb.T, nb.R) : (cut.ReferencePose[b].T, cut.ReferencePose[b].R);
            bind1[b] = Compose(bind1[cut.Parents[b]], l);
        }
        var bindLocal2 = new (Vector3 T, Quaternion R)[n2];
        var bind2 = new (Vector3 T, Quaternion R)[n2];
        bind2[0] = (Vector3.Zero, Quaternion.Identity);
        for (int m = 1; m < n2; m++)
        {
            bindLocal2[m] = tpl.Ds2PlayerBind.TryGetValue(Strip2(p2.Bones[m]), out var nb) ? (nb.T, nb.R) : (tpl.PlayerRef[m].T, tpl.PlayerRef[m].R);
            bind2[m] = Compose(bind2[p2.Parents[m]], bindLocal2[m]);
        }
        var map = new int[n2];
        for (int m = 0; m < n2; m++) map[m] = m >= 2 && ds1Index.TryGetValue(Strip2(p2.Bones[m]) == "Root" ? "Lower_Root" : Strip2(p2.Bones[m]), out int b) ? b : -1;
        int I2(string name) => Array.FindIndex(p2.Bones, x => Strip2(x) == name);
        // facing: the yaw that turns DS1's left vector (L_Thigh - R_Thigh) onto DS2's
        Vector3 Left1 = bind1[ds1Index["L_Thigh"]].T - bind1[ds1Index["R_Thigh"]].T, Left2 = bind2[I2("L_Thigh")].T - bind2[I2("R_Thigh")].T;
        float yaw = MathF.Atan2(Left1.X, Left1.Z) - MathF.Atan2(Left2.X, Left2.Z);
        var face = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yaw); // DS1 frame → DS2 frame
        if (MathF.Abs(yaw) > 0.05f) notes.Add($"{who} retarget: DS1 rig turned by {-yaw * 180 / MathF.PI:0.#}° onto DS2's");
        // leg length ratio (DS1's bind has its Master at the pelvis, DS2's Character at the feet: compare hip-to-foot)
        float scale = MathF.Abs(bind2[I2("Pelvis")].T.Y - bind2[I2("L_Foot")].T.Y) / MathF.Max(0.01f, MathF.Abs(bind1[ds1Index["Pelvis"]].T.Y - bind1[ds1Index["L_Foot"]].T.Y));
        // the binds differ in pose (DS1 T-pose arms, DS2 A-pose): per mapped bone the swing that turns DS2's bind bone
        // direction (to its main mapped child) onto DS1's; bones without a direction take their parent's
        var swing = new Quaternion[n2];
        for (int m = 0; m < n2; m++)
        {
            swing[m] = m >= 2 ? swing[p2.Parents[m]] : Quaternion.Identity;
            if (map[m] < 0) continue;
            var kids = Enumerable.Range(0, n2).Where(c => p2.Parents[c] == m && map[c] >= 0).ToList();
            int child = kids.OrderBy(c => ChildRank(Strip2(p2.Bones[c]))).FirstOrDefault(-1);
            if (child < 0) continue;
            var d1 = Vector3.Transform(bind1[map[child]].T - bind1[map[m]].T, face);
            var d2 = bind2[child].T - bind2[m].T;
            if (d1.Length() < 1e-4f || d2.Length() < 1e-4f) continue;
            swing[m] = Arc(Vector3.Normalize(d1), Vector3.Normalize(d2));
        }
        notes.Add($"{who} retarget: {map.Count(x => x >= 0)} of {n2} DS2 bones driven by DS1 bones (pelvis scale {scale:0.###}); DS1 bones without a DS2 namesake: {string.Join(", ", ds1Index.Keys.Where(k => k != "Lower_Root" && I2(k) < 0))}");
        if (Environment.GetEnvironmentVariable("LR_RETARGET_DEBUG") == "1")
            foreach (var j in new[] { "Lower_Root", "Pelvis", "Spine1", "L_Clavicle", "L_UpperArm", "L_Forearm", "L_Hand", "Head", "L_Foot" })
            {
                var a = ds1Index.TryGetValue(j, out int jb) ? bind1[jb] : default;
                int jm = I2(Ds2Bone(j));
                var bb = jm >= 0 ? bind2[jm] : default;
                var fr = face * a.R * Quaternion.Inverse(face);
                float ang = 2 * MathF.Acos(Math.Min(1, MathF.Abs(Quaternion.Dot(fr, bb.R)))) * 180 / MathF.PI;
                notes.Add($"  bind {j}: DS1 {Vector3.Transform(a.T, face):0.###} | DS2 {bb.T:0.###}; world rotation difference {ang:0.#}°");
            }

        var tracks = new (Vector3 T, Quaternion R, Vector3 S)[n2][];
        for (int m = 0; m < n2; m++) tracks[m] = new (Vector3, Quaternion, Vector3)[frames];
        var w1 = new Dictionary<int, (Vector3 T, Quaternion R)>();
        var w2 = new (Vector3 T, Quaternion R)[n2];
        for (int f = 0; f < frames; f++)
        {
            w1.Clear();
            w1[root1] = (Vector3.Zero, Quaternion.Identity);
            foreach (int b in part.Bones.Skip(1)) { var l = cut.Animation.Sample(cut.TrackOf(b), f); w1[b] = Compose(w1[cut.Parents[b]], (l.T, l.R)); }
            var rootLocal = cut.Animation.Sample(cut.TrackOf(root1), f);
            tracks[0][f] = (rootLocal.T, Quaternion.Normalize(rootLocal.R * Quaternion.Inverse(face)), Vector3.One);
            tracks[1][f] = (Vector3.Zero, Quaternion.Identity, Vector3.One);          // Master: a leaf locator, at the root
            w2[0] = (Vector3.Zero, Quaternion.Identity);
            w2[1] = w2[0];
            for (int m = 2; m < n2; m++)
            {
                var parent = w2[p2.Parents[m]];
                var bindLocal = bindLocal2[m];
                (Vector3 T, Quaternion R) world;
                if (map[m] >= 0)
                {
                    int b = map[m];
                    var r1 = face * w1[b].R * Quaternion.Inverse(face);
                    var b1 = face * bind1[b].R * Quaternion.Inverse(face);
                    var rot = Quaternion.Normalize(r1 * Quaternion.Inverse(b1) * Quaternion.Inverse(swing[m]) * bind2[m].R);
                    var pos = Strip2(p2.Bones[m]) == "Root"
                        ? Vector3.Transform(w1[b].T, face) * scale
                        : parent.T + Vector3.Transform(bindLocal.T, parent.R);
                    world = (pos, rot);
                }
                else world = Compose(parent, bindLocal);
                w2[m] = world;
                var inv = Quaternion.Inverse(parent.R);
                tracks[m][f] = (Vector3.Transform(world.T - parent.T, inv), Quaternion.Normalize(inv * world.R), Vector3.One);
            }
        }
        var refPose = new (Vector3 T, Quaternion R, Vector3 S)[n2];
        for (int m = 0; m < n2; m++) refPose[m] = m < 2 ? (Vector3.Zero, Quaternion.Identity, Vector3.One) : (bindLocal2[m].T, bindLocal2[m].R, Vector3.One);
        refPose[0] = tracks[0][0];
        var bones = p2.Bones.Select(b => actor + b[PlayerActor.Length..]).ToArray(); // "c000100_0000_Pelvis" → "<actor>_Pelvis"
        return new Ds2Scn.NewActor { Name = actor, Bones = bones, Parents = p2.Parents, ReferencePose = refPose, Tracks = tracks };
    }

    /// <summary>DS2's demo player character (the template skeleton scn101010 cut0020's; 82 bones = c0001's).</summary>
    public const string PlayerActor = "c000100_0000";

    /// <summary>Which child continues a bone's chain (its direction): the spine's next segment, the middle finger.</summary>
    static int ChildRank(string child) => child switch
    {
        "Spine1" or "Neck" or "Head" or "Pelvis" => 0,
        "L_Finger2" or "R_Finger2" => 0,
        _ when child.Contains("Twist") || child.Contains("Weapon") || child.Contains("Clavicle") || child.Contains("Thigh") => 2,
        _ => 1,
    };

    /// <summary>The shortest rotation turning unit vector a onto unit vector b.</summary>
    static Quaternion Arc(Vector3 a, Vector3 b)
    {
        float d = Vector3.Dot(a, b);
        if (d > 0.999999f) return Quaternion.Identity;
        if (d < -0.999999f)
        {
            var axis = Vector3.Cross(Vector3.UnitX, a);
            if (axis.LengthSquared() < 1e-6f) axis = Vector3.Cross(Vector3.UnitY, a);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        var c = Vector3.Cross(a, b);
        return Quaternion.Normalize(new Quaternion(c, 1 + d));
    }

    /// <summary>FLVER node rotation (Euler radians) → quaternion: SoulsFormats' ComputeLocalTransform applies X, then
    /// Z, then Y (row vectors), i.e. Ry·Rz·Rx — the same order as DS1's sibcam angles.</summary>
    public static Quaternion NodeRotation(Vector3 e) => CameraRotation(e);

    static (Vector3 T, Quaternion R) Compose((Vector3 T, Quaternion R) parent, (Vector3 T, Quaternion R) local) =>
        (parent.T + Vector3.Transform(local.T, parent.R), Quaternion.Normalize(parent.R * local.R));
}
