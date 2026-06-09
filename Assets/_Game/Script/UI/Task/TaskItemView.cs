using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 任务条目视图组件。
/// 挂在 ScrollView Content 下的任务模板根节点上，所有字段由 Inspector 手动拖入。
/// 运行时由 TaskUIForm 驱动刷新，自身不做任何逻辑判断。
/// </summary>
public sealed class TaskItemView : MonoBehaviour
{
    /// <summary>
    /// 任务名称文本。
    /// 初始状态由预制体决定，运行时刷新为 TaskDataRow.Name。
    /// </summary>
    [SerializeField]
    private TMP_Text _txtTaskName;

    /// <summary>
    /// 任务编号文本（如 "1-1"、"1-2"）。
    /// 若预制体中没有该节点则忽略。
    /// </summary>
    [SerializeField]
    private TMP_Text _txtTaskNumber;

    /// <summary>
    /// 领取按钮。
    /// 初始状态由预制体决定，运行时绑定点击回调并根据状态控制 interactable。
    /// </summary>
    [SerializeField]
    private Button _btnClaim;

    /// <summary>
    /// 领取按钮上的文字文本。
    /// 运行时刷新为"未达成"/"领取"/"已领取"。
    /// </summary>
    [SerializeField]
    private TMP_Text _txtBtnClaim;

    /// <summary>
    /// 奖励图标 Image。
    /// 初始状态由预制体决定（默认金币图标），运行时保持不变。
    /// </summary>
    [SerializeField]
    private Image _imgAwardIcon;

    /// <summary>
    /// 奖励数量文本。
    /// 运行时刷新为 TaskDataRow.AwardGold 的字符串。
    /// </summary>
    [SerializeField]
    private TMP_Text _txtAwardCount;

    /// <summary>
    /// 进度条图片（Image filled 类型）。
    /// 通过 fillAmount 显示任务完成进度，若预制体中没有该节点则忽略。
    /// </summary>
    [SerializeField]
    private Image _imgProgressBar;

    /// <summary>
    /// 进度文本（可选）。
    /// 显示"当前进度/目标数量"格式，若预制体中没有该节点则忽略。
    /// </summary>
    [SerializeField]
    private TMP_Text _txtProgress;

    /// <summary>
    /// 当前条目对应的任务下标（0-based）。
    /// -1 表示该条目尚未绑定数据。
    /// </summary>
    private int _taskIndex = -1;

    /// <summary>
    /// 外部注入的领取回调。
    /// 参数为当前条目绑定的任务下标。
    /// </summary>
    private Action<int> _onClaimClicked;

    /// <summary>
    /// 设置条目根节点显隐。
    /// </summary>
    /// <param name="active">是否显示该条目。</param>
    public void SetActive(bool active)
    {
        gameObject.SetActive(active);
    }

    /// <summary>
    /// 刷新完整条目数据。
    /// </summary>
    /// <param name="taskIndex">任务下标（0-based）。</param>
    /// <param name="taskNumber">任务编号（如 "1-1"）。</param>
    /// <param name="taskName">任务名称。</param>
    /// <param name="progress">当前进度值。</param>
    /// <param name="targetCount">目标数量。</param>
    /// <param name="awardGold">奖励金币数量。</param>
    /// <param name="status">任务状态。</param>
    /// <param name="onClaimClicked">领取按钮点击回调，参数为任务下标。</param>
    public void Refresh(int taskIndex, string taskNumber, string taskName, int progress, int targetCount,
        int awardGold, TaskStatus status, Action<int> onClaimClicked)
    {
        _taskIndex = taskIndex;
        _onClaimClicked = onClaimClicked;

        // 任务编号（可选节点）
        if (_txtTaskNumber != null)
        {
            _txtTaskNumber.text = taskNumber ?? string.Empty;
        }

        // 任务名称
        if (_txtTaskName != null)
        {
            _txtTaskName.text = taskName ?? string.Empty;
        }

        // 奖励数量
        if (_txtAwardCount != null)
        {
            _txtAwardCount.text = awardGold.ToString();
        }

        // 进度文本（可选节点）
        int displayProgress = Math.Min(progress, targetCount);
        if (_txtProgress != null)
        {
            _txtProgress.text = $"{displayProgress}/{targetCount}";
        }

        // 进度条 fillAmount（0~1）
        if (_imgProgressBar != null)
        {
            float fillAmount = targetCount > 0 ? (float)displayProgress / targetCount : 0f;
            _imgProgressBar.fillAmount = fillAmount;
        }

        // 按钮状态与文字
        RefreshButton(status);

        // 绑定按钮点击事件（先移除再添加，防止重复绑定）
        BindButtonClick();
    }

    /// <summary>
    /// 根据任务状态刷新按钮文字与可交互性。
    /// NotCompleted → "未达成"，不可点击
    /// Claimable    → "领取"，可点击
    /// Claimed      → "已领取"，不可点击
    /// </summary>
    /// <param name="status">当前任务状态。</param>
    private void RefreshButton(TaskStatus status)
    {
        if (_btnClaim == null)
        {
            return;
        }

        switch (status)
        {
            case TaskStatus.NotCompleted:
                _btnClaim.interactable = false;
                if (_txtBtnClaim != null)
                {
                    _txtBtnClaim.text = "未达成";
                }
                break;

            case TaskStatus.Claimable:
                _btnClaim.interactable = true;
                if (_txtBtnClaim != null)
                {
                    _txtBtnClaim.text = "领取";
                }
                break;

            case TaskStatus.Claimed:
                _btnClaim.interactable = false;
                if (_txtBtnClaim != null)
                {
                    _txtBtnClaim.text = "已领取";
                }
                break;
        }
    }

    /// <summary>
    /// 绑定领取按钮点击事件。
    /// 先移除旧监听再添加，防止多次 Refresh 导致重复绑定。
    /// </summary>
    private void BindButtonClick()
    {
        if (_btnClaim == null)
        {
            return;
        }

        _btnClaim.onClick.RemoveListener(OnBtnClaimClicked);
        _btnClaim.onClick.AddListener(OnBtnClaimClicked);
    }

    /// <summary>
    /// 领取按钮点击回调。
    /// 将当前绑定的任务下标透传给外部注入的回调。
    /// </summary>
    private void OnBtnClaimClicked()
    {
        UIInteractionSound.PlayClick();
        if (_taskIndex >= 0)
        {
            _onClaimClicked?.Invoke(_taskIndex);
        }
    }
}
