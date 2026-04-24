using GameFramework;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 通用购买确认弹窗。
/// 由业务层通过 PurchaseUIData 传入商品名称和金币价格，
/// BtnYes 确认购买并扣金币，BtnNo 取消关闭。
/// </summary>
public sealed class PurchaseUIForm : UIFormLogic
{
    /// <summary>
    /// 提示文本。
    /// 显示如 "是否购买xxxx头像" 的文案。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtPrompt;

    /// <summary>
    /// 金币信息文本。
    /// 显示 "拥有金币:xxxxx\n价格:xxxxxx"。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtGoldInfo;

    /// <summary>
    /// 确认购买按钮。
    /// </summary>
    [SerializeField]
    private Button _btnYes;

    /// <summary>
    /// 取消按钮。
    /// </summary>
    [SerializeField]
    private Button _btnNo;

    /// <summary>
    /// 当前打开时传入的购买数据。
    /// </summary>
    private PurchaseUIData _currentData;

    /// <summary>
    /// 界面初始化。
    /// </summary>
    /// <param name="userData">用户自定义数据。</param>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        CacheReferences();

        if (_btnYes != null)
        {
            _btnYes.onClick.RemoveListener(OnBtnYesClicked);
            _btnYes.onClick.AddListener(OnBtnYesClicked);
        }

        if (_btnNo != null)
        {
            _btnNo.onClick.RemoveListener(OnBtnNoClicked);
            _btnNo.onClick.AddListener(OnBtnNoClicked);
        }
    }

    /// <summary>
    /// 界面打开时刷新提示文案。
    /// </summary>
    /// <param name="userData">PurchaseUIData 实例。</param>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);

        _currentData = userData as PurchaseUIData;
        if (_currentData == null)
        {
            Log.Warning("PurchaseUIForm 打开时未收到 PurchaseUIData，直接关闭。");
            CloseSelf();
            return;
        }

        RefreshPrompt();
        RefreshGoldInfo();
    }

    /// <summary>
    /// 对象销毁时清理按钮监听。
    /// </summary>
    private void OnDestroy()
    {
        if (_btnYes != null)
        {
            _btnYes.onClick.RemoveListener(OnBtnYesClicked);
        }

        if (_btnNo != null)
        {
            _btnNo.onClick.RemoveListener(OnBtnNoClicked);
        }
    }

    /// <summary>
    /// 缓存节点引用（Inspector 未拖入时自动查找）。
    /// </summary>
    private void CacheReferences()
    {
        if (_txtPrompt == null)
        {
            Transform txtTransform = transform.Find("Purchase/Text (TMP)");
            if (txtTransform != null)
            {
                _txtPrompt = txtTransform.GetComponent<TextMeshProUGUI>();
            }
        }

        if (_btnYes == null)
        {
            Transform btnYesTransform = transform.Find("Purchase/BtnYes");
            if (btnYesTransform != null)
            {
                _btnYes = btnYesTransform.GetComponent<Button>();
            }
        }

        if (_btnNo == null)
        {
            Transform btnNoTransform = transform.Find("Purchase/BtnNo");
            if (btnNoTransform != null)
            {
                _btnNo = btnNoTransform.GetComponent<Button>();
            }
        }

        if (_txtGoldInfo == null)
        {
            Transform txtGoldInfoTransform = transform.Find("Purchase/TxtGoldInfo");
            if (txtGoldInfoTransform != null)
            {
                _txtGoldInfo = txtGoldInfoTransform.GetComponent<TextMeshProUGUI>();
            }
        }
    }

    /// <summary>
    /// 刷新提示文案。
    /// </summary>
    private void RefreshPrompt()
    {
        if (_txtPrompt == null || _currentData == null)
        {
            return;
        }

        _txtPrompt.text = _currentData.PromptText;
    }

    /// <summary>
    /// 刷新金币信息文本。
    /// 显示当前拥有金币和商品价格。
    /// </summary>
    private void RefreshGoldInfo()
    {
        if (_txtGoldInfo == null || _currentData == null)
        {
            return;
        }

        int currentGold = GameEntry.Fruits != null ? GameEntry.Fruits.CurrentGold : 0;
        _txtGoldInfo.text = Utility.Text.Format("拥有金币:{0}\n价格:{1}", currentGold, _currentData.AcquireParam);
    }

    /// <summary>
    /// 确认购买按钮回调。
    /// 扣除金币并通知调用方购买成功。
    /// </summary>
    private void OnBtnYesClicked()
    {
        UIInteractionSound.PlayClick();

        if (_currentData == null)
        {
            CloseSelf();
            return;
        }

        // 金币类型：走 TryConsumeGold 原子扣费
        if (string.Equals(_currentData.AcquireType, "gold", StringComparison.OrdinalIgnoreCase))
        {
            if (GameEntry.Fruits == null || !GameEntry.Fruits.TryConsumeGold(_currentData.AcquireParam))
            {
                Log.Warning("PurchaseUIForm 购买失败，金币不足或扣费失败，Item='{0}'。", _currentData.ItemName);
                CloseSelf();
                return;
            }
        }

        // 通知调用方购买成功
        _currentData.OnPurchaseSuccess?.Invoke();

        CloseSelf();
    }

    /// <summary>
    /// 取消按钮回调。
    /// </summary>
    private void OnBtnNoClicked()
    {
        UIInteractionSound.PlayClick();
        CloseSelf();
    }

    /// <summary>
    /// 关闭自身。
    /// </summary>
    private void CloseSelf()
    {
        if (UIForm == null || GameEntry.UI == null)
        {
            return;
        }

        GameEntry.UI.CloseUIForm(UIForm.SerialId);
    }
}

/// <summary>
/// PurchaseUIForm 打开数据。
/// 携带商品名称、获取类型、价格和购买成功回调。
/// </summary>
public sealed class PurchaseUIData
{
    /// <summary>
    /// 商品名称（如头像名）。
    /// </summary>
    public string ItemName;

    /// <summary>
    /// 获取类型（gold / other）。
    /// </summary>
    public string AcquireType;

    /// <summary>
    /// 获取参数（金币价格等）。
    /// </summary>
    public int AcquireParam;

    /// <summary>
    /// 提示文案。
    /// 如 "是否购买史莱姆头像"。
    /// </summary>
    public string PromptText;

    /// <summary>
    /// 购买成功后的回调。
    /// 扣费成功后由 PurchaseUIForm 调用。
    /// </summary>
    public Action OnPurchaseSuccess;
}
