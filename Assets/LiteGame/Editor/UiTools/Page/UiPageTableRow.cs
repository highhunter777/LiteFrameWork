using System;

namespace LiteGame.Editor.UiTools.Page
{
    /// <summary>
    /// `tbuiform` 行投影（《UI 编辑器工具链专项设计》§3.1/§3.6）：表读面/写面与页面清单对账共用的行 DTO。
    /// 字段名与 `scripts/codegen/_uiform_row.py` 的 JSON 输出一一对应；不引用 UnityEditor。
    /// </summary>
    [Serializable]
    public sealed class UiPageTableRow
    {
        public int id;
        public string lua;
        public string prefab;
        public int layer;
        public bool fullScreen;

        /// <summary>`Assets/…` 形式的 prefab 路径（表里存的是相对 Assets 的路径）。</summary>
        public string PrefabPath => string.IsNullOrEmpty(prefab) ? string.Empty : "Assets/" + prefab;

        /// <summary>prefab 文件名（含扩展名）。</summary>
        public string FileName => string.IsNullOrEmpty(prefab)
            ? string.Empty
            : prefab.Substring(prefab.LastIndexOf('/') + 1);

        /// <summary>页面名（prefab 文件名去扩展名）。</summary>
        public string PageName => string.IsNullOrEmpty(FileName)
            ? string.Empty
            : System.IO.Path.GetFileNameWithoutExtension(FileName);
    }
}
