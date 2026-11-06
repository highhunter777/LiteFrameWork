using System;
using System.Collections.Generic;
using LiteFramework;
using LiteGame.Modules.Config;

namespace LiteGame
{
    /// <summary>
    /// Bridge 能力健康探针（§10"新 Lua 引用旧包不存在的能力必须在执行前拒绝"）。
    /// </summary>
    public sealed class BridgeCapabilityHealthProbe : IHealthProbe
    {
        private readonly BridgeCapabilitySet _capabilities;
        private readonly Func<ReleaseManifest, IReadOnlyList<string>> _requiredCapabilities;

        public string Name => "bridge-capabilities";

        /// <param name="capabilities">本包具备的能力。</param>
        /// <param name="requiredCapabilities">清单 → 候选要求的能力集合（装配点/发布描述注入）。</param>
        public BridgeCapabilityHealthProbe(BridgeCapabilitySet capabilities,
            Func<ReleaseManifest, IReadOnlyList<string>> requiredCapabilities)
        {
            _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            _requiredCapabilities = requiredCapabilities ?? throw new ArgumentNullException(nameof(requiredCapabilities));
        }

        public System.Threading.Tasks.Task<string> CheckAsyncCore(ReleaseManifest candidate)
            => System.Threading.Tasks.Task.FromResult(Check(candidate));

        /// <summary>纯判定（L1 可测；与 UniTask 无关）。返回 null = 全部覆盖。</summary>
        public string Check(ReleaseManifest candidate)
        {
            if (candidate == null) return "候选为空";

            IReadOnlyList<string> required = _requiredCapabilities(candidate);
            if (required == null || required.Count == 0) return null;

            var missing = new List<string>();
            foreach (string c in required)
                if (!_capabilities.Has(c)) missing.Add(c);

            return missing.Count == 0
                ? null
                : $"候选要求的能力本包不具备（§10 须在执行前拒绝）：{string.Join(", ", missing)}";
        }

        public Cysharp.Threading.Tasks.UniTask<string> CheckAsync(
            ReleaseManifest candidate, System.Threading.CancellationToken ct = default)
            => Cysharp.Threading.Tasks.UniTask.FromResult(Check(candidate));
    }
}
