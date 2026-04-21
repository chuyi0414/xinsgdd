using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 消除卡片控制器。
/// 它是一个纯 C# 运行时对象，不挂到场景：
/// 1. 读取本地 CSV；
/// 2. 解析固定布局；
/// 3. 将 CSV 的逻辑坐标转换为当前主相机下的世界坐标；
/// 4. 生成/回收 UGF 卡片实体；
/// 5. 按整体包围盒适配主相机正交尺寸。
/// </summary>
public sealed class EliminateCardController
{
    /// <summary>
    /// 当前控制器静态实例。
    /// 卡片/区域实体逻辑通过此引用自动注册回调和引用。
    /// 生命周期：RebuildPreview 时赋值，Dispose 时清空。
    /// </summary>
    public static EliminateCardController Instance { get; private set; }
    /// <summary>
    /// 默认卡图名称。
    /// 这个名字同时作为“缺省精灵键”使用。
    /// </summary>
    private const string DefaultCardSpriteName = "WP_80001";

    /// <summary>
    /// 世界卡片所在的 Z 平面基准值。
    /// LayerIndex 越大（层级越高），Z 值越大，越靠近相机，视觉上覆盖低层卡片。
    /// </summary>
    private const float EntityWorldZ = 0f;

    /// <summary>
    /// 每个层级之间的 Z 轴间距。
    /// 与源项目 CardGenerator.layerHeight 口径一致，确保高层卡片在 Z 轴上偏移靠近相机。
    /// </summary>
    private const float LayerHeight = 0.1f;

    /// <summary>
    /// 单元格间距倍率。
    /// 源项目零间隙口径，卡片紧密排列无缝隙。
    /// </summary>
    private const float CellSpacingMultiplier = 1.0f;

    /// <summary>
    /// Legacy DIR 的固定锚点定义（col, row）。
    /// 按源场景 Inspector 锚点反推到逻辑格坐标，固化在代码里。
    /// 第 1 个锚点在主盘左侧，第 2 个锚点在主盘右侧。
    /// </summary>
    private static readonly Vector2[] LegacyDirAnchors = new Vector2[]
    {
        new Vector2(0f, 8.5f),
        new Vector2(8f, 8.5f),
    };

    /// <summary>
    /// Legacy DIR 的间距倍率。
    /// 与源场景 Inspector 中 spacing = 0.05 保持一致。
    /// </summary>
    private const float LegacyDirSpacing = 0.05f;

    /// <summary>
    /// 消除区域实体的固定锚点定义（col, row）。
    /// 口径与 LegacyDirAnchors 一致：col 为逻辑列，row 为逻辑行（正值向下）。
    /// 调整此值即可控制区域 prefab 在棋盘上的位置。
    /// </summary>
    private static readonly Vector2 EliminateTheAreaAnchor = new Vector2(4f, 11.5f);

    /// <summary>
    /// 道具置出区锚点定义（col, row）。
    /// 移出道具和拿取道具的卡片飞向此位置后再回收。
    /// 口径与 EliminateTheAreaAnchor 一致：col 为逻辑列，row 为逻辑行（正值向下）。
    /// 调整此值即可控制置出区在棋盘上的位置。
    /// </summary>
    private static readonly Vector2 PropOutputZoneAnchor = new Vector2(3f, 8.5f);

    /// <summary>
    /// 道具卡片飞向置出区的动画时长（秒）。
    /// </summary>
    private const float PropFlyToOutputDuration = 0.3f;

    /// <summary>
    /// 道具移出后前移补位动画时长（秒）。
    /// 与 EliminateTheAreaEntityLogic.CompactMoveDuration 口径一致。
    /// </summary>
    private const float PropCompactMoveDuration = 0.25f;

    /// <summary>
    /// 固定视口的逻辑列跨度。
    /// 主盘 8 列 (0~7) + DIR 两侧各 1 列 (-1, 8) = 10。
    /// </summary>
    private const float FixedViewportLogicalCols = 11f;

    /// <summary>
    /// 固定视口的逻辑行跨度。
    /// 主盘 8 行 (0~7) + 间隔 + DIR 行 (9.5) + 半卡上下余量 = 10.5。
    /// </summary>
    private const float FixedViewportLogicalRows = 12f;

    /// <summary>
    /// 主区期望使用的基础行数。
    /// 当前消除卡片主盘口径固定按 8 行设计；
    /// 如果实际只有 6 行、5 行，就需要优先往下补，让底边仍然对齐到第 8 行。
    /// </summary>
    private const int MainAreaTargetRowCount = 8;

    /// <summary>
    /// 相机边界额外留白。
    /// 用于避免棋盘刚好贴着屏幕边缘，导致视觉显得拥挤。
    /// </summary>
    private const float CameraPaddingWorldUnits = 0f;

    /// <summary>
    /// 卡片排序基础值。
    /// 让当前模式下的消除卡片整体压在普通场地实体之上，避免偶发穿插。
    /// </summary>
    private const int SortingOrderBase = 200;

    /// <summary>
    /// 新卡片实体组的自动释放检测间隔。
    /// </summary>
    private const float EntityGroupAutoReleaseInterval = 60f;

    /// <summary>
    /// 新卡片实体组的实例容量。
    /// </summary>
    private const int EntityGroupCapacity = 500;

    /// <summary>
    /// 新卡片实体组的实例过期时间。
    /// </summary>
    private const float EntityGroupExpireTime = 60f;

    /// <summary>
    /// 新卡片实体组的优先级。
    /// </summary>
    private const int EntityGroupPriority = 0;

    /// <summary>
    /// 统一卡片显示颜色。
    /// 不再使用颜色板区分类型，所有卡片显示原始贴图颜色。
    /// </summary>
    private static readonly Color UniformCardColor = Color.white;

    /// <summary>
    /// 被上层卡片压住时的显示颜色。
    /// 参考项目 xinpgdd 的正式玩法不是用 SpriteMask，而是直接把被遮挡卡片整体置灰。
    /// 这里沿用同样的灰度口径，让每日预览至少先具备正确的“遮罩感”。
    /// </summary>
    private static readonly Color BlockedCardTintColor = new Color(0.6f, 0.6f, 0.6f, 1f);

    /// <summary>
    /// 重叠判定容差。
    /// 与参考项目 EliminateManager 中的 0.01f 口径保持一致，避免浮点误差导致边缘卡片忽亮忽灰。
    /// </summary>
    private const float BlockingOverlapTolerance = 0.01f;

    /// <summary>
    /// bbl1 左侧 DIR 单阻挡上层卡布局索引。
    /// 用于定向跟踪“点击后下层是否立即解锁”。
    /// </summary>
    private const int DebugLeftDirUpperLayoutIndex = 229;

    /// <summary>
    /// bbl1 左侧 DIR 单阻挡下层卡布局索引。
    /// </summary>
    private const int DebugLeftDirLowerLayoutIndex = 228;

    /// <summary>
    /// bbl1 右侧 DIR 单阻挡上层卡布局索引。
    /// </summary>
    private const int DebugRightDirUpperLayoutIndex = 239;

    /// <summary>
    /// bbl1 右侧 DIR 单阻挡下层卡布局索引。
    /// </summary>
    private const int DebugRightDirLowerLayoutIndex = 238;

    /// <summary>
    /// 当前已生成的实体 Id 列表。
    /// 页面刷新或关闭时，会按这个列表统一回收。
    /// </summary>
    private readonly List<int> _activeEntityIds = new List<int>(300);

    /// <summary>
    /// 卡片实体逻辑缓存：entityId -> EliminateCardEntityLogic。
    /// 用于控制器快速查找卡片逻辑，更新遮挡状态等。
    /// </summary>
    private readonly Dictionary<int, EliminateCardEntityLogic> _cardLogics = new Dictionary<int, EliminateCardEntityLogic>(300);

    /// <summary>
    /// 卡片实体逻辑直索引缓存：layoutIndex -> EliminateCardEntityLogic。
    /// 遮挡重算时直接按布局索引命中目标实体，避免线性遍历。
    /// </summary>
    private readonly Dictionary<int, EliminateCardEntityLogic> _cardLogicsByLayoutIndex = new Dictionary<int, EliminateCardEntityLogic>(300);

    /// <summary>
    /// 消除区域实体逻辑引用。
    /// 卡片点击后通过此引用调用 TryRequestInsert。
    /// </summary>
    private EliminateTheAreaEntityLogic _areaEntityLogic;

    /// <summary>
    /// 当前是否处于失败状态（满格单牌过多）。
    /// 用于 IsExitUIForm 判断是否应弹出 VictoryFailUIForm(Fail) 而非直接返回。
    /// </summary>
    private bool _hasFailed;

    /// <summary>
    /// 胜利事件：所有棋盘卡片消除完毕时触发。
    /// 由 CombatUIForm 订阅，弹出 VictoryFailUIForm(Victory)。
    /// </summary>
    public event Action OnVictory;

    /// <summary>
    /// 失败事件：等待区满格且有 2 张以上单牌时触发。
    /// 由 CombatUIForm 订阅，弹出 VictoryFailUIForm(Fail)。
    /// </summary>
    public event Action OnFail;

    /// <summary>
    /// 得分更新事件：每次满格清空结算得分后触发。
    /// 事件参数为当前累计得分（int）。
    /// 由 CombatUIForm 订阅，刷新界面上的分数文本。
    /// </summary>
    public event Action<int> OnScoreUpdated;

    /// <summary>
    /// 拿取状态变化事件：进入/退出拿取状态时触发。
    /// 事件参数为当前是否处于拿取状态（bool）。
    /// 由 CombatUIForm 订阅，更新拿取按钮视觉反馈。
    /// </summary>
    public event Action<bool> OnTakeStateChanged;

    // ───────────── 拿取状态 ─────────────

    /// <summary>
    /// 当前是否处于拿取状态。
    /// 拿取状态下，玩家点击棋盘卡片直接回收（不入等待区），最多拿取 _maxTakeCount 张。
    /// </summary>
    private bool _isTakeState;

    /// <summary>
    /// 拿取状态中已拿取的卡片数量。
    /// </summary>
    private int _takenCount;

    /// <summary>
    /// 拿取状态中最多可拿取的卡片数量。
    /// 取 min(3, 棋盘未遮挡卡数)。
    /// </summary>
    private int _maxTakeCount;

    // ───────────── 置出区 ─────────────

    /// <summary>
    /// 置出区卡片列表。
    /// 移出/拿取道具飞入置出区的卡片在此列表中管理，卡片继续存活显示，不回收。
    /// </summary>
    private readonly List<EliminateCardEntityLogic> _outputZoneCards = new List<EliminateCardEntityLogic>();

    /// <summary>
    /// 置出区每行最多卡片数。第4张卡片回到第1张的 X 位置开始新行。
    /// </summary>
    private const int OutputZoneCardsPerRow = 3;

    /// <summary>
    /// 置出区卡片间 Z 轴偏移量（世界坐标）。
    /// 后进入的卡片 Z 递减（离相机更近），确保 Raycast 命中上层卡片。
    /// ⚠️ 避坑：Z 必须递减，递增会导致 Raycast 命中底层卡片！
    /// </summary>
    private const float OutputZoneZOffset = 0.1f;

    /// <summary>
    /// 置出区卡片间 X 轴偏移量（世界坐标），每张卡片向右偏移一个卡片宽度。
    /// </summary>
    private float OutputZoneXOffset => _cachedCellWidth;

    // ───────────── 得分与连击 ─────────────

    /// <summary>
    /// 当前累计得分。
    /// 每次满格清空时累加本轮得分，胜利时乘以翻倍倍率。
    /// </summary>
    private int _currentScore;

    /// <summary>
    /// 当前连击计数。
    /// 在连击时间窗口内连续满格清空则 +1，失败或超时则重置为 0。
    /// </summary>
    private int _comboCount;

    /// <summary>
    /// 上一次满格清空时的实时时间（秒）。
    /// 用于判断两次清空是否在连击时间窗口内。
    /// -1f 表示尚未发生过清空。
    /// </summary>
    private float _lastSettlementRealTime = -1f;

    /// <summary>
    /// 当前轮次（从1开始）。
    /// 每次满格清空结算后 +1，用于计算递增基础分。
    /// </summary>
    private int _currentRound = 1;

    /// <summary>
    /// 得分配置行缓存。
    /// RebuildPreview 时从数据表读取，避免每次结算都查表。
    /// </summary>
    private DailyChallengeScoreDataRow _scoreConfig;

    /// <summary>
    /// 得分配置默认值：每轮基础分。
    /// 数据表不可用时回退到此值。
    /// </summary>
    private const int DefaultBaseScorePerCard = 1;

    /// <summary>
    /// 得分配置默认值：同类型 2 张分量分。
    /// </summary>
    private const int DefaultSameTypeTwoScorePerCard = 2;

    /// <summary>
    /// 得分配置默认值：连击时间窗口（秒）。
    /// </summary>
    private const float DefaultComboWindowSeconds = 12f;

    /// <summary>
    /// 得分配置默认值：连击倍率。
    /// </summary>
    private const float DefaultComboMultiplier = 1f;

    /// <summary>
    /// 得分配置默认值：胜利分数翻倍倍率。
    /// </summary>
    private const int DefaultVictoryScoreMultiplier = 2;

    // ───────────── 棋盘摆盘数据缓存（用于重算遮挡） ─────────────

    /// <summary>
    /// 缓存的逻辑卡片列表。
    /// 卡片入等待区后需要重算遮挡，此时复用缓存数据避免重新解析 CSV。
    /// </summary>
    private List<EliminateCardLogicalCard> _cachedLogicalCards;

    /// <summary>
    /// 缓存的卡片投影世界 X 坐标数组。
    /// </summary>
    private float[] _cachedProjectedWorldX;

    /// <summary>
    /// 缓存的卡片投影世界 Y 坐标数组。
    /// </summary>
    private float[] _cachedProjectedWorldY;

    /// <summary>
    /// 缓存的单元格宽度。
    /// </summary>
    private float _cachedCellWidth;

    /// <summary>
    /// 缓存的单元格高度。
    /// </summary>
    private float _cachedCellHeight;

    /// <summary>
    /// 已从棋盘移除（入等待区或被回收）的卡片布局索引集合。
    /// 重算遮挡时跳过这些卡片，它们不再遮挡其他卡。
    /// </summary>
    private readonly HashSet<int> _removedFromBoard = new HashSet<int>();

    /// <summary>
    /// 预览期间主相机的额外 Y 轴偏移量。
    /// 负值表示“相机向下移动”，这样屏幕里看到的棋盘会整体往上抬
    /// </summary>
    private float _previewCameraOffsetY = -50f;

    /// <summary>
    /// 预览期间仅作用于相机本身的额外 XY 偏移量。
    /// 这个偏移不会参与棋盘世界坐标计算，只改变相机最终取景位置。
    /// </summary>
    private Vector2 _previewCameraOnlyOffset = new Vector2(0f, -3f);

    /// <summary>
    /// 当前绑定的主相机。
    /// 用来做棋盘居中与镜头尺寸恢复。
    /// </summary>
    private Camera _boundCamera;

    /// <summary>
    /// 是否已经缓存过主相机原始尺寸。
    /// 只缓存一次，避免在重复预览时把“已调整后的尺寸”误当成原始值。
    /// </summary>
    private bool _hasCapturedCameraState;

    /// <summary>
    /// 打开每日页前主相机的原始正交尺寸。
    /// 页面关闭时需要恢复。
    /// </summary>
    private float _originalOrthographicSize;

    /// <summary>
    /// 打开每日页前主相机的原始世界坐标。
    /// 一旦预览阶段给相机做了 Y 偏移，退出页面时必须用这个值精确恢复。
    /// 同时，摆盘计算也要基于这个“原始相机中心”，
    /// 不能直接吃已经被预览逻辑修改过的位置，否则重复预览会越算越偏。
    /// </summary>
    private Vector3 _originalCameraPosition;

    /// <summary>
    /// 根据一份本地 CSV 重建当前棋盘预览。
    /// </summary>
    /// <param name="levelAssetPath">本地 TextAsset 资源路径，不带扩展名。</param>
    /// <returns>本次预览构建结果。</returns>
    public EliminateCardPreviewResult RebuildPreview(string levelAssetPath)
    {
        if (GameEntry.Entity == null)
        {
            return EliminateCardPreviewResult.Failed("EntityComponent 缺失，无法生成消除卡片。");
        }

        if (GameEntry.GameAssets == null)
        {
            return EliminateCardPreviewResult.Failed("GameAssetModule 缺失，无法读取消除卡片资源缓存。");
        }

        TextAsset levelAsset = LoadLevelAsset(levelAssetPath);
        if (levelAsset == null)
        {
            return EliminateCardPreviewResult.Failed($"关卡 CSV 不存在：{levelAssetPath}");
        }

        DailyChallengeParsedLevel parsedLevel = DailyChallengeCsvParser.Parse(levelAsset.text);
        if (parsedLevel == null || parsedLevel.FixedCards.Count <= 0)
        {
            return EliminateCardPreviewResult.Failed("CSV 解析成功，但没有任何固定布局卡片。");
        }

        Camera worldCamera = ResolveMainCamera();
        if (worldCamera == null)
        {
            return EliminateCardPreviewResult.Failed("找不到 Main Camera，无法投影消除卡片。");
        }

        EnsureEntityGroupExists();
        CaptureCameraState(worldCamera);

        List<EliminateCardLogicalCard> logicalCards = BuildLogicalCards(parsedLevel, out int ignoredLegacyDirectionTaskCount);
        if (logicalCards.Count <= 0)
        {
            return EliminateCardPreviewResult.Failed("本地迁移版暂未拿到可生成的逻辑卡片。");
        }

        List<EliminateCardAssignedTypeVisual> visuals = BuildAssignedTypeVisuals(logicalCards.Count, parsedLevel.TypeConfigs);
        if (visuals.Count < logicalCards.Count)
        {
            return EliminateCardPreviewResult.Failed("卡片类型分配失败，生成的视觉数据不足。");
        }

        ShuffleAssignedTypeVisuals(visuals);

        List<EliminateCardSpawnInstruction> spawnInstructions = BuildSpawnInstructions(worldCamera, logicalCards, visuals);
        if (spawnInstructions.Count <= 0)
        {
            return EliminateCardPreviewResult.Failed("未能根据 CSV 生成任何实体落点。");
        }

        // 所有前置校验与数据准备都通过后，才真正切到“新一局”。
        // 这样即使外部在旧 CombatUIForm 上直接调用 RebuildPreview，
        // 也不会因为资源缺失/CSV 异常等前置失败，先把当前棋盘清掉再返回 false。
        Instance = this;

        // ── 重置新一局的运行时状态 ──
        _currentScore = 0;
        _comboCount = 0;
        _lastSettlementRealTime = -1f;
        _currentRound = 1;
        _scoreConfig = null;
        _hasFailed = false;
        _isTakeState = false;
        _takenCount = 0;
        _maxTakeCount = 0;

        // ⚠️ 这里必须保留 BuildSpawnInstructions 刚刚缓存好的棋盘几何数据，
        // 否则点击入等待区后，UpdateBlockingAfterRemoval 会因为缓存被清空直接返回，
        // 下层卡片永远停留在首帧置灰状态。
        ClearSpawnedEntities(false);
        ApplyCameraFit(worldCamera, spawnInstructions);
        SpawnCards(spawnInstructions);
        ShowEliminateTheAreaEntity(worldCamera, spawnInstructions);

        return EliminateCardPreviewResult.Succeeded(
            levelAssetPath,
            spawnInstructions.Count,
            ignoredLegacyDirectionTaskCount);
    }

    /// <summary>
    /// 释放当前预览状态。
    /// 包括：隐藏卡片实体、恢复主相机原始尺寸、清空静态实例。
    /// </summary>
    public void Dispose()
    {
        ClearSpawnedEntities();
        RestoreCameraState();

        // ── 重置得分/连击状态 ──
        _currentScore = 0;
        _comboCount = 0;
        _lastSettlementRealTime = -1f;
        _currentRound = 1;
        _scoreConfig = null;

        Instance = null;
    }

    /// <summary>
    /// 读取本地关卡 CSV 资源。
    /// </summary>
    private static TextAsset LoadLevelAsset(string levelAssetPath)
    {
        if (string.IsNullOrWhiteSpace(levelAssetPath) || GameEntry.GameAssets == null)
        {
            return null;
        }

        GameEntry.GameAssets.TryGetDailyChallengeLevelText(levelAssetPath, out TextAsset levelText);
        return levelText;
    }

    /// <summary>
    /// 确保消除卡片实体组已经存在。
    /// 这里不要求场景预先配置，缺失时直接在运行时补建。
    /// </summary>
    private static void EnsureEntityGroupExists()
    {
        if (GameEntry.Entity == null || GameEntry.Entity.HasEntityGroup(EntityDefine.EliminateCardGroup))
        {
            return;
        }

        GameEntry.Entity.AddEntityGroup(
            EntityDefine.EliminateCardGroup,
            EntityGroupAutoReleaseInterval,
            EntityGroupCapacity,
            EntityGroupExpireTime,
            EntityGroupPriority);
    }

    /// <summary>
    /// 解析一批真正可用于摆盘的逻辑卡片。
    /// 固定布局卡直接使用 CSV 自带坐标；
    /// Legacy DIR 因为缺少源工程 Inspector 起点，当前阶段明确跳过并统计数量。
    /// </summary>
    private static List<EliminateCardLogicalCard> BuildLogicalCards(
        DailyChallengeParsedLevel parsedLevel,
        out int ignoredLegacyDirectionTaskCount)
    {
        ignoredLegacyDirectionTaskCount = 0;
        List<EliminateCardLogicalCard> logicalCards = new List<EliminateCardLogicalCard>(parsedLevel.FixedCards.Count);
        int nextLayoutIndex = parsedLevel.FixedCards.Count;

        for (int i = 0; i < parsedLevel.FixedCards.Count; i++)
        {
            DailyChallengeFixedCardRecord fixedCard = parsedLevel.FixedCards[i];
            float logicalX = fixedCard.ColIndex + fixedCard.OffsetX * fixedCard.OffsetAmount;
            float logicalY = -fixedCard.RowIndex + fixedCard.OffsetY * fixedCard.OffsetAmount;

            logicalCards.Add(new EliminateCardLogicalCard(
                EliminateCardArea.Main,
                fixedCard.LayoutIndex,
                fixedCard.LayerIndex,
                fixedCard.RowIndex,
                logicalX,
                logicalY));
        }

        if (parsedLevel.DirectionTasks.Count > 0)
        {
            // ---- Legacy DIR 与 Extended DIR 统一生成 ----
            // Legacy DIR（UseGridStart == false）不再跳过，而是使用
            // LegacyDirAnchors 固化锚点按顺序分配起始位置。
            // Extended DIR（UseGridStart == true）继续使用 CSV 提供的行列起点。
            int legacyAnchorIndex = 0;

            for (int i = 0; i < parsedLevel.DirectionTasks.Count; i++)
            {
                DailyChallengeDirectionTask directionTask = parsedLevel.DirectionTasks[i];
                Vector2 directionStep = GetDirectionStep(directionTask.Direction);

                if (directionTask.UseGridStart)
                {
                    // ---- Extended DIR：使用 CSV 提供的行列起点 ----
                    for (int countIndex = 0; countIndex < directionTask.Count; countIndex++)
                    {
                        float logicalX = directionTask.StartCol + directionStep.x * directionTask.Spacing * countIndex;
                        float logicalY = -directionTask.StartRow + directionStep.y * directionTask.Spacing * countIndex;
                        int layerIndex = directionTask.StartLayer + countIndex;

                        logicalCards.Add(new EliminateCardLogicalCard(
                            EliminateCardArea.Dir,
                            nextLayoutIndex++,
                            layerIndex,
                            directionTask.StartRow,
                            logicalX,
                            logicalY));
                    }
                }
                else
                {
                    // ---- Legacy DIR：使用固化锚点作为起始位置 ----
                    // 按顺序从 LegacyDirAnchors 取锚点，超出锚点数量则跳过并计数。
                    if (legacyAnchorIndex >= LegacyDirAnchors.Length)
                    {
                        ignoredLegacyDirectionTaskCount++;
                        Log.Warning(
                            "EliminateCardController: legacy DIR 任务 #{0} 超出固化锚点数量（{1}），跳过。",
                            i, LegacyDirAnchors.Length);
                        continue;
                    }

                    Vector2 anchor = LegacyDirAnchors[legacyAnchorIndex++];
                    float startCol = anchor.x;
                    float startRow = anchor.y;

                    for (int countIndex = 0; countIndex < directionTask.Count; countIndex++)
                    {
                        float logicalX = startCol + directionStep.x * LegacyDirSpacing * countIndex;
                        float logicalY = -startRow + directionStep.y * LegacyDirSpacing * countIndex;
                        int layerIndex = directionTask.StartLayer + countIndex;

                        logicalCards.Add(new EliminateCardLogicalCard(
                            EliminateCardArea.Dir,
                            nextLayoutIndex++,
                            layerIndex,
                            Mathf.RoundToInt(startRow),
                            logicalX,
                            logicalY));
                    }
                }
            }
        }

        return logicalCards;
    }

    /// <summary>
    /// 将方向枚举转换为逻辑平面的步进向量。
    /// 这里返回的不是归一化向量，而是“每次前进一步时，逻辑行列各该变化多少格”。
    /// </summary>
    private static Vector2 GetDirectionStep(DailyChallengeDirection direction)
    {
        switch (direction)
        {
            case DailyChallengeDirection.Up:
                return new Vector2(0f, 1f);
            case DailyChallengeDirection.Down:
                return new Vector2(0f, -1f);
            case DailyChallengeDirection.Left:
                return new Vector2(-1f, 0f);
            case DailyChallengeDirection.Right:
                return new Vector2(1f, 0f);
            case DailyChallengeDirection.UpLeft:
                return new Vector2(-1f, 1f);
            case DailyChallengeDirection.UpRight:
                return new Vector2(1f, 1f);
            case DailyChallengeDirection.DownLeft:
                return new Vector2(-1f, -1f);
            case DailyChallengeDirection.DownRight:
                return new Vector2(1f, -1f);
            default:
                return Vector2.zero;
        }
    }

    /// <summary>
    /// 根据 CSV 类型配置，为每一张逻辑卡片分配一个显示类型。
    /// 策略：
    /// 1. 先尊重 CSV 里写死数量的固定类型；
    /// 2. 剩余卡片若遇到 '-1' 占位，则从已解锁水果卡池轮换分配不同卡图；
    /// 3. 若水果表不可用，回退到默认卡图。
    /// </summary>
    private List<EliminateCardAssignedTypeVisual> BuildAssignedTypeVisuals(
        int cardCount,
        List<DailyChallengeTypeConfig> typeConfigs)
    {
        List<EliminateCardAssignedTypeVisual> visuals = new List<EliminateCardAssignedTypeVisual>(cardCount);
        Dictionary<string, int> namedTypeIdBySpriteName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        List<string> namedSpriteOrder = new List<string>();
        int nextTypeId = 1;

        if (typeConfigs != null)
        {
            for (int i = 0; i < typeConfigs.Count; i++)
            {
                DailyChallengeTypeConfig config = typeConfigs[i];
                if (config == null || config.IsRandomPlaceholder || string.IsNullOrWhiteSpace(config.SpriteName))
                {
                    continue;
                }

                if (namedTypeIdBySpriteName.ContainsKey(config.SpriteName))
                {
                    continue;
                }

                namedTypeIdBySpriteName.Add(config.SpriteName, nextTypeId++);
                namedSpriteOrder.Add(config.SpriteName);
            }

            for (int i = 0; i < typeConfigs.Count; i++)
            {
                DailyChallengeTypeConfig config = typeConfigs[i];
                if (config == null || config.IsRandomPlaceholder || config.FixedCount <= 0)
                {
                    continue;
                }

                if (!namedTypeIdBySpriteName.TryGetValue(config.SpriteName, out int typeId))
                {
                    continue;
                }

                for (int countIndex = 0; countIndex < config.FixedCount && visuals.Count < cardCount; countIndex++)
                {
                    visuals.Add(CreateAssignedTypeVisual(typeId, config.SpriteName));
                }
            }
        }

        int remainingCount = cardCount - visuals.Count;
        if (remainingCount <= 0)
        {
            return visuals;
        }

        List<DailyChallengeTypeConfig> placeholderConfigs = null;
        if (typeConfigs != null)
        {
            placeholderConfigs = new List<DailyChallengeTypeConfig>(typeConfigs.Count);
            for (int i = 0; i < typeConfigs.Count; i++)
            {
                DailyChallengeTypeConfig config = typeConfigs[i];
                if (config != null && config.IsRandomPlaceholder)
                {
                    placeholderConfigs.Add(config);
                }
            }
        }

        if (placeholderConfigs != null && placeholderConfigs.Count > 0)
        {
            // ---- 从已解锁水果卡池轮换分配卡图 ----
            // 只有 IsUnlocked == true 的水果才会进入随机卡池。
            HashSet<string> excludedSpriteNames = new HashSet<string>(namedSpriteOrder, StringComparer.OrdinalIgnoreCase);
            string[] unlockedSpriteNames = GetUnlockedFruitSpriteNames(excludedSpriteNames);
            int slotCount = unlockedSpriteNames != null ? Mathf.Min(placeholderConfigs.Count, unlockedSpriteNames.Length) : 0;
            if (slotCount > 0)
            {
                if (placeholderConfigs.Count != unlockedSpriteNames.Length)
                {
                    Log.Warning(
                        "EliminateCardController placeholder config count '{0}' does not match unlocked random pool count '{1}'. Using '{2}' entries.",
                        placeholderConfigs.Count,
                        unlockedSpriteNames.Length,
                        slotCount);
                }

                int[] allocatedCounts = AllocatePlaceholderCounts(remainingCount, placeholderConfigs, slotCount);
                for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
                {
                    string spriteName = unlockedSpriteNames[slotIndex];
                    if (!namedTypeIdBySpriteName.TryGetValue(spriteName, out int typeId))
                    {
                        typeId = nextTypeId++;
                        namedTypeIdBySpriteName.Add(spriteName, typeId);
                        namedSpriteOrder.Add(spriteName);
                    }

                    for (int countIndex = 0; countIndex < allocatedCounts[slotIndex] && visuals.Count < cardCount; countIndex++)
                    {
                        visuals.Add(CreateAssignedTypeVisual(typeId, spriteName));
                    }
                }

                return visuals;
            }

            for (int i = 0; i < remainingCount; i++)
            {
                visuals.Add(CreateAssignedTypeVisual(nextTypeId + i, DefaultCardSpriteName));
            }

            return visuals;
        }

        if (namedSpriteOrder.Count > 0)
        {
            for (int i = 0; i < remainingCount; i++)
            {
                string spriteName = namedSpriteOrder[i % namedSpriteOrder.Count];
                visuals.Add(CreateAssignedTypeVisual(namedTypeIdBySpriteName[spriteName], spriteName));
            }

            return visuals;
        }

        for (int i = 0; i < remainingCount; i++)
        {
            visuals.Add(CreateAssignedTypeVisual(1 + i, DefaultCardSpriteName));
        }

        return visuals;
    }

    /// <summary>
    /// 原地打乱最终的卡片视觉分配序列。
    /// 这里故意放在“数量分配完成”之后执行，只改变落位顺序，不改变每种卡图最终张数。
    /// 算法使用 Fisher-Yates 洗牌，口径与参考项目 xinpgdd 的正式卡片分配逻辑保持一致。
    /// </summary>
    /// <param name="visuals">已经构建完成、即将映射到逻辑卡位的视觉列表。</param>
    private static void ShuffleAssignedTypeVisuals(List<EliminateCardAssignedTypeVisual> visuals)
    {
        if (visuals == null || visuals.Count <= 1)
        {
            return;
        }

        for (int visualIndex = 0; visualIndex < visuals.Count; visualIndex++)
        {
            // 关键点：
            // 1. 随机范围从当前索引开始，而不是始终从 0 开始；
            // 2. 这是标准 Fisher-Yates 原地洗牌写法；
            // 3. 只交换结构体值，不创建新列表，避免额外 GC。
            int randomIndex = UnityEngine.Random.Range(visualIndex, visuals.Count);
            EliminateCardAssignedTypeVisual cachedVisual = visuals[visualIndex];
            visuals[visualIndex] = visuals[randomIndex];
            visuals[randomIndex] = cachedVisual;
        }
    }

    /// <summary>
    /// 从 Fruit 数据表中提取所有已解锁水果的卡图精灵名。
    /// 只有 IsUnlocked == true 的水果才会被纳入随机卡池。
    /// 精灵名从 IconPath 末尾提取（如 Arts/Fruit/FruitTJ/WP_80001 -> WP_80001）。
    /// </summary>
    /// <returns>已解锁水果的精灵名数组；水果表不可用时返回 null。</returns>
    private static string[] GetUnlockedFruitSpriteNames(HashSet<string> excludedSpriteNames)
    {
        if (GameEntry.DataTables == null)
        {
            return null;
        }

        FruitDataRow[] allFruits = GameEntry.DataTables.GetAllDataRows<FruitDataRow>();
        if (allFruits == null || allFruits.Length <= 0)
        {
            return null;
        }

        List<string> unlocked = new List<string>(allFruits.Length);
        HashSet<string> uniqueSpriteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < allFruits.Length; i++)
        {
            FruitDataRow fruit = allFruits[i];
            if (fruit == null || !fruit.IsUnlocked)
            {
                continue;
            }

            string spriteName = ExtractSpriteNameFromIconPath(fruit.IconPath);
            if (string.IsNullOrWhiteSpace(spriteName))
            {
                continue;
            }

            if (excludedSpriteNames != null && excludedSpriteNames.Contains(spriteName))
            {
                continue;
            }

            if (uniqueSpriteNames.Add(spriteName))
            {
                unlocked.Add(spriteName);
            }
        }

        return unlocked.Count > 0 ? unlocked.ToArray() : null;
    }

    private static int[] AllocatePlaceholderCounts(
        int totalCount,
        List<DailyChallengeTypeConfig> placeholderConfigs,
        int slotCount)
    {
        int[] allocatedCounts = new int[Mathf.Max(0, slotCount)];
        if (totalCount <= 0 || placeholderConfigs == null || slotCount <= 0)
        {
            return allocatedCounts;
        }

        float totalWeight = 0f;
        for (int i = 0; i < slotCount; i++)
        {
            totalWeight += Mathf.Max(0f, placeholderConfigs[i].Probability);
        }

        if (totalWeight <= 0f)
        {
            int evenCount = totalCount / slotCount;
            int remainderCount = totalCount % slotCount;
            for (int i = 0; i < slotCount; i++)
            {
                allocatedCounts[i] = evenCount + (i < remainderCount ? 1 : 0);
            }

            return allocatedCounts;
        }

        float[] fractionalParts = new float[slotCount];
        int assignedCount = 0;
        for (int i = 0; i < slotCount; i++)
        {
            float exactCount = totalCount * (Mathf.Max(0f, placeholderConfigs[i].Probability) / totalWeight);
            int floorCount = Mathf.FloorToInt(exactCount);
            allocatedCounts[i] = floorCount;
            fractionalParts[i] = exactCount - floorCount;
            assignedCount += floorCount;
        }

        int remainingAllocation = totalCount - assignedCount;
        while (remainingAllocation > 0)
        {
            int bestIndex = 0;
            float bestFraction = float.MinValue;
            for (int i = 0; i < slotCount; i++)
            {
                if (fractionalParts[i] > bestFraction)
                {
                    bestFraction = fractionalParts[i];
                    bestIndex = i;
                }
            }

            allocatedCounts[bestIndex]++;
            fractionalParts[bestIndex] = -1f;
            remainingAllocation--;
        }

        return allocatedCounts;
    }

    /// <summary>
    /// 从 IconPath 中提取末尾精灵名。
    /// 例如：Arts/Fruit/FruitTJ/WP_80001 -> WP_80001。
    /// 与 GameAssetModule.ExtractAssetLeafName 口径一致。
    /// </summary>
    private static string ExtractSpriteNameFromIconPath(string iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return string.Empty;
        }

        int slashIndex = iconPath.LastIndexOf('/');
        if (slashIndex < 0 || slashIndex >= iconPath.Length - 1)
        {
            return iconPath.Trim();
        }

        return iconPath.Substring(slashIndex + 1).Trim();
    }

    /// <summary>
    /// 创建单个"卡片显示类型"。
    /// 所有卡片统一使用原始贴图颜色（Color.white），不再染色。
    /// </summary>
    private EliminateCardAssignedTypeVisual CreateAssignedTypeVisual(int typeId, string spriteName)
    {
        Sprite sprite = TryLoadCardSprite(spriteName);

        if (sprite == null)
        {
            // 保险：如果真实卡图没迁进来，也必须保证实体还能显示。
            sprite = TryLoadCardSprite(DefaultCardSpriteName);
        }

        return new EliminateCardAssignedTypeVisual(typeId, sprite, UniformCardColor);
    }

    /// <summary>
    /// 构建最终的实体生成指令。
    /// 核心原则：
    /// 1. 卡片按自己的逻辑坐标独立落点；
    /// 2. 以固定视口（8x8 主盘 + DIR 行 9.5）的中心为锚点居中；
    /// 3. 世界 Z 固定为 0，只让 sortingOrder 承担“层”的职责。
    /// </summary>
    private List<EliminateCardSpawnInstruction> BuildSpawnInstructions(
        Camera worldCamera,
        List<EliminateCardLogicalCard> logicalCards,
        List<EliminateCardAssignedTypeVisual> visuals)
    {
        List<EliminateCardSpawnInstruction> spawnInstructions = new List<EliminateCardSpawnInstruction>(logicalCards.Count);
        if (logicalCards == null || logicalCards.Count <= 0 || visuals == null || visuals.Count <= 0 || worldCamera == null)
        {
            return spawnInstructions;
        }

        Sprite referenceSprite = visuals[0].DisplaySprite != null ? visuals[0].DisplaySprite : TryLoadCardSprite(DefaultCardSpriteName);
        // CellSpacingMultiplier = 1.0f，卡片紧密排列，无额外间隙。
        float cellWidth = referenceSprite != null ? Mathf.Max(0.5f, referenceSprite.bounds.size.x * CellSpacingMultiplier) : 1.15f;
        float cellHeight = referenceSprite != null ? Mathf.Max(0.5f, referenceSprite.bounds.size.y * CellSpacingMultiplier) : 1.15f;

        // ---- 固定视口居中 ----
        // 视口逻辑中心 = 固定视口跨度的中心点。
        // 主盘列范围 [-1, 8]（含 DIR 侧边），中心 = 3.5
        // 主盘行范围 [0, 9.5]（含 DIR 行），中心 = -4.75（逻辑Y已翻转）
        float fixedCenterX = (FixedViewportLogicalCols - 1f) * 0.5f + (-1f); // = 3.5
        float fixedCenterY = -(FixedViewportLogicalRows - 1f) * 0.5f;       // = -4.75
        float mainAreaCenterX = GetMainAreaLogicalCenterX(logicalCards, fixedCenterX);
        float mainAreaDownShiftRows = GetMainAreaBottomAlignShiftRows(logicalCards);
        Vector3 boardWorldCenter = GetPreviewBoardWorldCenter(worldCamera);

        float[] projectedWorldX = new float[logicalCards.Count];
        float[] projectedWorldY = new float[logicalCards.Count];

        int maxLayer = 0;
        for (int i = 0; i < logicalCards.Count; i++)
        {
            EliminateCardLogicalCard logicalCard = logicalCards[i];
            if (logicalCard.LayerIndex > maxLayer)
            {
                maxLayer = logicalCard.LayerIndex;
            }

            // 主区与 DIR 区使用不同的 X 轴投影基准：
            // 1. 主区：按“主区自身真实逻辑包围范围”的中心来投影，
            //    这样 6 列、5 列这类窄棋盘不会继续死贴固定 8 列视口左边。
            // 2. DIR 区：继续沿用固定视口中心，不跟着主区一起平移，
            //    保证两侧/下方附属卡位保持原始设计口径。
            float logicalCenterX = logicalCard.Area == EliminateCardArea.Main
                ? mainAreaCenterX
                : fixedCenterX;

            // 主区如果不足 8 行，不做“上下居中”，而是优先往下补齐：
            // 例如当前最高只铺到第 6 行（maxRow = 5），就整体下移 2 行，
            // 让主区底边继续落在第 8 行口径上。
            float mainAreaLogicalDownShift = logicalCard.Area == EliminateCardArea.Main
                ? mainAreaDownShiftRows
                : 0f;

            projectedWorldX[i] = boardWorldCenter.x + (logicalCard.LogicalX - logicalCenterX) * cellWidth;
            projectedWorldY[i] = boardWorldCenter.y + (logicalCard.LogicalY - fixedCenterY - mainAreaLogicalDownShift) * cellHeight;
        }

        bool[] blockedStates = CalculateBlockedStates(logicalCards, projectedWorldX, projectedWorldY, cellWidth, cellHeight);

        for (int i = 0; i < logicalCards.Count; i++)
        {
            EliminateCardLogicalCard logicalCard = logicalCards[i];
            EliminateCardAssignedTypeVisual visual = visuals[i % visuals.Count];

            float worldX = projectedWorldX[i];
            float worldY = projectedWorldY[i];
            // Z 轴：LayerIndex 越大越靠近相机，确保高层卡片视觉上覆盖低层。
            float worldZ = EntityWorldZ + logicalCard.LayerIndex * LayerHeight;
            int sortingOrder = SortingOrderBase + logicalCard.LayerIndex * 100 + (logicalCards.Count - i) + (maxLayer - logicalCard.LayerIndex);
            bool isBlocked = blockedStates[i];
            Color tintColor = isBlocked ? BlockedCardTintColor : visual.TintColor;

            EliminateCardEntityData entityData = new EliminateCardEntityData(
                logicalCard.LayoutIndex,
                visual.TypeId,
                new Vector3(worldX, worldY, worldZ),
                sortingOrder,
                visual.DisplaySprite,
                tintColor,
                isBlocked);

            spawnInstructions.Add(new EliminateCardSpawnInstruction(entityData, cellWidth, cellHeight));
        }

        // 缓存棋盘摆盘数据，供后续重算遮挡使用
        CacheBoardData(logicalCards, projectedWorldX, projectedWorldY, cellWidth, cellHeight);

        return spawnInstructions;
    }

    /// <summary>
    /// 缓存棋盘摆盘数据，供后续重算遮挡使用。
    /// </summary>
    private void CacheBoardData(
        List<EliminateCardLogicalCard> logicalCards,
        float[] projectedWorldX,
        float[] projectedWorldY,
        float cellWidth,
        float cellHeight)
    {
        _cachedLogicalCards = logicalCards;
        _cachedProjectedWorldX = projectedWorldX;
        _cachedProjectedWorldY = projectedWorldY;
        _cachedCellWidth = cellWidth;
        _cachedCellHeight = cellHeight;
        _removedFromBoard.Clear();
    }

    /// <summary>
    /// 固定视口正交相机尺寸。
    /// 不再根据实际卡片包围盒动态缩放，而是按固定 8x8 + DIR 行 9.5 的
    /// 逻辑跨度计算正交尺寸，确保所有关卡看到的视野完全一致。
    /// </summary>
    private void ApplyCameraFit(Camera worldCamera, List<EliminateCardSpawnInstruction> spawnInstructions)
    {
        if (worldCamera == null || spawnInstructions == null || spawnInstructions.Count <= 0 || !worldCamera.orthographic)
        {
            return;
        }

        // 取第一张卡的尺寸作为基准（所有卡尺寸一致）
        float cellWidth = spawnInstructions[0].CellWidth;
        float cellHeight = spawnInstructions[0].CellHeight;

        // 固定视口的世界尺寸
        float viewportWorldWidth = FixedViewportLogicalCols * cellWidth;
        float viewportWorldHeight = FixedViewportLogicalRows * cellHeight;

        // 正交尺寸 = 视口高度的一半
        float requiredByHeight = viewportWorldHeight * 0.5f + CameraPaddingWorldUnits;
        // 宽度约束：orthographicSize = (width / 2) / aspect
        float requiredByWidth = viewportWorldWidth * 0.5f / Mathf.Max(0.01f, worldCamera.aspect) + CameraPaddingWorldUnits;
        float requiredOrthoSize = Mathf.Max(requiredByHeight, requiredByWidth);

        worldCamera.orthographicSize = requiredOrthoSize;

        Vector3 baseCameraPosition = _hasCapturedCameraState ? _originalCameraPosition : worldCamera.transform.position;
        worldCamera.transform.position = new Vector3(
            baseCameraPosition.x + _previewCameraOnlyOffset.x,
            baseCameraPosition.y + _previewCameraOffsetY + _previewCameraOnlyOffset.y,
            baseCameraPosition.z);
    }

    /// <summary>
    /// 真正向 UGF 提交实体显示请求。
    /// </summary>
    private void SpawnCards(List<EliminateCardSpawnInstruction> spawnInstructions)
    {
        if (GameEntry.Entity == null || spawnInstructions == null || spawnInstructions.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < spawnInstructions.Count; i++)
        {
            int entityId = AcquireEntityId();
            if (entityId <= 0)
            {
                Log.Warning("EliminateCardController failed to acquire entity id for card preview.");
                continue;
            }

            _activeEntityIds.Add(entityId);
            GameEntry.Entity.ShowEntity<EliminateCardEntityLogic>(
                entityId,
                EntityDefine.EliminateCardEntity,
                EntityDefine.EliminateCardGroup,
                spawnInstructions[i].EntityData);
        }
    }

    /// <summary>
    /// 在卡片生成完成后，额外显示一个区域底图实体。
    /// 位置由 <see cref="EliminateTheAreaAnchor"/> 控制，
    /// 世界坐标换算复用与卡片完全相同的棋盘投影口径。
    /// </summary>
    /// <param name="worldCamera">当前主相机，用于计算棋盘世界中心。</param>
    /// <param name="spawnInstructions">已生成的卡片指令列表，用于提取 cellWidth/cellHeight。</param>
    private void ShowEliminateTheAreaEntity(Camera worldCamera, List<EliminateCardSpawnInstruction> spawnInstructions)
    {
        if (worldCamera == null || spawnInstructions == null || spawnInstructions.Count <= 0 || GameEntry.Entity == null)
        {
            return;
        }

        float cellWidth = spawnInstructions[0].CellWidth;
        float cellHeight = spawnInstructions[0].CellHeight;

        // 复用与卡片相同的棋盘投影口径
        float fixedCenterX = (FixedViewportLogicalCols - 1f) * 0.5f + (-1f);
        float fixedCenterY = -(FixedViewportLogicalRows - 1f) * 0.5f;
        Vector3 boardWorldCenter = GetPreviewBoardWorldCenter(worldCamera);

        // 锚点口径：(col, row)，row 正值向下，与 LegacyDirAnchors 一致
        // 逻辑 Y 翻转：logicalY = -row
        float logicalX = EliminateTheAreaAnchor.x;
        float logicalY = -EliminateTheAreaAnchor.y;

        float worldX = boardWorldCenter.x + (logicalX - fixedCenterX) * cellWidth;
        float worldY = boardWorldCenter.y + (logicalY - fixedCenterY) * cellHeight;

        int entityId = AcquireEntityId();
        if (entityId <= 0)
        {
            Log.Warning("EliminateCardController failed to acquire entity id for the area entity.");
            return;
        }

        _activeEntityIds.Add(entityId);

        EliminateTheAreaEntityData entityData = new EliminateTheAreaEntityData(
            new Vector3(worldX, worldY, EntityWorldZ),
            0);

        GameEntry.Entity.ShowEntity<EliminateTheAreaEntityLogic>(
            entityId,
            EntityDefine.EliminateTheAreaEntity,
            EntityDefine.EliminateCardGroup,
            entityData);
    }

    /// <summary>
    /// 获取一个新的实体 Id。
    /// 这里必须走全局 EntityIdPoolComponent，不能再本地自增：
    /// 1. HideEntityComplete 事件会统一回收到实体 Id 池；
    /// 2. 如果每日关卡自己造 800000xxx 这类临时 Id，
    ///    回收时就会因为“从未登记到池中”而刷一整屏 Warning；
    /// 3. 与场地实体统一口径后，申请 / 回收链路才能闭环。
    /// </summary>
    private int AcquireEntityId()
    {
        if (GameEntry.EntityIdPool == null)
        {
            Log.Warning("EliminateCardController 无法申请实体 Id，EntityIdPoolComponent 缺失。");
            return 0;
        }

        return GameEntry.EntityIdPool.Acquire();
    }

    /// <summary>
    /// 解析并缓存指定卡图。
    /// </summary>
    private Sprite TryLoadCardSprite(string spriteName)
    {
        string normalizedName = string.IsNullOrWhiteSpace(spriteName) || string.Equals(spriteName, "-1", StringComparison.Ordinal)
            ? DefaultCardSpriteName
            : spriteName.Trim();

        if (GameEntry.GameAssets != null
            && GameEntry.GameAssets.TryGetEliminateCardSprite(normalizedName, out Sprite cachedSprite)
            && cachedSprite != null)
        {
            return cachedSprite;
        }

        if (GameEntry.GameAssets != null
            && !string.Equals(normalizedName, DefaultCardSpriteName, StringComparison.OrdinalIgnoreCase)
            && GameEntry.GameAssets.TryGetEliminateCardSprite(DefaultCardSpriteName, out Sprite fallbackSprite)
            && fallbackSprite != null)
        {
            return fallbackSprite;
        }

        return null;
    }

    /// <summary>
    /// 解析当前主相机。
    /// 先拿 Camera.main，再按名字回退搜索。
    /// </summary>
    private Camera ResolveMainCamera()
    {
        if (_boundCamera != null && _boundCamera.isActiveAndEnabled)
        {
            return _boundCamera;
        }

        _boundCamera = Camera.main;
        if (_boundCamera != null && _boundCamera.isActiveAndEnabled)
        {
            return _boundCamera;
        }

        Camera[] cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera != null && camera.isActiveAndEnabled && string.Equals(camera.name, "Main Camera", StringComparison.Ordinal))
            {
                _boundCamera = camera;
                return _boundCamera;
            }
        }

        return null;
    }

    /// <summary>
    /// 缓存主相机原始尺寸。
    /// 页面生命周期内只缓存一次，防止“预览后尺寸”覆盖掉“原始尺寸”。
    /// </summary>
    private void CaptureCameraState(Camera worldCamera)
    {
        _boundCamera = worldCamera;
        if (_hasCapturedCameraState || worldCamera == null || !worldCamera.orthographic)
        {
            return;
        }

        _originalCameraPosition = worldCamera.transform.position;
        _originalOrthographicSize = worldCamera.orthographicSize;
        _hasCapturedCameraState = true;
    }

    /// <summary>
    /// 恢复主相机原始正交尺寸。
    /// </summary>
    private void RestoreCameraState()
    {
        if (_boundCamera != null && _hasCapturedCameraState)
        {
            if (_boundCamera.orthographic)
            {
                _boundCamera.orthographicSize = _originalOrthographicSize;
            }

            _boundCamera.transform.position = _originalCameraPosition;
        }

        _boundCamera = null;
        _hasCapturedCameraState = false;
    }

    /// <summary>
    /// 计算主区在逻辑平面上的真实水平中心。
    /// 这里只扫描主区卡片，不把 DIR 区算进去，
    /// 否则主区列数变窄时，两侧附属卡会把中心重新拉偏。
    /// </summary>
    /// <param name="logicalCards">当前所有逻辑卡片。</param>
    /// <param name="fallbackCenterX">当主区数据缺失时的兜底中心值。</param>
    /// <returns>主区真实逻辑中心 X。</returns>
    private static float GetMainAreaLogicalCenterX(List<EliminateCardLogicalCard> logicalCards, float fallbackCenterX)
    {
        if (logicalCards == null || logicalCards.Count <= 0)
        {
            return fallbackCenterX;
        }

        bool hasMainAreaCard = false;
        float minLogicalX = 0f;
        float maxLogicalX = 0f;

        for (int i = 0; i < logicalCards.Count; i++)
        {
            EliminateCardLogicalCard logicalCard = logicalCards[i];
            if (logicalCard.Area != EliminateCardArea.Main)
            {
                continue;
            }

            if (!hasMainAreaCard)
            {
                minLogicalX = logicalCard.LogicalX;
                maxLogicalX = logicalCard.LogicalX;
                hasMainAreaCard = true;
                continue;
            }

            if (logicalCard.LogicalX < minLogicalX)
            {
                minLogicalX = logicalCard.LogicalX;
            }

            if (logicalCard.LogicalX > maxLogicalX)
            {
                maxLogicalX = logicalCard.LogicalX;
            }
        }

        return hasMainAreaCard ? (minLogicalX + maxLogicalX) * 0.5f : fallbackCenterX;
    }

    /// <summary>
    /// 获取本次预览棋盘的世界中心。
    /// 摆盘必须基于“进入每日页前的原始相机位置”，
    /// 而不能直接拿当前相机实时坐标，否则当预览流程已经给相机做过偏移后，
    /// 下一次重建会把偏移当成新的原点，导致棋盘逐次漂移。
    /// </summary>
    /// <param name="worldCamera">当前主相机。</param>
    /// <returns>用于摆盘计算的世界中心点。</returns>
    private Vector3 GetPreviewBoardWorldCenter(Camera worldCamera)
    {
        Vector3 baseCameraPosition;
        if (_hasCapturedCameraState)
        {
            baseCameraPosition = _originalCameraPosition;
        }
        else if (worldCamera == null)
        {
            baseCameraPosition = Vector3.zero;
        }
        else
        {
            baseCameraPosition = worldCamera.transform.position;
        }

        // 关键口径：
        // 这个世界中心必须和最终相机偏移保持一致，
        // 否则当你手调 _previewCameraOffsetY 时，就会出现“相机走了，卡片没跟着走”的错位。
        return new Vector3(
            baseCameraPosition.x,
            baseCameraPosition.y + _previewCameraOffsetY,
            EntityWorldZ);
    }

    /// <summary>
    /// 计算主区为了“底边对齐到第 8 行”需要额外下移多少行。
    /// 口径说明：
    /// 1. 这里只看主区固定布局卡，不看 DIR 区；
    /// 2. 不按“用了几行”均分上下留白，而是直接让主区底边贴到目标底边；
    /// 3. 例如主区最大行索引只有 5，说明当前只铺到第 6 行，需要整体下移 2 行。
    /// </summary>
    /// <param name="logicalCards">当前所有逻辑卡片。</param>
    /// <returns>主区应额外下移的整行数量。</returns>
    private static float GetMainAreaBottomAlignShiftRows(List<EliminateCardLogicalCard> logicalCards)
    {
        if (logicalCards == null || logicalCards.Count <= 0)
        {
            return 0f;
        }

        bool hasMainAreaCard = false;
        int maxMainGridRowIndex = 0;

        for (int i = 0; i < logicalCards.Count; i++)
        {
            EliminateCardLogicalCard logicalCard = logicalCards[i];
            if (logicalCard.Area != EliminateCardArea.Main)
            {
                continue;
            }

            if (!hasMainAreaCard)
            {
                maxMainGridRowIndex = logicalCard.GridRowIndex;
                hasMainAreaCard = true;
                continue;
            }

            if (logicalCard.GridRowIndex > maxMainGridRowIndex)
            {
                maxMainGridRowIndex = logicalCard.GridRowIndex;
            }
        }

        if (!hasMainAreaCard)
        {
            return 0f;
        }

        int bottomAlignedTargetRowIndex = MainAreaTargetRowCount - 1;
        int shiftRows = bottomAlignedTargetRowIndex - maxMainGridRowIndex;
        return Mathf.Max(0, shiftRows);
    }

    /// <summary>
    /// 根据最终世界落点与层级关系，计算哪些卡片当前处于“被遮挡”状态。
    /// 规则直接对齐参考项目 xinpgdd：
    /// 1. 更高层卡片会遮挡更低层卡片；
    /// 2. 若两张卡在平面投影上发生重叠（X/Y 差值均小于一张卡的宽高），
    ///    则低层卡判定为被压住；
    /// 3. 当前每日预览是静态棋盘，因此这里只做一次性分析并把结果烘到 TintColor，
    ///    不引入正式玩法那套 blockedBy / Byblocked 运行时链路。
    /// </summary>
    /// <param name="logicalCards">当前所有逻辑卡片。</param>
    /// <param name="projectedWorldX">卡片投影后的世界 X。</param>
    /// <param name="projectedWorldY">卡片投影后的世界 Y。</param>
    /// <param name="cellWidth">单张卡片宽度。</param>
    /// <param name="cellHeight">单张卡片高度。</param>
    /// <returns>每张卡片是否被上层遮挡的布尔表。</returns>
    private static bool[] CalculateBlockedStates(
        List<EliminateCardLogicalCard> logicalCards,
        float[] projectedWorldX,
        float[] projectedWorldY,
        float cellWidth,
        float cellHeight)
    {
        int cardCount = logicalCards != null ? logicalCards.Count : 0;
        bool[] blockedStates = new bool[cardCount];
        if (cardCount <= 1
            || projectedWorldX == null
            || projectedWorldY == null
            || projectedWorldX.Length < cardCount
            || projectedWorldY.Length < cardCount)
        {
            return blockedStates;
        }

        for (int upperCardIndex = 0; upperCardIndex < cardCount; upperCardIndex++)
        {
            EliminateCardLogicalCard upperCard = logicalCards[upperCardIndex];

            for (int lowerCardIndex = 0; lowerCardIndex < cardCount; lowerCardIndex++)
            {
                if (upperCardIndex == lowerCardIndex || blockedStates[lowerCardIndex])
                {
                    continue;
                }

                EliminateCardLogicalCard lowerCard = logicalCards[lowerCardIndex];
                if (upperCard.LayerIndex <= lowerCard.LayerIndex)
                {
                    continue;
                }

                float deltaX = Mathf.Abs(projectedWorldX[upperCardIndex] - projectedWorldX[lowerCardIndex]);
                float deltaY = Mathf.Abs(projectedWorldY[upperCardIndex] - projectedWorldY[lowerCardIndex]);
                if (deltaX + BlockingOverlapTolerance < cellWidth
                    && deltaY + BlockingOverlapTolerance < cellHeight)
                {
                    blockedStates[lowerCardIndex] = true;
                }
            }
        }

        return blockedStates;
    }

    // ───────────── 卡片/区域注册 ─────────────

    /// <summary>
    /// 注册卡片实体逻辑引用。
    /// 由 EliminateCardEntityLogic.OnShow 自动调用。
    /// </summary>
    /// <param name="entityId">卡片实体 Id。</param>
    /// <param name="logic">卡片实体逻辑实例。</param>
    public void RegisterCardLogic(int entityId, EliminateCardEntityLogic logic)
    {
        if (entityId <= 0 || logic == null)
        {
            return;
        }

        if (_cardLogics.TryGetValue(entityId, out EliminateCardEntityLogic previousLogic) && previousLogic != null)
        {
            RemoveLayoutIndexLookup(previousLogic);
        }

        if (_cardLogicsByLayoutIndex.TryGetValue(logic.LayoutIndex, out EliminateCardEntityLogic existedLogic)
            && existedLogic != null
            && !ReferenceEquals(existedLogic, logic))
        {
            Log.Warning(
                "EliminateCardController 检测到重复的布局索引注册：layoutIndex={0}，旧实体={1}，新实体={2}。",
                logic.LayoutIndex,
                existedLogic.Entity.Id,
                logic.Entity.Id);
        }

        _cardLogics[entityId] = logic;
        _cardLogicsByLayoutIndex[logic.LayoutIndex] = logic;
    }

    /// <summary>
    /// 反注册卡片实体逻辑引用。
    /// 由 EliminateCardEntityLogic.OnHide 自动调用。
    /// </summary>
    /// <param name="entityId">卡片实体 Id。</param>
    public void UnregisterCardLogic(int entityId)
    {
        if (entityId <= 0)
        {
            return;
        }

        if (_cardLogics.TryGetValue(entityId, out EliminateCardEntityLogic logic) && logic != null)
        {
            RemoveLayoutIndexLookup(logic);
        }

        _cardLogics.Remove(entityId);
    }

    /// <summary>
    /// 注册区域实体逻辑引用。
    /// 由 EliminateTheAreaEntityLogic.OnShow 自动调用。
    /// </summary>
    /// <param name="areaLogic">区域实体逻辑实例。</param>
    public void RegisterAreaEntityLogic(EliminateTheAreaEntityLogic areaLogic)
    {
        _areaEntityLogic = areaLogic;
        _hasFailed = false;

        // 注入满格结算回调（胜利/继续逻辑）
        if (areaLogic != null)
        {
            areaLogic.OnSettlementScoreCalculation = OnSettlementScoreCalculationHandler;
            areaLogic.OnSettlementCleared = OnSettlementClearedHandler;
            areaLogic.OnSettlementFailed = OnSettlementFailedHandler;
            areaLogic.OnWaitingAreaLayoutChanged = RefreshWaitingAreaScoreDisplay;
            SyncComboDisplay();
        }
    }

    /// <summary>
    /// 反注册区域实体逻辑引用。
    /// 由 EliminateTheAreaEntityLogic.OnHide 自动调用。
    /// </summary>
    public void UnregisterAreaEntityLogic()
    {
        // 清理回调引用，避免区域实体被回收后仍持有无效委托
        if (_areaEntityLogic != null)
        {
            _areaEntityLogic.HideComboDisplay();
            _areaEntityLogic.OnSettlementScoreCalculation = null;
            _areaEntityLogic.OnSettlementCleared = null;
            _areaEntityLogic.OnSettlementFailed = null;
            _areaEntityLogic.OnWaitingAreaLayoutChanged = null;
        }

        _areaEntityLogic = null;
    }

    // ───────────── 点击处理 ─────────────

    /// <summary>
    /// 卡片点击回调。
    /// 由 EliminateCardEntityLogic.OnPointerClick 通过 OnClickCallback 触发。
    /// </summary>
    /// <param name="card">被点击的卡片实体逻辑。</param>
    public void HandleCardClick(EliminateCardEntityLogic card)
    {
        if (card == null || _areaEntityLogic == null)
        {
            return;
        }

        // ── 置出区卡片分支：点击后飞回等待区 ──
        if (card.CurrentArea == CardArea.OutputZone)
        {
            HandleOutputZoneCardClick(card);
            return;
        }

        // ── 拿取状态分支：卡片直接飞向置出区 ──
        if (_isTakeState)
        {
            HandleTakeStateCardClick(card);
            return;
        }

        // ── 正常模式：尝试插入等待区 ──
        if (!_areaEntityLogic.TryRequestInsert(card))
        {
            return;
        }

        // 插入成功后，该卡片已经在 TryRequestInsert 中切到 WaitingArea，
        // Collider 会立即停用，不再继续占着棋盘位置吃点击。
        // 这里再把它加入“离开棋盘”集合，确保后续遮挡重算也不再把它当作遮挡源。
        MarkCardRemovedFromBoard(card, "NormalInsert");
        UpdateBlockingAfterRemoval();

        // ⚠️ 避坑：分数显示刷新不在此时调用！
        // TryRequestInsert 仅入队操作，快照尚未重建，_snapshotCards 仍为旧值。
        // 分数刷新由 OnWaitingAreaLayoutChanged 回调在 ExecuteInsertOp 快照重建后触发。
    }

    // ───────────── 遮挡重算 ─────────────

    /// <summary>
    /// 卡片入等待区后重算遮挡状态。
    /// 复用缓存的棋盘摆盘数据，跳过已移除卡片，重新计算哪些卡片被遮挡。
    /// </summary>
    private void UpdateBlockingAfterRemoval()
    {
        if (_cachedLogicalCards == null || _cachedProjectedWorldX == null || _cachedProjectedWorldY == null)
        {
            Log.Warning(
                "EliminateCardController 无法重算遮挡：棋盘缓存缺失。removedCount={0}, activeCardLogicCount={1}",
                _removedFromBoard.Count,
                _cardLogics.Count);
            return;
        }

        int cardCount = _cachedLogicalCards.Count;

        // 构建过滤后的逻辑卡片列表和坐标数组（跳过已移除卡片）
        // 为了避免每帧分配，直接在原数组上重算遮挡，然后跳过已移除卡片的遮挡贡献
        bool[] blockedStates = CalculateBlockedStatesWithRemoval(
            _cachedLogicalCards,
            _cachedProjectedWorldX,
            _cachedProjectedWorldY,
            _cachedCellWidth,
            _cachedCellHeight,
            _removedFromBoard);

        // 更新每张卡片的遮挡状态
        for (int i = 0; i < cardCount; i++)
        {
            int layoutIndex = _cachedLogicalCards[i].LayoutIndex;
            // 已移除的卡片不需要更新
            if (_removedFromBoard.Contains(layoutIndex))
            {
                continue;
            }

            // 通过布局索引查找对应的卡片逻辑
            EliminateCardEntityLogic cardLogic = FindCardLogicByLayoutIndex(layoutIndex);
            if (cardLogic == null)
            {
                Log.Warning(
                    "EliminateCardController 重算遮挡时找不到布局索引 {0} 对应的卡片实体，blocked={1}。",
                    layoutIndex,
                    blockedStates[i]);
                continue;
            }

            cardLogic.SetBlocked(blockedStates[i]);
            if (ShouldDebugSideDirLayout(layoutIndex))
            {
                Log.Debug(
                    "EliminateCardController UpdateBlockingAfterRemoval: layout={0}, blocked={1}, area={2}, moving={3}, colliderEnabled={4}, entityId={5}",
                    layoutIndex,
                    blockedStates[i],
                    cardLogic.CurrentArea,
                    cardLogic.IsMoving,
                    cardLogic.IsRaycastColliderEnabled,
                    cardLogic.Entity.Id);
            }
        }
    }

    /// <summary>
    /// 计算遮挡状态（考虑已移除卡片不再遮挡其他卡）。
    /// 逻辑与 CalculateBlockedStates 一致，但已移除卡片不作为遮挡源。
    /// </summary>
    private static bool[] CalculateBlockedStatesWithRemoval(
        List<EliminateCardLogicalCard> logicalCards,
        float[] projectedWorldX,
        float[] projectedWorldY,
        float cellWidth,
        float cellHeight,
        HashSet<int> removedFromBoard)
    {
        int cardCount = logicalCards != null ? logicalCards.Count : 0;
        bool[] blockedStates = new bool[cardCount];
        if (cardCount <= 1 || projectedWorldX == null || projectedWorldY == null)
        {
            return blockedStates;
        }

        for (int upperCardIndex = 0; upperCardIndex < cardCount; upperCardIndex++)
        {
            // 已移除的卡片不再遮挡其他卡
            if (removedFromBoard != null && removedFromBoard.Contains(logicalCards[upperCardIndex].LayoutIndex))
            {
                continue;
            }

            EliminateCardLogicalCard upperCard = logicalCards[upperCardIndex];

            for (int lowerCardIndex = 0; lowerCardIndex < cardCount; lowerCardIndex++)
            {
                if (upperCardIndex == lowerCardIndex || blockedStates[lowerCardIndex])
                {
                    continue;
                }

                // 已移除的卡片也不需要被标记为遮挡
                if (removedFromBoard != null && removedFromBoard.Contains(logicalCards[lowerCardIndex].LayoutIndex))
                {
                    continue;
                }

                EliminateCardLogicalCard lowerCard = logicalCards[lowerCardIndex];
                if (upperCard.LayerIndex <= lowerCard.LayerIndex)
                {
                    continue;
                }

                float deltaX = Mathf.Abs(projectedWorldX[upperCardIndex] - projectedWorldX[lowerCardIndex]);
                float deltaY = Mathf.Abs(projectedWorldY[upperCardIndex] - projectedWorldY[lowerCardIndex]);
                if (deltaX + BlockingOverlapTolerance < cellWidth
                    && deltaY + BlockingOverlapTolerance < cellHeight)
                {
                    blockedStates[lowerCardIndex] = true;
                }
            }
        }

        return blockedStates;
    }

    /// <summary>
    /// 通过布局索引查找卡片实体逻辑。
    /// </summary>
    /// <param name="layoutIndex">布局索引。</param>
    /// <returns>对应的卡片实体逻辑；未找到时返回 null。</returns>
    private EliminateCardEntityLogic FindCardLogicByLayoutIndex(int layoutIndex)
    {
        if (_cardLogicsByLayoutIndex.TryGetValue(layoutIndex, out EliminateCardEntityLogic logic))
        {
            return logic;
        }

        return null;
    }

    /// <summary>
    /// 把一张棋盘卡标记为“已离开棋盘”。
    /// 之后它不再作为遮挡源参与棋盘重算。
    /// </summary>
    /// <param name="card">目标卡片。</param>
    /// <param name="reason">触发原因，仅用于调试日志。</param>
    private void MarkCardRemovedFromBoard(EliminateCardEntityLogic card, string reason)
    {
        if (card == null)
        {
            return;
        }

        bool added = _removedFromBoard.Add(card.LayoutIndex);
        if (ShouldDebugSideDirLayout(card.LayoutIndex))
        {
            Log.Debug(
                "EliminateCardController MarkCardRemovedFromBoard: layout={0}, added={1}, reason={2}, area={3}, moving={4}, colliderEnabled={5}, entityId={6}",
                card.LayoutIndex,
                added,
                reason,
                card.CurrentArea,
                card.IsMoving,
                card.IsRaycastColliderEnabled,
                card.Entity.Id);
        }
    }

    /// <summary>
    /// 从布局索引直索引缓存中移除一张卡片逻辑。
    /// 仅当当前映射确实指向这张卡时才删除，避免误删新复用对象。
    /// </summary>
    /// <param name="logic">目标卡片逻辑。</param>
    private void RemoveLayoutIndexLookup(EliminateCardEntityLogic logic)
    {
        if (logic == null)
        {
            return;
        }

        if (_cardLogicsByLayoutIndex.TryGetValue(logic.LayoutIndex, out EliminateCardEntityLogic mappedLogic)
            && ReferenceEquals(mappedLogic, logic))
        {
            _cardLogicsByLayoutIndex.Remove(logic.LayoutIndex);
        }
    }

    /// <summary>
    /// 是否属于 bbl1 侧边 DIR 单阻挡诊断卡。
    /// </summary>
    /// <param name="layoutIndex">布局索引。</param>
    /// <returns>true=需要打定向调试日志。</returns>
    private static bool ShouldDebugSideDirLayout(int layoutIndex)
    {
        return layoutIndex == DebugLeftDirLowerLayoutIndex
            || layoutIndex == DebugLeftDirUpperLayoutIndex
            || layoutIndex == DebugRightDirLowerLayoutIndex
            || layoutIndex == DebugRightDirUpperLayoutIndex;
    }

    // ───────────── 道具操作 ─────────────

    /// <summary>
    /// 当前是否处于拿取状态。
    /// </summary>
    public bool IsTakeState => _isTakeState;

    /// <summary>
    /// 统计棋盘上未遮挡、未入等待区、未被移除的卡片数量。
    /// 供拿取道具判断是否可用。
    /// </summary>
    /// <returns>未遮挡的棋盘卡数量。</returns>
    public int GetUnblockedBoardCardCount()
    {
        int count = 0;
        foreach (var kvp in _cardLogics)
        {
            EliminateCardEntityLogic card = kvp.Value;
            if (card != null
                && card.CurrentArea == CardArea.Board
                && !card.IsBlocked
                && !_removedFromBoard.Contains(card.LayoutIndex))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 移出道具：将等待区前3张卡片移出。
    /// 卡片飞向置出区后继续存活显示（不回收），加入 _outputZoneCards。
    /// 全部飞完后执行前移补位动画。
    /// </summary>
    /// <returns>true=成功取出卡片并开始飞行动画；false=等待区为空。</returns>
    public bool PropShiftOut()
    {
        if (_areaEntityLogic == null || _areaEntityLogic.CurrentCardCount <= 0)
        {
            return false;
        }

        // 仅从数据结构取出卡片，不做动画/回收
        List<EliminateCardEntityLogic> detachedCards = _areaEntityLogic.DetachCardsFromWaitingArea(3);
        if (detachedCards == null || detachedCards.Count <= 0)
        {
            return false;
        }

        // 飞行中卡片计数器，用于判断何时全部飞完
        int flyingCount = detachedCards.Count;

        for (int i = 0; i < detachedCards.Count; i++)
        {
            EliminateCardEntityLogic card = detachedCards[i];
            if (card == null)
            {
                flyingCount--;
                continue;
            }

            // 退出等待区状态，即将飞向置出区
            card.SetCardArea(CardArea.OutputZone);

            // 标记卡片移动中，防止快速连点
            card.SetMoving(true);

            // 计算该卡片在置出区的目标位置（带层叠偏移）
            int outputIndex = _outputZoneCards.Count;
            Vector3 targetPos = GetOutputZoneWorldPosition(outputIndex);

            // 卡片飞向置出区，到达后标记为置出区卡片
            card.CachedTransform.DOKill(false);
            EliminateCardEntityLogic capturedCard = card;
            card.CachedTransform
                .DOMove(targetPos, PropFlyToOutputDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    capturedCard.SetMoving(false);
                    // 后进入的卡片 sortingOrder 更大，渲染在上方
                    capturedCard.SetSortingOrder(1000 + outputIndex);

                    // 递减飞行计数，全部飞完后前移补位
                    flyingCount--;
                    if (flyingCount <= 0)
                    {
                        _areaEntityLogic.CompactAfterDetach(PropCompactMoveDuration);
                    }
                });

            // 立即加入置出区列表（用于后续卡片计算 outputIndex）
            _outputZoneCards.Add(card);
        }

        return true;
    }

    /// <summary>
    /// 拿取道具：进入拿取状态。
    /// 拿取状态下，玩家点击棋盘卡片直接回收（不入等待区），最多拿取 maxTakeCount 张。
    /// </summary>
    /// <returns>true=成功进入拿取状态；false=棋盘无未遮挡卡。</returns>
    public bool PropEnterTakeState()
    {
        int availableCards = GetUnblockedBoardCardCount();
        if (availableCards <= 0)
        {
            return false;
        }

        _isTakeState = true;
        _takenCount = 0;
        // 最多拿取3张，不超过可用卡数
        _maxTakeCount = Mathf.Min(3, availableCards);

        OnTakeStateChanged?.Invoke(true);
        return true;
    }

    /// <summary>
    /// 退出拿取状态。
    /// 由 CombatUIForm 在拿取完成或取消时调用。
    /// </summary>
    public void ExitTakeState()
    {
        if (!_isTakeState)
        {
            return;
        }

        _isTakeState = false;
        _takenCount = 0;
        _maxTakeCount = 0;

        OnTakeStateChanged?.Invoke(false);
    }

    /// <summary>
    /// 随机道具：打乱棋盘上所有未遮挡、未入等待区、未被移除的卡片的 TypeId 和 Sprite。
    /// 使用 Fisher-Yates 洗牌算法，口径与 ShuffleAssignedTypeVisuals 一致。
    /// </summary>
    /// <returns>true=成功打乱；false=棋盘无可用卡。</returns>
    public bool PropShuffle()
    {
        // 收集所有可打乱的卡片
        List<EliminateCardEntityLogic> shuffleableCards = new List<EliminateCardEntityLogic>();
        foreach (var kvp in _cardLogics)
        {
            EliminateCardEntityLogic card = kvp.Value;
            if (card != null
                && card.CurrentArea == CardArea.Board
                && !_removedFromBoard.Contains(card.LayoutIndex))
            {
                shuffleableCards.Add(card);
            }
        }

        if (shuffleableCards.Count <= 1)
        {
            return false;
        }

        // 收集当前 TypeId 列表和对应的 Sprite 列表
        int cardCount = shuffleableCards.Count;
        List<int> typeIds = new List<int>(cardCount);
        List<Sprite> sprites = new List<Sprite>(cardCount);

        for (int i = 0; i < cardCount; i++)
        {
            typeIds.Add(shuffleableCards[i].TypeId);
            // ⚠️ 避坑：Sprite 需从 _spriteRenderer 取，TypeId 可直接读属性
            SpriteRenderer sr = shuffleableCards[i].GetComponentInChildren<SpriteRenderer>(true);
            sprites.Add(sr != null ? sr.sprite : null);
        }

        // Fisher-Yates 洗牌：同时打乱 typeIds 和 sprites，保持两者索引对齐
        for (int i = 0; i < cardCount; i++)
        {
            int randomIndex = UnityEngine.Random.Range(i, cardCount);
            // 交换 TypeId
            int tempType = typeIds[i];
            typeIds[i] = typeIds[randomIndex];
            typeIds[randomIndex] = tempType;
            // 交换 Sprite
            Sprite tempSprite = sprites[i];
            sprites[i] = sprites[randomIndex];
            sprites[randomIndex] = tempSprite;
        }

        // 将打乱后的 TypeId + Sprite 重新赋值给卡片
        for (int i = 0; i < cardCount; i++)
        {
            shuffleableCards[i].SetTypeIdAndSprite(typeIds[i], sprites[i]);
        }

        return true;
    }

    // ───────────── 拿取状态内部处理 ─────────────

    /// <summary>
    /// 拿取状态下点击卡片的处理逻辑。
    /// 卡片飞向置出区后继续存活显示（不回收），加入 _outputZoneCards。
    /// 每次拿取后重算遮挡，拿满后自动退出拿取状态。
    /// </summary>
    /// <param name="card">被点击的卡片实体逻辑。</param>
    private void HandleTakeStateCardClick(EliminateCardEntityLogic card)
    {
        // 被遮挡的卡片在拿取状态下也不可点击
        if (card.IsBlocked)
        {
            return;
        }

        // 标记卡片移动中，防止快速连点
        card.SetMoving(true);

        // 标记进入置出区，防止飞行中被再次点击
        card.SetCardArea(CardArea.OutputZone);

        // 标记卡片已从棋盘移除
        MarkCardRemovedFromBoard(card, "TakeState");

        // 计算该卡片在置出区的目标位置（带层叠偏移）
        int outputIndex = _outputZoneCards.Count;
        Vector3 targetPos = GetOutputZoneWorldPosition(outputIndex);

        // 卡片飞向置出区，到达后标记为置出区卡片
        card.CachedTransform.DOKill(false);
        EliminateCardEntityLogic capturedCard = card;
        card.CachedTransform
            .DOMove(targetPos, PropFlyToOutputDuration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true)
            .OnComplete(() =>
            {
                capturedCard.SetMoving(false);
                // 后进入的卡片 sortingOrder 更大，渲染在上方
                capturedCard.SetSortingOrder(1000 + outputIndex);
            });

        // 立即加入置出区列表（用于后续卡片计算 outputIndex）
        _outputZoneCards.Add(card);

        // 重算遮挡
        UpdateBlockingAfterRemoval();

        // 递增拿取计数
        _takenCount++;

        // 拿满后自动退出拿取状态
        if (_takenCount >= _maxTakeCount)
        {
            ExitTakeState();
        }
    }

    // ───────────── 置出区卡片点击处理 ─────────────

    /// <summary>
    /// 置出区卡片点击处理：卡片飞回等待区。
    /// 清除置出区标记 → 从 _outputZoneCards 移除 → 走正常 TryRequestInsert 流程。
    /// </summary>
    /// <param name="card">被点击的置出区卡片。</param>
    private void HandleOutputZoneCardClick(EliminateCardEntityLogic card)
    {
        // 等待区已满则无法放回
        if (_areaEntityLogic.CurrentCardCount >= _areaEntityLogic.MaxCardCount)
        {
            return;
        }

        // 清除置出区标记，恢复为棋盘状态
        card.SetCardArea(CardArea.Board);
        card.SetBlocked(false);

        // 从置出区列表移除
        _outputZoneCards.Remove(card);

        // 走正常插入等待区流程（自动 SetCardArea(WaitingArea) + 飞行动画）
        if (!_areaEntityLogic.TryRequestInsert(card))
        {
            // 插入失败（极端情况：刚满），恢复置出区标记
            card.SetCardArea(CardArea.OutputZone);
            _outputZoneCards.Add(card);
        }
    }

    /// <summary>
    /// 计算置出区中指定索引卡片的世界坐标。
    /// 基础位置由 PropOutputZoneAnchor 逻辑坐标投影得到，
    /// 每行 OutputZoneCardsPerRow 张，第4张回到第1张的 X 位置。
    /// Z 轴递增，确保 BoxCollider2D 不重叠、可单独点击。
    /// ⚠️ 避坑：必须在 RebuildPreview 之后调用，否则 _cachedCellWidth/Height 为 0。
    /// </summary>
    /// <param name="outputIndex">卡片在置出区中的索引（0-based）。</param>
    /// <returns>置出区目标世界坐标。</returns>
    private Vector3 GetOutputZoneWorldPosition(int outputIndex)
    {
        // 先计算基础锚点位置
        Camera worldCamera = Camera.main;
        Vector3 boardWorldCenter = GetPreviewBoardWorldCenter(worldCamera);

        // 口径与 SpawnAreaEntity 一致
        float fixedCenterX = (FixedViewportLogicalCols - 1f) * 0.5f + (-1f);
        float fixedCenterY = -(FixedViewportLogicalRows - 1f) * 0.5f;

        // 锚点口径：(col, row)，row 正值向下，逻辑 Y 翻转：logicalY = -row
        float logicalX = PropOutputZoneAnchor.x;
        float logicalY = -PropOutputZoneAnchor.y;

        float baseWorldX = boardWorldCenter.x + (logicalX - fixedCenterX) * _cachedCellWidth;
        float baseWorldY = boardWorldCenter.y + (logicalY - fixedCenterY) * _cachedCellHeight;

        // 行内偏移：每 OutputZoneCardsPerRow 张换行，回到起始 X
        int indexInRow = outputIndex % OutputZoneCardsPerRow;
        float xOffset = indexInRow * OutputZoneXOffset;

        // Z 轴递减：后进入的卡片 Z 更小（离相机更近），
        // 确保 Physics2D.Raycast 命中视觉上层的卡片，与 sortingOrder 一致。
        // ⚠️ 避坑：Z 递增会导致 Raycast 命中底层卡片而非上层！
        float zOffset = -outputIndex * OutputZoneZOffset;

        return new Vector3(baseWorldX + xOffset, baseWorldY, EntityWorldZ + zOffset);
    }

    // ───────────── 得分与连击：公开接口 ─────────────

    /// <summary>
    /// 获取当前累计得分。
    /// </summary>
    /// <returns>当前累计得分。</returns>
    public int GetCurrentScore() => _currentScore;

    /// <summary>
    /// 获取当前连击计数。
    /// </summary>
    /// <returns>当前连击数。</returns>
    public int GetComboCount() => _comboCount;

    /// <summary>
    /// 将当前连击状态同步到区域实体的 Combo UI。
    /// 当连击数小于 1 或剩余时间耗尽时，直接隐藏 Combo；
    /// 否则调用区域实体的 RefreshComboDisplay 刷新文本、滑条与动画。
    /// </summary>
    private void SyncComboDisplay()
    {
        if (_areaEntityLogic == null)
        {
            return;
        }

        float comboWindow = GetComboWindowSeconds();
        float remainingWindow = GetComboRemainingSeconds(comboWindow);
        if (_comboCount < 1 || remainingWindow <= 0f)
        {
            _areaEntityLogic.HideComboDisplay();
            return;
        }

        _areaEntityLogic.RefreshComboDisplay(_comboCount, comboWindow, remainingWindow);
    }

    /// <summary>
    /// 计算当前连击窗口的剩余秒数。
    /// 基于上一次满格清空时刻与连击窗口总时长，算出还剩多少时间。
    /// </summary>
    /// <param name="comboWindow">连击窗口总时长（秒）。</param>
    /// <returns>剩余秒数；若连击数不足或窗口非法则返回 0。</returns>
    private float GetComboRemainingSeconds(float comboWindow)
    {
        if (_comboCount < 1 || comboWindow <= 0f || _lastSettlementRealTime < 0f)
        {
            return 0f;
        }

        float remaining = comboWindow - (Time.unscaledTime - _lastSettlementRealTime);
        return remaining > 0f ? remaining : 0f;
    }

    // ───────────── 等待区分数显示 ─────────────

    /// <summary>
    /// 刷新等待区每个槽位的分数显示。
    /// 计算当前单卡总分，然后通知 EliminateTheAreaEntityLogic 更新每个槽位的渲染器。
    /// </summary>
    private void RefreshWaitingAreaScoreDisplay()
    {
        if (_areaEntityLogic == null)
        {
            return;
        }

        int perCardScore = CalculateCurrentPerCardScore();
        _areaEntityLogic.RefreshScoreDisplay(perCardScore);

        // ── 同步刷新轮数/基础分信息文本 ──
        DailyChallengeScoreDataRow config = GetScoreConfig();
        int baseScore = config != null
            ? config.GetBaseScorePerRound(_currentRound)
            : DefaultBaseScorePerCard;
        _areaEntityLogic.RefreshRoundInfoDisplay(_currentRound, baseScore);
    }

    /// <summary>
    /// 计算当前等待区的单卡总分（不乘卡片总数）。
    /// 与 CalculateSettlementScore 的前半段逻辑一致，但只返回 perCardScore。
    /// 用于实时显示每个槽位卡牌的分数。
    /// </summary>
    /// <returns>单卡总分；无可计分组合时返回 0。</returns>
    private int CalculateCurrentPerCardScore()
    {
        if (_areaEntityLogic == null)
        {
            return 0;
        }

        // 统计等待区中各 TypeId 出现次数
        Dictionary<int, int> typeCounts = new Dictionary<int, int>(8);
        foreach (var kvp in _cardLogics)
        {
            EliminateCardEntityLogic card = kvp.Value;
            if (card == null || card.CurrentArea != CardArea.WaitingArea)
            {
                continue;
            }

            int typeId = card.TypeId;
            if (!typeCounts.ContainsKey(typeId))
            {
                typeCounts[typeId] = 0;
            }
            typeCounts[typeId]++;
        }

        // 计算分量分叠加
        DailyChallengeScoreDataRow config = GetScoreConfig();
        int componentStack = 0;
        bool hasScoringCombo = false;
        foreach (var pair in typeCounts)
        {
            int sameTypeCount = pair.Value;
            if (sameTypeCount <= 1)
            {
                continue;
            }

            hasScoringCombo = true;
            int componentScore = config != null
                ? config.GetComponentScore(sameTypeCount)
                : GetDefaultComponentScore(sameTypeCount);
            componentStack += Mathf.Max(0, componentScore);
        }

        // 无可计分组合时返回 0
        if (!hasScoringCombo)
        {
            return 0;
        }

        // 每轮叠加一次基础分（随轮次递增）
        int baseOnce = config != null
            ? config.GetBaseScorePerRound(_currentRound)
            : DefaultBaseScorePerCard;

        // 单卡总分 = componentStack + baseOnce
        return Mathf.Max(0, componentStack + baseOnce);
    }

    // ───────────── 得分与连击：内部计算 ─────────────

    /// <summary>
    /// 计算本次满格清空的得分。
    /// 对齐参考项目 xinpgdd 的计分模型：
    /// 1. 统计等待区中各 TypeId 的出现次数；
    /// 2. 同类型 ≥2 张时，计算该类型的分量分（GetComponentScore）；
    /// 3. 单牌类型（=1 张）不参与叠加分；
    /// 4. 所有同类型≥2 的分量分累加 = componentStack；
    /// 5. 每轮叠加一次基础分 = baseOnce（随轮次递增）；
    /// 6. 单卡总分 = componentStack + baseOnce；
    /// 7. 本轮得分 = 单卡总分 × 等待区卡片总数。
    /// </summary>
    /// <returns>本轮清空得分。</returns>
    private int CalculateSettlementScore()
    {
        if (_areaEntityLogic == null)
        {
            return 0;
        }

        // 统计等待区中各 TypeId 出现次数
        // ⚠️ 避坑：此方法在 OnSettlementScoreCalculation 回调中调用，
        // 此时卡片尚未被回收，CardArea=WaitingArea，TypeId 可读。
        Dictionary<int, int> typeCounts = new Dictionary<int, int>(8);
        int scoredCardCount = 0;
        foreach (var kvp in _cardLogics)
        {
            EliminateCardEntityLogic card = kvp.Value;
            if (card == null || card.CurrentArea != CardArea.WaitingArea)
            {
                continue;
            }

            scoredCardCount++;
            int typeId = card.TypeId;
            if (!typeCounts.ContainsKey(typeId))
            {
                typeCounts[typeId] = 0;
            }
            typeCounts[typeId]++;
        }

        if (scoredCardCount <= 0)
        {
            return 0;
        }

        // 计算分量分叠加
        DailyChallengeScoreDataRow config = GetScoreConfig();
        int componentStack = 0;
        bool hasScoringCombo = false;
        foreach (var pair in typeCounts)
        {
            int sameTypeCount = pair.Value;
            if (sameTypeCount <= 1)
            {
                // 单牌类型不参与叠加分
                continue;
            }

            hasScoringCombo = true;
            int componentScore = config != null
                ? config.GetComponentScore(sameTypeCount)
                : GetDefaultComponentScore(sameTypeCount);
            componentStack += Mathf.Max(0, componentScore);
        }

        // 无可计分组合时返回 0
        if (!hasScoringCombo)
        {
            return 0;
        }

        // 每轮叠加一次基础分（随轮次递增）
        int baseOnce = config != null
            ? config.GetBaseScorePerRound(_currentRound)
            : DefaultBaseScorePerCard;

        // 单卡总分 = componentStack + baseOnce
        int perCardScore = Mathf.Max(0, componentStack + baseOnce);

        // 本轮得分 = 单卡总分 × 卡片总数
        return Mathf.Max(0, perCardScore * scoredCardCount);
    }

    /// <summary>
    /// 获取得分配置行。
    /// 优先从数据表缓存读取，不可用时返回 null。
    /// </summary>
    /// <returns>得分配置行；不可用时返回 null。</returns>
    private DailyChallengeScoreDataRow GetScoreConfig()
    {
        // 优先使用缓存
        if (_scoreConfig != null)
        {
            return _scoreConfig;
        }

        // 尝试从数据表读取
        if (GameEntry.DataTables == null || !GameEntry.DataTables.IsAvailable<DailyChallengeScoreDataRow>())
        {
            return null;
        }

        DailyChallengeScoreDataRow[] rows = GameEntry.DataTables.GetAllDataRows<DailyChallengeScoreDataRow>();
        if (rows != null && rows.Length > 0)
        {
            _scoreConfig = rows[0];
            return _scoreConfig;
        }

        return null;
    }

    /// <summary>
    /// 数据表不可用时，使用硬编码默认值计算分量分。
    /// 对齐 xinpgdd：1张返回0，2张返回1，3+统一按1处理。
    /// </summary>
    /// <param name="sameTypeCount">同类型卡片数量。</param>
    /// <returns>分量分默认值。</returns>
    private static int GetDefaultComponentScore(int sameTypeCount)
    {
        if (sameTypeCount <= 1)
        {
            return 0;
        }

        if (sameTypeCount == 2)
        {
            return DefaultSameTypeTwoScorePerCard;
        }

        // ⚠️ 避坑：默认值不含 3~8 张的阶梯分量分，统一按 2 张分值处理。
        // 若需完整阶梯分，必须配置数据表。
        return DefaultSameTypeTwoScorePerCard;
    }

    /// <summary>
    /// 获取连击时间窗口（秒）。
    /// 优先从得分配置表读取，若配置缺失则使用默认值 DefaultComboWindowSeconds。
    /// </summary>
    /// <returns>连击时间窗口时长（秒）。</returns>
    private float GetComboWindowSeconds()
    {
        DailyChallengeScoreDataRow config = GetScoreConfig();
        return config != null ? config.ComboWindowSeconds : DefaultComboWindowSeconds;
    }

    /// <summary>
    /// 获取连击倍率。
    /// </summary>
    /// <returns>连击倍率。</returns>
    private float GetComboMultiplier()
    {
        DailyChallengeScoreDataRow config = GetScoreConfig();
        return config != null ? config.ComboMultiplier : DefaultComboMultiplier;
    }

    /// <summary>
    /// 获取胜利分数翻倍倍率。
    /// </summary>
    /// <returns>胜利翻倍倍率。</returns>
    private int GetVictoryScoreMultiplier()
    {
        DailyChallengeScoreDataRow config = GetScoreConfig();
        return config != null ? config.VictoryScoreMultiplier : DefaultVictoryScoreMultiplier;
    }

    // ───────────── 满格结算回调 ─────────────

    /// <summary>
    /// 满格清空得分计算回调。
    /// 由 EliminateTheAreaEntityLogic 的 OnSettlementScoreCalculation 触发，
    /// 在清空动画开始前调用，此时等待区卡片仍完整存在（CardArea=WaitingArea）。
    /// 流程：计算本轮得分 → 连击判定 → 累加分数 → 通知 UI。
    /// </summary>
    public void OnSettlementScoreCalculationHandler()
    {
        // ── 计算本轮得分并累加 ──
        int roundScore = CalculateSettlementScore();

        // ── 连击判定 ──
        // 在连击时间窗口内连续清空，combo +1
        float now = Time.unscaledTime;
        float comboWindow = GetComboWindowSeconds();
        if (_lastSettlementRealTime > 0f && (now - _lastSettlementRealTime) <= comboWindow)
        {
            _comboCount++;
        }
        else
        {
            // 超出窗口，连击重新从 1 开始
            _comboCount = 1;
        }

        _lastSettlementRealTime = now;

        // ── 连击倍率加成 ──
        // 对齐 xinpgdd：连击数=1 时固定1倍；连击数≥2 时，倍率因子 = 连击数 × comboMultiplier
        float comboMultiplier = GetComboMultiplier();
        float factor = _comboCount <= 1
            ? 1f
            : _comboCount * comboMultiplier;
        roundScore = Mathf.RoundToInt(roundScore * factor);
        roundScore = Mathf.Max(0, roundScore);

        // ── 累加分数 ──
        _currentScore += roundScore;

        // ── 通知 UI 刷新 ──
        OnScoreUpdated?.Invoke(_currentScore);
        SyncComboDisplay();

        // ── 推进轮次 ──
        // 对齐 xinpgdd：每次有效结算后推进轮次，供"当前轮数/动态基础分"计算读取。
        _currentRound++;
    }

    /// <summary>
    /// 满格结算清空后的回调处理。
    /// 由 EliminateTheAreaEntityLogic 的 OnSettlementCleared 触发，
    /// 在清空动画结束后调用（卡片已被回收）。
    /// 流程：遮挡重算 → 胜利判定 → 自动入槽。
    /// </summary>
    public void OnSettlementClearedHandler()
    {
        // ── 结算清空后重置分数显示为 0，并同步刷新轮数/基础分信息 ──
        if (_areaEntityLogic != null)
        {
            _areaEntityLogic.ClearScoreDisplay();

            // 此时 _currentRound 已在 OnSettlementScoreCalculationHandler 中 +1
            DailyChallengeScoreDataRow config = GetScoreConfig();
            int baseScore = config != null
                ? config.GetBaseScorePerRound(_currentRound)
                : DefaultBaseScorePerCard;
            _areaEntityLogic.RefreshRoundInfoDisplay(_currentRound, baseScore);
        }

        // 结算清空后重算遮挡
        UpdateBlockingAfterRemoval();

        // ── 每日一关胜利判定 ──
        // 清空后若棋盘上已无剩余卡片，则判定为胜利
        if (GetRemainingBoardCardCount() <= 0)
        {
            // 胜利时分数翻倍
            int victoryMultiplier = GetVictoryScoreMultiplier();
            if (victoryMultiplier > 1)
            {
                _currentScore *= victoryMultiplier;
                OnScoreUpdated?.Invoke(_currentScore);
            }

            OnVictory?.Invoke();
            return;
        }

        // 检查是否需要自动入槽：若剩余棋盘卡 <= 等待区容量，自动依次入槽
        TryAutoInsertRemainingCards();
    }

    /// <summary>
    /// 满格失败回调处理。
    /// 由 EliminateTheAreaEntityLogic 的 OnSettlementFailed 触发。
    /// 标记失败状态、重置连击、通知 UI 弹出 VictoryFailUIForm(Fail)。
    /// </summary>
    public void OnSettlementFailedHandler()
    {
        _hasFailed = true;

        // 失败时重置连击
        _comboCount = 0;
        SyncComboDisplay();

        // ── 失败时清空分数显示 ──
        if (_areaEntityLogic != null)
        {
            _areaEntityLogic.ClearScoreDisplay();
        }

        OnFail?.Invoke();
    }

    /// <summary>
    /// 当前是否处于失败状态。
    /// 供 IsExitUIForm 判断：失败时弹出 VictoryFailUIForm(Fail) 而非直接返回 DailyChallengeUIForm。
    /// </summary>
    /// <returns>true=已失败；false=未失败。</returns>
    public bool HasFailedState() => _hasFailed;

    /// <summary>
    /// 尝试自动入槽：当剩余棋盘卡数量 <= 等待区容量时，
    /// 自动将所有未遮挡的卡片依次插入等待区。
    /// </summary>
    private void TryAutoInsertRemainingCards()
    {
        if (_areaEntityLogic == null || _areaEntityLogic.IsFull)
        {
            return;
        }

        int remainingCount = GetRemainingBoardCardCount();
        if (remainingCount <= 0)
        {
            return;
        }

        // 剩余卡 <= 等待区剩余容量，自动入槽
        int availableSlots = _areaEntityLogic.MaxCardCount - _areaEntityLogic.CurrentCardCount;
        if (remainingCount > availableSlots)
        {
            return;
        }

        // 收集所有未移除且未遮挡的卡片，按层级从低到高排序
        List<EliminateCardEntityLogic> autoInsertCards = new List<EliminateCardEntityLogic>();
        foreach (var kvp in _cardLogics)
        {
            EliminateCardEntityLogic card = kvp.Value;
            if (card == null || card.CurrentArea == CardArea.WaitingArea || card.IsBlocked)
            {
                continue;
            }

            autoInsertCards.Add(card);
        }

        // 按层级从低到高排序（低层先入槽，与手动点击顺序一致）
        autoInsertCards.Sort((a, b) =>
        {
            // 通过布局索引在缓存中找到层级
            int layerA = GetLayerIndexFromLayoutIndex(a.LayoutIndex);
            int layerB = GetLayerIndexFromLayoutIndex(b.LayoutIndex);
            if (layerA != layerB) return layerA - layerB;
            return a.LayoutIndex - b.LayoutIndex;
        });

        // 依次自动入槽
        for (int i = 0; i < autoInsertCards.Count; i++)
        {
            if (_areaEntityLogic.IsFull)
            {
                break;
            }

            EliminateCardEntityLogic card = autoInsertCards[i];
            if (_areaEntityLogic.TryRequestInsert(card))
            {
                MarkCardRemovedFromBoard(card, "AutoInsert");
            }
        }

        // 更新遮挡
        UpdateBlockingAfterRemoval();
    }

    /// <summary>
    /// 获取当前棋盘上剩余可点击的卡片数量。
    /// 不包括已入等待区或已回收的卡片。
    /// </summary>
    /// <returns>剩余棋盘卡数量。</returns>
    public int GetRemainingBoardCardCount()
    {
        int count = 0;
        foreach (var kvp in _cardLogics)
        {
            EliminateCardEntityLogic card = kvp.Value;
            if (card != null && card.CurrentArea != CardArea.WaitingArea && card.CurrentArea != CardArea.OutputZone && !_removedFromBoard.Contains(card.LayoutIndex))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 通过布局索引查找对应的层级索引。
    /// </summary>
    private int GetLayerIndexFromLayoutIndex(int layoutIndex)
    {
        if (_cachedLogicalCards == null)
        {
            return 0;
        }

        for (int i = 0; i < _cachedLogicalCards.Count; i++)
        {
            if (_cachedLogicalCards[i].LayoutIndex == layoutIndex)
            {
                return _cachedLogicalCards[i].LayerIndex;
            }
        }

        return 0;
    }

    /// <summary>
    /// 回收当前已经生成的所有实体。
    /// </summary>
    private void ClearSpawnedEntities(bool clearBoardCache = true)
    {
        if (_activeEntityIds.Count <= 0 || GameEntry.Entity == null)
        {
            _activeEntityIds.Clear();
            _cardLogics.Clear();
            _cardLogicsByLayoutIndex.Clear();
            _areaEntityLogic = null;
            if (clearBoardCache)
            {
                _cachedLogicalCards = null;
                _cachedProjectedWorldX = null;
                _cachedProjectedWorldY = null;
                _removedFromBoard.Clear();
            }
            _outputZoneCards.Clear();
            return;
        }

        for (int i = 0; i < _activeEntityIds.Count; i++)
        {
            int entityId = _activeEntityIds[i];
            if (entityId <= 0)
            {
                continue;
            }

            if (GameEntry.Entity.HasEntity(entityId) || GameEntry.Entity.IsLoadingEntity(entityId))
            {
                GameEntry.Entity.HideEntity(entityId);
            }
        }

        _activeEntityIds.Clear();
        _cardLogics.Clear();
        _cardLogicsByLayoutIndex.Clear();
        _areaEntityLogic = null;
        if (clearBoardCache)
        {
            _cachedLogicalCards = null;
            _cachedProjectedWorldX = null;
            _cachedProjectedWorldY = null;
            _removedFromBoard.Clear();
        }
        _outputZoneCards.Clear();
    }

    /// <summary>
    /// 逻辑卡片数据。
    /// 这里只保存“摆盘几何”所需的最小字段。
    /// </summary>
    private readonly struct EliminateCardLogicalCard
    {
        /// <summary>
        /// 当前卡片属于主区还是 DIR 区。
        /// 主区需要参与“按自身宽度重新居中”，DIR 区则必须保持固定口径不跟随移动。
        /// </summary>
        public readonly EliminateCardArea Area;

        /// <summary>
        /// 原始布局索引。
        /// 主要用于调试与稳定排序。
        /// </summary>
        public readonly int LayoutIndex;

        /// <summary>
        /// 层级。
        /// 迁到 sgdd 后不再映射为世界 Y 高度，而是映射为 sortingOrder。
        /// </summary>
        public readonly int LayerIndex;

        /// <summary>
        /// 逻辑 X 坐标。
        /// </summary>
        public readonly float LogicalX;

        /// <summary>
        /// 逻辑 Y 坐标。
        /// 这里已经把原 CSV 的“行向下增加”翻转成了世界中“向上为正”的口径。
        /// </summary>
        public readonly float LogicalY;

        /// <summary>
        /// 原始网格行索引。
        /// 主区做“底对齐补行”时，不看半格偏移后的 LogicalY，
        /// 而是直接按这份整行索引判断主区目前铺到了第几行。
        /// </summary>
        public readonly int GridRowIndex;

        public EliminateCardLogicalCard(
            EliminateCardArea area,
            int layoutIndex,
            int layerIndex,
            int gridRowIndex,
            float logicalX,
            float logicalY)
        {
            Area = area;
            LayoutIndex = layoutIndex;
            LayerIndex = layerIndex;
            GridRowIndex = gridRowIndex;
            LogicalX = logicalX;
            LogicalY = logicalY;
        }
    }

    /// <summary>
    /// 消除卡片逻辑卡所属区域。
    /// 这里只做最小区分：
    /// 1. Main：主棋盘固定布局卡；
    /// 2. Dir：通过 DIR / Extended DIR 追加出来的附属区域卡。
    /// </summary>
    private enum EliminateCardArea
    {
        /// <summary>
        /// 主棋盘区域。
        /// </summary>
        Main = 0,

        /// <summary>
        /// DIR 附属区域。
        /// </summary>
        Dir = 1,
    }

    /// <summary>
    /// 单个显示类型的最终视觉结果。
    /// </summary>
    private readonly struct EliminateCardAssignedTypeVisual
    {
        /// <summary>
        /// 逻辑类型 Id。
        /// </summary>
        public readonly int TypeId;

        /// <summary>
        /// 命中的卡图资源。
        /// </summary>
        public readonly Sprite DisplaySprite;

        /// <summary>
        /// 显示颜色。
        /// 当前用于在“卡图不全”的阶段区分不同类型。
        /// </summary>
        public readonly Color TintColor;

        public EliminateCardAssignedTypeVisual(int typeId, Sprite displaySprite, Color tintColor)
        {
            TypeId = typeId;
            DisplaySprite = displaySprite;
            TintColor = tintColor;
        }
    }

    /// <summary>
    /// 实体生成指令。
    /// </summary>
    private readonly struct EliminateCardSpawnInstruction
    {
        /// <summary>
        /// 传递给实体逻辑的显示数据。
        /// </summary>
        public readonly EliminateCardEntityData EntityData;

        /// <summary>
        /// 当前棋盘使用的单元格宽度。
        /// </summary>
        public readonly float CellWidth;

        /// <summary>
        /// 当前棋盘使用的单元格高度。
        /// </summary>
        public readonly float CellHeight;

        public EliminateCardSpawnInstruction(EliminateCardEntityData entityData, float cellWidth, float cellHeight)
        {
            EntityData = entityData;
            CellWidth = cellWidth;
            CellHeight = cellHeight;
        }
    }
}

/// <summary>
/// 消除卡片预览构建结果。
/// 这个结果对象只服务于 UI 文案展示与调试，不参与玩法逻辑。
/// </summary>
public readonly struct EliminateCardPreviewResult
{
    /// <summary>
    /// 本次预览是否成功。
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// 当前关卡名。
    /// 这里直接使用资源路径的最后一段，便于页面展示。
    /// </summary>
    public string LevelName { get; }

    /// <summary>
    /// 实际生成的卡片数量。
    /// </summary>
    public int CardCount { get; }

    /// <summary>
    /// 被跳过的 Legacy DIR 任务数量。
    /// 用于把当前阶段的能力边界直接展示在页面上。
    /// </summary>
    public int IgnoredLegacyDirectionTaskCount { get; }

    /// <summary>
    /// 失败原因。
    /// 成功时为空字符串。
    /// </summary>
    public string ErrorMessage { get; }

    private EliminateCardPreviewResult(
        bool isSuccess,
        string levelName,
        int cardCount,
        int ignoredLegacyDirectionTaskCount,
        string errorMessage)
    {
        IsSuccess = isSuccess;
        LevelName = levelName;
        CardCount = cardCount;
        IgnoredLegacyDirectionTaskCount = ignoredLegacyDirectionTaskCount;
        ErrorMessage = errorMessage ?? string.Empty;
    }

    /// <summary>
    /// 创建成功结果。
    /// </summary>
    public static EliminateCardPreviewResult Succeeded(
        string levelAssetPath,
        int cardCount,
        int ignoredLegacyDirectionTaskCount)
    {
        string levelName = string.IsNullOrWhiteSpace(levelAssetPath)
            ? "Unknown"
            : levelAssetPath.Substring(levelAssetPath.LastIndexOf('/') + 1);
        return new EliminateCardPreviewResult(true, levelName, cardCount, ignoredLegacyDirectionTaskCount, string.Empty);
    }

    /// <summary>
    /// 创建失败结果。
    /// </summary>
    public static EliminateCardPreviewResult Failed(string errorMessage)
    {
        return new EliminateCardPreviewResult(false, string.Empty, 0, 0, errorMessage);
    }
}
