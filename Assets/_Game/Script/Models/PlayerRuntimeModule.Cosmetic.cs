using System;
using System.Collections.Generic;
using UnityGameFramework.Runtime;

/// <summary>
/// 玩家运行时模块 — 外观装饰部分。
/// 负责宠物解锁、头像/头像框的解锁与选中状态管理。
/// </summary>
public sealed partial class PlayerRuntimeModule
{
    // ───────────── 外观字段 ─────────────

    /// <summary>
    /// 当前会话内已解锁的宠物 Code 集合。
    /// 宠物孵化成功后会写入这里，供宠物图鉴界面直接查询当前解锁状态。
    /// </summary>
    private readonly HashSet<string> _unlockedPetCodes = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前会话内已解锁的头像 Code 集合。
    /// 默认解锁的头像在初始化时写入，购买解锁的头像在购买成功后写入。
    /// </summary>
    private readonly HashSet<string> _unlockedHeadPortraitCodes = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前会话内已解锁的头像框 Code 集合。
    /// 默认解锁的头像框在初始化时写入，购买解锁的头像框在购买成功后写入。
    /// </summary>
    private readonly HashSet<string> _unlockedHeadPortraitFrameCodes = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前选中的头像 Code。
    /// 初始化时取第一个默认解锁的头像，切换时由 UI 写入。
    /// </summary>
    private string _selectedHeadPortraitCode;

    /// <summary>
    /// 当前选中的头像框 Code。
    /// 初始化时取第一个默认解锁的头像框，切换时由 UI 写入。
    /// </summary>
    private string _selectedHeadPortraitFrameCode;

    // ───────────── 宠物解锁 ─────────────

    /// <summary>
    /// 判断指定宠物在当前会话内是否已解锁。
    /// </summary>
    /// <param name="petCode">宠物 Code。</param>
    /// <returns>是否已解锁。</returns>
    public bool IsPetUnlocked(string petCode)
    {
        if (!EnsureInitialized() || string.IsNullOrWhiteSpace(petCode))
        {
            return false;
        }

        return _unlockedPetCodes.Contains(petCode);
    }

    /// <summary>
    /// 在当前会话内解锁指定宠物。
    /// </summary>
    /// <param name="petCode">宠物 Code。</param>
    /// <returns>是否成功解锁。</returns>
    public bool TryUnlockPet(string petCode)
    {
        if (!EnsureInitialized() || string.IsNullOrWhiteSpace(petCode))
        {
            return false;
        }

        PetDataRow petRow = GameEntry.DataTables.GetDataRowByCode<PetDataRow>(petCode);
        if (petRow == null)
        {
            Log.Warning("PlayerRuntimeModule can not unlock pet because code '{0}' is invalid.", petCode);
            return false;
        }

        if (!_unlockedPetCodes.Add(petCode))
        {
            return true;
        }

        return true;
    }

    // ───────────── 头像 ─────────────

    /// <summary>
    /// 判断指定头像在当前会话内是否已解锁。
    /// </summary>
    /// <param name="headPortraitCode">头像 Code。</param>
    /// <returns>是否已解锁。</returns>
    public bool IsHeadPortraitUnlocked(string headPortraitCode)
    {
        if (string.IsNullOrWhiteSpace(headPortraitCode))
        {
            return false;
        }

        if (_unlockedHeadPortraitCodes.Contains(headPortraitCode))
        {
            return true;
        }

        // 兜底：运行时集合未写入时（初始化时数据表未就绪），
        // 直接查数据行的 IsDefaultUnlocked 字段
        HeadPortraitDataRow row = GameEntry.DataTables?.GetDataRowByCode<HeadPortraitDataRow>(headPortraitCode);
        return row != null && row.IsDefaultUnlocked;
    }

    /// <summary>
    /// 在当前会话内解锁指定头像。
    /// </summary>
    /// <param name="headPortraitCode">头像 Code。</param>
    /// <returns>是否成功解锁。</returns>
    public bool TryUnlockHeadPortrait(string headPortraitCode)
    {
        if (string.IsNullOrWhiteSpace(headPortraitCode))
        {
            return false;
        }

        HeadPortraitDataRow row = GameEntry.DataTables?.GetDataRowByCode<HeadPortraitDataRow>(headPortraitCode);
        if (row == null)
        {
            Log.Warning("PlayerRuntimeModule can not unlock head portrait because code '{0}' is invalid.", headPortraitCode);
            return false;
        }

        if (!_unlockedHeadPortraitCodes.Add(headPortraitCode))
        {
            return true;
        }

        return true;
    }

    /// <summary>
    /// 获取当前选中的头像 Code。
    /// </summary>
    public string SelectedHeadPortraitCode => _selectedHeadPortraitCode;

    /// <summary>
    /// 设置当前选中的头像 Code。
    /// 仅在已解锁的情况下才允许切换。
    /// </summary>
    /// <param name="headPortraitCode">头像 Code。</param>
    /// <returns>是否切换成功。</returns>
    public bool TrySetSelectedHeadPortrait(string headPortraitCode)
    {
        if (string.IsNullOrWhiteSpace(headPortraitCode))
        {
            return false;
        }

        if (!IsHeadPortraitUnlocked(headPortraitCode))
        {
            return false;
        }

        _selectedHeadPortraitCode = headPortraitCode;
        return true;
    }

    // ───────────── 头像框 ─────────────

    /// <summary>
    /// 判断指定头像框在当前会话内是否已解锁。
    /// </summary>
    /// <param name="headPortraitFrameCode">头像框 Code。</param>
    /// <returns>是否已解锁。</returns>
    public bool IsHeadPortraitFrameUnlocked(string headPortraitFrameCode)
    {
        if (string.IsNullOrWhiteSpace(headPortraitFrameCode))
        {
            return false;
        }

        if (_unlockedHeadPortraitFrameCodes.Contains(headPortraitFrameCode))
        {
            return true;
        }

        // 兜底：运行时集合未写入时，直接查数据行的 IsDefaultUnlocked 字段
        HeadPortraitFrameDataRow row = GameEntry.DataTables?.GetDataRowByCode<HeadPortraitFrameDataRow>(headPortraitFrameCode);
        return row != null && row.IsDefaultUnlocked;
    }

    /// <summary>
    /// 在当前会话内解锁指定头像框。
    /// </summary>
    /// <param name="headPortraitFrameCode">头像框 Code。</param>
    /// <returns>是否成功解锁。</returns>
    public bool TryUnlockHeadPortraitFrame(string headPortraitFrameCode)
    {
        if (string.IsNullOrWhiteSpace(headPortraitFrameCode))
        {
            return false;
        }

        HeadPortraitFrameDataRow row = GameEntry.DataTables?.GetDataRowByCode<HeadPortraitFrameDataRow>(headPortraitFrameCode);
        if (row == null)
        {
            Log.Warning("PlayerRuntimeModule can not unlock head portrait frame because code '{0}' is invalid.", headPortraitFrameCode);
            return false;
        }

        if (!_unlockedHeadPortraitFrameCodes.Add(headPortraitFrameCode))
        {
            return true;
        }

        return true;
    }

    /// <summary>
    /// 获取当前选中的头像框 Code。
    /// </summary>
    public string SelectedHeadPortraitFrameCode => _selectedHeadPortraitFrameCode;

    /// <summary>
    /// 设置当前选中的头像框 Code。
    /// 仅在已解锁的情况下才允许切换。
    /// </summary>
    /// <param name="headPortraitFrameCode">头像框 Code。</param>
    /// <returns>是否切换成功。</returns>
    public bool TrySetSelectedHeadPortraitFrame(string headPortraitFrameCode)
    {
        if (string.IsNullOrWhiteSpace(headPortraitFrameCode))
        {
            return false;
        }

        if (!IsHeadPortraitFrameUnlocked(headPortraitFrameCode))
        {
            return false;
        }

        _selectedHeadPortraitFrameCode = headPortraitFrameCode;
        return true;
    }

    // ───────────── 外观初始化（由 EnsureInitialized 调用） ─────────────

    /// <summary>
    /// 初始化默认解锁的头像和头像框。
    /// 从数据表中读取 IsDefaultUnlocked 标记，写入运行时集合，
    /// 并取第一个默认解锁项作为初始选中。
    /// </summary>
    private void InitializeCosmetics()
    {
        _unlockedHeadPortraitCodes.Clear();
        _unlockedHeadPortraitFrameCodes.Clear();
        _selectedHeadPortraitCode = null;
        _selectedHeadPortraitFrameCode = null;

        // 初始化默认解锁的头像，并取第一个作为默认选中
        if (GameEntry.DataTables != null && GameEntry.DataTables.IsAvailable<HeadPortraitDataRow>())
        {
            HeadPortraitDataRow[] headPortraitRows = GameEntry.DataTables.GetAllDataRows<HeadPortraitDataRow>();
            for (int i = 0; i < headPortraitRows.Length; i++)
            {
                HeadPortraitDataRow row = headPortraitRows[i];
                if (row != null && row.IsDefaultUnlocked)
                {
                    _unlockedHeadPortraitCodes.Add(row.Code);

                    // 首个默认解锁头像作为初始选中
                    if (string.IsNullOrEmpty(_selectedHeadPortraitCode))
                    {
                        _selectedHeadPortraitCode = row.Code;
                    }
                }
            }
        }

        // 初始化默认解锁的头像框，并取第一个作为默认选中
        if (GameEntry.DataTables != null && GameEntry.DataTables.IsAvailable<HeadPortraitFrameDataRow>())
        {
            HeadPortraitFrameDataRow[] frameRows = GameEntry.DataTables.GetAllDataRows<HeadPortraitFrameDataRow>();
            for (int i = 0; i < frameRows.Length; i++)
            {
                HeadPortraitFrameDataRow row = frameRows[i];
                if (row != null && row.IsDefaultUnlocked)
                {
                    _unlockedHeadPortraitFrameCodes.Add(row.Code);

                    if (string.IsNullOrEmpty(_selectedHeadPortraitFrameCode))
                    {
                        _selectedHeadPortraitFrameCode = row.Code;
                    }
                }
            }
        }
    }
}
