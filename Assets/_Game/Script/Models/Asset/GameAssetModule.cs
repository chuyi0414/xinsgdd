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
    }

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
    /// 已缓存的水果图标，按水果 Code 索引。
    /// </summary>
    private readonly Dictionary<string, Sprite> _fruitSpritesByCode = new Dictionary<string, Sprite>(StringComparer.Ordinal);

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
    /// 当前仍在加载中的水果图标路径集合。
    /// </summary>
    private readonly HashSet<string> _loadingFruitAssetPaths = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前仍在加载中的宠物实体预制体路径集合。
    /// </summary>
    private readonly HashSet<string> _loadingPetEntityPrefabPaths = new HashSet<string>(StringComparer.Ordinal);

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
    /// 当前待完成的水果图标加载数量。
    /// </summary>
    private int _pendingFruitAssetCount;

    /// <summary>
    /// 当前待完成的宠物实体预制体加载数量。
    /// </summary>
    private int _pendingPetEntityPrefabCount;

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
    /// 是否已经发起过蛋图标预加载。
    /// </summary>
    private bool _eggPreloadRequested;

    /// <summary>
    /// 是否已经发起过宠物 SkeletonData 预加载。
    /// </summary>
    private bool _petPreloadRequested;

    /// <summary>
    /// 是否已经发起过水果图标预加载。
    /// </summary>
    private bool _fruitPreloadRequested;

    /// <summary>
    /// 是否已经发起过宠物实体预制体预热。
    /// </summary>
    private bool _petEntityPrefabPreloadRequested;

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
    /// 蛋图标预加载是否已经完成。
    /// </summary>
    private bool _eggPreloadCompleted;

    /// <summary>
    /// 宠物 SkeletonData 预加载是否已经完成。
    /// </summary>
    private bool _petPreloadCompleted;

    /// <summary>
    /// 水果图标预加载是否已经完成。
    /// </summary>
    private bool _fruitPreloadCompleted;

    /// <summary>
    /// 宠物实体预制体预热是否已经完成。
    /// </summary>
    private bool _petEntityPrefabPreloadCompleted;

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
    /// 已预热缓存的宠物实体预制体。
    /// </summary>
    private GameObject _petEntityPrefab;

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
    public bool IsReady => _eggPreloadCompleted
        && _petPreloadCompleted
        && _fruitPreloadCompleted
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
        if (!_petEntityPrefabPreloadRequested)
        {
            BeginPreloadPetEntityPrefab();
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
            BeginPreloadPetSkeletonDataAssets(GameEntry.DataTables.GetAllDataRows<PetDataRow>());
        }

        if (!_fruitPreloadRequested && GameEntry.DataTables.IsAvailable<FruitDataRow>())
        {
            BeginPreloadFruitSprites(GameEntry.DataTables.GetAllDataRows<FruitDataRow>());
        }
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

        UpdatePreloadCompletionState();
        NotifyPreloadStateChanged();
    }

    /// <summary>
    /// 预热宠物实体预制体资源。
    /// </summary>
    private void BeginPreloadPetEntityPrefab()
    {
        _petEntityPrefabPreloadRequested = true;
        _petEntityPrefabPreloadCompleted = false;

        if (_petEntityPrefab != null || _loadingPetEntityPrefabPaths.Contains(EntityDefine.PetEntity))
        {
            UpdatePreloadCompletionState();
            NotifyPreloadStateChanged();
            return;
        }

        if (!TryLoadAsset(EntityDefine.PetEntity, typeof(GameObject), PreloadAssetKind.PetEntityPrefab))
        {
            RegisterFailure(Utility.Text.Format("预热宠物实体预制体失败，无法开始加载资源，Path='{0}'。", EntityDefine.PetEntity));
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
    /// 同一路径会先聚合校验信息，再共享一次实际加载。
    /// </summary>
    private void StartLoadPetSkeletonData(PetDataRow row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.SkeletonDataPath))
        {
            RegisterFailure("预加载宠物 SkeletonData 失败，宠物表存在空 SkeletonDataPath。");
            return;
        }

        AddPetValidationInfo(row);
        if (_petSkeletonDataAssetsByPath.TryGetValue(row.SkeletonDataPath, out SkeletonDataAsset cachedSkeletonDataAsset) && cachedSkeletonDataAsset != null)
        {
            ValidatePetSkeletonData(row.SkeletonDataPath, cachedSkeletonDataAsset);
            return;
        }

        if (_loadingPetAssetPaths.Contains(row.SkeletonDataPath))
        {
            return;
        }

        if (!TryLoadAsset(row.SkeletonDataPath, typeof(SkeletonDataAsset), PreloadAssetKind.PetSkeletonData))
        {
            RegisterFailure(Utility.Text.Format("预加载宠物 SkeletonData 失败，无法开始加载资源，Code='{0}'，Path='{1}'。", row.Code, row.SkeletonDataPath));
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
        }

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

        UpdatePreloadCompletionState();
        NotifyPreloadStateChanged();
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
            return;
        }

        _petSkeletonDataAssetsByPath[skeletonDataPath] = skeletonDataAsset;
        ValidatePetSkeletonData(skeletonDataPath, skeletonDataAsset);
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
    /// 记录某条宠物表对应的动画校验信息。
    /// </summary>
    private void AddPetValidationInfo(PetDataRow row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.SkeletonDataPath))
        {
            return;
        }

        if (!_petValidationInfosByPath.TryGetValue(row.SkeletonDataPath, out List<PetSkeletonValidationInfo> validationInfos))
        {
            validationInfos = new List<PetSkeletonValidationInfo>();
            _petValidationInfosByPath.Add(row.SkeletonDataPath, validationInfos);
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

        if (_fruitPreloadRequested && _pendingFruitAssetCount <= 0)
        {
            _fruitPreloadCompleted = true;
        }

        if (_petEntityPrefabPreloadRequested && _pendingPetEntityPrefabCount <= 0)
        {
            _petEntityPrefabPreloadCompleted = true;
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
