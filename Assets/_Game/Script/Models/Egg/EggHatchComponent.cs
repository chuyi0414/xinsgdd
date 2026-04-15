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
    /// 当前累计的补蛋进度秒数。
    /// </summary>
    private float _refillElapsedSeconds;

    /// <summary>
    /// 当前组件是否已完成首次初始化。
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// 当前组件是否可用于业务逻辑。
    /// </summary>
    public bool IsAvailable => _isAvailable;

    /// <summary>
    /// 当前手动蛋库存。
    /// </summary>
    public int ManualEggCount => _manualEggCount;

    /// <summary>
    /// 最大手动蛋库存。
    /// </summary>
    public int MaxManualEggCount => _gameplayRuleDataRow != null ? _gameplayRuleDataRow.MaxManualEggCount : 0;

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
        if (!_isInitialized || !_isAvailable)
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
            }

            return;
        }

        if (_gameplayRuleDataRow != null && _manualEggCount < _gameplayRuleDataRow.MaxManualEggCount)
        {
            AccelerateRefill(_gameplayRuleDataRow.ManualReduceSeconds);
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

        for (int i = 0; i < _slotStates.Length; i++)
        {
            _slotStates[i].Clear();
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
        ApplyCompletedRefill();
    }

    /// <summary>
    /// 手动加速补蛋进度。
    /// </summary>
    private void AccelerateRefill(float seconds)
    {
        if (_gameplayRuleDataRow == null || _manualEggCount >= _gameplayRuleDataRow.MaxManualEggCount || seconds <= 0f)
        {
            return;
        }

        _refillElapsedSeconds += seconds;
        ApplyCompletedRefill();
    }

    /// <summary>
    /// 处理补蛋完成结果。
    /// </summary>
    private void ApplyCompletedRefill()
    {
        if (_gameplayRuleDataRow == null)
        {
            return;
        }

        // 允许一次跨过多个 30 秒区间，避免加速时丢失进度余量。
        while (_manualEggCount < _gameplayRuleDataRow.MaxManualEggCount
            && _refillElapsedSeconds >= _gameplayRuleDataRow.RefillDurationSeconds)
        {
            _refillElapsedSeconds -= _gameplayRuleDataRow.RefillDurationSeconds;
            if (!TryAppendEggToBack(_manualEggDataRow.Code, _manualEggDataRow.Quality))
            {
                break;
            }
        }

        if (_manualEggCount >= _gameplayRuleDataRow.MaxManualEggCount)
        {
            _refillElapsedSeconds = 0f;
        }
    }

    /// <summary>
    /// 查找最左侧空槽位。
    /// </summary>
    private bool TryGetEmptySlotIndex(out int slotIndex)
    {
        int unlockedSlotCount = UnlockedSlotCount;
        for (int i = 0; i < unlockedSlotCount; i++)
        {
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

        GameEntry.PetPlacement.TryHatchPetFromEggCode(slotState.EggCode, hatchSlotIndex, out _);
    }

    /// <summary>
    /// 通知显示层刷新孵化槽对应的蛋实体。
    /// </summary>
    private static void NotifyEggSlotsChanged()
    {
        GameEntry.PlayfieldEntities?.NotifyEggStateChanged();
    }
}
