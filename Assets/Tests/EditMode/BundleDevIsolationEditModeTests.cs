using LiteGame.EditorTools;
using LiteTesting;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 出包 dev 件隔离（A6 接线守卫）：收集清单不得落在 dev 目录，且每个包必须使用工程忽略规则
    /// （`/Editor/` 等编辑器资源由规则排除）。构建期另有
    /// <see cref="BuiltinBundleBuildInvoker"/> 对**实际构建报告**逐资源核对（本套件钉配置面）。
    ///
    /// 收集清单经 SerializedObject 读取：测试程序集不反向依赖 YooAsset.Editor，字段名与序列化面同源。
    /// </summary>
    public sealed class BundleDevIsolationEditModeTests
    {
        private const string CollectorSettingPath = "Assets/BundleCollectorSetting.asset";

        [Test]
        [Category(TestCategory.Contract)]
        public void dev路径判定_编辑器与开发装配层命中_产品资源放行()
        {
            Assert.IsTrue(BuiltinBundleBuildInvoker.IsDevAssetPath("Assets/LiteGame/Editor/Gm/GmPanel.cs"));
            Assert.IsTrue(BuiltinBundleBuildInvoker.IsDevAssetPath("Assets/ShareResource/UI/Screens/Editor/Preview.prefab"));
            Assert.IsTrue(BuiltinBundleBuildInvoker.IsDevAssetPath("Assets/LiteGame/Dev/Projection/Hud/DevHud.prefab"));
            Assert.IsTrue(BuiltinBundleBuildInvoker.IsDevAssetPath("Assets/LiteGame/Dev/LocalServer/Room.prefab"));

            Assert.IsFalse(BuiltinBundleBuildInvoker.IsDevAssetPath("Assets/ShareResource/UI/Screens/Page_Login.prefab"));
            Assert.IsFalse(BuiltinBundleBuildInvoker.IsDevAssetPath("Assets/LiteGame/Lua/UI/Page_Login.lua"));
            Assert.IsFalse(BuiltinBundleBuildInvoker.IsDevAssetPath(null));
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 收集清单_全部包用工程忽略规则_且无dev目录收集路径()
        {
            var setting = AssetDatabase.LoadAssetAtPath<ScriptableObject>(CollectorSettingPath);
            Assert.IsNotNull(setting, $"收集清单缺失：{CollectorSettingPath}");

            var so = new SerializedObject(setting);
            SerializedProperty packages = so.FindProperty("Packages");
            Assert.IsNotNull(packages, "收集清单缺少 Packages 字段（YooAsset 结构变更？）");
            Assert.Greater(packages.arraySize, 0, "收集清单为空");

            int checkedCollectors = 0;
            for (int p = 0; p < packages.arraySize; p++)
            {
                SerializedProperty package = packages.GetArrayElementAtIndex(p);
                string packageName = package.FindPropertyRelative("PackageName").stringValue;
                Assert.AreEqual(nameof(LiteGame.Editor.LiteGameIgnoreRule),
                    package.FindPropertyRelative("IgnoreRuleName").stringValue,
                    $"包 {packageName} 必须使用工程忽略规则（Editor 资源排除的单一来源）");

                SerializedProperty groups = package.FindPropertyRelative("Groups");
                for (int g = 0; g < groups.arraySize; g++)
                {
                    SerializedProperty group = groups.GetArrayElementAtIndex(g);
                    string groupName = group.FindPropertyRelative("GroupName").stringValue;
                    SerializedProperty collectors = group.FindPropertyRelative("Collectors");
                    for (int c = 0; c < collectors.arraySize; c++)
                    {
                        string collectPath = collectors.GetArrayElementAtIndex(c)
                            .FindPropertyRelative("CollectPath").stringValue;
                        checkedCollectors++;
                        Assert.IsFalse(BuiltinBundleBuildInvoker.IsDevAssetPath(collectPath + "/"),
                            $"收集路径落在 dev 目录（{packageName}/{groupName}）：{collectPath}");
                    }
                }
            }

            Assert.Greater(checkedCollectors, 0, "收集清单没有任何收集器——守卫失去意义");
        }
    }
}
