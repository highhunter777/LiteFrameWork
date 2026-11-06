using UnityEngine;

namespace LiteFramework.UI
{
    /// <summary>
    /// 静态文案声明标记（《UI 系统总设计》§6.3 / 《UI 编辑器工具链专项设计》§5）： 挂在静态文本节点上，声明该节点由 `tbltext` 的哪个 key 写入。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("LiteGame/UI/LTextKeyRef 静态文案")]
    public sealed class LTextKeyRef : MonoBehaviour
    {
        [Tooltip("tbltext 文案键（UI.<页面>.<语义> / Common.<语义>）；只存 key，不含参数/复数")]
        public string Key = string.Empty;
    }
}
