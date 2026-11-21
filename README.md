# illuFramework

我的 Unity 个人框架仓库。多包布局：一个模块一个 UPM 包，全部从本仓库安装。

| 包 | 说明 | 依赖 |
| --- | --- | --- |
| [`com.illu.managerofmanagers`](./com.illu.managerofmanagers) | 基础设施：统一纯 C# 与 MonoBehaviour 单例的获取方式与生命周期驱动 | 无 |
| [`com.illu.managers`](./com.illu.managers) | 通用功能 Manager：`JsonManager`（存档序列化，内置 LitJson）、`UIManager + BasePanel`（面板管理） | `com.illu.managerofmanagers`（自动安装） |

## 安装（任意 Unity 项目）

`Window → Package Manager → + → Add package from git URL`：

```
# 只要基础设施
https://github.com/illusogl/illuFramework.git?path=/com.illu.managerofmanagers

# 或者一步到位（基础包会作为依赖自动装上）
https://github.com/illusogl/illuFramework.git?path=/com.illu.managers
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

## com.illu.managers 用法速览

```csharp
using Illu.Managers;
using Illu.Managers.UI;

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
