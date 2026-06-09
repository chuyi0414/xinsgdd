using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// MainUIForm 任务实时进度分部类。
/// 在主界面上显示当前最优先任务的实时进度（名称 + 进度文字 + 进度条），
/// 点击该区域时打开任务界面（TaskUIForm）。
///
/// 显示逻辑：
///   - 优先显示"可领取"的任务（最靠前的一条）
///   - 没有可领取的，显示"未完成"中数据表顺序最靠前的一条
///   - 全部已领取时隐藏整个挂件
/// </summary>
public partial class MainUIForm
{
    /// <summary>
    /// 任务进度挂件根节点。
    /// 全部任务已领取时隐藏。由 Inspector 手动拖入。
    /// </summary>
    [SerializeField]
    private GameObject _taskProgressWidget;

    /// <summary>
    /// 任务进度挂件的点击按钮（覆盖整个挂件区域）。
    /// 点击时打开 TaskUIForm。由 Inspector 手动拖入。
    /// </summary>
    [SerializeField]
    private Button _btnTaskProgress;

    /// <summary>
    /// 任务名称文本。由 Inspector 手动拖入。
    /// </summary>
    [SerializeField]
    private TMP_Text _txtTaskProgressName;

    /// <summary>
    /// 任务进度文字（如 "3/10"）。由 Inspector 手动拖入。
    /// </summary>
    [SerializeField]
    private TMP_Text _txtTaskProgressValue;

    /// <summary>
    /// 任务进度条（Image filled 类型）。由 Inspector 手动拖入。
    /// 通过 fillAmount 显示完成比例。
    /// </summary>
    [SerializeField]
    private Image _imgTaskProgressBar;

    /// <summary>
    /// 是否已订阅任务进度变化事件。
    /// </summary>
    private bool _isTaskProgressSubscribed;

    /// <summary>
    /// 初始化任务进度挂件。
    /// 在 InitializeRuntimeViewsIfNeeded 中调用。
    /// </summary>
    private void InitializeTaskProgressView()
    {
        if (_btnTaskProgress != null)
        {
            _btnTaskProgress.onClick.RemoveListener(OnBtnTaskProgressClicked);
            _btnTaskProgress.onClick.AddListener(OnBtnTaskProgressClicked);
        }

        SubscribeTaskProgressEvents();
        RefreshTaskProgressWidget();
    }

    /// <summary>
    /// 关闭任务进度挂件（OnClose 中调用）。
    /// </summary>
    private void CloseTaskProgressView()
    {
        UnsubscribeTaskProgressEvents();
    }

    /// <summary>
    /// 销毁任务进度挂件（OnDestroy 中调用）。
    /// </summary>
    private void DestroyTaskProgressView()
    {
        if (_btnTaskProgress != null)
        {
            _btnTaskProgress.onClick.RemoveListener(OnBtnTaskProgressClicked);
        }

        UnsubscribeTaskProgressEvents();
    }

    // ──────────────────────────────────────────────────────────
    //  事件订阅
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 订阅 TaskModule.TaskProgressChanged 事件。
    /// </summary>
    private void SubscribeTaskProgressEvents()
    {
        if (_isTaskProgressSubscribed || GameEntry.Tasks == null)
        {
            return;
        }

        GameEntry.Tasks.TaskProgressChanged += OnTaskProgressChangedForWidget;
        _isTaskProgressSubscribed = true;
    }

    /// <summary>
    /// 取消订阅 TaskModule.TaskProgressChanged 事件。
    /// </summary>
    private void UnsubscribeTaskProgressEvents()
    {
        if (!_isTaskProgressSubscribed || GameEntry.Tasks == null)
        {
            return;
        }

        GameEntry.Tasks.TaskProgressChanged -= OnTaskProgressChangedForWidget;
        _isTaskProgressSubscribed = false;
    }

    /// <summary>
    /// 任务进度变化回调，刷新挂件。
    /// </summary>
    private void OnTaskProgressChangedForWidget()
    {
        RefreshTaskProgressWidget();
    }

    // ──────────────────────────────────────────────────────────
    //  挂件刷新
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 刷新任务进度挂件显示。
    /// 优先显示可领取的任务，其次显示未完成的任务，全部已领取时隐藏。
    /// </summary>
    private void RefreshTaskProgressWidget()
    {
        if (GameEntry.Tasks == null || GameEntry.Tasks.TaskCount <= 0)
        {
            SetTaskProgressWidgetVisible(false);
            return;
        }

        // 查找最优先显示的任务
        int displayIndex = FindPriorityTaskIndex();
        if (displayIndex < 0)
        {
            // 没有可显示的任务（全部已领取）
            SetTaskProgressWidgetVisible(false);
            return;
        }

        SetTaskProgressWidgetVisible(true);

        TaskDataRow row = GameEntry.Tasks.GetTaskRow(displayIndex);
        if (row == null)
        {
            return;
        }

        int progress = GameEntry.Tasks.GetProgress(displayIndex);
        int displayProgress = Math.Min(progress, row.TargetCount);

        // 任务名称
        if (_txtTaskProgressName != null)
        {
            _txtTaskProgressName.text = row.Name ?? string.Empty;
        }

        // 进度文字
        if (_txtTaskProgressValue != null)
        {
            _txtTaskProgressValue.text = $"{displayProgress}/{row.TargetCount}";
        }

        // 进度条
        if (_imgTaskProgressBar != null)
        {
            float fillAmount = row.TargetCount > 0 ? (float)displayProgress / row.TargetCount : 0f;
            _imgTaskProgressBar.fillAmount = fillAmount;
        }
    }

    /// <summary>
    /// 查找最优先显示的任务下标。
    /// 优先级：Claimable > NotCompleted > Claimed（已领取不算）。
    /// </summary>
    /// <returns>找到的任务下标；无可用任务时返回 -1。</returns>
    private int FindPriorityTaskIndex()
    {
        int taskCount = GameEntry.Tasks.TaskCount;
        int firstNotCompleted = -1;

        for (int i = 0; i < taskCount; i++)
        {
            TaskStatus status = GameEntry.Tasks.GetStatus(i);

            // 可领取的最优先
            if (status == TaskStatus.Claimable)
            {
                return i;
            }

            // 记录第一个未完成的
            if (status == TaskStatus.NotCompleted && firstNotCompleted < 0)
            {
                firstNotCompleted = i;
            }
        }

        return firstNotCompleted;
    }

    /// <summary>
    /// 设置任务进度挂件显隐。
    /// </summary>
    /// <param name="visible">是否显示。</param>
    private void SetTaskProgressWidgetVisible(bool visible)
    {
        if (_taskProgressWidget != null)
        {
            _taskProgressWidget.SetActive(visible);
        }
    }

    // ──────────────────────────────────────────────────────────
    //  点击事件
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 任务进度挂件点击回调。
    /// 打开任务界面（复用 _taskUIFormId 防止重复打开）。
    /// </summary>
    private void OnBtnTaskProgressClicked()
    {
        TryOpenTaskUIForm();
    }
}
