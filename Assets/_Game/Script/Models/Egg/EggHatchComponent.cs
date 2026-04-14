using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 蛋孵化统一运行时组件。
/// 统一管理手动蛋库存、补蛋进度和孵化槽位状态。
/// </summary>
public sealed class EggHatchComponent : GameFrameworkComponent
{
    // 首次启动时默认赠送 6 个万能蛋。
    private const int InitialManualEggCountValue = 6;
    // 手动蛋库存上限，补蛋达到上限后停止推进。
    private const int MaxManualEggCountValue = 6;
    // 满槽时点击手动孵化按钮，直接给补蛋进度减去 2 秒。
    private const float ManualReduceSeconds = 2f;
    // 自动补 1 个万能蛋所需时间。
    private const float RefillDurationSeconds = 30f;
    // 当前主界面固定摆放 4 个孵化槽位，但运行时可购买数量由 PlayerRuntimeModule 决定。
    private const int HatchSlotCountValue = 4;
    // 这一版只接万能蛋的手动孵化闭环。
    private const string UniversalEggCode = "egg_universal";

    /// <summary>
    /// 固定数量的孵化槽运行时状态集合。
    /// </summary>
    private readonly EggHatchSlotState[] _slotStates = new EggHatchSlotState[HatchSlotCountValue];

    /// <summary>
    /// 万能蛋配置缓存。
    /// </summary>
    private EggDataRow _universalEggDataRow;

    /// <summary>
    /// 是否已完成首次初始化。
    /// </summary>
    private bool _isInitialized;

    /// <summary>
    /// 当前组件是否处于可用状态。
    /// </summary>
    private bool _isAvailable;

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
    public int MaxManualEggCount => MaxManualEggCountValue;

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

            if (_manualEggCount >= MaxManualEggCountValue)
            {
                return 1f;
            }

            return Mathf.Clamp01(_refillElapsedSeconds / RefillDurationSeconds);
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
            return (hasEmptySlot && _manualEggCount > 0) || _manualEggCount < MaxManualEggCountValue;
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

        // 运行时状态依赖蛋表中的万能蛋配置，缺表时直接禁用模块。
        if (GameEntry.DataTables == null || !GameEntry.DataTables.IsAvailable<EggDataRow>())
        {
            Log.Error("EggHatchComponent initialize failed because EggDataTable is unavailable.");
            _isAvailable = false;
            return;
        }

        _universalEggDataRow = GameEntry.DataTables.GetDataRowByCode<EggDataRow>(UniversalEggCode);
        if (_universalEggDataRow == null)
        {
            Log.Error("EggHatchComponent initialize failed because '{0}' can not be found.", UniversalEggCode);
            _isAvailable = false;
            return;
        }

        if (_universalEggDataRow.HatchSeconds <= 0)
        {
            Log.Error("EggHatchComponent initialize failed because universal egg hatch seconds is invalid.");
            _isAvailable = false;
            return;
        }

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
            OccupySlot(emptySlotIndex);
            _manualEggCount--;
            return;
        }

        if (_manualEggCount < MaxManualEggCountValue)
        {
            AccelerateRefill(ManualReduceSeconds);
        }
    }

    /// <summary>
    /// 重置运行时状态。
    /// </summary>
    private void ResetRuntimeState()
    {
        _manualEggCount = InitialManualEggCountValue;
        _refillElapsedSeconds = 0f;

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
        if (_manualEggCount >= MaxManualEggCountValue)
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
        if (_manualEggCount >= MaxManualEggCountValue || seconds <= 0f)
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
        // 允许一次跨过多个 30 秒区间，避免加速时丢失进度余量。
        while (_manualEggCount < MaxManualEggCountValue && _refillElapsedSeconds >= RefillDurationSeconds)
        {
            _refillElapsedSeconds -= RefillDurationSeconds;
            _manualEggCount++;
        }

        if (_manualEggCount >= MaxManualEggCountValue)
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
    private void OccupySlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slotStates.Length)
        {
            return;
        }

        // 槽位里记录蛋 Code，孵化完成后会按蛋配置产出宠物。
        _slotStates[slotIndex].Occupy(_universalEggDataRow.Code, _universalEggDataRow.HatchSeconds);
        NotifyEggSlotsChanged();
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
