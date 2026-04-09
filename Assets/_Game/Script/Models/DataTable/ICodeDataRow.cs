/// <summary>
/// 带稳定 Code 的数据表行接口。
/// 用于通用数据表模块构建按 Code 查询索引。
/// </summary>
public interface ICodeDataRow
{
    /// <summary>
    /// 获取数据表行的稳定机器码。
    /// </summary>
    string Code { get; }
}
