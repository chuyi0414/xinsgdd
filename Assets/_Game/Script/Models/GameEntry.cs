using UnityEngine;

/// <summary>
/// 游戏入口组件。
/// 负责在场景生命周期早期准备好静态访问用到的内置组件与自定义组件。
/// </summary>
public partial class GameEntry : MonoBehaviour
{
    /// <summary>
    /// 场景启动后初始化 GameEntry 的全部组件入口。
    /// </summary>
    private void Start()
    {
        InitBuiltinComponents();

        InitCustomComponents();

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
}
