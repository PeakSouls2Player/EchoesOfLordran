using System.Buffers.Binary;
using System.Globalization;
using Lordran.Tools.Formats;
using Lordran.Tools.Formats.Morpheme;
using SoulsFormats;

namespace Lordran.Tools.Commands;

/// <summary>
/// <c>ds1net-check PACKDIR</c> (session 11, track enemy11): offline asserts for FIX-PLAN groups G1 / G2 / G3 on a built pack.
/// <list type="bullet">
/// <item>G1 (every 1:1 network = an anibnd whose network has DS1 attack TAE ids 65xxxxxx or the c1030 layout): HeadLook (122)
/// weight and HipsIK (129) weight inputs come from PlaySpeed_0; no input reads UnevenTerrain_BlendWeight; every FeatherBlend2
/// (114) has one alpha on all bones (0 or 1); no PlaySpeed node over a clip reads a DS2 rate CP (PlaySpeed_1_5/2/3/5/10,
/// DmgDuration, DmgHitBackDuration, GuardDuration, Attack_SwingSpeed*); no attack chain stage transition (category SMs
/// 30+) is a 0-event snap.</item>
/// <item>G2 (EnemyBehavior / Second / Third and EnemyComboBehaviorParam rows 220000-299999 = the 1:1 rows): active slots have
/// Back/FollowSwingSpeed 1 and EstimateFutureTime 0; every combo stage with a BulletId has the AttackId of the same slot.</item>
/// <item>G3 (EnemyBulletParam rows 3xxxxxxxx = the 1:1 bullets): printed for review with the DS1 source values by the build
/// report (the dummy and ballistics asserts live in ds1rig-check / the build log).</item>
/// </list>
/// Exit 1 on any problem.
/// </summary>
public static class Ds1NetCheck
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static ushort U16(byte[] b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o));

    /// <summary><c>tae-groups FILE.tae TYPE...</c>: the event-group type each given event type sits in (session 11).</summary>
    public static int Groups(string[] args)
    {
        var b = File.ReadAllBytes(args[1]);
        var want = args.Skip(2).Select(int.Parse).ToHashSet();
        var tae = new Ds2Tae(b);
        long L(int o) => BitConverter.ToInt64(b, o);
        int I(int o) => BitConverter.ToInt32(b, o);
        var seen = new SortedSet<string>();
        foreach (var (anim, r) in tae.Records)
        {
            long pGr = L(r + 8); if (pGr == 0) continue;
            int ng = I(r + 36);
            for (int g = 0; g < ng; g++)
            {
                int gr = (int)pGr + 32 * g;
                long n = L(gr), pIdx = L(gr + 8), pData = L(gr + 16);
                int gtype = I((int)pData);
                for (int k = 0; k < n; k++)
                {
                    int h = (int)L((int)pIdx + 8 * k); int d = (int)L(h + 16); int et = I(d);
                    if (want.Count == 0 || want.Contains(et)) seen.Add($"event {et} in group {gtype}");
                }
            }
        }
        foreach (var s in seen) Console.WriteLine(s);
        return 0;
    }

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: ds1net-check PACKDIR"); return 1; }
        string pack = args[1];
        var problems = new List<string>();
        int nets = 0;
        string[] rated = ["PlaySpeed_1_5", "PlaySpeed_2", "PlaySpeed_3", "PlaySpeed_5", "PlaySpeed_10", "DmgDuration", "DmgHitBackDuration", "GuardDuration", "Attack_SwingSpeed", "Attack_SwingSpeed2", "Attack_SwingSpeed3", "Attack_SwingSpeed4"];
        foreach (var f in Directory.GetFiles(Path.Combine(pack, "gamedata", "morpheme4", "chr"), "c2*.anibnd.dcx").Order())
        {
            string chr = Path.GetFileName(f)[..5];
            var nmbF = MorphemeTools.LoadFiles(f).First(x => x.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase));
            var nmb = NmbFile.Read(nmbF.Bytes);
            var x = nmb.Bundles[nmb.IndexOf(10)].Data;
            var net = new NmbNetwork(x);
            var names = NmbNetEdit.NodeNames(x);
            string Name(int n) => names.TryGetValue(n, out var s) ? s.Replace("ControlParameters|", "") : $"node{n}";
            var files = NmbFileNamesFull.Read(nmb.Bundles[nmb.IndexOf(13)].Data).Files;
            nets++;
            int p0 = problems.Count, alphas = 0, stages = 0;
            var stageDur = new List<float>();
            List<(int Src, int Pin)> Ins(NmbNetwork.Node n)
            {
                int p = n.Offset; int ncp = x[p + 0x0E]; long pCp = (long)net.U64(p + 0x28);
                return Enumerable.Range(0, pCp == 0 ? 0 : ncp).Select(k => ((int)U16(x, p + (int)pCp + 4 * k), (int)U16(x, p + (int)pCp + 4 * k + 2))).ToList();
            }
            bool HasClip(int root)
            {
                var seen = new HashSet<int>();
                bool Rec(int i)
                {
                    if (i == 0xFFFF || !seen.Add(i)) return false;
                    var n = net.GetNode(i);
                    if (n.Type == 104) return NmbNetwork.Find(n, 21) is { } sa && !files[net.ClipIndex(sa)].StartsWith("a00_50_");
                    if (n.Type is 400 or 402) return false;
                    return n.Children.Any(c => Rec(c));
                }
                return Rec(root);
            }
            foreach (var n in net.Nodes())
            {
                var ins = Ins(n);
                if (n.Type == 122 && (ins.Count < 2 || Name(ins[1].Src) != "PlaySpeed_0")) problems.Add($"{chr} G1 NECK: HeadLook {n.Index} weight from {(ins.Count > 1 ? Name(ins[1].Src) : "-")}");
                if (n.Type == 129 && ins.Count > 0 && Name(ins[^1].Src) != "PlaySpeed_0") problems.Add($"{chr} G1 HIPS: HipsIK {n.Index} weight from {Name(ins[^1].Src)}");
                foreach (var i in ins) if (i.Src != 0xFFFF && Name(i.Src) == "UnevenTerrain_BlendWeight") problems.Add($"{chr} G1 TERRAIN: node {n.Index} reads UnevenTerrain_BlendWeight");
                if (n.Type == 114 && NmbNetwork.Find(n, 87) is { } a87)
                {
                    int cnt = U16(x, a87.Offset + 0x12); long pp = (long)net.U64(a87.Offset + 0x18);
                    var v = Enumerable.Range(0, cnt).Select(k => net.F32(a87.Offset + (int)pp + 4 * k)).Distinct().ToList();
                    if (v.Count != 1 || (v[0] != 0f && v[0] != 1f)) problems.Add($"{chr} G1 PB1: FeatherBlend2 {n.Index} mixes children per bone (alphas {string.Join("/", v.Take(4).Select(a => a.ToString("0.##", Inv)))})");
                    else alphas++;
                }
                if (n.Type == 125 && ins.Count > 0 && ins[0].Src != 0xFFFF && rated.Contains(Name(ins[0].Src)) && n.Children.Any(c => c != 0xFFFF && HasClip(c)))
                    problems.Add($"{chr} G1 LM1: PlaySpeed {n.Index} over a DS1 clip runs at {Name(ins[0].Src)}");
            }
            // chain stages: category SMs 30+ under SM_Attack: transitions between two stage states
            int atk = names.FirstOrDefault(kv => kv.Value == "SM_Main|SM_Attack", new(-1, "")).Key;
            if (atk >= 0)
                foreach (var n in net.Nodes().Where(n => n.Type == 10))
                {
                    var a38 = NmbNetwork.Find(n, 38)!;
                    var def = NmbNetEdit.ReadSmDef(x, a38.Offset);
                    foreach (var s in def.States.Where(s => net.GetNode(s.Node).Type == 402 && s.Src != 0xFFFF && s.Src < def.States.Count && s.Dst < def.States.Count))
                    {
                        // a stage transition: both ends are attack-clip states (their leaves play a65_ clips) and the condition tests a Combo route
                        bool route = s.Entry.Any(ci => ci < def.Conds.Count && U16(def.Conds[ci], 0) is 623 or 624 or 625 && Name(U16(def.Conds[ci], 0x48)).StartsWith("Combo"));
                        if (!route) continue;
                        var tn = net.GetNode(s.Node);
                        float d = net.F32(NmbNetwork.Find(tn, 59)!.Offset + 0x10);
                        if (n.Index < 2082) continue;   // template categories (retired, values 900+)
                        // stages past a chain's end replay its last clip and are never entered (the combo row's route there is 0)
                        int ClipOf(int st) { var l = new Stack<int>([def.States[st].Node]); var seen = new HashSet<int>(); while (l.Count > 0) { int i = l.Pop(); if (i == 0xFFFF || !seen.Add(i)) continue; var nn = net.GetNode(i); if (nn.Type == 104 && NmbNetwork.Find(nn, 21) is { } sa && files[net.ClipIndex(sa)].StartsWith("a65_")) return net.ClipIndex(sa); if (nn.Type is not (400 or 402)) foreach (int c in nn.Children) l.Push(c); } return -1; }
                        if (d <= 0 && ClipOf(s.Src) == ClipOf(s.Dst)) continue;
                        stages++; stageDur.Add(d);
                        if (d <= 0) problems.Add($"{chr} G1 BL1: chain stage transition {s.Node} in SM {n.Index} is a 0-event snap");
                    }
                }
            Console.WriteLine($"{chr}: {alphas} FeatherBlend2 single-child, {stages} chain stage transitions (sync events {string.Join(" ", stageDur.Distinct().Select(v => v.ToString("0.###", Inv)))}), {problems.Count - p0} problem(s)");
        }
        // ---- G2 params
        var store = ParamStore.LoadDs2(Path.Combine(pack, "title"), ExtractDs2.DefaultOut);
        // the 1:1 behaviour ids: EnemyEquipWeaponParam rows EnemyParamId x 100 whose behaviorId is that EnemyParam id (Ds1AttackParams.Write)
        var vanilla = ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut).Params["EnemyComboBehaviorParam"].Rows.Select(r => r.ID).ToHashSet();
        var ours = store.Params["EnemyEquipWeaponParam"].Rows.Where(r => r.ID is >= 22000000 and < 30000000 && Convert.ToInt32(r["behaviorId"]!.Value) * 100 == r.ID && !vanilla.Contains(r.ID / 100)).Select(r => r.ID / 100).ToHashSet();
        int rows = 0;
        foreach (var pn in new[] { "EnemyBehaviorParam", "EnemyBehaviorSecondParam", "EnemyBehaviorThirdParam" })
            foreach (var r in store.Params[pn].Rows.Where(r => r.ID is >= 220000 and < 300000 && ours.Contains(r.ID)))
            {
                rows++;
                for (int k = 0; k < 10; k++)
                {
                    if (Convert.ToInt32(r[$"action{k}BladeCtrlType"]!.Value) == 4) continue;
                    float b = Convert.ToSingle(r[$"fAction{k}AttackBackSwingSpeed"]!.Value), fo = Convert.ToSingle(r[$"fAction{k}AttackFollowSwingSpeed"]!.Value), e = Convert.ToSingle(r[$"fAction{k}AttackEstimateFutureTime"]!.Value);
                    if (b != 1 || fo != 1 || e != 0) problems.Add($"G2 {pn} {r.ID} action {k}: swing {b}/{fo}, estimate {e} (DS1: 1/1, 0)");
                }
            }
        foreach (var r in store.Params["EnemyComboBehaviorParam"].Rows.Where(r => r.ID is >= 220000 and < 300000 && ours.Contains(r.ID)))
        {
            rows++;
            for (int k = 0; k < 7; k++)
            {
                if (Convert.ToInt32(r[$"combo{k}BladeCtrlType"]!.Value) == 4) continue;
                for (int s = 1; s <= 4; s++)
                {
                    float b = Convert.ToSingle(r[$"fCombo{k}AttackBackSwingSpeed0{s}"]!.Value), fo = Convert.ToSingle(r[$"fCombo{k}AttackFollowSwingSpeed0{s}"]!.Value), e = Convert.ToSingle(r[$"fCombo{k}EstimateFutureTime0{s}"]!.Value);
                    if (r.ID != 223210 && (b != 1 || fo != 1 || e != 0)) problems.Add($"G2 combo {r.ID} {k} slot {s}: swing {b}/{fo}, estimate {e}");
                    int bul = Convert.ToInt32(r[$"combo{k}BulletId0{s}"]!.Value), at = Convert.ToInt32(r[$"combo{k}AttackId0{s}"]!.Value);
                    if (bul > 0 && at <= 0) problems.Add($"G2 TB1 combo {r.ID} {k} slot {s}: BulletId {bul} without AttackId (the shot does no damage)");
                }
            }
        }
        // ---- G3 bullets: the 1:1 rows (rule E-ID1 block 134xxxxxx since session 12; 3xxxxxxxx before; not vanilla): spawn dummy = DS1's (not the DS2 template's 115 / 2), no lead,
        // no spread; the bullet's own damage row exists (the engine fires nothing without one)
        var vb = ParamStore.LoadDs2(Program.DefaultDs2Root, ExtractDs2.DefaultOut).Params["EnemyBulletParam"].Rows.Select(r => r.ID).ToHashSet();
        var dmgRows = store.Params["EnemyDamageParam"];
        foreach (var r in store.Params["EnemyBulletParam"].Rows.Where(r => Build.Chr.Ds1AttackParams.IsEnemyRow(r.ID) && !vb.Contains(r.ID)))
        {
            double V(string f) => Convert.ToDouble(r[f]!.Value);
            var dr = dmgRows.Rows.FirstOrDefault(d => d.ID == r.ID);
            string dum = dr == null ? "no damage row" : $"dummy type {dr["hitDummyPolyType"]!.Value} id {dr["hitDummyPolyId"]!.Value} radius {dr["radius"]!.Value} expand {dr["expandSize"]?.Value}/{dr["expandTime"]?.Value}";
            Console.WriteLine($"G3 bullet {r.ID}: emitteType {V("emitteType")}, v0 {V("initialVelocity")} accel {V("acceleration")} max {V("maxSpeed")}, pitch {V("shootAngle") * 180 / Math.PI:0.#}°, correct UD {V("upDownCorrectAngle") * 180 / Math.PI:0.#}° LR {V("rightleftCorrectAngle") * 180 / Math.PI:0.#}°, homing {V("homingAngle") * 180 / Math.PI:0.#}°/s, lead {V("upDownEstimateFuture")}/{V("rightLeftEstimateFuture")}, spread {V("upDownAccuracy")}/{V("rightLeftAccuracy")}, gravity {V("gravityValue")} from {V("downStartTime"):0.##} s; {dum}");
            if (V("upDownEstimateFuture") != 0 || V("rightLeftEstimateFuture") != 0) problems.Add($"G3 bullet {r.ID}: target lead on");
            if (V("upDownAccuracy") != 0 || V("rightLeftAccuracy") != 0) problems.Add($"G3 bullet {r.ID}: random spread on");
            if (dr == null) problems.Add($"G3 bullet {r.ID}: no EnemyDamageParam row of its own (the engine fires nothing)");
            else if (Convert.ToInt32(dr["hitDummyPolyType"]!.Value) == 0 && Convert.ToInt32(dr["hitDummyPolyId"]!.Value) is 115 or 2 && r.ID % 10 == 0
                && !Ds1BulletDummyIs(r.ID, Convert.ToInt32(dr["hitDummyPolyId"]!.Value))) problems.Add($"G3 bullet {r.ID}: spawn dummy is the DS2 template's ({dr["hitDummyPolyId"]!.Value})");
        }
        Console.WriteLine($"{nets} networks, {rows} 1:1 behaviour rows checked: {problems.Count} problem(s)");
        foreach (var p in problems.Take(200)) Console.WriteLine("  " + p);
        Console.WriteLine($"ds1net-check: {(problems.Count == 0 ? "all checks passed" : $"{problems.Count} problem(s)")}");
        return problems.Count == 0 ? 0 : 1;
    }

    /// <summary>Session 11 (fx11): the DS1 bullet behaviour of a 1:1 bullet row (id = 100,000,000 + BehaviorParam 2_VVVVV_JJJ) really
    /// spawns at <paramref name="dummy"/> in DS1 (the Stray Demon's 22300 leaves its dummy 2, which equals a DS2 template's): a TAE
    /// type-2 event with that judge and dummy in the anibnd of a DS1 character whose NpcParam uses the variation.</summary>
    static bool Ds1BulletDummyIs(int bulletRow, int dummy)
    {
        // session 12, rule E-ID1: the row id encodes (variation, judge) through Ds1AttackParams.EnemyRow
        if (Build.Chr.Ds1AttackParams.FromEnemyRow(Build.Ds1Source.LoadParam(Program.DefaultDsrRoot, "BehaviorParam")!, bulletRow) is not { } q) return false;
        int variation = q.Variation, judge = q.Judge;
        var npc = Build.Ds1Source.LoadParam(Program.DefaultDsrRoot, "NpcParam");
        foreach (var chr in npc?.Rows.Where(r => Convert.ToInt32(r["behaviorVariationId"]!.Value) == variation).Select(r => $"c{r.ID / 100:D4}").Distinct() ?? [])
        {
            string path = Path.Combine(Program.DefaultDsrRoot, "chr", chr + ".anibnd.dcx");
            if (!File.Exists(path)) continue;
            var tae = Build.Chr.Ds1ChrSource.Load(Program.DefaultDsrRoot, chr).Tae;
            if (tae?.Animations.SelectMany(a => a.Events).Any(e => e.Type == 2 && e.GetParameterBytes(false) is { Length: >= 12 } p && BitConverter.ToInt32(p, 8) == judge && BitConverter.ToInt32(p, 0) == dummy) == true) return true;
        }
        return false;
    }
}
