using Lordran.Tools.Formats;
using Lordran.Tools.Formats.Morpheme;
using SoulsFormats;
using Ds1ChrSound = Lordran.Tools.Build.Chr.Ds1ChrSound;
// merge stage 5: the boss track's network / TAE code lives in its own namespaces (see MERGE-NOTES.md "Unification backlog")
using NmbNetEdit = Lordran.Tools.Formats.Morpheme.Boss.NmbNetEdit;
using Ds2TaeFile = Lordran.Tools.Formats.Boss.Ds2TaeFile;

namespace Lordran.Tools.Commands;

/// <summary>
/// merge9 stage 6 (boss-sound) checks of the 1:1 boss's sound, run by <c>ds1boss-check</c> after the step-2 checks: the demon's
/// anim set moved out of DS2's decade 2230 into a free decade (animId 221010) and its sound bank is DS1's own FMOD project
/// built by track chrsound's rules (<see cref="Ds1ChrSound"/>, S1-S10).
/// <list type="number">
/// <item>decade: the animId's decade (animId/100 rounded down to tens) has no DS2 bank / fevbnd project and is not 2250
/// (<see cref="Ds1ChrSound.BankChecks"/>), is one of <see cref="Ds1ChrSound.FreeDecades"/> (no DS2 model, anibnd or TimeAct
/// either), and the boss track's decade 2230 is refused;</item>
/// <item>names: anibnd c{animId/100}, its NMB named like it (vanilla: 173/173), TAEs c{animId}_pl/_sfx/_snd, the bank
/// frpg2_c{decade}.bnd, nothing named after the old anim set (c2232.anibnd, c223210_*, frpg2_c2232/c2230);</item>
/// <item>bank: entry id 0 = FEV1 project named like the bank (S1b), id 1 = DS1's own wave bank, every wave bank the project
/// lists loose under title/sound with its FSB4 hash (S2/S9); rebuilt from DSR = the file (S10), also with the template bank
/// format ds1-chr-bank uses (c1030);</item>
/// <item>_snd: every 201 names an event the bank's project defines, written as rule S3 (+12/+13 = 1/1, unk 1, group 200),
/// dummy -1 or a dummy of the DS2 model; per TAE id the network names, the 201 count = the DS1 sound events of a DS1 clip
/// playing that id (counted from DSR's TAE, by name against the project; role table as BossAiCheck); no 202 left;</item>
/// <item>with --pack: EnemyParam animId, the config rows keyed by the animId moved (BossParam, CameraExFollowParam,
/// DeadTypeParam, EnemyCommonParam, EnemyMoveParam, LockOnParam: 223200 + the new animId, none at 223210), the
/// behaviour rows still at BehaviorId, the loose wave banks in the pack, no file of the old anim set.</item>
/// </list>
/// </summary>
public static partial class BossTools
{
    /// <summary>The boss track's animId (decade 2230, DS2's own frpg2_c2230.bnd), for the "nothing left behind" checks.</summary>
    const int OldAnimId = 223210;

    static void Ds1BossCheckSound(string dir, string? packDir, Action<bool, string> check)
    {
        var spec = CheckSpec;   // session 11 (ret11): the boss under check (--boss / boss.txt; default the Asylum Demon)
        string gd = Path.Combine(dir, "gamedata"), td = Path.Combine(dir, "title");
        string data = ExtractDs2.DefaultOut;
        int bankChr = Ds1ChrSound.BankChr(spec.AnimId);
        string bankName = Ds1ChrSound.BankName(bankChr), animChr = $"c{spec.AnimId / 100:D4}";
        // ---- 1. decade
        var objections = Ds1ChrSound.BankChecks(data, bankChr);
        var free = Ds1ChrSound.FreeDecades(data, Ds1ChrSound.BankChr(OldAnimId), 12);
        var old = Ds1ChrSound.BankChecks(data, Ds1ChrSound.BankChr(OldAnimId));
        check(objections.Count == 0 && (free.Contains(bankChr) || spec.Ds1Map != "m18_01_00_00") && old.Count > 0,   // session 11 (burgchr11): the Burg's decades lie outside the 12 nearest 2230
            $"stage 6: animId {spec.AnimId} → decade {bankChr} is free (no DS2 bank / fevbnd project, not 2250; FreeDecades near 2230: {string.Join(", ", free.Take(6))}); the boss track's decade {Ds1ChrSound.BankChr(OldAnimId)} is refused ({string.Join("; ", old)})");
        // ---- 2. names
        var rel = Directory.GetFiles(gd, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(gd, f).Replace('\\', '/')).ToList();
        string anibndRel = $"morpheme4/chr/{animChr}.anibnd.dcx";
        var taeRel = new[] { "pl", "sfx", "snd" }.Select(k => $"timeact/chr/c{spec.AnimId:D6}_{k}.tae").ToList();
        string bankRel = $"sound/{bankName}.bnd";
        string[] oldNames = [$"c{OldAnimId / 100:D4}.anibnd", $"c{OldAnimId:D6}_", $"frpg2_c{OldAnimId / 100:D4}.bnd", $"frpg2_c{Ds1ChrSound.BankChr(OldAnimId):D4}.bnd"];
        var stale = rel.Where(r => oldNames.Any(o => Path.GetFileName(r).StartsWith(o, StringComparison.OrdinalIgnoreCase))).ToList();
        var sounds = rel.Where(r => r.StartsWith("sound/", StringComparison.OrdinalIgnoreCase)).ToList();
        var nmbNames = new List<string>();
        if (rel.Contains(anibndRel))
        {
            byte[] ab = File.ReadAllBytes(Path.Combine(gd, anibndRel.Replace('/', Path.DirectorySeparatorChar)));
            if (DCX.Is(ab)) ab = DCX.Decompress(ab);
            nmbNames = BND4.Read(ab).Files.Where(f => f.Name.EndsWith(".nmb", StringComparison.OrdinalIgnoreCase)).Select(f => Path.GetFileName(f.Name.Replace('\\', '/'))).ToList();
        }
        check(rel.Contains(anibndRel) && taeRel.All(rel.Contains) && sounds.SequenceEqual([bankRel]) && nmbNames.SequenceEqual([$"{animChr}.nmb"]) && stale.Count == 0,
            $"stage 6 names: {anibndRel} (NMB {string.Join(", ", nmbNames)}), {string.Join(", ", taeRel.Select(Path.GetFileName))}, {string.Join(", ", sounds)}; files named after the old anim set {OldAnimId}: {(stale.Count == 0 ? "none" : string.Join(", ", stale))}");
        if (!rel.Contains(anibndRel) || !taeRel.All(rel.Contains) || !rel.Contains(bankRel))
        {
            check(false, $"stage 6: the files of anim set {animChr} / bank {bankName} are missing, so the sound checks cannot run (an output built with another animId?)");
            return;
        }
        // ---- 3. bank
        byte[] bankFile = File.ReadAllBytes(Path.Combine(gd, bankRel.Replace('/', Path.DirectorySeparatorChar)));
        byte[] braw = DCX.Is(bankFile) ? DCX.Decompress(bankFile) : bankFile;
        var bnd = BND4.Read(braw);
        var e0 = bnd.Files.FirstOrDefault(f => f.ID == 0); var e1 = bnd.Files.FirstOrDefault(f => f.ID == 1);
        byte[] fev = e0?.Bytes ?? [];
        var proj = e0 != null ? Fev1Project.Read(fev) : null;
        var (_, banks) = e0 != null ? Ds1ChrSound.Fev1Header(fev) : ("", new List<(string Name, ulong Hash)>());
        var looseOk = banks.Select(b => (b.Name, Path: Path.Combine(td, "sound", b.Name + ".fsb"), b.Hash)).Select(b => File.Exists(b.Path) && Ds1ChrSound.Fsb4Hash(File.ReadAllBytes(b.Path)) == b.Hash).ToList();
        string ownFsb = Path.Combine(td, "sound", $"frpg_{spec.Ds1Chr}.fsb");
        bool idOne = e1 != null && File.Exists(ownFsb) && e1.Bytes.AsSpan().SequenceEqual(File.ReadAllBytes(ownFsb));
        check(bnd.Files.Count == 2 && proj != null && proj.Name == bankName && e0!.Name.EndsWith($@"\{bankName}\{bankName}.fev") && e1 != null && e1.Name.EndsWith($@"\{bankName}\{bankName}.fsb")
            && idOne && looseOk.Count > 0 && looseOk.All(x => x),
            $"bank {bankRel}: id 0 = FEV1 project '{proj?.Name}' ({proj?.Events.Count} events; rule S1b: = the resource name), id 1 = DS1's frpg_{spec.Ds1Chr}.fsb ({idOne}); wave banks listed {string.Join(", ", banks.Select(b => b.Name))}, loose with matching FSB4 hash {looseOk.Count(x => x)}/{looseOk.Count}");
        var src = Build.Chr.Ds1ChrSource.Load(Program.DefaultDsrRoot, spec.Ds1Chr);
        var again = Ds1ChrSound.Bank(Program.DefaultDsrRoot, data, spec.Ds1Chr, bankChr, spec.TemplateChr, src.Tae);
        var viaTool = Ds1ChrSound.Bank(Program.DefaultDsrRoot, data, spec.Ds1Chr, bankChr, "c1030", src.Tae);
        check(again.Bank.AsSpan().SequenceEqual(bankFile) && viaTool.Bank.AsSpan().SequenceEqual(bankFile),
            $"rule S10: the bank rebuilt from DSR = the file ({bankFile.Length:N0} B; template format {spec.TemplateChr}: {again.Bank.AsSpan().SequenceEqual(bankFile)}, ds1-chr-bank's c1030: {viaTool.Bank.AsSpan().SequenceEqual(bankFile)})");
        // the boss path's S10 guard (ChrPorter.AddDs1SoundShared, used by build-pack for the boss): the same bank and the same
        // loose wave banks again are shared; another DS1 character's project in this decade (DS1 c2500's, built for it) is refused
        {
            var pk = new Build.Pack(Program.DefaultDs2Root, data);
            var gfiles = new Dictionary<string, byte[]> { [bankRel] = bankFile };
            bool sharedOk = true, refused;
            try
            {
                Build.Chr.ChrPorter.AddDs1SoundShared(pk, "check", gfiles, again.Loose);
                Build.Chr.ChrPorter.AddDs1SoundShared(pk, "check", gfiles, again.Loose);
            }
            catch (InvalidDataException) { sharedOk = false; }
            var otherSrc = Build.Chr.Ds1ChrSource.Load(Program.DefaultDsrRoot, "c2500");
            var other = Ds1ChrSound.Bank(Program.DefaultDsrRoot, data, "c2500", bankChr, "c1030", otherSrc.Tae);
            try { Build.Chr.ChrPorter.AddDs1SoundShared(pk, "check", new Dictionary<string, byte[]> { [bankRel] = other.Bank }, new Dictionary<string, byte[]>()); refused = false; }
            catch (InvalidDataException) { refused = true; }
            check(sharedOk && refused, $"rule S10 on the boss path (AddDs1SoundShared): the identical bank + loose wave banks added twice {(sharedOk ? "shared" : "NOT shared")}; DS1 c2500's project in decade {bankChr} {(refused ? "refused" : "NOT refused")}");
        }
        // ---- 4. _snd events
        var names = Ds1ChrSound.Fev1EventNames(fev);
        var snd = Ds2TaeFile.Read(File.ReadAllBytes(Path.Combine(gd, taeRel[2].Replace('/', Path.DirectorySeparatorChar))));
        var model = BND4.Read(Path.Combine(gd, "model", "chr", spec.N + ".bnd"));
        var flv = FLVER2.Read(model.Files.First(f => f.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase)).Bytes);
        var dummyIds = flv.Dummies.Select(d => (int)d.ReferenceID).ToHashSet();
        int n201 = 0, named = 0, layout = 0, dummyOk = 0, n202 = 0;
        var unnamed = new List<string>();
        foreach (var a in snd.Animations)
            foreach (var e in a.Events)
            {
                if (e.Type == 202) n202++;
                if (e.Type != 201) continue;
                n201++;
                string nm = Ds1ChrSound.Ds2EventName(e.Params);
                if (names.Contains(nm)) named++; else if (unnamed.Count < 6) unnamed.Add($"{a.Id}:{nm}");
                if (e.Unk04 == 1 && e.Params.Length >= 24 && e.Params[12] == 1 && e.Params[13] == 1 && a.Groups[e.Group].Type == Ds1ChrSound.SndGroup) layout++;
                short dm = BitConverter.ToInt16(e.Params, 10);
                if (dm == -1 || dummyIds.Contains(dm)) dummyOk++;
            }
        check(n201 > 0 && named == n201 && layout == n201 && dummyOk == n201 && n202 == 0,
            $"_snd: {n201} DS2 201 events, {named} name an event of the bank's project{(unnamed.Count > 0 ? $" (not: {string.Join(", ", unnamed)})" : "")}, rule S3 layout (+12/+13 = 1/1, unk 1, group 200) on {layout}, dummy -1 or on the model on {dummyOk}; 202 events left {n202}");
        // per TAE id the network names: the 201 count = the DS1 sound events (resolved by name against the project) of a DS1
        // clip playing that id; counted from DSR's TAE, independently of the build's slot table
        var files = MorphemeTools.LoadFiles(Path.Combine(gd, anibndRel.Replace('/', Path.DirectorySeparatorChar)));
        var nmb = NmbFile.Read(files.First(f => f.Name.EndsWith(".nmb")).Bytes);
        var x = nmb.Bundles[nmb.IndexOf(10)].Data;
        var net = new NmbNetwork(x);
        var fn = NmbNetEdit.ReadFileNames(nmb.Bundles[nmb.IndexOf(13)].Data);
        var tracks = nmb.Bundles.Where(b => b.Type is 3 or 4 or 5).ToDictionary(b => b.AssetId, b => NmbEventTrack.Read(b.Data, b.Type));
        int ds1ChrId = int.Parse(spec.Ds1Chr[1..]);
        int Expected(int ds1)
        {
            if (ds1 < 0) return 0;
            return Ds1ChrSound.Sounds(src.TaeAnim(ds1)).Count(s =>
            {
                var (t, id) = Ds1ChrSound.Ds1Event(s.Type, s.Id);
                string nm = t == 1 && id / 100000 == ds1ChrId ? $"c{id:D9}" : Ds1ChrSound.CommonName(ds1ChrId, id);
                return names.Contains(nm);
            });
        }
        var want = new Dictionary<long, HashSet<int>>();
        var clipOf = new Dictionary<long, HashSet<string>>();
        foreach (var node in net.Nodes().Where(n => n.Type == 104))
        {
            var sa = NmbNetwork.Find(node, 21); var et = NmbNetwork.Find(node, 23);
            if (sa == null || et == null) continue;
            string clip = fn.Files[net.ClipIndex(sa)];
            int ds1 = -1;
            if (clip.StartsWith(spec.N + "_a00_", StringComparison.OrdinalIgnoreCase)) ds1 = int.Parse(clip.Substring(spec.N.Length + 5, 4));
            else if (Build.Chr.Ds1RigAnims.RoleFor(spec.Anim.Roles, clip) is { } role) ds1 = role.Ds1.FirstOrDefault(v => v >= 0 && src.Has(v), -1);
            foreach (var (_, tid, _) in net.Tracks(et))
                if (tracks.TryGetValue(tid, out var t) && t.UserData == 1000)
                    foreach (var ev in t.Events)
                    {
                        (want.TryGetValue(ev.Value, out var w) ? w : want[ev.Value] = []).Add(Expected(ds1));
                        (clipOf.TryGetValue(ev.Value, out var c) ? c : clipOf[ev.Value] = []).Add(ds1 >= 0 ? Build.Chr.Ds1ChrSource.ClipName(ds1) : "bind");
                    }
        }
        int idsOk = 0, withSound = 0;
        var wrong = new List<string>();
        foreach (var (id, w) in want.OrderBy(kv => kv.Key))
        {
            var a = snd.Get(id);
            int have = a?.Events.Count(e => e.Type == 201) ?? -1;
            if (w.Contains(have)) idsOk++; else if (wrong.Count < 8) wrong.Add($"{id} ({string.Join("/", clipOf[id])}): {have} vs {string.Join("|", w)}");
            if (have > 0) withSound++;
        }
        var stray = snd.Animations.Where(a => !want.ContainsKey(a.Id) && a.Events.Any(e => e.Type is 201 or 202)).Select(a => a.Id).ToList();
        check(idsOk == want.Count && stray.Count == 0,
            $"_snd per TAE id the network names: 201 count = the DS1 clip's own + copied common sound events on {idsOk}/{want.Count} ids ({withSound} with sound){(wrong.Count > 0 ? "; wrong: " + string.Join("; ", wrong) : "")}; TAE ids the network does not name with sound events: {(stray.Count == 0 ? "none" : string.Join(", ", stray.Take(8)))}");
        // the intro's drop and the death: DS1 c2232's own sounds there
        int dropId = 9060, deathId = 2200;
        // session 11 (ret11), rule B-k: a boss without DS1's drop intro (the Stray Demon starts dormant: no a00_9060) checks the death only
        // session 11 (burgchr11): nor one whose template network has no start-up slot to play it in (the dog on c2270)
        bool hasDrop = Build.Chr.Ds1ChrSource.Load(Program.DefaultDsrRoot, spec.Ds1Chr).Has(dropId) && clipOf.Values.Any(l => l.Contains(Build.Chr.Ds1ChrSource.ClipName(dropId)));
        check((!hasDrop || Expected(dropId) > 0 && want.Any(kv => clipOf[kv.Key].Contains(Build.Chr.Ds1ChrSource.ClipName(dropId)) && (snd.Get(kv.Key)?.Events.Count(e => e.Type == 201) ?? 0) > 0)) && Expected(deathId) > 0,
            $"DS1's drop {Build.Chr.Ds1ChrSource.ClipName(dropId)} ({(hasDrop ? $"{Expected(dropId)} sound events" : "not in this boss's set")}) and death {Build.Chr.Ds1ChrSource.ClipName(deathId)} ({Expected(deathId)}) carry DS1 sounds; the start-up wake slot's TAE has them");

        if (packDir == null) return;
        // ---- 5. pack
        var reg = ParamStore.LoadDs2(Path.Combine(packDir, "title"), data);
        var ep = reg.Params["EnemyParam"][spec.EnemyParam];
        check(ep != null && Convert.ToInt32(ep["animId"].Value) == spec.AnimId && Convert.ToInt32(ep["modelId"].Value) == spec.Model,
            $"EnemyParam {spec.EnemyParam}: model {ep?["modelId"].Value}, animId {ep?["animId"].Value}");
        string[] cfg = ["BossParam", "CameraExFollowParam", "DeadTypeParam", "EnemyCommonParam", "EnemyMoveParam", "LockOnParam"];
        // session 11 (burgchr11): a config param whose template has no row (the Corpse Rat has no BossParam) is not cloned: expected only where
        // the vanilla template (keyed by the template EnemyParam, or by its commonId / moveId / deadType) has one
        var vanilla = ParamStore.LoadDs2(Program.DefaultDs2Root, data);
        bool HasSource(string p)
        {
            if (vanilla.Params[p][spec.TemplateEnemyParam] != null) return true;
            var te = vanilla.Params["EnemyParam"][spec.TemplateEnemyParam];
            string? field = p switch { "EnemyCommonParam" => "commonId", "EnemyMoveParam" => "moveId", "DeadTypeParam" => "deadType", _ => null };
            return te != null && field != null && te[field] is { } f && vanilla.Params[p][Convert.ToInt32(f.Value)] != null;
        }
        var cfgBad = cfg.Where(p => HasSource(p) && (reg.Params[p][spec.EnemyParam] == null || reg.Params[p][spec.AnimId] == null) || reg.Params[p][OldAnimId] != null).ToList();
        string[] beh = ["EnemyBehaviorParam", "EnemyBehaviorSecondParam", "EnemyBehaviorThirdParam", "EnemyComboBehaviorParam"];
        var behBad = beh.Where(p => reg.Params[p][spec.BehaviorId] == null).ToList();
        var w0 = reg.Params["EnemyEquipWeaponParam"][spec.WeaponParam];
        check(cfgBad.Count == 0 && behBad.Count == 0 && w0 != null && Convert.ToInt32(w0["behaviorId"].Value) == spec.BehaviorId,
            $"regulation: {string.Join("/", cfg)} at {spec.EnemyParam} and {spec.AnimId}, none at {OldAnimId}{(cfgBad.Count > 0 ? $" (wrong: {string.Join(", ", cfgBad)})" : "")}; behaviour rows at BehaviorId {spec.BehaviorId} (independent of the animId){(behBad.Count > 0 ? $" (missing: {string.Join(", ", behBad)})" : "")}; EnemyEquipWeaponParam {spec.WeaponParam}.behaviorId {w0?["behaviorId"].Value}");
        string pt = Path.Combine(packDir, "title");
        var titles = Directory.Exists(td) ? Directory.GetFiles(td, "*", SearchOption.AllDirectories) : [];
        int tSame = titles.Count(f => { string q = Path.Combine(pt, Path.GetRelativePath(td, f)); return File.Exists(q) && File.ReadAllBytes(q).AsSpan().SequenceEqual(File.ReadAllBytes(f)); });
        string pg = Path.Combine(packDir, "gamedata");
        var packStale = Directory.GetFiles(pg, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(pg, f).Replace('\\', '/'))
            .Where(r => oldNames.Any(o => Path.GetFileName(r).StartsWith(o, StringComparison.OrdinalIgnoreCase))).ToList();
        var packBanks = Directory.GetFiles(Path.Combine(pg, "sound"), "frpg2_c*.bnd").Select(Path.GetFileName).ToList();
        check(titles.Length > 0 && tSame == titles.Length && packStale.Count == 0,
            $"pack: the loose wave banks ({string.Join(", ", titles.Select(Path.GetFileName))}) identical {tSame}/{titles.Length}; files of the old anim set: {(packStale.Count == 0 ? "none" : string.Join(", ", packStale))}; character banks in the pack: {string.Join(", ", packBanks)}");
    }
}
