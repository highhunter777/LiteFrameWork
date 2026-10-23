using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.Editor.UiTools.Page;
using LiteGame.Editor.UiTools.Reference;

namespace LiteGame.Editor.UiTools.Contracts
{
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
            IReadOnlyList<UiPageTableRow> rows, UiPageManifest manifest)
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
        public static UiContractGenerationResult Regenerate(UiPageManifestEntry entry, bool allowDraft = false)
        {
            if (entry == null || string.IsNullOrEmpty(entry.PrefabPath))
            {
                var bad = new UiContractGenerationResult();
                bad.Errors.Add("清单条目无效（缺 prefab 路径）");
                return bad;
            }
            if (entry.Forms == null || entry.Forms.Count == 0)
            {
                if (!allowDraft)
                {
                    var draft = new UiContractGenerationResult();
                    draft.Errors.Add("清单条目无登记行（草稿）——契约对账不辖");
                    return draft;
                }

                return GenerateDraft(entry);
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

        /// <summary>
        /// 草稿契约（§3.5/§3.6 登记门槛）：未登记页用 `formId=0` + 草稿层/全屏默认 + 清单维护字段生成；
        /// 登记后由 <see cref="Regenerate"/> 以真实身份覆盖。同一口径供批量/漂移共用。
        /// </summary>
        public static UiContractGenerationResult GenerateDraft(UiPageManifestEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.PrefabPath))
            {
                var bad = new UiContractGenerationResult();
                bad.Errors.Add("草稿条目无效（缺 prefab 路径）");
                return bad;
            }

            UiContractGenerationResult result = UiContractGenerator.Generate(entry.PrefabPath, new UiPageMetadata
            {
                IsDraft = true,
                FormId = 0,
                LuaPath = null,
                Layer = UiContractGenerator.DraftLayer,
                FullScreen = UiContractGenerator.DraftFullScreen,
                CacheStrategy = UiPageContract.IsValidCacheStrategy(entry.CacheStrategy)
                    ? entry.CacheStrategy
                    : UiPageContract.DefaultCacheStrategy,
                Owner = entry.Owner,
                DesignRef = entry.DesignRef,
            });
            if (!result.Ok) return result;
            UiContractGenerator.ApplyManifestEditorial(result.Contract, entry);
            return result;
        }

        /// <summary>清单中的草稿页（forms 空、status Draft、prefab 存在、不重复已登记页）。</summary>
        private static IEnumerable<UiPageManifestEntry> DraftEntries(UiPageManifest manifest,
            ICollection<string> registered)
        {
            if (manifest?.Pages == null) yield break;
            foreach (UiPageManifestEntry entry in manifest.Pages
                         .Where(page => page != null)
                         .OrderBy(page => page.Name, StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(entry.Name) || registered.Contains(entry.Name)) continue;
                if (entry.Forms != null && entry.Forms.Count > 0) continue;
                if (!UiPageStatusRules.TryParse(entry.Status, out UiPageStatus status)
                    || status != UiPageStatus.Draft) continue;
                if (!UiContractGenerator.IsContractPageName(entry.Name)) continue;
                if (!File.Exists(UiContractPaths.FullPath(entry.PrefabPath))) continue;
                yield return entry;
            }
        }

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

        public static UiContractBatchResult Generate(string outputDirectory = null, UiPageManifest manifest = null)
        {
            var result = new UiContractBatchResult();
            if (!UiPageTableHelper.TryListPages(out List<UiPageTableRow> rows, out string readError))
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
            foreach (KeyValuePair<string, List<UiPageTableRow>> group in GroupByPrefab(rows))
            {
                string prefabPath = group.Key;
                string pageName = Path.GetFileNameWithoutExtension(prefabPath);
                registered.Add(pageName);
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

            foreach (UiPageManifestEntry entry in DraftEntries(manifest, registered))
            {
                UiContractGenerationResult generated = GenerateDraft(entry);
                if (!generated.Ok)
                {
                    result.Errors.Add(entry.Name + "：" + generated.ErrorText);
                    continue;
                }
                foreach (string warning in generated.Warnings) result.Warnings.Add(entry.Name + "：" + warning);
                string contractPath = ContractPathOf(directory, entry.Name);
                try
                {
                    UiContractStore.Save(contractPath, generated.Contract);
                }
                catch (Exception exception)
                {
                    result.Errors.Add(entry.Name + " 写盘失败：" + exception.Message);
                    continue;
                }
                result.Written.Add(new UiContractBatchEntry
                {
                    PageName = entry.Name,
                    PrefabPath = entry.PrefabPath,
                    ContractPath = contractPath,
                    Draft = true,
                });
            }
            return result;
        }

        public static UiContractBatchDiffResult Diff(string baselineDirectory = null, UiPageManifest manifest = null)
        {
            var result = new UiContractBatchDiffResult();
            if (!UiPageTableHelper.TryListPages(out List<UiPageTableRow> rows, out string readError))
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
            foreach (KeyValuePair<string, List<UiPageTableRow>> group in GroupByPrefab(rows))
            {
                string prefabPath = group.Key;
                string pageName = Path.GetFileNameWithoutExtension(prefabPath);
                registered.Add(pageName);
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

            foreach (UiPageManifestEntry entry in DraftEntries(manifest, registered))
            {
                UiContractGenerationResult current = GenerateDraft(entry);
                if (!current.Ok)
                {
                    result.Errors.Add(entry.Name + " 生成失败：" + current.ErrorText);
                    continue;
                }
                string contractPath = ContractPathOf(directory, entry.Name);
                if (!File.Exists(UiContractPaths.FullPath(contractPath)))
                {
                    result.MissingBaseline.Add(entry.Name + "（" + contractPath + "）");
                    continue;
                }
                try
                {
                    UiPageContract baseline = UiContractStore.Load(contractPath);
                    result.Compared.Add(new UiContractDiffEntry
                    {
                        PageName = entry.Name,
                        ContractPath = contractPath,
                        Diff = UiContractDiff.Compare(baseline, current.Contract),
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
        private static List<KeyValuePair<string, List<UiPageTableRow>>> GroupByPrefab(
            IReadOnlyList<UiPageTableRow> rows)
        {
            var map = new Dictionary<string, List<UiPageTableRow>>(StringComparer.Ordinal);
            foreach (UiPageTableRow row in rows ?? Array.Empty<UiPageTableRow>())
            {
                if (row == null) continue;
                string path = (row.PrefabPath ?? string.Empty).Replace('\\', '/').Trim();
                if (path.Length == 0) continue;
                if (!map.TryGetValue(path, out List<UiPageTableRow> list))
                {
                    list = new List<UiPageTableRow>();
                    map.Add(path, list);
                }
                list.Add(row);
            }

            return map.Keys
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new KeyValuePair<string, List<UiPageTableRow>>(path, map[path]))
                .ToList();
        }
    }
}
