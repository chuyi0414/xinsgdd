using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 设施详情弹窗面板。
/// 挂载在 ArchitectureMenuUIForm 预制体内的详情子物体上，
/// Inspector 中拖入对应的 UI 字段即可，ArchitectureMenuUIForm 只引用本脚本一个入口。
///
/// 【Inspector 配置步骤】
/// 1. 在 ArchitectureMenuUIForm 预制体下新建详情面板子物体（建议命名 GoDetailPanel），挂载本脚本。
/// 2. 将所有 UI 子节点拖入对应的 SerializeField 槽位。
/// 3. ArchitectureMenuUIForm 的 _architectureDetailPanel 会自动找到本组件。
///
/// 【显隐约定】
/// - 未解锁：显示 _goRequiredStars / _goRequiredGold / _btnUnlock，隐藏 _goUnlockedImage。
/// - 已解锁：隐藏 _goRequiredStars / _goRequiredGold / _btnUnlock，显示 _goUnlockedImage。
/// </summary>
public sealed class ArchitectureDetailPanel : MonoBehaviour
{
    // ───────────── 运行时状态 ─────────────

    /// <summary>当前正在展示的建筑类别。</summary>
    private PlayerRuntimeModule.ArchitectureCategory _category;

    /// <summary>当前正在展示的 1 基槽位索引。</summary>
    private int _slotIndex;

    /// <summary>当前正在展示的等级（1 基，1 = 初始解锁级）。</summary>
    private int _level;

    /// <summary>当前设施名称（由调用方传入，如"孵化器"、"餐厅"等）。</summary>
    private string _facilityName;

    // ───────────── Inspector 拖入字段 ─────────────

    /// <summary>详情面板根节点（控制整体显隐，Inspector 拖入详情面板自身即可）。</summary>
    [SerializeField] private GameObject _goRoot;

    /// <summary>关闭详情面板按钮。</summary>
    [SerializeField] private Button _btnClose;

    /// <summary>设施图片（Image 组件，显示 ArchitectureDataRow.IndicatorSpritePath 对应的精灵）。</summary>
    [SerializeField] private Image _imgFacility;

    /// <summary>设施名称文本（ArchitectureSlotDataRow.Description 或 ArchitectureUpgradeDataRow.Description）。</summary>
    [SerializeField] private TextMeshProUGUI _txtName;

    /// <summary>设施介绍文本（当前与 _txtName 同源，可独立扩展为长描述）。</summary>
    [SerializeField] private TextMeshProUGUI _txtDescription;

    /// <summary>设施效果文本（EffectParam：孵化区为产能加成、饮食区为金币加成、农场区为时长缩减）。</summary>
    [SerializeField] private TextMeshProUGUI _txtEffect;

    /// <summary>解锁获得星星数量文本（RewardStars）。</summary>
    [SerializeField] private TextMeshProUGUI _txtRewardStars;

    /// <summary>每分钟存钱数量文本（SaveGold，来自 ArchitectureSlotDataRow）。</summary>
    [SerializeField] private TextMeshProUGUI _txtSaveGold;

    /// <summary>需要多少星星解锁的容器（未解锁时显示，已解锁时隐藏；内含 TextMeshProUGUI）。</summary>
    [SerializeField] private GameObject _goRequiredStars;

    /// <summary>需要多少金币解锁的容器（未解锁时显示，已解锁时隐藏；内含 TextMeshProUGUI）。</summary>
    [SerializeField] private GameObject _goRequiredGold;

    /// <summary>已解锁图片容器（已解锁时显示，未解锁时隐藏；内含 Image）。</summary>
    [SerializeField] private GameObject _goUnlockedImage;

    /// <summary>解锁按钮（未解锁时显示，已解锁时隐藏）。</summary>
    [SerializeField] private Button _btnUnlock;

    // ───────────── 对外回调 ─────────────

    /// <summary>
    /// 点击解锁按钮时由 ArchitectureMenuUIForm 赋值的回调。
    /// 参数：(category, slotIndex)；面板本身不执行解锁逻辑，只负责通知父级。
    /// </summary>
    public Action<PlayerRuntimeModule.ArchitectureCategory, int> OnUnlockRequested;

    // ───────────── 对外 API ─────────────

    /// <summary>
    /// 显示设施详情面板。
    /// </summary>
    /// <param name="category">建筑类别。</param>
    /// <param name="slotIndex">1 基槽位索引。</param>
    /// <param name="level">要展示的等级（1 基，1 = 初始解锁级）。</param>
    /// <param name="facilityName">设施中文名（如"孵化器"），用于拼接标题。</param>
    public void Show(PlayerRuntimeModule.ArchitectureCategory category, int slotIndex, int level, string facilityName)
    {
        _category = category;
        _slotIndex = slotIndex;
        _level = level;
        _facilityName = facilityName;

        if (_goRoot != null) _goRoot.SetActive(true);
        Refresh();
    }

    /// <summary>
    /// 隐藏详情面板并清空回调。
    /// </summary>
    public void Hide()
    {
        if (_goRoot != null) _goRoot.SetActive(false);
        OnUnlockRequested = null;
    }

    // ───────────── 内部刷新 ─────────────

    /// <summary>
    /// 全量刷新详情面板所有字段。
    /// 从 PlayerRuntimeModule 读取当前建筑条目状态，分已解锁/未解锁两路填充 UI。
    /// </summary>
    private void Refresh()
    {
        if (GameEntry.Fruits == null) return;

        PlayerRuntimeModule.ArchitectureEntryState entryState =
            GameEntry.Fruits.GetArchitectureEntryState(_category, _slotIndex);

        // 该等级是否已被解锁：IsUnlocked 为 true 且 当前等级 ≤ 玩家已解锁等级。
        bool isLevelUnlocked = entryState.IsUnlocked && _level <= entryState.Level;

        // 注册/注销解锁按钮监听
        if (_btnUnlock != null)
        {
            _btnUnlock.onClick.RemoveAllListeners();
            _btnUnlock.onClick.AddListener(OnUnlockButtonClicked);
        }

        // 注册关闭按钮监听
        if (_btnClose != null)
        {
            _btnClose.onClick.RemoveAllListeners();
            _btnClose.onClick.AddListener(OnCloseButtonClicked);
        }

        // 切换已解锁/未解锁 UI 显隐
        if (isLevelUnlocked)
        {
            ApplyUnlockedState();
        }
        else
        {
            ApplyLockedState();
        }

        // 填充数据字段
        FillImage();
        FillNameAndDescription();
        FillEffect();
        FillRewardStars();
        FillSaveGold();
        FillRequiredStars();
        FillRequiredGold();
    }

    // ───────────── 显隐切换 ─────────────

    /// <summary>已解锁状态 UI：显示已解锁图片，隐藏解锁相关信息。</summary>
    private void ApplyUnlockedState()
    {
        if (_goUnlockedImage != null) _goUnlockedImage.SetActive(true);
        if (_goRequiredStars != null) _goRequiredStars.SetActive(false);
        if (_goRequiredGold != null) _goRequiredGold.SetActive(false);
        if (_btnUnlock != null) _btnUnlock.gameObject.SetActive(false);
    }

    /// <summary>未解锁状态 UI：隐藏已解锁图片，显示解锁需求与解锁按钮。按钮根据玩家资源是否充足变暗。</summary>
    private void ApplyLockedState()
    {
        if (_goUnlockedImage != null) _goUnlockedImage.SetActive(false);
        if (_goRequiredStars != null) _goRequiredStars.SetActive(true);
        if (_goRequiredGold != null) _goRequiredGold.SetActive(true);
        if (_btnUnlock != null)
        {
            _btnUnlock.gameObject.SetActive(true);

            // 计算解锁所需资源
            int requiredStars = 0;
            int requiredGold  = 0;

            if (_level == 1)
            {
                ArchitectureSlotDataRow slotRow = FindSlotDataRow(_category, _slotIndex);
                if (slotRow != null)
                {
                    requiredStars = slotRow.RequiredStars;
                    requiredGold  = slotRow.UnlockGold;
                }
            }
            else if (_category == PlayerRuntimeModule.ArchitectureCategory.SavingPot)
            {
                SavingPotDataRow savingPotRow = FindSavingPotDataRow(_level);
                if (savingPotRow != null)
                {
                    requiredStars = savingPotRow.RequiredStars;
                    requiredGold  = savingPotRow.UpgradeGold;
                }
            }
            else
            {
                ArchitectureUpgradeDataRow upgradeRow = FindUpgradeDataRow(_category, _slotIndex, _level - 1);
                if (upgradeRow != null)
                {
                    requiredStars = upgradeRow.RequiredStars;
                    requiredGold  = upgradeRow.UpgradeGold;
                }
            }

            // 判断玩家当前资源是否满足解锁条件
            int currentStars = GameEntry.Fruits != null ? GameEntry.Fruits.CurrentStars : 0;
            int currentGold  = GameEntry.Fruits != null ? GameEntry.Fruits.CurrentGold  : 0;
            bool canAfford   = currentStars >= requiredStars && currentGold >= requiredGold;

            // 按钮始终可点击（点击后由父级 Toast 提示不足原因），仅通过图片颜色变暗提示不可解锁
            Image btnImage = _btnUnlock.GetComponent<Image>();
            if (btnImage != null)
            {
                btnImage.color = canAfford ? Color.white : new Color(0.4f, 0.4f, 0.4f, 1f);
            }
        }
    }

    // ───────────── 数据填充 ─────────────

    /// <summary>
    /// 填充设施图片。
    /// 从 ArchitectureDataRow.IndicatorSpritePath 读取精灵路径，
    /// 再通过 GameAssetModule 预加载缓存获取 Sprite。
    /// </summary>
    private void FillImage()
    {
        if (_imgFacility == null || GameEntry.Fruits == null || GameEntry.GameAssets == null) return;

        string spritePath = GameEntry.Fruits.GetIndicatorSpritePath(_category, _level);
        if (!string.IsNullOrEmpty(spritePath) &&
            GameEntry.GameAssets.TryGetArchitectureSprite(spritePath, out Sprite sprite) &&
            sprite != null)
        {
            _imgFacility.sprite = sprite;
        }
    }

    /// <summary>
    /// 填充设施名称。
    /// 格式："{level}级{facilityName}"，如 "2级孵化器"、"3级餐厅"。
    /// _txtDescription 隐藏（当前版本无独立介绍文本字段）。
    /// </summary>
    private void FillNameAndDescription()
    {
        if (_txtName != null)
        {
            if (!string.IsNullOrEmpty(_facilityName))
            {
                _txtName.gameObject.SetActive(true);
                _txtName.SetText(string.Format("{0}级{1}", _level, _facilityName));
            }
            else
            {
                _txtName.gameObject.SetActive(false);
            }
        }

        // 介绍文本：SavingPot 所有等级统一读存钱罐行；其他类别 Level 1 读槽位行，Level 2+ 读升级行
        string desc = null;
        if (_category == PlayerRuntimeModule.ArchitectureCategory.SavingPot)
        {
            SavingPotDataRow savingPotRow = FindSavingPotDataRow(_level);
            if (savingPotRow != null) desc = savingPotRow.Description;
        }
        else if (_level == 1)
        {
            ArchitectureSlotDataRow slotRow = FindSlotDataRow(_category, _slotIndex);
            if (slotRow != null) desc = slotRow.Description;
        }
        else
        {
            ArchitectureUpgradeDataRow upgradeRow = FindUpgradeDataRow(_category, _slotIndex, _level - 1);
            if (upgradeRow != null) desc = upgradeRow.Description;
        }

        if (_txtDescription != null)
        {
            if (!string.IsNullOrEmpty(desc))
            {
                _txtDescription.gameObject.SetActive(true);
                _txtDescription.SetText(string.Format("{0}", desc));
            }
            else
            {
                _txtDescription.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// 填充设施效果文本。
    /// Level 1 无效果，隐藏；
    /// Level 2+ 读取 ArchitectureUpgradeDataRow.EffectParam（或 SavingPotDataRow.EffectParam），
    /// 按建筑类别拼接效果描述：孵化器→"！ 孵蛋时间减少{x}%"，餐厅→"！ 金币加成+{x}"，农场→"！ 生产时间减少{x}%"。
    /// </summary>
    private void FillEffect()
    {
        if (_txtEffect == null) return;

        int effectValue = 0;
        bool hasEffect = false;

        // SavingPot 所有等级（含 Level 1）均有效；其他类别 Level 1 无效果
        if (_category == PlayerRuntimeModule.ArchitectureCategory.SavingPot)
        {
            SavingPotDataRow savingPotRow = FindSavingPotDataRow(_level);
            if (savingPotRow != null)
            {
                effectValue = savingPotRow.EffectParam;
                hasEffect = true;
            }
        }
        else if (_level > 1)
        {
            ArchitectureUpgradeDataRow upgradeRow = FindUpgradeDataRow(_category, _slotIndex, _level - 1);
            if (upgradeRow != null)
            {
                effectValue = upgradeRow.EffectParam;
                hasEffect = true;
            }
        }

        if (hasEffect)
        {
            _txtEffect.gameObject.SetActive(true);
            string effectDesc;
            switch (_category)
            {
                case PlayerRuntimeModule.ArchitectureCategory.Hatch:
                    effectDesc = string.Format("！ 孵蛋时间减少{0}%", effectValue);
                    break;
                case PlayerRuntimeModule.ArchitectureCategory.Diet:
                    effectDesc = string.Format("！ 金币加成+{0}", effectValue);
                    break;
                case PlayerRuntimeModule.ArchitectureCategory.Fruiter:
                    effectDesc = string.Format("！ 生产时间减少{0}%", effectValue);
                    break;
                case PlayerRuntimeModule.ArchitectureCategory.SavingPot:
                    effectDesc = string.Format("！ 存钱上限{0}金币离线收益", effectValue);
                    break;
                default:
                    effectDesc = string.Format("！ 效果+{0}", effectValue);
                    break;
            }
            _txtEffect.SetText(effectDesc);
        }
        else
        {
            _txtEffect.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 填充奖励星星文本。
    /// Level 1 读 ArchitectureSlotDataRow.RewardStars（解锁获得星星）；
    /// Level 2+ 读 ArchitectureUpgradeDataRow.RewardStars（升级到该等级获得的星星）。
    /// 格式："！ 获得{x}个星星"，为 0 时隐藏。
    /// </summary>
    private void FillRewardStars()
    {
        if (_txtRewardStars == null) return;

        int rewardStars = 0;
        bool hasData = false;

        // SavingPot 所有等级读存钱罐行；其他类别 Level 1 读槽位行，Level 2+ 读升级行
        if (_category == PlayerRuntimeModule.ArchitectureCategory.SavingPot)
        {
            SavingPotDataRow savingPotRow = FindSavingPotDataRow(_level);
            if (savingPotRow != null)
            {
                rewardStars = savingPotRow.RewardStars;
                hasData = true;
            }
        }
        else if (_level == 1)
        {
            ArchitectureSlotDataRow slotRow = FindSlotDataRow(_category, _slotIndex);
            if (slotRow != null)
            {
                rewardStars = slotRow.RewardStars;
                hasData = true;
            }
        }
        else
        {
            ArchitectureUpgradeDataRow upgradeRow = FindUpgradeDataRow(_category, _slotIndex, _level - 1);
            if (upgradeRow != null)
            {
                rewardStars = upgradeRow.RewardStars;
                hasData = true;
            }
        }

        if (hasData)
        {
            _txtRewardStars.gameObject.SetActive(true);
            _txtRewardStars.SetText(string.Format("！ 获得{0}个星星", rewardStars));
        }
        else
        {
            _txtRewardStars.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 填充每分钟存钱数量。
    /// Level 1 读 ArchitectureSlotDataRow.SaveGold（基础存钱值）；
    /// Level 2+ 读 ArchitectureUpgradeDataRow.SaveGold（升级到该等级后的存钱值）。
    /// 格式："！ 每分钟存钱 +{x}"，为 0 时隐藏。
    /// </summary>
    private void FillSaveGold()
    {
        if (_txtSaveGold == null) return;

        int saveGold = 0;
        bool hasData = false;

        // SavingPot 没有 SaveGold 字段，直接隐藏；其他类别 Level 1 读槽位行，Level 2+ 读升级行
        if (_category == PlayerRuntimeModule.ArchitectureCategory.SavingPot)
        {
            hasData = false;
        }
        else if (_level == 1)
        {
            ArchitectureSlotDataRow slotRow = FindSlotDataRow(_category, _slotIndex);
            if (slotRow != null)
            {
                saveGold = slotRow.SaveGold;
                hasData = true;
            }
        }
        else
        {
            ArchitectureUpgradeDataRow upgradeRow = FindUpgradeDataRow(_category, _slotIndex, _level - 1);
            if (upgradeRow != null)
            {
                saveGold = upgradeRow.SaveGold;
                hasData = true;
            }
        }

        if (hasData)
        {
            _txtSaveGold.gameObject.SetActive(true);
            _txtSaveGold.SetText(string.Format("！ 每分钟存钱 +{0}", saveGold));
        }
        else
        {
            _txtSaveGold.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 填充解锁需要星星（仅未解锁时由 ApplyLockedState 控制显隐）。
    /// Level 1 读 ArchitectureSlotDataRow.RequiredStars（首次解锁星星门槛）；
    /// Level 2+ 读 ArchitectureUpgradeDataRow.RequiredStars（升级到该等级的星星门槛）。
    /// 格式："需要{x}个星星"，为 0 时不更新文本（由外部控制显隐）。
    /// </summary>
    private void FillRequiredStars()
    {
        if (_goRequiredStars == null) return;

        TextMeshProUGUI text = _goRequiredStars.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text == null) return;

        int requiredStars = 0;
        bool hasData = false;

        // SavingPot 所有等级读存钱罐行；其他类别 Level 1 读槽位行，Level 2+ 读升级行
        if (_category == PlayerRuntimeModule.ArchitectureCategory.SavingPot)
        {
            SavingPotDataRow savingPotRow = FindSavingPotDataRow(_level);
            if (savingPotRow != null)
            {
                requiredStars = savingPotRow.RequiredStars;
                hasData = true;
            }
        }
        else if (_level == 1)
        {
            ArchitectureSlotDataRow slotRow = FindSlotDataRow(_category, _slotIndex);
            if (slotRow != null)
            {
                requiredStars = slotRow.RequiredStars;
                hasData = true;
            }
        }
        else
        {
            ArchitectureUpgradeDataRow upgradeRow = FindUpgradeDataRow(_category, _slotIndex, _level - 1);
            if (upgradeRow != null)
            {
                requiredStars = upgradeRow.RequiredStars;
                hasData = true;
            }
        }

        if (hasData)
        {
            text.SetText(string.Format("需要{0}个星星", requiredStars));
        }
    }

    /// <summary>
    /// 填充解锁需要金币（仅未解锁时由 ApplyLockedState 控制显隐）。
    /// Level 1 读 ArchitectureSlotDataRow.UnlockGold（首次解锁金币花费）；
    /// Level 2+ 读 ArchitectureUpgradeDataRow.UpgradeGold（升级到该等级的金币花费）。
    /// 格式："需要{x}金币解锁升级"。
    /// </summary>
    private void FillRequiredGold()
    {
        if (_goRequiredGold == null) return;

        TextMeshProUGUI text = _goRequiredGold.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text == null) return;

        int costGold = 0;
        bool hasData = false;

        // SavingPot 所有等级读存钱罐行；其他类别 Level 1 读槽位行，Level 2+ 读升级行
        if (_category == PlayerRuntimeModule.ArchitectureCategory.SavingPot)
        {
            SavingPotDataRow savingPotRow = FindSavingPotDataRow(_level);
            if (savingPotRow != null)
            {
                costGold = savingPotRow.UpgradeGold;
                hasData = true;
            }
        }
        else if (_level == 1)
        {
            ArchitectureSlotDataRow slotRow = FindSlotDataRow(_category, _slotIndex);
            if (slotRow != null)
            {
                costGold = slotRow.UnlockGold;
                hasData = true;
            }
        }
        else
        {
            ArchitectureUpgradeDataRow upgradeRow = FindUpgradeDataRow(_category, _slotIndex, _level - 1);
            if (upgradeRow != null)
            {
                costGold = upgradeRow.UpgradeGold;
                hasData = true;
            }
        }

        if (hasData)
        {
            text.SetText(string.Format("需要{0}金币解锁升级", costGold));
        }
    }

    // ───────────── 数据表行查询 ─────────────

    /// <summary>
    /// 查询建筑槽位数据行（Category + SlotIndex 唯一匹配）。
    /// </summary>
    private static ArchitectureSlotDataRow FindSlotDataRow(
        PlayerRuntimeModule.ArchitectureCategory category, int slotIndex)
    {
        if (GameEntry.DataTables == null) return null;

        ArchitectureSlotDataRow[] rows = GameEntry.DataTables.GetAllDataRows<ArchitectureSlotDataRow>();
        if (rows == null) return null;

        for (int i = 0; i < rows.Length; i++)
        {
            ArchitectureSlotDataRow row = rows[i];
            if (row != null && row.Category == category && row.SlotIndex == slotIndex)
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>
    /// 查询建筑升级数据行（Category + SlotIndex + CurrentLevel 唯一匹配）。
    /// </summary>
    private static ArchitectureUpgradeDataRow FindUpgradeDataRow(
        PlayerRuntimeModule.ArchitectureCategory category, int slotIndex, int currentLevel)
    {
        if (GameEntry.DataTables == null) return null;

        ArchitectureUpgradeDataRow[] rows = GameEntry.DataTables.GetAllDataRows<ArchitectureUpgradeDataRow>();
        if (rows == null) return null;

        for (int i = 0; i < rows.Length; i++)
        {
            ArchitectureUpgradeDataRow row = rows[i];
            if (row != null && row.Category == category && row.SlotIndex == slotIndex && row.CurrentLevel == currentLevel)
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>
    /// 查询存钱罐数据行（CurrentLevel 唯一匹配）。
    /// </summary>
    private static SavingPotDataRow FindSavingPotDataRow(int currentLevel)
    {
        if (GameEntry.DataTables == null) return null;

        SavingPotDataRow[] rows = GameEntry.DataTables.GetAllDataRows<SavingPotDataRow>();
        if (rows == null) return null;

        for (int i = 0; i < rows.Length; i++)
        {
            SavingPotDataRow row = rows[i];
            if (row != null && row.CurrentLevel == currentLevel)
            {
                return row;
            }
        }

        return null;
    }

    // ───────────── 按钮回调 ─────────────

    /// <summary>解锁按钮点击：通知父级处理实际解锁逻辑。</summary>
    private void OnUnlockButtonClicked()
    {
        UIInteractionSound.PlayClick();
        OnUnlockRequested?.Invoke(_category, _slotIndex);
    }

    /// <summary>关闭按钮点击：隐藏详情面板。</summary>
    private void OnCloseButtonClicked()
    {
        UIInteractionSound.PlayClick();
        Hide();
    }
}
