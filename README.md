# illuFramework

我的 Unity 个人框架仓库。多包布局：一个模块一个 UPM 包，全部从本仓库安装。

| 包 | 说明 |
| --- | --- |
| [`com.illu.managerofmanagers`](./com.illu.managerofmanagers) | Manager of Managers：统一纯 C# 与 MonoBehaviour 单例的获取方式与生命周期驱动 |

## 安装（任意 Unity 项目）

`Window → Package Manager → + → Add package from git URL`：

```
https://github.com/illusogl/illuFramework.git?path=/com.illu.managerofmanagers
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

详细设计文档、时序图与验收记录见开发项目内的 `Docs/ManagerFramework.md`。
