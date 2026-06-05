using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 加载界面。
/// 预制体本身已注册到 Addressables（群组 Remote Assets，label "Load"）；
/// 6 张加载用图作为 prefab 的直接引用，会随 bundle 由 Addressables 依赖追踪自动加载，
/// 因此脚本不再持有 Image 引用，也无需运行时 LoadAssetsByLabel + 名字分发。
/// 注意：该结论的前提是 prefab 上 6 个 Image 节点的 m_Sprite 已在编辑器中拖入对应 Sprite asset。
/// </summary>
public class LoadUIForm : UIFormLogic
{
    // 「进入游戏」按钮：DataTable 与 GameAssets 双就绪后由 LoadProcedure 主动激活。
    [SerializeField] private Button _btnLoad;
    // 进度条：值域 [0, 1]，由 OnUpdate 内部计算出的「伪进度 + 真进度」混合驱动。
    [SerializeField] private Slider _progressSlider;

    private const float FakeDuration = 3f;
    private const float FakeTarget = 0.8f;
    private const float RealLerpSpeed = 0.5f;

    private float _displayProgress;
    private float _elapsedTime;
    private bool _fakeDone;
    private bool _enteredMain;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        if (_btnLoad != null) _btnLoad.onClick.AddListener(OnBtnLoad);
        SetProgress(0f);
        SetLoadButtonInteractable(false);
    }

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);

        // 每次重新打开都从零状态进入，避免上一次未完成的进度/标志位残留。
        _displayProgress = 0f;
        _elapsedTime = 0f;
        _fakeDone = false;
        _enteredMain = false;
        SetProgress(0f);
        // 数据/资源未就绪前禁止点击；CanEnterMain 通过后由 OnUpdate 主动解锁。
        SetLoadButtonInteractable(false);
    }

    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);
        if (_enteredMain) return;

        if (!_fakeDone)
        {
            _elapsedTime += realElapseSeconds;
            float t = Mathf.Clamp01(_elapsedTime / FakeDuration);
            float eased = 1f - (1f - t) * (1f - t);
            _displayProgress = eased * FakeTarget;
            SetProgress(_displayProgress);
            if (t >= 1f) _fakeDone = true;
        }
        else
        {
            float target = CanEnterMain() ? 1f : FakeTarget;
            _displayProgress = Mathf.MoveTowards(_displayProgress, target, RealLerpSpeed * realElapseSeconds);
            SetProgress(_displayProgress);
        }

        if (_displayProgress >= 1f && !_enteredMain)
        {
            SetLoadButtonInteractable(true);
        }
    }

    protected override void OnClose(bool isShutdown, object userData)
    {
        base.OnClose(isShutdown, userData);
        // 已无业务向 Temporary scope 注册资源；prefab 自身的依赖资源由 GF 关闭流程统一回收，无需手动 ReleaseScope。
    }

    private void OnDestroy()
    {
        if (_btnLoad != null) _btnLoad.onClick.RemoveListener(OnBtnLoad);
    }

    public void SetLoadButtonInteractable(bool isInteractable)
    {
        if (_btnLoad != null) _btnLoad.interactable = isInteractable;
    }

    private void OnBtnLoad()
    {
        UIInteractionSound.PlayClick();
        if (_enteredMain) return;

        LoadProcedure loadProcedure = GameEntry.Procedure.CurrentProcedure as LoadProcedure;
        if (loadProcedure == null || !loadProcedure.RequestEnterMainFromLoadUI())
        {
            SetLoadButtonInteractable(LoadProcedure.CanEnterMain());
            return;
        }

        _enteredMain = true;
        SetLoadButtonInteractable(false);
    }

    private void SetProgress(float progress)
    {
        if (_progressSlider != null) _progressSlider.value = progress;
    }

    private static bool CanEnterMain()
    {
        return LoadProcedure.CanEnterMain();
    }
}
