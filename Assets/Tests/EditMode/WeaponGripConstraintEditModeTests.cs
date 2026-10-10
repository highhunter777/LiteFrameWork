using LiteFramework.Animation;
using LiteTesting;
using LiteTesting.Unity;
using LiteView.Animation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 持枪约束（换弹上半身叠加期间武器骨跟随躯干）EditMode 用例：真角色 prefab + 真偏移资产 +
    /// 真片段，走生产路径（后端分层图 → 动画 Evaluate → 约束覆写）。
    ///
    /// **断言口径一律绝对事实**（期望位姿由跟随骨变换乘偏移的解析式算出，不用相对比较）——
    /// 相对断言在历史缺陷（武器脱手悬空）上曾全绿放行。
    /// </summary>
    public sealed class WeaponGripConstraintEditModeTests : UnityTestBase
    {
        private const string OffsetPath = "Assets/Prefab/WeaponGripOffset.asset";

        private GameObject LoadPrefabOrIgnore()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CombatGirlsAnimationProfile.ViewPrefabPath);
            if (prefab == null) Assert.Ignore("CombatGirlsCharacterPack 未入库——真角色动画用例跳过");
            return prefab;
        }

        private WeaponGripOffset LoadOffsetOrIgnore()
        {
            var offset = AssetDatabase.LoadAssetAtPath<WeaponGripOffset>(OffsetPath);
            if (offset == null || !offset.IsComplete)
                Assert.Ignore("持枪偏移资产缺失或未烘焙（先跑 WeaponGripBaker）");
            return offset;
        }

        private static AnimationProfile Profile()
            => new AnimationProfile()
                .Register(new AnimationDefinition(CharacterAnimationIds.Idle, AnimationChannel.Base,
                    "Idle", loop: true, minSpeed: 0.1f, maxSpeed: 2f))
                .Register(new AnimationDefinition(CharacterAnimationIds.Reload, AnimationChannel.Overlay,
                    "Reload", loop: false, minSpeed: 0.1f, maxSpeed: 2f));

        private static AnimatorAnimationBackend MakeBackend(Animator animator, AnimationProfile profile)
        {
            var manifest = AssetDatabase.LoadAssetAtPath<AnimationClipManifest>(
                CombatGirlsAnimationProfile.ClipManifestPath);
            var backend = new AnimatorAnimationBackend(animator, 0f, profile.OverlayMaskExclusions);
            if (manifest != null) backend.RegisterManifest(manifest);
            return backend;
        }

        private static void ExpectGripPose(Transform follow, Transform weapon, WeaponGripOffset offset,
            out Vector3 localPos, out Quaternion localRot)
        {
            localPos = weapon.parent.InverseTransformPoint(follow.TransformPoint(offset.LocalPosition));
            localRot = Quaternion.Inverse(weapon.parent.rotation) * (follow.rotation * offset.LocalRotation);
        }

        private static void PlayBaseAndReload(AnimatorAnimationBackend backend)
        {
            backend.TryPlay(new AnimationResolvedPlayback(default, AnimationChannel.Base, "Idle", 0f, 1f, false, true));
            backend.TryPlay(new AnimationResolvedPlayback(default, AnimationChannel.Overlay, "Reload", 0f, 1f, false, false));
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 换弹期_武器骨位姿等于跟随骨乘偏移_退出即交还动画()
        {
            var prefab = LoadPrefabOrIgnore();
            var offset = LoadOffsetOrIgnore();
            var go = Scope.Track(Object.Instantiate(prefab));
            var animator = go.GetComponentInChildren<Animator>(true);
            var follow = animator.transform.Find(offset.FollowBonePath);
            var weapon = animator.transform.Find(offset.WeaponBonePath);
            Assert.IsNotNull(follow, "跟随骨路径在真骨架上存在");
            Assert.IsNotNull(weapon, "武器骨路径在真骨架上存在");

            var backend = MakeBackend(animator, Profile());
            var grip = new WeaponGripConstraint(animator, offset);
            Assert.IsTrue(grip.IsValid, "偏移资产在真骨架上可解析");
            PlayBaseAndReload(backend);

            const float step = 1f / 60f;
            Vector3 animOnlyPos = default;
            for (int i = 0; i < 24; i++)
            {
                backend.Tick(step);
                animOnlyPos = weapon.localPosition;
                grip.Update(true);
            }

            ExpectGripPose(follow, weapon, offset, out var wantPos, out var wantRot);
            Assert.AreEqual(wantPos.x, weapon.localPosition.x, 1e-4f, "枪骨位置 = 跟随骨变换 x 偏移（X）");
            Assert.AreEqual(wantPos.y, weapon.localPosition.y, 1e-4f, "枪骨位置 = 跟随骨变换 x 偏移（Y）");
            Assert.AreEqual(wantPos.z, weapon.localPosition.z, 1e-4f, "枪骨位置 = 跟随骨变换 x 偏移（Z）");
            Assert.Less(Quaternion.Angle(wantRot, weapon.localRotation), 0.5f, "枪骨旋转 = 跟随骨旋转 x 偏移");
            Assert.Greater(grip.AppliedFrames, 0, "换弹期确实覆写过帧");

            grip.Update(false);
            backend.Tick(step);
            Assert.Greater(Vector3.Distance(animOnlyPos, weapon.localPosition), 1e-5f,
                "退出后武器骨回到动画值（约束不留痕）");

            backend.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 幅度缩放_一等于完全跟随且确实偏离动画值()
        {
            var prefab = LoadPrefabOrIgnore();
            var offset = LoadOffsetOrIgnore();
            var go = Scope.Track(Object.Instantiate(prefab));
            var animator = go.GetComponentInChildren<Animator>(true);
            var follow = animator.transform.Find(offset.FollowBonePath);
            var weapon = animator.transform.Find(offset.WeaponBonePath);

            var backend = MakeBackend(animator, Profile());
            var grip = new WeaponGripConstraint(animator, offset);
            PlayBaseAndReload(backend);

            const float step = 1f / 60f;
            var animValue = weapon.localPosition;
            for (int i = 0; i < 24; i++)
            {
                backend.Tick(step);
                animValue = weapon.localPosition;
                grip.Update(true);
            }
            ExpectGripPose(follow, weapon, offset, out var fullPos, out _);
            Assert.Less(Vector3.Distance(fullPos, weapon.localPosition), 1e-4f, "权重 1 = 完全跟随偏移");
            Assert.Greater(Vector3.Distance(animValue, weapon.localPosition), 1e-4f,
                "全跟随时确已偏离动画值（否则断言是空转）");

            backend.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 非换弹期_约束零写入_动画值原样()
        {
            var prefab = LoadPrefabOrIgnore();
            var offset = LoadOffsetOrIgnore();
            var go = Scope.Track(Object.Instantiate(prefab));
            var animator = go.GetComponentInChildren<Animator>(true);
            var weapon = animator.transform.Find(offset.WeaponBonePath);

            var backend = MakeBackend(animator, Profile());
            var grip = new WeaponGripConstraint(animator, offset);
            backend.TryPlay(new AnimationResolvedPlayback(default, AnimationChannel.Base, "Idle", 0f, 1f, false, true));

            const float step = 1f / 60f;
            for (int i = 0; i < 12; i++)
            {
                backend.Tick(step);
                var before = weapon.localPosition;
                grip.Update(false);
                Assert.AreEqual(before.x, weapon.localPosition.x, 0f, "非换弹期不改写枪骨（X）");
                Assert.AreEqual(before.y, weapon.localPosition.y, 0f, "非换弹期不改写枪骨（Y）");
                Assert.AreEqual(before.z, weapon.localPosition.z, 0f, "非换弹期不改写枪骨（Z）");
            }
            Assert.AreEqual(0, grip.AppliedFrames, "非换弹期零覆写");

            backend.Dispose();
        }
    }
}
