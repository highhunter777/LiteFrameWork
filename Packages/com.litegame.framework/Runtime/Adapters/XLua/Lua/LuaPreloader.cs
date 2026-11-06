using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteFramework
{
    /// <summary>
    /// Lua 全量预载器。
    /// </summary>
    public sealed class LuaPreloader
    {
        private readonly string _luaDir;                   // 产品 Lua 根（装配根注入——框架不写死产品路径）
        private readonly Func<string, CancellationToken, UniTask<byte[]>> _bytesProvider;   // 必注入（内容租约）
        private readonly Func<string[]> _listLuaFiles;    // 必注入（装配点绑定的清单来源）
        private readonly ICandidateLuaSource _candidateLua; // 可空：已确认候选整套脚本（H4-3）
        private readonly Dictionary<string, byte[]> _scripts = new Dictionary<string, byte[]>(256);

        /// <summary>已预载脚本（require 路径 → 字节；loader 只读）。</summary>
        public IReadOnlyDictionary<string, byte[]> Scripts => _scripts;

        public int Count => _scripts.Count;

        /// <param name="bytesProvider">字节通道（主链经内容租约）。**必注入**。</param>
        /// <param name="listLuaFiles">清单通道（装配点绑定——返回 .lua 资产路径数组；
        /// 以发布清单替换绑定时本类零改动）。**必注入**。</param>
        public LuaPreloader(Func<string, CancellationToken, UniTask<byte[]>> bytesProvider,
            Func<string[]> listLuaFiles, string luaDir, ICandidateLuaSource candidateLua = null)
        {
            _bytesProvider = bytesProvider;
            _listLuaFiles = listLuaFiles;
            _luaDir = luaDir ?? throw new ArgumentNullException(nameof(luaDir),
                "Lua 根目录必填——由产品装配根注入（框架不写死产品路径）");
            _candidateLua = candidateLua;
        }

        public async UniTask PreloadAllAsync(CancellationToken ct = default)
        {
            _scripts.Clear();                              // DevReload 重入语义：清了再来

            // 候选 lua/ 段（H4-3）：已确认候选整套脚本优先；不读 tag 清单、不回落静态门面。
            if (_candidateLua != null && _candidateLua.IsActive)
            {
                if (!_candidateLua.TryLoadAll(out IReadOnlyDictionary<string, byte[]> candidateScripts, out string problem))
                    throw new InvalidOperationException("候选 Lua 预载失败：" + problem);

                foreach (var pair in candidateScripts)
                {
                    ct.ThrowIfCancellationRequested();
                    if (_scripts.ContainsKey(pair.Key))
                        throw new InvalidOperationException(
                            $"Lua 预载重复 key:{pair.Key}（候选 lua/ 段自身重复——候选集合被污染）");
                    _scripts[pair.Key] = pair.Value;
                }

                Log.Info($"Lua 全量预载完成（候选 lua/）：{Count} 个文件", LogTag.Lua);
                return;
            }

            var paths = ListLuaFilePaths();
            if (paths == null || paths.Length == 0)
                throw new InvalidOperationException("Lua 预载清单为空——清单绑定/收集组 LiteGameLua 的 lua tag 未生效");

            foreach (var assetPath in paths)
            {
                if (!assetPath.EndsWith(".lua", StringComparison.Ordinal))
                    continue;
                var key = ToRequireKey(assetPath);
                ct.ThrowIfCancellationRequested();
                if (_scripts.ContainsKey(key))            // 候选完整性：重复 key 显性拒绝（不静默覆盖）
                    throw new InvalidOperationException($"Lua 预载重复 key:{key}（同一 require 路径两个来源——候选集合被污染）");
                _scripts[key] = await LoadBytesAsync(assetPath, ct);
            }
            Log.Info($"Lua 全量预载完成：{Count} 个文件", LogTag.Lua);
        }

        /// <summary>
        /// 清单来源：**必须注入** <c>listLuaFiles</c>（装配点绑定内容适配器）。
        /// </summary>
        private string[] ListLuaFilePaths()
        {
            if (_listLuaFiles == null)
                throw new InvalidOperationException(
                    "LuaPreloader 未注入 listLuaFiles——清单来源须在装配点绑定（内容适配器），" +
                    "本类不回落直读 YooAsset（§5.1 逻辑边界）");
            return _listLuaFiles() ?? Array.Empty<string>();
        }

        /// <summary>
        /// 字节通道：**必须注入** <c>bytesProvider</c>（内容租约）。
        /// 同 <see cref="ListLuaFilePaths"/>——不回落静态门面，避免本类认识内容适配器。
        /// </summary>
        private UniTask<byte[]> LoadBytesAsync(string assetPath, CancellationToken ct)
        {
            if (_bytesProvider == null)
                throw new InvalidOperationException(
                    "LuaPreloader 未注入 bytesProvider——字节通道归内容服务（§5.1 逻辑边界）");
            return _bytesProvider(assetPath, ct);
        }

        /// <summary>资源路径 → require 路径（loader 的 filepath 契约）。</summary>
        public string ToRequireKey(string assetPath)
        {
            if (assetPath == null) throw new ArgumentNullException(nameof(assetPath));
            if (assetPath.StartsWith(_luaDir, StringComparison.Ordinal) == false
                || assetPath.EndsWith(".lua", StringComparison.Ordinal) == false)
                throw new ArgumentException($"非 Lua 目录资源:{assetPath}");
            //去除后缀名作为键名
            return assetPath.Substring(_luaDir.Length, assetPath.Length - _luaDir.Length - 4);
        }
    }
}
