using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 宠物站位运行时模块 — 云存档导出与恢复部分。
/// 云存档只保存宠物 Code 与剩余吃饭次数，读取后不恢复餐桌、队列、点餐和动画状态。
/// </summary>
public sealed partial class PetPlacementModule
{
    /// <summary>
    /// 导出当前仍应跨会话保留的宠物轻量数据。
    /// 剩余吃饭次数小于等于 0 的宠物会被过滤掉，避免已经吃完的宠物重进后复活。
    /// </summary>
    /// <returns>宠物轻量存档数组。</returns>
    public PetLiteSaveData[] ExportPetLiteSaveData()
    {
        if (_petStates.Count <= 0)
        {
            return Array.Empty<PetLiteSaveData>();
        }

        List<PetLiteSaveData> results = new List<PetLiteSaveData>(_petStates.Count);
        foreach (KeyValuePair<int, PetRuntimeState> pair in _petStates)
        {
            PetRuntimeState petState = pair.Value;
            if (petState == null
                || string.IsNullOrWhiteSpace(petState.PetCode)
                || petState.RemainingEatFruitCount <= 0)
            {
                continue;
            }

            results.Add(new PetLiteSaveData
            {
                petCode = petState.PetCode ?? string.Empty,
                remainingEatFruitCount = petState.RemainingEatFruitCount
            });
        }

        return results.ToArray();
    }

    /// <summary>
    /// 从云端轻量宠物数据恢复当前场上宠物。
    /// 所有宠物都会先重建到游玩区，随后沿用 PlayArea 固定停留秒数后的原有分流逻辑。
    /// </summary>
    /// <param name="pets">云端保存的宠物轻量数据。</param>
    public void ApplyPetLiteSaveData(PetLiteSaveData[] pets)
    {
        ClearAllRuntimePetsForCloudLoad();
        if (pets == null || pets.Length <= 0)
        {
            NotifyPlacementChanged();
            return;
        }

        int playAreaCount = ResolveCloudLoadPlayAreaCount();

        for (int i = 0; i < pets.Length; i++)
        {
            PetLiteSaveData saveData = pets[i];
            if (saveData == null
                || string.IsNullOrWhiteSpace(saveData.petCode)
                || saveData.remainingEatFruitCount <= 0)
            {
                continue;
            }

            PetDataRow petDataRow = GameEntry.DataTables != null
                ? GameEntry.DataTables.GetDataRowByCode<PetDataRow>(saveData.petCode)
                : null;
            if (petDataRow == null)
            {
                Log.Warning("PetPlacementModule 读取云存档时跳过无效宠物 Code '{0}'。", saveData.petCode);
                continue;
            }

            int instanceId = AcquireInstanceId();
            if (instanceId <= 0)
            {
                continue;
            }

            int playAreaIndex = UnityEngine.Random.Range(0, playAreaCount);
            PetRuntimeState petState = new PetRuntimeState
            {
                InstanceId = instanceId,
                PetCode = saveData.petCode,
                Quality = petDataRow.Quality,
                PlacementType = PetPlacementType.PlayArea,
                SlotIndex = playAreaIndex,
                DesiredFruitCode = null,
                DiningWishState = PetDiningWishState.None,
                RemainingDiningStageSeconds = 0f,
                PlayAreaIndex = playAreaIndex,
                PlayAreaRandomPosition01 = new Vector2(UnityEngine.Random.value, UnityEngine.Random.value),
                RemainingPostMealSeconds = 0f,
                PendingSpawnHatchSlotIndex = -1,
                OrchardSlotIndex = -1,
                PendingPromoteToDining = false,
                RemainingEatFruitCount = saveData.remainingEatFruitCount
            };

            _petStates.Add(instanceId, petState);
            GameEntry.Fruits?.TryUnlockPet(saveData.petCode);
        }

        NotifyPlacementChanged();
    }

    /// <summary>
    /// 根据蛋配置抽取一只宠物，并像云存档宠物恢复一样直接放入游玩区。
    /// 该入口专供离线孵化结算使用，不设置 PendingSpawnHatchSlotIndex，避免宠物从孵化槽播放出生移动动画。
    /// </summary>
    /// <param name="eggCode">已经离线孵化完成的蛋 Code。</param>
    /// <param name="petState">成功生成的宠物运行时状态。</param>
    /// <returns>成功生成并登记宠物返回 true。</returns>
    public bool TryHatchPetFromEggCodeToPlayArea(string eggCode, out PetRuntimeState petState)
    {
        petState = null;
        if (string.IsNullOrWhiteSpace(eggCode))
        {
            Log.Warning("PetPlacementModule can not hatch offline pet because egg code is empty.");
            return false;
        }

        if (GameEntry.DataTables == null
            || !GameEntry.DataTables.IsAvailable<EggDataRow>()
            || !GameEntry.DataTables.IsAvailable<PetDataRow>())
        {
            Log.Warning("PetPlacementModule can not hatch offline pet because required data tables are unavailable.");
            return false;
        }

        EggDataRow eggDataRow = GameEntry.DataTables.GetDataRowByCode<EggDataRow>(eggCode);
        if (eggDataRow == null)
        {
            Log.Warning("PetPlacementModule can not hatch offline pet because egg '{0}' can not be found.", eggCode);
            return false;
        }

        if (!TryRollPetQuality(eggDataRow, out QualityType petQuality))
        {
            Log.Warning("PetPlacementModule can not hatch offline pet because egg '{0}' failed to roll quality.", eggCode);
            return false;
        }

        if (!TryPickPetCodeByQuality(petQuality, out string petCode))
        {
            return false;
        }

        int instanceId = AcquireInstanceId();
        if (instanceId <= 0)
        {
            Log.Error("PetPlacementModule can not hatch offline pet because instance id is invalid.");
            return false;
        }

        int playAreaCount = ResolveCloudLoadPlayAreaCount();
        int playAreaIndex = UnityEngine.Random.Range(0, playAreaCount);
        petState = new PetRuntimeState
        {
            InstanceId = instanceId,
            PetCode = petCode,
            Quality = petQuality,
            PlacementType = PetPlacementType.PlayArea,
            SlotIndex = playAreaIndex,
            DesiredFruitCode = null,
            DiningWishState = PetDiningWishState.None,
            RemainingDiningStageSeconds = 0f,
            PlayAreaIndex = playAreaIndex,
            PlayAreaRandomPosition01 = new Vector2(UnityEngine.Random.value, UnityEngine.Random.value),
            RemainingPostMealSeconds = 0f,
            PendingSpawnHatchSlotIndex = -1,
            OrchardSlotIndex = -1,
            PendingPromoteToDining = false,
            RemainingEatFruitCount = ResolveInitialEatFruitCount(petCode)
        };

        _petStates.Add(instanceId, petState);
        GameEntry.Fruits?.TryUnlockPet(petCode);
        NotifyPlacementChanged();
        return true;
    }

    /// <summary>
    /// 计算云读档时可用的 PlayArea 数量。
    /// </summary>
    /// <returns>可用 PlayArea 数量，至少为 1。</returns>
    private static int ResolveCloudLoadPlayAreaCount()
    {
        int playAreaCount = GameEntry.PlayfieldEntities != null ? GameEntry.PlayfieldEntities.PlayAreaCount : 0;
        return playAreaCount > 0 ? playAreaCount : 1;
    }

    /// <summary>
    /// 清理当前所有宠物运行时状态。
    /// 该方法仅用于云存档读取覆盖旧会话状态，不走正常离场动画。
    /// </summary>
    private void ClearAllRuntimePetsForCloudLoad()
    {
        _petStates.Clear();
        Array.Clear(_queueInstanceIds, 0, _queueInstanceIds.Length);
        if (_diningSeatInstanceIds != null && _diningSeatInstanceIds.Length > 0)
        {
            Array.Clear(_diningSeatInstanceIds, 0, _diningSeatInstanceIds.Length);
        }

        if (GameEntry.PlayfieldEntities != null)
        {
            for (int i = 0; i < _diningSeatInstanceIds.Length; i++)
            {
                GameEntry.PlayfieldEntities.HideDiningFruit(i);
            }
        }
    }
}
