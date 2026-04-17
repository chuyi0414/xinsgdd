using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 加载界面
/// </summary>
public class LoadUIForm : UIFormLogic
{
    // 加载按钮
    [SerializeField]
    private Button _btnLoad;

    // 进度条 Slider，需在 Inspector 中拖入
    [SerializeField]
    private Slider _progressSlider;

    // 假加载持续时间（秒），此阶段内从 0% 缓动到 80%
    private const float FakeDuration = 3f;

    // 假加载目标进度（0~1）
    private const float FakeTarget = 0.8f;

    // 实际初始化阶段进度缓动速率（每秒增量 0~1）
    private const float RealLerpSpeed = 0.5f;

    // 当前显示的进度值（0~1）
    private float _displayProgress;

    // 从 OnOpen 开始累计的真实时间
    private float _elapsedTime;

    // 假加载是否已完成（达到 80%）
    private bool _fakeDone;

    // 是否已触发进入主界面
    private bool _enteredMain;

    /// <summary>
    /// 初始化加载界面并绑定按钮事件，进度条归零。
    /// </summary>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);

        if (_btnLoad == null)
        {
            Log.Warning("LoadUIForm 找不到 BtnLoad。");
        }
        else
        {
            _btnLoad.onClick.AddListener(OnBtnLoad);
        }

        if (_progressSlider == null)
        {
            Log.Warning("LoadUIForm 找不到 ProgressSlider，进度条功能不可用。");
        }

        // 进度条初始归零
        SetProgress(0f);
        SetLoadButtonInteractable(false);
    }

    /// <summary>
    /// 打开界面时重置假加载状态并启动进度驱动。
    /// </summary>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);

        _displayProgress = 0f;
        _elapsedTime = 0f;
        _fakeDone = false;
        _enteredMain = false;
        SetProgress(0f);
        SetLoadButtonInteractable(false);
    }

    /// <summary>
    /// 每帧驱动进度条：假加载阶段 + 实际初始化阶段。
    /// </summary>
    /// <param name="elapseSeconds">逻辑流逝时间（秒）。</param>
    /// <param name="realElapseSeconds">真实流逝时间（秒）。</param>
    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);

        if (_enteredMain)
        {
            return;
        }

        if (!_fakeDone)
        {
            // ---- 假加载阶段：3 秒内用 EaseOutQuad 缓动从 0 → 80% ----
            _elapsedTime += realElapseSeconds;
            float t = Mathf.Clamp01(_elapsedTime / FakeDuration);
            // EaseOutQuad: f(t) = 1 - (1-t)^2，先快后慢，视觉上更自然
            float eased = 1f - (1f - t) * (1f - t);
            _displayProgress = eased * FakeTarget;
            SetProgress(_displayProgress);

            if (t >= 1f)
            {
                _fakeDone = true;
            }
        }
        else
        {
            // ---- 实际初始化阶段：检测 CanEnterMain，满足则缓动到 100% ----
            float target = CanEnterMain() ? 1f : FakeTarget;
            _displayProgress = Mathf.MoveTowards(_displayProgress, target, RealLerpSpeed * realElapseSeconds);
            SetProgress(_displayProgress);
        }

        // 进度满且尚未进入主界面 → 仅启用按钮，等待玩家手动点击
        if (_displayProgress >= 1f && !_enteredMain)
        {
            SetLoadButtonInteractable(true);
        }
    }

    /// <summary>
    /// 销毁时移除按钮监听。
    /// </summary>
    private void OnDestroy()
    {
        if (_btnLoad != null)
        {
            _btnLoad.onClick.RemoveListener(OnBtnLoad);
        }
    }

    /// <summary>
    /// 设置加载按钮是否可点击。
    /// </summary>
    public void SetLoadButtonInteractable(bool isInteractable)
    {
        if (_btnLoad == null)
        {
            return;
        }

        _btnLoad.interactable = isInteractable;
    }

    /// <summary>
    /// 加载按钮点击逻辑：仅当初始化完成时允许手动进入。
    /// </summary>
    private void OnBtnLoad()
    {
        // 进度未满时按钮不可交互，此处无需再检查 CanEnterMain
        // _enteredMain 防止重复点击导致多次状态切换
        if (_enteredMain)
        {
            return;
        }

        _enteredMain = true;
        EnterMain();
    }

    /// <summary>
    /// 执行进入主界面的流程切换。
    /// </summary>
    private void EnterMain()
    {
        GameFramework.Procedure.ProcedureBase currentProcedure = GameEntry.Procedure.CurrentProcedure;
        currentProcedure.ChangeState<MainProcedure>(currentProcedure.procedureOwner);
    }

    /// <summary>
    /// 设置进度条显示值（0~1）。Slider 为空时安全跳过。
    /// </summary>
    /// <param name="progress">进度值，范围 0~1。</param>
    private void SetProgress(float progress)
    {
        if (_progressSlider != null)
        {
            _progressSlider.value = progress;
        }
    }

    /// <summary>
    /// 当前是否允许进入主界面。
    /// </summary>
    private static bool CanEnterMain()
    {
        return GameEntry.DataTables != null
            && GameEntry.DataTables.IsReady
            && GameEntry.GameAssets != null
            && GameEntry.GameAssets.IsReady;
    }
}
