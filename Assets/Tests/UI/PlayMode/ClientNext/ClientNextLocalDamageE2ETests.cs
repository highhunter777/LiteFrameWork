using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using LiteGame.Adapters.Dev.LocalServer;
using LiteGameNext.Features.Battle;
using LiteGameNext.Features.Battle.Hud;
using LiteGameNext.Features.Battle.Input;
using LiteSim;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LiteGame.Tests.PlayMode
{
    /// <summary>进程内真房间内核（无 KCP/无遮挡）：直接读 bot 权威 HP，验证开火→伤害。</summary>
    public sealed class ClientNextLocalDamageE2ETests
    {
        [UnityTest]
        [Category("ClientNextE2E")]
        public IEnumerator 本地真房间_无遮挡开火产生权威命中事件()
        {
            var map = new SimMapData { GroundY = 0f, HalfWidth = 10f, HalfDepth = 10f, SpawnPointCount = 2 };
            map.SpawnPoints[0] = new SimVector3(0f, 0f, -3f);
            map.SpawnPoints[1] = new SimVector3(0f, 0f, 3f);
            using var transport = LocalServerTransport.ForRoom("local-damage", expectedPlayers: 2, map: map);
            var hud = new BattleHudModel();
            var input = new AimAtOtherInput();
            using var session = new RoomSession("local", 17777, "local-damage", string.Empty,
                LiteNet.BuildHash.Value, transport, hud: hud, input: input, autoStart: false);
            input.Session = session;
            using var cts = new CancellationTokenSource(15000);
            Task<BattleResult> run = RunAsync(session, cts.Token);
            float readyDeadline = Time.realtimeSinceStartup + 5f;
            while (hud.Ammo <= 0 && Time.realtimeSinceStartup < readyDeadline) yield return null;
            Assert.Greater(hud.Ammo, 0, "本地房间未开局");
            input.Enabled = true;

            // 判据只看权威战斗事件（CombatEventStats），不信 HUD 数字（Q12 改判）。
            float hitDeadline = Time.realtimeSinceStartup + 8f;
            while (session.Combat.HitEvents == 0 && Time.realtimeSinceStartup < hitDeadline) yield return null;

            Assert.Greater(session.Combat.FireEvents, 0L,
                "服务端未收到开火事件（sent=" + session.SentInputs + "）");
            Assert.Greater(session.Combat.HitEvents, 0L,
                "权威战斗事件里没有命中：fire=" + session.Combat.FireEvents
                + " obstacle=" + session.Combat.ObstacleHits
                + " hits=" + session.Combat.HitEvents);
            Assert.Greater(session.Combat.HitsDealtBy(session.LocalEntityId), 0,
                "本地实体应有造成命中的记录");

            cts.Cancel();
            while (!run.IsCompleted) yield return null;
        }

        private static async Task<BattleResult> RunAsync(RoomSession session, CancellationToken cancellationToken)
        {
            try
            {
                return await session.RunAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return BattleResult.Failed("cancel");
            }
        }

        private sealed class AimAtOtherInput : IBattleInput
        {
            public RoomSession Session { get; set; }

            public bool Enabled { get; set; }

            public BattleInputState Read()
            {
                if (!Enabled || Session == null) return default;
                // 瞄躯干中心（+1.0m）：落在身体判定带 [0, 1.449] 内，命中截面远大于 0.15m 的头部带。
                return Session.TryGetOtherEntityPosition(out _, out Vector3 position)
                    ? new BattleInputState(0f, 0f, fire: true, aim: false, position + Vector3.up * 1.0f)
                    : default;
            }
        }
    }
}
