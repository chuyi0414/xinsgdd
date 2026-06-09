using System;
using System.Collections.Generic;
using GameFramework;
using GameFramework.Resource;
using Spine;
using Spine.Unity;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 游戏资源模块。
/// 统一管理运行时会频繁使用的业务资源预加载与缓存，避免业务层直接散落 Resources.Load。
/// </summary>
public sealed class GameAssetModule
{
    /// <summary>
    /// 预加载资源类别。
    /// 用于在统一回调里区分不同业务资源。
    /// </summary>
    private enum PreloadAssetKind
    {
        /// <summary>
        /// 蛋图标精灵资源。
        /// </summary>
        EggSprite = 1,

        /// <summary>
        /// 宠物 Spine SkeletonData 资源。
        /// </summary>
        PetSkeletonData = 2,

        /// <summary>
        /// 宠物实体预制体资源。
        /// </summary>
        PetEntityPrefab = 3,

        /// <summary>
        /// 水果图标精灵资源。
        /// </summary>
        FruitSprite = 4,

        /// <summary>
        /// 宠物期望食物气泡预制体资源。
        /// </summary>
        PetFoodBubblePrefab = 5,

        /// <summary>
        /// 金币 UI 预制体资源。
        /// </summary>
        GoldCoinPrefab = 6,

        /// <summary>
        /// 产出物 UI 预制体资源。
        /// </summary>
        OutputProducePrefab = 7,

        /// <summary>
        /// 金币点击提示 Toast 预制体资源。
        /// </summary>
        GoldCoinToastPrefab = 8,

        /// <summary>
        /// 每日一关本地预览关卡文本资源。
        /// </summary>
        DailyChallengeLevelText = 9,

        /// <summary>
        /// 战斗消除分数数字精灵资源。
        /// </summary>
        ScoreDigitSprite = 10,

        /// <summary>
        /// 战斗消除分数数字精灵资源（小尺寸，Score/1 套图 64×64）。
        /// 用于等待区每个槽位的分数字图片渲染。
        /// </summary>
        ScoreDigitSmallSprite = 11,

        /// <summary>
        /// 头像图标精灵资源。
        /// </summary>
        HeadPortraitSprite = 12,

        /// <summary>
        /// 头像框图标精灵资源。
        /// </summary>
        HeadPortraitFrameSprite = 13,

        /// <summary>
        /// 建筑图片精灵资源。
        /// 包含升级界面指示器精灵与主界面实体精灵。
        /// </summary>
        ArchitectureSprite = 14,

        /// <summary>
        /// 每日关卡卡图精灵资源。
        /// 从 Fruit 表 DailyChallengePath 加载，专供每日一关消除卡使用。
        /// </summary>
        DailyChallengeCardSprite = 15,

        /// <summary>
        /// 宠物产出物图标精灵资源。
        /// 从 PetProduce 表 IconPath 加载，UI 主界面 OutputBtn 用。
        /// 设计语义：路径为空 / 加载失败均静默忽略，不阻塞主流程，UI 回退到预制体默认图。
        /// </summary>
        ProduceSprite = 16,

        /// <summary>
        /// 宠物材质资源。
        /// 从 PetDataRow.EntityMaterialPath / UiMaterialPath 按需加载，用于替换 Spine atlas 默认材质。
        /// </summary>
        PetMaterial = 17,

        /// <summary>
        /// 主界面 MainUIForm 预制体资源。
        /// 首次点击“进入游戏”时必须打开它，因此启动阶段先走一次 GF 资源加载回调，避免点击帧才冷加载。
        /// </summary>
        MainUIFormPrefab = 18,
    }

    /// <summary>
    /// MainUIForm 预制体的统一资源路径。
    /// </summary>
    private static readonly string MainUIFormPrefabPath = AssetPath.GetUI("Main/MainUIForm");

    /// <summary>
    /// 宠物食物气泡预制体的统一资源路径。
    /// </summary>
    private static readonly string PetFoodBubblePrefabPath = AssetPath.GetUI("Pet/PetFoodBtn");

    /// <summary>
    /// 金币按钮预制体的统一资源路径。
    /// </summary>
    private static readonly string GoldCoinPrefabPath = AssetPath.GetUI("Output/GoldBtn");

    /// <summary>
    /// 产出物按钮预制体的统一资源路径。
    /// </summary>
    private static readonly string OutputProducePrefabPath = AssetPath.GetUI("Output/OutputBtn");

    /// <summary>
    /// 金币点击提示 Toast 预制体的统一资源路径。
    /// </summary>
    private static readonly string GoldCoinToastPrefabPath = AssetPath.GetUI("Toast/GoldCoinToast");

    // 每日一关关卡文本预加载路径已改为数据表驱动，
    // 不再使用硬编码数组。BeginPreloadDailyChallengeLevelTexts 内动态从
    // GameEntry.DataTables.TryGetDefaultDailyChallengeLevelCode() 获取关卡标识码拼装路径。

    /// <summary>
    /// 单次资源加载任务的上下文数据。
    /// </summary>
    private sealed class PendingAssetLoadInfo
    {
        /// <summary>
        /// 资源路径。
        /// </summary>
        public string AssetPath;

        /// <summary>
        /// 资源类别。
        /// </summary>
        public PreloadAssetKind AssetKind;

        /// <summary>
        /// 附加的业务 Code。
        /// 用于水果图标等“路径与业务键并不相同”的资源回调映射。
        /// </summary>
        public string ContextCode;
    }

    /// <summary>
    /// 宠物 SkeletonData 校验信息。
    /// 一个 SkeletonData 可能被多只宠物行复用，因此按路径聚合校验项。
    /// </summary>
    private sealed class PetSkeletonValidationInfo
    {
        /// <summary>
        /// 宠物 Code。
        /// </summary>
        public string PetCode;

        /// <summary>
        /// 待机动画名。
        /// </summary>
        public string IdleAnimationName;

        /// <summary>
        /// 移动动画名。
        /// </summary>
        public string MoveAnimationName;
    }

    /// <summary>
    /// 已缓存的蛋图标，按 IconPath 索引。
    /// </summary>
    private readonly Dictionary<string, Sprite> _eggSpritesByPath = new Dictionary<string, Sprite>(StringComparer.Ordinal);

    /// <summary>
    /// 已缓存的宠物 SkeletonData，按 SkeletonDataPath 索引。
    /// </summary>
    private readonly Dictionary<string, SkeletonDataAsset> _petSkeletonDataAssetsByPath = new Dictionary<string, SkeletonDataAsset>(StringComparer.Ordinal);

    /// <summary>
    /// 已缓存的宠物材质，按材质资源路径索引。
    /// </summary>
    private readonly Dictionary<string, Material> _petMaterialsByPath = new Dictionary<string, Material>(StringComparer.Ordinal);

    /// <summary>
    /// 已缓存的水果图标，按水果 Code 索引。
    /// </summary>
    private readonly Dictionary<string, Sprite> _fruitSpritesByCode = new Dictionary<string, Sprite>(StringComparer.Ordinal);

    /// <summary>
    /// 已缓存的产出物图标，按产出物 Code 索引。
    /// 资源加载失败 / 路径为空的产出物不会进入此字典，UI 查询命中失败将自动回退到预制体默认图。
    /// </summary>
    private readonly Dictionary<string, Sprite> _produceSpritesByCode = new Dictionary<string, Sprite>(StringComparer.Ordinal);

    /// <summary>
    /// 已缓存的消除卡图，按精灵名索引。
    /// 从 Fruit 表 EffectiveDailyChallengePath 末尾文件名反向索引成卡图名。
    /// 例如：Arts/Fruit/DailyChallenge/WP_80001 -> WP_80001。
    /// </summary>
    private readonly Dictionary<string, Sprite> _eliminateCardSpritesByName = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 已缓存的每日一关本地预览关卡文本，按资源路径索引。
    /// </summary>
    private readonly Dictionary<string, TextAsset> _dailyChallengeLevelTextsByPath = new Dictionary<string, TextAsset>(StringComparer.Ordinal);

    /// <summary>
    /// 已缓存的分数数字精灵，按数字（0~9）索引。
    /// </summary>
    private readonly Dictionary<int, Sprite> _scoreDigitSpritesByDigit = new Dictionary<int, Sprite>(10);

    /// <summary>
    /// 已缓存的小尺寸分数数字精灵（Score/1 套图 64×64），按数字（0~9）索引。
    /// 用于等待区每个槽位的分数字图片渲染。
    /// </summary>
    private readonly Dictionary<int, Sprite> _scoreDigitSmallSpritesByDigit = new Dictionary<int, Sprite>(10);

    /// <summary>
    /// 已缓存的头像图标，按 IconPath 索引。
    /// </summary>
    private readonly Dictionary<string, Sprite> _headPortraitSpritesByPath = new Dictionary<string, Sprite>(StringComparer.Ordinal);

    /// <summary>
    /// 已缓存的头像框图标，按 IconPath 索引。
    /// </summary>
    private readonly Dictionary<string, Sprite> _headPortraitFrameSpritesByPath = new Dictionary<string, Sprite>(StringComparer.Ordinal);

    /// <summary>
    /// 已缓存的建筑精灵，按资源路径索引。
    /// 升级界面指示器与主界面实体都复用这一份缓存。
    /// </summary>
    private readonly Dictionary<string, Sprite> _architectureSpritesByPath = new Dictionary<string, Sprite>(StringComparer.Ordinal);

    /// <summary>
    /// 每个 SkeletonData 路径对应的动画校验信息集合。
    /// </summary>
    private readonly Dictionary<string, List<PetSkeletonValidationInfo>> _petValidationInfosByPath = new Dictionary<string, List<PetSkeletonValidationInfo>>(StringComparer.Ordinal);

    /// <summary>
    /// 当前仍在加载中的蛋图标路径集合。
    /// </summary>
    private readonly HashSet<string> _loadingEggAssetPaths = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前仍在加载中的宠物 SkeletonData 路径集合。
    /// </summary>
    private readonly HashSet<string> _loadingPetAssetPaths = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前仍在加载中的宠物材质路径集合。
    /// </summary>
    private readonly HashSet<string> _loadingPetMaterialPaths = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前仍在加载中的水果图标路径集合。
    /// </summary>
    private readonly HashSet<string> _loadingFruitAssetPaths = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前仍在加载中的产出物图标路径集合。
    /// </summary>
    private readonly HashSet<string> _loadingProduceSpritePaths = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前仍在加载中的每日关卡卡图路径集合。
    /// </summary>
    private readonly HashSet<string> _loadingDailyChallengeCardSpritePaths = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前仍在加载中的宠物实体预制体路径集合。
    /// </summary>
    private readonly HashSet<string> _loadingPetEntityPrefabPaths = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前仍在加载中的 MainUIForm 预制体路径集合。
    /// 正常情况下只有一个路径；仍使用 HashSet 是为了复用现有 TryLoadAsset 去重模型。
    /// </summary>
    private readonly HashSet<string> _loadingMainUIFormPrefabPaths = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前仍在加载中的宠物食物气泡预制体路径集合。
    /// </summary>
    private readonly HashSet<string> _loadingPetFoodBubblePrefabPaths = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前仍在加载中的金币 UI 预制体路径集合。
    /// </summary>
    private readonly HashSet<string> _loadingGoldCoinPrefabPaths = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前仍在加载中的产出物 UI 预制体路径集合。
    /// </summary>
    private readonly HashSet<string> _loadingOutputProducePrefabPaths = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前仍在加载中的金币点击提示 Toast 预制体路径集合。
    /// </summary>
    private readonly HashSet<string> _loadingGoldCoinToastPrefabPaths = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前仍在加载中的每日一关关卡文本路径集合。
    /// </summary>
    private readonly HashSet<string> _loadingDailyChallengeLevelTextPaths = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前仍在加载中的分数数字精灵路径集合。
    /// </summary>
    private readonly HashSet<string> _loadingScoreDigitSpritePaths = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前仍在加载中的小尺寸分数数字精灵路径集合（Score/1 套图）。
    /// </summary>
    private readonly HashSet<string> _loadingScoreDigitSmallSpritePaths = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前仍在加载中的头像图标路径集合。
    /// </summary>
    private readonly HashSet<string> _loadingHeadPortraitAssetPaths = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前仍在加载中的头像框图标路径集合。
    /// </summary>
    private readonly HashSet<string> _loadingHeadPortraitFrameAssetPaths = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前仍在加载中的建筑精灵路径集合。
    /// </summary>
    private readonly HashSet<string> _loadingArchitectureSpritePaths = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 统一复用的资源加载回调函数集。
    /// </summary>
    private readonly LoadAssetCallbacks _loadAssetCallbacks;

    /// <summary>
    /// 当前待完成的蛋图标加载数量。
    /// </summary>
    private int _pendingEggAssetCount;

    /// <summary>
    /// 当前待完成的宠物 SkeletonData 加载数量。
    /// </summary>
    private int _pendingPetAssetCount;

    /// <summary>
    /// 当前待完成的宠物材质加载数量。
    /// </summary>
    private int _pendingPetMaterialCount;

    /// <summary>
    /// 当前待完成的水果图标加载数量。
    /// </summary>
    private int _pendingFruitAssetCount;

    /// <summary>
    /// 当前待完成的产出物图标加载数量。
    /// </summary>
    private int _pendingProduceSpriteCount;

    /// <summary>
    /// 当前待完成的每日关卡卡图加载数量。
    /// </summary>
    private int _pendingDailyChallengeCardSpriteCount;

    /// <summary>
    /// 当前待完成的宠物实体预制体加载数量。
    /// </summary>
    private int _pendingPetEntityPrefabCount;

    /// <summary>
    /// 当前待完成的 MainUIForm 预制体加载数量。
    /// 初始为 0；启动预热后加 1，成功或失败回调后减回 0。
    /// </summary>
    private int _pendingMainUIFormPrefabCount;

    /// <summary>
    /// 当前待完成的宠物食物气泡预制体加载数量。
    /// </summary>
    private int _pendingPetFoodBubblePrefabCount;

    /// <summary>
    /// 当前待完成的金币 UI 预制体加载数量。
    /// </summary>
    private int _pendingGoldCoinPrefabCount;

    /// <summary>
    /// 当前待完成的产出物 UI 预制体加载数量。
    /// </summary>
    private int _pendingOutputProducePrefabCount;

    /// <summary>
    /// 当前待完成的金币点击提示 Toast 预制体加载数量。
    /// </summary>
    private int _pendingGoldCoinToastPrefabCount;

    /// <summary>
    /// 当前待完成的每日一关关卡文本加载数量。
    /// </summary>
    private int _pendingDailyChallengeLevelTextCount;

    /// <summary>
    /// 当前待完成的分数数字精灵加载数量。
    /// </summary>
    private int _pendingScoreDigitSpriteCount;

    /// <summary>
    /// 当前待完成的小尺寸分数数字精灵加载数量（Score/1 套图）。
    /// </summary>
    private int _pendingScoreDigitSmallSpriteCount;

    /// <summary>
    /// 当前待完成的头像图标加载数量。
    /// </summary>
    private int _pendingHeadPortraitAssetCount;

    /// <summary>
    /// 当前待完成的头像框图标加载数量。
    /// </summary>
    private int _pendingHeadPortraitFrameAssetCount;

    /// <summary>
    /// 当前待完成的建筑精灵加载数量。
    /// </summary>
    private int _pendingArchitectureSpriteCount;

    /// <summary>
    /// 是否已经发起过蛋图标预加载。
    /// </summary>
    private bool _eggPreloadRequested;

    /// <summary>
    /// 是否已经发起过宠物 SkeletonData 预加载。
    /// </summary>
    private bool _petPreloadRequested;

    /// <summary>
    /// 是否已经发起过宠物材质预加载。
    /// </summary>
    private bool _petMaterialPreloadRequested;

    /// <summary>
    /// 是否已经发起过水果图标预加载。
    /// </summary>
    private bool _fruitPreloadRequested;

    /// <summary>
    /// 是否已经发起过每日关卡卡图预加载。
    /// </summary>
    private bool _dailyChallengeCardSpritePreloadRequested;

    /// <summary>
    /// 是否已经发起过宠物实体预制体预热。
    /// </summary>
    private bool _petEntityPrefabPreloadRequested;

    /// <summary>
    /// 是否已经发起过 MainUIForm 预制体预热。
    /// 初始为 false；BeginPreloadRequiredAssets 首次调用后置为 true，避免重复发起冷加载。
    /// </summary>
    private bool _mainUIFormPrefabPreloadRequested;

    /// <summary>
    /// 是否已经发起过宠物食物气泡预制体预热。
    /// </summary>
    private bool _petFoodBubblePrefabPreloadRequested;

    /// <summary>
    /// 是否已经发起过金币 UI 预制体预热。
    /// </summary>
    private bool _goldCoinPrefabPreloadRequested;

    /// <summary>
    /// 是否已经发起过产出物 UI 预制体预热。
    /// </summary>
    private bool _outputProducePrefabPreloadRequested;

    /// <summary>
    /// 是否已经发起过金币点击提示 Toast 预制体预热。
    /// </summary>
    private bool _goldCoinToastPrefabPreloadRequested;

    /// <summary>
    /// 是否已经发起过每日一关关卡文本预加载。
    /// </summary>
    private bool _dailyChallengeLevelTextPreloadRequested;

    /// <summary>
    /// 是否已经发起过分数数字精灵预加载。
    /// </summary>
    private bool _scoreDigitSpritePreloadRequested;

    /// <summary>
    /// 是否已经发起过小尺寸分数数字精灵预加载（Score/1 套图）。
    /// </summary>
    private bool _scoreDigitSmallSpritePreloadRequested;

    /// <summary>
    /// 是否已经发起过头像图标预加载。
    /// </summary>
    private bool _headPortraitPreloadRequested;

    /// <summary>
    /// 是否已经发起过头像框图标预加载。
    /// </summary>
    private bool _headPortraitFramePreloadRequested;

    /// <summary>
    /// 是否已经发起过建筑精灵预加载。
    /// </summary>
    private bool _architectureSpritePreloadRequested;

    /// <summary>
    /// 蛋图标预加载是否已经完成。
    /// </summary>
    private bool _eggPreloadCompleted;

    /// <summary>
    /// 宠物 SkeletonData 预加载是否已经完成。
    /// </summary>
    private bool _petPreloadCompleted;

    /// <summary>
    /// 宠物材质预加载是否已经完成。
    /// </summary>
    private bool _petMaterialPreloadCompleted;

    /// <summary>
    /// 水果图标预加载是否已经完成。
    /// </summary>
    private bool _fruitPreloadCompleted;

    /// <summary>
    /// 每日关卡卡图预加载是否已经完成。
    /// </summary>
    private bool _dailyChallengeCardSpritePreloadCompleted;

    /// <summary>
    /// 宠物实体预制体预热是否已经完成。
    /// </summary>
    private bool _petEntityPrefabPreloadCompleted;

    /// <summary>
    /// MainUIForm 预制体预热是否已经完成。
    /// 这是“进入游戏”按钮解锁条件之一，用来把主界面首次资源加载前移到 Load 页。
    /// </summary>
    private bool _mainUIFormPrefabPreloadCompleted;

    /// <summary>
    /// 宠物食物气泡预制体预热是否已经完成。
    /// </summary>
    private bool _petFoodBubblePrefabPreloadCompleted;

    /// <summary>
    /// 金币 UI 预制体预热是否已经完成。
    /// </summary>
    private bool _goldCoinPrefabPreloadCompleted;

    /// <summary>
    /// 产出物 UI 预制体预热是否已经完成。
    /// </summary>
    private bool _outputProducePrefabPreloadCompleted;

    /// <summary>
    /// 金币点击提示 Toast 预制体预热是否已经完成。
    /// </summary>
    private bool _goldCoinToastPrefabPreloadCompleted;

    /// <summary>
    /// 每日一关关卡文本预加载是否已经完成。
    /// </summary>
    private bool _dailyChallengeLevelTextPreloadCompleted;

    /// <summary>
    /// 分数数字精灵预加载是否已经完成。
    /// </summary>
    private bool _scoreDigitSpritePreloadCompleted;

    /// <summary>
    /// 小尺寸分数数字精灵预加载是否已经完成（Score/1 套图）。
    /// </summary>
    private bool _scoreDigitSmallSpritePreloadCompleted;

    /// <summary>
    /// 头像图标预加载是否已经完成。
    /// </summary>
    private bool _headPortraitPreloadCompleted;

    /// <summary>
    /// 头像框图标预加载是否已经完成。
    /// </summary>
    private bool _headPortraitFramePreloadCompleted;

    /// <summary>
    /// 建筑精灵预加载是否已经完成。
    /// </summary>
    private bool _architectureSpritePreloadCompleted;

    /// <summary>
    /// 已预热缓存的宠物实体预制体。
    /// </summary>
    private GameObject _petEntityPrefab;

    /// <summary>
    /// 已预热缓存的 MainUIForm 预制体。
    /// 这里持有引用的目的不是手动实例化，而是防止资源系统在打开主界面前把预热结果释放掉。
    /// </summary>
    private GameObject _mainUIFormPrefab;

    /// <summary>
    /// 已预热缓存的宠物食物气泡预制体。
    /// </summary>
    private GameObject _petFoodBubblePrefab;

    /// <summary>
    /// 已预热缓存的金币 UI 预制体。
    /// </summary>
    private GameObject _goldCoinPrefab;

    /// <summary>
    /// 已预热缓存的产出物 UI 预制体。
    /// </summary>
    private GameObject _outputProducePrefab;

    /// <summary>
    /// 已预热缓存的金币点击提示 Toast 预制体。
    /// </summary>
    private GameObject _goldCoinToastPrefab;

    /// <summary>
    /// 预加载过程中是否出现过失败。
    /// </summary>
    private bool _hasPreloadFailure;

    /// <summary>
    /// 最近一次预加载失败信息。
    /// </summary>
    private string _lastErrorMessage;

    /// <summary>
    /// 预加载状态变化事件。
    /// </summary>
    public event Action PreloadStateChanged;

    /// <summary>
    /// 宠物 SkeletonData 加载状态变化事件。
    /// 参数为 SkeletonData 资源路径；成功写入缓存或加载失败都会触发，便于正在等待该路径的宠物实体及时刷新或停止等待。
    /// </summary>
    public event Action<string> PetSkeletonDataStateChanged;

    /// <summary>
    /// 宠物材质加载状态变化事件。
    /// 参数为材质资源路径；成功写入缓存或加载失败都会触发，便于正在等待该路径的 UI/实体及时刷新材质。
    /// </summary>
    public event Action<string> PetMaterialStateChanged;

    /// <summary>
    /// 产出物图标加载完成事件。
    /// 参数为产出物 Code；懒加载成功后触发，便于 UI 刷新对应图标的显示。
    /// </summary>
    public event Action<string> ProduceSpriteLoaded;

    /// <summary>
    /// 初始化资源模块并创建统一回调集。
    /// </summary>
    public GameAssetModule()
    {
        _loadAssetCallbacks = new LoadAssetCallbacks(OnLoadAssetSuccess, OnLoadAssetFailure);
    }

    /// <summary>
    /// 必需业务资源的预加载流程是否都已结束。
    /// 这里不要求全部资源都成功命中，只要预加载任务已经完成即可继续主流程。
    /// </summary>
    // 仅保留主界面打开时必须就绪的核心资源；
    // 头像/头像框/建筑/战斗分数/每日一关等资源仍在后台预加载，但不阻塞「进入游戏」按钮，
    // 从而降低 WebGL/微信小游戏 WASM 内存峰值，避免纹理并发解压导致 memory access out of bounds。
    // 宠物 SkeletonData 含 Spine atlas 纹理（2048×2048），是内存最大头。
    // 移至懒加载：后台继续预加载，但不阻塞「进入游戏」按钮，
    // 主界面打开后宠物按加载进度逐步出现在场地上。
    public bool IsReady => _eggPreloadCompleted
        && _fruitPreloadCompleted
        && _mainUIFormPrefabPreloadCompleted
        && _petEntityPrefabPreloadCompleted
        && _petFoodBubblePrefabPreloadCompleted
        && _goldCoinPrefabPreloadCompleted
        && _outputProducePrefabPreloadCompleted
        && _goldCoinToastPrefabPreloadCompleted;

    /// <summary>
    /// 当前是否已经出现预加载失败。
    /// 失败不会阻塞主流程，只用于日志与调试观察。
    /// </summary>
    public bool HasPreloadFailure => _hasPreloadFailure;

    /// <summary>
    /// 最近一次预加载失败信息。
    /// 失败不会阻塞主流程，只保留给外部排查问题使用。
    /// </summary>
    public string LastErrorMessage => _lastErrorMessage;

    /// <summary>
    /// 按当前已注册的数据表启动必需业务资源预加载。
    /// 重复调用是安全的，只会补齐尚未开始的部分。
    /// </summary>
    public void BeginPreloadRequiredAssets()
    {
        if (!_dailyChallengeLevelTextPreloadRequested)
        {
            BeginPreloadDailyChallengeLevelTexts();
        }

        if (!_scoreDigitSpritePreloadRequested)
        {
            BeginPreloadScoreDigitSprites();
        }

        if (!_scoreDigitSmallSpritePreloadRequested)
        {
            BeginPreloadScoreDigitSmallSprites();
        }

        if (!_petEntityPrefabPreloadRequested)
        {
            BeginPreloadPetEntityPrefab();
        }

        if (!_mainUIFormPrefabPreloadRequested)
        {
            BeginPreloadMainUIFormPrefab();
        }

        if (!_petFoodBubblePrefabPreloadRequested)
        {
            BeginPreloadPetFoodBubblePrefab();
        }

        if (!_goldCoinPrefabPreloadRequested)
        {
            BeginPreloadGoldCoinPrefab();
        }

        if (!_outputProducePrefabPreloadRequested)
        {
            BeginPreloadOutputProducePrefab();
        }

        if (!_goldCoinToastPrefabPreloadRequested)
        {
            BeginPreloadGoldCoinToastPrefab();
        }

        if (GameEntry.DataTables == null)
        {
            return;
        }

        if (!_eggPreloadRequested && GameEntry.DataTables.IsAvailable<EggDataRow>())
        {
            BeginPreloadEggSprites(GameEntry.DataTables.GetAllDataRows<EggDataRow>());
        }

        if (!_petPreloadRequested && GameEntry.DataTables.IsAvailable<PetDataRow>())
        {
            SkipStartupPetSkeletonDataPreload();
        }

        if (!_fruitPreloadRequested && GameEntry.DataTables.IsAvailable<FruitDataRow>())
        {
            BeginPreloadFruitSprites(GameEntry.DataTables.GetAllDataRows<FruitDataRow>());
        }

        if (!_headPortraitPreloadRequested && GameEntry.DataTables.IsAvailable<HeadPortraitDataRow>())
        {
            BeginPreloadHeadPortraitSprites(GameEntry.DataTables.GetAllDataRows<HeadPortraitDataRow>());
        }

        if (!_headPortraitFramePreloadRequested && GameEntry.DataTables.IsAvailable<HeadPortraitFrameDataRow>())
        {
            BeginPreloadHeadPortraitFrameSprites(GameEntry.DataTables.GetAllDataRows<HeadPortraitFrameDataRow>());
        }

        if (!_architectureSpritePreloadRequested && GameEntry.DataTables.IsAvailable<ArchitectureDataRow>())
        {
            BeginPreloadArchitectureSprites(GameEntry.DataTables.GetAllDataRows<ArchitectureDataRow>());
        }

    }

    /// <summary>
    /// 获取头像图标缓存。
    /// </summary>
    /// <param name="iconPath">头像图标资源路径。</param>
    /// <param name="sprite">命中的图标资源。</param>
    /// <returns>是否命中缓存。</returns>
    public bool TryGetHeadPortraitSprite(string iconPath, out Sprite sprite)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            sprite = null;
            return false;
        }

        return _headPortraitSpritesByPath.TryGetValue(iconPath, out sprite) && sprite != null;
    }

    /// <summary>
    /// 尝试从缓存中获取头像框图标精灵。
    /// </summary>
    /// <param name="iconPath">图标资源路径。</param>
    /// <param name="sprite">命中的精灵资源。</param>
    /// <returns>是否命中缓存。</returns>
    public bool TryGetHeadPortraitFrameSprite(string iconPath, out Sprite sprite)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            sprite = null;
            return false;
        }

        return _headPortraitFrameSpritesByPath.TryGetValue(iconPath, out sprite) && sprite != null;
    }

    /// <summary>
    /// 尝试从缓存中获取建筑精灵。
    /// 升级界面指示器与主界面实体统一按资源路径查询。
    /// </summary>
    /// <param name="assetPath">建筑精灵资源路径。</param>
    /// <param name="sprite">命中的精灵资源。</param>
    /// <returns>是否命中缓存。</returns>
    public bool TryGetArchitectureSprite(string assetPath, out Sprite sprite)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            sprite = null;
            return false;
        }

        return _architectureSpritesByPath.TryGetValue(assetPath, out sprite) && sprite != null;
    }

    /// <summary>
    /// 获取蛋图标缓存。
    /// </summary>
    public bool TryGetEggSprite(string iconPath, out Sprite sprite)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            sprite = null;
            return false;
        }

        return _eggSpritesByPath.TryGetValue(iconPath, out sprite) && sprite != null;
    }

    /// <summary>
    /// 获取宠物 SkeletonData 缓存。
    /// </summary>
    public bool TryGetPetSkeletonDataAsset(string skeletonDataPath, out SkeletonDataAsset skeletonDataAsset)
    {
        if (string.IsNullOrWhiteSpace(skeletonDataPath))
        {
            skeletonDataAsset = null;
            return false;
        }

        return _petSkeletonDataAssetsByPath.TryGetValue(skeletonDataPath, out skeletonDataAsset) && skeletonDataAsset != null;
    }

    /// <summary>
    /// 按需请求单只宠物的实体 SkeletonData 资源。
    /// 启动阶段已经跳过宠物全量预加载，因此蛋孵化出非默认宠物时，需要由实体显示链路按当前 PetDataRow 精准补齐该资源。
    /// </summary>
    /// <param name="row">当前宠物表行，初始状态由 PetPlacementModule 抽取结果决定。</param>
    /// <returns>请求发起前该实体 SkeletonData 是否已经命中缓存。</returns>
    public bool RequestPetEntitySkeletonDataAsset(PetDataRow row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.EntitySkeletonDataPath))
        {
            return false;
        }

        if (_petSkeletonDataAssetsByPath.TryGetValue(row.EntitySkeletonDataPath, out SkeletonDataAsset skeletonDataAsset) && skeletonDataAsset != null)
        {
            ValidatePetSkeletonData(row.EntitySkeletonDataPath, skeletonDataAsset);
            return true;
        }

        if (!_loadingPetAssetPaths.Contains(row.EntitySkeletonDataPath))
        {
            StartLoadPetSkeletonDataPath(row, row.EntitySkeletonDataPath);
        }

        return false;
    }

    /// <summary>
    /// 按需请求单只宠物的 SkeletonData 资源（实体和 UI 共用）。
    /// PetTJUIForm 原来使用 UiSkeletonDataPath，现在统一走 EntitySkeletonDataPath 配合 UiMaterialPath 区分材质。
    /// </summary>
    /// <param name="row">当前宠物表行。</param>
    /// <returns>请求发起前该骨架数据是否已经命中缓存。</returns>
    public bool RequestPetSkeletonDataAsset(PetDataRow row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.EntitySkeletonDataPath))
        {
            return false;
        }

        if (_petSkeletonDataAssetsByPath.TryGetValue(row.EntitySkeletonDataPath, out SkeletonDataAsset skeletonDataAsset) && skeletonDataAsset != null)
        {
            ValidatePetSkeletonData(row.EntitySkeletonDataPath, skeletonDataAsset);
            return true;
        }

        if (!_loadingPetAssetPaths.Contains(row.EntitySkeletonDataPath))
        {
            StartLoadPetSkeletonDataPath(row, row.EntitySkeletonDataPath);
        }

        return false;
    }

    /// <summary>
    /// 尝试获取已缓存的宠物材质。
    /// </summary>
    /// <param name="materialPath">材质资源路径（PetDataRow.EntityMaterialPath 或 UiMaterialPath）。</param>
    /// <param name="material">命中的材质资源。</param>
    /// <returns>是否命中缓存。</returns>
    public bool TryGetPetMaterial(string materialPath, out Material material)
    {
        if (string.IsNullOrWhiteSpace(materialPath))
        {
            material = null;
            return false;
        }

        return _petMaterialsByPath.TryGetValue(materialPath, out material) && material != null;
    }

    /// <summary>
    /// 按需请求单只宠物的实体材质资源。
    /// </summary>
    /// <param name="row">当前宠物表行。</param>
    /// <returns>请求发起前该材质是否已经命中缓存。</returns>
    public bool RequestPetEntityMaterial(PetDataRow row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.EntityMaterialPath))
        {
            return false;
        }

        if (_petMaterialsByPath.TryGetValue(row.EntityMaterialPath, out Material material) && material != null)
        {
            return true;
        }

        if (!_loadingPetMaterialPaths.Contains(row.EntityMaterialPath))
        {
            StartLoadPetMaterial(row.EntityMaterialPath);
        }

        return false;
    }

    /// <summary>
    /// 按需请求单只宠物的 UI 材质资源。
    /// </summary>
    /// <param name="row">当前宠物表行。</param>
    /// <returns>请求发起前该材质是否已经命中缓存。</returns>
    public bool RequestPetUiMaterial(PetDataRow row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.UiMaterialPath))
        {
            return false;
        }

        if (_petMaterialsByPath.TryGetValue(row.UiMaterialPath, out Material material) && material != null)
        {
            return true;
        }

        if (!_loadingPetMaterialPaths.Contains(row.UiMaterialPath))
        {
            StartLoadPetMaterial(row.UiMaterialPath);
        }

        return false;
    }

    /// <summary>
    /// 获取水果图标缓存。
    /// </summary>
    /// <param name="fruitCode">水果 Code。</param>
    /// <param name="sprite">命中的图标资源。</param>
    /// <returns>是否命中缓存。</returns>
    public bool TryGetFruitSprite(string fruitCode, out Sprite sprite)
    {
        if (string.IsNullOrWhiteSpace(fruitCode))
        {
            sprite = null;
            return false;
        }

        return _fruitSpritesByCode.TryGetValue(fruitCode, out sprite) && sprite != null;
    }

    /// <summary>
    /// 获取产出物图标缓存。
    /// 命中失败时调用方应自行回退到预制体默认图（产出物图标允许缺失，且不会阻塞主流程）。
    /// </summary>
    /// <param name="produceCode">产出物 Code。</param>
    /// <param name="sprite">命中的图标资源。</param>
    /// <returns>是否命中缓存。</returns>
    public bool TryGetProduceSprite(string produceCode, out Sprite sprite)
    {
        if (string.IsNullOrWhiteSpace(produceCode))
        {
            sprite = null;
            return false;
        }

        return _produceSpritesByCode.TryGetValue(produceCode, out sprite) && sprite != null;
    }

    /// <summary>
    /// 获取消除卡图缓存。
    /// 当前按精灵名读取，例如 WP_80001。
    /// </summary>
    /// <param name="spriteName">卡图精灵名。</param>
    /// <param name="sprite">命中的图标资源。</param>
    /// <returns>是否命中缓存。</returns>
    public bool TryGetEliminateCardSprite(string spriteName, out Sprite sprite)
    {
        if (string.IsNullOrWhiteSpace(spriteName))
        {
            sprite = null;
            return false;
        }

        return _eliminateCardSpritesByName.TryGetValue(spriteName.Trim(), out sprite) && sprite != null;
    }

    /// <summary>
    /// 获取每日一关本地预览关卡文本缓存。
    /// </summary>
    /// <param name="assetPath">关卡资源路径。</param>
    /// <param name="levelText">命中的文本资源。</param>
    /// <returns>是否命中缓存。</returns>
    public bool TryGetDailyChallengeLevelText(string assetPath, out TextAsset levelText)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            levelText = null;
            return false;
        }

        return _dailyChallengeLevelTextsByPath.TryGetValue(assetPath.Trim(), out levelText) && levelText != null;
    }

    /// <summary>
    /// 判断指定路径的每日一关关卡文本是否已在缓存中。
    /// 供 MainUIForm.OverrideDailyChallengeLevel 使用，避免重复加载。
    /// </summary>
    /// <param name="assetPath">关卡资源路径。</param>
    /// <returns>true=已缓存或正在加载中。</returns>
    public bool HasDailyChallengeLevelText(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return false;
        }

        string key = assetPath.Trim();
        return _dailyChallengeLevelTextsByPath.ContainsKey(key) || _loadingDailyChallengeLevelTextPaths.Contains(key);
    }

    /// <summary>
    /// 按需加载一份每日一关关卡文本（Cloud 下发新关卡后的补充加载入口）。
    /// 与预加载不同的是，这里不会参与预加载完成度的判定，仅填充缓存。
    /// </summary>
    /// <param name="assetPath">关卡资源路径，如 Configs/Levels/5-3。</param>
    public void LoadDailyChallengeLevelTextOnDemand(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return;
        }

        if (HasDailyChallengeLevelText(assetPath))
        {
            return;
        }

        if (!TryLoadAsset(assetPath, typeof(TextAsset), PreloadAssetKind.DailyChallengeLevelText))
        {
            Log.Warning("按需加载每日一关关卡文本失败，Path='{0}'。", assetPath);
        }
    }

    /// <summary>
    /// 获取分数数字精灵缓存。
    /// </summary>
    /// <param name="digit">数字 0~9。</param>
    /// <param name="sprite">命中的精灵资源。</param>
    /// <returns>是否命中缓存。</returns>
    public bool TryGetScoreDigitSprite(int digit, out Sprite sprite)
    {
        if (digit < 0 || digit > 9)
        {
            sprite = null;
            return false;
        }

        return _scoreDigitSpritesByDigit.TryGetValue(digit, out sprite) && sprite != null;
    }

    /// <summary>
    /// 获取小尺寸分数数字精灵缓存（Score/1 套图 64×64）。
    /// 用于等待区每个槽位的分数字图片渲染。
    /// </summary>
    /// <param name="digit">数字 0~9。</param>
    /// <param name="sprite">命中的精灵资源。</param>
    /// <returns>是否命中缓存。</returns>
    public bool TryGetScoreDigitSmallSprite(int digit, out Sprite sprite)
    {
        if (digit < 0 || digit > 9)
        {
            sprite = null;
            return false;
        }

        return _scoreDigitSmallSpritesByDigit.TryGetValue(digit, out sprite) && sprite != null;
    }

    /// <summary>
    /// 获取宠物期望食物气泡预制体缓存。
    /// </summary>
    /// <param name="petFoodBubblePrefab">命中的预制体资源。</param>
    /// <returns>是否命中缓存。</returns>
    public bool TryGetPetFoodBubblePrefab(out GameObject petFoodBubblePrefab)
    {
        petFoodBubblePrefab = _petFoodBubblePrefab;
        return petFoodBubblePrefab != null;
    }

    /// <summary>
    /// 获取金币 UI 预制体缓存。
    /// </summary>
    /// <param name="goldCoinPrefab">命中的预制体资源。</param>
    /// <returns>是否命中缓存。</returns>
    public bool TryGetGoldCoinPrefab(out GameObject goldCoinPrefab)
    {
        goldCoinPrefab = _goldCoinPrefab;
        return goldCoinPrefab != null;
    }

    /// <summary>
    /// 获取产出物 UI 预制体缓存。
    /// </summary>
    /// <param name="outputProducePrefab">命中的预制体资源。</param>
    /// <returns>是否命中缓存。</returns>
    public bool TryGetOutputProducePrefab(out GameObject outputProducePrefab)
    {
        outputProducePrefab = _outputProducePrefab;
        return outputProducePrefab != null;
    }

    /// <summary>
    /// 获取金币点击提示 Toast 预制体缓存。
    /// </summary>
    /// <param name="goldCoinToastPrefab">命中的预制体资源。</param>
    /// <returns>是否命中缓存。</returns>
    public bool TryGetGoldCoinToastPrefab(out GameObject goldCoinToastPrefab)
    {
        goldCoinToastPrefab = _goldCoinToastPrefab;
        return goldCoinToastPrefab != null;
    }

    /// <summary>
    /// 根据蛋表批量预加载图标资源。
    /// </summary>
    private void BeginPreloadEggSprites(EggDataRow[] rows)
    {
        _eggPreloadRequested = true;
        _eggPreloadCompleted = false;

        if (rows == null || rows.Length == 0)
        {
            RegisterFailure("预加载蛋图标失败，蛋表为空。");
            UpdatePreloadCompletionState();
            NotifyPreloadStateChanged();
            return;
        }

        for (int i = 0; i < rows.Length; i++)
        {
            EggDataRow row = rows[i];
            if (row == null)
            {
                RegisterFailure("预加载蛋图标失败，蛋表存在空行。");
                continue;
            }

            StartLoadEggSprite(row);
        }

        UpdatePreloadCompletionState();
        NotifyPreloadStateChanged();
    }

    /// <summary>
    /// 根据宠物表批量预加载 SkeletonData 资源。
    /// </summary>
    private void BeginPreloadPetSkeletonDataAssets(PetDataRow[] rows)
    {
        _petPreloadRequested = true;
        _petPreloadCompleted = false;

        if (rows == null || rows.Length == 0)
        {
            RegisterFailure("预加载宠物 SkeletonData 失败，宠物表为空。");
            UpdatePreloadCompletionState();
            NotifyPreloadStateChanged();
            return;
        }

        for (int i = 0; i < rows.Length; i++)
        {
            PetDataRow row = rows[i];
            if (row == null)
            {
                RegisterFailure("预加载宠物 SkeletonData 失败，宠物表存在空行。");
                continue;
            }

            StartLoadPetSkeletonData(row);
        }

        UpdatePreloadCompletionState();
        NotifyPreloadStateChanged();
    }

    /// <summary>
    /// 启动阶段跳过宠物 SkeletonData 预加载。
    /// LoadUIForm 自身引用到的宠物资源会由 Addressables 依赖链随界面预制体自动加载，这里只负责把宠物预加载门标记为完成，避免 IsReady 永远等待。
    /// </summary>
    private void SkipStartupPetSkeletonDataPreload()
    {
        _petPreloadRequested = true;
        _petPreloadCompleted = true;
        NotifyPreloadStateChanged();
    }

    /// <summary>
    /// 根据水果表批量预加载水果图标。
    /// </summary>
    /// <param name="rows">水果表行集合。</param>
    private void BeginPreloadFruitSprites(FruitDataRow[] rows)
    {
        _fruitPreloadRequested = true;
        _fruitPreloadCompleted = false;

        if (rows == null || rows.Length == 0)
        {
            RegisterFailure("预加载水果图标失败，水果表为空。");
            UpdatePreloadCompletionState();
            NotifyPreloadStateChanged();
            return;
        }

        for (int i = 0; i < rows.Length; i++)
        {
            FruitDataRow row = rows[i];
            if (row == null)
            {
                RegisterFailure("预加载水果图标失败，水果表存在空行。");
                continue;
            }

            StartLoadFruitSprite(row);
        }

        // 同步发起每日关卡卡图预加载（独立于水果图标链路）。
        if (!_dailyChallengeCardSpritePreloadRequested)
        {
            BeginPreloadDailyChallengeCardSprites(rows);
        }

        UpdatePreloadCompletionState();
        NotifyPreloadStateChanged();
    }

    /// <summary>
    /// 根据建筑图片配置表批量预加载建筑精灵。
    /// 指示器精灵与实体精灵统一复用路径缓存，重复路径只会实际加载一次。
    /// </summary>
    /// <param name="rows">建筑图片配置表行集合。</param>
    private void BeginPreloadArchitectureSprites(ArchitectureDataRow[] rows)
    {
        _architectureSpritePreloadRequested = true;
        _architectureSpritePreloadCompleted = false;

        if (rows == null || rows.Length == 0)
        {
            RegisterFailure("预加载建筑精灵失败，建筑图片表为空。");
            UpdatePreloadCompletionState();
            NotifyPreloadStateChanged();
            return;
        }

        for (int i = 0; i < rows.Length; i++)
        {
            ArchitectureDataRow row = rows[i];
            if (row == null)
            {
                RegisterFailure("预加载建筑精灵失败，建筑图片表存在空行。");
                continue;
            }

            StartLoadArchitectureSprite(row.IndicatorSpritePath);
            StartLoadArchitectureSprite(row.EntitySpritePath);
        }

        UpdatePreloadCompletionState();
        NotifyPreloadStateChanged();
    }

    /// <summary>
    /// 预加载分数数字精灵（0~9）。
    /// 精灵路径格式：Arts/Combat/Eliminate/Score/2/{数字}。
    /// 当前使用 Score/2 套图（256×256），如需切换到 Score/1 套图（64×64），
    /// 修改下方 _scoreDigitSpriteSubFolder 即可。
    /// </summary>
    private void BeginPreloadScoreDigitSprites()
    {
        _scoreDigitSpritePreloadRequested = true;
        _scoreDigitSpritePreloadCompleted = false;

        // ⚠️ 避坑：当前硬编码使用 Score/2 子文件夹。
        // 若后续需要动态切换，改为从配置表读取。
        const string subFolder = "2";

        for (int i = 0; i < 10; i++)
        {
            string assetPath = Utility.Text.Format("{0}{1}/{2}", AssetPath.CombatScoreDigitRoot, subFolder, i);

            if (_scoreDigitSpritesByDigit.ContainsKey(i) || _loadingScoreDigitSpritePaths.Contains(assetPath))
            {
                continue;
            }

            if (!TryLoadAsset(assetPath, typeof(Sprite), PreloadAssetKind.ScoreDigitSprite, i.ToString()))
            {
                RegisterFailure(Utility.Text.Format("预加载分数数字精灵失败，无法开始加载资源，Digit='{0}'，Path='{1}'。", i, assetPath));
            }
        }

        UpdatePreloadCompletionState();
        NotifyPreloadStateChanged();
    }

    /// <summary>
    /// 预加载小尺寸分数数字精灵（0~9）。
    /// 精灵路径格式：Arts/Combat/Eliminate/Score/1/{数字}。
    /// 使用 Score/1 套图（64×64），用于等待区每个槽位的分数字图片渲染。
    /// </summary>
    private void BeginPreloadScoreDigitSmallSprites()
    {
        _scoreDigitSmallSpritePreloadRequested = true;
        _scoreDigitSmallSpritePreloadCompleted = false;

        // ⚠️ 避坑：硬编码使用 Score/1 子文件夹（64×64 小尺寸），与 Score/2（256×256 UI 用）分开。
        const string subFolder = "1";

        for (int i = 0; i < 10; i++)
        {
            string assetPath = Utility.Text.Format("{0}{1}/{2}", AssetPath.CombatScoreDigitRoot, subFolder, i);

            if (_scoreDigitSmallSpritesByDigit.ContainsKey(i) || _loadingScoreDigitSmallSpritePaths.Contains(assetPath))
            {
                continue;
            }

            if (!TryLoadAsset(assetPath, typeof(Sprite), PreloadAssetKind.ScoreDigitSmallSprite, i.ToString()))
            {
                RegisterFailure(Utility.Text.Format("预加载小尺寸分数数字精灵失败，无法开始加载资源，Digit='{0}'，Path='{1}'。", i, assetPath));
            }
        }

        UpdatePreloadCompletionState();
        NotifyPreloadStateChanged();
    }

    /// <summary>
    /// 预加载每日一关本地预览关卡文本。
    /// 当前只迁入一份 bbl1；后续如果要扩充多关预览，只需要把路径继续加到常量数组里。
    /// </summary>
    private void BeginPreloadDailyChallengeLevelTexts()
    {
        // 从数据表读取默认每日关卡标识码，动态拼装预加载路径。
        // 数据表尚未就绪时重置请求标记并返回，由 LoadProcedure.OnDataTableLoadStateChanged
        // 在数据表加载完成后重新调用 BeginPreloadRequiredAssets 触发重试。
        string levelCode = string.Empty;
        if (GameEntry.DataTables == null || !GameEntry.DataTables.TryGetDefaultDailyChallengeLevelCode(out levelCode) || string.IsNullOrWhiteSpace(levelCode))
        {
            // 每日一关数据表可能尚未加载，属于正常时序，不再输出警告
            // Log.Warning("每日一关关卡文本预加载跳过：数据表未就绪或默认关卡标识码缺失。");
            _dailyChallengeLevelTextPreloadRequested = false;
            _dailyChallengeLevelTextPreloadCompleted = false;
            return;
        }

        _dailyChallengeLevelTextPreloadRequested = true;
        _dailyChallengeLevelTextPreloadCompleted = false;

        string assetPath = "Configs/Levels/" + levelCode;

        if (_dailyChallengeLevelTextsByPath.ContainsKey(assetPath) || _loadingDailyChallengeLevelTextPaths.Contains(assetPath))
        {
            UpdatePreloadCompletionState();
            NotifyPreloadStateChanged();
            return;
        }

        if (!TryLoadAsset(assetPath, typeof(TextAsset), PreloadAssetKind.DailyChallengeLevelText))
        {
            RegisterFailure(Utility.Text.Format("预加载每日一关关卡文本失败，无法开始加载资源，Path='{0}'。", assetPath));
        }

        UpdatePreloadCompletionState();
        NotifyPreloadStateChanged();
    }

    /// <summary>
    /// 预热宠物实体预制体资源。
    /// 同时预热场景实体预制体（Table/Orchard/Incubator），
    /// 确保它们的纹理在加载界面阶段完成解压，避免 OpenUIForm 期间并发解压导致 WASM crash。
    /// </summary>
    private void BeginPreloadPetEntityPrefab()
    {
        _petEntityPrefabPreloadRequested = true;
        _petEntityPrefabPreloadCompleted = false;

        // 场景实体预制体路径列表：与 PetEntity 同批预热，共用 PreloadAssetKind.PetEntityPrefab。
        // 这些实体在 OpenUIForm → OnOpen 之间被 GF 批量加载，纹理并发解压是 WASM crash 的根因。
        string[] sceneEntityPaths = { EntityDefine.TableEntity, EntityDefine.OrchardEntity, EntityDefine.IncubatorEntity };
        bool anyMissing = false;

        if (_petEntityPrefab == null && !_loadingPetEntityPrefabPaths.Contains(EntityDefine.PetEntity))
        {
            if (!TryLoadAsset(EntityDefine.PetEntity, typeof(GameObject), PreloadAssetKind.PetEntityPrefab))
            {
                RegisterFailure(Utility.Text.Format("预热宠物实体预制体失败，无法开始加载资源，Path='{0}'。", EntityDefine.PetEntity));
                anyMissing = true;
            }
        }

        for (int i = 0; i < sceneEntityPaths.Length; i++)
        {
            string path = sceneEntityPaths[i];
            if (!_loadingPetEntityPrefabPaths.Contains(path))
            {
                if (!TryLoadAsset(path, typeof(GameObject), PreloadAssetKind.PetEntityPrefab))
                {
                    RegisterFailure(Utility.Text.Format("预热场景实体预制体失败，无法开始加载资源，Path='{0}'。", path));
                    anyMissing = true;
                }
            }
        }

        if (anyMissing)
        {
            UpdatePreloadCompletionState();
            NotifyPreloadStateChanged();
        }
    }

    /// <summary>
    /// 预热 MainUIForm 预制体资源。
    /// 该预热只加载资源并持有引用，不实例化 UIForm；真正实例化仍由 GameFramework UI 模块负责。
    /// </summary>
    private void BeginPreloadMainUIFormPrefab()
    {
        _mainUIFormPrefabPreloadRequested = true;
        _mainUIFormPrefabPreloadCompleted = false;

        string prefabPath = MainUIFormPrefabPath;
        if (_mainUIFormPrefab != null || _loadingMainUIFormPrefabPaths.Contains(prefabPath))
        {
            UpdatePreloadCompletionState();
            NotifyPreloadStateChanged();
            return;
        }

        if (!TryLoadAsset(prefabPath, typeof(GameObject), PreloadAssetKind.MainUIFormPrefab))
        {
            RegisterFailure(Utility.Text.Format("预热 MainUIForm 预制体失败，无法开始加载资源，Path='{0}'。", prefabPath));
            UpdatePreloadCompletionState();
            NotifyPreloadStateChanged();
        }
    }

    /// <summary>
    /// 预热宠物期望食物气泡预制体资源。
    /// </summary>
    private void BeginPreloadPetFoodBubblePrefab()
    {
        _petFoodBubblePrefabPreloadRequested = true;
        _petFoodBubblePrefabPreloadCompleted = false;

        string prefabPath = PetFoodBubblePrefabPath;
        if (_petFoodBubblePrefab != null || _loadingPetFoodBubblePrefabPaths.Contains(prefabPath))
        {
            UpdatePreloadCompletionState();
            NotifyPreloadStateChanged();
            return;
        }

        if (!TryLoadAsset(prefabPath, typeof(GameObject), PreloadAssetKind.PetFoodBubblePrefab))
        {
            RegisterFailure(Utility.Text.Format("预热宠物食物气泡预制体失败，无法开始加载资源，Path='{0}'。", prefabPath));
            UpdatePreloadCompletionState();
            NotifyPreloadStateChanged();
        }
    }

    /// <summary>
    /// 预热金币 UI 预制体资源。
    /// </summary>
    private void BeginPreloadGoldCoinPrefab()
    {
        _goldCoinPrefabPreloadRequested = true;
        _goldCoinPrefabPreloadCompleted = false;

        string prefabPath = GoldCoinPrefabPath;
        if (_goldCoinPrefab != null || _loadingGoldCoinPrefabPaths.Contains(prefabPath))
        {
            UpdatePreloadCompletionState();
            NotifyPreloadStateChanged();
            return;
        }

        if (!TryLoadAsset(prefabPath, typeof(GameObject), PreloadAssetKind.GoldCoinPrefab))
        {
            RegisterFailure(Utility.Text.Format("预热金币 UI 预制体失败，无法开始加载资源，Path='{0}'。", prefabPath));
            UpdatePreloadCompletionState();
            NotifyPreloadStateChanged();
        }
    }

    /// <summary>
    /// 预热产出物 UI 预制体资源。
    /// </summary>
    private void BeginPreloadOutputProducePrefab()
    {
        _outputProducePrefabPreloadRequested = true;
        _outputProducePrefabPreloadCompleted = false;

        string prefabPath = OutputProducePrefabPath;
        if (_outputProducePrefab != null || _loadingOutputProducePrefabPaths.Contains(prefabPath))
        {
            UpdatePreloadCompletionState();
            NotifyPreloadStateChanged();
            return;
        }

        if (!TryLoadAsset(prefabPath, typeof(GameObject), PreloadAssetKind.OutputProducePrefab))
        {
            RegisterFailure(Utility.Text.Format("预热产出物 UI 预制体失败，无法开始加载资源，Path='{0}'。", prefabPath));
            UpdatePreloadCompletionState();
            NotifyPreloadStateChanged();
        }
    }

    /// <summary>
    /// 预热金币点击提示 Toast 预制体资源。
    /// </summary>
    private void BeginPreloadGoldCoinToastPrefab()
    {
        _goldCoinToastPrefabPreloadRequested = true;
        _goldCoinToastPrefabPreloadCompleted = false;

        string prefabPath = GoldCoinToastPrefabPath;
        if (_goldCoinToastPrefab != null || _loadingGoldCoinToastPrefabPaths.Contains(prefabPath))
        {
            UpdatePreloadCompletionState();
            NotifyPreloadStateChanged();
            return;
        }

        if (!TryLoadAsset(prefabPath, typeof(GameObject), PreloadAssetKind.GoldCoinToastPrefab))
        {
            RegisterFailure(Utility.Text.Format("预热金币点击提示 Toast 预制体失败，无法开始加载资源，Path='{0}'。", prefabPath));
            UpdatePreloadCompletionState();
            NotifyPreloadStateChanged();
        }
    }

    /// <summary>
    /// 为单条蛋表记录启动图标加载。
    /// 已缓存或已在加载中的路径会直接跳过。
    /// </summary>
    private void StartLoadEggSprite(EggDataRow row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.IconPath))
        {
            RegisterFailure("预加载蛋图标失败，蛋表存在空 IconPath。");
            return;
        }

        if (_eggSpritesByPath.ContainsKey(row.IconPath) || _loadingEggAssetPaths.Contains(row.IconPath))
        {
            return;
        }

        if (!TryLoadAsset(row.IconPath, typeof(Sprite), PreloadAssetKind.EggSprite))
        {
            RegisterFailure(Utility.Text.Format("预加载蛋图标失败，无法开始加载资源，Code='{0}'，Path='{1}'。", row.Code, row.IconPath));
        }
    }

    /// <summary>
    /// 为单条宠物表记录启动 SkeletonData 加载。
    /// 实体和 UI 共用 EntitySkeletonDataPath，不再加载 UiSkeletonDataPath。
    /// </summary>
    private void StartLoadPetSkeletonData(PetDataRow row)
    {
        if (row == null)
        {
            RegisterFailure("预加载宠物 SkeletonData 失败，宠物表存在空行。");
            return;
        }

        StartLoadPetSkeletonDataPath(row, row.EntitySkeletonDataPath);
    }

    /// <summary>
    /// 按指定路径启动一次宠物 SkeletonData 加载。
    /// 实体路径和 UI 路径共用此方法，避免复制两套几乎一样的逻辑。
    /// 如果两条路径相同，缓存集合和加载中集合会自动去重。
    /// </summary>
    /// <param name="row">当前宠物表行。</param>
    /// <param name="skeletonDataPath">本次要加载的 SkeletonData 路径。</param>
    private void StartLoadPetSkeletonDataPath(PetDataRow row, string skeletonDataPath)
    {
        if (string.IsNullOrWhiteSpace(skeletonDataPath))
        {
            RegisterFailure("预加载宠物 SkeletonData 失败，宠物表存在空 SkeletonDataPath。");
            return;
        }

        AddPetValidationInfo(row, skeletonDataPath);
        if (_petSkeletonDataAssetsByPath.TryGetValue(skeletonDataPath, out SkeletonDataAsset cachedSkeletonDataAsset) && cachedSkeletonDataAsset != null)
        {
            ValidatePetSkeletonData(skeletonDataPath, cachedSkeletonDataAsset);
            return;
        }

        if (_loadingPetAssetPaths.Contains(skeletonDataPath))
        {
            return;
        }

        if (!TryLoadAsset(skeletonDataPath, typeof(SkeletonDataAsset), PreloadAssetKind.PetSkeletonData))
        {
            RegisterFailure(Utility.Text.Format("预加载宠物 SkeletonData 失败，无法开始加载资源，Code='{0}'，Path='{1}'。", row.Code, skeletonDataPath));
        }
    }

    /// <summary>
    /// 按指定路径启动一次宠物材质加载。
    /// </summary>
    /// <param name="materialPath">材质资源路径。</param>
    private void StartLoadPetMaterial(string materialPath)
    {
        if (string.IsNullOrWhiteSpace(materialPath))
        {
            RegisterFailure("加载宠物材质失败，材质路径为空。");
            return;
        }

        if (_petMaterialsByPath.ContainsKey(materialPath))
        {
            return;
        }

        if (_loadingPetMaterialPaths.Contains(materialPath))
        {
            return;
        }

        if (!TryLoadAsset(materialPath, typeof(Material), PreloadAssetKind.PetMaterial))
        {
            RegisterFailure(Utility.Text.Format("加载宠物材质失败，无法开始加载资源，Path='{0}'。", materialPath));
        }
    }

    /// <summary>
    /// 为单条水果表记录启动图标加载。
    /// </summary>
    /// <param name="row">水果表行。</param>
    private void StartLoadFruitSprite(FruitDataRow row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.Code) || string.IsNullOrWhiteSpace(row.IconPath))
        {
            RegisterFailure("预加载水果图标失败，水果表存在空 Code 或空 IconPath。");
            return;
        }

        if (_fruitSpritesByCode.ContainsKey(row.Code) || _loadingFruitAssetPaths.Contains(row.IconPath))
        {
            return;
        }

        if (!TryLoadAsset(row.IconPath, typeof(Sprite), PreloadAssetKind.FruitSprite, row.Code))
        {
            RegisterFailure(Utility.Text.Format("预加载水果图标失败，无法开始加载资源，Code='{0}'，Path='{1}'。", row.Code, row.IconPath));
        }
    }

    /// <summary>
    /// 按需加载单条产出物图标（懒加载）。
    /// 设计语义：不再在启动期全量预加载所有产出物图标，
    /// 改为按需加载——只有真正掉落产出物时才发起 IO。
    /// 1. 路径为空 / 已缓存 / 已在加载中 → 直接返回；
    /// 2. 加载失败异步静默忽略，不阻塞主流程；
    /// 3. 首次加载未完成时 UI 自动回退到预制体默认图。
    /// </summary>
    /// <param name="produceCode">产出物 Code。</param>
    public void LoadProduceSprite(string produceCode)
    {
        if (string.IsNullOrWhiteSpace(produceCode))
        {
            return;
        }

        if (_produceSpritesByCode.ContainsKey(produceCode))
        {
            return;
        }

        if (GameEntry.DataTables == null || !GameEntry.DataTables.IsAvailable<PetProduceDataRow>())
        {
            return;
        }

        PetProduceDataRow row = GameEntry.DataTables.GetDataRowByCode<PetProduceDataRow>(produceCode);
        StartLoadProduceSprite(row);
    }

    /// <summary>
    /// 为单条产出物表记录启动图标加载。
    /// 路径为空、已缓存、已在加载中均直接返回，不写错误日志。
    /// </summary>
    /// <param name="row">产出物表行。</param>
    private void StartLoadProduceSprite(PetProduceDataRow row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.Code) || string.IsNullOrWhiteSpace(row.IconPath))
        {
            // 【关键】允许 IconPath 为空：策划尚未配图时不应触发任何错误，UI 自动回退默认图。
            return;
        }

        if (_produceSpritesByCode.ContainsKey(row.Code) || _loadingProduceSpritePaths.Contains(row.IconPath))
        {
            return;
        }

        if (!TryLoadAsset(row.IconPath, typeof(Sprite), PreloadAssetKind.ProduceSprite, row.Code))
        {
            // ⚠️ 注意：此处不调 RegisterFailure，与 OnLoadAssetFailure 中 ProduceSprite 分支保持一致 —
            // 产出物图标缺失被设计为可降级错误，不应污染 _hasPreloadFailure。
            Log.Warning("预加载产出物图标失败（已忽略），Code='{0}'，Path='{1}'。", row.Code, row.IconPath);
        }
    }

    /// <summary>
    /// 根据水果表批量预加载每日关卡卡图精灵。
    /// 使用 EffectiveDailyChallengePath 作为资源路径，与 IconPath 完全独立。
    /// 当 DailyChallengePath 为空时，EffectiveDailyChallengePath 回退到 IconPath，
    /// 此时会与 FruitSprite 加载同一路径，由 _loadingDailyChallengeCardSpritePaths 去重。
    /// </summary>
    /// <param name="rows">水果表行集合。</param>
    private void BeginPreloadDailyChallengeCardSprites(FruitDataRow[] rows)
    {
        _dailyChallengeCardSpritePreloadRequested = true;
        _dailyChallengeCardSpritePreloadCompleted = false;

        if (rows == null || rows.Length == 0)
        {
            _dailyChallengeCardSpritePreloadCompleted = true;
            return;
        }

        // 早期路径仅预加载“数据表中默认解锁”的水果卡图。
        // 触发时机：LoadProcedure 在 FruitDataRow 注册成功后即同步派发 LoadStateChanged，
        // 此时云存档必然未恢复，PlayerRuntimeModule 的依赖表也可能尚未全部注册完成。
        // 调用 GameEntry.Fruits.IsFruitUnlocked 在该时刻必为 false（运行时解锁集合还是空的），
        // 同时会触发 PlayerRuntimeModule.EnsureInitialized 在依赖未就绪时的 false 返回。
        // 因此这里只看 row.IsUnlocked，运行时解锁的水果卡图统一交由：
        // CloudSaveModule.ApplyPlayerCloudSaveSnapshot → SupplementDailyChallengeCardSpritesAfterCloudRestore 补齐。
        for (int i = 0; i < rows.Length; i++)
        {
            FruitDataRow row = rows[i];
            if (row == null || !row.IsUnlocked)
            {
                continue;
            }

            StartLoadDailyChallengeCardSprite(row);
        }

        UpdatePreloadCompletionState();
        NotifyPreloadStateChanged();
    }

    /// <summary>
    /// 云存档恢复完成后，补充预加载运行时解锁的水果卡图。
    /// 调用时机：CloudSaveModule.ApplySnapshotToRuntime 完成后。
    /// 只会为尚未缓存/尚未在加载中的水果发起新的加载请求。
    /// </summary>
    public void SupplementDailyChallengeCardSpritesAfterCloudRestore()
    {
        if (!_dailyChallengeCardSpritePreloadRequested)
        {
            // 首次预加载尚未触发，等 BeginPreloadDailyChallengeCardSprites 自然覆盖。
            return;
        }

        if (GameEntry.DataTables == null || !GameEntry.DataTables.IsAvailable<FruitDataRow>())
        {
            return;
        }

        FruitDataRow[] rows = GameEntry.DataTables.GetAllDataRows<FruitDataRow>();
        if (rows == null || rows.Length == 0)
        {
            return;
        }

        bool hasNewLoad = false;
        for (int i = 0; i < rows.Length; i++)
        {
            FruitDataRow row = rows[i];
            if (row == null)
            {
                continue;
            }

            // 只补充运行时解锁（非数据表默认解锁）且尚未缓存的水果。
            if (row.IsUnlocked)
            {
                continue;
            }

            if (GameEntry.Fruits == null || !GameEntry.Fruits.IsFruitUnlocked(row.Code))
            {
                continue;
            }

            StartLoadDailyChallengeCardSprite(row);
            hasNewLoad = true;
        }

        if (hasNewLoad)
        {
            UpdatePreloadCompletionState();
            NotifyPreloadStateChanged();
        }
    }

    /// <summary>
    /// 为单条水果表记录启动每日关卡卡图加载。
    /// 使用 EffectiveDailyChallengePath：若 DailyChallengePath 非空则用它，否则回退到 IconPath。
    /// </summary>
    /// <param name="row">水果表行。</param>
    private void StartLoadDailyChallengeCardSprite(FruitDataRow row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.Code))
        {
            return;
        }

        // 使用 EffectiveDailyChallengePath：优先 DailyChallengePath，为空时回退 IconPath。
        string dailyPath = row.EffectiveDailyChallengePath;
        if (string.IsNullOrWhiteSpace(dailyPath))
        {
            return;
        }

        string spriteName = ExtractAssetLeafName(dailyPath);
        if (string.IsNullOrWhiteSpace(spriteName))
        {
            return;
        }

        // 已缓存或正在加载则跳过。
        if (_eliminateCardSpritesByName.ContainsKey(spriteName) || _loadingDailyChallengeCardSpritePaths.Contains(dailyPath))
        {
            return;
        }

        if (!TryLoadAsset(dailyPath, typeof(Sprite), PreloadAssetKind.DailyChallengeCardSprite, row.Code))
        {
            RegisterFailure(Utility.Text.Format(
                "预加载每日关卡卡图失败，无法开始加载资源，Code='{0}'，Path='{1}'。",
                row.Code, dailyPath));
        }
    }

    /// <summary>
    /// 为单条建筑图片路径启动精灵加载。
    /// 已缓存或已在加载中的路径会直接跳过。
    /// </summary>
    /// <param name="assetPath">建筑精灵资源路径。</param>
    private void StartLoadArchitectureSprite(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return;
        }

        if (_architectureSpritesByPath.ContainsKey(assetPath) || _loadingArchitectureSpritePaths.Contains(assetPath))
        {
            return;
        }

        if (!TryLoadAsset(assetPath, typeof(Sprite), PreloadAssetKind.ArchitectureSprite))
        {
            RegisterFailure(Utility.Text.Format("预加载建筑精灵失败，无法开始加载资源，Path='{0}'。", assetPath));
        }
    }

    /// <summary>
    /// 通过 GF 资源管理器启动一次异步加载。
    /// 在编辑器资源模式下走 EditorResourceHelper，正式模式下走运行时 ResourceManager。
    /// </summary>
    private bool TryLoadAsset(string assetPath, Type assetType, PreloadAssetKind assetKind, string contextCode = null)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return false;
        }

        IResourceManager resourceManager = GameEntry.Base != null && GameEntry.Base.EditorResourceMode
            ? GameEntry.Base.EditorResourceHelper
            : GameFrameworkEntry.GetModule<IResourceManager>();
        if (resourceManager == null)
        {
            Log.Error("GameAssetModule can not load asset because IResourceManager is null.");
            return false;
        }

        PendingAssetLoadInfo loadInfo = new PendingAssetLoadInfo
        {
            AssetPath = assetPath,
            AssetKind = assetKind,
            ContextCode = contextCode,
        };

        switch (assetKind)
        {
            case PreloadAssetKind.EggSprite:
                _loadingEggAssetPaths.Add(assetPath);
                _pendingEggAssetCount++;
                break;

            case PreloadAssetKind.PetSkeletonData:
                _loadingPetAssetPaths.Add(assetPath);
                _pendingPetAssetCount++;
                break;

            case PreloadAssetKind.FruitSprite:
                _loadingFruitAssetPaths.Add(assetPath);
                _pendingFruitAssetCount++;
                break;

            case PreloadAssetKind.PetEntityPrefab:
                _loadingPetEntityPrefabPaths.Add(assetPath);
                _pendingPetEntityPrefabCount++;
                break;

            case PreloadAssetKind.MainUIFormPrefab:
                _loadingMainUIFormPrefabPaths.Add(assetPath);
                _pendingMainUIFormPrefabCount++;
                break;

            case PreloadAssetKind.PetFoodBubblePrefab:
                _loadingPetFoodBubblePrefabPaths.Add(assetPath);
                _pendingPetFoodBubblePrefabCount++;
                break;

            case PreloadAssetKind.GoldCoinPrefab:
                _loadingGoldCoinPrefabPaths.Add(assetPath);
                _pendingGoldCoinPrefabCount++;
                break;

            case PreloadAssetKind.OutputProducePrefab:
                _loadingOutputProducePrefabPaths.Add(assetPath);
                _pendingOutputProducePrefabCount++;
                break;

            case PreloadAssetKind.GoldCoinToastPrefab:
                _loadingGoldCoinToastPrefabPaths.Add(assetPath);
                _pendingGoldCoinToastPrefabCount++;
                break;

            case PreloadAssetKind.DailyChallengeLevelText:
                _loadingDailyChallengeLevelTextPaths.Add(assetPath);
                _pendingDailyChallengeLevelTextCount++;
                break;

            case PreloadAssetKind.ScoreDigitSprite:
                _loadingScoreDigitSpritePaths.Add(assetPath);
                _pendingScoreDigitSpriteCount++;
                break;

            case PreloadAssetKind.ScoreDigitSmallSprite:
                _loadingScoreDigitSmallSpritePaths.Add(assetPath);
                _pendingScoreDigitSmallSpriteCount++;
                break;

            case PreloadAssetKind.HeadPortraitSprite:
                _loadingHeadPortraitAssetPaths.Add(assetPath);
                _pendingHeadPortraitAssetCount++;
                break;

            case PreloadAssetKind.HeadPortraitFrameSprite:
                _loadingHeadPortraitFrameAssetPaths.Add(assetPath);
                _pendingHeadPortraitFrameAssetCount++;
                break;

            case PreloadAssetKind.ArchitectureSprite:
                _loadingArchitectureSpritePaths.Add(assetPath);
                _pendingArchitectureSpriteCount++;
                break;

            case PreloadAssetKind.DailyChallengeCardSprite:
                _loadingDailyChallengeCardSpritePaths.Add(assetPath);
                _pendingDailyChallengeCardSpriteCount++;
                break;

            case PreloadAssetKind.PetMaterial:
                _loadingPetMaterialPaths.Add(assetPath);
                _pendingPetMaterialCount++;
                break;

            case PreloadAssetKind.ProduceSprite:
                _loadingProduceSpritePaths.Add(assetPath);
                _pendingProduceSpriteCount++;
                break;
        }

                // [ASSET LOAD] 诊断日志已关闭，如需排查资源加载问题请取消注释
        // Log.Info("[ASSET LOAD] kind={0}, path={1}", loadInfo.AssetKind, assetPath);

        resourceManager.LoadAsset(assetPath, assetType, _loadAssetCallbacks, loadInfo);
        return true;
    }

    /// <summary>
    /// 资源加载成功回调。
    /// 根据任务类型写入对应缓存，并刷新预加载完成状态。
    /// </summary>
    private void OnLoadAssetSuccess(string assetName, object asset, float duration, object userData)
    {
        PendingAssetLoadInfo loadInfo = userData as PendingAssetLoadInfo;
        if (loadInfo == null)
        {
            RegisterFailure(Utility.Text.Format("GameAssetModule receive invalid load success callback, asset='{0}'.", assetName));
            return;
        }

        // [ASSET OK] 诊断日志已关闭，如需排查资源加载问题请取消注释
        // Log.Info("[ASSET OK] kind={0}, path={1}, type={2}", loadInfo.AssetKind, loadInfo.AssetPath, asset != null ? asset.GetType().Name : "null");

        switch (loadInfo.AssetKind)
        {
            case PreloadAssetKind.EggSprite:
                _loadingEggAssetPaths.Remove(loadInfo.AssetPath);
                _pendingEggAssetCount = Mathf.Max(0, _pendingEggAssetCount - 1);
                HandleEggSpriteLoaded(loadInfo.AssetPath, asset as Sprite);
                break;

            case PreloadAssetKind.PetSkeletonData:
                _loadingPetAssetPaths.Remove(loadInfo.AssetPath);
                _pendingPetAssetCount = Mathf.Max(0, _pendingPetAssetCount - 1);
                HandlePetSkeletonDataLoaded(loadInfo.AssetPath, asset as SkeletonDataAsset);
                break;

            case PreloadAssetKind.PetMaterial:
                _loadingPetMaterialPaths.Remove(loadInfo.AssetPath);
                _pendingPetMaterialCount = Mathf.Max(0, _pendingPetMaterialCount - 1);
                HandlePetMaterialLoaded(loadInfo.AssetPath, asset as Material);
                break;

            case PreloadAssetKind.FruitSprite:
                _loadingFruitAssetPaths.Remove(loadInfo.AssetPath);
                _pendingFruitAssetCount = Mathf.Max(0, _pendingFruitAssetCount - 1);
                HandleFruitSpriteLoaded(loadInfo.ContextCode, loadInfo.AssetPath, asset as Sprite);
                break;

            case PreloadAssetKind.PetEntityPrefab:
                _loadingPetEntityPrefabPaths.Remove(loadInfo.AssetPath);
                _pendingPetEntityPrefabCount = Mathf.Max(0, _pendingPetEntityPrefabCount - 1);
                HandlePetEntityPrefabLoaded(loadInfo.AssetPath, asset as GameObject);
                break;

            case PreloadAssetKind.MainUIFormPrefab:
                _loadingMainUIFormPrefabPaths.Remove(loadInfo.AssetPath);
                _pendingMainUIFormPrefabCount = Mathf.Max(0, _pendingMainUIFormPrefabCount - 1);
                HandleMainUIFormPrefabLoaded(loadInfo.AssetPath, asset as GameObject);
                break;

            case PreloadAssetKind.PetFoodBubblePrefab:
                _loadingPetFoodBubblePrefabPaths.Remove(loadInfo.AssetPath);
                _pendingPetFoodBubblePrefabCount = Mathf.Max(0, _pendingPetFoodBubblePrefabCount - 1);
                HandlePetFoodBubblePrefabLoaded(loadInfo.AssetPath, asset as GameObject);
                break;

            case PreloadAssetKind.GoldCoinPrefab:
                _loadingGoldCoinPrefabPaths.Remove(loadInfo.AssetPath);
                _pendingGoldCoinPrefabCount = Mathf.Max(0, _pendingGoldCoinPrefabCount - 1);
                HandleGoldCoinPrefabLoaded(loadInfo.AssetPath, asset as GameObject);
                break;

            case PreloadAssetKind.OutputProducePrefab:
                _loadingOutputProducePrefabPaths.Remove(loadInfo.AssetPath);
                _pendingOutputProducePrefabCount = Mathf.Max(0, _pendingOutputProducePrefabCount - 1);
                HandleOutputProducePrefabLoaded(loadInfo.AssetPath, asset as GameObject);
                break;

            case PreloadAssetKind.GoldCoinToastPrefab:
                _loadingGoldCoinToastPrefabPaths.Remove(loadInfo.AssetPath);
                _pendingGoldCoinToastPrefabCount = Mathf.Max(0, _pendingGoldCoinToastPrefabCount - 1);
                HandleGoldCoinToastPrefabLoaded(loadInfo.AssetPath, asset as GameObject);
                break;

            case PreloadAssetKind.DailyChallengeLevelText:
                _loadingDailyChallengeLevelTextPaths.Remove(loadInfo.AssetPath);
                _pendingDailyChallengeLevelTextCount = Mathf.Max(0, _pendingDailyChallengeLevelTextCount - 1);
                HandleDailyChallengeLevelTextLoaded(loadInfo.AssetPath, asset as TextAsset);
                break;

            case PreloadAssetKind.ScoreDigitSprite:
                _loadingScoreDigitSpritePaths.Remove(loadInfo.AssetPath);
                _pendingScoreDigitSpriteCount = Mathf.Max(0, _pendingScoreDigitSpriteCount - 1);
                HandleScoreDigitSpriteLoaded(loadInfo.ContextCode, asset as Sprite);
                break;

            case PreloadAssetKind.ArchitectureSprite:
                _loadingArchitectureSpritePaths.Remove(loadInfo.AssetPath);
                _pendingArchitectureSpriteCount = Mathf.Max(0, _pendingArchitectureSpriteCount - 1);
                HandleArchitectureSpriteLoaded(loadInfo.AssetPath, asset as Sprite);
                break;

            case PreloadAssetKind.DailyChallengeCardSprite:
                _loadingDailyChallengeCardSpritePaths.Remove(loadInfo.AssetPath);
                _pendingDailyChallengeCardSpriteCount = Mathf.Max(0, _pendingDailyChallengeCardSpriteCount - 1);
                HandleDailyChallengeCardSpriteLoaded(loadInfo.ContextCode, loadInfo.AssetPath, asset as Sprite);
                break;

            case PreloadAssetKind.ProduceSprite:
                _loadingProduceSpritePaths.Remove(loadInfo.AssetPath);
                _pendingProduceSpriteCount = Mathf.Max(0, _pendingProduceSpriteCount - 1);
                HandleProduceSpriteLoaded(loadInfo.ContextCode, loadInfo.AssetPath, asset as Sprite);
                break;

            case PreloadAssetKind.ScoreDigitSmallSprite:
                _loadingScoreDigitSmallSpritePaths.Remove(loadInfo.AssetPath);
                _pendingScoreDigitSmallSpriteCount = Mathf.Max(0, _pendingScoreDigitSmallSpriteCount - 1);
                HandleScoreDigitSmallSpriteLoaded(loadInfo.ContextCode, asset as Sprite);
                break;

            case PreloadAssetKind.HeadPortraitSprite:
                _loadingHeadPortraitAssetPaths.Remove(loadInfo.AssetPath);
                _pendingHeadPortraitAssetCount = Mathf.Max(0, _pendingHeadPortraitAssetCount - 1);
                HandleHeadPortraitSpriteLoaded(loadInfo.AssetPath, asset as Sprite);
                break;

            case PreloadAssetKind.HeadPortraitFrameSprite:
                _loadingHeadPortraitFrameAssetPaths.Remove(loadInfo.AssetPath);
                _pendingHeadPortraitFrameAssetCount = Mathf.Max(0, _pendingHeadPortraitFrameAssetCount - 1);
                HandleHeadPortraitFrameSpriteLoaded(loadInfo.AssetPath, asset as Sprite);
                break;
        }

        UpdatePreloadCompletionState();
        NotifyPreloadStateChanged();
    }

    /// <summary>
    /// 资源加载失败回调。
    /// 负责回收加载中的标记、减少待完成数量并记录错误。
    /// </summary>
    private void OnLoadAssetFailure(string assetName, LoadResourceStatus status, string errorMessage, object userData)
    {
        PendingAssetLoadInfo loadInfo = userData as PendingAssetLoadInfo;
        if (loadInfo == null)
        {
            RegisterFailure(Utility.Text.Format("GameAssetModule receive invalid load failure callback, asset='{0}', error='{1}'.", assetName, errorMessage));
            return;
        }

        if (loadInfo.AssetKind == PreloadAssetKind.EggSprite)
        {
            _loadingEggAssetPaths.Remove(loadInfo.AssetPath);
            _pendingEggAssetCount = Mathf.Max(0, _pendingEggAssetCount - 1);
            RegisterFailure(Utility.Text.Format("蛋图标加载失败，Path='{0}'，Status='{1}'，Error='{2}'。", loadInfo.AssetPath, status, errorMessage));
        }
        else if (loadInfo.AssetKind == PreloadAssetKind.PetSkeletonData)
        {
            _loadingPetAssetPaths.Remove(loadInfo.AssetPath);
            _pendingPetAssetCount = Mathf.Max(0, _pendingPetAssetCount - 1);
            RegisterFailure(Utility.Text.Format("宠物 SkeletonData 加载失败，Path='{0}'，Status='{1}'，Error='{2}'。", loadInfo.AssetPath, status, errorMessage));
            NotifyPetSkeletonDataStateChanged(loadInfo.AssetPath);
        }
        else if (loadInfo.AssetKind == PreloadAssetKind.PetMaterial)
        {
            _loadingPetMaterialPaths.Remove(loadInfo.AssetPath);
            _pendingPetMaterialCount = Mathf.Max(0, _pendingPetMaterialCount - 1);
            RegisterFailure(Utility.Text.Format("宠物材质加载失败，Path='{0}'，Status='{1}'，Error='{2}'。", loadInfo.AssetPath, status, errorMessage));
            NotifyPetMaterialStateChanged(loadInfo.AssetPath);
        }
        else if (loadInfo.AssetKind == PreloadAssetKind.FruitSprite)
        {
            _loadingFruitAssetPaths.Remove(loadInfo.AssetPath);
            _pendingFruitAssetCount = Mathf.Max(0, _pendingFruitAssetCount - 1);
            RegisterFailure(Utility.Text.Format(
                "水果图标加载失败，Code='{0}'，Path='{1}'，Status='{2}'，Error='{3}'。",
                loadInfo.ContextCode,
                loadInfo.AssetPath,
                status,
                errorMessage));
        }
        else if (loadInfo.AssetKind == PreloadAssetKind.PetEntityPrefab)
        {
            _loadingPetEntityPrefabPaths.Remove(loadInfo.AssetPath);
            _pendingPetEntityPrefabCount = Mathf.Max(0, _pendingPetEntityPrefabCount - 1);
            RegisterFailure(Utility.Text.Format("宠物实体预制体预热失败，Path='{0}'，Status='{1}'，Error='{2}'。", loadInfo.AssetPath, status, errorMessage));
        }
        else if (loadInfo.AssetKind == PreloadAssetKind.MainUIFormPrefab)
        {
            _loadingMainUIFormPrefabPaths.Remove(loadInfo.AssetPath);
            _pendingMainUIFormPrefabCount = Mathf.Max(0, _pendingMainUIFormPrefabCount - 1);
            RegisterFailure(Utility.Text.Format("MainUIForm 预制体预热失败，Path='{0}'，Status='{1}'，Error='{2}'。", loadInfo.AssetPath, status, errorMessage));
        }
        else if (loadInfo.AssetKind == PreloadAssetKind.PetFoodBubblePrefab)
        {
            _loadingPetFoodBubblePrefabPaths.Remove(loadInfo.AssetPath);
            _pendingPetFoodBubblePrefabCount = Mathf.Max(0, _pendingPetFoodBubblePrefabCount - 1);
            RegisterFailure(Utility.Text.Format("宠物食物气泡预制体预热失败，Path='{0}'，Status='{1}'，Error='{2}'。", loadInfo.AssetPath, status, errorMessage));
        }
        else if (loadInfo.AssetKind == PreloadAssetKind.GoldCoinPrefab)
        {
            _loadingGoldCoinPrefabPaths.Remove(loadInfo.AssetPath);
            _pendingGoldCoinPrefabCount = Mathf.Max(0, _pendingGoldCoinPrefabCount - 1);
            RegisterFailure(Utility.Text.Format("金币 UI 预制体预热失败，Path='{0}'，Status='{1}'，Error='{2}'。", loadInfo.AssetPath, status, errorMessage));
        }
        else if (loadInfo.AssetKind == PreloadAssetKind.OutputProducePrefab)
        {
            _loadingOutputProducePrefabPaths.Remove(loadInfo.AssetPath);
            _pendingOutputProducePrefabCount = Mathf.Max(0, _pendingOutputProducePrefabCount - 1);
            RegisterFailure(Utility.Text.Format("产出物 UI 预制体预热失败，Path='{0}'，Status='{1}'，Error='{2}'。", loadInfo.AssetPath, status, errorMessage));
        }
        else if (loadInfo.AssetKind == PreloadAssetKind.GoldCoinToastPrefab)
        {
            _loadingGoldCoinToastPrefabPaths.Remove(loadInfo.AssetPath);
            _pendingGoldCoinToastPrefabCount = Mathf.Max(0, _pendingGoldCoinToastPrefabCount - 1);
            RegisterFailure(Utility.Text.Format("金币点击提示 Toast 预制体预热失败，Path='{0}'，Status='{1}'，Error='{2}'。", loadInfo.AssetPath, status, errorMessage));
        }
        else if (loadInfo.AssetKind == PreloadAssetKind.DailyChallengeLevelText)
        {
            _loadingDailyChallengeLevelTextPaths.Remove(loadInfo.AssetPath);
            _pendingDailyChallengeLevelTextCount = Mathf.Max(0, _pendingDailyChallengeLevelTextCount - 1);
            RegisterFailure(Utility.Text.Format("每日一关关卡文本预加载失败，Path='{0}'，Status='{1}'，Error='{2}'。", loadInfo.AssetPath, status, errorMessage));
        }
        else if (loadInfo.AssetKind == PreloadAssetKind.ScoreDigitSprite)
        {
            _loadingScoreDigitSpritePaths.Remove(loadInfo.AssetPath);
            _pendingScoreDigitSpriteCount = Mathf.Max(0, _pendingScoreDigitSpriteCount - 1);
            RegisterFailure(Utility.Text.Format("分数数字精灵预加载失败，Digit='{0}'，Path='{1}'，Status='{2}'，Error='{3}'。", loadInfo.ContextCode, loadInfo.AssetPath, status, errorMessage));
        }
        else if (loadInfo.AssetKind == PreloadAssetKind.ArchitectureSprite)
        {
            _loadingArchitectureSpritePaths.Remove(loadInfo.AssetPath);
            _pendingArchitectureSpriteCount = Mathf.Max(0, _pendingArchitectureSpriteCount - 1);
            RegisterFailure(Utility.Text.Format("建筑精灵预加载失败，Path='{0}'，Status='{1}'，Error='{2}'。", loadInfo.AssetPath, status, errorMessage));
        }
        else if (loadInfo.AssetKind == PreloadAssetKind.ScoreDigitSmallSprite)
        {
            _loadingScoreDigitSmallSpritePaths.Remove(loadInfo.AssetPath);
            _pendingScoreDigitSmallSpriteCount = Mathf.Max(0, _pendingScoreDigitSmallSpriteCount - 1);
            RegisterFailure(Utility.Text.Format("小尺寸分数数字精灵预加载失败，Digit='{0}'，Path='{1}'，Status='{2}'，Error='{3}'。", loadInfo.ContextCode, loadInfo.AssetPath, status, errorMessage));
        }
        else if (loadInfo.AssetKind == PreloadAssetKind.HeadPortraitSprite)
        {
            _loadingHeadPortraitAssetPaths.Remove(loadInfo.AssetPath);
            _pendingHeadPortraitAssetCount = Mathf.Max(0, _pendingHeadPortraitAssetCount - 1);
            RegisterFailure(Utility.Text.Format("头像图标预加载失败，Path='{0}'，Status='{1}'，Error='{2}'。", loadInfo.AssetPath, status, errorMessage));
        }
        else if (loadInfo.AssetKind == PreloadAssetKind.HeadPortraitFrameSprite)
        {
            _loadingHeadPortraitFrameAssetPaths.Remove(loadInfo.AssetPath);
            _pendingHeadPortraitFrameAssetCount = Mathf.Max(0, _pendingHeadPortraitFrameAssetCount - 1);
            RegisterFailure(Utility.Text.Format("头像框图标预加载失败，Path='{0}'，Status='{1}'，Error='{2}'。", loadInfo.AssetPath, status, errorMessage));
        }
        else if (loadInfo.AssetKind == PreloadAssetKind.DailyChallengeCardSprite)
        {
            _loadingDailyChallengeCardSpritePaths.Remove(loadInfo.AssetPath);
            _pendingDailyChallengeCardSpriteCount = Mathf.Max(0, _pendingDailyChallengeCardSpriteCount - 1);
            RegisterFailure(Utility.Text.Format(
                "每日关卡卡图预加载失败，Code='{0}'，Path='{1}'，Status='{2}'，Error='{3}'。",
                loadInfo.ContextCode, loadInfo.AssetPath, status, errorMessage));
        }
        else if (loadInfo.AssetKind == PreloadAssetKind.ProduceSprite)
        {
            // 【设计语义】产出物图标资源缺失/加载失败属于“可降级”错误：
            // 1. 不调用 RegisterFailure，避免 _hasPreloadFailure 被噪声污染；
            // 2. 仅做 pending 计数回收 + 一条 Warning 便于排查；
            // 3. 不写入 _produceSpritesByCode，UI 端 TryGetProduceSprite 命中失败会自动回退到预制体默认图标。
            _loadingProduceSpritePaths.Remove(loadInfo.AssetPath);
            _pendingProduceSpriteCount = Mathf.Max(0, _pendingProduceSpriteCount - 1);
            Log.Warning(
                "产出物图标加载失败（已忽略，不阻塞主流程），Code='{0}'，Path='{1}'，Status='{2}'，Error='{3}'。",
                loadInfo.ContextCode, loadInfo.AssetPath, status, errorMessage);
        }

        UpdatePreloadCompletionState();
        NotifyPreloadStateChanged();
    }

    /// <summary>
    /// 根据头像表批量预加载头像图标资源。
    /// </summary>
    /// <param name="rows">头像表行集合。</param>
    private void BeginPreloadHeadPortraitSprites(HeadPortraitDataRow[] rows)
    {
        _headPortraitPreloadRequested = true;
        _headPortraitPreloadCompleted = false;

        if (rows == null || rows.Length == 0)
        {
            _headPortraitPreloadCompleted = true;
            return;
        }

        for (int i = 0; i < rows.Length; i++)
        {
            HeadPortraitDataRow row = rows[i];
            if (row == null || string.IsNullOrWhiteSpace(row.IconPath))
            {
                continue;
            }

            StartLoadHeadPortraitSprite(row);
        }

        UpdatePreloadCompletionState();
    }

    /// <summary>
    /// 为单条头像表记录启动图标加载。
    /// 已缓存或已在加载中的路径会直接跳过。
    /// </summary>
    /// <param name="row">头像表行。</param>
    private void StartLoadHeadPortraitSprite(HeadPortraitDataRow row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.IconPath))
        {
            RegisterFailure("预加载头像图标失败，头像表存在空 IconPath。");
            return;
        }

        if (_headPortraitSpritesByPath.ContainsKey(row.IconPath) || _loadingHeadPortraitAssetPaths.Contains(row.IconPath))
        {
            return;
        }

        if (!TryLoadAsset(row.IconPath, typeof(Sprite), PreloadAssetKind.HeadPortraitSprite))
        {
            RegisterFailure(Utility.Text.Format("预加载头像图标失败，无法开始加载资源，Code='{0}'，Path='{1}'。", row.Code, row.IconPath));
        }
    }

    /// <summary>
    /// 处理头像图标加载完成。
    /// </summary>
    /// <param name="iconPath">头像图标资源路径。</param>
    /// <param name="sprite">命中的图标资源。</param>
    private void HandleHeadPortraitSpriteLoaded(string iconPath, Sprite sprite)
    {
        if (sprite == null)
        {
            RegisterFailure(Utility.Text.Format("头像图标加载失败，资源类型不是 Sprite，Path='{0}'。", iconPath));
            return;
        }

        _headPortraitSpritesByPath[iconPath] = sprite;
    }

    /// <summary>
    /// 启动头像框图标预加载。
    /// </summary>
    /// <param name="rows">头像框表行集合。</param>
    private void BeginPreloadHeadPortraitFrameSprites(HeadPortraitFrameDataRow[] rows)
    {
        _headPortraitFramePreloadRequested = true;
        _headPortraitFramePreloadCompleted = false;

        if (rows == null || rows.Length == 0)
        {
            _headPortraitFramePreloadCompleted = true;
            return;
        }

        for (int i = 0; i < rows.Length; i++)
        {
            StartLoadHeadPortraitFrameSprite(rows[i]);
        }

        if (_pendingHeadPortraitFrameAssetCount <= 0)
        {
            _headPortraitFramePreloadCompleted = true;
        }
    }

    /// <summary>
    /// 发起单张头像框图标的异步加载。
    /// </summary>
    /// <param name="row">头像框数据行。</param>
    private void StartLoadHeadPortraitFrameSprite(HeadPortraitFrameDataRow row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.IconPath))
        {
            return;
        }

        if (_headPortraitFrameSpritesByPath.ContainsKey(row.IconPath) || _loadingHeadPortraitFrameAssetPaths.Contains(row.IconPath))
        {
            return;
        }

        if (!TryLoadAsset(row.IconPath, typeof(Sprite), PreloadAssetKind.HeadPortraitFrameSprite, row.IconPath))
        {
            RegisterFailure(Utility.Text.Format("预加载头像框图标失败，无法开始加载资源，Path='{0}'。", row.IconPath));
        }
    }

    /// <summary>
    /// 头像框图标加载成功回调。
    /// </summary>
    /// <param name="iconPath">图标资源路径。</param>
    /// <param name="sprite">加载到的精灵资源。</param>
    private void HandleHeadPortraitFrameSpriteLoaded(string iconPath, Sprite sprite)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return;
        }

        _headPortraitFrameSpritesByPath[iconPath] = sprite;
    }

    /// <summary>
    /// 处理蛋图标加载完成。
    /// </summary>
    private void HandleEggSpriteLoaded(string iconPath, Sprite sprite)
    {
        if (sprite == null)
        {
            RegisterFailure(Utility.Text.Format("蛋图标加载失败，资源类型不是 Sprite，Path='{0}'。", iconPath));
            return;
        }

        _eggSpritesByPath[iconPath] = sprite;
    }

    /// <summary>
    /// 处理宠物 SkeletonData 加载完成，并立即执行动画校验。
    /// </summary>
    private void HandlePetSkeletonDataLoaded(string skeletonDataPath, SkeletonDataAsset skeletonDataAsset)
    {
        if (skeletonDataAsset == null)
        {
            RegisterFailure(Utility.Text.Format("宠物 SkeletonData 加载失败，资源类型不是 SkeletonDataAsset，Path='{0}'。", skeletonDataPath));
            NotifyPetSkeletonDataStateChanged(skeletonDataPath);
            return;
        }

        _petSkeletonDataAssetsByPath[skeletonDataPath] = skeletonDataAsset;

        // 【关键】严禁修改 atlas 内嵌材质（如替换 shader 为 Spine/SkeletonGraphic）。
        // 实体（SkeletonAnimation）和 UI（SkeletonGraphic）共用同一 SkeletonDataAsset 及其 atlas，
        // 原地改 shader 会让实体侧也用 UI shader 渲染，导致实体材质显示错误。
        // UI 侧需要 _Stencil 时，由 Pet.txt 的 UiMaterialPath 列配置独立 UI 材质；
        // 实体侧需要自定义材质时，使用 EntityMaterialPath + CustomMaterialOverride（不修改原始材质）。
        ValidatePetSkeletonData(skeletonDataPath, skeletonDataAsset);
        NotifyPetSkeletonDataStateChanged(skeletonDataPath);
    }

    /// <summary>
    /// 通知指定宠物 SkeletonData 路径的加载状态发生变化。
    /// 这里单独拆出来，避免实体层监听全局 PreloadStateChanged 后被水果、头像、建筑等无关资源完成事件误唤醒。
    /// </summary>
    /// <param name="skeletonDataPath">发生变化的 SkeletonData 资源路径。</param>
    private void NotifyPetSkeletonDataStateChanged(string skeletonDataPath)
    {
        if (string.IsNullOrWhiteSpace(skeletonDataPath))
        {
            return;
        }

        PetSkeletonDataStateChanged?.Invoke(skeletonDataPath);
    }

    /// <summary>
    /// 处理宠物材质加载完成。
    /// </summary>
    /// <param name="materialPath">材质资源路径。</param>
    /// <param name="material">加载到的材质资源。</param>
    private void HandlePetMaterialLoaded(string materialPath, Material material)
    {
        if (material == null)
        {
            RegisterFailure(Utility.Text.Format("宠物材质加载失败，资源类型不是 Material，Path='{0}'。", materialPath));
            NotifyPetMaterialStateChanged(materialPath);
            return;
        }

        _petMaterialsByPath[materialPath] = material;
        NotifyPetMaterialStateChanged(materialPath);
    }

    /// <summary>
    /// 通知指定宠物材质路径的加载状态发生变化。
    /// </summary>
    /// <param name="materialPath">发生变化的材质资源路径。</param>
    private void NotifyPetMaterialStateChanged(string materialPath)
    {
        if (string.IsNullOrWhiteSpace(materialPath))
        {
            return;
        }

        PetMaterialStateChanged?.Invoke(materialPath);
    }

    /// <summary>
    /// 处理水果图标加载完成。
    /// </summary>
    /// <param name="fruitCode">水果 Code。</param>
    /// <param name="iconPath">水果图标资源路径。</param>
    /// <param name="sprite">命中的图标资源。</param>
    private void HandleFruitSpriteLoaded(string fruitCode, string iconPath, Sprite sprite)
    {
        if (string.IsNullOrWhiteSpace(fruitCode))
        {
            RegisterFailure(Utility.Text.Format("水果图标加载失败，缺少水果 Code，Path='{0}'。", iconPath));
            return;
        }

        if (sprite == null)
        {
            RegisterFailure(Utility.Text.Format("水果图标加载失败，资源类型不是 Sprite，Code='{0}'，Path='{1}'。", fruitCode, iconPath));
            return;
        }

        _fruitSpritesByCode[fruitCode] = sprite;
    }

    /// <summary>
    /// 处理产出物图标加载完成。
    /// 资源缺失或类型不匹配时静默忽略，不写入缓存，UI 端将自动回退到预制体默认图。
    /// </summary>
    /// <param name="produceCode">产出物 Code。</param>
    /// <param name="iconPath">产出物图标资源路径。</param>
    /// <param name="sprite">命中的图标资源。</param>
    private void HandleProduceSpriteLoaded(string produceCode, string iconPath, Sprite sprite)
    {
        if (string.IsNullOrWhiteSpace(produceCode) || sprite == null)
        {
            // 仅 Warning 不 RegisterFailure：产出物图标按设计可缺失，UI 自动回退默认图。
            Log.Warning("产出物图标加载完成但被忽略（已回退默认图），Code='{0}'，Path='{1}'。", produceCode, iconPath);
            return;
        }

        _produceSpritesByCode[produceCode] = sprite;
        ProduceSpriteLoaded?.Invoke(produceCode);
    }

    /// <summary>
    /// 处理每日关卡卡图精灵加载完成。
    /// 将精灵按 EffectiveDailyChallengePath 末尾精灵名写入消除卡图缓存。
    /// </summary>
    /// <param name="fruitCode">水果 Code，用于日志定位。</param>
    /// <param name="dailyPath">每日关卡图资源路径（EffectiveDailyChallengePath）。</param>
    /// <param name="sprite">命中的精灵资源。</param>
    private void HandleDailyChallengeCardSpriteLoaded(string fruitCode, string dailyPath, Sprite sprite)
    {
        if (string.IsNullOrWhiteSpace(dailyPath))
        {
            RegisterFailure(Utility.Text.Format("每日关卡卡图加载失败，资源路径为空，Code='{0}'。", fruitCode));
            return;
        }

        if (sprite == null)
        {
            RegisterFailure(Utility.Text.Format("每日关卡卡图加载失败，资源类型不是 Sprite，Code='{0}'，Path='{1}'。", fruitCode, dailyPath));
            return;
        }

        // 把 EffectiveDailyChallengePath 末尾精灵名反向登记成"卡图名 -> Sprite"缓存，
        // 让 DailyChallenge 业务层通过 TryGetEliminateCardSprite 同步读取。
        string spriteName = ExtractAssetLeafName(dailyPath);
        if (!string.IsNullOrWhiteSpace(spriteName))
        {
            _eliminateCardSpritesByName[spriteName] = sprite;
        }
    }

    /// <summary>
    /// 处理建筑精灵加载完成。
    /// </summary>
    /// <param name="assetPath">建筑精灵资源路径。</param>
    /// <param name="sprite">命中的精灵资源。</param>
    private void HandleArchitectureSpriteLoaded(string assetPath, Sprite sprite)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            RegisterFailure("建筑精灵加载完成回调失败，资源路径为空。");
            return;
        }

        if (sprite == null)
        {
            RegisterFailure(Utility.Text.Format("建筑精灵加载失败，资源类型不是 Sprite，Path='{0}'。", assetPath));
            return;
        }

        _architectureSpritesByPath[assetPath] = sprite;
    }

    /// <summary>
    /// 处理分数数字精灵加载完成。
    /// </summary>
    /// <param name="digitStr">数字字符串（0~9），由 ContextCode 传入。</param>
    /// <param name="sprite">命中的精灵资源。</param>
    private void HandleScoreDigitSpriteLoaded(string digitStr, Sprite sprite)
    {
        if (string.IsNullOrWhiteSpace(digitStr) || !int.TryParse(digitStr, out int digit) || digit < 0 || digit > 9)
        {
            RegisterFailure(Utility.Text.Format("分数数字精灵加载完成回调失败，ContextCode 不是有效数字，Code='{0}'。", digitStr));
            return;
        }

        if (sprite == null)
        {
            RegisterFailure(Utility.Text.Format("分数数字精灵加载失败，资源类型不是 Sprite，Digit='{0}'。", digit));
            return;
        }

        _scoreDigitSpritesByDigit[digit] = sprite;
    }

    /// <summary>
    /// 处理小尺寸分数数字精灵加载完成（Score/1 套图 64×64）。
    /// </summary>
    /// <param name="digitStr">数字字符串（0~9），由 ContextCode 传入。</param>
    /// <param name="sprite">命中的精灵资源。</param>
    private void HandleScoreDigitSmallSpriteLoaded(string digitStr, Sprite sprite)
    {
        if (string.IsNullOrWhiteSpace(digitStr) || !int.TryParse(digitStr, out int digit) || digit < 0 || digit > 9)
        {
            RegisterFailure(Utility.Text.Format("小尺寸分数数字精灵加载完成回调失败，ContextCode 不是有效数字，Code='{0}'。", digitStr));
            return;
        }

        if (sprite == null)
        {
            RegisterFailure(Utility.Text.Format("小尺寸分数数字精灵加载失败，资源类型不是 Sprite，Digit='{0}'。", digit));
            return;
        }

        _scoreDigitSmallSpritesByDigit[digit] = sprite;
    }

    /// <summary>
    /// 处理每日一关本地预览关卡文本加载完成。
    /// </summary>
    /// <param name="assetPath">关卡文本资源路径。</param>
    /// <param name="levelText">命中的文本资源。</param>
    private void HandleDailyChallengeLevelTextLoaded(string assetPath, TextAsset levelText)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            RegisterFailure("每日一关关卡文本加载完成回调失败，资源路径为空。");
            return;
        }

        if (levelText == null)
        {
            RegisterFailure(Utility.Text.Format("每日一关关卡文本加载失败，资源类型不是 TextAsset，Path='{0}'。", assetPath));
            return;
        }

        _dailyChallengeLevelTextsByPath[assetPath] = levelText;
    }

    /// <summary>
    /// 处理宠物实体预制体预热完成。
    /// </summary>
    private void HandlePetEntityPrefabLoaded(string assetPath, GameObject petEntityPrefab)
    {
        if (petEntityPrefab == null)
        {
            RegisterFailure(Utility.Text.Format("宠物实体预制体预热失败，资源类型不是 GameObject，Path='{0}'。", assetPath));
            return;
        }

        _petEntityPrefab = petEntityPrefab;
    }

    /// <summary>
    /// 处理 MainUIForm 预制体预热完成。
    /// </summary>
    /// <param name="assetPath">预制体资源路径。</param>
    /// <param name="mainUIFormPrefab">命中的 MainUIForm 预制体资源。</param>
    private void HandleMainUIFormPrefabLoaded(string assetPath, GameObject mainUIFormPrefab)
    {
        if (mainUIFormPrefab == null)
        {
            RegisterFailure(Utility.Text.Format("MainUIForm 预制体预热失败，资源类型不是 GameObject，Path='{0}'。", assetPath));
            return;
        }

        _mainUIFormPrefab = mainUIFormPrefab;
    }

    /// <summary>
    /// 处理宠物食物气泡预制体预热完成。
    /// </summary>
    /// <param name="assetPath">预制体路径。</param>
    /// <param name="petFoodBubblePrefab">命中的预制体资源。</param>
    private void HandlePetFoodBubblePrefabLoaded(string assetPath, GameObject petFoodBubblePrefab)
    {
        if (petFoodBubblePrefab == null)
        {
            RegisterFailure(Utility.Text.Format("宠物食物气泡预制体预热失败，资源类型不是 GameObject，Path='{0}'。", assetPath));
            return;
        }

        _petFoodBubblePrefab = petFoodBubblePrefab;
    }

    /// <summary>
    /// 处理金币 UI 预制体预热完成。
    /// </summary>
    /// <param name="assetPath">预制体路径。</param>
    /// <param name="goldCoinPrefab">命中的预制体资源。</param>
    private void HandleGoldCoinPrefabLoaded(string assetPath, GameObject goldCoinPrefab)
    {
        if (goldCoinPrefab == null)
        {
            RegisterFailure(Utility.Text.Format("金币 UI 预制体预热失败，资源类型不是 GameObject，Path='{0}'。", assetPath));
            return;
        }

        _goldCoinPrefab = goldCoinPrefab;
    }

    /// <summary>
    /// 处理产出物 UI 预制体预热完成。
    /// </summary>
    /// <param name="assetPath">预制体路径。</param>
    /// <param name="outputProducePrefab">命中的预制体资源。</param>
    private void HandleOutputProducePrefabLoaded(string assetPath, GameObject outputProducePrefab)
    {
        if (outputProducePrefab == null)
        {
            RegisterFailure(Utility.Text.Format("产出物 UI 预制体预热失败，资源类型不是 GameObject，Path='{0}'。", assetPath));
            return;
        }

        _outputProducePrefab = outputProducePrefab;
    }

    /// <summary>
    /// 处理金币点击提示 Toast 预制体预热完成。
    /// </summary>
    /// <param name="assetPath">预制体路径。</param>
    /// <param name="goldCoinToastPrefab">命中的预制体资源。</param>
    private void HandleGoldCoinToastPrefabLoaded(string assetPath, GameObject goldCoinToastPrefab)
    {
        if (goldCoinToastPrefab == null)
        {
            RegisterFailure(Utility.Text.Format("金币点击提示 Toast 预制体预热失败，资源类型不是 GameObject，Path='{0}'。", assetPath));
            return;
        }

        _goldCoinToastPrefab = goldCoinToastPrefab;
    }

    /// <summary>
    /// 为指定路径登记动画校验信息。
    /// 如果实体路径和 UI 路径恰好相同，这里会主动去重，避免同一只宠物重复登记。
    /// </summary>
    /// <param name="row">当前宠物表行。</param>
    /// <param name="skeletonDataPath">要挂载校验信息的 SkeletonData 路径。</param>
    private void AddPetValidationInfo(PetDataRow row, string skeletonDataPath)
    {
        if (row == null || string.IsNullOrWhiteSpace(skeletonDataPath))
        {
            return;
        }

        if (!_petValidationInfosByPath.TryGetValue(skeletonDataPath, out List<PetSkeletonValidationInfo> validationInfos))
        {
            validationInfos = new List<PetSkeletonValidationInfo>();
            _petValidationInfosByPath.Add(skeletonDataPath, validationInfos);
        }

        for (int i = 0; i < validationInfos.Count; i++)
        {
            PetSkeletonValidationInfo existingInfo = validationInfos[i];
            if (existingInfo != null
                && string.Equals(existingInfo.PetCode, row.Code, StringComparison.Ordinal)
                && string.Equals(existingInfo.IdleAnimationName, row.IdleAnimationName, StringComparison.Ordinal)
                && string.Equals(existingInfo.MoveAnimationName, row.MoveAnimationName, StringComparison.Ordinal))
            {
                return;
            }
        }

        validationInfos.Add(new PetSkeletonValidationInfo
        {
            PetCode = row.Code,
            IdleAnimationName = row.IdleAnimationName,
            MoveAnimationName = row.MoveAnimationName,
        });
    }

    /// <summary>
    /// 校验指定 SkeletonData 是否包含宠物表声明的待机/移动动画。
    /// </summary>
    private void ValidatePetSkeletonData(string skeletonDataPath, SkeletonDataAsset skeletonDataAsset)
    {
        if (skeletonDataAsset == null)
        {
            RegisterFailure(Utility.Text.Format("校验宠物 SkeletonData 失败，资源为空，Path='{0}'。", skeletonDataPath));
            return;
        }

        SkeletonData skeletonData = skeletonDataAsset.GetSkeletonData(true);
        if (skeletonData == null)
        {
            RegisterFailure(Utility.Text.Format("宠物 SkeletonData 无法读取，Path='{0}'。", skeletonDataPath));
            return;
        }

        if (!_petValidationInfosByPath.TryGetValue(skeletonDataPath, out List<PetSkeletonValidationInfo> validationInfos))
        {
            return;
        }

        for (int i = 0; i < validationInfos.Count; i++)
        {
            PetSkeletonValidationInfo validationInfo = validationInfos[i];
            if (validationInfo == null)
            {
                continue;
            }

            if (skeletonData.FindAnimation(validationInfo.IdleAnimationName) == null)
            {
                RegisterFailure(Utility.Text.Format(
                    "宠物表配置错误，待机动画不存在，Code='{0}'，Animation='{1}'，Path='{2}'。",
                    validationInfo.PetCode,
                    validationInfo.IdleAnimationName,
                    skeletonDataPath));
            }

            if (skeletonData.FindAnimation(validationInfo.MoveAnimationName) == null)
            {
                RegisterFailure(Utility.Text.Format(
                    "宠物表配置错误，移动动画不存在，Code='{0}'，Animation='{1}'，Path='{2}'。",
                    validationInfo.PetCode,
                    validationInfo.MoveAnimationName,
                    skeletonDataPath));
            }
        }
    }

    /// <summary>
    /// 根据当前待完成数量刷新各类资源的完成标记。
    /// </summary>
    private void UpdatePreloadCompletionState()
    {
        if (_eggPreloadRequested && _pendingEggAssetCount <= 0)
        {
            _eggPreloadCompleted = true;
        }

        if (_petPreloadRequested && _pendingPetAssetCount <= 0)
        {
            _petPreloadCompleted = true;
        }

        if (_petMaterialPreloadRequested && _pendingPetMaterialCount <= 0)
        {
            _petMaterialPreloadCompleted = true;
        }

        if (_fruitPreloadRequested && _pendingFruitAssetCount <= 0)
        {
            _fruitPreloadCompleted = true;
        }

        if (_petEntityPrefabPreloadRequested && _pendingPetEntityPrefabCount <= 0)
        {
            _petEntityPrefabPreloadCompleted = true;
        }

        if (_mainUIFormPrefabPreloadRequested && _pendingMainUIFormPrefabCount <= 0)
        {
            _mainUIFormPrefabPreloadCompleted = true;
        }

        if (_petFoodBubblePrefabPreloadRequested && _pendingPetFoodBubblePrefabCount <= 0)
        {
            _petFoodBubblePrefabPreloadCompleted = true;
        }

        if (_goldCoinPrefabPreloadRequested && _pendingGoldCoinPrefabCount <= 0)
        {
            _goldCoinPrefabPreloadCompleted = true;
        }

        if (_outputProducePrefabPreloadRequested && _pendingOutputProducePrefabCount <= 0)
        {
            _outputProducePrefabPreloadCompleted = true;
        }

        if (_goldCoinToastPrefabPreloadRequested && _pendingGoldCoinToastPrefabCount <= 0)
        {
            _goldCoinToastPrefabPreloadCompleted = true;
        }

        if (_dailyChallengeLevelTextPreloadRequested && _pendingDailyChallengeLevelTextCount <= 0)
        {
            _dailyChallengeLevelTextPreloadCompleted = true;
        }

        if (_scoreDigitSpritePreloadRequested && _pendingScoreDigitSpriteCount <= 0)
        {
            _scoreDigitSpritePreloadCompleted = true;
        }

        if (_scoreDigitSmallSpritePreloadRequested && _pendingScoreDigitSmallSpriteCount <= 0)
        {
            _scoreDigitSmallSpritePreloadCompleted = true;
        }

        if (_headPortraitPreloadRequested && _pendingHeadPortraitAssetCount <= 0)
        {
            _headPortraitPreloadCompleted = true;
        }

        if (_headPortraitFramePreloadRequested && _pendingHeadPortraitFrameAssetCount <= 0)
        {
            _headPortraitFramePreloadCompleted = true;
        }

        if (_architectureSpritePreloadRequested && _pendingArchitectureSpriteCount <= 0)
        {
            _architectureSpritePreloadCompleted = true;
        }

        if (_dailyChallengeCardSpritePreloadRequested && _pendingDailyChallengeCardSpriteCount <= 0)
        {
            _dailyChallengeCardSpritePreloadCompleted = true;
        }

    }

    /// <summary>
    /// 从资源路径中提取末尾文件名。
    /// 例如：Arts/Fruit/FruitTJ/WP_80001 -> WP_80001。
    /// </summary>
    /// <param name="assetPath">资源路径。</param>
    /// <returns>末尾文件名；提取失败时返回原字符串。</returns>
    private static string ExtractAssetLeafName(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return string.Empty;
        }

        int slashIndex = assetPath.LastIndexOf('/');
        if (slashIndex < 0 || slashIndex >= assetPath.Length - 1)
        {
            return assetPath.Trim();
        }

        return assetPath.Substring(slashIndex + 1).Trim();
    }

    /// <summary>
    /// 记录一次预加载失败并输出警告日志。
    /// 资源预加载失败不会阻塞进入主界面，只影响对应资源是否能命中缓存。
    /// </summary>
    private void RegisterFailure(string errorMessage)
    {
        _hasPreloadFailure = true;
        _lastErrorMessage = errorMessage;
        Log.Warning(errorMessage);
    }

    /// <summary>
    /// 通知外部预加载状态已变化。
    /// </summary>
    private void NotifyPreloadStateChanged()
    {
        PreloadStateChanged?.Invoke();
    }
}
