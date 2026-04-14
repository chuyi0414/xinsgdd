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
        && IsAvailable<PetProduceDataRow>();

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

        if (GameEntry.EggHatch == null)
        {
            Log.Error("EggHatchComponent 未挂载，无法初始化孵化运行时状态。");
            return false;
        }

        GameEntry.EggHatch.EnsureInitialized();
        return true;
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

        if (GameEntry.Fruits != null && !GameEntry.Fruits.EnsureInitialized())
        {
            Log.Error("水果运行时模块初始化失败。");
            Clear<FruitDataRow>();
            return false;
        }

        // Fruit 表与 PetProduce 表是异步加载的，
        // 任一方先完成时都主动触发一次点餐组件初始化尝试。
        GameEntry.PetDiningOrders?.EnsureInitialized();
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

        // PetProduce 表到位后再次尝试初始化点餐组件，
        // 覆盖 Fruit 表更早注册完成的场景。
        GameEntry.PetDiningOrders?.EnsureInitialized();
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
            // 允许异步加载顺序未完成时先跳过，待另一个表注册成功后再重试。
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
