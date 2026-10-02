using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
using Xunit;

namespace MetaServer.IntegrationTests
{
    /// <summary>
    /// Mongo 测试夹具（Meta 专项 §14 L3"真实 HTTP + Mongo 测试容器"）。
    ///
    /// 环境解析（依次）：
    /// 1. `MONGO_TEST_URI` 环境变量（CI 显式指定，不依赖 docker）；
    /// 2. 本机 `127.0.0.1:27017` 探活；
    /// 3. docker：启动既有 `litegame-mongo` 容器（不存在则创建——单节点副本集 mongo:8.0；
    ///    事务要求副本集，单机模式跑不了 §14 的事务重试用例）；
    /// 4. 全部不可达 → <see cref="Available"/> = false，用例 <c>Assert.Skip</c> 显式跳过
    ///    （跳过必须被记录看见，不得静默假绿）。
    ///
    /// 每个夹具实例独立数据库（GUID 命名）——用例互不污染；Dispose 时 drop。
    /// </summary>
    public sealed class MongoFixture : IAsyncLifetime
    {
        /// <summary>测试容器名（按项目命名约定——重启测试只作用于这个专用容器，不碰别的）。</summary>
        public const string ContainerName = "litegame-mongo";

        public bool Available { get; private set; }

        public string Uri { get; private set; } = "mongodb://127.0.0.1:27017";

        /// <summary>
        /// 本夹具创建过的全部数据库名（Dispose 时统一清理）。
        /// xunit **每个用例新建测试类实例**——用例在构造器里取独立库名即得用例级隔离
        /// （同集合串行执行，互不污染）。
        /// </summary>
        private readonly System.Collections.Generic.List<string> _databases =
            new System.Collections.Generic.List<string>();

        public string CreateDatabaseName()
        {
            string name = "meta_l3_" + Guid.NewGuid().ToString("N");
            lock (_databases)
            {
                _databases.Add(name);
            }
            return name;
        }

        private IMongoClient _client;

        public async Task InitializeAsync()
        {
            string uri = Environment.GetEnvironmentVariable("MONGO_TEST_URI");
            if (!string.IsNullOrWhiteSpace(uri))
            {
                Uri = uri;
                var envClient = BuildClient(Uri, TimeSpan.FromSeconds(3));
                if (await TryPingAsync(envClient))
                {
                    _client = envClient;
                    Available = true;
                }
                return;
            }

            var probe = BuildClient(Uri, TimeSpan.FromSeconds(3));
            if (await TryPingAsync(probe))
            {
                Available = true;
                _client = probe;
                return;
            }

            if (await TryEnsureDockerContainerAsync())
            {
                // 容器刚起或副本集刚初始化：给选举与就绪留时间，最多 45s
                var client = BuildClient(Uri, TimeSpan.FromSeconds(3));
                for (int i = 0; i < 15; i++)
                {
                    if (await TryPingAsync(client))
                    {
                        _client = client;
                        Available = true;
                        return;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(3));
                }
            }
        }

        public async Task DisposeAsync()
        {
            if (Available && _client != null)
            {
                foreach (string database in _databases)
                {
                    try
                    {
                        await _client.DropDatabaseAsync(database);
                    }
                    catch
                    {
                        // 清理失败不掩盖测试结果；数据库随容器回收
                    }
                }
            }
        }

        /// <summary>新客户端（模拟"进程/连接重建"——介质比进程活得久的 L3 形态之一）。</summary>
        public IMongoClient CreateClient()
        {
            return BuildClient(Uri, TimeSpan.FromSeconds(10));
        }

        /// <summary>对指定库运行真实迁移到最新（适配器直连用例的前置）。</summary>
        public async Task MigrateToLatestAsync(string databaseName)
        {
            IMongoDatabase database = CreateClient().GetDatabase(databaseName);
            var runner = new MetaServer.Infrastructure.Persistence.SchemaMigrationRunner(
                MetaServer.Infrastructure.Persistence.Mongo.MongoMigrations.All(database),
                new MetaServer.Infrastructure.Persistence.Mongo.MongoSchemaVersionStore(database));
            var outcome = await runner.MigrateAsync(
                MetaServer.Infrastructure.Persistence.Mongo.MongoMigrations.LatestVersion,
                CancellationToken.None);
            Assert.True(outcome is MetaServer.Contracts.Persistence.MigrationOutcome.Completed _
                        || outcome is MetaServer.Contracts.Persistence.MigrationOutcome.UpToDate _,
                "前置迁移失败：" + outcome);
        }

        /// <summary>重启 Mongo 容器并等待 PRIMARY 就绪（重启恢复用例）。仅在 docker 可用时可用。</summary>
        public async Task<bool> TryRestartContainerAsync()
        {
            if (!Available)
            {
                return false;
            }

            if (await RunDockerAsync("inspect " + ContainerName, TimeSpan.FromSeconds(10)) == null)
            {
                return false;
            }

            if (await RunDockerAsync("restart " + ContainerName, TimeSpan.FromSeconds(60)) == null)
            {
                return false;
            }

            var client = BuildClient(Uri, TimeSpan.FromSeconds(5));
            for (int i = 0; i < 30; i++)
            {
                if (await TryPingAsync(client))
                {
                    return true;
                }
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
            return false;
        }

        private static IMongoClient BuildClient(string uri, TimeSpan selectionTimeout)
        {
            MongoClientSettings settings = MongoClientSettings.FromUrl(MongoUrl.Create(uri));
            settings.ServerSelectionTimeout = selectionTimeout;
            return new MongoClient(settings);
        }

        private static async Task<bool> TryPingAsync(IMongoClient client)
        {
            try
            {
                await client.GetDatabase("admin").RunCommandAsync(
                    new BsonDocumentCommand<MongoDB.Bson.BsonDocument>(
                        new MongoDB.Bson.BsonDocument("ping", 1)),
                    cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> TryEnsureDockerContainerAsync()
        {
            // 既有容器（含停止态）优先；否则创建并初始化副本集
            if (await RunDockerAsync("start " + ContainerName, TimeSpan.FromSeconds(30)) != null)
            {
                return true;
            }

            string created = await RunDockerAsync(
                "run -d --name " + ContainerName
                + " -p 127.0.0.1:27017:27017 mongo:8.0 mongod --replSet rs0 --bind_ip_all",
                TimeSpan.FromMinutes(5));
            if (created == null)
            {
                return false;
            }

            // 等待 mongod 接受连接后 rs.initiate（单节点副本集——事务要求）
            var probe = BuildClient(Uri, TimeSpan.FromSeconds(3));
            for (int i = 0; i < 10; i++)
            {
                if (await TryPingAsync(probe))
                {
                    break;
                }
                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            await RunDockerAsync(
                "exec " + ContainerName
                + " mongosh --quiet --eval \"try { rs.initiate({_id:'rs0',members:[{_id:0,host:'localhost:27017'}]}) } catch (e) { }\"",
                TimeSpan.FromSeconds(30));
            return true;
        }

        private static async Task<string> RunDockerAsync(string arguments, TimeSpan timeout)
        {
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(info);
                if (process == null)
                {
                    return null;
                }

                // 并发排空输出管道（等退出后再读，docker inspect 的 KB 级 JSON 会
                // 填满管道缓冲、进程写阻塞不退出 → 恒定超时 → docker 被误判不可用）
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();

                process.EnableRaisingEvents = true;
                var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                process.Exited += (_, _) => exited.TrySetResult(true);

                Task wait = await Task.WhenAny(exited.Task, Task.Delay(Timeout.InfiniteTimeSpan, new CancellationTokenSource(timeout).Token));
                if (wait != exited.Task)
                {
                    try { process.Kill(); } catch { }
                    return null;
                }

                string output = await stdout;
                _ = await stderr;
                if (process.ExitCode != 0)
                {
                    return null;
                }
                return output;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>共享 Mongo 夹具的用例集合。</summary>
    [CollectionDefinition("Mongo")]
    public sealed class MongoCollection : ICollectionFixture<MongoFixture>
    {
    }
}
