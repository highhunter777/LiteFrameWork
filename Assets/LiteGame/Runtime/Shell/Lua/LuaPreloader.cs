using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// Lua 全量预载器（M3 步骤 2.2）。**loader 是同步签名 → 启动期全量预载是咽喉**（设计方案 §4.2）：
    /// require 链上任何一个未缓存文件都意味着运行中途炸——不能带缺口进 Main（§3.4 致命级）。
    /// 清单来源（§1b 实测定案）：**tag `lua` 经 `GetAssetInfos("lua")`**——3.0.5 该重载即按 tag 查询，
    /// **无目录枚举 API**（fallback 不存在，收集器 AssetTags 必须配 `lua`，已配）。
    /// key = require 路径：剥 `Assets/LiteGame/Lua/` 前缀与 `.lua` 后缀（如 `ui/UIMain`、`cfg/tbuiform`）。
    /// 生命周期：Preload 锚点构造并填充（2.4 接线），DevReload 时清空重载（§2.7）。
    ///
    /// C1-⑨ 候选通道（《热更与内容发布专项设计》§10 候选阶段最小落点）：
    /// - **字节通道可注入**：主链经 IContentService 租约（代次/引用统一；提取字节即释放源资产，§9）；
    ///   两个通道都**必须注入**（见下——2026-09-26 起不再有静态门面回落）。
    /// - **候选完整性**：预载集合即依赖闭包（全量 .lua 进缓存——require 不可能落空）；重复 key 显性拒绝
    ///   （静默覆盖 = 候选集合被污染）。深度候选验证（语法/导出/Bridge 能力/受控验证 env）随热更批。
    /// </summary>
    public sealed class LuaPreloader
    {
        /// <summary>Lua 资源收集目录（YooAsset location 前缀；跨平台恒为 Assets 路径——单源：Editor 的 LiteGameIgnoreRule 亦引用此常量）。</summary>
        public const string LuaDir = "Assets/LiteGame/Lua/";

        private readonly Func<string, CancellationToken, UniTask<byte[]>> _bytesProvider;   // 必注入（内容租约）
        private readonly Func<string[]> _listLuaFiles;    // 必注入（装配点绑定的清单来源）
        private readonly Dictionary<string, byte[]> _scripts = new Dictionary<string, byte[]>(256);

        /// <summary>已预载脚本（require 路径 → 字节；loader 只读）。</summary>
        public IReadOnlyDictionary<string, byte[]> Scripts => _scripts;

        public int Count => _scripts.Count;

        /// <param name="bytesProvider">字节通道（主链经内容租约）。**必注入**。</param>
        /// <param name="listLuaFiles">清单通道（G1 通用表现批：装配点绑定——返回 .lua 资产路径数组；
        /// 热更批以发布清单替换绑定时本类零改动）。**必注入**。</param>
        public LuaPreloader(Func<string, CancellationToken, UniTask<byte[]>> bytesProvider,
            Func<string[]> listLuaFiles)
        {
            _bytesProvider = bytesProvider;
            _listLuaFiles = listLuaFiles;
        }

        public async UniTask PreloadAllAsync(CancellationToken ct = default)
        {
            _scripts.Clear();                              // DevReload 重入语义：清了再来
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
            Log.Info($"Lua 全量预载完成：{Count} 个文件", "Lua");
        }

        /// <summary>
        /// 清单来源：**必须注入** <c>listLuaFiles</c>（装配点绑定内容适配器）。
        ///
        /// 原先留了一条"未注入则回落静态 `AssetService.Package.GetAssetInfos("lua")`"的迁移期兼容——
        /// 2026-09-26 删除（《客户端总设计》§5.1"先形成逻辑边界"）：那条回落让 **Lua 层硬依赖
        /// YooAsset 类型**（`AssetService.Package` 是 `ResourcePackage`），asmdef 拆不开（成环）。
        /// 需要读清单时组装配点的委托，本类只认 `Func<string[]>`。
        /// </summary>
        private string[] ListLuaFilePaths()
        {
            if (_listLuaFiles == null)
                throw new InvalidOperationException(
                    "LuaPreloader 未注入 listLuaFiles——清单来源已在装配点绑定（内容适配器），" +
                    "本类不再回落直读 YooAsset（§5.1 逻辑边界）");
            return _listLuaFiles() ?? Array.Empty<string>();
        }

        /// <summary>
        /// 字节通道：**必须注入** <c>bytesProvider</c>（内容租约）。
        ///
        /// 同 <see cref="ListLuaFilePaths"/>：原先的 `AssetService.LoadRawFileBytesAsync` 回落
        /// 已删除——它同样让本类认识内容适配器。
        /// </summary>
        private UniTask<byte[]> LoadBytesAsync(string assetPath, CancellationToken ct)
        {
            if (_bytesProvider == null)
                throw new InvalidOperationException(
                    "LuaPreloader 未注入 bytesProvider——字节通道归内容服务（§5.1 逻辑边界）");
            return _bytesProvider(assetPath, ct);
        }

        /// <summary>资源路径 → require 路径（loader 的 filepath 契约）。</summary>
        public static string ToRequireKey(string assetPath)
        {
            if (assetPath == null) throw new ArgumentNullException(nameof(assetPath));
            if (assetPath.StartsWith(LuaDir, StringComparison.Ordinal) == false
                || assetPath.EndsWith(".lua", StringComparison.Ordinal) == false)
                throw new ArgumentException($"非 Lua 目录资源:{assetPath}");
            //去除后缀名作为键名
            return assetPath.Substring(LuaDir.Length, assetPath.Length - LuaDir.Length - 4);
        }
    }
}
