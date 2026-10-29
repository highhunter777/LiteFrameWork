using System;
using LiteFramework;
using LiteFramework.UI;
using LiteFramework;
using LiteBattle;

namespace LiteGame
{
    /// <summary>
    /// 开发装配端口（由 `LiteGame.Dev` 实现；产品程序集只认本接口，不引用开发程序集）。
    /// 能力面 = 会话实现 / 容器装饰（GM/弱网/调试内核装配）/ 传输包装 / 对局诊断登记 / 对局开发叠加层。
    /// </summary>
    public interface IDevAssembly
    {
        /// <summary>建开发会话（发布形态不走本接口——空实现直接 `NullDevSession`）。</summary>
        IDevSession CreateSession();

        /// <summary>装配期装饰容器（GM 命令/弱网/调试内核）；返回的句柄负责注销与静态收口。</summary>
        IDisposable Decorate(DevServices services);

        /// <summary>包装传输工厂（弱网注入装饰器）；装配根在创建会话工厂前调用。</summary>
        Func<bool, MatchTransport> WrapTransportFactory(Func<bool, MatchTransport> inner);

        /// <summary>对局诊断登记端口（DevHUD/GM 页读口）。</summary>
        IBattleDevDiagnostics BattleDiagnostics { get; }

        /// <summary>建对局开发叠加层（爆头诊断绘制/自瞄验证；每次进局一件）。</summary>
        IBattleDevOverlay CreateBattleOverlay();

        /// <summary>注入宿主上的开发运行期调节件（DebugRuntimeTuner；未挂载 = no-op）。</summary>
        void InjectRuntimeTuners(UnityEngine.GameObject host, IWorldClock worldClock, IUIClock uiClock,
            EventBus events, CommandCenter commands);
    }

    /// <summary>装配期交给开发层的服务面（只读范围：容器/命令/场景/输入/会话）。</summary>
    public sealed class DevServices
    {
        public ModuleContext Context;
        public CommandCenter Commands;
        public ServiceContainer Services;
        public IDevSession Session;
        public ISceneService Scenes;
        public IInputService Input;
    }

    /// <summary>
    /// 开发装配钩子（**产品侧唯一静态缝**，M3-6b 3b）：`LiteGame.Dev` 在
    /// `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)` 绑定，早于场景引导；release 构建没有 Dev 程序集
    /// → 恒为 null，产品走 `NullDevSession` / 跳过开发装配（与 `LocalTransportRegistry` 同款接缝纪律：
    /// 只存一个实现引用、不做服务定位；新增能力一律走接口）。
    /// </summary>
    public static class DevHooks
    {
        private static IDevAssembly s_current;

        /// <summary>当前开发装配（null = 发布形态/Dev 未绑定）。</summary>
        public static IDevAssembly Current => s_current;

        /// <summary>对局诊断端口（null = 发布形态）。</summary>
        public static IBattleDevDiagnostics Diagnostics => s_current != null ? s_current.BattleDiagnostics : null;

        /// <summary>绑定实现（Dev 程序集引导期调用一次）。</summary>
        public static void Bind(IDevAssembly assembly) => s_current = assembly;

        /// <summary>解绑（编辑器域重载/测试收尾）。</summary>
        public static void Unbind() => s_current = null;
    }
}
