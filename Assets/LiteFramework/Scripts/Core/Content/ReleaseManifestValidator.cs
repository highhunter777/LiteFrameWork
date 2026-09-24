using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 候选被拒的原因（《热更与内容发布专项设计》§4"接受/拒绝候选与**稳定原因码**"）。
    /// 调用方据此决定：修复、提示更新、或直接放弃（不得靠反复重试绕过）。
    /// </summary>
    public enum ReleaseRejectReason
    {
        None = 0,

        // ---- 描述结构/预算（§6 路径与预算约束）----
        /// <summary>描述为空或 schemaVersion 不受支持。</summary>
        UnsupportedSchema,
        /// <summary>releaseId 缺失/含非法字符。</summary>
        InvalidReleaseId,
        /// <summary>文件路径越界、绝对路径、含 ".."、非法规范化或重复。</summary>
        InvalidPath,
        /// <summary>文件数/单文件大小/总字节超预算。</summary>
        BudgetExceeded,
        /// <summary>条目字段非法（长度负、摘要非 64 位小写 hex）。</summary>
        InvalidEntry,

        // ---- 信任（§6 签名/撤销/过期）----
        /// <summary>keyId 未登记或已撤销。</summary>
        UnknownOrRevokedKey,
        /// <summary>签名验证不通过。</summary>
        BadSignature,
        /// <summary>描述已过期。</summary>
        Expired,
        /// <summary>该发布已被安全撤销。</summary>
        Revoked,

        // ---- 版本（§6 反回退 / §5 兼容）----
        /// <summary>修订低于已确认修订（防重放旧清单）。</summary>
        RevisionRollback,
        /// <summary>平台/渠道不匹配。</summary>
        PlatformMismatch,
        /// <summary>Player 能力不满足候选要求的下限（App/Bridge/协议/Sim/schema/存档）。</summary>
        IncompatiblePlayer,
        /// <summary>该发布被标记为 LiveRefresh / 需要未证明的多版本并存能力——首版拒绝。</summary>
        UnsupportedEffectWindow,
    }

    /// <summary>校验结果（接受 = 无原因；拒绝 = 稳定原因码 + 可诊断说明）。</summary>
    public readonly struct ReleaseVerdict
    {
        public readonly bool Accepted;
        public readonly ReleaseRejectReason Reason;
        public readonly string Detail;

        private ReleaseVerdict(bool accepted, ReleaseRejectReason reason, string detail)
        {
            Accepted = accepted;
            Reason = reason;
            Detail = detail;
        }

        public static ReleaseVerdict Accept() => new ReleaseVerdict(true, ReleaseRejectReason.None, null);
        public static ReleaseVerdict Reject(ReleaseRejectReason reason, string detail)
            => new ReleaseVerdict(false, reason, detail);

        public override string ToString() => Accepted ? "接受" : $"拒绝({Reason}): {Detail}";
    }

    /// <summary>
    /// 运行时可接受的能力下限（《热更与内容发布专项设计》§5 版本元组里"当前 Player 实际具备"的一侧）。
    /// 由装配点从编译期常量/生成物填充——**不接受运行时可变来源**（否则兼容判定可被内容影响）。
    /// </summary>
    public sealed class PlayerCapabilities
    {
        public string AppVersion = "";
        public string Platform = "";
        public string Channel = "";
        public int BridgeApiVersion;
        public int ProtocolVersion;
        public int SimVersion;
        public int ConfigSchemaVersion;
        public int SaveSchemaVersion;
    }

    /// <summary>
    /// 描述校验预算（§7"清单/脚本容量：限制文件数、单文件/总字节"；§15"每个平台配置…没有目标包规模
    /// 与设备数据时不虚构固定毫秒或成功率"——故这些是**可配置上限**而非常量，装配点按平台给值）。
    ///
    /// 默认值取灰盒期保守量；真实平台预算在 G2/G4 用真实包与设备建立基线后收紧。
    /// </summary>
    public sealed class ReleaseBudget
    {
        /// <summary>最大文件数。</summary>
        public int MaxFileCount = 4096;

        /// <summary>单文件最大字节。</summary>
        public long MaxFileBytes = 512L * 1024 * 1024;

        /// <summary>候选总字节上限（§7"空间预检：计入候选、临时/解压峰值、保留版本及余量"由调用方据此计算）。</summary>
        public long MaxTotalBytes = 4L * 1024 * 1024 * 1024;

        /// <summary>路径段数上限（防深层嵌套）。</summary>
        public int MaxPathDepth = 16;

        /// <summary>单段路径长度上限。</summary>
        public int MaxPathSegmentLength = 128;
    }

    /// <summary>
    /// 发布描述校验（《热更与内容发布专项设计》§6 逐条落实）。
    ///
    /// **顺序即安全**（§6"先验证描述的结构/预算与签名，再依据可信描述计划下载"）：
    /// ① 结构/schema → ② releaseId → ③ 路径与预算 → ④ 条目字段 → ⑤ 过期 → ⑥ 撤销 → ⑦ 签名 → ⑧ 反回退 → ⑨ 平台 → ⑩ 兼容。
    /// **签名验证在路径/预算校验之后**：先拒绝明显畸形/超预算的输入（廉价且不涉及信任），
    /// 再花签名验证的算力；但**在依据描述做任何下载之前**——这是"再依据可信描述计划下载"的落点。
    ///
    /// 本类纯规则、零 IO（读文件/取字节由调用方），故 L1 全覆盖。
    /// </summary>
    public static class ReleaseManifestValidator
    {
        /// <summary>
        /// 校验描述并验证签名。<paramref name="signedBytes"/> = 描述文件的**原始字节**
        /// （签名对象；**不做换行归一化**——§5）。<paramref name="signature"/> = 解码后的签名字节。
        /// </summary>
        public static ReleaseVerdict Validate(
            ReleaseManifest manifest,
            byte[] signedBytes,
            byte[] signature,
            ISignatureVerifier verifier,
            PlayerCapabilities player,
            long confirmedRevision,
            ReleaseBudget budget = null,
            long nowUnix = 0)
        {
            budget = budget ?? new ReleaseBudget();

            // ① 结构
            if (manifest == null) return ReleaseVerdict.Reject(ReleaseRejectReason.UnsupportedSchema, "描述为空");
            if (manifest.SchemaVersion != ReleaseManifest.CurrentSchemaVersion)
                return ReleaseVerdict.Reject(ReleaseRejectReason.UnsupportedSchema,
                    $"schemaVersion={manifest.SchemaVersion}（支持 {ReleaseManifest.CurrentSchemaVersion}）");

            // ② releaseId
            var idVerdict = ValidateReleaseId(manifest.ReleaseId);
            if (!idVerdict.Accepted) return idVerdict;

            // ③ 路径与预算
            var files = manifest.Files ?? new List<ReleaseFileEntry>();
            if (files.Count == 0)
                return ReleaseVerdict.Reject(ReleaseRejectReason.InvalidEntry, "文件清单为空");
            if (files.Count > budget.MaxFileCount)
                return ReleaseVerdict.Reject(ReleaseRejectReason.BudgetExceeded,
                    $"文件数 {files.Count} 超上限 {budget.MaxFileCount}");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            long total = 0;
            foreach (var f in files)
            {
                var pathVerdict = ValidatePath(f, budget);
                if (!pathVerdict.Accepted) return pathVerdict;

                if (!seen.Add(f.Path))
                    return ReleaseVerdict.Reject(ReleaseRejectReason.InvalidPath, $"重复路径:{f.Path}");

                if (f.Length < 0)
                    return ReleaseVerdict.Reject(ReleaseRejectReason.InvalidEntry, $"长度非法:{f.Path}={f.Length}");
                if (f.Length > budget.MaxFileBytes)
                    return ReleaseVerdict.Reject(ReleaseRejectReason.BudgetExceeded,
                        $"单文件超上限:{f.Path}={f.Length}>{budget.MaxFileBytes}");

                if (!IsLowerHex64(f.Sha256))
                    return ReleaseVerdict.Reject(ReleaseRejectReason.InvalidEntry, $"摘要非 64 位小写 hex:{f.Path}");

                total += f.Length;
                if (total > budget.MaxTotalBytes)
                    return ReleaseVerdict.Reject(ReleaseRejectReason.BudgetExceeded,
                        $"总字节超上限:{total}>{budget.MaxTotalBytes}");
            }

            // ⑤ 过期（§6"元数据重放/过期"）
            if (manifest.ExpiresAtUnix > 0 && nowUnix > 0 && nowUnix > manifest.ExpiresAtUnix)
                return ReleaseVerdict.Reject(ReleaseRejectReason.Expired,
                    $"描述已过期:{manifest.ExpiresAtUnix} < now {nowUnix}");

            // ⑥ 安全撤销（§6"反回退校验与运营回退分别处理"；§12 回退前检查目标未被撤销）
            if (manifest.Revoked)
                return ReleaseVerdict.Reject(ReleaseRejectReason.Revoked, "该发布已被安全撤销");

            // ⑦ 签名（在计划下载之前——§6）
            if (verifier == null)
                return ReleaseVerdict.Reject(ReleaseRejectReason.UnknownOrRevokedKey, "无可用的验签器（keyId 未登记/已撤销）");
            if (signature == null || signature.Length == 0)
                return ReleaseVerdict.Reject(ReleaseRejectReason.BadSignature, "缺少签名");
            if (signedBytes == null || signedBytes.Length == 0)
                return ReleaseVerdict.Reject(ReleaseRejectReason.BadSignature, "缺少被签名的描述字节");
            if (!verifier.Verify(signedBytes, signature))
                return ReleaseVerdict.Reject(ReleaseRejectReason.BadSignature, "签名验证不通过");

            // ⑧ 反回退（§6"不接受攻击者重放旧清单"）
            if (manifest.Revision < confirmedRevision)
                return ReleaseVerdict.Reject(ReleaseRejectReason.RevisionRollback,
                    $"修订回退:{manifest.Revision} < 已确认 {confirmedRevision}");

            // ⑨ 平台/渠道
            if (player == null)
                return ReleaseVerdict.Reject(ReleaseRejectReason.PlatformMismatch, "缺少 Player 能力描述");
            if (!string.IsNullOrEmpty(manifest.Platform) &&
                !string.Equals(manifest.Platform, player.Platform, StringComparison.Ordinal))
                return ReleaseVerdict.Reject(ReleaseRejectReason.PlatformMismatch,
                    $"平台不符:{manifest.Platform} != {player.Platform}");
            if (!string.IsNullOrEmpty(manifest.Channel) &&
                !string.Equals(manifest.Channel, player.Channel, StringComparison.Ordinal))
                return ReleaseVerdict.Reject(ReleaseRejectReason.PlatformMismatch,
                    $"渠道不符:{manifest.Channel} != {player.Channel}");

            // ⑩ 兼容（候选声明的是**下限**；Player 低于下限即拒绝——不静默降级）
            var compat = manifest.Compatibility;
            if (compat != null)
            {
                if (compat.ProtocolVersion > player.ProtocolVersion)
                    return ReleaseVerdict.Reject(ReleaseRejectReason.IncompatiblePlayer,
                        $"协议版本不足:需 {compat.ProtocolVersion}，本机 {player.ProtocolVersion}");
                if (compat.SimVersion > player.SimVersion)
                    return ReleaseVerdict.Reject(ReleaseRejectReason.IncompatiblePlayer,
                        $"Sim 版本不足:需 {compat.SimVersion}，本机 {player.SimVersion}");
                if (compat.BridgeApiVersion > player.BridgeApiVersion)
                    return ReleaseVerdict.Reject(ReleaseRejectReason.IncompatiblePlayer,
                        $"Bridge 能力不足:需 {compat.BridgeApiVersion}，本机 {player.BridgeApiVersion}");
                if (compat.ConfigSchemaVersion > player.ConfigSchemaVersion)
                    return ReleaseVerdict.Reject(ReleaseRejectReason.IncompatiblePlayer,
                        $"配置 schema 不足:需 {compat.ConfigSchemaVersion}，本机 {player.ConfigSchemaVersion}");
                if (compat.SaveSchemaVersion > player.SaveSchemaVersion)
                    return ReleaseVerdict.Reject(ReleaseRejectReason.IncompatiblePlayer,
                        $"存档 schema 不足:需 {compat.SaveSchemaVersion}，本机 {player.SaveSchemaVersion}");

                // AppVersion 采用**精确匹配**（声明即要求相等）。
                // 不在此自造 semver 比较器（总设计 §22 精神）；"范围匹配"未实现——
                // 需要时引入受测比较器再放宽，**不得静默失效**（声明了却不生效比不声明更危险）。
                if (!string.IsNullOrEmpty(compat.AppVersion) &&
                    !string.Equals(compat.AppVersion, player.AppVersion, StringComparison.Ordinal))
                    return ReleaseVerdict.Reject(ReleaseRejectReason.IncompatiblePlayer,
                        $"App 版本不符:需 {compat.AppVersion}，本机 {player.AppVersion}");
            }

            return ReleaseVerdict.Accept();
        }

        /// <summary>生效窗口是否受本批支持（首版只有 NextLaunch 可冷启动激活；其余需安全窗口/局间协调器）。</summary>
        public static ReleaseVerdict ValidateEffectWindow(ReleaseManifest manifest)
        {
            if (manifest == null) return ReleaseVerdict.Reject(ReleaseRejectReason.UnsupportedSchema, "描述为空");
            if (manifest.EffectWindow != ReleaseEffectWindow.NextLaunch)
                return ReleaseVerdict.Reject(ReleaseRejectReason.UnsupportedEffectWindow,
                    $"生效窗口 {manifest.EffectWindow} 需安全窗口协调器（首版仅支持 NextLaunch 冷启动激活）");
            return ReleaseVerdict.Accept();
        }

        // ---- 内部 ----

        private static ReleaseVerdict ValidateReleaseId(string releaseId)
        {
            if (string.IsNullOrEmpty(releaseId))
                return ReleaseVerdict.Reject(ReleaseRejectReason.InvalidReleaseId, "releaseId 为空");
            if (releaseId.Length > 128)
                return ReleaseVerdict.Reject(ReleaseRejectReason.InvalidReleaseId, $"releaseId 过长({releaseId.Length})");

            foreach (char c in releaseId)
            {
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                          || c == '-' || c == '_' || c == '.';
                if (!ok)
                    return ReleaseVerdict.Reject(ReleaseRejectReason.InvalidReleaseId,
                        $"releaseId 含非法字符:'{c}'（只允许字母数字与 -_.）");
            }
            if (releaseId == "." || releaseId == "..")
                return ReleaseVerdict.Reject(ReleaseRejectReason.InvalidReleaseId, $"releaseId 非法:{releaseId}");
            return ReleaseVerdict.Accept();
        }

        /// <summary>
        /// 路径约束（§6"文件路径只能落在受控候选目录内，拒绝越界、重复、非法规范化路径和超预算长度/数量"）。
        /// 只接受**正斜杠分隔的相对路径**——反斜杠一律拒绝（避免 Windows 归一化歧义，
        /// §5"统一规范化"精神）；".."/"."/空段/前导斜杠/盘符/UNC 全部拒绝。
        /// </summary>
        private static ReleaseVerdict ValidatePath(ReleaseFileEntry f, ReleaseBudget budget)
        {
            if (f == null) return ReleaseVerdict.Reject(ReleaseRejectReason.InvalidEntry, "文件条目为空");
            string p = f.Path;
            if (string.IsNullOrEmpty(p))
                return ReleaseVerdict.Reject(ReleaseRejectReason.InvalidPath, "路径为空");

            if (p.IndexOf('\\') >= 0)
                return ReleaseVerdict.Reject(ReleaseRejectReason.InvalidPath, $"路径含反斜杠:{p}");
            if (p[0] == '/')
                return ReleaseVerdict.Reject(ReleaseRejectReason.InvalidPath, $"绝对路径:{p}");
            if (p.Length >= 2 && p[1] == ':')
                return ReleaseVerdict.Reject(ReleaseRejectReason.InvalidPath, $"含盘符:{p}");

            string[] segments = p.Split('/');
            if (segments.Length > budget.MaxPathDepth)
                return ReleaseVerdict.Reject(ReleaseRejectReason.BudgetExceeded,
                    $"路径层级 {segments.Length} 超上限 {budget.MaxPathDepth}:{p}");

            foreach (string seg in segments)
            {
                if (seg.Length == 0)
                    return ReleaseVerdict.Reject(ReleaseRejectReason.InvalidPath, $"空路径段:{p}");
                if (seg == "." || seg == "..")
                    return ReleaseVerdict.Reject(ReleaseRejectReason.InvalidPath, $"越界路径段:{p}");
                if (seg.Length > budget.MaxPathSegmentLength)
                    return ReleaseVerdict.Reject(ReleaseRejectReason.BudgetExceeded,
                        $"路径段过长({seg.Length}):{p}");
            }
            return ReleaseVerdict.Accept();
        }

        private static bool IsLowerHex64(string s)
        {
            if (s == null || s.Length != 64) return false;
            foreach (char c in s)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }
    }
}
