namespace Lordran.Tools.Build.Events;

/// <summary>
/// DS2 SOTFS's event-flag storage, read from the executable (session 9, track npcflags; V-code = decompile + disassembly of
/// DarkSoulsII.exe 1.0.3.0, not yet seen in game except where noted). An emulation of the lookup every flag read/write
/// goes through, so a build can prove that each flag it references has storage.
///
/// The flag manager ([[GameManager+0x70]+0x20], object 0x128 bytes) keeps a 31-bucket hash table of **blocks**; a flag
/// id F lives in block F / 10000 at bit F % 10000 (byte = bit >> 3, bit 7 − (bit &amp; 7)); a block node = {data, size in
/// bytes, block id, next}. Get = FUN_140474230, set = FUN_1404750b0 (130101 → FUN_140474a60 → FUN_1404750b0): when the
/// block isn't in the table, or the byte index is ≥ the block's size, **a read returns 0 and a write is dropped
/// silently** (no error, no allocation). Blocks that exist:
///  - **global block 10 (flags 100000..109999) and global block 20 (200000..209999)**: 1250 bytes each, registered at
///    save load (Arxan-split chunks at 0x1407249dd / 0x14070e63d: FUN_140474db0(mgr, 10 | 20, save+8 | save+0x4ea, 0x4e2)),
///    stored in the save's flag section (+0x0 / +0x4e4), persistent;
///  - **per-map blocks**, registered when a map loads (map loader FUN_1403ca8d0 step 1 → FUN_14044fbb0 → FUN_1404745c0 →
///    FUN_140474db0 ×3, size 0x19 = 25 bytes; an id that is already registered is NOT re-pointed): ids P·100 + k with the
///    map prefix P = (AA / 10)·100 + BB of mAA_BB (m10_04 → 104, m18_01 → 101, m19_02 → 102, m20_21 → 221, m50_35 → 535),
///    k = 0, 1, 2 → flags P·10^6 + k·10^4 + 0..199 (**only 200 flags per block**: 101000000..101000199 exist, 101000312
///    does not):
///      k = 0: save + 0x9cc + index·25, index = the map's index in the map registry (WorldMapList), **only if index &lt; 42**
///             (FUN_1401855f0; 38 vanilla maps, ours appended: m18_01 = 38, m19_02 = 39) → persistent;
///      k = 1: one of 3 map slots (save +0xde6, ids at +0xe7c; FUN_140185670/FUN_140186050);
///      k = 2: the same slots (+0xe31; FUN_1401854f0) and also zeroed by the world reset (FUN_140474730: every block with
///             id % 100 == 2, called by FUN_14044f880 from FUN_14017fd70 next to the generator respawn FUN_140417210).
///    **Lifetime (fix pass): a plain unload does not remove them** (unload FUN_1403cb1a0 → FUN_14044fb60 → FUN_1404746a0,
///    an empty function). The loader keeps an MRU list of the last **3 loaded maps** (FUN_1401e75d0/FUN_1401e7560, list at
///    +0x18, stride 0x10): loading a 4th evicts the oldest (FUN_1401e7bb0 → GameManagerImp vtable +0x88 = FUN_1401c2080 →
///    FUN_14044f7a0 → FUN_1404746b0: its 3 blocks removed, its slot's k1/k2 zeroed and freed, FUN_140186480); FUN_1401e7760
///    removes all of them (world teardown FUN_1403bd4a0/FUN_1403bd960; a session-state case); loader step 1 with +0x1e4
///    set removes the map's own blocks before re-registering. So k1 lives while its map is among the last 3 loaded maps
///    (saved with them), k2 the same but also zeroed at the world reset. **Prefix aliasing:** while a map with the same
///    prefix is among the last 3 (Things Betwixt m10_02 and our Firelink host m19_02 are both 102), the second map's
///    blocks are the first map's (k0 = the first map's registry block, k1/k2 = its slot), and evicting either removes both.
///  - nothing else: no block for 109xxxxxx, 14xxxxxxx, 1017xxxxx … (s9-12: Oscar's 101700050 write did not stick).
/// **Online (fix pass):** FUN_140474a60 (the 130101 path) drops the write when a session exists, FUN_1405135f0() is true
/// (from its other callers: "this player is a guest in another world") and <see cref="Syncs"/> is false; a successful set
/// is broadcast (FUN_14051e6b0); the receiver FUN_14025ce10 ignores an incoming flag for which <see cref="Syncs"/> is false
/// when its sender check passes. <see cref="Syncs"/> (FUN_14025cdb0) is false exactly for ids &lt; 10^6 (or a multiple of
/// 10^9 above) whose block % 100 &gt; 2: both global blocks. So global flags are per player: offline and as host they
/// work, a guest's writes are dropped and they don't follow the host. Map-block flags (vanilla's doors, levers, boss
/// kills: BossBattleParam killedEventId = map k0) are synced.
/// Other facts: 130131(first, last, v) is NOT a flag range: it sets the flag of every **EventFlagListParam row** whose id is
/// in [first, last] (FUN_140474af0; vanilla's per-bonfire reset rows); NG+ keeps only the flags of EventFlagListParam rows
/// 10000000..10009999 (FUN_1404747c0 clears the save and re-sets them) — so every DS1 flag is cleared by NG+ as in DS1.
/// </summary>
public static class Ds2FlagStore
{
    public const int GlobalA = 10, GlobalB = 20, MapBlockBytes = 25, MapBlockFlags = MapBlockBytes * 8, GlobalFlags = 10000;
    /// <summary>Per-map persistent blocks exist only for registry indices below this (save + 0x9cc, 42 × 25 bytes).</summary>
    /// Session 11 (cap11, rule CAP1): with build-pack --map-cap N the loader gives indices 42..N-1 blocks of its own
    /// (src\loader\mapcap.c; kept in a side file, never in the vanilla save), so the build raises this to N.
    public static int MapSlotsWithPersistentBlock { get; set; } = 42;

    public enum Kind { None, Global, MapPersistent, MapSlot, MapReset }

    /// <summary>The map prefix P of a DS2 map "mAA_BB…" or decimal id AABBCCDD: (AA / 10)·100 + BB.</summary>
    public static int Prefix(string ds2Map) => int.Parse(ds2Map.Substring(1, 2)) / 10 * 100 + int.Parse(ds2Map.Substring(4, 2));
    public static int Prefix(int ds2MapId) => ds2MapId / 1000000 / 10 * 100 + ds2MapId / 10000 % 100;

    /// <summary>The first flag of map block k of a DS2 map: P·10^6 + k·10^4.</summary>
    public static int MapBlockBase(string ds2Map, int k) => Prefix(ds2Map) * 1_000_000 + k * 10_000;

    /// <summary>
    /// Storage a flag has while <paramref name="loadedMaps"/> are loaded (FUN_140474230's lookup). Kind.None = reads 0,
    /// writes dropped. <paramref name="registryIndex"/> gives a map's WorldMapList index (k = 0 needs index &lt; 42).
    /// </summary>
    public static Kind Lookup(long flag, IEnumerable<string> loadedMaps, Func<string, int>? registryIndex = null)
    {
        if (flag <= 0 || flag > uint.MaxValue) return Kind.None;
        long block = flag / 10000, bit = flag % 10000;
        if (block is GlobalA or GlobalB) return Kind.Global;
        if (bit >= MapBlockFlags) return Kind.None;
        foreach (string m in loadedMaps)
        {
            long b0 = Prefix(m) * 100L;
            if (block < b0 || block > b0 + 2) continue;
            int k = (int)(block - b0);
            if (k == 0 && registryIndex != null && (registryIndex(m) is int ri && (ri < 0 || ri >= MapSlotsWithPersistentBlock))) return Kind.None;
            return k switch { 0 => Kind.MapPersistent, 1 => Kind.MapSlot, _ => Kind.MapReset };
        }
        return Kind.None;
    }

    /// <summary>FUN_14025cdb0: does DS2 sync this flag online (a guest may write it; an incoming message sets it)? False
    /// for (flag / 10^6) % 1000 == 0 and (flag / 10^4) % 100 &gt; 2, i.e. both global blocks 10 and 20.</summary>
    public static bool Syncs(long flag)
    {
        uint f = (uint)flag;
        return !(f / 1_000_000 % 1000 == 0 && f / 10_000 % 100 > 2);
    }

    /// <summary>Vanilla DS2 maps (data\ds2\map) whose prefix equals <paramref name="ds2Map"/>'s: their blocks alias ours
    /// while one of them is among the last 3 loaded maps (see the class summary).</summary>
    public static IEnumerable<string> SamePrefixVanilla(string ds2Map, string vanillaMapDir)
    {
        if (!Directory.Exists(vanillaMapDir)) yield break;
        int p = Prefix(ds2Map);
        foreach (string d in Directory.GetDirectories(vanillaMapDir, "m??_??_??_??").Select(x => Path.GetFileName(x)).Order())
            if (d != ds2Map && Prefix(d) == p) yield return d;
    }

    /// <summary>Lifetime text for reports.</summary>
    public static string Describe(Kind k) => k switch
    {
        Kind.Global => "global (save, persistent; per player online: not synced)",
        Kind.MapPersistent => "map block 0 (save by registry index < 42, persistent)",
        Kind.MapSlot => "map block 1 (one of 3 map slots: kept while the map is among the last 3 loaded, zeroed when evicted)",
        Kind.MapReset => "map block 2 (as block 1, and zeroed by the world reset at bonfire rest)",
        _ => "NO STORAGE (read 0, write dropped)",
    };
}
