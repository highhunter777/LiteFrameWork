using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame;
using LiteGame.Modules.Dev;
using LiteGame.Modules.Input;
using LiteGame.Modules.Dev.FallbacksSubModule;

namespace LiteGame.Modules.Composition
{
    /// <summary>
    /// ③ 开发会话端口（M0 接缝 6）。
    /// </summary>
    public sealed class DevSessionSegment : IClientSegment
    {
        public string Name => "DevSession";

        public UniTask InitializeAsync(SegmentContext context, CancellationToken ct)
        {
            // 开发装配钩子给会话实现；无 Dev 程序集（发布形态）= 全关空实现。
            context.Put<IDevSession>(DevHooks.Current?.CreateSession() ?? NullDevSession.Instance);
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
