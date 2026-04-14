using GameFramework.Event;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityGameFramework.Runtime;


/// <summary>
/// 实体实例 Id 池组件。
/// 用于分配与复用实体实例 Id。
/// </summary>
public sealed class EntityIdPoolComponent : GameFrameworkComponent
{
    /// <summary>
    /// 实体实例 Id 的起始值（避免 0 或负数）。
    /// </summary>
    [SerializeField]
    private int _startId = 1;

    /// <summary>
    /// 可复用的实体实例 Id 队列（优先复用，降低自增压力）。
    /// </summary>
    private readonly Queue<int> _reusableIds = new Queue<int>();

    /// <summary>
    /// 已分配且仍在使用中的实体实例 Id 集合（用于防重复）。
    /// </summary>
    private readonly HashSet<int> _inUseIds = new HashSet<int>();

    /// <summary>
    /// 下一个可分配的自增 Id（当无可复用 Id 时使用）。
    /// </summary>
    private int _nextId;

    /// <summary>
    /// 事件组件引用（用于订阅实体隐藏完成事件）。
    /// </summary>
    private EventComponent _eventComponent;

    /// <summary>
    /// 是否已成功订阅实体隐藏完成事件。
    /// </summary>
    private bool _isSubscribed;

    /// <summary>
    /// 初始化 Id 池基础状态。
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        if (_startId < 1)
        {
            _startId = 1;
        }

        _nextId = _startId;
    }

    /// <summary>
    /// 订阅实体隐藏完成事件，用于自动回收 Id。
    /// </summary>
    private void Start()
    {
        StartCoroutine(SubscribeWhenReady());
    }

    /// <summary>
    /// 等待 Event 组件就绪后再订阅实体隐藏完成事件。
    /// </summary>
    private IEnumerator SubscribeWhenReady()
    {
        int waitFrames = 0;
        while (waitFrames < 120)
        {
            _eventComponent = GameEntry.Event;
            if (_eventComponent != null)
            {
                _eventComponent.Subscribe(HideEntityCompleteEventArgs.EventId, OnHideEntityComplete);
                _isSubscribed = true;
                yield break;
            }

            waitFrames++;
            yield return null;
        }

        Log.Error("订阅实体隐藏事件失败，Event 组件为空。");
    }

    /// <summary>
    /// 释放事件订阅并清理缓存。
    /// </summary>
    private void OnDestroy()
    {
        if (_eventComponent != null && _isSubscribed && Application.isPlaying)
        {
            if (_eventComponent.Check(HideEntityCompleteEventArgs.EventId, OnHideEntityComplete))
            {
                _eventComponent.Unsubscribe(HideEntityCompleteEventArgs.EventId, OnHideEntityComplete);
            }
        }

        _isSubscribed = false;

        _reusableIds.Clear();
        _inUseIds.Clear();
    }

    /// <summary>
    /// 分配一个实体实例 Id（优先复用）。
    /// </summary>
    /// <returns>分配得到的 Id（失败返回 0）。</returns>
    public int Acquire()
    {
        int id = 0;

        while (_reusableIds.Count > 0)
        {
            int candidate = _reusableIds.Dequeue();
            if (candidate > 0 && !_inUseIds.Contains(candidate))
            {
                id = candidate;
                break;
            }
        }

        if (id == 0)
        {
            if (_nextId >= int.MaxValue)
            {
                Log.Error("实体 Id 池已达到 int.MaxValue 且无可复用 Id。");
                return 0;
            }

            id = _nextId++;
        }

        _inUseIds.Add(id);
        return id;
    }

    /// <summary>
    /// 回收实体实例 Id（仅回收已分配的 Id）。
    /// </summary>
    /// <param name="id">要回收的实体实例 Id。</param>
    public void Release(int id)
    {
        if (id <= 0)
        {
            Log.Warning("实体 Id 池回收失败，Id 无效：{0}。", id);
            return;
        }

        if (!_inUseIds.Remove(id))
        {
            Log.Warning("实体 Id 池回收忽略，Id 未处于使用中：{0}。", id);
            return;
        }

        _reusableIds.Enqueue(id);
    }

    /// <summary>
    /// 实体隐藏完成事件回调（自动回收 Id）。
    /// </summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">事件参数。</param>
    private void OnHideEntityComplete(object sender, GameEventArgs e)
    {
        HideEntityCompleteEventArgs ne = e as HideEntityCompleteEventArgs;
        if (ne == null)
        {
            return;
        }

        Release(ne.EntityId);
    }
}
