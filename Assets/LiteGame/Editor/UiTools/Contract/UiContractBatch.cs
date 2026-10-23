using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.Editor.UiTools.Page;
using LiteGame.Editor.UiTools.Reference;

namespace LiteGame.Editor.UiTools.Contracts
{
    /// <summary>批量生成的一条产物记录。</summary>
    public sealed class UiContractBatchEntry
    {
        public string PageName;
        public string PrefabPath;
        public string ContractPath;
    }

    /// <summary>批量生成结果：成功/跳过/告警/错误分开，避免"全有或全无"的假结论。</summary>
    public sealed class UiContractBatchResult
    {
        public readonly List<UiContractBatchEntry> Written = new List<UiContractBatchEntry>();
        public readonly List<string> Skipped = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();

        public bool Ok => Errors.Count == 0;

        public string Summary =>
            "契约生成：" + Written.Count + " 页 / 跳过 " + Skipped.Count
            + " / 告警 " + Warnings.Count + " / 错误 " + Errors.Count;

        public string ToReport()
        {
            var lines = new List<string> { Summary };
            foreach (UiContractBatchEntry entry in Written)
                lines.Add("  写入 " + entry.PageName + " → " + entry.ContractPath);
            foreach (string item in Skipped) lines.Add("  跳过 " + item);
            foreach (string item in Warnings) lines.Add("  告警 " + item);
            foreach (string item in Errors) lines.Add("  错误 " + item);
            return string.Join("\n", lines);
        }
    }

    /// <summary>单页契约对账结果。</summary>
    public sealed class UiContractDiffEntry
    {
        public string PageName;
        public string ContractPath;
        public UiContractDiffResult Diff;
    }

    /// <summary>批量对账结果：破坏性变更与错误分开计数（破坏性＝失败，其余只是变化）。</summary>
    public sealed class UiContractBatchDiffResult
    {
        public readonly List<UiContractDiffEntry> Compared = new List<UiContractDiffEntry>();
        public readonly List<string> MissingBaseline = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();

        public int BreakingCount => Compared.Count(entry => entry.Diff != null && entry.Diff.HasBreaking);

        public int ChangedCount => Compared.Count(entry => entry.Diff != null && !entry.Diff.IsEmpty);

        public bool Ok => Errors.Count == 0 && BreakingCount == 0;

        public string Summary =>
            "契约对账：" + Compared.Count + " 页 / 变更 " + ChangedCount + "（破坏性 " + BreakingCount + "）"
            + " / 无基线 " + MissingBaseline.Count + " / 错误 " + Errors.Count;

        public string ToReport()
        {
            var lines = new List<string> { Summary };
            foreach (UiContractDiffEntry entry in Compared)
            {
                if (entry.Diff == null || entry.Diff.IsEmpty) continue;
                lines.Add("  " + entry.PageName + "：变更 " + entry.Diff.Changes.Count
                    + (entry.Diff.HasBreaking ? "（含破坏性）" : string.Empty));
                foreach (UiContractChange change in entry.Diff.Changes)
                {
                    lines.Add("    [" + change.Kind + (change.Breaking ? "/破坏" : string.Empty) + "] "
                        + change.Section + " " + change.Key + " " + change.Detail);
                }
            }
            foreach (string item in MissingBaseline) lines.Add("  无基线 " + item);
            foreach (string item in Warnings) lines.Add("  告警 " + item);
            foreach (string item in Errors) lines.Add("  错误 " + item);
            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// 页面契约批处理（《UI 编辑器工具链专项设计》§5/§8.3）：`tbuiform` 只读一次、按 prefab 去重，
    /// 生成/对账两入口。生成器仍是单页真相，本类只做编排——不复制接口面定义、不另造 metadata。
    ///
    /// 口径：
    /// - 根名不在 <c>Page_/Hud_/Tmp_</c> 契约面（若未来出现常驻壳）显式跳过并告警，不计失败；
    /// - 同一 prefab 多行登记取首行，其余行由页面清单 `forms[]` 承担（告警见生成结果）；
    /// - 输出确定性（无时间戳），重复执行零 diff；负例不写盘。
    /// </summary>
    public static class UiContractBatch
    {
        /// <summary>
        /// 单页「生成 + 清单维护字段投影 + Lua 事件接线」全链（批量入口的组成步）：
        /// 表行提供身份（formId/lua/layer/fullScreen），页面清单提供维护字段（owner/designRef），
        /// 事件接线从页面 Lua 源提取后应用进 events[]。清单缺本页条目＝投影留空并告警（不臆造）。
        /// </summary>
        public static UiContractGenerationResult GenerateRegistered(string prefabPath,
            IReadOnlyList<UiPageScaffold.PageRow> rows, UiPageManifest manifest)
        {
            UiContractGenerationResult result = UiContractGenerator.GenerateRegistered(prefabPath, rows);
            if (!result.Ok) return result;
            ApplyEditorialAndEvents(result, FindEntry(manifest, prefabPath));
            return result;
        }

        /// <summary>
        /// 单页全链重生成（**漂移对账的判据单源**）：身份取页面清单条目（forms[0]＝主表单行——清单与表的
        /// 同步由 `ui.page-manifest` 规则另辖），维护字段与事件接线同批量口径。`ui.contract-drift` 规则与
        /// 本方法共用一份「什么算与盘上一致」，不另立重生成语义。
        /// </summary>
        public static UiContractGenerationResult Regenerate(UiPageManifestEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.PrefabPath))
            {
                var bad = new UiContractGenerationResult();
                bad.Errors.Add("清单条目无效（缺 prefab 路径）");
                return bad;
            }
            if (entry.Forms == null || entry.Forms.Count == 0)
            {
                var draft = new UiContractGenerationResult();
                draft.Errors.Add("清单条目无登记行（草稿）——契约对账不辖");
                return draft;
            }
            var primary = entry.Forms[0];
            UiContractGenerationResult result = UiContractGenerator.Generate(entry.PrefabPath, new UiPageMetadata
            {
                FormId = primary.FormId,
                LuaPath = string.IsNullOrEmpty(entry.LuaPath) ? null : entry.LuaPath,
                Layer = primary.Layer,
                FullScreen = primary.FullScreen,
                CacheStrategy = string.IsNullOrEmpty(entry.CacheStrategy)
                    ? UiPageContract.DefaultCacheStrategy
                    : entry.CacheStrategy,
            });
            if (!result.Ok) return result;
            ApplyEditorialAndEvents(result, entry);
            return result;
        }

        /// <summary>投影维护字段并应用事件接线（清单缺条目＝告警不臆造；Lua 源缺失＝events 留空并告警）。</summary>
        private static void ApplyEditorialAndEvents(UiContractGenerationResult result, UiPageManifestEntry entry)
        {
            if (entry == null)
            {
                result.Warnings.Add("页面清单缺本页条目——owner/designRef 不投影（重跑 ui.page-manifest 对账）");
            }
            else
            {
                UiContractGenerator.ApplyManifestEditorial(result.Contract, entry);
            }

            string luaPath = result.Contract.Page.LuaPath;
            if (string.IsNullOrEmpty(luaPath)) return;      // 无 Lua 页（纯表单面）——events 恒空
            string luaFile = UiLuaReferenceScanner.TryResolveLuaFile(luaPath);
            string source = luaFile != null ? UiLuaReferenceScanner.ReadSource(luaFile) : null;
            if (source == null)
            {
                result.Warnings.Add("页面 Lua 源缺失——events[] 留空：" + luaPath);
                return;
            }
            UiContractGenerator.ApplyEvents(result.Contract, UiLuaReferenceScanner.ScanEventWiring(source));
        }

        private static UiPageManifestEntry FindEntry(UiPageManifest manifest, string prefabPath)
        {
            if (manifest?.Pages == null) return null;
            string normalized = (prefabPath ?? string.Empty).Replace('\\', '/').Trim();
            foreach (UiPageManifestEntry page in manifest.Pages)
                if (string.Equals((page.PrefabPath ?? string.Empty).Replace('\\', '/').Trim(), normalized,
                    StringComparison.Ordinal))
                    return page;
            return null;
        }

        private static UiPageManifest TryLoadManifest()
        {
            try
            {
                return UiPageManifestStore.Load();
            }
            catch (Exception)
            {
                return null;       // 批量生成不依赖清单在场——缺清单＝投影/接线不生效并告警
            }
        }

        public static UiContractBatchResult Generate(string outputDirectory = null)
        {
            var result = new UiContractBatchResult();
            if (!UiPageScaffold.TryListPages(out List<UiPageScaffold.PageRow> rows, out string readError))
            {
                result.Errors.Add(readError);
                return result;
            }
            UiPageManifest manifest = TryLoadManifest();
            if (manifest == null) result.Warnings.Add("页面清单缺失——owner/designRef 与 events[] 不投影（重跑 ui.page-manifest）");

            if (!TryResolveDirectory(outputDirectory, out string directory, out string directoryError))
            {
                result.Errors.Add(directoryError);
                return result;
            }

            foreach (KeyValuePair<string, List<UiPageScaffold.PageRow>> group in GroupByPrefab(rows))
            {
                string prefabPath = group.Key;
                string pageName = Path.GetFileNameWithoutExtension(prefabPath);
                if (!UiContractGenerator.IsContractPageName(pageName))
                {
                    result.Skipped.Add(pageName + "（根名不在 Page_/Hud_/Tmp_ 契约面）");
                    result.Warnings.Add(pageName + " 未纳入契约：常驻壳/非页面根不产页面契约");
                    continue;
                }

                UiContractGenerationResult generated = GenerateRegistered(prefabPath, group.Value, manifest);
                if (!generated.Ok)
                {
                    result.Errors.Add(pageName + "：" + generated.ErrorText);
                    continue;
                }
                foreach (string warning in generated.Warnings) result.Warnings.Add(pageName + "：" + warning);

                string contractPath = ContractPathOf(directory, pageName);
                try
                {
                    UiContractStore.Save(contractPath, generated.Contract);
                }
                catch (Exception exception)
                {
                    result.Errors.Add(pageName + " 写盘失败：" + exception.Message);
                    continue;
                }
                result.Written.Add(new UiContractBatchEntry
                {
                    PageName = pageName,
                    PrefabPath = prefabPath,
                    ContractPath = contractPath,
                });
            }
            return result;
        }

        public static UiContractBatchDiffResult Diff(string baselineDirectory = null)
        {
            var result = new UiContractBatchDiffResult();
            if (!UiPageScaffold.TryListPages(out List<UiPageScaffold.PageRow> rows, out string readError))
            {
                result.Errors.Add(readError);
                return result;
            }
            UiPageManifest manifest = TryLoadManifest();

            if (!TryResolveDirectory(baselineDirectory, out string directory, out string directoryError))
            {
                result.Errors.Add(directoryError);
                return result;
            }

            foreach (KeyValuePair<string, List<UiPageScaffold.PageRow>> group in GroupByPrefab(rows))
            {
                string prefabPath = group.Key;
                string pageName = Path.GetFileNameWithoutExtension(prefabPath);
                if (!UiContractGenerator.IsContractPageName(pageName))
                {
                    result.Warnings.Add(pageName + " 未纳入契约：常驻壳/非页面根不产页面契约");
                    continue;
                }

                UiContractGenerationResult current = GenerateRegistered(prefabPath, group.Value, manifest);
                if (!current.Ok)
                {
                    result.Errors.Add(pageName + " 生成失败：" + current.ErrorText);
                    continue;
                }
                foreach (string warning in current.Warnings) result.Warnings.Add(pageName + "：" + warning);

                string contractPath = ContractPathOf(directory, pageName);
                if (!File.Exists(UiContractPaths.FullPath(contractPath)))
                {
                    result.MissingBaseline.Add(pageName + "（" + contractPath + "）");
                    continue;
                }

                try
                {
                    UiPageContract baseline = UiContractStore.Load(contractPath);
                    result.Compared.Add(new UiContractDiffEntry
                    {
                        PageName = pageName,
                        ContractPath = contractPath,
                        Diff = UiContractDiff.Compare(baseline, current.Contract),
                    });
                }
                catch (Exception exception)
                {
                    result.Errors.Add(pageName + " 基线读取失败：" + exception.Message);
                }
            }
            return result;
        }

        private static bool TryResolveDirectory(string directory, out string resolved, out string error)
        {
            resolved = string.IsNullOrEmpty(directory)
                ? UiContractPaths.ContractDirectory
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
        private static List<KeyValuePair<string, List<UiPageScaffold.PageRow>>> GroupByPrefab(
            IReadOnlyList<UiPageScaffold.PageRow> rows)
        {
            var map = new Dictionary<string, List<UiPageScaffold.PageRow>>(StringComparer.Ordinal);
            foreach (UiPageScaffold.PageRow row in rows ?? Array.Empty<UiPageScaffold.PageRow>())
            {
                if (row == null) continue;
                string path = (row.PrefabPath ?? string.Empty).Replace('\\', '/').Trim();
                if (path.Length == 0) continue;
                if (!map.TryGetValue(path, out List<UiPageScaffold.PageRow> list))
                {
                    list = new List<UiPageScaffold.PageRow>();
                    map.Add(path, list);
                }
                list.Add(row);
            }

            return map.Keys
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new KeyValuePair<string, List<UiPageScaffold.PageRow>>(path, map[path]))
                .ToList();
        }
    }
}
