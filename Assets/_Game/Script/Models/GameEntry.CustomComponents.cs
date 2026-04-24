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
    /// 通用资源预加载模块。
    /// </summary>
    public static GameAssetModule GameAssets { get; private set; }

    /// <summary>
    /// 玩家运行时模块（水果解锁 + 金币管理）。
    /// </summary>
    public static PlayerRuntimeModule Fruits { get; private set; }

    /// <summary>
    /// 蛋孵化运行时组件。
    /// </summary>
    public static EggHatchComponent EggHatch { get; private set; }

    /// <summary>
    /// 宠物站位运行时模块。
    /// </summary>
    public static PetPlacementModule PetPlacement { get; private set; }

    /// <summary>
    /// 宠物点餐生产组件。
    /// 负责推进点击点餐后的生产、上桌与收尾流程。
    /// </summary>
    public static PetDiningOrderComponent PetDiningOrders { get; private set; }

    /// <summary>
    /// 果园运行时模块。
    /// </summary>
    public static OrchardModule Orchards { get; private set; }

    /// <summary>
    /// 场地实体显示模块。
    /// </summary>
    public static PlayfieldEntityModule PlayfieldEntities { get; private set; }

    /// <summary>
    /// 广告管理模块。
    /// 负责激励视频广告等广告能力的生命周期管理与统一入口。
    /// </summary>
    public static AdvertisementModule Advertisement { get; private set; }

    /// <summary>
    /// 初始化自定义组件。
    /// 与框架组件保持统一获取方式，方便业务层通过 GameEntry 静态入口访问。
    /// </summary>
    private static void InitCustomComponents()
    {
        EntityIdPool = GFGameEntry.GetComponent<EntityIdPoolComponent>();
        EggHatch = GFGameEntry.GetComponent<EggHatchComponent>();
        PetDiningOrders = GFGameEntry.GetComponent<PetDiningOrderComponent>();
        DataTables = new GameDataTableModule();
        GameAssets = new GameAssetModule();
        Fruits = new PlayerRuntimeModule();
        PetPlacement = new PetPlacementModule();
        Orchards = new OrchardModule();
        PlayfieldEntities = new PlayfieldEntityModule();
        Advertisement = new AdvertisementModule();
        Advertisement.PreloadRewardedVideoAd();

        // 从 PlayerRuntimeModule 读取运行时数量，驱动各模块延迟初始化数组
        PetPlacement.Initialize(Fruits.DiningSeatCount);
        Orchards.Initialize(Fruits.OrchardSlotCount);
        // PlayfieldEntities 使用总槽位数初始化，确保未解锁槽位也有实体数组
        PlayfieldEntities.Initialize(Fruits.TotalDiningSeatCount, Fruits.TotalOrchardSlotCount);
    }
}
