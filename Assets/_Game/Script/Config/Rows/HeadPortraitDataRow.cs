using System;
using System.Text;
using UnityGameFramework.Runtime;

/// <summary>
/// 头像数据表行。
/// 定义每条头像的配置数据：名称、图标路径、解锁方式等。
/// </summary>
public sealed class HeadPortraitDataRow : DataRowBase, ICodeDataRow
{
    /// <summary>
    /// 列拆分分隔符。
    /// </summary>
    private static readonly string[] ColumnSplitSeparator = { "\t" };

    /// <summary>
    /// 数据表固定列数。
    /// </summary>
    private const int ColumnCount = 8;

    /// <summary>
    /// 合法头像 Code 的前缀。
    /// </summary>
    private const string CodePrefix = "head_portrait_";

    /// <summary>
    /// 当前行的内部 Id 缓存。
    /// </summary>
    private int _id;

    /// <summary>
    /// 头像唯一 Id。
    /// </summary>
    public override int Id => _id;

    /// <summary>
    /// 机器码。
    /// </summary>
    public string Code { get; private set; }

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// 图标资源路径。
    /// 用于加载头像 Sprite 或 Texture2D。
    /// </summary>
    public string IconPath { get; private set; }

    /// <summary>
    /// 是否默认解锁。
    /// true 表示开局即拥有，false 表示需要通过获取方式解锁。
    /// </summary>
    public bool IsDefaultUnlocked { get; private set; }

    /// <summary>
    /// 获取类型。
    /// gold = 金币购买，other = 其他方式。
    /// </summary>
    public string AcquireType { get; private set; }

    /// <summary>
    /// 获取参数。
    /// 当 AcquireType = gold 时，表示金币价格；其他类型时含义由业务定义。
    /// </summary>
    public int AcquireParam { get; private set; }

    /// <summary>
    /// 获取描述。
    /// 显示在 UI 上的解锁条件文案，如 "1000金币"。
    /// </summary>
    public string AcquireDesc { get; private set; }

    /// <summary>
    /// 从文本行解析头像表数据。
    /// </summary>
    /// <param name="dataRowString">原始数据行文本。</param>
    /// <param name="userData">额外上下文。</param>
    /// <returns>是否解析成功。</returns>
    public override bool ParseDataRow(string dataRowString, object userData)
    {
        if (string.IsNullOrWhiteSpace(dataRowString))
        {
            Log.Warning("HeadPortraitDataRow parse failed because row string is empty.");
            return false;
        }

        string[] columns = dataRowString.Split(ColumnSplitSeparator, StringSplitOptions.None);
        if (columns.Length != ColumnCount)
        {
            Log.Warning("HeadPortraitDataRow parse failed because column count '{0}' is invalid, row '{1}'.", columns.Length, dataRowString);
            return false;
        }

        // ─── Id ───
        if (!int.TryParse(columns[0], out int id) || id <= 0)
        {
            Log.Warning("HeadPortraitDataRow parse failed because Id '{0}' is invalid.", columns[0]);
            return false;
        }

        // ─── Code ───
        string code = columns[1].Trim();
        if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(CodePrefix, StringComparison.Ordinal))
        {
            Log.Warning("HeadPortraitDataRow parse failed because Code '{0}' is invalid.", columns[1]);
            return false;
        }

        // ─── Name ───
        string name = columns[2].Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Log.Warning("HeadPortraitDataRow parse failed because Name is empty, code '{0}'.", code);
            return false;
        }

        // ─── IconPath ───
        string iconPath = columns[3].Trim();
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            Log.Warning("HeadPortraitDataRow parse failed because IconPath is empty, code '{0}'.", code);
            return false;
        }

        // ─── IsDefaultUnlocked ───
        if (!bool.TryParse(columns[4].Trim(), out bool isDefaultUnlocked))
        {
            Log.Warning("HeadPortraitDataRow parse failed because IsDefaultUnlocked '{0}' is invalid, code '{1}'.", columns[4], code);
            return false;
        }

        // ─── AcquireType ───
        string acquireType = columns[5].Trim();
        if (string.IsNullOrWhiteSpace(acquireType))
        {
            Log.Warning("HeadPortraitDataRow parse failed because AcquireType is empty, code '{0}'.", code);
            return false;
        }

        // ─── AcquireParam ───
        if (!int.TryParse(columns[6], out int acquireParam) || acquireParam < 0)
        {
            Log.Warning("HeadPortraitDataRow parse failed because AcquireParam '{0}' is invalid, code '{1}'.", columns[6], code);
            return false;
        }

        // 默认解锁的头像，AcquireParam 必须为 0
        if (isDefaultUnlocked && acquireParam != 0)
        {
            Log.Warning("HeadPortraitDataRow parse failed because default unlocked portrait '{0}' must have AcquireParam = 0.", code);
            return false;
        }

        // 非默认解锁且获取类型为 gold 的头像，AcquireParam 必须大于 0
        if (!isDefaultUnlocked && string.Equals(acquireType, "gold", StringComparison.OrdinalIgnoreCase) && acquireParam <= 0)
        {
            Log.Warning("HeadPortraitDataRow parse failed because locked gold portrait '{0}' must have AcquireParam > 0.", code);
            return false;
        }

        // ─── AcquireDesc ───
        string acquireDesc = columns[7].Trim();

        _id = id;
        Code = code;
        Name = name;
        IconPath = iconPath;
        IsDefaultUnlocked = isDefaultUnlocked;
        AcquireType = acquireType;
        AcquireParam = acquireParam;
        AcquireDesc = acquireDesc;
        return true;
    }

    /// <summary>
    /// 从二进制数据解析头像表数据。
    /// </summary>
    /// <param name="dataRowBytes">原始字节数组。</param>
    /// <param name="startIndex">起始下标。</param>
    /// <param name="length">读取长度。</param>
    /// <param name="userData">额外上下文。</param>
    /// <returns>是否解析成功。</returns>
    public override bool ParseDataRow(byte[] dataRowBytes, int startIndex, int length, object userData)
    {
        return ParseDataRow(Encoding.UTF8.GetString(dataRowBytes, startIndex, length), userData);
    }
}
