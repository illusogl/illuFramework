# AudioManager 类图与时序图

音频库的异步启动流程见 [资源加载设计](../assetloadmanager/AssetLoadManager.md#音频库启动时序)。

## 类图 · 播放请求、素材和播放器

Hub 初始化 1 个 BGM、1 个 UI 和 12 个 SFX 播放器，然后发起音频库预加载。Demo 面板直接发播放指令。

![类图 · 播放请求、素材和播放器](audio-class.png)

<details>
<summary>Mermaid 源码</summary>

```mermaid
classDiagram
    direction TB

    class StatsPanel
    class GameHub
    class Singleton~T~ {
        +Instance
    }
    class AudioManager {
        +OnInit()
        +LoadDefaultLibraryAsync()
        +SetLibrary()
        +PlayUi()
        +PlayBgm()
        +PlaySfx()
        +OnTick()
        +OnDispose()
    }
    class AudioLibrary {
        +TryGet()
    }
    class AssetLoadManager {
        +LoadResourceAsync()
    }
    class AudioGroup {
        +id
    }
    class AudioEntry {
        +key
    }
    class AudioClip
    class AudioSource {
        +clip
        +Play()
        +PlayOneShot()
        +Stop()
    }

    StatsPanel ..> AudioManager : 播放请求
    GameHub ..> AudioManager : 注册并发起预加载
    Singleton <|-- AudioManager
    AudioManager ..> AssetLoadManager : 异步加载音频库
    AudioManager --> AudioLibrary : 按组和键查找
    AudioLibrary *-- AudioGroup : 分组
    AudioGroup *-- AudioEntry : 条目
    AudioEntry --> AudioClip : 引用素材
    AudioManager *-- "14" AudioSource : 创建并持有
    AudioSource --> AudioClip : 播放素材
```

</details>

## UI 时序 · 从点击到单次播放

库可用时，点击声交给 PlayOneShot，快速点击可以叠加。库为空时，AudioManager 跳过本次请求。

![UI 时序 · 从点击到单次播放](audio-ui-seq.png)

<details>
<summary>Mermaid 源码</summary>

```mermaid
sequenceDiagram
    participant D as Demo 面板
    participant A as AudioManager
    participant L as AudioLibrary
    participant U as UI 播放器

    D->>A: PlayUi("Click")
    alt 音频库可用
        A->>L: 查找 UI / Click
        L-->>A: 返回 ui_click 素材
        A->>U: PlayOneShot(素材, 本次音量 × UI 通道音量)
    else 库为空
        A->>A: 跳过本次播放
    end
```

</details>

## BGM 时序 · 淡出、换素材和淡入

加载期间只记下最新曲名。库装好后才查素材。播放中切歌会先找到新曲素材，等旧曲的淡变系数归零再换曲。

![BGM 时序 · 淡出、换素材和淡入](audio-bgm-seq.png)

<details>
<summary>Mermaid 源码</summary>

```mermaid
sequenceDiagram
    participant D as Demo 面板
    participant A as AudioManager
    participant L as AudioLibrary
    participant B as BGM 播放器
    participant H as GameHub

    D->>A: PlayBgm(曲名)
    opt 默认音频库正在加载并最终成功
        A->>A: 暂存最新曲名与淡入时长
        A->>A: 安装音频库，再调用 PlayBgm
    end
    A->>L: 查找 BGM / 曲名
    L-->>A: 返回查找结果
    alt 查不到曲目
        A->>A: 记录错误，当前音乐继续
    else 找到新的曲目
        alt 播放器空闲
            A->>B: 设置 Clip，从零音量循环播放
        else 已经在播歌
            A->>A: 保存下一首素材并开始淡出
            loop 淡出期间
                H->>A: OnTick(dt)
                A->>B: 淡变系数趋向 0，更新实际音量
                opt 本帧淡变系数到零
                    A->>B: 换素材，从零音量播放
                end
            end
        end
        loop 淡入期间
            H->>A: OnTick(dt)
            A->>B: 淡变系数趋向 1，更新实际音量
        end
    end
```

</details>

## SFX 时序 · 分配播放器与 12 路并发

查到素材才分配声部。固定位置的 3D 音效走同一套分配流程，使用传入的世界坐标。

![SFX 时序 · 分配播放器与 12 路并发](audio-sfx-seq.png)

<details>
<summary>Mermaid 源码</summary>

```mermaid
sequenceDiagram
    participant D as Demo 面板
    participant A as AudioManager
    participant L as AudioLibrary
    participant S as 选中的 SFX 播放器

    D->>A: Play 12 SFX 先停止池内已有音效
    loop 同一帧提交 12 次
        D->>A: PlaySfx(Player, 挥剑或受击)
        A->>L: 查找素材
        L-->>A: 返回素材
        A->>A: 从上次位置接着找空闲声部
        A->>S: 设置 Clip、音量和 2D 模式
        A->>S: Play()
        A->>A: 记录声部开始时间
    end
    Note over A,S: 12 个播放器各自继续播放
    opt 12 路都在播时又来一个请求
        D->>A: 请求播放新的 SFX
        A->>L: 查找新素材
        L-->>A: 返回新素材
        A->>A: 按开始时间挑出最早的一路
        A->>S: 停掉旧声音，换素材后立即播放
    end
```

</details>
