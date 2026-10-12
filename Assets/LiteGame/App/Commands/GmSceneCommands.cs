#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>GM 场景命令·单场景加载（带参：location = 场景名/路径）。</summary>
    public sealed class GmLoadSingleSceneCommand : ICommand { public string Location; }

    /// <summary>GM 场景命令·叠加加载（带参：location）。</summary>
    public sealed class GmLoadAdditiveSceneCommand : ICommand { public string Location; }

    /// <summary>GM 场景命令·卸载当前单场景（无参——发现面可直接执行）。</summary>
    public sealed class GmUnloadSingleSceneCommand : ICommand { }

    /// <summary>GM 场景命令·卸载指定叠加场景（带参：location）。</summary>
    public sealed class GmUnloadAdditiveSceneCommand : ICommand { public string Location; }

    /// <summary>
    /// GM 场景命令处理器（带参+异步形态——《GM 与调试工具分层设计》§2 场景操作命令化批）：
    /// 场景加载机制验证的四操作经 <see cref="ISceneService"/> 执行，操作结果进命令审计环
    /// （"刚才动了哪个场景"可查）。处理器异常由 SendAsync 隔离为 HandlerFault（本处理器不自裹 try——
    /// 与处理器作者代码缺陷的框架契约一致）；<see cref="OperationCanceledException"/> 透传归调用方。
    /// </summary>
    public sealed class GmSceneCommandHandler :
        IAsyncCommandHandler<GmLoadSingleSceneCommand>,
        IAsyncCommandHandler<GmLoadAdditiveSceneCommand>,
        IAsyncCommandHandler<GmUnloadSingleSceneCommand>,
        IAsyncCommandHandler<GmUnloadAdditiveSceneCommand>
    {
        private readonly ISceneService _scenes;

        public GmSceneCommandHandler(ISceneService scenes)
            => _scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));

        async ValueTask<CommandResult> IAsyncCommandHandler<GmLoadSingleSceneCommand>.ExecuteAsync(
            GmLoadSingleSceneCommand command, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(command.Location))
                return CommandResult.Fail(CommandReject.HandlerRejected, "location 为空（场景名/路径必填）");
            await _scenes.LoadSingleAsync(command.Location, ct);
            return CommandResult.Success($"单场景已加载：{_scenes.SingleSceneName ?? command.Location}");
        }

        async ValueTask<CommandResult> IAsyncCommandHandler<GmLoadAdditiveSceneCommand>.ExecuteAsync(
            GmLoadAdditiveSceneCommand command, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(command.Location))
                return CommandResult.Fail(CommandReject.HandlerRejected, "location 为空（场景名/路径必填）");
            await _scenes.LoadAdditiveAsync(command.Location, ct);
            return CommandResult.Success($"叠加已加载：{command.Location}（共 {_scenes.AdditiveCount}）");
        }

        async ValueTask<CommandResult> IAsyncCommandHandler<GmUnloadSingleSceneCommand>.ExecuteAsync(
            GmUnloadSingleSceneCommand command, CancellationToken ct)
        {
            await _scenes.UnloadSingleAsync(ct);
            return CommandResult.Success($"单场景已卸载（现单场景={_scenes.SingleSceneName ?? "无"}）");
        }

        async ValueTask<CommandResult> IAsyncCommandHandler<GmUnloadAdditiveSceneCommand>.ExecuteAsync(
            GmUnloadAdditiveSceneCommand command, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(command.Location))
                return CommandResult.Fail(CommandReject.HandlerRejected, "location 为空（场景名/路径必填）");
            await _scenes.UnloadAdditiveAsync(command.Location, ct);
            return CommandResult.Success($"叠加已卸载：{command.Location}（剩 {_scenes.AdditiveCount}）");
        }
    }

    /// <summary>
    /// GM 场景命令注册面（装配点唯一入口）：四命令 GmOnly 注册进命令中心——发现面自动列出
    /// （带参命令由场景页专面板执行）、加载/卸载进审计环；返回合并注销器（调用方持有，随装配生命周期释放）。
    /// </summary>
    public static class GmSceneCommands
    {
        public static IDisposable RegisterAll(CommandCenter center, ISceneService scenes)
        {
            if (center == null) throw new ArgumentNullException(nameof(center));
            var handler = new GmSceneCommandHandler(scenes);
            var bag = new SubscriptionBag();
            bag.Add(center.RegisterAsync<GmLoadSingleSceneCommand>(handler,
                new CommandOptions(gmOnly: true, description: "单场景加载（卸旧加载新）")).Dispose);
            bag.Add(center.RegisterAsync<GmLoadAdditiveSceneCommand>(handler,
                new CommandOptions(gmOnly: true, description: "叠加加载场景")).Dispose);
            bag.Add(center.RegisterAsync<GmUnloadSingleSceneCommand>(handler,
                new CommandOptions(gmOnly: true, description: "卸载当前单场景")).Dispose);
            bag.Add(center.RegisterAsync<GmUnloadAdditiveSceneCommand>(handler,
                new CommandOptions(gmOnly: true, description: "卸载指定叠加场景")).Dispose);
            return bag;
        }
    }
}
#endif
