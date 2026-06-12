using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

public partial class MainUIForm
{
    // GoShoDongFuHua 下的 BtnHatch。
    [SerializeField]
    private Button _btnManualHatch;

    // GoShoDongFuHua 下的 30 秒补蛋进度条。
    [SerializeField]
    private Slider _manualHatchRefillSlider;

    // GoDanShuLiang 根节点，下面固定 6 个小点表示库存。
    [SerializeField]
    private RectTransform _manualEggCountRoot;

    // GoFuHua 根节点，下面固定 4 个孵化槽。
    [SerializeField]
    private RectTransform _hatchSlotsRoot;

    // 中页玩法根节点。
    [SerializeField]
    private RectTransform _goYouWanRoot;

    /// <summary>
    /// 手动蛋库存指示点缓存。
    /// </summary>
    private GameObject[] _manualEggIndicators;

    /// <summary>
    /// 手动蛋库存指示点图形缓存。
    /// </summary>
    private Graphic[] _manualEggIndicatorGraphics;

    /// <summary>
    /// 孵化槽视图缓存。
    /// </summary>
    private HatchSlotView[] _hatchSlotViews;

    /// <summary>
    /// 孵化区视图是否已完成初始化。
    /// </summary>
    private bool _isHatchViewReady;

    /// <summary>
    /// 当前已打开的购买蛋窗体序列号。
    /// 只用于防止玩家快速点击多个孵化槽时重复叠加 PurchaseEggsUIForm。
    /// </summary>
    private int _purchaseEggsUIFormId;

    /// <summary>
    /// 万能蛋库存点颜色。
    /// </summary>
    private static readonly Color32 UniversalEggIndicatorColor = new Color32(255, 255, 255, 255);

    /// <summary>
    /// 普通蛋库存点颜色。
    /// </summary>
    private static readonly Color32 NormalEggIndicatorColor = new Color32(76, 175, 80, 255);

    /// <summary>
    /// 稀有蛋库存点颜色。
    /// </summary>
    private static readonly Color32 RareEggIndicatorColor = new Color32(33, 150, 243, 255);

    /// <summary>
    /// 史诗蛋库存点颜色。
    /// </summary>
    private static readonly Color32 EpicEggIndicatorColor = new Color32(156, 39, 176, 255);

    /// <summary>
    /// 传说蛋库存点颜色。
    /// </summary>
    private static readonly Color32 LegendaryEggIndicatorColor = new Color32(244, 67, 54, 255);

    /// <summary>
    /// 神话蛋库存点颜色。
    /// </summary>
    private static readonly Color32 MythicEggIndicatorColor = new Color32(255, 193, 7, 255);

    /// <summary>
    /// 单个孵化槽的界面缓存。
    /// </summary>
    private sealed class HatchSlotView
    {
        // 槽位倒计时文本。
        public TextMeshProUGUI TxtDJ;

        // 当前槽位自己的加蛋按钮。
        public Button BtnEggAdd;

        // 当前槽位加蛋按钮的缓存回调，销毁界面时用它精确移除监听。
        public UnityAction EggAddClickAction;
    }

    /// <summary>
    /// 缓存孵化相关节点。
    /// </summary>
    private void CacheHatchReferences()
    {
        if (_pageCenter == null)
        {
            return;
        }

        if (_btnManualHatch == null)
        {
            // 手动孵化按钮固定挂在中间页 GoYouWan 下。
            Transform manualHatch = _pageCenter.Find("GoYouWan/GoShoDongFuHua/BtnHatch");
            if (manualHatch != null)
            {
                _btnManualHatch = manualHatch.GetComponent<Button>();
            }
        }

        if (_manualHatchRefillSlider == null)
        {
            Transform manualHatchSlider = _pageCenter.Find("GoYouWan/GoShoDongFuHua/Slider");
            if (manualHatchSlider != null)
            {
                _manualHatchRefillSlider = manualHatchSlider.GetComponent<Slider>();
            }
        }

        if (_manualEggCountRoot == null)
        {
            _manualEggCountRoot = _pageCenter.Find("GoYouWan/GoShoDongFuHua/GoDanShuLiang") as RectTransform;
        }

        if (_goYouWanRoot == null)
        {
            _goYouWanRoot = _pageCenter.Find("GoYouWan") as RectTransform;
        }

        if (_hatchSlotsRoot == null)
        {
            _hatchSlotsRoot = _pageCenter.Find("GoYouWan/GoFuHua") as RectTransform;
        }

        Transform legacyEggAddButton = _pageCenter.Find("GoYouWan/GoShoDongFuHua/EggAdd");
        if (legacyEggAddButton != null && legacyEggAddButton.gameObject.activeSelf)
        {
            // 旧全局加蛋入口已废弃：现在每个孵化槽自己负责显示 BtnEggAdd。
            legacyEggAddButton.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// GoYouWan 作为中页内容根节点，始终保持激活。
    /// 页面显示完全交给 GoYiDong 的位移控制，不再单独切显隐。
    /// </summary>
    private void UpdateGoYouWanVisibility()
    {
        if (_goYouWanRoot == null)
        {
            CacheHatchReferences();
        }

        if (_goYouWanRoot == null)
        {
            return;
        }

        if (_goYouWanRoot.gameObject.activeSelf)
        {
            return;
        }

        _goYouWanRoot.gameObject.SetActive(true);
    }

    /// <summary>
    /// 初始化孵化界面。
    /// </summary>
    private void InitializeHatchView()
    {
        CacheHatchReferences();
        _isHatchViewReady = BuildHatchViewCache();
        if (!_isHatchViewReady)
        {
            return;
        }

        _btnManualHatch.onClick.RemoveListener(OnManualHatchClicked);
        _btnManualHatch.onClick.AddListener(OnManualHatchClicked);
        RegisterHatchSlotEggAddListeners();
        RefreshHatchView();
    }

    /// <summary>
    /// 打开孵化界面时刷新一次状态。
    /// </summary>
    private void OpenHatchView()
    {
        RefreshHatchView();
    }

    /// <summary>
    /// 关闭孵化界面。
    /// </summary>
    private void CloseHatchView()
    {
        ClosePurchaseEggsUIForm();
        RefreshHatchView();
    }

    /// <summary>
    /// 销毁孵化界面时清理按钮监听。
    /// </summary>
    private void DestroyHatchView()
    {
        if (_btnManualHatch != null)
        {
            _btnManualHatch.onClick.RemoveListener(OnManualHatchClicked);
        }

        UnregisterHatchSlotEggAddListeners();
        ClosePurchaseEggsUIForm();
    }

    /// <summary>
    /// 每帧刷新孵化界面。
    /// </summary>
    private void UpdateHatchView()
    {
        RefreshHatchView();
    }

    /// <summary>
    /// 手动孵化按钮点击回调。
    /// </summary>
    private void OnManualHatchClicked()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
        if (GameEntry.EggHatch == null)
        {
            Log.Warning("MainUIForm 无法执行手动孵化，EggHatchComponent 缺失。");
            return;
        }

        GameEntry.EggHatch.TryManualAction();
        RefreshHatchView();
    }

    /// <summary>
    /// 构建孵化界面缓存。
    /// </summary>
    private bool BuildHatchViewCache()
    {
        if (_btnManualHatch == null || _manualHatchRefillSlider == null || _manualEggCountRoot == null || _hatchSlotsRoot == null)
        {
            Log.Error("MainUIForm 孵化视图初始化失败，关键节点缺失。");
            return false;
        }

        // 当前 UI 结构就是 6 个库存点，少一个都按结构错误处理。
        if (_manualEggCountRoot.childCount != 6)
        {
            Log.Error("MainUIForm 孵化视图初始化失败，GoDanShuLiang 子节点数为 '{0}'，期望 6。", _manualEggCountRoot.childCount);
            return false;
        }

        // 当前 UI 结构就是 4 个孵化槽，少一个都按结构错误处理。
        if (_hatchSlotsRoot.childCount != 4)
        {
            Log.Error("MainUIForm 孵化视图初始化失败，GoFuHua 子节点数为 '{0}'，期望 4。", _hatchSlotsRoot.childCount);
            return false;
        }

        // 缓存 6 个库存点节点，刷新时直接按下标控制显隐。
        _manualEggIndicators = new GameObject[_manualEggCountRoot.childCount];
        _manualEggIndicatorGraphics = new Graphic[_manualEggCountRoot.childCount];
        for (int i = 0; i < _manualEggCountRoot.childCount; i++)
        {
            Transform indicatorTransform = _manualEggCountRoot.GetChild(i);
            _manualEggIndicators[i] = indicatorTransform.gameObject;
            _manualEggIndicatorGraphics[i] = indicatorTransform.GetComponent<Graphic>();
            if (_manualEggIndicatorGraphics[i] == null)
            {
                _manualEggIndicatorGraphics[i] = indicatorTransform.GetComponentInChildren<Graphic>(true);
            }

            if (_manualEggIndicatorGraphics[i] == null)
            {
                Log.Error("MainUIForm 孵化视图初始化失败，库存指示器 '{0}' 缺少 Graphic 组件。", indicatorTransform.name);
                return false;
            }
        }

        // 缓存每个孵化槽的倒计时文本，避免每帧查找组件。
        _hatchSlotViews = new HatchSlotView[_hatchSlotsRoot.childCount];
        for (int i = 0; i < _hatchSlotsRoot.childCount; i++)
        {
            Transform slotTransform = _hatchSlotsRoot.GetChild(i);
            TextMeshProUGUI txtDJ = slotTransform.GetComponentInChildren<TextMeshProUGUI>(true);
            if (txtDJ == null)
            {
                Log.Error("MainUIForm 孵化视图初始化失败，槽位 '{0}' 缺少 Text (TMP) 组件。", slotTransform.name);
                return false;
            }

            Button btnEggAdd = FindHatchSlotEggAddButton(slotTransform);
            if (btnEggAdd == null)
            {
                Log.Warning("MainUIForm 孵化槽 '{0}' 缺少 BtnEggAdd 按钮，该槽位不会显示手动放蛋入口。", slotTransform.name);
            }

            _hatchSlotViews[i] = new HatchSlotView
            {
                TxtDJ = txtDJ,
                BtnEggAdd = btnEggAdd,
                EggAddClickAction = CreateHatchSlotEggAddClickAction(i)
            };
        }

        return true;
    }

    /// <summary>
    /// 查找单个孵化槽下的加蛋按钮。
    /// 兼容 BtnEggAdd 与 EggAdd 两种命名，避免预制体命名调整后脚本初始化失败。
    /// </summary>
    /// <param name="slotTransform">孵化槽根节点。</param>
    /// <returns>找到的按钮组件；未找到返回 null。</returns>
    private static Button FindHatchSlotEggAddButton(Transform slotTransform)
    {
        if (slotTransform == null)
        {
            return null;
        }

        Transform buttonTransform = slotTransform.Find("BtnEggAdd") ?? slotTransform.Find("EggAdd");
        if (buttonTransform != null)
        {
            return buttonTransform.GetComponent<Button>();
        }

        Button[] buttons = slotTransform.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null)
            {
                continue;
            }

            string buttonName = button.gameObject.name;
            if (buttonName == "BtnEggAdd" || buttonName == "EggAdd")
            {
                return button;
            }
        }

        return null;
    }

    /// <summary>
    /// 创建单个孵化槽加蛋按钮的点击回调。
    /// 该闭包只在界面初始化时创建一次，不在 Update 高频路径产生 GC。
    /// </summary>
    /// <param name="slotIndex">0 基孵化槽索引。</param>
    /// <returns>可注册到 Button.onClick 的回调。</returns>
    private UnityAction CreateHatchSlotEggAddClickAction(int slotIndex)
    {
        return () => OnHatchSlotEggAddClicked(slotIndex);
    }

    /// <summary>
    /// 注册每个孵化槽自己的加蛋按钮监听。
    /// </summary>
    private void RegisterHatchSlotEggAddListeners()
    {
        if (_hatchSlotViews == null)
        {
            return;
        }

        for (int i = 0; i < _hatchSlotViews.Length; i++)
        {
            HatchSlotView slotView = _hatchSlotViews[i];
            if (slotView == null || slotView.BtnEggAdd == null || slotView.EggAddClickAction == null)
            {
                continue;
            }

            slotView.BtnEggAdd.onClick.RemoveListener(slotView.EggAddClickAction);
            slotView.BtnEggAdd.onClick.AddListener(slotView.EggAddClickAction);
        }
    }

    /// <summary>
    /// 反注册每个孵化槽自己的加蛋按钮监听。
    /// </summary>
    private void UnregisterHatchSlotEggAddListeners()
    {
        if (_hatchSlotViews == null)
        {
            return;
        }

        for (int i = 0; i < _hatchSlotViews.Length; i++)
        {
            HatchSlotView slotView = _hatchSlotViews[i];
            if (slotView == null || slotView.BtnEggAdd == null || slotView.EggAddClickAction == null)
            {
                continue;
            }

            slotView.BtnEggAdd.onClick.RemoveListener(slotView.EggAddClickAction);
        }
    }

    /// <summary>
    /// 单个孵化槽加蛋按钮点击回调。
    /// 只负责打开购买蛋弹窗，并把当前槽位索引传给购买界面；
    /// 是否能购买、金币是否足够、槽位是否已有蛋，统一在购买按钮点击时判断并提示。
    /// </summary>
    /// <param name="slotIndex">0 基孵化槽索引。</param>
    private void OnHatchSlotEggAddClicked(int slotIndex)
    {
        UIInteractionSound.PlayClick();

        EggHatchComponent eggHatch = GameEntry.EggHatch;
        if (eggHatch == null || !eggHatch.IsAvailable)
        {
            Log.Warning("MainUIForm 无法在指定孵化槽放蛋，EggHatchComponent 缺失。");
            ToastUtility.Show("孵化功能未初始化");
            return;
        }

        if (eggHatch.GetSlotState(slotIndex) == null)
        {
            ToastUtility.Show("孵化位未解锁");
            RefreshHatchView();
            return;
        }

        if (GameEntry.UI == null)
        {
            Log.Warning("MainUIForm 无法打开购买蛋界面，UIComponent 缺失。");
            return;
        }

        if (_purchaseEggsUIFormId > 0 && GameEntry.UI.HasUIForm(_purchaseEggsUIFormId))
        {
            return;
        }

        _purchaseEggsUIFormId = GameEntry.UI.OpenUIForm(
            UIFormDefine.PurchaseEggsUIForm,
            UIFormDefine.PopupGroup,
            new PurchaseEggsUIForm.OpenData(slotIndex));
        RefreshHatchView();
    }

    /// <summary>
    /// 刷新孵化区界面显示。
    /// </summary>
    private void RefreshHatchView()
    {
        if (!_isHatchViewReady)
        {
            return;
        }

        // UI 只拉运行时模块状态，不自己持有任何孵化业务数据。
        EggHatchComponent eggHatch = GameEntry.EggHatch;
        bool isAvailable = eggHatch != null && eggHatch.IsAvailable;

        if (_btnManualHatch != null)
        {
            _btnManualHatch.interactable = isAvailable && eggHatch.CanManualAction;
        }

        if (_manualHatchRefillSlider != null)
        {
            _manualHatchRefillSlider.value = isAvailable ? eggHatch.RefillProgressNormalized : 0f;
        }

        if (_manualEggIndicators != null)
        {
            for (int i = 0; i < _manualEggIndicators.Length; i++)
            {
                if (_manualEggIndicators[i] != null)
                {
                    QualityType quality = QualityType.Universal;
                    bool hasEgg = isAvailable && eggHatch.TryGetManualEggAt(i, out _, out quality);
                    _manualEggIndicators[i].SetActive(hasEgg);
                    if (hasEgg && _manualEggIndicatorGraphics != null && i < _manualEggIndicatorGraphics.Length && _manualEggIndicatorGraphics[i] != null)
                    {
                        _manualEggIndicatorGraphics[i].color = GetManualEggIndicatorColor(quality);
                    }
                }
            }
        }

        if (_hatchSlotViews == null)
        {
            return;
        }

        for (int i = 0; i < _hatchSlotViews.Length; i++)
        {
            HatchSlotView slotView = _hatchSlotViews[i];
            EggHatchSlotState slotState = isAvailable ? eggHatch.GetSlotState(i) : null;
            bool isUnlocked = slotState != null;
            bool isOccupied = slotState != null && slotState.IsOccupied;

            if (slotView.TxtDJ != null)
            {
                slotView.TxtDJ.gameObject.SetActive(isOccupied);
                // 显示向上取整后的剩余秒数，避免出现 0.x 秒直接显示 0。
                slotView.TxtDJ.text = isOccupied ? Mathf.CeilToInt(Mathf.Max(0f, slotState.RemainingSeconds)).ToString() : string.Empty;
            }

            if (slotView.BtnEggAdd != null)
            {
                GameObject buttonObject = slotView.BtnEggAdd.gameObject;
                if (buttonObject.activeSelf != isUnlocked)
                {
                    buttonObject.SetActive(isUnlocked);
                }

                slotView.BtnEggAdd.interactable = isUnlocked;
            }
        }
    }

    /// <summary>
    /// 关闭当前由孵化槽按钮打开的购买蛋窗体。
    /// </summary>
    private void ClosePurchaseEggsUIForm()
    {
        if (_purchaseEggsUIFormId <= 0)
        {
            return;
        }

        if (GameEntry.UI != null && GameEntry.UI.HasUIForm(_purchaseEggsUIFormId))
        {
            GameEntry.UI.CloseUIForm(_purchaseEggsUIFormId);
        }

        _purchaseEggsUIFormId = 0;
    }

    /// <summary>
    /// 获取库存点颜色。
    /// </summary>
    private static Color32 GetManualEggIndicatorColor(QualityType quality)
    {
        switch (quality)
        {
            case QualityType.Normal:
                return NormalEggIndicatorColor;

            case QualityType.Rare:
                return RareEggIndicatorColor;

            case QualityType.Epic:
                return EpicEggIndicatorColor;

            case QualityType.Legendary:
                return LegendaryEggIndicatorColor;

            case QualityType.Mythic:
                return MythicEggIndicatorColor;

            default:
                return UniversalEggIndicatorColor;
        }
    }
}
