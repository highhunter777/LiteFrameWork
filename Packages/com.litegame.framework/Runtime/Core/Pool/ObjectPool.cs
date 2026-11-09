using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>泛型实例池（机制件：取还 + 池策略回调 + 借出验证）。</summary>
    public sealed class ObjectPool<T> where T : class
    {
        private readonly Func<T> _create;
        private readonly Action<T> _onGet;
        private readonly Action<T> _onRelease;
        private readonly Action<T> _onDestroy;
        private readonly Queue<T> _idle;
        private readonly Func<T, bool> _validateOnAcquire;
        private readonly string _statsName;
        private readonly int _maxIdle;
        private readonly HashSet<T> _inUseSet;   // 重复归还检测（null = 不检测；见 diagnoseDoubleRelease）

        public int UnusedCount => _idle.Count;

        public ObjectPool(Func<T> create, Action<T> onGet = null, Action<T> onRelease = null,
                          Action<T> onDestroy = null, int maxIdle = 64, string statsName = null,
                          Func<T, bool> validateOnAcquire = null, bool diagnoseDoubleRelease = false)
        {
            _create = create ?? throw new ArgumentNullException(nameof(create));
            if (maxIdle < 0) throw new ArgumentOutOfRangeException(nameof(maxIdle));
            _onGet = onGet;
            _onRelease = onRelease;
            _onDestroy = onDestroy;
            _maxIdle = maxIdle;
            _idle = new Queue<T>(Math.Min(maxIdle, 16));
            _validateOnAcquire = validateOnAcquire;
            _inUseSet = diagnoseDoubleRelease ? new HashSet<T>(16) : null;
            _statsName = statsName ?? $"ObjectPool.{typeof(T).Name}";
        }

        /// <summary>取对象；空池则 create。工厂返回 null = 资源缺失，按 null 返回（不回调、不入池）。</summary>
        public T Acquire()
        {
            T obj = null;
            while (_idle.Count > 0)
            {
                T candidate = _idle.Dequeue();
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
                        continue;
                    }
                    if (!valid)
                    {
                        RunOnDestroy(candidate);
                        continue;
                    }
                }
                obj = candidate;
                break;
            }
            if (obj == null)
            {
                obj = _create();
                if (obj == null) return null;
            }
            _inUseSet?.Add(obj);
            _onGet?.Invoke(obj);
            return obj;
        }

        /// <summary>归还；满池销毁新归还件（不保留峰值）。</summary>
        public void Release(T obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (_inUseSet != null && !_inUseSet.Remove(obj))
                throw new InvalidOperationException($"ObjectPool<{typeof(T).Name}>: 重复归还或非 Acquire 所得");
            try
            {
                _onRelease?.Invoke(obj);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"{_statsName}.onRelease");     // 回调是对象作者代码；炸了销毁，不入池污染
                RunOnDestroy(obj);
                return;
            }

            if (_idle.Count >= _maxIdle)
            {
                RunOnDestroy(obj);
                return;
            }
            _idle.Enqueue(obj);
        }

        /// <summary>销毁全部空闲对象（在用对象不受影响）。</summary>
        public void Clear()
        {
            while (_idle.Count > 0) RunOnDestroy(_idle.Dequeue());
        }

        private void RunOnDestroy(T obj) => _onDestroy?.Invoke(obj);
    }
}
