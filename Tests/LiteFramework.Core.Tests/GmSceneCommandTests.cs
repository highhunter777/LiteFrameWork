using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// GM 场景命令用例（《GM 与调试工具分层设计》§2 场景操作命令化批）：命令族/处理器/注册面按
    /// 源链接纳入 L1（见 csproj 链接组注释）。钉住：四命令派发到对应服务操作、带参命令空 location
    /// 拒绝且不派发、GmOnly 注册过权限门（release 语义零派发）、发现面/注销器对称、审计进环。
    /// SceneService 真实现（YooAsset 适配器）归 L2——本测试用替身只记调用。
    /// </summary>
    public sealed class GmSceneCommandTests
    {

        private readonly ReferencePoolService _pool = new ReferencePoolService();        /// <summary>场景服务替身：记录四操作调用与实参（验证派发协议，不实现加载）。</summary>
        private sealed class FakeSceneService : ISceneService
        {
            public int SingleLoads; public string LastSingleLoad;
            public int AdditiveLoads; public string LastAdditiveLoad;
            public int SingleUnloads;
            public int AdditiveUnloads; public string LastAdditiveUnload;

            public bool IsLoaded(string location) => false;
            public string SingleSceneName => "FakeMain";
            public int AdditiveCount => 2;
            public IReadOnlyCollection<string> AdditiveLocations => Array.Empty<string>();
            public float Progress => 0f;                             // 替身不加载——按接口口径"无加载为 0"

            public UniTask LoadSingleAsync(string location, CancellationToken ct = default)
            { SingleLoads++; LastSingleLoad = location; return UniTask.CompletedTask; }
            public UniTask UnloadSingleAsync(CancellationToken ct = default)
            { SingleUnloads++; return UniTask.CompletedTask; }
            public UniTask LoadAdditiveAsync(string location, CancellationToken ct = default)
            { AdditiveLoads++; LastAdditiveLoad = location; return UniTask.CompletedTask; }
            public UniTask UnloadAdditiveAsync(string location, CancellationToken ct = default)
            { AdditiveUnloads++; LastAdditiveUnload = location; return UniTask.CompletedTask; }
            public void ReleaseAll() { }
        }

        [Fact]
        public async Task 四命令_各派发到对应服务操作_审计进环()
        {
            var commands = new CommandCenter(_pool, gmEnabled: true);          // 三宏内语义（L1 带 LITEFRAMEWORK_DEBUG）
            var scenes = new FakeSceneService();
            using (GmSceneCommands.RegisterAll(commands, scenes))
            {
                Assert.True((await commands.SendAsync(new GmLoadSingleSceneCommand { Location = "LevelA" })).Ok);
                Assert.True((await commands.SendAsync(new GmLoadAdditiveSceneCommand { Location = "LevelB" })).Ok);
                Assert.True((await commands.SendAsync(new GmUnloadSingleSceneCommand())).Ok);
                Assert.True((await commands.SendAsync(new GmUnloadAdditiveSceneCommand { Location = "LevelB" })).Ok);

                Assert.Equal(1, scenes.SingleLoads);                     // 各操作恰好派发一次
                Assert.Equal("LevelA", scenes.LastSingleLoad);
                Assert.Equal(1, scenes.AdditiveLoads);
                Assert.Equal("LevelB", scenes.LastAdditiveLoad);
                Assert.Equal(1, scenes.SingleUnloads);
                Assert.Equal(1, scenes.AdditiveUnloads);
                Assert.Equal("LevelB", scenes.LastAdditiveUnload);

                var audit = commands.GetAuditLog();                      // 四次执行全进审计环（旧→新）
                Assert.Equal(4, audit.Count);
                Assert.Equal(typeof(GmLoadSingleSceneCommand), audit[0].CommandType);
                Assert.Equal(typeof(GmUnloadAdditiveSceneCommand), audit[3].CommandType);
                Assert.All(audit, e => Assert.True(e.Ok));
            }
        }

        [Fact]
        public async Task 空location_带参命令拒绝且不派发()
        {
            var commands = new CommandCenter(_pool, gmEnabled: true);
            var scenes = new FakeSceneService();
            using (GmSceneCommands.RegisterAll(commands, scenes))
            {
                CommandResult r1 = await commands.SendAsync(new GmLoadSingleSceneCommand { Location = null });
                CommandResult r2 = await commands.SendAsync(new GmLoadAdditiveSceneCommand { Location = "  " });
                CommandResult r3 = await commands.SendAsync(new GmUnloadAdditiveSceneCommand { Location = "" });

                Assert.False(r1.Ok);                                     // 参数校验在处理器内拒绝
                Assert.Equal(CommandReject.HandlerRejected, r1.Reason);
                Assert.False(r2.Ok);
                Assert.False(r3.Ok);
                Assert.Equal(0, scenes.SingleLoads + scenes.AdditiveLoads + scenes.AdditiveUnloads);   // 零派发
            }
        }

        [Fact]
        public async Task 权限门_GmOnly在release语义下拦截_服务零调用()
        {
            var commands = new CommandCenter(_pool, gmEnabled: false);          // release 语义（构造注入可测接缝）
            var scenes = new FakeSceneService();
            using (GmSceneCommands.RegisterAll(commands, scenes))
            {
                CommandResult result = await commands.SendAsync(new GmUnloadSingleSceneCommand());

                Assert.False(result.Ok);                                 // GM 能力三宏外物理隔离
                Assert.Equal(CommandReject.GmBlocked, result.Reason);
                Assert.Equal(0, scenes.SingleUnloads);                   // 物理不执行
            }
        }

        [Fact]
        public void 注册面_四命令GmOnly进发现面_注销器对称清空()
        {
            var commands = new CommandCenter(_pool, gmEnabled: true);
            var scenes = new FakeSceneService();
            IDisposable un = GmSceneCommands.RegisterAll(commands, scenes);

            Assert.Equal(4, commands.GetRegisteredInfos().Count);        // 发现面自动列出（GM 面板据此渲染）
            Assert.All(commands.GetRegisteredInfos(), i => Assert.True(i.GmOnly));

            un.Dispose();
            Assert.Empty(commands.GetRegisteredInfos());                  // 注销对称——重复注册不撞唯一处理器契约
            GmSceneCommands.RegisterAll(commands, scenes);               // 注销后方可重新注册
        }
    }
}
