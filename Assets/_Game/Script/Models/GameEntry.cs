using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 游戏入口组件。
/// 负责在场景生命周期早期准备好静态访问用到的内置组件与自定义组件。
/// </summary>
public partial class GameEntry : MonoBehaviour
{
    /// <summary>
    /// 场景唤醒阶段先阻塞 GF 入口流程。
    /// 原因：ProcedureComponent.Start 会在第一帧末尾启动 LoadProcedure，
    /// 但 Addressables.InitializeAsync 在包体或 WebGL 环境下可能超过一帧；
    /// 如果不阻塞，LoadProcedure 会提前 OpenUIForm，导致 Addressables 路由还没 ready 就回退到 Resources 后端。
    /// </summary>
    private void Awake()
    {
        ProcedureStartupGate.BlockStartup();
    }

    /// <summary>
    /// 场景启动后初始化 GameEntry 的全部组件入口。
    /// 流程：
    /// 1. 同步初始化 GF 内置组件引用（必须先于 Addressables.InitializeAsync，因为 Router 需要往 GF IResourceManager 注入）。
    /// 2. 启动 Addressables catalog 初始化并把路由注入 GF 内核。
    /// 3. catalog 初始化完成后才执行 InitCustomComponents，确保 GameAssets 首次预加载请求即可走 Addressables 路由命中。
    /// 注意：InitializeAsync 在编辑器 Use Asset Database 模式下当帧即可完成，包体模式约 50-200ms；
    ///       这段空窗期 GameEntry 的自定义模块（GameAssets / AddressableAssets 等）尚未实例化，业务脚本必须在 Procedure 或更晚阶段访问。
    /// </summary>
    private void Start()
    {
        InitBuiltinComponents();

        // 启动 Addressables 路由初始化；完成回调内继续执行 InitCustomComponents 与 InitSoundSystem。
        // 失败时 IsAddressablesReady 保持 false，所有 LoadAsset 自动退回 GF 主链路（Resources.LoadAsync），不会卡死流程。
        AddressablesAssetRouterImpl.BeginInitialize(OnAddressablesReady);
    }

    /// <summary>
    /// Addressables catalog 初始化完成后的回调。
    /// 此处才创建自定义模块，确保 GameAssets 的预加载、UI/Entity/DataTable/Sound 的首批请求都能命中 Addressables 路由。
    /// </summary>
    private void OnAddressablesReady()
    {
        InitCustomComponents();

        // Addressables catalog 与业务模块都已经完成初始化，此时再允许入口流程打开加载界面和预加载资源。
        ProcedureStartupGate.AllowStartup();

        // 推迟到下一帧执行：确保 SoundComponent.Start() 已经先完成 SoundHelper 的初始化，
        // 避免 SoundManager.AddSoundAgentHelper() 检测到 m_SoundHelper == null 而抛异常。
        // Invoke(..., 0f) 会在当前帧 LateUpdate 之后、下一帧之前触发。
        Invoke(nameof(InitSoundSystem), 0f);
    }

    /// <summary>
    /// 延迟初始化声音系统：在 SoundComponent.Start() 设置完 SoundHelper 后执行。
    /// </summary>
    private void InitSoundSystem()
    {
        UIInteractionSound.InitializeSoundSystem();
    }

    /// <summary>
    /// 每帧推进自定义低频运行时模块。
    /// 当前只驱动云存档自动保存计时，使用 Time.unscaledDeltaTime 避免时间缩放影响保存间隔。
    /// </summary>
    private void Update()
    {
        CloudSave?.Update(Time.unscaledDeltaTime);
    }

    /// <summary>
    /// 游戏入口销毁时释放 Addressables 大资源句柄。
    /// 这里统一清理 GameAddressableAssetModule 中仍被缓存的 Arts、Audio 资源，避免退出场景后句柄泄漏。
    /// </summary>
    private void OnDestroy()
    {
        CloudSave?.Shutdown();
        AddressableAssets?.ReleaseAll();
    }
}
