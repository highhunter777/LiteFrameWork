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
    /// 持枪约束（换弹上半身叠加期间武器骨跟随持枪手）EditMode 用例：真角色 prefab +
    /// <see cref="GripBake"/> 烘焙常量（编译期同值，无资产环境依赖）+ 真片段，走生产路径
    /// （后端分层图 → 动画 Evaluate → 约束覆写）。
    ///
    /// **断言口径一律绝对事实**（期望位姿由跟随骨变换乘烘焙偏移的解析式算出，不用相对比较）——
    /// 相对断言在历史缺陷（武器脱手悬空）上曾全绿放行。
    /// </summary>
    public sealed class WeaponGripConstraintEditModeTests : UnityTestBase
    {
        [SetUp]
        public void ResetDevOverride()
        {
            // 幅度断言依赖烘焙值；GM 面板的实时覆写是跨用例全局面——进用例前显性复位。
            GripTuning.TorsoWeightDevOverride = -1f;
        }

        [TearDown]
        public void RestoreDevOverride()
        {
            GripTuning.TorsoWeightDevOverride = -1f;
        }

        private GameObject LoadPrefabOrIgnore()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CombatGirlsAnimationProfile.ViewPrefabPath);
            if (prefab == null) Assert.Ignore("CombatGirlsCharacterPack 未入库——真角色动画用例跳过");
            return prefab;
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

        private static Vector3 BakedOffsetPos => new Vector3(GripBake.LocalPosX, GripBake.LocalPosY, GripBake.LocalPosZ);
        private static Quaternion BakedOffsetRot => Quaternion.Euler(GripBake.LocalEulerX, GripBake.LocalEulerY, GripBake.LocalEulerZ);

        private static void ExpectGripPose(Transform follow, Transform weapon,
            out Vector3 localPos, out Quaternion localRot)
        {
            localPos = weapon.parent.InverseTransformPoint(follow.TransformPoint(BakedOffsetPos));
            localRot = Quaternion.Inverse(weapon.parent.rotation) * (follow.rotation * BakedOffsetRot);
        }

        private static void PlayBaseAndReload(AnimatorAnimationBackend backend)
        {
            backend.TryPlay(new AnimationResolvedPlayback(default, AnimationChannel.Base, "Idle", 0f, 1f, false, true));
            backend.TryPlay(new AnimationResolvedPlayback(default, AnimationChannel.Overlay, "Reload", 0f, 1f, false, false));
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 换弹期_武器骨位姿等于跟随骨乘烘焙偏移_退出即交还动画()
        {
            var prefab = LoadPrefabOrIgnore();
            var go = Scope.Track(Object.Instantiate(prefab));
            var animator = go.GetComponentInChildren<Animator>(true);
            var follow = animator.transform.Find(GripBake.FollowPath);
            var weapon = animator.transform.Find(GripBake.WeaponPath);
            Assert.IsNotNull(follow, "跟随骨路径（烘焙常量）在真骨架上存在");
            Assert.IsNotNull(weapon, "武器骨路径（烘焙常量）在真骨架上存在");

            var backend = MakeBackend(animator, Profile());
            var grip = new WeaponGripConstraint(animator);
            Assert.IsTrue(grip.IsValid, "烘焙常量骨路径在真骨架上可解析");
            PlayBaseAndReload(backend);

            const float step = 1f / 60f;
            Vector3 animOnlyPos = default;
            for (int i = 0; i < 24; i++)
            {
                backend.Tick(step);
                animOnlyPos = weapon.localPosition;
                grip.Update(true);
            }

            ExpectGripPose(follow, weapon, out var wantPos, out var wantRot);
            Assert.AreEqual(wantPos.x, weapon.localPosition.x, 1e-4f, "枪骨位置 = 跟随骨变换 x 烘焙偏移（X）");
            Assert.AreEqual(wantPos.y, weapon.localPosition.y, 1e-4f, "枪骨位置 = 跟随骨变换 x 烘焙偏移（Y）");
            Assert.AreEqual(wantPos.z, weapon.localPosition.z, 1e-4f, "枪骨位置 = 跟随骨变换 x 烘焙偏移（Z）");
            Assert.Less(Quaternion.Angle(wantRot, weapon.localRotation), 0.5f, "枪骨旋转 = 跟随骨旋转 x 烘焙偏移");
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
            var go = Scope.Track(Object.Instantiate(prefab));
            var animator = go.GetComponentInChildren<Animator>(true);
            var follow = animator.transform.Find(GripBake.FollowPath);
            var weapon = animator.transform.Find(GripBake.WeaponPath);
            Assert.GreaterOrEqual(GripBake.TorsoWeight, 1f, "烘焙幅度 = 1（完全跟随）；调权重走 GM 覆写，不进烘焙值");

            var backend = MakeBackend(animator, Profile());
            var grip = new WeaponGripConstraint(animator);
            PlayBaseAndReload(backend);

            const float step = 1f / 60f;
            var animValue = weapon.localPosition;
            for (int i = 0; i < 24; i++)
            {
                backend.Tick(step);
                animValue = weapon.localPosition;
                grip.Update(true);
            }
            ExpectGripPose(follow, weapon, out var fullPos, out _);
            Assert.Less(Vector3.Distance(fullPos, weapon.localPosition), 1e-4f, "权重 1 = 完全跟随烘焙偏移");
            Assert.Greater(Vector3.Distance(animValue, weapon.localPosition), 1e-4f,
                "全跟随时确已偏离动画值（否则断言是空转）");

            backend.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 非换弹期_约束零写入_动画值原样()
        {
            var prefab = LoadPrefabOrIgnore();
            var go = Scope.Track(Object.Instantiate(prefab));
            var animator = go.GetComponentInChildren<Animator>(true);
            var weapon = animator.transform.Find(GripBake.WeaponPath);

            var backend = MakeBackend(animator, Profile());
            var grip = new WeaponGripConstraint(animator);
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

        [Test]
        [Category(TestCategory.Contract)]
        public void 俯仰姿态_枪骨随Chest同刚体旋转_不脱节()
        {
            var prefab = LoadPrefabOrIgnore();
            var go = Scope.Track(Object.Instantiate(prefab));
            var animator = go.GetComponentInChildren<Animator>(true);
            var chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            var weapon = animator.transform.Find(GripBake.WeaponPath);
            Assert.IsNotNull(chest, "Chest 人形映射必须存在");
            Assert.IsNotNull(weapon, "武器骨路径（烘焙常量）必须存在");
            Assert.IsFalse(weapon.IsChildOf(chest), "枪骨是 root 级（非 Chest 子级）——本用例覆盖镜像补转");

            var posture = new AimPitchPostureDriver(animator);
            Assert.IsTrue(posture.IsValid, "俯仰姿态件应可建");

            Quaternion chestBefore = chest.rotation;
            Vector3 weaponPosBefore = weapon.position;
            Quaternion weaponRotBefore = weapon.rotation;
            Vector3 pivot = chest.position;

            posture.Update(true, 30f * Mathf.Deg2Rad);

            float angle = -AimPitchPosture.UpperBodyPitch(30f * Mathf.Deg2Rad) * Mathf.Rad2Deg;
            Quaternion pitch = Quaternion.AngleAxis(angle, animator.transform.right);

            Assert.Less(Quaternion.Angle(pitch * chestBefore, chest.rotation), 0.1f,
                "Chest 旋转 = 俯仰增量");
            Assert.Less(Vector3.Distance(pivot + pitch * (weaponPosBefore - pivot), weapon.position), 1e-4f,
                "枪骨位置随 Chest 同刚体绕枢轴转（枪不能留在原地）");
            Assert.Less(Quaternion.Angle(pitch * weaponRotBefore, weapon.rotation), 0.1f,
                "枪骨旋转随 Chest 同刚体转");
            Assert.AreEqual(1, posture.AppliedFrames, "射击语境确实覆写");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 瞄准对齐_枪口前向指向准心点_非瞄准不写()
        {
            var prefab = LoadPrefabOrIgnore();
            var go = Scope.Track(Object.Instantiate(prefab));
            var animator = go.GetComponentInChildren<Animator>(true);
            var aim = new WeaponAimConstraint(animator);
            Assert.IsTrue(aim.IsValid, "Chest/枪骨/Muzzle 路径必须可解析");

            Transform muzzle = FindDeep(animator.transform, "Muzzle");
            Transform weapon = animator.transform.Find(GripBake.WeaponPath);
            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Assert.IsNotNull(muzzle, "Muzzle 必须存在");
            Assert.IsNotNull(weapon, "枪骨必须存在");
            Assert.IsNotNull(hand, "RightHand 必须存在");
            Vector3 handLocalWeapon = hand.InverseTransformPoint(weapon.position);
            Quaternion handLocalRot = Quaternion.Inverse(hand.rotation) * weapon.rotation;
            Vector3 target = muzzle.position + muzzle.forward * 5f + Vector3.up * 0.25f;
            Vector3 before = muzzle.forward;

            Assert.IsTrue(aim.Update(false, false, target), "非射击语境调用不失败");
            Assert.Less(Vector3.Angle(before, muzzle.forward), 0.01f, "非射击语境不写枪骨");

            Assert.IsTrue(aim.Update(false, true, target), "腰射语境执行对齐");
            Vector3 toTarget = (target - muzzle.position).normalized;
            Assert.Less(Vector3.Angle(muzzle.forward, toTarget), 2f,
                "腰射枪口前向指向准心点（残余误差 <2°）");
            Assert.Less(Vector3.Distance(handLocalWeapon, hand.InverseTransformPoint(weapon.position)), 1e-3f,
                "腰射后手/枪相对位置不变（手不脱枪）");
            Assert.Less(Quaternion.Angle(handLocalRot, Quaternion.Inverse(hand.rotation) * weapon.rotation), 0.1f,
                "腰射后手/枪相对旋转不变");
            Assert.AreEqual(1, aim.AppliedFrames, "腰射确实修正一帧");

            Assert.IsTrue(aim.Update(true, false, target), "ADS 语境执行对齐");
            Assert.Less(Vector3.Angle(muzzle.forward, (target - muzzle.position).normalized), 2f,
                "ADS 枪口前向指向准心点（残余误差 <2°）");
            Assert.Less(Vector3.Distance(handLocalWeapon, hand.InverseTransformPoint(weapon.position)), 1e-3f,
                "ADS 后手/枪相对位置不变（手不脱枪）");
            Assert.Less(Quaternion.Angle(handLocalRot, Quaternion.Inverse(hand.rotation) * weapon.rotation), 0.1f,
                "ADS 后手/枪相对旋转不变");
            Assert.AreEqual(2, aim.AppliedFrames, "ADS 也修正一帧");
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
