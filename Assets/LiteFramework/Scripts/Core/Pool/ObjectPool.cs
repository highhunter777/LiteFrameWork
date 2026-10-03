using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 泛型实例池（**机制件**：自研替代 UnityEngine.Pool.ObjectPool）。
    /// 与静态 <see cref="ReferencePool"/> 的分工：纯数据对象（无创建/回收回调需求）走 ReferencePool 按类型池化；
    /// 带生命周期回调的实例（Buff、技能实例、GameObject 适配等）走本池（实例化后 DI 注册或由工厂持有）。
    ///
    /// 契约（所有权移交制）：
    /// - Acquire 得到的对象状态未定义，调用方必须完成初始化后使用；
    /// - **Release 即移交所有权**——调用方归还后禁止再持引用；挂载物清理遵循"谁挂谁清"（对象实现
    ///   <see cref="IPoolable"/> 时，OnDespawn 是生命周期容器的执行点）；
    /// - **池满（空闲数达 maxIdle）归还**：DropNewest 销毁新归还件 / EvictOldest 淘汰队首最旧——
    ///   不自动扩容（调 maxIdle 是配置不是运行时行为）；
    /// - 重复归还为 UB：Debug（三宏并集）下抛，release 信任 Debug 全绿；
    /// - 主线程 only（框架约定）。
    /// **调用顺序（回调与可选接口 IPoolable 共存，职责分层）**：
    /// - Acquire：空闲件先过 validateOnAcquire（验证失败即销毁、计 Invalidated，**先于** onGet/OnSpawn）→
    ///   onGet（池策略）→ OnSpawn（对象自初始化）；
    /// - Release：OnDespawn（对象自清理）→ onRelease（池策略）→ 空闲已满则按淘汰策略走 onDestroy；
    /// - Trim/Clear/TrimExpired 销毁的是空闲对象（Release 时已 OnDespawn），只走 onDestroy，**不会二次 OnDespawn**。
    ///
    /// 统计口径与 ReferencePool 一致（Created/Acquired/Released/Unused/Using/PeakUnused/Dropped，
    /// 另有 PoolHits/Evicted/Invalidated——Misses = Acquired − PoolHits 不另建键）；
    /// 计数器不做条件编译——本类作为实例件无条件实现 IModuleStats（HUD 数据源），自增开销可忽略。
    ///
    /// **零时钟纪律**：内核不持有任何时钟——空闲过期收缩（<see cref="TrimExpired"/>）由消费方驱动，
    /// 时刻只从调用方进入（Release/Preload 的可选 nowMs），同 FrameDriver 模式；不自动跑、无后台 Tick。
    /// </summary>
    public sealed class ObjectPool<T> : IModuleStats where T : class
    {
        private readonly Func<T> _create;
        private readonly Action<T> _onGet;
        private readonly Action<T> _onRelease;
        private readonly Action<T> _onDestroy;
        private readonly Queue<T> _idle;
        /// <summary>入池时刻队列（ms，消费方时钟域）——与 <see cref="_idle"/> 同进出序；
        /// 所有可能改变空闲队列前端的路径（Acquire 验证/淘汰、EvictOldest、Trim、TrimExpired）必须双队同步。</summary>
        private readonly Queue<long> _idleAtMs;
        private readonly PoolEviction _eviction;
        private readonly Func<T, bool> _validateOnAcquire;
        private readonly string _statsName;

        /// <summary>空闲容量上限。调小不立即裁剪存量，只影响后续归还；立即释放配 <see cref="Trim"/>。</summary>
        public int MaxIdle { get; private set; }

        public int UnusedCount => _idle.Count;
        public int UsingCount => _inUse;
        public long CreatedCount { get; private set; }
        public long AcquiredCount { get; private set; }
        public long ReleasedCount { get; private set; }
        public long DroppedCount { get; private set; }
        /// <summary>EvictOldest 淘汰数（与 <see cref="DroppedCount"/>（DropNewest 专属）不混用）。</summary>
        public long EvictedCount { get; private set; }
        /// <summary>借出验证失效销毁数。</summary>
        public long InvalidatedCount { get; private set; }
        /// <summary>池中直取数；Misses = Acquired − PoolHits，不另建键。复用率 = PoolHits / Acquired。</summary>
        public long PoolHits { get; private set; }
        public int PeakUnused { get; private set; }

        private int _inUse;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        private readonly HashSet<T> _inUseSet = new HashSet<T>(16);   // 重复归还检测
#endif

        public ObjectPool(Func<T> create, Action<T> onGet = null, Action<T> onRelease = null,
                          Action<T> onDestroy = null, int maxIdle = 64, string statsName = null,
                          PoolEviction eviction = PoolEviction.DropNewest, Func<T, bool> validateOnAcquire = null)
        {
            _create = create ?? throw new ArgumentNullException(nameof(create));
            _onGet = onGet;
            _onRelease = onRelease;
            _onDestroy = onDestroy;
            if (maxIdle < 0) throw new ArgumentOutOfRangeException(nameof(maxIdle));
            MaxIdle = maxIdle;
            _idle = new Queue<T>(Math.Min(maxIdle, 16));
            _idleAtMs = new Queue<long>(Math.Min(maxIdle, 16));
            _eviction = eviction;
            _validateOnAcquire = validateOnAcquire;
            _statsName = statsName ?? $"ObjectPool.{typeof(T).Name}";
        }

        /// <summary>
        /// 取对象；空池则 create。空闲件先过 validateOnAcquire（验证失败即销毁、计 Invalidated、继续取下一件，
        /// 池空转 create）——**验证在 onGet/OnSpawn 之前**（验证的是空闲件；被销毁件只走 onDestroy，与 Trim 同口径）。
        /// 验证回调抛异常：记日志、销毁该件、继续取下一件（作者代码缺陷不阻塞池可用性）。
        /// onGet（池策略）→ OnSpawn（对象自初始化）。OnSpawn 抛异常 → 销毁并回滚账目，异常传播。
        /// </summary>
        public T Acquire()
        {
            T obj = null;
            bool fromPool = false;
            while (_idle.Count > 0)
            {
                T candidate = _idle.Dequeue();
                _idleAtMs.Dequeue();
                if (_validateOnAcquire != null)
                {
                    bool valid;
                    try
                    {
                        valid = _validateOnAcquire(candidate);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"{_statsName}.validateOnAcquire");
                        RunOnDestroy(candidate);
                        InvalidatedCount++;
                        continue;
                    }
                    if (!valid)
                    {
                        InvalidatedCount++;
                        RunOnDestroy(candidate);
                        continue;
                    }
                }
                obj = candidate;
                fromPool = true;
                break;
            }
            if (obj == null)
            {
                obj = CreateAndCount();
                if (obj == null)
                {
                    // 工厂拒绝（如资源缺失）：不回调、不计账，调用方按 null 处理——
                    // 可空工厂的产出必须仍走 Acquire（经池登记），否则归还时 Debug 重复归还检测误伤
                    CreatedCount--;
                    return null;
                }
            }
            else
                PoolHits++;

            _inUse++;
            AcquiredCount++;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            _inUseSet.Add(obj);
#endif
            _onGet?.Invoke(obj);
            if (obj is IPoolable spawnable)
            {
                try
                {
                    spawnable.OnSpawn();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"{_statsName}.OnSpawn");   // 对象作者代码；炸了销毁并回滚账目（不留幽灵占用）
                    RollbackAcquire(obj);
                    throw;
                }
            }
            return obj;
        }

        private void RollbackAcquire(T obj)
        {
            _inUse--;
            AcquiredCount--;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            _inUseSet.Remove(obj);
#endif
            RunOnDestroy(obj);
        }

        /// <summary>
        /// 归还。流程：重复归还校验（Debug）→ OnDespawn（对象自清理，**作者代码，抛异常则销毁不入池污染**）→
        /// onRelease（池策略，同前保护）→ 空闲已满则按淘汰策略走 onDestroy，否则入池。
        /// nowMs：入池时刻（消费方时钟域，供 <see cref="TrimExpired"/> 按龄收缩；不用过期收缩的调用方不传）。
        /// </summary>
        public void Release(T obj, long nowMs = 0)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            if (!_inUseSet.Remove(obj))
                throw new InvalidOperationException($"ObjectPool<{typeof(T).Name}>: 重复归还或非 Acquire 所得");
#endif
            _inUse--;
            ReleasedCount++;
            try
            {
                (obj as IPoolable)?.OnDespawn();             // 对象自清理：先清自己的挂载，池再挪动它
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"{_statsName}.OnDespawn");    // 回调是对象作者代码；炸了销毁，不入池污染
                RunOnDestroy(obj);
                return;
            }
            try
            {
                _onRelease?.Invoke(obj);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"{_statsName}.onRelease");        // 回调是对象作者代码；炸了销毁，不入池污染
                RunOnDestroy(obj);
                return;
            }

            if (_idle.Count >= MaxIdle)
            {
                if (_eviction == PoolEviction.EvictOldest && _idle.Count > 0)
                {
                    // 淘汰队首最旧空闲件（走 onDestroy、计 Evicted），新归还件入池——热点件不被反复重建
                    EvictedCount++;
                    RunOnDestroy(_idle.Dequeue());
                    _idleAtMs.Dequeue();
                }
                else
                {
                    DroppedCount++;                              // DropNewest：销毁新归还件，不让池随峰值膨胀
                    RunOnDestroy(obj);
                    return;
                }
            }
            _idle.Enqueue(obj);
            _idleAtMs.Enqueue(nowMs);
            if (_idle.Count > PeakUnused) PeakUnused = _idle.Count;
        }

        /// <summary>预热：消除业务高峰（进战斗）首帧的 new 尖峰。**count 超过上限时自动上调**——预热即显式声明容量。
        /// nowMs：入池时刻（消费方时钟域；与 Release 的时刻同域，供 TrimExpired 按龄收缩）。</summary>
        public void Preload(int count, long nowMs = 0)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count > MaxIdle) MaxIdle = count;
            while (count-- > 0 && _idle.Count < MaxIdle)
            {
                var obj = CreateAndCount();
                _idle.Enqueue(obj);
                _idleAtMs.Enqueue(nowMs);
                if (_idle.Count > PeakUnused) PeakUnused = _idle.Count;
            }
        }

        /// <summary>裁剪：把空闲存量裁到 keep，被裁对象走 onDestroy。在用对象不受影响。</summary>
        public void Trim(int keep)
        {
            if (keep < 0) throw new ArgumentOutOfRangeException(nameof(keep));
            while (_idle.Count > keep)
            {
                RunOnDestroy(_idle.Dequeue());
                _idleAtMs.Dequeue();
            }
        }

        /// <summary>
        /// 按龄收缩（消费方驱动——时刻只从调用方进入，内核零时钟纪律）：从队首（最旧）销毁空闲时长
        /// **超过** maxIdleMs 的件，返回裁剪数。与 <see cref="Trim"/>（按量）互补：本方法按龄消灭
        /// "峰值后池常驻"。不自动跑、无后台 Tick。
        /// </summary>
        public int TrimExpired(long nowMs, long maxIdleMs)
        {
            int trimmed = 0;
            while (_idle.Count > 0 && nowMs - _idleAtMs.Peek() > maxIdleMs)
            {
                RunOnDestroy(_idle.Dequeue());
                _idleAtMs.Dequeue();
                trimmed++;
            }
            return trimmed;
        }

        /// <summary>销毁全部空闲对象（重开局/关服用）。**在用对象不受影响**——按契约归还时自然走完生命周期。</summary>
        public void Clear() => Trim(0);

        private T CreateAndCount()
        {
            var obj = _create();
            CreatedCount++;
            return obj;
        }

        private void RunOnDestroy(T obj)
        {
            _onDestroy?.Invoke(obj);
        }

        // ---- IModuleStats（HUD 数据源）----

        string IModuleStats.StatsName => _statsName;

        void IModuleStats.Snapshot(Dictionary<string, string> into)
        {
            into["Created"] = CreatedCount.ToString();
            into["Acquired"] = AcquiredCount.ToString();
            into["Released"] = ReleasedCount.ToString();
            into["Unused"] = UnusedCount.ToString();
            into["Using"] = UsingCount.ToString();
            into["PeakUnused"] = PeakUnused.ToString();
            into["Dropped"] = DroppedCount.ToString();
            into["Evicted"] = EvictedCount.ToString();
            into["Invalidated"] = InvalidatedCount.ToString();
            into["PoolHits"] = PoolHits.ToString();
            into["MaxIdle"] = MaxIdle.ToString();
        }
    }
}
