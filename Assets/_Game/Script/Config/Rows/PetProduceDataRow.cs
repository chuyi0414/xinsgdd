using System;
using System.Text;
using UnityGameFramework.Runtime;

/// <summary>
/// 宠物产出数据表行。
/// 每行描述一个宠物可以产出的物品，包含等级与金币价值。
/// </summary>
public sealed class PetProduceDataRow : DataRowBase, ICodeDataRow
{
    /// <summary>
    /// 列拆分分隔符。
    /// </summary>
    private static readonly string[] ColumnSplitSeparator = { "\t" };

    /// <summary>
    /// 数据表固定列数（Id / PetId / Code / Name / Grade / CoinValue / Description）。
    /// </summary>
    private const int ColumnCount = 7;

    /// <summary>
    /// 合法产出 Code 的前缀。
    /// </summary>
    private const string CodePrefix = "petprod_";

    /// <summary>
    /// 当前行的内部 Id 缓存。
    /// </summary>
    private int _id;

    /// <summary>
    /// 产出唯一 Id。
    /// </summary>
    public override int Id => _id;

    /// <summary>
    /// 关联的宠物 Id（对应 PetDataRow.Id）。
    /// </summary>
    public int PetId { get; private set; }

    /// <summary>
    /// 机器码。
    /// </summary>
    public string Code { get; private set; }

    /// <summary>
    /// 产出物品显示名称。
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// 产出等级（初级 / 中级 / 高级）。
    /// </summary>
    public ProduceGradeType Grade { get; private set; }

    /// <summary>
    /// 产出物品的金币价值。
    /// </summary>
    public int CoinValue { get; private set; }

    /// <summary>
    /// 备注描述。
    /// </summary>
    public string Description { get; private set; }

    /// <summary>
    /// 从文本行解析宠物产出表数据。
    /// </summary>
    /// <param name="dataRowString">原始数据行文本。</param>
    /// <param name="userData">额外上下文。</param>
    /// <returns>是否解析成功。</returns>
    public override bool ParseDataRow(string dataRowString, object userData)
    {
        // 空行直接跳过
        if (string.IsNullOrWhiteSpace(dataRowString))
        {
            Log.Warning("PetProduceDataRow parse failed because row string is empty.");
            return false;
        }

        // 按 Tab 拆列
        string[] columns = dataRowString.Split(ColumnSplitSeparator, StringSplitOptions.None);
        if (columns.Length != ColumnCount)
        {
            Log.Warning("PetProduceDataRow parse failed because column count '{0}' is invalid, row '{1}'.",
                columns.Length, dataRowString);
            return false;
        }

        // [0] Id — 必须为正整数
        if (!int.TryParse(columns[0], out int id) || id <= 0)
        {
            Log.Warning("PetProduceDataRow parse failed because Id '{0}' is invalid.", columns[0]);
            return false;
        }

        // [1] PetId — 必须为正整数，运行时会与 PetDataRow 关联
        if (!int.TryParse(columns[1], out int petId) || petId <= 0)
        {
            Log.Warning("PetProduceDataRow parse failed because PetId '{0}' is invalid.", columns[1]);
            return false;
        }

        // [2] Code — 必须以 petprod_ 前缀开头
        string code = columns[2].Trim();
        if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(CodePrefix, StringComparison.Ordinal))
        {
            Log.Warning("PetProduceDataRow parse failed because Code '{0}' is invalid.", columns[2]);
            return false;
        }

        // [3] Name — 不能为空
        string name = columns[3].Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Log.Warning("PetProduceDataRow parse failed because Name is empty, code '{0}'.", code);
            return false;
        }

        // [4] Grade — 必须是合法的 ProduceGradeType 枚举值
        if (!Enum.TryParse(columns[4].Trim(), true, out ProduceGradeType grade)
            || !Enum.IsDefined(typeof(ProduceGradeType), grade))
        {
            Log.Warning("PetProduceDataRow parse failed because Grade '{0}' is invalid, code '{1}'.",
                columns[4], code);
            return false;
        }

        // [5] CoinValue — 必须为正整数
        if (!int.TryParse(columns[5], out int coinValue) || coinValue <= 0)
        {
            Log.Warning("PetProduceDataRow parse failed because CoinValue '{0}' is invalid, code '{1}'.",
                columns[5], code);
            return false;
        }

        // 所有校验通过，写入字段
        _id = id;
        PetId = petId;
        Code = code;
        Name = name;
        Grade = grade;
        CoinValue = coinValue;
        Description = columns[6].Trim();
        return true;
    }

    /// <summary>
    /// 从二进制数据解析宠物产出表数据。
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
