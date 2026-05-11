const cloud = require('wx-server-sdk')

cloud.init({
    env: cloud.DYNAMIC_CURRENT_ENV
})

const db = cloud.database()

// 玩家存档集合。
// 每个 openid 只保留一条最新快照，避免客户端直接接触数据库权限。
const playerSaves = db.collection('sgdd_user')

// 每日一关排行榜集合。
// 每个玩家每天最多一条记录，文档 Id 使用 `${dateKey}_${openid}`，这里只保存分数与达成时间。
// 昵称、头像、头像框统一通过 openid 读取 sgdd_user.snapshot，避免榜单资料和玩家存档产生两份来源。
const leaderboard = db.collection('sgdd_daily_challenge_leaderboard')

// 玩家昵称递增计数器文档 Id。
// 计数器保存在 sgdd_counters 集合中，用事务保证并发新建账号时不会生成重复昵称。
const playerNameCounterId = 'playerName'

// 每日榜只返回前 100 名。
// 客户端 Content 对象池也按 100 个条目创建，云端和客户端容量保持一致。
const leaderboardTopLimit = 100

// 玩家资料批量查询每批最多 100 条。
// 今日榜前 100 再额外补当前玩家记录时，理论上可能出现 101 个 openid，因此需要拆批查询。
const playerProfileQueryBatchSize = 100

// 服务端初始存档模板。
// 新账号首次建档时只使用这里的数据，不再信任客户端传来的编辑器快照，方便在云函数内直接改数值测试。
const initialSnapshotTemplate = {
    currentGold: 0,
    pendingOfflineEarningGold: 0,
    currentStars: 60,
    hasClaimedNewcomerPackage: false,
    playerName: '',
    playerCode: '',
    dailyChallengeHistoricalBestScore: 0,
    dailyChallengeHistoricalBestTime: '',
    selectedHeadPortraitCode: 'head_portrait_001',
    selectedHeadPortraitFrameCode: 'head_portrait_frame_001',
    clientSaveTime: '',
    unlockedFruitCodes: [
        'fruit_apple',
        'fruit_banana',
        'fruit_strawberry',
        'fruit_corn',
        'fruit_berry',
        'fruit_grape',
        'fruit_kiwi',
        'fruit_wax_apple',
        'fruit_lemon',
        'fruit_avocado',
    ],
    unlockedPetCodes: [],
    unlockedProduceCodes: [],
    unlockedHeadPortraitCodes: [
        'head_portrait_001',
    ],
    unlockedHeadPortraitFrameCodes: [
        'head_portrait_frame_001',
    ],
    produceCounts: [],
    eggHatch: {
        manualEggCodes: [
        ],
        refillElapsedSeconds: 0,
        slots: [
            { eggCode: '', totalSeconds: 0, remainingSeconds: 0 },
            { eggCode: '', totalSeconds: 0, remainingSeconds: 0 },
            { eggCode: '', totalSeconds: 0, remainingSeconds: 0 },
            { eggCode: '', totalSeconds: 0, remainingSeconds: 0 },
        ],
    },
    architectures: [
        {
            category: 'Hatch',
            slots: [
                { isUnlocked: true, level: 1 },
                { isUnlocked: false, level: 0 },
                { isUnlocked: false, level: 0 },
                { isUnlocked: false, level: 0 },
            ],
        },
        {
            category: 'Diet',
            slots: [
                { isUnlocked: true, level: 1 },
                { isUnlocked: false, level: 0 },
                { isUnlocked: false, level: 0 },
                { isUnlocked: false, level: 0 },
                { isUnlocked: false, level: 0 },
                { isUnlocked: false, level: 0 },
            ],
        },
        {
            category: 'Fruiter',
            slots: [
                { isUnlocked: true, level: 1 },
                { isUnlocked: false, level: 0 },
                { isUnlocked: false, level: 0 },
                { isUnlocked: false, level: 0 },
                { isUnlocked: false, level: 0 },
                { isUnlocked: false, level: 0 },
            ],
        },
        {
            category: 'SavingPot',
            slots: [
                { isUnlocked: true, level: 1 },
            ],
        },
    ],
    pets: [],
    pendingGoldDrops: [],
    pendingProduceDrops: [],
}

// 云函数主入口。
// 客户端通过 event.action 分发到初始化、读取、保存三个业务动作。
exports.main = async (event, context) => {
    const wxContext = cloud.getWXContext()
    const openid = wxContext.OPENID
    const requestEvent = normalizeEvent(event)
    const action = requestEvent && requestEvent.action ? requestEvent.action : ''

    if (!openid) {
        return {
            ok: false,
            errMsg: 'openid is empty'
        }
    }

    try {
        switch (action) {
            case 'initOrLoadSave':
                return await initOrLoadSave(openid, requestEvent.snapshot)

            case 'loadSnapshot':
                return await loadSnapshot(openid)

            case 'saveSnapshot':
                return await saveSnapshot(openid, requestEvent.snapshot)

            case 'loadDailyChallengeLeaderboard':
                return await loadDailyChallengeLeaderboard(openid)

            case 'submitDailyChallengeScore':
                return await submitDailyChallengeScore(openid, requestEvent)

            default:
                return {
                    ok: false,
                    openid,
                    errMsg: `unknown action: ${action}`
                }
        }
    } catch (error) {
        return {
            ok: false,
            openid,
            errMsg: error && error.message ? error.message : String(error)
        }
    }
}

// 兼容 Unity SDK 传参差异。
// 部分 Unity 微信插件示例会把 CallFunctionParam.data 写成 JSON 字符串，这里统一反序列化为对象。
function normalizeEvent(event) {
    if (!event) {
        return {}
    }

    if (typeof event === 'string') {
        try {
            return JSON.parse(event)
        } catch (error) {
            return {}
        }
    }

    if (!event.action && typeof event.data === 'string') {
        try {
            return JSON.parse(event.data)
        } catch (error) {
            return event
        }
    }

    if (!event.action && event.data && typeof event.data === 'object') {
        return event.data
    }

    return event
}

// 首次进入时初始化或读取玩家存档。
// 云端已有数据时直接返回；云端没有数据时使用服务端模板创建新档。
async function initOrLoadSave(openid, initialSnapshot) {
    const existing = await getSaveDocument(openid)
    if (existing) {
        return {
            ok: true,
            created: false,
            openid,
            snapshot: normalizeSnapshot(existing.snapshot || null)
        }
    }

    initialSnapshot = await createInitialSnapshot()
    const now = db.serverDate()
    await playerSaves.add({
        data: {
            openid,
            snapshot: initialSnapshot,
            createdAt: now,
            updatedAt: now
        }
    })

    return {
        ok: true,
        created: true,
        openid,
        snapshot: normalizeSnapshot(initialSnapshot)
    }
}

// 单独读取云端玩家快照。
// 没有存档时返回 snapshot=null，由客户端决定是否使用本地默认初始数据。
async function loadSnapshot(openid) {
    const existing = await getSaveDocument(openid)
    return {
        ok: true,
        created: false,
        openid,
        snapshot: existing ? normalizeSnapshot(existing.snapshot || null) : null
    }
}

// 保存客户端提交的完整玩家快照。
// 同一个 openid 始终覆盖为最新快照，不做多设备冲突合并。
async function saveSnapshot(openid, snapshot) {
    if (!snapshot) {
        return {
            ok: false,
            openid,
            errMsg: 'snapshot is empty'
        }
    }

    const normalizedSnapshot = normalizeSnapshot(snapshot)
    const existing = await getSaveDocument(openid)
    const now = db.serverDate()
    if (existing) {
        const existingSnapshot = normalizeSnapshot(existing.snapshot || null)
        preservePlayerIdentity(normalizedSnapshot, existingSnapshot)
        preserveDailyChallengeHistoricalBest(normalizedSnapshot, existingSnapshot)
        await playerSaves.doc(existing._id).update({
            data: {
                snapshot: normalizedSnapshot,
                updatedAt: now
            }
        })
    } else {
        await playerSaves.add({
            data: {
                openid,
                snapshot: normalizedSnapshot,
                createdAt: now,
                updatedAt: now
            }
        })
    }

    return {
        ok: true,
        created: false,
        openid,
        snapshot: normalizedSnapshot
    }
}

// 保存完整快照时保护历史最高分。
// 自动云存档和排行榜提交可能并行返回；如果旧快照分数更低，不允许覆盖云端已经确认的历史最高分。
function preserveDailyChallengeHistoricalBest(targetSnapshot, existingSnapshot) {
    if (!targetSnapshot || !existingSnapshot) {
        return
    }

    const targetBestScore = normalizeNonNegativeInteger(targetSnapshot.dailyChallengeHistoricalBestScore, 0)
    const existingBestScore = normalizeNonNegativeInteger(existingSnapshot.dailyChallengeHistoricalBestScore, 0)
    if (existingBestScore > targetBestScore) {
        targetSnapshot.dailyChallengeHistoricalBestScore = existingBestScore
        targetSnapshot.dailyChallengeHistoricalBestTime = normalizeString(existingSnapshot.dailyChallengeHistoricalBestTime)
    }
}

// 创建一份独立的服务端初始存档。
// 这里使用 JSON 深拷贝，避免后续 normalize 或保存逻辑误改全局模板对象。
async function createInitialSnapshot() {
    const snapshot = JSON.parse(JSON.stringify(initialSnapshotTemplate))
    const playerIdentity = await allocatePlayerIdentity()
    snapshot.playerName = playerIdentity.playerName
    snapshot.playerCode = playerIdentity.playerCode
    return snapshot
}

// 归一化快照中的关键基础字段。
// 云开发控制台手动改测试数据时，金币/星星可能被填成字符串；这里统一转成非负整数，避免 Unity JsonUtility 读取成 0。
function normalizeSnapshot(snapshot) {
    if (!snapshot) {
        return null
    }

    snapshot.currentGold = normalizeNonNegativeInteger(snapshot.currentGold, initialSnapshotTemplate.currentGold)
    snapshot.pendingOfflineEarningGold = normalizeNonNegativeInteger(snapshot.pendingOfflineEarningGold, initialSnapshotTemplate.pendingOfflineEarningGold)
    snapshot.currentStars = normalizeNonNegativeInteger(snapshot.currentStars, initialSnapshotTemplate.currentStars)
    snapshot.hasClaimedNewcomerPackage = normalizeBoolean(snapshot.hasClaimedNewcomerPackage, initialSnapshotTemplate.hasClaimedNewcomerPackage)
    snapshot.playerName = normalizeString(snapshot.playerName)
    snapshot.playerCode = normalizePlayerCode(snapshot.playerCode)
    snapshot.dailyChallengeHistoricalBestScore = normalizeNonNegativeInteger(snapshot.dailyChallengeHistoricalBestScore, 0)
    snapshot.dailyChallengeHistoricalBestTime = normalizeString(snapshot.dailyChallengeHistoricalBestTime)
    snapshot.selectedHeadPortraitCode = normalizeString(snapshot.selectedHeadPortraitCode)
    snapshot.selectedHeadPortraitFrameCode = normalizeString(snapshot.selectedHeadPortraitFrameCode)
    snapshot.eggHatch = normalizeEggHatch(snapshot.eggHatch)
    return snapshot
}

// 分配新的玩家身份。
// 玩家编号固定为 10 位补零序号，例如 0000000001；昵称固定为“玩家”+ 玩家编号。
// 这里必须放在云函数事务里执行，否则多个新账号同时创建时可能读到同一个计数值。
async function allocatePlayerIdentity() {
    const sequence = await db.runTransaction(async transaction => {
        const counterRef = transaction.collection('sgdd_counters').doc(playerNameCounterId)
        let currentValue = 0
        try {
            const counterResult = await counterRef.get()
            currentValue = normalizeNonNegativeInteger(counterResult && counterResult.data ? counterResult.data.value : 0, 0)
        } catch (error) {
            currentValue = 0
        }

        const nextValue = currentValue + 1
        await counterRef.set({
            data: {
                value: nextValue,
                updatedAt: db.serverDate()
            }
        })
        return nextValue
    }, 5)

    const playerCode = formatPlayerCode(sequence)
    return {
        playerName: `玩家${playerCode}`,
        playerCode
    }
}

// 保存完整快照时保护玩家固定身份。
// playerCode 是服务端分配的稳定编号，不允许后续异常快照覆盖为空。
function preservePlayerIdentity(targetSnapshot, existingSnapshot) {
    if (!targetSnapshot) {
        return
    }

    const existingPlayerName = existingSnapshot ? normalizeString(existingSnapshot.playerName) : ''
    const existingPlayerCode = existingSnapshot ? normalizePlayerCode(existingSnapshot.playerCode) : ''
    if (existingPlayerName && !normalizeString(targetSnapshot.playerName)) {
        targetSnapshot.playerName = existingPlayerName
    }

    if (existingPlayerCode) {
        targetSnapshot.playerCode = existingPlayerCode
        return
    }

    targetSnapshot.playerCode = normalizePlayerCode(targetSnapshot.playerCode)
}

// 把递增序号格式化成固定 10 位玩家编号。
function formatPlayerCode(sequence) {
    return String(normalizeNonNegativeInteger(sequence, 0)).padStart(10, '0')
}

// 标准化玩家编号。
// 只保留数字并固定为最后 10 位，兼容控制台误填“玩家0000000001”等情况。
function normalizePlayerCode(value) {
    const text = normalizeString(value)
    if (!text) {
        return ''
    }

    const digits = text.replace(/\D/g, '')
    if (!digits) {
        return ''
    }

    return digits.slice(-10).padStart(10, '0')
}

// 读取今日每日一关排行榜。
// 只读服务端北京时间对应 dateKey 的榜单，客户端时间不参与每日重置判断。
async function loadDailyChallengeLeaderboard(openid) {
    return await buildDailyChallengeLeaderboardResponse(openid)
}

// 提交每日一关结算分数。
// 只有本次分数超过玩家今日已记录最高分时，才刷新 score 和 scoreAchievedAt。
// 相同分数不会刷新达成时间，保证“同分先达成者靠前”的排序规则稳定。
async function submitDailyChallengeScore(openid, requestEvent) {
    const score = normalizeNonNegativeInteger(requestEvent.score, 0)
    const saveDocument = await getSaveDocument(openid)
    if (!saveDocument || !saveDocument.snapshot) {
        return {
            ok: false,
            openid,
            errMsg: 'player save is empty'
        }
    }

    const snapshot = normalizeSnapshot(saveDocument.snapshot)
    const dateKey = getTodayDateKey()
    const now = db.serverDate()
    const recordId = getLeaderboardRecordId(openid, dateKey)
    const existingRecord = await getTodayLeaderboardRecord(openid, dateKey)
    const shouldReplaceScore = !existingRecord || score > normalizeNonNegativeInteger(existingRecord.score, 0)

    // 只有刷新今日最高分时才写入新的达成时间。
    if (existingRecord) {
        if (shouldReplaceScore) {
            await leaderboard.doc(recordId).update({
                data: {
                    score,
                    scoreAchievedAt: now,
                    updatedAt: now
                }
            })
        }
    } else {
        await leaderboard.doc(recordId).set({
            data: {
                dateKey,
                openid,
                score,
                scoreAchievedAt: now,
                updatedAt: now
            }
        })
    }

    // 历史最高分记录在玩家快照里，方便客户端入口界面直接展示。
    // 此处只在本次分数更高时写入，避免低分结算覆盖已达成的历史最高分。
    if (score > normalizeNonNegativeInteger(snapshot.dailyChallengeHistoricalBestScore, 0)) {
        snapshot.dailyChallengeHistoricalBestScore = score
        snapshot.dailyChallengeHistoricalBestTime = new Date().toISOString()
        await playerSaves.doc(saveDocument._id).update({
            data: {
                snapshot,
                updatedAt: now
            }
        })
    }

    return await buildDailyChallengeLeaderboardResponse(openid)
}

// 组装每日一关排行榜响应。
// 返回内容包括：今日前 100、当前玩家今日记录、今日最高分、历史最高分。
async function buildDailyChallengeLeaderboardResponse(openid) {
    const dateKey = getTodayDateKey()
    const saveDocument = await getSaveDocument(openid)
    const snapshot = saveDocument && saveDocument.snapshot ? normalizeSnapshot(saveDocument.snapshot) : null

    // 排序规则：
    // 1. score 降序，分数越高排名越靠前；
    // 2. scoreAchievedAt 升序，同分时越早达成排名越靠前。
    const topResult = await leaderboard
        .where({ dateKey })
        .orderBy('score', 'desc')
        .orderBy('scoreAchievedAt', 'asc')
        .limit(leaderboardTopLimit)
        .get()
    const topRecords = topResult && topResult.data ? topResult.data : []
    let myRecord = null
    let myRank = 0

    for (let i = 0; i < topRecords.length; i++) {
        if (normalizeString(topRecords[i].openid) === openid) {
            myRecord = topRecords[i]
            myRank = i + 1
            break
        }
    }

    // 当前玩家不在前 100 时，仍额外读取自己的今日记录。
    // 客户端 GoMy 会显示“未上榜”，但头像、昵称、今日最高分仍正常展示。
    if (!myRecord) {
        myRecord = await getTodayLeaderboardRecord(openid, dateKey)
    }

    const profileRecords = topRecords.slice()
    if (myRecord && myRank <= 0) {
        profileRecords.push(myRecord)
    }

    const profileMap = await buildPlayerProfileMap(profileRecords)
    const entries = []
    for (let i = 0; i < topRecords.length; i++) {
        const recordOpenid = normalizeString(topRecords[i].openid)
        entries.push(normalizeLeaderboardEntry(topRecords[i], i + 1, profileMap[recordOpenid]))
    }

    const myProfile = profileMap[openid] || normalizePlayerProfile(snapshot)
    const myEntry = myRecord
        ? normalizeLeaderboardEntry(myRecord, myRank, myProfile)
        : normalizeLeaderboardEntry({
            openid,
            score: 0,
            scoreAchievedAt: ''
        }, 0, myProfile)

    return {
        ok: true,
        openid,
        dateKey,
        entries,
        myEntry,
        todayBestScore: myEntry ? myEntry.score : 0,
        historicalBestScore: snapshot ? normalizeNonNegativeInteger(snapshot.dailyChallengeHistoricalBestScore, 0) : 0,
        historicalBestTime: snapshot ? normalizeString(snapshot.dailyChallengeHistoricalBestTime) : ''
    }
}

// 批量构建 openid 到玩家展示资料的映射。
// 榜单记录只保存分数，展示层需要在返回前从 sgdd_user.snapshot 补齐昵称、头像和头像框。
async function buildPlayerProfileMap(records) {
    const profileMap = Object.create(null)
    if (!Array.isArray(records) || records.length <= 0) {
        return profileMap
    }

    const openids = []
    const openidSet = Object.create(null)
    for (let i = 0; i < records.length; i++) {
        const recordOpenid = normalizeString(records[i] ? records[i].openid : '')
        if (!recordOpenid || openidSet[recordOpenid]) {
            continue
        }

        openidSet[recordOpenid] = true
        openids.push(recordOpenid)
    }

    if (openids.length <= 0) {
        return profileMap
    }

    for (let startIndex = 0; startIndex < openids.length; startIndex += playerProfileQueryBatchSize) {
        const batchOpenids = openids.slice(startIndex, startIndex + playerProfileQueryBatchSize)
        const command = db.command
        const queryResult = await playerSaves
            .where({
                openid: command.in(batchOpenids)
            })
            .limit(batchOpenids.length)
            .get()
        const saveDocuments = queryResult && queryResult.data ? queryResult.data : []
        for (let i = 0; i < saveDocuments.length; i++) {
            const saveDocument = saveDocuments[i]
            const saveOpenid = normalizeString(saveDocument ? saveDocument.openid : '')
            if (!saveOpenid) {
                continue
            }

            profileMap[saveOpenid] = normalizePlayerProfile(saveDocument.snapshot)
        }
    }

    for (let i = 0; i < openids.length; i++) {
        if (!profileMap[openids[i]]) {
            profileMap[openids[i]] = normalizePlayerProfile(null)
        }
    }

    return profileMap
}

// 从玩家存档快照提取排行榜展示资料。
// 快照不存在时使用安全兜底值，保证客户端 JsonUtility 始终能读到字符串字段。
function normalizePlayerProfile(snapshot) {
    return {
        playerName: snapshot ? normalizeString(snapshot.playerName) || '玩家' : '玩家',
        headPortraitCode: snapshot ? normalizeString(snapshot.selectedHeadPortraitCode) || initialSnapshotTemplate.selectedHeadPortraitCode : initialSnapshotTemplate.selectedHeadPortraitCode,
        headPortraitFrameCode: snapshot ? normalizeString(snapshot.selectedHeadPortraitFrameCode) || initialSnapshotTemplate.selectedHeadPortraitFrameCode : initialSnapshotTemplate.selectedHeadPortraitFrameCode
    }
}

// 获取当前玩家今日排行榜记录。
// 由于文档 Id 固定为 `${dateKey}_${openid}`，这里可以直接 doc 读取，避免 where 查询额外扫描。
async function getTodayLeaderboardRecord(openid, dateKey) {
    try {
        const result = await leaderboard.doc(getLeaderboardRecordId(openid, dateKey)).get()
        return result && result.data ? result.data : null
    } catch (error) {
        return null
    }
}

// 生成玩家每日榜记录文档 Id。
// dateKey 放在前面，便于云开发控制台人工排查同一天的数据。
function getLeaderboardRecordId(openid, dateKey) {
    return `${dateKey}_${openid}`
}

// 标准化排行榜单条记录。
// 云数据库 Date 类型返回到客户端前统一转成字符串，避免 Unity JsonUtility 解析复杂对象失败。
function normalizeLeaderboardEntry(record, rank, profile) {
    const normalizedProfile = profile || normalizePlayerProfile(null)
    return {
        rank,
        openid: normalizeString(record.openid),
        playerName: normalizedProfile.playerName,
        headPortraitCode: normalizedProfile.headPortraitCode,
        headPortraitFrameCode: normalizedProfile.headPortraitFrameCode,
        score: normalizeNonNegativeInteger(record.score, 0),
        scoreAchievedAt: formatCloudDate(record.scoreAchievedAt)
    }
}

// 获取服务端今日日期键。
// 使用北京时间 YYYYMMDD，确保每日榜重置只受云函数服务器时间影响。
function getTodayDateKey() {
    const beijingNow = new Date(Date.now() + 8 * 60 * 60 * 1000)
    return beijingNow.toISOString().slice(0, 10).replace(/-/g, '')
}

// 将云数据库时间字段转成字符串。
// 兼容 Date、字符串、带 $date 字段的对象以及其他兜底类型。
function formatCloudDate(value) {
    if (!value) {
        return ''
    }

    if (value instanceof Date) {
        return value.toISOString()
    }

    if (typeof value === 'string') {
        return value
    }

    if (value.$date) {
        return String(value.$date)
    }

    return String(value)
}

// 归一化孵化运行时存档。
// 字段不可用时使用服务端模板，避免客户端读到空库存。
function normalizeEggHatch(eggHatch) {
    if (!eggHatch || typeof eggHatch !== 'object') {
        return JSON.parse(JSON.stringify(initialSnapshotTemplate.eggHatch))
    }

    if (!Array.isArray(eggHatch.manualEggCodes)) {
        eggHatch.manualEggCodes = []
    }

    eggHatch.refillElapsedSeconds = normalizeNonNegativeNumber(eggHatch.refillElapsedSeconds, 0)
    if (!Array.isArray(eggHatch.slots)) {
        eggHatch.slots = JSON.parse(JSON.stringify(initialSnapshotTemplate.eggHatch.slots))
    }

    for (let i = 0; i < eggHatch.slots.length; i++) {
        const slot = eggHatch.slots[i]
        if (!slot || typeof slot !== 'object') {
            eggHatch.slots[i] = { eggCode: '', totalSeconds: 0, remainingSeconds: 0 }
            continue
        }

        slot.eggCode = typeof slot.eggCode === 'string' ? slot.eggCode : ''
        slot.totalSeconds = normalizeNonNegativeNumber(slot.totalSeconds, 0)
        slot.remainingSeconds = normalizeNonNegativeNumber(slot.remainingSeconds, 0)
    }

    return eggHatch
}

// 解析非负整数。
// value 非法时返回 fallback，避免手动测试数据写错导致客户端进度被清零。
function normalizeNonNegativeInteger(value, fallback) {
    const numberValue = Number(value)
    if (!Number.isFinite(numberValue) || numberValue < 0) {
        return fallback
    }

    return Math.floor(numberValue)
}

// 解析非负数。
// 用于补蛋进度和孵化剩余秒数，保留小数以避免读档时倒计时抖动。
function normalizeNonNegativeNumber(value, fallback) {
    const numberValue = Number(value)
    if (!Number.isFinite(numberValue) || numberValue < 0) {
        return fallback
    }

    return numberValue
}

// 解析布尔值。
// 兼容云开发控制台手动输入 true/false、1/0、'true'/'false' 的情况。
function normalizeBoolean(value, fallback) {
    if (typeof value === 'boolean') {
        return value
    }

    if (typeof value === 'number') {
        return value !== 0
    }

    if (typeof value === 'string') {
        const lowerValue = value.trim().toLowerCase()
        if (lowerValue === 'true' || lowerValue === '1') {
            return true
        }

        if (lowerValue === 'false' || lowerValue === '0') {
            return false
        }
    }

    return fallback
}

// 将值规范化为字符串，若非字符串则返回空字符串，并在遇到字符串时去除首尾空白。
function normalizeString(value) {
    return typeof value === 'string' ? value.trim() : ''
}

// 按 openid 查询当前玩家存档。
// 该集合设计为 openid 唯一；如果历史上意外产生多条，这里只取第一条。
async function getSaveDocument(openid) {
    const queryResult = await playerSaves.where({ openid }).limit(1).get()
    if (!queryResult || !queryResult.data || queryResult.data.length <= 0) {
        return null
    }

    return queryResult.data[0]
}