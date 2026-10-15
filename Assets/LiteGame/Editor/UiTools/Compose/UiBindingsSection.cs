using System.Collections.Generic;
using LiteGame.Editor.UiTools.Binding;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Compose
{
    /// <summary>
    /// 绑定暴露分区（《UI编辑器工具专项设计》§5"绑定暴露树"）：**内嵌只读视图 + 跳转编辑**——
    /// 树数据与绘制复用 <see cref="UiBindingRename.BuildTree"/> / <see cref="UiBindTreeView"/> 单源；
    /// 前缀预设/批量改名/预览等编辑动作仍归绑定标记工具窗口，不搬进本分区。
    /// </summary>
    public sealed class UiBindingsSection
    {
        private List<UiNodeRow> _rows = new List<UiNodeRow>();
        private Vector2 _scroll;
        private string _selectedPath;
        private int _rootId = -1;

        public void Draw(UiComposeTarget target)
        {
            if (target == null)
            {
                EditorGUILayout.HelpBox("未绑定目标 UI prefab。", MessageType.Info);
                return;
            }

            SyncRows(target);
            var root = target.DisplayRoot;

            UiBindTreeView.Draw(_rows, ref _scroll, ref _selectedPath, null, out _, pingOnSelect: true);

            int marked = 0, inClone = 0, roots = 0;
            foreach (var row in _rows)
            {
                if (row.HasBindNode) marked++;
                if (row.InCloneScope) inClone++;
                if (row.HasBindRoot) roots++;
            }
            EditorGUILayout.LabelField(
                $"节点 {_rows.Count} · 已标记 {marked} · 列表项作用域 {inClone} · 生成根 {roots}（只读视图）",
                EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("打开绑定标记工具（编辑：前缀预设/批量改名/预览）", GUILayout.ExpandWidth(false)))
                {
                    var window = EditorWindow.GetWindow<UiBindingWindow>("绑定标记工具");
                    if (target.PrefabAsset != null) window.BindPrefab(target.PrefabAsset);
                    window.Show();
                    window.Repaint();
                }
            }
            if (!string.IsNullOrEmpty(_selectedPath))
                EditorGUILayout.LabelField("选中：" + _selectedPath, EditorStyles.miniLabel);
        }

        /// <summary>显示根变化即重建行集（Stage 开关 / 换 prefab 都会换根）。</summary>
        private void SyncRows(UiComposeTarget target)
        {
            var root = target.DisplayRoot;
            int id = root != null ? root.GetInstanceID() : -1;
            if (id == _rootId) return;
            _rootId = id;
            _selectedPath = null;
            _rows = UiBindingRename.BuildTree(root);
        }
    }
}
