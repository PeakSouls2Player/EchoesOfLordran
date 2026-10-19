using System.Globalization;
using Lordran.Tools.Formats;
using Lordran.Tools.Formats.Morpheme;
using SoulsFormats;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// DS1 attacks in a DS2 network (session 8, track enemies step 1; user directive: DS1 enemies 1:1, DS1 attack patterns and
/// timings). Every DS1 attack the character's battle goal uses gets an SM_Attack category of its own, every DS1 combo chain
/// a combo category, each stage playing the DS1 clip with event tracks and TimeAct events built from the clip's DS1 TAE.
/// <para><b>Numbering</b> (NETWORK-FORMAT.md §6, DS2-AI-NOTES §5; permanent, for the DS1 Lua → AI ESD translator): DS1 attack
/// id A (the animation id the DS1 goal requests, 3000-3029) → DS2 action n = A − 3000 = AttackCategory_Ene n (band [0,9] or
/// [10,39]; the engine reads EnemyBehaviorParam / Second / Third action n % 10). DS1 combo chain k (ComboAttack → ComboRepeat*
/// → ComboFinal, found in the battle goal) → EnemyComboBehaviorParam combo k (AI action 50 + k) with AttackMotionId 30 + k,
/// routes 1, one stage per clip. The [10,19] AttackCategory_Ene conditions are widened to [10,39] network-wide (SM_Main →
/// SM_Attack and the band hubs; NETWORK-FORMAT.md §6 item 2), so categories 20-39 need no Shot route.</para>
/// <para><b>Interim aliases</b> (removed when step 2 translates the DS1 AI): until then the template's DS2 AI drives the
/// character and requests its own hard-coded actions (Hollow Infantry ai103000: 10, 11, 12, combos 50 / 51). An alias gives
/// such an action number a category playing a DS1 attack (only where the number is not a DS1 attack itself), so the DS2 AI
/// can only ever produce DS1 attacks. Combos 50 / 51 are DS1 chains 0 / 1 by the numbering above.</para>
/// <para><b>Template categories</b>: a single-stage category SM (c1030 weapon 0 category 10: one state, PlaySpeed → attack
/// clip + additive turn layer) and a route-chained one (c1030 weapon 2 category 10: 3 stages, stage s → s+1 on
/// AttackStateControl 101 ∧ Combo{s}_Route 1 or 102 ∧ route 2) are cloned (<see cref="NmbNetClone"/>); the categories the
/// template network had under the character's weapon category are retired (moved to values 900+k that no action requests).</para>
/// <para><b>DS1 TAE → DS2</b> (per clip; DS1 event names from Smithbox TAE.Template.DS1.xml; DS2 track meaning from what
/// SM_Main tests, Ds1RigAnims.KindOf): type 1 InvokeAttackBehavior windows (judge J → BehaviorParam 2_VVVVV_JJJ) → HitDuration
/// 123 windows and TimeAct 2200 events (param ID = the hit's damage slot); type 2 InvokeBulletBehavior → TimeAct 2300 (ID =
/// bullet slot); JumpTable 23 "AI Continuation Attack Flag" start → AttackStateControl 101 (the combo window the next stage
/// waits for); JT 86 "AI Attack Cancel" / 79 "AI Dodge Cancel" / 78 "AI Movement Cancel" → AttackCancelControl_Attack 112 /
/// _Avoid 110 / _Move 111 end and AttackDuration 120 end (the earliest of them = DS1's recovery); JT 7 "Disable Turning" →
/// TimeAct 280100 turn rate 0 (else NpcParam.turnVellocity); return to base (80) at DS1's AI Movement Cancel (JT 78, else the
/// recovery), never inside DS2's 0.4 s blend tail; Footsteps sync [0, first hit) [first hit, 1] like every DS2 attack clip; foot-lock windows from the DS1 motion
/// (rule C11). DS2's unnamed TimeAct events 120200 / 110000 (and 2105 on shots) keep their place relative to the hit window
/// (piecewise-linear time warp of the template clip's events: 0, hit start, hit end, clip end).</para>
/// </summary>
public static class Ds1Attacks
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>One DS1 attack the battle goal requests: DS1 attack id (3000+), optional max range (the goal's local
    /// AttXXXX_Dist_max) for EnemyBehaviorParam.AttackRange, evidence.</summary>
    public sealed record Attack(int Id, string Name, float Range, string Evidence)
    {
        /// <summary>E1's number for DS1 attacks 3000-3029; other DS1 ids (steps 700-703, the 3200 stance) get theirs from
        /// <see cref="Plan.ActionOf"/>.</summary>
        public int Action => Id is >= 3000 and < 3030 ? Id - 3000 : throw new InvalidOperationException($"DS1 {Id}: use Plan.ActionOf");
    }

    /// <summary>A DS1 combo chain (ComboAttack → ComboRepeat* → ComboFinal / Attack + follow-up) as DS1 attack ids.</summary>
    public sealed record Chain(int[] Ids, string Evidence);

    /// <summary>Interim: DS2 template-AI action number → DS1 attack id.</summary>
    public sealed record Alias(int Action, int Id, string Evidence);

    public sealed record Plan(int AnimOffset, int BehaviorVariation, int WeaponCategory, Attack[] Attacks, Chain[] Chains, Alias[] Aliases, float TurnRate = 180f)
    {
        /// <summary>The DS1 clip id of attack id A (A + NpcParam.animIdOffset: a00_3000 / a01_3000 / a02_3000).</summary>
        public int Clip(int id) => id + AnimOffset;
        /// <summary>The clip DS1 plays for attack id A: A + animIdOffset, or the base clip A when the offset set lacks it (DS1's
        /// offset sets hold only their own attacks: c2540 a01 has 3000-3010 but no 3008, c2550 a01 only 3000-3005, while idle and
        /// locomotion always come from a00; P: DS1's fallback inferred from those sets).</summary>
        public int Clip(int id, Ds1ChrSource ds1) => ds1.Has(id + AnimOffset) || !ds1.Has(id) ? id + AnimOffset : id;

        /// <summary>
        /// DS2 action number of DS1 id <paramref name="id"/> (track enemies step 2, rule E21): E1 for the attacks 3000-3029
        /// (n = A − 3000); every other DS1 id the battle goal plays as an EzState — SpinStep / ApproachStep 700-703 (a00_0700-0703)
        /// and non-attack EzStates like the soldier's 3200 stance, which E1 left at category 200 where no action reaches — takes
        /// the highest action number not used by an attack of this plan, 29, 28, … in plan order (actions 20-29 =
        /// EnemyBehaviorThirdParam; their categories sit in the widened [10,39] band, below the combo categories 30+k).
        /// </summary>
        /// <summary>The interim aliases in force: none when the DS1 AI is translated (step 2, <see cref="ChrPorter.UseDs1Ai"/>: E5 removed —
        /// nothing requests the template AI's action numbers any more), the plan's list with build-pack --no-ds1-ai.</summary>
        public Alias[] LiveAliases => ChrPorter.UseDs1Ai ? [] : Aliases;

        public int ActionOf(int id)
        {
            if (id is >= 3000 and < 3030) return id - 3000;
            var used = Attacks.Where(a => a.Id is >= 3000 and < 3030).Select(a => a.Id - 3000).ToHashSet();
            int n = 29;
            foreach (var a in Attacks.Where(a => a.Id is < 3000 or >= 3030))
            {
                while (used.Contains(n)) n--;
                if (n < 10) throw new InvalidDataException($"DS1 {a.Id}: no free action number left (10-29 all used)");
                if (a.Id == id) return n;
                used.Add(n);
            }
            throw new KeyNotFoundException($"DS1 {id} is not in this plan");
        }
    }

    /// <summary>What the DS1 TAE says about an attack clip (times in seconds).</summary>
    public sealed class ClipTiming
    {
        public int Clip; public double Duration;
        public List<(double S, double E, int Judge)> Hits = [];
        public List<(double S, double E, int Dummy, int Judge)> Bullets = [];
        public List<(double S, double E)> NoTurn = [], Continuation = [];
        /// <summary>Session 11 (G9 rule TJ1): DS1 JumpTable 8 "Invincible" and 24 "Poise" (hyper armor) windows.</summary>
        public List<(double S, double E)> Invincible = [], SuperArmor = [];
        /// <summary>Session 11 (track leap12, rule LP1): DS1 JumpTable 51 "Disable Fall Animation" windows (the airborne part of a
        /// leap: c2540 a00_3007 0.3-0.833 s covers 8.1 of its 9.07 m, up to 21.5 m/s) → DS2 TAE 280000 (<see cref="NoFallEvents"/>).</summary>
        public List<(double S, double E)> NoFall = [];
        /// <summary>DS1 TAE 224 "Set Turn Speed" windows (deg/s): DS2's 280100 "Set Character Turn Rate".</summary>
        public List<(double S, double E, float Rate)> TurnSpeed = [];
        public double MoveCancel = -1, DodgeCancel = -1, AttackCancel = -1;
        public double FirstHit => Hits.Count > 0 ? Hits.Min(h => h.S) : Bullets.Count > 0 ? Bullets.Min(b => b.S) : -1;
        public double LastHit => Hits.Count > 0 ? Hits.Max(h => h.E) : Bullets.Count > 0 ? Bullets.Max(b => b.E) : -1;
        /// <summary>DS1's recovery: the earliest AI cancel (attack, dodge or movement); -1 = none (plays to the end).</summary>
        public double Recovery => new[] { AttackCancel, DodgeCancel, MoveCancel }.Where(v => v >= 0).DefaultIfEmpty(-1).Min();
        public List<int> Judges => Hits.Select(h => h.Judge).Distinct().ToList();
    }

    public static ClipTiming Timing(Ds1ChrSource ds1, int clip, double duration)
    {
        var t = new ClipTiming { Clip = clip, Duration = duration };
        var a = ds1.TaeAnim(clip);
        if (a == null) return t;
        foreach (var e in a.Events)
        {
            var p = e.GetParameterBytes(false);
            if (e.Type == 1 && p.Length >= 12) t.Hits.Add((e.StartTime, e.EndTime, BitConverter.ToInt32(p, 8)));
            else if (e.Type == 2 && p.Length >= 12) t.Bullets.Add((e.StartTime, e.EndTime, BitConverter.ToInt32(p, 0), BitConverter.ToInt32(p, 8)));
            else if (e.Type == 224 && p.Length >= 4) t.TurnSpeed.Add((e.StartTime, e.EndTime, BitConverter.ToSingle(p, 0)));
        }
        t.Hits = [.. t.Hits.OrderBy(h => h.S)];
        t.Bullets = [.. t.Bullets.OrderBy(b => b.S)];
        t.NoTurn = Ds1RigAnims.JumpTable(a, 7);
        t.Continuation = Ds1RigAnims.JumpTable(a, 23);
        t.Invincible = Ds1RigAnims.JumpTable(a, 8); t.SuperArmor = Ds1RigAnims.JumpTable(a, 24);
        t.NoFall = Ds1RigAnims.JumpTable(a, 51);
        double First(int jt) { var l = Ds1RigAnims.JumpTable(a, jt); return l.Count > 0 ? l[0].S : -1; }
        t.MoveCancel = First(78); t.DodgeCancel = First(79); t.AttackCancel = First(86);
        return t;
    }

    // ------------------------------------------------------------------ result
    public sealed class ClipOut
    {
        public int Clip, TaeId, Index, HitIndex, BulletIndex;
        /// <summary>Damage slot of each DS1 judge of the clip: HitIndex for the first, then the next slots (DS2 does the same:
        /// Smelter 65101020 carries hits with ID 0 and 1; DS1 c2900 a00_3008 "running slash" stage 1 / 2 = judges 180 / 181).</summary>
        public int SlotOf(int judge) => HitIndex + Math.Max(0, Timing.Judges.IndexOf(judge));
        public string Name = "";
        public byte[] Nsa = [];
        public NsaAnim Back = null!;
        public ClipTiming Timing = null!;
        public List<string> TrackLog = [];
    }

    public sealed class CategoryOut
    {
        public float Value; public int Sm; public string What = "";
        public List<(int Leaf, int Clip)> Stages = [];
    }

    public sealed class Result
    {
        public byte[] Nmb = [];
        public Dictionary<int, ClipOut> Clips = [];
        /// <summary>Session 11 (burgchr11, rule CH5): further TimeAct animations of a clip that serves another damage slot (a clip
        /// that is stage 4 of one chain and a single attack, or stage 2 of one chain and stage 3 of another): same clip, NSA and
        /// DS1 timing, own TAE id (<see cref="AltTaeId"/>) whose 2200 / 2300 events carry that slot.</summary>
        public List<ClipOut> Extra = [];
        /// <summary>Every TimeAct animation of the attack layer: the primary ones and the CH5 variants.</summary>
        public IEnumerable<ClipOut> AllClips => Clips.Values.Concat(Extra);
        /// <summary>The variant of <paramref name="clip"/> whose hits use damage slot <paramref name="slot"/>.</summary>
        public ClipOut Variant(int clip, int slot) => Clips[clip].HitIndex == slot ? Clips[clip] : Extra.FirstOrDefault(c => c.Clip == clip && c.HitIndex == slot) ?? Clips[clip];
        /// <summary>The variant a plain action (DamageId00-02 / BulletId00-02) plays: the primary when its slot fits, else slot 0.</summary>
        public ClipOut ForAction(int id) => Clips[ClipOf[id]].HitIndex <= 2 ? Clips[ClipOf[id]] : Variant(ClipOf[id], 0);
        /// <summary>The variant combo stage <paramref name="s"/> of <paramref name="ids"/> plays (a repeated id keeps its first stage's slot).</summary>
        public ClipOut ForStage(int[] ids, int s) => Variant(ClipOf[ids[s]], Array.IndexOf(ids, ids[s]));
        /// <summary>DS1 attack id → the DS1 clip it plays (<see cref="Plan.Clip(int, Ds1ChrSource)"/>).</summary>
        public Dictionary<int, int> ClipOf = [];
        public List<CategoryOut> Categories = [];
        public List<(int Action, int Id)> Actions = [];          // plain action n → DS1 attack id (incl. aliases)
        public List<(int Row, int[] Ids)> Combos = [];           // combo row k → DS1 ids
        public List<(long Id, int Frames, List<Ds2TaeFile.Event> Events)> TaeAnims = [];
        public List<string> Report = [];
    }

    // ------------------------------------------------------------------ network helpers
    sealed record Trans(int Node, int Src, int DstState, int DstNode, List<(int Type, string Cp, float Lo, float Hi)> Conds);

    static List<Trans> Transitions(NmbNetClone ed, int sm)
    {
        var net = ed.View(); var x = ed.X; var names = NmbNetEdit.NodeNames(x);
        var def = ed.Sm(sm);
        var o = new List<Trans>();
        foreach (var st in def.States.Where(s => net.GetNode(s.Node).Type is 400 or 402))
        {
            var conds = st.Entry.Select(ci =>
            {
                var c = def.Conds[ci]; int t = BitConverter.ToUInt16(c, 0);
                string cp = t is 627 or 625 or 623 or 624 or 606 or 608 && names.TryGetValue(BitConverter.ToUInt16(c, 0x48), out var nm) ? nm.Replace("ControlParameters|", "") : "";
                float lo = t == 627 ? BitConverter.ToSingle(c, 0x4C) : t is 625 ? BitConverter.ToInt32(c, 0x4C) : t is 623 or 624 ? BitConverter.ToInt32(c, 0x4C) : 0;
                float hi = t == 627 ? BitConverter.ToSingle(c, 0x50) : t is 625 ? BitConverter.ToInt32(c, 0x50) : lo;
                return (t, cp, lo, hi);
            }).ToList();
            int dst = st.Dst != 0xFFFF && st.Dst != 0xCDCD && st.Dst < def.States.Count ? def.States[st.Dst].Node : -1;
            o.Add(new Trans(st.Node, st.Src, st.Dst, dst, conds));
        }
        return o;
    }

    static int StateCount(NmbNetClone ed, int sm)
    {
        var net = ed.View();
        return ed.Sm(sm).States.Count(s => net.GetNode(s.Node).Type is not (400 or 402));
    }

    /// <summary>Stage states of a category SM in play order: the default state, then each state the route transition of the
    /// previous stage leads to (Combo{s}_Route condition; DS2's stage pattern, NETWORK-FORMAT.md §3.2). A single-stage SM
    /// gives its one state.</summary>
    static List<int> Stages(NmbNetClone ed, int sm)
    {
        var def = ed.Sm(sm);
        var trans = Transitions(ed, sm);
        var o = new List<int> { def.States[def.Default].Node };
        int cur = def.Default;
        for (int s = 1; s < 8; s++)
        {
            var t = trans.FirstOrDefault(x => x.Src == cur && x.Conds.Any(c => c.Cp == $"Combo{s}_Route"));
            if (t == null) break;
            o.Add(t.DstNode); cur = t.DstState;
        }
        return o;
    }

    /// <summary>A category SM made of route stages only (every state is a route stage: no blade dash, grab or shot branch,
    /// whose leaves would keep a template clip).</summary>
    static bool PureChain(NmbNetClone ed, int sm) => Stages(ed, sm).Count == StateCount(ed, sm);

    /// <summary>Anim leaves under a node whose clip is an attack clip (file name a65_*), DFS.</summary>
    static List<int> AttackLeaves(NmbNetClone ed, int root, List<string> clipNames)
    {
        var net = ed.View(); var o = new List<int>(); var seen = new HashSet<int>();
        void Rec(int i)
        {
            if (i == 0xFFFF || !seen.Add(i)) return;
            var n = net.GetNode(i);
            if (n.Type == 104)
            {
                var sa = NmbNetwork.Find(n, 21);
                if (sa != null)
                {
                    int ci = net.ClipIndex(sa);
                    if (ci < clipNames.Count && clipNames[ci].StartsWith("a65_", StringComparison.OrdinalIgnoreCase)) o.Add(i);
                }
                return;
            }
            if (n.Type is 400 or 402) return;
            foreach (int c in n.Children) Rec(c);
        }
        Rec(root);
        return o;
    }

    // ------------------------------------------------------------------ track rules
    static List<(float, float, int)> Win(params (double S, double E, int V)[] w) =>
        w.Where(x => x.E > x.S + 1e-6).Select(x => ((float)x.S, (float)(x.E - x.S), x.V)).ToList();

    /// <summary>The events one attack-leaf track gets for a DS1 clip (null = keep the template's fractions). Fractions of the
    /// DS1 clip. See the class comment for the rules.</summary>
    static List<(float, float, int)>? TrackEvents(NmbEventTrack tr, bool isSync, ClipOut c, int taeId, (List<(float, float, int)> L, List<(float, float, int)> R) feet)
    {
        var t = c.Timing; double d = t.Duration;
        double F(double s) => d > 0 ? Math.Clamp(s / d, 0, 1) : 0;
        double h0 = t.FirstHit >= 0 ? F(t.FirstHit) : 0, h1 = t.LastHit >= 0 ? F(t.LastHit) : h0;
        double rec = t.Recovery >= 0 ? Math.Max(F(t.Recovery), h1) : 1;
        double c0 = t.Continuation.Count > 0 ? Math.Max(F(t.Continuation[0].S), h1) : h1;
        if (c0 > rec) c0 = rec;
        // return to base: DS2 state ends = the character may walk again: DS1's AI Movement Cancel (JT 78; else the earliest cancel), never
        // before DS2's 0.4 s blend tail
        double s80 = Math.Max(t.MoveCancel >= 0 ? Math.Max(F(t.MoveCancel), h1) : rec, d > 0.4 ? (d - 0.4) / d : 0);
        if (isSync) return h0 > 1e-4 && h0 < 1 - 1e-4 ? Win((0, h0, 0), (h0, 1, 0)) : Win((0, 1, 0));
        if (tr.UserData == 10) return feet.L;
        if (tr.UserData == 11) return feet.R;
        if (tr.Name == "TimeAct" || tr.UserData == 1000) return [(0f, 1f, taeId)];
        if (tr.Events.Count == 0) return null;
        var v = tr.Events.Select(e => e.Value).Distinct().ToList();
        double Cancel(double jt) => jt >= 0 ? Math.Max(F(jt), h1) : rec;
        if (v.All(x => x == 80)) return Win((s80, 1, 80));
        if (v.All(x => x is >= 100 and <= 106)) return Win((0, h0, 106), (h0, c0, 100), (c0, rec, 101), (rec, 1, 102));
        if (v.All(x => x == 123)) return t.Hits.Count > 0 ? t.Hits.Select(hh => ((float)F(hh.S), (float)(F(hh.E) - F(hh.S)), 123)).Where(x => x.Item2 > 0).ToList() : [];
        if (v.All(x => x == 120)) return Win((0, rec, 120));
        if (v.All(x => x == 110)) return Win((0, Cancel(t.DodgeCancel), 110));
        if (v.All(x => x == 111)) return Win((0, Cancel(t.MoveCancel), 111));
        if (v.All(x => x == 112)) return Win((0, Cancel(t.AttackCancel), 112));
        if (v.All(x => x is 150 or 151)) return Win((0, h0, 150), (h1, 1, 151));
        if (v.All(x => x == 155)) return Win((0, 1, 155));
        if (v.All(x => x is >= 9500 and <= 9550)) return Win((0, s80, v[0]));
        return null;
    }

    static string Ev(IEnumerable<(float S, float D, int V)> e) => "[" + string.Join(" ", e.Select(x => $"{x.S.ToString("0.###", Inv)}+{x.D.ToString("0.###", Inv)}:{x.V}")) + "]";

    // ------------------------------------------------------------------ TimeAct events
    static float Warp(float tt, double[] kt, double[] kd)
    {
        for (int i = 0; i + 1 < kt.Length; i++)
            if (tt <= kt[i + 1] || i + 2 == kt.Length)
            {
                double span = kt[i + 1] - kt[i];
                double f = span > 1e-6 ? (tt - kt[i]) / span : 0;
                return (float)Math.Clamp(kd[i] + f * (kd[i + 1] - kd[i]), 0, kd[^1]);
            }
        return (float)kd[^1];
    }

    /// <summary>Rule CH5: the TimeAct id of clip <paramref name="clip"/>'s variant for damage slot <paramref name="slot"/>: 64·10⁶ +
    /// (slot+1)·10⁵ + DS1 clip (the primaries are 65·10⁶ + clip; the build stops if the template TAE already has the id).</summary>
    public static int AltTaeId(int clip, int slot) => 64_000_000 + (slot + 1) * 100_000 + clip;

    /// <summary>
    /// Rule LP1 (session 11, track leap12; user report _base39 Firelink: "the one way the undead guy jumps at me, he just flew crazy
    /// far off the side of the cliff"): DS1 TAE JumpTable 51 "Disable Fall Animation" windows → DS2 TAE event 280000 over the same
    /// seconds. DS1 sets JT 51 over the airborne part of its leaps (Hollow Warrior a00_3005-3007, Skeleton a00_3006/3007, Hollow
    /// a00_3004-3006, Capra 3004, Titanite Demon 3007/3008, Hellkite …), where the clip's root motion runs at up to 20-30 m/s. DS2
    /// starts its fall state on an enemy that has been off the ground for the engine's air time (DarkSoulsII.c l.682682-682690 →
    /// chr+0x4c8 bit 12 → CP IsFall, FUN_140362c40), and c1030's / c3050's SM_Main "ANY → fall when IsFall" (T[9]) has no exemption
    /// for SM_Attack, so a leap over a step or an edge turned into a DS2 fall carrying the leap's speed. Event 280000 (group 280000,
    /// handler FUN_14016c150 l.268786) raises the physics counter +0x108 that FUN_14036da20 reads as "on the ground" for its
    /// duration (no air time, no IsFall), as vanilla DS2 does on its own jumping attacks (c509000 65111010 0-2.233 s, 120 uses);
    /// parameters 16 zero bytes as vanilla (byte 0 ≠ 0 would keep the counter raised after the window).
    /// </summary>
    public static IEnumerable<Ds2TaeFile.Event> NoFallEvents(IEnumerable<(double S, double E)> jt51, double duration) =>
        jt51.Select(w => (S: Math.Max(0, w.S), E: Math.Min(duration, w.E))).Where(w => w.E > w.S + 1e-4)
            .Select(w => new Ds2TaeFile.Event(280000, (float)w.S, (float)w.E, new byte[16]));

    static byte[] WithId(byte[] p, int id) { var b = (byte[])p.Clone(); BitConverter.GetBytes(id).CopyTo(b, 0); return b; }
    static byte[] WithF32(byte[] p, float v) { var b = (byte[])p.Clone(); BitConverter.GetBytes(v).CopyTo(b, 0); return b; }

    static List<Ds2TaeFile.Event> TaeEvents(ClipOut c, Ds2TaeFile tae, long templateTaeId, float turnRate, List<string> notes)
    {
        var t = c.Timing; double d = t.Duration;
        var o = new List<Ds2TaeFile.Event>();
        byte[] P(int type, int size) => tae.ParamTemplate(type) ?? new byte[size];
        foreach (var h in t.Hits) o.Add(new(2200, (float)h.S, (float)h.E, WithId(P(2200, 32), c.SlotOf(h.Judge))));
        foreach (var b in t.Bullets) o.Add(new(2300, (float)b.S, (float)b.E, WithId(P(2300, 32), c.BulletIndex)));
        // G9 rule TJ1 (session 11): DS1 JT 8 "Invincible" → DS2 110100 (decompile FUN_140325880 l.622846: raises the chr counters
        // +0x5f0 / +0x608 = action AttackInvincible mode 3; vanilla: 749 uses on get-ups, throw victims, wake-ups, player rolls),
        // DS1 JT 24 "Poise" → DS2 110600 (l.622931: raises +0x5ec alone, which skips the poise / stagger damage at l.223387;
        // vanilla: wake-ups and boss attack windows); no parameters (16 zero bytes as vanilla)
        foreach (var w in t.Invincible.Where(w => w.E > w.S)) o.Add(new(110100, (float)w.S, (float)Math.Min(w.E, d), P(110100, 16)));
        foreach (var w in t.SuperArmor.Where(w => w.E > w.S)) o.Add(new(110600, (float)w.S, (float)Math.Min(w.E, d), P(110600, 16)));
        o.AddRange(NoFallEvents(t.NoFall, d));   // rule LP1 (session 11, track leap12)
        // turn rate: DS1 JT 7 "Disable Turning" windows → 0, DS1 TAE 224 "Set Turn Speed" windows → their rate, else
        // NpcParam.turnVellocity (DS2 280100 "Set Character Turn Rate", f32 deg/s at +0: c103000_pl 1080 / 180 / 120 / 0)
        var cuts = new SortedSet<double> { 0, d };
        foreach (var w in t.NoTurn) { cuts.Add(Math.Clamp(w.S, 0, d)); cuts.Add(Math.Clamp(w.E, 0, d)); }
        foreach (var w in t.TurnSpeed) { cuts.Add(Math.Clamp(w.S, 0, d)); cuts.Add(Math.Clamp(w.E, 0, d)); }
        var cl = cuts.ToList();
        for (int i = 0; i + 1 < cl.Count; i++)
        {
            double mid = (cl[i] + cl[i + 1]) / 2;
            if (cl[i + 1] - cl[i] < 1e-4) continue;
            bool noTurn = t.NoTurn.Any(w => mid >= w.S && mid <= w.E);
            var ts = t.TurnSpeed.Where(w => mid >= w.S && mid <= w.E).OrderBy(w => w.S).LastOrDefault();
            float rate = noTurn ? 0f : ts.E > ts.S ? ts.Rate : turnRate;
            o.Add(new(280100, (float)cl[i], (float)cl[i + 1], WithF32(P(280100, 32), rate)));
        }
        // DS2's unnamed attack-state events, placed relative to the hit window (template clip → DS1 clip)
        var ta = tae.Find(templateTaeId);
        if (ta != null && t.FirstHit >= 0)
        {
            var tev = Ds2TaeFile.Events(ta);
            var th = tev.Where(e => e.Type is 2200 or 2300).ToList();
            double tEnd = (tae.Find(templateTaeId) is { } aa ? FramesOf(aa) : 0) / 30.0;
            if (th.Count > 0 && tEnd > 0)
            {
                double[] kt = [0, th.Min(e => e.Start), th.Max(e => e.End), tEnd];
                double[] kd = [0, t.FirstHit, t.LastHit, d];
                if (kt[1] >= kt[2]) kt[2] = kt[1] + 1e-3;
                foreach (var e in tev.Where(e => e.Type is 120200 or 110000 or 2105))
                    o.Add(new(e.Type, Warp(e.Start, kt, kd), Warp(e.End, kt, kd), e.Params));
            }
            else notes.Add($"{Ds1ChrSource.ClipName(c.Clip)}: template TAE {templateTaeId} has no hit to align 120200/110000 to");
        }
        return o;
    }

    static int FramesOf(Ds2TaeFile.Anim a)
    {
        long pF = BitConverter.ToInt64(a.Record, 24);
        int f = (int)(pF - a.BlobStart);
        return BitConverter.ToInt32(a.Blob, f + 0x1C);
    }

    // ------------------------------------------------------------------ apply
    public static Result Apply(byte[] nmbBytes, Ds1ChrSource ds1, Plan plan, MorphemeSkeleton rig, string chr, Ds2TaeFile templateTae)
    {
        var res = new Result();
        var ed = new NmbNetClone(nmbBytes);
        var clipNames = NmbFileNamesFull.Read(ed.Nmb.Bundles[ed.Nmb.IndexOf(13)].Data).Files;
        int smAttack = ed.NodeByName("SM_Main|SM_Attack");
        var weapon = Transitions(ed, smAttack).FirstOrDefault(t => t.Conds.Any(c => c.Cp == "WeaponCategory_Ene" && c.Lo <= plan.WeaponCategory && c.Hi >= plan.WeaponCategory))
            ?? throw new InvalidDataException($"{chr}: SM_Attack has no weapon category {plan.WeaponCategory}");
        int weaponSm = weapon.DstNode;
        (int Sm, float Lo, float Hi) Band(float lo)
        {
            var b = Transitions(ed, weaponSm).Where(t => t.Conds.Any(c => c.Cp == "AttackCategory_Ene" && c.Lo == lo)).Select(t => (t.DstNode, t.Conds.First(c => c.Cp == "AttackCategory_Ene"))).FirstOrDefault();
            if (b.DstNode == 0) throw new InvalidDataException($"{chr}: weapon category {plan.WeaponCategory} has no AttackCategory_Ene band from {lo}");
            return (b.DstNode, b.Item2.Lo, b.Item2.Hi);
        }
        // template categories: single-stage and route chain, searched over every weapon category's bands
        var allCats = new List<(float Value, int Sm, int Band)>();
        foreach (var wt in Transitions(ed, smAttack).Where(t => t.DstNode > 0 && t.Conds.Any(c => c.Cp == "WeaponCategory_Ene")))
            foreach (var bt in Transitions(ed, wt.DstNode).Where(t => t.DstNode > 0 && t.Conds.Any(c => c.Cp == "AttackCategory_Ene" && c.Lo != c.Hi)))
                foreach (var (v, node, _) in ed.Categories(bt.DstNode)) allCats.Add((v, node, bt.DstNode));
        bool IsChain(int sm) => Transitions(ed, sm).Any(t => t.Conds.Any(c => c.Cp == "Combo1_Route"));
        int Single()
        {
            var own = allCats.Where(c => StateCount(ed, c.Sm) == 1 && AttackLeaves(ed, c.Sm, clipNames).Count == 1 && !IsChain(c.Sm)).ToList();
            return own.OrderBy(c => c.Band == Band(10).Sm ? 0 : 1).ThenBy(c => c.Value).Select(c => c.Sm).FirstOrDefault(-1);
        }
        int tplSingle = Single();
        int maxChain = plan.Chains.Length == 0 ? 0 : plan.Chains.Max(c => c.Ids.Length);
        int tplChain = allCats.Where(c => IsChain(c.Sm) && PureChain(ed, c.Sm) && Stages(ed, c.Sm).Count >= maxChain).OrderBy(c => Stages(ed, c.Sm).Count).ThenBy(c => c.Value).Select(c => c.Sm).FirstOrDefault(-1);
        // session 11 (burgchr11, rule CH4): a DS1 chain longer than every template chain (the Black Knight's 3000→3001→3002→3003/3004,
        // c1030's longest route chain has 3 stages) clones the longest pure chain and adds route stages to the clone (AddStage, up to
        // 4: Combo1-3_Route / ComboAttackRoute1-3)
        if (maxChain > 0 && tplChain < 0)
            tplChain = allCats.Where(c => IsChain(c.Sm) && PureChain(ed, c.Sm)).OrderByDescending(c => Stages(ed, c.Sm).Count).ThenBy(c => c.Value).Select(c => c.Sm).FirstOrDefault(-1);
        if (maxChain > 4) throw new InvalidDataException($"{chr}: a DS1 chain of {maxChain} stages (EnemyComboBehaviorParam has 4 stages: AttackId01-04, routes 1-3)");
        if (tplSingle < 0) throw new InvalidDataException($"{chr}: no single-stage attack category in the template network");
        if (maxChain > 0 && tplChain < 0) throw new InvalidDataException($"{chr}: no route-chained attack category in the template network");
        res.Report.Add($"SM_Attack {smAttack}: weapon category {plan.WeaponCategory} → SM {weaponSm}; template categories: single-stage SM {tplSingle}, route chain SM {tplChain} ({(tplChain >= 0 ? Stages(ed, tplChain).Count : 0)} route stages)");

        // bands: [10,19] → [10,39] network-wide
        int widened = ed.WidenBand("AttackCategory_Ene", 10, 19, 10, 39);
        var bandA = Band(0); var bandB = Band(10);
        // retire the template's categories under this weapon category
        int retired = 0;
        foreach (var band in new[] { bandA.Sm, bandB.Sm })
            foreach (var (v, node, offs) in ed.Categories(band)) { ed.SetCategoryValue(offs, 900 + v); retired++; }
        res.Report.Add($"AttackCategory_Ene [10,19] → [10,39] on {widened} conditions; {retired} template categories of weapon category {plan.WeaponCategory} retired (value 900+k)");

        // ---- DS1 clips: bridge, encode, file-name entries
        var hitIndex = new Dictionary<int, int>();
        var extraSlots = new Dictionary<int, SortedSet<int>>();   // session 11 (burgchr11, rule CH5): clip → further damage slots
        foreach (var ch in plan.Chains)
            for (int s = 0; s < ch.Ids.Length; s++)
            {
                int c = plan.Clip(ch.Ids[s], ds1);
                // a clip repeated inside one chain (DS1 Shoot_Act: 3000 → 3001 → 3001) keeps the slot of its first stage: the combo
                // row's AttackId/BulletId at that slot serves every repeat
                int firstAt = Array.IndexOf(ch.Ids, ch.Ids[s]);
                if (firstAt < s) continue;
                // rule CH5 (was: "needs a second TAE animation; not supported yet"): a clip that is stage h of one chain and stage s of
                // another gets a second TimeAct animation whose hits carry slot s
                if (hitIndex.TryGetValue(c, out int h) && h != s) { if (!extraSlots.TryGetValue(c, out var es)) extraSlots[c] = es = []; es.Add(s); continue; }
                hitIndex[c] = s;
            }
        // CH5: a single attack whose clip is stage 4+ of a chain (slot 3: plain actions have DamageId00-02) plays a slot-0 variant
        foreach (var a in plan.Attacks)
        {
            int c = plan.Clip(a.Id, ds1);
            if (hitIndex.GetValueOrDefault(c) > 2) { if (!extraSlots.TryGetValue(c, out var es)) extraSlots[c] = es = []; es.Add(0); }
        }
        var used = plan.Attacks.Select(a => a.Id).Concat(plan.Chains.SelectMany(c => c.Ids)).Concat(plan.LiveAliases.Select(a => a.Id)).Distinct().Order().ToList();
        var newEntries = new List<(string, string, string, uint)>();
        var order = new List<int>();
        foreach (int id in used)
        {
            int clip = plan.Clip(id, ds1);
            res.ClipOf[id] = clip;
            if (!ds1.Has(clip)) throw new InvalidDataException($"{chr}: DS1 clip {Ds1ChrSource.ClipName(clip)} (attack {id} + offset {plan.AnimOffset}) not in the DS1 anibnd");
            var b = ds1.Bridge(clip).Anim;
            var bytes = NsaEncoder.EncodeFile(b).Build();
            var back = NsaCodec.Decode(NsaFile.Parse(bytes));
            uint crc = NmbNetClone.Crc32Bzip2(bytes);
            string anim = $"a65_{clip / 10000:D2}_{clip % 10000:D4}";
            var co = new ClipOut
            {
                Clip = clip, Name = NmbNetClone.NsaName(anim, "untitled", crc), Nsa = bytes, Back = back,
                TaeId = 65_000_000 + clip / 10000 * 10000 + clip % 10000, HitIndex = hitIndex.GetValueOrDefault(clip), BulletIndex = hitIndex.GetValueOrDefault(clip),
                Timing = Timing(ds1, clip, (back.Frames - 1) / back.Fps),
            };
            if (co.Timing.Judges.Count > 1) res.Report.Add($"NOTE {Ds1ChrSource.ClipName(clip)}: {co.Timing.Judges.Count} distinct DS1 judges ({string.Join(", ", co.Timing.Judges.Select(j => $"{j} → slot {co.SlotOf(j)}"))})");
            res.Clips[clip] = co;
            newEntries.Add((co.Name, $"$(RootDir)\\motion_xmd\\{chr}_{Ds1ChrSource.ClipName(clip)}.xmd", "untitled", crc));
            order.Add(clip);
        }
        int first = ed.AddClips(newEntries);
        for (int k = 0; k < order.Count; k++) res.Clips[order[k]].Index = first + k;
        clipNames = NmbFileNamesFull.Read(ed.Nmb.Bundles[ed.Nmb.IndexOf(13)].Data).Files;
        // rule CH5: the further slot variants (same clip / NSA / timing, own TimeAct id)
        foreach (var (c, slots) in extraSlots.OrderBy(kv => kv.Key))
            foreach (int s in slots)
            {
                var p = res.Clips[c];
                if (p.HitIndex == s) continue;
                res.Extra.Add(new ClipOut { Clip = c, Name = p.Name, Nsa = p.Nsa, Back = p.Back, Index = p.Index, Timing = p.Timing, TaeId = AltTaeId(c, s), HitIndex = s, BulletIndex = s });
                res.Report.Add($"rule CH5: {Ds1ChrSource.ClipName(c)} serves damage slot {p.HitIndex} (TAE {p.TaeId}) and slot {s} (TAE {AltTaeId(c, s)})");
            }

        // ---- categories
        var pose = MorphemePose.From(rig);
        var feetCache = new Dictionary<int, (List<(float, float, int)>, List<(float, float, int)>)>();
        var trackCache = new Dictionary<(long TaeId, string Key), uint>();   // CH5: per TimeAct animation (= per clip for the primaries)
        var tplTaeOf = new Dictionary<int, long>();         // clip → template leaf's TimeAct value (for the TAE warp)
        void Fill(int sm, IReadOnlyList<ClipOut> clips, float value, string what)
        {
            var cat = new CategoryOut { Value = value, Sm = sm, What = what };
            var stages = Stages(ed, sm);
            for (int s = 0; s < stages.Count; s++)
            {
                var leaves = AttackLeaves(ed, stages[s], clipNames);
                if (leaves.Count == 0) throw new InvalidDataException($"{chr}: category {value} stage {s + 1} has no attack leaf");
                var co = clips[Math.Min(s, clips.Count - 1)];   // stages past the chain's end are never entered (route 0): last clip
                int clip = co.Clip;
                foreach (int leaf in leaves)
                {
                    var net = ed.View();
                    var node = net.GetNode(leaf);
                    var sa = NmbNetwork.Find(node, 21)!; var et = NmbNetwork.Find(node, 23);
                    int syncIndex = net.SyncTrackIndex(sa);
                    if (!feetCache.TryGetValue(clip, out var feet))
                    {
                        var fe = Ds1RigAnims.FootEvents(co.Back, pose, false, 0);
                        feetCache[clip] = feet = (fe.L, fe.R);
                    }
                    var map = new Dictionary<uint, uint>();
                    if (et != null)
                    {
                        int disc = 0;
                        foreach (var (g, tid, _) in net.Tracks(et))
                        {
                            bool isSync = g == 0 && disc++ == syncIndex;
                            var tr = ed.Track(tid);
                            if ((tr.Name == "TimeAct" || tr.UserData == 1000) && tr.Events.Count > 0) tplTaeOf.TryAdd(clip, tr.Events[0].Value);
                            string key = $"{g}:{tr.Name}:{tr.UserData}:{(isSync ? "sync" : "")}";
                            if (!trackCache.TryGetValue((co.TaeId, key), out uint nid))
                            {
                                var evs = TrackEvents(tr, isSync, co, co.TaeId, feet) ?? tr.Events.ToList();
                                nid = ed.AddTrack(tid, evs, $"lordran:{chr}:{Ds1ChrSource.ClipName(clip)}{(co == res.Clips[clip] ? "" : $"#s{co.HitIndex}")}:{key}");
                                trackCache[(co.TaeId, key)] = nid;
                                co.TrackLog.Add($"{tr.Name} (ud {tr.UserData}{(isSync ? ", sync" : "")}): {Ev(tr.Events)} → {Ev(evs)}");
                            }
                            map[tid] = nid;
                        }
                    }
                    ed.RetargetLeaf(leaf, co.Index, co.Back, map, syncIndex);
                    cat.Stages.Add((leaf, clip));
                }
            }
            res.Categories.Add(cat);
        }
        foreach (var a in plan.Attacks)
        {
            int n = plan.ActionOf(a.Id);
            var band = n < 10 ? bandA.Sm : bandB.Sm;
            var cr = ed.AddCategory(tplSingle, n, band);
            res.Actions.Add((n, a.Id));
            Fill(cr.NewSm, [res.ForAction(a.Id)], n, $"DS1 {a.Id} {a.Name}");
        }
        for (int k = 0; k < plan.Chains.Length; k++)
        {
            var ch = plan.Chains[k];
            var cr = ed.AddCategory(tplChain, 30 + k, bandB.Sm);
            // rule CH4: route stages added to this clone until it holds the whole DS1 chain
            for (var st = Stages(ed, cr.NewSm); st.Count < ch.Ids.Length; st = Stages(ed, cr.NewSm)) ed.AddStage(cr.NewSm, st);
            Fill(cr.NewSm, ch.Ids.Select((_, s) => res.ForStage(ch.Ids, s)).ToList(), 30 + k, $"DS1 chain {string.Join("→", ch.Ids)}");
            res.Combos.Add((k, ch.Ids));
        }
        foreach (var al in plan.LiveAliases)
        {
            if (res.Actions.Any(x => x.Action == al.Action)) { res.Report.Add($"alias {al.Action} → DS1 {al.Id} skipped: {al.Action} is DS1 attack {3000 + al.Action}"); continue; }
            var band = al.Action < 10 ? bandA.Sm : bandB.Sm;
            var cr = ed.AddCategory(tplSingle, al.Action, band);
            if (!res.ClipOf.ContainsKey(al.Id)) res.ClipOf[al.Id] = plan.Clip(al.Id, ds1);
            Fill(cr.NewSm, [res.ForAction(al.Id)], al.Action, $"interim alias: template AI action {al.Action} → DS1 {al.Id}");
            res.Actions.Add((al.Action, al.Id));
        }
        // ---- chain stage blends (session 11, track enemy11, G1 rule BL1 = the boss's B3d): the template's stage transitions are
        // TransitSyncEvents with c1030's 0 (route 1) / 0.3 (route 2) sync events; DS1 blends the next chain clip in over its own
        // TAE type-16 span, so the duration becomes that time over the length of the source's sync event at the hand-over (the
        // source clip's JT 23 continuation start: [0, first hit) or [first hit, 1]).
        {
            var bl = new List<string>();
            var netB = ed.View();
            for (int k = 0; k < plan.Chains.Length; k++)
            {
                var cat = res.Categories.First(c => c.Value == 30 + k);
                var def = ed.Sm(cat.Sm);
                var stg = Stages(ed, cat.Sm);
                var idx = def.States.Select((st, i) => (st.Node, i)).GroupBy(t => (int)t.Node).ToDictionary(g => g.Key, g => g.First().i);
                var ids = plan.Chains[k].Ids;
                for (int s = 0; s + 1 < stg.Count && s + 1 < ids.Length; s++)
                {
                    var src = res.Clips[plan.Clip(ids[s], ds1)].Timing; int dstClip = plan.Clip(ids[s + 1], ds1);
                    double d1 = src.Duration, h = src.FirstHit >= 0 && d1 > 0 ? Math.Clamp(src.FirstHit / d1, 0, 1) : 0;
                    double cAt = src.Continuation.Count > 0 && d1 > 0 ? src.Continuation[0].S / d1 : 0.8;
                    double syncSec = (h > 0 && cAt >= h ? 1 - h : h > 0 ? h : 1) * d1;
                    double blendSec = Ds1NetRules.BlendSpan(ds1.TaeAnim(dstClip));
                    if (blendSec <= 0 || syncSec <= 0) continue;
                    foreach (var tr in def.States.Where(t => t.Src == idx[stg[s]] && t.Dst == idx[stg[s + 1]]))
                    {
                        var a59 = NmbNetwork.Find(netB.GetNode(tr.Node), 59);
                        if (a59 == null) continue;
                        float old = netB.F32(a59.Offset + 0x10), dur = (float)(blendSec / syncSec);
                        netB.PutF32(a59.Offset + 0x10, dur);
                        bl.Add($"{ids[s]}→{ids[s + 1]} {tr.Node}: {old.ToString("0.###", Inv)} → {dur.ToString("0.###", Inv)} ({blendSec:0.###} s / {syncSec:0.###} s)");
                    }
                }
            }
            res.Report.Add($"G1 BL1 chain stage blends ({bl.Count}): {string.Join("; ", bl)}");
        }
        // ---- TimeAct animations
        var notes = new List<string>();
        foreach (var co in res.Clips.Values.OrderBy(c => c.Clip).Concat(res.Extra))   // rule CH5: the slot variants after the primaries
        {
            long tpl = tplTaeOf.TryGetValue(co.Clip, out long tv) ? tv : 0;
            if (co.TaeId != res.Clips[co.Clip].TaeId && templateTae.Find(co.TaeId) != null) throw new InvalidDataException($"{chr}: rule CH5 TimeAct id {co.TaeId} is the template's own");
            res.TaeAnims.Add((co.TaeId, (int)Math.Round(co.Timing.Duration * 30), TaeEvents(co, templateTae, tpl, plan.TurnRate, notes)));
        }
        res.Report.AddRange(notes);
        // session 9 (track enemyfix): what each clone copied instead of sharing (NmbNetClone, nmb-runtime-check rule D1)
        var copied = ed.Log.Where(l => l.Contains(" copied ")).ToList();
        if (copied.Count > 0) res.Report.Add($"own additive layers (rule D1, no node shared with an inactive owner): {copied.Count} of {res.Categories.Count} categories copied the template's band-owned nodes; e.g. {copied[0]}");
        res.Nmb = ed.Build();
        res.Report.Add($"DS1 attacks: {res.Clips.Count} clips ({string.Join(", ", res.Clips.Values.OrderBy(c => c.Clip).Select(c => $"{Ds1ChrSource.ClipName(c.Clip)} hit slot {c.HitIndex}"))}), "
            + $"{res.Categories.Count} categories ({string.Join("; ", res.Categories.Select(c => $"{c.Value}: {c.What}"))})");
        return res;
    }

}
