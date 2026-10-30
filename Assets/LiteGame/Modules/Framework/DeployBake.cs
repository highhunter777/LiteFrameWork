using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LiteGame
{
    /// <summary>
    /// 部署烙入读取件（构建流水线的客户端半边）：<c>StreamingAssets/deploy.args</c> → 参数全文。
    ///
    /// **文件形态**：每行一个 <c>-key=value</c>（<see cref="MetaDeployConfig"/> 与
    /// <see cref="LiteFramework.ContentDeployConfig"/> 的 token 形态——烙入文件就是**持久化的命令行参数**，
    /// 由 <c>scripts/build/build-player.py</c> 按 <c>BuildProfile.endpoints</c> 在构建前写入/清删）。
    /// **装载合并序**：命令行参数 &gt; 本烙入 &gt; 字段默认——合并在两装载器各自的
    /// <c>Load(args, bakedArgsText)</c> 纯函数内（本件只负责"把文本取出来"）。
    ///
    /// **平台读取通道**：Standalone/Editor 的 streamingAssetsPath 是普通文件路径——直读；
    /// **Android 的 StreamingAssets 位于 APK 内**，<see cref="Application.dataPath"/> 就是 APK 自身，
    /// 故按 zip 条目 <c>assets/deploy.args</c> 读（真同步、无帧泵依赖）。
    ///
    /// **读取契约**：返回 null ＝"无烙入"（缺文件/缺条目，正常路径）；抛异常 ＝读失败（退化为无烙入，
    /// 但留一次性 Warning）。**本实例只读一次**：引导期多处消费点共用同一实例的 memo（不再有可变静态）。
    /// </summary>
    public sealed class DeployBake
    {
        /// <summary>烙入文件名（StreamingAssets 下；内容 = 参数行流）。</summary>
        public const string FileName = "deploy.args";

        /// <summary>Android APK 内的 StreamingAssets 条目名（zip 条目以 assets/ 为前缀）。</summary>
        internal const string AndroidAssetsEntry = "assets/" + FileName;

        private readonly Func<string, string> _reader;
        private bool _loaded;
        private string _text;
        private bool _missingLogged;
        private bool _failedLogged;

        /// <param name="reader">读取缝（用例注入；默认走平台实现）。入参＝设备侧 deploy.args 路径。</param>
        public DeployBake(Func<string, string> reader = null) => _reader = reader ?? ReadPlatform;

        /// <summary>读烙入全文（**本实例只读一次**：首读结果（含 null）即缓存，引导期消费点共用）。
        /// 返回 null ＝无烙入（缺文件/缺条目），不报错不猜。</summary>
        public string ReadArgs()
        {
            if (_loaded) return _text;
            _loaded = true;

            string path = Path.Combine(Application.streamingAssetsPath, FileName);
            var watch = Stopwatch.StartNew();
            try
            {
                _text = _reader(path);
                watch.Stop();
                if (_text == null)
                {
                    if (!_missingLogged)
                    {
                        _missingLogged = true;
                        Debug.Log($"[DeployBake] missing: {FileName}（无烙入，走命令行/默认）");
                    }
                }
                else
                {
                    Debug.Log($"[DeployBake] read {_text.Length} chars in {watch.ElapsedMilliseconds}ms");
                }

                return _text;
            }
            catch (Exception ex)
            {
                watch.Stop();
                _text = null;
                if (!_failedLogged)
                {
                    _failedLogged = true;
                    Debug.LogWarning($"[DeployBake] read failed after {watch.ElapsedMilliseconds}ms: "
                        + $"{ex.GetType().Name}: {ex.Message}（退化为无烙入）");
                }

                return null;
            }
        }

        /// <summary>平台分发（唯一一处按平台分叉；<c>#if UNITY_ANDROID</c> 在本件白名单内）。</summary>
        private static string ReadPlatform(string path)
        {
#if UNITY_ANDROID
            return ReadZipEntry(Application.dataPath, AndroidAssetsEntry);
#else
            return ReadDesktopFile(path);
#endif
        }

        /// <summary>桌面/编辑器：普通文件直读（缺文件＝null）。</summary>
        internal static string ReadDesktopFile(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        /// <summary>从 APK（zip）里读一个条目；缺条目/缺包＝null（"无烙入"而非失败）。</summary>
        internal static string ReadZipEntry(string zipPath, string entryName)
        {
            if (!File.Exists(zipPath)) return null;
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                ZipArchiveEntry entry = archive.GetEntry(entryName);
                if (entry == null) return null;
                using (var reader = new StreamReader(entry.Open()))
                    return reader.ReadToEnd();
            }
        }
    }
}
