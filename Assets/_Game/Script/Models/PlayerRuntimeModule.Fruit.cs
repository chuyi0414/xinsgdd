using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 玩家运行时模块 — 水果解锁与抽取部分。
/// 负责水果解锁状态管理、候选桶缓存、入座宠物期望水果抽取。
/// </summary>
public sealed partial class PlayerRuntimeModule
{
    // ───────────── 水果字段 ─────────────

    /// <summary>
    /// 当前会话内已解锁的水果 Code 集合。
    /// </summary>
    private readonly HashSet<string> _unlockedFruitCodes = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前已缓存的全部水果行。
    /// </summary>
    private FruitDataRow[] _allFruitRows = Array.Empty<FruitDataRow>();

    /// <summary>
    /// 已解锁候选桶缓存。
    /// </summary>
    private FruitDataRow[] _unlockedFruitCandidates = Array.Empty<FruitDataRow>();

    /// <summary>
    /// 未解锁候选桶缓存。
    /// </summary>
    private FruitDataRow[] _lockedFruitCandidates = Array.Empty<FruitDataRow>();

    /// <summary>
    /// 已解锁候选数量。
    /// </summary>
    private int _unlockedFruitCandidateCount;

    /// <summary>
    /// 未解锁候选数量。
    /// </summary>
    private int _lockedFruitCandidateCount;

    /// <summary>
    /// 候选桶缓存是否需要重建。
    /// </summary>
    private bool _isCandidateCacheDirty = true;

    // ───────────── 水果公共接口 ─────────────

    /// <summary>
    /// 判断指定水果在当前会话内是否已解锁。
    /// </summary>
    /// <param name="fruitCode">水果 Code。</param>
    /// <returns>是否已解锁。</returns>
    public bool IsFruitUnlocked(string fruitCode)
    {
        if (!EnsureInitialized() || string.IsNullOrWhiteSpace(fruitCode))
        {
            return false;
        }

        return _unlockedFruitCodes.Contains(fruitCode);
    }

    /// <summary>
    /// 在当前会话内解锁指定水果。
    /// </summary>
    /// <param name="fruitCode">水果 Code。</param>
    /// <returns>是否成功解锁。</returns>
    public bool TryUnlockFruit(string fruitCode)
    {
        if (!EnsureInitialized() || string.IsNullOrWhiteSpace(fruitCode))
        {
            return false;
        }

        FruitDataRow fruitRow = GameEntry.DataTables.GetDataRowByCode<FruitDataRow>(fruitCode);
        if (fruitRow == null)
        {
            Log.Warning("PlayerRuntimeModule can not unlock fruit because code '{0}' is invalid.", fruitCode);
            return false;
        }

        if (!_unlockedFruitCodes.Add(fruitCode))
        {
            return true;
        }

        _isCandidateCacheDirty = true;
        return true;
    }

    /// <summary>
    /// 原子购买接口：校验数据行存在 → 校验未解锁 → 校验金币充足 → 扣金币 → 解锁水果。
    /// UI 层只需调用此单一接口即可完成完整购买事务，无需自行拆分扣款与解锁。
    /// </summary>
    /// <param name="fruitCode">水果 Code。</param>
    /// <returns>是否购买成功。</returns>
    public bool TryPurchaseFruit(string fruitCode)
    {
        if (!EnsureInitialized() || string.IsNullOrWhiteSpace(fruitCode))
        {
            return false;
        }

        FruitDataRow fruitRow = GameEntry.DataTables.GetDataRowByCode<FruitDataRow>(fruitCode);
        if (fruitRow == null)
        {
            Log.Warning("PlayerRuntimeModule 无法购买水果，编码 '{0}' 无效。", fruitCode);
            return false;
        }

        // 已默认解锁或已运行时解锁的水果不允许重复购买
        if (fruitRow.IsUnlocked || IsFruitUnlocked(fruitCode))
        {
            return false;
        }

        // 解锁金币必须大于 0（数据表校验保证了这一点，此处做防御性检查）
        if (fruitRow.UnlockGold <= 0)
        {
            return false;
        }

        // 扣金币失败说明余额不足
        if (!TryConsumeGold(fruitRow.UnlockGold))
        {
            return false;
        }

        // 扣款成功，执行解锁
        return TryUnlockFruit(fruitCode);
    }

    /// <summary>
    /// 为入座宠物抽取本次期望水果。
    /// </summary>
    /// <param name="fruitDataRow">命中的水果配置行。</param>
    /// <returns>是否抽取成功。</returns>
    public bool TryRollDiningWishFruit(out FruitDataRow fruitDataRow)
    {
        fruitDataRow = null;
        if (!EnsureInitialized() || _gameplayRuleDataRow == null)
        {
            return false;
        }

        RebuildCandidateCachesIfNeeded();

        bool preferUnlocked = UnityEngine.Random.Range(0, FullProbability) < _gameplayRuleDataRow.PreferUnlockedFruitProbability;
        if (TryPickFruitFromBucket(preferUnlocked, out fruitDataRow))
        {
            return true;
        }

        if (TryPickFruitFromBucket(!preferUnlocked, out fruitDataRow))
        {
            return true;
        }

        Log.Warning("PlayerRuntimeModule can not roll dining wish fruit because both candidate buckets are empty.");
        return false;
    }

    // ───────────── 水果内部方法 ─────────────

    /// <summary>
    /// 按指定桶类型挑选一个水果。
    /// </summary>
    /// <param name="pickUnlockedBucket">是否挑选已解锁桶。</param>
    /// <param name="fruitDataRow">命中的水果配置行。</param>
    /// <returns>是否成功命中。</returns>
    private bool TryPickFruitFromBucket(bool pickUnlockedBucket, out FruitDataRow fruitDataRow)
    {
        fruitDataRow = null;

        FruitDataRow[] candidates = pickUnlockedBucket ? _unlockedFruitCandidates : _lockedFruitCandidates;
        int candidateCount = pickUnlockedBucket ? _unlockedFruitCandidateCount : _lockedFruitCandidateCount;
        if (candidates == null || candidateCount <= 0)
        {
            return false;
        }

        int randomIndex = UnityEngine.Random.Range(0, candidateCount);
        fruitDataRow = candidates[randomIndex];
        return fruitDataRow != null;
    }

    /// <summary>
    /// 重建已解锁桶与未解锁桶缓存。
    /// </summary>
    private void RebuildCandidateCachesIfNeeded()
    {
        if (!_isCandidateCacheDirty)
        {
            return;
        }

        _unlockedFruitCandidateCount = 0;
        _lockedFruitCandidateCount = 0;

        for (int i = 0; i < _allFruitRows.Length; i++)
        {
            FruitDataRow fruitRow = _allFruitRows[i];
            if (fruitRow == null || string.IsNullOrWhiteSpace(fruitRow.Code))
            {
                continue;
            }

            if (_unlockedFruitCodes.Contains(fruitRow.Code))
            {
                _unlockedFruitCandidates[_unlockedFruitCandidateCount] = fruitRow;
                _unlockedFruitCandidateCount++;
                continue;
            }

            _lockedFruitCandidates[_lockedFruitCandidateCount] = fruitRow;
            _lockedFruitCandidateCount++;
        }

        _isCandidateCacheDirty = false;
    }
}
