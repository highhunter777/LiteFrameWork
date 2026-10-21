using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using LiteGame.Toolchain;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor
{
    /// <summary>
    /// GM 工具箱·本地联测页（第五页件）：自开服务器 × 编辑器/exe 联测的**图形化一键面**
    /// （《项目说明/07-本地联测》§5 的一键链图形版）。
    ///
    /// **实时刷新自管**：壳的 <c>Poll</c> 只在 Play 中节拍——本页最有用的恰是编辑态，故自挂
    /// <c>EditorApplication.update</c>（1s 节流）：重探针（进程/HTTP/TCP/日志尾块）走后台任务写快照字段，
    /// 主线程消费（不卡编辑器）；完成后请求窗口重绘。探针全部**只读**，不影响两端进程。
    ///
    /// **起停语义**：Start = 停旧（如有）→ <c>dotnet build</c>（解锁后再编）→ 写 bin 侧
    /// appsettings（Meta 密钥——键值只落输出目录，实钥永不入库）→ 起 apphost（进程名
    /// MetaServer/RoomServer，WorkingDirectory=仓库根——配置/表/Outbox 相对路径与手动运行同形），
    /// stdout/stderr 重定向到 <c>deploy/logs/*.log</c> 由本页尾随。Stop = Kill——结算 Outbox
    /// 有重启恢复面（下次启动续投），Kill 不丢已收帧；优雅排空链归控制台/服务化形态。
    ///
    /// **单一来源 = deploy/.env**（与 scripts/local/local-chain.ps1 同一份键文件；本页只读它，
    /// 不另设第二处配置）。
    /// </summary>
    internal sealed class GmLocalChainPage : IGmPage
    {
        private const string EnvFileRelative = "deploy\\.env";
        private const string DeployArgsRelative = "Assets/StreamingAssets/deploy.args";
        private const string LogDirRelative = "deploy/logs";
        private const string MetaLogName = "meta.log";
        private const string RoomLogName = "room.log";

        /// <summary>状态标记列宽——容得下 "[ON]"/"[--]" 四字符（24 会把右括号裁掉）。</summary>
        private const int MarkerWidth = 32;

        /// <summary>.env 未给 ROOM_PORT 时的兜底——与客户端 F9 直连端口同源（本地联测两者必须相等）。</summary>
        private const int DefaultRoomPort = ProcedureMatch.TestPort;

        private const int DefaultMongoPort = 27017;

        /// <summary>页内尾随的日志尾块字节数（两端各一份）。</summary>
        private const int LogTailBytes = 4096;

        private string _root;
        private string _envFile;
        private volatile bool _probing;
        private volatile bool _dirty;
        private double _nextTick;

        // ---- 后台探针快照（主线程读，后台写——单写者+易失读足够）----
        private volatile ProbeSnapshot _snap = new ProbeSnapshot();
        private Process _metaProc;
        private Process _roomProc;

        private Vector2 _logScroll;
        private string _lastAction = "";

        private sealed class ProbeSnapshot
        {
            public string BuildHash = "-";
            public bool MetaProc, MetaLive, RoomProc, RoomPortBound, MongoUp;
            public int RoomPort = DefaultRoomPort;
            public string MongoLine = "MongoDB";
            public string BakedArgs = "(deploy.args 未烙入——init 后生成)";
            public string MetaLog = "";
            public string RoomLog = "";
            public string LastError = "";
        }

        public GUIContent Title => new GUIContent("本地联测");

        public void OnShow()
        {
            if (_root == null)
            {
                _root = Directory.GetParent(Application.dataPath).FullName;
                _envFile = Path.Combine(_root, EnvFileRelative);
            }
            EditorApplication.update -= OnEditorTick;
            EditorApplication.update += OnEditorTick;
            RequestProbe();
        }

        public void OnHide()
        {
            // 翻页/关窗即摘心跳（"非活动页不轮询"同语义）——探针是只读面，进程照常运行
            EditorApplication.update -= OnEditorTick;
        }

        public void Poll() { }      // 壳的 Play 态节拍——本页自管 update，不依赖 Play

        // ---- 编辑器心跳（1s 节流：探针后台 + 重绘主线程）----

        private void OnEditorTick()
        {
            if (EditorApplication.timeSinceStartup < _nextTick) return;
            _nextTick = EditorApplication.timeSinceStartup + 1.0;
            RequestProbe();
            if (_dirty)
            {
                _dirty = false;
                EditorWindow.GetWindow<LiteGameToolboxWindow>().Repaint();
            }
        }

        private void RequestProbe()
        {
            if (_probing) return;
            _probing = true;
            string root = _root;
            string envFile = _envFile;
            Task.Run(() =>
            {
                try
                {
                    _snap = Probe(root, envFile, _metaProc, _roomProc);
                    _dirty = true;
                }
                catch (Exception exception)
                {
                    var snapshot = new ProbeSnapshot { LastError = exception.Message };
                    _snap = snapshot;
                    _dirty = true;
                }
                finally
                {
                    _probing = false;
                }
            });
        }

        private static ProbeSnapshot Probe(string root, string envFile,
            Process metaProc, Process roomProc)
        {
            var s = new ProbeSnapshot();
            try
            {
                s.BuildHash = ReadBuildHash(root);
                var env = ReadDotEnv(envFile);
                s.BakedArgs = ReadDeployArgs(Path.Combine(root, DeployArgsRelative));

                s.MetaProc = IsAlive(metaProc) || Process.GetProcessesByName("MetaServer").Length > 0;
                s.RoomProc = IsAlive(roomProc) || Process.GetProcessesByName("RoomServer").Length > 0;

                s.MetaLive = ProbeHttp(env.TryGetValue("META_BIND", out var bind) ? bind : "http://127.0.0.1:5000", "/live");

                if (env.TryGetValue("MONGO_CONNECTION", out var mongo) && !string.IsNullOrWhiteSpace(mongo))
                {
                    var u = new Uri(mongo);
                    int port = u.Port > 0 ? u.Port : DefaultMongoPort;
                    s.MongoUp = TcpUp(u.Host, port);
                    s.MongoLine = s.MongoUp
                        ? $"MongoDB {u.Host}:{port} TCP 可达"
                        : $"MongoDB {u.Host}:{port} 不可达——Meta 起不来（持久层）";
                }
                else
                {
                    s.MongoUp = true;   // 持久化关 = 无依赖可失败
                    s.MongoLine = "MongoDB 未配（deploy/.env 持久化关——Meta 内存态可起）";
                }

                s.RoomPort = env.TryGetValue("ROOM_PORT", out var rp) && int.TryParse(rp, out int p)
                    ? p
                    : DefaultRoomPort;
                s.RoomPortBound = UdpBound(s.RoomPort);

                s.MetaLog = TailFile(Path.Combine(root, LogDirRelative, MetaLogName), LogTailBytes);
                s.RoomLog = TailFile(Path.Combine(root, LogDirRelative, RoomLogName), LogTailBytes);
            }
            catch (Exception ex)
            {
                s.LastError = ex.Message;
            }
            return s;
        }

        private static string ReadBuildHash(string root)
        {
            string gcs = Path.Combine(root, "Assets", "LiteNet", "Protocol", "BuildHash.g.cs");
            if (!File.Exists(gcs)) return "-";
            foreach (string line in File.ReadAllLines(gcs))
            {
                int i = line.IndexOf("public const string Value = \"", StringComparison.Ordinal);
                if (i >= 0)
                {
                    int start = i + "public const string Value = \"".Length;
                    int end = line.IndexOf('"', start);
                    if (end > start) return line.Substring(start, end - start);
                }
            }
            return "-";
        }

        private static Dictionary<string, string> ReadDotEnv(string path)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return map;
            foreach (string raw in File.ReadAllLines(path))
            {
                string t = raw.Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                int i = t.IndexOf('=');
                if (i <= 0) continue;
                map[t.Substring(0, i).Trim()] = t.Substring(i + 1).Trim();
            }
            return map;
        }

        private static string ReadDeployArgs(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "(deploy.args 未烙入——init 后生成)";
        }

        private static bool IsAlive(Process p)
        {
            try { return p != null && !p.HasExited; }
            catch { return false; }
        }

        private static bool ProbeHttp(string bind, string path)
        {
            try
            {
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(bind.TrimEnd('/') + path);
                req.Timeout = 1000;
                req.Method = "GET";
                using var resp = (System.Net.HttpWebResponse)req.GetResponse();
                return resp.StatusCode == System.Net.HttpStatusCode.OK;
            }
            catch { return false; }
        }

        private static bool TcpUp(string host, int port)
        {
            try
            {
                using var c = new System.Net.Sockets.TcpClient();
                var t = c.ConnectAsync(host, port);
                return t.Wait(500) && c.Connected;
            }
            catch { return false; }
        }

        /// <summary>UDP 端口占用探测：枚举系统监听表（"绑得动 = 空闲"判据在 Windows 不成立——
        /// UDP 多重绑定默认允许、Mono 的 UdpClient 不设排他，实测服务器在跑时仍可绑上）。</summary>
        private static bool UdpBound(int port)
        {
            try
            {
                foreach (var ep in System.Net.NetworkInformation.IPGlobalProperties
                    .GetIPGlobalProperties().GetActiveUdpListeners())
                {
                    if (ep.Port == port) return true;
                }
                return false;
            }
            catch { return false; }
        }

        // ---- Draw ----

        public void Draw()
        {
            ProbeSnapshot s = _snap;

            EditorGUILayout.HelpBox(
                "本地联测一键面：init 生成密钥与客户端烙入 → Start 起两端（日志在下方实时尾随）→ 客户端 Play/双击即连。"
                + " 停止 = 结束进程（结算 Outbox 下次启动续投，不丢账）。键单一来源 = deploy/.env（与 local-chain.ps1 同一份）。",
                MessageType.Info);

            EditorGUILayout.LabelField("共享身份", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("BuildHash（两端同源）", s.BuildHash);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("MetaServer", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(Marker(s.MetaLive), GUILayout.Width(MarkerWidth));
                GUILayout.Label(s.MetaLive ? "RUNNING (/live 200)" : (s.MetaProc ? "process alive, /live not answering" : "not running"));
                if (GUILayout.Button("Start", GUILayout.Width(70))) StartMeta();
                if (GUILayout.Button("Stop", GUILayout.Width(70))) StopMeta();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("RoomServer", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(Marker(s.RoomPortBound), GUILayout.Width(MarkerWidth));
                GUILayout.Label(s.RoomPortBound ? $"RUNNING (UDP {s.RoomPort} bound)" : (s.RoomProc ? "process alive, port not bound" : "not running"));
                if (GUILayout.Button("Start", GUILayout.Width(70))) StartRoom();
                if (GUILayout.Button("Stop", GUILayout.Width(70))) StopRoom();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("依赖与烙入", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(Marker(s.MongoUp), GUILayout.Width(MarkerWidth));
                GUILayout.Label(s.MongoLine);
            }
            EditorGUILayout.LabelField("deploy.args（客户端烙入）");
            DrawWrappedValue(s.BakedArgs);

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("init（生成密钥+烙入，已存在不动）")) InitKeys();
                if (GUILayout.Button("打开 .env")) RevealEnv();
                if (GUILayout.Button("打开日志目录")) RevealLogs();
            }
            if (!string.IsNullOrEmpty(_lastAction))
                EditorGUILayout.HelpBox(_lastAction, MessageType.None);
            if (!string.IsNullOrEmpty(s.LastError))
                EditorGUILayout.HelpBox("探针错误：" + s.LastError, MessageType.Warning);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("服务器日志（实时尾随——最新在下；可选中复制）", EditorStyles.boldLabel);
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(240));
            EditorGUILayout.LabelField("meta.log（MetaServer）", EditorStyles.boldLabel);
            DrawSelectableLog(s.MetaLog, "(meta.log 暂无内容——Start 后生成)");
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("room.log（RoomServer）", EditorStyles.boldLabel);
            DrawSelectableLog(s.RoomLog, "(room.log 暂无内容——Start 后生成)");
            EditorGUILayout.EndScrollView();
        }

        private static string Marker(bool ok)
        {
            return ok ? "[ON]" : "[--]";
        }

        /// <summary>只读多行值块（可选中复制）：高度按内容算——LabelField 只留单行高，多行值第二行会被裁半行。</summary>
        private static void DrawWrappedValue(string text)
        {
            float width = Mathf.Max(80f, EditorGUIUtility.currentViewWidth - 24f);
            float height = Mathf.Max(
                EditorStyles.textArea.CalcHeight(new GUIContent(text), width),
                EditorGUIUtility.singleLineHeight + 6f);
            EditorGUILayout.TextArea(text, GUILayout.Height(height));
        }

        /// <summary>只读可选中日志块：用 SelectableLabel（非 TextArea——TextArea 会接管滚轮，
        /// 外层 ScrollView 滚不动）；高度按内容全量展开，滚动交给外层。</summary>
        private static void DrawSelectableLog(string text, string placeholder)
        {
            if (string.IsNullOrEmpty(text)) text = placeholder;
            float width = Mathf.Max(80f, EditorGUIUtility.currentViewWidth - 48f);
            float height = Mathf.Max(
                EditorStyles.textArea.CalcHeight(new GUIContent(text), width),
                EditorGUIUtility.singleLineHeight + 6f);
            EditorGUILayout.SelectableLabel(text, EditorStyles.textArea, GUILayout.Height(height));
        }

        // ---- 动作（主线程；起停/构建/写件）----

        private void InitKeys()
        {
            string envText = Path.Combine(_root, "deploy", ".env");
            if (File.Exists(envText))
            {
                _lastAction = "deploy/.env 已存在（保持机器密钥——重生成请删文件或用 -Force 走脚本）";
                return;
            }
            RunHidden("scripts/local/local-chain.ps1 init");
            _lastAction = "init 完成（local-chain.ps1）——密钥与客户端烙入已生成";
            RequestProbe();
        }

        /// <summary>隐藏式调用 local-chain.ps1（复用单一来源逻辑；同步等待，init 秒级）。</summary>
        private void RunHidden(string scriptRelative)
        {
            var psi = new ProcessStartInfo("powershell",
                "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(_root, scriptRelative.Replace('/', '\\')) + "\"")
            {
                WorkingDirectory = _root,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            p.WaitForExit(30_000);
        }

        private void RevealEnv()
        {
            string envText = Path.Combine(_root, "deploy", ".env");
            if (File.Exists(envText)) EditorUtility.RevealInFinder(envText);
            else _lastAction = "deploy/.env 不存在——先点 init";
        }

        private void RevealLogs()
        {
            string dir = Path.Combine(_root, LogDirRelative);
            if (Directory.Exists(dir)) EditorUtility.RevealInFinder(dir);
            else _lastAction = "日志目录未生成——先 Start 任一端";
        }

        private void StartMeta()
        {
            if (IsAlive(_metaProc) || Process.GetProcessesByName("MetaServer").Length > 0)
            {
                _lastAction = "MetaServer 已在运行";
                return;
            }
            if (EnsureBuilt("Server\\MetaServer\\MetaServer.csproj")) return;
            WriteBinAppSettings();
            _metaProc = LaunchCaptured(
                Path.Combine(_root, "Server", "MetaServer", "bin", "Debug", "net8.0", "MetaServer.exe"),
                "--bind " + EnvOf("META_BIND", "http://127.0.0.1:5000"),
                LogPath(MetaLogName));
            _lastAction = "MetaServer started（日志尾随见下）";
        }

        private void StartRoom()
        {
            if (IsAlive(_roomProc) || Process.GetProcessesByName("RoomServer").Length > 0)
            {
                _lastAction = "RoomServer 已在运行";
                return;
            }
            if (EnsureBuilt("Server\\RoomServer\\RoomServer.csproj")) return;
            var env = ReadDotEnv(_envFile);
            string args = "--config " + (env.TryGetValue("ROOM_CONFIG", out var rc) ? rc : "Config/roomserver.json")
                + " --port " + (env.TryGetValue("ROOM_PORT", out var rp) && int.TryParse(rp, out int port) ? port : DefaultRoomPort)
                + " --envelope-key " + env.GetValueOrDefault("ENVELOPE_PSK")
                + " --ticket-key l1:" + env.GetValueOrDefault("LOBBY_TICKET_KEY")
                + " --lobby-instance-key " + env.GetValueOrDefault("LOBBY_INSTANCE_KEY")
                + " --settlement-instance-key " + env.GetValueOrDefault("SETTLEMENT_KEY");
            _roomProc = LaunchCaptured(
                Path.Combine(_root, "Server", "RoomServer", "bin", "Debug", "net8.0", "RoomServer.exe"),
                args,
                LogPath(RoomLogName));
            _lastAction = "RoomServer started（日志尾随见下）";
        }

        private void StopMeta()
        {
            foreach (var p in Process.GetProcessesByName("MetaServer"))
            {
                p.Kill();       // apphost 直起单进程（无子进程树——netstandard2.1 无 Kill(bool)）
                p.Dispose();
            }
            _metaProc = null;
            _lastAction = "MetaServer stopped（结算 Outbox 下次启动续投）";
        }

        private void StopRoom()
        {
            foreach (var p in Process.GetProcessesByName("RoomServer"))
            {
                p.Kill();       // 同上：单进程 Kill
                p.Dispose();
            }
            _roomProc = null;
            _lastAction = "RoomServer stopped（结算 Outbox 下次启动续投）";
        }

        // ---- 基础设施 ----

        private bool EnsureBuilt(string csprojRelative)
        {
            string csproj = Path.Combine(_root, csprojRelative.Replace('\\', '/'));
            try
            {
                var psi = new ProcessStartInfo("dotnet", "build \"" + csproj + "\" --nologo -v q")
                {
                    WorkingDirectory = _root,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                p.WaitForExit();
                if (p.ExitCode != 0)
                {
                    _lastAction = "build failed（" + csprojRelative + "）——查看编译输出";
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _lastAction = "build 异常：" + ex.Message;
                return true;
            }
        }

        private void WriteBinAppSettings()
        {
            var env = ReadDotEnv(_envFile);
            string bin = Path.Combine(_root, "Server", "MetaServer", "bin", "Debug", "net8.0", "appsettings.json");
            string json = "{\n" +
                "  \"Meta\": {\n" +
                "    \"BindAddress\": \"" + EnvOf("META_BIND", "http://127.0.0.1:5000") + "\",\n" +
                "    \"MongoConnectionString\": \"" + env.GetValueOrDefault("MONGO_CONNECTION") + "\",\n" +
                "    \"MongoDatabaseName\": \"" + env.GetValueOrDefault("MONGO_DB") + "\",\n" +
                "    \"AuthSigningKeyBase64\": \"" + env.GetValueOrDefault("AUTH_SIGNING_KEY") + "\",\n" +
                "    \"LobbyInstanceKeyBase64\": \"" + env.GetValueOrDefault("LOBBY_INSTANCE_KEY") + "\",\n" +
                "    \"LobbyTicketKeyBase64\": \"" + env.GetValueOrDefault("LOBBY_TICKET_KEY") + "\",\n" +
                "    \"LobbyTicketKeyId\": \"l1\",\n" +
                "    \"LobbyTicketAudience\": \"lobby\",\n" +
                "    \"SettlementInstanceKeyBase64\": \"" + env.GetValueOrDefault("SETTLEMENT_KEY") + "\",\n" +
                "    \"EnableSampleEndpoints\": false\n" +
                "  }\n}";
            Directory.CreateDirectory(Path.GetDirectoryName(bin));
            File.WriteAllText(bin, json);
        }

        private string EnvOf(string key, string fallback)
        {
            var env = ReadDotEnv(_envFile);
            return env.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;
        }

        /// <summary>日志路径单源：Start 写入面与页内尾随面取同一份（改一处不会漏另一处）。</summary>
        private string LogPath(string logName)
        {
            return Path.Combine(_root, LogDirRelative, logName);
        }

        /// <summary>起 apphost 并重定向输出到日志文件（GUI 尾随源——无独立控制台窗口）。</summary>
        private Process LaunchCaptured(string exePath, string args, string logPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            var psi = new ProcessStartInfo(exePath, args)
            {
                WorkingDirectory = _root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var p = Process.Start(psi);
            var logLock = new object();
            void Append(string line)
            {
                if (string.IsNullOrEmpty(line)) return;
                lock (logLock) File.AppendAllText(logPath, line + "\n");
            }
            p.OutputDataReceived += (_, e) => Append(e.Data);
            p.ErrorDataReceived += (_, e) => Append(e.Data);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            return p;
        }

        // ---- 日志尾随（后台探针内读取——主线程只消费快照）----

        private static string TailFile(string path, int maxBytes)
        {
            if (!File.Exists(path)) return "";
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                int len = (int)Math.Min(maxBytes, fs.Length);
                fs.Seek(-len, SeekOrigin.End);
                var buf = new byte[len];
                fs.Read(buf, 0, len);
                return Encoding.UTF8.GetString(buf);
            }
            catch (IOException) { return "(log busy)"; }
        }
    }
}
