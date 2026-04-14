using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// MainUIForm 建筑升级入口分部类。
/// 负责主界面上的“建筑升级”按钮缓存、监听与升级窗体打开关闭。
/// </summary>
public partial class MainUIForm
{
    /// <summary>
    /// 主界面上的建筑升级按钮。
    /// 对应 BJ 页面下的 GoJianZhuShengJi。
    /// </summary>
    [SerializeField]
    private Button _btnArchitectureUpgrade;

    /// <summary>
    /// 当前已打开的建筑升级窗体序列号。
    /// 为 0 表示当前没有记录到活动中的窗体。
    /// </summary>
    private int _architectureUpgradeUIFormId;

    /// <summary>
    /// 缓存建筑升级入口引用。
    /// 如果 Inspector 没拖，就按当前 prefab 结构自动查找。
    /// </summary>
    private void CacheArchitectureReferences()
    {
        if (_btnArchitectureUpgrade != null)
        {
            return;
        }

        if (_pageCenter == null)
        {
            return;
        }

        Transform architectureUpgradeButton = _pageCenter.Find("GoJianZhuShengJi");
        if (architectureUpgradeButton != null)
        {
            _btnArchitectureUpgrade = architectureUpgradeButton.GetComponent<Button>();
        }
    }

    /// <summary>
    /// 初始化建筑升级入口。
    /// 这里只做按钮监听注册，不在主界面常驻维护任何升级业务状态。
    /// </summary>
    private void InitializeArchitectureView()
    {
        CacheArchitectureReferences();
        if (_btnArchitectureUpgrade == null)
        {
            Log.Warning("MainUIForm can not find GoJianZhuShengJi.");
            return;
        }

        _btnArchitectureUpgrade.onClick.RemoveListener(OnArchitectureUpgradeClicked);
        _btnArchitectureUpgrade.onClick.AddListener(OnArchitectureUpgradeClicked);
    }

    /// <summary>
    /// 主界面打开时刷新建筑升级入口。
    /// 当前入口没有额外状态，因此这里只保留扩展点。
    /// </summary>
    private void OpenArchitectureView()
    {
    }

    /// <summary>
    /// 主界面关闭时同步关闭建筑升级窗体。
    /// 避免主界面销毁后弹窗残留。
    /// </summary>
    private void CloseArchitectureView()
    {
        CloseArchitectureUpgradeUIForm();
    }

    /// <summary>
    /// 主界面销毁时清理按钮监听。
    /// </summary>
    private void DestroyArchitectureView()
    {
        if (_btnArchitectureUpgrade != null)
        {
            _btnArchitectureUpgrade.onClick.RemoveListener(OnArchitectureUpgradeClicked);
        }

        _architectureUpgradeUIFormId = 0;
    }

    /// <summary>
    /// 建筑升级按钮点击回调。
    /// 若窗体已经打开，则不重复打开第二份实例。
    /// </summary>
    private void OnArchitectureUpgradeClicked()
    {
        if (GameEntry.UI == null)
        {
            Log.Warning("MainUIForm can not open architecture upgrade UI because UIComponent is missing.");
            return;
        }

        if (_architectureUpgradeUIFormId > 0 && GameEntry.UI.HasUIForm(_architectureUpgradeUIFormId))
        {
            return;
        }

        _architectureUpgradeUIFormId = GameEntry.UI.OpenUIForm(UIFormDefine.ArchitectureUpgradeUIForm, UIFormDefine.MainGroup);
    }

    /// <summary>
    /// 关闭当前记录到的建筑升级窗体。
    /// 这里只通过 SerialId 精确关闭，避免误伤同组其他界面。
    /// </summary>
    private void CloseArchitectureUpgradeUIForm()
    {
        if (_architectureUpgradeUIFormId <= 0)
        {
            return;
        }

        if (GameEntry.UI != null && GameEntry.UI.HasUIForm(_architectureUpgradeUIFormId))
        {
            GameEntry.UI.CloseUIForm(_architectureUpgradeUIFormId);
        }

        _architectureUpgradeUIFormId = 0;
    }
}
