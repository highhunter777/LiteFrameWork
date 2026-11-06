#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>GM 场景命令·单场景加载（带参：location = 场景名/路径）。</summary>
    public sealed class GMLoadSingleSceneCommand : ICommand { public string Location; }

    /// <summary>GM 场景命令·叠加加载（带参：location）。</summary>
    public sealed class GMLoadAdditiveSceneCommand : ICommand { public string Location; }

    /// <summary>GM 场景命令·卸载当前单场景（无参——发现面可直接执行）。</summary>
    public sealed class GMUnloadSingleSceneCommand : ICommand { }

    /// <summary>GM 场景命令·卸载指定叠加场景（带参：location）。</summary>
    public sealed class GMUnloadAdditiveSceneCommand : ICommand { public string Location; }

    /// <summary>
    /// GM 场景命令处理器（带参+异步形态——《GM 与调试工具分层设计》§2 场景操作命令化批）： 场景加载机制验证的四操作经 ISceneService 执行，操作结果进命令审计环 （"刚才动了哪个场景"可查）。
    /// </summary>
    public sealed class GMSceneCommandHandler :
        IAsyncCommandHandler<GMLoadSingleSceneCommand>,
        IAsyncCommandHandler<GMLoadAdditiveSceneCommand>,
        IAsyncCommandHandler<GMUnloadSingleSceneCommand>,
        IAsyncCommandHandler<GMUnloadAdditiveSceneCommand>
    {
        private readonly ISceneService _scenes;

        public GMSceneCommandHandler(ISceneService scenes)
            => _scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));

        async ValueTask<CommandResult> IAsyncCommandHandler<GMLoadSingleSceneCommand>.ExecuteAsync(
            GMLoadSingleSceneCommand command, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(command.Location))
                return CommandResult.Fail(CommandReject.HandlerRejected, "location 为空（场景名/路径必填）");
            await _scenes.LoadSingleAsync(command.Location, ct);
            return CommandResult.Success($"单场景已加载：{_scenes.SingleSceneName ?? command.Location}");
        }

        async ValueTask<CommandResult> IAsyncCommandHandler<GMLoadAdditiveSceneCommand>.ExecuteAsync(
            GMLoadAdditiveSceneCommand command, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(command.Location))
                return CommandResult.Fail(CommandReject.HandlerRejected, "location 为空（场景名/路径必填）");
            await _scenes.LoadAdditiveAsync(command.Location, ct);
            return CommandResult.Success($"叠加已加载：{command.Location}（共 {_scenes.AdditiveCount}）");
        }

        async ValueTask<CommandResult> IAsyncCommandHandler<GMUnloadSingleSceneCommand>.ExecuteAsync(
            GMUnloadSingleSceneCommand command, CancellationToken ct)
        {
            await _scenes.UnloadSingleAsync(ct);
            return CommandResult.Success($"单场景已卸载（现单场景={_scenes.SingleSceneName ?? "无"}）");
        }

        async ValueTask<CommandResult> IAsyncCommandHandler<GMUnloadAdditiveSceneCommand>.ExecuteAsync(
            GMUnloadAdditiveSceneCommand command, CancellationToken ct)
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
    public static class GMSceneCommands
    {
        public static IDisposable RegisterAll(CommandCenter center, ISceneService scenes)
        {
            if (center == null) throw new ArgumentNullException(nameof(center));
            var handler = new GMSceneCommandHandler(scenes);
            var bag = new SubscriptionBag();
            bag.Add(center.RegisterAsync<GMLoadSingleSceneCommand>(handler,
                new CommandOptions(gmOnly: true, description: "单场景加载（卸旧加载新）")).Dispose);
            bag.Add(center.RegisterAsync<GMLoadAdditiveSceneCommand>(handler,
                new CommandOptions(gmOnly: true, description: "叠加加载场景")).Dispose);
            bag.Add(center.RegisterAsync<GMUnloadSingleSceneCommand>(handler,
                new CommandOptions(gmOnly: true, description: "卸载当前单场景")).Dispose);
            bag.Add(center.RegisterAsync<GMUnloadAdditiveSceneCommand>(handler,
                new CommandOptions(gmOnly: true, description: "卸载指定叠加场景")).Dispose);
            return bag;
        }
    }
}
#endif
