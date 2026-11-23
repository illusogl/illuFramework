using ManagerOfManagers.Core;
using UnityEngine;

namespace Illu.Audio
{
    /// <summary>
    /// 统一音频管理：BGM（单源 + 淡入淡出）、Sfx（固定声部池，可并发）、UI（单源 PlayOneShot）。
    /// <para>
    /// 组（BGM / UI / Player / Enemy ...）是数据分类；通道（Bgm / Sfx / Ui）是播放路由。
    /// 3D 音效优先用 <see cref="PlaySfxAt"/>（池内声部）或 <see cref="PlaySfxOn"/>
    /// （调用方自己的 AudioSource，适合跟随角色移动的源）。
    /// </para>
    /// </summary>
    public class AudioManager : Singleton<AudioManager>
    {
        public const string BgmGroup = "BGM";
        public const string UiGroup = "UI";

        const int SfxVoiceCount = 12;
        const string LibraryResourcePath = "AudioLibrary";

        AudioLibrary _library;
        GameObject _root;
        AudioSource _bgm;
        AudioSource _ui;
        // 一个数组位置对应一个独立播放器和同索引的开始时间。
        AudioSource[] _sfxVoices;
        float[] _voiceStartTime;
        int _nextVoice;

        float _bgmVolume = 1f;
        float _sfxVolume = 1f;
        float _uiVolume = 1f;

        bool _fadingIn;
        bool _fadingOut;
        // 淡变系数独立于通道音量，静音时仍按时间推进。
        float _bgmFadeGain;
        float _fadeSeconds = 0.6f;
        string _currentBgm;
        // 切歌请求先存曲名和素材，等待旧曲淡出到零。
        string _pendingBgm;
        AudioClip _pendingClip;

        /// <summary>音频层：晚于数据层（15），早于 UI（20）与玩法。</summary>
        public override int Order => 18;

        /// <summary>已经交给播放器的曲名。切歌淡出期间仍是旧曲。</summary>
        public string CurrentBgm => _currentBgm;
        /// <summary>BGM 播放器当前是否正在播放，包含淡入和淡出期间。</summary>
        public bool IsBgmPlaying => _bgm != null && _bgm.isPlaying;
        /// <summary>BGM 播放器这一刻的音量，淡变期间会逐帧变化。</summary>
        public float CurrentBgmVolume => _bgm != null ? _bgm.volume : 0f;
        /// <summary>池内可同时播放的声部上限。池未创建或已释放时返回 0。</summary>
        public int MaxSfxVoices => _sfxVoices?.Length ?? 0;

        /// <summary>池内当前正在播放的声部数量，用于观察战斗音效并发。</summary>
        public int ActiveSfxCount
        {
            get
            {
                if (_sfxVoices == null) return 0;
                int count = 0;
                foreach (var voice in _sfxVoices)
                    if (voice != null && voice.isPlaying) count++;
                return count;
            }
        }

        /// <summary>由 Hub 启动音频模块。创建跨场景的 [Audio] 和播放器，再加载并校验默认音频库。</summary>
        public override void OnInit()
        {
            _root = new GameObject("[Audio]");
            GameObject.DontDestroyOnLoad(_root);

            _bgm = CreateSource("BGM");
            _bgm.volume = 0f;
            _ui = CreateSource("UI");
            _sfxVoices = new AudioSource[SfxVoiceCount];
            _voiceStartTime = new float[SfxVoiceCount];
            for (int i = 0; i < SfxVoiceCount; i++)
                _sfxVoices[i] = CreateSource("SFX_" + i);

            _library = Resources.Load<AudioLibrary>(LibraryResourcePath);
            if (_library == null)
            {
                Debug.LogWarning($"[Audio] Resources/{LibraryResourcePath} 不存在：调用 Play* 会报错，可先用 SetLibrary 注入。");
            }
            else
            {
                _library.Validate();
            }
        }

        /// <summary>替换后续播放请求所用的音频库，并校验新的配置。</summary>
        /// <param name="library">可以传入预先加载的资源或测试用库，传 null 表示清空。</param>
        public void SetLibrary(AudioLibrary library)
        {
            _library = library;
            _library?.Validate();
        }

        // ---------------------------------------------------------------- BGM

        /// <summary>请求播放背景音乐。空闲时直接淡入，已有曲目时先淡出旧曲，再换素材并淡入。</summary>
        /// <param name="name">音频库 BGM 组中的曲目键。</param>
        /// <param name="fade">单段淡变的时长，单位为秒，最小为 0.01 秒。</param>
        public void PlayBgm(string name, float fade = 0.6f)
        {
            if (_bgm == null) return;
            // 先查素材。曲名无效时，保留当前的播放和渐变状态。
            if (!TryGetClip(BgmGroup, name, out var clip)) return;
            if (_bgm.isPlaying && _currentBgm == name && !_fadingOut) return;

            if (_bgm.isPlaying)
            {
                _pendingBgm = name;
                _pendingClip = clip;
                _fadeSeconds = Mathf.Max(0.01f, fade);
                _fadingIn = false;
                _fadingOut = true;
            }
            else
            {
                StartBgm(name, clip, fade);
            }
        }

        /// <summary>取消待播曲目，把正在播放的背景音乐淡出到零后停止。</summary>
        /// <param name="fade">淡出时长，单位为秒，最小为 0.01 秒。</param>
        public void StopBgm(float fade = 0.6f)
        {
            if (_bgm == null) return;

            _pendingBgm = null;
            _pendingClip = null;

            if (!_bgm.isPlaying)
            {
                _currentBgm = null;
                return;
            }

            _fadeSeconds = Mathf.Max(0.01f, fade);
            _fadingIn = false;
            _fadingOut = true;
        }

        /// <summary>把已经查到的素材交给 BGM 播放器，从零音量开始循环，由后续帧淡入。</summary>
        /// <param name="name">要记为当前曲目的键。</param>
        /// <param name="clip">这次循环播放的音频素材。</param>
        /// <param name="fade">淡入时长，单位为秒。</param>
        void StartBgm(string name, AudioClip clip, float fade)
        {
            _currentBgm = name;
            _bgm.clip = clip;
            _bgm.loop = true;
            _bgmFadeGain = 0f;
            _bgm.volume = 0f;
            _bgm.Play();

            _fadeSeconds = Mathf.Max(0.01f, fade);
            _fadingIn = true;
            _fadingOut = false;
        }

        // ---------------------------------------------------------------- SFX

        /// <summary>使用池里的一个播放器播放 2D 战斗音效。</summary>
        /// <param name="group">素材所在的分组，例如 Player。</param>
        /// <param name="name">分组内的音效键，例如 Sword_1。</param>
        /// <param name="volume">本次播放的音量系数，限制在 0 到 1，再乘上 SFX 通道音量。</param>
        public void PlaySfx(string group, string name, float volume = 1f)
        {
            PlaySfxInternal(group, name, volume, null);
        }

        /// <summary>把池里的播放器移到指定位置，按 3D 音效播放。</summary>
        /// <param name="position">声音发出处的世界坐标。</param>
        /// <param name="group">素材所在的分组，例如 Player。</param>
        /// <param name="name">分组内的音效键，例如 Sword_1。</param>
        /// <param name="volume">本次播放的音量系数，限制在 0 到 1，再乘上 SFX 通道音量。</param>
        public void PlaySfxAt(Vector3 position, string group, string name, float volume = 1f)
        {
            PlaySfxInternal(group, name, volume, position);
        }

        /// <summary>在调用方提供的播放器上叠加音效，常用于声音跟随角色移动。</summary>
        /// <param name="source">调用方持有的播放器，空间化由该组件的设置决定。</param>
        /// <param name="group">素材所在的分组，例如 Player。</param>
        /// <param name="name">分组内的音效键，例如 Sword_1。</param>
        /// <param name="volume">本次播放的音量系数，限制在 0 到 1，再乘上 SFX 通道音量。</param>
        public void PlaySfxOn(AudioSource source, string group, string name, float volume = 1f)
        {
            if (source == null)
            {
                Debug.LogError($"[Audio] PlaySfxOn 收到 null AudioSource（{group}/{name}）");
                return;
            }
            if (!TryGetClip(group, name, out var clip)) return;

            source.PlayOneShot(clip, Mathf.Clamp01(volume) * _sfxVolume);
        }

        /// <summary>用 UI 播放器叠加一次短音效，素材直接传给 PlayOneShot。</summary>
        /// <param name="name">音频库 UI 组中的音效键。</param>
        /// <param name="volume">本次播放的音量系数，限制在 0 到 1，再乘上 UI 通道音量。</param>
        public void PlayUi(string name, float volume = 1f)
        {
            if (_ui == null) return;
            if (!TryGetClip(UiGroup, name, out var clip)) return;

            // 把素材直接传给本次播放。Inspector 中的 Clip 槽保持原值。
            _ui.PlayOneShot(clip, Mathf.Clamp01(volume) * _uiVolume);
        }

        /// <summary>停止由声部池承载的全部战斗音效。</summary>
        public void StopAllSfx()
        {
            if (_sfxVoices == null) return;
            foreach (var voice in _sfxVoices)
                if (voice != null) voice.Stop();
        }

        /// <summary>共用的池内播放流程。先查素材，再挑选声部，设置播放参数并记录开始时间。</summary>
        /// <param name="group">素材所在的分组，例如 Player。</param>
        /// <param name="name">分组内的音效键，例如 Sword_1。</param>
        /// <param name="volume">本次播放的音量系数，限制在 0 到 1，再乘上 SFX 通道音量。</param>
        /// <param name="position">传世界坐标时用 3D 模式，传 null 时用 2D 模式。</param>
        void PlaySfxInternal(string group, string name, float volume, Vector3? position)
        {
            if (!TryGetClip(group, name, out var clip)) return;

            int index = AcquireVoiceIndex();
            var voice = _sfxVoices[index];

            // 声部会复用，每次都要重新设定 2D 或 3D 模式。
            voice.spatialBlend = position.HasValue ? 1f : 0f;
            if (position.HasValue) voice.transform.position = position.Value;

            voice.clip = clip;
            voice.volume = Mathf.Clamp01(volume) * _sfxVolume;
            voice.Play();

            // 记下开始时刻，供 12 路全忙时判断该打断哪一路。
            _voiceStartTime[index] = Time.time;
        }

        /// <summary>优先复用空闲声部。全部在播时，停止记录中最早开始的一路并复用它。</summary>
        /// <returns>可立即用于播放的声部索引。</returns>
        int AcquireVoiceIndex()
        {
            // 从上次分配的位置往后找，到数组末尾后回到开头。
            for (int i = 0; i < _sfxVoices.Length; i++)
            {
                int index = (_nextVoice + i) % _sfxVoices.Length;
                if (_sfxVoices[index].isPlaying) continue;

                _nextVoice = (index + 1) % _sfxVoices.Length;
                return index;
            }

            int oldest = 0;
            float oldestTime = float.MaxValue;
            for (int i = 0; i < _voiceStartTime.Length; i++)
            {
                if (_voiceStartTime[i] >= oldestTime) continue;
                oldestTime = _voiceStartTime[i];
                oldest = i;
            }

            _sfxVoices[oldest].Stop();
            _nextVoice = (oldest + 1) % _sfxVoices.Length;
            return oldest;
        }

        // ---------------------------------------------------------------- 音量

        /// <summary>更新指定通道的音量。BGM 立即按当前淡变系数应用新值，UI 和 SFX 在发出播放请求时使用新值。</summary>
        /// <param name="channel">要调整的播放通道。</param>
        /// <param name="volume">目标音量，限制在 0 到 1。</param>
        public void SetChannelVolume(AudioChannel channel, float volume)
        {
            volume = Mathf.Clamp01(volume);

            switch (channel)
            {
                case AudioChannel.Bgm:
                    _bgmVolume = volume;
                    if (_bgm != null)
                        _bgm.volume = _bgmFadeGain * _bgmVolume;
                    break;
                case AudioChannel.Sfx:
                    _sfxVolume = volume;
                    break;
                case AudioChannel.Ui:
                    _uiVolume = volume;
                    break;
            }
        }

        /// <summary>读取指定通道保存的音量设定值。</summary>
        /// <param name="channel">要读取的播放通道。</param>
        /// <returns>该通道的音量设定。未知通道返回 0。</returns>
        public float GetChannelVolume(AudioChannel channel)
        {
            switch (channel)
            {
                case AudioChannel.Bgm: return _bgmVolume;
                case AudioChannel.Sfx: return _sfxVolume;
                case AudioChannel.Ui: return _uiVolume;
                default: return 0f;
            }
        }

        // ---------------------------------------------------------------- 生命周期

        /// <summary>由 Hub 每帧推进背景音乐淡变系数。系数降到零时切换到待播曲目，或者停止播放。</summary>
        /// <param name="dt">Hub 传入的 Time.deltaTime，单位为秒。</param>
        public override void OnTick(float dt)
        {
            if (_bgm == null) return;

            if (_fadingOut)
            {
                _bgmFadeGain = Mathf.MoveTowards(_bgmFadeGain, 0f, dt / _fadeSeconds);
                _bgm.volume = _bgmFadeGain * _bgmVolume;
                if (_bgmFadeGain > 0f) return;

                _fadingOut = false;

                // 淡出走完后再换素材。通道音量设成零时也会走到这里。
                if (_pendingClip != null)
                {
                    var name = _pendingBgm;
                    var clip = _pendingClip;
                    _pendingBgm = null;
                    _pendingClip = null;
                    StartBgm(name, clip, _fadeSeconds);
                }
                else
                {
                    _bgm.Stop();
                    _currentBgm = null;
                }
                return;
            }

            if (!_fadingIn) return;

            _bgmFadeGain = Mathf.MoveTowards(_bgmFadeGain, 1f, dt / _fadeSeconds);
            _bgm.volume = _bgmFadeGain * _bgmVolume;
            if (_bgmFadeGain < 1f) return;

            _fadingIn = false;
        }

        /// <summary>停止池内音效，销毁 [Audio] 及其播放器，并清理播放器引用和曲目记录。</summary>
        public override void OnDispose()
        {
            StopAllSfx();

            if (_root != null)
                GameObject.Destroy(_root);

            _root = null;
            _bgm = null;
            _ui = null;
            _sfxVoices = null;
            _voiceStartTime = null;
            _bgmFadeGain = 0f;
            _fadingIn = false;
            _fadingOut = false;
            _currentBgm = null;
            _pendingBgm = null;
            _pendingClip = null;
        }

        // ---------------------------------------------------------------- 工具

        /// <summary>在 [Audio] 下创建一个新的子物体，添加默认处于 2D 模式的 AudioSource。</summary>
        /// <param name="name">子物体在 Unity 层级中的名称。</param>
        /// <returns>新创建的播放器组件。</returns>
        AudioSource CreateSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);

            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            return source;
        }

        /// <summary>统一处理播放前的素材查找，在音频库未加载或找不到可用素材时记录错误。</summary>
        /// <param name="group">素材所在的分组。</param>
        /// <param name="name">分组内的音效或曲目键。</param>
        /// <param name="clip">查找到的音频素材。</param>
        /// <returns>查到可用的音频素材时返回 true。</returns>
        bool TryGetClip(string group, string name, out AudioClip clip)
        {
            clip = null;

            if (_library == null)
            {
                Debug.LogError("[Audio] AudioLibrary 未加载，无法播放音效");
                return false;
            }

            if (_library.TryGet(group, name, out clip))
                return true;

            Debug.LogError($"[Audio] 组 {group} 缺少键 {name}");
            return false;
        }
    }
}
