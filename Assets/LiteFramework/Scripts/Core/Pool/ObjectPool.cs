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
    /// - **池满（空闲数达 maxIdle）归还即销毁**——不淘汰他人、不自动扩容（调 maxIdle 是配置不是运行时行为）；
    /// - 重复归还为 UB：Debug（三宏并集）下抛，release 信任 Debug 全绿；
    /// - 主线程 only（框架约定）。
    /// **调用顺序（回调与可选接口 IPoolable 共存，职责分层）**：
    /// - Acquire：onGet（池策略）→ OnSpawn（对象自初始化）；
    /// - Release：OnDespawn（对象自清理）→ onRelease（池策略）→ 空闲已满则 onDestroy；
    /// - Trim/Clear 销毁的是空闲对象（Release 时已 OnDespawn），只走 onDestroy，**不会二次 OnDespawn**。
    ///
    /// 统计口径与 ReferencePool 一致（Created/Acquired/Released/Unused/Using/PeakUnused/Dropped）；
    /// 计数器不做条件编译——本类作为实例件无条件实现 IModuleStats（HUD 数据源），自增开销可忽略。
    /// </summary>
    public sealed class ObjectPool<T> : IModuleStats where T : class
    {
        private readonly Func<T> _create;
        private readonly Action<T> _onGet;
        private readonly Action<T> _onRelease;
        private readonly Action<T> _onDestroy;
        private readonly Queue<T> _idle;
        private readonly string _statsName;

        /// <summary>空闲容量上限。调小不立即裁剪存量，只影响后续归还；立即释放配 <see cref="Trim"/>。</summary>
        public int MaxIdle { get; private set; }

        public int UnusedCount => _idle.Count;
        public int UsingCount => _inUse;
        public long CreatedCount { get; private set; }
        public long AcquiredCount { get; private set; }
        public long ReleasedCount { get; private set; }
        public long DroppedCount { get; private set; }
        public int PeakUnused { get; private set; }

        private int _inUse;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        private readonly HashSet<T> _inUseSet = new HashSet<T>(16);   // 重复归还检测
#endif

        public ObjectPool(Func<T> create, Action<T> onGet = null, Action<T> onRelease = null,
                          Action<T> onDestroy = null, int maxIdle = 64, string statsName = null)
        {
            _create = create ?? throw new ArgumentNullException(nameof(create));
            _onGet = onGet;
            _onRelease = onRelease;
            _onDestroy = onDestroy;
            if (maxIdle < 0) throw new ArgumentOutOfRangeException(nameof(maxIdle));
            MaxIdle = maxIdle;
            _idle = new Queue<T>(Math.Min(maxIdle, 16));
            _statsName = statsName ?? $"ObjectPool.{typeof(T).Name}";
        }

        /// <summary>取对象；空池则 create。onGet（池策略）→ OnSpawn（对象自初始化）。OnSpawn 抛异常 → 销毁并回滚账目，异常传播。</summary>
        public T Acquire()
        {
            T obj = _idle.Count > 0 ? _idle.Dequeue() : CreateAndCount();
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
        /// onRelease（池策略，同前保护）→ 空闲已满则 onDestroy 丢弃，否则入池。
        /// </summary>
        public void Release(T obj)
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
                DroppedCount++;                                  // 超限：销毁，不让池随峰值膨胀
                RunOnDestroy(obj);
                return;
            }
            _idle.Enqueue(obj);
            if (_idle.Count > PeakUnused) PeakUnused = _idle.Count;
        }

        /// <summary>预热：消除业务高峰（进战斗）首帧的 new 尖峰。**count 超过上限时自动上调**——预热即显式声明容量。</summary>
        public void Preload(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count > MaxIdle) MaxIdle = count;
            while (count-- > 0 && _idle.Count < MaxIdle)
            {
                _idle.Enqueue(CreateAndCount());
                if (_idle.Count > PeakUnused) PeakUnused = _idle.Count;
            }
        }

        /// <summary>裁剪：把空闲存量裁到 keep，被裁对象走 onDestroy。在用对象不受影响。</summary>
        public void Trim(int keep)
        {
            if (keep < 0) throw new ArgumentOutOfRangeException(nameof(keep));
            while (_idle.Count > keep) RunOnDestroy(_idle.Dequeue());
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
            into["MaxIdle"] = MaxIdle.ToString();
        }
    }
}
