using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 每日一关界面。
/// 负责每日关入口、今日榜拉取、前 100 条排行榜展示和当前玩家榜单信息展示。
/// </summary>
public sealed class DailyChallengeUIForm : UIFormLogic
{
    /// <summary>
    /// 当前临时预览使用的本地关卡资源路径。
    /// 这里先固定到迁移进来的首份测试关卡，便于快速验证生成链路。
    /// </summary>
    private const string PreviewLevelAssetPath = "Configs/Levels/bbl1";

    /// <summary>
    /// 每日排行榜最大展示数量。
    /// 与服务端 `leaderboardTopLimit` 保持一致，避免 Content 中生成超出需求的条目。
    /// </summary>
    private const int LeaderboardCapacity = 100;

    /// <summary>
    /// 未进入前 100 时的名次文案。
    /// </summary>
    private const string NotRankedText = "未上榜";

    /// <summary>
    /// 开始每日一关按钮。
    /// 初始状态由预制体决定，运行时只绑定点击事件。
    /// </summary>
    [SerializeField]
    private Button _btnStartLevel;

    /// <summary>
    /// 今日最高分文本。
    /// 由 Inspector 手动拖入，不在运行时按节点名查找。
    /// </summary>
    [SerializeField]
    private TMP_Text _txtTodayBestScore;

    /// <summary>
    /// 历史最高分文本。
    /// 由 Inspector 手动拖入，不在运行时按节点名查找。
    /// </summary>
    [SerializeField]
    private TMP_Text _txtHistoricalBestScore;

    /// <summary>
    /// 排行榜 Content 节点。
    /// 由 Inspector 手动拖入，运行时只把条目模板克隆到该节点下。
    /// </summary>
    [SerializeField]
    private Transform _leaderboardContent;

    /// <summary>
    /// 排行榜条目模板。
    /// 模板根节点需要挂 DailyChallengeLeaderboardItemView，并由 Inspector 手动拖入内部字段。
    /// 运行时仅作为克隆源，不加入对象池，也不会被显示成排行榜数据行。
    /// </summary>
    [SerializeField]
    private DailyChallengeLeaderboardItemView _leaderboardItemTemplate;

    /// <summary>
    /// 当前玩家排行榜条目。
    /// 对应 GoMy，根节点需要挂 DailyChallengeLeaderboardItemView，并由 Inspector 手动拖入内部字段。
    /// </summary>
    [SerializeField]
    private DailyChallengeLeaderboardItemView _myItemView;

    /// <summary>
    /// 排行榜条目对象池。
    /// 打开界面后最多创建 100 个克隆条目，后续刷新只切换显隐和文本。
    /// 该池不包含隐藏模板本体。
    /// </summary>
    private readonly List<DailyChallengeLeaderboardItemView> _leaderboardItemViews = new List<DailyChallengeLeaderboardItemView>(LeaderboardCapacity);

    /// <summary>
    /// 排行榜请求序号。
    /// 每次打开或关闭都会递增，用于丢弃已经过期的异步回调。
    /// </summary>
    private int _leaderboardRequestSerial;

    /// <summary>
    /// 初始化界面引用并绑定按钮事件。
    /// </summary>
    /// <param name="userData">界面打开附加参数。</param>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        BindButtonEvents();
        EnsureLeaderboardItemPool();
    }

    /// <summary>
    /// 页面打开时恢复主界面返回按钮，并刷新每日排行榜。
    /// </summary>
    /// <param name="userData">界面打开附加参数。</param>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        MainUIForm mainUIForm = ResolveMainUIForm();
        if (mainUIForm != null)
        {
            mainUIForm.SetBtnUpVisible(true);
        }

        RefreshLeaderboard();
    }

    /// <summary>
    /// 界面关闭时递增请求序号，阻止已关闭界面的异步回调继续刷新 UI。
    /// </summary>
    /// <param name="isShutdown">是否为关闭界面管理器时触发。</param>
    /// <param name="userData">界面关闭附加参数。</param>
    protected override void OnClose(bool isShutdown, object userData)
    {
        _leaderboardRequestSerial++;
        base.OnClose(isShutdown, userData);
    }

    /// <summary>
    /// 绑定按钮点击事件。
    /// 为了防止重复绑定，先移除再添加。
    /// </summary>
    private void BindButtonEvents()
    {
        if (_btnStartLevel == null)
        {
            return;
        }

        _btnStartLevel.onClick.RemoveListener(OnBtnStartLevel);
        _btnStartLevel.onClick.AddListener(OnBtnStartLevel);
    }

    /// <summary>
    /// “开始/刷新”按钮点击回调。
    /// 隐藏 MainUIForm 的 BtnUp → 关闭自身 → 设置 TransitionToCombat 标记 → 切换到 CombatProcedure。
    /// </summary>
    private void OnBtnStartLevel()
    {
        UIInteractionSound.PlayClick();
        if (GameEntry.UI != null)
        {
            GameEntry.UI.OpenUIForm(UIFormDefine.LoadingUIForm, UIFormDefine.LoadingGroup);
        }

        MainUIForm mainUIForm = ResolveMainUIForm();
        if (mainUIForm == null)
        {
            CloseLoadingUIForm();
            WriteFailureText("主界面未打开，无法启动每日一关。");
            return;
        }

        if (!mainUIForm.TryStartDailyChallengePreviewFromUIForm(PreviewLevelAssetPath))
        {
            CloseLoadingUIForm();
            WriteFailureText("关卡加载失败，请检查资源或日志。");
            return;
        }

        mainUIForm.SetBtnUpVisible(false);
        if (UIForm != null && GameEntry.UI != null)
        {
            GameEntry.UI.CloseUIForm(UIForm.SerialId);
        }

        GameFramework.Procedure.ProcedureBase currentProcedure = GameEntry.Procedure.CurrentProcedure;
        currentProcedure.procedureOwner.SetData<VarInt32>(MainProcedure.TransitionToCombatDataName, 1);
        currentProcedure.ChangeState<CombatProcedure>(currentProcedure.procedureOwner);
    }

    /// <summary>
    /// 刷新每日一关排行榜。
    /// </summary>
    private void RefreshLeaderboard()
    {
        int requestSerial = ++_leaderboardRequestSerial;
        int localHistoricalBestScore = GameEntry.Fruits != null ? GameEntry.Fruits.DailyChallengeHistoricalBestScore : 0;
        SetScoreHeader(0, localHistoricalBestScore);
        SetLeaderboardItemsActive(0);
        RefreshMyEntry(null, 0, localHistoricalBestScore);

        if (GameEntry.DailyChallengeLeaderboard == null)
        {
            WriteFailureText("排行榜模块未初始化");
            return;
        }

        GameEntry.DailyChallengeLeaderboard.LoadTodayLeaderboard(
            response =>
            {
                if (requestSerial != _leaderboardRequestSerial)
                {
                    return;
                }

                ApplyLeaderboardResponse(response);
            },
            errorMessage =>
            {
                if (requestSerial != _leaderboardRequestSerial)
                {
                    return;
                }

                Log.Warning("DailyChallengeUIForm 刷新排行榜失败：{0}", errorMessage);
                WriteFailureText("排行榜加载失败");
            });
    }

    /// <summary>
    /// 应用排行榜响应数据。
    /// </summary>
    /// <param name="response">服务端返回的排行榜数据。</param>
    private void ApplyLeaderboardResponse(DailyChallengeLeaderboardResponse response)
    {
        if (response == null)
        {
            WriteFailureText("排行榜数据为空");
            return;
        }

        SetScoreHeader(response.todayBestScore, response.historicalBestScore);
        DailyChallengeLeaderboardEntry[] entries = response.entries ?? Array.Empty<DailyChallengeLeaderboardEntry>();
        int visibleCount = Mathf.Min(entries.Length, _leaderboardItemViews.Count);
        for (int i = 0; i < visibleCount; i++)
        {
            _leaderboardItemViews[i].SetActive(true);
            BindEntry(_leaderboardItemViews[i], entries[i], entries[i] != null ? entries[i].rank : i + 1);
        }

        SetLeaderboardItemsActive(visibleCount);
        RefreshMyEntry(response.myEntry, response.todayBestScore, response.historicalBestScore);
    }

    /// <summary>
    /// 刷新当前玩家排行榜条目。
    /// </summary>
    /// <param name="entry">当前玩家今日记录。</param>
    /// <param name="todayBestScore">今日最高分。</param>
    /// <param name="historicalBestScore">历史最高分。</param>
    private void RefreshMyEntry(DailyChallengeLeaderboardEntry entry, int todayBestScore, int historicalBestScore)
    {
        if (_myItemView == null)
        {
            return;
        }

        DailyChallengeLeaderboardEntry displayEntry = new DailyChallengeLeaderboardEntry
        {
            rank = entry != null ? entry.rank : 0,
            playerName = GetLocalPlayerDisplayName(),
            headPortraitCode = GameEntry.Fruits != null ? GameEntry.Fruits.SelectedHeadPortraitCode ?? string.Empty : string.Empty,
            headPortraitFrameCode = GameEntry.Fruits != null ? GameEntry.Fruits.SelectedHeadPortraitFrameCode ?? string.Empty : string.Empty,
            score = entry != null ? Mathf.Max(0, entry.score) : Mathf.Max(0, todayBestScore),
            scoreAchievedAt = entry != null ? entry.scoreAchievedAt ?? string.Empty : string.Empty
        };

        BindEntry(_myItemView, displayEntry, displayEntry.rank);
        SetScoreHeader(Mathf.Max(0, todayBestScore), Mathf.Max(0, historicalBestScore));
    }

    /// <summary>
    /// 获取当前玩家本地显示名。
    /// 当今日还没有榜单记录、服务端 myEntry 为空时，GoMy 仍要显示云存档已经下发的玩家昵称。
    /// </summary>
    /// <returns>玩家显示名；运行时名字为空时返回安全兜底文案。</returns>
    private static string GetLocalPlayerDisplayName()
    {
        string playerName = GameEntry.Fruits != null ? GameEntry.Fruits.PlayerName : string.Empty;
        return string.IsNullOrWhiteSpace(playerName) ? "玩家" : playerName;
    }

    /// <summary>
    /// 设置今日最高分和历史最高分文案。
    /// </summary>
    /// <param name="todayBestScore">今日最高分。</param>
    /// <param name="historicalBestScore">历史最高分。</param>
    private void SetScoreHeader(int todayBestScore, int historicalBestScore)
    {
        if (_txtTodayBestScore != null)
        {
            _txtTodayBestScore.text = $"今日最高分：{Mathf.Max(0, todayBestScore)}";
        }

        if (_txtHistoricalBestScore != null)
        {
            _txtHistoricalBestScore.text = $"历史最高分：{Mathf.Max(0, historicalBestScore)}";
        }
    }

    /// <summary>
    /// 绑定单个排行榜条目。
    /// </summary>
    /// <param name="view">目标条目视图。</param>
    /// <param name="entry">排行榜数据。</param>
    /// <param name="rank">当前名次。</param>
    private static void BindEntry(DailyChallengeLeaderboardItemView view, DailyChallengeLeaderboardEntry entry, int rank)
    {
        if (view == null)
        {
            return;
        }

        if (entry == null)
        {
            view.Refresh(NotRankedText, string.Empty, 0, string.Empty, string.Empty);
            return;
        }

        view.Refresh(
            rank > 0 ? rank.ToString() : NotRankedText,
            string.IsNullOrWhiteSpace(entry.playerName) ? "玩家" : entry.playerName,
            entry.score,
            entry.headPortraitCode,
            entry.headPortraitFrameCode);
    }

    /// <summary>
    /// 确保排行榜条目池已经创建。
    /// </summary>
    private void EnsureLeaderboardItemPool()
    {
        if (_leaderboardContent == null || _leaderboardItemTemplate == null)
        {
            return;
        }

        _leaderboardItemTemplate.SetActive(false);

        while (_leaderboardItemViews.Count < LeaderboardCapacity)
        {
            DailyChallengeLeaderboardItemView item = Instantiate(_leaderboardItemTemplate, _leaderboardContent, false);
            item.name = _leaderboardItemTemplate.name;
            item.SetActive(false);
            _leaderboardItemViews.Add(item);
        }

        SetLeaderboardItemsActive(0);
    }

    /// <summary>
    /// 设置排行榜对象池中前 N 个条目可见，其余隐藏。
    /// </summary>
    /// <param name="visibleCount">需要显示的条目数量。</param>
    private void SetLeaderboardItemsActive(int visibleCount)
    {
        for (int i = 0; i < _leaderboardItemViews.Count; i++)
        {
            _leaderboardItemViews[i].SetActive(i < visibleCount);
        }
    }

    /// <summary>
    /// 写入一条失败提示文本。
    /// </summary>
    /// <param name="message">失败原因。</param>
    private void WriteFailureText(string message)
    {
        if (_txtTodayBestScore != null)
        {
            _txtTodayBestScore.text = "今日最高分：加载失败";
        }

        if (_txtHistoricalBestScore != null)
        {
            _txtHistoricalBestScore.text = message;
        }
    }

    /// <summary>
    /// 关闭当前已打开的 LoadingUIForm。
    /// 棋盘生成失败时调用，避免加载遮罩残留在屏幕上。
    /// </summary>
    private static void CloseLoadingUIForm()
    {
        if (GameEntry.UI == null)
        {
            return;
        }

        UIForm loadingUI = GameEntry.UI.GetUIForm(UIFormDefine.LoadingUIForm);
        if (loadingUI != null)
        {
            GameEntry.UI.CloseUIForm(loadingUI.SerialId);
        }
    }

    /// <summary>
    /// 获取当前已打开的 MainUIForm 逻辑对象。
    /// </summary>
    /// <returns>主界面逻辑对象；不存在时返回 null。</returns>
    private static MainUIForm ResolveMainUIForm()
    {
        if (GameEntry.UI == null)
        {
            return null;
        }

        UIForm mainUI = GameEntry.UI.GetUIForm(UIFormDefine.MainUIForm);
        return mainUI != null ? mainUI.Logic as MainUIForm : null;
    }
}
