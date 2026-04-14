using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 蛋实体逻辑。
/// </summary>
public sealed class EggEntityLogic : EntityLogic
{
    /// <summary>
    /// 蛋实体的精灵渲染器。
    /// </summary>
    private SpriteRenderer _spriteRenderer;

    /// <summary>
    /// 当前已经应用到实体上的蛋 Code。
    /// </summary>
    private string _currentEggCode;

    /// <summary>
    /// 预制体默认精灵，用于资源缺失时回退。
    /// </summary>
    private Sprite _defaultSprite;

    /// <summary>
    /// 初始化并缓存常用组件。
    /// </summary>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        CacheComponents();
    }

    /// <summary>
    /// 实体显示时应用最新显示数据。
    /// </summary>
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        ApplyData(userData as EggEntityData);
    }

    /// <summary>
    /// 实体隐藏时清空显示状态。
    /// </summary>
    protected override void OnHide(bool isShutdown, object userData)
    {
        if (_spriteRenderer != null)
        {
            _spriteRenderer.sprite = null;
        }

        _currentEggCode = null;
        base.OnHide(isShutdown, userData);
    }

    /// <summary>
    /// 应用当前蛋实体显示数据。
    /// </summary>
    public void ApplyData(EggEntityData entityData)
    {
        if (entityData == null)
        {
            return;
        }

        SetWorldPosition(entityData.WorldPosition);
        ApplyEggVisual(entityData.EggCode);
    }

    /// <summary>
    /// 更新蛋实体世界位置。
    /// </summary>
    public void SetWorldPosition(Vector3 worldPosition)
    {
        CachedTransform.position = worldPosition;
    }

    /// <summary>
    /// 缓存渲染组件并记录默认精灵。
    /// </summary>
    private void CacheComponents()
    {
        if (_spriteRenderer == null)
        {
            _spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
            if (_spriteRenderer != null)
            {
                _defaultSprite = _spriteRenderer.sprite;
            }
        }
    }

    /// <summary>
    /// 按蛋配置刷新实体外观。
    /// </summary>
    private void ApplyEggVisual(string eggCode)
    {
        CacheComponents();
        if (_spriteRenderer == null || string.IsNullOrWhiteSpace(eggCode) || GameEntry.DataTables == null)
        {
            return;
        }

        if (string.Equals(_currentEggCode, eggCode, System.StringComparison.Ordinal))
        {
            return;
        }

        EggDataRow eggDataRow = GameEntry.DataTables.GetDataRowByCode<EggDataRow>(eggCode);
        if (eggDataRow == null)
        {
            Log.Warning("EggEntityLogic can not find egg data row by code '{0}'.", eggCode);
            return;
        }

        Sprite eggSprite = null;
        if (GameEntry.GameAssets != null)
        {
            GameEntry.GameAssets.TryGetEggSprite(eggDataRow.IconPath, out eggSprite);
        }

        if (eggSprite == null)
        {
            if (_defaultSprite == null)
            {
                Log.Warning("EggEntityLogic can not find cached egg sprite by path '{0}', and prefab default sprite is also missing.", eggDataRow.IconPath);
                return;
            }

            Log.Warning("EggEntityLogic can not find cached egg sprite by path '{0}', fallback to prefab default sprite.", eggDataRow.IconPath);
            eggSprite = _defaultSprite;
        }

        _spriteRenderer.sprite = eggSprite;
        _spriteRenderer.color = Color.white;
        _currentEggCode = eggCode;
    }
}
