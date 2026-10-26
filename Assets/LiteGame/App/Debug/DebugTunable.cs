#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using System.Collections.Generic;

namespace LiteGame
{
    /// <summary>调试参数的公开范围（只影响展示/定档口径，不改变 release 剥离）。</summary>
    public enum DebugTunableScope
    {
        /// <summary>沙箱参数：调试期有效，不落盘、不进摘要/重放。</summary>
        Sandbox,
        /// <summary>纯表现参数：只影响客户端观感。</summary>
        Presentation,
        /// <summary>产品参数：定型后应迁到表/配置；调试覆写只作临时试验。</summary>
        Product,
    }

    public enum DebugTunableKind
    {
        Bool,
        Float,
    }

    /// <summary>
    /// 一个可调参数的描述符：功能自己提供 get/set/默认值/范围，编辑器页与运行期面板按描述渲染，
    /// 不按功能类型写专用 UI。默认值必须来自功能的数据源（表/烘焙/机制常量），不是面板字面量。
    /// </summary>
    public sealed class DebugTunable
    {
        public string Id;
        public string Group;
        public string Label;
        /// <summary>检索标签（空格分隔，可空）：编辑器过滤用，不作为身份键。</summary>
        public string Tags;
        public DebugTunableKind Kind;
        public DebugTunableScope Scope;
        public float Default;
        public float Min;
        public float Max;
        /// <summary>显示小数位（Bool 忽略）。默认 3；极小值参数可设 4～6。</summary>
        public int Decimals = 3;
        /// <summary>是否进入运行期快捷调参（F2 面板）。只放常用、现场可解释的参数；长尾仍走编辑器。</summary>
        public bool RuntimeVisible;
        public Func<float> Get;
        public Action<float> Set;
        /// <summary>可空；null = Set(Default)。覆写型参数应提供“回到源值”语义（通常清 -1）。</summary>
        public Action Reset;
        /// <summary>拥有者（场景组件可空）；discovery 用它剔除已销毁组件的描述符。</summary>
        public object Owner;
    }

    /// <summary>功能侧调试参数提供者：由容器服务或场景组件实现，内核 discovery 统一收集。</summary>
    public interface IDebugTunableProvider
    {
        void CollectDebugTunables(ICollection<DebugTunable> into);
    }
}
#endif
