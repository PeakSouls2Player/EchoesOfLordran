using System.Buffers.Binary;
using SoulsFormats;
using Lordran.Tools.Formats;

namespace Lordran.Tools.Build.Maps;

/// <summary>
/// Session 11 (snd11), rule SND7: a ported DS1 object's own animation sounds (doors, gates, levers, the boulder, the Burg's
/// barrel). DS1 plays them from the object's TAE (DSR obj\oNNNN.objbnd → anibnd → oNNNN.tae: event 128 {i32 sound type,
/// i32 id} / 129 {type, id, i32 dummy, i32 slot}; type 3 'o', names from frpg_mAA or frpg_main). DS2 plays an object's
/// sounds from timeact/obj/oAA_BBBB.tae event 502000 (group type 502000; params {i32 id, u8 kind, u8 keep, i16 dummy,
/// u8 follow, ...}): kind 100 → FMOD event "o%09d" of model × 1000 + id (decomp FUN_1403e59b0, the 502000 handler: param byte +4 → FUN_1403fc900 →
/// FUN_1403fcc50 → FUN_1404a5760(…, 100, model, id)), model = the object's model number (o18_8541 → 188541) unless its
/// MapObjectParam row's timeActId (+0x14, FUN_1403c1db0) is set (0 on every ported row); kind 0xFF → "a%09d" of id (full
/// id, FUN_1403fc9c0 → FUN_1404a55f0(…, 0, id)); keep = byte +5 (1 = the sound outlives the event; vanilla 1 on one-shots);
/// dummy −1 = the object's origin.
///  - The ported object's TAE (the prefab template's, renamed) loses the template's own sound events (502000-502002: DS2's
///    sounds for another model; their names o{our model}{id} would be undefined or, worse, one of ours) and gets DS1's
///    sound events at DS1's times on the animation the DS1 clip became (a00_AA_BBBB → TAE id AA × 10000 + BBBB); other
///    template events (2104 effects) keep their groups. A non-prefab object (Kind.Door: o8540) has no template TAE: it
///    borrows a vanilla object TAE whose animation ids cover the mapped ones (o15_1060 "gimmick door", 10001/10002) with
///    only DS1's sounds in it (P: whether DS2 reads an object TAE for a door-type object is unverified in game).
///  - Ids: a DS1 'o' id in the object's own range (id / 100000 = the DS1 model, rest &lt; 1000) keeps its last three digits
///    (o854100001 → o18_8541 id 1 → "o188541001"); a DS1 id of another model (o8544 plays o854100002) gets a free id of the
///    object from 999 down. DS1 type 0 'a' → kind 0xFF with DS1's id. Other DS1 types: noted, not ported.
///  - The FMOD events "o{model}{id:000}" are copies of DS1's under the new name (<see cref="Fev1Project.AddEvents"/>), added
///    by <see cref="Atmosphere.SoundBanks"/> to our copy of the DS1 area memory project that defines the DS1 event (current
///    name in our copy: an SND3 'a' alias or the DS1 name), else copied from DS1's frpg_main (loose frpg_main.fsb), else
///    noted as not ported (<see cref="Pending"/>).
/// </summary>
public static class ObjectSounds
{
    /// <summary>A DS1 sound event a ported object's TAE now names under <paramref name="Ds2Event"/>.</summary>
    public sealed record Import(string Ds1Event, string Ds2Event, string Object);

    /// <summary>Imports not yet put into the fevbnd (consumed by <see cref="Atmosphere.SoundBanks"/>; build-pack order:
    /// a map's objects are ported before its sound banks are written). Reset by build-pack.</summary>
    public static List<Import> Pending { get; } = [];
    /// <summary>Every import of this build (checks, reports).</summary>
    public static List<Import> All { get; } = [];

    /// <summary>Off with build-pack --no-ds1-sound (no DS1 projects in the fevbnd: the object TAEs stay the template's).</summary>
    public static bool Enabled { get; set; } = true;
    public const int Ds2SoundEvent = 502000;
    static readonly HashSet<int> Ds2SoundTypes = [502000, 502001, 502002];
    /// <summary>Vanilla object TAE borrowed by an object without a template TAE (anim ids 10001, 10002).</summary>
    public const string BorrowedTae = "o15_1060";

    static int ModelNumber(string ds2Name) => int.Parse(ds2Name.Substring(1, 2) + ds2Name.Substring(4, 4));
    static int Ds2AnimId(string ds2Anim) // a00_AA_BBBB → AA × 10000 + BBBB
    {
        var p = ds2Anim.Split('_');
        return int.Parse(p[^2]) * 10000 + int.Parse(p[^1]);
    }
    static int I32(byte[] b, int o) => o + 4 <= b.Length ? BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o)) : -1;

    /// <summary>Rule SND7 for one ported object (<see cref="ObjPorter.Port"/>): rewrites / adds timeact/obj/{ds2Name}.tae and
    /// registers the FMOD copies. Returns the number of DS1 sound events written.</summary>
    /// <param name="timeShift">Session 11 (fx11, rule CH2): seconds added to DS1's event times (an object whose DS2 clip is not DS1's
    /// own: the chest's sounds move with the lid's motion onset, <see cref="MotionOnset"/>); 0 = DS1's times (SND7).</param>
    public static int Apply(Pack pack, BND3 anibnd, string ds1Obj, string ds2Name, (string Ds1Anim, string Ds2Anim)[] anims, float timeShift = 0)
    {
        var taeFile = anibnd.Files.FirstOrDefault(f => f.Name.EndsWith(".tae", StringComparison.OrdinalIgnoreCase));
        if (!Enabled || taeFile == null || anims.Length == 0) return 0;
        var ds1Tae = TAE.Read(taeFile.Bytes);
        int ds1Model = int.Parse(ds1Obj[1..]), model = ModelNumber(ds2Name);
        var sounds = new List<(long Ds2Anim, int Type, int Id, int Dummy, float Start, float End)>();
        var notes = new List<string>();
        foreach (var (a1, a2) in anims)
        {
            var anim = ds1Tae.Animations.FirstOrDefault(a => a.ID == int.Parse(a1.Split('_')[^1]));
            if (anim == null) continue;
            foreach (var e in anim.Events.Where(e => e.Type is 128 or 129))
            {
                var p = e.GetParameterBytes(false);
                sounds.Add((Ds2AnimId(a2), I32(p, 0), I32(p, 4), e.Type == 129 ? I32(p, 8) : -1, MathF.Max(0, e.StartTime + timeShift), MathF.Max(0, e.EndTime + timeShift)));
            }
        }
        string path = $"timeact/obj/{ds2Name}.tae";
        byte[]? taeBytes = pack.TryGetFile("gamedata", path);
        bool borrowed = false;
        if (taeBytes == null)
        {
            if (sounds.Count == 0) return 0;
            string b = Path.Combine(pack.Ds2Data, "timeact", "obj", BorrowedTae + ".tae");
            var probe = new Ds2Tae(File.ReadAllBytes(b));
            var need = sounds.Select(s => s.Ds2Anim).Distinct().ToList();
            if (!need.All(probe.Records.ContainsKey)) { pack.Note($"object sound (SND7) {ds1Obj} → {ds2Name}: no template TAE and {BorrowedTae}'s animations do not cover {string.Join(",", need)}: DS1 sounds not ported"); return 0; }
            taeBytes = File.ReadAllBytes(b);
            borrowed = true;
        }
        var tae = new Ds2Tae(taeBytes);
        var sf = TAE.Read(taeBytes); // exact parameter lengths of the events kept (SoulsFormats reads the headers in file order)
        var ds1Names = new Dictionary<string, int>(); // DS1 event → DS2 id
        int nextFree = 999, written = 0;
        int IdFor(string ds1Event, int id)
        {
            if (ds1Names.TryGetValue(ds1Event, out int have)) return have;
            int local = id / 100000 == ds1Model && id % 100000 < 1000 ? id % 1000 : -1;
            if (local < 0 || ds1Names.ContainsValue(local)) { while (ds1Names.ContainsValue(nextFree)) nextFree--; local = nextFree--; }
            return ds1Names[ds1Event] = local;
        }
        foreach (long animId in tae.Records.Keys.Order().ToList())
        {
            var full = tae.EventsFull(animId, 48);
            var groups = tae.Groups(animId);
            var keep = new List<(int, IReadOnlyList<Ds2Tae.NewEvent>)>();
            int dropped = 0;
            if (!borrowed)
                foreach (var (gt, idx) in groups)
                {
                    var sfEvents = sf.Animations.First(a => a.ID == animId).Events;
                    var evs = idx.Where(i => !Ds2SoundTypes.Contains(full[i].Type)).Select(i => full[i] with { Params = sfEvents[i].GetParameterBytes(false) }).ToList();
                    dropped += idx.Count - evs.Count;
                    if (evs.Count > 0) keep.Add((gt, evs));
                }
            else dropped = full.Count;
            foreach (var s in sounds.Where(s => s.Ds2Anim == animId))
            {
                string ds1Event = Events.MapSounds.Ds1Event(s.Type, s.Id);
                byte[] prm = new byte[16];
                if (s.Type == 3)
                {
                    int id = IdFor(ds1Event, s.Id);
                    BinaryPrimitives.WriteInt32LittleEndian(prm, id);
                    prm[4] = 100;
                    Register(new Import(ds1Event, $"o{model * 1000 + id:D9}", ds2Name));
                }
                else if (s.Type == 0)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(prm, s.Id);
                    prm[4] = 0xFF;
                }
                else { notes.Add($"{ds1Event} (DS1 type '{ds1Event[0]}') not ported"); continue; }
                prm[5] = 1;
                BinaryPrimitives.WriteInt16LittleEndian(prm.AsSpan(6), (short)(s.Dummy >= 0 ? s.Dummy : -1));
                keep.Add((Ds2SoundEvent, [new Ds2Tae.NewEvent(Ds2SoundEvent, 0, s.Start, s.End, prm)]));
                written++;
            }
            if (dropped == 0 && !sounds.Any(s => s.Ds2Anim == animId) && !borrowed) continue;
            tae.SetEventGroups(animId, keep);
        }
        pack.AddFile("gamedata", path, tae.Bytes);
        pack.Note($"object sound (SND7) {ds1Obj} → {ds2Name}: {written} DS1 sound events on {path}{(borrowed ? $" (borrowed {BorrowedTae}'s TAE, its own events dropped; P)" : " (template sounds dropped)")}: "
            + string.Join(", ", ds1Names.Select(kv => $"{kv.Key}→o{model * 1000 + kv.Value:D9}")) + (notes.Count > 0 ? "; " + string.Join("; ", notes.Distinct()) : ""));
        return written;
    }

    /// <summary>
    /// Session 11 (fx11), rule OBJ-SND1: a ported DS1 breakable's own break sound. DS1 names an object's break sound
    /// "o{model:D4}{00000}" (id 0 of the model's 'o' range: frpg_m18 defines o840000000 / o840300000 / o841000000 / o841100000
    /// for the Asylum's tubs and urns, frpg_m10 o100000000 … o132300000 for the Burg's crates and barrels; ObjectParam has no
    /// sound field). DS2's break controller (decomp FUN_1401d44d0 → FUN_1404a5760(…, kind 0, model)) plays "o%09d" of
    /// SoundObjectBreakParam[model].type (1..12 → DS2's generic o00000000t) when the model has a row, else of model × 1000
    /// (e.g. o002110000 for the skull o00_2110, which has no row). So a ported breakable gets no SoundObjectBreakParam row and
    /// DS1's event is copied (SND7's import route) under "o{DS2 model × 1000}" (o18_8410 → o188410000). Without the copy the
    /// break was silent (o188410000 undefined). A DS1 model without its own break event stays silent, as in DS1 (noted).
    /// Returns the DS2 event name, or null when nothing was registered.
    /// </summary>
    public static string? ApplyBreak(Pack pack, string ds1Obj, string ds2Name)
    {
        if (!Enabled) return null;
        int model = ModelNumber(ds2Name);
        string ds1Event = BreakEvent(ds1Obj), ds2Event = $"o{model * 1000:D9}";
        if (pack.Param("SoundObjectBreakParam")[model] is { } row)
        {
            pack.Note($"object break sound (OBJ-SND1) {ds1Obj} → {ds2Name}: SoundObjectBreakParam has a row {model} (type {row["type"].Value}): DS2 plays o00000000{row["type"].Value}, DS1's {ds1Event} not used");
            return null;
        }
        Register(new Import(ds1Event, ds2Event, ds2Name));
        pack.Note($"object break sound (OBJ-SND1) {ds1Obj} → {ds2Name}: DS1 {ds1Event} → {ds2Event} (DS2 break: o{{model×1000}}, no SoundObjectBreakParam row)");
        return ds2Event;
    }

    /// <summary>Rule CH2: the first time (s) any bone of a clip leaves its first-frame pose (rotation &gt; 1.5° or translation &gt;
    /// 1 cm): where a lid / leaf starts to move. Null when the clip never moves.</summary>
    public static float? MotionOnset(Formats.Havok.HkSplineAnim a)
    {
        var first = Enumerable.Range(0, a.TrackCount).Select(t => a.Sample(t, 0)).ToArray();
        for (int f = 1; f < a.NumFrames; f++)
            for (int t = 0; t < a.TrackCount; t++)
            {
                var s = a.Sample(t, f);
                float dot = MathF.Min(1, MathF.Abs(System.Numerics.Quaternion.Dot(System.Numerics.Quaternion.Normalize(first[t].R), System.Numerics.Quaternion.Normalize(s.R))));
                if (2 * MathF.Acos(dot) > 1.5f * MathF.PI / 180 || (s.T - first[t].T).Length() > 0.01f) return f * a.FrameDuration;
            }
        return null;
    }

    /// <summary>A DS2 object clip (Havok 2014 packfile) decoded for <see cref="MotionOnset"/>.</summary>
    public static Formats.Havok.HkSplineAnim Ds2Clip(byte[] hkx)
    {
        var pf = new Formats.Havok.HkPackfile(hkx);
        try { return Read(pf); }
        catch (Exception) { return Read(new Formats.Havok.HkPackfile(hkx).AdoptTypes(Commands.ScnDump.Donors())); }
        static Formats.Havok.HkSplineAnim Read(Formats.Havok.HkPackfile p)
        {
            var anim = p.Objects.First(o => o.Class == "hkaSplineCompressedAnimation");
            var bind = p.Objects.Where(o => o.Class == "hkaAnimationBinding").Select(o => p.ReadObject(o.Offset)).FirstOrDefault();
            return Formats.Havok.HkSplineAnim.FromPack(p.ReadObject(anim.Offset), bind);
        }
    }

    /// <summary>DS1's break sound name of a model (rule OBJ-SND1): "o" + 4-digit model + "00000".</summary>
    public static string BreakEvent(string ds1Obj) => $"o{int.Parse(ds1Obj[1..]):D4}00000";

    static void Register(Import i)
    {
        if (All.Any(x => x.Ds2Event == i.Ds2Event)) return;
        All.Add(i); Pending.Add(i);
    }
}
