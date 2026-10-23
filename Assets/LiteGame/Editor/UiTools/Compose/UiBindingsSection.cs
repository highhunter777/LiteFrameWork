using LiteGame.Editor.UiTools.Binding;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Compose
{
    /// <summary>
    /// 绑定暴露分区（《UI编辑器工具专项设计》§8.2）：**编辑面内嵌**，不再跳独立窗口。
    /// 树/预设/批量/预览/应用/hash 由 <see cref="UiBindingEditor"/> 承担；
    /// 本分区只做标题与目标就绪提示。
    /// </summary>
    public sealed class UiBindingsSection
    {
        private readonly UiBindingEditor _editor = new UiBindingEditor();

        public void Draw(UiComposeTarget target)
        {
            if (target == null)
            {
                EditorGUILayout.HelpBox("未绑定目标 UI prefab。", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("绑定暴露（编辑面：命名、BindName、批量与引用点）", EditorStyles.boldLabel);
            _editor.Draw(target);
        }
    }
}
