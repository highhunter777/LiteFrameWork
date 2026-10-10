using System.Collections.Generic;
using LiteClient;
using LiteFramework;
using LiteFramework.Animation;
using LiteSim;
using LiteView;
using LiteView.Animation;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 角色移动动画首版（《动画模块专项设计》直 Clip 主路径：片段清单供片 + Animator 后端 Manual
    /// PlayableGraph；Driver = 视图速度 → 移动语义）。真资源（prefab 无控制器、片源＝片段清单）驱动，
    /// 缺包克隆 Ignore 跳过（灰盒视图无绑定组件 → 驱动不建播放器的降级由真机冒烟覆盖）。
    /// </summary>
    public sealed class CharacterLocomotionEditModeTests : UnityTestBase
    {
        private GameObject LoadPrefabOrIgnore()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CombatGirlsAnimationProfile.ViewPrefabPath);
            if (prefab == null) Assert.Ignore("CombatGirlsCharacterPack 未入库——真角色动画用例跳过");
            return prefab;
        }

        /// <summary>测试用 ID（语义 ID 与片段名解耦，同一片段可按不同 Loop 定义登记——§5 判定归定义）。</summary>
        private static readonly AnimationId OverlayLoop = new AnimationId("Test.OverlayLoop");

        /// <summary>测试用混合 ID：Walk↔Run 双槽位（Base——普通混合器的典型用途，§4 Blend）。</summary>
        private static readonly AnimationId TestMoveBlend = new AnimationId("Test.MoveBlend");

        /// <summary>测试用混合 ID：单槽位 Reload（Overlay——验证混合路径的层权重淡入）。</summary>
        private static readonly AnimationId TestOverlayBlend = new AnimationId("Test.OverlayBlend");

        /// <summary>测试 Profile：真实片段名 + 可控 Loop（覆盖"定义与资产 loop 相反"两个方向）+ 两个混合定义。</summary>
        private static AnimationProfile TestProfile()
            => new AnimationProfile()
                .Register(new AnimationDefinition(CharacterAnimationIds.Idle, AnimationChannel.Base,
                    "Idle", loop: true, minSpeed: 0.1f, maxSpeed: 2f))
                .Register(new AnimationDefinition(CharacterAnimationIds.Walk, AnimationChannel.Base,
                    "Walk", loop: true, minSpeed: 0.1f, maxSpeed: 2f))
                .Register(new AnimationDefinition(CharacterAnimationIds.Run, AnimationChannel.Base,
                    "Run", loop: true, minSpeed: 0.1f, maxSpeed: 2f))
                .Register(new AnimationDefinition(OverlayLoop, AnimationChannel.Overlay,
                    "Reload", loop: true, minSpeed: 0.1f, maxSpeed: 2f))          // 循环定义 × 一次性资产
                .Register(new AnimationDefinition(CharacterAnimationIds.Reload, AnimationChannel.Overlay,
                    "Reload", loop: false, minSpeed: 0.1f, maxSpeed: 2f))
                .Register(new AnimationDefinition(CharacterAnimationIds.Death, AnimationChannel.Override,
                    "Die2", loop: false, minSpeed: 0.1f, maxSpeed: 2f))
                .RegisterBlend(new AnimationBlendDefinition(TestMoveBlend, AnimationChannel.Base,
                    new[] { "Walk", "Run" }, minSpeed: 0.1f, maxSpeed: 2f))
                .RegisterBlend(new AnimationBlendDefinition(TestOverlayBlend, AnimationChannel.Overlay,
                    new[] { "Reload" }, minSpeed: 0.1f, maxSpeed: 2f));

        /// <summary>经 Profile/播放器提交混合（权重槽位序 = 定义槽位序；唯一混合入口）。</summary>
        private static AnimationStartResult PlayBlend(AnimationPlayer player, AnimationId id,
            AnimationChannel channel, params float[] weights)
            => player.PlayBlend(new AnimationBlendRequest(id, channel, weights));

        /// <summary>生产形态 Profile（tbanimationprofile 表直载——与运行时同表同装载器，驱动器用例的实参）。</summary>
        private static AnimationProfile ProductionProfile()
            => AnimationProfileTableSource.Load(ConfigService.DataDir, CombatGirlsAnimationProfile.ModelFamily);

        // ---- 直 Clip 后端构造面（视图 prefab 无控制器——片源走片段清单）----

        private static AnimationClipManifest _manifest;

        /// <summary>片段清单资产（缓存；键单源在 Profile。缺包克隆时键在引用断——本文件用例均先
        /// <see cref="LoadPrefabOrIgnore"/> 挡前置，清单断引用由登记计数面观测）。</summary>
        private static AnimationClipManifest Manifest()
            => _manifest ?? (_manifest = AssetDatabase.LoadAssetAtPath<AnimationClipManifest>(
                   CombatGirlsAnimationProfile.ClipManifestPath));

        /// <summary>构造后端并登记清单（直 Clip 模型的供片形态——生产驱动同一路径：
        /// <c>AnimatorAnimationBackend.RegisterManifest</c>）。</summary>
        private static AnimatorAnimationBackend Backend(Animator animator,
            float blendSeconds = AnimatorAnimationBackend.DefaultBlendSeconds)
        {
            var backend = new AnimatorAnimationBackend(animator, blendSeconds);
            backend.RegisterManifest(Manifest());
            return backend;
        }

        private static AnimatorAnimationBackend Backend(GameObject go,
            float blendSeconds = AnimatorAnimationBackend.DefaultBlendSeconds)
            => Backend(go.GetComponentInChildren<Animator>(true), blendSeconds);

        // ---- 后端契约 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 后端_有效状态提交_循环状态不自然结束()
        {
            var prefab = LoadPrefabOrIgnore();
            var go = Scope.Track(Object.Instantiate(prefab));
            var backend = Backend(go);
            var player = new AnimationPlayer(backend, AnimationProfileTableSource.Load(ConfigService.DataDir, CombatGirlsAnimationProfile.ModelFamily));

            var ok = player.Play(new AnimationRequest(CharacterAnimationIds.Run, AnimationChannel.Base));
            Assert.IsTrue(ok.Accepted, "已登记且控制器存在的状态必须被接受");
            Assert.IsTrue(backend.IsChannelActive(AnimationChannel.Base));

            for (int i = 0; i < 30; i++) player.Tick(0.1f);                 // 3s：Run 为循环状态
            Assert.IsTrue(backend.IsChannelActive(AnimationChannel.Base), "循环移动状态不产生 Completed（§5）");

            player.Dispose();                                               // §9 销毁序：释放后端（Graph.Destroy）
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 后端_一次性状态到边界收Completed_未知绑定收Failed()
        {
            var prefab = LoadPrefabOrIgnore();

            // ① 一次性状态（包内 Die1，非循环）→ Completed 恰好一次
            var goDie = Scope.Track(Object.Instantiate(prefab));
            var dieId = new AnimationId("Locomotion.Die");
            var dieProfile = new AnimationProfile()
                .Register(new AnimationDefinition(dieId, AnimationChannel.Base, "Die2", loop: false, minSpeed: 1f, maxSpeed: 1f));
            var diePlayer = new AnimationPlayer(
                Backend(goDie), dieProfile);
            AnimationTerminalState? dieTerminal = null;
            diePlayer.OnTerminal += (h, t) => dieTerminal = t;

            Assert.IsTrue(diePlayer.Play(new AnimationRequest(dieId, AnimationChannel.Base)).Accepted);
            for (int i = 0; i < 60 && dieTerminal == null; i++) diePlayer.Tick(0.1f);   // ≤6s：死亡动画时长有界
            Assert.AreEqual(AnimationTerminalState.Completed, dieTerminal, "一次性状态到边界应收 Completed");

            dieTerminal = null;
            diePlayer.Tick(1f);
            Assert.IsNull(dieTerminal, "Completed 恰好一次——之后不再重复回报");
            diePlayer.Dispose();

            // ② 未知状态绑定 → 后端显性拒绝（不静默留在旧姿态假装成功）
            var goBad = Scope.Track(Object.Instantiate(prefab));
            var badId = new AnimationId("Locomotion.NoSuch");
            var badProfile = new AnimationProfile()
                .Register(new AnimationDefinition(badId, AnimationChannel.Base, "NoSuchState", loop: true, minSpeed: 1f, maxSpeed: 1f));
            var badBackend = Backend(goBad);
            var badPlayer = new AnimationPlayer(badBackend, badProfile);
            AnimationTerminalState? badTerminal = null;
            badPlayer.OnTerminal += (h, t) => badTerminal = t;

            Assert.IsTrue(badPlayer.Play(new AnimationRequest(badId, AnimationChannel.Base)).Accepted, "播放器接受（绑定由后端执行时裁决）");
            Assert.AreEqual(AnimationTerminalState.Failed, badTerminal, "控制器无该状态——后端执行失败必须回报（§3）");
            Assert.AreEqual(1, badBackend.UnknownBindings, "未知绑定必须计数（§13-4 诊断）");
            badPlayer.Dispose();
        }

        // ---- 分层混合（三通道；§6 通道语义 + §12 混合尾部有界）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 分层_Base与Overlay共存_叠加层只盖上半身()
        {
            var prefab = LoadPrefabOrIgnore();
            var profile = TestProfile();

            // 对照实例 A：只有基础移动
            var goA = Scope.Track(Object.Instantiate(prefab));
            var animA = goA.GetComponentInChildren<Animator>(true);
            var playerA = new AnimationPlayer(Backend(animA), profile);

            // 被测实例 B：基础移动 + 叠加层
            var goB = Scope.Track(Object.Instantiate(prefab));
            var animB = goB.GetComponentInChildren<Animator>(true);
            var backendB = Backend(animB);
            var playerB = new AnimationPlayer(backendB, profile);

            if (animB.avatar == null || !animB.avatar.isHuman)
                Assert.Ignore("Avatar 非 humanoid——叠加层 Mask 不可用（能力位拒绝路径由「能力」用例覆盖）");
            Assert.IsTrue(backendB.HasOverlayMask, "humanoid Avatar 必须构造出叠加层 LayerMask");

            Assert.IsTrue(playerA.Play(new AnimationRequest(CharacterAnimationIds.Walk, AnimationChannel.Base)).Accepted);
            Assert.IsTrue(playerB.Play(new AnimationRequest(CharacterAnimationIds.Walk, AnimationChannel.Base)).Accepted);
            Assert.IsTrue(playerB.Play(new AnimationRequest(OverlayLoop, AnimationChannel.Overlay)).Accepted,
                "Overlay 通道必须被接受（不得只占一半——§6）");

            // 两实例同片段同相位；先跑完淡入（BlendSeconds=0.12s）
            for (int i = 0; i < 20; i++) { playerA.Tick(0.05f); playerB.Tick(0.05f); }

            Assert.AreEqual(2, backendB.ActiveChannels, "Base + Overlay 应各自占用通道");
            Assert.IsTrue(backendB.TryGetChannelDebug(AnimationChannel.Overlay, out var ub) && ub.Active);
            Assert.AreEqual(1f, ub.Weight, 1e-3f, "叠加层淡入完成，权重应为 1");

            Transform legA = animA.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            Transform legB = animB.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            Transform armA = animA.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform armB = animB.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Assume.That(legA != null && legB != null && armA != null && armB != null, "humanoid 骨架应含腿/臂骨");

            // 采样窗口：腿必须来自基层（Mask 不覆盖腿），手臂必须被叠加层改写
            float legMax = 0f, armMax = 0f;
            for (int i = 0; i < 10; i++)
            {
                playerA.Tick(0.05f); playerB.Tick(0.05f);
                legMax = Mathf.Max(legMax, Quaternion.Angle(legA.localRotation, legB.localRotation));
                armMax = Mathf.Max(armMax, Quaternion.Angle(armA.localRotation, armB.localRotation));
            }

            Assert.Less(legMax, 5f, "腿部姿态必须来自基层（叠加层 LayerMask 不得覆盖腿）");
            Assert.Greater(armMax, 5f, "手臂必须被叠加层改写");

            playerA.Dispose();
            playerB.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 分层_基础层权重必须落地_关键骨骼姿态确实推进()
        {
            // 回归守卫：`AnimationLayerMixerPlayable` 的层 0 权重**默认是 0**，不显式置 1 时基础层
            // 整体不输出——通道在播、时间在走、IsChannelActive 全绿，角色姿态却停在默认姿势。
            // 相对差断言在缺陷态下两边同坏仍成立，故此处断言两件**绝对事实**：
            // 混合器真值权重、以及手骨逐帧确实在变。
            var prefab = LoadPrefabOrIgnore();
            var go = Scope.Track(Object.Instantiate(prefab));
            var animator = go.GetComponentInChildren<Animator>(true);
            if (animator.avatar == null || !animator.avatar.isHuman)
                Assert.Ignore("Avatar 非 humanoid——手骨不可取");

            var backend = Backend(animator);
            var player = new AnimationPlayer(backend, TestProfile());
            Assert.IsTrue(player.Play(new AnimationRequest(
                CharacterAnimationIds.Run, AnimationChannel.Base)).Accepted);

            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Assume.That(hand != null, "humanoid 骨架应含右手骨");
            Quaternion first = hand.localRotation;
            float maxDelta = 0f;
            for (int i = 0; i < 30; i++)
            {
                player.Tick(0.05f);
                maxDelta = Mathf.Max(maxDelta, Quaternion.Angle(first, hand.localRotation));
            }

            Assert.IsTrue(backend.TryGetChannelDebug(AnimationChannel.Base, out var loco));
            Assert.AreEqual(1f, loco.Weight, 1e-3f,
                "基础层在混合器上的实际权重必须为 1（默认 0 → 基础层不输出、姿态不落地）");
            Assert.Greater(maxDelta, 5f,
                "播放中的移动片段必须让骨骼姿态逐帧变化（动画确实送到角色身上）");

            player.Dispose();
        }

        // ---- 普通混合器（同通道多片段按权重混合；经 Profile/播放器的唯一管道）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 混合_经播放器_权重真的选中片段()
        {
            var prefab = LoadPrefabOrIgnore();

            // 参照实例 A：单片段直驱 Walk
            var goA = Scope.Track(Object.Instantiate(prefab));
            var animA = goA.GetComponentInChildren<Animator>(true);
            var playerA = new AnimationPlayer(
                Backend(animA, 0f), TestProfile());
            Assert.IsTrue(playerA.Play(new AnimationRequest(
                CharacterAnimationIds.Walk, AnimationChannel.Base)).Accepted);

            // 被测实例 B：**经 Profile/播放器**的普通混合器（Walk/Run 两槽位）
            var goB = Scope.Track(Object.Instantiate(prefab));
            var animB = goB.GetComponentInChildren<Animator>(true);
            var backendB = Backend(animB, 0f);
            Assert.IsTrue((backendB.Capabilities & AnimationBackendCapabilities.ClipBlending) != 0,
                "后端必须诚实声明 ClipBlending（§4）");
            var playerB = new AnimationPlayer(backendB, TestProfile());

            Transform legA = animA.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            Transform legB = animB.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            Assume.That(legA != null && legB != null, "humanoid 骨架应含右大腿骨");

            // ① 权重 Walk=1, Run=0（语义 ID → 槽位绑定由 Profile 解析）→ 与 A 的姿态差应很小
            var accepted = PlayBlend(playerB, TestMoveBlend, AnimationChannel.Base, 1f, 0f);
            Assert.IsTrue(accepted.Accepted, "已登记的混合必须被接受");
            Assert.AreEqual(AnimationStartResult.Reason.None, accepted.RejectReason, "接受时不应带拒绝原因");
            Assert.IsTrue(backendB.IsChannelActive(AnimationChannel.Base), "混合提交后通道激活");
            Assert.IsTrue(backendB.TryGetChannelDebug(AnimationChannel.Base, out var blendDbg));
            Assert.AreEqual(1f, blendDbg.Weight, 1e-3f, "基础层权重必须为 1（层 0 显式置 1）");
            StringAssert.StartsWith("mixer(", blendDbg.Source, "诊断源应标明这是普通混合器节点（含输入数）");

            float walkDiff = 0f;
            for (int i = 0; i < 15; i++)
            {
                playerA.Tick(0.05f); playerB.Tick(0.05f);
                walkDiff = Mathf.Max(walkDiff, Quaternion.Angle(legA.localRotation, legB.localRotation));
            }
            Assert.Less(walkDiff, 3f, "Walk=1 的混合应与单片段直驱 Walk 几乎一致（混合路径确实播到了骨架上）");

            // ② 交换权重（Walk=0, Run=1）→ 同一相位下姿态必须明显不同（权重真的在选片段）
            Assert.IsTrue(PlayBlend(playerB, TestMoveBlend, AnimationChannel.Base, 0f, 1f).Accepted,
                "交换权重后仍应被接受（同通道替换走同一套仲裁）");
            float runDiff = 0f;
            for (int i = 0; i < 15; i++)
            {
                playerA.Tick(0.05f); playerB.Tick(0.05f);
                runDiff = Mathf.Max(runDiff, Quaternion.Angle(legA.localRotation, legB.localRotation));
            }
            Assert.Greater(runDiff, 5f, "Run=1 的混合必须与 Walk 明显不同（权重要真的选中片段）");

            playerA.Dispose();
            playerB.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 混合_非法输入_整组拒绝且通道保持原播放()
        {
            var prefab = LoadPrefabOrIgnore();
            var go = Scope.Track(Object.Instantiate(prefab));
            var backend = Backend(go, 0f);
            var player = new AnimationPlayer(backend, TestProfile());

            Assert.IsTrue(player.Play(new AnimationRequest(
                CharacterAnimationIds.Walk, AnimationChannel.Base)).Accepted);
            for (int i = 0; i < 5; i++) player.Tick(0.05f);
            Assert.IsTrue(backend.TryGetChannelDebug(AnimationChannel.Base, out var before));

            // ① 解析期整组拒绝（InvalidRequest）：权重数目不符 / 负权重 / 全零 / 起点越界
            Assert.AreEqual(AnimationStartResult.Reason.InvalidRequest,
                PlayBlend(player, TestMoveBlend, AnimationChannel.Base, 1f).RejectReason, "权重数目与槽位不符");
            Assert.AreEqual(AnimationStartResult.Reason.InvalidRequest,
                PlayBlend(player, TestMoveBlend, AnimationChannel.Base, 1f, -1f).RejectReason, "负权重");
            Assert.AreEqual(AnimationStartResult.Reason.InvalidRequest,
                PlayBlend(player, TestMoveBlend, AnimationChannel.Base, 0f, 0f).RejectReason, "全零权重");
            Assert.AreEqual(AnimationStartResult.Reason.InvalidRequest,
                player.PlayBlend(new AnimationBlendRequest(TestMoveBlend, AnimationChannel.Base,
                    new[] { 1f, 1f }, startNormalized: 1.5f)).RejectReason, "起点越界");

            // ② 未登记的混合 ID → InvalidDefinition（混合不参与单片段回退链）
            Assert.AreEqual(AnimationStartResult.Reason.InvalidDefinition,
                PlayBlend(player, new AnimationId("Test.NoSuchBlend"), AnimationChannel.Base, 1f).RejectReason);

            // ③ 原子性：通道仍是原来那一次播放（没被半途改成混合——§6 不能只占一半）
            Assert.IsTrue(backend.TryGetChannelDebug(AnimationChannel.Base, out var after));
            Assert.AreEqual(before.Source, after.Source, "拒绝不得改变通道当前播放");
            Assert.IsTrue(backend.IsChannelActive(AnimationChannel.Base));

            // ④ 未知绑定（Profile 认得形态、后端不认得片段）→ 后端显性失败：Failed 终态 + 计数（§13-4）
            var badBlend = new AnimationId("Test.BadBlend");
            var badProfile = new AnimationProfile()
                .RegisterBlend(new AnimationBlendDefinition(badBlend, AnimationChannel.Base, new[] { "NoSuchClip" }));
            var goBad = Scope.Track(Object.Instantiate(prefab));
            var badBackend = Backend(goBad, 0f);
            var badPlayer = new AnimationPlayer(badBackend, badProfile);
            AnimationTerminalState? badTerminal = null;
            badPlayer.OnTerminal += (h, t) => badTerminal = t;

            Assert.IsTrue(PlayBlend(badPlayer, badBlend, AnimationChannel.Base, 1f).Accepted,
                "播放器接受（绑定由后端执行时裁决）");
            Assert.AreEqual(AnimationTerminalState.Failed, badTerminal, "未知绑定：后端执行失败必须回报（§3）");
            Assert.AreEqual(1, badBackend.UnknownBindings, "未知绑定必须计数（§13-4 诊断）");

            player.Dispose();
            badPlayer.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 混合_BlendSeconds是构造参数_0立即落位_默认值需要时间()
        {
            var prefab = LoadPrefabOrIgnore();

            // ① 构造值 0 → 瞬时落位（一次 Tick 权重就到 1）
            var goInstant = Scope.Track(Object.Instantiate(prefab));
            var animInstant = goInstant.GetComponentInChildren<Animator>(true);
            if (animInstant.avatar == null || !animInstant.avatar.isHuman)
                Assert.Ignore("Avatar 非 humanoid——叠加层 Mask 不可用（改测基础层即可，本用例跳过）");
            var backendInstant = Backend(animInstant, 0f);
            Assert.AreEqual(0f, backendInstant.BlendSeconds, "构造值必须原样可读");
            var playerInstant = new AnimationPlayer(backendInstant, TestProfile());
            Assert.IsTrue(PlayBlend(playerInstant, TestOverlayBlend, AnimationChannel.Overlay, 1f).Accepted);
            playerInstant.Tick(0.02f);
            Assert.IsTrue(backendInstant.TryGetChannelDebug(AnimationChannel.Overlay, out var instant));
            Assert.AreEqual(1f, instant.Weight, 1e-3f, "BlendSeconds=0 时上层权重应一帧内直接到 1（瞬时落位）");

            // ② 构造值 0.12（默认手感）→ 同一帧只淡入一小步（证明参数真的被用、不是常量写死）
            var goSmooth = Scope.Track(Object.Instantiate(prefab));
            var backendSmooth = Backend(goSmooth, 0.12f);
            var playerSmooth = new AnimationPlayer(backendSmooth, TestProfile());
            Assert.IsTrue(PlayBlend(playerSmooth, TestOverlayBlend, AnimationChannel.Overlay, 1f).Accepted);
            playerSmooth.Tick(0.02f);
            Assert.IsTrue(backendSmooth.TryGetChannelDebug(AnimationChannel.Overlay, out var smooth));
            Assert.Greater(smooth.Weight, 0f, "淡入应已开始");
            Assert.Less(smooth.Weight, 0.5f, "0.12s 淡入在 0.02s 内只能走一小步（不是瞬时）");

            // ③ 非法时长显性失败（不静默夹取）
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new AnimatorAnimationBackend(animInstant, blendSeconds: -1f), "负时长必须显性失败");

            playerInstant.Dispose();
            playerSmooth.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 混合_反复提交_节点数回基线()
        {
            var prefab = LoadPrefabOrIgnore();
            var go = Scope.Track(Object.Instantiate(prefab));
            // 用**默认淡化时长**（非 0）：尾部在帧间断续存在，反复提交才真的打断"在途尾部"——
            // 0.12s 的尾部才是 §12 截断计数的观察对象（0 时长瞬时落位，尾部活不过一个 Tick）。
            var backend = Backend(go);
            var player = new AnimationPlayer(backend, TestProfile());

            Assert.IsTrue(PlayBlend(player, TestMoveBlend, AnimationChannel.Base, 0.5f, 0.5f).Accepted);
            for (int i = 0; i < 10; i++) player.Tick(0.02f);
            int baseline = backend.PlayableCount;          // 不硬编码：基线由控制器内部节点数决定

            // 反复在"混合 ↔ 单片段"之间互相打断（每次都应销毁上一个节点与它的输入片段）
            var weights = new float[2];
            for (int i = 0; i < 100; i++)
            {
                if (i % 2 == 0)
                {
                    weights[0] = 1f - i * 0.01f;
                    weights[1] = i * 0.01f;
                    Assert.IsTrue(PlayBlend(player, TestMoveBlend, AnimationChannel.Base, weights).Accepted);
                }
                else
                {
                    Assert.IsTrue(player.Play(new AnimationRequest(
                        i % 4 == 1 ? CharacterAnimationIds.Idle : CharacterAnimationIds.Run,
                        AnimationChannel.Base)).Accepted);
                }
                player.Tick(0.02f);
            }

            // 收尾回到与基线**同形态**（2 槽位混合）再比节点数——否则"混合 ↔ 单片段"的节点数本就不同
            Assert.IsTrue(PlayBlend(player, TestMoveBlend, AnimationChannel.Base, 0.5f, 0.5f).Accepted);
            for (int i = 0; i < 10; i++) player.Tick(0.02f);
            Assert.AreEqual(baseline, backend.PlayableCount,
                "反复提交混合节点后节点数必须回基线（§12：混合节点与其输入片段都要被回收）");
            Assert.IsTrue(backend.IsChannelActive(AnimationChannel.Base));
            Assert.Greater(backend.TruncatedBlends, 0, "在途混合尾部被打断必须计数（§12 确定截断策略）");

            player.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 混合_永不Completed_长推进也不收终态()
        {
            var prefab = LoadPrefabOrIgnore();
            var go = Scope.Track(Object.Instantiate(prefab));
            var backend = Backend(go);   // 默认淡化时长
            var player = new AnimationPlayer(backend, TestProfile());

            int terminals = 0;
            AnimationTerminalState last = AnimationTerminalState.None;
            player.OnTerminal += (h, t) => { terminals++; last = t; };

            var info = PlayBlend(player, TestMoveBlend, AnimationChannel.Base, 0.5f, 0.5f);
            Assert.IsTrue(info.Accepted);
            for (int i = 0; i < 200; i++) player.Tick(0.05f);                  // 10s ≫ 片段时长

            Assert.AreEqual(0, terminals, "混合集合没有单一结束边界：永不 Completed（§5）");
            Assert.IsTrue(player.TryGetState(info.Handle, out var state) && state.IsPlaying, "混合应仍在播");
            Assert.IsTrue(backend.IsChannelActive(AnimationChannel.Base));

            player.Dispose();                                                  // 释放才收终态（§9）
            Assert.AreEqual(AnimationTerminalState.OwnerDisposed, last, "混合的终态只来自替换/停止/释放");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 混合_播放倍率就地缩放_真图生效()
        {
            // 步频同步：倍率乘在混合器节点上（Playable 速度沿图相乘），
            // 输入片段原生 Speed 不动——经引擎真值诊断（TryGetChannelDebug.Speed）验证，不经替身。
            var prefab = LoadPrefabOrIgnore();
            var go = Scope.Track(Object.Instantiate(prefab));
            var anim = go.GetComponentInChildren<Animator>(true);
            var backend = Backend(anim, 0f);
            var player = new AnimationPlayer(backend, TestProfile());

            var accepted = PlayBlend(player, TestMoveBlend, AnimationChannel.Base, 1f, 0f);
            Assert.IsTrue(accepted.Accepted, "混合提交应被接受");
            Assert.IsTrue(backend.TryGetChannelDebug(AnimationChannel.Base, out var d0), "混合提交后通道诊断可读");
            Assert.AreEqual(1f, d0.Speed, 1e-3f, "提交后应为原生 1×");

            Assert.IsTrue(player.TrySetBlendSpeed(accepted.Handle, 2f), "就地倍率更新应被接受");
            Assert.IsTrue(backend.TryGetChannelDebug(AnimationChannel.Base, out var d1), "就地更新后通道诊断可读");
            Assert.AreEqual(2f, d1.Speed, 1e-3f, "倍率应乘在混合器节点上（引擎 SetSpeed 真值）");

            Assert.IsFalse(player.TrySetBlendSpeed(accepted.Handle, 0f), "非正倍率必须拒绝");
            Assert.IsFalse(player.TrySetBlendSpeed(accepted.Handle, float.NaN), "NaN 倍率必须拒绝");
            Assert.IsTrue(backend.TryGetChannelDebug(AnimationChannel.Base, out var d2), "拒绝后通道诊断可读");
            Assert.AreEqual(2f, d2.Speed, 1e-3f, "拒绝后倍率不变");

            Assert.IsFalse(player.TrySetBlendSpeed(default, 2f), "未知句柄必须拒绝");
        }

        // ---- 帧事件接缝（§8）：Fire 事件即进 Fire 系，语义/通道由战斗层装配期从 Profile 单源解析 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 分层_FullBody覆盖后自动回Locomotion_时间继续推进()
        {
            var prefab = LoadPrefabOrIgnore();
            var go = Scope.Track(Object.Instantiate(prefab));
            var backend = Backend(go);
            var player = new AnimationPlayer(backend, TestProfile());

            Assert.IsTrue(player.Play(new AnimationRequest(CharacterAnimationIds.Walk, AnimationChannel.Base)).Accepted);
            for (int i = 0; i < 10; i++) player.Tick(0.05f);
            Assert.IsTrue(backend.TryGetChannelDebug(AnimationChannel.Base, out var move1));
            float t1 = move1.Time;

            AnimationTerminalState? terminal = null;
            player.OnTerminal += (h, t) => terminal = t;
            Assert.IsTrue(player.Play(new AnimationRequest(CharacterAnimationIds.Death, AnimationChannel.Override)).Accepted);
            Assert.AreEqual(2, backend.ActiveChannels, "FullBody 覆盖期应两通道并存");

            player.Tick(0.05f);
            Assert.IsTrue(backend.TryGetChannelDebug(AnimationChannel.Base, out var move2));
            Assert.AreNotEqual(t1, move2.Time, "FullBody 覆盖期间 Locomotion 时间仍在推进（无需恢复过期旧动作——§6）");

            for (int i = 0; i < 200 && terminal == null; i++) player.Tick(0.05f);
            Assert.AreEqual(AnimationTerminalState.Completed, terminal, "一次性 FullBody 到边界应收 Completed");

            for (int i = 0; i < 10; i++) player.Tick(0.05f);                  // 让权重淡出跑完
            Assert.AreEqual(1, backend.ActiveChannels, "FullBody 释放后只剩 Locomotion");
            Assert.IsTrue(backend.TryGetChannelDebug(AnimationChannel.Override, out var fb) && fb.Weight <= 1e-3f,
                "FullBody 权重必须归零（姿态自动回当前移动语义）");

            player.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 分层_反复打断200帧_节点数回基线且截断有计数()
        {
            var prefab = LoadPrefabOrIgnore();
            var go = Scope.Track(Object.Instantiate(prefab));
            var backend = Backend(go);
            var player = new AnimationPlayer(backend, TestProfile());

            // 单次播放落位后的节点基线（混合器 + AnimatorControllerPlayable 内部节点 + 当前片段）。
            // 不硬编码节点数——控制器内部节点数由 Unity 决定，本用例只关心"反复打断后是否回基线"。
            Assert.IsTrue(player.Play(new AnimationRequest(
                CharacterAnimationIds.Idle, AnimationChannel.Base)).Accepted);
            for (int i = 0; i < 20; i++) player.Tick(0.05f);
            int baseline = backend.PlayableCount;

            for (int i = 0; i < 200; i++)
            {
                player.Play(new AnimationRequest(
                    i % 2 == 0 ? CharacterAnimationIds.Idle : CharacterAnimationIds.Run, AnimationChannel.Base));
                player.Tick(0.016f);
            }
            Assert.Greater(backend.TruncatedBlends, 0, "在途混合尾部被打断必须计数（§12 确定截断策略）");

            for (int i = 0; i < 20; i++) player.Tick(0.05f);                  // 落位：尾巴销毁
            int settled = backend.PlayableCount;
            Assert.AreEqual(baseline, settled,
                "反复打断后节点数必须回单次播放基线（§12：每通道一个当前 + 至多一条尾部，不累积）");

            for (int i = 0; i < 100; i++)
            {
                player.Play(new AnimationRequest(
                    i % 2 == 0 ? CharacterAnimationIds.Run : CharacterAnimationIds.Idle, AnimationChannel.Base));
                player.Tick(0.016f);
            }
            for (int i = 0; i < 20; i++) player.Tick(0.05f);

            Assert.AreEqual(settled, backend.PlayableCount, "第二轮反复打断后节点数仍回基线（§12：不能无限累积）");
            player.Dispose();
        }

        // ---- 完成判定（§5：判定方式归定义，不读资产 loop 设置）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 完成判定_一次性_UpperBody与FullBody各恰好一次()
        {
            var prefab = LoadPrefabOrIgnore();
            AssertCompletedOnce(prefab, CharacterAnimationIds.Reload, AnimationChannel.Overlay);
            AssertCompletedOnce(prefab, CharacterAnimationIds.Death, AnimationChannel.Override);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 完成判定_定义为循环_资产不循环也永不完成且时间回绕()
        {
            var prefab = LoadPrefabOrIgnore();
            var go = Scope.Track(Object.Instantiate(prefab));
            var backend = Backend(go);
            var player = new AnimationPlayer(backend, TestProfile());

            int terminals = 0;
            player.OnTerminal += (h, t) => terminals++;
            Assert.IsTrue(player.Play(new AnimationRequest(OverlayLoop, AnimationChannel.Overlay)).Accepted);

            for (int i = 0; i < 100; i++)                                     // 5s ≫ 片段时长
            {
                player.Tick(0.05f);
                Assert.IsTrue(backend.TryGetChannelDebug(AnimationChannel.Overlay, out var d));
                Assert.LessOrEqual(d.Time, d.Length + 1e-3f, "循环定义即使在资产不循环时也必须回绕（不越界）");
            }

            Assert.AreEqual(0, terminals, "循环定义不自然 Completed（§5）");
            player.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 完成判定_定义为一次性_资产循环也按定义完成()
        {
            var prefab = LoadPrefabOrIgnore();
            var oneShot = new AnimationId("Test.IdleOnce");
            var profile = new AnimationProfile()
                .Register(new AnimationDefinition(oneShot, AnimationChannel.Base,
                    "Idle", loop: false, minSpeed: 0.1f, maxSpeed: 2f));      // 一次性定义 × 循环资产

            var go = Scope.Track(Object.Instantiate(prefab));
            var player = new AnimationPlayer(
                Backend(go), profile);

            AnimationTerminalState? terminal = null;
            player.OnTerminal += (h, t) => terminal = t;
            Assert.IsTrue(player.Play(new AnimationRequest(oneShot, AnimationChannel.Base)).Accepted);

            for (int i = 0; i < 200 && terminal == null; i++) player.Tick(0.05f);
            Assert.AreEqual(AnimationTerminalState.Completed, terminal,
                "资产循环但定义一次性：回卷即自然结束（判定归定义）");
            player.Dispose();
        }

        // ---- 能力位（§4 不静默降级）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 能力_速度与起点生效_无humanoid时UpperBody被显性拒绝()
        {
            var prefab = LoadPrefabOrIgnore();

            // ① SpeedOverride：同片段 2× 速度，时间按倍率推进（Tick 不再重复缩放——§7）
            var goFast = Scope.Track(Object.Instantiate(prefab));
            var fastBackend = Backend(goFast);
            var fastPlayer = new AnimationPlayer(fastBackend, TestProfile());
            Assert.IsTrue(fastPlayer.Play(new AnimationRequest(
                CharacterAnimationIds.Death, AnimationChannel.Override, 0f, 2f)).Accepted);
            for (int i = 0; i < 2; i++) fastPlayer.Tick(0.05f);               // 0.1s × 2 = 0.2s
            Assert.IsTrue(fastBackend.TryGetChannelDebug(AnimationChannel.Override, out var fast));
            Assert.AreEqual(2f, fast.Speed, 1e-3f);
            Assume.That(fast.Length > 0.3f, "测速窗口不越片段边界");
            Assert.AreEqual(0.2f, fast.Time, 0.03f, "速度倍率必须经 SetSpeed 生效");
            fastPlayer.Dispose();

            // ② StartAtNormalized：按归一化起点提交，采样时间落在该点
            var goStart = Scope.Track(Object.Instantiate(prefab));
            var startBackend = Backend(goStart);
            var startPlayer = new AnimationPlayer(startBackend, TestProfile());
            Assert.IsTrue(startPlayer.Play(new AnimationRequest(
                CharacterAnimationIds.Idle, AnimationChannel.Base, 0.5f)).Accepted);
            startPlayer.Tick(0f);
            Assert.IsTrue(startBackend.TryGetChannelDebug(AnimationChannel.Base, out var started));
            Assert.AreEqual(0.5f * started.Length, started.Time, 0.02f, "起点必须落在归一化位置");
            startPlayer.Dispose();

            // ③ 无 humanoid Avatar → 无叠加层 Mask：不声明 OverlayChannel（叠加层能力位），Overlay 显性拒绝——
            //    覆盖层（Override）不需 Mask，任何 rig 都声明 OverrideChannel（分层模式）
            //    （直 Clip 模型：裸 Animator 经清单供片——能力面与片源解耦，不再借控制器）
            var goNoAvatar = Scope.CreateGameObject("NoAvatarAnimator");
            var bareAnimator = goNoAvatar.AddComponent<Animator>();

            var bareBackend = new AnimatorAnimationBackend(bareAnimator);
            bareBackend.RegisterManifest(Manifest());
            Assert.IsFalse(bareBackend.HasOverlayMask, "非 humanoid 不构造叠加层 Mask");
            Assert.AreEqual(AnimationBackendCapabilities.None,
                bareBackend.Capabilities & AnimationBackendCapabilities.OverlayChannel,
                "Mask 不可用时不得声明 OverlayChannel（§4 诚实声明）");
            Assert.IsTrue(bareBackend.Capabilities.HasFlag(AnimationBackendCapabilities.OverrideChannel),
                "覆盖层不需 Mask——任何 rig 声明 OverrideChannel（分层模式）");

            var barePlayer = new AnimationPlayer(bareBackend, TestProfile());
            var rejected = barePlayer.Play(new AnimationRequest(CharacterAnimationIds.Reload, AnimationChannel.Overlay));
            Assert.IsFalse(rejected.Accepted, "无叠加层能力位时 Overlay 必须被拒绝（不静默降级）");
            Assert.AreEqual(AnimationStartResult.Reason.UnsupportedCapability, rejected.RejectReason);
            barePlayer.Dispose();
        }

        // ---- Profile 资源键校验（§11）----

        [Test]
        [Category(TestCategory.Contract)]
        public void Profile_每条绑定命中片段清单()
        {
            var prefab = LoadPrefabOrIgnore();
            Assert.IsNotNull(prefab.GetComponentInChildren<LiteAnimator>(true), "视图 prefab 应挂绑定组件（直 Clip 形态）");
            var manifest = AssetDatabase.LoadAssetAtPath<AnimationClipManifest>(CombatGirlsAnimationProfile.ClipManifestPath);
            Assume.That(manifest != null, "片段清单资产应存在");
            Assume.That(manifest.EntryCount > 0, "片段清单应有登记条目");

            var available = new HashSet<string>();
            for (int i = 0; i < manifest.EntryCount; i++)
                if (manifest.TryGetEntry(i, out var e) && e.Clip != null) available.Add(e.Key);

            var profile = AnimationProfileTableSource.Load(ConfigService.DataDir, CombatGirlsAnimationProfile.ModelFamily);
            var ids = new[]
            {
                CharacterAnimationIds.Idle, CharacterAnimationIds.Walk, CharacterAnimationIds.Run,
                CharacterAnimationIds.AimIdle, CharacterAnimationIds.Reload,
                CharacterAnimationIds.Hit, CharacterAnimationIds.Death, CharacterAnimationIds.Evade,
            };

            foreach (var id in ids)
            {
                Assert.IsTrue(profile.TryGetDefinition(id, out var def), $"Profile 应登记 {id}");
                Assert.IsTrue(available.Contains(def.Binding),
                    $"绑定「{def.Binding}」（{id}）不在片段清单——可用：{string.Join("/", available)}");
            }

            // 词汇表全行覆盖：CharacterAnimationIds 的每个常量在表中都有行（表与词汇表漂移的守门）
            foreach (var id in new[]
            {
                CharacterAnimationIds.Idle, CharacterAnimationIds.Walk, CharacterAnimationIds.Run,
                CharacterAnimationIds.AimIdle, CharacterAnimationIds.Reload,
                CharacterAnimationIds.Hit, CharacterAnimationIds.Death, CharacterAnimationIds.Evade,
                CharacterAnimationIds.MoveBlend, CharacterAnimationIds.AimMoveBlend,
            })
            {
                Assert.IsTrue(profile.TryGetDefinition(id, out _) || profile.TryGetBlendDefinition(id, out _),
                    $"语义 {id} 在 tbanimationprofile 表中无行——词汇表与表行漂移");
            }

            // 生产 Profile 的混合槽位也必须命中清单（速度轴 {Idle,Walk,Run} 与 4 向 AimWalk）
            foreach (var blendId in new[] { CharacterAnimationIds.MoveBlend, CharacterAnimationIds.AimMoveBlend })
            {
                Assert.IsTrue(profile.TryGetBlendDefinition(blendId, out var prodBlend), $"生产 Profile 应登记混合 {blendId}");
                for (int slot = 0; slot < prodBlend.SlotCount; slot++)
                {
                    Assert.IsTrue(available.Contains(prodBlend.Bindings[slot]),
                        $"混合「{blendId}」槽位 {slot} 的绑定「{prodBlend.Bindings[slot]}」不在片段清单");
                }
            }

            // 测试 Profile 的混合槽位同样必须命中清单（§11 资源键校验对混合路径一样成立）
            var blendProfile = TestProfile();
            foreach (var blendId in new[] { TestMoveBlend, TestOverlayBlend })
            {
                Assert.IsTrue(blendProfile.TryGetBlendDefinition(blendId, out var blend), $"测试 Profile 应登记混合 {blendId}");
                for (int slot = 0; slot < blend.SlotCount; slot++)
                {
                    Assert.IsTrue(available.Contains(blend.Bindings[slot]),
                        $"混合「{blendId}」槽位 {slot} 的绑定「{blend.Bindings[slot]}」不在片段清单");
                }
            }
        }

        private void AssertCompletedOnce(GameObject prefab, AnimationId id, AnimationChannel channel)
        {
            var go = Scope.Track(Object.Instantiate(prefab));
            var player = new AnimationPlayer(
                Backend(go), TestProfile());

            AnimationTerminalState? terminal = null;
            int count = 0;
            player.OnTerminal += (h, t) => { count++; terminal = t; };

            Assert.IsTrue(player.Play(new AnimationRequest(id, channel)).Accepted);
            for (int i = 0; i < 200 && terminal == null; i++) player.Tick(0.05f);
            Assert.AreEqual(AnimationTerminalState.Completed, terminal, $"{id}（{channel}）到边界应收 Completed");

            int atFirst = count;
            for (int i = 0; i < 20; i++) player.Tick(0.05f);
            Assert.AreEqual(atFirst, count, "Completed 恰好一次——之后不再重复回报");
            player.Dispose();
        }

        // ---- 驱动（速度/方向 → 形态与权重；§4"权重由 Driver 给"）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 驱动_速度轴走混合器_权重随速度推进_回收收口()
        {
            var prefab = LoadPrefabOrIgnore();
            var world = new SimWorldState { RngState = 1UL };
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);

            SimView view = new SimView(world, null,
                factory: (loc, parent) =>
                {
                    var go = Object.Instantiate(prefab, parent);
                    go.name = loc;
                    Scope.Track(go);
                    return go;
                },
                recycler: null);                                            // 自有池：回收 = 停用入池
            var driver = new CharacterLocomotionDriver(view, ProductionProfile(), manifest: Manifest());
            const float dt = 1f / 60f;
            var weights = new float[4];

            view.Tick(dt);
            driver.Tick(dt);
            Assert.AreEqual(1, driver.AnimatedViews, "真角色视图（带 Animator）应建播放器");

            // 静止：形态 = MoveBlend（**不是**单片段 Idle——速度轴改走混合器后形态恒定），Idle 槽位权重 1
            Assert.IsTrue(driver.TryGetCurrent(0, out var idleForm) && idleForm.Equals(CharacterAnimationIds.MoveBlend),
                "非瞄准移动一律走 MoveBlend（同形态换权重，不切片段）");
            Assert.IsTrue(driver.TryGetMotion(0, out _, weights), "混合形态应给出权重");
            Assert.AreEqual(1f, weights[0], 1e-3f, "静止：Idle 权重为 1");

            // 走速档（≈1.5 m/s）→ 落在 Idle→Walk 斜坡中点（连续混合，不是离散切档）
            view.TryGetView(0, out var go2);
            for (int i = 0; i < 5; i++) { go2.transform.position += new Vector3(0.025f, 0f, 0f); driver.Tick(dt); }
            Assert.IsTrue(driver.TryGetMotion(0, out var walkForm, weights));
            Assert.AreEqual(CharacterAnimationIds.MoveBlend, walkForm, "速度变化不换形态");
            Assert.AreEqual(0.5f, weights[0], 0.05f, "1.5 m/s 落在 Idle→Walk 斜坡中点（连续权重）");
            Assert.AreEqual(0.5f, weights[1], 0.05f);
            Assert.AreEqual(0f, weights[2], 1e-3f, "未到 Run 斜坡：Run 权重为 0");

            // 全速（≈5 m/s = CombatConfig.MoveSpeed）→ Run 槽位权重 1
            for (int i = 0; i < 5; i++) { go2.transform.position += new Vector3(0.084f, 0f, 0f); driver.Tick(dt); }
            Assert.IsTrue(driver.TryGetMotion(0, out _, weights));
            Assert.AreEqual(1f, weights[2], 1e-3f, "≥ RunFullMps：Run 权重为 1");

            // 静止 → Idle 槽位权重回 1（同一形态内连续回退）
            for (int i = 0; i < 3; i++) driver.Tick(dt);
            Assert.IsTrue(driver.TryGetMotion(0, out _, weights));
            Assert.AreEqual(1f, weights[0], 1e-3f, "静止回 Idle 权重");

            // 视图回收（死亡）→ 播放器随视图消失收口（Graph 不泄漏）
            world.Despawn(world.Entities[0].Id);
            view.Tick(dt);
            driver.Tick(dt);
            Assert.AreEqual(0, driver.AnimatedViews, "视图回收后播放器必须收口");

            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 驱动_瞄准态_静止举枪_移动走四向strafe_迟滞防抖()
        {
            var prefab = LoadPrefabOrIgnore();
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);

            SimView view = new SimView(world, null,
                factory: (loc, parent) =>
                {
                    var go = Object.Instantiate(prefab, parent);
                    go.name = loc;
                    Scope.Track(go);
                    return go;
                },
                recycler: null);
            view.AlignLocal(selfId);                                    // 本地实体：IsAiming 走预测态分支
            var driver = new CharacterLocomotionDriver(view, ProductionProfile(), manifest: Manifest());
            const float dt = 1f / 60f;
            var weights = new float[4];

            // 瞄准输入进 Sim（与真实链路同源：InputSystem 写 EntityFlags.Aiming），此后按住不放
            InputSystem.Run(world, new[]
            {
                new SimInputFrame { EntityId = selfId, AimPointX = 10f, AimPointY = 1f, AimPointZ = 0f, Buttons = SimInputFrame.ButtonAim },
            }, CombatValues.Default);

            view.Tick(dt);
            driver.Tick(dt);                                           // 首帧：建播放器（速度未知）
            driver.Tick(dt);                                           // 第二帧才有速度读数
            Assert.IsTrue(driver.TryGetCurrent(0, out var aimIdle) && aimIdle.Equals(CharacterAnimationIds.AimIdle),
                "瞄准 + 静止 → AimIdle（举枪站姿）");
            Assert.IsFalse(driver.TryGetMotion(0, out _, weights), "单片段形态没有权重（id 已给出）");

            // ① 低速 0.36 m/s（介于退出阈 0.3 与进入阈 0.6 之间）→ 迟滞保持 AimIdle，不来回切
            view.TryGetView(0, out var go);
            for (int i = 0; i < 3; i++) { go.transform.position += new Vector3(0.006f, 0f, 0f); driver.Tick(dt); }
            Assert.IsTrue(driver.TryGetCurrent(0, out var slow) && slow.Equals(CharacterAnimationIds.AimIdle),
                "低于进入阈不切形态（迟滞）");

            // ② 0.72 m/s ≥ 进入阈，且移动方向 = 朝向（+X）→ AimMoveBlend 的 F 槽位权重 1
            for (int i = 0; i < 3; i++) { go.transform.position += new Vector3(0.012f, 0f, 0f); driver.Tick(dt); }
            Assert.IsTrue(driver.TryGetMotion(0, out var strafe, weights), "瞄准移动 → AimMoveBlend 提供权重");
            Assert.AreEqual(CharacterAnimationIds.AimMoveBlend, strafe);
            Assert.AreEqual(1f, weights[0], 0.05f, "朝移动方向：AimWalk_F 权重为 1");

            // ③ 斜向移动（+X+Z 的 45°，朝向仍 +X）→ F 与相邻一侧槽位各半（相邻两片插值，不是硬切）
            //    注：正侧向（±90°）恰好落在槽位边界上（该侧片权重 = 1），要测插值必须取槽位之间
            float d = 0.012f / Mathf.Sqrt(2f);
            for (int i = 0; i < 3; i++) { go.transform.position += new Vector3(d, 0f, d); driver.Tick(dt); }
            Assert.IsTrue(driver.TryGetMotion(0, out _, weights));
            Assert.AreEqual(0.5f, weights[0], 0.05f, "45° 斜向：F 与相邻侧向片段各半（连续插值）");
            Assert.AreEqual(0.5f, Mathf.Max(weights[1], weights[3]), 0.05f, "相邻侧向槽位（R 或 L）承接另一半");
            Assert.AreEqual(0f, weights[2], 1e-3f, "背向槽位不参与");

            // ③b 正侧向（+Z，朝向 +X）→ 恰好落在槽位边界：侧向片段权重 = 1、F = 0
            for (int i = 0; i < 3; i++) { go.transform.position += new Vector3(0f, 0f, 0.012f); driver.Tick(dt); }
            Assert.IsTrue(driver.TryGetMotion(0, out _, weights));
            Assert.AreEqual(0f, weights[0], 1e-3f, "正侧向：F 权重为 0（边界点落在相邻槽位上）");
            Assert.AreEqual(1f, Mathf.Max(weights[1], weights[3]), 0.05f, "侧向片段权重为 1");

            // ④ 降到退出阈以下（0.18 m/s）→ 迟滞回 AimIdle（退出阈低于进入阈 = 不对称阈值）
            for (int i = 0; i < 3; i++) { go.transform.position += new Vector3(0.003f, 0f, 0f); driver.Tick(dt); }
            Assert.IsTrue(driver.TryGetCurrent(0, out var back) && back.Equals(CharacterAnimationIds.AimIdle),
                "低于退出阈才回 AimIdle");

            driver.Dispose();
        }

        // ---- 帧事件 → 开火动画（§8 接缝 → 单机双根：全身接管 + 窗内保持 clip，v0.5/v0.6 口径）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 驱动_帧事件开火_全身接管持枪站姿_连发零重提交_窗尽退根()
        {
            var prefab = LoadPrefabOrIgnore();
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);

            SimView view = new SimView(world, null,
                factory: (loc, parent) =>
                {
                    var go = Object.Instantiate(prefab, parent);
                    go.name = loc;
                    Scope.Track(go);
                    return go;
                },
                recycler: null);
            view.AlignLocal(selfId);
            var driver = new CharacterLocomotionDriver(view, ProductionProfile(), manifest: Manifest());       // 构造即自订阅 EventSink（§8 接缝）
            const float dt = 1f / 60f;
            var stats = new Dictionary<string, string>();
            var firePos = new SimVector3(0f, 0f, 0f);

            view.Tick(dt);
            driver.Tick(dt);                                        // 建播放器/机器（初建 = MoveBlend）
            view.Tick(dt);
            driver.Tick(dt);
            Assert.AreEqual(1, driver.AnimatedViews);

            // 站定单发：跨根进 FireIdle——战斗根覆盖移动根（收 Base、FireIdle 持 AimIdle；
            // 事务序先停后播）。
            DeliverFire(view, world, selfId, firePos);
            Assert.AreEqual(0, view.SilencedEvents, "非静默段的事件必须放行");
            Assert.AreEqual(1, view.DeliveredEvents);
            driver.Tick(dt);
            Assert.IsTrue(driver.TryGetAnimState(0, out var st1) && st1 == CharacterAnimId.FireIdle,
                "站定开火按锁存路由进 FireIdle（门控即路由——v0.5）");
            Assert.IsTrue(driver.TryGetCurrent(0, out var fireForm) && fireForm.Equals(CharacterAnimationIds.AimIdle),
                "开火窗持枪站姿 = AimIdle 循环（不播专用射击片段——反馈归枪口特效）");
            Assert.IsTrue(driver.TryGetFormHandle(0, out var fireHandle), "站姿句柄可读");

            // 覆盖直证：Base 被收（淡出后节点归零）→ 通道数回落 1——**仍处于射击窗内**（1s）。
            int settled = 0;
            for (; settled < 30; settled++)
            {
                driver.Tick(dt);
                ((IModuleStats)driver).Snapshot(stats);
                if (stats["channels"] == "1") break;
            }
            Assert.Less(settled, 30, "全身接管：Base 收口淡出后通道数必须回落 1（窗口 1s 内）");
            Assert.AreEqual("0", stats["truncatedBlends"], "首次提交不产生截断");

            // 连发（10 发/秒量级）：事件只刷窗、不重提——**全程同句柄续播**（零重提交、零终态）
            for (int i = 0; i < 120; i++) { DeliverFire(view, world, selfId, firePos); driver.Tick(dt); }
            Assert.IsTrue(driver.TryGetFormHandle(0, out var holdHandle) && holdHandle.Equals(fireHandle),
                "连发零重提交（同句柄续播——重提会按第 0 帧 + 刷 Interrupted 终态）");
            ((IModuleStats)driver).Snapshot(stats);
            Assert.AreEqual("0", stats["truncatedBlends"], "没有重提就没有截断");
            Assert.IsTrue(driver.TryGetAnimState(0, out var st2) && st2 == CharacterAnimId.FireIdle,
                "持续射击窗不断（事件刷新制）——战斗根不退");

            // 停火：窗自最后一次开火起衰减（1s）→ 窗尽退根回移动层（退根路①：窗尽 ∧ !IsAiming）
            int released = 0;
            for (; released < 200; released++)
            {
                driver.Tick(dt);
                if (driver.TryGetAnimState(0, out var st) && st == CharacterAnimId.Idle) break;
            }
            Assert.Less(released, 200, "停火后窗尽（≈1s）必须退根回移动层");
            Assert.IsTrue(driver.TryGetCurrent(0, out var back) && back.Equals(CharacterAnimationIds.MoveBlend),
                "退根后回移动根形态（MoveBlend 重提交）");
            driver.Dispose();
        }

        // ---- 单机双根（v0.5/v0.6）：窗内保持 clip / 路由 / 退根不变量 / 同形态续播 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 驱动_射击窗1s_单发持AimIdle整窗_窗尽退回移动层()
        {
            var prefab = LoadPrefabOrIgnore();
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);

            SimView view = new SimView(world, null,
                factory: (loc, parent) =>
                {
                    var go = Object.Instantiate(prefab, parent);
                    go.name = loc;
                    Scope.Track(go);
                    return go;
                },
                recycler: null);
            view.AlignLocal(selfId);
            var driver = new CharacterLocomotionDriver(view, ProductionProfile(), manifest: Manifest());
            const float dt = 1f / 60f;

            view.Tick(dt);
            driver.Tick(dt);
            view.Tick(dt);
            driver.Tick(dt);                                   // 建播放器/机器（初建 = MoveBlend）
            Assert.IsTrue(driver.TryGetCurrent(0, out var before) && before.Equals(CharacterAnimationIds.MoveBlend),
                "开火前：非瞄准 + 静止 = 移动轴混合");

            // 单发（非瞄准、静止）→ FireIdle：进态即持 AimIdle 循环（不播专用射击片段）
            DeliverFire(view, world, selfId, new SimVector3(0f, 0f, 0f));
            driver.Tick(dt);
            Assert.IsTrue(driver.TryGetAnimState(0, out var s1) && s1 == CharacterAnimId.FireIdle);
            Assert.IsTrue(driver.TryGetCurrent(0, out var inClip) && inClip.Equals(CharacterAnimationIds.AimIdle),
                "进态即持枪站姿循环（Override 当前形态 = AimIdle）");

            // 窗内保持：0.8s 后仍在 FireIdle 持 AimIdle（不迁移、不退根）
            float elapsed = 0f;
            while (elapsed < 0.8f) { driver.Tick(dt); elapsed += dt; }
            Assert.IsTrue(driver.TryGetCurrent(0, out var held) && held.Equals(CharacterAnimationIds.AimIdle),
                "窗内持枪站姿循环（窗内保持 clip）");
            Assert.IsTrue(driver.TryGetAnimState(0, out var s2) && s2 == CharacterAnimId.FireIdle,
                "窗内不迁移（持枪站姿仍是 FireIdle 态）");

            // 窗尽（1s 独立常量）→ 退根回移动层：退根点是窗长 1s
            while (elapsed < 2.5f)
            {
                driver.Tick(dt);
                elapsed += dt;
                if (driver.TryGetAnimState(0, out var s3) && s3 == CharacterAnimId.Idle) break;
            }
            Assert.Greater(elapsed, 0.8f, "窗尽前不得退根（窗 1s）");
            Assert.Less(elapsed, 1.6f, "退根应 ≈ 窗长 1s（CombatConfig.FireStanceFrames 单源）");
            Assert.IsTrue(driver.TryGetCurrent(0, out var after) && after.Equals(CharacterAnimationIds.MoveBlend),
                "退根后回非瞄准移动形态");

            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 驱动_开火事件按锁存路由_站定FireIdle播片段_移动FireWalk只开窗()
        {
            var prefab = LoadPrefabOrIgnore();
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);

            SimView view = new SimView(world, null,
                factory: (loc, parent) =>
                {
                    var go = Object.Instantiate(prefab, parent);
                    go.name = loc;
                    Scope.Track(go);
                    return go;
                },
                recycler: null);
            view.AlignLocal(selfId);
            var driver = new CharacterLocomotionDriver(view, ProductionProfile(), manifest: Manifest());
            const float dt = 1f / 60f;
            var firePos = new SimVector3(0f, 0f, 0f);

            view.Tick(dt);
            driver.Tick(dt);
            driver.Tick(dt);
            view.TryGetView(0, out var go);

            // ① 站定开火 → FireIdle：持枪站姿（AimIdle 循环）
            DeliverFire(view, world, selfId, firePos);
            driver.Tick(dt);
            Assert.IsTrue(driver.TryGetAnimState(0, out var s1) && s1 == CharacterAnimId.FireIdle);
            Assert.IsTrue(driver.TryGetCurrent(0, out var stance) && stance.Equals(CharacterAnimationIds.AimIdle),
                "站定开火持枪站姿（AimIdle 循环）");

            // ② 起跑（≥ 进入阈 0.6 m/s）后开火 → FireWalk：**不播站姿片段**，
            //    只开窗 + AimMoveBlend 四向（站姿循环不可盖步态——门控即路由）
            float elapsed = 0f;
            while (elapsed < 1.2f) { driver.Tick(dt); elapsed += dt; }   // 首发的窗先走完 → 回移动层
            Assert.IsTrue(driver.TryGetAnimState(0, out var s2) && s2 == CharacterAnimId.Idle, "首发窗尽回移动层");

            for (int i = 0; i < 3; i++) { go.transform.position += new Vector3(0.012f, 0f, 0f); driver.Tick(dt); }   // 0.72 m/s → 锁存置位
            Assert.IsTrue(driver.TryGetAnimState(0, out var s3) && s3 == CharacterAnimId.Moving, "移动根 Moving");

            DeliverFire(view, world, selfId, firePos);
            driver.Tick(dt);
            Assert.IsTrue(driver.TryGetAnimState(0, out var s4) && s4 == CharacterAnimId.FireWalk,
                "移动开火按锁存路由进 FireWalk");
            Assert.IsTrue(driver.TryGetCurrent(0, out var form) && form.Equals(CharacterAnimationIds.AimMoveBlend),
                "FireWalk 的保持 clip = AimMoveBlend 四向（firewalk 也一样）");

            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 驱动_战斗层不可被移动层打断_窗内起跑停步只切Fire族_窗尽才退根()
        {
            var prefab = LoadPrefabOrIgnore();
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);

            SimView view = new SimView(world, null,
                factory: (loc, parent) =>
                {
                    var go = Object.Instantiate(prefab, parent);
                    go.name = loc;
                    Scope.Track(go);
                    return go;
                },
                recycler: null);
            view.AlignLocal(selfId);
            var driver = new CharacterLocomotionDriver(view, ProductionProfile(), manifest: Manifest());
            const float dt = 1f / 60f;
            var firePos = new SimVector3(0f, 0f, 0f);

            view.Tick(dt);
            driver.Tick(dt);
            driver.Tick(dt);
            view.TryGetView(0, out var go);

            // 站定开火 → FireIdle
            DeliverFire(view, world, selfId, firePos);
            driver.Tick(dt);
            Assert.IsTrue(driver.TryGetAnimState(0, out var s1) && s1 == CharacterAnimId.FireIdle);

            // 起跑（锁存置位）→ FireWalk：停站姿循环、**窗不清**（限速语境保持）——不退战斗根
            for (int i = 0; i < 3; i++) { go.transform.position += new Vector3(0.012f, 0f, 0f); driver.Tick(dt); }
            Assert.IsTrue(driver.TryGetAnimState(0, out var s2) && s2 == CharacterAnimId.FireWalk,
                "起跑迁 FireWalk（窗保持——移动事实不触发退根）");
            Assert.IsTrue(driver.TryGetCurrent(0, out var walkForm) && walkForm.Equals(CharacterAnimationIds.AimMoveBlend),
                "持枪站姿让位 AimMoveBlend（同通道提交替换）");

            // 停步（锁存清零）→ 回 FireIdle：同持枪站姿（无新事件不重播——态内幂等）
            for (int i = 0; i < 3; i++) driver.Tick(dt);
            Assert.IsTrue(driver.TryGetAnimState(0, out var s3) && s3 == CharacterAnimId.FireIdle,
                "停步回 FireIdle（持枪站姿）——仍在战斗根");

            // 窗内全程：状态机只在本根内切（FireIdle↔FireWalk），移动层无权打断；
            // 窗尽（≈1s，无瞄准）→ 退根路①：回移动根叶（静止 → Idle）
            float elapsed = 0f;
            while (elapsed < 2.5f)
            {
                driver.Tick(dt);
                elapsed += dt;
                if (driver.TryGetAnimState(0, out var s4)
                    && (s4 == CharacterAnimId.Idle || s4 == CharacterAnimId.Moving)) break;
                Assert.IsTrue(s4 == CharacterAnimId.FireIdle || s4 == CharacterAnimId.FireWalk,
                    "窗内不得被移动层打断（移动事实只切层内轴）");
            }
            Assert.Less(elapsed, 1.6f, "窗尽必须退根（≈1s）");
            Assert.IsTrue(driver.TryGetAnimState(0, out var s5) && s5 == CharacterAnimId.Idle,
                "静止退根选 Idle 叶（按锁存选叶）");

            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 驱动_退根两条路_瞄准保持期窗充值不退_松ADS窗尽回移动根()
        {
            var prefab = LoadPrefabOrIgnore();
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);

            SimView view = new SimView(world, null,
                factory: (loc, parent) =>
                {
                    var go = Object.Instantiate(prefab, parent);
                    go.name = loc;
                    Scope.Track(go);
                    return go;
                },
                recycler: null);
            view.AlignLocal(selfId);                                    // 本地实体：IsAiming 走预测态分支
            var driver = new CharacterLocomotionDriver(view, ProductionProfile(), manifest: Manifest());
            const float dt = 1f / 60f;
            var firePos = new SimVector3(0f, 0f, 0f);

            // 瞄准输入进 Sim（与真实链路同源：InputSystem 写 EntityFlags.Aiming），此后按住不放
            InputSystem.Run(world, new[]
            {
                new SimInputFrame { EntityId = selfId, AimPointX = 10f, AimPointY = 1f, AimPointZ = 0f, Buttons = SimInputFrame.ButtonAim },
            }, CombatValues.Default);

            view.Tick(dt);
            driver.Tick(dt);
            driver.Tick(dt);
            Assert.IsTrue(driver.TryGetAnimState(0, out var s0) && s0 == CharacterAnimId.AimIdle,
                "瞄准建立 → 进战斗根 AimIdle（覆盖开始）");

            // 开火 → FireIdle：与 AimIdle 态同一形态（同句柄续播——零闪动、零重提交）
            DeliverFire(view, world, selfId, firePos);
            driver.Tick(dt);
            Assert.IsTrue(driver.TryGetAnimState(0, out var s1) && s1 == CharacterAnimId.FireIdle);
            Assert.IsTrue(driver.TryGetCurrent(0, out var held) && held.Equals(CharacterAnimationIds.AimIdle),
                "开火窗持枪站姿循环（与 AimIdle 态同一 clip）");
            Assert.IsTrue(driver.TryGetFormHandle(0, out var holdHandle), "持枪站姿句柄可读");
            float elapsed = 0f;
            while (elapsed < 0.8f) { driver.Tick(dt); elapsed += dt; }

            // `IsAiming` 在场即充值窗（CombatRootStage.OnUpdate）——瞄准保持期内
            // 窗被持续充值、永不尽：**不降级、不退根**，FireIdle 持续持有站姿循环（同句柄、零重提交）
            while (elapsed < 2.0f) { driver.Tick(dt); elapsed += dt; }
            Assert.IsTrue(driver.TryGetAnimState(0, out var s2) && s2 == CharacterAnimId.FireIdle,
                "瞄准保持期窗充值——保持 FireIdle（窗不尽、不降级、不退根）");
            Assert.IsTrue(driver.TryGetCurrent(0, out var held2) && held2.Equals(CharacterAnimationIds.AimIdle),
                "窗充值期仍持枪站姿循环");
            Assert.IsTrue(driver.TryGetFormHandle(0, out var holdHandle2) && holdHandle2.Equals(holdHandle),
                "窗充值期同句柄续播——零重提交（重提交会按第 0 帧 + 刷终态）");

            // 松 ADS（InputSystem 覆写标志位）→ !IsAiming → 窗不再充值、开始递减；窗尾（≈1s）内
            // 保持战斗根形态（窗内不回移动层——统一退根路）→ 窗尽才退根回移动根叶
            InputSystem.Run(world, new[] { new SimInputFrame { EntityId = selfId } }, CombatValues.Default);
            float releaseElapsed = 0f;
            for (; releaseElapsed < 2.5f; )
            {
                driver.Tick(dt);
                releaseElapsed += dt;
                if (driver.TryGetAnimState(0, out var s4)
                    && (s4 == CharacterAnimId.Idle || s4 == CharacterAnimId.Moving)) break;
                Assert.IsTrue(s4 == CharacterAnimId.FireIdle || s4 == CharacterAnimId.FireWalk
                    || s4 == CharacterAnimId.AimIdle || s4 == CharacterAnimId.AimWalk,
                    "松 ADS 后窗尾内必须保持战斗根形态（窗内不回移动层——统一退根路）");
            }
            Assert.Less(releaseElapsed, 2.5f, "窗尾（≈1s）内必须退根");
            Assert.IsTrue(driver.TryGetCurrent(0, out var back) && back.Equals(CharacterAnimationIds.MoveBlend),
                "退根后回移动根形态");

            driver.Dispose();
        }

        // ---- 换弹动画（Sim 私有面事实 → Overlay 上半身叠加：腿照常出步 / 不迁根 / 事实清除释放）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 驱动_换弹事实_上半身叠加层接管_腿继续走Base_事实清除释放()
        {
            var prefab = LoadPrefabOrIgnore();
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out int slot);

            SimView view = new SimView(world, null,
                factory: (loc, parent) =>
                {
                    var go = Object.Instantiate(prefab, parent);
                    go.name = loc;
                    Scope.Track(go);
                    return go;
                },
                recycler: null);
            view.AlignLocal(selfId);
            var driver = new CharacterLocomotionDriver(view, ProductionProfile(), manifest: Manifest());
            const float dt = 1f / 60f;
            var stats = new Dictionary<string, string>();

            view.Tick(dt);
            driver.Tick(dt);
            view.Tick(dt);
            driver.Tick(dt);
            Assert.AreEqual(1, driver.AnimatedViews);

            // Sim 侧换弹真路径（懒装备 → 打掉一发 → Reload 边沿）：事实经 SimView.IsReloading 可读
            int reloadEnd = StartReload(world, selfId, slot);
            Assert.IsTrue(view.IsReloading(slot), "换弹事实可读（Sim 运行态——武器私有面投影）");

            driver.Tick(dt);
            Assert.IsTrue(driver.TryGetReloadUpper(0), "换弹事实 → Overlay 上半身叠加层在场");
            Assert.IsTrue(driver.TryGetReloadHandle(0, out var reloadHandle), "换弹叠加层句柄可读");
            Assert.IsTrue(driver.TryGetCurrent(0, out var form) && form.Equals(CharacterAnimationIds.MoveBlend),
                "换弹**不发起根迁移**：仍是移动根 MoveBlend（Base 持续出步——停即滑步）");
            ((IModuleStats)driver).Snapshot(stats);
            Assert.AreEqual("2", stats["channels"], "Base(MoveBlend) + Overlay(Reload) 两通道并存");

            // 换弹期持续移动：腿照常跟位移（Base 权重就地维护），叠加层不重提交
            view.TryGetView(0, out var go);
            for (int i = 0; i < 3; i++) { go.transform.position += new Vector3(0.012f, 0f, 0f); driver.Tick(dt); }
            Assert.IsTrue(driver.TryGetReloadHandle(0, out var h2) && h2.Equals(reloadHandle),
                "同一事实期不重提交（重发会按第 0 帧重播）");
            Assert.IsTrue(driver.TryGetAnimState(0, out var s2) && s2 == CharacterAnimId.Moving,
                "移动锁存成立（Base 侧照常走移动叶——脚不停）");

            // 片段播完（时长对齐 Sim ReloadFrames——倍率见装配）∧ 事实仍在 → 持末帧恒驻叠加层
            for (float e = 0f; e < 2.6f; e += dt) driver.Tick(dt);
            Assert.IsTrue(driver.TryGetReloadHandle(0, out var h3) && h3.Equals(reloadHandle),
                "播完不重发（同句柄 = 帧锁定，非重播）");
            ((IModuleStats)driver).Snapshot(stats);
            Assert.AreEqual("2", stats["channels"], "持末帧叠加层保持活跃（通道不停机）");

            // 停步（锁存清零）→ 事实清除（到帧完成）→ 叠加层释放，退回纯移动层
            for (int i = 0; i < 3; i++) driver.Tick(dt);
            world.Frame = reloadEnd;
            WeaponSystem.Run(world, new[] { new SimInputFrame { EntityId = selfId } }, WeaponTable.Default);
            Assert.IsFalse(view.IsReloading(slot), "到帧完成——事实清除");
            driver.Tick(dt);
            Assert.IsFalse(driver.TryGetReloadUpper(0), "事实清除 → 叠加层释放（上半身回到 Base 姿态）");
            Assert.IsTrue(driver.TryGetAnimState(0, out var s4) && s4 == CharacterAnimId.Idle,
                "退根态不变（移动根 Idle 叶）");
            ((IModuleStats)driver).Snapshot(stats);
            Assert.AreEqual("1", stats["channels"], "释放后只剩 Base 通道（持帧占位的 Stop = 通道释放——不悬挂不冻结）");

            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 驱动_开火瞄准期换弹_战斗根形态不变_叠加层独立叠加()
        {
            var prefab = LoadPrefabOrIgnore();
            var world = new SimWorldState { RngState = 1UL };
            long selfId = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out int slot);

            SimView view = new SimView(world, null,
                factory: (loc, parent) =>
                {
                    var go = Object.Instantiate(prefab, parent);
                    go.name = loc;
                    Scope.Track(go);
                    return go;
                },
                recycler: null);
            view.AlignLocal(selfId);
            var driver = new CharacterLocomotionDriver(view, ProductionProfile(), manifest: Manifest());
            const float dt = 1f / 60f;
            var firePos = new SimVector3(0f, 0f, 0f);

            view.Tick(dt);
            driver.Tick(dt);
            driver.Tick(dt);

            // 站定开火（窗 1s）→ 换弹事实进场：战斗根形态**不受影响**，换弹走上半身叠加层
            DeliverFire(view, world, selfId, firePos);
            driver.Tick(dt);
            Assert.IsTrue(driver.TryGetAnimState(0, out var s1) && s1 == CharacterAnimId.FireIdle);

            StartReload(world, selfId, slot);
            driver.Tick(dt);
            Assert.IsTrue(driver.TryGetReloadUpper(0), "换弹 → 叠加层在场（上半身压住持枪姿态）");
            Assert.IsTrue(driver.TryGetAnimState(0, out var s2) && s2 == CharacterAnimId.FireIdle,
                "换弹不再抢占 Override 形态（不进战斗根迁叶——窗内持枪循环继续）");

            world.Frame = world.Weapons[slot * SimConfig.WeaponSlotsPerEntity + world.Entities[slot].SelectedWeapon].ReloadEndFrame;
            WeaponSystem.Run(world, new[] { new SimInputFrame { EntityId = selfId } }, WeaponTable.Default);
            driver.Tick(dt);
            Assert.IsFalse(driver.TryGetReloadUpper(0), "事实清除 → 叠加层释放");
            Assert.IsTrue(driver.TryGetAnimState(0, out var s3) && s3 == CharacterAnimId.FireIdle,
                "窗在 → 仍回 Fire 叶持枪站姿（换弹全程未离开该叶）");

            // 瞄准建立后换弹：形态同样不动，事实清除后仍是持枪站姿
            InputSystem.Run(world, new[]
            {
                new SimInputFrame { EntityId = selfId, AimPointX = 10f, AimPointY = 1f, AimPointZ = 0f, Buttons = SimInputFrame.ButtonAim },
            }, CombatValues.Default);
            driver.Tick(dt);
            driver.Tick(dt);
            StartReload(world, selfId, slot);
            driver.Tick(dt);
            Assert.IsTrue(driver.TryGetReloadUpper(0), "瞄准保持期换弹 → 叠加层在场");
            Assert.IsTrue(driver.TryGetAnimState(0, out var s4) && s4 == CharacterAnimId.FireIdle,
                "瞄准期换弹同样不迁叶（Override 形态与换弹叠加层互不干扰）");

            world.Frame = world.Weapons[slot * SimConfig.WeaponSlotsPerEntity + world.Entities[slot].SelectedWeapon].ReloadEndFrame;
            WeaponSystem.Run(world, new[] { new SimInputFrame { EntityId = selfId } }, WeaponTable.Default);
            driver.Tick(dt);
            Assert.IsFalse(driver.TryGetReloadUpper(0), "事实清除 → 叠加层释放");
            Assert.IsTrue(driver.TryGetCurrent(0, out var form) && form.Equals(CharacterAnimationIds.AimIdle),
                "持枪站姿 = AimIdle（与瞄准叶同形态）");

            driver.Dispose();
        }

        /// <summary>Sim 侧开始换弹真路径（懒装备 → 打掉一发 → Reload 边沿）；返回换弹结束帧。</summary>
        private static int StartReload(SimWorldState world, long selfId, int slot)
        {
            WeaponSystem.Run(world, new[] { new SimInputFrame { EntityId = selfId } }, WeaponTable.Default);     // 懒装备（默认步枪满弹）
            ref WeaponRuntime w = ref world.Weapons[slot * SimConfig.WeaponSlotsPerEntity
                + world.Entities[slot].SelectedWeapon];
            w.MagAmmo = WeaponConfig.Default.MagazineSize - 1;                             // 打掉一发（非满弹才可换）
            WeaponSystem.Run(world, new[]
            {
                new SimInputFrame { EntityId = selfId, Buttons = SimInputFrame.ButtonReload, ActionSeq = 1u },
            }, WeaponTable.Default);
            return w.ReloadEndFrame;
        }

        /// <summary>按真实链路交付一个 Fire 事件（写入缓冲 → SimView 静默门 → EventSink → 消费者）。</summary>
        private static void DeliverFire(SimView view, SimWorldState world, long shooterId, SimVector3 pos)
        {
            world.Events.Write(FrameEventKind.Fire, shooterId, 0L, 0, pos);
            view.OnFrameEvents(world);
            world.Events.Clear();                                   // FrameDriver 的消费后清空（决策⑥）
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 权重数学_速度轴三段插值_锚点连续且总和恒一()
        {
            var w = new float[3];                                           // 槽位序 {Idle, Walk, Run}

            LocomotionBlendMath.BuildSpeedWeights(0f, w);
            Assert.AreEqual(1f, w[0], 1e-4f, "静止：Idle 权重 1");

            LocomotionBlendMath.BuildSpeedWeights(1.5f, w);                 // 锚点 0.5 → 2.5 的中点
            Assert.AreEqual(0.5f, w[0], 1e-4f, "1.5 m/s：Idle/Walk 各半");
            Assert.AreEqual(0.5f, w[1], 1e-4f);
            Assert.AreEqual(0f, w[2], 1e-4f);

            // 锚点连续性：锚点两侧极限值相同（不会有跳变）——取锚点前 1e-3 与锚点对比
            LocomotionBlendMath.BuildSpeedWeights(LocomotionBlendMath.WalkFullMps - 1e-3f, w);
            float belowIdle = w[0], belowWalk = w[1];
            LocomotionBlendMath.BuildSpeedWeights(LocomotionBlendMath.WalkFullMps, w);
            Assert.AreEqual(belowIdle, w[0], 2e-3f, "Walk 锚点两侧 Idle 权重连续");
            Assert.AreEqual(belowWalk, w[1], 2e-3f, "Walk 锚点两侧 Walk 权重连续");

            LocomotionBlendMath.BuildSpeedWeights(LocomotionBlendMath.RunFullMps, w);
            Assert.AreEqual(1f, w[2], 1e-4f, "Run 锚点：Run 权重 1");

            foreach (var speed in new[] { 0f, 0.5f, 1.2f, 2.5f, 3.4f, 4.5f, 9f })
            {
                LocomotionBlendMath.BuildSpeedWeights(speed, w);
                Assert.AreEqual(1f, w[0] + w[1] + w[2], 1e-4f, $"{speed} m/s：权重总和必须恒为 1");
            }
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 权重数学_方向轴四向相邻插值_边界落槽位且跨扇区连续()
        {
            var w = new float[4];                                           // 槽位序 {F, R, B, L}

            LocomotionBlendMath.BuildAimWeights(0f, w);
            Assert.AreEqual(1f, w[0], 1e-4f, "正前：F 权重 1");

            LocomotionBlendMath.BuildAimWeights(45f, w);
            Assert.AreEqual(0.5f, w[0], 1e-4f, "45°：F 与相邻侧向各半");
            Assert.AreEqual(0.5f, w[1], 1e-4f, "45° 落在 F→R 扇区：相邻侧向是 R");

            LocomotionBlendMath.BuildAimWeights(90f, w);
            Assert.AreEqual(0f, w[0], 1e-4f, "正侧向是槽位边界：F 权重 0");
            Assert.AreEqual(1f, w[1], 1e-4f, "正侧向（+90°）落在 R 槽位，权重 1");

            LocomotionBlendMath.BuildAimWeights(-90f, w);
            Assert.AreEqual(1f, w[3], 1e-4f, "负侧向（−90°）落在 L 槽位，权重 1");

            LocomotionBlendMath.BuildAimWeights(180f, w);
            Assert.AreEqual(1f, w[2], 1e-4f, "正后：B 权重 1");

            // 跨扇区连续 + 环上负角：权重非负、总和恒 1（相邻两片插值，不是硬切）
            foreach (var deg in new[] { -180f, -135f, -45f, 0f, 22.5f, 89f, 91f, 135f, 179f })
            {
                LocomotionBlendMath.BuildAimWeights(deg, w);
                Assert.AreEqual(1f, w[0] + w[1] + w[2] + w[3], 1e-4f, $"{deg}°：权重总和必须恒为 1");
                for (int i = 0; i < 4; i++) Assert.GreaterOrEqual(w[i], 0f, $"{deg}°：槽位 {i} 权重不得为负");
            }
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 驱动_灰盒视图_无Animator不建播放器不抛()
        {
            var world = new SimWorldState { RngState = 1UL };
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);

            SimView view = new SimView(world, null,
                factory: (loc, parent) => Scope.CreateGameObject(loc),      // 灰盒形态：无 Animator
                recycler: null);
            var driver = new CharacterLocomotionDriver(view, ProductionProfile(), manifest: Manifest());
            const float dt = 1f / 60f;

            view.Tick(dt);
            Assert.DoesNotThrow(() => driver.Tick(dt), "无 Animator 的视图必须静默降级（表现为无动画）");
            Assert.AreEqual(0, driver.AnimatedViews);
            Assert.IsFalse(driver.TryGetCurrent(0, out _));

            driver.Dispose();
        }
    }
}
