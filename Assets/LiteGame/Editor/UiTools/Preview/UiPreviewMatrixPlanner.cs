using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame;
using LiteGame.Editor.UiTools.Validation;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Preview
{
    /// <summary>矩阵计划（页/产物目录/组合；只含已校验输入）。</summary>
    internal sealed class UiPreviewMatrixPlan
    {
        public string PagePath;
        public string PageName;
        public string OutputDir;
        public List<UiDeviceProfile> Profiles = new List<UiDeviceProfile>();
        public List<UiPreviewCombo> Combos = new List<UiPreviewCombo>();
    }

    /// <summary>矩阵输入解析与校验（页/设备档/语言/字体档/产物目录；纯编辑器侧，不碰 Play）。</summary>
    internal static class UiPreviewMatrixPlanner
    {
        public static bool TryCreatePlan(UiPreviewMatrixOptions options,
            out UiPreviewMatrixPlan plan, out string error)
        {
            plan = null;
            error = null;
            if (options == null)
            {
                error = "矩阵参数为空";
                return false;
            }
            if (!TryResolvePage(options.PagePath, out string pagePath, out string pageName, out error))
                return false;

            UiDeviceProfileDocument document;
            try
            {
                document = UiDeviceProfileCatalog.Load();
            }
            catch (Exception exception)
            {
                error = "设备档案装载失败：" + exception.Message;
                return false;
            }
            IReadOnlyList<string> issues = UiDeviceProfileCatalog.Validate(document);
            if (issues.Count > 0)
            {
                error = "设备档案非法：" + string.Join("；", issues);
                return false;
            }

            List<UiDeviceProfile> profiles = ResolveProfiles(document, options.ProfileIds, out error);
            if (profiles == null) return false;
            List<string> locales = ResolveLocales(options.Locales, out error);
            if (locales == null) return false;
            List<float> scales = ResolveScales(options.FontScales, out error);
            if (scales == null) return false;

            var filtered = new UiDeviceProfileDocument
            {
                SchemaVersion = document.SchemaVersion,
                Profiles = profiles,
            };
            List<UiPreviewCombo> combos = UiPreviewMatrix.Expand(filtered, locales, scales);
            if (combos.Count == 0)
            {
                error = "组合为空（检查设备档/语言/字体档选择）";
                return false;
            }
            if (!TryResolveOutputDir(options.OutputDir, pageName, out string outputDir, out error))
                return false;

            plan = new UiPreviewMatrixPlan
            {
                PagePath = pagePath,
                PageName = pageName,
                OutputDir = outputDir,
                Profiles = profiles,
                Combos = combos,
            };
            return true;
        }

        private static bool TryResolvePage(string input, out string pagePath, out string pageName, out string error)
        {
            pagePath = null;
            pageName = null;
            error = null;
            if (string.IsNullOrEmpty(input))
            {
                error = "目标页为空（传 prefab 路径或页面名）";
                return false;
            }
            if (input.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                pagePath = input;
            }
            else
            {
                string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { UiValidator.ScreensDir });
                List<string> hits = guids
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(path => string.Equals(Path.GetFileNameWithoutExtension(path), input,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (hits.Count != 1)
                {
                    error = hits.Count == 0 ? "找不到页面 prefab：" + input : "页面名不唯一：" + input;
                    return false;
                }
                pagePath = hits[0];
            }
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pagePath);
            if (prefab == null)
            {
                error = "页面 prefab 不存在：" + pagePath;
                return false;
            }
            pageName = Path.GetFileNameWithoutExtension(pagePath);
            return true;
        }

        private static List<UiDeviceProfile> ResolveProfiles(UiDeviceProfileDocument document,
            IReadOnlyList<string> requested, out string error)
        {
            error = null;
            IReadOnlyList<UiDeviceProfile> matrix = UiDeviceProfileCatalog.MatrixProfiles(document);
            if (requested == null || requested.Count == 0) return matrix.ToList();

            var profiles = new List<UiDeviceProfile>();
            foreach (string id in requested.Distinct(StringComparer.Ordinal))
            {
                UiDeviceProfile profile = matrix.FirstOrDefault(item =>
                    string.Equals(item.Id, id, StringComparison.Ordinal));
                if (profile == null)
                {
                    error = "设备档不在入矩阵集：" + id;
                    return null;
                }
                profiles.Add(profile);
            }
            return profiles;
        }

        private static List<string> ResolveLocales(IReadOnlyList<string> requested, out string error)
        {
            error = null;
            List<string> values = (requested == null || requested.Count == 0
                    ? UiPreviewMatrix.DefaultLocales.ToList()
                    : requested.ToList())
                .Distinct(StringComparer.Ordinal).ToList();
            foreach (string value in values)
            {
                if (!UiPreviewLocale.SupportedLocales.Contains(value))
                {
                    error = "不支持的语言（" + string.Join("|", UiPreviewLocale.SupportedLocales) + "）：" + value;
                    return null;
                }
            }
            return values;
        }

        private static List<float> ResolveScales(IReadOnlyList<float> requested, out string error)
        {
            error = null;
            List<float> values = (requested == null || requested.Count == 0
                    ? UiPreviewMatrix.DefaultFontScales.ToList()
                    : requested.ToList())
                .Distinct().ToList();
            if (values.Any(scale => scale <= 0f))
            {
                error = "字体缩放档必须为正";
                return null;
            }
            return values;
        }

        private static bool TryResolveOutputDir(string requested, string pageName,
            out string outputDir, out string error)
        {
            error = null;
            outputDir = string.IsNullOrEmpty(requested)
                ? "Artifacts/PreviewMatrix/" + pageName
                : requested.Replace('\\', '/');
            if (outputDir.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                || Path.IsPathRooted(outputDir)
                || !outputDir.StartsWith("Artifacts/", StringComparison.OrdinalIgnoreCase))
            {
                error = "产物目录必须在 Artifacts/ 下（相对路径）：" + outputDir;
                return false;
            }
            if (outputDir.Split('/').Any(segment => segment == ".." || segment == "."))
            {
                error = "产物目录不能包含 . / .. 段：" + outputDir;
                return false;
            }
            return true;
        }
    }
}
