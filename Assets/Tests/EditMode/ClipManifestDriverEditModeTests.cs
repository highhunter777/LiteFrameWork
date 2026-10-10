using System;
using LiteFramework.Animation;
using LiteSim;
using LiteTesting;
using LiteTesting.Unity;
using LiteView;
using LiteView.Animation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 去 AC 主路径收口（《动画模块专项设计》§4 片源载体裁决 + §7 灰盒放开）：片段清单 SO
    /// （键→Clip 显式引用）装配期逐键 RegisterClip、Profile 绑定全覆盖校验（校验后才放行播放）、
    /// 无控制器视图（直 Clip 模型）的驱动全链（机 Start 即提交 MoveBlend 落位——不依赖默认姿态位）。
    /// 片段与清单全程序化构造，不依赖美术资产（真角色 prefab + 控制器便利源由
    /// <see cref="CharacterLocomotionEditModeTests"/> 覆盖）。
    /// </summary>
    public sealed class ClipManifestDriverEditModeTests : UnityTestBase
    {
        // ---- 替身与工具 ----

        /// <summary>程序化片段：曲线打在**子骨**（路径 Bone）——真实角色片段只动骨骼不覆写根位移；
        /// 根位移是驱动的测量输入（帧间位移→速度），动画层覆写它会污染测试的运动输入。</summary>
        private static AnimationClip Clip(string name, float seconds)
        {
            var clip = new AnimationClip { name = name };
            clip.SetCurve("Bone", typeof(Transform), "localPosition.x",
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(seconds, 1f)));
            Assert.Greater(clip.length, 0f, "程序化片段须有可判定的时长");
            return clip;
        }

        /// <summary>直 Clip 模型视图替身：**只挂绑定组件**（RequireComponent 自动补 Animator）+ 子骨 Bone
        /// （片段驱动目标——曲线不打根位移，根位移是驱动的测量输入）。</summary>
        private GameObject NewDirectClipView()
        {
            var root = new GameObject("anim");
            root.AddComponent<LiteAnimator>();            // RequireComponent：Animator 自动补
            var bone = new GameObject("Bone");
            bone.transform.SetParent(root.transform, false);
            return root;
        }

        // ---- 组件契约（发现面唯一判据 + 完整配置面）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 组件_RequireComponent自动补Animator_配置面全量下发()
        {
            var go = new GameObject("anim");
            var binding = go.AddComponent<LiteAnimator>();
            var animator = go.GetComponent<Animator>();
            Assert.IsNotNull(animator, "RequireComponent：挂组件必须自动补 Animator");
            Assert.IsNotNull(binding.Animator, "组件的 Animator 读面必须解析到引擎输出目标");

            // 配置面＝Animator 的唯一配置源（编辑期 OnValidate 即下发）：
            // 默认值 = 直 Clip 模型口径（无控制器/无 Avatar[非人形合法]/不应用根运动/Normal/AlwaysAnimate）
            Assert.IsNull(animator.runtimeAnimatorController, "控制器默认空（直 Clip——可选便利源）");
            Assert.IsNull(animator.avatar, "Avatar 可空（非人形模型合法）");
            Assert.IsFalse(animator.applyRootMotion, "根运动默认关（联机角色权威位移归 Sim，§8）");
            Assert.AreEqual(AnimatorUpdateMode.Normal, animator.updateMode, "更新模式默认 Normal");
            Assert.AreEqual(AnimatorCullingMode.AlwaysAnimate, animator.cullingMode, "裁剪模式默认 AlwaysAnimate（插值/俯视角口径）");
            Assert.IsNull(binding.Manifest, "清单默认空（由视图作者在面板上声明）");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 组件_面板声明清单_驱动优先组件引用供片_无需装配传入()
        {
            var go = NewDirectClipView();
            var view = ViewWith(go);
            var full = FullManifest();
            SetManifest(go.GetComponent<LiteAnimator>(), full);   // 面板上声明的清单（组件序列化引用）

            using var driver = new CharacterLocomotionDriver(view, DriverProfile());   // 不传清单参数
            const float dt = 1f / 60f;
            view.Tick(dt);
            driver.Tick(dt);

            Assert.AreEqual(1, driver.AnimatedViews, "组件声明的清单即可供片（视图自含）——装配传入只是回退");
            Assert.AreEqual(0, driver.MissingManifestClips);
            Assert.IsTrue(driver.TryGetCurrent(0, out var form) && form.Equals(CharacterAnimationIds.MoveBlend),
                "机 Start 落位 MoveBlend（清单来自组件面板声明）");
        }

        /// <summary>把清单写进组件的序列化引用（模拟面板拖拽声明）。</summary>
        private static void SetManifest(LiteAnimator binding, AnimationClipManifest manifest)
        {
            var so = new SerializedObject(binding);
            so.FindProperty("manifest").objectReferenceValue = manifest;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>程序化清单（编辑面与资产构建器同径——SerializedObject 写序列化条目）。</summary>
        private AnimationClipManifest Manifest(params (string key, AnimationClip clip)[] entries)
        {
            var manifest = ScriptableObject.CreateInstance<AnimationClipManifest>();
            var so = new SerializedObject(manifest);
            var list = so.FindProperty("_entries");
            list.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                var elem = list.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("Key").stringValue = entries[i].key;
                elem.FindPropertyRelative("Clip").objectReferenceValue = entries[i].clip;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            Scope.Track(manifest);
            return manifest;
        }

        /// <summary>驱动要求的 Profile 形状（与 CombatGirls 同键位——绑定即清单键，值全程序化片段）。</summary>
        private static AnimationProfile DriverProfile()
            => new AnimationProfile()
                .Register(new AnimationDefinition(CharacterAnimationIds.Idle, AnimationChannel.Base,
                    "Idle", loop: true, minSpeed: 0.01f, maxSpeed: 2f))
                .Register(new AnimationDefinition(CharacterAnimationIds.Walk, AnimationChannel.Base,
                    "Walk", loop: true, minSpeed: 0.01f, maxSpeed: 2f))
                .Register(new AnimationDefinition(CharacterAnimationIds.Run, AnimationChannel.Base,
                    "Run", loop: true, minSpeed: 0.01f, maxSpeed: 2f))
                .Register(new AnimationDefinition(CharacterAnimationIds.AimIdle, AnimationChannel.Override,
                    "AimIdle", loop: true, minSpeed: 0.01f, maxSpeed: 2f))
                .Register(new AnimationDefinition(CharacterAnimationIds.Reload, AnimationChannel.Overlay,
                    "Reload", loop: false, minSpeed: 0.01f, maxSpeed: 2f))
                .RegisterBlend(new AnimationBlendDefinition(CharacterAnimationIds.MoveBlend, AnimationChannel.Base,
                    new[] { "Idle", "Walk", "Run" }, minSpeed: 0.01f, maxSpeed: 2f))
                .RegisterBlend(new AnimationBlendDefinition(CharacterAnimationIds.AimMoveBlend, AnimationChannel.Override,
                    new[] { "AimWalk_F", "AimWalk_R", "AimWalk_B", "AimWalk_L" }, minSpeed: 0.01f, maxSpeed: 2f));

        /// <summary>与 DriverProfile 全键位对齐的清单（9 键）。</summary>
        private AnimationClipManifest FullManifest()
            => Manifest(
                ("Idle", Clip("Idle", 1f)),
                ("Walk", Clip("Walk", 1f)),
                ("Run", Clip("Run", 1f)),
                ("AimIdle", Clip("AimIdle", 1f)),
                ("Reload", Clip("Reload", 1.4f)),
                ("AimWalk_F", Clip("AimWalk_F", 1f)),
                ("AimWalk_R", Clip("AimWalk_R", 1f)),
                ("AimWalk_B", Clip("AimWalk_B", 1f)),
                ("AimWalk_L", Clip("AimWalk_L", 1f)));

        /// <summary>单实体世界 + SimView；factory 决定视图形态（直 Clip 模型 = 裸 GameObject + 无控制器 Animator）。</summary>
        private SimView ViewWith(GameObject viewGo)
        {
            var world = new SimWorldState { RngState = 1UL };
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);
            return new SimView(world, null,
                factory: (loc, parent) =>
                {
                    viewGo.name = loc;
                    Scope.Track(viewGo);
                    return viewGo;
                },
                recycler: null);
        }

        // ---- 清单查询面 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 清单_键查询面_ContainsKey只看键_引用缺失不猜()
        {
            var idle = Clip("Idle", 1f);
            var manifest = Manifest(("Idle", idle), ("Reload", null));

            Assert.AreEqual(2, manifest.EntryCount);
            Assert.IsTrue(manifest.ContainsKey("Idle"));
            Assert.IsTrue(manifest.ContainsKey("Reload"), "引用缺失是资源态——键仍在（覆盖校验不因此失败）");
            Assert.IsFalse(manifest.ContainsKey("Walk"));

            Assert.IsTrue(manifest.TryGetClip("Idle", out var clip) && clip == idle);
            Assert.IsFalse(manifest.TryGetClip("Reload", out _), "引用缺失：不猜、不回退，交调用方显性处理");
            Assert.IsFalse(manifest.TryGetClip("Walk", out _));

            Assert.IsTrue(manifest.TryGetEntry(0, out var entry) && entry.Key == "Idle");
            Assert.IsFalse(manifest.TryGetEntry(-1, out _), "越界不猜");
            Assert.IsFalse(manifest.TryGetEntry(manifest.EntryCount, out _));
        }

        // ---- 覆盖校验（校验后才放行播放）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 覆盖校验_清单缺Profile绑定键_构造期显性失败()
        {
            var view = ViewWith(new GameObject("anim", typeof(Animator)));
            var profile = DriverProfile();

            // 缺 AimWalk_L（混合槽位键）——清单与 Profile 不同步 = 配置错误
            var broken = Manifest(
                ("Idle", Clip("Idle", 1f)), ("Walk", Clip("Walk", 1f)), ("Run", Clip("Run", 1f)),
                ("AimIdle", Clip("AimIdle", 1f)), ("Reload", Clip("Reload", 1f)),
                ("AimWalk_F", Clip("AimWalk_F", 1f)), ("AimWalk_R", Clip("AimWalk_R", 1f)),
                ("AimWalk_B", Clip("AimWalk_B", 1f)));

            Assert.Throws<ArgumentException>(() => new CharacterLocomotionDriver(view, profile, broken),
                "缺键必须装配期失败——不等到运行时逐提交静默失败");

            // 键全齐（引用可有缺失——资源态不拦装配）：构造成立
            var resourceBroken = Manifest(
                ("Idle", Clip("Idle", 1f)), ("Walk", null), ("Run", Clip("Run", 1f)),
                ("AimIdle", Clip("AimIdle", 1f)), ("Reload", Clip("Reload", 1f)),
                ("AimWalk_F", Clip("AimWalk_F", 1f)), ("AimWalk_R", Clip("AimWalk_R", 1f)),
                ("AimWalk_B", Clip("AimWalk_B", 1f)), ("AimWalk_L", null));
            using (var driver = new CharacterLocomotionDriver(view, profile, resourceBroken))
            {
                Assert.IsNotNull(driver, "键覆盖成立即放行（引用缺失归槽位登记计数，不拦装配）");
            }
        }

        // ---- 去 AC 驱动全链（直 Clip 模型：有 Animator、无控制器、清单供片）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 去AC_无控制器视图_清单供片建播放器_机Start落位MoveBlend()
        {
            var go = NewDirectClipView();
            Assert.IsNull(go.GetComponent<Animator>().runtimeAnimatorController, "前置：直 Clip 模型——无控制器资产");
            var view = ViewWith(go);

            using var driver = new CharacterLocomotionDriver(view, DriverProfile(), FullManifest());
            const float dt = 1f / 60f;
            view.Tick(dt);
            driver.Tick(dt);

            Assert.AreEqual(1, driver.AnimatedViews, "无控制器不跳过——清单供片即建播放器（灰盒放开）");
            Assert.AreEqual(0, driver.MissingManifestClips, "清单健康：逐键登记零缺失");
            Assert.IsTrue(driver.TryGetCurrent(0, out var form) && form.Equals(CharacterAnimationIds.MoveBlend),
                "机 Start 即提交 MoveBlend——落位不依赖默认姿态位（无控制器也无 T-pose 悬空）");

            // 后端真值（非 ctx 记账）：IModuleStats 聚合的激活通道数——MoveBlend 真提交进了图
            var stats = new System.Collections.Generic.Dictionary<string, string>();
            ((LiteFramework.IModuleStats)driver).Snapshot(stats);
            Assert.GreaterOrEqual(int.Parse(stats["channels"]), 1,
                "Base 通道激活——清单片段在直驱图上真在播（无控制器供片成立）");

            var weights = new float[3];
            Assert.IsTrue(driver.TryGetMotion(0, out _, weights));
            Assert.AreEqual(1f, weights[0], 1e-3f, "静止：Idle 权重 1");

            // 帧间位移 → 锁存 → 混合权重就地推进（清单片段真在驱动）
            view.TryGetView(0, out var moved);
            for (int i = 0; i < 5; i++) { moved.transform.position += new Vector3(0.025f, 0f, 0f); driver.Tick(dt); }
            Assert.IsTrue(driver.TryGetMotion(0, out _, weights));
            Assert.AreEqual(0.5f, weights[1], 0.05f, "1.5 m/s 落在 Idle→Walk 斜坡中点（直 Clip 路径权重推进）");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 去AC_引用缺失计数_其余键照常生效()
        {
            var go = NewDirectClipView();
            var view = ViewWith(go);
            var manifest = Manifest(
                ("Idle", Clip("Idle", 1f)), ("Walk", null), ("Run", Clip("Run", 1f)),
                ("AimIdle", Clip("AimIdle", 1f)), ("Reload", Clip("Reload", 1f)),
                ("AimWalk_F", Clip("AimWalk_F", 1f)), ("AimWalk_R", Clip("AimWalk_R", 1f)),
                ("AimWalk_B", Clip("AimWalk_B", 1f)), ("AimWalk_L", null));

            using var driver = new CharacterLocomotionDriver(view, DriverProfile(), manifest);
            const float dt = 1f / 60f;
            view.Tick(dt);
            driver.Tick(dt);

            Assert.AreEqual(2, driver.MissingManifestClips, "两条断引用（Walk/AimWalk_L）计数可观测");
            Assert.AreEqual(1, driver.AnimatedViews, "资源缺失态不拦装配——播放器照建");
            Assert.IsTrue(driver.TryGetCurrent(0, out var form) && form.Equals(CharacterAnimationIds.MoveBlend),
                "Idle/Run 在册：MoveBlend 照常提交落位");
        }

        // ---- 灰盒口径（诚实降级边界）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 灰盒_无绑定组件不建播放器_真灰盒仍跳过()
        {
            // ① 无绑定组件（纯 GameObject）：真灰盒——清单在场也不建（发现面＝组件，唯一判据）
            var plain = new GameObject("plain");
            var viewA = ViewWith(plain);
            using (var driver = new CharacterLocomotionDriver(viewA, DriverProfile(), FullManifest()))
            {
                viewA.Tick(1f / 60f);
                driver.Tick(1f / 60f);
                Assert.AreEqual(0, driver.AnimatedViews, "无绑定组件：跳过（与缺角色资源灰盒同姿势）");
            }

            // ② 裸 Animator 而无绑定组件：同样灰盒——裸 Animator 不构成"可动画视图"判据
            //    （防误挂：任何带 Animator 的物体不再被建播放器）
            var bare = new GameObject("bare", typeof(Animator));
            var viewB = ViewWith(bare);
            using (var driver = new CharacterLocomotionDriver(viewB, DriverProfile()))
            {
                viewB.Tick(1f / 60f);
                driver.Tick(1f / 60f);
                Assert.AreEqual(0, driver.AnimatedViews, "裸 Animator 无组件：灰盒（组件是唯一发现面）");
            }

            // ③ 绑定组件在场、但既无控制器又无清单：无任何片源——同样不建
            var bindingOnly = NewDirectClipView();
            var viewC = ViewWith(bindingOnly);
            using (var driver = new CharacterLocomotionDriver(viewC, DriverProfile()))
            {
                viewC.Tick(1f / 60f);
                driver.Tick(1f / 60f);
                Assert.AreEqual(0, driver.AnimatedViews, "控制器与清单皆空：无片源即灰盒");
            }
        }
    }
}
