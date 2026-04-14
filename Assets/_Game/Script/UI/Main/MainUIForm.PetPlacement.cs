using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

public partial class MainUIForm
{
    /// <summary>
    /// 场地实体统一投影到的世界 Z 值。
    /// </summary>
    private const float EntityWorldZ = 0f;

    /// <summary>
    /// 宠物食物气泡根节点名称。
    /// 若主界面预制体未提前放置该节点，则运行时按这个名称动态创建。
    /// </summary>
    private const string PetFoodBubbleRootName = "PetFoodBubbleRoot";

    /// <summary>
    /// 排队位标记点根节点。
    /// </summary>
    [SerializeField]
    private RectTransform _queueSlotsRoot;

    /// <summary>
    /// 餐桌位标记点根节点。
    /// </summary>
    [SerializeField]
    private RectTransform _diningSeatsRoot;

    /// <summary>
    /// 果园位标记点根节点。
    /// </summary>
    [SerializeField]
    private RectTransform _orchardSlotsRoot;

    /// <summary>
    /// 玩耍区列表。
    /// 由外部手动拖入多个 PlayArea 节点，不再按名称自动搜索。
    /// </summary>
    [SerializeField]
    private List<RectTransform> _playAreas = new List<RectTransform>();

    /// <summary>
    /// 排队位标记点缓存。
    /// </summary>
    private RectTransform[] _queueMarkerTransforms;

    /// <summary>
    /// 桌位标记点缓存。
    /// </summary>
    private RectTransform[] _tableMarkerTransforms;

    /// <summary>
    /// 果园位标记点缓存。
    /// </summary>
    private RectTransform[] _orchardMarkerTransforms;

    /// <summary>
    /// 孵化槽标记点缓存。
    /// </summary>
    private RectTransform[] _hatchMarkerTransforms;

    /// <summary>
    /// 玩耍区标记点缓存。
    /// 它是可变长度数组，允许为空。
    /// </summary>
    private RectTransform[] _playAreaMarkerTransforms = System.Array.Empty<RectTransform>();

    /// <summary>
    /// 场地标记点视图是否已完成初始化。
    /// </summary>
    private bool _isPetPlacementViewReady;

    /// <summary>
    /// 当前布局是否需要重新采样。
    /// </summary>
    private bool _isPetPlacementLayoutDirty = true;

    /// <summary>
    /// 根 Canvas 缓存，用于获取 UI 相机。
    /// </summary>
    private Canvas _rootCanvas;

    /// <summary>
    /// UI 翻页相机缓存。
    /// </summary>
    private Camera _uiPageCamera;

    /// <summary>
    /// 场地主相机缓存。
    /// </summary>
    private Camera _mainPageCamera;

    /// <summary>
    /// 主相机在中页对齐时的基准位置。
    /// </summary>
    private Vector3 _centerMainCameraPosition;

    /// <summary>
    /// 是否已经缓存过主相机的中页基准位置。
    /// </summary>
    private bool _hasCachedMainCameraBase;

    /// <summary>
    /// 当前主相机相对中页的横向偏移。
    /// </summary>
    private float _currentMainCameraOffsetX;

    /// <summary>
    /// 是否已经输出过缺失 UI 相机日志。
    /// </summary>
    private bool _hasLoggedMissingUICamera;

    /// <summary>
    /// 是否已经输出过缺失主相机日志。
    /// </summary>
    private bool _hasLoggedMissingMainCamera;

    /// <summary>
    /// 宠物食物气泡容器。
    /// 屏幕空间气泡统一挂在这里，方便统一做投影与显隐。
    /// </summary>
    private RectTransform _petFoodBubbleRoot;

    /// <summary>
    /// 当前激活中的宠物食物气泡集合。
    /// Key 是宠物实例 Id，Value 是对应的气泡项脚本。
    /// </summary>
    private readonly Dictionary<int, PetFoodBubbleItem> _petFoodBubbleItemsByPetInstanceId = new Dictionary<int, PetFoodBubbleItem>();

    /// <summary>
    /// 复用的宠物食物气泡池。
    /// 站位变化时优先从池里取，避免频繁实例化。
    /// </summary>
    private readonly Stack<PetFoodBubbleItem> _petFoodBubblePool = new Stack<PetFoodBubbleItem>();

    /// <summary>
    /// 重建气泡 roster 时，用于回收多余项的缓冲。
    /// </summary>
    private readonly List<int> _petFoodBubbleRemoveBuffer = new List<int>();

    /// <summary>
    /// 当前气泡 roster 是否需要重建。
    /// </summary>
    private bool _isPetFoodBubbleRosterDirty = true;

    /// <summary>
    /// 是否已经订阅宠物站位变化事件。
    /// </summary>
    private bool _isListeningPetPlacementChanged;

    /// <summary>
    /// 是否已经订阅场地区容量变化事件。
    /// 当建筑升级扩容时，需要立即重采样当前开放的 marker。
    /// </summary>
    private bool _isListeningPlayfieldCapacityChanged;

    /// <summary>
    /// 食物气泡根节点是否由运行时动态创建。
    /// 动态创建的节点会在界面销毁时一并清理。
    /// </summary>
    private bool _isRuntimeCreatedPetFoodBubbleRoot;

    /// <summary>
    /// 是否已经输出过缺失气泡预制体日志。
    /// </summary>
    private bool _hasLoggedMissingPetFoodBubblePrefab;

    /// <summary>
    /// 缓存宠物站位相关节点。
    /// </summary>
    private void CachePetPlacementReferences()
    {
        if (_pageCenter == null || _pageRight == null)
        {
            return;
        }

        if (_rootCanvas == null)
        {
            _rootCanvas = GetComponentInParent<Canvas>();
            if (_rootCanvas != null)
            {
                _rootCanvas = _rootCanvas.rootCanvas;
            }
        }

        if (!IsMarkerRootValid(_queueSlotsRoot, _pageCenter))
        {
            _queueSlotsRoot = _pageCenter.Find("GoYouWan/GoPaiDui") as RectTransform;
        }

        if (!IsMarkerRootValid(_diningSeatsRoot, _pageCenter))
        {
            _diningSeatsRoot = _pageCenter.Find("GoYouWan/GoChiFanRoot") as RectTransform;
        }

        if (!IsMarkerRootValid(_orchardSlotsRoot, _pageRight))
        {
            _orchardSlotsRoot = _pageRight.Find("GoGuoYuan") as RectTransform;
        }

        if (_petFoodBubbleRoot == null && _pageCenter != null)
        {
            _petFoodBubbleRoot = _pageCenter.Find(PetFoodBubbleRootName) as RectTransform;
        }
    }

    /// <summary>
    /// 判断当前缓存的标记根节点是否仍然属于期望的页面层级。
    /// 这里用于兜底 prefab 上的错误拖拽引用，避免把别的空容器误当成 marker root。
    /// </summary>
    /// <param name="markerRoot">当前缓存的 marker root。</param>
    /// <param name="expectedPageRoot">该 marker root 理应所属的页面根节点。</param>
    /// <returns>引用是否有效。</returns>
    private static bool IsMarkerRootValid(RectTransform markerRoot, RectTransform expectedPageRoot)
    {
        return markerRoot != null
            && expectedPageRoot != null
            && markerRoot != expectedPageRoot
            && markerRoot.IsChildOf(expectedPageRoot);
    }

    /// <summary>
    /// 初始化场地标记点同步。
    /// </summary>
    private void InitializePetPlacementView()
    {
        CacheHatchReferences();
        CachePetPlacementReferences();
        EnsurePetPlacementEventSubscription();
        EnsurePetFoodBubbleRoot();

        _isPetPlacementViewReady = BuildPetPlacementViewCache();
        if (!_isPetPlacementViewReady)
        {
            return;
        }

        MarkPetPlacementLayoutDirty();
        MarkPetFoodBubbleRosterDirty();
        SyncPetPlacementMarkersToEntities();
        RebuildPetFoodBubbleRosterIfNeeded();
        UpdatePetFoodBubblePositions();
    }

    /// <summary>
    /// 打开界面时刷新场地标记点与宠物食物气泡。
    /// </summary>
    private void OpenPetPlacementView()
    {
        MarkPetPlacementLayoutDirty();
        MarkPetFoodBubbleRosterDirty();
        SyncPetPlacementMarkersToEntities();
        RebuildPetFoodBubbleRosterIfNeeded();
        UpdatePetFoodBubblePositions();
    }

    /// <summary>
    /// 关闭界面时回收当前激活的气泡项。
    /// 主界面再次打开时会按最新宠物站位重建一次。
    /// </summary>
    private void ClosePetPlacementView()
    {
        ReleaseAllActivePetFoodBubbleItems();
    }

    /// <summary>
    /// 销毁视图缓存。
    /// </summary>
    private void DestroyPetPlacementView()
    {
        ReleasePetPlacementEventSubscription();
        DestroyAllPetFoodBubbleItems();

        _queueMarkerTransforms = null;
        _tableMarkerTransforms = null;
        _orchardMarkerTransforms = null;
        _hatchMarkerTransforms = null;
        _playAreaMarkerTransforms = System.Array.Empty<RectTransform>();
        _petFoodBubbleRoot = null;
        _isPetPlacementViewReady = false;
        _isPetPlacementLayoutDirty = true;
        _isPetFoodBubbleRosterDirty = true;
    }

    /// <summary>
    /// 切页期间不重采样实体，但宠物食物气泡的位置必须持续跟随。
    /// 否则翻页时气泡会停留在旧屏幕坐标上。
    /// </summary>
    private void UpdatePetPlacementView()
    {
        RebuildPetFoodBubbleRosterIfNeeded();
        UpdatePetFoodBubblePositions();

        if (_isSwitching)
        {
            return;
        }

        if (_isPetPlacementLayoutDirty)
        {
            SyncPetPlacementMarkersToEntities();
        }
    }

    /// <summary>
    /// 构建场地标记点缓存。
    /// </summary>
    private bool BuildPetPlacementViewCache()
    {
        if (_queueSlotsRoot == null || _diningSeatsRoot == null || _orchardSlotsRoot == null || _hatchSlotsRoot == null)
        {
            Log.Error("MainUIForm playfield marker initialize failed because key roots are missing.");
            return false;
        }

        if (_queueSlotsRoot.childCount != PlayfieldEntityModule.QueueSlotCountValue)
        {
            Log.Error(
                "MainUIForm playfield marker initialize failed because GoPaiDui child count is '{0}', expected '{1}'.",
                _queueSlotsRoot.childCount,
                PlayfieldEntityModule.QueueSlotCountValue);
            return false;
        }

        int tableCount = GameEntry.PlayfieldEntities?.TableCount ?? PetPlacementModule.DefaultDiningSeatCount;
        if (_diningSeatsRoot.childCount < tableCount)
        {
            Log.Error(
                "MainUIForm playfield marker initialize failed because GoChiFanRoot child count is '{0}', expected at least '{1}'.",
                _diningSeatsRoot.childCount,
                tableCount);
            return false;
        }

        int orchardCount = GameEntry.PlayfieldEntities?.OrchardCount ?? OrchardModule.DefaultOrchardSlotCount;
        if (_orchardSlotsRoot.childCount < orchardCount)
        {
            Log.Error(
                "MainUIForm playfield marker initialize failed because GoGuoYuan child count is '{0}', expected at least '{1}'.",
                _orchardSlotsRoot.childCount,
                orchardCount);
            return false;
        }

        if (_hatchSlotsRoot.childCount != PlayfieldEntityModule.HatchSlotCountValue)
        {
            Log.Error(
                "MainUIForm playfield marker initialize failed because GoFuHua child count is '{0}', expected '{1}'.",
                _hatchSlotsRoot.childCount,
                PlayfieldEntityModule.HatchSlotCountValue);
            return false;
        }

        _queueMarkerTransforms = new RectTransform[_queueSlotsRoot.childCount];
        for (int i = 0; i < _queueSlotsRoot.childCount; i++)
        {
            _queueMarkerTransforms[i] = _queueSlotsRoot.GetChild(i) as RectTransform;
        }

        _tableMarkerTransforms = BuildMarkerTransformCache(_diningSeatsRoot, tableCount);

        _orchardMarkerTransforms = BuildMarkerTransformCache(_orchardSlotsRoot, orchardCount);

        _hatchMarkerTransforms = new RectTransform[_hatchSlotsRoot.childCount];
        for (int i = 0; i < _hatchSlotsRoot.childCount; i++)
        {
            _hatchMarkerTransforms[i] = _hatchSlotsRoot.GetChild(i) as RectTransform;
        }

        BuildPlayAreaMarkerCache();

        return true;
    }

    /// <summary>
    /// 按当前开放数量缓存 marker 根节点下前 N 个直接子节点。
    /// prefab 可以预放更多槽位，但当前快照只采样已经开放的那一段。
    /// </summary>
    /// <param name="markerRoot">marker 根节点。</param>
    /// <param name="activeCount">当前开放数量。</param>
    /// <returns>与当前开放数量一一对应的 marker 缓存数组。</returns>
    private static RectTransform[] BuildMarkerTransformCache(RectTransform markerRoot, int activeCount)
    {
        RectTransform[] markerTransforms = new RectTransform[activeCount];
        for (int i = 0; i < activeCount; i++)
        {
            markerTransforms[i] = markerRoot.GetChild(i) as RectTransform;
        }

        return markerTransforms;
    }

    /// <summary>
    /// 把当前 UI 标记点同步给全局实体模块。
    /// </summary>
    private void SyncPetPlacementMarkersToEntities()
    {
        if (!_isPetPlacementViewReady || GameEntry.PlayfieldEntities == null)
        {
            return;
        }

        RebuildPetPlacementLayoutIfNeeded();

        Camera worldCamera = GetPlayfieldWorldCamera();
        if (worldCamera == null)
        {
            return;
        }

        Camera uiCamera = GetMarkerUICamera();
        if (uiCamera == null)
        {
            LogMissingUICameraOnce();
            return;
        }

        Vector3[] hatchWorldPositions = CaptureWorldPositions(_hatchMarkerTransforms, uiCamera, worldCamera);
        Vector3[] queueWorldPositions = CaptureWorldPositions(_queueMarkerTransforms, uiCamera, worldCamera);
        Vector3[] tableWorldPositions = CaptureWorldPositions(_tableMarkerTransforms, uiCamera, worldCamera);
        Vector3[] orchardWorldPositions = CaptureWorldPositions(_orchardMarkerTransforms, uiCamera, worldCamera);
        PlayAreaWorldRegion[] playAreaWorldRegions = CapturePlayAreaWorldRegions(_playAreaMarkerTransforms, uiCamera, worldCamera);

        // 计算 BJRight 左边界的世界 X 坐标，供水果送达动画使用
        float rightPageLeftEdgeWorldX = CaptureRightPageLeftEdgeWorldX(uiCamera, worldCamera);

        PlayfieldMarkerSnapshot markerSnapshot = new PlayfieldMarkerSnapshot(
            hatchWorldPositions,
            queueWorldPositions,
            tableWorldPositions,
            orchardWorldPositions,
            playAreaWorldRegions,
            rightPageLeftEdgeWorldX);
        if (!markerSnapshot.IsValid)
        {
            return;
        }

        GameEntry.PlayfieldEntities.ApplyMarkerSnapshot(markerSnapshot);
    }

    /// <summary>
    /// 标记当前布局需要重新采样。
    /// </summary>
    private void MarkPetPlacementLayoutDirty()
    {
        _isPetPlacementLayoutDirty = true;
    }

    /// <summary>
    /// 标记宠物食物气泡 roster 需要重建。
    /// </summary>
    private void MarkPetFoodBubbleRosterDirty()
    {
        _isPetFoodBubbleRosterDirty = true;
    }

    /// <summary>
    /// 首次打开和分辨率变化后，先强制跑完布局系统，再采样 UI 标记点。
    /// </summary>
    private void RebuildPetPlacementLayoutIfNeeded()
    {
        if (!_isPetPlacementLayoutDirty)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();
        ForceRebuildLayout(_pageViewport);
        ForceRebuildLayout(_goYiDong);
        ForceRebuildLayout(_pageCenter);
        ForceRebuildLayout(_pageRight);
        ForceRebuildLayout(_queueSlotsRoot);
        ForceRebuildLayout(_diningSeatsRoot);
        ForceRebuildLayout(_orchardSlotsRoot);
        ForceRebuildLayout(_hatchSlotsRoot);
        if (_playAreaMarkerTransforms != null)
        {
            for (int i = 0; i < _playAreaMarkerTransforms.Length; i++)
            {
                ForceRebuildLayout(_playAreaMarkerTransforms[i]);
            }
        }
        Canvas.ForceUpdateCanvases();
        _isPetPlacementLayoutDirty = false;
    }

    /// <summary>
    /// 根据 Inspector 拖拽的列表重建玩耍区标记缓存。
    /// 允许列表为空，也会自动跳过 null 项。
    /// </summary>
    private void BuildPlayAreaMarkerCache()
    {
        if (_playAreas == null || _playAreas.Count == 0)
        {
            _playAreaMarkerTransforms = System.Array.Empty<RectTransform>();
            return;
        }

        int validCount = 0;
        for (int i = 0; i < _playAreas.Count; i++)
        {
            if (_playAreas[i] != null)
            {
                validCount++;
            }
        }

        if (validCount <= 0)
        {
            _playAreaMarkerTransforms = System.Array.Empty<RectTransform>();
            return;
        }

        _playAreaMarkerTransforms = new RectTransform[validCount];
        int writeIndex = 0;
        for (int i = 0; i < _playAreas.Count; i++)
        {
            RectTransform playArea = _playAreas[i];
            if (playArea == null)
            {
                continue;
            }

            _playAreaMarkerTransforms[writeIndex] = playArea;
            writeIndex++;
        }
    }

    /// <summary>
    /// 强制重建指定节点的布局。
    /// </summary>
    private static void ForceRebuildLayout(RectTransform rectTransform)
    {
        if (rectTransform == null)
        {
            return;
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
    }

    /// <summary>
    /// 获取用于投影场地实体的世界相机。
    /// </summary>
    private Camera GetPlayfieldWorldCamera()
    {
        if (_mainPageCamera != null && _mainPageCamera.isActiveAndEnabled)
        {
            return _mainPageCamera;
        }

        _mainPageCamera = FindSceneCameraByName("Main Camera");
        if (_mainPageCamera == null)
        {
            _mainPageCamera = Camera.main;
        }

        if (_mainPageCamera == null)
        {
            LogMissingMainCameraOnce();
        }

        return _mainPageCamera;
    }

    /// <summary>
    /// 获取用于采样 UI 标记点的相机。
    /// </summary>
    private Camera GetMarkerUICamera()
    {
        if (_uiPageCamera != null && _uiPageCamera.isActiveAndEnabled)
        {
            return _uiPageCamera;
        }

        if (_rootCanvas != null && _rootCanvas.worldCamera != null && _rootCanvas.worldCamera.isActiveAndEnabled)
        {
            _uiPageCamera = _rootCanvas.worldCamera;
            return _uiPageCamera;
        }

        _uiPageCamera = FindSceneCameraByName("UI Camera");
        if (_uiPageCamera == null)
        {
            LogMissingUICameraOnce();
        }

        return _uiPageCamera;
    }

    /// <summary>
    /// 将一组 UI 标记点转换为世界坐标数组。
    /// </summary>
    private static Vector3[] CaptureWorldPositions(RectTransform[] markerTransforms, Camera uiCamera, Camera worldCamera)
    {
        if (markerTransforms == null || worldCamera == null)
        {
            return null;
        }

        Vector3[] worldPositions = new Vector3[markerTransforms.Length];
        for (int i = 0; i < markerTransforms.Length; i++)
        {
            worldPositions[i] = ConvertRectTransformToWorldPosition(markerTransforms[i], uiCamera, worldCamera);
        }

        return worldPositions;
    }

    /// <summary>
    /// 将一组 UI 区域标记点转换为场地世界矩形区域数组。
    /// PlayArea 是一个范围，不是单个中心点，因此这里会保留投影后的矩形边界。
    /// </summary>
    private static PlayAreaWorldRegion[] CapturePlayAreaWorldRegions(RectTransform[] markerTransforms, Camera uiCamera, Camera worldCamera)
    {
        if (markerTransforms == null || worldCamera == null)
        {
            return null;
        }

        PlayAreaWorldRegion[] regions = new PlayAreaWorldRegion[markerTransforms.Length];
        for (int i = 0; i < markerTransforms.Length; i++)
        {
            regions[i] = ConvertRectTransformToWorldRegion(markerTransforms[i], uiCamera, worldCamera);
        }

        return regions;
    }

    /// <summary>
    /// 将单个 UI 标记点投影到场地世界坐标。
    /// </summary>
    private static Vector3 ConvertRectTransformToWorldPosition(RectTransform markerTransform, Camera uiCamera, Camera worldCamera)
    {
        if (markerTransform == null || worldCamera == null)
        {
            return Vector3.zero;
        }

        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(uiCamera, markerTransform.position);
        float worldDistance = Mathf.Abs(EntityWorldZ - worldCamera.transform.position.z);
        Vector3 worldPosition = worldCamera.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, worldDistance));
        worldPosition.z = EntityWorldZ;
        return worldPosition;
    }

    /// <summary>
    /// 将单个 UI 区域投影到场地世界中的矩形区域。
    /// </summary>
    private static PlayAreaWorldRegion ConvertRectTransformToWorldRegion(RectTransform markerTransform, Camera uiCamera, Camera worldCamera)
    {
        PlayAreaWorldRegion region = default;
        if (markerTransform == null || worldCamera == null)
        {
            return region;
        }

        Vector3[] corners = new Vector3[4];
        markerTransform.GetWorldCorners(corners);

        Vector3 bottomLeftWorld = ConvertScreenPointToWorldPosition(corners[0], uiCamera, worldCamera);
        Vector3 topLeftWorld = ConvertScreenPointToWorldPosition(corners[1], uiCamera, worldCamera);
        Vector3 topRightWorld = ConvertScreenPointToWorldPosition(corners[2], uiCamera, worldCamera);
        Vector3 bottomRightWorld = ConvertScreenPointToWorldPosition(corners[3], uiCamera, worldCamera);

        float minX = Mathf.Min(bottomLeftWorld.x, topLeftWorld.x, topRightWorld.x, bottomRightWorld.x);
        float maxX = Mathf.Max(bottomLeftWorld.x, topLeftWorld.x, topRightWorld.x, bottomRightWorld.x);
        float minY = Mathf.Min(bottomLeftWorld.y, topLeftWorld.y, topRightWorld.y, bottomRightWorld.y);
        float maxY = Mathf.Max(bottomLeftWorld.y, topLeftWorld.y, topRightWorld.y, bottomRightWorld.y);

        region.Min = new Vector2(minX, minY);
        region.Max = new Vector2(maxX, maxY);
        return region;
    }

    /// <summary>
    /// 把一个 UI 世界点先转成屏幕点，再投影到场地世界坐标。
    /// </summary>
    private static Vector3 ConvertScreenPointToWorldPosition(Vector3 uiWorldPoint, Camera uiCamera, Camera worldCamera)
    {
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(uiCamera, uiWorldPoint);
        float worldDistance = Mathf.Abs(EntityWorldZ - worldCamera.transform.position.z);
        Vector3 worldPosition = worldCamera.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, worldDistance));
        worldPosition.z = EntityWorldZ;
        return worldPosition;
    }

    /// <summary>
    /// 计算 BJRight 左边界在场地世界中的 X 坐标。
    /// 取 _pageRight 的左边两个角投影到世界后取最小 X。
    /// </summary>
    private float CaptureRightPageLeftEdgeWorldX(Camera uiCamera, Camera worldCamera)
    {
        if (_pageRight == null || uiCamera == null || worldCamera == null)
        {
            return 0f;
        }

        Vector3[] corners = new Vector3[4];
        _pageRight.GetWorldCorners(corners);

        // corners[0] = bottom-left, corners[1] = top-left
        Vector3 bottomLeftWorld = ConvertScreenPointToWorldPosition(corners[0], uiCamera, worldCamera);
        Vector3 topLeftWorld = ConvertScreenPointToWorldPosition(corners[1], uiCamera, worldCamera);
        return Mathf.Min(bottomLeftWorld.x, topLeftWorld.x);
    }

    /// <summary>
    /// 当前主相机相对中页的横向偏移。
    /// </summary>
    private float CurrentMainCameraOffsetX => _currentMainCameraOffsetX;

    /// <summary>
    /// 获取指定页对应的主相机横向偏移。
    /// 这里使用与实体建场一致的“UI 标记点投影到世界”的换算方式，
    /// 确保翻页时移动的是主相机，而不是重设实体世界坐标。
    /// </summary>
    private float GetMainCameraPageOffset(int pageIndex)
    {
        Camera worldCamera = GetPlayfieldWorldCamera();
        Camera uiCamera = GetMarkerUICamera();
        RectTransform targetPage = GetPageRootByIndex(pageIndex);
        if (worldCamera == null || uiCamera == null || _pageCenter == null || targetPage == null)
        {
            return 0f;
        }

        float worldDistance = Mathf.Abs(EntityWorldZ - worldCamera.transform.position.z);
        Vector2 centerScreenPoint = RectTransformUtility.WorldToScreenPoint(uiCamera, _pageCenter.position);
        Vector2 targetScreenPoint = RectTransformUtility.WorldToScreenPoint(uiCamera, targetPage.position);

        Vector3 centerWorldPoint = worldCamera.ScreenToWorldPoint(
            new Vector3(centerScreenPoint.x, centerScreenPoint.y, worldDistance));
        Vector3 targetWorldPoint = worldCamera.ScreenToWorldPoint(
            new Vector3(targetScreenPoint.x, targetScreenPoint.y, worldDistance));
        return targetWorldPoint.x - centerWorldPoint.x;
    }

    /// <summary>
    /// 应用主相机的分页横向偏移。
    /// </summary>
    private bool ApplyMainCameraOffset(float offsetX)
    {
        Camera mainCamera = GetPlayfieldWorldCamera();
        if (mainCamera == null)
        {
            return false;
        }

        if (!_hasCachedMainCameraBase)
        {
            _centerMainCameraPosition = mainCamera.transform.position - new Vector3(_currentMainCameraOffsetX, 0f, 0f);
            _hasCachedMainCameraBase = true;
        }

        _currentMainCameraOffsetX = offsetX;
        Vector3 cameraPosition = _centerMainCameraPosition;
        cameraPosition.x += offsetX;
        mainCamera.transform.position = cameraPosition;
        return true;
    }

    /// <summary>
    /// 根据当前主相机位置和当前分页偏移，反推出中页对应的主相机基准位置。
    /// 供分辨率变化后的重新排版使用。
    /// </summary>
    private void SyncMainCameraBaseFromCurrentOffset()
    {
        Camera mainCamera = GetPlayfieldWorldCamera();
        if (mainCamera == null)
        {
            return;
        }

        _centerMainCameraPosition = mainCamera.transform.position - new Vector3(_currentMainCameraOffsetX, 0f, 0f);
        _hasCachedMainCameraBase = true;
    }

    /// <summary>
    /// 根据页索引获取对应页根节点。
    /// </summary>
    private RectTransform GetPageRootByIndex(int pageIndex)
    {
        switch (pageIndex)
        {
            case LeftPageIndex:
                return _pageLeft;

            case RightPageIndex:
                return _pageRight;

            default:
                return _pageCenter;
        }
    }

    /// <summary>
    /// 按名称查找场景中的相机。
    /// </summary>
    private static Camera FindSceneCameraByName(string cameraName)
    {
        Camera[] cameras = Object.FindObjectsOfType<Camera>();
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera != null
                && camera.isActiveAndEnabled
                && string.Equals(camera.name, cameraName, System.StringComparison.Ordinal))
            {
                return camera;
            }
        }

        return null;
    }

    /// <summary>
    /// 记录缺失 UI 相机日志，避免重复刷屏。
    /// </summary>
    private void LogMissingUICameraOnce()
    {
        if (_hasLoggedMissingUICamera)
        {
            return;
        }

        _hasLoggedMissingUICamera = true;
        Log.Warning("MainUIForm can not find UI Camera for page switch.");
    }

    /// <summary>
    /// 记录缺失主相机日志，避免重复刷屏。
    /// </summary>
    private void LogMissingMainCameraOnce()
    {
        if (_hasLoggedMissingMainCamera)
        {
            return;
        }

        _hasLoggedMissingMainCamera = true;
        Log.Warning("MainUIForm can not find Main Camera for playfield projection.");
    }

    /// <summary>
    /// 订阅宠物站位变化事件和场地区容量变化事件。
    /// roster 只在真正发生站位变化时才重建，容量变化则只在升级时低频触发一次。
    /// </summary>
    private void EnsurePetPlacementEventSubscription()
    {
        if (!_isListeningPetPlacementChanged && GameEntry.PetPlacement != null)
        {
            GameEntry.PetPlacement.PlacementChanged += OnPetPlacementChanged;
            _isListeningPetPlacementChanged = true;
        }

        if (!_isListeningPlayfieldCapacityChanged && GameEntry.Fruits != null)
        {
            GameEntry.Fruits.PlayfieldCapacityChanged += OnPlayfieldCapacityChanged;
            _isListeningPlayfieldCapacityChanged = true;
        }
    }

    /// <summary>
    /// 取消订阅宠物站位变化事件和场地区容量变化事件。
    /// </summary>
    private void ReleasePetPlacementEventSubscription()
    {
        if (_isListeningPetPlacementChanged && GameEntry.PetPlacement != null)
        {
            GameEntry.PetPlacement.PlacementChanged -= OnPetPlacementChanged;
            _isListeningPetPlacementChanged = false;
        }

        if (_isListeningPlayfieldCapacityChanged && GameEntry.Fruits != null)
        {
            GameEntry.Fruits.PlayfieldCapacityChanged -= OnPlayfieldCapacityChanged;
            _isListeningPlayfieldCapacityChanged = false;
        }
    }

    /// <summary>
    /// 宠物站位变化回调。
    /// </summary>
    private void OnPetPlacementChanged()
    {
        MarkPetFoodBubbleRosterDirty();
    }

    /// <summary>
    /// 场地区容量变化回调。
    /// 建筑升级后只重采样当前开放的桌位与果园位，并把新快照同步给全局实体模块。
    /// </summary>
    /// <param name="diningSeatCount">最新餐桌位数量。</param>
    /// <param name="orchardSlotCount">最新果园位数量。</param>
    private void OnPlayfieldCapacityChanged(int diningSeatCount, int orchardSlotCount)
    {
        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        CachePetPlacementReferences();
        EnsurePetFoodBubbleRoot();

        _isPetPlacementViewReady = BuildPetPlacementViewCache();
        if (!_isPetPlacementViewReady)
        {
            return;
        }

        MarkPetPlacementLayoutDirty();
        MarkPetFoodBubbleRosterDirty();
        SyncPetPlacementMarkersToEntities();
        RebuildPetFoodBubbleRosterIfNeeded();
        UpdatePetFoodBubblePositions();
    }

    /// <summary>
    /// 确保宠物食物气泡根节点存在。
    /// 如果主界面预制体里没有预放置，则在运行时自动创建到 BJ 页面根节点下。
    /// </summary>
    private void EnsurePetFoodBubbleRoot()
    {
        RectTransform overlayParent = _pageCenter != null ? _pageCenter : _pageViewport;
        if (_petFoodBubbleRoot != null || overlayParent == null)
        {
            return;
        }

        Transform existingRoot = overlayParent.Find(PetFoodBubbleRootName);
        if (existingRoot != null)
        {
            _petFoodBubbleRoot = existingRoot as RectTransform;
            return;
        }

        GameObject rootObject = new GameObject(PetFoodBubbleRootName, typeof(RectTransform));
        RectTransform rootRectTransform = rootObject.GetComponent<RectTransform>();
        rootRectTransform.SetParent(overlayParent, false);
        rootRectTransform.anchorMin = Vector2.zero;
        rootRectTransform.anchorMax = Vector2.one;
        rootRectTransform.offsetMin = Vector2.zero;
        rootRectTransform.offsetMax = Vector2.zero;
        rootRectTransform.anchoredPosition = Vector2.zero;
        rootRectTransform.SetAsLastSibling();

        _petFoodBubbleRoot = rootRectTransform;
        _isRuntimeCreatedPetFoodBubbleRoot = true;
    }

    /// <summary>
    /// 按当前宠物站位重建一次食物气泡 roster。
    /// 只处理餐桌位宠物，其余区域的宠物不会生成气泡。
    /// </summary>
    private void RebuildPetFoodBubbleRosterIfNeeded()
    {
        if (!_isPetFoodBubbleRosterDirty)
        {
            return;
        }

        _isPetFoodBubbleRosterDirty = false;
        EnsurePetFoodBubbleRoot();
        if (_petFoodBubbleRoot == null || GameEntry.PetPlacement == null)
        {
            ReleaseAllActivePetFoodBubbleItems();
            return;
        }

        PetRuntimeState[] petStates = GameEntry.PetPlacement.GetAllPets();
        _petFoodBubbleRemoveBuffer.Clear();
        foreach (KeyValuePair<int, PetFoodBubbleItem> pair in _petFoodBubbleItemsByPetInstanceId)
        {
            _petFoodBubbleRemoveBuffer.Add(pair.Key);
        }

        for (int i = 0; i < petStates.Length; i++)
        {
            PetRuntimeState petState = petStates[i];
            if (petState == null
                || petState.PlacementType != PetPlacementType.DiningSeat
                || string.IsNullOrWhiteSpace(petState.DesiredFruitCode)
                || petState.DiningWishState != PetDiningWishState.Pending)
            {
                continue;
            }

            _petFoodBubbleRemoveBuffer.Remove(petState.InstanceId);
            EnsurePetFoodBubbleItem(petState);
        }

        for (int i = 0; i < _petFoodBubbleRemoveBuffer.Count; i++)
        {
            ReleasePetFoodBubbleItem(_petFoodBubbleRemoveBuffer[i]);
        }

        _petFoodBubbleRemoveBuffer.Clear();
    }

    /// <summary>
    /// 确保指定宠物拥有一个食物气泡项。
    /// </summary>
    /// <param name="petState">宠物运行时状态。</param>
    private void EnsurePetFoodBubbleItem(PetRuntimeState petState)
    {
        if (petState == null)
        {
            return;
        }

        if (!_petFoodBubbleItemsByPetInstanceId.TryGetValue(petState.InstanceId, out PetFoodBubbleItem bubbleItem)
            || bubbleItem == null)
        {
            bubbleItem = AcquirePetFoodBubbleItem();
            if (bubbleItem == null)
            {
                return;
            }

            _petFoodBubbleItemsByPetInstanceId[petState.InstanceId] = bubbleItem;
        }

        Sprite fruitSprite = null;
        if (GameEntry.GameAssets != null)
        {
            GameEntry.GameAssets.TryGetFruitSprite(petState.DesiredFruitCode, out fruitSprite);
        }

        bubbleItem.Bind(petState.InstanceId, fruitSprite, OnPetFoodBubbleClicked);
        bubbleItem.SetVisible(fruitSprite != null);
    }

    /// <summary>
    /// 从缓存池中获取一个食物气泡项。
    /// 若缓存池为空，则实例化新的 PetFoodBtn 预制体。
    /// </summary>
    /// <returns>可用的气泡项；若资源不可用则返回 null。</returns>
    private PetFoodBubbleItem AcquirePetFoodBubbleItem()
    {
        EnsurePetFoodBubbleRoot();
        if (_petFoodBubbleRoot == null)
        {
            return null;
        }

        while (_petFoodBubblePool.Count > 0)
        {
            PetFoodBubbleItem pooledBubbleItem = _petFoodBubblePool.Pop();
            if (pooledBubbleItem == null)
            {
                continue;
            }

            RectTransform pooledRectTransform = pooledBubbleItem.CachedRectTransform;
            if (pooledRectTransform != null)
            {
                pooledRectTransform.SetParent(_petFoodBubbleRoot, false);
            }

            pooledBubbleItem.gameObject.SetActive(true);
            return pooledBubbleItem;
        }

        if (GameEntry.GameAssets == null || !GameEntry.GameAssets.TryGetPetFoodBubblePrefab(out GameObject bubblePrefab) || bubblePrefab == null)
        {
            if (!_hasLoggedMissingPetFoodBubblePrefab)
            {
                _hasLoggedMissingPetFoodBubblePrefab = true;
                Log.Warning("MainUIForm can not create pet food bubble because prefab cache is missing.");
            }

            return null;
        }

        GameObject bubbleObject = Object.Instantiate(bubblePrefab, _petFoodBubbleRoot, false);
        PetFoodBubbleItem bubbleItem = bubbleObject.GetComponent<PetFoodBubbleItem>();
        if (bubbleItem == null)
        {
            bubbleItem = bubbleObject.AddComponent<PetFoodBubbleItem>();
        }

        return bubbleItem;
    }

    /// <summary>
    /// 释放指定宠物对应的食物气泡项。
    /// </summary>
    /// <param name="petInstanceId">宠物实例 Id。</param>
    private void ReleasePetFoodBubbleItem(int petInstanceId)
    {
        if (!_petFoodBubbleItemsByPetInstanceId.TryGetValue(petInstanceId, out PetFoodBubbleItem bubbleItem))
        {
            return;
        }

        _petFoodBubbleItemsByPetInstanceId.Remove(petInstanceId);
        if (bubbleItem == null)
        {
            return;
        }

        bubbleItem.SetVisible(false);
        _petFoodBubblePool.Push(bubbleItem);
    }

    /// <summary>
    /// 释放全部当前激活中的食物气泡项。
    /// 界面关闭时使用，重新打开后再按最新状态重建。
    /// </summary>
    private void ReleaseAllActivePetFoodBubbleItems()
    {
        _petFoodBubbleRemoveBuffer.Clear();
        foreach (KeyValuePair<int, PetFoodBubbleItem> pair in _petFoodBubbleItemsByPetInstanceId)
        {
            _petFoodBubbleRemoveBuffer.Add(pair.Key);
        }

        for (int i = 0; i < _petFoodBubbleRemoveBuffer.Count; i++)
        {
            ReleasePetFoodBubbleItem(_petFoodBubbleRemoveBuffer[i]);
        }

        _petFoodBubbleRemoveBuffer.Clear();
    }

    /// <summary>
    /// 销毁全部气泡项与运行时创建的气泡根节点。
    /// </summary>
    private void DestroyAllPetFoodBubbleItems()
    {
        foreach (KeyValuePair<int, PetFoodBubbleItem> pair in _petFoodBubbleItemsByPetInstanceId)
        {
            if (pair.Value != null)
            {
                Object.Destroy(pair.Value.gameObject);
            }
        }

        _petFoodBubbleItemsByPetInstanceId.Clear();

        while (_petFoodBubblePool.Count > 0)
        {
            PetFoodBubbleItem pooledBubbleItem = _petFoodBubblePool.Pop();
            if (pooledBubbleItem != null)
            {
                Object.Destroy(pooledBubbleItem.gameObject);
            }
        }

        if (_isRuntimeCreatedPetFoodBubbleRoot && _petFoodBubbleRoot != null)
        {
            Object.Destroy(_petFoodBubbleRoot.gameObject);
        }

        _isRuntimeCreatedPetFoodBubbleRoot = false;
    }

    /// <summary>
    /// 每帧刷新所有食物气泡的屏幕空间位置。
    /// 这里只做“世界坐标转 UI 坐标”和显隐切换，不重建 roster。
    /// </summary>
    private void UpdatePetFoodBubblePositions()
    {
        if (_petFoodBubbleItemsByPetInstanceId.Count == 0)
        {
            return;
        }

        Camera worldCamera = GetPlayfieldWorldCamera();
        if (worldCamera == null || _petFoodBubbleRoot == null || GameEntry.PlayfieldEntities == null)
        {
            HideAllActivePetFoodBubbles();
            return;
        }

        Camera uiCamera = GetPetFoodBubbleUICamera();
        foreach (KeyValuePair<int, PetFoodBubbleItem> pair in _petFoodBubbleItemsByPetInstanceId)
        {
            UpdateSinglePetFoodBubblePosition(pair.Key, pair.Value, worldCamera, uiCamera);
        }
    }

    /// <summary>
    /// 获取食物气泡投影所需的 UI 相机。
    /// Screen Space Overlay 下必须传 null；否则使用当前页面 UI 相机。
    /// </summary>
    /// <returns>可用于屏幕点转换的 UI 相机。</returns>
    private Camera GetPetFoodBubbleUICamera()
    {
        if (_rootCanvas != null && _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }

        return GetMarkerUICamera();
    }

    /// <summary>
    /// 刷新单个宠物食物气泡的位置与显隐。
    /// </summary>
    /// <param name="petInstanceId">宠物实例 Id。</param>
    /// <param name="bubbleItem">当前对应的气泡项。</param>
    /// <param name="worldCamera">场地世界相机。</param>
    /// <param name="uiCamera">UI 投影相机；Overlay 模式下为 null。</param>
    private void UpdateSinglePetFoodBubblePosition(int petInstanceId, PetFoodBubbleItem bubbleItem, Camera worldCamera, Camera uiCamera)
    {
        if (bubbleItem == null)
        {
            return;
        }

        if (!bubbleItem.HasFruitSprite)
        {
            bubbleItem.SetVisible(false);
            return;
        }

        if (!GameEntry.PlayfieldEntities.TryGetPetEntityLogic(petInstanceId, out PetEntityLogic petEntityLogic)
            || petEntityLogic == null
            || petEntityLogic.BubbleAnchor == null)
        {
            bubbleItem.SetVisible(false);
            return;
        }

        if (petEntityLogic.IsMoving || !GameEntry.PlayfieldEntities.IsPetAttachedToDiningTable(petInstanceId))
        {
            bubbleItem.SetVisible(false);
            return;
        }

        Vector3 screenPoint = worldCamera.WorldToScreenPoint(petEntityLogic.BubbleAnchor.position);
        if (screenPoint.z <= 0f)
        {
            bubbleItem.SetVisible(false);
            return;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_petFoodBubbleRoot, screenPoint, uiCamera, out Vector2 localPoint))
        {
            bubbleItem.SetVisible(false);
            return;
        }

        RectTransform bubbleRectTransform = bubbleItem.CachedRectTransform;
        if (bubbleRectTransform == null)
        {
            bubbleItem.SetVisible(false);
            return;
        }

        bubbleRectTransform.anchoredPosition = localPoint;
        bubbleItem.SetVisible(true);
    }

    /// <summary>
    /// 隐藏当前所有激活中的宠物食物气泡。
    /// 场地实体未就绪或相机缺失时使用，避免气泡残留在旧位置。
    /// </summary>
    private void HideAllActivePetFoodBubbles()
    {
        foreach (KeyValuePair<int, PetFoodBubbleItem> pair in _petFoodBubbleItemsByPetInstanceId)
        {
            if (pair.Value != null)
            {
                pair.Value.SetVisible(false);
            }
        }
    }

    /// <summary>
    /// 气泡按钮点击回调。
    /// UI 只做事件转发，真正的解锁校验、计时和上桌逻辑都交给全局点餐组件。
    /// </summary>
    /// <param name="petInstanceId">被点击的宠物实例 Id。</param>
    private void OnPetFoodBubbleClicked(int petInstanceId)
    {
        if (petInstanceId <= 0 || GameEntry.PetDiningOrders == null)
        {
            return;
        }

        GameEntry.PetDiningOrders.HandlePetFoodBubbleClick(petInstanceId);
    }
}
