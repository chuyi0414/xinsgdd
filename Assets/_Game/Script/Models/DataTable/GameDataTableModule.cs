using System;
using System.Collections.Generic;
using GameFramework.DataTable;
using GameFramework.Event;
using UnityGameFramework.Runtime;

/// <summary>
/// 通用数据表模块。
/// 统一管理业务层所有 DataTable 的创建、加载、注册、校验、缓存与查询。
/// </summary>
public sealed class GameDataTableModule
{
    /// <summary>
    /// 蛋表资源路径。
    /// </summary>
    private static readonly string EggDataTableAssetName = AssetPath.GetDataTable("Egg");

    /// <summary>
    /// 宠物表资源路径。
    /// </summary>
    private static readonly string PetDataTableAssetName = AssetPath.GetDataTable("Pet");

    /// <summary>
    /// 水果表资源路径。
    /// </summary>
    private static readonly string FruitDataTableAssetName = AssetPath.GetDataTable("Fruit");

    /// <summary>
    /// 宠物产出表资源路径。
    /// </summary>
    private static readonly string PetProduceDataTableAssetName = AssetPath.GetDataTable("PetProduce");

    /// <summary>
    /// 全局玩法规则表资源路径。
    /// </summary>
    private static readonly string GameplayRuleDataTableAssetName = AssetPath.GetDataTable("GameplayRule");

    /// <summary>
    /// 建筑槽位配置表资源路径。
    /// </summary>
    private static readonly string ArchitectureSlotDataTableAssetName = AssetPath.GetDataTable("ArchitectureSlot");

    /// <summary>
    /// 建筑升级配置表资源路径。
    /// </summary>
    private static readonly string ArchitectureUpgradeDataTableAssetName = AssetPath.GetDataTable("ArchitectureUpgrade");

    /// <summary>
    /// 每日一关得分配置表资源路径。
    /// </summary>
    private static readonly string DailyChallengeScoreDataTableAssetName = AssetPath.GetDataTable("DailyChallengeScore");

    /// <summary>
    /// 每日一关价格配置表资源路径。
    /// </summary>
    private static readonly string DailyChallengeCostDataTableAssetName = AssetPath.GetDataTable("DailyChallengeCost");

    /// <summary>
    /// 已注册的数据表缓存，按行类型索引。
    /// </summary>
    private readonly Dictionary<Type, object> _dataTables = new Dictionary<Type, object>();

    /// <summary>
    /// 带 Code 的数据表行索引缓存。
    /// </summary>
    private readonly Dictionary<Type, Dictionary<string, int>> _rowIdsByCode = new Dictionary<Type, Dictionary<string, int>>();

    /// <summary>
    /// 当前是否已经订阅底层数据表加载事件。
    /// </summary>
    private bool _isListeningLoadDataTableEvents;

    /// <summary>
    /// 数据表可用状态发生变化时触发。
    /// 加载流程可监听它刷新按钮状态和触发后续资源预加载。
    /// </summary>
    public event Action LoadStateChanged;

    /// <summary>
    /// 必需业务数据表是否全部可用。
    /// </summary>
    public bool IsReady => IsAvailable<EggDataRow>()
        && IsAvailable<PetDataRow>()
        && IsAvailable<FruitDataRow>()
        && IsAvailable<PetProduceDataRow>()
        && IsAvailable<GameplayRuleDataRow>()
        && IsAvailable<ArchitectureSlotDataRow>()
        && IsAvailable<ArchitectureUpgradeDataRow>()
        && IsAvailable<DailyChallengeScoreDataRow>()
        && IsAvailable<DailyChallengeCostDataRow>();

    /// <summary>
    /// 启动全部必需业务数据表加载。
    /// 重复调用是安全的，只会补齐尚未开始或尚未注册完成的部分。
    /// </summary>
    public void BeginLoadRequiredDataTables()
    {
        EnsureLoadEventSubscription();

        BeginLoadEggDataTable();
        BeginLoadPetDataTable();
        BeginLoadFruitDataTable();
        BeginLoadPetProduceDataTable();
        BeginLoadGameplayRuleDataTable();
        BeginLoadArchitectureSlotDataTable();
        BeginLoadArchitectureUpgradeDataTable();
        BeginLoadDailyChallengeScoreDataTable();
        BeginLoadDailyChallengeCostDataTable();

        NotifyLoadStateChanged();
    }

    /// <summary>
    /// 确保指定类型的数据表已创建。
    /// </summary>
    public IDataTable<T> EnsureDataTable<T>() where T : class, IDataRow, new()
    {
        if (GameEntry.DataTable == null)
        {
            Log.Error("GameDataTableModule ensure data table failed because DataTable component is null.");
            return null;
        }

        if (GameEntry.DataTable.HasDataTable<T>())
        {
            return GameEntry.DataTable.GetDataTable<T>();
        }

        return GameEntry.DataTable.CreateDataTable<T>();
    }

    /// <summary>
    /// 注册已加载完成的数据表。
    /// </summary>
    public bool Register<T>(IDataTable<T> dataTable) where T : class, IDataRow
    {
        if (dataTable == null)
        {
            Log.Error("GameDataTableModule register failed because data table is null.");
            Clear<T>();
            return false;
        }

        T[] rows = dataTable.GetAllDataRows();
        if (rows == null || rows.Length == 0)
        {
            Log.Error("GameDataTableModule register failed because data table '{0}' is empty.", typeof(T).Name);
            Clear<T>();
            return false;
        }

        Dictionary<string, int> rowIdsByCode = null;
        if (typeof(ICodeDataRow).IsAssignableFrom(typeof(T)))
        {
            rowIdsByCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rows.Length; i++)
            {
                T row = rows[i];
                if (row == null)
                {
                    Log.Error("GameDataTableModule register failed because row is null in '{0}'.", typeof(T).Name);
                    Clear<T>();
                    return false;
                }

                ICodeDataRow codeRow = row as ICodeDataRow;
                if (codeRow == null || string.IsNullOrWhiteSpace(codeRow.Code))
                {
                    Log.Error("GameDataTableModule register failed because code row is invalid in '{0}', id '{1}'.", typeof(T).Name, row.Id);
                    Clear<T>();
                    return false;
                }

                if (rowIdsByCode.ContainsKey(codeRow.Code))
                {
                    Log.Error("GameDataTableModule register failed because code '{0}' is duplicated in '{1}'.", codeRow.Code, typeof(T).Name);
                    Clear<T>();
                    return false;
                }

                rowIdsByCode.Add(codeRow.Code, row.Id);
            }
        }

        Type rowType = typeof(T);
        _dataTables[rowType] = dataTable;
        if (rowIdsByCode != null)
        {
            _rowIdsByCode[rowType] = rowIdsByCode;
        }
        else
        {
            _rowIdsByCode.Remove(rowType);
        }

        NotifyLoadStateChanged();
        return true;
    }

    /// <summary>
    /// 指定类型的数据表是否已可用。
    /// </summary>
    public bool IsAvailable<T>() where T : class, IDataRow
    {
        return _dataTables.ContainsKey(typeof(T));
    }

    /// <summary>
    /// 获取指定类型的数据表。
    /// </summary>
    public IDataTable<T> GetDataTable<T>() where T : class, IDataRow
    {
        if (!_dataTables.TryGetValue(typeof(T), out object dataTableObject))
        {
            Log.Warning("GameDataTableModule can not find registered data table '{0}'.", typeof(T).Name);
            return null;
        }

        return dataTableObject as IDataTable<T>;
    }

    /// <summary>
    /// 按 Id 获取数据表行。
    /// </summary>
    public T GetDataRow<T>(int id) where T : class, IDataRow
    {
        IDataTable<T> dataTable = GetDataTable<T>();
        if (dataTable == null)
        {
            return null;
        }

        T row = dataTable.GetDataRow(id);
        if (row == null)
        {
            Log.Warning("GameDataTableModule can not find row in '{0}' by id '{1}'.", typeof(T).Name, id);
        }

        return row;
    }

    /// <summary>
    /// 按 Code 获取数据表行。
    /// </summary>
    public T GetDataRowByCode<T>(string code) where T : class, IDataRow, ICodeDataRow
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            Log.Warning("GameDataTableModule can not find row in '{0}' because code is empty.", typeof(T).Name);
            return null;
        }

        if (!_rowIdsByCode.TryGetValue(typeof(T), out Dictionary<string, int> rowIdsByCode))
        {
            Log.Warning("GameDataTableModule can not find code index for '{0}'.", typeof(T).Name);
            return null;
        }

        if (!rowIdsByCode.TryGetValue(code, out int rowId))
        {
            Log.Warning("GameDataTableModule can not find row in '{0}' by code '{1}'.", typeof(T).Name, code);
            return null;
        }

        return GetDataRow<T>(rowId);
    }

    /// <summary>
    /// 获取指定类型的全部数据表行。
    /// </summary>
    public T[] GetAllDataRows<T>() where T : class, IDataRow
    {
        IDataTable<T> dataTable = GetDataTable<T>();
        if (dataTable == null)
        {
            return Array.Empty<T>();
        }

        return dataTable.GetAllDataRows();
    }

    /// <summary>
    /// 清空指定类型的数据表缓存。
    /// </summary>
    public void Clear<T>() where T : class, IDataRow
    {
        Type rowType = typeof(T);
        _dataTables.Remove(rowType);
        _rowIdsByCode.Remove(rowType);
        NotifyLoadStateChanged();
    }

    /// <summary>
    /// 清空全部数据表缓存。
    /// </summary>
    public void ClearAll()
    {
        _dataTables.Clear();
        _rowIdsByCode.Clear();
        NotifyLoadStateChanged();
    }

    /// <summary>
    /// 确保已经订阅底层数据表加载事件。
    /// </summary>
    private void EnsureLoadEventSubscription()
    {
        if (_isListeningLoadDataTableEvents)
        {
            return;
        }

        if (GameEntry.Event == null)
        {
            Log.Error("GameDataTableModule can not subscribe load events because Event component is null.");
            return;
        }

        GameEntry.Event.Subscribe(LoadDataTableSuccessEventArgs.EventId, OnLoadDataTableSuccess);
        GameEntry.Event.Subscribe(LoadDataTableFailureEventArgs.EventId, OnLoadDataTableFailure);
        _isListeningLoadDataTableEvents = true;
    }

    /// <summary>
    /// 开始加载蛋系统数据表。
    /// </summary>
    private void BeginLoadEggDataTable()
    {
        if (IsAvailable<EggDataRow>())
        {
            return;
        }

        IDataTable<EggDataRow> eggDataTable = EnsureDataTable<EggDataRow>();
        if (eggDataTable == null)
        {
            Log.Error("创建蛋系统数据表失败。");
            return;
        }

        if (eggDataTable.Count > 0)
        {
            TryRegisterEggDataTable(eggDataTable);
            return;
        }

        ((GameFramework.DataTable.DataTableBase)eggDataTable).ReadData(EggDataTableAssetName);
    }

    /// <summary>
    /// 开始加载宠物系统数据表。
    /// </summary>
    private void BeginLoadPetDataTable()
    {
        if (IsAvailable<PetDataRow>())
        {
            return;
        }

        IDataTable<PetDataRow> petDataTable = EnsureDataTable<PetDataRow>();
        if (petDataTable == null)
        {
            Log.Error("创建宠物系统数据表失败。");
            return;
        }

        if (petDataTable.Count > 0)
        {
            TryRegisterPetDataTable(petDataTable);
            return;
        }

        ((GameFramework.DataTable.DataTableBase)petDataTable).ReadData(PetDataTableAssetName);
    }

    /// <summary>
    /// 开始加载水果系统数据表。
    /// </summary>
    private void BeginLoadFruitDataTable()
    {
        if (IsAvailable<FruitDataRow>())
        {
            return;
        }

        IDataTable<FruitDataRow> fruitDataTable = EnsureDataTable<FruitDataRow>();
        if (fruitDataTable == null)
        {
            Log.Error("创建水果系统数据表失败。");
            return;
        }

        if (fruitDataTable.Count > 0)
        {
            TryRegisterFruitDataTable(fruitDataTable);
            return;
        }

        ((GameFramework.DataTable.DataTableBase)fruitDataTable).ReadData(FruitDataTableAssetName);
    }

    /// <summary>
    /// 开始加载宠物产出数据表。
    /// </summary>
    private void BeginLoadPetProduceDataTable()
    {
        if (IsAvailable<PetProduceDataRow>())
        {
            return;
        }

        IDataTable<PetProduceDataRow> petProduceDataTable = EnsureDataTable<PetProduceDataRow>();
        if (petProduceDataTable == null)
        {
            Log.Error("创建宠物产出数据表失败。");
            return;
        }

        if (petProduceDataTable.Count > 0)
        {
            TryRegisterPetProduceDataTable(petProduceDataTable);
            return;
        }

        ((GameFramework.DataTable.DataTableBase)petProduceDataTable).ReadData(PetProduceDataTableAssetName);
    }

    /// <summary>
    /// 开始加载全局玩法规则表。
    /// </summary>
    private void BeginLoadGameplayRuleDataTable()
    {
        if (IsAvailable<GameplayRuleDataRow>())
        {
            return;
        }

        IDataTable<GameplayRuleDataRow> gameplayRuleDataTable = EnsureDataTable<GameplayRuleDataRow>();
        if (gameplayRuleDataTable == null)
        {
            Log.Error("创建全局玩法规则表失败。");
            return;
        }

        if (gameplayRuleDataTable.Count > 0)
        {
            TryRegisterGameplayRuleDataTable(gameplayRuleDataTable);
            return;
        }

        ((GameFramework.DataTable.DataTableBase)gameplayRuleDataTable).ReadData(GameplayRuleDataTableAssetName);
    }

    /// <summary>
    /// 开始加载建筑槽位配置表。
    /// </summary>
    private void BeginLoadArchitectureSlotDataTable()
    {
        if (IsAvailable<ArchitectureSlotDataRow>())
        {
            return;
        }

        IDataTable<ArchitectureSlotDataRow> architectureSlotDataTable = EnsureDataTable<ArchitectureSlotDataRow>();
        if (architectureSlotDataTable == null)
        {
            Log.Error("创建建筑槽位配置表失败。");
            return;
        }

        if (architectureSlotDataTable.Count > 0)
        {
            TryRegisterArchitectureSlotDataTable(architectureSlotDataTable);
            return;
        }

        ((GameFramework.DataTable.DataTableBase)architectureSlotDataTable).ReadData(ArchitectureSlotDataTableAssetName);
    }

    /// <summary>
    /// 开始加载建筑升级配置表。
    /// </summary>
    private void BeginLoadArchitectureUpgradeDataTable()
    {
        if (IsAvailable<ArchitectureUpgradeDataRow>())
        {
            return;
        }

        IDataTable<ArchitectureUpgradeDataRow> architectureUpgradeDataTable = EnsureDataTable<ArchitectureUpgradeDataRow>();
        if (architectureUpgradeDataTable == null)
        {
            Log.Error("创建建筑升级配置表失败。");
            return;
        }

        if (architectureUpgradeDataTable.Count > 0)
        {
            TryRegisterArchitectureUpgradeDataTable(architectureUpgradeDataTable);
            return;
        }

        ((GameFramework.DataTable.DataTableBase)architectureUpgradeDataTable).ReadData(ArchitectureUpgradeDataTableAssetName);
    }

    /// <summary>
    /// 开始加载每日一关得分配置表。
    /// </summary>
    private void BeginLoadDailyChallengeScoreDataTable()
    {
        if (IsAvailable<DailyChallengeScoreDataRow>())
        {
            return;
        }

        IDataTable<DailyChallengeScoreDataRow> dailyChallengeScoreDataTable = EnsureDataTable<DailyChallengeScoreDataRow>();
        if (dailyChallengeScoreDataTable == null)
        {
            Log.Error("创建每日一关得分配置表失败。");
            return;
        }

        if (dailyChallengeScoreDataTable.Count > 0)
        {
            TryRegisterDailyChallengeScoreDataTable(dailyChallengeScoreDataTable);
            return;
        }

        ((GameFramework.DataTable.DataTableBase)dailyChallengeScoreDataTable).ReadData(DailyChallengeScoreDataTableAssetName);
    }

    /// <summary>
    /// 开始加载每日一关价格配置表。
    /// </summary>
    private void BeginLoadDailyChallengeCostDataTable()
    {
        if (IsAvailable<DailyChallengeCostDataRow>())
        {
            return;
        }

        IDataTable<DailyChallengeCostDataRow> dailyChallengeCostDataTable = EnsureDataTable<DailyChallengeCostDataRow>();
        if (dailyChallengeCostDataTable == null)
        {
            Log.Error("创建每日一关价格配置表失败。");
            return;
        }

        if (dailyChallengeCostDataTable.Count > 0)
        {
            TryRegisterDailyChallengeCostDataTable(dailyChallengeCostDataTable);
            return;
        }

        ((GameFramework.DataTable.DataTableBase)dailyChallengeCostDataTable).ReadData(DailyChallengeCostDataTableAssetName);
    }

    /// <summary>
    /// 数据表加载成功回调。
    /// </summary>
    private void OnLoadDataTableSuccess(object sender, GameEventArgs e)
    {
        LoadDataTableSuccessEventArgs ne = e as LoadDataTableSuccessEventArgs;
        if (ne == null)
        {
            return;
        }

        if (string.Equals(ne.DataTableAssetName, EggDataTableAssetName, StringComparison.Ordinal))
        {
            TryRegisterEggDataTable(GameEntry.DataTable.GetDataTable<EggDataRow>());
        }
        else if (string.Equals(ne.DataTableAssetName, PetDataTableAssetName, StringComparison.Ordinal))
        {
            TryRegisterPetDataTable(GameEntry.DataTable.GetDataTable<PetDataRow>());
        }
        else if (string.Equals(ne.DataTableAssetName, FruitDataTableAssetName, StringComparison.Ordinal))
        {
            TryRegisterFruitDataTable(GameEntry.DataTable.GetDataTable<FruitDataRow>());
        }
        else if (string.Equals(ne.DataTableAssetName, PetProduceDataTableAssetName, StringComparison.Ordinal))
        {
            TryRegisterPetProduceDataTable(GameEntry.DataTable.GetDataTable<PetProduceDataRow>());
        }
        else if (string.Equals(ne.DataTableAssetName, GameplayRuleDataTableAssetName, StringComparison.Ordinal))
        {
            TryRegisterGameplayRuleDataTable(GameEntry.DataTable.GetDataTable<GameplayRuleDataRow>());
        }
        else if (string.Equals(ne.DataTableAssetName, ArchitectureSlotDataTableAssetName, StringComparison.Ordinal))
        {
            TryRegisterArchitectureSlotDataTable(GameEntry.DataTable.GetDataTable<ArchitectureSlotDataRow>());
        }
        else if (string.Equals(ne.DataTableAssetName, ArchitectureUpgradeDataTableAssetName, StringComparison.Ordinal))
        {
            TryRegisterArchitectureUpgradeDataTable(GameEntry.DataTable.GetDataTable<ArchitectureUpgradeDataRow>());
        }
        else if (string.Equals(ne.DataTableAssetName, DailyChallengeScoreDataTableAssetName, StringComparison.Ordinal))
        {
            TryRegisterDailyChallengeScoreDataTable(GameEntry.DataTable.GetDataTable<DailyChallengeScoreDataRow>());
        }
        else if (string.Equals(ne.DataTableAssetName, DailyChallengeCostDataTableAssetName, StringComparison.Ordinal))
        {
            TryRegisterDailyChallengeCostDataTable(GameEntry.DataTable.GetDataTable<DailyChallengeCostDataRow>());
        }
    }

    /// <summary>
    /// 数据表加载失败回调。
    /// </summary>
    private void OnLoadDataTableFailure(object sender, GameEventArgs e)
    {
        LoadDataTableFailureEventArgs ne = e as LoadDataTableFailureEventArgs;
        if (ne == null)
        {
            return;
        }

        if (string.Equals(ne.DataTableAssetName, EggDataTableAssetName, StringComparison.Ordinal))
        {
            Log.Error("加载蛋系统数据表失败：{0}", ne.ErrorMessage);
            Clear<EggDataRow>();
        }
        else if (string.Equals(ne.DataTableAssetName, PetDataTableAssetName, StringComparison.Ordinal))
        {
            Log.Error("加载宠物系统数据表失败：{0}", ne.ErrorMessage);
            Clear<PetDataRow>();
        }
        else if (string.Equals(ne.DataTableAssetName, FruitDataTableAssetName, StringComparison.Ordinal))
        {
            Log.Error("加载水果系统数据表失败：{0}", ne.ErrorMessage);
            Clear<FruitDataRow>();
        }
        else if (string.Equals(ne.DataTableAssetName, PetProduceDataTableAssetName, StringComparison.Ordinal))
        {
            Log.Error("加载宠物产出数据表失败：{0}", ne.ErrorMessage);
            Clear<PetProduceDataRow>();
        }
        else if (string.Equals(ne.DataTableAssetName, GameplayRuleDataTableAssetName, StringComparison.Ordinal))
        {
            Log.Error("加载全局玩法规则表失败：{0}", ne.ErrorMessage);
            Clear<GameplayRuleDataRow>();
        }
        else if (string.Equals(ne.DataTableAssetName, ArchitectureSlotDataTableAssetName, StringComparison.Ordinal))
        {
            Log.Error("加载建筑槽位配置表失败：{0}", ne.ErrorMessage);
            Clear<ArchitectureSlotDataRow>();
        }
        else if (string.Equals(ne.DataTableAssetName, ArchitectureUpgradeDataTableAssetName, StringComparison.Ordinal))
        {
            Log.Error("加载建筑升级配置表失败：{0}", ne.ErrorMessage);
            Clear<ArchitectureUpgradeDataRow>();
        }
        else if (string.Equals(ne.DataTableAssetName, DailyChallengeScoreDataTableAssetName, StringComparison.Ordinal))
        {
            Log.Error("加载每日一关得分配置表失败：{0}", ne.ErrorMessage);
            Clear<DailyChallengeScoreDataRow>();
        }
        else if (string.Equals(ne.DataTableAssetName, DailyChallengeCostDataTableAssetName, StringComparison.Ordinal))
        {
            Log.Error("加载每日一关价格配置表失败：{0}", ne.ErrorMessage);
            Clear<DailyChallengeCostDataRow>();
        }
    }

    /// <summary>
    /// 注册蛋系统数据表到通用模块。
    /// </summary>
    private bool TryRegisterEggDataTable(IDataTable<EggDataRow> eggDataTable)
    {
        if (!Register(eggDataTable))
        {
            Log.Error("蛋系统数据表注册失败。");
            return false;
        }

        return TryWarmupEggHatchRuntimeState();
    }

    /// <summary>
    /// 注册宠物系统数据表到通用模块。
    /// </summary>
    private bool TryRegisterPetDataTable(IDataTable<PetDataRow> petDataTable)
    {
        if (!ValidatePetDataRows(petDataTable))
        {
            Clear<PetDataRow>();
            return false;
        }

        if (!Register(petDataTable))
        {
            Log.Error("宠物系统数据表注册失败。");
            return false;
        }

        if (GameEntry.PetPlacement != null && !GameEntry.PetPlacement.WarmupPetSelectionCatalog())
        {
            Log.Error("宠物挑选缓存初始化失败。");
            Clear<PetDataRow>();
            return false;
        }

        if (!TryWarmupPetProduceRuntimeState())
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 注册水果系统数据表到通用模块。
    /// </summary>
    private bool TryRegisterFruitDataTable(IDataTable<FruitDataRow> fruitDataTable)
    {
        if (!Register(fruitDataTable))
        {
            Log.Error("水果系统数据表注册失败。");
            return false;
        }

        if (!TryWarmupPlayerRuntimeState())
        {
            return false;
        }

        if (!TryWarmupPetDiningRuntimeState())
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 注册宠物产出数据表到通用模块。
    /// </summary>
    private bool TryRegisterPetProduceDataTable(IDataTable<PetProduceDataRow> petProduceDataTable)
    {
        if (!Register(petProduceDataTable))
        {
            Log.Error("宠物产出数据表注册失败。");
            return false;
        }

        if (!TryWarmupPetProduceRuntimeState())
        {
            return false;
        }

        if (!TryWarmupPetDiningRuntimeState())
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 注册全局玩法规则表到通用模块。
    /// </summary>
    private bool TryRegisterGameplayRuleDataTable(IDataTable<GameplayRuleDataRow> gameplayRuleDataTable)
    {
        if (!ValidateGameplayRuleDataRows(gameplayRuleDataTable))
        {
            Clear<GameplayRuleDataRow>();
            return false;
        }

        if (!Register(gameplayRuleDataTable))
        {
            Log.Error("全局玩法规则表注册失败。");
            return false;
        }

        GameEntry.PetPlacement?.WarmupGameplayRuleCache();

        if (!TryWarmupEggHatchRuntimeState())
        {
            return false;
        }

        if (!TryWarmupPlayerRuntimeState())
        {
            return false;
        }

        if (!TryWarmupPetDiningRuntimeState())
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 注册建筑槽位配置表到通用模块。
    /// </summary>
    private bool TryRegisterArchitectureSlotDataTable(IDataTable<ArchitectureSlotDataRow> architectureSlotDataTable)
    {
        if (!ValidateArchitectureSlotDataRows(architectureSlotDataTable))
        {
            Clear<ArchitectureSlotDataRow>();
            return false;
        }

        if (!Register(architectureSlotDataTable))
        {
            Log.Error("建筑槽位配置表注册失败。");
            return false;
        }

        return TryWarmupPlayerRuntimeState();
    }

    /// <summary>
    /// 注册建筑升级配置表到通用模块。
    /// </summary>
    private bool TryRegisterArchitectureUpgradeDataTable(IDataTable<ArchitectureUpgradeDataRow> architectureUpgradeDataTable)
    {
        if (!ValidateArchitectureUpgradeDataRows(architectureUpgradeDataTable))
        {
            Clear<ArchitectureUpgradeDataRow>();
            return false;
        }

        if (!Register(architectureUpgradeDataTable))
        {
            Log.Error("建筑升级配置表注册失败。");
            return false;
        }

        return TryWarmupPlayerRuntimeState();
    }

    /// <summary>
    /// 注册每日一关得分配置表到通用模块。
    /// </summary>
    private bool TryRegisterDailyChallengeScoreDataTable(IDataTable<DailyChallengeScoreDataRow> dailyChallengeScoreDataTable)
    {
        if (!Register(dailyChallengeScoreDataTable))
        {
            Log.Error("每日一关得分配置表注册失败。");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 注册每日一关价格配置表到通用模块。
    /// </summary>
    private bool TryRegisterDailyChallengeCostDataTable(IDataTable<DailyChallengeCostDataRow> dailyChallengeCostDataTable)
    {
        if (!Register(dailyChallengeCostDataTable))
        {
            Log.Error("每日一关价格配置表注册失败。");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 当蛋表与玩法规则表都准备好后，初始化蛋孵化运行时缓存。
    /// </summary>
    /// <returns>是否初始化成功。</returns>
    private bool TryWarmupEggHatchRuntimeState()
    {
        if (!IsAvailable<EggDataRow>() || !IsAvailable<GameplayRuleDataRow>())
        {
            return true;
        }

        if (GameEntry.EggHatch == null)
        {
            Log.Error("EggHatchComponent 未挂载，无法初始化孵化运行时状态。");
            Clear<EggDataRow>();
            return false;
        }

        GameEntry.EggHatch.EnsureInitialized();
        if (!GameEntry.EggHatch.IsAvailable)
        {
            Log.Error("孵化运行时模块初始化失败。");
            Clear<EggDataRow>();
            return false;
        }

        return true;
    }

    /// <summary>
    /// 当水果表、玩法规则表与建筑配置表都准备好后，初始化玩家运行时缓存。
    /// </summary>
    /// <returns>是否初始化成功。</returns>
    private bool TryWarmupPlayerRuntimeState()
    {
        if (!ArePlayerRuntimeTablesReady())
        {
            return true;
        }

        if (GameEntry.Fruits == null || !GameEntry.Fruits.EnsureInitialized())
        {
            Log.Error("玩家运行时模块初始化失败。");
            Clear<FruitDataRow>();
            return false;
        }

        return true;
    }

    /// <summary>
    /// 当宠物表与宠物产出表都准备好后，校验并初始化产出运行时缓存。
    /// </summary>
    /// <returns>是否初始化成功。</returns>
    private bool TryWarmupPetProduceRuntimeState()
    {
        if (!IsAvailable<PetDataRow>() || !IsAvailable<PetProduceDataRow>())
        {
            return true;
        }

        IDataTable<PetDataRow> petDataTable = GetDataTable<PetDataRow>();
        IDataTable<PetProduceDataRow> petProduceDataTable = GetDataTable<PetProduceDataRow>();
        if (!ValidatePetProduceDataRows(petDataTable, petProduceDataTable))
        {
            Clear<PetProduceDataRow>();
            return false;
        }

        if (GameEntry.Fruits == null || !GameEntry.Fruits.WarmupProduceCatalog())
        {
            Log.Error("宠物产出运行时缓存初始化失败。");
            Clear<PetProduceDataRow>();
            return false;
        }

        return true;
    }

    /// <summary>
    /// 当点餐流程依赖的全部表都准备好后，初始化点餐运行时缓存。
    /// </summary>
    /// <returns>是否初始化成功。</returns>
    private bool TryWarmupPetDiningRuntimeState()
    {
        if (!ArePetDiningRuntimeTablesReady())
        {
            return true;
        }

        if (GameEntry.Fruits == null || !GameEntry.Fruits.EnsureInitialized())
        {
            Log.Error("玩家运行时模块尚未就绪，无法初始化点餐运行时模块。");
            Clear<FruitDataRow>();
            return false;
        }

        if (GameEntry.PetDiningOrders == null)
        {
            Log.Error("PetDiningOrderComponent 未挂载，无法初始化点餐运行时模块。");
            Clear<PetProduceDataRow>();
            return false;
        }

        GameEntry.PetDiningOrders.EnsureInitialized();
        if (!GameEntry.PetDiningOrders.IsAvailable)
        {
            Log.Error("点餐运行时模块初始化失败。");
            Clear<PetProduceDataRow>();
            return false;
        }

        return true;
    }

    /// <summary>
    /// 玩家运行时模块所需表是否已全部就绪。
    /// </summary>
    private bool ArePlayerRuntimeTablesReady()
    {
        return IsAvailable<FruitDataRow>()
            && IsAvailable<GameplayRuleDataRow>()
            && IsAvailable<ArchitectureSlotDataRow>()
            && IsAvailable<ArchitectureUpgradeDataRow>();
    }

    /// <summary>
    /// 点餐运行时模块所需表是否已全部就绪。
    /// </summary>
    private bool ArePetDiningRuntimeTablesReady()
    {
        return ArePlayerRuntimeTablesReady()
            && IsAvailable<PetProduceDataRow>();
    }

    /// <summary>
    /// 校验玩法规则表是否满足运行时约束。
    /// </summary>
    private static bool ValidateGameplayRuleDataRows(IDataTable<GameplayRuleDataRow> gameplayRuleDataTable)
    {
        if (gameplayRuleDataTable == null)
        {
            Log.Error("校验玩法规则表失败，数据表为空。");
            return false;
        }

        GameplayRuleDataRow[] rows = gameplayRuleDataTable.GetAllDataRows();
        if (rows == null || rows.Length != 1)
        {
            Log.Error("校验玩法规则表失败，必须且只能存在 1 行配置。");
            return false;
        }

        GameplayRuleDataRow row = rows[0];
        if (row == null)
        {
            Log.Error("校验玩法规则表失败，存在空行。");
            return false;
        }

        if (!string.Equals(row.Code, GameplayRuleDataRow.DefaultCode, StringComparison.Ordinal))
        {
            Log.Error("校验玩法规则表失败，唯一配置行的 Code 必须为 '{0}'。", GameplayRuleDataRow.DefaultCode);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 校验建筑槽位配置表。
    /// </summary>
    private static bool ValidateArchitectureSlotDataRows(IDataTable<ArchitectureSlotDataRow> architectureSlotDataTable)
    {
        if (architectureSlotDataTable == null)
        {
            Log.Error("校验建筑槽位配置表失败，数据表为空。");
            return false;
        }

        ArchitectureSlotDataRow[] rows = architectureSlotDataTable.GetAllDataRows();
        if (rows == null || rows.Length == 0)
        {
            Log.Error("校验建筑槽位配置表失败，数据表为空。");
            return false;
        }

        Dictionary<PlayerRuntimeModule.ArchitectureCategory, ArchitectureSlotDataRow[]> rowsByCategory =
            new Dictionary<PlayerRuntimeModule.ArchitectureCategory, ArchitectureSlotDataRow[]>
            {
                {
                    PlayerRuntimeModule.ArchitectureCategory.Hatch,
                    new ArchitectureSlotDataRow[PlayerRuntimeModule.HatchArchitectureCountValue]
                },
                {
                    PlayerRuntimeModule.ArchitectureCategory.Diet,
                    new ArchitectureSlotDataRow[PlayerRuntimeModule.DietArchitectureCountValue]
                },
                {
                    PlayerRuntimeModule.ArchitectureCategory.Fruiter,
                    new ArchitectureSlotDataRow[PlayerRuntimeModule.FruiterArchitectureCountValue]
                },
            };

        for (int i = 0; i < rows.Length; i++)
        {
            ArchitectureSlotDataRow row = rows[i];
            if (row == null)
            {
                Log.Error("校验建筑槽位配置表失败，存在空行。");
                return false;
            }

            if (!rowsByCategory.TryGetValue(row.Category, out ArchitectureSlotDataRow[] categoryRows))
            {
                Log.Error("校验建筑槽位配置表失败，存在不支持的建筑类别 '{0}'。", row.Category);
                return false;
            }

            if (row.SlotIndex <= 0 || row.SlotIndex > categoryRows.Length)
            {
                Log.Error(
                    "校验建筑槽位配置表失败，类别 '{0}' 的 SlotIndex '{1}' 超出当前项目支持上限 '{2}'。",
                    row.Category,
                    row.SlotIndex,
                    categoryRows.Length);
                return false;
            }

            if (categoryRows[row.SlotIndex - 1] != null)
            {
                Log.Error("校验建筑槽位配置表失败，类别 '{0}' 的 SlotIndex '{1}' 重复。", row.Category, row.SlotIndex);
                return false;
            }

            categoryRows[row.SlotIndex - 1] = row;
        }

        foreach (KeyValuePair<PlayerRuntimeModule.ArchitectureCategory, ArchitectureSlotDataRow[]> pair in rowsByCategory)
        {
            ArchitectureSlotDataRow[] categoryRows = pair.Value;
            bool foundLockedSlot = false;
            for (int i = 0; i < categoryRows.Length; i++)
            {
                ArchitectureSlotDataRow row = categoryRows[i];
                if (row == null)
                {
                    Log.Error("校验建筑槽位配置表失败，类别 '{0}' 缺少 SlotIndex '{1}' 的配置。", pair.Key, i + 1);
                    return false;
                }

                if (!row.IsInitiallyUnlocked)
                {
                    foundLockedSlot = true;
                    continue;
                }

                if (foundLockedSlot)
                {
                    Log.Error("校验建筑槽位配置表失败，类别 '{0}' 的初始解锁配置必须是连续前缀。", pair.Key);
                    return false;
                }
            }

            if (!categoryRows[0].IsInitiallyUnlocked)
            {
                Log.Error("校验建筑槽位配置表失败，类别 '{0}' 的 1 号位必须默认解锁。", pair.Key);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 校验建筑升级配置表。
    /// </summary>
    private static bool ValidateArchitectureUpgradeDataRows(IDataTable<ArchitectureUpgradeDataRow> architectureUpgradeDataTable)
    {
        if (architectureUpgradeDataTable == null)
        {
            Log.Error("校验建筑升级配置表失败，数据表为空。");
            return false;
        }

        ArchitectureUpgradeDataRow[] rows = architectureUpgradeDataTable.GetAllDataRows();
        if (rows == null || rows.Length == 0)
        {
            Log.Error("校验建筑升级配置表失败，数据表为空。");
            return false;
        }

        Dictionary<PlayerRuntimeModule.ArchitectureCategory, HashSet<int>> levelsByCategory =
            new Dictionary<PlayerRuntimeModule.ArchitectureCategory, HashSet<int>>();
        Dictionary<PlayerRuntimeModule.ArchitectureCategory, int> maxLevelByCategory =
            new Dictionary<PlayerRuntimeModule.ArchitectureCategory, int>();

        for (int i = 0; i < rows.Length; i++)
        {
            ArchitectureUpgradeDataRow row = rows[i];
            if (row == null)
            {
                Log.Error("校验建筑升级配置表失败，存在空行。");
                return false;
            }

            if (row.EffectParam < 0)
            {
                Log.Error("校验建筑升级配置表失败，类别 '{0}' 的 CurrentLevel '{1}' 存在非法 EffectParam '{2}'。", row.Category, row.CurrentLevel, row.EffectParam);
                return false;
            }

            if (!levelsByCategory.TryGetValue(row.Category, out HashSet<int> levels))
            {
                levels = new HashSet<int>();
                levelsByCategory.Add(row.Category, levels);
                maxLevelByCategory[row.Category] = 0;
            }

            if (!levels.Add(row.CurrentLevel))
            {
                Log.Error("校验建筑升级配置表失败，类别 '{0}' 的 CurrentLevel '{1}' 重复。", row.Category, row.CurrentLevel);
                return false;
            }

            if (row.CurrentLevel > maxLevelByCategory[row.Category])
            {
                maxLevelByCategory[row.Category] = row.CurrentLevel;
            }
        }

        PlayerRuntimeModule.ArchitectureCategory[] categories =
        {
            PlayerRuntimeModule.ArchitectureCategory.Hatch,
            PlayerRuntimeModule.ArchitectureCategory.Diet,
            PlayerRuntimeModule.ArchitectureCategory.Fruiter,
        };

        for (int i = 0; i < categories.Length; i++)
        {
            PlayerRuntimeModule.ArchitectureCategory category = categories[i];
            if (!levelsByCategory.TryGetValue(category, out HashSet<int> levels)
                || !maxLevelByCategory.TryGetValue(category, out int maxCurrentLevel)
                || levels == null
                || levels.Count == 0
                || maxCurrentLevel <= 0)
            {
                Log.Error("校验建筑升级配置表失败，类别 '{0}' 没有任何升级配置。", category);
                return false;
            }

            for (int level = 1; level <= maxCurrentLevel; level++)
            {
                if (!levels.Contains(level))
                {
                    Log.Error("校验建筑升级配置表失败，类别 '{0}' 缺少 CurrentLevel '{1}' 的升级配置。", category, level);
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// 校验宠物表行数据是否满足运行时约束。
    /// </summary>
    private static bool ValidatePetDataRows(IDataTable<PetDataRow> petDataTable)
    {
        if (petDataTable == null)
        {
            Log.Error("校验宠物表失败，数据表为空。");
            return false;
        }

        PetDataRow[] rows = petDataTable.GetAllDataRows();
        if (rows == null || rows.Length == 0)
        {
            Log.Error("校验宠物表失败，数据表为空。");
            return false;
        }

        if (!ValidatePetQualityCoverage(rows))
        {
            return false;
        }

        for (int i = 0; i < rows.Length; i++)
        {
            PetDataRow row = rows[i];
            if (row == null)
            {
                Log.Error("校验宠物表失败，存在空行。");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 校验每个可孵化品质至少有一只宠物。
    /// </summary>
    private static bool ValidatePetQualityCoverage(PetDataRow[] rows)
    {
        bool hasNormal = false;
        bool hasRare = false;
        bool hasEpic = false;
        bool hasLegendary = false;
        bool hasMythic = false;

        for (int i = 0; i < rows.Length; i++)
        {
            PetDataRow row = rows[i];
            if (row == null)
            {
                continue;
            }

            switch (row.Quality)
            {
                case QualityType.Normal:
                    hasNormal = true;
                    break;

                case QualityType.Rare:
                    hasRare = true;
                    break;

                case QualityType.Epic:
                    hasEpic = true;
                    break;

                case QualityType.Legendary:
                    hasLegendary = true;
                    break;

                case QualityType.Mythic:
                    hasMythic = true;
                    break;
            }
        }

        if (hasNormal && hasRare && hasEpic && hasLegendary && hasMythic)
        {
            return true;
        }

        Log.Error(
            "宠物表配置错误，品质覆盖不完整。Normal={0}, Rare={1}, Epic={2}, Legendary={3}, Mythic={4}。",
            hasNormal,
            hasRare,
            hasEpic,
            hasLegendary,
            hasMythic);
        return false;
    }

    /// <summary>
    /// 校验宠物产出表是否满足“每个宠物正好有三档产出”的运行时约束。
    /// </summary>
    /// <param name="petDataTable">宠物表。</param>
    /// <param name="petProduceDataTable">宠物产出表。</param>
    /// <returns>是否校验通过。</returns>
    private static bool ValidatePetProduceDataRows(
        IDataTable<PetDataRow> petDataTable,
        IDataTable<PetProduceDataRow> petProduceDataTable)
    {
        if (petDataTable == null || petProduceDataTable == null)
        {
            Log.Error("校验宠物产出表失败，宠物表或产出表为空。");
            return false;
        }

        PetDataRow[] petRows = petDataTable.GetAllDataRows();
        PetProduceDataRow[] produceRows = petProduceDataTable.GetAllDataRows();
        if (petRows == null || petRows.Length == 0 || produceRows == null || produceRows.Length == 0)
        {
            Log.Error("校验宠物产出表失败，宠物表或产出表为空。");
            return false;
        }

        HashSet<int> validPetIds = new HashSet<int>();
        Dictionary<int, int> gradeMaskByPetId = new Dictionary<int, int>(petRows.Length);
        Dictionary<int, string> petCodeById = new Dictionary<int, string>(petRows.Length);

        for (int i = 0; i < petRows.Length; i++)
        {
            PetDataRow petRow = petRows[i];
            if (petRow == null)
            {
                Log.Error("校验宠物产出表失败，宠物表存在空行。");
                return false;
            }

            validPetIds.Add(petRow.Id);
            gradeMaskByPetId[petRow.Id] = 0;
            petCodeById[petRow.Id] = petRow.Code;
        }

        for (int i = 0; i < produceRows.Length; i++)
        {
            PetProduceDataRow produceRow = produceRows[i];
            if (produceRow == null)
            {
                Log.Error("校验宠物产出表失败，产出表存在空行。");
                return false;
            }

            if (!validPetIds.Contains(produceRow.PetId))
            {
                Log.Error("校验宠物产出表失败，产出物 '{0}' 关联了不存在的 PetId '{1}'。", produceRow.Code, produceRow.PetId);
                return false;
            }

            int gradeMask = GetProduceGradeMask(produceRow.Grade);
            if (gradeMask == 0)
            {
                Log.Error("校验宠物产出表失败，产出物 '{0}' 的等级 '{1}' 非法。", produceRow.Code, produceRow.Grade);
                return false;
            }

            int currentMask = gradeMaskByPetId[produceRow.PetId];
            if ((currentMask & gradeMask) != 0)
            {
                Log.Error(
                    "校验宠物产出表失败，宠物 '{0}'（PetId={1}）存在重复的等级配置 '{2}'。",
                    petCodeById[produceRow.PetId],
                    produceRow.PetId,
                    produceRow.Grade);
                return false;
            }

            gradeMaskByPetId[produceRow.PetId] = currentMask | gradeMask;
        }

        for (int i = 0; i < petRows.Length; i++)
        {
            PetDataRow petRow = petRows[i];
            if (petRow == null)
            {
                continue;
            }

            if (!gradeMaskByPetId.TryGetValue(petRow.Id, out int gradeMask) || gradeMask != 7)
            {
                Log.Error(
                    "校验宠物产出表失败，宠物 '{0}'（PetId={1}）必须正好配置 Primary / Intermediate / Advanced 三档产出。",
                    petRow.Code,
                    petRow.Id);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 将产出等级转换为校验用的位掩码。
    /// </summary>
    /// <param name="grade">产出等级。</param>
    /// <returns>对应位掩码；未知等级返回 0。</returns>
    private static int GetProduceGradeMask(ProduceGradeType grade)
    {
        switch (grade)
        {
            case ProduceGradeType.Primary:
                return 1;

            case ProduceGradeType.Intermediate:
                return 2;

            case ProduceGradeType.Advanced:
                return 4;

            default:
                return 0;
        }
    }

    /// <summary>
    /// 通知外部当前数据表可用状态已变化。
    /// </summary>
    private void NotifyLoadStateChanged()
    {
        LoadStateChanged?.Invoke();
    }
}
