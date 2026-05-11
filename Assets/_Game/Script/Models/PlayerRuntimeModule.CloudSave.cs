using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 玩家运行时模块 — 云存档导出与应用部分。
/// 只处理玩家长期进度，不处理宠物动画状态、果园生产状态和 UI 掉落物状态。
/// </summary>
public sealed partial class PlayerRuntimeModule
{
    /// <summary>
    /// 将当前玩家长期进度写入云存档快照。
    /// 调用方负责继续补充宠物轻量数据和未点击掉落物数据。
    /// </summary>
    /// <param name="snapshot">待写入的玩家云存档快照。</param>
    /// <returns>成功写入返回 true；初始化失败或参数为空时返回 false。</returns>
    public bool ExportPlayerCloudSaveSnapshot(PlayerCloudSaveSnapshot snapshot)
    {
        if (snapshot == null || !EnsureInitialized() || !EnsureProduceCatalogInitialized())
        {
            return false;
        }

        snapshot.currentGold = _currentGold;
        snapshot.currentStars = _currentStars;
        snapshot.hasClaimedNewcomerPackage = _hasClaimedNewcomerPackage;
        snapshot.playerName = _playerName ?? string.Empty;
        snapshot.playerCode = _playerCode ?? string.Empty;
        snapshot.dailyChallengeHistoricalBestScore = _dailyChallengeHistoricalBestScore;
        snapshot.dailyChallengeHistoricalBestTime = _dailyChallengeHistoricalBestTime ?? string.Empty;
        snapshot.selectedHeadPortraitCode = _selectedHeadPortraitCode ?? string.Empty;
        snapshot.selectedHeadPortraitFrameCode = _selectedHeadPortraitFrameCode ?? string.Empty;
        snapshot.unlockedFruitCodes = CopyStringSetToArray(_unlockedFruitCodes);
        snapshot.unlockedPetCodes = CopyStringSetToArray(_unlockedPetCodes);
        snapshot.unlockedProduceCodes = CopyStringSetToArray(_unlockedProduceCodes);
        snapshot.unlockedHeadPortraitCodes = CopyStringSetToArray(_unlockedHeadPortraitCodes);
        snapshot.unlockedHeadPortraitFrameCodes = CopyStringSetToArray(_unlockedHeadPortraitFrameCodes);
        snapshot.produceCounts = ExportProduceCounts();
        snapshot.architectures = ExportArchitectureStates();
        return true;
    }

    /// <summary>
    /// 将云端玩家长期进度应用到当前运行时模块。
    /// 该方法会覆盖金币、星星、图鉴、库存和建筑状态，并广播对应 UI 刷新事件。
    /// </summary>
    /// <param name="snapshot">云端玩家快照。</param>
    /// <returns>成功应用返回 true；快照为空或初始化失败时返回 false。</returns>
    public bool ApplyPlayerCloudSaveSnapshot(PlayerCloudSaveSnapshot snapshot)
    {
        if (snapshot == null || !EnsureInitialized() || !EnsureProduceCatalogInitialized())
        {
            return false;
        }

        _currentGold = Mathf.Max(0, snapshot.currentGold);
        _currentStars = Mathf.Max(0, snapshot.currentStars);
        _hasClaimedNewcomerPackage = snapshot.hasClaimedNewcomerPackage;
        _playerName = snapshot.playerName ?? string.Empty;
        _playerCode = snapshot.playerCode ?? string.Empty;
        _dailyChallengeHistoricalBestScore = Mathf.Max(0, snapshot.dailyChallengeHistoricalBestScore);
        _dailyChallengeHistoricalBestTime = snapshot.dailyChallengeHistoricalBestTime ?? string.Empty;
        ApplyStringSet(snapshot.unlockedFruitCodes, _unlockedFruitCodes, IsValidFruitCode);
        ApplyStringSet(snapshot.unlockedPetCodes, _unlockedPetCodes, IsValidPetCode);
        ApplyStringSet(snapshot.unlockedProduceCodes, _unlockedProduceCodes, IsValidProduceCode);
        ApplyStringSet(snapshot.unlockedHeadPortraitCodes, _unlockedHeadPortraitCodes, IsValidHeadPortraitCode);
        ApplyStringSet(snapshot.unlockedHeadPortraitFrameCodes, _unlockedHeadPortraitFrameCodes, IsValidHeadPortraitFrameCode);
        ApplySelectedCosmetics(snapshot.selectedHeadPortraitCode, snapshot.selectedHeadPortraitFrameCode);
        ApplyProduceCounts(snapshot.produceCounts);
        ApplyArchitectureStates(snapshot.architectures);
        _isCandidateCacheDirty = true;
        RebuildCandidateCachesIfNeeded();
        GoldChanged?.Invoke(_currentGold);
        StarsChanged?.Invoke(_currentStars);
        NotifyAllProduceCountsChanged();
        ArchitectureStateChanged?.Invoke();
        NotifyPlayfieldCapacityChanged();
        GameEntry.PlayfieldEntities?.EnsureCapacity(_diningSeatCount, _orchardSlotCount);
        GameEntry.PlayfieldEntities?.NotifyEggStateChanged();
        return true;
    }

    /// <summary>
    /// 从字符串集合复制出数组。
    /// 云存档构建发生在低频保存路径，可以接受一次性数组分配。
    /// </summary>
    /// <param name="source">源集合。</param>
    /// <returns>复制后的字符串数组。</returns>
    private static string[] CopyStringSetToArray(HashSet<string> source)
    {
        if (source == null || source.Count <= 0)
        {
            return Array.Empty<string>();
        }

        string[] results = new string[source.Count];
        int index = 0;
        foreach (string value in source)
        {
            results[index++] = value ?? string.Empty;
        }

        Array.Sort(results, StringComparer.Ordinal);
        return results;
    }

    /// <summary>
    /// 导出当前产出物库存。
    /// 只保存数量大于 0 的条目，数量为 0 的条目读取时由产出物目录默认初始化。
    /// </summary>
    /// <returns>产出物库存数组。</returns>
    private ProduceCountSaveData[] ExportProduceCounts()
    {
        if (_produceCountsByCode.Count <= 0)
        {
            return Array.Empty<ProduceCountSaveData>();
        }

        List<ProduceCountSaveData> results = new List<ProduceCountSaveData>(_produceCountsByCode.Count);
        foreach (KeyValuePair<string, int> pair in _produceCountsByCode)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value <= 0)
            {
                continue;
            }

            results.Add(new ProduceCountSaveData
            {
                code = pair.Key ?? string.Empty,
                count = pair.Value
            });
        }

        return results.ToArray();
    }

    /// <summary>
    /// 导出四类建筑槽位状态。
    /// </summary>
    /// <returns>建筑类别状态数组。</returns>
    private ArchitectureCategorySaveData[] ExportArchitectureStates()
    {
        return new[]
        {
            ExportArchitectureCategory(ArchitectureCategory.Hatch, _hatchArchitectureStates),
            ExportArchitectureCategory(ArchitectureCategory.Diet, _dietArchitectureStates),
            ExportArchitectureCategory(ArchitectureCategory.Fruiter, _fruiterArchitectureStates),
            ExportArchitectureCategory(ArchitectureCategory.SavingPot, _savingPotArchitectureStates)
        };
    }

    /// <summary>
    /// 导出单个建筑类别的槽位状态。
    /// </summary>
    /// <param name="category">建筑类别。</param>
    /// <param name="slotStates">该类别的运行时槽位数组。</param>
    /// <returns>该类别的存档数据。</returns>
    private static ArchitectureCategorySaveData ExportArchitectureCategory(ArchitectureCategory category, ArchitectureSlotState[] slotStates)
    {
        int length = slotStates != null ? slotStates.Length : 0;
        ArchitectureSlotSaveData[] savedSlots = new ArchitectureSlotSaveData[length];
        for (int i = 0; i < length; i++)
        {
            ArchitectureSlotState slotState = slotStates[i];
            savedSlots[i] = new ArchitectureSlotSaveData
            {
                isUnlocked = slotState != null && slotState.IsUnlocked,
                level = slotState != null ? slotState.Level : 0
            };
        }

        return new ArchitectureCategorySaveData
        {
            category = category.ToString() ?? string.Empty,
            slots = savedSlots
        };
    }

    /// <summary>
    /// 应用字符串数组到目标集合。
    /// 只有通过合法性校验的 Code 才会写入，避免云端脏数据污染运行时集合。
    /// </summary>
    /// <param name="values">云端保存的 Code 数组。</param>
    /// <param name="target">目标运行时集合。</param>
    /// <param name="isValidCode">Code 合法性检查函数。</param>
    private static void ApplyStringSet(string[] values, HashSet<string> target, Func<string, bool> isValidCode)
    {
        if (values == null || target == null || isValidCode == null)
        {
            return;
        }

        target.Clear();
        for (int i = 0; i < values.Length; i++)
        {
            string value = values[i];
            if (string.IsNullOrWhiteSpace(value) || !isValidCode(value))
            {
                continue;
            }

            target.Add(value);
        }
    }

    /// <summary>
    /// 应用头像与头像框选中状态。
    /// 如果云端选择项为空或未解锁，则保留当前默认选择，避免 UI 展示空头像。
    /// </summary>
    /// <param name="headPortraitCode">云端头像 Code。</param>
    /// <param name="headPortraitFrameCode">云端头像框 Code。</param>
    private void ApplySelectedCosmetics(string headPortraitCode, string headPortraitFrameCode)
    {
        if (!string.IsNullOrWhiteSpace(headPortraitCode) && _unlockedHeadPortraitCodes.Contains(headPortraitCode))
        {
            _selectedHeadPortraitCode = headPortraitCode;
        }

        if (!string.IsNullOrWhiteSpace(headPortraitFrameCode) && _unlockedHeadPortraitFrameCodes.Contains(headPortraitFrameCode))
        {
            _selectedHeadPortraitFrameCode = headPortraitFrameCode;
        }
    }

    /// <summary>
    /// 应用产出物库存。
    /// 先把目录中全部已知产出物重置为 0，再按云端数量覆盖。
    /// </summary>
    /// <param name="produceCounts">云端产出物库存数组。</param>
    private void ApplyProduceCounts(ProduceCountSaveData[] produceCounts)
    {
        List<string> keys = new List<string>(_produceCountsByCode.Keys);
        for (int i = 0; i < keys.Count; i++)
        {
            _produceCountsByCode[keys[i]] = 0;
        }

        if (produceCounts == null)
        {
            return;
        }

        for (int i = 0; i < produceCounts.Length; i++)
        {
            ProduceCountSaveData saveData = produceCounts[i];
            if (saveData == null || string.IsNullOrWhiteSpace(saveData.code) || saveData.count <= 0)
            {
                continue;
            }

            if (_produceCountsByCode.ContainsKey(saveData.code))
            {
                _produceCountsByCode[saveData.code] = saveData.count;
            }
        }
    }

    /// <summary>
    /// 应用四类建筑槽位状态，并根据解锁槽位重算孵化、餐桌和果树容量。
    /// </summary>
    /// <param name="architectures">云端建筑状态数组。</param>
    private void ApplyArchitectureStates(ArchitectureCategorySaveData[] architectures)
    {
        if (architectures == null)
        {
            RecalculateArchitectureCapacitiesFromStates();
            return;
        }

        for (int i = 0; i < architectures.Length; i++)
        {
            ArchitectureCategorySaveData categorySaveData = architectures[i];
            if (categorySaveData == null || string.IsNullOrWhiteSpace(categorySaveData.category))
            {
                continue;
            }

            if (!Enum.TryParse(categorySaveData.category, out ArchitectureCategory category))
            {
                continue;
            }

            ApplyArchitectureCategory(category, categorySaveData.slots);
        }

        RecalculateArchitectureCapacitiesFromStates();
    }

    /// <summary>
    /// 应用单个建筑类别的槽位状态。
    /// 等级会被限制在当前配置允许的最大等级内，防止云端旧数据越界。
    /// </summary>
    /// <param name="category">建筑类别。</param>
    /// <param name="slots">云端槽位状态数组。</param>
    private void ApplyArchitectureCategory(ArchitectureCategory category, ArchitectureSlotSaveData[] slots)
    {
        ArchitectureSlotState[] targetStates = GetArchitectureSlotStates(category);
        if (targetStates == null || slots == null)
        {
            return;
        }

        int count = Mathf.Min(targetStates.Length, slots.Length);
        for (int i = 0; i < count; i++)
        {
            ArchitectureSlotState targetState = targetStates[i];
            ArchitectureSlotSaveData slotSaveData = slots[i];
            if (targetState == null || slotSaveData == null)
            {
                continue;
            }

            int slotIndex = i + 1;
            targetState.IsUnlocked = slotSaveData.isUnlocked;
            targetState.Level = targetState.IsUnlocked
                ? Mathf.Clamp(slotSaveData.level, InitialArchitectureLevel, GetMaxArchitectureLevel(category, slotIndex))
                : 0;
        }
    }

    /// <summary>
    /// 根据当前建筑槽位解锁状态重算三类物理容量。
    /// 使用“已解锁最大槽位索引”作为容量，兼容跳购造成的中间锁位。
    /// </summary>
    private void RecalculateArchitectureCapacitiesFromStates()
    {
        _hatchSlotCount = Mathf.Max(FallbackInitialUnlockedSlotCount, GetMaxUnlockedSlotIndex(_hatchArchitectureStates));
        _diningSeatCount = Mathf.Max(FallbackInitialUnlockedSlotCount, GetMaxUnlockedSlotIndex(_dietArchitectureStates));
        _orchardSlotCount = Mathf.Max(FallbackInitialUnlockedSlotCount, GetMaxUnlockedSlotIndex(_fruiterArchitectureStates));
        GameEntry.PetPlacement?.EnsureDiningSeatCapacity(_diningSeatCount);
        GameEntry.Orchards?.EnsureSlotCapacity(_orchardSlotCount);
    }

    /// <summary>
    /// 获取指定建筑数组中已解锁的最大 1 基槽位索引。
    /// </summary>
    /// <param name="slotStates">建筑槽位状态数组。</param>
    /// <returns>已解锁最大索引；没有解锁槽位时返回 0。</returns>
    private static int GetMaxUnlockedSlotIndex(ArchitectureSlotState[] slotStates)
    {
        if (slotStates == null)
        {
            return 0;
        }

        for (int i = slotStates.Length - 1; i >= 0; i--)
        {
            ArchitectureSlotState slotState = slotStates[i];
            if (slotState != null && slotState.IsUnlocked)
            {
                return i + 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// 广播所有产出物当前库存数量。
    /// 用于云存档读取后刷新可能已经打开的 UI。
    /// </summary>
    private void NotifyAllProduceCountsChanged()
    {
        foreach (KeyValuePair<string, int> pair in _produceCountsByCode)
        {
            ProduceChanged?.Invoke(pair.Key, pair.Value);
        }
    }

    /// <summary>
    /// 获取指定建筑类别对应的内部状态数组。
    /// </summary>
    /// <param name="category">建筑类别。</param>
    /// <returns>该类别的槽位状态数组。</returns>
    private ArchitectureSlotState[] GetArchitectureSlotStates(ArchitectureCategory category)
    {
        switch (category)
        {
            case ArchitectureCategory.Hatch:
                return _hatchArchitectureStates;

            case ArchitectureCategory.Diet:
                return _dietArchitectureStates;

            case ArchitectureCategory.Fruiter:
                return _fruiterArchitectureStates;

            case ArchitectureCategory.SavingPot:
                return _savingPotArchitectureStates;

            default:
                return null;
        }
    }

    /// <summary>
    /// 检查水果 Code 是否存在于当前数据表。
    /// </summary>
    /// <param name="code">待检查 Code。</param>
    /// <returns>存在返回 true。</returns>
    private static bool IsValidFruitCode(string code)
    {
        return GameEntry.DataTables != null && GameEntry.DataTables.GetDataRowByCode<FruitDataRow>(code) != null;
    }

    /// <summary>
    /// 检查宠物 Code 是否存在于当前数据表。
    /// </summary>
    /// <param name="code">待检查 Code。</param>
    /// <returns>存在返回 true。</returns>
    private static bool IsValidPetCode(string code)
    {
        return GameEntry.DataTables != null && GameEntry.DataTables.GetDataRowByCode<PetDataRow>(code) != null;
    }

    /// <summary>
    /// 检查产出物 Code 是否存在于当前产出物目录。
    /// </summary>
    /// <param name="code">待检查 Code。</param>
    /// <returns>存在返回 true。</returns>
    private bool IsValidProduceCode(string code)
    {
        return !string.IsNullOrWhiteSpace(code) && _produceCountsByCode.ContainsKey(code);
    }

    /// <summary>
    /// 检查头像 Code 是否存在于当前数据表。
    /// </summary>
    /// <param name="code">待检查 Code。</param>
    /// <returns>存在返回 true。</returns>
    private static bool IsValidHeadPortraitCode(string code)
    {
        return GameEntry.DataTables != null && GameEntry.DataTables.GetDataRowByCode<HeadPortraitDataRow>(code) != null;
    }

    /// <summary>
    /// 检查头像框 Code 是否存在于当前数据表。
    /// </summary>
    /// <param name="code">待检查 Code。</param>
    /// <returns>存在返回 true。</returns>
    private static bool IsValidHeadPortraitFrameCode(string code)
    {
        return GameEntry.DataTables != null && GameEntry.DataTables.GetDataRowByCode<HeadPortraitFrameDataRow>(code) != null;
    }
}
