using System;
using UnityGameFramework.Runtime;

/// <summary>
/// 任务系统运行时模块。
/// 负责跟踪 8 个固定任务的进度、状态和领取记录，并通过云存档持久化。
///
/// 进度驱动方式：
///   - HatchComplete  → 订阅 EggHatchComponent.HatchStateChanged，读取 TotalHatchCount
///   - FeedComplete   → 订阅 PetDiningOrderComponent.CoinDropRequested，每次触发 +1
///   - GoldTotal      → 订阅 PetDiningOrderComponent.CoinDropRequested，只累计宠物本次吐出的金币数量
///   - HatchSlotCount → 订阅 PlayerRuntimeModule.ArchitectureStateChanged，读取 HatchSlotCount
///   - DietSlotCount  → 订阅 PlayerRuntimeModule.ArchitectureStateChanged，读取 DiningSeatCount
///   - FruiterSlotCount → 订阅 PlayerRuntimeModule.ArchitectureStateChanged，读取 OrchardSlotCount
/// </summary>
public sealed class TaskModule
{
    /// <summary>
    /// 任务数据表行缓存数组。
    /// 按任务 Id（从 1 开始）减 1 作为下标，固定 8 个槽位。
    /// </summary>
    private TaskDataRow[] _taskRows;

    /// <summary>
    /// 每个任务的当前进度值。
    /// 下标 = task.Id - 1。
    /// </summary>
    private int[] _progressValues;

    /// <summary>
    /// 每个任务的领取时间戳（UTC Ticks）。
    /// 0 表示未领取，大于 0 表示已领取且值为领取时刻。
    /// 下标 = task.Id - 1。
    /// </summary>
    private long[] _claimedTimestamps;

    /// <summary>
    /// 当前是否已完成首次初始化。
    /// 防止重复订阅事件。
    /// </summary>
    private bool _isInitialized;

    /// <summary>
    /// 当前是否已订阅游戏事件。
    /// 用于 Shutdown 时取消订阅。
    /// </summary>
    private bool _isEventSubscribed;

    /// <summary>
    /// 任务进度或状态发生变化时触发。
    /// UI 层（TaskUIForm）监听此事件刷新列表。
    /// </summary>
    public event Action TaskProgressChanged;

    /// <summary>
    /// 初始化任务模块。
    /// 加载数据表行、分配进度数组、订阅游戏事件。
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized)
        {
            return;
        }

        if (GameEntry.DataTables == null || !GameEntry.DataTables.IsAvailable<TaskDataRow>())
        {
            Log.Warning("TaskModule Initialize 失败：Task 数据表不可用。");
            return;
        }

        _taskRows = GameEntry.DataTables.GetAllDataRows<TaskDataRow>();
        if (_taskRows == null || _taskRows.Length <= 0)
        {
            Log.Warning("TaskModule Initialize 失败：Task 数据表无数据行。");
            return;
        }

        int taskCount = _taskRows.Length;
        _progressValues = new int[taskCount];
        _claimedTimestamps = new long[taskCount];

        SubscribeEvents();
        _isInitialized = true;

        // 初始化完成后立即评估一次，保证打开界面时进度已就绪。
        EvaluateAllProgress();
    }

    /// <summary>
    /// 关闭任务模块，取消事件订阅。
    /// </summary>
    public void Shutdown()
    {
        UnsubscribeEvents();
        _isInitialized = false;
    }

    /// <summary>
    /// 获取任务总数（等于数据表行数）。
    /// </summary>
    public int TaskCount => _taskRows != null ? _taskRows.Length : 0;

    /// <summary>
    /// 获取指定下标的任务数据表行。
    /// </summary>
    /// <param name="index">下标，范围 [0, TaskCount)。</param>
    /// <returns>任务数据行；越界或未初始化时返回 null。</returns>
    public TaskDataRow GetTaskRow(int index)
    {
        if (_taskRows == null || index < 0 || index >= _taskRows.Length)
        {
            return null;
        }

        return _taskRows[index];
    }

    /// <summary>
    /// 获取指定下标的任务当前状态。
    /// </summary>
    /// <param name="index">下标，范围 [0, TaskCount)。</param>
    /// <returns>任务状态；越界时返回 NotCompleted。</returns>
    public TaskStatus GetStatus(int index)
    {
        if (!_isInitialized || index < 0 || index >= _progressValues.Length)
        {
            return TaskStatus.NotCompleted;
        }

        // 已领取
        if (_claimedTimestamps[index] > 0)
        {
            return TaskStatus.Claimed;
        }

        // 可领取：进度达到目标
        TaskDataRow row = _taskRows[index];
        if (row != null && _progressValues[index] >= row.TargetCount)
        {
            return TaskStatus.Claimable;
        }

        return TaskStatus.NotCompleted;
    }

    /// <summary>
    /// 获取指定下标的任务当前进度。
    /// </summary>
    /// <param name="index">下标，范围 [0, TaskCount)。</param>
    /// <returns>当前进度值；越界时返回 0。</returns>
    public int GetProgress(int index)
    {
        if (!_isInitialized || index < 0 || index >= _progressValues.Length)
        {
            return 0;
        }

        return _progressValues[index];
    }

    /// <summary>
    /// 获取指定下标任务的领取时间戳（UTC Ticks）。
    /// </summary>
    /// <param name="index">下标，范围 [0, TaskCount)。</param>
    /// <returns>领取时间戳；未领取时返回 0。</returns>
    public long GetClaimedTimestamp(int index)
    {
        if (!_isInitialized || index < 0 || index >= _claimedTimestamps.Length)
        {
            return 0;
        }

        return _claimedTimestamps[index];
    }

    /// <summary>
    /// 尝试领取指定下标的任务奖励。
    /// </summary>
    /// <param name="index">下标，范围 [0, TaskCount)。</param>
    /// <returns>领取成功返回 true；任务不存在、状态非 Claimable 或依赖不可用时返回 false。</returns>
    public bool TryClaim(int index)
    {
        if (!_isInitialized || index < 0 || index >= _progressValues.Length)
        {
            return false;
        }

        if (GetStatus(index) != TaskStatus.Claimable)
        {
            return false;
        }

        TaskDataRow row = _taskRows[index];
        if (row == null)
        {
            return false;
        }

        if (GameEntry.Fruits == null)
        {
            Log.Warning("TaskModule TryClaim 失败：PlayerRuntimeModule 不可用。");
            return false;
        }

        // 发放金币奖励
        GameEntry.Fruits.AddGold(row.AwardGold);

        // 记录领取时间
        _claimedTimestamps[index] = DateTime.UtcNow.Ticks;

        // 触发状态变化事件
        TaskProgressChanged?.Invoke();

        // 标记云存档脏
        GameEntry.CloudSave?.MarkDirty(CloudSaveDirtyModule.Tasks);

        // 弹出 Toast 提示
        ToastUtility.Show($"{row.Name} 领取成功 +{row.AwardGold}金币");

        return true;
    }

    // ──────────────────────────────────────────────────────────
    //  云存档导出与应用
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 导出当前任务进度到云存档数组。
    /// 每条记录包含任务 Code 和领取时间戳（0=未领取）。
    /// </summary>
    /// <returns>任务存档数组；未初始化时返回空数组。</returns>
    public TaskSaveData[] ExportTaskProgress()
    {
        if (!_isInitialized || _taskRows == null)
        {
            return Array.Empty<TaskSaveData>();
        }

        TaskSaveData[] result = new TaskSaveData[_taskRows.Length];
        for (int i = 0; i < _taskRows.Length; i++)
        {
            result[i] = new TaskSaveData
            {
                code = _taskRows[i] != null ? _taskRows[i].Code : string.Empty,
                claimedAt = _claimedTimestamps[i],
                progress = _progressValues[i]
            };
        }

        return result;
    }

    /// <summary>
    /// 从云存档恢复任务进度。
    /// 按 Code 匹配任务行，恢复领取时间戳和进度值。
    /// </summary>
    /// <param name="data">云端保存的任务存档数组；为 null 时不做任何操作。</param>
    public void ApplyTaskProgress(TaskSaveData[] data)
    {
        if (!_isInitialized || _taskRows == null || data == null)
        {
            return;
        }

        // 重置为初始值，防止旧数据残留
        for (int i = 0; i < _claimedTimestamps.Length; i++)
        {
            _claimedTimestamps[i] = 0;
            _progressValues[i] = 0;
        }

        // 按 Code 匹配恢复
        for (int i = 0; i < data.Length; i++)
        {
            TaskSaveData saved = data[i];
            if (saved == null || string.IsNullOrWhiteSpace(saved.code))
            {
                continue;
            }

            int taskIndex = FindTaskIndexByCode(saved.code);
            if (taskIndex < 0)
            {
                continue;
            }

            _claimedTimestamps[taskIndex] = saved.claimedAt;
            _progressValues[taskIndex] = saved.progress;
        }

        // 恢复后再评估一次，保证进度与当前游戏状态对齐
        EvaluateAllProgress();
    }

    // ──────────────────────────────────────────────────────────
    //  事件订阅与进度评估
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 订阅所有必要的游戏事件。
    /// </summary>
    private void SubscribeEvents()
    {
        if (_isEventSubscribed)
        {
            return;
        }

        if (GameEntry.EggHatch != null)
        {
            GameEntry.EggHatch.HatchStateChanged += OnHatchStateChanged;
        }

        if (GameEntry.PetDiningOrders != null)
        {
            GameEntry.PetDiningOrders.CoinDropRequested += OnCoinDropRequested;
        }

        if (GameEntry.Fruits != null)
        {
            GameEntry.Fruits.ArchitectureStateChanged += OnArchitectureStateChanged;
        }

        _isEventSubscribed = true;
    }

    /// <summary>
    /// 取消订阅所有游戏事件。
    /// </summary>
    private void UnsubscribeEvents()
    {
        if (!_isEventSubscribed)
        {
            return;
        }

        if (GameEntry.EggHatch != null)
        {
            GameEntry.EggHatch.HatchStateChanged -= OnHatchStateChanged;
        }

        if (GameEntry.PetDiningOrders != null)
        {
            GameEntry.PetDiningOrders.CoinDropRequested -= OnCoinDropRequested;
        }

        if (GameEntry.Fruits != null)
        {
            GameEntry.Fruits.ArchitectureStateChanged -= OnArchitectureStateChanged;
        }

        _isEventSubscribed = false;
    }

    /// <summary>
    /// 孵化状态变化事件回调。
    /// 更新所有 HatchComplete 类型任务的进度。
    /// </summary>
    private void OnHatchStateChanged()
    {
        bool changed = EvaluateConditionProgress(TaskCondition.HatchComplete);
        if (changed)
        {
            TaskProgressChanged?.Invoke();
        }
    }

    /// <summary>
    /// 宠物喂养完成事件回调（每次宠物吃完食物产出金币时触发）。
    /// 递增 FeedComplete 类型任务，并把本次宠物吐出的金币数累计到 GoldTotal 类型任务。
    /// </summary>
    /// <param name="petInstanceId">宠物实例 Id（未使用）。</param>
    /// <param name="coinAmount">本次宠物吐出的金币数量；只有该值会计入 GoldTotal。</param>
    private void OnCoinDropRequested(int petInstanceId, int coinAmount)
    {
        if (_taskRows == null)
        {
            return;
        }

        bool changed = false;
        int safeCoinAmount = Math.Max(0, coinAmount);
        for (int i = 0; i < _taskRows.Length; i++)
        {
            if (_taskRows[i] == null)
            {
                continue;
            }

            // 已领取的任务不再累计
            if (_claimedTimestamps[i] > 0)
            {
                continue;
            }

            if (_taskRows[i].ConditionType == TaskCondition.FeedComplete)
            {
                _progressValues[i]++;
                changed = true;
                continue;
            }

            if (_taskRows[i].ConditionType == TaskCondition.GoldTotal && safeCoinAmount > 0)
            {
                long nextProgress = (long)_progressValues[i] + safeCoinAmount;
                _progressValues[i] = nextProgress >= int.MaxValue ? int.MaxValue : (int)nextProgress;
                changed = true;
            }
        }

        if (changed)
        {
            GameEntry.CloudSave?.MarkDirty(CloudSaveDirtyModule.Tasks);
            TaskProgressChanged?.Invoke();
        }
    }

    /// <summary>
    /// 建筑状态变化事件回调。
    /// 更新所有建筑槽位类型任务的进度（HatchSlotCount / DietSlotCount / FruiterSlotCount）。
    /// </summary>
    private void OnArchitectureStateChanged()
    {
        bool changed = false;
        changed |= EvaluateConditionProgress(TaskCondition.HatchSlotCount);
        changed |= EvaluateConditionProgress(TaskCondition.DietSlotCount);
        changed |= EvaluateConditionProgress(TaskCondition.FruiterSlotCount);

        if (changed)
        {
            TaskProgressChanged?.Invoke();
        }
    }

    /// <summary>
    /// 遍历所有任务，对指定条件类型的任务重新评估进度。
    /// </summary>
    /// <param name="condition">要评估的条件类型。</param>
    /// <returns>是否有任务的进度发生变化。</returns>
    private bool EvaluateConditionProgress(TaskCondition condition)
    {
        if (_taskRows == null || GameEntry.Fruits == null)
        {
            return false;
        }

        bool changed = false;
        for (int i = 0; i < _taskRows.Length; i++)
        {
            if (_taskRows[i] == null || _taskRows[i].ConditionType != condition)
            {
                continue;
            }

            // 已领取的任务不再更新进度
            if (_claimedTimestamps[i] > 0)
            {
                continue;
            }

            int newProgress = ReadConditionValue(condition);
            if (_progressValues[i] != newProgress)
            {
                _progressValues[i] = newProgress;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// 读取指定条件类型的当前实际值。
    /// FeedComplete 和 GoldTotal 都由宠物吐金币事件递增，不从 CurrentGold 反推。
    /// </summary>
    /// <param name="condition">条件类型。</param>
    /// <returns>当前实际值；依赖不可用时返回 0。</returns>
    private int ReadConditionValue(TaskCondition condition)
    {
        switch (condition)
        {
            case TaskCondition.HatchComplete:
                return GameEntry.EggHatch != null ? GameEntry.EggHatch.TotalHatchCount : 0;

            case TaskCondition.HatchSlotCount:
                return GameEntry.Fruits != null ? GameEntry.Fruits.HatchSlotCount : 0;

            case TaskCondition.DietSlotCount:
                return GameEntry.Fruits != null ? GameEntry.Fruits.DiningSeatCount : 0;

            case TaskCondition.FruiterSlotCount:
                return GameEntry.Fruits != null ? GameEntry.Fruits.OrchardSlotCount : 0;

            // FeedComplete / GoldTotal 由宠物吐金币事件递增驱动，不走 ReadConditionValue。
            default:
                return 0;
        }
    }

    /// <summary>
    /// 评估所有任务的进度（用于初始化或读档后全量刷新）。
    /// </summary>
    private void EvaluateAllProgress()
    {
        if (_taskRows == null)
        {
            return;
        }

        for (int i = 0; i < _taskRows.Length; i++)
        {
            if (_taskRows[i] == null)
            {
                continue;
            }

            // 已领取的任务跳过进度评估
            if (_claimedTimestamps[i] > 0)
            {
                continue;
            }

            TaskCondition condition = _taskRows[i].ConditionType;
            if (condition == TaskCondition.FeedComplete || condition == TaskCondition.GoldTotal)
            {
                // FeedComplete / GoldTotal 由事件递增，不从运行时读取。
                continue;
            }

            _progressValues[i] = ReadConditionValue(condition);
        }
    }

    /// <summary>
    /// 按 Code 查找任务在数组中的下标。
    /// </summary>
    /// <param name="code">任务 Code。</param>
    /// <returns>找到的下标；未找到返回 -1。</returns>
    private int FindTaskIndexByCode(string code)
    {
        if (_taskRows == null || string.IsNullOrWhiteSpace(code))
        {
            return -1;
        }

        for (int i = 0; i < _taskRows.Length; i++)
        {
            if (_taskRows[i] != null && string.Equals(_taskRows[i].Code, code, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
/// 任务状态枚举。
/// </summary>
public enum TaskStatus
{
    /// <summary>
    /// 进度未达到目标，尚不可领取。
    /// </summary>
    NotCompleted = 0,

    /// <summary>
    /// 进度已达到目标，可以领取奖励。
    /// </summary>
    Claimable = 1,

    /// <summary>
    /// 奖励已经领取。
    /// </summary>
    Claimed = 2,
}

/// <summary>
/// 任务云存档数据。
/// 每条记录对应一个任务的领取状态和进度。
/// </summary>
[Serializable]
public sealed class TaskSaveData
{
    /// <summary>
    /// 任务 Code，与 TaskDataRow.Code 对应。
    /// </summary>
    public string code = string.Empty;

    /// <summary>
    /// 领取时间戳（UTC Ticks）。
    /// 0 表示未领取；大于 0 表示已领取且值为领取时刻。
    /// </summary>
    public long claimedAt;

    /// <summary>
    /// 任务当前进度值。
    /// 用于离线后再恢复，避免 FeedComplete / GoldTotal 等事件驱动型任务的进度丢失。
    /// </summary>
    public int progress;
}
