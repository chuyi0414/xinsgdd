using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 果园实体逻辑。
/// </summary>
public sealed class OrchardEntityLogic : EntityLogic
{
    /// <summary>
    /// 实体显示时应用最新显示数据。
    /// </summary>
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        ApplyData(userData as OrchardEntityData);
    }

    /// <summary>
    /// 应用果园实体显示数据。
    /// </summary>
    public void ApplyData(OrchardEntityData entityData)
    {
        if (entityData == null)
        {
            return;
        }

        SetWorldPosition(entityData.WorldPosition);
    }

    /// <summary>
    /// 更新果园实体世界位置。
    /// </summary>
    public void SetWorldPosition(Vector3 worldPosition)
    {
        CachedTransform.position = worldPosition;
    }
}
