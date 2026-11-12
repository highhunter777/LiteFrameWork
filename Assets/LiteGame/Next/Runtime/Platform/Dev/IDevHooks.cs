using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteGameNext.Platform.Dev
{
    /// <summary>开发面钩子：hot 侧只认这个接口；Dev 程序集实现它，发布构建用 NullDev。</summary>
    public interface IDevHooks
    {
        /// <summary>是否启用（发布构建恒 false）。</summary>
        bool Enabled { get; }

        /// <summary>逐帧驱动（无开发面为 no-op）。</summary>
        void Tick(float deltaTime);

        /// <summary>开始一次本地房间会话（Dev 本地服）；返回可用会话或 null。</summary>
        UniTask<ILocalRoomSession> StartLocalRoomAsync(string roomId, int expectedPlayers,
            CancellationToken cancellationToken);

        /// <summary>执行一条 GM 命令；返回结果文本（未知命令返回错误说明）。</summary>
        string ExecuteCommand(string line);

        /// <summary>关闭开发面。</summary>
        void Shutdown();
    }

    /// <summary>本地房间会话（Dev 本地服的对外面）：拿传输给 RoomSession 用。</summary>
    public interface ILocalRoomSession : IDisposable
    {
        /// <summary>传输实例（传给 RoomSession）。</summary>
        object Transport { get; }

        /// <summary>房间号。</summary>
        string RoomId { get; }

        /// <summary>推进本地服（每帧）。</summary>
        void Tick(float deltaTime);
    }

    /// <summary>发布构建的空开发面：所有操作 no-op，不引用 Dev 程序集。</summary>
    public sealed class NullDevHooks : IDevHooks
    {
        public static readonly NullDevHooks Instance = new NullDevHooks();

        public bool Enabled => false;

        public void Tick(float deltaTime)
        {
        }

        public UniTask<ILocalRoomSession> StartLocalRoomAsync(string roomId, int expectedPlayers,
            CancellationToken cancellationToken) => UniTask.FromResult<ILocalRoomSession>(null);

        public string ExecuteCommand(string line) => "开发面未启用（发布构建）";

        public void Shutdown()
        {
        }
    }
}
