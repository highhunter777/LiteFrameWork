using System.Collections.Generic;
using LiteSim;
using LiteSim.View;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// C2 批② 表现视图验收（全替身、零网络、零资源包）：
    /// - SimView：槽位镜像增删、远端快照插值、本地预测跟随与和解衰减、硬切、**事件静默门**去重；
    /// - EntityViewMap：按槽位索引、按 prefab 分池复用、回收计数回落（泄漏断言）。
    ///
    /// 纯数学（插值/衰减/硬切判据）在 L1（Tests/LiteSim.Core.Tests/ViewTransformMathTests）——本类只验
    /// 引擎侧接线与生命周期。**输入服务**（上下文门/帧边界门/采样节流）同在 L1
    /// （Tests/LiteSim.Core.Tests/InputServiceTests）——设备源的相机换算属本层，但需真实 Input 状态，
    /// 由夜间 L2 与 Player 冒烟覆盖。
    /// </summary>
    public sealed class SimViewEditModeTests : UnityTestBase
    {
        private sealed class ViewCounter
        {
            public int Created;
            public readonly List<GameObject> Recycled = new List<GameObject>();
        }

        /// <summary>视图工厂替身：造一个带名字的空件（名字 = location，供池键识别）。</summary>
        private static SimView.ViewFactory Factory(ViewCounter counter)
            => (location, parent) =>
            {
                counter.Created++;
                var go = new GameObject(location);
                if (parent != null) go.transform.SetParent(parent, false);
                return go;
            };

        private static SimWorldState NewWorld(params SimVector3[] positions)
        {
            var world = new SimWorldState { RngState = 1UL };
            foreach (var p in positions)
                world.Spawn(new EntitySlot { Hp = 100, Pos = p, Yaw = 0f }, out int _);
            return world;
        }

        private static SimView NewView(SimWorldState world, ViewCounter counter,
            ICameraService camera = null, System.Func<EntitySlot, string> locationOf = null)
            => new SimView(world, null, Factory(counter), counter.Recycled.Add, camera, locationOf);

        /// <summary>无回收方形态：EntityViewMap 走**自有池**（生产接 EntityService 时池归它所有，
        /// 两份池不重复记账——这是设计口径）。</summary>
        private static SimView NewViewOwnPool(SimWorldState world, ViewCounter counter)
            => new SimView(world, null, Factory(counter), recycler: null);

        // ---- 槽位镜像 ----

        [Test]
        public void 镜像_活体建视图_死亡回收()
        {
            var world = NewWorld(new SimVector3(1f, 0f, 2f), new SimVector3(3f, 0f, 4f));
            var counter = new ViewCounter();
            var view = NewView(world, counter);

            view.Tick(1f / 60f);
            Assert.AreEqual(2, view.ViewCount, "两个活体各建一个视图");
            Assert.AreEqual(2, counter.Created);

            world.Despawn(world.Entities[0].Id);             // 槽位 0 死亡
            view.Tick(1f / 60f);
            Assert.AreEqual(1, view.ViewCount, "死亡槽位视图被回收");
            Assert.AreEqual(1, counter.Recycled.Count, "回收交给回收方（不静默丢弃）");
        }

        [Test]
        public void 镜像_视图位置跟随Sim态_朝向按Yaw()
        {
            var world = NewWorld(new SimVector3(5f, 0f, -3f));
            var counter = new ViewCounter();
            var view = NewView(world, counter);

            world.Entities[0].Yaw = Mathf.PI / 2f;           // 绕 Y 轴 90°
            view.Tick(1f / 60f);

            Assert.AreEqual(1, view.ViewCount);
        }

        [Test]
        public void 镜像_视图复用_同prefab从池取不重建()
        {
            var world = NewWorld(new SimVector3(0f, 0f, 0f));
            var counter = new ViewCounter();
            var view = NewViewOwnPool(world, counter);

            view.Tick(1f / 60f);
            Assert.AreEqual(1, counter.Created);

            world.Despawn(world.Entities[0].Id);
            view.Tick(1f / 60f);

            // 新实体（同 prefab）——回收方为 null 时走自有池，不再新建视图
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(9f, 0f, 9f) }, out int slot);
            view.Tick(1f / 60f);
            Assert.AreEqual(1, view.ViewCount, "回收的视图被新实体复用");
            Assert.AreEqual(1, counter.Created, "池命中不重新建视图");
        }

        // ---- 远端插值 ----

        [Test]
        public void 远端插值_两份快照之间平滑推进()
        {
            var world = NewWorld(new SimVector3(100f, 0f, 0f));   // 本地世界里的远端实体
            var counter = new ViewCounter();
            var view = NewView(world, counter);
            view.LocalEntityId = 999;                             // 不匹配任何实体 → 走远端插值分支

            view.Tick(1f / 60f);                                   // 建视图
            Assert.IsTrue(view.TryGetView(0, out var go), "槽位 0 视图已建");
            Assert.AreEqual(100f, go.transform.position.x, 0.01f, "无插值源时退回预测态位置");

            // 两份快照：帧 1 在 x=0，帧 2 在 x=10
            var snapA = new SimWorldStateSnapshot();
            var simA = NewWorld(new SimVector3(0f, 0f, 0f));
            snapA.CaptureFull(simA);
            var snapB = new SimWorldStateSnapshot();
            var simB = NewWorld(new SimVector3(10f, 0f, 0f));
            snapB.CaptureFull(simB);

            view.OnAuthoritativeSnapshot(snapA);
            view.OnAuthoritativeSnapshot(snapB);                  // From=A To=B 窗口起点

            view.Tick(0f);                                         // alpha = 0 → 停在 A（x=0）
            view.TryGetView(0, out go);
            Assert.AreEqual(0f, go.transform.position.x, 0.01f, "窗口起点 = 旧快照位置");

            view.Tick(SimView.SnapshotInterval);                   // alpha = 1 → 到 B
            view.TryGetView(0, out go);
            Assert.AreEqual(10f, go.transform.position.x, 0.01f, "窗口终点 = 新快照位置");

            view.Tick(SimView.SnapshotInterval * 10f);             // 快照停摆：停在最新不外推
            view.TryGetView(0, out go);
            Assert.AreEqual(10f, go.transform.position.x, 0.01f, "快照停摆不无限外推");
        }

        [Test]
        public void 远端插值_前后快照跳变超SnapDistance硬切_不播成飞人()
        {
            var world = NewWorld(new SimVector3(0f, 0f, 0f));     // 预测态里的远端实体（无插值源时的退回位）
            var counter = new ViewCounter();
            var view = NewView(world, counter);
            view.LocalEntityId = 999;                             // 不匹配任何实体 → 走远端插值分支
            view.SnapDistance = 3f;

            view.Tick(1f / 60f);                                   // 建视图
            Assert.IsTrue(view.TryGetView(0, out var go));

            // 前后快照：x=0 → x=50（权威侧传送/复活级跳变；两份世界同序 Spawn → 同实体 Id 可互相找到）
            var snapA = new SimWorldStateSnapshot();
            snapA.CaptureFull(NewWorld(new SimVector3(0f, 0f, 0f)));
            var snapB = new SimWorldStateSnapshot();
            snapB.CaptureFull(NewWorld(new SimVector3(50f, 0f, 0f)));

            view.OnAuthoritativeSnapshot(snapA);
            view.OnAuthoritativeSnapshot(snapB);

            // 窗口内任意 alpha 都必须直接落新位置——插值会把 50m 跳变播成 33ms 横穿地图的"飞人"
            view.Tick(0f);                                         // alpha = 0（旧实现停在 x=0 再起步飞越）
            view.TryGetView(0, out go);
            Assert.AreEqual(50f, go.transform.position.x, 0.01f, "跳变超阈值 → 远端硬切（§6.2 远端必要时 snap）");

            view.Tick(SimView.SnapshotInterval * 0.5f);           // 窗口中点：稳定在新位置，不回头插值
            view.TryGetView(0, out go);
            Assert.AreEqual(50f, go.transform.position.x, 0.01f, "硬切后不横穿（无中途飞越帧）");
        }

        // ---- 本地预测/衰减 ----

        [Test]
        public void 本地_首帧落位_随后跟预测()
        {
            var world = NewWorld(new SimVector3(1f, 0f, 1f));
            long id = world.Entities[0].Id;
            var counter = new ViewCounter();
            var view = NewView(world, counter);
            view.AlignLocal(id);

            view.Tick(1f / 60f);
            Assert.IsTrue(view.HasLocalDisplay);
            Assert.AreEqual(1f, view.LocalDisplayPosition.x, 0.01f);
        }

        [Test]
        public void 本地_大幅位移触发硬切_小位移走衰减()
        {
            var world = NewWorld(new SimVector3(0f, 0f, 0f));
            long id = world.Entities[0].Id;
            var counter = new ViewCounter();
            var view = NewView(world, counter);
            view.AlignLocal(id);
            view.SnapDistance = 3f;
            view.ReconcileSharpness = 8f;

            view.Tick(1f / 60f);                                   // 落到 (0,0,0)

            world.Entities[0].Pos = new SimVector3(1f, 0f, 0f);     // 小位移 → 衰减（未到 1.0）
            view.Tick(1f / 60f);
            float afterSmall = view.LocalDisplayPosition.x;
            Assert.Greater(afterSmall, 0f, "应向权威推进");
            Assert.Less(afterSmall, 1f, "单帧衰减不瞬移（平滑）");

            world.Entities[0].Pos = new SimVector3(50f, 0f, 0f);    // 超 SnapDistance → 硬切
            view.Tick(1f / 60f);
            Assert.AreEqual(50f, view.LocalDisplayPosition.x, 0.01f, "复活/传送硬切");
        }

        // ---- 事件静默门 ----

        [Test]
        public void 事件静默门_正常帧派发_静默帧丢弃()
        {
            var world = NewWorld(new SimVector3(0f, 0f, 0f));
            var counter = new ViewCounter();
            var view = NewView(world, counter);

            int delivered = 0;
            view.EventSink = (in FrameEvent e) => delivered++;

            // 逻辑帧边界交付（接 RollbackSim.OnFrameEvents——事件在 FrameDriver 清空缓冲前取件）
            world.Events.Write(FrameEventKind.Hit, 1, 2, -10, new SimVector3(0f, 0f, 0f));
            view.OnFrameEvents(world);
            Assert.AreEqual(1, delivered, "非静默帧事件正常派发");
            Assert.AreEqual(0, view.SilencedEvents);
            world.Events.Clear();                                   // FrameDriver 的消费后清空（决策⑥）

            // 静默闸推进到当前帧之后：重放段事件一律不派发
            view.Silence(world.Frame + 5);
            world.Events.Write(FrameEventKind.Hit, 1, 2, -10, new SimVector3(0f, 0f, 0f));
            view.OnFrameEvents(world);
            Assert.AreEqual(1, delivered, "静默帧内事件不派发（回滚重放不重播）");
            Assert.AreEqual(1, view.SilencedEvents, "被静默计数留痕");
        }

        /// <summary>
        /// 帧事件**不在渲染帧轮询**（回归卡）：Sim 事件是帧内瞬态，FrameDriver 在逻辑帧回调返回后
        /// 立即清空缓冲（决策⑥）——轮询要么读不到、要么重复读。本用例钉死"交付走 OnFrameEvents"。
        /// </summary>
        [Test]
        public void 事件静默门_渲染帧不轮询事件_只走逻辑帧交付()
        {
            var world = NewWorld(new SimVector3(0f, 0f, 0f));
            var counter = new ViewCounter();
            var view = NewView(world, counter);

            int delivered = 0;
            view.EventSink = (in FrameEvent e) => delivered++;

            world.Events.Write(FrameEventKind.Fire, 1, 0, 0, new SimVector3(0f, 0f, 0f));
            view.Tick(1f / 60f);                                     // 渲染帧驱动
            Assert.AreEqual(0, delivered, "渲染帧不轮询帧事件（否则同一事件每帧重复派发）");

            view.OnFrameEvents(world);                               // 逻辑帧边界交付
            Assert.AreEqual(1, delivered, "逻辑帧交付恰好一次");
        }

        [Test]
        public void 事件静默门_和解与回滚接缝推进闸门()
        {
            var world = NewWorld(new SimVector3(0f, 0f, 0f));
            var counter = new ViewCounter();
            var view = NewView(world, counter);

            Assert.AreEqual(-1, view.SilenceUntilFrame);
            view.OnReconcile(100);
            Assert.AreEqual(100, view.SilenceUntilFrame, "和解推进静默闸");
            view.OnRollback(50);
            Assert.AreEqual(100, view.SilenceUntilFrame, "回滚不回退闸门（取较大值）");
            view.OnRollback(200);
            Assert.AreEqual(200, view.SilenceUntilFrame, "回滚推进更远的闸门");
        }

        // ---- 相机（端口）：SimView 只把本地表现位置喂给 ICameraService ----
        // 真实实现（CinemachineCameraService）属 Platform.Unity 适配器：它要场景里的虚拟相机与
        // CinemachineBrain，归 PlayMode/Player 验证；本层用替身钉住**契约**（喂什么位置、何时开始）。

        [Test]
        public void 相机_跟随本地表现位置_按渲染帧喂给相机端口()
        {
            var world = NewWorld(new SimVector3(4f, 0f, 6f));
            long id = world.Entities[0].Id;
            var counter = new ViewCounter();
            var cam = new CameraProbe();

            var view = NewView(world, counter, camera: cam);
            view.AlignLocal(id);

            view.Tick(1f / 60f);
            Assert.AreEqual(1, cam.FollowCount, "每渲染帧一次");
            Assert.AreEqual(4f, cam.LastTarget.x, 0.1f, "喂的是本地表现位置（X）");
            Assert.AreEqual(6f, cam.LastTarget.z, 0.1f, "喂的是本地表现位置（Z）");
        }

        [Test]
        public void 相机_未对齐本地实体时不喂_对齐后才开始()
        {
            var world = NewWorld(new SimVector3(1f, 0f, 2f));
            var counter = new ViewCounter();
            var cam = new CameraProbe();

            var view = NewView(world, counter, camera: cam);
            view.Tick(1f / 60f);                       // 未 AlignLocal：无本地表现
            Assert.AreEqual(0, cam.FollowCount, "没有本地表现就不该驱动相机");

            view.AlignLocal(world.Entities[0].Id);
            view.Tick(1f / 60f);
            Assert.AreEqual(1, cam.FollowCount);
        }

        /// <summary>相机端口替身：只记"喂了几次、喂的什么"。</summary>
        private sealed class CameraProbe : ICameraService
        {
            public int FollowCount;
            public Vector3 LastTarget;
            public bool HasFocus { get; private set; }
            public Vector3 Focus => LastTarget;

            public void Follow(in Vector3 target, float deltaSeconds)
            {
                FollowCount++;
                LastTarget = target;
                HasFocus = true;
            }

            public void Reset() => HasFocus = false;
            public void Shutdown() { }
        }

        // ---- 输入设备源 ----
        // 原键鼠源（KeyboardMouseIntentSource）已删除，改由 Platform.Unity 适配器的
        // NewInputIntentSource 承担（New Input System：读 InputAction，需要真实设备与 Action 资产，
        // 归 Player/真机验证）。三个门（上下文门/帧边界门/采样节流）与设备源契约由 L1 覆盖
        // （Tests/LiteSim.Core.Tests/InputServiceTests.cs）。
    }
}
