using System;

namespace GameFramework.Timer
{
    /// <summary>
    /// 计时器模块接口（由框架统一轮询驱动）。
    /// </summary>
    public interface ITimerManager
    {
        /// <summary>
        /// 获取当前激活计时器数量（不含已完成计时器）。
        /// </summary>
        int ActiveCount { get; }

        /// <summary>
        /// 设置计时器列表初始容量（用于减少运行时扩容开销）。
        /// </summary>
        /// <param name="capacity">期望的初始容量（小于 1 时按 1 处理）。</param>
        void SetInitialCapacity(int capacity);

        /// <summary>
        /// 延迟执行（可指定时间模式）。
        /// </summary>
        /// <param name="seconds">延迟时长（秒）。</param>
        /// <param name="onComplete">完成回调。</param>
        /// <param name="useUnscaledTime">是否使用真实时间（不受 Time.timeScale 影响）。</param>
        /// <returns>返回计时器句柄。</returns>
        Timer Delay(float seconds, Action onComplete, bool useUnscaledTime);

        /// <summary>
        /// 循环执行（可指定时间模式）。
        /// </summary>
        /// <param name="interval">循环间隔（秒）。</param>
        /// <param name="onTick">每次触发回调。</param>
        /// <param name="useUnscaledTime">是否使用真实时间（不受 Time.timeScale 影响）。</param>
        /// <returns>返回计时器句柄。</returns>
        Timer Loop(float interval, Action onTick, bool useUnscaledTime);

        /// <summary>
        /// 下一帧执行（真正的下一帧，非计时器列表）。
        /// </summary>
        /// <param name="onComplete">下一帧回调。</param>
        void NextFrame(Action onComplete);

        /// <summary>
        /// 取消指定计时器（按 ID）。
        /// </summary>
        /// <param name="timerId">计时器 ID。</param>
        void Cancel(int timerId);

        /// <summary>
        /// 尝试取消计时器（找不到返回 false）。
        /// </summary>
        /// <param name="timerId">计时器 ID。</param>
        /// <returns>是否成功取消。</returns>
        bool TryCancel(int timerId);

        /// <summary>
        /// 获取计时器实例（按 ID）。
        /// </summary>
        /// <param name="timerId">计时器 ID。</param>
        /// <returns>计时器实例（不存在返回 null）。</returns>
        Timer GetTimer(int timerId);

        /// <summary>
        /// 是否存在指定计时器（按 ID）。
        /// </summary>
        /// <param name="timerId">计时器 ID。</param>
        /// <returns>是否存在且未完成。</returns>
        bool HasTimer(int timerId);

        /// <summary>
        /// 暂停计时器（按 ID）。
        /// </summary>
        /// <param name="timerId">计时器 ID。</param>
        void Pause(int timerId);

        /// <summary>
        /// 恢复计时器（按 ID）。
        /// </summary>
        /// <param name="timerId">计时器 ID。</param>
        void Resume(int timerId);

        /// <summary>
        /// 取消所有计时器（不触发回调）。
        /// </summary>
        void CancelAll();
    }
}
