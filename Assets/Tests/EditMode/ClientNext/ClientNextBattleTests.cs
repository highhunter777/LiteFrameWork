using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGameNext.Features.Battle;
using LiteGameNext.Features.Matchmaking;
using LiteGameNext.Platform.Ui;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>BattleFeature 边界：RoomPage 必开必关；结果原样返回；取消仍清理。</summary>
    public sealed class ClientNextBattleTests
    {
        [Test]
        public void 对局成功_返回Completed并关闭房间页()
        {
            var ui = new UiRuntime(EditorPageFactory.CreateAsync);
            ui.Initialize();
            var feature = new BattleFeature(ui, (match, hud, ct) =>
                UniTask.FromResult(BattleResult.Completed("p1")));
            try
            {
                BattleResult result = feature.RunAsync(Match(), CancellationToken.None).GetAwaiter().GetResult();
                Assert.IsTrue(result.IsSuccess);
                Assert.AreEqual(BattleOutcome.Completed, result.Outcome);
                Assert.AreEqual(0, ui.OpenPageCount, "房间页必须关闭");
            }
            finally
            {
                feature.Dispose();
                ui.Shutdown();
            }
        }

        [Test]
        public void 对局失败_原样返回SessionFailed()
        {
            var ui = new UiRuntime(EditorPageFactory.CreateAsync);
            ui.Initialize();
            var feature = new BattleFeature(ui, (match, hud, ct) =>
                UniTask.FromResult(BattleResult.Failed("connect refused")));
            try
            {
                BattleResult result = feature.RunAsync(Match(), CancellationToken.None).GetAwaiter().GetResult();
                Assert.AreEqual(BattleOutcome.SessionFailed, result.Outcome);
                Assert.AreEqual("connect refused", result.Reason);
                Assert.AreEqual(0, ui.OpenPageCount);
            }
            finally
            {
                feature.Dispose();
                ui.Shutdown();
            }
        }

        [Test]
        public void 取消_传播且关闭房间页()
        {
            var ui = new UiRuntime(EditorPageFactory.CreateAsync);
            ui.Initialize();
            var feature = new BattleFeature(ui, (match, hud, ct) =>
                throw new OperationCanceledException(ct));
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            try
            {
                feature.RunAsync(Match(), cts.Token).GetAwaiter().GetResult();
                Assert.Fail("应传播取消");
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                Assert.AreEqual(0, ui.OpenPageCount, "取消也必须关闭房间页");
                feature.Dispose();
                ui.Shutdown();
            }
        }

        private static MatchInfo Match()
            => new MatchInfo("instance", "127.0.0.1:7777", "room", "ticket", 1);
    }
}
