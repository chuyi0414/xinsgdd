using UnityEngine;

/// <summary>
/// 游戏入口组件。
/// 负责在场景生命周期早期准备好静态访问用到的内置组件与自定义组件。
/// </summary>
public partial class GameEntry : MonoBehaviour
{
    private void Start()
    {
        InitBuiltinComponents();
        InitCustomComponents();
    }
}
