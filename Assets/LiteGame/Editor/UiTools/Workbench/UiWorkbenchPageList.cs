using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.Editor.UiTools.Page;

namespace LiteGame.Editor.UiTools.Workbench
{
    /// <summary>页面透视的一行：清单条目投影（窗口只读展示，编辑走 <see cref="UiWorkbenchPageEditor"/>）。</summary>
    public sealed class UiWorkbenchPageRow
    {
        public string Name;
        public string PrefabPath;
        public string LuaPath;
        public string Owner;
        public string Status;
        public string DesignRef;
        public string Notes;
        public bool ContractPresent;
        public List<int> FormIds = new List<int>();
    }

    /// <summary>
    /// 页面清单投影：真实清单 → 透视行；缺清单/损坏**显式返回错误**，不伪造空表。
    /// 页面选择源＝`UiPageManifest`（含 `Page_/Hud_/Tmp_` 与草稿条目；窗口按 Status 标注草稿）。
    /// </summary>
    public static class UiWorkbenchPageList
    {
        /// <summary>读默认清单（<see cref="UiPageManifestPaths.DefaultPath"/>）。</summary>
        public static IReadOnlyList<UiWorkbenchPageRow> LoadDefault(out string error)
            => Load(UiPageManifestPaths.DefaultPath, out error);

        /// <summary>读指定清单（测试注入路径）；不存在或不可读时返回空并给出错误。</summary>
        public static IReadOnlyList<UiWorkbenchPageRow> Load(string assetPath, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(assetPath))
            {
                error = "页面清单路径为空";
                return Array.Empty<UiWorkbenchPageRow>();
            }

            if (!File.Exists(UiPageManifestPaths.FullPath(assetPath)))
            {
                error = "页面清单不存在：" + assetPath + "（先运行 ui.page-manifest）";
                return Array.Empty<UiWorkbenchPageRow>();
            }

            try
            {
                return FromManifest(UiPageManifestStore.Load(assetPath));
            }
            catch (Exception exception)
            {
                error = "页面清单不可读：" + assetPath + "：" + exception.Message;
                return Array.Empty<UiWorkbenchPageRow>();
            }
        }

        /// <summary>纯投影（清单已加载时用；不读盘，不改清单）。</summary>
        public static IReadOnlyList<UiWorkbenchPageRow> FromManifest(UiPageManifest manifest)
        {
            var rows = new List<UiWorkbenchPageRow>();
            if (manifest?.Pages == null) return rows;

            foreach (UiPageManifestEntry entry in manifest.Pages)
            {
                rows.Add(new UiWorkbenchPageRow
                {
                    Name = entry.Name,
                    PrefabPath = entry.PrefabPath,
                    LuaPath = entry.LuaPath,
                    Owner = entry.Owner,
                    Status = entry.Status,
                    DesignRef = entry.DesignRef,
                    Notes = entry.Notes,
                    ContractPresent = entry.ContractPresent,
                    FormIds = (entry.Forms ?? new List<UiPageFormEntry>()).Select(form => form.FormId).ToList(),
                });
            }

            return rows;
        }
    }
}
