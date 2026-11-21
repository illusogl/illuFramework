# illuFramework

我的 Unity 个人框架仓库。多包布局：一个模块一个 UPM 包，全部从本仓库安装。

| 包 | 说明 | 依赖 |
| --- | --- | --- |
| [`com.illu.managerofmanagers`](./com.illu.managerofmanagers) | 基础设施：统一纯 C# 与 MonoBehaviour 单例的获取方式与生命周期驱动 | 无 |
| [`com.illu.json`](./com.illu.json) | `JsonManager`：存档序列化/反序列化（内置 LitJson） | `com.illu.managerofmanagers` |
| [`com.illu.ui`](./com.illu.ui) | `UIManager + BasePanel`：Canvas 常驻、面板字典、淡入淡出 | `com.illu.managerofmanagers` |

## 安装（任意 Unity 项目）

`Window → Package Manager → + → Add package from git URL`，**按需登记**（顺序不限）：

```
# 基础包（必装）
https://github.com/illusogl/illuFramework.git?path=/com.illu.managerofmanagers

# 功能包（用到哪个装哪个）
https://github.com/illusogl/illuFramework.git?path=/com.illu.json
https://github.com/illusogl/illuFramework.git?path=/com.illu.ui
```

> 为什么不能只装一条：Unity 的 UPM 里，包的 `dependencies` **只支持 registry 版本号，不支持 git URL**。
> 走 git 安装时每个包必须各自登记（实测过：只装功能包会缺基础包，编译报 CS0246）。
> 以后若上架 registry / OpenUPM，就可以只留一条。

或者直接写进 `Packages/manifest.json` 的 `dependencies`：

```json
"com.illu.managerofmanagers": "https://github.com/illusogl/illuFramework.git?path=/com.illu.managerofmanagers",
"com.illu.json": "https://github.com/illusogl/illuFramework.git?path=/com.illu.json",
"com.illu.ui": "https://github.com/illusogl/illuFramework.git?path=/com.illu.ui"
```

要求：机器装有 git；仓库公开，无需凭证。

## com.illu.managerofmanagers 用法速览

```csharp
// 1. 纯 C# Manager
public sealed class AudioManager : Singleton<AudioManager>
{
    AudioManager() { }                       // 私有构造
    public override int Order => 20;        // Order 必须唯一，忘了写编译不过
}

// 2. MonoBehaviour Manager（场景里手动挂一个）
public sealed class InputManager : MonoSingleton<InputManager>
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
        Register(InputManager.Instance);
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

存档结构（`GameData` 之类）属于**游戏业务**，请写在项目里，
参考样板：业务 Manager 继承 `Singleton<T>`、读档放在 `OnInit()`、
Order 排在 `JsonManager(10)` 之后。

详细设计文档、时序图与验收记录见开发项目内的 `Docs/ManagerFramework.md`。
