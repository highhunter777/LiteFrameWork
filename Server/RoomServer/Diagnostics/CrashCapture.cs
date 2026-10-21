using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace RoomServer.Diagnostics
{
    /// <summary>
    /// 进程级崩溃捕获（T0-2）：未处理异常 → 崩溃件（异常/堆栈 + HostLog 尾 + buildHash + 运行时身份）
    /// 落诊断目录，Windows 下尽力附 minidump。
    ///
    /// **只装一次、崩溃路径零抛**：钩子内任何失败都静默——现场捕获不得在崩溃中再崩。
    /// **不捕房间记录**：崩溃时房间线程可能停在半步，跨线程封印不安全；记录证据由
    /// mismatch 即时 flush 与房间终态路径兜底（彼时线程边界清晰）。
    /// 安装面是进程级 AppDomain 事件——状态只用于防重复安装（诊断设施的唯一全局面）。
    /// </summary>
    public static class CrashCapture
    {
        private static readonly object Gate = new object();
        private static bool _installed;

        /// <summary>挂 UnhandledException 钩子（诊断启用时由组合根调用；缺参 = 不装，行为零变化）。</summary>
        public static void Install(string directory, string buildHash, int logTailLines = 200)
        {
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(buildHash)) return;
            lock (Gate)
            {
                if (_installed) return;
                _installed = true;
            }
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
                TryWrite(directory, buildHash, logTailLines, args.ExceptionObject as Exception, writeMinidump: true);
        }

        /// <summary>
        /// 写崩溃件（可测）：<c>crash-&lt;时间戳&gt;-&lt;随机&gt;.json</c>（+ 可选 <c>.dmp</c> 尽力）。
        /// 返回 json 路径；任何失败返回 null。
        /// </summary>
        public static string TryWrite(string directory, string buildHash, int logTailLines,
            Exception exception, bool writeMinidump = false)
        {
            try
            {
                System.IO.Directory.CreateDirectory(directory);
                string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
                string json = Path.Combine(directory, $"crash-{stamp}-{Guid.NewGuid():N}.json");
                File.WriteAllText(json, BuildJson(buildHash, logTailLines, exception), new UTF8Encoding(false));
                if (writeMinidump)
                    TryWriteMinidump(Path.Combine(directory, $"crash-{stamp}-{Guid.NewGuid():N}.dmp"));
                return json;
            }
            catch
            {
                return null;
            }
        }

        private static string BuildJson(string buildHash, int logTailLines, Exception exception)
        {
            return JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                capturedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                buildHash,
                runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                exception = exception == null ? null : new
                {
                    type = exception.GetType().FullName,
                    message = exception.Message,
                    stackTrace = exception.StackTrace,
                    inner = exception.InnerException == null ? null : new
                    {
                        type = exception.InnerException.GetType().FullName,
                        message = exception.InnerException.Message,
                    },
                },
                hostLogTail = HostLog.RecentTail(logTailLines),
            }, new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            });
        }

        /// <summary>尽力写 minidump（Windows；失败静默）。崩溃现场的原生堆栈归它，托管语义归 json 件。</summary>
        private static void TryWriteMinidump(string path)
        {
            try
            {
                if (!OperatingSystem.IsWindows()) return;
                using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                MiniDumpWriteDump(GetCurrentProcess(), GetCurrentProcessId(),
                    stream.SafeFileHandle, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                // dump 是尽力而为：磁盘满/权限不足不应让 json 件也丢
            }
        }

        [DllImport("dbghelp.dll", SetLastError = true)]
        private static extern bool MiniDumpWriteDump(IntPtr hProcess, uint processId,
            Microsoft.Win32.SafeHandles.SafeFileHandle hFile, uint dumpType,
            IntPtr exceptionParam, IntPtr callbackParam, IntPtr callbackParam2);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();
    }
}
