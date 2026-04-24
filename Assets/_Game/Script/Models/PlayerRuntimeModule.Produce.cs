using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 玩家运行时模块 — 产出物库存与抽取部分。
/// 负责宠物产出物的预热、库存管理和按档位概率抽取。
/// </summary>
public sealed partial class PlayerRuntimeModule
{
    // ───────────── 产出物字段 ─────────────

    /// <summary>
    /// 宠物 Code 到宠物 Id 的缓存。
    /// 产出逻辑使用宠物 Id 与 PetProduceDataRow.PetId 对齐。
    /// </summary>
    private readonly Dictionary<string, int> _petIdsByCode = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// 宠物 Id 到三档产出配置的缓存。
    /// </summary>
    private readonly Dictionary<int, PetProduceBucket> _produceBucketsByPetId = new Dictionary<int, PetProduceBucket>();

    /// <summary>
    /// 当前会话内的产出物库存。
    /// Key 为产出物 Code，Value 为当前持有数量。
    /// </summary>
    private readonly Dictionary<string, int> _produceCountsByCode = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// 产出物缓存是否已经完成预热。
    /// </summary>
    private bool _isProduceCatalogInitialized;

    // ───────────── 产出物事件 ─────────────

    /// <summary>
    /// 产出物库存发生变化时触发。
    /// 参数一：产出物 Code。
    /// 参数二：变化后的最新数量。
    /// </summary>
    public event Action<string, int> ProduceChanged;

    // ───────────── 产出物公共接口 ─────────────

    /// <summary>
    /// 预热宠物产出缓存。
    /// 该过程只在加载完成后执行一次，避免运行时重复重建。
    /// </summary>
    /// <returns>是否预热成功。</returns>
    public bool WarmupProduceCatalog()
    {
        if (_isProduceCatalogInitialized)
        {
            return true;
        }

        if (GameEntry.DataTables == null
            || !GameEntry.DataTables.IsAvailable<PetDataRow>()
            || !GameEntry.DataTables.IsAvailable<PetProduceDataRow>())
        {
            Log.Warning("PlayerRuntimeModule 无法预热产出目录，所需数据表不可用。");
            return false;
        }

        PetDataRow[] petRows = GameEntry.DataTables.GetAllDataRows<PetDataRow>();
        PetProduceDataRow[] produceRows = GameEntry.DataTables.GetAllDataRows<PetProduceDataRow>();
        if (petRows == null || petRows.Length == 0 || produceRows == null || produceRows.Length == 0)
        {
            Log.Warning("PlayerRuntimeModule 无法预热产出目录，宠物或产出表为空。");
            return false;
        }

        _petIdsByCode.Clear();
        _produceBucketsByPetId.Clear();
        _produceCountsByCode.Clear();

        for (int i = 0; i < petRows.Length; i++)
        {
            PetDataRow petRow = petRows[i];
            if (petRow == null || string.IsNullOrWhiteSpace(petRow.Code))
            {
                continue;
            }

            _petIdsByCode[petRow.Code] = petRow.Id;
        }

        for (int i = 0; i < produceRows.Length; i++)
        {
            PetProduceDataRow produceRow = produceRows[i];
            if (produceRow == null || string.IsNullOrWhiteSpace(produceRow.Code))
            {
                continue;
            }

            if (!_produceBucketsByPetId.TryGetValue(produceRow.PetId, out PetProduceBucket produceBucket))
            {
                produceBucket = new PetProduceBucket();
                _produceBucketsByPetId.Add(produceRow.PetId, produceBucket);
            }

            switch (produceRow.Grade)
            {
                case ProduceGradeType.Primary:
                    produceBucket.Primary = produceRow;
                    break;

                case ProduceGradeType.Intermediate:
                    produceBucket.Intermediate = produceRow;
                    break;

                case ProduceGradeType.Advanced:
                    produceBucket.Advanced = produceRow;
                    break;
            }

            _produceCountsByCode[produceRow.Code] = 0;
        }

        _isProduceCatalogInitialized = true;
        return true;
    }

    /// <summary>
    /// 为指定产出物增加库存数量。
    /// </summary>
    /// <param name="produceCode">产出物 Code。</param>
    /// <returns>是否成功入库。</returns>
    public bool AddProduce(string produceCode)
    {
        if (!EnsureProduceCatalogInitialized() || string.IsNullOrWhiteSpace(produceCode))
        {
            return false;
        }

        if (!_produceCountsByCode.TryGetValue(produceCode, out int currentCount))
        {
            Log.Warning("PlayerRuntimeModule 无法添加产出物，编码 '{0}' 无效。", produceCode);
            return false;
        }

        currentCount++;
        _produceCountsByCode[produceCode] = currentCount;
        ProduceChanged?.Invoke(produceCode, currentCount);
        return true;
    }

    /// <summary>
    /// 获取指定产出物在当前会话内的库存数量。
    /// </summary>
    /// <param name="produceCode">产出物 Code。</param>
    /// <returns>当前库存数量；无效 Code 返回 0。</returns>
    public int GetProduceCount(string produceCode)
    {
        if (!EnsureProduceCatalogInitialized() || string.IsNullOrWhiteSpace(produceCode))
        {
            return 0;
        }

        return _produceCountsByCode.TryGetValue(produceCode, out int count) ? count : 0;
    }

    /// <summary>
    /// 根据宠物 Code 抽取本次产出物。
    /// </summary>
    /// <param name="petCode">宠物 Code。</param>
    /// <param name="produceDataRow">命中的产出物配置行。</param>
    /// <returns>是否抽取成功。</returns>
    public bool TryRollPetProduce(string petCode, out PetProduceDataRow produceDataRow)
    {
        produceDataRow = null;
        if (!EnsureInitialized() || !EnsureProduceCatalogInitialized() || string.IsNullOrWhiteSpace(petCode))
        {
            return false;
        }

        if (!_petIdsByCode.TryGetValue(petCode, out int petId))
        {
            Log.Warning("PlayerRuntimeModule 无法抽取产出物，宠物编码 '{0}' 无效。", petCode);
            return false;
        }

        if (!_produceBucketsByPetId.TryGetValue(petId, out PetProduceBucket produceBucket) || produceBucket == null)
        {
            Log.Warning("PlayerRuntimeModule 无法抽取产出物，宠物 Id '{0}' 无产出桶。", petId);
            return false;
        }

        produceDataRow = RollProduceByGrade(produceBucket);
        if (produceDataRow == null)
        {
            Log.Warning("PlayerRuntimeModule 无法抽取产出物，宠物 Id '{0}' 的产出桶不完整。", petId);
            return false;
        }

        return true;
    }

    // ───────────── 产出物内部方法 ─────────────

    /// <summary>
    /// 确保产出物缓存已经初始化。
    /// </summary>
    /// <returns>是否可用。</returns>
    private bool EnsureProduceCatalogInitialized()
    {
        return _isProduceCatalogInitialized || WarmupProduceCatalog();
    }

    /// <summary>
    /// 按三档固定概率从缓存桶中抽取一个产出物。
    /// </summary>
    /// <param name="produceBucket">当前宠物的产出桶。</param>
    /// <returns>命中的产出物配置；若桶不完整则返回 null。</returns>
    private PetProduceDataRow RollProduceByGrade(PetProduceBucket produceBucket)
    {
        if (produceBucket == null || _gameplayRuleDataRow == null)
        {
            return null;
        }

        int roll = UnityEngine.Random.Range(0, FullProbability);
        if (roll < _gameplayRuleDataRow.PrimaryProduceProbability)
        {
            return produceBucket.Primary;
        }

        if (roll < _gameplayRuleDataRow.PrimaryProduceProbability + _gameplayRuleDataRow.IntermediateProduceProbability)
        {
            return produceBucket.Intermediate;
        }

        return produceBucket.Advanced;
    }
}
