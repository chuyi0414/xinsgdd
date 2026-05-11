using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 每日一关排行榜条目视图。
/// 字段全部由 Inspector 手动拖入，不在运行时按节点名查找。
/// </summary>
public sealed class DailyChallengeLeaderboardItemView : MonoBehaviour
{
    /// <summary>
    /// 名次文本。
    /// 初始状态由预制体决定，运行时刷新为具体名次或“未上榜”。
    /// </summary>
    [SerializeField]
    private TMP_Text _txtRanking;

    /// <summary>
    /// 玩家头像 RawImage。
    /// 初始状态保留预制体默认头像，资源缓存命中后替换为玩家头像纹理。
    /// </summary>
    [SerializeField]
    private RawImage _rawImageAvatar;

    /// <summary>
    /// 玩家头像框 Image。
    /// 初始状态保留预制体默认头像框，资源缓存命中后替换为玩家头像框精灵。
    /// </summary>
    [SerializeField]
    private Image _imageAvatarFrame;

    /// <summary>
    /// 玩家昵称文本。
    /// 初始状态由预制体决定，运行时刷新为排行榜返回的玩家名。
    /// </summary>
    [SerializeField]
    private TMP_Text _txtName;

    /// <summary>
    /// 玩家得分文本。
    /// 初始状态由预制体决定，运行时刷新为今日最高分。
    /// </summary>
    [SerializeField]
    private TMP_Text _txtScore;

    /// <summary>
    /// 预制体默认头像纹理缓存。
    /// 首次刷新头像前记录，用于头像资源未命中时回退。
    /// </summary>
    private Texture _defaultAvatarTexture;

    /// <summary>
    /// 预制体默认头像框精灵缓存。
    /// 首次刷新头像框前记录，用于头像框资源未命中时回退。
    /// </summary>
    private Sprite _defaultAvatarFrameSprite;

    /// <summary>
    /// 是否已经缓存过默认视觉资源。
    /// 初始为 false，首次刷新条目时设置为 true。
    /// </summary>
    private bool _hasCachedDefaultVisuals;

    /// <summary>
    /// 设置条目根节点显隐。
    /// </summary>
    /// <param name="active">是否显示该条目。</param>
    public void SetActive(bool active)
    {
        gameObject.SetActive(active);
    }

    /// <summary>
    /// 刷新完整条目数据。
    /// </summary>
    /// <param name="rankText">名次文案。</param>
    /// <param name="playerName">玩家昵称。</param>
    /// <param name="score">玩家得分。</param>
    /// <param name="headPortraitCode">头像 Code。</param>
    /// <param name="headPortraitFrameCode">头像框 Code。</param>
    public void Refresh(string rankText, string playerName, int score, string headPortraitCode, string headPortraitFrameCode)
    {
        EnsureDefaultVisualsCached();
        if (_txtRanking != null)
        {
            _txtRanking.text = rankText ?? string.Empty;
        }

        if (_txtName != null)
        {
            _txtName.text = playerName ?? string.Empty;
        }

        if (_txtScore != null)
        {
            _txtScore.text = Mathf.Max(0, score).ToString();
        }

        RefreshAvatar(headPortraitCode);
        RefreshAvatarFrame(headPortraitFrameCode);
    }

    /// <summary>
    /// 缓存预制体默认视觉资源。
    /// </summary>
    private void EnsureDefaultVisualsCached()
    {
        if (_hasCachedDefaultVisuals)
        {
            return;
        }

        _defaultAvatarTexture = _rawImageAvatar != null ? _rawImageAvatar.texture : null;
        _defaultAvatarFrameSprite = _imageAvatarFrame != null ? _imageAvatarFrame.sprite : null;
        _hasCachedDefaultVisuals = true;
    }

    /// <summary>
    /// 刷新头像图片。
    /// </summary>
    /// <param name="headPortraitCode">头像 Code。</param>
    private void RefreshAvatar(string headPortraitCode)
    {
        if (_rawImageAvatar == null)
        {
            return;
        }

        HeadPortraitDataRow row = !string.IsNullOrWhiteSpace(headPortraitCode) && GameEntry.DataTables != null
            ? GameEntry.DataTables.GetDataRowByCode<HeadPortraitDataRow>(headPortraitCode)
            : null;
        if (row != null && GameEntry.GameAssets != null && GameEntry.GameAssets.TryGetHeadPortraitSprite(row.IconPath, out Sprite sprite) && sprite != null)
        {
            _rawImageAvatar.texture = sprite.texture;
            return;
        }

        _rawImageAvatar.texture = _defaultAvatarTexture;
    }

    /// <summary>
    /// 刷新头像框图片。
    /// </summary>
    /// <param name="headPortraitFrameCode">头像框 Code。</param>
    private void RefreshAvatarFrame(string headPortraitFrameCode)
    {
        if (_imageAvatarFrame == null)
        {
            return;
        }

        HeadPortraitFrameDataRow row = !string.IsNullOrWhiteSpace(headPortraitFrameCode) && GameEntry.DataTables != null
            ? GameEntry.DataTables.GetDataRowByCode<HeadPortraitFrameDataRow>(headPortraitFrameCode)
            : null;
        if (row != null && GameEntry.GameAssets != null && GameEntry.GameAssets.TryGetHeadPortraitFrameSprite(row.IconPath, out Sprite sprite) && sprite != null)
        {
            _imageAvatarFrame.sprite = sprite;
            return;
        }

        _imageAvatarFrame.sprite = _defaultAvatarFrameSprite;
    }
}
