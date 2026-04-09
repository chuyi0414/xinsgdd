using UnityEngine;
using UnityGameFramework.Runtime;
using GFGameEntry = UnityGameFramework.Runtime.GameEntry;

public partial class GameEntry
{
    /// <summary>
    /// 实体 Id 池组件。
    /// 用于分配和回收实体实例 Id。
    /// </summary>
    public static EntityIdPoolComponent EntityIdPool { get; private set; }

    /// <summary>
    /// 通用数据表模块。
    /// </summary>
    public static GameDataTableModule DataTables { get; private set; }

    /// <summary>
    /// 初始化自定义组件。
    /// 与框架组件保持统一获取方式，方便业务层通过 GameEntry 静态入口访问。
    /// </summary>
    private static void InitCustomComponents()
    {
        EntityIdPool = GFGameEntry.GetComponent<EntityIdPoolComponent>();
        DataTables = new GameDataTableModule();
    }
}
