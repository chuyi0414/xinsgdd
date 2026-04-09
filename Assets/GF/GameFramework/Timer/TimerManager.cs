using System;
using System.Collections.Generic;

namespace GameFramework.Timer
{
    /// <summary>
    /// 计时器模块实现（由框架统一轮询驱动）。
    /// </summary>
    internal sealed class TimerManager : GameFrameworkModule, ITimerManager
    {
        /// <summary>
        /// 默认计时器列表初始容量。
        /// </summary>
        private const int DefaultInitialCapacity = 32;

        /// <summary>
        /// 所有激活中的计时器。
        /// </summary>
        private readonly List<Timer> m_Timers = new List<Timer>(DefaultInitialCapacity);

        /// <summary>
        /// 下一帧要执行的回调（真正的下一帧）。
        /// </summary>
        private readonly List<Action> m_NextFrameActions = new List<Action>();

        /// <summary>
        /// 执行中的回调缓存（避免遍历时修改集合）。
        /// </summary>
        private readonly List<Action> m_ExecutingActions = new List<Action>();

        /// <summary>
        /// 计时器 ID 自增计数器。
        /// </summary>
        private int m_NextId = 1;

        /// <summary>
        /// 获取当前激活计时器数量（不含已完成计时器）。
        /// </summary>
        public int ActiveCount => m_Timers.Count;

        /// <summary>
        /// 设置计时器列表初始容量（用于减少运行时扩容开销）。
        /// </summary>
        /// <param name="capacity">期望的初始容量（小于 1 时按 1 处理）。</param>
        public void SetInitialCapacity(int capacity)
        {
            if (capacity < 1)
            {
                capacity = 1;
            }

            if (m_Timers.Capacity < capacity)
            {
                m_Timers.Capacity = capacity;
            }
        }

        /// <summary>
        /// 延迟执行（可指定时间模式）。
        /// </summary>
        /// <param name="seconds">延迟时长（秒）。</param>
        /// <param name="onComplete">完成回调。</param>
        /// <param name="useUnscaledTime">是否使用真实时间（不受 Time.timeScale 影响）。</param>
        /// <returns>返回计时器句柄。</returns>
        public Timer Delay(float seconds, Action onComplete, bool useUnscaledTime)
        {
            Timer timer = new Timer(seconds, onComplete, false, useUnscaledTime) { Id = m_NextId++ };
            m_Timers.Add(timer);
            return timer;
        }

        /// <summary>
        /// 循环执行（可指定时间模式）。
        /// </summary>
        /// <param name="interval">循环间隔（秒）。</param>
        /// <param name="onTick">每次触发回调。</param>
        /// <param name="useUnscaledTime">是否使用真实时间（不受 Time.timeScale 影响）。</param>
        /// <returns>返回计时器句柄。</returns>
        public Timer Loop(float interval, Action onTick, bool useUnscaledTime)
        {
            Timer timer = new Timer(interval, onTick, true, useUnscaledTime) { Id = m_NextId++ };
            m_Timers.Add(timer);
            return timer;
        }

        /// <summary>
        /// 下一帧执行（真正的下一帧，非计时器列表）。
        /// </summary>
        /// <param name="onComplete">下一帧回调。</param>
        public void NextFrame(Action onComplete)
        {
            if (onComplete != null)
            {
                m_NextFrameActions.Add(onComplete);
            }
        }

        /// <summary>
        /// 取消指定计时器（按 ID）。
        /// </summary>
        /// <param name="timerId">计时器 ID。</param>
        public void Cancel(int timerId)
        {
            RemoveTimerById(timerId, out _);
        }

        /// <summary>
        /// 尝试取消计时器（找不到返回 false）。
        /// </summary>
        /// <param name="timerId">计时器 ID。</param>
        /// <returns>是否成功取消。</returns>
        public bool TryCancel(int timerId)
        {
            return RemoveTimerById(timerId, out _);
        }

        /// <summary>
        /// 获取计时器实例（按 ID）。
        /// </summary>
        /// <param name="timerId">计时器 ID。</param>
        /// <returns>计时器实例（不存在返回 null）。</returns>
        public Timer GetTimer(int timerId)
        {
            int index = FindTimerIndex(timerId);
            return index >= 0 ? m_Timers[index] : null;
        }

        /// <summary>
        /// 是否存在指定计时器（按 ID）。
        /// </summary>
        /// <param name="timerId">计时器 ID。</param>
        /// <returns>是否存在且未完成。</returns>
        public bool HasTimer(int timerId)
        {
            Timer timer = GetTimer(timerId);
            return timer != null && !timer.IsCompleted;
        }

        /// <summary>
        /// 暂停计时器（按 ID）。
        /// </summary>
        /// <param name="timerId">计时器 ID。</param>
        public void Pause(int timerId)
        {
            GetTimer(timerId)?.Pause();
        }

        /// <summary>
        /// 恢复计时器（按 ID）。
        /// </summary>
        /// <param name="timerId">计时器 ID。</param>
        public void Resume(int timerId)
        {
            GetTimer(timerId)?.Resume();
        }

        /// <summary>
        /// 取消所有计时器（不触发回调）。
        /// </summary>
        public void CancelAll()
        {
            m_Timers.Clear();
        }

        /// <summary>
        /// 模块轮询更新（由框架统一调用）。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（秒）。</param>
        /// <param name="realElapseSeconds">真实流逝时间（秒）。</param>
        internal override void Update(float elapseSeconds, float realElapseSeconds)
        {
            ExecuteNextFrameActions();
            UpdateTimers(elapseSeconds, realElapseSeconds);
        }

        /// <summary>
        /// 模块关闭并清理。
        /// </summary>
        internal override void Shutdown()
        {
            m_Timers.Clear();
            m_NextFrameActions.Clear();
            m_ExecutingActions.Clear();
            m_NextId = 1;
        }

        /// <summary>
        /// 执行下一帧回调队列。
        /// </summary>
        private void ExecuteNextFrameActions()
        {
            if (m_NextFrameActions.Count <= 0)
            {
                return;
            }

            m_ExecutingActions.Clear();
            m_ExecutingActions.AddRange(m_NextFrameActions);
            m_NextFrameActions.Clear();

            for (int i = 0; i < m_ExecutingActions.Count; i++)
            {
                try
                {
                    m_ExecutingActions[i]?.Invoke();
                }
                catch (Exception exception)
                {
                    GameFrameworkLog.Warning("Timer NextFrame callback exception: {0}", exception.Message);
                }
            }
        }

        /// <summary>
        /// 使用框架时间更新计时器列表。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（秒）。</param>
        /// <param name="realElapseSeconds">真实流逝时间（秒）。</param>
        private void UpdateTimers(float elapseSeconds, float realElapseSeconds)
        {
            for (int i = m_Timers.Count - 1; i >= 0; i--)
            {
                Timer timer = m_Timers[i];
                float step = timer.UseUnscaledTime ? realElapseSeconds : elapseSeconds;
                if (timer.Update(step))
                {
                    m_Timers.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 查找计时器索引（按 ID）。
        /// </summary>
        /// <param name="timerId">计时器 ID。</param>
        /// <returns>索引（不存在返回 -1）。</returns>
        private int FindTimerIndex(int timerId)
        {
            if (timerId <= 0)
            {
                return -1;
            }

            for (int i = 0; i < m_Timers.Count; i++)
            {
                if (m_Timers[i] != null && m_Timers[i].Id == timerId)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 根据 ID 移除计时器。
        /// </summary>
        /// <param name="timerId">计时器 ID。</param>
        /// <param name="timer">移除到的计时器实例。</param>
        /// <returns>是否成功移除。</returns>
        private bool RemoveTimerById(int timerId, out Timer timer)
        {
            timer = null;
            int index = FindTimerIndex(timerId);
            if (index < 0)
            {
                return false;
            }

            timer = m_Timers[index];
            timer?.Stop();
            m_Timers.RemoveAt(index);
            return true;
        }
    }
}
