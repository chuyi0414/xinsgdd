using UnityGameFramework.Runtime;

/// <summary>
/// MainUIForm 水果图鉴分部类。
/// 管理水果图鉴界面（FruitTJUIForm）的打开、关闭与生命周期状态跟踪。
/// 整体结构与 MainUIForm.DailyChallenge.cs 一致：
///   - 在 MainUIForm.OnInit 里调用 InitializeFruitTJView 清零序列号
///   - 在 MainUIForm.OnClose 里调用 CloseFruitTJView 关闭窗体
///   - 在 MainUIForm.OnDestroy 里调用 DestroyFruitTJView 清零序列号
///   - OnBtnFruitTJ 是 GOSGTJ 按钮的点击回调
/// </summary>
public partial class MainUIForm
{
    /// <summary>
    /// 当前已打开的水果图鉴窗体序列号。
    /// 为 0 表示当前没有活动中的水果图鉴界面实例。
    /// </summary>
    private int _fruitTJUIFormId;

    /// <summary>
    /// 初始化水果图鉴相关的运行时状态。
    /// </summary>
    private void InitializeFruitTJView()
    {
        _fruitTJUIFormId = 0;
    }

    /// <summary>
    /// 主界面关闭时关闭水果图鉴窗体。
    /// </summary>
    private void CloseFruitTJView()
    {
        CloseFruitTJUIForm();
    }

    /// <summary>
    /// 主界面销毁时清理水果图鉴状态。
    /// </summary>
    private void DestroyFruitTJView()
    {
        _fruitTJUIFormId = 0;
    }

    /// <summary>
    /// GOSGTJ 按钮点击回调，打开水果图鉴界面。
    /// 无需做页面切换，直接在当前页上层打开独立窗体。
    /// </summary>
    private void OnBtnFruitTJ()
    {
        TryOpenFruitTJUIForm();
    }

    /// <summary>
    /// 尝试打开水果图鉴窗体。
    /// 若当前已有活动实例，则不重复打开。
    /// </summary>
    private void TryOpenFruitTJUIForm()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
        if (GameEntry.UI == null)
        {
            Log.Warning("MainUIForm 无法打开水果图鉴界面，UIComponent 缺失。");
            return;
        }

        // 防止重复打开
        if (_fruitTJUIFormId > 0 && GameEntry.UI.HasUIForm(_fruitTJUIFormId))
        {
            return;
        }

        _fruitTJUIFormId = GameEntry.UI.OpenUIForm(UIFormDefine.FruitTJUIForm, UIFormDefine.MainGroup);
    }

    /// <summary>
    /// 关闭当前记录到的水果图鉴窗体。
    /// </summary>
    private void CloseFruitTJUIForm()
    {
        if (_fruitTJUIFormId <= 0)
        {
            return;
        }

        if (GameEntry.UI != null && GameEntry.UI.HasUIForm(_fruitTJUIFormId))
        {
            GameEntry.UI.CloseUIForm(_fruitTJUIFormId);
        }

        _fruitTJUIFormId = 0;
    }
}
