using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

public partial class MainUIForm
{
    // GoShoDongFuHua 按钮本体。
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
    /// 孵化槽视图缓存。
    /// </summary>
    private HatchSlotView[] _hatchSlotViews;

    /// <summary>
    /// 孵化区视图是否已完成初始化。
    /// </summary>
    private bool _isHatchViewReady;

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
            Transform manualHatch = _pageCenter.Find("GoYouWan/GoShoDongFuHua");
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
        if (GameEntry.EggHatch == null)
        {
            Log.Warning("MainUIForm can not handle manual hatch because EggHatchComponent is missing.");
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
            Log.Error("MainUIForm hatch view initialize failed because key nodes are missing.");
            return false;
        }

        // 当前 UI 结构就是 6 个库存点，少一个都按结构错误处理。
        if (_manualEggCountRoot.childCount != 6)
        {
            Log.Error("MainUIForm hatch view initialize failed because GoDanShuLiang child count is '{0}', expected 6.", _manualEggCountRoot.childCount);
            return false;
        }

        // 当前 UI 结构就是 4 个孵化槽，少一个都按结构错误处理。
        if (_hatchSlotsRoot.childCount != 4)
        {
            Log.Error("MainUIForm hatch view initialize failed because GoFuHua child count is '{0}', expected 4.", _hatchSlotsRoot.childCount);
            return false;
        }

        // 缓存 6 个库存点节点，刷新时直接按下标控制显隐。
        _manualEggIndicators = new GameObject[_manualEggCountRoot.childCount];
        for (int i = 0; i < _manualEggCountRoot.childCount; i++)
        {
            _manualEggIndicators[i] = _manualEggCountRoot.GetChild(i).gameObject;
        }

        // 缓存每个孵化槽的倒计时文本，避免每帧查找组件。
        _hatchSlotViews = new HatchSlotView[_hatchSlotsRoot.childCount];
        for (int i = 0; i < _hatchSlotsRoot.childCount; i++)
        {
            Transform slotTransform = _hatchSlotsRoot.GetChild(i);
            TextMeshProUGUI txtDJ = slotTransform.GetComponentInChildren<TextMeshProUGUI>(true);
            if (txtDJ == null)
            {
                Log.Error("MainUIForm hatch view initialize failed because slot '{0}' is missing Text (TMP).", slotTransform.name);
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
        int manualEggCount = isAvailable ? eggHatch.ManualEggCount : 0;

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
                    // 前 N 个点亮表示当前还剩多少个万能蛋。
                    _manualEggIndicators[i].SetActive(i < manualEggCount);
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
}
