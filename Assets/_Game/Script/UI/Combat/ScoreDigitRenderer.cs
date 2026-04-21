using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 精灵数字渲染器。
/// 挂到 Scores 节点上，根据分数值动态生成/复用数字 Image 并自适应居中排列。
/// 
/// 布局规则：
/// - 奇数位数（1、3、5…）：最中间的数字在 x=0，其余向左右对称展开；
/// - 偶数位数（2、4、6…）：从 x=0 开始左右交替排列。
///   例：20 → 2 在 -digitWidth，0 在 +digitWidth；
///       1  → 1 在 0；
///       123 → 2 在 0，1 在 -digitWidth，3 在 +digitWidth。
/// 
/// digitWidth 取自模板 Image（_templateImage）的 RectTransform 宽度，
/// 修改模板大小即可自适应所有数字的间距。
/// 
/// 数字精灵由 GameAssetModule 在启动阶段预加载并缓存，
/// 本组件通过 GameEntry.GameAssets.TryGetScoreDigitSprite 读取，
/// 不再使用 Resources.Load。
/// </summary>
public sealed class ScoreDigitRenderer : MonoBehaviour
{
    /// <summary>
    /// 模板 Image：用于确定单个数字的尺寸和初始精灵。
    /// Inspector 中拖入 Scores 下的 Image 子物体。
    /// 该物体同时作为"0"位数字使用，不会被销毁。
    /// </summary>
    [SerializeField]
    private Image _templateImage;

    /// <summary>
    /// 当前已创建的数字 Image 列表（含模板）。
    /// 分数位数增长时追加新 Image，位数减少时隐藏多余 Image。
    /// </summary>
    private readonly List<Image> _digitImages = new List<Image>(8);

    /// <summary>
    /// 当前显示的分数值。
    /// 用于避免重复设置相同分数导致的冗余刷新。
    /// -1 表示尚未初始化。
    /// </summary>
    private int _currentDisplayScore = -1;

    /// <summary>
    /// 单个数字的宽度（像素）。
    /// 从模板 Image 的 RectTransform.rect.width 读取，OnEnable 时缓存。
    /// </summary>
    private float _digitWidth;

    private void OnEnable()
    {
        // 缓存模板尺寸
        if (_templateImage != null)
        {
            _digitWidth = _templateImage.rectTransform.rect.width;

            // 模板 Image 作为第 0 个数字位
            if (_digitImages.Count == 0)
            {
                _digitImages.Add(_templateImage);
            }
        }

        // 初始化显示 0
        SetScore(0);
    }

    private void OnDestroy()
    {
        // 清理动态创建的数字 Image（模板由 Unity 销毁）
        for (int i = _digitImages.Count - 1; i >= 1; i--)
        {
            if (_digitImages[i] != null)
            {
                Destroy(_digitImages[i].gameObject);
            }
        }

        _digitImages.Clear();
    }

    /// <summary>
    /// 设置显示的分数。
    /// 内部做脏标记，相同分数不重复刷新。
    /// </summary>
    /// <param name="score">要显示的分数（≥0）。</param>
    public void SetScore(int score)
    {
        if (score < 0)
        {
            score = 0;
        }

        if (_currentDisplayScore == score)
        {
            return;
        }

        _currentDisplayScore = score;
        RefreshDigits(score);
    }

    /// <summary>
    /// 强制刷新显示（忽略脏标记）。
    /// 用于模板尺寸变化后强制重排。
    /// </summary>
    /// <param name="score">要显示的分数。</param>
    public void ForceRefresh(int score)
    {
        _currentDisplayScore = -1;

        // 重新缓存模板尺寸
        if (_templateImage != null)
        {
            _digitWidth = _templateImage.rectTransform.rect.width;
        }

        SetScore(score);
    }

    /// <summary>
    /// 刷新数字精灵和位置。
    /// </summary>
    /// <param name="score">当前分数。</param>
    private void RefreshDigits(int score)
    {
        // 先算出当前分数一共需要几位数字，后续据此决定要激活多少个 Image。
        int digitCount = GetDigitCount(score);

        // 确保 Image 数量足够
        EnsureDigitImageCount(digitCount);

        // 设置每个数字的精灵和位置
        LayoutDigits(score, digitCount);
    }

    /// <summary>
    /// 计算当前分数的十进制位数。
    /// 使用整数除法逐位缩小，避免通过 ToString 取位时产生额外字符串分配。
    /// </summary>
    /// <param name="score">当前分数。</param>
    /// <returns>该分数对应的位数，最少返回 1。</returns>
    private static int GetDigitCount(int score)
    {
        int digitCount = 1;
        while (score >= 10)
        {
            // 每除以 10 一次，就表示去掉了最低一位，因此位数 +1。
            score /= 10;
            digitCount++;
        }

        return digitCount;
    }

    /// <summary>
    /// 确保数字 Image 数量 ≥ digitCount。
    /// 不足时以模板 Image 为蓝本 Instantiate 新 Image。
    /// </summary>
    /// <param name="digitCount">需要的数字位数。</param>
    private void EnsureDigitImageCount(int digitCount)
    {
        while (_digitImages.Count < digitCount)
        {
            // 以模板 Image 为蓝本克隆
            // ⚠️ 避坑：Instantiate 后必须设 parent，否则层级混乱。
            Image newImage = Instantiate(_templateImage, _templateImage.transform.parent);
            newImage.gameObject.name = "Digit";
            newImage.gameObject.SetActive(true);
            _digitImages.Add(newImage);
        }

        // 隐藏多余的 Image
        for (int i = digitCount; i < _digitImages.Count; i++)
        {
            if (_digitImages[i] != null)
            {
                _digitImages[i].gameObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// 设置每个数字的精灵并计算居中位置。
    /// 
    /// 布局算法：
    /// - 奇数位：中心位在 x=0，左右对称 ±digitWidth
    ///   例 3 位：[-W, 0, +W]
    /// - 偶数位：从中心两侧开始，间距 digitWidth
    ///   例 2 位：[-W/2, +W/2]
    ///   例 4 位：[-3W/2, -W/2, +W/2, +3W/2]
    /// 
    /// 统一公式：position[i] = (i - (digitCount - 1) / 2f) * digitWidth
    /// </summary>
    /// <param name="score">分数值。</param>
    /// <param name="digitCount">位数。</param>
    private void LayoutDigits(int score, int digitCount)
    {
        // ⚠️ 避坑：中心偏移量用浮点计算，奇数位时 (digitCount-1)/2 恰好为整数，
        // 中心位 x=0；偶数位时中心两位分别在 ±digitWidth/2。
        float centerOffset = (digitCount - 1) * 0.5f;

        // remainingScore 会在循环里不断去掉最低位，配合从右往左遍历即可拿到正确数字顺序。
        int remainingScore = score;

        for (int i = digitCount - 1; i >= 0; i--)
        {
            Image img = _digitImages[i];
            if (img == null)
            {
                continue;
            }

            // 设置精灵：从 GameAssetModule 预加载缓存读取
            // 先取当前最低位，再把 remainingScore 缩小到下一位，整个过程不产生字符串 GC。
            int digit = remainingScore % 10;
            remainingScore /= 10;
            if (digit >= 0 && digit <= 9
                && GameEntry.GameAssets != null
                && GameEntry.GameAssets.TryGetScoreDigitSprite(digit, out Sprite digitSprite)
                && digitSprite != null)
            {
                img.sprite = digitSprite;
            }

            // 计算位置：以 Scores 容器中心为原点
            float xPos = (i - centerOffset) * _digitWidth;
            img.rectTransform.anchoredPosition = new Vector2(xPos, 0f);

            // 确保可见
            img.gameObject.SetActive(true);
        }
    }
}
