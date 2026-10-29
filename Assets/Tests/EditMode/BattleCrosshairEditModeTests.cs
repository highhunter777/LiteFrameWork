using System.Collections.Generic;
using System.IO;
using Luban;
using LiteSim;
using LiteView;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using LiteFramework;
using LiteBattle;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 对局准心驱动的 L2 EditMode 覆盖：**位置恒屏心**（TPS——画布中心系原点；位置契约由
    /// TpsCrosshairCenterEditModeTests 钉住）、
    /// 形态切换（本地预测态 Aiming 位 → Hip/Ads 两组）、散布张开（有效散布角 → 整体缩放，
    /// 腰射 > 瞄准、未装备回基线——满张倍率按 tbcrosshair 武器行，未装备/缺行回落防御默认）、
    /// **连发临时张开/后坐**（本地 Fire 帧事件逐发累加、钳上限、停火按回正速率衰减；后坐冲量
    /// 上跳并入位置、指数回弹）、可见性与系统光标（上下文门被拦 → 藏准心还光标；
    /// 本地表现未建 → 整体隐藏；Dispose 恢复光标）；**表值=防御默认值守卫**（tbcrosshair id=0 行
    /// 与 CrosshairFeel.Default 同值——错表/漂移即红）。位置与光标走注入位——EditMode 无鼠标/画布依赖，确定性。
    /// 真场景与真鼠标的手感对位（含连发/后坐幅度定档——GM 面板「准心调参」滑杆）归 PlayMode/手测；构建器幂等另由 run_script 验收。
    /// </summary>
    public sealed class BattleCrosshairEditModeTests : UnityTestBase
    {
        /// <summary>灰盒视图 + 本地对齐（IsAiming 走预测态分支，同 CharacterLocomotionEditModeTests 形态）。</summary>
        private SimView BuildLocalView(SimWorldState world, long selfId)
        {
            SimView view = new SimView(world, null,
                factory: (loc, parent) => Scope.CreateGameObject(loc),
                recycler: null);
            view.AlignLocal(selfId);
            view.Tick(1f / 60f);                        // 首帧落位：HasLocalDisplay 置真
            return view;
        }

        /// <summary>
        /// 画布根几何（对齐 Overlay Canvas 世界系：轴心=中心、位在 (960,540)、尺寸 1920x1080——
        /// 该系下"世界单位 = 屏幕像素"，ScreenPointToLocalPointInRectangle(cam=null) 与线上同一条数学路径）。
        /// </summary>
        private (RectTransform Plane, RectTransform Reticle) BuildCanvasRects()
        {
            GameObject planeGo = Scope.CreateGameObject("Plane", typeof(RectTransform));
            RectTransform plane = (RectTransform)planeGo.transform;
            plane.position = new Vector3(960f, 540f, 0f);
            plane.sizeDelta = new Vector2(1920f, 1080f);

            GameObject reticleGo = Scope.CreateGameObject("Reticle", typeof(RectTransform));
            RectTransform reticle = (RectTransform)reticleGo.transform;
            reticle.SetParent(plane, false);
            return (plane, reticle);
        }

        private (GameObject Hip, GameObject Ads) BuildFormObjects(RectTransform reticle)
        {
            GameObject hip = Scope.CreateGameObject("Hip", typeof(RectTransform));
            ((RectTransform)hip.transform).SetParent(reticle, false);
            GameObject ads = Scope.CreateGameObject("Ads", typeof(RectTransform));
            ((RectTransform)ads.transform).SetParent(reticle, false);
            return (hip, ads);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 准心_本地预测态瞄准位切Ads组_松开回Hip组()
        {
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);
            SimView view = BuildLocalView(world, selfId);
            var (plane, reticle) = BuildCanvasRects();
            var (hip, ads) = BuildFormObjects(reticle);

            var driver = new BattleCrosshairDriver(view, null, plane, reticle, hip, ads);

            world.Entities[0].Flags |= EntityFlags.Aiming;        // 本地预测态瞄准位（驱动读 SimView.IsAiming）
            driver.Tick(0f);
            Assert.IsTrue(ads.activeSelf, "瞄准位 → Ads 形");
            Assert.IsFalse(hip.activeSelf, "瞄准位 → Hip 收");

            world.Entities[0].Flags &= ~(uint)EntityFlags.Aiming;  // 松开右键
            driver.Tick(0f);
            Assert.IsTrue(hip.activeSelf, "松开 → 回腰射形");
            Assert.IsFalse(ads.activeSelf);

            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 准心_上下文门被拦_藏准心还光标_解拦恢复()
        {
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);
            SimView view = BuildLocalView(world, selfId);
            var (plane, reticle) = BuildCanvasRects();
            var (hip, ads) = BuildFormObjects(reticle);

            var input = new InputService();
            bool modalOpen = false;
            input.RegisterBlocker(new IntentGate.BlockerKey("ui.modal", "模态打开"), () => modalOpen);

            var cursor = new List<bool>();
            var driver = new BattleCrosshairDriver(view, input, plane, reticle, hip, ads,
                setCursorVisible: cursor.Add);

            modalOpen = true;
            input.SampleOnRenderFrame(default);                   // 渲染帧采样 = 门在此裁决（生产序）
            input.TryTakeForSend(out _);                          // 上行取值（本渲染帧结论消费一次）
            driver.Tick(0f);
            Assert.IsFalse(reticle.gameObject.activeSelf, "被拦 → 藏准心");
            Assert.AreEqual(true, cursor[cursor.Count - 1], "被拦 → 还系统光标（模态要点按钮）");

            modalOpen = false;
            input.SampleOnRenderFrame(default);
            input.TryTakeForSend(out _);                          // 生产序：新渲染帧采样后才重新开闸
            driver.Tick(0f);
            Assert.IsTrue(reticle.gameObject.activeSelf, "解拦 → 准心恢复");
            Assert.AreEqual(false, cursor[cursor.Count - 1], "对局内准心替代光标");

            driver.Dispose();
            Assert.AreEqual(true, cursor[cursor.Count - 1], "离场 → 光标恢复（不遗留隐藏态）");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 准心_按有效散布角缩放_腰射大于瞄准_未装备回基线()
        {
            ResetCrosshairTuning();   // 静态调参面隔离（本用例断言绝对缩放值）
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);
            SimView view = BuildLocalView(world, selfId);
            var (plane, reticle) = BuildCanvasRects();
            var (hip, ads) = BuildFormObjects(reticle);

            var driver = new BattleCrosshairDriver(view, null, plane, reticle, hip, ads);

            // 未装备：无散布信息 → 基线缩放（如实降级，不猜）
            driver.Tick(0f);
            Assert.AreEqual(1f, reticle.localScale.x, 1e-4f, "未装备 → 基线缩放 1");

            // 懒装备（WeaponSystem 槽 0 默认步枪满弹）→ 腰射有效散布角 = 表值 × HipSpreadFactor
            // （无表装配：满张倍率走防御默认行——与 tbcrosshair id=0 行同值，表守卫钉住）
            WeaponSystem.Run(world, new[] { new SimInputFrame { EntityId = selfId } }, WeaponTable.Default);
            driver.Tick(0f);
            float rifleSpread = WeaponConfig.Default.Spread;
            float span = CrosshairFeel.Default.StaticMaxScale - BattleCrosshairDriver.MinSpreadScale;
            float hipExpected = BattleCrosshairDriver.MinSpreadScale
                + span * Mathf.Clamp01(rifleSpread * CombatConfig.HipSpreadFactor / BattleCrosshairDriver.SpreadFullDegrees);
            Assert.AreEqual(hipExpected, reticle.localScale.x, 1e-3f, "腰射：有效散布角按 HipSpreadFactor 放大");

            // 瞄准（右键 ADS 位）→ 有效散布角 = 表值（不乘腰射倍率）
            world.Entities[0].Flags |= EntityFlags.Aiming;
            driver.Tick(0f);
            float adsExpected = BattleCrosshairDriver.MinSpreadScale
                + span * Mathf.Clamp01(rifleSpread / BattleCrosshairDriver.SpreadFullDegrees);
            Assert.AreEqual(adsExpected, reticle.localScale.x, 1e-3f, "瞄准：有效散布角 = 表值");
            Assert.Greater(hipExpected, adsExpected, "腰射准心比瞄准准心张开（腰射散布更大）");

            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 准心_本地表现未建_整体隐藏还光标()
        {
            var world = new SimWorldState { RngState = 1UL };
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);
            SimView view = new SimView(world, null,
                factory: (loc, parent) => Scope.CreateGameObject(loc),
                recycler: null);                                  // 未 AlignLocal/未 Tick：LocalEntityId=0、HasLocalDisplay=false
            var (plane, reticle) = BuildCanvasRects();
            var (hip, ads) = BuildFormObjects(reticle);

            var cursor = new List<bool>();
            var driver = new BattleCrosshairDriver(view, null, plane, reticle, hip, ads,
                setCursorVisible: cursor.Add);
            driver.Tick(0f);
            Assert.IsFalse(reticle.gameObject.activeSelf, "本地表现未建 → 整体隐藏");
            Assert.AreEqual(true, cursor[cursor.Count - 1], "未建局 → 光标可见");

            driver.Dispose();
        }

        // ---- 连发临时张开 / 后坐（帧事件驱动 + 表值 per 武器 + 回正节拍）----

        /// <summary>本地 Fire 帧事件上下文（对齐 <see cref="HitFeedbackDispatcher"/> Resolve 的 Fire 形：
        /// OtherId 恒 0、Role=Caused、Slot=开火者槽位）。</summary>
        private static HitFeedbackContext FireCtx(long selfId)
            => new HitFeedbackContext(FrameEventKind.Fire, selfId, 0L, 0, -1, 0, Vector3.zero, HitLocalRole.Caused);

        /// <summary>GM 调参覆写复位（进程级静态面——用例间隔离，防面板拖值渗入断言）。</summary>
        private static void ResetCrosshairTuning()
        {
            CrosshairTuning.StaticMaxDevOverride = -1f;
            CrosshairTuning.BurstGrowthDevOverride = -1f;
            CrosshairTuning.BurstCapDevOverride = -1f;
            CrosshairTuning.KickDevOverride = -1f;
            CrosshairTuning.BurstRecoverDevOverride = -1f;
            CrosshairTuning.KickRecoverDevOverride = -1f;
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 准心_连发临时张开_每发累加钳上限_停火指数回正()
        {
            ResetCrosshairTuning();
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);
            SimView view = BuildLocalView(world, selfId);
            var (plane, reticle) = BuildCanvasRects();
            var (hip, ads) = BuildFormObjects(reticle);
            WeaponSystem.Run(world, new[] { new SimInputFrame { EntityId = selfId } }, WeaponTable.Default);   // 懒装备（def id 0）

            var driver = new BattleCrosshairDriver(view, null, plane, reticle, hip, ads);       // 无表装配 → 防御默认行（与表 id=0 行同值）
            driver.Tick(0f);
            float staticScale = reticle.localScale.x;      // 腰射静态张开（前置：散布 > 0 ⇒ > 基线）
            Assert.Greater(staticScale, BattleCrosshairDriver.MinSpreadScale, "前置：腰射静态张开");

            // 逐发累加（3 发——事件间无 Tick 衰减：dt=0）
            for (int i = 0; i < 3; i++) driver.OnHitFeedback(FireCtx(selfId));
            driver.Tick(0f);
            Assert.AreEqual(staticScale + CrosshairFeel.Default.BurstGrowth * 3f, reticle.localScale.x, 1e-3f,
                "连发逐发累加（dt=0 无衰减）");

            // 钳上限：增量上限 = burst_cap − 基线（再 40 发远超——总值被连发上限钳住）
            for (int i = 0; i < 40; i++) driver.OnHitFeedback(FireCtx(selfId));
            driver.Tick(0f);
            Assert.AreEqual(CrosshairFeel.Default.BurstCap, reticle.localScale.x, 1e-3f, "连发张开钳到上限");
            Assert.AreEqual(43, driver.ShotPulseCount, "诊断面：逐发对账");

            // 停火回正：指数衰减——2s 后回静态 ±0.01（1/e ≈ 0.33s）
            const float dt = 1f / 60f;
            for (int i = 0; i < 120; i++) driver.Tick(dt);
            Assert.AreEqual(staticScale, reticle.localScale.x, 0.01f, "停火 ~2s 回静态（指数回正）");
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 准心_后坐冲量_上跳并入位置_指数回弹归零()
        {
            ResetCrosshairTuning();
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);
            SimView view = BuildLocalView(world, selfId);
            var (plane, reticle) = BuildCanvasRects();
            var (hip, ads) = BuildFormObjects(reticle);

            var driver = new BattleCrosshairDriver(view, null, plane, reticle, hip, ads);
            driver.Tick(0f);
            // TPS：屏心基准（画布中心系原点 = 屏心）——后坐断言一律相对该基准
            float baseY = reticle.localPosition.y;
            Assert.AreEqual(0f, baseY, 1e-3f, "前置：屏心 = 画布中心系原点（基准捕获）");

            driver.OnHitFeedback(FireCtx(selfId));
            driver.Tick(0f);
            Assert.AreEqual(baseY + CrosshairFeel.Default.Kick, reticle.localPosition.y, 1e-3f, "后坐：每发上跳冲量并入位置");

            // 指数回弹：0.1s ≈ 1/e（残余过清零阈前可验衰减式）
            const float dt = 1f / 60f;
            for (int i = 0; i < 6; i++) driver.Tick(dt);
            Assert.AreEqual(baseY + CrosshairFeel.Default.Kick
                * Mathf.Exp(-BattleCrosshairDriver.KickRecoverPerSecond * 6f * dt),
                reticle.localPosition.y, 1e-2f, "指数回弹（0.1s ≈ 1/e）");

            // 长时归零：残余过清零阈后收口（免浮点残尾常驻脏写）
            for (int i = 0; i < 60; i++) driver.Tick(dt);
            Assert.AreEqual(baseY, reticle.localPosition.y, 1e-3f, "回弹归零（清零收口）");
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 准心_手感按武器行_book行值驱动满张倍率()
        {
            ResetCrosshairTuning();
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out int slot);
            SimView view = BuildLocalView(world, selfId);
            var (plane, reticle) = BuildCanvasRects();
            var (hip, ads) = BuildFormObjects(reticle);

            // 懒装备（def id 0——EditMode 下 SimView 读口解析走 WeaponConfig 回落表，步枪行在册）
            WeaponSystem.Run(world, new[] { new SimInputFrame { EntityId = selfId } }, WeaponTable.Default);
            Assert.IsTrue(view.TryGetWeaponDefId(slot, out int defId) && defId == 0,
                "SimView 武器定义 Id 读口：本地预测态选中武器可解析");

            // book 行值 ≠ 防御默认行——满张倍率由 book 行驱动（不落代码常量/默认行）
            var rows = new Dictionary<int, CrosshairFeel>
            {
                [0] = new CrosshairFeel(2.2f, 0.1f, 2.6f, 8f),
            };
            var driver = new BattleCrosshairDriver(view, null, plane, reticle, hip, ads,
                feel: new CrosshairFeelProfile(rows));

            world.Entities[0].Flags |= EntityFlags.Aiming;   // 瞄准：有效散布角 = 表值（期望式确定）
            driver.Tick(0f);
            Assert.AreEqual(1f + (2.2f - 1f) * Mathf.Clamp01(1.2f / BattleCrosshairDriver.SpreadFullDegrees),
                reticle.localScale.x, 1e-3f, "满张倍率 = book 行值 2.2（按武器行驱动，非默认 1.6）");
            Assert.AreNotEqual(1f + (CrosshairFeel.Default.StaticMaxScale - 1f)
                * Mathf.Clamp01(1.2f / BattleCrosshairDriver.SpreadFullDegrees), reticle.localScale.x,
                "非默认行——表行→手感的分档面真实生效");

            // book 缺行 → 防御回落 Default（如实降级不猜）
            var noRow = new CrosshairFeelProfile(new Dictionary<int, CrosshairFeel> { [99] = CrosshairFeel.Default });
            var driver2 = new BattleCrosshairDriver(view, null, plane, reticle, hip, ads,
                feel: noRow);
            driver2.Tick(0f);
            Assert.AreEqual(1f + (CrosshairFeel.Default.StaticMaxScale - 1f)
                * Mathf.Clamp01(1.2f / BattleCrosshairDriver.SpreadFullDegrees),
                reticle.localScale.x, 1e-3f, "缺行 → 防御回落 Default 行");

            driver.Dispose();
            driver2.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 准心_表值与防御默认同源_tbcrosshair零行钉住Default()
        {
            cfg.Tables tables = LoadTables();   // 真实表集（与 ConfigService 同款 loader——错表在装载闸已拦，此处钉读面）
            cfg.crosshair row = tables.Tbcrosshair.GetOrDefault(0);
            Assert.IsNotNull(row, "tbcrosshair 缺 id=0 行");
            Assert.AreEqual(CrosshairFeel.Default.StaticMaxScale, row.StaticMaxScale, 1e-4f, "静态满张倍率同源（表↔防御默认）");
            Assert.AreEqual(CrosshairFeel.Default.BurstGrowth, row.BurstGrowth, 1e-4f, "连发增量同源");
            Assert.AreEqual(CrosshairFeel.Default.BurstCap, row.BurstCap, 1e-4f, "连发上限同源");
            Assert.AreEqual(CrosshairFeel.Default.Kick, row.Kick, 1e-4f, "后坐冲量同源");
        }

        /// <summary>直建真实表集（.bytes 缓存 + Luban 同步 loader——CombatValuesEditModeTests 同形态，
        /// 不经 YooAsset 异步链，EditMode 可拉起）。</summary>
        private static cfg.Tables LoadTables()
        {
            string dir = Path.Combine(ProjectRoot(), ConfigService.DataDir);
            var cache = new Dictionary<string, byte[]>(ConfigService.TableDataFiles.Length);
            foreach (string f in ConfigService.TableDataFiles)
                cache[TableLayout.TableName(f)] = File.ReadAllBytes(Path.Combine(dir, f + ".bytes"));
            return new cfg.Tables(file => new ByteBuf(cache[file]));
        }

        private static string ProjectRoot()
        {
            for (var c = new DirectoryInfo(Application.dataPath); c != null; c = c.Parent)
                if (Directory.Exists(Path.Combine(c.FullName, "Tests"))
                    && File.Exists(Path.Combine(c.FullName, "Tests", "Tests.slnx")))
                    return c.FullName;
            throw new DirectoryNotFoundException("找不到仓库根（需要 Tests/Tests.slnx）");
        }
    }
}
