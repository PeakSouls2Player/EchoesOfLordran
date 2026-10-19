using SoulsFormats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Session 11 (fx11, user: "make sure things like pots breaking when you roll into them make their correct sounds"): the
/// break presentation of a ported DS1 breakable (ObjPorter Kind.Breakable; its collision, fragments and break trigger are
/// ObjPorter's / rule potroll-3):
///  - OBJ-SND1: DS1's own break sound (<see cref="ObjectSounds.ApplyBreak"/>);
///  - OBJ-SFX1: DS1's break effect. DS1 ObjectParam.breakSfxId (the effect DS1 spawns when the object breaks; −1 = none,
///    DS1 then shows only its fragments) → DS2 MapObjectGDParam.sfxIdBreak0 of the model (the effect DS2's break controller
///    spawns): a DS1 effect is converted by rule B2b (<see cref="FfxPorter.Convert"/>, DS2 id B2b-id); −1 → −1. The jar
///    template's own 3521 (DS2's dust puff) is DS2 content DS1 does not show, so it is not kept.
/// </summary>
public static class Breakables
{
    static PARAM? objectParam;

    public sealed record Result(string? BreakSound, int Ds1Sfx, int Ds2Sfx, string Note);

    /// <summary>Every breakable handled in this build (obj-check reads the notes; reset by build-pack).</summary>
    public static List<(string Ds1Obj, string Ds2Name, Result R)> All { get; } = [];

    public static Result Apply(Pack pack, string dsrRoot, string ds1Obj, string ds2Name)
    {
        objectParam ??= Ds1Source.LoadParam(dsrRoot, "ObjectParam");
        var row = objectParam?[int.Parse(ds1Obj[1..])];
        string? sound = ObjectSounds.ApplyBreak(pack, ds1Obj, ds2Name);
        int ds1Sfx = row != null ? Convert.ToInt32(row["breakSfxId"].Value) : -1, ds2Sfx = -1;
        string note;
        var gd = pack.Param("MapObjectGDParam")[ObjPorter.ModelId(ds2Name)];
        if (gd == null) note = "no MapObjectGDParam row (template without one): break effect unchanged";
        else
        {
            int before = Convert.ToInt32(gd["sfxIdBreak0"].Value);
            if (ds1Sfx >= 0)
            {
                // the DS1 area of the object (our area 19 = DS1 area 10); FfxPorter looks in mAA_BB, then mAA, then CommonEffects
                // the DS1 binder that has the effect (Burg barrels: FRPG_SfxBnd_m10_01): its map; FfxPorter then looks in mAA_BB, mAA, CommonEffects
                string area = ds2Name.Substring(1, 2) == "19" ? "10" : ds2Name.Substring(1, 2);
                string binder = FfxPorter.Ds1EffectAll(dsrRoot, ds1Sfx).Select(x => x.Binder).FirstOrDefault(b => b.StartsWith($"m{area}_")) ?? "";   // "m10_01"
                var bm = System.Text.RegularExpressions.Regex.Match(binder, @"^m(\d\d)_(\d\d)$");
                string ds1Map = bm.Success ? $"m{bm.Groups[1].Value}_{bm.Groups[2].Value}_00_00" : $"m{area}_99_00_00";
                var log = new List<string>();
                var conv = FfxPorter.Convert(dsrRoot, ds1Map, ds1Sfx, log);
                foreach (var l in log) pack.Note(l);
                if (conv != null) { ds2Sfx = conv.Id; gd["sfxIdBreak0"].Value = ds2Sfx; note = $"DS1 break effect {ds1Sfx} → DS2 {ds2Sfx} (rule B2b)"; }
                else { ds2Sfx = before; note = $"DS1 break effect {ds1Sfx} not convertible (see the atmos lines): template's {before} kept"; }
            }
            else { gd["sfxIdBreak0"].Value = -1; note = $"DS1 breakSfxId -1 (no break effect, fragments only): sfxIdBreak0 {before} → -1"; }
        }
        var r = new Result(sound, ds1Sfx, ds2Sfx, note);
        All.Add((ds1Obj, ds2Name, r));
        pack.Note($"object break effect (OBJ-SFX1) {ds1Obj} → {ds2Name}: {note}");
        return r;
    }
}
