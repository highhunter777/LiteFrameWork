using System.Collections.Generic;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Compose
{
    /// <summary>
    /// 受控属性分区（《UI编辑器工具专项设计》§5"受控属性"）：按白名单 attribute（<c>UiWidgetProperty</c>）
    /// 枚举可改成员，逐组件用 **Odin 属性树**只画白名单成员——未标注成员在本分区**没有任何绘制入口**，
    /// "不暴露控件内部实现"是机制而非按钮禁用。写入/Undo/脏标记由 Odin 属性树统一处理
    /// （<see cref="InspectorUtilities.BeginDrawPropertyTree"/> / <see cref="InspectorUtilities.EndDrawPropertyTree"/> 绘制对）。
    ///
    /// 白名单发现走零 Sirenix 的 <see cref="UiWidgetPropertyCatalog"/>（测试只测该处）。
    /// 几何/锚点/尺寸不在此面——归 Unity Prefab Stage 原生编辑（§5"布局操作仍由 Prefab Stage 提供"）。
    /// </summary>
    public sealed class UiWidgetPropertySection
    {
        private sealed class ComponentView
        {
            public PropertyTree Tree;
            public HashSet<string> Members;
        }

        private readonly List<ComponentView> _views = new List<ComponentView>();
        private readonly List<UiWidgetPropRow> _rows = new List<UiWidgetPropRow>();
        private Vector2 _scroll;
        private int _rootId = -1;

        public void Draw(UiComposeTarget target)
        {
            if (target == null)
            {
                EditorGUILayout.HelpBox("未绑定目标 UI prefab。", MessageType.Info);
                return;
            }

            SyncViews(target);
            if (_rows.Count == 0)
            {
                EditorGUILayout.HelpBox("本页无受控属性声明——控件组件上未见 UiWidgetProperty 标注。", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField($"受控成员 {_rows.Count} 行 · 承载组件 {_views.Count} 个（只列白名单成员）",
                EditorStyles.miniLabel);

            int unsupported = 0;
            foreach (var row in _rows)
                if (row.Kind == UiWidgetPropKind.Unsupported) unsupported++;
            if (unsupported > 0)
                EditorGUILayout.LabelField($"值型未支持 {unsupported} 行（受控最小集＝bool/int/float/string/enum；"
                                           + "其余按 Odin 默认绘制——需要收口就别标注）", EditorStyles.miniLabel);

            EditorGUILayout.HelpBox("控件内部实现与接线引用不进此面；几何/锚点请在 Prefab Stage 原生编辑。", MessageType.None);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var view in _views)
            {
                if (view == null || view.Tree == null) continue;
                EditorGUILayout.LabelField(view.Tree.TargetType.Name, EditorStyles.boldLabel);
                view.Tree.UpdateTree();
                InspectorUtilities.BeginDrawPropertyTree(view.Tree, true);
                try
                {
                    foreach (var property in view.Tree.RootProperty.Children)
                    {
                        if (property == null || !view.Members.Contains(property.Name)) continue;
                        property.Draw(property.Label);
                    }
                }
                finally
                {
                    InspectorUtilities.EndDrawPropertyTree(view.Tree);
                }
                view.Tree.ApplyChanges();
                EditorGUILayout.Space(4f);
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>显示根变化即重收集（行与属性树都挂在旧根上）。</summary>
        private void SyncViews(UiComposeTarget target)
        {
            var root = target.DisplayRoot;
            int id = root != null ? root.GetInstanceID() : -1;
            if (id == _rootId) return;

            DisposeViews();
            _rootId = id;
            _rows.Clear();
            if (root == null) return;

            _rows.AddRange(UiWidgetPropertyCatalog.Collect(root));
            var membersByComponent = new Dictionary<Component, HashSet<string>>();
            var ordered = new List<Component>();
            foreach (var row in _rows)
            {
                if (row.Owner == null) continue;
                if (!membersByComponent.TryGetValue(row.Owner, out var members))
                {
                    members = new HashSet<string>();
                    membersByComponent[row.Owner] = members;
                    ordered.Add(row.Owner);
                }
                members.Add(row.MemberName);
            }

            foreach (var component in ordered)
                _views.Add(new ComponentView { Tree = PropertyTree.Create(component), Members = membersByComponent[component] });
        }

        private void DisposeViews()
        {
            foreach (var view in _views)
                if (view.Tree is System.IDisposable disposable) disposable.Dispose();
            _views.Clear();
        }
    }
}
