using TMPro;
using UnityEngine;

/// <summary>
/// MainUIForm 星星视图分部类。
/// 负责把 PlayerRuntimeModule.CurrentStars 实时渲染到主界面 GoJB/TxtDJ 文本上。
/// 与 MainUIForm.Gold.cs 完全对称：OnInit 阶段订阅事件 + 一次性刷新；OnOpen 阶段再刷一次保证显式打开后视图同步；OnDestroy 阶段释放订阅。
/// </summary>
public partial class MainUIForm
{
    /// <summary>
    /// 当前星星总额文本（对应主界面 GoJB/TxtDJ）。
    /// 用户在 Inspector 中自行拖入 TxtDJ 上的 TextMeshProUGUI 组件。
    /// 该字段缺失时不会报错，仅刷新被跳过，避免阻塞主界面其余流程。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _starsText;

    /// <summary>
    /// 当前是否已经订阅 PlayerRuntimeModule.StarsChanged 事件。
    /// 防止 OnInit 后再次手动调用造成重复 +=。
    /// </summary>
    private bool _isStarsEventSubscribed;

    /// <summary>
    /// 星星视图初始化：订阅事件并刷新一次。
    /// 由 MainUIForm.cs 的 OnInit 调用。
    /// </summary>
    private void InitializeStarsView()
    {
        EnsureStarsEventSubscription();
        RefreshStarsText();
    }

    /// <summary>
    /// 星星视图打开：刷新文本。
    /// 由 MainUIForm.cs 的 OnOpen 调用，确保界面被复用时仍能显示最新值。
    /// </summary>
    private void OpenStarsView()
    {
        RefreshStarsText();
    }

    /// <summary>
    /// 星星视图销毁：释放事件订阅。
    /// 由 MainUIForm.cs 的 OnDestroy 调用。
    /// </summary>
    private void DestroyStarsView()
    {
        ReleaseStarsEventSubscription();
    }

    /// <summary>
    /// 确保已订阅 StarsChanged 事件。
    /// 重复订阅会被 _isStarsEventSubscribed 拦截。
    /// </summary>
    private void EnsureStarsEventSubscription()
    {
        if (_isStarsEventSubscribed)
        {
            return;
        }

        if (GameEntry.Fruits != null)
        {
            GameEntry.Fruits.StarsChanged += OnStarsChanged;
        }

        _isStarsEventSubscribed = true;
    }

    /// <summary>
    /// 释放 StarsChanged 事件订阅。
    /// </summary>
    private void ReleaseStarsEventSubscription()
    {
        if (!_isStarsEventSubscribed)
        {
            return;
        }

        if (GameEntry.Fruits != null)
        {
            GameEntry.Fruits.StarsChanged -= OnStarsChanged;
        }

        _isStarsEventSubscribed = false;
    }

    /// <summary>
    /// 星星总额变化回调：直接刷新文本。
    /// </summary>
    /// <param name="newStars">最新星星总额。</param>
    private void OnStarsChanged(int newStars)
    {
        RefreshStarsText();
    }

    /// <summary>
    /// 把当前星星总额写入 TxtDJ。
    /// 1. _starsText 未拖入时直接返回，不报错。
    /// 2. PlayerRuntimeModule 未就绪时退化显示 0。
    /// 3. 使用 TMP 的 SetText(int) 重载，避免 ToString() 触发 GC。
    /// </summary>
    private void RefreshStarsText()
    {
        if (_starsText == null)
        {
            return;
        }

        int currentStars = GameEntry.Fruits != null ? GameEntry.Fruits.CurrentStars : 0;
        _starsText.SetText("{0}", currentStars);
    }
}
