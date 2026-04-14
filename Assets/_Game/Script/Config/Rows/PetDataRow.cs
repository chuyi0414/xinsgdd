using System;
using System.Text;
using UnityGameFramework.Runtime;

/// <summary>
/// 宠物系统数据表行。
/// </summary>
public sealed class PetDataRow : DataRowBase, ICodeDataRow
{
    /// <summary>
    /// 列拆分分隔符。
    /// </summary>
    private static readonly string[] ColumnSplitSeparator = { "\t" };

    /// <summary>
    /// 数据表固定列数。
    /// </summary>
    private const int ColumnCount = 10;

    /// <summary>
    /// 合法宠物 Code 的前缀。
    /// </summary>
    private const string CodePrefix = "pet_";

    /// <summary>
    /// 当前行的内部 Id 缓存。
    /// </summary>
    private int _id;

    /// <summary>
    /// 宠物唯一 Id。
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
    /// 宠物品质。
    /// </summary>
    public QualityType Quality { get; private set; }

    /// <summary>
    /// Spine SkeletonData 资源路径。
    /// </summary>
    public string SkeletonDataPath { get; private set; }

    /// <summary>
    /// 待机动画名。
    /// </summary>
    public string IdleAnimationName { get; private set; }

    /// <summary>
    /// 移动动画名。
    /// </summary>
    public string MoveAnimationName { get; private set; }

    /// <summary>
    /// 属性类型。
    /// </summary>
    public PetAttributeType AttributeType { get; private set; }

    /// <summary>
    /// 属性数值。
    /// </summary>
    public int AttributeValue { get; private set; }

    /// <summary>
    /// 备注描述。
    /// </summary>
    public string Description { get; private set; }

    /// <summary>
    /// 从文本行解析宠物表数据。
    /// </summary>
    /// <param name="dataRowString">原始数据行文本。</param>
    /// <param name="userData">额外上下文。</param>
    /// <returns>是否解析成功。</returns>
    public override bool ParseDataRow(string dataRowString, object userData)
    {
        if (string.IsNullOrWhiteSpace(dataRowString))
        {
            Log.Warning("PetDataRow parse failed because row string is empty.");
            return false;
        }

        string[] columns = dataRowString.Split(ColumnSplitSeparator, StringSplitOptions.None);
        if (columns.Length != ColumnCount)
        {
            Log.Warning("PetDataRow parse failed because column count '{0}' is invalid, row '{1}'.", columns.Length, dataRowString);
            return false;
        }

        if (!int.TryParse(columns[0], out int id) || id <= 0)
        {
            Log.Warning("PetDataRow parse failed because Id '{0}' is invalid.", columns[0]);
            return false;
        }

        string code = columns[1].Trim();
        if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(CodePrefix, StringComparison.Ordinal))
        {
            Log.Warning("PetDataRow parse failed because Code '{0}' is invalid.", columns[1]);
            return false;
        }

        string name = columns[2].Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Log.Warning("PetDataRow parse failed because Name is empty, code '{0}'.", code);
            return false;
        }

        if (!Enum.TryParse(columns[3].Trim(), true, out QualityType quality) || !Enum.IsDefined(typeof(QualityType), quality) || quality == QualityType.Universal)
        {
            Log.Warning("PetDataRow parse failed because Quality '{0}' is invalid, code '{1}'.", columns[3], code);
            return false;
        }

        string skeletonDataPath = columns[4].Trim();
        if (string.IsNullOrWhiteSpace(skeletonDataPath))
        {
            Log.Warning("PetDataRow parse failed because SkeletonDataPath is empty, code '{0}'.", code);
            return false;
        }

        string idleAnimationName = columns[5].Trim();
        if (string.IsNullOrWhiteSpace(idleAnimationName))
        {
            Log.Warning("PetDataRow parse failed because IdleAnimationName is empty, code '{0}'.", code);
            return false;
        }

        string moveAnimationName = columns[6].Trim();
        if (string.IsNullOrWhiteSpace(moveAnimationName))
        {
            Log.Warning("PetDataRow parse failed because MoveAnimationName is empty, code '{0}'.", code);
            return false;
        }

        if (!Enum.TryParse(columns[7].Trim(), true, out PetAttributeType attributeType) || !Enum.IsDefined(typeof(PetAttributeType), attributeType))
        {
            Log.Warning("PetDataRow parse failed because AttributeType '{0}' is invalid, code '{1}'.", columns[7], code);
            return false;
        }

        if (!int.TryParse(columns[8], out int attributeValue))
        {
            Log.Warning("PetDataRow parse failed because AttributeValue '{0}' is invalid, code '{1}'.", columns[8], code);
            return false;
        }

        if (attributeType == PetAttributeType.None && attributeValue != 0)
        {
            Log.Warning("PetDataRow parse failed because AttributeType None requires AttributeValue 0, code '{0}'.", code);
            return false;
        }

        if (attributeType != PetAttributeType.None && attributeValue <= 0)
        {
            Log.Warning("PetDataRow parse failed because AttributeValue must be > 0, code '{0}'.", code);
            return false;
        }

        _id = id;
        Code = code;
        Name = name;
        Quality = quality;
        SkeletonDataPath = skeletonDataPath;
        IdleAnimationName = idleAnimationName;
        MoveAnimationName = moveAnimationName;
        AttributeType = attributeType;
        AttributeValue = attributeValue;
        Description = columns[9].Trim();
        return true;
    }

    /// <summary>
    /// 从二进制数据解析宠物表数据。
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
