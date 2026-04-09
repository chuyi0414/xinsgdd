using System;
using System.Text;
using UnityGameFramework.Runtime;

/// <summary>
/// 宠物系统数据表行。
/// </summary>
public sealed class PetDataRow : DataRowBase, ICodeDataRow
{
    private static readonly string[] ColumnSplitSeparator = { "\t" };
    private const int ColumnCount = 8;
    private const string CodePrefix = "pet_";

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

        if (!Enum.TryParse(columns[5].Trim(), true, out PetAttributeType attributeType) || !Enum.IsDefined(typeof(PetAttributeType), attributeType))
        {
            Log.Warning("PetDataRow parse failed because AttributeType '{0}' is invalid, code '{1}'.", columns[5], code);
            return false;
        }

        if (!int.TryParse(columns[6], out int attributeValue))
        {
            Log.Warning("PetDataRow parse failed because AttributeValue '{0}' is invalid, code '{1}'.", columns[6], code);
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
        AttributeType = attributeType;
        AttributeValue = attributeValue;
        Description = columns[7].Trim();
        return true;
    }

    public override bool ParseDataRow(byte[] dataRowBytes, int startIndex, int length, object userData)
    {
        return ParseDataRow(Encoding.UTF8.GetString(dataRowBytes, startIndex, length), userData);
    }
}
