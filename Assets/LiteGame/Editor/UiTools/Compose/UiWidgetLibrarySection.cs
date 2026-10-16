using System.Collections.Generic;
using LiteGame.Editor.UiTools.Binding;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Compose
{
    /// <summary>
    /// 控件库分区（《UI编辑器工具专项设计》§5"控件库"）：**清单＝目录即清单**
    /// （<see cref="UiWidgetLibrary.Load"/> 扫 <c>Assets/ShareResource/UI/Widgets</c> 的 <c>Tmp_</c> 模板）；
    /// **列表呈现＝inline OdinMenuTree**（左树 + Odin 搜索栏；右侧为选中模板的组件/受控面预览 + 插入）；
    /// 插入走 <see cref="UiWidgetInstaller"/>——**只在 Prefab Stage 内**原生嵌套实例化
    /// （Variant/嵌套作用域与 Undo 全复用 Unity 原生），插入后**不改名**，命名/标记的下一步交绑定标记工具。
    /// </summary>
    public sealed class UiWidgetLibrarySection
    {
        private List<UiWidgetEntry> _entries;
        private OdinMenuTree _tree;

        public void Draw(UiComposeTarget target)
        {
            if (target == null)
            {
                EditorGUILayout.HelpBox("未绑定目标 UI prefab。", MessageType.Info);
                return;
            }

            EnsureTree();
            bool canInstall = UiWidgetInstaller.CanInstall(target.PrefabPath, out string reason);
            var parent = ResolveParent(target, out string parentNote);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"模板 {_entries.Count} 件（目录即清单）", EditorStyles.miniLabel,
                    GUILayout.Width(220f));
                EditorGUILayout.LabelField($"插入父级 {parentNote}", EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("刷新", GUILayout.Width(52f)))
                {
                    _tree = null;
                    _entries = null;
                    EnsureTree();
                }
            }
            if (!canInstall) EditorGUILayout.HelpBox(reason, MessageType.Warning);

            _tree.DrawSearchToolbar(EditorStyles.toolbarSearchField);
            using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(300f)))
                {
                    _tree.DrawMenuTree();
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    DrawSelection(target, parent, canInstall);
                }
            }
        }

        private void EnsureTree()
        {
            if (_tree != null) return;
            if (_entries == null) _entries = UiWidgetLibrary.Load();

            _tree = new OdinMenuTree(supportsMultiSelect: false);
            foreach (var entry in _entries)
                _tree.Add(entry.TemplateName, entry.Asset);
        }

        /// <summary>右侧：选中模板的组件清单 + 受控面预览 + 插入入口（未选＝提示）。</summary>
        private void DrawSelection(UiComposeTarget target, Transform parent, bool canInstall)
        {
            var entry = CurrentEntry();
            if (entry == null)
            {
                EditorGUILayout.HelpBox("在左侧树中选一个模板（顶部有搜索栏）。", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(entry.TemplateName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"使用段：{entry.UsageSegment ?? "（结构工具件，不占类型段）"}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(entry.Path, EditorStyles.miniLabel);

            var components = new List<string>();
            foreach (var component in entry.Asset.GetComponents<Component>())
                if (component != null) components.Add(component.GetType().Name);
            EditorGUILayout.LabelField("组件：" + string.Join("、", components), EditorStyles.miniLabel);

            var rows = UiWidgetPropertyCatalog.Collect(entry.Asset);
            EditorGUILayout.LabelField(rows.Count > 0
                ? $"受控属性 {rows.Count} 行（可改面）："
                : "受控属性：无声明", EditorStyles.miniLabel);
            foreach (var row in rows)
                EditorGUILayout.LabelField($"　{row.ComponentTypeName}.{row.MemberName}（{row.Kind}）", EditorStyles.miniLabel);

            EditorGUILayout.Space(6f);
            using (new EditorGUI.DisabledScope(!canInstall || parent == null))
            {
                string caption = parent != null ? "插入到 " + UiBindingRename.RelativePath(parent) : "插入";
                if (GUILayout.Button(caption, GUILayout.Height(24f)))
                    Insert(entry, parent, target);
            }
            if (parent == null)
                EditorGUILayout.HelpBox("插入须在 Prefab Stage 内进行（打开目标页面后重试）。", MessageType.Warning);

            EditorGUILayout.HelpBox("插入后请到「绑定暴露」页签标记/命名（工具不自动改名，避免掩盖绑定冲突）。",
                MessageType.None);
        }

        private UiWidgetEntry CurrentEntry()
        {
            if (_tree?.Selection == null || _tree.Selection.Count == 0) return null;
            var asset = _tree.Selection[0].ObjectInstance as GameObject;
            if (asset == null) return null;
            foreach (var entry in _entries)
                if (entry.Asset == asset) return entry;
            return null;
        }

        /// <summary>插入父级：Stage 内当前选中节点；无选中＝Stage 根。父级不在 Stage 内则不可插入。</summary>
        private static Transform ResolveParent(UiComposeTarget target, out string note)
        {
            var stageRoot = UiComposeTarget.StageRootFor(target.PrefabPath);
            if (stageRoot == null)
            {
                note = "（无 Prefab Stage）";
                return null;
            }
            var selected = Selection.activeTransform;
            if (selected != null && selected != stageRoot.transform && selected.IsChildOf(stageRoot.transform))
            {
                note = UiBindingRename.RelativePath(selected);
                return selected;
            }
            note = "（Stage 根）";
            return stageRoot.transform;
        }

        private static void Insert(UiWidgetEntry entry, Transform parent, UiComposeTarget target)
        {
            var instance = UiWidgetInstaller.Install(entry.Asset, parent, target.PrefabPath, out string reason);
            if (instance == null)
            {
                if (!string.IsNullOrEmpty(reason)) Debug.LogWarning($"[UI 编排] 插入未进行：{reason}");
                return;
            }

            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);
            Debug.Log($"[UI 编排] 已插入模板 {entry.TemplateName} → {UiBindingRename.RelativePath(instance.transform)}"
                      + "（下一步：标记/命名）");
        }
    }
}
