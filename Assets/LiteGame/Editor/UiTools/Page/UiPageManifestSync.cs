using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.Editor.UiTools.Contracts;

namespace LiteGame.Editor.UiTools.Page
{
    /// <summary>对账结果：成功时带 <see cref="Manifest"/>；失败时 <see cref="Errors"/> 逐条可读、不写盘。</summary>
    public sealed class UiPageManifestSyncResult
    {
        public UiPageManifest Manifest;
        public readonly List<string> Errors = new List<string>();

        public bool Ok => Errors.Count == 0;
        public string ErrorText => string.Join("\n", Errors);

        /// <summary>非阻断告警（单一来源＝<see cref="UiPageManifest.Warnings"/>）。</summary>
        public List<string> Warnings => Manifest?.Warnings;
    }

    /// <summary>
    /// 页面清单对账（《UI 编辑器工具链专项设计》§8.5）：`tbuiform` 行 + 已有清单 + 契约存在性 → 清单。
    ///
    /// - 条目按**页面（prefab）**唯一，`forms[]` 承载该 prefab 的全部登记行（多行合法，如 Tmp_Dialog 201/206）；
    /// - 身份字段（forms/luaPath/prefabPath/契约路径）以表为准刷新；
    /// - 维护字段（Owner/状态/设计稿/设备档/备注）**保留**；
    /// - 表行新增 → 新条目（`Registered` + 默认 Owner）；清单有而表无：`Draft` 保留、其余告警；
    /// - 表读失败不伪造空表（显式错误、不写盘）；负例不写盘。
    /// </summary>
    public static class UiPageManifestSync
    {
        /// <summary>默认 Owner（RACI：页面视觉归 UI 美术）。</summary>
        public const string DefaultOwner = "art.ui";

        /// <summary>读真实 `tbuiform` + 默认清单路径对账。</summary>
        public static UiPageManifestSyncResult Sync() => Sync(null, null);

        /// <summary>按显式行与已有清单对账（测试/工具用；rows=null 表示读真实表）。</summary>
        public static UiPageManifestSyncResult Sync(IReadOnlyList<UiPageScaffold.PageRow> rows, UiPageManifest existing)
        {
            var result = new UiPageManifestSyncResult { Manifest = new UiPageManifest() };

            bool readTable = rows == null;
            if (readTable)
            {
                if (!UiPageScaffold.TryListPages(out List<UiPageScaffold.PageRow> tableRows, out string readError))
                {
                    result.Errors.Add(readError);
                    return result;
                }
                rows = tableRows;
                if (existing == null)
                {
                    try
                    {
                        existing = UiPageManifestStore.LoadIfExists();
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add("页面清单不可读：" + ex.Message);
                        return result;
                    }
                }
            }

            var groups = new Dictionary<string, List<UiPageScaffold.PageRow>>(StringComparer.Ordinal);
            var knownFormIds = new HashSet<int>();
            foreach (UiPageScaffold.PageRow row in rows)
            {
                if (row == null) continue;
                string prefabPath = Normalize(row.PrefabPath);
                string name = Path.GetFileNameWithoutExtension(prefabPath);
                if (string.IsNullOrEmpty(name))
                {
                    result.Errors.Add($"表行缺少 prefab（id={row.id}）");
                    continue;
                }
                if (!knownFormIds.Add(row.id))
                {
                    result.Errors.Add($"重复 formId {row.id}（{name}）");
                    continue;
                }
                if (!groups.TryGetValue(name, out List<UiPageScaffold.PageRow> group))
                    groups[name] = group = new List<UiPageScaffold.PageRow>();
                group.Add(row);
            }

            var pages = new List<UiPageManifestEntry>();
            var byName = new HashSet<string>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, List<UiPageScaffold.PageRow>> pair in
                     groups.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                string name = pair.Key;
                List<UiPageScaffold.PageRow> groupRows = pair.Value;
                string prefabPath = Normalize(groupRows[0].PrefabPath);

                UiPageManifestEntry entry = FindExisting(existing, name, groupRows)
                    ?? new UiPageManifestEntry
                    {
                        Name = name,
                        Owner = DefaultOwner,
                        Status = nameof(UiPageStatus.Registered),
                    };

                entry.Name = name;
                entry.PrefabPath = prefabPath;
                entry.Forms = groupRows
                    .OrderBy(row => row.id)
                    .Select(row => new UiPageFormEntry
                    {
                        FormId = row.id,
                        Layer = row.layer,
                        FullScreen = row.fullScreen,
                    })
                    .ToList();
                entry.CacheStrategy = UiPageContract.DefaultCacheStrategy;
                entry.ContractPath = UiContractPaths.ForPage(name);
                entry.ContractPresent = File.Exists(UiPageManifestPaths.FullPath(entry.ContractPath));

                List<string> luaPaths = groupRows
                    .Select(row => (row.lua ?? string.Empty).Trim())
                    .Where(lua => lua.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(lua => lua, StringComparer.Ordinal)
                    .ToList();
                if (luaPaths.Count > 1)
                {
                    entry.LuaPath = null;
                    result.Manifest.Warnings.Add($"{name}：多行 luaPath 不一致（{string.Join(",", luaPaths)}）");
                }
                else
                {
                    entry.LuaPath = luaPaths.Count == 1 ? luaPaths[0] : null;
                }

                if (string.IsNullOrWhiteSpace(entry.Owner)) entry.Owner = DefaultOwner;
                if (!UiPageStatusRules.TryParse(entry.Status, out UiPageStatus parsed))
                    result.Errors.Add($"{name}：状态非法 {entry.Status}");
                else
                    entry.Status = parsed.ToString();
                if (!File.Exists(UiPageManifestPaths.FullPath(prefabPath)))
                    result.Manifest.Warnings.Add($"{name}：prefab 不存在 {prefabPath}");

                byName.Add(name);
                pages.Add(entry);
            }

            IReadOnlyList<UiPageManifestEntry> oldPages = existing?.Pages;
            if (oldPages == null) oldPages = Array.Empty<UiPageManifestEntry>();
            foreach (UiPageManifestEntry old in oldPages)
            {
                if (old == null || string.IsNullOrEmpty(old.Name)) continue;
                if (byName.Contains(old.Name)) continue;
                if (old.Forms != null && old.Forms.Any(form => knownFormIds.Contains(form.FormId))) continue;

                if (!UiPageStatusRules.TryParse(old.Status, out UiPageStatus oldStatus))
                {
                    result.Errors.Add($"{old.Name}：状态非法 {old.Status}");
                    continue;
                }
                if (oldStatus != UiPageStatus.Draft)
                    result.Manifest.Warnings.Add($"{old.Name}：清单有但 tbuiform 无对应行（状态 {old.Status}）");

                byName.Add(old.Name);
                pages.Add(old);
            }

            pages.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            result.Manifest.Pages = pages;
            result.Manifest.Warnings.Sort(StringComparer.Ordinal);
            result.Manifest.SourceHash = UiPageManifestHash.Compute(result.Manifest);
            return result;
        }

        /// <summary>对账并写盘；负例只返回错误、不落任何文件。</summary>
        public static UiPageManifestSyncResult SyncToDisk(string outputPath = null) => SyncToDisk(null, null, outputPath);

        /// <summary>按显式行/清单对账并写盘（测试用）。</summary>
        public static UiPageManifestSyncResult SyncToDisk(IReadOnlyList<UiPageScaffold.PageRow> rows,
            UiPageManifest existing, string outputPath = null)
        {
            UiPageManifestSyncResult result = Sync(rows, existing);
            if (!result.Ok) return result;

            string path = string.IsNullOrEmpty(outputPath)
                ? UiPageManifestPaths.DefaultPath
                : outputPath.Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                result.Errors.Add("页面清单输出路径必须在 Assets/ 下：" + path);
                result.Manifest = null;
                return result;
            }
            try
            {
                UiPageManifestStore.Save(result.Manifest, path);
            }
            catch (Exception ex)
            {
                result.Errors.Add("页面清单写盘失败：" + ex.Message);
                result.Manifest = null;
            }
            return result;
        }

        /// <summary>编辑后重算内容指纹（工具 API 调用）。</summary>
        public static void RefreshSourceHash(UiPageManifest manifest)
        {
            if (manifest != null) manifest.SourceHash = UiPageManifestHash.Compute(manifest);
        }

        private static UiPageManifestEntry FindExisting(UiPageManifest existing, string name,
            IReadOnlyList<UiPageScaffold.PageRow> rows)
        {
            if (existing?.Pages == null) return null;
            foreach (UiPageManifestEntry entry in existing.Pages)
                if (entry != null && string.Equals(entry.Name, name, StringComparison.Ordinal)) return entry;
            foreach (UiPageManifestEntry entry in existing.Pages)
            {
                if (entry?.Forms == null) continue;
                foreach (UiPageFormEntry form in entry.Forms)
                    if (rows.Any(row => row.id == form.FormId)) return entry;
            }
            return null;
        }

        private static string Normalize(string path) => (path ?? string.Empty).Replace('\\', '/').Trim();
    }
}
