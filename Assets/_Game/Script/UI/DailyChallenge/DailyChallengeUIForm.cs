using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 每日一关界面。
/// 当前阶段只承担“入口 UI”职责：
/// 1. 进入界面时不自动加载关卡；
/// 2. 只有点击 BtnStartLevel 才会真正触发 DailyChallenge 棋盘生成；
/// 3. 点击成功后立刻关闭当前窗体，让下页直接展示世界中的卡片实体。
/// </summary>
public sealed class DailyChallengeUIForm : UIFormLogic
{
    /// <summary>
    /// 当前临时预览使用的本地关卡资源路径。
    /// 这里先固定到迁移进来的首份测试关卡，便于快速验证生成链路。
    /// </summary>
    private const string PreviewLevelAssetPath = "Configs/Levels/bbl1";

    /// <summary>
    /// “开始/刷新”按钮。
    /// 当前用于“真正开始加载每日一关预览”的入口按钮。
    /// </summary>
    [SerializeField]
    private Button _btnStartLevel;

    /// <summary>
    /// 当前关卡文本。
    /// 用于显示当前加载的是哪一份本地 CSV。
    /// </summary>
    private TMP_Text _txtCurrentLevel;

    /// <summary>
    /// 通过条件文本。
    /// 当前复用为“提示/错误信息”展示位。
    /// </summary>
    private TMP_Text _txtPassingCriteria;

    /// <summary>
    /// 初始化界面引用。
    /// 这里只做组件缓存与按钮事件绑定，不做实际关卡生成。
    /// </summary>
    /// <param name="userData">界面打开附加参数。</param>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        CacheReferences();
        BindButtonEvents();
    }

    /// <summary>
    /// 页面打开时只刷新文案提示，不自动开始加载关卡。
    /// </summary>
    /// <param name="userData">界面打开附加参数。</param>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        // 从战斗流程返回后重新打开 DailyChallengeUIForm 时，确保 BtnUp 恢复可见。
        MainUIForm mainUIForm = ResolveMainUIForm();
        if (mainUIForm != null)
        {
            mainUIForm.SetBtnUpVisible(true);
        }

        //RefreshIdleTexts();
    }

    /// <summary>
    /// 缓存界面上会用到的核心控件。
    /// 这里统一走名字匹配，避免要求用户额外重新拖 Inspector 引用。
    /// </summary>
    private void CacheReferences()
    {

        TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text text = texts[i];
            if (text == null)
            {
                continue;
            }

            string textName = string.IsNullOrEmpty(text.name) ? string.Empty : text.name.Trim();
            if (_txtCurrentLevel == null && string.Equals(textName, "TxtCurrentLevel", System.StringComparison.Ordinal))
            {
                _txtCurrentLevel = text;
                continue;
            }

            if (_txtPassingCriteria == null && string.Equals(textName, "TxtPassingCriteria", System.StringComparison.Ordinal))
            {
                _txtPassingCriteria = text;
            }
        }
    }

    /// <summary>
    /// 绑定按钮点击事件。
    /// 为了防止重复绑定，先移除再添加。
    /// </summary>
    private void BindButtonEvents()
    {
        if (_btnStartLevel == null)
        {
            return;
        }

        _btnStartLevel.onClick.RemoveListener(OnBtnStartLevel);
        _btnStartLevel.onClick.AddListener(OnBtnStartLevel);
    }

    /// <summary>
    /// “开始/刷新”按钮点击回调。
    /// 隐藏 MainUIForm 的 BtnUp → 关闭自身 → 设置 TransitionToCombat 标记 → 切换到 CombatProcedure。
    /// CombatProcedure.OnEnter 会打开 CombatUIForm；
    /// MainProcedure.OnLeave 检测到 TransitionToCombat 后会保留 MainUIForm 不销毁。
    /// </summary>
    private void OnBtnStartLevel()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
        MainUIForm mainUIForm = ResolveMainUIForm();
        if (mainUIForm == null)
        {
            WriteFailureText("主界面未打开，无法启动每日一关。");
            return;
        }

        if (!mainUIForm.TryStartDailyChallengePreviewFromUIForm(PreviewLevelAssetPath))
        {
            WriteFailureText("关卡加载失败，请检查资源或日志。");
            return;
        }

        // 隐藏 MainUIForm 的上翻按钮，战斗期间不需要返回操作。
        mainUIForm.SetBtnUpVisible(false);

        // 关闭自身，避免战斗界面与每日一关界面重叠。
        if (UIForm != null && GameEntry.UI != null)
        {
            GameEntry.UI.CloseUIForm(UIForm.SerialId);
        }

        // 设置 TransitionToCombat 标记，通知 MainProcedure.OnLeave 保留 MainUIForm。
        GameFramework.Procedure.ProcedureBase currentProcedure = GameEntry.Procedure.CurrentProcedure;
        currentProcedure.procedureOwner.SetData<VarInt32>(MainProcedure.TransitionToCombatDataName, 1);

        // 切换到战斗流程，CombatProcedure.OnEnter 会打开 CombatUIForm。
        currentProcedure.ChangeState<CombatProcedure>(currentProcedure.procedureOwner);
    }

    /// <summary>
    /// 刷新当前界面的默认提示文案。
    /// </summary>
    private void RefreshIdleTexts()
    {
        if (_txtCurrentLevel != null)
        {
            _txtCurrentLevel.text = "当前关卡：bbl1";
        }

        if (_txtPassingCriteria != null)
        {
            _txtPassingCriteria.text = "点击开始加载关卡";
        }
    }

    /// <summary>
    /// 写入一条失败提示文本。
    /// </summary>
    /// <param name="message">失败原因。</param>
    private void WriteFailureText(string message)
    {
        if (_txtCurrentLevel != null)
        {
            _txtCurrentLevel.text = "当前关卡：加载失败";
        }

        if (_txtPassingCriteria != null)
        {
            _txtPassingCriteria.text = message;
        }
    }

    /// <summary>
    /// 获取当前已打开的 MainUIForm 逻辑对象。
    /// DailyChallengeUIForm 本身不持有棋盘控制器，所有真正的世界实体生成都委托给主界面。
    /// </summary>
    /// <returns>主界面逻辑对象；不存在时返回 null。</returns>
    private static MainUIForm ResolveMainUIForm()
    {
        if (GameEntry.UI == null)
        {
            return null;
        }

        UIForm mainUI = GameEntry.UI.GetUIForm(UIFormDefine.MainUIForm);
        return mainUI != null ? mainUI.Logic as MainUIForm : null;
    }
}
