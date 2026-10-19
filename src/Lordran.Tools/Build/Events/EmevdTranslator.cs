using SoulsFormats;

namespace Lordran.Tools.Build.Events;

/// <summary>
/// DS1 EMEVD → DS2 event ESD, by rule (docs/translation-rules.md §4). Each DS1 event, once per Initialize Event slot
/// with its arguments substituted, becomes one ESD state group: DS1's sequential instructions become a chain of states
/// (state 0 → 1 first: DS2 never runs state 0's entry commands); DS1 condition registrations (IF … into AND/OR groups)
/// are collected and compiled where DS1 waits (IF … into MAIN) or tests (SKIP/END IF condition group state) into one DS2
/// expression over direct functions (f6 flag, f3 state time) and group conditions (entry command 140xxx(slot, …) read
/// by f140001(slot)); SKIP/END become branches, Restart goes back to state 1, End sets the event's own flag (DS1: an
/// event id is also its flag) and stops. Arguments are decoded with DS1's instruction definitions (DarkScript3 EMEDF:
/// DSR stores even byte-sized arguments in 4-byte slots, so offsets come from the types, never hard-coded).
///
/// An event that uses any instruction without a rule is not translated at all; <see cref="Report"/> lists it with the
/// missing rules, so a half-working script never ships. Map-specific knowledge (entities, regions, texts, object
/// animation → prefab state, generators, boss rows, item lots, hit groups) comes from <see cref="IContext"/>.
///
/// Session 8 rules (evidence in the vanilla DS2 scripts, `lr.cmd esd-usage` / `edd-machines`; EDD preset parameter names
/// in brackets):
///  - characters by generator (<see cref="EntityRules.Generator"/>): 2004:5 enable → 131731(gen, 0|1) 「指定の敵の一時停止」
///    (m10_15 hides enemies behind a door with it); 2004:4 force death → 131721(gen, 1) 「敵の強制削除」(ジェネレータID,
///    死亡扱いにするか); 2004:41 short warp → 131704(gen, point) 「初期開始位置上書き（生成済みの場合はワープ）」, the player →
///    132121(point); 4:0 dead → 140111(slot, gen) 「キャラ死亡判定」 (alive: == 0); 4:2 HP ratio → 140121(slot, gen,
///    percent, cmp) 「HP割合比較」 (m10_16 「ボスのHPが7割以下になったか？」 = (gen, 70, 5): DS2's comparison codes are DS1's,
///    5 = &lt;=; DS1's ratio 0..1 → percent);
///  - absent characters (no generator placed yet: the 1:1 pipeline hasn't ported them): commands on them are no-ops and
///    conditions on them never pass; an event that warps or fights a BOSS (DS1 GameAreaParam row: 2004:41, 2003:11,
///    2003:12) needs the boss and is not translated while it is absent (no softlocking lock-ins, no kill flags without a
///    kill). Session 10 (asy-items i2), rule K1: a death/HP test (4:0, 4:2) on an absent boss no longer keeps the event
///    out: like any absent character's it is a condition that never passes (Never);
///  - bosses: 2003:11 HP bar on/off → 132201/132204(BossBattleParam) 「ボス戦開始」/「ボス戦キャンセル」 (m10_14's
///    prelude fight shows and hides the bar with exactly these); 2003:12 Handle Boss Defeat → 132202 「ボス戦終了」; the
///    row comes from DS1's GameAreaParam / NpcParam (<see cref="IContext.BossBattle"/>); 2004:1 AI state on a boss → no-op
///    (DS2 stops boss generators outside a boss battle: GeneratorParam.bossBattleStopDisable);
///  - items: 2003:36 / 2003:4 award → 131402(lot, 1) 「アイテム追加」(アイテム抽選ID) with our ItemLotParam2_Other row;
///  - objects: 5:0 destroyed → 140202(slot, inst, 1) 「OBJ破壊状態判定」; 2005:1 destruction → 131613(inst, inst, 0)
///    「OBJを破壊する（インスタンスID指定）」; 2005:2 restoration → 131652(inst) 「OBJのステートを初期化」; 2005:4 treasure
///    state → 131411(inst, 0|1) 「OBJのアイテム有効化」; 2005:9 damaging object → the object's own instance row
///    (damageId0, FoFG's rolling rock) when the context says so;
///  - collision: 2011:1 → 132153(hit group, 0|1) 「マップヒットの当たり有効化」 when the map builder gave the collision its
///    own hit group;
///  - ObjAct key doors: 5:2 on an ObjAct with a key/SpEffect requirement → the event is owned by the DS2 door and waits on
///    131602(region, item, 1005, fail text, state flag, 0) (FoFG 【Lib】アイテム指定扉開錠 / 領域指定扉開錠); DS1's own "Used
///    %s" dialog after it is dropped (DS2's door shows mapevent 1005 itself);
///  - fog walls: an action button whose line segment is a Light Wall with a DS2 white door = traversing that white door;
///    DS2 has no "traversed" condition, so the wait is a crossing detector (player in the box on the button's side, then
///    in the box on the far side); the player's warp onto the button area, the rotation and the walk-through anim 7410
///    are dropped (the white door plays its own traversal);
///  - 2002:4 cutscene + warp to another map → 130613(1) + 132125(0, 0, map, point, 0) (FoFG's memory warps; the cutscene
///    itself is dropped until a remo converter exists); same-map cutscene warps stay dropped (DS2 arrives at the start);
///  - 3:2 outside → 140132(slot, loc, loc, 0) (vanilla m10_04/m10_10 use the 4th argument 0 for "outside");
///    4:3 player character type Alive/Hollow → true (the host rule, like 3:6); 3:8 player in map → constant (own map);
///  - "(Compiled)" group tests (1000:7/8) read the groups of the last MAIN wait (DS1 clears groups there but keeps their
///    results); "(Uncompiled)" (1000:1/2) the current ones;
///  - Force Animation Playback with "wait for completion" on a prefab object → a wait for its end state (f131653) or 10 s.
/// Session 8 fix: arguments were decoded through float (C# switch typing), so every id above 2^24 was off by up to a few
/// units (DS1 61810105 → 61810104: event 0 and 11810313 tested the F2 West door's flag instead of the inner door's).
///
/// Session 8, track boss step 2 (a 1:1 boss exists: DS1's Asylum Demon; evidence = vanilla scripts and params):
///  - 2004:1 AI state on a boss → 130101(boss logic flag, 0|1): DS2 bosses start (wake from their start-up state, begin
///    thinking) when their GeneratorParam.logicEventFlag1 turns on (vanilla 【Lib】【プリセット】ボス戦開始 sets
///    「ロジック用その他フラグ」 119020080 for the Smelter, whose generator names it; the upper logic waits on f5903(0));
///  - 2004:9 Set Special Animation(s) with a waiting animation on a boss → 131702(gen, start-up state) 「起動ステート上書き」
///    (vanilla: the Pursuer's and the petrified enemies' placements) whose dormant loop plays that DS1 clip (the boss's role
///    table: SM_StartUp StartupType 2 → a75_01_7100/7101); all -1 (cleared) after this event set one → 131702(gen, 0), the
///    generator's own start-up, placed after the boss's wake-clip wait when DS1 plays the wake next (fix pass: vanilla
///    resets overrides itself, m10_10 【実行】…初期配置変更_再戦時 131702(p0, 0); whether DS2 keeps an override across loads
///    is unknown, so it is undone once the wake has played and never before it);
///  - 2003:18 Force Animation Playback of the boss's wake clip (DS1 9060, the drop) → no command: the AI plays it when the
///    logic flag starts the boss; "wait for completion" waits DS1's clip length;
///  - 2004:8 / 2004:21 DS1 SpEffect "Falling control for enemy" (stateInfo 47) → 130308(gen) 「敵の落下ダメージを一度だけ
///    無効化」 (m50_37 【実行】落下ダメージ無効_NPC) / no command (DS2's is once-only);
///  - 2004:13 Set Character Home → 131703(gen, point) 「初期開始位置上書き」 (no warp: the generator's start position);
///  - 2004:30 Set Character HP Bar Display on a boss → no command: the boss generator carries GeneratorParam.hpGaugeDisable
///    (the Smelter's generator does; the boss gauge from 132201 is the bar DS1 shows);
///  - 2003:36 / 2003:4 award of a DS1 lot whose rows carry their own getItemFlagId (DS1: a row whose flag is set is not
///    awarded; awarding sets it) → per row: skip if its flag is set, else 131402(row lot, 1) + 130101(flag, 1) (Asylum
///    Demon: lot 2660 = Big Pilgrim's Key 50001660 + Demon's Great Hammer 50001661, which DS1's 11810300 switches);
///  - <see cref="TranslateCommon"/>: DS1 common.emevd events instanced for this map's flags (event 970 = a boss's rewards on
///    its defeat flag: slot 21 waits for flag 16 → lots 2660 and, 5 s later, 9000 Humanity).
///
/// Session 8, track boss fix pass (review findings):
///  - event 0's load writes: DS1's event 0 has no wait, so it runs to its end in the load frame, before the events it
///    initialises take their first step; its flag writes are load-time resets (11810310/11810314 cleared on every load
///    until the gate flag 11810312 is set: that, not the event's RestBehavior "Restart", re-arms the demon's intro after a
///    death; DS1's engine does not clear the intro's own flag by itself, or event 0 [96] would not). The translated event 0
///    walks its states one by one (≈ 100 before those resets), so an event's leading tests could read the previous load's
///    value. Rule: the leading flag tests of a map event (its first run of SKIP/END IF Event Flag; events that never
///    restart within the load) read the flag as event 0 leaves it (<see cref="FoldedFlagTest"/>, from running event 0 once
///    per assignment of the flags it tests): 11810310 "END IF 11810314 ON" → f6(101000314) &amp;&amp; f6(101000312). Event 0
///    still writes the flags (the saved state is DS1's); later tests and waits read the flags as they are;
///  - the 2004:9 reset above (131702(gen, 0) after the wake).
///
/// Session 8 fix pass (review findings):
///  - settle time: a state that registers group conditions (140xxx) reads their slots only after
///    <see cref="Settle"/> s of state time (f3), and its fallback (SKIP/END tests) waits as long. Vanilla never reads a
///    slot negated or in a test with a fallback (all 28 event ESDs: f140001 is only a positive wait; complementary
///    outcomes are two positive registrations, e.g. Things Betwixt g2147483645 s5 140201 state 10 / state 20), so
///    whether a just-registered slot already holds its value in the first frame is unknown; the crossing detector's
///    "left both boxes" abort waits <see cref="AbortSettle"/> s;
///  - bosses (vanilla boss routine 【条件】ボス戦_終了判定 'ボスを倒したか？' = 140522(slot, BossBattleParam, 0, 1), 49 uses;
///    Belfry Gargoyles 【条件】順次起動するガーゴイル像 '合算HPが○○以下？' = 140523(slot, BossBattleParam, 0, percent, cmp)):
///    4:0 dead on a boss → 140522, 4:2 on a boss → 140523; 2003:11 on → 132201 only while no battle runs (f132201
///    「ボス戦中判定」 == 0), off → 132204 「ボス戦キャンセル」 only while one runs: DS2's own prelude fight (m10_14
///    【実行】前哨戦_ボスHPゲージ表示 → 132201, 【条件】…_終了判定 [撤退フラグ] → 【実行】…_終了 → 132204) is exactly DS1's
///    "bar while the player is in the arena, hidden when he escapes" (11815394/95), and after Handle Boss Defeat (132202)
///    the battle is over, so the bar-off of a dead boss is a no-op;
///  - session 10 (asy-sound): 2010:2 Play SE → 131203(key) on the point Sound region placed for it, 2006:3 one-shot SFX
///    → 131503(key) on its keyed SFX region (rules SND4/SND5, <see cref="MapSounds"/>); a switched sound that is ON at
///    map start is started by event 0 (SND1);
///  - 2010:3 Set Map Sound State → 131201 / 131202 「ポイント指定でサウンド再生/停止」(sound id) with the DS2 sound id of the
///    MSB2 Sound region the map builder placed for that DS1 sound event (<see cref="EntityRules.SoundId"/>; Skeleton
///    Lords: 131201(101) at the fight's start, 131202(101) after 132202); no region placed → no-op;
///  - DS1 animations of the player (2003:18 / 2004:9 on 10000: fog walk 7410, the nest's curl-up 7815-7817) → dropped:
///    the player is DS2's character with DS2's animations (the white door plays its own traversal);
///  - action buttons on an area (3:5 type 1 without a light wall) → the area's key-guide helper
///    (<see cref="IContext.ActionHelper"/>), its prompt on only while the player is inside DS1's area (two-stage wait;
///    leaving the area switches it off again);
///  - a cross-map warp (2002:4 to another map) is the event's last act in its run: the flag sets, waits, saves and
///    dropped commands DS1 runs after it (DS1 finishes them during the cutscene; DS2 unloads the map at 132125) move
///    in front of it (<see cref="HoistCrossMapWarps"/>), so 11810000's "left the Asylum" flag is set before the warp;
///  - routes translate together (<see cref="TranslateMap"/>): an event with a cross-map warp whose flag another
///    event's cross-map warp to the same map depends on (the cliff 11810000 and the nest 11810150) is kept back unless
///    that event translates too, so a one-way route never closes without its revisit route.
/// Track nests fix pass: departure flags (<see cref="DepartureFlags"/>): a same-map wait on a flag DS1 sets only after
///  the player has left, whose continuation only changes per-load display state, is tested once and then parks
///  (<see cref="ParksAtDeparture"/>), so the hoisted flag set can't show the cliff nest before or during the crow warp.
/// Track nestfix (session 9), rule G1, the group-condition gate (decompile, V from code; EventSim models it): DS2 runs an
///  event's machine in a frame only if the current state registered no group condition or one of them holds
///  (FUN_140196b80 → FUN_140183cb0), while f140001 evaluates a slot on demand (FUN_140183c30), so it is current in the
///  state's entry iteration. Hence (a) SKIP/END IF condition group tests resolve in their entry iteration, without the
///  settle (vanilla's test idiom `if f140001(n) → A; if 1 → B`, m20_11 g2147483557 s1): with it, a failing test waited for
///  a slot that never held and the event froze (Firelink 11020020 after "Stand up": no prompt, no warp, run s9-3);
///  (b) a negated region read in a wait (the area button's "left the area", the crossing detector's "left both boxes") is
///  a positive registration of the complement (140132 … 0), so leaving opens the gate (vanilla never reads a slot negated);
///  (c) a wait's flag terms that could pass while no slot holds become 140301 registrations (see the post-pass).
/// Track nestfix fix pass, rule G2, one visit per run (decompile, V from code): at every state change DS2 records how a
///  registering state was left (FUN_14046e780 → FUN_140183d40: path = state index, the event's run number, the slot mask;
///  the first record per path and run is kept) and f140001 returns the recorded bit on a later visit in the same run
///  (FUN_1401836a0); FUN_140183620 even steps it once without a holding slot. A state re-entered in one run replays its
///  first exit (vanilla: 0 of 3103 registering states can be re-entered without 130002). So DS1's Restart (END type 1,
///  2003:8 → R9) is DS2's re-execute 130002 (the run number goes up, FUN_140196850), and the area button's "left the
///  area" / the crossing detector's "left both boxes" re-execute too when DS1's instructions before that wait can rerun
///  unchanged (<see cref="ReplaySafe"/>); otherwise they are dropped (the prompt is spatial: KeyGuideParam radius).
/// Session 10 (sandbox asy-events):
///  - rule C1: one multiplayer-state rule (<see cref="NetExpr"/>): Host ≡ ¬Client = f130602() == 0, Client = f130602()
///    != 0, Multiplayer = f130602() || (f130601() &amp;&amp; f130661(0, 1) &gt; 0), Singleplayer = ¬Multiplayer; for 3:6 and
///    1003:5/6; 4:3 on the player: phantom types → Client, Alive + Hollow in one OR group → Host, a lone Alive/Hollow (the
///    human/hollow form) → no rule, reported (before: constants, Singleplayer compiled false). DS1's Host is true
///    offline (11810220's floor collapse);
///  - rule C11: 2005:7 Reproduce Object Animation → WE5 no-op on a part whose placement bakes the end pose (DS1 only
///    reproduces it and hides it at load: C16), else the prefab's rest state when its template takes that request, else
///    the play state (fallback);
///  - rule WE4b: inside the 2003:18 wait, DS1's damaging-object hits as timed 131613 (only while the target is active);
///    WE4b-L (fix pass): the event's load-time end branch (SKIP IF own flag OFF … END) re-applies those hits on targets
///    still active after <see cref="ReapplySettle"/> (repairs saves whose flag was set without the hit);
///  - rule WE6: 2005:8 Reproduce Object Destruction → 130702(inst, 0); rule WE8: a 2005:1 floor break whose drop
///    reaches DS2's lethal fall height first arms 130307 (none in the Asylum: 14.9 m &lt; 19.5 m);
///  - rule G11: departure continuations (<see cref="DepartureFold"/>).
/// Session 10, item e2 (doors + ladders; data\sandbox\asy10\doors.md K1-K7 + COVERAGE C15/G4, rules in <see cref="ObjActRules"/>):
///  - K3: a SpEffect requirement is a side test: the lock region = the grant slot's region with the door's flag OFF,
///    opened text 0; an event that only grants such a SpEffect is not emitted (11810120 → SKIP "rule K3");
///  - K4: a key door with an OR alternative chooses the item the player holds (140801) before its 131602;
///  - K7: "Used &lt;?gdsparam@N?&gt;" outside a 131602 door → 130443(1005, 0, 0, 190, 2, item, 0) (vanilla form);
///  - G4: 2009:0 on a ladder registered later than load → 131636(ladder, 10); <see cref="LadderPass"/> hides it at load
///    (event 0, 131636(ladder, 30)) and settles the registering groups, only when every registering event translates.
/// </summary>
public sealed class EmevdTranslator
{
    public interface IContext
    {
        /// <summary>The DS1 map this script belongs to (e.g. m18_01_00_00).</summary>
        string Ds1Map { get; }
        /// <summary>Session 9 (track firelink), rule T1: the map's script resets DS1's temporary flags (1AAB5000..1AAB5999)
        /// at every load (see <see cref="TranslateMap"/>). Opt-in per map until each map's in-game tests cover it.</summary>
        bool ResetsTemporaryFlags => false;
        /// <summary>Session 11 (merge11, enemy-audit G4, rule T1-B): the logic flags (GeneratorParam.logicEventFlag1) of the
        /// map's placed bosses: DS1's AI state (2004:1) is per load, so event 0 clears them at load with the T1 list.</summary>
        IEnumerable<int> BossLogicFlags => [];
        /// <summary>Session 11 (fx11), rule E-REQ-P: the AI request channels (slot, command, k, DS2 flag) of a placed regular
        /// (<see cref="Ai.EventRequests.PlacedChannels"/>; empty = none, 2004:17 on it stays unsupported).</summary>
        IReadOnlyList<(int Slot, int V, int K, int Flag)> AiChannels(int ds1Entity) => Ai.EventRequests.PlacedChannels(Ds1Map, ds1Entity);
        /// <summary>Rule E-REQ-P / T1-B: every channel flag of the map's placed regulars (event 0 clears them at load).</summary>
        IEnumerable<int> AiChannelFlags => Ai.EventRequests.PlacedFlags(Ds1Map);
        /// <summary>Session 11 (dchr11), rule WK2: the wake flag (GeneratorParam.logicEventFlag1, read by the boss-pipeline upper logic's
        /// start-up wake) of a placed large regular that starts posed (MapEnemies.WakeFlags); null = none. Reset at load with T1-B.</summary>
        int? WakeFlag(int ds1Entity) => Maps.MapEnemies.WakeFlags.TryGetValue(Ds1Map, out var m) && m.TryGetValue(ds1Entity, out int f) ? f : null;
        /// <summary>DS1 event flag (incl. event ids) → DS2 flag, or null if no rule.</summary>
        int? Flag(int ds1Flag);
        /// <summary>DS1 object entity → DS2 object instance, or null.</summary>
        int? Object(int ds1Entity);
        /// <summary>A DS1 object the map doesn't place (not ported yet; not a swap-in copy of a placed object): commands
        /// on it are no-ops, conditions on it never pass.</summary>
        bool ObjectAbsent(int ds1Entity);
        /// <summary>The placed object carries DS1's damaging-object behaviour on its own instance row (2005:9 → no-op).</summary>
        bool ObjectDamages(int ds1Entity);
        /// <summary>DS1 region (area) entity → DS2 EventLocation box, or null.</summary>
        int? Region(int ds1Entity);
        /// <summary>DS1 region entity → DS2 EventLocation for warps (position + facing), or null.</summary>
        int? Point(int ds1Entity);
        /// <summary>DS1 collision (hit) entity → DS2 EventLocation box over its bounds, or null.</summary>
        int? HitRegion(int ds1Entity);
        /// <summary>DS1 collision entity → its own DS2 hit group (2011:1 toggles), or null if the builder left it out.</summary>
        int? HitGroup(int ds1Entity);
        /// <summary>DS1 object entity + DS1 animation id → DS2 prefab state that plays it, or null.</summary>
        int? ObjectAnimState(int ds1Entity, int anim);
        /// <summary>The prefab state the object rests in once that animation has played (DS1's "wait for completion").</summary>
        int? ObjectAnimEndState(int ds1Entity, int anim);
        /// <summary>The key-guide helper standing for a DS1 action button (object or area entity, with DS1's help message:
        /// an area gets one helper per prompt text), or null.</summary>
        int? ActionHelper(int ds1Entity, int helpMessage);
        /// <summary>DS1 map sound entity (MSB1 Sound event) → the DS2 sound id of the MSB2 Sound region the map builder
        /// placed for it (the id 131201/131202 take), or null when the map has no such region (2010:3 → no-op).</summary>
        int? Sound(int ds1SoundEntity);
        /// <summary>Session 10 (asy-sound, rule SND4): the key of the point Sound region the map builder placed for a DS1
        /// 2010:2 Play SE (131203 plays it), or null (not ported: the SE is dropped and reported).</summary>
        int? SoundEffect(int ds1Entity, int type, int id) => null;
        /// <summary>Session 10 (asy-sound, rule SND5): the key (MSB2 SFX UnkT04) of the SFX region placed for a DS1 2006:3
        /// one-shot SFX (131503 plays it), or null.</summary>
        int? OneShotSfx(int ds1Entity, int sfx) => null;
        /// <summary>Session 10 (asy-sound, rule SND1): switched DS1 sounds that are ON at map start (DS2 never autoplays a
        /// keyed region) whose start this DS1 event's group carries, before its own instructions (fix pass: the load-time
        /// event that decides the sound, <see cref="MapSounds.StartIn"/>; event 0 as the fallback).</summary>
        IEnumerable<int> SoundsStartedIn(long ds1Event) => [];
        /// <summary>The key-guide helper standing for a DS1 ObjAct (Set ObjAct State switches its key guide), or null.</summary>
        int? ObjActHelper(int ds1Entity);
        /// <summary>DS1 map SFX entity → the DS2 object that stands for it (a fog's white door), or null.</summary>
        int? SfxObject(int ds1SfxEntity);
        /// <summary>DS1 event message id → DS2 mapevent text id (DS2's own entry when the English text is the same,
        /// else DS1's text added), or null.</summary>
        int? Message(int ds1MessageId);
        /// <summary>DS1 ObjAct event flag (ObjAct entity) → DS2 flag the translated door/helper sets, or null.</summary>
        int? ObjActFlag(int ds1ObjActFlag);
        /// <summary>DS1 ObjAct with a key item or SpEffect requirement on a ported DS2 door → its lock, or null.</summary>
        ObjActLock? Lock(int ds1ObjActFlag);
        /// <summary>An ObjAct on a part DS2 has no interaction for (doors that never open): its flag never comes.</summary>
        bool ObjActAbsent(int ds1ObjActFlag);
        /// <summary>DS1 SpEffect that only gates an ObjAct (now the door's region): granting it is dropped.</summary>
        bool SpEffectGatesObjAct(int spEffect);
        /// <summary>DS1 character entity → placed DS2 generator, or null (absent).</summary>
        int? Generator(int ds1Entity);
        /// <summary>Session 11 (track ret11), rule W1 (<see cref="WorldState"/>): the placed character's enable state is carried
        /// by its generator's start/stop conditions (DS1's load-time toggles and live switches folded), so 2004:5 on it → no
        /// command (131731 "enable" has no vanilla precedent, D8; conditions also survive DS2's rest respawn).</summary>
        bool WorldOwned(int ds1Entity) => false;
        /// <summary>Session 11 (ret11), rule N-H2: the placed NPC is hostile through its generator (hostility flag), so DS1's Set Team
        /// Type 9 on it needs no command.</summary>
        bool NpcHostile(int ds1Entity) => false;
        /// <summary>The character is a DS1 boss (has a DS1 GameAreaParam row).</summary>
        bool IsBoss(int ds1Entity);
        /// <summary>DS1 boss entity (+ HP bar name id, 0 if unknown) → BossBattleParam row (built on first use), or null.</summary>
        int? BossBattle(int ds1Entity, int nameId);
        /// <summary>DS1 ItemLotParam id → our ItemLotParam2_Other row (built on first use), or null if none of its
        /// items exist in DS2 yet.</summary>
        int? ItemLot(int ds1Lot);
        /// <summary>Session 10 (asy-items i2), rule S2: a DS1 ItemLotParam row's getItemFlagId (DS1 id), or null.</summary>
        int? ItemLotFlag(int ds1Lot) => null;
        /// <summary>Session 10 (asy-items i2 fix pass), rule R5: a DS1 SpEffect that is a ring's own effect
        /// (EquipParamAccessory.refId) → the DS2 ring item standing for that ring (DS2's ring, or RingPorter's 43M item), or
        /// null.</summary>
        int? RingItem(int ds1SpEffect) => null;
        /// <summary>A light wall + the action button's area → the crossing boxes (button side, far side), or null when
        /// the entity isn't a fog with a DS2 white door.</summary>
        (int From, int To)? FogCrossing(int ds1FogObject, int ds1Area);
        /// <summary>A DS1 blood message (MSB entity) the map doesn't have yet: showing/hiding it is a no-op.</summary>
        bool MessageAbsent(int ds1MessageEntity);
        /// <summary>Session 10 (asy-items), rule M2: a DS1 blood message (MSB entity) → the DS2 EventLocation row the
        /// build placed for it (BloodMessages: category 12, visible at every load like DS1's), or null.</summary>
        int? MessageLocation(int ds1MessageEntity) => null;
        /// <summary>Session 10 (asy-items), rule C3: pickup instances of DS1 treasures that start disabled (MSB
        /// StartDisabled): the map's event 0 disables their item at every load (DS1's own 2005:4 enables it).</summary>
        IEnumerable<int> TreasuresDisabledAtLoad => [];
        /// <summary>DS1 point in another map (area, block) → (DS2 map id decimal AABBCCDD, EventLocation), or null.</summary>
        (int Map, int Point)? MapPoint(int area, int block, int point);
        /// <summary>Session 8 (cutscene track): DS1 cutscene id → the DS2 demo the build converted it to (DemoParam row,
        /// model/scn/scnNNNNNN.scnbnd), or null when it isn't converted (the warp then plays no demo: 0).</summary>
        int? Demo(int ds1Scene) => null;
        /// <summary>A placed boss's DS2 logic flag (its GeneratorParam.logicEventFlag1), or null.</summary>
        int? LogicFlag(int ds1Entity);
        /// <summary>The DS2 start-up state whose dormant loop plays DS1 waiting animation <paramref name="anim"/> on this
        /// character, or null.</summary>
        int? StartUpState(int ds1Entity, int anim);
        /// <summary>DS1 length (s) of the character's start-up wake clip when <paramref name="anim"/> is it, or null.</summary>
        double? WakeClip(int ds1Entity, int anim);
        /// <summary>The DS1 SpEffect only keeps a falling character alive (stateInfo 47 "Falling control for enemy").</summary>
        bool FallControl(int spEffect);
        /// <summary>A DS1 lot whose rows carry their own "obtained" flags: (our lot row, DS2 flag) per row in DS1's order
        /// (flag null: always awarded), or null when no row has a flag (the merged lot of <see cref="ItemLot"/> applies).</summary>
        List<(int Lot, int? Flag)>? ItemLotRows(int ds1Lot);
        /// <summary>Session 10 (rule C11): the prefab state the object rests in after DS1 animation <paramref name="anim"/>,
        /// when its template accepts a direct request of it (2005:7 then requests it: the end pose at once); null = the
        /// C11 fallback (request the play state: the animation replays to its end).</summary>
        int? ObjectRestState(int ds1Entity, int anim) => null;
        /// <summary>Session 11 (burg11, rule LF1): DS1 animation <paramref name="anim"/> only holds a rest pose (the lift's
        /// settle clips 21/22): 2003:18 requests the rest state (<see cref="ObjectRestState"/>) instead of a move.</summary>
        bool ObjectAnimIsRest(int ds1Entity, int anim) => false;
        /// <summary>Session 10 (rule WE5): the part's placement already shows the animation's end pose (a part DS1 only
        /// reproduces, placed as a non-prefab object): 2005:7 → no-op.</summary>
        bool ObjectPoseBaked(int ds1Entity, int anim) => false;
        /// <summary>Session 10 (rule WE4b): object instances DS1's damaging object on this entity breaks while the
        /// animation plays, with DS1's first-contact time (s after the animation starts).</summary>
        IReadOnlyList<(int Instance, float Seconds)> ObjectHits(int ds1Entity, int anim) => [];
        /// <summary>Session 10 (rule WE8): breaking this floor object drops the player at least DS2's lethal fall height,
        /// which DS1 survives: arm DS2's "no fall damage once" (130307) with the break.</summary>
        bool BreakNeedsFallGuard(int ds1Entity) => false;
        /// <summary>Session 10 (rule K7): the DS2 item a DS1 dialog's &lt;?gdsparam@N?&gt; placeholder names (DS1 goods N
        /// through the item rules), or null.</summary>
        int? MessageItem(int ds1MessageId) => null;
        /// <summary>Session 10 (rule G4): the object instance of a DS1 ladder some event registers later than load (it
        /// starts hidden and 2009:0 shows it), or null (registered at load: DS2's plain ladder, 2009:0 is a no-op).</summary>
        int? Ladder(int ds1Entity) => null;
        /// <summary>Rule G4: every such ladder of the map (DS1 entity → instance).</summary>
        IReadOnlyDictionary<int, int> EventLadders => new Dictionary<int, int>();
        /// <summary>Session 10 (e3, rule N1): the souls DS2 gives for this placed character (DS1 NpcParam getSoul ×
        /// ChrPorter.SoulScale, as its EnemyParam.dropSoulValue), handed out by 2004:4 Force Character Death when DS1's
        /// ShouldReceiveSouls is set; null = unknown (no souls command).</summary>
        int? CharacterSouls(int ds1Entity) => null;
        /// <summary>Session 11 (burg11, rule K6b): DS1's player positioning before an action (2004:41 short warp onto an
        /// object's dummy, 2004:14 turn toward it) is cosmetic on DS2's helper route (no player animation either, rule K6
        /// deviation): translated as no-ops. Opt-in per context (Ds1MapContext); the verified maps keep reporting them.</summary>
        bool PlayerPoseIsCosmetic => false;
        /// <summary>Session 11 (burg11, rule ST1 opt-in): commands on DS1 parts this map has no DS2 counterpart for yet are
        /// no-ops reported as absent instead of keeping the whole event untranslated: map SFX of a light wall left out
        /// (FW1 stand-in) and navmesh connection flags (2003:13, rule NV3 pending: no enemy paths there before M3). Opt-in
        /// (Ds1MapContext): a load event (event 0) then still runs its other instructions.</summary>
        bool StandInsAreNoOps => false;
        /// <summary>Session 11 (burg11), rule I1: also translate the events translated events initialise (2000:0 below the
        /// roots 0 / 50), each in its own group (guarded by the parent's flag when DS1 starts it after the parent completed).</summary>
        bool NestedInits => false;
        /// <summary>Session 11 (burg11), rule N1: DS1's 1000:9 WAIT For Network Approval is a no-op (else: no rule).</summary>
        bool NetworkWaitsAreNoOps => false;
        /// <summary>Session 11 (burg11, M3/M4), the character-event rules of a MapObjects map (opt-in; the Asylum and Firelink
        /// keep their verified translation): CR1 network-only commands (2004:28/34, 2003:16, 2004:43) are no-ops; CR2 AI state on a
        /// placed regular → 131731 pause; CR3 Character Damaged By → HP below 100 %; CR4 All Players In Area = the player (host
        /// view); CR5 Entity In Radius of Entity (player and a placed character) → 140101; CR6 WAIT Random Time → its mean;
        /// CR7 boss-room fog buttons (3:19) as 3:18; CR8 forced animations of placed regulars (ambush / door kick clips the
        /// 1:1 rigs do not carry) → no command (the AI acts); CR9 invincibility / immortality / gravity / AI target on placed
        /// characters → no command; CR10 TAE event messages (4:8) → never (no DS2 counterpart yet).</summary>
        bool CharacterRulesM3 => false;
        /// <summary>Session 11 (burg11, round 6), rule IT1: the DS2 item of a DS1 goods id (key items: GoodsPorter), or null.</summary>
        int? GoodsItem(int ds1Goods) => null;
        /// <summary>Session 11 (track chrai11), rule EM1: the placed character whose translated AI writes DS1 TAE message
        /// <paramref name="message"/> on its board (Ai.Boss.EventMessages): (generator, board code), or null.</summary>
        (int Generator, int Code)? EventMessage(int ds1Entity, int message) => null;
        /// <summary>Rule EM1-r: the message is sent only by the character's NPC-part break reaction (rule TC1: DS2 plays it with the
        /// part break), so it has come once the event's preceding break test held.</summary>
        bool ReactionMessage(int ds1Entity, int message) => false;
        /// <summary>Rule FA1: an event-forced clip the character plays as an action of its own: (the clip's request flag, DS1
        /// length in s), or null.</summary>
        (int Flag, double Seconds)? ForcedClip(int ds1Entity, int anim) => null;
        /// <summary>Rule FA1: the character's AI-off signal flag (DS1 2004:1 on it), or null.</summary>
        int? AiOffFlag(int ds1Entity) => null;
        /// <summary>Rule HB1: DS1's Hellkite Breath Control on this character → (generator, DS2 breath bullet), or null.</summary>
        (int Generator, int Bullet)? Breath(int ds1Entity, int ds1Object, int anim) => null;
    }

    /// <summary>A DS2 door lock (131602) standing for a DS1 ObjAct requirement; the event must belong to the door.
    /// Session 10 (rule K4): Item2 = DS1's OR alternative (spQualifiedType2 1, e.g. the Master Key); 131602 checks its two
    /// items with AND, so the event chooses the item the player holds.</summary>
    public sealed record ObjActLock(int Owner, int Region, int Item, int UsedText, int FailText, int Flag, int Item2 = 0);

    /// <param name="Owner">object instance the EventParam row must belong to (ownerType 1), 0 = ownerless</param>
    /// <param name="Absent">targets the map doesn't have yet whose commands became no-ops (reported)</param>
    public sealed record Result(long EventId, int Slot, int GroupId, Dictionary<long, ESD.State>? States, List<string> Missing,
        int Owner, List<string> Absent)
    {
        /// <summary>A common.emevd event instanced for this map (<see cref="TranslateCommon"/>).</summary>
        public bool Common { get; init; }
        /// <summary>DS2 flags whose leading tests read them as DS1's event 0 leaves them (fix pass).</summary>
        public List<string> Folded { get; init; } = [];
    }

    public const int Player = 10000;
    readonly EMEVD emevd;
    readonly IContext ctx;
    readonly Dictionary<(int, int), (string Name, int[] Types)> defs;
    /// <summary>Per cross-map warp of the event being translated: (pre-warp demo, post-warp demo) for 132125.</summary>
    readonly Dictionary<EMEVD.Instruction, (int Before, int After)> warpDemos = new(ReferenceEqualityComparer.Instance);
    /// <summary>Session 9 (track firelink): (event, slot) pairs another event of this map restarts (2003:8 Restart).</summary>
    readonly HashSet<(long Event, int Slot)> restartTargets = [];
    /// <summary>Session 9 (track firelink): a restart request's flag was the event's own DS2 flag + this. Superseded (track
    /// npcflags): DS2 has no storage there; the request flag is <see cref="FlagRules.Restart"/> (rule F3). Unused.</summary>
    public const int RestartFlagOffset = 800_000;
    /// <summary>Session 9 (fix pass): entry commands per state at most (vanilla's maximum: event_m50_37 group 2147483436
    /// state 1 issues 64; the 28 vanilla event ESDs never more).</summary>
    public const int MaxEntryCommands = 64;
    /// <summary>Session 9 (rule T1): the DS2 ids of the DS1 temporary flags this map's events use (empty unless
    /// <see cref="IContext.ResetsTemporaryFlags"/>) and its restart request flags; event 0's group clears each at load
    /// (session 9, npcflags: one 130101 per flag).</summary>
    public SortedSet<int> TemporaryFlags { get; } = [];
    /// <summary>Session 9 (rule T1): event 0's group got the load reset (false: no temporary flags, or event 0 untranslated).</summary>
    public bool TemporaryFlagsReset { get; private set; }
    public List<Result> Results { get; } = [];

    /// <param name="defs">instruction (bank, id) → name and argument types (EMEDF: 0 u8, 1 u16, 2 u32, 3 s8, 4 s16, 5 s32, 6 f32)</param>
    public EmevdTranslator(EMEVD emevd, IContext ctx, Dictionary<(int, int), (string Name, int[] Types)> defs)
    {
        this.emevd = emevd; this.ctx = ctx; this.defs = defs;
    }

    public string Report() => string.Join("\n", Notes.Select(n => "  NOTE " + n)) + (Notes.Count > 0 ? "\n" : "") + string.Join("\n", Results.Select(r => r.States != null
        ? $"  OK   {(r.Common ? "common " : "")}event {r.EventId} slot {r.Slot} → group {r.GroupId} ({r.States.Count} states{(r.Owner != 0 ? $", owner {r.Owner}" : "")}{(r.Absent.Count > 0 ? "; absent: " + string.Join(", ", r.Absent.Distinct()) : "")}{(r.Folded.Count > 0 ? "; event-0 fold: " + string.Join(", ", r.Folded) : "")})"
        : $"  SKIP {(r.Common ? "common " : "")}event {r.EventId} slot {r.Slot}: {string.Join("; ", r.Missing.Distinct())}"));

    /// <summary>
    /// Translates event 0 and 50 and every event they initialise (one result per event and slot). Then the route rule:
    /// a translated event with a cross-map warp whose own flag (event id + slot) gates another event's cross-map warp to
    /// the same map (DS1 11810000 "carried off the cliff" → 11810150 "curl up in the nest" waits for it) is kept back
    /// when that event doesn't translate: its flag would close the route for good with no way back (the map then keeps
    /// its own stand-in, e.g. the crow group 37200).
    /// </summary>
    /// <summary>Session 11 (burg11, round 9), rule H2-V2: the H2 member rank of a DS1 event id (null: one map).</summary>
    public Func<long, int?>? MemberRank { get; set; }

    public void TranslateMap(Func<long, int, int> groupId)
    {
        var byId = emevd.Events.ToDictionary(e => e.ID);
        var calls = new List<(long Id, int Slot, byte[] Args)>();
        foreach (var root in new long[] { 0, 50 })
        {
            if (!byId.TryGetValue(root, out var ev)) continue;
            calls.Add((root, 0, []));
            foreach (var ins in ev.Instructions.Where(i => i.Bank == 2000 && i.ID == 0))
                calls.Add((BitConverter.ToUInt32(ins.ArgData, 4), BitConverter.ToInt32(ins.ArgData, 0), ins.ArgData[8..]));
        }
        // session 11 (burg11), rule I1 (opt-in, IContext.NestedInits): an event a translated event initialises (DS1 Firelink's
        // lift 11020300 → 11020301) gets its own group too; when the 2000:0 sits right behind "SKIP IF this event's flag OFF"
        // (1003:1 type 1, id 0: DS1 starts it once the parent has completed), the group waits for the parent's flag (rule I0's
        // guard mechanism)
        if (ctx.NestedInits)
            for (int k = 0; k < calls.Count; k++)
            {
                if (calls[k].Id is 0 or 50 || !byId.TryGetValue(calls[k].Id, out var pev)) continue;
                var pins = pev.Instructions;
                for (int i = 0; i < pins.Count; i++)
                {
                    if (pins[i].Bank != 2000 || pins[i].ID != 0 || pins[i].ArgData.Length < 8) continue;
                    var child = ((long)BitConverter.ToUInt32(pins[i].ArgData, 4), BitConverter.ToInt32(pins[i].ArgData, 0), pins[i].ArgData[8..]);
                    if (calls.Any(c => c.Id == child.Item1 && c.Item2 == child.Item2) || !byId.ContainsKey(child.Item1)) continue;
                    calls.Add(child);
                    int parentFlag = (int)calls[k].Id + calls[k].Slot;
                    bool afterOwnFlag = i >= 1 && pins[i - 1].Bank == 1003 && pins[i - 1].ID == 1 && pins[i - 1].ArgData.Length >= 8
                        && pins[i - 1].ArgData[1] == 0 && pins[i - 1].ArgData[2] == 1 && BitConverter.ToInt32(pins[i - 1].ArgData, 4) == 0;
                    if (afterOwnFlag && ctx.Flag(parentFlag) != null) initGuards[(child.Item1, child.Item2)] = [(parentFlag, true)];
                    Notes.Add($"rule I1: event {child.Item1} slot {child.Item2} initialised by translated event {calls[k].Id}{(afterOwnFlag ? $": its group waits for {parentFlag} ON" : "")}");
                }
            }
        if (MemberRank != null)
        {
            // rule H2-V2: member blocks (a call without a member of its own — event 0/50, a common event — stays with the call before it)
            var ranked = new List<(long Id, int Slot, byte[] Args)>(); var rank = new List<int>(); int last = 0;
            foreach (var c in calls) { last = c.Id is 0 or 50 ? 0 : MemberRank(c.Id) ?? last; ranked.Add(c); rank.Add(last); }
            calls = ranked.Select((c, i) => (c, r: rank[i])).OrderBy(x => x.r).Select(x => x.c).ToList();
        }
        ScanCalls(calls.DistinctBy(c => (c.Id, c.Slot)).Where(c => byId.ContainsKey(c.Id)).ToList(), byId); // session 9 (track firelink): R9, T1
        calls = calls.DistinctBy(c => (c.Id, c.Slot)).Where(c => byId.ContainsKey(c.Id)).ToList();
        departureFlags = DepartureFlags(calls.Select(c => (byId[c.Id], c.Slot, c.Args)).ToList()); // track nests
        // session 10, rule G11: departure continuations folded into the first load with the departure flag
        foreach (var (id, slot, args) in calls)
            if (DepartureFold(HoistCrossMapWarps(byId[id].Instructions.Select((x, i) => Substitute(byId[id], i, x, args)).ToList()), (int)id + slot) is { } fold)
            {
                folds[(id, slot)] = fold;
                FoldFlags.UnionWith(fold.Ds2Flags);
            }
        // session 11 (ret11), rule I0: an event that event 0 / 50 initialises only on a flag path waits for that path's flags
        foreach (var root in new long[] { 0, 50 })
            if (byId.TryGetValue(root, out var rev))
                foreach (var (k, g) in InitGuards(rev))
                    if (g.Count > 0 && !initGuards.ContainsKey(k)) initGuards[k] = g;
        foreach (var (id, slot, args) in calls)
            Results.Add(Translate(byId[id], slot, args, groupId(id, slot)));
        // rule I0: the guard as the group's first wait, in front of what the translation starts with (T1 settle, folds included)
        for (int k = 0; k < Results.Count; k++)
        {
            var r = Results[k];
            if (r.States == null || !initGuards.TryGetValue((r.EventId, r.Slot), out var ig) || !r.States.TryGetValue(0, out var st0)
                || st0.Conditions.Count == 0 || st0.Conditions[0].TargetState is not long first) continue;
            var lits = ig.Select(l => (F: ctx.Flag(l.Flag), l.On)).ToList();
            if (lits.Any(l => l.F == null)) continue;
            byte[] expr = FlagExpr(lits[0].F!.Value, lits[0].On);
            foreach (var l in lits.Skip(1)) expr = [.. expr, .. FlagExpr(l.F!.Value, l.On), 0x98];
            r.States[InitGuardState] = S([], ([.. expr, 0xA1], first));
            st0.Conditions[0].TargetState = InitGuardState;
        }

        // routes translate together
        List<EMEVD.Instruction> Ins((long Id, int Slot, byte[] Args) c) => byId[c.Id].Instructions.Select((x, i) => Substitute(byId[c.Id], i, x, c.Args)).ToList();
        HashSet<(int, int)> Warps((long Id, int Slot, byte[] Args) c) => Ins(c).Where(IsCrossMapWarp).Select(x => V(x)).Select(v => ((int)v[3], (int)v[4])).ToHashSet();
        bool Tests((long Id, int Slot, byte[] Args) c, int flag) => Ins(c).Any(x => (x.Bank, x.ID) is (3, 0) or (1003, 1) or (1003, 2) && V(x) is var v && (int)v[2] == 0 && (int)v[3] == flag);
        for (int k = 0; k < Results.Count; k++)
        {
            var r = Results[k];
            if (r.States == null) continue;
            var a = calls[k];
            var to = Warps(a);
            if (to.Count == 0) continue;
            int flag = (int)a.Id + a.Slot;
            var needs = calls.Select((c, j) => (c, j)).Where(t => t.j != k && Tests(t.c, flag) && Warps(t.c).Overlaps(to)).ToList();
            var missing = needs.Where(t => Results[t.j].States == null).Select(t => t.c.Id).ToList();
            if (missing.Count > 0)
                Results[k] = r with { States = null, Missing = [$"route: its flag closes the warp for good, but the revisit route (event {string.Join(", ", missing)}) doesn't translate"] };
        }
        LadderPass();
    }

    /// <summary>Session 11 (ret11), rule I0: (event id, slot) → the DS1 flag literals (flag, ON?) its group waits for first.</summary>
    readonly Dictionary<(long, int), List<(int Flag, bool On)>> initGuards = [];

    /// <summary>
    /// Session 11 (track ret11), rule I0: DS1 initialises some events only on a flag path of event 0 / 50 (m18_01 event 0 [117]
    /// "END IF 11810000 OFF" before 11815382 / 11810900 / 11815384 / 11815385: the Stray Demon's events exist only on the
    /// return trip), while the translator starts every initialised event at load. Per Initialize Event of the root: the paths
    /// that reach it (SKIP / END IF Event Flag on literal flags, SKIP / END unconditionally); guard = the literals common to
    /// every path when the other literals of the paths cover every assignment (11815382: {11810000 ON, 11810900 ON} ∪
    /// {11810000 ON, 11810900 OFF} → 11810000 ON); a path through any other control flow (condition groups, multiplayer,
    /// waits) leaves the event unguarded, as before. The guard is the group's first wait (3:0 MAIN, not END IF: an END would
    /// set the event's own flag, which DS1 never sets for an event it didn't start); the flag only turns on when the player
    /// leaves the map (11810000: the crow), so waiting = DS1's "not this load".
    /// </summary>
    Dictionary<(long, int), List<(int Flag, bool On)>> InitGuards(EMEVD.Event root)
    {
        var paths = new Dictionary<(long, int), List<(List<(int Flag, bool On)> Lits, bool Sure)>>();
        var ins = root.Instructions;
        var todo = new Stack<(int Pc, List<(int, bool)> Lits, bool Sure)>();
        todo.Push((0, [], true));
        int guard = 0;
        var seenStates = new HashSet<(int, string, bool)>();
        while (todo.Count > 0 && ++guard < 20000)
        {
            var (pc, lits, sure) = todo.Pop();
            // a branch state seen before (same instruction, literals and certainty) adds nothing: unknown SKIPs fork without a literal
            if (!seenStates.Add((pc, string.Join(",", lits.Distinct().OrderBy(l => l.Item1).Select(l => $"{l.Item1}{(l.Item2 ? "+" : "-")}")), sure))) continue;
            while (pc < ins.Count)
            {
                var x = ins[pc]; var v = V(x);
                if ((x.Bank, x.ID) is (1003, 1) && v.Length >= 4 && (int)v[2] == 0)
                {
                    bool d = v[1] != 0; int f = (int)v[3];
                    todo.Push((pc + 1 + (int)v[0], [.. lits, (f, d)], sure));
                    lits = [.. lits, (f, !d)]; pc++; continue;
                }
                if ((x.Bank, x.ID) is (1003, 2) && v.Length >= 4 && (int)v[2] == 0) { lits = [.. lits, ((int)v[3], v[1] == 0)]; pc++; continue; }
                if ((x.Bank, x.ID) is (1000, 3)) { pc += 1 + (int)v[0]; continue; }
                if ((x.Bank, x.ID) is (1000, 4)) break;
                // the host's view inside its own map (as rule W1's fold): SKIP / END IF Multiplayer State (guest) and Player In/Out Map
                // (outside) do not branch for the host standing in the map; their guest branch stays the translator's rule C1
                if ((x.Bank, x.ID) is (1003, 5) or (1003, 6) or (1003, 7) or (1003, 8)) { pc++; continue; }
                if (x.Bank is 1000 or 1003) { if (x.ID is 1 or 3 or 5 or 7) todo.Push((pc + 1 + (int)v[0], lits, false)); sure = false; pc++; continue; }
                if (x.Bank is 0 or 1 or 3 or 4 or 5 or 11 && v.Length > 0 && (int)v[0] == 0) sure = false;   // a MAIN wait before the init
                if ((x.Bank, x.ID) is (2000, 0) && x.ArgData.Length >= 8)
                {
                    var key = ((long)BitConverter.ToUInt32(x.ArgData, 4), BitConverter.ToInt32(x.ArgData, 0));
                    (paths.TryGetValue(key, out var l) ? l : paths[key] = []).Add(([.. lits], sure));
                }
                pc++;
            }
        }
        var result = new Dictionary<(long, int), List<(int, bool)>>();
        if (todo.Count > 0) { Notes.Add($"rule I0: event {root.ID}'s init paths not enumerated within {guard} branch states: no guards (as before)"); return result; }
        foreach (var (key, ps) in paths)
        {
            if (ps.Any(p => !p.Sure || p.Lits.Count == 0)) continue;
            var common = ps[0].Lits.Distinct().Where(l => ps.All(p => p.Lits.Contains(l))).ToList();
            if (common.Count == 0) continue;
            // the residuals must cover every assignment of their flags (else the guard would be wider than DS1's paths)
            var rest = ps.Select(p => p.Lits.Except(common).Distinct().ToList()).ToList();
            var flags = rest.SelectMany(r => r.Select(l => l.Item1)).Distinct().ToList();
            if (flags.Count > 10) continue;
            bool covered = Enumerable.Range(0, 1 << flags.Count).All(m => rest.Any(r => r.All(l => ((m >> flags.IndexOf(l.Item1) & 1) != 0) == l.Item2)));
            if (!covered) continue;
            if (common.Any(l => ctx.Flag(l.Item1) == null)) continue;   // no DS2 storage: leave the event unguarded (reported elsewhere)
            result[key] = common;   // DS1 flags: the guard is prepended as DS1 instructions and translated like the rest
            Notes.Add($"rule I0: event {key.Item1} slot {key.Item2} is initialised only when {string.Join(" ∧ ", common.Select(l => $"{l.Item1} {(l.Item2 ? "ON" : "OFF")}"))}: its group waits for that first");
        }
        return result;
    }

    /// <summary>Session 10: per-map rule notes for the report (rule G4 ladders).</summary>
    public List<string> Notes { get; } = [];

    /// <summary>
    /// Session 10, rule G4 (COVERAGE G4): a DS1 ladder some event registers later than load (2009:0 in 11815382 / 11810900:
    /// the Stray Demon pit ladder 1811141) must not be climbable before. DS2's mechanism (V-data, vanilla m10_17 group
    /// 111095 and Majula's laddersmith groups 111098 / 2147483629): a plain ladder object (objCtrlType 4, its own instance
    /// row, no pfbbin) is hidden by 131636(ladder, 30) 「梯子設置：非表示」 and shown by 131636(ladder, 10) 「梯子設置：表示」;
    /// V-code: objCtrlType 4, 18 and 26 all build the ladder component (the object component factory FUN_1403e6390, l.779024-779043 →
    /// FUN_1401cd1c0), a MapObjGimmickComponent whose use is gated by the object's state component (FUN_1403f2500 →
    /// FUN_1401d7130). So: event 0's group hides every such ladder in its first state (before T1's reset), and every group
    /// that registers one (2009:0 → 131636(ladder, 10)) starts after <see cref="Settle"/> s, so the show always follows
    /// the load-time hide whatever order DS2 steps the groups in. When NO registering event translates (the Stray Demon
    /// is not ported yet: 11815382 / 11810900 SKIP), the ladder is not hidden (no lock-in without its way out, as for
    /// bosses) and the report says so.
    /// </summary>
    void LadderPass()
    {
        var ladders = ctx.EventLadders;
        if (ladders.Count == 0) return;
        static bool Shows(ESD.State s, int inst) => s.EntryCommands.Any(c => c.CommandID == 131636 && c.Arguments.Count > 1
            && (int)EventSim.Evaluate(c.Arguments[0], (_, _) => 0) == inst && (int)EventSim.Evaluate(c.Arguments[1], (_, _) => 0) == ObjActRules.LadderShown);
        var ev0 = Results.FirstOrDefault(r => r.EventId == 0 && r.Slot == 0 && r.States != null);
        var hide = new List<int>();
        foreach (var (e, inst) in ladders.OrderBy(l => l.Key))
        {
            var by = Results.Where(r => r.States != null && r.States.Values.Any(s => Shows(s, inst))).ToList();
            // every DS1 path that registers it must exist (11815382 re-registers it at each load after the kill, 11810900 at
            // the kill): with one of them untranslated the ladder would stay hidden where DS1 shows it (a lock-in)
            var need = Results.Where(r => emevd.Events.First(ev => ev.ID == r.EventId).Instructions.Any(x => x is { Bank: 2009, ID: 0 } && (int)V(x)[2] == e)).ToList();
            var untranslated = need.Where(r => r.States == null).Select(r => r.EventId).Distinct().ToList();
            if (by.Count == 0 || untranslated.Count > 0 && !G4Diagnostic)
            {
                Notes.Add($"rule G4: ladder {e} ({inst}) stays climbable from load (pending): {(by.Count == 0 ? "no event that registers it translates" : "registering event(s) " + string.Join(", ", untranslated) + " don't translate")}");
                foreach (var r in by) foreach (var s in r.States!.Values) s.EntryCommands.RemoveAll(c => c.CommandID == 131636 && c.Arguments.Count > 1 && (int)EventSim.Evaluate(c.Arguments[0], (_, _) => 0) == inst);
                continue;
            }
            if (ev0 == null) { Notes.Add($"rule G4: ladder {e} ({inst}) not hidden: event 0 doesn't translate"); continue; }
            hide.Add(inst);
            foreach (var r in by.Where(r => r != ev0))
            {
                var st0 = r.States![0];
                if (st0.Conditions[0].TargetState is not long t || t == 8996 || t == LadderSettle) continue; // T1's settle already waits
                r.States[LadderSettle] = S([], ([.. StateTime(Settle), 0xA1], t));
                st0.Conditions[0].TargetState = LadderSettle;
            }
            Notes.Add($"rule G4: ladder {e} ({inst}) hidden at load (event 0), shown by {string.Join(", ", by.Select(r => $"{r.EventId} slot {r.Slot}"))}");
        }
        if (hide.Count == 0) return;
        var s0 = ev0!.States![0];
        if (s0.Conditions[0].TargetState is long first)
        {
            ev0.States[LadderHide] = S(hide.Select(i => C(131636, i, ObjActRules.LadderHidden)), (True, first));
            s0.Conditions[0].TargetState = LadderHide;
        }
    }
    const long LadderHide = 8991, LadderSettle = 8990; // merge11: 8993 is asy-items' LoadInit
    /// <summary>Session 11 (ret11), rule I0: the init-guard wait state (free: 8989-8999 are taken).</summary>
    const long InitGuardState = 8988;
    /// <summary>DIAGNOSTIC ONLY (emevd-translate --g4-any, never in a build): apply G4 when at least one registering
    /// event translates, to show the hide / settle / show states while the Stray Demon is not ported.</summary>
    public bool G4Diagnostic { get; init; }

    /// <summary>
    /// The cutscene rule for cross-map warps (session 8, cutscene track): DS1 plays a map change as cutscenes around
    /// 2002:4 "Play Cutscene and Warp Player": the cutscene of the map being left and the cutscene of the arrival map,
    /// one of them the warp's own and the other a 2002:3 "Play Cutscene to Player" right before or after it (11810000:
    /// 2002:3 180125 (m18_01) + 2002:4 100225 (m10_02) → Firelink; 11810150: 2002:4 180126 (m18_01) + 2002:3 100226
    /// (m10_02)). A scene id names its map (AABBNN → mAA_BB). DS2's map warp takes exactly that pair:
    /// 132125(pre-warp demo, post-warp demo, map, point, 0) (vanilla 【Lib】ポリ劇後マップ間ワープ, FoFG's bird: 101010 +
    /// 101620 → the Lost Bastille). Scenes of the current map → the pre-warp demo, of the target map → the post-warp one,
    /// each through <see cref="IContext.Demo"/> (0 when not converted). The paired 2002:3s are dropped (they already are:
    /// 2002:3 is a provisional drop).
    /// </summary>
    void PairWarpDemos(List<EMEVD.Instruction> ins)
    {
        warpDemos.Clear();
        static int SceneArea(int scene) => scene / 10000;
        static int SceneBlock(int scene) => scene / 100 % 100;
        int curArea = int.Parse(ctx.Ds1Map[1..3]), curBlock = int.Parse(ctx.Ds1Map[4..6]);
        for (int w = 0; w < ins.Count; w++)
        {
            if (!IsCrossMapWarp(ins[w])) continue;
            var v = V(ins[w]);
            int toArea = (int)v[3], toBlock = (int)v[4];
            var scenes = new List<int> { (int)v[0] };
            foreach (int k in new[] { w - 1, w + 1 })
                if (k >= 0 && k < ins.Count && ins[k].Bank == 2002 && ins[k].ID == 3) scenes.Add((int)V(ins[k])[0]);
            int before = 0, after = 0;
            foreach (int sc in scenes)
            {
                if (SceneArea(sc) == curArea && SceneBlock(sc) == curBlock && before == 0) before = ctx.Demo(sc) ?? 0;
                else if (SceneArea(sc) == toArea && SceneBlock(sc) == toBlock && after == 0) after = ctx.Demo(sc) ?? 0;
            }
            warpDemos[ins[w]] = (before, after);
        }
    }

    /// <summary>Session 9 (rule B1): the DS1 bonfire objects of this map (the entities its 2009:3 Register Bonfire names).</summary>
    HashSet<int> Bonfires => bonfires ??= emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2009 && i.ID == 3)
        .Select(V).Where(v => v.Length > 1).Select(v => (int)v[1]).ToHashSet();
    HashSet<int>? bonfires;

    /// <summary>
    /// Session 9 (track firelink), rule T1: DS1 resets a map's temporary flags 1AAB5000..1AAB5999 (the 5nnn sub-range of its
    /// flag block; ids of the events that rerun every load) whenever the map loads. Evidence in DS1's own scripts: Firelink's
    /// 11020800 instances set 11025300 at load and 11020899 reads it one frame later to decide the relief chest's reroll
    /// (meaningful only if it starts OFF at every load); the nest's 11025060 ("curled up") is set ON on a press and its
    /// event restarts at load without clearing it; the Asylum's 11815394 waits for 11815396 OFF before showing the bar
    /// again (boss track). DS2 (no per-load block; FlagRules F2 puts them in the host map's map-slot block, which DS2 keeps
    /// in the save): the map's translated event 0 clears each one its events use with 130101(flag, 0) in its
    /// first state (session 9 npcflags fix: the first version used 130131(first, last, 0), which sets EventFlagListParam
    /// rows, not a flag range, so it did nothing),
    /// and every other group that reads or writes one of them starts after <see cref="Settle"/> s (event 0's reset is in
    /// by then whatever order DS2 steps the groups in). This returns the DS2 id of a temporary flag of this map, or null.
    /// </summary>
    int? TemporaryFlag(int ds1Flag)
    {
        int first = 10_000_000 + int.Parse(ctx.Ds1Map[1..3]) * 100_000 + int.Parse(ctx.Ds1Map[4..6]) % 10 * 10_000 + 5000;
        return ds1Flag >= first && ds1Flag < first + 1000 ? ctx.Flag(ds1Flag) : null;
    }

    /// <summary>Session 9 (rule T1): the event flags a DS1 instruction reads or writes (flag tests, sets, batches).</summary>
    IEnumerable<int> FlagArgs(EMEVD.Instruction x)
    {
        var v = V(x);
        static IEnumerable<int> Span(double a, double b) => b >= a && b - a < 1000 ? Enumerable.Range((int)a, (int)(b - a) + 1) : [];
        return (x.Bank, x.ID) switch
        {
            (3, 0) or (1003, 1) or (1003, 2) when v.Length > 3 && (int)v[2] == 0 => [(int)v[3]],
            (3, 1) or (1003, 3) or (1003, 4) when v.Length > 4 => Span(v[3], v[4]),
            (2003, 2) when v.Length > 0 => [(int)v[0]],
            (2003, 22) or (2003, 17) when v.Length > 1 => Span(v[0], v[1]),
            _ => [],
        };
    }

    /// <summary>
    /// Session 9 (track firelink): what the map's events need known before any is translated: the events another event
    /// restarts (2003:8 Set Event State, Restart: the target gets the restart test in its states, rule R9) and the DS1
    /// temporary flags the events use, own flags of 1AAB5nnn events included (rule T1: event 0 clears their span).
    /// </summary>
    void ScanCalls(List<(long Id, int Slot, byte[] Args)> calls, Dictionary<long, EMEVD.Event> byId)
    {
        foreach (var c in calls)
        {
            if (ctx.ResetsTemporaryFlags && TemporaryFlag((int)c.Id + c.Slot) is int own) TemporaryFlags.Add(own);
            foreach (var x in byId[c.Id].Instructions.Select((x, i) => Substitute(byId[c.Id], i, x, c.Args)))
            {
                if (x.Bank == 2003 && x.ID == 8 && V(x) is { Length: >= 3 } v && (int)v[2] == 1)
                {
                    restartTargets.Add(((long)v[0], (int)v[1]));
                    // session 9 (track npcflags): the request flag is temporary too (FlagRules F3): cleared at load with them
                    if (ctx.ResetsTemporaryFlags && FlagRules.Restart(ctx.Ds1Map, (int)v[0] + (int)v[1]) is int rq) TemporaryFlags.Add(rq);
                }
                if (ctx.ResetsTemporaryFlags)
                    foreach (int f in FlagArgs(x))
                        if (TemporaryFlag(f) is int t) TemporaryFlags.Add(t);
            }
        }
        // session 9 (track npcflags): the whole temporary list of the map (FlagRules F2/F3), also the ones only its talk
        // scripts use (the Firelink merchant's per-visit 11025022), so event 0 clears every one at load
        if (ctx.ResetsTemporaryFlags)
            foreach (int id in FlagRules.TemporaryIds(ctx.Ds1Map)) TemporaryFlags.Add(id);
        // session 11 (merge11, enemy-audit G4), rule T1-B: the placed bosses' logic flags reset at load (DS1's AI state
        // 2004:1 is not saved; the flag sits in DS2's block k = 2 as vanilla's 1BB020080, which only a rest zeroes)
        if (ctx.ResetsTemporaryFlags)
            foreach (int lf in ctx.BossLogicFlags) TemporaryFlags.Add(lf);
        // session 11 (fx11), rule E-REQ-P: DS1's AI requests are per load too (a fresh character has none)
        if (ctx.ResetsTemporaryFlags)
            foreach (int cf in ctx.AiChannelFlags) TemporaryFlags.Add(cf);
        // session 11 (dchr11), rule WK2: the posed large regulars' wake flags are per load too (DS1 restarts them posed)
        if (ctx.ResetsTemporaryFlags && Maps.MapEnemies.WakeFlags.TryGetValue(ctx.Ds1Map, out var wakes))
            foreach (int wf in wakes.Values) TemporaryFlags.Add(wf);
    }

    /// <summary>Session 9 (rule T1): a state reads (f6) or sets (130101) one of the flags.</summary>
    static bool TouchesFlags(ESD.State st, IReadOnlySet<int> flags)
    {
        bool At(byte[] b, int i) => i + 4 < b.Length && b[i] == 0x82 && flags.Contains(BitConverter.ToInt32(b, i + 1));
        foreach (var c in st.EntryCommands.Concat(st.Conditions.SelectMany(k => k.PassCommands)))
            if (c.CommandID is 130101 && c.Arguments.Count > 0 && At(c.Arguments[0], 0)) return true;
        foreach (var k in st.Conditions)
            for (int i = 0; i + 1 < k.Evaluator.Length; i++)
                if (k.Evaluator[i] == 0x46 && At(k.Evaluator, i + 1)) return true;
        return false;
    }

    /// <summary>
    /// DS1 common.emevd events instanced for this map (step 2): common.emevd's event 0 initialises shared events per boss and
    /// NPC with that character's flags (event 970: a boss's rewards when its defeat flag turns on). A slot whose first argument
    /// is a flag this map's own events set (2003:2 … ON) or own (an event id) belongs to this map and is translated with the
    /// map's context (DS1 runs it everywhere; the reward only fires where its flag is set). Group ids continue the map's.
    /// </summary>
    public void TranslateCommon(EMEVD common, Func<long, int, int> groupId, IEnumerable<long>? only = null)
    {
        var byId = common.Events.ToDictionary(e => e.ID);
        if (!byId.TryGetValue(0, out var root)) return;
        var mapFlags = emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2003 && i.ID == 2).Select(V).Where(v => v.Length >= 2 && v[1] == 1).Select(v => (int)v[0])
            .Concat(emevd.Events.Select(e => (int)e.ID)).ToHashSet();
        var want = only?.ToHashSet();
        foreach (var ins in root.Instructions.Where(i => i.Bank == 2000 && i.ID == 0))
        {
            long id = BitConverter.ToUInt32(ins.ArgData, 4);
            int slot = BitConverter.ToInt32(ins.ArgData, 0);
            byte[] args = ins.ArgData[8..];
            if (want != null && !want.Contains(id) || !byId.ContainsKey(id) || args.Length < 4) continue;
            int flag = BitConverter.ToInt32(args, 0);
            if (!mapFlags.Contains(flag)) continue;
            var r = Translate(byId[id], slot, args, groupId(id, slot), foldLoadWrites: false);
            Results.Add(r with { EventId = id, Common = true });
        }
    }

    /// <summary>2002:4 Play Cutscene and Warp Player to another map.</summary>
    bool IsCrossMapWarp(EMEVD.Instruction x) => x.Bank == 2002 && x.ID == 4 && V(x) is var v && v.Length >= 5
        && !((int)v[3] == int.Parse(ctx.Ds1Map[1..3]) && (int)v[4] == int.Parse(ctx.Ds1Map[4..6]));

    /// <summary>
    /// The cross-map warp rule: DS1 keeps running an event during "Play Cutscene and Warp Player" (the cutscene plays,
    /// then the warp; the instructions after it run meanwhile: 11810000 sets 11810000, the respawn point and saves after
    /// its warp), but DS2 leaves the map when 132125 runs. So the run of side-effect instructions right after a cross-map
    /// warp (flag sets, waits, flag SKIPs, saves, achievements, backread, cutscenes) moves in front of it, up to the
    /// next other instruction (11810150: the Restart). Not when a SKIP would land beyond the run or a SKIP before the warp
    /// lands inside it (the order would change what runs).
    /// </summary>
    List<EMEVD.Instruction> HoistCrossMapWarps(List<EMEVD.Instruction> ins)
    {
        static bool Movable(EMEVD.Instruction x) => (x.Bank, x.ID) is (1001, 0) or (1001, 1) or (1003, 1) or (1000, 3) or (2003, 2) or (2003, 22)
            or (2003, 23) or (2000, 5) or (2003, 28) or (2004, 29) or (2000, 2) or (2002, 3) or (2009, 4) or (2009, 6);
        int? SkipCount(EMEVD.Instruction x) => (x.Bank, x.ID) is (1000, 1) or (1000, 3) or (1000, 5) or (1000, 7) or (1003, 1) or (1003, 5) or (1003, 7) ? (int)V(x)[0] : null;
        var list = ins.ToList();
        for (int w = 0; w < list.Count; w++)
        {
            if (!IsCrossMapWarp(list[w])) continue;
            int r = w + 1;
            while (r < list.Count && Movable(list[r])) r++;
            if (r == w + 1) continue;
            bool ok = true;
            for (int i = 0; i < r && ok; i++)
                if (i != w && SkipCount(list[i]) is int n)
                    ok = i < w ? !(i + 1 + n > w && i + 1 + n < r) : i + 1 + n <= r;
            if (!ok) continue;
            var warp = list[w];
            list.RemoveAt(w);
            list.Insert(r - 1, warp);
            w = r - 1;
        }
        return list;
    }

    /// <summary>Track nests: the DS1 flags the map's events set only as they leave the map (see <see cref="DepartureFlags"/>).</summary>
    HashSet<int> departureFlags = [];

    /// <summary>
    /// Track nests (fix pass): the departure flags of a map. DS1 sets them only after a cross-map warp (11810000 sets
    /// 11810000 after 2002:3 180125 and 2002:4 100225), i.e. once the player has left; the hoist above sets them before
    /// DS2's 132125, so a same-map wait on one would pass while the player is still there (and its pre-warp demo plays).
    /// A flag is a departure flag when every ON/toggle set of it in the map's initialised events is an instruction the
    /// hoist moved in front of a cross-map warp, or the End of an event that warps away (no End-type exit before the warp
    /// other than "END IF its own flag ON"). Only this map's script is scanned.
    /// </summary>
    HashSet<int> DepartureFlags(List<(EMEVD.Event Ev, int Slot, byte[] Args)> runs)
    {
        var departure = new HashSet<int>();
        var other = new HashSet<int>();
        foreach (var (ev, slot, args) in runs)
        {
            var ins = ev.Instructions.Select((x, i) => Substitute(ev, i, x, args)).ToList();
            var hoisted = HoistCrossMapWarps(ins);
            var moved = new HashSet<EMEVD.Instruction>(ReferenceEqualityComparer.Instance);
            int firstWarp = ins.Count;
            for (int w = 0; w < ins.Count; w++)
            {
                if (!IsCrossMapWarp(ins[w])) continue;
                firstWarp = Math.Min(firstWarp, w);
                int to = hoisted.FindIndex(h => ReferenceEquals(h, ins[w]));
                for (int k = w + 1; k <= to; k++) moved.Add(ins[k]);
            }
            int Flag(double type, double id) => (int)type switch { 1 => (int)ev.ID, 2 => (int)ev.ID + slot, _ => (int)id };
            foreach (var x in ins.Where(x => x.Bank == 2003))
            {
                var v = V(x);
                if (x.ID == 2 && v.Length >= 2 && (int)v[1] != 0) (moved.Contains(x) && (int)v[1] == 1 ? departure : other).Add((int)v[0]);
                else if (x.ID == 22 && v.Length >= 3 && (int)v[2] != 0)
                    for (long f = (long)v[0]; f <= (long)v[1] && f - (long)v[0] < 100000; f++) other.Add((int)f);
            }
            int own = (int)ev.ID + slot;
            bool endsBefore = firstWarp == ins.Count || ins.Take(firstWarp).Any(x => (x.Bank, x.ID) switch
            {
                (1000, 2) or (1000, 4) or (1000, 6) or (1000, 8) or (1003, 6) => (int)V(x)[0] == 0,
                (1003, 2) => V(x) is var v && (int)v[0] == 0 && !(v[1] != 0 && Flag(v[2], v[3]) == own),
                _ => false,
            });
            (endsBefore ? other : departure).Add(own);
        }
        departure.ExceptWith(other);
        return departure;
    }

    /// <summary>
    /// Track nests (fix pass), the departure-flag wait rule: DS1 releases a MAIN wait "IF Event Flag ON" on a departure
    /// flag only after the player has left the map, so what it does next there is never seen. When that continuation, up
    /// to the next MAIN wait on the player (an action button, the player in an area: he is gone), only changes per-load
    /// display state (2005:3 (De)activate Object, 2004:5 character enable: DS1's events set them again at every load), the
    /// wait is tested once (flag already on → pass) and otherwise parks until the next load; the map's next load reads the
    /// flag as DS1 does. The Asylum's cliff nest (11810150: hide; IF 11810000; show; IF action button 1812001) stays
    /// hidden while 11810000's hoisted flag set runs before the crow warp (pre-warp demo 180125 and the warp fade), as in
    /// DS1. A continuation with a persistent effect (11810350 resets the boulder flag 11810211) keeps the live wait.
    /// </summary>
    bool ParksAtDeparture(List<EMEVD.Instruction> ins, int i, int ds1Flag)
    {
        if (!departureFlags.Contains(ds1Flag)) return false;
        for (int k = i + 1; k < ins.Count; k++)
        {
            var y = ins[k];
            if ((y.Bank, y.ID) is (2005, 3) or (2004, 5)) continue;
            var w = V(y);
            return w.Length >= 3 && (int)w[0] == 0 && ((y.Bank, y.ID) is (3, 5) or (3, 18) || (y.Bank, y.ID) is (3, 2) && (int)w[2] == Player);
        }
        return false; // the event's End sets its own flag (persistent)
    }

    /// <summary>Rule G11: a departure continuation folded into the event's load-time branch (instruction indices of the
    /// hoisted list: the SKIP over the wait, its target, the wait; the continuation's persistent flag writes).</summary>
    public sealed record Fold(int SkipAt, int Target, int WaitAt, int DepartureFlag, List<EMEVD.Instruction> Writes, List<int> Ds2Flags);
    readonly Dictionary<(long Event, int Slot), Fold> folds = [];
    /// <summary>Rule G11: the DS2 flags folded continuations write at load (other groups that read one start after
    /// <see cref="FoldSettle"/> s, as T1's readers do).</summary>
    public HashSet<int> FoldFlags { get; } = [];
    /// <summary>State time before a group that reads a G11-folded flag starts (twice T1's settle: the folding group may
    /// itself wait T1's settle first).</summary>
    public const float FoldSettle = 2 * Settle;

    /// <summary>
    /// Rule G11 (session 10): DS1 keeps a live wait on a departure flag D when what follows has a persistent effect
    /// (<see cref="ParksAtDeparture"/>): Asylum 11810350 waits for 11810000 ("carried off by the crow"), then clears the
    /// boulder flag 11810211 so the boulder rolls once more on the return trip. DS1 runs it during the crow cutscene;
    /// DS2 unloads the map shortly after the hoisted flag set (132125), so the continuation may never run. The event's
    /// load-time branch for D ON (DS1: `SKIP IF D ON` over the wait) then repeats the continuation's persistent flag
    /// writes once, guarded by the event's own flag: DS1 sets it only when the event ends, which happens in the departing
    /// run right after the continuation or in the first load-time run, so "own flag OFF with D ON" = the first load after
    /// a departure whose continuation didn't finish (U in game: whether DS2 ran it before the unload). Applies when the
    /// load-time branch has no further wait (it ends in that load, setting the own flag) and the own flag has storage.
    /// Generalises to every DS1 "world switch" event of the form IF departure flag → persistent flag writes.
    /// </summary>
    Fold? DepartureFold(List<EMEVD.Instruction> ins, int ownDs1Flag)
    {
        if (ctx.Flag(ownDs1Flag) == null) return null;
        static bool MainWait(EMEVD.Instruction y, double[] w) => y.Bank is 0 or 1 or 3 or 4 or 5 or 11 && w.Length > 0 && (int)w[0] == 0;
        for (int i = 0; i < ins.Count; i++)
        {
            var v = V(ins[i]);
            if ((ins[i].Bank, ins[i].ID) != (3, 0) || v.Length < 4 || (int)v[0] != 0 || v[1] == 0 || (int)v[2] != 0) continue;
            int d = (int)v[3];
            if (!departureFlags.Contains(d) || ParksAtDeparture(ins, i, d)) continue;
            int s = -1;
            for (int k = 0; k < i; k++)
                if ((ins[k].Bank, ins[k].ID) == (1003, 1) && V(ins[k]) is var w && w[1] != 0 && (int)w[2] == 0 && (int)w[3] == d && k + 1 + (int)w[0] > i) s = k;
            if (s < 0) continue;
            int target = s + 1 + (int)V(ins[s])[0];
            var writes = new List<EMEVD.Instruction>();
            for (int k = i + 1; k < ins.Count; k++)
            {
                var y = ins[k];
                if (MainWait(y, V(y)) || y.Bank is 1000 or 1003) break;
                if ((y.Bank, y.ID) is (2003, 2) or (2003, 22)) writes.Add(y);
            }
            if (writes.Count == 0) continue;
            if (ins.Skip(target).Any(y => MainWait(y, V(y)))) continue; // the load-time run must end in that load
            var ds2 = new List<int>();
            foreach (var y in writes)
            {
                var w = V(y);
                for (int f = (int)w[0]; f <= ((y.ID == 22) ? (int)w[1] : (int)w[0]) && f - (int)w[0] < 1000; f++)
                    if (ctx.Flag(f) is int g) ds2.Add(g); else return null;
            }
            return new Fold(s, target, i, d, writes, ds2);
        }
        return null;
    }

    /// <summary>
    /// Rule G2 (track nestfix fix pass): DS1's instructions before a wait (ins[0..upTo)) can rerun without changing anything,
    /// so "back to that wait" may be a re-execute (130002): only SKIP/END/flag tests (1000, 1003), conditions registered
    /// into groups, MAIN waits on flags, per-load display state (2005:3 objects, 2006:1/2 SFX, 2004:5 characters: as
    /// <see cref="ParksAtDeparture"/>) and network sync. Firelink 11020020 (nothing before its button), the Asylum's
    /// 11810150 (flag test, show the nest) and the courtyard fog 11810090 (flag test) qualify.
    /// </summary>
    bool ReplaySafe(List<EMEVD.Instruction> ins, int upTo)
    {
        for (int k = 0; k < upTo && k < ins.Count; k++)
        {
            var x = ins[k];
            if (x.Bank is 1000 or 1003 || (x.Bank, x.ID) is (2005, 3) or (2006, 1) or (2006, 2) or (2004, 5) or (2000, 2)) continue;
            if (x.Bank is 0 or 1 or 3 or 4 or 5 or 11 && V(x) is { Length: > 0 } v && ((int)v[0] != 0 || (x.Bank, x.ID) is (3, 0) or (3, 1))) continue;
            return false;
        }
        return true;
    }

    double[] V(EMEVD.Instruction x) => Decode(defs, x);

    /// <summary>The instruction's arguments, decoded by type with natural alignment (floats as their value).</summary>
    public static double[] Decode(Dictionary<(int, int), (string Name, int[] Types)> defs, EMEVD.Instruction x)
    {
        if (!defs.TryGetValue((x.Bank, x.ID), out var d)) return [];
        var a = x.ArgData;
        var v = new double[d.Types.Length];
        int off = 0;
        for (int i = 0; i < d.Types.Length; i++)
        {
            int t = d.Types[i], size = t is 0 or 3 ? 1 : t is 1 or 4 ? 2 : 4;
            off = (off + size - 1) / size * size;
            if (off + size > a.Length) break;
            // every arm as double: with a float arm the switch's common type was float, which rounded every integer above
            // 2^24 (session 8 fix: DS1 61810105 read as 61810104, so 11810313 tested the F2 West door's flag)
            v[i] = t switch
            {
                0 => (double)a[off], 1 => BitConverter.ToUInt16(a, off), 2 => BitConverter.ToUInt32(a, off), 3 => (sbyte)a[off],
                4 => BitConverter.ToInt16(a, off), 5 => BitConverter.ToInt32(a, off), 6 => (double)BitConverter.ToSingle(a, off), _ => 0d,
            };
            off += size;
        }
        return v;
    }

    // ---------------------------------------------------------------- one event
    /// <summary>A white-door traversal: the player first in the box on the button's side, then in the far box.</summary>
    sealed record FogBranch(int From, int To, Cond? Others);

    sealed class Cond
    {
        public byte[] Expr = [];                 // DS2 expression bytecode without the final A1
        public List<ESD.CommandCall> Setup = []; // group-condition commands it needs (slots assigned when registered)
        public List<ESD.CommandCall> Pass = [];  // commands run when the wait/test passes (action buttons: prompt off again)
        public bool IsAction;                    // an action button (its prompt is switched on only while waited for)
        public Cond? Pre;                        // AND group with action buttons: the other terms, waited for first
        public List<FogBranch>? Fog;             // white-door traversals (a wait on them becomes a crossing detector)
        public List<Cond>? Direct;               // OR group with fog branches: the other terms (exit straight away)
        public Cond? Leave;                      // two-stage wait: back to stage 1 when this holds (area buttons: left the area)
        public int OutsideOf;                    // rule G1 (track nestfix): this Leave is "outside region N", registered positively
    }

    /// <summary>State time (s) before a state reads the group-condition slots it registers, or takes its fallback.</summary>
    public const float Settle = 0.1f;
    /// <summary>State time (s) before the crossing detector concludes the player left both boxes.</summary>
    public const float AbortSettle = 0.5f;
    /// <summary>Rule WE4b-L: state time (s) before a load-time end branch re-applies its hits (the targets' own load branches,
    /// which hide what DS1 recorded as destroyed, have run by then: they start in the first frames, or at FoldWait 0.2 s).</summary>
    public const float ReapplySettle = 0.5f;
    /// <summary>(f3() >= s) != 0: the state has run for s seconds (0x94 = >= as DS2 runs it, rulebook §7; label only, chrint
    /// stage 3: the bytes were always 0x94).</summary>
    static byte[] StateTime(float s) => [0x43, 0x84, 0xA6, 0x80, .. BitConverter.GetBytes(s), 0x94, 0x40, 0x96];
    static byte[] StateTimeEnd(float s) => [.. StateTime(s), 0xA1];

    /// <param name="foldLoadWrites">fold DS1 event 0's load writes into the event's leading flag tests (map events: event 0
    /// initialises them; not common.emevd's)</param>
    Result Translate(EMEVD.Event ev, int slot, byte[] args, int gid, bool foldLoadWrites = true)
    {
        var missing = new List<string>();
        var absent = new List<string>();
        var folded = new List<string>();
        int owner = 0;
        bool fogEvent = false, keyDoorWait = false;
        var helpers = new HashSet<int>();
        var timed = new HashSet<ESD.Condition>(); // conditions with their own state-time guard (the settle pass leaves them)
        var substituted = ev.Instructions.Select((x, i) => Substitute(ev, i, x, args)).ToList();
        // session 10, rule K3 (doors.md rev. 2): an event whose only effect is granting the player a SpEffect that gates an
        // ObjAct (11810120: 4150 "inside key" while in 1812020, or in 1812021 once the door is open) is DS2's 131602 lock
        // region; translating it gave a 3-s polling loop that did nothing. Not emitted (no group, no EventParam row)
        bool SpGrant(EMEVD.Instruction x) => x is { Bank: 2004, ID: 8 } && V(x) is var g && (int)g[0] == Player && ctx.SpEffectGatesObjAct((int)g[1]);
        if (substituted.Any(SpGrant) && substituted.All(x => SpGrant(x) || x.Bank is 0 or 1 or 3 or 4 or 5 or 11 or 1000 or 1001 or 1003 || (x.Bank, x.ID) == (2000, 2)))
            return new Result(ev.ID, slot, gid, null, ["rule K3: SpEffect key grant only (the door's 131602 lock region is the side test; nothing to emit)"], 0, []);
        // session 10 (asy-sound, rule SND1): a switched sound that starts ON is a keyed region DS2 never autoplays, so the
        // group of the load-time event deciding it starts it first (as a DS1 2010:3 (entity, 1), translated like every
        // other), then runs DS1's own logic (fix pass: was event 0 for all, racing the deciding group); event 0's slot 0,
        // or the instance of the deciding event that switches this entity
        var soundStarts = ctx.SoundsStartedIn(ev.ID).Where(e => ctx.Sound(e) != null
            && (ev.ID == 0 ? slot == 0 : substituted.Any(x => x.Bank == 2010 && x.ID == 3 && x.ArgData.Length >= 4 && BitConverter.ToInt32(x.ArgData, 0) == e))).ToList();
        substituted.InsertRange(0, soundStarts.Select(e => new EMEVD.Instruction(2010, 3, [.. BitConverter.GetBytes(e), 1, 0, 0, 0])));
        // session 10 (e3), rule N3: an NPC death event (HP ≤ 0 → state block off → dead flag on) waits DeathSettle after its
        // death test, so the NPC's talk machine, which carries the same death AND the flag-gated drop rows (TalkTranslator's
        // death branch: "dead && dead flag still off"), goes first; DS2's forced death (131721, rule N1) removes the
        // character without the talk's branch, and then this event alone sets the flags (as DS1's own event does)
        // (review e3: not on an event with a G11 departure fold, whose SkipAt/Target index the uninserted list)
        int deathAt = CharacterEvents.DeathWait(substituted, V, out int dying);
        if (deathAt >= 0 && ctx.Generator(dying) != null && !folds.ContainsKey((ev.ID, slot)))
        {
            // review e3, rule N3b: the port keeps ONE death per NPC (NpcPorter.FindDeath: its dead flag is the generator's
            // respawnDisableFlag / start condition and the talk machine's death branch). A second DS1 death event of the
            // same NPC (Petrus 11020569: block 1190-1209 off, 1198 on, while the port's 11020567 / talk set 1196) would
            // clear that dead flag after the engine set it (the NPC back at the next load): not translated, reported
            if (PortedDeathEvent(dying) is long ported && ported != ev.ID)
                return new Result(ev.ID, slot, gid, null, [$"rule N3b: a second death event of NPC {dying} (the port's death is DS1 event {ported}: its dead flag drives the generator and the talk machine)"], 0, []);
            substituted = InsertAfter(substituted, deathAt, new EMEVD.Instruction(1001, 0, BitConverter.GetBytes(CharacterEvents.DeathSettle)));
            absent.Add($"rule N3: the death writes wait {CharacterEvents.DeathSettle} s after the death test (the talk machine's death branch first)");
        }
        // session 11 (ret11), rule XM1: DS1 marks another map as done when the player arrives here WITHOUT having been in that map
        // ("SKIP IF Player In Map X (inside)" over "Set Event Flag(X's flag, ON)": m10_02 11020001 [2]-[4] sets 11810000 = left the
        // Asylum, DS1's fallback for a start in Firelink). DS1 has no other way into this map; the port has (DS2 travel, test
        // warps), and the write would turn the player's first Asylum visit into the return trip. Dropped (a 0 s wait in its
        // place, so no skip count moves); the map's own departure event still sets the flag (11810000's crow).
        // Session 11 (fix11, user decision): only with build-pack --entry cell. With the Firelink entry (default) DS1's fallback is
        // exactly right: arriving in Firelink without the Asylum marks it left, so the Asylum is the return trip.
        for (int i = 0; !Build.Maps.EntryItem.ToFirelink && i < substituted.Count; i++)
        {
            var sk = substituted[i];
            if ((sk.Bank, sk.ID) != (1003, 7) || V(sk) is not { Length: >= 4 } sv || sv[1] == 0) continue;
            string aab = $"{(int)sv[2]:D2}{(int)sv[3] % 10}";
            for (int j = i + 1; j <= Math.Min(substituted.Count - 1, i + (int)sv[0]); j++)
                if ((substituted[j].Bank, substituted[j].ID) == (2003, 2) && V(substituted[j]) is { Length: >= 2 } fv && fv[1] == 1
                    && ((int)fv[0]).ToString() is { Length: 8 } fs && fs[0] == '1' && fs.Substring(1, 3) == aab && !fs.StartsWith("1" + aab + "5"))
                {
                    substituted[j] = new EMEVD.Instruction(1001, 0, BitConverter.GetBytes(0f));
                    absent.Add($"rule XM1: DS1's fallback write of map m{(int)sv[2]:D2}_0{(int)sv[3]}'s flag {(int)fv[0]} when the player has not been there (the port reaches this map by other routes)");
                }
        }
        PairWarpDemos(substituted);
        var ins = HoistCrossMapWarps(substituted);
        // event 0's load writes fold into the leading flag tests (the run of SKIP/END IF Event Flag the event starts with),
        // only in events that never restart within the load (a restart re-runs those tests after event 0's writes are in)
        bool restarts = ins.Any(x => (x.Bank, x.ID) is (1000, 4) or (1000, 2) or (1000, 6) or (1000, 8) or (1003, 2) or (1003, 6) && V(x) is var rv && rv.Length > 0 && (int)rv[0] == 1);
        int leading = foldLoadWrites && ev.ID is not (0 or 50) && !restarts ? ins.TakeWhile(x => (x.Bank, x.ID) is (1003, 1) or (1003, 2)).Count() : 0;
        // DS1 entities this event gave a start-up state (2004:9 waiting animation → 131702), and per instruction of a boss's
        // wake-clip playback the generators whose start-up DS1 clears before it (reset after the wake: see the 2004:9 rule)
        var overridden = new HashSet<int>();
        var resetAfter = new Dictionary<int, List<int>>();
        const long End = 9000; // state 1 + i for instruction i; End = 9000 (sets the event flag), 9001 terminal
        const long Init = 8999; // helpers' prompts off before the first instruction (state 0 can't have entry commands)
        var states = new Dictionary<long, ESD.State>();
        long St(int i) => i >= ins.Count ? EndState() : 1 + i;
        var groups = new Dictionary<int, List<Cond>>();
        // DS1 clears its condition groups when a MAIN wait passes but keeps their results for the "(Compiled)" tests
        // (1000:7/8); DS2 re-evaluates the same expressions there (the terms are flags/regions that don't change between)
        var compiled = new Dictionary<int, List<Cond>>();
        // session 11 (burg11), rule CR16 (opt-in CharacterRulesM3): DS1 keeps every group's compiled result until that group
        // is evaluated again, so a "(Compiled)" test may name a group of an EARLIER MAIN wait (m10_01 11010001: groups 1/2 of the
        // first wait, tested after the wait on group 3); the latest compilation of each group is kept here
        var everCompiled = new Dictionary<int, List<Cond>>();
        int slotCounter = 0;
        long extraState = 6000;
        bool readsClass = false; // session 10 (asy-items), rule C1: the event tests the DS1 class (4:11)

        // a boss this event warps, tests or fights: without its generator the event is not translated (see summary)
        foreach (var x in ins)
        {
            var v = V(x);
            int? boss = (x.Bank, x.ID) switch
            {
                // session 10 (asy-items i2), rule K1: a death/HP test (4:0, 4:2) on an absent boss no longer keeps the whole
                // event out: like any absent character it is a condition that never comes (Never), so the event's
                // boss-independent part runs (11810300's first-load flags: Oscar's Big Pilgrim's Key stays DS1's "not
                // given" until the boss can be killed early)
                (2004, 41) or (2003, 12) => (int)v[0],
                (2003, 11) => (int)v[1],
                _ => null,
            };
            if (boss is int b && b != Player && ctx.IsBoss(b) && ctx.Generator(b) == null) missing.Add($"boss {b} absent (no generator yet)");
        }
        // session 9 (track firelink), rule B1: DS2 owns bonfires (rulebook principle 1: its bonfire object, menu, rest,
        // healing; no kindling, no Fire Keeper absence). A DS1 event that waits for an action button on a bonfire object is
        // dropped (Firelink 11020106 "Light bonfire" → "Fire Keeper is absent"); Register Bonfire (2009:3) is a plain
        // instruction drop in every event (fix pass: 11020105 keeps its Fire Keeper part, flag 102 and the Fire Keeper
        // Soul treasure, and drops only its kindling-30 line). Bonfire objects = the entities this map's 2009:3 registers.
        foreach (var x in ins)
        {
            var v = V(x);
            if (x.Bank == 3 && x.ID is 5 or 18 && v.Length > 2 && (int)v[1] == 0 && Bonfires.Contains((int)v[2]))
                missing.Add($"dropped by rule B1 (DS2 owns bonfires): action button on bonfire {(int)v[2]}");
        }

        long EndState()
        {
            if (!states.ContainsKey(End))
            {
                // DS1: an event that ends sets its own flag (event id, + slot for slotted events)
                var f = ctx.Flag((int)ev.ID + slot);
                states[End] = f is int fv ? S([C(130101, fv, 1)], (True, End + 1)) : S([], (True, End + 1));
                states[End + 1] = S([]);
            }
            return End;
        }
        long Finish(double endType) => (int)endType == 1 ? ReExecute() : EndState(); // END type 1 = Restart
        // rule G2 (track nestfix): a restart re-executes the event (130002, vanilla Majula g12000 state 1: no conditions)
        long ReExecute()
        {
            const long Rerun = 8995;
            if (!states.ContainsKey(Rerun)) states[Rerun] = S([C(130002)]);
            return Rerun;
        }

        // a group-condition command gets its slot when registered; its value is f140001(slot)
        Cond Grp(int cmd, params int[] rest)
        {
            int sl = slotCounter++ % 8;
            return new Cond { Setup = [C(cmd, [sl, .. rest])], Expr = [0x82, .. BitConverter.GetBytes(140001), (byte)(0x40 + sl), 0x85, 0xA6] };
        }
        Cond Never(string what) { absent.Add(what); return new Cond { Expr = [0x40] }; }
        // session 9 (track firelink): a batch flag test (3:1 / 1003:3 / 1003:4, DS1 "Logical Operation Type" 0 all ON, 1 all
        // OFF, 2 not all OFF, 3 not all ON) over event flags first..last (at most 32) → the single flag tests joined with
        // && (0, 1) or || (2, 3); Firelink's event 50 (Griggs: 1112..1114 not all OFF), 11020501 / 11020560 (Thorolund)
        byte[]? BatchExpr(double op, double type, double first, double last)
        {
            if ((int)type != 0 || last < first || last - first >= 32 || (int)op is < 0 or > 3) { missing.Add($"batch flags {first}..{last} (op {op}, type {type})"); return null; }
            byte[]? e = null;
            for (int f = (int)first; f <= (int)last; f++)
            {
                if (F(f) is not int d) return null;
                var t = FlagExpr(d, (int)op is 0 or 2);
                e = e == null ? t : [.. e, .. t, (int)op is 0 or 1 ? (byte)0x98 : (byte)0x99];
            }
            return e;
        }
        int? F(int ds1) { var r = ctx.Flag(ds1); if (r == null) missing.Add($"flag {ds1}"); return r; }
        int EventFlag(double type, double id) => (int)type switch { 1 => (int)ev.ID, 2 => (int)ev.ID + slot, _ => (int)id };

        Cond? Condition(EMEVD.Instruction x, double[] v)
        {
            switch (x.Bank, x.ID)
            {
                case (3, 0): // IF Event Flag(group, state, type, id)
                {
                    var f = F(EventFlag(v[2], v[3]));
                    return f == null ? null : new Cond { Expr = FlagExpr(f.Value, v[1] != 0) };
                }
                case (3, 1): // IF Batch Event Flags(group, op, type, first, last): session 9, see BatchExpr
                {
                    var e = BatchExpr(v[1], v[2], v[3], v[4]);
                    return e == null ? null : new Cond { Expr = e };
                }
                case (3, 2): // IF In/Outside Area(group, state, target, area): 140132(slot, loc, loc, 1 inside | 0 outside)
                {
                    if ((int)v[2] != Player && ctx.CharacterRulesM3)
                    {
                        // session 11 (burg11), rule CR12: a placed character in an area → 140131(slot, generator, loc, loc, 1|0) (vanilla m10_23)
                        if (ctx.Generator((int)v[2]) is not int gc) return Never($"chr {(int)v[2]}");
                        var rc = ctx.Region((int)v[3]);
                        if (rc == null) { missing.Add($"region {(int)v[3]}"); return null; }
                        return Grp(140131, gc, rc.Value, rc.Value, v[1] != 0 ? 1 : 0);
                    }
                    if ((int)v[2] != Player) { missing.Add($"3:2 target {(int)v[2]}"); return null; }
                    var r = ctx.Region((int)v[3]);
                    if (r == null) { missing.Add($"region {(int)v[3]}"); return null; }
                    return Grp(140132, r.Value, r.Value, v[1] != 0 ? 1 : 0);
                }
                case (11, 2): // IF Player Standing On Hit(group, hit)
                {
                    var r = ctx.HitRegion((int)v[1]);
                    if (r == null) { missing.Add($"hit {(int)v[1]}"); return null; }
                    return Grp(140132, r.Value, r.Value, 1);
                }
                case (3, 5) or (3, 18) or (3, 19) when x.ID != 19 || ctx.CharacterRulesM3: // IF Action Button(group, entity type, entity, …[, line segment entity]); 3:19 (boss room): rule CR7
                {
                    if (x.ID is 18 or 19 && (int)v[9] > 0 && ctx.FogCrossing((int)v[9], (int)v[2]) is var (from, to))
                    {
                        // traversing a DS2 white door: re-evaluated later as "the player is on the far side"
                        fogEvent = true;
                        var far = Grp(140132, to, to, 1);
                        far.Fog = [new FogBranch(from, to, null)];
                        return far;
                    }
                    var h = ctx.ActionHelper((int)v[2], (int)v[6]);
                    if (h == null) { missing.Add($"action button {(int)v[2]}"); return null; }
                    // the helper's prompt shows only while DS1 waits for this button
                    var c = Grp(140205, h.Value);
                    c.Setup.Insert(0, C(131638, h.Value, 0));
                    c.Pass.Add(C(131638, h.Value, 1));
                    c.IsAction = true;
                    helpers.Add(h.Value);
                    if ((int)v[1] == 1)
                    {
                        // an area (DS1: the prompt shows while the player is inside it): stage 1 waits for the player in
                        // DS1's area, stage 2 shows the helper's prompt and waits for it (still inside); leaving → stage 1
                        var r = ctx.Region((int)v[2]);
                        if (r == null) { missing.Add($"region {(int)v[2]} (action button area)"); return null; }
                        var inArea = Grp(140132, r.Value, r.Value, 1);
                        return new Cond
                        {
                            Setup = [.. inArea.Setup, .. c.Setup], Expr = [.. inArea.Expr, .. c.Expr, 0x98], Pass = c.Pass, IsAction = true,
                            Pre = inArea, Leave = new Cond { Expr = [.. inArea.Expr, 0x40, 0x95], Pass = [C(131638, h.Value, 1)], OutsideOf = r.Value },
                        };
                    }
                    return c;
                }
                case (5, 2): // IF ObjAct Event Flag(group, flag)
                {
                    int a = (int)v[1];
                    if (ctx.Lock(a) is { } lk)
                    {
                        // the DS2 door's lock: unlocked (DS2 sets the ObjAct's state flag) = DS1's ObjAct used
                        if (owner != 0 && owner != lk.Owner) { missing.Add($"objact {a}: two doors in one event"); return null; }
                        owner = lk.Owner;
                        keyDoorWait = true;
                        return new Cond { Setup = [C(131602, lk.Region, lk.Item, lk.UsedText, lk.FailText, lk.Flag, 0)], Expr = FlagExpr(lk.Flag, true) };
                    }
                    if (ctx.ObjActAbsent(a)) { keyDoorWait = true; return Never($"objact {a}"); } // never comes: its dialog is unreachable
                    var f = ctx.ObjActFlag(a);
                    if (f == null) { missing.Add($"objact {a}"); return null; }
                    return new Cond { Expr = FlagExpr(f.Value, true) };
                }
                case (4, 0): // IF Character Dead/Alive(group, entity, state 1 dead | 0 alive)
                {
                    int e = (int)v[1];
                    if (ctx.Generator(e) is not int g) return Never($"chr {e}");
                    // a boss's death: DS2's boss-defeated condition (【条件】ボス戦_終了判定 'ボスを倒したか？')
                    if (v[2] != 0 && ctx.IsBoss(e) && ctx.BossBattle(e, 0) is int bb) return Grp(140522, bb, 0, 1);
                    var c = Grp(140111, g);
                    if (v[2] == 0) c.Expr = [.. c.Expr, 0x40, 0x95];
                    return c;
                }
                case (4, 7): // IF Character Backread Status(group, entity, state): no DS2 rule for a present character yet
                {
                    int e = (int)v[1];
                    if (ctx.Generator(e) == null) return Never($"chr {e}");
                    // session 11 (ret11), rule BR1 (P): "IF backread (loaded) TRUE" on a placed generator = the state has run Settle s
                    // (DS2 creates a map's generators with the map; DS1 waits for the character's model to load)
                    if (v[2] != 0) return new Cond { Expr = StateTime(Settle) };
                    missing.Add($"4:7 backread status FALSE of chr {e}");
                    return null;
                }
                case (4, 2): // IF HP Ratio(group, entity, comparison, ratio): DS2 compares percent with DS1's comparison codes
                {
                    int e = (int)v[1];
                    if (ctx.Generator(e) is not int g) return Never($"chr {e}");
                    // session 10 (asy-items i2), rule K1b' (COVERAGE C23): HP ratio <= 0 (code 5) or == 0 (code 0) = "is dead",
                    // translated exactly like 4:0 dead for that character (boss → the boss-defeated form 140522, else the
                    // generator death 140111): one death test per character, no battle-gauge comparison code (U)
                    if (v[3] == 0 && (int)v[2] is 5 or 0)
                    {
                        if (ctx.IsBoss(e) && ctx.BossBattle(e, 0) is int bd) return Grp(140522, bd, 0, 1);
                        return Grp(140111, g);
                    }
                    // a boss: the boss battle's HP (Belfry Gargoyles 140523(slot, battle, 0, percent, 5); 0 = the battle's
                    // combined gauge)
                    if (ctx.IsBoss(e) && ctx.BossBattle(e, 0) is int bb) return Grp(140523, bb, 0, (int)Math.Round(v[3] * 100), (int)v[2]);
                    return Grp(140121, g, (int)Math.Round(v[3] * 100), (int)v[2]);
                }
                case (5, 0): // IF Object Destroyed(group, state 1 destroyed | 0 intact, entity)
                {
                    int e = (int)v[2];
                    if (ctx.Object(e) is int o) return Grp(140202, o, v[1] != 0 ? 1 : 0);
                    if (ctx.ObjectAbsent(e)) return Never($"object {e}");
                    missing.Add($"object {e}");
                    return null;
                }
                case (1, 0): // IF Elapsed Seconds(group, s)
                    return new Cond { Expr = [0x43, 0x84, 0xA6, 0x80, .. BitConverter.GetBytes((float)v[1]), 0x94, 0x40, 0x96] };
                // ---- session 11 (burg11, M3/M4): rules CR3-CR5, CR10 (opt-in IContext.CharacterRulesM3)
                case (4, 1) when ctx.CharacterRulesM3: // IF Character Damaged By(group, target, attacker): the target's HP below 100 % (any attacker: P)
                {
                    int e = (int)v[1];
                    if (e == Player) return Grp(140126);
                    if (ctx.Generator(e) is not int g) return Never($"chr {e}");
                    absent.Add($"rule CR3: chr {e} damaged by {(int)v[2]} = its HP below 100 % (DS2 has no attacker test)");
                    return Grp(140121, g, 100, 3);
                }
                case (3, 7) when ctx.CharacterRulesM3: // IF All Players In/Outside Area(group, state, area): the host's view = the player
                {
                    var r = ctx.Region((int)v[2]);
                    if (r == null) { missing.Add($"region {(int)v[2]}"); return null; }
                    return Grp(140132, r.Value, r.Value, v[1] != 0 ? 1 : 0);
                }
                case (3, 3) when ctx.CharacterRulesM3: // IF Entity In/Outside Radius Of Entity(group, state, a, b, radius[, count])
                {
                    int a = (int)v[2], b = (int)v[3];
                    int other = a == Player ? b : b == Player ? a : 0;
                    if (other == 0) { missing.Add($"3:3 radius between {a} and {b} (no player)"); return null; }
                    if (ctx.Generator(other) is not int g) return Never($"chr {other}");
                    var c = Grp(140101, g, (int)Math.Round(v[4]), 5); // vanilla 140101(slot, generator, distance, 5 = within)
                    if (v[1] == 0) c.Expr = [.. c.Expr, 0x40, 0x95];
                    return c;
                }
                // ---- session 11 (burg11, round 6): rules WT1 / IT1 (opt-in CharacterRulesM3)
                case (3, 11) when ctx.CharacterRulesM3: // IF World Tendency(group, type, comparison, threshold): DS2 has none → DS1's
                {                                       // starting tendency 0 compared once (single player never moves it here)
                    double t = v[3]; int cmp = (int)v[2];
                    bool r = cmp switch { 0 => 0 == t, 1 => 0 != t, 2 => 0 > t, 3 => 0 < t, 4 => 0 >= t, 5 => 0 <= t, _ => false };
                    absent.Add($"rule WT1: world tendency {(int)v[1]} {cmp} {t} = {(r ? "true" : "false")} (DS2 has no world tendency; DS1's 0)");
                    return new Cond { Expr = [(byte)(r ? 0x41 : 0x40)] };
                }
                case (3, 4) when ctx.CharacterRulesM3 && (int)v[1] == 3: // IF Player Has/Doesn't Have Item(group, goods, id, owns)
                {
                    if (ctx.GoodsItem((int)v[2]) is not int item) { missing.Add($"3:4 goods {(int)v[2]} (no DS2 item)"); return null; }
                    var c = Grp(140801, item, 1, 3, 1, 1, 0);   // rule IT1: K4's form 「所持アイテム数比較」 (count ≥ 1)
                    if (v[3] == 0) c.Expr = [.. c.Expr, 0x40, 0x95];
                    return c;
                }
                case (3, 12) when ctx.CharacterRulesM3: // IF Event Value(group, base flag, bits, comparison, value): rule EV1, the value's
                {                                       // patterns as an OR of flag ANDs (base flag = the most significant bit, DS1)
                    int bits = (int)v[2]; double want = v[4]; int cmp = (int)v[3];
                    if (bits is < 1 or > 6) { missing.Add($"3:12 event value of {bits} bits (EV1: up to 6)"); return null; }
                    var flags = new int[bits];
                    for (int b = 0; b < bits; b++) { if (ctx.Flag((int)v[1] + b) is not int fb) { missing.Add($"flag {(int)v[1] + b}"); return null; } flags[b] = fb; }
                    byte[]? expr = null;
                    for (int val = 0; val < 1 << bits; val++)
                    {
                        bool ok = cmp switch { 0 => val == want, 1 => val != want, 2 => val > want, 3 => val < want, 4 => val >= want, 5 => val <= want, _ => false };
                        if (!ok) continue;
                        byte[] term = [];
                        for (int b = 0; b < bits; b++)
                        {
                            byte[] lit = FlagExpr(flags[b], (val >> (bits - 1 - b) & 1) != 0);
                            term = b == 0 ? lit : [.. term, .. lit, 0x98];
                        }
                        expr = expr == null ? term : [.. expr, .. term, 0x99];
                    }
                    return new Cond { Expr = expr ?? [0x40] };
                }
                case (4, 6) when ctx.CharacterRulesM3: // IF NPC Part HP(group, entity, part, threshold, comparison): rule TC1-E, DS2's
                {                                      // part break f130310(generator, part 1) 「部位破壊判定（ジェネレータ指定版）」 (vanilla m20_21)
                    if (ctx.Generator((int)v[1]) is not int pg) return Never($"chr {(int)v[1]}");
                    if (!((int)v[4] is 5 or 0 && v[3] <= 0)) { missing.Add($"4:6 NPC part HP {v[4]} {v[3]} (only 'destroyed' has a rule)"); return null; }
                    absent.Add($"rule TC1-E: chr {(int)v[1]} NPC part {(int)v[2]} destroyed = DS2 part break 1 (BossPartsParam / hit parts: TC1 open)");
                    return new Cond { Expr = [0x82, .. BitConverter.GetBytes(130310), .. Push(pg), .. Push(1), 0x86, 0xA6, 0x40, 0x96] };
                }
                case (1, 1) when ctx.CharacterRulesM3: // IF Elapsed Frames(group, frames): rule CR14, 30 frames per second (as 1001:1)
                    return new Cond { Expr = [0x43, 0x84, 0xA6, 0x80, .. BitConverter.GetBytes((float)(v[1] / 30)), 0x94, 0x40, 0x96] };
                case (4, 8) when ctx.CharacterRulesM3: // IF Character Has Event Message(group, entity, message, state)
                {
                    // session 11 (chrai11), rule EM1: the character's translated AI writes the message's code on board slot 7 while its
                    // DS1 clip's window runs → 140171(slot, generator, 7, code, 0 = equal | 1 = not equal) 「ロジックのEzStateフラグを比較」
                    int e8 = (int)v[1], msg = (int)v[2];
                    if (ctx.EventMessage(e8, msg) is { } em)
                    {
                        absent.Add($"rule EM1: chr {e8} TAE message {msg} = board {Ai.Boss.EventMessages.Slot} of generator {em.Generator} == {em.Code}");
                        return Grp(140171, em.Generator, Ai.Boss.EventMessages.Slot, em.Code, v[3] != 0 ? 0 : 1);
                    }
                    // rule EM1-r: sent only by the NPC-part break reaction (DS2 plays it with the break): it has come (P)
                    if (ctx.ReactionMessage(e8, msg)) { absent.Add($"rule EM1-r: chr {e8} TAE message {msg} = its part-break reaction (DS2 plays it with the break): passes"); return new Cond { Expr = [0x41] }; }
                    return Never($"rule CR10: chr {e8} event message {msg} (DS1 TAE message no translated AI clip sends)");
                }
                case (4, 9) when ctx.CharacterRulesM3: // IF Character AI State(group, entity, state): session 11 (dchr11), rule CR21
                    // DS2's event ESD reads no other character's AI mode (the Depths' 11005050: the Butcher also wakes when dog 1000110 is in
                    // combat; its other OR'd triggers - player within 7 m, hit, table 1001400 broken - translate) → never, reported
                    return Never($"rule CR21: chr {(int)v[1]} AI state {(int)v[2]} (no DS2 event query of a character's AI mode)");
                case (3, 6): // IF Multiplayer State(group, state): rule C1 (session 10), see NetExpr
                {
                    if (NetExpr((int)v[1]) is not { } ne) { missing.Add($"3:6 multiplayer state {(int)v[1]} (no rule)"); return null; }
                    return new Cond { Expr = ne };
                }
                case (4, 3): // IF Character Type(group, entity, type) on the player (rule C1): a phantom (1 white, 2 black, …)
                             // is a guest = DS1 Client; Alive (0) and Hollow (8) together in ONE OR group = the world's own
                             // player in either form = DS1 Host (11810850 [6]/[7]). Fix pass: a lone Alive or Hollow test is
                             // DS1's human/hollow FORM, not Host (m10_01 11015100: summon sign only in human form): no rule
                             // until DS2's form test is established (f130307 「プレイヤーの生者亡者タイプを取得」, case 0x1fd03
                             // of FUN_14045c6a0, 0 vanilla uses; 140154 「プレイヤーの生者亡者判定」, 5 uses in m20_11 as
                             // (slot, 0, 1): value meanings U) → reported, the event is not translated
                {
                    if ((int)v[1] != Player) { missing.Add($"4:3 character type of {(int)v[1]}"); return null; }
                    int ct = (int)v[2];
                    if (ct is not (0 or 8)) return new Cond { Expr = NetExpr(1)! };
                    int other = ct == 0 ? 8 : 0;
                    bool pair = (int)v[0] < 0 && ins.Any(y => (y.Bank, y.ID) == (4, 3) && V(y) is var w && w.Length > 2
                        && (int)w[0] == (int)v[0] && (int)w[1] == Player && (int)w[2] == other);
                    if (!pair) { missing.Add($"4:3 player character type {(ct == 0 ? "Alive" : "Hollow")} alone (human/hollow form: no DS2 rule, f130307/140154 U)"); return null; }
                    return new Cond { Expr = NetExpr(0)! };
                }
                case (3, 17): // IF Game Cycle(group, comparison, completed cycles)
                {
                    // session 10 (e3), rule G5: f132241() 「ゲーム周回数取得」 (case 0x20491 of FUN_14045c6a0 → FUN_14019ee30: in a
                    // session the session's cycle, else GM+0xa8 → +0xc0 → +0x68, V-code) counts the playthrough in progress
                    // (vanilla m10_02 group 2147483620 state 「ゲーム2周目以上か？」 = f132241() >= 2, V-data); DS1 counts the
                    // completed ones → DS1 c ↔ DS2 c + 1. A direct function (negatable, rule G1), as vanilla reads it
                    if (CharacterEvents.EsdCmp((int)v[1]) is not byte op) { missing.Add($"3:17 comparison {(int)v[1]}"); return null; }
                    return new Cond { Expr = [0x82, .. BitConverter.GetBytes(FnGameCycle), 0x84, 0xA6, .. Push(CharacterEvents.Ds2Cycle((int)v[2])), op, 0x40, 0x96] };
                }
                case (4, 12): // IF Player's Covenant(group, DS1 covenant index)
                    // session 10 (e3), rule G5b: DS2's getter exists (f130361 「プレイヤーの誓約を取得」 = PlayerGameData+0x1ad,
                    // case 0x1fd39, V-code) but holds DS2's covenants (VowParam 1-9: Heirs of the Sun … Pilgrims of Dark), none of
                    // them DS1's. Session 11 (cov11), rule K2 replaces G5b's "never holds": DS1 covenants join DS2 ids by role
                    // (Mappingsvenants.tsv; Petrus' Way of White = Way of Blue 4, Darkmoon Blade = Blue Sentinels 2 …), so the
                    // test is f130361() == id (+ the sub-identity flag on a shared id), a direct expression (negatable, rule G1)
                    if (Build.Talk.Covenants.MemberBytes((int)v[1]) is { } member) return new Cond { Expr = member };
                    missing.Add($"4:12 DS1 covenant {(int)v[1]} ({CharacterEvents.Covenant((int)v[1])}): no covenants.tsv row / sub flag slot");
                    return null;
                case (3, 8): // IF Player In/Out Map(group, inside, area, block): the event runs in its own map
                {
                    bool here = (int)v[2] == int.Parse(ctx.Ds1Map[1..3]) && (int)v[3] == int.Parse(ctx.Ds1Map[4..6]);
                    return new Cond { Expr = [(byte)(0x40 + (here == (v[1] != 0) ? 1 : 0))] };
                }
                case (0, 0): // IF Condition Group(result, state, target)
                    return GroupExpr((int)v[2], v[1] != 0);
                case (3, 14) or (3, 15): // session 10 (asy-items i2): DS1 drop trades are rebuilt as a whole (rule S1, DropTrades)
                    missing.Add("drop trade (rule S1: DropTrades group)");
                    return null;
                case (4, 5): // IF Character Has SpEffect(group, entity, SpEffect, should have)
                {
                    // session 10 (asy-items i2 fix pass), rule R5: DS1 tests a ring by its SpEffect (the Abyss: Covenant of
                    // Artorias 2200; the "ring broke" messages: Ring of Sacrifice 2130/2131). DS2 tests the ring itself:
                    // f131403(item) 「装備しているアイテム数を取得」 > 0 (vanilla m20_21 g2147483444 s2 `f131403(40510000) > 0`,
                    // talk_m10_32 f131403(40610000); bytes 82 4B010200 82 item 85 A6 40 92). Only the player: DS2 has no
                    // equipment test on another character (logged by RingPorter as UNMAPPED).
                    if ((int)v[1] == Player && ctx.RingItem((int)v[2]) is int ring)
                        return new Cond { Expr = [0x82, .. BitConverter.GetBytes(131403), .. Push(ring), 0x85, 0xA6, 0x40, v[3] != 0 ? (byte)0x92 : (byte)0x95] };
                    missing.Add($"4:5 SpEffect {(int)v[2]} on {((int)v[1] == Player ? "the player (not a ring's SpEffect)" : $"chr {(int)v[1]}")}");
                    return null;
                }
                case (4, 11): // IF Player's Class(group, class): session 10 (asy-items), rule C1: the synthetic class flag
                {             // (OriginPrompt); the event starts once the class is known (see readsClass)
                    if (FlagRules.Ds1Origin((int)v[1]) is not int cf) { missing.Add($"4:11 class {(int)v[1]}: no ds1-origin flag (slot table S rows)"); return null; }
                    readsClass = true;
                    return new Cond { Expr = FlagExpr(cf, true) };
                }
                default:
                    missing.Add($"{x.Bank}:{x.ID} {Name(x)}");
                    return null;
            }
        }

        Cond? GroupExpr(int g, bool pass, bool fromCompiled = false)
        {
            if (!(fromCompiled ? compiled : groups).TryGetValue(g, out var list) || list.Count == 0)
            {
                if (fromCompiled && ctx.CharacterRulesM3 && everCompiled.TryGetValue(g, out var earlier) && earlier.Count > 0) list = earlier;
                else { missing.Add($"empty condition group {g}"); return null; }
            }
            var e = new Cond();
            byte op = g > 0 ? (byte)0x98 : (byte)0x99; // AND groups positive, OR groups negative
            for (int k = 0; k < list.Count; k++)
            {
                e.Setup.AddRange(list[k].Setup);
                e.Pass.AddRange(list[k].Pass);
                e.Expr = k == 0 ? list[k].Expr : [.. e.Expr, .. list[k].Expr, op];
            }
            if (!pass) e.Expr = [.. e.Expr, 0x40, 0x95]; // == 0
            // white-door traversals: AND = one traversal + other terms; OR = several traversals (+ direct terms)
            var fogs = list.Where(c => c.Fog != null).ToList();
            if (fogs.Count > 0 && pass)
            {
                if (g > 0)
                {
                    if (fogs.Count > 1 || fogs[0].Fog!.Count > 1) { missing.Add("two white-door traversals in one AND group"); return e; }
                    var others = list.Where(c => c.Fog == null).ToList();
                    var b = fogs[0].Fog![0];
                    e.Fog = [b with { Others = others.Count == 0 ? b.Others : Join(others.Append(b.Others).Where(c => c != null).Cast<Cond>().ToList()) }];
                }
                else
                {
                    e.Fog = fogs.SelectMany(c => c.Fog!).ToList();
                    e.Direct = list.Where(c => c.Fog == null).ToList();
                }
                return e;
            }
            // AND group mixing action buttons with other terms: wait for the others first (prompt shows only then)
            var nonAction = list.Where(c => !c.IsAction).ToList();
            if (g > 0 && pass && nonAction.Count > 0 && nonAction.Count < list.Count)
                e.Pre = Join(nonAction);
            return e;
        }
        static Cond Join(List<Cond> terms)
        {
            var c = new Cond();
            for (int k = 0; k < terms.Count; k++)
            {
                c.Setup.AddRange(terms[k].Setup);
                c.Expr = k == 0 ? terms[k].Expr : [.. c.Expr, .. terms[k].Expr, 0x98];
            }
            return c;
        }
        static (List<ESD.CommandCall> Setup, byte[] Expr) Compile(Cond c) => (c.Setup, c.Expr.Length == 0 ? True : [.. c.Expr, 0xA1]);
        // a wait/test state: the condition's pass commands run when it fires
        static ESD.State W(Cond c, long then, params (byte[] If, long Then)[] more)
        {
            var (setup, expr) = Compile(c);
            var st = S(setup, more);
            var cond = new ESD.Condition(then, expr);
            cond.PassCommands.AddRange(c.Pass);
            st.Conditions.Insert(0, cond);
            return st;
        }
        // "in box" as a fresh group condition (own slot) for the crossing detector's states
        Cond InBox(int loc) => Grp(140132, loc, loc, 1);
        // a MAIN wait: plain, two-stage (action buttons after the other terms) or a white-door crossing detector
        void Wait(long at, Cond c, long next)
        {
            if (c.Fog is { } fog)
            {
                // wait state: the player near the door on a branch's button side → that branch's state
                var waitSt = S([]);
                var branchStates = new List<long>();
                foreach (var b in fog)
                {
                    long bs = extraState++;
                    branchStates.Add(bs);
                    var near = InBox(b.From);
                    var nearCond = b.Others is { } o ? Join([near, o]) : near;
                    waitSt.EntryCommands.AddRange(nearCond.Setup);
                    waitSt.Conditions.Add(new ESD.Condition(bs, [.. nearCond.Expr, 0xA1]));
                    // branch state: through to the far side → passed; left both boxes → back to waiting
                    var farIn = InBox(b.To);
                    var nearAgain = InBox(b.From);
                    var passCond = b.Others is { } o2 ? Join([farIn, o2]) : farIn;
                    var st = S([.. passCond.Setup, .. nearAgain.Setup]);
                    st.Conditions.Add(new ESD.Condition(next, [.. passCond.Expr, 0xA1]));
                    // the abort reads the slots negated: only after AbortSettle s in this state (review, session 8: if a
                    // just-registered slot read 0 in its first frame, an unguarded abort would bounce wait ↔ branch every
                    // frame and never see the far box); a white door's traversal takes longer than that anyway
                    // rule G1 (track nestfix): "left both boxes" as two positive registrations (outside each box,
                    // 140132 … 0): a negated read is never stepped, since DS2 runs this state only while a slot holds
                    // rule G2 (fix pass): back to waiting = a re-execute (or no abort: the branch stays armed)
                    if (ReplaySafe(ins, (int)at - 1))
                    {
                    var farOut = Grp(140132, b.To, b.To, 0);
                    var nearOut = Grp(140132, b.From, b.From, 0);
                    st.EntryCommands.AddRange([.. farOut.Setup, .. nearOut.Setup]);
                    var abort = new ESD.Condition(ReExecute(), [.. farOut.Expr, .. nearOut.Expr, 0x98, .. StateTime(AbortSettle), 0x98, 0xA1]);
                    timed.Add(abort);
                    st.Conditions.Add(abort);
                    }
                    else absent.Add("rule G2: no crossing abort (the instructions before the wait can't rerun)");
                    states[bs] = st;
                }
                foreach (var d in c.Direct ?? [])
                {
                    waitSt.EntryCommands.AddRange(d.Setup);
                    var dc = new ESD.Condition(next, [.. d.Expr, 0xA1]);
                    dc.PassCommands.AddRange(d.Pass);
                    waitSt.Conditions.Add(dc);
                }
                states[at] = waitSt;
                return;
            }
            if (c.Pre is { } pre)
            {
                // two stages: the other conditions, then the prompt and all conditions together
                long stage2 = extraState++;
                states[at] = W(pre, stage2);
                var st2 = W(c, next);
                if (c.Leave != null && !ReplaySafe(ins, (int)at - 1)) absent.Add("rule G2: the button stays armed off its area (the instructions before it can't rerun)");
                if (c.Leave is { } lv && ReplaySafe(ins, (int)at - 1)) // rule G2 (fix pass): leaving re-executes, or stays
                {
                    // rule G1 (track nestfix): DS2 steps a state that registered group conditions only in frames where
                    // one of them holds, so "left the area" can't be a negated slot read: it is its own positive
                    // registration, outside the region (140132 … 0), which opens the gate when the player leaves
                    if (lv.OutsideOf != 0)
                    {
                        var outside = Grp(140132, lv.OutsideOf, lv.OutsideOf, 0);
                        st2.EntryCommands.AddRange(outside.Setup);
                        lv = new Cond { Expr = outside.Expr, Pass = lv.Pass };
                    }
                    var back = new ESD.Condition(ReExecute(), [.. lv.Expr, 0xA1]);
                    back.PassCommands.AddRange(lv.Pass);
                    st2.Conditions.Add(back);
                }
                states[stage2] = st2;
                return;
            }
            states[at] = W(c, next);
        }

        // session 11 (burg11), rule CR17 (opt-in CharacterRulesM3): the host's view of "SKIP IF Multiplayer State(n, Host)": the
        // host always skips, so the skipped lines never run and their condition registrations never join a group (m10_01
        // 11015384: the client-only 11015381 test would otherwise gate the host's boss bar)
        var hostSkipped = new HashSet<int>();
        if (ctx.CharacterRulesM3)
            for (int k = 0; k < ins.Count; k++)
                if ((ins[k].Bank, ins[k].ID) is (1003, 5) && V(ins[k]) is { Length: >= 2 } hv && (int)hv[1] == 0)
                    for (int j = k + 1; j <= k + (int)hv[0] && j < ins.Count; j++) hostSkipped.Add(j);
        for (int i = 0; i < ins.Count; i++)
        {
            var x = ins[i];
            var v = V(x);
            long next = St(i + 1);
            ESD.State Cmd(params ESD.CommandCall[] cmds) => S(cmds, (True, next));
            long Skip(double n) => St(i + 1 + (int)n);
            if (hostSkipped.Contains(i)) { states[St(i)] = Cmd(); continue; }
            if (ctx.CharacterRulesM3 && (x.Bank, x.ID) is (1003, 5) && v.Length >= 2 && (int)v[1] == 0) { states[St(i)] = S([], (True, Skip(v[0]))); continue; }
            switch (x.Bank)
            {
                case 0 or 1 or 3 or 4 or 5 or 11:
                {
                    int group = v.Length > 0 ? (int)v[0] : 0;
                    // session 10, rule K4: a key door with an OR alternative (DS1 ObjActParam type2 1, the Master Key rows):
                    // 131602 checks its items with AND, so the event picks the item the player holds (140801 「所持アイテム数
                    // 比較」, vanilla form (slot, item, 1, 3, 1, 1, 0) = "holds it", m10_10 group 2147483554), item 1 first;
                    // neither → item 1's lock ("It's locked"). Re-chosen at every load (the group restarts with the map)
                    if (x.Bank == 5 && x.ID == 2 && ctx.Lock((int)v[1]) is { Item2: not 0 } k4)
                    {
                        if (group != 0 || (owner != 0 && owner != k4.Owner)) { missing.Add($"objact {(int)v[1]}: rule K4 lock inside a condition group / second door"); states[St(i)] = Cmd(); break; }
                        owner = k4.Owner; keyDoorWait = true;
                        long alt = extraState++, lock1 = extraState++, lock2 = extraState++;
                        byte[] Slot0 = [0x82, .. BitConverter.GetBytes(140001), 0x40, 0x85, 0xA6, 0xA1];
                        states[St(i)] = S([C(140801, 0, k4.Item, 1, 3, 1, 1, 0)], (Slot0, lock1), (True, alt));
                        states[alt] = S([C(140801, 0, k4.Item2, 1, 3, 1, 1, 0)], (Slot0, lock2), (True, lock1));
                        timed.UnionWith(states[St(i)].Conditions); timed.UnionWith(states[alt].Conditions); // rule G1: entry-iteration tests
                        states[lock1] = S([C(131602, k4.Region, k4.Item, k4.UsedText, k4.FailText, k4.Flag, 0)], ([.. FlagExpr(k4.Flag, true), 0xA1], next));
                        states[lock2] = S([C(131602, k4.Region, k4.Item2, k4.UsedText, k4.FailText, k4.Flag, 0)], ([.. FlagExpr(k4.Flag, true), 0xA1], next));
                        absent.Add($"rule K4: lock {k4.Flag} takes item {k4.Item} or {k4.Item2} (chosen at load)");
                        foreach (var (gk, gl) in groups) everCompiled[gk] = gl;
                        compiled = groups;
                        groups = new Dictionary<int, List<Cond>>();
                        break;
                    }
                    var cond = Condition(x, v);
                    if (cond == null) { states[St(i)] = Cmd(); break; }
                    if (group != 0)
                    {
                        if (!groups.TryGetValue(group, out var l)) groups[group] = l = [];
                        l.Add(cond);
                        states[St(i)] = Cmd();
                    }
                    else
                    {
                        if (x.Bank == 3 && x.ID == 0 && v[1] != 0 && ParksAtDeparture(ins, i, EventFlag(v[2], v[3])))
                        {
                            // track nests: a departure flag's wait is tested once, then parks until the next load
                            long park = extraState++;
                            var (setup, expr) = Compile(cond);
                            states[St(i)] = S(setup, (expr, next), (True, park));
                            states[park] = S([]);
                            absent.Add($"flag {EventFlag(v[2], v[3])} is set only on leaving the map (wait parks)");
                        }
                        else // (the base line below kept as is for the 3-way merge)
                        Wait(St(i), cond, next);
                        foreach (var (gk, gl) in groups) everCompiled[gk] = gl;
                        compiled = groups;
                        groups = new Dictionary<int, List<Cond>>();
                    }
                    break;
                }
                case 1000:
                    switch (x.ID)
                    {
                        case 3: states[St(i)] = S([], (True, Skip(v[0]))); break;                    // SKIP n
                        case 4: states[St(i)] = S([], (True, Finish(v[0]))); break;                  // END / RESTART
                        case 1 or 7: // SKIP IF condition group state(n, state, group): 1 uncompiled (current), 7 compiled (last MAIN wait's)
                        {
                            var c = GroupExpr((int)v[2], v[1] != 0, x.ID == 7);
                            if (c == null) { states[St(i)] = Cmd(); break; }
                            states[St(i)] = W(c, Skip(v[0]), (True, next));
                            timed.UnionWith(states[St(i)].Conditions); // rule G1: a test resolves in its entry iteration
                            break;
                        }
                        case 2 or 8: // END IF condition group state(end type, state, group): 2 uncompiled, 8 compiled
                        {
                            var c = GroupExpr((int)v[2], v[1] != 0, x.ID == 8);
                            if (c == null) { states[St(i)] = Cmd(); break; }
                            states[St(i)] = W(c, Finish(v[0]), (True, next));
                            timed.UnionWith(states[St(i)].Conditions); // rule G1: a test resolves in its entry iteration
                            break;
                        }
                        case 5 or 6: // SKIP / END IF comparison(n | end type, comparison, left, right): constants after substitution
                        {
                            double l = v[2], r = v[3];
                            bool t = (int)v[1] switch { 0 => l == r, 1 => l != r, 2 => l > r, 3 => l < r, 4 => l >= r, 5 => l <= r, _ => false };
                            states[St(i)] = S([], (True, t ? (x.ID == 5 ? Skip(v[0]) : Finish(v[0])) : next));
                            break;
                        }
                        // session 11 (burg11), rule N1 (opt-in, IContext.NetworkWaitsAreNoOps): WAIT For Network Approval(timeout)
                        // syncs DS1's session before a shared object moves (the lift 11020301); offline and for the host it
                        // passes at once, DS2 scripts have no such wait → no-op
                        case 9 when ctx.NetworkWaitsAreNoOps: absent.Add("1000:9 network approval wait (rule N1: passes at once offline / for the host)"); states[St(i)] = Cmd(); break;
                        default: missing.Add($"1000:{x.ID} {Name(x)}"); states[St(i)] = Cmd(); break;
                    }
                    break;
                case 1001: // WAIT seconds / frames
                {
                    float s = x.ID == 0 ? (float)v[0] : x.ID == 1 ? (float)(v[0] / 30) : -1;
                    if (x.ID == 2 && ctx.CharacterRulesM3 && v.Length >= 2) { s = (float)((v[0] + v[1]) / 2); absent.Add($"rule CR6: random wait {v[0]}..{v[1]} s → {s} s"); } // WAIT Random Time(min, max) → its mean
                    if (s < 0) { missing.Add($"1001:{x.ID} {Name(x)}"); states[St(i)] = Cmd(); break; }
                    states[St(i)] = S([], ([0x43, 0x84, 0xA6, 0x80, .. BitConverter.GetBytes(s), 0x94, 0x40, 0x96, 0xA1], next));
                    break;
                }
                case 1003:
                    switch (x.ID)
                    {
                        case 1 or 2: // SKIP / END IF Event Flag(n | end type, state, type, id)
                        {
                            var f = F(EventFlag(v[2], v[3]));
                            if (f == null) { states[St(i)] = Cmd(); break; }
                            var test = FlagExpr(f.Value, v[1] != 0);
                            // a leading test reads the flag as DS1's event 0 leaves it (see the class summary, fix pass)
                            if (i < leading && FoldedFlagTest(EventFlag(v[2], v[3]), v[1] != 0) is { } fold)
                            {
                                test = fold;
                                folded.Add($"{f.Value}");
                            }
                            states[St(i)] = S([], ([.. test, 0xA1], x.ID == 1 ? Skip(v[0]) : Finish(v[0])), (True, next));
                            break;
                        }
                        case 5 or 6: // SKIP / END IF Multiplayer State(n | end type, state): rule C1 (session 10), see NetExpr
                        {
                            if (NetExpr((int)v[1]) is not { } ne) { missing.Add($"1003:{x.ID} multiplayer state {(int)v[1]} (no rule)"); states[St(i)] = Cmd(); break; }
                            states[St(i)] = S([], ([.. ne, 0xA1], x.ID == 5 ? Skip(v[0]) : Finish(v[0])), (True, next));
                            break;
                        }
                        case 7: // SKIP IF Player In/Out Map(n, inside, area, block): the event runs in its own map (session 9:
                                // compared with the map, as 3:8; Firelink's 11020001 tests "inside m18_01")
                            states[St(i)] = S([], (True, (v[1] != 0) == ((int)v[2] == int.Parse(ctx.Ds1Map[1..3]) && (int)v[3] == int.Parse(ctx.Ds1Map[4..6])) ? Skip(v[0]) : next));
                            break;
                        case 3 or 4: // SKIP / END IF Batch Event Flags(n | end type, op, type, first, last): session 9, BatchExpr
                        {
                            var e = BatchExpr(v[1], v[2], v[3], v[4]);
                            if (e == null) { states[St(i)] = Cmd(); break; }
                            states[St(i)] = S([], ([.. e, 0xA1], x.ID == 3 ? Skip(v[0]) : Finish(v[0])), (True, next));
                            break;
                        }
                        default: missing.Add($"1003:{x.ID} {Name(x)}"); states[St(i)] = Cmd(); break;
                    }
                    break;
                default:
                {
                    // Display Boss Health Bar(state, entity, slot, name) on a present boss: on → 132201 unless a battle runs
                    // (f132201 「ボス戦中判定」), off → 132204 only while it runs (DS2's prelude-fight pattern; after
                    // 132202 the battle is over and DS1's bar-off of the dead boss is a no-op)
                    if (x.Bank == 2003 && x.ID == 11 && ctx.IsBoss((int)v[1]) && ctx.Generator((int)v[1]) != null && ctx.BossBattle((int)v[1], (int)v[3]) is int bb)
                    {
                        long act = extraState++;
                        bool on = v[0] != 0;
                        states[St(i)] = S([], ([0x82, .. BitConverter.GetBytes(132201), .. Push(bb), 0x85, 0xA6, 0x40, on ? (byte)0x95 : (byte)0x96, 0xA1], act), (True, next));
                        states[act] = S([C(on ? 132201 : 132204, bb)], (True, next));
                        break;
                    }
                    // Force Animation Playback of a boss's start-up wake clip (step 2): the AI plays it (logic flag); "wait for
                    // completion" waits DS1's clip length
                    if (x.Bank == 2003 && x.ID == 18 && IsCharacter((int)v[0]) && ctx.Generator((int)v[0]) != null && ctx.WakeClip((int)v[0], (int)v[1]) is double wake)
                    {
                        long after = next;
                        if (resetAfter.TryGetValue(i, out var gens))
                        {
                            // the start-up DS1 cleared before the wake, reset once the wake has played (2004:9 rule)
                            after = extraState++;
                            states[after] = S(gens.Select(g => C(131702, g, 0)), (True, next));
                        }
                        states[St(i)] = v[3] != 0 ? S([], (StateTimeEnd((float)wake), after)) : S([], (True, after));
                        break;
                    }
                    // session 11 (chrai11), rule FA1: Force Animation Playback of a clip the character plays as an action of its own → its
                    // request flag on (the map script's encoder puts the clip's code on the generator's logicEventFlag bits; the AI's
                    // dispatcher plays it and acknowledges); "wait for completion" waits DS1's clip length
                    if (x.Bank == 2003 && x.ID == 18 && ctx.CharacterRulesM3 && IsCharacter((int)v[0]) && ctx.ForcedClip((int)v[0], (int)v[1]) is { } fc)
                    {
                        absent.Add($"rule FA1: chr {(int)v[0]} forced clip {(int)v[1]} = request flag {fc.Flag}{(v[3] != 0 ? $" + wait {fc.Seconds:0.##} s" : "")}");
                        states[St(i)] = v[3] != 0 ? S([C(130101, fc.Flag, 1)], (StateTimeEnd((float)fc.Seconds), next)) : S([C(130101, fc.Flag, 1)], (True, next));
                        break;
                    }
                    // Set Special Animation(s) all -1 on a character this event gave a start-up state: 131702(gen, 0), the
                    // generator's own start-up (GeneratorRegistParam.startUpStateType 0 on every Lordran generator); when DS1
                    // plays the boss's wake clip next (11810310: clear, then 9060), after the wake's wait (fix pass: DS1 clears
                    // the waiting animation so the drop can play; in DS2 the wake ends the dormant start-up, and whether DS2
                    // keeps a start-up override across loads is unknown: reset once the wake has played, the dormant boss is
                    // never touched before its wake and a later spawn starts in start-up 0 either way)
                    if (x.Bank == 2004 && x.ID == 9 && overridden.Contains((int)v[0]) && v.Skip(1).Take(5).All(a => (int)a <= 0) && ctx.Generator((int)v[0]) is int rg)
                    {
                        int wakeAt = Enumerable.Range(i + 1, ins.Count - i - 1).FirstOrDefault(j => ins[j].Bank == 2003 && ins[j].ID == 18 && V(ins[j]) is var wv
                            && (int)wv[0] == (int)v[0] && wv[3] != 0 && ctx.WakeClip((int)wv[0], (int)wv[1]) != null, -1);
                        if (wakeAt >= 0)
                        {
                            (resetAfter.TryGetValue(wakeAt, out var l) ? l : resetAfter[wakeAt] = []).Add(rg);
                            states[St(i)] = Cmd();
                        }
                        else states[St(i)] = Cmd(C(131702, rg, 0));
                        break;
                    }
                    // Award Items of a DS1 lot with per-row "obtained" flags: each row only while its flag is off, then the flag
                    if (x.Bank == 2003 && x.ID is 36 or 4 && ctx.ItemLotRows((int)v[0]) is { Count: > 0 } rows)
                    {
                        long after = next;
                        for (int r = rows.Count - 1; r >= 0; r--)
                        {
                            var (lot, flag) = rows[r];
                            long give = extraState++;
                            if (flag is int fl)
                            {
                                states[give] = S([C(131402, lot, 1), C(130101, fl, 1)], (True, after));
                                long test = extraState++;
                                states[test] = S([], ([.. FlagExpr(fl, true), 0xA1], after), (True, give));
                                after = test;
                            }
                            else
                            {
                                states[give] = S([C(131402, lot, 1)], (True, after));
                                after = give;
                            }
                        }
                        states[St(i)] = S([], (True, after));
                        break;
                    }
                    var cmds = Command(x, v, missing, absent, fogEvent, keyDoorWait);
                    if (x.Bank == 2004 && x.ID == 9 && cmds is { } c9 && c9.Any(c => c.CommandID == 131702)) overridden.Add((int)v[0]);
                    // Force Animation Playback with ShouldWaitForCompletion on an object: wait for the prefab's end state
                    // (f131653), at most 10 s so a wrong state table can't hang the event
                    if (cmds is { Count: > 0 } && x.Bank == 2003 && x.ID == 18 && v[3] != 0 && ctx.Object((int)v[0]) is int obj
                        && ctx.ObjectAnimEndState((int)v[0], (int)v[1]) is int end)
                    {
                        long waitSt = extraState++;
                        // rule WE4b (session 10): DS1's damaging object breaks parts at DS1's contact times while the
                        // animation plays: one timed state per hit (f3() >= Δt → 131613(target, target, 0)), then the
                        // end-state wait. DS1 can't hit a disabled part: break only while the target is active (140209
                        // 「OBJアクティブ判定」; vanilla m10_02 registers (slot, inst, 1) and (slot, inst, 0) as complementary
                        // waits, rule G1: both positive, so the state steps every frame)
                        var hits = ctx.ObjectHits((int)v[0], (int)v[1]).OrderBy(h => h.Seconds).ToList();
                        long first = waitSt;
                        if (hits.Count > 0)
                        {
                            first = extraState++;
                            long cur = first; float prev = 0;
                            for (int h = 0; h < hits.Count; h++)
                            {
                                long brk = extraState++;
                                long nextHit = h + 1 < hits.Count ? extraState++ : waitSt;
                                byte[] at = StateTime(Math.Max(Settle, hits[h].Seconds - prev));
                                byte[] Slot(int k) => [0x82, .. BitConverter.GetBytes(140001), (byte)(0x40 + k), 0x85, 0xA6];
                                var hitState = S([C(140209, 0, hits[h].Instance, 1), C(140209, 1, hits[h].Instance, 0)],
                                    ([.. Slot(0), .. at, 0x98, 0xA1], brk), ([.. Slot(1), .. at, 0x98, 0xA1], nextHit));
                                timed.UnionWith(hitState.Conditions); // own state-time guard (the settle pass leaves them)
                                states[cur] = hitState;
                                states[brk] = S([C(131613, hits[h].Instance, hits[h].Instance, 0)], (True, nextHit));
                                prev = hits[h].Seconds; cur = nextHit;
                            }
                        }
                        states[St(i)] = S(cmds, (True, first));
                        states[waitSt] = S([], ([0x82, .. BitConverter.GetBytes(131653), .. Push(obj), .. Push(end), 0x40, 0x87, 0xA6,
                            0x43, 0x84, 0xA6, 0x80, .. BitConverter.GetBytes(10f), 0x94, 0x99, 0xA1], next));
                        break;
                    }
                    // a cross-map warp waits until the warp is done (f132101() != 0), as vanilla's 【Lib】ポリ劇後マップ間ワープ
                    // and MapLinks' crow group do. Session 8: the translated nest event (11810150) moved on at once,
                    // looped back and re-armed during the load: the arrival demo 190226 never started and the screen
                    // stayed black (run s8-8)
                    if (cmds is { Count: > 0 } && cmds.Any(c => c.CommandID == 132125))
                    {
                        states[St(i)] = S(cmds, (Convert.FromHexString("820504020084A64096A1"), next));
                        break;
                    }
                    states[St(i)] = cmds == null ? Cmd() : Cmd([.. cmds]);
                    break;
                }
            }
        }
        if (ins.Count == 0) states[1] = S([], (True, EndState()));
        // fix pass, rule WE4b-L: DS1's load-time end branch (a leading SKIP n IF own flag OFF whose skipped block ends in END)
        // stands for "this event has happened"; the live branch's WE4b hits are among its persistent effects (DS1 records
        // the target's destruction only through the target's own event). Re-apply them there: after ReapplySettle, every
        // hit target still active is broken (the same 140209 pair + 131613 as the live hits). Idempotent: a target whose own
        // event already ran hides it at load (inactive → skipped). Repairs saves where the flag is on but the target stands
        // (11810211 set by main's stand-in 37340 while Oscar's wall did not exist: the wall would never break, 11810200 waits)
        for (int i = 0; i < ins.Count; i++)
        {
            if ((ins[i].Bank, ins[i].ID) != (1003, 1)) continue;
            var v = V(ins[i]);
            if (v[1] != 0 || EventFlag(v[2], v[3]) != (int)ev.ID + slot) continue;
            int endAt = i + (int)v[0];
            if (endAt <= i || endAt >= ins.Count || (ins[endAt].Bank, ins[endAt].ID) != (1000, 4) || (int)V(ins[endAt])[0] != 0) break;
            var targets = ins.Skip(endAt + 1).Where(y => (y.Bank, y.ID) == (2003, 18)).Select(V)
                .SelectMany(w => ctx.ObjectHits((int)w[0], (int)w[1])).Select(h => h.Instance).Distinct().ToList();
            if (targets.Count == 0 || !states.TryGetValue(St(endAt), out var endSt)) break;
            long moved = extraState++, cur = St(endAt);
            states[moved] = endSt;
            byte[] Slot(int k) => [0x82, .. BitConverter.GetBytes(140001), (byte)(0x40 + k), 0x85, 0xA6];
            for (int h = 0; h < targets.Count; h++)
            {
                long brk = extraState++, nxt = h + 1 < targets.Count ? extraState++ : moved;
                var check = S([C(140209, 0, targets[h], 1), C(140209, 1, targets[h], 0)],
                    ([.. Slot(0), .. StateTime(ReapplySettle), 0x98, 0xA1], brk), ([.. Slot(1), .. StateTime(ReapplySettle), 0x98, 0xA1], nxt));
                timed.UnionWith(check.Conditions);
                states[cur] = check;
                states[brk] = S([C(131613, targets[h], targets[h], 0)], (True, nxt));
                cur = nxt;
            }
            absent.Add($"rule WE4b-L: the load-time end branch re-applies the live hits on {string.Join(", ", targets)} while still active");
            break;
        }
        // session 10, rule G11 (see DepartureFold): the load-time branch for the departure flag first repeats the
        // continuation's persistent writes while the event's own flag is still off
        bool folding = false;
        if (folds.TryGetValue((ev.ID, slot), out var dfold) && ctx.Flag((int)ev.ID + slot) is int ownFlag && states.TryGetValue(St(dfold.SkipAt), out var skipSt))
        {
            long tgt = St(dfold.Target), guard = extraState++, apply = extraState++;
            var cmds = new List<ESD.CommandCall>();
            foreach (var y in dfold.Writes) cmds.AddRange(Command(y, V(y), missing, absent, fogEvent, keyDoorWait) ?? []);
            states[guard] = S([], ([.. FlagExpr(ownFlag, false), 0xA1], apply), (True, tgt));
            states[apply] = S(cmds, (True, tgt));
            foreach (var c in skipSt.Conditions) if (c.TargetState == tgt) c.TargetState = guard;
            absent.Add($"rule G11: the continuation after the departure wait on flag {dfold.DepartureFlag} ({string.Join(", ", dfold.Ds2Flags)}) is repeated at the first load after a departure");
            folding = true;
        }
        EndState();
        // state 0 → (helpers' prompts off) → 1
        if (helpers.Count > 0)
        {
            states[0] = S([], (True, Init));
            states[Init] = S(helpers.OrderBy(h => h).Select(h => C(131638, h, 1)), (True, 1));
        }
        else states[0] = S([], (True, 1));
        // session 10 (asy-items), rules C1/C2: an event that reads the DS1 class (4:11) starts once the class is known (any
        // of the ten origin flags set): in DS1 the class exists before any event runs (character creation), so no DS1
        // event ever sees "no class"; here the origin prompt sets it on DS1's opening (OriginPrompt). Common 290's
        // "neither Knight nor Cleric" branch would otherwise pass before the prompt was answered. Fix pass (rule C2b):
        // every map hosting such an event also hosts the prompt (OriginPrompt.Add, keyed on "opening ON and no class"),
        // so the wait can't park for ever on a character that left the Asylum without a class
        if (readsClass && OriginPrompt.KnownExpr() is { } known && states[0].Conditions[0].TargetState is long firstState)
        {
            const long OriginWait = 8994;
            states[OriginWait] = S([], ([.. known, 0xA1], firstState));
            states[0] = S([], (True, OriginWait));
            absent.Add("rule C1: starts once the DS1 class is known (origin prompt)");
        }
        // session 9 (track firelink): an event another event restarts (2003:8 Set Event State, Restart: Firelink's
        // 11020020 "Stand up" restarts 11020021, cancelling its 20 s wait) leaves any waiting state for its start when its
        // restart-request flag is set (the flag cleared on the way, the helpers' prompts off as at load); ended events
        // (terminal states) stay ended (P: whether DS1 revives an ended event is unknown).
        // Fix pass: nothing restarts an event past its cross-map warp. Once DS1's 2002:4 has started, the cutscene plays
        // and the map's events unload; the instructions DS1 runs after it were hoisted in front of the warp, so every
        // state from the warp's DS1 position on (and what follows it) keeps no restart test: the warp state only waits
        // for f132101 (the session 8 warp-wait fix: leaving that wait re-armed the event during the load, run s8-8/s8-9)
        if (restartTargets.Contains((ev.ID, slot)) && ctx.Flag((int)ev.ID + slot) is int && FlagRules.Restart(ctx.Ds1Map, (int)ev.ID + slot) is int rf)
        {
            const long Restart = 8998;
            // session 9 (track npcflags): the request flag is FlagRules F3 (the map's temporary list; was own + 800000,
            // an id DS2 has no storage for)
            var past = new HashSet<long>();
            int w0 = substituted.FindIndex(IsCrossMapWarp);
            var todo = new Stack<long>(w0 < 0 ? [] : Enumerable.Range(w0, ins.Count - w0).Select(i => 1L + i));
            while (todo.Count > 0)
                if (todo.Pop() is long s && s > w0 && s < End && s != Init && states.TryGetValue(s, out var st0) && past.Add(s))
                    foreach (var c in st0.Conditions) if (c.TargetState is long t) todo.Push(t);
            var waiting = states.Where(s => s.Key != 0 && s.Key < End && !past.Contains(s.Key) && s.Value.Conditions.Count > 0).Select(s => s.Value).ToList();
            states[Restart] = S([C(130101, rf, 0), C(130002)]); // rule G2 (track nestfix): a restart is a re-execute
            foreach (var st in waiting) st.Conditions.Insert(0, new ESD.Condition(Restart, [.. FlagExpr(rf, true), 0xA1]));
        }
        // session 9, rule T1 (see TemporaryFlag): event 0 clears the map's temporary flags first; every other group that
        // reads or writes one waits Settle s at its start (state 0 → load state → its first state)
        if (TemporaryFlags.Count > 0 && states[0].Conditions[0].TargetState is long start)
        {
            if (ev.ID == 0 && slot == 0)
            {
                const long LoadReset = 8997;
                // session 9 (track npcflags): one 130101(flag, 0) per flag. 130131(first, last, v) is not a flag range in
                // DS2: it sets the flags of the EventFlagListParam ROWS first..last (FUN_140474af0), none here, so the old
                // reset did nothing. The entry-command cap below splits a long list.
                states[LoadReset] = S(TemporaryFlags.Select(f => C(130101, f, 0)), (True, start));
                states[0] = S([], (True, LoadReset));
                TemporaryFlagsReset = missing.Count == 0;
            }
            else if (states.Values.Any(st => TouchesFlags(st, TemporaryFlags)))
            {
                const long LoadSettle = 8996;
                states[LoadSettle] = S([], ([.. StateTime(Settle), 0xA1], start));
                states[0] = S([], (True, LoadSettle));
            }
        }
        // session 10 (asy-items), load-init (rule C3; its own state after rule T1's reset 8997, outside any state another
        // rule may guard: COVERAGE C5): event 0 disables the items of start-disabled DS1 treasures (MSB StartDisabled: the
        // treasure only, not the object: DS1's 11810350 hides the doll's object itself with 2005:3 and on the return trip
        // enables only its treasure with 2005:4) at every load; every other group that switches one of them starts Settle s
        // later (as rule T1's readers), so DS1's own enable comes after it. (DS1 messages need no load-init: rule M2's cat
        // 12 rows are rebuilt visible at every load.)
        var disabled = ctx.TreasuresDisabledAtLoad.ToHashSet();
        if (ev.ID == 0 && slot == 0 && disabled.Count > 0)
        {
            const long LoadInit = 8993;
            long from = states.ContainsKey(8997) ? 8997 : 0;
            if (states[from].Conditions.Count == 1 && states[from].Conditions[0].TargetState is long after)
            {
                states[LoadInit] = S(disabled.Order().Select(t => C(131411, t, 0)), (True, after));
                states[from].Conditions[0].TargetState = LoadInit;
            }
            else missing.Add("load-init: event 0 has no single start transition");
        }
        else if (disabled.Count > 0 && !states.ContainsKey(8996) && states[0].Conditions[0].TargetState is long start2
            && states.Values.Any(st => st.EntryCommands.Concat(st.Conditions.SelectMany(k => k.PassCommands)).Any(c => c.CommandID == 131411
                && c.Arguments.Count > 0 && Const(c.Arguments[0]) is int a0 && disabled.Contains(a0))))
        {
            const long LoadSettle = 8996;
            states[LoadSettle] = S([], ([.. StateTime(Settle), 0xA1], start2));
            states[0] = S([], (True, LoadSettle));
        }
        // session 10, rule G11: a group that reads or writes a flag another group's fold writes at load starts after
        // FoldSettle s (the fold runs in its group's first frame, or after T1's settle), so it sees the folded value
        if (!folding && FoldFlags.Count > 0 && states.Values.Any(st => TouchesFlags(st, FoldFlags)))
        {
            const long FoldWait = 8989; // merge11: 8994 is asy-items' OriginWait
            // after event 0's T1 reset (8997), never before it (the reset must stay within T1's settle)
            var from = states.TryGetValue(8997, out var reset) ? reset : states[0];
            if (from.Conditions[0].TargetState is long next0)
            {
                states[FoldWait] = S([], ([.. StateTime(FoldSettle), 0xA1], next0));
                from.Conditions[0].TargetState = FoldWait;
            }
        }
        // session 9 (fix pass): a command state carries at most MaxEntryCommands entry commands (vanilla's maximum, 64 in
        // event_m50_37); a longer list (Firelink's event 50: 2003:22 over 71 lot flags) continues in follow-up states
        foreach (var (sid, st) in states.ToList())
        {
            var cur = st;
            while (cur.EntryCommands.Count > MaxEntryCommands && cur.Conditions.Count == 1 && cur.Conditions[0].Evaluator.SequenceEqual(True)
                && cur.Conditions[0].PassCommands.Count == 0)
            {
                long more = extraState++;
                var rest = cur.EntryCommands.Skip(MaxEntryCommands).ToList();
                cur.EntryCommands.RemoveRange(MaxEntryCommands, rest.Count);
                states[more] = S(rest, (True, cur.Conditions[0].TargetState ?? End));
                cur.Conditions[0].TargetState = more;
                cur = states[more];
            }
        }
        // rule G1 (track nestfix), c: in a state that registers group conditions and waits (a test ends in `if 1` and
        // resolves in its entry iteration), a transition that could pass while no slot holds never runs (a flag term
        // outside every slot read: 11815392's "|| flag 11815393", Firelink's NPC waits "flags && alive"), so its flag
        // terms become group conditions too: 140301(slot, flag, 1 on | 0 off) on free slots (vanilla's flag condition,
        // 1622 uses; m10_04 sets 104020099 off and then waits on 140301(0, 104020099, 0)). What still can't pass (a
        // state-time fallback, a lone negated character condition) stays as it is: esd-check warns (EventSim.Lint)
        foreach (var st in states.Values)
        {
            var regs = st.EntryCommands.Where(c => c.CommandID is >= 140000 and < 150000).ToList();
            if (regs.Count == 0 || st.Conditions.Count == 0 || st.Conditions[^1].Evaluator.SequenceEqual(True)) continue;
            var free = new Queue<int>(Enumerable.Range(0, 8).Except(regs.Select(c => (int)EventSim.Evaluate(c.Arguments[0], (_, _) => 0))));
            foreach (var cond in st.Conditions)
                if (EventSim.Gated(cond.Evaluator) is { Gated: true } && GateFlags(cond.Evaluator, free, st.EntryCommands) is { } gated)
                    cond.Evaluator = gated;
            // session 11 (chrai11), rule TC1-G: a part break f130310 (a direct function, no group condition) in a wait beside slot reads
            // (11015397's "part HP 0 || dead" = f130310 || 140522) could pass while no slot holds: the state opens its gate with a
            // complementary pair on the part's generator's AI board (140171 slot 7 == 0 / != 0, rule FA1's encoder pattern: one of
            // them always holds, so the state steps every frame and still reads 140522 positively)
            int pg = 0;
            foreach (var cond in st.Conditions.Where(c => EventSim.Gated(c.Evaluator) is { Gated: true }))
                EventSim.Evaluate(cond.Evaluator, (f, a) => { if (f == 130310 && a.Length > 0) pg = (int)a[0]; return 0; });
            if (pg > 0 && free.Count >= 2)
            {
                st.EntryCommands.Add(C(140171, free.Dequeue(), pg, Ai.Boss.EventMessages.Slot, 0, 0));
                st.EntryCommands.Add(C(140171, free.Dequeue(), pg, Ai.Boss.EventMessages.Slot, 0, 1));
                absent.Add($"rule TC1-G: the part break of generator {pg} waits beside group conditions: gate held open (140171 pair on board {Ai.Boss.EventMessages.Slot})");
            }
        }
        // settle time: in a state that registers group conditions, the conditions that read slots and the unconditional
        // fallback (SKIP/END tests) wait Settle s of state time, so no slot is read before the engine has evaluated it
        // (vanilla gives no evidence that a slot holds its value in the frame it is registered; see the summary)
        foreach (var st in states.Values.Where(s => s.EntryCommands.Any(c => c.CommandID is >= 140000 and < 150000)))
            foreach (var cond in st.Conditions.Where(c => !timed.Contains(c)))
                if (cond.Evaluator.SequenceEqual(True)) cond.Evaluator = [.. StateTime(Settle), 0xA1];
                else if (ReadsGroup(cond.Evaluator)) cond.Evaluator = [.. cond.Evaluator[..^1], .. StateTime(Settle), 0x98, 0xA1];
        // every state registers each group-condition slot once
        foreach (var (sid, st) in states)
        {
            var slots = st.EntryCommands.Where(c => c.CommandID is >= 140000 and < 150000).Select(c => c.Arguments[0]).Select(Convert.ToHexString).ToList();
            if (slots.Count != slots.Distinct().Count()) missing.Add($"state {sid}: a condition slot registered twice");
        }
        return new Result(ev.ID, slot, gid, missing.Count == 0 ? states : null, missing, owner, absent) { Folded = folded };
    }

    List<ESD.CommandCall>? Command(EMEVD.Instruction x, double[] v, List<string> missing, List<string> absent, bool fogEvent, bool keyDoorWait)
    {
        int I(int k) => (int)v[k];
        // a command on a character: its generator, or a no-op while it is absent
        int? Chr(int e) { var g = ctx.Generator(e); if (g == null) absent.Add($"chr {e}"); return g; }
        switch (x.Bank, x.ID)
        {
            case (2003, 2): // Set Event Flag(flag, state)
            {
                var f = ctx.Flag(I(0));
                if (f == null) { missing.Add($"flag {I(0)}"); return null; }
                return [C(130101, f.Value, I(1) == 1 ? 1 : 0)];
            }
            case (2003, 22): // Batch Set Event Flags(start, end, state)
            {
                var l = new List<ESD.CommandCall>();
                for (int f = I(0); f <= I(1); f++)
                {
                    var m = ctx.Flag(f);
                    if (m == null) { missing.Add($"flag {f}"); return null; }
                    l.Add(C(130101, m.Value, I(2) == 1 ? 1 : 0));
                }
                return l;
            }
            case (2005, 3): // (De)activate Object(entity, state)
            {
                if (ctx.Object(I(0)) is int o) return [C(130702, o, I(1) != 0 ? 1 : 0)];
                if (ctx.ObjectAbsent(I(0))) { absent.Add($"object {I(0)}"); return []; }
                missing.Add($"object {I(0)}");
                return null;
            }
            case (2003, 18) or (2005, 7): // Force Animation Playback(entity, anim, …) / Reproduce Object Animation(entity, anim)
            {
                int e = I(0), anim = I(1);
                if (e == Player)
                {
                    // DS1's event animations of the player (the walk through the fog 7410: the white door plays its own;
                    // the nest's curl-up 7815/7817): DS2's player has DS2's animations, so they are dropped
                    absent.Add($"player anim {anim}");
                    return [];
                }
                if (ctx.Object(e) is int o)
                {
                    if (x.ID == 7)
                    {
                        // rule C11 (session 10): DS1 "Reproduce Object Animation" = the animation's end pose at once.
                        // WE5: the placement already shows it (a part DS1 only reproduces, not a prefab) → nothing to do;
                        if (ctx.ObjectPoseBaked(e, anim)) { absent.Add($"object {e} anim {anim}: end pose baked (WE5)"); return []; }
                        // a prefab whose template accepts a direct request of its rest state → that state (its entry
                        // holds the animation's end pose); else the fallback below: the play state (replays to the end)
                        if (ctx.ObjectRestState(e, anim) is int rest) return [C(131636, o, rest)];
                        if (ctx.ObjectAnimState(e, anim) is int play) absent.Add($"object {e} anim {anim}: C11 fallback, state {play} replays the animation (the template takes no direct request of its rest state)");
                    }
                    // rule LF1 (session 11): a rest-pose clip → its rest state (no movement)
                    if (x.ID == 18 && ctx.ObjectAnimIsRest(e, anim) && ctx.ObjectRestState(e, anim) is int restPose) return [C(131636, o, restPose)];
                    var st = ctx.ObjectAnimState(e, anim);
                    // session 11 (burg11), rule CR13: an object animation no prefab state plays (chest lids: DS2's treasure box opens
                    // itself; lever clips outside the template table) → no command
                    if (st == null && ctx.CharacterRulesM3) { absent.Add($"rule CR13: object {e} anim {anim} (no prefab state plays it)"); return []; }
                    if (st == null) { missing.Add($"object {e} anim {anim}"); return null; }
                    return [C(131636, o, st.Value)];
                }
                if (ctx.ObjectAbsent(e)) { absent.Add($"object {e}"); return []; }
                if (x.Bank == 2003 && IsCharacter(e)) { if (Chr(e) == null) return []; if (ctx.WakeClip(e, anim) != null) return [];
                    if (ctx.CharacterRulesM3) { absent.Add($"rule CR8: chr {e} forced animation {anim} (no DS2 command; its AI acts)"); return []; }
                    missing.Add($"chr {e} anim {anim}"); return null; }
                missing.Add($"animated entity {e} (not an object)");
                return null;
            }
            case (2006, 1) or (2006, 2): // Delete / Spawn Map SFX(entity, …): fog walls are white doors
            {
                var o = ctx.SfxObject(I(0));
                if (o == null && ctx.StandInsAreNoOps) { absent.Add($"sfx {I(0)} (no DS2 counterpart yet, ST1)"); return []; }
                if (o == null) { missing.Add($"sfx {I(0)}"); return null; }
                return [C(130702, o.Value, x.ID == 2 ? 1 : 0)];
            }
            case (2005, 6) or (2005, 14): // Set ObjAct State(entity, param, state) / (De)activate ObjAct (Assign Index)(entity, param, index, state)
            {
                var h = ctx.ObjActHelper(I(0));
                if (h == null) return []; // DS2 doors carry their own action
                return [C(131638, h.Value, I(x.ID == 6 ? 2 : 3) != 0 ? 0 : 1)];
            }
            case (2007, 1): // Display Generic Dialog(msg, type, options, entity, distance)
            {
                if (I(0) <= 0) return []; // no message (11810100's inner door: X8 = 0, skipped by DS1's own comparison)
                var m = ctx.Message(I(0));
                if (m == null) { missing.Add($"message {I(0)}"); return null; }
                if (m == 1005) // "Used %s": the DS2 door's lock shows it; elsewhere DS2 would print the placeholder
                {
                    if (keyDoorWait) return [];
                    // session 10, rule K7: outside a 131602 door (an ObjAct prefab's event, 11810110 "Used Big Pilgrim's Key")
                    // DS2's own item form fills %s: vanilla 130443(1005, 0, 0, 190, 2, item, 0) (arg 4 = 2: item param)
                    if (ctx.MessageItem(I(0)) is int item) return [C(130443, 1005, 0, 0, 190, 2, item, 0)];
                    missing.Add($"message {I(0)} (item placeholder outside a key door)");
                    return null;
                }
                return [C(130443, m.Value, 3, 15, 220, 2, 0, 0)];
            }
            case (2003, 26): // Set Visibility of Message(entity, state): DS1 blood messages
            {                // session 10 (asy-items), rule M2: 132221(location, 0|1) 「イベント血文字有効化」 (argument layout U)
                if (ctx.MessageLocation(I(0)) is int loc) return [C(132221, loc, I(1) != 0 ? 1 : 0)];
                if (ctx.MessageAbsent(I(0))) { absent.Add($"message {I(0)}"); return []; }
                missing.Add($"blood message {I(0)}");
                return null;
            }
            // ---- characters
            case (2004, 5): // Change Character Enable State(entity, state) → pause/unpause
                // session 11 (ret11), rule W1: a character whose generator conditions carry DS1's world state → no command
                if (ctx.WorldOwned(I(0))) { absent.Add($"rule W1: chr {I(0)} enable state = its generator's start/stop conditions"); return []; }
                return Chr(I(0)) is int g5 ? [C(131731, g5, I(1) != 0 ? 0 : 1)] : [];
            case (2004, 4): // Force Character Death(entity, souls) → delete, counted as dead
            {
                // session 10 (e3), rule N1: 131721(gen, 1) (handler 0x140466c69 → FUN_140419460(chr, 1, 1) → FUN_14040fdb0(record,
                // 1, 1, 1): dead bits record+0x76/+0x7a, HP record+0x2c = 0 (what 140111/140121 read), the character removed;
                // param_4 = 1 skips the respawnDisableFlag write, so DS1's own death event sets the dead flag (rule N3); a
                // guest's call does nothing (FUN_1405135f0). DS1's ShouldReceiveSouls → 132211(souls) 「ソウル値加算」 with the
                // character's DS2 souls (vanilla 132211(amount): 20 uses)
                if (Chr(I(0)) is not int g4) return [];
                if (I(1) != 0 && ctx.CharacterSouls(I(0)) is int souls and > 0) return [C(131721, g4, 1), C(132211, souls)];
                return [C(131721, g4, 1)];
            }
            case (2004, 29): // Set Character Backread State(entity, removed): out of processing during a cutscene → paused (P)
                return Chr(I(0)) is int g29 ? [C(131731, g29, I(1) != 0 ? 1 : 0)] : [];
            case (2004, 41): // Issue Short Warp Request(entity, type, destination, dummy)
            {
                int e = I(0), dest = I(2);
                if (e == Player && I(1) == 0 && ctx.PlayerPoseIsCosmetic) { absent.Add($"player short warp onto object {dest} (rule K6b)"); return []; }
                if (I(1) != 1) { missing.Add($"2004:41 warp type {I(1)}"); return null; }
                if (e == Player && fogEvent) return []; // onto the fog button's spot: the white door places the player
                if (e != Player && ctx.Generator(e) == null) { absent.Add($"chr {e}"); return []; }
                var p = ctx.Point(dest);
                if (p == null) { missing.Add($"point {dest}"); return null; }
                return e == Player ? [C(132121, p.Value)] : [C(131704, ctx.Generator(e)!.Value, p.Value)];
            }
            case (2004, 1): // Set Character AI State: a boss's logic flag (step 2; its generator names it as logicEventFlag1)
            {
                if (Chr(I(0)) == null) return [];
                // session 11 (chrai11), rule FA1: a character DS1's events force clips on keeps running (its dispatcher plays them): AI
                // off = its AI-off signal (the dispatcher holds still), not DS2's pause (which would stop the forced clips too)
                if (ctx.CharacterRulesM3 && ctx.AiOffFlag(I(0)) is int af) { absent.Add($"rule FA1: chr {I(0)} AI {(I(1) != 0 ? "on" : "off")} = AI-off signal {af}"); return [C(130101, af, I(1) != 0 ? 0 : 1)]; }
                if (ctx.IsBoss(I(0)) && ctx.LogicFlag(I(0)) is int lf) return [C(130101, lf, I(1) != 0 ? 1 : 0)];
                // session 11 (burg11), rule CR2: a placed regular's AI off/on = the enemy paused / resumed (131731(generator, 1|0), vanilla m10_14/m10_34)
                if (ctx.CharacterRulesM3 && ctx.Generator(I(0)) is int g1) return [C(131731, g1, I(1) != 0 ? 0 : 1)];
                missing.Add($"2004:1 AI state of chr {I(0)}");
                return null;
            }
            case (2004, 9): // Set Special Animation(s)(entity, waiting, damage, cancel, death, return)
            {
                if (I(0) == Player) { absent.Add("player special animations"); return []; } // DS1 player animations are dropped
                if (Chr(I(0)) is not int g9) return [];
                if (I(1) > 0 && I(2) <= 0 && I(3) <= 0 && I(4) <= 0 && I(5) <= 0 && ctx.StartUpState(I(0), I(1)) is int su) return [C(131702, g9, su)];
                // session 11 (dchr11), rule WK2: no waiting animation (cleared, or only a cancel clip) on a large regular that starts posed
                // (its MSB InitAnimID → start-up type) = DS1 releases it: its wake flag on (the upper logic's start-up wake, logicEventFlag1)
                if (I(1) <= 0 && ctx.WakeFlag(I(0)) is int wf9) { absent.Add($"rule WK2: special animations {string.Join("/", v.Skip(1).Take(5))} on {I(0)} = its release: wake flag {wf9}"); return [C(130101, wf9, 1)]; }
                if (v.Skip(1).Take(5).All(a => (int)a <= 0)) return []; // cleared without a start-up set by this event (the event loop resets one it set)
                // session 11 (burg11), rule CR11: special animations no start-up slot plays (DS1 waiting / damage / cancel clips of a
                // placed character: the perched Hellkite 7006, the Hollows' 9060 cancel, the sitting NPCs 9000) → no command
                if (ctx.CharacterRulesM3) { absent.Add($"rule CR11: special animations {string.Join("/", v.Skip(1).Take(5))} on {I(0)} (no start-up slot plays them)"); return []; }
                missing.Add($"2004:9 special animations {string.Join("/", v.Skip(1).Take(5))} on {I(0)}");
                return null;
            }
            case (2004, 13): // Set Character Home(entity, area) → the generator's start position (no warp)
            {
                if (Chr(I(0)) is not int g13) return [];
                var p13 = ctx.Point(I(1));
                if (p13 == null) { missing.Add($"point {I(1)} (character home)"); return null; }
                return [C(131703, g13, p13.Value)];
            }
            case (2004, 21): // Clear SpEffect(entity, id)
                if (I(0) != Player && Chr(I(0)) == null) return [];
                if (I(0) != Player && ctx.FallControl(I(1))) return []; // DS2's fall-damage immunity is once-only (130308)
                if (ctx.CharacterRulesM3 && I(0) == Player) { absent.Add($"rule CR19: player SpEffect {I(1)} cleared (no DS2 SpEffect id)"); return []; }
                missing.Add($"2004:21 clear SpEffect {I(1)} on {I(0)}");
                return null;
            case (2004, 14): // Rotate Character(entity, toward)
                if (I(0) == Player && fogEvent) return []; // facing through the fog: the white door turns the player
                if (I(0) == Player && ctx.PlayerPoseIsCosmetic) { absent.Add("player turn (rule K6b)"); return []; }
                if (I(0) != Player && Chr(I(0)) == null) return [];
                missing.Add($"2004:14 rotate {I(0)}");
                return null;
            case (2004, 8): // Set SpEffect(entity, id)
            {
                if (I(0) == Player && ctx.SpEffectGatesObjAct(I(1))) return []; // the door's region (131602) does the gating
                if (I(0) != Player && Chr(I(0)) is not int g8) return [];
                if (I(0) != Player && ctx.FallControl(I(1))) return [C(130308, ctx.Generator(I(0))!.Value)]; // falling control → no fall damage once
                // session 10 (e3), rule SP1 (CharacterEvents.HpPercent): an instant HP-rate change and nothing else → the HP
                // percentage it leaves, set directly: 130317(gen, pct) 「ジェネレータ指定でHP割合を設定」 (handler 0x14046275a:
                // int or float argument, HP = max(1, trunc(maxHP × pct × 0.01)) into record+0x2c, V-code; vanilla m10_14
                // 【実行】ボスのＨＰ設定 130317(803, p0) with 95…50). DS2 has no SpEffectParam table: its effects are
                // SpEffect*.emevd events (130333(gen, event id, 19, 4), ids up to 170100000), so DS1's effect ids are not
                // DS2 ids and the B2b-id SFX cap (< 30000) does not apply to them
                float? pct = CharacterEvents.HpPercent(Program.DefaultDsrRoot, I(1), out string why8);
                if (I(0) != Player && pct is float p8)
                {
                    absent.Add($"rule SP1: {why8}");
                    return [CF(130317, ctx.Generator(I(0))!.Value, p8)];
                }
                // session 11 (burg11, round 6), rule CR19: a player SpEffect DS2 has no id for (the Sunlight altar's prayer 4170) → no command
                if (ctx.CharacterRulesM3) { absent.Add($"rule CR19: {(I(0) == Player ? "player" : $"chr {I(0)}")} SpEffect {I(1)} ({why8}; no DS2 SpEffect id)"); return []; }
                missing.Add($"2004:8 SpEffect {I(1)} on {I(0)} ({why8})");
                return null;
            }
            case (2004, 30): // Set Character HP Bar Display(entity, state)
            {
                // session 10 (e3), rule HB: DS2 has no event command for a character's own HP bar (EDD: 456 commands, the
                // only gauge command is the boss battle's 132203); DS2 shows it when the character is hit. DS1's uses
                // (npc-events census, 30): a transient off/on pair around a scripted HP change (Oscar 11815010: DS2's
                // 130317 writes HP without a hit, so no bar shows), and bosses' bars off (DS2 bosses carry the battle gauge)
                if (I(0) == Player) { missing.Add("2004:30 HP bar of the player"); return null; }
                if (Chr(I(0)) == null) return [];
                absent.Add(I(1) != 0 ? $"rule HB: chr {I(0)} HP bar on (no command)"
                    : ctx.IsBoss(I(0)) ? $"rule HB: boss {I(0)} overhead bar off (no command: DS2 bosses show the battle gauge)"
                    : $"rule HB: chr {I(0)} HP bar off (no command: DS2 shows it when hit)");
                return [];
            }
            case (2004, 37): // Force Character Treasure(entity)
                // session 10 (e3), rule N4: DS1 re-offers a dead character's loot at load; DS2 hands an NPC's drops out at
                // its death (NpcPorter: the rows a talk gift shares by the talk machine's death branch, each once by its own
                // flag; the rest by EnemyParam's lot), so there is nothing to re-offer (an unclaimed EnemyParam drop is not
                // restored at the next load: U whether DS2 keeps it)
                if (Chr(I(0)) != null) absent.Add($"rule N4: chr {I(0)}'s drops were handed out at its death (no command)");
                return [];
            case (2004, 45): // Register NPC Humanity Event Value(npc, base flag): DS1's per-NPC humanity counter in flags
                // session 10 (e3): DS2 NPCs have no humanity counter (no command, no param); dropped, reported (P)
                if (Chr(I(0)) != null) absent.Add($"2004:45 NPC humanity counter of chr {I(0)} (no DS2 counterpart)");
                return [];
            // commands that only shape a character DS2 doesn't have yet (no DS2 rule needed until it is ported)
            case (2004, 15) when I(0) != Player && ctx.IsBoss(I(0)) && ctx.Generator(I(0)) != null:
                // session 11 (ret11), rule B-inv (asy10 enemies.md §3 event rules): Set Character Invincibility on a 1:1 boss → no command: its
                // generator's invincibleWithoutBossBattle (0x48 = 0x6C) keeps it invincible until 132201 starts the battle, DS1's pattern
                // (11810320 invincible on the first visit; 11815382 invincible until the player is on the pit floor, then off + HP bar)
                absent.Add($"rule B-inv: boss {I(0)} invincibility {(I(1) != 0 ? "on" : "off")} = its generator's invincibleWithoutBossBattle until the battle starts");
                return [];
            case (2004, 17) when I(0) != Player && ctx.IsBoss(I(0)) && ctx.Generator(I(0)) != null:
                // session 11 (ret11), rule E-REQ-B (P): Request Character AI Command on a 1:1 boss → no command, and the boss AI reads
                // ai:GetEventRequest as 0 (Ds1AiInterpreter: unsupported → nil, reported). Exact for every request DS1 issues only while
                // the boss's AI cannot run: the Stray Demon's 10 (dormant patrol, Act09) is issued by 11810320 on the first visit,
                // when its logic flag is off (11815382, which wakes it, starts only once 11810000 is set) and the pit is unreachable;
                // the same event ENDs at load once 11810000 is set, and its -1 (clear) runs before any wake. The general rule (one
                // logicEventFlag channel per request value, f5903 in the AI: asy10 enemies.md E-REQ / CH) is open
                absent.Add($"rule E-REQ-B: boss {I(0)} AI command {I(1)} (slot {I(2)}) issued while its AI is off: no command");
                return [];
            case (2004, 17) when I(0) != Player && !ctx.IsBoss(I(0)) && ctx.Generator(I(0)) != null && Ai.EventRequests.IsRegistered(ctx.Ds1Map) && ctx.AiChannels(I(0)) is { Count: 0 }:
                // rule E-REQ-P: a placed regular without channels (template AI, or its think has none): the request reaches nothing (no-op)
                absent.Add($"rule E-REQ-P: chr {I(0)} AI command {I(1)} (slot {I(2)}): no translated DS1 AI reads it (no-op)");
                return [];
            case (2004, 17) when I(0) != Player && ctx.AiChannels(I(0)) is { Count: > 0 } channels:
            {
                // session 11 (fx11), rule E-REQ-P: Request Character AI Command(entity, v, slot) on a placed regular whose think reads
                // the slot (GetEventRequest → f5903(k) of its generator's logicEventFlag(k+1)): v sets the (slot, v) channel's flag and
                // clears the slot's other channels; v = −1 clears them all (DS1: "no request"). Firelink 11025150: the player in
                // 1022710 → the aqueduct Warriors / Soldier hold (act 8 Wait), the firebomb Hollow 1020161 walks to 1022711 and throws.
                var slot = channels.Where(c => c.Slot == I(2)).ToList();
                if (slot.Count == 0) { absent.Add($"rule E-REQ-P: chr {I(0)} AI command {I(1)} on slot {I(2)}, which its think never reads (no-op, as in DS1)"); return []; }
                if (I(1) == -1) return [.. slot.Select(c => C(130101, c.Flag, 0))];
                var on = slot.Where(c => c.V == I(1)).ToList();
                if (on.Count == 0) { missing.Add($"rule E-REQ-P: chr {I(0)} AI command {I(1)} on slot {I(2)} has no channel"); return null; }
                return [.. slot.Where(c => c.V != I(1)).Select(c => C(130101, c.Flag, 0)), C(130101, on[0].Flag, 1)];
            }
            case (2004, 20) when I(0) != Player && !ctx.IsBoss(I(0)) && ctx.Generator(I(0)) != null && Ai.EventRequests.IsRegistered(ctx.Ds1Map):
                // Request Character AI Re-plan: no DS2 command; the battle machine re-reads f5903 at its next think (P: DS1 re-plans at once)
                absent.Add($"rule E-REQ-P: chr {I(0)} AI re-plan (no command: next think)");
                return [];
            case (2004, 2) when I(1) == 9 && I(0) != Player && ctx.NpcHostile(I(0)):
                // session 11 (ret11), rule N-H2: Set Team Type 9 (Hostile Ally) on a placed NPC whose generator carries the hostility flag
                // (NpcPorter.HostileAtLoad: ON whenever DS1 has the NPC present) → no command; keeps event 50 (incl. its NG+ branch) translatable
                absent.Add($"rule N-H2: chr {I(0)} team type 9 = its generator's hostility flag (logicEventFlag3)");
                return [];
            // session 11 (burg11), rules CR1 / CR9 (opt-in): network-only commands, invincibility / AI target on placed characters
            case (2004, 40) or (2004, 42) when ctx.CharacterRulesM3 && I(0) != Player && I(1) != 1:
                // rule CR15b: a warp onto another character / object (the cut tail onto the boss's dummy) → no command (P)
                if (Chr(I(0)) == null) return [];
                absent.Add($"rule CR15b: {x.Bank}:{x.ID} warp of {I(0)} onto entity {I(2)} (type {I(1)}: no DS2 command)");
                return [];
            case (2004, 40) or (2004, 42) when ctx.CharacterRulesM3 && I(0) != Player && I(1) == 1:
            {
                // rule CR15: Warp Character and Set / Copy Floor(entity, area, point, …) on a placed character → 131704(generator, point) (as 2004:41)
                if (Chr(I(0)) is not int g40) return [];
                var p40 = ctx.Point(I(2));
                if (p40 == null) { missing.Add($"point {I(2)}"); return null; }
                return [C(131704, g40, p40.Value)];
            }
            case (2004, 2) or (2004, 44) or (2004, 19) when ctx.CharacterRulesM3 && I(0) is > 0 and < 10000 && ctx.NpcHostile(I(0)):
                // rule N-H3: an NPC DS1 turns hostile by event (team type / AI id after its hostility flag is set) → its generator's hostility flag
                absent.Add($"rule N-H3: chr {I(0)} {x.Bank}:{x.ID} = its generator's hostility flag (logicEventFlag3)");
                return [];
            case (2005, 5) when ctx.CharacterRulesM3: // Initialize ObjAct(entity, …): rule CR18, its helper's key guide shown again (131638 0)
                return ctx.ObjActHelper(I(0)) is int oh ? [C(131638, oh, 0)] : [];
            case (2003, 17) when ctx.CharacterRulesM3: // Randomly Set Event Flag In Range(min, max, state): rule RND1 (P), the first flag
            {
                if (ctx.Flag(I(0)) is not int rf) { missing.Add($"flag {I(0)}"); return null; }
                absent.Add($"rule RND1: random flag in {I(0)}..{I(1)} → {I(0)} (no DS2 random flag command)");
                return [C(130101, rf, I(2) != 0 ? 1 : 0)];
            }
            case (2004, 22) or (2004, 26) or (2004, 27) or (2004, 24) or (2004, 6) when ctx.CharacterRulesM3 && I(0) != Player:   // session 11 (dchr11): + 2004:24 part SE/SFX, 2004:6 the break clip request (TC1-E)
                // rule TC1-E: Create NPC Part / Change DispMask / HitMask → no command: DS2's part break (BossPartsParam HP, BossPartsModelParam
                // breakCulling / disableHitParts) owns the part's HP, its mesh and its hits
                if (Chr(I(0)) == null) return [];
                absent.Add($"rule TC1-E: {x.Bank}:{x.ID} {Name(x)} on {I(0)} (DS2 part break params own it)");
                return [];
            case (2004, 44) or (2004, 19) when ctx.CharacterRulesM3 && I(0) != Player && ctx.Generator(I(0)) == null:
                absent.Add($"chr {I(0)}"); return []; // an NPC this map does not place
            case (2003, 32) when ctx.CharacterRulesM3: // Clear Event Value(base flag, bits): rule EV1, every bit off
            {
                var clear = new List<ESD.CommandCall>();
                for (int b = 0; b < I(1); b++) { if (ctx.Flag(I(0) + b) is not int fb) { missing.Add($"flag {I(0) + b}"); return null; } clear.Add(C(130101, fb, 0)); }
                return clear;
            }
            case (2005, 10) when ctx.CharacterRulesM3: // Register Statue (DS1's online petrified-player statues): rule CR20, no DS2 counterpart
                absent.Add($"rule CR20: 2005:10 statue {I(0)} (online petrification statues: no DS2 counterpart)");
                return [];
            case (2005, 13) when ctx.CharacterRulesM3:
                absent.Add($"rule CR9: 2005:13 object {I(0)} invulnerability (no DS2 command)");
                return [];
            case (2004, 36) when ctx.CharacterRulesM3: // Hellkite Breath Control(character, object, character animation)
            {
                // session 11 (chrai11), rule HB1: DS1's engine sweeps fire behaviours along the object's dummy line (o1060: 114 dummies over
                // 100 m of bridge) while the clip plays; DS2 events cannot place damage along points: the breath = DS1's breath bullet
                // (Bullet 34300 ballistics + AtkParam_Npc 343000 fire) attached to the dragon (131821 「敵に弾をアタッチ」, vanilla m10_23), P
                if (Chr(I(0)) == null) return [];
                if (ctx.Breath(I(0), I(1), I(2)) is { } br) { absent.Add($"rule HB1: Hellkite Breath Control on {I(0)} (clip {I(2)}, object {I(1)}) = breath bullet {br.Bullet} attached to generator {br.Generator}"); return [C(131821, br.Generator, br.Bullet)]; }
                missing.Add($"2004:36 Hellkite Breath Control on {I(0)} (no breath bullet)");
                return null;
            }
            case (2004, 31) or (2004, 35) or (2006, 4) or (2006, 5) when ctx.CharacterRulesM3:
                absent.Add($"rule CR9: {x.Bank}:{x.ID} {Name(x)} (no DS2 command)");
                return [];
            case (2004, 28) or (2004, 34) or (2003, 16) when ctx.CharacterRulesM3:
                absent.Add($"rule CR1: {x.Bank}:{x.ID} {Name(x)} (network only: no-op offline / for the host)");
                return [];
            case (2004, 15) or (2004, 16) or (2004, 43) when ctx.CharacterRulesM3 && I(0) != Player:
                if (Chr(I(0)) == null) return [];
                absent.Add($"rule CR9: {x.Bank}:{x.ID} {Name(x)} on {I(0)} (no DS2 command)");
                return [];
            case (2004, 20) when ctx.CharacterRulesM3 && I(0) != Player:
                if (Chr(I(0)) == null) return [];
                absent.Add($"rule CR9: AI re-plan of {I(0)} (no command: next think)");
                return [];
            case (2004, 2) or (2004, 15) or (2004, 17): // (2004:9/13/21: chrint's rules; 2004:37/45: e3's rules N4/2004:45)
                if (I(0) != Player && Chr(I(0)) == null) return [];
                missing.Add($"{x.Bank}:{x.ID} {Name(x)} on {I(0)}");
                return null;
            // ---- bosses
            case (2003, 11): // Display Boss Health Bar(state, entity, slot, name): a present boss is handled by the state loop
            {                // (guarded by f132201); here: no DS1 boss data, or the boss is absent (no-op)
                var b = ctx.BossBattle(I(1), I(3));
                if (b == null) { missing.Add($"boss bar for chr {I(1)} (no DS1 boss data)"); return null; }
                if (Chr(I(1)) == null) return [];
                return [C(I(0) != 0 ? 132201 : 132204, b.Value)];
            }
            case (2003, 12): // Handle Boss Defeat(entity)
            {
                var b = ctx.BossBattle(I(0), 0);
                if (b == null) { missing.Add($"boss defeat for chr {I(0)} (no DS1 boss data)"); return null; }
                if (Chr(I(0)) == null) return [];
                return [C(132202, b.Value)];
            }
            // ---- items
            case (2003, 36) or (2003, 4): // Award Items (Including Clients)(lot) / Award Item Lot(lot)
            {
                // rule L0 (chrint stage 4): DS1 item lot 0 = "no lot". DS1's ItemLotParam row 0 is a DEBUG row (100000 /
                // 10000 / 240), never a reward; every parameterised award guards it (common event 970: SKIP IF X == 0
                // before each 2003:36). No row is built and nothing is awarded (was: row 1000000000 in two dead states).
                if (I(0) == 0) return [];
                var lot = ctx.ItemLot(I(0));
                if (lot == null) { missing.Add($"item lot {I(0)} (no item in DS2 yet)"); return null; }
                return [C(131402, lot.Value, 1)];
            }
            // session 10 (asy-items i2), rule S2: DS1's drop trades (Snuggly). Spawn Snuggly Item(lot, area, trigger, hitbox)
            // = award the lot and set its getItemFlagId (DS2 has no "item appears in the nest"); Set Snuggly Next
            // Trade(flag) = set the trigger flag (only inside a drop-trade event, which DropTrades rebuilds as a whole)
            case (2003, 34):
            {
                var lot = ctx.ItemLot(I(0));
                if (lot == null) { missing.Add($"item lot {I(0)} (no item in DS2 yet)"); return null; }
                if (ctx.ItemLotFlag(I(0)) is not int lf || ctx.Flag(lf) is not int lfd) { missing.Add($"getItemFlag of lot {I(0)}"); return null; }
                return [C(131402, lot.Value, 1), C(130101, lfd, 1)];
            }
            case (2003, 33):
            {
                if (ctx.Flag(I(0)) is not int tf) { missing.Add($"flag {I(0)}"); return null; }
                return [C(130101, tf, 1)];
            }
            // ---- objects
            case (2005, 1) or (2005, 2) or (2005, 4): // Request Object Destruction / Restoration, Set Object Treasure State
            {
                int e = I(0);
                if (ctx.Object(e) is int o)
                    return x.ID switch
                    {
                        // rule WE8 (session 10): a scripted floor break DS1 survives but DS2's fall would kill → no fall damage once
                        1 when ctx.BreakNeedsFallGuard(e) => [C(130307), C(131613, o, o, 0)],
                        1 => [C(131613, o, o, 0)], 2 => [C(131652, o)], _ => [C(131411, o, I(1) != 0 ? 1 : 0)],
                    };
                if (ctx.ObjectAbsent(e)) { absent.Add($"object {e}"); return []; }
                missing.Add($"object {e}");
                return null;
            }
            case (2005, 8): // Reproduce Object Destruction(entity, slot): rule WE6 (session 10): the destroyed state shown at
            {               // once = the object hidden (DS2's breakobj debris poses: later, U)
                if (ctx.Object(I(0)) is int o8) return [C(130702, o8, 0)];
                if (ctx.ObjectAbsent(I(0))) { absent.Add($"object {I(0)}"); return []; }
                missing.Add($"object {I(0)}");
                return null;
            }
            case (2005, 9): // Create Damaging Object(flag, entity, …): the object's instance row deals the damage
                if (ctx.ObjectDamages(I(1))) return [];
                missing.Add($"2005:9 damaging object {I(1)}");
                return null;
            // ---- session 9 (track firelink)
            case (2003, 8): // Set Event State(event, slot, state): Restart → the target's restart-request flag (see Translate)
            {
                if (I(2) != 1) { missing.Add($"2003:8 event state {I(2)} (only Restart has a rule)"); return null; }
                if (!restartTargets.Contains((I(0), I(1))) || ctx.Flag(I(0) + I(1)) is null) { missing.Add($"2003:8 restart of event {I(0)} slot {I(1)} (not in this map's script)"); return null; }
                if (FlagRules.Restart(ctx.Ds1Map, I(0) + I(1)) is not int request) { missing.Add($"2003:8 restart of event {I(0)} slot {I(1)}: no request flag (FlagRules F3)"); return null; }
                return [C(130101, request, 1)];
            }
            case (2004, 10) or (2004, 12): // Set Character Gravity / Immortality: only shape a character (as 2004:2 …)
                if (I(0) != Player && Chr(I(0)) == null) return [];
                if (ctx.CharacterRulesM3 && I(0) != Player) { absent.Add($"rule CR9: {x.Bank}:{x.ID} {Name(x)} on {I(0)} (no DS2 command)"); return []; }
                missing.Add($"{x.Bank}:{x.ID} {Name(x)} on {I(0)}");
                return null;
            // ---- collision
            case (2011, 1): // Activate Hit(hit, state)
                return ctx.HitGroup(I(0)) is int hg ? [C(132153, hg, I(1) != 0 ? 1 : 0)] : []; // left out by the builder: nothing to toggle
            // ---- cutscenes
            case (2002, 4): // Play Cutscene and Warp Player(cutscene, method, point, area, block, player)
            {
                bool here = I(3) == int.Parse(ctx.Ds1Map[1..3]) && I(4) == int.Parse(ctx.Ds1Map[4..6]);
                if (here) return []; // same-map: DS2 arrives at the start point (no cutscene converter yet)
                var mp = ctx.MapPoint(I(3), I(4), I(2));
                if (mp == null) { missing.Add($"map warp to m{I(3)}_{I(4):D2} point {I(2)}"); return null; }
                var (before, after) = warpDemos.TryGetValue(x, out var d) ? d : (0, 0);
                return [C(130613, 1), C(132125, before, after, mp.Value.Map, mp.Value.Point, 0)];
            }
            // ---- sound
            case (2010, 3): // Set Map Sound State(entity, state): the map's DS1 sound region (BGM) started / stopped
            {
                if (ctx.Sound(I(0)) is int snd) return [C(I(1) != 0 ? 131201 : 131202, snd)];
                absent.Add($"sound region {I(0)}"); // the map builder placed no region for it (DS1 sound not ported)
                return [];
            }
            // dropped (no DS2 counterpart needed): network sync, prefetch, save request, achievements, MP buffs, boss-room
            // notifications, Register Bonfire / Ladder (the map builder), Initialize Event (its own group)
            // session 10, rule G4: Register Ladder on a ladder DS1 registers later than load → its "shown" state (it starts
            // hidden, TranslateMap's G4 pass); a load-registered ladder is DS2's plain ladder (no-op)
            case (2009, 0):
                return ctx.Ladder(I(2)) is int ladder ? [C(131636, ladder, ObjActRules.LadderShown)] : [];
            case (2000, 2) or (2000, 4) or (2000, 5) or (2003, 28) or (2009, 4) or (2009, 6) or (2009, 3) or (2000, 0):
                return [];
            // session 11 (cov11), rule K6: Betray Character's Current Covenant → leave it (130381(0): DS2 sets its own "left
            // covenant" flag 200899 + id) and clear the sub-identity flags; Increment Player PvP Sin → dropped (DS2 scripts have
            // no sin command: 130310 only resets; listed as absent). DS1's own flags (F742/743/746) stay its 2003:2s
            case (2004, 38):
                if (Build.Talk.Covenants.Betray() is { } betray) return betray.Select(b => C(b.Cmd, b.Args)).ToList();
                missing.Add("2004:38 betray: no covenants.tsv sub flag slot");
                return null;
            case (2004, 46):
                absent.Add("2004:46 PvP sin (rule K6: no DS2 command)");
                return [];
            // session 10 (asy-sound, rule SND4): Play SE(entity, type, id) → 131203(key) 「ポイント指定でワンショットSE再生」 on the
            // point Sound region the map builder placed at the entity (stop + play: FUN_14047d7d0)
            case (2010, 2):
            {
                if (ctx.SoundEffect(I(0), I(1), I(2)) is int key) return [C(131203, key)];
                absent.Add($"SE {MapSounds.Ds1Event(I(1), I(2))} at {I(0)}");
                return [];
            }
            // session 10 (asy-sound, rule SND5): Spawn Oneshot SFX(type, entity, dummy, sfx) → 131503(key) on the keyed SFX
            // region placed at the entity
            case (2006, 3):
            {
                if (ctx.OneShotSfx(I(1), I(3)) is int key) return [C(131503, key)];
                absent.Add($"one-shot SFX {I(3)} at {I(1)}");
                return [];
            }
            // provisional drops (rulebook P): cutscenes without a warp (no DS1 cutscene port yet), respawn point (DS2
            // provisional drops (rulebook P): cutscenes without a warp (no DS1 cutscene port yet)
            case (2002, 3):
                return [];
            case (2003, 13) when ctx.StandInsAreNoOps: // Modify Navimesh Connection Bitflag: rule NV3 pending (ST1 opt-in)
                absent.Add($"2003:13 navmesh flag on {I(0)} (rule NV3 pending)");
                return [];
            case (2003, 23): // Set Player Respawn Point(point)
                // session 10 (e3), rule G2 (gap): DS2 respawns the player at the last rested bonfire, kept in the save
                // (USER_DATA +0x28ECE: u8 BB, u8 AA, u16 bonfire id, docs/ds2-re-notes.md) and written by the engine's rest;
                // no event command sets it (EDD: 456 commands, none; vanilla scripts: none) → no command, reported
                absent.Add($"rule G2: respawn point {I(0)} (DS2: the last rested bonfire; no event command sets it)");
                return [];
            default:
                missing.Add($"{x.Bank}:{x.ID} {Name(x)}");
                return null;
        }
    }

    // ---------------------------------------------------------------- event 0's load-time flag writes (fix pass)
    /// <summary>
    /// DS1 event 0 run through once per assignment of the flags it tests (<see cref="LoadRun.Vars"/>): per flag it writes,
    /// the value it has when event 0 is through (null: not written in that run). A run stops at a wait or a test of
    /// registered conditions (writes after it are not the load frame's); flag toggles or more than 12 tested flags → no
    /// analysis (null).
    /// </summary>
    sealed record LoadRun(List<int> Vars, Dictionary<int, bool?[]> Writes);
    LoadRun? loadRun;
    bool loadRunDone;

    LoadRun? LoadWrites()
    {
        if (loadRunDone) return loadRun;
        loadRunDone = true;
        var ev = emevd.Events.FirstOrDefault(e => e.ID == 0);
        if (ev == null) return null;
        var ins = ev.Instructions;
        int Flag(double type, double id) => (int)type is 1 or 2 ? (int)ev.ID : (int)id;
        var vars = ins.Where(x => (x.Bank, x.ID) is (1003, 1) or (1003, 2)).Select(V).Select(v => Flag(v[2], v[3])).Distinct().Order().ToList();
        if (vars.Count > 12) return null;
        var writes = new Dictionary<int, bool?[]>();
        for (int a = 0; a < 1 << vars.Count; a++)
        {
            var store = vars.Select((f, k) => (f, on: (a >> k & 1) == 1)).ToDictionary(p => p.f, p => p.on);
            var written = new Dictionary<int, bool>();
            for (int pc = 0; pc < ins.Count;)
            {
                var x = ins[pc];
                var v = V(x);
                void Write(int f, double state) { written[f] = state == 1; store[f] = state == 1; }
                switch (x.Bank, x.ID)
                {
                    case (1003, 1) or (1003, 2): // SKIP / END IF Event Flag(n | end type, state, type, id)
                        pc = store[Flag(v[2], v[3])] == (v[1] != 0) ? (x.ID == 1 ? pc + 1 + (int)v[0] : ins.Count) : pc + 1;
                        break;
                    case (1003, 5) or (1003, 6): // multiplayer state: single player = Host (the translator's rule)
                        pc = (int)v[1] == 0 ? (x.ID == 5 ? pc + 1 + (int)v[0] : ins.Count) : pc + 1;
                        break;
                    case (1003, 7): // player in/out map: the event's own map (the translator's rule)
                        pc = v[1] != 0 ? pc + 1 + (int)v[0] : pc + 1;
                        break;
                    case (1000, 3): pc += 1 + (int)v[0]; break;
                    case (1000, 4): pc = ins.Count; break;
                    case (2003, 2):
                        if ((int)v[1] is not (0 or 1)) return null; // toggle: no analysis
                        Write((int)v[0], v[1]); pc++;
                        break;
                    case (2003, 22):
                        if ((int)v[2] is not (0 or 1)) return null;
                        for (int f = (int)v[0]; f <= (int)v[1]; f++) Write(f, v[2]);
                        pc++;
                        break;
                    case (1000, 1) or (1000, 2) or (1000, 7) or (1000, 8) or (1001, _): pc = ins.Count; break; // registered conditions / waits
                    case (0 or 1 or 3 or 4 or 5 or 11, _) when v.Length > 0 && (int)v[0] == 0: pc = ins.Count; break; // a MAIN wait
                    default: pc++; break;
                }
            }
            foreach (var (f, on) in written)
                (writes.TryGetValue(f, out var arr) ? arr : writes[f] = new bool?[1 << vars.Count])[a] = on;
        }
        return loadRun = new LoadRun(vars, writes);
    }

    /// <summary>
    /// A leading flag test folded with event 0's load writes: DS1 flag <paramref name="ds1Flag"/> == <paramref name="on"/>
    /// as event 0 leaves it, as a DS2 expression over the flags now (prime implicants of the truth table over event 0's tested
    /// flags and the flag itself, reduced to the flags it depends on); null when event 0 doesn't write the flag or a flag has
    /// no DS2 rule. 11810310's "END IF 11810314 ON" → (f6(101000314) &amp;&amp; f6(101000312)): event 0 clears 11810314 unless the
    /// gate flag 11810312 is set.
    /// </summary>
    byte[]? FoldedFlagTest(int ds1Flag, bool on)
    {
        var lr = LoadWrites();
        if (lr == null || !lr.Writes.TryGetValue(ds1Flag, out var w)) return null;
        var vars = lr.Vars.ToList();
        if (!vars.Contains(ds1Flag)) vars.Add(ds1Flag);
        int n = vars.Count, self = vars.IndexOf(ds1Flag), run = (1 << lr.Vars.Count) - 1;
        bool G(int a) => (w[a & run] ?? (a >> self & 1) == 1) == on;
        var essential = Enumerable.Range(0, n).Where(k => Enumerable.Range(0, 1 << n).Any(a => G(a) != G(a ^ 1 << k))).ToList();
        if (essential.Count == 0) return [(byte)(0x40 + (G(0) ? 1 : 0))];
        // minterms over the essential flags (the others at 0: they don't change G), then prime implicants (Quine-McCluskey)
        int m = essential.Count;
        int Full(int t) => essential.Select((k, j) => (t >> j & 1) << k).Sum();
        var level = Enumerable.Range(0, 1 << m).Where(t => G(Full(t))).Select(t => (Val: t, Care: (1 << m) - 1)).ToHashSet();
        var primes = new HashSet<(int Val, int Care)>();
        while (level.Count > 0)
        {
            var next = new HashSet<(int, int)>(); var merged = new HashSet<(int, int)>();
            foreach (var p in level)
                foreach (var q in level)
                    if (p.Care == q.Care && (p.Val ^ q.Val) is int d && d != 0 && (d & d - 1) == 0 && (d & p.Care) != 0)
                    { next.Add((p.Val & ~d, p.Care & ~d)); merged.Add(p); merged.Add(q); }
            foreach (var p in level) if (!merged.Contains(p)) primes.Add(p);
            level = next;
        }
        byte[]? expr = null;
        foreach (var (val, care) in primes.OrderBy(p => p.Care).ThenBy(p => p.Val))
        {
            byte[]? term = null;
            for (int j = 0; j < m; j++)
            {
                if ((care >> j & 1) == 0) continue;
                if (ctx.Flag(vars[essential[j]]) is not int f2) return null;
                var lit = FlagExpr(f2, (val >> j & 1) == 1);
                term = term == null ? lit : [.. term, .. lit, 0x98];
            }
            term ??= [0x41];
            expr = expr == null ? term : [.. expr, .. term, 0x99];
        }
        return expr;
    }

    // ---------------------------------------------------------------- helpers
    string Name(EMEVD.Instruction x) => defs.TryGetValue((x.Bank, x.ID), out var d) ? d.Name : "?";
    /// <summary>DS1 character entities: map characters 1AAB0nnn / 1AAB3nnn and global NPCs 6000-6999.</summary>
    static bool IsCharacter(int e) => e is >= 6000 and < 7000 || e >= 1_000_000 && e % 10000 / 1000 is 0 or 3;

    readonly Dictionary<int, long?> portedDeaths = [];
    /// <summary>Review e3 (rule N3b): the DS1 death event NpcPorter ports for this NPC (its dead flag), null = none.</summary>
    long? PortedDeathEvent(int npc)
    {
        if (!portedDeaths.TryGetValue(npc, out var e))
            portedDeaths[npc] = e = Build.Chr.NpcPorter.FindDeath(Program.DefaultDsrRoot, ctx.Ds1Map, npc)?.Event;
        return e;
    }

    /// <summary>Session 10 (e3, rule N3): <paramref name="y"/> inserted after instruction <paramref name="at"/>; a SKIP
    /// before it whose target lies beyond <paramref name="at"/> skips one line more (it still lands on the instruction it
    /// named, so a branch that jumps past the insertion is not delayed).</summary>
    internal static List<EMEVD.Instruction> InsertAfter(List<EMEVD.Instruction> ins, int at, EMEVD.Instruction y)
    {
        var list = new List<EMEVD.Instruction>(ins.Count + 1);
        for (int i = 0; i < ins.Count; i++)
        {
            var x = ins[i];
            if (i <= at && (x.Bank, x.ID) is (1000, 1) or (1000, 3) or (1000, 5) or (1000, 7) or (1003, 1) or (1003, 3) or (1003, 5) or (1003, 7)
                && x.ArgData.Length > 0 && i + 1 + x.ArgData[0] > at)
            {
                var z = new EMEVD.Instruction(x.Bank, x.ID, (byte[])x.ArgData.Clone());
                z.ArgData[0]++;
                x = z;
            }
            list.Add(x);
            if (i == at) list.Add(y);
        }
        return list;
    }

    internal static EMEVD.Instruction Substitute(EMEVD.Event ev, int i, EMEVD.Instruction x, byte[] args)
    {
        var y = new EMEVD.Instruction(x.Bank, x.ID, (byte[])x.ArgData.Clone());
        foreach (var p in ev.Parameters.Where(p => p.InstructionIndex == i))
            if (p.SourceStartByte + p.ByteCount <= args.Length)
                Array.Copy(args, p.SourceStartByte, y.ArgData, p.TargetStartByte, p.ByteCount);
        return y;
    }

    static readonly byte[] True = [0x41, 0xA1];
    /// <summary>The evaluator calls f140001 (push int 140001, then its slot and a 1-argument call).</summary>
    static bool ReadsGroup(byte[] b)
    {
        for (int i = 0; i + 6 < b.Length; i++)
            if (b[i] == 0x82 && BitConverter.ToInt32(b, i + 1) == 140001 && b[i + 6] == 0x85) return true;
        return false;
    }
    /// <summary>Rule G1c (track nestfix): every flag test of <see cref="FlagExpr"/>'s shape (f6(flag) != 0 / == 0) in an
    /// evaluator → f140001(slot) of a new 140301(slot, flag, 1|0) registration on the next free slot; null when there is
    /// none or the free slots run out (the evaluator is then left as it is).</summary>
    static byte[]? GateFlags(byte[] b, Queue<int> free, List<ESD.CommandCall> entry)
    {
        var o = new List<byte>();
        var added = new List<ESD.CommandCall>();
        var slots = new Queue<int>(free);
        for (int i = 0; i < b.Length;)
        {
            if (i + 10 <= b.Length && b[i] == 0x46 && b[i + 1] == 0x82 && b[i + 6] == 0x85 && b[i + 7] == 0xA6 && b[i + 8] == 0x40 && b[i + 9] is 0x95 or 0x96)
            {
                if (slots.Count == 0) return null;
                int slot = slots.Dequeue();
                added.Add(C(140301, slot, BitConverter.ToInt32(b, i + 2), b[i + 9] == 0x96 ? 1 : 0));
                o.AddRange([0x82, .. BitConverter.GetBytes(140001), (byte)(0x40 + slot), 0x85, 0xA6]);
                i += 10;
                continue;
            }
            int len = b[i] switch { 0x80 or 0x82 => 5, 0x81 => 9, _ => 1 };
            if (b[i] == 0xA5) { len = 1; while (i + len + 1 < b.Length && (b[i + len] | b[i + len + 1]) != 0) len += 2; len += 2; }
            o.AddRange(b.AsSpan(i, Math.Min(len, b.Length - i)).ToArray());
            i += len;
        }
        if (added.Count == 0) return null;
        while (free.Count > slots.Count) free.Dequeue();
        entry.AddRange(added);
        return [.. o];
    }
    static byte[] Push(int v) => v is >= -64 and < 64 ? [(byte)(0x40 + v)] : [0x82, .. BitConverter.GetBytes(v)];
    static byte[] Int(int v) => [.. Push(v), 0xA1];
    /// <summary>The constant an argument encodes (<see cref="Int"/>'s two forms), or null.</summary>
    static int? Const(byte[] b) => b.Length == 2 && b[1] == 0xA1 && b[0] is >= 0x00 and < 0x80 ? b[0] - 0x40 : b.Length == 6 && b[0] == 0x82 && b[5] == 0xA1 ? BitConverter.ToInt32(b, 1) : null;
    static byte[] FlagExpr(int flag, bool on) => [0x46, .. Push(flag), 0x85, 0xA6, 0x40, on ? (byte)0x96 : (byte)0x95];

    /// <summary>DS2 「ゲストユーザー判定」 f130602() (guest user) and 「ホストユーザー判定」 f130601() (host of a session).</summary>
    public const int FnGuest = 130602, FnSessionHost = 130601, FnMembers = 130661;
    /// <summary>Session 10 (e3, rule G5): DS2 「ゲーム周回数取得」 (the playthrough in progress, NG = 1).</summary>
    public const int FnGameCycle = 132241;
    static byte[] Fn0(int fn, bool nonZero) => [0x82, .. BitConverter.GetBytes(fn), 0x84, 0xA6, 0x40, nonZero ? (byte)0x96 : (byte)0x95];
    /// <summary>f130661(0, 1) 「マルチプレイ人数比較取得」 &gt; 0 (or &lt;= 0): other players present. Case 0x1fe65 of
    /// FUN_14045c6a0 → FUN_14045c520(0, true): 0 without a net object (GM+0x22f0), else FUN_140513070 + FUN_1405130d0 = the
    /// world's phantom list (+0x3b0: 5 slots, active ones, by type) plus, in a session (+0x38), the session's two counts
    /// (DAT_141616cf8+0x20 shorts +0xc/+0xe).</summary>
    static byte[] Members(bool any) => [0x82, .. BitConverter.GetBytes(FnMembers), 0x40, 0x41, 0x86, 0xA6, 0x40, any ? (byte)0x92 : (byte)0x93];

    /// <summary>
    /// Rule C1 (session 10): ONE DS1 multiplayer-state rule for the whole translator (3:6 IF, 1003:5 SKIP IF, 1003:6 END IF
    /// Multiplayer State; 4:3 player character type). DS1 enum: 0 Host, 1 Client, 2 Multiplayer, 3 Singleplayer (DSR 5:
    /// no rule). DS1's Host includes single player (11810220's floor collapse waits on "Host" and fires in offline DSR),
    /// so Host ≡ ¬Client:
    ///  - Client = DS2's own guest test f130602() != 0 (vanilla's form: 390 uses, `if f130602() != 0 → end`); decompile
    ///    case 0x1fe2a of FUN_14045c6a0: no net object (GameManager+0x22f0) → 0, else FUN_140513440 = (session flag +0x38
    ///    or members &gt; 0) &amp;&amp; guest bit (GameManagerImp vt+0x58 = GM+0x24b1 bit 6, set only by the summon / invasion
    ///    join controls): 0 offline and for a host (V-code);
    ///  - Host = f130602() == 0 (true offline: V-code);
    ///  - Multiplayer = f130602() != 0 || (f130601() != 0 &amp;&amp; f130661(0, 1) &gt; 0): DS1's Multiplayer = other players in the
    ///    world. f130601 (case 0x1fe29 → FUN_1405134b0) = +0x38 &amp;&amp; +0x39 (a session and its host; the member count is
    ///    NOT part of it: a host alone in an open session reads 1, fix pass), so the host form also needs a member count
    ///    (<see cref="Members"/>); 0 offline (V-code); online P (+0x38/+0x39/count meanings inferred; f130601 and f130661 have
    ///    no vanilla use; whether the session counts include the host itself is U: in-game check E8b);
    ///  - Singleplayer = ¬Multiplayer (true offline: V-code).
    /// The 4:3 Character Type on the player uses Host/Client only (phantom → Client; Alive + Hollow in one OR group → Host;
    /// a lone Alive/Hollow is the human/hollow form: no rule, reported).
    /// Direct functions, not group conditions: they can be negated and read anywhere (rule G1 forbids negated slot reads in
    /// a wait). Not 140602 (EventConditionNet_IsHost, eval FUN_14046fdc0): its mode 0 tests only the guest bit once a net
    /// object exists (no session check), and vanilla's 102 uses register 140602(s, 1, 0) and 140602(s, 0, 0) together as
    /// "host or guest" in boss-start conditions (m10_10 【条件】ボス：ベイグラント戦_開始) that must fire offline, so its
    /// session forms are not "Multiplayer" (evidence against COVERAGE C1's proposal; recorded in the rulebook).
    /// </summary>
    public static byte[]? NetExpr(int state) => state switch
    {
        0 => Fn0(FnGuest, false),
        1 => Fn0(FnGuest, true),
        2 => [.. Fn0(FnGuest, true), .. Fn0(FnSessionHost, true), .. Members(true), 0x98, 0x99],
        3 => [.. Fn0(FnGuest, false), .. Fn0(FnSessionHost, false), .. Members(false), 0x99, 0x98],
        _ => null,
    };
    static ESD.CommandCall C(int id, params int[] args) => new(1, id, args.Select(Int).ToArray());
    /// <summary>Session 10 (e3): a command whose last argument is a float literal (0x80 f32, vanilla "1.5f" form:
    /// 140511(0, 1.5f, 2, 1.5f)).</summary>
    static ESD.CommandCall CF(int id, int a0, float a1) => new(1, id, [Int(a0), [0x80, .. BitConverter.GetBytes(a1), 0xA1]]);
    static ESD.State S(IEnumerable<ESD.CommandCall> entry, params (byte[] If, long Then)[] conds)
    {
        var s = new ESD.State();
        s.EntryCommands.AddRange(entry);
        foreach (var (c, t) in conds) s.Conditions.Add(new ESD.Condition(t, c));
        return s;
    }
}
