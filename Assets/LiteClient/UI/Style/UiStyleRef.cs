using UnityEngine;

namespace LiteClient.UI
{
    /// <summary>
    /// 颜色令牌引用（《UI 系统总设计》§6.2「prefab 上的颜色优先挂引用组件，而不是裸值」）：
    /// 挂本组件＝声明"该 Graphic 的颜色＝<see cref="ColorToken"/> 令牌现值"——编辑期由样式工具按声明应用/对账，
    /// 运行期由 UIForm 装配注入（ThemeProvider 缝）写入。**只声明、不驱动**（无 Awake/OnEnable 写值——写值统一走应用核心，
    /// 单写者）；空 token＝未声明合法（应用跳过）；目标 v1＝自身 Graphic。
    /// </summary>
    [AddComponentMenu("LiteGame/UI/UiStyleRef 颜色令牌引用")]
    [DisallowMultipleComponent]
    public sealed class UiStyleRef : MonoBehaviour
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
