using System.Threading;
using Cysharp.Threading.Tasks;
using LiteClient;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// ③ 开发会话端口（M0 接缝 6；M2 装配序修正）：产品代码只经 <see cref="IDevSession"/> 取测试面，
    /// 开发三宏内注入 <see cref="DevSession"/>、发布包注入全关空实现（<see cref="NullDevSession"/>）。
    ///
    /// **为什么单独成模块**：设备源（<see cref="InputModule"/>）构造即消费端口，而装配根
    /// （<see cref="ContainerModule"/>）在这一步之后——端口必须由更早的模块产出（注册序 = 依赖序），
    /// 否则引导期报"缺少产物 IDevSession"。
    /// </summary>
    internal sealed class DevSessionModule : IClientModule
    {
        public string Name => "DevSession";

        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            // 开发装配钩子给会话实现；无 Dev 程序集（发布形态）= 全关空实现。
            context.Put<IDevSession>(DevHooks.Current?.CreateSession() ?? NullDevSession.Instance);
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
