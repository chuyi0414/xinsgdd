using System.Collections.Generic;
using GameFramework.Event;
using UnityGameFramework.Runtime;

/// <summary>
/// Toast 轻提示全局工具。
/// 封装 ToastUIForm 的打开与显示，业务层只需一行调用即可弹出提示。
///
/// 用法：
///   ToastUtility.Show("金币不足");
///   ToastUtility.Show("购买成功", 2f);
///
/// 内部逻辑：
/// 1. 若 ToastUIForm 已经打开，直接调用 ShowToast 显示文本。
/// 2. 若 ToastUIForm 尚未打开，自动发起 OpenUIForm 并将本次消息暂存到待发队列，
///    等 UI 打开成功后统一补发。
/// </summary>
public static class ToastUtility
{
    /// <summary>
    /// 待发消息结构体。
    /// 用于 ToastUIForm 尚未打开时缓存调用方传入的文本与时长。
    /// </summary>
    private struct PendingMessage
    {
        /// <summary>
        /// 提示文本。
        /// </summary>
        public string Message;

        /// <summary>
        /// 停留时长（秒）。
        /// </summary>
        public float Duration;
    }

    /// <summary>
    /// 当前正在等待打开的 ToastUIForm 序列号。
    /// 0 表示当前没有待完成的打开请求。
    /// </summary>
    private static int s_pendingOpenId;

    /// <summary>
    /// 当前是否已订阅 UI 打开成功事件。
    /// </summary>
    private static bool s_isListeningOpenEvent;

    /// <summary>
    /// 待发消息队列。
    /// ToastUIForm 打开成功后会依次显示并清空。
    /// </summary>
    private static readonly List<PendingMessage> s_pendingMessages = new List<PendingMessage>(4);

    /// <summary>
    /// 显示一条通用提示，使用默认停留时长（1.5 秒）。
    /// </summary>
    /// <param name="message">提示文本。</param>
    public static void Show(string message)
    {
        Show(message, 0f);
    }

    /// <summary>
    /// 显示一条通用提示，指定停留时长。
    /// duration 传 0 或负数时使用 ToastUIForm 内部默认时长。
    /// </summary>
    /// <param name="message">提示文本。</param>
    /// <param name="duration">停留时长（秒）。≤0 时使用默认值。</param>
    public static void Show(string message, float duration)
    {
        if (string.IsNullOrEmpty(message) || GameEntry.UI == null)
        {
            return;
        }

        // 尝试直接获取已打开的 ToastUIForm 并立即显示。
        ToastUIForm toastForm = ResolveToastUIForm();
        if (toastForm != null)
        {
            ShowOnForm(toastForm, message, duration);
            return;
        }

        // ToastUIForm 尚未打开，缓存本次消息并发起打开请求。
        EnqueuePendingMessage(message, duration);
        EnsureToastUIFormOpen();
    }

    // ──────────────────────────────────────────────────────────
    //  内部实现
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 从 UI 系统中获取已打开的 ToastUIForm 实例。
    /// </summary>
    /// <returns>已打开的 ToastUIForm；未打开时返回 null。</returns>
    private static ToastUIForm ResolveToastUIForm()
    {
        if (GameEntry.UI == null)
        {
            return null;
        }

        UIForm uiForm = GameEntry.UI.GetUIForm(UIFormDefine.ToastUIForm);
        return uiForm != null ? uiForm.Logic as ToastUIForm : null;
    }

    /// <summary>
    /// 在已打开的 ToastUIForm 上显示一条消息。
    /// </summary>
    /// <param name="form">已打开的 ToastUIForm 实例。</param>
    /// <param name="message">提示文本。</param>
    /// <param name="duration">停留时长（秒）。≤0 时使用默认值。</param>
    private static void ShowOnForm(ToastUIForm form, string message, float duration)
    {
        if (duration > 0f)
        {
            form.ShowToast(message, duration);
        }
        else
        {
            form.ShowToast(message);
        }
    }

    /// <summary>
    /// 将一条消息加入待发队列。
    /// </summary>
    /// <param name="message">提示文本。</param>
    /// <param name="duration">停留时长。</param>
    private static void EnqueuePendingMessage(string message, float duration)
    {
        s_pendingMessages.Add(new PendingMessage
        {
            Message = message,
            Duration = duration,
        });
    }

    /// <summary>
    /// 确保 ToastUIForm 已发起打开请求。
    /// 若已经有一个待完成的打开请求，则不重复发起。
    /// </summary>
    private static void EnsureToastUIFormOpen()
    {
        // 已有待完成的打开请求，等回调即可。
        if (s_pendingOpenId > 0)
        {
            return;
        }

        if (GameEntry.UI == null)
        {
            return;
        }

        SubscribeOpenEvent();
        s_pendingOpenId = GameEntry.UI.OpenUIForm(UIFormDefine.ToastUIForm, UIFormDefine.ToastGroup);

        // 打开请求未成功发起，清理状态。
        if (s_pendingOpenId <= 0)
        {
            UnsubscribeOpenEvent();
            s_pendingMessages.Clear();
        }
    }

    /// <summary>
    /// UI 打开成功事件回调。
    /// 仅处理当前待打开的 ToastUIForm 请求，匹配成功后补发所有待发消息。
    /// </summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">事件参数。</param>
    private static void OnOpenUIFormSuccess(object sender, GameEventArgs e)
    {
        OpenUIFormSuccessEventArgs ne = (OpenUIFormSuccessEventArgs)e;

        // 仅匹配本次打开请求的序列号。
        if (ne.UIForm == null || ne.UIForm.SerialId != s_pendingOpenId)
        {
            return;
        }

        // 打开成功，清理等待状态。
        s_pendingOpenId = 0;
        UnsubscribeOpenEvent();

        // 补发所有待发消息。
        ToastUIForm toastForm = ne.UIForm.Logic as ToastUIForm;
        if (toastForm != null)
        {
            for (int i = 0; i < s_pendingMessages.Count; i++)
            {
                PendingMessage pending = s_pendingMessages[i];
                ShowOnForm(toastForm, pending.Message, pending.Duration);
            }
        }

        s_pendingMessages.Clear();
    }

    /// <summary>
    /// UI 打开失败事件回调。
    /// 清理等待状态与待发队列，避免消息永远积压。
    /// </summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">事件参数。</param>
    private static void OnOpenUIFormFailure(object sender, GameEventArgs e)
    {
        OpenUIFormFailureEventArgs ne = (OpenUIFormFailureEventArgs)e;

        if (ne.SerialId != s_pendingOpenId)
        {
            return;
        }

        s_pendingOpenId = 0;
        UnsubscribeOpenEvent();
        s_pendingMessages.Clear();
    }

    /// <summary>
    /// 订阅 UI 打开成功/失败事件。
    /// </summary>
    private static void SubscribeOpenEvent()
    {
        if (s_isListeningOpenEvent || GameEntry.Event == null)
        {
            return;
        }

        GameEntry.Event.Subscribe(OpenUIFormSuccessEventArgs.EventId, OnOpenUIFormSuccess);
        GameEntry.Event.Subscribe(OpenUIFormFailureEventArgs.EventId, OnOpenUIFormFailure);
        s_isListeningOpenEvent = true;
    }

    /// <summary>
    /// 取消订阅 UI 打开成功/失败事件。
    /// </summary>
    private static void UnsubscribeOpenEvent()
    {
        if (!s_isListeningOpenEvent || GameEntry.Event == null)
        {
            return;
        }

        GameEntry.Event.Unsubscribe(OpenUIFormSuccessEventArgs.EventId, OnOpenUIFormSuccess);
        GameEntry.Event.Unsubscribe(OpenUIFormFailureEventArgs.EventId, OnOpenUIFormFailure);
        s_isListeningOpenEvent = false;
    }
}
