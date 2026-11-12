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
    /// <summary>进程内真房间（kill_limit=1）：开火击杀→MatchEnded→Completed+结算行。</summary>
    public sealed class ClientNextLocalKillE2ETests
    {
        [UnityTest]
        public IEnumerator 本地真房间_击杀自然结算()
        {
            var map = new SimMapData { GroundY = 0f, HalfWidth = 10f, HalfDepth = 10f, SpawnPointCount = 2 };
            map.SpawnPoints[0] = new SimVector3(0f, 0f, -3f);
            map.SpawnPoints[1] = new SimVector3(0f, 0f, 3f);
            var rules = new MatchRules(durationFrames: 0, killLimit: 1, respawnDelayFrames: 60,
                spawnProtectionFrames: 0);
            using var transport = LocalServerTransport.ForRoom("local-kill", expectedPlayers: 2,
                map: map, rules: rules);
            var hud = new BattleHudModel();
            var input = new AimAtOtherInput();
            using var session = new RoomSession("local", 17777, "local-kill", string.Empty,
                LiteNet.BuildHash.Value, transport, hud: hud, input: input, autoStart: false);
            input.Session = session;
            using var cts = new CancellationTokenSource(15000);
            Task<BattleResult> run = Pump(session, cts.Token);
            float readyDeadline = Time.realtimeSinceStartup + 5f;
            while (hud.Ammo <= 0 && Time.realtimeSinceStartup < readyDeadline) yield return null;
            Assert.Greater(hud.Ammo, 0, "本地房间未开局");
            input.Enabled = true;
            float endDeadline = Time.realtimeSinceStartup + 8f;
            while (!run.IsCompleted && Time.realtimeSinceStartup < endDeadline) yield return null;
            Assert.IsTrue(run.IsCompleted, "击杀后未收到 MatchEnded");
            Assert.AreEqual(BattleOutcome.Completed, run.Result.Outcome);
            Assert.Greater(run.Result.Settlement.Count, 0, "结算行缺失");
        }

        private static async Task<BattleResult> Pump(RoomSession session, CancellationToken cancellationToken)
        {
            return await session.RunAsync(cancellationToken);
        }

        private sealed class AimAtOtherInput : IBattleInput
        {
            public RoomSession Session { get; set; }

            public bool Enabled { get; set; }

            public BattleInputState Read()
            {
                if (!Enabled || Session == null) return default;
                return Session.TryGetOtherEntityPosition(out _, out Vector3 position)
                    ? new BattleInputState(0f, 0f, fire: true, aim: false, position + Vector3.up * 1.6f)
                    : default;
            }
        }
    }
}
