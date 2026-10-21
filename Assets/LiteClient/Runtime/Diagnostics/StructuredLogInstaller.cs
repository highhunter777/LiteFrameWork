#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System.IO;
using LiteFramework;
using LiteFramework.Diagnostics;
using UnityEngine;

namespace LiteClient
{
    /// <summary>
    /// 结构化日志的安装/卸载（dev 三宏内）。路径 = `persistentDataPath/logs`；
    /// 随 `Application.quitting` 收口（Editor 停止 Play 也会触发）。
    /// 失败不阻断启动：sink 的 IO/线程错误只进 `LastError`，业务照常跑。
    /// </summary>
    public static class StructuredLogInstaller
    {
        private static StructuredLogSink _sink;

        /// <summary>当前已安装的 sink（未安装为 null；只读，供 DevHUD 状态段）。</summary>
        public static StructuredLogSink Current => _sink;

        /// <summary>安装（幂等）；返回当前 sink（已安装则原样返回）。</summary>
        public static StructuredLogSink Install()
        {
            if (_sink != null) return _sink;
            string directory = Path.Combine(Application.persistentDataPath, "logs");
            var sink = new StructuredLogSink(directory);
            sink.Start();
            _sink = sink;
            Log.SetSink(sink);
            Application.quitting += OnQuitting;
            return sink;
        }

        private static void OnQuitting()
        {
            Application.quitting -= OnQuitting;
            Log.SetSink(null);
            StructuredLogSink sink = _sink;
            _sink = null;
            sink?.Dispose();
        }
    }
}
#endif
