using GameFramework.Sound;
using UnityGameFramework.Runtime;

/// <summary>
/// UI 交互音效统一入口。
/// 负责播放所有 UI 层面的交互音效（按钮点击、页签切换、面板展开/关闭、通用确认提示等），
/// 并在首次调用时自动补建声音组，无需在 prefab 上预配 SoundGroup。
/// </summary>
public static class UIInteractionSound
{
    /// <summary>
    /// UI 交互音效所属的声音组名称。
    /// 所有 UI 交互音效统一归入此组，便于后续独立控制音量、静音等。
    /// </summary>
    public const string SoundGroupName = "UI";

    /// <summary>
    /// 背景音乐所属的声音组名称。
    /// 与 UI 音效组分离，便于独立控制 BGM 音量、静音、暂停等。
    /// </summary>
    public const string BgmSoundGroupName = "BGM";

    /// <summary>
    /// 默认点击音效资源名。
    /// 基于当前工程的资源命名规则：使用 Assets/_Game/Resources 下的相对路径，不带扩展名。
    /// 适用于绝大多数点击、确认类交互场景。
    /// </summary>
    private const string DefaultClickSoundAssetName = "Musics/button-394464";

    /// <summary>
    /// 默认背景音乐资源名。
    /// 对应 Assets/_Game/Resources/Musics/BackgroundMusic/music1.mp3。
    /// </summary>
    private const string DefaultBgmAssetName = "Musics/BackgroundMusic/music1";

    /// <summary>
    /// UI 音效组运行时动态补建时创建的 Sound Agent 数量。
    /// </summary>
    private const int SoundAgentHelperCount = 10;

    /// <summary>
    /// BGM 声音组的 Sound Agent 数量。
    /// 背景音乐同时只播放一条，设为 1 即可。
    /// </summary>
    private const int BgmAgentHelperCount = 1;

    /// <summary>
    /// 特效音效所属的声音组名称。
    /// 与 UI 点击音效、BGM 分离，便于独立控制特效音量、静音等。
    /// </summary>
    public const string EffectSoundGroupName = "Effect";

    /// <summary>
    /// 消除音效资源名。
    /// 对应 Assets/_Game/Resources/Musics/Eliminate/EliminateAudio.wav。
    /// </summary>
    private const string EliminateSoundAssetName = "Musics/Eliminate/EliminateAudio";

    /// <summary>
    /// 特效音效组的 Sound Agent 数量。
    /// 消除等特效音效同时播放数较少，3 个 Agent 足够。
    /// </summary>
    private const int EffectAgentHelperCount = 3;

    /// <summary>
    /// 播放默认的 UI 点击音效。
    /// 适用于按钮点击、页签切换、通用确认等绝大多数点击场景，无需指定资源名。
    /// </summary>
    public static void PlayClick()
    {
        PlayClick(DefaultClickSoundAssetName);
    }

    /// <summary>
    /// 播放指定的 UI 点击音效。
    /// 当后续需要区分不同点击音效时，可通过此重载传入不同的资源名。
    /// </summary>
    /// <param name="soundAssetName">
    /// 音效资源名，遵循工程约定：Assets/_Game/Resources 下的相对路径，不带扩展名。
    /// 例如 "Musics/button-394464"、"Musics/tab-switch"、"Musics/panel-close"。
    /// </param>
    public static void PlayClick(string soundAssetName)
    {
        // 安全短路：SoundComponent 未就绪时静默返回，不抛异常，不阻断业务流程。
        SoundComponent soundComponent = global::GameEntry.Sound;
        if (soundComponent == null)
        {
            return;
        }

        // 声音组已在 InitializeSoundSystem 中预加载，此处直接播放。
        soundComponent.PlaySound(soundAssetName, SoundGroupName);
    }

    /// <summary>
    /// 初始化声音系统：预加载全部声音组并播放默认背景音乐。
    /// 此方法非懒加载：直接创建所有声音组（UI / BGM / Effect）并立即播放 BGM，
    /// 确保游戏启动时背景音乐最优先响起，且后续音效调用无需补建。
    /// 应在 GameEntry.InitBuiltinComponents 之后、InitCustomComponents 之前调用。
    /// </summary>
    public static void InitializeSoundSystem()
    {
        InitializeSoundSystem(DefaultBgmAssetName);
    }

    /// <summary>
    /// 初始化声音系统：预加载全部声音组并播放指定背景音乐。
    /// 非懒加载：无条件创建所有声音组，设置循环参数后立即播放 BGM。
    /// </summary>
    /// <param name="bgmAssetName">
    /// 背景音乐资源名，遵循工程约定：Assets/_Game/Resources 下的相对路径，不带扩展名。
    /// </param>
    public static void InitializeSoundSystem(string bgmAssetName)
    {
        SoundComponent soundComponent = global::GameEntry.Sound;
        if (soundComponent == null)
        {
            return;
        }

        // ── 预加载全部声音组 ──
        // 非懒加载：直接创建，不走 HasSoundGroup 判断，确保第一时间全部可用。
        soundComponent.AddSoundGroup(SoundGroupName, SoundAgentHelperCount);
        soundComponent.AddSoundGroup(BgmSoundGroupName, BgmAgentHelperCount);
        soundComponent.AddSoundGroup(EffectSoundGroupName, EffectAgentHelperCount);

        // ── 播放背景音乐（循环） ──
        PlaySoundParams playSoundParams = PlaySoundParams.Create();
        playSoundParams.Loop = true;

        soundComponent.PlaySound(bgmAssetName, BgmSoundGroupName, playSoundParams);

        ApplySavedSoundSettings();
    }

    /// <summary>
    /// 应用玩家本地保存的声音开关设置。
    /// 在 InitializeSoundSystem 之后调用，确保启动时即恢复上次的 BGM / 音效静音状态。
    /// </summary>
    private static void ApplySavedSoundSettings()
    {
        SoundComponent soundComponent = global::GameEntry.Sound;
        if (soundComponent == null)
        {
            return;
        }

        // ── 背景音乐 ──
        bool bgmEnabled = PlayerPrefs.GetInt("Setting_BgmEnabled", 1) != 0;
        ISoundGroup bgmGroup = soundComponent.GetSoundGroup(BgmSoundGroupName);
        if (bgmGroup != null)
        {
            bgmGroup.Mute = !bgmEnabled;
        }

        // ── 音效（UI + Effect 两组统一控制） ──
        bool sfxEnabled = PlayerPrefs.GetInt("Setting_SfxEnabled", 1) != 0;
        ISoundGroup uiGroup = soundComponent.GetSoundGroup(SoundGroupName);
        if (uiGroup != null)
        {
            uiGroup.Mute = !sfxEnabled;
        }

        ISoundGroup effectGroup = soundComponent.GetSoundGroup(EffectSoundGroupName);
        if (effectGroup != null)
        {
            effectGroup.Mute = !sfxEnabled;
        }
    }

    /// <summary>
    /// 播放消除音效。
    /// 满格结算卡片缩小完毕后调用，与消除特效动画同步触发。
    /// </summary>
    public static void PlayEliminateSound()
    {
        SoundComponent soundComponent = global::GameEntry.Sound;
        if (soundComponent == null)
        {
            return;
        }

        // 声音组已在 InitializeSoundSystem 中预加载，此处直接播放。
        soundComponent.PlaySound(EliminateSoundAssetName, EffectSoundGroupName);
    }
}
