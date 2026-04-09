using System;
using GameFramework.Event;
using GameFramework.Procedure;
using Spine.Unity;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 加载流程
/// </summary>
public class LoadProcedure : ProcedureBase
{
    private static readonly string EggDataTableAssetName = AssetPath.GetDataTable("Egg");
    private static readonly string PetDataTableAssetName = AssetPath.GetDataTable("Pet");

    // 流程间传递“待关闭界面”序列号的键名。
    private const string PendingCloseUIFormIdDataName = "PendingCloseUIFormId";

    //加载界面ID
    private int _loadUIFormId;
    // 当前是否已订阅数据表加载事件。
    private bool _isListeningDataTableEvents;

    protected override void OnEnter(ProcedureOwner procedureOwner)
    {
        SubscribeDataTableEvents();
        _loadUIFormId = GameEntry.UI.OpenUIForm(UIFormDefine.LoadUIForm, UIFormDefine.MainGroup);
        RefreshLoadButtonState(AreRequiredDataTablesReady());
        BeginLoadEggDataTable();
        BeginLoadPetDataTable();

        base.OnEnter(procedureOwner);
    }

    protected override void OnLeave(ProcedureOwner procedureOwner, bool isShutdown)
    {
        UnsubscribeDataTableEvents();

        if (isShutdown)
        {
            // 整个流程关闭时直接回收当前界面。
            CloseLoadUIForm();
        }
        else
        {
            // 保持加载界面直到下一个界面真正打开，避免切流程时露出黑底。
            procedureOwner.SetData<VarInt32>(PendingCloseUIFormIdDataName, _loadUIFormId);
            _loadUIFormId = 0;
        }

        base.OnLeave(procedureOwner, isShutdown);
    }

    /// <summary>
    /// 关闭当前加载界面。
    /// </summary>
    private void CloseLoadUIForm()
    {
        if (_loadUIFormId <= 0 || !GameEntry.UI.HasUIForm(_loadUIFormId))
        {
            _loadUIFormId = 0;
            return;
        }

        GameEntry.UI.CloseUIForm(_loadUIFormId);
        _loadUIFormId = 0;
    }

    /// <summary>
    /// 开始加载蛋系统数据表。
    /// </summary>
    private void BeginLoadEggDataTable()
    {
        if (GameEntry.DataTables != null && GameEntry.DataTables.IsAvailable<EggDataRow>())
        {
            RefreshLoadButtonState(AreRequiredDataTablesReady());
            return;
        }

        if (GameEntry.DataTables == null)
        {
            Log.Error("通用数据表模块不存在，无法加载蛋系统配置。");
            RefreshLoadButtonState(AreRequiredDataTablesReady());
            return;
        }

        var eggDataTable = GameEntry.DataTables.EnsureDataTable<EggDataRow>();
        if (eggDataTable == null)
        {
            Log.Error("创建蛋系统数据表失败。");
            RefreshLoadButtonState(AreRequiredDataTablesReady());
            return;
        }

        if (eggDataTable.Count > 0)
        {
            TryRegisterEggDataTable(eggDataTable);
            RefreshLoadButtonState(AreRequiredDataTablesReady());
            return;
        }

        RefreshLoadButtonState(AreRequiredDataTablesReady());
        ((GameFramework.DataTable.DataTableBase)eggDataTable).ReadData(EggDataTableAssetName);
    }

    /// <summary>
    /// 开始加载宠物系统数据表。
    /// </summary>
    private void BeginLoadPetDataTable()
    {
        if (GameEntry.DataTables != null && GameEntry.DataTables.IsAvailable<PetDataRow>())
        {
            RefreshLoadButtonState(AreRequiredDataTablesReady());
            return;
        }

        if (GameEntry.DataTables == null)
        {
            Log.Error("通用数据表模块不存在，无法加载宠物系统配置。");
            RefreshLoadButtonState(AreRequiredDataTablesReady());
            return;
        }

        var petDataTable = GameEntry.DataTables.EnsureDataTable<PetDataRow>();
        if (petDataTable == null)
        {
            Log.Error("创建宠物系统数据表失败。");
            RefreshLoadButtonState(AreRequiredDataTablesReady());
            return;
        }

        if (petDataTable.Count > 0)
        {
            TryRegisterPetDataTable(petDataTable);
            RefreshLoadButtonState(AreRequiredDataTablesReady());
            return;
        }

        RefreshLoadButtonState(AreRequiredDataTablesReady());
        ((GameFramework.DataTable.DataTableBase)petDataTable).ReadData(PetDataTableAssetName);
    }

    /// <summary>
    /// 订阅蛋系统数据表加载事件。
    /// </summary>
    private void SubscribeDataTableEvents()
    {
        if (_isListeningDataTableEvents)
        {
            return;
        }

        GameEntry.Event.Subscribe(LoadDataTableSuccessEventArgs.EventId, OnLoadDataTableSuccess);
        GameEntry.Event.Subscribe(LoadDataTableFailureEventArgs.EventId, OnLoadDataTableFailure);
        _isListeningDataTableEvents = true;
    }

    /// <summary>
    /// 取消订阅蛋系统数据表加载事件。
    /// </summary>
    private void UnsubscribeDataTableEvents()
    {
        if (!_isListeningDataTableEvents)
        {
            return;
        }

        GameEntry.Event.Unsubscribe(LoadDataTableSuccessEventArgs.EventId, OnLoadDataTableSuccess);
        GameEntry.Event.Unsubscribe(LoadDataTableFailureEventArgs.EventId, OnLoadDataTableFailure);
        _isListeningDataTableEvents = false;
    }

    /// <summary>
    /// 蛋系统数据表加载成功回调。
    /// </summary>
    private void OnLoadDataTableSuccess(object sender, GameEventArgs e)
    {
        var ne = e as LoadDataTableSuccessEventArgs;
        if (ne == null)
        {
            return;
        }

        if (string.Equals(ne.DataTableAssetName, EggDataTableAssetName, StringComparison.Ordinal))
        {
            var eggDataTable = GameEntry.DataTable.GetDataTable<EggDataRow>();
            TryRegisterEggDataTable(eggDataTable);
        }
        else if (string.Equals(ne.DataTableAssetName, PetDataTableAssetName, StringComparison.Ordinal))
        {
            var petDataTable = GameEntry.DataTable.GetDataTable<PetDataRow>();
            TryRegisterPetDataTable(petDataTable);
        }

        RefreshLoadButtonState(AreRequiredDataTablesReady());
    }

    /// <summary>
    /// 蛋系统数据表加载失败回调。
    /// </summary>
    private void OnLoadDataTableFailure(object sender, GameEventArgs e)
    {
        var ne = e as LoadDataTableFailureEventArgs;
        if (ne == null)
        {
            return;
        }

        if (string.Equals(ne.DataTableAssetName, EggDataTableAssetName, StringComparison.Ordinal))
        {
            Log.Error("加载蛋系统数据表失败：{0}", ne.ErrorMessage);
            GameEntry.DataTables?.Clear<EggDataRow>();
        }
        else if (string.Equals(ne.DataTableAssetName, PetDataTableAssetName, StringComparison.Ordinal))
        {
            Log.Error("加载宠物系统数据表失败：{0}", ne.ErrorMessage);
            GameEntry.DataTables?.Clear<PetDataRow>();
        }

        RefreshLoadButtonState(AreRequiredDataTablesReady());
    }

    /// <summary>
    /// 注册蛋系统数据表到通用模块。
    /// </summary>
    private bool TryRegisterEggDataTable(GameFramework.DataTable.IDataTable<EggDataRow> eggDataTable)
    {
        if (GameEntry.DataTables == null)
        {
            Log.Error("通用数据表模块未初始化。");
            return false;
        }

        if (!GameEntry.DataTables.Register(eggDataTable))
        {
            Log.Error("蛋系统数据表注册失败。");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 注册宠物系统数据表到通用模块。
    /// </summary>
    private bool TryRegisterPetDataTable(GameFramework.DataTable.IDataTable<PetDataRow> petDataTable)
    {
        if (GameEntry.DataTables == null)
        {
            Log.Error("通用数据表模块未初始化。");
            return false;
        }

        if (!ValidatePetSkeletonDataAssets(petDataTable))
        {
            GameEntry.DataTables.Clear<PetDataRow>();
            return false;
        }

        if (!GameEntry.DataTables.Register(petDataTable))
        {
            Log.Error("宠物系统数据表注册失败。");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 校验宠物表中的 SkeletonData 资源是否存在。
    /// </summary>
    private static bool ValidatePetSkeletonDataAssets(GameFramework.DataTable.IDataTable<PetDataRow> petDataTable)
    {
        if (petDataTable == null)
        {
            Log.Error("校验宠物 SkeletonData 资源失败，数据表为空。");
            return false;
        }

        PetDataRow[] rows = petDataTable.GetAllDataRows();
        if (rows == null || rows.Length == 0)
        {
            Log.Error("校验宠物 SkeletonData 资源失败，数据表为空。");
            return false;
        }

        for (int i = 0; i < rows.Length; i++)
        {
            PetDataRow row = rows[i];
            if (row == null)
            {
                Log.Error("校验宠物 SkeletonData 资源失败，存在空行。");
                return false;
            }

            SkeletonDataAsset skeletonDataAsset = Resources.Load<SkeletonDataAsset>(row.SkeletonDataPath);
            if (skeletonDataAsset == null)
            {
                Log.Error("宠物表配置错误，SkeletonData 资源不存在，Code='{0}'，Path='{1}'。", row.Code, row.SkeletonDataPath);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 必需静态表是否全部可用。
    /// </summary>
    private static bool AreRequiredDataTablesReady()
    {
        return GameEntry.DataTables != null
            && GameEntry.DataTables.IsAvailable<EggDataRow>()
            && GameEntry.DataTables.IsAvailable<PetDataRow>();
    }

    /// <summary>
    /// 刷新加载界面的按钮状态。
    /// </summary>
    private void RefreshLoadButtonState(bool isInteractable)
    {
        if (_loadUIFormId <= 0 || !GameEntry.UI.HasUIForm(_loadUIFormId))
        {
            return;
        }

        UIForm loadUIForm = GameEntry.UI.GetUIForm(_loadUIFormId);
        LoadUIForm loadUIFormLogic = loadUIForm != null ? loadUIForm.Logic as LoadUIForm : null;
        if (loadUIFormLogic == null)
        {
            return;
        }

        loadUIFormLogic.SetLoadButtonInteractable(isInteractable);
    }
}
