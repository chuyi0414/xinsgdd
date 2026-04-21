using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
///  CSV 解析器。
/// 这里只保留“临时迁移卡片生成机制”所必需的数据：
/// 1. 固定布局卡；
/// 2. 类型配置；
/// 3. 方向任务声明。
/// </summary>
public static class DailyChallengeCsvParser
{
    /// <summary>
    /// 解析一份 CSV 文本。
    /// </summary>
    /// <param name="csvContent">CSV 原始文本。</param>
    /// <returns>解析完成的关卡数据。</returns>
    public static DailyChallengeParsedLevel Parse(string csvContent)
    {
        DailyChallengeParsedLevel parsedLevel = new DailyChallengeParsedLevel();
        if (string.IsNullOrWhiteSpace(csvContent))
        {
            return parsedLevel;
        }

        string[] lines = csvContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        int layoutIndex = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i] == null ? string.Empty : lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = line.Split(',');
            if (parts.Length <= 0)
            {
                continue;
            }

            if (IsLayoutRecord(parts))
            {
                DailyChallengeFixedCardRecord fixedCard = ParseFixedCardRecord(parts, layoutIndex);
                parsedLevel.FixedCards.Add(fixedCard);
                layoutIndex++;
                continue;
            }

            string directiveTag = NormalizeDirectiveTag(parts[0]);
            if (string.Equals(directiveTag, "DIR", StringComparison.Ordinal))
            {
                DailyChallengeDirectionTask directionTask = ParseLegacyDirectionTask(parts);
                if (directionTask != null)
                {
                    parsedLevel.DirectionTasks.Add(directionTask);
                }

                continue;
            }

            if (string.Equals(directiveTag, "EXTENDEDDIR", StringComparison.Ordinal))
            {
                DailyChallengeDirectionTask directionTask = ParseExtendedDirectionTask(parts);
                if (directionTask != null)
                {
                    parsedLevel.DirectionTasks.Add(directionTask);
                }

                continue;
            }

            DailyChallengeTypeConfig typeConfig = ParseTypeConfig(parts);
            if (typeConfig != null)
            {
                parsedLevel.TypeConfigs.Add(typeConfig);
            }
        }

        return parsedLevel;
    }

    /// <summary>
    /// 判断当前一行是否是固定布局数据。
    /// 布局行的第 1~3 列必须能解析为数字。
    /// </summary>
    private static bool IsLayoutRecord(string[] parts)
    {
        if (parts == null || parts.Length < 5)
        {
            return false;
        }

        return TryParseFloat(parts[0], out _)
            && TryParseFloat(parts[1], out _)
            && TryParseFloat(parts[2], out _);
    }

    /// <summary>
    /// 解析固定布局卡记录。
    /// CSV 口径沿用源项目：
    /// Row, Col, Layer, Direction, OffsetAmount
    /// </summary>
    private static DailyChallengeFixedCardRecord ParseFixedCardRecord(string[] parts, int layoutIndex)
    {
        int rowIndex = Mathf.RoundToInt(ParseFloatOrDefault(parts[0], 0f));
        int colIndex = Mathf.RoundToInt(ParseFloatOrDefault(parts[1], 0f));
        int layerIndex = Mathf.RoundToInt(ParseFloatOrDefault(parts[2], 0f));
        Vector2 offsetDirection = ParseDirectionVector(parts.Length > 3 ? parts[3] : string.Empty);
        float offsetAmount = parts.Length > 4 ? ParseFloatOrDefault(parts[4], 0f) : 0f;

        return new DailyChallengeFixedCardRecord(
            layoutIndex,
            rowIndex,
            colIndex,
            layerIndex,
            offsetDirection.x,
            offsetDirection.y,
            offsetAmount);
    }

    /// <summary>
    /// 解析旧版 DIR 任务。
    /// 旧版只告诉方向和数量，起点依赖源工程 Inspector 数据。
    /// 因此这里仅记录下来，后续由上层决定是否忽略。
    /// </summary>
    private static DailyChallengeDirectionTask ParseLegacyDirectionTask(string[] parts)
    {
        if (parts == null || parts.Length < 3)
        {
            return null;
        }

        int count = Mathf.RoundToInt(ParseFloatOrDefault(parts[2], 0f));
        if (count <= 0)
        {
            return null;
        }

        return new DailyChallengeDirectionTask(
            ParseDirection(parts[1]),
            count,
            1f,
            false,
            0,
            0,
            0);
    }

    /// <summary>
    /// 解析扩展版方向任务。
    /// 扩展版自带起始行列和层级，因此当前项目可以直接使用。
    /// </summary>
    private static DailyChallengeDirectionTask ParseExtendedDirectionTask(string[] parts)
    {
        if (parts == null || parts.Length < 7)
        {
            return null;
        }

        int count = Mathf.RoundToInt(ParseFloatOrDefault(parts[2], 0f));
        if (count <= 0)
        {
            return null;
        }

        float spacing = Mathf.Max(0.01f, ParseFloatOrDefault(parts[3], 1f));
        int startRow = Mathf.RoundToInt(ParseFloatOrDefault(parts[4], 0f));
        int startCol = Mathf.RoundToInt(ParseFloatOrDefault(parts[5], 0f));
        int startLayer = Mathf.RoundToInt(ParseFloatOrDefault(parts[6], 0f));

        return new DailyChallengeDirectionTask(
            ParseDirection(parts[1]),
            count,
            spacing,
            true,
            startRow,
            startCol,
            startLayer);
    }

    /// <summary>
    /// 解析类型配置。
    /// 当前支持两种格式：
    /// 1. SpriteName, Count
    /// 2. -1, Probability
    /// </summary>
    private static DailyChallengeTypeConfig ParseTypeConfig(string[] parts)
    {
        if (parts == null || parts.Length < 2)
        {
            return null;
        }

        string spriteName = SafeTrim(parts[0]);
        if (string.IsNullOrWhiteSpace(spriteName))
        {
            return null;
        }

        float rawValue = ParseFloatOrDefault(parts[1], 0f);
        if (string.Equals(spriteName, "-1", StringComparison.Ordinal))
        {
            return new DailyChallengeTypeConfig(spriteName, 0, Mathf.Max(0f, rawValue));
        }

        bool looksLikeProbability = parts[1] != null && parts[1].Contains(".");
        if (looksLikeProbability || rawValue < 1f)
        {
            return new DailyChallengeTypeConfig(spriteName, 0, Mathf.Max(0f, rawValue));
        }

        return new DailyChallengeTypeConfig(spriteName, Mathf.Max(0, Mathf.RoundToInt(rawValue)), 0f);
    }

    /// <summary>
    /// 规范化指令标记。
    /// 会去掉空格并统一转大写。
    /// </summary>
    private static string NormalizeDirectiveTag(string rawValue)
    {
        string trimmedValue = SafeTrim(rawValue);
        if (string.IsNullOrWhiteSpace(trimmedValue))
        {
            return string.Empty;
        }

        return trimmedValue.Replace(" ", string.Empty).ToUpperInvariant();
    }

    /// <summary>
    /// 将中文方向名转换为项目内部方向枚举。
    /// </summary>
    private static DailyChallengeDirection ParseDirection(string rawValue)
    {
        switch (SafeTrim(rawValue))
        {
            case "上":
                return DailyChallengeDirection.Up;
            case "下":
                return DailyChallengeDirection.Down;
            case "左":
                return DailyChallengeDirection.Left;
            case "右":
                return DailyChallengeDirection.Right;
            case "左上":
                return DailyChallengeDirection.UpLeft;
            case "右上":
                return DailyChallengeDirection.UpRight;
            case "左下":
                return DailyChallengeDirection.DownLeft;
            case "右下":
                return DailyChallengeDirection.DownRight;
            default:
                return DailyChallengeDirection.None;
        }
    }

    /// <summary>
    /// 将中文方向名转换为逻辑坐标偏移。
    /// 注意这里返回的是“逻辑格坐标偏移方向”，不是归一化向量。
    /// 这样 `offsetAmount=0.5` 时，能直接表达“半格偏移”。
    /// </summary>
    private static Vector2 ParseDirectionVector(string rawValue)
    {
        switch (ParseDirection(rawValue))
        {
            case DailyChallengeDirection.Up:
                return new Vector2(0f, 1f);
            case DailyChallengeDirection.Down:
                return new Vector2(0f, -1f);
            case DailyChallengeDirection.Left:
                return new Vector2(-1f, 0f);
            case DailyChallengeDirection.Right:
                return new Vector2(1f, 0f);
            case DailyChallengeDirection.UpLeft:
                return new Vector2(-1f, 1f);
            case DailyChallengeDirection.UpRight:
                return new Vector2(1f, 1f);
            case DailyChallengeDirection.DownLeft:
                return new Vector2(-1f, -1f);
            case DailyChallengeDirection.DownRight:
                return new Vector2(1f, -1f);
            default:
                return Vector2.zero;
        }
    }

    /// <summary>
    /// 安全 Trim。
    /// </summary>
    private static string SafeTrim(string value)
    {
        return value == null ? string.Empty : value.Trim();
    }

    /// <summary>
    /// 按不变文化解析浮点值。
    /// </summary>
    private static bool TryParseFloat(string value, out float result)
    {
        return float.TryParse(
            SafeTrim(value),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result);
    }

    /// <summary>
    /// 解析浮点值，失败时返回默认值。
    /// </summary>
    private static float ParseFloatOrDefault(string value, float defaultValue)
    {
        return TryParseFloat(value, out float parsedValue) ? parsedValue : defaultValue;
    }
}

/// <summary>
/// 每日一关解析后的关卡数据。
/// </summary>
public sealed class DailyChallengeParsedLevel
{
    /// <summary>
    /// 固定布局卡片列表。
    /// </summary>
    public List<DailyChallengeFixedCardRecord> FixedCards { get; } = new List<DailyChallengeFixedCardRecord>(64);

    /// <summary>
    /// 类型配置列表。
    /// </summary>
    public List<DailyChallengeTypeConfig> TypeConfigs { get; } = new List<DailyChallengeTypeConfig>(16);

    /// <summary>
    /// 方向任务列表。
    /// </summary>
    public List<DailyChallengeDirectionTask> DirectionTasks { get; } = new List<DailyChallengeDirectionTask>(8);
}

/// <summary>
/// 单张固定布局卡记录。
/// </summary>
public sealed class DailyChallengeFixedCardRecord
{
    /// <summary>
    /// 布局索引。
    /// </summary>
    public int LayoutIndex { get; }

    /// <summary>
    /// 行索引。
    /// </summary>
    public int RowIndex { get; }

    /// <summary>
    /// 列索引。
    /// </summary>
    public int ColIndex { get; }

    /// <summary>
    /// 层索引。
    /// </summary>
    public int LayerIndex { get; }

    /// <summary>
    /// 逻辑 X 偏移方向。
    /// </summary>
    public float OffsetX { get; }

    /// <summary>
    /// 逻辑 Y 偏移方向。
    /// </summary>
    public float OffsetY { get; }

    /// <summary>
    /// 偏移量。
    /// </summary>
    public float OffsetAmount { get; }

    public DailyChallengeFixedCardRecord(
        int layoutIndex,
        int rowIndex,
        int colIndex,
        int layerIndex,
        float offsetX,
        float offsetY,
        float offsetAmount)
    {
        LayoutIndex = layoutIndex;
        RowIndex = rowIndex;
        ColIndex = colIndex;
        LayerIndex = layerIndex;
        OffsetX = offsetX;
        OffsetY = offsetY;
        OffsetAmount = offsetAmount;
    }
}

/// <summary>
/// 卡片类型配置。
/// </summary>
public sealed class DailyChallengeTypeConfig
{
    /// <summary>
    /// 卡图名称。
    /// </summary>
    public string SpriteName { get; }

    /// <summary>
    /// 固定数量。
    /// </summary>
    public int FixedCount { get; }

    /// <summary>
    /// 权重/概率。
    /// </summary>
    public float Probability { get; }

    /// <summary>
    /// 是否是 '-1' 随机占位。
    /// </summary>
    public bool IsRandomPlaceholder => string.Equals(SpriteName, "-1", StringComparison.Ordinal);

    public DailyChallengeTypeConfig(string spriteName, int fixedCount, float probability)
    {
        SpriteName = spriteName;
        FixedCount = fixedCount;
        Probability = probability;
    }
}

/// <summary>
/// 方向任务数据。
/// </summary>
public sealed class DailyChallengeDirectionTask
{
    /// <summary>
    /// 方向。
    /// </summary>
    public DailyChallengeDirection Direction { get; }

    /// <summary>
    /// 数量。
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// 间距倍率。
    /// </summary>
    public float Spacing { get; }

    /// <summary>
    /// 是否带显式起始格坐标。
    /// </summary>
    public bool UseGridStart { get; }

    /// <summary>
    /// 起始行索引。
    /// </summary>
    public int StartRow { get; }

    /// <summary>
    /// 起始列索引。
    /// </summary>
    public int StartCol { get; }

    /// <summary>
    /// 起始层索引。
    /// </summary>
    public int StartLayer { get; }

    public DailyChallengeDirectionTask(
        DailyChallengeDirection direction,
        int count,
        float spacing,
        bool useGridStart,
        int startRow,
        int startCol,
        int startLayer)
    {
        Direction = direction;
        Count = count;
        Spacing = spacing;
        UseGridStart = useGridStart;
        StartRow = startRow;
        StartCol = startCol;
        StartLayer = startLayer;
    }
}

/// <summary>
/// 每日一关方向枚举。
/// </summary>
public enum DailyChallengeDirection
{
    /// <summary>
    /// 无方向。
    /// </summary>
    None = 0,

    /// <summary>
    /// 上。
    /// </summary>
    Up = 1,

    /// <summary>
    /// 下。
    /// </summary>
    Down = 2,

    /// <summary>
    /// 左。
    /// </summary>
    Left = 3,

    /// <summary>
    /// 右。
    /// </summary>
    Right = 4,

    /// <summary>
    /// 左上。
    /// </summary>
    UpLeft = 5,

    /// <summary>
    /// 右上。
    /// </summary>
    UpRight = 6,

    /// <summary>
    /// 左下。
    /// </summary>
    DownLeft = 7,

    /// <summary>
    /// 右下。
    /// </summary>
    DownRight = 8,
}
