using SoulsFormats;
using static Lordran.Tools.Build.Talk.TalkExpr;

namespace Lordran.Tools.Build.Talk;

/// <summary>
/// DS1 talk ESD (DSR script\talk\mAA_BB.talkesdbnd.dcx, t&lt;talkId&gt;.esd: one polling state machine per NPC) → one DS2 talk
/// ESD state group, by rule (session 8, track npc, step 2; the rulebook's talk section). Generic for every DS1 talk: the
/// machine is kept state for state (DS1's own flow, flags and branches), each DS1 talk command and condition function
/// is rewritten by a rule below, and what DS2 has no rule for is left out and listed (<see cref="Missing"/>).
///
/// DS2 mechanisms (vanilla talk ESDs/EDDs, `edd-machines talk_m10_04_00_00.edd`: the developers' 【Lib】 talk routines with
/// parameter names; `esd-usage` over every talk_mAA ESD):
///  - talk prompt = a key-guide region on the NPC: 130408(9, 600) 「キーガイド領域の作成」 (600 = KeyGuideParam "Talk";
///    【Lib】会話：ユニークキーガイド用 passes its own row: 130408(9, p2)), 130407() deletes it; f132003() 「会話要求判定」 =
///    the player pressed the button in it ('【Lib】会話：汎用' states 2-4);
///  - a line: 132001(talk, f131002(7), -1) 「会話開始を通知」 with an NpcTalkParam row (per map: msgId → talk FMG,
///    voiceId, bNextPlay = continue with the next row in table order: 1748 of 1919 continuing rows are followed by id+10,
///    44 by a larger gap); f132002() 「会話が終了判定」 = ended; 132002() 「会話キャンセルを通知」 cancels;
///  - YES/NO: 130441(mapevent text, 0, -1, 0, 0, 0), f130422() shown, f130421() 1 yes / 2 no / 3 cancel
///    ('【Lib】選択ダイアログ'); OK window 130443(text, 0, 0, -1, 0, 0, 0), f130431() shown ('【Lib】OKダイアログ');
///  - item gift: 130101(flag, 1) + 131402(lot, 1), f130412() item window shown ('【Lib】アイテム取得ダイアログ');
///  - menus: one NPC window 130461(cancel distance, camera, dummy 220, NpcEventParam) ('【Lib】メニュー開始：汎用'); its
///    buttons come from NpcEventParam (bShop + shopIdBegin/End, bTalk, bRepair, …; npcId = npcmenu.fmg name), the
///    shop runs inside the window; f130452() result: 16 talk, 17/18 normal end (Leave), 19 cancelled; 130462 closes,
///    130463 clears the result, f130453() = something was bought ('【Lib】メニュー終了：汎用' purchase / no-purchase lines).
///  - guests in co-op run no NPC talk (every vanilla NPC's top machine: f130602() 「ゲストユーザー判定」 → 130001 end).
///
/// Rules (DS1 names from Mappings\ds1-talk-esd.tsv; status P until an in-game run):
///  - TalkToPlayer(t) → 132001(t, f131002(7), -1) with NpcTalkParam rows = DS1's TalkParam chain from t (DS1 ids kept: our
///    map's own NpcTalkParam/talk FMG, consecutive DS1 rows → bNextPlay 1, the chain's last row 0) — see the context;
///    HasTalkEnded → f132002(); c1_9 (stops the current line on interruptions) → 132002();
///  - SetActionButtonText(10010200 "Talk") → 130408(9, 600); (-1) → 130407(); another DS1 prompt text (Anastacia's
///    corpse "Examine") → a KeyGuideParam copy of 600 with DS2's keyguide text of the same English words (the context);
///    IsActionPromptShown → a constant per DS2 state (fix pass, review: the first version kept it in an area variable
///    130121 / f130121 on our maps, a runtime store nobody had verified there; if DS2 kept no value, no talk could start
///    and the prompt state would re-fire every frame): DS2 has no "prompt shown" function, and DS1 sets the prompt only
///    in entry commands (2341 of 2341 SetActionButtonText calls in the 179 talks, never in exit/pass commands), so whether
///    it is shown is known statically. Each DS1 state is emitted once per prompt value it can be entered with (a product
///    of the machine with one bit: the "off" copy keeps the DS1 id, the "on" copy gets a new id), the bit changes with
///    SetActionButtonText in the entry commands, and IsActionPromptShown folds to 0 or 1 in that copy's conditions; copies
///    no transition reaches are not emitted. No runtime storage, nothing new to verify; IsActionButtonPressed → f132003();
///  - GetDistanceToPlayer → f130201() (the owner = the NPC; minus the NPC → talk-dummy distance when DS1 talks through an
///    invisible dummy, so DS1's metres stay measured from the dummy's spot); GetAngleToPlayer → 0 (no DS2 function: the key
///    guide region decides facing, 600 = 1.8 m cylinder, 80°), so "≤ 45" holds and the "&gt; 120" abort never fires;
///  - DS1's NPC initial states (common event 50: a state block whose flags are all off gets its default state on every
///    load, e.g. Anastacia 1140-1169 → 1140, which her talk requires) → set at the machine's start for the blocks the talk
///    reads or writes;
///  - Get/SetEventFlag → f6 / 130101 through FlagRules; a flag without a rule reads 0 and is not written (listed);
///  - a SetEventFlag(f, 1) whose flag DS1's common event 911 watches (flag → Award Items lot: Oscar 11810590 → lot 1082)
///    gives the lot right there: per row of DS1's lot chain (rows lot, lot+1 … while they exist; each row's
///    getItemFlagId is DS1's "already given" flag) a branch "flag set? skip : 131402(row lot, 1) + flag on" — DS2 lots have
///    no item flags, so the flags move into the script as DS2's own gift routine does;
///  - OpenGenericDialog(8 yes/no | 7 notice, Event_text_) → 130441 / 130443 with the text in mapevent.fmg, then a wait
///    until the window is shown (≤ 1 s, as DS2's routines wait before reading it); GetGenericDialogButtonResult 1 yes / 2 no
///    / 0 cancelled → f130421() 1 / 2 / 3, after a notice (no answer in DS2) → true, so DS1's "answered and closed" test is
///    "closed"; IsGenericDialogOpen → f130422() || f130431(); ForceCloseGenericDialog → 130442() + 130444();
///  - the talk list (c1_10 + AddTalkListData(index, label, flag) …, read by GetTalkListEntryResult) → the NPC window: each
///    DS1 entry by its label (Event_text_): Talk → bTalk / result 16 (several "Talk" entries: the first whose flag is on),
///    Leave/Quit → 17 or 18, Purchase Item/Weapon → bShop with the ShopLineupParam range of the OpenRegularShop the entry
///    leads to (the shop runs inside DS2's window), Repair → bRepair, Reinforce → bFortify; result 0 (backed out) → 19;
///    other entries (covenants, gestures, level up, Fire Keeper services …) are left out of the window (listed); the list's
///    exit (c1_20) → 130462 close; IsMenuOpen(11) (shop) → f130451() window shown, IsMenuOpen(63) (item popup) →
///    f130412(); f28(menu) "bought in that menu" → f130453();
///  - HasItem(category, id) → f131401(item, 1, 1, 0) != 0; ComparePlayerStat(12 sex, 0, v) → f130305() (DS1 1 male → DS2 0);
///  - the NPC's own state (fix pass, review: f6 was a constant 0, so the Undead Merchant's prompt condition
///    "… &amp;&amp; f6() &gt;= 60" folded to false and she could never be talked to): DS1 f6 = the NPC's HP in percent → f130302()
///    「HP割合取得」 (vanilla compares it with 50: talk_m10_02 g2147483641 "f130302() &gt; 50"); DS1 f3 = the NPC is dead →
///    f130301() &lt; 1 (「HP取得」, vanilla's own death test in 24 talk states: "!(f130301() &gt;= 1)"; DS2's 「死亡判定」 f130303
///    is used by no vanilla script); DS1 f5 = attacked (in wait and line states: "f5 || f3 → interrupt") and f9 = hit
///    (tested in the interrupt state DS1 goes to, to pick a hit-reaction line) → f130311(1) &gt; 0 (「ダメージを受けた回数」:
///    hits by the player, once per attack; vanilla's "took damage from the player" is f130311(1) &gt;= 1, warnings at
///    &gt;= 2 / hostile at &gt;= 3 or &gt;= 10 — session 9 opcode fix: the old dumps read these as "&gt; 1 / &gt; 3"). DS1's tests are
///    transient, DS2's count stays until 130351() 「ダメージを受けた回数をリセット」 (vanilla talk_m10_14 g7420): the talk
///    machine starts with a reset (vanilla '会話：開始'); a transition on f5 goes through an acknowledgement state (entry
///    130351, as vanilla resets in entry commands) and sets a hit bit in the target copy (the same product as the prompt
///    bit), kept through unconditional hops and consumed by the next conditional transition; f9 reads 1 while the bit is
///    set (the attack that led there), else the count; a transition on f9 is acknowledged too. So one hit gives one
///    reaction and the count can't keep re-triggering (Oscar has f5 but no f9). Evidence for the DS1 roles:
///    the 179 talks (`ds1-talk`) compare f6 with
///    90/60/40/70 (the merchant shows her prompt only while f6 &gt;= 60, plays "Why, you!" on f9 &amp;&amp; f6 &gt;= 90 and hostile lines
///    on f9 &amp;&amp; f6 &lt;= 90, matching DS1 event 11020510's hostility at HP ratio ≤ 0.9 after damage by the player); f3 == 1
///    plays Oscar's "Hrggkt... But...why..." and the Crestfallen Warrior's "Heheh, not too shabby..." (death lines);
///  - a DS1 NPC death (DS1 map event: HP ratio ≤ 0 → state block off → dead flag on, e.g. Oscar 11810520(6023, 1060,
///    1074, 1073); its NpcParam lot chain drops once per row by getItemFlagId) → when <see cref="IContext.Death"/> is
///    set, every DS1 state gets "f130301() &lt; 1 → death" right after its last condition that tests f3 (DS1's own death
///    reactions keep priority, as DS1's talk runs before its event in the frame): the block cleared and the dead flag set
///    at once (DS1's event; the generator's respawnDisableFlag is the same flag), then the lot rows a DS2 lot can't gate
///    (their flag is also a talk gift's: Oscar's F2 East Key is lot 6020 and gift 1080 under flag 50000080) by the gift
///    rule, then an idle state;
///  - talk housekeeping without a DS2 counterpart (DebugEvent, ClearTalkTimer, SetUpdateDistance, c1_5/8/12/20 outside a
///    list/32/35/43/67/68, c1_10) → dropped by rule; DS1 functions whose role is unknown get a fixed value (<see
///    cref="UnknownValue"/>: f32 1 = "talk possible", the abort tests f19/f26/f27/f31 0, …) and are listed.
/// Policy (differs from the EMEVD translator, which ships no half-translated event): the conversation is the content,
/// so a DS1 talk feature without a DS2 rule is left out (menu entry not offered, command dropped) and listed in the
/// report instead of withholding the NPC's whole talk.
/// </summary>
public sealed class TalkTranslator
{
    /// <summary>What the translation needs from the map/build (ids, rows, texts it writes).</summary>
    public interface IContext
    {
        string Ds1Map { get; }
        /// <summary>DS1 flag → DS2 flag (FlagRules), null = no rule.</summary>
        int? Flag(int ds1Flag);
        /// <summary>The NPC's DS1 death (dead flag, state block, flag-gated drop rows), null = no DS1 death event (Anastacia:
        /// immortal at Firelink in DS1) or a report without an NPC.</summary>
        DeathSpec? Death { get; }
        /// <summary>DS1 TalkParam id → the DS2 NpcTalkParam row to start (registers the chain and its FMG lines), null = no row.</summary>
        int? Talk(int ds1TalkId);
        /// <summary>DS1 Event_text_ (dialog) → mapevent.fmg id.</summary>
        int? Dialog(int ds1Text);
        /// <summary>DS1 item (talk/EMEVD item category 0 weapon, 1 armor, 2 ring, 3 goods) → DS2 item id, null = not in DS2.</summary>
        int? Item(int category, int ds1Id);
        /// <summary>The gift rows DS1's common event 911 gives when this flag turns on, null = not a gift flag.</summary>
        IReadOnlyList<GiftRow>? Gift(int ds1Flag);
        /// <summary>The NpcEventParam row for a DS1 talk list (or a lone shop), null = nothing DS2 can offer.</summary>
        int? Window(WindowSpec spec);
        /// <summary>An ItemLotParam2_Other row giving one DS2 item (DS1 GiveItem), null = none.</summary>
        int? LotFor(int ds2Item, int num);
        /// <summary>DS1 Event_text_ (for the report).</summary>
        string EventText(int id);
        /// <summary>DS1's NPC state blocks (common event 50: first, last, default flag) that contain any of these DS1 flags.</summary>
        IReadOnlyList<(int First, int Last, int Default)> StateBlocks(IEnumerable<int> ds1Flags);
        /// <summary>The KeyGuideParam row for a DS1 action-button text (10010200 "Talk" → 600), null = no DS2 prompt text.</summary>
        int? KeyGuide(int ds1Text);
        /// <summary>Metres from the NPC to the DS1 talk dummy its talk belongs to (0 = its own talk): DS1 measures the
        /// player's distance from the dummy (Anastacia's stands outside her bars), DS2 from the NPC.</summary>
        float DistanceOffset { get; }
    }

    /// <summary>One DS1 lot row of a gift chain as a DS2 lot (ItemLotParam2_Other). Flag = its DS1 getItemFlagId mapped
    /// (null: always given); Skip = DS1 pre-sets that flag in a map event the build didn't translate, or translated but
    /// without DS2 storage for the flag (rule NT1) (the row is DS1's "not this time" case, e.g. Oscar's Big Pilgrim's Key
    /// without the early demon kill); Gate = an extra "give only if" expression (none today: rule G1 gives DS1's Estus
    /// gifts as an Estus Flask Shard without a "has none" test).</summary>
    public sealed record GiftRow(int Ds1Lot, int? Lot, int? Flag, bool Skip, TalkExpr? Gate, string Note);

    /// <summary>DS1 talk item category 3 (goods) 200-215 = an Estus Flask (rule G1, Chr.Ds1Drops.IsEstus).</summary>
    static bool IsEstusItem(int talkCategory, int id) => talkCategory == 3 && id is >= 200 and <= 215;

    /// <summary>A DS1 NPC death as the talk machine carries it: DeadFlag = DS2 flag of DS1's dead flag (also the generator's
    /// respawnDisableFlag), Clear = DS2 flags of DS1's state block (set off first, as DS1's event does), Rows = the NPC's
    /// DS1 drop rows whose item flag a talk gift shares (given by the gift rule), Source = the DS1 event for the report.</summary>
    public sealed record DeathSpec(int DeadFlag, IReadOnlyList<int> Clear, IReadOnlyList<GiftRow> Rows, string Source);

    /// <summary>A DS2 NPC window: the DS1 list entries it carries. Session 11 (cov11, rule K1b): Pledge = the DS2 covenant id
    /// of DS1's "Enter Covenant" entry (NpcEventParam bPledge + pledgeType; 0 = none), Votive = the DS2 item of DS1's
    /// "Offer …" entry (bVotive + votiveItemId; 0 = none).</summary>
    public sealed record WindowSpec(int ListState, bool Talk, int ShopFirst, int ShopLast, bool Repair, bool Fortify, int Pledge = 0, int Votive = 0)
    {
        public bool Shop => ShopFirst > 0;
        public bool Any => Talk || Shop || Repair || Fortify || Pledge > 0 || Votive > 0;
    }

    // DS1 talk command ids (Mappings\ds1-talk-esd.tsv)
    const int DebugEvent = 0, TalkToPlayer = 1, SetActionButtonText = 6, SetEventFlag = 11, OpenGenericDialog = 17,
        ForceCloseGenericDialog = 18, AddTalkListData = 19, OpenRegularShop = 22, ClearTalkTimer = 36, SetUpdateDistance = 38,
        ChangePlayerStat = 47, ChangeItemCount = 52, GiveItem = 65, AcquireGesture = 66;
    // DS1 talk function ids
    const int GetDistanceToPlayer = 1, HasTalkEnded = 2, IsDead = 3, IsActionButtonPressed = 4, IsAttacked = 5, GetHpPercent = 6,
        GetAngleToPlayer = 8, IsHitByPlayer = 9, IsActionPromptShown = 14,
        GetEventFlag = 15, HasItem = 16, IsGenericDialogOpen = 21, GetGenericDialogButtonResult = 22, GetTalkListEntryResult = 23,
        IsMenuOpen = 25, BoughtInMenu = 28, ComparePlayerStat = 45, PhantomPresent = 52, CovenantPoints = 60, PlayerStatValue = 29;
    /// <summary>Session 11 (burg11): DS1 GetPlayerStat ids (DSR FUN_1404d7d90, research\f29.md): 6 Faith, 13 co-op successes.</summary>
    const int Ds1StatFaith = 6, Ds1StatCoopSuccess = 13;
    /// <summary>DS2 NPC window results of its covenant buttons (f130452; vanilla talk_m10_17 g2147483634: 5 → its join
    /// routine, 6 → its offering routine).</summary>
    const int WindowPledge = 5, WindowVotive = 6;
    /// <summary>DS2 talk EDD (talk_m10_04_00_00.edd): 130301 「HP取得」, 130302 「HP割合取得」, 130311 「ダメージを受けた回数」
    /// (argument 1 as in 79 of 80 vanilla uses), 130351 「ダメージを受けた回数をリセット」.</summary>
    const int Ds2Hp = 130301, Ds2HpPercent = 130302, Ds2DamageCount = 130311, Ds2DamageCountReset = 130351;

    /// <summary>DS1's dead test as DS2 tests it: HP &lt; 1 (vanilla's "!(f130301() &gt;= 1)", opcode 0x94 = &gt;= in DS2's
    /// evaluator; the bytes are the same as before the session-9 opcode fix, 0x91 = &lt;).</summary>
    static TalkExpr DeadTest() => B(Lt, F(Ds2Hp), N(1));
    /// <summary>DS1's hit tests: the player hit the NPC since the last reaction, f130311(1) &gt; 0 (opcode 0x92). DS2:
    /// f130311(n) = byte n of the owner's generator-record hit counters (record +0x74 + n; +0x74 any attacker, +0x75 the
    /// player, once per attack: FUN_140416d60 keeps the player's recent victims), 130351 zeroes both (FUN_140457c90 case
    /// 0x1fd2f). Vanilla's "took damage from the player" is the same predicate, f130311(1) &gt;= 1 (talk_m10_14 g7420).
    /// Session 9: the first version wrote "&gt; 0" with opcode 0x94, which DS2 evaluates as &gt;= 0: always true.</summary>
    static TalkExpr HitTest() => B(Gt, F(Ds2DamageCount, N(1)), N(0));

    /// <summary>DS1 talk commands dropped by rule (no DS2 counterpart needed; the evidence is in the name table).</summary>
    static readonly Dictionary<int, string> Dropped = new()
    {
        [DebugEvent] = "DebugEvent (debug label)", [5] = "c1_5 (after interrupted lines)", [8] = "c1_8 (talk reset)", [10] = "c1_10 (list open: the NPC window)",
        [12] = "c1_12 (list force-release)", [32] = "c1_32 (close message box)", [35] = "c1_35 (talk timer)", [ClearTalkTimer] = "ClearTalkTimer",
        [SetUpdateDistance] = "SetUpdateDistance", [43] = "c1_43", [50] = "c1_50 (bonfire list)", [67] = "c1_67 (after reaction lines)", [68] = "c1_68",
        [60] = "c1_60 (pledge UI, with ChangePlayerStat(11, 5, n); session 11 rule K1b)",
    };

    /// <summary>
    /// Fixed values for DS1 talk functions whose role is unknown (name table status U) or that DS2 has no counterpart
    /// for. Chosen so the ordinary talk flow runs: f32 must be 1 before DS1 shows the Talk prompt (every NPC); the abort
    /// tests (f19, f26, f27, f31: with f3 the four "talk impossible" tests, f34 player count-like, f48 …) never fire;
    /// GetPlayerChrType 0 (DS1 living: DS2's own hollowing is not DS1's); CompareBonfireLevel / covenant / soul prices: 0
    /// (their menus are not offered: DS2 owns bonfires, so the 74 bonfire talks keep no reachable prompt). f3/f5/f6/f9
    /// have rules since the fix pass (<see cref="Function"/>).
    /// </summary>
    public static int UnknownValue(int fn) => fn switch
    {
        32 => 1,
        _ => 0,
    };

    readonly ESD ds1;
    readonly IContext ctx;
    readonly int talkId;
    readonly Dictionary<long, ESD.State> src;
    readonly Dictionary<long, ESD.State> outStates = [];
    long nextState;
    readonly SortedDictionary<string, int> missing = [];
    readonly List<string> notes = [];
    /// <summary>List state → its window(s). Session 11 (cov11): Variants = (condition, NpcEventParam row) when DS1 shows a
    /// covenant entry only while a flag holds (the Sunlight Altar's "Offer Sunlight Medal" for members only): one window
    /// row per combination, opened with conditional commands (bank 5, as vanilla's 5:130381 / 5:130462); one variant
    /// with a null condition otherwise.</summary>
    readonly Dictionary<long, (WindowSpec Spec, int? Window, List<(int Index, string Kind, int Flag)> Entries, List<(TalkExpr? Cond, int Window)> Variants)> lists = [];
    /// <summary>Session 11 (gest11), rule GS3b: list state → its "Learn gesture" entries (DS1 entry flag, gesture flag, index).</summary>
    readonly Dictionary<long, List<(int EntryFlag, int GestureFlag, int Index)>> listGestures = [];
    /// <summary>Session 11 (cov11, rule K3): the DS1 covenants this talk joins or counts points for (the candidates of
    /// ChangePlayerStat(15, 0, k)).</summary>
    readonly List<Covenants.Row> pointCovenants = [];
    readonly HashSet<int> kneelNoted = [], mirrorNoted = [];
    /// <summary>Every DS1 flag the talk reads or writes (constant arguments), for the NPC state blocks.</summary>
    readonly HashSet<int> ds1Flags = [];
    /// <summary>The DS1 state being translated opened a notice (OpenGenericDialog kind 7).</summary>
    bool okDialog;
    /// <summary>Session 11 (cov11), rule K3b: the DS1 state opened a "DECIDE_NUMBER" dialog (OpenGenericDialog kind 3, only in
    /// the 7 covenant offering talks, always right after a YES on the same "Offer …?" text; DS1's offer state then takes
    /// exactly one): no DS2 window, the state reads as answered YES and closed.</summary>
    bool numberDialog;
    /// <summary>The prompt bit in the copy being translated, after its entry commands (IsActionPromptShown's value).</summary>
    bool promptShown;
    /// <summary>The hit bit of the copy being translated: an attack (DS1 f5) was taken and not yet reacted to.</summary>
    bool hitPending;
    /// <summary>(DS1 state, prompt bit on entry, hit bit) → DS2 state id; the copies still to translate.</summary>
    readonly Dictionary<(long, bool, bool), long> copies = [];
    readonly Queue<(long Ds1, bool On, bool Hit)> pendingCopies = new();
    /// <summary>DS1 states translated once already: a second copy reports nothing again.</summary>
    readonly HashSet<long> reported = [];
    bool quiet;
    /// <summary>The death branch's first state (when the context has a death), else -1.</summary>
    long deathHead = -1;
    /// <summary>Hit acknowledgements (session 9): target state → the state that resets DS2's hit count on the way there
    /// (entry 130351, as vanilla resets it: entry commands only; vanilla ESDs have no bank-1 pass commands at all).</summary>
    readonly Dictionary<long, long> acks = [];

    /// <summary>Unsupported DS1 constructs with use counts (report).</summary>
    public IReadOnlyDictionary<string, int> Missing => missing;
    public IReadOnlyList<string> Notes => notes;
    /// <summary>Extra copies of DS1 states (prompt on, or hit pending), for the report.</summary>
    public int PromptCopies { get; private set; }

    public TalkTranslator(ESD ds1Talk, int talkId, IContext ctx)
    {
        ds1 = ds1Talk; this.talkId = talkId; this.ctx = ctx;
        if (ds1.StateGroups.Count == 0) throw new InvalidDataException($"t{talkId}: no state group");
        // DSR talks have one machine (group 1; `ds1-talk --list`); more would be reported
        var first = ds1.StateGroups.OrderBy(g => g.Key).First();
        if (ds1.StateGroups.Count > 1) Miss($"t{talkId}: {ds1.StateGroups.Count} state groups, only group {first.Key} translated");
        src = first.Value;
        // session 11 (cov11, rule K3): the DS1 covenants whose points this talk adds: those it joins (ChangePlayerStat(11, 5,
        // n)) and those whose point stat it reads (f60(stat, N), ComparePlayerStat(stat, …)); DS1 adds to "the current
        // covenant", DS2 needs the id
        var stats = new HashSet<int>();
        foreach (var st in src.Values)
        {
            foreach (var c in st.EntryCommands.Where(c => c.CommandBank == 1 && c.CommandID == ChangePlayerStat))
                if (ConstArg(c, 0) == Covenants.StatCovenant && ConstArg(c, 2) is int n && Covenants.Of(n) is { PointStat: > 0 } r) stats.Add(r.PointStat);
            var regs = new Dictionary<int, TalkExpr>(); // the state's shared registers, in evaluation order
            foreach (var cond in Flatten(st.Conditions))
                try
                {
                    foreach (var call in Parse(cond.Evaluator, regs).Nodes().OfType<Call>())
                        if (call.Fn is CovenantPoints or ComparePlayerStat && call.Args.Length > 0 && call.Args[0] is Num { V: var s } && Covenants.ByPointStat(s) != null) stats.Add(s);
                }
                catch (InvalidDataException) { } // reported when the condition is translated
        }
        pointCovenants.AddRange(Covenants.Rows.Where(r => r.PointStat > 0 && stats.Contains(r.PointStat)));
    }

    void Miss(string what) { if (!quiet) missing[what] = missing.GetValueOrDefault(what) + 1; }
    void Note(string what) { if (!quiet) notes.Add(what); }

    static ESD.CommandCall C(int id, params TalkExpr[] args) => new(1, id, args.Select(a => a.Write()).ToArray());
    static ESD.Condition Cond(TalkExpr e, long? target) => new(target ?? -1, e.Write()) { TargetState = target };

    /// <summary>The DS2 state group. DS1 state ids are kept (the prompt-off copies; prompt-on copies get new ids); state 0
    /// becomes DS2's entry (guest check, prompt reset).</summary>
    public Dictionary<long, ESD.State> Translate()
    {
        long max = src.Keys.Max();
        long ds1Zero = max + 1, init = max + 2, end = max + 3;
        nextState = max + 4;
        // lists first: their windows are needed when conditions read GetTalkListEntryResult
        foreach (var (sid, s) in src)
            if (s.EntryCommands.Any(c => c.CommandBank == 1 && c.CommandID == AddTalkListData)) PrepareList(sid, s);
        if (ctx.Death != null) deathHead = nextState++;

        // DS1's machine from its state 0 with the prompt off (the init state deletes it) and no hit pending, one copy per
        // (DS1 state, prompt bit, hit bit) that a transition reaches
        Copy(0, false, false);
        while (pendingCopies.TryDequeue(out var copy))
        {
            if (!src.TryGetValue(copy.Ds1, out var s)) { Miss($"transition to DS1 state {copy.Ds1}, which doesn't exist"); outStates[copies[copy]] = new ESD.State(); continue; }
            quiet = !reported.Add(copy.Ds1);
            if (quiet) PromptCopies++;
            hitPending = copy.Hit;
            TranslateState(copies[copy], s, copy.Ds1, copy.On);
        }
        quiet = false;
        if (ctx.Death is { } death) EmitDeath(death);

        // DS2 entry: guests run no talk; otherwise delete the prompt and enter DS1's state 0
        // (vanilla shape, Saulden's 7410: state 0 → 1 unconditionally, the guest test in state 1)
        long guest = nextState++;
        outStates[0] = new ESD.State();
        outStates[0].Conditions.Add(Cond(N(1), guest));
        var g = new ESD.State();
        g.Conditions.Add(Cond(B(Ne, F(130602), N(0)), end));
        g.Conditions.Add(Cond(N(1), init));
        outStates[guest] = g;
        // DS1's NPC initial states (common event 50: a state block whose flags are all off gets its default state, e.g.
        // Anastacia 1140-1169 → 1140, which her talk requires before she speaks) for the blocks this talk reads
        long afterInit = ds1Zero;
        foreach (var (first, last, def) in ctx.StateBlocks(ds1Flags).Reverse())
        {
            var flags = Enumerable.Range(first, last - first + 1).Select(f => ctx.Flag(f)).OfType<int>().ToList();
            if (ctx.Flag(def) is not int d || flags.Count == 0) continue;
            long check = nextState++, set = nextState++;
            var k = new ESD.State();
            k.Conditions.Add(Cond(AndAll(flags.Select(f => B(Eq, F(6, N(f)), N(0)))), set));
            k.Conditions.Add(Cond(N(1), afterInit));
            outStates[check] = k;
            var s = new ESD.State();
            s.EntryCommands.Add(C(130101, N(d), N(1)));
            s.Conditions.Add(Cond(N(1), afterInit));
            outStates[set] = s;
            notes.Add($"NPC state block {first}-{last}: default {def} (flag {d}) set at machine start while the block is all off (DS1 common event 50)");
            afterInit = check;
        }
        var st = new ESD.State();
        st.EntryCommands.Add(C(130407));
        // vanilla '会話：開始' (talk_m10_14 g7420/7440/7760 state 1): the hit count starts at 0 with the talk machine, so a
        // count from before it ran (a hit while the machine was off, a previous run of it) can't fire a reaction
        st.EntryCommands.Add(C(Ds2DamageCountReset));
        st.Conditions.Add(Cond(N(1), afterInit));
        outStates[init] = st;
        var stEnd = new ESD.State();
        stEnd.EntryCommands.Add(C(130001));
        outStates[end] = stEnd;
        return outStates;
    }

    /// <summary>The DS2 state of a DS1 state entered with the prompt bit <paramref name="on"/> and the hit bit
    /// <paramref name="hit"/>: the plain copy (off, no hit) keeps the DS1 id (DS1 state 0 → max + 1, as DS2's state 0 is
    /// the entry), the others get new ids. Queues it for translation on first use.</summary>
    long Copy(long ds1State, bool on, bool hit)
    {
        if (copies.TryGetValue((ds1State, on, hit), out long id)) return id;
        id = on || hit ? nextState++ : ds1State == 0 ? src.Keys.Max() + 1 : ds1State;
        copies[(ds1State, on, hit)] = id;
        pendingCopies.Enqueue((ds1State, on, hit));
        return id;
    }

    /// <summary>The prompt bit after these DS1 commands (SetActionButtonText(-1) deletes it, any other text shows it).</summary>
    static bool PromptAfter(IEnumerable<ESD.CommandCall> commands, bool shown)
    {
        foreach (var c in commands)
            if (c.CommandBank == 1 && c.CommandID == SetActionButtonText) shown = ConstArg(c, 0) is not -1;
        return shown;
    }

    /// <summary>The death branch (DS1's death event as the talk machine carries it): state block off, dead flag on, the
    /// flag-gated drop rows by the gift rule, then idle (no End: a death line DS1 started keeps playing).</summary>
    void EmitDeath(DeathSpec d)
    {
        long idle = nextState++;
        outStates[idle] = new ESD.State();
        long gifts = EmitGift(d.Rows, idle);
        var head = DeathHead(d);
        head.Conditions.Add(Cond(N(1), gifts));
        outStates[deathHead] = head;
        notes.Add($"death ({d.Source}): DS1's own death reactions (transitions on f3) go through the death event's effects first, "
            + $"{deathGuards.Count} target(s); every DS1 state then tests f130301() < 1 while the dead flag is off (once) → {d.Clear.Count} state flags off, "
            + $"dead flag {d.DeadFlag} on, {d.Rows.Count} drop row(s) by the gift rule");
    }

    /// <summary>DS1's death event as entry commands. DS2's death state ('【Lib】死亡状態' state 1, talk_m10_04 EDD) first
    /// deletes the talk prompt and closes the NPC window (session 9: Petrus kept his "Talk" prompt after death in the frame
    /// simulator); it also cancels the line, left out here so a DS1 death line started just before keeps playing.</summary>
    ESD.State DeathHead(DeathSpec d)
    {
        var head = new ESD.State();
        head.EntryCommands.Add(C(130407));
        head.EntryCommands.Add(C(130462));
        foreach (int f in d.Clear) head.EntryCommands.Add(C(130101, N(f), N(0)));
        head.EntryCommands.Add(C(130101, N(d.DeadFlag), N(1)));
        return head;
    }

    /// <summary>The NPC is dead and DS1's death event hasn't run yet (its dead flag is still off).</summary>
    static TalkExpr DeadNow(DeathSpec d) => B(And, DeadTest(), B(Eq, F(6, N(d.DeadFlag)), N(0)));

    /// <summary>DS1 death reactions by target (session 9): DS1's talk sees the death (f3) and its map event sets the dead
    /// flag in the same frame, so DS1's reaction states can branch on that flag (Petrus 16: flag 1196 → "N...no...this
    /// can't be..."). A transition on f3 goes through this guard: dead and the flag still off → the event's effects (flags,
    /// drop rows by the gift rule) → the DS1 target; otherwise straight on.</summary>
    readonly Dictionary<long, long> deathGuards = [];

    long DeathGuard(long target)
    {
        if (ctx.Death is not { } d) return target;
        if (deathGuards.TryGetValue(target, out long id)) return id;
        id = nextState++;
        deathGuards[target] = id;
        long head = nextState++;
        bool q = quiet;
        quiet = true; // the gift rows are reported once (EmitDeath)
        long gifts = EmitGift(d.Rows, target, wait: false);
        quiet = q;
        var h = DeathHead(d);
        h.Conditions.Add(Cond(N(1), gifts));
        outStates[head] = h;
        var g = new ESD.State();
        g.Conditions.Add(Cond(DeadNow(d), head));
        g.Conditions.Add(Cond(N(1), target));
        outStates[id] = g;
        return id;
    }

    // ------------------------------------------------------------------ lists → NPC window
    static string? ListKind(int label) => label switch
    {
        15000000 => "talk",
        15000005 or 15000180 => "leave",
        15000010 or 15000011 => "shop",
        15000120 => "repair",
        15000110 or 15000111 or 15000112 => "fortify",
        // session 11 (cov11, rule K1b): "Enter Covenant" → the window's pledge button (result 5); "Offer <item>" / "Offer
        // Humanity" / "Offer soul of Great Lord" → its votive button (result 6); DS1's own join / offer states run after it
        15000200 => "pledge",
        >= 15000260 and <= 15000265 or 15000360 => "votive",
        _ => null,
    };

    void PrepareList(long sid, ESD.State s)
    {
        var entries = new List<(int Index, string Kind, int Flag)>();
        int shopFirst = 0, shopLast = 0, pledge = 0, votive = 0;
        foreach (var c in s.EntryCommands.Where(c => c.CommandBank == 1 && c.CommandID == AddTalkListData))
        {
            int? index = ConstArg(c, 0), label = ConstArg(c, 1), flag = ConstArg(c, 2);
            if (index == null || label == null) { Miss("AddTalkListData with non-constant arguments"); continue; }
            string? kind = ListKind(label.Value);
            if (kind == null)
            {
                // session 11 (gest11), rule GS3b: DS1's "Learn gesture" entry (its states run GiveItem(3, 90xx) + AcquireGesture(n)):
                // DS2's window has no flag-based gesture button (its gesture button gives an item: rule GS2 keeps DS1 gestures
                // out of the inventory), so the gesture is learned when the list opens while DS1 would show the entry (its
                // flag); DS1's "learned" lines are not played (listed stand-in)
                var regsG = new Dictionary<int, TalkExpr>();
                var targetsG = Flatten(s.Conditions).Where(x => ReadsListResult(x.Evaluator, index.Value, regsG)).Select(x => x.TargetState).OfType<long>().ToList();
                if (FindCommand(targetsG, 8, c2 => c2.CommandID == AcquireGesture ? ConstArg(c2, 0) : null) is int gi && Chr.Ds1Gestures.Flag(gi) is int gflag)
                {
                    if (!listGestures.TryGetValue(sid, out var lg)) listGestures[sid] = lg = [];
                    lg.Add((flag ?? -1, gflag, gi));
                    Note($"talk list entry {label} \"{ctx.EventText(label.Value)}\": DS1 gesture {9000 + gi} learned (flag {gflag}) when the list opens{(flag is > 0 ? $" while DS1 flag {flag} holds" : "")} (rule GS3b)");
                    continue;
                }
                Miss($"talk list entry {label} \"{ctx.EventText(label.Value)}\" (no DS2 window feature)"); continue;
            }
            // the states the entry leads to
            var regs = new Dictionary<int, TalkExpr>();
            var targets = Flatten(s.Conditions).Where(x => ReadsListResult(x.Evaluator, index.Value, regs)).Select(x => x.TargetState).OfType<long>().ToList();
            if (kind == "shop")
            {
                // the shop the entry leads to: the first OpenRegularShop within 4 transitions of its target
                if (FindShop(targets) is (int a, int b)) { shopFirst = a; shopLast = b; }
                else Miss("shop entry without an OpenRegularShop after it");
            }
            if (kind == "pledge")
            {
                // session 11 (cov11): the covenant DS1's join states behind the entry join (ChangePlayerStat(11, 5, n))
                int? n = FindCommand(targets, 6, c2 => c2.CommandID == ChangePlayerStat && ConstArg(c2, 0) == Covenants.StatCovenant && ConstArg(c2, 2) is > 0 ? ConstArg(c2, 2) : null);
                if (n is int nn && Covenants.Of(nn) is { } row) { if (pledge == 0) pledge = row.Ds2; }
                else { Miss($"talk list entry {label} \"Enter Covenant\": no DS1 join (ChangePlayerStat(11, 5, n)) with a covenants.tsv row behind it"); continue; }
            }
            if (kind == "votive")
            {
                // session 11 (cov11): what DS1's offer states behind the entry take (ChangeItemCount(3, item, -k); humanity
                // ChangePlayerStat(10, 1, k)) → its DS2 item (the item rules), the window's votiveItemId
                int? item = FindCommand(targets, 6, c2 =>
                    c2.CommandID == ChangeItemCount && ConstArg(c2, 2) is < 0 && ConstArg(c2, 0) is int cat && ConstArg(c2, 1) is int it ? ctx.Item(cat, it)
                    : c2.CommandID == ChangePlayerStat && ConstArg(c2, 0) == Covenants.StatHumanity && ConstArg(c2, 1) == 1 ? ctx.Item(3, Covenants.Ds1Humanity) : null);
                if (item is int iv) { if (votive == 0) votive = iv; }
                else { Miss($"talk list entry {label} \"{ctx.EventText(label.Value)}\": no DS1 offering (ChangeItemCount / humanity) with a DS2 item behind it"); continue; }
            }
            entries.Add((index.Value, kind, flag ?? -1));
        }
        var spec = new WindowSpec((int)sid, entries.Any(e => e.Kind == "talk"), shopFirst, shopLast, entries.Any(e => e.Kind == "repair"), entries.Any(e => e.Kind == "fortify"), pledge, votive);
        // session 11 (cov11): DS1 shows a covenant entry only while its flag holds (members only: F850 + n), DS2's window
        // has no per-button condition → one window row per combination of the pledge / votive buttons shown
        TalkExpr Shown(string kind) => entries.Where(e => e.Kind == kind).Select(On).DefaultIfEmpty(N(0)).Aggregate((a, b) => B(Or, a, b)).Fold();
        var variants = new List<(TalkExpr? Cond, int Window)>();
        var p = pledge > 0 ? Shown("pledge") : N(0);
        var v = votive > 0 ? Shown("votive") : N(0);
        var pOpts = p is Num pn ? [(pn.V != 0, (TalkExpr?)null)] : new[] { (true, (TalkExpr?)p), (false, (TalkExpr?)B(Eq, p, N(0))) };
        var vOpts = v is Num vn ? [(vn.V != 0, (TalkExpr?)null)] : new[] { (true, (TalkExpr?)v), (false, (TalkExpr?)B(Eq, v, N(0))) };
        bool anyWindow = spec.Any;
        foreach (var (pOn, pCond) in pOpts)
            foreach (var (vOn, vCond) in vOpts)
            {
                var vs = spec with { Pledge = pOn ? pledge : 0, Votive = vOn ? votive : 0 };
                if (!anyWindow || ctx.Window(vs) is not int w) continue;
                var conds = new[] { pCond, vCond }.OfType<TalkExpr>().ToList();
                variants.Add((conds.Count == 0 ? null : AndAll(conds), w));
            }
        if (variants.Count > 1) Note($"list state {sid}: {variants.Count} NPC window rows by DS1's entry flags (covenant buttons shown only while DS1 shows them): {string.Join(", ", variants.Select(x => x.Window))}");
        lists[sid] = (spec, variants.Count > 0 ? variants[0].Window : null, entries, variants);
    }

    /// <summary>Session 11 (cov11): the first DS1 command within <paramref name="depth"/> transitions of
    /// <paramref name="start"/> for which <paramref name="pick"/> gives a value (entry commands, breadth first).</summary>
    int? FindCommand(List<long> start, int depth, Func<ESD.CommandCall, int?> pick)
    {
        var seen = new HashSet<long>();
        var frontier = start;
        for (int d = 0; d < depth && frontier.Count > 0; d++)
        {
            var next = new List<long>();
            foreach (long t in frontier)
            {
                if (!seen.Add(t) || !src.TryGetValue(t, out var st)) continue;
                foreach (var c in st.EntryCommands.Where(c => c.CommandBank == 1))
                    if (pick(c) is int v) return v;
                next.AddRange(Flatten(st.Conditions).Select(x => x.TargetState).OfType<long>());
            }
            frontier = next;
        }
        return null;
    }

    (int, int)? FindShop(List<long> start)
    {
        var seen = new HashSet<long>();
        var frontier = start;
        for (int depth = 0; depth < 4 && frontier.Count > 0; depth++)
        {
            var next = new List<long>();
            foreach (long t in frontier)
            {
                if (!seen.Add(t) || !src.TryGetValue(t, out var st)) continue;
                var shop = st.EntryCommands.FirstOrDefault(c => c.CommandBank == 1 && c.CommandID == OpenRegularShop);
                if (shop != null && ConstArg(shop, 0) is int a && ConstArg(shop, 1) is int b) return (a, b);
                next.AddRange(Flatten(st.Conditions).Select(x => x.TargetState).OfType<long>());
            }
            frontier = next;
        }
        return null;
    }

    /// <summary>The condition tests GetTalkListEntryResult() == index (registers shared in the state's evaluation order).</summary>
    static bool ReadsListResult(byte[] evaluator, int index, Dictionary<int, TalkExpr> regs)
    {
        try
        {
            return Parse(evaluator, regs).Nodes().OfType<Bin>().Any(b => b.Op == Eq && b.L is Call { Fn: GetTalkListEntryResult } && b.R is Num n && n.V == index);
        }
        catch (InvalidDataException) { return false; }
    }

    static int? ConstArg(ESD.CommandCall c, int k)
    {
        if (k >= c.Arguments.Count) return null;
        try { return Parse(c.Arguments[k]) is Num n ? n.V : null; } catch (InvalidDataException) { return null; }
    }

    static IEnumerable<ESD.Condition> Flatten(IEnumerable<ESD.Condition> cs) => cs.SelectMany(c => new[] { c }.Concat(Flatten(c.Subconditions)));

    /// <summary>
    /// Structural check of a translated group (fix pass, review: the merchant's prompt state had become unreachable): the
    /// states reachable from state 0 over condition targets, and whether one of them creates the talk prompt (130408).
    /// Conditions that folded to a constant 0 are not emitted, so "reachable" here means some condition can lead there.
    /// </summary>
    public static (int Reachable, bool Prompt) PromptReach(IReadOnlyDictionary<long, ESD.State> group)
    {
        var seen = new HashSet<long>();
        var todo = new Stack<long>([0]);
        bool prompt = false;
        while (todo.Count > 0)
        {
            long s = todo.Pop();
            if (!seen.Add(s) || !group.TryGetValue(s, out var st)) continue;
            prompt |= st.EntryCommands.Any(c => c.CommandID == 130408);
            foreach (var c in Flatten(st.Conditions))
                if (c.TargetState is long t) todo.Push(t);
        }
        return (seen.Count, prompt);
    }

    // ------------------------------------------------------------------ states
    /// <summary>A step of a translated command list: DS2 commands, or a gift chain (needs branch states).</summary>
    abstract record Step;
    sealed record Cmds(List<ESD.CommandCall> List) : Step;
    sealed record GiftChain(IReadOnlyList<GiftRow> Rows) : Step;
    /// <summary>Wait until a window the commands before opened is on screen (DS2's routines do: '【Lib】選択ダイアログ' waits
    /// f130422 != 0 before it reads the answer, '【Lib】OKダイアログ' f130431 != 0), at most 1 s.</summary>
    sealed record WaitShown(int Function) : Step;

    void TranslateState(long id, ESD.State s, long ds1Id, bool promptOnEntry)
    {
        promptShown = PromptAfter(s.EntryCommands, promptOnEntry);
        bool promptAtExit = PromptAfter(s.ExitCommands, promptShown);
        bool isList = lists.TryGetValue(ds1Id, out var list);
        var steps = new List<Step>();
        foreach (var c in s.EntryCommands) steps.AddRange(Command(c, isList));
        // rule GS3b: the list's gesture entries teach on opening (conditional on DS1's entry flag: bank 5 = run if arg 0)
        if (listGestures.TryGetValue(ds1Id, out var lgs))
            steps.Add(new Cmds(lgs.Select(x => On((0, "gesture", x.EntryFlag)).Fold() is Num { V: not 0 } ? C(130101, N(x.GestureFlag), N(1))
                : new ESD.CommandCall(5, 130101, [On((0, "gesture", x.EntryFlag)).Write(), N(x.GestureFlag).Write(), N(1).Write()])).ToList()));
        if (isList && list.Window is int window)
        {
            if (list.Variants.Count <= 1) steps.Add(new Cmds([C(130463), C(130461, N(0), N(0), N(220), N(window))]));
            // session 11 (cov11): one of the window rows by DS1's entry flags (the conditions exclude each other)
            else steps.Add(new Cmds([C(130463), .. list.Variants.Select(v => new ESD.CommandCall(5, 130461, [v.Cond!.Write(), N(0).Write(), N(0).Write(), N(220).Write(), N(v.Window).Write()]))]));
        }
        var exit = s.ExitCommands.SelectMany(c => Command(c, isList)).OfType<Cmds>().SelectMany(x => x.List).ToList();
        if (isList && list.Window != null && !exit.Any(c => c.CommandID == 130462)) exit.Add(C(130462));
        var whileCmds = s.WhileCommands.SelectMany(c => Command(c, isList)).OfType<Cmds>().SelectMany(x => x.List).ToList();

        // gift chains split the state: [commands before] → gift branches → [commands after + DS1's conditions]
        long current = id;
        var pending = new List<ESD.CommandCall>();
        foreach (var step in steps)
        {
            if (step is Cmds cmds) { pending.AddRange(cmds.List); continue; }
            var head = new ESD.State();
            head.EntryCommands.AddRange(pending);
            pending = [];
            outStates[current] = head;
            long after = nextState++;
            if (step is WaitShown wait)
            {
                head.Conditions.Add(Cond(B(Ne, F(wait.Function), N(0)), after));
                head.Conditions.Add(Cond(B(Gt, F(3), new Real(1, false)), after));
            }
            else head.Conditions.Add(Cond(N(1), EmitGift(((GiftChain)step).Rows, after)));
            current = after;
        }
        var last = new ESD.State();
        last.EntryCommands.AddRange(pending);
        last.ExitCommands.AddRange(exit);
        last.WhileCommands.AddRange(whileCmds);
        var registers = new Dictionary<int, TalkExpr>(); // DS1's shared subexpressions, in the state's evaluation order
        okDialog = s.EntryCommands.Any(c => c.CommandBank == 1 && c.CommandID == OpenGenericDialog && ConstArg(c, 0) == 7);
        numberDialog = s.EntryCommands.Any(c => c.CommandBank == 1 && c.CommandID == OpenGenericDialog && ConstArg(c, 0) == 3);
        int deathAt = 0; // after the last condition testing f3: DS1's own death reactions (lines) keep priority
        foreach (var cond in s.Conditions)
            if (Condition(cond, ds1Id, registers, promptAtExit, out bool testsDeath) is { } kept)
            {
                last.Conditions.Add(kept);
                if (testsDeath) deathAt = last.Conditions.Count;
            }
        // the fallback death branch, once (session 9: guarded by the dead flag, so a DS1 death reaction that ran the
        // event's effects on its way (DeathGuard) keeps going instead of being cut off here)
        if (deathHead >= 0 && ctx.Death is { } death) last.Conditions.Insert(deathAt, Cond(DeadNow(death), deathHead));
        outStates[current] = last;
    }

    /// <summary>Branch states for a gift chain; returns the first state, ends in <paramref name="done"/>. Without
    /// <paramref name="wait"/> (a death drop on the way to DS1's death line, session 9) the item goes out without waiting
    /// for its window, so the line isn't held back until the player closes it.</summary>
    long EmitGift(IReadOnlyList<GiftRow> rows, long done, bool wait = true)
    {
        long next = done;
        // built back to front so each check knows where to go next
        foreach (var row in rows.Reverse())
        {
            if (row.Skip || row.Lot == null) { Note($"gift lot {row.Ds1Lot}: {row.Note}"); continue; }
            long give = nextState++, check = nextState++, shown = nextState++;
            // DS2's gift routine ('【Lib】アイテム取得ダイアログ'): flag + 131402, then wait for the item window to appear and
            // close (f130412); a window that never appears (nothing given, e.g. inventory full) ends the wait after 2 s
            var g = new ESD.State();
            if (row.Flag is int f) g.EntryCommands.Add(C(130101, N(f), N(1)));
            g.EntryCommands.Add(C(131402, N(row.Lot.Value), N(1)));
            if (wait)
            {
                g.Conditions.Add(Cond(B(Ne, F(130412), N(0)), shown));
                g.Conditions.Add(Cond(B(Gt, F(3), new Real(2, false)), next));
                var w = new ESD.State();
                w.Conditions.Add(Cond(B(Eq, F(130412), N(0)), next));
                outStates[shown] = w;
            }
            else g.Conditions.Add(Cond(N(1), next));
            outStates[give] = g;
            // first true condition wins: already given → next row; the gate (if any) → give; otherwise give / skip
            var k = new ESD.State();
            if (row.Flag is int f2) k.Conditions.Add(Cond(B(Ne, F(6, N(f2)), N(0)), next));
            if (row.Gate != null)
            {
                k.Conditions.Add(Cond(row.Gate, give));
                k.Conditions.Add(Cond(N(1), next));
            }
            else k.Conditions.Add(Cond(N(1), give));
            outStates[check] = k;
            Note($"gift lot {row.Ds1Lot} → ItemLotParam2_Other {row.Lot}{(row.Flag is int ff ? $" (given once: flag {ff})" : "")}: {row.Note}");
            next = check;
        }
        return next;
    }

    // ------------------------------------------------------------------ commands
    IEnumerable<Step> Command(ESD.CommandCall c, bool inList)
    {
        if (c.CommandBank != 1) { Miss($"command bank {c.CommandBank}:{c.CommandID}"); yield break; }
        int? A(int k) => ConstArg(c, k);
        switch (c.CommandID)
        {
            case TalkToPlayer:
            {
                if (A(0) is not int t) { Miss("TalkToPlayer with a computed talk id"); yield break; }
                if (ctx.Talk(t) is int row) yield return new Cmds([C(132001, N(row), F(131002, N(7)), N(-1))]);
                else Miss($"TalkToPlayer({t}): no TalkParam row or text");
                yield break;
            }
            case 9: // c1_9: stops the current line (interruption states)
                yield return new Cmds([C(132002)]);
                yield break;
            case SetActionButtonText: // the prompt bit of the state copy follows it (PromptAfter)
                if (A(0) == -1) yield return new Cmds([C(130407)]);
                else
                {
                    int keyGuide = A(0) is int text && ctx.KeyGuide(text) is int kg ? kg : 600;
                    if (A(0) is not int t2 || ctx.KeyGuide(t2) == null) Miss($"SetActionButtonText({A(0)}): no DS2 prompt text (KeyGuideParam 600 \"Talk\" used)");
                    yield return new Cmds([C(130408, N(9), N(keyGuide))]);
                }
                yield break;
            case SetEventFlag:
            {
                if (A(0) is not int flag || A(1) is not int value) { Miss("SetEventFlag with computed arguments"); yield break; }
                ds1Flags.Add(flag);
                // session 11 (cov11), rule K7: a kneel flag (840-849) is not written (DS2's pledge has no kneel; DS1's common
                // event 840 would play it and clear the flag, and the talk waits for that); mirror flags (K2b) are DS1
                // events' own state, never a talk's to write
                if (Covenants.IsKneelFlag(flag) || Covenants.Mirror(flag) != null)
                {
                    if (!quiet && kneelNoted.Add(flag)) Note($"flag {flag}: {(Covenants.IsKneelFlag(flag) ? "kneel flag not written, reads 0 (rule K7: no kneel, the talk goes on at once)" : "mirror flag not written (rule K2b)")}");
                    yield break;
                }
                if (ctx.Flag(flag) is int f) yield return new Cmds([C(130101, N(f), N(value))]);
                else Miss($"flag {flag} (no FlagRules prefix): not written");
                if (value == 1 && ctx.Gift(flag) is { Count: > 0 } rows) yield return new GiftChain(rows);
                yield break;
            }
            case OpenGenericDialog:
            {
                if (A(1) is not int text || ctx.Dialog(text) is not int msg) { Miss($"OpenGenericDialog text {A(1)} not in mapevent"); yield break; }
                // rule Y1 (session 9, track npcpose): DS1's 5th argument is the button the cursor starts on (1 YES, 2 NO: 238 of
                // DS1's 244 YES/NO dialogs start on NO, Oscar's question on YES). DS2's 130441 has no such argument (message,
                // close distance, dummy poly, placeholder type, two placeholder values: vanilla calls + FUN_14019ace0), and its
                // window opens on NO: a DS1 YES default stays DS2's NO (listed stand-in)
                if (A(0) == 8 && A(4) == 1) Note($"OpenGenericDialog({A(1)}): DS1 starts the cursor on YES; DS2's YES/NO window opens on NO (rule Y1, no DS2 argument)");
                if (A(0) == 8) { yield return new Cmds([C(130441, N(msg), N(0), N(-1), N(0), N(0), N(0))]); yield return new WaitShown(130422); }
                else if (A(0) == 7) { yield return new Cmds([C(130443, N(msg), N(0), N(0), N(-1), N(0), N(0), N(0))]); yield return new WaitShown(130431); }
                else if (A(0) == 3) Note($"OpenGenericDialog(3, {A(1)}): DS1's DECIDE_NUMBER step after the YES on the same text: no DS2 window, read as YES (rule K3b)");
                else Miss($"OpenGenericDialog kind {A(0)}");
                yield break;
            }
            case ForceCloseGenericDialog:
                yield return new Cmds([C(130442), C(130444)]);
                yield break;
            case AddTalkListData:
                yield break; // the list state's NPC window (PrepareList)
            case 20: // c1_20: the list's exit = close the NPC window
                yield return new Cmds(inList ? [C(130462)] : []);
                yield break;
            case OpenRegularShop:
            {
                // reached only when DS1 opens a shop outside a list (lists run their shop inside the NPC window)
                if (A(0) is int a && A(1) is int b && ctx.Window(new WindowSpec(-1, false, a, b, false, false)) is int w)
                    yield return new Cmds([C(130463), C(130461, N(0), N(0), N(220), N(w))]);
                else Miss("OpenRegularShop without a translatable range");
                yield break;
            }
            case ChangePlayerStat: // session 11 (cov11): (stat, op 0 add / 1 subtract / 5 set, value)
            {
                if (A(0) is not int stat || A(1) is not int op || A(2) is not int val) { Miss("ChangePlayerStat with computed arguments"); yield break; }
                if (stat == Covenants.StatOffered) yield break; // the covenant the join dialog names: UI only (rule K1b)
                if (stat == Covenants.StatCovenant && op == 5 && Covenants.Join(val) is { } join) // K1b: join / leave
                {
                    if (!quiet) Note($"ChangePlayerStat(11, 5, {val}): {(val == 0 ? "leave the covenant" : $"join DS1 covenant {val} ({Covenants.Of(val)!.Name}) = DS2 {Covenants.Of(val)!.Ds2} ({Covenants.Of(val)!.Ds2Name})")} (rule K1b)");
                    yield return new Cmds(join.Select(x => new ESD.CommandCall(x.Bank, x.Cmd, x.Args.Select(a => a.Write()).ToArray())).ToList());
                    yield break;
                }
                if (stat == Covenants.StatPoints && op == 0) // K3: points to the current covenant
                {
                    var cands = pointCovenants.Count > 0 ? pointCovenants : Covenants.Rows.Where(r => r.PointStat > 0).ToList();
                    if (!quiet) Note($"ChangePlayerStat(15, 0, {val}): +{val} DS2 contribution (130382) and DS1 count ({Covenants.Gate}) for {string.Join(", ", cands.Select(r => r.Name))} (rule K3)");
                    yield return new Cmds(Covenants.Points(cands, val).Select(x => new ESD.CommandCall(x.Bank, x.Cmd, x.Args.Select(a => a.Write()).ToArray())).ToList());
                    yield break;
                }
                if (stat == Covenants.StatHumanity && op == 1 && ctx.Item(3, Covenants.Ds1Humanity) is int hum) // K3: humanity offered → the item rule
                {
                    yield return new Cmds([C(Covenants.CmdConsume, N(hum), N(val))]);
                    yield break;
                }
                Miss($"command ChangePlayerStat({stat}, {op}, {val})");
                yield break;
            }
            case ChangeItemCount: // session 11 (cov11), rule K3: (category, item, delta): an offering takes it (DS2 131401 「アイテム消費」)
            {
                if (A(0) is int cat && A(1) is int item && A(2) is int d && ctx.Item(cat, item) is int ds2)
                {
                    if (d < 0) yield return new Cmds([C(Covenants.CmdConsume, N(ds2), N(-d))]);
                    else if (d > 0) yield return new GiftChain([new GiftRow(0, ctx.LotFor(ds2, d), null, false, null, $"ChangeItemCount({cat}, {item}, {d}) → item {ds2}")]);
                }
                else Miss($"ChangeItemCount({A(0)}, {A(1)}) (item not in DS2)");
                yield break;
            }
            case AcquireGesture: // session 11 (gest11), rule GS3: DS1 learns gesture n → its flag (the loader's Lordran gesture page reads it)
            {
                if (A(0) is int n && Chr.Ds1Gestures.Flag(n) is int gf)
                {
                    if (!quiet) Note($"AcquireGesture({n}): DS1 gesture {9000 + n} learned = flag {gf} (rule GS3; the Lordran gesture page shows it)");
                    yield return new Cmds([C(130101, N(gf), N(1))]);
                }
                else Miss($"AcquireGesture({A(0)}): no gesture flag (rule GS2: starting gestures have none)");
                yield break;
            }
            case GiveItem:
            {
                // rule GS3: DS1's gesture goods 9000-9014 are no DS2 items (GS2: gestures are flags); the flag is set here too,
                // since DS1 always follows the gift with AcquireGesture (same gesture), so either one learns it
                if (A(0) is int gcat && A(1) is int gitem && Chr.Ds1Gestures.IndexOfGoods(gcat, gitem) is int gi)
                {
                    if (Chr.Ds1Gestures.Flag(gi) is int gf2) yield return new Cmds([C(130101, N(gf2), N(1))]);
                    yield break;
                }
                if (A(0) is int cat && A(1) is int item && A(2) is int num && ctx.Item(cat, item) is int ds2)
                    yield return new GiftChain([new GiftRow(0, ctx.LotFor(ds2, IsEstusItem(cat, item) ? 1 : num), null, false, null, $"GiveItem({cat}, {item}, {num}) → item {ds2}{(IsEstusItem(cat, item) ? " x1 (rule G1)" : "")}")]);
                else Miss($"GiveItem({A(0)}, {A(1)}) (item not in DS2)");
                yield break;
            }
        }
        if (Dropped.TryGetValue(c.CommandID, out var why)) yield break;
        Miss($"command {Ds1Name("cmd", c.CommandID)}");
    }

    static string Ds1Name(string kind, int id) =>
        Commands.Ds1Talk.Names.TryGetValue((kind, id), out var n) ? $"{n.Text} ({kind} {id})" : $"{(kind == "cmd" ? "c1_" : "f")}{id}";

    // ------------------------------------------------------------------ conditions
    /// <summary>A DS1 condition rewritten; null when it can never pass (folded to 0, no subconditions: DS2's machine
    /// would only test a constant). <paramref name="promptAtExit"/> = the prompt bit when the state is left (its target
    /// copy follows the bit after the pass commands); <paramref name="testsDeath"/> = the DS1 condition reads f3.</summary>
    ESD.Condition? Condition(ESD.Condition c, long ds1State, Dictionary<int, TalkExpr> registers, bool promptAtExit, out bool testsDeath, bool ackFromParent = false)
    {
        TalkExpr e;
        testsDeath = false;
        bool testsAttack = false, testsHit = false;
        try
        {
            var parsed = Parse(c.Evaluator, registers);
            testsDeath = parsed.Nodes().Any(n => n is Call { Fn: IsDead });
            testsAttack = parsed.Nodes().Any(n => n is Call { Fn: IsAttacked });
            testsHit = parsed.Nodes().Any(n => n is Call { Fn: IsHitByPlayer });
            e = Rewrite(parsed, ds1State).Fold();
        }
        catch (InvalidDataException ex) { Miss($"condition doesn't parse ({ex.Message})"); e = N(0); }
        if (e is Num { V: 0 } && c.Subconditions.Count == 0) return null;
        bool promptAtTarget = PromptAfter(c.PassCommands, promptAtExit);
        // the hit bit: set by a transition on DS1's attack test (f5: DS1 then stops the line and picks a hit reaction with
        // f9 in the next state(s), e.g. the Crestfallen Warrior 6 →f5→ 1 → 7 →f9→ "Ooph!"), kept through unconditional
        // hops, consumed by the first conditional transition after it
        bool hitAtTarget = testsAttack || (e is Num { V: not 0 } && c.Subconditions.Count == 0 && hitPending);
        long? target = c.TargetState is long t ? Copy(t, promptAtTarget, hitAtTarget) : null;
        // a DS1 death reaction: DS1's death event runs on the way (DeathGuard); not on transitions that need the NPC alive
        if (testsDeath && CanFireDead(e) && target is long dt) target = DeathGuard(dt);
        // DS2's count stays until reset: an attack transition moves it into the hit bit, a reaction consumes it. The reset
        // runs on the way to the target, in the entry of an acknowledgement state (session 9: vanilla resets 130351 only
        // in entry commands and has no bank-1 pass commands; the first version used a pass command)
        bool ack = testsAttack || testsHit || ackFromParent;
        if (ack && target is long tt) target = Ack(tt);
        var outC = Cond(e, target);
        foreach (var p in c.PassCommands)
            foreach (var step in Command(p, false))
                if (step is Cmds cmds) outC.PassCommands.AddRange(cmds.List);
                else Miss("gift inside a pass command");
        foreach (var sub in c.Subconditions)
            if (Condition(sub, ds1State, registers, promptAtTarget, out bool subDeath, ack) is { } kept)
            {
                outC.Subconditions.Add(kept);
                testsDeath |= subDeath;
            }
        return outC;
    }

    /// <summary>Whether a translated condition can hold while the NPC is dead: the dead test as 1 and its opposite (vanilla's
    /// alive test, HP &gt;= 1) as 0, folded; 0 = it needs the NPC alive (Talk prompts, "talk possible" tests).</summary>
    static bool CanFireDead(TalkExpr e) =>
        e.Map(x => x switch
        {
            Bin { Op: Lt, L: Call { Fn: Ds2Hp }, R: Num { V: 1 } } => N(1),
            Bin { Op: Ge, L: Call { Fn: Ds2Hp }, R: Num { V: 1 } } => N(0),
            _ => x,
        }).Fold() is not Num { V: 0 };

    /// <summary>The acknowledgement state in front of <paramref name="target"/>: resets the hit count (130351) and goes
    /// on at once. One per target; an extra transition in the same frame (DS2 runs up to 100 per frame).</summary>
    long Ack(long target)
    {
        if (acks.TryGetValue(target, out long id)) return id;
        id = nextState++;
        var s = new ESD.State();
        s.EntryCommands.Add(C(Ds2DamageCountReset));
        s.Conditions.Add(Cond(N(1), target));
        outStates[id] = s;
        return acks[target] = id;
    }

    /// <summary>DS2 functions that return 0/1: a DS1 "== 1" on them becomes "!= 0" (DS2's own scripts test them so).</summary>
    static readonly HashSet<int> Boolean = [6, 130412, 130422, 130431, 130451, 130453, 130602, 132002, 132003];

    TalkExpr Rewrite(TalkExpr e, long ds1State) => e switch
    {
        // comparisons whose meaning depends on the compared value
        Bin { Op: Eq or Ne } b when b.L is Call { Fn: GetTalkListEntryResult } && b.R is Num n => ListResult(n.V, ds1State, b.Op == Ne),
        // a notice (OK window) has no answer in DS2 (its routine only waits for it to close): DS1's "result 0 or 1 and
        // closed" tests reduce to "closed"
        Bin b when IsComparison(b.Op) && b.L is Call { Fn: GetGenericDialogButtonResult } && okDialog => N(1),
        // session 11 (cov11), rule K3b: the number step was answered YES (1) and is closed
        Bin b when IsComparison(b.Op) && b.L is Call { Fn: GetGenericDialogButtonResult } && numberDialog && b.R is Num nd => N(Cmp(b.Op, 1, nd.V) ? 1 : 0),
        Call { Fn: IsGenericDialogOpen } when numberDialog => N(0),
        Bin b when IsComparison(b.Op) && b.L is Call { Fn: GetGenericDialogButtonResult } && b.R is Num n =>
            B(b.Op, F(130421), N(n.V == 0 ? 3 : n.V)),
        Bin b when IsComparison(b.Op) && b.L is Call { Fn: ComparePlayerStat } cs && b.R is Num n => Booleanize(B(b.Op, PlayerStat(cs), n)),
        Bin b when IsComparison(b.Op) => Booleanize(B(b.Op, Rewrite(b.L, ds1State), Rewrite(b.R, ds1State))),
        Bin b => B(b.Op, Rewrite(b.L, ds1State), Rewrite(b.R, ds1State)),
        Neg n => new Neg(Rewrite(n.X, ds1State)),
        Call c => Function(c),
        Raw r => Unparsed(r),
        _ => e,
    };

    TalkExpr Unparsed(Raw r) { Miss("expression with registers/opaque bytecode"); return N(0); }

    static TalkExpr Booleanize(TalkExpr e)
    {
        if (e is Bin { Op: Eq or Ne } b && b.L is Call { Fn: var fn } && Boolean.Contains(fn) && b.R is Num { V: 1 })
            return B(b.Op == Eq ? Ne : Eq, b.L, N(0));
        // a DS1 function that became a DS2 comparison (f3 → f130301() < 1, f9 → f130311(1) > 0): "== 1" is the comparison,
        // "== 0" its opposite
        if (e is Bin { Op: Eq or Ne } c && c.L is Bin { Op: var op } inner && IsComparison(op) && c.R is Num { V: 0 or 1 } v)
            return (c.Op == Eq) == (v.V == 1) ? inner : B(Opposite(op), inner.L, inner.R);
        // session 11 (cov11): a DS1 test that became a conjunction / disjunction of DS2 comparisons (covenant membership,
        // rule K2): "== 1" / "!= 0" is the expression itself
        if (e is Bin { Op: Eq or Ne } d && d.L is Bin { Op: And or Or } j && d.R is Num { V: 0 or 1 } w && (d.Op == Eq) == (w.V == 1))
            return j;
        return e;
    }

    static bool Cmp(byte op, int a, int b) => op switch { Lt => a < b, Gt => a > b, Le => a <= b, Ge => a >= b, Eq => a == b, Ne => a != b, _ => false };

    static byte Opposite(byte op) => op switch { Le => Gt, Gt => Le, Lt => Ge, Ge => Lt, Eq => Ne, _ => Eq };

    TalkExpr Function(Call c)
    {
        int? A(int k) => k < c.Args.Length && c.Args[k] is Num n ? n.V : null;
        switch (c.Fn)
        {
            case GetDistanceToPlayer: return ctx.DistanceOffset > 0 ? B(Sub, F(130201), new Real(ctx.DistanceOffset, false)) : F(130201);
            case HasTalkEnded: return F(132002);
            case IsActionButtonPressed: return F(132003);
            case GetAngleToPlayer: return N(0);
            case IsActionPromptShown: return N(promptShown ? 1 : 0); // known per state copy (Translate)
            case IsDead: return DeadTest();
            case IsAttacked: return HitTest();
            case IsHitByPlayer: return hitPending ? N(1) : HitTest(); // the attack that led here, or a hit since
            case GetHpPercent: return F(Ds2HpPercent);
            case GetEventFlag:
                if (A(0) is int read) ds1Flags.Add(read);
                // session 11 (cov11): kneel flags (K7) and mirror flags (K2b) by their covenant rules
                if (A(0) is int cov && (Covenants.IsKneelFlag(cov) || Covenants.Mirror(cov) != null)) return FlagTest(cov);
                if (A(0) is int flag && ctx.Flag(flag) is int f) return F(6, N(f));
                Miss($"flag {A(0)} read (no FlagRules prefix): 0");
                return N(0);
            case HasItem:
                // rule G1: a DS1 Estus gift gives a shard, but "has an Estus Flask" stays DS2's own flask
                if (A(0) is int ce && A(1) is int ie && IsEstusItem(ce, ie)) return B(Ne, F(131401, N(Chr.Ds1Drops.Ds2EstusFlask), N(1), N(1), N(0)), N(0));
                if (A(0) is int cat && A(1) is int item && ctx.Item(cat, item) is int ds2) return B(Ne, F(131401, N(ds2), N(1), N(1), N(0)), N(0));
                Miss($"HasItem({A(0)}, {A(1)}): item not in DS2, 0");
                return N(0);
            case IsGenericDialogOpen: return B(Or, F(130422), F(130431));
            case IsMenuOpen:
                if (A(0) == 63) return F(130412);
                if (A(0) is not 11) Miss($"IsMenuOpen({A(0)}) → the NPC window");
                return F(130451);
            case BoughtInMenu:
                if (A(0) == 11) return F(130453);
                Miss($"f28({A(0)}) (used menu {A(0)}): 0");
                return N(0);
            case GetTalkListEntryResult: Miss("GetTalkListEntryResult outside an == test"); return N(-1);
            case GetGenericDialogButtonResult: return F(130421);
            // session 11 (cov11): covenant functions (rules K1b / K4)
            case PhantomPresent: return Covenants.PhantomPresent; // f52: a phantom is in the world (join refused)
            case PlayerStatValue when A(0) == Covenants.StatHumanity && ctx.Item(3, Covenants.Ds1Humanity) is int hum: // f29(10): DS1 humanity → the item rule's Human Effigy count
                return F(131401, N(hum), N(1), N(1), N(0));
            // session 11 (burg11, rule TK1-f29, research\f29.md): 6 = Faith (PlayerGameData +0x70, V) → DS2 f130304(6, 1)
            // 「プレイヤーステータス取得」 (effective value; internal index = Faith, P); 13 = CoopPlaySuccessCount (+0xd8, V) → 0:
            // DS2's per-phantom-type count f130642(type) has an unconfirmed type (U) and offline DS1's count is 0 too. The
            // Sunlight Altar / Solaire test f29(6) + 5·f29(13) >= 25 so becomes Faith >= 25.
            case PlayerStatValue when A(0) == Ds1StatFaith:
                return F(130304, N(6), N(1));
            case PlayerStatValue when A(0) == Ds1StatCoopSuccess:
                Miss("f29(13) co-op successes → 0 (offline value; f130642(type) once the phantom type is confirmed)");
                return N(0);
            case CovenantPoints: // f60(point stat, N): the covenant's points reached N (rank-up / reward tests at 10 / 30 / 80 / 100)
                if (A(0) is int stat && A(1) is int pts && Covenants.PointCompare(stat, Eq, pts) is { } pc) return pc;
                Miss($"f60({A(0)}, {A(1)}): no covenants.tsv point stat: 0");
                return N(0);
        }
        Miss($"function {Ds1Name("fn", c.Fn)} → {UnknownValue(c.Fn)}");
        return N(UnknownValue(c.Fn));
    }

    TalkExpr PlayerStat(Call cs)
    {
        int? A(int k) => k < cs.Args.Length && cs.Args[k] is Num n ? n.V : null;
        // ComparePlayerStat(12 = sex, 0 = equals, v) == 1: DS1 v 1 male / 0 female (the female merchant's "lad"/"lass" lines)
        if (A(0) == 12 && A(1) == 0 && A(2) is int v) return B(Eq, F(130305), N(v == 1 ? 0 : 1));
        // session 11 (cov11): covenant stats; DS1 comparison codes 0 == 1 != 2 > 3 < 4 >= 5 <= (EMEDF ComparisonType)
        if (A(1) is int op && Covenants.EsdOp(op) is byte esdOp && A(2) is int val)
        {
            TalkExpr? r = A(0) switch
            {
                Covenants.StatCovenant => Covenants.CovenantCompare(op, val),          // K2: in DS1 covenant(s)
                Covenants.StatKillRank => Covenants.KillRank(esdOp, val),              // K4: Forest Hunter kill rank → DS2 level
                // DS1 humanity (offered at Kaathe / the Fair Lady) → the item rule's Human Effigy count
                Covenants.StatHumanity => ctx.Item(3, Covenants.Ds1Humanity) is int hum ? B(esdOp, F(131401, N(hum), N(1), N(1), N(0)), N(val)) : null,
                int s when Covenants.ByPointStat(s) != null => Covenants.PointCompare(s, esdOp, val), // K4: points (100 = "cannot offer more")
                _ => null,
            };
            if (r != null) return r;
        }
        Miss($"ComparePlayerStat({string.Join(", ", cs.Args.Select(a => a.ToString()))}): 0");
        return N(0);
    }

    /// <summary>GetTalkListEntryResult() == k inside a list state → the NPC window's result for entry k.</summary>
    TalkExpr ListResult(int index, long ds1State, bool negate)
    {
        if (!lists.TryGetValue(ds1State, out var list)) list = lists.Values.FirstOrDefault();
        TalkExpr r;
        if (list.Entries == null || list.Window == null) r = N(0);
        else if (index == 0) r = B(Eq, F(130452), N(19));
        else
        {
            var entry = list.Entries.FirstOrDefault(x => x.Index == index);
            r = entry.Kind switch
            {
                "talk" => KindEntry(list.Entries, entry, 16),
                "leave" => B(Or, B(Eq, F(130452), N(17)), B(Eq, F(130452), N(18))),
                // session 11 (cov11, rule K1b): the covenant buttons
                "pledge" => KindEntry(list.Entries, entry, WindowPledge),
                "votive" => KindEntry(list.Entries, entry, WindowVotive),
                _ => N(0), // shop/repair/fortify run inside the window; unsupported entries are not offered
            };
        }
        return negate ? B(Eq, r, N(0)) : r;
    }

    /// <summary>Result <paramref name="code"/> (16 Talk, 5 pledge, 6 votive) goes to the first DS1 entry of that kind whose
    /// flag is on (DS1 shows an entry only while its flag is on; DS2's window has one button of each kind: the Sunlight
    /// Altar's "Offer soul of Great Lord" while flag 715 holds, else "Offer Sunlight Medal").</summary>
    TalkExpr KindEntry(List<(int Index, string Kind, int Flag)> entries, (int Index, string Kind, int Flag) entry, int code)
    {
        var same = entries.Where(x => x.Kind == entry.Kind).ToList();
        var terms = new List<TalkExpr> { B(Eq, F(130452), N(code)), On(entry) };
        foreach (var earlier in same.TakeWhile(x => x.Index != entry.Index)) terms.Add(B(Eq, On(earlier), N(0)));
        return AndAll(terms).Fold();
    }

    /// <summary>A DS1 list entry is shown: no flag, or its flag holds (<see cref="FlagTest"/>).</summary>
    TalkExpr On((int Index, string Kind, int Flag) x) => x.Flag <= 0 ? N(1) : FlagTest(x.Flag);

    /// <summary>
    /// A DS1 flag read as a truth value (DS2 expression). FlagRules for ordinary flags; session 11 (cov11): a mirror flag
    /// (rule K2b: common event 870's F850 + n "in covenant n", event 715 "may offer the Soul of Gwyn") is its DS1 condition,
    /// a kneel flag (rule K7) reads 0. A flag without a rule reads 0 (reported).
    /// </summary>
    TalkExpr FlagTest(int ds1Flag)
    {
        ds1Flags.Add(ds1Flag);
        if (Covenants.IsKneelFlag(ds1Flag)) return N(0);
        if (Covenants.Mirror(ds1Flag) is { } conds)
        {
            var terms = new List<TalkExpr>();
            foreach (var c in conds)
            {
                TalkExpr? t = c.Kind switch
                {
                    "covenant" => Covenants.Member(c.A),
                    "flag" => ctx.Flag(c.A) is int f ? B(c.On ? Ne : Eq, F(6, N(f)), N(0)) : null,
                    "item" => ItemCategory(c.A) is int cat && ctx.Item(cat, c.B) is int item ? B(c.On ? Ge : Lt, F(131401, N(item), N(1), N(1), N(0)), N(1)) : null,
                    _ => null,
                };
                if (t == null) { Miss($"flag {ds1Flag} (DS1 mirror event condition {c.Kind} {c.A} {c.B}: no rule): 0"); return N(0); }
                terms.Add(t);
            }
            if (mirrorNoted.Add(ds1Flag)) Note($"flag {ds1Flag}: DS1 mirror event's condition read in place (rule K2b): {AndAll(terms)}");
            return AndAll(terms);
        }
        if (ctx.Flag(ds1Flag) is int g) return B(Ne, F(6, N(g)), N(0));
        Miss($"flag {ds1Flag} read (no FlagRules prefix): 0");
        return N(0);
    }

    /// <summary>EMEDF ItemType (0 weapon, 1 armor, 2 ring, 3 goods) = the talk item categories.</summary>
    static int? ItemCategory(int emedfType) => emedfType is >= 0 and <= 3 ? emedfType : null;
}
