using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Binding
{
    /// <summary>
    /// 绑定暴露树的**只读/可勾选**绘制器（《UI编辑器工具专项设计》§8.2 绑定分区）：
    /// 数据单源＝<see cref="UiBindingRename.BuildTree"/> 的行集，本类只画行、不改资产。
    /// 编辑动作（前缀预设/批量改名/预览/应用）由 <see cref="UiBindingEditor"/> 在同一分区内承担。
    /// </summary>
    public static class UiBindTreeView
    {
        /// <summary>
        /// 画行集。<paramref name="checkedPaths"/> 非空＝画勾选列（批量操作用）；<paramref name="clicked"/>
        /// 回传本帧被点选的行（无＝null）。选中态与勾选态就地更新。
        /// 返回 true＝本帧选中或勾选发生变化（调用方据此作废旧预览）。
        /// </summary>
        public static bool Draw(IReadOnlyList<UiNodeRow> rows, ref Vector2 scroll,
                                ref string selectedPath, HashSet<string> checkedPaths, out UiNodeRow clicked,
                                bool pingOnSelect = true)
        {
            bool changed = false;
            clicked = null;
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            foreach (var row in rows)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (checkedPaths != null)
                    {
                        bool wasChecked = checkedPaths.Contains(row.Path);
                        bool nowChecked = GUILayout.Toggle(wasChecked, GUIContent.none, GUILayout.Width(16f));
                        if (nowChecked != wasChecked)
                        {
                            if (nowChecked) checkedPaths.Add(row.Path); else checkedPaths.Remove(row.Path);
                            changed = true;
                        }
                    }

                    var style = row.IsRoot ? EditorStyles.boldLabel : EditorStyles.label;
                    if (GUILayout.Button(new string(' ', row.Depth * 2) + row.Name, style, GUILayout.ExpandWidth(true)))
                    {
                        selectedPath = row.Path;
                        clicked = row;
                        changed = true;
                        if (pingOnSelect && row.Node != null) EditorGUIUtility.PingObject(row.Node.gameObject);
                    }

                    if (row.HasBindRoot) GUILayout.Label("[生成根]", EditorStyles.miniLabel, GUILayout.Width(52f));
                    if (row.InCloneScope) GUILayout.Label("[列表项]", EditorStyles.miniLabel, GUILayout.Width(52f));
                    if (row.HasBindNode)
                    {
                        bool drift = row.BindName != row.Name && !string.IsNullOrEmpty(row.BindName);
                        GUILayout.Label(drift
                            ? $"[Bind:{row.BindName}≠{row.Name}]"
                            : $"[Bind:{row.BindName}]", drift ? EditorStyles.miniBoldLabel : EditorStyles.miniLabel,
                            GUILayout.Width(150f));
                        GUILayout.Label(row.TypeName ?? "-", EditorStyles.miniLabel, GUILayout.Width(190f));
                    }
                }
            }
            EditorGUILayout.EndScrollView();
            return changed;
        }
    }
}
