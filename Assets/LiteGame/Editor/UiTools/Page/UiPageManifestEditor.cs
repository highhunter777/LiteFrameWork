using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.Editor.UiTools.Contracts;

namespace LiteGame.Editor.UiTools.Page
{
    /// <summary>
    /// 页面清单的编辑 API（《UI 编辑器工具链专项设计》§3.1/§3.6/§8.5）：起页草稿、状态流转、
    /// 维护字段更新。**纯内存操作**——调用方显式保存（`UiPageManifestStore.Save` 或 `SyncToDisk`）；
    /// 每次成功编辑后重算 `sourceHash`。
    /// </summary>
    public static class UiPageManifestEditor
    {
        /// <summary>创建/取回草稿条目（幂等）：未登记页（无 formId）、状态 `Draft`、Owner 默认美术。</summary>
        public static UiPageManifestEntry EnsureDraft(UiPageManifest manifest, string prefabPath, string owner,
            out string error)
        {
            error = null;
            if (manifest == null)
            {
                error = "清单为空";
                return null;
            }

            string path = (prefabPath ?? string.Empty).Replace('\\', '/').Trim();
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".prefab", StringComparison.Ordinal))
            {
                error = "prefab 路径必须是 Assets/ 下的 .prefab：" + path;
                return null;
            }
            string name = Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith("Page_", StringComparison.Ordinal))
            {
                error = "页面名须 Page_<Semantic>：" + name;
                return null;
            }
            if (!File.Exists(UiPageManifestPaths.FullPath(path)))
            {
                error = "prefab 不存在：" + path;
                return null;
            }

            manifest.Pages ??= new List<UiPageManifestEntry>();
            UiPageManifestEntry existing = manifest.Pages.FirstOrDefault(
                entry => entry != null && string.Equals(entry.Name, name, StringComparison.Ordinal));
            if (existing != null)
            {
                if (string.IsNullOrWhiteSpace(existing.Owner)) existing.Owner = NormalizeOwner(owner);
                UiPageManifestSync.RefreshSourceHash(manifest);
                return existing;
            }

            var draft = new UiPageManifestEntry
            {
                Name = name,
                PrefabPath = path,
                Forms = new List<UiPageFormEntry>(),
                Owner = NormalizeOwner(owner),
                Status = nameof(UiPageStatus.Draft),
                CacheStrategy = UiPageContract.DefaultCacheStrategy,
                ContractPath = UiContractPaths.ForPage(name),
                ContractPresent = File.Exists(UiPageManifestPaths.FullPath(UiContractPaths.ForPage(name))),
            };
            manifest.Pages.Add(draft);
            manifest.Pages.Sort((a, b) => string.CompareOrdinal(a?.Name, b?.Name));
            UiPageManifestSync.RefreshSourceHash(manifest);
            return draft;
        }

        /// <summary>状态流转（按 <see cref="UiPageStatusRules"/> 迁移表；同值＝无操作）。</summary>
        public static bool SetStatus(UiPageManifest manifest, string name, UiPageStatus status, out string error)
        {
            UiPageManifestEntry entry = Find(manifest, name, out error);
            if (entry == null) return false;

            if (!UiPageStatusRules.TryParse(entry.Status, out UiPageStatus current))
            {
                error = $"{name}：当前状态非法 {entry.Status}";
                return false;
            }
            if (current == status) return true;
            if (!UiPageStatusRules.CanTransition(current, status))
            {
                error = $"{name}：非法状态迁移 {current} → {status}";
                return false;
            }

            entry.Status = status.ToString();
            UiPageManifestSync.RefreshSourceHash(manifest);
            return true;
        }

        /// <summary>更新维护字段（Owner/设计稿/设备档/备注）；null＝保留原值。</summary>
        public static bool SetEditorial(UiPageManifest manifest, string name, string owner, string designRef,
            IReadOnlyList<string> deviceProfiles, string notes, out string error)
        {
            UiPageManifestEntry entry = Find(manifest, name, out error);
            if (entry == null) return false;

            if (owner != null) entry.Owner = NormalizeOwner(owner);
            if (designRef != null) entry.DesignRef = Trimmed(designRef);
            if (notes != null) entry.Notes = Trimmed(notes);
            if (deviceProfiles != null)
            {
                entry.DeviceProfiles = deviceProfiles
                    .Where(profile => !string.IsNullOrWhiteSpace(profile))
                    .Select(profile => profile.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(profile => profile, StringComparer.Ordinal)
                    .ToList();
            }

            UiPageManifestSync.RefreshSourceHash(manifest);
            return true;
        }

        /// <summary>
        /// 缓存策略（维护字段；词表单源＝<see cref="LiteClient.UI.UICacheStrategy"/>）。`tbuiform` 无该列，
        /// 运行时暂不消费；对账保留本字段，契约生成投影同一值。
        /// </summary>
        public static bool SetCacheStrategy(UiPageManifest manifest, string name, string cacheStrategy,
            out string error)
        {
            UiPageManifestEntry entry = Find(manifest, name, out error);
            if (entry == null) return false;

            string normalized = (cacheStrategy ?? string.Empty).Trim();
            if (!UiPageContract.IsValidCacheStrategy(normalized))
            {
                error = $"{name}：缓存策略非法 {cacheStrategy}";
                return false;
            }

            entry.CacheStrategy = normalized;
            UiPageManifestSync.RefreshSourceHash(manifest);
            return true;
        }

        /// <summary>显式移除条目（页面退役/撤销登记）：对账的孤儿策略会保留非 Draft 条目，故删除必须是显式编辑。</summary>
        public static bool Remove(UiPageManifest manifest, string name, out string error)
        {
            UiPageManifestEntry entry = Find(manifest, name, out error);
            if (entry == null) return false;
            manifest.Pages.Remove(entry);
            UiPageManifestSync.RefreshSourceHash(manifest);
            return true;
        }

        private static UiPageManifestEntry Find(UiPageManifest manifest, string name, out string error)
        {
            error = null;
            if (manifest?.Pages == null)
            {
                error = "清单为空";
                return null;
            }
            foreach (UiPageManifestEntry entry in manifest.Pages)
            {
                if (entry == null || !string.Equals(entry.Name, name, StringComparison.Ordinal)) continue;
                return entry;
            }
            error = $"页面不存在：{name}";
            return null;
        }

        private static string NormalizeOwner(string owner)
            => string.IsNullOrWhiteSpace(owner) ? UiPageManifestSync.DefaultOwner : owner.Trim();

        private static string Trimmed(string value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
