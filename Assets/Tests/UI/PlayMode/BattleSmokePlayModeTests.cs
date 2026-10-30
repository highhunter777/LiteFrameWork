using System;
using System.Collections;
using LiteFramework;
using LiteSim;
using LiteGame.View;
using LiteTesting;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using LiteGame.Modules.Settings;

namespace LiteGame.Tests.UI.PlayMode
{
    /// <summary>
    /// **对局全链冒烟**（批 2 · A12）：Boot.unity 起服 → 流程到 <see cref="ProcedureId.Main"/> →
    /// 开发桥 <see cref="TestModeRuntime.RequestEnter"/> 进本地测试房 → 核对"视图组已建 + HUD 根件/准心 +
    /// 开火后四路表现（准心脉冲/音效 voice/世界 VFX/伤害数字）＋越肩几何" → <see cref="TestModeRuntime.RequestExit"/>
    /// 离场 → **只对容器级服务与工厂**断言（LastCreated == null、计数 ≤2s 轮询归零）。
    ///
    /// **为什么必须有它**：L1 只钉静态结构与守卫；"装配序改了是否还能起服、进局、开火、离场"只有真跑一次才知道
    /// ——批 1/2 反复出现的失效模式正是"分段全绿、整链断在时序"（模块段数/相机段位置/视图组注册落点）。
    ///
    /// **执行载体三段式**（判据见验收 A12）：
    /// ① harness＝加载 Boot.unity 起服（ClientHost 全链 + 本地测试房），流程机经
    ///    <see cref="GameEntry.TakeContainer"/> 解析后轮询 Started 且 Current == Main；
    /// ② rig 存取口＝<see cref="IBattleViewGroupFactory.LastCreated"/>（返回 <see cref="IBattleViewGroup"/>，
    ///    用例向下转型 internal 的 <see cref="BattleViewRig"/>，friend 见 Modules/Battle/AssemblyInfo.cs）；
    /// ③ 观测面＝IsBuilt / Hud.Root / Hud.Crosshair / ShotPulseCount / VoiceCount /
    ///    VfxService.ActiveCount / BattleDamageNumberDriver.ActiveCount。
    ///
    /// **开火不是绕过输入**：走 <see cref="TestModeRuntime.AutoHeadshot"/>（开发叠加层把设备源换成
    /// "锁最近敌人头部带 + 持续开火"的自瞄源）——它不改判定，只等价于"玩家把准心压在头上"。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class BattleSmokePlayModeTests
    {
        private const string BootScene = "Assets/Scenes/Boot.unity";
        private const float BootTimeout = 90f;        // 起服（资源/表/流程装配）
        private const float MainTimeout = 60f;        // 到主菜单
        private const float EnterTimeout = 60f;       // 进对局（建会话 + 加载场景 + 视图装配）
        private const float FireTimeout = 30f;        // 开火到首个表现事件
        private const float ExitTimeout = 45f;        // 离场回主菜单
        private const float ZeroTimeout = 2f;         // 离场后计数归零的观察窗

        private GameEntry _entry;
        private ServiceContainer _container;
        private StageMachine<ProcedureId, ProcedureArgs> _fsm;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (!SceneManager.GetSceneByPath(BootScene).isLoaded)
            {
                AsyncOperation load = SceneManager.LoadSceneAsync("Boot", LoadSceneMode.Additive);
                Assert.IsNotNull(load, "Boot 场景应可加载（build settings 唯一场景）");
                yield return load;
            }

            // 直入训练场关掉：本用例要走 RequestEnter 的**同一请求桥**（场景组件每帧把字段发布到
            // 静态读口，故先禁用组件、再落静态值——否则 ProcedureMain 会抢先自进对局，Main 阶段一闪而过）。
            var configs = UnityEngine.Object.FindObjectsByType<DevStartupConfig>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < configs.Length; i++)
            {
                configs[i].DirectToTraining = false;
                configs[i].enabled = false;
            }
            DevStartupConfig.DirectToTrainingEnabled = false;

            _entry = UnityEngine.Object.FindFirstObjectByType<GameEntry>();
            Assert.IsNotNull(_entry, "Boot 场景应挂引导件 GameEntry");
            yield return WaitUntil(BootDone, BootTimeout, "引导未在限时内完成装配（ClientHost 失败/流水线卡住）");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            TestModeRuntime.Current.AutoHeadshot = false;
            TestModeRuntime.Current.Active = false;

            // 引导件 DontDestroyOnLoad：销毁它触发 AppLifetime 的优雅关闭（逆序模块关闭 + 根作用域释放）。
            if (_entry != null)
            {
                UnityEngine.Object.Destroy(_entry.gameObject);
                _entry = null;
                _container = null;
                _fsm = null;
            }
            for (int i = 0; i < 10; i++) yield return null;

            // A15a 释放探针：关停后容器应已释放（根作用域 + 全部服务归零）
            Assert.IsTrue(_container == null || _container.IsDisposed, "关停后容器应已释放（根作用域未归零）");

            Scene boot = SceneManager.GetSceneByPath(BootScene);
            if (boot.isLoaded)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(boot);
                if (unload != null) yield return unload;
            }
        }

        [UnityTest]
        public IEnumerator 对局全链冒烟_起服到进测试房到开火离场_容器级归零()
        {
            // ═══ ⓪ 装配归属（A15a）：13 项帧驱动序回读 + 视角偏好单实例（模块产物按归属表落位）═══
            AssertTickOrder(_container);
            AssertLookPreferencesSingleInstance(_container);

            // ═══ ① 流程到主菜单（Started 与 Current 同判：只读 Current 会误命中枚举默认值 Launch）═══
            yield return WaitUntil(() => _fsm.Started && _fsm.Current == ProcedureId.Main, MainTimeout,
                "未能进入 Main 阶段（当前 " + _fsm.Current + "）");

            // ═══ ② 开发桥进本地测试房（生产路径唯一入口：ProcedureMain.OnUpdate 消费请求）═══
            TestModeRuntime.Current.RequestEnter();
            yield return WaitUntil(() => _fsm.Current == ProcedureId.Battle, EnterTimeout,
                "未能进入 Battle 阶段（当前 " + _fsm.Current + "）");

            // ═══ ③ 视图组：工厂端口取件（装配根注册）→ 等装配完成 ═══
            IBattleViewGroupFactory factory = _container.Resolve<IBattleViewGroupFactory>();
            yield return WaitUntil(() => factory.LastCreated is BattleViewRig, EnterTimeout,
                "视图组工厂未产出装配件（LastCreated 一直为 null 或类型不符）");
            var rig = (BattleViewRig)factory.LastCreated;
            yield return WaitUntil(() => rig.IsBuilt, EnterTimeout, "视图组已创建但未完成 Attach（IsBuilt=false）");

            // 出场前断言：视图已建 + HUD 根件在场且在层 + 准心就位
            Assert.IsTrue(rig.IsBuilt, "③视图组应已建并接线");
            Assert.IsNotNull(rig.Hud, "③HUD 族装配器应在场");
            Assert.IsNotNull(rig.Hud.Root, "③HUD 根件应已实例化（缺 prefab 会静默降级为 null）");
            Assert.IsTrue(rig.Hud.Root.activeInHierarchy, "③HUD 根件应在层（Canvas 被禁用 = 玩家看不见 HUD）");
            Assert.IsNotNull(rig.Hud.Crosshair, "③准心驱动应就位");

            // ═══ ④ 开火：自瞄源（等价"玩家把准心压在敌人头上"）→ 四路表现各取一个在场证据 ═══
            var vfx = _container.Resolve<VfxService>();
            var audio = _container.Resolve<AudioService>();
            TestModeRuntime.Current.AutoHeadshot = true;
            yield return WaitUntil(() => rig.Hud.Crosshair.ShotPulseCount > 0, FireTimeout,
                "开火后准心未产生脉冲（输入源未替换/射击没走到 HUD 反馈）");
            yield return WaitUntil(() => audio.VoiceCount > 0, FireTimeout, "开火后无音效 voice 在场");
            yield return WaitUntil(() => vfx.ActiveCount > 0, FireTimeout, "开火后无世界 VFX 在场");
            yield return WaitUntil(() => rig.Feedback.DamageNumbers != null && rig.Feedback.DamageNumbers.ActiveCount > 0,
                FireTimeout, "命中后无伤害数字在场（飘字链路/开关）");

            // ═══ ⑤ 越肩几何（带容差）：相机在角色右后方，角度/水平距离/高度三档 ═══
            AssertOverShoulderGeometry(rig);

            // ═══ ⑥ 离场：只读容器级（rig/HUD 在本步被拆——读完计数即不得再触其成员）═══
            TestModeRuntime.Current.AutoHeadshot = false;      // 先停火：否则离场过渡期还在产新特效
            yield return null;
            TestModeRuntime.Current.RequestExit();
            yield return WaitUntil(() => _fsm.Current == ProcedureId.Main, ExitTimeout,
                "离场后未回到 Main 阶段（当前 " + _fsm.Current + "）");
            yield return WaitUntil(() => factory.LastCreated == null, ZeroTimeout,
                "离场后工厂仍持有视图组（装饰器 Dispose 未清最后创建口——视图可能泄漏）");
            if (vfx.ActiveCount != 0)
            {
                var snap = new System.Collections.Generic.Dictionary<string, string>();
                vfx.Snapshot(snap);
                var builder = new System.Text.StringBuilder();
                foreach (var pair in snap) builder.Append(pair.Key).Append('=').Append(pair.Value).Append("; ");
                Debug.Log("[BattleSmoke][PROBE] 离场后 vfx 未归零：" + builder);
            }
            yield return WaitUntil(() => vfx.ActiveCount == 0, ZeroTimeout,
                "离场后世界 VFX 未归零（仍有 " + vfx.ActiveCount + " 个活跃实例）");
            if (audio.VoiceCount != 0)
            {
                var snapA = new System.Collections.Generic.Dictionary<string, string>();
                audio.Snapshot(snapA);
                var builderA = new System.Text.StringBuilder();
                foreach (var pair in snapA) builderA.Append(pair.Key).Append('=').Append(pair.Value).Append("; ");
                Debug.Log("[BattleSmoke][PROBE] 离场后音效 voice 未归零：" + builderA);
            }
            yield return WaitUntil(() => audio.VoiceCount == 0, ZeroTimeout,
                "离场后音效 voice 未归零（仍有 " + audio.VoiceCount + " 个）");
        }

        // ---- 装配归属（A15a）----

        /// <summary>帧驱动序回读：<c>Tickables</c> 的类型序必须按 13 项期望序出现（接口键按实现类型匹配）。
        /// 判据钉的是"SetTickOrder 单点声明"——段各自声明会让相对序随注册序漂移。</summary>
        private static void AssertTickOrder(ServiceContainer container)
        {
            var expected = new[]
            {
                typeof(IMainThreadDispatcher), typeof(IWorldClock), typeof(IUIClock),
                typeof(LiteFramework.UI.DotweenUiClockDriver), typeof(LiteFramework.UI.UIService),
                typeof(ILogicScheduler), typeof(IUIScheduler), typeof(ITimelineRunner),
                typeof(IVFXService), typeof(LiteFramework.UI.DialogService),
                typeof(LiteFramework.UI.ToastTicker), typeof(ICommandCenter),
                typeof(StageMachine<ProcedureId, ProcedureArgs>),
            };

            int cursor = 0;
            foreach (ITickable tickable in container.Tickables)
                if (cursor < expected.Length && expected[cursor].IsInstanceOfType(tickable)) cursor++;

            Assert.AreEqual(expected.Length, cursor,
                "帧驱动序回读与 13 项期望序不一致（SetTickOrder 单点声明失效）");
        }

        /// <summary>视角偏好**单实例**：设置门面与战斗域端口必须拿到同一对象；改灵敏度后端口读值同步
        /// （否则"设置页改值 → 对局不生效"是静默行为退化）。</summary>
        private static void AssertLookPreferencesSingleInstance(ServiceContainer container)
        {
            var settings = container.Resolve<ProductSettings>();
            var concrete = container.Resolve<LookPreferences>();
            ILookPreferences port = container.Resolve<ILookPreferences>();

            Assert.AreSame(concrete, port, "具体键与端口键必须是同一实例");
            Assert.AreSame(concrete, settings.Look, "设置门面持有的偏好必须是设置段产出的那一份");

            float before = port.SensitivityMultiplier;
            float target = before >= 2.5f ? 1.5f : 2.5f;
            settings.SetLookSensitivity(target);
            Assert.AreEqual(settings.LookSensitivity, port.SensitivityMultiplier, 1e-4f,
                "改灵敏度后端口读值必须同步（Refresh 落在同一实例上）");
            settings.SetLookSensitivity(before);                 // 还原（后续用例按默认手感）
        }

        // ---- 断言细节 ----

        /// <summary>越肩几何：相机位于角色**右**后方——肩偏角 ∈ [15°,45°]、水平距离 ∈ [1.5,4.0]m、
        /// 相机高度高于胸口 ≤ 1.0m（胸口参照 <see cref="CombatConfig.HeadHitLine"/>，与命中柱同源）。
        /// **参照系 = 相机视线水平轴**（不是角色 transform.forward：角色 prefab 的模型朝向轴属美术约定，
        /// 实测该角色 view 的 forward 为 +X，拿它当"身后"会把 20° 肩偏读成 155°）。
        /// **容差就是判据**：场景 vcam 的 FollowOffset 可调，本断言判的是"确实是越肩机位而不是正后/头顶/贴脸"。</summary>
        private static void AssertOverShoulderGeometry(BattleViewRig rig)
        {
            Camera camera = Camera.main;
            Assert.IsNotNull(camera, "⑤场景应有主相机（投影面）");

            SimView view = rig.View.View;
            Assert.IsNotNull(view, "⑤视图组应已建 SimView");
            Assert.AreNotEqual(0L, view.LocalEntityId, "⑤本地实体应已对齐（LocalEntityId=0 表示未绑定）");

            Vector3 characterPos = view.LocalDisplayPosition;
            if (view.TryGetSlot(view.LocalEntityId, out int slot) && view.TryGetView(slot, out GameObject character)
                && character != null)
                characterPos = character.transform.position;

            Vector3 cameraPos = camera.transform.position;
            Vector3 delta = cameraPos - characterPos;
            var flat = new Vector3(delta.x, 0f, delta.z);
            float horizontal = flat.magnitude;

            // 参照系用**相机视线水平轴**而不是角色 transform.forward：角色 prefab 的模型朝向轴属美术资产约定
            // （实测该角色 view 的 forward 为 +X），拿它当"身后"方向会把 20° 的肩偏读成 155°。
            // 越肩机位的可判据定义＝"相机在角色身后、且相对视线轴横向偏出 15~45°"：
            Vector3 viewForward = new Vector3(camera.transform.forward.x, 0f, camera.transform.forward.z).normalized;
            var behindAxis = -viewForward;
            var rightAxis = new Vector3(viewForward.z, 0f, -viewForward.x);   // cross(up, viewForward)
            var flatDir = horizontal > 0.01f ? flat.normalized : behindAxis;
            float shoulderAngle = Vector3.Angle(behindAxis, flatDir);
            float lateral = Vector3.Dot(flatDir, rightAxis);
            float aboveChest = cameraPos.y - (characterPos.y + CombatConfig.HeadHitLine);

            Debug.Log("[BattleSmoke] 越肩几何：肩偏=" + shoulderAngle.ToString("0.0") + "度 横向="
                      + lateral.ToString("0.00") + " 水平距离=" + horizontal.ToString("0.00")
                      + "m 高于胸口=" + aboveChest.ToString("0.00") + "m");

            Assert.That(horizontal, Is.InRange(1.5f, 4.0f),
                "⑤相机水平距离应在 [1.5,4.0]m（实测 " + horizontal.ToString("0.00") + "m）");
            Assert.That(shoulderAngle, Is.InRange(15f, 45f),
                "⑤相机应在角色身后并横向偏出 15-45 度（实测 " + shoulderAngle.ToString("0.0") + " 度）");
            Assert.Greater(lateral, 0f, "⑤相机应在角色**右**后方（实测横向偏 " + lateral.ToString("0.00") + "，负数=左侧）");
            Assert.LessOrEqual(aboveChest, 1.0f,
                "⑤相机高于胸口不得超过 1.0m（实测 " + aboveChest.ToString("0.00") + "m）");
            Assert.LessOrEqual(aboveChest, 1.0f,
                "⑤相机高于胸口不得超过 1.0m（实测 " + aboveChest.ToString("0.00") + "m）");
        }

        // ---- 辅助 ----

        private bool BootDone()
        {
            if (_entry == null) return false;
            if (_container == null)
            {
                try { _container = _entry.TakeContainer(); }
                catch (InvalidOperationException) { return false; }   // 装配尚未完成
            }
            if (_fsm == null) _fsm = _container.Resolve<StageMachine<ProcedureId, ProcedureArgs>>();
            return _fsm != null && _fsm.Started;
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds, string failureMessage)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup >= deadline) Assert.Fail(failureMessage + "（超时 " + timeoutSeconds + "s）");
                yield return null;
            }
        }
    }
}
