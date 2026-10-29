using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteFramework.UI;
using UnityEditor;
using UnityEngine;

namespace LiteGame.EditorTools.UI
{
    /// <summary>
    /// 令牌读面（《UI 编辑器工具链专项设计》§8.1「工具只读生成物与 prefab 引用」）：
    /// 从盘上 `UiThemeAsset` 装载颜色/文字令牌，供样式工具、校验规则与夹具共用。
    ///
    /// **委托壳（P2-3 B2 起解析核心单源＝<see cref="UiStyleResolver"/>〔LiteFramework.UI，运行时同源〕；
    /// 本类只保留编辑器装载面（AssetDatabase）与既有公开签名，调用点源兼容）。**
    ///
    /// 纪律：**不做静态缓存**——编辑器域重载后静态缓存会读到旧值；样式工具每次操作只装载一次，
    /// 之后把实例逐层下传（不逐节点重新装载）。资产缺失或类型不符时显式失败，不返回空集冒充成功。
    /// </summary>
    public sealed class UiTokenCatalog
    {
        private readonly UiStyleResolver _resolver;

        private UiTokenCatalog(UiStyleResolver resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        /// <summary>资产里记录的主题名（诊断/报告用）。</summary>
        public string Theme => _resolver.Theme;

        /// <summary>资产里记录的令牌源指纹（诊断/报告用）。</summary>
        public string SourceHash => _resolver.SourceHash;

        /// <summary>颜色令牌（按 ID `Ordinal` 排序，顺序稳定供遍历/断言）。</summary>
        public IReadOnlyList<KeyValuePair<string, Color>> Colors => _resolver.Colors;

        /// <summary>文字令牌（按 ID `Ordinal` 排序）。</summary>
        public IReadOnlyList<KeyValuePair<string, UiThemeTypographyEntry>> Typography => _resolver.Typography;

        /// <summary>按工程根相对路径装载（缺省＝<see cref="UiThemePaths.DefaultAssetPath"/>）。</summary>
        public static UiTokenCatalog Load(string assetPath = null)
        {
            string path = string.IsNullOrEmpty(assetPath) ? UiThemePaths.DefaultAssetPath : assetPath;
            UiThemeAsset asset = AssetDatabase.LoadAssetAtPath<UiThemeAsset>(path);
            if (asset == null)
            {
                string full = UiThemePaths.FullPath(path);
                if (!File.Exists(full)) throw new FileNotFoundException("主题资产不存在：" + path, full);
                throw new InvalidOperationException("主题资产类型不符（不是 UiThemeAsset）：" + path);
            }
            return FromAsset(asset);
        }

        /// <summary>从已加载资产构建（夹具可直接喂内存资产，不必落盘）。</summary>
        public static UiTokenCatalog FromAsset(UiThemeAsset asset)
            => new UiTokenCatalog(UiStyleResolver.FromAsset(asset));

        /// <summary>解析核心（单源）——需要 resolver 形态（如 Apply/结果枚举）的调用方从这里取。</summary>
        public UiStyleResolver Resolver => _resolver;

        /// <summary>取颜色令牌；未知 ID 返回 false（由调用方显式处理，不猜默认色）。</summary>
        public bool TryColor(string id, out Color value)
            => _resolver.TryColor(id, out value);

        /// <summary>取文字令牌；未知 ID 返回 false。</summary>
        public bool TryTypography(string id, out UiThemeTypographyEntry entry)
            => _resolver.TryTypography(id, out entry);
    }
}
