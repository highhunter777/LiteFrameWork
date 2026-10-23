using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using LiteClient.UI;
using LiteGame.Editor.UiTools.Binding;
using LiteGame.Editor.UiTools.Validation;
using LiteGame.Toolchain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace LiteGame.Editor.UiTools.Page
{
    /// <summary>
    /// 起页内核（《UI 编辑器工具链专项设计》§3.1）：视觉基线 → 自包含 prefab + 根契约 + 页面清单草稿。
    /// **不写 Lua、不写 `tbuiform`、不跑 Luban**（归 <see cref="UiPageRegistration"/> 的登记动作）。
    ///
    /// 表读写归 <see cref="UiPageTableHelper"/>；本类只负责资产面与页根契约。
    /// </summary>
    public static class UiPageScaffold
    {
        public const string TemplateDir = "Assets/LiteGame/Editor/UiTools/Templates";
        public const string TemplatePath = TemplateDir + "/Tmp_Page.prefab";
        public const string LuaDir = "Assets/LiteGame/Lua/UI";

        /// <summary>页根契约参数（模板与起页共用单源）。</summary>
        public const float ReferenceWidth = 1920f;

        public const float ReferenceHeight = 1080f;
        public const float MatchWidthOrHeight = 0.5f;
        public const string SafeAreaName = "Cot_SafeArea";

        /// <summary>视觉基线（§3.1）：标签只用于窗口；路径是唯一真相。</summary>
        public sealed class UiPageBaseline
        {
            public string Label;
            public string Path;
        }

        /// <summary>起页失败步（夹具按步断言）。</summary>
        public enum ScaffoldFailure
        {
            None = 0,
            Validate,
            Copy,
            RootContract,
            Manifest,
            NotImplemented,
        }

        /// <summary>起页结果。</summary>
        public sealed class ScaffoldResult
        {
            public bool Ok;
            public ScaffoldFailure FailedAt = ScaffoldFailure.None;
            public string Error;
            public string PrefabPath;
            public string ManifestPath;
            public string EntryName;
            public readonly List<string> Log = new List<string>();
        }

        /// <summary>基线表（单源）：新增基线只加一行；缺失项由窗口禁用并给原因。</summary>
        public static IReadOnlyList<UiPageBaseline> Baselines { get; } = new List<UiPageBaseline>
        {
            new UiPageBaseline { Label = "全屏基线 A", Path = "Assets/ShareResource/UI/Screens/Tmp_BaselineA.prefab" },
            new UiPageBaseline { Label = "全屏基线 B", Path = "Assets/ShareResource/UI/Screens/Tmp_BaselineB.prefab" },
            new UiPageBaseline { Label = "列表页基线", Path = "Assets/ShareResource/UI/Screens/Tmp_ListScreen.prefab" },
            new UiPageBaseline { Label = "空白骨架", Path = TemplatePath },
        };

        /// <summary>
        /// 起页内核（§3.1）：视觉基线复制为自包含 prefab + 补根契约 + 页面清单草稿；不写 Lua、不写表。
        /// 任一步失败都删除新建 prefab，并把清单恢复为操作前状态（原子失败）。
        /// </summary>
        public static ScaffoldResult Scaffold(string pageName, string baselinePath = null,
            string manifestPath = null, bool openStage = true)
        {
            var result = new ScaffoldResult();
            if (!IsValidPageName(pageName))
                return Fail(result, ScaffoldFailure.Validate, "页面名须为 Page_<PascalCase>（ASCII 字母数字）");

            string manifest = string.IsNullOrEmpty(manifestPath) ? UiPageManifestPaths.DefaultPath
                : manifestPath.Replace('\\', '/').Trim();
            if (!manifest.StartsWith("Assets/", StringComparison.Ordinal) || !manifest.EndsWith(".json", StringComparison.Ordinal))
                return Fail(result, ScaffoldFailure.Validate, "页面清单路径必须是 Assets/ 下的 .json：" + manifest);

            // 空白骨架是编辑器模板：首次使用时补建，再走存在性校验（不能被“模板不存在”误拒）。
            if (!string.IsNullOrEmpty(baselinePath)
                && string.Equals(baselinePath.Replace('\\', '/').Trim(), TemplatePath, StringComparison.Ordinal))
            {
                EnsureTemplate();
            }

            string baseline = ResolveBaseline(baselinePath);
            if (string.IsNullOrEmpty(baseline) ||
                AssetDatabase.LoadAssetAtPath<GameObject>(baseline) == null)
            {
                return Fail(result, ScaffoldFailure.Validate,
                    "视觉基线不存在：" + (baselinePath ?? baseline ?? "（无可用基线）"));
            }

            string prefabPath = UiValidator.ScreensDir + "/" + pageName + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null || File.Exists(FullPath(prefabPath)))
                return Fail(result, ScaffoldFailure.Validate, "目标 prefab 已存在：" + prefabPath);

            UiPageManifest existing;
            try
            {
                existing = UiPageManifestStore.LoadIfExists(manifest);
            }
            catch (Exception exception)
            {
                return Fail(result, ScaffoldFailure.Validate, "页面清单不可读：" + exception.Message);
            }

            UiPageManifestEntry listed = existing?.Pages?.Find(item => item != null &&
                string.Equals(item.Name, pageName, StringComparison.Ordinal));
            if (listed != null && listed.Forms != null && listed.Forms.Count > 0)
                return Fail(result, ScaffoldFailure.Validate, "页面已登记（forms 非空），不能再起页：" + pageName);

            byte[] manifestBefore;
            try
            {
                manifestBefore = File.Exists(FullPath(manifest)) ? File.ReadAllBytes(FullPath(manifest)) : null;
            }
            catch (Exception exception)
            {
                return Fail(result, ScaffoldFailure.Validate, "页面清单不可读：" + exception.Message);
            }

            try
            {
                if (!AssetDatabase.CopyAsset(baseline, prefabPath))
                    return Fail(result, ScaffoldFailure.Copy, "复制视觉基线失败：" + baseline);
                AssetDatabase.ImportAsset(prefabPath);
            }
            catch (Exception exception)
            {
                                return Fail(result, ScaffoldFailure.Copy,
                    "复制视觉基线失败：" + exception.Message + CleanupFailed(prefabPath, manifest, manifestBefore));
            }

            try
            {
                GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    contents.name = pageName;
                    EnsureRootContract(contents);
                    PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
                AssetDatabase.ImportAsset(prefabPath);
            }
            catch (Exception exception)
            {
                                return Fail(result, ScaffoldFailure.RootContract,
                    "补齐页根契约失败：" + exception.Message + CleanupFailed(prefabPath, manifest, manifestBefore));
            }

            try
            {
                var target = existing ?? new UiPageManifest();
                UiPageManifestEntry draft = UiPageManifestEditor.EnsureDraft(
                    target, prefabPath, UiPageManifestSync.DefaultOwner, out string draftError);
                if (draft == null) throw new InvalidOperationException(draftError);
                UiPageManifestStore.Save(target, manifest);

                result.Ok = true;
                result.PrefabPath = prefabPath;
                result.ManifestPath = manifest;
                result.EntryName = draft.Name;
                result.Log.Add("prefab：" + prefabPath);
                result.Log.Add("草稿：" + manifest + "（" + draft.Status + "）");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                if (openStage && PrefabStageUtility.OpenPrefab(prefabPath) == null)
                    result.Log.Add("Prefab Stage 打开失败，请手动打开：" + prefabPath);
                return result;
            }
            catch (Exception exception)
            {
                return Fail(result, ScaffoldFailure.Manifest,
                    exception.Message + CleanupFailed(prefabPath, manifest, manifestBefore));
            }
        }

        private static string ResolveBaseline(string baselinePath)
        {
            if (!string.IsNullOrEmpty(baselinePath)) return baselinePath.Replace('\\', '/').Trim();
            foreach (UiPageBaseline baseline in Baselines)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(baseline.Path) != null) return baseline.Path;
            }
            return null;
        }

        /// <summary>清理失败现场并返回**按实际结果**生成的文案（不得在清理失败时宣称已恢复）。</summary>
        private static string CleanupFailed(string prefabPath, string manifestPath, byte[] manifestBefore)
        {
            bool prefabCleaned;
            try
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                    AssetDatabase.DeleteAsset(prefabPath);
                prefabCleaned = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null && !File.Exists(FullPath(prefabPath));
            }
            catch (Exception exception)
            {
                prefabCleaned = false;
                Debug.LogWarning("[UI 起页] 清理新 prefab 失败：" + prefabPath + "：" + exception.Message);
            }

            bool manifestRestored;
            try
            {
                string full = FullPath(manifestPath);
                if (manifestBefore != null) File.WriteAllBytes(full, manifestBefore);
                else if (File.Exists(full)) File.Delete(full);
                AssetDatabase.Refresh();
                if (File.Exists(full)) AssetDatabase.ImportAsset(manifestPath);
                manifestRestored = manifestBefore == null
                    ? !File.Exists(full)
                    : File.ReadAllBytes(full).SequenceEqual(manifestBefore);
            }
            catch (Exception exception)
            {
                manifestRestored = false;
                Debug.LogWarning("[UI 起页] 恢复清单失败：可重跑 ui.page-manifest：" + exception.Message);
            }

            return "（" + (prefabCleaned ? "无新建 prefab 残留" : "新建 prefab 清理失败，见 Console")
                        + "；" + (manifestRestored ? "无清单残留" : "清单恢复失败，可重跑 ui.page-manifest") + "）";
        }

        private static ScaffoldResult Fail(ScaffoldResult result, ScaffoldFailure step, string error)
        {
            result.Ok = false;
            result.FailedAt = step;
            result.Error = error;
            result.Log.Add("失败：" + error);
            return result;
        }

        private static string FullPath(string assetPath)
            => Path.Combine(UiValidator.ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));

        public static bool IsValidPageName(string pageName)
        {
            if (string.IsNullOrEmpty(pageName)) return false;
            if (!UiBindingRename.TryDecomposeName(pageName, out string prefix, out string semantic)) return false;
            if (prefix != "Page_") return false;
            return UiBindingRename.TryComposeName(prefix, semantic, out string composed, out _)
                   && composed == pageName;
        }

        /// <summary>确保编辑器脚手架模板存在（结构性根，不是运行期视觉资产）。</summary>
        public static void EnsureTemplate()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath) != null) return;
            EnsureFolder(TemplateDir);

            var root = new GameObject("Tmp_Page", typeof(RectTransform));
            try
            {
                EnsureRootContract(root);
                PrefabUtility.SaveAsPrefabAsset(root, TemplatePath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 页根契约（《UI 制作规范》§3.1）：四件套缺则补；安全区优先挂现有拉伸 `Cot_*` 内容容器，
        /// 无容器则新建 `Cot_SafeArea` 并把原有子节点按序移入（内容跟随安全区）。幂等、可 Undo。
        /// </summary>
        public static void EnsureRootContract(GameObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            var canvas = root.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            var scaler = root.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = MatchWidthOrHeight;

            if (root.GetComponent<GraphicRaycaster>() == null) root.AddComponent<GraphicRaycaster>();
            if (root.GetComponent<CanvasGroup>() == null) root.AddComponent<CanvasGroup>();

            if (root.GetComponentInChildren<SafeAreaReceiver>(true) != null) return;

            Transform content = FindStretchContainer(root.transform);
            if (content == null)
            {
                var safe = new GameObject(SafeAreaName, typeof(RectTransform));
                var safeRect = safe.GetComponent<RectTransform>();
                safeRect.SetParent(root.transform, false);
                safeRect.anchorMin = Vector2.zero;
                safeRect.anchorMax = Vector2.one;
                safeRect.offsetMin = Vector2.zero;
                safeRect.offsetMax = Vector2.zero;
                content = safeRect;

                // 无现成内容容器：把原有子节点按序搬入安全区（根与容器同为全屏，布局中性）。
                Transform rootTransform = root.transform;
                var children = new List<Transform>();
                for (int i = 0; i < rootTransform.childCount; i++)
                {
                    Transform child = rootTransform.GetChild(i);
                    if (child != content) children.Add(child);
                }
                foreach (Transform child in children) child.SetParent(content, false);
            }

            content.gameObject.AddComponent<SafeAreaReceiver>();
        }

        private static Transform FindStretchContainer(Transform root)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (!child.name.StartsWith("Cot_", StringComparison.Ordinal)) continue;
                if (!child.TryGetComponent(out RectTransform rect)) continue;
                if (rect.anchorMin != Vector2.zero || rect.anchorMax != Vector2.one) continue;
                return child;
            }

            return null;
        }

        internal static string BuildLuaSkeleton(string pageName)
        {
            return
                $"-- UI/{pageName}.lua：页面骨架（登记生成）\n" +
                "local class = require(\"Core.class\")\n\n" +
                $"local M = class(\"{pageName}\")\n\n" +
                "function M:ctor()\n" +
                $"    self._viewName = \"{pageName}\"\n" +
                "end\n\n" +
                "function M:OnShow(data)\n" +
                "end\n\n" +
                "function M:OnHide()\n" +
                "end\n\n" +
                "return M\n";
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
