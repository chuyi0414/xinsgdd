using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityGameFramework.Runtime;
#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
using WeChatWASM;
#endif

/// <summary>
/// 玩家云存档运行时模块。
/// 负责启动期读取云端快照、组装完整存档、静默自动保存以及失败重试。
/// </summary>
public sealed class CloudSaveModule
{
    /// <summary>
    /// 云函数名称。
    /// 需要与云开发中部署的函数名保持一致。
    /// </summary>
    private const string CloudFunctionName = "sgdd_server";

    /// <summary>
    /// 云开发环境 Id。
    /// 如果项目已经在微信侧全局初始化云环境，可保持为空；否则请填写真实 env，例如 cloud1-xxxx。
    /// </summary>
    private const string CloudEnvironmentId = "tianxing-001-2g9lrxwh45e5182d";

    /// <summary>
    /// 启动期云存档请求失败后的重试等待秒数。
    /// 微信小游戏真机环境必须拿到云端快照后才能进入主界面，避免把客户端数据表默认值误当成新用户云档。
    /// </summary>
    private const float RetryDelaySeconds = 10f;

    /// <summary>
    /// 默认自动保存间隔秒数。
    /// 当 GameplayRule 表不可用或字段非法时使用。
    /// </summary>
    private const float DefaultAutoSaveIntervalSeconds = 180f;

    /// <summary>
    /// 当前是否已经开始过启动期云存档初始化。
    /// </summary>
    private bool _hasBegunInitialize;

    /// <summary>
    /// 当前启动期云存档流程是否已经结束。
    /// LoadUIForm 会用它决定是否允许进入主界面。
    /// </summary>
    private bool _isReady;

    /// <summary>
    /// 当前是否正在请求云函数。
    /// 防止同一时间发起多次保存覆盖请求。
    /// </summary>
    private bool _isCallingCloud;

#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
    /// <summary>
    /// 微信小游戏运行时云能力是否已经完成初始化。
    /// 初始值为 false，首次真实调用云函数前会通过 WX.InitSDK 和 WX.cloud.Init 完成初始化。
    /// </summary>
    private bool _isWechatCloudInitialized;

    /// <summary>
    /// 当前是否已经订阅微信小游戏隐藏到后台事件。
    /// 初始值为 false；订阅成功后置为 true，用于避免重复绑定 WX.OnHide 导致一次切后台触发多次保存。
    /// </summary>
    private bool _isWechatHideEventSubscribed;
#endif

    /// <summary>
    /// 当前是否已经订阅运行时脏标记事件。
    /// </summary>
    private bool _isDirtyEventSubscribed;

    /// <summary>
    /// 当前存档中尚未同步到云端的模块集合。
    /// </summary>
    private CloudSaveDirtyModule _dirtyModules;

    /// <summary>
    /// 当前已经发往云端、正在等待保存结果的模块集合。
    /// 保存请求发出时会先从 _dirtyModules 中移除这些位；若请求失败，再把这些位并回 _dirtyModules。
    /// </summary>
    private CloudSaveDirtyModule _modulesInFlight;

    /// <summary>
    /// 当前云函数请求结束后是否需要立刻再保存一次。
    /// 初始值为 false；当保存请求进行中又发生强制保存需求时置 true，避免旧请求成功后误清掉新脏数据。
    /// </summary>
    private bool _pendingSaveAfterCurrentCloudCall;

    /// <summary>
    /// 当前是否已经真正进入主界面并允许业务秒数持续推进。
    /// 初始为 false，LoadUIForm 阶段只读档和一次性离线结算，不开启孵化等模块的在线 Update 跑秒。
    /// </summary>
    private bool _hasGameplayStarted;

    /// <summary>
    /// 当前是否允许在主界面打开前执行保存。
    /// 仅启动期离线结算产生变化后置 true，用于立即落盘并在失败时允许重试，避免普通加载等待刷新 clientSaveTime。
    /// </summary>
    private bool _isPreGameplaySaveAllowed;

    /// <summary>
    /// 自动保存间隔秒数。
    /// 初始化时从 GameplayRule.CloudAutoSaveIntervalMinutes 换算得到。
    /// </summary>
    private float _autoSaveIntervalSeconds = DefaultAutoSaveIntervalSeconds;

    /// <summary>
    /// 下一次自动保存前的倒计时。
    /// 只有 _dirtyModules 非空时才会持续扣减。
    /// </summary>
    private float _autoSaveCountdownSeconds = DefaultAutoSaveIntervalSeconds;

    /// <summary>
    /// 保存失败后的重试倒计时。
    /// 大于 0 时优先等待重试，不触发常规自动保存倒计时。
    /// </summary>
    private float _retryCountdownSeconds;

    /// <summary>
    /// 启动期云存档读取失败后的重试倒计时。
    /// 初始值为 0；只有微信小游戏非 Editor 环境读档失败时才会被设置为 RetryDelaySeconds。
    /// </summary>
    private float _initialLoadRetryCountdownSeconds;

    /// <summary>
    /// 当前打开中的主界面引用。
    /// 用于采集或恢复未点击金币和产出物掉落物。
    /// </summary>
    private MainUIForm _mainUIForm;

    /// <summary>
    /// 云端读取到、等待 MainUIForm 打开后恢复的金币掉落物。
    /// </summary>
    private PendingGoldDropSaveData[] _loadedPendingGoldDrops;

    /// <summary>
    /// 云端读取到、等待 MainUIForm 打开后恢复的产出物掉落按钮。
    /// </summary>
    private PendingProduceDropSaveData[] _loadedPendingProduceDrops;

    /// <summary>
    /// 启动期云存档流程是否已经完成。
    /// </summary>
    public bool IsReady => _isReady;

    /// <summary>
    /// 启动期云存档流程是否已经开始。
    /// </summary>
    public bool HasBegunInitialize => _hasBegunInitialize;

    /// <summary>
    /// 当前存档是否存在尚未同步到云端的变化。
    /// 排行榜提交新纪录前会读取该状态，决定是否需要先保存头像、头像框等展示资料。
    /// </summary>
    public bool HasDirtyChanges => _dirtyModules != CloudSaveDirtyModule.None;

    /// <summary>
    /// 云存档保存成功事件。
    /// 仅在 saveSnapshot 动作被云端确认成功后触发，自动保存和手动保存都会触发。
    /// </summary>
    public event Action SaveSucceeded;

    /// <summary>
    /// 云存档保存失败事件。
    /// 参数为失败原因文本；自动保存和手动保存都会触发。
    /// </summary>
    public event Action<string> SaveFailed;

    /// <summary>
    /// 尝试开始启动期云存档初始化。
    /// 必须在数据表和必要资源都完成加载后调用，确保默认快照可以完整构建。
    /// </summary>
    /// <returns>成功发起或已经完成返回 true；前置条件未满足返回 false。</returns>
    public bool BeginInitialize()
    {
        if (_hasBegunInitialize)
        {
            return true;
        }

        if (!PrepareRuntimeForCloudSave())
        {
            return false;
        }

        _hasBegunInitialize = true;
        RefreshAutoSaveInterval();
        SubscribeDirtyEvents();
        SubscribeWechatLifecycleEvents();
        // 注意：initOrLoadSave 严禁上传客户端当前快照。
        // 新用户无云档时，初始存档必须只能由 sgdd_server.js 的 initialSnapshotTemplate 创建；
        // 否则编辑器/本地数据表进度可能被旧云函数或兼容逻辑当成新用户初始档写入云端。
        CallCloudFunction("initOrLoadSave", null, 0, OnInitialLoadCloudSuccess, OnInitialLoadCloudFailure);
        return true;
    }

    /// <summary>
    /// 每帧推进自动保存计时。
    /// 调用方应传入真实时间，避免游戏内时间缩放影响云存档。
    /// </summary>
    /// <param name="realElapseSeconds">真实流逝秒数。</param>
    public void Update(float realElapseSeconds)
    {
        if (realElapseSeconds <= 0f)
        {
            return;
        }

        if (!_isReady)
        {
            UpdateInitialLoadRetry(realElapseSeconds);
            return;
        }

        if (_isCallingCloud)
        {
            return;
        }

        if (!_hasGameplayStarted && !_isPreGameplaySaveAllowed)
        {
            return;
        }

        if (_retryCountdownSeconds > 0f)
        {
            _retryCountdownSeconds -= realElapseSeconds;
            if (_retryCountdownSeconds <= 0f)
            {
                SaveNow(false);
            }

            return;
        }

        _autoSaveCountdownSeconds -= realElapseSeconds;
        if (_autoSaveCountdownSeconds > 0f)
        {
            return;
        }

        if (_dirtyModules != CloudSaveDirtyModule.None)
        {
            SaveNow(false);
            return;
        }

        _autoSaveCountdownSeconds = _autoSaveIntervalSeconds;
    }

    /// <summary>
    /// 推进启动期云存档读取重试倒计时。
    /// 非 Editor 的微信小游戏必须等云函数返回服务端快照，不能在这里切到客户端本地默认档。
    /// </summary>
    /// <param name="realElapseSeconds">真实流逝秒数。</param>
    private void UpdateInitialLoadRetry(float realElapseSeconds)
    {
        if (!_hasBegunInitialize || _isCallingCloud)
        {
            return;
        }

        if (_initialLoadRetryCountdownSeconds > 0f)
        {
            _initialLoadRetryCountdownSeconds -= realElapseSeconds;
            if (_initialLoadRetryCountdownSeconds > 0f)
            {
                return;
            }
        }

        RetryInitialCloudLoad();
    }

    /// <summary>
    /// 标记当前快照已经变脏，需要在下一次自动保存或强制保存时同步云端。
    /// </summary>
    public void MarkDirty()
    {
        MarkDirty(CloudSaveDirtyModule.All);
    }

    /// <summary>
    /// 标记指定云存档模块已经变脏，需要在下一次自动保存或强制保存时同步云端。
    /// </summary>
    /// <param name="dirtyModules">发生变化的云存档模块集合。</param>
    public void MarkDirty(CloudSaveDirtyModule dirtyModules)
    {
        if (!_isReady)
        {
            return;
        }

        if (dirtyModules == CloudSaveDirtyModule.None)
        {
            return;
        }

        _dirtyModules |= dirtyModules;
    }

    /// <summary>
    /// 立即请求保存当前快照。
    /// </summary>
    /// <param name="markDirtyIfBusy">当前已有云函数请求时，是否只标脏等待下次保存。</param>
    /// <returns>成功发起保存请求返回 true。</returns>
    public bool SaveNow(bool markDirtyIfBusy)
    {
        if (!_isReady)
        {
            return false;
        }

        if (_isCallingCloud)
        {
            if (markDirtyIfBusy)
            {
                if (_dirtyModules == CloudSaveDirtyModule.None)
                {
                    _dirtyModules = CloudSaveDirtyModule.All;
                }

                _pendingSaveAfterCurrentCloudCall = true;
            }

            return false;
        }

        CloudSaveDirtyModule modulesToSave = ResolveModulesToSave();
        if (modulesToSave == CloudSaveDirtyModule.None)
        {
            return false;
        }

        if (!TryBuildPatchSnapshot(modulesToSave, out PlayerCloudSaveSnapshot snapshot))
        {
            Log.Warning("CloudSaveModule 保存失败：无法构建当前快照。");
            _dirtyModules |= modulesToSave;
            _modulesInFlight = CloudSaveDirtyModule.None;
            ScheduleSaveRetry();
            return false;
        }

        _modulesInFlight = modulesToSave;
        _dirtyModules &= ~modulesToSave;
        CallCloudFunction("saveSnapshot", snapshot, (int)modulesToSave, OnSaveCloudSuccess, OnSaveCloudFailure);
        return true;
    }

    /// <summary>
    /// 通知云存档模块玩家已经真正进入主界面。
    /// 该方法只打开在线业务秒数推进开关，不做离线结算；离线结算已经在启动期云读档成功后立即完成。
    /// </summary>
    public void NotifyGameplayStarted()
    {
        if (_hasGameplayStarted)
        {
            return;
        }

        _hasGameplayStarted = true;
        GameEntry.EggHatch?.SetRuntimeTickEnabled(true);
    }

    /// <summary>
    /// 注册当前打开的主界面。
    /// 主界面打开后，如果启动期已经读取到未点击掉落物，则在这里恢复成可点击 UI。
    /// </summary>
    /// <param name="mainUIForm">当前打开的 MainUIForm。</param>
    public void RegisterMainUIForm(MainUIForm mainUIForm)
    {
        _mainUIForm = mainUIForm;
        _mainUIForm?.RefreshPlayerNameDisplay();
        RestoreLoadedDropsIfPossible();
    }

    /// <summary>
    /// 反注册主界面引用。
    /// 只有传入实例与当前缓存实例一致时才清空，避免旧界面覆盖新界面。
    /// </summary>
    /// <param name="mainUIForm">即将关闭的 MainUIForm。</param>
    public void UnregisterMainUIForm(MainUIForm mainUIForm)
    {
        if (ReferenceEquals(_mainUIForm, mainUIForm))
        {
            _mainUIForm = null;
        }
    }

    /// <summary>
    /// 释放云存档模块持有的事件订阅。
    /// </summary>
    public void Shutdown()
    {
        UnsubscribeWechatLifecycleEvents();
        UnsubscribeDirtyEvents();
        _mainUIForm = null;
    }

    /// <summary>
    /// 确保云存档依赖的运行时模块已经根据数据表完成初始化。
    /// </summary>
    /// <returns>全部准备完成返回 true。</returns>
    private static bool PrepareRuntimeForCloudSave()
    {
        if (GameEntry.DataTables == null
            || !GameEntry.DataTables.IsReady
            || GameEntry.GameAssets == null
            || !GameEntry.GameAssets.IsReady
            || GameEntry.Fruits == null)
        {
            return false;
        }

        if (!GameEntry.Fruits.EnsureInitialized())
        {
            return false;
        }

        GameEntry.PetPlacement?.Initialize(GameEntry.Fruits.DiningSeatCount);
        GameEntry.PetPlacement?.WarmupPetSelectionCatalog();
        GameEntry.PetPlacement?.WarmupGameplayRuleCache();
        GameEntry.EggHatch?.EnsureInitialized();
        GameEntry.PetDiningOrders?.EnsureInitialized();
        GameEntry.Orchards?.Initialize(GameEntry.Fruits.OrchardSlotCount);
        GameEntry.PlayfieldEntities?.EnsureCapacity(GameEntry.Fruits.DiningSeatCount, GameEntry.Fruits.OrchardSlotCount);
        return true;
    }

    /// <summary>
    /// 从玩法规则表刷新自动保存间隔。
    /// </summary>
    private void RefreshAutoSaveInterval()
    {
        GameplayRuleDataRow gameplayRuleDataRow = GameEntry.DataTables != null
            ? GameEntry.DataTables.GetDataRowByCode<GameplayRuleDataRow>(GameplayRuleDataRow.DefaultCode)
            : null;
        int intervalMinutes = gameplayRuleDataRow != null ? gameplayRuleDataRow.CloudAutoSaveIntervalMinutes : 0;
        _autoSaveIntervalSeconds = intervalMinutes > 0
            ? intervalMinutes * 60f
            : DefaultAutoSaveIntervalSeconds;
        _autoSaveCountdownSeconds = _autoSaveIntervalSeconds;
    }

    /// <summary>
    /// 组装当前完整玩家快照。
    /// 包括玩家长期进度、宠物轻量数据和当前 MainUIForm 中尚未点击的掉落物。
    /// </summary>
    /// <param name="snapshot">输出的完整玩家云存档快照。</param>
    /// <returns>成功构建返回 true。</returns>
    private bool TryBuildCurrentSnapshot(out PlayerCloudSaveSnapshot snapshot)
    {
        snapshot = new PlayerCloudSaveSnapshot
        {
            clientSaveTime = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        if (GameEntry.Fruits == null || !GameEntry.Fruits.ExportPlayerCloudSaveSnapshot(snapshot))
        {
            snapshot = null;
            return false;
        }

        snapshot.pets = GameEntry.PetPlacement != null
            ? GameEntry.PetPlacement.ExportPetLiteSaveData()
            : Array.Empty<PetLiteSaveData>();
        snapshot.eggHatch = GameEntry.EggHatch != null
            ? GameEntry.EggHatch.ExportCloudSaveData()
            : new EggHatchSaveData();

        snapshot.claimedTasks = GameEntry.Tasks != null
            ? GameEntry.Tasks.ExportTaskProgress()
            : Array.Empty<TaskSaveData>();

        List<PendingGoldDropSaveData> goldDrops = new List<PendingGoldDropSaveData>(8);
        List<PendingProduceDropSaveData> produceDrops = new List<PendingProduceDropSaveData>(8);
        if (_mainUIForm != null)
        {
            _mainUIForm.AppendPendingGoldDropsForCloudSave(goldDrops);
            _mainUIForm.AppendPendingProduceDropsForCloudSave(produceDrops);
            snapshot.pendingGoldDrops = goldDrops.ToArray();
            snapshot.pendingProduceDrops = produceDrops.ToArray();
        }
        else
        {
            snapshot.pendingGoldDrops = _loadedPendingGoldDrops ?? Array.Empty<PendingGoldDropSaveData>();
            snapshot.pendingProduceDrops = _loadedPendingProduceDrops ?? Array.Empty<PendingProduceDropSaveData>();
        }

        NormalizeSnapshotForWechatCloudCall(snapshot);
        return true;
    }

    /// <summary>
    /// 解析本次实际需要保存的模块集合。
    /// 只要发生任意保存，就同步孵化模块，保证 clientSaveTime 前移时 eggHatch.remainingSeconds 也一起前移。
    /// </summary>
    /// <returns>本次需要提交给云端合并的模块集合。</returns>
    private CloudSaveDirtyModule ResolveModulesToSave()
    {
        if (_dirtyModules == CloudSaveDirtyModule.None)
        {
            return CloudSaveDirtyModule.None;
        }

        return _dirtyModules | CloudSaveDirtyModule.EggHatch;
    }

    /// <summary>
    /// 按模块集合组装本次补丁快照。
    /// 快照对象中只有 patchModules 声明的模块会被云函数读取，其余默认值不会覆盖云端。
    /// </summary>
    /// <param name="modulesToSave">本次需要保存的模块集合。</param>
    /// <param name="snapshot">输出的补丁快照。</param>
    /// <returns>成功构建返回 true。</returns>
    private bool TryBuildPatchSnapshot(CloudSaveDirtyModule modulesToSave, out PlayerCloudSaveSnapshot snapshot)
    {
        if (modulesToSave == CloudSaveDirtyModule.All)
        {
            return TryBuildCurrentSnapshot(out snapshot);
        }

        if (!TryBuildCurrentSnapshot(out PlayerCloudSaveSnapshot currentSnapshot))
        {
            snapshot = null;
            return false;
        }

        snapshot = new PlayerCloudSaveSnapshot
        {
            clientSaveTime = currentSnapshot.clientSaveTime
        };

        if ((modulesToSave & CloudSaveDirtyModule.PlayerProgress) != 0)
        {
            snapshot.currentGold = currentSnapshot.currentGold;
            snapshot.pendingOfflineEarningGold = currentSnapshot.pendingOfflineEarningGold;
            snapshot.currentStars = currentSnapshot.currentStars;
            snapshot.hasClaimedNewcomerPackage = currentSnapshot.hasClaimedNewcomerPackage;
        }

        if ((modulesToSave & CloudSaveDirtyModule.IdentityAndCosmetic) != 0)
        {
            snapshot.playerName = currentSnapshot.playerName;
            snapshot.playerCode = currentSnapshot.playerCode;
            snapshot.selectedHeadPortraitCode = currentSnapshot.selectedHeadPortraitCode;
            snapshot.selectedHeadPortraitFrameCode = currentSnapshot.selectedHeadPortraitFrameCode;
            snapshot.unlockedHeadPortraitCodes = currentSnapshot.unlockedHeadPortraitCodes;
            snapshot.unlockedHeadPortraitFrameCodes = currentSnapshot.unlockedHeadPortraitFrameCodes;
        }

        if ((modulesToSave & CloudSaveDirtyModule.CollectionUnlocks) != 0)
        {
            snapshot.unlockedFruitCodes = currentSnapshot.unlockedFruitCodes;
            snapshot.unlockedPetCodes = currentSnapshot.unlockedPetCodes;
            snapshot.unlockedProduceCodes = currentSnapshot.unlockedProduceCodes;
        }

        if ((modulesToSave & CloudSaveDirtyModule.ProduceInventory) != 0)
        {
            snapshot.produceCounts = currentSnapshot.produceCounts;
        }

        if ((modulesToSave & CloudSaveDirtyModule.Architectures) != 0)
        {
            snapshot.architectures = currentSnapshot.architectures;
        }

        if ((modulesToSave & CloudSaveDirtyModule.EggHatch) != 0)
        {
            snapshot.eggHatch = currentSnapshot.eggHatch;
        }

        if ((modulesToSave & CloudSaveDirtyModule.Pets) != 0)
        {
            snapshot.pets = currentSnapshot.pets;
        }

        if ((modulesToSave & CloudSaveDirtyModule.PendingDrops) != 0)
        {
            snapshot.pendingGoldDrops = currentSnapshot.pendingGoldDrops;
            snapshot.pendingProduceDrops = currentSnapshot.pendingProduceDrops;
        }

        if ((modulesToSave & CloudSaveDirtyModule.DailyChallengeBest) != 0)
        {
            snapshot.dailyChallengeHistoricalBestScore = currentSnapshot.dailyChallengeHistoricalBestScore;
            snapshot.dailyChallengeHistoricalBestTime = currentSnapshot.dailyChallengeHistoricalBestTime;
        }

        if ((modulesToSave & CloudSaveDirtyModule.Tasks) != 0)
        {
            snapshot.claimedTasks = currentSnapshot.claimedTasks;
        }

        NormalizeSnapshotForWechatCloudCall(snapshot);
        return true;
    }

    /// <summary>
    /// 发送给微信云函数前净化快照，确保对象树中不会出现 null 字符串、null 数组或 null 子对象。
    /// 微信小游戏 SDK 的 fixCallFunctionData 会对 typeof object 的值递归 Object.keys，null 会直接触发 TypeError。
    /// </summary>
    /// <param name="snapshot">待净化的玩家快照。</param>
    private static void NormalizeSnapshotForWechatCloudCall(PlayerCloudSaveSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        snapshot.playerName = snapshot.playerName ?? string.Empty;
        snapshot.playerCode = snapshot.playerCode ?? string.Empty;
        snapshot.dailyChallengeHistoricalBestTime = snapshot.dailyChallengeHistoricalBestTime ?? string.Empty;
        snapshot.selectedHeadPortraitCode = snapshot.selectedHeadPortraitCode ?? string.Empty;
        snapshot.selectedHeadPortraitFrameCode = snapshot.selectedHeadPortraitFrameCode ?? string.Empty;
        snapshot.clientSaveTime = snapshot.clientSaveTime ?? string.Empty;
        snapshot.unlockedFruitCodes = NormalizeStringArray(snapshot.unlockedFruitCodes);
        snapshot.unlockedPetCodes = NormalizeStringArray(snapshot.unlockedPetCodes);
        snapshot.unlockedProduceCodes = NormalizeStringArray(snapshot.unlockedProduceCodes);
        snapshot.unlockedHeadPortraitCodes = NormalizeStringArray(snapshot.unlockedHeadPortraitCodes);
        snapshot.unlockedHeadPortraitFrameCodes = NormalizeStringArray(snapshot.unlockedHeadPortraitFrameCodes);
        snapshot.produceCounts = NormalizeProduceCounts(snapshot.produceCounts);
        snapshot.architectures = NormalizeArchitectures(snapshot.architectures);
        snapshot.eggHatch = NormalizeEggHatch(snapshot.eggHatch);
        snapshot.pets = NormalizePets(snapshot.pets);
        snapshot.pendingGoldDrops = NormalizePendingGoldDrops(snapshot.pendingGoldDrops);
        snapshot.pendingProduceDrops = NormalizePendingProduceDrops(snapshot.pendingProduceDrops);
        snapshot.claimedTasks = NormalizeTaskSaveData(snapshot.claimedTasks);
    }

    /// <summary>
    /// 净化字符串数组，保证数组本体和每个元素都非 null。
    /// </summary>
    /// <param name="values">原数组。</param>
    /// <returns>净化后的数组。</returns>
    private static string[] NormalizeStringArray(string[] values)
    {
        if (values == null || values.Length <= 0)
        {
            return Array.Empty<string>();
        }

        for (int i = 0; i < values.Length; i++)
        {
            values[i] = values[i] ?? string.Empty;
        }

        return values;
    }

    /// <summary>
    /// 净化产出物库存数组。
    /// </summary>
    /// <param name="values">原产出物库存数组。</param>
    /// <returns>净化后的数组。</returns>
    private static ProduceCountSaveData[] NormalizeProduceCounts(ProduceCountSaveData[] values)
    {
        if (values == null || values.Length <= 0)
        {
            return Array.Empty<ProduceCountSaveData>();
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == null)
            {
                values[i] = new ProduceCountSaveData();
            }

            values[i].code = values[i].code ?? string.Empty;
        }

        return values;
    }

    /// <summary>
    /// 净化建筑存档数组。
    /// </summary>
    /// <param name="values">原建筑存档数组。</param>
    /// <returns>净化后的数组。</returns>
    private static ArchitectureCategorySaveData[] NormalizeArchitectures(ArchitectureCategorySaveData[] values)
    {
        if (values == null || values.Length <= 0)
        {
            return Array.Empty<ArchitectureCategorySaveData>();
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == null)
            {
                values[i] = new ArchitectureCategorySaveData();
            }

            values[i].category = values[i].category ?? string.Empty;
            values[i].slots = values[i].slots ?? Array.Empty<ArchitectureSlotSaveData>();
            for (int slotIndex = 0; slotIndex < values[i].slots.Length; slotIndex++)
            {
                if (values[i].slots[slotIndex] == null)
                {
                    values[i].slots[slotIndex] = new ArchitectureSlotSaveData();
                }
            }
        }

        return values;
    }

    /// <summary>
    /// 净化孵化存档。
    /// </summary>
    /// <param name="value">原孵化存档。</param>
    /// <returns>净化后的孵化存档。</returns>
    private static EggHatchSaveData NormalizeEggHatch(EggHatchSaveData value)
    {
        if (value == null)
        {
            value = new EggHatchSaveData();
        }

        value.manualEggCodes = NormalizeStringArray(value.manualEggCodes);
        value.slots = value.slots ?? Array.Empty<EggHatchSlotSaveData>();
        for (int i = 0; i < value.slots.Length; i++)
        {
            if (value.slots[i] == null)
            {
                value.slots[i] = new EggHatchSlotSaveData();
            }

            value.slots[i].eggCode = value.slots[i].eggCode ?? string.Empty;
        }

        return value;
    }

    /// <summary>
    /// 净化宠物轻量存档数组。
    /// </summary>
    /// <param name="values">原宠物存档数组。</param>
    /// <returns>净化后的数组。</returns>
    private static PetLiteSaveData[] NormalizePets(PetLiteSaveData[] values)
    {
        if (values == null || values.Length <= 0)
        {
            return Array.Empty<PetLiteSaveData>();
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == null)
            {
                values[i] = new PetLiteSaveData();
            }

            values[i].petCode = values[i].petCode ?? string.Empty;
        }

        return values;
    }

    /// <summary>
    /// 净化未点击金币掉落存档数组。
    /// </summary>
    /// <param name="values">原金币掉落数组。</param>
    /// <returns>净化后的数组。</returns>
    private static PendingGoldDropSaveData[] NormalizePendingGoldDrops(PendingGoldDropSaveData[] values)
    {
        if (values == null || values.Length <= 0)
        {
            return Array.Empty<PendingGoldDropSaveData>();
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == null)
            {
                values[i] = new PendingGoldDropSaveData();
            }
        }

        return values;
    }

    /// <summary>
    /// 净化未点击产出物掉落存档数组。
    /// </summary>
    /// <param name="values">原产出物掉落数组。</param>
    /// <returns>净化后的数组。</returns>
    private static PendingProduceDropSaveData[] NormalizePendingProduceDrops(PendingProduceDropSaveData[] values)
    {
        if (values == null || values.Length <= 0)
        {
            return Array.Empty<PendingProduceDropSaveData>();
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == null)
            {
                values[i] = new PendingProduceDropSaveData();
            }

            values[i].code = values[i].code ?? string.Empty;
        }

        return values;
    }

    /// <summary>
    /// 净化任务存档数组，保证数组本体和每个元素都非 null。
    /// </summary>
    /// <param name="values">原任务存档数组。</param>
    /// <returns>净化后的数组。</returns>
    private static TaskSaveData[] NormalizeTaskSaveData(TaskSaveData[] values)
    {
        if (values == null || values.Length <= 0)
        {
            return Array.Empty<TaskSaveData>();
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == null)
            {
                values[i] = new TaskSaveData();
            }

            values[i].code = values[i].code ?? string.Empty;
        }

        return values;
    }

    /// <summary>
    /// 将云端快照应用到当前运行时。
    /// 未点击掉落物不入账，只缓存到 MainUIForm 打开后恢复成可点击物体。
    /// </summary>
    /// <param name="snapshot">云端玩家快照。</param>
    private void ApplySnapshotToRuntime(PlayerCloudSaveSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        GameEntry.Fruits?.ApplyPlayerCloudSaveSnapshot(snapshot);
        GameEntry.EggHatch?.ApplyCloudSaveData(snapshot.eggHatch);
        GameEntry.Tasks?.ApplyTaskProgress(snapshot.claimedTasks);
        GameEntry.PetPlacement?.ApplyPetLiteSaveData(snapshot.pets);

        // 云存档恢复了运行时解锁的水果后，补充预加载这些水果的每日关卡卡图。
        // 必须在 ApplyPlayerCloudSaveSnapshot 之后调用，此时 _unlockedFruitCodes 已写入。
        GameEntry.GameAssets?.SupplementDailyChallengeCardSpritesAfterCloudRestore();
        _loadedPendingGoldDrops = snapshot.pendingGoldDrops;
        _loadedPendingProduceDrops = snapshot.pendingProduceDrops;
        _mainUIForm?.RefreshPlayerNameDisplay();
        RestoreLoadedDropsIfPossible();
    }

    /// <summary>
    /// 应用云存档读取后需要立即结算的离线业务秒数。
    /// 该结算只扣减存档时间差，不开启在线 Update 跑秒；若有蛋完成孵化，会直接生成到游玩区。
    /// </summary>
    /// <param name="snapshot">刚从云端读取到的快照。</param>
    /// <returns>本次离线结算是否改变了需要保存的运行时状态。</returns>
    private static bool ApplyInitialOfflineSettlement(PlayerCloudSaveSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return false;
        }

        float offlineSeconds = CalculateOfflineElapsedSeconds(snapshot);
        if (offlineSeconds <= 0f)
        {
            return false;
        }

        bool hasChanged = false;
        if (GameEntry.EggHatch != null && GameEntry.EggHatch.ApplyOfflineElapsedSeconds(offlineSeconds))
        {
            hasChanged = true;
        }

        if (GameEntry.Fruits != null && GameEntry.Fruits.ApplyOfflineEarningSeconds(offlineSeconds))
        {
            hasChanged = true;
        }

        return hasChanged;
    }

    /// <summary>
    /// 根据快照中的客户端保存时间计算离线秒数。
    /// clientSaveTime 使用 UTC ISO-8601 字符串保存；解析失败、未来时间或空字符串都按 0 秒处理。
    /// </summary>
    /// <param name="snapshot">云端快照。</param>
    /// <returns>需要一次性结算的真实秒数。</returns>
    private static float CalculateOfflineElapsedSeconds(PlayerCloudSaveSnapshot snapshot)
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.clientSaveTime))
        {
            return 0f;
        }

        if (!DateTime.TryParse(
                snapshot.clientSaveTime,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime clientSaveTime))
        {
            return 0f;
        }

        double elapsedSeconds = (DateTime.UtcNow - clientSaveTime.ToUniversalTime()).TotalSeconds;
        if (elapsedSeconds <= 0d)
        {
            return 0f;
        }

        return elapsedSeconds >= float.MaxValue ? float.MaxValue : (float)elapsedSeconds;
    }

    /// <summary>
    /// 如果主界面已经打开，则把启动期读取到的掉落物恢复到 UI。
    /// </summary>
    private void RestoreLoadedDropsIfPossible()
    {
        if (_mainUIForm == null)
        {
            return;
        }

        if (_loadedPendingGoldDrops != null)
        {
            _mainUIForm.RestorePendingGoldDropsFromCloudSave(_loadedPendingGoldDrops);
            _loadedPendingGoldDrops = null;
        }

        if (_loadedPendingProduceDrops != null)
        {
            _mainUIForm.RestorePendingProduceDropsFromCloudSave(_loadedPendingProduceDrops);
            _loadedPendingProduceDrops = null;
        }
    }

    /// <summary>
    /// 订阅微信小游戏生命周期事件。
    /// 当前只监听隐藏到后台事件，用于用户锁屏、按 Home、切后台或离开小游戏时尽快触发一次云存档保存。
    /// </summary>
    private void SubscribeWechatLifecycleEvents()
    {
#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
        if (_isWechatHideEventSubscribed)
        {
            return;
        }

        WX.OnHide(OnWechatGameHidden);
        _isWechatHideEventSubscribed = true;
#endif
    }

    /// <summary>
    /// 取消订阅微信小游戏生命周期事件。
    /// 该方法在 GameEntry 销毁时执行，避免模块释放后仍被 WX.OnHide 回调访问。
    /// </summary>
    private void UnsubscribeWechatLifecycleEvents()
    {
#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
        if (!_isWechatHideEventSubscribed)
        {
            return;
        }

        WX.OffHide(OnWechatGameHidden);
        _isWechatHideEventSubscribed = false;
#endif
    }

#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
    /// <summary>
    /// 微信小游戏隐藏到后台回调。
    /// 用户锁屏、按 Home、切后台、从聊天顶部隐藏或离开小游戏时会触发该事件。
    /// </summary>
    /// <param name="result">微信隐藏事件结果；当前保存逻辑不依赖其中字段。</param>
    private void OnWechatGameHidden(GeneralCallbackResult result)
    {
        if (!_isReady)
        {
            return;
        }

        if (!_hasGameplayStarted && !_isPreGameplaySaveAllowed)
        {
            return;
        }

        MarkDirty(CloudSaveDirtyModule.All);
        SaveNow(true);
    }
#endif

    /// <summary>
    /// 订阅运行时状态变化事件，用于自动设置云存档脏标记。
    /// </summary>
    private void SubscribeDirtyEvents()
    {
        if (_isDirtyEventSubscribed)
        {
            return;
        }

        if (GameEntry.Fruits != null)
        {
            GameEntry.Fruits.GoldChanged += OnPlayerGoldChanged;
            GameEntry.Fruits.StarsChanged += OnPlayerStarsChanged;
            GameEntry.Fruits.ProduceChanged += OnPlayerProduceChanged;
            GameEntry.Fruits.ArchitectureStateChanged += OnPlayerArchitectureStateChanged;
            GameEntry.Fruits.PlayfieldCapacityChanged += OnPlayerPlayfieldCapacityChanged;
            GameEntry.Fruits.NewcomerPackageClaimStateChanged += OnNewcomerPackageClaimStateChanged;
            GameEntry.Fruits.OfflineEarningsChanged += OnOfflineEarningsChanged;
            GameEntry.Fruits.CollectionUnlocksChanged += OnCollectionUnlocksChanged;
            GameEntry.Fruits.CosmeticUnlocksChanged += OnCosmeticUnlocksChanged;
            GameEntry.Fruits.SelectedHeadPortraitChanged += OnSelectedHeadPortraitChanged;
            GameEntry.Fruits.SelectedHeadPortraitFrameChanged += OnSelectedHeadPortraitFrameChanged;
        }

        if (GameEntry.PetPlacement != null)
        {
            GameEntry.PetPlacement.PlacementChanged += OnPetPlacementChanged;
        }

        if (GameEntry.EggHatch != null)
        {
            GameEntry.EggHatch.HatchStateChanged += OnEggHatchStateChanged;
        }

        _isDirtyEventSubscribed = true;
    }

    /// <summary>
    /// 取消订阅运行时状态变化事件。
    /// </summary>
    private void UnsubscribeDirtyEvents()
    {
        if (!_isDirtyEventSubscribed)
        {
            return;
        }

        if (GameEntry.Fruits != null)
        {
            GameEntry.Fruits.GoldChanged -= OnPlayerGoldChanged;
            GameEntry.Fruits.StarsChanged -= OnPlayerStarsChanged;
            GameEntry.Fruits.ProduceChanged -= OnPlayerProduceChanged;
            GameEntry.Fruits.ArchitectureStateChanged -= OnPlayerArchitectureStateChanged;
            GameEntry.Fruits.PlayfieldCapacityChanged -= OnPlayerPlayfieldCapacityChanged;
            GameEntry.Fruits.NewcomerPackageClaimStateChanged -= OnNewcomerPackageClaimStateChanged;
            GameEntry.Fruits.OfflineEarningsChanged -= OnOfflineEarningsChanged;
            GameEntry.Fruits.CollectionUnlocksChanged -= OnCollectionUnlocksChanged;
            GameEntry.Fruits.CosmeticUnlocksChanged -= OnCosmeticUnlocksChanged;
            GameEntry.Fruits.SelectedHeadPortraitChanged -= OnSelectedHeadPortraitChanged;
            GameEntry.Fruits.SelectedHeadPortraitFrameChanged -= OnSelectedHeadPortraitFrameChanged;
        }

        if (GameEntry.PetPlacement != null)
        {
            GameEntry.PetPlacement.PlacementChanged -= OnPetPlacementChanged;
        }

        if (GameEntry.EggHatch != null)
        {
            GameEntry.EggHatch.HatchStateChanged -= OnEggHatchStateChanged;
        }

        _isDirtyEventSubscribed = false;
    }

    /// <summary>
    /// 玩家金币变化事件回调。
    /// </summary>
    /// <param name="gold">最新金币数量。</param>
    private void OnPlayerGoldChanged(int gold)
    {
        MarkDirty(CloudSaveDirtyModule.PlayerProgress);
    }

    /// <summary>
    /// 玩家星星变化事件回调。
    /// </summary>
    /// <param name="stars">最新星星数量。</param>
    private void OnPlayerStarsChanged(int stars)
    {
        MarkDirty(CloudSaveDirtyModule.PlayerProgress);
    }

    /// <summary>
    /// 产出物库存变化事件回调。
    /// </summary>
    /// <param name="produceCode">产出物 Code。</param>
    /// <param name="count">最新库存数量。</param>
    private void OnPlayerProduceChanged(string produceCode, int count)
    {
        MarkDirty(CloudSaveDirtyModule.PlayerProgress | CloudSaveDirtyModule.CollectionUnlocks | CloudSaveDirtyModule.ProduceInventory);
    }

    /// <summary>
    /// 建筑状态变化事件回调。
    /// </summary>
    private void OnPlayerArchitectureStateChanged()
    {
        MarkDirty(CloudSaveDirtyModule.PlayerProgress | CloudSaveDirtyModule.Architectures | CloudSaveDirtyModule.EggHatch | CloudSaveDirtyModule.Pets);
    }

    /// <summary>
    /// 场地容量变化事件回调。
    /// </summary>
    /// <param name="diningSeatCount">最新餐桌位数量。</param>
    /// <param name="orchardSlotCount">最新果树位数量。</param>
    private void OnPlayerPlayfieldCapacityChanged(int diningSeatCount, int orchardSlotCount)
    {
        MarkDirty(CloudSaveDirtyModule.Architectures | CloudSaveDirtyModule.EggHatch | CloudSaveDirtyModule.Pets);
    }

    /// <summary>
    /// 宠物站位变化事件回调。
    /// </summary>
    private void OnPetPlacementChanged()
    {
        MarkDirty(CloudSaveDirtyModule.CollectionUnlocks | CloudSaveDirtyModule.Pets);
    }

    /// <summary>
    /// 新人礼包领取状态变化事件回调。
    /// </summary>
    private void OnNewcomerPackageClaimStateChanged()
    {
        MarkDirty(CloudSaveDirtyModule.PlayerProgress);
    }

    /// <summary>
    /// 离线收益待领取数量变化事件回调。
    /// </summary>
    private void OnOfflineEarningsChanged()
    {
        MarkDirty(CloudSaveDirtyModule.PlayerProgress);
    }

    /// <summary>
    /// 图鉴解锁集合变化事件回调。
    /// </summary>
    private void OnCollectionUnlocksChanged()
    {
        MarkDirty(CloudSaveDirtyModule.CollectionUnlocks);
    }

    /// <summary>
    /// 外观解锁集合变化事件回调。
    /// </summary>
    private void OnCosmeticUnlocksChanged()
    {
        MarkDirty(CloudSaveDirtyModule.IdentityAndCosmetic);
    }

    /// <summary>
    /// 玩家选中头像变化事件回调。
    /// </summary>
    /// <param name="headPortraitCode">最新头像 Code。</param>
    private void OnSelectedHeadPortraitChanged(string headPortraitCode)
    {
        MarkDirty(CloudSaveDirtyModule.IdentityAndCosmetic);
    }

    /// <summary>
    /// 玩家选中头像框变化事件回调。
    /// </summary>
    /// <param name="headPortraitFrameCode">最新头像框 Code。</param>
    private void OnSelectedHeadPortraitFrameChanged(string headPortraitFrameCode)
    {
        MarkDirty(CloudSaveDirtyModule.IdentityAndCosmetic);
    }

    /// <summary>
    /// 孵化运行时状态变化事件回调。
    /// </summary>
    private void OnEggHatchStateChanged()
    {
        MarkDirty(CloudSaveDirtyModule.EggHatch | CloudSaveDirtyModule.Pets | CloudSaveDirtyModule.CollectionUnlocks);
    }

    /// <summary>
    /// 启动期读取成功回调。
    /// </summary>
    /// <param name="responseJson">云函数业务响应 JSON。</param>
    private void OnInitialLoadCloudSuccess(string responseJson)
    {
        _isCallingCloud = false;
        PlayerCloudSaveEnvelope envelope = ParseCloudSaveEnvelope(responseJson);
        if (envelope != null && envelope.ok && envelope.snapshot != null)
        {
            PlayerCloudSaveSnapshot snapshot = envelope.snapshot;
            ApplySnapshotToRuntime(snapshot);
            bool hasOfflineSettlementChanged = !envelope.created && ApplyInitialOfflineSettlement(snapshot);
            _dirtyModules = hasOfflineSettlementChanged
                ? CloudSaveDirtyModule.PlayerProgress | CloudSaveDirtyModule.EggHatch | CloudSaveDirtyModule.Pets | CloudSaveDirtyModule.CollectionUnlocks
                : CloudSaveDirtyModule.None;
            _modulesInFlight = CloudSaveDirtyModule.None;
            _isPreGameplaySaveAllowed = hasOfflineSettlementChanged;
            _isReady = true;
            _retryCountdownSeconds = 0f;
            _initialLoadRetryCountdownSeconds = 0f;
            _autoSaveCountdownSeconds = _autoSaveIntervalSeconds;
            if (hasOfflineSettlementChanged)
            {
                SaveNow(true);
            }

            return;
        }

        Log.Warning("CloudSaveModule 启动期云存档读取失败：{0}", envelope != null ? envelope.errMsg : responseJson);
        CompleteInitialLoadAfterFailure();
    }

    /// <summary>
    /// 启动期读取失败回调。
    /// </summary>
    /// <param name="errorMessage">错误信息。</param>
    private void OnInitialLoadCloudFailure(string errorMessage)
    {
        _isCallingCloud = false;
        Log.Warning("CloudSaveModule 启动期云存档请求失败：{0}", errorMessage);
        CompleteInitialLoadAfterFailure();
    }

    /// <summary>
    /// 保存成功回调。
    /// </summary>
    /// <param name="responseJson">云函数业务响应 JSON。</param>
    private void OnSaveCloudSuccess(string responseJson)
    {
        _isCallingCloud = false;
        PlayerCloudSaveEnvelope envelope = ParseCloudSaveEnvelope(responseJson);
        if (envelope != null && envelope.ok)
        {
            if (_pendingSaveAfterCurrentCloudCall)
            {
                _pendingSaveAfterCurrentCloudCall = false;
                _modulesInFlight = CloudSaveDirtyModule.None;
                _retryCountdownSeconds = 0f;
                _autoSaveCountdownSeconds = 0f;
                if (_dirtyModules != CloudSaveDirtyModule.None && SaveNow(false))
                {
                    return;
                }
            }

            _modulesInFlight = CloudSaveDirtyModule.None;
            _isPreGameplaySaveAllowed = false;
            _retryCountdownSeconds = 0f;
            _autoSaveCountdownSeconds = _autoSaveIntervalSeconds;
            SaveSucceeded?.Invoke();
            return;
        }

        _pendingSaveAfterCurrentCloudCall = false;
        _dirtyModules |= _modulesInFlight;
        _modulesInFlight = CloudSaveDirtyModule.None;
        Log.Warning("CloudSaveModule 云存档保存失败：{0}", envelope != null ? envelope.errMsg : responseJson);
        SaveFailed?.Invoke(envelope != null ? envelope.errMsg : responseJson);
        ScheduleSaveRetry();
    }

    /// <summary>
    /// 保存失败回调。
    /// </summary>
    /// <param name="errorMessage">错误信息。</param>
    private void OnSaveCloudFailure(string errorMessage)
    {
        _isCallingCloud = false;
        _pendingSaveAfterCurrentCloudCall = false;
        _dirtyModules |= _modulesInFlight;
        _modulesInFlight = CloudSaveDirtyModule.None;
        Log.Warning("CloudSaveModule 云存档保存请求失败：{0}", errorMessage);
        SaveFailed?.Invoke(errorMessage);
        ScheduleSaveRetry();
    }

    /// <summary>
    /// 使用本地初始进度完成启动期流程。
    /// 该兜底保证云服务不可用时不会卡死加载界面。
    /// </summary>
    private void CompleteInitialLoadWithFallback()
    {
        _isReady = true;
        _dirtyModules = CloudSaveDirtyModule.All;
        _modulesInFlight = CloudSaveDirtyModule.None;
        _isPreGameplaySaveAllowed = false;
        _initialLoadRetryCountdownSeconds = 0f;
        _retryCountdownSeconds = RetryDelaySeconds;
    }

    /// <summary>
    /// 处理启动期云存档失败。
    /// Editor / 非微信环境允许用本地模拟快照继续开发；微信小游戏真机必须保持加载并重试云函数。
    /// </summary>
    private void CompleteInitialLoadAfterFailure()
    {
        if (CanUseLocalInitialFallback())
        {
            Log.Warning("CloudSaveModule 当前环境允许本地兜底，将使用本地初始进度进入游戏。");
            CompleteInitialLoadWithFallback();
            return;
        }

        ScheduleInitialLoadRetry();
    }

    /// <summary>
    /// 安排启动期云存档重新读取。
    /// 这里显式清理所有保存脏标记，避免云端未读成功前把客户端默认进度写回云端。
    /// </summary>
    private void ScheduleInitialLoadRetry()
    {
        _isReady = false;
        _dirtyModules = CloudSaveDirtyModule.None;
        _modulesInFlight = CloudSaveDirtyModule.None;
        _pendingSaveAfterCurrentCloudCall = false;
        _isPreGameplaySaveAllowed = false;
        _initialLoadRetryCountdownSeconds = RetryDelaySeconds;
        Log.Warning("CloudSaveModule 将在 {0} 秒后重试启动期云存档读取，期间不会进入主界面。", RetryDelaySeconds);
    }

    /// <summary>
    /// 重新发起启动期云存档读取。
    /// 云函数会在新用户无档时使用 sgdd_server.js 的 initialSnapshotTemplate 建档。
    /// </summary>
    private void RetryInitialCloudLoad()
    {
        if (!PrepareRuntimeForCloudSave())
        {
            _initialLoadRetryCountdownSeconds = RetryDelaySeconds;
            return;
        }

        // 启动期重试同样不能携带客户端快照，确保新用户建档入口始终只信任云函数模板。
        CallCloudFunction("initOrLoadSave", null, 0, OnInitialLoadCloudSuccess, OnInitialLoadCloudFailure);
    }

    /// <summary>
    /// 当前环境是否允许启动期云存档失败后使用本地初始进度兜底。
    /// 真机微信小游戏禁止兜底，避免新账号没有走云函数模板而误用客户端数据表默认值。
    /// </summary>
    /// <returns>允许本地兜底返回 true；必须等待云函数返回返回 false。</returns>
    private static bool CanUseLocalInitialFallback()
    {
#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
        return false;
#else
        return true;
#endif
    }

    /// <summary>
    /// 安排下一次保存重试。
    /// </summary>
    private void ScheduleSaveRetry()
    {
        _retryCountdownSeconds = RetryDelaySeconds;
        _autoSaveCountdownSeconds = _autoSaveIntervalSeconds;
    }

    /// <summary>
    /// 解析云函数业务响应外壳。
    /// </summary>
    /// <param name="responseJson">云函数返回的业务 JSON。</param>
    /// <returns>解析后的响应外壳；解析失败返回 null。</returns>
    private static PlayerCloudSaveEnvelope ParseCloudSaveEnvelope(string responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            return null;
        }

        try
        {
            return JsonUtility.FromJson<PlayerCloudSaveEnvelope>(responseJson);
        }
        catch (Exception exception)
        {
            Log.Warning("CloudSaveModule 解析云函数响应失败：{0}", exception.Message);
            return null;
        }
    }

    /// <summary>
    /// 调用微信云函数。
    /// 非微信 WebGL 环境下直接构造本地成功响应，避免编辑器触发 WXCallFunction 入口缺失异常。
    /// </summary>
    /// <param name="action">云函数业务动作。</param>
    /// <param name="snapshot">需要提交的玩家快照；读取动作可为空。</param>
    /// <param name="patchModules">saveSnapshot 补丁模块掩码；0 表示全量快照。</param>
    /// <param name="onSuccess">业务成功回调，参数为云函数 result JSON。</param>
    /// <param name="onFailure">失败回调，参数为错误信息。</param>
    private void CallCloudFunction(
        string action,
        PlayerCloudSaveSnapshot snapshot,
        int patchModules,
        Action<string> onSuccess,
        Action<string> onFailure)
    {
        _isCallingCloud = true;
#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
        try
        {
            Dictionary<string, object> requestData = CreateCloudFunctionRequestData(action, snapshot, patchModules);
            EnsureWechatCloudInitialized(
                () => ExecuteWechatCloudFunction(requestData, onSuccess, onFailure),
                onFailure);
        }
        catch (Exception exception)
        {
            onFailure?.Invoke(exception.Message);
        }
#else
        PlayerCloudSaveSnapshot localSnapshot = snapshot;
        if (localSnapshot == null && action == "initOrLoadSave")
        {
            TryBuildCurrentSnapshot(out localSnapshot);
        }

        PlayerCloudSaveEnvelope localEnvelope = new PlayerCloudSaveEnvelope
        {
            ok = true,
            created = action == "initOrLoadSave",
            openid = "local_editor",
            snapshot = localSnapshot
        };
        onSuccess?.Invoke(JsonUtility.ToJson(localEnvelope));
#endif
    }

    /// <summary>
    /// 构建发送给微信云函数的根级参数对象。
    /// 当前协议要求 action、snapshot 与 patchModules 全部位于 event 根级，云函数不再解析 data 字符串桥接体。
    /// </summary>
    /// <param name="action">云函数业务动作。</param>
    /// <param name="snapshot">需要提交的玩家快照；读取动作可为空。</param>
    /// <param name="patchModules">saveSnapshot 补丁模块掩码；0 表示全量快照。</param>
    /// <returns>可直接作为 CallFunctionParam.data 传入的根级参数对象。</returns>
    private static Dictionary<string, object> CreateCloudFunctionRequestData(string action, PlayerCloudSaveSnapshot snapshot, int patchModules)
    {
        Dictionary<string, object> requestData = new Dictionary<string, object>
        {
            { "action", action ?? string.Empty },
            { "patchModules", patchModules }
        };

        if (snapshot != null)
        {
            requestData["snapshot"] = snapshot;
        }

        return requestData;
    }

#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
    /// <summary>
    /// 确保微信小游戏 SDK 与云开发能力已经初始化。
    /// 参考当前项目微信插件的真实类型签名，使用 ICloudConfig 而不是不存在的 CallFunctionInitParam。
    /// </summary>
    /// <param name="onReady">云能力可用后的回调。</param>
    /// <param name="onFailure">初始化失败时的回调。</param>
    private void EnsureWechatCloudInitialized(Action onReady, Action<string> onFailure)
    {
        if (_isWechatCloudInitialized)
        {
            onReady?.Invoke();
            return;
        }

        WX.InitSDK(code =>
        {
            try
            {
                ICloudConfig cloudConfig = new ICloudConfig
                {
                    env = string.IsNullOrWhiteSpace(CloudEnvironmentId) ? "_default_" : CloudEnvironmentId,
                    traceUser = false
                };
                WX.cloud.Init(cloudConfig);
                _isWechatCloudInitialized = true;
                onReady?.Invoke();
            }
            catch (Exception exception)
            {
                onFailure?.Invoke(exception.Message);
            }
        });
    }

    /// <summary>
    /// 在微信小游戏环境中执行真实云函数调用。
    /// data 保持为根级业务对象以满足当前 sgdd_server 协议。
    /// </summary>
    /// <param name="requestData">传给云函数的根级参数对象。</param>
    /// <param name="onSuccess">云函数成功回调，参数为 result 字符串。</param>
    /// <param name="onFailure">云函数失败回调，参数为 errMsg。</param>
    private static void ExecuteWechatCloudFunction(
        Dictionary<string, object> requestData,
        Action<string> onSuccess,
        Action<string> onFailure)
    {
        WX.cloud.CallFunction(new CallFunctionParam
        {
            name = CloudFunctionName,
            data = requestData,
            success = response =>
            {
                onSuccess?.Invoke(response != null ? response.result : null);
            },
            fail = error =>
            {
                onFailure?.Invoke(error != null ? error.errMsg : "wx.cloud.CallFunction fail");
            }
        });
    }
#endif
}
