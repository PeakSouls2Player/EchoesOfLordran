using System.Buffers.Binary;
using System.Globalization;
using Lordran.Tools.Formats.Morpheme;
using SoulsFormats;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// Session 11 (track enemy11, FIX-PLAN group G1): the post-pass that takes DS2's procedural and layered motion off a 1:1 DS1
/// network. DS1 plays exactly one clip per state on the whole body, at 1x, blended in over the clip's own TAE type-16 "Blend"
/// span; it has no head look, no hips / uneven-terrain IK and no partial-body reaction layer. Every edit is in place and of
/// fixed size (a float, a u16 CP-connection source, a per-bone alpha, a condition bound, a transition destination), so node
/// ids, blocks and tables stay as the template's (nmb-runtime-check rules unaffected; it is re-run on the pack).
/// <list type="bullet">
/// <item><b>NECK</b>: every HeadLook node (type 122) takes its blend-weight input (CP pin 1) from the constant CP
/// PlaySpeed_0 (default 0, never written by the engine: the decompile names only LookAtWeight / DmgDuration / ... as
/// engine-written CPs) - a head look at weight 0 passes its input pose through. DS1: NpcParam isEnableNeckTurn 0, no look-at
/// in any regular's or the demon's data.</item>
/// <item><b>HIPS / TERRAIN</b>: every HipsIK node (type 129; its last input is the weight: MoveHit_Weight, BlendWeight_1,
/// LandingRate) and every input fed by UnevenTerrain_BlendWeight (types 138 / 500) take PlaySpeed_0 - no procedural hip push
/// on hits, no slope tilt, no foot lifting. DS1 has none of them.</item>
/// <item><b>LM1</b>: a PlaySpeed node (125) over DS1 clips whose rate comes from a constant (PlaySpeed_1_5 / _2 / _3 / _5 / _10)
/// or from a DS2 duration the engine writes (DmgDuration, DmgHitBackDuration, GuardDuration, Attack_SwingSpeed1-4) takes
/// PlaySpeed_1: DS1 plays every clip at 1x. PlaySpeed_0 (a held pose), ladder / banking / roll / pitch speeds (gameplay
/// rates on DS2 layers) and operator outputs stay.</item>
/// <item><b>PB1</b> (template c1030, the 10 regulars): every FeatherBlend2 (type 114) passes ONE child on every bone. The base
/// child (0) on the weapon / guard / head-additive / finger / upper-damage / guard-hit layers; the child that plays a single
/// DS1 clip per direction on the light-hit mix (N1214 → 1193) and on the attack rebound (N1420 → 1382). DS2's DmgCategory 20 /
/// 35 hits (upper-body-only reactions on N2009) reach SM_Main's full-body damage path: its DmgCategory [30,30] entry is widened
/// to [20,35] (DS1: superArmorDurability 0, every hit interrupts with a full-body clip).</item>
/// <item><b>HB1</b> (c1030): the rebound SM's weapon-category-0 route (T1414 → empty state 1383) goes to SM 1385, whose clip slot
/// a00_16_7002 carries DS1 a00_3500 (role "a00_16_70"): a blocked DS1 attack plays DS1's rebound, full body, at 1x.</item>
/// <item><b>BL1</b>: SM_Main's TransitSyncEvents (402, durations in sync events of the clip the blend starts in) into and out of
/// the DS1 attack and damage states last DS1's TAE-16 span: duration = blend seconds ÷ the length of the sync event the
/// transition is measured on - the battle-stand loop's event (WalkStay) for entries from the base state, the DS1 clip's last
/// event (median over the state's clips) for returns to base. Chain stages: <see cref="Ds1Attacks"/> (same rule as the boss's
/// B3d).</item>
/// </list>
/// </summary>
public static class Ds1NetRules
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>What the pass knows about the DS1 clip a leaf plays: DS1 id (-1 = bind pose), seconds, its sync events
    /// (seconds), the DS1 TAE-16 blend-in span (-1 = none).</summary>
    public sealed record ClipFacts(int Ds1Id, double Duration, double[] EventLengths, double Blend, bool Attack = false);

    /// <summary>DS1 TAE type 16 "Blend" span of a clip (seconds), -1 when the clip has none.</summary>
    public static double BlendSpan(TAE.Animation? a)
    {
        var e = a?.Events.FirstOrDefault(x => x.Type == 16);
        return e != null ? e.EndTime - e.StartTime : -1;
    }

    static ushort U16(byte[] b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o));
    static void P16(byte[] b, int o, int v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), (ushort)v);

    /// <summary>CP-connection entries of a node: (offset of the entry, source node, pin).</summary>
    static List<(int Off, int Src, int Pin)> Inputs(NmbNetwork net, NmbNetwork.Node n)
    {
        var x = net.X; int p = n.Offset; int ncp = x[p + 0x0E]; long pCp = (long)net.U64(p + 0x28);
        var o = new List<(int, int, int)>();
        for (int k = 0; k < ncp && pCp != 0; k++) { int e = p + (int)pCp + 4 * k; o.Add((e, U16(x, e), U16(x, e + 2))); }
        return o;
    }

    static double Median(IEnumerable<double> v)
    {
        var l = v.Where(d => d > 0 && double.IsFinite(d)).Order().ToList();
        return l.Count == 0 ? -1 : l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2;
    }

    /// <summary>Anim leaves (type 104) under a node, not crossing into transitions.</summary>
    static List<NmbNetwork.Node> Leaves(NmbNetwork net, int root)
    {
        var o = new List<NmbNetwork.Node>(); var seen = new HashSet<int>();
        void Rec(int i)
        {
            if (i == 0xFFFF || !seen.Add(i)) return;
            var n = net.GetNode(i);
            if (n.Type == 104) { o.Add(n); return; }
            if (n.Type is 400 or 402) return;
            foreach (int c in n.Children) Rec(c);
        }
        Rec(root);
        return o;
    }

    public sealed class Options
    {
        /// <summary>The network's template: "c1030" (the regulars; PB1 / HB1 tables apply) or another (generic rules only).</summary>
        public string Template = "c1030";
        /// <summary>Sync-event length (s) of the battle-stand loop (c1030 slot a00_01_0010 WalkStay): the event attack / damage
        /// entries from the base state are measured on; ≤ 0 = skip BL1 entries.</summary>
        public double StandEvent = -1;
        /// <summary>DS1 TAE-16 span of the idle the base state returns to (a00_0000).</summary>
        public double IdleBlend = -1;
    }

    /// <summary>Apply the rules to a network bundle in place. <paramref name="facts"/>: clip index → what plays (null = not a
    /// DS1 slot). Returns the report lines.</summary>
    public static List<string> Apply(NmbNetwork net, Func<int, ClipFacts?> facts, Options opt)
    {
        var rep = new List<string>();
        var x = net.X;
        var names = NmbNetEdit.NodeNames(x);
        int Cp(string n) => names.FirstOrDefault(kv => kv.Value == "ControlParameters|" + n, new(-1, "")).Key;
        int zero = Cp("PlaySpeed_0"), one = Cp("PlaySpeed_1");
        float Default(int cp) { var a = NmbNetwork.Find(net.GetNode(cp), 3); return a != null ? net.F32(a.Offset + 0x10) : float.NaN; }
        if (zero < 0 || Default(zero) != 0f) throw new InvalidDataException("Ds1NetRules: no constant CP PlaySpeed_0 = 0 in the network");
        if (one < 0 || Default(one) != 1f) throw new InvalidDataException("Ds1NetRules: no constant CP PlaySpeed_1 = 1 in the network");
        string Name(int n) => names.TryGetValue(n, out var s) ? s.Replace("ControlParameters|", "") : $"node{n}";
        bool HasDs1(int root) => Leaves(net, root).Any(l => NmbNetwork.Find(l, 21) is { } sa && facts(net.ClipIndex(sa)) is { Ds1Id: >= 0 });

        // ---- NECK / HIPS / TERRAIN
        int neck = 0, hips = 0, terrain = 0;
        foreach (var n in net.Nodes())
        {
            var ins = Inputs(net, n);
            if (n.Type == 122 && ins.Count >= 2 && ins[1].Src != zero) { P16(x, ins[1].Off, zero); P16(x, ins[1].Off + 2, 0); neck++; }
            if (n.Type == 129 && ins.Count >= 1 && ins[^1].Src != zero && ins[^1].Src != 0xFFFF) { P16(x, ins[^1].Off, zero); P16(x, ins[^1].Off + 2, 0); hips++; }
            foreach (var i in ins)
                if (i.Src != 0xFFFF && Name(i.Src) == "UnevenTerrain_BlendWeight") { P16(x, i.Off, zero); P16(x, i.Off + 2, 0); terrain++; }
        }
        rep.Add($"NECK: {neck} HeadLook weights → PlaySpeed_0 (0); HIPS: {hips} HipsIK weights → 0; TERRAIN: {terrain} UnevenTerrain_BlendWeight inputs → 0");

        // ---- LM1
        var rated = new HashSet<string> { "PlaySpeed_1_5", "PlaySpeed_2", "PlaySpeed_3", "PlaySpeed_5", "PlaySpeed_10", "DmgDuration", "DmgHitBackDuration", "GuardDuration", "Attack_SwingSpeed", "Attack_SwingSpeed2", "Attack_SwingSpeed3", "Attack_SwingSpeed4" };
        var lm = new Dictionary<string, int>();
        foreach (var n in net.Nodes().Where(n => n.Type == 125))
        {
            var ins = Inputs(net, n);
            if (ins.Count == 0 || ins[0].Src == 0xFFFF) continue;
            string cn = Name(ins[0].Src);
            if (!rated.Contains(cn) || !n.Children.Any(c => c != 0xFFFF && HasDs1(c))) continue;
            P16(x, ins[0].Off, one); P16(x, ins[0].Off + 2, 0);
            lm[cn] = lm.GetValueOrDefault(cn) + 1;
        }
        rep.Add($"LM1: PlaySpeed nodes over DS1 clips → PlaySpeed_1: {string.Join(", ", lm.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))}");

        // ---- PB1 (every template): each FeatherBlend2 passes one child on every bone. Child 0 (DS2's base: locomotion under a
        // weapon / guard / head / finger layer, SM_Main under the upper-body damage layer, the full-body damage SM under DamageUpper,
        // the light / heavy guard hit), except where child 1 is the subtree that plays one DS1 clip per state: the light-hit mix
        // whose child 0 is the DmgDownOrUp switch of distance-blended knock-backs (c1030 N1214, c3050 N1064), and the attack
        // rebound SM_HitBack once HB1 routes its weapon-category slot to DS1's rebound clip (c1030).
        {
            var done = new List<string>();
            foreach (var n in net.Nodes().Where(n => n.Type == 114 && n.Children.Length == 2))
            {
                if (NmbNetwork.Find(n, 87) is not { } a) continue;
                var c0 = net.GetNode(n.Children[0]);
                bool lightMix = c0.Type == 131 && Inputs(net, c0).Any(i => i.Src != 0xFFFF && Name(i.Src) == "DmgDownOrUp");
                bool hitBack = opt.Template == "c1030" && n.Index == 1420 && names.GetValueOrDefault(n.Index) == "SM_Main|BT_Damage|DamageFullBody|SM_HitBack";
                float alpha = lightMix || hitBack ? 1f : 0f;
                int cnt = U16(x, a.Offset + 0x12); long pp = (long)net.U64(a.Offset + 0x18);
                for (int k = 0; k < cnt; k++) net.PutF32(a.Offset + (int)pp + 4 * k, alpha);
                done.Add($"{n.Index}→child {n.Children[alpha == 0 ? 0 : 1]}");
            }
            rep.Add($"PB1: {done.Count} FeatherBlend2 pass one child on all bones ({string.Join(", ", done)})");
        }
        {
            if (opt.Template == "c1030")
            {
            var expect = new Dictionary<int, int> { [131] = 707, [609] = 646, [632] = 646, [818] = 604, [1214] = 1193, [1420] = 1382, [1433] = 1428, [1493] = 1494, [2009] = 130, [2063] = 2011 };
            foreach (var (id, keep) in expect)
            {
                var n = net.GetNode(id); var a = NmbNetwork.Find(n, 87);
                if (n.Type != 114 || a == null) throw new InvalidDataException($"PB1: c1030 node {id} is not a FeatherBlend2");
                long pp = (long)net.U64(a.Offset + 0x18);
                int kept = n.Children[net.F32(a.Offset + (int)pp) == 0f ? 0 : 1];
                if (kept != keep) throw new InvalidDataException($"PB1: c1030 FeatherBlend2 {id} keeps {kept}, expected {keep}");
            }
            }
            // DmgCategory 20 / 35 → SM_Main's full-body damage entry
            int smMain = net.GetNode(NodeByName(names, "SM_Main|SM_Attack")).Parent;
            var smN = net.GetNode(smMain);
            var smA = NmbNetwork.Find(smN, 38)!;
            var def = NmbNetEdit.ReadSmDef(x, smA.Offset);
            int widened = 0;
            int ncond = U16(x, smA.Offset + 0x2A); long pC = (long)net.U64(smA.Offset + 0x30);
            for (int k = 0; k < ncond; k++)
            {
                int c = smA.Offset + (int)net.U64(smA.Offset + (int)pC + 8 * k);
                int t = U16(x, c);
                if (t is not (625 or 627) || Name(U16(x, c + 0x48)) != "DmgCategory") continue;
                bool f = t == 627;
                double lo = f ? net.F32(c + 0x4C) : (int)net.U32(c + 0x4C), hi = f ? net.F32(c + 0x50) : (int)net.U32(c + 0x50);
                if (lo != 30 || hi != 30) continue;
                if (f) { net.PutF32(c + 0x4C, 20); net.PutF32(c + 0x50, 35); } else { net.PutU32(c + 0x4C, 20); net.PutU32(c + 0x50, 35); }
                widened++;
            }
            if (widened != 1 && opt.Template == "c1030") throw new InvalidDataException($"PB1: SM_Main {smMain} has {widened} DmgCategory [30,30] conditions (expected 1)");
            rep.Add($"PB1: SM_Main {smMain} full-body damage entry DmgCategory [30,30] → [20,35] on {widened} condition(s) (DS2's upper-body categories 20 / 35 interrupt like DS1's hits)");
        }
        if (opt.Template == "c1030")
        {
            // HB1: rebound SM 1382, weapon category 0 → SM 1385 (a00_16_7002 = DS1 a00_3500)
            var hb = net.GetNode(1382);
            if (hb.Type != 10) throw new InvalidDataException("HB1: node 1382 is not the c1030 rebound SM");
            var hbA = NmbNetwork.Find(hb, 38)!;
            var hdef = NmbNetEdit.ReadSmDef(x, hbA.Offset);
            int i1383 = hdef.States.FindIndex(s => s.Node == 1383), i1385 = hdef.States.FindIndex(s => s.Node == 1385);
            int rec = hdef.States.FindIndex(s => s.Node == 1414);
            if (i1383 < 0 || i1385 < 0 || rec < 0 || hdef.States[rec].Dst != i1383) throw new InvalidDataException("HB1: transition 1414 is not 1413 → 1383 in SM 1382");
            var leaves1385 = Leaves(net, 1385);
            if (!leaves1385.All(l => NmbNetwork.Find(l, 21) is { } sa && facts(net.ClipIndex(sa)) is { Ds1Id: 3500 }))
                rep.Add("HB1 NOTE: SM 1385's leaves do not all play DS1 a00_3500");
            long pSt = (long)net.U64(hbA.Offset + 0x18);
            int recOff = hbA.Offset + (int)pSt + 0x30 * rec;
            P16(x, recOff + 0x12, i1385);
            var t1414 = net.GetNode(1414);
            int pCh = t1414.Offset + (int)net.U64(t1414.Offset + 0x20);
            if (U16(x, pCh + 2) != 1383) throw new InvalidDataException("HB1: transit 1414's destination child is not 1383");
            P16(x, pCh + 2, 1385);
            rep.Add($"HB1: rebound SM 1382 weapon category 0: 1414 → SM 1385 (was the empty state 1383); leaves of 1385 play DS1 {string.Join("/", leaves1385.Select(l => facts(net.ClipIndex(NmbNetwork.Find(l, 21)!))?.Ds1Id ?? -9).Distinct())}");
        }

        // ---- BL1: SM_Main entries / exits of the DS1 attack and damage states
        if (opt.StandEvent > 0 && opt.IdleBlend > 0)
        {
            int atk = NodeByName(names, "SM_Main|SM_Attack");
            int smMain = net.GetNode(atk).Parent;
            var smN = net.GetNode(smMain);
            var smA = NmbNetwork.Find(smN, 38)!;
            var def = NmbNetEdit.ReadSmDef(x, smA.Offset);
            int StateNode(int k) => k < def.States.Count ? def.States[k].Node : -1;
            // the damage state: the SM_Main state whose subtree holds BT_Damage|DamageFullBody
            int dmgFull = names.FirstOrDefault(kv => kv.Value == "SM_Main|BT_Damage|DamageFullBody", new(-1, "")).Key;
            int dmg = -1;
            foreach (var s in def.States.Where(s => net.GetNode(s.Node).Type is not (400 or 402)))
                if (dmgFull >= 0 && Subtree(net, s.Node).Contains(dmgFull)) dmg = s.Node;
            // the base state: the destination of the attack state's return (event 80)
            int bas = def.States.Where(s => net.GetNode(s.Node).Type is 402 && StateNode(s.Src) == atk).Select(s => StateNode(s.Dst)).GroupBy(v => v).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault(-1);
            List<ClipFacts> Facts(int root) => Leaves(net, root).Select(l => NmbNetwork.Find(l, 21) is { } sa ? facts(net.ClipIndex(sa)) : null).Where(f => f is { Ds1Id: >= 0 }).Select(f => f!).ToList();
            var atkF = Facts(atk).Where(f => f.Attack).ToList(); var dmgF = dmg >= 0 ? Facts(dmg) : [];
            double atkBlend = Median(atkF.Select(f => f.Blend)), dmgBlend = Median(dmgF.Select(f => f.Blend));
            double atkLast = Median(atkF.Select(f => f.EventLengths.Length > 0 ? f.EventLengths[^1] : f.Duration));
            double dmgLast = Median(dmgF.Select(f => f.EventLengths.Length > 0 ? f.EventLengths[^1] : f.Duration));
            var notes = new List<string>();
            foreach (var (s, k) in def.States.Select((s, k) => (s, k)))
            {
                var tn = net.GetNode(s.Node);
                if (tn.Type != 402) continue;
                int src = s.Src == 0xFFFF ? -1 : StateNode(s.Src), dst = StateNode(s.Dst);
                double blend, unit; string what;
                if (dst == atk && src != atk) { blend = atkBlend; unit = opt.StandEvent; what = "attack entry"; }
                else if (src == atk && dst == bas) { blend = opt.IdleBlend; unit = atkLast; what = "attack return"; }
                else if (dmg >= 0 && dst == dmg && src != dmg) { blend = dmgBlend; unit = opt.StandEvent; what = "damage entry"; }
                else if (dmg >= 0 && src == dmg && dst == bas) { blend = opt.IdleBlend; unit = dmgLast; what = "damage return"; }
                else continue;
                if (blend <= 0 || unit <= 0) continue;
                var a59 = NmbNetwork.Find(tn, 59)!;
                float old = net.F32(a59.Offset + 0x10), dur = (float)(blend / unit);
                net.PutF32(a59.Offset + 0x10, dur);
                notes.Add($"{what} {s.Node}: {old.ToString("0.###", Inv)} → {dur.ToString("0.###", Inv)}");
            }
            rep.Add($"BL1: SM_Main {smMain} (attack {atk}, damage {dmg}, base {bas}): DS1 blends attack {atkBlend:0.###} s / damage {dmgBlend:0.###} s / idle {opt.IdleBlend:0.###} s on sync events stand {opt.StandEvent:0.###} s, attack last {atkLast:0.###} s, damage last {dmgLast:0.###} s; {notes.Count} transitions: " + string.Join("; ", notes.GroupBy(n => n[..n.IndexOf(' ', n.IndexOf(' ') + 1)]).Select(g => $"{g.Count()}× {g.First()}")));
        }
        return rep;
    }

    static int NodeByName(Dictionary<int, string> names, string n) => names.First(kv => kv.Value == n).Key;

    static HashSet<int> Subtree(NmbNetwork net, int root)
    {
        var seen = new HashSet<int>();
        void Rec(int i)
        {
            if (i == 0xFFFF || !seen.Add(i)) return;
            var n = net.GetNode(i);
            if (n.Type is 400 or 402) return;
            foreach (int c in n.Children) Rec(c);
        }
        Rec(root);
        return seen;
    }
}
