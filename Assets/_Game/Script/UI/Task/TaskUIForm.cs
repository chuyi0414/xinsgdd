using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 任务界面。
/// 负责展示 8 个固定任务的进度、状态和领取操作。
/// ScrollView 对象池模式：OnInit 时克隆 8 个条目，后续刷新只切换数据。
///
/// 排序规则（从前到后）：
///   1. 可领取（Claimable）的任务
///   2. 未完成（NotCompleted）的任务
///   3. 已领取（Claimed）的任务
///      - 已领取区间内：最新领取的排在前面，最早领取的排在最后
/// </summary>
public sealed class TaskUIForm : UIFormLogic
{
    /// <summary>
    /// 关闭按钮。
    /// 由 Inspector 手动拖入，不在运行时按节点名查找。
    /// </summary>
    [SerializeField]
    private Button _btnClose;

    /// <summary>
    /// ScrollView 的 Content 节点。
    /// 由 Inspector 手动拖入，运行时把条目模板克隆到该节点下。
    /// </summary>
    [SerializeField]
    private Transform _taskContent;

    /// <summary>
    /// 任务条目模板。
    /// 模板根节点需要挂 TaskItemView，并由 Inspector 手动拖入内部字段。
    /// 运行时仅作为克隆源，不加入对象池。
    /// </summary>
    [SerializeField]
    private TaskItemView _taskItemTemplate;

    /// <summary>
    /// 任务条目对象池。
    /// 打开界面后创建 8 个克隆条目（与任务总数一致），后续刷新只切换数据。
    /// 该池不包含隐藏模板本体。
    /// </summary>
    private readonly List<TaskItemView> _taskItemViews = new List<TaskItemView>(8);

    /// <summary>
    /// 排序用的临时索引列表。
    /// 避免每次刷新都 new 新列表。
    /// </summary>
    private readonly List<int> _sortedIndices = new List<int>(8);

    /// <summary>
    /// 初始化界面引用并绑定按钮事件。
    /// </summary>
    /// <param name="userData">界面打开附加参数。</param>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        BindCloseButton();
        EnsureTaskItemPool();
    }

    /// <summary>
    /// 页面打开时刷新任务列表，并订阅进度变化事件。
    /// </summary>
    /// <param name="userData">界面打开附加参数。</param>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        SubscribeTaskEvents();
        RefreshTaskList();
    }

    /// <summary>
    /// 界面关闭时取消事件订阅。
    /// </summary>
    /// <param name="isShutdown">是否为关闭界面管理器时触发。</param>
    /// <param name="userData">界面关闭附加参数。</param>
    protected override void OnClose(bool isShutdown, object userData)
    {
        UnsubscribeTaskEvents();
        base.OnClose(isShutdown, userData);
    }

    // ──────────────────────────────────────────────────────────
    //  对象池与 UI 初始化
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 确保任务条目池已经创建（恰好 8 个条目对应 8 个固定任务）。
    /// </summary>
    private void EnsureTaskItemPool()
    {
        if (_taskContent == null || _taskItemTemplate == null)
        {
            return;
        }

        // 隐藏模板本体
        _taskItemTemplate.SetActive(false);

        int taskCount = GameEntry.Tasks != null ? GameEntry.Tasks.TaskCount : 8;
        while (_taskItemViews.Count < taskCount)
        {
            TaskItemView item = Instantiate(_taskItemTemplate, _taskContent, false);
            item.name = _taskItemTemplate.name;
            item.SetActive(false);
            _taskItemViews.Add(item);
        }

        // 初始全部隐藏
        SetTaskItemsActive(0);
    }

    /// <summary>
    /// 绑定关闭按钮点击事件。
    /// </summary>
    private void BindCloseButton()
    {
        if (_btnClose == null)
        {
            return;
        }

        _btnClose.onClick.RemoveListener(OnBtnClose);
        _btnClose.onClick.AddListener(OnBtnClose);
    }

    /// <summary>
    /// 关闭按钮点击回调。
    /// </summary>
    private void OnBtnClose()
    {
        UIInteractionSound.PlayClick();
        if (UIForm != null && GameEntry.UI != null)
        {
            GameEntry.UI.CloseUIForm(UIForm.SerialId);
        }
    }

    // ──────────────────────────────────────────────────────────
    //  事件订阅
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 是否已订阅任务进度变化事件。
    /// </summary>
    private bool _isSubscribed;

    /// <summary>
    /// 订阅任务进度变化事件。
    /// 进度或状态变化时自动刷新列表。
    /// </summary>
    private void SubscribeTaskEvents()
    {
        if (_isSubscribed || GameEntry.Tasks == null)
        {
            return;
        }

        GameEntry.Tasks.TaskProgressChanged += OnTaskProgressChanged;
        _isSubscribed = true;
    }

    /// <summary>
    /// 取消订阅任务进度变化事件。
    /// </summary>
    private void UnsubscribeTaskEvents()
    {
        if (!_isSubscribed || GameEntry.Tasks == null)
        {
            return;
        }

        GameEntry.Tasks.TaskProgressChanged -= OnTaskProgressChanged;
        _isSubscribed = false;
    }

    /// <summary>
    /// 任务进度或状态变化回调。
    /// 直接刷新整个任务列表。
    /// </summary>
    private void OnTaskProgressChanged()
    {
        RefreshTaskList();
    }

    // ──────────────────────────────────────────────────────────
    //  列表刷新与排序
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 刷新任务列表。
    /// 按照排序规则重新排列条目，然后逐个绑定数据。
    /// 排序规则：
    ///   1. Claimable（可领取）— 最靠前
    ///   2. NotCompleted（未完成）— 中间
    ///   3. Claimed（已领取）— 最靠后
    ///      已领取区间内：最新领取的排前面，最早领取的排最后
    /// </summary>
    private void RefreshTaskList()
    {
        if (GameEntry.Tasks == null)
        {
            return;
        }

        int taskCount = GameEntry.Tasks.TaskCount;
        if (taskCount <= 0)
        {
            SetTaskItemsActive(0);
            return;
        }

        // 构建排序索引
        _sortedIndices.Clear();
        for (int i = 0; i < taskCount; i++)
        {
            _sortedIndices.Add(i);
        }

        // 自定义排序
        _sortedIndices.Sort(CompareTaskIndices);

        // 绑定数据到条目视图
        int visibleCount = Math.Min(taskCount, _taskItemViews.Count);
        for (int i = 0; i < visibleCount; i++)
        {
            int taskIndex = _sortedIndices[i];
            TaskDataRow row = GameEntry.Tasks.GetTaskRow(taskIndex);
            if (row == null)
            {
                continue;
            }

            TaskStatus status = GameEntry.Tasks.GetStatus(taskIndex);
            int progress = GameEntry.Tasks.GetProgress(taskIndex);

            _taskItemViews[i].SetActive(true);
            _taskItemViews[i].Refresh(
                taskIndex,
                row.TaskNumber,
                row.Name,
                progress,
                row.TargetCount,
                row.AwardGold,
                status,
                OnClaimTask);
        }

        // 隐藏多余条目
        SetTaskItemsActive(visibleCount);
    }

    /// <summary>
    /// 任务排序比较器。
    /// 排序优先级：Claimable &gt; NotCompleted &gt; Claimed。
    /// 已领取区间内：最新领取（claimedAt 较大）排在前面。
    /// </summary>
    /// <param name="a">第一个任务下标。</param>
    /// <param name="b">第二个任务下标。</param>
    /// <returns>比较结果。</returns>
    private int CompareTaskIndices(int a, int b)
    {
        TaskStatus statusA = GameEntry.Tasks.GetStatus(a);
        TaskStatus statusB = GameEntry.Tasks.GetStatus(b);

        // 状态优先级：Claimable=0, NotCompleted=1, Claimed=2
        int priorityA = GetStatusPriority(statusA);
        int priorityB = GetStatusPriority(statusB);

        if (priorityA != priorityB)
        {
            return priorityA.CompareTo(priorityB);
        }

        // 同状态内部排序
        if (statusA == TaskStatus.Claimed)
        {
            // 已领取：最新领取的排前面（claimedAt 降序）
            long claimedA = GameEntry.Tasks.GetClaimedTimestamp(a);
            long claimedB = GameEntry.Tasks.GetClaimedTimestamp(b);
            return claimedB.CompareTo(claimedA);
        }

        // NotCompleted / Claimable：保持数据表原始顺序
        return a.CompareTo(b);
    }

    /// <summary>
    /// 获取任务状态的排序优先级。
    /// 数字越小越靠前。
    /// </summary>
    /// <param name="status">任务状态。</param>
    /// <returns>排序优先级值。</returns>
    private static int GetStatusPriority(TaskStatus status)
    {
        switch (status)
        {
            case TaskStatus.Claimable:
                return 0;
            case TaskStatus.NotCompleted:
                return 1;
            case TaskStatus.Claimed:
                return 2;
            default:
                return 3;
        }
    }

    /// <summary>
    /// 设置对象池中前 N 个条目可见，其余隐藏。
    /// </summary>
    /// <param name="visibleCount">需要显示的条目数量。</param>
    private void SetTaskItemsActive(int visibleCount)
    {
        for (int i = 0; i < _taskItemViews.Count; i++)
        {
            _taskItemViews[i].SetActive(i < visibleCount);
        }
    }

    // ──────────────────────────────────────────────────────────
    //  领取操作
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 领取任务奖励回调。
    /// 由 TaskItemView 的按钮点击事件触发。
    /// </summary>
    /// <param name="taskIndex">任务下标（0-based）。</param>
    private void OnClaimTask(int taskIndex)
    {
        if (GameEntry.Tasks == null)
        {
            return;
        }

        // TryClaim 内部已经处理了：发放金币、记录时间戳、触发事件、Toast 提示
        GameEntry.Tasks.TryClaim(taskIndex);
    }
}
