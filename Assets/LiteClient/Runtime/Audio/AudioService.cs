using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 声音壳（M4 §2.9，手册步骤 6）：**组 + 代理模型**。
    /// 组 = 类别（Effect/Ui/Voice/Bgm），每组 N 个 AudioSource 代理轮转（并发通道，同组音效可叠加）；
    /// 组音量/静音组级控制；**同优先级不被替换**——全忙时仅"更高优先级"抢占最低优先级代理，否则丢弃（日志可观测）。
    /// 句柄在代理被抢占/自然播完后失效（Stop 对失效句柄安全忽略）。
    ///
    /// 评审决议（2026-09-15，对应外部评审 6.2–6.5）：
    /// - 6.2 Stop O(n)：**不建句柄索引**——总代理数构造期定死（默认 9），Stop 为低频操作；
    ///   索引反而要维护"自然播完/抢占"的失效清理，得不偿失（不过度设计）。
    /// - 6.3 抢占语义：**行为正确**——句柄单调递增且永不复用，旧句柄必然失效，
    ///   Stop(旧句柄) 安全忽略；Source.Play 换 clip 自动停旧音。
    /// - 6.4 淡入淡出：**已实现** Play(fadeIn) / Stop(fadeOut)，Bgm 组扩为 2 代理支持交叉淡切。
    /// - 6.5 空间化：**已实现** Play3D（spatialBlend + 一次性世界坐标；跟随实体待真实需求）。
    /// </summary>
    public sealed class AudioService : IModuleStats
    {
        public enum Group { Effect, Ui, Voice, Bgm }

        private sealed class Proxy
        {
            public AudioSource Source;
            public int Priority;                           // 当前占用者的优先级（空闲 = int.MinValue）
            public int Handle;                             // 当前占用者句柄（0 = 空闲；单调递增永不复用）
            public float BaseVolume = 1f;
            public bool Busy => Handle != 0 && Source != null && Source.isPlaying;
        }

        private sealed class GroupDef
        {
            public Group Group;
            public string Name;
            public float Volume = 1f;
            public bool Mute;
            public List<Proxy> Proxies = new List<Proxy>(4);
            public int Cursor;                             // 轮转游标（空闲代理公平轮转）
        }

        private readonly Dictionary<Group, GroupDef> _groups = new Dictionary<Group, GroupDef>(4);
        private Transform _root;                             // 关闭后置空（Shutdown 幂等判据）
        private int _nextHandle = 1;
        private IWorldClock _worldClock;                     // 淡变的世界轨（Effect/Voice）
        private IUIClock _uiClock;                           // 淡变的 UI 轨（Ui/Bgm）

        /// <summary>
        /// 分域时钟注入（《动画模块专项设计》§7 时钟表 + 《状态同步专项设计》§6.2"世界/UI 暂停"）：
        /// 音效随世界暂停/变速，UI 音与 BGM 随 UI 暂停——**不再读 <c>Time.deltaTime</c>**。
        /// 本工程 <c>Time.timeScale</c> 恒为 1（<see cref="GameClock"/> 纪律），读它等于永不暂停：
        /// 旧注释声称的"时停即停"从未成立。未注入时退化为真实帧步进（编辑器/测试兜底）。
        /// </summary>
        public void BindClocks(IWorldClock world, IUIClock ui)
        {
            _worldClock = world;
            _uiClock = ui;
        }

        /// <summary>组 → 时钟域（Effect/Voice 走世界轨；Ui/Bgm 走 UI 轨——UI 暂停不该掐掉战斗音效）。</summary>
        private float DeltaSeconds(Group group)
        {
            IGameClock clock = group == Group.Effect || group == Group.Voice ? (IGameClock)_worldClock : _uiClock;
            return clock != null ? clock.ScaledDelta : UnityEngine.Time.deltaTime;
        }

        public AudioService(int effectProxies = 4, int uiProxies = 2, int voiceProxies = 2, int bgmProxies = 2)
        {
            _root = new GameObject("[Audio]").transform;
            // DontDestroyOnLoad 仅 Play mode 合法（编辑器脚本直调抛 InvalidOperationException）——
            // 编辑态用例（EditMode 验收 / 工具脚本）跳过，与 Shutdown 的 Destroy/DestroyImmediate 分支同口径。
            if (Application.isPlaying)
                UnityEngine.Object.DontDestroyOnLoad(_root.gameObject);
            AddGroup(Group.Effect, "Effect", effectProxies);
            AddGroup(Group.Ui, "Ui", uiProxies);
            AddGroup(Group.Voice, "Voice", voiceProxies);
            AddGroup(Group.Bgm, "Bgm", bgmProxies);        // 2 代理：BGM 交叉淡切（旧淡出 + 新淡入）
            Log.Info("声音壳就绪:Effect/Ui/Voice/Bgm 四组（组+代理模型；支持 fadeIn/fadeOut 与 Play3D）", "Audio");
        }

        private void AddGroup(Group group, string name, int proxyCount)
        {
            var node = new GameObject(name).transform;
            node.SetParent(_root, false);
            var def = new GroupDef { Group = group, Name = name };
            for (int i = 0; i < proxyCount; i++)
            {
                var src = node.gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                def.Proxies.Add(new Proxy { Source = src, Priority = int.MinValue, Handle = 0 });
            }
            _groups[group] = def;
        }

        /// <summary>播放（2D）：组内空闲代理轮转取用；全忙按优先级抢占（同优先级不替换，无法抢占即丢弃）。</summary>
        public int Play(Group group, AudioClip clip, float volume = 1f, bool loop = false, int priority = 0, float fadeIn = 0f)
        {
            return PlayInternal(group, clip, volume, loop, priority, spatialBlend: 0f, position: null, fadeIn);
        }

        /// <summary>
        /// 播放（3D 空间化）：spatialBlend=1 为全 3D；音源置于 <paramref name="position"/>（一次性定位，
        /// 不跟随实体——跟随版待真实需求）。代理复用时由 PlayInternal 统一复位 2D/位置，无状态泄漏。
        /// </summary>
        public int Play3D(Group group, AudioClip clip, Vector3 position, float volume = 1f, int priority = 0,
            float spatialBlend = 1f, float fadeIn = 0f)
        {
            return PlayInternal(group, clip, volume, false, priority, Mathf.Clamp01(spatialBlend), position, fadeIn);
        }

        private int PlayInternal(Group group, AudioClip clip, float volume, bool loop, int priority,
            float spatialBlend, Vector3? position, float fadeIn)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            var g = _groups[group];

            Proxy pick = null;
            for (int i = 0; i < g.Proxies.Count; i++)
            {
                var p = g.Proxies[(g.Cursor + i) % g.Proxies.Count];
                if (!p.Busy) { pick = p; g.Cursor = (g.Cursor + i + 1) % g.Proxies.Count; break; }
            }

            if (pick == null)
            {
                Proxy steal = null;                        // 抢占目标 = 被占代理中优先级最低者
                foreach (var p in g.Proxies)
                    if (priority > p.Priority && (steal == null || p.Priority < steal.Priority))
                        steal = p;
                if (steal == null)
                {
                    Log.Info($"声音[{group}] 全忙且同优先级——丢弃（同优先级不被替换，手册 §六）", "Audio");
                    return 0;                              // 0 = 无效句柄
                }
                pick = steal;
                Log.Info($"声音[{group}] 抢占代理（新优先级 {priority} > 旧 {steal.Priority}）", "Audio");
            }

            // 评审 6.3：句柄单调递增且永不复用——被抢占代理的旧句柄必然失效，
            // Stop(旧句柄) 走安全忽略分支，外部持有旧句柄不会误停新音。
            pick.Handle = _nextHandle++;
            pick.Priority = priority;
            pick.BaseVolume = volume;

            var src = pick.Source;
            src.clip = clip;
            src.loop = loop;
            src.mute = g.Mute;
            src.spatialBlend = spatialBlend;               // 2D/3D 复位（代理复用无状态泄漏）
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1f;
            src.maxDistance = 30f;
            if (position.HasValue) src.transform.position = position.Value;
            else src.transform.localPosition = Vector3.zero;

            if (fadeIn > 0f)
            {
                src.volume = 0f;                           // 淡入从 0 起步
                src.Play();
                FadeAsync(g, pick, pick.Handle, 0f, 1f, fadeIn).Forget();
            }
            else
            {
                ApplyVolume(g, pick, 1f);
                src.Play();
            }
            return pick.Handle;
        }

        /// <summary>
        /// 停止指定句柄。<paramref name="fadeOut"/> &gt; 0 时先淡出再停（淡出期间代理仍占用，
        /// 可被更高优先级抢占——抢占会取消淡出）。失效句柄安全忽略。
        /// O(总代理数) 有界遍历（构造期定死，默认 9）；Stop 为低频操作，不建句柄索引（评审 6.2 决议）。
        /// </summary>
        public void Stop(int handle, float fadeOut = 0f)
        {
            foreach (var g in _groups.Values)
                foreach (var p in g.Proxies)
                    if (p.Handle == handle)
                    {
                        if (fadeOut > 0f)
                        {
                            FadeAsync(g, p, handle, 1f, 0f, fadeOut).Forget();
                            return;
                        }
                        ReleaseProxy(p);
                        return;
                    }
        }

        /// <summary>组音量（实时作用于组内全部代理；叠加音效一起变）。</summary>
        public void SetGroupVolume(Group group, float volume)
        {
            var g = _groups[group];
            g.Volume = Mathf.Clamp01(volume);
            foreach (var p in g.Proxies) ApplyVolume(g, p, 1f);
        }

        /// <summary>组静音（Source.mute——播放状态保留，解除静音无缝续播）。</summary>
        public void SetGroupMute(Group group, bool mute)
        {
            var g = _groups[group];
            g.Mute = mute;
            foreach (var p in g.Proxies) p.Source.mute = mute;
        }

        public bool IsGroupMuted(Group group) => _groups[group].Mute;

        /// <summary>全局停止（G1 通用表现批：Audio 总线统一停止面）——全部组全部在播代理立即停
        /// （系统路径不等淡出表现；失效句柄语义不变）。用户语义的逐句柄淡出走 <see cref="Stop"/>。</summary>
        public void StopAll()
        {
            int stopped = 0;
            foreach (var g in _groups.Values)
                foreach (var p in g.Proxies)
                    if (p.Busy) { ReleaseProxy(p); stopped++; }
            if (stopped > 0) Log.Info($"音频全局停止：{stopped} 个在播代理", "Audio");
        }

        /// <summary>释放面（宿主关闭，G1"统一取消与释放"）：全局停止 + 销毁 [Audio] 根（幂等）。</summary>
        public void Shutdown()
        {
            if (_root == null) return;                       // 幂等（_root 置空即已关闭）
            StopAll();
            var go = _root.gameObject;
            _root = null;
            _groups.Clear();
#if UNITY_EDITOR
            if (!Application.isPlaying) { UnityEngine.Object.DestroyImmediate(go); return; }
#endif
            UnityEngine.Object.Destroy(go);
            Log.Info("音频壳已关闭（全局停止 + 根销毁）", "Audio");
        }

        public string StatsName => "Audio";

        public void Snapshot(Dictionary<string, string> into)
        {
            foreach (var kv in _groups)
            {
                int busy = 0;
                foreach (var p in kv.Value.Proxies) if (p.Busy) busy++;
                into[$"{kv.Value.Name}{(kv.Value.Mute ? "(静音)" : "")}"] = $"{busy}/{kv.Value.Proxies.Count}";
            }
        }

        // ---- 内部 ----

        private void ApplyVolume(GroupDef g, Proxy p, float k)
        {
            p.Source.volume = Mathf.Clamp01(p.BaseVolume * g.Volume * k);
        }

        private void ReleaseProxy(Proxy p)
        {
            p.Source.Stop();
            p.Handle = 0;
            p.Priority = int.MinValue;
        }

        /// <summary>
        /// 音量淡变（fire-and-forget）：k 从 from 到 to 线性过渡，音量 = BaseVolume × 组音量 × k。
        /// **时钟域**：按组取 <see cref="DeltaSeconds"/>——Effect/Voice 吃 WorldClock（世界暂停即停），
        /// Ui/Bgm 吃 UIClock。暂停期间 dt 为 0，淡变原地保持（不推进也不误判完成）。
        /// 自检退出：句柄被抢占/停止即取消（不靠时钟推进来发现失效）。
        /// </summary>
        private async UniTaskVoid FadeAsync(GroupDef g, Proxy p, int handle, float fromK, float toK, float duration)
        {
            if (duration <= 0f) return;
            float t = 0f;
            while (p.Handle == handle)
            {
                float dt = DeltaSeconds(g.Group);
                if (dt > 0f)
                {
                    t += dt;
                    float k = Mathf.Lerp(fromK, toK, Mathf.Clamp01(t / duration));
                    ApplyVolume(g, p, k);
                    if (t >= duration) break;
                }
                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            if (p.Handle == handle && toK <= 0f) ReleaseProxy(p);   // 淡出到位 → 释放代理
        }
    }
}
