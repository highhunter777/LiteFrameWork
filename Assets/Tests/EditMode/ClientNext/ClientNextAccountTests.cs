using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using LiteGameNext.Features.Account;
using LiteGameNext.Platform.Meta;
using LiteGameNext.Platform.Ui;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>AccountFeature 真实登录：Meta 成功/失败/取消 + 无 Meta 回退假账号。</summary>
    public sealed class ClientNextAccountTests
    {
        [Test]
        public async Task 有Meta时_游客登录置令牌与账号()
        {
            var transport = new FakeTransport();
            transport.Enqueue(200, "{\"accountId\":\"acc\",\"accessToken\":\"tok\",\"tokenType\":\"Bearer\",\"expiresAtMs\":1}");
            var account = new AccountFeature(new UiRuntime(), new MetaClient(transport));

            bool ok = await account.SignInAsync();

            Assert.IsTrue(ok);
            Assert.IsTrue(account.IsSignedIn);
            Assert.AreEqual("tok", account.AccessToken);
            Assert.AreEqual("acc", account.AccountId);
            Assert.AreEqual(string.Empty, account.LastError);
        }

        [Test]
        public async Task 有Meta时_失败保留LastError且不置登录态()
        {
            var transport = new FakeTransport();
            transport.Enqueue(401, "{\"code\":\"auth.bad\",\"messageKey\":\"auth.bad\"}");
            var account = new AccountFeature(new UiRuntime(), new MetaClient(transport));

            bool ok = await account.SignInAsync();

            Assert.IsFalse(ok);
            Assert.IsFalse(account.IsSignedIn);
            Assert.AreEqual("auth.bad", account.LastError);
        }

        [Test]
        public async Task 无Meta时_回退假账号()
        {
            var account = new AccountFeature(new UiRuntime());

            bool ok = await account.SignInAsync();

            Assert.IsTrue(ok);
            Assert.IsTrue(account.IsSignedIn);
            Assert.AreEqual(string.Empty, account.AccessToken);
        }

        [Test]
        public async Task 取消_不置登录态()
        {
            var transport = new FakeTransport { ThrowOnSend = true };
            var account = new AccountFeature(new UiRuntime(), new MetaClient(transport));
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                try
                {
                    await account.SignInAsync(cts.Token);
                    Assert.Fail("应传播取消");
                }
                catch (OperationCanceledException)
                {
                }
            }

            Assert.IsFalse(account.IsSignedIn);
        }

        private sealed class FakeTransport : IMetaTransport
        {
            private readonly Queue<MetaResponse> _responses = new Queue<MetaResponse>();

            public bool ThrowOnSend { get; set; }

            public void Enqueue(long status, string body) => _responses.Enqueue(new MetaResponse(status, body));

            public UniTask<MetaResponse> SendAsync(MetaRequest request, CancellationToken cancellationToken)
            {
                if (ThrowOnSend) throw new OperationCanceledException(cancellationToken);
                return UniTask.FromResult(_responses.Count == 0
                    ? new MetaResponse(0, "no response")
                    : _responses.Dequeue());
            }
        }
    }
}
