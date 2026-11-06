using System;
using System.Collections.Generic;

namespace LiteGame.Modules.Config
{
    /// <summary>
    /// Bridge 能力声明（《热更与内容发布专项设计》§10"BridgeApiVersion/能力集合覆盖方法签名、语义、 委托、IL2CPP/AOT 适配与裁剪保留）。
    /// </summary>
    public sealed class BridgeCapabilitySet
    {
        /// <summary>当前 Bridge API 版本（对应 <see cref="ReleaseCompatibility.BridgeApiVersion"/> 的下限比较）。</summary>
        public int ApiVersion { get; }

        private readonly HashSet<string> _capabilities;

        public BridgeCapabilitySet(int apiVersion, IEnumerable<string> capabilities)
        {
            ApiVersion = apiVersion;
            _capabilities = new HashSet<string>(StringComparer.Ordinal);
            if (capabilities != null)
            {
                foreach (string c in capabilities)
                    if (!string.IsNullOrEmpty(c)) _capabilities.Add(c);
            }
        }

        /// <summary>已声明的能力数（诊断）。</summary>
        public int Count => _capabilities.Count;

        /// <summary>是否具备某能力（§10"新 Lua 引用旧包不存在的能力必须在执行前拒绝"）。</summary>
        public bool Has(string capability)
            => !string.IsNullOrEmpty(capability) && _capabilities.Contains(capability);

        /// <summary>
        /// 当前装配的默认能力集。
        /// </summary>
        public static BridgeCapabilitySet Default() => new BridgeCapabilitySet(
            apiVersion: LiteFramework.UI.UIApiManifest.ApiVersion,
            capabilities: new[]
            {
                "bridge.data",      // Bridge.data 门面（配置/数值读取）
                "bridge.ui",        // Bridge.ui 门面（页面打开/替换/关闭，按 lua_path）
                "bridge.content",   // Bridge.content 门面（注册表读取）
                "bridge.text",      // Bridge.text 门面（LText 取文/切语言——批C-②）
                "bridge.meta",      // Bridge.meta 门面（联机入口链路——批C-③）
                "bridge.settings",  // Bridge.settings 门面（音量/音效/画质——批C-⑥）
                "bridge.room",      // Bridge.room 门面（房间会话：房主事实/提前开局/席位名单/房规模）
                "bridge.battle",    // Bridge.battle 门面（B3 只读事实：规则四值/排行口径/结算名前缀）
            });
    }
}
