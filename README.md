# illuFramework

我的 Unity 个人框架仓库。多包布局：一个模块一个 UPM 包，全部从本仓库安装。

| 包 | 说明 | 依赖 |
| --- | --- | --- |
| [`com.illu.managerofmanagers`](./com.illu.managerofmanagers) | 基础设施：统一纯 C# 与 MonoBehaviour 单例的获取方式与生命周期驱动 | 无 |
| [`com.illu.assetload`](./com.illu.assetload) | `AssetLoadManager`：Resources 单项与批量加载，场景同步和异步加载 | `com.illu.managerofmanagers` |
| [`com.illu.json`](./com.illu.json) | `JsonManager`：存档序列化/反序列化（内置 LitJson） | `com.illu.managerofmanagers` |
| [`com.illu.ui`](./com.illu.ui) | `UIManager + BasePanel`：Canvas 常驻、面板字典、淡入淡出 | `com.illu.managerofmanagers` |
| [`com.illu.audio`](./com.illu.audio) | `AudioManager`：音频库异步预加载、BGM 淡入淡出、12 路 SFX、UI 和 3D 音效 | `com.illu.managerofmanagers`、`com.illu.assetload` |
| [`com.illu.input`](./com.illu.input) | `InputManager` 管理玩法动作、键鼠与手柄方案、改键和按键提示 | `com.illu.managerofmanagers`、`com.unity.inputsystem` |

## 安装（Unity 6）

`Window → Package Manager → + → Add package from git URL`，按依赖顺序添加。使用音频包时，先添加 Core，再添加资源加载包，最后添加音频包。使用输入包时，先添加 Core，再添加输入包。输入包要求 Unity 6000.6，UPM 会解析 Input System 1.20.0 依赖。

```
# 基础包（必装）
https://github.com/illusogl/illuFramework.git?path=/com.illu.managerofmanagers

# 资源加载包（音频包依赖）
https://github.com/illusogl/illuFramework.git?path=/com.illu.assetload

# 功能包（用到哪个装哪个）
https://github.com/illusogl/illuFramework.git?path=/com.illu.json
https://github.com/illusogl/illuFramework.git?path=/com.illu.ui
https://github.com/illusogl/illuFramework.git?path=/com.illu.audio
https://github.com/illusogl/illuFramework.git?path=/com.illu.input
```

通过 Git 安装时，在项目的 `manifest.json` 中为每个依赖包单独登记地址。 [Unity Git 依赖说明](https://docs.unity.com/en-us/engine/6000.0/manual/packages-list/upm-git)

也可以一次写入 `Packages/manifest.json` 的 `dependencies`。下例安装音频和它的两个依赖包。

```json
"com.illu.managerofmanagers": "https://github.com/illusogl/illuFramework.git?path=/com.illu.managerofmanagers",
"com.illu.assetload": "https://github.com/illusogl/illuFramework.git?path=/com.illu.assetload",
"com.illu.audio": "https://github.com/illusogl/illuFramework.git?path=/com.illu.audio"
```

要求：机器装有 git；仓库公开，无需凭证。

## 开发顺序

动手前按 asmdef 梳理引用，先实现并验证底层依赖，再接入上层 Manager，最后接入游戏层。当前音频链按 Core、AssetLoad、Audio、游戏启动流程的顺序开发。

如果某个 Manager 缺少可复用的底层能力，先指出具体缺口和调用场景，再确定是否独立成组件。

## com.illu.managerofmanagers 用法速览

```csharp
// 1. 纯 C# Manager
public sealed class AudioManager : Singleton<AudioManager>
{
    AudioManager() { }                       // 私有构造
    public override int Order => 20;        // Order 必须唯一，忘了写编译不过
}

// 2. MonoBehaviour Manager（场景里手动挂一个）
public sealed class SampleSceneManager : MonoSingleton<SampleSceneManager>
{
    public override int Order => 30;
    protected override void Awake() { base.Awake(); /* 只做身份相关 */ }
}

// 3. 唯一注册表：派生一个 Hub 挂到启动场景
public sealed class GameHub : ManagerHub
{
    protected override void RegisterManagers()
    {
        Register(AudioManager.Instance);
        Register(SampleSceneManager.Instance);
    }
}
```

规则要点：

- 注册表**拓扑固定**：`Register` 是 `protected`，只允许出现在 `RegisterManagers()`；
- `Order` 升序初始化 / 升序 Tick、**降序 Dispose**，同 Order 冲突会直接报错；
- 两段启动：`Awake` 只确身份，注册和 `OnInit` 在 `Start`（所有 `Awake` 已结束）；
- 全项目只有 Hub 一个 `Update` / `LateUpdate`；
- Editor 工具在独立程序集（`includePlatforms: ["Editor"]`），不进打包清单。

### 激活 / 失活（方案 A：自门控）

Manager 常驻注册，但可以暂时不参与逻辑：

```csharp
CombatManager.Instance.SetActive(false);   // 只改标志 + 调 OnDeactivate
CombatManager.Instance.SetActive(true);    // OnActivate；不会重新 OnInit
```

约定：在 `OnTick` 首行检查 `IsActive`，失活期间直接 `return`。

- 不销毁实例、不摘注册表、不重新 `OnInit`；
- `SetActive` 幂等，可来回多次；
- **只影响自己**，不传播给其它 Manager（需要"一关一片"时用分组，不动这里）。

## com.illu.json / com.illu.ui 用法速览

```csharp
using Illu.Json;
using Illu.UI;

// 存档：JsonManager（Order 10，数据层最先就绪）
JsonManager.Instance.SaveData(myData, "SaveFile");
MyData d = JsonManager.Instance.LoadData<MyData>("SaveFile");

// 面板：UIManager（Order 20）
//   约定：Canvas 放在场景里且名为 "Canvas"；面板预制体放 Resources/UI/面板类名
KeySettingPanel p = UIManager.Instance.ShowPanel<KeySettingPanel>();
UIManager.Instance.HidePanel<KeySettingPanel>();

// 自定义面板继承 BasePanel（CanvasGroup 淡入淡出已内置）
public class KeySettingPanel : BasePanel
{
    public override void Init() { }
}
```

## com.illu.assetload 用法速览

把 `AssetLoadManager.Instance` 注册到游戏的 Hub。它的 Order 为 5。Hub 初始化完成后，可以从游戏启动流程或加载面板提交请求。

- 单项资源用 `LoadResource<T>()` 或 `LoadResourceAsync<T>()`。
- 批次用 `LoadResourceBatchAsync<T>()`。等待结算后用 `Assets` 按输入索引取结果，用 `FailureCount` 和 `IsCanceled` 判断批次结果。
- 场景用 `LoadScene()` 或 `LoadSceneAsync()`。同步入口返回 `true` 表示已提交，场景就绪以 `sceneLoaded` 为准。

[资源加载接口、类图与时序图](docs/assetloadmanager/AssetLoadManager.md)

## com.illu.audio 用法速览

注册 `AssetLoadManager.Instance`（Order 5）和 `AudioManager.Instance`（Order 18）。从 `0.2.0` 开始，启动流程要在 Hub 完成初始化后显式预加载默认音频库，例如在 `GameHub.Start()` 调完 `base.Start()` 之后。

```csharp
using Illu.Audio;

// 配置：ScriptableObject 放 Resources/AudioLibrary（组 → key → clip）
// 组：BGM / UI / Player / Enemy / FootStep ...
// 通道：Bgm / Sfx / Ui（决定用哪个源与通道音量）

AudioManager.Instance.LoadDefaultLibraryAsync();                // 由启动流程调用一次
AudioManager.Instance.PlayBgm("Forest");                        // 淡入；切歌自动淡出旧曲
AudioManager.Instance.PlayUi("Click");                          // UI 组 + Ui 通道
AudioManager.Instance.PlaySfx("Player", "Sword_1");             // 2D，声部池并发
AudioManager.Instance.PlaySfxAt(pos, "Enemy", "GetHit_1");      // 3D，池内 voice
AudioManager.Instance.PlaySfxOn(enemySource, "Enemy", "GetHit_1"); // 跟随角色移动的源
AudioManager.Instance.StopAllSfx();
```

- Sfx 是 **12 个 voice 的声部池**：空闲优先，全忙抢占最早开始的一条；
  `ActiveSfxCount` 可查询，**不排队**（排队会让打击音效延迟）。
- 播放入口自行处理库状态。库加载中会暂存最新的 BGM 请求，空库时跳过音效；默认库加载失败时记录一次警告。
- 已有库但缺组或缺键时记录配置错误，保留当前 BGM。
- `AudioManager` 自建 `[Audio]` 对象和播放器。也可以调用 `SetLibrary()` 装入调用方准备好的库。

[音频类图与播放时序图](docs/audiomanager/AudioManager.md)

## com.illu.input 用法速览

在项目中启用 Unity Input System，建立独立的 `Gameplay.inputactions`。在 `GameHub.RegisterManagers()` 中创建并注册 `new InputManager(gameplayActions)`。下面各段在各自对应的游戏流程或界面回调中调用。

```csharp
using Illu.Input;
using UnityEngine;

// Hub 初始化完成后，从玩家存档恢复键位和控制方案。
// 本例的动作资产定义了 Keyboard&Mouse 和 Gamepad 两个方案。
InputManager input = gameHub.Input;
input.LoadBindingOverrides(savedBindingsJson);
if (!input.SetControlScheme(savedScheme))
    input.SetControlScheme("Keyboard&Mouse");
input.SetGameplayEnabled(true);

// 角色就绪时订阅。动作引用由 Inspector 配置。
var moveSubscription = input.SubscribeValue<Vector2>(moveAction, value => moveDirection = value);
var attackSubscription = input.SubscribeButton(attackAction, Attack);

// 打开引导 UI 时查询键名，此处取当前方案的第一条绑定。
var bindings = input.GetBindings(interactAction, input.CurrentControlScheme);
if (bindings.Count > 0)
    hintLabel.text = $"按 {bindings[0].DisplayName} 交互";

// 玩家选择改键时，玩法已经暂停。selectedBinding 来自同一动作的绑定列表。
input.BeginRebind(interactAction, selectedBinding.Id, applied =>
{
    if (applied)
        SaveInputSettings(input.SaveBindingOverrides(), input.CurrentControlScheme);
});

// 下面各行分别在对应的操作发生时执行。
// input.SetGameplayEnabled(false);                  // 暂停玩法
// input.SetGameplayEnabled(true);                   // 玩家选择继续游戏
// input.SetControlScheme("Gamepad");               // 玩家切换到手柄
// input.CancelRebind();                             // 设置面板取消改键
// input.ResetBinding(interactAction, selectedBinding.Id); // 恢复该条绑定的默认键
// moveSubscription.Dispose();                      // 角色退出时解绑
// attackSubscription.Dispose();
```

`SaveInputSettings` 由游戏项目实现。玩家改键、还原键位或切换方案后，项目可以保存绑定覆盖 JSON 和方案名，并刷新已有的按键提示。

[输入接口、类图与时序图](docs/inputmanager/InputManager.md)

存档结构（`GameData` 之类）属于**游戏业务**，请写在项目里，
参考样板：业务 Manager 继承 `Singleton<T>`、读档放在 `OnInit()`、
Order 排在 `JsonManager(10)` 之后。

详细设计文档、时序图与验收记录见开发项目内的 `Docs/ManagerFramework.md`。
