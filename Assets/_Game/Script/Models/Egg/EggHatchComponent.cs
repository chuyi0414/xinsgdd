using System;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 蛋孵化统一运行时组件。
/// 统一管理手动蛋库存、补蛋进度和孵化槽位状态。
/// </summary>
public sealed class EggHatchComponent : GameFrameworkComponent
{
    // 当前主界面固定摆放 4 个孵化槽位，但运行时可购买数量由 PlayerRuntimeModule 决定。
    private const int HatchSlotCountValue = 4;

    /// <summary>
    /// 自动孵化（看广告获得）库存上限。
    /// 与手动蛋库存彻底独立，互不挤占。
    /// </summary>
    private const int MaxAutoEggCountValue = 15;

    /// <summary>
    /// 自动孵化奖励池：85% 普通蛋 Code。
    /// 不在配置表中再加字段，所有 Code 与现有 Egg.txt 行 1:1 对应。
    /// </summary>
    private const string AutoEggCodeNormal = "egg_normal";

    /// <summary>
    /// 自动孵化奖励池：8% 稀有蛋 Code。
    /// </summary>
    private const string AutoEggCodeRare = "egg_rare";

    /// <summary>
    /// 自动孵化奖励池：4% 史诗蛋 Code。
    /// </summary>
    private const string AutoEggCodeEpic = "egg_epic";

    /// <summary>
    /// 自动孵化奖励池：2% 传说蛋 Code。
    /// </summary>
    private const string AutoEggCodeLegendary = "egg_legendary";

    /// <summary>
    /// 自动孵化奖励池：1% 神话蛋 Code。
    /// </summary>
    private const string AutoEggCodeMythic = "egg_mythic";

    /// <summary>
    /// 普通蛋累计阈值：[0,85) 命中。
    /// </summary>
    private const int AutoEggThresholdNormal = 85;

    /// <summary>
    /// 稀有蛋累计阈值：[85,93) 命中。
    /// </summary>
    private const int AutoEggThresholdRare = 93;

    /// <summary>
    /// 史诗蛋累计阈值：[93,97) 命中。
    /// </summary>
    private const int AutoEggThresholdEpic = 97;

    /// <summary>
    /// 传说蛋累计阈值：[97,99) 命中。
    /// 神话蛋走 [99,100) 默认分支。
    /// </summary>
    private const int AutoEggThresholdLegendary = 99;

    /// <summary>
    /// 蛋购买失败原因。
    /// </summary>
    public enum EggPurchaseFailure
    {
        None = 0,
        DependenciesUnavailable = 1,
        InvalidEgg = 2,
        NotPurchasable = 3,
        InsufficientGold = 4,
        InventoryFull = 5,
        /// <summary>
        /// 玩家星星总额不足 EggDataRow.RequiredStars。
        /// </summary>
        NotEnoughStars = 6,
    }

    /// <summary>
    /// 固定数量的孵化槽运行时状态集合。
    /// </summary>
    private readonly EggHatchSlotState[] _slotStates = new EggHatchSlotState[HatchSlotCountValue];

    /// <summary>
    /// 全局玩法规则缓存。
    /// </summary>
    private GameplayRuleDataRow _gameplayRuleDataRow;

    /// <summary>
    /// 手动孵化所使用的蛋配置缓存。
    /// </summary>
    private EggDataRow _manualEggDataRow;

    /// <summary>
    /// 是否已完成首次初始化。
    /// </summary>
    private bool _isInitialized;

    /// <summary>
    /// 当前组件是否处于可用状态。
    /// </summary>
    private bool _isAvailable;

    /// <summary>
    /// 当前手动蛋库存中的蛋 Code 队列。
    /// </summary>
    private string[] _manualEggCodes;

    /// <summary>
    /// 当前手动蛋库存中的蛋品质队列。
    /// </summary>
    private QualityType[] _manualEggQualities;

    /// <summary>
    /// 当前手动蛋库存数量。
    /// </summary>
    private int _manualEggCount;

    /// <summary>
    /// 自动孵化（看广告获得）库存中的蛋 Code 队列。
    /// 容量固定为 MaxAutoEggCountValue，0 号元素为最先消耗。
    /// </summary>
    private readonly string[] _autoEggCodes = new string[MaxAutoEggCountValue];

    /// <summary>
    /// 自动孵化（看广告获得）库存中的蛋品质队列。
    /// 与 _autoEggCodes 同步维护，仅供 UI 着色使用。
    /// </summary>
    private readonly QualityType[] _autoEggQualities = new QualityType[MaxAutoEggCountValue];

    /// <summary>
    /// 自动孵化库存的当前数量。
    /// 永远满足 0 ≤ _autoEggCount ≤ MaxAutoEggCountValue。
    /// </summary>
    private int _autoEggCount;

    /// <summary>
    /// 当前累计的补蛋进度秒数。
    /// </summary>
    private float _refillElapsedSeconds;

    /// <summary>
    /// 当前是否允许在线业务秒数每帧推进。
    /// 初始为 false，保证 LoadUIForm 阶段只读档和做一次性离线结算，不让 Unity Update 持续扣秒。
    /// </summary>
    private bool _isRuntimeTickEnabled;

    /// <summary>
    /// 孵化运行时状态发生离散变化时触发。
    /// 仅在库存数量、库存内容、孵化槽占用、孵化完成、读档覆盖等低频节点派发，不在每帧倒计时里派发。
    /// </summary>
    public event Action HatchStateChanged;

    /// <summary>
    /// 当前组件是否已完成首次初始化。
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// 当前组件是否可用于业务逻辑。
    /// </summary>
    public bool IsAvailable => _isAvailable;

    /// <summary>
    /// 当前是否允许在线业务秒数每帧推进。
    /// </summary>
    public bool IsRuntimeTickEnabled => _isRuntimeTickEnabled;

    /// <summary>
    /// 当前手动蛋库存。
    /// </summary>
    public int ManualEggCount => _manualEggCount;

    /// <summary>
    /// 最大手动蛋库存。
    /// </summary>
    public int MaxManualEggCount => _gameplayRuleDataRow != null ? _gameplayRuleDataRow.MaxManualEggCount : 0;

    /// <summary>
    /// 最大自动孵化蛋库存。
    /// </summary>
    public int MaxAutoEggCount => MaxAutoEggCountValue;

    /// <summary>
    /// 当前自动孵化蛋库存数量。
    /// </summary>
    public int AutoEggCount => _autoEggCount;

    /// <summary>
    /// 获取指定下标的自动孵化蛋信息。
    /// 该接口供 UI 刷新 GoZiDongFuHua/GoDanShuLiang 库存点使用。
    /// </summary>
    /// <param name="index">下标，范围 [0, AutoEggCount)。</param>
    /// <param name="eggCode">输出蛋 Code。</param>
    /// <param name="quality">输出蛋品质。</param>
    /// <returns>该下标存在合法蛋时返回 true。</returns>
    public bool TryGetAutoEggAt(int index, out string eggCode, out QualityType quality)
    {
        eggCode = null;
        quality = QualityType.Universal;

        if (index < 0 || index >= _autoEggCount)
        {
            return false;
        }

        eggCode = _autoEggCodes[index];
        quality = _autoEggQualities[index];
        return !string.IsNullOrWhiteSpace(eggCode);
    }

    /// <summary>
    /// 孵化槽位数量。
    /// 这里返回的是总槽位数量，不等于当前已购买数量。
    /// </summary>
    public int SlotCount => HatchSlotCountValue;

    /// <summary>
    /// 当前已购买并可参与孵化的槽位数量。
    /// 若玩家运行时模块不可用，则退回到全部 4 槽。
    /// </summary>
    public int UnlockedSlotCount
    {
        get
        {
            int runtimeSlotCount = GameEntry.Fruits != null ? GameEntry.Fruits.HatchSlotCount : HatchSlotCountValue;
            return Mathf.Clamp(runtimeSlotCount, 1, HatchSlotCountValue);
        }
    }

    /// <summary>
    /// 获取指定库存位的蛋信息。
    /// </summary>
    public bool TryGetManualEggAt(int index, out string eggCode, out QualityType quality)
    {
        eggCode = null;
        quality = QualityType.Universal;

        if (index < 0 || index >= _manualEggCount || _manualEggCodes == null || _manualEggQualities == null)
        {
            return false;
        }

        eggCode = _manualEggCodes[index];
        quality = _manualEggQualities[index];
        return !string.IsNullOrWhiteSpace(eggCode);
    }

    /// <summary>
    /// 补蛋进度，0 到 1。
    /// </summary>
    public float RefillProgressNormalized
    {
        get
        {
            if (!_isInitialized)
            {
                return 0f;
            }

            if (_gameplayRuleDataRow == null)
            {
                return 0f;
            }

            if (_manualEggCount >= _gameplayRuleDataRow.MaxManualEggCount)
            {
                return 1f;
            }

            return Mathf.Clamp01(_refillElapsedSeconds / _gameplayRuleDataRow.RefillDurationSeconds);
        }
    }

    /// <summary>
    /// 当前是否允许执行手动操作。
    /// </summary>
    public bool CanManualAction
    {
        get
        {
            if (!_isAvailable)
            {
                return false;
            }

            bool hasEmptySlot = TryGetEmptySlotIndex(out _);
            // 只要还有库存缺口，就允许继续点击按钮加速补蛋；
            // 如果同时有空槽且手里有蛋，则点击优先消耗蛋进入孵化。
            return _gameplayRuleDataRow != null
                && ((hasEmptySlot && _manualEggCount > 0) || _manualEggCount < _gameplayRuleDataRow.MaxManualEggCount);
        }
    }

    /// <summary>
    /// 初始化孵化槽对象缓存。
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        for (int i = 0; i < _slotStates.Length; i++)
        {
            _slotStates[i] = new EggHatchSlotState();
        }
    }

    /// <summary>
    /// 推进孵化倒计时与补蛋进度。
    /// </summary>
    private void Update()
    {
        if (!_isInitialized || !_isAvailable || !_isRuntimeTickEnabled)
        {
            return;
        }

        float deltaTime = Time.unscaledDeltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        UpdateHatchSlots(deltaTime);
        UpdateRefillProgress(deltaTime);
        // 自动孵化库存只要还有蛋且存在空槽，就在每帧 Tick 末尾消耗一次。
        // 放在手动孵化和补蛋之后，确保玩家手动行为始终拥有最高优先级。
        TryAutoConsumeFront();
    }

    /// <summary>
    /// 设置在线业务秒数是否允许推进。
    /// </summary>
    /// <param name="isEnabled">true 表示主界面已进入，可以从当前帧开始按真实时间推进孵化和补蛋。</param>
    public void SetRuntimeTickEnabled(bool isEnabled)
    {
        _isRuntimeTickEnabled = isEnabled;
    }

    /// <summary>
    /// 确保组件已完成初始化。
    /// </summary>
    public void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        // 运行时状态依赖蛋表与全局玩法规则表，缺任一表都不能初始化。
        if (GameEntry.DataTables == null
            || !GameEntry.DataTables.IsAvailable<EggDataRow>()
            || !GameEntry.DataTables.IsAvailable<GameplayRuleDataRow>())
        {
            Log.Error("EggHatchComponent initialize failed because required data tables are unavailable.");
            _isAvailable = false;
            return;
        }

        _gameplayRuleDataRow = GameEntry.DataTables.GetDataRowByCode<GameplayRuleDataRow>(GameplayRuleDataRow.DefaultCode);
        if (_gameplayRuleDataRow == null)
        {
            Log.Error("EggHatchComponent initialize failed because GameplayRuleDataRow is unavailable.");
            _isAvailable = false;
            return;
        }

        _manualEggDataRow = GameEntry.DataTables.GetDataRowByCode<EggDataRow>(_gameplayRuleDataRow.ManualEggCode);
        if (_manualEggDataRow == null)
        {
            Log.Error("EggHatchComponent initialize failed because manual egg '{0}' can not be found.", _gameplayRuleDataRow.ManualEggCode);
            _isAvailable = false;
            return;
        }

        if (_manualEggDataRow.HatchSeconds <= 0)
        {
            Log.Error("EggHatchComponent initialize failed because manual egg hatch seconds is invalid.");
            _isAvailable = false;
            return;
        }

        if (_gameplayRuleDataRow.MaxManualEggCount <= 0)
        {
            Log.Error("EggHatchComponent initialize failed because max manual egg count is invalid.");
            _isAvailable = false;
            return;
        }

        _manualEggCodes = new string[_gameplayRuleDataRow.MaxManualEggCount];
        _manualEggQualities = new QualityType[_gameplayRuleDataRow.MaxManualEggCount];

        // 按需求只在应用首次启动时初始化一次库存和槽位。
        ResetRuntimeState();
        _isInitialized = true;
        _isAvailable = true;
    }

    /// <summary>
    /// 获取指定槽位状态。
    /// </summary>
    public EggHatchSlotState GetSlotState(int index)
    {
        if (index < 0 || index >= _slotStates.Length)
        {
            Log.Warning("EggHatchComponent can not get slot state because index '{0}' is invalid.", index);
            return null;
        }

        if (index >= UnlockedSlotCount)
        {
            return null;
        }

        // 防止外部按"前 N 个连续解锁"假设访问跳购未解锁的"洞"。
        // 例如玩家跳购 3 号位后 _hatchSlotCount=3，但 2 号位 IsUnlocked=false，GetSlotState(1) 必须返回 null。
        if (GameEntry.Fruits != null && !GameEntry.Fruits.IsArchitectureSlotUnlocked(PlayerRuntimeModule.ArchitectureCategory.Hatch, index + 1))
        {
            return null;
        }

        return _slotStates[index];
    }

    /// <summary>
    /// 尝试购买一个商店蛋并插入库存队首。
    /// </summary>
    public bool TryPurchaseEgg(string eggCode, out EggPurchaseFailure failure)
    {
        failure = EggPurchaseFailure.DependenciesUnavailable;
        EnsureInitialized();
        if (!_isInitialized
            || !_isAvailable
            || _gameplayRuleDataRow == null
            || GameEntry.DataTables == null
            || !GameEntry.DataTables.IsAvailable<EggDataRow>()
            || GameEntry.Fruits == null
            || !GameEntry.Fruits.EnsureInitialized())
        {
            return false;
        }

        EggDataRow eggDataRow = GameEntry.DataTables.GetDataRowByCode<EggDataRow>(eggCode);
        if (eggDataRow == null)
        {
            failure = EggPurchaseFailure.InvalidEgg;
            return false;
        }

        if ((eggDataRow.AcquireWays & EggDataRow.EggAcquireWay.Shop) == 0 || eggDataRow.PurchaseGold <= 0)
        {
            failure = EggPurchaseFailure.NotPurchasable;
            return false;
        }

        // 星星条件校验：必须在 TryConsumeGold 之前拦截，避免金币已扣除但星星不够造成资源错错。
        // RequiredStars=0 表示无限制，CurrentStars >= 0 总是成立，与旧逻辑完全兼容。
        if (GameEntry.Fruits.CurrentStars < eggDataRow.RequiredStars)
        {
            failure = EggPurchaseFailure.NotEnoughStars;
            return false;
        }

        if (!CanInsertPurchasedEgg(eggDataRow.Quality, out int replaceIndex))
        {
            failure = EggPurchaseFailure.InventoryFull;
            return false;
        }

        if (!GameEntry.Fruits.TryConsumeGold(eggDataRow.PurchaseGold))
        {
            failure = EggPurchaseFailure.InsufficientGold;
            return false;
        }

        if (!InsertEggAtFront(eggDataRow.Code, eggDataRow.Quality, replaceIndex))
        {
            GameEntry.Fruits.AddGold(eggDataRow.PurchaseGold);
            failure = EggPurchaseFailure.InventoryFull;
            return false;
        }

        NotifyHatchStateChanged();
        failure = EggPurchaseFailure.None;
        return true;
    }

    /// <summary>
    /// 尝试执行一次手动操作。
    /// </summary>
    public void TryManualAction()
    {
        if (!CanManualAction)
        {
            return;
        }

        // 点击优先走“有空槽就放蛋孵化”，只有满槽时才走补蛋减秒。
        if (TryGetEmptySlotIndex(out int emptySlotIndex) && _manualEggCount > 0)
        {
            if (TryDequeueFrontEgg(out string eggCode, out float hatchSeconds))
            {
                OccupySlot(emptySlotIndex, eggCode, hatchSeconds);
                NotifyHatchStateChanged();
            }

            return;
        }

        if (_gameplayRuleDataRow != null && _manualEggCount < _gameplayRuleDataRow.MaxManualEggCount)
        {
            if (AccelerateRefill(_gameplayRuleDataRow.ManualReduceSeconds))
            {
                NotifyHatchStateChanged();
            }
        }
    }

    /// <summary>
    /// 增加手动蛋库存。
    /// </summary>
    /// <param name="amount">要增加的手动蛋数量。</param>
    public void AddManualEggs(int amount)
    {
        if (!_isInitialized || !_isAvailable || _gameplayRuleDataRow == null || amount <= 0)
        {
            return;
        }

        int previousManualEggCount = _manualEggCount;
        for (int i = 0; i < amount; i++)
        {
            if (!TryAppendEggToBack(_manualEggDataRow.Code, _manualEggDataRow.Quality))
            {
                break;
            }
        }

        if (_manualEggCount >= _gameplayRuleDataRow.MaxManualEggCount)
        {
            _refillElapsedSeconds = 0f;
        }

        if (_manualEggCount != previousManualEggCount)
        {
            NotifyHatchStateChanged();
        }
    }

    /// <summary>
    /// 广告奖励入口：按 85/8/4/2/1 概率独立随机 amount 个蛋追加到自动孵化库存队尾。
    /// 单次调用内每个蛋单独 roll，不做批量优化，严格满足"15 次独立随机"。
    /// 库存满（达到 MaxAutoEggCountValue）时会丢弃多余蛋并发出警告，
    /// 业务层应在调用前确保有足够空位（通常一次给 15 个，必须保证库存为空才能塞满）。
    /// </summary>
    /// <param name="amount">本次要发放的蛋数量；非正数直接返回。</param>
    /// <returns>实际成功入库的蛋数量。</returns>
    public int AddRandomAutoEggs(int amount)
    {
        EnsureInitialized();
        if (!_isInitialized || !_isAvailable || amount <= 0)
        {
            return 0;
        }

        if (GameEntry.DataTables == null || !GameEntry.DataTables.IsAvailable<EggDataRow>())
        {
            Log.Warning("EggHatchComponent 无法发放广告奖励蛋，蛋表不可用。");
            return 0;
        }

        int previousAutoEggCount = _autoEggCount;
        int grantedCount = 0;
        for (int i = 0; i < amount; i++)
        {
            // 每个蛋独立 roll：Random.Range(int,int) 上界开区间 → [0,100)。
            // 阈值升序累加比较，分支极少，无装箱、无委托、无 LINQ。
            int roll = UnityEngine.Random.Range(0, 100);
            string eggCode;
            if (roll < AutoEggThresholdNormal)
            {
                eggCode = AutoEggCodeNormal;
            }
            else if (roll < AutoEggThresholdRare)
            {
                eggCode = AutoEggCodeRare;
            }
            else if (roll < AutoEggThresholdEpic)
            {
                eggCode = AutoEggCodeEpic;
            }
            else if (roll < AutoEggThresholdLegendary)
            {
                eggCode = AutoEggCodeLegendary;
            }
            else
            {
                eggCode = AutoEggCodeMythic;
            }

            EggDataRow eggDataRow = GameEntry.DataTables.GetDataRowByCode<EggDataRow>(eggCode);
            if (eggDataRow == null || eggDataRow.HatchSeconds <= 0)
            {
                // 配置缺失：记录一次，后续相同蛋仍按当前 roll 失败处理，避免静默吞掉奖励。
                Log.Warning("EggHatchComponent 跳过非法广告奖励蛋 '{0}'。", eggCode);
                continue;
            }

            if (!TryAppendAutoEggToBack(eggDataRow.Code, eggDataRow.Quality))
            {
                // 库存已满：直接停止，避免 14 次空循环。
                Log.Warning("EggHatchComponent 自动孵化库存已满，本次仅入库 {0}/{1} 个广告奖励蛋。", grantedCount, amount);
                break;
            }

            grantedCount++;
        }

        if (_autoEggCount != previousAutoEggCount)
        {
            // 立刻尝试消耗一次，防止"看完广告 → 4 个空槽未瞬间生效"的体感卡顿。
            TryAutoConsumeFront();
            NotifyHatchStateChanged();
        }

        return grantedCount;
    }

    /// <summary>
    /// 自动消耗：只要还有空槽且自动孵化库存非空，就把队首蛋直接放入孵化槽。
    /// 该方法在 Update Tick 与 AddRandomAutoEggs 末尾各调一次，
    /// 保证"满足空位即开始孵化并库存 -1"的预期行为。
    /// </summary>
    /// <returns>本次是否真正占用过任何槽位。</returns>
    private bool TryAutoConsumeFront()
    {
        if (_autoEggCount <= 0 || GameEntry.DataTables == null)
        {
            return false;
        }

        bool hasOccupied = false;
        // 这里使用 while 而不是单次 if，是为了在玩家同时购买多个槽位/解锁多个槽位的极端情况下，
        // 一次 Update 就能把所有空槽全部填满，不依赖下一帧再补。
        while (_autoEggCount > 0 && TryGetEmptySlotIndex(out int emptySlotIndex))
        {
            string eggCode = _autoEggCodes[0];
            EggDataRow eggDataRow = ResolveValidEggDataRow(eggCode);
            if (eggDataRow == null)
            {
                // 数据非法时丢弃队首并继续尝试下一个，避免脏数据卡死循环。
                Log.Warning("EggHatchComponent 跳过非法自动孵化蛋 '{0}'。", eggCode);
                DequeueAutoEggFront();
                continue;
            }

            // 注意：OccupySlot 内部会按建筑加速倍率再次缩放 hatchSeconds，
            // 这里直接传蛋表基础时长即可，与手动孵化路径完全一致。
            OccupySlot(emptySlotIndex, eggDataRow.Code, eggDataRow.HatchSeconds);
            DequeueAutoEggFront();
            hasOccupied = true;
        }

        return hasOccupied;
    }

    /// <summary>
    /// 自动孵化库存 -> 队尾追加一个蛋。
    /// 与 TryAppendEggToBack（手动队列）独立，不会触碰 MaxManualEggCount。
    /// </summary>
    /// <param name="eggCode">蛋 Code。</param>
    /// <param name="quality">蛋品质。</param>
    /// <returns>追加成功返回 true。</returns>
    private bool TryAppendAutoEggToBack(string eggCode, QualityType quality)
    {
        if (string.IsNullOrWhiteSpace(eggCode) || _autoEggCount >= MaxAutoEggCountValue)
        {
            return false;
        }

        _autoEggCodes[_autoEggCount] = eggCode;
        _autoEggQualities[_autoEggCount] = quality;
        _autoEggCount++;
        return true;
    }

    /// <summary>
    /// 自动孵化库存 -> 弹出队首一个蛋。
    /// 内部用尾元素覆盖头位的方式做 O(1) 弹出，避免大数组 Array.Copy 触发 GC。
    /// 注意：库存语义保持队列顺序对 UI 着色无影响（UI 只关心数量与品质聚合），
    /// 真要严格 FIFO，可改成线性左移；这里用尾覆盖头是有意为之，性能优先。
    /// </summary>
    private void DequeueAutoEggFront()
    {
        if (_autoEggCount <= 0)
        {
            return;
        }

        // 把队尾元素移到 0 号位，等价于一次出队：保持数量 -1，且 [0, _autoEggCount) 内仍是合法蛋。
        int lastIndex = _autoEggCount - 1;
        if (lastIndex > 0)
        {
            _autoEggCodes[0] = _autoEggCodes[lastIndex];
            _autoEggQualities[0] = _autoEggQualities[lastIndex];
        }
        _autoEggCodes[lastIndex] = null;
        _autoEggQualities[lastIndex] = QualityType.Universal;
        _autoEggCount--;
    }

    /// <summary>
    /// 导出当前孵化运行时状态到云存档。
    /// </summary>
    /// <returns>可序列化的孵化存档数据；组件不可用时返回空存档对象，避免微信云函数入参出现 null。</returns>
    public EggHatchSaveData ExportCloudSaveData()
    {
        EnsureInitialized();
        if (!_isInitialized || !_isAvailable)
        {
            return new EggHatchSaveData();
        }

        EggHatchSaveData saveData = new EggHatchSaveData
        {
            manualEggCodes = ExportManualEggCodes(),
            autoEggCodes = ExportAutoEggCodes(),
            refillElapsedSeconds = Mathf.Max(0f, _refillElapsedSeconds),
            slots = ExportSlotSaveData()
        };

        return saveData;
    }

    /// <summary>
    /// 从云存档恢复孵化运行时状态。
    /// </summary>
    /// <param name="saveData">云端保存的孵化状态；为空时保持本地默认初始化状态。</param>
    public void ApplyCloudSaveData(EggHatchSaveData saveData)
    {
        EnsureInitialized();
        if (!_isInitialized || !_isAvailable)
        {
            return;
        }

        if (saveData == null)
        {
            NotifyEggSlotsChanged();
            return;
        }

        ClearManualEggInventory();
        RestoreManualEggInventory(saveData.manualEggCodes);
        ClearAutoEggInventory();
        RestoreAutoEggInventory(saveData.autoEggCodes);
        RestoreRefillElapsedSeconds(saveData.refillElapsedSeconds);
        RestoreHatchSlots(saveData.slots);
        NotifyEggSlotsChanged();
        NotifyHatchStateChanged();
    }

    /// <summary>
    /// 应用云存档离线期间累计的真实秒数。
    /// 该方法只做一次性结算，不打开每帧在线跑秒；LoadUIForm 阶段可安全调用。
    /// </summary>
    /// <param name="offlineSeconds">从上次云端快照到本次读档之间经过的真实秒数。</param>
    /// <returns>本次是否改变了库存蛋、孵化槽或孵化出的宠物状态。</returns>
    public bool ApplyOfflineElapsedSeconds(float offlineSeconds)
    {
        EnsureInitialized();
        if (!_isInitialized || !_isAvailable || offlineSeconds <= 0f)
        {
            return false;
        }

        bool hasStateChanged = false;
        if (_gameplayRuleDataRow != null && _manualEggCount < _gameplayRuleDataRow.MaxManualEggCount)
        {
            _refillElapsedSeconds += offlineSeconds;
            hasStateChanged = true;
            if (ApplyCompletedRefill())
            {
                hasStateChanged = true;
            }
        }

        int unlockedSlotCount = UnlockedSlotCount;
        for (int i = 0; i < unlockedSlotCount; i++)
        {
            if (GameEntry.Fruits != null && !GameEntry.Fruits.IsArchitectureSlotUnlocked(PlayerRuntimeModule.ArchitectureCategory.Hatch, i + 1))
            {
                continue;
            }

            EggHatchSlotState slotState = _slotStates[i];
            if (slotState == null || !slotState.IsOccupied)
            {
                continue;
            }

            if (offlineSeconds >= slotState.RemainingSeconds)
            {
                TrySpawnOfflineHatchedPetToPlayArea(slotState);
                slotState.Clear();
                hasStateChanged = true;
                continue;
            }

            slotState.RemainingSeconds -= offlineSeconds;
            hasStateChanged = true;
        }

        if (hasStateChanged)
        {
            NotifyEggSlotsChanged();
            NotifyHatchStateChanged();
        }

        return hasStateChanged;
    }

    /// <summary>
    /// 重置运行时状态。
    /// </summary>
    private void ResetRuntimeState()
    {
        _manualEggCount = 0;
        _refillElapsedSeconds = 0f;

        if (_manualEggCodes != null && _manualEggQualities != null)
        {
            for (int i = 0; i < _manualEggCodes.Length; i++)
            {
                _manualEggCodes[i] = null;
                _manualEggQualities[i] = QualityType.Universal;
            }
        }

        // 自动孵化库存与运行时一起清空，避免重置后旧的广告奖励残留。
        ClearAutoEggInventory();

        for (int i = 0; i < _slotStates.Length; i++)
        {
            _slotStates[i].Clear();
        }

        for (int i = 0; i < _gameplayRuleDataRow.InitialManualEggCount; i++)
        {
            if (!TryAppendEggToBack(_manualEggDataRow.Code, _manualEggDataRow.Quality))
            {
                break;
            }
        }

        NotifyEggSlotsChanged();
    }

    /// <summary>
    /// 更新孵化槽位倒计时。
    /// </summary>
    private void UpdateHatchSlots(float deltaTime)
    {
        bool hasSlotChanged = false;
        int unlockedSlotCount = UnlockedSlotCount;
        for (int i = 0; i < unlockedSlotCount; i++)
        {
            // 跳过跳购导致的未解锁"洞"：例如 _hatchSlotCount=3 但 2 号槽仍锁着不该推进孵化计时。
            if (GameEntry.Fruits != null && !GameEntry.Fruits.IsArchitectureSlotUnlocked(PlayerRuntimeModule.ArchitectureCategory.Hatch, i + 1))
            {
                continue;
            }

            EggHatchSlotState slotState = _slotStates[i];
            if (!slotState.IsOccupied)
            {
                continue;
            }

            slotState.RemainingSeconds -= deltaTime;
            if (slotState.RemainingSeconds <= 0f)
            {
                TrySpawnHatchedPet(slotState, i);
                slotState.Clear();
                hasSlotChanged = true;
            }
        }

        if (hasSlotChanged)
        {
            NotifyEggSlotsChanged();
            NotifyHatchStateChanged();
        }
    }

    /// <summary>
    /// 更新补蛋进度。
    /// </summary>
    private void UpdateRefillProgress(float deltaTime)
    {
        if (_gameplayRuleDataRow == null)
        {
            return;
        }

        if (_manualEggCount >= _gameplayRuleDataRow.MaxManualEggCount)
        {
            // 满库存时不保留历史补蛋进度，避免再次消耗时立即补满。
            _refillElapsedSeconds = 0f;
            return;
        }

        _refillElapsedSeconds += deltaTime;
        if (ApplyCompletedRefill())
        {
            NotifyHatchStateChanged();
        }
    }

    /// <summary>
    /// 手动加速补蛋进度。
    /// </summary>
    private bool AccelerateRefill(float seconds)
    {
        if (_gameplayRuleDataRow == null || _manualEggCount >= _gameplayRuleDataRow.MaxManualEggCount || seconds <= 0f)
        {
            return false;
        }

        _refillElapsedSeconds += seconds;
        return ApplyCompletedRefill();
    }

    /// <summary>
    /// 处理补蛋完成结果。
    /// </summary>
    private bool ApplyCompletedRefill()
    {
        if (_gameplayRuleDataRow == null)
        {
            return false;
        }

        bool hasInventoryChanged = false;
        // 允许一次跨过多个 30 秒区间，避免加速时丢失进度余量。
        while (_manualEggCount < _gameplayRuleDataRow.MaxManualEggCount
            && _refillElapsedSeconds >= _gameplayRuleDataRow.RefillDurationSeconds)
        {
            _refillElapsedSeconds -= _gameplayRuleDataRow.RefillDurationSeconds;
            if (!TryAppendEggToBack(_manualEggDataRow.Code, _manualEggDataRow.Quality))
            {
                break;
            }

            hasInventoryChanged = true;
        }

        if (_manualEggCount >= _gameplayRuleDataRow.MaxManualEggCount)
        {
            _refillElapsedSeconds = 0f;
        }

        return hasInventoryChanged;
    }

    /// <summary>
    /// 查找最左侧空槽位。
    /// </summary>
    private bool TryGetEmptySlotIndex(out int slotIndex)
    {
        int unlockedSlotCount = UnlockedSlotCount;
        for (int i = 0; i < unlockedSlotCount; i++)
        {
            // 跳过跳购导致的未解锁"洞"，避免把蛋分配到锁着的槽位。
            if (GameEntry.Fruits != null && !GameEntry.Fruits.IsArchitectureSlotUnlocked(PlayerRuntimeModule.ArchitectureCategory.Hatch, i + 1))
            {
                continue;
            }

            if (_slotStates[i].IsOccupied)
            {
                continue;
            }

            slotIndex = i;
            return true;
        }

        slotIndex = -1;
        return false;
    }

    /// <summary>
    /// 占用指定槽位开始孵化。
    /// </summary>
    private void OccupySlot(int slotIndex, string eggCode, float hatchSeconds)
    {
        if (slotIndex < 0 || slotIndex >= _slotStates.Length)
        {
            return;
        }

        if (GameEntry.Fruits != null)
        {
            hatchSeconds = Mathf.Max(0.1f, hatchSeconds * GameEntry.Fruits.GetHatchDurationScale(slotIndex + 1));
        }

        // 槽位里记录蛋 Code，孵化完成后会按蛋配置产出宠物。
        _slotStates[slotIndex].Occupy(eggCode, hatchSeconds);
        NotifyEggSlotsChanged();
    }

    /// <summary>
    /// 将一个蛋追加到库存队尾。
    /// </summary>
    private bool TryAppendEggToBack(string eggCode, QualityType quality)
    {
        if (_manualEggCodes == null
            || _manualEggQualities == null
            || string.IsNullOrWhiteSpace(eggCode)
            || _manualEggCount >= MaxManualEggCount)
        {
            return false;
        }

        _manualEggCodes[_manualEggCount] = eggCode;
        _manualEggQualities[_manualEggCount] = quality;
        _manualEggCount++;
        return true;
    }

    /// <summary>
    /// 检查购买蛋时是否能够插入库存。
    /// </summary>
    private bool CanInsertPurchasedEgg(QualityType quality, out int replaceIndex)
    {
        replaceIndex = -1;
        if (_manualEggCodes == null || _manualEggQualities == null)
        {
            return false;
        }

        if (_manualEggCount < MaxManualEggCount)
        {
            return true;
        }

        if (quality == QualityType.Universal)
        {
            return false;
        }

        for (int i = _manualEggCount - 1; i >= 0; i--)
        {
            if (_manualEggQualities[i] == QualityType.Universal)
            {
                replaceIndex = i;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 将购买到的蛋插入库存队首。
    /// </summary>
    private bool InsertEggAtFront(string eggCode, QualityType quality, int replaceIndex)
    {
        if (_manualEggCodes == null || _manualEggQualities == null || string.IsNullOrWhiteSpace(eggCode))
        {
            return false;
        }

        if (_manualEggCount < MaxManualEggCount)
        {
            for (int i = _manualEggCount; i > 0; i--)
            {
                _manualEggCodes[i] = _manualEggCodes[i - 1];
                _manualEggQualities[i] = _manualEggQualities[i - 1];
            }

            _manualEggCodes[0] = eggCode;
            _manualEggQualities[0] = quality;
            _manualEggCount++;
            return true;
        }

        if (replaceIndex < 0 || replaceIndex >= _manualEggCount)
        {
            return false;
        }

        for (int i = replaceIndex; i > 0; i--)
        {
            _manualEggCodes[i] = _manualEggCodes[i - 1];
            _manualEggQualities[i] = _manualEggQualities[i - 1];
        }

        _manualEggCodes[0] = eggCode;
        _manualEggQualities[0] = quality;
        return true;
    }

    /// <summary>
    /// 从库存队首取出一个可孵化的蛋。
    /// </summary>
    private bool TryDequeueFrontEgg(out string eggCode, out float hatchSeconds)
    {
        eggCode = null;
        hatchSeconds = 0f;
        while (_manualEggCount > 0)
        {
            string currentEggCode = _manualEggCodes[0];
            RemoveEggAt(0);
            if (string.IsNullOrWhiteSpace(currentEggCode))
            {
                continue;
            }

            EggDataRow eggDataRow = GameEntry.DataTables != null
                ? GameEntry.DataTables.GetDataRowByCode<EggDataRow>(currentEggCode)
                : null;
            if (eggDataRow == null || eggDataRow.HatchSeconds <= 0)
            {
                Log.Warning("EggHatchComponent skipped invalid queued egg '{0}'.", currentEggCode);
                continue;
            }

            eggCode = eggDataRow.Code;
            hatchSeconds = eggDataRow.HatchSeconds;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 移除指定库存位的蛋。
    /// </summary>
    private void RemoveEggAt(int index)
    {
        if (_manualEggCodes == null || _manualEggQualities == null || index < 0 || index >= _manualEggCount)
        {
            return;
        }

        for (int i = index; i < _manualEggCount - 1; i++)
        {
            _manualEggCodes[i] = _manualEggCodes[i + 1];
            _manualEggQualities[i] = _manualEggQualities[i + 1];
        }

        int lastIndex = _manualEggCount - 1;
        _manualEggCodes[lastIndex] = null;
        _manualEggQualities[lastIndex] = QualityType.Universal;
        _manualEggCount--;
    }

    /// <summary>
    /// 处理孵化完成后的宠物生成。
    /// 行为分两段：
    /// 　① 餐桌或排队任一有空位 → 走 TryHatchPetFromEggCode（保留从孵化槽出生的移动动画）。
    /// 　② 餐桌+排队都满 → 改走 TryHatchPetFromEggCodeToPlayArea，宠物直接出现在 PlayArea 玩耍，避免蛋丢失。
    /// 这样玩家在自动孵化高峰期（看广告攒满 15 蛋 + 4 个槽并行孵化）也不会出现蛋无声消失的问题。
    /// </summary>
    private static void TrySpawnHatchedPet(EggHatchSlotState slotState, int hatchSlotIndex)
    {
        if (slotState == null || string.IsNullOrWhiteSpace(slotState.EggCode))
        {
            return;
        }

        if (GameEntry.PetPlacement == null)
        {
            Log.Warning("EggHatchComponent can not spawn pet because PetPlacementModule is missing.");
            return;
        }

        // 满员预检：避开 TryHatchPetFromEggCode 内部那条 "餐桌和排队位均已满" 的 Warning 日志风暴。
        // 该接口扫描 _diningSeatInstanceIds + _queueInstanceIds，O(N)、零分配，满足 Zero GC。
        if (GameEntry.PetPlacement.IsDiningAndQueueFull())
        {
            // 满员 fallback：宠物状态进 PlayArea，但 PendingSpawnHatchSlotIndex 仍写当前孵化槽索引，
            // PlayfieldEntityModule 会用它从蛋位置生成实体并播放"走到 PlayArea 目标点"的动画，
            // 视觉与正常孵化一致，避免宠物在 PlayArea 直接瞬现。
            GameEntry.PetPlacement.TryHatchPetFromEggCodeToPlayArea(slotState.EggCode, hatchSlotIndex, out _);
            return;
        }

        GameEntry.PetPlacement.TryHatchPetFromEggCode(slotState.EggCode, hatchSlotIndex, out _);
    }

    /// <summary>
    /// 处理离线期间已经完成孵化的蛋。
    /// 离线恢复不播放孵化槽出生动画，宠物会像云存档恢复宠物一样直接出现在游玩区。
    /// </summary>
    /// <param name="slotState">已经达到完成条件的孵化槽状态。</param>
    private static void TrySpawnOfflineHatchedPetToPlayArea(EggHatchSlotState slotState)
    {
        if (slotState == null || string.IsNullOrWhiteSpace(slotState.EggCode))
        {
            return;
        }

        if (GameEntry.PetPlacement == null)
        {
            Log.Warning("EggHatchComponent can not spawn offline pet because PetPlacementModule is missing.");
            return;
        }

        GameEntry.PetPlacement.TryHatchPetFromEggCodeToPlayArea(slotState.EggCode, out _);
    }

    /// <summary>
    /// 通知显示层刷新孵化槽对应的蛋实体。
    /// </summary>
    private static void NotifyEggSlotsChanged()
    {
        GameEntry.PlayfieldEntities?.NotifyEggStateChanged();
    }

    /// <summary>
    /// 导出手动蛋库存队列。
    /// </summary>
    /// <returns>手动蛋 Code 数组。</returns>
    private string[] ExportManualEggCodes()
    {
        if (_manualEggCount <= 0 || _manualEggCodes == null)
        {
            return Array.Empty<string>();
        }

        string[] results = new string[_manualEggCount];
        for (int i = 0; i < _manualEggCount; i++)
        {
            results[i] = _manualEggCodes[i] ?? string.Empty;
        }

        return results;
    }

    /// <summary>
    /// 导出自动孵化库存队列。
    /// 与手动队列结构一致，老存档若该字段为空数组则反序列化即代表"无自动蛋"。
    /// </summary>
    /// <returns>自动孵化蛋 Code 数组。</returns>
    private string[] ExportAutoEggCodes()
    {
        if (_autoEggCount <= 0)
        {
            return Array.Empty<string>();
        }

        string[] results = new string[_autoEggCount];
        for (int i = 0; i < _autoEggCount; i++)
        {
            results[i] = _autoEggCodes[i] ?? string.Empty;
        }

        return results;
    }

    /// <summary>
    /// 导出固定孵化槽状态。
    /// </summary>
    /// <returns>孵化槽存档数组。</returns>
    private EggHatchSlotSaveData[] ExportSlotSaveData()
    {
        EggHatchSlotSaveData[] results = new EggHatchSlotSaveData[_slotStates.Length];
        for (int i = 0; i < _slotStates.Length; i++)
        {
            EggHatchSlotState slotState = _slotStates[i];
            if (slotState == null || !slotState.IsOccupied || string.IsNullOrWhiteSpace(slotState.EggCode))
            {
                results[i] = new EggHatchSlotSaveData
                {
                    eggCode = string.Empty,
                    totalSeconds = 0f,
                    remainingSeconds = 0f
                };
                continue;
            }

            results[i] = new EggHatchSlotSaveData
            {
                eggCode = slotState.EggCode ?? string.Empty,
                totalSeconds = Mathf.Max(0.1f, slotState.TotalSeconds),
                remainingSeconds = Mathf.Clamp(slotState.RemainingSeconds, 0.1f, Mathf.Max(0.1f, slotState.TotalSeconds))
            };
        }

        return results;
    }

    /// <summary>
    /// 清空当前手动蛋库存。
    /// </summary>
    private void ClearManualEggInventory()
    {
        _manualEggCount = 0;
        if (_manualEggCodes == null || _manualEggQualities == null)
        {
            return;
        }

        for (int i = 0; i < _manualEggCodes.Length; i++)
        {
            _manualEggCodes[i] = null;
            _manualEggQualities[i] = QualityType.Universal;
        }
    }

    /// <summary>
    /// 恢复手动蛋库存队列。
    /// </summary>
    /// <param name="manualEggCodes">云端保存的手动蛋 Code 队列。</param>
    private void RestoreManualEggInventory(string[] manualEggCodes)
    {
        if (manualEggCodes == null || manualEggCodes.Length <= 0)
        {
            return;
        }

        for (int i = 0; i < manualEggCodes.Length && _manualEggCount < MaxManualEggCount; i++)
        {
            string eggCode = manualEggCodes[i];
            EggDataRow eggDataRow = ResolveValidEggDataRow(eggCode);
            if (eggDataRow == null)
            {
                Log.Warning("EggHatchComponent 读取云存档时跳过无效库存蛋 '{0}'。", eggCode);
                continue;
            }

            TryAppendEggToBack(eggDataRow.Code, eggDataRow.Quality);
        }
    }

    /// <summary>
    /// 清空当前自动孵化库存。
    /// 该方法只负责重置队列状态，不派发事件，调用方按需调用 NotifyHatchStateChanged。
    /// </summary>
    private void ClearAutoEggInventory()
    {
        _autoEggCount = 0;
        for (int i = 0; i < _autoEggCodes.Length; i++)
        {
            _autoEggCodes[i] = null;
            _autoEggQualities[i] = QualityType.Universal;
        }
    }

    /// <summary>
    /// 恢复自动孵化库存队列。
    /// </summary>
    /// <param name="autoEggCodes">云端保存的自动孵化蛋 Code 队列。</param>
    private void RestoreAutoEggInventory(string[] autoEggCodes)
    {
        if (autoEggCodes == null || autoEggCodes.Length <= 0)
        {
            return;
        }

        for (int i = 0; i < autoEggCodes.Length && _autoEggCount < MaxAutoEggCountValue; i++)
        {
            string eggCode = autoEggCodes[i];
            EggDataRow eggDataRow = ResolveValidEggDataRow(eggCode);
            if (eggDataRow == null)
            {
                Log.Warning("EggHatchComponent 读取云存档时跳过无效自动孵化蛋 '{0}'。", eggCode);
                continue;
            }

            TryAppendAutoEggToBack(eggDataRow.Code, eggDataRow.Quality);
        }
    }

    /// <summary>
    /// 恢复自动补蛋累计秒数。
    /// </summary>
    /// <param name="refillElapsedSeconds">云端保存的累计秒数。</param>
    private void RestoreRefillElapsedSeconds(float refillElapsedSeconds)
    {
        if (_gameplayRuleDataRow == null || _manualEggCount >= _gameplayRuleDataRow.MaxManualEggCount)
        {
            _refillElapsedSeconds = 0f;
            return;
        }

        _refillElapsedSeconds = Mathf.Clamp(refillElapsedSeconds, 0f, Mathf.Max(0f, _gameplayRuleDataRow.RefillDurationSeconds - 0.01f));
    }

    /// <summary>
    /// 恢复所有孵化槽状态。
    /// </summary>
    /// <param name="slots">云端保存的孵化槽状态数组。</param>
    private void RestoreHatchSlots(EggHatchSlotSaveData[] slots)
    {
        for (int i = 0; i < _slotStates.Length; i++)
        {
            _slotStates[i].Clear();
        }

        if (slots == null || slots.Length <= 0)
        {
            return;
        }

        int unlockedSlotCount = UnlockedSlotCount;
        for (int i = 0; i < slots.Length && i < _slotStates.Length && i < unlockedSlotCount; i++)
        {
            if (GameEntry.Fruits != null && !GameEntry.Fruits.IsArchitectureSlotUnlocked(PlayerRuntimeModule.ArchitectureCategory.Hatch, i + 1))
            {
                continue;
            }

            EggHatchSlotSaveData slotSaveData = slots[i];
            if (slotSaveData == null || string.IsNullOrWhiteSpace(slotSaveData.eggCode))
            {
                continue;
            }

            EggDataRow eggDataRow = ResolveValidEggDataRow(slotSaveData.eggCode);
            if (eggDataRow == null)
            {
                Log.Warning("EggHatchComponent 读取云存档时跳过无效孵化蛋 '{0}'。", slotSaveData.eggCode);
                continue;
            }

            float fallbackTotalSeconds = ResolveScaledHatchSeconds(i, eggDataRow.HatchSeconds);
            float totalSeconds = slotSaveData.totalSeconds > 0f ? slotSaveData.totalSeconds : fallbackTotalSeconds;
            totalSeconds = Mathf.Max(0.1f, totalSeconds);
            float remainingSeconds = Mathf.Clamp(slotSaveData.remainingSeconds, 0.1f, totalSeconds);
            _slotStates[i].Restore(eggDataRow.Code, totalSeconds, remainingSeconds);
        }
    }

    /// <summary>
    /// 解析并校验蛋配置。
    /// </summary>
    /// <param name="eggCode">蛋 Code。</param>
    /// <returns>合法蛋配置；无效时返回 null。</returns>
    private static EggDataRow ResolveValidEggDataRow(string eggCode)
    {
        if (GameEntry.DataTables == null || string.IsNullOrWhiteSpace(eggCode))
        {
            return null;
        }

        EggDataRow eggDataRow = GameEntry.DataTables.GetDataRowByCode<EggDataRow>(eggCode);
        return eggDataRow != null && eggDataRow.HatchSeconds > 0 ? eggDataRow : null;
    }

    /// <summary>
    /// 计算指定槽位受建筑加速影响后的孵化秒数。
    /// </summary>
    /// <param name="slotIndex">0 基孵化槽索引。</param>
    /// <param name="baseHatchSeconds">蛋表基础孵化秒数。</param>
    /// <returns>最终孵化秒数。</returns>
    private static float ResolveScaledHatchSeconds(int slotIndex, float baseHatchSeconds)
    {
        float hatchSeconds = Mathf.Max(0.1f, baseHatchSeconds);
        if (GameEntry.Fruits != null)
        {
            hatchSeconds = Mathf.Max(0.1f, hatchSeconds * GameEntry.Fruits.GetHatchDurationScale(slotIndex + 1));
        }

        return hatchSeconds;
    }

    /// <summary>
    /// 派发孵化运行时离散状态变化事件。
    /// </summary>
    private void NotifyHatchStateChanged()
    {
        HatchStateChanged?.Invoke();
    }
}
