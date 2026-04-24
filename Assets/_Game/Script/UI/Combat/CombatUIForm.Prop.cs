using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 战斗界面 — 道具部分。
/// 负责道具按钮点击分发、道具包/购买次数状态管理、道具数量显示刷新。
/// </summary>
public sealed partial class CombatUIForm
{
    // ───────────── 道具按钮 ─────────────

    /// <summary>
    /// 移出道具按钮（BtnProp）。
    /// 点击后消耗移出道具，将等待区前3张卡片移出并回收。
    /// </summary>
    [SerializeField]
    private Button _btnShiftOut;

    /// <summary>
    /// 移出道具剩余数文本。
    /// 显示当前可用的移出道具次数。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtShiftOutCount;

    /// <summary>
    /// 拿取道具按钮（BtnProp (1)）。
    /// 点击后消耗拿取道具，进入拿取状态。
    /// </summary>
    [SerializeField]
    private Button _btnTake;

    /// <summary>
    /// 拿取道具剩余数文本。
    /// 显示当前可用的拿取道具次数。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtTakeCount;

    /// <summary>
    /// 随机道具按钮（BtnProp (2)）。
    /// 点击后消耗随机道具，打乱棋盘上所有未遮挡卡片的类型。
    /// </summary>
    [SerializeField]
    private Button _btnRandom;

    /// <summary>
    /// 随机道具剩余数文本。
    /// 显示当前可用的随机道具次数。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtRandomCount;

    // ───────────── 道具状态 ─────────────

    /// <summary>
    /// 本次战斗是否携带道具包。
    /// 道具包提供每种道具各1次免费使用；携带时禁止购买。
    /// </summary>
    private bool _hasPropKit;

    /// <summary>
    /// 当前战斗是否携带道具包。
    /// 供 VictoryFailUIForm 在"再来一局"时把当前战斗配置透传给下一局，
    /// 避免重开后丢失本局的道具包状态。
    /// </summary>
    internal bool HasPropKit => _hasPropKit;

    /// <summary>
    /// 移出道具是否已使用（道具包内）。
    /// </summary>
    private bool _removeToolUsed;

    /// <summary>
    /// 拿取道具是否已使用（道具包内）。
    /// </summary>
    private bool _pickToolUsed;

    /// <summary>
    /// 随机道具是否已使用（道具包内）。
    /// </summary>
    private bool _randomToolUsed;

    /// <summary>
    /// 购买的移出道具次数。
    /// </summary>
    private int _purchasedRemoveCount;

    /// <summary>
    /// 购买的拿取道具次数。
    /// </summary>
    private int _purchasedRetrieveCount;

    /// <summary>
    /// 购买的随机道具次数。
    /// </summary>
    private int _purchasedShuffleCount;

    /// <summary>
    /// 本局是否已经复活过。
    /// 每局只允许成功复活一次。
    /// </summary>
    private bool _hasRevivedThisBattle;

    /// <summary>
    /// 当前已打开的 PropPurchaseUIForm 序列号。
    /// 为 0 表示当前没有活动的购买窗实例。
    /// </summary>
    private int _propPurchaseUIFormId;

    /// <summary>
    /// 移出道具按钮点击回调。
    /// 优先级：道具包免费次数 → 购买次数 → 弹出购买UI。
    /// </summary>
    private void OnBtnShiftOut()
    {
        UIInteractionSound.PlayClick();

        var controller = EliminateCardController.Instance;
        if (controller == null)
        {
            return;
        }

        // 1. 道具包免费次数
        if (_hasPropKit && !_removeToolUsed)
        {
            if (controller.PropShiftOut())
            {
                _removeToolUsed = true;
                RefreshToolCounts();
            }
            return;
        }

        // 2. 购买次数
        if (_purchasedRemoveCount > 0)
        {
            if (controller.PropShiftOut())
            {
                _purchasedRemoveCount--;
                RefreshToolCounts();
            }
            return;
        }

        // 3. 弹出购买UI
        OpenPropPurchaseUIForm(PropPurchaseUIForm.PropType.Remove);
    }

    /// <summary>
    /// 拿取道具按钮点击回调。
    /// 优先级：道具包免费次数 → 购买次数 → 弹出购买UI。
    /// 若已在拿取状态，点击则退出拿取状态。
    /// </summary>
    private void OnBtnTake()
    {
        UIInteractionSound.PlayClick();

        var controller = EliminateCardController.Instance;
        if (controller == null)
        {
            return;
        }

        // 若已在拿取状态，点击则退出
        if (controller.IsTakeState)
        {
            controller.ExitTakeState();
            return;
        }

        // 1. 道具包免费次数
        if (_hasPropKit && !_pickToolUsed)
        {
            if (controller.PropEnterTakeState())
            {
                _pickToolUsed = true;
                RefreshToolCounts();
            }
            return;
        }

        // 2. 购买次数
        if (_purchasedRetrieveCount > 0)
        {
            if (controller.PropEnterTakeState())
            {
                _purchasedRetrieveCount--;
                RefreshToolCounts();
            }
            return;
        }

        // 3. 弹出购买UI
        OpenPropPurchaseUIForm(PropPurchaseUIForm.PropType.Retrieve);
    }

    /// <summary>
    /// 随机道具按钮点击回调。
    /// 优先级：道具包免费次数 → 购买次数 → 弹出购买UI。
    /// </summary>
    private void OnBtnRandom()
    {
        UIInteractionSound.PlayClick();

        var controller = EliminateCardController.Instance;
        if (controller == null)
        {
            return;
        }

        // 1. 道具包免费次数
        if (_hasPropKit && !_randomToolUsed)
        {
            if (controller.PropShuffle())
            {
                _randomToolUsed = true;
                RefreshToolCounts();
            }
            return;
        }

        // 2. 购买次数
        if (_purchasedShuffleCount > 0)
        {
            if (controller.PropShuffle())
            {
                _purchasedShuffleCount--;
                RefreshToolCounts();
            }
            return;
        }

        // 3. 弹出购买UI
        OpenPropPurchaseUIForm(PropPurchaseUIForm.PropType.Shuffle);
    }

    // ───────────── 道具状态管理 ─────────────

    /// <summary>
    /// 重置道具状态（每局战斗开始时调用）。
    /// </summary>
    private void ResetPropState()
    {
        _hasPropKit = false;
        _removeToolUsed = false;
        _pickToolUsed = false;
        _randomToolUsed = false;
        _purchasedRemoveCount = 0;
        _purchasedRetrieveCount = 0;
        _purchasedShuffleCount = 0;
        _hasRevivedThisBattle = false;

        PropPurchaseUIForm.ResetBattlePurchaseState();
        RefreshToolCounts();
    }

    /// <summary>
    /// 刷新道具数量显示。
    /// 数量 = 道具包未使用次数 + 购买次数。
    /// </summary>
    private void RefreshToolCounts()
    {
        // 移出道具数量
        int removeCount = 0;
        if (_hasPropKit && !_removeToolUsed) removeCount++;
        if (_purchasedRemoveCount > 0) removeCount += _purchasedRemoveCount;
        if (_txtShiftOutCount != null) _txtShiftOutCount.text = removeCount.ToString();

        // 拿取道具数量
        int pickCount = 0;
        if (_hasPropKit && !_pickToolUsed) pickCount++;
        if (_purchasedRetrieveCount > 0) pickCount += _purchasedRetrieveCount;
        if (_txtTakeCount != null) _txtTakeCount.text = pickCount.ToString();

        // 随机道具数量
        int randomCount = 0;
        if (_hasPropKit && !_randomToolUsed) randomCount++;
        if (_purchasedShuffleCount > 0) randomCount += _purchasedShuffleCount;
        if (_txtRandomCount != null) _txtRandomCount.text = randomCount.ToString();
    }

    /// <summary>
    /// 购买成功回调：增加对应道具的购买次数。
    /// </summary>
    /// <param name="propType">购买的道具类型。</param>
    private void OnPropPurchased(PropPurchaseUIForm.PropType propType)
    {
        switch (propType)
        {
            case PropPurchaseUIForm.PropType.Remove:
                _purchasedRemoveCount++;
                break;
            case PropPurchaseUIForm.PropType.Retrieve:
                _purchasedRetrieveCount++;
                break;
            case PropPurchaseUIForm.PropType.Shuffle:
                _purchasedShuffleCount++;
                break;
        }

        RefreshToolCounts();
    }

    /// <summary>
    /// 拿取状态变化回调：更新拿取按钮视觉反馈。
    /// </summary>
    /// <param name="isTakeState">当前是否处于拿取状态。</param>
    private void OnTakeStateChanged(bool isTakeState)
    {
        // 拿取状态下高亮拿取按钮（缩小提示玩家可再次点击退出）
        if (_btnTake != null)
        {
            _btnTake.transform.localScale = isTakeState
                ? new Vector3(0.9f, 0.9f, 0.9f)
                : Vector3.one;
        }
    }

    // ───────────── 道具购买UI ─────────────

    /// <summary>
    /// 打开道具购买UI。
    /// 先检查是否可购买，不可购买则跳过。
    /// </summary>
    /// <param name="propType">要购买的道具类型。</param>
    private void OpenPropPurchaseUIForm(PropPurchaseUIForm.PropType propType)
    {
        if (!PropPurchaseUIForm.CanPurchaseProp(propType, _hasPropKit))
        {
            return;
        }

        if (_propPurchaseUIFormId > 0 && GameEntry.UI.HasUIForm(_propPurchaseUIFormId))
        {
            return;
        }

        _propPurchaseUIFormId = GameEntry.UI.OpenUIForm(
            UIFormDefine.PropPurchaseUIForm, UIFormDefine.PopupGroup, propType);
    }

    /// <summary>
    /// 关闭当前记录到的 PropPurchaseUIForm。
    /// </summary>
    private void ClosePropPurchaseUIForm()
    {
        CloseTrackedUIForm(ref _propPurchaseUIFormId);
    }
}
