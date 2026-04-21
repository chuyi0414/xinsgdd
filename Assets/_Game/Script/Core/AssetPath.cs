using System;

/// <summary>
/// 游戏资源路径工具。
/// 统一收口 GF 资源模块使用的路径前缀，避免业务代码分散拼接字符串。
/// </summary>
public static class AssetPath
{
    /// <summary>
    /// UI 预制体根路径。
    /// </summary>
    public const string UIRoot = "Prefabs/UI/";

    /// <summary>
    /// 实体预制体根路径。
    /// </summary>
    public const string EntityRoot = "Prefabs/Entity/";

    /// <summary>
    /// 数据表资源根路径。
    /// </summary>
    public const string DataTableRoot = "DataTable/";

    /// <summary>
    /// 战斗消除分数数字精灵根路径。
    /// 子文件夹名即为数字编号，如 "2/" 对应 Score/2 下的 0~9.png。
    /// </summary>
    public const string CombatScoreDigitRoot = "Arts/Combat/Eliminate/Score/";

    /// <summary>
    /// 获取 UI 资源路径。
    /// </summary>
    /// <param name="subPath">UI 子路径，例如 Login/LoginUIForm。</param>
    /// <returns>完整的 UI 资源路径。</returns>
    public static string GetUI(string subPath)
    {
        return Combine(UIRoot, subPath);
    }

    /// <summary>
    /// 获取实体资源路径。
    /// </summary>
    /// <param name="subPath">实体子路径，例如 Monster/Slime。</param>
    /// <returns>完整的实体资源路径。</returns>
    public static string GetEntity(string subPath)
    {
        return Combine(EntityRoot, subPath);
    }

    /// <summary>
    /// 获取数据表资源路径。
    /// </summary>
    /// <param name="subPath">数据表子路径，例如 Egg。</param>
    /// <returns>完整的数据表资源路径。</returns>
    public static string GetDataTable(string subPath)
    {
        return Combine(DataTableRoot, subPath);
    }

    /// <summary>
    /// 组合资源前缀与子路径。
    /// 这里统一去掉子路径前导斜杠，避免调用方手误导致路径格式不一致。
    /// </summary>
    /// <param name="root">资源根路径。</param>
    /// <param name="subPath">资源子路径。</param>
    /// <returns>拼接后的资源路径。</returns>
    private static string Combine(string root, string subPath)
    {
        if (string.IsNullOrWhiteSpace(subPath))
        {
            throw new ArgumentException("资源子路径不能为空。", nameof(subPath));
        }

        return root + subPath.Trim().TrimStart('/');
    }
}
