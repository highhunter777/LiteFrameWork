using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LiteGameNext.Features.Battle;
using LiteGameNext.Features.Battle.Hud;
using LiteGameNext.Features.Battle.Input;
using LiteNet;
using LiteNet.Transport;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LiteGame.Tests.PlayMode
{
    /// <summary>双人真实房间：两人进房→房主开局→开火被服务端消费（不保证命中）。</summary>
    public sealed class ClientNextRoomTwoPlayerE2ETests
    {
        [UnityTest]
        [Category("ClientNextE2E")]
        public IEnumerator 双人进房_开火被服务端消费()
        {
            string root = Directory.GetCurrentDirectory();
            string envPath = Path.Combine(root, "deploy", ".env");
            string ticketsPath = Path.Combine(root, "TestResults", "tickets.txt");
            if (!File.Exists(envPath) || !File.Exists(ticketsPath))
                Assert.Ignore("缺少 deploy/.env 或 TestResults/tickets.txt（本机 E2E 夹具）");

            string envelope = null;
            foreach (string raw in File.ReadAllLines(envPath))
            {
                string line = raw.Trim();
                if (line.StartsWith("ENVELOPE_PSK=", StringComparison.Ordinal))
                    envelope = line.Substring("ENVELOPE_PSK=".Length).Trim();
            }

            if (string.IsNullOrEmpty(envelope)) Assert.Ignore("ENVELOPE_PSK 未配置");
            string[] tickets = File.ReadAllLines(ticketsPath);
            if (tickets.Length < 2) Assert.Ignore("至少需要两张 ticket");

            var hud1 = new BattleHudModel();
            var hud2 = new BattleHudModel();
            using var s1 = Create(envelope, tickets[0], hud1, new IdleInput());
            using var s2 = Create(envelope, tickets[1], hud2);
            var aim = new AimAtInput(s1, s2);
            using var cts = new CancellationTokenSource(20000);
            Task<string> t1 = Pump(s1, cts.Token);
            Task<string> t2 = Pump(s2, cts.Token);
            float joinDeadline = Time.realtimeSinceStartup + 8f;
            while ((!s1.IsJoined || !s2.IsJoined) && Time.realtimeSinceStartup < joinDeadline)
                yield return null;
            Assert.IsTrue(s1.IsJoined && s2.IsJoined, "两人未都进房");
            s1.StartMatch();
            s2.StartMatch();
            float startDeadline = Time.realtimeSinceStartup + 8f;
            while ((!s1.Status.Contains("对局进行中") || !s2.Status.Contains("对局进行中"))
                   && Time.realtimeSinceStartup < startDeadline)
                yield return null;
            Assert.IsTrue(s1.Status.Contains("对局进行中") && s2.Status.Contains("对局进行中"),
                "未开局：" + s1.Status + " / " + s2.Status);
            yield return new WaitForSeconds(3.5f);
            s1.SetInput(aim);
            float fireDeadline = Time.realtimeSinceStartup + 5f;
            while (hud1.Ammo >= 30 && Time.realtimeSinceStartup < fireDeadline) yield return null;
            s1.TryGetOtherEntityPosition(out long otherId, out Vector3 otherPos);
            Assert.Less(hud1.Ammo, 30,
                "服务端未消费开火：ammo=" + hud1.Ammo + " sent=" + s1.SentInputs
                + " frame=" + s1.LastInputFrame + " ack=" + s1.LastAckInput
                + " other=" + otherId + "@" + otherPos);
            yield return new WaitForSeconds(8f);
            cts.Cancel();
            while (!t1.IsCompleted || !t2.IsCompleted) yield return null;
            if (t1.IsFaulted) throw t1.Exception;
            if (t2.IsFaulted) throw t2.Exception;
        }

        private static RoomSession Create(string envelope, string ticket, BattleHudModel hud,
            IBattleInput input = null)
        {
            IClientTransport transport = new SecureEnvelopeClientTransport(
                new KcpTransportClient(), SecureEnvelopeOptions.FromBase64(envelope));
            return new RoomSession("127.0.0.1", 17777, "lobby-default", ticket, BuildHash.Value,
                transport, hud: hud, input: input, autoStart: false);
        }

        private sealed class IdleInput : IBattleInput
        {
            public BattleInputState Read() => default;
        }

        private sealed class AimAtInput : IBattleInput
        {
            private readonly RoomSession _shooter;
            private readonly RoomSession _target;

            public AimAtInput(RoomSession shooter, RoomSession target)
            {
                _shooter = shooter;
                _target = target;
            }

            public BattleInputState Read()
            {
                bool hasSelf = _shooter.TryGetEntityPosition(_shooter.LocalEntityId, out Vector3 self);
                bool hasTarget = _target.TryGetEntityPosition(_target.LocalEntityId, out Vector3 position);
                if (!hasTarget) return default;
                Vector3 point = position + Vector3.up * 1.6f;
                float moveX = 0f;
                float moveZ = 0f;
                if (hasSelf)
                {
                    Vector3 delta = position - self;
                    delta.y = 0f;
                    if (delta.sqrMagnitude > 4f)
                    {
                        delta.Normalize();
                        moveX = delta.x;
                        moveZ = delta.z;
                    }
                }

                return new BattleInputState(moveX, moveZ, fire: true, aim: false, point);
            }
        }

        private static async Task<string> Pump(RoomSession session, CancellationToken cancellationToken)
        {
            try
            {
                BattleResult result = await session.RunAsync(cancellationToken);
                return "结束 " + result.Outcome;
            }
            catch (OperationCanceledException)
            {
                return "取消 " + session.Status;
            }
        }

    }
}
