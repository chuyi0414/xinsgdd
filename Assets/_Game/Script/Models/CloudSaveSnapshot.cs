using System;
using UnityEngine;

/// <summary>
/// 云存档模块级脏标记。
/// 每一位代表 PlayerCloudSaveSnapshot 中一组需要一起覆盖的业务字段，用于部分更新时避免未变化模块被默认值覆盖。
/// </summary>
[Flags]
public enum CloudSaveDirtyModule
{
    /// <summary>
    /// 没有任何模块需要保存。
    /// </summary>
    None = 0,

    /// <summary>
    /// 玩家基础进度模块：金币、待领取离线金币、星星、新人礼包领取状态。
    /// </summary>
    PlayerProgress = 1 << 0,

    /// <summary>
    /// 玩家身份与外观模块：昵称、编号、头像、头像框以及头像/头像框解锁集合。
    /// </summary>
    IdentityAndCosmetic = 1 << 1,

    /// <summary>
    /// 图鉴解锁模块：水果、宠物、产出物解锁集合。
    /// </summary>
    CollectionUnlocks = 1 << 2,

    /// <summary>
    /// 产出物库存模块。
    /// </summary>
    ProduceInventory = 1 << 3,

    /// <summary>
    /// 建筑槽位模块。
    /// </summary>
    Architectures = 1 << 4,

    /// <summary>
    /// 蛋孵化模块。
    /// </summary>
    EggHatch = 1 << 5,

    /// <summary>
    /// 场上宠物轻量数据模块。
    /// </summary>
    Pets = 1 << 6,

    /// <summary>
    /// 主界面未点击金币和产出物掉落模块。
    /// </summary>
    PendingDrops = 1 << 7,

    /// <summary>
    /// 每日一关历史最高分模块。
    /// </summary>
    DailyChallengeBest = 1 << 8,

    /// <summary>
    /// 任务系统模块：任务进度和领取状态。
    /// </summary>
    Tasks = 1 << 9,

    /// <summary>
    /// 全部模块。
    /// </summary>
    All = PlayerProgress
        | IdentityAndCosmetic
        | CollectionUnlocks
        | ProduceInventory
        | Architectures
        | EggHatch
        | Pets
        | PendingDrops
        | DailyChallengeBest
        | Tasks
}

/// <summary>
/// 云存档专用二维坐标。
/// 该结构只保留普通 float 字段，避免微信小游戏 SDK 递归反射 UnityEngine.Vector2 的内部结构。
/// </summary>
[Serializable]
public struct CloudSaveVector2
{
    /// <summary>
    /// UI 局部坐标的 X 分量。
    /// 默认值为 0，表示位于父节点局部坐标原点的横向位置。
    /// </summary>
    public float x;

    /// <summary>
    /// UI 局部坐标的 Y 分量。
    /// 默认值为 0，表示位于父节点局部坐标原点的纵向位置。
    /// </summary>
    public float y;

    /// <summary>
    /// 使用两个浮点分量创建云存档坐标。
    /// </summary>
    /// <param name="x">UI 局部坐标的 X 分量。</param>
    /// <param name="y">UI 局部坐标的 Y 分量。</param>
    public CloudSaveVector2(float x, float y)
    {
        this.x = x;
        this.y = y;
    }

    /// <summary>
    /// 从 Unity UI 使用的 Vector2 坐标创建云存档坐标。
    /// </summary>
    /// <param name="value">Unity UI 运行时坐标。</param>
    /// <returns>只包含 x/y 普通字段的云存档坐标。</returns>
    public static CloudSaveVector2 FromVector2(Vector2 value)
    {
        return new CloudSaveVector2(value.x, value.y);
    }

    /// <summary>
    /// 将云存档坐标还原为 Unity UI 使用的 Vector2。
    /// </summary>
    /// <returns>可直接赋给 RectTransform.anchoredPosition 的坐标。</returns>
    public Vector2 ToVector2()
    {
        return new Vector2(x, y);
    }
}

/// <summary>
/// 玩家云存档的完整快照。
/// 该对象只承载需要跨会话持久化的数据，不保存宠物动画、点餐流程、果园生产等可重建状态。
/// </summary>
[Serializable]
public sealed class PlayerCloudSaveSnapshot
{
    /// <summary>
    /// 当前玩家已经真正入账的金币总额。
    /// 未点击金币掉落物不合并到这里，而是保存到 pendingGoldDrops。
    /// </summary>
    public int currentGold;

    /// <summary>
    /// 当前已经结算但尚未点击领取的离线收益金币。
    /// 该值进入云存档，避免玩家离线收益已经产生但未点击领取时退出后丢失。
    /// </summary>
    public int pendingOfflineEarningGold;

    /// <summary>
    /// 当前玩家累计拥有的星星总额。
    /// 星星只作为解锁阈值，不在购买行为中消耗。
    /// </summary>
    public int currentStars;

    /// <summary>
    /// 新人礼包是否已经领取。
    /// true 表示该账号已经领取过新人礼包，后续不再弹出礼包界面，也不允许重复发奖。
    /// </summary>
    public bool hasClaimedNewcomerPackage;

    /// <summary>
    /// 云端创建新账号时分配的玩家显示名。
    /// 当前用于每日一关排行榜展示。
    /// </summary>
    public string playerName = string.Empty;

    /// <summary>
    /// 云端创建新账号时分配的玩家唯一编号。
    /// 固定为玩家昵称后 10 位数字，创建后通常不再变化。
    /// </summary>
    public string playerCode = string.Empty;

    /// <summary>
    /// 每日一关历史最高分。
    /// 由服务端排行榜提交逻辑写入，客户端读档后仅作为展示缓存。
    /// </summary>
    public int dailyChallengeHistoricalBestScore;

    /// <summary>
    /// 每日一关历史最高分达成时间。
    /// 使用服务端返回的时间字符串，仅用于排查和后续扩展展示。
    /// </summary>
    public string dailyChallengeHistoricalBestTime = string.Empty;

    /// <summary>
    /// 当前选中的头像 Code。
    /// 为空时读取端会回退到运行时默认头像。
    /// </summary>
    public string selectedHeadPortraitCode = string.Empty;

    /// <summary>
    /// 当前选中的头像框 Code。
    /// 为空时读取端会回退到运行时默认头像框。
    /// </summary>
    public string selectedHeadPortraitFrameCode = string.Empty;

    /// <summary>
    /// 客户端生成本次快照时的 UTC 时间字符串。
    /// 当前不参与冲突合并，只用于排查云端数据来源。
    /// </summary>
    public string clientSaveTime = string.Empty;

    /// <summary>
    /// 已解锁水果 Code 列表。
    /// 读取时会覆盖运行时水果解锁集合，并重建点餐候选缓存。
    /// </summary>
    public string[] unlockedFruitCodes = Array.Empty<string>();

    /// <summary>
    /// 已解锁宠物图鉴 Code 列表。
    /// 这是图鉴解锁进度，不等价于当前场上宠物列表。
    /// </summary>
    public string[] unlockedPetCodes = Array.Empty<string>();

    /// <summary>
    /// 已首次拾取过的产出物 Code 列表。
    /// 用于避免同一产出物重复发放首次拾取星星奖励。
    /// </summary>
    public string[] unlockedProduceCodes = Array.Empty<string>();

    /// <summary>
    /// 已解锁头像 Code 列表。
    /// </summary>
    public string[] unlockedHeadPortraitCodes = Array.Empty<string>();

    /// <summary>
    /// 已解锁头像框 Code 列表。
    /// </summary>
    public string[] unlockedHeadPortraitFrameCodes = Array.Empty<string>();

    /// <summary>
    /// 已点击领取并入库的产出物库存。
    /// 未点击的产出物按钮不写入这里，而是保存到 pendingProduceDrops。
    /// </summary>
    public ProduceCountSaveData[] produceCounts = Array.Empty<ProduceCountSaveData>();

    /// <summary>
    /// 四类建筑槽位状态。
    /// 每一类按槽位索引顺序保存解锁状态和等级。
    /// </summary>
    public ArchitectureCategorySaveData[] architectures = Array.Empty<ArchitectureCategorySaveData>();

    /// <summary>
    /// 蛋孵化运行时存档。
    /// 保存手动蛋库存、自动补蛋进度，以及每个孵化槽中正在孵化的蛋和剩余秒数。
    /// </summary>
    public EggHatchSaveData eggHatch = new EggHatchSaveData();

    /// <summary>
    /// 当前仍应存在的宠物轻量数据。
    /// 只保存 petCode 与 remainingEatFruitCount，读取后在游玩区随机刷新。
    /// </summary>
    public PetLiteSaveData[] pets = Array.Empty<PetLiteSaveData>();

    /// <summary>
    /// 未点击金币掉落物列表。
    /// 读取后恢复为可点击金币，不直接加入 currentGold。
    /// </summary>
    public PendingGoldDropSaveData[] pendingGoldDrops = Array.Empty<PendingGoldDropSaveData>();

    /// <summary>
    /// 未点击产出物掉落按钮列表。
    /// 读取后恢复为可点击产出物，不直接加入 produceCounts。
    /// </summary>
    public PendingProduceDropSaveData[] pendingProduceDrops = Array.Empty<PendingProduceDropSaveData>();

    /// <summary>
    /// 任务系统存档。
    /// 保存每个任务的领取时间戳和当前进度。
    /// 旧存档反序列化时该字段缺失，默认空数组。
    /// </summary>
    public TaskSaveData[] claimedTasks = Array.Empty<TaskSaveData>();
}

/// <summary>
/// 云函数返回给客户端的统一响应外壳。
/// </summary>
[Serializable]
public sealed class PlayerCloudSaveEnvelope
{
    /// <summary>
    /// 本次云函数业务处理是否成功。
    /// </summary>
    public bool ok;

    /// <summary>
    /// initOrLoadSave 是否创建了新玩家数据。
    /// </summary>
    public bool created;

    /// <summary>
    /// 云开发上下文中的用户 openid。
    /// 客户端只用于日志排查，不参与本地逻辑。
    /// </summary>
    public string openid = string.Empty;

    /// <summary>
    /// 云函数返回的错误信息。
    /// ok 为 false 时用于日志输出。
    /// </summary>
    public string errMsg = string.Empty;

    /// <summary>
    /// 云端返回的玩家快照。
    /// 保存动作成功时一般回传本次写入的快照；读取动作成功时回传云端最新快照。
    /// </summary>
    public PlayerCloudSaveSnapshot snapshot;
}

/// <summary>
/// 单个产出物库存存档项。
/// </summary>
[Serializable]
public sealed class ProduceCountSaveData
{
    /// <summary>
    /// 产出物 Code。
    /// </summary>
    public string code = string.Empty;

    /// <summary>
    /// 当前已入库数量。
    /// </summary>
    public int count;
}

/// <summary>
/// 单类建筑的存档数据。
/// </summary>
[Serializable]
public sealed class ArchitectureCategorySaveData
{
    /// <summary>
    /// 建筑类别名称。
    /// 使用 PlayerRuntimeModule.ArchitectureCategory 的枚举名字符串保存。
    /// </summary>
    public string category = string.Empty;

    /// <summary>
    /// 该类别下所有槽位的状态数组。
    /// 数组下标 + 1 对应游戏内 1 基槽位索引。
    /// </summary>
    public ArchitectureSlotSaveData[] slots = Array.Empty<ArchitectureSlotSaveData>();
}

/// <summary>
/// 单个建筑槽位的存档数据。
/// </summary>
[Serializable]
public sealed class ArchitectureSlotSaveData
{
    /// <summary>
    /// 该建筑槽位是否已经解锁。
    /// </summary>
    public bool isUnlocked;

    /// <summary>
    /// 该建筑槽位当前等级。
    /// 未解锁时固定为 0；已解锁时至少为 1。
    /// </summary>
    public int level;
}

/// <summary>
/// 蛋孵化运行时存档数据。
/// </summary>
[Serializable]
public sealed class EggHatchSaveData
{
    /// <summary>
    /// 当前手动蛋库存队列。
    /// 数组顺序与运行时库存顺序一致，0 号元素表示最先被消耗进入孵化槽的蛋。
    /// </summary>
    public string[] manualEggCodes = Array.Empty<string>();

    /// <summary>
    /// 当前自动孵化（看广告获得）蛋库存队列。
    /// 数组顺序与运行时库存顺序一致，0 号元素优先消耗。
    /// 该队列与 manualEggCodes 完全独立，不与手动蛋互相挤压。
    /// 老存档反序列化时若该字段缺失，默认空数组，业务上等价于"无自动蛋"。
    /// </summary>
    public string[] autoEggCodes = Array.Empty<string>();

    /// <summary>
    /// 当前自动补蛋已经累计的秒数。
    /// 读取时会被限制在 0 到 RefillDurationSeconds 之间，避免云端手改异常值导致立即刷满。
    /// </summary>
    public float refillElapsedSeconds;

    /// <summary>
    /// 累计孵化完成总数（含在线孵化和离线结算）。
    /// 任务系统使用此字段跟踪「完成 N 次宠物孵化」条件进度。
    /// 旧存档反序列化时该字段缺失，默认为 0。
    /// </summary>
    public int totalHatchCount;

    /// <summary>
    /// 固定孵化槽状态。
    /// 数组下标对应 0 基孵化槽索引。
    /// </summary>
    public EggHatchSlotSaveData[] slots = Array.Empty<EggHatchSlotSaveData>();
}

/// <summary>
/// 单个孵化槽的存档数据。
/// </summary>
[Serializable]
public sealed class EggHatchSlotSaveData
{
    /// <summary>
    /// 当前槽位中正在孵化的蛋 Code。
    /// 为空表示该槽位没有蛋。
    /// </summary>
    public string eggCode = string.Empty;

    /// <summary>
    /// 本次孵化的总时长，单位秒。
    /// 该值已经包含建筑加速倍率，读取时仅用于恢复进度比例和防御性限制。
    /// </summary>
    public float totalSeconds;

    /// <summary>
    /// 当前剩余孵化秒数。
    /// 读取后 EggHatchComponent 会继续逐帧扣减，归零时正常孵出宠物。
    /// </summary>
    public float remainingSeconds;
}

/// <summary>
/// 当前场上宠物的轻量存档数据。
/// </summary>
[Serializable]
public sealed class PetLiteSaveData
{
    /// <summary>
    /// 宠物 Code。
    /// 读取时按该 Code 重建宠物运行时状态。
    /// </summary>
    public string petCode = string.Empty;

    /// <summary>
    /// 剩余可吃饭次数。
    /// 保存时会过滤掉小于等于 0 的宠物。
    /// </summary>
    public int remainingEatFruitCount;
}

/// <summary>
/// 未点击金币掉落物的存档数据。
/// </summary>
[Serializable]
public sealed class PendingGoldDropSaveData
{
    /// <summary>
    /// 该金币掉落物被点击并飞入金币栏后实际入账的金币数量。
    /// </summary>
    public int amount;

    /// <summary>
    /// 金币在奖励 UI 根节点下的局部坐标。
    /// 使用 UI 局部坐标可以避免重进后依赖已经不存在的宠物世界坐标。
    /// </summary>
    public CloudSaveVector2 localPosition;
}

/// <summary>
/// 未点击产出物掉落按钮的存档数据。
/// </summary>
[Serializable]
public sealed class PendingProduceDropSaveData
{
    /// <summary>
    /// 产出物 Code。
    /// 点击后会按该 Code 写入 PlayerRuntimeModule 的产出物库存。
    /// </summary>
    public string code = string.Empty;

    /// <summary>
    /// 产出物按钮在奖励 UI 根节点下的局部坐标。
    /// </summary>
    public CloudSaveVector2 localPosition;
}

/// <summary>
/// 微信 SDK 回传给 C# 的云函数回调消息。
/// </summary>
[Serializable]
public sealed class CloudFunctionCallbackMessage
{
    /// <summary>
    /// 本次调用的回调 Id。
    /// 用于在本地回调表中找到对应请求。
    /// </summary>
    public string callbackId = string.Empty;

    /// <summary>
    /// 回调类型。
    /// SDK 通常返回 success、fail、complete。
    /// </summary>
    public string type = string.Empty;

    /// <summary>
    /// SDK 原始响应 JSON 字符串。
    /// success 时里面包含云函数 result 字段。
    /// </summary>
    public string res = string.Empty;
}

/// <summary>
/// 微信云函数 success 响应中的外层结果。
/// </summary>
[Serializable]
public sealed class CloudFunctionResultEnvelope
{
    /// <summary>
    /// 云函数返回的业务结果 JSON 字符串。
    /// </summary>
    public string result = string.Empty;

    /// <summary>
    /// 微信云函数请求 Id。
    /// 用于云端日志排查。
    /// </summary>
    public string requestID = string.Empty;

    /// <summary>
    /// 微信 SDK 层错误信息。
    /// </summary>
    public string errMsg = string.Empty;
}
