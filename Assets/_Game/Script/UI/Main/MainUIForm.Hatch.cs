using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

public partial class MainUIForm
{
    // GoShoDongFuHua 下的 BtnHatch。
    [SerializeField]
    private Button _btnManualHatch;

    // GoShoDongFuHua 下的 EggAdd。
    [SerializeField]
    private Button _btnEggAdd;

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

        if (_btnEggAdd == null)
        {
            Transform eggAddButton = _pageCenter.Find("GoYouWan/GoShoDongFuHua/EggAdd");
            if (eggAddButton != null)
            {
                _btnEggAdd = eggAddButton.GetComponent<Button>();
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
        _btnEggAdd.onClick.RemoveListener(OnEggAddClicked);
        _btnEggAdd.onClick.AddListener(OnEggAddClicked);
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

        if (_btnEggAdd != null)
        {
            _btnEggAdd.onClick.RemoveListener(OnEggAddClicked);
        }

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
    /// 购买蛋入口点击回调。
    /// </summary>
    private void OnEggAddClicked()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
        if (GameEntry.UI == null)
        {
            Log.Warning("MainUIForm 无法打开购买蛋界面，UIComponent 缺失。");
            return;
        }

        if (_purchaseEggsUIFormId > 0 && GameEntry.UI.HasUIForm(_purchaseEggsUIFormId))
        {
            return;
        }

        _purchaseEggsUIFormId = GameEntry.UI.OpenUIForm(UIFormDefine.PurchaseEggsUIForm, UIFormDefine.PopupGroup);
    }

    /// <summary>
    /// 构建孵化界面缓存。
    /// </summary>
    private bool BuildHatchViewCache()
    {
        if (_btnManualHatch == null || _btnEggAdd == null || _manualHatchRefillSlider == null || _manualEggCountRoot == null || _hatchSlotsRoot == null)
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

            _hatchSlotViews[i] = new HatchSlotView
            {
                TxtDJ = txtDJ
            };
        }

        return true;
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
            bool isOccupied = slotState != null && slotState.IsOccupied;

            if (slotView.TxtDJ != null)
            {
                slotView.TxtDJ.gameObject.SetActive(isOccupied);
                // 显示向上取整后的剩余秒数，避免出现 0.x 秒直接显示 0。
                slotView.TxtDJ.text = isOccupied ? Mathf.CeilToInt(Mathf.Max(0f, slotState.RemainingSeconds)).ToString() : string.Empty;
            }
        }
    }

    /// <summary>
    /// 关闭当前记录到的购买蛋窗体。
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
