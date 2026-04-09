using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 加载界面
/// </summary>
public class LoadUIForm : UIFormLogic
{
    // 加载按钮
    [SerializeField]
    private Button _btnLoad;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);

        if (_btnLoad == null)
        {
            Log.Warning("LoadUIForm can not find BtnLoad.");
            return;
        }

        _btnLoad.onClick.AddListener(OnBtnLoad);
        SetLoadButtonInteractable(CanEnterMain());
    }

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        SetLoadButtonInteractable(CanEnterMain());
    }

    private void OnDestroy()
    {
        if (_btnLoad != null)
        {
            _btnLoad.onClick.RemoveListener(OnBtnLoad);
        }
    }

    /// <summary>
    /// 设置加载按钮是否可点击。
    /// </summary>
    public void SetLoadButtonInteractable(bool isInteractable)
    {
        if (_btnLoad == null)
        {
            return;
        }

        _btnLoad.interactable = isInteractable;
    }

    /// <summary>
    /// 加载按钮点击逻辑
    /// </summary>
    private void OnBtnLoad()
    {
        if (!CanEnterMain())
        {
            Log.Warning("静态配置尚未加载完成，暂时不能进入主界面。");
            return;
        }

        // 切换流程。
        GameFramework.Procedure.ProcedureBase currentProcedure = GameEntry.Procedure.CurrentProcedure;
        currentProcedure.ChangeState<MainProcedure>(currentProcedure.procedureOwner);
    }

    /// <summary>
    /// 当前是否允许进入主界面。
    /// </summary>
    private static bool CanEnterMain()
    {
        return GameEntry.DataTables != null
            && GameEntry.DataTables.IsAvailable<EggDataRow>()
            && GameEntry.DataTables.IsAvailable<PetDataRow>();
    }
}
