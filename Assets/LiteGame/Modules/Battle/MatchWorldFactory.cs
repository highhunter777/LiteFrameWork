using LiteNet.Proto;
using LiteNet.Protocol;
using LiteSim;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 对局世界构建：按服务器 seed/地图/房间规模重建同构 Sim 世界与输入模板。
    /// 两端世界构造必须逐位一致——本类只做确定性构造，不触达网络/视图/诊断。
    /// </summary>
    internal static class MatchWorldFactory
    {
        public static SimWorldState Build(StartGame start, SimMapData map, CombatValues values,
            int expectedPlayers, out SimInputFrame[] inputTemplate)
        {
            var world = new SimWorldState { RngState = (ulong)start.Seed };
            if (start.Match != null) world.Match = SnapshotReassembler.FromProto(start.Match);

            uint used = 0u;
            for (int i = 0; i < expectedPlayers; i++)
            {
                int spawnIndex = SpawnPointSelector.Pick(map, used,
                    SpawnPointSelector.InitialHash((ulong)start.Seed, start.MatchId, i));
                used |= 1u << spawnIndex;
                SimVector3 spawn = map.SpawnPoints[spawnIndex];
                world.Spawn(new EntitySlot
                {
                    Hp = values.EntityHp,
                    Pos = spawn,
                    Yaw = 0f,
                    Flags = EntityFlags.Player,
                    SpawnPointIndex = spawnIndex,
                }, out int _);
            }

            inputTemplate = new SimInputFrame[expectedPlayers];
            for (int i = 0; i < expectedPlayers; i++) inputTemplate[i].EntityId = i;   // 服务器覆写防伪；本地预测按 playerId 对齐
            return world;
        }
    }
}
