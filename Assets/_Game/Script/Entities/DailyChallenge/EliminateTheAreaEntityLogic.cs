using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

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

    // ───────────── 等待区数据 ─────────────

    /// <summary>
    /// 等待区逻辑顺序列表。
    /// 唯一真相源：任何插入/删除/清空都只修改这个列表，再统一刷新布局。
    /// </summary>
    private readonly List<EliminateCardEntityLogic> _waitingOrder = new List<EliminateCardEntityLogic>(MaxSlotCount);

    /// <summary>
    /// 当前等待区中的卡片数量。
    /// </summary>
    private int _currentCardCount;

    /// <summary>
    /// 等待区最大容量。
    /// 初始化时从 _waitingAreaSlots.Length 取值，预期为 MaxSlotCount。
    /// </summary>
    private int _maxCardCount;

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
        /// 操作涉及的卡片（Insert 时为单卡，RemoveBatch 时为批量）。
        /// </summary>
        public readonly List<EliminateCardEntityLogic> Cards;

        /// <summary>
        /// 移动动画时长（秒）。
        /// </summary>
        public readonly float MoveDuration;

        public WaitingAreaOp(WaitingAreaOpType opType, List<EliminateCardEntityLogic> cards, float moveDuration)
        {
            OpType = opType;
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
    /// 满格失败回调：等待区满格时有 2 张以上单牌（出现次数恰好为 1 的类型 >= 2）。
    /// 由 EliminateCardController 注入，用于通知控制器弹出失败 UI。
    /// </summary>
    public System.Action OnSettlementFailed { get; set; }

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

        // 自动注册区域实体逻辑引用与结算回调（OnSettlementCleared / OnSettlementFailed 均由 RegisterAreaEntityLogic 内部注入）
        EliminateCardController.Instance?.RegisterAreaEntityLogic(this);
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
        card.SetInWaitingArea();

        // 构造 Insert 操作并入队
        var cards = new List<EliminateCardEntityLogic>(1) { card };
        _waitingAreaOpQueue.Enqueue(new WaitingAreaOp(WaitingAreaOpType.Insert, cards, InsertMoveDuration));
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
                ExecuteInsertOp(op);
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
    /// 核心链路：计算插入索引 → 写入列表 → 新卡飞入 + 已有卡移位同时开始 → 全部完成后满格检测。
    /// 新卡飞入（InsertMoveDuration）与已有卡移位（ShiftMoveDuration）并行执行。
    /// </summary>
    private void ExecuteInsertOp(WaitingAreaOp op)
    {
        EliminateCardEntityLogic card = op.Cards != null && op.Cards.Count > 0 ? op.Cards[0] : null;
        if (card == null)
        {
            FinishWaitingAreaOp();
            return;
        }

        // 计算插入索引：同类型归组，组合靠左，单牌靠右
        int insertIndex = CalculateInsertIndex(card.TypeId);

        // 写入逻辑列表
        if (insertIndex < 0 || insertIndex > _waitingOrder.Count)
        {
            insertIndex = _waitingOrder.Count;
        }
        _waitingOrder.Insert(insertIndex, card);
        _currentCardCount = _waitingOrder.Count;

        // ── 并行动画：新卡飞入 + 已有卡移位同时开始 ──

        // 计数器：追踪正在移动的卡片数量，全部完成后才结束当前操作
        int movingCount = 0;

        // 1) 新卡飞入目标槽位
        Vector3 targetPos = GetSlotPosition(insertIndex);
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

        // 2) 已有卡移位：insertIndex 之后的卡片需要右移一格
        for (int i = insertIndex + 1; i < _waitingOrder.Count; i++)
        {
            EliminateCardEntityLogic existingCard = _waitingOrder[i];
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
    /// 将 _waitingOrder 中的所有卡片动画移动到对应的槽位位置。
    /// </summary>
    /// <param name="moveDuration">移动动画时长（秒）。</param>
    private void RefreshWaitingAreaLayout(float moveDuration)
    {
        AnimateRemainingCardsToSlots(moveDuration);
    }

    /// <summary>
    /// 将所有等待区卡片动画移动到对应的槽位位置。
    /// 使用 DOTween Sequence 保证所有卡片并行移动。
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

        for (int i = 0; i < _waitingOrder.Count; i++)
        {
            EliminateCardEntityLogic card = _waitingOrder[i];
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
                int capturedIndex = i; // 闭包捕获

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

            // 结算完成，通知控制器（更新遮挡、检查自动入槽等）
            OnSettlementCleared?.Invoke();

            FinishWaitingAreaOp();
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
                // 回收卡片实体
                if (card.gameObject.activeInHierarchy)
                {
                    card.CachedTransform.localScale = Vector3.one;
                    GameEntry.Entity.HideEntity(card.Entity);
                }
            }
        }

        _currentCardCount = _waitingOrder.Count;

        // 前移补位
        AnimateRemainingCardsToSlots(op.MoveDuration);
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
}
