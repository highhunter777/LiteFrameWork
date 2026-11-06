using LiteGame.Editor.UITools.Binding;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UITools.Compose
{
    /// <summary>
    /// 绑定暴露分区（《UI编辑器工具专项设计》§8.2）：**编辑面内嵌**，不再跳独立窗口。
    /// </summary>
    public sealed class UIBindingsSection
    {
        private readonly UIBindingEditor _editor = new UIBindingEditor();

        public void Draw(UIComposeTarget target)
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
