#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using LiteFramework;
using LiteNet.Transport;
using LiteSim;
using UnityEngine;
using LiteGame.Modules.Map;

namespace LiteGame.Adapters.Dev
{
    /// <summary>
    /// 本地服传输工厂（开发装配）：把"进程内本地服"从 <see cref="ProcedureMatch"/> 里搬出来——
    /// App 不引用 `LiteGame.Dev`/`RoomServer.*`，发布依赖图因此不含服务端程序集。
    ///
    /// 装订：`RuntimeInitializeOnLoadMethod(BeforeSceneLoad)` 在引导前登记到
    /// <see cref="LocalTransportRegistry"/>（本程序集按三宏约束编译，发布包不参与）。
    /// </summary>
    internal sealed class DevLocalTransportFactory : ILocalTransportFactory
    {
        private static readonly DevLocalTransportFactory Instance = new DevLocalTransportFactory();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            LocalTransportRegistry.Bind(Instance);
            Application.quitting += () => LocalTransportRegistry.Unbind(Instance);
        }

        public IClientTransport Create(bool testRoom, MatchTestRules rules,
            Func<uint> gameplayDigestBase, IMapProvider maps)
        {
            if (rules == null)
                throw new InvalidOperationException(
                    "本地测试模式缺每局规则持有件——MatchRulesSeeder.Seed(testRoom) 未执行（装配序错）");
            if (maps == null)
                throw new InvalidOperationException(
                    "本地服务器形态缺地图提供者（装配根注入 IMapProvider——D22：无回退地图）");

            Log.Info("[Match] **本地测试模式**：本地服 Room-Test + 全房免死（Hp 保底 1、目标不消失——测试专用）",
                LogTag.Procedure);
            int players = 1 + Mathf.Max(0, TestModeRuntime.Current.BotCount);
            int mapId = BattleScenes.LocalServerMapId(testRoom: true);   // 本地测试模式 = 训练图
            return LocalServerTransport.ForRoom(ProcedureMatch.TestRoomId, players, rules,
                gameplayDigestBase?.Invoke() ?? 0u,   // 房间身份与客户端同源（同进程同一份装载——批 E）
                maps.GetMap(mapId), mapId,            // 判定图与客户端逐位同图 ＋ 行键随 JoinAck 回执（多图批）
                TestModeRuntime.Current);             // 本地测试钩子（开发面唯一实例）
        }
    }
}
#endif
