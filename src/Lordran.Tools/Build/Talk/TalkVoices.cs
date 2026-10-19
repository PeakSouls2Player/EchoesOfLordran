using SoulsFormats;

namespace Lordran.Tools.Build.Talk;

/// <summary>
/// DS1 NPC voices for translated talks (session 8, track npc; opt-in `build-pack --ds1-voices`, status U: needs an
/// in-game run). Formats, both FMOD Ex / FMOD Designer:
///  - DS1 (DSR sound\): per area a speech project frpg_sm{AA}.fev (FEV1, group "sm{AA}", category Voice) with one event
///    per line named "v%09d" of TalkParam.voiceId (Oscar 812000000 → event v812000000, Firelink 37000000 → v037000000),
///    whose sound definition /frpg_sm{AA}/v… plays wave bank/frpg_sm{AA}/v…/v….wav from frpg_sm{AA}.fsb (FSB4, MP3 mono
///    44.1 kHz; sm18 382 samples, 360 voice events);
///  - DS2: per map a speech bank sound/frpg2_sm{AA}{BB}.bnd (a stream placeholder .dm naming the project; the FSB5 Vorbis
///    frpg2_sm{AABB}.fsb loose in the game's sound folder) whose project in frpg2.fevbnd has one event per line named
///    "v%02d%02d%08d" (map AA, BB, NpcTalkParam.voiceId: decomp FUN_1404a5f20 builds it; Saulden 74100000 in m10_04 →
///    v100474100000, sound definition /frpg2_sm1004/v100474100000, wave bank/frpg2_sm1004/v7410/…); unvoiced lines use
///    voiceId 99999999 with a silent sample v{AABB}99999999 (119 vanilla rows).
/// Port: DS2 loads DS1's FEV1 projects and FSB4 banks (Atmosphere.SoundBanks, `--ds1-sound`, puts frpg_sm{AA}.fev into
/// frpg2.fevbnd, frpg2_sm{AABB}.bnd naming frpg_sm{AA}.dm, the FSB loose under title:/sound), so the only mismatch is
/// the event name. This renames DS1's voice events in a copy of the project: "v%09d" → "v{AABB}%08d" (NpcTalkParam keeps
/// DS1's voiceId). FEV1 stores names inline as u32 length (with NUL) + bytes; renaming shifts the bytes after it, and
/// the two header counts after the version (sm18: 18320 / 21376) are per-wave memory sizes (they differ by exactly
/// 8 × waves in sm18/sm11/sm12/m18/smain/xm10: 382, 23, 184, 58, 6, 1), not offsets or string sizes, so they stay.
/// Only exact length-prefixed names followed by a 16-byte GUID (FEV1 event header) are renamed; sound definition and
/// wave paths ("/frpg_sm18/v…", "bank/…") keep DS1's names. Rule FH2 (chrint stage-4 fix pass): before renaming, the
/// copy keeps only the voice events the talks use, plus the non-voice events when DS1 sound regions can name them
/// (--ds1-sound) (Fev1Project.KeepEvents), so the
/// fevbnd-resident cost of a DS1 speech project scales with the lines DS2 can request, not with DS1's whole area.
/// Limits: one DS1 speech project serves one DS2 map (area 10's frpg_sm10 holds Firelink, the Burg and the Depths: a
/// second DS2 map from area 10 would need its own copy under another project name).
/// </summary>
public static class TalkVoices
{
    public static List<string> Build(Pack pack, string dsrRoot, string ds1Map, string ds2Map, IReadOnlyCollection<int> voiceIds, bool keepNonVoice = false)
    {
        var log = new List<string>();
        string area = ds1Map.Substring(1, 2), aabb = ds2Map.Substring(1, 2) + ds2Map.Substring(4, 2);
        string proj = $"frpg_sm{area}", snd = Path.Combine(dsrRoot, "sound");
        string fevPath = Path.Combine(snd, proj + ".fev"), fsbPath = Path.Combine(snd, proj + ".fsb");
        if (!File.Exists(fevPath) || !File.Exists(fsbPath)) { log.Add($"voices: no DS1 speech project {proj}"); return log; }

        var renames = voiceIds.Where(v => v > 0 && v != 99999999).Distinct()
            .ToDictionary(v => $"v{v:D9}", v => $"v{aabb}{v:D8}");
        // rule FH2 (chrint stage-4 fix pass): only the lines the talks use go into the fevbnd. DS2 names a speech event
        // only as v{AABB}%08d of a talk's voiceId (FUN_1404a5f20); the DS1 lines no translated talk uses keep DS1's
        // v%09d and no DS2 request can reach them (EMEVD 2010:2 PlaySE is dropped by the translator), but every project in
        // frpg2.fevbnd is resident in every map (sm18 + sm10 whole: +5.26 MB of FMOD heap, Majula 66.70 → 71.98 MB; FMOD
        // keeps ≈ 2.55 KB per loaded event: work\fh1\projmem.py). The project's other events (sm18: 4 a / 4 m / 1 o / 13 p,
        // sm10: 3 a / 5 m / 3 o / 10 p: streamed BGM, ambience, cutscene sounds) keep DS1's names, which DS2's own
        // "%c%09d" requests reach only through DS1 sound regions (--ds1-sound: Atmosphere.SoundRegions places them with
        // DS1's ids; cutscene sounds are not ported, RemoConverter): kept exactly when the map is built with --ds1-sound.
        // the fevbnd is shared with Atmosphere.SoundBanks: strip + rename its copy of the same project when it has one, so
        // whatever the map build did to that copy stays (in chrint that copy is DSR's original, byte for byte)
        byte[] fevBytes = pack.TryGetFile("gamedata", "sound/frpg2.fevbnd.dcx") ?? File.ReadAllBytes(Path.Combine(pack.Ds2Data, "sound", "frpg2.fevbnd.dcx"));
        var fevbnd = BND4.Read(DCX.Decompress(fevBytes, out DCX.CompressionInfo dcx));
        string name = $@"N:\FRPG2_64\SoundOutTemp\win64\{proj}\{proj}.fev";
        var existing = fevbnd.Files.FirstOrDefault(f => f.Name == name);
        // session 11 (burg11), rule SND4v: the area's speech project already serves ANOTHER DS2 map (its copy is stripped to
        // that map's lines and renamed v{other AABB}...): this map gets its own copy of DSR's project under its own project
        // name {proj}_{AABB} (voice events only: no non-voice name is defined twice), wave bank still DS1's (the loose FSB is
        // shared), and its speech bank names that project
        // (a byte scan: the stripped copy's header keeps DSR's event count, which Fev1Project.Read refuses)
        if (existing != null && System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(existing.Bytes), "v(\\d{12})\0").Any(m => !m.Groups[1].Value.StartsWith(aabb)))
        {
            string own = $"{proj}_{aabb}";
            var src = Formats.Fev1Project.Read(File.ReadAllBytes(fevPath));
            byte[] strip = src.KeepEvents(src.Events.Select(e => e.Name).Where(n => n[0] == 'v' && renames.ContainsKey(n)).ToHashSet()) ?? File.ReadAllBytes(fevPath);
            byte[] copy = RenameProject(Rename(strip, renames, out var done2), own);
            string ownName = $@"N:\FRPG2_64\SoundOutTemp\win64\{own}\{own}.fev";
            fevbnd.Files.RemoveAll(f => f.Name == ownName);
            fevbnd.Files.Add(new BinderFile(fevbnd.Files[0].Flags, fevbnd.Files.Max(f => f.ID) + 1, ownName, copy));
            pack.AddFile("gamedata", "sound/frpg2.fevbnd.dcx", DCX.Compress(fevbnd.Write(), dcx));
            byte[] smRaw2 = File.ReadAllBytes(Path.Combine(pack.Ds2Data, "sound", "frpg2_sm1002.bnd"));
            DCX.CompressionInfo? smInfo2 = null;
            if (DCX.Is(smRaw2)) { smRaw2 = DCX.Decompress(smRaw2, out DCX.CompressionInfo info2); smInfo2 = info2; }
            var smBnd2 = BND4.Read(smRaw2);
            var dummy2 = smBnd2.Files[0];
            smBnd2.Files.Clear();
            smBnd2.Files.Add(new BinderFile(dummy2.Flags, dummy2.ID, $@"N:\FRPG2_64\SoundOutTemp\win64\{own}\{own}.dm", dummy2.Bytes));
            byte[] smOut2 = smBnd2.Write();
            pack.AddFile("gamedata", $"sound/frpg2_sm{aabb}.bnd", smInfo2 != null ? DCX.Compress(smOut2, smInfo2) : smOut2);
            pack.AddFile("title", $"sound/{proj}.fsb", File.ReadAllBytes(fsbPath));
            log.Add($"voices: rule SND4v: {proj} already serves another map; own project {own}.fev ({done2.Count} of {renames.Count} voice events renamed v%09d → v{aabb}%08d, voice events only, wave bank {proj} shared), frpg2_sm{aabb}.bnd → {own}.dm");
            return log;
        }
        byte[] ds1Fev = existing?.Bytes ?? File.ReadAllBytes(fevPath);
        var ds1Proj = Formats.Fev1Project.Read(ds1Fev);
        var keep = ds1Proj.Events.Select(e => e.Name).Where(n => n[0] == 'v' ? renames.ContainsKey(n) : keepNonVoice).ToHashSet();
        byte[]? kept = ds1Proj.KeepEvents(keep);
        if (kept != null)
        {
            var kp = Formats.Fev1Project.Read(kept);
            log.Add($"voices: {proj}.fev stripped to the talks' lines{(keepNonVoice ? " + its non-voice events (--ds1-sound)" : "")} (rule FH2): {kp.Events.Count} of {ds1Proj.Events.Count} events, {kp.SoundDefs.Count} of {ds1Proj.SoundDefs.Count} sound definitions, {kp.ConfigCount} of {ds1Proj.ConfigCount} configs ({ds1Fev.Length:N0} → {kept.Length:N0} bytes)");
        }
        else log.Add($"voices: {proj}.fev NOT stripped (a used event is neither simple nor S5c-layered, or none is used): whole project kept");
        byte[] fev = Rename(kept ?? ds1Fev, renames, out var done);
        var missing = renames.Keys.Except(done).ToList();
        log.Add($"voices: {proj}.fev: {done.Count} of {renames.Count} voice events renamed v%09d → v{aabb}%08d ({fev.Length - ds1Fev.Length:+0;-0} bytes){(existing != null ? " on the map sound copy" : "")}{(missing.Count > 0 ? $"; no event for {string.Join(", ", missing.Take(8))}" : "")}");

        if (existing != null) existing.Bytes = fev;
        else fevbnd.Files.Add(new BinderFile(fevbnd.Files[0].Flags, fevbnd.Files.Max(f => f.ID) + 1, name, fev));
        pack.AddFile("gamedata", "sound/frpg2.fevbnd.dcx", DCX.Compress(fevbnd.Write(), dcx));

        // the map's speech bank: TB's stream placeholder renamed to the DS1 project (as Atmosphere.SoundBanks does)
        byte[] smRaw = File.ReadAllBytes(Path.Combine(pack.Ds2Data, "sound", "frpg2_sm1002.bnd"));
        DCX.CompressionInfo? smInfo = null;
        if (DCX.Is(smRaw)) { smRaw = DCX.Decompress(smRaw, out DCX.CompressionInfo info); smInfo = info; }
        var smBnd = BND4.Read(smRaw);
        var dummy = smBnd.Files[0];
        smBnd.Files.Clear();
        smBnd.Files.Add(new BinderFile(dummy.Flags, dummy.ID, $@"N:\FRPG2_64\SoundOutTemp\win64\{proj}\{proj}.dm", dummy.Bytes));
        byte[] smOut = smBnd.Write();
        pack.AddFile("gamedata", $"sound/frpg2_sm{aabb}.bnd", smInfo != null ? DCX.Compress(smOut, smInfo) : smOut);
        pack.AddFile("title", $"sound/{proj}.fsb", File.ReadAllBytes(fsbPath));
        log.Add($"voices: frpg2_sm{aabb}.bnd → {proj}.dm, loose title/sound/{proj}.fsb ({new FileInfo(fsbPath).Length:N0} bytes)");
        return log;
    }

    /// <summary>Session 11 (rule SND4v): the FEV1 project's own name (the first string at 0x14: u32 length incl. NUL + ASCII +
    /// NUL) replaced; the bytes after it shift (FEV1 has no absolute offsets after the header, Atmosphere.RenameLstrs).</summary>
    public static byte[] RenameProject(byte[] fev, string name)
    {
        if (fev.Length < 0x18 || fev[0] != (byte)'F' || fev[1] != (byte)'E' || fev[2] != (byte)'V' || fev[3] != (byte)'1')
            throw new InvalidDataException("not an FMOD Designer FEV1 project");
        // FEV1: "FEV1", version (u32), two u32 sizes (0x08 / 0x0C), then at 0x10 the project name as u32 length + bytes + NUL
        int at = 0x10;
        int len = BitConverter.ToInt32(fev, at);
        if (len < 2 || at + 4 + len > fev.Length || fev[at + 4 + len - 1] != 0) throw new InvalidDataException("FEV1 project name not found at 0x10");
        var bytes = System.Text.Encoding.ASCII.GetBytes(name);
        return [.. fev.AsSpan(0, at).ToArray(), .. BitConverter.GetBytes(bytes.Length + 1), .. bytes, 0, .. fev.AsSpan(at + 4 + len).ToArray()];
    }

    /// <summary>Renames FEV1 event names (u32 length incl. NUL + ASCII + NUL, then a 16-byte GUID).</summary>
    public static byte[] Rename(byte[] fev, IReadOnlyDictionary<string, string> renames, out HashSet<string> done)
    {
        done = [];
        if (fev.Length < 4 || fev[0] != (byte)'F' || fev[1] != (byte)'E' || fev[2] != (byte)'V' || fev[3] != (byte)'1')
            throw new InvalidDataException("not an FMOD Designer FEV1 project");
        var o = new List<byte>(fev.Length + renames.Count * 8);
        int i = 0;
        while (i < fev.Length)
        {
            if (i + 4 <= fev.Length)
            {
                int len = BitConverter.ToInt32(fev, i);
                if (len is >= 2 and <= 64 && i + 4 + len + 16 <= fev.Length && fev[i + 4 + len - 1] == 0)
                {
                    string s = System.Text.Encoding.ASCII.GetString(fev, i + 4, len - 1);
                    if (s.Length > 0 && s[0] == 'v' && renames.TryGetValue(s, out var to) && s.All(ch => ch == 'v' || char.IsAsciiDigit(ch)))
                    {
                        var bytes = System.Text.Encoding.ASCII.GetBytes(to);
                        o.AddRange(BitConverter.GetBytes(bytes.Length + 1));
                        o.AddRange(bytes);
                        o.Add(0);
                        done.Add(s);
                        i += 4 + len;
                        continue;
                    }
                }
            }
            o.Add(fev[i++]);
        }
        return o.ToArray();
    }
}
