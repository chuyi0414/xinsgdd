using UnityGameFramework.Runtime;

/// <summary>
/// MainUIForm 宠物图鉴分部类。
/// 管理宠物图鉴界面（PetTJUIForm）的打开、关闭与生命周期状态跟踪。
/// 整体结构与水果图鉴分部类保持一致：
///   - 在 MainUIForm.OnInit 里调用 InitializePetTJView 清零序列号
///   - 在 MainUIForm.OnClose 里调用 ClosePetTJView 关闭窗体
///   - 在 MainUIForm.OnDestroy 里调用 DestroyPetTJView 清零序列号
///   - OnBtnPetTJ 是 GOCWTJ 按钮的点击回调
/// </summary>
public partial class MainUIForm
{
    /// <summary>
    /// 当前已打开的宠物图鉴窗体序列号。
    /// 为 0 表示当前没有活动中的宠物图鉴界面实例。
    /// </summary>
    private int _petTJUIFormId;

    /// <summary>
    /// 初始化宠物图鉴相关的运行时状态。
    /// </summary>
    private void InitializePetTJView()
    {
        _petTJUIFormId = 0;
    }

    /// <summary>
    /// 主界面关闭时关闭宠物图鉴窗体。
    /// </summary>
    private void ClosePetTJView()
    {
        ClosePetTJUIForm();
    }

    /// <summary>
    /// 主界面销毁时清理宠物图鉴状态。
    /// </summary>
    private void DestroyPetTJView()
    {
        _petTJUIFormId = 0;
    }

    /// <summary>
    /// GOCWTJ 按钮点击回调，打开宠物图鉴界面。
    /// 无需做页面切换，直接在当前页上层打开独立窗体。
    /// </summary>
    private void OnBtnPetTJ()
    {
        TryOpenPetTJUIForm();
    }

    /// <summary>
    /// 尝试打开宠物图鉴窗体。
    /// 若当前已有活动实例，则不重复打开。
    /// </summary>
    private void TryOpenPetTJUIForm()
    {
        if (GameEntry.UI == null)
        {
            Log.Warning("MainUIForm 无法打开宠物图鉴界面，UIComponent 缺失。");
            return;
        }

        // 防止重复打开
        if (_petTJUIFormId > 0 && GameEntry.UI.HasUIForm(_petTJUIFormId))
        {
            return;
        }

        _petTJUIFormId = GameEntry.UI.OpenUIForm(UIFormDefine.PetTJUIForm, UIFormDefine.MainGroup);
    }

    /// <summary>
    /// 关闭当前记录到的宠物图鉴窗体。
    /// </summary>
    private void ClosePetTJUIForm()
    {
        if (_petTJUIFormId <= 0)
        {
            return;
        }

        if (GameEntry.UI != null && GameEntry.UI.HasUIForm(_petTJUIFormId))
        {
            GameEntry.UI.CloseUIForm(_petTJUIFormId);
        }

        _petTJUIFormId = 0;
    }
}
