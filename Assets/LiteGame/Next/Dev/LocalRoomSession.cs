using System;
using LiteFrameworkNext;
using LiteGame.Adapters.Dev.LocalServer;
using LiteGame.Modules.Config;
using LiteGameNext.Platform.Config;
using LiteGameNext.Platform.Dev;
using LiteSim;
using UnityEngine;

namespace LiteGameNext.Dev
{
    /// <summary>本地房间会话：进程内真 RoomRuntime（无 KCP）；判定数值与规则来自配置表。</summary>
    internal sealed class LocalRoomSession : ILocalRoomSession
    {
        private readonly LocalServerTransport _transport;

        /// <param name="config">配置表服务；null = 直构形态，判定数值与地图回落 C# 默认。</param>
        public LocalRoomSession(string roomId, int expectedPlayers, ConfigService config = null)
        {
            RoomId = string.IsNullOrEmpty(roomId) ? "local-dev" : roomId;
            cfg.Tables tables = config?.Tables;
            SimMapData map = tables == null ? BuildFlatMap() : MapTableProjection.Project(tables);
            // 表值优先：把「注释说等于表值」的人工同步变成机制单源。
            CombatValues? values = config == null ? null : config.TableCombatValues;
            MatchRules rules = config == null ? MatchRules.Default : config.Rules(MatchRules.Default);
            // 摘要基值必须与客户端 ExpectedConfigHash() 同源，否则 StartGame.ConfigHash 比对会误拒局。
            // 服务端侧 Digest = WithTestRules(基值, values.NoDeath, values.InfiniteAmmo)，故这里也传基值。
            uint digestBase = 0u;
            if (config != null)
            {
                try
                {
                    digestBase = config.ComputeGameplayDigestBase();
                }
                catch (InvalidDataException ex)
                {
                    Log.Warning("[Dev] 摘要无法复算，本地服以 0 启动：" + ex.Message);
                }
            }

            _transport = LocalServerTransport.ForRoom(RoomId, Math.Max(1, expectedPlayers), null, digestBase, map,
                roomValues: values, rules: rules);
            _transport.Connect("local", 0);
            Log.Info("[Dev] 本地房间数值来源：" + (values.HasValue ? "配置表" : "C# 默认")
                + "，地图来源：" + (tables == null ? "内置平坦图" : "tbmap"));
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

        /// <summary>平坦兜底图：2 个出生点相距 6m（表未装载时用）。</summary>
        private static SimMapData BuildFlatMap()
        {
            var map = new SimMapData { GroundY = 0f, HalfWidth = 20f, HalfDepth = 20f, SpawnPointCount = 2 };
            map.SpawnPoints[0] = new SimVector3(0f, 0f, -3f);
            map.SpawnPoints[1] = new SimVector3(0f, 0f, 3f);
            return map;
        }
    }
}
