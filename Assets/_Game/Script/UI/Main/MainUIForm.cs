using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// Main界面
/// </summary>
public class MainUIForm : UIFormLogic
{
    private const int LeftPageIndex = -1;
    private const int CenterPageIndex = 0;
    private const int RightPageIndex = 1;

    // 左边按钮
    [SerializeField]
    private Button _btnLeft;
    // 右边按钮
    [SerializeField]
    private Button _btnRight;
    // 分页可视区域，未手动绑定时默认取 GoYiDong 的父节点。
    [SerializeField]
    private RectTransform _pageViewport;
    // 可移动分页容器。
    [SerializeField]
    private RectTransform _goYiDong;
    // 左页根节点。
    [SerializeField]
    private RectTransform _pageLeft;
    // 中页根节点。
    [SerializeField]
    private RectTransform _pageCenter;
    // 右页根节点。
    [SerializeField]
    private RectTransform _pageRight;
    // 切页动画时长。
    [SerializeField]
    private float _switchDuration = 0.2f;

    private Tweener _switchTween;
    private int _currentPageIndex = CenterPageIndex;
    private float _cachedViewportWidth = -1f;
    private bool _isSwitching = false;

    protected override void OnInit(object userData)
    {
        CacheReferences();
        if (_btnLeft != null)
        {
            _btnLeft.onClick.AddListener(OnBtnLeft);
        }
        else
        {
            Log.Warning("MainUIForm can not find BtnLeft.");
        }

        if (_btnRight != null)
        {
            _btnRight.onClick.AddListener(OnBtnRight);
        }
        else
        {
            Log.Warning("MainUIForm can not find BtnRight.");
        }

        base.OnInit(userData);
        RefreshPageLayout(true);
        UpdateButtonState();
    }

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        RefreshPageLayout(true);
        UpdateButtonState();
    }

    protected override void OnClose(bool isShutdown, object userData)
    {
        StopSwitchTween();
        base.OnClose(isShutdown, userData);
    }

    private void OnDestroy()
    {
        StopSwitchTween();

        if (_btnLeft != null)
        {
            _btnLeft.onClick.RemoveListener(OnBtnLeft);
        }

        if (_btnRight != null)
        {
            _btnRight.onClick.RemoveListener(OnBtnRight);
        }
    }

    /// <summary>
    /// 右边按钮点击逻辑
    /// </summary>
    private void OnBtnRight()
    {
        SwitchToPage(_currentPageIndex + 1);
    }

    /// <summary>
    /// 左边按钮点击逻辑
    /// </summary>
    private void OnBtnLeft()
    {
        SwitchToPage(_currentPageIndex - 1);
    }

    /// <summary>
    /// RectTransform 尺寸变化时，重新按当前可视宽度排版页面。
    /// </summary>
    private void OnRectTransformDimensionsChange()
    {
        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        RefreshPageLayout(false);
    }

    /// <summary>
    /// 缓存界面上会用到的节点引用，未在 Inspector 绑定时按约定名称自动查找。
    /// </summary>
    private void CacheReferences()
    {
        if (_btnLeft == null)
        {
            Transform btnLeft = transform.Find("BtnLeft");
            if (btnLeft != null)
            {
                _btnLeft = btnLeft.GetComponent<Button>();
            }
        }

        if (_btnRight == null)
        {
            Transform btnRight = transform.Find("BtnRight");
            if (btnRight != null)
            {
                _btnRight = btnRight.GetComponent<Button>();
            }
        }

        if (_goYiDong == null)
        {
            _goYiDong = transform.Find("GoYiDong") as RectTransform;
        }

        if (_pageCenter == null && _goYiDong != null)
        {
            _pageCenter = _goYiDong.Find("BJ") as RectTransform;
        }

        if (_pageLeft == null && _goYiDong != null)
        {
            _pageLeft = _goYiDong.Find("BJLeft") as RectTransform;
        }

        if (_pageRight == null && _goYiDong != null)
        {
            _pageRight = _goYiDong.Find("BJRight") as RectTransform;
        }

        if (_pageViewport == null)
        {
            RectTransform parentRectTransform = _goYiDong != null ? _goYiDong.parent as RectTransform : null;
            if (parentRectTransform != null && parentRectTransform != _goYiDong)
            {
                _pageViewport = parentRectTransform;
            }
            else
            {
                _pageViewport = CachedTransform as RectTransform;
            }
        }
    }

    /// <summary>
    /// 根据当前可视宽度刷新三页的摆放位置，并让容器对齐到当前页。
    /// </summary>
    private void RefreshPageLayout(bool force)
    {
        CacheReferences();

        if (_goYiDong == null || _pageViewport == null)
        {
            return;
        }

        float viewportWidth = _pageViewport.rect.width;
        if (viewportWidth <= 0f)
        {
            return;
        }

        if (!force && Mathf.Approximately(_cachedViewportWidth, viewportWidth))
        {
            return;
        }

        // 备注：仅在首次打开或分辨率变化时归一化分页容器，避免切页过程中被重置回中间。
        _goYiDong.offsetMin = Vector2.zero;
        _goYiDong.offsetMax = Vector2.zero;

        _cachedViewportWidth = viewportWidth;
        SetPageOffset(_pageLeft, -viewportWidth);
        SetPageOffset(_pageCenter, 0f);
        SetPageOffset(_pageRight, viewportWidth);

        StopSwitchTween();
        SetContainerOffset(GetPageOffset(_currentPageIndex));
        UpdateButtonState();
    }

    /// <summary>
    /// 切换到指定页。
    /// </summary>
    /// <param name="targetPageIndex">目标页索引。</param>
    private void SwitchToPage(int targetPageIndex)
    {
        CacheReferences();
        if (_goYiDong == null || _pageViewport == null)
        {
            return;
        }

        RefreshPageLayout(false);
        if (_cachedViewportWidth <= 0f)
        {
            return;
        }

        int clampedPageIndex = Mathf.Clamp(targetPageIndex, LeftPageIndex, RightPageIndex);
        if (clampedPageIndex == _currentPageIndex && !_isSwitching)
        {
            return;
        }

        _currentPageIndex = clampedPageIndex;
        float targetOffsetX = GetPageOffset(_currentPageIndex);

        StopSwitchTween();
        if (_switchDuration <= 0f)
        {
            SetContainerOffset(targetOffsetX);
            UpdateButtonState();
            return;
        }

        _isSwitching = true;
        UpdateButtonState();

        _switchTween = _goYiDong.DOAnchorPosX(targetOffsetX, _switchDuration)
            .SetEase(Ease.OutCubic)
            .SetUpdate(true)
            .OnComplete(OnSwitchTweenComplete);
    }

    /// <summary>
    /// 切页补间完成回调。
    /// </summary>
    private void OnSwitchTweenComplete()
    {
        _isSwitching = false;
        _switchTween = null;
        UpdateButtonState();
    }

    /// <summary>
    /// 停止当前切页补间。
    /// </summary>
    private void StopSwitchTween()
    {
        if (_switchTween != null)
        {
            _switchTween.Kill();
            _switchTween = null;
        }

        _isSwitching = false;
    }

    /// <summary>
    /// 设置单页相对分页原点的横向偏移。
    /// </summary>
    /// <param name="page">页面节点。</param>
    /// <param name="offsetX">横向偏移。</param>
    private static void SetPageOffset(RectTransform page, float offsetX)
    {
        if (page == null)
        {
            return;
        }

        Vector2 anchoredPosition = page.anchoredPosition;
        page.anchoredPosition = new Vector2(offsetX, anchoredPosition.y);
    }

    /// <summary>
    /// 设置分页容器的横向偏移。
    /// </summary>
    /// <param name="offsetX">横向偏移。</param>
    private void SetContainerOffset(float offsetX)
    {
        if (_goYiDong == null)
        {
            return;
        }

        Vector2 anchoredPosition = _goYiDong.anchoredPosition;
        _goYiDong.anchoredPosition = new Vector2(offsetX, anchoredPosition.y);
    }

    /// <summary>
    /// 获取指定页在容器上的目标偏移。
    /// </summary>
    /// <param name="pageIndex">页索引。</param>
    /// <returns>目标横向偏移。</returns>
    private float GetPageOffset(int pageIndex)
    {
        return -pageIndex * _cachedViewportWidth;
    }

    /// <summary>
    /// 根据当前页更新按钮是否可交互。
    /// </summary>
    private void UpdateButtonState()
    {
        if (_btnLeft != null)
        {
            _btnLeft.interactable = !_isSwitching && _currentPageIndex > LeftPageIndex;
        }

        if (_btnRight != null)
        {
            _btnRight.interactable = !_isSwitching && _currentPageIndex < RightPageIndex;
        }
    }
}
