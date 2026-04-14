using UnityEngine;

/// <summary>
/// 玩耍区在场地世界中的矩形区域。
/// 由 MainUI 的 RectTransform 区域投影而来，用于在该范围内随机取点。
/// </summary>
public struct PlayAreaWorldRegion
{
    /// <summary>
    /// 区域左下角。
    /// </summary>
    public Vector2 Min;

    /// <summary>
    /// 区域右上角。
    /// </summary>
    public Vector2 Max;

    /// <summary>
    /// 当前区域是否有效。
    /// </summary>
    public bool IsValid => Max.x >= Min.x && Max.y >= Min.y;

    /// <summary>
    /// 根据 0 到 1 的归一化随机点，映射出区域内的实际世界坐标。
    /// </summary>
    /// <param name="normalizedPosition">归一化位置。</param>
    /// <returns>区域内对应的世界坐标。</returns>
    public Vector3 Evaluate(Vector2 normalizedPosition)
    {
        float x = Mathf.LerpUnclamped(Min.x, Max.x, Mathf.Clamp01(normalizedPosition.x));
        float y = Mathf.LerpUnclamped(Min.y, Max.y, Mathf.Clamp01(normalizedPosition.y));
        return new Vector3(x, y, 0f);
    }
}
