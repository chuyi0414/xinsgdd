//------------------------------------------------------------
// Game Framework
// Project Extension: procedure startup gate.
//------------------------------------------------------------

namespace UnityGameFramework.Runtime
{
    /// <summary>
    /// 流程启动门闩。
    /// 用途：项目启动阶段存在 Addressables catalog 初始化这类异步前置条件时，
    /// 允许入口脚本先阻塞 ProcedureComponent 的入口流程启动，等关键资源系统就绪后再放行。
    /// </summary>
    public static class ProcedureStartupGate
    {
        /// <summary>
        /// 当前是否允许 ProcedureComponent 启动入口流程。
        /// 初始为 true，保证未接入本门闩的场景仍保持 GF 原始行为。
        /// </summary>
        private static bool s_CanStartProcedure = true;

        /// <summary>
        /// 当前是否允许启动入口流程。
        /// </summary>
        public static bool CanStartProcedure
        {
            get { return s_CanStartProcedure; }
        }

        /// <summary>
        /// 阻塞入口流程启动。
        /// 必须在 Unity 所有 Start 执行前调用，推荐放在 GameEntry.Awake。
        /// </summary>
        public static void BlockStartup()
        {
            s_CanStartProcedure = false;
        }

        /// <summary>
        /// 放行入口流程启动。
        /// 推荐在 Addressables catalog 与业务模块初始化完成后调用。
        /// </summary>
        public static void AllowStartup()
        {
            s_CanStartProcedure = true;
        }
    }
}
