using System;
using System.Collections.Generic;
using LiteFrameworkNext;
using UnityEngine;

namespace LiteGameNext.Platform.Audio
{
    /// <summary>音频分组（Effect/UI/Voice/Bgm 四组独立音量）。</summary>
    public enum AudioGroup
    {
        Effect = 0,
        UI = 1,
        Voice = 2,
        Bgm = 3,
    }

    /// <summary>音频服务：按组播放/3D/淡入淡出/分组音量；代理池复用 AudioSource，超出即丢弃最旧。</summary>
    public sealed class AudioService : IDisposable
    {
        private sealed class Voice
        {
            public AudioSource Source;
            public AudioGroup Group;
            public int Handle;
            public float BaseVolume = 1f;
            public float FadeTarget = -1f;
            public float FadeSpeed;
            public bool Releasing;

            public bool Busy => Source != null && Source.isPlaying && !Releasing;
        }

        private const int ProxyPerGroup = 4;
        private const int DropLogEvery = 100;

        private readonly Dictionary<AudioGroup, List<Voice>> _pools = new Dictionary<AudioGroup, List<Voice>>();
        private readonly Dictionary<AudioGroup, float> _groupVolume = new Dictionary<AudioGroup, float>();
        private readonly Dictionary<int, Voice> _byHandle = new Dictionary<int, Voice>();
        private GameObject _root;
        private int _nextHandle = 1;
        private int _dropped;

        /// <summary>构造：建四组代理池；根对象随服务生命周期。</summary>
        public AudioService()
        {
            foreach (AudioGroup group in Enum.GetValues(typeof(AudioGroup)))
            {
                _pools[group] = new List<Voice>(ProxyPerGroup);
                _groupVolume[group] = 1f;
            }
            if (Application.isPlaying)
            {
                _root = new GameObject("[AudioService]");
                UnityEngine.Object.DontDestroyOnLoad(_root);
            }
        }

        /// <summary>是否已关闭。</summary>
        public bool IsShutdown { get; private set; }

        /// <summary>超出代理池被丢弃的播放请求数（诊断）。</summary>
        public int DroppedCount => _dropped;

        /// <summary>当前活跃声道数。</summary>
        public int VoiceCount
        {
            get
            {
                int count = 0;
                foreach (List<Voice> pool in _pools.Values)
                    for (int i = 0; i < pool.Count; i++)
                        if (pool[i].Busy) count++;
                return count;
            }
        }

        /// <summary>播放 2D 音效；返回句柄（0 = 未播放）。</summary>
        public int Play(AudioGroup group, AudioClip clip, float volume = 1f, bool loop = false, float fadeIn = 0f)
            => PlayInternal(group, clip, volume, loop, fadeIn, null);

        /// <summary>播放 3D 音效（世界坐标）；返回句柄。</summary>
        public int Play3D(AudioGroup group, AudioClip clip, Vector3 position, float volume = 1f, float fadeIn = 0f)
        {
            int handle = PlayInternal(group, clip, volume, false, fadeIn, position);
            return handle;
        }

        /// <summary>停止指定句柄（可带淡出）。</summary>
        public void Stop(int handle, float fadeOut = 0f)
        {
            if (!_byHandle.TryGetValue(handle, out Voice voice)) return;
            _byHandle.Remove(handle);
            voice.Handle = 0;
            if (fadeOut <= 0f)
            {
                voice.Source.Stop();
                voice.Releasing = false;
                return;
            }
            voice.FadeTarget = 0f;
            voice.FadeSpeed = voice.Source.volume / fadeOut;
            voice.Releasing = true;
        }

        /// <summary>设置分组音量（0..1）；立即作用于该组在播声道。</summary>
        public void SetGroupVolume(AudioGroup group, float volume)
        {
            if (IsShutdown) return;
            _groupVolume[group] = Mathf.Clamp01(volume);
            List<Voice> pool = _pools[group];
            for (int i = 0; i < pool.Count; i++)
            {
                Voice voice = pool[i];
                if (voice.Source == null) continue;
                voice.Source.volume = voice.BaseVolume * _groupVolume[group];
            }
        }

        /// <summary>全局停止（系统路径，不淡出）。</summary>
        public void StopAll()
        {
            foreach (List<Voice> pool in _pools.Values)
                for (int i = 0; i < pool.Count; i++)
                {
                    Voice voice = pool[i];
                    if (voice.Source != null) voice.Source.Stop();
                    voice.Handle = 0;
                    voice.Releasing = false;
                }
            _byHandle.Clear();
        }

        /// <summary>关闭并释放根对象；幂等。</summary>
        public void Shutdown()
        {
            if (IsShutdown) return;
            IsShutdown = true;
            StopAll();
            foreach (List<Voice> pool in _pools.Values)
            {
                for (int i = 0; i < pool.Count; i++)
                    if (pool[i].Source != null) UnityEngine.Object.Destroy(pool[i].Source.gameObject);
                pool.Clear();
            }
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
        }

        /// <summary>逐帧推进淡入淡出（由 ClientApp.Tick 驱动）。</summary>
        public void Tick(float deltaTime)
        {
            if (IsShutdown) return;
            foreach (List<Voice> pool in _pools.Values)
                for (int i = 0; i < pool.Count; i++)
                {
                    Voice voice = pool[i];
                    if (voice.Source == null || voice.FadeTarget < 0f) continue;
                    voice.Source.volume = Mathf.MoveTowards(voice.Source.volume,
                        voice.FadeTarget, voice.FadeSpeed * deltaTime);
                    if (!Mathf.Approximately(voice.Source.volume, voice.FadeTarget)) continue;
                    if (voice.FadeTarget <= 0f) voice.Source.Stop();
                    voice.FadeTarget = -1f;
                    voice.Releasing = false;
                }
        }

        public void Dispose() => Shutdown();

        private int PlayInternal(AudioGroup group, AudioClip clip, float volume, bool loop,
            float fadeIn, Vector3? position)
        {
            if (IsShutdown || clip == null) return 0;
            Voice voice = Acquire(group);
            if (voice == null) return 0;

            int handle = _nextHandle++;
            voice.Handle = handle;
            voice.Group = group;
            voice.BaseVolume = Mathf.Clamp01(volume);
            voice.Releasing = false;
            _byHandle[handle] = voice;

            AudioSource source = voice.Source;
            source.clip = clip;
            source.loop = loop;
            source.volume = voice.BaseVolume * _groupVolume[group];
            if (position.HasValue)
            {
                source.transform.position = position.Value;
                source.spatialBlend = 1f;
            }
            else
            {
                source.spatialBlend = 0f;
            }

            if (fadeIn > 0f)
            {
                source.volume = 0f;
                voice.FadeTarget = voice.BaseVolume * _groupVolume[group];
                voice.FadeSpeed = voice.FadeTarget / fadeIn;
            }
            source.Play();
            return handle;
        }

        private Voice Acquire(AudioGroup group)
        {
            List<Voice> pool = _pools[group];
            for (int i = 0; i < pool.Count; i++)
                if (!pool[i].Busy) return pool[i];

            if (pool.Count < ProxyPerGroup)
            {
                Voice created = CreateVoice(group);
                if (created != null) pool.Add(created);
                return created;
            }

            // 池满：丢弃最旧（第一个非释放中的声道），并计数。
            _dropped++;
            if (_dropped % DropLogEvery == 0)
                Log.Warning("音频声道不足，已丢弃 " + _dropped + " 次（组 " + group + "）");
            Voice victim = pool[0];
            victim.Source.Stop();
            victim.FadeTarget = -1f;
            victim.Releasing = false;
            return victim;
        }

        private Voice CreateVoice(AudioGroup group)
        {
            if (_root == null) return null;
            var go = new GameObject("voice_" + group);
            go.transform.SetParent(_root.transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 30f;
            return new Voice { Source = source, Group = group };
        }
    }
}
