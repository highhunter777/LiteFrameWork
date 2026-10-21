using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using LiteSim;
using RoomServer.Application;
using RoomServer.Diagnostics;

namespace RoomServer
{
    /// <summary>
    /// 运维（GM）HTTP 端点（《云端GM通道专项设计》§1/§4）：只绑运维可达地址（云上＝隧道地址
    /// <c>10.8.0.1</c>）、仅 POST、共享密钥鉴权、**缺省关闭**（<c>gm_port=0</c> 不创建本对象）。
    ///
    /// 与 <see cref="MetricsHttpListener"/> 同形态（HttpListener + 单后台线程、Start 失败由装配方告警降级），
    /// 但**是写面**，故多三重门禁：① 仅 POST（GET 一律 404，避免被顺手探测）；
    /// ② 密钥恒定时间比较，密钥为空 ⇒ 装配方根本不创建本对象；③ 每个动作打 <c>[GM]</c> 审计行。
    ///
    /// **线程契约**：本线程只做"鉴权 → 解析 → 入队"，动作经 <see cref="GmActionQueue"/> 交权威循环执行；
    /// 唯一例外是 <c>/gm/rooms</c> 读面（与 Lobby 心跳同族的近似快照口径，不进权威循环）。
    /// </summary>
    public sealed class GmHttpListener : IDisposable
    {
        /// <summary>密钥头名（部署契约：脚本与文档必须同名）。</summary>
        public const string KeyHeaderName = "X-GM-Key";

        /// <summary>请求体上限（字节）：动作载荷只有 room/player 两个字段，4 KB 绰绰有余——超限按恶意请求拒。</summary>
        private const int MaxBodyBytes = 4096;

        /// <summary>诊断下载的回传上限（字节）：现场包 GZip 后数 MB 级；超限拒绝防误拉日志巨件。</summary>
        private const long MaxDiagFetchBytes = 64L * 1024 * 1024;

        private readonly byte[] _keyBytes;
        private readonly Func<GmAction, GmReject?> _submit;
        private readonly Func<ServerHost.RoomSummary[]> _rooms;
        private readonly Func<DiagFileSummary[]> _diagList;
        private readonly Func<string, string> _diagResolve;
        private HttpListener _listener;
        private Thread _thread;

        /// <param name="keyBytes">共享密钥的 UTF-8 字节（**空 ⇒ 调用方不得创建本对象**）。</param>
        /// <param name="submit">入队回调：返回 null = 已入队；否则为拒绝原因（不执行）。</param>
        /// <param name="rooms">房间列表读面（近似快照）。</param>
        /// <param name="diagList">诊断文件清单读面（null = 本端点不开诊断下载）。</param>
        /// <param name="diagResolve">文件名 → 绝对路径（null = 不开；非法/不存在 = null 返回）。</param>
        public GmHttpListener(byte[] keyBytes, Func<GmAction, GmReject?> submit,
            Func<ServerHost.RoomSummary[]> rooms,
            Func<DiagFileSummary[]> diagList = null,
            Func<string, string> diagResolve = null)
        {
            _keyBytes = keyBytes ?? throw new ArgumentNullException(nameof(keyBytes));
            if (_keyBytes.Length == 0) throw new ArgumentException("GM 密钥不可为空（空 = 拒绝启用端点）", nameof(keyBytes));
            _submit = submit ?? throw new ArgumentNullException(nameof(submit));
            _rooms = rooms ?? throw new ArgumentNullException(nameof(rooms));
            _diagList = diagList;
            _diagResolve = diagResolve;
        }

        /// <summary>在指定端口启动端点（重复启动抛）。<paramref name="host"/> = 绑定面（云上取隧道地址；
        /// 通配地址由配置装载期拒绝，不到这里）。</summary>
        public void Start(int port, string host)
        {
            if (port <= 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port), port, "端口越界（允许 1..65535）");
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("绑定面不可为空（如 127.0.0.1 / 具体地址；通配地址已被配置层拒绝）", nameof(host));
            if (_listener != null)
                throw new InvalidOperationException("GM 端点已启动（一个宿主一个端点）");

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://{host}:{port}/");
            listener.Start();
            _listener = listener;
            _thread = new Thread(ServeLoop) { IsBackground = true, Name = "RoomServer.Gm" };
            _thread.Start();
        }

        private void ServeLoop()
        {
            HttpListener listener = _listener;
            while (listener != null && listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch (Exception) { break; }                        // Stop/Close 解除阻塞——退出

                try { Handle(ctx); }
                catch (Exception) { /* 单请求失败不杀端点线程 */ }
                finally { try { ctx.Response.Close(); } catch { } }
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            HttpListenerResponse resp = ctx.Response;
            resp.ContentType = "application/json; charset=utf-8";

            // 只认 POST：GET 一律 404——诊断/写面都不提供"顺手 curl 就能看"的读口
            if (!string.Equals(ctx.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                resp.StatusCode = 404;
                return;
            }

            string presented = ctx.Request.Headers[KeyHeaderName] ?? string.Empty;
            if (!KeyMatches(presented))
            {
                resp.StatusCode = 401;
                WriteJson(resp, "{\"ok\":false,\"error\":\"unauthorized\"}");
                Console.WriteLine("[GM] 拒绝：密钥不匹配（未授权请求）");
                return;
            }

            string path = ctx.Request.Url.AbsolutePath;
            if (string.Equals(path, "/gm/rooms", StringComparison.Ordinal))
            {
                resp.StatusCode = 200;
                WriteJson(resp, FormatRooms());
                Console.WriteLine("[GM] rooms/list");
                return;
            }

            if (string.Equals(path, "/gm/diag-list", StringComparison.Ordinal))
            {
                if (_diagList == null)
                {
                    resp.StatusCode = 404;
                    WriteJson(resp, "{\"ok\":false,\"error\":\"diagnostics disabled\"}");
                    return;
                }
                resp.StatusCode = 200;
                WriteJson(resp, FormatDiagList(_diagList()));
                Console.WriteLine("[GM] diag/list");
                return;
            }

            if (string.Equals(path, "/gm/diag-fetch", StringComparison.Ordinal))
            {
                HandleDiagFetch(ctx, ReadDiagFileName(ctx.Request));
                return;
            }

            string body = ReadBody(ctx.Request);
            string roomId = null;
            int playerId = -1;
            int flags = 0;
            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(body);
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("room", out JsonElement roomEl) && roomEl.ValueKind == JsonValueKind.String)
                            roomId = roomEl.GetString();
                        if (root.TryGetProperty("player", out JsonElement playerEl) && playerEl.ValueKind == JsonValueKind.Number)
                            playerId = playerEl.GetInt32();
                        // GM 规则：可给 flags（位掩码），也可给两个布尔（更可读）
                        bool hasFlags = root.TryGetProperty("flags", out JsonElement flagsEl)
                            && flagsEl.ValueKind == JsonValueKind.Number;
                        if (hasFlags) flags = flagsEl.GetInt32();
                        else
                        {
                            bool noDeath = root.TryGetProperty("noDeath", out JsonElement nd) && nd.ValueKind == JsonValueKind.True;
                            bool infiniteAmmo = root.TryGetProperty("infiniteAmmo", out JsonElement ia) && ia.ValueKind == JsonValueKind.True;
                            flags = (noDeath ? MatchTestRules.FlagNoDeath : 0)
                                | (infiniteAmmo ? MatchTestRules.FlagInfiniteAmmo : 0);
                        }
                    }
                }
                catch (JsonException)
                {
                    resp.StatusCode = 400;
                    WriteJson(resp, "{\"ok\":false,\"error\":\"bad json\"}");
                    return;
                }
            }

            switch (path)
            {
                case "/gm/start":
                    Dispatch(resp, GmAction.StartEarly(roomId), "start", roomId, playerId);
                    return;
                case "/gm/abort":
                    Dispatch(resp, GmAction.Abort(roomId), "abort", roomId, playerId);
                    return;
                case "/gm/kick":
                    if (playerId < 0)
                    {
                        resp.StatusCode = 400;
                        WriteJson(resp, "{\"ok\":false,\"error\":\"kick 需要 player 字段\"}");
                        return;
                    }
                    Dispatch(resp, GmAction.Kick(roomId, playerId), "kick", roomId, playerId);
                    return;
                case "/gm/rules":
                    // 只对测试房生效（ServerHost.GmSubmit 里判 _hiddenRooms）——普通房 409
                    Dispatch(resp, GmAction.SetRules(roomId, flags), "rules", roomId, -1);
                    return;
                case "/gm/settle-flush":
                    Dispatch(resp, GmAction.FlushSettlement(), "settle-flush", null, -1);
                    return;
                default:
                    resp.StatusCode = 404;
                    WriteJson(resp, "{\"ok\":false,\"error\":\"unknown action\"}");
                    return;
            }
        }

        private void Dispatch(HttpListenerResponse resp, in GmAction action, string name, string roomId, int playerId)
        {
            GmReject? reject = _submit(action);
            if (reject.HasValue)
            {
                GmReject r = reject.Value;
                resp.StatusCode = r.Status;
                // 宽松编码（不转义中文）：运维直接读响应，\uXXXX 形态不可读
                WriteJson(resp, "{\"ok\":false,\"error\":\""
                    + JsonEncodedText.Encode(r.Message, JavaScriptEncoder.UnsafeRelaxedJsonEscaping) + "\"}");
                Console.WriteLine($"[GM] 拒绝：action={name} room={roomId ?? "-"} player={playerId} → {r.Status} {r.Message}");
                return;
            }
            resp.StatusCode = 200;
            WriteJson(resp, "{\"ok\":true,\"queued\":true}");
            Console.WriteLine($"[GM] 入队：action={name} room={roomId ?? "-"} player={playerId}");
        }

        private bool KeyMatches(string presented)
        {
            byte[] given = Encoding.UTF8.GetBytes(presented);
            // 长度不等会提前返回（只泄露长度）——与信封侧 FixedEquals 同口径
            return given.Length == _keyBytes.Length && CryptographicOperations.FixedTimeEquals(given, _keyBytes);
        }

        private static string ReadBody(HttpListenerRequest request)
        {
            try
            {
                // 上限前置（Content-Length 可用就连接层先拒；chunked 无长度仍由读后复核兜底）
                if (request.ContentLength64 > MaxBodyBytes) return null;
                using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                string body = reader.ReadToEnd();
                return body != null && Encoding.UTF8.GetByteCount(body) > MaxBodyBytes ? null : body;
            }
            catch (Exception) { return null; }
        }

        private string FormatRooms()
        {
            ServerHost.RoomSummary[] rooms = _rooms();
            var sb = new StringBuilder();
            sb.Append("{\"ok\":true,\"rooms\":[");
            for (int i = 0; i < rooms.Length; i++)
            {
                if (i > 0) sb.Append(',');
                ServerHost.RoomSummary r = rooms[i];
                sb.Append("{\"room\":\"").Append(JsonEncodedText.Encode(r.RoomId ?? string.Empty, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString()).Append('"')
                  .Append(",\"phase\":").Append(r.Phase)
                  .Append(",\"players\":").Append(r.Players)
                  .Append(",\"expected\":").Append(r.ExpectedPlayers)
                  .Append(",\"map\":").Append(r.MapId).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>诊断文件清单（GM 读面）：名/大小/修改时刻。</summary>
        private static string FormatDiagList(DiagFileSummary[] files)
        {
            var sb = new StringBuilder();
            sb.Append("{\"ok\":true,\"files\":[");
            if (files != null)
            {
                for (int i = 0; i < files.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    DiagFileSummary f = files[i];
                    sb.Append("{\"name\":\"").Append(JsonEncodedText.Encode(f.Name ?? string.Empty, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString()).Append('"')
                      .Append(",\"bytes\":").Append(f.Bytes)
                      .Append(",\"lastWriteUtc\":\"").Append(f.LastWriteUtc.ToString("O")).Append("\"}");
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>diag-fetch 请求体：{"file": "name.json.gz"}（缺字段/坏 JSON = null）。</summary>
        private static string ReadDiagFileName(HttpListenerRequest request)
        {
            try
            {
                string body = ReadBody(request);
                if (string.IsNullOrEmpty(body)) return null;
                using JsonDocument doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
                if (!doc.RootElement.TryGetProperty("file", out JsonElement file)
                    || file.ValueKind != JsonValueKind.String) return null;
                return file.GetString();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 诊断文件回传：名字只作<b>目录直下文件名</b>解析（分隔符/穿越 = 400），不存在 = 404，
        /// 超过 <see cref="MaxDiagFetchBytes"/> = 413。原样字节流（application/octet-stream）。
        /// </summary>
        private void HandleDiagFetch(HttpListenerContext ctx, string fileName)
        {
            HttpListenerResponse resp = ctx.Response;
            if (_diagResolve == null)
            {
                resp.StatusCode = 404;
                resp.ContentType = "application/json; charset=utf-8";
                WriteJson(resp, "{\"ok\":false,\"error\":\"diagnostics disabled\"}");
                return;
            }

            if (string.IsNullOrEmpty(fileName)
                || fileName.IndexOf('/') >= 0 || fileName.IndexOf('\\') >= 0 || fileName.Contains(".."))
            {
                resp.StatusCode = 400;
                resp.ContentType = "application/json; charset=utf-8";
                WriteJson(resp, "{\"ok\":false,\"error\":\"bad file name\"}");
                Console.WriteLine($"[GM] 拒绝：diag-fetch 非法文件名");
                return;
            }

            string path = _diagResolve(fileName);
            if (path == null)
            {
                resp.StatusCode = 404;
                resp.ContentType = "application/json; charset=utf-8";
                WriteJson(resp, "{\"ok\":false,\"error\":\"not found\"}");
                Console.WriteLine($"[GM] diag-fetch 未命中：{fileName}");
                return;
            }

            try
            {
                using FileStream stream = File.OpenRead(path);
                if (stream.Length > MaxDiagFetchBytes)
                {
                    resp.StatusCode = 413;
                    resp.ContentType = "application/json; charset=utf-8";
                    WriteJson(resp, "{\"ok\":false,\"error\":\"file too large\"}");
                    Console.WriteLine($"[GM] 拒绝：diag-fetch 超限 file={fileName} bytes={stream.Length}");
                    return;
                }
                resp.StatusCode = 200;
                resp.ContentType = "application/octet-stream";
                resp.ContentLength64 = stream.Length;
                stream.CopyTo(resp.OutputStream);
                Console.WriteLine($"[GM] diag-fetch file={fileName} bytes={stream.Length}");
            }
            catch (Exception ex)
            {
                resp.StatusCode = 500;
                resp.ContentType = "application/json; charset=utf-8";
                WriteJson(resp, "{\"ok\":false,\"error\":\"io\"}");
                Console.WriteLine($"[GM] diag-fetch IO 异常：{ex.Message}");
            }
        }

        private static void WriteJson(HttpListenerResponse resp, string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            resp.ContentLength64 = body.LongLength;
            resp.OutputStream.Write(body, 0, body.Length);
        }

        public void Dispose()
        {
            HttpListener listener = _listener;
            _listener = null;
            try { listener?.Stop(); } catch { }
            try { listener?.Close(); } catch { }
            _thread = null;
        }
    }
}
