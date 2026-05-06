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
    /// 已解锁但排除最高水果后的候选桶缓存。
    /// GameplayRule 中 DiningOtherUnlockedFruitProbability 概率从此桶随机抽取。
    /// </summary>
    private FruitDataRow[] _otherUnlockedFruitCandidates = Array.Empty<FruitDataRow>();

    /// <summary>
    /// 当前最高已解锁水果（Id 最大的已解锁水果）。
    /// GameplayRule 中 DiningHighestUnlockedFruitProbability 概率直接命中此水果。
    /// </summary>
    private FruitDataRow _highestUnlockedFruit;

    /// <summary>
    /// 已解锁候选数量。
    /// </summary>
    private int _unlockedFruitCandidateCount;

    /// <summary>
    /// 未解锁候选数量。
    /// </summary>
    private int _lockedFruitCandidateCount;

    /// <summary>
    /// 排除最高水果后的已解锁候选数量。
    /// </summary>
    private int _otherUnlockedFruitCandidateCount;

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

        // 首次解锁发星：仅 RewardStars > 0 才调 AddStars（同为 partial 内部可见，无须改可见性）。
        // 故意放在 TryUnlockFruit 而非 TryPurchaseFruit，让未来"看广告解锁/任务奖励解锁"等路径
        // 直接调 TryUnlockFruit 也能自动发星，单点收口。
        // 默认解锁的水果走 InitializeFruitCatalog 直接 _unlockedFruitCodes.Add 不会到这里，
        // 算上 FruitDataRow 校验"IsUnlocked=true → RewardStars=0"，双保险阻止默认解锁误发星。
        if (fruitRow.RewardStars > 0)
        {
            AddStars(fruitRow.RewardStars);
        }

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

        // 解锁金币防御：禁止负数（数据表已经挡住，这里二次保险）；允许 0（免费解锁）。
        if (fruitRow.UnlockGold < 0)
        {
            return false;
        }

        // UnlockGold > 0 走金币购买路径：扣金币失败说明余额不足，整个事务终止。
        // UnlockGold == 0 直接跳过扣费，进入 TryUnlockFruit；保持发星与解锁集合写入语义统一。
        if (fruitRow.UnlockGold > 0 && !TryConsumeGold(fruitRow.UnlockGold))
        {
            return false;
        }

        // 扣款成功（或免费），执行解锁；TryUnlockFruit 内部按 RewardStars 自动发星。
        return TryUnlockFruit(fruitCode);
    }

    /// <summary>
    /// 为入座宠物抽取本次期望水果。
    /// 三档概率从 GameplayRule.txt 读取：
    /// DiningHighestUnlockedFruitProbability → 最高已解锁；
    /// DiningOtherUnlockedFruitProbability → 其他已解锁（排除最高）；
    /// DiningLockedFruitProbability → 未解锁。
    /// 每档命中后若桶为空则逐级降级，确保总能抽到水果。
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

        int roll = UnityEngine.Random.Range(0, FullProbability);
        int highestUnlockedProbability = _gameplayRuleDataRow.DiningHighestUnlockedFruitProbability;
        int otherUnlockedProbability = _gameplayRuleDataRow.DiningOtherUnlockedFruitProbability;
        int lockedFruitProbability = _gameplayRuleDataRow.DiningLockedFruitProbability;
        int otherUnlockedThreshold = highestUnlockedProbability + otherUnlockedProbability;
        int lockedFruitThreshold = otherUnlockedThreshold + lockedFruitProbability;

        // ── 配置概率 1：直接命中当前最高已解锁水果 ──
        if (roll < highestUnlockedProbability)
        {
            if (_highestUnlockedFruit != null)
            {
                fruitDataRow = _highestUnlockedFruit;
                return true;
            }

            // 最高桶空，降级到其他已解锁桶
            if (TryPickOtherUnlockedFruit(out fruitDataRow))
            {
                return true;
            }

            // 再降级到未解锁桶
            return TryPickFruitFromBucket(false, out fruitDataRow);
        }

        // ── 配置概率 2：从其他已解锁水果（排除最高）中随机抽 ──
        if (roll < otherUnlockedThreshold)
        {
            if (TryPickOtherUnlockedFruit(out fruitDataRow))
            {
                return true;
            }

            // 其他已解锁桶空，降级到最高桶
            if (_highestUnlockedFruit != null)
            {
                fruitDataRow = _highestUnlockedFruit;
                return true;
            }

            // 再降级到未解锁桶
            return TryPickFruitFromBucket(false, out fruitDataRow);
        }

        // ── 配置概率 3：从未解锁水果中随机抽 ──
        if (roll < lockedFruitThreshold && TryPickFruitFromBucket(false, out fruitDataRow))
        {
            return true;
        }

        // 未解锁桶空，降级到最高桶
        if (_highestUnlockedFruit != null)
        {
            fruitDataRow = _highestUnlockedFruit;
            return true;
        }

        // 最终降级到其他已解锁桶
        return TryPickOtherUnlockedFruit(out fruitDataRow);
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
    /// 从排除最高水果后的已解锁候选桶中随机抽取一个。
    /// </summary>
    /// <param name="fruitDataRow">命中的水果配置行。</param>
    /// <returns>是否成功命中。</returns>
    private bool TryPickOtherUnlockedFruit(out FruitDataRow fruitDataRow)
    {
        fruitDataRow = null;
        if (_otherUnlockedFruitCandidateCount <= 0)
        {
            return false;
        }

        int randomIndex = UnityEngine.Random.Range(0, _otherUnlockedFruitCandidateCount);
        fruitDataRow = _otherUnlockedFruitCandidates[randomIndex];
        return fruitDataRow != null;
    }

    /// <summary>
    /// 重建已解锁桶、排除最高桶、未解锁桶缓存，并追踪最高已解锁水果。
    /// </summary>
    private void RebuildCandidateCachesIfNeeded()
    {
        if (!_isCandidateCacheDirty)
        {
            return;
        }

        _unlockedFruitCandidateCount = 0;
        _lockedFruitCandidateCount = 0;
        _otherUnlockedFruitCandidateCount = 0;
        _highestUnlockedFruit = null;

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

                // 追踪最高已解锁水果（Id 最大者）
                if (_highestUnlockedFruit == null || fruitRow.Id > _highestUnlockedFruit.Id)
                {
                    _highestUnlockedFruit = fruitRow;
                }

                continue;
            }

            _lockedFruitCandidates[_lockedFruitCandidateCount] = fruitRow;
            _lockedFruitCandidateCount++;
        }

        // 构建"排除最高的其他已解锁"候选桶
        for (int i = 0; i < _unlockedFruitCandidateCount; i++)
        {
            if (_unlockedFruitCandidates[i] != _highestUnlockedFruit)
            {
                _otherUnlockedFruitCandidates[_otherUnlockedFruitCandidateCount] = _unlockedFruitCandidates[i];
                _otherUnlockedFruitCandidateCount++;
            }
        }

        _isCandidateCacheDirty = false;
    }
}
