using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LiteCodeGen;
using LiteGame.Editor.UiTools.Validation;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Binding
{
    /// <summary>生成物 hash 校验问题（<see cref="UiBindGenerationHash.Check"/> 产出）。</summary>
    public sealed class UiBindHashIssue
    {
        public string PrefabPath;
        public UiBindHashIssueKind Kind;
        public string Detail;
    }

    public enum UiBindHashIssueKind
    {
        /// <summary>带 BindRoot 的 prefab 从未记录——首次生成后须记录（Error）。</summary>
        NotRecorded,
        /// <summary>源绑定面与记录不符——改了 prefab 却未重新生成/重记录（Error，§2.1.5 本意）。</summary>
        Stale,
        /// <summary>记录的生成物文件缺失——生成物被删/未提交（Error）。</summary>
        GeneratedMissing,
        /// <summary>清单条目对应的 prefab 已不存在（Warning——清单残留，重记录即清）。</summary>
        OrphanEntry,
    }

    /// <summary>
    /// 生成物 hash（《UI编辑器工具专项设计》§2.1.5）：记录源 prefab 绑定面内容指纹；
    /// CI 侧复算比对——"提交资产却漏生成物"在门禁变红，不靠人眼。
    ///
    /// **记录**是作者动作（生成完成后经菜单/窗口记录）；**校验**经校验核心的
    /// <c>ui.binding-hash</c> 规则跑（三入口共用，CI 走既有 <c>UiValidator.RunCli</c>）。
    /// 清单＝工程内 JSON 文本（git 可 diff，CI 可直读）；键＝prefab GUID（改名不断链）。
    /// </summary>
    public static class UiBindGenerationHash
    {
        /// <summary>清单落点（Editor 域——纯开发期对账数据，不进包）。</summary>
        public const string DefaultManifestPath = "Assets/LiteGame/Editor/UiTools/UiBindManifest.json";

        // ---------------- 指纹 ----------------

        /// <summary>
        /// 绑定面指纹：BindRoot 生成配置 + 全部 BindNode 标记（按 <c>BindSearch</c> 同一 traversal
        /// ——遇嵌套 BindRoot 子树不深入，与生成语义一致）。同内容必同 hash、任何标记/配置变更必变。
        /// </summary>
        public static string ComputeHash(GameObject prefabRoot)
        {
            var sb = new StringBuilder();
            var root = prefabRoot != null ? prefabRoot.GetComponent<BindRoot>() : null;
            if (root != null)
            {
                sb.Append("bindroot|").Append(root.ScriptName ?? string.Empty).Append('|')
                    .Append(root.Namespace ?? string.Empty).Append('|')
                    .Append(root.ScriptsFolder ?? string.Empty).Append('|')
                    .Append(root.BaseClassFullName ?? string.Empty).Append('|')
                    .Append(root.EmitRegisterControl).Append('\n');
            }
            else
            {
                sb.Append("bindroot|none\n");
            }

            if (prefabRoot != null)
                Walk(prefabRoot.transform, sb);

            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").ToLowerInvariant();
        }

        private static void Walk(Transform t, StringBuilder sb)
        {
            foreach (Transform child in t)
            {
                if (child.GetComponent<BindRoot>() != null) continue;   // 嵌套生成根：独立单位（BindSearch 同款）
                var bind = child.GetComponent<BindNode>();
                if (bind != null)
                {
                    sb.Append("node|").Append(UiBindingRename.RelativePath(child)).Append('|')
                        .Append(bind.BindName ?? string.Empty).Append('|')
                        .Append(bind.CustomTypeName ?? string.Empty).Append('|')
                        .Append(bind.ResolvedTypeName ?? string.Empty).Append('|')
                        .Append(bind.Comment ?? string.Empty).Append('\n');
                }
                Walk(child, sb);
            }
        }

        // ---------------- 生成物文件 ----------------

        /// <summary>BindRoot 指向的生成物路径（与 BindGenerator 同一拼法；ScriptName 为空＝尚未可生成 → 空表）。</summary>
        public static List<string> GeneratedFilePaths(BindRoot root)
        {
            var files = new List<string>();
            if (root == null) return files;
            var className = root.ScriptName == null ? string.Empty : root.ScriptName.Trim();
            if (string.IsNullOrEmpty(className)) return files;
            var folder = (root.ScriptsFolder ?? string.Empty).Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(folder)) folder = "Assets/Scripts";
            files.Add(folder + "/" + className + ".cs");
            files.Add(folder + "/" + className + ".Designer.cs");
            return files;
        }

        // ---------------- 清单 I/O ----------------

        [Serializable]
        private sealed class ManifestFile
        {
            public ManifestEntry[] entries = Array.Empty<ManifestEntry>();
        }

        [Serializable]
        private sealed class ManifestEntry
        {
            public string guid;
            public string path;
            public string hash;
            public string[] files = Array.Empty<string>();
        }

        private static ManifestFile LoadManifest(string manifestPath)
        {
            var full = FullPath(manifestPath);
            if (!File.Exists(full)) return new ManifestFile();
            try
            {
                return JsonUtility.FromJson<ManifestFile>(File.ReadAllText(full)) ?? new ManifestFile();
            }
            catch (Exception e)
            {
                Debug.LogError($"[UI 绑定] 生成物清单不可读（{manifestPath}）：{e.Message}——重记录全部可重建");
                return new ManifestFile();
            }
        }

        private static void SaveManifest(ManifestFile manifest, string manifestPath)
        {
            var full = FullPath(manifestPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, JsonUtility.ToJson(manifest, prettyPrint: true));
            AssetDatabase.ImportAsset(manifestPath);
        }

        private static string FullPath(string manifestPath)
            => Path.Combine(UiValidator.ProjectRoot, manifestPath.Replace('/', Path.DirectorySeparatorChar));

        // ---------------- 记录 ----------------

        /// <summary>记录一个 prefab 的绑定面 hash 与生成物清单（幂等——重记录即覆盖该条）。</summary>
        public static void Record(string prefabPath, string manifestPath = DefaultManifestPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) throw new InvalidOperationException($"prefab 不存在：{prefabPath}");

            var manifest = LoadManifest(manifestPath);
            var entry = new ManifestEntry
            {
                guid = RequireGuid(prefabPath),
                path = prefabPath,
                hash = ComputeHash(prefab),
                files = GeneratedFilePaths(prefab.GetComponent<BindRoot>()).ToArray(),
            };

            int index = Array.FindIndex(manifest.entries, e => e.guid == entry.guid);
            if (index < 0)
            {
                Array.Resize(ref manifest.entries, manifest.entries.Length + 1);
                manifest.entries[manifest.entries.Length - 1] = entry;
            }
            else
            {
                manifest.entries[index] = entry;
            }
            SaveManifest(manifest, manifestPath);
            Debug.Log($"[UI 绑定] 已记录生成物 hash：{prefabPath} → {entry.hash.Substring(0, 8)}…");
        }

        /// <summary>清掉已无 prefab 的清单残留（重记录前顺手调用；返回清理条数）。</summary>
        public static int PruneOrphans(string manifestPath = DefaultManifestPath)
        {
            var manifest = LoadManifest(manifestPath);
            int before = manifest.entries.Length;
            var kept = new List<ManifestEntry>();
            foreach (var e in manifest.entries)
                if (IsEntryAlive(e)) kept.Add(e);
            if (kept.Count == before) return 0;
            manifest.entries = kept.ToArray();
            SaveManifest(manifest, manifestPath);
            return before - kept.Count;
        }

        /// <summary>读某 prefab 已记录的 hash（未记录/无清单 → false；窗口状态面用）。</summary>
        public static bool TryGetRecordedHash(string prefabPath, out string recordedHash, string manifestPath = DefaultManifestPath)
        {
            recordedHash = null;
            var guid = AssetDatabase.AssetPathToGUID(prefabPath);
            if (string.IsNullOrEmpty(guid)) return false;
            var manifest = LoadManifest(manifestPath);
            foreach (var e in manifest.entries)
                if (e.guid == guid) { recordedHash = e.hash; return true; }
            return false;
        }

        // ---------------- 校验 ----------------

        /// <summary>
        /// 全量对账：prefabPaths 里带 BindRoot 者须有记录且 hash 相符、生成物文件在盘；
        /// 清单条目对应的 prefab 缺失 → OrphanEntry。无 BindRoot 的 prefab 不产生条目要求。
        /// </summary>
        public static List<UiBindHashIssue> Check(IReadOnlyList<string> prefabPaths, string manifestPath = DefaultManifestPath)
        {
            var issues = new List<UiBindHashIssue>();
            var manifest = LoadManifest(manifestPath);
            var byGuid = new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
            foreach (var e in manifest.entries)
                if (!string.IsNullOrEmpty(e.guid)) byGuid[e.guid] = e;

            var seenGuids = new HashSet<string>(StringComparer.Ordinal);
            if (prefabPaths != null)
            {
                foreach (var path in prefabPaths)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) continue;
                    var guid = RequireGuid(path);
                    seenGuids.Add(guid);
                    var bindRoot = prefab.GetComponent<BindRoot>();
                    string current = ComputeHash(prefab);

                    if (!byGuid.TryGetValue(guid, out var entry))
                    {
                        if (bindRoot != null)
                            issues.Add(new UiBindHashIssue
                            {
                                PrefabPath = path,
                                Kind = UiBindHashIssueKind.NotRecorded,
                                Detail = "带 BindRoot 但从未记录生成物 hash——生成并记录（LiteGame/UI/绑定/生成物 hash）",
                            });
                        continue;
                    }

                    if (entry.hash != current)
                        issues.Add(new UiBindHashIssue
                        {
                            PrefabPath = path,
                            Kind = UiBindHashIssueKind.Stale,
                            Detail = $"绑定面已变更（记录 {Short(entry.hash)} ≠ 当前 {Short(current)}）——重新生成并记录",
                        });

                    foreach (var file in entry.files)
                        if (!File.Exists(Path.Combine(UiValidator.ProjectRoot, file.Replace('/', Path.DirectorySeparatorChar))))
                            issues.Add(new UiBindHashIssue
                            {
                                PrefabPath = path,
                                Kind = UiBindHashIssueKind.GeneratedMissing,
                                Detail = $"生成物缺失：{file}",
                            });
                }
            }

            foreach (var e in manifest.entries)
            {
                if (seenGuids.Contains(e.guid)) continue;
                if (!IsEntryAlive(e))
                    issues.Add(new UiBindHashIssue
                    {
                        PrefabPath = e.path,
                        Kind = UiBindHashIssueKind.OrphanEntry,
                        Detail = "清单条目的 prefab 已不存在——重记录全部可清",
                    });
            }
            return issues;
        }

        /// <summary>
        /// 清单条目是否仍活着。单查 <c>GUIDToAssetPath</c> 不够：同 tick 内 DeleteAsset 后缓存仍回旧路径
        /// ——须再核磁盘真身；而资产改名（MoveAsset）后 guid 解析到**新路径**且在盘——不是孤儿。
        /// </summary>
        private static bool IsEntryAlive(ManifestEntry e)
        {
            string resolved = AssetDatabase.GUIDToAssetPath(e.guid);
            if (string.IsNullOrEmpty(resolved)) return false;
            return File.Exists(Path.Combine(UiValidator.ProjectRoot, resolved.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string Short(string hash) => string.IsNullOrEmpty(hash) ? "-" : hash.Substring(0, 8);

        private static string RequireGuid(string path)
        {
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException($"取不到 GUID：{path}");
            return guid;
        }

        // ---------------- 菜单入口（记录是作者动作；校验走校验核心规则） ----------------

        private static void MenuRecordSelected()
        {
            int done = 0;
            foreach (var go in Selection.GetFiltered<GameObject>(SelectionMode.Assets))
            {
                if (!PrefabUtility.IsPartOfPrefabAsset(go)) continue;
                Record(AssetDatabase.GetAssetPath(go));
                done++;
            }
            if (done == 0) Debug.LogWarning("[UI 绑定] 未选中 prefab 资产——在 Project 面板选中后重试");
        }

        private static void MenuRecordAll()
        {
            int pruned = PruneOrphans();
            int done = 0;
            foreach (var path in CollectBindRootPrefabs())
            {
                Record(path);
                done++;
            }
            Debug.Log($"[UI 绑定] 重记录完成：{done} 个 BindRoot prefab（清理残留 {pruned} 条）");
        }


        /// <summary>工具平台入口：记录全部 BindRoot prefab。</summary>
        public static void RecordAllForTool()
        {
            MenuRecordAll();
        }

        /// <summary>工具平台入口：记录当前选中的 prefab。</summary>
        public static void RecordSelectedForTool()
        {
            MenuRecordSelected();
        }

        private static List<string> CollectBindRootPrefabs()
        {
            var paths = new List<string>();
            foreach (var dir in new[] { UiValidator.ScreensDir, UiValidator.WidgetsDir, UiValidator.HudDir })
            {
                if (!AssetDatabase.IsValidFolder(dir)) continue;
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { dir }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null && prefab.GetComponent<BindRoot>() != null) paths.Add(path);
                }
            }
            paths.Sort(System.StringComparer.Ordinal);
            return paths;
        }
    }
}
