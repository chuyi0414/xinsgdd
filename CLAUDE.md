# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

Unity 2022.3.45f1 游戏项目（"sgdd"），使用 URP 渲染管线，目标平台为 PC、移动端及 WebGL（微信小游戏，通过 WX-WASM-SDK-V2）。基于 **Game Framework**（gameframework.cn）构建——一个模块化的 Unity 框架，提供 Entity、UI、Procedure、DataTable、Event、Resource、Sound 等子系统。

核心玩法：孵蛋 → 养宠物 → 生产水果 → 升级建筑 → 每日消除挑战。

## 构建与开发

本项目为 Unity Editor 工程，无 CLI 构建命令。所有构建、测试、运行均在 Unity Editor 内完成。

- **解决方案文件**：`sgdd.sln`（Unity 自动生成，已 gitignore）
- **主游戏程序集**：`Assembly-CSharp.csproj`（自动生成）
- **框架程序集**：`GameFramework.csproj`、`UnityGameFramework.Runtime.csproj`、`UnityGameFramework.Editor.csproj`
- **微信 SDK**：`Wx.csproj`、`WxEditor.csproj`
- **Spine 运行时**：`spine-unity.csproj`、`spine-unity-editor.csproj`

## 架构

### 流程系统（游戏生命周期）

游戏使用 Game Framework 的 **Procedure** 系统作为顶层状态机。流程：

```
LoadProcedure → MainProcedure ⇄ CombatProcedure
```

- **LoadProcedure**（`_Game/Script/Procedures/LoadProcedure.cs`）：打开加载界面，触发 `GameDataTableModule.BeginLoadRequiredDataTables()` 和 `GameAssetModule.BeginPreloadRequiredAssets()`。当两者 `IsReady` 后转入 MainProcedure。
- **MainProcedure**（`_Game/Script/Procedures/MainProcedure.cs`）：打开 MainUIForm。处理新人礼包弹窗。通过 `TransitionToCombat` / `ReturningFromCombat` 数据标记与 CombatProcedure 协调，战斗期间保持 MainUIForm 不销毁。
- **CombatProcedure**（`_Game/Script/Procedures/CombatProcedure.cs`）：打开 CombatUIForm。支持 `HasPropKit`（道具包）和 `RechallengeCombat`（再来一局）标记。

`ProcedureStartupGate` 在 Addressables catalog 初始化完成前阻塞 GF 流程启动。

### GameEntry — 中央访问入口

`GameEntry`（`_Game/Script/Models/GameEntry.cs`）是 `partial class`，分三个文件：

| 文件 | 职责 |
|------|------|
| `GameEntry.cs` | MonoBehaviour 生命周期：Awake 阻塞启动门，Start 初始化内置组件，`OnAddressablesReady` 创建自定义模块 |
| `GameEntry.BuiltinComponents.cs` | GF 内置组件的静态属性（UI、Entity、DataTable、Event、Sound、Resource 等） |
| `GameEntry.CustomComponents.cs` | 游戏自定义模块的静态属性：`Fruits`（PlayerRuntimeModule）、`EggHatch`、`PetPlacement`、`PetDiningOrders`、`Orchards`、`PlayfieldEntities`、`DataTables`、`GameAssets`、`AddressableAssets`、`Advertisement` |

访问方式：`GameEntry.UI`、`GameEntry.Entity`、`GameEntry.Fruits`、`GameEntry.DataTables` 等。

### 资源管理

两套并行系统：

1. **GF Resource 系统** — 处理 DataTable、UI 预制体、Entity 预制体等小资源。通过 `AddressablesAssetRouterImpl` 注入 GF 的 `IResourceManager`，Addressables 未就绪时回退到 `Resources.LoadAsync`。
2. **GameAddressableAssetModule**（`GameEntry.AddressableAssets`）— 处理大体积 Arts/Audio 资源，显式管理句柄生命周期。退出场景时调用 `ReleaseAll()`。

`AssetPath`（`_Game/Script/Core/AssetPath.cs`）统一收口所有资源路径前缀：`Prefabs/UI/`、`Prefabs/Entity/`、`DataTable/`。

### 数据表系统

制表符分隔的 `.txt` 文件，位于 `Assets/_Game/Resources/DataTable/`。格式：`#Id\tCode\tName\t...` 以 `#` 开头的行为表头定义 schema，后续行为数据。

`GameDataTableModule`（`_Game/Script/Models/DataTable/GameDataTableModule.cs`）是唯一入口。**新增数据表步骤**：

1. 在 `Resources/DataTable/` 下创建 `.txt` 文件
2. 创建 `*DataRow` 类，实现 `IDataRow`（若有 Code 列还需实现 `ICodeDataRow`）
3. 在 `RequiredTables` 数组追加 `DataTableEntry`
4. 在 `BeginLoadCore`、`DispatchOnLoadSuccess`、`DispatchClear` 三个 switch 语句中各加一个 `case`
5. 若需自定义校验/预热逻辑，新增 `TryRegister*DataTable` 方法并在 `TryRegisterDispatch` 中接入

### 实体系统

GF 实体是分组管理的动态 GameObject。实体采用 Data/Logic 配对模式：
- `*EntityData` — 可序列化数据，传递给实体
- `*EntityLogic` — 派生自 MonoBehaviour 的行为

实体组：`Item`、`Environment`、`Character`、`EliminateCard`（定义在 `EntityDefine.cs`）。

### UI 系统

GF UI 界面继承 `UIFormLogic`。按功能组织在 `_Game/Script/UI/` 下。大型界面使用 **partial class** 拆分（如 `MainUIForm.cs` + `MainUIForm.Hatch.cs` + `MainUIForm.Gold.cs`，`CombatUIForm.cs` + `CombatUIForm.Score.cs` 等）。

`UIFormDefine`（`_Game/Script/UI/UIFormDefine.cs`）统一收口所有界面资源路径与组名。

UI 组（按深度排列，高深度覆盖低深度）：

| 组名 | 深度 | 用途 |
|------|------|------|
| BJ | 0 | 背景/启动加载 |
| Main | 100 | 常驻主界面与战斗界面 |
| Info | 200 | 规则说明等非阻塞信息 |
| Popup | 300 | 需用户确认的弹窗 |
| Toast | 400 | 轻提示 |
| Guide | 500 | 新手引导遮罩 |
| Loading | 600 | 全屏加载过渡 |
| Top | 700 | 断线重连等必须置顶的界面 |

### 玩家运行时模块

`PlayerRuntimeModule`（`_Game/Script/Models/PlayerRuntimeModule.cs`）是 `partial class`，按领域拆分：

- `.cs` — 核心初始化、建筑状态、枚举定义
- `.Architecture.cs` — 建筑槽位/升级逻辑
- `.Cosmetic.cs` — 头像/头像框管理
- `.Fruit.cs` — 水果目录、解锁/星星追踪
- `.Produce.cs` — 宠物产出目录、随机抽取

UI 层消费**只读快照结构体**（如 `ArchitectureEntryState`），不直接读写内部状态。

### 核心模块一览

| 模块 | 访问方式 | 职责 |
|------|---------|------|
| `EggHatchComponent` | `GameEntry.EggHatch` | 蛋孵化槽位、计时器、品质抽取 |
| `PetPlacementModule` | `GameEntry.PetPlacement` | 宠物站位分配、挑选目录 |
| `PetDiningOrderComponent` | `GameEntry.PetDiningOrders` | 宠物点餐生产流程 |
| `OrchardModule` | `GameEntry.Orchards` | 果园槽位状态 |
| `PlayfieldEntityModule` | `GameEntry.PlayfieldEntities` | 餐桌/果园实体显示绑定 |
| `AdvertisementModule` | `GameEntry.Advertisement` | 激励视频广告生命周期 |
| `EntityIdPoolComponent` | `GameEntry.EntityIdPool` | 实体 ID 分配/回收 |

## 编码规范（摘自 AGENTS.md）

- **Update/LateUpdate 内零 GC**：禁止装箱/拆箱、闭包、LINQ、`new`、字符串拼接。强制使用 Non-Alloc API 并缓存引用。
- **禁止 SendMessage**：使用接口、事件总线或响应式流。
- **禁止 Resources.Load**：所有资源加载走 Addressables 或 GF Resource 系统。
- **禁止 Coroutine**：使用 UniTask 或 Job System/Burst。
- **禁止单例滥用**：优先 DI 或通过 GameEntry 静态模块访问。
- **全量中文注释**：每个字段、每个方法、每个关键逻辑块必须有中文注释说明用途与注意事项。

## 第三方库

- **Game Framework**（`Assets/GF/`）— 核心游戏框架（gameframework.cn）
- **Spine**（`Assets/Spine/`）— 骨骼动画运行时
- **DOTween**（`Assets/DOTween/`）— 缓动动画库
- **WX-WASM-SDK-V2**（`Assets/WX-WASM-SDK-V2/`）— 微信小游戏 WebGL SDK
- **TextMesh Pro** — UI 文本渲染
- **Addressables 1.22.3** — 资源管理
