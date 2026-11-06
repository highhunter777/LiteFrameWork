using UnityEngine;

namespace LiteFramework.UI
{
    /// <summary>
    /// 颜色令牌引用（《UI 系统总设计》§6.2「prefab 上的颜色优先挂引用组件，而不是裸值」）： 挂本组件＝声明"该 Graphic 的颜色＝<see cref="ColorToken"/> 令牌现值"——编辑期由样式。
    /// </summary>
    [AddComponentMenu("LiteGame/UI/UIStyleRef 颜色令牌引用")]
    [DisallowMultipleComponent]
    public sealed class UIStyleRef : MonoBehaviour
    {
        [Tooltip("颜色令牌 ID（如 color.bg.screen；空＝未声明，应用跳过）")]
        [SerializeField] private string colorToken;

        /// <summary>颜色令牌 ID；空＝未声明。</summary>
        public string ColorToken
        {
            get => colorToken;
            set => colorToken = value;
        }
    }
}
