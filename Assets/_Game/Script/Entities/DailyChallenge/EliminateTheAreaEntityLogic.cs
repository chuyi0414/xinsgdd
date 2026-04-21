using DG.Tweening;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
// ⚠️ 避坑：System 已由 UnityEngine 覆盖，此处不额外 using System，避免歧义。

/// <summary>
/// 消除区域实体逻辑。
/// 主线纯消消乐模式下的等待区管理核心：
/// 1. 接收世界坐标定位区域底图；
/// 2. 从 prefab 的 Locations 子物体缓存 8 个槽位；
/// 3. 管理等待区卡片的插入、布局刷新、满格结算清空；
/// 4. 操作队列串行化，保证插入/移除/清空不并发。
/// </summary>
public sealed class EliminateTheAreaEntityLogic : EntityLogic
{
    // ───────────── 常量 ─────────────

    /// <summary>
    /// 等待区最大槽位数。
    /// 与 prefab 的 Locations 子物体数量一致。
    /// </summary>
    private const int MaxSlotCount = 8;

    /// <summary>
    /// 卡片插入移动动画时长（秒）。
    /// 从棋盘位置飞入等待区槽位的 DOMove 耗时。
    /// </summary>
    private const float InsertMoveDuration = 0.2f;

    /// <summary>
    /// 卡片位移动画时长（秒）。
    /// 插入后已有卡片让位移动的 DOMove 耗时。
    /// </summary>
    private const float ShiftMoveDuration = 0.1f;

    /// <summary>
    /// 满格结算清空缩小动画时长（秒）。
    /// 所有卡片同时 DOScale(Vector3.zero) 的耗时。
    /// </summary>
    private const float ClearScaleDuration = 0.3f;

    /// <summary>
    /// 满格结算清空前延迟（秒）。
    /// 给玩家一个"满格了"的视觉缓冲。
    /// </summary>
    private const float ClearDelaySeconds = 0.15f;

    /// <summary>
    /// 前移补位动画时长（秒）。
    /// 清空后剩余卡片前移到空槽位的 DOMove 耗时。
    /// </summary>
    private const float CompactMoveDuration = 0.25f;

    // ───────────── 等待区槽位 ─────────────

    /// <summary>
    /// 等待区槽位 Transform 数组。
    /// 从 prefab 的 Locations 子物体按顺序缓存，长度固定为 MaxSlotCount。
    /// </summary>
    private Transform[] _waitingAreaSlots;

    // ───────────── 分数槽位渲染器 ─────────────

    /// <summary>
    /// ScoreLocations 下每个槽位的分数字图片渲染器数组。
    /// 与 _waitingAreaSlots 一一对应，用于显示每个槽位卡牌的分数。
    /// </summary>
    private WaitingAreaScoreSpriteRenderer[] _scoreSlotRenderers;

    // ───────────── 消除动画组件 ─────────────

    /// <summary>
    /// Eliminate 子物体下的消除动画组件数组。
    /// Prefab 层级：EliminateTheAreaEntity → Eliminate → EliminateAnimation[0~7]。
    /// 每个 EliminateAnimation 挂载 Legacy Animation 组件，
    /// clip 为帧动画换 Sprite（~0.85s）。满格结算卡片缩小完毕后从首帧播放。
    /// </summary>
    private Animator[] _eliminateAnimations;

    /// <summary>
    /// Eliminate 子物体下的 SpriteRenderer 组件数组。
    /// 与 _eliminateAnimations 一一对应，用于通过 enabled 控制显隐，
    /// 避免 SetActive(true) 与 Legacy Animation 同帧 Play 时只显示首帧不推进的问题。
    /// </summary>
    private SpriteRenderer[] _eliminateAnimationRenderers;

    /// <summary>
    /// 消除动画播放中的 Sequence 引用，用于 OnHide 时终止。
    /// </summary>
    private Sequence _eliminateAnimSequence;

    private float _eliminateAnimationDuration;

    private static readonly int EliminateAnimationStateHash = Animator.StringToHash("Base Layer.EliminateAnimation");

    // ───────────── Combo UI 组件 ─────────────

    /// <summary>
    /// Combo 根节点的 RectTransform。
    /// 需要在 EliminateTheAreaEntity 预制体上手动拖入 `Combo` 节点。
    /// 用于控制整块 Combo UI 的显隐与缩放动画。
    /// 初始状态：由 Inspector 赋值；运行时不主动置空，便于对象池复用。
    /// </summary>
    [SerializeField] private RectTransform _comboRootRect;

    /// <summary>
    /// Combo 数值文本组件。
    /// 运行时从手动拖入的 `Combo` 根节点下自动解析。
    /// 用于显示当前连击倍数，例如 `x2`、`x3`。
    /// 初始状态：null；在 ResolveComboDisplay 中缓存，隐藏时会被清空文本。
    /// </summary>
    private TextMeshProUGUI _comboTextTMP;

    /// <summary>
    /// Combo 倒计时滑条组件。
    /// 运行时从手动拖入的 `Combo` 根节点下自动解析。
    /// 用于可视化当前连击时间窗的剩余进度。
    /// 初始状态：null；在 ResolveComboDisplay 中缓存，隐藏时会被重置为 0。
    /// </summary>
    private Slider _comboSlider;

    /// <summary>
    /// Combo 根节点上的 CanvasGroup 缓存。
    /// 若预制体上未提前挂载，则运行时自动补挂到 `_comboRootRect` 所在对象。
    /// 用于执行淡入淡出动画。
    /// 初始状态：null；在引用校验完成后缓存。
    /// </summary>
    private CanvasGroup _comboCanvasGroup;

    /// <summary>
    /// Combo 显示动画 Sequence 句柄。
    /// 负责管理续击强调与超时淡出动画，OnHide 时必须统一终止。
    /// 初始状态：null。
    /// </summary>
    private Sequence _comboDisplaySequence;

    /// <summary>
    /// 当前 Combo 时间窗总时长（秒）。
    /// 由 EliminateCardController 在刷新 UI 时传入，用于将 Slider 归一化到 0~1。
    /// 初始状态：0，表示当前没有有效的 Combo 倒计时。
    /// </summary>
    private float _comboWindowSeconds;

    /// <summary>
    /// 当前 Combo 的过期实时时刻（Time.unscaledTime）。
    /// 当当前时间超过这个值时，Combo UI 会自动隐藏。
    /// 初始状态：-1，表示当前无激活中的 Combo。
    /// </summary>
    private float _comboExpireRealTime = -1f;

    /// <summary>
    /// Combo 在屏幕内的目标锚点位置。
    /// 每次 OnShow 时从当前预制体布局缓存，用于确保 Combo 每次出现都回到正确可见位置。
    /// 初始状态：Vector2.zero。
    /// </summary>
    private Vector2 _comboShownAnchoredPosition;

    /// <summary>
    /// Combo 进场位移动画的起始偏移量。
    /// 让 Combo 从右侧轻微滑入，避免只缩放不位移导致仍停留在屏幕外的旧位置。
    /// </summary>
    private static readonly Vector2 ComboIntroOffset = new Vector2(2.5f, 0f);

    // ───────────── 轮数/基础分信息文本 ─────────────

    /// <summary>
    /// Canvas 下的轮数/基础分信息文本（TMP）。
    /// 在 Inspector 中拖拽赋值。
    /// </summary>
    [SerializeField] private TMPro.TextMeshProUGUI _roundInfoTextTMP;

    // ───────────── 等待区数据 ─────────────

    /// <summary>
    /// 等待区逻辑顺序列表。
    /// 唯一真相源：任何插入/删除/清空都只修改这个列表，再统一刷新布局。
    /// </summary>
    private readonly List<EliminateCardEntityLogic> _waitingOrder = new List<EliminateCardEntityLogic>(MaxSlotCount);

    /// <summary>
    /// 双端快照视觉数组。
    /// 长度 = _maxCardCount，由 BuildDualEndSnapshot 从 _waitingOrder 重建。
    /// 组合（同类型≥2）从左端填入，单牌（同类型=1）从右端填入，中间允许空位。
    /// 所有动画目标位置必须基于此数组，而非 _waitingOrder 的顺序索引。
    /// </summary>
    private EliminateCardEntityLogic[] _snapshotCards;

    /// <summary>
    /// 当前等待区中的卡片数量。
    /// </summary>
    private int _currentCardCount;

    /// <summary>
    /// 等待区最大容量。
    /// 初始化时从 _waitingAreaSlots.Length 取值，预期为 MaxSlotCount。
    /// </summary>
    private int _maxCardCount;

    // ───────────── 双端快照排序追踪 ─────────────

    /// <summary>
    /// 卡片入槽序号种子。
    /// 每次卡片首次进入等待区时递增并分配，用于双端布局稳定排序。
    /// 同一张卡片在同一局内只分配一次序号。
    /// </summary>
    private int _insertSerialSeed;

    /// <summary>
    /// 卡片实体 Id → 入槽序号 的映射表。
    /// 用于双端快照排序时保证组内卡片按入槽先后稳定排列。
    /// </summary>
    private readonly Dictionary<int, int> _cardInsertSerialByEntityId = new Dictionary<int, int>(MaxSlotCount);

    /// <summary>
    /// 组合成组序号种子。
    /// 当某类型首次达到 2 张时，固化一个成组序号，保证"先成组先靠左"。
    /// </summary>
    private int _groupOrderSeed;

    /// <summary>
    /// 类型 Id → 成组序号 的映射表。
    /// 成组序号本质是时间戳，排序时保证"谁先成双谁更靠左"。
    /// </summary>
    private readonly Dictionary<int, int> _groupOrderByTypeId = new Dictionary<int, int>(MaxSlotCount);

    // ───────────── 操作队列 ─────────────

    /// <summary>
    /// 等待区操作类型。
    /// Insert=插入卡片；RemoveBatch=批量移除（满格结算清空后）；Compact=前移补位。
    /// </summary>
    private enum WaitingAreaOpType
    {
        Insert = 0,
        RemoveBatch = 1,
        Compact = 2,
    }

    /// <summary>
    /// 等待区操作描述。
    /// </summary>
    private readonly struct WaitingAreaOp
    {
        /// <summary>
        /// 操作类型。
        /// </summary>
        public readonly WaitingAreaOpType OpType;

        /// <summary>
        /// 操作涉及的单卡引用。
        /// Insert 操作使用此字段，避免为单卡 new List 分配 GC。
        /// </summary>
        public readonly EliminateCardEntityLogic Card;

        /// <summary>
        /// 操作涉及的批量卡片（RemoveBatch 时使用）。
        /// </summary>
        public readonly List<EliminateCardEntityLogic> Cards;

        /// <summary>
        /// 移动动画时长（秒）。
        /// </summary>
        public readonly float MoveDuration;

        /// <summary>
        /// 单卡 Insert 操作构造。
        /// Zero-GC：直接持有 Card 引用，不包装 List。
        /// </summary>
        public WaitingAreaOp(WaitingAreaOpType opType, EliminateCardEntityLogic card, float moveDuration)
        {
            OpType = opType;
            Card = card;
            Cards = null;
            MoveDuration = moveDuration;
        }

        /// <summary>
        /// 批量 RemoveBatch 操作构造。
        /// </summary>
        public WaitingAreaOp(WaitingAreaOpType opType, List<EliminateCardEntityLogic> cards, float moveDuration)
        {
            OpType = opType;
            Card = null;
            Cards = cards;
            MoveDuration = moveDuration;
        }
    }

    /// <summary>
    /// 等待区操作队列。
    /// 串行化处理，避免快速点击导致并发写入。
    /// </summary>
    private readonly Queue<WaitingAreaOp> _waitingAreaOpQueue = new Queue<WaitingAreaOp>();

    /// <summary>
    /// 等待区操作执行中标记。
    /// 保证单次只处理一个操作。
    /// </summary>
    private bool _isWaitingAreaOpRunning;

    // ───────────── 满格结算 ─────────────

    /// <summary>
    /// 满格结算标记。
    /// 当等待区满格时置 true，触发结算清空流程。
    /// </summary>
    private bool _settlementDirty;

    /// <summary>
    /// 满格结算完成后的回调。
    /// 由 EliminateCardController 注入，用于通知控制器更新遮挡状态、
    /// 检查是否需要自动入槽等。
    /// </summary>
    public System.Action OnSettlementCleared { get; set; }

    /// <summary>
    /// 满格清空得分回调：在清空动画开始前触发，此时等待区卡片仍完整存在。
    /// 由 EliminateCardController 注入，用于在卡片被回收前计算得分和连击。
    /// </summary>
    public System.Action OnSettlementScoreCalculation { get; set; }

    /// <summary>
    /// 满格失败回调：等待区满格时有 2 张以上单牌（出现次数恰好为 1 的类型 >= 2）。
    /// 由 EliminateCardController 注入，用于通知控制器弹出失败 UI。
    /// </summary>
    public System.Action OnSettlementFailed { get; set; }

    /// <summary>
    /// 等待区布局变更回调：双端快照重建后触发。
    /// 由 EliminateCardController 注入，用于在快照就绪后刷新每个槽位的分数显示。
    /// ⚠️ 避坑：不可在 TryRequestInsert 时直接刷新，因为此时操作尚未出队执行，快照仍为旧值。
    /// </summary>
    public System.Action OnWaitingAreaLayoutChanged { get; set; }

    // ───────────── EntityLogic 生命周期 ─────────────

    /// <summary>
    /// 实体显示时：定位 + 缓存 Locations 槽位。
    /// </summary>
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        ApplyData(userData as EliminateTheAreaEntityData);
        ResolveWaitingAreaSlots();
        _waitingOrder.Clear();
        _currentCardCount = 0;
        _isWaitingAreaOpRunning = false;
        _settlementDirty = false;
        _waitingAreaOpQueue.Clear();

        // 重置双端快照排序追踪状态，避免跨局残留
        ResetDualEndLayoutState();

        // 初始化快照数组
        if (_maxCardCount > 0)
        {
            _snapshotCards = new EliminateCardEntityLogic[_maxCardCount];
        }

        // 校验 Combo 组件引用并初始化显示状态。
        // ⚠️ 这里不再通过路径查找，而是完全依赖预制体 Inspector 手动拖拽。
        ResolveComboDisplay();
        HideComboDisplay(true);

        // 自动注册区域实体逻辑引用与结算回调（OnSettlementCleared / OnSettlementFailed 均由 RegisterAreaEntityLogic 内部注入）
        EliminateCardController.Instance?.RegisterAreaEntityLogic(this);

        // 解析 ScoreLocations 下的分数渲染器
        ResolveScoreSlotRenderers();

        // 解析 Eliminate 下的消除动画组件
        ResolveEliminateAnimations();

        // 重置分数显示，避免跨局残留旧值
        ClearScoreDisplay();

        // ── 确保 Canvas 排序层级高于 SpriteRenderer（卡片基准 200+），否则文字被遮挡 ──
        SetupCanvasSortingOrder();

        // 重置轮数/基础分信息显示
        RefreshRoundInfoDisplay(1, 1);
    }

    /// <summary>
    /// 实体隐藏时清理所有状态。
    /// </summary>
    protected override void OnHide(bool isShutdown, object userData)
    {
        // 终止所有残留 Tween
        for (int i = 0; i < _waitingOrder.Count; i++)
        {
            if (_waitingOrder[i] != null)
            {
                _waitingOrder[i].CachedTransform.DOKill(false);
            }
        }

        _waitingOrder.Clear();
        _waitingAreaOpQueue.Clear();
        _currentCardCount = 0;
        _isWaitingAreaOpRunning = false;
        _settlementDirty = false;
        OnSettlementCleared = null;
        OnSettlementScoreCalculation = null;
        OnWaitingAreaLayoutChanged = null;

        // 清理双端快照排序追踪状态
        ResetDualEndLayoutState();
        _snapshotCards = null;

        // 清理分数槽位渲染器引用
        _scoreSlotRenderers = null;

        // 停止并清理消除动画
        StopEliminateAnimations();
        _eliminateAnimations = null;
        _eliminateAnimationRenderers = null;
        _eliminateAnimationDuration = 0f;

        // 只保留手动拖拽的 Combo 根节点引用。
        // 文本、Slider、CanvasGroup 都属于运行时缓存，这里清掉以避免对象池复用时拿到旧引用。
        HideComboDisplay(true);
        _comboCanvasGroup = null;
        _comboTextTMP = null;
        _comboSlider = null;

        // 自动反注册区域实体逻辑引用
        EliminateCardController.Instance?.UnregisterAreaEntityLogic();

        base.OnHide(isShutdown, userData);
    }

    /// <summary>
    /// 每帧检测是否需要触发满格结算。
    /// </summary>
    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);

        UpdateComboDisplayCountdown();

        // 尝试处理操作队列
        TryProcessWaitingAreaQueue();

        // 满格结算检测
        if (_settlementDirty && !_isWaitingAreaOpRunning)
        {
            _settlementDirty = false;
            HandleFullAreaSettlement();
        }
    }

    // ───────────── 公开接口 ─────────────

    /// <summary>
    /// 尝试将一张卡片插入等待区。
    /// 若等待区已满则拒绝插入。
    /// </summary>
    /// <param name="card">待插入的卡片实体逻辑。</param>
    /// <returns>true=入队成功，false=等待区已满或参数无效。</returns>
    public bool TryRequestInsert(EliminateCardEntityLogic card)
    {
        if (card == null)
        {
            return false;
        }

        // 等待区已满，拒绝插入
        if (_currentCardCount >= _maxCardCount)
        {
            return false;
        }

        // 立即标记为移动中，避免快速连点重复入队
        card.SetMoving(true);
        card.SetCardArea(CardArea.WaitingArea);

        // 为卡片分配入槽序号（双端快照排序依据）
        RecordInsertSerialIfNeeded(card);

        // Zero-GC：使用单卡构造，不 new List
        _waitingAreaOpQueue.Enqueue(new WaitingAreaOp(WaitingAreaOpType.Insert, card, InsertMoveDuration));
        return true;
    }

    /// <summary>
    /// 当前等待区是否已满。
    /// </summary>
    public bool IsFull => _currentCardCount >= _maxCardCount;

    /// <summary>
    /// 当前等待区卡片数量。
    /// </summary>
    public int CurrentCardCount => _currentCardCount;

    /// <summary>
    /// 等待区最大容量。
    /// </summary>
    public int MaxCardCount => _maxCardCount;

    /// <summary>
    /// 从等待区前方取出指定数量的卡片，仅修改数据结构，不做动画/回收。
    /// 由移出道具（ShiftOut）调用：取出卡片后由 Controller 层负责飞行动画 + 回收 + 前移补位。
    /// ⚠️ 避坑：此方法仅修改 _waitingOrder / _currentCardCount / 入槽序号缓存，
    /// 调用方必须自行负责：1) 卡片飞行动画 2) HideEntity 回收 3) 调用 CompactAfterDetach 前移补位。
    /// </summary>
    /// <param name="count">要取出的卡片数量；实际取出数 = min(count, _currentCardCount)。</param>
    /// <returns>取出的卡片列表；等待区为空时返回空列表。</returns>
    public List<EliminateCardEntityLogic> DetachCardsFromWaitingArea(int count)
    {
        List<EliminateCardEntityLogic> result = new List<EliminateCardEntityLogic>();

        if (count <= 0 || _currentCardCount <= 0 || _waitingOrder.Count <= 0)
        {
            return result;
        }

        // 实际取出数量不超过当前等待区卡数
        int detachCount = Mathf.Min(count, _currentCardCount);

        for (int i = 0; i < detachCount; i++)
        {
            if (_waitingOrder[i] != null)
            {
                result.Add(_waitingOrder[i]);
            }
        }

        if (result.Count <= 0)
        {
            return result;
        }

        // 从 _waitingOrder 中移除这些卡片
        for (int i = 0; i < result.Count; i++)
        {
            _waitingOrder.Remove(result[i]);
            // 清理该卡片的入槽序号缓存，防止字典膨胀
            RemoveInsertSerialByCard(result[i]);
        }

        // 更新等待区卡片计数
        _currentCardCount = _waitingOrder.Count;

        return result;
    }

    /// <summary>
    /// 在 DetachCardsFromWaitingArea 之后调用，执行前移补位动画。
    /// 重建双端快照 + 将剩余卡片动画移动到前方空槽位。
    /// ⚠️ 避坑：必须在 DetachCardsFromWaitingArea 之后、且所有飞出卡片动画完成后调用，
    /// 否则快照中仍包含已取出的卡片，导致补位动画错乱。
    /// </summary>
    /// <param name="moveDuration">前移补位动画时长（秒）。</param>
    public void CompactAfterDetach(float moveDuration)
    {
        RefreshWaitingAreaLayout(moveDuration);
    }

    // ───────────── 分数槽位公开接口 ─────────────

    /// <summary>
    /// 刷新所有分数槽位的显示。
    /// 对齐 xinpgdd 的 RefreshWaitingAreaScoreTexts 逻辑：
    /// 有卡槽位显示 perCardScore，空槽位显示 0。
    /// </summary>
    /// <param name="perCardScore">当前单卡总分（分量叠加分 + 基础分）。</param>
    public void RefreshScoreDisplay(int perCardScore)
    {
        if (_scoreSlotRenderers == null || _scoreSlotRenderers.Length <= 0)
        {
            return;
        }

        for (int i = 0; i < _scoreSlotRenderers.Length; i++)
        {
            var renderer = _scoreSlotRenderers[i];
            if (renderer == null)
            {
                continue;
            }

            // 基于快照数组判断槽位是否有卡
            bool hasCard = _snapshotCards != null
                && i < _snapshotCards.Length
                && _snapshotCards[i] != null;

            int displayScore = hasCard ? perCardScore : 0;
            renderer.SetVisible(true);
            renderer.SetNumber(displayScore);
        }
    }

    /// <summary>
    /// 清空所有分数槽位的显示，归零为 0 并保持可见。
    /// </summary>
    public void ClearScoreDisplay()
    {
        if (_scoreSlotRenderers == null || _scoreSlotRenderers.Length <= 0)
        {
            return;
        }

        for (int i = 0; i < _scoreSlotRenderers.Length; i++)
        {
            var renderer = _scoreSlotRenderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.SetVisible(true);
            renderer.SetNumber(0);
        }
    }

    // ───────────── 轮数/基础分信息文本公开接口 ─────────────

    /// <summary>
    /// 刷新轮数/基础分信息文本显示。
    /// 格式："当前轮数:{currentRound} / 基础分:{baseScore}"
    /// </summary>
    /// <param name="currentRound">当前轮次（从 1 开始）。</param>
    /// <param name="baseScore">当前轮次的基础分。</param>
    public void RefreshRoundInfoDisplay(int currentRound, int baseScore)
    {
        if (_roundInfoTextTMP != null)
        {
            _roundInfoTextTMP.text = $"当前轮数:{currentRound} / 基础分:{baseScore}";
        }
    }

    /// <summary>
    /// 刷新 Combo 显示。
    /// 当连击数大于 1 且仍处于有效时间窗内时，显示 Combo、更新文本与 Slider，
    /// 并播放一次轻量的强调动画。
    /// </summary>
    /// <param name="combo">当前连击数。</param>
    /// <param name="comboWindowSeconds">本次连击窗口总时长（秒）。</param>
    /// <param name="remainingWindowSeconds">当前剩余时间（秒）。</param>
    public void RefreshComboDisplay(int combo, float comboWindowSeconds, float remainingWindowSeconds)
    {
        // 任一核心引用缺失都直接跳过，避免空引用打断整局流程。
        if (_comboRootRect == null || _comboTextTMP == null || _comboSlider == null)
        {
            return;
        }

        // combo = 1 不显示连击 UI；窗口非法或已到期也直接隐藏。
        if (combo < 1 || comboWindowSeconds <= 0f || remainingWindowSeconds <= 0f)
        {
            HideComboDisplay(true);
            return;
        }

        // 记录当前倒计时状态，供 OnUpdate 中无 GC 驱动 Slider 递减。
        _comboWindowSeconds = comboWindowSeconds;
        _comboExpireRealTime = Time.unscaledTime + remainingWindowSeconds;

        // 更新文本与滑条数值。
        _comboTextTMP.text = $"x{combo}";
        _comboSlider.minValue = 0f;
        _comboSlider.maxValue = 1f;
        _comboSlider.SetValueWithoutNotify(Mathf.Clamp01(remainingWindowSeconds / comboWindowSeconds));

        // 判断本次是首次出现还是续击刷新，用于决定初始缩放和透明度。
        bool wasVisible = _comboRootRect.gameObject.activeSelf;

        // 先终止上一段残留动画，避免快速连击时 Sequence 叠加。
        StopComboDisplayTween();
        _comboRootRect.gameObject.SetActive(true);
        _comboRootRect.anchoredPosition = wasVisible
            ? _comboShownAnchoredPosition
            : _comboShownAnchoredPosition + ComboIntroOffset;
        _comboRootRect.localScale = wasVisible ? Vector3.one : new Vector3(0.85f, 0.85f, 1f);

        // 续击/首次出现都做一次轻量强调，保持反馈统一且成本可控。
        _comboDisplaySequence = DOTween.Sequence();
        if (_comboCanvasGroup != null)
        {
            _comboCanvasGroup.alpha = wasVisible ? 1f : 0f;
            if (!wasVisible)
            {
                _comboDisplaySequence.Join(_comboCanvasGroup.DOFade(1f, 0.12f));
            }
        }

        if (!wasVisible)
        {
            _comboDisplaySequence.Join(_comboRootRect.DOAnchorPos(_comboShownAnchoredPosition, 0.18f).SetEase(Ease.OutQuad));
        }

        _comboDisplaySequence.Append(_comboRootRect.DOScale(1.12f, 0.12f).SetEase(Ease.OutQuad));
        _comboDisplaySequence.Append(_comboRootRect.DOScale(Vector3.one, 0.12f).SetEase(Ease.OutQuad));
        _comboDisplaySequence.OnComplete(() => _comboDisplaySequence = null);
    }

    /// <summary>
    /// 对外隐藏 Combo 显示。
    /// 供控制器在失败、切场景、区域反注册时主动调用。
    /// </summary>
    public void HideComboDisplay()
    {
        HideComboDisplay(true);
    }

    // ───────────── 内部方法：定位与槽位 ─────────────

    /// <summary>
    /// 把一份区域显示数据真正应用到实体上。
    /// </summary>
    public void ApplyData(EliminateTheAreaEntityData entityData)
    {
        if (entityData == null)
        {
            return;
        }

        CachedTransform.position = entityData.WorldPosition;
    }

    /// <summary>
    /// 从 prefab 层级中解析 Locations 子物体，缓存为等待区槽位。
    /// Locations 下预期有 MaxSlotCount(8) 个子物体，按 GetChild 顺序对应槽位 0~7。
    /// </summary>
    private void ResolveWaitingAreaSlots()
    {
        Transform locationsRoot = CachedTransform.Find("Locations");
        if (locationsRoot == null)
        {
            Log.Warning("EliminateTheAreaEntityLogic: 未找到 Locations 子物体，等待区槽位为空。");
            _waitingAreaSlots = new Transform[0];
            _maxCardCount = 0;
            return;
        }

        int childCount = locationsRoot.childCount;
        if (childCount <= 0)
        {
            Log.Warning("EliminateTheAreaEntityLogic: Locations 下无子物体，等待区槽位为空。");
            _waitingAreaSlots = new Transform[0];
            _maxCardCount = 0;
            return;
        }

        _waitingAreaSlots = new Transform[childCount];
        for (int i = 0; i < childCount; i++)
        {
            _waitingAreaSlots[i] = locationsRoot.GetChild(i);
        }

        _maxCardCount = childCount;
    }

    /// <summary>
    /// 校验 Combo 相关 Inspector 引用，并补齐 CanvasGroup。
    /// 这里只要求预制体手动拖拽 `Combo` 根节点；文本与 Slider 从其子节点自动解析。
    /// </summary>
    private void ResolveComboDisplay()
    {
        // 根节点未绑定时，只记录警告，不中断实体其它逻辑。
        if (_comboRootRect == null)
        {
            _comboCanvasGroup = null;
            _comboTextTMP = null;
            _comboSlider = null;
            Log.Warning("EliminateTheAreaEntityLogic: Combo 根节点未在 Inspector 绑定，请在预制体上手动拖入 Combo。");
            return;
        }

        // Combo 必须挂在当前实体实例的 Canvas 下，否则即使文本与 Slider 在刷新，
        // 实际显示位置也可能漂移到屏幕外。
        Transform canvasTransform = CachedTransform.Find("Canvas");
        if (canvasTransform == null)
        {
            _comboCanvasGroup = null;
            _comboTextTMP = null;
            _comboSlider = null;
            Log.Warning("EliminateTheAreaEntityLogic: 未找到 Canvas 子物体，Combo 无法定位到正确显示区域。");
            return;
        }

        Vector2 comboAnchorMin = _comboRootRect.anchorMin;
        Vector2 comboAnchorMax = _comboRootRect.anchorMax;
        Vector2 comboPivot = _comboRootRect.pivot;
        Vector2 comboSizeDelta = _comboRootRect.sizeDelta;
        Vector2 comboAnchoredPosition = _comboRootRect.anchoredPosition;

        if (_comboRootRect.parent != canvasTransform)
        {
            _comboRootRect.SetParent(canvasTransform, false);
        }

        // 重新挂回 Canvas 后，把预制体里的布局参数原样写回，避免手动拖拽或对象池复用导致位置脏掉。
        _comboRootRect.anchorMin = comboAnchorMin;
        _comboRootRect.anchorMax = comboAnchorMax;
        _comboRootRect.pivot = comboPivot;
        _comboRootRect.sizeDelta = comboSizeDelta;
        _comboRootRect.anchoredPosition = comboAnchoredPosition;
        _comboShownAnchoredPosition = comboAnchoredPosition;

        // CanvasGroup 不强制手动拖拽，缺失时运行时补挂到 Combo 根节点。
        _comboCanvasGroup = _comboRootRect.GetComponent<CanvasGroup>();
        if (_comboCanvasGroup == null)
        {
            _comboCanvasGroup = _comboRootRect.gameObject.AddComponent<CanvasGroup>();
        }

        // 从手动拖入的 Combo 根节点下解析文本与滑条子节点。
        Transform comboTextTransform = _comboRootRect.Find("TxtCombo");
        if (comboTextTransform == null)
        {
            comboTextTransform = _comboRootRect.Find("Text (TMP)");
        }

        _comboTextTMP = comboTextTransform != null
            ? comboTextTransform.GetComponent<TextMeshProUGUI>()
            : null;

        Transform comboSliderTransform = _comboRootRect.Find("Slider");
        _comboSlider = comboSliderTransform != null
            ? comboSliderTransform.GetComponent<Slider>()
            : null;

        if (_comboTextTMP == null || _comboSlider == null)
        {
            Log.Warning("EliminateTheAreaEntityLogic: Combo 子节点缺失，请检查 Combo 下是否存在 TxtCombo/Text (TMP) 与 Slider。");
            return;
        }

        // 统一规范 Slider 区间，后续只写 0~1 的归一化值，避免依赖 prefab 默认配置。
        if (_comboSlider != null)
        {
            _comboSlider.minValue = 0f;
            _comboSlider.maxValue = 1f;
            _comboSlider.SetValueWithoutNotify(0f);
        }
    }

    /// <summary>
    /// 从 prefab 层级中解析 ScoreLocations 子物体，缓存为分数槽位渲染器。
    /// ScoreLocations 下预期有 MaxSlotCount(8) 个子物体，每个子物体挂载或运行时补挂 WaitingAreaScoreSpriteRenderer。
    /// ⚠️ 避坑：不按 X 独立排序，而是按世界 X 坐标邻近度匹配到 _waitingAreaSlots，
    /// 保证 _scoreSlotRenderers[i] 与 _waitingAreaSlots[i] 严格一一对应。
    /// </summary>
    private void ResolveScoreSlotRenderers()
    {
        Transform scoresRoot = CachedTransform.Find("ScoreLocations");
        if (scoresRoot == null)
        {
            Log.Warning("EliminateTheAreaEntityLogic: 未找到 ScoreLocations 子物体，分数槽位为空。");
            _scoreSlotRenderers = new WaitingAreaScoreSpriteRenderer[0];
            return;
        }

        int childCount = scoresRoot.childCount;
        if (childCount <= 0)
        {
            Log.Warning("EliminateTheAreaEntityLogic: ScoreLocations 下无子物体，分数槽位为空。");
            _scoreSlotRenderers = new WaitingAreaScoreSpriteRenderer[0];
            return;
        }

        // 采集每个子节点的 WaitingAreaScoreSpriteRenderer，缺失则运行时补挂
        var buffer = new List<WaitingAreaScoreSpriteRenderer>(childCount);
        for (int i = 0; i < childCount; i++)
        {
            Transform child = scoresRoot.GetChild(i);
            if (child == null)
            {
                continue;
            }

            var renderer = child.GetComponent<WaitingAreaScoreSpriteRenderer>();
            if (renderer == null)
            {
                // 兼容旧预制体：运行时自动补挂图片渲染组件
                renderer = child.gameObject.AddComponent<WaitingAreaScoreSpriteRenderer>();
            }

            if (renderer != null)
            {
                buffer.Add(renderer);
            }
        }

        // ⚠️ 避坑：按世界 X 坐标邻近度匹配到 _waitingAreaSlots，
        // 而非独立排序。保证 _scoreSlotRenderers[i] 对应 _waitingAreaSlots[i]。
        if (_waitingAreaSlots == null || _waitingAreaSlots.Length <= 0)
        {
            _scoreSlotRenderers = new WaitingAreaScoreSpriteRenderer[0];
            return;
        }

        WaitingAreaScoreSpriteRenderer[] allRenderers = buffer.ToArray();
        _scoreSlotRenderers = new WaitingAreaScoreSpriteRenderer[_waitingAreaSlots.Length];

        // 已匹配标记，避免一个渲染器被多个槽位重复匹配
        bool[] matched = new bool[allRenderers.Length];

        for (int slotIdx = 0; slotIdx < _waitingAreaSlots.Length; slotIdx++)
        {
            Transform slot = _waitingAreaSlots[slotIdx];
            if (slot == null)
            {
                continue;
            }

            float slotX = slot.position.x;
            float minDist = float.MaxValue;
            int bestIdx = -1;

            for (int rIdx = 0; rIdx < allRenderers.Length; rIdx++)
            {
                if (matched[rIdx] || allRenderers[rIdx] == null)
                {
                    continue;
                }

                float dist = Mathf.Abs(allRenderers[rIdx].transform.position.x - slotX);
                if (dist < minDist)
                {
                    minDist = dist;
                    bestIdx = rIdx;
                }
            }

            if (bestIdx >= 0)
            {
                _scoreSlotRenderers[slotIdx] = allRenderers[bestIdx];
                matched[bestIdx] = true;
            }
        }
    }

    /// <summary>
    /// 设置 Canvas 的排序层级，确保其渲染在 SpriteRenderer 之上。
    /// 卡片 SpriteRenderer 基准 sortingOrder = 200+，Canvas 默认 0 会被完全遮挡。
    /// ⚠️ 避坑：World Space Canvas 的 sortingOrder 必须大于场景中所有 SpriteRenderer，
    /// 否则 Canvas 上的 TMP 文本会被精灵覆盖而不可见。
    /// </summary>
    private void SetupCanvasSortingOrder()
    {
        Transform canvasTransform = CachedTransform.Find("Canvas");
        if (canvasTransform == null)
        {
            return;
        }

        var canvas = canvasTransform.GetComponent<Canvas>();
        if (canvas == null)
        {
            return;
        }

        // 卡片 sortingOrder 基准 200，Canvas 需要更高才能显示在卡片之上
        if (canvas.sortingOrder < 300)
        {
            canvas.sortingOrder = 300;
        }
    }

    /// <summary>
    /// 获取指定索引的槽位世界位置。
    /// </summary>
    /// <param name="index">槽位索引，0~maxCardCount-1。</param>
    /// <returns>槽位世界位置；索引越界时返回当前实体位置。</returns>
    private Vector3 GetSlotPosition(int index)
    {
        if (_waitingAreaSlots == null || index < 0 || index >= _waitingAreaSlots.Length || _waitingAreaSlots[index] == null)
        {
            return CachedTransform.position;
        }

        return _waitingAreaSlots[index].position;
    }

    /// <summary>
    /// 每帧刷新 Combo 倒计时滑条。
    /// 这里只做纯数值更新，不创建 Tween，不触发额外 GC。
    /// </summary>
    private void UpdateComboDisplayCountdown()
    {
        // 仅在 Combo 正处于激活窗口时更新。
        if (_comboRootRect == null || _comboSlider == null || _comboExpireRealTime <= 0f || _comboWindowSeconds <= 0f)
        {
            return;
        }

        // 剩余时间耗尽后自动走隐藏流程。
        float remaining = _comboExpireRealTime - Time.unscaledTime;
        if (remaining <= 0f)
        {
            HideComboDisplay(false);
            return;
        }

        // Slider 始终写入归一化值，避免依赖 prefab 的 min/max 配置。
        _comboSlider.SetValueWithoutNotify(Mathf.Clamp01(remaining / _comboWindowSeconds));
    }

    /// <summary>
    /// 隐藏 Combo 显示。
    /// immediate=true 时立即隐藏；false 时执行一个很短的淡出/缩小动画。
    /// </summary>
    /// <param name="immediate">是否立即隐藏。</param>
    private void HideComboDisplay(bool immediate)
    {
        // 先清空逻辑时窗，防止隐藏动画期间仍被 OnUpdate 继续驱动。
        _comboExpireRealTime = -1f;
        _comboWindowSeconds = 0f;

        // 根节点未绑定时直接返回，避免空引用。
        if (_comboRootRect == null)
        {
            return;
        }

        // 先终止旧动画，避免隐藏动画与显示动画并发叠加。
        StopComboDisplayTween();

        GameObject comboGameObject = _comboRootRect.gameObject;
        if (immediate || !comboGameObject.activeSelf)
        {
            // 立即隐藏分支：直接恢复到“不可见 + 零进度 + 空文本”的干净状态。
            if (_comboCanvasGroup != null)
            {
                _comboCanvasGroup.alpha = 0f;
            }

            if (_comboSlider != null)
            {
                _comboSlider.SetValueWithoutNotify(0f);
            }

            if (_comboTextTMP != null)
            {
                _comboTextTMP.text = string.Empty;
            }

            _comboRootRect.anchoredPosition = _comboShownAnchoredPosition;
            _comboRootRect.localScale = Vector3.one;
            comboGameObject.SetActive(false);
            return;
        }

        // 若缺少 CanvasGroup，则退化为直接隐藏，避免额外依赖导致流程失败。
        if (_comboCanvasGroup == null)
        {
            if (_comboSlider != null)
            {
                _comboSlider.SetValueWithoutNotify(0f);
            }

            if (_comboTextTMP != null)
            {
                _comboTextTMP.text = string.Empty;
            }

            _comboRootRect.anchoredPosition = _comboShownAnchoredPosition;
            _comboRootRect.localScale = Vector3.one;
            comboGameObject.SetActive(false);
            return;
        }

        // 非立即隐藏分支：做一个很短的淡出 + 缩小收尾动画。
        _comboDisplaySequence = DOTween.Sequence();
        _comboDisplaySequence.Join(_comboCanvasGroup.DOFade(0f, 0.12f));
        _comboDisplaySequence.Join(_comboRootRect.DOScale(0.9f, 0.12f).SetEase(Ease.OutQuad));
        _comboDisplaySequence.OnComplete(() =>
        {
            _comboDisplaySequence = null;

            // 动画结束后统一恢复到干净初始态，避免对象池复用时残留旧显示。
            if (_comboSlider != null)
            {
                _comboSlider.SetValueWithoutNotify(0f);
            }

            if (_comboTextTMP != null)
            {
                _comboTextTMP.text = string.Empty;
            }

            _comboRootRect.anchoredPosition = _comboShownAnchoredPosition;
            _comboRootRect.localScale = Vector3.one;
            comboGameObject.SetActive(false);
        });
    }

    /// <summary>
    /// 终止 Combo 相关 Tween。
    /// 供显示前、隐藏前、OnHide 清理时统一调用。
    /// </summary>
    private void StopComboDisplayTween()
    {
        if (_comboDisplaySequence != null && _comboDisplaySequence.IsActive())
        {
            _comboDisplaySequence.Kill(false);
        }

        _comboDisplaySequence = null;
    }

    // ───────────── 内部方法：操作队列 ─────────────

    /// <summary>
    /// 尝试处理等待区操作队列。
    /// 若当前有操作正在执行则跳过。
    /// </summary>
    private void TryProcessWaitingAreaQueue()
    {
        if (_isWaitingAreaOpRunning)
        {
            return;
        }

        if (_waitingAreaOpQueue.Count <= 0)
        {
            return;
        }

        _isWaitingAreaOpRunning = true;
        WaitingAreaOp op = _waitingAreaOpQueue.Dequeue();
        ExecuteWaitingAreaOp(op);
    }

    /// <summary>
    /// 执行一个等待区操作。
    /// </summary>
    private void ExecuteWaitingAreaOp(WaitingAreaOp op)
    {
        switch (op.OpType)
        {
            case WaitingAreaOpType.Insert:
                // 优先使用 Zero-GC 单卡字段 Card，兜底取 Cards[0]
                ExecuteInsertOp(op.Card ?? (op.Cards != null && op.Cards.Count > 0 ? op.Cards[0] : null));
                break;
            case WaitingAreaOpType.RemoveBatch:
                ExecuteRemoveBatchOp(op);
                break;
            case WaitingAreaOpType.Compact:
                ExecuteCompactOp(op);
                break;
        }
    }

    // ───────────── 内部方法：插入 ─────────────

    /// <summary>
    /// 执行插入操作。
    /// 核心链路：计算逻辑插入索引 → 写入 _waitingOrder → 重建双端快照 → 基于快照执行并行动画 → 满格检测。
    /// 新卡飞入与已有卡移位并行执行，目标位置全部基于 _snapshotCards 而非 _waitingOrder 的顺序索引。
    /// </summary>
    /// <param name="card">待插入的卡片实体逻辑。</param>
    private void ExecuteInsertOp(EliminateCardEntityLogic card)
    {
        if (card == null)
        {
            FinishWaitingAreaOp();
            return;
        }

        // 计算逻辑插入索引：同类型归组，组合靠左，单牌靠右
        int insertIndex = CalculateInsertIndex(card.TypeId);

        // 写入逻辑列表
        if (insertIndex < 0 || insertIndex > _waitingOrder.Count)
        {
            insertIndex = _waitingOrder.Count;
        }
        _waitingOrder.Insert(insertIndex, card);
        _currentCardCount = _waitingOrder.Count;

        // 插入后检测同类型是否首次达到 2 张，若是则固化成组序号
        TryAssignGroupOrderWhenTypeBecomesPair(card);

        // ── 重建双端快照，获取视觉槽位映射 ──
        BuildDualEndSnapshot();

        // ── 快照就绪后通知控制器刷新分数显示 ──
        // ⚠️ 避坑：必须在 BuildDualEndSnapshot 之后调用，否则 _snapshotCards 仍为旧值。
        OnWaitingAreaLayoutChanged?.Invoke();

        // ── 并行动画：基于 _snapshotCards 计算目标位置 ──

        // 计数器：追踪正在移动的卡片数量，全部完成后才结束当前操作
        int movingCount = 0;

        // 1) 新卡飞入快照中的目标槽位
        int snapshotIndex = FindSnapshotIndex(card);
        Vector3 targetPos = GetSlotPosition(snapshotIndex);
        card.CachedTransform.DOKill(false);
        movingCount++;
        card.CachedTransform.DOMove(targetPos, InsertMoveDuration)
            .SetEase(Ease.OutQuad)
            .OnComplete(() =>
            {
                card.SetMoving(false);
                movingCount--;
                if (movingCount <= 0)
                {
                    OnAllInsertAnimationsComplete();
                }
            });

        // 2) 已有卡移位：基于快照索引，把不在正确位置的卡片动画移过去
        for (int i = 0; i < _snapshotCards.Length; i++)
        {
            EliminateCardEntityLogic existingCard = _snapshotCards[i];
            if (existingCard == null || existingCard == card)
            {
                continue;
            }

            Vector3 shiftTarget = GetSlotPosition(i);
            // 仅对位置不一致的卡片执行移动
            if (Vector3.SqrMagnitude(existingCard.CachedTransform.position - shiftTarget) > 0.0001f)
            {
                movingCount++;
                existingCard.CachedTransform.DOKill(false);
                existingCard.CachedTransform.DOMove(shiftTarget, ShiftMoveDuration)
                    .SetEase(Ease.OutQuad)
                    .OnComplete(() =>
                    {
                        movingCount--;
                        if (movingCount <= 0)
                        {
                            OnAllInsertAnimationsComplete();
                        }
                    });
            }
        }

        // 如果没有移动动画（极端情况），直接完成
        if (movingCount <= 0)
        {
            OnAllInsertAnimationsComplete();
        }
    }

    /// <summary>
    /// 插入动画全部完成后的统一回调。
    /// 满格检测在此处执行，保证所有卡片已就位。
    /// </summary>
    private void OnAllInsertAnimationsComplete()
    {
        // 满格检测
        if (_currentCardCount >= _maxCardCount)
        {
            _settlementDirty = true;
        }

        FinishWaitingAreaOp();
    }

    /// <summary>
    /// 计算卡片在等待区中的插入索引。
    /// 同类型归组策略：
    /// 1. 扫描 _waitingOrder，找到已有同类型卡片的区间；
    /// 2. 若找到同类型，插入到该区间的末尾；
    /// 3. 若未找到同类型，插入到所有组合之后（单牌靠右）。
    /// </summary>
    /// <param name="typeId">待插入卡片的类型 Id。</param>
    /// <returns>插入索引。</returns>
    private int CalculateInsertIndex(int typeId)
    {
        if (_waitingOrder.Count <= 0)
        {
            return 0;
        }

        // 找到同类型区间的末尾位置
        int lastSameTypeIndex = -1;
        int firstSingleTypeIndex = _waitingOrder.Count; // 第一个"单牌"的索引

        // 统计每种类型的数量
        for (int i = 0; i < _waitingOrder.Count; i++)
        {
            if (_waitingOrder[i] != null && _waitingOrder[i].TypeId == typeId)
            {
                lastSameTypeIndex = i;
            }
        }

        // 如果找到同类型，插入到该区间末尾之后
        if (lastSameTypeIndex >= 0)
        {
            return lastSameTypeIndex + 1;
        }

        // 未找到同类型：找到第一个"出现次数=1"的单牌位置，插入到它前面
        // 简化策略：直接插入到列表末尾（单牌靠右）
        return _waitingOrder.Count;
    }

    // ───────────── 内部方法：布局刷新 ─────────────

    /// <summary>
    /// 刷新等待区布局。
    /// 先重建双端快照，再基于快照执行动画。
    /// </summary>
    /// <param name="moveDuration">移动动画时长（秒）。</param>
    internal void RefreshWaitingAreaLayout(float moveDuration)
    {
        BuildDualEndSnapshot();
        AnimateRemainingCardsToSlots(moveDuration);
    }

    /// <summary>
    /// 将所有等待区卡片动画移动到对应的槽位位置。
    /// 基于 _snapshotCards 而非 _waitingOrder，保证组合靠左、单牌靠右的视觉布局。
    /// </summary>
    /// <param name="moveDuration">移动动画时长（秒）。</param>
    private void AnimateRemainingCardsToSlots(float moveDuration)
    {
        if (_waitingOrder.Count <= 0)
        {
            FinishWaitingAreaOp();
            return;
        }

        // 计数器：追踪正在移动的卡片数量，全部完成后才结束当前操作
        int movingCount = 0;
        bool anyMoved = false;

        // 遍历快照数组，而非逻辑顺序列表
        for (int i = 0; i < _snapshotCards.Length; i++)
        {
            EliminateCardEntityLogic card = _snapshotCards[i];
            if (card == null)
            {
                continue;
            }

            Vector3 targetPos = GetSlotPosition(i);
            // 仅对位置不一致的卡片执行移动
            if (Vector3.SqrMagnitude(card.CachedTransform.position - targetPos) > 0.0001f)
            {
                anyMoved = true;
                movingCount++;

                card.CachedTransform.DOKill(false);
                card.CachedTransform.DOMove(targetPos, moveDuration)
                    .SetEase(Ease.OutQuad)
                    .OnComplete(() =>
                    {
                        movingCount--;
                        if (movingCount <= 0)
                        {
                            FinishWaitingAreaOp();
                        }
                    });
            }
        }

        // 如果没有卡片需要移动，直接结束当前操作
        if (!anyMoved)
        {
            FinishWaitingAreaOp();
        }
    }

    // ───────────── 内部方法：满格结算 ─────────────

    /// <summary>
    /// 满格结算处理。
    /// 每日一关消除规则：
    /// 1. 等待区满格时，统计各 TypeId 出现次数；
    /// 2. 若单牌类型（出现次数恰好为 1）>= 2，判定失败，触发 OnSettlementFailed，不清空；
    /// 3. 否则走原清空流程：延迟 → 全卡同时缩小动画 → 批量回收 → 清空等待区 → 通知控制器。
    /// </summary>
    private void HandleFullAreaSettlement()
    {
        if (_currentCardCount < _maxCardCount)
        {
            return;
        }

        // ── 每日一关单牌失败判定 ──
        // 统计各 TypeId 在等待区的出现次数，单牌类型 >= 2 则判定失败
        if (HasTooManySingleTypes())
        {
            OnSettlementFailed?.Invoke();
            return;
        }

        _isWaitingAreaOpRunning = true;

        // ── 在动画开始前通知控制器计算得分 ──
        // 此时等待区卡片仍完整存在（CardArea=WaitingArea），TypeId 可读。
        // 动画结束后卡片会被 HideEntity 回收，届时 CardArea 会被重置为 Board。
        OnSettlementScoreCalculation?.Invoke();

        // 快照当前等待区卡片列表
        List<EliminateCardEntityLogic> cardsToClear = new List<EliminateCardEntityLogic>(_waitingOrder.Count);
        for (int i = 0; i < _waitingOrder.Count; i++)
        {
            if (_waitingOrder[i] != null)
            {
                cardsToClear.Add(_waitingOrder[i]);
            }
        }

        if (cardsToClear.Count <= 0)
        {
            _waitingOrder.Clear();
            _currentCardCount = 0;
            FinishWaitingAreaOp();
            return;
        }

        // 第一阶段：延迟 + 全卡同时缩小动画
        Sequence settleSequence = DOTween.Sequence();
        settleSequence.AppendInterval(ClearDelaySeconds);

        // 所有卡片同时缩小到零
        for (int i = 0; i < cardsToClear.Count; i++)
        {
            EliminateCardEntityLogic card = cardsToClear[i];
            if (card != null)
            {
                card.CachedTransform.DOKill(false);
                settleSequence.AppendCallback(() =>
                {
                    if (card != null && card.gameObject.activeInHierarchy)
                    {
                        card.CachedTransform.DOScale(Vector3.zero, ClearScaleDuration).SetEase(Ease.OutQuad);
                    }
                });
            }
        }

        // 第二阶段：缩小动画完成后批量回收
        settleSequence.AppendInterval(ClearScaleDuration);
        settleSequence.AppendCallback(() =>
        {
            // 批量回收卡片实体
            for (int i = 0; i < cardsToClear.Count; i++)
            {
                EliminateCardEntityLogic card = cardsToClear[i];
                if (card != null && card.gameObject.activeInHierarchy)
                {
                    // 恢复缩放，防止对象池复用时残留零缩放
                    card.CachedTransform.localScale = Vector3.one;
                    GameEntry.Entity.HideEntity(card.Entity);
                }
            }

            // 清空等待区数据
            _waitingOrder.Clear();
            _currentCardCount = 0;

            // 清空快照数组与排序追踪状态，为下一轮入槽做准备
            if (_snapshotCards != null)
            {
                for (int i = 0; i < _snapshotCards.Length; i++)
                {
                    _snapshotCards[i] = null;
                }
            }
            ResetDualEndLayoutState();

            // ── 播放 EliminateAnimation 消除特效动画 ──
            // ⚠️ 关键时序：必须等特效播放完毕后，才允许继续结算回调与后续自动入槽，
            // 否则新入槽卡片（sortingOrder 200+）会立刻盖住特效（sortingOrder 0）。
            PlayEliminateAnimations(() =>
            {
                // 结算完成，通知控制器（更新遮挡、检查自动入槽等）
                OnSettlementCleared?.Invoke();

                FinishWaitingAreaOp();
            });
        });
    }

    /// <summary>
    /// 判断当前等待区是否存在 2 张以上单牌。
    /// 单牌定义：某个 TypeId 在等待区中恰好只出现 1 次。
    /// 若单牌类型数量 >= 2，则判定为失败。
    /// </summary>
    /// <returns>true=单牌过多应判定失败；false=可继续清空。</returns>
    private bool HasTooManySingleTypes()
    {
        // 统计各 TypeId 出现次数
        // ⚠️ 避坑：这里用简单遍历而非 Dictionary，因为等待区最大 8 卡，
        // O(8^2) 远比 Dictionary 的 GC 开销更划算。
        int singleTypeCount = 0;
        for (int i = 0; i < _waitingOrder.Count; i++)
        {
            EliminateCardEntityLogic card = _waitingOrder[i];
            if (card == null)
            {
                continue;
            }

            int typeId = card.TypeId;
            int count = 0;
            for (int j = 0; j < _waitingOrder.Count; j++)
            {
                if (_waitingOrder[j] != null && _waitingOrder[j].TypeId == typeId)
                {
                    count++;
                }
            }

            // 恰好出现 1 次 = 单牌
            if (count == 1)
            {
                singleTypeCount++;
            }
        }

        // 单牌类型 >= 2 → 失败
        return singleTypeCount >= 2;
    }

    // ───────────── 内部方法：消除动画 ─────────────

    /// <summary>
    /// 从 prefab 层级中解析 Eliminate 子物体下的 Animation 组件。
    /// Prefab 层级：EliminateTheAreaEntity → Eliminate → EliminateAnimation[0~7]。
    /// 运行时统一保持子物体 active，仅通过 SpriteRenderer.enabled 控制显隐，
    /// 避免 Legacy Animation 在 SetActive(true) 当帧 Play 时只停留首帧。
    /// </summary>
    private void ResolveEliminateAnimations()
    {
        Transform eliminateRoot = CachedTransform.Find("Eliminate");
        _eliminateAnimationDuration = 0f;
        if (eliminateRoot == null)
        {
            Log.Warning("EliminateTheAreaEntityLogic: 未找到 Eliminate 子物体，消除动画不可用。");
            _eliminateAnimations = new Animator[0];
            _eliminateAnimationRenderers = new SpriteRenderer[0];
            return;
        }

        int childCount = eliminateRoot.childCount;
        if (childCount <= 0)
        {
            Log.Warning("EliminateTheAreaEntityLogic: Eliminate 下无子物体，消除动画不可用。");
            _eliminateAnimations = new Animator[0];
            _eliminateAnimationRenderers = new SpriteRenderer[0];
            return;
        }

        _eliminateAnimations = new Animator[childCount];
        _eliminateAnimationRenderers = new SpriteRenderer[childCount];
        for (int i = 0; i < childCount; i++)
        {
            Transform child = eliminateRoot.GetChild(i);
            if (child == null)
            {
                continue;
            }

            // 统一强制为 active，播放时不再切换激活态，避免 Legacy Animation 播放启动不稳定。
            child.gameObject.SetActive(true);

            // 缓存 Animation 组件引用；缺失则留空，Play 时跳过
            Animator animator = child.GetComponent<Animator>();
            _eliminateAnimations[i] = animator;

            // 缓存 SpriteRenderer，并默认隐藏。
            SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
            _eliminateAnimationRenderers[i] = renderer;
            if (renderer != null)
            {
                renderer.enabled = false;
            }

            if (animator != null)
            {
                animator.enabled = false;

                if (_eliminateAnimationDuration <= 0f)
                {
                    RuntimeAnimatorController controller = animator.runtimeAnimatorController;
                    if (controller != null)
                    {
                        AnimationClip[] clips = controller.animationClips;
                        if (clips != null)
                        {
                            for (int clipIdx = 0; clipIdx < clips.Length; clipIdx++)
                            {
                                AnimationClip clip = clips[clipIdx];
                                if (clip != null)
                                {
                                    _eliminateAnimationDuration = clip.length;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 播放所有 EliminateAnimation 子物体的 Legacy Animation 动画。
    /// 满格结算卡片缩小完毕后调用，视觉上呈现消除特效。
    /// 播放完毕后自动隐藏 SpriteRenderer，并在完成后触发回调。
    /// ⚠️ 避坑：Animation.clip.length 获取 clip 实际时长，用于延迟隐藏回调。
    /// ⚠️ 避坑：不再通过 SetActive(true) 触发播放，而是常驻 active + renderer.enabled，
    /// 否则 Legacy Animation 在部分 Unity 版本/运行时组合下只显示首帧不推进。
    /// </summary>
    /// <param name="onComplete">全部特效播放并隐藏后的回调。</param>
    private void PlayEliminateAnimations(System.Action onComplete)
    {
        if (_eliminateAnimations == null || _eliminateAnimations.Length <= 0)
        {
            onComplete?.Invoke();
            return;
        }

        // 终止上一轮残留的延迟 Sequence，防止叠加
        if (_eliminateAnimSequence != null && _eliminateAnimSequence.IsActive())
        {
            _eliminateAnimSequence.Kill(false);
        }

        // ── 播放消除音效 ──
        // 资源路径：Assets/_Game/Resources/Musics/Eliminate/EliminateAudio.wav
        // 通过 UIInteractionSound 统一入口播放，归入 "Effect" 声音组。
        UIInteractionSound.PlayEliminateSound();

        float maxDuration = 0f;

        for (int i = 0; i < _eliminateAnimations.Length; i++)
        {
            Animator animator = _eliminateAnimations[i];
            if (animator == null)
            {
                continue;
            }

            // 先确保渲染器可见，再停止旧状态，随后从 0 时间重新播放。
            // ⚠️ 避坑：必须直接操作 AnimationState.time，而不是依赖 Rewind()/SetActive 组合。
            if (_eliminateAnimationRenderers != null && i < _eliminateAnimationRenderers.Length)
            {
                SpriteRenderer renderer = _eliminateAnimationRenderers[i];
                if (renderer != null)
                {
                    renderer.enabled = true;
                }
            }

            animator.enabled = true;
            animator.Rebind();
            animator.Update(0f);
            animator.speed = 1f;
            animator.Play(EliminateAnimationStateHash, 0, 0f);
            animator.Update(0f);

            if (_eliminateAnimationDuration > maxDuration)
            {
                maxDuration = _eliminateAnimationDuration;
            }
        }

        // 延迟最长时长后自动隐藏所有动画 GameObject
        if (maxDuration > 0f)
        {
            _eliminateAnimSequence = DOTween.Sequence();
            _eliminateAnimSequence.AppendInterval(maxDuration);
            _eliminateAnimSequence.OnComplete(() =>
            {
                HideAllEliminateAnimationRenderers();
                _eliminateAnimSequence = null;
                onComplete?.Invoke();
            });
        }
        else
        {
            // 无 clip 或 length=0，立即隐藏并继续流程
            HideAllEliminateAnimationRenderers();
            onComplete?.Invoke();
        }
    }

    /// <summary>
    /// 停止所有消除动画并隐藏 SpriteRenderer。
    /// 由 OnHide 和播放完毕回调调用，确保不留残留。
    /// </summary>
    private void StopEliminateAnimations()
    {
        // 终止延迟 Sequence
        if (_eliminateAnimSequence != null && _eliminateAnimSequence.IsActive())
        {
            _eliminateAnimSequence.Kill(false);
        }
        _eliminateAnimSequence = null;

        // 停止动画 + 隐藏渲染器
        if (_eliminateAnimations != null)
        {
            for (int i = 0; i < _eliminateAnimations.Length; i++)
            {
                Animator animator = _eliminateAnimations[i];
                if (animator == null)
                {
                    continue;
                }

                animator.enabled = false;
            }
        }

        HideAllEliminateAnimationRenderers();
    }

    /// <summary>
    /// 隐藏所有 EliminateAnimation 的 SpriteRenderer，保持 GameObject 常驻 active。
    /// 这样下次播放时无需再切换激活态，可稳定触发 Legacy Animation 逐帧采样。
    /// </summary>
    private void HideAllEliminateAnimationRenderers()
    {
        if (_eliminateAnimationRenderers == null)
        {
            return;
        }

        for (int i = 0; i < _eliminateAnimationRenderers.Length; i++)
        {
            SpriteRenderer renderer = _eliminateAnimationRenderers[i];
            if (renderer != null)
            {
                renderer.enabled = false;
            }
        }
    }

    // ───────────── 内部方法：批量移除与前移 ─────────────

    /// <summary>
    /// 执行批量移除操作。
    /// 由外部调用（如道具移出）时使用，满格结算走 HandleFullAreaSettlement。
    /// </summary>
    private void ExecuteRemoveBatchOp(WaitingAreaOp op)
    {
        if (op.Cards == null || op.Cards.Count <= 0)
        {
            FinishWaitingAreaOp();
            return;
        }

        // 从 _waitingOrder 中移除指定卡片
        for (int i = 0; i < op.Cards.Count; i++)
        {
            EliminateCardEntityLogic card = op.Cards[i];
            if (card != null)
            {
                _waitingOrder.Remove(card);
                // 清理该卡片的入槽序号缓存，防止字典膨胀
                RemoveInsertSerialByCard(card);
                // 回收卡片实体
                if (card.gameObject.activeInHierarchy)
                {
                    card.CachedTransform.localScale = Vector3.one;
                    GameEntry.Entity.HideEntity(card.Entity);
                }
            }
        }

        _currentCardCount = _waitingOrder.Count;

        // 前移补位：先重建快照再动画
        RefreshWaitingAreaLayout(op.MoveDuration);
    }

    /// <summary>
    /// 执行前移补位操作。
    /// 将剩余卡片动画移动到前方的空槽位。
    /// </summary>
    private void ExecuteCompactOp(WaitingAreaOp op)
    {
        AnimateRemainingCardsToSlots(op.MoveDuration);
    }

    // ───────────── 内部方法：操作完成 ─────────────

    /// <summary>
    /// 标记当前等待区操作完成，允许处理下一个操作。
    /// </summary>
    private void FinishWaitingAreaOp()
    {
        _isWaitingAreaOpRunning = false;
    }

    // ───────────── 双端快照构建 ─────────────

    /// <summary>
    /// 重建双端快照视觉数组。
    /// 算法与旧项目 xinpgdd 的 BuildMainlineDualEndSnapshot 完全对齐：
    /// 1) 组合（同类型≥2）从左到右连续排列，且"先成组先靠左"；
    /// 2) 单牌（同类型=1）从右向左排列，按入槽序号旧到新放置，保证"新单牌更靠左"；
    /// 3) 中间允许空位，不做强制紧凑。
    /// </summary>
    private void BuildDualEndSnapshot()
    {
        // 确保快照数组容量
        if (_snapshotCards == null || _snapshotCards.Length != _maxCardCount)
        {
            _snapshotCards = _maxCardCount > 0 ? new EliminateCardEntityLogic[_maxCardCount] : new EliminateCardEntityLogic[0];
        }
        else
        {
            // 清空旧数据
            for (int i = 0; i < _snapshotCards.Length; i++)
            {
                _snapshotCards[i] = null;
            }
        }

        if (_waitingOrder.Count <= 0)
        {
            return;
        }

        // ── 第一步：按类型分组 ──
        // ⚠️ 避坑：这里用 Dictionary 是因为分组逻辑只在操作发生时执行（非每帧），
        // 且 MaxSlotCount=8 时 GC 开销可忽略。若后续需要 Zero-GC 可改为固定数组遍历。
        Dictionary<int, List<EliminateCardEntityLogic>> cardsByType = new Dictionary<int, List<EliminateCardEntityLogic>>(Mathf.Max(4, _waitingOrder.Count));
        for (int i = 0; i < _waitingOrder.Count; i++)
        {
            EliminateCardEntityLogic card = _waitingOrder[i];
            if (card == null)
            {
                continue;
            }

            List<EliminateCardEntityLogic> sameTypeCards;
            if (!cardsByType.TryGetValue(card.TypeId, out sameTypeCards))
            {
                sameTypeCards = new List<EliminateCardEntityLogic>(4);
                cardsByType.Add(card.TypeId, sameTypeCards);
            }

            sameTypeCards.Add(card);
        }

        // ── 第二步：分离组合类型与单牌类型 ──
        List<int> groupedTypes = new List<int>(cardsByType.Count);
        List<EliminateCardEntityLogic> singleCards = new List<EliminateCardEntityLogic>(cardsByType.Count);
        foreach (var pair in cardsByType)
        {
            List<EliminateCardEntityLogic> sameTypeCards = pair.Value;
            if (sameTypeCards == null || sameTypeCards.Count <= 0)
            {
                continue;
            }

            if (sameTypeCards.Count >= 2)
            {
                groupedTypes.Add(pair.Key);
            }
            else
            {
                singleCards.Add(sameTypeCards[0]);
            }
        }

        // ── 第三步：组合按成组序升序排列（先成组先靠左） ──
        groupedTypes.Sort(CompareGroupTypeOrder);

        // ── 第四步：组合从左端填入 ──
        int left = 0;
        for (int i = 0; i < groupedTypes.Count; i++)
        {
            int typeId = groupedTypes[i];
            List<EliminateCardEntityLogic> sameTypeCards = cardsByType[typeId];
            // 组内按入槽序号升序（旧到新），保证稳定排列
            sameTypeCards.Sort(CompareCardInsertSerial);

            for (int j = 0; j < sameTypeCards.Count; j++)
            {
                if (left < 0 || left >= _snapshotCards.Length)
                {
                    break;
                }

                _snapshotCards[left] = sameTypeCards[j];
                left++;
            }

            if (left >= _snapshotCards.Length)
            {
                break;
            }
        }

        // ── 第五步：单牌从右端填入 ──
        // 按入槽序号升序（旧到新），然后从右向左放置，保证"新单牌更靠左"
        singleCards.Sort(CompareCardInsertSerial);

        int right = _snapshotCards.Length - 1;
        for (int i = 0; i < singleCards.Count && right >= 0; i++)
        {
            // 跳过已被组合占用的位置
            while (right >= 0 && _snapshotCards[right] != null)
            {
                right--;
            }

            if (right < 0)
            {
                break;
            }

            _snapshotCards[right] = singleCards[i];
            right--;
        }
    }

    /// <summary>
    /// 在快照数组中查找指定卡片所在的索引。
    /// </summary>
    /// <param name="card">目标卡片。</param>
    /// <returns>快照索引；未找到返回 -1。</returns>
    private int FindSnapshotIndex(EliminateCardEntityLogic card)
    {
        if (_snapshotCards == null || card == null)
        {
            return -1;
        }

        for (int i = 0; i < _snapshotCards.Length; i++)
        {
            if (_snapshotCards[i] == card)
            {
                return i;
            }
        }

        return -1;
    }

    // ───────────── 入槽序号与成组序号 ─────────────

    /// <summary>
    /// 为卡片分配入槽序号。
    /// 同一张卡片在同一局内只分配一次，用于双端布局稳定排序。
    /// </summary>
    /// <param name="card">待记录的卡片。</param>
    private void RecordInsertSerialIfNeeded(EliminateCardEntityLogic card)
    {
        if (card == null)
        {
            return;
        }

        int entityId = card.Entity.Id;
        if (_cardInsertSerialByEntityId.ContainsKey(entityId))
        {
            return;
        }

        _insertSerialSeed++;
        _cardInsertSerialByEntityId[entityId] = _insertSerialSeed;
    }

    /// <summary>
    /// 获取卡片的入槽序号。
    /// 若历史缺失则即时补发，确保异常路径仍有稳定排序依据。
    /// </summary>
    /// <param name="card">目标卡片。</param>
    /// <returns>入槽序号；卡片为空时返回 int.MaxValue。</returns>
    private int GetInsertSerial(EliminateCardEntityLogic card)
    {
        if (card == null)
        {
            return int.MaxValue;
        }

        int serial;
        if (_cardInsertSerialByEntityId.TryGetValue(card.Entity.Id, out serial))
        {
            return serial;
        }

        // 兜底补发
        _insertSerialSeed++;
        serial = _insertSerialSeed;
        _cardInsertSerialByEntityId[card.Entity.Id] = serial;
        return serial;
    }

    /// <summary>
    /// 移除卡片的入槽序号缓存。
    /// 卡片从等待区移除时调用，防止字典持续膨胀。
    /// </summary>
    /// <param name="card">被移除的卡片。</param>
    private void RemoveInsertSerialByCard(EliminateCardEntityLogic card)
    {
        if (card == null)
        {
            return;
        }

        _cardInsertSerialByEntityId.Remove(card.Entity.Id);
    }

    /// <summary>
    /// 插入后检测同类型是否首次达到 2 张，若是则固化成组序号。
    /// 规则：仅当该类型当前数量首次达到 2 且此前未分配顺序时，才固化。
    /// </summary>
    /// <param name="card">刚插入的卡片。</param>
    private void TryAssignGroupOrderWhenTypeBecomesPair(EliminateCardEntityLogic card)
    {
        if (card == null)
        {
            return;
        }

        int typeId = card.TypeId;
        if (_groupOrderByTypeId.ContainsKey(typeId))
        {
            return;
        }

        int sameTypeCount = 0;
        for (int i = 0; i < _waitingOrder.Count; i++)
        {
            if (_waitingOrder[i] != null && _waitingOrder[i].TypeId == typeId)
            {
                sameTypeCount++;
                if (sameTypeCount >= 2)
                {
                    EnsureGroupOrder(typeId);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// 确保指定类型存在成组序号。
    /// 优先使用该类型"首次成双序号"（第2张卡片的入槽序号），
    /// 若无法推导则使用兜底序号。
    /// </summary>
    /// <param name="typeId">卡片类型 Id。</param>
    /// <returns>成组序号。</returns>
    private int EnsureGroupOrder(int typeId)
    {
        int groupOrder;
        if (_groupOrderByTypeId.TryGetValue(typeId, out groupOrder))
        {
            return groupOrder;
        }

        // 尝试从当前 waitingOrder 推导"首次成双"的入槽序号
        if (TryGetFirstPairInsertSerial(typeId, out int pairInsertSerial))
        {
            groupOrder = pairInsertSerial;
            _groupOrderByTypeId[typeId] = groupOrder;
            return groupOrder;
        }

        // 兜底：极端路径下仍需给出稳定值
        _groupOrderSeed++;
        groupOrder = _insertSerialSeed + 100000 + _groupOrderSeed;
        _groupOrderByTypeId[typeId] = groupOrder;
        return groupOrder;
    }

    /// <summary>
    /// 尝试根据 _waitingOrder 计算某类型"首次成双"的入槽序号。
    /// 定义：同类型卡片按入槽序号升序后，第2张卡片的序号即"首次成双序号"。
    /// </summary>
    /// <param name="typeId">卡片类型 Id。</param>
    /// <param name="firstPairInsertSerial">输出的首次成双序号。</param>
    /// <returns>是否成功推导。</returns>
    private bool TryGetFirstPairInsertSerial(int typeId, out int firstPairInsertSerial)
    {
        firstPairInsertSerial = int.MaxValue;
        if (_waitingOrder == null || _waitingOrder.Count <= 0)
        {
            return false;
        }

        int minSerial = int.MaxValue;
        int secondMinSerial = int.MaxValue;
        int sameTypeCount = 0;
        for (int i = 0; i < _waitingOrder.Count; i++)
        {
            EliminateCardEntityLogic card = _waitingOrder[i];
            if (card == null || card.TypeId != typeId)
            {
                continue;
            }

            int insertSerial = GetInsertSerial(card);
            sameTypeCount++;

            if (insertSerial < minSerial)
            {
                secondMinSerial = minSerial;
                minSerial = insertSerial;
            }
            else if (insertSerial < secondMinSerial)
            {
                secondMinSerial = insertSerial;
            }
        }

        if (sameTypeCount < 2 || secondMinSerial == int.MaxValue)
        {
            return false;
        }

        firstPairInsertSerial = secondMinSerial;
        return true;
    }

    /// <summary>
    /// 组合类型排序比较器：按成组序号升序，保证"先成组先靠左"。
    /// </summary>
    private int CompareGroupTypeOrder(int leftType, int rightType)
    {
        int leftOrder = EnsureGroupOrder(leftType);
        int rightOrder = EnsureGroupOrder(rightType);
        if (leftOrder != rightOrder)
        {
            return leftOrder.CompareTo(rightOrder);
        }

        // 兜底：同序号按类型值稳定排序，避免帧间抖动
        return leftType.CompareTo(rightType);
    }

    /// <summary>
    /// 卡片入槽序号排序比较器：按入槽序号从旧到新，保证组内/单牌间显示稳定。
    /// </summary>
    private int CompareCardInsertSerial(EliminateCardEntityLogic leftCard, EliminateCardEntityLogic rightCard)
    {
        int leftSerial = GetInsertSerial(leftCard);
        int rightSerial = GetInsertSerial(rightCard);
        if (leftSerial != rightSerial)
        {
            return leftSerial.CompareTo(rightSerial);
        }

        // 兜底：同序号按 Entity.Id 稳定排序
        int leftId = leftCard != null ? leftCard.Entity.Id : int.MaxValue;
        int rightId = rightCard != null ? rightCard.Entity.Id : int.MaxValue;
        return leftId.CompareTo(rightId);
    }

    /// <summary>
    /// 重置双端布局排序追踪状态。
    /// 在 OnShow / OnHide / 清空时调用，避免跨局残留顺序数据。
    /// </summary>
    private void ResetDualEndLayoutState()
    {
        _insertSerialSeed = 0;
        _groupOrderSeed = 0;
        _cardInsertSerialByEntityId.Clear();
        _groupOrderByTypeId.Clear();
    }
}
