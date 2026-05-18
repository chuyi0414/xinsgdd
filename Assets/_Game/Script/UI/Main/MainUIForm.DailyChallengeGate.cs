using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// MainUIForm 分部文件：每日一关解锁闸门子系统。
/// 职责：
///   1. 根据已解锁水果数量决定每日一关图标颜色（灰/白）。
///   2. 在 OnBtnDailyChallenge 中插入早退分支，未达阈值时弹 Toast 并拦截切页。
///   3. 通过订阅 CollectionUnlocksChanged 实现解锁数量变化时即时刷新，零轮询。
///
/// 设计约束（来自 AGENTS.md 与 design.md）：
///   - 零 GC：不使用 LINQ、闭包、字符串拼接、new 集合。
///   - 最小侵入：不修改现有字段/方法签名，仅追加生命周期调用与早退分支。
///   - 不修改 _btnDailyChallenge.interactable，保留 UpdateButtonState 原有语义。
/// </summary>
public partial class MainUIForm
{
    // ─── Inspector 序列化字段 ────────────────────────────────────────────────

    /// <summary>
    /// GoDailyChallenge 上用于表达"置暗/正常"视觉状态的 Image 组件。
    /// 必须在 Inspector 中手动拖入；运行时不做路径查找，缺失时仅输出一次 Warning。
    /// </summary>
    [SerializeField]
    private Image _imgDailyChallengeIcon;

    /// <summary>
    /// 锁定态图标颜色（灰色）。
    /// 默认值 (0.5, 0.5, 0.5, 1)，可在 Inspector 中覆盖。
    /// </summary>
    [SerializeField]
    private Color _lockedIconColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    /// <summary>
    /// 解锁态图标颜色（白色）。
    /// 默认值 (1, 1, 1, 1)，可在 Inspector 中覆盖。
    /// </summary>
    [SerializeField]
    private Color _unlockedIconColor = Color.white;

    // ─── 常量 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 进入每日一关所需的最小已解锁水果数量。
    /// 集中定义，避免魔法数字散落在多处。
    /// </summary>
    private const int UnlockThresholdFruitCount = 10;

    /// <summary>
    /// 锁定态点击时弹出的 Toast 文案。
    /// 使用字面量常量，不做任何字符串拼接，保证零 GC。
    /// </summary>
    private const string LockedToastMessage = "未解锁十种水果";

    // ─── 生命周期入口（由 MainUIForm.cs 的 OnOpen/OnClose/OnDestroy 调用）────

    /// <summary>
    /// 在 OnOpen 末尾调用：先退订（防重复挂接），再订阅 CollectionUnlocksChanged，
    /// 然后立即按当前解锁数刷新图标颜色。
    /// </summary>
    private void OpenDailyChallengeGate()
    {
        // 先 -= 再 +=，防止 OnOpen 被多次调用时重复挂接同一处理器。
        // 这是项目内统一的"防重复订阅"惯例，与 SubscribeManualCloudSaveResultEvents 保持一致。
        if (GameEntry.Fruits != null)
        {
            GameEntry.Fruits.CollectionUnlocksChanged -= OnCollectionUnlocksChangedForDailyChallengeGate;
            GameEntry.Fruits.CollectionUnlocksChanged += OnCollectionUnlocksChangedForDailyChallengeGate;
        }

        // 首次刷新：确保图标颜色与当前解锁数严格匹配，不依赖外部初始化顺序。
        RefreshDailyChallengeGateVisual(CountUnlockedFruits());
    }

    /// <summary>
    /// 在 OnClose 中（base.OnClose 之前）调用：退订 CollectionUnlocksChanged。
    /// 界面关闭后不再响应解锁变化，避免无效刷新与悬挂引用。
    /// </summary>
    private void CloseDailyChallengeGate()
    {
        if (GameEntry.Fruits != null)
        {
            GameEntry.Fruits.CollectionUnlocksChanged -= OnCollectionUnlocksChangedForDailyChallengeGate;
        }
    }

    /// <summary>
    /// 在 OnDestroy 末尾调用：再次退订，作为双保险。
    /// 防止 OnClose 未被调用（如直接销毁 GameObject）时出现悬挂引用。
    /// </summary>
    private void DestroyDailyChallengeGate()
    {
        if (GameEntry.Fruits != null)
        {
            GameEntry.Fruits.CollectionUnlocksChanged -= OnCollectionUnlocksChangedForDailyChallengeGate;
        }
    }

    // ─── 事件处理器 ──────────────────────────────────────────────────────────

    /// <summary>
    /// CollectionUnlocksChanged 事件处理器。
    /// 每当玩家解锁新水果/宠物/产出物时触发，重新计算解锁数并刷新图标颜色。
    /// 不在 OnUpdate 中轮询，完全事件驱动，零额外帧开销。
    /// </summary>
    private void OnCollectionUnlocksChangedForDailyChallengeGate()
    {
        RefreshDailyChallengeGateVisual(CountUnlockedFruits());
    }

    // ─── 核心逻辑方法 ────────────────────────────────────────────────────────

    /// <summary>
    /// 统计当前已解锁水果数量。
    /// 解锁语义（并集）：row.IsUnlocked（数据表默认解锁）|| IsFruitUnlocked(row.Code)（运行时购买解锁）。
    /// 与 ShuiGuoTJUIForm.RefreshItem 中的判定逻辑保持一致。
    ///
    /// 零 GC 保证：
    ///   - 使用 for 循环遍历数组，不调用 LINQ/ToList/Count(predicate)。
    ///   - 不分配任何中间集合。
    ///   - GameEntry.Fruits/DataTables 为 null 时提前返回 0，不抛异常。
    /// </summary>
    /// <returns>当前已解锁水果总数；任何前置条件不满足时返回 0。</returns>
    private int CountUnlockedFruits()
    {
        // 前置守卫：DataTables 未就绪时视为 0 个解锁，按锁定态处理。
        if (GameEntry.DataTables == null)
        {
            return 0;
        }

        FruitDataRow[] rows = GameEntry.DataTables.GetAllDataRows<FruitDataRow>();

        // GetAllDataRows 返回 null 时视为空数组，返回 0。
        if (rows == null)
        {
            return 0;
        }

        int count = 0;

        for (int i = 0; i < rows.Length; i++)
        {
            FruitDataRow row = rows[i];

            // 跳过空行（数据表解析异常时可能出现）。
            if (row == null)
            {
                continue;
            }

            // 跳过 Code 为 null/空白的行（数据表配置错误兜底）。
            // 注意：string.IsNullOrEmpty 比 IsNullOrWhiteSpace 更轻量，
            // 且 FruitDataRow.ParseDataRow 已保证合法行的 Code 不含空白，此处用 IsNullOrEmpty 即可。
            if (string.IsNullOrEmpty(row.Code))
            {
                continue;
            }

            // 并集语义：数据表默认解锁 OR 运行时购买解锁，满足任一即计入。
            // GameEntry.Fruits 为 null 时退化为仅统计 IsUnlocked，不抛异常。
            bool isUnlocked = row.IsUnlocked
                || (GameEntry.Fruits != null && GameEntry.Fruits.IsFruitUnlocked(row.Code));

            if (isUnlocked)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 根据解锁数量刷新每日一关图标颜色。
    /// 幂等性保证：若目标颜色与当前颜色相同则跳过赋值，避免无效 dirty mark。
    /// </summary>
    /// <param name="unlockedCount">当前已解锁水果数量，由 CountUnlockedFruits() 提供。</param>
    private void RefreshDailyChallengeGateVisual(int unlockedCount)
    {
        // _imgDailyChallengeIcon 未在 Inspector 中绑定时，输出一次性 Warning 并跳过。
        // 不阻断点击流程（已解锁时仍可正常切页）。
        if (_imgDailyChallengeIcon == null)
        {
            Log.Warning("MainUIForm 缺少 _imgDailyChallengeIcon 引用，请在 Inspector 中绑定 GoDailyChallenge 上的 Image 组件。");
            return;
        }

        // 根据阈值决定目标颜色：< 10 → 灰色（锁定），>= 10 → 白色（解锁）。
        Color targetColor = unlockedCount < UnlockThresholdFruitCount
            ? _lockedIconColor
            : _unlockedIconColor;

        // 跳过相同颜色赋值，保证幂等性，同时避免触发 UGUI 的 dirty mark。
        // Color 是值类型，== 运算符逐分量比较，无 GC。
        if (_imgDailyChallengeIcon.color == targetColor)
        {
            return;
        }

        _imgDailyChallengeIcon.color = targetColor;
    }

    /// <summary>
    /// 判断每日一关当前是否处于锁定态。
    /// 在 OnBtnDailyChallenge 的早退分支中调用，决定是否拦截切页。
    /// </summary>
    /// <returns>true 表示锁定（解锁数 &lt; 阈值）；false 表示已解锁。</returns>
    private bool IsDailyChallengeGateLocked()
    {
        return CountUnlockedFruits() < UnlockThresholdFruitCount;
    }
}
