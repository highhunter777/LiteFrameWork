using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;
using LiteFramework;
using MetaWire;
using Newtonsoft.Json;

namespace RoomServer
{
    /// <summary>
    /// Lobby 实例注册客户端（《Meta 服务专项设计》§7"RoomServer 实例启动后向 Lobby 注册
    /// （实例 ID、版本、房间容量、当前占用、健康状态），并周期性上报；心跳超时即从可分配集合移除";
    /// 《上云测试专项设计》§5"探针/进程守护"）。
    ///
    /// - 启动即发一发，其后按配置间隔周期心跳；载荷 = 宿主实时快照（buildHash/容量/占用/排空位）。
    /// - **失败不退出对局服务**：网络/服务不可达记失败计数并按次重试（Lobby 不可达只影响"新玩家分配"，
    ///   不影响进行中的对局）；失败日志节流（首败与每 20 次一行），不刷屏。
    /// - **排空上报**：宿主 <c>BeginDrain</c> 后快照 Draining=true——Lobby 立即停止向其分配新对局（drain 协同）。
    /// - **单在途请求**（专用后台线程串行），无队列、无堆积；间隔/超时可配。
    /// - 实例密钥与票据**不进日志**（§11.1 同类约定——本类只打状态与计数）。
    ///
    /// HTTP 客户端可注入 <see cref="HttpMessageHandler"/>——L1 用伪造 handler 断言载荷与失败路径。
    /// </summary>
    public sealed class LobbyRegistrationClient : HttpSubmitWorker
    {
        /// <summary>注册参数（Program 从 RoomServerConfig + 环境/命令行密钥组装；密钥经装配注入）。</summary>
        public sealed class Settings
        {
            /// <summary>注册端点完整 URL（如 <c>https://meta.example.com/lobby/instances/register</c>）。</summary>
            public string Url;

            /// <summary>实例唯一标识（重启保持稳定——同一实例的心跳是更新，不是新登记）。</summary>
            public string InstanceId;

            /// <summary>**注册**实例密钥（Bearer 原文以其 Base64 串编码——与 Meta 的
            /// <c>LobbyInstanceKeyBase64</c> 同值；结算提交另有独立凭据，P1②）。</summary>
            public byte[] InstanceKey;

            /// <summary>客户端可达 host（端口用宿主实际 <c>BoundPort</c>——不信任配置端口值）。</summary>
            public string AdvertiseHost;

            /// <summary>心跳间隔（毫秒）。</summary>
            public int HeartbeatIntervalMs;

            /// <summary>单次请求超时（毫秒）。</summary>
            public int RequestTimeoutMs;
        }

        /// <summary>心跳载荷快照（由宿主事实构造——注册表只收事实，不收估算）。</summary>
        public struct Snapshot
        {
            public string BuildHash;
            public int MaxRooms;
            public int RoomCount;
            public int MaxPlayers;
            public int PlayerCount;
            public bool Draining;

            /// <summary>宿主实际监听端口（<c>BoundPort</c>；0 配置经内核分配后回读）。</summary>
            public int Port;

            /// <summary>房间明细（房间列表批；null/空 = 不上报明细——注册表按"未上报"兼容）。</summary>
            public ServerHost.RoomSummary[] Rooms;
        }

        // 注册载荷与房间明细的线形状在 MetaWire 共享契约里（客户端/MetaServer/RoomServer 同一份）。


        private readonly Settings _settings;
        private readonly Func<Snapshot> _snapshot;
        private readonly Action<string> _log;
        private long _sent;
        private long _failed;
        private long _lastHeartbeatTtlMs;
        private int _consecutiveFailures;
        private int _ttlWarned;

        public LobbyRegistrationClient(Settings settings, Func<Snapshot> snapshot, Action<string> log,
            HttpMessageHandler handler = null)
            // URL/密钥校验与线程骨架归基类（P2 批二）；实参序保证 settings 的 null 校验先于其余取值
            : base(CheckedUrl(settings), "Lobby 注册 URL", CheckedKey(settings), "LobbyHeartbeat",
                   settings?.RequestTimeoutMs ?? 0, handler)
        {
            if (string.IsNullOrWhiteSpace(settings.InstanceId))
                throw new ArgumentException("实例标识不能为空", nameof(settings));
            if (string.IsNullOrWhiteSpace(settings.AdvertiseHost))
                throw new ArgumentException("对客户端通告的 host 不能为空", nameof(settings));
            if (settings.HeartbeatIntervalMs < 1000)
                throw new ArgumentException("心跳间隔不得低于 1000ms（过密无益，且放大故障域）", nameof(settings));

            _settings = settings;
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>URL 取值（含 settings 的 null 前置校验——在 <c>base(...)</c> 实参序最前）。</summary>
        private static string CheckedUrl(Settings settings)
            => (settings ?? throw new ArgumentNullException(nameof(settings))).Url;

        private static byte[] CheckedKey(Settings settings) => settings.InstanceKey;

        /// <summary>已成功送达的心跳数（诊断/测试断言）。</summary>
        public long Sent
        {
            get { return Interlocked.Read(ref _sent); }
        }

        /// <summary>失败次数（诊断；持续增长说明 Lobby 不可达或密钥/配置不符）。</summary>
        public long Failed
        {
            get { return Interlocked.Read(ref _failed); }
        }

        /// <summary>最近一次注册响应回告的心跳时限（0 = 未成功过）。客户端间隔应显著小于它。</summary>
        public long LastHeartbeatTtlMs
        {
            get { return Interlocked.Read(ref _lastHeartbeatTtlMs); }
        }

        /// <summary>心跳循环（骨架：启动/停止/释放归 <see cref="HttpSubmitWorker"/>；此处只留策略——
        /// 定间隔心跳，启动即刻首发）。</summary>
        protected override void Loop()
        {
            while (!StopRequested)
            {
                TrySendOnce(out _);
                WaitStop(_settings.HeartbeatIntervalMs);
            }
        }

        /// <summary>
        /// 单次注册/心跳（周期循环与启动首发共用；测试直调可断言载荷与失败分类）。
        /// 成功返回 true 并刷新 <see cref="LastHeartbeatTtlMs"/>；失败返回 false 且 error 非空。
        /// **不抛异常**——网络面故障是预期路径（fail-open 于对局服务，仅影响分配资格）。
        /// </summary>
        public bool TrySendOnce(out string error)
        {
            error = null;
            try
            {
                Snapshot snapshot = _snapshot();
                var payload = new InstanceRegisterRequest
                {
                    InstanceId = _settings.InstanceId,
                    BuildHash = snapshot.BuildHash,
                    Address = _settings.AdvertiseHost + ":" + snapshot.Port,
                    MaxRooms = snapshot.MaxRooms,
                    RoomCount = snapshot.RoomCount,
                    MaxPlayers = snapshot.MaxPlayers,
                    PlayerCount = snapshot.PlayerCount,
                    Draining = snapshot.Draining,
                    Rooms = ToRoomDtos(snapshot.Rooms),
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, _settings.Url);
                HttpWorkerSupport.AddBearer(request, _settings.InstanceKey);   // Bearer 构造单源（P2）
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(payload, WireJsonSettings.Value), Encoding.UTF8, "application/json");

                // 专用心跳线程上同步等待异步发送（无同步上下文，不可能死锁）——
                // 该形态同时兼容覆写 SendAsync 的测试替身与真实 SocketsHttpHandler。
                // **带停止令牌**（关闭链批）：Stop 时取消在途请求，让"有界等待"真的能等到线程退出
                // （否则线程停在 5s 请求里，Dispose 会在存活线程上释放停止位 → 未处理异常杀进程）。
                using HttpResponseMessage response = Http
                    .SendAsync(request, HttpCompletionOption.ResponseContentRead, StopToken)
                    .GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    // **2xx 区间**（P1①）：只认 200 会把同义的 201/202/204 当失败，反复重投——
                    // 端点换实现（Created/NoContent）即触发，且症状是"心跳看起来一直在重试"。
                    error = "HTTP " + (int)response.StatusCode;
                    RecordFailure(error);
                    return false;
                }

                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                long ttl = ReadHeartbeatTtlMs(body);
                if (ttl > 0)
                {
                    Interlocked.Exchange(ref _lastHeartbeatTtlMs, ttl);
                    if (_settings.HeartbeatIntervalMs * 2 > ttl && Interlocked.Exchange(ref _ttlWarned, 1) == 0)
                    {
                        // 心跳间隔 ≥ 存活时限的一半：实例将在两跳之间被判超时（分配资格闪断）。
                        _log("[Lobby] 警告：心跳间隔 " + _settings.HeartbeatIntervalMs + "ms 相对存活时限 " + ttl
                            + "ms 过大——请调小心跳间隔或调大 Meta 的 LobbyHeartbeatTtlSeconds");
                    }
                }

                Interlocked.Increment(ref _sent);
                Interlocked.Exchange(ref _consecutiveFailures, 0);
                return true;
            }
            catch (Exception ex)
            {
                // 只打异常类型与消息（消息不含密钥；URL 与实例标识进日志便于排障）
                error = ex.GetType().Name + ": " + ex.Message;
                RecordFailure(error);
                return false;
            }
        }

        private void RecordFailure(string error)
        {
            Interlocked.Increment(ref _failed);
            int consecutive = Interlocked.Increment(ref _consecutiveFailures);
            if (HttpWorkerSupport.ShouldReportFailure(consecutive))   // 节流节奏单源（P2）
            {
                _log("[Lobby] 注册心跳失败（连续 " + consecutive + " 次）：" + error
                    + "（对局服务不受影响；恢复后自动继续）");
            }
        }

        /// <summary>停止超时（线程未在有界等待内退出）：如实告警——此时停止位/HttpClient 有意不释放
        /// （释放会让存活线程抛未处理异常），见 <see cref="HttpSubmitWorker.Dispose"/>。</summary>
        protected override void OnStopTimeout()
        {
            _log("[Lobby] 停止超时：心跳线程未在 " + HttpWorkerSupport.StopJoinTimeoutMs
                + "ms 内退出（在途请求未返回）——不释放其停止位/HttpClient，避免关闭阶段未处理异常");
        }

        /// <summary>宿主明细 → wire DTO（null 保持 null——"未上报"语义两端一致）。</summary>
        private static List<LobbyRoomEntry> ToRoomDtos(ServerHost.RoomSummary[] rooms)
        {
            if (rooms == null) return null;
            var result = new List<LobbyRoomEntry>(rooms.Length);
            for (int i = 0; i < rooms.Length; i++)
            {
                result.Add(new LobbyRoomEntry
                {
                    RoomId = rooms[i].RoomId,
                    Phase = rooms[i].Phase,
                    Players = rooms[i].Players,
                    ExpectedPlayers = rooms[i].ExpectedPlayers,
                    MapId = rooms[i].MapId,
                });
            }
            return result;
        }

        private static long ReadHeartbeatTtlMs(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return 0;
            try
            {
                var response = JsonConvert.DeserializeObject<InstanceRegisterResponse>(body, WireJsonSettings.Value);
                return response == null ? 0 : response.HeartbeatTtlMs;
            }
            catch (Exception)
            {
                return 0;   // 响应形状异常只影响读 TTL，不影响心跳成功判定
            }
        }

    }
}
