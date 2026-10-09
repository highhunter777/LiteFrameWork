using System.Collections.Generic;
using LiteClient;
using LiteView.Animation;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 真角色资产校验（模型只用 CombatGirlsCharacterPack，主要动画也用该包）。
    ///
    /// **视图形态＝直 Clip 模型**：prefab 只挂 <see cref="LiteAnimator"/>（RequireComponent
    /// 自动补 Animator、Avatar 由组件持有下发），**不挂控制器**——片源走片段清单
    /// （`CombatGirlsAnimationProfile.ClipManifestPath`，键单源在 Profile）。
    ///
    /// 资源包按仓库政策不入 VCS（.gitignore"第三方素材包不入库"）——**缺包克隆 Ignore 跳过**
    /// （对局走 SimView 灰盒兜底，由既有用例覆盖）；有包机器全量校验。
    /// </summary>
    public sealed class CharacterAssetEditModeTests : UnityTestBase
    {
        private const string PrefabPath = CombatGirlsAnimationProfile.ViewPrefabPath;

        [Test]
        [Category(TestCategory.Contract)]
        public void 视图prefab_直Clip形态_只挂绑定组件与主Avatar并含蒙皮网格()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Assert.Ignore("CombatGirlsCharacterPack 未入库——真角色资产校验跳过（对局走灰盒兜底）"); return; }

            // 发现面＝视图绑定组件（唯一判据；RequireComponent 保证 Animator 在场且不可被单独剥除）
            var binding = prefab.GetComponentInChildren<LiteAnimator>(true);
            Assert.IsNotNull(binding, "视图 prefab 必须挂 LiteAnimator（可动画视图的唯一挂件）");

            var animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(animator, "RequireComponent 必须保证 Animator 在场");
            Assert.IsNull(animator.runtimeAnimatorController, "直 Clip 模型：Animator 不挂控制器（片源走片段清单）");
            Assert.IsNotNull(binding.Avatar, "绑定组件必须持有主 Avatar（Humanoid_F 源——包内全部模型复制它）");
            Assert.AreEqual(binding.Avatar, animator.avatar, "组件持有的 Avatar 必须已下发到 Animator");
            Assert.AreEqual(AnimatorCullingMode.AlwaysAnimate, animator.cullingMode, "裁剪模式＝AlwaysAnimate（插值/俯视角口径）");
            Assert.IsNotNull(prefab.GetComponentInChildren<SkinnedMeshRenderer>(true), "角色模型必须含蒙皮网格");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 片段清单_覆盖Profile全绑定键且片段可解析()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Assert.Ignore("CombatGirlsCharacterPack 未入库——真角色资产校验跳过"); return; }

            var manifest = AssetDatabase.LoadAssetAtPath<AnimationClipManifest>(CombatGirlsAnimationProfile.ClipManifestPath);
            Assert.IsNotNull(manifest, $"片段清单缺失:{CombatGirlsAnimationProfile.ClipManifestPath}（改绑定键后须重跑构建器）");

            // 覆盖校验与驱动构造期同一口径：Profile 两张登记表（单片段 + 混合槽位）的每个绑定键
            // 都必须在清单可解析且片段引用非空——防 Profile 与清单两处登记面漂移
            var profile = AnimationProfileTableSource.Load(ConfigService.DataDir, CombatGirlsAnimationProfile.ModelFamily);
            foreach (var def in profile.Definitions)
            {
                Assert.IsTrue(manifest.TryGetClip(def.Binding, out var clip) && clip != null,
                    $"清单缺绑定或片段断引用:{def.Id}:{def.Binding}");
            }
            foreach (var blend in profile.BlendDefinitions)
            {
                for (int slot = 0; slot < blend.SlotCount; slot++)
                {
                    Assert.IsTrue(manifest.TryGetClip(blend.Bindings[slot], out var clip) && clip != null,
                        $"清单缺混合槽位或片段断引用:{blend.Id}:槽{slot}:{blend.Bindings[slot]}");
                }
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 包内控制器_移动与主要战斗状态齐备()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Assert.Ignore("CombatGirlsCharacterPack 未入库——真角色资产校验跳过"); return; }

            // 控制器不再挂在视图上（直 Clip 模型），但它仍是清单构建器的片段解析源——
            // 状态齐备性照旧校验（资产面完整性，非装配依赖）
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CombatGirlsAnimationProfile.ControllerPath);
            Assert.IsNotNull(controller, "包内控制器缺失");

            var states = new HashSet<string>();
            CollectStates(controller.layers[0].stateMachine, states);

            // 移动三态 + 包内已备的主要战斗状态（出现消费者时登记 Profile）
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
            // 收集组为单条 `Assets/Prefab`（运行时 prefab 所在目录）；模型/动画依赖在
            // `CombatGirlsCharacterPack` 内由 **YooAsset 依赖链自动收集**（见组描述），本用例断言 prefab 目录。
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
