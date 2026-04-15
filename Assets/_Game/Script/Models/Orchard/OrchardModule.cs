using System;
using UnityGameFramework.Runtime;

/// <summary>
/// 果园运行时模块。
/// 当前只维护固定数量的果园位状态，为后续生产流程预留入口。
/// </summary>
public sealed class OrchardModule
{
    /// <summary>
    /// 果园位数量默认值。
    /// 实际运行时数量由 Initialize 方法从 PlayerRuntimeModule 读取。
    /// </summary>
    public const int DefaultOrchardSlotCount = 1;

    /// <summary>
    /// 果园位状态集合。
    /// 延迟到 Initialize 调用后才真正分配，避免硬编码数组大小。
    /// </summary>
    private OrchardSlotState[] _slotStates = Array.Empty<OrchardSlotState>();

    /// <summary>
    /// 果园位数量。
    /// </summary>
    public int SlotCount => _slotStates.Length;

    /// <summary>
    /// 根据运行时数据初始化果园位数量。
    /// 初始化和后续升级都走同一套扩容逻辑，避免重复维护两份数组分配代码。
    /// </summary>
    /// <param name="slotCount">果园位数量，必须大于 0。</param>
    public void Initialize(int slotCount)
    {
        EnsureSlotCapacity(slotCount);
    }

    /// <summary>
    /// 确保果园位容量至少达到指定数量。
    /// 这里只允许扩容，不允许缩容，避免打乱当前果树索引与产出状态。
    /// </summary>
    /// <param name="slotCount">目标果园位数量。</param>
    /// <returns>本次是否实际发生了扩容。</returns>
    public bool EnsureSlotCapacity(int slotCount)
    {
        if (slotCount <= 0)
        {
            slotCount = DefaultOrchardSlotCount;
        }

        if (slotCount <= _slotStates.Length)
        {
            return false;
        }

        OrchardSlotState[] expandedSlotStates = new OrchardSlotState[slotCount];
        if (_slotStates.Length > 0)
        {
            _slotStates.CopyTo(expandedSlotStates, 0);
        }

        for (int i = _slotStates.Length; i < expandedSlotStates.Length; i++)
        {
            expandedSlotStates[i] = new OrchardSlotState();
        }

        _slotStates = expandedSlotStates;
        return true;
    }

    /// <summary>
    /// 获取指定果园位状态。
    /// </summary>
    public OrchardSlotState GetSlotState(int index)
    {
        if (index < 0 || index >= _slotStates.Length)
        {
            Log.Warning("OrchardModule can not get slot state because index '{0}' is invalid.", index);
            return null;
        }

        return _slotStates[index];
    }

    /// <summary>
    /// 获取所有果园位状态。
    /// </summary>
    public OrchardSlotState[] GetAllSlotStates()
    {
        OrchardSlotState[] slotStates = new OrchardSlotState[_slotStates.Length];
        _slotStates.CopyTo(slotStates, 0);
        return slotStates;
    }

    /// <summary>
    /// 尝试获取一个空闲果树并占用它。
    /// 成功时返回果树索引，失败返回 -1。
    /// </summary>
    /// <param name="fruitCode">水果 Code。</param>
    /// <param name="produceSeconds">生产总时长（秒）。</param>
    /// <returns>占用的果树索引，无空闲果树时返回 -1。</returns>
    public int TryAcquireIdleSlot(string fruitCode, float produceSeconds)
    {
        for (int i = 0; i < _slotStates.Length; i++)
        {
            if (_slotStates[i].IsIdle)
            {
                _slotStates[i].Occupy(fruitCode, produceSeconds);
                return i;
            }
        }

        return -1;
    }

    public bool TryGetIdleSlotIndex(out int index)
    {
        for (int i = 0; i < _slotStates.Length; i++)
        {
            if (_slotStates[i].IsIdle)
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    public bool TryOccupySlot(int index, string fruitCode, float produceSeconds)
    {
        if (index < 0 || index >= _slotStates.Length || !_slotStates[index].IsIdle)
        {
            return false;
        }

        _slotStates[index].Occupy(fruitCode, produceSeconds);
        return true;
    }

    /// <summary>
    /// 释放指定果树位。
    /// </summary>
    /// <param name="index">果树索引。</param>
    public void ReleaseSlot(int index)
    {
        if (index < 0 || index >= _slotStates.Length)
        {
            return;
        }

        _slotStates[index].Release();
    }

    /// <summary>
    /// 逐帧推进所有占用中果树的生产倒计时。
    /// </summary>
    /// <param name="deltaTime">本帧推进秒数。</param>
    public void Tick(float deltaTime)
    {
        for (int i = 0; i < _slotStates.Length; i++)
        {
            _slotStates[i].Tick(deltaTime);
        }
    }

    /// <summary>
    /// 重置所有果园位状态。
    /// </summary>
    public void ResetRuntimeState()
    {
        for (int i = 0; i < _slotStates.Length; i++)
        {
            _slotStates[i].Clear();
        }
    }
}
