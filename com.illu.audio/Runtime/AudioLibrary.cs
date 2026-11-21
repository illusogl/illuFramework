using System;
using System.Collections.Generic;
using UnityEngine;

namespace Illu.Audio
{
    /// <summary>
    /// 音效库：组 → (key → clip)。
    /// <para>
    /// 约定放在 Resources/AudioLibrary，AudioManager.OnInit 自动加载；
    /// 也可以通过 <see cref="AudioManager.SetLibrary"/> 注入（Addressables / 测试）。
    /// 运行时可用 <see cref="Register"/> 动态登记（热更新、测试）。
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Illu/Audio Library", fileName = "AudioLibrary")]
    public class AudioLibrary : ScriptableObject
    {
        [SerializeField] private AudioGroup[] groups = Array.Empty<AudioGroup>();

        private Dictionary<string, AudioGroup> _map;

        void EnsureMap()
        {
            if (_map != null) return;

            _map = new Dictionary<string, AudioGroup>(groups.Length);
            foreach (var group in groups)
            {
                if (group == null || string.IsNullOrEmpty(group.id)) continue;

                if (_map.ContainsKey(group.id))
                {
                    Debug.LogError($"[AudioLibrary] 组名重复：{group.id}，保留第一个");
                    continue;
                }
                _map.Add(group.id, group);
            }
        }

        /// <summary>配置期校验：组名重复、组内 key 重复、空 clip。</summary>
        public void Validate()
        {
            EnsureMap();

            foreach (var group in _map.Values)
            {
                var seen = new HashSet<string>();
                foreach (var entry in group.clips)
                {
                    if (entry == null) continue;
                    if (string.IsNullOrEmpty(entry.key))
                    {
                        Debug.LogError($"[AudioLibrary] 组 {group.id} 存在空 key");
                        continue;
                    }
                    if (!seen.Add(entry.key))
                        Debug.LogError($"[AudioLibrary] 组 {group.id} 的 key 重复：{entry.key}");
                    if (entry.clip == null)
                        Debug.LogError($"[AudioLibrary] 组 {group.id}/{entry.key} 没有绑定 AudioClip");
                }
            }
        }

        /// <summary>取 clip。失败返回 false，由调用方决定如何报错。</summary>
        public bool TryGet(string groupId, string key, out AudioClip clip)
        {
            clip = null;
            EnsureMap();

            if (!_map.TryGetValue(groupId, out var group))
                return false;

            foreach (var entry in group.clips)
            {
                if (entry != null && entry.key == key)
                {
                    clip = entry.clip;
                    return clip != null;
                }
            }
            return false;
        }

        /// <summary>运行期登记（已存在则覆盖）。用于 Addressables 加载或测试。</summary>
        public void Register(string groupId, string key, AudioClip clip)
        {
            EnsureMap();

            if (!_map.TryGetValue(groupId, out var group))
            {
                group = new AudioGroup { id = groupId };
                Array.Resize(ref groups, groups.Length + 1);
                groups[groups.Length - 1] = group;
                _map.Add(groupId, group);
            }

            for (int i = 0; i < group.clips.Length; i++)
            {
                if (group.clips[i] != null && group.clips[i].key == key)
                {
                    group.clips[i].clip = clip;
                    return;
                }
            }

            Array.Resize(ref group.clips, group.clips.Length + 1);
            group.clips[group.clips.Length - 1] = new AudioEntry { key = key, clip = clip };
        }
    }
}
