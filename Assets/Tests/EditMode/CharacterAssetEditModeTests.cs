using System.Collections.Generic;
using LiteSim.View.Animation;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 真角色资产校验（2026-09-25 资源指令：模型只用 CombatGirlsCharacterPack，主要动画也用该包——
    /// RifleGirl 模型 + 包内 Rifle_Controller + Humanoid_F 主 Avatar）。
    ///
    /// 资源包按仓库政策不入 VCS（.gitignore"第三方素材包不入库"）——**缺包克隆 Ignore 跳过**
    /// （对局走 SimView 灰盒兜底，由既有用例覆盖）；有包机器全量校验。
    /// </summary>
    public sealed class CharacterAssetEditModeTests : UnityTestBase
    {
        private const string PrefabPath = CombatGirlsAnimationProfile.ViewPrefabPath;

        [Test]
        [Category(TestCategory.Contract)]
        public void 视图prefab_挂包内控制器与主Avatar并含蒙皮网格()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Assert.Ignore("CombatGirlsCharacterPack 未入库——真角色资产校验跳过（对局走灰盒兜底）"); return; }

            var animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(animator, "视图 prefab 必须带 Animator");
            Assert.IsNotNull(animator.runtimeAnimatorController, "Animator 必须挂控制器");
            Assert.AreEqual(CombatGirlsAnimationProfile.ControllerPath,
                AssetDatabase.GetAssetPath(animator.runtimeAnimatorController), "控制器必须是包内 Rifle_Controller（单源）");
            Assert.IsNotNull(animator.avatar, "必须挂主 Avatar（Humanoid_F.fbx 源——包内全部模型复制它）");
            Assert.IsNotNull(prefab.GetComponentInChildren<SkinnedMeshRenderer>(true), "角色模型必须含蒙皮网格");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 包内控制器_移动与主要战斗状态齐备()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Assert.Ignore("CombatGirlsCharacterPack 未入库——真角色资产校验跳过"); return; }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CombatGirlsAnimationProfile.ControllerPath);
            Assert.IsNotNull(controller, "包内控制器缺失");

            var states = new HashSet<string>();
            CollectStates(controller.layers[0].stateMachine, states);

            // 移动三态（本批消费）+ 包内已备的主要战斗状态（出现消费者时登记 Profile）
            foreach (var name in new[] { "Idle", "Walk", "Run", "Hit1", "Hit2", "Die1", "Die2", "Reload", "AimIdle", "Evade", "Stun" })
                Assert.IsTrue(states.Contains(name), $"包内控制器缺状态:{name}");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 收集器_Characters组覆盖运行时目录()
        {
            // 期望值从**单源**推：运行时角色 prefab（`CombatGirlsAnimationProfile.ViewPrefabPath`）所在目录
            // 必须被 Characters 组收集——否则角色进不了内容包。不硬编码目录字面量，prefab 换位置时断言跟着走。
            //
            // 2026-09-27 裁决：收集组保持单条 `Assets/Prefab`（prefab 已随 2a0906a 迁到该目录），
            // 模型/动画依赖在 `CombatGirlsCharacterPack` 内由 **YooAsset 依赖链自动收集**（见组描述），
            // 不再要求显式收 `CombatGirlsCharacterPack/Runtime`——故本用例由「断言包目录」改为「断言 prefab 目录」。
            string expectedDir = System.IO.Path.GetDirectoryName(CombatGirlsAnimationProfile.ViewPrefabPath)
                .Replace('\\', '/');

            // 反射读取 YooAsset 收集配置（测试程序集不引 YooAsset.Editor——避免为一条结构断言扩 asmdef）
            var setting = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/BundleCollectorSetting.asset");
            Assert.IsNotNull(setting, "BundleCollectorSetting 缺失");

            bool found = false;
            var packages = (System.Collections.IEnumerable)setting.GetType().GetField("Packages").GetValue(setting);
            foreach (var pkg in packages)
            {
                var groups = (System.Collections.IEnumerable)pkg.GetType().GetField("Groups").GetValue(pkg);
                foreach (var group in groups)
                {
                    var groupName = (string)group.GetType().GetField("GroupName").GetValue(group);
                    if (groupName != "Characters") continue;
                    var collectors = (System.Collections.IEnumerable)group.GetType().GetField("Collectors").GetValue(group);
                    foreach (var collector in collectors)
                    {
                        var path = (string)collector.GetType().GetField("CollectPath").GetValue(collector);
                        if (path == expectedDir) found = true;
                    }
                }
            }
            Assert.IsTrue(found, $"Characters 收集组必须覆盖运行时角色 prefab 目录 {expectedDir}——否则角色进不了内容包");
        }

        /// <summary>递归收集层 0 全部状态名（含子状态机）。</summary>
        private static void CollectStates(AnimatorStateMachine machine, HashSet<string> into)
        {
            foreach (var child in machine.states) into.Add(child.state.name);
            foreach (var child in machine.stateMachines) CollectStates(child.stateMachine, into);
        }
    }
}
