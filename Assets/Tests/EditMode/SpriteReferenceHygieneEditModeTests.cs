using System.Collections.Generic;
using LiteSim;
using LiteTesting;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 收集组 UI prefab 的 sprite 引用卫生守卫（"复制进收集目录再引用"资源规范的机检化）：
    /// **ShareResource/UI 下全部 prefab 的 sprite 引用（Image.sprite ＋ Button 三态）不得指向
    /// 原始库目录**（`Assets/Art`、`Assets/Synty` 等不入 VCS 的第三方/素材库——干净克隆下这些
    /// 引用即悬空；且原始目录不在任何收集组内，直引是"收集组外依赖"）。复制件常规位＝
    /// `ShareResource/UI/Icons/<用途>/`（随引用 prefab 依赖入包）；本守卫把"再添直引"变成测试红。
    /// 背景：迁移批实证 8 prefab × 41 处引用悬空（同 sprite 在 Hud 用复制件、页面用原始库并存）。
    /// </summary>
    public sealed class SpriteReferenceHygieneEditModeTests
    {
        /// <summary>原始库目录（.gitignore 的 Assets 排除段中含美术素材的根——引用侧禁入）。</summary>
        private static readonly string[] RawRoots =
        {
            "Assets/Art/", "Assets/Synty/", "Assets/Model/", "Assets/Animation/", "Assets/Animator/",
            "Assets/Map/", "Assets/FX/", "Assets/Sound/", "Assets/CombatGirlsCharacterPack/",
            "Assets/KriptoFX/", "Assets/DamageNumbersPro/", "Assets/MagicaCloth2/", "Assets/GameFramework/",
            "Assets/GameMain/", "Assets/TutorialInfo/", "Assets/StateMachine/", "Assets/Resources/",
        };

        private static bool IsRaw(string assetPath)
        {
            foreach (string root in RawRoots)
                if (assetPath.StartsWith(root, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 收集组UI预制体_原始库sprite直引清零()
        {
            var violations = new List<string>();
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/ShareResource/UI" });
            Assert.Greater(guids.Length, 0, "扫描面失效（收集目录无 prefab？核对路径）");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                foreach (Image image in prefab.GetComponentsInChildren<Image>(true))
                {
                    if (image.sprite == null) continue;
                    string spritePath = AssetDatabase.GetAssetPath(image.sprite);
                    if (!string.IsNullOrEmpty(spritePath) && IsRaw(spritePath))
                        violations.Add($"{path} :: {image.name} :: {spritePath}");
                }

                foreach (Button button in prefab.GetComponentsInChildren<Button>(true))
                {
                    SpriteState state = button.spriteState;
                    foreach (Sprite sprite in new[]
                             {
                                 state.highlightedSprite, state.pressedSprite, state.disabledSprite,
                             })
                    {
                        if (sprite == null) continue;
                        string spritePath = AssetDatabase.GetAssetPath(sprite);
                        if (!string.IsNullOrEmpty(spritePath) && IsRaw(spritePath))
                            violations.Add($"{path} :: {button.name}(spriteState) :: {spritePath}");
                    }
                }
            }

            Assert.AreEqual(0, violations.Count,
                "收集组 UI prefab 不得直引原始库 sprite（复制进 ShareResource/UI/Icons/<用途>/ 再引用）:\n"
                + string.Join("\n", violations));
        }
    }
}
