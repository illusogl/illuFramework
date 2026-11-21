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
        AudioSource[] _sfxVoices;
        float[] _voiceStartTime;
        int _nextVoice;

        float _bgmVolume = 1f;
        float _sfxVolume = 1f;
        float _uiVolume = 1f;

        bool _fadingIn;
        bool _fadingOut;
        float _fadeSeconds = 0.6f;
        string _currentBgm;
        string _pendingBgm;
        AudioClip _pendingClip;

        /// <summary>音频层：晚于数据层（15），早于 UI（20）与玩法。</summary>
        public override int Order => 18;

        public string CurrentBgm => _currentBgm;
        public bool IsBgmPlaying => _bgm != null && _bgm.isPlaying;
        public float CurrentBgmVolume => _bgm != null ? _bgm.volume : 0f;
        public int MaxSfxVoices => _sfxVoices?.Length ?? 0;

        /// <summary>当前正在播放的声部数量。</summary>
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

        public override void OnInit()
        {
            _root = new GameObject("[Audio]");
            GameObject.DontDestroyOnLoad(_root);

            _bgm = CreateSource("BGM");
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

        /// <summary>注入音效库（Addressables 加载、测试用）。</summary>
        public void SetLibrary(AudioLibrary library)
        {
            _library = library;
            _library?.Validate();
        }

        // ---------------------------------------------------------------- BGM

        /// <summary>切歌：旧曲淡出 → 换 clip → 淡入。缺键报错并保留当前曲。</summary>
        public void PlayBgm(string name, float fade = 0.6f)
        {
            if (_bgm == null) return;
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

        /// <summary>淡出后停止当前 BGM。</summary>
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

        void StartBgm(string name, AudioClip clip, float fade)
        {
            _currentBgm = name;
            _bgm.clip = clip;
            _bgm.loop = true;
            _bgm.volume = 0f;
            _bgm.Play();

            _fadeSeconds = Mathf.Max(0.01f, fade);
            _fadingIn = true;
            _fadingOut = false;
        }

        // ---------------------------------------------------------------- SFX

        /// <summary>2D 音效，从声部池取一个 voice 播放。</summary>
        public void PlaySfx(string group, string name, float volume = 1f)
        {
            PlaySfxInternal(group, name, volume, null);
        }

        /// <summary>3D 音效（池内 voice，按位置衰减）。</summary>
        public void PlaySfxAt(Vector3 position, string group, string name, float volume = 1f)
        {
            PlaySfxInternal(group, name, volume, position);
        }

        /// <summary>用调用方自己的 AudioSource 播放（适合跟随角色移动的 3D 源）。</summary>
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

        /// <summary>UI 音效：固定 UI 组 + UI 通道，PlayOneShot 叠加。</summary>
        public void PlayUi(string name, float volume = 1f)
        {
            if (_ui == null) return;
            if (!TryGetClip(UiGroup, name, out var clip)) return;

            _ui.PlayOneShot(clip, Mathf.Clamp01(volume) * _uiVolume);
        }

        /// <summary>停止池内所有音效。</summary>
        public void StopAllSfx()
        {
            if (_sfxVoices == null) return;
            foreach (var voice in _sfxVoices)
                if (voice != null) voice.Stop();
        }

        void PlaySfxInternal(string group, string name, float volume, Vector3? position)
        {
            if (!TryGetClip(group, name, out var clip)) return;

            int index = AcquireVoiceIndex();
            var voice = _sfxVoices[index];

            voice.spatialBlend = position.HasValue ? 1f : 0f;
            if (position.HasValue) voice.transform.position = position.Value;

            voice.clip = clip;
            voice.volume = Mathf.Clamp01(volume) * _sfxVolume;
            voice.Play();

            _voiceStartTime[index] = Time.time;
        }

        /// <summary>空闲 voice 优先；全忙时抢占最早开始的那条。</summary>
        int AcquireVoiceIndex()
        {
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

        public void SetChannelVolume(AudioChannel channel, float volume)
        {
            volume = Mathf.Clamp01(volume);

            switch (channel)
            {
                case AudioChannel.Bgm:
                    _bgmVolume = volume;
                    if (_bgm != null && !_fadingIn && !_fadingOut)
                        _bgm.volume = volume;
                    break;
                case AudioChannel.Sfx:
                    _sfxVolume = volume;
                    break;
                case AudioChannel.Ui:
                    _uiVolume = volume;
                    break;
            }
        }

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

        public override void OnTick(float dt)
        {
            if (_bgm == null) return;

            if (_fadingOut)
            {
                _bgm.volume -= _bgmVolume * dt / _fadeSeconds;
                if (_bgm.volume > 0f) return;

                _bgm.volume = 0f;
                _fadingOut = false;

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

            _bgm.volume += _bgmVolume * dt / _fadeSeconds;
            if (_bgm.volume < _bgmVolume) return;

            _bgm.volume = _bgmVolume;
            _fadingIn = false;
        }

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
            _currentBgm = null;
            _pendingBgm = null;
            _pendingClip = null;
        }

        // ---------------------------------------------------------------- 工具

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
