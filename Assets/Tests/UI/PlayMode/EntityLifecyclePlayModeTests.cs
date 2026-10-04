using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteClient;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LiteGame.Tests.UI.PlayMode
{
    /// <summary>
    /// 实体壳对象级故障段（准入项「生命周期收敛」Entity 段——对齐 VFX/动画/租约同族口径）。
    ///
    /// 依据：《框架先行建设与业务接入专项设计》§5 接缝 1（谁创建、谁取消、谁等待清理、谁释放资源）、
    /// §6 样例③「加载后 Owner 已退出」。此前 Entity 的故障面只有 EditMode 契约钉
    /// （ShellLifecycleEditModeTests：作用域取消/重复归还/关闭面/挂接级联——替身加载口），
    /// 缺**真池实例 + 确定性在途窗口**下的对象级故障：关闭后在途真取消、关闭后拒绝新实体、
    /// 加载完成时壳已关闭的幽灵复活、加载中宿主已退出、加载中 Hide 竞态。
    ///
    /// **为什么必须是 PlayMode**：在途窗口要真实帧推进（yield 数帧）才能确定性打开——
    /// EditMode 没有 PlayerLoop，同步加载口永远测不到竞态/取消语义。
    /// 池根注入测试作用域（PlayMode 下自建根会 DontDestroyOnLoad 跨用例驻留——对象级卫生）。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class EntityLifecyclePlayModeTests
    {
        private PlayModeTestScope _scope;
        private EntityService _entities;            // 非 UnityEngine.Object —— TearDown 显式关闭
        private readonly List<GameObject> _spawned = new List<GameObject>();   // loader 产出、被故障路径丢弃的件（收尾统一销毁）

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _scope = new PlayModeTestScope(nameof(EntityLifecyclePlayModeTests));
            _entities = null;
            _spawned.Clear();
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _entities?.Shutdown();                  // 幂等：用例内已关闭时再调无副作用
            _entities = null;
            foreach (GameObject go in _spawned)
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
            _spawned.Clear();
            yield return _scope.DisposeAsync();
        }

        /// <summary>建服务：池根注入测试作用域（自建根会 DontDestroyOnLoad 跨用例驻留）。</summary>
        private EntityService NewService(Func<string, CancellationToken, UniTask<GameObject>> loader, ClientScope host = null)
        {
            var poolRoot = _scope.CreateGameObject("EntityPool").transform;
            var service = new EntityService(loader, poolRoot) { HostScope = host };
            _entities = service;
            return service;
        }

        // ---- 关闭面：在途真取消 + 拒绝新实体 ----

        [UnityTest]
        public IEnumerator 实体_关闭后在途加载被真取消_且拒绝新实体()
        {
            EntityService service = NewService(ObservingSlowLoader);
            UniTask<EntityHandle> load = service.ShowAsync("ent_slow");
            yield return Null();
            Assert.AreEqual(1, service.InFlightCount, "在途窗口确定存在（yield 后仍未完成）");

            service.Shutdown();                     // 关闭：先行取消在途（生命周期令牌已链接加载口）
            yield return WaitSettled(load, 10f);

            Assert.AreEqual(UniTaskStatus.Canceled, load.Status, "在途加载得类型化取消终态（非 faulted、非悬挂）");
            Assert.AreEqual(0, service.ActiveCount, "关闭后不得有活体");
            Assert.AreEqual(0, service.PooledTotal, "关闭后池已排空且无新实例");
            Assert.AreEqual(0, service.InFlightCount, "在途清零");

            Assert.Throws<ObjectDisposedException>(
                () => service.ShowAsync("ent_a").GetAwaiter().GetResult(),
                "已关闭的实体壳不得接受新实体（同 SceneService 拒绝口径）");
            yield return Null();
        }

        [UnityTest]
        public IEnumerator 实体_加载完成时壳已关闭_不实例化不留幽灵()
        {
            // loader **不观察**取消令牌：取消后仍会完成——钉服务级收尾复查（不依赖 loader 纪律，
            // 同 VfxService「inst.Cancelled || IsShutdown」口径）。若无复查，加载完成会经池
            // 实例化并把幽灵实体登记在已关闭的壳上。
            EntityService service = NewService(ObliviousSlowLoader);
            UniTask<EntityHandle> load = service.ShowAsync("ent_oblivious");
            yield return Null();
            Assert.AreEqual(1, service.InFlightCount);

            service.Shutdown();
            yield return WaitSettled(load, 10f);

            Assert.AreEqual(UniTaskStatus.Succeeded, load.Status, "loader 不观察取消——任务正常完成（非 faulted）");
            Assert.IsNull(load.GetAwaiter().GetResult(), "收尾复查：取消显示返回 null，不给幽灵句柄");
            Assert.AreEqual(0, service.ActiveCount, "不得复活活体");
            Assert.AreEqual(0, service.PooledTotal, "不得实例化进池");
            yield return Null();
        }

        // ---- Owner 已退出（§6 样例③ Entity 形态）----

        [UnityTest]
        public IEnumerator 实体_加载中宿主已退出_显性失败_无泄漏()
        {
            using (var host = new ClientScope("Match"))
            {
                EntityService service = NewService(ObservingSlowLoader, host);
                UniTask<EntityHandle> load = service.ShowAsync("ent_owner");
                yield return Null();
                Assert.AreEqual(1, service.InFlightCount);

                host.Dispose();                     // Owner 在加载中退出
                yield return WaitSettled(load, 10f);

                Assert.AreEqual(UniTaskStatus.Faulted, load.Status, "Owner 退出 → 显性失败（不静默消失）");
                Assert.Throws<ObjectDisposedException>(
                    () => load.GetAwaiter().GetResult(),
                    "类型化 ObjectDisposedException（对照动画段 OwnerDisposed 终态）");
                Assert.AreEqual(0, service.ActiveCount, "不留活体");
                Assert.AreEqual(0, service.PooledTotal, "实例化前先检宿主——不得有件滞留池中");
            }
            yield return Null();
        }

        // ---- 加载中 Hide：竞态表对象级钉 ----

        [UnityTest]
        public IEnumerator 实体_加载中Hide_竞态表取消显示_零实例化()
        {
            EntityService service = NewService(ObservingSlowLoader);
            int id = service.Reserve();
            UniTask<EntityHandle> load = service.ShowAsync(id, "ent_race");
            yield return Null();
            Assert.AreEqual(1, service.InFlightCount);

            service.Hide(id);                       // 加载在途 → 竞态表
            yield return WaitSettled(load, 10f);

            Assert.AreEqual(UniTaskStatus.Succeeded, load.Status, "竞态是设计路径：正常完成非取消");
            Assert.IsNull(load.GetAwaiter().GetResult(), "竞态表命中：取消显示返回 null");
            Assert.AreEqual(0, service.DuplicateReturns, "竞态 Hide 不得计入重复归还（非纪律问题）");
            Assert.AreEqual(0, service.ActiveCount, "无活体");
            Assert.AreEqual(0, service.PooledTotal, "零实例化");
            yield return Null();
        }

        // ---- 辅助 ----

        /// <summary>观察取消令牌的慢加载器：让出数帧把在途窗口确定化（同 VFX SlowLoader 口径）。</summary>
        private async UniTask<GameObject> ObservingSlowLoader(string location, CancellationToken ct)
        {
            for (int i = 0; i < 5; i++) await UniTask.Yield(ct);
            return NewTrackedCube("ent-slow:" + location);
        }

        /// <summary>**不**观察取消令牌的慢加载器：取消后仍完成——收尾复查的确定性载体。</summary>
        private async UniTask<GameObject> ObliviousSlowLoader(string location, CancellationToken ct)
        {
            for (int i = 0; i < 5; i++) await UniTask.Yield();
            return NewTrackedCube("ent-oblivious:" + location);
        }

        private GameObject NewTrackedCube(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            _spawned.Add(go);                       // 故障路径丢弃 loader 产出——收尾统一销毁
            return go;
        }

        private static IEnumerator Null()
        {
            yield return null;
        }

        /// <summary>轮询至任务终结（有界超时；终结后的具体状态由用例精确断言）。</summary>
        private static IEnumerator WaitSettled<T>(UniTask<T> task, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (task.Status == UniTaskStatus.Pending && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsFalse(task.Status == UniTaskStatus.Pending, "等待任务终结超时（悬挂）");
        }
    }
}
