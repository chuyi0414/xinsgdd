using GameFramework.Event;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 退出确认界面。
/// 职责：
/// 1. BtnYes —— 确认退出战斗，先请求打开 VictoryFailUIForm(Fail)，
///    待结果窗真正打开成功后，再关闭本窗体；
/// 2. BtnNo  —— 取消退出，仅关闭本窗体，战斗继续。
/// </summary>
public sealed class IsExitUIForm : UIFormLogic
{
    /// <summary>
    /// 确认退出按钮。
    /// 点击后请求显示失败结算窗；待结算窗打开成功后关闭本窗体。
    /// </summary>
    [SerializeField]
    private Button _btnYes;

    /// <summary>
    /// 取消退出按钮。
    /// 点击后仅关闭本窗体，战斗继续。
    /// </summary>
    [SerializeField]
    private Button _btnNo;

    /// <summary>
    /// 当前待打开的失败结算窗数据。
    /// 作为本次打开请求的唯一引用锚点，用于在打开成功/失败事件里精确匹配
    /// 当前这一次 VictoryFailUIForm 打开请求。
    /// 初始状态为 null，表示当前没有待完成的结果窗打开流程。
    /// </summary>
    private VictoryFailUIData _pendingVictoryFailUIData;

    /// <summary>
    /// 当前待打开的 VictoryFailUIForm 序列号。
    /// 用于记录本次打开请求返回的 SerialId，便于同步打开场景下立即判断
    /// 结果窗是否已经存在。
    /// 初始状态为 0，表示当前没有待跟踪的结果窗实例。
    /// </summary>
    private int _pendingVictoryFailUIFormId;

    /// <summary>
    /// 当前是否已经订阅 VictoryFailUIForm 的打开成功/失败事件。
    /// 用于避免重复订阅或重复反订阅。
    /// 初始状态为 false。
    /// </summary>
    private bool _isListeningVictoryFailOpenEvent;

    /// <summary>
    /// 初始化退出确认界面，绑定按钮事件。
    /// </summary>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        if (_btnYes != null)
        {
            _btnYes.onClick.RemoveListener(OnBtnYes);
            _btnYes.onClick.AddListener(OnBtnYes);
        }

        if (_btnNo != null)
        {
            _btnNo.onClick.RemoveListener(OnBtnNo);
            _btnNo.onClick.AddListener(OnBtnNo);
        }
    }

    /// <summary>
    /// 关闭界面时清理等待 VictoryFailUIForm 打开的临时状态。
    /// 避免本窗体被外部关闭后仍然残留事件订阅或按钮锁定状态。
    /// </summary>
    /// <param name="isShutdown">是否为关闭界面管理器时触发。</param>
    /// <param name="userData">用户自定义数据。</param>
    protected override void OnClose(bool isShutdown, object userData)
    {
        // 无论是主动关闭还是被 CombatUIForm 连带关闭，都要把等待态彻底清理干净。
        ClearPendingVictoryFailOpenState();
        base.OnClose(isShutdown, userData);
    }

    /// <summary>
    /// 销毁时移除按钮监听。
    /// </summary>
    private void OnDestroy()
    {
        if (_btnYes != null)
        {
            _btnYes.onClick.RemoveListener(OnBtnYes);
        }

        if (_btnNo != null)
        {
            _btnNo.onClick.RemoveListener(OnBtnNo);
        }
    }

    /// <summary>
    /// 确认退出按钮点击回调。
    /// 职责：
    /// 1. 发起打开 VictoryFailUIForm(Fail)；
    /// 2. 等待结果窗真正打开成功后，再关闭当前确认窗；
    /// 3. 若打开失败，则恢复按钮可点击状态并保留当前确认窗。
    /// </summary>
    private void OnBtnYes()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();

        // 若 UI 系统不可用，或当前已经有一个待完成的结果窗打开流程，则直接跳过。
        // 避免重复点击导致重复订阅事件、重复打开结果窗。
        if (GameEntry.UI == null || _pendingVictoryFailUIData != null)
        {
            return;
        }

        // 先锁住 Yes / No 按钮，防止等待结果窗打开期间再次点击造成状态错乱。
        SetButtonsInteractable(false);

        // 先订阅打开成功/失败事件。
        // ⚠️ 避坑：底层 UI 框架在同步打开场景里，成功/失败事件可能在 OpenUIForm 返回前就抛出，
        // 所以必须先订阅，再发起打开请求，不能反过来。
        SubscribeVictoryFailOpenEvents();

        // 计算本次失败结算需要显示的当前分数，并构造唯一的 userData 对象。
        // 后续通过 ReferenceEquals(ne.UserData, _pendingVictoryFailUIData) 精确匹配本次请求。
        var controller = EliminateCardController.Instance;
        int failScore = controller != null ? controller.GetCurrentScore() : 0;
        _pendingVictoryFailUIData = new VictoryFailUIData(false, failScore);

        // 优先委托 CombatUIForm 打开结果窗，保证 VictoryFailUIForm 仍由 CombatUIForm 统一跟踪。
        // 若极端情况下取不到 CombatUIForm，则直接兜底打开结果窗，避免按钮点击无响应。
        UIForm combatUIForm = GameEntry.UI != null
            ? GameEntry.UI.GetUIForm(UIFormDefine.CombatUIForm)
            : null;
        if (combatUIForm != null && combatUIForm.Logic is CombatUIForm combatForm)
        {
            _pendingVictoryFailUIFormId = combatForm.OpenVictoryFailUIForm(false, _pendingVictoryFailUIData);
        }
        else
        {
            _pendingVictoryFailUIFormId = GameEntry.UI.OpenUIForm(UIFormDefine.VictoryFailUIForm, UIFormDefine.MainGroup, _pendingVictoryFailUIData);
        }

        // 若同步打开成功/失败事件已经在 OpenUIForm 返回前把等待态清掉，
        // 此处直接结束，避免使用已经失效的等待态字段继续往下判断。
        if (_pendingVictoryFailUIData == null)
        {
            return;
        }

        // 若打开请求没有返回有效序列号，说明本次打开流程未成功发起，恢复等待态并退出。
        if (_pendingVictoryFailUIFormId <= 0)
        {
            ClearPendingVictoryFailOpenState();
            return;
        }

        // 若当前结果窗已经存在，说明是同步打开成功场景。
        // 这时无需再等待事件回调，直接关闭自身即可。
        if (GameEntry.UI.HasUIForm(_pendingVictoryFailUIFormId))
        {
            ClearPendingVictoryFailOpenState();
            CloseSelfIfNeeded();
        }
    }

    /// <summary>
    /// 取消退出按钮点击回调。
    /// 仅关闭本窗体，战斗继续。
    /// </summary>
    private void OnBtnNo()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();

        // 若正在等待失败结算窗打开，则禁止关闭自己。
        // 这样可以保证“VictoryFailUIForm 出来后，IsExitUIForm 才关闭”的时序要求不被破坏。
        if (_pendingVictoryFailUIData != null)
        {
            return;
        }
        
        if (UIForm != null && GameEntry.UI != null)
        {
            GameEntry.UI.CloseUIForm(UIForm.SerialId);
        }
    }

    /// <summary>
    /// VictoryFailUIForm 打开成功事件回调。
    /// 仅处理与当前待打开请求完全匹配的 userData；
    /// 匹配成功后关闭 IsExitUIForm，并清理等待态。
    /// </summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">打开界面成功事件参数。</param>
    private void OnOpenVictoryFailUIFormSuccess(object sender, GameEventArgs e)
    {
        OpenUIFormSuccessEventArgs ne = (OpenUIFormSuccessEventArgs)e;

        // 仅响应当前这一次失败结算窗打开请求。
        // ⚠️ 避坑：不能只按资源名匹配，否则可能误伤别处打开的同名 UI。
        if (_pendingVictoryFailUIData == null || ne.UIForm == null || !ReferenceEquals(ne.UserData, _pendingVictoryFailUIData))
        {
            return;
        }

        // 记录真正打开成功的 SerialId，便于后续调试或状态追踪。
        _pendingVictoryFailUIFormId = ne.UIForm.SerialId;

        // 先清理等待态，再关闭自身。
        // 这样即使 CloseSelfIfNeeded 触发 OnClose，也不会重复残留事件订阅。
        ClearPendingVictoryFailOpenState();
        CloseSelfIfNeeded();
    }

    /// <summary>
    /// VictoryFailUIForm 打开失败事件回调。
    /// 仅处理当前待打开请求；若失败，则恢复按钮状态并保留当前确认窗。
    /// </summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">打开界面失败事件参数。</param>
    private void OnOpenVictoryFailUIFormFailure(object sender, GameEventArgs e)
    {
        OpenUIFormFailureEventArgs ne = (OpenUIFormFailureEventArgs)e;

        // 仅响应当前这一次失败结算窗打开请求的失败回调。
        if (_pendingVictoryFailUIData == null || !ReferenceEquals(ne.UserData, _pendingVictoryFailUIData))
        {
            return;
        }

        // 打开失败时，不关闭当前确认窗，而是恢复等待态与按钮交互，让玩家仍可继续操作。
        ClearPendingVictoryFailOpenState();
    }

    /// <summary>
    /// 订阅 VictoryFailUIForm 的打开成功/失败事件。
    /// 用于在结果窗真正打开成功后关闭当前确认窗，或在打开失败时恢复按钮状态。
    /// </summary>
    private void SubscribeVictoryFailOpenEvents()
    {
        if (_isListeningVictoryFailOpenEvent || GameEntry.Event == null)
        {
            return;
        }

        // 同时监听成功与失败，保证这次打开请求无论哪条分支结束，都能正确收口状态。
        GameEntry.Event.Subscribe(OpenUIFormSuccessEventArgs.EventId, OnOpenVictoryFailUIFormSuccess);
        GameEntry.Event.Subscribe(OpenUIFormFailureEventArgs.EventId, OnOpenVictoryFailUIFormFailure);
        _isListeningVictoryFailOpenEvent = true;
    }

    /// <summary>
    /// 取消订阅 VictoryFailUIForm 的打开成功/失败事件。
    /// 防止 IsExitUIForm 关闭后仍然接收事件回调。
    /// </summary>
    private void UnsubscribeVictoryFailOpenEvents()
    {
        if (!_isListeningVictoryFailOpenEvent || GameEntry.Event == null)
        {
            return;
        }

        // 对称移除成功/失败监听，防止重复回调或对象回收后访问失效实例。
        GameEntry.Event.Unsubscribe(OpenUIFormSuccessEventArgs.EventId, OnOpenVictoryFailUIFormSuccess);
        GameEntry.Event.Unsubscribe(OpenUIFormFailureEventArgs.EventId, OnOpenVictoryFailUIFormFailure);
        _isListeningVictoryFailOpenEvent = false;
    }

    /// <summary>
    /// 清理等待 VictoryFailUIForm 打开的全部临时状态。
    /// 包括：事件订阅、待匹配的 userData、待跟踪的 SerialId，以及按钮交互状态。
    /// </summary>
    private void ClearPendingVictoryFailOpenState()
    {
        // 先取消事件监听，确保后续重置字段时不会再收到旧回调。
        UnsubscribeVictoryFailOpenEvents();

        // 清空本次打开请求的匹配锚点与序列号，表示当前已经不再等待任何结果窗打开。
        _pendingVictoryFailUIData = null;
        _pendingVictoryFailUIFormId = 0;

        // 恢复按钮交互，允许玩家继续点击 Yes / No。
        SetButtonsInteractable(true);
    }

    /// <summary>
    /// 若当前确认窗仍然处于打开状态，则关闭自身。
    /// 仅在 VictoryFailUIForm 已经真正打开成功后调用。
    /// </summary>
    private void CloseSelfIfNeeded()
    {
        // 防御式判断：仅当本窗体仍然存在且仍被 UI 系统持有时才执行关闭。
        if (UIForm == null || GameEntry.UI == null || !GameEntry.UI.HasUIForm(UIForm.SerialId))
        {
            return;
        }

        GameEntry.UI.CloseUIForm(UIForm.SerialId);
    }

    /// <summary>
    /// 设置 Yes / No 按钮的可交互状态。
    /// 用于等待结果窗打开期间临时锁定按钮，或在流程结束后恢复点击。
    /// </summary>
    /// <param name="interactable">true=可点击；false=不可点击。</param>
    private void SetButtonsInteractable(bool interactable)
    {
        if (_btnYes != null)
        {
            _btnYes.interactable = interactable;
        }

        if (_btnNo != null)
        {
            _btnNo.interactable = interactable;
        }
    }
}
