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

        // 初始化声音系统：预加载全部声音组（UI / BGM / Effect）并播放背景音乐，早于一切业务初始化。
        UIInteractionSound.InitializeSoundSystem();

        InitCustomComponents();
    }
}
