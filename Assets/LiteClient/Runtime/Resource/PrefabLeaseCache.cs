using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;

namespace LiteClient
{
    /// <summary>
    /// 表现壳 prefab 租约缓存（《商业级通用客户端总设计》§8.2——"Prefab 租约从加载完成持有至
    /// 最后一个依赖实例销毁"）：VFX/Entity 等表现服务的加载口适配。
    ///
    /// 语义：按 location **持租约返回 prefab**——同 location 重复取用共享一份租约（引用计数在内容服务侧）；
    /// 表现服务的实例池当前为**常驻设计**（回收不卸载），故租约由本缓存持有到 <see cref="ReleaseAll"/>
    /// （宿主关闭/Presentation 模块 Shutdown）。UI 走自身的 <see cref="IUIPrefabLease"/> 生命周期
    /// （按 formId 到实例销毁），不经本缓存。按 location 的增量卸载（引用归零即释放）尚未实现
    /// （届时池需提供排空回调）。
    /// </summary>
    public sealed class PrefabLeaseCache
    {
        private readonly object _gate = new object();
        private readonly IContentService _content;
        private readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>(8);
        private readonly List<IDisposable> _leases = new List<IDisposable>(8);
        private bool _released;

        public PrefabLeaseCache(IContentService content)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
        }

        /// <summary>已持有的 location 数（诊断/断言用）。</summary>
        public int HeldLocations { get { lock (_gate) return _prefabs.Count; } }

        /// <summary>取 prefab（同 location 共享一份租约；首次经内容服务获取）。
        /// 失败原样上抛（含 location）；<see cref="ReleaseAll"/> 后拒绝再取。</summary>
        public async UniTask<GameObject> GetAsync(string location, CancellationToken ct = default)
        {
            lock (_gate)
            {
                if (_released)
                    throw new InvalidOperationException($"PrefabLeaseCache 已释放——不再提供 prefab（{location}）");
                if (_prefabs.TryGetValue(location, out var cached))
                    return cached;
            }

            var lease = await _content.AcquireAsync<GameObject>(location, ct: ct);
            lock (_gate)
            {
                if (_released)
                {
                    lease.Dispose();                        // 竞态：取到时已被释放——就地归还
                    throw new InvalidOperationException($"PrefabLeaseCache 已释放——不再提供 prefab（{location}）");
                }
                if (_prefabs.TryGetValue(location, out var cached))
                {
                    lease.Dispose();                        // 竞态：并发首取——后到者归还，共享先到者
                    return cached;
                }
                _prefabs[location] = lease.Asset;
                _leases.Add(lease);
                return lease.Asset;
            }
        }

        /// <summary>释放全部租约（宿主关闭面——表现服务实例池当前常驻，归一到关闭时统一释放；幂等）。</summary>
        public void ReleaseAll()
        {
            List<IDisposable> toRelease;
            lock (_gate)
            {
                if (_released) return;
                _released = true;
                toRelease = new List<IDisposable>(_leases);
                _leases.Clear();
                _prefabs.Clear();
            }
            for (int i = toRelease.Count - 1; i >= 0; i--) toRelease[i].Dispose();
        }
    }
}
