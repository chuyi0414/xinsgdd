using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 消除规则说明界面。
/// 职责：
/// 1. BtnClose 点击后播放缩放+位移关闭动画，动画结束后关闭自身；
/// 2. OnClose 时 Kill 残留 Tween，防止 UIForm 回收后回调空引用；
/// 3. 关闭动画位移目标与关闭回调由 CombatUIForm 通过 SetCloseAnimationTarget 设置。
/// </summary>
public sealed class EliminateRulesUIForm : UIFormLogic
{
    /// <summary>
    /// 关闭按钮。
    /// 点击后播放关闭动画再关闭本窗体。
    /// </summary>
    [SerializeField]
    private Button _btnClose;

    /// <summary>
    /// 规则面板根节点（EliminateRules）。
    /// 关闭动画的目标对象，缩放和位移都作用于此。
    /// </summary>
    [SerializeField]
    private Transform _rulesPanel;

    /// <summary>
    /// 关闭动画缩放目标值。面板缩小到该比例后消失。
    /// </summary>
    private const float CloseTargetScale = 0.1f;

    /// <summary>
    /// 关闭动画时长（秒）。
    /// </summary>
    private const float CloseDuration = 0.3f;

    /// <summary>
    /// 关闭动画缓动曲线。
    /// </summary>
    private static readonly Ease CloseEase = Ease.InBack;

    /// <summary>
    /// 当前正在播放的关闭动画 Sequence 句柄。
    /// 为 null 表示没有动画在播。
    /// </summary>
    private Sequence _closeSequence;

    /// <summary>
    /// 关闭动画 DOMove 的目标 Transform 引用。
    /// 由 CombatUIForm 打开本窗体后通过 SetCloseAnimationTarget 设置，
    /// 指向 BtnEliminateRules 的 Transform，使面板向规则按钮方向收拢。
    /// 使用 Transform 引用而非 Vector3，是因为首次打开时布局可能尚未完成，
    /// 按钮的 position 不准确；延迟到关闭动画实际触发时才读取，确保位置正确。
    /// </summary>
    private Transform _targetTransform;

    /// <summary>
    /// 关闭动画完成后的回调。
    /// 由 CombatUIForm 设置，用于在面板关闭后触发规则按钮的 Punch 回弹动画。
    /// </summary>
    private Action _onClosedCallback;

    /// <summary>
    /// 规则面板初始局部坐标缓存。
    /// 动画播放后需要还原到此位置，避免下次打开位置偏移。
    /// </summary>
    private Vector3 _rulesPanelInitialLocalPosition;

    /// <summary>
    /// 是否已缓存规则面板初始局部坐标。
    /// </summary>
    private bool _hasCachedInitialLocalPosition;

    /// <summary>
    /// 初始化，绑定按钮事件，缓存面板初始位置。
    /// </summary>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        if (_btnClose != null)
        {
            _btnClose.onClick.RemoveListener(OnBtnClose);
            _btnClose.onClick.AddListener(OnBtnClose);
        }

        CacheRulesPanelInitialStateIfNeeded();
    }

    /// <summary>
    /// 打开时主动查找 CombatUIForm，设置关闭动画目标和回调。
    /// 不依赖外部调用 SetCloseAnimationTarget，因为 OpenUIForm 后立即 GetUIForm
    /// 可能返回 null（GF 框架延迟创建），导致首次打开时目标未设置。
    /// </summary>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        EnsureCloseAnimationTarget();
    }

    /// <summary>
    /// 关闭时 Kill 残留 Tween，清空回调，还原面板状态。
    /// 防止 UIForm 回收后回调空引用，确保下次打开面板状态正确。
    /// </summary>
    protected override void OnClose(bool isShutdown, object userData)
    {
        KillCloseSequence();
        ResetRulesPanelState();
        _targetTransform = null;
        _onClosedCallback = null;
        base.OnClose(isShutdown, userData);
    }

    /// <summary>
    /// 销毁时移除按钮监听并清理动画。
    /// </summary>
    private void OnDestroy()
    {
        KillCloseSequence();
        if (_btnClose != null)
        {
            _btnClose.onClick.RemoveListener(OnBtnClose);
        }
    }

    /// <summary>
    /// 关闭按钮点击回调。
    /// 播放缩放+位移关闭动画，动画结束后关闭本窗体。
    /// </summary>
    private void OnBtnClose()
    {
        PlayCloseAnimation();
    }

    /// <summary>
    /// 缓存规则面板初始局部坐标（仅首次）。
    /// 必须在面板被动画修改之前调用，否则缓存到的是错误位置。
    /// </summary>
    private void CacheRulesPanelInitialStateIfNeeded()
    {
        if (_hasCachedInitialLocalPosition || _rulesPanel == null)
        {
            return;
        }

        _rulesPanelInitialLocalPosition = _rulesPanel.localPosition;
        _hasCachedInitialLocalPosition = true;
    }

    /// <summary>
    /// 还原规则面板到初始状态（位置 + 缩放）。
    /// 在 OnClose 和动画中断时调用，确保下次打开面板状态正确。
    /// </summary>
    private void ResetRulesPanelState()
    {
        if (_rulesPanel == null)
        {
            return;
        }

        _rulesPanel.localScale = Vector3.one;
        if (_hasCachedInitialLocalPosition)
        {
            _rulesPanel.localPosition = _rulesPanelInitialLocalPosition;
        }
    }

    /// <summary>
    /// 通过 GF 框架查找 CombatUIForm 实例，获取规则按钮 Transform 并设置关闭动画目标。
    /// 在 OnOpen 中调用，确保目标在自身生命周期内一定被设置。
    /// 若目标已设置则跳过，保留外部通过 SetCloseAnimationTarget 传入的值。
    /// </summary>
    private void EnsureCloseAnimationTarget()
    {
        // 已有目标则跳过，避免覆盖外部设置
        if (_targetTransform != null)
        {
            return;
        }

        if (GameEntry.UI == null)
        {
            return;
        }

        // 通过资源路径获取 CombatUIForm 的 UIForm 对象
        UIForm combatUIForm = GameEntry.UI.GetUIForm(UIFormDefine.CombatUIForm);
        if (combatUIForm == null || !(combatUIForm.Logic is CombatUIForm combatForm))
        {
            return;
        }

        // 获取规则按钮 Transform 作为关闭动画位移目标，
        // 并绑定 Punch 回弹回调
        if (combatForm.BtnEliminateRulesTransform != null)
        {
            _targetTransform = combatForm.BtnEliminateRulesTransform;
            _onClosedCallback = combatForm.PlayEliminateRulesButtonPunchAnimation;
        }
    }

    /// <summary>
    /// 设置关闭动画的位移目标和关闭回调。
    /// 由 CombatUIForm 在打开本窗体后调用，传入规则按钮的 Transform 和回弹回调。
    /// 使用 Transform 而非 Vector3，延迟到关闭动画触发时才读取 position，
    /// 避免首次打开时布局未就绪导致位置错误。
    /// </summary>
    /// <param name="targetTransform">DOMove 目标 Transform（通常是规则按钮）。</param>
    /// <param name="onClosedCallback">关闭动画完成后的回调（用于触发规则按钮 Punch 回弹）。</param>
    public void SetCloseAnimationTarget(Transform targetTransform, Action onClosedCallback)
    {
        _targetTransform = targetTransform;
        _onClosedCallback = onClosedCallback;
    }

    /// <summary>
    /// 播放关闭动画。
    /// 面板缩放到 CloseTargetScale 并向 _targetWorldPosition 方向位移，
    /// 动画结束后调用回调并关闭自身。
    /// </summary>
    private void PlayCloseAnimation()
    {
        if (_rulesPanel == null)
        {
            InvokeClosedCallback();
            CloseSelf();
            return;
        }

        CacheRulesPanelInitialStateIfNeeded();

        // 先 Kill 上一轮残留动画，防止叠加
        KillCloseSequence();

        // 延迟读取目标 Transform 的世界坐标，确保布局已完成时才取值
        Vector3 targetWorldPos = _targetTransform != null
            ? _targetTransform.position
            : _rulesPanel.position;

        float duration = Mathf.Max(0.01f, CloseDuration);
        Vector3 endLocalScale = Vector3.one * CloseTargetScale;

        _closeSequence = DOTween.Sequence();
        _closeSequence.Join(_rulesPanel.DOScale(endLocalScale, duration).SetEase(CloseEase));
        _closeSequence.Join(_rulesPanel.DOMove(targetWorldPos, duration).SetEase(CloseEase));
        _closeSequence.OnComplete(() =>
        {
            _closeSequence = null;
            InvokeClosedCallback();
            CloseSelf();
        });
        _closeSequence.OnKill(() =>
        {
            if (_closeSequence != null)
            {
                _closeSequence = null;
            }
        });
    }

    /// <summary>
    /// 安全调用关闭回调并置空，防止重复调用。
    /// </summary>
    private void InvokeClosedCallback()
    {
        Action callback = _onClosedCallback;
        _onClosedCallback = null;
        callback?.Invoke();
    }

    /// <summary>
    /// Kill 当前关闭动画 Sequence 并置空句柄。
    /// </summary>
    private void KillCloseSequence()
    {
        if (_closeSequence != null)
        {
            _closeSequence.Kill(false);
            _closeSequence = null;
        }
    }

    /// <summary>
    /// 通过 GF UI 模块关闭本窗体。
    /// </summary>
    private void CloseSelf()
    {
        if (UIForm != null && GameEntry.UI != null)
        {
            GameEntry.UI.CloseUIForm(UIForm.SerialId);
        }
    }
}
