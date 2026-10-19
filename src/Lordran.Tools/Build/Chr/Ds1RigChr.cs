using Lordran.Tools.Formats;
using Lordran.Tools.Formats.Havok;
using SoulsFormats;

namespace Lordran.Tools.Build.Chr;

/// <summary>
/// A 1:1 DS1 character for DS2 (session 8, track chr step 2): DS1's body on DS1's skeleton (<see cref="Ds1RigModel"/>)
/// driven by a DS2 network running DS1's own clips (<see cref="Ds1RigAnims"/>). First user: the Asylum's unarmed Hollow
/// (DS1 c2500, NpcParam 250020) as model c2501 / animId 250100, in place of the interim template port.
/// <para><b>Files</b> (all keyed by the EnemyParam: modelId picks model/chr/cNNNN.bnd + .texbnd, animId picks the anibnd
/// cAAAA (AAAA = animId / 100), the extanibnd, timeact/chr/c{animId}_pl/_sfx/_snd.tae and sound/frpg2_cBBBB.bnd (BBBB = AAAA rounded down to tens, rule S1); run s5-26
/// log_opens, HANDOFF session 5):</para>
/// <list type="bullet">
/// <item>model/chr/c2501.bnd: the template's (c1030) archive layout with DS1's FLVER (twice: .flv and _s.flv, as the interim
/// port), DS1's ragdoll + its hkxpwv, our flvpwv, the template's ACB renamed;</item>
/// <item>model/chr/c2501.texbnd: the template's texture names for c2501 with DS1 pixels (ChrPorter.WriteTexbnd);</item>
/// <item>morpheme4/chr/c2501.anibnd.dcx: c1030's network on the DS1 rig, DS1 clips;</item>
/// <item>morpheme4/chr/c0001/c0001_c2501.extanibnd.dcx: copy of c1030's (the player's own catch animation against this
/// enemy type: player rig, DS2 rules);</item>
/// <item>timeact/chr/c250100_pl|sfx|snd.tae: the template variant's (c103000_*) edited in place; _snd/_sfx carry the DS1
/// clips' own sound / effect events (track chrsound, <see cref="Ds1ChrSound"/>);</item>
/// <item>sound/frpg2_c2500.bnd (track chrsound, rule S1: DS2 names the bank after animId / 100 rounded down to tens, run
/// s8-6 opened frpg2_c2500.bnd for c2501, not the frpg2_c2501.bnd copy of c1030's bank shipped before): DS1's own FMOD
/// project frpg_c2500.fev with the DS1 common sounds its TAE names copied in from frpg_main (id 0, rule S5), renamed to the
/// resource name frpg2_c2500 (rule S1b), and wave bank frpg_c2500.fsb (id 1); the wave banks loose as
/// title/sound/frpg_c2500.fsb + frpg_main.fsb (<see cref="Result.TitleFiles"/>, rules S2/S9). The bank depends only on the
/// DS1 character and the decade, so the 1:1 builds of one DS1 character in one decade share it (rule S10).</item>
/// </list>
/// The model id is the DS1 id + 1 while the interim port of the same DS1 model (c2500: the sword and bow Hollows on c1030's
/// body) still exists; DSR has no c2501..c2508 (chr folder), DS2 no c25xx.
/// </summary>
public static class Ds1RigChr
{
    public sealed class Result
    {
        public Dictionary<string, byte[]> Files = [];
        /// <summary>Loose files for the game folder (pack device "title"): DS1's wave bank, which DS1's FMOD project asks
        /// FMOD for by its own name (track chrsound, rule S2).</summary>
        public Dictionary<string, byte[]> TitleFiles = [];
        /// <summary>Per TAE animation: the DS1 sound / effect events written and the ones not played (track chrsound).</summary>
        public List<string> SoundLog = [];
        public Ds1RigAnims.Result Anims = null!;
        public Ds1RigModel.Result Model = null!;
        public List<string> Report = [];
        /// <summary>DS1's own FLVER (model-space dummies: the hit capsules of the attack params, track enemies).</summary>
        public FLVER2 Ds1Flver = null!;
    }

    /// <summary>Session 11 (ret11): DS1 dummy id → foot ('L'/'R') by its attach bone: "L/R Foot" (DS1's names, or the L_Foot
    /// spelling), or "L/R Calf" for a dummy at ground level (model y &lt; 0.2 m: c2500's footstep dummies 3 / 4 hang on the calves
    /// at y 0.01); dummies attached to one bone only (as Ds1Boss).</summary>
    public static Dictionary<int, char> FootDummies(FLVER2 f) => f.Dummies.Where(d => d.AttachBoneIndex >= 0 && d.AttachBoneIndex < f.Nodes.Count)
        .Select(d => (Id: (int)d.ReferenceID, Bone: f.Nodes[d.AttachBoneIndex].Name.Trim().Replace('_', ' '), d.Position.Y))
        .Where(t => t.Bone is "L Foot" or "R Foot" || t.Bone is "L Calf" or "R Calf" && t.Y < 0.2f).GroupBy(t => t.Id).Where(g => g.Count() == 1)
        .ToDictionary(g => g.Key, g => g.First().Bone[0]);

    /// <summary>Session 11 (ret11), rule MT1: the DS1 model's drawn materials (names not "#NN#") use more than one diffuse texture.</summary>
    public static bool Mt1(FLVER2 f) => f.Materials.Where(m => !m.Name.StartsWith('#'))
        .Select(m => m.Textures.FirstOrDefault(x => x.ParamName == "g_Diffuse" && !string.IsNullOrEmpty(x.Path))?.Path)
        .Where(p => p != null).Select(p => Path.GetFileNameWithoutExtension(p!.Replace('\\', '/')).ToLowerInvariant()).Distinct().Count() > 1;

    /// <summary>Rule MT1: the ACB's texture assets = our texture names (the template's first texture asset as the path pattern),
    /// as Ds1Boss's ACB.</summary>
    static byte[] AcbTextures(byte[] bytes, List<string> textures)
    {
        var acb = ACB.Read(bytes);
        var proto = acb.Assets.OfType<ACB.Asset.Texture>().FirstOrDefault();
        if (proto == null) return bytes;
        int at = acb.Assets.IndexOf(proto);
        acb.Assets.RemoveAll(a => a is ACB.Asset.Texture);
        static string Swap(string p, string tex)
        {
            if (string.IsNullOrEmpty(p)) return p;
            int cut = Math.Max(p.LastIndexOf('\\'), p.LastIndexOf('/')) + 1;
            return p[..cut] + tex + Path.GetExtension(p);
        }
        acb.Assets.InsertRange(Math.Min(at, acb.Assets.Count), textures.Select(tex => (ACB.Asset)new ACB.Asset.Texture { AbsolutePath = Swap(proto.AbsolutePath, tex), RelativePath = Swap(proto.RelativePath, tex) }));
        return acb.Write();
    }

    public static Result Build(string dsrRoot, string ds2Data, ChrPorter.Options o, ChrPorter.OneToOne r)
    {
        var res = new Result();
        string t = o.TemplateChr, n = $"c{r.Model:D4}", animChr = $"c{r.AnimId / 100:D4}";
        // ---- DS1 sources
        var ds1Src = Ds1ChrSource.Load(dsrRoot, o.Ds1Chr);
        var chrbnd = BND3.Read(Path.Combine(dsrRoot, "chr", o.Ds1Chr + ".chrbnd.dcx"));
        byte[] Ds1File(string ext) => chrbnd.Files.First(f => f.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)).Bytes;
        var ds1Flver = FLVER2.Read(Ds1File(".flver"));
        res.Ds1Flver = FLVER2.Read(Ds1File(".flver"));

        // ---- animations first (the rig comes from DS1's Skeleton.hkx)
        string taeT = $"c{r.TemplateAnimId:D6}", taeN = $"c{r.AnimId:D6}";
        string netT = r.TemplateNetwork ?? t;
        var templateTae = Ds2TaeFile.Read(File.ReadAllBytes(Path.Combine(ds2Data, "timeact", "chr", $"{taeT}_pl.tae")));
        var anims = Ds1RigAnims.BuildAnibnd(File.ReadAllBytes(Path.Combine(ds2Data, "morpheme4", "chr", netT + ".anibnd.dcx")), ds1Src, r.Roles, o.Ds1Chr, animChr, r.Attacks, templateTae,
            i1bReserved: Formats.Morpheme.NmbIdentity.For(ds2Data),   // session 10: rule I1b (own Morpheme ids; I1c: one registry per build)
            footDummy: FootDummies(ds1Flver), idleFallback: r.IdleFallback);   // session 11 (ret11): DS1 TAE footfall fallback for loops; rule RF1
        res.Anims = anims;
        res.Report.AddRange(anims.Report);
        res.Files[$"morpheme4/chr/{animChr}.anibnd.dcx"] = anims.Anibnd;
        res.Files[$"morpheme4/chr/c0001/c0001_{animChr}.extanibnd.dcx"] = File.ReadAllBytes(Path.Combine(ds2Data, "morpheme4", "chr", "c0001", $"c0001_{netT}.extanibnd.dcx"));
        int ds1ChrId = int.Parse(o.Ds1Chr[1..], System.Globalization.CultureInfo.InvariantCulture);
        // ---- sound bank (track chrsound, rules S1/S1b/S2/S5/S9/S10): DS1's own FMOD project (+ the DS1 common sounds its TAE
        // names) and wave banks under DS2's bank name; built first so the _snd events can name the copied common sounds.
        // (merge9 stage 4: this replaces the enemies track's copy of the template network's bank as sound/frpg2_{animChr}.bnd,
        // a name DS2 never opens: it opens animId / 100 rounded down to tens, 2501 → frpg2_c2500.)
        int bankChr = Ds1ChrSound.BankChr(r.AnimId);
        // the bank name is shared by the whole decade of animIds: it must not be one of DS2's own banks (it would replace
        // that DS2 character's sounds) nor a name DS2 already uses for a project (rule S10). The variants of one DS1
        // character in one decade share the bank (identical bytes, ChrPorter.AddDs1RigFiles).
        var objections = Ds1ChrSound.BankChecks(ds2Data, bankChr);
        if (objections.Count > 0)
            throw new InvalidDataException($"{o.Ds1Chr} 1:1 animId {r.AnimId}: sound bank {Ds1ChrSound.BankName(bankChr)}.bnd (animId/100 rounded down to tens): {string.Join("; ", objections)}. Choose an animId in a free decade, e.g. {string.Join(", ", Ds1ChrSound.FreeDecades(ds2Data, bankChr).Select(d => $"{d * 100} (bank {Ds1ChrSound.BankName(d)})"))}");
        var bank = Ds1ChrSound.Bank(dsrRoot, ds2Data, o.Ds1Chr, bankChr, t, ds1Src.Tae);
        res.Files[$"sound/{Ds1ChrSound.BankName(bankChr)}.bnd"] = bank.Bank;
        foreach (var (path, bytes) in bank.Loose) res.TitleFiles[path] = bytes;
        res.Report.AddRange(bank.Report);
        // DS1 dummy ids the model has more than once (c2500: 199, the two eyes): a DS2 effect there spawns at each (rule S7)
        var multiDummies = ds1Flver.Dummies.GroupBy(d => (int)d.ReferenceID).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        foreach (var kind in new[] { "pl", "sfx", "snd" })
        {
            byte[] template = File.ReadAllBytes(Path.Combine(ds2Data, "timeact", "chr", $"{taeT}_{kind}.tae"));
            byte[] bytes; string rep;
            if (anims.Attacks != null && kind != "pl")
            {
                // merge9 stage 4 (enemies x chrsound): the DS1 attack animations first (Ds2TaeFile needs each animation's data
                // contiguous; Ds2Tae.SetEvents appends the translated events at the end of the file), then the clip slots' and
                // the attack clips' DS1 sound / effect events (EditTae)
                var tf = Ds2TaeFile.Read(template);
                foreach (var (id, frames, _) in anims.Attacks.TaeAnims) tf.AddAnimation(id, frames, []);
                (bytes, rep) = Ds1RigAnims.EditTae(tf.Write(), anims, kind, ds1Src, ds1ChrId, res.SoundLog, bank.Commons, multiDummies, bank.OwnEvents);
                rep += $"; {anims.Attacks.TaeAnims.Count} DS1 attack animations added before the edit";
            }
            else
            {
                (bytes, rep) = Ds1RigAnims.EditTae(template, anims, kind, ds1Src, ds1ChrId, res.SoundLog, bank.Commons, multiDummies, bank.OwnEvents);
                if (anims.Attacks != null)
                {
                    // track enemies: one TimeAct animation per DS1 attack clip (Ds1Attacks; Ds2TaeFile adds records, byte-identical
                    // re-serialiser on all 696 DS2 chr TAEs) with its gameplay events (_pl only: _snd/_sfx are handled above)
                    var tf = Ds2TaeFile.Read(bytes);
                    foreach (var (id, frames, events) in anims.Attacks.TaeAnims) tf.AddAnimation(id, frames, events);
                    bytes = tf.Write();
                    rep += $"; {anims.Attacks.TaeAnims.Count} DS1 attack animations added ({anims.Attacks.TaeAnims.Sum(a => a.Events.Count)} events)";
                }
            }
            res.Files[$"timeact/chr/{taeN}_{kind}.tae"] = bytes;
            res.Report.Add($"{taeN}_{kind}.tae (from {taeT}): {rep}");
        }

        // ---- model archive: the template's layout with DS1 content
        var bnd = BND4.Read(Path.Combine(ds2Data, "model", "chr", t + ".bnd"));
        var templateFlv = FLVER2.Read(bnd.Files.First(f => f.Name.EndsWith($"{t}.flv", StringComparison.OrdinalIgnoreCase)).Bytes);
        // session 11 (ret11), rule MT1: a DS1 model whose drawn (non-"#NN#") materials use more than one diffuse texture (the Black
        // Knight: 8 armour / sword / shield materials) gets texture names of its own per DS1 material, as the boss pipeline's
        // (ds1TextureNames; Ds1Boss.Texbnd: one TPF per texture), instead of the template's one set (every mesh would show the
        // last material's pixels). Single-texture characters (the Hollows, soldiers, warriors, skeletons) keep the template names.
        bool mt1 = Mt1(ds1Flver);
        var model = Ds1RigModel.Build(ds1Flver, templateFlv, anims.Rig, anims.Ds1Names, o, t, n, r.WeaponMeshes, ds1TextureNames: mt1);
        res.Model = model;
        res.Report.AddRange(model.Report);
        byte[] flv = model.Flver.Write();
        var templateRagdoll = new HkPackfile(bnd.Files.First(f => f.Name.EndsWith("_Ragdoll.hkx", StringComparison.OrdinalIgnoreCase)).Bytes);
        // DS1's ragdoll is cNNNN.hkx (c2550 also has cNNNN_c.hkx = its cloth, which DS2 does not get)
        byte[] Ds1Named(string file) => chrbnd.Files.First(f => Path.GetFileName(f.Name.Replace('\\', '/')).Equals(file, StringComparison.OrdinalIgnoreCase)).Bytes;
        var (rag, ragPwv, ragRep) = Ds1RigModel.Ragdoll(Ds1Named(o.Ds1Chr + ".hkx"), Ds1Named(o.Ds1Chr + ".hkxpwv"), templateRagdoll, anims.Rig, anims.Ds1Names);
        res.Report.AddRange(ragRep);
        int cloth = bnd.Files.RemoveAll(f => Path.GetFileName(f.Name).Contains("_Cloth", StringComparison.OrdinalIgnoreCase));
        foreach (var f in bnd.Files)
        {
            string name = Path.GetFileName(f.Name);
            if (name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase)) f.Bytes = flv;
            else if (name.EndsWith(".acb", StringComparison.OrdinalIgnoreCase)) f.Bytes = mt1 ? AcbTextures(ChrPorter.RewriteAcb(f.Bytes, t, n, cloth > 0), [.. model.TexUse.Keys.Order(StringComparer.Ordinal)]) : ChrPorter.RewriteAcb(f.Bytes, t, n, cloth > 0);   // track enemies: c1020's cloth asset goes with its files (run s5-30: a stale cloth crashed DS2); MT1: our texture assets
            else if (name.EndsWith("_Ragdoll.hkx", StringComparison.OrdinalIgnoreCase)) f.Bytes = rag;
            else if (name.EndsWith("_Ragdoll.hkxpwv", StringComparison.OrdinalIgnoreCase)) f.Bytes = ragPwv;
            else if (name.EndsWith(".flvpwv", StringComparison.OrdinalIgnoreCase)) f.Bytes = model.Flvpwv;
            else f.Bytes = ChrPorter.ReplaceAscii(f.Bytes, t, n);
            f.Name = f.Name.Replace(t, n);
        }
        if (r.OwnModel) res.Files[$"model/chr/{n}.bnd"] = bnd.Write();   // a shared model (track enemies) is written by its owner build
        var ds1Tex = ChrPorter.LoadDs1Textures(dsrRoot, o);
        var missing = model.TexUse.Values.Where(x => !ds1Tex.ContainsKey(x)).Distinct().ToList();
        if (missing.Count > 0) throw new InvalidDataException($"{o.Ds1Chr}: DS1 textures not found: {string.Join(", ", missing)}");
        if (r.OwnModel) res.Files[$"model/chr/{n}.texbnd"] = mt1 ? Ds1Boss.Texbnd(Path.Combine(ds2Data, "model", "chr", t + ".texbnd"), model.TexUse, ds1Tex) : ChrPorter.WriteTexbnd(ds2Data, t, n, o, model.TexUse, [], ds1Tex);
        res.Report.Add($"textures: " + string.Join(", ", model.TexUse.Select(kv => $"{kv.Key}←{kv.Value}")));
        return res;
    }
}
