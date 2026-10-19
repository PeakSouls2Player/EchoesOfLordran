using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using Lordran.Tools.Formats;
using Lordran.Tools.Formats.Morpheme;
using SoulsFormats;
using static Lordran.Tools.Formats.Morpheme.MorphemeMath;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// DS1's own idle clips on our c0001-based NPCs (session 9, track npcidle; rules I1-I8 in data\sandbox\npcidle\NOTES.md).
/// <para><b>How DS2 drives an NPC idle</b> (evidence): GeneratorRegistParam.startUpStateType n → the NPC AI (ai741000,
/// group 2147483645: f5902() 0..20 → 2147483644(n)) → AI command 5204(n, 0) (decomp case 0x1454: calls the chr's vtable
/// +0x80 with n) → the c0001 network's control parameter <c>StartUpCategory</c> + request 48 <c>StartUp</c>: SM 210
/// (StateMachine_Main) → BT_StartUp (node 8287) → SM 8003 picks its state by StartUpCategory (13 → SM 8171 Saulden,
/// default leaf node 8175 = clip a77_03_0010 NPCIdleSaulden; 20 → SM 8245 → leaf 8249 a77_04_0210 NPCIdleMaughlin03; 14
/// Maughlin, 15/16 Rosabeth, 17 Magerold, 18 Cromwell, 19 Maughlin02, 12 WideUse, 11 phantom, 1-6 the player's wake-ups).
/// Saulden's idle clip holds his pelvis 0.58 m up and 0.44 m behind the root (he sits on a ledge): on Oscar's DS1
/// position that floats him; DS1's own clip carries DS1's offsets.</para>
/// <para><b>Route</b> (I1): a DS1-NPC network <c>c8001</c> = c0001's anibnd (network + every clip, byte-for-byte) with
/// the startup slots' idle clips replaced by DS1 c0000 clips retargeted onto c0001's rig (<see cref="Retarget"/>: the
/// cutscene track's R3 method in Morpheme space) and encoded by our NSA encoder, and its network def under its own Morpheme
/// asset id (<see cref="NetworkIdentity"/>, I1b: DS2 shares NMB objects by id); the NPC's EnemyParam.animId 800100 picks
/// it (decomp FUN_140359760: per EnemyParam the resource set loads model = modelId (1: the player body, unchanged),
/// anibnd = animId / 100, TAE = animId; FUN_140166a00 "chr:/morpheme4/chr/c%04d.anibnd.dcx", FUN_140166d70
/// "chr:/TimeAct/chr/c%06d_pl.tae"). The player (anibnd 1, c0001.anibnd) and every vanilla NPC (animId 100) are untouched.
/// Cost: one extra c0001-sized network per map that places such an NPC (shared by all of them), 10.8 MB in the pack.</para>
/// <para><b>Superseded by I1c</b> (<see cref="Route"/>, session 9 pass 2): animId ≠ 100 makes DS2 spawn the NPC as a
/// generic (non-human) chr without the StartUp component, so the slot never plays; the default now writes the same
/// network edits into the human network c0001.anibnd (+ c000100 TAEs) and leaves the NPC on animId 100.</para>
/// </summary>
public static partial class NpcIdle   // partial: the Template route (rule I1d) is in NpcIdleTemplate.cs
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>build-pack --no-npc-idle turns it off (NPCs keep animId 100 and DS2's stand-in startup state).</summary>
    public static bool Enabled = true;

    /// <summary>
    /// I1c (session 9 pass 2, track npcpose; replaces I1's route): <b>DS2 runs a model-1 NPC as a human only when its
    /// EnemyParam.animId is exactly 100.</b> The generator spawn FUN_1403560a0 sets <c>human = (EnemyParam +4 == 100)</c>
    /// (decomp line 662506): human → chr class FUN_14037ebe0 ("Npc_c%06d", 0x4A0) whose network interface FUN_140384060
    /// (vtable 0x1410e52a8, init FUN_140384110) creates the StartUp component FUN_1401ff0b0 (binds CPs IsStartUp,
    /// StartUpCategory, NPCActionCategory and requests StartUp, NPCAction by name: FUN_1401ff340/430) and the human move,
    /// attack, guard and IK components; any other animId → the generic class FUN_1403114f0 ("NPC_c%d" for categoryType 4,
    /// 0x480, still dressed by NPCGenerateParam FUN_14035a650) whose interface FUN_14039fee0 (vtable 0x1410e6780, init
    /// FUN_14039ff90) creates the enemy set (FUN_1403a61e0 "SM_BasicMove|…", FUN_1403a4750 Attack/Combination,
    /// FUN_1403a5a80 TwoBoneFootIk …) and <b>no StartUp component</b>. So the I1 route (animId 800100 → c8001) made
    /// Oscar a generic NPC: StartUpCategory 13 / request 48 were never sent, c8001 stayed in its default BasicMove state
    /// (SM 210 → 6103 → SM 288 → SM_Idle 672, whose hub 673 leads to a00_00_0000/0010 Idle, a00_00_0020/0030
    /// BattleStandby or a00_19_0000/0001 StaggerIdle; best render match of the s9-12/s9-17 crouch a00_19_0000, which one
    /// plays is unconfirmed: the probe pack tests the family), and slot 13 (our DS1 clip, I6b, I9) never ran.
    /// <see cref="IdleRoute.Human"/> (default): the DS1 idles go into the human network itself (the pack's
    /// <c>c0001.anibnd.dcx</c>, c0001's own ids, replacing the boot file) and the NPC keeps animId 100;
    /// <see cref="IdleRoute.Own"/> = the old c8001 route (A/B only). Collateral: <see cref="VanillaOnSlots"/>.
    /// </summary>
    public enum IdleRoute { Template, Human, Own }
    /// <summary>Session 9 track npcpose2: <see cref="IdleRoute.Template"/> (rule I1d, <see cref="NpcNet"/>) is the default;
    /// <see cref="IdleRoute.Human"/> (I1c, ships the global c0001.anibnd: build-pack --npc-idle-human) and
    /// <see cref="IdleRoute.Own"/> (c8001 / probe: --npc-idle-own-network, --npc-idle-probe) stay as A/Bs only.</summary>
    public static IdleRoute Route = IdleRoute.Template;

    /// <summary>I1c collateral (pass 3 survey: the 28 vanilla GeneratorRegistParam files × EnemyParam modelId 1, and every
    /// vanilla ESD's 5204 constant; S\work\p2\vanilla-human-regist.txt): the vanilla human NPCs on a DS1 slot's
    /// StartUpCategory, who play the DS1 clip once c0001.anibnd is replaced. Vanilla DS2 content changes: a user decision
    /// (accept as a listed stand-in, or new categories, see NOTES); <c>--no-npc-idle</c> ships no c0001 (no collateral).</summary>
    public static readonly (int Category, string Who)[] VanillaOnSlots =
        [(13, "Crestfallen Saulden (Majula m10_04, regist 74100000)"), (20, "Maughlin the Armourer, 4th state (Majula m10_04, regist 76100003)")];

    /// <summary>A/B probe (build-pack --npc-idle-probe CLIP, own route only): c8001's BasicMove idle clips
    /// (<see cref="BasicMoveIdles"/>) all play this vanilla c0001 clip, e.g. a00_16_0750 DmgDownFaceUpLoop (lying on the
    /// back): if the NPC lies flat in game, the generic class left the network in BasicMove (rule I1c).</summary>
    public static string? ProbeIdle;
    static readonly string[] BasicMoveIdles = ["a00_00_0000", "a00_00_0010", "a00_00_0020", "a00_00_0030", "a00_19_0000", "a00_19_0001", "a00_10_0010"];   // + IdleTurnCenter0 (SM 779, idle turning: pass 3)

    /// <summary>I2: the own route's anibnd id. Free in DS2 (vanilla anibnds 1, 1000-7850; EnemyParam models 8050,
    /// 9000-9200; DLC device ranges 6500-6999 / 8600-8999 / TAE 650000-699999, 860000-899999 avoided); its sound-bank id
    /// (anibnd / 10 · 10 = 8000, FUN_140350c10 → FUN_140166c20) has no file, as for vanilla anibnds 2250, 3040, 7005 …</summary>
    public const int Anibnd = 8001;
    /// <summary>EnemyParam.animId of a DS1-idle NPC: 100 (human, I1c) or 800100 (own route).</summary>
    public static int AnimId => Route == IdleRoute.Human ? 100 : Anibnd * 100;
    public static string AnibndName => Route == IdleRoute.Human ? "c0001" : $"c{Anibnd:D4}";

    /// <summary>I1c: the DS1 slot on a StartUpCategory in the human network (one network for every human NPC, so any NPC
    /// on that category plays the slot's DS1 clip); null in the own route or when off.</summary>
    public static Slot? ForCategory(int category) => Enabled && Route == IdleRoute.Human ? Slots.FirstOrDefault(s => s.Category == category) : null;

    /// <summary>
    /// I3: DS1 InitAnimID → a startup slot of the NPC network (its StartUpCategory = the regist row's startUpStateType)
    /// whose idle leaf plays the DS1 clip. Only slots Saulden's AI (ai741000/741010, kept for DS1 NPCs) treats as
    /// "sitting" (f5900(0) 13 and 20 → group 2147483632; 14/12/18 → 2147483629 turns towards the target with 5405; 17 →
    /// 2147483631), so a sitting DS1 NPC never turns on the spot.
    /// </summary>
    public sealed record Slot(int Ds1Anim, int Category, string Clip, string Who);

    public static readonly Slot[] Slots =
    [
        new(7805, 13, "a77_03_0010", "Oscar (Asylum, slumped against the wall)"),
        new(7810, 20, "a77_04_0210", "Crestfallen Warrior (Firelink, sitting on the steps)"),
    ];

    /// <summary>The I1c / I1 slot of a DS1 InitAnimID (Human / Own routes only; the default Template route uses <see cref="NetFor"/>).</summary>
    public static Slot? For(int ds1InitAnim) => Enabled && Route != IdleRoute.Template ? Slots.FirstOrDefault(s => s.Ds1Anim == ds1InitAnim) : null;

    // ------------------------------------------------------------------ pack integration

    static readonly ConditionalWeakTable<Pack, Result> built = new();

    /// <summary>Writes the NPC network and its TimeAct files into the pack once (every slot of <see cref="Slots"/>),
    /// returns the build result (report lines).</summary>
    public static Result Ensure(Pack pack, string dsrRoot)
    {
        if (built.TryGetValue(pack, out var r)) return r;
        r = Build(dsrRoot, pack.Ds2Data);
        // chrint stage-2 fix pass (rule I1b pack-wide): the 1:1 anibnds of this build never draw c8001's network-def id
        // (and a 1:1 drawn earlier with it makes this a build error, not a silent share)
        Formats.Morpheme.NmbIdentity.For(pack.Ds2Data).Claim(r.NetworkAssetId, $"net/{AnibndName}", AnibndName);
        foreach (var (path, bytes) in r.Files) pack.AddFile("gamedata", path, bytes);
        foreach (var line in r.Report) pack.Note($"npc idle: {line}");
        built.Add(pack, r);
        Console.WriteLine($"  npc idle network {AnibndName}: {r.Report.Count(l => l.StartsWith("slot"))} DS1 idle slots");
        return r;
    }

    public sealed class Result
    {
        public Dictionary<string, byte[]> Files = [];
        public List<string> Report = [];
        public List<SlotResult> Slots = [];
        public byte[] Nmb = [];
        public uint NetworkAssetId;
        /// <summary>NSA entries the A/B probe replaced (own route, <see cref="ProbeIdle"/>).</summary>
        public List<string> ProbeNsas = [];
    }

    public sealed class SlotResult
    {
        public Slot Slot = null!;
        public string NsaName = "";
        public int ClipIndex;
        public byte[] Nsa = [];
        public NsaAnim Ds2 = null!;              // decoded back from our NSA
        public NsaAnim Ds2Original = null!;      // the DS2 stand-in clip the slot played
        public Ds1AnimBridge.Result Ds1 = null!;
        public double Ds1Duration, Ds2Duration;
        public int Ds1Frames;
        public List<int> Nodes = [];
        public List<int> TimeAct = [];
        public Check Check = null!;
    }

    // ------------------------------------------------------------------ DS1 side

    /// <summary>DS1's player-body clips: c0000.anibnd holds Skeleton.hkx and the TAEs (a00.tae for a00_xxxx), the clips
    /// sit in c0000_a00_md/hi/lo and c0000_aNx anibnds (a00_7805 / a00_7810 in c0000_a00_md).</summary>
    public static Ds1ChrSource LoadC0000(string dsrRoot, IEnumerable<int> ids)
    {
        string dir = Path.Combine(dsrRoot, "chr");
        var main = BND3.Read(Path.Combine(dir, "c0000.anibnd.dcx"));
        var merged = new BND3();
        foreach (var f in main.Files)
        {
            string n = Path.GetFileName(f.Name.Replace('\\', '/'));
            if (n.Equals("Skeleton.hkx", StringComparison.OrdinalIgnoreCase) || n.Equals("a00.tae", StringComparison.OrdinalIgnoreCase)) merged.Files.Add(f);
        }
        var want = ids.Select(Ds1ChrSource.ClipName).Select(s => s + ".hkx").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.GetFiles(dir, "c0000_a*.anibnd.dcx").OrderBy(p => p, StringComparer.Ordinal))
            foreach (var f in BND3.Read(path).Files)
                if (want.Remove(Path.GetFileName(f.Name.Replace('\\', '/')))) merged.Files.Add(f);
        if (want.Count > 0) throw new InvalidDataException($"DS1 c0000 clips not found: {string.Join(", ", want)}");
        return Ds1ChrSource.FromAnibnd(merged);
    }

    // ------------------------------------------------------------------ retarget (I4)

    public sealed class Check
    {
        public double Scale, FaceYawDeg;
        public int Mapped, Ds2Bones;
        public List<string> Ds1Unmapped = [], Ds2Unmapped = [];
        /// <summary>Per checked joint: largest distance (cm) between DS1's world position × scale and ours (decoded NSA).</summary>
        public Dictionary<string, double> JointCm = [];
        /// <summary>Largest per-bone world rotation difference between DS1 and ours, after the bind swing (degrees).</summary>
        public Dictionary<string, double> RotDeg = [];
        public double SeamDs1Deg, SeamOursDeg, EncodeMaxDeg;
        public (V3 Pelvis, V3 Head, V3 LFoot, V3 RFoot, V3 LHand, V3 RHand) Frame0;
        public V3 Ds1PelvisFrame0;
    }

    /// <summary>DS1 bone name (DS2-style after Ds1RigAnims.Ds2BoneName) for a DS2 c0001 rig bone, null = DS1 has none.
    /// The DS2 Root carries the pelvis (Pelvis local translation 0): its DS1 counterpart is DS1 Pelvis's parent.</summary>
    static int[] MapBones(List<string> ds2Names, List<string> ds1Names, List<int> ds1Parents)
    {
        var map = new int[ds2Names.Count];
        int pelvis1 = ds1Names.IndexOf("Pelvis");
        for (int m = 0; m < ds2Names.Count; m++)
        {
            string n = ds2Names[m];
            map[m] = n switch
            {
                "CharacterWorldSpaceTM" or "Character" or "Master" => -1,
                "Root" => pelvis1 >= 0 ? ds1Parents[pelvis1] : -1,
                _ => ds1Names.IndexOf(n),
            };
            if (map[m] >= 0 && map[m] < 3) map[m] = -1; // DS1 bridge bones (CharacterWorldSpaceTM, Character, Trajectory)
        }
        return map;
    }

    static int ChildRank(string child) => child switch
    {
        "Spine1" or "Neck" or "Head" or "Pelvis" or "Spine" => 0,
        "L_Finger2" or "R_Finger2" => 0,
        _ when child.Contains("Twist") || child.Contains("Weapon") || child.Contains("Clavicle") || child.Contains("Thigh") => 2,
        _ => 1,
    };

    static Q4 Arc(V3 a, V3 b)
    {
        double d = a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        if (d > 0.999999) return Q4.Identity;
        if (d < -0.999999)
        {
            var ax = Cross(new V3(1, 0, 0), a);
            if (Len(ax) < 1e-3) ax = Cross(new V3(0, 1, 0), a);
            ax = Unit(ax);
            return new Q4(0, ax.X, ax.Y, ax.Z);
        }
        var c = Cross(a, b);
        return Normalize(new Q4(1 + d, c.X, c.Y, c.Z));
    }

    static V3 Cross(V3 a, V3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    static double Len(V3 a) => Math.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z);
    static V3 Unit(V3 a) { double l = Len(a); return new V3(a.X / l, a.Y / l, a.Z / l); }
    static V3 Sub(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    static V3 Add(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    static V3 Mul(V3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    static (V3 T, Q4 R)[] BindWorld(IReadOnlyList<int> parents, IReadOnlyList<V3> t, IReadOnlyList<Q4> r)
    {
        var w = new (V3 T, Q4 R)[parents.Count];
        for (int i = 0; i < parents.Count; i++)
        {
            int p = parents[i];
            w[i] = p < 0 ? (t[i], r[i]) : (Add(w[p].T, Rotate(w[p].R, t[i])), MorphemeMath.Mul(w[p].R, r[i]));
        }
        return w;
    }

    /// <summary>
    /// I4: a DS1 c0000 clip (bridged into Morpheme space by Ds1AnimBridge: Z up, forward -Y) on DS2's c0001 network rig.
    /// The cutscene track's R3 rule, in Morpheme space: per DS2 bone with a DS1 namesake, world rotation = DS1's world
    /// rotation relative to its bind, applied to the DS2 bind after the swing that turns DS2's bind bone direction onto
    /// DS1's (DS1 binds in a T-pose, DS2 in an A-pose); the pelvis carrier (DS2 Root) takes DS1's pelvis position × the
    /// leg-length ratio; every other bone keeps DS2's bone lengths; bones DS1 lacks keep DS2's bind (local). Channels per
    /// the clip slot's channel map; trajectory = DS1's root motion.
    /// </summary>
    public static NsaAnim Retarget(Ds1AnimBridge.Result src, NmbRig rig2, NmbChannelMap map2, out Check check)
    {
        var s1 = Ds1RigAnims.WithDs2Names(src.Skeleton);
        var a1 = src.Anim;
        var pose1 = MorphemePose.From(s1);
        int n1 = s1.Names.Count, n2 = rig2.Names.Count, nf = a1.Frames;
        check = new Check { Ds2Bones = n2 };

        var t1 = s1.BindPos.Select(p => new V3(p[0], p[1], p[2])).ToList();
        var r1 = s1.BindRot.Select(q => new Q4(q[3], q[0], q[1], q[2])).ToList();
        var bind1 = BindWorld(s1.Parents, t1, r1);
        var t2 = rig2.BindPos.Take(n2).Select(p => new V3(p[0], p[1], p[2])).ToList();
        var q2 = rig2.BindRot.Take(n2).Select(q => Normalize(new Q4(q[3], q[0], q[1], q[2]))).ToList();
        var bind2 = BindWorld(rig2.Parents, t2, q2);
        var map = MapBones(rig2.Names, s1.Names, s1.Parents);
        int I1(string n) => s1.Names.IndexOf(n);
        int I2(string n) => rig2.Names.IndexOf(n);
        check.Mapped = map.Count(x => x >= 0);
        check.Ds2Unmapped = Enumerable.Range(3, n2 - 3).Where(m => map[m] < 0).Select(m => rig2.Names[m]).ToList();
        check.Ds1Unmapped = Enumerable.Range(3, n1 - 3).Where(b => !map.Contains(b)).Select(b => s1.Names[b]).ToList();

        // facing: yaw about Z turning DS1's left vector (L_Thigh - R_Thigh) onto DS2's
        var left1 = Sub(bind1[I1("L_Thigh")].T, bind1[I1("R_Thigh")].T);
        var left2 = Sub(bind2[I2("L_Thigh")].T, bind2[I2("R_Thigh")].T);
        double yaw = Math.Atan2(left2.Y, left2.X) - Math.Atan2(left1.Y, left1.X);
        if (Math.Abs(yaw) < 0.02) yaw = 0;
        check.FaceYawDeg = yaw * 180 / Math.PI;
        var face = new Q4(Math.Cos(yaw / 2), 0, 0, Math.Sin(yaw / 2));
        var faceI = Conj(face);
        V3 F(V3 v) => yaw == 0 ? v : Rotate(face, v);
        Q4 FQ(Q4 q) => yaw == 0 ? q : MorphemeMath.Mul(MorphemeMath.Mul(face, q), faceI);

        // leg-length ratio (hip to foot, bind poses)
        double h1 = Math.Abs(bind1[I1("Pelvis")].T.Z - bind1[I1("L_Foot")].T.Z), h2 = Math.Abs(bind2[I2("Pelvis")].T.Z - bind2[I2("L_Foot")].T.Z);
        double scale = h2 / Math.Max(0.01, h1);
        check.Scale = scale;

        // bind swings: DS1 bind direction (bone → main mapped child) onto DS2's
        var swing = new Q4[n2];
        for (int m = 0; m < n2; m++)
        {
            swing[m] = m >= 3 && rig2.Parents[m] >= 0 ? swing[rig2.Parents[m]] : Q4.Identity;
            if (map[m] < 0) continue;
            var kids = Enumerable.Range(0, n2).Where(c => rig2.Parents[c] == m && map[c] >= 0).OrderBy(c => ChildRank(rig2.Names[c])).ToList();
            foreach (int child in kids)
            {
                var d1 = F(Sub(bind1[map[child]].T, bind1[map[m]].T));
                var d2 = Sub(bind2[child].T, bind2[m].T);
                if (Len(d1) < 1e-4 || Len(d2) < 1e-4) continue;
                swing[m] = Arc(Unit(d1), Unit(d2));
                break;
            }
        }

        int nch = map2.AnimToRig.Count;
        var anim = new NsaAnim
        {
            Fps = a1.Fps, Duration = a1.Duration, ChannelCount = nch,
            Pos = new V3[nch][], Rot = new Q4[nch][], RootPos = new V3[nf], RootRot = new Q4[nf],
        };
        for (int c = 0; c < nch; c++) { anim.Pos[c] = new V3[nf]; anim.Rot[c] = new Q4[nf]; }
        var w2 = new (V3 T, Q4 R)[n2];
        var local = new (V3 T, Q4 R)[n2];
        var prev = new Q4[n2];
        for (int f = 0; f < nf; f++)
        {
            var (wp1, wq1) = pose1.World(a1, f, false);
            for (int m = 0; m < n2; m++)
            {
                int p = rig2.Parents[m];
                (V3 T, Q4 R) parent = p >= 0 ? w2[p] : (new V3(0, 0, 0), Q4.Identity);
                (V3 T, Q4 R) world;
                if (map[m] >= 0)
                {
                    int b = map[m];
                    var rot = Normalize(MorphemeMath.Mul(MorphemeMath.Mul(MorphemeMath.Mul(FQ(wq1[b]), Conj(FQ(bind1[b].R))), Conj(swing[m])), bind2[m].R));
                    var pos = rig2.Names[m] == "Root" ? Mul(F(wp1[I1("Pelvis")]), scale) : Add(parent.T, Rotate(parent.R, t2[m]));
                    world = (pos, rot);
                }
                else world = m < 3 ? (t2[m], q2[m]) : (Add(parent.T, Rotate(parent.R, t2[m])), MorphemeMath.Mul(parent.R, q2[m]));
                w2[m] = world;
                var inv = Conj(parent.R);
                var lq = Normalize(MorphemeMath.Mul(inv, world.R));
                if (f > 0 && Dot(lq, prev[m]) < 0) lq = lq.Neg();   // hemisphere continuity
                prev[m] = lq;
                local[m] = (Rotate(inv, Sub(world.T, parent.T)), lq);
            }
            for (int c = 0; c < nch; c++)
            {
                int bone = map2.AnimToRig[c];
                var (lt, lr) = bone < 3 ? (new V3(0, 0, 0), Q4.Identity) : local[bone];
                anim.Pos[c][f] = new V3(F32(lt.X), F32(lt.Y), F32(lt.Z));
                anim.Rot[c][f] = lr;
            }
            anim.RootPos[f] = F(a1.RootPos[f]);
            anim.RootRot[f] = FQ(a1.RootRot[f]);
        }
        return anim;
    }

    /// <summary>Compares the encoded clip (decoded back, FK on DS2's rig) with DS1's motion: joint positions (DS1 × scale)
    /// and world rotations relative to the binds; the loop seam (last frame vs first) of both.</summary>
    public static void CompareFor(Ds1AnimBridge.Result src, NsaAnim back, NmbRig rig2, NmbChannelMap map2, Check check) => Compare(src, back, rig2, map2, check);

    static void Compare(Ds1AnimBridge.Result src, NsaAnim back, NmbRig rig2, NmbChannelMap map2, Check check)
    {
        var s1 = Ds1RigAnims.WithDs2Names(src.Skeleton);
        var pose1 = MorphemePose.From(s1);
        var pose2 = MorphemePose.From(rig2, map2);
        int nf = Math.Min(src.Anim.Frames, back.Frames);
        string[] joints = ["Pelvis", "Spine1", "Head", "L_Hand", "R_Hand", "L_Foot", "R_Foot", "L_Calf", "R_Calf"];
        var yaw = check.FaceYawDeg * Math.PI / 180;
        var face = new Q4(Math.Cos(yaw / 2), 0, 0, Math.Sin(yaw / 2));
        foreach (var j in joints) { check.JointCm[j] = 0; check.RotDeg[j] = 0; }
        for (int f = 0; f < nf; f++)
        {
            var (p1, _) = pose1.World(src.Anim, f, false);
            var (p2, _) = pose2.World(back, f, false);
            foreach (var j in joints)
            {
                int a = pose1.Index(j), b = pose2.Index(j);
                if (a < 0 || b < 0) continue;
                var v1 = Mul(yaw == 0 ? p1[a] : Rotate(face, p1[a]), check.Scale);
                check.JointCm[j] = Math.Max(check.JointCm[j], 100 * MorphemePose.Dist(v1, p2[b]));
            }
            if (f == 0)
            {
                V3 P(string n) => p2[pose2.Index(n)];
                check.Frame0 = (P("Pelvis"), P("Head"), P("L_Foot"), P("R_Foot"), P("L_Hand"), P("R_Hand"));
                check.Ds1PelvisFrame0 = p1[pose1.Index("Pelvis")];
            }
        }
        double Seam(NsaAnim a)
        {
            double m = 0;
            for (int c = 0; c < a.ChannelCount; c++) m = Math.Max(m, AngleDeg(a.Rot[c][0], a.Rot[c][a.Frames - 1]));
            return m;
        }
        check.SeamDs1Deg = Seam(src.Anim);
        check.SeamOursDeg = Seam(back);
    }

    // ------------------------------------------------------------------ network + anibnd (I1b, I5)

    /// <summary>
    /// I1b: the NPC network's own Morpheme identity. DS2 shares NMB objects by asset id: the bundle loader
    /// (FUN_1404e0f40) looks every bundle's id up in the global asset manager (FUN_1405f5aa0, a hash map keyed by the id
    /// alone) and, when found, only adds a reference to the registered object (FUN_1405f5c00) and skips this file's
    /// copy; the file-name table is named <c>MorphemeFileNameLookUpTable_%08x</c> after the network def's id (disassembly:
    /// 0x1404e0fd4 keeps the type-10 bundle's id in r15d, passed at 0x1404e11b3), the clip container
    /// <c>MorphemeAnimContainer_%08x</c> likewise (FUN_1404e1d10), and an existing container only gets this anibnd's
    /// clips in its empty slots (FUN_1404deb10). c0001 (the player) is resident
    /// from boot, so a copy keeping c0001's network-def id would run the player's NetworkDef and clips (Saulden's), never
    /// ours. The network def therefore gets a new id + GUID (the id occurs once in the NMB, in its own bundle header);
    /// the rig, channel maps and event tracks keep c0001's ids (content identical: they resolve to the player's objects).
    /// Every vanilla anibnd has its own network-def id (173/173). Deterministic: MD5 of a seed, re-drawn on a clash with
    /// any id of c0001's NMB; global uniqueness (all vanilla + pack anibnds) is checked offline by <c>nmb-ids</c>.
    /// </summary>
    public static (uint Id, byte[] Guid) NetworkIdentity(NmbFile nmb) => NetworkIdentity(nmb, AnibndName);

    /// <summary>I1b per network name (the Template route: one per <c>c{AnibndId}</c>, rule I1d).</summary>
    public static (uint Id, byte[] Guid) NetworkIdentity(NmbFile nmb, string name)
    {
        var taken = nmb.Bundles.Select(b => b.AssetId).ToHashSet();
        for (int k = 0; ; k++)
        {
            byte[] h = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.ASCII.GetBytes($"Lordran.NpcIdle/{name}/NetworkDef/{k}"));
            uint id = BitConverter.ToUInt32(h, 0);
            if (id == 0 || id == 0xFFFFFFFF || taken.Contains(id)) continue;
            byte[] g = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.ASCII.GetBytes($"Lordran.NpcIdle/{name}/NetworkDef/{k}/guid"));
            g[6] = (byte)((g[6] & 0x0F) | 0x40);   // random-GUID layout like DS2's network GUIDs (c06d330f-45c3-402a-9496-…)
            g[8] = (byte)((g[8] & 0x3F) | 0x80);
            return (id, g);
        }
    }

    public static Result Build(string dsrRoot, string ds2Data)
    {
        var res = new Result();
        string srcPath = Path.Combine(ds2Data, "morpheme4", "chr", "c0001.anibnd.dcx");
        byte[] raw = File.ReadAllBytes(srcPath);
        DCX.CompressionInfo? comp = null;
        if (DCX.Is(raw)) raw = DCX.Decompress(raw, out comp);
        var bnd = BND4.Read(raw);
        var nmbFile = bnd.Files.First(f => f.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase));
        var nmb = NmbFile.Read(nmbFile.Bytes);
        var rig2 = NmbRig.Read(nmb.Bundles[nmb.IndexOf(1)].Data);
        var maps = nmb.Bundles.Where(b => b.Type == 2).ToDictionary(b => b.AssetId, b => NmbChannelMap.Read(b.Data));
        int netIndex = nmb.IndexOf(10);
        var net = new NmbNetwork((byte[])nmb.Bundles[netIndex].Data.Clone());
        var fileNames = NmbFileNames.Read(nmb.Bundles[nmb.IndexOf(13)].Data).Anims;
        var trackBundle = new Dictionary<uint, int>();
        for (int i = 0; i < nmb.Bundles.Count; i++) if (nmb.Bundles[i].Type is 3 or 4 or 5) trackBundle[nmb.Bundles[i].AssetId] = i;
        var animNodes = net.Nodes().Where(x => x.Type == 104).ToList();

        var ds1 = LoadC0000(dsrRoot, Slots.Select(s => s.Ds1Anim));
        res.Report.Add($"network {AnibndName} = c0001's (rig {rig2.Names.Count} bones, {fileNames.Count} clip names, {animNodes.Count} anim nodes, {bnd.Files.Count(f => f.Name.EndsWith(".nsa", StringComparison.OrdinalIgnoreCase))} NSA files) with {Slots.Length} DS1 idle slots");
        var repl = new Dictionary<int, byte[]>();

        foreach (var slot in Slots)
        {
            int ci = fileNames.FindIndex(n => n.StartsWith(slot.Clip, StringComparison.OrdinalIgnoreCase));
            if (ci < 0) throw new InvalidDataException($"c0001 network has no clip {slot.Clip}");
            string nsaName = fileNames[ci].EndsWith(".nsa", StringComparison.OrdinalIgnoreCase) ? fileNames[ci] : fileNames[ci] + ".nsa";
            var file = bnd.Files.FirstOrDefault(f => Path.GetFileName(f.Name.Replace('\\', '/')).Equals(nsaName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"c0001.anibnd has no {nsaName}");
            var users = animNodes.Where(n => n.Attribs.Any(a => a.Type == 21 && net.ClipIndex(a) == ci)).ToList();
            var mapIds = users.SelectMany(n => n.Attribs.Where(a => a.Type == 21).Select(a => net.U32(a.Offset + 0x78))).Distinct().ToList();
            if (mapIds.Count != 1) throw new InvalidDataException($"{slot.Clip}: {mapIds.Count} channel maps");
            var map2 = maps[mapIds[0]];
            var ds2Orig = NsaCodec.Decode(NsaFile.Parse(file.Bytes));
            if (ds2Orig.ChannelCount != map2.AnimToRig.Count) throw new InvalidDataException($"{slot.Clip}: NSA {ds2Orig.ChannelCount} channels, map {map2.AnimToRig.Count}");

            // DS1 clips are 30 fps; the bridge's default (frames-1)/duration gives 29.99999 on a00_7805
            var src = Ds1AnimBridge.Bridge(ds1.Samples(slot.Ds1Anim), 30);
            var anim = Retarget(src, rig2, map2, out var check);
            byte[] nsa = NsaEncoder.EncodeFile(anim).Build();
            var back = NsaCodec.Decode(NsaFile.Parse(nsa));
            Compare(src, back, rig2, map2, check);
            double enc = 0;
            for (int c = 0; c < anim.ChannelCount; c++) for (int f = 0; f < anim.Frames; f++) enc = Math.Max(enc, AngleDeg(anim.Rot[c][f], back.Rot[c][f]));
            check.EncodeMaxDeg = enc;
            file.Bytes = nsa;
            double d1 = (back.Frames - 1) / back.Fps;

            var sr = new SlotResult
            {
                Slot = slot, NsaName = nsaName, ClipIndex = ci, Nsa = nsa, Ds2 = back, Ds2Original = ds2Orig, Ds1 = src, Check = check,
                Ds1Duration = d1, Ds2Duration = ds2Orig.Duration ?? (ds2Orig.Frames - 1) / ds2Orig.Fps, Ds1Frames = back.Frames,
                Nodes = users.Select(n => n.Index).ToList(),
            };
            // SourceAnim attribs (every anim set) of every node that plays the clip: duration, trajectory, whole clip
            foreach (var n in users)
                foreach (var sa in n.Attribs.Where(a => a.Type == 21))
                {
                    net.PatchSourceAnim(sa, back.RootPos[0], back.RootRot[0], back.RootPos[^1], back.RootRot[^1], d1);
                    net.PutF32(sa.Offset + 0x80, 0f); net.PutF32(sa.Offset + 0x84, 1f);
                }
            // event tracks of those nodes: their TimeAct ids (TAE, I7). Tracks are left as they are: an edited track would
            // need its own asset id too (shared ids resolve to the player's track), and DS2 data has no event track with 0
            // events (0 of 94,011) — the dropped I6 head-look edit, see NOTES §2.
            foreach (var n in users)
                foreach (var at in n.Attribs.Where(a => a.Type == 23))
                    foreach (var (_, tid, _) in net.Tracks(at))
                    {
                        if (!trackBundle.TryGetValue(tid, out int bi)) continue;
                        var tr = NmbEventTrack.Read(nmb.Bundles[bi].Data, nmb.Bundles[bi].Type);
                        if (tr.Name == "TimeAct") foreach (var ev in tr.Events) if (!sr.TimeAct.Contains(ev.Value)) sr.TimeAct.Add(ev.Value);
                    }
            // I6b (session 9, track npcpose): DS1's idle has no head look, so the slot leaf drops its DS2 head-look track
            // reference (the track itself stays, shared, unedited: the node just no longer lists it)
            if (Ds1Layers)
                foreach (var line in DropHeadLook(net, nmb, trackBundle, users)) res.Report.Add($"  I6b {line}");
            res.Slots.Add(sr);
            var ck = check;
            res.Report.Add($"slot StartUpCategory {slot.Category} ← DS1 a00_{slot.Ds1Anim} ({slot.Who}): clip {nsaName} (index {ci}, nodes {string.Join(",", sr.Nodes)}, TimeAct {string.Join(",", sr.TimeAct)}); DS1 {src.Frames} frames {d1:0.###} s (DS2 stand-in {sr.Ds2Duration:0.###} s); {nsa.Length} bytes");
            res.Report.Add($"  retarget: {ck.Mapped} of {ck.Ds2Bones} DS2 bones driven, leg scale {ck.Scale:0.###}, facing turn {ck.FaceYawDeg:0.#}°; DS2 bones without DS1 namesake (bind): {string.Join(" ", ck.Ds2Unmapped)}; DS1 bones unused: {string.Join(" ", ck.Ds1Unmapped)}");
            res.Report.Add($"  check: joint distance DS1×scale vs ours (cm, max over frames) {string.Join(", ", ck.JointCm.Select(kv => $"{kv.Key} {kv.Value:0.#}"))}; encode max {ck.EncodeMaxDeg:0.###}°; loop seam DS1 {ck.SeamDs1Deg:0.##}° ours {ck.SeamOursDeg:0.##}°");
            {
                var pz = MorphemePose.From(rig2, map2);
                var (ow, _) = pz.World(ds2Orig, 0, false);
                V3 O(string n) => ow[pz.Index(n)];
                res.Report.Add($"  DS2 stand-in {slot.Clip} frame 0: pelvis {Fmt(O("Pelvis"))}, head {Fmt(O("Head"))}, feet {Fmt(O("L_Foot"))} {Fmt(O("R_Foot"))}, hands {Fmt(O("L_Hand"))} {Fmt(O("R_Hand"))}");
            }
            res.Report.Add($"  pose frame 0 (Morpheme space, root at the origin, forward -Y): pelvis {Fmt(ck.Frame0.Pelvis)} (DS1 {Fmt(ck.Ds1PelvisFrame0)}), head {Fmt(ck.Frame0.Head)}, feet {Fmt(ck.Frame0.LFoot)} {Fmt(ck.Frame0.RFoot)}, hands {Fmt(ck.Frame0.LHand)} {Fmt(ck.Frame0.RHand)}; DS1 TAE {Ds1Tae(ds1, slot.Ds1Anim)}");
        }
        if (Ds1Layers) res.Report.Add(FootIkOff(net, res.Slots, ds1));
        if (ProbeIdle != null && Route == IdleRoute.Own) Probe(net, bnd, fileNames, animNodes, maps, res);
        repl[netIndex] = net.X;
        var netBundle = nmb.Bundles[netIndex];
        if (Route == IdleRoute.Own)
        {
            // I1b: the network def's own asset id + GUID (own NetworkDef and MorphemeAnimContainer in DS2's asset manager)
            uint oldId = netBundle.AssetId;
            (netBundle.AssetId, netBundle.Guid) = NetworkIdentity(nmb);
            res.Report.Add($"network def asset id 0x{oldId:X8} (c0001, the player's) → 0x{netBundle.AssetId:X8}, GUID {Convert.ToHexString(netBundle.Guid).ToLowerInvariant()}: own NetworkDef + MorphemeAnimContainer_{netBundle.AssetId:x8} (rule I1b); rig, channel maps, event tracks keep c0001's ids (identical content)");
        }
        else
        {
            res.Report.Add($"I1c: human route: the pack's c0001.anibnd.dcx replaces the boot file (gamedata:/morpheme4/chr/c0001.anibnd.dcx), network def keeps c0001's id 0x{netBundle.AssetId:X8}: one NetworkDef + MorphemeAnimContainer_{netBundle.AssetId:x8} for the player and every human NPC (EnemyParam animId 100); the player's own startup states are 1-6 (wake-ups) and 11 (phantom arrival, P), the DS1 slots ({string.Join(", ", Slots.Select(s => s.Category))}) are NPC-only");
            foreach (var (cat, who) in VanillaOnSlots.Where(v => Slots.Any(s => s.Category == v.Category)))
                res.Report.Add($"I1c COLLATERAL (vanilla DS2 content, user decision pending): {who} is on StartUpCategory {cat} and plays DS1 a00_{Slots.First(s => s.Category == cat).Ds1Anim} too; --no-npc-idle = no c0001 in the pack");
        }
        res.NetworkAssetId = netBundle.AssetId;
        nmbFile.Bytes = nmb.Rebuild(repl);
        res.Nmb = nmbFile.Bytes;
        nmbFile.Name = nmbFile.Name.Replace("c0001", AnibndName);   // vanilla anibnds hold cNNNN.nmb (c2501 rule)
        byte[] outBnd = bnd.Write();
        res.Files[$"morpheme4/chr/{AnibndName}.anibnd.dcx"] = comp != null ? DCX.Compress(outBnd, comp) : outBnd;

        // ---- TimeAct (I7): c000100's three files as c800100's; the replaced idles get DS1's length, _pl's whole-clip
        // flags (110210, 111400, 290002 …) time-scaled to it, _sfx/_snd events of the DS2 clip dropped
        foreach (var kind in new[] { "pl", "sfx", "snd" })
        {
            var tae = new Ds2Tae(File.ReadAllBytes(Path.Combine(ds2Data, "timeact", "chr", $"c000100_{kind}.tae")));
            var notes = new List<string>();
            foreach (var sr in res.Slots)
                foreach (int id in sr.TimeAct)
                {
                    int? len0 = tae.Length(id);
                    tae.SetLength(id, (int)Math.Round(sr.Ds1Duration * 30));
                    int nev = tae.EventCount(id);
                    if (kind == "pl") { if (nev > 0 && sr.Ds2Duration > 0) tae.ScaleTimes(id, sr.Ds1Duration / sr.Ds2Duration); }
                    else if (nev > 0) tae.ClearEvents(id);
                    notes.Add($"{id}: length {len0} → {tae.Length(id)}, {nev} events {(kind == "pl" ? "time-scaled" : "dropped")}");
                }
            res.Files[$"timeact/chr/c{AnimId:D6}_{kind}.tae"] = tae.Bytes;
            res.Report.Add($"c{AnimId:D6}_{kind}.tae (from c000100_{kind}): {string.Join("; ", notes)}");
        }
        return res;
    }

    static string Fmt(V3 v) => $"({v.X.ToString("0.00", Inv)}, {v.Y.ToString("0.00", Inv)}, {v.Z.ToString("0.00", Inv)})";

    /// <summary>A/B probe of rule I1c (own route): every BasicMove idle clip entry of the NPC network gets the NSA bytes of
    /// the vanilla clip <see cref="ProbeIdle"/> (same channel map required) and the SourceAnims playing them its duration
    /// and trajectory (as I5). Test only: an unmistakable silhouette in the state the generic class leaves the network in.</summary>
    static void Probe(NmbNetwork net, BND4 bnd, List<string> fileNames, List<NmbNetwork.Node> animNodes, Dictionary<uint, NmbChannelMap> maps, Result res)
    {
        BinderFile Entry(string prefix) => bnd.Files.FirstOrDefault(f => Path.GetFileName(f.Name.Replace('\\', '/')).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && f.Name.EndsWith(".nsa", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"c0001.anibnd has no {prefix}*.nsa");
        var probeFile = Entry(ProbeIdle!);
        byte[] probe = (byte[])probeFile.Bytes.Clone();
        var pa = NsaCodec.Decode(NsaFile.Parse(probe));
        double dur = (pa.Frames - 1) / pa.Fps;
        foreach (string idle in BasicMoveIdles)
        {
            int ci = fileNames.FindIndex(n => n.StartsWith(idle, StringComparison.OrdinalIgnoreCase));
            var file = Entry(idle);
            var users = animNodes.Where(n => n.Attribs.Any(a => a.Type == 21 && net.ClipIndex(a) == ci)).ToList();
            foreach (uint mapId in users.SelectMany(n => n.Attribs.Where(a => a.Type == 21).Select(a => net.U32(a.Offset + 0x78))).Distinct())
                if (maps[mapId].AnimToRig.Count != pa.ChannelCount) throw new InvalidDataException($"probe {ProbeIdle}: {pa.ChannelCount} channels, {idle}'s map {maps[mapId].AnimToRig.Count}");
            file.Bytes = probe;
            foreach (var n in users)
                foreach (var sa in n.Attribs.Where(a => a.Type == 21 && net.ClipIndex(a) == ci))   // node 360: set 1 plays a00_19_0010
                {
                    net.PatchSourceAnim(sa, pa.RootPos[0], pa.RootRot[0], pa.RootPos[^1], pa.RootRot[^1], dur);
                    net.PutF32(sa.Offset + 0x80, 0f); net.PutF32(sa.Offset + 0x84, 1f);
                }
            res.ProbeNsas.Add(Path.GetFileName(file.Name.Replace('\\', '/')));
            res.Report.Add($"PROBE (A/B, not for main): {Path.GetFileName(file.Name.Replace('\\', '/'))} (BasicMove idle, nodes {string.Join(",", users.Select(n => n.Index))}) ← {Path.GetFileName(probeFile.Name.Replace('\\', '/'))} ({pa.Frames} frames {dur:0.###} s)");
        }
    }

    // ------------------------------------------------------------------ DS2 run-time layers on the DS1 slots (track npcpose)

    /// <summary>build-pack --npc-ds2-layers turns I6b/I9 off (A/B: DS2's head look and uneven-terrain IK on the DS1 slots).</summary>
    public static bool Ds1Layers = true;

    /// <summary>build-pack --npc-foot-ik-off (pass 3, A/B): I9 also on the human route, where node 8286 is shared with DS2
    /// content (the player's wake-ups, phantom arrivals, every vanilla human NPC's startup state). Off by default there.</summary>
    public static bool FootIkOffShared;

    /// <summary>
    /// I6b: the slot leaf's SourceEventTracks (attrib 23, every anim set) lose their head-look track references (event
    /// tracks named LookAt*: c0001's LookAtFromNeck, value 64, drives SM 8290 into a HeadLook node that turns Neck+Head
    /// towards the target; 65-67 the spine chains). DS1's idles carry no look-at (their TAE: 7805 → 226, 16, JT 7, JT 28,
    /// 308, JT 24; 7810 → 226, 16, JT 7, JT 28, 308), so a DS1 slot keeps the pose its clip has. The entry is removed by
    /// shifting the later ids/sizes down and decrementing the group's count (track order kept; the sync index of group 0
    /// is untouched: head-look tracks are duration tracks, group 2). c0001's 8175 (Saulden) has none; 8249 (Maughlin03,
    /// the Crestfallen's slot 20) has LookAtFromNeck.
    /// </summary>
    static List<string> DropHeadLook(NmbNetwork net, NmbFile nmb, Dictionary<uint, int> trackBundle, IEnumerable<NmbNetwork.Node> leaves)
    {
        var notes = new List<string>();
        foreach (var n in leaves)
            foreach (var at in n.Attribs.Where(a => a.Type == 23))
            {
                int e = at.Offset;
                for (int k = 0; k < 3; k++)
                {
                    int cntOff = e + 0x10 + 0x18 * k;
                    int cnt = (int)net.U32(cntOff);
                    int pid = (int)net.U64(e + 0x18 + 0x18 * k), psz = (int)net.U64(e + 0x20 + 0x18 * k);
                    for (int j = cnt - 1; j >= 0; j--)
                    {
                        uint tid = net.U32(e + pid + 8 * j);
                        if (!trackBundle.TryGetValue(tid, out int bi)) continue;
                        var tr = NmbEventTrack.Read(nmb.Bundles[bi].Data, nmb.Bundles[bi].Type);
                        if (!tr.Name.StartsWith("LookAt", StringComparison.Ordinal)) continue;
                        for (int m = j; m < cnt - 1; m++)
                        {
                            net.PutU32(e + pid + 8 * m, net.U32(e + pid + 8 * (m + 1)));
                            net.PutU32(e + pid + 8 * m + 4, net.U32(e + pid + 8 * (m + 1) + 4));
                            if (psz != 0) net.PutU32(e + psz + 4 * m, net.U32(e + psz + 4 * (m + 1)));
                        }
                        net.PutU32(e + pid + 8 * (cnt - 1), 0); net.PutU32(e + pid + 8 * (cnt - 1) + 4, 0);
                        if (psz != 0) net.PutU32(e + psz + 4 * (cnt - 1), 0);
                        cnt--;
                        net.PutU32(cntOff, (uint)cnt);
                        notes.Add($"node {n.Index} attrib 23 @0x{e:X}: dropped head-look track {tr.Name.Trim()} 0x{tid:X8} (group {k}, events {string.Join(",", tr.Events.Select(x => x.Value))}); {cnt} left");
                    }
                }
            }
        if (notes.Count == 0) notes.Add($"nodes {string.Join(",", leaves.Select(n => n.Index))}: no head-look track (nothing to drop)");
        return notes;
    }

    /// <summary>c0001's startup path (SM 210 → BT_StartUp 8287 → 8286 uneven-terrain IK, type 138, CP inputs
    /// UnevenTerrain_Hips 42, UnevenTerrain_BlendWeight 43, UnevenTerrain_Prediction 70); CP 46 = BlendWeight_0, a
    /// constant float 0 (default 0; read by 4 Blend2 nodes, written by no game code: not in the IK module's CP list).</summary>
    const int UnevenTerrainNode = 8286, UnevenTerrainWeightCp = 43, ConstantZeroCp = 46;

    /// <summary>
    /// I9 (was the candidate, session 9 track npcpose): DS1 JT 28 "Disable Foot IK" on a slot's DS1 idle → DS2's
    /// uneven-terrain IK off on the startup path: node 8286's blend-weight input is rewired from the game-driven CP 43
    /// (FUN_140362800: chr+0xb8+0x150 unless chr+0xc0+0xa8) to the constant-0 CP 46. The node is shared by every startup
    /// slot of the NPC network, so it applies only when every DS1 slot's idle disables foot IK over its whole length (today
    /// 7805 and 7810: JT 28 0-4.77 s); else nothing changes and the report says why. Without it DS2 may pull each foot onto
    /// the floor under it (Oscar's right foot 0.42 m, left 0.02 m over our collision: the legs would reach down the
    /// rubble, the pelvis follow by UnevenTerrain_Hips; in game U). Pass 3: on the human route (I1c) the node also serves
    /// DS2 content (the player's wake-ups 1-6, phantom arrival 11, every vanilla human NPC's state 12-20), so I9 applies
    /// there only with <see cref="FootIkOffShared"/>; by default DS2's IK stays = a listed DS2 stand-in for JT 28.
    /// </summary>
    static string FootIkOff(NmbNetwork net, List<SlotResult> slots, Ds1ChrSource ds1)
    {
        foreach (var s in slots)
        {
            var a = ds1.TaeAnim(s.Slot.Ds1Anim);
            bool off = a != null && a.Events.Any(e => e.Type == 0 && BitConverter.ToInt32(e.GetParameterBytes(false), 0) == 28
                && e.StartTime <= 0.001f && e.EndTime >= s.Ds1Duration - 0.001);
            if (!off) return $"I9: not applied: DS1 a00_{s.Slot.Ds1Anim} has no JT 28 over its whole clip (the shared IK node stays DS2's)";
        }
        if (Route == IdleRoute.Human && !FootIkOffShared)
            return $"I9: not applied (human route): DS1 JT 28 on {string.Join(", ", slots.Select(s => $"a00_{s.Slot.Ds1Anim}"))}, but node {UnevenTerrainNode} is c0001's shared startup path (the player's wake-ups 1-6, phantom arrival 11, every vanilla human NPC's startup state 12-20): DS2's uneven-terrain IK stays = listed DS2 stand-in for JT 28 (--npc-foot-ik-off forces it, A/B)";
        var n = net.GetNode(UnevenTerrainNode);
        var c0 = net.GetNode(ConstantZeroCp);
        var cpA = NmbNetwork.Find(c0, 3);
        if (n.Type != 138 || c0.Type != 20 || cpA == null || net.F32(cpA.Offset + 0x10) != 0f)
            throw new InvalidDataException($"I9: c0001 layout changed (node {UnevenTerrainNode} type {n.Type}, CP {ConstantZeroCp} type {c0.Type})");
        int ncp = net.X[n.Offset + 0x0E];
        int pCp = (int)net.U64(n.Offset + 0x28);
        for (int k = 0; k < ncp; k++)
        {
            int o = n.Offset + pCp + 4 * k;
            if (net.U16(o) != UnevenTerrainWeightCp) continue;
            BinaryPrimitives.WriteUInt16LittleEndian(net.X.AsSpan(o), ConstantZeroCp);
            return $"I9: node {UnevenTerrainNode} (uneven-terrain IK, startup path) CP input {k} {UnevenTerrainWeightCp} UnevenTerrain_BlendWeight → {ConstantZeroCp} BlendWeight_0 (constant 0): DS1 JT 28 Disable Foot IK on every DS1 slot ({string.Join(", ", slots.Select(s => $"a00_{s.Slot.Ds1Anim}"))})";
        }
        throw new InvalidDataException($"I9: node {UnevenTerrainNode} has no CP input {UnevenTerrainWeightCp}");
    }

    public static string Ds1Tae(Ds1ChrSource ds1, int id)
    {
        var a = ds1.TaeAnim(id);
        if (a == null) return "(no entry)";
        if (a.Events.Count == 0) return "(no events)";
        return string.Join(" ", a.Events.Select(e => $"type {e.Type}{(e.Type == 0 ? $" JT {BitConverter.ToInt32(e.GetParameterBytes(false), 0)}" : "")} {e.StartTime:0.##}-{e.EndTime:0.##} [{Convert.ToHexString(e.GetParameterBytes(false))}]"));
    }
}
