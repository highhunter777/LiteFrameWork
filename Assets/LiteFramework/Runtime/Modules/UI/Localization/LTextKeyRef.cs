using UnityEngine;

namespace LiteClient.UI
{
    /// <summary>
    /// 静态文案声明标记（《UI 系统总设计》§6.3 / 《UI 编辑器工具链专项设计》§5）：
    /// 挂在静态文本节点上，声明该节点由 `tbltext` 的哪个 key 写入。
    ///
    /// **只存 Key**：参数/复数/富文本替换属于工程动态文案，不进本组件；同一节点只能有一个
    /// 文案写者（静态 key 与 Lua/C# 动态写互斥，由契约校验拦截）。
    /// 写入时机归 <see cref="UiStaticTextBinder"/>：页面打开与语言变更时写，不重跑 OnShow。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("LiteGame/UI/LTextKeyRef 静态文案")]
    public sealed class LTextKeyRef : MonoBehaviour
    {
        [Tooltip("tbltext 文案键（UI.<页面>.<语义> / Common.<语义>）；只存 key，不含参数/复数")]
        public string Key = string.Empty;
    }
}
