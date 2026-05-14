using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 玩家运行时模块 — 产出物库存与抽取部分。
/// 负责宠物产出物的预热、库存管理和按权重抽取。
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
    /// 宠物 Id 到产出随机池的缓存。
    /// 三档抽取已废弃，现为在同 PetId 的池子里按 PetProduce.Weight 权重随机挑 1 条。
    /// </summary>
    private readonly Dictionary<int, PetProducePool> _producePoolsByPetId = new Dictionary<int, PetProducePool>();

    /// <summary>
    /// 当前会话内的产出物库存。
    /// Key 为产出物 Code，Value 为当前持有数量。
    /// </summary>
    private readonly Dictionary<string, int> _produceCountsByCode = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// produceCode → PetProduceDataRow 反查缓存。
    /// 仅在 WarmupProduceCatalog 一次性填入，运行时 AddProduce 走这里取 RewardStars，避免重复调 GetDataRowByCode 走哈表。
    /// </summary>
    private readonly Dictionary<string, PetProduceDataRow> _produceRowsByCode = new Dictionary<string, PetProduceDataRow>(StringComparer.Ordinal);

    /// <summary>
    /// 当前会话内已经首次拾取过（已解锁）的产出物 Code 集合。
    /// 该集合只为“首次拾取发星”语义服务：首次拾取时拿星并加入集合；后续拾取同 code 仅入库不发星。
    /// 与 _currentGold / _currentStars 同口径仅存运行时；重启后会重置，未来接入存档时需同步落盘。
    /// </summary>
    private readonly HashSet<string> _unlockedProduceCodes = new HashSet<string>(StringComparer.Ordinal);

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
        _producePoolsByPetId.Clear();
        _produceCountsByCode.Clear();
        _produceRowsByCode.Clear();
        _unlockedProduceCodes.Clear();

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

            // 同 PetId 的产出物全部丢进同一个随机池，Grade 运行时不再读取。
            // 字段仅作策划备注保留在 PetProduceDataRow.Grade；真实二次抽取概率由 Weight / TotalWeight 决定。
            if (!_producePoolsByPetId.TryGetValue(produceRow.PetId, out PetProducePool producePool))
            {
                producePool = new PetProducePool();
                _producePoolsByPetId.Add(produceRow.PetId, producePool);
            }

            producePool.Items.Add(produceRow);
            producePool.TotalWeight += produceRow.Weight;
            _produceCountsByCode[produceRow.Code] = 0;
            // 同时写入反查表，让 AddProduce 发星星走 O(1) 字典路径，满足 Zero GC。
            _produceRowsByCode[produceRow.Code] = produceRow;
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

        if (!_produceCountsByCode.ContainsKey(produceCode))
        {
            Log.Warning("PlayerRuntimeModule 无法添加产出物，编码 '{0}' 无效。", produceCode);
            return false;
        }

        _produceRowsByCode.TryGetValue(produceCode, out PetProduceDataRow produceRow);
        UnlockProduceIfNeeded(produceCode, produceRow);
        return true;
    }

    /// <summary>
    /// 直接卖出指定产出物并把 PetProduce.txt 中配置的金币价值加入玩家金币。
    /// 该路径不会增加产出物库存，只保留首次拾取解锁与发星逻辑。
    /// </summary>
    /// <param name="produceCode">产出物 Code。</param>
    /// <returns>卖出成功返回 true。</returns>
    public bool SellProduceForGold(string produceCode)
    {
        if (!EnsureProduceCatalogInitialized() || string.IsNullOrWhiteSpace(produceCode))
        {
            return false;
        }

        if (!_produceRowsByCode.TryGetValue(produceCode, out PetProduceDataRow produceRow) || produceRow == null)
        {
            Log.Warning("PlayerRuntimeModule 无法卖出产出物，编码 '{0}' 无效。", produceCode);
            return false;
        }

        UnlockProduceIfNeeded(produceCode, produceRow);
        AddGold(produceRow.CoinValue);
        return true;
    }

    /// <summary>
    /// 查询指定产出物在当前会话内是否已经首次拾取过（已解锁）。
    /// 供后续图鉴 / 首解提示类 UI 查询使用；运行时全部返回 O(1) 哈表查询。
    /// </summary>
    /// <param name="produceCode">产出物 Code。</param>
    /// <returns>已解锁返回 true；未拾取过或者 code 非法返回 false。</returns>
    public bool IsProduceUnlocked(string produceCode)
    {
        if (string.IsNullOrWhiteSpace(produceCode))
        {
            return false;
        }

        return _unlockedProduceCodes.Contains(produceCode);
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

        if (!_producePoolsByPetId.TryGetValue(petId, out PetProducePool producePool)
            || producePool == null
            || producePool.Items.Count == 0
            || producePool.TotalWeight <= 0)
        {
            Log.Warning("PlayerRuntimeModule 无法抽取产出物，宠物 Id '{0}' 无产出池。", petId);
            return false;
        }

        // 同 PetId 产出池中按 Weight 权重随机抽 1 条。
        // 例：三条权重 20/51/11，则分母为 82，对应概率为 20/82、51/82、11/82；
        // 若删掉第三条，分母自然变为 71，对应概率为 20/71、51/71。
        // 这里不额外构建累计数组，避免 Warmup 之外再维护一份重复内存；单宠物产出条目规模很小，线性扣减足够稳定。
        int roll = UnityEngine.Random.Range(0, producePool.TotalWeight);
        int accumulatedWeight = 0;
        for (int i = 0; i < producePool.Items.Count; i++)
        {
            PetProduceDataRow candidateRow = producePool.Items[i];
            if (candidateRow == null)
            {
                Log.Warning("PlayerRuntimeModule 无法抽取产出物，宠物 Id '{0}' 产出池内出现空项。", petId);
                return false;
            }

            accumulatedWeight += candidateRow.Weight;
            if (roll < accumulatedWeight)
            {
                produceDataRow = candidateRow;
                return true;
            }
        }

        // 理论上不会走到这里；若走到，说明 TotalWeight 与 Items 内权重总和不一致。
        produceDataRow = producePool.Items[producePool.Items.Count - 1];
        if (produceDataRow == null)
        {
            Log.Warning("PlayerRuntimeModule 无法抽取产出物，宠物 Id '{0}' 产出池内出现空项。", petId);
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
    /// 在首次获得产出物时写入解锁集合，并按 PetProduce.txt 的 RewardStars 发放星星。
    /// </summary>
    /// <param name="produceCode">产出物 Code。</param>
    /// <param name="produceRow">产出物配置行，允许为空；为空时只写入解锁状态。</param>
    private void UnlockProduceIfNeeded(string produceCode, PetProduceDataRow produceRow)
    {
        // 首解发星：HashSet<T>.Add 返回 true 代表“之前不在集合里”，即本次为首次拾取。
        // 原子“查+写”合并为一句，避免先 Contains 后 Add 的两次哈表查询。
        // 后续同 code 拾取 Add 返回 false 直接跳过发星逻辑。
        if (!_unlockedProduceCodes.Add(produceCode))
        {
            return;
        }

        // 表中 RewardStars > 0 才调 AddStars（同为 partial 内部可见，无须改可见性）。
        // produceRow 缺失只跳过发星但仍保留解锁状态，避免表错乱时反复为同一 code 发星。
        if (produceRow != null && produceRow.RewardStars > 0)
        {
            AddStars(produceRow.RewardStars);
        }

        CollectionUnlocksChanged?.Invoke();
    }
}
