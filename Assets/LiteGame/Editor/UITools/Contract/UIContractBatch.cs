using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.Editor.UITools.Page;
using LiteGame.Editor.UITools.Reference;

namespace LiteGame.Editor.UITools.Contracts
{
    /// <summary>
    /// 页面契约批处理（《UI 编辑器工具链专项设计》§5/§8.3）：`tbuiform` 只读一次、按 prefab 去重， 生成/对账两入口。
    /// </summary>
    public static class UIContractBatch
    {
        /// <summary>
        /// 单页「生成 + 清单维护字段投影 + 事件接线」全链（批量入口的组成步）： 表行提供身份（formId/layer/fullScreen），页面清单提供维护字段（owner/designRef）。
        /// </summary>
        public static UIContractGenerationResult GenerateRegistered(string prefabPath,
            IReadOnlyList<UIPageTableRow> rows, UIPageManifest manifest)
        {
            UIContractGenerationResult result = UIContractGenerator.GenerateRegistered(prefabPath, rows);
            if (!result.Ok) return result;
            ApplyEditorialAndEvents(result, FindEntry(manifest, prefabPath));
            return result;
        }

        /// <summary>
        /// 单页全链重生成（**漂移对账的判据单源**）：身份取页面清单条目（forms[0]＝主表单行——清单与表的 同步由 `ui.page-manifest` 规则另辖），维护字段与事件接线同批量口径。
        /// </summary>
        public static UIContractGenerationResult Regenerate(UIPageManifestEntry entry, bool allowDraft = false)
        {
            if (entry == null || string.IsNullOrEmpty(entry.PrefabPath))
            {
                var bad = new UIContractGenerationResult();
                bad.Errors.Add("清单条目无效（缺 prefab 路径）");
                return bad;
            }
            if (entry.Forms == null || entry.Forms.Count == 0)
            {
                if (!allowDraft)
                {
                    var draft = new UIContractGenerationResult();
                    draft.Errors.Add("清单条目无登记行（草稿）——契约对账不辖");
                    return draft;
                }

                return GenerateDraft(entry);
            }
            var primary = entry.Forms[0];
            UIContractGenerationResult result = UIContractGenerator.Generate(entry.PrefabPath, new UIPageMetadata
            {
                FormId = primary.FormId,
                Layer = primary.Layer,
                FullScreen = primary.FullScreen,
                CacheStrategy = string.IsNullOrEmpty(entry.CacheStrategy)
                    ? UIPageContract.DefaultCacheStrategy
                    : entry.CacheStrategy,
            });
            if (!result.Ok) return result;
            ApplyEditorialAndEvents(result, entry);
            return result;
        }

        /// <summary>
        /// 草稿契约（§3.5/§3.6 登记门槛）：未登记页用 `formId=0` + 草稿层/全屏默认 + 清单维护字段生成；
        /// 登记后由 <see cref="Regenerate"/> 以真实身份覆盖。同一口径供批量/漂移共用。
        /// </summary>
        public static UIContractGenerationResult GenerateDraft(UIPageManifestEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.PrefabPath))
            {
                var bad = new UIContractGenerationResult();
                bad.Errors.Add("草稿条目无效（缺 prefab 路径）");
                return bad;
            }

            UIContractGenerationResult result = UIContractGenerator.Generate(entry.PrefabPath, new UIPageMetadata
            {
                IsDraft = true,
                FormId = 0,
                Layer = UIContractGenerator.DraftLayer,
                FullScreen = UIContractGenerator.DraftFullScreen,
                CacheStrategy = UIPageContract.IsValidCacheStrategy(entry.CacheStrategy)
                    ? entry.CacheStrategy
                    : UIPageContract.DefaultCacheStrategy,
                Owner = entry.Owner,
                DesignRef = entry.DesignRef,
            });
            if (!result.Ok) return result;
            UIContractGenerator.ApplyManifestEditorial(result.Contract, entry);
            return result;
        }

        /// <summary>清单中的草稿页（forms 空、status Draft、prefab 存在、不重复已登记页）。</summary>
        private static IEnumerable<UIPageManifestEntry> DraftEntries(UIPageManifest manifest,
            ICollection<string> registered)
        {
            if (manifest?.Pages == null) yield break;
            foreach (UIPageManifestEntry entry in manifest.Pages
                         .Where(page => page != null)
                         .OrderBy(page => page.Name, StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(entry.Name) || registered.Contains(entry.Name)) continue;
                if (entry.Forms != null && entry.Forms.Count > 0) continue;
                if (!UIPageStatusRules.TryParse(entry.Status, out UIPageStatus status)
                    || status != UIPageStatus.Draft) continue;
                if (!UIContractGenerator.IsContractPageName(entry.Name)) continue;
                if (!File.Exists(UIContractPaths.FullPath(entry.PrefabPath))) continue;
                yield return entry;
            }
        }

        private static void ApplyEditorialAndEvents(UIContractGenerationResult result, UIPageManifestEntry entry)
        {
            if (entry == null)
            {
                result.Warnings.Add("页面清单缺本页条目——owner/designRef 不投影（重跑 ui.page-manifest 对账）");
            }
            else
            {
                UIContractGenerator.ApplyManifestEditorial(result.Contract, entry);
            }

            // 事件接线：C# 页源（解析单源＝UIPageLogicScanner）。
            string sourceFile = UIPageLogicScanner.ResolveFile(result.Contract.Page.Name);
            string source = UIPageLogicScanner.ReadSource(sourceFile);
            if (source == null)
            {
                result.Warnings.Add("页面逻辑源缺失——events[] 留空：" + (sourceFile ?? result.Contract.Page.Name));
                return;
            }
            UIContractGenerator.ApplyEvents(result.Contract, UIPageLogicScanner.ScanEventWiring(source));
        }

        private static UIPageManifestEntry FindEntry(UIPageManifest manifest, string prefabPath)
        {
            if (manifest?.Pages == null) return null;
            string normalized = (prefabPath ?? string.Empty).Replace('\\', '/').Trim();
            foreach (UIPageManifestEntry page in manifest.Pages)
                if (string.Equals((page.PrefabPath ?? string.Empty).Replace('\\', '/').Trim(), normalized,
                    StringComparison.Ordinal))
                    return page;
            return null;
        }

        private static UIPageManifest TryLoadManifest()
        {
            try
            {
                return UIPageManifestStore.Load();
            }
            catch (Exception)
            {
                return null;       // 批量生成不依赖清单在场——缺清单＝投影/接线不生效并告警
            }
        }

        public static UIContractBatchResult Generate(string outputDirectory = null, UIPageManifest manifest = null)
        {
            var result = new UIContractBatchResult();
            if (!UIPageTableHelper.TryListPages(out List<UIPageTableRow> rows, out string readError))
            {
                result.Errors.Add(readError);
                return result;
            }
            manifest = manifest ?? TryLoadManifest();
            if (manifest == null)
                result.Warnings.Add("页面清单缺失——草稿契约未纳入，owner/designRef 与 events[] 不投影（重跑 ui.page-manifest）");

            if (!TryResolveDirectory(outputDirectory, out string directory, out string directoryError))
            {
                result.Errors.Add(directoryError);
                return result;
            }

            var registered = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<UIPageTableRow>> group in GroupByPrefab(rows))
            {
                string prefabPath = group.Key;
                string pageName = Path.GetFileNameWithoutExtension(prefabPath);
                registered.Add(pageName);
                if (!UIContractGenerator.IsContractPageName(pageName))
                {
                    result.Skipped.Add(pageName + "（根名不在 Page_/Hud_/Tmp_ 契约面）");
                    result.Warnings.Add(pageName + " 未纳入契约：常驻壳/非页面根不产页面契约");
                    continue;
                }

                UIContractGenerationResult generated = GenerateRegistered(prefabPath, group.Value, manifest);
                if (!generated.Ok)
                {
                    result.Errors.Add(pageName + "：" + generated.ErrorText);
                    continue;
                }
                foreach (string warning in generated.Warnings) result.Warnings.Add(pageName + "：" + warning);

                string contractPath = ContractPathOf(directory, pageName);
                try
                {
                    UIContractStore.Save(contractPath, generated.Contract);
                }
                catch (Exception exception)
                {
                    result.Errors.Add(pageName + " 写盘失败：" + exception.Message);
                    continue;
                }
                result.Written.Add(new UIContractBatchEntry
                {
                    PageName = pageName,
                    PrefabPath = prefabPath,
                    ContractPath = contractPath,
                });
            }

            foreach (UIPageManifestEntry entry in DraftEntries(manifest, registered))
            {
                UIContractGenerationResult generated = GenerateDraft(entry);
                if (!generated.Ok)
                {
                    result.Errors.Add(entry.Name + "：" + generated.ErrorText);
                    continue;
                }
                foreach (string warning in generated.Warnings) result.Warnings.Add(entry.Name + "：" + warning);
                string contractPath = ContractPathOf(directory, entry.Name);
                try
                {
                    UIContractStore.Save(contractPath, generated.Contract);
                }
                catch (Exception exception)
                {
                    result.Errors.Add(entry.Name + " 写盘失败：" + exception.Message);
                    continue;
                }
                result.Written.Add(new UIContractBatchEntry
                {
                    PageName = entry.Name,
                    PrefabPath = entry.PrefabPath,
                    ContractPath = contractPath,
                    Draft = true,
                });
            }
            return result;
        }

        public static UIContractBatchDiffResult Diff(string baselineDirectory = null, UIPageManifest manifest = null)
        {
            var result = new UIContractBatchDiffResult();
            if (!UIPageTableHelper.TryListPages(out List<UIPageTableRow> rows, out string readError))
            {
                result.Errors.Add(readError);
                return result;
            }
            manifest = manifest ?? TryLoadManifest();

            if (!TryResolveDirectory(baselineDirectory, out string directory, out string directoryError))
            {
                result.Errors.Add(directoryError);
                return result;
            }

            var registered = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<UIPageTableRow>> group in GroupByPrefab(rows))
            {
                string prefabPath = group.Key;
                string pageName = Path.GetFileNameWithoutExtension(prefabPath);
                registered.Add(pageName);
                if (!UIContractGenerator.IsContractPageName(pageName))
                {
                    result.Warnings.Add(pageName + " 未纳入契约：常驻壳/非页面根不产页面契约");
                    continue;
                }

                UIContractGenerationResult current = GenerateRegistered(prefabPath, group.Value, manifest);
                if (!current.Ok)
                {
                    result.Errors.Add(pageName + " 生成失败：" + current.ErrorText);
                    continue;
                }
                foreach (string warning in current.Warnings) result.Warnings.Add(pageName + "：" + warning);

                string contractPath = ContractPathOf(directory, pageName);
                if (!File.Exists(UIContractPaths.FullPath(contractPath)))
                {
                    result.MissingBaseline.Add(pageName + "（" + contractPath + "）");
                    continue;
                }

                try
                {
                    UIPageContract baseline = UIContractStore.Load(contractPath);
                    result.Compared.Add(new UIContractDiffEntry
                    {
                        PageName = pageName,
                        ContractPath = contractPath,
                        Diff = UIContractDiff.Compare(baseline, current.Contract),
                    });
                }
                catch (Exception exception)
                {
                    result.Errors.Add(pageName + " 基线读取失败：" + exception.Message);
                }
            }

            foreach (UIPageManifestEntry entry in DraftEntries(manifest, registered))
            {
                UIContractGenerationResult current = GenerateDraft(entry);
                if (!current.Ok)
                {
                    result.Errors.Add(entry.Name + " 生成失败：" + current.ErrorText);
                    continue;
                }
                string contractPath = ContractPathOf(directory, entry.Name);
                if (!File.Exists(UIContractPaths.FullPath(contractPath)))
                {
                    result.MissingBaseline.Add(entry.Name + "（" + contractPath + "）");
                    continue;
                }
                try
                {
                    UIPageContract baseline = UIContractStore.Load(contractPath);
                    result.Compared.Add(new UIContractDiffEntry
                    {
                        PageName = entry.Name,
                        ContractPath = contractPath,
                        Diff = UIContractDiff.Compare(baseline, current.Contract),
                    });
                }
                catch (Exception exception)
                {
                    result.Errors.Add(entry.Name + " 基线读取失败：" + exception.Message);
                }
            }
            return result;
        }

        private static bool TryResolveDirectory(string directory, out string resolved, out string error)
        {
            resolved = string.IsNullOrEmpty(directory)
                ? UIContractPaths.ContractDirectory
                : directory.Replace('\\', '/').TrimEnd('/');
            error = null;
            if (!resolved.StartsWith("Assets/", StringComparison.Ordinal))
            {
                error = "契约输出目录必须在 Assets/ 下：" + resolved;
                resolved = null;
                return false;
            }
            return true;
        }

        private static string ContractPathOf(string directory, string pageName)
        {
            return directory + "/" + pageName + ".contract.json";
        }

        /// <summary>按 prefab 去重（保持确定性：键序按路径排序），同页多行归一组。</summary>
        private static List<KeyValuePair<string, List<UIPageTableRow>>> GroupByPrefab(
            IReadOnlyList<UIPageTableRow> rows)
        {
            var map = new Dictionary<string, List<UIPageTableRow>>(StringComparer.Ordinal);
            foreach (UIPageTableRow row in rows ?? Array.Empty<UIPageTableRow>())
            {
                if (row == null) continue;
                string path = (row.PrefabPath ?? string.Empty).Replace('\\', '/').Trim();
                if (path.Length == 0) continue;
                if (!map.TryGetValue(path, out List<UIPageTableRow> list))
                {
                    list = new List<UIPageTableRow>();
                    map.Add(path, list);
                }
                list.Add(row);
            }

            return map.Keys
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new KeyValuePair<string, List<UIPageTableRow>>(path, map[path]))
                .ToList();
        }
    }
}
