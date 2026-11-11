using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LiteGameNext.Features.Battle;
using LiteGameNext.Features.Battle.Hud;
using LiteNet;
using LiteNet.Transport;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LiteGame.Tests.PlayMode
{
    /// <summary>本机真实房间会话冒烟（需本地 RoomServer + deploy/.env + ticket；缺文件则 Ignore）。</summary>
    public sealed class ClientNextRoomE2ETests
    {
        [UnityTest]
        public IEnumerator 真实房间_JoinStart快照()
        {
            string root = Directory.GetCurrentDirectory();
            string envPath = Path.Combine(root, "deploy", ".env");
            string ticketPath = Path.Combine(root, "TestResults", "ticket.txt");
            if (!File.Exists(envPath) || !File.Exists(ticketPath))
                Assert.Ignore("缺少 deploy/.env 或 TestResults/ticket.txt（本机 E2E 夹具）");

            string envelope = null;
            foreach (string raw in File.ReadAllLines(envPath))
            {
                string line = raw.Trim();
                if (line.StartsWith("ENVELOPE_PSK=", StringComparison.Ordinal))
                    envelope = line.Substring("ENVELOPE_PSK=".Length).Trim();
            }

            if (string.IsNullOrEmpty(envelope)) Assert.Ignore("ENVELOPE_PSK 未配置");
            string ticket = File.ReadAllText(ticketPath).Trim();
            Task<string> run = RunAsync(envelope, ticket);
            while (!run.IsCompleted) yield return null;
            if (run.IsFaulted) throw run.Exception;
            Assert.IsTrue(run.Result.Contains("对局进行中") || run.Result.Contains("对局结束"),
                "未进房/开局：" + run.Result);
        }

        private static async Task<string> RunAsync(string envelope, string ticket)
        {
            IClientTransport transport = new SecureEnvelopeClientTransport(
                new KcpTransportClient(), SecureEnvelopeOptions.FromBase64(envelope));
            var hud = new BattleHudModel();
            using var cts = new CancellationTokenSource(8000);
            using var session = new RoomSession("127.0.0.1", 17777, "lobby-default", ticket,
                LiteNet.BuildHash.Value, transport, hud: hud);
            try
            {
                BattleResult result = await session.RunAsync(cts.Token);
                return "对局结束 " + result.Outcome + " " + result.Reason;
            }
            catch (OperationCanceledException)
            {
                return "status=" + session.Status + " hp=" + hud.Health + " ammo=" + hud.Ammo;
            }
        }
    }
}
