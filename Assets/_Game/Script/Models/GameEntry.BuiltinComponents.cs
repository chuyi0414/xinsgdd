using UnityGameFramework.Runtime;
using GFGameEntry = UnityGameFramework.Runtime.GameEntry;

public partial class GameEntry
{
    public static BaseComponent Base { get; private set; }
    public static ConfigComponent Config { get; private set; }
    public static DataNodeComponent DataNode { get; private set; }
    public static DataTableComponent DataTable { get; private set; }
    public static DebuggerComponent Debugger { get; private set; }
    public static DownloadComponent Download { get; private set; }
    public static EntityComponent Entity { get; private set; }
    public static EventComponent Event { get; private set; }
    public static FileSystemComponent FileSystem { get; private set; }
    public static FsmComponent Fsm { get; private set; }
    public static LocalizationComponent Localization { get; private set; }
    public static NetworkComponent Network { get; private set; }
    public static ObjectPoolComponent ObjectPool { get; private set; }
    public static ProcedureComponent Procedure { get; private set; }
    public static ResourceComponent Resource { get; private set; }
    public static SceneComponent Scene { get; private set; }
    public static SettingComponent Setting { get; private set; }
    public static SoundComponent Sound { get; private set; }
    public static UIComponent UI { get; private set; }
    public static WebRequestComponent WebRequest { get; private set; }

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
