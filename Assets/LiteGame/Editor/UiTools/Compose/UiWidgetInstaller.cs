using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Compose
{
    /// <summary>
    /// 控件模板插入（《UI编辑器工具专项设计》§5"控件库"）：**只在 Prefab Stage 内插入**——
    /// 复用 Unity 原生嵌套/Variant 作用域与 Undo，不造第二套画布、不生成 prefab。
    /// 插入**不改名**（§5：不以面板自动重名规则掩盖绑定冲突），命名/标记的下一步交绑定标记工具。
    /// 纯逻辑面（零 Sirenix）。
    /// </summary>
    public static class UiWidgetInstaller
    {
        /// <summary>可否插入：目标页面须正开 Prefab Stage（插入即原生嵌套实例，保存随 Stage）。</summary>
        public static bool CanInstall(string prefabPath, out string reason)
        {
            if (string.IsNullOrEmpty(prefabPath))
            {
                reason = "未绑定目标 UI prefab";
                return false;
            }
            if (UiComposeTarget.StageRootFor(prefabPath) == null)
            {
                reason = "目标页面未在 Prefab Stage 打开——插入须在原生编辑态进行（打开 Stage 后重试）";
                return false;
            }
            reason = null;
            return true;
        }

        /// <summary>
        /// 把模板实例插到 <paramref name="parent"/> 下（同一 Undo 组，可整体撤销）。返回插入实例；
        /// 不可插入时返回 null（<paramref name="reason"/> 给出原因）。
        /// </summary>
        public static GameObject Install(GameObject template, Transform parent, string prefabPath, out string reason)
        {
            if (template == null)
            {
                reason = "模板资产缺失";
                return null;
            }
            if (!CanInstall(prefabPath, out reason)) return null;

            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("插入 UI 控件模板");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(template, parent);
            if (instance != null) Undo.RegisterCreatedObjectUndo(instance, "插入 UI 控件模板");
            Undo.CollapseUndoOperations(group);
            reason = null;
            return instance;
        }
    }
}
