using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LiteTesting.Unity
{
    /// <summary>
    /// PlayMode 版测试所有权（《UI测试开发专项设计》§2.2 目标目录 `LiteTesting/Runtime`；
    /// 《框架先行》§4 包③退出条件"真 Lua/Prefab/动画资源组合运行，取消、暂停、复用、卸载可验证"）。
    ///
    /// 与 Editor 版 <c>UnityTestScope</c> 的差异——**两处都是硬约束，不是风格选择**：
    /// - 销毁走 <see cref="Object.Destroy"/>，不是 <c>DestroyImmediate</c>：播放态下 Immediate 会破坏
    ///   正在进行的帧，且销毁要等帧末才能真正落地（"页面关闭 ≠ 资源清理完成"，§7.3）。
    /// - **不引用 UnityEditor**：本程序集是 Runtime 程序集，Player 测试构建同样链接它。资产类操作
    ///   （临时 AssetDatabase 目录等）归 Editor 版，PlayMode 用例不构造持久资产。
    ///
    /// 清理是**协程式**的：<see cref="DisposeAsync"/> 销毁对象并让出一帧再断言"确实没了"——
    /// 同步 Dispose 无法证明卸载完成，会把"排队待销毁"误判成"已清理"。
    /// </summary>
    public sealed class PlayModeTestScope : IDisposable
    {
        private readonly TestScope _core;
        private readonly List<Object> _objects = new List<Object>();
        private bool _disposed;

        public PlayModeTestScope(string testId, TestRunSettings settings = null)
        {
            _core = new TestScope(testId, settings);
        }

        public TestScope Core => _core;

        /// <summary>创建并纳入所有权（测试结束时销毁）。</summary>
        public GameObject CreateGameObject(string name, params Type[] components)
        {
            ThrowIfDisposed();
            var gameObject = new GameObject(name, components ?? Array.Empty<Type>());
            _objects.Add(gameObject);
            return gameObject;
        }

        /// <summary>纳入既有对象的所有权（含实例化出的 prefab 实例）。</summary>
        public T Track<T>(T instance) where T : Object
        {
            ThrowIfDisposed();
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            _objects.Add(instance);
            return instance;
        }

        /// <summary>当前仍存活的受管对象数（诊断：泄漏断言用）。</summary>
        public int LiveCount
        {
            get
            {
                int live = 0;
                for (int i = 0; i < _objects.Count; i++)
                    if (_objects[i] != null) live++;
                return live;
            }
        }

        /// <summary>
        /// 协程清理：销毁全部受管对象，**让出一帧**，再核对确实已销毁。
        /// PlayMode 用例应以 `yield return scope.DisposeAsync()` 收尾，而不是 `using`
        /// ——同步 Dispose 看不到帧末才落地的销毁结果。
        /// </summary>
        public IEnumerator DisposeAsync()
        {
            if (_disposed) yield break;
            _disposed = true;

            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) Object.Destroy(_objects[i]);

            _objects.Clear();
            yield return null;                               // 等帧末销毁落地

            _core.Dispose();                                 // 清理失败会抛——与断言失败同等对待
        }

        /// <summary>同步兜底（用例异常退出时的 TearDown 路径；不保证帧末销毁已落地）。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            List<Exception> failures = null;
            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (_objects[i] != null) Object.Destroy(_objects[i]);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }
            _objects.Clear();

            try { _core.Dispose(); }
            catch (Exception exception) { (failures ??= new List<Exception>()).Add(exception); }

            if (failures != null) throw new AggregateException("PlayMode test cleanup failed.", failures);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PlayModeTestScope));
        }
    }
}
