using System;
using UnityEngine;

namespace Illu.Audio
{
    /// <summary>播放通道：决定用哪个 AudioSource、乘哪个音量。</summary>
    public enum AudioChannel
    {
        Bgm,
        Sfx,
        Ui,
    }

    /// <summary>音效组：库里 clip 的分类（BGM / UI / Player / Enemy / FootStep ...）。</summary>
    [Serializable]
    public class AudioGroup
    {
        public string id;
        public AudioEntry[] clips = Array.Empty<AudioEntry>();
    }

    /// <summary>一条音效配置：key → clip。</summary>
    [Serializable]
    public class AudioEntry
    {
        public string key;
        public AudioClip clip;
    }
}
