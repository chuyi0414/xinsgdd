using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// MainUIForm 任务系统分部类。
/// 管理任务界面（TaskUIForm）的打开、关闭与生命周期状态跟踪。
/// 整体结构与 MainUIForm.FruitTJ.cs 一致：
///   - 在 MainUIForm.OnInit 里调用 InitializeTaskView 清零序列号
///   - 在 MainUIForm.OnClose 里调用 CloseTaskView 关闭窗体
///   - 在 MainUIForm.OnDestroy 里调用 DestroyTaskView 清零序列号
///   - OnBtnTask 是任务按钮的点击回调
/// </summary>
public partial class MainUIForm
{
    /// <summary>
    /// 任务按钮。
    /// 用户在 Inspector 中自行拖入对应 Button 组件，不做运行时路径查找。
    /// </summary>
    [UnityEngine.SerializeField]
    private Button _btnTask;

    /// <summary>
    /// 当前已打开的任务窗体序列号。
    /// 为 0 表示当前没有活动中的任务界面实例。
    /// </summary>
    private int _taskUIFormId;

    /// <summary>
    /// 初始化任务相关的运行时状态。
    /// </summary>
    private void InitializeTaskView()
    {
        _taskUIFormId = 0;

        if (_btnTask != null)
        {
            _btnTask.onClick.RemoveListener(OnBtnTask);
            _btnTask.onClick.AddListener(OnBtnTask);
        }
    }

    /// <summary>
    /// 主界面关闭时关闭任务窗体。
    /// </summary>
    private void CloseTaskView()
    {
        CloseTaskUIForm();
    }

    /// <summary>
    /// 主界面销毁时清理任务状态。
    /// </summary>
    private void DestroyTaskView()
    {
        if (_btnTask != null)
        {
            _btnTask.onClick.RemoveListener(OnBtnTask);
        }

        _taskUIFormId = 0;
    }

    /// <summary>
    /// 任务按钮点击回调，打开任务界面。
    /// </summary>
    private void OnBtnTask()
    {
        TryOpenTaskUIForm();
    }

    /// <summary>
    /// 尝试打开任务窗体。
    /// 若当前已有活动实例，则不重复打开。
    /// </summary>
    private void TryOpenTaskUIForm()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();

        if (GameEntry.UI == null)
        {
            Log.Warning("MainUIForm 无法打开任务界面，UIComponent 缺失。");
            return;
        }

        // 防止重复打开
        if (_taskUIFormId > 0 && GameEntry.UI.HasUIForm(_taskUIFormId))
        {
            return;
        }

        _taskUIFormId = GameEntry.UI.OpenUIForm(UIFormDefine.TaskUIForm, UIFormDefine.MainGroup);
    }

    /// <summary>
    /// 关闭当前记录到的任务窗体。
    /// </summary>
    private void CloseTaskUIForm()
    {
        if (_taskUIFormId <= 0)
        {
            return;
        }

        if (GameEntry.UI != null && GameEntry.UI.HasUIForm(_taskUIFormId))
        {
            GameEntry.UI.CloseUIForm(_taskUIFormId);
        }

        _taskUIFormId = 0;
    }
}
