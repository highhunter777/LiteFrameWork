using System;
using LiteGame.Adapters.Dev.LocalServer;
using LiteGameNext.Platform.Dev;
using LiteSim;
using UnityEngine;

namespace LiteGameNext.Dev
{
    /// <summary>本地房间会话：进程内真 RoomRuntime（无 KCP），把传输交给 RoomSession 消费。</summary>
    internal sealed class LocalRoomSession : ILocalRoomSession
    {
        private readonly LocalServerTransport _transport;

        public LocalRoomSession(string roomId, int expectedPlayers)
        {
            RoomId = string.IsNullOrEmpty(roomId) ? "local-dev" : roomId;
            SimMapData map = BuildFlatMap();
            _transport = LocalServerTransport.ForRoom(RoomId, Math.Max(1, expectedPlayers), null, 0u, map);
            _transport.Connect("local", 0);
        }

        public object Transport => _transport;

        public string RoomId { get; }

        /// <summary>本地服没有网络泵；推进时只需驱动传输的收发（真 RoomRuntime 在 Tick 内步进）。</summary>
        public void Tick(float deltaTime)
        {
            _transport.TickIncoming();
            _transport.TickOutgoing();
        }

        public void Dispose() => _transport.Dispose();

        /// <summary>平坦测试图：2 个出生点相距 6m（无遮挡，验命中用）。</summary>
        private static SimMapData BuildFlatMap()
        {
            var map = new SimMapData { GroundY = 0f, HalfWidth = 20f, HalfDepth = 20f, SpawnPointCount = 2 };
            map.SpawnPoints[0] = new SimVector3(0f, 0f, -3f);
            map.SpawnPoints[1] = new SimVector3(0f, 0f, 3f);
            return map;
        }
    }
}
