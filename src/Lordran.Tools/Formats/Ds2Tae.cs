using System.Buffers.Binary;

namespace Lordran.Tools.Formats;

/// <summary>
/// In-place editor for DS2 SOTFS TimeAct files (<c>timeact/chr/cNNNNNN_pl|sfx|snd.tae</c>), session 8 (track chr step 2).
/// SoulsFormats reads them but its writer gives another layout (tae-roundtrip: 0/696 vanilla files byte-identical), so the
/// 1:1 pipeline edits the vanilla bytes in place and never moves anything. Layout (64-bit, from SoulsFormats' reader and
/// checked against c103000_pl.tae): header u32 animation count at 0x54, u64 offset of the animation table at 0x58; table
/// entries {u64 animation id, u64 record offset}; record {u64 event headers, u64 event groups, u64 times, u64 anim file
/// struct, i32 event count, i32 group count, i32 time count, i32 0}; event header {u64 start-time ptr, u64 end-time ptr,
/// u64 event data ptr}; times = the record's own f32 table; anim file struct {u32 type (0 = standard), u32 0, u64 p (= +0x10
/// or 0), at p: u64 name ptr, u8 loop, u8 30, u8, u8, i32 length}. The i32 is the clip length in frames at 30 fps (150/150
/// c103000_pl animations equal round(NSA duration × 30) of the clip their TimeAct track names, session 8); the byte before
/// is 30 (the frame rate, read by SoulsFormats as a bool).
/// <para>Track chrsound (session 8): <see cref="SetEvents"/> gives an animation a new event list. Nothing in a TAE points
/// at the end of the file (the header's u32 at 0x0C is the file size; offsets are absolute; no relocation table), so the new
/// times, event headers, event data and group are appended and the record is pointed at them; the old bytes stay as
/// unreferenced data, as <see cref="ClearEvents"/> leaves them. The appended block copies the vanilla layout of an
/// animation with events (c103000_snd 10011, survey of all 696 vanilla TAEs): time table (unique f32, 16-aligned), event
/// headers (24 B each), per event {i32 type, i32 unk, u64 params ptr = data + 16, params padded to 16 B}, one event group
/// {u64 event count, u64 ptr to the event-header pointer list, u64 ptr to the group data, u64 0} + group data {i32 group
/// type, i32 0, u64 ptr = group data + 16, 16 zero bytes} + the list of event-header pointers. Every vanilla event sits in
/// a group (18,320 _snd / 293 _sfx animations with events: groups cover all their events); _snd groups have type 200
/// (31,240 groups of 201 events, 401 of 202), _sfx groups type 2100 (2100-2104 events).</para>
/// </summary>
public sealed class Ds2Tae
{
    public byte[] Bytes { get; private set; }
    public readonly Dictionary<long, int> Records = [];   // animation id → record offset

    /// <summary>One event for <see cref="SetEvents"/>: type, the data block's second i32 (vanilla: 1 on every 201, 0 on
    /// 202 and the _sfx types), start/end time in seconds, parameter bytes (padded to 16 when written).</summary>
    public sealed record NewEvent(int Type, int Unk04, float Start, float End, byte[] Params);

    public Ds2Tae(byte[] bytes)
    {
        Bytes = (byte[])bytes.Clone();
        if (BinaryPrimitives.ReadUInt32LittleEndian(Bytes) != 0x20454154) throw new InvalidDataException("not a TAE");
        int n = BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(0x54));
        int tab = (int)BinaryPrimitives.ReadInt64LittleEndian(Bytes.AsSpan(0x58));
        for (int i = 0; i < n; i++)
            Records[BinaryPrimitives.ReadInt64LittleEndian(Bytes.AsSpan(tab + 16 * i))] = (int)BinaryPrimitives.ReadInt64LittleEndian(Bytes.AsSpan(tab + 16 * i + 8));
    }

    long L(int o) => BinaryPrimitives.ReadInt64LittleEndian(Bytes.AsSpan(o));
    int I(int o) => BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(o));

    public int EventCount(long anim) => Records.TryGetValue(anim, out int r) ? I(r + 32) : 0;

    /// <summary>The event types and times of an animation (for reports).</summary>
    public List<(int Type, float Start, float End)> Events(long anim) =>
        EventsFull(anim).Select(e => (e.Type, e.Start, e.End)).ToList();

    /// <summary>The events of an animation with their unk i32 and <paramref name="paramBytes"/> parameter bytes (checks).</summary>
    public List<NewEvent> EventsFull(long anim, int paramBytes = 32)
    {
        var list = new List<NewEvent>();
        if (!Records.TryGetValue(anim, out int r)) return list;
        int pEv = (int)L(r), n = I(r + 32);
        for (int k = 0; k < n; k++)
        {
            int h = pEv + 24 * k, d = (int)L(h + 16), p = (int)L(d + 8);
            byte[] prm = Bytes.AsSpan(p, Math.Min(paramBytes, Bytes.Length - p)).ToArray();
            list.Add(new NewEvent(I(d), I(d + 4), BitConverter.ToSingle(Bytes, (int)L(h)), BitConverter.ToSingle(Bytes, (int)L(h + 8)), prm));
        }
        return list;
    }

    /// <summary>Length field (frames at 30 fps) of the animation's standard header, or null if it has none.</summary>
    int? LengthOffset(long anim)
    {
        if (!Records.TryGetValue(anim, out int r)) return null;
        int pf = (int)L(r + 24);
        if (pf == 0 || I(pf) != 0) return null;           // not a standard header
        int p = (int)L(pf + 8);
        return p == 0 ? null : p + 12;
    }

    public int? Length(long anim) => LengthOffset(anim) is int o ? I(o) : null;

    public void SetLength(long anim, int frames30)
    {
        if (LengthOffset(anim) is int o) BinaryPrimitives.WriteInt32LittleEndian(Bytes.AsSpan(o), frames30);
    }

    /// <summary>Session 11 (merge11, boulder WE4 defect 2): set event <paramref name="index"/>'s end time to <paramref name="end"/>.
    /// Times are shared table entries, so the new time is appended (16-aligned) and only this event's end pointer moves.</summary>
    public void SetEventEnd(long anim, int index, float end)
    {
        if (!Records.TryGetValue(anim, out int r) || index < 0 || index >= I(r + 32)) return;
        int h = (int)L(r) + 24 * index;
        var o = new List<byte>(Bytes);
        while (o.Count % 16 != 0) o.Add(0);
        int at = o.Count;
        o.AddRange(BitConverter.GetBytes(end));
        while (o.Count % 16 != 0) o.Add(0);
        Bytes = [.. o];
        BinaryPrimitives.WriteInt64LittleEndian(Bytes.AsSpan(h + 8), at);
        BinaryPrimitives.WriteInt32LittleEndian(Bytes.AsSpan(0x0C), Bytes.Length);
    }

    /// <summary>Drop every event of the animation (counts and pointers zeroed like vanilla event-less animations; the old
    /// event bytes stay as unreferenced data).</summary>
    public void ClearEvents(long anim)
    {
        if (!Records.TryGetValue(anim, out int r)) return;
        for (int k = 0; k < 3; k++) BinaryPrimitives.WriteInt64LittleEndian(Bytes.AsSpan(r + 8 * k), 0);
        BinaryPrimitives.WriteInt32LittleEndian(Bytes.AsSpan(r + 32), 0);
        BinaryPrimitives.WriteInt32LittleEndian(Bytes.AsSpan(r + 36), 0);
        BinaryPrimitives.WriteInt32LittleEndian(Bytes.AsSpan(r + 40), 0);
    }

    /// <summary>
    /// Replace the animation's events with <paramref name="events"/> (none = <see cref="ClearEvents"/>), all in one event
    /// group of <paramref name="groupType"/> (200 for _snd, 2100 for _sfx). Appends the vanilla block layout (class remarks)
    /// at the end of the file, points the record at it and updates the file size at 0x0C.
    /// </summary>
    public void SetEvents(long anim, IReadOnlyList<NewEvent> events, int groupType) => SetEventGroups(anim, [(groupType, events)]);

    /// <summary>Session 11 (snd11, rule SND7): the animation's event groups as (group type, indices into
    /// <see cref="EventsFull"/>), in file order (DS2 object TAEs: one 502000 group per sound event, 2100 for 2104 effects).</summary>
    public List<(int GroupType, List<int> Events)> Groups(long anim)
    {
        var list = new List<(int, List<int>)>();
        if (!Records.TryGetValue(anim, out int r)) return list;
        int pEv = (int)L(r), pGr = (int)L(r + 8), nE = I(r + 32), nG = I(r + 36);
        for (int g = 0; g < nG; g++)
        {
            int gg = pGr + 32 * g, c = (int)L(gg), pIdx = (int)L(gg + 8), pData = (int)L(gg + 16);
            var idx = new List<int>();
            for (int j = 0; j < c; j++) { int h = (int)L(pIdx + 8 * j); if (h >= pEv && (h - pEv) % 24 == 0 && (h - pEv) / 24 < nE) idx.Add((h - pEv) / 24); }
            list.Add((pData != 0 ? I(pData) : 0, idx));
        }
        return list;
    }

    /// <summary>
    /// Session 11 (snd11, rule SND7): <see cref="SetEvents"/> with several event groups, each (group type, its events), in
    /// order. One group gives exactly <see cref="SetEvents"/>' bytes (times, headers, event data, then the group, its data
    /// and its event-pointer list); more groups reserve all group records first, then each group's data + pointer list.
    /// </summary>
    public void SetEventGroups(long anim, IReadOnlyList<(int GroupType, IReadOnlyList<NewEvent> Events)> groups)
    {
        if (!Records.TryGetValue(anim, out int r)) return;
        groups = groups.Where(g => g.Events.Count > 0).ToList();
        var events = groups.SelectMany(g => g.Events).ToList();
        if (events.Count == 0) { ClearEvents(anim); return; }
        var o = new List<byte>(Bytes);
        void Pad16() { while (o.Count % 16 != 0) o.Add(0); }
        void U32(int at, int v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, v); for (int i = 0; i < 4; i++) o[at + i] = b[i]; }
        void U64(int at, long v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteInt64LittleEndian(b, v); for (int i = 0; i < 8; i++) o[at + i] = b[i]; }
        int Reserve(int n) { int at = o.Count; for (int i = 0; i < n; i++) o.Add(0); return at; }
        Pad16();
        // time table: every distinct start/end time once, ascending (vanilla shares one entry between events)
        var times = events.SelectMany(e => new[] { e.Start, e.End }).Distinct().Order().ToList();
        int pT = o.Count;
        foreach (float t in times) o.AddRange(BitConverter.GetBytes(t));
        Pad16();
        int pEv = Reserve(24 * events.Count);
        Pad16();
        var headers = new List<int>();
        for (int k = 0; k < events.Count; k++)
        {
            var e = events[k];
            int d = Reserve(16);
            U32(d, e.Type); U32(d + 4, e.Unk04); U64(d + 8, d + 16);
            o.AddRange(e.Params);
            Pad16();
            int h = pEv + 24 * k;
            U64(h, pT + 4 * times.IndexOf(e.Start)); U64(h + 8, pT + 4 * times.IndexOf(e.End)); U64(h + 16, d);
            headers.Add(h);
        }
        int pGr = Reserve(32 * groups.Count);
        int first = 0;
        for (int g = 0; g < groups.Count; g++)
        {
            int n = groups[g].Events.Count;
            int pData = Reserve(32), pIdx = Reserve(8 * n);
            U64(pGr + 32 * g, n); U64(pGr + 32 * g + 8, pIdx); U64(pGr + 32 * g + 16, pData);
            U32(pData, groups[g].GroupType); U64(pData + 8, pData + 16);
            for (int k = 0; k < n; k++) U64(pIdx + 8 * k, headers[first + k]);
            first += n;
        }
        Pad16();
        U64(r, pEv); U64(r + 8, pGr); U64(r + 16, pT);
        U32(r + 32, events.Count); U32(r + 36, groups.Count); U32(r + 40, times.Count);
        U32(0x0C, o.Count);
        Bytes = [.. o];
    }

    readonly HashSet<int> scaledTimes = [];

    /// <summary>Multiply every time of the animation's own time table by <paramref name="ratio"/> (each table entry once,
    /// even if two animations shared one).</summary>
    public void ScaleTimes(long anim, double ratio)
    {
        if (!Records.TryGetValue(anim, out int r)) return;
        int pT = (int)L(r + 16), n = I(r + 40);
        for (int k = 0; k < n; k++)
        {
            int o = pT + 4 * k;
            if (!scaledTimes.Add(o)) continue;
            float t = BitConverter.ToSingle(Bytes, o);
            BinaryPrimitives.WriteSingleLittleEndian(Bytes.AsSpan(o), (float)(t * ratio));
        }
    }
}
