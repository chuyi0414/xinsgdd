using UnityGameFramework.Runtime;
using GFGameEntry = UnityGameFramework.Runtime.GameEntry;

/// <summary>
/// GameEntry 内置框架组件静态入口。
/// </summary>
public partial class GameEntry
{
    /// <summary>
    /// 框架基础组件。
    /// </summary>
    public static BaseComponent Base { get; private set; }

    /// <summary>
    /// 配置组件。
    /// </summary>
    public static ConfigComponent Config { get; private set; }

    /// <summary>
    /// 数据节点组件。
    /// </summary>
    public static DataNodeComponent DataNode { get; private set; }

    /// <summary>
    /// 数据表组件。
    /// </summary>
    public static DataTableComponent DataTable { get; private set; }

    /// <summary>
    /// 调试器组件。
    /// </summary>
    public static DebuggerComponent Debugger { get; private set; }

    /// <summary>
    /// 下载组件。
    /// </summary>
    public static DownloadComponent Download { get; private set; }

    /// <summary>
    /// 实体组件。
    /// </summary>
    public static EntityComponent Entity { get; private set; }

    /// <summary>
    /// 事件组件。
    /// </summary>
    public static EventComponent Event { get; private set; }

    /// <summary>
    /// 文件系统组件。
    /// </summary>
    public static FileSystemComponent FileSystem { get; private set; }

    /// <summary>
    /// 状态机组件。
    /// </summary>
    public static FsmComponent Fsm { get; private set; }

    /// <summary>
    /// 本地化组件。
    /// </summary>
    public static LocalizationComponent Localization { get; private set; }

    /// <summary>
    /// 网络组件。
    /// </summary>
    public static NetworkComponent Network { get; private set; }

    /// <summary>
    /// 对象池组件。
    /// </summary>
    public static ObjectPoolComponent ObjectPool { get; private set; }

    /// <summary>
    /// 流程组件。
    /// </summary>
    public static ProcedureComponent Procedure { get; private set; }

    /// <summary>
    /// 资源组件。
    /// </summary>
    public static ResourceComponent Resource { get; private set; }

    /// <summary>
    /// 场景组件。
    /// </summary>
    public static SceneComponent Scene { get; private set; }

    /// <summary>
    /// 设置组件。
    /// </summary>
    public static SettingComponent Setting { get; private set; }

    /// <summary>
    /// 音频组件。
    /// </summary>
    public static SoundComponent Sound { get; private set; }

    /// <summary>
    /// UI 组件。
    /// </summary>
    public static UIComponent UI { get; private set; }

    /// <summary>
    /// Web 请求组件。
    /// </summary>
    public static WebRequestComponent WebRequest { get; private set; }

    /// <summary>
    /// 初始化内置框架组件引用。
    /// </summary>
    private static void InitBuiltinComponents()
    {
        Base = GFGameEntry.GetComponent<BaseComponent>();
        Config = GFGameEntry.GetComponent<ConfigComponent>();
        DataNode = GFGameEntry.GetComponent<DataNodeComponent>();
        DataTable = GFGameEntry.GetComponent<DataTableComponent>();
        Debugger = GFGameEntry.GetComponent<DebuggerComponent>();
        Download = GFGameEntry.GetComponent<DownloadComponent>();
        Entity = GFGameEntry.GetComponent<EntityComponent>();
        Event = GFGameEntry.GetComponent<EventComponent>();
        FileSystem = GFGameEntry.GetComponent<FileSystemComponent>();
        Fsm = GFGameEntry.GetComponent<FsmComponent>();
        Localization = GFGameEntry.GetComponent<LocalizationComponent>();
        Network = GFGameEntry.GetComponent<NetworkComponent>();
        ObjectPool = GFGameEntry.GetComponent<ObjectPoolComponent>();
        Procedure = GFGameEntry.GetComponent<ProcedureComponent>();
        Resource = GFGameEntry.GetComponent<ResourceComponent>();
        Scene = GFGameEntry.GetComponent<SceneComponent>();
        Setting = GFGameEntry.GetComponent<SettingComponent>();
        Sound = GFGameEntry.GetComponent<SoundComponent>();
        UI = GFGameEntry.GetComponent<UIComponent>();
        WebRequest = GFGameEntry.GetComponent<WebRequestComponent>();
    }
}
