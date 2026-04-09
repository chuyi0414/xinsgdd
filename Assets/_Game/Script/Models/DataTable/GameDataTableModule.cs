using System;
using System.Collections.Generic;
using GameFramework.DataTable;
using UnityGameFramework.Runtime;

/// <summary>
/// 通用数据表模块。
/// 统一管理业务层所有 DataTable 的初始化、缓存与查询。
/// </summary>
public sealed class GameDataTableModule
{
    private readonly Dictionary<Type, object> _dataTables = new Dictionary<Type, object>();
    private readonly Dictionary<Type, Dictionary<string, int>> _rowIdsByCode = new Dictionary<Type, Dictionary<string, int>>();

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
    }

    /// <summary>
    /// 清空全部数据表缓存。
    /// </summary>
    public void ClearAll()
    {
        _dataTables.Clear();
        _rowIdsByCode.Clear();
    }
}
