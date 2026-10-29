using LiteFramework.UI;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Widgets
{
    /// <summary>
    /// `WidgetSpec` 的编辑器面（《UI 制作规范》§3.2 控件模板根声明）：默认字段编辑 +
    /// 「从节点发现插槽」一键并入（Undo 可回滚、幂等、保留作者填写）。
    /// </summary>
    [CustomEditor(typeof(WidgetSpec))]
    public sealed class WidgetSpecInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();

            var spec = (WidgetSpec)target;
            if (GUILayout.Button("从节点发现插槽"))
            {
                Undo.RecordObject(spec, "发现 WidgetSpec 插槽");
                int added = WidgetSpecAuthoring.ApplyDiscoveredSlots(spec, spec.gameObject);
                if (added > 0)
                {
                    EditorUtility.SetDirty(spec);
                    serializedObject.Update();
                    Debug.Log($"[WidgetSpec] 已并入 {added} 个插槽：{spec.name}");
                }
                else
                {
                    Debug.Log("[WidgetSpec] 未发现新插槽（已声明条目保持不变）");
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
