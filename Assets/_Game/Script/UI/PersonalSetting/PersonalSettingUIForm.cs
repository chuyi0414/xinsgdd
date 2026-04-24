using GameFramework;
using GameFramework.Sound;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 个人设置界面。
/// 负责展示设置选项，目前仅提供关闭按钮。
/// </summary>
public sealed class PersonalSettingUIForm : UIFormLogic
{
    /// <summary>
    /// 关闭按钮。
    /// 用户在 Inspector 中手动拖入。
    /// </summary>
    [SerializeField]
    private Button _btnClose;

    /// <summary>
    /// 头像按钮。
    /// 用户在 Inspector 中手动拖入。
    /// </summary>
    [SerializeField]
    private Button _btnAvatar;

    /// <summary>
    /// 头像框按钮。
    /// 用户在 Inspector 中手动拖入。
    /// </summary>
    [SerializeField]
    private Button _btnAvatarFrame;

    /// <summary>
    /// 设置按钮。
    /// 用户在 Inspector 中手动拖入。
    /// </summary>
    [SerializeField]
    private Button _btnSettings;

    /// <summary>
    /// 头像页签对应的物体。
    /// 选中头像时显示，其余页签时隐藏。
    /// 用户在 Inspector 中手动拖入。
    /// </summary>
    [SerializeField]
    private GameObject _goAvatar;

    /// <summary>
    /// 头像框页签对应的物体。
    /// 选中头像框时显示，其余页签时隐藏。
    /// 用户在 Inspector 中手动拖入。
    /// </summary>
    [SerializeField]
    private GameObject _goAvatarFrame;

    /// <summary>
    /// 设置页签对应的物体。
    /// 选中设置时显示，其余页签时隐藏。
    /// 用户在 Inspector 中手动拖入。
    /// </summary>
    [SerializeField]
    private GameObject _goSettings;

    /// <summary>
    /// 背景音乐开关 Toggle。
    /// 位于 Settings 页签下，用户在 Inspector 中手动拖入。
    /// </summary>
    [SerializeField]
    private Toggle _toggleBgm;

    /// <summary>
    /// 音效开关 Toggle。
    /// 位于 Settings 页签下，用户在 Inspector 中手动拖入。
    /// </summary>
    [SerializeField]
    private Toggle _toggleSoundEffect;

    // ──────────────────────────────────────────────
    //  头像页签列表引用（全部由用户在 Inspector 中手动拖入）
    // ──────────────────────────────────────────────

    /// <summary>
    /// 已解锁头像计数文本。
    /// 显示格式：已解锁:X/Y。
    /// 用户在 Inspector 中手动拖入。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtOwnedCount;

    /// <summary>
    /// 头像列表 Content 容器。
    /// Scroll View 的 Content 节点，条目模板会克隆到此处。
    /// 用户在 Inspector 中手动拖入。
    /// </summary>
    [SerializeField]
    private RectTransform _contentHeadPortrait;

    /// <summary>
    /// 当前选中头像的展示 RawImage。
    /// 用户在 Inspector 中手动拖入。
    /// 切换头像时实时更新此图片。
    /// </summary>
    [SerializeField]
    private RawImage _rawImageCurrentAvatar;

    /// <summary>
    /// 条目模板（GoHeadPortrait）。
    /// 包含 TxtName / RawImage / ImgSelect / ImgUnlock / UnlockDesc 子节点。
    /// 用户在 Inspector 中手动拖入。
    /// </summary>
    [SerializeField]
    private Transform _itemTemplate;

    // ──────────────────────────────────────────────
    //  头像框页签列表引用（全部由用户在 Inspector 中手动拖入）
    // ──────────────────────────────────────────────

    /// <summary>
    /// 已解锁头像框计数文本。
    /// 显示格式：已解锁:X/Y。
    /// 用户在 Inspector 中手动拖入。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtFrameOwnedCount;

    /// <summary>
    /// 头像框列表 Content 容器。
    /// Scroll View 的 Content 节点，条目模板会克隆到此处。
    /// 用户在 Inspector 中手动拖入。
    /// </summary>
    [SerializeField]
    private RectTransform _contentHeadPortraitFrame;

    /// <summary>
    /// 当前选中头像框的展示 RawImage。
    /// 用户在 Inspector 中手动拖入。
    /// 切换头像框时实时更新此图片。
    /// </summary>
    [SerializeField]
    private Image _imageCurrentAvatarFrame;

    /// <summary>
    /// 头像框条目模板（GoHeadPortraitFrame）。
    /// 包含 TxtName / RawImage / ImgSelect / ImgUnlock / UnlockDesc 子节点。
    /// 用户在 Inspector 中手动拖入。
    /// </summary>
    [SerializeField]
    private Transform _frameItemTemplate;

    // ──────────────────────────────────────────────
    //  运行时状态
    // ──────────────────────────────────────────────

    /// <summary>
    /// 当前选中的设置页签。
    /// 打开界面时默认重置为 Avatar。
    /// </summary>
    private PersonalSettingTab _currentTab = PersonalSettingTab.Avatar;

    /// <summary>
    /// 未选中页签按钮的透明度。
    /// 视觉上表达"未激活"态。
    /// </summary>
    private const float UnselectedTabAlpha = 0f;


    /// <summary>
    /// 头像列表是否已构建完成。
    /// 首次打开构建一次，后续只刷新显示。
    /// </summary>
    private bool _isHeadPortraitListBuilt;

    /// <summary>
    /// 所有头像条目的运行时缓存。
    /// </summary>
    private readonly List<HeadPortraitItemEntry> _headPortraitEntries = new List<HeadPortraitItemEntry>(16);

    /// <summary>
    /// 所有头像数据行缓存。
    /// 列表首次构建时从数据表取出并排序，后续只读不重复取表。
    /// </summary>
    private HeadPortraitDataRow[] _allHeadPortraitRows = Array.Empty<HeadPortraitDataRow>();

    /// <summary>
    /// 头像框列表是否已构建完成。
    /// 首次打开构建一次，后续只刷新显示。
    /// </summary>
    private bool _isHeadPortraitFrameListBuilt;

    /// <summary>
    /// 所有头像框条目的运行时缓存。
    /// </summary>
    private readonly List<HeadPortraitFrameItemEntry> _headPortraitFrameEntries = new List<HeadPortraitFrameItemEntry>(16);

    /// <summary>
    /// 所有头像框数据行缓存。
    /// 列表首次构建时从数据表取出并排序，后续只读不重复取表。
    /// </summary>
    private HeadPortraitFrameDataRow[] _allHeadPortraitFrameRows = Array.Empty<HeadPortraitFrameDataRow>();

    /// <summary>
    /// 防止代码同步 Toggle.isOn 时触发 onValueChanged 导致循环或重复写入 PlayerPrefs。
    /// </summary>
    private bool _isSyncingToggleState;

    /// <summary>
    /// 单个头像条目的运行时缓存。
    /// 每个条目对应一个 GoHeadPortrait 列节点。
    /// </summary>
    private sealed class HeadPortraitItemEntry
    {
        /// <summary>
        /// 条目根节点（GoHeadPortrait）。
        /// 用于控制显隐和透明占位。
        /// </summary>
        public GameObject Root;

        /// <summary>
        /// 条目按钮。
        /// 点击后选中该头像。
        /// </summary>
        public Button Button;

        /// <summary>
        /// 头像名称文本。
        /// </summary>
        public TextMeshProUGUI TxtName;

        /// <summary>
        /// 头像图片。
        /// </summary>
        public RawImage RawImagePortrait;

        /// <summary>
        /// 选中状态图片。
        /// </summary>
        public GameObject ImgSelect;

        /// <summary>
        /// 解锁状态图片（显示=未解锁锁定态，隐藏=已解锁）。
        /// </summary>
        public GameObject ImgUnlock;

        /// <summary>
        /// 解锁按钮（ImgUnlock 上的 Button）。
        /// 点击后打开购买确认弹窗。
        /// </summary>
        public Button BtnUnlock;

        /// <summary>
        /// 解锁描述文本。
        /// </summary>
        public TextMeshProUGUI TxtUnlockDesc;

        /// <summary>
        /// 当前条目绑定的数据行。
        /// 为 null 时表示该槽位为空占位。
        /// </summary>
        public HeadPortraitDataRow DataRow;
    }

    /// <summary>
    /// 单个头像框条目的运行时缓存。
    /// 每个条目对应一个 GoHeadPortraitFrame 列节点。
    /// </summary>
    private sealed class HeadPortraitFrameItemEntry
    {
        /// <summary>
        /// 条目根节点。
        /// </summary>
        public GameObject Root;

        /// <summary>
        /// 条目按钮。
        /// 点击后选中该头像框。
        /// </summary>
        public Button Button;

        /// <summary>
        /// 头像框名称文本。
        /// </summary>
        public TextMeshProUGUI TxtName;

        /// <summary>
        /// 头像框图片。
        /// </summary>
        public Image ImageFrame;

        /// <summary>
        /// 选中状态图片。
        /// </summary>
        public GameObject ImgSelect;

        /// <summary>
        /// 解锁状态图片（显示=未解锁锁定态，隐藏=已解锁）。
        /// </summary>
        public GameObject ImgUnlock;

        /// <summary>
        /// 解锁按钮（ImgUnlock 上的 Button）。
        /// 点击后打开购买确认弹窗。
        /// </summary>
        public Button BtnUnlock;

        /// <summary>
        /// 解锁描述文本。
        /// </summary>
        public TextMeshProUGUI TxtUnlockDesc;

        /// <summary>
        /// 当前条目绑定的数据行。
        /// </summary>
        public HeadPortraitFrameDataRow DataRow;
    }

    /// <summary>
    /// 个人设置界面的三个离散页签。
    /// </summary>
    private enum PersonalSettingTab
    {
        Avatar = 0,
        AvatarFrame = 1,
        Settings = 2,
    }

    /// <summary>
    /// 界面初始化。
    /// 只执行一次：校验引用并注册所有按钮监听。
    /// </summary>
    /// <param name="userData">打开窗体时传入的自定义参数。</param>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        CacheReferences();

        if (_btnClose != null)
        {
            _btnClose.onClick.RemoveListener(OnBtnCloseClicked);
            _btnClose.onClick.AddListener(OnBtnCloseClicked);
        }

        BindTabButtonListeners();
        BindSoundToggleListeners();
    }

    /// <summary>
    /// 界面打开。
    /// 默认选中头像页签并刷新按钮视觉状态。
    /// </summary>
    /// <param name="userData">打开窗体时传入的自定义参数。</param>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        _currentTab = PersonalSettingTab.Avatar;
        RefreshTabVisualState();
        BuildHeadPortraitList();
        RefreshHeadPortraitList();
        RefreshCurrentAvatar();
    }

    /// <summary>
    /// 对象销毁时移除所有按钮监听，防止闭包或委托残留。
    /// </summary>
    private void OnDestroy()
    {
        if (_btnClose != null)
        {
            _btnClose.onClick.RemoveListener(OnBtnCloseClicked);
        }

        UnbindTabButtonListeners();
        UnbindSoundToggleListeners();
        UnbindHeadPortraitItemListeners();
        UnbindHeadPortraitFrameItemListeners();
    }

    /// <summary>
    /// 缓存界面节点引用。
    /// 所有引用均由用户在 Inspector 中手动拖入，缺少时仅输出警告。
    /// </summary>
    private void CacheReferences()
    {
        if (_btnClose == null)
        {
            Log.Warning("PersonalSettingUIForm 缺少关闭按钮引用，请在 Inspector 中把 BtnClose 拖入 _btnClose。");
        }

        if (_btnAvatar == null)
        {
            Log.Warning("PersonalSettingUIForm 缺少头像按钮引用，请在 Inspector 中把头像按钮拖入 _btnAvatar。");
        }

        if (_btnAvatarFrame == null)
        {
            Log.Warning("PersonalSettingUIForm 缺少头像框按钮引用，请在 Inspector 中把头像框按钮拖入 _btnAvatarFrame。");
        }

        if (_btnSettings == null)
        {
            Log.Warning("PersonalSettingUIForm 缺少设置按钮引用，请在 Inspector 中把设置按钮拖入 _btnSettings。");
        }

        if (_txtOwnedCount == null)
        {
            Log.Warning("PersonalSettingUIForm 缺少已解锁计数文本引用，请在 Inspector 中把已解锁文本拖入 _txtOwnedCount。");
        }

        if (_contentHeadPortrait == null)
        {
            Log.Warning("PersonalSettingUIForm 缺少头像列表 Content 引用，请在 Inspector 中把 Content 拖入 _contentHeadPortrait。");
        }

        if (_itemTemplate == null)
        {
            Log.Warning("PersonalSettingUIForm 缺少条目模板引用，请在 Inspector 中把 GoHeadPortrait 拖入 _itemTemplate。");
        }

        if (_toggleBgm == null)
        {
            Log.Warning("PersonalSettingUIForm 缺少背景音乐开关 Toggle 引用，请在 Inspector 中把对应 Toggle 拖入 _toggleBgm。");
        }

        if (_toggleSoundEffect == null)
        {
            Log.Warning("PersonalSettingUIForm 缺少音效开关 Toggle 引用，请在 Inspector 中把对应 Toggle 拖入 _toggleSoundEffect。");
        }
    }

    /// <summary>
    /// 绑定三个页签按钮的点击监听。
    /// </summary>
    private void BindTabButtonListeners()
    {
        if (_btnAvatar != null)
        {
            _btnAvatar.onClick.RemoveListener(OnBtnAvatarClicked);
            _btnAvatar.onClick.AddListener(OnBtnAvatarClicked);
        }

        if (_btnAvatarFrame != null)
        {
            _btnAvatarFrame.onClick.RemoveListener(OnBtnAvatarFrameClicked);
            _btnAvatarFrame.onClick.AddListener(OnBtnAvatarFrameClicked);
        }

        if (_btnSettings != null)
        {
            _btnSettings.onClick.RemoveListener(OnBtnSettingsClicked);
            _btnSettings.onClick.AddListener(OnBtnSettingsClicked);
        }
    }

    /// <summary>
    /// 注销三个页签按钮的点击监听。
    /// </summary>
    private void UnbindTabButtonListeners()
    {
        if (_btnAvatar != null)
        {
            _btnAvatar.onClick.RemoveListener(OnBtnAvatarClicked);
        }

        if (_btnAvatarFrame != null)
        {
            _btnAvatarFrame.onClick.RemoveListener(OnBtnAvatarFrameClicked);
        }

        if (_btnSettings != null)
        {
            _btnSettings.onClick.RemoveListener(OnBtnSettingsClicked);
        }
    }

    /// <summary>
    /// 根据当前选中页签，统一刷新三个按钮的 Image 透明度
    /// 以及三个对应物体的显隐。
    /// 选中项 alpha = 1 且物体显示，未选中项 alpha = UnselectedTabAlpha 且物体隐藏。
    /// </summary>
    private void RefreshTabVisualState()
    {
        SetButtonAlpha(_btnAvatar, _currentTab == PersonalSettingTab.Avatar);
        SetButtonAlpha(_btnAvatarFrame, _currentTab == PersonalSettingTab.AvatarFrame);
        SetButtonAlpha(_btnSettings, _currentTab == PersonalSettingTab.Settings);

        SetNodeActive(_goAvatar, _currentTab == PersonalSettingTab.Avatar);
        SetNodeActive(_goAvatarFrame, _currentTab == PersonalSettingTab.AvatarFrame);
        SetNodeActive(_goSettings, _currentTab == PersonalSettingTab.Settings);
    }

    /// <summary>
    /// 安全设置节点激活状态，跳过空引用和重复赋值。
    /// </summary>
    /// <param name="node">目标节点，允许为 null。</param>
    /// <param name="isActive">是否激活。</param>
    private static void SetNodeActive(GameObject node, bool isActive)
    {
        if (node == null || node.activeSelf == isActive)
        {
            return;
        }

        node.SetActive(isActive);
    }

    /// <summary>
    /// 设置单个按钮的 Image 透明度。
    /// </summary>
    /// <param name="button">目标按钮。</param>
    /// <param name="isSelected">是否处于选中态。</param>
    private static void SetButtonAlpha(Button button, bool isSelected)
    {
        if (button == null)
        {
            return;
        }

        Image image = button.GetComponent<Image>();
        if (image == null)
        {
            return;
        }

        Color color = image.color;
        color.a = isSelected ? 1f : UnselectedTabAlpha;
        image.color = color;
    }

    /// <summary>
    /// 绑定声音开关 Toggle 的 onValueChanged 监听。
    /// </summary>
    private void BindSoundToggleListeners()
    {
        if (_toggleBgm != null)
        {
            _toggleBgm.onValueChanged.RemoveListener(OnToggleBgmValueChanged);
            _toggleBgm.onValueChanged.AddListener(OnToggleBgmValueChanged);
        }

        if (_toggleSoundEffect != null)
        {
            _toggleSoundEffect.onValueChanged.RemoveListener(OnToggleSoundEffectValueChanged);
            _toggleSoundEffect.onValueChanged.AddListener(OnToggleSoundEffectValueChanged);
        }
    }

    /// <summary>
    /// 注销声音开关 Toggle 的 onValueChanged 监听。
    /// </summary>
    private void UnbindSoundToggleListeners()
    {
        if (_toggleBgm != null)
        {
            _toggleBgm.onValueChanged.RemoveListener(OnToggleBgmValueChanged);
        }

        if (_toggleSoundEffect != null)
        {
            _toggleSoundEffect.onValueChanged.RemoveListener(OnToggleSoundEffectValueChanged);
        }
    }

    /// <summary>
    /// 从本地持久化读取声音开关状态并同步到两个 Toggle。
    /// 通过 _isSyncingToggleState 标志避免触发业务回调。
    /// </summary>
    private void RefreshSoundToggleStates()
    {
        if (_toggleBgm == null || _toggleSoundEffect == null)
        {
            return;
        }

        _isSyncingToggleState = true;

        // 默认值视为开启（1），确保首次安装时正常发声
        _toggleBgm.isOn = PlayerPrefs.GetInt("Setting_BgmEnabled", 1) != 0;
        _toggleSoundEffect.isOn = PlayerPrefs.GetInt("Setting_SfxEnabled", 1) != 0;

        _isSyncingToggleState = false;
    }

    /// <summary>
    /// 背景音乐 Toggle 值变化回调。
    /// 写入 PlayerPrefs 并实时应用到 SoundComponent 的 BGM 声音组。
    /// </summary>
    /// <param name="isOn">Toggle 是否选中（开启）。</param>
    private void OnToggleBgmValueChanged(bool isOn)
    {
        if (_isSyncingToggleState)
        {
            return;
        }

        PlayerPrefs.SetInt("Setting_BgmEnabled", isOn ? 1 : 0);
        PlayerPrefs.Save();

        SoundComponent soundComponent = global::GameEntry.Sound;
        if (soundComponent == null)
        {
            return;
        }

        ISoundGroup bgmGroup = soundComponent.GetSoundGroup(UIInteractionSound.BgmSoundGroupName);
        if (bgmGroup != null)
        {
            bgmGroup.Mute = !isOn;
        }
    }

    /// <summary>
    /// 音效 Toggle 值变化回调。
    /// 写入 PlayerPrefs 并实时应用到 UI 与 Effect 两个声音组。
    /// </summary>
    /// <param name="isOn">Toggle 是否选中（开启）。</param>
    private void OnToggleSoundEffectValueChanged(bool isOn)
    {
        if (_isSyncingToggleState)
        {
            return;
        }

        PlayerPrefs.SetInt("Setting_SfxEnabled", isOn ? 1 : 0);
        PlayerPrefs.Save();

        SoundComponent soundComponent = global::GameEntry.Sound;
        if (soundComponent == null)
        {
            return;
        }

        ISoundGroup uiGroup = soundComponent.GetSoundGroup(UIInteractionSound.SoundGroupName);
        if (uiGroup != null)
        {
            uiGroup.Mute = !isOn;
        }

        ISoundGroup effectGroup = soundComponent.GetSoundGroup(UIInteractionSound.EffectSoundGroupName);
        if (effectGroup != null)
        {
            effectGroup.Mute = !isOn;
        }
    }

    /// <summary>
    /// 头像按钮点击回调。
    /// 当前执行空逻辑，仅切换选中态并刷新视觉。
    /// </summary>
    private void OnBtnAvatarClicked()
    {
        UIInteractionSound.PlayClick();
        _currentTab = PersonalSettingTab.Avatar;
        RefreshTabVisualState();
        RefreshHeadPortraitList();
    }

    /// <summary>
    /// 头像框按钮点击回调。
    /// 当前执行空逻辑，仅切换选中态并刷新视觉。
    /// </summary>
    private void OnBtnAvatarFrameClicked()
    {
        UIInteractionSound.PlayClick();
        _currentTab = PersonalSettingTab.AvatarFrame;
        RefreshTabVisualState();
        BuildHeadPortraitFrameList();
        RefreshHeadPortraitFrameList();
        RefreshCurrentAvatarFrame();
    }

    /// <summary>
    /// 设置按钮点击回调。
    /// 切换到设置页签并同步声音开关 Toggle 状态。
    /// </summary>
    private void OnBtnSettingsClicked()
    {
        UIInteractionSound.PlayClick();
        _currentTab = PersonalSettingTab.Settings;
        RefreshTabVisualState();
        RefreshSoundToggleStates();
    }

    /// <summary>
    /// 关闭按钮点击回调。
    /// 通过 UIForm.SerialId 精确关闭当前窗体实例。
    /// </summary>
    private void OnBtnCloseClicked()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();

        if (UIForm == null || GameEntry.UI == null)
        {
            return;
        }

        GameEntry.UI.CloseUIForm(UIForm.SerialId);
    }

    // ═══════════════════════════════════════════════════
    //  头像列表：构建 / 刷新 / 选中 / 清理
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// 首次打开界面时构建头像列表。
    /// 从数据表取全部头像行，按行×列克隆模板，填入条目缓存。
    /// 构建一次后反复复用。
    /// </summary>
    private void BuildHeadPortraitList()
    {
        if (_isHeadPortraitListBuilt || _contentHeadPortrait == null || _itemTemplate == null)
        {
            return;
        }

        if (GameEntry.DataTables == null)
        {
            Log.Warning("PersonalSettingUIForm 构建头像列表失败，DataTables 模块缺失。");
            return;
        }

        HeadPortraitDataRow[] allRows = GameEntry.DataTables.GetAllDataRows<HeadPortraitDataRow>();
        if (allRows == null || allRows.Length == 0)
        {
            Log.Warning("PersonalSettingUIForm 头像数据表为空，无法构建列表。");
            return;
        }

        // 按 Id 升序排列，确保列表顺序稳定
        Array.Sort(allRows, (a, b) => a.Id.CompareTo(b.Id));
        _allHeadPortraitRows = allRows;

        // 隐藏条目模板
        _itemTemplate.gameObject.SetActive(false);

        for (int i = 0; i < allRows.Length; i++)
        {
            // 克隆条目模板到 Content
            Transform itemTransform = Instantiate(_itemTemplate, _contentHeadPortrait);
            itemTransform.gameObject.SetActive(true);

            HeadPortraitItemEntry entry = new HeadPortraitItemEntry
            {
                Root = itemTransform.gameObject,
                Button = itemTransform.GetComponent<Button>(),
                DataRow = allRows[i]
            };

            // 查找子节点组件（按索引定位，与 prefab 层级严格绑定）
            // 第1个子物体 = 名称文本
            Transform txtName = itemTransform.GetChild(0);
            if (txtName != null)
            {
                entry.TxtName = txtName.GetComponent<TextMeshProUGUI>();
            }

            // 第3个子物体 = 头像 RawImage
            Transform rawImage = itemTransform.GetChild(2);
            if (rawImage != null)
            {
                entry.RawImagePortrait = rawImage.GetComponent<RawImage>();
            }

            // 第4个子物体 = 选中标识
            Transform imgSelect = itemTransform.GetChild(3);
            if (imgSelect != null)
            {
                entry.ImgSelect = imgSelect.gameObject;
            }

            // 第5个子物体 = 解锁标识（显示=未解锁锁定态，隐藏=已解锁）
            Transform imgUnlock = itemTransform.GetChild(4);
            if (imgUnlock != null)
            {
                entry.ImgUnlock = imgUnlock.gameObject;
                entry.BtnUnlock = imgUnlock.GetComponent<Button>();

                // 第5个子物体的第2个子物体 = 获取描述文本
                if (imgUnlock.childCount > 1)
                {
                    entry.TxtUnlockDesc = imgUnlock.GetChild(1).GetComponent<TextMeshProUGUI>();
                }
            }

            // 绑定条目点击监听（选中头像）
            if (entry.Button != null)
            {
                int capturedId = allRows[i].Id;
                entry.Button.onClick.AddListener(() => OnHeadPortraitItemClicked(capturedId));
            }

            // 绑定解锁按钮点击监听（打开购买弹窗）
            if (entry.BtnUnlock != null)
            {
                int capturedUnlockId = allRows[i].Id;
                entry.BtnUnlock.onClick.AddListener(() => OnBtnUnlockClicked(capturedUnlockId));
            }

            _headPortraitEntries.Add(entry);
        }

        _isHeadPortraitListBuilt = true;
    }

    /// <summary>
    /// 刷新头像列表的所有条目显示。
    /// 包括名字、图片、选中态、解锁态、已解锁计数。
    /// </summary>
    private void RefreshHeadPortraitList()
    {
        if (!_isHeadPortraitListBuilt)
        {
            return;
        }

        // 兜底：运行时尚未选中任何头像时，自动选中第一个默认解锁的
        if (GameEntry.Fruits != null && string.IsNullOrEmpty(GameEntry.Fruits.SelectedHeadPortraitCode))
        {
            for (int i = 0; i < _allHeadPortraitRows.Length; i++)
            {
                if (_allHeadPortraitRows[i] != null && _allHeadPortraitRows[i].IsDefaultUnlocked)
                {
                    GameEntry.Fruits.TrySetSelectedHeadPortrait(_allHeadPortraitRows[i].Code);
                    break;
                }
            }
        }

        int ownedCount = 0;
        int totalCount = _allHeadPortraitRows.Length;

        for (int i = 0; i < _headPortraitEntries.Count; i++)
        {
            HeadPortraitItemEntry entry = _headPortraitEntries[i];
            if (entry == null)
            {
                continue;
            }

            HeadPortraitDataRow row = entry.DataRow;

            // 名称
            if (entry.TxtName != null)
            {
                entry.TxtName.text = row.Name;
            }

            // 解锁状态：默认解锁 或 运行时已购买解锁
            bool isUnlocked = row.IsDefaultUnlocked
                || (GameEntry.Fruits != null && GameEntry.Fruits.IsHeadPortraitUnlocked(row.Code));
            if (isUnlocked)
            {
                ownedCount++;
            }

            // 选中态：与运行时选中 Code 匹配
            string selectedCode = GameEntry.Fruits != null ? GameEntry.Fruits.SelectedHeadPortraitCode : null;
            SetNodeActive(entry.ImgSelect, string.Equals(row.Code, selectedCode, StringComparison.Ordinal));

            // 条目按钮：始终可点击
            // 已解锁 → 选中该头像；未解锁 → 打开购买弹窗
            if (entry.Button != null)
            {
                entry.Button.interactable = true;
            }

            // 解锁标识：显示=未解锁（锁定态），隐藏=已解锁
            SetNodeActive(entry.ImgUnlock, !isUnlocked);

            // 获取描述文本：未解锁时显示解锁条件
            if (entry.TxtUnlockDesc != null)
            {
                entry.TxtUnlockDesc.text = isUnlocked ? string.Empty : row.AcquireDesc;
            }

            // 加载头像图片（异步）
            LoadHeadPortraitIcon(entry, row.IconPath);
        }

        // 更新已解锁计数
        if (_txtOwnedCount != null)
        {
            _txtOwnedCount.text = Utility.Text.Format("已解锁:{0}/{1}", ownedCount, totalCount);
        }
    }

    /// <summary>
    /// 从 GameAssetModule 预加载缓存中同步获取头像图标并赋值给 RawImage。
    /// 头像 Sprite 在启动阶段由 GameAssetModule 批量预加载，此处直接查缓存即可。
    /// </summary>
    /// <param name="entry">目标条目。</param>
    /// <param name="iconPath">图标资源路径。</param>
    private static void LoadHeadPortraitIcon(HeadPortraitItemEntry entry, string iconPath)
    {
        if (entry.RawImagePortrait == null || string.IsNullOrEmpty(iconPath))
        {
            return;
        }

        if (GameEntry.GameAssets == null)
        {
            return;
        }

        // 从预加载缓存同步取 Sprite，无需异步加载
        if (GameEntry.GameAssets.TryGetHeadPortraitSprite(iconPath, out Sprite sprite) && sprite != null)
        {
            entry.RawImagePortrait.texture = sprite.texture;
        }
        else
        {
            Log.Warning("PersonalSettingUIForm 头像图标未命中预加载缓存，Path='{0}'。", iconPath);
        }
    }

    /// <summary>
    /// 注销所有头像条目的点击监听。
    /// </summary>
    private void UnbindHeadPortraitItemListeners()
    {
        for (int i = 0; i < _headPortraitEntries.Count; i++)
        {
            HeadPortraitItemEntry entry = _headPortraitEntries[i];
            if (entry == null)
            {
                continue;
            }

            if (entry.Button != null)
            {
                entry.Button.onClick.RemoveAllListeners();
            }

            if (entry.BtnUnlock != null)
            {
                entry.BtnUnlock.onClick.RemoveAllListeners();
            }
        }
    }

    /// <summary>
    /// 头像条目点击回调。
    /// 已解锁头像：选中；未解锁头像：打开购买弹窗。
    /// </summary>
    /// <param name="rowId">点击的头像数据行 Id。</param>
    private void OnHeadPortraitItemClicked(int rowId)
    {
        UIInteractionSound.PlayClick();

        HeadPortraitDataRow row = GameEntry.DataTables?.GetDataRow<HeadPortraitDataRow>(rowId);
        if (row == null)
        {
            return;
        }

        bool isUnlocked = row.IsDefaultUnlocked
            || (GameEntry.Fruits != null && GameEntry.Fruits.IsHeadPortraitUnlocked(row.Code));

        if (isUnlocked)
        {
            // 已解锁：选中该头像
            if (GameEntry.Fruits != null)
            {
                GameEntry.Fruits.TrySetSelectedHeadPortrait(row.Code);
            }

            RefreshHeadPortraitList();
            RefreshCurrentAvatar();
        }
        else
        {
            // 未解锁：打开购买弹窗
            OpenPurchaseUIForm(row);
        }
    }

    /// <summary>
    /// 解锁按钮点击回调。
    /// 打开购买确认弹窗。
    /// </summary>
    /// <param name="rowId">头像数据行 Id。</param>
    private void OnBtnUnlockClicked(int rowId)
    {
        UIInteractionSound.PlayClick();

        HeadPortraitDataRow row = GameEntry.DataTables?.GetDataRow<HeadPortraitDataRow>(rowId);
        if (row == null)
        {
            return;
        }

        OpenPurchaseUIForm(row);
    }

    /// <summary>
    /// 打开通用购买确认弹窗。
    /// 提示文案格式：是否购买{头像名}头像
    /// </summary>
    /// <param name="row">头像数据行。</param>
    private void OpenPurchaseUIForm(HeadPortraitDataRow row)
    {
        if (row == null || GameEntry.UI == null)
        {
            return;
        }

        int capturedId = row.Id;
        PurchaseUIData data = new PurchaseUIData
        {
            ItemName = row.Name,
            AcquireType = row.AcquireType,
            AcquireParam = row.AcquireParam,
            PromptText = Utility.Text.Format("是否购买{0}头像", row.Name),
            OnPurchaseSuccess = () => OnHeadPortraitPurchased(capturedId),
        };

        GameEntry.UI.OpenUIForm(UIFormDefine.PurchaseUIForm, UIFormDefine.PopupGroup, data);
    }

    /// <summary>
    /// 头像购买成功回调。
    /// 标记该头像为已解锁并刷新列表。
    /// </summary>
    /// <param name="rowId">购买成功的头像 Id。</param>
    private void OnHeadPortraitPurchased(int rowId)
    {
        HeadPortraitDataRow row = GameEntry.DataTables?.GetDataRow<HeadPortraitDataRow>(rowId);
        if (row == null)
        {
            return;
        }

        // 写入运行时解锁数据并自动选中
        if (GameEntry.Fruits != null)
        {
            GameEntry.Fruits.TryUnlockHeadPortrait(row.Code);
            GameEntry.Fruits.TrySetSelectedHeadPortrait(row.Code);
        }

        RefreshHeadPortraitList();
        RefreshCurrentAvatar();
    }

    /// <summary>
    /// 刷新当前选中头像的展示图片。
    /// 从运行时数据读取选中 Code，再从预加载缓存取 Sprite。
    /// </summary>
    private void RefreshCurrentAvatar()
    {
        if (_rawImageCurrentAvatar == null || GameEntry.Fruits == null || GameEntry.GameAssets == null)
        {
            return;
        }

        string selectedCode = GameEntry.Fruits.SelectedHeadPortraitCode;
        if (string.IsNullOrWhiteSpace(selectedCode))
        {
            _rawImageCurrentAvatar.texture = null;
            return;
        }

        HeadPortraitDataRow row = GameEntry.DataTables?.GetDataRowByCode<HeadPortraitDataRow>(selectedCode);
        if (row == null)
        {
            _rawImageCurrentAvatar.texture = null;
            return;
        }

        if (GameEntry.GameAssets.TryGetHeadPortraitSprite(row.IconPath, out Sprite sprite) && sprite != null)
        {
            _rawImageCurrentAvatar.texture = sprite.texture;
        }
        else
        {
            _rawImageCurrentAvatar.texture = null;
        }
    }

    // ──────────────────────────────────────────────
    //  头像框页签逻辑
    // ──────────────────────────────────────────────

    /// <summary>
    /// 构建头像框列表。
    /// 首次切到头像框页签时调用，从数据表读取所有行并克隆模板生成条目。
    /// </summary>
    private void BuildHeadPortraitFrameList()
    {
        if (_isHeadPortraitFrameListBuilt)
        {
            return;
        }

        if (GameEntry.DataTables == null || !GameEntry.DataTables.IsAvailable<HeadPortraitFrameDataRow>())
        {
            return;
        }

        if (_contentHeadPortraitFrame == null || _frameItemTemplate == null)
        {
            return;
        }

        HeadPortraitFrameDataRow[] allRows = GameEntry.DataTables.GetAllDataRows<HeadPortraitFrameDataRow>();
        if (allRows == null || allRows.Length == 0)
        {
            return;
        }

        Array.Sort(allRows, (a, b) => a.Id.CompareTo(b.Id));
        _allHeadPortraitFrameRows = allRows;

        _frameItemTemplate.gameObject.SetActive(false);

        for (int i = 0; i < allRows.Length; i++)
        {
            Transform itemTransform = Instantiate(_frameItemTemplate, _contentHeadPortraitFrame);
            itemTransform.gameObject.SetActive(true);

            HeadPortraitFrameItemEntry entry = new HeadPortraitFrameItemEntry
            {
                Root = itemTransform.gameObject,
                Button = itemTransform.GetComponent<Button>(),
                DataRow = allRows[i]
            };

            Transform txtName = itemTransform.GetChild(0);
            if (txtName != null)
            {
                entry.TxtName = txtName.GetComponent<TextMeshProUGUI>();
            }

            Transform imgFrame = itemTransform.GetChild(2);
            if (imgFrame != null)
            {
                entry.ImageFrame = imgFrame.GetComponent<Image>();
            }

            Transform imgSelect = itemTransform.GetChild(3);
            if (imgSelect != null)
            {
                entry.ImgSelect = imgSelect.gameObject;
            }

            Transform imgUnlock = itemTransform.GetChild(4);
            if (imgUnlock != null)
            {
                entry.ImgUnlock = imgUnlock.gameObject;
                entry.BtnUnlock = imgUnlock.GetComponent<Button>();

                if (imgUnlock.childCount > 1)
                {
                    entry.TxtUnlockDesc = imgUnlock.GetChild(1).GetComponent<TextMeshProUGUI>();
                }
            }

            if (entry.Button != null)
            {
                int capturedId = allRows[i].Id;
                entry.Button.onClick.AddListener(() => OnHeadPortraitFrameItemClicked(capturedId));
            }

            if (entry.BtnUnlock != null)
            {
                int capturedUnlockId = allRows[i].Id;
                entry.BtnUnlock.onClick.AddListener(() => OnBtnFrameUnlockClicked(capturedUnlockId));
            }

            _headPortraitFrameEntries.Add(entry);
        }

        _frameItemTemplate.gameObject.SetActive(false);
        _isHeadPortraitFrameListBuilt = true;
    }

    /// <summary>
    /// 刷新头像框列表的所有条目显示。
    /// </summary>
    private void RefreshHeadPortraitFrameList()
    {
        if (!_isHeadPortraitFrameListBuilt)
        {
            return;
        }

        // 兜底：运行时尚未选中任何头像框时，自动选中第一个默认解锁的
        if (GameEntry.Fruits != null && string.IsNullOrEmpty(GameEntry.Fruits.SelectedHeadPortraitFrameCode))
        {
            for (int i = 0; i < _allHeadPortraitFrameRows.Length; i++)
            {
                if (_allHeadPortraitFrameRows[i] != null && _allHeadPortraitFrameRows[i].IsDefaultUnlocked)
                {
                    GameEntry.Fruits.TrySetSelectedHeadPortraitFrame(_allHeadPortraitFrameRows[i].Code);
                    break;
                }
            }
        }

        int ownedCount = 0;
        int totalCount = _allHeadPortraitFrameRows.Length;

        for (int i = 0; i < _headPortraitFrameEntries.Count; i++)
        {
            HeadPortraitFrameItemEntry entry = _headPortraitFrameEntries[i];
            if (entry == null)
            {
                continue;
            }

            HeadPortraitFrameDataRow row = entry.DataRow;

            if (entry.TxtName != null)
            {
                entry.TxtName.text = row.Name;
            }

            bool isUnlocked = row.IsDefaultUnlocked
                || (GameEntry.Fruits != null && GameEntry.Fruits.IsHeadPortraitFrameUnlocked(row.Code));
            if (isUnlocked)
            {
                ownedCount++;
            }

            string selectedCode = GameEntry.Fruits != null ? GameEntry.Fruits.SelectedHeadPortraitFrameCode : null;
            SetNodeActive(entry.ImgSelect, string.Equals(row.Code, selectedCode, StringComparison.Ordinal));

            if (entry.Button != null)
            {
                entry.Button.interactable = true;
            }

            SetNodeActive(entry.ImgUnlock, !isUnlocked);

            if (entry.TxtUnlockDesc != null)
            {
                entry.TxtUnlockDesc.text = isUnlocked ? string.Empty : row.AcquireDesc;
            }

            LoadHeadPortraitFrameIcon(entry, row.IconPath);
        }

        if (_txtFrameOwnedCount != null)
        {
            _txtFrameOwnedCount.text = Utility.Text.Format("已解锁:{0}/{1}", ownedCount, totalCount);
        }
    }

    /// <summary>
    /// 头像框条目点击回调。
    /// 已解锁：选中；未解锁：打开购买弹窗。
    /// </summary>
    /// <param name="rowId">点击的头像框数据行 Id。</param>
    private void OnHeadPortraitFrameItemClicked(int rowId)
    {
        UIInteractionSound.PlayClick();

        HeadPortraitFrameDataRow row = GameEntry.DataTables?.GetDataRow<HeadPortraitFrameDataRow>(rowId);
        if (row == null)
        {
            return;
        }

        bool isUnlocked = row.IsDefaultUnlocked
            || (GameEntry.Fruits != null && GameEntry.Fruits.IsHeadPortraitFrameUnlocked(row.Code));

        if (isUnlocked)
        {
            if (GameEntry.Fruits != null)
            {
                GameEntry.Fruits.TrySetSelectedHeadPortraitFrame(row.Code);
            }

            RefreshHeadPortraitFrameList();
            RefreshCurrentAvatarFrame();
        }
        else
        {
            OpenPurchaseUIFormForFrame(row);
        }
    }

    /// <summary>
    /// 头像框解锁按钮点击回调。
    /// </summary>
    /// <param name="rowId">头像框数据行 Id。</param>
    private void OnBtnFrameUnlockClicked(int rowId)
    {
        UIInteractionSound.PlayClick();

        HeadPortraitFrameDataRow row = GameEntry.DataTables?.GetDataRow<HeadPortraitFrameDataRow>(rowId);
        if (row == null)
        {
            return;
        }

        OpenPurchaseUIFormForFrame(row);
    }

    /// <summary>
    /// 打开通用购买确认弹窗（头像框）。
    /// </summary>
    /// <param name="row">头像框数据行。</param>
    private void OpenPurchaseUIFormForFrame(HeadPortraitFrameDataRow row)
    {
        if (row == null || GameEntry.UI == null)
        {
            return;
        }

        int capturedId = row.Id;
        PurchaseUIData data = new PurchaseUIData
        {
            ItemName = row.Name,
            AcquireType = row.AcquireType,
            AcquireParam = row.AcquireParam,
            PromptText = Utility.Text.Format("是否购买{0}头像框", row.Name),
            OnPurchaseSuccess = () => OnHeadPortraitFramePurchased(capturedId),
        };

        GameEntry.UI.OpenUIForm(UIFormDefine.PurchaseUIForm, UIFormDefine.PopupGroup, data);
    }

    /// <summary>
    /// 头像框购买成功回调。
    /// </summary>
    /// <param name="rowId">购买成功的头像框 Id。</param>
    private void OnHeadPortraitFramePurchased(int rowId)
    {
        HeadPortraitFrameDataRow row = GameEntry.DataTables?.GetDataRow<HeadPortraitFrameDataRow>(rowId);
        if (row == null)
        {
            return;
        }

        if (GameEntry.Fruits != null)
        {
            GameEntry.Fruits.TryUnlockHeadPortraitFrame(row.Code);
            GameEntry.Fruits.TrySetSelectedHeadPortraitFrame(row.Code);
        }

        RefreshHeadPortraitFrameList();
        RefreshCurrentAvatarFrame();
    }

    /// <summary>
    /// 刷新当前选中头像框的展示图片。
    /// </summary>
    private void RefreshCurrentAvatarFrame()
    {
        if (_imageCurrentAvatarFrame == null || GameEntry.Fruits == null || GameEntry.GameAssets == null)
        {
            return;
        }

        string selectedCode = GameEntry.Fruits.SelectedHeadPortraitFrameCode;

        if (string.IsNullOrEmpty(selectedCode) && GameEntry.DataTables != null && GameEntry.DataTables.IsAvailable<HeadPortraitFrameDataRow>())
        {
            HeadPortraitFrameDataRow[] rows = GameEntry.DataTables.GetAllDataRows<HeadPortraitFrameDataRow>();
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i] != null && rows[i].IsDefaultUnlocked)
                {
                    GameEntry.Fruits.TrySetSelectedHeadPortraitFrame(rows[i].Code);
                    selectedCode = rows[i].Code;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(selectedCode))
        {
            _imageCurrentAvatarFrame.sprite = null;
            return;
        }

        HeadPortraitFrameDataRow row = GameEntry.DataTables?.GetDataRowByCode<HeadPortraitFrameDataRow>(selectedCode);
        if (row == null)
        {
            _imageCurrentAvatarFrame.sprite = null;
            return;
        }

        if (GameEntry.GameAssets.TryGetHeadPortraitFrameSprite(row.IconPath, out Sprite sprite) && sprite != null)
        {
            _imageCurrentAvatarFrame.sprite = sprite;
        }
        else
        {
            _imageCurrentAvatarFrame.sprite = null;
        }
    }

    /// <summary>
    /// 异步加载头像框图标到条目 RawImage。
    /// </summary>
    private void LoadHeadPortraitFrameIcon(HeadPortraitFrameItemEntry entry, string iconPath)
    {
        if (entry.ImageFrame == null || string.IsNullOrWhiteSpace(iconPath))
        {
            return;
        }

        if (GameEntry.GameAssets != null && GameEntry.GameAssets.TryGetHeadPortraitFrameSprite(iconPath, out Sprite sprite) && sprite != null)
        {
            entry.ImageFrame.sprite = sprite;
        }
        else
        {
            entry.ImageFrame.sprite = null;
        }
    }

    /// <summary>
    /// 清理所有头像框条目的按钮监听。
    /// </summary>
    private void UnbindHeadPortraitFrameItemListeners()
    {
        for (int i = 0; i < _headPortraitFrameEntries.Count; i++)
        {
            HeadPortraitFrameItemEntry entry = _headPortraitFrameEntries[i];
            if (entry == null)
            {
                continue;
            }

            if (entry.Button != null)
            {
                entry.Button.onClick.RemoveAllListeners();
            }

            if (entry.BtnUnlock != null)
            {
                entry.BtnUnlock.onClick.RemoveAllListeners();
            }
        }
    }
}
