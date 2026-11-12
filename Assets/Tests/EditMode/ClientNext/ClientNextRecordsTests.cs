using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGameNext.Features.Account;
using LiteGameNext.Features.Records;
using LiteGameNext.Platform.Meta;
using LiteGameNext.Platform.Ui;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>战绩功能：列表/空/失败/真实 prefab；只读 Meta，不写结算。</summary>
    public sealed class ClientNextRecordsTests
    {
        [Test]
        public void 刷新战绩_成功写Matches()
        {
            var transport = new FakeTransport();
            transport.Enqueue(200, "{\"matches\":[{\"matchId\":\"m1\",\"kills\":3,\"deaths\":1}]}");
            var ui = new UiRuntime();
            ui.Initialize();
            var account = new AccountFeature(ui);
            account.SignInAsGuest();
            var feature = new RecordsFeature(ui, account, new MetaClient(transport));
            try
            {
                bool ok = feature.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();
                Assert.IsTrue(ok);
                Assert.AreEqual(1, feature.Matches.Count);
                Assert.AreEqual("m1", feature.Matches[0].MatchId);
            }
            finally
            {
                feature.Dispose();
                ui.Shutdown();
            }
        }

        [Test]
        public void 空战绩_状态为暂无()
        {
            var transport = new FakeTransport();
            transport.Enqueue(200, "{\"matches\":[]}");
            var ui = new UiRuntime();
            ui.Initialize();
            var account = new AccountFeature(ui);
            account.SignInAsGuest();
            var feature = new RecordsFeature(ui, account, new MetaClient(transport));
            try
            {
                Assert.IsTrue(feature.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult());
                Assert.AreEqual("暂无战绩", feature.Status);
            }
            finally
            {
                feature.Dispose();
                ui.Shutdown();
            }
        }

        [Test]
        public void 失败_保留失败状态()
        {
            var transport = new FakeTransport();
            transport.Enqueue(401, "{\"code\":\"auth.bad\",\"messageKey\":\"auth.bad\"}");
            var ui = new UiRuntime();
            ui.Initialize();
            var account = new AccountFeature(ui);
            account.SignInAsGuest();
            var feature = new RecordsFeature(ui, account, new MetaClient(transport));
            try
            {
                Assert.IsFalse(feature.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult());
                StringAssert.Contains("战绩读取失败", feature.Status);
            }
            finally
            {
                feature.Dispose();
                ui.Shutdown();
            }
        }

        [Test]
        public void 打开战绩页_真实prefab可装载()
        {
            var transport = new FakeTransport();
            transport.Enqueue(200, "{\"matches\":[]}");
            var ui = new UiRuntime(ClientNextTestPages.PageFactory());
            ui.Initialize();
            var account = new AccountFeature(ui);
            account.SignInAsGuest();
            var feature = new RecordsFeature(ui, account, new MetaClient(transport));
            try
            {
                feature.OpenAsync(CancellationToken.None).GetAwaiter().GetResult();
                Assert.IsInstanceOf<RecordsPage>(ui.Top.Page);
            }
            finally
            {
                feature.Dispose();
                ui.Shutdown();
            }
        }

        private sealed class FakeTransport : IMetaTransport
        {
            private readonly Queue<MetaResponse> _responses = new Queue<MetaResponse>();

            public void Enqueue(long status, string body) => _responses.Enqueue(new MetaResponse(status, body));

            public UniTask<MetaResponse> SendAsync(MetaRequest request, CancellationToken cancellationToken)
                => UniTask.FromResult(_responses.Count == 0
                    ? new MetaResponse(0, "no response")
                    : _responses.Dequeue());
        }
    }
}
