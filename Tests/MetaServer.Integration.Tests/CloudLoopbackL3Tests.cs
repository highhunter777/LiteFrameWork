using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LiteNet;
using LiteNet.Protocol;
using LiteNet.Transport;
using LiteSim;
using LiteSim.Tests;
using LiteTesting;
using MetaServer.Contracts.Lobby;
using MetaServer.Modules.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;

namespace MetaServer.IntegrationTests
{
    /// <summary>
    /// 上云测试回环（《上云测试专项设计》§3 批C 的服务端侧等价物）：**在本机把"云上"那条链完整跑一遍**。
    ///
    /// 链路（每一段都是真实实现，无替身）：
    /// <list type="number">
    /// <item>Meta 真 Kestrel + 真 Mongo：游客登录签发访问令牌；</item>
    /// <item>房间服真注册心跳 → Meta 实例注册表（buildHash/容量/地址）；</item>
    /// <item>客户端持令牌取 Join Ticket（Meta 按容量分配实例并签发）；</item>
    /// <item>客户端真 KCP + 安全信封连**房间服真监听端口**，用票据 Join → 房间端**真验签**；</item>
    /// <item>**房主发起开局**（起满员不再自动开局）→ **真开火打真击杀**（走完整判定链：枪口→AimPoint 射线、命中柱、伤害、K/D）；</item>
    /// <item>达击杀上限 → 结算冻结 → Outbox → 提交管道真 HTTP → Meta 落库；</item>
    /// <item><c>GET /matches</c> 按账号查回该局 → **与权威 Sim 逐项对账**。</item>
    /// </list>
    ///
    /// **为什么值得单独一条**：既有的 L3 覆盖"注册→签发→验签"（<c>LobbyHttpTests</c>）与
    /// "Outbox→提交→落库"（<c>SettlementL3Tests</c>）两段，但**没有任何一条**把"真客户端经真票据进真房间、
    /// 打出真击杀、结算回流"串起来——两段之间的接缝（票据玩家身份 → 席位账号 → 结算载荷）正是
    /// 最容易漂移又最不容易被发现的地方。
    ///
    /// **客户端角色**：本用例用真 <see cref="RoomClient"/> + 真 KCP/信封扮演客户端（批C 的 Unity
    /// 客户端尚未接线，见《非作者机恢复与验证记录》§5 第 10 项）——验的是**协议与数据链**，
    /// 不含 Unity 表现层。
    /// </summary>
    [Collection("Mongo")]
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public sealed class CloudLoopbackL3Tests : IAsyncLifetime
    {
        // ---- 密钥（本用例一次性；部署形态经环境变量/Secret 注入，禁命令行明文）----
        private static readonly byte[] AuthKey = Filled(0x33);            // Meta 访问令牌签名
        private static readonly byte[] LobbyInstanceKey = Filled(0x11);   // 实例注册凭据
        private static readonly byte[] SettlementInstanceKey = Filled(0x66); // 结算提交凭据（与注册分权）
        private static readonly byte[] TicketKey = Filled(0x22);          // Join 票据 HMAC
        private static readonly byte[] EnvelopeKey = Filled(0x77);        // 安全信封 PSK

        private const string TicketKid = "l1";
        private const string Audience = "lobby";
        private const string RoomId = "loop-ffa";

        /// <summary>击杀上限：**打 3 个击杀**即关房——够验 K/D/比分/胜负，又不拖长用例。</summary>
        private const int KillLimit = 3;

        private readonly MongoFixture _fixture;
        private readonly List<WebApplication> _hosts = new List<WebApplication>();
        private readonly List<IDisposable> _cleanup = new List<IDisposable>();
        private readonly List<KcpTransportClient> _transports = new List<KcpTransportClient>();
        /// <summary>已建立的回环客户端——等待期间必须持续泵（KCP 握手/收发都靠 Tick 推进）。</summary>
        private readonly List<LoopbackClient> _clients = new List<LoopbackClient>();

        public CloudLoopbackL3Tests(MongoFixture fixture) => _fixture = fixture;

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            foreach (IDisposable d in _cleanup)
            {
                try { d.Dispose(); } catch { /* 收尾失败不掩盖断言结果 */ }
            }
            _cleanup.Clear();
            _clients.Clear();
            foreach (KcpTransportClient t in _transports)
            {
                try { t.Dispose(); } catch { }
            }
            _transports.Clear();
            foreach (WebApplication app in _hosts)
            {
                try { await app.StopAsync(); } catch { }
                finally { await app.DisposeAsync(); }
            }
            _hosts.Clear();
        }

        private static byte[] Filled(byte value)
        {
            var bytes = new byte[32];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = value;
            return bytes;
        }

        [SkippableFact]
        public async Task 上云回环_真登录真票据真进房真击杀_结算数据三面对账()
        {
            Skip.If(!_fixture.Available, "Mongo 不可达且 docker 不可用（MONGO_TEST_URI / litegame-mongo 容器）");

            // ── ① Meta 宿主（真 Kestrel + 真 Mongo，四把密钥齐备）────────────────────────
            (WebApplication meta, string metaBase) = await StartMetaAsync(_fixture.CreateDatabaseName());

            // ── ② 房间服宿主（真 KCP + 真票据验签 + 真注册/真结算管道）──────────────────
            (ServerHost host, LobbyRegistrationClient lobby, SettlementSubmitService settlement, ServerLoop loop, int roomPort)
                = await StartRoomServerAsync(metaBase);

            // 注册心跳真打 Meta（真 HttpClient）——Meta 注册表由此出现本实例。
            Assert.True(lobby.TrySendOnce(out string regError), regError);

            // 结算提交管道显式启动（生产入口 Program 同款；只装配不 Start = 条目永留待提交面）
            settlement.Start();
            _cleanup.Add(new WorkerStopper(settlement));

            // ── ③ 两个真客户端：登录 → 取票 → 真 KCP/信封 → Join（票据）─────────────────
            loop.Start();
            _cleanup.Add(new LoopStopper(loop));

            string accountA = await GuestLoginAsync(metaBase, "dev-loop-A");
            string accountB = await GuestLoginAsync(metaBase, "dev-loop-B");

            LoopbackClient a = await JoinAsync(metaBase, accountA, roomPort, expectPlayerId: 0);
            LoopbackClient b = await JoinAsync(metaBase, accountB, roomPort, expectPlayerId: 1);
            _cleanup.Add(a);
            _cleanup.Add(b);

            // 两人入座 → **房主发起开局**（起满员不再自动开局；expected_players=2）
            a.Client.SendStartMatchRequest();
            Assert.True(WaitFor(() => a.HasStartGame && b.HasStartGame, 15_000),
                $"StartGame 未达（room.Phase={host.Room?.Phase} seats={host.Room?.NextPlayerId}）");

            // 票据身份 → 席位账号 → 结算载荷（SeatAccountIds）这条链不在此处窥探内部状态：
            // 它的可观察终点是"按各自账号查得到本局"（见 §⑥ 的按账号查询断言）——
            // 链上任一环断裂（票据身份丢失/席位账号空串/Meta 按账号落库错位）都会让该断言红。

            // ── ④ 真开火打真击杀（完整判定链）──────────────────────────────────────────
            long entityA = host.Room.EntityIdOf(0);
            long entityB = host.Room.EntityIdOf(1);
            Assert.NotEqual(0L, entityA);
            Assert.NotEqual(0L, entityB);

            int kills = DriveKills(a, host, target: b, targetSlot: 1, budgetMs: 30_000);

            // 权威 Sim 的 K/D 是本用例的"真值面"
            int authKillsA = host.Room.AuthSim.Entities[0].Kills;
            int authDeathsB = host.Room.AuthSim.Entities[1].Deaths;
            int authDeathsA = host.Room.AuthSim.Entities[0].Deaths;   // 击杀者本人死亡；房间关闭后 host.Room 被移除，故在此先取
            Assert.True(authKillsA >= 1, $"真击杀未发生（Kills={authKillsA}；Gate acc={host.Room.Gate.AcceptedCount} "
                + $"stale={host.Room.Gate.DroppedStaleFrame} oor={host.Room.Gate.DroppedOutOfRange} "
                + $"dup={host.Room.Gate.DroppedDuplicateFrame}）");
            Assert.Equal(authKillsA, authDeathsB);   // 每次击杀对应一次死亡

            // ── ⑤ 达上限关房 → 结算冻结 → Outbox → 提交管道 → Meta 落库 ────────────────
            Assert.True(WaitFor(() => host.Room.Closed, 30_000), "达击杀上限后房间未关闭");
            Assert.Equal(KillLimit, authKillsA);      // 恰好打满上限

            string matchId = host.Room.MatchId;
            Assert.False(string.IsNullOrEmpty(matchId));

            // 提交管道真打 Meta（间隔 1s）——等落库可查
            JsonElement matchRow = default;
            bool found = WaitForJson(async () =>
            {
                JsonElement rows = await QueryMatchesAsync(metaBase, accountA);
                foreach (JsonElement row in rows.EnumerateArray())
                {
                    if (row.GetProperty("matchId").GetString() == matchId) { matchRow = row; return true; }
                }
                return false;
            }, 30_000);
            Assert.True(found, $"结算未在 Meta 落库可查（matchId={matchId}，Outbox 待提交={settlement.PendingCount}）");

            // ── ⑥ 三面对账：权威 Sim ↔ 结算载荷 ↔ Meta 查询面 ─────────────────────────
            Assert.Equal(authKillsA, matchRow.GetProperty("kills").GetInt32());
            Assert.Equal(authDeathsA, matchRow.GetProperty("deaths").GetInt32());  // A 本人 deaths（=0；用先取值）
            Assert.Equal((int)MatchEndReason.KillLimit, matchRow.GetProperty("gameplayEndReason").GetInt32());

            // 击杀者 A 的本人视角：K=上限、D=0（B 的 deaths 见下方 loserRow 断言）
            Assert.Equal(0, authDeathsA);

            // 结算只落一份（幂等；提交管道可能重投）
            Assert.Equal(1, CountMatches(await QueryMatchesAsync(metaBase, accountA), matchId));

            // 输家视角同局可见（Deaths=上限、Kills=0）——按账号查询是各自视角
            JsonElement loserRow = await FindMatchAsync(metaBase, accountB, matchId);
            Assert.Equal(0, loserRow.GetProperty("kills").GetInt32());
            Assert.Equal(KillLimit, loserRow.GetProperty("deaths").GetInt32());
        }

        /// <summary>
        /// **多图批 · 真服务器**（独立用例——不与 FFA 主链耦合，几十秒内完成）：
        /// Meta `create-room{mapId=2}` 出号并把行键盖章进票据 → 客户端真 KCP/信封持票进房 →
        /// **JoinAck.map_id=2**（客户端据此选地图表行与场景）＋ 房间端实建房间的行键与判定图同为训练场行
        /// （行 2：8 障碍/16 出生，摘要 c78441a74f6bc5f9，== 表行投影）。
        /// 同批验**开房参数**：创建意图带 `expected_players=4` → 房间端建房时生效、JoinAck 回执房事实。
        /// 该房不开局（4 座只进 1 人）；验完即断。
        /// </summary>
        [SkippableFact]
        public async Task 多图与人数_创建训练场四人房_真票据真进房_JoinAck回执房事实()
        {
            Skip.If(!_fixture.Available, "Mongo 不可达且 docker 不可用（MONGO_TEST_URI / litegame-mongo 容器）");

            (WebApplication meta, string metaBase) = await StartMetaAsync(_fixture.CreateDatabaseName());
            (ServerHost host, LobbyRegistrationClient lobby, _, ServerLoop loop, int roomPort)
                = await StartRoomServerAsync(metaBase);

            Assert.True(lobby.TrySendOnce(out string regError), regError);   // Meta 注册表出现本实例
            loop.Start();
            _cleanup.Add(new LoopStopper(loop));

            string account = await GuestLoginAsync(metaBase, "dev-map-A");
            await 多图与人数_创建训练场四人房_真票据进房_回执房事实(metaBase, account, roomPort, host);
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // 外接模式（进程级验证）：指向**已起的真服务器进程**，不再起进程内宿主
        //   启用：LITELOOP_META_URL=http://127.0.0.1:5000  LITELOOP_ROOM_PORT=17777
        //   可选：LITELOOP_KILL_LIMIT=3（须与外部房间配置一致）
        //   —— 与同进程用例的差别：无进程内权威态可读，**唯一可观测面 = 客户端镜像**；
        //      对账降为两面对账（客户端观测 ↔ Meta 落库），权威面由外部服务器的日志/指标印证。
        // ══════════════════════════════════════════════════════════════════════════════

        [SkippableFact]
        public async Task 上云回环_外接进程_真链跑通()
        {
            string metaUrl = Environment.GetEnvironmentVariable("LITELOOP_META_URL");
            string roomPortRaw = Environment.GetEnvironmentVariable("LITELOOP_ROOM_PORT");
            Skip.If(string.IsNullOrWhiteSpace(metaUrl) || string.IsNullOrWhiteSpace(roomPortRaw),
                "外接模式未启用（设 LITELOOP_META_URL / LITELOOP_ROOM_PORT 指向已起的真服务器进程）");

            string metaBase = metaUrl.TrimEnd('/');
            int roomPort = int.Parse(roomPortRaw);
            int killLimit = int.TryParse(Environment.GetEnvironmentVariable("LITELOOP_KILL_LIMIT"), out int kl) ? kl : KillLimit;

            string tag = Guid.NewGuid().ToString("N").Substring(0, 6);
            string accountA = await GuestLoginAsync(metaBase, "ext-loop-A-" + tag);
            string accountB = await GuestLoginAsync(metaBase, "ext-loop-B-" + tag);

            LoopbackClient a = await JoinAsync(metaBase, accountA, roomPort, expectPlayerId: 0);
            LoopbackClient b = await JoinAsync(metaBase, accountB, roomPort, expectPlayerId: 1);
            _cleanup.Add(a);
            _cleanup.Add(b);

            a.Client.SendStartMatchRequest();   // 房主发起开局（起满员不再自动开局）
            Assert.True(WaitFor(() => a.HasStartGame && b.HasStartGame, 20_000), "StartGame 未达（外接进程）");

            int kills = DriveKillsViaMirror(a, targetSlot: 1, budgetMs: 60_000, killLimit: killLimit);
            Assert.True(kills >= 1, $"真击杀未发生（mirror Kills={kills}）");

            // 结算回流：轮询 Meta 按账号查询直到本局出现（房关 + 提交管道 + 落库三段的可观察终点）
            JsonElement rowA = default;
            bool found = WaitForJson(async () =>
            {
                JsonElement rows = await QueryMatchesAsync(metaBase, accountA);
                foreach (JsonElement row in rows.EnumerateArray())
                {
                    if (row.GetProperty("kills").GetInt32() >= 1) { rowA = row; return true; }
                }
                return false;
            }, 60_000);
            Assert.True(found, "结算未在 Meta 落库可查（外接进程）");

            string matchId = rowA.GetProperty("matchId").GetString();
            Assert.Equal(killLimit, rowA.GetProperty("kills").GetInt32());        // 打满上限
            Assert.Equal(0, rowA.GetProperty("deaths").GetInt32());              // A 本人未死
            Assert.Equal(1, CountMatches(await QueryMatchesAsync(metaBase, accountA), matchId));  // 幂等只落一份

            JsonElement loserRow = await FindMatchAsync(metaBase, accountB, matchId);
            Assert.Equal(0, loserRow.GetProperty("kills").GetInt32());
            Assert.Equal(killLimit, loserRow.GetProperty("deaths").GetInt32());
        }

        /// <summary>外接模式驱动：**唯一可观测面=客户端镜像**——目标位取镜像、帧号取镜像最新帧+2（恒领先）。</summary>
        private int DriveKillsViaMirror(LoopbackClient shooter, int targetSlot, int budgetMs, int killLimit)
        {
            var watch = Stopwatch.StartNew();
            int kills = 0, sent = 0, skippedDead = 0, nextFrame = 0;
            while (watch.ElapsedMilliseconds < budgetMs)
            {
                kills = shooter.Mirror.Entities[0].Kills;
                if (kills >= killLimit) break;

                if (shooter.HasStartGame && TryGetMirrorPos(shooter, targetSlot, out SimVector3 aim))
                {
                    int floor = shooter.Mirror.Frame + 2;
                    if (nextFrame < floor) nextFrame = floor;
                    var input = new SimInputFrame
                    {
                        AimPointX = aim.X,
                        AimPointY = aim.Y + 1.0f,   // 躯干中部（爆头带下沿 1.449 → 普通命中）
                        AimPointZ = aim.Z,
                        Buttons = SimInputFrame.ButtonFire,
                    };
                    shooter.SendInput(nextFrame, input);
                    nextFrame++;
                    sent++;
                }
                else
                {
                    skippedDead++;
                }

                PumpAll();
                Thread.Sleep(8);
            }

            Assert.True(sent > 0, $"从未发出开火输入（外接，跳过={skippedDead}）");
            return kills;
        }

        private static bool TryGetMirrorPos(LoopbackClient viewer, int slot, out SimVector3 pos)
        {
            pos = default;
            ref EntitySlot e = ref viewer.Mirror.Entities[slot];
            if (e.Id == 0L || e.Hp <= 0) return false;
            pos = e.Pos;
            return true;
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // 击杀驱动
        // ══════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 真开火直到权威 Sim 的击杀数达上限（或预算耗尽）。
        ///
        /// **瞄准口径**：AimPoint = 对手实体位置 + 1.0m（躯干中部）。射击链从逻辑枪口沿
        /// "枪口→AimPoint"求交（<c>SimRaycast</c>），命中柱 [pos.Y, pos.Y+1.8]×0.45 覆盖该高度；
        /// 爆头带下沿 = 1.8×0.805 = 1.449，故 1.0m 判为**躯干命中**（伤害 16，非 32）。
        ///
        /// **输入帧号**：取房间服权威帧 +1（inputDelay=1 语义，与 <c>RoomServerTests</c> 同款）——
        /// 客户端在本进程内可直接读权威帧，这是测试 harness 的特权，不是生产路径。
        /// </summary>
        private int DriveKills(LoopbackClient shooter, ServerHost host, LoopbackClient target, int targetSlot,
            int budgetMs)
        {
            var watch = Stopwatch.StartNew();
            int kills = 0;
            int sent = 0;
            int skippedDead = 0;
            int nextFrame = 0;

            while (watch.ElapsedMilliseconds < budgetMs)
            {
                RoomRuntime room = host.Room;
                if (room == null || room.Closed) break;

                kills = room.AuthSim.Entities[0].Kills;
                if (kills >= KillLimit) break;

                // **每轮必须泵**（KCP 靠收发保活；任一分支跳过泵 → 10s 无消息即被判超时断链）
                if (room.Started && !room.Closed)
                {
                    if (TryGetAuthoritativePos(room, targetSlot, out SimVector3 aim))
                    {
                        // 输入帧号：**单调前进且恒领先权威帧**（inputDelay=1 语义）。
                        // 不能只读权威帧 +1——读与包到达之间服务器可能已推进，该帧即成 stale 被丢。
                        int floor = room.AuthSim.Frame + 2;
                        if (nextFrame < floor) nextFrame = floor;

                        var input = new SimInputFrame
                        {
                            AimPointX = aim.X,
                            AimPointY = aim.Y + 1.0f,   // 躯干中部（爆头带下沿 = 1.8×0.805 = 1.449 → 判普通命中）
                            AimPointZ = aim.Z,
                            Buttons = SimInputFrame.ButtonFire,
                        };
                        shooter.SendInput(nextFrame, input);
                        nextFrame++;
                        sent++;
                    }
                    else
                    {
                        skippedDead++;                  // 目标死亡窗口（等复活）：只泵不发
                    }
                }

                PumpAll();
                Thread.Sleep(8);
            }

            Assert.True(sent > 0, $"从未发出开火输入（跳过={skippedDead}）——瞄准/帧号前置失败");
            return kills;
        }

        /// <summary>对手当前**权威**位（测试特权：进程内直读权威态；真客户端用其插值视图，差量微小）。</summary>
        private static bool TryGetAuthoritativePos(RoomRuntime room, int playerSlot, out SimVector3 pos)
        {
            pos = default;
            long id = room.EntityIdOf(playerSlot);
            if (id == 0L) return false;
            if (!room.AuthSim.TryResolve(id, out int slot)) return false;
            ref EntitySlot e = ref room.AuthSim.Entities[slot];
            if (e.Hp <= 0) return false;                    // 死亡窗口（等复活）
            pos = e.Pos;
            return true;
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // HTTP 客户端面（游客登录 / 取票 / 查结果）
        // ══════════════════════════════════════════════════════════════════════════════

        private static async Task<string> GuestLoginAsync(string metaBase, string deviceId)
        {
            using var http = new HttpClient();
            var body = new StringContent(
                JsonSerializer.Serialize(new { requestId = "req-" + Guid.NewGuid().ToString("N"), deviceId }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await http.PostAsync(metaBase + "/auth/guest", body);
            Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            string token = doc.RootElement.GetProperty("accessToken").GetString();
            Assert.False(string.IsNullOrEmpty(token));
            return token;
        }

        /// <summary>取 Join Ticket（Bearer = 游客令牌）→ 真 KCP/信封连房间服 → Join(票据)。</summary>
        private async Task<LoopbackClient> JoinAsync(string metaBase, string accessToken, int roomPort, int expectPlayerId)
        {
            string ticket;
            string address;
            using (var http = new HttpClient())
            {
                var req = new HttpRequestMessage(HttpMethod.Post, metaBase + "/lobby/join-ticket");
                req.Headers.Add("Authorization", "Bearer " + accessToken);
                req.Content = new StringContent(JsonSerializer.Serialize(new
                {
                    requestId = "req-" + Guid.NewGuid().ToString("N"),
                    roomId = RoomId,
                    buildHash = ServerHost.ServerBuildHash,   // 版本准入：与实例上报哈希一致
                }), Encoding.UTF8, "application/json");

                HttpResponseMessage resp = await http.SendAsync(req);
                Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
                using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                ticket = doc.RootElement.GetProperty("ticket").GetString();
                address = doc.RootElement.GetProperty("address").GetString();
            }

            Assert.False(string.IsNullOrEmpty(ticket), "Join Ticket 未签发");
            Assert.Equal("127.0.0.1:" + roomPort, address);   // 分配到的正是本房间服的监听地址

            LoopbackClient client = JoinWithTicket(ticket, RoomId, roomPort);
            Assert.True(WaitFor(() => client.Joined, 15_000), "JoinAck 未达（票据被拒或连接失败）");
            Assert.Equal(expectPlayerId, client.PlayerId);
            return client;
        }

        /// <summary>
        /// 起房间服宿主（真 KCP + 真票据验签 + 真注册/结算管道，端口系统分配）——两条 L3 用例共用：
        /// ① 上云回环主链（FFA 真击杀/结算）；② 多图批（真创建真进房/JoinAck 回执）。
        /// **不**注册心跳、**不**启动循环/结算（调用方决定——主链要结算，多图用例不需要）。
        /// 多图批：`MapCatalog` 与生产装配 Program 同形（缓存装载、缺行 fail-closed）。
        /// </summary>
        private async Task<(ServerHost host, LobbyRegistrationClient lobby, SettlementSubmitService settlement,
            ServerLoop loop, int roomPort)> StartRoomServerAsync(string metaBase)
        {
            await Task.CompletedTask;   // 与调用方 async 语境对齐（当前实现全同步——保持签名可演进）

            string journalDir = Path.Combine(Path.GetTempPath(), "litegame_loop_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(journalDir);
            string journal = Path.Combine(journalDir, "settlements.journal");

            var configJson = BuildRoomServerConfigJson(metaBase, journal);
            RoomServerConfig config = RoomServerConfig.Parse(configJson, "<cloud-loopback>");

            SimMapData defaultMap = ServerMapLoader.LoadFromRepo();
            var mapCatalog = new ServerMapCatalog(ServerMapLoader.LoadFromRepo, defaultMap,
                LiteGame.MapTableProjection.DefaultMapId);

            var inputs = new HostAssembly.Inputs
            {
                Config = config,
                Audience = Audience,
                // 房间端验签器 = 真 HmacJoinTicketValidator（与 Meta 签发端同源 JoinTicketFormat）
                TicketValidator = new HmacJoinTicketValidator(new[] { new JoinTicketKey(TicketKid, TicketKey) }),
                EnvelopeOptions = new SecureEnvelopeOptions(EnvelopeKey),
                CombatValues = ServerTableLoader.LoadFromRepo().Combat.ToValues(),
                Weapons = ServerTableLoader.LoadFromRepo().Weapons,
                Map = defaultMap,
                MapCatalog = mapCatalog.Resolve,
                Lobby = new LobbyRegistrationClient.Settings
                {
                    Url = metaBase + "/lobby/instances/register",
                    InstanceId = "room-loop",
                    InstanceKey = LobbyInstanceKey,
                    AdvertiseHost = "127.0.0.1",
                    HeartbeatIntervalMs = 1000,
                    RequestTimeoutMs = 5000,
                },
                Settlement = new SettlementSubmitService.Settings
                {
                    Url = metaBase + "/matches/result",
                    InstanceKey = SettlementInstanceKey,   // 结算凭据（与注册分权）
                    IntervalMs = 1000,
                    MaxBackoffMs = 60_000,
                    RequestTimeoutMs = 5000,
                },
            };

            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            HostAssembly.Register(services, inputs);
            ServiceProvider provider = HostAssembly.Build(services);
            _cleanup.Add(provider);

            var host = provider.GetRequiredService<ServerHost>();
            LobbyRegistrationClient lobby = provider.GetRequiredService<LobbyRegistrationClient>();
            SettlementSubmitService settlement = provider.GetRequiredService<SettlementSubmitService>();
            ServerLoop loop = provider.GetRequiredService<ServerLoop>();

            int roomPort = host.BoundPort;   // port=0 → 系统分配，回读真端口
            Assert.True(roomPort > 0, "房间服未监听");
            return (host, lobby, settlement, loop, roomPort);
        }

        /// <summary>票据在手：真 KCP + 真安全信封（与房间服同 PSK）连房间服 → Join（多图批：房号可非默认）。</summary>
        private LoopbackClient JoinWithTicket(string ticket, string roomId, int roomPort)
        {
            var kcp = new KcpTransportClient();
            _transports.Add(kcp);
            var transport = new SecureEnvelopeClientTransport(kcp, new SecureEnvelopeOptions(EnvelopeKey));
            var client = new LoopbackClient(transport, ticket, roomId);
            _clients.Add(client);
            client.Connect("127.0.0.1", roomPort);
            return client;
        }

        /// <summary>
        /// 多图批（真服务器）：`create-room{mapId=2}` → 响应票据 → 真 KCP/信封 Join → **JoinAck.map_id=2**；
        /// 房间端实建房间的行键与判定图同为训练场行（表行 2：训练场快照摘要 c78441a74f6bc5f9）。
        /// 该房不开局（expected_players=2 只进 1 人）；验完即断——多图链路证据与 FFA 主链共用同一次真宿主。
        /// </summary>
        private async Task 多图与人数_创建训练场四人房_真票据进房_回执房事实(string metaBase, string accessToken,
            int roomPort, ServerHost host)
        {
            const int trainingMapId = 2;   // = MapTableProjection.TrainingMapId（表行键；此处钉死为协议/数据面证据）
            const int roomPlayers = 4;     // 开房参数：期望人数（房间模板未配该值以外时按请求生效）
            string roomId;
            string ticket;
            using (var http = new HttpClient())
            {
                var req = new HttpRequestMessage(HttpMethod.Post, metaBase + "/lobby/create-room");
                req.Headers.Add("Authorization", "Bearer " + accessToken);
                req.Content = new StringContent(JsonSerializer.Serialize(new
                {
                    requestId = "req-map-" + Guid.NewGuid().ToString("N"),
                    mapId = trainingMapId,                     // 创建意图：地图行键随票据盖章（签名覆盖内）
                    expectedPlayers = roomPlayers,             // 创建意图：期望人数随票据盖章（同批）
                    buildHash = ServerHost.ServerBuildHash,
                }), Encoding.UTF8, "application/json");

                HttpResponseMessage resp = await http.SendAsync(req);
                Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
                using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                roomId = doc.RootElement.GetProperty("roomId").GetString();
                ticket = doc.RootElement.GetProperty("ticket").GetString();
            }

            Assert.False(string.IsNullOrEmpty(roomId), "多图：服务端未出新房号");
            LoopbackClient client = JoinWithTicket(ticket, roomId, roomPort);
            Assert.True(WaitFor(() => client.Joined, 15_000), "多图：真票据进房未达 JoinAck");

            Assert.Equal(trainingMapId, client.MapId);                     // ① 权威回执：客户端据此选表行与场景
            Assert.Equal(roomPlayers, client.ExpectedPlayers);             // ① 权威回执：客户端据此定容预测世界
            Assert.True(host.TryGetRoom(roomId, out RoomRuntime room), "多图：房间端未见该房");
            Assert.Equal(trainingMapId, room.MapId);                       // ② 房事实：行键落进房间配置
            Assert.Equal(roomPlayers, room.ExpectedPlayers);               // ② 房事实：请求人数落进房间配置
            Assert.Equal(SimMapDigest.Compute(TestMaps.TrainingGround()),  // ③ 判定图 = 该行投影（非宿主默认图）
                SimMapDigest.Compute(room.Map));
        }

        private static async Task<JsonElement> QueryMatchesAsync(string metaBase, string accessToken)
        {
            using var http = new HttpClient();
            var req = new HttpRequestMessage(HttpMethod.Get, metaBase + "/matches");
            req.Headers.Add("Authorization", "Bearer " + accessToken);
            HttpResponseMessage resp = await http.SendAsync(req);
            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("matches").Clone();
        }

        private static async Task<JsonElement> FindMatchAsync(string metaBase, string accessToken, string matchId)
        {
            JsonElement rows = await QueryMatchesAsync(metaBase, accessToken);
            foreach (JsonElement row in rows.EnumerateArray())
            {
                if (row.GetProperty("matchId").GetString() == matchId) return row;
            }
            throw new Xunit.Sdk.XunitException($"按账号查询未找到该局：{matchId}");
        }

        private static int CountMatches(JsonElement rows, string matchId)
        {
            int n = 0;
            foreach (JsonElement row in rows.EnumerateArray())
            {
                if (row.GetProperty("matchId").GetString() == matchId) n++;
            }
            return n;
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // 装配与轮询
        // ══════════════════════════════════════════════════════════════════════════════

        /// <summary>房间服配置（等价部署形态：注册+结算都指向本机 Meta；单人不下发、2 人满员开局）。</summary>
        private static string BuildRoomServerConfigJson(string metaBase, string journal)
        {
            return JsonSerializer.Serialize(new
            {
                port = 0,                       // 系统分配空闲端口（回读 BoundPort）
                max_rooms = 3,                  // FFA 房 ＋ 多图批的训练场房（各占一格）
                worker_count = 1,
                mailbox_capacity = 1024,
                audience = Audience,            // 与 Meta 签发的 ticket.audience 一致
                default_template = "loop",
                settlement_journal = journal.Replace('\\', '/'),
                rooms = new Dictionary<string, object>
                {
                    ["loop"] = new
                    {
                        expected_players = 2,
                        seed = 424242L,          // 固定 seed：确定性回放/验收脚本形态
                        match_time_limit_ms = 0,
                        waiting_timeout_ms = 0,
                        match_duration_frames = 10800,
                        kill_limit = KillLimit,  // 3 个击杀即关房（用例时长可控）
                        respawn_delay_frames = 30,
                        spawn_protection_frames = 0,   // 关出生保护：否则复活后 2s 不可击杀
                    },
                },
                lobby = new
                {
                    url = metaBase + "/lobby/instances/register",
                    instance_id = "room-loop",
                    advertise_host = "127.0.0.1",
                    heartbeat_interval_ms = 1000,
                },
                settlement = new
                {
                    submit_url = metaBase + "/matches/result",
                    submit_interval_ms = 1000,
                    submit_max_backoff_ms = 60000,
                    submit_timeout_ms = 5000,
                },
            });
        }

        private async Task<(WebApplication App, string BaseAddress)> StartMetaAsync(string databaseName)
        {
            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetaHost.ConfigSection + ":BindAddress"] = "http://127.0.0.1:0",
                [MetaHost.ConfigSection + ":MongoConnectionString"] = _fixture.Uri,
                [MetaHost.ConfigSection + ":MongoDatabaseName"] = databaseName,
                [MetaHost.ConfigSection + ":AuthSigningKeyBase64"] = Convert.ToBase64String(AuthKey),
                [MetaHost.ConfigSection + ":LobbyInstanceKeyBase64"] = Convert.ToBase64String(LobbyInstanceKey),
                [MetaHost.ConfigSection + ":SettlementInstanceKeyBase64"] = Convert.ToBase64String(SettlementInstanceKey),
                [MetaHost.ConfigSection + ":LobbyTicketKeyBase64"] = Convert.ToBase64String(TicketKey),
                [MetaHost.ConfigSection + ":LobbyTicketKeyId"] = TicketKid,
                [MetaHost.ConfigSection + ":LobbyTicketAudience"] = Audience,
                [MetaHost.ConfigSection + ":LobbyDefaultRoomId"] = RoomId,
            };

            WebApplication app = MetaHost.Build(Array.Empty<string>(), overrides);
            MetaHost.WireGracefulShutdown(app);
            _hosts.Add(app);
            try
            {
                await app.StartAsync();
            }
            catch
            {
                _hosts.Remove(app);
                await app.DisposeAsync();
                throw;
            }

            var server = app.Services.GetRequiredService<IServer>();
            foreach (string address in server.Features.Get<IServerAddressesFeature>().Addresses)
            {
                return (app, address.TrimEnd('/'));
            }
            throw new InvalidOperationException("未取得 Meta 监听地址");
        }

        /// <summary>推进所有已建立客户端的传输泵（KCP 握手与收发都依赖它——只 sleep 永远等不到）。</summary>
        private void PumpAll()
        {
            foreach (LoopbackClient c in _clients) c.Pump();
        }

        /// <summary>轮询谓词，**每轮泵一次**（传输不 Tick 则握手与快照都不推进）。</summary>
        private bool WaitFor(Func<bool> predicate, int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (predicate()) return true;
                PumpAll();
                Thread.Sleep(10);
            }
            return predicate();
        }

        /// <summary>异步谓词轮询（HTTP 查询面）。</summary>
        private bool WaitForJson(Func<Task<bool>> predicate, int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (predicate().GetAwaiter().GetResult()) return true;
                PumpAll();
                Thread.Sleep(200);
            }
            return false;
        }

        private sealed class WorkerStopper : IDisposable
        {
            private readonly HttpSubmitWorker _worker;
            public WorkerStopper(HttpSubmitWorker worker) => _worker = worker;
            public void Dispose() => _worker.Stop();
        }

        private sealed class LoopStopper : IDisposable
        {
            private readonly ServerLoop _loop;
            public LoopStopper(ServerLoop loop) => _loop = loop;
            public void Dispose() => _loop.Stop();
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // 回环客户端（真 RoomClient + 真传输；批C Unity 客户端未接线，本类即其协议面替身）
        // ══════════════════════════════════════════════════════════════════════════════

        private sealed class LoopbackClient : IDisposable
        {
            private readonly IClientTransport _transport;
            private readonly string _roomId;
            private readonly string _ticket;
            private readonly SimWorldState _mirror = new SimWorldState();
            private readonly Dictionary<int, long> _slotEntityIds = new Dictionary<int, long>();

            public readonly RoomClient Client;
            public SimWorldState Mirror => _mirror;
            public int PlayerId => Client.PlayerId;
            public bool Joined => Client.PlayerId >= 0;
            public bool HasStartGame => Client.HasStartGame;

            /// <summary>JoinAck 回执的地图行键（多图批；0 = 服务器未标注）。</summary>
            public int MapId => Client.MapId;

            /// <summary>JoinAck 回执的期望人数（开房参数批；0 = 服务器未标注——客户端按默认 2 座回落）。</summary>
            public int ExpectedPlayers => Client.ExpectedPlayers;

            public LoopbackClient(IClientTransport transport, string ticket, string roomId = RoomId)
            {
                _transport = transport;
                _roomId = roomId;
                _ticket = ticket;
                Client = new RoomClient(transport);
                Client.OnSnapshot += OnSnapshot;
                _transport.OnConnected += () =>
                {
                    if (Client.Phase == ClientSessionPhase.Idle)
                        Client.SendJoin(_roomId, _ticket, ServerHost.ServerBuildHash);
                };
            }

            public void Connect(string host, int port) => Client.Connect(host, port);

            public void Pump()
            {
                Client.TickIncoming();
                Client.TickOutgoing();
            }

            public void SendInput(int frame, in SimInputFrame input) => Client.SendInput(frame, in input, viewFrame: 0);

            /// <summary>按席位号取实体 Id（快照 SlotDelta 携带；用于定位对手位置）。</summary>
            public long EntityIdOfSlot(int slot)
            {
                lock (_slotEntityIds)
                {
                    return _slotEntityIds.TryGetValue(slot, out long id) ? id : 0L;
                }
            }

            private void OnSnapshot(LiteNet.Proto.StateSnapshot snapshot)
            {
                lock (_slotEntityIds)
                {
                    foreach (LiteNet.Proto.SlotDelta slot in snapshot.Slots)
                    {
                        if (slot.Id != 0L) _slotEntityIds[slot.Slot] = slot.Id;
                    }
                }
                SnapshotReassembler.Apply(snapshot, _mirror, out uint _);
            }

            public void Dispose() => Client.Dispose();
        }
    }
}
